using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using GameLoop;
using CombatSystem;
using MapSystem;
using EventSystem;
using ItemSystem;
using ItemSystem.Shop;
using AutoTest.Ultra;

namespace AutoTest
{
    /// <summary>
    /// GameManager の公開APIだけを使い、キー入力なしで1ランを最後まで自動進行させる
    /// ヘッドレス・ドライバ。N回連続実行し、阻害要因を集計したログファイルを生成する。
    ///
    /// 行動方針: 前進貪欲 + 生存重視
    ///   - マップは常にボス方向(forward)へ進む。HP低下時は休憩/宝箱/ショップを優先。
    ///   - 戦闘は ExecuteFullCombat で即決。儀式は資源温存のため全拒否。
    ///
    /// ゲーム側コードは一切改変せず、Debug.Log("[GameManager] ...") を購読して
    /// 進行ナラティブを取得する。
    /// </summary>
    public partial class AutoRunner : MonoBehaviour
    {
        [Header("バッチ設定")]
        [Tooltip("バッチあたりのラン数。 自己学習(L1/L2)を信頼させるには 1000以上推奨。 200未満は L2 が自動スキップされる")]
        public int runCount = 1000;
        [Tooltip("自動周回モード: 0 or 1 で通常1バッチのみ。 2以上なら『1000ラン × N回』を連続実行し、 各バッチ間で L1/L2 自動学習が回る")]
        public int autoLoopBatches = 1;
        public bool autoStart = false;
        [Tooltip("バッチ中の Time.timeScale（演出を早送り）")]
        public float batchTimeScale = 50f;
        [Tooltip("1フレームあたりに実行する Step 回数。1=旧挙動、20-50で大幅高速化（ゲームロジックがCPUバウンドのため）")]
        public int stepsPerYield = 20;
        [Tooltip("1フレームに処理するラン数。結果を変えず、長大なバッチの描画待ちだけを減らす。")]
        public int runsPerYield = 20;
        [Tooltip("バッチ中 VSync を無効化しフレームレート上限を解除する（60fps→数百fps化で大幅高速化）")]
        public bool disableVSyncDuringBatch = true;
        [Tooltip("1ランあたりの最大ループ反復数。超過でDEADLOCK判定")]
        public int maxIterationsPerRun = 4000;
        [Tooltip("同一フェーズが進展なく続いた反復数の上限。超過でDEADLOCK判定")]
        public int stallLimit = 400;
        [Tooltip("1 ラン の実時間上限(秒)。超過で DEADLOCK 打ち切り。0 で無効。"
               + "反復上限/ストール検出では捕まらない『動き続ける固着』の最終防衛線")]
        public float runWatchdogSeconds = 20f;

        // ============================================================
        //  外部観測用の状態 (2026-08-10)
        //
        //  固着を追うのに反射で private を覗いていたが、 読んだ値どうしが矛盾して
        //  原因究明が空転した (Run==null なのに phase=ShopVisit 等)。
        //  **「いま何をしているか」を一意に読める公開状態**を持たせる。
        //  推測ではなくこれを見ること。
        // ============================================================

        /// <summary>RunOne の本体ループが 1 周するたびに増える。 **止まっていれば
        /// コルーチンが本体ループへ戻っていない** ＝ 入れ子のどこかで固着している。</summary>
        [System.NonSerialized] public long heartbeat;
        /// <summary>いま走っているスイープのアーム番号 (1 始まり) と総数。</summary>
        [System.NonSerialized] public int curArmIndex, curArmCount;
        /// <summary>そのアームの何ラン目か (1 始まり) と総数。</summary>
        [System.NonSerialized] public int curRunInArm, curRunsPerArm;
        /// <summary>いま走っているアームの表示名。</summary>
        [System.NonSerialized] public string curArmLabel = "";
        /// <summary>本体ループが最後に見たフェーズ。</summary>
        [System.NonSerialized] public string lastPhaseSeen = "";
        [System.NonSerialized] public bool showRunProgressGui;
        private readonly List<string> _screenRunLog = new List<string>(20);
        private int _screenCompletedRuns;
        private int _screenTotalRuns;
        private float _screenSweepStartRt;
        private float _screenLastCompletionRt;
        private GUIStyle _screenPanelStyle;
        private GUIStyle _screenTextStyle;
        private GUIStyle _screenAlertStyle;
        /// <summary>_records が Clear された回数。 **1 バッチで 1 回を超えたら異常**。</summary>
        [System.NonSerialized] public int recordsClearedCount;
        [Tooltip("詳細ログ(全ランのナラティブ)を書き出す。 1バッチで 30MB+ に膨らむため自動周回時はOFF推奨")]
        public bool writeDetailLog = false;
        [Tooltip("全runの巨大JSONLを保存する。集計だけを見る高速バッチではOFFにできる。")]
        public bool writeRunsJsonl = true;
        [Tooltip("詳細ログの1ランあたり最大行数。超過時は古い行を捨て末尾(決定的な終端)を必ず保持")]
        public int detailMaxLinesPerRun = 5000;
        [Tooltip("バッチ中 Debug.Log を完全抑止 + StackTrace を無効化 (Editor RAM爆発防止)。 10Kラン×400Log×StackTrace 5KB = 20GB+ の蓄積を阻止")]
        public bool suppressLogsDuringBatch = true;
        [Tooltip("バッチ終了時に Editor コンソールをクリア + GC.Collect (蓄積したログを即解放)")]
        public bool clearConsoleAfterBatch = true;

        /// <summary>ADR-0010 Verification ①: **技量帯を実測するための 2 実装**。
        ///
        /// 同一シードで両者を回し、 クリア率の差を McNemar で見る。 その差が
        /// 「役システムがプレイヤーに開けた伸びしろ」の実測値になる ── 差が出ないなら
        /// 役は飾りで、 難易度の梯子は結局デバフ設計に戻る (ADR-0010 を出した理由そのもの)。</summary>
        public enum WiringSkill
        {
            /// <summary>素朴: 出目降順の本数分割のみ。 リロールも役の発動もしない ＝ ADR-0010 以前と同じ打ち方。</summary>
            Naive,
            /// <summary>最適: 3^5 の割り当てを全列挙し役の価値も効用へ入れる。 リロールと役も使う。</summary>
            Optimal,
            /// <summary>天井: 戦闘決着まで前向きに読む (<see cref="SuperCombatAI"/>)。
            ///
            /// Optimal との差が「1 ターン貪欲では届かない読みの深さ」＝ **技量帯の上半分**。
            /// **未来の出目は読まない** ── ロールアウトは専用乱数で、 GameRng を消費しない。
            /// 1 ターンあたりの計算量が 2 桁上がるので、 スイープは短めのラン数で。</summary>
            Super,
            /// <summary>厳密: 近似を入れない expectimax (<see cref="ExactCombatAI"/>)。
            ///
            /// <para>**天井の位置を測るための参照実装**で、 バランス測定には使わない。
            /// Super との差が「Super が理論値からどれだけ遠いか」になる。
            /// 偶然ノードを全列挙するので**乱数を一切使わず**、 同じ盤面には同じ手を返す。
            /// 速度は桁で落ちる ── ラン数は 3〜50 程度で回すこと。</para>
            ///
            /// <para><b>末尾に足すこと。</b> 既存値の番号を動かすとセーブ/EditorPrefs が壊れる。</para></summary>
            Exact,
            /// <summary>Production worker が実ゲームを使って Super の基準手を改善する方策。
            /// 未登録・build不一致・checkpoint不完全では開始せず、実行中の個別判断失敗時だけ
            /// 同じ状態で先に確保した Super の基準手へ戻る。</summary>
            Ultra,
        }
        [Tooltip("配線方策の技量。同一シードで Naive/Optimal/Super を比較すると技量帯が測れる")]
        public WiringSkill wiringSkill = WiringSkill.Optimal;
        [NonSerialized] private WiringSkill _ultraSelectedBaseSkill = WiringSkill.Super;

        /// <summary>
        /// Ultra が候補を採用できない判断で使う production baseline。
        /// 表示・記録上の技量は <see cref="wiringSkill"/> の Ultra を維持する。
        /// </summary>
        public WiringSkill EffectiveWiringSkill
            => wiringSkill == WiringSkill.Ultra ? _ultraSelectedBaseSkill : wiringSkill;

        [Header("通常AI・並列評価")]
        [Tooltip("【実験用】Optimal AI の配線候補評価を CPU 並列化する。長時間バッチで停止事例があるため既定OFF")]
        public bool parallelizeOptimalWiring = false;
        [Tooltip("この候補数以上で並列化する。小さい探索はスレッド起動コストを避けて直列実行")]
        public int optimalWiringParallelMinCandidates = 128;
        [Tooltip("並列ワーカー上限。0 は min(8, 論理CPU数-1)。小候補での過剰並列を防ぐ")]
        public int optimalWiringMaxParallelism = 0;
        [NonSerialized] public long optimalWiringDecisions;
        [NonSerialized] public long optimalWiringCandidatesEvaluated;

        /// <summary>技量帯の天井を担う先読み方策。 <see cref="WiringSkill.Super"/> のときだけ使う。
        /// レバー (ロールアウト本数・候補数・深さ) はここから触る。</summary>
        public SuperCombatAI superAI = new SuperCombatAI();

        /// <summary>厳密方策 (<see cref="WiringSkill.Exact"/> のときだけ使う)。
        /// 天井の位置を測る参照実装で、 通常の測定には使わない。</summary>
        public ExactCombatAI exactAI = new ExactCombatAI();

        [NonSerialized] private IUltraAutoRunController _ultraController;
        [NonSerialized] private UltraAutoRunProfile _ultraProfile;
        [NonSerialized] private UltraProgressSession _ultraProgressSession;
        [NonSerialized] private bool _ultraBatchActive;

        [Header("天井AI・最大難度ルーチン")]
        [Tooltip("満点難度のSuperだけ、6層固定ショップで所持金を高分散な再入荷へ全投入する")]
        public bool superHighDifficultyTailMode = true;
        [Tooltip("この実効挑戦スコア以上で専用ルーチンを起動")]
        public int superHighDifficultyMinScore = 50;
        [Tooltip("満点難度の6層ボスだけ、同一シード実測で優位だった通常AIの戦闘方策を使う。7層ではSuper先読みに戻す")]
        public bool superLayer6OptimalCombatRoutine = true;

        /// <summary>**7層ヴェスカでも Optimal の戦闘方策を使う** (既定 OFF)。
        ///
        /// <para><b>一度採用し、同日に撤回した。</b> これは <c>StaticValue</c> の飽和という
        /// 実際のバグへの**対症療法**だった。 根本 (<see cref="SuperCombatAI.ttdCapTurns"/>) を
        /// 直したら Super の方が強くなり (74.9% 対 73.3%)、 併用しても上乗せが無い
        /// (74.9% → 73.2% / p=0.0821)。 **原因を直したので逃がす必要がなくなった。**
        /// フラグは対照アーム用に残す。</para>
        ///
        /// <para>採用時の実測 (参考):
        ///
        /// <para>採用の実測 (パーツ有り・同一シード・各1000ラン):
        /// <b>0pt 遺物なし 31.1% → 33.9% (p=0.0056) / 0pt 遺物あり 70.9% → 73.3% (p=0.0250)</b>
        /// ── **両条件とも有意で同じ向き**。 同日に階層DP は符号反転で、
        /// `chargeRerollGain` はパーツ抑止の artifact で撤回しており、
        /// 2 条件そろって効いたのはこれだけ。</para>
        ///
        /// <para>2026-08-22 実測: 同一シードで両 AI が 7層へ到達した 379 ラン に限ると
        /// <b>Optimal だけクリア 71 / Super だけクリア 41 (p=0.0059)</b>。
        /// 6F 突破時点の状態 (HP率 0.888 vs 0.878 / パッシブ 46.95 vs 46.71 / 武器Tier 3.855 vs 3.842)
        /// は Super が僅かに**上**なので、 「弱いランを拾って着いている」では説明できない。</para>
        ///
        /// <para>心当たり: <c>SuperCombatAI.StaticValue</c> は期待ブロックが敵攻撃を上回ると
        /// <c>ttd=999</c> → 勝率 p が 1 に飽和し、 **敵HP が評価から消える**
        /// (飽和センサスで同点候補の 19.5%)。 8 ターンの通常戦なら「いま安全」はほぼ正しいが、
        /// 7層は 4 連戦 40 ターン級でエスカレーションが ×2.3 まで伸びるので、
        /// そこで飽和させるのが最も危ない。</para></summary>
        [NonSerialized] public bool superLayer7OptimalCombatRoutine;

        private bool IsSuperTailMode
            => superHighDifficultyTailMode
            && EffectiveWiringSkill == WiringSkill.Super
            && MetaProgression.MetaDebuffApplicator.Score >= superHighDifficultyMinScore;

        /// <summary>余剰リロール後に「1 個買える」ために残す額。 これを割るなら回さない。</summary>
        public int SurplusBuyReserve = 30;

        // ===== リロール停止則 (2026-09-17) =====
        /// <summary>フェーズ2/2b を 1 本の停止則に置き換えるか。 <b>既定 false = 旧規則</b>
        /// (A/B で測ってから畳む)。</summary>
        public static bool RerollStopRule = false;
        /// <summary>[A/B] リロール直前にフェーズ3 の購入を済ませる (<c>BuyPhase3Items</c>)。 既定 false。
        ///
        /// <para><b>棄却 (2026-09-18、Balanced 10,000 ラン)。</b> 上限2回で 38.13% → 19.25%
        /// (−18.88pt, z=−38.3)。 捨てる棚は 0 になったが、 フェーズ3 がゲート (25G) まで金を
        /// 使い切るので余剰リロールが止まり (8.40 → 5.06 回)、 優先アイテム取得が 28.70 → 23.96 に落ちた。
        /// しかも <b>捨てる棚が 0 でも 3 回目のリロールは損</b> (−1.26pt, z=−4.05) ── 3 回目が損な理由は
        /// 棚を捨てることではない。 「リロールが購入対象を 5 品捨てている」は事実だが原因ではなかった。</para></summary>
        public static bool Phase3BeforeReroll = false;
        /// <summary><b>1G の band 価値。</b> ランダム付与 ITT (163 品・100,000 ラン・2026-09-17) の
        /// 金の処置係数: 10G → 0.0823 / 25G → 0.1720 band (1G あたり 0.0082 / 0.0069)。
        /// pt 側は用量間で 51% ずれたので、 安定している band 側を使う。
        /// <b>序列の単位 (RankMap = band) と揃っていること</b>が前提 ── RawPt に切り替えたら引き直す。</summary>
        public static float GoldBandRate = 0.0075f;
        /// <summary>1 回の棚替えで買える純益 (band) の<b>事前値</b>。 ラン内の実績で上書きされていく。
        /// 実績が 0 のとき、 これが「何 G のリロールまで回すか」を決める:
        /// 0.15 / 0.0075 = 20G ＝ 3 回目の手前で止まる (上限掃引の最適 = 店あたり 2 回 と同じ位置)。</summary>
        public static float RerollShelfValue = 0.15f;
        /// <summary>事前値を何件ぶんの観測として扱うか。 小さいほどラン内実績に早く寄る。</summary>
        public static float RerollPriorWeight = 2f;
        /// <summary>回した後に最低限残す額 (パッシブ中央価格 8G)。 買えない棚替えは無意味。</summary>
        public static int RerollBuyFloor = 8;
        /// <summary>ラン内の棚替え実績 (純益の和 / 回数)。 ラン開始で 0 に戻す。</summary>
        [NonSerialized] private float _rerollYieldSum;
        [NonSerialized] private int _rerollYieldN;

        /// <summary>[計装] 停止則の判断。 回した/止めた の件数と、 そのときの見込み・費用の和。</summary>
        public static long RuleGo, RuleStop;
        public static double RuleGainSum, RuleCostSum;
        public static void ResetRerollRuleStats() { RuleGo = RuleStop = 0; RuleGainSum = RuleCostSum = 0; }
        private static void NoteRerollDecision(float gain, float cost)
        {
            if (gain > cost) RuleGo++; else RuleStop++;
            RuleGainSum += gain; RuleCostSum += cost;
        }

        /// <summary>7層の最終ショップで「即買いする」消費アイテムの Tier 下限 (2026-08-16)。
        /// これ未満は、 再入荷を回せる限り見送って上位 Tier を探す。
        /// 消費は T1〜4 の一様抽選 (ShopManager.Generate) なので、 3 以上は 1 枠あたり 50%。
        /// 消費枠は 3 なので、 1 回の陳列で 1 枠以上が T3+ になる確率は 87.5%。</summary>
        public const int HighConsumableTier = 3;

        /// <summary>デバフ非適用時にも「1 端子へ N 本以上」を避ける閾値。 0 で無効。
        /// 〈不器用〉が BOT を**強くしていた**実測 (+6.0/+6.2pt・p&lt;0.001) を素の方策へ取り込む。</summary>
        /// <summary>メタ恒久進行の扱い方。</summary>
        public enum MetaPattern
        {
            /// <summary>臆病パターン: メタ進行を全リセット。パッシブボーナス0でバランス計測。</summary>
            Cowardly,
            /// <summary>全有効化パターン: トラック全段解放。メタバフ込みの上限プレイ計測。</summary>
            FullProgression,
            /// <summary>保存済み状態のまま手を付けない（実プレイヤーの進行データを使う）。</summary>
            Untouched,
        }

        // ============================================================
        //  2026-07-28 整理: 散らばっていたトグルを 3 グループへ統合。
        //    ① メタバフ   … 3 択・排他
        //    ② アイテム選択 … 2 択・排他
        //    ③ 学習        … 独立トグル (同時選択可・単独可)
        //  旧 metaProfile / metaPattern / learningMode / useBuildPersonas は
        //  下の派生プロパティが供給するので、 インスペクタからは消えている。
        // ============================================================

        /// <summary>メタバフ(整備パネル v6・36pt)の使い方。</summary>
        public enum MetaBuffMode
        {
            /// <summary>ビルド軸を使用: <see cref="metaBuildAxis"/> の配分で回す。</summary>
            BuildFocused,
            /// <summary>標準を使用: Balanced 配分。 **ボス調整の基準はこれ**。</summary>
            Standard,
            /// <summary>オフ: 0pt。 メタ未取得の新規プレイヤー＝バランスの床。</summary>
            Off,
        }

        /// <summary>BOT の所持品選択の判断軸。</summary>
        public enum ItemPickMode
        {
            /// <summary>ビルド軸: ペルソナを抽選し、 軸に沿ったアイテムを優先する。</summary>
            BuildFocused,
            /// <summary>Tier 軸: 学習済み Tier 表のスコア順に素直に取る (従来動作)。</summary>
            TierBased,
        }

        [Header("① メタバフ (排他)")]
        [Tooltip("① メタバフの使い方 (排他): BuildFocused=下の軸配分 / Standard=Balanced / Off=0pt")]
        public MetaBuffMode metaBuffMode = MetaBuffMode.Standard;

        [Tooltip("metaBuffMode = BuildFocused のときに適用する 36pt 配分")]
        public MetaAllocationPresets.Preset metaBuildAxis = MetaAllocationPresets.Preset.Offense;

        [Tooltip("【一斉走査】BuildFocused 時、 単一軸ではなく全軸をラン単位でラウンドロビンし、 軸別に成績を比較する")]
        public bool sweepAllMetaAxes = false;

        /// <summary>一斉走査の対象軸 (None=0pt は比較対象外なので除く)。</summary>
        public static readonly MetaAllocationPresets.Preset[] SweepAxes =
        {
            MetaAllocationPresets.Preset.Balanced,
            MetaAllocationPresets.Preset.Offense,
            MetaAllocationPresets.Preset.Defense,
            MetaAllocationPresets.Preset.Economy,
            MetaAllocationPresets.Preset.SparkBuild,
        };

        /// <summary>このランで実際に適用された配分 (集計キー)。</summary>
        private MetaAllocationPresets.Preset _currentAxis = MetaAllocationPresets.Preset.Balanced;

        [Header("①-b 周回モード (遺物 §15-5)")]
        [Tooltip("周回モード: ペルソナごとに独立したラインを走らせ、 ランのたびに遺物を更新し、 " +
                 "成績に余裕があれば難易度を上げる。 遺物と難易度のループが収束するかを測る")]
        public bool ascensionMode = false;

        [Tooltip("周回モードの難易度ポリシー。 Fixed は難易度を上げない対照群")]
        public AscensionLoop.Policy ascensionPolicy = AscensionLoop.Policy.Standard;

        [Tooltip("1 ペルソナあたりのラン数。 10 ペルソナぶん走るので総ラン数はこの 10 倍")]
        public int ascensionRunsPerPersona = 300;

        [Tooltip("成績を見る窓幅 (ラン数)。 短いと分散で誤判定して難易度が暴れる")]
        public int ascensionWindow = 20;

        /// <summary>周回モードで走らせるペルソナ。 RawTier は中立の対照群として含める。</summary>
        public static readonly BuildPersona[] AscensionPersonas =
        {
            BuildPersona.RawTier, BuildPersona.Standard, BuildPersona.Crit,   BuildPersona.Bleed,
            BuildPersona.Rinkai,  BuildPersona.Poison,   BuildPersona.Bludgeon,
            BuildPersona.Charge,  BuildPersona.Shield,   BuildPersona.Berserk,
        };

        /// <summary>現在走っているライン。 null なら周回モードではない。</summary>
        private AscensionLoop _ascension;
        /// <summary>true の間、 RunOne はペルソナを抽選し直さない。
        /// 周回モードは 1 ライン中ペルソナを固定する必要があるため
        /// （遺物がそのペルソナの重みで蓄積するので、 途中で入れ替わると効用比較が壊れる）。</summary>
        private bool _ascensionPersonaLock;
        /// <summary>ライン別の推移記録。 [ペルソナ][ラン] = (挑戦スコア, 効用, 到達層)。</summary>
        private readonly List<AscensionTrace> _ascensionTraces = new List<AscensionTrace>();

        /// <summary>1 ラインぶんの推移。 レポート用。</summary>
        public class AscensionTrace
        {
            public BuildPersona persona;
            public List<int>   score  = new List<int>();   // 挑戦スコア
            public List<float> util   = new List<float>(); // 遺物の効用
            public List<int>   floor  = new List<int>();   // 到達層
            public List<bool>  win    = new List<bool>();  // 5層クリア以上か
            public int swaps, upCount, downCount;
            public string finalRelic = "";
        }

        [Tooltip("メタデバフ Lv1-10 を全ON (最高難易度モード)。 上の 3 択とは独立")]
        public bool enableAllDebuffs = false;

        /// <summary>挑戦デバフ構成の指定 (docs/GAME.md §15-2)。 空なら無効。
        /// 書式は <c>軸名:Tier</c> をカンマ区切り、 T4 は <c>T4:名前</c>。
        /// 例: <c>練度不足:3,俊敏:2,T4:暗夜</c>
        /// <see cref="enableAllDebuffs"/> より優先される。 EditorPref "AutoRun.ChallengeSpec" から入る。</summary>
        public string challengeSpec = "";

        [Header("④ 決定論シード (再現性)")]
        [Tooltip("16 桁の 10 進シード。 空なら毎回ランダム (従来動作)。 同じ値なら同じ 1 万通りのランが再現され、 変更前後で同じシードを使えば対応のある比較になる (シナリオ差のノイズが消える)。")]
        public string masterSeed = "";

        [Header("② アイテム選択 (排他)")]
        [Tooltip("② アイテム選択の判断軸 (排他): BuildFocused=ビルド軸ペルソナ / TierBased=Tier表準拠")]
        public ItemPickMode itemPickMode = ItemPickMode.TierBased;

        [Tooltip("BuildFocused 時、 Tier表信奉ペルソナを混ぜる割合 (0=全ラン軸ビルド / 1=全ラン Tier)")]
        [Range(0f, 1f)] public float rawTierRatio = 0.5f;
        [Tooltip("ペルソナ抽選の乱数シード (0 = 時刻ベース)")]
        public int personaSeed = 0;

        [Header("③ 学習 (同時選択可・単独可)")]

        [Tooltip("ボス難易度オートチューナーを動かす (§13-3)")]
        public bool tuneBosses = false;
        [Tooltip("Tier表 (item_stats / regression / BALANCE_TIER_LIST.md) を更新する")]
        public bool learnTier = false;
        [Tooltip("BOT の AI ルーチン (policy / event_stats) を成長させる")]
        public bool learnBotAi = false;

        // ---- 派生 (旧フィールド名の供給元。 消費側は書き換えずに済む) ----

        /// <summary>Tier表を更新するか。</summary>
        public bool UpdatesTier => learnTier;
        /// <summary>AIルーチンを成長させるか。</summary>
        public bool UpdatesAi => learnBotAi;
        /// <summary>ボス難易度オートチューナーを動かすか。</summary>
        public bool BossAutoTune => tuneBosses;
        /// <summary>ビルド軸ペルソナを使うか。</summary>
        public bool useBuildPersonas => itemPickMode == ItemPickMode.BuildFocused;

        /// <summary>メタ恒久進行の扱い。 metaBuffMode から決まる。</summary>
        public MetaPattern metaPattern
            => metaBuffMode == MetaBuffMode.Off ? MetaPattern.Cowardly : MetaPattern.FullProgression;

        /// <summary>実際に適用する 36pt 配分。 Standard は Balanced 固定。
        /// BuildFocused かつ一斉走査中は、 ラン単位でラウンドロビンした軸 (<see cref="_currentAxis"/>)。</summary>
        public MetaAllocationPresets.Preset metaAllocation
            => metaBuffMode == MetaBuffMode.BuildFocused
                   ? (sweepAllMetaAxes ? _currentAxis : metaBuildAxis)
             : metaBuffMode == MetaBuffMode.Standard ? MetaAllocationPresets.Preset.Balanced
             :                                         MetaAllocationPresets.Preset.None;

        /// <summary>学習データの分離キー。 メタバフ/デバフの有無から導出する。</summary>
        public MetaProfile metaProfile
            => metaBuffMode == MetaBuffMode.Off ? MetaProfile.BuffOff_DebuffOff
             : enableAllDebuffs                 ? MetaProfile.BuffOn_DebuffOn
             :                                    MetaProfile.BuffOn_DebuffOff;

        [Header("ADR-0009 相互攻撃パイプライン検証")]
        [Tooltip("true でバッチを ADR-0009 相互攻撃パイプラインで実行 (新旧比較用)。配線は交換レート自動ポリシー")]
        public bool useMutualAttackPipeline = false;

        /// <summary>5層裏ボス(シュヴァリエ)を遮断するか。 **既定 true**。
        ///
        /// <para>裏ボスはレイピア所持という BOT のアイテム運で出現し、 引いたランだけ 5層の難度が
        /// 跳ね上がる。 バランス測定では分散源=ノイズにしかならないので既定で切る。</para>
        ///
        /// <para><b>2026-08-15 に既定へ格上げした。</b> 基準値測定・固定難易度スイープ・挑戦軸スイープ・
        /// AI比較は各々のコルーチン内で個別に <c>SuppressLayer5HiddenBoss = true</c> を立てていたが、
        /// 素の <c>Run N runs</c> だけが漏れていた。 その結果 1000 ラン中 385 ランが裏ボスに置換され、
        /// 致命 177 件 (全死因 2 位) を生みながら「5層が硬い」という誤った読みの材料になっていた。
        /// 個別に <c>false</c> を必要とするのは遺物スイープの非基準アームだけで、
        /// そちらは <see cref="Begin"/> の後に自分で書き戻す。</para></summary>
        public bool suppressLayer5HiddenBoss = true;

        /// <summary>軽減無視ダメージをシールドが肩代わりするか (既定 true = 2026-08-15 の仕様)。
        /// **false は対照群専用**。 <see cref="ItemSystem.PassiveSkills.CombatContext.ShieldAbsorbsUnmitigable"/> を見ること。</summary>
        public bool shieldAbsorbsUnmitigable = true;

        // ラン内で選ばれたペルソナ (RunOne 冒頭で抽選)
        private BuildPersona _currentPersona = BuildPersona.RawTier;
        private System.Random _personaRng;

        /// <summary>現在ラン中のペルソナへスコア加点 (0 = RawTier or マッチなし)。
        /// LearnedPriorityProvider.Score() の結果に加算して使う。</summary>
        private int PersonaBonus(string itemId)
        {
            if (!useBuildPersonas || _currentPersona == BuildPersona.RawTier) return 0;
            return BuildPersonaProfiles.ScoreBonusForItem(_currentPersona, itemId);
        }

        [Header("実行後")]
        [Tooltip("バッチ完了後にPlayModeを抜ける(Editorメニュー起動時)")]
        public bool exitPlayModeWhenDone = false;

        [Header("5Fボス勝率スイープ (検証モード)")]
        [Tooltip("true で通常バッチの代わりに『実ラン採取ビルド × 全武器×ダイス』の5Fボス勝率スイープを実行")]
        public bool simBoss5Sweep = false;
        [Tooltip("対象ボスのフロア(既定5)")]
        public int simBossFloor = 5;
        [Tooltip("実ランから採取する『5F到達時ビルド』の数。武器・ダイス以外(パッシブ/強化段階)の土台になる。HPは simBaseHP で固定")]
        public int simSampleBuilds = 10;
        [Tooltip("各(武器×ダイス)組み合わせの試行回数")]
        public int simTrialsPerCombo = 300;
        [Tooltip("戦闘開始HP(固定)。採取ビルドの現在HPは使わず、この値で統一して武器×ダイスを純粋比較する")]
        public int simBaseHP = 50;
        [Tooltip("スイープ対象武器(種別+Tier＋ユニーク)。存在しないIDは自動スキップ")]
        public string[] simWeapons = {
            "銀の長剣","デュランダル","血塗りの戦斧","血帝廻天","処刑人の曲刀","ノクタリア",
            "聖騎士の盾","ドーンブリンガー",
            // ユニーク/特殊武器（非進行）
            "竜閃"
        };
        [Tooltip("スイープ対象ダイス。存在しないIDは自動スキップ")]
        public string[] simDice = {
            "dice_wood","dice_bone","dice_copper","dice_iron","dice_biased",
            "dice_gem","dice_flame","dice_stable","dice_twinsnake",
            "dice_star","dice_destiny","dice_greed","dice_moroha","dice_perfection"
        };

        /// <summary>**[廃止 2026-10-28] 何も読まない。** 旧 Λ層の撤退条件 (周回マス数)。
        /// Λ層は廃止したが、 Ultra の方策仕様 (<c>UltraPortfolioPolicySpec.lambdaFarmTiles</c>) が
        /// 候補集合のハッシュと許可プロパティに含めて運んでいる値なので、 ワーカーとの取り決めを変えないよう残す。
        /// 仕様側を作り直すときに一緒に消すこと。 Λ のファーム量スイープと lambdaLv3Risk は同日削除。</summary>
        [HideInInspector] public int lambdaFarmTiles = 6;

        [Header("固定難易度スイープ")]
        [Tooltip("true で 挑戦スコアを固定値ごとに振り、最良遺物で 7 層クリアが成立するかを測る")]
        public bool challengeFixedSweep = false;
        [Header("Item Tier 難易度別更新")]
        [Tooltip("空なら通常実行。指定時は各スコアを Standard ペルソナ・通常AIで独立学習する")]
        public int[] tierCalibrationScores = Array.Empty<int>();
        public int tierCalibrationRuns = 1000;
        public int tierCalibrationBatches = 1;
        [Tooltip("任意。scoresと同数なら難易度ごとのセット数を上書きする")]
        public int[] tierCalibrationBatchCounts = Array.Empty<int>();
        private int _activeTierCalibrationScore = -1;
        private bool _tierCalibrationLogsWritten;
        private string _tierCalibrationLastDir;

        private bool IsTierCalibration => tierCalibrationScores != null && tierCalibrationScores.Length > 0;
        /// <summary>このバッチの学習の書き込み先。
        ///
        /// <para>Tier 較正中は隔離領域 (`tier_score_*`)。 それ以外は
        /// **挑戦スコア帯ごとの BOT 学習ルート** (`bot_band_*`) へ積む ── 0pt で無双できる品と
        /// 高難易度で要る品は違うので、 混ぜるとラン数の多い 0pt が序列を支配する。</para>
        ///
        /// <para><b>読み書きで同じ帯を使うこと。</b> ここと
        /// <see cref="LearnedPriorityProvider.SwitchToChallengeScore"/> がずれると、
        /// 「A の序列で買って B に記録する」という無意味な学習になる。</para></summary>
        private string TierLearningRoot => _activeTierCalibrationScore >= 0
            ? MetaProfileHelper.TierLearningRoot(_activeTierCalibrationScore)
            : MetaProfileHelper.BotLearningRoot(CurrentChallengeScore());

        /// <summary>いまの実効挑戦スコア。 解決済みキャッシュを読む
        /// (ロードアウトを書き換えても InvalidateChallenge しなければ古い値が残るため)。</summary>
        private static int CurrentChallengeScore()
        {
            var st = MetaProgression.MetaProgressManager.Instance?.State;
            return st?.Challenge != null ? st.Challenge.Score : 0;
        }
        private string TierOutputSuffix => _activeTierCalibrationScore >= 0
            ? $"{MetaProfileHelper.CurrentSuffix}_score{_activeTierCalibrationScore}"
            : MetaProfileHelper.CurrentSuffix;
        [Tooltip("測る挑戦スコア。 30 = 全軸最高 Tier / 50 = T4 込みの満点")]
        // V8 の階段測定。 交互配置による汚染検査は 2026-08-04 に合格済み
        // (0pt 3回が 47.7/45.3/48.3%、 5pt 3回が 14.7/14.7/19.0% で再現) なので、
        // 以後は昇順で難度カーブそのものを見る。
        // 限界到達点の走査 (2026-08-05)。 基準を作り直した (遺物なし 7層クリア 22.6%) ので
        // 壁の位置が動いている。 0/10/20/30/40/50 では粗いため 5pt 刻みで詰める。
        // 40 は構成不能で 42 に丸まる (基礎満点 30 + T4 が 4pt 刻みのため)。
        // 急落帯 (20→30pt) だけを見るための最小構成。 0 は比較の錨。
        // 全域の走査が要るときだけ { 0,5,10,15,20,25,30,35,42,50 } に戻す。
        // 40 は構成不能 (基礎満点30 + T4 が 4pt 刻みなので 34/38/42/46/50)。 実測値は 38 or 42 に丸まる。
        // 2026-08-09: 敵会心バグ (§24) の修正で全体の被ダメが下がり、 旧測定値が全部無効になった。
        //   低難度側も含めて階段を引き直すため 0〜50 の 6 点で測る。
        public int[] challengeSweepScores = { 0, 10, 20, 30, 40, 50 };
        [Tooltip("1 段階あたりのラン数")]
        public int challengeSweepRuns = 300;
        [Tooltip("軸単独試験と組み合わせ試験も回す。 汚染バグ追跡用の足場で、 ラン数が 2.25 倍になる")]
        public bool challengeSweepDiagnostics = false;
        [Tooltip("**計測専用**: 5層撃破時に〈ブレイドダンス〉を強制付与する。"
               + "剣の舞4枚完成で高難易度を通せるかの切り分け用")]
        public bool challengeSweepGrantBladeDance = false;
        private string _challengeSweepReport;
        /// <summary>次ランに流し込む固定挑戦構成。 ResetAll 直後に State へ入れる。</summary>
        private MetaProgression.ChallengeLoadout _pendingChallengeLoadout;

        [Header("遺物プリセットスイープ")]
        [Tooltip("true で ペルソナ/挑戦スコアを固定したまま遺物プリセットだけを振り、深度が動くかを測る")]
        public bool relicPresetSweep = false;
        [Tooltip("1 プリセットあたりのラン数")]
        public int relicSweepRuns = 300;
        [Tooltip("遺物なし(None)だけを回す。 バランス基準値を速く出す調整ループ用")]
        public bool relicSweepBaselineOnly = false;

        /// <summary>技量帯の実測 (ADR-0010 Verification ①)。 **配線方策だけを振る**。
        /// 条件は基準値測定と同じ (遺物なし・挑戦0pt・ペルソナ Standard・5層裏ボス遮断)。</summary>
        public bool wiringSkillCompare = false;
        public int wiringSkillCompareRuns = 300;
        /// <summary>比較する挑戦スコア。 0 なら基準値条件 (遺物なし)。
        /// 1 以上なら固定難易度スイープと同条件 (遺物 TheoreticalBestCursed) へ切り替える ──
        /// Verification ③ (30pt で 7層クリア 10% 前後) を方策別に測るため。</summary>
        /// <summary>挑戦 単軸スイープを**遺物なし**で回す。 段の値付けは基準条件で測るのが筋。</summary>
        public bool challengeAxisSweepNoRelic = false;
        /// <summary>この軸だけを回す (空なら全軸)。 <see cref="MetaProgression.ChallengeAxis"/> の名前で指定。
        /// 変更した軸だけ測り直すとき用 ── 全 23 段を回すと 1000 ラン で 40 分かかる。</summary>
        public string[] challengeAxisSweepAxisNames;
        /// <summary>T4 調整モード: 単軸ではなく**カテゴリ 6pt** と **6pt+T4** をアームにする。
        /// T4 はカテゴリ 6pt が解禁条件なので、 素の T4 単独では測れない ──
        /// 「6pt だけ」と「6pt+T4」を比べて初めて T4 自身のコストが出る。</summary>
        public bool challengeCategorySweep = false;
        /// <summary>T4 アームだけを回す (6pt アームを省く)。 基礎軸を変えていない回に使う。</summary>
        public bool challengeCategorySweepT4Only = false;
        /// <summary>**T4 を単独で測る**。 カテゴリ 6pt を載せず、 基準の上に T4 だけを置く。
        ///
        /// 通常 T4 はカテゴリ 6pt が解禁条件なので「6pt」と「6pt+T4」の差で測るが、
        /// それでは **T4 とカテゴリ基礎軸の相互作用**を分離できない。
        /// 〈最後の審判〉が E の上でだけ +1.4pt (プレイヤーが強くなる) を出したとき、
        /// 「審判自体が変」なのか「E の〈天変地異〉= ボス攻撃のターン比例 と噛み合って
        /// 戦闘が短くなっている」のかを区別する手段が無かった。 これはその分離用。
        /// **配点としては不正な組み合わせ**なので、 値付けには使わず診断専用。</summary>
        public bool challengeCategorySweepT4Alone = false;
        /// <summary>カテゴリ 1 つだけに絞る (ChallengeCategory の値)。 −1 で全部。
        /// **固着の再現用**。 どのアームで止まったかが分かっているとき、 そこだけを
        /// 進捗毎ラン で回して**止まったラン番号を 1 本に特定する**。</summary>
        public int challengeCategorySweepOnlyCat = -1;
        public int wiringSkillCompareScore = 0;
        /// <summary>技量帯スイープの runIdx の起点。 単軸スイープ (60000) と揃えるため。</summary>
        public int wiringSkillCompareIndexBase = 60000;
        /// <summary>挑戦単軸スイープの runIdx 起点。別シード帯で効果の再現性を検証するため変更可能。</summary>
        public int challengeAxisSweepIndexBase = 60000;
        /// <summary>アームごとの「要求戦力を読むか」。 arms と同じ長さ。 null なら全アーム有効。
        /// 同じ Super を新旧で比べるために要る (技量だけでは区別できないため)。</summary>
        public bool[] wiringSkillCompareBudget;

        /// <summary>ラン全体で次のボスの要求戦力を読むか (<see cref="RunPowerBudget"/>)。
        /// **天井専用の能力**。 false なら従来どおりの買い方に戻る。
        ///
        /// **既定は false** (2026-08-11)。 0pt・500 ラン のペア比較で 5層クリアが
        /// 有意に悪化した (23対41・χ²=4.52・p=0.034)。 7層は 38対37 で変化なし。
        /// 疑い: `NextBoss(floor)` が**現在の層のボス**を見ているため、 1〜3F では相手が弱く
        /// 「余裕」判定になってゲートが ×1.5 ＝ **序盤にむしろ買わなくなる**。 狙いと逆向き。
        /// 層別のギャップ実測分布を採ってから直すこと (docs/design-rejects.md)。</summary>
        public bool usePowerBudget = false;
        /// <summary>比べる方策。 既定は 素朴 / 最適 / 天井 の 3 本。</summary>
        public WiringSkill[] wiringSkillCompareArms =
            { WiringSkill.Naive, WiringSkill.Optimal, WiringSkill.Super };
        private string _skillSweepReport;
        /// <summary>乖離調査用: 先頭 10 ランの到達層。 同一 runIdx で両スイープを突き合わせる。</summary>
        private string _probeTrace = "";
        private string _probeRunConfig = "";
        private string _stateLineAtStart = "";

        /// <summary><b>アーム構成を印字する (2026-09-09)。</b>
        ///
        /// <para><c>_records</c> には通常バッチだけでなく、 後段で回るスイープのランも
        /// <b>同じリストに積まれる</b>。 サマリの「総ラン数」とクリア率はその混合平均になる。
        /// 実際に <c>Launch(3000)</c> の床/天井測定が 6 回連続で <c>n9000</c> ＝
        /// 本体3,000 + アブレーション1,000 + ランダム付与5,000 の混合として出ていた
        /// (SessionState のフラグが消えず、 以後の全バッチに相乗りしていた)。</para>
        ///
        /// <para>ランのインデックス帯で見分ける ── 本体 <c>0..runCount-1</c> /
        /// アブレーション <c>70000+</c> / ランダム付与 <c>90000+</c>。
        /// 混ざっていたら行頭に <b>警告</b>を出す ── 数字の由来が読めないサマリは
        /// 引用してはいけない。</para></summary>
        private string ArmCompositionLine()
        {
            int main = 0, abl = 0, grant = 0, other = 0;
            foreach (var r in _records)
            {
                if (r.index >= 90000) grant++;
                else if (r.index >= 70000 && r.index < 80000) abl++;
                else if (r.index >= 0 && r.index < runCount) main++;
                else other++;
            }
            // **割付監査 (SRM 相当)。** 期待した本数と実際の本数が合っているかを毎回照合する。
            //   ラン数は乱数割付ではないので厳密一致で判定してよい。 2026-09-09 に
            //   `Launch(3000)` が 9,000 ラン になっていたのを 2 日間見逃したので、
            //   ずれたら**エラーで鳴らす** ── サマリの隅に出すだけでは読み飛ばす。
            int expectedMain = ascensionMode || sweepAllMetaAxes ? main : runCount;
            if (main != expectedMain)
                Debug.LogError($"[AutoRunner][割付監査] 通常バッチの本数が合わない: 期待 {expectedMain} / 実際 {main}"
                             + " ── この条件の数字は使わないこと");

            if (abl == 0 && grant == 0 && other == 0)
                return $"[アーム] 通常バッチのみ {main} ラン (期待 {expectedMain}: {(main == expectedMain ? "一致" : "★不一致★")})";
            var parts = new List<string> { $"通常バッチ {main}" };
            if (abl > 0) parts.Add($"アイテムAblation {abl}");
            if (grant > 0) parts.Add($"ランダム付与 {grant}");
            if (other > 0) parts.Add($"その他 {other}");
            return "[アーム] " + string.Join(" / ", parts)
                 + "  ※★上のクリア率はこれらの混合平均★ 条件比較には使えない";
        }

        /// <summary><b>主目標をサマリの先頭に出す (2026-09-09)。</b>
        /// クリア率を最上位の目的関数に決めたので、 R帯分布の 300 行下ではなく冒頭に置く。
        /// 区間は Wilson ── 正規近似は p が 0 に近いと下限が負になり、 床条件 (1%台) で壊れる。
        ///
        /// <para><b>SESOI を併記する。</b> 「有意差なし」は「差が無い」ではないので、
        /// この n で検出できる最小の差を出しておく。 これ未満の主張はしないこと。</para></summary>
        private string PrimaryObjectiveLine()
        {
            int n = 0, clears = 0;
            foreach (var r in _records)
            {
                if (r.bandScore < 0) continue;      // CRASH / DEADLOCK は母数から外す
                n++;
                if (r.bandScore >= 11) clears++;
            }
            if (n == 0) return "【主目標】 クリア率 : 計測不能 (有効ラン 0)";
            WilsonInterval(clears, n, out double lo, out double hi);
            double p = clears / (double)n;
            // 2 群比較で 80% の検出力を得るのに必要な差 (両群同 n・α=0.05 両側)。
            double sd = Math.Sqrt(2.0 * p * (1 - p) / n);
            double mde = 2.8 * sd * 100.0;
            return $"【主目標】 クリア率 : {p * 100:F2}%  ({clears}/{n})"
                 + $"  95%CI [{lo * 100:F2}, {hi * 100:F2}]"
                 + $"  ※この n で検出できる最小差 (MDE) ≒ {mde:F2}pt";
        }

        /// <summary>Wilson score 区間。 p が 0 付近でも下限が負にならない。</summary>
        private static void WilsonInterval(int x, int n, out double lo, out double hi, double z = 1.96)
        {
            double p = x / (double)n, z2 = z * z;
            double denom = 1 + z2 / n;
            double center = (p + z2 / (2 * n)) / denom;
            double half = z / denom * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n));
            lo = Math.Max(0, center - half); hi = Math.Min(1, center + half);
        }

        /// <summary>**名目ではなく実効値**を読む計装 (2026-08-10)。
        ///
        /// 同じ「遺物なし・挑戦0pt」を名乗る 2 つのスイープで基準値が 53.3% と 16.6% に
        /// 割れたため。 `_pendingPresetRelic` / `_pendingChallengeLoadout` は
        /// **null が「無し」ではなく「触らない」** を意味する構造なので、
        /// 設定したつもりの条件が実際には前の状態を引き継いでいる可能性がある。
        /// 推測で潰さず、 生きている State から直接読む。</summary>
        private static string EffectiveStateLine()
        {
            var st = MetaProgression.MetaProgressManager.Instance?.State;
            if (st == null) return "[実効状態] MetaProgressState が null";
            int chal = st.challenge != null ? MetaProgression.ChallengeResolver.Score(st.challenge) : -1;
            int relicCount = st.relics != null ? st.relics.Count : 0;
            int relicPts = 0;
            if (st.relics != null)
                for (int i = 0; i < st.relics.Count; i++) relicPts += st.relics[i].TotalPoints;
            // **生のロードアウトと解決済みキャッシュを分けて出す。**
            //   各システムが見るのは st.Challenge (解決済み)。 ロードアウトを書き換えても
            //   InvalidateChallenge() を呼ばなければ古い解決結果が残り続ける。
            //   前者だけ見て「0pt だから条件は同じ」と判断すると取り違える (2026-08-10 に実際にやった)。
            int resolved = st.Challenge != null ? st.Challenge.Score : -1;
            return $"[実効状態] 挑戦スコア ロードアウト {chal}pt / **解決済み {resolved}pt** "
                 + $"/ 遺物 {relicCount}個 計{relicPts}pt / 装備index {st.equippedRelicIndex} "
                 + $"/ メタ ダイス合計+{MetaProgression.MetaBuffApplicator.GetDiceTotalBonus()} "
                 + $"攻撃端子+{MetaProgression.MetaBuffApplicator.GetAttackTerminalPerDice()}";
        }

        /// <summary>次のボスに対する戦力ギャップ。 **天井 (Super) のときだけ効く** ──
        /// Optimal と共有すると技量帯の参照点が動き、 要求戦力の寄与を単独で測れなくなる。
        /// 1.0 を返せば「充足」＝ 従来どおりの振る舞いになる。</summary>
        private float PowerGap()
        {
            if (!usePowerBudget || EffectiveWiringSkill != WiringSkill.Super) return 1f;
            var run = GameManager.Instance?.Run;
            if (run == null) return 1f;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            int dice = ctx != null && ctx.playerDice != null && ctx.playerDice.Length > 0
                     ? ctx.playerDice.Length : 5;
            float mean = 3.5f;
            if (ctx?.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0)
            {
                long sum = 0;
                for (int i = 0; i < ctx.equippedDiceFaces.Length; i++) sum += ctx.equippedDiceFaces[i];
                mean = sum / (float)ctx.equippedDiceFaces.Length;
            }
            return RunPowerBudget.Gap(run, run.Depth, superAI.DmgMul, dice, mean);
        }
        private string _relicSweepReport;
        /// <summary>次ランに流し込む固定遺物。 ResetAll 直後に State へ入れる (周回モードと同じ位置)。</summary>
        private MetaProgression.Relics.RolledRelic _pendingPresetRelic;
        /// <summary>遺物構成をスイープ側が**明示的に管理する**か。 true なら毎ラン作り直し、
        /// <see cref="_pendingPresetRelic"/> が null なら **遺物なしを強制**する。
        /// false のときだけ従来どおり「触らない」。</summary>
        private bool _pendingRelicExplicit;

        [Tooltip("【計測用】出目パーツを陳列しない。天井AIのロールアウトがパーツを理解しない問題の切り分け")]
        public bool suppressFacePartOffers = false;

        [Header("探索 (ランダム化ホールドアウト)")]
        /// <summary>購入決定のうち**ランダムに選ぶ割合**。 0 = 探索なし (既定)。
        ///
        /// <para><b>学習バッチでだけ立てること。</b> 測定中に効かせると BOT が意図的に劣る
        /// 買い物をするので、 クリア率が下がって比較にならない。 AutoRunner が learnTier に
        /// 合わせて自動で立てる。</para>
        ///
        /// <para>0.10 は**実測していない仮値**。 高いほど不偏サンプルが速く貯まるが、
        /// そのぶん貪欲データのクリア率が下がる。 較正が要る。</para></summary>
        public float exploreHoldoutRate = 0f;

        /// <summary>**探索ランの割合** (2026-08-17b)。 探索は**ラン単位**で ON/OFF する。
        ///
        /// <para>決定レベルで 10% 探索すると、 1 ラン 25 回の購入のうち平均 2〜3 回が探索になり、
        /// **ほぼ全ランが「探索を含むラン」**になる。 すると探索で買った品が観測 lift 側の
        /// 「取得」としても記録され、 貪欲統計が汚れる。 実測: band_0 に探索付き 10,000 ランを
        /// 足しただけで 0pt クリア率が Optimal −3.1pt / Super −4.7pt 下がった。</para>
        ///
        /// <para>ラン単位で分ければ両者は交わらない ── 貪欲ランは方策の質を測り、
        /// 探索ランは不偏な値付けだけに使う。</para></summary>
        public float exploreRunFraction = 0.30f;
        /// <summary>このランが探索ランか。 **ラン番号で決める** (乱数を使わない = 再現可能)。</summary>
        private bool _isExploreRun;
        /// <summary>探索の専用乱数。 <b>GameRng を消費しない</b> ── 消費列が変わると
        /// シード固定のペア比較が壊れる。</summary>
        private System.Random _exploreRng = new System.Random(12345);
        /// <summary>足切りを通った候補の添字 (1 決定ぶん・使い回し)。</summary>
        private readonly List<int> _exploreCandidates = new List<int>();

        [Header("遺物")]
        /// <summary>通常バッチでも**遺物なしを強制**する (2026-08-17)。
        ///
        /// <para>既定 false は「触らない」＝ 保存された MetaProgressState の遺物を引き継ぐ。
        /// バランスの正式な基準は**遺物なし**（周回引継ぎなので初回プレイヤーは 0 個）なので、
        /// 基準値を採るときはこれを立てる。 立てないまま「遺物なし」を名乗ると、
        /// 前セッションで転がった遺物込みの数字を基準として扱ってしまう
        /// ── 実際に 2026-08-17 の 1000 ラン測定は 13pt の遺物込みで走っていた。</para>
        ///
        /// <para><b>実効値はサマリー冒頭の `[実効状態]` 行で必ず確認すること。</b>
        /// 名前で判断しない。</para></summary>
        public bool forceNoRelic = false;

        /// <summary><b>通常バッチで遺物を理論値 (TheoreticalBestCursed / 29pt) に固定する。</b>
        ///
        /// <para>アイテム学習用 (2026-09-04)。 遺物なしだと 6F/7F 到達が 15% 前後しかなく、
        /// 終盤の情報がほとんど溜まらない ── 到達したランの数がそのまま推定精度になるので、
        /// 学習は「クリアが起きる領域」で回した方が同じラン数から採れる情報が多い。
        /// 実測でも遺物 29pt は 7層クリア率をおよそ 2 倍にする ([[project_difficulty_curve_20260817]])。</para>
        ///
        /// <para><see cref="forceNoRelic"/> と同時に立てないこと。 両方立った場合は
        /// <b>遺物なしを優先</b>する (基準値測定の方が壊れると影響が大きい)。</para>
        ///
        /// <para><b>実効値はサマリー冒頭の `[実効状態]` 行で必ず確認すること。</b></para></summary>
        public bool forceTheoreticalRelic = false;

        /// <summary><b>武器家系をクラス開始武器に固定する</b> (計測用・既定 OFF)。
        /// <see cref="GameLoop.Loadout.LockWeaponFamily"/> へ流す。
        ///
        /// <para>職業は 4 種均等配分なので、 立てると各家系が約 25% のランで最後まで担がれ、
        /// <b>4 家系を同じ厚みで測れる</b>。 立てないと BOT の乗り換えが偏り
        /// (2026-09-05 実測: 最終武器が 盾 1.0%、 評価式修正後でも 4.7%)、
        /// 「弱いのか選ばれていないだけなのか」が分離できない ── 循環して永久に測れない。</para>
        ///
        /// <para><b>ゲーム性ではなく測定条件</b>。 製品挙動を測るときは OFF に戻すこと。
        /// static フィールドはドメインリロードで消えるので、 **必ずこの MonoBehaviour 経由で渡す**
        /// (エディタメニューから直接 static へ書くと PlayMode 開始時に失われる)。</para></summary>
        public bool lockWeaponFamily = false;

        /// <summary><b>強盗の罰を旧仕様「出禁」に戻す (既定 false・2026-09-12)。</b>
        /// true = 強盗後のショップマスを素通り / false = 以降のショップ価格 ×2。
        ///
        /// <para>BOT の方策修正 (買う前に撃つ / ランで最後の店に限定) と 罰の設計変更 を
        /// 同時に入れたので、 <b>どちらが効いたのかを分ける</b>ために要る。
        /// true で 1 本、 false で 1 本走らせて差を取る。 static はドメインリロードで消えるので
        /// 必ずこのフィールド経由で渡すこと。</para></summary>
        public bool robberyBlocksShops = false;

        /// <summary>強盗の罰 (以降のショップ価格倍率) の上書き。 <b>0 = 触らない (既定 2.0)。</b>
        ///
        /// <para>切り分け用。 強盗は 3 つのコストを同時に払う ── 価格 ×2 / 物資 −10 /
        /// エリート戦 (HP1840・被ダメ50%軽減)。 極点が負けているとき、
        /// <b>どれが効いているかは 1 つずつ外さないと分からない</b>。
        /// 1.0 にすれば罰だけが消え、 残りは物資と戦闘の負担になる。</para></summary>
        public float robberySurcharge = 0f;

        /// <summary>強盗を<b>ラン最後の店でだけ</b>撃つ (旧方策)。 既定 false = 6 層以降で撃つ。
        ///
        /// <para>切り分け用。 「報酬が機能していない」のか「撃つタイミングが悪い」のかは、
        /// <b>同じビルド・同じ戦闘経路でタイミングだけを変えないと</b>分からない。
        /// 旧測定の +0.68pt は戦闘経路が違う上に発動率が 4.4 分の 1 で、 比較に使えない。</para></summary>
        public bool robberyFinalShopOnly = false;

        /// <summary>強盗の物資コストの上書き。 <b>-1 = 触らない (既定 10)。</b>
        /// 切り分け用 ── 罰 (価格×2) は外しても 0.14pt しか動かなかったので、
        /// 残る「戻らない資源」は物資だけ。 0 にして 6 層ボス突破率が戻れば物資が原因。</summary>
        [UnityEngine.Serialization.FormerlySerializedAs("robberyHopeCost")] public int robberyProvisionCost = -1;

        /// <summary>この層のボス戦だけをターン単位で記録する。 <b>0 = 無効 (既定)。</b>
        /// 出力は <c>productionOutputRootOverride</c> 配下の boss_trace.tsv。
        /// 集計では掴めない差を、 同一シードの対で 1 本ずつ読むための道具。</summary>
        public int bossTraceFloor = 0;

        // [廃止 2026-09-14] reserveGoldForRitual。 〈門〉リワークでゴールドの要求が消えた。

        /// <summary>〈門〉① 血 (最大HP −25%) を払うか。 <b>既定 true = 欠陥を避ける</b>。
        /// false にすると〈不完全な修復〉(充電が毎ターン半減) を背負って戦う。</summary>
        public bool gateBotPaysBlood = true;
        /// <summary>〈門〉② 遺物 2 個を焚くか。 <b>既定 true</b>。
        /// false にすると〈不完全な起動〉(同一端子は 2 本まで) を背負って戦う。</summary>
        public bool gateBotPaysRelics = true;
        /// <summary>〈門〉③ 物資 40 を払うか。 <b>既定 true</b>。
        /// false にすると〈不完全な転移〉(ブロック貫通) を背負って戦う。</summary>
        public bool gateBotPaysTransfer = true;

        /// <summary><b>[計測専用] 門を素通りする。</b> 既定 false。
        ///
        /// <para>true にすると 3 工程のどれも解決せずに <c>CompleteGateRitual</c> だけを呼ぶ ──
        /// <b>代償も払わず欠陥も背負わない</b>。 製品では起こり得ない状態で、
        /// 「門が無かったら」の対照群を作るためだけに在る。</para>
        ///
        /// <para><b>なぜ要るか。</b> 「払う/払わない」の比較だけでは、 どちらが得かは分かっても
        /// <b>門という機構が正味プラスなのかマイナスなのか</b>が分からない。
        /// 払う側が損と出ても、 それは「払わない側が得」なのか「門ごと無い方が得」なのかで
        /// 次にやることが変わる (数値調整 vs 機構の撤去)。 §24 の旧儀式は
        /// この対照を取らないまま「払う方が −4.33pt」だけで判断していた。</para></summary>
        public bool gateBotSkipsAll = false;

        /// <summary>[較正専用] フラット攻撃加算の倍率。 <b>負 = 触らない。</b>
        /// 配線 : フラットの比を目標 (5:5〜6:4) へ寄せるための掃き用。</summary>
        public float flatAttackMul = -1f;

        /// <summary>[較正専用] エリート戦の報酬倍率 (CombatRewards.EliteRewardMul)。 <b>負 = 触らない。</b></summary>
        public float eliteRewardMul = -1f;

        /// <summary>[較正専用] 戦闘報酬の配分 (GameLoop.CombatRewards)。 <b>負 = 触らない。</b>
        /// 「現物で払うか金で払うか」の比率なので、 <b>3 つは必ずセットで振る</b> ──
        /// ドロップだけ削ると供給が減り、 金だけ増やすと供給が増える。</summary>
        public float eliteDropRate = -1f;
        public float normalDropRate = -1f;
        public int combatGoldScale = -1;

        /// <summary>[較正専用] エリート格上げ率 (%) の上書き。 <b>負 = 触らない。</b>
        /// メタ配点と切り離して「エリートの損得」だけを測るための穴。</summary>
        public float eliteUpgradePctOverride = -1f;

        /// <summary>[較正専用] 低HP 忌避ゲートの 2 値。 <b>負 = 触らない。</b>
        ///
        /// <para>ゲートは <c>lowBar = dangerTarget</c> の一本 (2026-09-15 に床を一本化)。
        /// <c>dangerTarget = clamp(max(1戦の実被弾 × SafetyFights, 最悪の1発), 0.25, 0.90)</c>。
        /// <see cref="FightBudgetDanger"/> 参照。</para></summary>
        public float navSafetyFights = -1f;
        /// <summary>[較正専用] 被弾見積りから引くブロックの割合。 <b>負 = 触らない</b> (既定 0.7)。
        /// <see cref="BlockCreditRatio"/> 参照。</summary>
        public float navBlockCredit = -1f;
        /// <summary>[較正専用] 金逼迫度の目盛り <see cref="GoldComfort"/>。 <b>負 = 触らない。</b></summary>
        public float navGoldComfort = -1f;
        /// <summary>[較正専用] 開示済みマスの敵で忌避を判断し直す (既定 false)。</summary>
        public bool navTileDanger = false;
        /// <summary>[較正専用] 危険度の下限 <see cref="DangerFloor"/>。 <b>負 = 触らない。</b></summary>
        public float navDangerFloor = -1f;
        /// <summary>[較正専用] 剛胆の極点〈精鋭スレイヤー〉を切る (既定 false = 有効のまま)。</summary>
        public bool valorSlayerOff = false;
        /// <summary>[較正専用] 特売の購入意欲補正のしきい値 (%)。 <b>負 = 触らない。</b></summary>
        public int saleBonusHiPct = -1;
        public int saleBonusLoPct = -1;
        /// <summary>[較正専用] リロール後に特売を再適用する (既定 false = 従来)。</summary>
        public bool rerollKeepsSale = false;
        /// <summary>[較正専用] 全アイテムの店頭価格へ一律加算する額 (G)。</summary>
        public int itemPriceAdd = 0;
        /// <summary>[較正専用] 1 店あたりのリロール上限の上書き。
        /// <b>-1 = BOT 方策の既定 (<see cref="BotRerollCapPerShop"/>) / -2 = 無制限 / 0 以上 = その値</b>。
        /// <see cref="ItemSystem.Shop.ShopManager.RerollHardCap"/> 経由で全 6 経路に効く。</summary>
        public int rerollHardCap = -1;

        /// <summary><b>BOT のリロールは 1 店 2 回まで</b> (2026-09-18 製品化)。
        ///
        /// <para>実測 (Balanced 10,000 ラン × 2 日): 無制限 27.33% → 33.77% (+6.44 / z=+16.3)、
        /// 翌日の再測定で 33.00% → 39.59% (+6.59 / z=+16.6)。 1 回目 +14.24pt / 2 回目 +1.76pt /
        /// 3 回目 −1.35pt。 価格が 5→10→20 と上がる 3 回目から、 棚替えで得る品の価値を
        /// 代金が上回る (ITT の金係数 1G=0.0075 band で 20G = 0.15 band ≈ 棚の中央品)。</para>
        ///
        /// <para><b>ゲーム規則ではなく BOT の方策。</b> 人間のリロールは制限しない ──
        /// ShopManager.RerollHardCap の既定は -1 のままで、 AutoRunner が走るときだけ立てる。</para>
        ///
        /// <para>評価を見る停止則 (<see cref="RerollStopRule"/>) は 2 経路だけ置き換えた版で
        /// 上限に 5.5〜9.8pt 負けた。 止めて浮いた金を最終店の消耗品ループが
        /// 1 回 47G で燃やしたため。 上限は TryReroll の入口で効くので全経路を同時に縛る。</para></summary>
        public const int BotRerollCapPerShop = 2;
        /// <summary>[較正専用] リロール価格の倍率。 <b>1.0 = 製品</b>。 下限 1G なので 0 にしても無料にはならない。
        /// <see cref="ItemSystem.Shop.ShopInventory.RerollPriceScale"/>。</summary>
        public float rerollPriceScale = 1f;
        /// <summary>[較正専用] リロール停止則を使う (<see cref="RerollStopRule"/>)。 既定 false = 旧規則。</summary>
        public bool rerollStopRule = false;
        /// <summary>[較正専用] リロール直前にフェーズ3 の購入を済ませる。 既定 false = 従来の順番。</summary>
        public bool phase3BeforeReroll = false;
        /// <summary>[較正専用] リロール価格曲線 first + step×(n−1)。 <b>step 負 = 製品曲線 (触らない)</b>。</summary>
        public int rerollCurveFirst = 5;
        public int rerollCurveStep = -1;
        /// <summary>[較正専用] 停止則の事前値 <see cref="RerollShelfValue"/>。 <b>負 = 触らない。</b></summary>
        public float rerollShelfValue = -1f;
        /// <summary>[較正専用] 事前値の重み <see cref="RerollPriorWeight"/>。 <b>負 = 触らない。</b></summary>
        public float rerollPriorWeight = -1f;

        /// <summary>[較正専用] 航行スコアのエリート基準値。 <b>負 = 触らない。</b>
        /// 既定は <see cref="EliteNavBase"/> / <see cref="EliteNavHpBonus"/> (1.6 / 1.2)。
        /// <b>旧挙動を再現するなら 2.0 / 0.0</b> ── エリートが一度も選ばれない固定順位。</summary>
        public float eliteNavBase = -1f;
        public float eliteNavHpBonus = -1f;
        /// <summary>精鋭の戦闘コスト見積り (<see cref="UseEliteCostNav"/>) を使うか。 既定 true (2026-09-20)。</summary>
        public bool navEliteCost = true;

        /// <summary>敵の <c>baseAttack</c> 上書き。 <c>"boss_layer1:4,boss_layer6:24"</c> 形式。
        /// 空 = enemies.json のまま。 難易度の梯子を層ごとに振るための較正用。</summary>
        public string enemyAttackSpec = "";
        /// <summary>較正スイープ専用: 敵の最大 HP の上書き ("id:hp,...")。 EnemyDatabase.HpOverrideSpec。</summary>
        public string enemyHpSpec = "";

        /// <summary>全敵の攻撃側 (baseAttack/ロール上下限/threat) へ掛ける倍率。 1.0 = 素のまま。
        /// <b>応答は極端に非線形 (§13-5)。 0.1 刻みより粗く振らないこと。</b></summary>
        public float enemyAttackMul = 1f;

        /// <summary>〈サーベル・ワルツ〉の剣の舞プール絞り込み確率。 <b>負 = 触らない (既定 0.6)。</b>
        /// 4 枚集約の成立率を較正するための穴。</summary>
        public float saberWaltzShopBias = -1f;

        /// <summary>〈不完全な修復〉毎ターンの HP 喪失率。 <b>負 = 触らない (GateFlaws の既定)。</b>
        /// 較正スイープで倍率を掃くための穴。</summary>
        public float gateRepairDrainPct = -1f;
        /// <summary>〈不完全な起動〉攻撃端子のカット率。 <b>負 = 触らない。</b></summary>
        public float gateIgnitionAttackCutPct = -1f;
        /// <summary>〈不完全な転移〉ブロック貫通率。 <b>負 = 触らない。</b></summary>
        public float gatePierceRate = -1f;

        /// <summary>〈門〉で物資 400 (2026-09-28 に ×10) を払うつもりのランが、その 400 を<b>取り置く</b>ようになる層。
        /// 0 で無効。 既定 6 = 6 層に入った時点から。
        ///
        /// <para><b>なぜ要るか。</b> 素の方策は物資を「使い切ってよい資源」として扱う。
        /// そこへ 40 の支払いを足すと、 実測で 7 層突入時の平均 86 から 46 へ落ちる ──
        /// これは <see cref="GameLoop.ProvisionSystem.FloorDepleting"/> (45) の 1 上で、
        /// <c>UpdateCapLock</c> が<b>物資上限を 45 に恒久ロック</b>する境界のすぐ際。
        /// つまり「払う」を選ぶだけで帯が落ち、 二度と戻らない。
        /// これでは測っているのが<b>代償の重さ</b>ではなく<b>取り置きを知らない方策の損</b>になる
        /// (規則と方策はセット)。</para>
        ///
        /// <para><b>6 層からにしてある。</b> 1 層から効かせると寄り道の判断が全ラン変わり、
        /// 「門の代償」ではなく「迂回方策」を測ることになる。 6/7 層は一本道 (横移動なし・
        /// 6 層は戦闘 1 回) なので、 実質は<b>物資回復の使用と購入が早まるだけ</b>で、
        /// 道中の経路は動かない。</para></summary>
        [UnityEngine.Serialization.FormerlySerializedAs("gateHopeReserveFromFloor")] public int gateProvisionReserveFromFloor = 6;

        /// <summary>いま取り置くべき物資。 払わないアーム・素通りアーム・門より手前の層では 0。</summary>
        private int GateProvisionReserve()
        {
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null || gateProvisionReserveFromFloor <= 0) return 0;
            if (gateBotSkipsAll || !gateBotPaysTransfer) return 0;
            // 7 層を越えたら門は解決済み。 取り置く理由が無い。
            if (run.Depth < gateProvisionReserveFromFloor || run.Depth > 7) return 0;
            return GameLoop.GameManager.GateProvisionDemand;
        }

        /// <summary>物資回復を使う/買う閾値。 取り置きぶんだけ底上げする。</summary>
        private float ProvisionRefillFloorEffective()
            => AutoTest.PolicyParameters.Current.provisionRefillFloor + GateProvisionReserve();

        /// <summary>寄り道を許す物資の下限。 取り置きぶんだけ底上げする。
        /// (6/7 層は一本道なので実効は無いが、 <b>規則を 1 か所に閉じる</b>ために揃えておく。)</summary>
        private float LateralProvisionFloorEffective()
            => AutoTest.PolicyParameters.Current.lateralProvisionFloor + GateProvisionReserve();

        /// <summary><b>通常バッチを固定の挑戦スコアで走らせる (2026-09-10)。</b> 0 = 触らない。
        ///
        /// <para>従来、 挑戦ptを固定する経路は<b>スイープ専用</b>だった
        /// (<c>ChalOneScoreKey</c> + <c>ChalSweepKey</c> → <see cref="RunChallengeSweep"/>)。
        /// そのため「高難易度帯で通常バッチを回す」「高難易度帯でランダム付与を採る」ができず、
        /// 帯別の学習ディレクトリ (<c>bot_band_16_30</c> / <c>bot_band_31up</c>) が
        /// 器だけあって埋まらなかった。</para>
        ///
        /// <para><see cref="AscensionLoop.BuildLoadoutAtScore"/> で構成を組み、
        /// <c>_pendingChallengeLoadout</c> へ流し込む。 実効値は必ずサマリ冒頭の
        /// <c>[実効状態]</c> 行で確認すること ── 目標 pt と解決済み pt は一致しないことがある。</para></summary>
        public int challengeScoreTarget = 0;

        /// <summary><b>整備パネルの配分を直接指定する (2026-09-10)。</b> 空なら
        /// <see cref="metaAllocation"/> のプリセット。 形式は <c>"Shell:6,Output:6,Guard:5"</c>。
        /// 未記載の軸は 0 ＝ drop-one アブレーションが書ける。
        /// 実効値は <c>[実効状態]</c> 行ではなくコンソールの
        /// <c>[MetaAllocationPresets] spec:… を適用</c> で確認すること。</summary>
        public string metaRankSpec = "";

        [Header("整備パネル drop-one アブレーション (2026-09-10)")]
        /// <summary>Balanced から 1 軸ずつ抜いて限界寄与を測る。</summary>
        public bool metaAxisAblation = false;
        /// <summary>極点 (r10) 同士の比較。 <see cref="metaAxisAblation"/> と排他。</summary>
        public bool keystoneSweep = false;
        /// <summary><b>r9 と r10 の限界比較 (2026-09-12)。</b> 極点比較は「そのトラックに 10pt 使う」と
        /// 「極点そのもの」を分離できない ── 燈火 −5.37pt が、 横移動5→2 が悪いのか
        /// 燈火に 10pt 使うのが悪いのかが不明のままだった。 同じトラックの r9 と r10 を並べれば
        /// 差が極点の取り分になる (r9 の余った 1pt は共通の埋め草へ行く)。</summary>
        public bool rankMarginSweep = false;
        /// <summary>r9/r10 を比べるトラック名。 カンマ区切り (例 "Shell,Lantern")。
        /// <c>"*"</c> または空で 10 段トラック全部。</summary>
        public string rankMarginTracks = "Shell,Lantern";
        /// <summary><b>スループット計測 (2026-09-13)。</b> <see cref="runsPerYield"/> を内部で振って
        /// ラン/秒 を実測する。
        ///
        /// <para><b>仮説</b>: スイープ系は <c>runsPerYield = 1</c> ＝ <b>1 ラン ごとに 1 フレーム待つ</b>
        /// 設定で走っている。 実測スループットが 12〜15 ラン/秒 で、 <b>非フォーカスのエディタの
        /// フレームレートとほぼ一致</b>する ── CPU ではなくフレーム待ちで律速している疑い。
        /// 正しければ刻みを増やすだけで数倍になる。 <b>推測で設定を変えず、 測ってから決める。</b></para></summary>
        public bool yieldBenchmark = false;
        /// <summary>ベンチ 1 条件あたりのラン数。</summary>
        public int yieldBenchRuns = 600;

        /// <summary>走らせる段。 カンマ区切り (既定 "9,10")。
        /// <b><c>"9"</c> だけにすれば r9 側のみ</b> ── 極点比較 (r10) を同じシード・同じ配分構築で
        /// 既に走らせてあるなら、 r10 を撮り直すのは 2 倍の無駄になる。</summary>
        public string rankMarginRanks = "9,10";
        /// <summary>1 アームあたりのラン数。 11 アーム 走るので総数はこの 11 倍。</summary>
        public int metaAblationRuns = 3000;
        private Dictionary<MetaProgression.MetaPanelKind, int> _metaAblationSpec;
        private string _metaAblationReport = "";

        [Header("ITT 優先度 (2026-09-09)")]
        /// <summary><b>BOT の購入優先度を、ランダム付与の ITT 係数で並べる。</b>
        /// 既定 false ＝ 従来の観測 regβ。 詳細は <see cref="GrantItt"/>。
        /// static はドメインリロードで消えるので**必ずこのフィールド経由**で渡す。</summary>
        public bool useIttBeta = false;
        /// <summary>ITT に推定値が無い品を未学習として落とす (生 pt モードでのみ意味を持つ)。</summary>
        public bool ittStrict = true;
        /// <summary>Δクリア率 (pt) を準パワーへ直接入れる。 <b>既定 false = 順位マッピング</b>。
        /// 生 pt は付与プールが全品を覆うまで交絡する
        /// (<see cref="LearnedPriorityProvider.IttScoreMode"/> の解説)。</summary>
        public bool ittRawPt = false;

        [Header("アイテム アブレーション (2026-09-08)")]
        /// <summary><b>同一シードのペアで「そのアイテムを開幕付与する / しない」を比べる。</b>
        ///
        /// <para><b>なぜ要るか。</b> 所持は結果の<b>下流</b>にある ── 「持っている」は
        /// 「そこまで生き延びた」の帰結でもあるので、 観測データからは因果が取り出せない。
        /// 実測 (2026-09-07 / 250,000 ラン): 所持率 80% 超の品の素の差は <b>+4.35 band</b>、
        /// 同じ品の不偏 lift は <b>+0.14</b> ── 見えているものの 97% が交絡である。
        /// リッジ回帰はその 95% を落として +0.22 まで詰めるが、 残差 ±0.2 は
        /// アイテム間の真のばらつき (SD <b>0.15 band</b>) より大きい。 観測系はここが限界。</para>
        ///
        /// <para><b>シミュレーションだけが持てる手段。</b> ライブゲームは同じ試合をやり直せないので
        /// 17lands の IWD (引いたかどうかの乱数を道具変数にする) のような工夫が要る。
        /// こちらは<b>同じシードで付与あり/なしを両方走らせられる</b> ── 道具変数ではなく本物の介入。
        /// 交絡はゼロになる。</para>
        ///
        /// <para><b>まず測るのは効果ではなく「対の差の SD」。</b> ペアにしてもランが途中で
        /// 発散すれば差の分散は落ちない。 落ちなければ全品アブレーションは非現実的な
        /// ラン数になるので、 <b>1 品 500 対で実現可能性を先に確かめる</b>。
        /// A/A アーム (両方とも付与なし) を必ず並べること ── そこで差が出るなら
        /// 決定性が壊れており、 ペアリング自体が無意味になる。</para></summary>
        [Tooltip("true で 同一シードのペアで『開幕付与する/しない』を比べ、対の差のSDを実測する")]
        public bool itemAblationSweep = false;
        [Tooltip("対象アイテムID (カンマ区切り)。 空なら既定の3品 (強LEG/中BRONZE/弱め)")]
        public string itemAblationIds = "";
        [Tooltip("1 アームあたりのラン数 (= 対の数)")]
        public int itemAblationRuns = 500;
        /// <summary>このランの開幕に強制付与するアイテム id (null = 付与しない)。
        /// <b>GameRng を消費しない</b>ので、 付与の有無でシード列はずれない。</summary>
        private string _pendingGrantItemId;
        private string _itemAblationReport;

        [Header("ランダム付与試行 (2026-09-08)")]
        /// <summary><b>1 ランに複数品をランダムに配り、割り当てだけで回帰する。</b>
        ///
        /// <para><b>1 品ずつのアブレーションより 2〜5 倍安い。</b> 必要ラン数は
        /// <c>SE = σ / (SD(x)·√N)</c> で決まり (σ=2.83)、 1 ランが全品の情報を運ぶので
        /// 全 163 品を同時に測れる ── 期待 3 品/ラン なら SE 0.075 に 78,000 ラン、
        /// 6 品/ラン なら 39,500 ラン。 1 品ずつだと 183,000 ラン かかる。
        /// 失うのは同一シードのペアだが、 その利得は元々 1 割しかなかった (SD 2.83→2.5)。</para>
        ///
        /// <para><b>付与の層をランごとにランダムにする。</b> 開幕固定だと、 複利で効く品
        /// (金を生む品など) だけが 7 層ぶん転がるので系統的に持ち上がる。 戦闘系は
        /// タイミングに鈍感なので、 開幕固定は<b>経済系だけを贔屓する</b>。 実際に手へ入る
        /// 時期に合わせて配れば estimand が実態に寄り、 <b>付与層で層別すれば複利の効き方も
        /// タダで出る</b> (開幕付与と後半付与の差がそのまま「スケール性」の指標)。</para>
        ///
        /// <para><b>金も 1 つの処置として混ぜる。</b> 「+NG を配る」を割り当てベクトルに入れると、
        /// 回帰係数がそのまま <b>1G の band 価値</b>になる。 これでアイテムが生んだ金額を
        /// 換算して引けるので、 <c>総効果 − 経済経由 = 戦闘への純粋な寄与</c> に分解できる。</para>
        ///
        /// <para><b>回帰の入力は割り当てベクトルだけ (ITT)。</b> BOT が後から買った品を
        /// 説明変数に入れてはいけない ── 購入は処置の<b>下流</b>なので、 経済アイテムの効果が
        /// 媒介変数に吸われて過小に出るうえ、 コライダー条件づけで偏りが入る。</para>
        ///
        /// <para><b>不服従は層で切って読む。</b> 4 層に配る割り当ては、 3 層で死んだランには
        /// 届かない。 ITT としては偏らないが、 効果は薄まる。 <b>到達したランだけに絞るのは禁止</b>
        /// ── 到達は結果の下流なのでコライダー条件づけになる。 正しくは<b>割り当て層ごとに
        /// 別々に読む</b>: 1 層の割り当てはほぼ全ランに届くので薄まらず、 後半層は薄まる代わりに
        /// 「遅く手に入れた場合の価値」を測っている。</para></summary>
        [Tooltip("true で 1ランに複数品をランダム付与し、割り当てだけで回帰する (ITT)")]
        public bool randomGrantTrial = false;
        [Tooltip("ラン数")]
        public int randomGrantRuns = 5000;
        [Tooltip("1 ランあたりの期待付与数。 多いほど安いが、開幕の姿が変わる")]
        public float randomGrantExpectedItems = 4f;
        /// <summary>付与プールを消耗品・武器・出目パーツまで広げる。 <b>既定 false = パッシブのみ (従来)</b>。
        ///
        /// <para><b>1 品あたりの付与確率を保つこと。</b> プールは 89 → 約 160 品 へ 1.8 倍になるので、
        /// <c>randomGrantExpectedItems</c> を据え置くと 1 品あたりの処置ラン数が半分になり、
        /// <see cref="GrantItt.MinTreatedRuns"/> (300) を割る品が出る。 同じ精度が要るなら
        /// 期待付与数も 1.8 倍にするか、 ラン数を 1.8 倍にする。</para>
        ///
        /// <para><b>基準クリア率が動く点に注意。</b> 付与数を増やすとランが強くなり、
        /// pt の単位 (「基準クリア率からの pt」) ごと変わる。 しきい値と金の交換レートは
        /// <b>同じ fit から引き直す</b> ── itt_clear.txt の金係数がそれ。</para></summary>
        [Tooltip("true で 消耗品・武器・出目パーツ も付与プールへ入れる (ITT を全品へ広げる)")]
        public bool randomGrantAllKinds = false;
        /// <summary>付与試行のラン番号オフセット。 <b>並列プロセスごとに区間を分けるためにある</b>。
        /// runIdx = 90000 + これ + i。 0 のとき従来と完全に同じ。</summary>
        [Tooltip("付与試行の開始オフセット (並列時にプロセスごとの担当区間を分ける)")]
        public int randomGrantStartIndex = 0;
        /// <summary>プール構成のログを 1 度だけ出すためのフラグ。</summary>
        [NonSerialized] private bool _grantPoolLogged;
        [Tooltip("金の処置水準 (カンマ区切り)。 0 は常に対照として含まれる")]
        public string randomGrantGoldLevels = "10,25";
        /// <summary>付与を遅らせる計画 (層 → まだ配っていない id)。 層に到達した時点で配る。</summary>
        private readonly List<(int floor, string id)> _grantPlan = new List<(int, string)>();
        /// <summary>このランの金の処置量 (0 = 対照)。 割り当てログに残す。</summary>
        private int _grantGoldAmount;
        private int _grantGoldFloor;
        /// <summary>割り当てログ (1 行 1 ラン)。 band|gold@floor|id@floor,... </summary>
        private readonly List<string> _grantLog = new List<string>();
        /// <summary>付与抽選の専用乱数。 **GameRng を消費しない**。</summary>
        private System.Random _grantRng;

        [Header("遺物 単軸スイープ")]
        [Tooltip("true で 全 16 軸を『その軸 1 本だけ・段N』の遺物として個別に回し、軸ごとの寄与を切り出す")]
        public bool relicAxisSweep = false;
        [Tooltip("1 軸あたりのラン数")]
        public int relicAxisSweepRuns = 300;
        [Tooltip("測る段。 実測では挑戦25pt以上のメインは段8・9 が 6 割強なので 9 が実態に近い")]
        public int relicAxisSweepStep = 9;
        [Tooltip("挑戦スコア。 0 なら遺物なし基準値 (§13-5) と直接比較できる")]
        public int relicAxisSweepChallengeScore = 0;
        [Tooltip("診断モード: [基準]攻撃3 とこの軸の 2 アームだけを回す。 -1 で全軸。 jsonl から死因を追う用")]
        public int relicAxisSweepOnlyAxis = -1;
        private string _relicAxisSweepReport;

        [Header("挑戦 単軸スイープ")]
        [Tooltip("true で 挑戦デバフを『その軸 1 本だけ・段N』で個別に回し、軸ごとの重さを切り出す")]
        public bool challengeAxisSweep = false;
        [Tooltip("1 アームあたりのラン数")]
        public int challengeAxisSweepRuns = 300;
        [Tooltip("true で 各軸の最上位 Tier だけを回す (アーム数が 23 → 12 に減る)")]
        public bool challengeAxisSweepMaxTierOnly = false;
        [Tooltip("価格弾性の曲線を測る。 この倍率でアームを回す (挑戦デバフは 0pt のまま)。 空で無効")]
        // ×1.15 と ×1.35 で 7F差分が −11.0 / −11.7 とほぼ同じだった。 第一段で
        // 弾性を使い切っているのか、 それとも別の要因かを、 曲線を取って切り分ける。
        public float[] shopPriceCurve = new float[0];
        [Tooltip("アーム内の進捗を N ラン ごとに出す。 0 で無効 (アーム末尾のみ)")]
        public int challengeAxisSweepProgressEvery = 50;
        [Tooltip("決定性診断: 0pt だけの同一アームを N 本並べ、 ラン単位で指紋を突き合わせる。 0 で無効")]
        // 同一シード・同一設定のはずのアームが 4.0pt 食い違った (2026-08-10)。
        // 集計では追えないので、 **どのランが最初に分かれたか**を出す。
        public int challengeAxisSweepDeterminismArms = 0;
        [Tooltip("並列化診断: 先頭アームを直列、以後を並列にして同一シードの指紋と所要時間を比較")]
        public bool challengeAxisSweepParallelCompare = false;
        [Tooltip("診断モード: この軸の全段だけを回す。 -1 で全軸。 ChallengeCatalog.Axes の添字")]
        // 1 軸を確かめたいだけで 6900 ラン回すのは無駄 (2026-08-10 に指摘された)。
        // 基準 + その軸の段数 で済むので、 900〜1200 ラン ＝ 1〜2 分で答えが出る。
        public int challengeAxisSweepOnlyAxis = -1;
        private string _challengeAxisSweepReport;

        [Header("ビルド別勝率スイープ")]
        [Tooltip("true で 全 BuildPersona を同一条件・同一シードで回し、 5層/7層クリア率を出す")]
        public bool personaSweep = false;
        [Tooltip("1 ペルソナあたりのラン数")]
        public int personaSweepRuns = 500;
        private string _personaSweepReport;

        // 採取した5F到達ビルド（武器・ダイスは差し替えるため保持しない）
        private struct SimBuild { public int hp; public int weaponPlus; public int limitBreakStage; public List<string> passives; }
        private readonly List<SimBuild> _simBases = new List<SimBuild>();
        private bool _simHarvestArmed;
        private string _simReport;

        // ===== 集計分類 =====
        public enum Outcome { GameOver, NormalClear, FullClear, Deadlock, Crash }
        public enum DeathCause { None, CombatLoss, CombatPyrrhic, Starvation, Unknown }

        [Serializable]
        public class CombatRec
        {
            public string enemy;
            public string enemyId;
            public int floor;
            public bool isBoss;
            public bool won;
            public int turns;
            public int hpBefore;
            public int hpAfter;
            public bool afterLastStand;
            // ターン内訳（非解決グラインドの原因特定用）
            public int tWin;       // ロール勝利ターン数
            public int tDraw;      // 引き分けターン数
            public int tLoss;      // ロール敗北ターン数
            public int tLossAbs;   // うちメインダメ0（シールド吸収/無効化で死を回避）
            // 検証計測: この戦闘でプレイヤーが獲得した累計回復量／シールド量（OnBattleEnded のみ）
            public int healApplied;
            public int shieldGained;
            // L1学習: 与ダメ・被ダメ・敵maxHP
            public int damageDealt;
            public int damageTaken;
            public int enemyMaxHP;
            public bool isFightEnd; // OnBattleEnded で確定した1戦分か（チェーン途中形態は false）
            // この戦闘時点の装備（武器×ダイス勝率集計用）。武器は family_tN まで（業物+段階は区別しない）。
            public string weaponId = "";
            public string diceId = "";
            // ボス難易度オートチューナー: 敗北時の致死メカニズム (勝利時は Normal)
            public ItemSystem.PassiveSkills.DeathCause deathCause;
            // ボス難易度オートチューナー: プレイヤーロール合計と回数 (平均出目算出用)
            public long playerRollSum;
            public int playerRollCount;
            // ボス難易度オートチューナー: この戦闘の総被ダメの ソース別内訳 (支配率診断用・キル時の一撃ではない)
            public Dictionary<ItemSystem.PassiveSkills.DeathCause, int> playerDamageBySource;
            // ボス難易度オートチューナー: スタンス別の「ボスがロール勝ちしたターン数/総ターン数」(強/弱別レンジ制御用)
            public int strongRollTurns, strongRollBossWins, weakRollTurns, weakRollBossWins;
            // 緊張感曲線用: プレイヤー最大HP (戦闘終了時点)。 hpAfter / playerMaxHpEnd で残HP%
            public int playerMaxHpEnd;
        }

        [Serializable]
        public class RunRec
        {
            public int index;
            public Outcome outcome;
            public DeathCause cause = DeathCause.None;
            public int deathFloor;
            public bool deathInBossFight;
            public string fatalEnemy = "";
            public int reachedFloor = 1;
            public bool reached6F;
            public int finalHP;
            public int finalMaxHP;
            public int finalCoins;
            public int peakCoins;
            public int totalGoldGained;
            /// <summary>**このランでゲームが実際に見た挑戦スコア。**
            /// 意図した構成が本当に適用されていたかを、 結果ではなく入力側で検証するために残す。</summary>
            public int appliedChallengeScore;
            public int materialsGainedTotal; // このランで得た強化素材の累計(全源: 戦闘/イベント/ショップ/賢者の石/天工開物/メタ)
            public int starvationTotal;
            public int starvationHits;
            // 物資(ADR-0002): 最終/最低物資と発狂到達
            public int finalProvision;
            public int finalProvisionCap;
            public int minProvision = GameLoop.ProvisionRules.Max;   // 2026-09-28: 物資 ×10
            public bool reachedMadness;   // 物資0(払底)に到達したか (旧・発狂。 名前は集計の互換のため据え置き)
            // 物資の発生源別 収支（ProvisionSystem.Stats を1ラン分キャプチャ）
            public int provisionCombatLoss;
            public int provisionComposureGain;
            public int provisionLateralLoss;
            public int provisionMarchLoss;
            public int provisionEvilLoss;
            public int provisionFoodGain;
            public int provisionRerollLoss;   // ダイス振り直しコスト（#1）
            public int totalCombats;
            public int totalWins;
            public int shopPurchases;
            // 種別ごとの購入数。 **価格倍率に対する弾性が枠ごとに違う**ため、
            //   合計だけでは「価格に鈍い枠が分母を薄めている」のか
            //   「そもそも買う対象が尽きている」のかを区別できない (2026-08-10)。
            //   リロールは倍率非適用、 強化素材は 3×2^N×倍率 で log 弾性、
            //   特売品は 20〜60% 引きで値上げを吸収する ── いずれも合計に混ざる。
            public int shopBuyPassive;
            public int shopBuyConsumable;
            public int shopBuyWeapon;
            public int shopBuyDice;
            public int shopBuyMaterial;
            /// <summary>ラン中の総支出 (run.coinsSpent)。 残金 (finalCoins) と対で見る
            /// ── 残金が積み上がっているなら「買えない」ではなく「買う物が無い」。</summary>
            public int totalCoinsSpent;

            /// <summary>〈長引く負傷〉の発動回数と、 失った最大HPの累計。 **計装専用**。</summary>
            public int woundTriggers;
            public int woundHpLost;

            /// <summary>ランの**指紋**。 同一シード・同一設定なら一致するはずの値を 1 個に畳んだもの。
            ///
            /// **決定性の検査専用。** 2026-08-10、 設定が完全に同一・同一シードの 2 アームで
            /// 7層クリアが 4.0pt (300 ラン中 66 ラン) 食い違った。 集計値だけでは
            /// 「どのランがいつ分かれたか」が追えないので、 ラン単位で比較できる値を持たせる。</summary>
            public int fingerprint;
            /// <summary>公開chronicleと終端mechanical stateをSHA-256へ畳んだworker照合用digest。</summary>
            public string deterministicDigest = "";
            public int shopRerolls;
            public int shopRerollCoins;
            public int priorityItemsAcquired; // S/A 級を取得した回数
            public int tierUpgradeCount;      // 強化で Tier ID が変わった回数 (T1→T2 等、 +昇格は含まず)
            public string finalWeaponTier = ""; // 終了時の武器 Tier (例: "ドーンブリンガー")
            public int finalLimitBreak;       // 終了時の業物 lv
            public int totalTurns;
            public bool lastStandUsed;
            public int lastStandFloor;
            public int combatsAfterLastStand;
            public int winsAfterLastStand;
            public string profile = "";    // 行動ルーチン: "貪欲"(戦闘貪欲) / "回避"(戦闘回避)
            public string persona = "";    // ビルド軸ペルソナ名 (RawTier/Standard/Crit/Bleed/…)
            public string metaAxis = "";   // このランで適用したメタバフ配分 (一斉走査時の集計キー)
            public string band = "";       // R1..R10 / CRASH / DEADLOCK
            public string bandLabel = "";

            // 5F突入時点の確信チェーン状態スナップショット (-1=未到達)
            public int convictionStageAt5F = -1;
            public bool hadConvictionItem5F;       // 〈根拠のない確信〉所持
            public bool hadResolveAt5F;            // 〈決意〉所持
            public bool hadTruthAt5F;              // 〈真理〉所持
            public bool hadFlagYogenAt5F;          // 苦難の予言 所持
            public bool hadFlagKakushinAt5F;       // 苦難の確信 所持
            // 実際に起動したタイル種別ごとの回数（再訪・消化済みは含まない＝ActivateTile 初回のみ）
            public readonly Dictionary<TileType, int> tileVisits = new Dictionary<TileType, int>();

            // [廃止] 旅団契約の計装一式 (2026-08-11 にシステムごと削除)
            public string note = "";
            public List<CombatRec> combats = new List<CombatRec>();
            /// <summary>6F (灰燼の王) 撃破時点のビルド情報スナップショット。
            /// 後から「どんな装備で6Fまで到達できたか」をサルベージするための行単位プレーンテキスト。
            /// null = 6F未到達(または到達前にラン終了)。</summary>
            public string clear6FSnapshot;

            // =========================
            // L1 学習用フィールド
            // =========================
            /// <summary>このランで「過去に1度でも所持/獲得した」アイテムIDの集合（売却や消費で消えたものも含む）。</summary>
            public HashSet<string> acquiredItemsEver = new HashSet<string>();
            /// <summary>このランで「ショップ等で1度でも提示された」アイテムIDの集合。
            /// 取得有無に関わらず記録。 offeredLift = (提示されたラン群) - (提示されなかったラン群) の bandScore 差
            /// により、出現バイアスを除いた純粋寄与の参考指標となる。</summary>
            public HashSet<string> offeredItemsEver = new HashSet<string>();

            // ===== ランダム化ホールドアウト (2026-08-17) =====
            // 通常の lift は `取得群 − 未取得群` の**観測差**で、 BOT は「金がある」「順調」な
            // ときに買うので **取得したこと自体が「うまくいっている」の代理変数**になる。
            // 提示条件付け (offeredLift) と回帰で共取得は潰せるが、 方策自身の選択バイアスは残る。
            //
            // **ランダム化した決定だけを別系統で記録する。** 探索決定では足切りを通った候補から
            // 一様に選ぶので、 その候補集合の中では取得/未取得が期待値で等質になり、
            // 差がそのまま因果効果になる。 貪欲データ (方策の質) とは混ぜない。

            /// <summary>探索決定の**候補集合**に 1 度でも入った id。 不偏 lift の母集団。</summary>
            public HashSet<string> exploreOfferedEver = new HashSet<string>();
            /// <summary>探索決定で**実際に選ばれた** id。 不偏 lift の処置群。</summary>
            public HashSet<string> exploreAcquiredEver = new HashSet<string>();
            /// <summary>このランで探索決定が起きた回数 (較正用)。</summary>
            public int exploreDecisions;
            /// <summary>**探索ランか。** true なら観測 lift の集計から丸ごと外す ──
            /// 探索で買った品が「取得」として混ざると貪欲統計が汚れる。</summary>
            public bool isExploreRun;
            /// <summary>このランで実際に与えた累計ダメージ（全戦闘合算）。</summary>
            public long totalDamageDealt;
            /// <summary>このランで実際に受けた累計ダメージ（heal込みのgross）。</summary>
            public long totalDamageTaken;
            /// <summary>このランで獲得した回復量（healApplied）合算。</summary>
            public long totalHealed;
            /// <summary>このランで獲得したシールド量合算。</summary>
            public long totalShieldGained;
            /// <summary>ヴェスカ連戦で撃破した段の id 集合（boss_layer7 / _p2.._p4）。</summary>
            public HashSet<string> awakenedFormsKilled = new HashSet<string>();
            /// <summary>帯ランクの数値表記 (R1=1, R10=10, R11=11, CRASH=-1, DEADLOCK=-2)。集計用。</summary>
            public int bandScore;
            /// <summary>2026-06-22: 各層末時点の InventoryPower スナップショット (floor → power)。
            /// ラン中の戦力推移、 売買コスパ計算、 Tier別ビルド比較に使う。</summary>
            public Dictionary<int, int> inventoryPowerByFloor = new Dictionary<int, int>();
            /// <summary>ラン終了時点 (死亡/クリア) の InventoryPower。</summary>
            public int finalInventoryPower;
            /// <summary>死亡時の抱え落ち診断。消耗品は戦闘中に即使用できる種類ごとに分離。</summary>
            public int finalConsumableCount, finalHealCount, finalDamageBuffCount, finalShieldCount, finalProvisionCount;
            public List<string> finalConsumableIds = new List<string>();
            /// <summary>未使用素材と、その場で次の武器強化が可能だったか。</summary>
            public int finalMaterials, finalUpgradeCost;
            public bool finalUpgradeReady;
            /// <summary>2026-06-23: ラン終了時点の所持アイテム ID 集合 (装備+所持+昇華、 dedup)。 保持率計算用。</summary>
            public HashSet<string> finalOwnedItemIds = new HashSet<string>();
            /// <summary>L1.5: このランで実施した「eventId|choiceIndex」一覧（重複あり）。
            /// EventChoiceLearningStats が per-event-choice 集計に使う。</summary>
            public List<string> eventChoicesMade = new List<string>();
            /// <summary>L2 ペアテスト: このランで使われたポリシーバリアント。
            /// "" / "baseline" / "challenger"。 PolicyExplorer がペア diff 計算に使う。</summary>
            public string policyVariant = "";
            /// <summary>L2 ペアテスト: このランで使われたシード値 (ペア識別用)。</summary>
            public int pairedSeed;
        }

        private readonly List<RunRec> _records = new List<RunRec>();
        private RunRec _cur;
        // Queue で O(1) Dequeue するためのリングバッファ用途。RemoveAt(0) を避ける。
        private readonly Queue<string> _curLog = new Queue<string>();
        private bool _exceptionFlag;
        private string _exceptionMsg;
        private int _prevCoins;
        private int _prevMaterials;
        private object _lastResolvedEvent;
        private string _pendingEnemyName;
        private string _pendingEnemyId;
        private bool _pendingEnemyIsBoss;
        private int _pendingEnemyHpBefore;
        private int _eventStuckCount;
        private string _lastEventInfo = "";
        /// <summary>イベント選択のタイブレーク用。 **ラン開始ごとに RebuildRunRng() で作り直す** ──
        /// 決定論シード時に前ランの消費状態を持ち越すと再現性が壊れるため。</summary>
        private System.Random _rng = new System.Random();
        // 行動ルーチン分割: 前半50%=戦闘貪欲 / 後半50%=戦闘回避（航行Rankのみ差し替え）
        private bool _curCombatAverse;
        private bool _curBossNear;   // 直近のDoNavigateで判定したボス接近フラグ（休憩判断で参照）

        /// <summary>Ultra controller。 **null が既定**で、 null の間は決定経路に一切触れない
        /// （既存の全基準値を保つため）。 差すと対応済みの決定点だけが委譲される。</summary>
        [NonSerialized] public AutoTest.Ultra.IUltraDecisionSink ultraSink;
        /// <summary>現ランで Ultra の評価に費やした実時間。 番犬から差し引く
        /// (worker 待ちは「固着」ではない)。 集計には使わない診断値。</summary>
        [NonSerialized] private float _ultraThinkingSeconds;
        /// <summary>バッチ全体で Ultra の評価に費やした実時間。 サマリに出す。</summary>
        [NonSerialized] private float _ultraThinkingSecondsTotal;

        // ===== Ultra: フレームを跨ぐ決定 =====
        //
        //  worker を同期で待つとメインスレッドが数秒停車し、 Editor が 1 フレームも
        //  描画しない。 決定を「開始」と「回収」に割り、 待っている間はフレームを返す。
        /// <summary>進行中の Ultra 決定。 null でない間、 盤面を進めてはならない。</summary>
        [NonSerialized] private AutoTest.Ultra.UltraPendingDecision _ultraPending;
        /// <summary>保留中の決定が却下されたときに使う生産方策の手。
        /// **待機中に再計算してはいけない** ── 探索経路は RNG を消費し得るので、
        /// 毎フレーム引き直すと再現性が壊れる。</summary>
        [NonSerialized] private MapNode _ultraPendingProductionChoice;
        /// <summary>保留中の決定を発行した checkpoint。 回収時の commit 検証に要る。</summary>
        [NonSerialized] private AutoTest.Ultra.UltraCheckpoint _ultraPendingCheckpoint;
        [NonSerialized] private float _ultraWaitBeganRt;
        /// <summary>run ループ用: 直前の Step が worker 待ちで空回りしたか。
        /// ストール検出と反復上限から除外する ── 盤面は意図的に止まっているのであって
        /// 固着ではない。 除外し忘れると待機がそのまま DEADLOCK になる。</summary>
        [NonSerialized] private bool _ultraWaiting;
        /// <summary>ラン内で単調増加する決定機会の番号。 stale な回答を弾く鍵。</summary>
        private int _ultraEpoch;
        /// <summary>直近に生産方策へ落ちた理由。 サマリへ出して「実は一度も効いていない」を可視化する。</summary>
        private string _ultraLastFallbackReason;

        /// <summary>休憩の 2 択。 添字を直書きしないための定数
        /// （生産方策の分岐と Ultra の選択肢と適用の 3 箇所で一致している必要がある）。</summary>
        private const int RestHealIndex = 0, RestUpgradeIndex = 1;

        /// <summary>実ランの途中で checkpoint を 1 つ書き出す先。 **既定は空＝何もしない**。
        ///
        /// <para>合成した盤面では rollout のコストが測れない ── 手組みの <c>RunState</c> は
        /// 装備もダイスも既定のままで、 戦闘に入った瞬間に固着する。
        /// <c>RunState.Initialize</c> と <c>MapGenerator.Generate</c> は
        /// メタ進行のシングルトンに触るため play mode 外では呼べないので、
        /// **実ランの中で採る**しかない。</para></summary>
        [NonSerialized] public string ultraCaptureCheckpointPath = "";
        /// <summary>何回目のマップ航行で採るか。 序盤すぎると選択肢が乏しく、
        /// 遅すぎるとランが終わっている。</summary>
        [NonSerialized] public int ultraCaptureAtNavigation = 6;
        private int _ultraNavSeen;
        private bool _ultraCaptured;
        /// <summary>「戦闘フェーズなのに戦闘が動いていない」が連続した回数。 20 で 1 回だけ吠える。</summary>
        private int _combatInactiveStreak;
        /// <summary>resume の実効状態。 サマリへ出す ── ラン中の Log は
        /// <c>filterLogType=Error</c> で落ちるので、 これが唯一の記録になる。</summary>
        private string _ultraResumeLine = "";

        /// <summary>Ultra worker がラン途中から再開するための checkpoint。 **既定 null**。
        /// null の間は <see cref="RunOne"/> は従来どおり新規ランから始まる。</summary>
        [NonSerialized] public AutoTest.Ultra.UltraResumePayload ultraResumeFrom;
        /// <summary>再開直後に 1 手だけ強制する行動 id。 空なら方策が普通に選ぶ。
        /// rollout は「この手を打ったら」を測るので、 最初の 1 手だけ差し替える。</summary>
        [NonSerialized] public string ultraResumeForcedActionId;

        /// <summary>checkpoint をラン状態へ流し込む。
        ///
        /// <para><see cref="GameManager.StartNewRun"/> が正規に走った**後**に呼ぶ ──
        /// パッシブレジストリのリセットや戦闘乱数の通し番号リセットといった
        /// 「新規ランで必ず起きること」を飛ばしたくないため。 上書きするのは
        /// <c>Run</c> の中身とマップだけで、 シングルトンの初期化経路には手を入れない。</para>
        ///
        /// <para><c>Run</c> オブジェクトは差し替えず**中身を書き換える**。 他所が
        /// 参照を掴んでいても矛盾しないので、 差し替えより安全。</para></summary>
        private bool TryApplyUltraResume(GameManager gm, out string failure)
        {
            failure = null;
            var payload = ultraResumeFrom;
            if (payload == null) { failure = "payload is null"; return false; }
            if (gm?.Run == null) { failure = "run was not initialised"; return false; }

            var mm = MapManager.Instance;
            if (mm == null) { failure = "MapManager is unavailable"; return false; }

            try
            {
                payload.run.RestoreInto(gm.Run);
                mm.RestoreState(payload.map.BuildMap(), payload.map.currentNodeId);
                gm.ResumeAtPhase(GameManager.GamePhase.MapNavigation);
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": " + ex.Message;
                return false;
            }

            // **成否を必ず印字する。** 失敗は Crash で落ちるので分かるが、 成功したのに
            //   その後の挙動がおかしい場合、 復元されたのかどうかが分からないと切り分けられない。
            //   実際 1 回目の実走で「戦闘が始まらない」を追うのに、 この行が無くて詰まった。
            _ultraResumeLine = $"{gm.Run.Depth}層"
                    + $" / 現在地 {mm.CurrentNode?.id ?? "?"} (要求 {payload.map.currentNodeId})"
                    + $" / HP {gm.Run.playerHP}/{gm.Run.playerMaxHP}"
                    + $" / 武器 {gm.Run.equippedWeaponId}+{gm.Run.weaponPlus}"
                    + $" / パーツ {gm.Run.diceFaceParts?.Count ?? 0}"
                    + $" / 強制手 '{ultraResumeForcedActionId}'"
                    + $" / 合法手 {(mm.GetAvailableMoves()?.Count ?? -1)}";
            Debug.Log("[AutoRunner][Ultra] resume 完了: " + _ultraResumeLine);

            _ultraEpoch = payload.epoch;
            // 再開直後の 1 手だけを固定する sink。 以降は通常の方策が打つ ──
            //   rollout が測っているのは「この手を打った後、生産方策が最後まで打つ」なので、
            //   2 手目以降まで固定すると別の量を測ることになる。
            if (!string.IsNullOrEmpty(ultraResumeForcedActionId))
                ultraSink = new AutoTest.Ultra.UltraForcedFirstActionSink(ultraResumeForcedActionId);

            return true;
        }
        // 現戦闘のターン内訳タリー（OnEnemyEncounteredでリセット、ExecuteTurnで加算）
        private int _cwWin, _cwDraw, _cwLoss, _cwLossAbs;

        private static readonly string[] LeaveKeywords =
            { "立ち去", "去る", "無視", "帰", "やめ", "見送", "通り過ぎ", "何もしない", "断る", "拒" };

        // 危険を示唆する選択肢（生存重視のため可能なら回避）
        private static readonly string[] DangerKeywords =
            { "戦", "挑", "賭", "呪", "捧", "食らう", "奪わ", "盗", "襲", "犠牲", "毒", "燃" };

        // ============================================================
        //  ペアテスト (PolicyExplorer 経由で外部からセット)
        // ============================================================
        /// <summary>挑戦者ポリシー。 null でなければバッチを baseline/challenger 交互に実行する。</summary>
        public static PolicyParameters PairedChallengerPolicy;
        /// <summary>ベースラインポリシーのスナップショット (バッチ開始時に固定)。</summary>
        private PolicyParameters _baselinePolicySnap;
        /// <summary>
        /// Standalone production workerまたはUltra run-start plannerが固定した方策。
        /// RunBatch内のdisk reloadより後に適用し、バッチ中は同じcloneを使い続ける。
        /// nullなら従来どおりdisk上の方策を使う。
        /// </summary>
        [NonSerialized] public PolicyParameters forcedPolicyParameters;
        [NonSerialized] public bool ultraProductionWorkerMode;
        [NonSerialized] public int ultraProductionExpectedRuns;
        [NonSerialized] public string ultraProductionPolicyId = "";
        [NonSerialized] public string ultraProductionScenarioSeedVectorHash = "";
        [NonSerialized] public ulong[] ultraProductionScenarioSeeds;
        [NonSerialized] public string productionOutputRootOverride = "";

        /// <summary>r9/r10 比較の<b>並列ワーカー</b>モード。 1 プロセス = 1 アームのシード区間。
        ///
        /// <para>Ultra の production worker と違い、 合成シードもポリシー固定も使わない。
        /// 通常バッチと**同じ <c>runIdx</c> のまま**区間だけを切り出すので、
        /// 逐次スイープと digest が一致する (それが唯一の検算手段)。</para>
        ///
        /// <para>使い方は Ultra 側と同じで、 <c>runCount = 0</c> にして本体ループを空にし、
        /// このフラグで RunBatch 後段の分岐へ入る。</para></summary>
        [NonSerialized] public bool rankMarginWorkerMode;
        [NonSerialized] public string rankMarginWorkerLabel = "";
        [NonSerialized] public Dictionary<MetaProgression.MetaPanelKind, int> rankMarginWorkerSpec;
        [NonSerialized] public int rankMarginWorkerRuns;
        [NonSerialized] public int rankMarginWorkerSeedStart = RankMarginSeedBase;
        /// <summary>バッチ (RunBatch) が全工程を終えた合図。 引数はログ出力先。
        /// <b>rankMarginWorkerMode 以外のアームを並列ワーカーで回すための出口</b>。</summary>
        public event Action<string> BatchCompleted;

        public event Action<RankMarginArmTally> RankMarginArmCompleted;
        /// <summary>走行中の進捗 (完了ラン数)。 <b>完了時だけでは親の ETA が出せない</b> ──
        /// 進捗ファイルが 0 のままだと「0.0 ラン/秒 残り 0 分」になり、
        /// 停止しているのか走っているのか区別できない。</summary>
        public event Action<int> RankMarginArmProgress;

        public event Action<UltraProductionRunRecord> UltraProductionRunCompleted;
        public event Action<UltraProductionBatchRecord> UltraProductionBatchCompleted;
        private readonly List<UltraProductionRunRecord> _ultraProductionRuns =
            new List<UltraProductionRunRecord>();
        private string _lastSummaryPath = "";
        private ulong _currentUltraProductionScenarioSeed;
        /// <summary>現ランがどちらのポリシーで実行されているか。</summary>
        private string _currentRunVariant = "";
        /// <summary>現ランのシード値。 ペアテスト時は (i/2) を共有して同一マップ・同一RNGを再現。</summary>
        private int _currentRunSeed;

        void Start()
        {
            if (autoStart) Begin();
        }

        public void Begin()
        {
            if (ultraProductionWorkerMode
                && !TryValidateUltraProductionWorkerConfiguration(out string workerConfigError))
            {
                EmitUltraProductionConfigurationFailure(workerConfigError);
                return;
            }

            // 2026-07-28: 依存の向きを反転した。 旧はプロファイルが metaPattern / enableAllDebuffs を
            // 上書きしていたが、 いまは **metaBuffMode + enableAllDebuffs からプロファイルを導出**する。
            // これで「プロファイルを変えたつもりが配分も勝手に変わる」二重管理が消える。
            MetaProfileHelper.SetCurrent(metaProfile);
            Debug.Log($"[AutoRunner] ① メタバフ={metaBuffMode}"
                    + (metaBuffMode == MetaBuffMode.BuildFocused ? $"({metaBuildAxis})" : "")
                    + $" / メタデバフ全ON={enableAllDebuffs}"
                    + $" → 学習データ分離キー: {MetaProfileHelper.CurrentSuffix}");
            Debug.Log($"[AutoRunner] ② アイテム選択={itemPickMode}"
                    + (itemPickMode == ItemPickMode.BuildFocused ? $" (Tier信奉混率 {rawTierRatio:P0})" : ""));
            Debug.Log($"[AutoRunner] ③ 学習: ボスチューナー={(tuneBosses ? "ON" : "OFF")}"
                    + $" / Tier学習={(learnTier ? "ON" : "OFF")}"
                    + $" / BOT学習={(learnBotAi ? "ON" : "OFF")}");

            // 5層裏ボス遮断。 各スイープのコルーチンはこの後で自分の値へ書き戻すので、
            //   ここは「素の Run N runs も既定で遮断される」ことだけを担保する。
            GameLoop.GameManager.SuppressLayer5HiddenBoss = suppressLayer5HiddenBoss;
            if (suppressLayer5HiddenBoss)
                Debug.Log("[AutoRunner] 5層裏ボス(シュヴァリエ)を遮断 (既定)");

            ItemSystem.PassiveSkills.CombatContext.ShieldAbsorbsUnmitigable = shieldAbsorbsUnmitigable;
            ItemSystem.Shop.ShopManager.SuppressFacePartOffers = suppressFacePartOffers;
            if (suppressFacePartOffers) Debug.Log("[AutoRunner] **計測**: 出目パーツを陳列しない");

            // 探索は**学習バッチでだけ**有効にする (2026-08-17)。 測定中に劣る買い物をさせると
            //   クリア率が下がって比較にならない。 2 系統は役割が違う:
            //     ・サンプル加点 (UCB型)        … 序列の固定化を防ぐ = **方策側**
            //     ・ランダム化ホールドアウト     … 不偏な値付け       = **推定側**
            //   前者は交絡を減らさないので、 値付けの根拠にしてはいけない。
            LearnedPriorityProvider.SampleBonusEnabled = learnTier;
            exploreHoldoutRate = learnTier ? 0.10f : 0f;
            // 武器分岐への探索流し込みは 2026-09-21 削除 (複合武器の廃止で分岐が無くなった)。
            if (learnTier)
                Debug.Log($"[AutoRunner] 探索 ON: ホールドアウト {exploreHoldoutRate:P0} / サンプル加点 "
                        + $"{LearnedPriorityProvider.ExplorationWeight:F2} (n<{LearnedPriorityProvider.ExplorationSatN})");

            // 遺物なしの強制 (2026-08-17)。 **null は「無し」ではなく「触らない」**なので、
            //   明示フラグを立てた上で preset を null にする必要がある。 ラン中の付与も止める。
            if (forceNoRelic)
            {
                _pendingRelicExplicit = true;
                _pendingPresetRelic = null;
                MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
                Debug.Log("[AutoRunner] 遺物なしを強制 (基準条件)");
            }
            else if (forceTheoreticalRelic)
            {
                // 学習条件: 遺物を理論値で固定する。 ラン中の付与も止めて構成を動かさない
                //   ── 途中で遺物が増えると、 同じバッチ内で難度が変わって値付けが混ざる。
                _pendingRelicExplicit = true;
                _pendingPresetRelic = RelicPresets.Build(RelicPresets.Preset.TheoreticalBestCursed);
                MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
                Debug.Log("[AutoRunner] 遺物を理論値固定 (TheoreticalBestCursed "
                        + $"{MetaProgression.Relics.RelicRoller.TotalCap}pt/5枠+刻印)");
            }

            // 計測モード: 武器家系の乗り換え禁止。 **static はドメインリロードで消える**ので
            //   MonoBehaviour のフィールドから毎バッチ流し込む (エディタメニューから直接
            //   static へ書くと PlayMode 開始時に失われる)。
            GameLoop.Loadout.LockWeaponFamily = lockWeaponFamily;
            if (lockWeaponFamily)
                Debug.Log("[AutoRunner] 武器家系を固定 (クラス開始武器から乗り換えない・計測条件)");

            // 同じ理由 (static はドメインリロードで消える) で ITT もフィールド経由で流し込む。
            AutoTest.CritRateCensus.Reset();
            // 強盗の計装も static なのでここで 0 へ (ドメインリロードで消えるため毎バッチ必要)。
            ItemSystem.Shop.ShopManager.RobberyAttempts = 0;
            GameManager.RobberyWins = GameManager.RobberyLosses = GameManager.RobberyLootTotal = 0;
            // 罰の実体も static。 出禁と価格割増は排他 (両方掛けると二重罰になる)。
            ItemSystem.Shop.ShopManager.RobberyProvisionCost = robberyProvisionCost;
            CombatSystem.BossCombatTrace.TargetFloor = bossTraceFloor;
            if (bossTraceFloor > 0)
            {
                CombatSystem.BossCombatTrace.Label = rankMarginWorkerLabel;
                string traceRoot = string.IsNullOrWhiteSpace(productionOutputRootOverride)
                    ? "AutoRunLogs" : productionOutputRootOverride;
                CombatSystem.BossCombatTrace.OutputPath =
                    System.IO.Path.Combine(traceRoot, "boss_trace.tsv");
            }
            ItemSystem.Shop.ShopManager.RobberyBlocksShops = robberyBlocksShops;
            ItemSystem.Shop.ShopManager.RobberySurcharge = robberyBlocksShops ? 1.0f
                : (robberySurcharge > 0f ? robberySurcharge : 2.0f);
            Debug.Log(robberyBlocksShops
                ? "[AutoRunner] 強盗の罰: 出禁 (旧仕様・比較用)"
                : $"[AutoRunner] 強盗の罰: 以降のショップ価格 ×{ItemSystem.Shop.ShopManager.RobberySurcharge:F2}");
            AutoTest.LearnedPriorityProvider.UseIttBeta = useIttBeta;
            AutoTest.LearnedPriorityProvider.IttStrict = ittStrict;
            if (challengeScoreTarget > 0)
            {
                _pendingChallengeLoadout = AscensionLoop.BuildLoadoutAtScore(challengeScoreTarget);
                int resolved = MetaProgression.ChallengeResolver.Score(_pendingChallengeLoadout);
                Debug.Log($"[AutoRunner] 挑戦スコア固定: 目標 {challengeScoreTarget}pt → 解決済み {resolved}pt"
                        + $" / 帯 {MetaProfileHelper.ChallengeBand(resolved)}");
            }

            AutoTest.LearnedPriorityProvider.IttMode = ittRawPt
                ? AutoTest.LearnedPriorityProvider.IttScoreMode.RawPt
                : AutoTest.LearnedPriorityProvider.IttScoreMode.RankMap;
            if (useIttBeta && ittRawPt)
            {
                // **生 pt のときだけ しきい値の単位を差し替える。** 準パワーが band から
                //   Δクリア率 (pt) になるので、 cuts を band 単位のまま残すと
                //   「全品が絶対取得」か「全品が罠」に振り切れる。
                //   RankMap では得点分布が band のままなので触ってはいけない。
                AutoTest.LearnedPriorityProvider.PowerCuts =
                    (float[])AutoTest.LearnedPriorityProvider.PowerCutsClear.Clone();
                AutoTest.LearnedPriorityProvider.StepPerTier =
                    AutoTest.LearnedPriorityProvider.StepPerTierClear;
            }
            if (useIttBeta)
                Debug.Log($"[AutoRunner] 購入優先度 = ランダム付与 ITT / 目的関数=クリア率"
                        + $" / 載せ方={AutoTest.LearnedPriorityProvider.IttMode}"
                        + $" / cuts={string.Join("/", Array.ConvertAll(AutoTest.LearnedPriorityProvider.PowerCuts, v => v.ToString("F2")))}");
#if UNITY_EDITOR
            // 出目パーツの評価係数を掃引するための注入口。 未設定なら既定値のまま。
            AutoTest.InventoryPower.PowerCalibration =
                UnityEditor.EditorPrefs.GetFloat("AutoRun.PartCalib", AutoTest.InventoryPower.PowerCalibration);
            AutoTest.InventoryPower.TierGainPair =
                UnityEditor.EditorPrefs.GetFloat("AutoRun.PartPair", AutoTest.InventoryPower.TierGainPair);
            AutoTest.InventoryPower.TierGainTriple =
                UnityEditor.EditorPrefs.GetFloat("AutoRun.PartTriple", AutoTest.InventoryPower.TierGainTriple);
            AutoTest.InventoryPower.TierGainT4Extra =
                UnityEditor.EditorPrefs.GetFloat("AutoRun.PartT4", AutoTest.InventoryPower.TierGainT4Extra);
#endif
            if (!shieldAbsorbsUnmitigable)
                Debug.LogWarning("[AutoRunner] **対照群**: 軽減無視をシールドで肩代わりしない (製品挙動ではない)");

            // ADR-0009: 戦闘パイプラインの切替 (新旧比較スイープ用)
            CombatManager.UseMutualAttackPipeline = useMutualAttackPipeline;
            // 〈門〉の欠陥の強さ (2026-09-14 較正用)。 負なら触らない = GateFlaws の既定値。
            //   **スイープで倍率を掃くための穴。** static を上書きするので、
            //   useMutualAttackPipeline と同じ「運び漏れ」事故を起こしうる ──
            //   だから DescribeEffective に必ず並べてある。
            if (gateRepairDrainPct >= 0f)
                GameLoop.GateFlaws.RepairHpDrainPerTurnPct = gateRepairDrainPct;
            if (gateIgnitionAttackCutPct >= 0f)
                GameLoop.GateFlaws.IgnitionAttackCutPct = gateIgnitionAttackCutPct;
            if (gatePierceRate >= 0f)
                GameLoop.GateFlaws.TransferPierceRate = gatePierceRate;
            if (saberWaltzShopBias >= 0f)
                ItemSystem.Shop.ShopManager.SaberWaltzShopBias = saberWaltzShopBias;
            // 敵ステータスの上書きは **DB の読み直し**なので、 ラン開始より前にここで 1 回だけ。
            CombatSystem.EnemyDatabase.ReloadWithOverrides(enemyAttackSpec, enemyAttackMul, enemyHpSpec);
            MetaProgression.MetaBuffApplicator.EliteUpgradePctOverride = eliteUpgradePctOverride;
            if (flatAttackMul  >= 0f)
                ItemSystem.PassiveSkills.CombatContext.FlatAttackMul = flatAttackMul;
            // 較正ノブは static なのでラン跨ぎ・アーム跨ぎで残る。 **毎回既定へ戻してから乗せる。**
            GameLoop.CombatRewards.EliteRewardMul = GameLoop.CombatRewards.EliteRewardMulDefault;
            if (eliteRewardMul >= 0f) GameLoop.CombatRewards.EliteRewardMul = eliteRewardMul;
            if (eliteNavBase    >= 0f) EliteNavBase    = eliteNavBase;
            if (eliteNavHpBonus >= 0f) EliteNavHpBonus = eliteNavHpBonus;
            if (eliteDropRate   >= 0f) GameLoop.CombatRewards.EliteDropRate  = eliteDropRate;
            if (normalDropRate  >= 0f) GameLoop.CombatRewards.NormalDropRate = normalDropRate;
            if (combatGoldScale >  0)  GameLoop.CombatRewards.GoldScale      = combatGoldScale;
            if (useMutualAttackPipeline)
                Debug.Log("[AutoRunner] ADR-0009 相互攻撃パイプラインで実行 (配線=交換レート自動ポリシー)");

            // プロファイル切替で参照先サブディレクトリが変わるため、 各キャッシュを再読み込み
            try { PolicyParameters.ReloadFromDisk(MetaProfileHelper.LearningRoot()); } catch { }
            try { EventChoiceLearningStats.Reload(MetaProfileHelper.LearningRoot()); } catch { }
            try { LearnedPriorityProvider.Reload(TierLearningRoot, writeMarkdown: UpdatesTier); } catch { }
            try { BossTuning.Reload(MetaProfileHelper.LearningRoot()); } catch { }

            if (wiringSkill == WiringSkill.Ultra)
            {
                StartCoroutine(PrepareUltraAndRun());
            }
            else if (autoLoopBatches >= 2)
                StartCoroutine(RunAutoLoop());
            else
                StartCoroutine(RunBatch());
        }

        private bool TryValidateUltraProductionWorkerConfiguration(out string reason)
        {
            if (ultraProductionExpectedRuns <= 0)
            {
                reason = "expected run count must be positive";
                return false;
            }
            if (ultraProductionScenarioSeeds == null
                || ultraProductionScenarioSeeds.Length != ultraProductionExpectedRuns)
            {
                reason = "scenario seed vector length does not match expected runs";
                return false;
            }
            if (forcedPolicyParameters == null)
            {
                reason = "forced production policy is missing";
                return false;
            }
            if (string.IsNullOrEmpty(ultraProductionPolicyId)
                || string.IsNullOrEmpty(ultraProductionScenarioSeedVectorHash))
            {
                reason = "policy id or scenario seed vector hash is missing";
                return false;
            }
            if (string.IsNullOrWhiteSpace(productionOutputRootOverride))
            {
                reason = "isolated production output root is missing";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private void EmitUltraProductionConfigurationFailure(string reason)
        {
            var batch = new UltraProductionBatchRecord
            {
                policyId = ultraProductionPolicyId ?? "",
                scenarioSeedVectorHash = ultraProductionScenarioSeedVectorHash ?? "",
                runs = Array.Empty<UltraProductionRunRecord>(),
                outputPath = productionOutputRootOverride ?? "",
                artifactPath = "",
                artifactSha256 = "",
                completedNormally = false,
                failureCode = "INVALID_WORKER_CONFIGURATION: " + reason,
            };
            try { UltraProductionBatchCompleted?.Invoke(batch); } catch { }
            Debug.LogError("[AutoRunner][UltraWorker] START REJECTED: " + reason);
            if (!Application.isEditor) Application.Quit(2);
        }

        /// <summary>
        /// Apply one controller-owned synthetic seed to production GameRng. Every
        /// policy candidate receives the same (ordinal, seed, runIndex) triples.
        /// This method is worker-only; the live Ultra 10000-run batch never calls it.
        /// </summary>
        public UltraProductionScenarioBinding BeginUltraProductionScenario(
            int ordinal,
            int liveRunIndex)
        {
            if (!ultraProductionWorkerMode)
                throw new InvalidOperationException("production scenario seeds are worker-only");
            if (ultraProductionScenarioSeeds == null
                || ordinal < 0 || ordinal >= ultraProductionScenarioSeeds.Length)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            ulong seed = ultraProductionScenarioSeeds[ordinal];
            _currentUltraProductionScenarioSeed = seed;
            GameLoop.GameRng.SetMasterSeed(seed);
            GameLoop.GameRng.BeginRun(liveRunIndex);
            return new UltraProductionScenarioBinding(ordinal, liveRunIndex, seed);
        }

        private IEnumerator PrepareUltraAndRun()
        {
            _ultraProfile = UltraAutoRunProfile.CreateStandard50WithRelic10000();
            _ultraProgressSession = UltraProgressHub.Begin(new UltraProgressPlan
            {
                label = "Ultra Standard / relic / 50pt",
                totalRuns = _ultraProfile.runCount,
                workerCount = 0,
            });
            _ultraBatchActive = true;

            if (!_ultraProfile.Matches(this, out string profileReason))
            {
                FailUltraBeforeRun("runtime profile mismatch: " + profileReason);
                yield break;
            }

            if (!UltraAutoRunBridge.TryOpen(_ultraProfile, out _ultraController, out string openReason))
            {
                FailUltraBeforeRun(openReason);
                yield break;
            }

            _ultraController.BindProgress(_ultraProgressSession);
            UltraProgressHub.SetPhase(
                _ultraProgressSession,
                UltraProgressPhase.Preparing,
                1,
                "production full-run policy portfolio");

            var request = new UltraPolicyPortfolioRequest
            {
                profile = _ultraProfile,
                baselinePolicy = PolicyParameters.Current != null ? PolicyParameters.Current.Clone() : null,
                productionRunsPerCandidate = 1000,
                progressSession = _ultraProgressSession,
            };

            bool began;
            string beginReason;
            try { began = _ultraController.TryBeginPolicyPortfolio(request, out beginReason); }
            catch (Exception ex)
            {
                began = false;
                beginReason = ex.GetType().Name + ": " + ex.Message;
            }
            if (!began)
            {
                FailUltraBeforeRun("production portfolio start failed: " + beginReason);
                yield break;
            }

            const float maxPlanningSeconds = 3600f;
            float planningStarted = Time.realtimeSinceStartup;
            UltraPolicyPortfolioResult selection = null;
            while (selection == null)
            {
                bool polled;
                bool completed;
                string pollReason;
                UltraPolicyPortfolioResult result;
                try
                {
                    polled = _ultraController.TryPollPolicyPortfolio(
                        out completed, out result, out pollReason);
                }
                catch (Exception ex)
                {
                    polled = false;
                    completed = false;
                    result = null;
                    pollReason = ex.GetType().Name + ": " + ex.Message;
                }

                if (!polled)
                {
                    FailUltraBeforeRun("production portfolio polling failed: " + pollReason);
                    yield break;
                }
                if (completed)
                {
                    selection = result;
                    break;
                }
                if (Time.realtimeSinceStartup - planningStarted > maxPlanningSeconds)
                {
                    FailUltraBeforeRun("production portfolio timed out before run 1");
                    yield break;
                }

                UltraProgressHub.Heartbeat(_ultraProgressSession, "waiting for production workers");
                yield return null;
            }

            string selectionReason = "portfolio result is null";
            if (selection == null || !selection.IsUsable(out selectionReason))
            {
                FailUltraBeforeRun("production portfolio result rejected: " + selectionReason);
                yield break;
            }

            forcedPolicyParameters = selection.selectedPolicy.Clone();
            UltraPortfolioPolicySpec selectedRunPolicy = selection.selectedRunPolicy;
            _ultraSelectedBaseSkill = (WiringSkill)selectedRunPolicy.wiringSkill;
            superHighDifficultyTailMode = selectedRunPolicy.superHighDifficultyTailMode;
            superLayer6OptimalCombatRoutine = selectedRunPolicy.superLayer6OptimalCombatRoutine;
            lambdaFarmTiles = selectedRunPolicy.lambdaFarmTiles;
            UltraProgressHub.SetPhase(
                _ultraProgressSession,
                UltraProgressPhase.Executing,
                _ultraProfile.runCount,
                "selected=" + selection.selectedPolicyId
                + " coverage=" + UltraPlanningCoverage.RunStartOnly);
            Debug.Log("[AutoRunner][Ultra] production policy selected: "
                    + selection.selectedPolicyId + " / " + selection.outputPath
                    + " / coverage=RunStartOnly / " + selection.diagnostic);
            // **候補が同一挙動なら、 選択そのものが無意味なので目立たせる。**
            //   停止はしない ── 稀にしか到達しない分岐だけが違う候補は正当に高一致になりうる。
            //   ただし「何も測れていない可能性」は必ず人間の目に入れる。
            if (selection.configurationNoOpPolicyIds != null
                && selection.configurationNoOpPolicyIds.Length > 0)
                Debug.LogWarning("[AutoRunner][Ultra] "
                    + AutoTest.Ultra.UltraPortfolioSelector.DescribeNoOpCheck(
                        selection.configurationNoOpPolicyIds, selection.maxDigestAgreement)
                    + " ── この portfolio の選択結果は信用できない可能性がある。");

            yield return RunBatch();
        }

        private void FailUltraBeforeRun(string reason)
        {
            string detail = string.IsNullOrEmpty(reason) ? "unknown Ultra startup failure" : reason;
            UltraProgressHub.Fail(_ultraProgressSession, detail);
            try { _ultraController?.Cancel(detail); } catch { }
            try { _ultraController?.Dispose(); } catch { }
            _ultraController = null;
            _ultraBatchActive = false;
            Debug.LogError("[AutoRunner][Ultra] START REJECTED (0 runs): " + detail);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void OnDestroy()
        {
            if (!_ultraBatchActive) return;
            const string reason = "Ultra batch cancelled because AutoRunner was destroyed";
            UltraProgressHub.Cancel(_ultraProgressSession, reason);
            try { _ultraController?.Cancel(reason); } catch { }
            try { _ultraController?.Dispose(); } catch { }
            _ultraController = null;
            _ultraBatchActive = false;
        }

        /// <summary>
        /// 1000ラン × autoLoopBatches 回 を連続実行する自動周回モード。
        /// 各バッチ間で L1 (item_stats) と L2 (policy) が自動更新され、
        /// 次バッチはその更新後のリストとパラメータを使う。
        /// 30バッチ程度回せば policy の局所最適化が見える。
        /// </summary>
        private bool _suppressExitDuringLoop;

        private IEnumerator RunAutoLoop()
        {
            int loops = Mathf.Max(1, autoLoopBatches);
            bool finalExit = exitPlayModeWhenDone;
            _suppressExitDuringLoop = true;  // RunBatch 内の exitPlayMode を抑止
            string root = MetaProfileHelper.LearningRoot();

            // ── バッチ0回目(初期)スナップショット ──
            try { BossTuning.Reload(root); } catch { }
            try { PolicyParameters.ReloadFromDisk(root); } catch { }
            var bossStart   = SnapshotBossTuning();
            var policyStart = PolicyParameters.Current?.Clone();
            float firstClearRate = -1f, lastClearRate = -1f;

            Debug.Log($"[AutoRunner] === 自動周回モード START: {runCount}ラン × {loops}周 ===");
            for (int i = 1; i <= loops; i++)
            {
                Debug.Log($"[AutoRunner] ── 自動周回 {i}/{loops} 開始 ──");
                // **Clear は 1 バッチ 1 回だけのはず。** 2 回以上走っているなら
                //   自動周回が意図せず回っており、 スイープ途中で記録が消える。
                recordsClearedCount++;
                if (recordsClearedCount > 1)
                    Debug.LogWarning($"[AutoRunner] _records を {recordsClearedCount} 回目の Clear "
                                   + $"(autoLoopBatches={autoLoopBatches})。 スイープ中なら測定が壊れる。");
                _records.Clear();
                _detail.Clear();
                yield return RunBatch();
                float cr = ComputeFullClearRate(_records);
                if (i == 1) firstClearRate = cr;
                lastClearRate = cr;
                Debug.Log($"[AutoRunner] ── 自動周回 {i}/{loops} 終了 (7層クリア率 {cr:P2}) ──");
                yield return null;
            }
            _suppressExitDuringLoop = false;
            // 最終バッチ後に Reload を1回呼んで、 最新の item_stats を BALANCE_TIER_LIST.md に反映 (Tier更新モードのみ)
            try
            {
                LearnedPriorityProvider.Reload(root, writeMarkdown: UpdatesTier);
                AppendInventoryPowerBlocksToTierList();
            }
            catch { }
            // ── 周回サマリ: 初期 vs 最終の差分をチェンジログに追記 ──
            try { AppendAutoLoopSummary(loops, bossStart, policyStart, firstClearRate, lastClearRate); }
            catch (Exception e) { Debug.LogWarning($"[AutoRunner] 周回サマリ追記失敗: {e.Message}"); }
            Debug.Log($"[AutoRunner] === 自動周回モード END: {loops}周完了 (最終 Reload: {LearnedPriorityProvider.LastLoadedSummary}) ===");
            if (finalExit)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }

        /// <summary>_records から 7層クリア率 (bandScore>=11 / 有効ラン) を算出。</summary>
        private static float ComputeFullClearRate(List<RunRec> recs)
        {
            if (recs == null || recs.Count == 0) return 0f;
            int valid = 0, full = 0;
            foreach (var r in recs)
            {
                if (r == null || r.bandScore < 0) continue;
                valid++;
                if (r.bandScore >= 11) full++;
            }
            return valid > 0 ? (float)full / valid : 0f;
        }

        /// <summary>現在のボス調整値を {key → {ラベル → 実数値}} で複製 (具体パラメータ + HP + Dice期待値)。</summary>
        private static Dictionary<string, Dictionary<string, float>> SnapshotBossTuning()
        {
            var snap = new Dictionary<string, Dictionary<string, float>>();
            try
            {
                foreach (var k in BossTuning.All())
                {
                    var m = new Dictionary<string, float>();
                    foreach (BossParam p in System.Enum.GetValues(typeof(BossParam)))
                        m[p.ToString()] = BossTuning.GetParam(k, p);
                    m["HP"] = BossTuning.MaxHpFor(k.key);
                    if (BossTuning.IsSignatureDiceBoss(k.key))
                        m["SigE"] = BossTuning.SignatureExpected(BossTuning.SignatureFaces(k.key));
                    else
                        m["DiceE"] = BossTuning.CurrentDiceExpected(k.key);
                    snap[k.key] = m;
                }
            }
            catch { }
            return snap;
        }

        /// <summary>自動周回の初期(バッチ0)→最終(バッチN)差分を BALANCE_CHANGELOG_&lt;profile&gt;.md に追記。</summary>
        private void AppendAutoLoopSummary(int loops,
            Dictionary<string, Dictionary<string, float>> bossStart,
            PolicyParameters policyStart, float firstClear, float lastClear)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"## {BotJudgmentLog.Now()} — 🔁 自動周回サマリ ({runCount}ラン × {loops}周 / {MetaProfileHelper.CurrentSuffix})");
            sb.AppendLine();
            sb.AppendLine($"- **メタバフ**: {metaBuffMode}"
                + (metaBuffMode == MetaBuffMode.BuildFocused ? $"({metaBuildAxis})" : "")
                + $" / メタデバフ全ON: {enableAllDebuffs} / アイテム選択: {itemPickMode}");
            sb.AppendLine($"- **学習**: ボスチューナー={(tuneBosses ? "ON" : "OFF")}"
                + $" / Tier学習={(learnTier ? "ON" : "OFF")} / BOT学習={(learnBotAi ? "ON" : "OFF")}");
            if (firstClear >= 0f && lastClear >= 0f)
            {
                float d = lastClear - firstClear;
                sb.AppendLine($"- **7層クリア率**: 初回 {firstClear:P2} → 最終 {lastClear:P2} ({d:+0.0%;-0.0%})");
            }
            sb.AppendLine();

            // ボス難易度係数の差分 (変化した軸のみ)
            if (BossAutoTune)
            {
                var bossEnd = SnapshotBossTuning();
                var keys = new SortedSet<string>();
                foreach (var k in bossStart.Keys) keys.Add(k);
                foreach (var k in bossEnd.Keys) keys.Add(k);
                var lines = new List<string>();
                foreach (var key in keys)
                {
                    bossStart.TryGetValue(key, out var sa);
                    bossEnd.TryGetValue(key, out var sb2);
                    var parts = new List<string>();
                    var labels = new SortedSet<string>();
                    if (sa != null) foreach (var l in sa.Keys) labels.Add(l);
                    if (sb2 != null) foreach (var l in sb2.Keys) labels.Add(l);
                    foreach (var label in labels)
                    {
                        float av = (sa != null && sa.TryGetValue(label, out var v1)) ? v1 : 0f;
                        float bv = (sb2 != null && sb2.TryGetValue(label, out var v2)) ? v2 : 0f;
                        if (Mathf.Abs(bv - av) <= 0.05f) continue;
                        if (label == "DiceE")
                        {
                            var (cnt, faces) = BossTuning.BestDiceConfig(bv > 0 ? bv : 1f);
                            parts.Add($"Dice E{av:F1}→**E{bv:F1}({cnt}d{faces})**");
                        }
                        else if (label == "SigE")
                            parts.Add($"固有面 E{av:F1}→**E{bv:F1}**");
                        else if (label == "HP")
                            parts.Add($"HP {av:F0}→**{bv:F0}**");
                        else
                            parts.Add($"{label} {av:0.#}→**{bv:0.#}**");
                    }
                    if (parts.Count > 0) lines.Add($"| `{key}` | {string.Join(", ", parts)} |");
                }
                sb.AppendLine("### ボス難易度パラメータ (変化した実数値のみ)");
                sb.AppendLine();
                if (lines.Count == 0) sb.AppendLine("*(変化なし)*");
                else
                {
                    sb.AppendLine("| ボス | 変化したパラメータ |");
                    sb.AppendLine("|---|---|");
                    foreach (var l in lines) sb.AppendLine(l);
                }
                sb.AppendLine();
            }

            // policy 差分
            if (UpdatesAi && policyStart != null)
            {
                var p = PolicyParameters.Current;
                var lines = new List<string>();
                void Diff(string name, object a, object b) { if (!Equals(a, b)) lines.Add($"| {name} | `{a}` → `{b}` |"); }
                Diff("rerollCostRatio", policyStart.rerollCostRatio, p.rerollCostRatio);
                Diff("consumableStockMax", policyStart.consumableStockMax, p.consumableStockMax);
                Diff("robberyMinHpRatio", policyStart.robberyMinHpRatio, p.robberyMinHpRatio);
                Diff("eventExplorationRate", policyStart.eventExplorationRate, p.eventExplorationRate);
                Diff("importantThreatThreshold", policyStart.importantThreatThreshold, p.importantThreatThreshold);
                Diff("emergencyHealRatio", policyStart.emergencyHealRatio, p.emergencyHealRatio);
                Diff("hpCritThreshold", policyStart.hpCritThreshold, p.hpCritThreshold);
                sb.AppendLine("### AIポリシー (policy.json)");
                sb.AppendLine();
                if (lines.Count == 0) sb.AppendLine("*(変化なし)*");
                else
                {
                    sb.AppendLine("| パラメータ | 初期 → 最終 |");
                    sb.AppendLine("|---|---|");
                    foreach (var l in lines) sb.AppendLine(l);
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            BotJudgmentLog.Append(sb.ToString());
        }

        /// <summary>Editor コンソールを強制クリア + 大規模 GC + Unity未参照リソース解放。
        /// バッチ後の RAM 占有 (Editor LogEntries が 20GB+ 蓄積するため) を即座に解放する。</summary>
        private static void ClearEditorConsoleAndCollect()
        {
            try
            {
#if UNITY_EDITOR
                // UnityEditor.LogEntries.Clear() をリフレクション経由で呼ぶ (Editor only API)
                var asm = System.Reflection.Assembly.GetAssembly(typeof(UnityEditor.Editor));
                var t = asm?.GetType("UnityEditor.LogEntries");
                var m = t?.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                m?.Invoke(null, null);
#endif
            }
            catch (Exception e) { Debug.LogWarning($"[AutoRunner] ConsoleClear失敗: {e.Message}"); }
            try
            {
                Resources.UnloadUnusedAssets();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch (Exception e) { Debug.LogWarning($"[AutoRunner] GC失敗: {e.Message}"); }
        }

        private IEnumerator RunBatch()
        {
            _ultraProductionRuns.Clear();
            _lastSummaryPath = "";
            ResetBatchStatics();
            Application.logMessageReceived += OnLog;
            float prevScale = Time.timeScale;
            Time.timeScale = Mathf.Max(1f, batchTimeScale);
            // VSync解除でフレームレート上限を外す
            int prevVSync = QualitySettings.vSyncCount;
            int prevTargetFps = Application.targetFrameRate;
            if (disableVSyncDuringBatch)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }

            // RAM爆発防止: Debug.Log を完全抑止 + StackTrace 無効化。
            //   1ラン400Log × StackTrace 5KB × 10K = 20GB+ になるため必須。
            //   summary.txt / detail.log ファイル出力には影響しない (Editor コンソール側のみ抑止)。
            bool prevLogEnabled = Debug.unityLogger.logEnabled;
            var prevFilter    = Debug.unityLogger.filterLogType;
            var prevStackLog  = Application.GetStackTraceLogType(LogType.Log);
            var prevStackWarn = Application.GetStackTraceLogType(LogType.Warning);
            var prevStackErr  = Application.GetStackTraceLogType(LogType.Error);
            if (suppressLogsDuringBatch)
            {
                // **logEnabled = false にしてはいけない** (2026-08-11)。 全部止まるので
                //   Application.logMessageReceived が一切呼ばれず、 <see cref="OnLog"/> が
                //   死ぬ ── つまり **例外が握り潰される**。 コルーチン内で例外が飛ぶと
                //   そのコルーチンだけが黙って死に、 番犬も反復上限もストール検出も
                //   **同じコルーチンの中にいるので一緒に死ぬ**。 バッチは進捗ゼロのまま
                //   永久に「実行中」に見える (実際に T4 スイープが arm 2 で消えた)。
                //   filterLogType なら Log/Warning だけを落として例外は通せる。
                Debug.unityLogger.logEnabled = true;
                Debug.unityLogger.filterLogType = LogType.Error;
                Application.SetStackTraceLogType(LogType.Log,     StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Error,   StackTraceLogType.None);
            }

            // DB / 演出の事前準備
            SafeInitDatabases();
            DisableMapTransition();

            // GameManager 出現待ち
            float waited = 0f;
            while (GameManager.Instance == null && waited < 10f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (GameManager.Instance == null)
            {
                // Log抑止を一時解除してエラーを確実に出す
                if (suppressLogsDuringBatch) Debug.unityLogger.logEnabled = true;
                Debug.LogError("[AutoRunner] GameManager が見つかりません。SampleScene で実行してください。");
                Application.logMessageReceived -= OnLog;
                Time.timeScale = prevScale;
                if (disableVSyncDuringBatch) { QualitySettings.vSyncCount = prevVSync; Application.targetFrameRate = prevTargetFps; }
                if (suppressLogsDuringBatch)
                {
                    Debug.unityLogger.logEnabled = prevLogEnabled;
                    Debug.unityLogger.filterLogType = prevFilter;
                    Application.SetStackTraceLogType(LogType.Log,     prevStackLog);
                    Application.SetStackTraceLogType(LogType.Warning, prevStackWarn);
                    Application.SetStackTraceLogType(LogType.Error,   prevStackErr);
                }
                yield break;
            }

            // L1学習データを読み込み、 動的 PriorityItemList を構築。
            //  BOTは常に Tier を読む必要がある (S/A/B 判定) ので Reload 自体は常に実行。
            //  ただし MD 再生成は Tier更新モードのみ (AIルーチン学習モードでは Tier表凍結)。
            LearnedPriorityProvider.Reload(TierLearningRoot, writeMarkdown: UpdatesTier);
            Debug.Log($"[AutoRunner] {LearnedPriorityProvider.LastLoadedSummary}");
            // L1.5: イベント学習リフトを読み込み
            EventChoiceLearningStats.Reload();
            // L2: policy.json を読み込み (前バッチの摂動結果を引き継ぐ)
            PolicyParameters.ReloadFromDisk();
            if (forcedPolicyParameters != null)
            {
                PolicyParameters.SetCurrent(forcedPolicyParameters.Clone());
                Debug.Log("[AutoRunner][Ultra] production portfolioで選択した方策をreload後に固定適用");
            }
            // [較正専用] 危険度の上書き。 static なのでアーム跨ぎで残る ── 毎回既定へ戻してから乗せる。
            BlockCreditRatio = 0.7f; SafetyFights = 2.0f; GoldComfort = 60f; DangerFloor = 0.25f;
            if (navDangerFloor  >= 0f) DangerFloor      = navDangerFloor;
            MetaProgression.MetaPanel.SlayerEnabled = !valorSlayerOff;
            SaleBonusHiPct = 60; SaleBonusLoPct = 40;
            ItemSystem.Shop.ShopManager.RerollKeepsSale = rerollKeepsSale;
            ItemSystem.Shop.ShopManager.ItemPriceAdd = itemPriceAdd;
            ItemSystem.Shop.ShopManager.RerollHardCap =
                rerollHardCap == -1 ? BotRerollCapPerShop
              : rerollHardCap <= -2 ? -1
              : rerollHardCap;
            ItemSystem.Shop.ShopInventory.RerollPriceScale = rerollPriceScale;
            // 停止則。 static なのでアーム跨ぎで残る ── 毎回既定へ戻してから乗せる。
            RerollStopRule = rerollStopRule;
            Phase3BeforeReroll = phase3BeforeReroll;
            ItemSystem.Shop.ShopInventory.RerollCurveFirst = rerollCurveFirst;
            ItemSystem.Shop.ShopInventory.RerollCurveStep  = rerollCurveStep;
            RerollShelfValue = 0.15f; RerollPriorWeight = 2f;
            if (rerollShelfValue  >= 0f) RerollShelfValue  = rerollShelfValue;
            if (rerollPriorWeight >= 0f) RerollPriorWeight = rerollPriorWeight;
            if (saleBonusHiPct >= 0) SaleBonusHiPct = saleBonusHiPct;
            if (saleBonusLoPct >= 0) SaleBonusLoPct = saleBonusLoPct;
            if (navGoldComfort  >  0f) GoldComfort      = navGoldComfort;
            UseTileDanger = navTileDanger;
            UseEliteCostNav = navEliteCost;
            if (navBlockCredit  >= 0f) BlockCreditRatio = navBlockCredit;
            if (navSafetyFights >= 0f) SafetyFights     = navSafetyFights;
            Debug.Log($"[AutoRunner] policy: {PolicyParameters.Current.Summary()}");
            // 攻撃/防御スタンス(ADR-0006): 学習閾値を CombatSystem.PlayerStance へ結線（Current を常時読む）。
            // ＝CombatSystem は AutoTest に依存せず、BOT が学習値を注入。実プレイヤー時は未結線で既定値。
            CombatSystem.PlayerStance.DefendWinProbProvider = () => PolicyParameters.Current.stanceDefendWinProb;
            CombatSystem.PlayerStance.DefendHpBiasProvider  = () => PolicyParameters.Current.stanceDefendHpBias;
            // L3: ボス難易度係数を読み込み (前バッチの自動調整を引き継ぐ)
            BossTuning.Reload();
            if (BossAutoTune) Debug.Log($"[AutoRunner] bossTuning: {BossTuning.Summary()}");
            // L2ペアテスト: 挑戦者ポリシーを生成 (バッチ内 paired diff 評価用)
            //  ・AIルーチン学習モード時のみ (TierOnly では policy 凍結)
            //  ・runCount が L2ゲート(200) 未満ならスキップ
            //  ・偶数バッチである必要 (runCountを偶数にする)
            if (UpdatesAi && runCount >= PolicyExplorer.MinBatchForL2 && runCount % 2 == 0)
            {
                PolicyExplorer.PrepareChallenger();
            }
            _baselinePolicySnap = PolicyParameters.Current.Clone();
            if (PairedChallengerPolicy != null)
                Debug.Log($"[AutoRunner] ペアテスト ON: 挑戦者ポリシー = {PairedChallengerPolicy.Summary()}");
            // サンプル信頼性警告
            if (runCount < PolicyExplorer.MinBatchForL2)
                Debug.LogWarning($"[AutoRunner] runCount={runCount} < {PolicyExplorer.MinBatchForL2}: L2自動更新スキップ (policy 不変)");
            else if (runCount < 500)
                Debug.LogWarning($"[AutoRunner] runCount={runCount}: SEM大きめ。 1000以上推奨");

            var gm = GameManager.Instance;
            gm.OnEnemyEncountered += OnEnemyEncountered;
            gm.OnBattleEnded += OnBattleEnded;
            gm.OnStarvationDamage += OnStarvation;
            gm.OnTileActivated += OnTileActivated;
            // 武器強化で Tier が上がるたび新Tier ID を acquiredItemsEver に追記 (L1学習の集計漏れ修正)
            GameManager.OnWeaponTierUpgraded += OnWeaponTierUpgraded;

            // どのメタ/遺物/挑戦プロファイルでも必ず表示する。各スイープは開始時に
            // 自分の総ラン数で再初期化するが、通常バッチにも共通の進捗を持たせる。
            ResetScreenProgress(Mathf.Max(1, runCount), "通常バッチ");

            // パッシブ取得の経路別計装をバッチ単位で数え直す (前バッチの値を持ち越さない)。
            ItemSystem.Helpers.PassiveSourceAudit.Reset();
            ItemSystem.PassiveSkills.Effects.MirrorTwinsResponse.ResetStats();

            // **マスターシードはスイープ経路にも適用する** (2026-08-05)。
            //   従来は下の通常バッチ分岐の中でだけ設定していたため、 5Fボス スイープ等は
            //   常に「決定論シード: なし」で走り、 区分間が同一シード比較になっていなかった。
            //   固定難易度/遺物スイープは通常バッチの後段で走るので影響を受けていない。
            if (GameLoop.GameRng.TryParseSeed(masterSeed, out var _msAll))
                GameLoop.GameRng.SetMasterSeed(_msAll);
            else
                GameLoop.GameRng.ClearSeed();

            if (simBoss5Sweep)
            {
                Debug.Log($"[AutoRunner] 5Fボス勝率スイープ開始");
                yield return RunBoss5Sweep(gm);
            }
            else if (challengeFixedSweep)
            {
                // 固定難易度スイープは後段で必要本数をすべて走らせる。
                // Launch(1) の器として渡された通常1ランを先に実行すると、0pt RawTier の
                // 深層ボスを Super AI が数十分探索して、本計測へ入る前に止まって見える。
                Debug.Log("[AutoRunner] 固定難易度スイープ: 通常バッチの先行1ランを省略");
            }
            else if (IsTierCalibration)
            {
                yield return RunTierCalibration();
            }
            else
            {
                bool paired = PairedChallengerPolicy != null;
                Debug.Log($"[AutoRunner] バッチ開始: {runCount} ラン (ペアテスト={(paired ? "ON" : "OFF")})");
                // ペア時間隔: 2連続を同シードで実行 (i, i+1) → 偶数=baseline, 奇数=challenger
                // 決定論シード: 設定されていればマスターシードを適用する。
                // ラン i のシードは GameRng が hash(master, i) で派生させる (順序非依存)。
                if (GameLoop.GameRng.TryParseSeed(masterSeed, out var _ms))
                {
                    GameLoop.GameRng.SetMasterSeed(_ms);
                    Debug.Log($"[AutoRunner] 決定論シード ON: {GameLoop.GameRng.FormatSeed(_ms)}");
                }
                else
                {
                    GameLoop.GameRng.ClearSeed();
                    if (!string.IsNullOrEmpty(masterSeed))
                        Debug.LogWarning($"[AutoRunner] シード '{masterSeed}' を解釈できず — ランダム動作");
                }

                for (int i = 0; i < runCount; i++)
                {
                    curRunInArm = i + 1;
                    GameLoop.GameRng.BeginRun(i);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", i));
                    if (paired)
                    {
                        // 同じペアシードを (i/2) で共有
                        _currentRunSeed = 0xC0FFEE ^ (i / 2);
                        bool isChallenger = (i % 2 == 1);
                        _currentRunVariant = isChallenger ? "challenger" : "baseline";
                        // ポリシーをスワップ
                        PolicyParameters.SetCurrent(isChallenger ? PairedChallengerPolicy : _baselinePolicySnap);
                        // シード適用 (UnityEngine.Random と System.Random 両方)
                        UnityEngine.Random.InitState(_currentRunSeed);
                    }
                    else
                    {
                        _currentRunSeed = 0;
                        _currentRunVariant = "";
                    }
                    yield return RunOne(i);
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0)
                        yield return null;
                }
                if (paired)
                    PolicyParameters.SetCurrent(_baselinePolicySnap); // 戻す
            }

            // 周回モード: 通常バッチの後段で、 ペルソナごとに独立したラインを走らせる。
            if (ascensionMode)
                yield return RunAscension();

            // 遺物プリセットスイープ: 遺物の強さだけを振って深度が動くかを見る。
            if (relicPresetSweep)
                yield return RunRelicPresetSweep();

            // 遺物 単軸スイープ: 軸ごとの寄与を 1 本ずつ切り出す。
            if (relicAxisSweep)
                yield return RunRelicAxisSweep();

            // アイテム アブレーション: 同一シードで「開幕付与あり/なし」の対を作る。
            if (metaAxisAblation)
                yield return RunMetaAxisAblation();

            if (keystoneSweep)
                yield return RunKeystoneSweep();

            if (rankMarginSweep)
                yield return RunRankMarginSweep();

            if (rankMarginWorkerMode)
                yield return RunRankMarginWorkerArm();

            if (yieldBenchmark)
                yield return RunYieldBenchmark();

            if (itemAblationSweep)
                yield return RunItemAblationSweep();

            // ランダム付与試行: 1 ランに複数品を配り、割り当てだけで回帰する (ITT)。
            if (randomGrantTrial)
                yield return RunRandomGrantTrial();

            // ビルド別勝率スイープ: ペルソナだけを振って 5層/7層クリア率を比べる。
            if (personaSweep)
                yield return RunPersonaSweep();

            // 固定難易度スイープ: 「難易度 N で 7 層をクリアできるか」に直接答える。
            if (challengeFixedSweep)
                yield return RunChallengeFixedSweep();

            // 挑戦 単軸スイープ: 段ごとの重さを 1 つずつ切り出す。
            if (challengeAxisSweep)
                yield return RunChallengeAxisSweep();

            // 技量帯の実測: 配線方策だけを振って、 同一シードのペア比較で差を出す。
            if (wiringSkillCompare)
                yield return RunWiringSkillCompare();

            gm.OnEnemyEncountered -= OnEnemyEncountered;
            gm.OnBattleEnded -= OnBattleEnded;
            GameManager.OnWeaponTierUpgraded -= OnWeaponTierUpgraded;
            gm.OnStarvationDamage -= OnStarvation;
            gm.OnTileActivated -= OnTileActivated;
            Application.logMessageReceived -= OnLog;
            Time.timeScale = prevScale;
            if (disableVSyncDuringBatch) { QualitySettings.vSyncCount = prevVSync; Application.targetFrameRate = prevTargetFps; }

            // ログ抑止/StackTrace 設定を復元
            if (suppressLogsDuringBatch)
            {
                Debug.unityLogger.logEnabled = prevLogEnabled;
                Debug.unityLogger.filterLogType = prevFilter;
                Application.SetStackTraceLogType(LogType.Log,     prevStackLog);
                Application.SetStackTraceLogType(LogType.Warning, prevStackWarn);
                Application.SetStackTraceLogType(LogType.Error,   prevStackErr);
            }

            // Editor コンソール強制クリア + GC: 抑止しても少量蓄積 + メモリ即時解放
            if (clearConsoleAfterBatch) ClearEditorConsoleAndCollect();

            string dir = _tierCalibrationLogsWritten ? _tierCalibrationLastDir : WriteLogs();
            if (simBoss5Sweep && !string.IsNullOrEmpty(_simReport))
            {
                try { File.WriteAllText(Path.Combine(dir, "sim_boss5_winrate.txt"), _simReport, new UTF8Encoding(false)); }
                catch (Exception ex) { Debug.LogWarning($"[AutoRunner] sim出力失敗: {ex.Message}"); }
            }
            if (challengeFixedSweep && !string.IsNullOrEmpty(_challengeSweepReport))
            {
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(dir) ?? dir,
                        $"chalsweep_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), _challengeSweepReport, new UTF8Encoding(false));
                }
                catch (Exception ex) { Debug.LogWarning($"[AutoRunner] 難易度スイープ出力失敗: {ex.Message}"); }
            }
            if (wiringSkillCompare && !string.IsNullOrEmpty(_skillSweepReport))
            {
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(dir) ?? dir,
                        $"skillsweep_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), _skillSweepReport, new UTF8Encoding(false));
                }
                catch (Exception ex) { Debug.LogWarning($"[AutoRunner] 技量スイープ出力失敗: {ex.Message}"); }
            }
            if (challengeAxisSweep && !string.IsNullOrEmpty(_challengeAxisSweepReport))
            {
                try
                {
                    File.WriteAllText(Path.Combine(dir, "challenge_axis_sweep.txt"), _challengeAxisSweepReport, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(dir) ?? dir,
                        $"chalaxis_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), _challengeAxisSweepReport, new UTF8Encoding(false));
                }
                catch (Exception ex) { Debug.LogWarning($"[AutoRunner] 挑戦単軸スイープ出力失敗: {ex.Message}"); }
            }
            if (relicPresetSweep && !string.IsNullOrEmpty(_relicSweepReport))
            {
                try
                {
                    File.WriteAllText(Path.Combine(dir, "relic_preset_sweep.txt"), _relicSweepReport, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(dir) ?? dir,
                        $"relicsweep_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), _relicSweepReport, new UTF8Encoding(false));
                }
                catch (Exception ex) { Debug.LogWarning($"[AutoRunner] 遺物スイープ出力失敗: {ex.Message}"); }
            }
            if (randomGrantTrial && _grantLog.Count > 0)
            {
                // 割り当てログ。 **学習ディレクトリには置かない** ── ランダム付与のランを
                //   BOT の学習へ混ぜると、 買っていないものを「買った」として積む。
                try
                {
                    string gp = Path.Combine(Path.GetDirectoryName(dir) ?? dir, "grant_trial");
                    Directory.CreateDirectory(gp);
                    // **帯ごとにファイルを分ける (2026-09-10)。** 0pt で強い品と高難易度で要る品は
                    //   違うので (`bot_band_*` を分けた理由と同じ)、 追記式の 1 ファイルへ混ぜると
                    //   ラン数の多い帯が序列を支配する。 band_0 は既存ファイル名を維持する。
                    int chalScore = CurrentChallengeScore();
                    string band = MetaProfileHelper.ChallengeBand(chalScore);
                    string csvName = band == "band_0" ? "grant_runs.csv" : $"grant_runs_{band}.csv";
                    // **設計が違うログを同じファイルへ追記しない (2026-09-17)。**
                    //   全種プールは列の語彙も 1 品あたりの付与確率も違う。 追記式なので、
                    //   混ぜると「旧行では消耗品が一度も処置されていない」状態の回帰になり、
                    //   切片と基準クリア率が 2 つの設計の混合になる。 別名にして物理的に分ける。
                    if (randomGrantAllKinds) csvName = "allkinds_" + csvName;
                    File.AppendAllLines(Path.Combine(gp, csvName), _grantLog, new UTF8Encoding(false));
                    Debug.Log($"[AutoRunner] ランダム付与 {_grantLog.Count} 行を追記: {gp}/{csvName}"
                            + $" (挑戦 {chalScore}pt / 帯 {band})");
                    // **ITT を推定して学習ディレクトリへ書く (2026-09-09)。**
                    //   grant_runs.csv 自体は学習へ混ぜない (上のコメントどおり) が、
                    //   そこから出した**割り当てベースの係数**は交絡していないので、
                    //   BOT の購入優先度に載せてよい ── 載せる先は GrantItt が書く
                    //   itt_effects.json で、 item_stats.json とは別ファイル。
                    string gcsv = Path.Combine(gp, csvName);
                    // 出力先も帯別 ── `LearnedPriorityProvider.Reload` は
                    //   `BotLearningRoot(挑戦pt)` を渡されるので、 そこに置けば自動で読まれる。
                    string groot = MetaProfileHelper.BotLearningRoot(chalScore);
                    Directory.CreateDirectory(groot);
                    if (GrantItt.Recompute(gcsv, groot))          // band 線形 (診断用)
                        GrantItt.RecomputeSurvival(gcsv, groot);  // クリア率 (本番の序列)
                }
                catch (Exception ex) { Debug.LogWarning($"[AutoRunner] 付与ログ出力失敗: {ex.Message}"); }
            }
            if ((metaAxisAblation || keystoneSweep || rankMarginSweep || yieldBenchmark)
                && !string.IsNullOrEmpty(_metaAblationReport))
            {
                try
                {
                    File.WriteAllText(Path.Combine(dir, "meta_axis_ablation.txt"), _metaAblationReport, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(dir) ?? dir,
                        $"metaablation_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), _metaAblationReport, new UTF8Encoding(false));
                }
                catch (Exception ex) { Debug.LogWarning($"[AutoRunner] パネルAblation出力失敗: {ex.Message}"); }
            }
            if (itemAblationSweep && !string.IsNullOrEmpty(_itemAblationReport))
            {
                try
                {
                    File.WriteAllText(Path.Combine(dir, "item_ablation.txt"), _itemAblationReport, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(dir) ?? dir,
                        $"itemablation_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), _itemAblationReport, new UTF8Encoding(false));
                }
                catch (Exception ex) { Debug.LogWarning($"[AutoRunner] アブレーション出力失敗: {ex.Message}"); }
            }
            if (relicAxisSweep && !string.IsNullOrEmpty(_relicAxisSweepReport))
            {
                try
                {
                    File.WriteAllText(Path.Combine(dir, "relic_axis_sweep.txt"), _relicAxisSweepReport, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(dir) ?? dir,
                        $"axissweep_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), _relicAxisSweepReport, new UTF8Encoding(false));
                }
                catch (Exception ex) { Debug.LogWarning($"[AutoRunner] 単軸スイープ出力失敗: {ex.Message}"); }
            }
            if (personaSweep && !string.IsNullOrEmpty(_personaSweepReport))
            {
                try
                {
                    File.WriteAllText(Path.Combine(dir, "persona_sweep.txt"), _personaSweepReport, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(dir) ?? dir,
                        $"personasweep_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), _personaSweepReport, new UTF8Encoding(false));
                }
                catch (Exception ex) { Debug.LogWarning($"[AutoRunner] ビルド別スイープ出力失敗: {ex.Message}"); }
            }
            EmitUltraProductionBatchCompletion(dir);
            CompleteUltraBatchIfActive("production batch completed / output=" + dir);
            Debug.Log($"[AutoRunner] バッチ完了。ログ出力先:\n{dir}");
            // 並列ワーカー用の終了フック (2026-09-17)。 rankMarginWorkerMode 以外の
            //   アーム (付与試行など) をワーカーで回すとき、 **プロセスを畳む合図がなかった**。
            try { BatchCompleted?.Invoke(dir); } catch { }
            // BOT 方策の上限はゲーム側の static に立てているので、 バッチを抜けたら外す
            //   (同じ Editor セッションで人間が遊ぶと 2 回で止まってしまう)。
            ItemSystem.Shop.ShopManager.RerollHardCap = -1;

            if (exitPlayModeWhenDone && !_suppressExitDuringLoop)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }

        /// <summary>固定難易度スイープ。 **「難易度 N で 7 層をクリアできるか」に直接答える。**
        ///
        /// 周回モードのラチェットは探索挙動なので、 「BOT が 16 で止まる」は
        /// **難易度の壁**なのか **探索方策の限界**なのかを区別できない。 ここでは
        /// 挑戦スコアを固定値で置き、 **理論最良の遺物 (29pt+刻印) を持たせた状態**で
        /// 到達層とクリア率を測る。 これが「現実的に可能か」の上限になる。</summary>
        private void OnGUI()
        {
            if (!showRunProgressGui) return;

            if (_screenPanelStyle == null)
            {
                _screenPanelStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(12, 12, 10, 10)
                };
                _screenTextStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15,
                    normal = { textColor = new Color(0.25f, 1f, 0.35f) }
                };
                _screenAlertStyle = new GUIStyle(_screenTextStyle)
                {
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.25f, 1f, 0.35f) }
                };
            }

            int total = Mathf.Max(1, _screenTotalRuns);
            int done = Mathf.Clamp(_screenCompletedRuns, 0, total);
            float progressPercent = done * 100f / total;
            int filled = Mathf.Clamp(Mathf.FloorToInt(done * 10f / total), 0, 10);
            string bar = new string('\u25A0', filled) + new string('\u25A1', 10 - filled);
            float now = Time.realtimeSinceStartup;
            float elapsed = Mathf.Max(0f, now - _screenSweepStartRt);
            float eta = done > 0 ? elapsed * (total - done) / done : 0f;
            float lastAge = Mathf.Max(0f, now - _screenLastCompletionRt);
            var gm = GameManager.Instance;
            bool runNull = gm == null || gm.Run == null;
            string phase = gm != null ? gm.CurrentPhase.ToString() : "NO GameManager";

            float width = Mathf.Min(720f, Mathf.Max(420f, Screen.width - 20f));
            float height = 104f + _screenRunLog.Count * 20f;
            var panel = new Rect(10f, 10f, width, height);
            GUI.Box(panel, GUIContent.none, _screenPanelStyle);
            float x = panel.x + 12f;
            float y = panel.y + 9f;
            GUI.Label(new Rect(x, y, width - 24f, 24f),
                $"[{bar}]  {progressPercent:F1}%  elapsed {elapsed:F1}s  ETA {eta:F1}s", _screenTextStyle);
            y += 23f;
            GUI.Label(new Rect(x, y, width - 24f, 24f),
                $"arm={curArmIndex}/{curArmCount}  run={curRunInArm}/{curRunsPerArm}  phase={phase}  heartbeat={heartbeat}  last {lastAge:F1}s", _screenTextStyle);
            y += 23f;
            GUI.Label(new Rect(x, y, width - 24f, 24f),
                runNull ? "Run=NULL  (check for stall)" : $"Run=OK  floor={gm.Run.Depth}  HP={gm.Run.playerHP}/{gm.Run.playerMaxHP}",
                runNull ? _screenAlertStyle : _screenTextStyle);
            y += 24f;
            for (int i = 0; i < _screenRunLog.Count; i++, y += 20f)
                GUI.Label(new Rect(x, y, width - 24f, 20f), _screenRunLog[i], _screenTextStyle);
        }

        private void ResetScreenProgress(int totalRuns, string armLabel)
        {
            showRunProgressGui = true;
            _screenCompletedRuns = 0;
            _screenTotalRuns = Mathf.Max(1, totalRuns);
            _screenRunLog.Clear();
            _screenSweepStartRt = Time.realtimeSinceStartup;
            _screenLastCompletionRt = _screenSweepStartRt;
            curArmIndex = 1;
            curArmCount = 1;
            curRunInArm = 0;
            curRunsPerArm = _screenTotalRuns;
            curArmLabel = armLabel ?? "";
        }

        private void RecordScreenRunCompletion(RunRec rec)
        {
            if (!showRunProgressGui) return;
            _screenCompletedRuns++;
            _screenLastCompletionRt = Time.realtimeSinceStartup;
            float pct = _screenCompletedRuns * 100f / Mathf.Max(1, _screenTotalRuns);
            string outcome = rec != null ? rec.outcome.ToString() : "NO RECORD";
            int floor = rec != null ? rec.reachedFloor : -1;
            string label = string.IsNullOrEmpty(curArmLabel) ? "run" : curArmLabel;
            _screenRunLog.Add($"{DateTime.Now:HH:mm:ss}  {pct,5:F1}%  {label}  floor={floor}  {outcome}");
            if (_screenRunLog.Count > 20) _screenRunLog.RemoveAt(0);
        }

        /// <summary>挑戦デバフ構成の文字列を解釈して適用する。 書式は <see cref="challengeSpec"/> 参照。
        /// 未知の軸名 / 存在しない Tier は警告して読み飛ばす (バッチを止めない)。</summary>
        private static void ApplyChallengeSpec(MetaProgression.MetaProgressManager mgr, string spec)
        {
            if (mgr == null) return;
            mgr.ClearChallenge();
            foreach (var raw in spec.Split(','))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                int colon = part.IndexOf(':');
                if (colon <= 0) { Debug.LogWarning($"[挑戦] 書式不正: '{part}'"); continue; }
                string key = part.Substring(0, colon).Trim();
                string val = part.Substring(colon + 1).Trim();

                if (key == "T4")
                {
                    var t4 = MetaProgression.ChallengeCatalog.T4s.Find(d => d.displayName == val);
                    if (t4 == null) { Debug.LogWarning($"[挑戦] 未知の T4: '{val}'"); continue; }
                    mgr.SetChallengeT4(t4.t4, true);
                    continue;
                }

                var def = MetaProgression.ChallengeCatalog.Axes.Find(d => d.displayName == key);
                if (def == null) { Debug.LogWarning($"[挑戦] 未知の軸: '{key}'"); continue; }
                if (!int.TryParse(val, out int tier) || !def.HasTier(tier))
                { Debug.LogWarning($"[挑戦] {key} に Tier {val} は存在しない"); continue; }
                mgr.SetChallengeTier(def.axis, tier);
            }
            Debug.Log($"[挑戦] {mgr.State.Challenge.Describe()}");
        }
    }
}

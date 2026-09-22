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
using InventorySystem;
using InventorySystem.Shop;
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
    public class AutoRunner : MonoBehaviour
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
        /// **false は対照群専用**。 <see cref="InventorySystem.PassiveSkills.CombatContext.ShieldAbsorbsUnmitigable"/> を見ること。</summary>
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

        [Header("Λ層 ファーム量スイープ")]
        [Tooltip("true で『Λ層を固定Nマス周回してから離脱』を lambdaFarmSweepValues の各値で runCount ラン回し、ファーム量別の勝率を採取")]
        public bool lambdaFarmSweep = false;
        [Tooltip("Λ層で離脱(中央踏破)前に周回する目標マス数。スイープ中は各値で上書きされる")]
        public int lambdaFarmTiles = 6;
        [Tooltip("Λ撤退条件。 -2=安全(lv2×2) / -1=現在(lv2×4) / "
               + "-3=リスキー((lv2×6かつlv3×2) or lv3×4)。 正の値は固定踏破マス数 (旧挙動)")]
        // 3 区分とも「背負ったデバフの重さ」で規定する (2026-08-08)。
        // 絶対マス数だと付与間隔を変えた瞬間に区分の意味が壊れる。
        // 全域走査が要るときだけ { 3,6,9,12,16,18,21,24,27,30 } に戻す。
        public int[] lambdaFarmSweepValues = { -2, -1, -3 };

        [Tooltip("Λ撤退を『次の付与でどれかが lv3 になる確率』のしきい値で決める。 "
               + "0〜1 で有効 (既定 0.25)。 こちらが lambdaFarmTiles より優先。 負値で無効")]
        // **踏んでから降りるのでは遅い** (2026-09-20)。 lv3 は重いので、 引いた後の本数ではなく
        //   <b>次の一歩で lv3 を踏む確率</b>だけを BOT に渡す。
        //   指標は LambdaDebuffEffects.NextLv3Chance (= lv2 の数 / lv3 未満の数)。
        //
        // **0.25 は平坦域の中央** (10,000 ラン × 5 点)。 0.14 / 0.25 / 0.40 は
        //   14.06 / 14.09 / 13.69% で互いに区別できず (差の標準誤差 0.49pt)、
        //   有意なのは 0.55 以降の落ち込みだけ (−1.44pt, z≈2.9)。 Λ は
        //   「好きなだけ潜れるが 0.55 が限界」という形で、 支配戦略が無い。
        //   旧規則 (lv2 の本数で降りる) は lambdaFarmTiles の負値として退避してある。
        public float lambdaLv3Risk = 0.25f;
        private string _lambdaSweepReport;

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
            var ctx = InventorySystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            int dice = ctx != null && ctx.playerDice != null && ctx.playerDice.Length > 0
                     ? ctx.playerDice.Length : 5;
            float mean = 3.5f;
            if (ctx?.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0)
            {
                long sum = 0;
                for (int i = 0; i < ctx.equippedDiceFaces.Length; i++) sum += ctx.equippedDiceFaces[i];
                mean = sum / (float)ctx.equippedDiceFaces.Length;
            }
            return RunPowerBudget.Gap(run, run.currentFloor, superAI.DmgMul, dice, mean);
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
        /// <para>切り分け用。 強盗は 3 つのコストを同時に払う ── 価格 ×2 / 希望 −10 /
        /// エリート戦 (HP1840・被ダメ50%軽減)。 極点が負けているとき、
        /// <b>どれが効いているかは 1 つずつ外さないと分からない</b>。
        /// 1.0 にすれば罰だけが消え、 残りは希望と戦闘の負担になる。</para></summary>
        public float robberySurcharge = 0f;

        /// <summary>強盗を<b>ラン最後の店でだけ</b>撃つ (旧方策)。 既定 false = 6 層以降で撃つ。
        ///
        /// <para>切り分け用。 「報酬が機能していない」のか「撃つタイミングが悪い」のかは、
        /// <b>同じビルド・同じ戦闘経路でタイミングだけを変えないと</b>分からない。
        /// 旧測定の +0.68pt は戦闘経路が違う上に発動率が 4.4 分の 1 で、 比較に使えない。</para></summary>
        public bool robberyFinalShopOnly = false;

        /// <summary>強盗の希望コストの上書き。 <b>-1 = 触らない (既定 10)。</b>
        /// 切り分け用 ── 罰 (価格×2) は外しても 0.14pt しか動かなかったので、
        /// 残る「戻らない資源」は希望だけ。 0 にして 6 層ボス突破率が戻れば希望が原因。</summary>
        public int robberyHopeCost = -1;

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
        /// <summary>〈門〉③ 希望 40 を払うか。 <b>既定 true</b>。
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
        /// <see cref="InventorySystem.Shop.ShopManager.RerollHardCap"/> 経由で全 6 経路に効く。</summary>
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
        /// <see cref="InventorySystem.Shop.ShopInventory.RerollPriceScale"/>。</summary>
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

        /// <summary>〈門〉で希望 40 を払うつもりのランが、その 40 を<b>取り置く</b>ようになる層。
        /// 0 で無効。 既定 6 = 6 層に入った時点から。
        ///
        /// <para><b>なぜ要るか。</b> 素の方策は希望を「使い切ってよい資源」として扱う。
        /// そこへ 40 の支払いを足すと、 実測で 7 層突入時の平均 86 から 46 へ落ちる ──
        /// これは <see cref="GameLoop.HopeSystem.FloorPessimism"/> (45) の 1 上で、
        /// <c>UpdateCapLock</c> が<b>希望上限を 45 に恒久ロック</b>する境界のすぐ際。
        /// つまり「払う」を選ぶだけで帯が落ち、 二度と戻らない。
        /// これでは測っているのが<b>代償の重さ</b>ではなく<b>取り置きを知らない方策の損</b>になる
        /// (規則と方策はセット)。</para>
        ///
        /// <para><b>6 層からにしてある。</b> 1 層から効かせると寄り道の判断が全ラン変わり、
        /// 「門の代償」ではなく「迂回方策」を測ることになる。 6/7 層は一本道 (横移動なし・
        /// 6 層は戦闘 1 回) なので、 実質は<b>希望回復の使用と購入が早まるだけ</b>で、
        /// 道中の経路は動かない。</para></summary>
        public int gateHopeReserveFromFloor = 6;

        /// <summary>いま取り置くべき希望。 払わないアーム・素通りアーム・門より手前の層では 0。</summary>
        private int GateHopeReserve()
        {
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null || gateHopeReserveFromFloor <= 0) return 0;
            if (gateBotSkipsAll || !gateBotPaysTransfer) return 0;
            // 7 層を越えたら門は解決済み。 取り置く理由が無い。
            if (run.currentFloor < gateHopeReserveFromFloor || run.currentFloor > 7) return 0;
            return GameLoop.GameManager.GateHopeDemand;
        }

        /// <summary>希望回復を使う/買う閾値。 取り置きぶんだけ底上げする。</summary>
        private float HopeRefillFloorEffective()
            => AutoTest.PolicyParameters.Current.hopeRefillFloor + GateHopeReserve();

        /// <summary>寄り道を許す希望の下限。 取り置きぶんだけ底上げする。
        /// (6/7 層は一本道なので実効は無いが、 <b>規則を 1 か所に閉じる</b>ために揃えておく。)</summary>
        private float LateralHopeFloorEffective()
            => AutoTest.PolicyParameters.Current.lateralHopeFloor + GateHopeReserve();

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
            public InventorySystem.PassiveSkills.DeathCause deathCause;
            // ボス難易度オートチューナー: プレイヤーロール合計と回数 (平均出目算出用)
            public long playerRollSum;
            public int playerRollCount;
            // ボス難易度オートチューナー: この戦闘の総被ダメの ソース別内訳 (支配率診断用・キル時の一撃ではない)
            public Dictionary<InventorySystem.PassiveSkills.DeathCause, int> playerDamageBySource;
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
            /// <summary>Λ層 (§14-1) で死んだか。 Λ 滞在中は currentFloor が 5 のままなので、
            /// これが無いと 5F の死亡率に Λ の周回死が混ざって 5 層自体の難度が読めない。</summary>
            public bool deathInLambda;
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
            // 希望(ADR-0002): 最終/最低希望と発狂到達
            public int finalHope;
            public int finalHopeCap;
            public int minHope = 100;
            public bool reachedMadness;   // 希望0(発狂)に到達したか
            // 希望の発生源別 収支（HopeSystem.Stats を1ラン分キャプチャ）
            public int hopeCombatLoss;
            public int hopeComposureGain;
            public int hopeLateralLoss;
            public int hopeMarchLoss;
            public int hopeEvilLoss;
            public int hopeFoodGain;
            public int hopeRerollLoss;   // ダイス振り直しコスト（#1）
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

            /// <summary>Λスイープ: このランで使用した目標ファームマス数（非スイープ時は0）。</summary>
            public int lambdaFarmTilesUsed;
            /// <summary>このランでΛ層へ突入したか。</summary>
            public bool enteredLambda;
            /// <summary>Λ層滞在中に獲得したゴールド量（離脱時 or Λ内死亡時に確定）。</summary>
            public int lambdaGoldGained;
            /// <summary>2026-06-22: Λ で実際に追加されたアイテム総数。</summary>
            public int lambdaItemsAcquiredGross;
            /// <summary>**計装 (2026-08-08)**: Λ で最後に戦利品を引いた時点の抽選候補残数。
            /// All = 重複排除・除外フィルタ後 / Floored = さらに深度連動レア度下限を掛けた数。
            /// 枯渇が原因なら踏破が伸びるほど 0 に近づく。 減らないなら原因は取得機会側。</summary>
            public int lambdaPoolRemainingAll = -1;
            public int lambdaPoolRemainingFloored = -1;
            /// <summary>**計装**: Λ環状線マスの解決内訳 (踏破→マス起動→エリート/イベント→アイテム抽選)。
            /// どの段で機会が失われているかを特定する。</summary>
            public int lambdaRingsEntered;
            public int lambdaRingElite;
            public int lambdaRingEvent;
            public int lambdaRingEventHeal;
            public int lambdaRingEventItem;
            /// <summary>Λ層滞在中に獲得したアイテム数（ownedPassiveItems 増分）。</summary>
            public int lambdaItemsGained;
            /// <summary>このランで実際にΛ層で踏破したマス数(=次元の乱れ最終値)。</summary>
            public int lambdaTilesFarmed;
            /// <summary>このランで獲得したΛデバフの段階合計(段階1〜3の総和)。</summary>
            public int lambdaDebuffLevelSum;

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
            public int finalConsumableCount, finalHealCount, finalDamageBuffCount, finalShieldCount, finalHopeCount;
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
                mm.RestoreState(
                    payload.map.BuildMap(), payload.map.currentNodeId,
                    payload.map.hungerCurrent, payload.map.hungerMax);
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
            _ultraResumeLine = $"{gm.Run.currentFloor}層"
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
        private int _lambdaNavSteps; // このランのΛ走破ステップ数（無限周回防止の安全弁。RunOneでリセット）
        private int _lambdaEntryCoins;    // Λ突入時の所持ゴールド（ファーム獲得量の基準）
        private int _lambdaEntryPassives; // Λ突入時の ownedPassiveItems 数
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

            InventorySystem.PassiveSkills.CombatContext.ShieldAbsorbsUnmitigable = shieldAbsorbsUnmitigable;
            InventorySystem.Shop.ShopManager.SuppressFacePartOffers = suppressFacePartOffers;
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
            InventorySystem.Shop.ShopManager.RobberyAttempts = 0;
            GameManager.RobberyWins = GameManager.RobberyLosses = GameManager.RobberyLootTotal = 0;
            // 罰の実体も static。 出禁と価格割増は排他 (両方掛けると二重罰になる)。
            InventorySystem.Shop.ShopManager.RobberyHopeCost = robberyHopeCost;
            CombatSystem.BossCombatTrace.TargetFloor = bossTraceFloor;
            if (bossTraceFloor > 0)
            {
                CombatSystem.BossCombatTrace.Label = rankMarginWorkerLabel;
                string traceRoot = string.IsNullOrWhiteSpace(productionOutputRootOverride)
                    ? "AutoRunLogs" : productionOutputRootOverride;
                CombatSystem.BossCombatTrace.OutputPath =
                    System.IO.Path.Combine(traceRoot, "boss_trace.tsv");
            }
            InventorySystem.Shop.ShopManager.RobberyBlocksShops = robberyBlocksShops;
            InventorySystem.Shop.ShopManager.RobberySurcharge = robberyBlocksShops ? 1.0f
                : (robberySurcharge > 0f ? robberySurcharge : 2.0f);
            Debug.Log(robberyBlocksShops
                ? "[AutoRunner] 強盗の罰: 出禁 (旧仕様・比較用)"
                : $"[AutoRunner] 強盗の罰: 以降のショップ価格 ×{InventorySystem.Shop.ShopManager.RobberySurcharge:F2}");
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
            AutoTest.InventoryPower.TierGainT1 =
                UnityEditor.EditorPrefs.GetFloat("AutoRun.PartT1", AutoTest.InventoryPower.TierGainT1);
            AutoTest.InventoryPower.TierGainT3 =
                UnityEditor.EditorPrefs.GetFloat("AutoRun.PartT3", AutoTest.InventoryPower.TierGainT3);
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
                InventorySystem.Shop.ShopManager.SaberWaltzShopBias = saberWaltzShopBias;
            // 敵ステータスの上書きは **DB の読み直し**なので、 ラン開始より前にここで 1 回だけ。
            CombatSystem.EnemyDatabase.ReloadWithOverrides(enemyAttackSpec, enemyAttackMul, enemyHpSpec);
            MetaProgression.MetaBuffApplicator.EliteUpgradePctOverride = eliteUpgradePctOverride;
            if (flatAttackMul  >= 0f)
                InventorySystem.PassiveSkills.CombatContext.FlatAttackMul = flatAttackMul;
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
            InventorySystem.Shop.ShopManager.RerollKeepsSale = rerollKeepsSale;
            InventorySystem.Shop.ShopManager.ItemPriceAdd = itemPriceAdd;
            InventorySystem.Shop.ShopManager.RerollHardCap =
                rerollHardCap == -1 ? BotRerollCapPerShop
              : rerollHardCap <= -2 ? -1
              : rerollHardCap;
            InventorySystem.Shop.ShopInventory.RerollPriceScale = rerollPriceScale;
            // 停止則。 static なのでアーム跨ぎで残る ── 毎回既定へ戻してから乗せる。
            RerollStopRule = rerollStopRule;
            Phase3BeforeReroll = phase3BeforeReroll;
            InventorySystem.Shop.ShopInventory.RerollCurveFirst = rerollCurveFirst;
            InventorySystem.Shop.ShopInventory.RerollCurveStep  = rerollCurveStep;
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
            InventorySystem.Helpers.PassiveSourceAudit.Reset();
            InventorySystem.PassiveSkills.Effects.MirrorTwinsResponse.ResetStats();

            // **マスターシードはスイープ経路にも適用する** (2026-08-05)。
            //   従来は下の通常バッチ分岐の中でだけ設定していたため、 Λ/5Fボス スイープは
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
            else if (lambdaFarmSweep)
            {
                Debug.Log($"[AutoRunner] Λファーム量スイープ開始");
                yield return RunLambdaFarmSweep(gm);
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
            if (lambdaFarmSweep && !string.IsNullOrEmpty(_lambdaSweepReport))
            {
                try { File.WriteAllText(Path.Combine(dir, "lambda_farm_sweep.txt"), _lambdaSweepReport, new UTF8Encoding(false)); }
                catch (Exception ex) { Debug.LogWarning($"[AutoRunner] Λスイープ出力失敗: {ex.Message}"); }
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
            InventorySystem.Shop.ShopManager.RerollHardCap = -1;

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
                runNull ? "Run=NULL  (check for stall)" : $"Run=OK  floor={gm.Run.currentFloor}  HP={gm.Run.playerHP}/{gm.Run.playerMaxHP}",
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

        /// <summary>
        /// Item Tier を挑戦難易度ごとに独立更新する。戦闘方策は Optimal、取得ペルソナは
        /// Standard に固定し、メタ遺物は通常バッチと同じ状態を使う。各スコアは同じ run index
        /// を使うため、masterSeed 指定時は 0/30/50pt を同一シードで比較できる。
        /// </summary>
        private IEnumerator RunTierCalibration()
        {
            var prevPickMode = itemPickMode;
            var prevSkill = wiringSkill;
            bool prevLock = _ascensionPersonaLock;
            var prevChallenge = _pendingChallengeLoadout;

            itemPickMode = ItemPickMode.BuildFocused;
            wiringSkill = WiringSkill.Optimal;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;
            _tierCalibrationLogsWritten = false;
            int runs = Mathf.Max(1, tierCalibrationRuns);

            try
            {
                for (int scoreIndex = 0; scoreIndex < tierCalibrationScores.Length; scoreIndex++)
                {
                    int requestedScore = tierCalibrationScores[scoreIndex];
                    _activeTierCalibrationScore = Mathf.Max(0, requestedScore);
                    var loadout = AscensionLoop.BuildLoadoutAtScore(_activeTierCalibrationScore);
                    int actualScore = MetaProgression.ChallengeResolver.Score(loadout);
                    _activeTierCalibrationScore = actualScore;
                    _pendingChallengeLoadout = loadout;
                    _currentPersona = BuildPersona.Standard;

                    string learningRoot = TierLearningRoot;
                    Directory.CreateDirectory(learningRoot);
                    int batches = tierCalibrationBatchCounts != null
                               && tierCalibrationBatchCounts.Length == tierCalibrationScores.Length
                        ? Mathf.Max(1, tierCalibrationBatchCounts[scoreIndex])
                        : Mathf.Max(1, tierCalibrationBatches);
                    for (int batch = 0; batch < batches; batch++)
                    {
                        _records.Clear();
                        _detail.Clear();
                        // 前セットまでのTierを次セットのStandard判断へ反映する。
                        LearnedPriorityProvider.Reload(learningRoot, writeMarkdown: false);
                        ResetScreenProgress(runs, $"Item Tier {actualScore}pt set {batch + 1}/{batches}");
                        Debug.Log($"[AutoRunner] Item Tier更新 START: {actualScore}pt / Standard / Optimal / "
                                + $"set {batch + 1}/{batches} / {runs}ラン / {learningRoot}");

                        for (int i = 0; i < runs; i++)
                        {
                            curRunInArm = i + 1;
                            int runIndex = batch * runs + i;
                            GameLoop.GameRng.BeginRun(runIndex);
                            if (GameLoop.GameRng.IsSeeded)
                                _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIndex));
                            _currentPersona = BuildPersona.Standard;
                            yield return RunOne(runIndex);
                            if ((i + 1) % Mathf.Max(1, runsPerYield) == 0)
                                yield return null;
                        }

                        _tierCalibrationLastDir = WriteLogs();
                        _tierCalibrationLogsWritten = true;
                        Debug.Log($"[AutoRunner] Item Tier更新 END: {actualScore}pt / set {batch + 1}/{batches} "
                                + $"/ {_records.Count}ラン / {_tierCalibrationLastDir}");
                        yield return null;
                    }
                }
            }
            finally
            {
                _pendingChallengeLoadout = prevChallenge;
                _ascensionPersonaLock = prevLock;
                itemPickMode = prevPickMode;
                wiringSkill = prevSkill;
                _activeTierCalibrationScore = -1;
            }
        }

        private void EmitUltraProductionBatchCompletion(string outputDirectory)
        {
            if (!ultraProductionWorkerMode && UltraProductionBatchCompleted == null) return;

            int expected = ultraProductionExpectedRuns > 0
                ? ultraProductionExpectedRuns
                : _ultraProductionRuns.Count;
            bool countMatches = _ultraProductionRuns.Count == expected;
            var batch = new UltraProductionBatchRecord
            {
                policyId = ultraProductionPolicyId ?? "",
                scenarioSeedVectorHash = ultraProductionScenarioSeedVectorHash ?? "",
                runs = _ultraProductionRuns.ToArray(),
                outputPath = outputDirectory ?? "",
                artifactPath = _lastSummaryPath ?? "",
                artifactSha256 = Sha256File(_lastSummaryPath),
                completedNormally = countMatches,
                failureCode = countMatches ? "" : "RUN_COUNT_MISMATCH",
            };

            try { UltraProductionBatchCompleted?.Invoke(batch); }
            catch (Exception ex)
            {
                batch.completedNormally = false;
                batch.failureCode = "COMPLETION_CALLBACK_FAILED";
                Debug.LogError("[AutoRunner][UltraWorker] batch completion callback failed: " + ex.Message);
            }

            if (ultraProductionWorkerMode && !Application.isEditor)
                Application.Quit(batch.completedNormally ? 0 : 2);
        }

        private IEnumerator RunChallengeFixedSweep()
        {
            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;
            _pendingRelicExplicit = true;
            // 既定は理論最良遺物。 forceNoRelic (⑥) を立てると**遺物なし**で同じ挑戦ptを測る。
            //   遺物の寄与を挑戦ptと分離して読むための対照アーム (2026-08-17)。
            _pendingPresetRelic = forceNoRelic
                ? null : RelicPresets.Build(RelicPresets.Preset.TheoreticalBestCursed);
            // 5層裏ボスは BOT のレイピア運で 5層の難度を二分するため、 挑戦スコアの効きを
            // 測る上では純粋な分散源。 基準値測定と同じく遮断する (2026-08-05)。
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = true;
            bool prevBD = GameLoop.GameManager.GrantBladeDanceOnFloor5Clear;
            GameLoop.GameManager.GrantBladeDanceOnFloor5Clear = challengeSweepGrantBladeDance;

            int runs = Mathf.Max(1, challengeSweepRuns);
            ResetScreenProgress(challengeSweepScores.Length * runs, "固定難易度");
            curArmCount = challengeSweepScores.Length;
            curRunsPerArm = runs;
            string liveProgressPath = Path.Combine(ResolveAutoRunOutputRoot(), "challenge_sweep_progress.txt");
            float sweepStartRt = Time.realtimeSinceStartup;
            var sb = new StringBuilder();
            sb.AppendLine("=== 固定難易度スイープ ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"配線AI  : {wiringSkill}");
            sb.AppendLine($"ペルソナ Standard 固定 / 遺物 "
                        + (forceNoRelic ? "**なし** (対照アーム)"
                           : $"TheoreticalBestCursed ({MetaProgression.Relics.RelicRoller.TotalCap}pt/5枠+刻印) 固定"));
            sb.AppendLine($"1 段階 {runs} ラン / 決定論シード: 0000 0000 0000 0000");
            sb.AppendLine($"※ 全軸最高 Tier = {MetaProgression.ChallengeCatalog.BaseMaxScore}pt "
                        + $"(T4 込みの満点は {MetaProgression.ChallengeCatalog.TotalMaxScore}pt)");
            sb.AppendLine();
            sb.AppendLine("挑戦pt(実) | 平均到達層 | 5層クリア | 7層クリア | 到達層分布 1F〜7F");

            for (int si = 0; si < challengeSweepScores.Length; si++)
            {
                var loadout = AscensionLoop.BuildLoadoutAtScore(challengeSweepScores[si]);
                int actual = MetaProgression.ChallengeResolver.Score(loadout);
                _pendingChallengeLoadout = loadout;
                curArmIndex = si + 1;
                curArmLabel = $"{actual}pt/{wiringSkill}";

                var reach = new int[9];
                int c5 = 0, c7 = 0; double sumFloor = 0;
                for (int i = 0; i < runs; i++)
                {
                    curRunInArm = i + 1;
                    // **BeginRun を必ず呼ぶ。** メインのバッチループと同じ前処理で、
                    //   これが無いと GameRng の counters が clear されず run 塩も derive されない。
                    //   結果、 ラン間で乱数列が引き継がれて挙動が壊れる (2026-08-04 修正)。
                    int runIdx = 10000 + si * runs + i;
                    if (_ultraBatchActive)
                    {
                        bool beganRun;
                        string beginRunReason;
                        try { beganRun = _ultraController.TryBeginRun(runIdx, out beginRunReason); }
                        catch (Exception ex)
                        {
                            beganRun = false;
                            beginRunReason = ex.GetType().Name + ": " + ex.Message;
                        }
                        if (!beganRun)
                        {
                            FailUltraDuringBatch("run " + runIdx + " rejected: " + beginRunReason);
                            RestoreChallengeFixedSweepState(prevSuppress, prevPickMode, prevHidden5, prevBD);
                            yield break;
                        }
                    }
                    int productionOrdinal = si * runs + i;
                    if (ultraProductionWorkerMode)
                    {
                        BeginUltraProductionScenario(productionOrdinal, runIdx);
                    }
                    else
                    {
                        _currentUltraProductionScenarioSeed = 0UL;
                        GameLoop.GameRng.BeginRun(runIdx);
                    }
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    int cf = died ? reached - 1 : reached;
                    reach[Mathf.Clamp(reached, 0, 8)]++; sumFloor += reached;
                    if (cf >= 5) c5++;
                    if (cf >= 7) c7++;
                    if (_ultraBatchActive)
                    {
                        int completedRuns = si * runs + i + 1;
                        string runSummary = "run=" + runIdx
                            + " floor=" + reached
                            + " outcome=" + (rec != null ? rec.outcome.ToString() : "NO_RECORD");
                        UltraProgressHub.ReportRunCompleted(
                            _ultraProgressSession, completedRuns, runSummary);
                        try { _ultraController.CompleteRun(runIdx, runSummary); }
                        catch (Exception ex)
                        {
                            FailUltraDuringBatch("run completion callback failed: "
                                + ex.GetType().Name + ": " + ex.Message);
                            RestoreChallengeFixedSweepState(prevSuppress, prevPickMode, prevHidden5, prevBD);
                            yield break;
                        }
                    }
                    if ((i + 1) == 1 || (i + 1) % 10 == 0 || (i + 1) == runs)
                    {
                        try
                        {
                            float elapsed = Time.realtimeSinceStartup - sweepStartRt;
                            float rate = elapsed > 0f ? (i + 1) / elapsed : 0f;
                            float eta = rate > 0f ? (runs - i - 1) / rate : 0f;
                            File.WriteAllText(liveProgressPath,
                                $"updated={DateTime.Now:yyyy-MM-dd HH:mm:ss}\n"
                              + $"skill={wiringSkill}\nscore={actual}\n"
                              + $"arm={si + 1}/{challengeSweepScores.Length}\n"
                              + $"runs={i + 1}/{runs}\nrecords={_records.Count}\n"
                              + $"elapsedSeconds={elapsed:F1}\netaSeconds={eta:F1}\n"
                              + $"heartbeat={heartbeat}\nphase={lastPhaseSeen}\n"
                              + $"superWiringDecisions={superAI.wiringDecisions}\n"
                              + $"superRerollDecisions={superAI.rerollDecisions}\n"
                              + $"superRolloutEvaluations={superAI.rolloutEvaluations}\n",
                                new UTF8Encoding(false));
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[AutoRunner] 進捗ファイル出力失敗: {ex.Message}");
                        }
                    }
                    // Let OnGUI repaint after every completed run. This also makes a
                    // frozen last-completed line immediately visible during long batches.
                    yield return null;
                }
                var dist = new StringBuilder();
                for (int f = 1; f <= 7; f++) dist.Append($"{reach[f] * 100.0 / runs,5:F1}%");
                sb.AppendLine($"{actual,10} | {sumFloor / runs,10:F2} | {c5 * 100.0 / runs,8:F1}% | "
                            + $"{c7 * 100.0 / runs,8:F1}% |{dist}");
            }
            sb.AppendLine();
            sb.AppendLine("※ 7層クリアが 0% になる地点が『理論上の壁』。 BOT の探索限界とは別物。");

            // ── 診断セクション (軸単独 + 組み合わせ) ──
            //   状態汚染バグ (共有 EnemyData の破壊) を追うために足した足場で、 階段測定の
            //   1.25 倍のラン数を食う。 バグは 2026-08-04 に解消したので **既定 OFF**。
            //   軸ごとの重さを測り直したくなったら true に戻す。
            if (!challengeSweepDiagnostics)
            {
                _challengeSweepReport = sb.ToString();
                // **末尾の後始末をここでも行う。** yield break で抜けると下の復元に到達せず、
                // SuppressRelicGrant / itemPickMode / 裏ボス遮断が立ちっぱなしで
                // 後続のバッチへ漏れる (2026-08-05 修正)。
                RestoreChallengeFixedSweepState(prevSuppress, prevPickMode, prevHidden5, prevBD);
                yield break;
            }

            // ── 軸ごとの単独試験 (plan §6: 特定 1 軸だけでクリアが消滅しないこと) ──
            //   累積スイープだけでは「どの軸が壊しているか」が分からない。 1 軸ずつ T3 で回す。
            int isoRuns = Mathf.Max(50, runs / 2);
            sb.AppendLine();
            sb.AppendLine($"=== 軸ごとの単独試験 (その軸だけ T3 = 3pt / 各 {isoRuns} ラン) ===");
            sb.AppendLine("軸                 | 平均到達層 | 5層クリア | 1F終了");
            for (int ai = 0; ai < MetaProgression.ChallengeCatalog.Axes.Count; ai++)
            {
                var def = MetaProgression.ChallengeCatalog.Axes[ai];
                var lo = new MetaProgression.ChallengeLoadout();
                lo.SetTier(def.axis, 3);
                _pendingChallengeLoadout = lo;

                int c5 = 0, died1F = 0; double sumFloor = 0;
                for (int i = 0; i < isoRuns; i++)
                {
                    int runIdx = 90000 + ai * isoRuns + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    sumFloor += reached;
                    if (reached <= 1) died1F++;
                    if ((died ? reached - 1 : reached) >= 5) c5++;
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                sb.AppendLine($"{def.displayName,-18} | {sumFloor / isoRuns,10:F2} | "
                            + $"{c5 * 100.0 / isoRuns,8:F1}% | {died1F * 100.0 / isoRuns,5:F1}%");
            }
            sb.AppendLine("※ 0pt の 5層クリア率と比べて突出して低い軸が犯人。");

            // ── 組み合わせ試験 ──
            //   「軸単独は健全なのに累積スイープでは崩壊する」現象が、
            //   **組み合わせ固有**なのか **スイープ位置依存 (状態汚染)** なのかを切り分ける。
            //   単独試験と同じ index 帯・同じ手順で、 手組みの組み合わせを回す。
            var combos = new List<(string name, MetaProgression.ChallengeAxis[] axes, int tier)>
            {
                ("空 (対照群 0pt)",       new MetaProgression.ChallengeAxis[0], 0),
                ("A負傷 単独T1",          new[]{ MetaProgression.ChallengeAxis.長引く負傷 }, 1),
                ("C不器用 単独T1",        new[]{ MetaProgression.ChallengeAxis.綻び }, 1),
                ("E絶望 単独T1",          new[]{ MetaProgression.ChallengeAxis.絶望的な戦闘 }, 1),
                ("5軸すべてT1",           new[]{ MetaProgression.ChallengeAxis.長引く負傷, MetaProgression.ChallengeAxis.厚い皮膚,
                                                 MetaProgression.ChallengeAxis.綻び, MetaProgression.ChallengeAxis.搾取経済,
                                                 MetaProgression.ChallengeAxis.絶望的な戦闘 }, 1),
            };
            sb.AppendLine();
            sb.AppendLine($"=== 組み合わせ試験 (単独試験と同じ index 帯 / 各 {isoRuns} ラン) ===");
            sb.AppendLine("構成                 | 平均到達層 | 5層クリア | 1F終了");
            for (int ci = 0; ci < combos.Count; ci++)
            {
                var lo = new MetaProgression.ChallengeLoadout();
                foreach (var ax in combos[ci].axes) lo.SetTier(ax, combos[ci].tier);
                _pendingChallengeLoadout = lo;

                int c5 = 0, died1F = 0; double sumFloor = 0;
                for (int i = 0; i < isoRuns; i++)
                {
                    int runIdx = 95000 + ci * isoRuns + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    sumFloor += reached;
                    if (reached <= 1) died1F++;
                    if ((died ? reached - 1 : reached) >= 5) c5++;
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                sb.AppendLine($"{combos[ci].name,-20} | {sumFloor / isoRuns,10:F2} | "
                            + $"{c5 * 100.0 / isoRuns,8:F1}% | {died1F * 100.0 / isoRuns,5:F1}%");
            }
            sb.AppendLine("※「空」が 0pt 相当にならないならスイープ側の状態汚染。");
            sb.AppendLine("※「5軸すべてT1」だけが崩れるなら組み合わせ固有の相互作用。");

            _challengeSweepReport = sb.ToString();

            _pendingChallengeLoadout = null;
            _pendingPresetRelic = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;
            GameLoop.GameManager.GrantBladeDanceOnFloor5Clear = prevBD;
        }

        private void RestoreChallengeFixedSweepState(
            bool previousRelicSuppression,
            ItemPickMode previousPickMode,
            bool previousHiddenBossSuppression,
            bool previousBladeDanceGrant)
        {
            _pendingChallengeLoadout = null;
            _pendingPresetRelic = null;
            _ascensionPersonaLock = false;
            itemPickMode = previousPickMode;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = previousRelicSuppression;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = previousHiddenBossSuppression;
            GameLoop.GameManager.GrantBladeDanceOnFloor5Clear = previousBladeDanceGrant;
        }

        private void CompleteUltraBatchIfActive(string summary)
        {
            if (!_ultraBatchActive) return;
            UltraProgressHub.Complete(_ultraProgressSession, summary);
            try { _ultraController?.CompleteBatch(summary); }
            catch (Exception ex)
            {
                Debug.LogError("[AutoRunner][Ultra] completion callback failed: " + ex.Message);
            }
            try { _ultraController?.Dispose(); } catch { }
            _ultraController = null;
            _ultraBatchActive = false;
        }

        private void FailUltraDuringBatch(string reason)
        {
            string detail = string.IsNullOrEmpty(reason) ? "unknown Ultra batch failure" : reason;
            UltraProgressHub.Fail(_ultraProgressSession, detail);
            try { _ultraController?.Cancel(detail); } catch { }
            try { _ultraController?.Dispose(); } catch { }
            _ultraController = null;
            _ultraBatchActive = false;
            Debug.LogError("[AutoRunner][Ultra] BATCH ABORTED: " + detail);
        }

        /// <summary>遺物 単軸スイープ。 **軸 1 本だけを載せて、その軸の寄与を切り出す。**
        ///
        /// プリセットスイープは「遺物の総量」を振るので、 個々の軸が単位 (期待与ダメ 6%) に
        /// 揃っているかは分からない。 ここでは **メイン枠 1 本のみ・段N** の遺物を全軸ぶん作り、
        /// 遺物なし (None) を基準に差分を見る。 軸間の強弱が直接読める。
        ///
        /// **1〜3F の与ダメ/被ダメも採る。** 到達層だけだと、 攻撃系と防御系が同じ深度に
        /// 落ち着いたときに「両方 6 単位」なのか「両方測れていない」のかを区別できない。
        ///
        /// 高難易度限定軸 (Λ共鳴/渇き/刻限/背水) は条件付きなので、 挑戦 0 では条件が
        /// 揃わず低く出る可能性がある ── その場合は軸が弱いのではなく **BOT が条件を
        /// 踏みに行かない**（回復目標が高く低HP/低希望に留まらない）ことを疑うこと。</summary>
        private IEnumerator RunRelicAxisSweep()
        {
            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;
            // 裏ボスは遮断する。 レイピアを引いたランだけ 5層の難度が跳ね上がり、
            // 軸間の差より大きい分散源になるため。
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = true;

            int runs = Mathf.Max(50, relicAxisSweepRuns);
            int step = Mathf.Clamp(relicAxisSweepStep, 1, MetaProgression.Relics.RelicAxisCatalog.CursedStep);
            var axes = MetaProgression.Relics.RelicAxisCatalog.All;

            var loadout = AscensionLoop.BuildLoadoutAtScore(relicAxisSweepChallengeScore);
            int actualScore = MetaProgression.ChallengeResolver.Score(loadout);

            var sb = new StringBuilder();
            sb.AppendLine("=== 遺物 単軸スイープ (§15-5) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ペルソナ Standard 固定 / 挑戦スコア {actualScore} 固定 / 5層裏ボス遮断");
            sb.AppendLine($"遺物は **メイン枠 = 対象軸@段{step} + 全アーム共通の埋め草サブ 2 枠** / 1 軸 {runs} ラン");
            sb.AppendLine($"ペア比較の基準は [基準]攻撃3 (メインだけ段3 へ落とした同一構成)。 遺物なしは文脈用。");
            sb.AppendLine("決定論シード: 0000 0000 0000 0000");
            sb.AppendLine();
            sb.AppendLine("軸            段の効果                              | 平均到達層 | 5層クリア | 7層クリア | 1攻撃与ダメ | 被ダメ/戦 | 5F改善/悪化 (McNemar p) | 7F改善/悪化 (McNemar p)");

            // ペア比較の基準の 1 ラン単位の結果。 **同一シードのペア比較**に使う。
            //   平均どうしの比較は使わない ── 500 ランでも 5層クリアの標準誤差が ±2.2pt あり、
            //   軸 1 本の寄与 (数pt) が埋もれるため (§13-5 / paircmp と同じ方針)。
            bool[] baseC5 = new bool[runs];
            // **7層クリアでも取る。** Λ共鳴は 5層クリア**後**に入る Λ 層でしか積まないので、
            //   5F 指標では構造的に「効果ゼロの軸」と測定される (2026-08-08 に実際に誤読した)。
            //   実測: 7F 到達ランの 100% が Λ を通過している ＝ Λ は 6層への強制通過点。
            bool[] baseC7 = new bool[runs];
            // 追加区分 (会心群/渇き群) はビルドごと条件を変えるので、 **その区分の基準**と対にする。
            //   全体基準と比べると「軸の寄与」と「ビルドを変えた効果」が混ざるため。
            bool[] grpC5 = new bool[runs], grpC7 = new bool[runs];

            // ── 追加区分: **その軸を主軸に据えたビルドで測り直す** ──
            //   軸によっては「噛み合わせて初めて働く」ので、 Standard/Balanced では実力が出ない。
            //   ただし条件を変えたら**基準も同じ条件で取り直す**こと ── そうしないと
            //   「軸の寄与」と「ビルドを変えた効果」が混ざる。 区分ごとに基準を先頭へ置く。
            //     会心群: ペルソナ Crit (メタ側の会心軸は 2026-09-10 に撤去)
            //     渇き群: hopeRefillFloor = 0 ＝ **希望を買い戻さない** = 低希望を維持する命令
            int extraCrit = 3;   // [会心基準]攻撃3 / 会心率 / 会心倍率
            int extraHope = 2;   // [渇き基準]攻撃3 / 渇き
            int total = axes.Length + extraCrit + extraHope;

            // 診断モード: [基準]攻撃3 と指定軸の 2 アームだけ。 追加区分は回さない。
            bool diagOne = relicAxisSweepOnlyAxis >= 0 && relicAxisSweepOnlyAxis < axes.Length;
            if (diagOne) { extraCrit = 0; extraHope = 0; total = axes.Length; }
            ResetScreenProgress((diagOne ? 3 : total + 2) * runs, "遺物単軸");

            var prevPolicy = PolicyParameters.Current;
            var prevMetaAxis = metaBuildAxis;

            // ai == -2: 遺物なし (文脈用・ペア比較の基準にはしない)
            // ai == -1: **ペア比較の基準** = メイン 攻撃@段3 + 全アーム共通の埋め草サブ 2 枠
            //           枠構成を揃えないと「軸の差」と「埋め草の有無」が混ざる。
            //           段3 はメインの構造下限なので、 ここからの差分が軸の寄与になる。
            // ai >= 0 : メインを対象軸@段N へ差し替え (末尾 5 本は上記の追加区分)
            int armOrdinal = 0;
            for (int ai = -2; ai < total; ai++)
            {
                // ── この区分の測定条件を決める ──
                var armPersona = BuildPersona.Standard;
                var armMeta    = prevMetaAxis;
                float armHopeRefill = -1f;                 // <0 = 既定のまま
                var armAxis = MetaProgression.Relics.RelicAxis.Attack;
                int  armStep = MetaProgression.Relics.RelicAxisCatalog.MainStepMin;
                string groupTag = "";
                float armHopeCeil = -1f;                   // <0 = 既定 (上限まで満たす)

                // 診断モードでは指定軸以外の本体アームを飛ばす (基準 ai==-1 は残す)。
                if (diagOne && ai >= 0 && ai != relicAxisSweepOnlyAxis) continue;
                curArmIndex = ++armOrdinal;
                curArmCount = diagOne ? 3 : total + 2;
                curRunsPerArm = runs;

                int ci = ai - axes.Length;                  // 追加区分のインデックス
                if (ai >= 0 && ci < 0) { armAxis = axes[ai]; armStep = step; }
                else if (ci >= 0 && ci < extraCrit)
                {
                    armPersona = BuildPersona.Crit;
                    // 2026-09-10: 精密トラック撤去に伴い PrecisionApex を廃止。 会心群は
                    //   ペルソナ Crit + 標準配分 (Balanced) で測る ── メタ側に会心軸が無くなった。
                    armMeta = MetaAllocationPresets.Preset.Balanced;
                    groupTag = "会心";
                    if (ci == 1) { armAxis = MetaProgression.Relics.RelicAxis.CritRatePct; armStep = step; }
                    if (ci == 2) { armAxis = MetaProgression.Relics.RelicAxis.CritMultPct; armStep = step; }
                }
                else if (ci >= extraCrit)
                {
                    // **帯を維持する**。 補充を止める (refill=0) と希望が 0 まで落ちて発狂で
                    //   ランが終わり、 上限まで満たすと〈渇き〉の発動条件から外れる。
                    //   実測ではどちらも軸が死んだので、 発動閾値 (hopeCap×40%) の少し下を
                    //   天井に、 発狂を避けられる高さを床に置いて **帯 [18, 38] を維持**する。
                    armHopeRefill = 18f;
                    armHopeCeil   = 0.38f;
                    groupTag = "渇き";
                    if (ci == extraCrit + 1) { armAxis = MetaProgression.Relics.RelicAxis.HopeBurn; armStep = step; }
                }

                metaBuildAxis = armMeta;
                var pol = prevPolicy.Clone();
                if (armHopeRefill >= 0f) pol.hopeRefillFloor = armHopeRefill;
                if (armHopeCeil   >= 0f) pol.hopeBandCeilRatio = armHopeCeil;
                PolicyParameters.SetCurrent(pol);

                bool isGroupBase = ci >= 0 && (ci == 0 || ci == extraCrit);

                var relic = ai == -2 ? null
                          : RelicPresets.BuildSingleAxis(armAxis, armStep);
                string label = ai == -2 ? "(遺物なし)"
                             : ai == -1 ? "[基準]攻撃3"
                             : isGroupBase ? $"[{groupTag}基準]攻撃3"
                             : (groupTag != "" ? groupTag + ":" : "")
                               + MetaProgression.Relics.RelicAxisCatalog.NameOf(armAxis);
                curArmLabel = label;
                string effect = ai == -2 ? "—"
                              : (ai == -1 || isGroupBase) ? "攻撃+3 (+埋め草サブ2枠)"
                              : MetaProgression.Relics.RelicAxisCatalog.Describe(armAxis, armStep);
                // **装備できているかを毎行に出す。** 枠数 1 の遺物は IsValid() で弾かれ、
                //   EquippedRelic が null を返して効果が一切乗らない ── これを黙って通すと
                //   「全軸が横並び」という読める形の嘘になる (2026-08-08 に実際に起きた)。
                if (relic != null && !relic.IsValid()) effect = "!!装備不可!! " + effect;

                var reach = new int[9];
                int c5 = 0, c7 = 0; double sumFloor = 0;
                int recBase = _records.Count;
                var thisC5 = new bool[runs];
                var thisC7 = new bool[runs];
                CombatSystem.CombatManager.ResetFloorDamage();
                MetaProgression.Relics.RelicApplicator.ResetHopeBurnStats();

                for (int i = 0; i < runs; i++)
                {
                    // **全軸で同じ runIdx を使う ＝ ペア比較にする。** (ai を混ぜない)
                    //   軸ごとに別シード帯を割ると軸間の差にシード分散が丸ごと乗り、
                    //   300 ラン (5層クリアの標準誤差 ±2.9pt) では軸の寄与が完全に埋もれる。
                    //   実測: 軸ごとに別帯にした版では 攻撃+9 が与ダメを下げ、
                    //   攻撃に無関係な 被ダメージ−45% が与ダメを上げる、という構造上ありえない
                    //   並びが出た (2026-08-08)。 同一シードなら差は軸だけに帰属する。
                    //
                    // **BeginRun を必ず呼ぶ。** 抜けるとラン間で乱数列が引き継がれる (2026-08-04)。
                    int runIdx = 40000 + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = armPersona;
                    _pendingChallengeLoadout = loadout;
                    _pendingPresetRelic = relic;
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    int cf = died ? reached - 1 : reached;
                    reach[Mathf.Clamp(reached, 0, 8)]++; sumFloor += reached;
                    if (cf >= 5) { c5++; thisC5[i] = true; }
                    if (cf >= 7) { c7++; thisC7[i] = true; }
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                if (ai == -1)
                {
                    System.Array.Copy(thisC5, baseC5, runs);
                    System.Array.Copy(thisC7, baseC7, runs);
                }
                if (isGroupBase)
                {
                    System.Array.Copy(thisC5, grpC5, runs);
                    System.Array.Copy(thisC7, grpC7, runs);
                }
                // 追加区分の軸は自分の区分基準と、 それ以外は全体基準と対にする。
                bool inGroup = ci >= 0;
                var refC5 = inGroup ? grpC5 : baseC5;
                var refC7 = inGroup ? grpC7 : baseC7;

                // ── ペア比較 (McNemar)。 同一シードなので「同じランがどう転んだか」で見る ──
                //   b = 軸ありだけ 5層クリア / c = 基準だけ 5層クリア。
                //   一致したランは検定に寄与しない ＝ 分散が落ちて数pt の差が読める。
                int b = 0, cc = 0, b7 = 0, c7b = 0;
                for (int i = 0; i < runs; i++)
                {
                    if (thisC5[i] && !refC5[i]) b++;
                    else if (!thisC5[i] && refC5[i]) cc++;
                    if (thisC7[i] && !refC7[i]) b7++;
                    else if (!thisC7[i] && refC7[i]) c7b++;
                }
                bool isBaseRow = ai < 0 || isGroupBase;
                string mc  = isBaseRow ? "—" : $"{b,3}/{cc,3} (p={McNemarP(b, cc),6:F3})";
                string mc7 = isBaseRow ? "—" : $"{b7,3}/{c7b,3} (p={McNemarP(b7, c7b),6:F3})";

                // 1〜3F の通常+エリート戦だけ (ボスは長さの性質が違うので除く)。
                //   FloorDamage の第3添字は 0=攻撃回数 / 1=与ダメ合計 / 2=最小 / 3=最大 /
                //   4=敵maxHP合計 / 5=ターン合計 / 6=戦闘数。 **被ダメは入っていない**ので
                //   そちらは _records の戦闘明細から拾う。
                var fd = CombatSystem.CombatManager.FloorDamage;
                double atk = 0, dmgOut = 0;
                for (int f = 1; f <= 3; f++)
                    for (int k = 0; k < 2; k++)
                    { atk += fd[f, k, 0]; dmgOut += fd[f, k, 1]; }

                double dmgIn = 0; int fights = 0;
                for (int r = recBase; r < _records.Count; r++)
                {
                    var cl = _records[r].combats;
                    if (cl == null) continue;
                    for (int c = 0; c < cl.Count; c++)
                        if (!cl[c].isBoss && cl[c].floor >= 1 && cl[c].floor <= 3)
                        { dmgIn += cl[c].damageTaken; fights++; }
                }

                sb.AppendLine($"{label,-12} {Trunc(effect, 36),-36} | {sumFloor / runs,10:F2} | "
                            + $"{c5 * 100.0 / runs,8:F1}% | {c7 * 100.0 / runs,8:F1}% | "
                            + $"{(atk > 0 ? dmgOut / atk : 0),11:F1} | {(fights > 0 ? dmgIn / fights : 0),9:F1} | {mc} | {mc7}");
                // 〈渇き〉のアームだけ、 発動率を追記する。 「弱い」と「発動していない」を分けるため。
                if (armAxis == MetaProgression.Relics.RelicAxis.HopeBurn)
                    sb.AppendLine($"             └ {MetaProgression.Relics.RelicApplicator.DescribeHopeBurnStats()}");
            }

            sb.AppendLine();
            sb.AppendLine("※ 基準は 1 行目 (遺物なし)。 単位の定義は『参照状態での期待与ダメ 6% = 攻撃+1』(§15-5)。");
            sb.AppendLine("※ **全軸が同一シード**。 判断は『5F改善/悪化』の McNemar p 値で行い、");
            sb.AppendLine("   平均どうしの比較はしない (300ランの標準誤差 ±2.9pt に軸の寄与が埋もれるため)。");
            sb.AppendLine("※ Λ共鳴は **5層クリア後**の Λ 層でしか積まないので 5F 指標では必ず 0 に出る。");
            sb.AppendLine("   Λ は 6層への強制通過点 (実測: 7F到達ランの100%が通過) なので **7F 指標で読む**。");
            sb.AppendLine("※ 末尾 5 行は測定条件を変えた区分。 会心群=ペルソナCrit /");
            sb.AppendLine("   渇き群=希望を帯 [18, hopeCap×38%] に維持 (発動閾値 40% の下に留めつつ発狂を避ける)。");
            sb.AppendLine("   **各区分の先頭が対の基準**。");
            _relicAxisSweepReport = sb.ToString();

            _pendingChallengeLoadout = null;
            _pendingPresetRelic = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            metaBuildAxis = prevMetaAxis;
            PolicyParameters.SetCurrent(prevPolicy);
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
        }

        // ============================================================
        //  ランダム付与試行 (2026-09-08)
        // ============================================================

        /// <summary>このランの割り当てを引く。 <b>GameRng を使わない</b> ── 消費列が変われば
        /// 同一シードの他の測定と比較できなくなる。 ラン番号だけで決まるので再現する。</summary>
        private void RollGrantPlan(int runIndex, GameLoop.RunState run)
        {
            _grantPlan.Clear();
            _grantGoldAmount = 0; _grantGoldFloor = 0;
            _grantRng = new System.Random(unchecked(runIndex * 2654435761u).GetHashCode());

            var db = InventorySystem.ItemDatabase.Instance;
            var all = db?.GetAllItems();
            if (all == null || all.Count == 0) return;

            // 対象は「普通に手へ入る品」。 イベント専用・職業スターターは
            //   通常の入手経路が無いので、 配ると存在しない状況を測ることになる
            //   (EventOnlyItemFilter がどちらも弾く)。
            //
            // [2026-09-17] **randomGrantAllKinds で消耗品・武器・出目パーツまで広げる。**
            //   ITT が意見を持つのがパッシブだけだと、 準パワーを Δクリア率 (pt) 単位で
            //   運用できない ── 非ITT品が regβ (band 単位) のまま同じ序列へ混ざり、
            //   しきい値が両者の間に落ちて**パーツと消耗品が丸ごと足切りされる**。
            //   band→pt の等化は引けなかった (2026-09-17 実測 89 対で r=−0.100 ＝
            //   相関が検出できない。 regβ は内生・ITT は外生で別物を測っている)。
            //   単位を揃える道は「全品を付与プールへ入れる」しか無い。
            var pool = new List<string>();
            int nPassive = 0, nCons = 0, nWeapon = 0, nPart = 0;
            foreach (var it in all)
            {
                if (it == null) continue;
                if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (it.category == InventorySystem.ItemCategory.Passive)
                { pool.Add(it.internalName); nPassive++; continue; }
                if (!randomGrantAllKinds) continue;
                if (it.category == InventorySystem.ItemCategory.Consumable)
                { pool.Add(it.internalName); nCons++; continue; }
                if (it.category == InventorySystem.ItemCategory.Weapon)
                { pool.Add(it.internalName); nWeapon++; continue; }
            }
            if (randomGrantAllKinds)
            {
                // 出目パーツは ItemDatabase に無い (DiceFaceParts.cs の規約コメント)。
                //   36 種を ID で列挙する ── 所持済みの除外は配る側 (ApplyGrants) で見る。
                foreach (var kind in GameLoop.DiceFaceParts.AllKinds())
                { pool.Add(GameLoop.DiceFaceParts.Id(kind)); nPart++; }
            }
            if (pool.Count == 0) return;
            if (!_grantPoolLogged)
            {
                _grantPoolLogged = true;
                Debug.Log($"[AutoRunner] 付与プール {pool.Count} 品"
                        + $" (パッシブ {nPassive} / 消耗品 {nCons} / 武器 {nWeapon} / 出目パーツ {nPart})"
                        + $" / 1品あたり付与確率 {Mathf.Clamp01(randomGrantExpectedItems / pool.Count):P2}"
                        + $" / 期待付与数 {randomGrantExpectedItems:F1}");
            }

            float p = Mathf.Clamp01(randomGrantExpectedItems / pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                if (_grantRng.NextDouble() >= p) continue;
                // **層をランダムにする。** 開幕固定だと複利で効く品だけが贔屓される。
                _grantPlan.Add((_grantRng.Next(1, 6), pool[i]));
            }

            // 金も 1 つの処置。 係数がそのまま 1G の band 価値になる。
            var levels = new List<int> { 0 };
            if (!string.IsNullOrEmpty(randomGrantGoldLevels))
                foreach (var s in randomGrantGoldLevels.Split(','))
                    if (int.TryParse(s.Trim(), out int v) && v > 0) levels.Add(v);
            _grantGoldAmount = levels[_grantRng.Next(levels.Count)];
            _grantGoldFloor  = _grantGoldAmount > 0 ? _grantRng.Next(1, 6) : 0;

            // 割り当てログはここで確定させる (band は Finish 後に足す)。
            var sb = new StringBuilder();
            sb.Append(_grantGoldAmount).Append('@').Append(_grantGoldFloor).Append('|');
            for (int i = 0; i < _grantPlan.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_grantPlan[i].id).Append('@').Append(_grantPlan[i].floor);
            }
            _pendingGrantRow = sb.ToString();
        }
        private string _pendingGrantRow;

        /// <summary>割り当てられた 1 品を種別に応じて配る (2026-09-17)。
        ///
        /// <para>入手経路は<b>ショップの購入処理と同じものを通す</b>
        /// (<see cref="InventorySystem.Shop.ShopManager"/> の CompletePurchase)。
        /// 別経路を作ると「配られた状態」と「買った状態」がずれて、
        /// ITT で測った効果が本番の購入判断へ載らない。</para>
        ///
        /// <para><b>失敗しても計画からは消す。</b> 所持済みの出目パーツや、
        /// 更新にならない武器は何も起きないが、 それは「その品を渡されることの効果」が
        /// たまたま 0 だったということ ── 割り当ては割り当てなので ITT は偏らない。
        /// ここで配り直すと処置確率が状態に依存してランダム化が壊れる。</para></summary>
        private static void GrantOne(GameLoop.RunState run, string id)
        {
            if (run == null || string.IsNullOrEmpty(id)) return;

            // 出目パーツ: ItemDatabase に無いので ID から復元する。
            if (GameLoop.DiceFaceParts.IsPartId(id))
            {
                if (!GameLoop.DiceFaceParts.TryParseId(id, out var part)) { NoteGrant(0, false); return; }
                if (run.diceFaceParts == null) { NoteGrant(0, false); return; }
                // **所持済みは足さない。** 抽選プールの除外集合そのものなので、
                //   重複させると以後その種類が棚から消えなくなる。
                for (int i = 0; i < run.diceFaceParts.Count; i++)
                    if (run.diceFaceParts[i].Key == part.Key) { NoteGrant(0, false); return; }
                run.diceFaceParts.Add(part);
                NoteGrant(0, true);
                return;
            }

            var data = InventorySystem.ItemDatabase.Instance?.GetItem(id);
            if (data != null && data.category == InventorySystem.ItemCategory.Consumable)
            { NoteGrant(1, run.TryAddConsumable(id)); return; }

            // パッシブと武器は同じ経路 (ShopManager も同じ 2 行を呼ぶ)。
            bool added = InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(run, id);
            bool weapon = data != null && data.category == InventorySystem.ItemCategory.Weapon;
            if (weapon) GameLoop.Loadout.TryAutoEquip(run, id);
            NoteGrant(weapon ? 2 : 3, added);
        }

        /// <summary>[計装 2026-09-17] <b>割り当てではなく「実際に届いたか」を数える。</b>
        ///
        /// <para>grant_runs.csv に載るのは <b>割り当て計画</b> (RollGrantPlan が組む) なので、
        /// 配る側が黙って落としていても行は立つ。 出目パーツは ItemDatabase に存在しない ID で、
        /// 消耗品・武器も別経路なので、 **ログを見ただけでは配布の成否が分からない**。
        /// 添字: 0=出目パーツ 1=消耗品 2=武器 3=パッシブ。</para></summary>
        public static readonly long[] GrantOk = new long[4];
        public static readonly long[] GrantNg = new long[4];
        public static readonly string[] GrantKindName = { "出目パーツ", "消耗品", "武器", "パッシブ" };
        public static void ResetGrantStats()
        { System.Array.Clear(GrantOk, 0, 4); System.Array.Clear(GrantNg, 0, 4); }
        private static void NoteGrant(int kind, bool ok)
        { if (kind < 0 || kind > 3) return; if (ok) GrantOk[kind]++; else GrantNg[kind]++; }

        /// <summary>到達した層ぶんの付与を実行する。 配り終えたものは計画から外す。</summary>
        private void ApplyDueGrants(GameManager gm)
        {
            var run = gm?.Run;
            if (run == null) return;
            int floor = run.currentFloor;

            for (int i = _grantPlan.Count - 1; i >= 0; i--)
            {
                if (_grantPlan[i].floor > floor) continue;
                GrantOne(run, _grantPlan[i].id);
                _cur?.acquiredItemsEver.Add(_grantPlan[i].id);
                _grantPlan.RemoveAt(i);
            }
            if (_grantGoldAmount > 0 && _grantGoldFloor <= floor)
            {
                GameLoop.GoldIncome.Gain(run, _grantGoldAmount, "付与試行");
                _grantGoldAmount = 0;
            }
        }

        /// <summary>ラン終了後に band と近位指標を添えて割り当てログへ 1 行足す。
        ///
        /// <para><b>近位指標を足した理由 (2026-09-08)。</b> 鈍器家系が「効果 6 倍で価値が単調に下がる」
        /// (Bludgeon_1 +0.134 → Lv4 −0.365) 問題で、 <b>会心との衝突という仮説は棄却された</b>
        /// (交互作用 +0.024 ± 0.036 / 会心を多く配るほど鈍器は<b>良く</b>なる)。
        /// 残る有力な筋は「配線 AI を誤誘導している」 ── Optimal は 3^5 を全列挙して評価関数で
        /// 選ぶので、 「非会心なら +120%」が評価に乗ると会心を避ける配線を選ぶ。
        /// <b>実ダメージが落ちていれば誤誘導、 ダメージは変わらず band だけ落ちるなら別の副作用</b>。
        /// band は 7 層ぶんの高分散な成果なので、 近位指標の方が同じラン数で解像度が高い。</para>
        ///
        /// <para>列: <c>band|gold@floor|id@floor,...|与ダメ,被ダメ,戦闘数,勝利数,回復,シールド</c></para></summary>
        private void RecordGrantRow()
        {
            if (!randomGrantTrial || string.IsNullOrEmpty(_pendingGrantRow)) return;
            var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
            int band = rec != null ? rec.bandScore : -1;
            string prox = rec == null ? "0,0,0,0,0,0"
                : string.Join(",", rec.totalDamageDealt, rec.totalDamageTaken,
                              rec.totalCombats, rec.totalWins, rec.totalHealed, rec.totalShieldGained);
            _grantLog.Add(band + "|" + _pendingGrantRow + "|" + prox);
            _pendingGrantRow = null;
        }

        /// <summary>ランダム付与試行の本体。 アームは無く、 全ランが同じ手続きで割り当てを引く。</summary>
        private IEnumerator RunRandomGrantTrial()
        {
            int runs = Mathf.Max(100, randomGrantRuns);
            _grantLog.Clear();
            ResetScreenProgress(runs, "ランダム付与");
            curArmLabel = "ランダム付与"; curArmIndex = 1; curArmCount = 1; curRunsPerArm = runs;

            for (int i = 0; i < runs; i++)
            {
                // **並列化の要 (2026-09-17)。** runIdx は GameRng と付与抽選 (RollGrantPlan)
                //   の両方を決める。 プロセスごとに区間をずらさないと、 全プロセスが
                //   **同じ割り当てを引いて同じランを繰り返す** ── 行数だけ増えて情報は増えない。
                int runIdx = 90000 + randomGrantStartIndex + i;
                GameLoop.GameRng.BeginRun(runIdx);
                if (GameLoop.GameRng.IsSeeded)
                    _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                yield return RunOne(runIdx);
                RecordGrantRow();
                if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
            }
        }

        /// <summary>アイテム アブレーション。 **同一シードで「開幕付与あり / なし」を対で走らせる。**
        ///
        /// <para>出すのは効果量ではなく <b>対の差の SD</b> ── これが全品アブレーションの
        /// 実現可能性を決める。 差の SD を s とすると、 真の効果 0.15 band を SE 0.05 で
        /// 見るのに必要な対の数は <c>(s/0.05)²</c>。 band の素の SD 2.83 のままなら
        /// 3,200 対/品 × 163 品 = 52 万ラン、 s が 1.0 まで落ちれば 400 対/品 = 6.5 万ラン。
        /// <b>桁が変わる</b>ので、 先にここを測る。</para>
        ///
        /// <para>先頭 2 アームは <b>A/A (両方とも付与なし)</b>。 ここで 1 ランでも差が出たら
        /// シミュレーションが決定的でないということで、 ペアリングの前提が崩れる ──
        /// その場合は下の SD を読む意味がないので、 まず決定性を直すこと。</para></summary>
        /// <summary><b>極点 (r10) 同士の比較 (2026-09-10)。</b> 数値系 8 トラックを 1 本ずつ
        /// r10 まで振り切り、 残り 23pt を共通の順序で埋めて比べる。
        ///
        /// <para><b>なぜ要るか。</b> r10 の極点 (ラストスタンド / 宝箱ゴールド / 強盗解禁 …) は
        /// 「1 トラックを振り切る理由」として 2026-07-28〜08-04 に意図的に置かれたのに、
        /// <b>一度も測られていない</b>。 drop-one は全アームが r6 以下だったので極点に触れていない。
        /// 「MAX を 9 段階へ」の可否も、 特化が報われるかどうかも、 ここが分からないと決められない。</para>
        ///
        /// <para><b>埋め方を固定する。</b> 各アームは [焦点 r10] + [共通の順序で 23pt]。
        /// 焦点になったトラックは埋め草から飛ばす。 埋め草の中身がアーム間で少し違うのは
        /// 避けられないが、 <b>どのアームも「1 本振り切って残りを薄く」</b>という同じ形になる。
        /// 比較対象は Balanced (どの極点にも届かない標準配分)。</para></summary>
        private IEnumerator RunKeystoneSweep()
        {
            int runs = Mathf.Max(200, metaAblationRuns);
            var K = typeof(MetaProgression.MetaPanelKind);
            // 数値系のみ (max 10)。 精密は撤去済みなので MaxRank 0 で自動的に外れる。
            var focus = new List<MetaProgression.MetaPanelKind>();
            foreach (MetaProgression.MetaPanelKind k in Enum.GetValues(K))
                if (MetaProgression.MetaPanelKindExt.MaxRank(k) == 10) focus.Add(k);

            // **土台は「焦点 10pt + 他 7 本を 2pt ずつ」(2026-09-12・予算 24pt)。**
            //   Balanced (数値系 8 本 × 3pt) と合計が一致し、 極点の代償が
            //   「全軸を 1 段ずつ削る」という均一な形になる。 詳細は RunRankMarginSweep。
            var arms = new List<(string label, Dictionary<MetaProgression.MetaPanelKind, int> spec)>();
            arms.Add(("Balanced (極点なし)", MetaAllocationPresets.Ranks(MetaAllocationPresets.Preset.Balanced)));
            foreach (var f in focus)
            {
                var d = new Dictionary<MetaProgression.MetaPanelKind, int> { { f, 10 } };
                int left = MetaProgression.MetaPanel.MaxPoints - 10;
                foreach (var k in focus)
                {
                    if (k == f || left <= 0) continue;
                    int r = Math.Min(2, left);
                    d[k] = r; left -= r;
                }
                arms.Add(($"{f} r10", d));
            }

            GameManager.PlunderDrops = 0;
            ResetScreenProgress(arms.Count * runs, "極点比較");
            var sb = new StringBuilder();
            sb.AppendLine("=== 極点 (r10) 比較 ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss} / 1 アーム {runs} ラン / シードは全アーム共通");
            sb.AppendLine($"各アーム = [焦点トラック r10 = 10pt] + [共通順序で残り {MetaProgression.MetaPanel.MaxPoints - 10}pt]");
            sb.AppendLine("比較対象 = Balanced (どの極点にも届かない標準配分)");
            sb.AppendLine();
            sb.AppendLine("アーム                  |  クリア率 |  Balanced比 | 平均到達段 | 極点の内容");

            var note = new Dictionary<string, string>
            {
                { "Shell",   "ラストスタンド解禁 + HP+3" },
                { "Output",  $"〈戦意〉勝利ごとに与ダメ +{MetaProgression.MetaPanel.BattleSpiritPctPerWin}% (恒久)" },
                { "Guard",   "シールドの戦闘間持ち越し" },
                { "Vault",   "宝箱ゴールド復活" },
                { "Plunder", "ショップ強盗 解禁" },
                { "Trade",   "99%引きの特売枠が出る" },
                { "Supply",  "開幕パッシブが 2択" },
                { "Lantern", "横移動の希望消費 5→2" },
            };

            double baseClear = 0;
            for (int ai = 0; ai < arms.Count; ai++)
            {
                var (label, spec) = arms[ai];
                curArmLabel = label; curArmIndex = ai + 1; curArmCount = arms.Count; curRunsPerArm = runs;
                _metaAblationSpec = spec;

                int clears = 0, valid = 0; double stageSum = 0;
                for (int i = 0; i < runs; i++)
                {
                    int runIdx = 60000 + i;   // 全アーム共通シード
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    if (rec != null && rec.bandScore >= 0)
                    { valid++; stageSum += rec.bandScore; if (rec.bandScore >= 11) clears++; }
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                double clear = valid > 0 ? clears / (double)valid * 100.0 : 0;
                double stage = valid > 0 ? stageSum / valid : 0;
                if (ai == 0) baseClear = clear;
                string key = label.Replace(" r10", "");
                sb.AppendLine($"{label,-22} | {clear,8:F2}% | {(ai == 0 ? 0 : clear - baseClear),+10:F2}pt | {stage,9:F2} | "
                            + (note.TryGetValue(key, out string n) ? n : (ai == 0 ? "── 基準" : "")));
            }
            _metaAblationSpec = null;
            sb.AppendLine();
            sb.AppendLine($"※ 1 アーム {runs} ラン。 クリア率 {baseClear:F0}% 帯の MDE は概ね "
                        + $"{2.8 * Math.Sqrt(2 * baseClear * (100 - baseClear) / runs):F2}pt。 これ未満の差は読まないこと。");
            sb.AppendLine($"※ 強奪の道中パッシブドロップ (全アーム累積): {GameManager.PlunderDrops:N0} 件");
            {
                long ra = InventorySystem.Shop.ShopManager.RobberyAttempts;
                long rw = GameManager.RobberyWins, rl = GameManager.RobberyLosses;
                sb.AppendLine($"※ 強盗の罰: {(robberyBlocksShops ? "出禁 (旧仕様・切り分け用)" : "以降のショップ価格 ×2")}"
                            + $" / 発動 {ra:N0} 回 (勝 {rw:N0} 敗 {rl:N0}"
                            + $" 平均戦利品 {(rw > 0 ? GameManager.RobberyLootTotal / (double)rw : 0):F1} 件)");
                sb.AppendLine("   ↑ 発動 0 なら 商才 r10 の差は強盗以外 (割引下限40%) の取り分。 先にここを読むこと。");
            }
            sb.AppendLine("※ Balanced 比が正 = 「1 本振り切る」ほうが「薄く広く」より良い ＝ 極点に価値がある。");
            sb.AppendLine("※ 全アームが負なら特化そのものが損 ＝ 極点は現状機能していない。");
            _metaAblationReport = sb.ToString();
            Debug.Log("[AutoRunner] 極点比較 完了\n" + _metaAblationReport);
        }

        /// <summary><b>スループット計測 (2026-09-13)。</b> <see cref="runsPerYield"/> だけを振って
        /// ラン/秒 を実測する。 <see cref="stepsPerYield"/> は固定 (ラン内の yield を条件から外すため)。
        ///
        /// <para>同じシード列を毎条件で使い回すので、 <b>条件間で仕事量は完全に同じ</b>。
        /// 差が出たらそれは待ち時間の差。 クリア率も併記して、 条件で結果が変わっていないことを確かめる
        /// (変わっていたら計測が壊れている)。</para></summary>
        private IEnumerator RunYieldBenchmark()
        {
            int runs = Mathf.Max(50, yieldBenchRuns);
            int[] cadences = { 1, 5, 25, 100 };
            int savedRpy = runsPerYield, savedSpy = stepsPerYield;
            stepsPerYield = 1000;   // ラン内 yield を実質無効化して条件から外す

            var sb = new StringBuilder();
            sb.AppendLine("=== スループット計測 (runsPerYield) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss} / 1 条件 {runs} ラン / stepsPerYield={stepsPerYield} 固定");
            sb.AppendLine("同一シード列を毎条件で再利用 ── 仕事量は同じなので、 差は待ち時間の差");
            sb.AppendLine();
            sb.AppendLine("runsPerYield |    秒 | ラン/秒 | 相対 | クリア率");

            double baseRate = 0;
            foreach (int c in cadences)
            {
                runsPerYield = c;
                curArmLabel = $"rpy={c}"; curArmIndex = Array.IndexOf(cadences, c) + 1;
                curArmCount = cadences.Length; curRunsPerArm = runs;

                int clears = 0, valid = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < runs; i++)
                {
                    int runIdx = 70000 + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    if (rec != null && rec.bandScore >= 0) { valid++; if (rec.bandScore >= 11) clears++; }
                    if ((i + 1) % c == 0) yield return null;
                }
                sw.Stop();
                double sec = sw.Elapsed.TotalSeconds;
                double rate = sec > 0 ? runs / sec : 0;
                if (baseRate <= 0) baseRate = rate;
                double clear = valid > 0 ? clears / (double)valid * 100.0 : 0;
                sb.AppendLine($"{c,12} | {sec,5:F1} | {rate,7:F1} | {rate / baseRate,4:F2}x | {clear,7:F2}%");
            }

            runsPerYield = savedRpy; stepsPerYield = savedSpy;
            sb.AppendLine();
            sb.AppendLine("※ 相対が 1.00 付近で頭打ち = 計算律速。 刻みを増やしても速くならない。");
            sb.AppendLine("※ 大きく伸びる = フレーム待ち律速。 スイープの runsPerYield=1 を上げるべき。");
            sb.AppendLine("※ クリア率が条件間でズレたら計測が壊れている (同じシード・同じ仕事のはず)。");
            _metaAblationReport = sb.ToString();
            Debug.Log("[AutoRunner] スループット計測 完了\n" + _metaAblationReport);
        }

        /// <summary><b>r9 vs r10 の限界比較 (2026-09-12)。</b> 極点比較は
        /// 「そのトラックに 10pt 使うこと」と「極点そのもの」を足し合わせた数字しか出さない。
        /// 同じトラックの r9 と r10 を並べると、 差が<b>極点 1 個ぶんの取り分</b>になる。
        ///
        /// <para><b>完全な分離ではない。</b> r10 は数値効果も 1 段ぶん増えるし、
        /// r9 で浮いた 1pt は埋め草へ回る。 測っているのは
        /// 「10 段目に 1pt 足すか、 埋め草に 1pt 足すか」という<b>実際の選択</b>なので、
        /// 配点を決めるにはこの形が正しい。</para>
        ///
        /// <para>各トラック 2 アーム。 Balanced は置かない ── 比較はトラック内で閉じており、
        /// 基準アームを足すと 1 本ぶん余計に走る (2026-09-12 の方針: 判断に要らないアームは立てない)。</para></summary>
        private IEnumerator RunRankMarginSweep()
        {
            int runs = Mathf.Max(200, metaAblationRuns);
            // **土台は「焦点 10pt + 他 7 本を 2pt ずつ」で固定 (2026-09-12・予算 24pt)。**
            //
            //   Balanced (数値系 8 本 × 3pt) と合計が一致し、 極点を取る代償が
            //   <b>全軸を 1 段ずつ削る</b>という均一な形になる。 どのアームも
            //   「1 本が 10 / 7 本が 2」で形が同じなので、 違うのは**どのラベルが 10 か**だけ。
            //
            //   旧版は Balanced を土台に流用していたため、 Balanced が 0 に置いていた
            //   強奪/商才だけが 10pt 全額を新規に払い、 **出力を 4 段まるごと失っていた**。
            //   犠牲の不均一がトラックの弱さと混ざって読めなかった。
            var tracks = new List<MetaProgression.MetaPanelKind>();
            string spec0 = (rankMarginTracks ?? "").Trim();
            if (spec0.Length == 0 || spec0 == "*")
            {
                foreach (MetaProgression.MetaPanelKind k in Enum.GetValues(typeof(MetaProgression.MetaPanelKind)))
                    if (MetaProgression.MetaPanelKindExt.MaxRank(k) == 10) tracks.Add(k);
            }
            else foreach (var tok in spec0.Split(','))
            {
                if (!Enum.TryParse(tok.Trim(), true, out MetaProgression.MetaPanelKind k)) continue;
                if (MetaProgression.MetaPanelKindExt.MaxRank(k) != 10)
                { Debug.LogWarning($"[AutoRunner] r9/r10 比較: {k} は 10 段トラックではない。 飛ばす"); continue; }
                tracks.Add(k);
            }
            if (tracks.Count == 0) { Debug.LogError("[AutoRunner] r9/r10 比較: 対象トラックが空"); yield break; }

            var ranks = new List<int>();
            foreach (var tok in (rankMarginRanks ?? "9,10").Split(','))
                if (int.TryParse(tok.Trim(), out int r) && r >= 1 && r <= 10) ranks.Add(r);
            if (ranks.Count == 0) ranks.Add(9);

            var arms = new List<(string label, Dictionary<MetaProgression.MetaPanelKind, int> spec)>();
            // **Balanced を同じバッチに入れる。** 判定基準が Balanced 比で書かれているので、
            //   別バッチの古い値と突き合わせると今日のような「基準が陳腐化していた」事故になる。
            arms.Add(("Balanced", MetaAllocationPresets.Ranks(MetaAllocationPresets.Preset.Balanced)));
            foreach (var t in tracks)
                foreach (int r in ranks) arms.Add(($"{t} r{r}", BuildRankMarginArmSpec(t, r)));

            GameManager.PlunderDrops = 0;
            InventorySystem.Shop.ShopManager.RobberyAttempts = 0;
            GameManager.RobberyWins = GameManager.RobberyLosses = GameManager.RobberyLootTotal = 0;
            ResetScreenProgress(arms.Count * runs, "全軸 r9/r10");
            var sb = new StringBuilder();
            sb.AppendLine("=== 全軸 r9 / r10 (Balanced 同梱) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss} / 1 アーム {runs} ラン / シードは全アーム共通");
            sb.AppendLine($"予算 {MetaProgression.MetaPanel.MaxPoints}pt / "
                        + "各アーム = [焦点トラック r9 or r10] + [他の数値系 7 本を 2pt ずつ]");
            sb.AppendLine("  r10 = 10+14 = 24pt ちょうど / r9 = 9+14 = 23pt (余り 1pt は**使わない**)");
            sb.AppendLine("  → r10 と r9 の違いは焦点トラックの 1 段だけ。 埋め草は完全に同一");
            sb.AppendLine("判定基準: r10 は Balanced ±3pt");
            sb.AppendLine();
            sb.AppendLine("アーム                  |  クリア率 |  Bal比 |   r10−r9 | 判定 | 平均到達段");

            var clearOf = new Dictionary<string, double>();
            double baseClear = 0;
            for (int ai = 0; ai < arms.Count; ai++)
            {
                var (label, spec) = arms[ai];
                curArmLabel = label; curArmIndex = ai + 1; curArmCount = arms.Count; curRunsPerArm = runs;

                var tally = new RankMarginArmTally();
                yield return RunRankMarginArm(spec, runs, RankMarginSeedBase, tally);
                double clear = tally.ClearPct;
                double stage = tally.MeanStage;
                clearOf[label] = clear;
                if (ai == 0) baseClear = clear;

                double bal = clear - baseClear;
                string delta = "", verdict = "";
                if (ai == 0) { verdict = "基準"; }
                else if (label.EndsWith(" r9"))
                {
                    verdict = Math.Abs(bal) <= 2.0 ? "○" : (bal > 0 ? "強" : "弱");
                }
                else if (label.EndsWith(" r10")
                         && clearOf.TryGetValue(label.Replace(" r10", " r9"), out double r9c))
                {
                    double d = clear - r9c;
                    delta = $"{d,+9:F2}pt";
                    verdict = (d > 0 && Math.Abs(bal) <= 3.0) ? "○" : (d <= 0 ? "極点×" : (bal > 0 ? "強" : "弱"));
                }
                sb.AppendLine($"{label,-22} | {clear,8:F2}% | {(ai == 0 ? 0 : bal),+6:F2} | {delta,9} "
                            + $"| {verdict,-4} | {stage,9:F2}");
            }
            _metaAblationSpec = null;
            sb.AppendLine();
            sb.AppendLine($"※ 1 アーム {runs} ラン。 差の 95%CI は概ね ±"
                        + $"{1.96 * Math.Sqrt(2 * baseClear * (100 - baseClear) / runs):F2}pt。"
                        + " **r9 の基準 ±2pt はこの分解能とほぼ同じ**なので、 ○/× は目安。");
            sb.AppendLine("※ 判定  ○=基準内 / 強=上振れ / 弱=下振れ / 極点×= r10 が r9 以下 (極点が無価値か有害)。");
            sb.AppendLine("※ r9 が弱い → 1 段あたりの増加量を上げるか内容を変える。");
            sb.AppendLine("※ r10 が未達 → 極点をリワークかバフ。 r9 は基準内なのに r10 が外れているなら原因は極点だけ。");
            sb.AppendLine($"※ 強奪の道中パッシブドロップ: {GameManager.PlunderDrops:N0} 件");
            {
                long ra = InventorySystem.Shop.ShopManager.RobberyAttempts;
                sb.AppendLine($"※ 強盗 (強奪r10 へ移設): 発動 {ra:N0} 回 / 勝 {GameManager.RobberyWins:N0}"
                            + $" 敗 {GameManager.RobberyLosses:N0} / 平均戦利品 "
                            + $"{(GameManager.RobberyWins > 0 ? GameManager.RobberyLootTotal / (double)GameManager.RobberyWins : 0):F1} 件");
            }
            _metaAblationReport = sb.ToString();
            Debug.Log("[AutoRunner] 全軸 r9/r10 比較 完了\n" + _metaAblationReport);
        }

        /// <summary>r9/r10 比較のシード列の先頭。 全アーム共通。</summary>
        public const int RankMarginSeedBase = 60000;

        /// <summary>各アーム/各プロセスの頭で 1 本だけ空回しするラン。 集計に入れない。
        /// 理由は <c>RunRankMarginArm</c> の捨てランのコメント。</summary>
        public const int RankMarginWarmupSeed = RankMarginSeedBase - 1;

        /// <summary>r9/r10 比較の配点 — 焦点トラック <paramref name="rank"/> + 他の数値系 7 本を 2pt。
        ///
        /// <para>余りは配らない。 r9 は 9+2×7 = 23pt で 1pt 未使用のまま置く。
        /// 端数を他所へ回すと「10 段目 1pt の価値」を測っているはずが
        /// 「10 段目 1pt と埋め草 1pt の差」になる。 r10 と r9 の違いを
        /// <b>焦点トラックの 1 段だけ</b>に閉じるため、 余りは捨てる。</para>
        ///
        /// <para>逐次スイープと並列ワーカーが同じ配点を組むための唯一の実体。</para></summary>
        public static Dictionary<MetaProgression.MetaPanelKind, int> BuildRankMarginArmSpec(
            MetaProgression.MetaPanelKind focus, int rank)
        {
            var d = new Dictionary<MetaProgression.MetaPanelKind, int> { { focus, rank } };
            int left = MetaProgression.MetaPanel.MaxPoints - rank;   // 数値系は 1pt/段
            foreach (MetaProgression.MetaPanelKind k in Enum.GetValues(typeof(MetaProgression.MetaPanelKind)))
            {
                if (MetaProgression.MetaPanelKindExt.MaxRank(k) != 10) continue;
                if (k == focus || left <= 0) continue;
                int r = Math.Min(2, left);
                d[k] = r; left -= r;
            }
            return d;
        }

        /// <summary>1 アーム分の集計。 並列ワーカーはこれを JSON にして親へ返す。</summary>
        public sealed class RankMarginArmTally
        {
            public int valid;
            public int clears;
            public double stageSum;
            /// <summary>ラン毎の (runIdx, bandScore, digest)。 ペア比較と決定論の検算に使う。</summary>
            public readonly List<int> runIndices = new List<int>();
            public readonly List<int> bandScores = new List<int>();
            public readonly List<string> digests = new List<string>();
            /// <summary>Λ 層でランが終わった件数。 <b>bandScore では Λ と 5層道中 が同じ帯 (6) に入る</b>
            /// ── Λ 滞在中は currentFloor が 5 のままだから (§14-1)。 層別の死亡を
            /// <b>ラン単位で</b>切り分けるには、 戦闘側の計装ではなくこの件数で引く
            /// (戦闘側は救済で生き返ったランも数えるので、 引くと符号が合わない)。</summary>
            public int lambdaRunDeaths;

            public double ClearPct => valid > 0 ? clears / (double)valid * 100.0 : 0;
            public double MeanStage => valid > 0 ? stageSum / valid : 0;

            public void Add(int runIdx, RunRec rec)
            {
                int band = rec != null ? rec.bandScore : -1;
                runIndices.Add(runIdx);
                bandScores.Add(band);
                digests.Add(rec != null ? (rec.deterministicDigest ?? "") : "");
                if (band < 0) return;
                if (rec != null && rec.deathInLambda) lambdaRunDeaths++;
                valid++;
                stageSum += band;
                if (band >= 11) clears++;
            }
        }

        /// <summary>並列ワーカー 1 プロセス分 — 担当アームの担当シード区間だけを走らせる。</summary>
        private IEnumerator RunRankMarginWorkerArm()
        {
            if (rankMarginWorkerSpec == null || rankMarginWorkerRuns <= 0)
            {
                Debug.LogError("[AutoRunner] r9/r10 ワーカー: 配点かラン数が未設定");
                try { RankMarginArmCompleted?.Invoke(null); } catch { }
                yield break;
            }
            curArmLabel = rankMarginWorkerLabel; curArmIndex = 1; curArmCount = 1;
            curRunsPerArm = rankMarginWorkerRuns;
            ResetScreenProgress(rankMarginWorkerRuns, rankMarginWorkerLabel);

            var tally = new RankMarginArmTally();
            yield return RunRankMarginArm(rankMarginWorkerSpec, rankMarginWorkerRuns,
                rankMarginWorkerSeedStart, tally);
            _metaAblationSpec = null;
            Debug.Log($"[AutoRunner] r9/r10 ワーカー完了: {rankMarginWorkerLabel} "
                    + $"seed {rankMarginWorkerSeedStart}..{rankMarginWorkerSeedStart + rankMarginWorkerRuns - 1} "
                    + $"/ 有効 {tally.valid} / クリア {tally.clears}");
            try { RankMarginArmCompleted?.Invoke(tally); } catch (Exception ex)
            { Debug.LogError("[AutoRunner] r9/r10 ワーカー: 応答書き出しで例外 " + ex.Message); }
        }

        /// <summary>r9/r10 比較の 1 アーム。
        /// <b>逐次スイープと並列ワーカーが共有する唯一の実体。</b>
        ///
        /// <para>アームの中身を 2 か所に写すと必ず食い違う (2026-08-17 の前例)。
        /// 並列版の検算は「逐次版と digest が一致すること」で行うので、
        /// <b>比較する 2 つが同じコードを通っていなければ検算にならない</b>。</para>
        ///
        /// <para><c>seedStart</c> でシード区間を切り出せるのは、 ラン i の結果が
        /// <c>runIdx</c> だけの関数だから (digest_cmp.py で 1,000/1,000 一致を確認済)。
        /// この前提が崩れたら並列化そのものが無効になる。</para></summary>
        private IEnumerator RunRankMarginArm(Dictionary<MetaProgression.MetaPanelKind, int> spec,
            int runs, int seedStart, RankMarginArmTally tally)
        {
            _metaAblationSpec = spec;

            // **捨てラン 1 本を先に回す。**
            //
            //   プロセス内の**先頭ランだけ**が、 2 本目以降と違う結果になる。 実測で
            //   chunks=1 と chunks=4 を突き合わせると、 ずれるのは各チャンクの先頭
            //   ちょうど 9/900 件で、 2 本目以降は全一致した。 同じチャンク数どうしは
            //   0/900 で完全再現するので、 タイミング由来のゆらぎではなく決定的な差。
            //
            //   効いているのは**前のランの中身ではなく「前のランが在ったか」の二値**。
            //   根拠: run#60151 は直前の 60150 が両者で違う結果になっているのに一致する。
            //   つまり 1 本空回しすれば、 先頭でも 2 本目以降と同じ土俵に乗る。
            //
            //   原因の初期化そのものは未特定。 特定できたらこの空回しは消すこと ──
            //   **これは症状を揃えているだけで、 原因を直してはいない。**
            GameLoop.GameRng.BeginRun(RankMarginWarmupSeed);
            UnityEngine.Random.InitState(RankMarginWarmupSeed);
            if (GameLoop.GameRng.IsSeeded)
                _rng = new System.Random(GameLoop.GameRng.Range(
                    0, int.MaxValue, "autorun.eventTiebreak", RankMarginWarmupSeed));
            yield return RunOne(RankMarginWarmupSeed);

            for (int i = 0; i < runs; i++)
            {
                int runIdx = seedStart + i;
                GameLoop.GameRng.BeginRun(runIdx);
                // **UnityEngine.Random もラン単位で固定する** (2026-08-10 の修正をここへ展開)。
                //   GameRng は BeginRun で完全にリセットされるが、 UnityEngine.Random の
                //   静的状態はラン跨ぎで残り、 **それまでに何回引かれたか**に依存する。
                //   同じプロセスで先頭から回す限り履歴が揃うので逐次では再現してしまい、
                //   **プロセスへ分割した瞬間に先頭ランだけ割れる** (実測 9/900・境界ちょうど)。
                //   ここが抜けていると並列版と逐次版が一致しない。
                UnityEngine.Random.InitState(runIdx);
                if (GameLoop.GameRng.IsSeeded)
                    _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                yield return RunOne(runIdx);
                var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                // **打ち切りは理由ごと残す。** 無効ランは母数から外れるので、
                //   黙って捨てると「そういう分布なのだ」と読んでしまう。
                //   しかも打ち切りは長生きしたランに偏るので、 捨てた分だけ結果が歪む。
                if (rec != null && (rec.outcome == Outcome.Deadlock || rec.outcome == Outcome.Crash))
                    LogAlways($"[r9/r10] 打切 run#{runIdx} — {rec.band}: {rec.note}", LogType.Warning);
                tally.Add(runIdx, rec);
                // **毎ラン報告する** (2026-09-19)。 50 ランごとだと重い方策 (Super) で
                //   最初の報告まで数分かかり、 止まっているのか区別できなかった。
                //   書くのは数十バイトの progress.txt 1 つなので毎ランでも負担にならない。
                {
                    try { RankMarginArmProgress?.Invoke(i + 1); }
                    catch (Exception ex)
                    { Debug.LogWarning("[AutoRunner] 進捗通知で例外: " + ex.Message); }
                }
                if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
            }
        }

        /// <summary><b>整備パネルの drop-one アブレーション (2026-09-10)。</b>
        /// Balanced から 1 軸ずつ抜いて、 どの軸がクリア率を支配しているかを測る。
        ///
        /// <para><b>なぜ要るか。</b> 整備パネルは実測でオッズ比 14〜21 と全強化軸で最大
        /// (遺物 29pt でさえ 5.4)。 ところが個々のボーナスは +30% 与ダメ / +18HP / +15%会心 と
        /// 中庸で、 <b>数字と効果が釣り合っていない</b>。 釣り合わないときは実装を読むか
        /// 内訳を測る ── 内訳が無いまま「強すぎる/妥当」は論じられない。</para>
        ///
        /// <para><b>drop-one にする理由。</b> 「その軸だけ」を測ると他軸との相乗が落ちるし、
        /// 予算も揃わない。 <b>限界寄与</b> (満額から 1 軸抜いたときの落ち幅) が
        /// 「この軸に振る価値」の定義として素直。 抜いた pt 数で割れば pt あたりの効率も出る。</para></summary>
        private IEnumerator RunMetaAxisAblation()
        {
            int runs = Mathf.Max(200, metaAblationRuns);
            var full = MetaAllocationPresets.Ranks(MetaAllocationPresets.Preset.Balanced);

            // アーム: [0]=満額 Balanced / [1..n]=1 軸抜き / 最後=全部無し (0pt)
            var arms = new List<(string label, Dictionary<MetaProgression.MetaPanelKind, int> spec, int dropped)>();
            arms.Add(("満額 Balanced", new Dictionary<MetaProgression.MetaPanelKind, int>(full), 0));
            foreach (var kv in full)
            {
                var d = new Dictionary<MetaProgression.MetaPanelKind, int>(full);
                d.Remove(kv.Key);
                arms.Add(($"−{kv.Key}({kv.Value}pt)", d, kv.Value));
            }
            arms.Add(("全無し 0pt", new Dictionary<MetaProgression.MetaPanelKind, int>(), 36));

            ResetScreenProgress(arms.Count * runs, "パネルAblation");
            var sb = new StringBuilder();
            sb.AppendLine("=== 整備パネル drop-one アブレーション ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss} / 1 アーム {runs} ラン / シードは全アーム共通");
            sb.AppendLine("満額 Balanced から 1 軸ずつ抜いた落ち幅 = その軸の限界寄与。");
            sb.AppendLine("**抜いた pt は再配分しない** ── 再配分すると『どの軸か』と『何ptか』が混ざる。");
            sb.AppendLine();
            sb.AppendLine("アーム                  |  クリア率 |    落ち幅 |  抜いたpt | pt あたり | 平均到達段");

            double baseClear = 0, baseStage = 0;
            for (int ai = 0; ai < arms.Count; ai++)
            {
                var (label, spec, dropped) = arms[ai];
                curArmLabel = label; curArmIndex = ai + 1; curArmCount = arms.Count; curRunsPerArm = runs;
                _metaAblationSpec = spec;

                int clears = 0; double stageSum = 0; int valid = 0;
                for (int i = 0; i < runs; i++)
                {
                    // **全アームで同じ runIdx** ── ここを崩すと差にシード分散が丸ごと乗る。
                    int runIdx = 50000 + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    if (rec != null && rec.bandScore >= 0)
                    { valid++; stageSum += rec.bandScore; if (rec.bandScore >= 11) clears++; }
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                double clear = valid > 0 ? clears / (double)valid * 100.0 : 0;
                double stage = valid > 0 ? stageSum / valid : 0;
                if (ai == 0) { baseClear = clear; baseStage = stage; }
                double drop = baseClear - clear;
                sb.AppendLine($"{label,-22} | {clear,8:F2}% | {(ai == 0 ? 0 : drop),8:F2}pt | {dropped,8} | "
                            + $"{(dropped > 0 ? drop / dropped : 0),8:F3} | {stage,8:F2}"
                            + (ai == 0 ? "  ← 基準" : ""));
            }
            _metaAblationSpec = null;
            sb.AppendLine();
            sb.AppendLine("※ 落ち幅が大きい軸ほど支配的。 pt あたりで見ると「効率」が出る。");
            sb.AppendLine($"※ 1 アーム {runs} ラン なので、 クリア率 {baseClear:F0}% 帯の MDE は概ね "
                        + $"{2.8 * Math.Sqrt(2 * baseClear * (100 - baseClear) / runs):F2}pt。 これ未満の差は読まないこと。");
            _metaAblationReport = sb.ToString();
            Debug.Log("[AutoRunner] パネル drop-one 完了\n" + _metaAblationReport);
        }

        private IEnumerator RunItemAblationSweep()
        {
            int runs = Mathf.Max(20, itemAblationRuns);
            var ids = new List<string>();
            if (!string.IsNullOrEmpty(itemAblationIds))
                foreach (var s in itemAblationIds.Split(','))
                    if (!string.IsNullOrWhiteSpace(s)) ids.Add(s.Trim());

            var db = InventorySystem.ItemDatabase.Instance;
            var sb = new StringBuilder();
            sb.AppendLine("=== アイテム アブレーション (同一シードのペア) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"1 アーム {runs} 対 / 開幕付与あり vs なし / シードは全アーム共通");
            sb.AppendLine("目的は効果量ではなく **対の差の SD** ── 全品アブレーションの必要ラン数を決める");
            sb.AppendLine();

            // **進捗の分母を張り替える。** 忘れると通常バッチの runCount (=1) が残り、
            //   完了数だけ増えて 100% を超え続ける (2026-09-08 に実際に出した)。
            ResetScreenProgress((ids.Count + 2) * runs, "アイテムAblation");

            // アーム -2 / -1 が A/A (両方とも付与なし)。 決定性の検査。
            var baseBand = new int[runs];
            var sb2 = new StringBuilder();
            sb2.AppendLine("アーム              | 平均band | 差の平均 | 差のSD | 差が出た対 | 7F改善/悪化 (McNemar p) | 0.05 に必要な対数");

            for (int ai = -2; ai < ids.Count; ai++)
            {
                string grant = ai < 0 ? null : ids[ai];
                string label = ai == -2 ? "[A] 付与なし"
                             : ai == -1 ? "[A] 付与なし(再)"
                             : (db?.GetItem(grant)?.displayName ?? grant);
                curArmLabel = label;
                curArmIndex = ai + 3;
                curArmCount = ids.Count + 2;
                curRunsPerArm = runs;

                var band = new int[runs];
                var c7 = new bool[runs];
                for (int i = 0; i < runs; i++)
                {
                    // **全アームで同じ runIdx。** ここを崩すと差にシード分散が丸ごと乗る。
                    int runIdx = 70000 + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _pendingGrantItemId = grant;
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    band[i] = rec != null ? rec.bandScore : -1;
                    c7[i] = band[i] >= 11;
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }

                if (ai == -2) { System.Array.Copy(band, baseBand, runs); }

                double mean = 0; for (int i = 0; i < runs; i++) mean += band[i];
                mean /= runs;

                string row;
                if (ai == -2)
                {
                    row = $"{label,-19} | {mean,8:F2} | {"—",8} | {"—",6} | {"—",10} | {"—",23} | {"—",16}";
                }
                else
                {
                    double dm = 0; int moved = 0;
                    for (int i = 0; i < runs; i++) { dm += band[i] - baseBand[i]; if (band[i] != baseBand[i]) moved++; }
                    dm /= runs;
                    double dv = 0;
                    for (int i = 0; i < runs; i++) { double d = (band[i] - baseBand[i]) - dm; dv += d * d; }
                    double sd = Math.Sqrt(dv / Math.Max(1, runs - 1));
                    int b = 0, c = 0;
                    for (int i = 0; i < runs; i++)
                    {
                        bool bb = baseBand[i] >= 11;
                        if (c7[i] && !bb) b++;
                        else if (!c7[i] && bb) c++;
                    }
                    // 真の効果 0.15 band を SE 0.05 で見るのに要る対の数
                    string need = sd > 0 ? $"{Mathf.CeilToInt((float)((sd / 0.05) * (sd / 0.05))),16}" : $"{"(差ゼロ)",16}";
                    row = $"{label,-19} | {mean,8:F2} | {dm,+8:F3} | {sd,6:F3} | {moved,4}/{runs,-5} | "
                        + $"{b,3}/{c,3} (p={McNemarP(b, c),6:F3})      | {need}";
                }
                sb2.AppendLine(row);
            }

            sb.Append(sb2);
            sb.AppendLine();
            sb.AppendLine("※ 1 行目と 2 行目は **同じ条件 (A/A)**。 ここで『差が出た対』が 0 でなければ");
            sb.AppendLine("   シミュレーションが決定的でない ── ペアリングが効かないので下の SD は読めない。");
            sb.AppendLine("※ 『0.05 に必要な対数』= (差のSD / 0.05)²。 アイテム間の真のばらつきは SD 0.15 band");
            sb.AppendLine("   (2026-09-07 / ホールドアウトの信頼性から逆算) なので、 これを 1/3 の精度で見る想定。");
            sb.AppendLine("※ 対照アームは**自然なラン**なので、 対象品を途中で買うことがある。 その分だけ");
            sb.AppendLine("   効果は薄まる (推定量としては『開幕から確実に持つこと』の効果)。");
            _itemAblationReport = sb.ToString();
            _pendingGrantItemId = null;
        }

        /// <summary>挑戦 単軸スイープ。 **挑戦デバフ 1 段だけを載せて、その段の重さを切り出す。**
        ///
        /// 固定難易度スイープは「合計 Npt」を振るので、 30pt が重すぎる時に
        /// **どの軸が値段に見合わないか**が分からない。 ここでは (軸, Tier) の組を
        /// 1 つずつ単独で載せ、 **0pt を基準にした同一シードのペア比較**で差を採る。
        ///
        /// 読み方: 「1pt あたり何 pt 分の 7層クリアを削るか」で軸を横並びにする。
        /// 3pt の軸が 1pt の軸の 3 倍削っていれば値付けは正しい。 大きく外れた軸が
        /// 調整対象。 **平均到達層では読まない** ── 崩れ方 (深く進んで死ぬ / 早期に詰む)
        /// が軸ごとに違うので、 到達層分布と 3F 止まり率を併記する。
        ///
        /// 条件は固定難易度スイープと揃える (Standard / TheoreticalBestCursed / 裏ボス遮断)
        /// ── そうしないと階段の測定値と直接つき合わせられない。</summary>
        private IEnumerator RunChallengeAxisSweep()
        {
            bool prevParallelWiring = parallelizeOptimalWiring;
            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;
            _pendingRelicExplicit = true;
            // 遺物なしは**バランスの正式な基準** (周回引継ぎなので初回プレイヤーは 0 個)。
            //   2026-08-10: このフラグ分岐が**固定難易度スイープ側にしか入っていなかった**ため、
            //   レポートは「遺物なし (基準条件)」と表示しながら理論最良遺物込みで走っていた。
            //   結果、 同じ 0pt 基準値が単軸 53.3% / 技量帯 16.6% に割れた
            //   (実測: 同一シード・同一職業で最大HP が 113 と 93。 差は遺物の +20 だけ)。
            //   **表示と実体を分けて持つと、 嘘が静かに通る。**
            _pendingPresetRelic = challengeAxisSweepNoRelic
                ? null : RelicPresets.Build(RelicPresets.Preset.TheoreticalBestCursed);
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = true;

            // **下限は 1**。 他のスイープは Max(50,…) で床を張っているが、 ここは
            //   動作確認 (各3ラン) が主要な使い方なので床を張ると「3 と指定したのに
            //   50 走る」ことになる (2026-08-10 に実際にやった)。
            int runs = Mathf.Max(1, challengeAxisSweepRuns);

            // ── アームの列挙 ──
            //   先頭は必ず 0pt (ペア比較の基準)。 以降は (軸, Tier) の全組。
            //   **必ず def.tiers を回すこと。** v4.1 で Tier 番号が飛び飛びになったので、
            //   1..MaxTier で回すと存在しない段のアームを走らせる (2026-08-10 に実際にやった)。
            var arms = new List<(MetaProgression.AxisDef def, int tier)>();
            arms.Add((null, 0));
            // 診断モード: T4 の解禁条件 (カテゴリ 6pt) を外す。 **このスイープの間だけ**。
            MetaProgression.ChallengeResolver.IgnoreT4Unlock = challengeCategorySweepT4Alone;

            // T4 モード: 軸ではなくカテゴリ単位のアームを組む (下の catArms を使う)。
            var catArms = new List<(MetaProgression.ChallengeCategory cat, bool withT4)>();
            if (challengeCategorySweep)
                foreach (MetaProgression.ChallengeCategory c in
                         System.Enum.GetValues(typeof(MetaProgression.ChallengeCategory)))
                {
                    // **基礎軸を変えていない回は 6pt アームを省ける** ── 前回と同じ値を
                    //   再現するだけなので、 前回の実測を参照値に使う。 その代わり
                    //   **基準アームが前回と一致するか**でバッチ間の地続きを確認すること。
                    if (challengeCategorySweepOnlyCat >= 0 && (int)c != challengeCategorySweepOnlyCat) continue;
                    if (!challengeCategorySweepT4Only && !challengeCategorySweepT4Alone) catArms.Add((c, false));
                    catArms.Add((c, true));
                }
            // 指定があればその軸だけに絞る。 基準アームとノイズ床は常に残す。
            var axisFilter = (challengeAxisSweepAxisNames != null && challengeAxisSweepAxisNames.Length > 0)
                           ? new HashSet<string>(challengeAxisSweepAxisNames) : null;
            // 価格曲線モード: 挑戦デバフは 0pt のまま、 価格倍率だけを振る。
            bool priceCurve = shopPriceCurve != null && shopPriceCurve.Length > 0;
            if (priceCurve)
            {
                for (int k = 0; k < shopPriceCurve.Length; k++) arms.Add((null, 0));
            }
            // 決定性診断: 0pt だけを N 本。 軸は一切載せない。
            else if (challengeAxisSweepDeterminismArms > 0)
            {
                for (int k = 1; k < challengeAxisSweepDeterminismArms; k++) arms.Add((null, 0));
            }
            else if (challengeCategorySweep)
            {
                // アームの中身は下の catArms が持つ。 ここでは個数だけ合わせる。
                for (int ci = 0; ci < catArms.Count; ci++) arms.Add((null, -1 - ci));
            }
            else
            for (int ai = 0; ai < MetaProgression.ChallengeCatalog.Axes.Count; ai++)
            {
                if (challengeAxisSweepOnlyAxis >= 0 && ai != challengeAxisSweepOnlyAxis) continue;
                var def = MetaProgression.ChallengeCatalog.Axes[ai];
                if (axisFilter != null && !axisFilter.Contains(def.axis.ToString())) continue;
                foreach (int t in def.tiers)
                {
                    if (challengeAxisSweepMaxTierOnly && t != def.MaxTier) continue;
                    arms.Add((def, t));
                }
            }
            // **末尾に基準の複製を置く。** 設定が 1 行目と完全に同一・同一シードなのに
            //   結果がどれだけ動くか ＝ **アーム間ノイズの実測床**。 これが無いと、
            //   ノイズを軸の効果と読み違える (2026-08-10 に偶然の重複アームで 4.0pt と判明)。
            //   軸の差がこの床より小さいなら「差は測れていない」と判定すること。
            arms.Add((null, 0));

            var sb = new StringBuilder();
            sb.AppendLine("=== 挑戦 単軸スイープ (v4.0) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ペルソナ Standard 固定 / 遺物 {(challengeAxisSweepNoRelic ? "なし (基準条件)" : "TheoreticalBestCursed 固定")} / 5層裏ボス遮断 / 技量 {wiringSkill}");
            sb.AppendLine($"1 アーム {runs} ラン / **全アーム同一シード** (ペア比較)");
            sb.AppendLine(_stateLineAtStart);
            _probeTrace = "";
            _probeTrace = "";
            sb.AppendLine();
            sb.AppendLine("軸              段  点 | 平均到達層 | 5層クリア | 7層クリア | 7F差分 | 1pt当り | 3F止まり | 獲得G | 支出G | 残金 | 購入計 | パッシブ| 消耗品| 武器 | ダイス| 素材 | 装備力 | 打切 | 到達層分布 1F〜7F | 7F改善/悪化 (McNemar p)");

            // 固定難易度1000ランと同じ画面内進捗。単軸スイープは全アーム合計を100%とする。
            ResetScreenProgress(arms.Count * runs, "挑戦単軸");

            bool[] baseC5 = new bool[runs], baseC7 = new bool[runs];
            double base7Pct = 0;
            int missingRec = 0;   // RunOne 後に _records から結果を拾えなかった回数
            float swStart = Time.realtimeSinceStartup;   // 進捗行の経過/ETA 用
            // 決定性検査用。 アームごとのラン指紋を丸ごと保持する。
            var fpBase = new int[runs];
            var fpDiag = new List<(string label, int[] fp)>();
            // 1ラン差分モード: アームごとのナラティブを退避する (_detail は runIdx キーなので
            //   後のアームが上書きしてしまい、 そのままでは 2 アームを比べられない)。
            bool narrateDiff = challengeAxisSweepDeterminismArms > 0 && runs <= 2;
            var narr = new List<List<string>>();

            for (int armIdx = 0; armIdx < arms.Count; armIdx++)
            {
                if (challengeAxisSweepParallelCompare)
                    parallelizeOptimalWiring = armIdx > 0;
                float armStartRt = Time.realtimeSinceStartup;
                var (def, tier) = arms[armIdx];
                var lo = new MetaProgression.ChallengeLoadout();
                int pts = def != null ? def.PointsOf(tier) : 0;
                string catLabel = null;
                if (def != null) lo.SetTier(def.axis, tier);
                else if (tier < 0)
                {
                    // T4 モード: カテゴリの全軸を最上位へ。 withT4 ならその上に T4 を載せる。
                    var (cat, withT4) = catArms[-1 - tier];
                    // 単独モードではカテゴリ基礎軸を載せない (基準 + T4 のみ)。
                    if (!challengeCategorySweepT4Alone)
                        foreach (var ax in MetaProgression.ChallengeCatalog.AxesOf(cat))
                        { lo.SetTier(ax.axis, ax.MaxTier); pts += ax.MaxPoints; }
                    if (withT4)
                    {
                        var t4 = MetaProgression.ChallengeCatalog.T4s.Find(x => x.category == cat);
                        if (t4 != null) { lo.SetT4(t4.t4, true); pts += t4.points; }
                        catLabel = challengeCategorySweepT4Alone
                                 ? "単独:" + (t4 != null ? t4.displayName : "T4")
                                 : cat + "+" + (t4 != null ? t4.displayName : "T4");
                    }
                    else catLabel = cat + " 6pt";
                }

                // 救済 (灯火/ラストスタンド/フルーレ) の発動をアーム単位で数える。
                GameLoop.LastStand.ResetStats();

                var reach = new int[9];
                int c5 = 0, c7 = 0; double sumFloor = 0;
                var thisC5 = new bool[runs];
                var thisC7 = new bool[runs];
                var thisFp = new int[runs];
                int recBase = _records.Count;   // 経済統計の集計開始位置
                int abortedSoFar = 0;           // 進捗行に出す打ち切り件数
                // 価格曲線モードでは倍率を差し込む。 基準アーム (armIdx 0) は上書きしない。
                float priceMul = 0f;
                if (priceCurve && armIdx > 0 && armIdx - 1 < shopPriceCurve.Length)
                    priceMul = shopPriceCurve[armIdx - 1];
                MetaProgression.MetaDebuffApplicator.ShopPriceOverride = priceMul;

                string armLabel = catLabel != null ? catLabel
                                : def != null ? def.displayName + " T" + tier
                                : priceMul > 0f ? $"価格 ×{priceMul:F2}"
                                : challengeAxisSweepParallelCompare
                                    ? (armIdx == 0 ? "0pt直列" : "0pt並列")
                                : (armIdx == 0 ? "0pt基準" : "0pt複製");
                // 外部観測用。 「どのアームの何ラン目で止まったか」を反射に頼らず読めるように。
                curArmIndex = armIdx + 1; curArmCount = arms.Count;
                curRunsPerArm = runs; curArmLabel = armLabel;

                for (int i = 0; i < runs; i++)
                {
                    // **全アームで同じ runIdx** ＝ ペア比較。 アームごとに別シード帯を割ると
                    //   軸間の差にシード分散が丸ごと乗り、 数pt の寄与が埋もれる (§13-5)。
                    curRunInArm = i + 1;
                    int runIdx = challengeAxisSweepIndexBase + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    // **UnityEngine.Random もラン単位で固定する** (2026-08-10)。
                    //   GameRng は BeginRun で完全にリセットされるが、 UnityEngine.Random の
                    //   静的状態はラン跨ぎで残り、 **それまでに何回引かれたか**に依存する。
                    //   スイープ内は呼び出し履歴が同一なので再現する (ノイズ床 0/300) 一方、
                    //   別のスイープ関数から回すと履歴が変わって同一シードのランが割れていた。
                    UnityEngine.Random.InitState(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    _pendingChallengeLoadout = lo;
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    // **rec が拾えないまま黙って進まない。** 拾えなければ全ランが「1F 敗北」に
                    //   計上され、 表は形になるのに中身が全部 0 という**読める形の嘘**になる。
                    if (rec == null) missingRec++;
                    if (rec != null && (rec.outcome == Outcome.Deadlock || rec.outcome == Outcome.Crash))
                    {
                        abortedSoFar++;
                        // **打ち切りは即座に出す。** 後でまとめて数えるだけだと、
                        //   走行中に「いま何が起きているか」が分からない。
                        LogAlways($"[挑戦単軸] 打切 {armLabel} run#{i} — {rec.note}", LogType.Warning);
                    }
                    thisFp[i] = rec != null ? rec.fingerprint : 0;
                    if (narrateDiff && i == 0)
                        narr.Add(_detail.TryGetValue(runIdx, out var dl)
                                 ? new List<string>(dl) : new List<string>());
                    int reached = rec != null ? rec.reachedFloor : 1;
                    // 乖離調査 (2026-08-10): 同一 runIdx のランが技量帯スイープと一致するか。
                    if ( armIdx == 0 && i < 10)
                        _probeTrace += $" #{runIdx}:{reached}{((rec == null || rec.outcome == Outcome.GameOver) ? "x" : "o")}"
                                     + $"[{_probeRunConfig}]";
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    int cf = died ? reached - 1 : reached;
                    reach[Mathf.Clamp(reached, 0, 8)]++; sumFloor += reached;
                    if (cf >= 5) { c5++; thisC5[i] = true; }
                    if (cf >= 7) { c7++; thisC7[i] = true; }
                    // **アーム内でも進捗を出す。** アーム末尾だけだと 1 アーム 300 ラン の間
                    //   無音になり、 「遅い」と「止まった」を区別できない (2026-08-10)。
                    if (challengeAxisSweepProgressEvery > 0
                        && (i + 1) % challengeAxisSweepProgressEvery == 0 && i + 1 < runs)
                    {
                        float el = Time.realtimeSinceStartup - swStart;
                        int doneRuns = armIdx * runs + i + 1;
                        int allRuns = arms.Count * runs;
                        float eta = doneRuns > 0 ? el / doneRuns * (allRuns - doneRuns) : 0f;
                        LogAlways($"[挑戦単軸]   {armIdx + 1}/{arms.Count} {armLabel} "
                                + $"{i + 1}/{runs} ラン … 経過 {el / 60f:F1}分 / 残り {eta / 60f:F1}分 "
                                + $"(暫定 7F {c7 * 100.0 / (i + 1):F1}%, 打切 {abortedSoFar})");
                    }
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }

                // アーム完了。 経過と残り見込みを添える。
                {
                    float el = Time.realtimeSinceStartup - swStart;
                    int doneRuns = (armIdx + 1) * runs;
                    int allRuns = arms.Count * runs;
                    float eta = el / doneRuns * (allRuns - doneRuns);
                    LogAlways($"[挑戦単軸] {armIdx + 1}/{arms.Count} 完了 {armLabel} "
                            + $"→ 5F {c5 * 100.0 / runs:F1}% / 7F {c7 * 100.0 / runs:F1}% "
                            + $"| 打切 {abortedSoFar} / rec欠落 {missingRec} "
                            + $"| アーム {Time.realtimeSinceStartup - armStartRt:F1}秒 "
                            + $"| 経過 {el / 60f:F1}分 残り {eta / 60f:F1}分");
                }

                double pct7 = c7 * 100.0 / runs;
                if (armIdx == 0)
                {
                    System.Array.Copy(thisC5, baseC5, runs);
                    System.Array.Copy(thisC7, baseC7, runs);
                    System.Array.Copy(thisFp, fpBase, runs);
                    base7Pct = pct7;
                }
                else
                {
                    var keep = new int[runs];
                    System.Array.Copy(thisFp, keep, runs);
                    fpDiag.Add((def != null ? def.displayName + " T" + tier : "0pt複製#" + armIdx, keep));
                }

                int b7 = 0, c7b = 0;
                for (int i = 0; i < runs; i++)
                {
                    if (thisC7[i] && !baseC7[i]) b7++;
                    else if (!thisC7[i] && baseC7[i]) c7b++;
                }

                double delta7 = pct7 - base7Pct;
                // **1pt 当たりの削り量**。 値付けが揃っているかはこの列だけで読める。
                string perPt = (armIdx == 0 || pts <= 0) ? "—" : $"{delta7 / pts,6:F2}";
                string mc7 = armIdx == 0 ? "—" : $"{b7,3}/{c7b,3} (p={McNemarP(b7, c7b),6:F3})";

                // ── 経済の実挙動 ──
                //   価格倍率を上げたのに 7F 差分が動かない、 といった飽和が
                //   「買えなくなった」のか「買い方が変わった」のかは、
                //   到達層だけでは絶対に区別できない。 購入数・獲得GOLD・
                //   最終装備力を並べて初めて読める (2026-08-10 に説明を憶測で埋めて指摘された)。
                double gold = 0, buys = 0, power = 0, spent = 0, left = 0;
                double bPas = 0, bCon = 0, bWep = 0, bDic = 0, bMat = 0;
                double wTrig = 0, wLost = 0; int wRuns = 0;
                int en = 0, aborted = 0;
                for (int r = recBase; r < _records.Count; r++)
                {
                    var rr = _records[r];
                    if (rr.outcome == Outcome.Deadlock || rr.outcome == Outcome.Crash) aborted++;
                    gold  += rr.totalGoldGained;
                    buys  += rr.shopPurchases;
                    power += rr.finalInventoryPower;
                    spent += rr.totalCoinsSpent;
                    left  += rr.finalCoins;
                    bPas += rr.shopBuyPassive;  bCon += rr.shopBuyConsumable;
                    bWep += rr.shopBuyWeapon;   bDic += rr.shopBuyDice;
                    bMat += rr.shopBuyMaterial;
                    wTrig += rr.woundTriggers; wLost += rr.woundHpLost;
                    if (rr.woundTriggers > 0) wRuns++;
                    en++;
                }
                string econ = en > 0
                    ? $"{gold / en,6:F0} |{spent / en,6:F0} |{left / en,5:F1} |{buys / en,6:F2} |"
                      + $"{bPas / en,5:F2} |{bCon / en,5:F2} |{bWep / en,5:F2} |{bDic / en,5:F2} |"
                      + $"{bMat / en,5:F2} |{power / en,7:F1} |{aborted,4}"
                    : "     ? |     ? |    ? |     ? |    ? |    ? |    ? |    ? |    ? |      ? |   ?";

                var dist = new StringBuilder();
                for (int f = 1; f <= 7; f++) dist.Append($"{reach[f] * 100.0 / runs,5:F1}%");

                // **T4 モードのアームは tier < 0 で区別する。** これを見ないと
                //   カテゴリアームが全部「基準複製」と表示され、 表が読めない (2026-08-11 に踏んだ)。
                bool isNoiseFloor = def == null && tier == 0 && armIdx > 0;
                string label = def != null ? def.displayName
                             : catLabel != null ? catLabel
                             : (isNoiseFloor ? "[ノイズ床] 基準複製" : "(挑戦なし 0pt)");
                string seg = def != null ? $"T{tier}{(tier == def.MaxTier ? "*" : " ")}{pts,2}pt"
                           : catLabel != null ? $"    {pts,2}pt" : "     0pt";

                sb.AppendLine($"{PadR(label, 14)} {seg} | {sumFloor / runs,10:F2} | "
                            + $"{c5 * 100.0 / runs,8:F1}% | {pct7,8:F1}% | "
                            + $"{(armIdx == 0 ? "—" : $"{delta7,6:F1}"),6} | {perPt,7} | "
                            + $"{reach[3] * 100.0 / runs,7:F1}% |{econ} |{dist} | {mc7}");
                // 〈長引く負傷〉の行にだけ発動状況を添える。 **「弱い」と「発動していない」は
                //   到達層からは区別できない** ── 実際 2026-08-10 にそこで判断を誤った。
                // 救済の発動元。 **HP0 は「死」ではなく「全回復の引き金」**でもあるので、
                //   HP を削るデバフの効き方はここを見ないと読めない (2026-08-11)。
                if (en > 0)
                {
                    var rc = GameLoop.LastStand.RevivalCount;
                    long rtot = rc[0] + rc[1] + rc[2];
                    sb.AppendLine($"             └ 救済: 灯火 {rc[0]} / ラストスタンド {rc[1]} / フルーレ {rc[2]} "
                                + $"= 計 {rtot} 回 ({rtot * 100.0 / en:F1}% のラン) "
                                + $"/ 戻した HP 計 {GameLoop.LastStand.RevivalHpRestored}");
                }
                // **閾値・上限は印字しない (2026-08-17)。** 2026-08-10 のリワークで
                //   〈長引く負傷〉は「被ダメ比例」になり、 閾値も回数上限も持たなくなったのに
                //   定数だけが残り、 ログが廃止済みの旧仕様を出し続けていた。
                //   実測の平均損失 32.4 が「上限 20」を超えていて矛盾しており、
                //   **上限が壊れているのではなく上限が存在しない**のが実態。
                //   効いている値 (被ダメの何 %) だけを出す。
                if (def != null && def.axis == MetaProgression.ChallengeAxis.長引く負傷 && en > 0)
                    sb.AppendLine($"             └ 発動: {wRuns * 100.0 / en:F1}% のランで平均 {wTrig / en:F2} 回 "
                                + $"/ 失った最大HP 平均 {wLost / en:F1} "
                                + $"(被ダメの {MetaProgression.MetaDebuffApplicator.GetLingeringWoundRatio():P0})");
            }

            // **診断バイパスを必ず戻す。** 立てっぱなしだと以後の全測定が不正条件になる。
            MetaProgression.ChallengeResolver.IgnoreT4Unlock = false;
            parallelizeOptimalWiring = prevParallelWiring;

            // ── 決定性検査 ──
            //   1 行目と設定が同じアーム (0pt 複製) は、 同一シードなら**全ラン指紋一致**が正。
            //   一致しないなら、 それは統計のばらつきではなく **状態の持ち越し**。
            sb.AppendLine();
            sb.AppendLine("=== 決定性検査 (基準アームとのラン単位 指紋照合) ===");
            for (int k = 0; k < fpDiag.Count; k++)
            {
                var (lbl, fp) = fpDiag[k];
                int diff = 0, first = -1;
                for (int i = 0; i < runs; i++)
                    if (fp[i] != fpBase[i]) { diff++; if (first < 0) first = i; }
                sb.AppendLine($"{PadR(lbl, 22)} 不一致 {diff,4}/{runs} ({diff * 100.0 / runs,5:F1}%)"
                            + (first < 0 ? "  — 完全一致" : $"  初回発散 = ラン #{first}"));
            }
            sb.AppendLine("※ **0pt複製 は 1 行目と設定が完全に同一**。 ここが完全一致でないなら");
            sb.AppendLine("   ラン間で状態が持ち越されている ＝ 全ての軸間比較が信用できない。");
            sb.AppendLine("   **初回発散 #0 は「アーム切替の初期化漏れ」を意味しない** ── アーム 1 が");
            sb.AppendLine("   始まる時点で既に前アーム分のランが経過しており、 累積状態でも #0 で出る。");
            sb.AppendLine("   両者の区別には 1 ラン単位の詳細ログ差分が要る (2026-08-10 の反省)。");

            // ── 1ラン差分: 同一設定・同一 runIdx の 2 アームで、 **最初に分岐した行**を出す ──
            //   ここが「何が状態を持ち越しているか」への唯一の直接証拠になる。
            if (narrateDiff && narr.Count >= 2)
            {
                var a = narr[0]; var b = narr[1];
                int at = -1;
                int n = Math.Min(a.Count, b.Count);
                for (int i = 0; i < n; i++) if (a[i] != b[i]) { at = i; break; }
                if (at < 0 && a.Count != b.Count) at = n;
                sb.AppendLine();
                sb.AppendLine("=== 1ラン差分 (アーム1 vs アーム2 / 同一 runIdx・同一設定) ===");
                sb.AppendLine($"行数: アーム1 {a.Count} / アーム2 {b.Count}");
                if (at < 0) sb.AppendLine("→ **完全一致**。 このランでは分岐していない。");
                else
                {
                    sb.AppendLine($"→ **最初の分岐は {at} 行目**。 直前 6 行と両者の該当行:");
                    for (int i = Math.Max(0, at - 6); i < at; i++) sb.AppendLine($"   共通 | {a[i]}");
                    sb.AppendLine($"   A>>> | {(at < a.Count ? a[at] : "(行なし)")}");
                    sb.AppendLine($"   B>>> | {(at < b.Count ? b[at] : "(行なし)")}");
                    for (int i = at + 1; i < Math.Min(at + 5, n); i++)
                    {
                        sb.AppendLine($"   A    | {a[i]}");
                        sb.AppendLine($"   B    | {b[i]}");
                    }
                }
            }

            sb.AppendLine();
            if (missingRec > 0)
                sb.AppendLine($"!!! rec 欠落 {missingRec} 件 — この表は信用できない (欠落ランは全て『1F 敗北』に計上されている) !!!");
            sb.AppendLine("※ **打切** = Deadlock/Crash で終わったラン数 (番犬による実時間打ち切りを含む)。");
            sb.AppendLine("   0 でないアームの数値は、 その分だけ『1F 敗北』側に歪んでいる。");
            sb.AppendLine($"[先頭10ラン 到達層]{_probeTrace}");
            {
                long calls = MetaProgression.MetaDebuffApplicator.HealSpotCalls;
                long raw   = MetaProgression.MetaDebuffApplicator.HealSpotRawHeal;
                long capn  = MetaProgression.MetaDebuffApplicator.HealSpotCapped;
                long lost  = MetaProgression.MetaDebuffApplicator.HealSpotLostHeal;
                sb.AppendLine($"[回復スポット計装] 呼出 {calls} 回 / 実回復 {raw} HP"
                            + $" / 上限で削られた {capn} 回・{lost} HP"
                            + (calls > 0 ? $" (1 回あたり 実回復 {raw / (float)calls:F1} / 損失 {lost / (float)calls:F1})" : ""));
            }
            {
                var M = typeof(MetaProgression.MetaDebuffApplicator);
                long tt = MetaProgression.MetaDebuffApplicator.JudgmentTurnsTotal;
                long ov = MetaProgression.MetaDebuffApplicator.JudgmentRunsOverThreshold;
                long tk = MetaProgression.MetaDebuffApplicator.JudgmentTicks;
                long hl = MetaProgression.MetaDebuffApplicator.JudgmentHpLost;
                long bd = MetaProgression.MetaDebuffApplicator.BreakdownTriggers;
                long totalRuns = (long)runs * arms.Count;
                sb.AppendLine($"[T4計装] 累計戦闘ターン {tt} / 全 {totalRuns} ラン ＝ 1ラン {tt / (double)Math.Max(1, totalRuns):F1} ターン"
                            + $" / 刻限({MetaProgression.MetaDebuffApplicator.GetJudgmentTurnThreshold()}T)超え {ov} ラン ({100.0 * ov / Math.Max(1, totalRuns):F1}%)"
                            + $" / 被ダメ増幅 {tk} 回・+{hl} HP");
                long vc = MetaProgression.MetaDebuffApplicator.VoidTileChecks;
                long vt = MetaProgression.MetaDebuffApplicator.VoidTileTriggers;
                sb.AppendLine($"[T4計装] 破綻: 有利マス判定 {vc} 回 (1ラン {vc / (double)Math.Max(1, runs):F1} 個)"
                            + $" / 空白化 {vt} 回 (1ラン {vt / (double)Math.Max(1, runs):F2} 個 / 実効 {100.0 * vt / Math.Max(1, vc):F1}%)");
                {
                    long jk = MetaProgression.MetaDebuffApplicator.JudgmentKills;
                    long jd = MetaProgression.MetaDebuffApplicator.JudgmentPostDamageTaken;
                    long jh = MetaProgression.MetaDebuffApplicator.JudgmentPostHeal;
                    var rf = MetaProgression.MetaDebuffApplicator.JudgmentReachByFloor;
                    long rfTot = 0; for (int f = 0; f < rf.Length; f++) rfTot += rf[f];
                    var rfs = new System.Text.StringBuilder();
                    for (int f = 1; f <= 7; f++)
                        rfs.Append($" {f}F {100.0 * rf[f] / Math.Max(1, rfTot):F1}%");
                    long hp = MetaProgression.MetaDebuffApplicator.JudgmentHealPrevented;
                    sb.AppendLine($"[T4計装] 審判: 致命打 {jk} 回"
                                + $" / 超過後 被ダメ {jd}・実回復 {jh}・被ダメ増幅 +{hl}"
                                + $" (増幅分は超過後被ダメの {100.0 * hl / Math.Max(1, jd):F1}%)");
                    sb.AppendLine($"[T4計装] 審判: 刻限超過ランの到達層 ({rfTot} ラン)" + rfs);
                    long dn = MetaProgression.MetaDebuffApplicator.JudgmentDeadlineDeaths;
                    long sn = MetaProgression.MetaDebuffApplicator.JudgmentDeadlineSurvivors;
                    long dd = MetaProgression.MetaDebuffApplicator.JudgmentDeathDamage;
                    long dr = MetaProgression.MetaDebuffApplicator.JudgmentDeathHealRequested;
                    long da = MetaProgression.MetaDebuffApplicator.JudgmentDeathHealActual;
                    long sd = MetaProgression.MetaDebuffApplicator.JudgmentSurvivorDamage;
                    long sr = MetaProgression.MetaDebuffApplicator.JudgmentSurvivorHealRequested;
                    long sa = MetaProgression.MetaDebuffApplicator.JudgmentSurvivorHealActual;
                    sb.AppendLine($"[T4計装] 審判: 死亡 {dn}ラン 平均 被ダメ {dd / (double)Math.Max(1, dn):F1} / 回復要求 {dr / (double)Math.Max(1, dn):F1} / 実回復 {da / (double)Math.Max(1, dn):F1}");
                    sb.AppendLine($"[T4計装] 審判: 生存 {sn}ラン 平均 被ダメ {sd / (double)Math.Max(1, sn):F1} / 回復要求 {sr / (double)Math.Max(1, sn):F1} / 実回復 {sa / (double)Math.Max(1, sn):F1}");
                }
            }
            sb.AppendLine("※ 基準は 1 行目 (挑戦なし 0pt)。 * が付く段はその軸の最上位。");
            sb.AppendLine("※ **最終行 [ノイズ床] は 1 行目と設定が完全に同一・同一シード**。 その 7F差分が");
            sb.AppendLine("   このバッチのアーム間ノイズの実測値。 **これより小さい差は読まないこと**。");
            sb.AppendLine("   (2026-08-10 実測で 4.0pt。 軸の効果と誤認しかけたため常設化した)");
            sb.AppendLine("※ **判断は McNemar p で行う**。 7F差分の絶対値だけを見ない (同一シードでも");
            sb.AppendLine("   7層クリアは低頻度事象なので、 有意でない ±数pt は普通に出る)。");
            sb.AppendLine("※ 『1pt当り』が他軸より大きく負の軸が **値段に見合わず重い**軸。");
            sb.AppendLine("   同じ軸の T1→T2→T3 でこの列が悪化するなら、 その軸は段が超線形に効いている。");
            sb.AppendLine("※ 3F止まりが基準より突出する軸は、 天井を下げるのでなく **序盤で詰ませて**いる。");
            sb.AppendLine("※ 経済列の読み方 (2026-08-10 に追加)。 価格倍率を上げても 7F 差分が動かないとき:");
            sb.AppendLine("   ・**残金が増えている** → 買えないのではなく **買う対象が尽きている**");
            sb.AppendLine("     (陳列は 12 枠しかない)。 価格では段を作れず、 触るべきは陳列枠。");
            sb.AppendLine("   ・**種別ごとの弾性が違う** → 価格に鈍い枠が分母を薄めている。");
            sb.AppendLine("     リロールは倍率非適用、 素材は 3×2^N×倍率 で log 弾性、 特売品は 20〜60% 引き。");
            sb.AppendLine("   ・購入計だけ見ると上の 2 つを区別できない。 **必ず内訳と残金を併せて読む**。");
            _challengeAxisSweepReport = sb.ToString();

            _pendingChallengeLoadout = null;
            _pendingPresetRelic = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
            // **計測用の上書きは必ず戻す。** 立ちっぱなしだと後続の測定が全部汚れる。
            MetaProgression.MetaDebuffApplicator.ShopPriceOverride = 0f;
        }

        /// <summary>ビルド別勝率スイープ。 **ペルソナだけを振って 5層/7層クリア率を比べる。**
        ///
        /// 周回モード (ascensionMode) は難易度ラチェットが入るので「そのビルドがどこまで
        /// 上げられたか」しか分からない。 ここは **条件を全部固定**して素の勝率を出す:
        ///   メタバフ Standard (Balanced 配分) / 遺物なし / 挑戦 0pt / 5層裏ボス遮断。
        ///
        /// **全ペルソナで同一シード** ＝ ペア比較。 判断は Standard を基準にした McNemar で行う
        /// (500 ランでも 5層クリアの標準誤差は ±2.2pt あり、 平均どうしでは数pt の差が読めない)。</summary>
        private IEnumerator RunPersonaSweep()
        {
            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;   // ペルソナ選好を効かせるため
            var prevMetaMode = metaBuffMode;
            metaBuffMode = MetaBuffMode.Standard;       // 「スタンダードで」= Balanced 配分に固定
            _ascensionPersonaLock = true;
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = true;

            int runs = Mathf.Max(50, personaSweepRuns);
            var personas = (BuildPersona[])Enum.GetValues(typeof(BuildPersona));
            ResetScreenProgress(personas.Length * runs, "ビルド別");
            var loadout = AscensionLoop.BuildLoadoutAtScore(0);

            var sb = new StringBuilder();
            sb.AppendLine("=== ビルド別勝率スイープ ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("メタバフ Standard (Balanced) / 遺物なし / 挑戦 0pt / 5層裏ボス遮断");
            sb.AppendLine($"1 ペルソナ {runs} ラン / 全ペルソナ同一シード (ペア比較)");
            sb.AppendLine("決定論シード: 0000 0000 0000 0000");
            sb.AppendLine();
            sb.AppendLine("ビルド      | 平均到達層 | 5層クリア | 7層クリア | 到達層分布 1F〜7F                          | 対Standard 5F改善/悪化 (p) | 7F改善/悪化 (p)");

            bool[] baseC5 = new bool[runs], baseC7 = new bool[runs];
            var rows = new List<(string label, string line, bool[] c5, bool[] c7)>();

            for (int pi = 0; pi < personas.Length; pi++)
            {
                var persona = personas[pi];
                curArmIndex = pi + 1; curArmCount = personas.Length;
                curRunsPerArm = runs; curArmLabel = persona.ToString();
                var reach = new int[9];
                int c5 = 0, c7 = 0; double sumFloor = 0;
                var thisC5 = new bool[runs];
                var thisC7 = new bool[runs];

                for (int i = 0; i < runs; i++)
                {
                    curRunInArm = i + 1;
                    curRunInArm = i + 1;
                    // **全ペルソナで同じ runIdx** ＝ ペア比較 (pi を混ぜない)。
                    int runIdx = 70000 + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = persona;
                    _pendingChallengeLoadout = loadout;
                    _pendingPresetRelic = null;
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    int cf = died ? reached - 1 : reached;
                    reach[Mathf.Clamp(reached, 0, 8)]++; sumFloor += reached;
                    if (cf >= 5) { c5++; thisC5[i] = true; }
                    if (cf >= 7) { c7++; thisC7[i] = true; }
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }

                if (persona == BuildPersona.Standard)
                {
                    System.Array.Copy(thisC5, baseC5, runs);
                    System.Array.Copy(thisC7, baseC7, runs);
                }

                var dist = new StringBuilder();
                for (int f = 1; f <= 7; f++) dist.Append($"{reach[f] * 100.0 / runs,5:F1}%");
                string line = $"{persona,-11} | {sumFloor / runs,10:F2} | {c5 * 100.0 / runs,8:F1}% | "
                            + $"{c7 * 100.0 / runs,8:F1}% |{dist} |";
                rows.Add((persona.ToString(), line, thisC5, thisC7));
            }

            // McNemar は Standard を全ペルソナぶん走らせ終えてから当てる (基準の順序に依存しないように)。
            foreach (var (label, line, c5arr, c7arr) in rows)
            {
                int b = 0, c = 0, b7 = 0, c7b = 0;
                for (int i = 0; i < runs; i++)
                {
                    if (c5arr[i] && !baseC5[i]) b++; else if (!c5arr[i] && baseC5[i]) c++;
                    if (c7arr[i] && !baseC7[i]) b7++; else if (!c7arr[i] && baseC7[i]) c7b++;
                }
                bool isBase = label == BuildPersona.Standard.ToString();
                sb.AppendLine(line + (isBase ? " —（基準） | —"
                    : $" {b,3}/{c,3} (p={McNemarP(b, c),6:F3}) | {b7,3}/{c7b,3} (p={McNemarP(b7, c7b),6:F3})"));
            }

            sb.AppendLine();
            sb.AppendLine("※ 基準は Standard。 平均どうしの比較はせず McNemar p で判断する。");
            sb.AppendLine("※ RawTier は Tier 表そのまま = ビルド選好なし。 ペルソナ選好の効果量はこことの差で読む。");
            _personaSweepReport = sb.ToString();

            _pendingChallengeLoadout = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            metaBuffMode = prevMetaMode;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
        }

        private static string Trunc(string s, int n)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));

        /// <summary>McNemar 検定の両側 p 値 (二項検定の厳密版)。 b / c は不一致ペアの数。
        ///
        /// 不一致ペアだけを見て「b が二項(n=b+c, p=0.5) からどれだけ外れているか」を測る。
        /// 一致ペア (両方クリア / 両方失敗) は情報を持たないので分母から落ちる ──
        /// これが同一シードのペア比較で分散が下がる理屈。</summary>
        private static double McNemarP(int b, int c)
        {
            int n = b + c;
            if (n == 0) return 1.0;
            int k = Mathf.Min(b, c);
            // 片側 = Σ_{i=0..k} C(n,i) / 2^n を対数で。 n が数百でも溢れない。
            double logHalfPow = -n * System.Math.Log(2.0);
            double sum = 0.0, logC = 0.0;              // logC = log C(n,0) = 0
            for (int i = 0; i <= k; i++)
            {
                if (i > 0) logC += System.Math.Log((double)(n - i + 1) / i);
                sum += System.Math.Exp(logC + logHalfPow);
            }
            return System.Math.Min(1.0, 2.0 * sum);    // 両側
        }

        /// <summary>遺物プリセットスイープ。 **遺物の強さ以外を全部固定して深度が動くかを測る。**
        ///
        /// 周回モードの「挑戦上限」はペルソナの素の強さで決まってしまい (序盤クリア率との相関
        /// r=+0.92 に対し 遺物効用とは r=+0.13)、 遺物の寄与を切り出せない。 ここでは
        /// **ペルソナも挑戦スコアも固定**し、 遺物プリセットだけを None→Mid→High→天井 と振る。
        /// これで「強い遺物を持ち込めば深く潜れるのか」に直接答えが出る。</summary>
        /// <summary>ADR-0010 Verification ①: 技量帯の実測。 **配線方策だけを振る**。
        ///
        /// 条件は基準値測定と完全に同一 (遺物なし・挑戦0pt・ペルソナ Standard・5層裏ボス遮断)。
        /// **同一の runIdx を各アームで使い回す** ので、 ラン単位で対応が付く ──
        /// 平均どうしを引き算するのではなく、 改善/悪化の件数を McNemar にかける
        /// (平均比較は分散に埋もれて数ポイントの差を判定できない)。
        ///
        /// 差が出ないなら本 ADR の目的 (技量帯の構築) を達成していない、 という判定に使う。</summary>
        private IEnumerator RunWiringSkillCompare()
        {
            var arms = (wiringSkillCompareArms != null && wiringSkillCompareArms.Length > 0)
                     ? wiringSkillCompareArms
                     : new[] { WiringSkill.Optimal, WiringSkill.Super };

            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            var prevSkill = wiringSkill;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = true;

            // 挑戦スコアを載せるときは**固定難易度スイープと同条件**へ揃える (遺物 理論最良)。
            //   0pt の基準値条件と混ぜると「遺物なしの 30pt」という別物を測ってしまう。
            _pendingRelicExplicit = true;   // 0pt 側は **遺物なしを強制** する (null = 触らない ではない)
            var cmpRelic = wiringSkillCompareScore > 0
                ? RelicPresets.Build(RelicPresets.Preset.TheoreticalBestCursed) : null;
            // **0pt でも必ず非 null を渡す。** null は「0pt」ではなく「挑戦設定に触らない」を
            //   意味するので、 前の状態が残る。 単軸スイープは常に代入していて、
            //   それが両者で唯一残った前処理の差だった (2026-08-10 の乖離調査)。
            var cmpLoadout = AscensionLoop.BuildLoadoutAtScore(wiringSkillCompareScore);

            int runs = Mathf.Max(20, wiringSkillCompareRuns);
            ResetScreenProgress(arms.Length * runs, "技量比較");
            // ラン単位の結果 [アーム][ラン] = クリアした層 (死亡なら到達層-1)
            var cf = new int[arms.Length][];
            var reachAll = new int[arms.Length][];
            var elapsed = new double[arms.Length];

            var sb = new StringBuilder();
            sb.AppendLine("=== 配線技量スイープ (ADR-0010 Verification ①) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(wiringSkillCompareScore > 0
                ? $"遺物 TheoreticalBestCursed / 挑戦{wiringSkillCompareScore}pt / ペルソナ Standard 固定 / 5層裏ボス遮断 / 各 {runs} ラン"
                : $"遺物なし / 挑戦0pt / ペルソナ Standard 固定 / 5層裏ボス遮断 / 各 {runs} ラン");
            sb.AppendLine("**全アームで同一 runIdx** ＝ ラン単位で対応が付く (McNemar 可能)");
            sb.AppendLine(_stateLineAtStart);
            _probeTrace = "";
            sb.AppendLine();

            for (int ai = 0; ai < arms.Length; ai++)
            {
                wiringSkill = arms[ai];
                curArmIndex = ai + 1; curArmCount = arms.Length;
                curRunsPerArm = runs; curArmLabel = wiringSkill.ToString();
                usePowerBudget = wiringSkillCompareBudget == null
                              || ai >= wiringSkillCompareBudget.Length
                              || wiringSkillCompareBudget[ai];
                cf[ai] = new int[runs];
                reachAll[ai] = new int[runs];
                var t0 = DateTime.Now;

                for (int i = 0; i < runs; i++)
                {
                    curRunInArm = i + 1;
                    // **同じ runIdx を全アームで使う。** これがペア比較の土台。
                    //   2026-08-10: シード帯を単軸スイープ (60000+i) へ揃えた。 同一条件のはずの
                    //   両スイープで基準値が 53.3% と 16.6% に割れ、 差の残りがシード帯だけだったため。
                    //   **0 始まりの低い runIdx で乱数列が縮退している疑い**を切り分ける。
                    int runIdx = wiringSkillCompareIndexBase + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    // **UnityEngine.Random もラン単位で固定する** (2026-08-10)。
                    //   GameRng は BeginRun で完全にリセットされるが、 UnityEngine.Random の
                    //   静的状態はラン跨ぎで残り、 **それまでに何回引かれたか**に依存する。
                    //   スイープ内は呼び出し履歴が同一なので再現する (ノイズ床 0/300) 一方、
                    //   別のスイープ関数から回すと履歴が変わって同一シードのランが割れていた。
                    UnityEngine.Random.InitState(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    _pendingPresetRelic = cmpRelic;
                    _pendingChallengeLoadout = cmpLoadout;
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    reachAll[ai][i] = reached;
                    cf[ai][i] = died ? reached - 1 : reached;
                    // 乖離調査 (2026-08-10): 同一 runIdx のランが両スイープで同じ結果になるか。
                    if (ai == 0 && i < 10)
                        _probeTrace += $" #{runIdx}:{reached}{(died ? "x" : "o")}[{_probeRunConfig}]";

                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                elapsed[ai] = (DateTime.Now - t0).TotalSeconds;
                Debug.Log($"[技量スイープ] {arms[ai]} 完了 ({ai + 1}/{arms.Length}) "
                        + $"{elapsed[ai]:F0}秒 / {elapsed[ai] / runs:F2}秒per ラン");
            }

            // ---- 集計 ----
            sb.AppendLine("技量       | 平均到達層 | 5層クリア | 7層クリア | 秒/ラン");
            for (int ai = 0; ai < arms.Length; ai++)
            {
                double sum = 0; int c5 = 0, c7 = 0;
                for (int i = 0; i < runs; i++)
                {
                    sum += reachAll[ai][i];
                    if (cf[ai][i] >= 5) c5++;
                    if (cf[ai][i] >= 7) c7++;
                }
                string label = arms[ai] + (wiringSkillCompareBudget != null
                    && ai < wiringSkillCompareBudget.Length && wiringSkillCompareBudget[ai] ? "+要求戦力" : "");
                sb.AppendLine($"{label,-14} | {sum / runs,10:F2} | {100.0 * c5 / runs,8:F1}% | "
                            + $"{100.0 * c7 / runs,8:F1}% | {elapsed[ai] / runs,7:F2}");
            }

            // ---- ペア比較 (McNemar) ----
            //   b = 前者だけがクリア / c = 後者だけがクリア。 χ² = (|b−c|−1)² / (b+c) (連続補正)。
            sb.AppendLine();
            sb.AppendLine("【ペア比較 (McNemar・連続補正)】 同一シードのラン単位で改善/悪化を数える");
            sb.AppendLine("比較                     | 指標   | 後者のみ | 前者のみ |    χ² |     p | 到達層 改善/悪化");
            for (int x = 0; x < arms.Length; x++)
                for (int y = x + 1; y < arms.Length; y++)
                {
                    for (int m = 0; m < 2; m++)
                    {
                        int need = m == 0 ? 5 : 7;
                        int b = 0, c = 0;
                        for (int i = 0; i < runs; i++)
                        {
                            bool px = cf[x][i] >= need, py = cf[y][i] >= need;
                            if (px && !py) c++; else if (!px && py) b++;
                        }
                        double chi = (b + c) > 0 ? Math.Pow(Math.Abs(b - c) - 1, 2) / (b + c) : 0.0;
                        double p = ChiSqP1(chi);
                        int up = 0, dn = 0;
                        for (int i = 0; i < runs; i++)
                        {
                            if (reachAll[y][i] > reachAll[x][i]) up++;
                            else if (reachAll[y][i] < reachAll[x][i]) dn++;
                        }
                        sb.AppendLine($"{arms[x]}→{arms[y],-12} | {need}層   | {b,8} | {c,8} | "
                                    + $"{chi,5:F2} | {p,5:F3} | {up}/{dn}");
                    }
                }
            sb.AppendLine();
            sb.AppendLine($"[先頭10ラン 到達層]{_probeTrace}");
            sb.AppendLine("※ 「後者のみ」が「前者のみ」を上回れば後者が強い。 p<0.05 で有意。");
            sb.AppendLine("※ 到達層 改善/悪化 は同一シードで到達層が上がった/下がったラン数。");

            wiringSkill = prevSkill;
            usePowerBudget = true;
            itemPickMode = prevPickMode;
            _ascensionPersonaLock = false;
            _pendingPresetRelic = null;
            _pendingRelicExplicit = false;
            _pendingChallengeLoadout = null;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;

            _skillSweepReport = sb.ToString();
            Debug.Log($"[技量スイープ] 完了\n{_skillSweepReport}");
        }

        /// <summary>自由度 1 のカイ二乗 上側確率。 誤差関数の近似 (Abramowitz-Stegun 7.1.26) を使う。
        /// p = erfc(sqrt(χ²/2))。 有意判定の目安が出れば十分なので、 これ以上の精度は要らない。</summary>
        private static double ChiSqP1(double chi)
        {
            if (chi <= 0) return 1.0;
            double x = Math.Sqrt(chi / 2.0);
            double t = 1.0 / (1.0 + 0.3275911 * x);
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t
                              - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return Math.Max(0.0, Math.Min(1.0, 1.0 - y));
        }

        private IEnumerator RunRelicPresetSweep()
        {
            // **遺物なしがバランスの基準値** (2026-08-04 決定)。 遺物は周回引継ぎなので初回
            //   プレイヤーは 0 個。 最良遺物を前提に調整すると、 遺物が報酬ではなく
            //   「持っていないと基準に届かない前提条件」になる。 素の 7 層クリア率の目標は 2 割強。
            //   baselineOnly は調整ループ用 ── 1/5 のラン数で基準値だけを速く出す。
            var presets = relicSweepBaselineOnly
                ? new[] { RelicPresets.Preset.None }
                : new[]
                {
                    RelicPresets.Preset.None,
                    RelicPresets.Preset.Mid,
                    RelicPresets.Preset.High,
                    RelicPresets.Preset.TheoreticalBest,
                    RelicPresets.Preset.TheoreticalBestCursed,
                };

            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;      // ペルソナ固定 = 交絡を断つ
            // 5層裏ボスを遮断する。 レイピアを引いたランだけ 5層の難度が跳ね上がり、
            // 基準値の分散源になるため (2026-08-05)。 **基準値測定のときだけ。**
            // 2026-08-15: Begin() が既定 true を敷くようになったので、 非基準アームは
            //   ここで明示的に false へ戻す (遺物の効きを裏ボス込みで見るのが本来の意図)。
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = relicSweepBaselineOnly;

            int runs = Mathf.Max(50, relicSweepRuns);
            ResetScreenProgress(presets.Length * runs, "遺物プリセット");
            var sb = new StringBuilder();
            sb.AppendLine("=== 遺物プリセットスイープ (§15-5) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ペルソナ Standard 固定 / 挑戦スコア 0 固定 / 1 プリセット {runs} ラン");
            sb.AppendLine("決定論シード: 0000 0000 0000 0000");
            sb.AppendLine();
            sb.AppendLine("プリセット             pt/枠 | 平均到達層 | 5層クリア | 7層クリア | 到達層分布 1F〜7F");
            var effRows = new List<string>();

            for (int pi = 0; pi < presets.Length; pi++)
            {
                var preset = presets[pi];
                curArmIndex = pi + 1; curArmCount = presets.Length;
                curRunsPerArm = runs; curArmLabel = preset.ToString();
                var relic = RelicPresets.Build(preset);
                var reach = new int[9];
                int cleared5 = 0, cleared7 = 0;
                double sumFloor = 0;
                // **序盤ファーム効率**をプリセットごとに切り出す。 生存だけでは
                // 「強い遺物で序盤を効率よく回せるのか」に答えられない。
                int recBase = _records.Count;
                CombatSystem.CombatManager.ResetFloorDamage();

                for (int i = 0; i < runs; i++)
                {
                    curRunInArm = i + 1;
                    // **BeginRun を必ず呼ぶ** (理由は RunChallengeFixedSweep 側のコメント参照)。
                    int runIdx = pi * runs + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    _pendingPresetRelic = relic;             // RunOne 内の ResetAll 直後に適用される
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    int clearedFloor = died ? reached - 1 : reached;
                    reach[Mathf.Clamp(reached, 0, 8)]++;
                    sumFloor += reached;
                    if (clearedFloor >= 5) cleared5++;
                    if (clearedFloor >= 7) cleared7++;

                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }

                int pts = relic != null ? relic.TotalPoints : 0;
                int slots = relic != null ? relic.SlotCount : 0;
                var dist = new StringBuilder();
                for (int f = 1; f <= 7; f++) dist.Append($"{reach[f] * 100.0 / runs,5:F1}%");
                sb.AppendLine($"{preset,-22} {pts,2}/{slots} | {sumFloor / runs,10:F2} | "
                            + $"{cleared5 * 100.0 / runs,8:F1}% | {cleared7 * 100.0 / runs,8:F1}% |{dist}");

                // ── 序盤ファーム効率 ──
                double gold = 0, buys = 0; int n = 0;
                for (int r = recBase; r < _records.Count; r++)
                { gold += _records[r].totalGoldGained; buys += _records[r].shopPurchases; n++; }
                var fd = CombatSystem.CombatManager.FloorDamage;
                // 1〜3F の通常+エリート戦だけを見る (ボスは長さの性質が違うので除く)
                double atk = 0, fights = 0, dmgSum = 0;
                for (int f = 1; f <= 3; f++)
                    for (int k = 0; k < 2; k++)
                    { atk += fd[f, k, 0]; fights += fd[f, k, 6]; dmgSum += fd[f, k, 1]; }
                effRows.Add($"{preset,-22} {pts,2}/{slots} | {(n > 0 ? gold / n : 0),9:F1} | "
                          + $"{(n > 0 ? buys / n : 0),8:F2} | {(fights > 0 ? atk / fights : 0),9:F2} | "
                          + $"{(atk > 0 ? dmgSum / atk : 0),10:F1}");
            }

            sb.AppendLine();
            sb.AppendLine("=== 経済と序盤効率 (GOLD/購入は**ラン全体**・ターン/与ダメは1〜3F通常+エリートのみ) ===");
            sb.AppendLine("プリセット             pt/枠 | GOLD総獲得 | 購入回数 | 平均ターン | 1攻撃与ダメ");
            foreach (var row in effRows) sb.AppendLine(row);

            sb.AppendLine();
            sb.AppendLine("※ 遺物以外は完全に同一。 深度が動かないなら「強い遺物で押し切る」は成立しない。");
            _relicSweepReport = sb.ToString();

            _pendingPresetRelic = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;
        }

        /// <summary>周回モード (§15-5): ペルソナごとに独立したラインを走らせる。
        ///
        /// 1 ラインの中では **ペルソナを固定**する ── 遺物はそのペルソナの重みで蓄積するので、
        /// 途中で入れ替わると効用の比較が意味を失う。
        ///
        /// ラン前に遺物と挑戦構成を State へ入れ、 ラン後に遺物を引いて乗り換え判定と
        /// 難易度判定を行う。 到達層とクリア判定は **実ゲームの結果** (_cur) を使う。</summary>
        /// <summary>サマリーに出す計装 static をバッチ頭で落とす。
        ///
        /// **プロセス横断の static なので、 落とさないと前バッチの値が混ざる**
        /// (2026-08-10 の <c>_combatSeq</c> と同じ罠)。 かつては <see cref="RunAscension"/> だけが
        /// この処理を持っていて、 **通常バッチは落としていなかった** ── 同じ Editor セッションで
        /// 2 回目以降に回したバッチの【役の実測】【充電経済】は、 前のバッチと**プールされた値**
        /// だったことになる。 両方の入口から必ずここを通す。</summary>
        private void ResetBatchStatics()
        {
            CombatSystem.YachtRoleEffects.ResetStats();
            GameLoop.RunChronicle.ResetCounts();
            InventorySystem.PassiveSkills.CombatContext.ResetChargeStats();
            CombatSystem.CombatManager.PassiveUpkeepDue = CombatSystem.CombatManager.PassiveUpkeepPaid = 0;
            System.Array.Clear(CombatSystem.CombatManager.RerollPerTurn, 0, CombatSystem.CombatManager.RerollPerTurn.Length);
            System.Array.Clear(CombatSystem.CombatManager.RerollPerTurnYacht, 0, CombatSystem.CombatManager.RerollPerTurnYacht.Length);
            CombatSystem.CombatManager.ResetFirstRollStats();
            System.Array.Clear(CombatSystem.CombatManager.RerollStats, 0,
                               CombatSystem.CombatManager.RerollStats.Length);
            CombatSystem.CombatManager.ResetDamageBreakdown();
            SuperCombatAI.ResetSearchStats();
            ResetNavStats();
            AutoTest.FamilyTierStats.Reset();
        }

        private IEnumerator RunAscension()
        {
            _ascensionTraces.Clear();
            ResetBatchStatics();
            // 自動獲得を止める。 周回モードは自前で RelicRoller を呼ぶので二重取得になるうえ、
            // 毎ラン PlayerPrefs.Save() が走るとバッチが極端に遅くなる。
            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;

            // 周回モードはペルソナを固定するので、 ラン毎の抽選を止める。
            // itemPickMode は BuildFocused でなければペルソナがアイテム選好に効かないので合わせる。
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            _ascensionPersonaLock = true;

            int runs = Mathf.Max(10, ascensionRunsPerPersona);
            ResetScreenProgress(AscensionPersonas.Length * runs, "周回モード");
            for (int p = 0; p < AscensionPersonas.Length; p++)
            {
                var persona = AscensionPersonas[p];
                curArmIndex = p + 1; curArmCount = AscensionPersonas.Length;
                curRunsPerArm = runs; curArmLabel = persona.ToString();
                _currentPersona = persona;                       // ライン中は固定
                _ascension = new AscensionLoop(persona, ascensionPolicy, ascensionWindow);
                _ascension.Reset();

                var tr = new AscensionTrace { persona = persona };
                _ascensionTraces.Add(tr);

                for (int i = 0; i < runs; i++)
                {
                    curRunInArm = i + 1;
                    _currentPersona = persona;                   // RunOne が上書きしないよう毎回入れ直す
                    // BeforeRun は RunOne の中 (ResetAll の直後) で呼ぶ。 ここで呼ぶと消される。
                    yield return RunOne(p * runs + i);

                    // **_cur はランの終端で null 化される**ので、 確定済みレコードから読む。
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;

                    // **死んだ層はクリアしていない。** currentFloor は「死んだ時にいた層」なので、
                    // クリア済みの最深層は 1 つ下になる。 踏破 (GameOver 以外) ならその層まで。
                    int clearedFloor = died ? reached - 1 : reached;
                    // §15-5 の層点は 5 層クリア以降だけが跳ねる。 4 層以下のクリアは死亡と同格なので、
                    // 「5 層以上をクリアしたか」を cleared として渡し、 それ未満は到達層で評価する。
                    bool cleared = clearedFloor >= 5;
                    int layerFloor = cleared ? clearedFloor : reached;
                    _ascension.AfterRun(layerFloor, cleared);

                    tr.score.Add(_ascension.ChallengeScore);
                    tr.util.Add(_ascension.CurrentUtility);
                    tr.floor.Add(reached);
                    tr.win.Add(cleared);

                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                tr.swaps     = _ascension.SwapCount;
                tr.upCount   = _ascension.DifficultyUpCount;
                tr.downCount = _ascension.DifficultyDownCount;
                tr.finalRelic = _ascension.CurrentRelic != null
                              ? _ascension.CurrentRelic.Describe() : "遺物なし";
            }

            _ascension = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
            WriteAscensionReport();
        }

        /// <summary>周回モードのレポート。 収束したか / 停滞したか / 発散したかを読めるように、
        /// **前半と後半を分けて**出す（平均だけだと途中の伸びが潰れる）。</summary>
        private void WriteAscensionReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 周回モード (遺物 §15-5) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ポリシー: {ascensionPolicy}  窓幅 {ascensionWindow}  "
                        + $"1ペルソナ {ascensionRunsPerPersona} ラン × {AscensionPersonas.Length} = "
                        + $"{ascensionRunsPerPersona * AscensionPersonas.Length} ラン");
            sb.AppendLine(GameLoop.GameRng.IsSeeded
                ? $"決定論シード: {GameLoop.GameRng.FormatSeed(GameLoop.GameRng.MasterSeed)}" : "シード: 未固定");
            sb.AppendLine();
            // **双方向ラチェット (2026-08-04) 以降、「終」は意味を持たない。**
            //   上げ下げを繰り返すので run1000 時点の値はただのスナップショット。
            //   到達できた難易度を見るなら **最高値**、 落ち着き先を見るなら **後半の中央値**。
            sb.AppendLine("ペルソナ    | 挑戦pt 最高/後半中央/終 | 効用 初→終 | 5層クリア率 前半→後半 | 乗換 難度↑↓");
            sb.AppendLine(new string('-', 104));

            foreach (var tr in _ascensionTraces)
            {
                int n = tr.score.Count;
                if (n == 0) continue;
                int half = Mathf.Max(1, n / 2);
                float winEarly = 0f, winLate = 0f;
                for (int i = 0; i < half; i++)      if (tr.win[i]) winEarly++;
                for (int i = half; i < n; i++)      if (tr.win[i]) winLate++;
                winEarly = winEarly / half * 100f;
                winLate  = winLate / Mathf.Max(1, n - half) * 100f;

                int peak = 0;
                for (int i = 0; i < n; i++) if (tr.score[i] > peak) peak = tr.score[i];
                var lateSorted = new List<int>();
                for (int i = half; i < n; i++) lateSorted.Add(tr.score[i]);
                lateSorted.Sort();
                int lateMed = lateSorted.Count > 0 ? lateSorted[lateSorted.Count / 2] : 0;

                sb.AppendLine($"{tr.persona,-10} |   {peak,3} / {lateMed,3} / {tr.score[n - 1],-3}       | "
                            + $"{tr.util[0],4:F1} → {tr.util[n - 1],-4:F1} | "
                            + $"{winEarly,5:F1}% → {winLate,5:F1}%        | "
                            + $"{tr.swaps,3}  ↑{tr.upCount,2} ↓{tr.downCount,2}");
            }

            // オーバーロード (§23-10) が実際に撃たれているかの確認。
            // バッチ中は Debug.Log が抑止されるので、 数字で残さないと検証できない。
            // 与ダメージの内訳。 **実機の火力がどこから来ているか**を数字で残す。
            // §15-5 の遺物単位を机上の参照状態で較正して 5 倍ずれた経緯があるため (2026-08-03)。
            var db = CombatSystem.CombatManager.DamageBreakdown;
            if (db[0] > 0)
            {
                double n = db[0];
                sb.AppendLine();
                sb.AppendLine("=== 与ダメージの内訳 (1 攻撃あたり平均) ===");
                sb.AppendLine($"  武器素火力 {db[1] / n,6:F1}  攻撃端子の出目 {db[2] / n,6:F1}  "
                            + $"パッシブ加算 {db[3] / n,6:F1}  消費 {db[4] / n,5:F1}  遺物 {db[5] / n,5:F1}");
                sb.AppendLine($"  → atkBase {db[6] / n,6:F1}   最終与ダメ {db[7] / n,6:F1}   "
                            + $"倍率 ×{db[7] / System.Math.Max(1.0, db[6]),5:F2}");
                sb.AppendLine($"  会心率 {db[8] * 100 / n,5:F1}%   固定ダメ {db[9] / n,5:F1}   "
                            + $"追撃 {db[10] / n,5:F1}");
                sb.AppendLine($"  与ダメ倍率の内訳: パッシブ由来 ×{db[11] / n,4:F2} "
                            + $"(+業物 {db[12] / n,4:F2} +メタ {db[13] / n,4:F2} "
                            + $"+遺物 {db[15] / n,4:F2}) "
                            + $"= ×{db[16] / n,4:F2}   最大観測 ×{db[18],4:F2}");
                sb.AppendLine($"  平均 消費GOLD {db[17] / n,6:F0}  "
                            + $"→ 黄金卿の剣を持つなら outgoing +{db[17] / n * 0.01,4:F2} 相当");
                sb.AppendLine($"  **1 ターンあたりの総与ダメ ≒ {(db[7] + db[9]) / n,6:F1}**  "
                            + $"(§15-5 の参照状態が想定していた値: 21.4)");
            }

            // 総倍率の**段別**分解。 会心 × outgoing だけでは総倍率に届かなかったため
            // (残差 ×1.27 の所在不明・2026-08-03)、 修飾チェーンの各段の平均倍率を実測する。
            var st = CombatSystem.CombatManager.DamageStages;
            if (st[0] > 0)
            {
                double sn = st[0];
                string[] label =
                {
                    "A atkBase", "B damageBonus+与ダメパッシブ", "C 追撃加算", "D 会心/鈍器",
                    "E outgoing", "F 鬼火の油+攻撃バースト", "G 練度不足", "H 敵軽減パッシブ",
                    "I 基礎防御", "J 利刃 余剰貫通", "K Λ微妙な手応え", "L 脆弱",
                    "M 最低保証", "N 狂暴化", "O 俊敏/防御スタンス",
                };
                sb.AppendLine();
                sb.AppendLine("=== 総倍率の段別分解 (各段の通過後平均と、 直前段との比) ===");
                for (int i = 1; i <= 15; i++)
                {
                    double cur = st[i] / sn;
                    if (i == 1) { sb.AppendLine($"  {label[0],-24} {cur,8:F2}"); continue; }
                    double prev = st[i - 1] / sn;
                    double ratio = prev > 0.0001 ? cur / prev : 0;
                    string mark = ratio > 1.03 || ratio < 0.97 ? " ★" : "";
                    sb.AppendLine($"  {label[i - 1],-24} {cur,8:F2}   ×{ratio,5:F3}{mark}");
                }
                sb.AppendLine($"  → A から O までの総倍率 ×{st[15] / System.Math.Max(1.0, st[1]),5:F2}");
                double critMul = st[17] > 0 ? st[16] / st[17] : 0;
                sb.AppendLine($"  実測 会心倍率 平均 ×{critMul:F2} (基準 2.00 に固定ではない: メタ精密 r10 等)  "
                            + $"会心 {st[17] * 100 / sn:F1}%");
                sb.AppendLine($"  乗算時点の outgoing 平均 ×{st[22] / sn:F2} (研磨剤込み)   "
                            + $"余剰貫通 平均 +{st[23] / sn:P1}");
                sb.AppendLine($"  発動回数: 鈍器 {st[18]:F0} / 研磨剤 {st[19]:F0} / "
                            + $"脆弱 {st[20]:F0} / 狂暴化 {st[21]:F0}   (総攻撃 {sn:F0})");
            }

            // 層 × 敵種別の与ダメ分布。 **全層平均の 137 で敵HPを決めると序盤を壊す**ため、
            // 敵HP の再設計は必ずこの表を根拠にする (2026-08-03)。
            var fd = CombatSystem.CombatManager.FloorDamage;
            var fh = CombatSystem.CombatManager.FloorDamageHist;
            sb.AppendLine();
            sb.AppendLine("=== 層 × 敵種別の 1 攻撃あたり与ダメ (主+固定) ===");
            sb.AppendLine("層 種別    攻撃数    最小   p10   p50   p90   最大    平均 | 敵maxHP  HP/平均 | 平均T");
            string[] kindName = { "通常", "エリ", "ボス" };
            // f=0 は Λ層 (§14-1)。 currentFloor が 5 のままなので分離しないと 5F に混ざる。
            for (int f = 0; f <= 7; f++)
                for (int k = 0; k < 3; k++)
                {
                    double cnt = fd[f, k, 0];
                    if (cnt < 20) continue;   // 標本が薄い組み合わせは出さない
                    double avg = fd[f, k, 1] / cnt;
                    double ehp = fd[f, k, 4] / cnt;
                    double turns = fd[f, k, 6] > 0 ? fd[f, k, 0] / fd[f, k, 6] : 0;
                    double[] q = new double[3];
                    double[] want = { 0.10, 0.50, 0.90 };
                    int acc = 0, qi = 0;
                    for (int b = 0; b < 80 && qi < 3; b++)
                    {
                        acc += fh[f, k, b];
                        while (qi < 3 && acc >= cnt * want[qi]) q[qi++] = b * 10 + 5;
                    }
                    sb.AppendLine($"{(f == 0 ? "Λ " : f + "F")} {kindName[k]}  {cnt,8:F0}  {fd[f, k, 2],6:F0}{q[0],6:F0}{q[1],6:F0}"
                                + $"{q[2],6:F0}{fd[f, k, 3],7:F0}  {avg,6:F1} | {ehp,7:F0}  "
                                + $"{(avg > 0 ? ehp / avg : 0),6:F2} | {turns,5:F2}");
                }
            sb.AppendLine("※ HP/平均 = 敵maxHP ÷ 1攻撃の平均与ダメ ＝ **理論上の必要ターン数**。 1.0 未満は 1 撃で消える。");
            sb.AppendLine("※ 平均T = 攻撃回数 ÷ 戦闘数。 ロール敗北のターンは攻撃が無いので実ターン数の下限。");

            // ターン収支。 **敵が手番を得るようになった影響**と、 BOT がブロック端子へ
            // 配線し始めたか (ADR-0009 のジレンマが起動したか) を同時に見る。
            var ts = CombatSystem.CombatManager.TurnStats;
            sb.AppendLine();
            sb.AppendLine("=== 層 × 敵種別の ターン収支 ===");
            sb.AppendLine("層 種別    ターン   被ダメ/T  敵攻撃値  ブロック出目  遮断率 | 配線 攻/防/充  防配線率 | ctxHP runHP  HP残");
            for (int f = 0; f <= 7; f++)
                for (int k = 0; k < 3; k++)
                {
                    double t = ts[f, k, 0];
                    if (t < 20) continue;
                    double atkV = ts[f, k, 6] / t, blkV = ts[f, k, 7] / t;
                    sb.AppendLine($"{(f == 0 ? "Λ " : f + "F")} {kindName[k]}  {t,8:F0}   {ts[f, k, 5] / t,7:F2}  {atkV,7:F1}  "
                                + $"{blkV,10:F1}  {(atkV > 0 ? blkV / atkV : 0),5:P0} | "
                                + $"{ts[f, k, 2] / t,4:F2}/{ts[f, k, 3] / t,4:F2}/{ts[f, k, 4] / t,4:F2}  "
                                + $"{ts[f, k, 1] * 100 / t,6:F1}% | "
                                + $"{ts[f, k, 9] / t,5:F0} {ts[f, k, 10] / t,5:F0}  "
                                + $"{(ts[f, k, 9] > 0 ? ts[f, k, 8] / ts[f, k, 9] : 0),5:P0}");
                }
            sb.AppendLine("※ 遮断率 = ブロック端子の出目合計 ÷ 敵攻撃値。 1.00 を超えると被ダメ 0。");
            sb.AppendLine("※ 防配線率 = ブロック端子へ 1 本以上配線したターンの割合。 **ADR-0009 のジレンマが");
            sb.AppendLine("   起動しているかの指標** ── 1 ターンで決着していた頃はここが常時 0 に近かった。");

            // **どこで走が終わっているか**。 被ダメ/T が 1 前後しかないのにクリア率が落ちたので、
            // 「削られて死ぬ」以外の終わり方をしている可能性を潰す (2026-08-03)。
            if (_records.Count > 0)
            {
                var byFloor = new int[9];
                var byFloorBoss = new int[9];
                var byCause = new Dictionary<DeathCause, int>();
                var reachDist = new int[9];
                int died = 0;
                // Λ層 (§14-1) は currentFloor が 5 のままなので、 5F から切り出して別勘定にする。
                int lambdaDeaths = 0, lambdaEntered = 0;
                foreach (var r in _records)
                {
                    reachDist[Mathf.Clamp(r.reachedFloor, 0, 8)]++;
                    if (r.enteredLambda) lambdaEntered++;
                    if (r.outcome != Outcome.GameOver) continue;
                    died++;
                    byCause.TryGetValue(r.cause, out int c);
                    byCause[r.cause] = c + 1;
                    if (r.deathInLambda) { lambdaDeaths++; continue; }
                    int df = Mathf.Clamp(r.deathFloor, 0, 8);
                    byFloor[df]++;
                    if (r.deathInBossFight) byFloorBoss[df]++;
                }
                sb.AppendLine();
                sb.AppendLine($"=== ランの終わり方 (全 {_records.Count} ラン / 死亡 {died}) ===");
                sb.Append("到達層     ");
                for (int i = 1; i <= 8; i++) sb.Append($"{i}F{reachDist[i] * 100.0 / _records.Count,5:F1}% ");
                sb.AppendLine();
                sb.Append("死亡層     ");
                for (int i = 1; i <= 8; i++)
                    sb.Append($"{i}F{(died > 0 ? byFloor[i] * 100.0 / died : 0),5:F1}% ");
                sb.AppendLine();
                sb.Append("うちボス戦 ");
                for (int i = 1; i <= 8; i++)
                    sb.Append($"{i}F{(byFloor[i] > 0 ? byFloorBoss[i] * 100.0 / byFloor[i] : 0),5:F1}% ");
                sb.AppendLine();
                // **層別死亡率** = その層に到達したランのうち、 その層で死んだ割合。
                //   「死亡層」の分布は到達数に引きずられる (到達しなければ死にようがない) ので、
                //   層ごとの危険度を比べるならこちらを見る。 分母は reachedFloor >= i のラン数。
                sb.Append("到達数     ");
                var entered = new int[10];
                foreach (var r in _records)
                    for (int i = 1; i <= Mathf.Clamp(r.reachedFloor, 0, 8); i++) entered[i]++;
                for (int i = 1; i <= 8; i++) sb.Append($"{i}F{entered[i],6} ");
                sb.AppendLine();
                sb.Append("**層別死亡率** ");
                for (int i = 1; i <= 8; i++)
                    sb.Append($"{i}F{(entered[i] > 0 ? byFloor[i] * 100.0 / entered[i] : 0),5:F1}% ");
                sb.AppendLine();
                // **Λ層は 5F から切り出してある** (§14-1: 滞在中も currentFloor は 5)。
                //   混ぜると 5F エリートのバケツが Λ の再訪周回で膨らみ、 5 層自体の難度が読めない。
                sb.AppendLine($"Λ層       突入 {lambdaEntered,5} ラン ({lambdaEntered * 100.0 / _records.Count,4:F1}%)"
                            + $"  Λ内死亡 {lambdaDeaths,5}  → Λ死亡率 "
                            + $"{(lambdaEntered > 0 ? lambdaDeaths * 100.0 / lambdaEntered : 0),5:F1}%"
                            + $"  (全死亡に占める割合 {(died > 0 ? lambdaDeaths * 100.0 / died : 0),4:F1}%)");
                // 経済。 消費アイテムの価格を「同 Tier アイテムの半額」に直した (2026-08-04) ので、
                // 実際に買われるようになったかを見る。 買われていないなら価格ではなく優先度の問題。
                double gain = 0, fin = 0, peak = 0, buys = 0;
                foreach (var r in _records)
                { gain += r.totalGoldGained; fin += r.finalCoins; peak += r.peakCoins; buys += r.shopPurchases; }
                double rn = _records.Count;
                sb.AppendLine($"GOLD: 総獲得 {gain / rn,6:F1}  ピーク所持 {peak / rn,6:F1}  終了時残 {fin / rn,6:F1}"
                            + $"  → 実消費 {(gain - fin) / rn,6:F1}   ショップ購入 {buys / rn,5:F2} 回/ラン");

                // 最大HP が道中で半分近くまで落ちている件の切り分け (2026-08-04)。
                // 最有力候補は **ラストスタンド** ([LastStand.cs] 発動時に最大HP を半減)。
                // 発動率と発動層が分かれば、 層別の平均最大HP の落ち方と突き合わせられる。
                var lsFloor = new int[9];
                int lsUsed = 0; double finMax = 0;
                foreach (var r in _records)
                {
                    finMax += r.finalMaxHP;
                    if (!r.lastStandUsed) continue;
                    lsUsed++;
                    lsFloor[Mathf.Clamp(r.lastStandFloor, 0, 8)]++;
                }
                sb.Append($"ラストスタンド: 発動 {lsUsed * 100.0 / rn,5:F1}% (最大HP半減)  終了時 最大HP 平均 {finMax / rn,5:F1}"
                        + $"  発動層 ");
                for (int i = 1; i <= 7; i++)
                    sb.Append($"{i}F{(lsUsed > 0 ? lsFloor[i] * 100.0 / lsUsed : 0),5:F1}% ");
                sb.AppendLine();
                sb.Append("死因: ");
                var causes = new List<KeyValuePair<DeathCause, int>>(byCause);
                causes.Sort((a, b) => b.Value.CompareTo(a.Value));
                for (int i = 0; i < causes.Count && i < 8; i++)
                    sb.Append($"{causes[i].Key} {causes[i].Value * 100.0 / System.Math.Max(1, died):F1}%  ");
                sb.AppendLine();
            }

            var rr = CombatSystem.CombatManager.RerollStats;
            sb.AppendLine();
            sb.AppendLine("=== リロール経済 (ADR-0010) ===");
            sb.AppendLine($"  回数 {rr[0]:N0}  振り直したダイス {rr[1]:N0}  消費充電 {rr[2]:N0}"
                        + (rr[0] > 0 ? $"  (1回あたり {rr[1] / (double)rr[0]:F2}個 / {rr[2] / (double)rr[0]:F2}充電)" : ""));
            if (rr[0] == 0)
                sb.AppendLine("  **一度も振り直していない** — 方策か充電収支を確認すること");
            sb.AppendLine();
            sb.AppendLine("=== 役の成立/発動 (ADR-0010 Verification ④) ===");
            sb.AppendLine(CombatSystem.YachtRoleEffects.DescribeStats());
            sb.Append(SuperCombatAI.DescribeSearch());

            sb.AppendLine();
            sb.AppendLine(GameLoop.RunChronicle.DescribeCounts(_ascensionTraces.Count));

            sb.AppendLine();
            sb.AppendLine("=== 各ラインの最終遺物 ===");
            foreach (var tr in _ascensionTraces)
                sb.AppendLine($"  {tr.persona,-10} {tr.finalRelic}");

            // 推移を 10 分割して出す。 収束/停滞/発散の形を目で見るため。
            sb.AppendLine();
            sb.AppendLine("=== 挑戦スコアの推移 (10 分割の各区間末) ===");
            sb.Append("ペルソナ    ");
            for (int k = 1; k <= 10; k++) sb.Append($"{k * 10,5}%");
            sb.AppendLine();
            foreach (var tr in _ascensionTraces)
            {
                sb.Append($"{tr.persona,-10}  ");
                int n = tr.score.Count;
                for (int k = 1; k <= 10; k++)
                {
                    int idx = Mathf.Clamp(n * k / 10 - 1, 0, n - 1);
                    sb.Append($"{tr.score[idx],5}");
                }
                sb.AppendLine();
            }
            sb.AppendLine();
            sb.AppendLine("=== 遺物の効用の推移 (同上) ===");
            foreach (var tr in _ascensionTraces)
            {
                sb.Append($"{tr.persona,-10}  ");
                int n = tr.util.Count;
                for (int k = 1; k <= 10; k++)
                {
                    int idx = Mathf.Clamp(n * k / 10 - 1, 0, n - 1);
                    sb.Append($"{tr.util[idx],5:F1}");
                }
                sb.AppendLine();
            }

            string text = sb.ToString();
            LogAlways(text);
            try
            {
                string dir = ResolveAutoRunOutputRoot();
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dir, $"ascension_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), text);
            }
            catch (Exception e) { Debug.LogWarning($"[周回モード] レポート保存に失敗: {e.Message}"); }
        }

        /// <summary>Λファーム量スイープ: lambdaFarmSweepValues の各値で runCount ラン回し、
        /// ファーム量別の Λ突入/6F到達/7Fクリア/解脱/死亡 を採取して表形式で出力する。</summary>
        private IEnumerator RunLambdaFarmSweep(GameManager gm)
        {
            ResetScreenProgress(lambdaFarmSweepValues.Length * runCount, "Λファーム");
            // **基準値測定と同じ条件へ揃える** (2026-08-05)。 遺物なし・挑戦0pt・ペルソナ固定。
            //   遺物やペルソナが混ざると Λ の滞在長以外の分散が支配して比較にならない。
            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = true;

            var sb = new StringBuilder();
            sb.AppendLine("=== Λ層 ファーム量スイープ ===");
            sb.AppendLine("条件: 遺物なし / 挑戦0pt / ペルソナ Standard 固定 / 5層裏ボス遮断");
            sb.AppendLine("撤退条件: -2=安全(lv2×2) / -1=現在(lv2×4) / -3=リスキー((lv2×6かつlv3×2) or lv3×4・迫りくる死lv3まで許容)");
            sb.AppendLine("          正の値は固定踏破マス数 (旧挙動)");
            sb.AppendLine($"日時      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"メタ進行: {metaPattern} / メタデバフ全ON: {enableAllDebuffs} / 各値 {runCount} ラン");
            sb.AppendLine(GameLoop.GameRng.IsSeeded
                ? $"決定論シード: {GameLoop.GameRng.FormatSeed(GameLoop.GameRng.MasterSeed)}"
                : "決定論シード: なし (毎回ランダム)");
            sb.AppendLine("※ Λ突入には〈決意〉以上が必要。突入したランのみが分母として意味を持つ。");
            sb.AppendLine("※ 中央離脱はスポーク(3マス毎)でのみ可能なため、実踏破マスは目標値を3の倍数へ丸めた値。");
            sb.AppendLine($"※ 期待Λデバフ付与回数 ≒ 踏破マス/{GameManager.LambdaDebuffInterval}（同種再付与で段階上昇）。");
            sb.AppendLine();
            sb.AppendLine("目標 | runs | Λ突入 | 6F到達 | 7Fクリア | 解脱 | 死亡 | 突入別6F% | 突入別7F% | 平均踏破 | 平均デバフLv計");
            sb.AppendLine("-----+------+-------+--------+----------+------+------+-----------+-----------+----------+--------------");

            for (int vi = 0; vi < lambdaFarmSweepValues.Length; vi++)
            {
                int v = lambdaFarmSweepValues[vi];
                curArmIndex = vi + 1; curArmCount = lambdaFarmSweepValues.Length;
                curRunsPerArm = runCount; curArmLabel = $"Λ目標 {v}";
                lambdaFarmTiles = v;
                int start = _records.Count;
                for (int i = 0; i < runCount; i++)
                {
                    curRunInArm = i + 1;
                    // **BeginRun を必ず呼ぶ。** 他スイープでは修正済みだったがここだけ漏れていた。
                    //   呼ばないとラン毎の RNG カウンタが持ち越され、 確信チェーンが縮退する
                    //   (実測: 900 ラン中 720 が stage1 で停滞・真理到達 21 のみ。 通常バッチは 4 割が真理)。
                    //   結果 Λ 突入が 4〜7% しか起きず、 滞在長の比較が成立していなかった (2026-08-05)。
                    //   index も値ごとに 0..N-1 で重複していたので通し番号にする。
                    int runIdx = vi * runCount + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    yield return RunOne(runIdx);
                    yield return null;
                }

                int runs = 0, entered = 0, reached6 = 0, full = 0, ged = 0, deaths = 0;
                long tilesSum = 0, dbgSum = 0;
                for (int k = start; k < _records.Count; k++)
                {
                    var r = _records[k];
                    runs++;
                    bool didEnter = r.lambdaTilesFarmed > 0 || r.hadResolveAt5F || r.hadTruthAt5F;
                    if (didEnter) entered++;
                    if (r.reached6F) reached6++;
                    if (r.outcome == Outcome.FullClear) full++;
                    if (r.outcome == Outcome.GameOver) deaths++;
                    tilesSum += r.lambdaTilesFarmed;
                    dbgSum += r.lambdaDebuffLevelSum;
                }
                float p6 = entered > 0 ? 100f * reached6 / entered : 0f;
                float p7 = entered > 0 ? 100f * full / entered : 0f;
                float avgTiles = runs > 0 ? (float)tilesSum / runs : 0f;
                float avgDbg = runs > 0 ? (float)dbgSum / runs : 0f;
                sb.AppendLine($"{v,4} | {runs,4} | {entered,5} | {reached6,6} | {full,8} | {ged,4} | {deaths,4} | {p6,8:F1}% | {p7,8:F1}% | {avgTiles,8:F1} | {avgDbg,13:F2}");
                Debug.Log($"[AutoRunner] Λスイープ farm={v}: 突入{entered}/{runs} 6F{reached6} 7F{full} 解脱{ged} 死{deaths}");
            }

            _lambdaSweepReport = sb.ToString();
            Debug.Log("[AutoRunner] Λファーム量スイープ完了\n" + _lambdaSweepReport);

            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;
        }

        private IEnumerator RunOne(int index)
        {
            var gm = GameManager.Instance;
            // [計装] 家系 Tier の梯子。 ラン内カウンタだけ落とす (累計は保つ)。
            AutoTest.FamilyTierStats.BeginRun();
            // 全ラン戦闘貪欲ルーチン（戦闘回避ルーチンは廃止）
            _curCombatAverse = false;

            // ビルド軸ペルソナ抽選 (2026-07-15)。RawTier = 従来動作、非RawTier は選好パッシブに Score+2 加点。
            if (_ascensionPersonaLock)
            {
                // 周回モード: ペルソナは RunAscension が固定済み。 抽選し直さない。
            }
            else if (useBuildPersonas)
            {
                // **決定論シード時はラン毎に派生シードで作り直す。** TickCount 由来の種を使い回すと
                // 同じマスターシードでもランに配られるペルソナ列が毎回変わり、 再現性が壊れる
                // (2026-07-29: A/B バッチが一致しなかった主因)。
                if (GameLoop.GameRng.IsSeeded)
                    _personaRng = new System.Random(
                        GameLoop.GameRng.Range(0, int.MaxValue, "autorun.persona", index));
                else if (_personaRng == null)
                    _personaRng = new System.Random(personaSeed == 0 ? System.Environment.TickCount : personaSeed);
                _currentPersona = BuildPersonaProfiles.Roll(rawTierRatio, _personaRng);
            }
            else
            {
                _currentPersona = BuildPersona.RawTier;
            }
            // メタバフ軸の一斉走査: ラン単位でラウンドロビン (等サンプル)。
            // ランダムではなく順送りにすることで、 中断しても軸ごとの本数が偏らない。
            if (metaBuffMode == MetaBuffMode.BuildFocused && sweepAllMetaAxes)
                _currentAxis = SweepAxes[index % SweepAxes.Length];
            else
                _currentAxis = metaAllocation;

            // 停止則のラン内実績。 **ランを跨いで持ち越さない** ── 持ち越すと run i の判断が
            //   それ以前のランに依存し、 チャンク分割と逐次で結果が変わる (決定性違反)。
            _rerollYieldSum = 0f; _rerollYieldN = 0;

            _cur = new RunRec
            {
                index = index, profile = "貪欲",
                persona = _currentPersona.ToString(),
                metaAxis = metaAllocation.ToString(),
            };
            GameLoop.HopeSystem.Stats.Reset(); // 希望の発生源別収支を1ラン単位で集計
            _curLog.Clear();
            _exceptionFlag = false;
            _exceptionMsg = null;
            _lastResolvedEvent = null;
            _pendingEnemyName = null;
            _eventStuckCount = 0;
            _lastEventInfo = "";
            _lambdaNavSteps = 0;

            // タイトルへ戻す（前ランが RunClear/GameOver 停止のままなら）
            int guard = 0;
            while (gm.CurrentPhase != GameManager.GamePhase.Title && guard++ < 50)
            {
                // 前ランが一択待ちのまま止まっていたら答えてから閉じる (答えないと RunClear に進まない)
                if (gm.CurrentPhase == GameManager.GamePhase.RiftChoice)
                    gm.ChooseRiftFate(false);
                if (gm.CurrentPhase == GameManager.GamePhase.GameOver ||
                    gm.CurrentPhase == GameManager.GamePhase.RunClear)
                    gm.ReturnToTitle();
                // ReturnToTitle は同期遷移。完了済みなら不要な1フレーム待ちを挟まない。
                if (gm.CurrentPhase == GameManager.GamePhase.Title)
                    break;
                yield return null;
            }

            // メタ恒久進行の前処理 (パターン別)
            try
            {
                switch (metaPattern)
                {
                    case MetaPattern.Cowardly:
                        MetaProgression.MetaProgressManager.Instance?.ResetAll();
                        break;
                    case MetaPattern.FullProgression:
                        // 2026-07-28: MaxAllForTesting() は予算 36pt に対し全 108 段を立てる
                        // (v6 の固定予算モデルへの追従漏れ)。 実在する 36pt 配分プリセットへ置換。
                        // 既定は Balanced ＝ 予算を初めて使い切ったプレイヤーの標準形。
                        // §13-3 のボス調整はこの配分に対して行う。
                        // **配分スペックが指定されていればそちらを優先** (2026-09-10)。
                        //   整備パネルはオッズ比 14〜21 で全軸中最大なのに、 どの軸が効いているかの
                        //   内訳が無い。 drop-one アブレーション用の注入口。
                        if (_metaAblationSpec != null)
                        {
                            MetaProgression.MetaProgressManager.Instance?.ResetAll();
                            MetaAllocationPresets.ApplyRanks(_metaAblationSpec, "ablation:" + curArmLabel);
                        }
                        else if (!string.IsNullOrWhiteSpace(metaRankSpec))
                        {
                            MetaProgression.MetaProgressManager.Instance?.ResetAll();
                            MetaAllocationPresets.ApplyRanks(
                                MetaAllocationPresets.ParseSpec(metaRankSpec), "spec:" + metaRankSpec);
                        }
                        else MetaAllocationPresets.Apply(metaAllocation);
                        break;
                    case MetaPattern.Untouched:
                        // 何もしない
                        break;
                }

                // 挑戦デバフ (docs/GAME.md §15-2)
                //   challengeSpec があればそれを適用、 無ければ enableAllDebuffs で 100pt / 全解除。
                //
                //   **周回モード中はここを飛ばす。** SetChallengeMax は State.challenge.Clear() を
                //   呼ぶので、 AscensionLoop が上げた難易度がラン開始のたびに消えてしまう。
                //   周回モードでは AscensionLoop が挑戦構成の唯一の所有者。
                if (!_ascensionPersonaLock)
                {
                    var mgr = MetaProgression.MetaProgressManager.Instance;
                    if (!string.IsNullOrWhiteSpace(challengeSpec)) ApplyChallengeSpec(mgr, challengeSpec);
                    else mgr?.SetChallengeMax(enableAllDebuffs);
                }
                else
                {
                    // 周回モード: **ここが ResetAll の直後**。 上の MetaAllocationPresets.Apply は
                    // ResetAll() で MetaProgressState を作り直すので、 遺物と挑戦構成の流し込みは
                    // 必ずこの位置で行う。 これより前に置くと毎ラン消える。
                    _ascension?.BeforeRun();
                }

                // 遺物プリセット / 挑戦構成をここで流し込む。
                //
                // **2026-09-05 修正: この流し込みは `_ascensionPersonaLock` の else 側に入っていた。**
                //   ＝ 周回/スイープモードでしか実行されず、 **通常バッチでは遺物が一度も乗らなかった**。
                //   `forceTheoreticalRelic` を立てて「遺物を理論値固定」とログに出しても、
                //   State には 1 個も入らない。 25 万ラン の学習を丸ごと「遺物なし」で回して発覚した
                //   (サマリーの `[実効状態]` 行が `遺物 0個 計0pt` を出し続けていた ── **設定ログではなく
                //   実効状態の行を読むこと**)。 if/else の外へ出して両方の経路で通す。
                //
                // **遺物の有無と挑戦構成は独立に扱う** (2026-08-09 修正)。 旧実装は
                //   丸ごと `if (_pendingPresetRelic != null)` の中にあったため、
                //   **遺物なしアームだけ挑戦スコアが 0 に落ちていた**。 実測: 挑戦30pt の
                //   単軸スイープで、 遺物なし行だけ 5層クリア 53.2% (他アームは 5% 前後) と
                //   0pt の値がそのまま出ていた。
                //
                // 位置は **SetChallengeMax の後**でなければならない ── 先に置くと
                //   `_pendingChallengeLoadout` が Clear() で消える。
                {
                    var st = MetaProgression.MetaProgressManager.Instance?.State;
                    if (st != null)
                    {
                        // **遺物は「明示的に管理する」と宣言したスイープでは必ず作り直す。**
                        //   2026-08-10: 旧実装は `_pendingPresetRelic != null` のときだけ
                        //   作り直していたため、 **null は「遺物なし」ではなく「触らない」**
                        //   を意味していた。 結果、 前セッションでランダムに転がった遺物が
                        //   そのまま残り、 「遺物なし」を名乗るスイープが遺物込みで走っていた。
                        //   実測: 同一シード・同一職業・同一武器なのに開始 HP が 113 と 93 に割れ、
                        //   0pt 基準値が 53.3% と 16.6% に分かれていた (合計 pt は両方 9pt で
                        //   一致していたため、 合計だけ見ても気づけない ── 配点先の軸が違う)。
                        if (_pendingRelicExplicit)
                        {
                            st.relics = new List<MetaProgression.Relics.RolledRelic>();
                            st.equippedRelicIndex = -1;
                            if (_pendingPresetRelic != null) st.AddRelic(_pendingPresetRelic);
                        }
                        if (_pendingChallengeLoadout != null)
                        {
                            st.challenge = _pendingChallengeLoadout;
                            st.InvalidateChallenge();
                        }
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[AutoRunner] Meta pattern={metaPattern}: {e.Message}"); }

            // **探索ランか否かをラン番号で決める (2026-08-17b)。** 乱数を使わないので再現する。
            //   探索ランは不偏な値付けだけに使い、 観測 lift の集計からは丸ごと外す。
            _isExploreRun = exploreHoldoutRate > 0f
                         && (index % 10) < Mathf.RoundToInt(exploreRunFraction * 10f);
            if (_cur != null) _cur.isExploreRun = _isExploreRun;

            // **アイテム評価学習を挑戦スコア帯へ切り替える (2026-08-17)。**
            //   挑戦構成を流し込んだ直後・ラン開始前がこの位置。 帯が変わったときだけ読み直す。
            //   スイープはアームごとに pt が変わるので、 ここを通さないと
            //   **前のアームの序列で買い続ける**。 Tier 較正中は隔離領域を使うので触らない。
            if (!IsTierCalibration)
            {
                try { LearnedPriorityProvider.SwitchToChallengeScore(CurrentChallengeScore()); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] 学習帯の切替失敗: {e.Message}"); }
            }

            // 2026-06-28: BOT は職業をランダムに選択 (BOT 学習で全 4 職を均等にサンプル)
            GameLoop.GameManager.SelectedClass = (GameLoop.ClassType)GameLoop.GameRng.RangeAuto("AutoRunner.1", 0, 4);
            // 特殊端子 (§6-5) もラン開始前の宣言。 **ペルソナに合わせて選ぶ** ──
            //   一様ランダムだと「どの端子がどのビルドで効くか」が測れず、
            //   全部を平均した薄い数字しか出ない。 ペルソナ無しのランだけ一様抽選にして
            //   11 種すべてがサンプルされるようにする。
            GameLoop.GameManager.SelectedSpecialTerminal = PickSpecialTerminal();
            gm.StartNewRun();
            // アイテム アブレーション: 開幕付与。 **StartNewRun の直後**でないと
            //   ownedPassiveItems がまだ存在しない。 AddPassiveItem は GameRng を
            //   1 つも消費しないので、 付与あり/なしで乱数列はずれない ＝ ペアが保たれる。
            if (!string.IsNullOrEmpty(_pendingGrantItemId) && gm.Run != null)
                InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(gm.Run, _pendingGrantItemId);

            // ランダム付与試行: このランの割り当てを引く。 引くだけで、 配るのは各層に着いてから。
            if (randomGrantTrial && gm.Run != null) RollGrantPlan(index, gm.Run);
            // 乖離調査 (2026-08-10): **ラン開始時点の構成**。 職業と特殊端子はここまでの
            //   GameRng 消費で決まるので、 ここが割れていれば乱数、 揃っていれば乱数より後の分岐。
            {
                var r = gm.Run;
                string pd = (r?.permanentDebuffs != null && r.permanentDebuffs.Count > 0)
                          ? string.Join("+", r.permanentDebuffs) : "なし";
                _probeRunConfig = $"{GameLoop.GameManager.SelectedClass}"
                                + $"/max{r?.playerMaxHP}"
                                + $"/整備パネルHP+{MetaProgression.MetaPanel.ShellHp(MetaProgression.MetaProgressManager.Instance?.State?.GetRank(MetaProgression.MetaPanelKind.Shell) ?? 0)}"
                                + $"/遺物HP+{MetaProgression.Relics.RelicApplicator.GetMaxHpBonus(r)}"
                                + $"/メタ{metaBuffMode}"
                                + $"/脆弱x{MetaProgression.MetaDebuffApplicator.GetMaxHpMultiplier():F2}"
                                + $"/恒久:{pd}";
            }
            // 先読み方策の内部乱数と較正をラン単位で戻す。 **ラン番号だけで決まる**ので、
            // 同一シードの再実行で AI の判断まで同じ列を辿る (決定性検査の対象に入る)。
            superAI.ResetForRun(index);
            ResetAblationRng(index);

            // --- Ultra: checkpoint からの再開 (§10 Phase E/G) ---
            //   **null なら 1 命令も実行されない。** 通常のバッチは従来どおり
            //   「StartNewRun した新規ラン」から始まる。
            if (ultraResumeFrom != null && !TryApplyUltraResume(gm, out string resumeFailure))
            {
                // 復元に失敗したランを「普通のラン」として集計へ混ぜると、
                // worker の故障が敗北として統計に入る。 明示的に落とす。
                Finish(Outcome.Crash, "Ultra resume 失敗: " + resumeFailure);
                yield break;
            }

            _prevCoins = gm.Run != null ? gm.Run.coins : 0;
            _prevMaterials = gm.Run != null ? gm.Run.weaponMaterials : 0;

            int iter = 0;
            int stall = 0;
            var lastPhase = gm.CurrentPhase;
            string lastNode = CurrentNodeId();
            int lastFloor = gm.Run?.currentFloor ?? 1;
            int lastHp = gm.Run?.playerHP ?? 0;
            // **番犬**: 1 ラン の実時間上限 (2026-08-10)。
            //   反復上限もストール検出も「状態が変わり続ける」形の固着には効かない
            //   ── 実際、 戦闘数が止まったまま 15 万フレーム回り続けるランが出た。
            //   バッチが 1 ラン で永久に止まると、 測定そのものが成立しない。
            float runStartRt = Time.realtimeSinceStartup;
            // **Ultra の思考時間は番犬から差し引く。** rollout は worker プロセスの完了を
            //   同期で待つので、 1 決定あたり数秒ブロックする。 46 決定/ラン なら 1 ラン は
            //   実時間で 2 分を超え、 20 秒の番犬に**全ラン**が DEADLOCK として刈られる
            //   ── 5 時間走らせて中身が全部打切、 という形の事故になる。
            //   番犬が捕まえたいのは「動き続ける固着」であって、 worker 待ちではない。
            _ultraThinkingSeconds = 0f;
            ClearUltraPending();   // 前ランの取り残しを持ち越さない

            while (true)
            {
                // 直前の Step が worker 待ちで空回りしたか。 **待機は「進展なし」ではない** ──
                //   反復上限にもストール検出にも数えない。 数えると待機がそのまま
                //   DEADLOCK 判定になり、 バッチ全体が打切で埋まる。
                bool waitingForUltra = _ultraWaiting;

                if (_exceptionFlag)
                {
                    Finish(Outcome.Crash, $"例外: {_exceptionMsg}");
                    yield break;
                }
                // 到達した層の付与を配る。 計画は空のことがほとんどなので毎周でも安い。
                if (_grantPlan.Count > 0 || _grantGoldAmount > 0) ApplyDueGrants(gm);
                if (!waitingForUltra && iter++ > maxIterationsPerRun)
                {
                    Finish(Outcome.Deadlock, $"反復上限超過 phase={gm.CurrentPhase} node={CurrentNodeId()}");
                    yield break;
                }
                if (runWatchdogSeconds > 0f
                    && Time.realtimeSinceStartup - runStartRt - _ultraThinkingSeconds > runWatchdogSeconds)
                {
                    // **打ち切ったことを必ず残す。** 黙って捨てると、 集計だけ見て
                    //   「そういう分布なのだ」と読んでしまう。
                    Finish(Outcome.Deadlock,
                        $"番犬 {runWatchdogSeconds:F0}s 超過 phase={gm.CurrentPhase} node={CurrentNodeId()} "
                      + $"floor={gm.Run?.currentFloor ?? -1} 戦闘={_cur?.totalCombats ?? -1} Λ歩={_lambdaNavSteps}"
                      + (_ultraThinkingSeconds > 0f ? $" Ultra思考={_ultraThinkingSeconds:F1}s(控除済)" : ""));
                    yield break;
                }

                var phase = gm.CurrentPhase;
                heartbeat++;                 // 本体ループが生きている証拠 (外部観測用)
                lastPhaseSeen = phase.ToString();

                // 進展のないストール検出
                string node = CurrentNodeId();
                int fl = gm.Run?.currentFloor ?? 0;
                int hp = gm.Run?.playerHP ?? 0;
                if (waitingForUltra)
                {
                    // 盤面は意図的に止まっている。 stall は据え置き (0 にも戻さない ──
                    // 待機前から続いていた停滞を待機で帳消しにしないため)。
                }
                else if (phase == lastPhase && node == lastNode && fl == lastFloor && hp == lastHp)
                {
                    if (++stall > stallLimit)
                    {
                        string ev = phase == GameManager.GamePhase.EventEncounter && !string.IsNullOrEmpty(_lastEventInfo)
                            ? $" event=[{_lastEventInfo}]" : "";
                        Finish(Outcome.Deadlock, $"ストール phase={phase} node={node} floor={fl}{ev}");
                        yield break;
                    }
                }
                else stall = 0;

                // 5F突入時 (4→5) にチェーン進行状況をスナップショット
                if (fl == 5 && lastFloor != 5 && _cur != null && _cur.convictionStageAt5F < 0 && gm.Run != null)
                {
                    var rs = gm.Run;
                    _cur.convictionStageAt5F = rs.convictionStage;
                    var owned = rs.ownedPassiveItems;
                    _cur.hadConvictionItem5F = owned != null && owned.Contains(GameLoop.ConvictionSystem.IdConviction);
                    _cur.hadResolveAt5F     = owned != null && owned.Contains(GameLoop.ConvictionSystem.IdResolve);
                    _cur.hadTruthAt5F       = owned != null && owned.Contains(GameLoop.ConvictionSystem.IdTruth);
                    var flags = rs.ownedFlags;
                    _cur.hadFlagYogenAt5F    = flags != null && flags.Contains("苦難の予言");
                    _cur.hadFlagKakushinAt5F = flags != null && flags.Contains("苦難の確信");
                }

                lastPhase = phase; lastNode = node; lastFloor = fl; lastHp = hp;

                // ラストスタンド発動検知
                TrackLastStand();
                TrackEconomy();

                bool finished = false;
                try
                {
                    finished = Step(phase);
                }
                catch (Exception e)
                {
                    Finish(Outcome.Crash, $"Step例外 phase={phase}: {e.GetType().Name} {e.Message}");
                    yield break;
                }

                if (finished) yield break;

                // **worker 待ちなら必ず 1 フレーム返す。** stepsPerYield は既定 1000 なので、
                //   ここを通さないと 1 フレームに 1000 回ポーリングして Editor が固まる
                //   ── 非同期にした意味がなくなる。
                if (_ultraWaiting)
                {
                    _innerStepCount = 0;
                    yield return null;
                    continue;
                }

                // 高速化: 1フレームあたり複数 Step を回す。
                // GameManager は同期遷移なので問題ないが、UI/演出が必要な場合は stepsPerYield=1 にする。
                _innerStepCount++;
                if (_innerStepCount >= Mathf.Max(1, stepsPerYield))
                {
                    _innerStepCount = 0;
                    yield return null;
                }
            }
        }

        private int _innerStepCount;

        /// <summary>現フェーズに対する1アクション。ラン終了時 true。</summary>
        private bool Step(GameManager.GamePhase phase)
        {
            var gm = GameManager.Instance;

            switch (phase)
            {
                case GameManager.GamePhase.Title:
                case GameManager.GamePhase.RunStart:
                case GameManager.GamePhase.FloorIntro:
                    return false; // GameManager が即座に MapNavigation へ遷移

                case GameManager.GamePhase.MapNavigation:
                    return DoNavigate();

                case GameManager.GamePhase.Combat:
                    // **フェーズが Combat なのに戦闘が動いていない状態を可視化する。**
                    //   ここが起きると RunCombatWithItems は即 return し、 ループが空回りして
                    //   「ストール phase=Combat」としか出ない ── 理由が残らないので追えない。
                    //   バッチ中は filterLogType=Error なので Error で出す (Log は落ちる)。
                    {
                        var cmProbe = CombatManager.Instance;
                        if (cmProbe == null || !cmProbe.IsCombatActive)
                        {
                            if (++_combatInactiveStreak == 20)
                                Debug.LogError("[AutoRunner] 戦闘フェーズだが戦闘が非アクティブ: "
                                    + $"cm={(cmProbe == null ? "null" : "有")} "
                                    + $"enemy={(cmProbe?.CurrentEnemy?.id ?? "null")} "
                                    + $"node={CurrentNodeId()} floor={gm.Run?.currentFloor} "
                                    + $"gmEnemy={(gm.CurrentEnemy?.id ?? "null")} "
                                    // 開始処理が「入った」通番と「完了した」通番。 食い違えば
                                    // StartCombatInternal が途中で抜けている。
                                    + $"startEntered={cmProbe?.StartCombatEntered} "
                                    + $"startCompleted={cmProbe?.StartCombatCompleted} "
                                    // 開始処理が完了しているのに非アクティブ ＝ 一度始まって
                                    // すぐ終わった。 どちらの HP が尽きていたかで原因が割れる。
                                    + $"pHP={cmProbe?.PlayerHP} eHP={cmProbe?.EnemyHP} "
                                    + $"runHP={gm.Run?.playerHP}/{gm.Run?.playerMaxHP} "
                                    + $"eMax={cmProbe?.CurrentEnemy?.maxHP}");
                        }
                        else _combatInactiveStreak = 0;
                    }
                    RunCombatWithItems(gm);
                    return false;

                case GameManager.GamePhase.BattleResult:
                    // 6Fクリア時のビルド情報を後でサルベージできるよう、撃破直後にスナップショット
                    bool floor6BossWinPending = gm.LastCombatResult.HasValue
                        && gm.LastCombatResult.Value.playerWon
                        && MapManager.Instance?.CurrentNode != null
                        && MapManager.Instance.CurrentNode.type == TileType.Boss
                        && gm.Run != null && gm.Run.currentFloor == 6;
                    // (旧 TryDropRapierOnFloor4Boss は撤去。レイピアは 4F イベント「亡霊との決闘」 で正規配布される)
                    gm.ConfirmBattleResult();
                    if (floor6BossWinPending) Capture6FClearSnapshot(gm);
                    return false;

                case GameManager.GamePhase.Reward:
                    // 戦闘後ドロップ2択: 未所持(ビルドの幅)を優先、同条件なら option a。
                    while (gm.HasPendingRewardChoice)
                    {
                        var (ra, rb) = gm.CurrentRewardChoice;
                        var owned = gm.Run?.ownedPassiveItems;
                        bool ownsA = owned != null && ra != null && owned.Contains(ra);
                        bool ownsB = owned != null && rb != null && owned.Contains(rb);
                        int pick = (ownsA && !ownsB) ? 1 : 0;
                        // Ultra: 2 択をそのまま委ねる。 sink 未接続なら pick は素通り。
                        pick = UltraOverrideChoice(gm,
                            AutoTest.Ultra.UltraDecisionPoint.RewardChoice,
                            AutoTest.Ultra.UltraActionKind.ChooseReward,
                            new AutoTest.Ultra.UltraChoiceView
                            {
                                kind = "reward",
                                prompt = "戦闘後ドロップ",
                                options = new[]
                                {
                                    new AutoTest.Ultra.UltraChoiceOption
                                    { index = 0, id = ra ?? "", label = ra ?? "" },
                                    new AutoTest.Ultra.UltraChoiceOption
                                    { index = 1, id = rb ?? "", label = rb ?? "" },
                                },
                            },
                            pick);
                        gm.ResolveRewardChoice(pick);
                    }
                    gm.ConfirmReward();
                    return false;

                case GameManager.GamePhase.RestStop:
                {
                    // 休憩は3択(食事/回復/強化)から1つ。生存(燃料→HP)優先、余裕あれば成長。
                    var run = gm.Run;
                    float hpR = run.playerMaxHP > 0 ? (float)run.playerHP / run.playerMaxHP : 1f;
                    int cost = GameManager.WeaponUpgradeCost(run);
                    // 希望(ADR-0002): 飢餓→希望統合で「食事」休憩は廃止。休憩は HP回復 or 武器強化のみ。
                    bool greedyBossPrep = !_curCombatAverse && _curBossNear;

                    // T4 到達率改善 v2: 強化を更に優先 (T4 追跡型)
                    //   ・素材が足りる && HPギリ余裕 → 必ず強化
                    //   ・T4 寸前 (cost ≤ 4 で次が T4) なら HP 0.25 でも強化
                    //   ・素材余剰 (≥2cost) なら HP 0.30 で強化
                    bool richMaterials = run.weaponMaterials >= cost * 2;
                    bool canUpgrade    = cost != int.MaxValue && run.weaponMaterials >= cost;
                    // T4 到達直前判定: 次の強化で T4 になる (現 T3+ → T4 等)
                    bool nextStepReachesT4 =
                        canUpgrade &&
                        !string.IsNullOrEmpty(run.equippedWeaponId) &&
                        run.equippedWeaponId.Contains("_t3") &&
                        run.weaponPlus > 0;
                    int restPick;
                    if (nextStepReachesT4 && hpR > 0.25f)
                        restPick = RestUpgradeIndex;         // T4 寸前は HP低めでも強化 (機会逃さない)
                    else if (richMaterials && hpR > 0.30f)
                        restPick = RestUpgradeIndex;         // 素材余剰: 危機未満なら強化を優先
                    else if (greedyBossPrep && hpR < 0.8f)
                        restPick = RestHealIndex;            // 貪欲: ボス前にHPを整える
                    else if (hpR <= 0.40f)
                        restPick = RestHealIndex;            // 低HPは回復
                    else if (canUpgrade)
                        restPick = RestUpgradeIndex;         // 中HP+素材有り → 強化優先
                    else
                        restPick = RestHealIndex;            // 満ちていれば回復で無駄なく（食事休憩は廃止）

                    // Ultra: 強化は素材が足りるときだけ**選択肢として出す**。
                    //   出したうえで本体に弾かれると、 controller が辞退した場合と区別が付かない。
                    restPick = UltraOverrideChoice(gm,
                        AutoTest.Ultra.UltraDecisionPoint.RestChoice,
                        AutoTest.Ultra.UltraActionKind.ChooseRest,
                        new AutoTest.Ultra.UltraChoiceView
                        {
                            kind = "rest",
                            prompt = "休憩",
                            options = new[]
                            {
                                new AutoTest.Ultra.UltraChoiceOption
                                { index = RestHealIndex, id = "heal", label = "HP回復" },
                                new AutoTest.Ultra.UltraChoiceOption
                                {
                                    index = RestUpgradeIndex, id = "upgrade", label = "武器強化",
                                    available = canUpgrade,
                                    cost = cost == int.MaxValue ? 0 : cost,
                                },
                            },
                        },
                        restPick);

                    // **`canUpgrade` で条件を足さないこと。** 元の分岐は
                    //   `richMaterials && hpR > 0.30f` で `RestUpgrade()` を呼んでおり、
                    //   `richMaterials` は `weaponMaterials >= cost * 2` ── 武器が最大強化だと
                    //   `cost == int.MaxValue` なので `cost * 2` が unchecked で −2 になり、
                    //   **常に true** になる。 つまり強化不能でも `RestUpgrade()` が呼ばれる
                    //   経路が既にあり、 ここに `&& canUpgrade` を足すと **その分が回復へ
                    //   移って既存の基準値が変わる**。 挙動は 1 ビットも変えない。
                    if (restPick == RestUpgradeIndex) gm.RestUpgrade();
                    else gm.RestHeal();
                    return false;
                }

                case GameManager.GamePhase.ShopVisit:
                    DoShop();
                    return false;

                case GameManager.GamePhase.EventEncounter:
                    DoEvent();
                    return false;

                case GameManager.GamePhase.TreasureOpen:
                case GameManager.GamePhase.TrapTriggered:
                    gm.ConfirmTileEvent();
                    return false;

                case GameManager.GamePhase.ExchangeTile:
                    // 交換は最低Tierパッシブ→上位ランダムの厳密アップグレード。所持があれば必ず交換。
                    if (gm.CanExchangeTile) gm.DoExchangeTile();
                    else gm.SkipExchangeTile();
                    return false;

                case GameManager.GamePhase.GateRitual:
                    // **方針: 3 工程とも払う (既定)。** 欠陥は避けられる限り避ける。
                    //   払えないとき (最大HP を割る / 遺物が 2 個未満 / 希望 40 未満) だけ
                    //   〈不完全な〜〉が付く。
                    //
                    //   **これは方策であって最適解ではない。** 旧儀式では「払う方が
                    //   −4.33pt で一方的に損」という測定結果が出た前例がある。
                    //   門は代償も罰も別物なので再測定が要る ── 工程ごとに払う/払わないを
                    //   切り替えるアームを組んで McNemar で比べること (§15-1 の配点スイープと同じ形)。
                    if (!gateBotSkipsAll)
                    {
                        gm.OfferGateBlood(gateBotPaysBlood);
                        gm.OfferGateRelics(gateBotPaysRelics);
                        gm.OfferGateTransfer(gateBotPaysTransfer);
                    }
                    // 素通り (gateBotSkipsAll) は**計測専用の対照群**。 工程を 1 つも解決しないので
                    //   代償も欠陥も無い ＝「門が無かったラン」。 製品では起こらない。
                    gm.CompleteGateRitual();
                    return false;

                case GameManager.GamePhase.FloorClear:
                    gm.ConfirmFloorClear();
                    return false;

                case GameManager.GamePhase.RiftChoice:
                    // ヴェスカ撃破後の一択 (2026-08-17)。 **必ず答えること** ── 放置すると
                    //   RunClear へ進まずバッチが止まる。
                    //   BOT は常に「閉じる」= TRUE END。 勝敗の計測値は分岐で変わらないので、
                    //   額縁 (Epilogue) が開く側に寄せておく。
                    gm.ChooseRiftFate(false);
                    return false;

                case GameManager.GamePhase.RunClear:
                    FinishClear();
                    return true;

                case GameManager.GamePhase.GameOver:
                    FinishGameOver();
                    return true;

                default:
                    return false;
            }
        }

        // ===== 行動方針: 前進貪欲 + 生存重視 =====

        private bool DoNavigate()
        {
            var gm = GameManager.Instance;
            var mm = MapManager.Instance;
            if (mm == null) { Finish(Outcome.Deadlock, "MapManager null"); return true; }

            // **Ultra の評価中は、 盤面にも RNG にも触れずに戻る。**
            //   ここより下の探索は GameRng を消費し得るので、 待っている間に通すと
            //   毎フレーム引き直すことになり再現性が壊れる。 生産方策の手は
            //   評価を始めた時点のものを取ってある。
            if (_ultraPending != null) return PollUltraNavigation(gm, mm);

            _eventStuckCount = 0; // マップに戻った＝イベント解決済み

            // Λ層（時間の狭間）: 固定Nマス周回してから中央(離脱)へ。
            if (gm.Run != null && gm.Run.inLambda)
                return DoNavigateLambda(gm, mm);

            var (fwd, lat) = mm.GetCategorizedMoves();
            bool hasFwd = fwd != null && fwd.Count > 0;
            var pool = new List<MapNode>(hasFwd ? fwd : (lat ?? new List<MapNode>()));

            // 利得最大化(ADR-0002・希望のリソース化): 前進のみでなく、横方向の「未訪問」マスも
            // 価値評価(Rank)の候補に含める。Rank が前進候補より価値が高いと判定した時だけ横移動し、
            // その対価として希望-LateralCost を支払う＝希望を消費して利得を取りにいく挙動。
            // pool は前進候補が先頭なので、Rank 同点なら前進が勝つ（横移動は厳密に価値が上の時のみ）。
            // 「どこまで希望を損耗して寄り道するか」は L2 学習軸 lateralHopeFloor が勝率(composite)で最適化する
            // （現在希望がこの下限を超えるときのみ寄り道。低いほど深く損耗、高いほど温存）。
            // 2026-09-12: **横移動が無税 (燈火 r10) なら希望の下限ゲートを掛けない。**
            //   ゲートは「希望を払ってまで寄り道するか」の判断なので、 払うものが無ければ
            //   判断自体が不要。 旧実装は無税でも希望 20 以下で寄り道を止めていた。
            if (hasFwd && lat != null && lat.Count > 0
                && (MetaProgression.MetaBuffApplicator.GetLateralHopeCost() <= 0
                    || gm.Run.hope > LateralHopeFloorEffective()))
            {
                foreach (var ln in lat)
                    if (!ln.visited && !pool.Contains(ln)) pool.Add(ln); // 訪問済みを追うと同行往復で無限ループ
            }

            if (pool == null || pool.Count == 0)
            {
                Finish(Outcome.Deadlock, "移動先なし(MapNavigation)");
                return true;
            }

            float hpRatio = gm.Run.playerMaxHP > 0
                ? (float)gm.Run.playerHP / gm.Run.playerMaxHP : 1f;

            // ボス接近判定: プールにBossがある or 残り行数が2以内
            bool bossNear = false;
            foreach (var n in pool) if (n.EffectiveType == TileType.Boss) { bossNear = true; break; }
            int rowCount = mm.CurrentMap?.rowCount ?? 10;
            if (!bossNear && mm.CurrentNode != null && mm.CurrentNode.row >= rowCount - 2)
                bossNear = true;
            _curBossNear = bossNear; // 休憩フェーズの選択判断で参照

            // 回復目標: **想定被弾 safetyHits 発分の HP を確保して次の戦闘へ入る** (危険度駆動)。
            //   旧実装は「ボス接近 0.8 / それ以外 0.5」の固定割合で、 高難易度でも HP 半分で
            //   戦闘に入り続けていた (30pt の敗因の 56% が削り負け・死亡戦の開始HP中央値 58%)。
            //   被弾量で駆動すればデバフ・層深度・エリートのどれが原因でも自動追従する。
            //   0.5 の床を残すので低難易度の挙動は従来どおり。
            int floorHit = EstimateFloorMaxHit(gm.Run);
            float dangerTarget = FightBudgetDanger(gm.Run, floorHit);
            // ボス接近時は 1 発ぶん上乗せ (旧 0.8 固定の役割を相対量で引き継ぐ)。
            if (!_curCombatAverse && bossNear && floorHit > 0 && gm.Run.playerMaxHP > 0)
            {
                float bossMargin = (float)floorHit / gm.Run.playerMaxHP;
                dangerTarget = Mathf.Clamp(dangerTarget + bossMargin, 0.5f, 0.95f);
            }
            float healTarget = dangerTarget;
            while (Consumables.TryUseBestHeal(gm.Run, healTarget)) { }

            // 希望の補充 (2026-08-05: cons_hope_* 新設で復活)。
            //   学習軸 hopeRefillFloor 以下に落ちたら手持ちの希望回復を使う。
            //   **溢れさせない** ── 不足分を超える Tier は温存し、 小さい方から充てる。
            //
            //   戻す先は hopeCap ではなく **hopeCap × hopeBandCeilRatio** (既定 1.0 = 従来どおり上限)。
            //   1 未満なら「補充はするが上へ戻し切らない」＝ 希望を帯の中に留める。
            //   〈渇き〉のような低希望発動の効果を維持したまま発狂を避けるために要る。
            if (gm.Run.hope <= HopeRefillFloorEffective())
            {
                int hopeCeil = Mathf.RoundToInt(
                    gm.Run.hopeCap * AutoTest.PolicyParameters.Current.hopeBandCeilRatio);
                int guardHope = 0;
                while (gm.Run.hope < hopeCeil && guardHope++ < 8
                       && UseFirst(gm.Run, "湯気の立つ椀", "古い手紙", "凱旋の記憶", "希望の欠片")) { }
            }

            // 休憩を強く優先すべき状況:
            //  - 貪欲がボス接近かつHPが8割未満（スケールしたbuildをボスへ生存させる）
            //  ※旧・空腹切れ条件は飢餓→希望統合で廃止（休憩は希望を回復しない＝希望は食料で対応）。
            bool preferRest = (!_curCombatAverse && bossNear && hpRatio < dangerTarget);

            // 戦闘忌避の境界 ＝ 危険度そのもの (2026-09-15)。
            //   旧実装は `max(hpLowThreshold=0.55, dangerTarget)` だったが、 **床が 2 つ競合して
            //   必ずどちらかが飽和していた** ── 旧式では dangerTarget が 0.95 に張り付いて
            //   hpLowThreshold が死に、 新式に替えたら今度は 0.55 が全部を潰した
            //   (SafetyFights を 1.0〜2.5 で振っても結果が動かなくなった)。
            //   新式は DangerFloor (0.25) と tailGuard (最悪の 1 発) で同じ役割を内蔵している。
            float lowBar = dangerTarget;

            // ── 2 手先読み ──
            //   1 手決めだと「回復してからエリート」のような組み立てが作れず、 目先の
            //   タイル種別だけで決めてしまう。 各候補について、 そこを踏んだ後の HP を
            //   予測し、 その状態で次に選べる最良手を足して評価する。
            //   **先の手は割り引く** (係数 0.6) ── 予測 HP は概算で、 分岐も再抽選されるため、
            //   同点なら手前の確実さを優先させる。
            const float LookaheadDiscount = 0.6f;
            MapNode best = pool[0];
            float bestScore = float.MaxValue;
            var fmap = mm.CurrentMap;
            var here = mm.CurrentNode;
            if (useFloorDpNavigation) FloorDpPrepare(fmap);
            _navScores.Clear();
            foreach (var n in pool)
            {
                float score = Rank(n, hpRatio, _curCombatAverse, preferRest, gm.Run, lowBar);
                score += LateralPenalty(here, n, gm.Run);

                float nextHpRatio = PredictHpRatioAfter(n, hpRatio, floorHit, gm.Run);
                var succ = fmap?.GetReachableFrom(n.id);
                if (succ != null && succ.Count > 0)
                {
                    float bestNext = float.MaxValue;
                    foreach (var s in succ)
                    {
                        // **既定は 1 手先までの評価** (`Rank` 1 回)。 `useFloorDpNavigation` を
                        //   立てると、 そこから階層の出口まで後ろ向きに解いた値に差し替わる。
                        //   既定 OFF なので、 立てない限り従来とビット単位で同一 ──
                        //   Super は全測定の基準線なので、 黙って変えると既存の基準値が
                        //   まとめて無効になる。
                        float r2 = useFloorDpNavigation
                            ? FloorDpValue(fmap, s, HpBandOf(nextHpRatio), floorHit, gm.Run, lowBar)
                            : Rank(s, nextHpRatio, _curCombatAverse, false, gm.Run, lowBar);
                        r2 += LateralPenalty(n, s, gm.Run);   // 先の手の横移動も勘定に入れる
                        if (r2 < bestNext) bestNext = r2;
                    }
                    if (bestNext != float.MaxValue) score += LookaheadDiscount * bestNext;
                }
                _navScores.Add(score);
                if (score < bestScore) { bestScore = score; best = n; }
            }

            // --- [計装] 航行スコアの同点率 ---
            //   `Rank()` は **int** を返し、 `LateralPenalty` は 1 判断の中では {0, L} の 2 値。
            //   合成は int + {0,L} + 0.6×(int + {0,L}) の粗い格子なので、 同点が多発するはず
            //   ── だが**戦闘で同じ推測を 6 倍外した**ので数える。 最良と厳密同値の本数を採る。
            if (pool.Count > 1)
            {
                int tied = 0;
                for (int i = 0; i < _navScores.Count; i++)
                    if (_navScores[i] == bestScore) tied++;
                NavDecisions++;
                NavCandSum += pool.Count;
                NavTiedSum += tied;
                if (tied > 1) NavTiedDecisions++;
            }

            // --- [アブレーション] 航行判断を潰す ---
            //   **判断の価値は「無くしたときの落ち幅」で測る。** 触る前に測る (2026-08-22)。
            //   専用乱数を使うので GameRng を 1 つも消費しない ── マップ・敵・アイテムの
            //   抽選が対照アームと完全に一致し、 同一シードのペア比較が成立する。
            if (ablateNavigation && pool.Count > 1)
                best = pool[AblationRng.Next(0, pool.Count)];

            TryCaptureUltraCheckpoint(gm, mm);

            // --- Ultra: 公開観測だけで決める controller に 1 手だけ委ねる (§10 Phase D) ---
            //   **sink が null なら 1 命令も実行されない。** ここより上の探索は一切変えていない
            //   ので、 controller 未接続時の挙動は従来とビット単位で同一 ── 既存の基準値
            //   (Super/Optimal の全測定) が生き続けることがこの分岐の前提条件。
            MapNode overridden = UltraOverrideMove(gm, mm, best);
            // 評価が始まった (＝この決定はまだ終わっていない)。 盤面を動かさずに戻り、
            // 次以降の Step で回収する。 移動はそのときに 1 回だけ実行される。
            if (_ultraPending != null) { _ultraWaiting = true; return false; }

            NoteEliteChoice(overridden ?? best, pool, hpRatio, preferRest, gm.Run, lowBar);
            gm.MoveToNode((overridden ?? best).id);
            return false;
        }

        /// <summary>[計装 2026-09-20] エリートマスを選んだ理由と、 その時点の被ダメ見積りを SkillDiag へ置く。
        /// 戦闘の終わりに実際の被ダメ・死亡と突き合わされる (SkillDiag.EliteReason)。</summary>
        private void NoteEliteChoice(MapNode chosen, List<MapNode> pool, float hpRatio, bool preferRest,
                                     GameLoop.RunState run, float lowBar)
        {
            if (chosen == null || chosen.EffectiveType != TileType.EliteBattle) return;
            bool allElite = true;
            foreach (var n in pool) if (n.EffectiveType != TileType.EliteBattle) { allElite = false; break; }
            float r = Rank(chosen, hpRatio, _curCombatAverse, preferRest, run, lowBar);
            int reason = !UseEliteCostNav ? 4 : allElite ? 3 : r <= -5f ? 2 : r >= 90f ? 1 : 0;
            float loss = EliteFightLoss(chosen, run);
            CombatSystem.SkillDiag.PendingEliteReason = reason;
            CombatSystem.SkillDiag.PendingEliteEst = (loss >= 0f && run != null && run.playerMaxHP > 0)
                ? loss / run.playerMaxHP : -1f;
        }

        /// <summary>進行中の決定を捨て、 走っている worker を殺す。
        ///
        /// <para>ラン終了やバッチ停止で呼ぶ。 呼び忘れると、 誰も答えを待っていない
        /// worker プロセスが取り残される。</para></summary>
        private void ClearUltraPending()
        {
            if (_ultraPending != null)
                AutoTest.Ultra.UltraRolloutSink.CancelDecision(_ultraPending);
            _ultraPending = null;
            _ultraPendingCheckpoint = null;
            _ultraPendingProductionChoice = null;
            _ultraWaiting = false;
        }

        /// <summary>進行中の Ultra 決定を 1 掃引ぶん進める。 まだなら盤面に触れずに戻る。</summary>
        private bool PollUltraNavigation(GameManager gm, MapManager mm)
        {
            var sink = ultraSink as AutoTest.Ultra.UltraRolloutSink;
            if (sink == null)
            {
                // ありえないが、 保留を抱えたまま sink が入れ替わったら手を離す。
                ClearUltraPending();
                return false;
            }

            string actionId = null;
            string reason = null;
            bool faulted = false;
            try
            {
                if (!sink.PollDecide(_ultraPending, out actionId, out reason))
                {
                    _ultraWaiting = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                // controller の故障はゲーム上の敗北ではない。 数えて生産方策へ落とす。
                AutoTest.Ultra.UltraDispatch.NoteFault(ex, ref reason);
                faulted = true;
            }

            AutoTest.Ultra.UltraCheckpoint checkpoint = _ultraPendingCheckpoint;
            MapNode production = _ultraPendingProductionChoice;
            _ultraPending = null;
            _ultraPendingCheckpoint = null;
            _ultraPendingProductionChoice = null;
            _ultraWaiting = false;

            float spent = Time.realtimeSinceStartup - _ultraWaitBeganRt;
            _ultraThinkingSeconds += spent;
            _ultraThinkingSecondsTotal += spent;

            // **同期経路と同じ受け入れ規律を通す。** フレームを跨いだからといって
            //   commit 検証を素通しにはしない。
            if (!faulted)
                actionId = AutoTest.Ultra.UltraDispatch.CompleteResolve(
                    checkpoint, _ultraEpoch, AutoTest.Ultra.UltraDecisionPoint.MapNavigation,
                    actionId, ref reason);
            else
                actionId = null;
            _ultraEpoch++;   // **必ず進める。** 採否に関わらず 1 決定機会を消費した
            if (actionId == null && !string.IsNullOrEmpty(reason)) _ultraLastFallbackReason = reason;

            MapNode target = ResolveUltraMove(mm, actionId, reason);
            if (target != null)
                AutoTest.Ultra.UltraDispatch.NoteOutcome(
                    production == null
                    || !string.Equals(target.id, production.id, StringComparison.Ordinal));

            MapNode chosen = target ?? production;
            if (chosen == null)
            {
                // 生産方策の手すら残っていない ── 次の Step で普通に選び直させる。
                // **黙って進めない。**
                Debug.LogError("[AutoRunner][Ultra] 保留していた移動先が消えた。 選び直す");
                return false;
            }
            gm.MoveToNode(chosen.id);
            return false;
        }

        /// <summary>実ランの途中で checkpoint を 1 つだけ書き出す。 **path が空なら即 return**。
        ///
        /// <para>採るのは航行フェーズの判断直前 ── 合法手が確定していて、
        /// かつ盤面がまだ動いていない一点。 veil 種は 0 固定で書き出すが、
        /// 利用側は種を変えて読み直す（<see cref="AutoTest.Ultra.UltraResumePayload.Create"/> が
        /// 毎回 veil を掛け直す）。</para></summary>
        private void TryCaptureUltraCheckpoint(GameManager gm, MapManager mm)
        {
            if (_ultraCaptured || string.IsNullOrEmpty(ultraCaptureCheckpointPath)) return;
            if (gm?.Run == null || mm?.CurrentMap == null) return;
            if (++_ultraNavSeen < Math.Max(1, ultraCaptureAtNavigation)) return;

            try
            {
                var payload = AutoTest.Ultra.UltraResumePayload.Create(
                    gm.Run, mm, _ultraEpoch,
                    AutoTest.Ultra.UltraDecisionPoint.MapNavigation, 0,
                    out AutoTest.Ultra.UltraVeilReport veil);

                string full = System.IO.Path.GetFullPath(ultraCaptureCheckpointPath);
                string dir = System.IO.Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(full, JsonUtility.ToJson(payload, false),
                    new UTF8Encoding(false));

                _ultraCaptured = true;
                Debug.Log($"[AutoRunner][Ultra] checkpoint を書き出した: {full}"
                        + $"  航行{_ultraNavSeen}回目 / {gm.Run.currentFloor}層"
                        + $" / HP {gm.Run.playerHP}/{gm.Run.playerMaxHP}"
                        + $" / 所持金 {gm.Run.coins} / {veil.Describe()}");
            }
            catch (Exception ex)
            {
                _ultraCaptured = true;   // 失敗を毎ターン繰り返さない
                Debug.LogError("[AutoRunner][Ultra] checkpoint 書き出し失敗: " + ex.Message);
            }
        }

        /// <summary>Ultra へ問い合わせ、 掛かった実時間を控除枠へ積む。
        ///
        /// <para>rollout は worker プロセスの完了を同期で待つため、 1 決定で数秒ブロックする。
        /// その時間を番犬に数えさせると、 ラン は「固着」と判定されて全部 DEADLOCK になる。
        /// 番犬そのものは無効化しない ── 無効化すると、 本物の固着が出たときに
        /// バッチが永久に止まる。</para></summary>
        private string ResolveUltraTimed(
            AutoTest.Ultra.UltraCheckpoint checkpoint,
            AutoTest.Ultra.UltraDecisionPoint point,
            out string reason)
        {
            float began = Time.realtimeSinceStartup;
            try
            {
                return AutoTest.Ultra.UltraDispatch.TryResolve(
                    ultraSink, checkpoint, _ultraEpoch, point, out reason);
            }
            finally
            {
                float spent = Time.realtimeSinceStartup - began;
                _ultraThinkingSeconds += spent;
                _ultraThinkingSecondsTotal += spent;
            }
        }

        /// <summary>離散選択を Ultra controller に委ねる。 採用できなければ
        /// <paramref name="productionChoice"/> をそのまま返す。
        ///
        /// <para>移動と同じ規律: 提示した合法手だけを受け付け、 epoch と決定点を検証し、
        /// 例外・辞退・不正はすべて生産方策へ落ちる。 呼び出し側は戻り値の添字を
        /// **そのまま**使えばよく、 Ultra が居るかどうかを気にしなくてよい。</para></summary>
        private int UltraOverrideChoice(
            GameManager gm,
            AutoTest.Ultra.UltraDecisionPoint point,
            AutoTest.Ultra.UltraActionKind kind,
            AutoTest.Ultra.UltraChoiceView choice,
            int productionChoice)
        {
            if (gm?.Run == null || choice == null) return productionChoice;

            // 合法手の数え方は ChoiceActions と揃える ── 買えない選択肢は「見えているが
            // 選べない」ので手ではない。 ここで数がずれると、 国勢調査だけが別のゲームを
            // 数えることになる。
            int legalCount = 0;
            if (choice.options != null)
                for (int i = 0; i < choice.options.Length; i++)
                    if (choice.options[i] != null && choice.options[i].available) legalCount++;
            AutoTest.Ultra.UltraDecisionCensus.Record(point, gm.Run.currentFloor, legalCount);

            if (ultraSink == null) return productionChoice;

            var observation = AutoTest.Ultra.UltraObservationBuilder.Capture(
                gm.Run, MapManager.Instance, point.ToString(), _ultraEpoch);
            observation.choice = choice;

            var checkpoint = AutoTest.Ultra.UltraCheckpointProtocol.Create(
                _ultraEpoch, point, observation,
                AutoTest.Ultra.UltraCheckpointProtocol.ChoiceActions(observation, kind));

            string actionId = ResolveUltraTimed(checkpoint, point, out string reason);
            _ultraEpoch++;

            if (actionId == null)
            {
                if (!string.IsNullOrEmpty(reason)) _ultraLastFallbackReason = reason;
                return productionChoice;
            }

            int picked = AutoTest.Ultra.UltraCheckpointProtocol.ResolveChoiceIndex(
                observation, kind, actionId);
            if (picked < 0)
            {
                AutoTest.Ultra.UltraDispatchStats.Rejected++;
                _ultraLastFallbackReason = "choice action did not resolve to an option";
                return productionChoice;
            }

            AutoTest.Ultra.UltraDispatch.NoteOutcome(picked != productionChoice);
            return picked;
        }

        /// <summary>Ultra controller に移動先を選ばせる。 採用できなければ null を返す。
        ///
        /// <para><b>合法手は observation から作る。</b> 直上の <c>Rank</c> ループは
        /// <c>EffectiveType</c> を pool と**その後続**の両方について読むので、
        /// 未公開マスがあれば、 そこから手集合を作ると「プレイヤーが持たない情報で絞られた
        /// 候補」を Ultra に渡すことになる。
        /// <see cref="UltraCheckpointProtocol.MapNavigationActions"/> は observation の
        /// <c>reachableNow</c> だけを見るので、 その経路が構造的に存在しない。</para>
        ///
        /// <para><b>ただし現時点で未公開マスは存在しない (2026-08-21 検証)。</b>
        /// 視界制限を持っていたのは 〈戦場の霧〉/〈暗夜〉 の 2 軸だけで、 どちらも廃止され
        /// <c>MetaDebuffApplicator.GetMapSightLimit()</c> は <c>-1</c> (制限なし) を返すだけ。
        /// よって <c>MapNode.revealed</c> は常に true、 observation も
        /// <c>Classify(EffectiveType)</c> をそのまま渡す ── **Super と Ultra は同じ盤面を見る。**
        /// この分岐は今の非対称を消すためではなく、 視界制限が復活したときに自動的に
        /// 正しくあり続けるための構造。
        /// かつてここには「Super は未公開マスの正体まで見ている」と書いてあり、 軸の廃止後も
        /// 残っていたため、 2026-08-21 の Phase H 測定で敗因の解釈を誤らせた。
        /// **正本は実コード。 コメントを根拠に測定結果を説明しないこと。**</para>
        ///
        /// <para><b>候補は生産方策の pool ではなく合法手全体。</b> pool は希望残量で横移動を
        /// 足すかどうかを決める発見的な絞り込みで、 それを継承すると Ultra の選択肢が
        /// Super の選択肢に頭打ちされる。 横移動の希望コストは
        /// <c>GameManager.MoveToNode</c> が row の比較から徴収するので、
        /// **誰が選んでも同じように支払われる** ── 方策側で払い忘れる経路は無い。</para></summary>
        private MapNode UltraOverrideMove(GameManager gm, MapManager mm, MapNode productionChoice)
        {
            if (gm?.Run == null || mm == null) return null;

            // 国勢調査は sink が居なくても数える。 「1ラン あたりマクロ決定 30 回」は
            // 実測ではなくコスト計測メニューに直書きされた定数で、 rollout をどこで撃つかの
            // 判断はこの数に乗っている。 **絞る前に数える。**
            if (ultraSink == null)
            {
                if (AutoTest.Ultra.UltraDecisionCensus.Enabled)
                {
                    var censusObs = AutoTest.Ultra.UltraObservationBuilder.Capture(
                        gm.Run, mm, "MapNavigation", _ultraEpoch);
                    AutoTest.Ultra.UltraDecisionCensus.Record(
                        AutoTest.Ultra.UltraDecisionPoint.MapNavigation, gm.Run.currentFloor,
                        AutoTest.Ultra.UltraCheckpointProtocol.MapNavigationActions(censusObs).Count);
                }
                return null;
            }

            var observation = AutoTest.Ultra.UltraObservationBuilder.Capture(
                gm.Run, mm, "MapNavigation", _ultraEpoch);
            AutoTest.Ultra.UltraDecisionCensus.Record(
                AutoTest.Ultra.UltraDecisionPoint.MapNavigation, gm.Run.currentFloor,
                AutoTest.Ultra.UltraCheckpointProtocol.MapNavigationActions(observation).Count);
            var checkpoint = AutoTest.Ultra.UltraCheckpointProtocol.Create(
                _ultraEpoch,
                AutoTest.Ultra.UltraDecisionPoint.MapNavigation,
                observation,
                AutoTest.Ultra.UltraCheckpointProtocol.MapNavigationActions(observation));

            // rollout sink は worker を待つ。 **同期で待つとメインスレッドが数秒停車する**ので、
            // 開始だけして戻り、 回収は後続の Step に任せる。 それ以外の sink (疎通用の
            // 先頭手/末尾手など) は即答するので従来どおり同期で通す。
            if (ultraSink is AutoTest.Ultra.UltraRolloutSink rolloutSink)
            {
                AutoTest.Ultra.UltraDispatch.NoteOffered();
                _ultraPending = rolloutSink.BeginDecide(checkpoint);
                _ultraPendingCheckpoint = checkpoint;
                _ultraPendingProductionChoice = productionChoice;
                _ultraWaitBeganRt = Time.realtimeSinceStartup;
                // **epoch は回収時に進める。** ここで進めてしまうと、 待っている間に
                //   checkpoint の epoch と runner の epoch がずれ、 回収時の commit 検証
                //   (TryValidateCommit) が必ず落ちる ── 検証を素通しにするか、 全ての
                //   決定を却下するかの二択になる。 どちらも避ける。
                return null;
            }

            string actionId = ResolveUltraTimed(
                checkpoint, AutoTest.Ultra.UltraDecisionPoint.MapNavigation, out string reason);
            _ultraEpoch++;   // **必ず進める。** 採否に関わらず 1 決定機会を消費した

            if (actionId == null)
            {
                if (!string.IsNullOrEmpty(reason)) _ultraLastFallbackReason = reason;
                return null;
            }

            MapNode target = ResolveUltraMove(mm, actionId, reason);
            if (target == null) return null;

            AutoTest.Ultra.UltraDispatch.NoteOutcome(
                productionChoice == null || !string.Equals(target.id, productionChoice.id,
                    StringComparison.Ordinal));
            return target;
        }

        /// <summary>受け取った action id を、 いまの盤面で実際に踏める接続へ解決する。
        ///
        /// <para>checkpoint が通っても、 その id が現在の接続かどうかは別問題 ──
        /// 解決できなければ生産方策へ落とす。</para></summary>
        private MapNode ResolveUltraMove(MapManager mm, string actionId, string reason)
        {
            if (actionId == null)
            {
                if (!string.IsNullOrEmpty(reason)) _ultraLastFallbackReason = reason;
                return null;
            }
            var moves = mm != null ? mm.GetAvailableMoves() : null;
            if (moves != null)
                for (int i = 0; i < moves.Count; i++)
                {
                    if (moves[i] == null) continue;
                    if (!string.Equals(
                            AutoTest.Ultra.UltraLegalAction.Of(
                                AutoTest.Ultra.UltraActionKind.MoveTo, moves[i].id).ActionId,
                            actionId, StringComparison.Ordinal)) continue;
                    return moves[i];
                }
            AutoTest.Ultra.UltraDispatchStats.Rejected++;
            _ultraLastFallbackReason = "action did not resolve to a legal move";
            return null;
        }

        /// <summary>Λ層の走破方針。スポーク(lambda_s)で撤退条件を満たしたら中央(離脱)へ。
        /// 通常バッチ: Λデバフの lv2 以上が4つ到達、または迫りくる死が lv3(>2) に達したら撤退。
        /// スイープ時(lambdaFarmSweep): 従来どおり固定 lambdaFarmTiles マスで離脱。
        /// 安全弁: 周回ステップが上限超過 or 踏破マス過多なら強制撤退（加算不具合でも無限周回しない）。</summary>
        private bool DoNavigateLambda(GameManager gm, MapManager mm)
        {
            var node = mm.CurrentNode;
            if (node == null) { Finish(Outcome.Deadlock, "Λ: CurrentNode null"); return true; }

            _lambdaNavSteps++;
            var run = gm.Run;

            // Λ突入の基準スナップショット（このランの最初のΛナビ）
            if (_lambdaNavSteps == 1 && _cur != null)
            {
                _cur.enteredLambda = true;
                _lambdaEntryCoins = run.coins;
                _lambdaEntryPassives = run.ownedPassiveItems?.Count ?? 0;
                GameLoop.RunChronicle.Lambda(run, GameLoop.RunChronicle.LambdaEnter, 0, run.coins);
            }

            int farmed = run.dimensionalDisturbance;
            bool atSpoke = node.id == "lambda_s";

            bool wantExit;
            // **lambdaFarmTiles <= 0 は撤退条件の指定** (2026-08-08)。
            //   絶対踏破マス数で区分すると、 デバフ付与間隔を変えたときに意味が変わってしまう
            //   (間隔 3→5 で適応が 13.9→22.7 マスへ暴走した)。 リスクの度合いは
            //   **背負ったデバフの重さ**で表すのが正しいので、 3 区分とも同じ形の条件にする。
            //     -1 現在  : lv2 が 4 つ / 迫りくる死 lv2 で撤退
            //     -2 安全策: lv2 が 2 つ / 迫りくる死 lv2 で撤退
            //     -3 リスキー: lv2 が 7 つ / **迫りくる死 lv3 まで許容** (= 戦闘開始 HP1 を飲む)
            //   正の値は固定マス数 (旧挙動・全域走査用に残置)。
            // 正の値は「固定マス数で撤退」。 **専用スイープでなくても効かせる** (2026-09-20) ──
            //   並列ワーカーの A/B では lambdaFarmSweep を立てると別のバッチ経路へ入ってしまい、
            //   固定マス数のアームが全部同じ結果になっていた。
            if (lambdaLv3Risk >= 0f)
            {
                // **崖リスクのしきい値** (2026-09-20)。 BOT に渡すのはこの 1 つの数だけ ──
                //   「lv2 が何本あるか」ではなく「次の一歩で lv3 を引く確率」。
                //   どの水準で降りるべきかは A/B で決める (lambdaLv3Risk のスイープ)。
                wantExit = GameLoop.Lambda.LambdaDebuffEffects.NextLv3Chance(run) >= lambdaLv3Risk;
            }
            else if (lambdaFarmTiles > 0)
            {
                wantExit = farmed >= Mathf.Max(3, lambdaFarmTiles);
            }
            else
            {
                int lv2plus = 0, lv3plus = 0;
                if (run.lambdaDebuffs != null)
                    foreach (var kv in run.lambdaDebuffs)
                    {
                        if (kv.Value >= 2) lv2plus++;
                        if (kv.Value >= 3) lv3plus++;
                    }

                int impendingCap = 2;
                if (lambdaFarmTiles == -5)
                {
                    // 崖回避 (2026-09-20): **lv3 を 1 つでも引いたら即撤退**。
                    //   Λ デバフは lv2 までが当たり枠で lv3 が崖 (会心枝の消滅・与ダメ −40% 等) なので、
                    //   「崖を踏むまで潜る」が判断として正しいかを測るための区分。
                    wantExit = lv3plus >= 1
                        || run.GetLambdaDebuffLevel(GameLoop.Lambda.LambdaDebuffIds.ImpendingDeath) >= 3;
                }
                else if (lambdaFarmTiles == -4)
                {
                    // 深掘り (2026-09-20): lv2 の本数では降りず、 **lv3 が 6 つ**か〈迫りくる死〉lv3 で撤退。
                    //   -3 (リスキー) より深く潜る区分。 Λ は 1 戦の被ダメが 6〜7% と軽く、
                    //   危険は周回数とデバフの蓄積で出るので、 どこで頭打ちになるかを見るために置く。
                    wantExit = lv3plus >= 6
                        || run.GetLambdaDebuffLevel(GameLoop.Lambda.LambdaDebuffIds.ImpendingDeath) >= 3;
                }
                else if (lambdaFarmTiles == -3)
                {
                    // リスキー: **段階の深さ**で見る。 lv2 を数だけ増やしても踏破 37 マスまで
                    //   潜って逓減の底に沈んだ (7層クリア 20.7%) ので、 lv3 到達を条件に据える。
                    //   迫りくる死は lv3 (戦闘開始HP1) まで許容 ── lv2 で止めると
                    //   この条件が成立する前に必ず先に降りてしまい、 区分として機能しない。
                    impendingCap = 3;
                    bool risky = (lv2plus >= 6 && lv3plus >= 2) || lv3plus >= 4;
                    wantExit = risky
                        || run.GetLambdaDebuffLevel(GameLoop.Lambda.LambdaDebuffIds.ImpendingDeath) >= impendingCap;
                }
                else
                {
                    int needLv2 = lambdaFarmTiles == -2 ? 2 : 4;   // -2 安全策 / 既定 現在

                    // **挑戦スコアに応じて早く降りる** (2026-08-09)。
                    //   旧実装は難易度を見ずに常に同じ深さまで潜っていた。 Λ デバフは
                    //   挑戦デバフと**乗算で重なる**ので、 高難易度では同じ深さが自殺行為になる。
                    //   実測: 挑戦 30pt で 68.3% のランが 5F (＝Λ) で終わっていた。
                    //   「いつ降りるか」は本来プレイヤーの判断＝技量帯の一部なので、
                    //   BOT が難易度を読めないままだと**梯子の長さを BOT の欠陥が決めてしまう**。
                    int score = MetaProgression.MetaDebuffApplicator.Score;
                    if (score >= 25)      { needLv2 = 1; impendingCap = 1; }
                    else if (score >= 18) { needLv2 = Mathf.Min(needLv2, 2); impendingCap = 1; }
                    else if (score >= 8)  { needLv2 = Mathf.Min(needLv2, 3); }

                    wantExit = lv2plus >= needLv2
                        || run.GetLambdaDebuffLevel(GameLoop.Lambda.LambdaDebuffIds.ImpendingDeath) >= impendingCap;
                }
            }

            // 安全弁（加算不具合・条件恒久未達でも周回を止める）
            if (_lambdaNavSteps > 240 || farmed >= 120) wantExit = true;

            if (atSpoke && wantExit)
            {
                RecordLambdaGains(run);
                GameLoop.RunChronicle.Lambda(run, GameLoop.RunChronicle.LambdaLeave,
                                             farmed, _cur?.lambdaGoldGained ?? 0);
                Debug.Log($"[Λ] 撤退: 踏破{farmed} 獲得G+{_cur?.lambdaGoldGained} 獲得アイテム+{_cur?.lambdaItemsGained} lv2+={CountLambdaLv2Plus(run)} 迫死lv{run.GetLambdaDebuffLevel(GameLoop.Lambda.LambdaDebuffIds.ImpendingDeath)} steps={_lambdaNavSteps}");
                gm.MoveToNode("lambda_center");
                return false;
            }

            // 環状線の次マス(LambdaRing)へ前進。
            foreach (var conn in node.connections)
            {
                var t = mm.CurrentMap?.GetNode(conn);
                if (t != null && t.type == TileType.LambdaRing)
                {
                    gm.MoveToNode(conn);
                    return false;
                }
            }

            // 異常系（中央しか無い等）。安全に離脱。
            if (node.connections.Contains("lambda_center"))
            {
                RecordLambdaGains(run);
                gm.MoveToNode("lambda_center");
                return false;
            }
            Finish(Outcome.Deadlock, $"Λ: 前進先なし node={node.id}");
            return true;
        }

        private static int CountLambdaLv2Plus(GameLoop.RunState run)
        {
            int n = 0;
            if (run?.lambdaDebuffs != null)
                foreach (var kv in run.lambdaDebuffs) if (kv.Value >= 2) n++;
            return n;
        }

        /// <summary>Λ滞在中のゴールド/アイテム獲得量を突入時スナップショットとの差分で確定し _cur に記録。</summary>
        private void RecordLambdaGains(GameLoop.RunState run)
        {
            if (_cur == null || !_cur.enteredLambda || run == null) return;
            _cur.lambdaGoldGained = run.coins - _lambdaEntryCoins;
            _cur.lambdaItemsGained = (run.ownedPassiveItems?.Count ?? 0) - _lambdaEntryPassives;
            _cur.lambdaItemsAcquiredGross = run.lambdaItemsAcquiredGross;
            _cur.lambdaPoolRemainingAll = run.lambdaPoolRemainingAll;
            _cur.lambdaPoolRemainingFloored = run.lambdaPoolRemainingFloored;
            _cur.lambdaRingsEntered = run.lambdaRingsEntered;
            _cur.lambdaRingElite = run.lambdaRingElite;
            _cur.lambdaRingEvent = run.lambdaRingEvent;
            _cur.lambdaRingEventHeal = run.lambdaRingEventHeal;
            _cur.lambdaRingEventItem = run.lambdaRingEventItem;
        }

        /// <summary>この敵から 1 ターンに飛んでくる最大被弾の見積り。
        ///
        /// **挑戦デバフとエスカレーションを必ず掛ける。** 素の <see cref="EnemyData.EffectiveBaseAttack"/>
        /// だけを見ると、 狂暴化 (敵ダメ +10〜20%) も長期戦の倍率 (最大 ×2.30) も推定に入らず、
        /// 「高難易度ほど危険」を BOT が検知できない (2026-08-05)。 危険度駆動の方針は
        /// この見積りを唯一の入力にするので、 ここが鈍いと方針全体が鈍る。
        ///
        /// **敵の会心は 2026-08-05 に全廃した** (雑魚15体・ボス5体・偽の商人)。 被弾が
        /// 「基礎 + ロール × エスカレーション」だけになり、 プレイヤーが読める量になる。
        /// そのためこの推定に会心項は無い。 <c>criticalNumerator</c> はプレイヤー側と
        /// 旧データ互換のために残っているだけで、 敵側は全て 0。</summary>
        private static int EstimateMaxHit(CombatSystem.EnemyData e, int turn)
            => EstimateMaxHit(e, turn, null);

        /// <summary>敵の 1 発分の最大被弾見積り。 緊急回復のトリガに使う。
        ///
        /// <para><b>ctx を渡すこと。</b> 素ステータス (EnemyData) だけだと、
        /// <c>enemyDiceTotalBonus</c> / <c>bossDiceBonus</c> に積まれた**実行時の加算**が丸ごと抜ける。
        /// 実測 (2026-08-16・7層p4): 見積り 35 に対し実際の攻撃値は 57.5 で 39% の過小評価だった
        /// ── 停滞スタック 4.9 + 遺物の出目加算 7.6 + 溜め打ち 5 が計上されていなかったため。</para>
        ///
        /// <para>結果として <c>HP ≤ 見積り</c> で回復する方策に**死角**ができていた:
        /// 見積り 35 / 実際の貫通 44 なら、 HP 36〜44 の帯は「まだ安全」と判断されたまま
        /// 次の一撃で落ちる。 実測で p4 敗死 277 件のうち <b>52% が回復アイテムを持ったまま</b>
        /// 死んでおり (平均 1.08 個)、 その大半がこの死角によるもの。</para></summary>
        private static int EstimateMaxHit(CombatSystem.EnemyData e, int turn,
                                          InventorySystem.PassiveSkills.CombatContext ctx)
        {
            if (e == null) return 0;
            float hit = e.EffectiveBaseAttack + e.EffectiveRollCount * e.EffectiveRollMax;
            if (ctx != null) hit += Mathf.Max(0, ctx.enemyDiceTotalBonus) + Mathf.Max(0, ctx.bossDiceBonus);
            hit *= MetaProgression.MetaDebuffApplicator.GetEnemyDamageMultiplier();
            hit *= CombatSystem.Escalation.GetMultiplier(null, Mathf.Max(1, turn));
            return Mathf.RoundToInt(hit);
        }

        /// <summary>次に踏む戦闘で想定される 1 発分の被弾。 マップ上ではまだ敵が確定していないので、
        /// **現在フロアの敵から最大値**を取る。 エスカレーションは序盤ターン (=1.00) で評価する。
        /// 進路選択・回復目標・イベント選択の 3 箇所が共通の危険度尺度として使うので public。</summary>
        public static int EstimateFloorMaxHit(GameLoop.RunState run)
        {
            if (run == null) return 0;
            int worst = 0;
            foreach (var e in CombatSystem.EnemyDatabase.GetByFloor(run.currentFloor))
            {
                if (e == null || e.elite || GameLoop.BossIds.IsBoss(e.id)) continue;   // 通常進行の見積りなのでボスとエリート敵は除く
                int h = EstimateMaxHit(e, 1);
                if (h > worst) worst = h;
            }
            // **ブロックを差し引く** (2026-09-15)。 上の見積りは「敵の最大上振れ × 防御ゼロ」で、
            //   ADR-0009 の被ダメは `lossBase = max(0, 敵攻撃値 − blockSum)` なのに blockSum が
            //   入っていなかった。 実測 (10,000 ラン): 平均ブロック 10.37 / 平均 lossBase 15.67 /
            //   完全防御 17.8%。 つまり `safetyHits = 3.0` は実質「5〜6 発ぶん」を確保していた。
            //
            //   **控えめに引く** (BlockCreditRatio < 1)。 過小評価は過大評価より危険で、
            //   2026-08-16 に 7層p4 で見積り 35 / 実際 57.5 の死角を作り、 p4 敗死 277 件の
            //   52% が回復アイテムを持ったまま死んでいた。 上振れたターンはブロックが足りない。
            //
            //   **ラン内の実績平均だけを使う** (run.AverageBlockSeen)。 GuardDiag はバッチ累計なので
            //   使うと「ラン i は runIdx だけの関数」が壊れる。 1 戦もしていなければ 0 ＝ 従来どおり。
            if (BlockCreditRatio > 0f && worst > 0)
                worst = Mathf.Max(1, Mathf.RoundToInt(worst - run.AverageBlockSeen * BlockCreditRatio));
            return worst;
        }


        // ================================================================
        //  危険度 (dangerTarget) の組み直し — 2026-09-15
        //
        //  **旧式は 4 箇所で過大評価していた。**
        //      dangerTarget = clamp(floorHit × safetyHits / maxHP, 0.5, 0.95)
        //      floorHit = max over 出現敵( baseAttack + 1 × attackRollMax )
        //    ① attackRollMax ＝ ロールの**上限**。 期待値ではない
        //    ② ブロックを引いていない (ADR-0009: lossBase = max(0, 敵攻撃値 − blockSum))。
        //       実測 平均ブロック 10.37 / 平均 lossBase 15.67 / 完全防御 17.8%
        //    ③ 「その層までに出る敵の最悪値」1 体ぶん。 実際の 1 戦は約 7.4 回の被弾
        //    ④ **結果として 4〜7 層は常にクランプ天井 0.95 に張り付く**
        //       (floorHit 41 × 3.0 / maxHP 86 = 1.43)。 1〜2 層は床 0.5。
        //       実際に効いていたのは 3 層だけで、 ゲートは事実上
        //       「序盤 50% / 中盤以降 95%」の二値だった。
        //
        //  **置き換えの考え方。** 「最悪の 1 発を N 回」ではなく
        //  **「次の 1 戦で失う体力」を実測から取る**。 ラン内の実績なので
        //  ブロック・軽減パッシブ・層の深さ・精鋭化・デバフが全部すでに入っている ──
        //  式を足し込む必要がない (RunState.AverageFightDamage)。
        //
        //  ただし平均だけでは**上振れた 1 発で落ちる**のを防げない。
        //  2026-08-16 に 7層p4 で見積り 35 / 実際 57.5 の死角を作り、
        //  p4 敗死 277 件の 52% が回復アイテムを持ったまま死んでいた前科がある。
        //  そこで **max(平均 1 戦ぶん × 係数, 最悪の 1 発)** を取る。
        // ================================================================

        /// <summary>「次の何戦ぶんの体力を確保して動くか」。 旧 safetyHits の後継。
        /// <b>2.0 は実測で採った値</b> ── 1.0/1.5/2.0/2.5 の差は 0.5pt 未満で、
        /// この軸はもう律速ではない (効いたのは値ではなく式を直したこと自体)。</summary>
        public static float SafetyFights = 2.0f;
        /// <summary>クランプ。 旧 [0.50, 0.95] は両端で飽和して判別力が無かった。</summary>
        public static float DangerFloor = 0.25f;
        public static float DangerCeil  = 0.90f;


        /// <summary>開示済みマスの敵から、そのマス専用の忌避しきい値を出す。
        /// <see cref="FightBudgetDanger"/> と同じ形で、 tailGuard だけ「層の最悪値」から
        /// 「このマスの敵」へ差し替える。 引けなければ 0 (＝呼び出し側が従来の lowBar を使う)。</summary>
        private static float TileDanger(MapNode node, bool elite, GameLoop.RunState run)
        {
            if (run == null || run.playerMaxHP <= 0) return 0f;
            var ea = CombatSystem.EnemyDatabase.Get(node.encounterEnemyA);
            if (ea == null) return 0f;

            // エリートマスの敵はエリート敵そのもの (2026-09-20) なので倍率は要らない。
            float tail = EstimateMaxHit(ea, 1);
            var eb = CombatSystem.EnemyDatabase.Get(node.encounterEnemyB);
            if (eb != null) tail += EstimateMaxHit(eb, 1);   // 2 体戦は攻撃が 2 パケット来る

            tail = Mathf.Max(1f, tail - run.AverageBlockSeen * BlockCreditRatio);
            float budget = run.AverageFightDamage * SafetyFights;
            return Mathf.Clamp(Mathf.Max(budget, tail) / run.playerMaxHP, DangerFloor, DangerCeil);
        }

        // [計装 2026-09-16] max(budget, tailGuard) の**どちらが勝っているか**を数える。
        //   片方が常に勝っていれば、 もう片方 (と、 それを動かすノブ) は死んでいる。
        //   今日 3 回踏んだ形なので、 値を振る前にここを読む。
        public static long DangerSamples, DangerBudgetWins, DangerClampLo, DangerClampHi;
        public static double DangerBudgetSum, DangerTailSum, DangerOutSum;
        public static void ResetDangerStats()
        { DangerSamples = DangerBudgetWins = DangerClampLo = DangerClampHi = 0;
          DangerBudgetSum = DangerTailSum = DangerOutSum = 0; }
        private static void NoteDanger(float budget, float tail, float maxHp)
        {
            DangerSamples++;
            if (budget >= tail) DangerBudgetWins++;
            DangerBudgetSum += budget; DangerTailSum += tail;
            float raw = Mathf.Max(budget, tail) / maxHp;
            if (raw <= DangerFloor) DangerClampLo++;
            else if (raw >= DangerCeil) DangerClampHi++;
            DangerOutSum += Mathf.Clamp(raw, DangerFloor, DangerCeil);
        }

        /// <summary>開示済みマスで忌避しきい値を引き直すか。 <b>既定 OFF</b> ── A/B で測ってから畳む。</summary>
        public static bool UseTileDanger = false;

        /// <summary>1 戦ぶんの体力予算から危険度を出す。
        /// <paramref name="floorHit"/> は <see cref="EstimateFloorMaxHit"/> (ブロック控除済み) の値。</summary>
        private static float FightBudgetDanger(GameLoop.RunState run, int floorHit)
        {
            if (run == null || run.playerMaxHP <= 0) return DangerFloor;
            float maxHp = run.playerMaxHP;

            // ① 平均 1 戦ぶん × 係数。 **実績が無い間 (1 戦目) は 0** なので ② が効く。
            float budget = run.AverageFightDamage * SafetyFights;

            // ② 最悪の 1 発。 上振れで即死しないための床。
            float tailGuard = floorHit;

            NoteDanger(budget, tailGuard, maxHp);
            return Mathf.Clamp(Mathf.Max(budget, tailGuard) / maxHp, DangerFloor, DangerCeil);
        }

        /// <summary>被弾見積りから差し引くブロックの割合 (0 = 引かない ＝ 2026-09-15 以前の挙動)。
        /// **既定は 0。** 機構だけ入れて値は較正で決める ── 測らずに既定を動かすと、
        /// 進路選択・回復目標・イベント選択の 3 箇所が一度に変わる。
        /// 較正ノブは <c>AutoRunner.navBlockCredit</c>。</summary>
        public static float BlockCreditRatio = 0.7f;

        /// <summary>特売の購入意欲補正のしきい値 (%)。 <b>50/30 へ下げると悪化する</b>
        /// (2026-09-17 実測・全条件で −0.5〜−1.4pt)。 段 {15,30,50} のうち 50% だけを
        /// 拾う線引きが正しい ── 下げると「安いだけの品」が足切りを通る。</summary>
        public static int SaleBonusHiPct = 60, SaleBonusLoPct = 40;

        // ================================================================
        //  階層まるごとの後ろ向き解 (④)
        //
        //  既存の航行評価は **1 手先まで**しか見ず、 しかもその先を 0.6 の手調整係数で
        //  割り引いていた。 だが 1 階層のマップは高々 10 行程度の小さな DAG で、
        //  ボス行から後ろ向きに解けば出口までの最良経路が**厳密に**求まる。
        //  rollout は 1 本も要らず、 コストはマイクロ秒。
        //
        //  **既定 OFF。** Super は全測定の基準線なので、 黙って強くすると
        //  既存の基準値がまとめて無効になる。 A/B で測ってから既定を動かすこと。
        // ================================================================

        // ================================================================
        //  アブレーション (マクロ判断の価値を「無くして測る」)
        // ================================================================
        //  技量帯スイープは `wiringSkill` しか振っておらず、 航行・ショップ・報酬・イベントは
        //  **両アームで同一**だった ── マクロ判断の技量帯は一度も測られていない。
        //  丸ごと弱いマクロ AI を作るより、 **1 軸ずつランダム化して落ち幅を測る**方が安く、
        //  しかも軸ごとに順位が付く。 落ちない軸は「そこに技量が無い」という設計上の答えになる。
        //
        //  **専用乱数を使う。** GameRng を消費するとマップ・敵・アイテムの抽選がずれて
        //  同一シードのペア比較が壊れる。 ここは判断だけを潰し、 世界は動かさない。

        /// <summary>航行の選択を一様抽選へ潰す (既定 OFF)。 実測 −13.4pt (p&lt;0.0001)。</summary>
        [NonSerialized] public bool ablateNavigation;

        // ---- [計装] 航行スコアの同点率 ----
        //   航行は 13.4pt の軸。 `Rank()` が int を返すので同点がリスト順で割れている疑い。
        //   **直す前に頻度を数える** (戦闘では eps 同点と厳密同値を取り違えて 6 倍過大に見た)。
        private readonly List<float> _navScores = new List<float>(16);
        public static long NavDecisions, NavTiedDecisions, NavTiedSum, NavCandSum;
        public static void ResetNavStats()
            => NavDecisions = NavTiedDecisions = NavTiedSum = NavCandSum = 0;
        /// <summary>7層だけの与ダメ倍率内訳。 **1 攻撃あたりに正規化して出す** ──
        /// アーム間でラン数も到達率も違うので、 合計値では比べられない。</summary>
        private static string DescribeDamageBreakdown7F()
        {
            var b = CombatSystem.CombatManager.DamageBreakdown7F;
            double n = b[0];
            if (n < 1) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"【7層だけの与ダメ内訳 (1攻撃あたり)】攻撃 {n:N0} 回");
            sb.AppendLine($"  素火力 {b[1] / n,6:F2} + 攻撃端子出目 {b[2] / n,6:F2}"
                        + $" + パッシブ加算 {b[3] / n,5:F2} + 消費 {b[4] / n,4:F2} + 遺物 {b[5] / n,4:F2}"
                        + $"  = atkBase {b[6] / n,6:F2}");
            sb.AppendLine($"  **最終与ダメ {b[7] / n,7:F2}** / 固定ダメ {b[9] / n,5:F2}"
                        + $" / 追撃 {b[10] / n,5:F2} / 会心率 {100.0 * b[8] / n,5:F1}%");
            sb.AppendLine($"  与ダメ倍率: パッシブ {b[11] / n,5:F3} → メタ +{b[13] / n,5:F3}"
                        + $" → 遺物 +{b[15] / n,5:F3}"
                        + $"  = **最終 {b[16] / n,5:F3}**");
            sb.AppendLine($"  ※atkBase から最終与ダメへの実効倍率 {(b[6] > 0 ? b[7] / b[6] : 0),5:F3}"
                        + "   ── ここがアーム間で違えば倍率、 同じなら配分の問題");
            return sb.ToString();
        }

        public static string DescribeNavTies()
        {
            if (NavDecisions <= 0) return "";
            double d = NavDecisions;
            return "【航行スコアの同点率】判断 " + NavDecisions.ToString("N0")
                 + $" / 平均候補 {NavCandSum / d:F2} 本\n"
                 + $"  最良が同点だった判断 {NavTiedDecisions:N0} ({100.0 * NavTiedDecisions / d:F1}%)"
                 + $" / 同点 平均 {NavTiedSum / d:F2} 本"
                 + "   ※同点はリスト順で決まる ＝ そこは判断していない\n";
        }
        /// <summary>ショップの購入判断を潰す (買えるものから一様に買えるだけ買う)。</summary>
        [NonSerialized] public bool ablateShop;
        /// <summary>イベントの選択判断を潰す (提示された選択肢から一様抽選)。</summary>
        [NonSerialized] public bool ablateEvent;
        /// <summary>ショップの**順位付けだけ**を潰す (足切りは残し、通った候補から一様抽選)。
        /// <see cref="ablateShop"/> が「買い物の作法ごと潰す」のに対し、 こちらは
        /// 「どれが最良かを知っていること」だけの価値を切り出す。</summary>
        [NonSerialized] public bool ablateShopScore;

        [NonSerialized] private System.Random _ablationRng;
        private System.Random AblationRng => _ablationRng ?? (_ablationRng = new System.Random(0x_AB1A));
        /// <summary>ラン開始時に呼ぶ。 同一シードのラン比較でアブレーションまで再現させる。</summary>
        private void ResetAblationRng(int runSeed)
        {
            unchecked { _ablationRng = new System.Random((runSeed * 1103515245 + 12345) & 0x7FFFFFFF); }
        }

        /// <summary>階層 DP を使う (既定 OFF)。 立てると 1 手先読みが階層全体の解に変わる。</summary>
        [NonSerialized] public bool useFloorDpNavigation;
        /// <summary>DP の 1 段あたりの割引。 既定は既存の先読みと同じ 0.6。
        /// **0.6 のままだと 10 行先は 0.6^10 ≒ 0.006 で実質 1 手先読みと変わらない** ──
        /// 構造を入れただけで満足せず、 ここを掃引して初めて DP の意味が出る。</summary>
        [NonSerialized] public float floorDpDiscount = 0.6f;

        /// <summary>HP 比の量子化段数。 状態は (ノード, HP帯)。
        ///
        /// <para><b>これは精度のつまみではなく、実験の軸。</b> 1 手先読みは HP 比を float の
        /// まま <see cref="Rank"/> へ渡すが、DP は帯へ丸める ── 丸めが判定を反転させうるので、
        /// 深さを測るつもりで粗さも一緒に動かしてしまう交絡になる (2026-08-21 に実際に踏んだ)。
        /// 大きくすれば量子化はほぼ消え、DP は 1 手先読みと一致するはずで、
        /// **それが実装の正当性チェックになる**。</para></summary>
        [NonSerialized] public int floorDpHpBands = 11;   // 既定 = 0.1 刻み

        private int FloorDpHpBands { get { return Mathf.Clamp(floorDpHpBands, 2, 201); } }

        private float[] _dpValue;
        private bool[] _dpDone;
        private readonly Dictionary<string, int> _dpIndex =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private int HpBandOf(float ratio)
        {
            int bands = FloorDpHpBands;
            return Mathf.Clamp(Mathf.RoundToInt(ratio * (bands - 1)), 0, bands - 1);
        }

        /// <summary>1 決定ぶんのメモ表を用意する。 盤面は決定ごとに変わりうるので毎回作り直す
        /// (使い回して古い値を引くのは、 一番見つけにくい壊れ方)。</summary>
        private void FloorDpPrepare(FloorMap map)
        {
            _dpIndex.Clear();
            if (map == null) return;
            var nodes = map.GetAllNodes();
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] != null) _dpIndex[nodes[i].id] = i;

            int need = nodes.Count * FloorDpHpBands;
            if (_dpValue == null || _dpValue.Length < need)
            {
                _dpValue = new float[need];
                _dpDone = new bool[need];
            }
            Array.Clear(_dpDone, 0, need);
        }

        /// <summary>ノード <paramref name="node"/> を踏んでから階層の出口までの評価値
        /// (小さいほど良い)。 自身の <see cref="Rank"/> を含む。
        ///
        /// <para><b>再帰は前進辺だけを辿る。</b> 横移動辺 (row が増えない) を DP の中で辿ると
        /// 同一行を往復して循環しうる。 横移動は呼び出し側の pool が既に候補として持って
        /// いるので、 ここで扱わなくても選択肢は失われない ── かつ DAG であることが
        /// 構造的に保証され、 停止性を別途証明しなくてよくなる。</para>
        ///
        /// <para>HP は帯に量子化して状態に持つ。 「回復してからエリート」のような組み立ては
        /// HP が状態に入っていないと表現できない。</para></summary>
        private float FloorDpValue(FloorMap map, MapNode node, int band, int floorHit,
                                   GameLoop.RunState run, float lowBar)
        {
            if (map == null || node == null) return 0f;
            if (!_dpIndex.TryGetValue(node.id, out int idx)) return 0f;

            int bands = FloorDpHpBands;
            int key = idx * bands + band;
            if (_dpDone[key]) return _dpValue[key];

            float hp = band / (float)(bands - 1);
            // preferRest は「いま休むべきか」の判断なので手前の 1 手にしか掛けない。
            // 先の行にまで掛けると、 遠くの休憩マスが実際より魅力的に見える。
            float total = Rank(node, hp, _curCombatAverse, false, run, lowBar);

            var succ = map.GetReachableFrom(node.id);
            if (succ != null && succ.Count > 0)
            {
                int nextBand = HpBandOf(
                    PredictHpRatioAfter(node, hp, floorHit, run));
                float bestNext = float.MaxValue;
                for (int i = 0; i < succ.Count; i++)
                {
                    MapNode s = succ[i];
                    if (s == null || s.row <= node.row) continue;   // 前進辺のみ = 循環しない
                    float v = LateralPenalty(node, s, run)
                            + FloorDpValue(map, s, nextBand, floorHit, run, lowBar);
                    if (v < bestNext) bestNext = v;
                }
                if (bestNext != float.MaxValue) total += floorDpDiscount * bestNext;
            }

            _dpValue[key] = total;
            _dpDone[key] = true;
            return total;
        }

        /// <summary>横移動 (row が増えない移動) に課す希望コストのペナルティ。
        ///
        /// **先読みを入れるならこれが必須。** Rank() はタイル種別と HP しか見ないので、
        /// 横移動が希望を削ることが score に現れず、 先読みは「タダで遠回りできる」と誤認する。
        /// 実際 2 手先読みだけを入れた測定で横移動の希望損が +60%、
        /// 発狂到達率が 15.6% → 33.0% へ倍増した (2026-08-05)。
        /// 希望が低いほど 1 回の横移動が重いので、 残量で重み付けする。
        ///
        /// <para><b>2026-09-12 修正: 実コストを引くようにした。</b> 旧実装は定数
        /// <c>HopeSystem.LateralCost</c> (5) を直接読んでおり、 燈火 r10 で実コストが
        /// 下がっても<b>BOT の評価は 5 のまま</b>だった。 課金側 (<c>HopeSystem.ApplyMove</c>) は
        /// メタ調整後の値を引いているので、 <b>規則と方策がずれていた</b> ──
        /// 極点を取っても BOT は寄り道を増やさず、 効果が測定に現れない。</para></summary>
        private static float LateralPenalty(MapNode from, MapNode to, GameLoop.RunState run)
        {
            if (from == null || to == null || run == null) return 0f;
            if (to.row > from.row) return 0f;                 // 前進・斜めは無料
            int cost = MetaProgression.MetaBuffApplicator.GetLateralHopeCost();
            if (cost <= 0) return 0f;                         // 無税なら寄り道を抑制しない
            float cap = Mathf.Max(1, run.hopeCap);
            float pct = Mathf.Clamp01(run.hope / cap);
            // 希望満タンなら軽く、 枯渇に近いほど重く (満: ×1 → 空: ×4)。
            float p = cost * (1f + 3f * (1f - pct));

            // **2026-09-13: 余裕があるときは希望を資源として使う。**
            //   比 (hope/cap) だけだと、 上限が伸びても「満タンなら ×1」で頭打ちになり、
            //   燈火で上限を +135 積んでも BOT の寄り道量が変わらなかった。
            //   実際のコストは「あと何回払えるか」なので、 **悲観帯 (45) までの絶対距離**で割る。
            //   cost 10 回ぶんの余裕があれば、 1 回の横移動はほぼ無視できる。
            int headroom = Mathf.Max(0, run.hope - GameLoop.HopeSystem.FloorPessimism);
            float rich = Mathf.Clamp01(headroom / (float)(cost * 10));
            return p * (1f - 0.85f * rich);
        }

        /// <summary>そのタイルを踏んだ後の HP 割合の概算。 2 手先読みで「次の状態」を作るために使う。
        /// 精度は要らない ── 必要なのは「回復系なら上がる / 戦闘系なら下がる」の向きだけ。
        /// 戦闘は想定被弾 1 発ぶん、 エリートは 2 発ぶん減るものとして見る。</summary>
        private static float PredictHpRatioAfter(MapNode node, float hpRatio, int floorHit, GameLoop.RunState run)
        {
            if (node == null || run == null || run.playerMaxHP <= 0) return hpRatio;
            TileType t = node.EffectiveType;
            float unit = floorHit > 0 ? (float)floorHit / run.playerMaxHP : 0.12f;

            // 2 体戦は攻撃が 2 パケット来る。 **開示済みのときだけ**見込みに織り込む
            //   (未開示のプリセットを読むと 1 マス前開示という規則を BOT だけが破ることになる)。
            //   戦闘長は伸びない (与ダメは両方に入るので撃破ターンは max) ので、
            //   増えるのは 1 ターンあたりの被ダメ ＝ 素直に倍で見る。
            if (node.encounterRevealed && !string.IsNullOrEmpty(node.encounterEnemyB)) unit *= 2f;

            switch (t)
            {
                case TileType.Rest:        return Mathf.Min(1f, hpRatio + 0.30f);
                case TileType.Battle:      return Mathf.Max(0f, hpRatio - unit);
                case TileType.EliteBattle: return Mathf.Max(0f, hpRatio - unit * 2f);
                case TileType.Boss:        return Mathf.Max(0f, hpRatio - unit * 3f);
                default:                   return hpRatio;   // ショップ/イベント/宝箱は増減なしとみなす
            }
        }

        /// <summary>低いほど優先。HP帯(危機/低/健康)で重み分け。
        /// 戦闘タイル(Battle/EliteBattle)のみ profile で差し替え、他は共通固定。
        /// averse=false(戦闘貪欲): 健康なら戦闘を最優先級で選ぶ。
        /// averse=true(戦闘回避): 戦闘を最下位級にし、戦闘以外があれば必ず回避。</summary>
        /// <summary>金の逼迫度を測る基準額。 店頭価格 4〜20G・1 ラン 19 個購入が目標なので、
        /// 「いま 3〜4 個買える」= 逼迫していない、 とみなす額に置く。</summary>
        /// <summary>「これだけ持っていれば金に困っていない」水準。 <c>gp = clamp01(1 − 所持/この値)</c>。
        ///
        /// <para><b>収入と一緒に動かすこと。</b> 60 は収入が 100G/ラン 前後だった頃の目盛りで、
        /// 2026-09-15 の経済作り直し後は 456G/ラン。 実測 (2,000 ラン) で
        /// <b>判断の 36.6% が gp=0 に飽和</b>していた ── そこでは「稼ぐ必要性」の項も、
        /// エリートの報酬倍率 (goldPull = 0.8 × RewardMul × gp) も、
        /// ショップの優先度 (1 + 2.0gp − 0.8(1−gp)) も<b>まとめて定数に潰れる</b>。</para></summary>
        public static float GoldComfort = 60f;

        /// <summary>0 = 潤沢 / 1 = 無一文。</summary>
        // [計装 2026-09-15] gp の分布。 **飽和しているかを数える。**
        //   gp = clamp01(1 - coins/GoldComfort) は所持 GoldComfort 以上で 0 に張り付く。
        //   GoldComfort=60 は収入が 100G/ラン の頃の値で、 現在は 449G/ラン。
        //   gp が 0 のとき「稼ぐ必要性」の項が丸ごと消え、 エリートの報酬倍率
        //   (goldPull = 0.8 × RewardMul × gp) も同時に無効化される。
        public static long GpSamples, GpZero, GpOne; public static double GpSum;
        public static void ResetGoldPressureStats() { GpSamples = GpZero = GpOne = 0; GpSum = 0; }

        [NonSerialized] private int _lastShopExitCoins;
        [NonSerialized] private bool _lastShopExitSeen;
        [NonSerialized] private int _lastShopExitFloor;
        [NonSerialized] private int _floor7ShopEntryCoins = -1;
        /// <summary>[計装 2026-09-18] 最終店の出口と 7 層の店の入口を <b>クリア(0) / 非クリア(1)</b> で分けた和。</summary>
        public static readonly long[] ExitByOutcomeN = new long[2], Floor7EntryN = new long[2];
        public static readonly double[] ExitByOutcomeCoins = new double[2], ExitByOutcomeGain = new double[2],
                                        ExitByOutcomeFloor = new double[2], Floor7EntryCoins = new double[2];
        /// <summary>[計装] 最後に店を出たときの残金と、 そこからラン終了までの増分 (ラン単位)。</summary>
        public static long ShopExitRuns;
        public static double ShopExitCoinsSum, PostShopGainSum;
        public static void ResetShopExitStats()
        {
            ShopExitRuns = 0; ShopExitCoinsSum = PostShopGainSum = 0;
            for (int i = 0; i < 2; i++)
            {
                ExitByOutcomeN[i] = Floor7EntryN[i] = 0;
                ExitByOutcomeCoins[i] = ExitByOutcomeGain[i] = ExitByOutcomeFloor[i] = Floor7EntryCoins[i] = 0;
            }
        }

        /// <summary>[計装] 余剰リロールループの出口。 0=リロール不可 / 1=水位 / 2=リロール失敗 / 3=回数上限
        /// / 4=価値 (停止則: 見込み ≤ 費用)。</summary>
        public static readonly long[] SurplusExit = new long[5];
        public static readonly double[] SurplusExitCoins = new double[5];
        public static void ResetSurplusExitStats()
        { System.Array.Clear(SurplusExit, 0, 5); System.Array.Clear(SurplusExitCoins, 0, 5); }
        private static void NoteSurplusExit(int why, int coins)
        { if (why < 0 || why > 4) return; SurplusExit[why]++; SurplusExitCoins[why] += coins; }

        /// <summary>[計装 2026-09-17] <b>リロールの経路別。</b> 店の処理には独立した
        /// リロールループが 5 本あり、 <b>どれが 223G を動かしているのか分かっていなかった</b>。
        ///
        /// <para>フェーズ2 (品質) の条件は「S 級を 1 個も持っていない」なので、
        /// <b>最初の店で S を買った時点で恒久停止する</b>はず ── そうであれば回数の大半は
        /// フェーズ2b の水位ルール (<c>coins &gt;= price + SurplusBuyReserve</c>) 由来で、
        /// <b>品物の評価が一切入っていない</b>ことになる。 推論のままにせず数える。</para>
        ///
        /// <para>添字は <see cref="RerollPathName"/> と対応。</para></summary>
        public static readonly long[] RerollPathCount = new long[6];
        public static readonly double[] RerollPathCoins = new double[6];
        public static readonly string[] RerollPathName =
        { "品質(S/A狙い)", "余剰再投資", "最終店の消耗品", "Super全賭け", "強盗直前", "防御ストック" };
        public static void ResetRerollPathStats()
        {
            System.Array.Clear(RerollPathCount, 0, 6); System.Array.Clear(RerollPathCoins, 0, 6);
            DiscardRolls = DiscardPassive = DiscardPart = DiscardWeapon = DiscardJunk = 0; DiscardPower = 0;
            System.Array.Clear(DiscardRollsByNth, 0, 4); System.Array.Clear(DiscardItemsByNth, 0, 4);
        }
        /// <summary>何回目のリロールか別 (0=1回目 / 1=2回目 / 2=3回目 / 3=4回目以降)。</summary>
        public static readonly long[] DiscardRollsByNth = new long[4], DiscardItemsByNth = new long[4];

        /// <summary>[計装 2026-09-18] リロールが張り替えで捨てた「フェーズ3 なら買った」枠。
        /// 種別ごとの件数、 準パワーの和、 そのうち PowerCuts[3] 未満 (スコアでは足切りされる品) の件数。</summary>
        public static long DiscardRolls, DiscardPassive, DiscardPart, DiscardWeapon, DiscardJunk;
        public static double DiscardPower;
        /// <summary>1 回ぶんを経路へ計上する。 <paramref name="paid"/> は実際に減ったゴールド
        /// (〈試し振りの符牒〉の無料リロールは 0 になる)。</summary>
        private static void NoteReroll(int path, int paid)
        { if (path < 0 || path > 5) return; RerollPathCount[path]++; RerollPathCoins[path] += paid; }

        /// <summary>[計装] ラン終了時の残金。 クリア / 非クリア 別。</summary>
        public static long EndGoldClearN, EndGoldDeadN;
        public static double EndGoldClearSum, EndGoldDeadSum;
        public static long EndGoldClear30, EndGoldDead30;
        public static void ResetEndGoldStats()
        { EndGoldClearN = EndGoldDeadN = EndGoldClear30 = EndGoldDead30 = 0;
          EndGoldClearSum = EndGoldDeadSum = 0; }
        private static void NoteEndGold(int coins, bool cleared)
        {
            if (cleared) { EndGoldClearN++; EndGoldClearSum += coins; if (coins >= 30) EndGoldClear30++; }
            else         { EndGoldDeadN++;  EndGoldDeadSum  += coins; if (coins >= 30) EndGoldDead30++; }
        }

        private static float GoldPressure(GameLoop.RunState run)
        {
            if (run == null) return 0.5f;
            float gp = Mathf.Clamp01(1f - run.coins / GoldComfort);
            GpSamples++; GpSum += gp;
            if (gp <= 0.0001f) GpZero++; else if (gp >= 0.9999f) GpOne++;
            return gp;
        }

        // ── エリートの航行評価 (2026-09-15) ──
        //   `Rank` の戦闘枝で `elite ? (EliteNavBase - EliteNavHpBonus * hpRatio) : 1f` として使う
        //   (**小さいほど優先**)。 通常戦の基準 1.0 に対する位置を HP 比で決める:
        //
        //       HP 100%  →  0.40   エリートを取りに行く
        //       HP  75%  →  0.70   まだエリート優先
        //       HP  50%  →  1.00   通常戦と釣り合う (交差点)
        //       HP  50% 未満       low ゲート (95/98) が先に弾く
        //
        //   交差点を 50% に置いたのは、 確信チェーンの強制優先枝が使う `hpRatio >= 0.5f` と
        //   同じ境界だから ── 二つの枝が違う HP 観を持っていると、 片方が他方を打ち消す。
        //   **較正専用**: AutoRunner の `eliteNavBase` / `eliteNavHpBonus` から上書きできる。

        // ── エリートの戦闘コスト見積り (2026-09-20) ──
        //   **旧判断は HP 比だけで、 自分の火力と相手の硬さを見ていなかった。** 満タンなら必ず取りに行くので、
        //   初期装備の 1 層でもほぼ毎ラン精鋭を踏み、 1 層エリート戦の死亡率が 46% (被ダメ平均 53%) だった。
        //   危険度ゲート (FightBudgetDanger) の基準「平均 1 戦 × 2」も中身はほぼ雑魚戦で、
        //   1 層では精鋭 1 戦 ≒ 雑魚 2.4 戦ぶん削られることが見えていなかった。
        //
        //   見積り: 精鋭の HP ÷ (このランの 1 ターン与ダメ) = 倒すまでのターン数、
        //           × (このランの 1 ターン被ダメ) × 精鋭の攻撃倍率 = 失う HP。
        //   戦った後に HP が EliteReserve (最大 HP 比) 以上残る見込みがなければ、 精鋭を避ける。
        //   **実績が無いうち (まだ雑魚と戦っていない) は精鋭を踏まない** ── 自分の強さが分からないまま
        //   格上に挑むのは判断ではなく賭け。

        /// <summary>精鋭の戦闘コスト見積りを使うか。 A/B 用 (AutoRunner.navEliteCost)。</summary>
        public static bool UseEliteCostNav = true;
        /// <summary>見積りの上振れ余裕。 精鋭は攻撃ロール上限 +3 で波が荒いので 1.5 倍で見る。</summary>
        public static float EliteLossSafety = 1.5f;
        /// <summary>精鋭戦の後に残したい HP (最大 HP 比)。</summary>
        public static float EliteReserve = 0.30f;
        /// <summary>[計装] 見積りで精鋭を避けた回数 / 見積りを行った回数 / 実績なしで避けた回数。</summary>
        public static long EliteCostChecks, EliteCostAvoided, EliteCostNoData;
        public static void ResetEliteCostStats() { EliteCostChecks = EliteCostAvoided = EliteCostNoData = 0; }

        /// <summary>この精鋭マスで失う HP の見積り。 実績が無ければ負を返す。</summary>
        private static float EliteFightLoss(MapNode node, GameLoop.RunState run)
        {
            if (run == null || run.navTurns <= 0 || run.navDealt <= 0) return -1f;
            float hp;
            var ea = node != null && node.encounterRevealed ? CombatSystem.EnemyDatabase.Get(node.encounterEnemyA) : null;
            float atkRatio;
            if (ea != null) { hp = ea.maxHP; atkRatio = EliteAttackRatio(ea, run.currentFloor); }
            else
            {
                // 未開示: この層に出るエリート敵の平均 (直近 3 層の窓は FloorManager と同じ)。
                float sum = 0, ratio = 0; int n = 0;
                foreach (var e in CombatSystem.EnemyDatabase.GetByFloorRange(Mathf.Max(1, run.currentFloor - 2), run.currentFloor))
                {
                    if (e == null || !e.elite) continue;
                    sum += e.maxHP; ratio += EliteAttackRatio(e, run.currentFloor); n++;
                }
                if (n == 0) return -1f;
                hp = sum / n; atkRatio = ratio / n;
            }
            float dealtPerTurn = run.navDealt / (float)run.navTurns;
            float takenPerTurn = run.navTaken / (float)run.navTurns;
            float turns = Mathf.Ceil(hp / Mathf.Max(1f, dealtPerTurn));
            return turns * takenPerTurn * atkRatio;
        }

        /// <summary>このエリートの攻撃が、 この層の雑魚の平均攻撃の何倍か。 ラン内の被ダメ実績 (ほぼ雑魚戦) をエリートへ換算する係数。</summary>
        private static float EliteAttackRatio(CombatSystem.EnemyData elite, int floor)
        {
            float sum = 0; int n = 0;
            foreach (var e in CombatSystem.EnemyDatabase.GetByFloorRange(Mathf.Max(1, floor - 2), floor))
            {
                if (e == null || e.elite || GameLoop.BossIds.IsBoss(e.id) || e.floor >= GameLoop.FloorManager.SpecialEncounterFloor) continue;
                sum += e.EffectiveBaseAttack + 0.5f * (e.attackRollMin + e.attackRollMax); n++;
            }
            if (n == 0 || sum <= 0f) return 1f;
            float normal = sum / n;
            return (elite.EffectiveBaseAttack + 0.5f * (elite.attackRollMin + elite.attackRollMax)) / normal;
        }

        /// <summary>精鋭を踏んでよいか。 見積りを使わない設定なら常に true。</summary>
        private static bool EliteAffordable(MapNode node, float hpRatio, GameLoop.RunState run)
        {
            if (!UseEliteCostNav || run == null || run.playerMaxHP <= 0) return true;
            EliteCostChecks++;
            float loss = EliteFightLoss(node, run);
            if (loss < 0f) { EliteCostNoData++; EliteCostAvoided++; return false; }
            float after = hpRatio - loss * EliteLossSafety / run.playerMaxHP;
            if (after >= EliteReserve) return true;
            EliteCostAvoided++;
            return false;
        }

        /// <summary>エリートの基準値 (HP 0 のときの値)。 大きいほどエリートを避ける。</summary>
        public static float EliteNavBase = 1.6f;
        /// <summary>HP 比 1 あたりの引き。 大きいほど「健康ならエリート」が強くなる。</summary>
        public static float EliteNavHpBonus = 1.2f;

        /// <summary>層化ホールドアウトの抽選。 候補を**提示重みの逆数**で重み付けして 1 つ選ぶ。
        ///
        /// <para>重みの出所は <see cref="InventorySystem.Shop.ShopManager.TierOfferWeight"/> 一箇所。
        /// ここで直書きすると、 ショップ側の重みを変えたときに静かに食い違う。</para>
        ///
        /// <para>rarity を引けない枠 (出目パーツ・強化素材) は中立の 0.25 相当として扱う。
        /// 在庫に居ない帯は選べないので、 均等化は<b>並んでいる範囲でのみ</b>成立する。</para></summary>
        private int PickStratified(InventorySystem.Shop.ShopInventory inv)
        {
            if (_exploreCandidates.Count == 1) return _exploreCandidates[0];
            var db = InventorySystem.ItemDatabase.Instance;
            double total = 0;
            _exploreWeights.Clear();
            for (int i = 0; i < _exploreCandidates.Count; i++)
            {
                var s = inv.slots[_exploreCandidates[i]];
                var data = (s != null && !string.IsNullOrEmpty(s.itemId)) ? db?.GetItem(s.itemId) : null;
                float w = data != null
                    ? 1f / Mathf.Max(0.001f, InventorySystem.Shop.ShopManager.TierOfferWeight(data.rarity))
                    : 4f;   // rarity 不明 (パーツ/素材) は 1/0.25
                _exploreWeights.Add(w);
                total += w;
            }
            double r = _exploreRng.NextDouble() * total;
            for (int i = 0; i < _exploreCandidates.Count; i++)
            {
                r -= _exploreWeights[i];
                if (r <= 0) return _exploreCandidates[i];
            }
            return _exploreCandidates[_exploreCandidates.Count - 1];
        }

        private readonly List<double> _exploreWeights = new List<double>();

        /// <summary>[計装] ラン終了時の家系別最高段を <see cref="FamilyTierStats"/> へ渡す。
        ///
        /// <para><b>14 家系すべてを渡す。</b> 取得された家系だけを渡すと
        /// 「0 段で終わった家系」が分母から落ち、 到達率が過大に出る。</para></summary>
        private void NoteFamilyTierRunEnd()
        {
            var run = GameManager.Instance?.Run;
            var db = InventorySystem.ItemDatabase.Instance;
            var max = new Dictionary<string, int>();
            foreach (var fam in InventorySystem.PassiveSkills.PassiveSkillRegistry.LeveledFamilies)
                max[fam] = 0;

            void Scan(string id)
            {
                var d = db?.GetItem(id);
                if (d?.passiveSkills == null) return;
                foreach (var ps in d.passiveSkills)
                {
                    if (string.IsNullOrEmpty(ps.internalName)) continue;
                    var (f, lv) = InventorySystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ps.internalName);
                    if (lv > 0 && max.TryGetValue(f, out int prev) && lv > prev) max[f] = lv;
                }
            }
            if (run != null)
            {
                if (run.ownedPassiveItems != null) foreach (var id in run.ownedPassiveItems) Scan(id);
                Scan(run.equippedWeaponId);
                // 昇華済みも「到達した段」として数える (1G 割引の根拠と揃える)。
                if (run.ascendedPassiveIds != null) foreach (var id in run.ascendedPassiveIds) Scan(id);
            }
            AutoTest.FamilyTierStats.EndRun(max);
        }

        private float Rank(MapNode node, float hpRatio, bool averse, bool preferRest, GameLoop.RunState run,
                           float lowBar)
        {
            if (node == null) return 4f;
            TileType t = node.EffectiveType;
            var polN = AutoTest.PolicyParameters.Current;
            bool crit = hpRatio < polN.hpCritThreshold;   // 危機
            bool low  = hpRatio < lowBar;                 // 低HP (危険度駆動・床は hpLowThreshold)

            // 空腹尽きかけ／貪欲のボス前整え: 休憩を最優先（食事＝空腹全回復も兼ねる）
            if (preferRest && t == TileType.Rest) return -1;

            // 確信チェーン進路の強制優先 (両プロファイル共通)
            //  - 災厄の予兆 未完了 (= convictionStage 0) → イベント/ミステリ を常に最優先
            //    (Event/Mystery は戦闘でないので HP に関わらず追跡可能)
            //  - 災厄の予兆 完了 + 真理未到達 (stage 1〜4) → HP≥50% のみエリート最優先
            //    (エリートは戦闘発生 = HP リスクなので安全時のみ)
            int convStage = run?.convictionStage ?? 0;
            // 7層資格は6層の一度限りイベントでのみ得る。イベントマスを見つけたら
            // HPに関係なく最優先し、人間が進行条件を理解している場合を再現する。
            if (run != null && run.currentFloor == 6
                && GameLoop.ConvictionSystem.HasResolveOrBetter(run)
                && !GameLoop.ConvictionSystem.HasTruth(run)
                && t == TileType.Event)
                return -20;
            if (convStage == 0)
            {
                // Mystery は 20% でしか Event 化しないため Bot の確信進路としては当てにせず、
                // Event タイルのみ強優先する。
                if (t == TileType.Event) return -10;
            }
            else if (convStage > 0 && convStage <= GameLoop.ConvictionSystem.StageTruth)
            {
                if (t == TileType.EliteBattle && hpRatio >= 0.5f && EliteAffordable(node, hpRatio, run)) return -5;
            }

            // --- 戦闘タイル: ここだけが比較軸（1変数） ---
            // HP低下時 (50%未満) は profile に関係なく Battle/Elite を強く忌避する。
            //  (回復を最優先にしたいというユーザー要望)
            // 金の逼迫度。 **2026-08-28 追加**。 それ以前の Rank は `run` を
            //   convictionStage と currentFloor にしか使っておらず、 **所持金を一度も見ていなかった**。
            //   「金が無いから戦って稼ぐ」「余っているから店を優先する」がどちらも表現できず、
            //   ショップは常に 1、 戦闘は常に 1/2 の固定階級だった。
            float gp = GoldPressure(run);

            if (t == TileType.Battle || t == TileType.EliteBattle)
            {
                bool elite = t == TileType.EliteBattle;

                // **開示済みなら、そのマスの敵で忌避を判断し直す** (2026-09-16)。
                //   `lowBar` の元になる EstimateFloorMaxHit は「その層までに出る敵の最悪値」で、
                //   4 層以降はずっと death_knight (攻撃 41) 基準。 ところが `node.encounterRevealed`
                //   なら**次に戦う敵は確定している** ── 下のスコア微調整 (0.03 × 攻撃値) では
                //   既にその値を読んでいるのに、 <b>戦うかどうかの足切りだけ層の最悪ケースのまま</b>
                //   だった。 弱い敵が見えているマスも death_knight 基準で門前払いしていた。
                //   規則は変えない (見えていないマスは従来どおり層の最悪値)。
                if (UseTileDanger && node.encounterRevealed)
                {
                    float tileBar = TileDanger(node, elite, run);
                    if (tileBar > 0f) low = hpRatio < tileBar;
                }

                // 精鋭は戦闘コストの見積りでも弾く (2026-09-20)。 弾いたら低 HP と同じ扱い (強く忌避)。
                if (elite && !low && !EliteAffordable(node, hpRatio, run)) low = true;
                if (low) return elite ? (crit ? 98f : 95f) : (crit ? 96f : 93f); // 低HP/危機: 強く忌避
                if (averse) return elite ? 93f : 90f;

                // ── エリート vs 通常戦の基準値 (2026-09-15 修正) ──
                //   **旧: `elite ? 2f : 1f` ＝ 通常戦がエリートより常に上位という固定順位。**
                //   下の gp 補正 (最大 1.1 対 0.8) を足しても差は 0.7〜1.0 残るので、
                //   **所持金がいくらでもエリートは一度も選ばれなかった**。
                //   実測ではエリートは踏むほど得 (格上げ率 0%→50% でクリア率 21.69%→28.13%・
                //   z=+13.0) で、 BOT は得な選択肢を構造的に避けていた。
                //
                //   危険は既に 3 箇所で見ている ── low/averse ゲート、 下の被攻撃値、
                //   そしてエリート敵の数値。 ここが持つべきは<b>報酬の差</b>と
                //   <b>それを受け止められる余力</b>で、 それは HP 比で表す:
                //   満タンなら取りに行き、 半分で通常戦と釣り合い、 それ以下は上のゲートが弾く。
                float score = elite ? (EliteNavBase - EliteNavHpBonus * hpRatio) : 1f;

                // 稼ぎの必要性。 無一文なら戦闘の優先度を上げる。
                //
                //   **エリートの取り分は CombatRewards.EliteRewardMul から引く** (2026-09-15・2026-09-20 に移設)。
                //   旧実装は `elite ? 1.1f : 0.8f` の決め打ちで、 <b>報酬倍率を上げても
                //   BOT の評価が一切動かなかった</b>。 危険側は 2026-09-15 に AttackMul を
                //   反映させたので、 報酬だけ固定のままだと<b>強化するほど一方的に嫌われる</b> ──
                //   実測で E (攻撃 ×1.35→×1.50) を入れるとエリート踏破が 6.32 → 5.39 に落ち、
                //   報酬を ×1.8 → ×2.6 に上げても戻らなかったのはこれが原因。
                //   危険と報酬の両方を同じ出典から読ませる。
                float goldPull = elite ? 0.8f * GameLoop.CombatRewards.EliteRewardMul : 0.8f;
                score -= goldPull * gp;

                // ── エンカウント (2026-08-28) ──
                //   **開示済みのノードしか見ない。** 未開示のプリセットを読むと BOT だけが
                //   1 マス前開示という規則の外側に立つことになり、 機構の意味が消える。
                if (node.encounterRevealed)
                {
                    // 2 体戦は**通常マス限定** (2026-09-14 以降エリートは常に単体・FloorManager)。
                    //   報酬 2 倍の引き ×(HPが高いほど強い) と、 攻撃 2 パケットの押し返し
                    //   ×(HPが低いほど強い)。 健康なら取りに行き、 削れていれば迂回する
                    //   ＝「普段は地雷、 仕上がっていれば最高の稼ぎ場」という設計そのもの。
                    if (!string.IsNullOrEmpty(node.encounterEnemyB))
                    {
                        score -= 1.4f * hpRatio;
                        score += 1.8f * (1f - hpRatio);
                    }

                    // 相手の攻撃値で薄く差をつける。 **戦闘マス同士の同点を割るのはここだけ。**
                    //   従来は同じ TileType なら常に同値だったので、 戦闘 vs 戦闘の比較は
                    //   100% 同点 → リスト順のタイブレークに落ちていた。
                    //
                    //   エリートマスの敵はエリート敵 (enemies.json の elite: true) そのものなので、 素の値で読む。
                    var ea = CombatSystem.EnemyDatabase.Get(node.encounterEnemyA);
                    if (ea != null) score += 0.03f * ea.EffectiveBaseAttack;
                    var eb = CombatSystem.EnemyDatabase.Get(node.encounterEnemyB);
                    if (eb != null) score += 0.03f * eb.EffectiveBaseAttack;
                }
                return score;
            }

            // --- 非戦闘タイル: 両プロファイル共通固定 ---
            switch (t)
            {
                case TileType.Rest:        return crit ? 0f : low ? 0f : 6f;
                // 店は所持金で価値が変わる。 無一文なら踏んでも何も買えず、
                //   潤沢なら Battle(1) を抜いて最優先級になる。
                case TileType.Shop:        return 1f + 2.0f * gp - 0.8f * (1f - gp);
                case TileType.Treasure:    return crit ? 2f : low ? 1f : -2f; // 装備強化源: T4到達率改善のため健康時優先度UP (0 → -2)
                case TileType.Event:       return crit ? 5f : 3f;
                case TileType.Mystery:     return crit ? 5f : 3f;
                case TileType.Exchange:    return crit ? 5f : 2f;  // ビルド強化源（厳密アップグレード）
                case TileType.Trap:        return crit ? 7f : 6f;
                case TileType.Gate:        return 9f;  // 7層の終端。踏まないと 8 層へ降りられない
                case TileType.Boss:        return 9f;  // 最後に残れば踏む(フロアクリア必須)
                case TileType.Outpost:     return 0f;
                default:                   return 4f;
            }
        }

        /// <summary><b>この店が「ランで最後のショップ」か (2026-09-12)。</b>
        ///
        /// <para><b>なぜ要るか。</b> 値下げ交渉(=強盗) の代償は
        /// 「以降のショップ価格 ×<see cref="InventorySystem.Shop.ShopManager.RobberySurcharge"/>」 で、
        /// <b>残りの店の数にそのまま比例する</b>。 旧規則の `currentFloor &gt;= 6` は条件を満たした
        /// 最初の店で撃つ貪欲規則だったので、 各フロアの確定ショップ (MapGenerator.ForceInjectShops) と
        /// 7 層のボス前確定ショップ (Shop→Rest→Boss) をまとめて割増価格にしていた。</para>
        ///
        /// <para><b>判定は純粋な到達可能性で行う。</b> 「7 層のあの店」 のような座標決め打ちは
        /// マップ生成を触るたびに黙って壊れる。 現在地から前進して届くノードに Shop が
        /// 1 つも無いことを確かめる ── 横移動リンクは同じ行へ戻るだけなので探索は前へしか進まない。
        /// <b>判定できないときは false</b> (＝撃たない) ── 撃つのは取り返しがつかない。</para></summary>
        /// <summary>この店で手をつけずに残す額。 <b>強盗の直前リロール用の取り置き。</b>
        /// 購入の足切り (<c>Buy</c> / <c>BuyPriorityPass</c>) がここを差し引いて判断する。
        /// 店を出るたびに 0 へ戻す。</summary>
        private int _shopReserve;

        private static bool IsFinalShopOfRun(GameLoop.RunState run)
        {
            if (run == null) return false;
            // 以降のフロアには確定ショップが 1 個ずつ生える。 **店が生える最後の層**より
            //   手前なら、 この店が最後ということはありえない。
            //   <b>maxFloor ではない</b> ── 8 層 (Null Point) に店は無いので、
            //   maxFloor で比べると永遠に成立せず強盗が撃たれなくなる (2026-09-14)。
            if (run.currentFloor < MapSystem.MapGenerator.LastFloorWithShop) return false;

            var mm = MapSystem.MapManager.Instance;
            var map = mm != null ? mm.CurrentMap : null;
            var cur = mm != null ? mm.CurrentNode : null;
            if (map == null || cur == null) return false;

            var seen = new HashSet<string> { cur.id };
            var stack = new Stack<string>();
            stack.Push(cur.id);
            while (stack.Count > 0)
            {
                var node = map.GetNode(stack.Pop());
                if (node?.connections == null) continue;
                foreach (var nextId in node.connections)
                {
                    if (!seen.Add(nextId)) continue;
                    var next = map.GetNode(nextId);
                    if (next == null) continue;
                    if (next.EffectiveType == MapSystem.TileType.Shop) return false;
                    stack.Push(nextId);
                }
            }
            return true;
        }

        private void DoShop()
        {
            var gm = GameManager.Instance;
            var sm = ShopManager.Instance;
            var inv = sm != null ? sm.Current : null;
            if (inv != null && inv.slots != null)
            {
                var run = gm.Run;

                // L1出現lift用: このショップでの提示アイテムを記録 (購入有無を問わず)
                if (_cur != null)
                {
                    for (int oi = 0; oi < inv.slots.Count; oi++)
                    {
                        var os = inv.slots[oi];
                        if (os != null && !string.IsNullOrEmpty(os.itemId))
                            _cur.offeredItemsEver.Add(os.itemId);
                    }
                }

                bool Buy(int i)
                {
                    var s = inv.slots[i];
                    // **支払い可能額で見る** ── 燈火 r10 は不足分を希望で払える。
                    //   ここを run.coins のままにすると、 足切りを通した候補が
                    //   この入口で弾かれ、 **機構が 1 回も発動しない** (実測 170,000 ラン で 0 件)。
                    //   同じ判断を 2 箇所に置いた典型で、 2026-08-10 の WouldUpgrade と同じ形。
                    if (s == null || s.sold
                        || s.price > GameLoop.HopePayment.PolicyAffordable(run) - _shopReserve)
                        return false;
                    if (s.kind != InventorySystem.Shop.ShopSlotKind.WeaponMaterial
                        && !string.IsNullOrEmpty(s.itemId))
                    {
                        // 2026-06-22 Phase C: 購入候補が現所持品の上位 Lv なら、 下位 Lv を先に売却 (装備武器は除く)
                        TrySellLowerTierBefore(run, s.itemId);
                    }
                    int before = run.coins;
                    string id = s.itemId;
                    gm.ShopBuy(i);
                    // **成否は「金が減ったか」では判定しない。** 全額を希望で払った場合
                    //   (所持金 0 で購入) は金が動かないので、 買えたのに失敗として数えてしまう。
                    //   在庫が売り切れたかを見るのが素直 ── 素材枠だけは sold を立てないので
                    //   従来どおり金の減少で見る。
                    bool bought = s.kind == InventorySystem.Shop.ShopSlotKind.WeaponMaterial
                        ? run.coins < before
                        : s.sold;
                    if (bought)
                    {
                        _cur.shopPurchases++;
                        // 種別ごとに分解する。 価格倍率の弾性が枠ごとに違うため (上記コメント)。
                        switch (s.kind)
                        {
                            case InventorySystem.Shop.ShopSlotKind.Passive:        _cur.shopBuyPassive++; break;
                            case InventorySystem.Shop.ShopSlotKind.Consumable:     _cur.shopBuyConsumable++; break;
                            case InventorySystem.Shop.ShopSlotKind.Weapon:         _cur.shopBuyWeapon++; break;
                            case InventorySystem.Shop.ShopSlotKind.FacePart:       _cur.shopBuyDice++; break;
                            case InventorySystem.Shop.ShopSlotKind.WeaponMaterial: _cur.shopBuyMaterial++; break;
                        }
                        if (AutoTest.LearnedPriorityProvider.IsPriority(id)) _cur.priorityItemsAcquired++;
                        // L1学習: 購入時点で取得集合に記録（後で使い切って消えても残る）
                        if (!string.IsNullOrEmpty(id)) _cur.acquiredItemsEver.Add(id);
                        return true;
                    }
                    return false;
                }

                // Phase C: 購入候補のパッシブが、 現所持品 (装備武器以外) の上位 Lv に該当するなら下位を売却
                void TrySellLowerTierBefore(RunState rs, string candidateItemId)
                {
                    var db = InventorySystem.ItemDatabase.Instance;
                    if (db == null || rs == null) return;
                    var cdata = db.GetItem(candidateItemId);
                    if (cdata?.passiveSkills == null) return;
                    // 候補の各パッシブの (家系, Lv) を抽出
                    var candidateFamilyLv = new Dictionary<string, int>();
                    foreach (var ps in cdata.passiveSkills)
                    {
                        if (string.IsNullOrEmpty(ps.internalName)) continue;
                        var (fam, lv) = InventorySystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ps.internalName);
                        if (lv > 0 && (!candidateFamilyLv.TryGetValue(fam, out int prev) || lv > prev))
                            candidateFamilyLv[fam] = lv;
                    }
                    if (candidateFamilyLv.Count == 0) return;
                    // 所持品 (装備武器・装備ダイス以外) を見て、 下位 Lv を持つアイテムを売却対象に
                    int safety = 0;
                    while (safety++ < 8)
                    {
                        int targetIdx = -1;
                        for (int j = 0; j < rs.ownedPassiveItems.Count; j++)
                        {
                            string ownedId = rs.ownedPassiveItems[j];
                            if (string.IsNullOrEmpty(ownedId)) continue;
                            if (ownedId == rs.equippedWeaponId) continue; // 装備武器は除外
                            if (ownedId == rs.equippedDiceId) continue;
                            var odata = db.GetItem(ownedId);
                            if (odata?.passiveSkills == null) continue;
                            bool isLowerTier = false;
                            foreach (var ops in odata.passiveSkills)
                            {
                                if (string.IsNullOrEmpty(ops.internalName)) continue;
                                var (ofam, olv) = InventorySystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ops.internalName);
                                if (olv > 0 && candidateFamilyLv.TryGetValue(ofam, out int candLv) && candLv > olv)
                                {
                                    isLowerTier = true; break;
                                }
                            }
                            if (!isLowerTier) continue;
                            // 2026-06-23c: 由来制限撤廃 ── TrySellFromBot が「ショップ滞在中なら由来問わず売却可」 を統括
                            targetIdx = j;
                            break;
                        }
                        if (targetIdx < 0) break;
                        string sellId = rs.ownedPassiveItems[targetIdx];
                        int coinsBefore = rs.coins;
                        if (AutoTest.BotSelling.TrySellFromBot(rs, targetIdx))
                            UnityEngine.Debug.Log($"[Phase C] 上位購入前に下位 {sellId} を売却 (+{rs.coins - coinsBefore}G)");
                        else break;
                    }
                }

                // --- [アブレーション] ショップの購入判断を潰す ---
                //   **買う/買わないの選別だけを捨てる。** 買えるものから一様に選んで金が尽きるまで買う。
                //   売却・優先度・戦力ギャップの判断が全部飛ぶので、 落ち幅が
                //   「ショップで何を買うか」の技量そのものになる。
                if (ablateShop)
                {
                    var cand = new List<int>();
                    for (int guard = 0; guard < 40; guard++)
                    {
                        cand.Clear();
                        for (int i = 0; i < inv.slots.Count; i++)
                        {
                            var s = inv.slots[i];
                            if (s != null && !s.sold && s.price <= run.coins) cand.Add(i);
                        }
                        if (cand.Count == 0) break;
                        int pick = cand[AblationRng.Next(0, cand.Count)];
                        // 買えない枠が 1 つだけ残ったら打ち切る (無限ループ防止)。
                        if (!Buy(pick) && cand.Count == 1) break;
                    }
                    gm.ExitShop();
                    return;
                }

                bool BuyKind(ShopSlotKind k)
                {
                    // 武器/ダイスは **装備に至るものだけ**。 劣る個体を買っても
                    //   Loadout.TryAutoEquip が装備しないので、 ゴールドが消えるだけ (2026-08-10)。
                    // 出目パーツは「増設」なので更新判定 (WouldUpgrade) の対象外。
                    //   旧ダイスは面が同じで常に false → 1 個も買われなかった。
                    bool gearOnly = (k == ShopSlotKind.Weapon);
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold
                            && inv.slots[i].kind == k
                            && (!gearOnly || GameLoop.Loadout.WouldUpgrade(run, inv.slots[i].itemId))
                            && Buy(i)) return true;
                    return false;
                }

                /// <summary>手持ちの**回復薬**の本数。 シールド薬・攻撃薬・希望薬は数えない。</summary>
                int HealCount()
                {
                    if (run.ownedConsumables == null) return 0;
                    int n = 0;
                    foreach (var id in run.ownedConsumables)
                        if (!string.IsNullOrEmpty(id)
                            && GameLoop.ItemIds.ConsFamilyOf(id) == GameLoop.ItemIds.ConsHealFamily) n++;
                    return n;
                }

                /// <summary>回復薬を 1 本買う。 買えるうち **最上位 Tier** を選ぶ。
                /// 回復は毎回 1 枠固定で並ぶ (ItemIds.ConsumableFixedFamily) ので、
                /// 金さえあれば必ず取れる。</summary>
                bool BuyHeal()
                {
                    int bestIdx = -1, bestTier = 0;
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold || s.kind != ShopSlotKind.Consumable) continue;
                        if (string.IsNullOrEmpty(s.itemId)
                            || GameLoop.ItemIds.ConsFamilyOf(s.itemId) != GameLoop.ItemIds.ConsHealFamily) continue;
                        if (s.price > run.coins) continue;
                        int t = GameLoop.ItemIds.ConsTierOf(s.itemId);
                        if (t > bestTier) { bestTier = t; bestIdx = i; }
                    }
                    return bestIdx >= 0 && Buy(bestIdx);
                }

                /// <summary>**回復薬を 1 本も持っていないなら、 何より先に確保する (2026-08-16)。**
                ///
                /// <para>旧実装は「消耗品を 1 つも持っていないなら」だったが、 消耗品は 4 系統
                /// (回復/シールド/攻撃強化/希望) あるので、 **シールド薬 1 本を持っているだけで
                /// この保険が外れていた**。 実測 (batch_20260816_145127): p4 で死亡した 199 ランのうち
                /// 回復薬 0 本が 90.5% ある一方、 消耗品の所持数は平均 0.86 ＝
                /// 「回復以外は持っているのに回復は無い」状態が常態化していた。
                /// 判定を回復系統に限定する。</para></summary>
                bool EnsureHealStock()
                {
                    if (HealCount() > 0) return false;
                    if (BuyHeal()) return true;
                    // 回復が買えない (金/売切) なら、 保険として他系統でも確保しておく。
                    return BuyKind(ShopSlotKind.Consumable);
                }

                /// <summary>手持ちの**防御系** (回復 + シールド) の本数。</summary>
                int DefensiveCount()
                {
                    if (run.ownedConsumables == null) return 0;
                    int n = 0;
                    foreach (var id in run.ownedConsumables)
                        if (!string.IsNullOrEmpty(id)
                            && (GameLoop.ItemIds.ConsFamilyOf(id) == GameLoop.ItemIds.ConsHealFamily
                             || GameLoop.ItemIds.ConsFamilyOf(id) == GameLoop.ItemIds.ConsShieldFamily)) n++;
                    return n;
                }

                /// <summary>陳列中の防御系のうち**最上位 Tier** を 1 つ買う。</summary>
                bool BuyBestDefensive()
                {
                    int bestIdx = -1, bestTier = 0;
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold || s.kind != ShopSlotKind.Consumable) continue;
                        if (string.IsNullOrEmpty(s.itemId)) continue;
                        if (GameLoop.ItemIds.ConsFamilyOf(s.itemId) != GameLoop.ItemIds.ConsHealFamily
                         && GameLoop.ItemIds.ConsFamilyOf(s.itemId) != GameLoop.ItemIds.ConsShieldFamily) continue;
                        if (s.price > run.coins) continue;
                        int t = GameLoop.ItemIds.ConsTierOf(s.itemId);
                        if (t > bestTier) { bestTier = t; bestIdx = i; }
                    }
                    return bestIdx >= 0 && Buy(bestIdx);
                }

                /// <summary>**回復・シールドを上限まで積む。 在庫に無ければ再入荷させてでも探す。**
                ///
                /// <para>実測 (batch_20260817_042337): 消耗品は 1 ラン 14.7 回買われており、
                /// 金も在庫も制約になっていない。
                /// それでも p4 死亡時の回復所持 0 本が 69% なのは、
                /// **道中で使い切ってボス前に手元が薄くなる**ため。 買う量ではなく
                /// 「ボスに入る時点で満タンか」を保証する。</para>
                ///
                /// <para>回復は毎回 1 枠固定で並ぶ (ItemIds.ConsumableFixedFamily) が、
                /// シールドは残り 2 枠を攻撃強化・希望と争う抽選なので **並ばない店がある**。
                /// リロールが要るのは主にシールド側。</para></summary>
                void StockDefensive(int cap)
                {
                    int guard = 24;
                    while (DefensiveCount() < cap && guard-- > 0)
                    {
                        if (BuyBestDefensive()) continue;
                        // 陳列に無い → 再入荷させて探す。 回した後に 1 つ買える額は残す
                        //   (最安の消費は basePrice 4 なので、 倍率込みでも 8 あれば足りる)。
                        int rp = inv.CurrentRerollPrice;
                        if (rp <= 0 || run.coins < rp + 8) break;
                        if (!RuleSaysReroll(rp)) break;   // 停止則 on のときだけ効く
                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int before = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        gm.ShopReroll();
                        if (run.coins >= before) break;
                        _cur.shopRerolls++;
                        _cur.shopRerollCoins += before - run.coins;
                        NoteReroll(5, before - run.coins);
                    }
                }

                // ============================================================
                // フェーズ0: 値下げ交渉(=強盗) 判定  ── **購入より先に置く**
                //
                //   旧版はフェーズ2.5 (購入の後) に在って、 3 つの意味で損をしていた:
                //     ① 棚の未売却分は強盗で**タダで手に入る**のに、 先に金を払って買っていた。
                //     ② `StockDepletedByPurchase()` が「1 枠でも sold なら true」 なので、
                //        ①で必ず何か買っている以上ほぼ常に真 → 毎回リロールを 3 回転ぶん金を捨てていた。
                //     ③ 強盗は `return` で抜けるため、 フェーズ3 の StockDefensive /
                //        EnsureHealStock が走らず、 **回復薬もシールド薬も積まずに**先へ進んでいた。
                //   撃つと決めたなら買わない・回さない。 撃たないと決めたなら以降は普通の店。
                //
                //   タイミングは「ランで最後のショップ」に限定する。 旧版の `currentFloor >= 6` は
                //   条件を満たした**最初の**店で撃つ貪欲規則で、 各フロアの確定ショップと
                //   7 層のボス前確定ショップ (MapGenerator: Shop→Rest→Boss) を捨てていた。
                // ============================================================
                //   **2026-09-13: 6 層以降で決め打ち。** 規則は 1 ラン 1 回のまま。
                //
                //     旧「ラン最後の店」は最終層へ到達したランでしか撃てず、 発動 11%。
                //     しかも最後の店で撃つと以降に店が無く、 **手持ちのゴールドが
                //     使い道を失ったまま消える**。 6 層で撃てば 7 層の店が残る。
                //
                //     <b>条件を全部外したら壊れた。</b> 階層ゲートが 1 つも無いと
                //     `BaseStartingHP = 75` で開幕から HP 条件を満たし、
                //     **最初に出会った店で撃つ** ── 実測の分布は
                //     F1=44.8% / F2=30.2% / F3=23.8% で 98.8% が 1〜3 層、 6 層以降は 0%。
                //     HP1840・被ダメ50%軽減のエリートに 1 層の装備で挑んで勝率 23.4%、
                //     平均到達段 2.97 (Balanced 7.70) まで落ちた。
                //
                //     層は「強さ」の代理でしかないが、 代理として機能する。
                //     **実行済みフラグは必ず見ること** ── 外すと 2 軒目以降、
                //     規則に弾かれるのに return でショップ処理ごと飛ばしてしまう。
                bool plannedRobbery =
                    MetaProgression.MetaBuffApplicator.IsShopRobberyUnlocked()
                    && !run.shopRobberyDone
                    && (robberyFinalShopOnly ? IsFinalShopOfRun(run) : run.currentFloor >= 6)
                    && run.playerMaxHP >= 50     // 最大HP下限 固定 (序盤の貧弱を除外)
                    && run.playerHP >= run.playerMaxHP * AutoTest.PolicyParameters.Current.robberyMinHpRatio;

                // **撃つと決めても、 先に買い物を済ませる (2026-09-13)。**
                //   旧実装はここで即 ShopRobbery して return していたため、
                //   **この店で 1 つも買わずにゴールドを抱えたまま次層へ行き**、
                //   価格 ×2 の罰でその金の価値が半減していた。 6 層で撃つと
                //   7 層がラン最後の買い物機会なので、 そこが一番痛い。
                //
                //   正しい順序は 「買い占める → リロール → 奪う」。
                //   **リロールは割増の対象外** (ShopInventory.CurrentRerollPrice は
                //   priceMultiplier を参照しない) なので、 割増前のレートで
                //   ゴールドを「棚の質」へ変換できる。 その 1 回ぶんだけ取り置く。
                _shopReserve = plannedRobbery ? inv.CurrentRerollPrice : 0;
                // [計装 2026-09-18] 7 層の店に入った時点の所持金 (ランで最初の 1 回だけ)。
                if (run.currentFloor >= 7 && _floor7ShopEntryCoins < 0) _floor7ShopEntryCoins = run.coins;
                // [廃止 2026-09-14] 〈貪欲の儀〉のためのゴールド取り置き。
                //   門リワークでゴールドの要求そのものが無くなった (代償は 最大HP / 遺物 / 希望)。

                // ============================================================
                // フェーズ1: 在庫の S/A 級を先取り（買えるだけ買う）
                //   2026-06-22: 買えない S/A があり、 廃棄候補 (Score=0 かつショップ由来在庫あり) を所持しているなら
                //               TrySell で換金してから購入を試みる。
                //   2026-06-23: Power 帯認識 (Weak/Early は C+ 購入、 Late/Apex は A+ のみ)。
                // ============================================================
                // 2026-06-23c: Power 帯は購入で変動するため、 毎回再計算 (旧版は 1 ショップ訪問で 1 度のみ算出し帯遷移を取り逃していた)。
                int CurrentPowerBand()
                    => AutoTest.InventoryPower.GetPowerBandRank(AutoTest.InventoryPower.Compute(run));
                int CurrentDynamicMinScore()
                {
                    int b = CurrentPowerBand();
                    return b <= 1 ? 1 : (b <= 2 ? 2 : 3);
                }
                // 連続値版の足切り (2026-08-17b: Tier 枠廃止に伴い準パワーの絶対しきい値へ)。
                //   Weak/Early は中立まで、 Mid は余裕時帯以上、 Late/Apex は強推以上。
                //   整数版 CurrentDynamicMinScore と同じ段に対応させてある。
                float CurrentDynamicMinPower()
                {
                    var cuts = AutoTest.LearnedPriorityProvider.PowerCuts;
                    int b = CurrentPowerBand();
                    return b <= 1 ? cuts[3] : (b <= 2 ? cuts[2] : cuts[1]);
                }

                bool HasUnaffordablePriority()
                {
                    int dyn = CurrentDynamicMinScore();
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        // 希望で届くなら「買えない」ではないので、 売却して工面する必要が無い。
                        if (s.price <= GameLoop.HopePayment.PolicyAffordable(run)) continue;
                        if (string.IsNullOrEmpty(s.itemId)) continue;
                        // Score 細分化 (2026-06-22): B+ (>=2) を S/A/B 帯として扱う
                        // 特売補正込み: 40% 割引以上は Score +1、 60% 以上は +2
                        // 2026-06-23: Power 帯認識: Weak/Early は C+ で十分価値あり、 Mid 以上は B+
                        int sc = AutoTest.LearnedPriorityProvider.Score(s.itemId) + PersonaBonus(s.itemId);
                        int saleBonus = s.discountPct >= 60 ? 2 : (s.discountPct >= 40 ? 1 : 0);
                        if (sc + saleBonus >= dyn) return true;
                    }
                    return false;
                }
                bool SellOneScore0()
                {
                    // (商人の符牒 売却阻止フックは 2026-07-18 アイテム削除に伴い除去)
                    for (int i = 0; i < run.ownedPassiveItems.Count; i++)
                    {
                        string id = run.ownedPassiveItems[i];
                        if (string.IsNullOrEmpty(id)) continue;
                        if (id == run.equippedWeaponId || id == run.equippedDiceId) continue;
                        if (AutoTest.LearnedPriorityProvider.Score(id) + PersonaBonus(id) > 0) continue;
                        // 2026-06-23c: 由来制限撤廃 (TrySellFromBot 内で符牒/ショップ滞在を統括判定)
                        if (AutoTest.BotSelling.TrySellFromBot(run, i)) return true;
                    }
                    return false;
                }
                int sellGuard = 0;
                while (HasUnaffordablePriority() && SellOneScore0() && sellGuard++ < 16) { }

                // 2026-06-23: Power 帯認識ショップ判定 (dynamicMinScore は上で算出済み)
                //   Weak(<10)/Early(<25): C 級 (Score 1) も購入対象、 minScore 緩和
                //   Mid(25-49): 標準 (B+)
                //   Late/Apex(>=50): A+ のみ (ゴミ買い禁止、 G 温存)
                // 2026-08-17b: **Tier 枠を廃し、準パワー (連続値) の降順で上から買う**。
                //   旧版は整数 Score (S=4..C=1) を主軸に、 同値のときだけ連続値で割っていた。
                //   段が粗いぶん「同点」が頻発し、 実質 ΔPower/G が決めていた場面が多い。
                //   枠が無くなった以上、 主軸そのものを連続値にするのが素直。
                //   <paramref name="minPower"/> は caller が指定する下限 (Power 帯の下限と max を取る)。
                // 停止則が「1 回のリロールで実際にどれだけ買えたか」を読むための積算。
                float passPowerBought = 0f;

                /// 1 枠の購入意欲 (準パワー + 補正)。 買えない・対象外なら false。
                /// **BuyPriorityPass と停止則の両方がここを通る** ── 同じ判断を 2 箇所に書くと
                /// 片方だけ直してずれる (2026-08-10 WouldUpgrade の教訓)。
                bool TrySlotPower(int i, out float pw)
                {
                    pw = 0f;
                    var s = inv.slots[i];
                    if (s == null || s.sold) return false;
                    // 燈火 r10 (希望払い) を織り込んだ支払い可能額。 規則より保守的な PolicyFloor。
                    if (s.price > GameLoop.HopePayment.PolicyAffordable(run) - _shopReserve) return false;
                    // **武器は装備判定を通す** (2026-08-10)。 主経路にゲートが無く素通りしていた。
                    if (s.kind == ShopSlotKind.Weapon && !GameLoop.Loadout.WouldUpgrade(run, s.itemId)) return false;
                    float step = AutoTest.LearnedPriorityProvider.StepPerTier;
                    if (s.kind == ShopSlotKind.FacePart
                        && !AutoTest.LearnedPriorityProvider.HasLearned(s.itemId))
                    {
                        int raw = AutoTest.InventoryPower.FacePartPower(run, s.facePart);
                        if (raw <= 0) return false;
                        pw = AutoTest.InventoryPower.BootstrapPowerScore(raw);
                    }
                    else
                    {
                        pw = AutoTest.LearnedPriorityProvider.BuyScore(s.itemId)
                           + PersonaBonus(s.itemId) * step
                           + AutoTest.BuildSynergy.ScoreAdjust(s.itemId, run);
                    }
                    // 特売補正。 **60/40 は割引段 {15,30,50} に対する意図的な線引き**。
                    //   50/30 は全条件で悪化した (2026-09-17・安物買いになる)。 下げないこと。
                    pw += (s.discountPct >= SaleBonusHiPct ? 2
                         : (s.discountPct >= SaleBonusLoPct ? 1 : 0)) * step;
                    return true;
                }

                /// 今の棚に残っている最良品の準パワー (0 下限)。 リロールすると未売却枠は
                /// 張り替わるので、 **回すことはこれを捨てること**でもある。
                /// 停止則の判定本体。 <b>停止則が off なら常に true</b> (旧規則の経路は触らない)。
                /// フェーズ2 の置き換えだけでなく、 最終店の消耗品・防御ストック・Super 全賭け
                /// も同じ判定を通す ── 1 経路だけ賢くしても、 浮いた金を別の経路が
                /// 1 回 47G で燃やす (2026-09-18 実測)。
                bool RuleSaysReroll(int price)
                {
                    if (!RerollStopRule) return true;
                    float expected = (RerollShelfValue * RerollPriorWeight + _rerollYieldSum)
                                   / (RerollPriorWeight + _rerollYieldN);
                    float gain = expected - BestRemainingPower();
                    float cost = price * GoldBandRate;
                    NoteRerollDecision(gain, cost);
                    return gain > cost;
                }

                /// [計装 2026-09-18] <b>リロールが捨てる棚</b>を数える。 各 ShopReroll の直前に呼ぶ。
                ///
                /// <para>フェーズ1/2 は準パワー ≥ PowerCuts[3] の品しか買わないが、 その後の
                /// フェーズ3 (通常購入) は所持金がゲートを超えていれば<b>スコアに関係なく</b>
                /// パッシブを全部買う。 リロールは未売却枠を張り替えるので、
                /// <b>フェーズ3 が買うはずだった品を先に捨てている</b>疑いがある。 金が余っていても
                /// 3 回目のリロールが損になる (上限 2 < 3 < 無制限) 理由の候補。</para>
                ///
                /// <para>TryReroll が断る条件 (上限・金不足) は先に見て、 捨てなかった回を数えない。</para>
                /// [A/B 2026-09-18] <b>リロールの前に、 フェーズ3 が買う品を買い切る</b>。
                /// 計装で「リロール 1 回が フェーズ3 の購入対象を約 5 品 (準パワー平均 0.065 band ≒ 売値相当)
                /// 捨てている」と出た。 フェーズ1/2 の下限 (進行度で 0.10〜0.20 band) と
                /// フェーズ3 (所持金がゲートを超えればパッシブを全部買う) の基準が 2 段になっているため。
                /// 先に買い切れば、 リロールが張り替えるのは本当に要らない枠だけになる。
                /// 基準はフェーズ3 と同じ ── 最終店はゲート無し、 それ以外はゲート付き。
                void BuyPhase3Items()
                {
                    float gate = RunPowerBudget.GateScale(PowerGap());
                    bool gk = run.OwnsPassive(GameLoop.ItemIds.GoldenKingSword);
                    bool lastShopForP3 = run.currentFloor >= run.normalClearFloor;   // フェーズ3 の lastShop と同じ式
                    int passiveGate = lastShopForP3 ? 0 : Mathf.RoundToInt((gk ? 15 : 25) * gate);
                    int weaponGate  = lastShopForP3 ? 0 : Mathf.RoundToInt((gk ? 25 : 40) * gate);
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        if (s.kind == ShopSlotKind.Weapon && run.coins > weaponGate
                            && GameLoop.Loadout.WouldUpgrade(run, s.itemId)) Buy(i);
                        else if (s.kind == ShopSlotKind.FacePart && run.coins > s.price) Buy(i);
                        else if (s.kind == ShopSlotKind.Passive && run.coins > passiveGate) Buy(i);
                    }
                }

                void NoteShelfDiscard()
                {
                    int price = inv.CurrentRerollPrice;
                    int cap = InventorySystem.Shop.ShopManager.RerollHardCap;
                    if (cap >= 0 && inv.rerollCount >= cap) return;
                    if (run.coins < price) return;
                    int after = run.coins - price;
                    float gate = RunPowerBudget.GateScale(PowerGap());
                    int passiveGate = Mathf.RoundToInt((run.OwnsPassive(GameLoop.ItemIds.GoldenKingSword) ? 15 : 25) * gate);
                    int weaponGate  = Mathf.RoundToInt((run.OwnsPassive(GameLoop.ItemIds.GoldenKingSword) ? 25 : 40) * gate);
                    DiscardRolls++;
                    int nth = Mathf.Min(3, inv.rerollCount);   // 0=1回目 … 3=4回目以降
                    DiscardRollsByNth[nth]++;
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        bool p3 =
                            (s.kind == ShopSlotKind.Passive  && after > passiveGate) ||
                            (s.kind == ShopSlotKind.FacePart && after > s.price) ||
                            (s.kind == ShopSlotKind.Weapon   && after > weaponGate
                                && GameLoop.Loadout.WouldUpgrade(run, s.itemId));
                        if (!p3) continue;
                        DiscardItemsByNth[nth]++;
                        if (s.kind == ShopSlotKind.Passive) DiscardPassive++;
                        else if (s.kind == ShopSlotKind.FacePart) DiscardPart++;
                        else DiscardWeapon++;
                        if (TrySlotPower(i, out float pw))
                        {
                            DiscardPower += pw;
                            if (pw < AutoTest.LearnedPriorityProvider.PowerCuts[3]) DiscardJunk++;
                        }
                    }
                }

                float BestRemainingPower()
                {
                    float best = 0f;   // 純益で比べる (棚替え実績と同じ物差し)
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (TrySlotPower(i, out float p))
                        {
                            float net = p - inv.slots[i].price * GoldBandRate;
                            if (net > best) best = net;
                        }
                    return best;
                }

                bool BuyPriorityPass(float minPower)
                {
                    // 動的閾値を反映。 2026-06-23c: 毎回再計算 ── 同ショップ訪問内で帯を跨いだら即時反映
                    float effMinPower = Mathf.Max(minPower, CurrentDynamicMinPower());
                    // [アブレーション] **足切りは残し、順位付けだけ潰す。**
                    //   「買ってよいものを知っている」と「その中でどれが最良かを知っている」を分ける。
                    var ablCand = ablateShopScore ? new List<int>() : null;
                    int bestIdx = -1; float bestPower = float.MinValue; float bestEff = float.MinValue;
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        // 準パワー・支払可否・武器の装備判定・特売補正は TrySlotPower に一本化。
                        //   経緯 (燈火 r10 の希望払い / 武器ゲートの取りこぼし / 60/40 の線引き) は
                        //   git log を参照。
                        if (!TrySlotPower(i, out float pw)) continue;
                        if (pw < effMinPower) continue;
                        ablCand?.Add(i);   // 足切りを通った候補 (アブレーション時のみ収集)
                        // ΔPower/G コスパ (slot.price は割引後の価格なので特売/upgrade が自然に有利化)
                        int delta = AutoTest.InventoryPower.SimulateAddItemDelta(run, s.itemId);
                        float eff = AutoTest.InventoryPower.CostEfficiency(delta, s.price);
                        // 足切りを通った候補は探索の母集団になる (下のホールドアウトで使う)
                        if (exploreHoldoutRate > 0f) _exploreCandidates.Add(i);
                        // 準パワー主軸 → ΔPower/G tiebreak
                        bool better = pw > bestPower || (pw == bestPower && eff > bestEff);
                        if (better) { bestPower = pw; bestEff = eff; bestIdx = i; }
                    }
                    // [アブレーション] 足切りを通った候補から一様抽選へ差し替える。
                    if (ablCand != null && ablCand.Count > 0)
                        bestIdx = ablCand[AblationRng.Next(0, ablCand.Count)];
                    if (bestIdx < 0) { _exploreCandidates.Clear(); return false; }

                    // **ランダム化ホールドアウト。** 一定割合の決定で、 足切りを通った候補から
                    //   **一様に**選ぶ。 その候補集合の中では取得/未取得が期待値で等質になるので、
                    //   ここで採ったデータだけを使えば lift が不偏になる。
                    //   乱数は GameRng ではなく専用ストリーム ── 消費列が変わると
                    //   シード固定のペア比較が壊れる (SuperCombatAI のロールアウトと同じ方針)。
                    if (_isExploreRun && exploreHoldoutRate > 0f && _exploreCandidates.Count > 1
                        && _exploreRng.NextDouble() < exploreHoldoutRate)
                    {
                        // **層化抽選 (2026-09-04)。** 旧: 候補から一様抽選。
                        //   一様だと選ばれる品の rarity 構成が**提示重みをそのまま保存**するので
                        //   (BRONZE 45 / SILVER 32 / GOLD 18 / LEGENDARY 5)、 稀少帯のサンプルが
                        //   永久に薄いままだった ── 実測 10,000 ラン で LEGENDARY は 1 品 2.8 件、
                        //   両群 30 以上に届いたのは 104 品中 21 品 (全部 BRONZE/SILVER) だけ。
                        //   **ラン数では解けない**: 帯間の比は試行回数に依存しないため。
                        //
                        //   提示重みの**逆数**で重み付けすると各帯の期待選択率が均等化し、
                        //   LEGENDARY のサンプルが約 4 倍になる (理論 5 倍・在庫天井で 4 倍
                        //   ── パッシブ 5 枠中に LEGENDARY が 1 つ以上並ぶ確率が 22.6% なので)。
                        //
                        //   **ゲーム側は一切変えない。** 提示も価格も購入の主経路も製品条件のまま、
                        //   変わるのは「探索が発火したとき候補のどれを選ぶか」だけ。 選択確率が
                        //   アイテムの効果と独立なので、 群内比較としての不偏性は保たれる。
                        int pick = PickStratified(inv);
                        if (_cur != null)
                        {
                            _cur.exploreDecisions++;
                            foreach (int ci in _exploreCandidates)
                            {
                                var cs = inv.slots[ci];
                                if (cs != null && !string.IsNullOrEmpty(cs.itemId))
                                    _cur.exploreOfferedEver.Add(cs.itemId);
                            }
                            var ps = inv.slots[pick];
                            if (ps != null && !string.IsNullOrEmpty(ps.itemId))
                                _cur.exploreAcquiredEver.Add(ps.itemId);
                        }
                        _exploreCandidates.Clear();
                        return Buy(pick);
                    }
                    _exploreCandidates.Clear();
                    int paidPrice = inv.slots[bestIdx].price;
                    bool boughtBest = Buy(bestIdx);
                    // 停止則の学習用: 買えた品の**純益** (準パワー − 代金の band 価値) を積む。
                    //   代金を引かないと「高い品を買えた棚替え」ほど得に見えて回し過ぎる。
                    if (boughtBest) passPowerBought += bestPower - paidPrice * GoldBandRate;
                    return boughtBest;
                }
                // 余金が許す限り 余裕時帯以上を全て取得
                int safety = 0;
                while (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[2]) && safety++ < 30) { }

                // ============================================================
                // フェーズ2: リロール判定
                //   トリガ = (S級未所持 かつ 在庫にS級なし) OR
                //            (5層以降の最終ショップで A級ゼロ かつ 在庫にA級なし)
                //   コスト ≤ run.coins × 0.30 かつ「購入予算 6G 以上は残す」
                // ============================================================
                bool HasSInInventory()
                {
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        if (AutoTest.LearnedPriorityProvider.IsSRank(s.itemId)) return true;
                    }
                    return false;
                }
                bool HasAInInventory()
                {
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        if (AutoTest.LearnedPriorityProvider.IsARank(s.itemId)) return true;
                    }
                    return false;
                }
                bool OwnsAnyS()
                {
                    if (run.ownedPassiveItems != null)
                        foreach (var id in run.ownedPassiveItems)
                            if (AutoTest.LearnedPriorityProvider.IsSRank(id)) return true;
                    if (run.ownedConsumables != null)
                        foreach (var id in run.ownedConsumables)
                            if (AutoTest.LearnedPriorityProvider.IsSRank(id)) return true;
                    return false;
                }
                bool OwnsAnyA()
                {
                    if (run.ownedPassiveItems != null)
                        foreach (var id in run.ownedPassiveItems)
                            if (AutoTest.LearnedPriorityProvider.IsARank(id)) return true;
                    return false;
                }

                bool lastShop = run.currentFloor >= run.normalClearFloor;
                var pol = AutoTest.PolicyParameters.Current;

                // ============================================================
                // リロール停止則 (2026-09-17) ── フェーズ2 と 2b を 1 本に畳む
                //
                //   旧: 「S 級を 1 個も持っていない」(品質) と「所持金 ≥ 価格 + 30G」(余剰) の
                //   2 本立て。 実測で S は最初の店でほぼ埋まり品質側は 1.4 回/ラン で停止、
                //   **223G の 82% を評価の入っていない水位ルールが使っていた**。
                //   しかも安い 1〜2 回目を判断のある側が、 高い 3 回目以降を無判断の側が使う
                //   ── 判断の質が価格の逆順に並んでいた。
                //
                //   新: 1 回ごとに **回す価値 > 払う額の価値** かを比べるだけ。
                //     回す価値 = E[1 回の棚替えで買える準パワー] − 今の棚に残る最良品
                //     払う額   = price × GoldBandRate  (ITT 実測の 1G の band 価値)
                //   価格が 5→10→20→30 と上がるので、 同じ期待値でもいずれ必ず止まる。
                //
                //   **E[1 回の棚替え] はラン内の実績から更新する。** 事前値 RerollShelfValue を
                //   擬似観測 RerollPriorWeight 件ぶんとして持ち、 回すたびに実際に買えた
                //   準パワーの和で置き換えていく。 所持が埋まって新顔が減る終盤は自然に下がる。
                //   ラン内の実績だけを使うので runIdx の純関数のまま (決定性を壊さない)。
                // ============================================================
                if (RerollStopRule)
                {
                    int exitWhy = 3;
                    int ruleGuard = 0;
                    while (ruleGuard++ < 16)
                    {
                        // 買えるものは先に買う (旧 2b と同じ下限)。
                        int sb3 = 0;
                        while (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[3]) && sb3++ < 30) { }

                        int price = inv.CurrentRerollPrice;
                        if (price <= 0) { exitWhy = 0; break; }
                        // 回した後に 1 品も買えないなら回す意味が無い。
                        if (run.coins < price + RerollBuyFloor) { exitWhy = 1; break; }

                        if (!RuleSaysReroll(price)) { exitWhy = 4; break; }

                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int beforeR = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        bool rolled = gm.ShopReroll();
                        if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                        if (!rolled) { exitWhy = 2; break; }
                        _cur.shopRerolls++;
                        _cur.shopRerollCoins += (beforeR - run.coins);
                        NoteReroll(0, beforeR - run.coins);

                        // この棚替えで実際に買えた準パワーを実績に積む。
                        passPowerBought = 0f;
                        int sb4 = 0;
                        while (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[3]) && sb4++ < 30) { }
                        _rerollYieldSum += passPowerBought;
                        _rerollYieldN++;
                    }
                    NoteSurplusExit(exitWhy, run.coins);
                }
                else
                {
                int rerollGuard = 0;
                // 2026-06-23 Power 帯認識リロール:
                //   Weak/Early (band 0-1): リロールしない (G 温存、 まず手元の品で固める)
                //   Mid (band 2): 通常 (S 未所持なら reroll)
                //   Late/Apex (band 3-4): 積極 (リロール上限 +2、 残り G ライン緩和)
                // 2026-06-23c: 各ループで band を再評価 ── 購入で帯が上がっても新しい閾値で続行
                while (rerollGuard++ < 12)
                {
                    int band = CurrentPowerBand();
                    int rerollMaxLoops = band >= 3 ? 10 : (band <= 1 ? 3 : 8);
                    int residualG = band >= 3 ? 4 : (band <= 1 ? 10 : 6);
                    if (rerollGuard > rerollMaxLoops) break;

                    int price = inv.CurrentRerollPrice;
                    if (price > run.coins * pol.rerollCostRatio) break;
                    // 通常ショップでは購入余力を最低 residualG 残す
                    if (!lastShop && run.coins - price < residualG) break;

                    bool needS = !OwnsAnyS() && !HasSInInventory();
                    bool needA = lastShop && !OwnsAnyA() && !HasAInInventory();
                    // Weak/Early はリロールしない (まず手持ち固め優先)
                    if (band <= 1) { needS = false; needA = false; }
                    if (!needS && !needA) break;

                    if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                    int beforeR = run.coins;
                    // **戻り値で判定する。** コインの増減で見ると〈合札〉の無料リロールを
                    //   失敗と誤認する (2026-09-17)。
                    NoteShelfDiscard();   // [計装] 捨てる棚を数える
                    bool rolled = gm.ShopReroll();
                    // 取り置きを現在価格へ更新。 リロール価格は 5→10→20→30 と逓増するので、
                    //   店に入った時点の額のままだと強盗直前に足りなくなる。
                    if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                    if (!rolled) break;
                    _cur.shopRerolls++;
                    _cur.shopRerollCoins += (beforeR - run.coins);
                    NoteReroll(0, beforeR - run.coins);

                    // 直後に新在庫の S/A をかき集める
                    int s2 = 0;
                    while (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[2]) && s2++ < 30) { }
                }

                // ============================================================
                // フェーズ2b: **余剰資金の再投資** (2026-08-11)
                //   従来のリロールは「S 級を持っていない時」だけが条件だった。 S を 1 つ確保した
                //   時点で二度と回さなくなり、 **平均 57.9G を使い残したままランを終えていた**。
                //   金は持ち越せるが、 持ち越しても勝率にはならない ── 在庫が尽きたなら
                //   **リロールで商品を補給する**のが、 余剰の唯一の出口である。
                //
                //   条件は「買うものが無い」かつ「回してもなお買える額が残る」。
                //   買えるものが並んでいる限り回さないので、 購入より優先されることはない。
                // ============================================================
                {
                    int surplusGuard = 0;
                    int exitWhy = 3;   // 既定 = ガード上限に当たった
                    while (surplusGuard++ < 8)
                    {
                        int price = inv.CurrentRerollPrice;
                        if (price <= 0) { exitWhy = 0; break; }
                        // **回した後に 1 個買える額を残す。** 回すだけ回して買えないのは最悪の使い方。
                        if (run.coins < price + SurplusBuyReserve) { exitWhy = 1; break; }
                        // まだ買える品が並んでいるなら、 リロールせずそちらを買う
                        if (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[3])) continue;

                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int beforeR = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        bool rolled = gm.ShopReroll();
                        // 取り置きを現在価格へ更新。 リロール価格は 5→10→20→30 と逓増するので、
                        //   店に入った時点の額のままだと強盗直前に足りなくなる。
                        if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                        if (!rolled) { exitWhy = 2; break; }
                        _cur.shopRerolls++;
                        _cur.shopRerollCoins += (beforeR - run.coins);
                        NoteReroll(1, beforeR - run.coins);
                        int s3 = 0;
                        while (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[3]) && s3++ < 30) { }
                    }
                    // [計装 2026-09-17] **余剰ループの出口**。 残金 89.5G の内訳を切り分ける。
                    //   構造的な下限は「リロール価格(最大30) + SurplusBuyReserve(30) = 60G」だが
                    //   実測はそれより 30G 高い ＝ 水位以外の理由で止まっている分がある。
                    NoteSurplusExit(exitWhy, run.coins);
                }
                }   // !RerollStopRule

                // (旧フェーズ2.5 の強盗判定はフェーズ0 へ移動 ── 買う前に撃つ)

                // ============================================================
                // フェーズ3: 通常購入（旧ロジック）
                //   黄金卿の剣 所持時 (2026-05-31 v3 消費Gold基準に変更後):
                //   旧 = 余剰金保持 / 新 = **積極消費**で与ダメ倍率を伸ばす
                //   → 通常購入閾値を緩める (=より積極的に買い回し)
                // ============================================================
                bool hasGoldKing = run != null && run.OwnsPassive(GameLoop.ItemIds.GoldenKingSword);
                if (lastShop)
                {
                    // ボス直前。 **まず防御系を上限まで積む** ── 装備より先に生存資源を確保する。
                    StockDefensive(pol.consumableStockMax);
                    int guard = 0;
                    bool bought = true;
                    while (bought && guard++ < 40)
                    {
                        bought = false;
                        // 回復 0 本なら何よりも先に確保する (2026-08-16)。
                        if (EnsureHealStock()) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.Weapon)) { bought = true; continue; }
                        // **2026-08-17 実験 B: 出目パーツをパッシブより前に置く。**
                        //   A (パッシブ優先) では 1 ラン 0.90 個しか買われず、 提示 20 回前後に対して
                        //   ほぼ全部をパッシブが吸っていた。 パーツは 8〜17G の恒久強化で
                        //   ダイス 5 個すべてに毎ターン効くので、 費用対効果で上回る可能性がある。
                        //   **ただし旧ダイス用の高いゲート (75G) を外しただけでは足りない**ことが
                        //   実測で分かったので、 順序そのものを入れ替えて比較する。
                        if (BuyKind(ShopSlotKind.FacePart)) { bought = true; continue; }
                        // **パッシブを先に買う** (2026-08-10)。 同じ金額ならパッシブの方が強い
                        //   ことが実測で出ている (ダイスが陳列から消えるだけで +9.4pt)。
                        if (BuyKind(ShopSlotKind.Passive)) { bought = true; continue; }
                        // 業物廃止 (2026-08-10) 後は T4+ で素材の行き先が消える。
                        //   最後のショップで金を使い切るループなので、 ここを塞がないと
                        //   使い道の無い素材に金を捨て、 測定側のビルドが不当に弱くなる。
                        if (GameManager.CanSpendMaterials(run)
                            && BuyKind(ShopSlotKind.WeaponMaterial)) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.Consumable)) { bought = true; continue; }
                    }
                }
                else
                {
                    // **回復を1つも持っていないなら、 何よりも先に1つ確保する (2026-08-05)。**
                    //   高難易度の敗因の 56% は削り負けで、 その多くは「低HPで戦闘に入る」形。
                    //   回復薬は購入順の最後・余り金 (coins>4) 依存だったため、 搾取経済で価格が
                    //   上がる高難易度ほど買えなくなり、 手ぶらで進む悪循環になっていた。
                    //   1 個目だけは武器・パッシブ・素材より優先する。
                    //   2026-08-16: 判定を「消耗品 0 個」から **「回復薬 0 本」** へ (EnsureHealStock)。
                    //   2026-08-17: **回復・シールドを上限まで積む** (リロールしてでも) へ強化。
                    StockDefensive(pol.consumableStockMax);

                    // 希望が枯れかけているなら希望回復も最優先で確保する (2026-08-05)。
                    //   希望は横移動・戦闘で一方的に減り、 発狂すると秒読みでランが終わる。
                    //   回復源が cons_hope_* だけになったので、 切らすと進路の自由度を失う。
                    // 取り置き中 (門で希望を払うアーム) はここが底上げされ、
                    //   6/7 層の店で希望回復を先に確保する。
                    if (run.hope <= HopeRefillFloorEffective())
                        for (int i = 0; i < inv.slots.Count; i++)
                        {
                            var s = inv.slots[i];
                            if (s != null && !s.sold && s.kind == ShopSlotKind.Consumable
                                && GameLoop.ItemIds.ConsFamilyOf(s.itemId) == GameLoop.ItemIds.ConsHopeFamily
                                && run.coins >= s.price) { Buy(i); break; }
                        }

                    // 2026-05-31 v3: 消費Gold基準なので、 黄金卿所持時は **閾値を緩めて積極消費** (旧と逆向き)
                    // 2026-08-10 経済リスケール: **絶対値の残金閾値なので価格と同じ ×5**。
                    //   据え置くとゲートが実質無効化され、 BOT が残金を使い切ってしまう。
                    // 次のボスに対する戦力ギャップでゲートを動かす (天井専用・§RunPowerBudget)。
                    //   足りないほど金を使う。 充足なら倍率 1.0 ＝ 従来どおり。
                    float gate = RunPowerBudget.GateScale(PowerGap());
                    int weaponGate   = Mathf.RoundToInt((hasGoldKing ? 25 : 40) * gate);
                    int passiveGate  = Mathf.RoundToInt((hasGoldKing ? 15 : 25) * gate);
                    int materialGate = Mathf.RoundToInt((hasGoldKing ? 10 : 20) * gate);
                    // **装備に至らない武器/ダイスは買わない (2026-08-10)。**
                    //   旧: 所持金がゲートを超えていれば枠を無条件に全部買っていた。
                    //   Loadout.TryAutoEquip は「今より良い時だけ」装備するので、
                    //   劣るダイスを買ってもゴールドが消えるだけだった
                    //   (実測 1 ラン 8.14 個購入。 大半が死に金)。
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > weaponGate
                            && inv.slots[i].kind == ShopSlotKind.Weapon
                            && GameLoop.Loadout.WouldUpgrade(run, inv.slots[i].itemId)) Buy(i);
                    // 出目パーツは「増設」なので武器のような更新判定を通さない (面が 1 枚増えるだけ)。
                    //   恒久強化なので、 素材や消耗品より先に取る。
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold
                            && inv.slots[i].kind == ShopSlotKind.FacePart
                            && run.coins > inv.slots[i].price) Buy(i);
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > passiveGate
                            && inv.slots[i].kind == ShopSlotKind.Passive) Buy(i);
                    // T4 到達率改善: 素材買い溜め上限 2→4 (連続強化で T3+ → T4 まで一気に届く余地)
                    int needMat = GameManager.WeaponUpgradeCost(run);
                    int matBuys = 0;
                    int matBuyCap = run.currentFloor >= 4 ? 4 : 2;
                    // **強化先が無いなら買わない。** 業物廃止 (2026-08-10) で T4+ 到達後は
                    //   武器が素材を受け付けない。 needMat が int.MaxValue のとき needMat*2 は
                    //   桁溢れで負になり「偶然買わない」形になっていたが、 偶然に頼らず明示する。
                    if (GameManager.CanSpendMaterials(run))
                        while (run.weaponMaterials < needMat * 2 && run.coins > materialGate && matBuys++ < matBuyCap
                               && BuyKind(ShopSlotKind.WeaponMaterial)) { }
                    int stock = run.ownedConsumables != null ? run.ownedConsumables.Count : 0;
                    int stockCap = pol.consumableStockMax;
                    for (int i = 0; i < inv.slots.Count && stock < stockCap; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > 20   // ×5 リスケール
                            && inv.slots[i].kind == ShopSlotKind.Consumable && Buy(i)) stock++;
                }

                // ============================================================
                // **7層のショップでは所持金を残さない (2026-08-16)。**
                //   実測 (batch_20260816_145127): p4 で死亡した 199 ランの残金が平均 29.3G、
                //   回復薬の所持 0 個が 90.5%。 **買えるものがあるのに金を握ったまま
                //   最終ボスへ入っている。** 人間なら絶対に取らない判断で、 計測器としての欠陥。
                //
                //   既存の all-in ルーチン (下の IsSuperTailMode) は挑戦スコア 50 以上が条件
                //   (superHighDifficultyMinScore = 50) なので、 **0pt の基準測定では
                //   一度も起動していなかった**。 難易度に依らず 7層では常に使い切る。
                //
                //   **7層では買える枠がほぼ「パッシブ×3」と「消費×3」しかない。**
                //   ダイスと武器は Loadout.WouldUpgrade がまず通らず、 素材は強化 4 到達で
                //   CanSpendMaterials が false、 拡張は上限到達なら陳列自体が生成されない
                //   (ShopManager.Generate)。 なので「金が余る」＝「消費枠しか残っていないのに、
                //   その Tier が低い」状態を指す。
                //
                //   よって **低 Tier の消費を掴んで終わるのではなく、 回して上位 Tier を探す**。
                //   T3/T4 なら即買い、 T1/T2 は回せる限り見送る。 回せなくなった最後の周だけ
                //   Tier を問わず買う ── 残り物でも空手で入るよりは良い。
                // ============================================================
                if (run.currentFloor >= 7)
                {
                    // 現在の陳列の最安値を、 再入荷後の最安値の代理として使う。
                    int cheapest = int.MaxValue;
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && inv.slots[i].price > 0)
                            cheapest = Math.Min(cheapest, inv.slots[i].price);
                    if (cheapest == int.MaxValue) cheapest = 20;   // 空店: ×5 リスケール後の下限相当

                    // Tier 下限つきの消費購入。 minTier=0 で「Tier を問わない」。
                    bool BuyConsumableAtLeast(int minTier)
                    {
                        for (int i = 0; i < inv.slots.Count; i++)
                        {
                            var s = inv.slots[i];
                            if (s == null || s.sold || s.kind != ShopSlotKind.Consumable) continue;
                            if (GameLoop.ItemIds.ConsTierOf(s.itemId) < minTier) continue;
                            if (Buy(i)) return true;
                        }
                        return false;
                    }

                    // 7層のボス直前。 使い切る前に防御系を満たしておく。
                    StockDefensive(pol.consumableStockMax);

                    int spendGuard = 0;
                    while (spendGuard++ < 64)
                    {
                        int price = inv.CurrentRerollPrice;
                        // 回した後に最安品を 1 つ買えるか。 買えないなら、 これが最後の周。
                        bool canReroll = price > 0 && run.coins >= price + cheapest;
                        int minTier = canReroll ? HighConsumableTier : 0;

                        bool bought;
                        int buyGuard = 0;
                        do
                        {
                            bought = false;
                            // 回復 0 本なら何よりも先に確保する。 Tier 待ちより優先 ──
                            //   T1 でも「持っている」ことに意味がある (次の 1 撃で死ぬ判定で使える)。
                            if (EnsureHealStock()) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.Weapon)) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.FacePart)) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.Passive)) { bought = true; continue; }
                            if (BuyConsumableAtLeast(minTier)) { bought = true; continue; }
                            if (GameManager.CanSpendMaterials(run)
                                && BuyKind(ShopSlotKind.WeaponMaterial)) { bought = true; continue; }
                        }
                        while (bought && buyGuard++ < 64);

                        if (!canReroll) break;
                        if (!RuleSaysReroll(price)) break;   // 停止則 on のときだけ効く
                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int before = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        gm.ShopReroll();
                        // 取り置きを現在価格へ更新。 リロール価格は 5→10→20→30 と逓増するので、
                        //   店に入った時点の額のままだと強盗直前に足りなくなる。
                        if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                        // [欠陥 2026-09-17] **コインの増減で失敗を判定している。**
                        //   〈試し振りの符牒〉の無料リロールは coins が減らないので、
                        //   この店で 1 度も回していないと **初回で必ず break する**。
                        //   フェーズ2/2b は戻り値判定へ直したが (8206/8242)、 ここは残っている。
                        //   **今は直さない** ── 判断の組み直しと同じバッチで動かすと切り分けが壊れる。
                        if (run.coins >= before) break;   // 減らないなら回しても無駄
                        _cur.shopRerolls++;
                        _cur.shopRerollCoins += before - run.coins;
                        NoteReroll(2, before - run.coins);
                    }
                }

                // 最大難度Super・6層以降: 金を残して死ぬ平均点戦略を捨てる。
                // 固定ショップはボス直前なので、最大6回の再入荷へ賭け、各回で装備更新・
                // パッシブ・戦闘消耗品を買えるだけ買う。外れれば金だけ失う高分散ルーチン。
                if (IsSuperTailMode && run.currentFloor >= 6)
                {
                    int allInGuard = 0;
                    while (true)
                    {
                        bool bought;
                        int buyGuard = 0;
                        do
                        {
                            bought = false;
                            if (BuyKind(ShopSlotKind.Weapon)) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.Passive)) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.Consumable)) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.FacePart)) { bought = true; continue; }
                        }
                        while (bought && buyGuard++ < 32);

                        if (allInGuard++ >= 6) break;
                        int price = inv.CurrentRerollPrice;
                        // 再入荷後に最安品を1つ買える最低限だけ残す。価格10未満の品が多い。
                        if (price <= 0 || run.coins < price + 4) break;
                        if (!RuleSaysReroll(price)) break;   // 停止則 on のときだけ効く
                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int before = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        gm.ShopReroll();   // [経路3] Super 全賭け
                        // 取り置きを現在価格へ更新。 リロール価格は 5→10→20→30 と逓増するので、
                        //   店に入った時点の額のままだと強盗直前に足りなくなる。
                        if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                        if (run.coins >= before) break;
                        _cur.shopRerolls++;
                        _cur.shopRerollCoins += before - run.coins;
                        NoteReroll(3, before - run.coins);
                    }
                }

                // 買い切った後に 「リロール → 強盗」。 順序の理由は上の取り置きのコメント。
                if (plannedRobbery)
                {
                    _shopReserve = 0;
                    int rerollPrice = inv.CurrentRerollPrice;
                    if (rerollPrice > 0 && run.coins >= rerollPrice)
                    {
                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int beforeRoll = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        gm.ShopReroll();
                        // 取り置きを現在価格へ更新。 リロール価格は 5→10→20→30 と逓増するので、
                        //   店に入った時点の額のままだと強盗直前に足りなくなる。
                        if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                        if (run.coins < beforeRoll)
                        {
                            _cur.shopRerolls++;
                            _cur.shopRerollCoins += beforeRoll - run.coins;
                            NoteReroll(4, beforeRoll - run.coins);
                        }
                    }
                    Debug.Log($"[AutoRunner] 値下げ交渉 (6層以降・買い物後): "
                            + $"F{run.currentFloor} HP{run.playerHP}/{run.playerMaxHP} "
                            + $"棚{inv.slots.Count}枠 残金{run.coins}G");
                    gm.ShopRobbery();
                    return;
                }
            }
            // [計装 2026-09-17] **ラン単位**で「最後に店を出たときの残金」を上書き記録する。
            //   店ごとの平均と終了時残金を引き算すると単位が違う (1 ラン 6 店) ので、
            //   「店を出たあとに増えた分」はラン単位でしか出せない。
            _lastShopExitCoins = gm.Run != null ? gm.Run.coins : 0;
            _lastShopExitFloor = gm.Run != null ? gm.Run.currentFloor : 0;
            _lastShopExitSeen = true;
            _shopReserve = 0;
            NoteUpgradeDeclines(gm.Run);
            gm.ExitShop();
        }

        /// <summary>[計装] 店を出る時点で売れ残っている 1G アップグレード枠を、
        /// **理由別に 1 スロット 1 回だけ**数える (§ FamilyTierStats ②)。
        ///
        /// <para><b>成約率だけでは「段の設計」を判定できない。</b> 購入の足切り
        /// (<c>if (pw &lt; effMinPower) continue;</c>) は<b>価格を一切見ず</b>、
        /// コスパ (<c>ΔPower/G</c>) は準パワー同点時のタイブレークにしか使われない。
        /// つまり見送りは「より良い品を選んだ」とは限らず、
        /// <b>学習スコアが閾値に届かなかっただけ</b>の可能性が高い。 そこを分ける。</para>
        ///
        /// <para>判定に使うのは<b>最も緩い帯</b> <c>PowerCuts[3]</c> ── 実際の購入ループが
        /// 最後にそこまで緩めて回るので、 これを下回った品は<b>どの周回でも一度も候補に入っていない</b>。</para></summary>
        private void NoteUpgradeDeclines(GameLoop.RunState run)
        {
            var inv = InventorySystem.Shop.ShopManager.Instance?.Current;
            if (inv?.slots == null || run == null) return;
            float loosest = AutoTest.LearnedPriorityProvider.PowerCuts[3];
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var s = inv.slots[i];
                if (s == null || s.sold || s.upgradeStep <= 0) continue;
                float pw = AutoTest.LearnedPriorityProvider.BuyScore(s.itemId)
                         + (s.discountPct >= 60 ? 2 : (s.discountPct >= 40 ? 1 : 0));
                if (pw < loosest) AutoTest.FamilyTierStats.NoteUpgradeDeclined(FamilyTierStats.DeclineCut);
                else if (run.coins < s.price) AutoTest.FamilyTierStats.NoteUpgradeDeclined(FamilyTierStats.DeclineNoGold);
                else AutoTest.FamilyTierStats.NoteUpgradeDeclined(FamilyTierStats.DeclineOutbid);
            }
        }

        /// <summary>戦闘をターン逐次で進行。開始直後にバフ消費、各ターン前に緊急回復判定。</summary>
        private void RunCombatWithItems(GameManager gm)
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsCombatActive) return;
            var run = gm.Run;

            // バフ/シールドは「重要戦闘」のみ。雑魚相手の初手オールインは非合理なので回避。
            // 重要 = ボス / 高脅威(threat>=5) / 一撃が現HPの半分以上を奪い得る危険戦闘。
            var e0 = cm.CurrentEnemy;
            bool important = false;
            var polC = AutoTest.PolicyParameters.Current;
            if (e0 != null)
            {
                bool boss = GameLoop.BossIds.IsBoss(e0.id);
                // 敵の最大単発 = 基礎攻撃 + ロール上限 (攻撃ロール方式・旧ダイス方式の両方に対応)
                int maxHit = (e0.EffectiveBaseAttack + e0.EffectiveRollCount * e0.EffectiveRollMax)
                           * (e0.criticalNumerator > 0 ? 2 : 1);
                important = boss || e0.threat >= polC.importantThreatThreshold
                                 || maxHit * 2 >= Math.Max(1, cm.PlayerHP);
            }

            if (important)
            {
                // 2026-05-31: LEG (Lv4) は本当の窮地のみ使用 (BOT 過剰消費抑制 = LEG 評価低下対策 C案)。
                //   desperate = ボス OR HP残り33%以下 → LEG 解禁
                //   通常 important → GOLD(Lv3)以下のみ
                bool desperate = GameLoop.BossIds.IsBoss(e0?.id)
                              || (run.playerMaxHP > 0 && cm.PlayerHP * 3 <= run.playerMaxHP);
                // 2026-08-04 消費アイテム再編: 攻撃強化 (cons_dmg_*) / シールド (cons_def_*) の
                //   2 カテゴリを Tier 順に使う。 cons_atk_* は廃止 (与ダメ% へ統合)。
                //   desperate: T4 から / 通常: T2 以下 (上位を温存)。
                if (desperate)
                {
                    UseFirst(run, "天火の膏薬", "業火の膏薬", "燐の油", "鬼火の油");
                    UseFirst(run, "惜別の護符", "銀の護符", "鉄の護符", "木の護符");
                }
                else
                {
                    UseFirst(run, "燐の油", "鬼火の油");
                    UseFirst(run, "鉄の護符", "木の護符");
                }

                // 2026-06-28: 職業スターター消耗品の使い時判定
                // 不抜の聖紋 (騎士): HP 90% 以上で発動 (80% gate を最大活用)
                if (run.playerMaxHP > 0 && cm.PlayerHP * 10 >= run.playerMaxHP * 9)
                    UseFirst(run, GameLoop.ClassStarter.OathId);
                // 仕込み刃 (暗殺者): 敗北は予測不能なので強敵戦冒頭で常時貼る
                UseFirst(run, GameLoop.ClassStarter.DaggerId);
                // 瞬間研磨剤 (剣士): 1 ロール burst なので強敵戦冒頭で切る
                UseFirst(run, GameLoop.ClassStarter.PolishId);
                // 痛覚遮断剤 (狂戦士): desperate (ボス + HP 33% 以下) のときのみ
                if (desperate) UseFirst(run, GameLoop.ClassStarter.PainkillerId);
            }

            // ADR-0009/0010: 配線・リロール・役の 3 方策を注入 (パイプライン OFF 時は未使用)。
            //   素朴版は **リロールと役を一切使わない** ＝ ADR-0010 以前と同じ打ち方。
            //   最適版との差が「役システムが開けた技量帯の幅」そのものになる (Verification ①)。
            if (CombatManager.UseMutualAttackPipeline)
            {
                WiringSkill effectiveSkill = EffectiveWiringSkill;
                bool useLayer6Routine = effectiveSkill == WiringSkill.Super
                                     && IsSuperTailMode
                                     && superLayer6OptimalCombatRoutine
                                     && e0 != null
                                     && e0.id == "boss_layer6";
                // [2026-08-22] **7層でも Optimal 方策へ切り替える** (既定 OFF)。
                //   下のコメントは「7層は Super が勝つので切り替えない」と書いているが、
                //   **それは古い測定**。 役の出目依存化・役札の連戦持ち越し・パーツ有りの
                //   現行条件で測り直すと符号が反転している:
                //     両AIが7層へ到達した 379 ラン に限定して Optimalだけクリア 71 /
                //     Superだけクリア 41 (p=0.0059)。 到達時点の状態 (HP率・パッシブ数・
                //     武器Tier・希望) は Super が僅かに上なので、 選択バイアスでは説明できない。
                //   IsSuperTailMode で絞らない ── 0pt で観測した差なので難度条件を付けない。
                bool useLayer7Routine = effectiveSkill == WiringSkill.Super
                                     && superLayer7OptimalCombatRoutine
                                     && e0 != null
                                     && !string.IsNullOrEmpty(e0.id)
                                     && e0.id.StartsWith(GameLoop.BossIds.Layer7Prefix);
                bool useOptimalRoutine = useLayer6Routine || useLayer7Routine;
                if (effectiveSkill == WiringSkill.Exact)
                {
                    // 厳密版。 **役の発動は最適版と共有する** ── 役の選択まで expectimax に
                    //   入れるには状態へ usedRoles の全ビットを持たせる必要があり、
                    //   いまの S 構造体の粒度では扱えない。 ここが未達である点は
                    //   ExactCombatAI の doc に明記してある。
                    exactAI.BeginCombat(e0 != null ? e0.id : "");
                    cm.WiringPolicy = exactAI.ChooseWiringPlan;
                    cm.RerollPolicy = exactAI.ChooseReroll;
                    cm.RolePolicy   = (System.Func<List<CombatSystem.RoleKind>, CombatSystem.MutualTurnTelegraph,
                                                    HashSet<CombatSystem.RoleKind>, List<CombatSystem.RoleKind>>)MutualRolePolicy;
                }
                else if (effectiveSkill == WiringSkill.Super && !useOptimalRoutine)
                {
                    // 天井版は 3 判断を一つの価値関数で決める。 **3 つとも差し替える** ──
                    // 配線だけ先読みにして役の発動を貪欲のままにすると、 探索の前提と
                    // 実際の発動が食い違い、 選んだ配線が意味を失う。
                    cm.WiringPolicy = logWiringDiff
                        ? (System.Func<int[], CombatSystem.MutualTurnTelegraph, CombatSystem.WiringPlan>)SuperWiringWithDiff
                        : superAI.ChooseWiringPlan;
                    // [計測 2026-09-19] Super のリロール判断を軽いものへ差し替えられるようにする。
                    //   Super の所要時間の 98% がリロールの全列挙なので、 近似で強さがどれだけ落ちるかを測る。
                    //   0 = 全列挙 (既定) / 1 = Optimal のリロール方策 / 2 = 全列挙を軽く (標本・厳密範囲を絞る)
                    if (superRerollMode == 2)
                    {
                        superAI.rerollSamples = superLightRerollSamples;
                        superAI.exactRerollMaxDice = superLightExactMaxDice;
                    }
                    cm.RerollPolicy = superRerollMode == 1
                        ? (System.Func<int[], CombatSystem.MutualTurnTelegraph, int, int[]>)MutualRerollPolicy
                        : superAI.ChooseReroll;
                    cm.RolePolicy   = superAI.ChooseRoles;
                }
                else
                {
                    // 50pt・同一10000シードで、両AIが6層へ遭遇した2322件に限定すると
                    // Optimalだけ勝利425 / Superだけ勝利267。Superの未来近似は灰の再生・
                    // 周期予兆・烈炎の複合戦で局所的に劣るため、ここだけ実測上位方策へ切替える。
                    // 7層はSuperだけ完全クリア300 / Optimalだけ173なので切り替えない。
                    //   ↑ **この 7層の判断は 2026-08-22 に覆った** (上の useLayer7Routine 参照)。
                    if (useOptimalRoutine) superAI.BeginCombat(e0.id);
                    cm.WiringPolicy = MutualWiringPlanPolicy;
                    cm.RerollPolicy = effectiveSkill == WiringSkill.Naive ? null
                                    : (System.Func<int[], CombatSystem.MutualTurnTelegraph, int, int[]>)MutualRerollPolicy;
                    cm.RolePolicy   = effectiveSkill == WiringSkill.Naive ? null
                                    : (System.Func<List<CombatSystem.RoleKind>, CombatSystem.MutualTurnTelegraph,
                                                    HashSet<CombatSystem.RoleKind>, List<CombatSystem.RoleKind>>)MutualRolePolicy;
                }
            }

            int guard = 0;
            while (cm.IsCombatActive && guard++ < 250)
            {
                // シュヴァリエ戦専用: ボス形態に同期してレイピアをトグル
                AutoToggleRapierVsSaintGeorges(cm, run);

                // 緊急回復: 敵の最大ダイスダメージ(会心なら×2)で落ちそうなら回復
                var e = cm.CurrentEnemy;
                if (e != null)
                {
                    // 挑戦デバフとエスカレーションを含む共通推定を使う (2026-08-05)。
                    // 素ステータスだけだと長期戦で倍率が乗った実被弾を過小評価する。
                    // ctx を渡す ── 停滞/遺物/溜め打ちの実行時加算を含めないと 4 割過小になり、
                    //   回復を持ったまま一撃で落ちる死角ができる (EstimateMaxHit の doc 参照)。
                    int maxHit = EstimateMaxHit(e, cm.CurrentCombatTurn,
                                                InventorySystem.PassiveSkills.PassiveSkillManager.Instance?.Context);
                    // 致死見積り: 主攻撃の最大 + そのターンに確実に入る継続ダメ。
                    //   大出血は軽減不可・毎ターン確定なので必ず足す (7層 p4 で被ダメの 25〜40%)。
                    var hctx = InventorySystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
                    int chip = hctx != null
                             ? hctx.massiveBleedStacks
                               * InventorySystem.PassiveSkills.Effects.VescaRelicPool.MassiveBleedDamagePerStack
                             : 0;
                    int lethal = maxHit + chip;
                    int healTrigger = Mathf.RoundToInt(maxHit * polC.emergencyHealRatio);

                    if (cm.PlayerHP <= lethal)
                    {
                        // **死ぬ場面では温存しない。** 人間なら 100% しない判断なので BOT にもさせない。
                        //   2026-08-16 まで「T3/T4 は HP 1/4 以下まで温存」だったが、 7層後半は
                        //   1 撃が最大HP の 3 割入るため **HP 25〜40% の帯を 1 ターンで飛び越え**、
                        //   解除条件に一度も触れずに死んでいた。 実測: p4 敗死 278 件のうち 43% が
                        //   回復を所持したまま死亡し、 その内訳は T3 が 157 件・T4 が 29 件 ──
                        //   **温存ルールが大きい回復を死蔵させていた**。
                        UseBestHeal(run, cm.PlayerHP, lethal);
                    }
                    else if (cm.PlayerHP <= healTrigger)
                    {
                        // 死なないが削れている場面。 ここは従来どおり小さいものだけ使う
                        //   (大きい回復を上限で溢れさせない、 という温存の本来の意図)。
                        UseFirst(run, "回復薬", "小回復薬");
                    }
                }
                var tr = cm.ExecuteTurn();
                if (tr.isDraw) _cwDraw++;
                else if (tr.playerWon) _cwWin++;
                else { _cwLoss++; if (tr.totalDamage <= 0) _cwLossAbs++; }
            }
        }


/// <summary>6F (灰燼の王) 撃破直後のビルド情報を _cur に記録。
        /// 装備/アイテム/HP/希望/各種デバフを 1 行プレーンテキストに圧縮。
        /// 後でサマリーから「どんな装備で 6F まで来たか」をサルベージする用途。</summary>
        private void Capture6FClearSnapshot(GameManager gm)
        {
            if (_cur == null || gm?.Run == null) return;
            var run = gm.Run;
            var sb = new System.Text.StringBuilder();
            sb.Append($"HP {run.playerHP}/{run.playerMaxHP} | coins {run.coins} | mat {run.weaponMaterials} | 武器 {run.equippedWeaponId} 限界突破{run.limitBreakStage} | 希望 {run.hope}/{run.hopeCap}[{GameLoop.HopeSystem.GetTier(run)}]");
            sb.Append($"\n      武器: {(string.IsNullOrEmpty(run.equippedWeaponId) ? "(無)" : run.equippedWeaponId)} | ダイス: {(string.IsNullOrEmpty(run.equippedDiceId) ? "(武器ダイス)" : run.equippedDiceId)}");
            int pCnt = run.ownedPassiveItems?.Count ?? 0;
            string pList = pCnt > 0 ? string.Join(", ", run.ownedPassiveItems) : "(無)";
            sb.Append($"\n      パッシブ({pCnt}): {pList}");
            int cCnt = run.ownedConsumables?.Count ?? 0;
            string cList = cCnt > 0 ? string.Join(", ", run.ownedConsumables) : "(無)";
            sb.Append($"\n      消費品({cCnt}): {cList}");
            int fCnt = run.ownedFlags?.Count ?? 0;
            if (fCnt > 0) sb.Append($"\n      フラグ({fCnt}): {string.Join(", ", run.ownedFlags)}");
            if (run.timedBuffs != null && run.timedBuffs.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in run.timedBuffs) parts.Add($"{kv.Key}×{kv.Value}");
                sb.Append($"\n      時限バフ: {string.Join(", ", parts)}");
            }
            if (run.timedDebuffs != null && run.timedDebuffs.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in run.timedDebuffs) parts.Add($"{kv.Key}×{kv.Value}");
                sb.Append($"\n      時限デバフ: {string.Join(", ", parts)}");
            }
            if (run.permanentDebuffs != null && run.permanentDebuffs.Count > 0)
                sb.Append($"\n      恒久デバフ: {string.Join(", ", run.permanentDebuffs)}");
            if (run.gateFlaws != GameLoop.GateFlaw.None)
                sb.Append($"\n      門の欠陥: {run.gateFlaws}");
            sb.Append($"\n      LS使用済: {run.lastStandActive} | サン=ジョリオラ撃破: {run.defeatedSaintGeorges}");
            _cur.clear6FSnapshot = sb.ToString();
        }

        /// <summary>
        /// ADR-0009 配線ポリシー (完全情報・交換レート最大化)。
        /// tools/adr0009_sim.py の choose_wiring と同じロジックを C# 移植:
        ///   出目降順で「攻撃 a 本 / ブロック b 本 / 充電 c 本」を全列挙 (O(k^2)) し、
        ///   効用 U = w_atk × min(dmg, enemyHP) + w_def × min(block, enemyAtk)
        ///          + 撃破ボーナス − 致死ペナルティ + 充電価値 で最大化。
        /// ビルド重みはシミュレータのバランス型 (1.0/1.0/0.6) を既定にする。
        /// </summary>
        /// <summary>実体のみを返す薄い包み。 ゴースト (出目パーツ T3/T4) の列挙は未実装なので
        /// 現状は常にゴースト無し ── 入れる前と挙動が変わらないことを保証するための段。</summary>
        private CombatSystem.WiringPlan MutualWiringPlanPolicy(int[] rolls, CombatSystem.MutualTurnTelegraph tele)
            => MutualWiringPolicy(rolls, tele);

        // ============================================================
        //  [計装 2026-09-19] 先読み (Super) と 1 ターン最善 (Optimal) の配線の食い違い
        // ============================================================
        /// <summary>true のとき、 Super の配線判断ごとに<b>同じ局面で Optimal ならどう配線したか</b>も計算し、
        /// 局面の特徴と一緒に CSV へ書く。 Optimal の方策は乱数を使わないので、 呼んでもゲームは変わらない。
        /// 目的は「先読みで何が得られているか」を局面で特定すること (エリート戦の死亡率 5.2% → 3.9%)。</summary>
        public bool logWiringDiff;

        /// <summary>[計測] Super のリロール判断。 0 = 全列挙 (既定) / 1 = Optimal の方策 / 2 = 軽い全列挙。</summary>
        public int superRerollMode = 0;
        /// <summary>superRerollMode=2 のときの標本数と、 厳密に全列挙する振り直し個数の上限。</summary>
        public int superLightRerollSamples = 4;
        public int superLightExactMaxDice = 0;
        [NonSerialized] public string wiringDiffPath;
        private System.IO.StreamWriter _wiringDiffWriter;

        private CombatSystem.WiringPlan SuperWiringWithDiff(int[] rolls, CombatSystem.MutualTurnTelegraph tele)
        {
            var sp = superAI.ChooseWiringPlan(rolls, tele);
            try
            {
                var op = MutualWiringPolicy(rolls, tele);
                WriteWiringDiff(rolls, tele, sp.main, op.main);
            }
            catch (Exception ex) { Debug.LogWarning("[WiringDiff] " + ex.Message); }
            return sp;
        }

        private void WriteWiringDiff(int[] rolls, CombatSystem.MutualTurnTelegraph tele,
                                     CombatSystem.DiceTerminal[] sw, CombatSystem.DiceTerminal[] ow)
        {
            if (string.IsNullOrEmpty(wiringDiffPath) || rolls == null) return;
            if (_wiringDiffWriter == null)
            {
                _wiringDiffWriter = new System.IO.StreamWriter(wiringDiffPath, false, new System.Text.UTF8Encoding(false));
                _wiringDiffWriter.WriteLine("run,kind,enemy,turn,stage,nextThr,enemyAtk,pHp,pMax,eHp,eMax,shield,charge,diceSum,"
                    + "sAtk,sBlk,sChg,sOth,oAtk,oBlk,oChg,oOth");
            }
            var cm = CombatManager.Instance;
            var ctx = InventorySystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            var e = cm?.CurrentEnemy;
            int kind = GameLoop.BossIds.IsBoss(e?.id) ? 2
                     : MapSystem.MapManager.Instance?.CurrentNode?.EffectiveType.ToEnemyKind() == EnemyKind.Elite ? 1 : 0;
            int dsum = 0; foreach (var d in rolls) dsum += d;
            void Sums(CombatSystem.DiceTerminal[] w, out int a, out int b, out int c, out int o)
            {
                a = b = c = o = 0;
                if (w == null) return;
                for (int i = 0; i < rolls.Length && i < w.Length; i++)
                    switch (w[i])
                    {
                        case CombatSystem.DiceTerminal.Attack: a += rolls[i]; break;
                        case CombatSystem.DiceTerminal.Block:  b += rolls[i]; break;
                        case CombatSystem.DiceTerminal.Charge: c += rolls[i]; break;
                        default: o += rolls[i]; break;
                    }
            }
            Sums(sw, out int sa, out int sb, out int sc, out int so);
            Sums(ow, out int oa, out int ob, out int oc, out int oo);
            _wiringDiffWriter.WriteLine(string.Join(",", new object[] {
                _cur?.index ?? -1, kind, e?.id ?? "", tele.turn, tele.escalationStage, tele.nextThresholdTurn, tele.enemyAttackValue,
                cm?.PlayerHP ?? 0, cm?.PlayerMaxHP ?? 0, cm?.EnemyHP ?? 0, cm?.EnemyMaxHP ?? 0,
                ctx?.consShield ?? 0, ctx?.GetCharge() ?? 0, dsum, sa, sb, sc, so, oa, ob, oc, oo }));
        }

        /// <summary>バッチ終了時に閉じる (worker が呼ぶ)。</summary>
        public void CloseWiringDiff()
        {
            try { _wiringDiffWriter?.Flush(); _wiringDiffWriter?.Dispose(); } catch { }
            _wiringDiffWriter = null;
        }

        private CombatSystem.WiringPlan MutualWiringPolicy(int[] rolls, CombatSystem.MutualTurnTelegraph tele)
        {
            int k = rolls?.Length ?? 0;
            if (k == 0) return default;
            // ゴースト貪欲パス中だけ非 null。 並列探索中は null なので競合しない。
            int[] curGhost = null;
            var cm = CombatManager.Instance;
            int enemyHp = cm?.EnemyHP ?? 1;
            int myHp = cm?.PlayerHP ?? 1;
            int myMaxHp = cm?.PlayerMaxHP ?? System.Math.Max(1, myHp);
            // 麻痺毒の小瓶: このターンの攻撃力減算 (0 で床)。 予告済みなので見積もりに含める。
            int atkBase = System.Math.Max(0, (cm?.PlayerAttackPower ?? 2) - tele.playerAttackPenalty);
            int enemyAtk = tele.enemyAttackValue;

            // 出目降順ソートのインデックス (攻撃には高い目・ブロックは次・充電は残り)
            var idx = new int[k];
            for (int i = 0; i < k; i++) idx[i] = i;
            System.Array.Sort(idx, (a, b) => rolls[b].CompareTo(rolls[a]));

            // 短期決戦の視野 (2026-08-04)。 「この配分を撃破まで続けたら累積でいくら食らうか」を
            // 効用に入れるための係数。 HorizonCapTurns は effDmg=0 のときの発散止めと、
            // 「遠すぎる先は見ない」の兼用。
            const int HorizonCapTurns = 12;
            // 1 ターン引き延ばすことのコスト。 既存項の桁 (wAtk×effDmg が ~25〜100、
            // wDef×min(block,enemyAtk) が ~3〜40) に対し、 ttk が 1 増える不利がブロック 1 本の
            // 利得を上回るように置いた初期値。
            const float TimeCostPerTurn = 4.0f;

            // ペルソナで端子重みを調整 (2026-07-15): 該当ビルドが配線を意識して立ち回る
            float wAtk = 1.0f, wDef = 1.0f, wChg = 0.6f;
            switch (_currentPersona)
            {
                case BuildPersona.Charge:    wChg = 2.5f; break;                      // 充電優先
                case BuildPersona.Shield:    wDef = 1.6f; wChg = 0.9f; break;         // 守り厚く
                case BuildPersona.Berserk:   wAtk = 1.4f; wDef = 0.7f; break;         // 攻めっ気
                case BuildPersona.Bludgeon:  wAtk = 1.3f; break;                      // 火力寄せ
                case BuildPersona.Standard:  wAtk = 1.2f; break;                      // 若干攻め
                case BuildPersona.Bleed:     wAtk = 1.15f; break;                     // 攻撃回数で積む
                case BuildPersona.Poison:    wAtk = 1.1f; break;                      // DoT積みつつ攻める
                case BuildPersona.Rinkai:    wAtk = 1.35f; break;                     // メーター爆発型 = 攻撃全振り
                case BuildPersona.Crit:      wAtk = 1.15f; break;
            }
            // ボス別プレイブック (2026-07-16): 該当ボスなら端子重みを追加調整
            //   予兆Tは defBias×omenDefBoost, atkBias×omenAtkDamp を上乗せ
            //   damageCapped ボス (6F 灰塵の外殻/5F ChipCap) は攻撃玉あたりの実効ダメを wiring 探索側でクランプ
            //   2026-07-16 追加: ペルソナと Playbook の合成で二重ブレーキ/二重ゲタを緩和
            //     耐久ペルソナ (Shield/Charge) は既に defBias 高 → Playbook の omenDefBoost を弱める
            //     火力ペルソナ (Standard/Bludgeon/Crit/Berserk) は Playbook の omenAtkDamp を弱める (削り速度維持)
            // 〈虚空〉所持判定 (2026-07-29 改訂)。
            //   旧: 虚空は「攻撃 0 本で双方 0 ダメ」だったため、 攻撃 0 本の抑止から **除外**していた。
            //   新: リワークで双方 0 ダメは撤去され、「殻が剥がれる / 現在 HP 割合を刻む」に変わった。
            //       除外条項だけが残った結果、 剣ビルドは攻撃 0 本を無条件で選び放題になり、
            //       7 層で 15.3 ターン中 3.2 回しか殴らない状態を作っていた。
            //   → 除外をやめ、 **刻みの価値と殻の軽減を効用へ入れて天秤にかける**。
            var runForVoid = GameLoop.GameManager.Instance?.Run;
            bool hasVoidStance = runForVoid?.equippedWeaponId != null
                                 && (runForVoid.equippedWeaponId == "銀の長剣"
                                     || runForVoid.equippedWeaponId == "デュランダル");
            int voidStacks = InventorySystem.PassiveSkills.PassiveSkillManager.Instance?.Context?.voidStanceStacks ?? 0;

            var play = BossPlaybook.Get(cm?.CurrentEnemy?.id);
            wAtk *= play.atkBias;
            wDef *= play.defBias;
            wChg *= play.chgBias;
            // ボス側 HP 比 (Phase 判定に必要): CombatManager から取得 (敵ID・敵HP・敵MaxHP)
            float bossHpRatio = 1f;
            if (cm != null && cm.CurrentEnemy != null && cm.CurrentEnemy.maxHP > 0)
                bossHpRatio = (float)cm.EnemyHP / cm.CurrentEnemy.maxHP;
            if (play.IsOmenTurn(tele.turn, bossHpRatio))
            {
                float atkDamp = play.omenAtkDamp;
                float defBoost = play.omenDefBoost;
                switch (_currentPersona)
                {
                    case BuildPersona.Shield:
                    case BuildPersona.Charge:
                        // 既に守り厚い → 予兆Tの追加防御ゲタを緩和 (1.8 → ~1.3)
                        defBoost = 1f + (defBoost - 1f) * 0.4f;
                        break;
                    case BuildPersona.Standard:
                    case BuildPersona.Bludgeon:
                    case BuildPersona.Crit:
                    case BuildPersona.Berserk:
                        // 火力軸は削り速度が生命線 → 予兆Tの攻撃減衰を緩和 (0.7 → ~0.85)
                        atkDamp = 1f - (1f - atkDamp) * 0.5f;
                        break;
                }
                wAtk *= atkDamp;
                wDef *= defBoost;
            }
            // ===== 割り当ての列挙 (2026-08-09 / ADR-0010) =====
            //   旧実装は「出目降順で 攻撃 a 本 / ブロック b 本 / 充電 c 本」の**本数分割**しか
            //   見ていなかった。 役は「どのダイスを同じ端子へ置くか」で決まるので、
            //   本数分割では原理的に表現できない ── 2 と 2 を同じ端子に置けば〈対〉、
            //   割れば素の 2 のまま。 最適版は全 3^k を列挙する (k=5 で 243 通り)。
            var ctxRoles = InventorySystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            // 〈無銘の賽〉は端子役を作れないが手札役・配線役は生きるので、 盤面役だけ評価する。
            // Superの6層専用ハイブリッドもこの方策を通る。Naiveだけ役を使わない。
            bool useHandRoles = EffectiveWiringSkill != WiringSkill.Naive && ctxRoles != null;
            bool useTermRoles = useHandRoles && !ctxRoles.suppressTerminalRoles;

            // 端子ごとの組を役判定へ渡すためのバッファ。 243 通りを回すので毎回 new すると GC を踏む。
            var atkGroups = new int[k + 1][];
            var blkGroups = new int[k + 1][];
            for (int n = 0; n <= k; n++) { atkGroups[n] = new int[n]; blkGroups[n] = new int[n]; }
            var scratchA = new int[k];
            var scratchB = new int[k];
            // 手札役は配線に依存しないので 1 回だけ評価する。
            var handRoles = new List<CombatSystem.RoleKind>(8);
            if (useHandRoles) CombatSystem.YachtRoles.EvaluateHand(rolls, handRoles);
            var roleBuf = new List<CombatSystem.RoleKind>(16);

            // 会心倍率は「束(攻)＝会心確定」「奇(攻)＝会心率+25%」の価値見積もりに要る。
            float critMul = ctxRoles?.criticalMultiplier ?? 3f;
            var specDef = CombatSystem.SpecialTerminals.Equipped(runForVoid);

            float ScoreWithBuffers(
                CombatSystem.DiceTerminal[] w,
                int[] scoreScratchA,
                int[] scoreScratchB,
                int[][] scoreAtkGroups,
                int[][] scoreBlkGroups,
                List<CombatSystem.RoleKind> scoreRoleBuf)
            {
                int aCount = 0, bCount = 0, c = 0, aSum = 0, blockRaw = 0, maxBlockDie = 0;
                int sCount = 0, sSum = 0, chargeRaw = 0;
                for (int i = 0; i < k; i++)
                {
                    int v = rolls[i];
                    if (w[i] == CombatSystem.DiceTerminal.Attack)
                    { scoreScratchA[aCount++] = v; aSum += v; }
                    else if (w[i] == CombatSystem.DiceTerminal.Block)
                    {
                        scoreScratchB[bCount++] = v; blockRaw += v;
                        if (v > maxBlockDie) maxBlockDie = v;
                    }
                    else if (w[i] == CombatSystem.DiceTerminal.Special)
                    { sCount++; sSum += v; }
                    else { c++; chargeRaw += v; }
                }

                // --- ゴースト接続 (出目パーツ T3/T4) ---
                //   **合計値にだけ乗せ、 役判定の組 (scratchA/B) には入れない。**
                //   実行側 (CombatManager) と同じ扱いにしないと、 BOT が見積もった効用と
                //   実際の結果がずれる ── 本数系のボーナスも増やさない。
                //   curGhost は貪欲パス中だけ非 null。 並列探索中は null なので競合しない。
                if (curGhost != null)
                {
                    for (int i = 0; i < k && i < curGhost.Length; i++)
                    {
                        int g = curGhost[i];
                        if (g == CombatSystem.WiringPlan.NoGhost) continue;
                        int add = rolls[i];
                        if ((int)w[i] == g) add += GameLoop.DiceFaceParts.T4StackBonus;
                        if (g == (int)CombatSystem.DiceTerminal.Attack) aSum += add;
                        else if (g == (int)CombatSystem.DiceTerminal.Block)
                        {
                            blockRaw += add;
                            if (add > maxBlockDie) maxBlockDie = add;
                        }
                        else if (g == (int)CombatSystem.DiceTerminal.Special) sSum += add;
                        else chargeRaw += add;
                    }
                }

                // --- 特殊端子 (§6-5) の寄与を見積もる ---
                //   **ここを書かないと BOT は特殊端子へ 1 本も挿さない** ── 挿した瞬間に
                //   攻撃にもブロックにも充電にもならないので、 効用が単調に下がるため。
                float specFlat = 0f;
                if (specDef != null && sCount > 0)
                {
                    switch (specDef.kind)
                    {
                        case CombatSystem.SpecialTerminalKind.HeavyStrike:
                            aSum += Mathf.CeilToInt(sSum * CombatSystem.SpecialTerminals.HeavyStrikeMultiplier); break;
                        case CombatSystem.SpecialTerminalKind.FullGuard:
                            blockRaw += sSum * CombatSystem.SpecialTerminals.FullGuardMultiplier; break;
                        case CombatSystem.SpecialTerminalKind.Bleed:
                            specFlat += sSum * 0.8f + sSum * sCount * 0.5f; break;
                        case CombatSystem.SpecialTerminalKind.Heal:
                            specFlat += sSum * wDef; break;
                        case CombatSystem.SpecialTerminalKind.Deathwish:
                        {
                            int missing = Mathf.Max(0, myMaxHp - myHp);
                            aSum += sSum + Mathf.FloorToInt(missing * CombatSystem.SpecialTerminals.DeathwishMissingHpPct);
                            specFlat -= sSum * wDef;   // 自傷ぶんの不利
                            break;
                        }
                        case CombatSystem.SpecialTerminalKind.VitalPoint:
                        {
                            int mn = int.MaxValue;
                            for (int i = 0; i < k; i++) if (rolls[i] < mn) mn = rolls[i];
                            bool allMin = true;
                            for (int i = 0; i < k && allMin; i++)
                                if (w[i] == CombatSystem.DiceTerminal.Special && rolls[i] != mn) allMin = false;
                            if (allMin) aSum += sSum * CombatSystem.SpecialTerminals.VitalPointMultiplier;
                            break;
                        }
                        case CombatSystem.SpecialTerminalKind.Aim:
                            specFlat += Mathf.CeilToInt(sSum / 2f) / 100f * aSum * (critMul - 1f); break;
                        case CombatSystem.SpecialTerminalKind.Battery:
                            chargeRaw += sSum + sCount * CombatSystem.SpecialTerminals.BatteryPerDice;
                            c += sCount;
                            break;
                        case CombatSystem.SpecialTerminalKind.Coolant:
                        case CombatSystem.SpecialTerminalKind.Foundry:
                            // 蓄積型。 1 ターンでは返らないので「蓄積 1 あたりの期待値」で薄く評価する
                            specFlat += sSum * 0.6f; break;
                        case CombatSystem.SpecialTerminalKind.Riposte:
                            specFlat += Mathf.Min(sSum, Mathf.Max(0, enemyAtk - blockRaw)) * wAtk; break;
                    }
                }

                int dmg = atkBase + aSum;
                // damageCapped ボス: 与ダメが閾値超えたら超過分を半減した見積もりで探索 (実挙動と一致)
                int effDmg = dmg;
                if (play.damageCapped && play.damageCapThreshold > 0 && dmg > play.damageCapThreshold)
                    effDmg = play.damageCapThreshold + (dmg - play.damageCapThreshold) / 2;
                // 反射ボス: 与ダメの reflectRatio 分が自HPに返る想定でペナルティ
                float reflectPenalty = play.reflectsAttack ? effDmg * play.reflectRatio : 0f;

                // === 7層ヴェスカ〈遺物学者〉の抽選結果を織り込む (§13-4) ===
                // 予告で開示されている情報なので、 配線判断に使うのは完全情報の範囲内。
                if (aCount > 0)
                {
                    // 天与の盾: このターンの与ダメは半分。 予告済みなので「効率が落ちる
                    // ターン」として読める ── 無効ではないので押し切る選択も残る。
                    if (tele.enemyHalvesDamageThisTurn) effDmg = (effDmg + 1) / 2;
                    // 旧経路 (倍率) は互換のため残置。 現行の盾はここを通らない。
                    else if (tele.enemyDamageTakenMul > 0f && tele.enemyDamageTakenMul < 1f)
                        effDmg = (int)(effDmg * tele.enemyDamageTakenMul);
                    // 解析演算: 全ダメージを確率で回避 → 期待値で割る
                    if (tele.enemyDodgeChance > 0f)
                        effDmg = (int)(effDmg * (1f - tele.enemyDodgeChance));
                    // シールド: 与ダメを先に吸う。 吸われた分 × 反射率 が自 HP へ返る。
                    if (tele.enemyShield > 0)
                    {
                        int absorbed = System.Math.Min(tele.enemyShield, effDmg);
                        effDmg -= absorbed;
                        if (tele.enemyShieldReflectRate > 0f)
                            reflectPenalty += absorbed * tele.enemyShieldReflectRate;
                    }
                }

                int block = blockRaw;
                // 〈貫きの錐〉: 敵攻撃値 1 につきブロックを 2 削る (予告済み)。
                if (tele.playerBlockIgnored)
                    block = System.Math.Max(0, block - enemyAtk * CombatSystem.CombatManager.PierceAwlPerAttack);

                // ===== 役の見積もり (ADR-0010) =====
                //   **「切れるものは全部切る」前提**で見積もる。 実際の発動可否は
                //   MutualRolePolicy が決めるので、 両者の前提を揃えてある。
                //   ここの値は実効果の厳密な再現ではなく **順序さえ合っていればよい**近似 ──
                //   配線候補どうしを比べるための指標なので、 絶対値の較正は要らない。
                float roleFlat = 0f;
                bool roleZeroTaken = false, roleKill = false;
                if (useHandRoles)
                {
                    scoreRoleBuf.Clear();
                    for (int i = 0; i < handRoles.Count; i++) scoreRoleBuf.Add(handRoles[i]);
                    if (useTermRoles)
                    {
                        System.Array.Copy(scoreScratchA, scoreAtkGroups[aCount], aCount);
                        System.Array.Copy(scoreScratchB, scoreBlkGroups[bCount], bCount);
                        CombatSystem.YachtRoles.EvaluateTerminal(scoreAtkGroups[aCount], scoreRoleBuf);
                    }
                    int atkEnd = scoreRoleBuf.Count;
                    if (useTermRoles) CombatSystem.YachtRoles.EvaluateTerminal(scoreBlkGroups[bCount], scoreRoleBuf);
                    CombatSystem.YachtRoles.EvaluateWiring(aSum, block, enemyAtk, enemyHp, scoreRoleBuf);

                    int dAtk = 0, dBlk = 0;
                    for (int i = 0; i < scoreRoleBuf.Count; i++)
                    {
                        var rk = scoreRoleBuf[i];
                        if (ctxRoles.usedRoles.Contains(rk)) continue;   // 1 戦闘 1 回
                        // T4-C〈凶運〉: 封印された役は切れないので、 効用にも数えない。
                        if ((ctxRoles.sealedRoleMask & (1 << (int)rk)) != 0) continue;
                        // 挿入順が 手札 → 攻撃端子 → ブロック端子 → 配線 なので、
                        // 添字が atkEnd 以降の端子役だけがブロック側で成立したもの。
                        bool onAttack = !(i >= atkEnd
                            && CombatSystem.YachtRoles.ScopeOf(rk) == CombatSystem.RoleScope.Terminal);
                        RoleEstimate(rk, onAttack, aCount, bCount, aSum, block, enemyAtk, enemyHp,
                                     critMul, wChg, ref dAtk, ref dBlk, ref roleFlat,
                                     ref roleZeroTaken, ref roleKill);
                    }
                    effDmg += dAtk;
                    block += dBlk;
                }

                int taken = System.Math.Max(0, enemyAtk - block) + (int)reflectPenalty;
                if (roleZeroTaken) taken = 0;
                if (roleKill) effDmg = System.Math.Max(effDmg, enemyHp);

                // 処刑人の烙印: 攻撃終了時に HP が最大の 20% 以下なら 9999。
                if (tele.executeArmed && (myHp - taken) <= UnityEngine.Mathf.CeilToInt(myMaxHp * 0.20f))
                    taken = myHp;

                // 〈虚空〉: 攻撃 0 本のターンだけ発動。 殻で被ダメを削り、 現在 HP 割合を刻む。
                if (aCount == 0 && hasVoidStance)
                {
                    int n = System.Math.Min(
                        InventorySystem.PassiveSkills.Effects.VoidStance.StackCap, voidStacks);
                    int chip = UnityEngine.Mathf.CeilToInt(
                        enemyHp * (InventorySystem.PassiveSkills.Effects.VoidStance.ChipBase
                                 + InventorySystem.PassiveSkills.Effects.VoidStance.ChipPerStack * n) / 100f);
                    effDmg += chip;   // 殴らない代わりに通る軽減不可ダメージ
                    float shell = System.Math.Max(0,
                        90 - InventorySystem.PassiveSkills.Effects.VoidStance.ReducePerStack * n) / 100f;
                    if (shell > 0f) taken = System.Math.Max(0, (int)(taken * (1f - shell)));
                }

                float u = wAtk * System.Math.Min(effDmg, enemyHp)
                        + wDef * System.Math.Min(block, enemyAtk)
                        + roleFlat + specFlat;
                if (effDmg >= enemyHp) u += 50f + enemyAtk; // 今ターン撃破 = 敵攻撃が消える
                if (taken >= myHp) u -= 10000f;              // 致死回避を最優先

                // ===== 短期決戦の概念 (2026-08-04) =====
                //   従来の効用は **1 ターンぶんの収支しか見ていなかった**。 撃破ボーナスも
                //   「**今ターン**倒せるなら」だけで、 2〜3 ターンで倒せる配分を評価できない。
                //   その結果 wDef * min(block, enemyAtk) が敵攻撃値に比例して伸び、
                //   強敵ほど防御が常に勝つ。 ブロックは敵 HP を減らさないので戦闘が伸び、
                //   総被ダメは増えるが BOT はそれを見られない
                //   （実測: 6F ボス戦の防御配線率 95.6% ＝ 亀に固定）。
                //
                //   **撃破までの残りターン数そのものを罰する。**
                //
                //   試して外した定式化 (2026-08-04・再提案しないこと):
                //     ① `taken * ttk` … 完全に防ぎ切ると taken=0 で罰が 0 になる。
                //        BOT は全層で亀に固定 (1F 防御配線率 18.6%→95.0%)、 完全クリア 9.1%→2.8%。
                //     ② ①をエスカレーション曲線で射影 … 短い戦闘では曲線が立ち上がらず罰が 0 のまま。
                //   どちらも「被弾量」経由で時間を測ろうとしたのが誤り。 防ぎ切れる相手には
                //   被弾量が 0 に張り付くので、 時間の情報が消える。 ここでは ttk を直接引く。
                int ttk = effDmg > 0
                        ? UnityEngine.Mathf.CeilToInt((float)enemyHp / effDmg)
                        : HorizonCapTurns;
                if (ttk > HorizonCapTurns) ttk = HorizonCapTurns;
                u -= TimeCostPerTurn * ttk;

                // 烈炎 (6 層ボス): ブロック端子へ閾値以上配線すると毎ターン スタック+1、
                //   ターン開始時にスタック分の軽減無視ダメ。 **配線側がこれを見られないと
                //   ただの一方的な税になる**ので、 撃破までの累積を効用へ入れる。
                if (tele.blazePenalizesBlock)
                {
                    int s0 = tele.blazeStacks;
                    float blaze = (bCount >= InventorySystem.PassiveSkills.Effects.BlazeBrand.BlockDiceThreshold)
                                ? s0 * ttk + ttk * (ttk + 1) * 0.5f   // 積み続ける
                                : s0 * ttk;                            // 据え置き
                    u -= blaze;
                }
                u += wChg * c * 2f;                          // 充電価値 (リロールの原資)
                u += 0.01f * aCount;                         // タイブレーク: 同効用なら攻撃を選ぶ

                // 2026-07-28: 攻撃端子 0 本 = **攻撃を行わない** (素火力も追撃も出ない)。
                //   よって「攻撃 0 本」は、 その 1 本を防御へ回さないと死ぬ場合にのみ選ぶ。
                //   ※〈虚空〉持ちもこの抑止の対象 (2026-07-29)。 刻みの価値は effDmg に足してあるので、
                //     本当に得なら抑止を越えて選ばれる。
                if (aCount == 0)
                {
                    // 「ブロックの最大目を 1 本だけ攻撃へ回す」場合の被弾と比較する。
                    int takenIfAttackOne = System.Math.Max(0, enemyAtk - (block - maxBlockDie));
                    bool onlySurvivesByNotAttacking = (takenIfAttackOne >= myHp) && (taken < myHp);
                    if (!onlySurvivesByNotAttacking) u -= 10000f;
                }
                return u;
            }

            float Score(CombatSystem.DiceTerminal[] w)
                => ScoreWithBuffers(w, scratchA, scratchB, atkGroups, blkGroups, roleBuf);

            // --- ゴースト (出目パーツ T3/T4) の決定 ---
            //   **実体の探索コストは 1 命令も増やさない。** ゴーストは合計値にしか乗らず
            //   役判定に参加しないので、 最良の実体配線が決まった**後**に貪欲で足せる。
            //   全列挙 (4^m) にはしない ── 相互作用は効用関数の min() の頭打ちだけで、
            //   そこだけのために指数を払う価値がない。 コストは 4 × 該当ダイス数の線形。
            //   **該当 0 本なら処理ごとスキップ**＝ パーツ未取得の間は追加コスト厳密に 0。
            CombatSystem.WiringPlan AttachGhosts(CombatSystem.DiceTerminal[] main)
            {
                var tiers = ctxRoles?.equippedFaceTiers;
                var fidx  = ctxRoles?.playerDiceFaceIdx;
                if (tiers == null || fidx == null) return CombatSystem.WiringPlan.Of(main);

                int[] cand = null;
                for (int i = 0; i < k && i < fidx.Length; i++)
                    if (GameLoop.DiceFaceParts.AllowsDualLink(tiers, fidx[i]))
                    { if (cand == null) cand = new int[k]; }
                if (cand == null) return CombatSystem.WiringPlan.Of(main);   // 0 本 = 何もしない

                for (int i = 0; i < k; i++) cand[i] = CombatSystem.WiringPlan.NoGhost;
                curGhost = cand;

                // 端子の種類数は本探索と同じ規則で引く (特殊端子は装着時のみ存在)。
                int ghostSpecialLimit = CombatSystem.SpecialTerminals.EquippedLimit(runForVoid);
                int ghostTermKinds = ghostSpecialLimit > 0 ? 4 : 3;

                // 出目の大きい順に決める。 頭打ちのある効用では、 大きい寄与から埋めた方が
                // 貪欲の取りこぼしが小さい。
                var order = new List<int>(k);
                for (int i = 0; i < k && i < fidx.Length; i++)
                    if (GameLoop.DiceFaceParts.AllowsDualLink(tiers, fidx[i])) order.Add(i);
                order.Sort((x, y) => rolls[y].CompareTo(rolls[x]));

                foreach (int i in order)
                {
                    bool canStack = GameLoop.DiceFaceParts.AllowsSameTerminalStack(tiers, fidx[i]);
                    int bestG = CombatSystem.WiringPlan.NoGhost;
                    float bestGu = Score(main);            // ゴースト無しを基準に
                    for (int t = 0; t < ghostTermKinds; t++)
                    {
                        // T3 は「異なる 2 端子」。 実体と同じ端子へ重ねられるのは T4 だけ。
                        if ((int)main[i] == t && !canStack) continue;
                        if (t == (int)CombatSystem.DiceTerminal.Special && ghostSpecialLimit <= 0) continue;
                        cand[i] = t;
                        float u = Score(main);
                        if (u > bestGu) { bestGu = u; bestG = t; }
                    }
                    cand[i] = bestG;
                }

                curGhost = null;
                return new CombatSystem.WiringPlan { main = main, ghost = cand };
            }

            var assign = new CombatSystem.DiceTerminal[k];
            var bestW = new CombatSystem.DiceTerminal[k];
            float bestU = float.NegativeInfinity;
            if (EffectiveWiringSkill == WiringSkill.Naive)
            {
                // 素朴版: 出目降順の本数分割だけを見る (ADR-0010 以前と同じ探索)。
                for (int a = 0; a <= k; a++)
                    for (int b = 0; b <= k - a; b++)
                    {
                        for (int i = 0; i < k; i++)
                            assign[idx[i]] = (i < a) ? CombatSystem.DiceTerminal.Attack
                                           : (i < a + b) ? CombatSystem.DiceTerminal.Block
                                           : CombatSystem.DiceTerminal.Charge;
                        if (tele.sealedTerminal >= 0)
                        {
                            bool used = false;
                            for (int i = 0; i < k && !used; i++)
                                if ((int)assign[i] == tele.sealedTerminal) used = true;
                            if (used) continue;
                        }
                        float u = Score(assign);
                        if (u > bestU) { bestU = u; System.Array.Copy(assign, bestW, k); }
                    }
            }
            else
            {
                // 端子は 攻撃/ブロック/充電 の 3 種、 特殊端子 (§6-5) を装着していれば 4 種。
                //   **未装着なら第 4 端子は存在しない**ので列挙もしない (3^5=243 / 4^5=1024)。
                int specialLimit = CombatSystem.SpecialTerminals.EquippedLimit(runForVoid);
                int termKinds = specialLimit > 0 ? 4 : 3;
                // 〈綻び〉: 封印された端子は使えない。 **予告に載っているので配線前に読める**。
                //   本体側 (SanitizeWiring) が追い出すので、 候補に残すと
                //   「追い出された後の形」で効用を出せず判断がずれる。
                int sealedTerm = tele.sealedTerminal;
                // 挑戦デバフ〈不器用〉(2026-08-10 リワーク) は端子の種類を縛らない。
                //   N 個以上挿すと**次のターン**その端子が半減するので、 合法性ではなく
                //   効用側で扱う (下の overload ペナルティ)。
                int termCap = 4;

                int total = 1;
                for (int i = 0; i < k; i++) total *= termKinds;
                optimalWiringDecisions++;
                optimalWiringCandidatesEvaluated += total;

                bool runParallel = parallelizeOptimalWiring
                    && total >= System.Math.Max(1, optimalWiringParallelMinCandidates)
                    && System.Environment.ProcessorCount > 1;
                if (runParallel)
                {
                    var scores = new float[total];
                    int maxWorkers = optimalWiringMaxParallelism > 0
                        ? optimalWiringMaxParallelism
                        : System.Math.Min(8, System.Math.Max(1, System.Environment.ProcessorCount - 1));
                    var options = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };
                    Parallel.For(
                        0, total, options,
                        () => new OptimalWiringBuffers(k),
                        (code, _, buffers) =>
                        {
                            scores[code] = float.NegativeInfinity;
                            int t = code;
                            for (int i = 0; i < k; i++)
                            {
                                buffers.assign[i] = (CombatSystem.DiceTerminal)(t % termKinds);
                                t /= termKinds;
                            }

                            int spec = 0, distinct = 0;
                            System.Array.Clear(buffers.termSeen, 0, buffers.termSeen.Length);
                            for (int i = 0; i < k; i++)
                            {
                                int ti = (int)buffers.assign[i];
                                if (ti == (int)CombatSystem.DiceTerminal.Special) spec++;
                                if (!buffers.termSeen[ti]) { buffers.termSeen[ti] = true; distinct++; }
                            }
                            if (spec > specialLimit || distinct > termCap) return buffers;
                            if (sealedTerm >= 0)
                            {
                                for (int i = 0; i < k; i++)
                                    if ((int)buffers.assign[i] == sealedTerm) return buffers;
                            }

                            scores[code] = ScoreWithBuffers(
                                buffers.assign,
                                buffers.scratchA,
                                buffers.scratchB,
                                buffers.atkGroups,
                                buffers.blkGroups,
                                buffers.roleBuf);
                            return buffers;
                        },
                        _ => { });

                    // 評価順に集約し、従来の「最初の最大値」を厳密に維持する。
                    for (int code = 0; code < total; code++)
                    {
                        float u = scores[code];
                        if (u <= bestU) continue;
                        bestU = u;
                        int t = code;
                        for (int i = 0; i < k; i++)
                        {
                            bestW[i] = (CombatSystem.DiceTerminal)(t % termKinds);
                            t /= termKinds;
                        }
                    }
                }
                else
                {
                    for (int code = 0; code < total; code++)
                    {
                        int t = code;
                        for (int i = 0; i < k; i++) { assign[i] = (CombatSystem.DiceTerminal)(t % termKinds); t /= termKinds; }

                        // 実行側 (SanitizeWiring) が畳む前に、 **制約を破る割り当てをそもそも評価しない**。
                        int spec = 0, distinct = 0;
                        _termSeen[0] = _termSeen[1] = _termSeen[2] = _termSeen[3] = false;
                        for (int i = 0; i < k; i++)
                        {
                            int ti = (int)assign[i];
                            if (ti == (int)CombatSystem.DiceTerminal.Special) spec++;
                            if (!_termSeen[ti]) { _termSeen[ti] = true; distinct++; }
                        }
                        if (spec > specialLimit || distinct > termCap) continue;
                        if (sealedTerm >= 0)
                        {
                            bool usesSealed = false;
                            for (int i = 0; i < k && !usesSealed; i++)
                                if ((int)assign[i] == sealedTerm) usesSealed = true;
                            if (usesSealed) continue;
                        }
                        float u = Score(assign);
                        if (u > bestU) { bestU = u; System.Array.Copy(assign, bestW, k); }
                    }
                }
            }
            return AttachGhosts(bestW);
        }

        /// <summary>ADR-0010 リロール方策。 **役を狙って残す**古典的なヨットの打ち方。
        ///
        /// 出目の大小ではなく「何を揃えかけているか」で残す札を決める ── 平均値で振り直すと
        /// リロールが単なる期待値嵩上げになり、 役システムの技量帯が消える。
        ///
        /// 判断の順:
        ///   ① 同値が 3 個以上 → それを残して残りを振る (大束・極を追う)
        ///   ② 4 連番が見えている → 連番を残して残りを振る (中階・大階を追う)
        ///   ③ 同値が 2 個 → ペアを残して残りを振る (二対・満を追う)
        ///   ④ 何も無い → 面平均を下回るダイスだけ振る
        ///
        /// コストは `個数 × 回数`。 **充電を使い切らない** ── 充電は端子へ挿さなかった
        /// ダイスの対価なので、 全部リロールへ流すと配線の判断そのものが消える。</summary>
        private int[] MutualRerollPolicy(int[] dice, CombatSystem.MutualTurnTelegraph tele, int attempt)
        {
            if (dice == null || dice.Length == 0) return null;
            if (attempt > RerollMaxAttempts) return null;
            var ctx = InventorySystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx == null) return null;

            int k = dice.Length;
            var faces = ctx.equippedDiceFaces;
            float meanFace = 3.5f;
            if (faces != null && faces.Length > 0)
            {
                int sum = 0;
                for (int i = 0; i < faces.Length; i++) sum += faces[i];
                meanFace = sum / (float)faces.Length;
            }

            // 残す添字を決める (keep=true のダイスは振らない)
            var keep = new bool[k];
            int maxSame = CombatSystem.YachtRoles.MaxSameCount(dice, out _, out _);
            int runLen = CombatSystem.YachtRoles.LongestRun(dice, 1);

            if (maxSame >= 3 || (maxSame >= 2 && runLen < 3))
            {
                // 同値の最大グループを残す
                int target = dice[0], bestCount = 0;
                for (int i = 0; i < k; i++)
                {
                    int c = 0;
                    for (int j = 0; j < k; j++) if (dice[j] == dice[i]) c++;
                    if (c > bestCount) { bestCount = c; target = dice[i]; }
                }
                for (int i = 0; i < k; i++) keep[i] = dice[i] == target;
            }
            else if (runLen >= 3)
            {
                // 最長連番の起点を求めて、 その並びに乗るダイスだけ残す
                int start = 0, best = 0;
                for (int i = 0; i < k; i++)
                {
                    int len = 1, next = dice[i] + 1;
                    while (Contains(dice, next)) { len++; next++; }
                    if (len > best) { best = len; start = dice[i]; }
                }
                var used = new bool[k];
                for (int v = start; v < start + best; v++)
                    for (int i = 0; i < k; i++)
                        if (!used[i] && dice[i] == v) { used[i] = true; keep[i] = true; break; }
            }
            else
            {
                // 何も揃っていない ── 面平均を下回るダイスだけ振り直す
                for (int i = 0; i < k; i++) keep[i] = dice[i] >= meanFace;
            }

            // 出目パーツ T1〈振り直せない〉: 方策の段階で対象から外す。
            //   CombatManager.RerollPhase でも最終的に弾かれるが、 **コストは pick.Length で
            //   先に計算される**ので、 そこで捨てられると払った充電が丸損になる。
            //   予算を正しく使うために方策側でも落とす (規則の強制点は CombatManager のまま)。
            var faceIdx = ctx.playerDiceFaceIdx;
            if (faceIdx != null && ctx.equippedFaceTiers != null)
                for (int i = 0; i < k && i < faceIdx.Length; i++)
                    if (GameLoop.DiceFaceParts.IsLocked(ctx.equippedFaceTiers, faceIdx[i])) keep[i] = true;

            var pickList = new List<int>(k);
            for (int i = 0; i < k; i++) if (!keep[i]) pickList.Add(i);
            if (pickList.Count == 0) return null;

            // 予算: 充電を使い切らない。 端子へ挿さなかった対価なので、
            // 全部リロールへ流すと「何本を充電へ回すか」の判断が消える。
            int charge = ctx.GetCharge();
            int budget = charge - RerollChargeReserve;
            int affordable = attempt > 0 ? budget / attempt : 0;
            if (affordable <= 0) return null;
            // 払える本数まで削る。 削る順は出目の高い方から (低い目ほど振り直す価値が高い)
            while (pickList.Count > affordable)
            {
                int worst = 0;
                for (int i = 1; i < pickList.Count; i++)
                    if (dice[pickList[i]] > dice[pickList[worst]]) worst = i;
                pickList.RemoveAt(worst);
            }
            return pickList.Count > 0 ? pickList.ToArray() : null;
        }

        /// <summary>1 ターンあたりのリロール上限。 コストが `個数 × 回数` で逓増するので
        /// 3 回目は 1 個でも 3 充電 ── 実質ここで止まる。</summary>
        private const int RerollMaxAttempts = 2;
        /// <summary>リロールに回さず残す充電。 パッシブ (火花・雷撃) の原資を枯らさないため。</summary>
        private const int RerollChargeReserve = 6;

        private static bool Contains(int[] v, int x)
        {
            for (int i = 0; i < v.Length; i++) if (v[i] == x) return true;
            return false;
        }

        /// <summary>ADR-0010 役の発動方策。 **切れるものは切る**。
        ///
        /// 1 戦闘 1 役 1 回なので「温存」が理屈上はありうるが、 見送れば必ずそのターンぶんの
        /// ダメージを払う ── 温存が得になるのは効果が完全に無駄になる局面だけなので、
        /// **効果が空振りする役だけを見送る**。 具体的には敵が攻撃してこないターンの防御役。
        ///
        /// 配線方策 (MutualWiringPolicy) は「切れるものは全部切る」前提で効用を出しているので、
        /// **両者の前提を揃えてある**。 ここで温存を入れるなら配線側の見積もりも直すこと。</summary>
        private List<CombatSystem.RoleKind> MutualRolePolicy(
            List<CombatSystem.RoleKind> candidates,
            CombatSystem.MutualTurnTelegraph tele,
            HashSet<CombatSystem.RoleKind> used)
        {
            if (candidates == null || candidates.Count == 0) return null;

            // === 7層ヴェスカ (§13-4) ===
            //   **予告に出ている抽選結果を役の判断へ通す** (2026-08-10)。 ここが無いと、
            //   〈天与の盾〉で半減されるターンに攻撃系の役を、 〈シールド〉で吸われるターンに
            //   撃破系の役を切ってしまう ── 1 戦闘 1 回の札を最悪の瞬間に捨てることになる。
            //   配線側 (MutualWiringPolicy) は既に予告を読んでいるのに、 役側だけ素通しだった。
            bool wasted = tele.enemyHalvesDamageThisTurn || tele.enemyShield > 0;
            bool enemyIdle = tele.enemyAttackValue <= 0;
            if (!enemyIdle && !wasted) return candidates;

            var fire = new List<CombatSystem.RoleKind>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                var rk = candidates[i];
                // 敵が殴ってこないターンに被ダメ 0 / 半減を切っても何も起きない。
                if (enemyIdle && (rk == CombatSystem.RoleKind.LargeRun
                               || rk == CombatSystem.RoleKind.Offset)) continue;
                // 与ダメが半減/吸収されるターンに撃破系を切ると、 効果が丸ごと目減りする。
                //   〈極〉はボスなら最大HP比の削りなので半減の影響を受けない ── 除外しない。
                // 〈拮抗〉は与ダメ+50% なので、 半減/吸収されるターンに切ると目減りする。
                if (wasted && rk == CombatSystem.RoleKind.MatchedStrike) continue;
                fire.Add(rk);
            }
            // 挑戦デバフ〈綻び〉で**末尾から切り捨てられる**ので、 価値の高い順に並べる。
            fire.Sort((a, b) => RolePriorityOf(b).CompareTo(RolePriorityOf(a)));
            return fire;
        }

        /// <summary>役の優先度 (大きいほど先に切る)。 〈綻び〉の発動上限で**末尾が捨てられる**ため、
        /// 切り捨てられて惜しい順に並べる必要がある。 撃破・無効化 > 盤面強化 > 小加算。</summary>
        public static int RolePriorityOf(CombatSystem.RoleKind k)
        {
            switch (k)
            {
                case CombatSystem.RoleKind.Yacht:        return 100;  // 即勝利 / ボス大削り
                case CombatSystem.RoleKind.MatchedStrike: return 60;  // 与ダメ+50% (確定キルから格下げ)
                case CombatSystem.RoleKind.LargeRun:     return 90;   // 被ダメ 0
                case CombatSystem.RoleKind.Offset:       return 85;   // 被ダメ 0 + 充電
                case CombatSystem.RoleKind.Quad:         return 80;   // 敵 1 ターン行動不能
                case CombatSystem.RoleKind.FullHouse:    return 70;   // 両端子 +10
                case CombatSystem.RoleKind.Triple:       return 65;   // 会心確定 / 被ダメ半減
                case CombatSystem.RoleKind.MediumRun:    return 60;   // 充電 +12 + 無料リロール
                case CombatSystem.RoleKind.TwoPair:      return 50;   // 充電 +8
                case CombatSystem.RoleKind.Balance:      return 45;   // 両端子 +5
                case CombatSystem.RoleKind.SkipRun:      return 40;
                case CombatSystem.RoleKind.AllEven:      return 35;
                case CombatSystem.RoleKind.AllDifferent: return 30;
                case CombatSystem.RoleKind.Pair:         return 25;
                case CombatSystem.RoleKind.SmallRun:     return 20;
                case CombatSystem.RoleKind.AllOdd:       return 15;
                default:                                 return 10;
            }
        }

        /// <summary>ラン開始前に宣言する特殊端子 (§6-5) をペルソナから決める。
        ///
        /// **一様ランダムにしない。** 端子は「そのビルドで何をしたいか」の宣言なので、
        /// ビルドと無関係に配ると全ペルソナの平均しか出ず、 どの端子が効くのか分からなくなる。
        /// ペルソナが立っていないラン (RawTier) だけ一様抽選にして、 11 種の網羅を担保する。</summary>
        private string PickSpecialTerminal()
        {
            var K = CombatSystem.SpecialTerminalKind.None;
            switch (_currentPersona)
            {
                case BuildPersona.Charge:   K = CombatSystem.SpecialTerminalKind.Battery;     break;
                case BuildPersona.Shield:   K = CombatSystem.SpecialTerminalKind.FullGuard;   break;
                case BuildPersona.Berserk:  K = CombatSystem.SpecialTerminalKind.Deathwish;   break;
                case BuildPersona.Bludgeon: K = CombatSystem.SpecialTerminalKind.HeavyStrike; break;
                case BuildPersona.Crit:     K = CombatSystem.SpecialTerminalKind.Aim;         break;
                case BuildPersona.Bleed:    K = CombatSystem.SpecialTerminalKind.Bleed;       break;
                case BuildPersona.Poison:   K = CombatSystem.SpecialTerminalKind.VitalPoint;  break;
                case BuildPersona.Rinkai:   K = CombatSystem.SpecialTerminalKind.Foundry;     break;
                case BuildPersona.Standard: K = CombatSystem.SpecialTerminalKind.HeavyStrike; break;
            }
            if (K != CombatSystem.SpecialTerminalKind.None)
                return CombatSystem.SpecialTerminals.Get(K)?.id ?? "";

            var pool = CombatSystem.SpecialTerminals.All;
            return pool[GameLoop.GameRng.RangeAuto("AutoRunner.specialTerm", 0, pool.Count)].id;
        }

        /// <summary>端子上限の判定で使う「その端子を使ったか」のフラグ。 毎回 new しないため使い回す。</summary>
        private readonly bool[] _termSeen = new bool[4];

        /// <summary>Optimal 配線評価のワーカー専用スクラッチ。共有書き込みを完全に避ける。</summary>
        private sealed class OptimalWiringBuffers
        {
            public readonly CombatSystem.DiceTerminal[] assign;
            public readonly int[] scratchA;
            public readonly int[] scratchB;
            public readonly int[][] atkGroups;
            public readonly int[][] blkGroups;
            public readonly List<CombatSystem.RoleKind> roleBuf = new List<CombatSystem.RoleKind>(16);
            public readonly bool[] termSeen = new bool[4];

            public OptimalWiringBuffers(int diceCount)
            {
                assign = new CombatSystem.DiceTerminal[diceCount];
                scratchA = new int[diceCount];
                scratchB = new int[diceCount];
                atkGroups = new int[diceCount + 1][];
                blkGroups = new int[diceCount + 1][];
                for (int n = 0; n <= diceCount; n++)
                {
                    atkGroups[n] = new int[n];
                    blkGroups[n] = new int[n];
                }
            }
        }

        /// <summary>役 1 つぶんの効用寄与を見積もる。 **順序さえ合っていればよい近似**で、
        /// 実効果 (YachtRoleEffects.Apply) の厳密な再現ではない ── 配線候補どうしを
        /// 比べるための指標なので絶対値の較正は要らない。
        ///
        /// 出力は 4 経路に分ける: 攻撃合計への加算 / ブロック合計への加算 /
        /// 直接の効用加点 / 盤面フラグ (被ダメ 0・確定キル)。</summary>
        private static void RoleEstimate(CombatSystem.RoleKind rk, bool onAttack,
                                         int aCount, int bCount, int aSum, int block,
                                         int enemyAtk, int enemyHp, float critMul, float wChg,
                                         ref int dAtk, ref int dBlk, ref float flat,
                                         ref bool zeroTaken, ref bool kill)
        {
            int n = onAttack ? aCount : bCount;
            switch (rk)
            {
                // ── 端子役 ──
                case CombatSystem.RoleKind.AllDifferent:                  // 散: 合計 +(本数×2)
                    if (onAttack) dAtk += n * 2; else dBlk += n * 2;
                    break;
                case CombatSystem.RoleKind.Pair:                          // 対: ペア出目合計÷2
                    // 出目依存化 (2026-08-22)。 ペア 1 組なら実効果は「その面の値」なので、
                    //   この近似では**その端子の平均面**で代用する (順序が合えばよい)。
                    if (n > 0) { int avg = System.Math.Max(1, (onAttack ? aSum : block) / n);
                                 if (onAttack) dAtk += avg; else dBlk += avg; }
                    break;
                case CombatSystem.RoleKind.AllEven:                       // 偶: 攻=合計×1.3 / 防=余剰の半分をシールド
                    if (onAttack) dAtk += UnityEngine.Mathf.RoundToInt(aSum * 0.3f);
                    else flat += System.Math.Max(0, block - enemyAtk) * 0.25f;
                    break;
                case CombatSystem.RoleKind.AllOdd:                        // 奇: 攻=会心率+25% / 防=反射20%
                    if (onAttack) flat += 0.25f * aSum * (critMul - 1f);
                    break;
                case CombatSystem.RoleKind.SmallRun:                      // 小階: 攻=貫通+30% / 防=敵の次T攻撃-3
                    if (onAttack) flat += 0.10f * aSum; else flat += 3f;
                    break;
                case CombatSystem.RoleKind.Triple:                        // 束: 攻=会心確定 / 防=被ダメ半減
                    // 素の会心率ぶんは既に期待値へ織り込まれているので、 差分だけを 0.6 掛けで見る。
                    if (onAttack) flat += 0.6f * aSum * (critMul - 1f);
                    else flat += System.Math.Max(0, enemyAtk - block) * 0.5f;
                    break;
                case CombatSystem.RoleKind.SkipRun:                       // 飛階: 攻=固定ダメ+(本数×3) / 防=反射30%
                    if (onAttack) dAtk += n * 3;
                    break;

                // ── 手札役 (盤面全体) ──
                case CombatSystem.RoleKind.TwoPair:   flat += wChg * 8f;  break;      // 二対: 充電+8
                case CombatSystem.RoleKind.MediumRun: flat += wChg * 12f + 6f; break; // 中階: 充電+12 + 次T無料リロール
                case CombatSystem.RoleKind.FullHouse:                                 // 満: 手札合計÷2 を各端子
                {
                    // 出目依存化 (2026-08-22)。 手札合計は分からないので端子合計の和で代用する。
                    int fh = System.Math.Max(1, (aSum + block) / 2);
                    if (aCount > 0) dAtk += fh;
                    if (bCount > 0) dBlk += fh;
                    break;
                }
                case CombatSystem.RoleKind.LargeRun:  zeroTaken = true;   break;      // 大階: 被ダメ 0
                case CombatSystem.RoleKind.Quad:      flat += enemyAtk;   break;      // 大束: 敵が次ターン行動不能
                case CombatSystem.RoleKind.Yacht:     kill = true;        break;      // 極: 出目依存の大削り
                                                                                      //   (低い揃えは 20% 止まりなので過大評価だが、
                                                                                      //    順序用の近似として撃破扱いのまま置く)

                // ── 配線役 ──
                case CombatSystem.RoleKind.Balance:   dAtk += 5; dBlk += 5; break;    // 均
                case CombatSystem.RoleKind.Offset:    zeroTaken = true; flat += wChg * 10f; break; // 相殺
                case CombatSystem.RoleKind.MatchedStrike: flat += 0.5f * aSum; break; // 拮抗: 与ダメ+50%
            }
        }

        /// <summary>Bot 専用: 戦闘中、シュヴァリエ・サン=ジョリオラ戦でのレイピア起動同期。
        /// ボスが形態1(コントラタック)中はレイピアON、形態2(オポジション)中はOFF。
        /// 切替時の解除効果(次T ダイス+1, 撃破済みなら会心+9)も自動で享受する。</summary>
        private void AutoToggleRapierVsSaintGeorges(CombatManager cm, GameLoop.RunState run)
        {
            if (cm == null || run == null) return;
            if (run.ownedPassiveItems == null || !run.ownedPassiveItems.Contains("シュヴァリエのレイピア")) return;
            var enemy = cm.CurrentEnemy;
            if (enemy == null || enemy.id != "boss_layer5_hidden") return;
            var ctx = InventorySystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx == null) return;
            int bossPhase = (int)ctx.GetAccumulated("sg_phase");
            bool contreActive = ctx.GetAccumulated("player_contre") > 0f;
            if (bossPhase == 1 && !contreActive)
                GameLoop.Consumables.TryUseRapier(run);
            else if (bossPhase == 2 && contreActive)
                GameLoop.Consumables.TryUseRapier(run);
        }

        /// <summary>所持consumableから優先順に最初の1個を使用（戦闘中＝ctx即時適用）。</summary>
        /// <summary>回復量 (最大HP 比)。 <c>ItemIds.ConsHealFamily</c> の 4 段。 昇順で持つこと。</summary>
        private static readonly (string id, float pct)[] HealTiers =
        {
            ("小回復薬", 0.25f), ("回復薬", 0.40f),
            ("上回復薬", 0.60f), ("完全回復薬", 1.00f),
        };

        /// <summary>**死ぬ場面**で飲む 1 本を選ぶ。 温存しない。
        ///
        /// <para>小さい順に見て「飲めば致死量を超えて生き残れる」最初のものを使う ──
        /// 過剰回復で上限に溢れさせないため。 どれでも足りないときは**最大のものを飲む**
        /// (足りなくても生存確率は上がる。 持ったまま死ぬよりは必ず良い)。</para>
        ///
        /// <para>「大きい回復は窮地まで温存」という旧ルールは、 1 撃が最大HP の 3 割入る
        /// 7層後半では解除条件 (HP 1/4 以下) に触れる前に死ぬため機能しなかった。</para></summary>
        private bool UseBestHeal(GameLoop.RunState run, int hp, int lethal)
        {
            if (run?.ownedConsumables == null || run.playerMaxHP <= 0) return false;
            string pick = null;
            foreach (var t in HealTiers)
            {
                if (!run.ownedConsumables.Contains(t.id)) continue;
                int after = Mathf.Min(run.playerMaxHP, hp + Mathf.RoundToInt(run.playerMaxHP * t.pct));
                if (after > lethal) { pick = t.id; break; }
            }
            if (pick == null)
                foreach (var t in HealTiers)          // 昇順なので最後に残るのが最大
                    if (run.ownedConsumables.Contains(t.id)) pick = t.id;
            return pick != null && GameLoop.Consumables.Use(run, pick);
        }

        private bool UseFirst(GameLoop.RunState run, params string[] ids)
        {
            if (run?.ownedConsumables == null) return false;
            foreach (var id in ids)
                if (run.ownedConsumables.Contains(id))
                    return GameLoop.Consumables.Use(run, id);
            return false;
        }

        private void DoEvent()
        {
            var gm = GameManager.Instance;
            var ee = EventEncounter.Instance;
            var cur = ee != null ? ee.Current : null;

            // ランダムイベント無限ループ等の保険: 一定回数で強制読了して脱出を試みる
            // （回復不能なら RunOne のストール検出が DEADLOCK として確定させる）
            _eventStuckCount++;
            if (_eventStuckCount > 40)
            {
                // Current を null 化 → GameManager 側(D1)が未確定でも MapNavigation へ脱出させる
                EventEncounter.Instance?.Clear();
                gm.ConfirmEventEncounter();
                _lastResolvedEvent = cur;
                return;
            }

            if (cur == null) { gm.ConfirmEventEncounter(); return; }

            if (!ReferenceEquals(cur, _lastResolvedEvent))
            {
                int idx = PickSafeChoice(cur);
                // [アブレーション] イベントの選択判断を潰す (提示された選択肢から一様抽選)。
                if (ablateEvent && cur.choices != null && cur.choices.Count > 1)
                    idx = AblationRng.Next(0, cur.choices.Count);
                // Ultra: 選択肢に条件は無く (EventChoice は text/effects のみ)、
                //   提示された分がそのまま合法手になる。
                idx = UltraOverrideChoice(gm,
                    AutoTest.Ultra.UltraDecisionPoint.EventChoice,
                    AutoTest.Ultra.UltraActionKind.ChooseEventOption,
                    BuildEventChoiceView(cur), idx);
                _lastResolvedEvent = cur;
                // L1.5: イベント学習用に「id|choiceIndex」を記録
                if (_cur != null && cur != null && !string.IsNullOrEmpty(cur.id))
                    _cur.eventChoicesMade.Add(cur.id + "|" + idx);
                gm.ResolveEventChoice(idx);
            }
            else
            {
                // フレーバー読了 or 戦闘トリガ後の復帰待ち
                gm.ConfirmEventEncounter();
            }
        }

        /// <summary>イベントの選択肢を観測用の形へ写す。
        ///
        /// <para>選択肢の**文面をそのまま**ラベルに載せる ── プレイヤーが読んでいるのは
        /// これであって、 効果の内部表現ではない。 効果の要約 (<c>EventChoiceScorer</c> が
        /// 使うような数値) を渡すと、 盤面に出ていない情報を渡すことになる。</para></summary>
        private static AutoTest.Ultra.UltraChoiceView BuildEventChoiceView(EventDefinition def)
        {
            var view = new AutoTest.Ultra.UltraChoiceView
            {
                kind = "event",
                prompt = def != null ? (def.name ?? def.id ?? "") : "",
            };
            if (def?.choices == null || def.choices.Count == 0) return view;

            var options = new AutoTest.Ultra.UltraChoiceOption[def.choices.Count];
            for (int i = 0; i < def.choices.Count; i++)
                options[i] = new AutoTest.Ultra.UltraChoiceOption
                {
                    index = i,
                    id = "",                       // 安定 ID は無いので添字で識別する
                    label = def.choices[i]?.text ?? "",
                };
            view.options = options;
            return view;
        }

        private int PickSafeChoice(EventDefinition def)
        {
            if (def == null || def.choices == null || def.choices.Count == 0) return 0;

            var run = GameLoop.GameManager.Instance?.Run;

            // 7層を目指す方策では、6層の専用イベントで〈真理〉を得る選択を明示的に選ぶ。
            // 効果欄は「なし」なので、通常の数値スコアだけでは離脱択と同点になってしまう。
            if (def.name == GameLoop.ConvictionSystem.Layer7RevelationEventName)
                return 0;

            // ① フラグ進路は依然として「ほぼ無条件で進める」（チェーン進路は数値スコア以上に価値が高い）
            int progressIdx = PickFlagProgressChoice(def);
            if (progressIdx >= 0) return progressIdx;

            // ② 数値スコアラで選定。スコア差が小さければ次点も取り得る（両分岐の探索性）。
            //    HPが低い時は HpDelta/EnterCombat 系が強烈にマイナス → 自動的に「立ち去り」を選ぶ。
            //    現状HP余裕で 100G+希望損 vs なし なら 100G を取る（ゴールド価値 > 希望コスト）。
            int byScore = EventChoiceScorer.PickBestIndex(def, run, _rng,
                explorationRate: AutoTest.PolicyParameters.Current.eventExplorationRate);

            // ③ 効果が全くない選択肢が複数ある場合は、最低限のフォールバックとして「立ち去り」系を選ぶ
            //    （スコアラは効果ゼロを 0 と評価するので、明示的な離脱選択肢を優先）
            if (byScore >= 0 && byScore < def.choices.Count)
            {
                var pickedText = def.choices[byScore]?.text ?? "";
                bool pickedIsLeave = false;
                foreach (var kw in LeaveKeywords) if (pickedText.Contains(kw)) { pickedIsLeave = true; break; }
                // 危険語入り選択肢のスコアが負ならOK、もしスコア同点で危険語のみの選択肢を引いてしまった場合の救済
                if (!pickedIsLeave)
                {
                    // スコア 0 以下なら離脱選択肢を探す
                    float pickedScore = EventChoiceScorer.Score(def.choices[byScore], run);
                    if (pickedScore <= 0f)
                    {
                        for (int i = 0; i < def.choices.Count; i++)
                        {
                            var txt = def.choices[i]?.text ?? "";
                            foreach (var kw in LeaveKeywords)
                                if (txt.Contains(kw)) return i;
                        }
                    }
                }
                return byScore;
            }
            return 0;
        }

        /// <summary>選択肢の効果を見て「フラグを進める」スコアを算出。
        /// いずれかの選択肢がプラスならその index を返し、なければ -1。
        /// 既存所持フラグを廃棄するだけの選択肢はマイナス点で忌避される。</summary>
        private int PickFlagProgressChoice(EventDefinition def)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            int bestIdx = -1;
            int bestScore = 0;
            for (int i = 0; i < def.choices.Count; i++)
            {
                int s = ScoreFlagChoice(def.choices[i], run);
                if (s > bestScore) { bestScore = s; bestIdx = i; }
            }
            return bestIdx;
        }

        /// <summary>選択肢1つに対するフラグ進路スコア。</summary>
        private int ScoreFlagChoice(EventSystem.EventChoice choice, GameLoop.RunState run)
        {
            if (choice?.effects == null || choice.effects.Count == 0) return 0;
            int score = 0;
            bool hasGain = false;
            int discardCount = 0;
            foreach (var eff in choice.effects)
            {
                switch (eff.type)
                {
                    case EventSystem.EventEffectType.GainFlag:
                        score += 100; hasGain = true;
                        // 未所持なら追加加点 (フラグ未成立時=新規進路を優先)
                        if (run?.ownedFlags == null || !run.ownedFlags.Contains(eff.param ?? "")) score += 50;
                        break;
                    case EventSystem.EventEffectType.GainPassiveItem:
                    case EventSystem.EventEffectType.GainSpecificItem:
                        // 名前付き / パッシブ獲得は基本的に進路系
                        score += 60; hasGain = true;
                        break;
                    case EventSystem.EventEffectType.DiscardFlag:
                        discardCount++;
                        break;
                }
            }
            // フラグ廃棄のみで何も獲得しない選択肢は強い忌避
            if (discardCount > 0 && !hasGain) score -= 80;
            // 廃棄しつつ獲得もある (チェーン継続: 旧フラグ → 新フラグ/パッシブ) は中立 (gain加点で十分)
            return score;
        }

        // ===== 5Fボス勝率スイープ =====

        /// <summary>実ランから5F到達ビルドを採取し、全(武器×ダイス)で5Fボス勝率を総当たり計測。</summary>
        private IEnumerator RunBoss5Sweep(GameManager gm)
        {
            // --- Phase A: 実ランから「5F到達時ビルド」を採取 ---
            _simHarvestArmed = true;
            int attempts = 0;
            int cap = Mathf.Max(runCount, simSampleBuilds * 30);
            ResetScreenProgress(cap, "5Fビルド採取");
            while (_simBases.Count < simSampleBuilds && attempts < cap)
            {
                curRunInArm = attempts + 1;
                yield return RunOne(attempts);
                attempts++;
                yield return null;
            }
            _simHarvestArmed = false;
            Debug.Log($"[AutoRunner] ビルド採取: {_simBases.Count}件 / {attempts}ラン試行");
            if (_simBases.Count == 0)
            {
                _simReport = "5F到達ビルドを採取できませんでした（到達率0）。simSampleBuilds やメタ設定を見直してください。\n";
                yield break;
            }

            // --- 有効な武器/ダイスIDに絞る ---
            var db = ItemDatabase.Instance;
            var weapons = new List<string>();
            foreach (var w in simWeapons) if (db?.GetItem(w) != null) weapons.Add(w);
            var dice = new List<string>();
            foreach (var d in simDice) if (db?.GetItem(d) != null) dice.Add(d);
            if (weapons.Count == 0 || dice.Count == 0)
            {
                _simReport = "有効な武器/ダイスIDがありません。simWeapons / simDice を確認してください。\n";
                yield break;
            }

            // --- クリーンな Run を1つ用意し、毎試行で土台ビルドを上書き ---
            // 2026-06-28: シミュレータも職業をランダム化 (build シミュレーションでクラス間バランスも測定)
            GameLoop.GameManager.SelectedClass = (GameLoop.ClassType)GameLoop.GameRng.RangeAuto("AutoRunner.2", 0, 4);
            gm.StartNewRun();

            // --- Phase B: 全(武器×ダイス)スイープ ---
            var winPct = new Dictionary<string, double>();
            int comboCount = weapons.Count * dice.Count;
            int comboIdx = 0;
            foreach (var weapon in weapons)
            {
                foreach (var d in dice)
                {
                    int wins = 0;
                    for (int t = 0; t < simTrialsPerCombo; t++)
                    {
                        var b = _simBases[t % _simBases.Count];
                        var run = gm.Run;
                        run.playerHP = simBaseHP;
                        run.playerMaxHP = simBaseHP;
                        run.weaponPlus = b.weaponPlus;
                        run.limitBreakStage = b.limitBreakStage;
                        run.equippedWeaponId = weapon;
                        run.equippedDiceId = d;
                        run.ownedPassiveItems = new List<string>(b.passives);

                        var res = gm.SimulateBossFight(simBossFloor);
                        if (res.playerWon) wins++;

                        if ((t & 127) == 0) yield return null; // フレーム譲り
                    }
                    winPct[weapon + "|" + d] = 100.0 * wins / Mathf.Max(1, simTrialsPerCombo);
                    comboIdx++;
                    if ((comboIdx & 3) == 0) Debug.Log($"[AutoRunner] スイープ {comboIdx}/{comboCount}");
                    yield return null;
                }
            }

            _simReport = BuildSweepReport(weapons, dice, winPct);
        }

        /// <summary>ダイスIDの短縮表示コード（マトリクス列見出し用）。</summary>
        private static string DiceCode(string id)
        {
            switch (id)
            {
                case "dice_wood": return "Wo";
                case "dice_bone": return "Bo";
                case "dice_copper": return "Co";
                case "dice_iron": return "Ir";
                case "dice_biased": return "Bi";
                case "dice_gem": return "Ge";
                case "dice_flame": return "Fl";
                case "dice_stable": return "Sb";
                case "dice_twinsnake": return "Tw";
                case "dice_star": return "Sr";
                case "dice_destiny": return "De";
                case "dice_greed": return "Gr";
                case "dice_moroha": return "Mo";
                case "dice_perfection": return "Pf";
                default: return id.StartsWith("dice_") ? id.Substring(5, Math.Min(2, id.Length - 5)) : id;
            }
        }

        private string BuildSweepReport(List<string> weapons, List<string> dice, Dictionary<string, double> winPct)
        {
            var sb = new StringBuilder();
            sb.AppendLine("================ 5Fボス 勝率スイープ ================");
            sb.AppendLine($"日時          : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"対象ボス      : boss_layer{simBossFloor}");
            sb.AppendLine($"採取ビルド数  : {_simBases.Count}（実ランの5F到達時パッシブ/強化段階を土台。武器・ダイスのみ差し替え）");
            sb.AppendLine($"戦闘開始HP    : {simBaseHP}（固定。採取ビルドの現在HPは不使用）");
            sb.AppendLine($"試行/組合せ   : {simTrialsPerCombo}（採取ビルドをラウンドロビンで均等使用）");
            sb.AppendLine("※消費アイテムは不使用（武器×ダイスの素の勝率を比較）");
            sb.AppendLine();

            // 凡例
            sb.AppendLine("---- ダイス略号 ----");
            var legend = new StringBuilder("  ");
            foreach (var d in dice) legend.Append($"{DiceCode(d)}={d.Replace("dice_", "")}  ");
            sb.AppendLine(legend.ToString());
            sb.AppendLine();

            // マトリクス（行=武器, 列=ダイス, 値=勝率%）
            sb.AppendLine("---- 勝率マトリクス（行=武器 / 列=ダイス） ----");
            var header = new StringBuilder();
            header.Append(PadR("武器", 12));
            foreach (var d in dice) header.Append(PadL(DiceCode(d), 5));
            sb.AppendLine(header.ToString());
            foreach (var w in weapons)
            {
                var row = new StringBuilder();
                row.Append(PadR(TruncDisp(w, 11), 12));
                foreach (var d in dice)
                {
                    double v = winPct.TryGetValue(w + "|" + d, out var p) ? p : -1;
                    row.Append(PadL(v < 0 ? "-" : v.ToString("F0") + "%", 5));
                }
                sb.AppendLine(row.ToString());
            }
            sb.AppendLine();

            // 上位/下位 組み合わせ
            var all = new List<KeyValuePair<string, double>>(winPct);
            all.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.AppendLine("---- 勝率トップ10（強コンボ） ----");
            for (int i = 0; i < all.Count && i < 10; i++)
            {
                var k = all[i].Key; int bar = k.IndexOf('|');
                sb.AppendLine($"  {PadR(k.Substring(0, bar), 12)}{PadR(k.Substring(bar + 1).Replace("dice_", ""), 14)}{PadL(all[i].Value.ToString("F0") + "%", 5)}");
            }
            sb.AppendLine();
            sb.AppendLine("---- 勝率ワースト10（弱コンボ） ----");
            for (int i = all.Count - 1; i >= 0 && i >= all.Count - 10; i--)
            {
                var k = all[i].Key; int bar = k.IndexOf('|');
                sb.AppendLine($"  {PadR(k.Substring(0, bar), 12)}{PadR(k.Substring(bar + 1).Replace("dice_", ""), 14)}{PadL(all[i].Value.ToString("F0") + "%", 5)}");
            }
            sb.AppendLine();
            return sb.ToString();
        }

        // ===== 計測フック =====

        private void OnEnemyEncountered(EnemyData e)
        {
            var gm = GameManager.Instance;
            var cm = CombatSystem.CombatManager.Instance;

            // 5Fボス勝率スイープ: 採取モード中、対象フロアのボスに到達したらビルドを採取
            if (_simHarvestArmed && e != null && e.id != null
                && e.id.StartsWith($"boss_layer{simBossFloor}")
                && _simBases.Count < simSampleBuilds && gm?.Run != null)
            {
                var run = gm.Run;
                var passives = new List<string>();
                if (run.ownedPassiveItems != null)
                    foreach (var id in run.ownedPassiveItems)
                    {
                        // 武器・ダイスはスイープ側で差し替えるため土台ビルドからは除外
                        var it = ItemDatabase.Instance?.GetItem(id);
                        if (it != null && (it.category == ItemCategory.Weapon || it.category == ItemCategory.Dice)) continue;
                        passives.Add(id);
                    }
                _simBases.Add(new SimBuild
                {
                    hp = run.playerHP,
                    weaponPlus = run.weaponPlus,
                    limitBreakStage = run.limitBreakStage,
                    passives = passives
                });
            }

            // チェーン swap で再エンカウントしたケース: 前フォームの戦績を 1 件確定させる。
            // (戦闘自体は継続するため OnBattleEnded は鳴らない → ここで明示記録しないと
            //  途中フォームが永遠に summary に出てこない)
            bool isChainSwap = cm != null && cm.IsCombatActive
                && !string.IsNullOrEmpty(_pendingEnemyId);
            if (isChainSwap && _cur != null)
            {
                // 戦闘中は Run.playerHP が更新されない（戦闘終了時のみ同期）。
                // チェーン中の正しい現在HPは CombatManager のライブ値を使う。
                int hpNow = (cm != null && cm.IsCombatActive) ? cm.PlayerHP : (gm?.Run?.playerHP ?? 0);
                var midRec = new CombatRec
                {
                    enemy = _pendingEnemyName ?? "?",
                    enemyId = _pendingEnemyId ?? "",
                    floor = gm?.Run?.currentFloor ?? 0,
                    isBoss = _pendingEnemyIsBoss,
                    won = true, // チェーン swap は前形態を倒したから起きる
                    turns = _cwWin + _cwDraw + _cwLoss,
                    hpBefore = _pendingEnemyHpBefore,
                    hpAfter = hpNow,
                    afterLastStand = _cur.lastStandUsed,
                    tWin = _cwWin, tDraw = _cwDraw, tLoss = _cwLoss, tLossAbs = _cwLossAbs,
                    weaponId = gm?.Run?.equippedWeaponId ?? "",
                    diceId = gm?.Run?.equippedDiceId ?? "",
                    // **被ダメ/与ダメ/敵HP を必ず埋める (2026-09-09)。** 未設定のままだと 0 が入り、
                    //   per-enemy 集計で 7層 p1〜p3 が「被ダメ 0・与ダメ 0・敵HP 2」という
                    //   **存在しない弱さ**に見える (敵HP 2 は「その段で死んだ数件だけが実HP を持つ」
                    //   平均の産物)。 実際に一度それを根拠に強化しかけた。
                    //   damageDealt は段の HP を削り切ったから遷移が起きるので maxHP と等しい。
                    damageTaken = Math.Max(0, _pendingEnemyHpBefore - hpNow),
                    enemyMaxHP = CombatSystem.EnemyDatabase.Get(_pendingEnemyId)?.maxHP ?? 0,
                    damageDealt = CombatSystem.EnemyDatabase.Get(_pendingEnemyId)?.maxHP ?? 0,
                    playerMaxHpEnd = (cm != null && cm.IsCombatActive)
                                   ? cm.PlayerMaxHP : (gm?.Run?.playerMaxHP ?? 0),
                };
                _cur.combats.Add(midRec);
                // L1学習: チェーン途中で倒した形態も「撃破記録」に入れる
                if (!string.IsNullOrEmpty(midRec.enemyId) && midRec.enemyId.StartsWith(GameLoop.BossIds.Layer7Prefix))
                    _cur.awakenedFormsKilled.Add(midRec.enemyId);
                // 注: totalCombats / totalTurns / totalWins には加算しない
                // (OnBattleEnded 側のチェーン最終形態分でラン全体の合計が記録されるため、
                //  ここで足すと二重計上になる。combats リストの per-enemy 集計だけ厚くする)
            }

            _pendingEnemyName = e != null ? e.displayName : "?";
            _pendingEnemyId = e != null && e.id != null ? e.id : "";
            // ボス判定は敵IDのみで厳密に行う（ノード種別フォールバックは誤検出の元）
            _pendingEnemyIsBoss = GameLoop.BossIds.IsBoss(_pendingEnemyId);
            // 次フォーム開始時点の現在HP。チェーン中は CombatManager のライブHPを使う
            // （Run.playerHP は戦闘終了まで更新されないため、これが無いと各フォームの被ダメが常に0と誤計測される）。
            _pendingEnemyHpBefore = (cm != null && cm.IsCombatActive) ? cm.PlayerHP : (gm?.Run?.playerHP ?? 0);
            _cwWin = _cwDraw = _cwLoss = _cwLossAbs = 0; // 新戦闘のターン内訳リセット
        }

        private void OnBattleEnded(CombatResult r)
        {
            if (_cur == null) return;
            var gm = GameManager.Instance;
            var rec = new CombatRec
            {
                enemy = string.IsNullOrEmpty(r.enemyDisplayName) ? _pendingEnemyName : r.enemyDisplayName,
                enemyId = _pendingEnemyId ?? "",
                floor = gm.Run?.currentFloor ?? 0,
                isBoss = _pendingEnemyIsBoss,
                won = r.playerWon,
                turns = r.totalTurns,
                hpBefore = _pendingEnemyHpBefore,
                hpAfter = r.playerHPRemaining,
                afterLastStand = _cur.lastStandUsed,
                tWin = _cwWin, tDraw = _cwDraw, tLoss = _cwLoss, tLossAbs = _cwLossAbs,
                isFightEnd = true,
                healApplied = r.healApplied,
                shieldGained = r.shieldGained,
                damageDealt = r.damageDealt,
                damageTaken = r.damageTaken,
                enemyMaxHP = r.enemyMaxHP,
                weaponId = gm?.Run?.equippedWeaponId ?? "",
                diceId = gm?.Run?.equippedDiceId ?? "",
                deathCause = r.deathCause,
                playerRollSum = r.playerRollSum,
                playerRollCount = r.playerRollCount,
                playerDamageBySource = r.playerDamageBySource,
                strongRollTurns = r.strongRollTurns,
                strongRollBossWins = r.strongRollBossWins,
                weakRollTurns = r.weakRollTurns,
                weakRollBossWins = r.weakRollBossWins,
                playerMaxHpEnd = gm?.Run?.playerMaxHP ?? 0,
            };
            _cur.combats.Add(rec);
            _cur.totalCombats++;
            _cur.totalTurns += r.totalTurns;
            if (r.playerWon) _cur.totalWins++;
            // L1学習: ラン全体に加算
            _cur.totalDamageDealt += r.damageDealt;
            _cur.totalDamageTaken += r.damageTaken;
            _cur.totalHealed += r.healApplied;
            _cur.totalShieldGained += r.shieldGained;
            // ヴェスカ段撃破: 7層ボス chain で勝った（含 swap）段を記録
            if (r.playerWon && !string.IsNullOrEmpty(rec.enemyId) && rec.enemyId.StartsWith(GameLoop.BossIds.Layer7Prefix))
                _cur.awakenedFormsKilled.Add(rec.enemyId);
            if (_cur.lastStandUsed)
            {
                _cur.combatsAfterLastStand++;
                if (r.playerWon) _cur.winsAfterLastStand++;
            }
        }

        private void OnStarvation(int dmg)
        {
            if (_cur == null) return;
            _cur.starvationTotal += dmg;
            _cur.starvationHits++;
        }

        private void OnTileActivated(TileType t)
        {
            if (_cur == null) return;
            _cur.tileVisits.TryGetValue(t, out int c);
            _cur.tileVisits[t] = c + 1;

            // 前哨基地: 層別の戦力スナップショットを採る
            if (t == TileType.Outpost) HandleOutpostArrival();
        }

        /// <summary>前哨基地に着いたときの計測。 旅団契約システムは 2026-08-11 に削除されたので、
        /// 層別戦力推移のスナップショットだけが残っている。</summary>
        private void HandleOutpostArrival()
        {
            var run = GameManager.Instance?.Run;
            if (run == null || _cur == null) return;

            int floor = run.currentFloor;
            if (floor > 0 && !_cur.inventoryPowerByFloor.ContainsKey(floor))
                _cur.inventoryPowerByFloor[floor] = InventoryPower.Compute(run);
        }

        // 武器強化で新 Tier に到達した瞬間に L1学習へ記録 (中間Tierの集計漏れ修正)
        private void OnWeaponTierUpgraded(string prevId, string newId)
        {
            if (_cur == null || string.IsNullOrEmpty(newId)) return;
            _cur.acquiredItemsEver.Add(newId);
            _cur.tierUpgradeCount++;
        }

        private void TrackLastStand()
        {
            var run = GameManager.Instance.Run;
            if (run == null || _cur == null) return;
            if (run.lastStandActive && !_cur.lastStandUsed)
            {
                _cur.lastStandUsed = true;
                _cur.lastStandFloor = run.currentFloor;
                _curLog.Enqueue($"[AutoRunner] ラストスタンド発動 (Floor {run.currentFloor})");
            }
        }

        // 〈昇華〉(GameLoop.SublimationSystem) は現状 BOT からも UI からも呼ばれていない。
        //   再導入するなら発動条件から設計すること。

        private void TrackEconomy()
        {
            var run = GameManager.Instance.Run;
            if (run == null || _cur == null) return;
            if (run.coins > _cur.peakCoins) _cur.peakCoins = run.coins;
            if (run.coins > _prevCoins) _cur.totalGoldGained += (run.coins - _prevCoins);
            _prevCoins = run.coins;
            // 素材収入(差分): 増加分だけ累計（昇華コスト逓増カーブ較正用の pt 基準）
            if (run.weaponMaterials > _prevMaterials) _cur.materialsGainedTotal += (run.weaponMaterials - _prevMaterials);
            _prevMaterials = run.weaponMaterials;
            // 希望(ADR-0002): 最低希望と発狂到達を追跡
            if (run.hope < _cur.minHope) _cur.minHope = run.hope;
            if (run.hope <= 0) _cur.reachedMadness = true;
        }

        private string CurrentNodeId()
        {
            return MapManager.Instance?.CurrentNode?.id ?? "";
        }

        // ===== ラン終了処理 =====

        private void FinishClear()
        {
            var run = GameManager.Instance.Run;
            bool full = run != null && run.currentFloor >= run.maxFloor;
            Finish(full ? Outcome.FullClear : Outcome.NormalClear, full ? "完全クリア(7F)" : "通常クリア(5F)");
        }

        private void FinishGameOver()
        {
            var run = GameManager.Instance.Run;
            var cause = DeathCause.Unknown;
            bool bossFight = MapManager.Instance?.CurrentNode?.EffectiveType == TileType.Boss;
            string fatal = "";

            // **_lastPhaseWasCombat は Debug.Log の文字列パースで立つ**が、 バッチ実行では
            // suppressLogsDuringBatch で logEnabled=false にしているためハンドラが呼ばれず、
            // 死因が常に Unknown になっていた (2026-08-04 発覚)。 ログに依存しない判定を先に置く。
            // 直近の戦闘結果が「このランの終端で敗北」なら戦闘死とみなす。
            var lcr = GameManager.Instance.LastCombatResult;
            if (lcr.HasValue && !_lastPhaseWasCombat)
                _lastPhaseWasCombat = !lcr.Value.playerWon || run == null || run.playerHP <= 0;

            if (lcr.HasValue && (_lastPhaseWasCombat))
            {
                if (!lcr.Value.playerWon) cause = DeathCause.CombatLoss;
                else cause = DeathCause.CombatPyrrhic;
                fatal = lcr.Value.enemyDisplayName;
            }
            else if (_recentStarvation)
            {
                cause = DeathCause.Starvation;
            }
            if (_cur != null)
            {
                _cur.deathInBossFight = bossFight;
                _cur.fatalEnemy = fatal;
            }
            Finish(Outcome.GameOver, $"敗北 cause={cause}", cause);
        }

        private bool _lastPhaseWasCombat;
        private bool _recentStarvation;

        private void Finish(Outcome o, string note, DeathCause cause = DeathCause.None)
        {
            if (_cur == null) return;
            var run = GameManager.Instance.Run;
            _cur.outcome = o;
            if (cause != DeathCause.None) _cur.cause = cause;
            _cur.note = note;
            if (run != null)
            {
                _cur.reachedFloor = run.currentFloor;
                _cur.reached6F = run.currentFloor >= 6;
                // 刻限を越えたランだけの到達層分布。 深い側に寄っていれば勝ち筋を
                //   削れており、 浅い側なら「負けるランを罰しているだけ」。
                MetaProgression.MetaDebuffApplicator.NoteRunEnd(run, run.currentFloor, o == Outcome.GameOver);
                // Λ内death (F3): **台帳から導く。** 別途フラグを持つと
                //   「踏み込んだ→離脱した→後の層で死んだ」ランを取り違える。
                //   最後の F 系コードが F1 (踏み込み) のまま死んだ＝狭間から出ていない。
                if (o == Outcome.GameOver && run.chronicle != null)
                {
                    for (int i = run.chronicle.Count - 1; i >= 0; i--)
                    {
                        if (run.chronicle[i][0] != 'F') continue;
                        if (run.chronicle[i].StartsWith(GameLoop.RunChronicle.LambdaEnter))
                            GameLoop.RunChronicle.Lambda(run, GameLoop.RunChronicle.LambdaSink, 0, 0);
                        break;
                    }
                }

                // 行動台帳の締め。 **ラン 1 本につき必ず 1 行** ── G 群の合計が
                //   ラン数と一致しない場合、 どこかの終了経路がここを通っていない。
                GameLoop.RunChronicle.End(run,
                    o == Outcome.GameOver ? GameLoop.RunChronicle.EndDeath
                                          : GameLoop.RunChronicle.EndClear, null);
                _cur.finalHP = run.playerHP;
                _cur.finalMaxHP = run.playerMaxHP;
                _cur.finalCoins = run.coins;
                _cur.finalHope = run.hope;
                _cur.finalHopeCap = run.hopeCap;
                if (run.hope <= 0) _cur.reachedMadness = true;
                _cur.hopeCombatLoss   = GameLoop.HopeSystem.Stats.combatLoss;
                _cur.hopeComposureGain = GameLoop.HopeSystem.Stats.composureGain;
                _cur.hopeLateralLoss  = GameLoop.HopeSystem.Stats.lateralLoss;
                _cur.hopeMarchLoss    = GameLoop.HopeSystem.Stats.marchLoss;
                _cur.hopeEvilLoss     = GameLoop.HopeSystem.Stats.evilLoss;
                _cur.hopeFoodGain     = GameLoop.HopeSystem.Stats.foodGain;
                _cur.hopeRerollLoss   = GameLoop.HopeSystem.Stats.rerollLoss;
                _cur.deathFloor = (o == Outcome.GameOver) ? run.currentFloor : 0;
                _cur.deathInLambda = (o == Outcome.GameOver) && run.inLambda;
                _cur.appliedChallengeScore = MetaProgression.MetaDebuffApplicator.Score;
                _cur.finalInventoryPower = InventoryPower.Compute(run);
                _cur.finalMaterials = run.weaponMaterials;
                _cur.finalUpgradeCost = GameManager.CanSpendMaterials(run)
                    ? GameManager.WeaponUpgradeCost(run) : -1;
                _cur.finalUpgradeReady = _cur.finalUpgradeCost > 0
                    && run.weaponMaterials >= _cur.finalUpgradeCost;
                _cur.finalConsumableIds.Clear();
                if (run.ownedConsumables != null)
                {
                    foreach (var id in run.ownedConsumables)
                    {
                        if (string.IsNullOrEmpty(id)) continue;
                        _cur.finalConsumableIds.Add(id);
                        _cur.finalConsumableCount++;
                        // **接頭辞では分類しない** (2026-09-22) ── items.json の consFamily を読む。
                        switch (GameLoop.ItemIds.ConsFamilyOf(id))
                        {
                            case "heal": _cur.finalHealCount++; break;
                            case "dmg":  _cur.finalDamageBuffCount++; break;
                            case "def":  _cur.finalShieldCount++; break;
                            case "hope": _cur.finalHopeCount++; break;
                        }
                    }
                }
                // 2026-06-23: 最終所持アイテム ID 集合 (保持率計算用)
                _cur.finalOwnedItemIds.Clear();
                if (!string.IsNullOrEmpty(run.equippedWeaponId)) _cur.finalOwnedItemIds.Add(run.equippedWeaponId);
                if (!string.IsNullOrEmpty(run.equippedDiceId)) _cur.finalOwnedItemIds.Add(run.equippedDiceId);
                if (run.ownedPassiveItems != null)
                    foreach (var id in run.ownedPassiveItems) _cur.finalOwnedItemIds.Add(id);
                if (run.ascendedPassiveIds != null)
                    foreach (var id in run.ascendedPassiveIds) _cur.finalOwnedItemIds.Add(id);

                _cur.lambdaFarmTilesUsed = lambdaFarmTiles;
                _cur.lambdaTilesFarmed = run.dimensionalDisturbance;
                if (run.lambdaDebuffs != null)
                    foreach (var kv in run.lambdaDebuffs) _cur.lambdaDebuffLevelSum += kv.Value;
                // Λ内で死亡（未離脱）なら、この時点でファーム獲得量を確定
                if (run.inLambda) RecordLambdaGains(run);

                // L1学習: 最終所持アイテムを acquiredItemsEver に union
                //   ・売却/消費で消えた分は別途 ShopBuy/UseItem 経由で捕捉する設計だが、
                //     現状の最小実装ではラン終了時の最終所持の和をベースラインとする
                //     （AutoRunner は売却を行わず、消費は使用するため、ここでは「使用前/購入時のスナップ」を別途用意）
                if (run.ownedPassiveItems != null)
                    foreach (var id in run.ownedPassiveItems) if (!string.IsNullOrEmpty(id)) _cur.acquiredItemsEver.Add(id);
                if (run.ownedConsumables != null)
                    foreach (var id in run.ownedConsumables) if (!string.IsNullOrEmpty(id)) _cur.acquiredItemsEver.Add(id);
                if (!string.IsNullOrEmpty(run.equippedWeaponId)) _cur.acquiredItemsEver.Add(run.equippedWeaponId);
                // 装備ダイスは 2026-08-17 に廃止 (equippedDiceId はセーブ互換の残置)。
                // **所持した出目パーツを学習へ載せる。** 購入時にも Buy() が積むが、
                //   イベント等ショップ以外の入手経路が付いたときに取りこぼさないよう最終所持でも union する。
                if (run.diceFaceParts != null)
                    foreach (var p in run.diceFaceParts) _cur.acquiredItemsEver.Add(GameLoop.DiceFaceParts.Id(p));
                _cur.finalWeaponTier = run.equippedWeaponId ?? "";
                _cur.finalLimitBreak = run.limitBreakStage;
                _cur.totalCoinsSpent = run.coinsSpent;
                _cur.woundTriggers = run.lingeringWoundTriggers;
                _cur.woundHpLost = run.lingeringWoundLost;
            }
            // 指紋を確定する。 **Classify より前**に置かない ── 分類は指紋に含めない
            //   (帯ラベルの定義を変えただけで指紋が動くと、 決定性の検査にならない)。
            unchecked
            {
                int h = 17;
                h = h * 31 + _cur.reachedFloor;
                h = h * 31 + _cur.finalHP;
                h = h * 31 + _cur.finalMaxHP;
                h = h * 31 + _cur.finalCoins;
                h = h * 31 + _cur.totalCoinsSpent;
                h = h * 31 + _cur.totalCombats;
                h = h * 31 + _cur.totalWins;
                h = h * 31 + _cur.shopPurchases;
                h = h * 31 + _cur.finalInventoryPower;
                h = h * 31 + (int)_cur.outcome;
                _cur.fingerprint = h;
            }
            Classify(_cur);
            _cur.bandScore = ComputeBandScore(_cur);
            // [計装 2026-09-17] **ラン終了時の残金**。 クリア/死亡で分けて数える。
            //   店を出る基準は「リロール価格 + SurplusBuyReserve(30) を切ったら止める」で、
            //   リロール価格は 5→10→20→30 と逓増する ＝ **構造的に数十Gは残る**。
            //   支出を減らす軸 (商才) と現金を配る軸 (金庫) の価値差を読むのに要る ──
            //   終了時に余っているなら、 節約は現金より価値が低くて当然。
            NoteEndGold(run != null ? run.coins : 0, _cur.bandScore >= 11);
            if (_lastShopExitSeen && run != null)
            {
                ShopExitRuns++;
                ShopExitCoinsSum += _lastShopExitCoins;
                PostShopGainSum += run.coins - _lastShopExitCoins;
                // [計装 2026-09-18] **クリア/非クリアで分ける。** 全ラン平均と「クリア時残金」を
                //   並べると母集団が違うのに前後比較に見える (243G → 449G と読まれた)。
                int k = _cur.bandScore >= 11 ? 0 : 1;
                ExitByOutcomeN[k]++; ExitByOutcomeCoins[k] += _lastShopExitCoins;
                ExitByOutcomeGain[k] += run.coins - _lastShopExitCoins;
                ExitByOutcomeFloor[k] += _lastShopExitFloor;
                if (_floor7ShopEntryCoins >= 0) { Floor7EntryN[k]++; Floor7EntryCoins[k] += _floor7ShopEntryCoins; }
            }
            _lastShopExitSeen = false; _lastShopExitCoins = 0; _lastShopExitFloor = 0; _floor7ShopEntryCoins = -1;
            _cur.deterministicDigest = ComputeRunDeterministicDigest(_cur, run);
            var productionRecord = new UltraProductionRunRecord
            {
                runOrdinal = _ultraProductionRuns.Count,
                runIndex = _cur.index,
                scenarioSeed = _currentUltraProductionScenarioSeed,
                scenarioSeedHex = _currentUltraProductionScenarioSeed.ToString("x16"),
                valid = _cur.bandScore >= 0,
                fullClear = _cur.bandScore >= 11,
                crash = _cur.outcome == Outcome.Crash,
                deadlock = _cur.outcome == Outcome.Deadlock,
                fingerprint = _cur.fingerprint,
                deterministicDigest = _cur.deterministicDigest,
            };
            _ultraProductionRuns.Add(productionRecord);
            try { UltraProductionRunCompleted?.Invoke(productionRecord); }
            catch (Exception ex)
            {
                Debug.LogError("[AutoRunner][UltraWorker] run completion callback failed: " + ex.Message);
            }
            // L2ペアテスト記録
            _cur.policyVariant = _currentRunVariant;
            _cur.pairedSeed = _currentRunSeed;
            _records.Add(_cur);
            NoteFamilyTierRunEnd();
            AutoTest.Ultra.UltraDecisionCensus.NoteRunCompleted();
            RecordScreenRunCompletion(_cur);
            _curLog.Enqueue($"[AutoRunner] === RUN {_cur.index} 終了: {_cur.band} ({_cur.bandLabel}) — {note} ===");
            _curAttachLog(_cur);
            _cur = null;
        }

        private static string ComputeRunDeterministicDigest(RunRec rec, GameLoop.RunState run)
        {
            var sb = new StringBuilder(2048);
            sb.Append("ultra-production-run-v1\n");
            sb.Append(rec.index).Append('|').Append((int)rec.outcome).Append('|')
              .Append(rec.reachedFloor).Append('|').Append(rec.finalHP).Append('|')
              .Append(rec.finalMaxHP).Append('|').Append(rec.finalCoins).Append('|')
              .Append(rec.totalCoinsSpent).Append('|').Append(rec.totalCombats).Append('|')
              .Append(rec.totalWins).Append('|').Append(rec.finalInventoryPower).Append('|')
              .Append(rec.appliedChallengeScore).Append('|').Append(rec.fingerprint).Append('\n');
            if (run?.chronicle != null)
            {
                for (int i = 0; i < run.chronicle.Count; i++)
                {
                    string entry = run.chronicle[i] ?? "";
                    sb.Append(entry.Length).Append(':').Append(entry).Append('\n');
                }
            }
            return Sha256Hex(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        private static string Sha256File(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return ToLowerHex(sha.ComputeHash(stream));
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return ToLowerHex(sha.ComputeHash(bytes ?? Array.Empty<byte>()));
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        /// <summary>L1学習用: 帯ラベルから数値スコアを返す。
        /// CRASH=-1, DEADLOCK=-2, R1a..R10/R11 を 1..11 にマップ（先頭文字 R+数字部）。</summary>
        private int ComputeBandScore(RunRec r)
        {
            if (string.IsNullOrEmpty(r.band)) return 0;
            if (r.band == "CRASH") return -1;
            if (r.band == "DEADLOCK") return -2;
            // "R11" → 11, "R8b" → 8 等
            int v = 0; int i = 1;
            while (i < r.band.Length && char.IsDigit(r.band[i])) { v = v * 10 + (r.band[i] - '0'); i++; }
            // 小文字 a/b で 0.5 単位の細分はしないが、6Fクリアは R8b(=8) のまま、5Fクリアは R8(=8) で同点
            // 7層クリア(R11)が最高。死亡 R1a..R10。
            return v;
        }

        private readonly Dictionary<int, List<string>> _detail = new Dictionary<int, List<string>>();
        private void _curAttachLog(RunRec r)
        {
            var list = new List<string>(_curLog);
            _detail[r.index] = list;
        }

        /// <summary>結果を10段階バンド + CRASH/DEADLOCK に分類。
        /// R1:2F以前(道中/ボス問わず) R2:3F道中 R3:3Fボス R4:4F道中 R5:4Fボス
        /// R6:5F道中 R7:5Fボス R8:5Fクリア R9:6Fボスで死亡 R10:6層クリア。</summary>
        private void Classify(RunRec r)
        {
            if (r.outcome == Outcome.Crash) { r.band = "CRASH"; r.bandLabel = "クラッシュ(例外)"; return; }
            if (r.outcome == Outcome.Deadlock) { r.band = "DEADLOCK"; r.bandLabel = "デッドロック"; return; }
            if (r.outcome == Outcome.FullClear)
            {
                r.band = "R11"; r.bandLabel = "Null Pointクリア(完全クリア)";
                return;
            }
            if (r.outcome == Outcome.NormalClear)
            {
                // 6F クリア (〈真理〉未所持で 7F 進入不可) と 5F クリア (〈決意〉未所持で 6F 進入不可) を区別
                if (r.reachedFloor >= 6) { r.band = "R8b"; r.bandLabel = "6Fクリア(真理未所持)"; }
                else                     { r.band = "R8"; r.bandLabel = "5Fクリア(決意未所持)"; }
                return;
            }

            // GameOver
            int f = r.deathFloor;
            bool boss = r.deathInBossFight;
            // **7 層では死なない (2026-09-14)。** 戦闘マスが無く、 門も HP を 0 にしない。
            //   死ぬなら 8 層 Null Point のヴェスカ戦。 7 層が出たら異常なので
            //   同じ帯へ畳まず、 到達したこと自体は R10 として残す。
            if (f >= 7) { r.band = "R10"; r.bandLabel = "Null Pointで死亡(門を通過)"; return; }
            if (f >= 6) { r.band = "R9"; r.bandLabel = "6Fボスで死亡"; return; }
            switch (f)
            {
                case 1:
                    if (boss) { r.band = "R1b"; r.bandLabel = "1Fボスで死亡"; }
                    else      { r.band = "R1a"; r.bandLabel = "1F道中で死亡"; }
                    break;
                case 2:
                    if (boss) { r.band = "R1d"; r.bandLabel = "2Fボスで死亡"; }
                    else      { r.band = "R1c"; r.bandLabel = "2F道中で死亡"; }
                    break;
                case 3:
                    if (boss) { r.band = "R3"; r.bandLabel = "3Fボスで死亡"; }
                    else      { r.band = "R2"; r.bandLabel = "3F道中で死亡"; }
                    break;
                case 4:
                    if (boss) { r.band = "R5"; r.bandLabel = "4Fボスで死亡"; }
                    else      { r.band = "R4"; r.bandLabel = "4F道中で死亡"; }
                    break;
                case 5:
                    if (boss) { r.band = "R7"; r.bandLabel = "5Fボスで死亡"; }
                    else      { r.band = "R6"; r.bandLabel = "5F道中で死亡"; }
                    break;
                default:
                    r.band = "R1a"; r.bandLabel = "1F道中で死亡"; break;
            }
        }

        // ===== ログ購読 =====

        /// <summary>バッチ中のログ抑止を**貫通して必ず出す**ログ。
        ///
        /// 2026-08-11: 各所で `logEnabled = true` に切り替えて出す書き方をしていたが、
        /// 抑止の実装を `filterLogType = Error` に変えた途端に**全部黙った**
        /// (logEnabled を戻しても filterLogType が Log を弾く)。 抑止の掛け方と
        /// 貫通の仕方が 2 箇所に分かれていると、 片方を直したときに必ずこうなる。
        /// **貫通はこの 1 関数だけが知っている**形にして、 二度と分岐させない。</summary>
        private static void LogAlways(string msg, LogType type = LogType.Log)
        {
            var lg = Debug.unityLogger;
            bool pe = lg.logEnabled;
            var pf = lg.filterLogType;
            lg.logEnabled = true;
            lg.filterLogType = LogType.Log;
            if (type == LogType.Warning) Debug.LogWarning(msg);
            else if (type == LogType.Error) Debug.LogError(msg);
            else Debug.Log(msg);
            lg.logEnabled = pe;
            lg.filterLogType = pf;
        }

        private void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception)
            {
                _exceptionFlag = true;
                _exceptionMsg = condition;
            }
            if (_cur == null) return;
            if (condition != null && condition.StartsWith("[GameManager]"))
            {
                AddLog(condition);

                // 死因推定の補助
                if (condition.Contains("Phase:"))
                    _lastPhaseWasCombat = condition.Contains("Combat") || condition.Contains("BattleResult");
                if (condition.Contains("空腹ダメージ"))
                    _recentStarvation = true;
                else if (condition.Contains("Phase:") && !condition.Contains("GameOver"))
                    _recentStarvation = false;
            }
            else if (condition != null && condition.StartsWith("[EventEncounter]"))
            {
                AddLog(condition);
                // 「[EventEncounter] 開始: <名> (id=<id>)」からイベント識別子を保持
                if (condition.Contains("開始:"))
                    _lastEventInfo = condition.Substring(condition.IndexOf("開始:"));
            }
            else if (condition != null && condition.StartsWith("[DBG]"))
            {
                AddLog(condition); // 一時トレース（原因特定後に削除）
            }
            else if (type == LogType.Exception || type == LogType.Error)
            {
                AddLog($"[{type}] {condition}");
            }
        }

        /// <summary>リングバッファ追記。上限超過時は先頭(古い行)を捨て、末尾の終端ログを必ず残す。
        /// Queue による O(1) Dequeue で 10000ラン規模でも線形時間を維持する。</summary>
        private void AddLog(string line)
        {
            if (_curLog.Count >= detailMaxLinesPerRun && _curLog.Count > 0)
                _curLog.Dequeue();
            _curLog.Enqueue(line);
        }

        // ===== 初期化補助 =====

        private void SafeInitDatabases()
        {
            try { var _ = ItemDatabase.Instance; } catch (Exception e) { Debug.LogWarning($"[AutoRunner] ItemDatabase: {e.Message}"); }
            try { EnemyDatabase.EnsureInitialized(); } catch (Exception e) { Debug.LogWarning($"[AutoRunner] EnemyDatabase: {e.Message}"); }
        }

        private void DisableMapTransition()
        {
            try
            {
                var t = MapSystem.Visual.MapTransitionController.Instance;
                if (t != null)
                {
                    Destroy(t.gameObject);
                    Debug.Log("[AutoRunner] MapTransitionController を無効化(同期ショップ遷移)");
                }
            }
            catch { /* 型が無い/未配置なら無視 */ }
        }

        // ===== ログ出力 =====

        private string ResolveAutoRunOutputRoot()
        {
            string root = !string.IsNullOrWhiteSpace(productionOutputRootOverride)
                ? productionOutputRootOverride
                : Path.Combine(Application.dataPath, "..", "AutoRunLogs");
            root = Path.GetFullPath(root);
            Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>残すバッチ出力の件数。 <c>Tools/prune_outputs.py</c> の <c>KEEP_RUNS</c> と揃えること。</summary>
        private const int KeepBatchDirs = 20;

        /// <summary>古い <c>batch_*</c> を刈る (2026-09-22)。
        ///
        /// <para><b>バッチは実行のたびにディレクトリを 1 つ作り、 何も消さなかった。</b>
        /// 2026-09-22 時点で 537 個・862MB。 git 側は .gitignore で除外済みなので、
        /// ここはディスク側の上限を保つ役。 スイープ側は <c>Tools/prune_outputs.py</c> が同じことをする。</para>
        ///
        /// <para>新しさは更新時刻で判定する。 いま作ったディレクトリは必ず残す。
        /// 失敗してもバッチ本体は止めない (刈り込みは副業)。</para></summary>
        private static void PruneOldBatchDirs(string root, string justCreated)
        {
            try
            {
                var dirs = new List<DirectoryInfo>(new DirectoryInfo(root).GetDirectories("batch_*"));
                dirs.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                string keep = Path.GetFullPath(justCreated);
                for (int i = KeepBatchDirs; i < dirs.Count; i++)
                {
                    if (string.Equals(dirs[i].FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    dirs[i].Delete(true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoRunner] 古いバッチ出力の削除に失敗 (続行): {e.Message}");
            }
        }

        private string WriteLogs()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string root = ResolveAutoRunOutputRoot();
            // バッチ命名: プロファイルサフィックスを使用 (旧 cowardly/fullmeta + debuff タグは廃止)
            string profileTag = TierOutputSuffix;
            string dir = Path.Combine(root, $"batch_{stamp}_n{_records.Count}_{profileTag}");
            Directory.CreateDirectory(dir);
            PruneOldBatchDirs(root, dir);

            // L1学習: プロファイル別サブディレクトリに分離して累積
            string learningRoot = TierLearningRoot;
            ItemLearningStats.StatsFile learnStats = null;
            Debug.Log($"[AutoRunner] 学習: ボスチューナー={tuneBosses} / Tier={learnTier} / BOT AI={learnBotAi}");
            if (UpdatesTier)
            {
                try
                {
                    learnStats = ItemLearningStats.IngestBatch(learningRoot, _records);
                    File.WriteAllText(Path.Combine(dir, "ai_stats.json"),
                        ItemLearningStats.BuildAiCompact(learnStats), new UTF8Encoding(false));
                }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] L1学習出力失敗: {e.Message}"); }

                // 回帰用ラン単位生データを永続化 (次バッチ起動時に ItemRegression.Recompute が読む)
                try { RunDataLogger.AppendBatch(learningRoot, _records); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] RunDataLogger 失敗: {e.Message}"); }
            }

            // L1.5: イベント選択肢の bandScore 集計を更新 (AIルーチン側)
            // L2自動探索: 今バッチの bandScore で policy を評価し、 次バッチへ向けて1軸摂動
            if (UpdatesAi)
            {
                try { EventChoiceLearningStats.IngestBatch(learningRoot, _records); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] イベント学習失敗: {e.Message}"); }

                try { PolicyExplorer.AssessAndPropose(_records, learningRoot); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] L2探索失敗: {e.Message}"); }
            }

            // L3: ボス難易度オートチューナー (突破率→目標ファネルへ寄せる)。 他の学習と同時実行可。
            if (BossAutoTune)
            {
                try { BossBalanceTuner.AssessAndAdjust(_records, MetaProfileHelper.CurrentDebuffOn, learningRoot, learnStats?.totalBatches ?? 0); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] L3ボス調整失敗: {e.Message}"); }
            }

            // ファイル検索で最新サマリーを開きやすいよう、 summary に profile + 時刻を埋め込む
            string summaryName = $"summary_{TierOutputSuffix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            _lastSummaryPath = Path.Combine(dir, summaryName);
            File.WriteAllText(_lastSummaryPath,
                BuildSummary() + (learnStats != null ? "\n" + ItemLearningStats.BuildHumanLiftTable(learnStats) : ""),
                new UTF8Encoding(false));
            if (writeRunsJsonl)
                File.WriteAllText(Path.Combine(dir, "runs.jsonl"), BuildJsonl(), new UTF8Encoding(false));
            if (writeDetailLog)
                File.WriteAllText(Path.Combine(dir, "detail.log"), BuildDetail(), new UTF8Encoding(false));

            // バッチ完了直後に Reload。 Tier更新モードのみ BALANCE_TIER_LIST.md を再生成する。
            // AIルーチン学習モードでは Tier表を凍結 (MDを書かない) が、 BOTが読む in-memory の S/A/B は更新しておく。
            try
            {
                LearnedPriorityProvider.Reload(learningRoot, writeMarkdown: UpdatesTier);
                Debug.Log($"[AutoRunner] WriteLogs後 Reload (MD書込={UpdatesTier}): {LearnedPriorityProvider.LastLoadedSummary}");

                // 2026-06-23: InventoryPower 系ブロックを Tier 表 MD に追記 (Tier更新モードのみ)
                AppendInventoryPowerBlocksToTierList();
            }
            catch (Exception e) { Debug.LogWarning($"[AutoRunner] WriteLogs後 Reload失敗: {e.Message}"); }

            return dir;
        }

        /// <summary>2026-06-23: Power 系ブロック (層別 + Item別寄与) を BALANCE_TIER_LIST_*.md に追記。
        /// 既存の Power セクションが残っていれば置換 (重複防止)。 Tier更新モード/AI学習モード双方で実行。</summary>
        private void AppendInventoryPowerBlocksToTierList()
        {
            try
            {
                if (_records == null || _records.Count == 0)
                {
                    Debug.Log("[AutoRunner] _records 空のため Power 追記スキップ");
                    return;
                }
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                    $"BALANCE_TIER_LIST_{TierOutputSuffix}.md"));
                if (!File.Exists(path))
                {
                    Debug.Log($"[AutoRunner] Tier表MD未生成のため Power 追記スキップ: {path}");
                    return;
                }

                // 既存 Power セクションを除去 (重複防止) ── マーカーは "# インベントリパワー指標"
                string content = File.ReadAllText(path, new UTF8Encoding(false));
                const string marker = "# インベントリパワー指標";
                int markerIdx = content.IndexOf(marker);
                if (markerIdx >= 0)
                {
                    // マーカーの直前にある "---" 区切り (前後の空行込み) も除去したい。 簡易処理: マーカーから -8 文字程度前を捜す。
                    int trimFrom = markerIdx;
                    int sepIdx = content.LastIndexOf("\n---", markerIdx);
                    if (sepIdx > 0 && markerIdx - sepIdx < 20) trimFrom = sepIdx;
                    content = content.Substring(0, trimFrom).TrimEnd('\r', '\n', ' ');
                }

                var sb = new StringBuilder();
                sb.Append(content);
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("# インベントリパワー指標 (Tier 表ベース戦力)");
                sb.AppendLine();
                sb.AppendLine($"> 集計範囲: 本バッチ {_records.Count} ラン");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.Append(BuildInventoryPowerBlock());
                sb.AppendLine();
                sb.Append(BuildItemPowerContributionBlock());
                sb.AppendLine();
                sb.Append(BuildPickRetentionBlock());
                sb.AppendLine("```");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Debug.Log($"[AutoRunner] Power ブロックを Tier 表MDに反映: {path} (置換={markerIdx >= 0})");
            }
            catch (Exception e) { Debug.LogWarning($"[AutoRunner] Power ブロック追記失敗: {e.Message}\n{e.StackTrace}"); }
        }

        private string Pct(int n, int total) =>
            total == 0 ? "0.0%" : (100.0 * n / total).ToString("F1", CultureInfo.InvariantCulture) + "%";

        /// <summary>同一ファイル内で、行動ルーチン別（戦闘貪欲/戦闘回避）に
        /// 集計ブロックを分離して出力する。各ブロックは自己完結の全集計。</summary>
        private string BuildSummary()
        {
            var greedy = _records.FindAll(r => r.profile == "貪欲");
            var averse = _records.FindAll(r => r.profile == "回避");
            var other  = _records.FindAll(r => r.profile != "貪欲" && r.profile != "回避");

            var sb = new StringBuilder();
            string metaLabel = metaPattern switch
            {
                MetaPattern.Cowardly        => "臆病(メタ全リセット)",
                MetaPattern.FullProgression => $"全有効化(メタLv{MetaProgression.MetaBuffTrack.TotalSteps})",
                MetaPattern.Untouched       => "保存値そのまま",
                _                           => metaPattern.ToString(),
            };
            sb.AppendLine("################ AutoRun サマリ ################");
            sb.AppendLine($"日時      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"メタ進行  : {metaLabel}");
            sb.AppendLine($"メタデバフ: {(enableAllDebuffs ? "全ON (Lv1-10, 最高難易度)" : "全OFF")}");
            sb.AppendLine($"実行設定  : メタ={metaBuffMode} / 軸={(sweepAllMetaAxes ? "全軸走査" : metaAllocation.ToString())} / アイテム={itemPickMode}");
            sb.AppendLine($"配線AI    : {wiringSkill}");
            sb.AppendLine($"戦闘経路  : {(useMutualAttackPipeline ? "ADR-0009 相互攻撃" : "旧戦闘経路")} / 役=ADR-0010");
            // **実効状態を必ず印字する。** EditorPrefs 由来のトグルは黙って持ち越されるので、
            //   サマリを後から読んだときに条件が復元できないと測定が丸ごと無駄になる。
            //   (技量帯が別メニューの設定を持ち越したまま走った事故が実際に起きている)
            sb.AppendLine($"5層裏ボス : {(suppressLayer5HiddenBoss ? "遮断" : "出現あり")}"
                        + $" / 軽減無視の盾肩代わり: {(shieldAbsorbsUnmitigable ? "あり (製品挙動)" : "**なし (対照群)**")}");
            // **結果を変えるトグルを 1 行にまとめて必ず出す (2026-08-18)。**
            //   Ultra の portfolio で、 worker 側が既定値のまま走り本ラン側は EditorPrefs 継承、
            //   という不一致が起きた。 どちらも「正常に進行中」と表示され、 4 候補が同一挙動に
            //   なっていたことに **1000ラン×4 を回し終えるまで誰も気付かなかった**。
            //   個別の行から推測させる形 (パーツ抑止は「平均面数」から逆算する等) では
            //   後から条件を突き合わせられない。 **値そのものを書く。**
            if (!string.IsNullOrEmpty(_ultraResumeLine))
                sb.AppendLine("Ultra resume: " + _ultraResumeLine);
            // Ultra の委譲が実際に効いたか。 **落ちても run は完走するので、
            //   印字しないと「一度も効いていない Ultra」を成果と取り違える。**
            if (AutoTest.Ultra.UltraDispatchStats.Offered > 0
                || wiringSkill == WiringSkill.Ultra)
                sb.AppendLine("Ultra委譲  : " + AutoTest.Ultra.UltraDispatchStats.Describe()
                    + (string.IsNullOrEmpty(_ultraLastFallbackReason)
                        ? "" : "  直近の落ちた理由=" + _ultraLastFallbackReason));
            if (_ultraThinkingSecondsTotal > 0f)
            {
                sb.AppendLine($"Ultra思考  : 計 {_ultraThinkingSecondsTotal / 60f:F1}分"
                    + $" ({_ultraThinkingSecondsTotal / Math.Max(1, _records.Count):F1}秒/ラン)"
                    + "  ※番犬からは控除済み");
                // **上書き率だけでは強さを読めない。** 全手0クリアの決定では 2 手の下限が
                //   どちらも 0.000 の同点になり、 選ばれるのは「評価で勝った手」ではなく
                //   「id が辞書順で先の手」。 差がついた件数を必ず並べて出す。
                sb.AppendLine("Ultra評価  : " + AutoTest.Ultra.UltraRolloutEvaluator.DescribeCounters());
            }
            sb.AppendLine($"実効トグル: 相互攻撃={useMutualAttackPipeline}"
                        + $" / パーツ陳列抑止={suppressFacePartOffers}"
                        + $" / 盾肩代わり={shieldAbsorbsUnmitigable}"
                        + $" / メタデバフ全ON={enableAllDebuffs}"
                        + $" / 全軸走査={sweepAllMetaAxes}"
                        + $" / 購入序列={(useIttBeta ? $"ITT(生存N={GrantItt.SurvivalN}/{LearnedPriorityProvider.IttMode})" : "観測regβ")}"
                        + $" / 挑戦spec={(string.IsNullOrEmpty(challengeSpec) ? "(空=スコアから導出)" : challengeSpec)}");
            // 出目パーツの評価係数。 **掃引中はこの行が唯一の「どの値で回したか」の記録**。
            sb.AppendLine($"出目パーツ: 較正={AutoTest.InventoryPower.PowerCalibration:F2}"
                        + $" / T1={AutoTest.InventoryPower.TierGainT1:F1}"
                        + $" T3×={AutoTest.InventoryPower.TierGainT3:F2}"
                        + $" T4+={AutoTest.InventoryPower.TierGainT4Extra:F1}");
            bool validSeed = GameLoop.GameRng.TryParseSeed(masterSeed, out ulong parsedSeed);
            sb.AppendLine($"決定seed  : {(validSeed ? GameLoop.GameRng.FormatSeed(parsedSeed) : string.IsNullOrWhiteSpace(masterSeed) ? "なし（ランダム）" : $"不正 '{masterSeed}'（ランダムへフォールバック）")}");
            sb.AppendLine($"総ラン数  : {_records.Count}  (戦闘貪欲={greedy.Count} / 戦闘回避={averse.Count})");
            sb.AppendLine(ArmCompositionLine());
            sb.AppendLine(PrimaryObjectiveLine());
            sb.Append(AutoTest.CritRateCensus.Describe());
            // **実効状態を通常バッチでも必ず印字する (2026-08-17)。**
            //   従来はスイープのレポートにしか出しておらず、 通常バッチのサマリーからは
            //   遺物の有無が読めなかった。 `_pendingRelicExplicit` が false のとき
            //   遺物は **「無し」ではなく「触らない」** ＝ 前の状態を引き継ぐ構造なので、
            //   印字が無いと「遺物なしのつもりだった」を後から検証できない。
            //   2026-08-10 に同じ穴で 0pt 基準値が 53.3% / 16.6% に割れている。
            sb.AppendLine(EffectiveStateLine());
            sb.AppendLine("比較軸    : 航行Rankのみ差し替え（消費/ショップ/戦闘実行/イベントは共通固定）");
            sb.AppendLine("################################################");
            if (AutoTest.Ultra.UltraDecisionCensus.Enabled
                && AutoTest.Ultra.UltraDecisionCensus.Runs > 0)
            {
                sb.AppendLine();
                sb.Append(AutoTest.Ultra.UltraDecisionCensus.Report());
            }
            sb.AppendLine();
            sb.Append(BuildPersonaBlock());
            sb.AppendLine();
            sb.Append(BuildMetaAxisBlock());
            sb.AppendLine();
            sb.Append(BuildLambdaFarmBlock());
            sb.AppendLine();
            sb.Append(BuildWeaponProgressionBlock());
            sb.AppendLine();
            sb.Append(BuildBossWinRateBlock());
            sb.AppendLine();
            sb.Append(BuildRoleBlock());
            sb.AppendLine();
            sb.Append(BuildTensionCurveBlock());
            sb.AppendLine();
            sb.Append(BuildBuildDiversityBlock());
            sb.AppendLine();
            sb.Append(BuildDeathCauseQualityBlock());
            sb.AppendLine();
            sb.Append(BuildDecisionWeightBlock());
            sb.AppendLine();
            sb.Append(BuildDeathHoardingBlock());
            sb.AppendLine();
            // 2026-06-23: InventoryPower 系ブロックは BALANCE_TIER_LIST_*.md に移植 (summary から削除)
            sb.Append(BuildSummaryBlock("【前半 50% ─ 戦闘貪欲（戦闘マスを最優先で選ぶ）】", greedy));
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(BuildSummaryBlock("【後半 50% ─ 戦闘回避（戦闘以外があれば必ず回避）】", averse));
            if (other.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append(BuildSummaryBlock("【プロファイル未設定（保険）】", other));
            }
            return sb.ToString();
        }

        /// <summary>死亡時に即使用できる資源を残していた割合。
        /// Gold は戦闘中に使えないため抱え落ち本体から分離し、経済余剰として併記する。</summary>
        private string BuildDeathHoardingBlock()
        {
            var deaths = _records.FindAll(r => r.outcome == Outcome.GameOver);
            var pre6 = deaths.FindAll(r => r.deathFloor < 6);
            var floor5 = deaths.FindAll(r => r.deathFloor == 5);
            var floor6Plus = deaths.FindAll(r => r.deathFloor >= 6);
            var sb = new StringBuilder();
            sb.AppendLine("【全死亡・抱え落ち率】");
            sb.AppendLine("  抱え落ち = 死亡時に戦闘使用可能な 回復/与ダメ油/盾 を1個以上所持。Goldは別集計。");

            void Append(string label, List<RunRec> rows)
            {
                int n = rows.Count;
                int any = 0, heal = 0, dmg = 0, shield = 0, hope = 0;
                int gold10 = 0, gold30 = 0, upgrade = 0;
                long consSum = 0, goldSum = 0, matSum = 0;
                foreach (var r in rows)
                {
                    if (r.finalHealCount + r.finalDamageBuffCount + r.finalShieldCount > 0) any++;
                    if (r.finalHealCount > 0) heal++;
                    if (r.finalDamageBuffCount > 0) dmg++;
                    if (r.finalShieldCount > 0) shield++;
                    if (r.finalHopeCount > 0) hope++;
                    if (r.finalCoins >= 10) gold10++;
                    if (r.finalCoins >= 30) gold30++;
                    if (r.finalUpgradeReady) upgrade++;
                    consSum += r.finalConsumableCount;
                    goldSum += r.finalCoins;
                    matSum += r.finalMaterials;
                }
                sb.AppendLine($"  {label,-12}: {n,4}死 / 戦闘資源あり {Pct(any,n),6}"
                    + $" (回復{Pct(heal,n)}, 油{Pct(dmg,n)}, 盾{Pct(shield,n)})"
                    + $" / 希望薬{Pct(hope,n)} / 強化可能{Pct(upgrade,n)}");
                sb.AppendLine($"  {"",-12}  平均残: 消耗品{(n > 0 ? consSum/(double)n : 0):F2}個"
                    + $" Gold{(n > 0 ? goldSum/(double)n : 0):F1} (10+:{Pct(gold10,n)}, 30+:{Pct(gold30,n)})"
                    + $" 素材{(n > 0 ? matSum/(double)n : 0):F1}");
            }

            Append("全死亡", deaths);
            Append("6層未突入", pre6);
            Append("5層内死亡", floor5);
            Append("6層以降死亡", floor6Plus);
            return sb.ToString();
        }

        /// <summary>役とリロールの実測 (ADR-0010 Verification ④)。
        ///
        /// **数値を調整する前に「効いているか」を数える**。 〈渇き〉の発動率が 6.6% しか
        /// なかったことに、 3 回も数値をいじってから気づいた反省による (§24)。
        /// 実測 0% に近い役があればそれは設計上存在しないのと同じ。</summary>
        private string BuildRoleBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【役の実測 (ADR-0010)】");
            sb.AppendLine(CombatSystem.YachtRoleEffects.DescribeStats());
            sb.Append(CombatSystem.YachtRoleEffects.DescribeYachtFaces());
            sb.AppendLine(CombatSystem.YachtRoleEffects.DescribeFireTiming());
            // **計装はサマリに書く。** メモリ上の static を実行後に読む方式だと
            //   ヘッドレス実行 (プロセスが終わると静的変数も消える) で採れなくなる。
            {
                // 充電経済。 **リロールが安いのか、 財布が溢れているのか**を分ける。
                var C = typeof(InventorySystem.PassiveSkills.CombatContext);
                long g = InventorySystem.PassiveSkills.CombatContext.ChargeGained;
                long w = InventorySystem.PassiveSkills.CombatContext.ChargeWasted;
                long s = InventorySystem.PassiveSkills.CombatContext.ChargeSpentTotal;
                long rrC = CombatSystem.CombatManager.RerollStats[2];
                double t = System.Math.Max(1, CombatSystem.YachtRoleEffects.TurnsEvaluated);
                sb.Append(AutoTest.FamilyTierStats.Dump());
                sb.AppendLine("【充電経済】");
                sb.AppendLine($"  収入 {g:N0} (1ターン {g / t:F2}) / 上限で破棄 {w:N0} (1ターン {w / t:F2}"
                            + $" ・収入比 {(g + w > 0 ? 100.0 * w / (g + w) : 0):F1}%)");
                sb.AppendLine($"  支出 {s:N0} (1ターン {s / t:F2}) / うちリロール {rrC:N0}"
                            + $" ({(s > 0 ? 100.0 * rrC / s : 0):F1}%)");
                sb.AppendLine($"  収支 {(g - s):N0} (1ターン {(g - s) / t:F2})  ※プラスなら使い切れていない");
                long ud = CombatSystem.CombatManager.PassiveUpkeepDue;
                long up = CombatSystem.CombatManager.PassiveUpkeepPaid;
                sb.AppendLine($"  装備維持費 請求 {ud:N0} (1ターン {ud / t:F2}) / 実収 {up:N0}"
                            + $" (踏み倒し {(ud > 0 ? 100.0 * (ud - up) / ud : 0):F1}%)");

                {
                    long fc = CombatSystem.CombatManager.FirstRollCount;
                    long fy = CombatSystem.CombatManager.FirstRollYacht;
                    sb.AppendLine("  初回ロール (振り直し前)");
                    sb.AppendLine($"    回数 {fc:N0} / 5個同値 {fy:N0} = {(fc > 0 ? 100.0 * fy / fc : 0):F3}%"
                                + "   ※5個の d6 の理論値 0.077%");
                    sb.AppendLine($"    平均ダイス本数 {(fc > 0 ? CombatSystem.CombatManager.FirstRollDiceCountSum / (double)fc : 0):F2}"
                                + $" / 平均面数 {(fc > 0 ? CombatSystem.CombatManager.FirstRollFaceLenSum / (double)fc : 0):F2}"
                                + $" / 面配列なし {CombatSystem.CombatManager.FirstRollNoFaces:N0}");
                    var bc = CombatSystem.CombatManager.FirstRollByDiceCount;
                    var by = CombatSystem.CombatManager.FirstRollYachtByDiceCount;
                    for (int i = 0; i < bc.Length; i++)
                        if (bc[i] > 0)
                            sb.AppendLine($"    {i}本: {bc[i],9} 回 / 極 {by[i],7} = {(100.0 * by[i] / bc[i]),6:F3}%");
                    var fh = CombatSystem.CombatManager.FirstRollFaceHist;
                    long fhTot = 0; foreach (var v in fh) fhTot += v;
                    sb.Append("    出目分布:");
                    for (int i = 0; i < fh.Length; i++)
                        if (fh[i] > 0) sb.Append($" {i}={(fhTot > 0 ? 100.0 * fh[i] / fhTot : 0):F1}%");
                    sb.AppendLine();
                }
                var rpt = CombatSystem.CombatManager.RerollPerTurn;
                var rpy = CombatSystem.CombatManager.RerollPerTurnYacht;
                long rptTot = 0, rpyTot = 0;
                for (int i = 0; i < rpt.Length; i++) { rptTot += rpt[i]; rpyTot += rpy[i]; }
                sb.AppendLine("  リロール回数の分布 (全ターン / 〈極〉成立ターン)");
                for (int i = 0; i < rpt.Length; i++)
                {
                    if (rpt[i] == 0 && rpy[i] == 0) continue;
                    sb.AppendLine($"    {i}{(i == 8 ? "回以上" : "回    ")} {rpt[i],9} ({(rptTot > 0 ? 100.0 * rpt[i] / rptTot : 0),5:F1}%)"
                                + $"  |  {rpy[i],7} ({(rpyTot > 0 ? 100.0 * rpy[i] / rpyTot : 0),5:F1}%)");
                }
            }
            sb.AppendLine(CombatSystem.YachtRoleEffects.DescribeByDice(_records.Count));
            sb.AppendLine(GameLoop.RunChronicle.DescribeRest());
            sb.AppendLine(GameLoop.RunChronicle.DescribeShieldSplit());
            sb.AppendLine(GameLoop.RunChronicle.DescribeShieldBash());
            sb.AppendLine(GameLoop.RunChronicle.DescribeP4());
            sb.AppendLine(GameLoop.RunChronicle.DescribePhaseDuration());
            sb.AppendLine(GameLoop.RunChronicle.DescribePhaseWiring());
            var rr = CombatSystem.CombatManager.RerollStats;
            sb.AppendLine($"  リロール: {rr[0]:N0} 回 / {rr[1]:N0} 個 / 充電 {rr[2]:N0}"
                        + (rr[0] > 0 ? $"  (1回あたり {rr[1] / (double)rr[0]:F2}個 / {rr[2] / (double)rr[0]:F2}充電)" : ""));
            // 先読みが順位を変えているか。 Super 以外の技量では 0 件なので何も出ない。
            sb.Append(SuperCombatAI.DescribeSearch());
            sb.Append(DescribeNavTies());
            sb.Append(DescribeDamageBreakdown7F());
            return sb.ToString();
        }

        /// <summary>ビルド軸ペルソナ別の勝率・平均ラン深度を集計 (2026-07-15 追加)。
        /// useBuildPersonas=false の場合は「【ペルソナ】使用なし」だけ 1 行出して終わる。</summary>
        private string BuildPersonaBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ビルド軸ペルソナ別成績】");
            if (!useBuildPersonas)
            {
                sb.AppendLine("  ペルソナ機能: OFF (全ラン RawTier = Tier表信奉)");
                return sb.ToString();
            }
            sb.AppendLine($"  ペルソナ機能: ON (RawTier率={rawTierRatio:P0}・非RawTier9種から等確率)");
            sb.AppendLine($"  {"ペルソナ",-10} | {"ラン数",5} | {"クリア率",7} | {"6F到達",7} | {"平均R帯",7}");
            var byPersona = new Dictionary<string, List<RunRec>>();
            foreach (var r in _records)
            {
                string key = string.IsNullOrEmpty(r.persona) ? "-" : r.persona;
                if (!byPersona.TryGetValue(key, out var list)) { list = new List<RunRec>(); byPersona[key] = list; }
                list.Add(r);
            }
            foreach (var kv in byPersona)
            {
                var recs = kv.Value;
                if (recs.Count == 0) continue;
                int cleared = 0, reach6 = 0;
                float bandSum = 0f; int bandN = 0;
                foreach (var r in recs)
                {
                    if (r.band != null && r.band.StartsWith("R"))
                    {
                        // 数字部分のみ抽出 ("R8b" 等の接尾辞付きも拾う。旧 TryParse は R8b をパース失敗で全集計から除外していた)
                        int rn = 0, ci = 1;
                        while (ci < r.band.Length && char.IsDigit(r.band[ci])) { rn = rn * 10 + (r.band[ci] - '0'); ci++; }
                        if (rn > 0)
                        { bandSum += rn; bandN++; if (rn >= 11) cleared++; if (rn >= 7) reach6++; } // R11=7層クリア
                    }
                }
                float clr = recs.Count > 0 ? (float)cleared / recs.Count * 100f : 0f;
                float r6  = recs.Count > 0 ? (float)reach6 / recs.Count * 100f : 0f;
                float avgB = bandN > 0 ? bandSum / bandN : 0f;
                sb.AppendLine($"  {kv.Key,-10} | {recs.Count,5} | {clr,6:F1}% | {r6,6:F1}% | R{avgB,5:F1}");
            }
            return sb.ToString();
        }

        /// <summary>メタバフ配分 (整備パネル v6・36pt) 別の成績。
        /// 一斉走査 (sweepAllMetaAxes) 時はラン単位でラウンドロビンしているので、
        /// ここが **軸同士の直接比較表** になる。 単一軸/標準/オフ時は 1 行だけ出る。</summary>
        private string BuildMetaAxisBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【メタバフ配分別成績】");
            if (metaBuffMode == MetaBuffMode.Off)
            {
                sb.AppendLine("  メタバフ: OFF (0pt = 新規プレイヤーの床)");
                return sb.ToString();
            }
            sb.AppendLine(sweepAllMetaAxes && metaBuffMode == MetaBuffMode.BuildFocused
                ? $"  一斉走査: ON ({SweepAxes.Length} 軸をラン単位でラウンドロビン)"
                : $"  単一配分: {metaAllocation}");
            sb.AppendLine($"  {"配分",-14} | {"ラン数",5} | {"クリア率",7} | {"6F到達",7} | {"平均R帯",7}");

            var byAxis = new Dictionary<string, List<RunRec>>();
            foreach (var r in _records)
            {
                string key = string.IsNullOrEmpty(r.metaAxis) ? "-" : r.metaAxis;
                if (!byAxis.TryGetValue(key, out var list)) { list = new List<RunRec>(); byAxis[key] = list; }
                list.Add(r);
            }
            foreach (var kv in byAxis)
            {
                var recs = kv.Value;
                int cleared = 0, reach6 = 0;
                float bandSum = 0f; int bandN = 0;
                foreach (var r in recs)
                {
                    if (r.band == null || !r.band.StartsWith("R")) continue;
                    int rn = 0, ci = 1;
                    while (ci < r.band.Length && char.IsDigit(r.band[ci])) { rn = rn * 10 + (r.band[ci] - '0'); ci++; }
                    if (rn > 0)
                    { bandSum += rn; bandN++; if (rn >= 11) cleared++; if (rn >= 7) reach6++; }
                }
                float clr  = recs.Count > 0 ? (float)cleared / recs.Count * 100f : 0f;
                float r6   = recs.Count > 0 ? (float)reach6 / recs.Count * 100f : 0f;
                float avgB = bandN > 0 ? bandSum / bandN : 0f;
                sb.AppendLine($"  {kv.Key,-14} | {recs.Count,5} | {clr,6:F1}% | {r6,6:F1}% | R{avgB,5:F1}");
            }
            return sb.ToString();
        }

        /// <summary>ボス別の戦闘勝率ブロック。 1〜6層 + 5裏 + 7層各形態を遭遇順に並べ、
        /// 勝率/遭遇数/平均ターン/ロール勝率/主死因を出す (難易度調整の確認用)。</summary>
        private string BuildBossWinRateBlock()
        {
            // enemyId 別に集計
            var agg = new Dictionary<string, BossWinAgg>();
            foreach (var r in _records)
            {
                if (r?.combats == null) continue;
                foreach (var c in r.combats)
                {
                    if (c == null || string.IsNullOrEmpty(c.enemyId) || !BossTuning.IsBoss(c.enemyId)) continue;
                    if (!agg.TryGetValue(c.enemyId, out var a)) { a = new BossWinAgg(); agg[c.enemyId] = a; }
                    a.enc++;
                    if (c.won) a.wins++;
                    a.tWin += c.tWin; a.tLoss += c.tLoss; a.tDraw += c.tDraw; a.turns += c.turns;
                    if (c.won) a.turnsOnWin += c.turns; else a.turnsOnLoss += c.turns;
                    if (!c.won)
                    {
                        string dc = c.deathCause.ToString();
                        a.causes[dc] = a.causes.TryGetValue(dc, out int n) ? n + 1 : 1;
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("【ボス別 戦闘勝率】");
            if (agg.Count == 0) { sb.AppendLine("  (ボス戦の記録なし)"); return sb.ToString(); }

            // 表示順: 1,2,3,4,5,5裏,6, 7層各形態(p1..p7), その他
            string[] ordered = {
                "boss_layer1", "boss_layer2", "boss_layer3", "boss_layer4",
                "boss_layer5", "boss_layer5_hidden", "boss_layer6",
                "boss_layer7", "boss_layer7_p2", "boss_layer7_p3", "boss_layer7_p4",
                "boss_layer7_p5", "boss_layer7_p6", "boss_layer7_p7",
            };
            var shown = new HashSet<string>();
            foreach (var id in ordered)
                if (agg.TryGetValue(id, out var a)) { sb.AppendLine(FormatBossRow(BossLabel(id), a)); shown.Add(id); }
            // 既知順に無い未知ボスを末尾に追加
            foreach (var kv in agg)
                if (!shown.Contains(kv.Key)) sb.AppendLine(FormatBossRow(BossLabel(kv.Key), kv.Value));

            return sb.ToString();
        }

        private string BuildInventoryPowerBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験⑤ インベントリパワー (Tier 表ベース戦力)】");
            sb.AppendLine("  Power = 装備武器 Tier 係数 (T1=1/T2=3/T3=6/T4=10) + 所持品 Tier スコア合算 (S=4/A=3/B=2/C=1/D-E=0)");
            sb.AppendLine("  装備武器・装備ダイス・所持パッシブ・昇華済みパッシブを集計 (同名 1 個 dedup)");
            sb.AppendLine();

            int n = _records.Count;
            if (n == 0) { sb.AppendLine("  (データなし)"); return sb.ToString(); }

            // 層別平均
            var floorPow = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                var r = _records[i];
                if (r?.inventoryPowerByFloor == null) continue;
                foreach (var kv in r.inventoryPowerByFloor)
                {
                    if (!floorPow.TryGetValue(kv.Key, out var list)) { list = new List<int>(); floorPow[kv.Key] = list; }
                    list.Add(kv.Value);
                }
            }

            var floors = new List<int>(floorPow.Keys);
            floors.Sort();
            sb.AppendLine($"  {PadR("層", 4)}{PadR("ラン数", 7)}{PadR("平均Power", 10)}{PadR("中央値", 8)}{PadR("最大", 6)}");
            foreach (int f in floors)
            {
                var list = floorPow[f];
                if (list.Count == 0) continue;
                long sum = 0; int max = 0;
                for (int j = 0; j < list.Count; j++) { sum += list[j]; if (list[j] > max) max = list[j]; }
                float avg = (float)sum / list.Count;
                list.Sort();
                int median = list[list.Count / 2];
                sb.AppendLine($"  {PadR(f.ToString()+"F", 4)}{PadR(list.Count.ToString(), 7)}{PadR(avg.ToString("F1"), 10)}{PadR(median.ToString(), 8)}{PadR(max.ToString(), 6)}");
            }

            // ラン終了時の Power 分布
            long finSum = 0; int finMax = 0, finCnt = 0;
            var finList = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                int p = _records[i].finalInventoryPower;
                finList.Add(p);
                finSum += p; finCnt++; if (p > finMax) finMax = p;
            }
            finList.Sort();
            float finAvg = finCnt > 0 ? (float)finSum / finCnt : 0f;
            int finMed = finCnt > 0 ? finList[finCnt / 2] : 0;
            sb.AppendLine();
            sb.AppendLine($"  ラン終了時 Power: 平均 {finAvg:F1} / 中央値 {finMed} / 最大 {finMax}");
            sb.AppendLine($"  ラン終了時 帯分布: " + ComputePowerBandDistribution(finList));

            // 6F 到達時 vs 死亡時の Power 比較
            long aliveSum = 0, deadSum = 0; int aliveN = 0, deadN = 0;
            for (int i = 0; i < n; i++)
            {
                var r = _records[i];
                if (r.reached6F) { aliveSum += r.finalInventoryPower; aliveN++; }
                else { deadSum += r.finalInventoryPower; deadN++; }
            }
            if (aliveN > 0) sb.AppendLine($"  6F 到達ラン平均 Power: {(float)aliveSum / aliveN:F1} ({aliveN} ラン)");
            if (deadN > 0) sb.AppendLine($"  5F 以下死亡ラン平均 Power: {(float)deadSum / deadN:F1} ({deadN} ラン)");
            sb.AppendLine("  → 差が大きいほど Power が突破力の予測子として有効");

            // Power 別 6F 到達率テーブル (5F snapshot をキーに 6F到達率を予測)
            sb.AppendLine();
            sb.AppendLine("  ── Power 別 6F 到達率予測 (5F snapshot 基準) ──");
            // 帯: [0,20), [20,40), [40,60), [60,80), [80,100), [100,+∞)
            int[] bandLowers = { 0, 20, 40, 60, 80, 100 };
            int bands = bandLowers.Length;
            int[] bandTotal = new int[bands];
            int[] bandReached6 = new int[bands];
            for (int i = 0; i < n; i++)
            {
                var r = _records[i];
                if (r == null || r.inventoryPowerByFloor == null) continue;
                if (!r.inventoryPowerByFloor.TryGetValue(5, out int p5)) continue;
                int b = bands - 1;
                for (int k = 0; k < bands - 1; k++)
                    if (p5 < bandLowers[k + 1]) { b = k; break; }
                bandTotal[b]++;
                if (r.reached6F) bandReached6[b]++;
            }
            sb.AppendLine($"  {PadR("Power 帯", 14)}{PadR("ラン数", 7)}{PadR("6F到達", 7)}{PadR("到達率", 8)}");
            for (int b = 0; b < bands; b++)
            {
                if (bandTotal[b] == 0) continue;
                string label = (b == bands - 1)
                    ? $"{bandLowers[b]}+"
                    : $"{bandLowers[b]}-{bandLowers[b + 1] - 1}";
                float rate = 100f * bandReached6[b] / bandTotal[b];
                sb.AppendLine($"  {PadR(label, 14)}{PadR(bandTotal[b].ToString(), 7)}{PadR(bandReached6[b].ToString(), 7)}{PadR(rate.ToString("F1") + "%", 8)}");
            }
            return sb.ToString();
        }

        /// <summary>⑥ アイテム別 Power 寄与 (2026-06-22 Phase a)。
        /// 各アイテム ID について、 取得ランと非取得ランの finalInventoryPower 差分を出す。
        /// 2026-06-23 案 A: 「ΔPower 提示時」 (offered cohort) を併記。
        ///   ΔPower 全体は selection bias が大きい (非取得側 = 早期死亡で店に辿り着けなかった群が混入)。
        ///   提示時 ΔPower はその品を offerd された run 限定で比較 → 真の貢献に近い。</summary>
        private string BuildItemPowerContributionBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験⑥ アイテム別 Power 寄与 (上位 30、 提示時 ΔPower 降順)】");
            sb.AppendLine("  ΔPower全体 = avg(finalPower | 取得) − avg(finalPower | 非取得)  ← selection bias 込み");
            sb.AppendLine("  ΔPower提示 = avg(finalPower | 取得 ∧ 提示) − avg(finalPower | 非取得 ∧ 提示)  ← bias 緩和");
            sb.AppendLine("  (取得ラン≥30 のみ表示、 ExcludedFromLift 除外)");
            sb.AppendLine();

            int n = _records.Count;
            if (n == 0) { sb.AppendLine("  (データなし)"); return sb.ToString(); }

            // 全アイテム ID 集合
            var allIds = new HashSet<string>();
            for (int i = 0; i < n; i++)
            {
                var r = _records[i];
                if (r?.acquiredItemsEver != null)
                    foreach (var id in r.acquiredItemsEver) allIds.Add(id);
            }

            // 各 ID の Power 寄与を計算 (全体 / 提示時 の 2 系統)
            var rows = new List<(string id, int acqN, int offeredN, float deltaAll, float deltaOffered)>();
            foreach (var id in allIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (ItemLearningStats.ExcludedFromLift.Contains(id)) continue;
                long acqSum = 0, noacqSum = 0;
                int acqN = 0, noacqN = 0;
                long offAcqSum = 0, offNoacqSum = 0;
                int offAcqN = 0, offNoacqN = 0;
                for (int i = 0; i < n; i++)
                {
                    var r = _records[i];
                    if (r == null) continue;
                    bool acq = r.acquiredItemsEver != null && r.acquiredItemsEver.Contains(id);
                    bool offered = r.offeredItemsEver != null && r.offeredItemsEver.Contains(id);
                    if (acq) { acqSum += r.finalInventoryPower; acqN++; }
                    else { noacqSum += r.finalInventoryPower; noacqN++; }
                    // 提示コホート: offered または acq (取得=提示扱い、 ショップ以外の経路を含む)
                    if (offered || acq)
                    {
                        if (acq) { offAcqSum += r.finalInventoryPower; offAcqN++; }
                        else { offNoacqSum += r.finalInventoryPower; offNoacqN++; }
                    }
                }
                if (acqN < 30 || noacqN < 30) continue; // 全体の最低サンプル
                float deltaAll = (float)(acqSum / (double)acqN - noacqSum / (double)noacqN);
                float deltaOffered = (offAcqN >= 15 && offNoacqN >= 15)
                    ? (float)(offAcqSum / (double)offAcqN - offNoacqSum / (double)offNoacqN)
                    : float.NaN; // 提示時サンプル不足は NaN
                rows.Add((id, acqN, offAcqN + offNoacqN, deltaAll, deltaOffered));
            }
            // 提示時 ΔPower 降順 (NaN は末尾)
            rows.Sort((x, y) =>
            {
                bool xn = float.IsNaN(x.deltaOffered), yn = float.IsNaN(y.deltaOffered);
                if (xn && yn) return y.deltaAll.CompareTo(x.deltaAll);
                if (xn) return 1;
                if (yn) return -1;
                return y.deltaOffered.CompareTo(x.deltaOffered);
            });

            int show = Math.Min(30, rows.Count);
            sb.AppendLine($"  {PadR("アイテム ID", 26)}{PadR("取得数", 7)}{PadR("提示母数", 9)}{PadR("ΔPower全体", 12)}{PadR("ΔPower提示", 12)}");
            for (int i = 0; i < show; i++)
            {
                var r = rows[i];
                string offStr = float.IsNaN(r.deltaOffered) ? "n/a" : ((r.deltaOffered >= 0 ? "+" : "") + r.deltaOffered.ToString("F1"));
                sb.AppendLine($"  {PadR(TruncDisp(r.id, 24), 26)}{PadR(r.acqN.ToString(), 7)}{PadR(r.offeredN.ToString(), 9)}{PadR((r.deltaAll >= 0 ? "+" : "") + r.deltaAll.ToString("F1"), 12)}{PadR(offStr, 12)}");
            }
            sb.AppendLine();
            sb.AppendLine("  注: 提示時 ΔPower がより信頼可。 大幅差がある品は selection bias による誤評価の可能性。");
            sb.AppendLine("  動的効果品 (BD/双蛇/不屈系等) の真価は bandScore lift6F も併読推奨。");
            return sb.ToString();
        }

        /// <summary>Power 帯ごとのラン数分布を文字列化。 Weak/Early/Mid/Late/Apex の 5 帯。</summary>
        private static string ComputePowerBandDistribution(List<int> powers)
        {
            if (powers == null || powers.Count == 0) return "(データなし)";
            int[] counts = new int[5];
            foreach (var p in powers) counts[AutoTest.InventoryPower.GetPowerBandRank(p)]++;
            int total = powers.Count;
            string[] labels = { "Weak", "Early", "Mid", "Late", "Apex" };
            var parts = new List<string>(5);
            for (int i = 0; i < 5; i++)
                if (counts[i] > 0)
                    parts.Add($"{labels[i]}={counts[i]}({100f * counts[i] / total:F1}%)");
            return string.Join(" / ", parts);
        }

        /// <summary>⑦ アイテム別 pick率 / retention率 (2026-06-23)。
        ///   pick率   = 取得数 / 提示数 (提示された時にどれだけ拾うか)
        ///   保持率   = 最終所持数 / 取得数 (取った後どれだけ残すか)
        /// 高 pick / 低 retain = 「序盤強いが終盤お役御免」 の典型パターン抽出に有効。</summary>
        private string BuildPickRetentionBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験⑦ アイテム別 pick率 / 保持率 (上位 50、 提示数≥30、 pick率降順)】");
            sb.AppendLine("  pick率 = 取得数 / 提示数 (提示時の採用率)");
            sb.AppendLine("  保持率 = 最終所持数 / 取得数 (取った後の残存率)");
            sb.AppendLine("  高pick/低保持 = 序盤強・終盤お役御免の典型パターン");
            sb.AppendLine();

            int n = _records.Count;
            if (n == 0) { sb.AppendLine("  (データなし)"); return sb.ToString(); }

            // id → (offered, picked, retained) を集計
            var stats = new Dictionary<string, int[]>(); // [offered, picked, retained]
            for (int i = 0; i < n; i++)
            {
                var r = _records[i];
                if (r == null) continue;
                // 提示集合 (offered) と取得集合 (acquired) の和を「offered (機会あり)」 として扱う
                //   ─ 取得品は必ず提示扱い (ショップ以外の経路を含む)
                var offered = new HashSet<string>();
                if (r.offeredItemsEver != null) foreach (var id in r.offeredItemsEver) offered.Add(id);
                if (r.acquiredItemsEver != null) foreach (var id in r.acquiredItemsEver) offered.Add(id);
                foreach (var id in offered)
                {
                    if (!stats.TryGetValue(id, out var a)) { a = new int[3]; stats[id] = a; }
                    a[0]++; // offered
                    if (r.acquiredItemsEver != null && r.acquiredItemsEver.Contains(id)) a[1]++; // picked
                    if (r.finalOwnedItemIds != null && r.finalOwnedItemIds.Contains(id)) a[2]++; // retained
                }
            }

            // 集計 → 行リスト化 (提示数≥30 のみ)
            var rows = new List<(string id, int offered, int picked, int retained, float pickRate, float retentionRate)>();
            foreach (var kv in stats)
            {
                int off = kv.Value[0], pick = kv.Value[1], ret = kv.Value[2];
                if (off < 30) continue;
                float pickRate = (float)pick / off;
                float retRate = pick > 0 ? (float)ret / pick : 0f;
                rows.Add((kv.Key, off, pick, ret, pickRate, retRate));
            }
            // pick率 降順
            rows.Sort((x, y) => y.pickRate.CompareTo(x.pickRate));

            int show = Math.Min(50, rows.Count);
            sb.AppendLine($"  {PadR("アイテム ID", 26)}{PadR("提示", 6)}{PadR("取得", 6)}{PadR("最終", 6)}{PadR("pick率", 8)}{PadR("保持率", 8)}{PadR("性質", 18)}");
            for (int i = 0; i < show; i++)
            {
                var r = rows[i];
                string trait = ClassifyPickRetention(r.pickRate, r.retentionRate);
                sb.AppendLine($"  {PadR(TruncDisp(r.id, 24), 26)}{PadR(r.offered.ToString(), 6)}{PadR(r.picked.ToString(), 6)}{PadR(r.retained.ToString(), 6)}{PadR((r.pickRate * 100).ToString("F1") + "%", 8)}{PadR((r.retentionRate * 100).ToString("F1") + "%", 8)}{PadR(trait, 18)}");
            }
            return sb.ToString();
        }

        /// <summary>pick率/保持率の組合せから性質ラベル付与。</summary>
        private static string ClassifyPickRetention(float pickRate, float retentionRate)
        {
            if (pickRate >= 0.7f && retentionRate >= 0.7f) return "★定番 (高取得・残)";
            if (pickRate >= 0.7f && retentionRate < 0.4f) return "⚠序盤要員 (拾うが捨)";
            if (pickRate >= 0.7f) return "○常用品";
            if (pickRate < 0.2f && retentionRate >= 0.7f) return "◇隠れ強 (拾えば残)";
            if (pickRate < 0.2f) return "△マイナー";
            return "─普通";
        }

        /// <summary>順位ベース Tier 帯 (上位 20% = S, ... , 下位 20% = D)。</summary>
        private static string TierBand(int rank, int total)
        {
            if (total <= 0) return "?";
            float pct = (rank + 0.5f) / total;
            if (pct <= 0.20f) return "S";
            if (pct <= 0.40f) return "A";
            if (pct <= 0.60f) return "B";
            if (pct <= 0.80f) return "C";
            return "D";
        }

        // ===========================================================
        //  ゲーム体験 4 軸メトリクス (2026-06-21)
        //  - 緊張感曲線 / ビルド多様性 / 死因分布質 / 選択意味度
        //  全て既存 RunRec/CombatRec を集計するのみ (新規イベント収集なし)
        // ===========================================================

        /// <summary>1. 緊張感曲線 ── 各層末の HP%、 knife-edge (僅差勝利)、 blowout (大差敗北) を集計。
        /// 「ギリギリ勝った気持ち良さ」 と「不公平な瞬殺」 の発生率で体験のメリハリを測る。</summary>
        private string BuildTensionCurveBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験① 緊張感曲線 (Tension Curve)】");
            sb.AppendLine("  各層の戦闘終了 HP%、 knife-edge 勝利 (HP≤20% で勝った戦闘)、 blowout 敗北 (HP>80% から 1 戦で死亡)");
            sb.AppendLine();

            // floor → 集計
            var byFloor = new Dictionary<int, int[]>(); // [n, hpPctSum_x100, knifeWin, blowoutLoss, winN, lossN]
            for (int ri = 0; ri < _records.Count; ri++)
            {
                var rr = _records[ri];
                if (rr?.combats == null) continue;
                for (int ci = 0; ci < rr.combats.Count; ci++)
                {
                    var c = rr.combats[ci];
                    if (c == null || !c.isFightEnd) continue;
                    int max = c.playerMaxHpEnd > 0 ? c.playerMaxHpEnd : 1;
                    if (!byFloor.TryGetValue(c.floor, out var a)) { a = new int[6]; byFloor[c.floor] = a; }
                    a[0]++;
                    int hpPct = Mathf.Clamp(c.hpAfter * 100 / max, 0, 100);
                    a[1] += hpPct;
                    if (c.won)
                    {
                        a[4]++;
                        if (hpPct <= 20) a[2]++; // knife-edge: HP残≤20% で勝った
                    }
                    else
                    {
                        a[5]++;
                        int hpBeforePct = Mathf.Clamp(c.hpBefore * 100 / max, 0, 100);
                        if (hpBeforePct > 80) a[3]++; // blowout: HP>80% スタートで死亡
                    }
                }
            }

            var floors = new List<int>(byFloor.Keys);
            floors.Sort();
            sb.AppendLine($"  {PadR("層",4)}{PadR("戦闘数",7)}{PadR("平均残HP%",10)}{PadR("knife勝率",10)}{PadR("blowout率",10)}");
            foreach (int f in floors)
            {
                var a = byFloor[f];
                if (a[0] == 0) continue;
                float avgHp = (float)a[1] / a[0];
                float knife = a[4] > 0 ? 100f * a[2] / a[4] : 0f;
                float blow  = a[5] > 0 ? 100f * a[3] / a[5] : 0f;
                sb.AppendLine($"  {PadR(f.ToString()+"F",4)}{PadR(a[0].ToString(),7)}{PadR(avgHp.ToString("F1")+"%",10)}{PadR(knife.ToString("F1")+"%",10)}{PadR(blow.ToString("F1")+"%",10)}");
            }
            sb.AppendLine("  knife勝率 = 勝利戦闘のうち残HP≤20% で勝った割合 (高=緊張感ある勝利)");
            sb.AppendLine("  blowout率 = 敗北戦闘のうち HP>80% から負けた割合 (高=理不尽な瞬殺)");

            // 全層合算の comeback (HP<20% から 6F 到達)
            int comebackRuns = 0, comebackEligible = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                var rr = _records[i];
                if (rr?.combats == null) continue;
                bool wasCritical = false;
                bool reached6F = false;
                for (int ci = 0; ci < rr.combats.Count; ci++)
                {
                    var c = rr.combats[ci];
                    if (c == null) continue;
                    int max = c.playerMaxHpEnd > 0 ? c.playerMaxHpEnd : 1;
                    if (c.floor <= 4 && c.hpAfter * 5 < max) wasCritical = true;
                    if (c.floor >= 6) reached6F = true;
                }
                if (wasCritical) { comebackEligible++; if (reached6F) comebackRuns++; }
            }
            sb.AppendLine();
            sb.AppendLine($"  Comeback: 1-4層で HP<20% を経験 → 6F到達: {comebackRuns} / {comebackEligible} ラン ({(comebackEligible > 0 ? 100f*comebackRuns/comebackEligible : 0f):F1}%)");
            sb.AppendLine("  (高=逆転シナリオが多い、 低=ピンチ=即死パターン)");
            return sb.ToString();
        }

        /// <summary>2. ビルド多様性 ── 5F到達時の「武器Tier + 装備ダイス + 主要パッシブ」 を組として集計し、
        /// Top-K 集中度・Shannon エントロピー・ユニーク数で多様性を測る。</summary>
        private string BuildBuildDiversityBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験② ビルド多様性 (Build Diversity)】");
            sb.AppendLine("  5F到達ランの「最終武器Tier × 装備ダイス」 で組成し、 集中度を測る");
            sb.AppendLine();

            var buildCount = new Dictionary<string, int>();
            int reached5F = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                var rr = _records[i];
                if (rr == null) continue;
                if (rr.deathFloor < 5 && !rr.reached6F) continue; // 5F到達のみ
                reached5F++;
                string wpn = string.IsNullOrEmpty(rr.finalWeaponTier) ? "(無)" : rr.finalWeaponTier;
                // 5F到達時点のダイスは記録されていない → 最終ダイスで近似
                string dice = "";
                if (rr.combats != null)
                {
                    for (int ci = rr.combats.Count - 1; ci >= 0; ci--)
                    {
                        if (rr.combats[ci]?.floor == 5)
                        {
                            dice = rr.combats[ci].diceId ?? "";
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(dice)) dice = "(武器ダイス)";
                string key = wpn + " × " + dice;
                buildCount.TryGetValue(key, out int c);
                buildCount[key] = c + 1;
            }

            if (reached5F == 0)
            {
                sb.AppendLine("  (5F到達ランなし)");
                return sb.ToString();
            }

            var sorted = new List<KeyValuePair<string, int>>(buildCount);
            sorted.Sort((x, y) => y.Value.CompareTo(x.Value));

            // 集中度
            int top10Share = 0;
            int topN = Math.Min(10, sorted.Count);
            for (int i = 0; i < topN; i++) top10Share += sorted[i].Value;

            // ユニーク (1 回しか出ないビルド)
            int hapax = 0;
            foreach (var kv in sorted) if (kv.Value == 1) hapax++;

            // Shannon entropy (bit)
            double H = 0;
            foreach (var kv in sorted)
            {
                double p = (double)kv.Value / reached5F;
                if (p > 0) H -= p * Math.Log(p, 2);
            }
            double maxH = sorted.Count > 1 ? Math.Log(sorted.Count, 2) : 1;

            sb.AppendLine($"  5F到達ラン: {reached5F}");
            sb.AppendLine($"  ユニークビルド数: {sorted.Count} (1回限り: {hapax})");
            sb.AppendLine($"  上位10ビルド集中度: {100f*top10Share/reached5F:F1}% (低=広く分散、 高=収束)");
            sb.AppendLine($"  Shannon エントロピー: {H:F2} bit / 最大 {maxH:F2} bit (正規化 {H/maxH:F2})");
            sb.AppendLine();
            sb.AppendLine("  上位 10 ビルド:");
            for (int i = 0; i < topN; i++)
            {
                var kv = sorted[i];
                sb.AppendLine($"    {PadR(kv.Key, 36)} {kv.Value,5} ({100f*kv.Value/reached5F:F1}%)");
            }
            return sb.ToString();
        }

        /// <summary>3. 死因分布質 ── 死因を「公平死 (累積攻撃/スリップ)」「理不尽死 (反射/サドンデス/烙印一撃)」 に分類し、
        /// 各層の理不尽死率を出す。 高い層 = 調整候補。</summary>
        private string BuildDeathCauseQualityBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験③ 死因分布質 (Death Cause Quality)】");
            sb.AppendLine("  公平死 = Normal/Chip (累積攻撃や持続)");
            sb.AppendLine("  理不尽死 = Reflect (反射自滅) / SuddenDeath (純運勝負) / Garyo (画竜点睛) / その他突発要因");
            sb.AppendLine();

            // 死因の分類 (CombatRec.deathCause を使う、 deathCauseは戦闘の死因)
            // 公平: Normal, Chip
            // 理不尽: Reflect, SuddenDeath, Garyo, Pursuit (突発反撃), Threat (脅威でハメ殺し)
            var byFloor = new Dictionary<int, int[]>(); // [死戦闘総数, 公平, 理不尽]
            for (int i = 0; i < _records.Count; i++)
            {
                var rr = _records[i];
                if (rr?.combats == null) continue;
                for (int ci = 0; ci < rr.combats.Count; ci++)
                {
                    var c = rr.combats[ci];
                    if (c == null || c.won) continue;
                    if (!c.isFightEnd) continue;
                    string dc = c.deathCause.ToString();
                    bool fair = dc == "Normal" || dc == "Chip" || dc == "None";
                    bool unfair = dc == "Reflect" || dc == "SuddenDeath" || dc == "Garyo"
                               || dc == "Pursuit" || dc == "Threat" || dc == "Judgment";
                    if (!byFloor.TryGetValue(c.floor, out var a)) { a = new int[3]; byFloor[c.floor] = a; }
                    a[0]++;
                    if (fair) a[1]++;
                    else if (unfair) a[2]++;
                }
            }

            var floors = new List<int>(byFloor.Keys);
            floors.Sort();
            sb.AppendLine($"  {PadR("層",4)}{PadR("死戦闘",7)}{PadR("公平死",8)}{PadR("理不尽死",10)}{PadR("理不尽率",10)}");
            foreach (int f in floors)
            {
                var a = byFloor[f];
                if (a[0] == 0) continue;
                float unfairPct = 100f * a[2] / a[0];
                sb.AppendLine($"  {PadR(f.ToString()+"F",4)}{PadR(a[0].ToString(),7)}{PadR(a[1].ToString(),8)}{PadR(a[2].ToString(),10)}{PadR(unfairPct.ToString("F1")+"%",10)}");
            }
            sb.AppendLine("  理不尽率が高い層は調整候補 (Reflect=反射火力過剰 / SuddenDeath=運勝負化過剰 等)");
            return sb.ToString();
        }

        /// <summary>4. 選択意味度 ── イベント選択肢で「常に同じ選択肢」 = 等価/つまらない分岐、
        /// 「選択直後 3 戦闘以内に死亡」 = ハズレ選択肢を可視化。</summary>
        private string BuildDecisionWeightBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験④ 選択意味度 (Decision Weight)】");
            sb.AppendLine("  各イベントの選択肢分布。 偏りが極端 (1選択肢が ≥80%) = AI が等価でないと判断 = ジレンマ無し");
            sb.AppendLine();

            // EventChoiceLearningStats / RunRec.eventChoicesMade から集計
            // 各 eventId につき「最頻選択肢の割合」「分岐数」「合計回数」
            var perEvent = new Dictionary<string, Dictionary<int, int>>(); // eventId -> (choiceIdx -> count)
            for (int i = 0; i < _records.Count; i++)
            {
                var rr = _records[i];
                if (rr?.eventChoicesMade == null) continue;
                for (int j = 0; j < rr.eventChoicesMade.Count; j++)
                {
                    string item = rr.eventChoicesMade[j];
                    int pipe = item.IndexOf('|');
                    if (pipe <= 0 || pipe >= item.Length - 1) continue;
                    string eid = item.Substring(0, pipe);
                    if (!int.TryParse(item.Substring(pipe + 1), out int idx)) continue;
                    if (!perEvent.TryGetValue(eid, out var dict)) { dict = new Dictionary<int, int>(); perEvent[eid] = dict; }
                    dict.TryGetValue(idx, out int c);
                    dict[idx] = c + 1;
                }
            }

            if (perEvent.Count == 0)
            {
                sb.AppendLine("  (イベント選択記録なし)");
                return sb.ToString();
            }

            // 各イベントごとの最頻選択肢割合
            var rows = new List<(string id, int total, int branches, float topPct)>();
            int dominantCount = 0, balancedCount = 0;
            foreach (var kv in perEvent)
            {
                int total = 0, top = 0;
                foreach (var v in kv.Value.Values) { total += v; if (v > top) top = v; }
                if (total == 0) continue;
                float topPct = 100f * top / total;
                rows.Add((kv.Key, total, kv.Value.Count, topPct));
                if (topPct >= 80f) dominantCount++;
                else if (topPct <= 50f + (50f / Math.Max(1, kv.Value.Count - 1))) balancedCount++;
            }
            rows.Sort((x, y) => y.topPct.CompareTo(x.topPct));

            sb.AppendLine($"  イベント総数: {rows.Count}");
            sb.AppendLine($"  Dominant (最頻≥80%): {dominantCount} ({100f*dominantCount/rows.Count:F1}%) ── ジレンマ無しイベント");
            sb.AppendLine($"  Balanced (拮抗): {balancedCount} ({100f*balancedCount/rows.Count:F1}%) ── 選択肢が意味を持つイベント");
            sb.AppendLine();
            sb.AppendLine("  最も偏った 10 イベント (= AI が常に同じ選択肢):");
            int show = Math.Min(10, rows.Count);
            for (int i = 0; i < show; i++)
            {
                var r = rows[i];
                sb.AppendLine($"    {PadR(TruncDisp(r.id, 22), 24)} 回数{r.total,5} / 分岐{r.branches} / 最頻{r.topPct:F1}%");
            }
            return sb.ToString();
        }

        private class BossWinAgg
        {
            public int enc, wins;
            public long tWin, tLoss, tDraw, turns;
            /// <summary>撃破できた戦闘だけのターン合計 / 力尽きた戦闘だけのターン合計。
            /// 2026-07-28: 全遭遇平均だけでは「ボスHPが律速か、プレイヤー生存が律速か」を
            /// 区別できないため分離した。 勝T = ボスを削り切るのに要した時間、
            /// 敗T = プレイヤーが保った時間。 前者は火力、 後者は加害のスケールを示す。</summary>
            public long turnsOnWin, turnsOnLoss;
            public Dictionary<string, int> causes = new Dictionary<string, int>();
            public float WinRate => enc > 0 ? (float)wins / enc : 0f;
            public float RollWinRate { get { long t = tWin + tLoss + tDraw; return t > 0 ? (float)tWin / t : 0f; } }
            public float AvgTurns => enc > 0 ? (float)turns / enc : 0f;
            public float AvgTurnsWin => wins > 0 ? (float)turnsOnWin / wins : 0f;
            public float AvgTurnsLoss => Losses > 0 ? (float)turnsOnLoss / Losses : 0f;
            public int Losses => enc - wins;
        }

        private static string FormatBossRow(string label, BossWinAgg a)
        {
            string cause = "敗北なし";
            if (a.Losses > 0)
            {
                string dom = ""; int domN = 0;
                foreach (var kv in a.causes) if (kv.Value > domN) { dom = kv.Key; domN = kv.Value; }
                cause = domN > 0 ? $"主死因{dom} {(float)domN / a.Losses:P0}" : "—";
            }
            // 「収支勝率」= 収支プラス (与ダメ-被ダメ>0) で終えたターンの割合。ADR-0009 で「ロール勝率」から再定義。
            // 勝T/敗T の分離 (2026-07-28): 勝T=ボスを削り切る時間 (＝ボスHPの尺度)、
            // 敗T=プレイヤーが保つ時間 (＝ボス加害の尺度)。 全遭遇平均だけだと両者が混ざり、
            // 「HPを下げてもターンが動かない」現象の原因が読めなくなる。
            string winT  = a.wins   > 0 ? $"{a.AvgTurnsWin,4:F1}T" : "   -";
            string lossT = a.Losses > 0 ? $"{a.AvgTurnsLoss,4:F1}T" : "   -";
            return $"  {label,-7}: 勝率 {a.WinRate,6:P1}  (遭遇 {a.enc,5} / 勝 {a.wins,5})"
                 + $"  平均{a.AvgTurns,4:F1}T (勝{winT}/敗{lossT})  収支勝率{a.RollWinRate,5:P0}  {cause}";
        }

        private static string BossLabel(string id)
        {
            switch (id)
            {
                case "boss_layer1": return "1層";
                case "boss_layer2": return "2層";
                case "boss_layer3": return "3層";
                case "boss_layer4": return "4層";
                case "boss_layer5": return "5層";
                case "boss_layer5_hidden": return "5裏";
                case "boss_layer6": return "6層";
                case "boss_layer7": return "7層p1";
                case "boss_layer7_p2": return "7層p2";
                case "boss_layer7_p3": return "7層p3";
                case "boss_layer7_p4": return "7層p4";
                case "boss_layer7_p5": return "7層p5";
                case "boss_layer7_p6": return "7層p6";
                case "boss_layer7_p7": return "7層p7";
                default: return id;
            }
        }

        /// <summary>Λ層（時間の狭間）のファーム期待値ブロック。突入ランのみを母数に
        /// 獲得ゴールド/アイテムの平均、踏破マス・Λデバフ段階合計の平均、離脱/Λ内死亡の内訳を出す。</summary>
        private string BuildLambdaFarmBlock()
        {
            var entered = _records.FindAll(r => r.enteredLambda);
            var sb = new StringBuilder();
            sb.AppendLine("【Λ層 ファーム期待値（突入ランのみ）】");
            if (entered.Count == 0)
            {
                sb.AppendLine("  Λ突入ラン: 0（〈決意〉未到達 or 5F到達前に終了）");
                return sb.ToString();
            }
            double gold = 0, items = 0, tiles = 0, dbg = 0, gross = 0;
            int diedInLambda = 0, exited = 0;
            foreach (var r in entered)
            {
                gold += r.lambdaGoldGained;
                items += r.lambdaItemsGained;
                gross += r.lambdaItemsAcquiredGross;
                tiles += r.lambdaTilesFarmed;
                dbg += r.lambdaDebuffLevelSum;
                // Λ内死亡: 5Fで死亡かつ inLambda 由来（reachedFloor==5 のGameOver）。離脱できれば6F以上へ。
                if (r.outcome == Outcome.GameOver && r.reachedFloor <= 5) diedInLambda++;
                else exited++;
            }
            int n = entered.Count;
            sb.AppendLine($"  Λ突入ラン   : {n}");
            sb.AppendLine($"  獲得ゴールド : 平均 +{gold / n:F1}");
            sb.AppendLine($"  獲得アイテム : 平均 取得+{gross / n:F2} → 純増+{items / n:F2}");
            sb.AppendLine($"  踏破マス     : 平均 {tiles / n:F1}");
            sb.AppendLine($"  Λデバフ段階計: 平均 {dbg / n:F2}");
            sb.AppendLine($"  離脱成功/Λ内死亡: {exited} / {diedInLambda}");
            return sb.ToString();
        }

        /// <summary>武器強化経路の到達状況。 T4集計バグ修正の効果検証用。
        /// OnWeaponTierUpgraded 発火回数とラン終了時の武器Tier分布を出す。</summary>
        private string BuildWeaponProgressionBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【武器強化経路 (集計修正バグ検証)】");
            int n = _records.Count;
            if (n == 0) { sb.AppendLine("  (データなし)"); return sb.ToString(); }

            // 強化回数分布
            int totalUpgrades = 0;
            int upgradeUsers = 0;
            int reachT4 = 0;
            var tierCount = new Dictionary<string, int>();
            foreach (var r in _records)
            {
                if (r == null) continue;
                totalUpgrades += r.tierUpgradeCount;
                if (r.tierUpgradeCount > 0) upgradeUsers++;
                if (!string.IsNullOrEmpty(r.finalWeaponTier))
                {
                    tierCount[r.finalWeaponTier] = tierCount.TryGetValue(r.finalWeaponTier, out int c) ? c + 1 : 1;
                    if (r.finalWeaponTier.EndsWith("_t4")) reachT4++;
                }
            }
            sb.AppendLine($"  強化発火合計   : {totalUpgrades} 回 ({(double)totalUpgrades / n:F2}/ラン)");
            sb.AppendLine($"  強化を行ったラン: {upgradeUsers} / {n} ({100.0 * upgradeUsers / n:F1}%)");
            sb.AppendLine($"  最終T4到達ラン : {reachT4} / {n} ({100.0 * reachT4 / n:F1}%)");
            sb.AppendLine($"  最終武器Tier分布:");
            var sorted = new List<KeyValuePair<string, int>>(tierCount);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in sorted)
                sb.AppendLine($"    {kv.Key,-20} : {kv.Value} ({100.0 * kv.Value / n:F1}%)");
            return sb.ToString();
        }

        /// <summary>1コホート分の自己完結サマリブロック。</summary>
        private string BuildSummaryBlock(string title, List<RunRec> recs)
        {
            int n = recs.Count;
            var sb = new StringBuilder();
            sb.AppendLine("================ " + title + " ================");
            sb.AppendLine($"ラン数    : {n}");
            sb.AppendLine($"行動方針  : 前進貪欲 + 生存重視");
            sb.AppendLine();
            if (n == 0) { sb.AppendLine("（該当ランなし）"); return sb.ToString(); }

            // --- バンド分布 (R1-R11, R8b 含む) ---
            // R8b = 6Fクリアして 7F進入不可で終了 (真理未所持) を R9 と R10 の間に挿入。
            sb.AppendLine("---- 結果バンド分布（上位%＝その結果『以上』に到達したランの割合） ----");
            string[] bands = { "R1a","R1b","R1c","R1d","R2","R3","R4","R5","R6","R7","R8","R9","R8b","R10","R11" };
            string[] labels = {
                "1F道中で死亡","1Fボスで死亡","2F道中で死亡","2Fボスで死亡",
                "3F道中で死亡","3Fボスで死亡","4F道中で死亡","4Fボスで死亡",
                "5F道中で死亡","5Fボスで死亡","5Fクリア(決意未所持)","6Fボスで死亡",
                "6Fクリア(真理未所持)","7Fで死亡(7層到達)","7層クリア(完全クリア)" };
            int crash = recs.FindAll(r => r.band == "CRASH").Count;
            int dead  = recs.FindAll(r => r.band == "DEADLOCK").Count;
            int valid = n - crash - dead;
            // bands は進行の浅い→深い順。各バンドの「以上(=そのバンド＋それより良い全て)」を
            // ベスト側から累積し、valid に対する割合＝上位% として表示する。
            int[] counts = new int[bands.Length];
            for (int i = 0; i < bands.Length; i++)
                counts[i] = recs.FindAll(r => r.band == bands[i]).Count;
            int[] topCum = new int[bands.Length];
            int cum = 0;
            for (int i = bands.Length - 1; i >= 0; i--) { cum += counts[i]; topCum[i] = cum; }
            for (int i = 0; i < bands.Length; i++)
            {
                sb.AppendLine($"  {PadR(bands[i],4)}{PadR(labels[i],22)}: {PadL(counts[i].ToString(),4)}  上位 {PadL(Pct(topCum[i], valid),6)}");
            }
            sb.AppendLine("  （例: 「7層クリア 上位X%」= 全ランの上位X%が7層クリア以上を達成）");
            sb.AppendLine();
            sb.AppendLine($"  {PadR("CRASH",4)}{PadR("クラッシュ(例外)",22)}: {PadL(crash.ToString(),4)}  ({Pct(crash, n)} of all)");
            sb.AppendLine($"  {PadR("DEAD",4)}{PadR("デッドロック",22)}: {PadL(dead.ToString(),4)}  ({Pct(dead, n)} of all)");
            sb.AppendLine();

            // --- 死亡階層・死因 ---
            sb.AppendLine("---- 死亡階層・死因 ----");
            var deaths = recs.FindAll(r => r.outcome == Outcome.GameOver);
            sb.AppendLine($"  総死亡数: {deaths.Count} / {n}");
            for (int f = 1; f <= 6; f++)
            {
                int c = deaths.FindAll(r => r.deathFloor == f).Count;
                if (c > 0) sb.AppendLine($"   {PadR("Floor " + f, 16)}: {PadL(c.ToString(),4)}  ({Pct(c, deaths.Count)})");
            }
            foreach (DeathCause dc in Enum.GetValues(typeof(DeathCause)))
            {
                if (dc == DeathCause.None) continue;
                int c = deaths.FindAll(r => r.cause == dc).Count;
                if (c > 0) sb.AppendLine($"   {PadR(dc.ToString(), 16)}: {PadL(c.ToString(),4)}  ({Pct(c, deaths.Count)})");
            }
            sb.AppendLine();

            // --- ラストスタンド ---
            sb.AppendLine("---- ラストスタンド ----");
            var ls = recs.FindAll(r => r.lastStandUsed);
            sb.AppendLine($"  発動ラン数: {ls.Count} / {n}  ({Pct(ls.Count, n)})");
            for (int f = 1; f <= 6; f++)
            {
                int c = ls.FindAll(r => r.lastStandFloor == f).Count;
                if (c > 0) sb.AppendLine($"   {PadR("発動Floor " + f, 16)}: {PadL(c.ToString(),4)}  ({Pct(c, ls.Count)})");
            }
            if (ls.Count > 0)
            {
                double avgAfter = 0, avgWin = 0;
                foreach (var r in ls) { avgAfter += r.combatsAfterLastStand; avgWin += r.winsAfterLastStand; }
                int maxAfter = 0; foreach (var r in ls) maxAfter = Math.Max(maxAfter, r.combatsAfterLastStand);
                sb.AppendLine($"   {PadR("発動後の平均戦闘数", 20)}: {(avgAfter/ls.Count):F2}");
                sb.AppendLine($"   {PadR("発動後の平均勝利数", 20)}: {(avgWin/ls.Count):F2}");
                sb.AppendLine($"   {PadR("発動後の最大潜り抜け", 20)}: {maxAfter} 戦");
            }
            sb.AppendLine();

            // --- ラストスタンド直接死因 ---
            sb.AppendLine("---- ラストスタンド直接死因 ----");
            var lsDead = ls.FindAll(r => r.outcome == Outcome.GameOver);
            sb.AppendLine($"  発動後に死亡したラン: {lsDead.Count} / {ls.Count}");
            if (lsDead.Count > 0)
            {
                // 発動後 何戦目で死亡したか（combatsAfterLastStand のバケット）
                int b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0;
                foreach (var r in lsDead)
                {
                    int c = r.combatsAfterLastStand;
                    if (c <= 0) b0++; else if (c == 1) b1++; else if (c == 2) b2++;
                    else if (c == 3) b3++; else b4++;
                }
                sb.AppendLine($"   {PadR("発動と同戦闘で即死(0戦)", 24)}: {PadL(b0.ToString(),4)}  ({Pct(b0, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後1戦で死亡", 24)}: {PadL(b1.ToString(),4)}  ({Pct(b1, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後2戦で死亡", 24)}: {PadL(b2.ToString(),4)}  ({Pct(b2, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後3戦で死亡", 24)}: {PadL(b3.ToString(),4)}  ({Pct(b3, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後4戦以上で死亡", 24)}: {PadL(b4.ToString(),4)}  ({Pct(b4, lsDead.Count)})");

                // 死亡階層
                for (int f = 1; f <= 6; f++)
                {
                    int c = lsDead.FindAll(r => r.deathFloor == f).Count;
                    if (c > 0) sb.AppendLine($"   {PadR("Floor " + f + " で死亡", 24)}: {PadL(c.ToString(),4)}  ({Pct(c, lsDead.Count)})");
                }

                // 致命を与えた敵（各ランの最後の敗北戦から）
                var fatalAgg = new Dictionary<string, int>();
                foreach (var r in lsDead)
                {
                    if (r.combats == null) continue;
                    CombatRec fc = null;
                    for (int i = r.combats.Count - 1; i >= 0; i--)
                        if (!r.combats[i].won) { fc = r.combats[i]; break; }
                    if (fc == null) continue;
                    bool boss = fc.isBoss || GameLoop.BossIds.IsBoss(fc.enemyId);
                    string nm = (string.IsNullOrEmpty(fc.enemy) ? "?" : fc.enemy) + (boss ? "(BOSS)" : "");
                    fatalAgg.TryGetValue(nm, out var v);
                    fatalAgg[nm] = v + 1;
                }
                var fk = new List<string>(fatalAgg.Keys);
                fk.Sort((x, y) => fatalAgg[y].CompareTo(fatalAgg[x]));
                sb.AppendLine("   ── 致命を与えた敵 ──");
                foreach (var k in fk)
                    sb.AppendLine($"   {PadR(TruncDisp(k,18), 22)}: {PadL(fatalAgg[k].ToString(),4)}  ({Pct(fatalAgg[k], lsDead.Count)})");
            }
            sb.AppendLine("（発動後は基本ロール敗北のメインダメのみ＝致命敵＝その止めを刺した敵）");
            sb.AppendLine();

            // --- 経済・燃費 ---
            sb.AppendLine("---- 経済・燃費バランス ----");
            double sCoins=0, sPeak=0, sGain=0, sStarv=0, sStarvHit=0, sShop=0;
            double sReroll=0, sRerollG=0, sPrio=0, sMatGain=0;
            double sFinalHope=0, sMinHope=0; int madnessCount=0;   // 希望(ADR-0002)
            double sHCombat=0, sHComposure=0, sHLateral=0, sHMarch=0, sHEvil=0, sHFood=0, sHReroll=0; // 希望 発生源別収支
            foreach (var r in recs)
            { sCoins+=r.finalCoins; sPeak+=r.peakCoins; sGain+=r.totalGoldGained;
              sStarv+=r.starvationTotal; sStarvHit+=r.starvationHits; sShop+=r.shopPurchases;
              sReroll+=r.shopRerolls; sRerollG+=r.shopRerollCoins; sPrio+=r.priorityItemsAcquired; sMatGain+=r.materialsGainedTotal;
              sFinalHope+=r.finalHope; sMinHope+=r.minHope; if (r.reachedMadness) madnessCount++;
              sHCombat+=r.hopeCombatLoss; sHComposure+=r.hopeComposureGain; sHLateral+=r.hopeLateralLoss;
              sHMarch+=r.hopeMarchLoss; sHEvil+=r.hopeEvilLoss; sHFood+=r.hopeFoodGain; sHReroll+=r.hopeRerollLoss; }
            int dn = Math.Max(1, n);
            sb.AppendLine($"  {PadR("平均最終ゴールド", 20)}: {(sCoins/dn):F1}");
            sb.AppendLine($"  {PadR("平均ピークゴールド", 20)}: {(sPeak/dn):F1}");
            sb.AppendLine($"  {PadR("平均総獲得ゴールド", 20)}: {(sGain/dn):F1}");
            sb.AppendLine($"  {PadR("平均総獲得素材(pt基準)", 20)}: {(sMatGain/dn):F1}");
            // 値下げ交渉(=強盗)。 1 ラン 1 回までなので 試行数 = 実行したラン数。
            {
                long ra = InventorySystem.Shop.ShopManager.RobberyAttempts;
                long rw = GameManager.RobberyWins, rl = GameManager.RobberyLosses;
                sb.AppendLine($"  {PadR("値下げ交渉", 20)}: 実行 {ra}/{n} ラン ({Pct((int)ra, n)})"
                            + (ra > 0
                               ? $"  勝 {rw} / 敗 {rl} (勝率 {Pct((int)rw, (int)(rw + rl))})"
                                 + $"  平均戦利品 {(rw > 0 ? GameManager.RobberyLootTotal / (double)rw : 0):F1} 件"
                               : "  ※未発動"));
            }
            sb.AppendLine($"  {PadR("平均最終希望", 20)}: {(sFinalHope/dn):F1}");
            sb.AppendLine($"  {PadR("平均最低希望", 20)}: {(sMinHope/dn):F1}  (発狂到達 {madnessCount}/{n} = {Pct(madnessCount, n)})");
            sb.AppendLine($"  ── 希望 発生源別収支（1ラン平均・損は−） ──");
            sb.AppendLine($"  {PadR("  戦闘損", 20)}: -{(sHCombat/dn):F1}   {PadR("被弾0回復", 12)}: +{(sHComposure/dn):F1}");
            sb.AppendLine($"  {PadR("  横移動損", 20)}: -{(sHLateral/dn):F1}   {PadR("絶望進軍損", 12)}: -{(sHMarch/dn):F1}");
            sb.AppendLine($"  {PadR("  悪選択損", 20)}: -{(sHEvil/dn):F1}   {PadR("食料回復", 12)}: +{(sHFood/dn):F1}");
            sb.AppendLine($"  {PadR("  振り直し損", 20)}: -{(sHReroll/dn):F1}");
            sb.AppendLine($"  {PadR("ショップ購入数", 20)}: {(sShop/dn):F2}");
            // --- パッシブ取得の経路別内訳 (2026-09-09) ---
            //   供給の 2/3 が無料経路で、 価格を上げても届かない。 どこが配っているかを出す。
            {
                var au = InventorySystem.Helpers.PassiveSourceAudit.Counts;
                if (au.Count > 0)
                {
                    int tot = 0; foreach (var kv in au) tot += kv.Value;
                    // **(double) を必ず挟む。** dn が整数だと int/int で切り捨てられ、
                    //   全部 "8.00 品" のような整数になって内訳が読めなくなる (2026-09-09 に実際に出した)。
                    double srcRuns = Math.Max(1, n);
                    sb.AppendLine($"  ── パッシブ取得の経路別 (1ラン平均 / 計 {(tot / srcRuns):F2} 品) ──");
                    var srcKeys = new List<string>(au.Keys);
                    srcKeys.Sort((a, b) => au[b].CompareTo(au[a]));
                    foreach (var k in srcKeys)
                        sb.AppendLine($"  {PadR("  " + k, 20)}: {(au[k] / srcRuns):F2} 品  ({au[k] * 100.0 / tot:F1}%)");
                }
            }
            // --- 戦闘ドロップの実効率 (2026-09-15) ---
            //   設定値ではなく**実際に落ちた率**を出す。 エリートと通常を分けて数えないと、
            //   「どちらの経路が供給しているか」が合算に埋もれる。
            {
                long er = GameLoop.CombatRewards.EliteRolls, ed = GameLoop.CombatRewards.EliteDrops;
                long nr = GameLoop.CombatRewards.NormalRolls, nd = GameLoop.CombatRewards.NormalDrops;
                if (er + nr > 0)
                    sb.AppendLine($"  {PadR("戦闘ドロップ実効率", 20)}: "
                        + $"エリート {(er > 0 ? ed * 100.0 / er : 0):F1}% ({ed}/{er})"
                        + $" / 通常 {(nr > 0 ? nd * 100.0 / nr : 0):F1}% ({nd}/{nr})"
                        + $"  期待比 {(nr > 0 && nd > 0 ? (ed / (double)er) / (nd / (double)nr) : 0):F1} 倍");
            }
            {
                var RO = GameLoop.GameManager.RerollOutcome;
                long rtot = 0; foreach (var v in RO) rtot += v;
                if (rtot > 0)
                    sb.AppendLine($"  {PadR("リロール要求の結果", 20)}: 成功 {100.0*RO[3]/rtot:F1}%"
                        + $" / フェーズ不一致 {100.0*RO[0]/rtot:F1}%"
                        + $" / Instance無し {100.0*RO[1]/rtot:F1}%"
                        + $" / TryReroll=false {100.0*RO[2]/rtot:F1}%"
                        + $"  (内訳: 店無し {InventorySystem.Shop.ShopManager.RerollFailNoShop / dn:F2}"
                        + $" / 金不足 {InventorySystem.Shop.ShopManager.RerollFailNoGold / dn:F2}"
                        + $" / 上限到達 {InventorySystem.Shop.ShopManager.RerollFailCapped / dn:F2}"
                        + $" / **無料リロール {InventorySystem.Shop.ShopManager.FreeRolls / dn:F2}** 回/ラン)");
            }
            {
                long gt = 0;
                for (int i = 0; i < 4; i++) gt += GrantOk[i] + GrantNg[i];
                if (gt > 0)
                {
                    sb.AppendLine($"  ── ランダム付与の配布結果 (計 {(double)gt / dn:F2} 品/ラン) ──");
                    for (int i = 0; i < 4; i++)
                    {
                        long tot = GrantOk[i] + GrantNg[i];
                        if (tot <= 0) continue;
                        sb.AppendLine($"  {PadR("  " + GrantKindName[i], 22)}: 届いた {(double)GrantOk[i] / dn,6:F3}"
                            + $" / 空振り {(double)GrantNg[i] / dn,6:F3} 品/ラン"
                            + $"  (成功 {100.0 * GrantOk[i] / tot,5:F1}%)");
                    }
                }
            }
            {
                long rt = 0; double rg = 0;
                for (int i = 0; i < 6; i++) { rt += RerollPathCount[i]; rg += RerollPathCoins[i]; }
                if (rt > 0)
                {
                    sb.AppendLine($"  ── リロールの経路別 (計 {(double)rt / dn:F2} 回 / {rg / dn:F1}G) ──");
                    for (int i = 0; i < 6; i++)
                    {
                        if (RerollPathCount[i] <= 0) continue;
                        // **(double) を落とさないこと。** RerollPathCount は long、 dn は int なので
                        //   キャストが無いと整数除算で 0.91 回/ラン が 0 と印字される (2026-09-17 に踏んだ)。
                        sb.AppendLine($"  {PadR("  " + RerollPathName[i], 22)}: {(double)RerollPathCount[i] / dn,6:F3} 回/ラン"
                            + $" ({100.0 * RerollPathCount[i] / rt,4:F1}%)"
                            + $"  {RerollPathCoins[i] / dn,6:F1}G ({100.0 * RerollPathCoins[i] / System.Math.Max(1e-9, rg),4:F1}%)"
                            + $"  1回あたり {RerollPathCoins[i] / RerollPathCount[i],5:F1}G");
                    }
                }
            }
            {
                long st = 0; foreach (var v in SurplusExit) st += v;
                if (st > 0)
                {
                    string[] why = { "リロール不可", "水位/買えない", "リロール失敗", "回数上限", "価値で停止" };
                    sb.AppendLine($"  ── 余剰リロールの出口 (店 {(double)st / dn:F1}/ラン) ──");
                    for (int i = 0; i < 5; i++)
                        if (SurplusExit[i] > 0)
                            sb.AppendLine($"  {PadR("  " + why[i], 22)}: {100.0 * SurplusExit[i] / st,5:F1}%"
                                + $"  そのときの残金 平均 {SurplusExitCoins[i] / SurplusExit[i],6:F1}G");
                }
            }
            if (DiscardRolls > 0)
            {
                long dAll = DiscardPassive + DiscardPart + DiscardWeapon;
                sb.AppendLine($"  {PadR("リロールが捨てた棚", 20)}: {(double)DiscardRolls / dn:F2} 回/ラン で"
                    + $" フェーズ3 購入対象を {(double)dAll / dn:F2} 品/ラン"
                    + $" (1 回あたり {(double)dAll / DiscardRolls:F2}: パッシブ {(double)DiscardPassive / DiscardRolls:F2}"
                    + $" / パーツ {(double)DiscardPart / DiscardRolls:F2} / 武器 {(double)DiscardWeapon / DiscardRolls:F2})"
                    + $"  準パワー平均 {(dAll > 0 ? DiscardPower / dAll : 0):F3}"
                    + $" / スコア足切り品 {(dAll > 0 ? 100.0 * DiscardJunk / dAll : 0):F0}%");
                string[] nthName = { "1回目", "2回目", "3回目", "4回目以降" };
                var nsb = new System.Text.StringBuilder();
                for (int k = 0; k < 4; k++)
                    if (DiscardRollsByNth[k] > 0)
                        nsb.Append($" {nthName[k]} {(double)DiscardRollsByNth[k] / dn:F2}回×{(double)DiscardItemsByNth[k] / DiscardRollsByNth[k]:F2}品");
                sb.AppendLine($"  {PadR("  何回目で捨てたか", 20)}:{nsb}");
            }
            if (RuleGo + RuleStop > 0)
            {
                long rn = RuleGo + RuleStop;
                sb.AppendLine($"  {PadR("停止則の判断", 20)}: 回す {100.0 * RuleGo / rn:F1}% / 止める {100.0 * RuleStop / rn:F1}%"
                    + $"  見込み平均 {RuleGainSum / rn:F3} band / 費用平均 {RuleCostSum / rn:F3} band"
                    + $"  (1G={GoldBandRate:F4} / 事前値 {RerollShelfValue:F2}×{RerollPriorWeight:F0})");
            }
            if (GameLoop.GoldIncome.BySource.Count > 0)
            {
                var src = new List<KeyValuePair<string, long>>(GameLoop.GoldIncome.BySource);
                src.Sort((a, b) => b.Value.CompareTo(a.Value));
                long gtot = 0; foreach (var kv in src) gtot += kv.Value;
                sb.AppendLine($"  ── ゴールド獲得の経路別 (1ラン平均 / 計 {gtot / dn:F1}G) ──");
                foreach (var kv in src)
                    sb.AppendLine($"  {PadR("  " + kv.Key, 20)}: {kv.Value / dn,7:F1}G"
                                + $"  ({kv.Value * 100.0 / System.Math.Max(1, gtot):F1}%)");
            }
            if (ShopExitRuns > 0)
                sb.AppendLine($"  {PadR("最後に店を出た時点", 20)}: 残金 {ShopExitCoinsSum / ShopExitRuns:F1}G"
                    + $" → 終了時までの増分 {PostShopGainSum / ShopExitRuns:+0.0;-0.0}G"
                    + $"  (店を踏んだラン {100.0 * ShopExitRuns / dn:F0}%)");
            for (int k = 0; k < 2; k++)
            {
                if (ExitByOutcomeN[k] <= 0) continue;
                double en = ExitByOutcomeN[k];
                string f7 = Floor7EntryN[k] > 0
                    ? $" / 7層の店に入った時 {Floor7EntryCoins[k] / Floor7EntryN[k]:F1}G (n={Floor7EntryN[k]})" : "";
                sb.AppendLine($"  {PadR(k == 0 ? "  └ クリアしたラン" : "  └ しなかったラン", 20)}: "
                    + $"最終店 (平均 {ExitByOutcomeFloor[k] / en:F1} 層) を出た時 {ExitByOutcomeCoins[k] / en:F1}G"
                    + $" → 増分 {ExitByOutcomeGain[k] / en:+0.0;-0.0}G{f7}  (n={ExitByOutcomeN[k]})");
            }
            if (EndGoldClearN + EndGoldDeadN > 0)
                sb.AppendLine($"  {PadR("ラン終了時の残金", 20)}: "
                    + $"クリア {(EndGoldClearN > 0 ? EndGoldClearSum / EndGoldClearN : 0):F1}G"
                    + $" (30G以上 {(EndGoldClearN > 0 ? 100.0 * EndGoldClear30 / EndGoldClearN : 0):F0}%)"
                    + $" / 非クリア {(EndGoldDeadN > 0 ? EndGoldDeadSum / EndGoldDeadN : 0):F1}G"
                    + $" (30G以上 {(EndGoldDeadN > 0 ? 100.0 * EndGoldDead30 / EndGoldDeadN : 0):F0}%)"
                    + $"  ※店を出る基準 = 残金 < リロール価格 + {SurplusBuyReserve}G");
            {
                long shops = InventorySystem.Shop.ShopManager.SaleShops;
                if (shops > 0)
                {
                    double want = (double)InventorySystem.Shop.ShopManager.SaleWanted / shops;
                    double cand = (double)InventorySystem.Shop.ShopManager.SaleCandidates / shops;
                    double appl = (double)InventorySystem.Shop.ShopManager.SaleApplied / shops;
                    double pct  = 100.0 * InventorySystem.Shop.ShopManager.SaleApplied
                                / System.Math.Max(1L, InventorySystem.Shop.ShopManager.SaleWanted);
                    double saved = InventorySystem.Shop.ShopManager.SaleDiscountSum / dn;
                    sb.AppendLine($"  {PadR("商才〈特売〉", 20)}: 店 {shops / dn:F1}/ラン"
                        + $" / 希望枠 {want:F1} / 候補 {cand:F1}"
                        + $" / **実際に乗った {appl:F1}** ({pct:F0}% 消化)"
                        + $" / 浮いた金 {saved:F1}G/ラン");
                }
            }
            if (DangerSamples > 0)
                sb.AppendLine($"  {PadR("危険度の内訳", 20)}: budget 平均 {DangerBudgetSum / DangerSamples:F1}"
                    + $" / tail 平均 {DangerTailSum / DangerSamples:F1}"
                    + $" / **budget が勝つ {100.0 * DangerBudgetWins / DangerSamples:F1}%**"
                    + $" / 出力 {DangerOutSum / DangerSamples:F3}"
                    + $" (下限張付 {100.0 * DangerClampLo / DangerSamples:F1}% 上限張付 {100.0 * DangerClampHi / DangerSamples:F1}%)");
            if (GpSamples > 0)
                sb.AppendLine($"  {PadR("金逼迫度 gp の分布", 20)}: 平均 {GpSum / GpSamples:F3}"
                    + $" / gp=0 (所持{(int)GoldComfort}G以上) {100.0 * GpZero / GpSamples:F1}%"
                    + $" / gp=1 (無一文) {100.0 * GpOne / GpSamples:F1}%"
                    + $"  ※0 のとき稼ぎ項とエリート報酬項が消える");
            sb.AppendLine($"  {PadR("平均リロール回数", 20)}: {(sReroll/dn):F2}  (平均消費 {(sRerollG/dn):F1}G)");
            sb.AppendLine($"  {PadR("平均優先アイテム取得", 20)}: {(sPrio/dn):F2}");
            sb.AppendLine();

            // ================================================================
            //  敵別 脅威度 (2026-09-09)
            //
            //  **撃破数や死因だけでは「弱い敵」は見つからない。** 死因は「とどめを刺した敵」の
            //  集計なので、 一度も殺さないが毎回削ってくる敵と、 何もせず溶ける敵が同じ 0 になる。
            //  脅威は **プレイヤー最大HP に対する被ダメ%** で測る ── 生の被ダメだと層が進んで
            //  HP が伸びた分だけ大きく出て、 1層の敵が過小評価される。
            //  速度は **平均ターン数と 1〜2 ターン撃破率**。 両方低い敵が「強化対象」。
            // ================================================================
            {
                var eAgg = new Dictionary<string, int[]>();     // n, turns, fast12, won
                var eAggD = new Dictionary<string, double[]>(); // taken, takenPct, dealt, eMaxHP, floor
                foreach (var r in recs)
                {
                    if (r?.combats == null) continue;
                    foreach (var c in r.combats)
                    {
                        if (c == null || string.IsNullOrEmpty(c.enemyId)) continue;
                        if (!eAgg.TryGetValue(c.enemyId, out var a))
                        { a = new int[4]; eAgg[c.enemyId] = a; eAggD[c.enemyId] = new double[5]; }
                        var dv = eAggD[c.enemyId];
                        a[0]++; a[1] += c.turns;
                        if (c.turns <= 2) a[2]++;
                        if (c.won) a[3]++;
                        dv[0] += c.damageTaken;
                        dv[1] += c.playerMaxHpEnd > 0 ? 100.0 * c.damageTaken / c.playerMaxHpEnd : 0;
                        dv[2] += c.damageDealt;
                        dv[3] += c.enemyMaxHP;
                        dv[4] += c.floor;
                    }
                }
                if (eAgg.Count > 0)
                {
                    var ek = new List<string>(eAgg.Keys);
                    // 脅威の低い順 = 被ダメ%HP の小さい順
                    ek.Sort((x, y) => (eAggD[x][1] / eAgg[x][0]).CompareTo(eAggD[y][1] / eAgg[y][0]));
                    sb.AppendLine();
                    sb.AppendLine("---- 敵別 脅威度 (被ダメ%HP の低い順 / 遭遇 100 以上) ----");
                    sb.AppendLine("  敵ID                       層   遭遇   勝率  平均T  ≤2T率  被ダメ  被ダメ%HP  敵HP  与ダメ");
                    foreach (var k in ek)
                    {
                        var a = eAgg[k]; var dv = eAggD[k];
                        if (a[0] < 100) continue;
                        double en = a[0];
                        sb.AppendLine($"  {PadR(k, 26)} {dv[4] / en,3:F1} {a[0],6} {100.0 * a[3] / en,5:F1}% "
                                    + $"{a[1] / en,6:F1} {100.0 * a[2] / en,5:F0}% {dv[0] / en,7:F1} "
                                    + $"{dv[1] / en,9:F1}% {dv[3] / en,5:F0} {dv[2] / en,6:F0}");
                    }
                    // 〈鏡映の応答〉(4層ボス) の発火率。 閾値が高すぎ/低すぎを一目で判る形にする。
                    {
                        long h = InventorySystem.PassiveSkills.Effects.MirrorTwinsResponse.Hits;
                        long f = InventorySystem.PassiveSkills.Effects.MirrorTwinsResponse.Fires;
                        long rd = InventorySystem.PassiveSkills.Effects.MirrorTwinsResponse.ReflectDamage;
                        if (h > 0)
                        {
                            sb.AppendLine($"  [鏡映の応答] 被弾 {h} 回中 {f} 回発火 ({100.0 * f / h:F1}%) / "
                                        + $"反射合計 {rd} (1発火あたり {(f > 0 ? (double)rd / f : 0):F1})");
                            sb.AppendLine("    1ヒットの威力 (ボス最大HP比) の分位: "
                                        + InventorySystem.PassiveSkills.Effects.MirrorTwinsResponse.DescribePercentiles()
                                        + "  ← 閾値をここから選ぶ");
                        }
                        else
                            sb.AppendLine("  [鏡映の応答] 被弾イベント 0 ── 4層ボスに到達していないか計装が繋がっていない");
                    }
                    sb.AppendLine("  ※ 被ダメ%HP = その戦闘の被ダメ ÷ 戦闘終了時のプレイヤー最大HP。");
                    sb.AppendLine("     **被ダメ%HP が低く、 かつ ≤2T率 が高い敵が強化対象** ── 何もせず溶けている。");
                }
            }

            // --- 5F突入時 確信チェーン進行状況 ---
            // 5F到達ランのみ対象。 各種フラグ/パッシブ所持割合を出して、 どこで詰まったか分析する。
            var arrived5F = recs.FindAll(r => r.convictionStageAt5F >= 0);
            if (arrived5F.Count > 0)
            {
                sb.AppendLine("---- 5F突入時 確信チェーン進行 ----");
                int total5F = arrived5F.Count;
                int yogen   = arrived5F.FindAll(r => r.hadFlagYogenAt5F).Count;
                int kakushin= arrived5F.FindAll(r => r.hadFlagKakushinAt5F).Count;
                int gen     = arrived5F.FindAll(r => r.hadConvictionItem5F).Count;
                int ketsui  = arrived5F.FindAll(r => r.hadResolveAt5F).Count;
                int shinri  = arrived5F.FindAll(r => r.hadTruthAt5F).Count;
                int s0 = arrived5F.FindAll(r => r.convictionStageAt5F == 0).Count;
                int s1 = arrived5F.FindAll(r => r.convictionStageAt5F == 1).Count;
                int s2 = arrived5F.FindAll(r => r.convictionStageAt5F == 2).Count;
                int s3 = arrived5F.FindAll(r => r.convictionStageAt5F == 3).Count;
                int s4plus = arrived5F.FindAll(r => r.convictionStageAt5F >= 4).Count;
                sb.AppendLine($"  5F到達ラン数      : {total5F}");
                sb.AppendLine($"  フラグ[苦難の予言]: {PadL(yogen.ToString(),4)}  ({Pct(yogen, total5F)})  ※チェーン1段目完了");
                sb.AppendLine($"  フラグ[苦難の確信]: {PadL(kakushin.ToString(),4)}  ({Pct(kakushin, total5F)})  ※チェーン2段目完了");
                sb.AppendLine($"  〈根拠のない確信〉: {PadL(gen.ToString(),4)}  ({Pct(gen, total5F)})  ※チェーン3段目完了 (stage 1)");
                sb.AppendLine($"  〈決意〉所持      : {PadL(ketsui.ToString(),4)}  ({Pct(ketsui, total5F)})  ※stage 2-3 = 6F進入可");
                sb.AppendLine($"  〈真理〉所持      : {PadL(shinri.ToString(),4)}  ({Pct(shinri, total5F)})  ※stage 4+ = 7F進入可");
                sb.AppendLine($"  stage 0           : {PadL(s0.ToString(),4)}  ({Pct(s0, total5F)})");
                sb.AppendLine($"  stage 1           : {PadL(s1.ToString(),4)}  ({Pct(s1, total5F)})");
                sb.AppendLine($"  stage 2           : {PadL(s2.ToString(),4)}  ({Pct(s2, total5F)})");
                sb.AppendLine($"  stage 3           : {PadL(s3.ToString(),4)}  ({Pct(s3, total5F)})");
                sb.AppendLine($"  stage 4+          : {PadL(s4plus.ToString(),4)}  ({Pct(s4plus, total5F)})");
                sb.AppendLine();
            }

            // --- タイル踏破分布（イベント実遭遇数の検証用） ---
            // 1ランあたり、各タイル種別を実際に何回起動したか（再訪・消化済みは含まない）。
            // 「イベントを順に3回踏むのが難しい」仮説の真偽を実数で確認する。
            if (recs.Count > 0)
            {
                int runN = recs.Count;
                sb.AppendLine("---- タイル踏破分布（1ランあたり平均・実起動回数） ----");
                var order = new[]
                {
                    TileType.Battle, TileType.EliteBattle, TileType.Event, TileType.Exchange,
                    TileType.Shop, TileType.Treasure, TileType.Rest, TileType.Trap,
                };
                foreach (var tt in order)
                {
                    long sum = 0; int runsWithAny = 0;
                    foreach (var r in recs)
                    {
                        r.tileVisits.TryGetValue(tt, out int c);
                        sum += c;
                        if (c > 0) runsWithAny++;
                    }
                    double avg = (double)sum / runN;
                    sb.AppendLine($"  {PadR(tt.ToString(), 12)}: 平均 {avg:F2}/ラン  (総{sum}, 1回以上踏破 {Pct(runsWithAny, runN)})");
                }
                sb.AppendLine();
            }

            // --- 6/7層ボス戦のプレイヤー回復・シールド量（1戦平均・検証用） ---
            // 回復/シールド依存ビルドが各層ボスでどれだけ"延命資源"を獲得しているかを可視化。
            // 1戦 = OnBattleEnded で確定した戦闘（7層ヴェスカ連戦は最終段の1件に連戦全体の累計を計上）。
            {
                long h6 = 0, s6 = 0, h7 = 0, s7 = 0; int n6 = 0, n7 = 0;
                foreach (var r in recs)
                    foreach (var cb in r.combats)
                    {
                        if (!cb.isFightEnd || !cb.isBoss) continue;
                        if (cb.floor == 6) { h6 += cb.healApplied; s6 += cb.shieldGained; n6++; }
                        else if (cb.floor == 7) { h7 += cb.healApplied; s7 += cb.shieldGained; n7++; }
                    }
                sb.AppendLine("---- 6/7層ボス戦 プレイヤー回復・シールド量（1戦平均） ----");
                sb.AppendLine($"  6層ボス: {n6}戦  回復 {(n6 > 0 ? (double)h6 / n6 : 0):F1}/戦  シールド {(n6 > 0 ? (double)s6 / n6 : 0):F1}/戦");
                sb.AppendLine($"  7層ボス: {n7}戦  回復 {(n7 > 0 ? (double)h7 / n7 : 0):F1}/戦  シールド {(n7 > 0 ? (double)s7 / n7 : 0):F1}/戦");
                sb.AppendLine("  （7層はヴェスカ連戦全体の累計を1戦として計上）");
                sb.AppendLine();
            }

            // --- 敵・ボス脅威度 ---
            // key = displayName。boss(enemyId が boss_layer*)は別エントリとして分離集計。
            sb.AppendLine("---- 敵・ボス脅威度 ----");
            var agg = new Dictionary<string, int[]>();    // key -> [0enc,1wins,2losses,3turns,4hpLost,5fatal, 6tWin,7tDraw,8tLoss,9tLossAbs]
            var isBossKey = new Dictionary<string, bool>(); // key -> boss か
            var dispName = new Dictionary<string, string>(); // key -> 表示名

            string KeyOf(CombatRec c)
            {
                bool boss = c.isBoss || GameLoop.BossIds.IsBoss(c.enemyId);
                string nm = string.IsNullOrEmpty(c.enemy) ? (c.enemyId ?? "?") : c.enemy;
                string key = boss ? nm + "BOSS" : nm;
                isBossKey[key] = boss;
                dispName[key] = nm;
                return key;
            }

            foreach (var r in recs)
            {
                foreach (var c in r.combats)
                {
                    string key = KeyOf(c);
                    if (!agg.TryGetValue(key, out var a)) { a = new int[12]; agg[key] = a; }
                    a[0]++;
                    if (c.won) a[1]++; else a[2]++;
                    a[3] += c.turns;
                    a[4] += Math.Max(0, c.hpBefore - c.hpAfter);
                    a[6] += c.tWin; a[7] += c.tDraw; a[8] += c.tLoss; a[9] += c.tLossAbs;
                    // 勝T/敗T の分離: 勝T=削り切る時間(敵HPの尺度) / 敗T=保つ時間(敵加害の尺度)
                    if (c.won) a[10] += c.turns; else a[11] += c.turns;
                }
                // 致命: そのランの最後の「敗北」戦闘をゲームオーバー要因と見なす
                if (r.outcome == Outcome.GameOver && r.combats != null && r.combats.Count > 0)
                {
                    CombatRec fatal = null;
                    for (int i = r.combats.Count - 1; i >= 0; i--)
                        if (!r.combats[i].won) { fatal = r.combats[i]; break; }
                    if (fatal != null)
                    {
                        string key = KeyOf(fatal);
                        if (!agg.TryGetValue(key, out var a)) { a = new int[12]; agg[key] = a; }
                        a[5]++;
                    }
                }
            }

            var keys = new List<string>(agg.Keys);
            keys.Sort((x, y) =>
            {
                // 勝率の低い順（危険＝主指標）。同率は致命数の多い順→遭遇数の多い順。
                double wx = agg[x][0] == 0 ? 1.0 : (double)agg[x][1] / agg[x][0];
                double wy = agg[y][0] == 0 ? 1.0 : (double)agg[y][1] / agg[y][0];
                int c = wx.CompareTo(wy);
                if (c != 0) return c;
                int f = agg[y][5].CompareTo(agg[x][5]);
                return f != 0 ? f : agg[y][0].CompareTo(agg[x][0]);
            });
            sb.AppendLine($"  {PadR("敵名",18)}{PadR("種別",6)}{PadL("遭遇",5)} {PadL("勝率",6)} {PadL("平均T",7)} {PadL("勝T",6)} {PadL("敗T",6)} {PadL("平均被ダメ",11)} {PadL("致命",5)}");
            foreach (var k in keys)
            {
                var a = agg[k];
                string wr = a[0] == 0 ? "-" : (100.0*a[1]/a[0]).ToString("F0")+"%";
                string at = a[0] == 0 ? "-" : ((double)a[3]/a[0]).ToString("F1");
                string wt = a[1] == 0 ? "-" : ((double)a[10]/a[1]).ToString("F1");
                string lt = a[2] == 0 ? "-" : ((double)a[11]/a[2]).ToString("F1");
                string ad = a[0] == 0 ? "-" : ((double)a[4]/a[0]).ToString("F1");
                string nm = dispName.TryGetValue(k, out var dn2) ? dn2 : k;
                string kind = isBossKey.TryGetValue(k, out var b) && b ? "BOSS"
                            : (!string.IsNullOrEmpty(nm) && nm.StartsWith("精鋭")) ? "精鋭"
                            : "雑魚";
                sb.AppendLine($"  {PadR(TruncDisp(nm,16),18)}{PadR(kind,6)}{PadL(a[0].ToString(),5)} {PadL(wr,6)} {PadL(at,7)} {PadL(wt,6)} {PadL(lt,6)} {PadL(ad,11)} {PadL(a[5].ToString(),5)}");
            }
            sb.AppendLine();
            sb.AppendLine("（勝率の低い順にソート。致命=そのランをゲームオーバーに導いた回数）");
            sb.AppendLine("（勝T=撃破できた戦闘の平均ターン＝敵HPの尺度 / 敗T=力尽きた戦闘の平均ターン＝敵加害の尺度）");
            sb.AppendLine();

            // 武器×ダイス 組み合わせ別 戦闘勝率は 2026-06-21 削除 (バンドが肥大化、 ビルド多様性ブロックで代替)。

            // --- ボス戦ターン内訳（非解決グラインドの原因特定） ---
            // ADR-0009 でロール勝負は存在しない。 ここでの 収支+/0/− は
            //   balance = (与ダメ + 固定ダメ) − 被ダメ  の符号 (CombatManager.cs の result.playerWon/isDraw)。
            // 「無為T」= 収支マイナスのターンのうち **プレイヤーの与ダメが 0** だったターン。
            //   旧ラベルは「吸収敗＝シールドで被弾を防いだ」と読める書き方だったが、
            //   実際に見ているのは tr.totalDamage (= プレイヤーが与えた量) なので主語が逆だった。
            sb.AppendLine("---- ボス戦ターン内訳（収支の符号別 ％・無為T=収支マイナスかつ与ダメ0） ----");
            sb.AppendLine($"  {PadR("ボス名",16)}{PadL("総T",6)} {PadL("収支+",6)} {PadL("収支0",6)} {PadL("収支-",6)} {PadL("無為T%",8)}");
            foreach (var k in keys)
            {
                if (!(isBossKey.TryGetValue(k, out var b) && b)) continue;
                var a = agg[k];
                int tot = a[6] + a[7] + a[8];
                if (tot == 0) continue;
                string nm = dispName.TryGetValue(k, out var dn3) ? dn3 : k;
                string pw = (100.0*a[6]/tot).ToString("F0")+"%";
                string pd = (100.0*a[7]/tot).ToString("F0")+"%";
                string pl = (100.0*a[8]/tot).ToString("F0")+"%";
                string pa = a[8]==0 ? "-" : (100.0*a[9]/a[8]).ToString("F0")+"%";
                sb.AppendLine($"  {PadR(TruncDisp(nm,16),16)}{PadL(tot.ToString(),6)} {PadL(pw,6)} {PadL(pd,6)} {PadL(pl,6)} {PadL(pa,8)}");
            }
            sb.AppendLine("（無為T% = 収支マイナスのターンのうち、プレイヤーが1もダメージを与えられなかった割合。");
            sb.AppendLine("　高い = 一方的に殴られている＝手が出ていない。 低い = 削り合いにはなっている）");
            sb.AppendLine();

            // --- 6Fクリア時ビルドスナップショット（サルベージ用） ---
            // 一旦オフ: ログが煩雑になるため出力を抑止（再有効化は EmitClearBuildSnapshot=true）。
            const bool EmitClearBuildSnapshot = false;
            var snaps = recs.FindAll(r => !string.IsNullOrEmpty(r.clear6FSnapshot));
            if (EmitClearBuildSnapshot && snaps.Count > 0)
            {
                sb.AppendLine($"---- 6Fクリア時ビルドスナップショット ({snaps.Count}件・サルベージ用) ----");
                foreach (var r in snaps)
                {
                    sb.AppendLine($"  ● RUN {r.index} [{r.bandLabel}]");
                    sb.AppendLine($"      {r.clear6FSnapshot}");
                }
                sb.AppendLine();
            }

            // --- デッドロック発生時の状況（原因特定用・最下段） ---
            var dls = recs.FindAll(r => r.band == "DEADLOCK");
            if (dls.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"---- デッドロック発生時の状況（{dls.Count}件・原因特定用） ----");
                foreach (var r in dls)
                {
                    sb.AppendLine($"  ● RUN {r.index} : {r.note}");
                    sb.AppendLine($"    reached F{r.reachedFloor}  HP {r.finalHP}/{r.finalMaxHP}  " +
                                  $"coins {r.finalCoins}  combats {r.totalWins}/{r.totalCombats}  " +
                                  $"LS={r.lastStandUsed}");
                    if (_detail.TryGetValue(r.index, out var lines) && lines.Count > 0)
                    {
                        const int tail = 40;
                        int start = Math.Max(0, lines.Count - tail);
                        sb.AppendLine($"    ── 発生時点周辺ログ（末尾 {lines.Count - start} 行 / 総 {lines.Count} 行） ──");
                        for (int i = start; i < lines.Count; i++)
                            sb.AppendLine($"    | {lines[i]}");
                    }
                    else
                    {
                        sb.AppendLine("    （ログ未取得）");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("=============================================");
            return sb.ToString();
        }

        // ===== 表示幅(全角=2)対応の整列ヘルパー =====

        private static int CharW(char ch)
        {
            if (ch < 0x1100) return 1;
            return
                (ch >= 0x1100 && ch <= 0x115F) ||                       // Hangul Jamo
                (ch >= 0x2E80 && ch <= 0xA4CF && ch != 0x303F) ||       // CJK..Yi
                (ch >= 0xAC00 && ch <= 0xD7A3) ||                       // Hangul Syllables
                (ch >= 0xF900 && ch <= 0xFAFF) ||                       // CJK Compat Ideographs
                (ch >= 0xFE30 && ch <= 0xFE4F) ||                       // CJK Compat Forms
                (ch >= 0xFF00 && ch <= 0xFF60) ||                       // Fullwidth Forms
                (ch >= 0xFFE0 && ch <= 0xFFE6)
                ? 2 : 1;
        }

        private static int DispWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            foreach (var c in s) w += CharW(c);
            return w;
        }

        /// <summary>表示幅で左詰めパディング。</summary>
        private static string PadR(string s, int w)
        {
            s = s ?? "";
            int d = DispWidth(s);
            return d >= w ? s : s + new string(' ', w - d);
        }

        /// <summary>表示幅で右詰めパディング。</summary>
        private static string PadL(string s, int w)
        {
            s = s ?? "";
            int d = DispWidth(s);
            return d >= w ? s : new string(' ', w - d) + s;
        }

        /// <summary>表示幅で切り詰め。</summary>
        private static string TruncDisp(string s, int w)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            int acc = 0;
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                int cw = CharW(c);
                if (acc + cw > w) break;
                sb.Append(c);
                acc += cw;
            }
            return sb.ToString();
        }

        private string BuildJsonl()
        {
            var sb = new StringBuilder();
            foreach (var r in _records)
                sb.AppendLine(JsonUtility.ToJson(r));
            return sb.ToString();
        }

        private string BuildDetail()
        {
            var sb = new StringBuilder();
            foreach (var r in _records)
            {
                sb.AppendLine($"########## RUN {r.index}  [{r.band} {r.bandLabel}]  {r.note}");
                sb.AppendLine($"# reached F{r.reachedFloor} HP {r.finalHP}/{r.finalMaxHP} coins {r.finalCoins} " +
                              $"combats {r.totalWins}/{r.totalCombats} LS={r.lastStandUsed}");
                if (_detail.TryGetValue(r.index, out var lines))
                    foreach (var l in lines) sb.AppendLine(l);
                sb.AppendLine();
            }
            return sb.ToString();
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

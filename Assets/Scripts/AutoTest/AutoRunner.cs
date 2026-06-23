using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using GameLoop;
using CombatSystem;
using MapSystem;
using EventSystem;
using InventorySystem;
using InventorySystem.Shop;

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
        [Tooltip("バッチ中 VSync を無効化しフレームレート上限を解除する（60fps→数百fps化で大幅高速化）")]
        public bool disableVSyncDuringBatch = true;
        [Tooltip("1ランあたりの最大ループ反復数。超過でDEADLOCK判定")]
        public int maxIterationsPerRun = 4000;
        [Tooltip("同一フェーズが進展なく続いた反復数の上限。超過でDEADLOCK判定")]
        public int stallLimit = 400;
        [Tooltip("詳細ログ(全ランのナラティブ)を書き出す。 1バッチで 30MB+ に膨らむため自動周回時はOFF推奨")]
        public bool writeDetailLog = false;
        [Tooltip("詳細ログの1ランあたり最大行数。超過時は古い行を捨て末尾(決定的な終端)を必ず保持")]
        public int detailMaxLinesPerRun = 5000;
        [Tooltip("バッチ中 Debug.Log を完全抑止 + StackTrace を無効化 (Editor RAM爆発防止)。 10Kラン×400Log×StackTrace 5KB = 20GB+ の蓄積を阻止")]
        public bool suppressLogsDuringBatch = true;
        [Tooltip("バッチ終了時に Editor コンソールをクリア + GC.Collect (蓄積したログを即解放)")]
        public bool clearConsoleAfterBatch = true;
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

        [Tooltip("メタ恒久進行モード: Cowardly=全リセット / FullProgression=全段解放 / Untouched=保存値そのまま")]
        public MetaPattern metaPattern = MetaPattern.Cowardly;

        /// <summary>メタデバフ(挑戦モード)を全ON にするか。 最高難易度プレイ計測用。</summary>
        [Tooltip("メタデバフ Lv1-10 を全ON にする (最高難易度モード)")]
        public bool enableAllDebuffs = false;

        /// <summary>バッチ実行時のメタプロファイル。 学習データ分離 + Meta系切替の主軸。
        /// Begin 時に MetaProfileHelper.SetCurrent し、 metaPattern / enableAllDebuffs を自動上書き。</summary>
        [Tooltip("メタプロファイル (バフ/デバフ ON/OFF)。 学習データの分離キー兼 Meta系切替の主軸")]
        public MetaProfile metaProfile = MetaProfile.BuffOn_DebuffOff;

        /// <summary>学習モード: 何を更新(成長)させ、 何を凍結するかを切り替える (排他)。</summary>
        public enum LearningMode
        {
            /// <summary>従来挙動: Tier表(item_stats/regression/MD) と AIルーチン(policy/event) を両方更新。</summary>
            TierAndAi,
            /// <summary>Tier表更新モード: item_stats / regression / MD を更新。 BOT挙動(policy/event)は凍結 = 純粋なバランス計測。</summary>
            TierOnly,
            /// <summary>AIルーチン学習モード: policy / event を成長。 Tier表(item_stats/regression/MD)は凍結。 BOTは凍結済みTierを読みつつ立ち回りだけ最適化。</summary>
            AiOnly,
            /// <summary>ボス難易度オートチューナーのみ: ボス係数を自動調整。 Tier/AI(policy/event)は全凍結。
            /// ボス環境が変動するため他学習と混ぜると統計が濁る → 排他にして単独実行。</summary>
            BossTuning,
        }

        [Tooltip("学習モード(排他): TierAndAi=両方更新 / TierOnly=Tier表のみ / AiOnly=AIルーチンのみ / BossTuning=ボス難易度オートチューナーのみ(Tier/AI凍結)")]
        public LearningMode learningMode = LearningMode.TierAndAi;

        /// <summary>Tier表 (item_stats / regression / BALANCE_TIER_LIST.md) を更新するか。 BossTuning時は凍結。</summary>
        public bool UpdatesTier => learningMode == LearningMode.TierAndAi || learningMode == LearningMode.TierOnly;
        /// <summary>AIルーチン (policy / event_stats) を成長させるか。 BossTuning時は凍結。</summary>
        public bool UpdatesAi => learningMode == LearningMode.TierAndAi || learningMode == LearningMode.AiOnly;
        /// <summary>ボス難易度オートチューナーを動かすか (BossTuningモードのみ)。</summary>
        public bool BossAutoTune => learningMode == LearningMode.BossTuning;

        // 旧フィールド (互換用、内部では metaPattern を見る)
        [System.Obsolete("metaPattern を使用してください")] public bool resetMetaForCleanBaseline = true;

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
        public int simBaseHP = 30;
        [Tooltip("スイープ対象武器(種別+Tier＋ユニーク)。存在しないIDは自動スキップ")]
        public string[] simWeapons = {
            "sword_t3","sword_t4","axe_t3","axe_t4","dagger_t3","dagger_t4",
            "shield_t3","shield_t4","curse_t3","curse_t4",
            // ユニーク/特殊武器（非進行）。1個ダイス武器は完全性の評価にも有用
            "ryusen","dead_staff"
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
        [Tooltip("スイープするΛファーム量(踏破マス数)。スポークは3マス毎なので中央到達は3の倍数に丸められる")]
        public int[] lambdaFarmSweepValues = { 3, 6, 9, 12, 16, 18, 21, 24, 27, 30 };
        private string _lambdaSweepReport;

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
            public bool deathInBossFight;
            public string fatalEnemy = "";
            public int reachedFloor = 1;
            public bool reached6F;
            public int finalHP;
            public int finalMaxHP;
            public int finalCoins;
            public int peakCoins;
            public int totalGoldGained;
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
            public int shopRerolls;
            public int shopRerollCoins;
            public int priorityItemsAcquired; // S/A 級を取得した回数
            public int inventoryExpansionsPurchased; // ショップでインベントリ列拡張を購入した回数 (0-4)
            public int passivesDiscarded;     // インベントリ容量超過で廃棄したパッシブ数 (案A・取捨選択)
            public int sublimationsTotal;     // 〈昇華〉実行回数 (永久パッシブ化で枠を空けた回数)
            public int tierUpgradeCount;      // 強化で Tier ID が変わった回数 (T1→T2 等、 +昇格は含まず)
            public string finalWeaponTier = ""; // 終了時の武器 Tier (例: "shield_t4")
            public int finalLimitBreak;       // 終了時の業物 lv
            public int totalTurns;
            public bool lastStandUsed;
            public int lastStandFloor;
            public int combatsAfterLastStand;
            public int winsAfterLastStand;
            public string profile = "";    // 行動ルーチン: "貪欲"(戦闘貪欲) / "回避"(戦闘回避)
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

            // 旅団契約システム (docs/specs/contracts.md) の統計
            public readonly Dictionary<GameLoop.Contracts.ContractKind, int> contractsSigned
                = new Dictionary<GameLoop.Contracts.ContractKind, int>();
            public readonly Dictionary<GameLoop.Contracts.ContractKind, int> contractsExtended
                = new Dictionary<GameLoop.Contracts.ContractKind, int>();
            public int contractsForcedReleased;  // 敵対による強制解除
            public int contractsHpReleased;      // HP20% 解除
            public int contractsShortfallReleased; // 維持費不足解除
            public int contractMaintenancePaid;  // 維持費総支払額
            public int contractOutpostsVisited;  // 前哨基地で契約処理した回数
            public List<string> contractsFinalActive = new List<string>(); // ラン終了時の「Name Lv」 一覧
            public string note = "";
            public List<CombatRec> combats = new List<CombatRec>();
            /// <summary>6F (灰燼の王) 撃破時点のビルド情報スナップショット。
            /// 後から「どんな装備で6Fまで到達できたか」をサルベージするための行単位プレーンテキスト。
            /// null = 6F未到達(または到達前にラン終了)。</summary>
            public string clear6FSnapshot;

            /// <summary>解脱: 覚者・妙覚のサドンデス勝利で完全クリアした場合 true。</summary>
            public bool gedatsuVictory;

            /// <summary>Λスイープ: このランで使用した目標ファームマス数（非スイープ時は0）。</summary>
            public int lambdaFarmTilesUsed;
            /// <summary>このランでΛ層へ突入したか。</summary>
            public bool enteredLambda;
            /// <summary>Λ層滞在中に獲得したゴールド量（離脱時 or Λ内死亡時に確定）。</summary>
            public int lambdaGoldGained;
            /// <summary>2026-06-22: Λ で実際に追加されたアイテム総数 (gross, triage 削除前)。</summary>
            public int lambdaItemsAcquiredGross;
            /// <summary>2026-06-22: Λ 滞在中に triage で discard されたアイテム数 (容量圧迫ロス)。</summary>
            public int lambdaItemsDiscardedDuringLambda;
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
            /// <summary>このランで実際に与えた累計ダメージ（全戦闘合算）。</summary>
            public long totalDamageDealt;
            /// <summary>このランで実際に受けた累計ダメージ（heal込みのgross）。</summary>
            public long totalDamageTaken;
            /// <summary>このランで獲得した回復量（healApplied）合算。</summary>
            public long totalHealed;
            /// <summary>このランで獲得したシールド量合算。</summary>
            public long totalShieldGained;
            /// <summary>覚者連戦で撃破した形態の id 集合（boss_layer7_p1..p7 等）。
            /// 妙覚到達後の完全撃破は gedatsuVictory が true。</summary>
            public HashSet<string> awakenedFormsKilled = new HashSet<string>();
            /// <summary>帯ランクの数値表記 (R1=1, R10=10, 解脱=12, CRASH=-1, DEADLOCK=-2)。集計用。</summary>
            public int bandScore;
            /// <summary>2026-06-22: 各層末時点の InventoryPower スナップショット (floor → power)。
            /// ラン中の戦力推移、 売買コスパ計算、 Tier別ビルド比較に使う。</summary>
            public Dictionary<int, int> inventoryPowerByFloor = new Dictionary<int, int>();
            /// <summary>ラン終了時点 (死亡/クリア) の InventoryPower。</summary>
            public int finalInventoryPower;
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
        private readonly System.Random _rng = new System.Random();
        // 行動ルーチン分割: 前半50%=戦闘貪欲 / 後半50%=戦闘回避（航行Rankのみ差し替え）
        private bool _curCombatAverse;
        private bool _curBossNear;   // 直近のDoNavigateで判定したボス接近フラグ（休憩判断で参照）
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
            // メタプロファイルをグローバル反映 (学習データ分離 + Meta系切替)
            MetaProfileHelper.SetCurrent(metaProfile);
            // プロファイルから metaPattern / enableAllDebuffs を自動上書き
            //   BuffOn  → FullProgression (メタバフ全段解放)
            //   BuffOff → Cowardly        (メタバフ全リセット)
            //   DebuffOn → enableAllDebuffs = true
            metaPattern      = MetaProfileHelper.CurrentBuffOn
                ? MetaPattern.FullProgression : MetaPattern.Cowardly;
            enableAllDebuffs = MetaProfileHelper.CurrentDebuffOn;
            Debug.Log($"[AutoRunner] メタプロファイル: {MetaProfileHelper.DisplayName(metaProfile)} → metaPattern={metaPattern}, debuffsOn={enableAllDebuffs}");

            // プロファイル切替で参照先サブディレクトリが変わるため、 各キャッシュを再読み込み
            try { PolicyParameters.ReloadFromDisk(MetaProfileHelper.LearningRoot()); } catch { }
            try { EventChoiceLearningStats.Reload(MetaProfileHelper.LearningRoot()); } catch { }
            try { LearnedPriorityProvider.Reload(MetaProfileHelper.LearningRoot(), writeMarkdown: UpdatesTier); } catch { }
            try { BossTuning.Reload(MetaProfileHelper.LearningRoot()); } catch { }

            if (autoLoopBatches >= 2)
                StartCoroutine(RunAutoLoop());
            else
                StartCoroutine(RunBatch());
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
            sb.AppendLine($"- **学習モード**: {learningMode} / ボスオートチューン: {(BossAutoTune ? "ON" : "OFF")}");
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
                Diff("hpLowThreshold", policyStart.hpLowThreshold, p.hpLowThreshold);
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
            var prevStackLog  = Application.GetStackTraceLogType(LogType.Log);
            var prevStackWarn = Application.GetStackTraceLogType(LogType.Warning);
            var prevStackErr  = Application.GetStackTraceLogType(LogType.Error);
            if (suppressLogsDuringBatch)
            {
                Debug.unityLogger.logEnabled = false;
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
                    Application.SetStackTraceLogType(LogType.Log,     prevStackLog);
                    Application.SetStackTraceLogType(LogType.Warning, prevStackWarn);
                    Application.SetStackTraceLogType(LogType.Error,   prevStackErr);
                }
                yield break;
            }

            // L1学習データを読み込み、 動的 PriorityItemList を構築。
            //  BOTは常に Tier を読む必要がある (S/A/B 判定) ので Reload 自体は常に実行。
            //  ただし MD 再生成は Tier更新モードのみ (AIルーチン学習モードでは Tier表凍結)。
            LearnedPriorityProvider.Reload(writeMarkdown: UpdatesTier);
            Debug.Log($"[AutoRunner] {LearnedPriorityProvider.LastLoadedSummary}");
            // L1.5: イベント学習リフトを読み込み
            EventChoiceLearningStats.Reload();
            // L2: policy.json を読み込み (前バッチの摂動結果を引き継ぐ)
            PolicyParameters.ReloadFromDisk();
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
            else
            {
                bool paired = PairedChallengerPolicy != null;
                Debug.Log($"[AutoRunner] バッチ開始: {runCount} ラン (ペアテスト={(paired ? "ON" : "OFF")})");
                // ペア時間隔: 2連続を同シードで実行 (i, i+1) → 偶数=baseline, 奇数=challenger
                for (int i = 0; i < runCount; i++)
                {
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
                    yield return null;
                }
                if (paired)
                    PolicyParameters.SetCurrent(_baselinePolicySnap); // 戻す
            }

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
                Application.SetStackTraceLogType(LogType.Log,     prevStackLog);
                Application.SetStackTraceLogType(LogType.Warning, prevStackWarn);
                Application.SetStackTraceLogType(LogType.Error,   prevStackErr);
            }

            // Editor コンソール強制クリア + GC: 抑止しても少量蓄積 + メモリ即時解放
            if (clearConsoleAfterBatch) ClearEditorConsoleAndCollect();

            string dir = WriteLogs();
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
            Debug.Log($"[AutoRunner] バッチ完了。ログ出力先:\n{dir}");

            if (exitPlayModeWhenDone && !_suppressExitDuringLoop)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }

        /// <summary>Λファーム量スイープ: lambdaFarmSweepValues の各値で runCount ラン回し、
        /// ファーム量別の Λ突入/6F到達/7Fクリア/解脱/死亡 を採取して表形式で出力する。</summary>
        private IEnumerator RunLambdaFarmSweep(GameManager gm)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Λ層 ファーム量スイープ ===");
            sb.AppendLine($"日時      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"メタ進行: {metaPattern} / メタデバフ全ON: {enableAllDebuffs} / 各値 {runCount} ラン");
            sb.AppendLine("※ Λ突入には〈決意〉以上が必要。突入したランのみが分母として意味を持つ。");
            sb.AppendLine("※ 中央離脱はスポーク(3マス毎)でのみ可能なため、実踏破マスは目標値を3の倍数へ丸めた値。");
            sb.AppendLine($"※ 期待Λデバフ付与回数 ≒ 踏破マス/3（同種再付与で段階上昇）。");
            sb.AppendLine();
            sb.AppendLine("目標 | runs | Λ突入 | 6F到達 | 7Fクリア | 解脱 | 死亡 | 突入別6F% | 突入別7F% | 平均踏破 | 平均デバフLv計");
            sb.AppendLine("-----+------+-------+--------+----------+------+------+-----------+-----------+----------+--------------");

            foreach (int v in lambdaFarmSweepValues)
            {
                lambdaFarmTiles = v;
                int start = _records.Count;
                for (int i = 0; i < runCount; i++)
                {
                    yield return RunOne(i);
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
                    if (r.gedatsuVictory) ged++;
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
        }

        private IEnumerator RunOne(int index)
        {
            var gm = GameManager.Instance;
            // 全ラン戦闘貪欲ルーチン（戦闘回避ルーチンは廃止）
            _curCombatAverse = false;
            _cur = new RunRec { index = index, profile = "貪欲" };
            GameLoop.HopeSystem.Stats.Reset(); // 希望の発生源別収支を1ラン単位で集計
            GameLoop.Contracts.ContractManager.Instance.Stat_HpReleaseCount = 0; // 契約 HP20% 解除カウンタ
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
                if (gm.CurrentPhase == GameManager.GamePhase.GameOver ||
                    gm.CurrentPhase == GameManager.GamePhase.RunClear)
                    gm.ReturnToTitle();
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
                        MetaProgression.MetaProgressManager.Instance?.MaxAllForTesting();
                        break;
                    case MetaPattern.Untouched:
                        // 何もしない
                        break;
                }

                // メタデバフトグル (最高難易度モード)
                var mgr = MetaProgression.MetaProgressManager.Instance;
                if (mgr?.State != null)
                {
                    for (int lv = 1; lv <= 10; lv++)
                        mgr.ToggleDebuff((MetaProgression.MetaDebuffLevel)lv, enableAllDebuffs);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[AutoRunner] Meta pattern={metaPattern}: {e.Message}"); }

            gm.StartNewRun();
            _prevCoins = gm.Run != null ? gm.Run.coins : 0;
            _prevMaterials = gm.Run != null ? gm.Run.weaponMaterials : 0;

            int iter = 0;
            int stall = 0;
            var lastPhase = gm.CurrentPhase;
            string lastNode = CurrentNodeId();
            int lastFloor = gm.Run?.currentFloor ?? 1;
            int lastHp = gm.Run?.playerHP ?? 0;

            while (true)
            {
                if (_exceptionFlag)
                {
                    Finish(Outcome.Crash, $"例外: {_exceptionMsg}");
                    yield break;
                }
                if (iter++ > maxIterationsPerRun)
                {
                    Finish(Outcome.Deadlock, $"反復上限超過 phase={gm.CurrentPhase} node={CurrentNodeId()}");
                    yield break;
                }

                var phase = gm.CurrentPhase;

                // 進展のないストール検出
                string node = CurrentNodeId();
                int fl = gm.Run?.currentFloor ?? 0;
                int hp = gm.Run?.playerHP ?? 0;
                if (phase == lastPhase && node == lastNode && fl == lastFloor && hp == lastHp)
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

            // 〈昇華〉(案・Phase4): 満杯かつ素材に余裕があれば、最良の刻印持ちパッシブを永久化して枠を空ける。
            // 取捨選択より先に試す（良い品を昇華で救い、枠を空けてから余剰を廃棄する流れ）。
            if (gm?.Run != null) TryBotSublimate(gm.Run);

            // 取捨選択(案A): 直前のアクションで得たファーム品で容量超過していたら、
            // チェーンアイテムを除く最下位パッシブから容量内に収まるまで廃棄する。
            if (gm?.Run != null)
            {
                int discarded = AutoTest.InventoryTriage.EnforceCapacity(gm.Run);
                if (discarded > 0 && _cur != null) _cur.passivesDiscarded += discarded;
            }

            switch (phase)
            {
                case GameManager.GamePhase.Title:
                case GameManager.GamePhase.RunStart:
                case GameManager.GamePhase.FloorIntro:
                    return false; // GameManager が即座に MapNavigation へ遷移

                case GameManager.GamePhase.MapNavigation:
                    return DoNavigate();

                case GameManager.GamePhase.Combat:
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
                    if (nextStepReachesT4 && hpR > 0.25f)
                        gm.RestUpgrade();                    // T4 寸前は HP低めでも強化 (機会逃さない)
                    else if (richMaterials && hpR > 0.30f)
                        gm.RestUpgrade();                    // 素材余剰: 危機未満なら強化を優先
                    else if (greedyBossPrep && hpR < 0.8f)
                        gm.RestHeal();                       // 貪欲: ボス前にHPを整える
                    else if (hpR <= 0.40f)
                        gm.RestHeal();                       // 低HPは回復
                    else if (canUpgrade)
                        gm.RestUpgrade();                    // 中HP+素材有り → 強化優先
                    else
                        gm.RestHeal();                       // 満ちていれば回復で無駄なく（食事休憩は廃止）
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

                case GameManager.GamePhase.SinRitual:
                    // 方針: 6層固有デバフ(ゴルゴダの心/断絶した時間/灰燼の烙印)は
                    // 回避できる限り必ず回避する。3儀式すべて支払いを選択（accept=true）。
                    // ゲーム側で支払い可能なときのみ実際に消費され、不能時のみデバフ付与。
                    gm.OfferHpSacrifice(true);
                    gm.OfferGoldSacrifice(true);
                    gm.OfferItemSacrifice(true);
                    gm.CompleteSinRitual();
                    return false;

                case GameManager.GamePhase.FloorClear:
                    gm.ConfirmFloorClear();
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
            if (hasFwd && lat != null && lat.Count > 0
                && gm.Run.hope > AutoTest.PolicyParameters.Current.lateralHopeFloor)
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

            // 回復目標: 貪欲はボス接近時 HP8割を目指す。それ以外は半分。
            float healTarget = (!_curCombatAverse && bossNear) ? 0.8f : 0.5f;
            while (Consumables.TryUseBestHeal(gm.Run, healTarget)) { }

            // 希望(ADR-0002): 飢餓は希望に統合。現在希望が補充点 hopeRefillFloor 以下なら食料で回復する。
            // 「いつ希望を補充に回すか」は L2 学習軸 hopeRefillFloor が勝率(composite)で最適化する
            // （高いほど早めに温存補充、低いほど枯渇近くまで引っ張る）。
            // 佯狂者の冠所持時は発狂(希望0)狙いのため回復しない。
            if (gm.Run.hope <= AutoTest.PolicyParameters.Current.hopeRefillFloor
                && !GameLoop.YokyoSet.HasCrown(gm.Run))
                while (Consumables.TryUseBestFood(gm.Run)) { }

            // 休憩を強く優先すべき状況:
            //  - 貪欲がボス接近かつHPが8割未満（スケールしたbuildをボスへ生存させる）
            //  ※旧・空腹切れ条件は飢餓→希望統合で廃止（休憩は希望を回復しない＝希望は食料で対応）。
            bool preferRest = (!_curCombatAverse && bossNear && hpRatio < 0.8f);

            MapNode best = pool[0];
            int bestRank = int.MaxValue;
            foreach (var n in pool)
            {
                int r = Rank(n.EffectiveType, hpRatio, _curCombatAverse, preferRest, gm.Run);
                if (r < bestRank) { bestRank = r; best = n; }
            }
            gm.MoveToNode(best.id);
            return false;
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
            }

            int farmed = run.dimensionalDisturbance;
            bool atSpoke = node.id == "lambda_s";

            bool wantExit;
            if (lambdaFarmSweep)
            {
                wantExit = farmed >= Mathf.Max(3, lambdaFarmTiles);
            }
            else
            {
                // 通常ルーチン: lv2以上のデバフが4つ到達、または迫りくる死が lv2 到達で即時撤退
                int lv2plus = 0;
                if (run.lambdaDebuffs != null)
                    foreach (var kv in run.lambdaDebuffs) if (kv.Value >= 2) lv2plus++;
                bool impendingDanger =
                    run.GetLambdaDebuffLevel(GameLoop.Lambda.LambdaDebuffIds.ImpendingDeath) >= 2;
                wantExit = lv2plus >= 4 || impendingDanger;
            }

            // 安全弁（加算不具合・条件恒久未達でも周回を止める）
            if (_lambdaNavSteps > 240 || farmed >= 120) wantExit = true;

            if (atSpoke && wantExit)
            {
                RecordLambdaGains(run);
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
            _cur.lambdaItemsDiscardedDuringLambda = run.lambdaItemsDiscardedDuringLambda;
        }

        /// <summary>低いほど優先。HP帯(危機/低/健康)で重み分け。
        /// 戦闘タイル(Battle/EliteBattle)のみ profile で差し替え、他は共通固定。
        /// averse=false(戦闘貪欲): 健康なら戦闘を最優先級で選ぶ。
        /// averse=true(戦闘回避): 戦闘を最下位級にし、戦闘以外があれば必ず回避。</summary>
        private int Rank(TileType t, float hpRatio, bool averse, bool preferRest, GameLoop.RunState run)
        {
            var polN = AutoTest.PolicyParameters.Current;
            bool crit = hpRatio < polN.hpCritThreshold;   // 危機
            bool low  = hpRatio < polN.hpLowThreshold;    // 低HP

            // 空腹尽きかけ／貪欲のボス前整え: 休憩を最優先（食事＝空腹全回復も兼ねる）
            if (preferRest && t == TileType.Rest) return -1;

            // 確信チェーン進路の強制優先 (両プロファイル共通)
            //  - 災厄の予兆 未完了 (= convictionStage 0) → イベント/ミステリ を常に最優先
            //    (Event/Mystery は戦闘でないので HP に関わらず追跡可能)
            //  - 災厄の予兆 完了 + 真理未到達 (stage 1〜4) → HP≥50% のみエリート最優先
            //    (エリートは戦闘発生 = HP リスクなので安全時のみ)
            int convStage = run?.convictionStage ?? 0;
            if (convStage == 0)
            {
                // Mystery は 20% でしか Event 化しないため Bot の確信進路としては当てにせず、
                // Event タイルのみ強優先する。
                if (t == TileType.Event) return -10;
            }
            else if (convStage > 0 && convStage <= GameLoop.ConvictionSystem.StageTruth)
            {
                if (t == TileType.EliteBattle && hpRatio >= 0.5f) return -5;
            }

            // --- 戦闘タイル: ここだけが比較軸（1変数） ---
            // HP低下時 (50%未満) は profile に関係なく Battle/Elite を強く忌避する。
            //  (回復を最優先にしたいというユーザー要望)
            if (t == TileType.Battle)
            {
                if (low) return crit ? 96 : 93; // 低HP/危機: averse 同等の忌避
                return averse ? 90 : 1;
            }
            if (t == TileType.EliteBattle)
            {
                if (low) return crit ? 98 : 95; // 低HP/危機: averse 同等の忌避
                return averse ? 93 : 2;
            }

            // --- 非戦闘タイル: 両プロファイル共通固定 ---
            switch (t)
            {
                case TileType.Rest:        return crit ? 0 : low ? 0 : 6;
                case TileType.Shop:        return crit ? 1 : 1;
                case TileType.Treasure:    return crit ? 2 : low ? 1 : -2; // 装備強化源: T4到達率改善のため健康時優先度UP (0 → -2)
                case TileType.Event:       return crit ? 5 : 3;
                case TileType.Mystery:     return crit ? 5 : 3;
                case TileType.Exchange:    return crit ? 5 : 2;  // ビルド強化源（厳密アップグレード）
                case TileType.Trap:        return crit ? 7 : 6;
                case TileType.SinAltar:    return 4;
                case TileType.Boss:        return 9;  // 最後に残れば踏む(フロアクリア必須)
                case TileType.Outpost:     return 0;
                default:                   return 4;
            }
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
                    if (s == null || s.sold || s.price > run.coins) return false;
                    // 拡張/素材は容量を消費しないので CanAdd チェック対象外
                    if (s.kind != InventorySystem.Shop.ShopSlotKind.InventoryExpansion
                        && s.kind != InventorySystem.Shop.ShopSlotKind.WeaponMaterial
                        && !string.IsNullOrEmpty(s.itemId))
                    {
                        // 2026-06-22 Phase C: 購入候補が現所持品の上位 Lv なら、 下位 Lv を先に売却 (装備武器は除く)
                        TrySellLowerTierBefore(run, s.itemId);

                        if (!InventorySystem.Helpers.InventoryCapacity.CanAdd(run, s.itemId))
                            return false; // 容量不足
                    }
                    int before = run.coins;
                    string id = s.itemId;
                    gm.ShopBuy(i);
                    if (run.coins < before)
                    {
                        _cur.shopPurchases++;
                        if (s.kind == InventorySystem.Shop.ShopSlotKind.InventoryExpansion)
                            _cur.inventoryExpansionsPurchased++;
                        else if (AutoTest.LearnedPriorityProvider.IsPriority(id)) _cur.priorityItemsAcquired++;
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
                            // ショップ由来在庫があれば売却対象
                            if (!rs.shopPurchasedCounts.TryGetValue(ownedId, out int stock) || stock <= 0) continue;
                            targetIdx = j;
                            break;
                        }
                        if (targetIdx < 0) break;
                        string sellId = rs.ownedPassiveItems[targetIdx];
                        int coinsBefore = rs.coins;
                        if (AutoTest.InventoryTriage.TrySellFromBot(rs, targetIdx))
                            UnityEngine.Debug.Log($"[Phase C] 上位購入前に下位 {sellId} を売却 (+{rs.coins - coinsBefore}G)");
                        else break;
                    }
                }

                // 拡張優先購入: 容量がカツカツ (空きセル < 平均アイテムサイズ4) かつ 残金で拡張可能なら先に買う
                bool TryBuyExpansionIfNeeded()
                {
                    int freeCells = InventorySystem.Helpers.InventoryCapacity.FreeCells(run);
                    if (freeCells >= 4) return false;
                    int expCost = InventorySystem.Helpers.InventoryCapacity.NextExpansionCost(run);
                    if (expCost == int.MaxValue) return false;
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s != null && !s.sold
                            && s.kind == InventorySystem.Shop.ShopSlotKind.InventoryExpansion
                            && s.price <= run.coins)
                            return Buy(i);
                    }
                    return false;
                }
                int expGuard = 0;
                while (TryBuyExpansionIfNeeded() && expGuard++ < 4) { }
                bool BuyKind(ShopSlotKind k)
                {
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold
                            && inv.slots[i].kind == k && Buy(i)) return true;
                    return false;
                }

                // ============================================================
                // フェーズ1: 在庫の S/A 級を先取り（買えるだけ買う）
                //   2026-06-22: 買えない S/A があり、 廃棄候補 (Score=0 かつショップ由来在庫あり) を所持しているなら
                //               TrySell で換金してから購入を試みる。
                //   2026-06-23: Power 帯認識 (Weak/Early は C+ 購入、 Late/Apex は A+ のみ)。
                // ============================================================
                int currentPower = AutoTest.InventoryPower.Compute(run);
                int powerBand = AutoTest.InventoryPower.GetPowerBandRank(currentPower);
                int dynamicMinScore = powerBand <= 1 ? 1 : (powerBand <= 2 ? 2 : 3);

                bool HasUnaffordablePriority()
                {
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        if (s.price <= run.coins) continue;
                        if (string.IsNullOrEmpty(s.itemId)) continue;
                        // Score 細分化 (2026-06-22): B+ (>=2) を S/A/B 帯として扱う
                        // 特売補正込み: 40% 割引以上は Score +1、 60% 以上は +2
                        // 2026-06-23: Power 帯認識: Weak/Early は C+ で十分価値あり、 Mid 以上は B+
                        int sc = AutoTest.LearnedPriorityProvider.Score(s.itemId);
                        int saleBonus = s.discountPct >= 60 ? 2 : (s.discountPct >= 40 ? 1 : 0);
                        if (sc + saleBonus >= dynamicMinScore) return true;
                    }
                    return false;
                }
                bool SellOneScore0()
                {
                    // 商人の符牒は売却阻止。 装備中も除外
                    if (run.OwnsPassive("商人の符牒")) return false;
                    for (int i = 0; i < run.ownedPassiveItems.Count; i++)
                    {
                        string id = run.ownedPassiveItems[i];
                        if (string.IsNullOrEmpty(id)) continue;
                        if (id == run.equippedWeaponId || id == run.equippedDiceId) continue;
                        if (AutoTest.LearnedPriorityProvider.Score(id) > 0) continue;
                        if (!run.shopPurchasedCounts.TryGetValue(id, out int stock) || stock <= 0) continue;
                        if (AutoTest.InventoryTriage.TrySellFromBot(run, i)) return true;
                    }
                    return false;
                }
                int sellGuard = 0;
                while (HasUnaffordablePriority() && SellOneScore0() && sellGuard++ < 16) { }

                // 2026-06-23: Power 帯認識ショップ判定 (dynamicMinScore は上で算出済み)
                //   Weak(<10)/Early(<25): C 級 (Score 1) も購入対象、 minScore 緩和
                //   Mid(25-49): 標準 (B+)
                //   Late/Apex(>=50): A+ のみ (ゴミ買い禁止、 G 温存)
                bool BuyPriorityPass(int minScore)
                {
                    // 動的閾値を反映 (caller 指定の minScore と Power 帯ベース下限の max)
                    int effMinScore = System.Math.Max(minScore, dynamicMinScore);
                    // 2026-06-22 Phase D: 同 Score 帯内では ΔPower/G (コスパ) が高いものを優先
                    // 2026-06-22b: 特売品 (discountPct > 0) は Score +1 補正 (40% 以上で +1、 60% 以上で +2)
                    //              ─ 割引で実効コスパが上がる分、 1〜2 段下の Tier も購入対象にする。
                    // 2026-06-23 案A: 同 Score 内では LiftScore (連続値) → ΔPower/G の二段 tiebreak。
                    //              同家系 Lv 違いを自然に序列化、 unique 内も統計的優劣で並ぶ。
                    int bestIdx = -1; int bestScore = -1; float bestLift = float.MinValue; float bestEff = float.MinValue;
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        if (s.price > run.coins) continue;
                        int sc = AutoTest.LearnedPriorityProvider.Score(s.itemId);
                        // 特売補正: 割引率が大きいほど Tier 1〜2 段相当の購入意欲
                        int saleBonus = s.discountPct >= 60 ? 2 : (s.discountPct >= 40 ? 1 : 0);
                        int effSc = sc + saleBonus;
                        if (effSc < effMinScore) continue;
                        // 案A: 連続値 LiftScore (同 Tier 内序列の細粒度判定、 家系 Lv 補正済)
                        float liftSc = AutoTest.LearnedPriorityProvider.LiftScore(s.itemId);
                        // ΔPower/G コスパ (slot.price は割引後の価格なので特売/upgrade が自然に有利化)
                        int delta = AutoTest.InventoryPower.SimulateAddItemDelta(run, s.itemId);
                        float eff = AutoTest.InventoryPower.CostEfficiency(delta, s.price);
                        // Score 主軸 → LiftScore tiebreak → ΔPower/G 第二 tiebreak
                        bool better = effSc > bestScore
                                   || (effSc == bestScore && liftSc > bestLift)
                                   || (effSc == bestScore && liftSc == bestLift && eff > bestEff);
                        if (better) { bestScore = effSc; bestLift = liftSc; bestEff = eff; bestIdx = i; }
                    }
                    if (bestIdx < 0) return false;
                    return Buy(bestIdx);
                }
                // 余金が許す限り S/A を全て取得
                int safety = 0;
                while (BuyPriorityPass(2) && safety++ < 30) { }

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
                int rerollGuard = 0;
                var pol = AutoTest.PolicyParameters.Current;
                // 2026-06-23 Power 帯認識リロール:
                //   Weak/Early (band 0-1): リロールしない (G 温存、 まず手元の品で固める)
                //   Mid (band 2): 通常 (S 未所持なら reroll)
                //   Late/Apex (band 3-4): 積極 (リロール上限 +2、 残り G ライン緩和)
                int rerollMaxLoops = powerBand >= 3 ? 10 : (powerBand <= 1 ? 3 : 8);
                int residualG = powerBand >= 3 ? 4 : (powerBand <= 1 ? 10 : 6);
                while (rerollGuard++ < rerollMaxLoops)
                {
                    int price = inv.CurrentRerollPrice;
                    if (price > run.coins * pol.rerollCostRatio) break;
                    // 通常ショップでは購入余力を最低 residualG 残す
                    if (!lastShop && run.coins - price < residualG) break;

                    bool needS = !OwnsAnyS() && !HasSInInventory();
                    bool needA = lastShop && !OwnsAnyA() && !HasAInInventory();
                    // Weak/Early はリロールしない (まず手持ち固め優先)
                    if (powerBand <= 1) { needS = false; needA = false; }
                    if (!needS && !needA) break;

                    int beforeR = run.coins;
                    gm.ShopReroll();
                    if (run.coins >= beforeR) break; // リロール失敗
                    _cur.shopRerolls++;
                    _cur.shopRerollCoins += (beforeR - run.coins);

                    // 直後に新在庫の S/A をかき集める
                    int s2 = 0;
                    while (BuyPriorityPass(2) && s2++ < 30) { }
                }

                // ============================================================
                // フェーズ2.5: 値下げ交渉(=強盗) 判定
                //   条件: アンロック済み AND まだ未使用 AND
                //         「6層以降」 AND 「勝てそう=HP余裕」
                //   さらに 強盗実行 直前に「在庫に優先0 OR 購入で在庫が欠けてる」なら
                //   最大3回までリロールして戦利品を仕込む(=強奪する物を厚くする)。
                // ============================================================
                if (MetaProgression.MetaBuffApplicator.IsShopRobberyUnlocked()
                    && !run.shopsBlocked
                    && !run.shopRobberyInProgress
                    && run.currentFloor >= 6     // 強盗は 6F以降 固定
                    && run.playerMaxHP >= 50     // 最大HP下限 固定 (序盤の貧弱を除外)
                    && run.playerHP >= run.playerMaxHP * pol.robberyMinHpRatio)
                {
                    bool NoPriorityInStock()
                    {
                        for (int i = 0; i < inv.slots.Count; i++)
                        {
                            var s = inv.slots[i];
                            if (s == null || s.sold) continue;
                            if (s.kind == ShopSlotKind.WeaponMaterial) continue;
                            if (AutoTest.LearnedPriorityProvider.IsPriority(s.itemId)) return false;
                        }
                        return true;
                    }
                    bool StockDepletedByPurchase()
                    {
                        for (int i = 0; i < inv.slots.Count; i++)
                            if (inv.slots[i] != null && inv.slots[i].sold) return true;
                        return false;
                    }

                    int preRobReroll = 0;
                    while (preRobReroll < 3 && (NoPriorityInStock() || StockDepletedByPurchase()))
                    {
                        int price = inv.CurrentRerollPrice;
                        if (price > run.coins) break; // 払えなければ諦めて強奪
                        int beforeR = run.coins;
                        gm.ShopReroll();
                        if (run.coins >= beforeR) break;
                        _cur.shopRerolls++;
                        _cur.shopRerollCoins += (beforeR - run.coins);
                        preRobReroll++;
                    }
                    if (preRobReroll > 0)
                        Debug.Log($"[AutoRunner] 強奪前リロール {preRobReroll}回 実施");

                    Debug.Log($"[AutoRunner] 値下げ交渉トリガ: F{run.currentFloor} HP{run.playerHP}/{run.playerMaxHP}");
                    gm.ShopRobbery();
                    return;
                }

                // ============================================================
                // フェーズ3: 通常購入（旧ロジック）
                //   黄金卿の剣 所持時 (2026-05-31 v3 消費Gold基準に変更後):
                //   旧 = 余剰金保持 / 新 = **積極消費**で与ダメ倍率を伸ばす
                //   → 通常購入閾値を緩める (=より積極的に買い回し)
                // ============================================================
                bool hasGoldKing = run != null && run.OwnsPassive("黄金卿の剣");
                if (lastShop)
                {
                    int guard = 0;
                    bool bought = true;
                    while (bought && guard++ < 40)
                    {
                        bought = false;
                        if (BuyKind(ShopSlotKind.Weapon)) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.Dice)) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.Passive)) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.WeaponMaterial)) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.Consumable)) { bought = true; continue; }
                    }
                }
                else
                {
                    // 2026-05-31 v3: 消費Gold基準なので、 黄金卿所持時は **閾値を緩めて積極消費** (旧と逆向き)
                    int weaponGate   = hasGoldKing ? 5 : 8;
                    int passiveGate  = hasGoldKing ? 3 : 5;
                    int materialGate = hasGoldKing ? 2 : 4;
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > weaponGate
                            && (inv.slots[i].kind == ShopSlotKind.Weapon
                                || inv.slots[i].kind == ShopSlotKind.Dice)) Buy(i);
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > passiveGate
                            && inv.slots[i].kind == ShopSlotKind.Passive) Buy(i);
                    // T4 到達率改善: 素材買い溜め上限 2→4 (連続強化で T3+ → T4 まで一気に届く余地)
                    int needMat = GameManager.WeaponUpgradeCost(run);
                    int matBuys = 0;
                    int matBuyCap = run.currentFloor >= 4 ? 4 : 2;
                    while (run.weaponMaterials < needMat * 2 && run.coins > materialGate && matBuys++ < matBuyCap
                           && BuyKind(ShopSlotKind.WeaponMaterial)) { }
                    int stock = run.ownedConsumables != null ? run.ownedConsumables.Count : 0;
                    int stockCap = pol.consumableStockMax;
                    for (int i = 0; i < inv.slots.Count && stock < stockCap; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > 4
                            && inv.slots[i].kind == ShopSlotKind.Consumable && Buy(i)) stock++;
                }
            }
            gm.ExitShop();
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
                bool boss = e0.id != null && e0.id.StartsWith("boss_layer");
                int maxHit = e0.diceCount * e0.diceMaxValue * (e0.criticalNumerator > 0 ? 2 : 1);
                important = boss || e0.threat >= polC.importantThreatThreshold
                                 || maxHit * 2 >= Math.Max(1, cm.PlayerHP);
            }

            if (important)
            {
                // 2026-05-31: LEG (Lv4) は本当の窮地のみ使用 (BOT 過剰消費抑制 = LEG 評価低下対策 C案)。
                //   desperate = ボス OR HP残り33%以下 → LEG 解禁
                //   通常 important → GOLD(Lv3)以下のみ
                bool desperate = (e0?.id != null && e0.id.StartsWith("boss_layer"))
                              || (run.playerMaxHP > 0 && cm.PlayerHP * 3 <= run.playerMaxHP);
                if (desperate)
                {
                    // 攻撃/ダイス/会心: LEG 解禁
                    UseFirst(run, "uniq_oni_oil", "cons_atk_4", "cons_dice_4", "cons_crit_4",
                                  "cons_atk_3", "cons_dice_3", "cons_crit_3",
                                  "cons_atk_2", "cons_dice_2", "cons_crit_2");
                    UseFirst(run, "cons_shield_4", "cons_shield_3", "cons_shield_2", "uniq_earth_guard",
                                  "cons_reduce_4", "cons_reduce_3", "cons_shield_1");
                    UseFirst(run, "cons_regen_4", "cons_regen_3", "cons_regen_2", "cons_regen_1");
                }
                else
                {
                    // 通常重要戦闘: GOLD 以下のみ (LEG は温存)
                    UseFirst(run, "uniq_oni_oil", "cons_atk_3", "cons_dice_3", "cons_crit_3",
                                  "cons_atk_2", "cons_dice_2", "cons_crit_2");
                    UseFirst(run, "cons_shield_3", "cons_shield_2", "uniq_earth_guard",
                                  "cons_reduce_3", "cons_shield_1");
                    UseFirst(run, "cons_regen_3", "cons_regen_2", "cons_regen_1");
                }
                UseFirst(run, "uniq_mirror");
                UseFirst(run, "uniq_ambush");
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
                    int maxHit = e.diceCount * e.diceMaxValue;
                    if (e.criticalNumerator > 0) maxHit *= 2;
                    int healTrigger = Mathf.RoundToInt(maxHit * polC.emergencyHealRatio);
                    if (cm.PlayerHP <= healTrigger)
                    {
                        // 通常閾値では Lv3 以下のみ使用 (LEG 温存)
                        UseFirst(run, "cons_heal_3", "cons_heal_2", "cons_heal_1");
                        // LEG (完全回復薬) は HP <= 25% maxHP の真の窮地のみ
                        if (run.playerMaxHP > 0 && cm.PlayerHP * 4 <= run.playerMaxHP)
                            UseFirst(run, "cons_heal_4");
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
            if (run.sinDebuffs != GameLoop.SinDebuff.None)
                sb.Append($"\n      6層儀式の罪: {run.sinDebuffs}");
            sb.Append($"\n      LS使用済: {run.lastStandActive} | サン=ジョリオラ撃破: {run.defeatedSaintGeorges}");
            _cur.clear6FSnapshot = sb.ToString();
        }

        /// <summary>Bot 専用: 戦闘中、シュヴァリエ・サン=ジョリオラ戦でのレイピア起動同期。
        /// ボスが形態1(コントラタック)中はレイピアON、形態2(オポジション)中はOFF。
        /// 切替時の解除効果(次T ダイス+1, 撃破済みなら会心+9)も自動で享受する。</summary>
        private void AutoToggleRapierVsSaintGeorges(CombatManager cm, GameLoop.RunState run)
        {
            if (cm == null || run == null) return;
            if (run.ownedPassiveItems == null || !run.ownedPassiveItems.Contains("chevalier_rapier")) return;
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

        private int PickSafeChoice(EventDefinition def)
        {
            if (def == null || def.choices == null || def.choices.Count == 0) return 0;

            var run = GameLoop.GameManager.Instance?.Run;

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
            while (_simBases.Count < simSampleBuilds && attempts < cap)
            {
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
                    diceId = gm?.Run?.equippedDiceId ?? ""
                };
                _cur.combats.Add(midRec);
                // L1学習: チェーン途中で倒した形態も「撃破記録」に入れる
                if (!string.IsNullOrEmpty(midRec.enemyId) && midRec.enemyId.StartsWith("boss_layer7"))
                    _cur.awakenedFormsKilled.Add(midRec.enemyId);
                // 注: totalCombats / totalTurns / totalWins には加算しない
                // (OnBattleEnded 側のチェーン最終形態分でラン全体の合計が記録されるため、
                //  ここで足すと二重計上になる。combats リストの per-enemy 集計だけ厚くする)
            }

            _pendingEnemyName = e != null ? e.displayName : "?";
            _pendingEnemyId = e != null && e.id != null ? e.id : "";
            // ボス判定は敵IDのみで厳密に行う（ノード種別フォールバックは誤検出の元）
            _pendingEnemyIsBoss = _pendingEnemyId.StartsWith("boss_layer");
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
            // 覚者形態撃破: 7層ボス chain で勝った（含 swap）形態を記録
            if (r.playerWon && !string.IsNullOrEmpty(rec.enemyId) && rec.enemyId.StartsWith("boss_layer7"))
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

            // 前哨基地: 旅団契約 AI を起動 (UI 未実装のため runtime API を直接呼ぶ)
            if (t == TileType.Outpost) HandleOutpostContracts();
        }

        /// <summary>前哨基地での契約処理。 維持費徴収 → 不足時は AI で切捨選択 → 新規/延長の AI 選択。</summary>
        private void HandleOutpostContracts()
        {
            var gm = GameManager.Instance;
            var run = gm?.Run;
            if (run == null || _cur == null) return;
            _cur.contractOutpostsVisited++;

            // 各層の前哨基地到達時に InventoryPower スナップショット (層別戦力推移の計測)
            int floor = run.currentFloor;
            if (floor > 0 && !_cur.inventoryPowerByFloor.ContainsKey(floor))
                _cur.inventoryPowerByFloor[floor] = InventoryPower.Compute(run);

            var mgr = GameLoop.Contracts.ContractManager.Instance;

            // ① 維持費徴収。 不足時は ContractAiPicker で切り捨て候補を選ぶ。
            int needBefore = 0;
            foreach (var c in run.activeContracts) needBefore += c.CurrentMaintenanceCost;
            var shortfall = mgr.CollectMaintenanceOrFlagShortfall(run);
            if (shortfall != null && shortfall.Count > 0)
            {
                // 不足額算出
                int totalNeeded = 0;
                foreach (var c in shortfall) totalNeeded += c.CurrentMaintenanceCost;
                int shortAmount = totalNeeded - run.coins;
                var toCancel = ContractAiPicker.PickShortfallReleases(run, shortAmount);
                _cur.contractsShortfallReleased += toCancel.Count;
                mgr.ResolveShortfall(run, toCancel);
                _curLog?.Enqueue($"[契約] 維持費不足 → {toCancel.Count}件 解除 (Floor {run.currentFloor})");
            }
            else
            {
                _cur.contractMaintenancePaid += needBefore;
            }

            // ② 既存契約の延長 (1 件まで)
            int budget = run.coins;
            var extKind = ContractAiPicker.PickExtension(run, budget);
            if (extKind.HasValue)
            {
                if (GameLoop.Contracts.ContractOfferRoller.Extend(run, extKind.Value))
                {
                    _cur.contractsExtended.TryGetValue(extKind.Value, out int xc);
                    _cur.contractsExtended[extKind.Value] = xc + 1;
                    var def = GameLoop.Contracts.ContractDatabase.Get(extKind.Value);
                    _curLog?.Enqueue($"[契約] 延長: {def.displayName} → Lv {mgr.Find(run, extKind.Value)?.level} (Floor {run.currentFloor})");
                }
            }

            // ③ 新規契約抽選
            var offers = GameLoop.Contracts.ContractOfferRoller.RollOffers(run);
            if (offers.Count > 0)
            {
                var picks = ContractAiPicker.PickOffers(run, offers, run.coins);
                foreach (var k in picks)
                {
                    var removed = GameLoop.Contracts.ContractOfferRoller.Sign(run, k, 1);
                    if (removed != null)
                    {
                        _cur.contractsSigned.TryGetValue(k, out int sc);
                        _cur.contractsSigned[k] = sc + 1;
                        _cur.contractsForcedReleased += removed.Count;
                        if (removed.Count > 0)
                        {
                            var rk = removed[0];
                            var rdef = GameLoop.Contracts.ContractDatabase.Get(rk.kind);
                            _curLog?.Enqueue($"[契約] 敵対強制解除: {rdef.displayName} Lv{rk.level} (Floor {run.currentFloor})");
                        }
                        var def = GameLoop.Contracts.ContractDatabase.Get(k);
                        _curLog?.Enqueue($"[契約] 新規: {def.displayName} Lv1 (Floor {run.currentFloor})");
                    }
                }
            }
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

        /// <summary>〈昇華〉BOTヒューリスティク(v1・L2較正待ち):
        /// インベントリが満杯(パッシブ1枠未満の空き)で、 武器強化用に少し残しても昇華コストを払えるなら、
        /// 最良(L1スコア最大)の刻印持ちパッシブを永久化して枠を空ける。 良い品を恒久ロックしつつ枠拡張。</summary>
        private void TryBotSublimate(GameLoop.RunState run)
        {
            if (run?.ownedPassiveItems == null) return;
            // 武器強化用に温存する素材pt（L2学習軸 sublimationReserve）。
            int weaponReserve = Mathf.Max(0, Mathf.RoundToInt(AutoTest.PolicyParameters.Current.sublimationReserve));
            int guard = 16;
            while (guard-- > 0)
            {
                // 枠に余裕があるうちは温存（パッシブ1個=4セル分の空きがあれば昇華しない）
                if (InventorySystem.Helpers.InventoryCapacity.FreeCells(run) >= 4) break;
                int cost = GameLoop.SublimationSystem.Cost(run);
                if (run.weaponMaterials < cost + weaponReserve) break;
                // 最良の昇華可能パッシブを選ぶ（恒久化する価値が高い順）
                int bestIdx = -1, bestScore = -1;
                for (int i = 0; i < run.ownedPassiveItems.Count; i++)
                {
                    if (!GameLoop.SublimationSystem.CanSublimate(run, i)) continue;
                    int sc = AutoTest.LearnedPriorityProvider.Score(run.ownedPassiveItems[i]);
                    if (sc > bestScore) { bestScore = sc; bestIdx = i; }
                }
                if (bestIdx < 0) break;
                if (GameLoop.SublimationSystem.Sublimate(run, bestIdx))
                { if (_cur != null) _cur.sublimationsTotal++; }
                else break;
            }
        }

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

            var lcr = GameManager.Instance.LastCombatResult;
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
                _cur.gedatsuVictory = run.gedatsuVictory;
                _cur.finalInventoryPower = InventoryPower.Compute(run);
                // 2026-06-23: 最終所持アイテム ID 集合 (保持率計算用)
                _cur.finalOwnedItemIds.Clear();
                if (!string.IsNullOrEmpty(run.equippedWeaponId)) _cur.finalOwnedItemIds.Add(run.equippedWeaponId);
                if (!string.IsNullOrEmpty(run.equippedDiceId)) _cur.finalOwnedItemIds.Add(run.equippedDiceId);
                if (run.ownedPassiveItems != null)
                    foreach (var id in run.ownedPassiveItems) _cur.finalOwnedItemIds.Add(id);
                if (run.ascendedPassiveIds != null)
                    foreach (var id in run.ascendedPassiveIds) _cur.finalOwnedItemIds.Add(id);

                // 旅団契約: ラン跨ぎカウンタを取り込み
                _cur.contractsHpReleased = GameLoop.Contracts.ContractManager.Instance.Stat_HpReleaseCount;

                // 旅団契約: ラン終了時の発効中契約をスナップショット
                if (run.activeContracts != null)
                {
                    foreach (var c in run.activeContracts)
                    {
                        var def = GameLoop.Contracts.ContractDatabase.Get(c.kind);
                        _cur.contractsFinalActive.Add($"{def.displayName} Lv{c.level}");
                    }
                }
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
                if (!string.IsNullOrEmpty(run.equippedDiceId)) _cur.acquiredItemsEver.Add(run.equippedDiceId);
                _cur.finalWeaponTier = run.equippedWeaponId ?? "";
                _cur.finalLimitBreak = run.limitBreakStage;
            }
            Classify(_cur);
            _cur.bandScore = ComputeBandScore(_cur);
            // L2ペアテスト記録
            _cur.policyVariant = _currentRunVariant;
            _cur.pairedSeed = _currentRunSeed;
            _records.Add(_cur);
            _curLog.Enqueue($"[AutoRunner] === RUN {_cur.index} 終了: {_cur.band} ({_cur.bandLabel}) — {note} ===");
            _curAttachLog(_cur);
            _cur = null;
        }

        /// <summary>L1学習用: 帯ラベルから数値スコアを返す。
        /// CRASH=-1, DEADLOCK=-2, R1a..R10/R11/R12 を 1..12 にマップ（先頭文字 R+数字部）。</summary>
        private int ComputeBandScore(RunRec r)
        {
            if (string.IsNullOrEmpty(r.band)) return 0;
            if (r.band == "CRASH") return -1;
            if (r.band == "DEADLOCK") return -2;
            // "R12" → 12, "R8b" → 8 等
            int v = 0; int i = 1;
            while (i < r.band.Length && char.IsDigit(r.band[i])) { v = v * 10 + (r.band[i] - '0'); i++; }
            // 小文字 a/b で 0.5 単位の細分はしないが、6Fクリアは R8b(=8) のまま、5Fクリアは R8(=8) で同点
            // 7層クリア(R11)/解脱(R12)が最高。死亡 R1a..R10。
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
                if (r.gedatsuVictory) { r.band = "R12"; r.bandLabel = "解脱(妙覚サドンデス勝利)"; }
                else                  { r.band = "R11"; r.bandLabel = "7層クリア(完全クリア)"; }
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
            if (f >= 7) { r.band = "R10"; r.bandLabel = "7Fで死亡(覚者到達)"; return; }
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

        private string WriteLogs()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string root = Path.Combine(Application.dataPath, "..", "AutoRunLogs");
            root = Path.GetFullPath(root);
            // バッチ命名: プロファイルサフィックスを使用 (旧 cowardly/fullmeta + debuff タグは廃止)
            string profileTag = MetaProfileHelper.CurrentSuffix;
            string dir = Path.Combine(root, $"batch_{stamp}_n{_records.Count}_{profileTag}");
            Directory.CreateDirectory(dir);

            // L1学習: プロファイル別サブディレクトリに分離して累積
            string learningRoot = MetaProfileHelper.LearningRoot();
            ItemLearningStats.StatsFile learnStats = null;
            Debug.Log($"[AutoRunner] 学習モード: {learningMode} (Tier更新={UpdatesTier} / AI成長={UpdatesAi})");
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

            // L3: ボス難易度オートチューナー (突破率→目標ファネルへ寄せる)。 learningMode とは独立トグル。
            if (BossAutoTune)
            {
                try { BossBalanceTuner.AssessAndAdjust(_records, MetaProfileHelper.CurrentDebuffOn, learningRoot, learnStats?.totalBatches ?? 0); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] L3ボス調整失敗: {e.Message}"); }
            }

            // ファイル検索で最新サマリーを開きやすいよう、 summary に profile + 時刻を埋め込む
            string summaryName = $"summary_{MetaProfileHelper.CurrentSuffix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            File.WriteAllText(Path.Combine(dir, summaryName),
                BuildSummary() + (learnStats != null ? "\n" + ItemLearningStats.BuildHumanLiftTable(learnStats) : ""),
                new UTF8Encoding(false));
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
                    $"BALANCE_TIER_LIST_{MetaProfileHelper.CurrentSuffix}.md"));
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
            sb.AppendLine($"総ラン数  : {_records.Count}  (戦闘貪欲={greedy.Count} / 戦闘回避={averse.Count})");
            sb.AppendLine("比較軸    : 航行Rankのみ差し替え（消費/ショップ/戦闘実行/イベントは共通固定）");
            sb.AppendLine("################################################");
            sb.AppendLine();
            sb.Append(BuildLambdaFarmBlock());
            sb.AppendLine();
            sb.Append(BuildWeaponProgressionBlock());
            sb.AppendLine();
            sb.Append(BuildBossWinRateBlock());
            sb.AppendLine();
            sb.Append(BuildContractBlock());
            sb.AppendLine();
            sb.Append(BuildContractTierBlock());
            sb.AppendLine();
            sb.Append(BuildTensionCurveBlock());
            sb.AppendLine();
            sb.Append(BuildBuildDiversityBlock());
            sb.AppendLine();
            sb.Append(BuildDeathCauseQualityBlock());
            sb.AppendLine();
            sb.Append(BuildDecisionWeightBlock());
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

        /// <summary>旅団契約システムの集計ブロック (docs/specs/contracts.md)。
        /// 旅団ごとの新規/延長回数・解除内訳・前哨基地訪問数・最終発効中契約を出力。</summary>
        private string BuildContractBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【旅団契約 (docs/specs/contracts.md)】");
            int n = _records.Count;
            if (n == 0) { sb.AppendLine("  (データなし)"); return sb.ToString(); }

            // 集計
            int outposts = 0;
            int forcedRel = 0, hpRel = 0, shortfallRel = 0, maintPaid = 0;
            var signCount = new Dictionary<GameLoop.Contracts.ContractKind, int>();
            var extCount  = new Dictionary<GameLoop.Contracts.ContractKind, int>();
            var finalActiveCount = new Dictionary<GameLoop.Contracts.ContractKind, int>();
            foreach (var r in _records)
            {
                outposts += r.contractOutpostsVisited;
                forcedRel += r.contractsForcedReleased;
                hpRel += r.contractsHpReleased;
                shortfallRel += r.contractsShortfallReleased;
                maintPaid += r.contractMaintenancePaid;
                foreach (var kv in r.contractsSigned)
                {
                    signCount.TryGetValue(kv.Key, out int v);
                    signCount[kv.Key] = v + kv.Value;
                }
                foreach (var kv in r.contractsExtended)
                {
                    extCount.TryGetValue(kv.Key, out int v);
                    extCount[kv.Key] = v + kv.Value;
                }
                if (r.contractsFinalActive != null)
                {
                    foreach (var s in r.contractsFinalActive)
                    {
                        // s = "傭兵団 Lv2" を kind に逆引き
                        foreach (var def in GameLoop.Contracts.ContractDatabase.All())
                        {
                            if (s.StartsWith(def.displayName))
                            {
                                finalActiveCount.TryGetValue(def.kind, out int v);
                                finalActiveCount[def.kind] = v + 1;
                                break;
                            }
                        }
                    }
                }
            }

            sb.AppendLine($"  前哨基地訪問: 平均 {outposts / (float)n:F1} 回/ラン");
            sb.AppendLine($"  維持費総額  : 平均 {maintPaid / (float)n:F1} G/ラン");
            sb.AppendLine($"  解除内訳    : 敵対 {forcedRel / (float)n:F2} / HP20% {hpRel / (float)n:F2} / 維持不足 {shortfallRel / (float)n:F2} (件/ラン)");
            sb.AppendLine();
            sb.AppendLine("  | 旅団 | 新規 (合計) | 延長 (合計) | 最終発効ラン数 |");
            sb.AppendLine("  |---|---|---|---|");
            foreach (var def in GameLoop.Contracts.ContractDatabase.All())
            {
                signCount.TryGetValue(def.kind, out int sc);
                extCount.TryGetValue(def.kind, out int ec);
                finalActiveCount.TryGetValue(def.kind, out int fc);
                if (sc == 0 && ec == 0 && fc == 0) continue; // 0 行は省略
                sb.AppendLine($"  | {def.displayName} | {sc} | {ec} | {fc}/{n} ({fc * 100f / n:F0}%) |");
            }
            return sb.ToString();
        }

        /// <summary>旅団個別 + 協力ペアの Tier ブロック。 純粋寄与 (bandScore の delta) を計算。
        /// 純粋寄与 = avg(bandScore | 当該旅団契約ラン) − avg(bandScore | 同前哨基地訪問ラン中の非契約ラン)。
        /// シナジー寄与 = avg(bandScore | 両方契約) − avg(bandScore | どちらか片方のみ契約)。
        /// Tier は順位ベース (S=上位20%, A=20-40%, B=40-60%, C=60-80%, D=下位20%)。
        /// データ不足のものは「該当ランなし」 として末尾に注記。</summary>
        private string BuildContractTierBlock()
        {
            var sb = new StringBuilder();
            int n = _records.Count;
            if (n == 0) { return ""; }

            // 前哨基地に 1 回以上訪問したラン (契約のチャンスがあった集団)
            var visited = new List<RunRec>(n);
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].contractOutpostsVisited >= 1) visited.Add(_records[i]);
            }
            if (visited.Count == 0)
            {
                sb.AppendLine("【旅団 個別 Tier (純粋寄与)】");
                sb.AppendLine("  (前哨基地を訪問したランがないため計測不能)");
                return sb.ToString();
            }

            // === 1. 旅団個別 純粋寄与 ===
            sb.AppendLine("【旅団 個別 Tier (純粋寄与)】");
            sb.AppendLine("  純粋寄与 = avg(bandScore | 当該旅団契約ラン) − avg(bandScore | 同前哨基地訪問ラン中の非契約ラン)");
            sb.AppendLine($"  (前哨基地に 1 回以上訪問した {visited.Count} ラン中で集計、 +値が大きいほど寄与大)");
            sb.AppendLine();

            var indivScores = new List<(GameLoop.Contracts.ContractKind kind, string name, float delta, int signedN, int unsignedN)>();
            var indivSkipped = new List<string>();
            foreach (var def in GameLoop.Contracts.ContractDatabase.All())
            {
                int signedN = 0, unsignedN = 0;
                long signedRankSum = 0, unsignedRankSum = 0;
                for (int i = 0; i < visited.Count; i++)
                {
                    var r = visited[i];
                    bool signed = r.contractsSigned.TryGetValue(def.kind, out int sc) && sc > 0;
                    if (signed) { signedN++; signedRankSum += r.bandScore; }
                    else { unsignedN++; unsignedRankSum += r.bandScore; }
                }
                if (signedN == 0 || unsignedN == 0) { indivSkipped.Add(def.displayName); continue; }
                float signedAvg = (float)signedRankSum / signedN;
                float unsignedAvg = (float)unsignedRankSum / unsignedN;
                indivScores.Add((def.kind, def.displayName, signedAvg - unsignedAvg, signedN, unsignedN));
            }
            indivScores.Sort((a, b) => b.delta.CompareTo(a.delta));

            sb.AppendLine("  | Tier | 旅団 | 純粋寄与 | 契約ラン数 | 非契約ラン数 |");
            sb.AppendLine("  |---|---|---|---|---|");
            int total = indivScores.Count;
            for (int i = 0; i < total; i++)
            {
                string tier = TierBand(i, total);
                var s = indivScores[i];
                sb.AppendLine($"  | {tier} | {s.name} | {s.delta:+0.00;-0.00; 0.00} | {s.signedN} | {s.unsignedN} |");
            }
            if (indivSkipped.Count > 0)
            {
                sb.AppendLine($"  (データ不足で計測不能: {string.Join(", ", indivSkipped)})");
            }
            sb.AppendLine();

            // === 2. 協力ペア シナジー寄与 ===
            sb.AppendLine("【旅団 協力ペア Tier (純粋寄与・シナジー)】");
            sb.AppendLine("  シナジー寄与 = avg(bandScore | 両方契約) − avg(bandScore | どちらか片方のみ契約)");
            sb.AppendLine($"  (前哨基地訪問 {visited.Count} ラン中、 両方契約 vs 片方契約のみで集計)");
            sb.AppendLine();

            var pairScores = new List<(string name, float delta, int bothN, int xorN)>();
            var pairSkipped = new List<string>();
            foreach (var pair in GameLoop.Contracts.ContractRelations.Alliances)
            {
                int bothN = 0, xorN = 0;
                long bothRankSum = 0, xorRankSum = 0;
                for (int i = 0; i < visited.Count; i++)
                {
                    var r = visited[i];
                    bool aOn = r.contractsSigned.TryGetValue(pair.a, out int ac) && ac > 0;
                    bool bOn = r.contractsSigned.TryGetValue(pair.b, out int bc) && bc > 0;
                    if (aOn && bOn) { bothN++; bothRankSum += r.bandScore; }
                    else if (aOn ^ bOn) { xorN++; xorRankSum += r.bandScore; }
                }
                string pairName = $"{GameLoop.Contracts.ContractDatabase.Get(pair.a).displayName} + {GameLoop.Contracts.ContractDatabase.Get(pair.b).displayName}";
                if (bothN == 0 || xorN == 0) { pairSkipped.Add(pairName); continue; }
                float bothAvg = (float)bothRankSum / bothN;
                float xorAvg = (float)xorRankSum / xorN;
                pairScores.Add((pairName, bothAvg - xorAvg, bothN, xorN));
            }
            pairScores.Sort((a, b) => b.delta.CompareTo(a.delta));

            sb.AppendLine("  | Tier | 協力ペア | シナジー寄与 | 両方契約ラン数 | 片方のみラン数 |");
            sb.AppendLine("  |---|---|---|---|---|");
            int pTotal = pairScores.Count;
            for (int i = 0; i < pTotal; i++)
            {
                string tier = TierBand(i, pTotal);
                var p = pairScores[i];
                sb.AppendLine($"  | {tier} | {p.name} | {p.delta:+0.00;-0.00; 0.00} | {p.bothN} | {p.xorN} |");
            }
            if (pairSkipped.Count > 0)
            {
                sb.AppendLine($"  (データ不足で計測不能: {string.Join(", ", pairSkipped)})");
            }

            return sb.ToString();
        }

        /// <summary>⑤ インベントリパワー指標 (2026-06-22 新設)。
        /// Tier 表から算出した「現在の戦力」 を層別 + 終了時で集計。 売買コスパの基準値。</summary>
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
            public Dictionary<string, int> causes = new Dictionary<string, int>();
            public float WinRate => enc > 0 ? (float)wins / enc : 0f;
            public float RollWinRate { get { long t = tWin + tLoss + tDraw; return t > 0 ? (float)tWin / t : 0f; } }
            public float AvgTurns => enc > 0 ? (float)turns / enc : 0f;
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
            return $"  {label,-7}: 勝率 {a.WinRate,6:P1}  (遭遇 {a.enc,5} / 勝 {a.wins,5})  平均{a.AvgTurns,4:F1}T  ロール勝率{a.RollWinRate,5:P0}  {cause}";
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
            double gold = 0, items = 0, tiles = 0, dbg = 0, gross = 0, discarded = 0;
            int diedInLambda = 0, exited = 0;
            foreach (var r in entered)
            {
                gold += r.lambdaGoldGained;
                items += r.lambdaItemsGained;
                gross += r.lambdaItemsAcquiredGross;
                discarded += r.lambdaItemsDiscardedDuringLambda;
                tiles += r.lambdaTilesFarmed;
                dbg += r.lambdaDebuffLevelSum;
                // Λ内死亡: 5Fで死亡かつ inLambda 由来（reachedFloor==5 のGameOver）。離脱できれば6F以上へ。
                if (r.outcome == Outcome.GameOver && r.reachedFloor <= 5) diedInLambda++;
                else exited++;
            }
            int n = entered.Count;
            sb.AppendLine($"  Λ突入ラン   : {n}");
            sb.AppendLine($"  獲得ゴールド : 平均 +{gold / n:F1}");
            sb.AppendLine($"  獲得アイテム : 平均 取得+{gross / n:F2} (Λ保護, 容量圧迫ロス -{discarded / n:F2}) → 純増+{items / n:F2}");
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

            // --- バンド分布 (R1-R12, R8b 含む) ---
            // R8b = 6Fクリアして 7F進入不可で終了 (真理未所持) を R9 と R10 の間に挿入。
            sb.AppendLine("---- 結果バンド分布（上位%＝その結果『以上』に到達したランの割合） ----");
            string[] bands = { "R1a","R1b","R1c","R1d","R2","R3","R4","R5","R6","R7","R8","R9","R8b","R10","R11","R12" };
            string[] labels = {
                "1F道中で死亡","1Fボスで死亡","2F道中で死亡","2Fボスで死亡",
                "3F道中で死亡","3Fボスで死亡","4F道中で死亡","4Fボスで死亡",
                "5F道中で死亡","5Fボスで死亡","5Fクリア(決意未所持)","6Fボスで死亡",
                "6Fクリア(真理未所持)","7Fで死亡(覚者到達)","7層クリア(完全クリア)","解脱(妙覚サドンデス勝利)" };
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
                    bool boss = fc.isBoss || (!string.IsNullOrEmpty(fc.enemyId) && fc.enemyId.StartsWith("boss_layer"));
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
            double sReroll=0, sRerollG=0, sPrio=0, sMatGain=0, sSubl=0, sDisc=0;
            double sFinalHope=0, sMinHope=0; int madnessCount=0;   // 希望(ADR-0002)
            double sHCombat=0, sHComposure=0, sHLateral=0, sHMarch=0, sHEvil=0, sHFood=0, sHReroll=0; // 希望 発生源別収支
            foreach (var r in recs)
            { sCoins+=r.finalCoins; sPeak+=r.peakCoins; sGain+=r.totalGoldGained;
              sStarv+=r.starvationTotal; sStarvHit+=r.starvationHits; sShop+=r.shopPurchases;
              sReroll+=r.shopRerolls; sRerollG+=r.shopRerollCoins; sPrio+=r.priorityItemsAcquired; sMatGain+=r.materialsGainedTotal;
              sSubl+=r.sublimationsTotal; sDisc+=r.passivesDiscarded;
              sFinalHope+=r.finalHope; sMinHope+=r.minHope; if (r.reachedMadness) madnessCount++;
              sHCombat+=r.hopeCombatLoss; sHComposure+=r.hopeComposureGain; sHLateral+=r.hopeLateralLoss;
              sHMarch+=r.hopeMarchLoss; sHEvil+=r.hopeEvilLoss; sHFood+=r.hopeFoodGain; sHReroll+=r.hopeRerollLoss; }
            int dn = Math.Max(1, n);
            sb.AppendLine($"  {PadR("平均最終ゴールド", 20)}: {(sCoins/dn):F1}");
            sb.AppendLine($"  {PadR("平均ピークゴールド", 20)}: {(sPeak/dn):F1}");
            sb.AppendLine($"  {PadR("平均総獲得ゴールド", 20)}: {(sGain/dn):F1}");
            sb.AppendLine($"  {PadR("平均総獲得素材(pt基準)", 20)}: {(sMatGain/dn):F1}  ※昇華コスト逓増カーブ較正用");
            sb.AppendLine($"  {PadR("平均昇華回数", 20)}: {(sSubl/dn):F2}   平均パッシブ廃棄: {(sDisc/dn):F2}");
            sb.AppendLine($"  {PadR("平均最終希望", 20)}: {(sFinalHope/dn):F1}");
            sb.AppendLine($"  {PadR("平均最低希望", 20)}: {(sMinHope/dn):F1}  (発狂到達 {madnessCount}/{n} = {Pct(madnessCount, n)})");
            sb.AppendLine($"  ── 希望 発生源別収支（1ラン平均・損は−） ──");
            sb.AppendLine($"  {PadR("  戦闘損", 20)}: -{(sHCombat/dn):F1}   {PadR("被弾0回復", 12)}: +{(sHComposure/dn):F1}");
            sb.AppendLine($"  {PadR("  横移動損", 20)}: -{(sHLateral/dn):F1}   {PadR("絶望進軍損", 12)}: -{(sHMarch/dn):F1}");
            sb.AppendLine($"  {PadR("  悪選択損", 20)}: -{(sHEvil/dn):F1}   {PadR("食料回復", 12)}: +{(sHFood/dn):F1}");
            sb.AppendLine($"  {PadR("  振り直し損", 20)}: -{(sHReroll/dn):F1}");
            sb.AppendLine($"  {PadR("ショップ購入数", 20)}: {(sShop/dn):F2}");
            sb.AppendLine($"  {PadR("平均リロール回数", 20)}: {(sReroll/dn):F2}  (平均消費 {(sRerollG/dn):F1}G)");
            sb.AppendLine($"  {PadR("平均優先アイテム取得", 20)}: {(sPrio/dn):F2}");
            sb.AppendLine();

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
            // 1戦 = OnBattleEnded で確定した戦闘（7層覚者連戦は最終形態の1件に連戦全体の累計を計上）。
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
                sb.AppendLine("  （7層は覚者連戦全体の累計を1戦として計上）");
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
                bool boss = c.isBoss || (!string.IsNullOrEmpty(c.enemyId) && c.enemyId.StartsWith("boss_layer"));
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
                    if (!agg.TryGetValue(key, out var a)) { a = new int[10]; agg[key] = a; }
                    a[0]++;
                    if (c.won) a[1]++; else a[2]++;
                    a[3] += c.turns;
                    a[4] += Math.Max(0, c.hpBefore - c.hpAfter);
                    a[6] += c.tWin; a[7] += c.tDraw; a[8] += c.tLoss; a[9] += c.tLossAbs;
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
                        if (!agg.TryGetValue(key, out var a)) { a = new int[10]; agg[key] = a; }
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
            sb.AppendLine($"  {PadR("敵名",18)}{PadR("種別",6)}{PadL("遭遇",5)} {PadL("勝率",6)} {PadL("平均T",7)} {PadL("平均被ダメ",11)} {PadL("致命",5)}");
            foreach (var k in keys)
            {
                var a = agg[k];
                string wr = a[0] == 0 ? "-" : (100.0*a[1]/a[0]).ToString("F0")+"%";
                string at = a[0] == 0 ? "-" : ((double)a[3]/a[0]).ToString("F1");
                string ad = a[0] == 0 ? "-" : ((double)a[4]/a[0]).ToString("F1");
                string nm = dispName.TryGetValue(k, out var dn2) ? dn2 : k;
                string kind = isBossKey.TryGetValue(k, out var b) && b ? "BOSS"
                            : (!string.IsNullOrEmpty(nm) && nm.StartsWith("精鋭")) ? "精鋭"
                            : "雑魚";
                sb.AppendLine($"  {PadR(TruncDisp(nm,16),18)}{PadR(kind,6)}{PadL(a[0].ToString(),5)} {PadL(wr,6)} {PadL(at,7)} {PadL(ad,11)} {PadL(a[5].ToString(),5)}");
            }
            sb.AppendLine();
            sb.AppendLine("（勝率の低い順にソート。致命=そのランをゲームオーバーに導いた回数）");
            sb.AppendLine();

            // 武器×ダイス 組み合わせ別 戦闘勝率は 2026-06-21 削除 (バンドが肥大化、 ビルド多様性ブロックで代替)。

            // --- ボス戦ターン内訳（非解決グラインドの原因特定） ---
            // 全ターンの勝/分/敗の比率＋「吸収敗(敗北だがメインダメ0で死回避)」を表示。
            // 99T級の長期戦＝勝or分or吸収敗ばかりで致命敗が稀、を定量化する。
            sb.AppendLine("---- ボス戦ターン内訳（勝/分/敗 ％・吸収敗=敗北だが被ダメ0） ----");
            sb.AppendLine($"  {PadR("ボス名",16)}{PadL("総T",6)} {PadL("勝%",6)} {PadL("分%",6)} {PadL("敗%",6)} {PadL("吸収敗%",8)}");
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
            sb.AppendLine("（吸収敗% = 敗北ターンのうちシールド吸収/無効化でメインダメ0だった割合。LS中はこれ＋勝＋分が生存ターン）");
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
    }
}

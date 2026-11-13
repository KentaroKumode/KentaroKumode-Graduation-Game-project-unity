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

namespace AutoTest
{
    /// <summary>AutoRunner: ラン・戦闘の記録の型 (RunRec / CombatRec) と集計分類の enum。 AutoRunner.cs から切り出した (2026-11-12・中身は同じ)。</summary>
    public partial class AutoRunner
    {
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
            /// <summary>[計装 2026-11-06] 与ダメの内訳 [武器, 攻撃の出目, 攻撃+N, 主攻撃の与ダメ, 固定ダメ, 追撃, 出目由来の攻撃, 毒, 出血, 臨界の爆発]。
            /// 毒と爆発は固定ダメの内数、 出血は固定ダメの外。</summary>
            public int[] dmgParts;
            /// <summary>[計装 2026-11-10] 配線を決めたターン数 / うち防ぎ切りの規則の対象 (今ターン撃破を除く) / うち今ターン撃破。</summary>
            public int fbDecided, fbTurns, fbKillTurns;
            /// <summary>[計装 2026-11-10] 敵の HP を実際に減らした量の経路別 (<see cref="CombatSystem.EnemyHpLedger"/> の添字・この段の開始からの差分)。</summary>
            public float[] hpLedger;
            /// <summary>[計装 2026-11-10] 見せる層 (RunState.currentFloor)。 <see cref="floor"/> は深さ (Run.Depth)。</summary>
            public int shownFloor;
            /// <summary>[計装 2026-11-10] 戦闘 (段) の開始時の偏重 6 軸の品の数 (AxisRules.Ranked の順: 会心・出血・毒・臨界・充電・シールド)。</summary>
            public int[] axisAtStart;
            /// <summary>[計装 2026-11-10 V3 mainCorr] この段で攻撃したターンの 主攻撃の見積もり (比を掛ける前) / 比を掛けた見積もり (切り替えが OFF でも、 掛けたなら の値) /
            /// 実際 (DamageBreakdown[7] の増分) の合計。</summary>
            public float mainPred, mainPredCorr, mainActual;
            /// <summary>[計装 2026-11-10] この段の配線のターン数を攻撃の本数 0/1/2/3+ で数えたもの。</summary>
            public int[] atkHist;
            /// <summary>[計装 2026-11-10 黎明の溜める形] 攻撃したターンに乗った溜め (%・1 回 1 値)。</summary>
            public int[] dawnShots;
            public bool isFightEnd; // OnBattleEnded で確定した1戦分か（チェーン途中形態は false）
            // この戦闘時点の装備（武器×ダイス勝率集計用）。武器は family_tN まで（業物+段階は区別しない）。
            public string weaponId = "";
            public string diceId = "";
            // 敗北時の致死メカニズム (勝利時は Normal)
            public ItemSystem.PassiveSkills.DeathCause deathCause;
            // この戦闘の総被ダメの ソース別内訳 (支配率診断用・キル時の一撃ではない)
            public Dictionary<ItemSystem.PassiveSkills.DeathCause, int> playerDamageBySource;
            // 緊張感曲線用: プレイヤー最大HP (戦闘終了時点)。 hpAfter / playerMaxHpEnd で残HP%
            public int playerMaxHpEnd;
            /// <summary>戦闘名の鍵 (層|種類|戦闘名・<see cref="EncounterDangerStats.KeyOf"/>)。 戦闘名が無い戦闘は空 (2026-11-05)。</summary>
            public string encounterKey = "";
            /// <summary>[計装 2026-11-13] この戦闘でエスカレーションの段が上がった数 (ボス戦も・GameManager.EscalationRisesThisCombat) と、
            /// そこから溜まった不安定化ポイント (ボス戦・嵐の無い層・stormClock=Turns は 0)。 戦闘の終わり (isFightEnd) の記録だけが持つ (チェーンの途中は 0)。</summary>
            public int escRises, instEsc;
            /// <summary>[計装 2026-11-14] この戦闘 (ヴェスカは段) でボスがダウンしたターンの数 (BossDownDiag.TotalDowns の差)。</summary>
            public int downs;
            /// <summary>[計装 2026-11-14] この戦闘 (ヴェスカは段) で実際に撃たれた大技を受けた回数 / 大技で受けた HP の合計 (防御・シールドの後) / 最大の 1 発。
            /// 大技 = 取り消されず・ダウンでなく・敵が倒れる前に撃った大技のターン (灰燼の王の〈業火〉は烈炎の分も含む)。 CombatSystem.HeavyHitDiag。</summary>
            public int heavyHits, heavyTaken, heavyMax;
            /// <summary>[計装 2026-11-14・嵐の航行] 戦闘が始まった時の嵐: -1 = 不安定化の時計の層でない / 0 = 嵐の外 / 1 = 嵐の中 (敵に〈嵐〉)・その時のフェーズ・不安定化の累計。</summary>
            public int stormIn = -1, stormPhase, stormInst;
        }

        /// <summary><see cref="RunRec.stormNav"/> の 1 層の列の数 (第 2 巡で 5 → 10)。</summary>
        public const int StormNavCols = 10;

        /// <summary><see cref="RunRec.stormLayers"/> の 1 層の列の数。</summary>
        public const int StormLayerCols = 12;

        [Serializable]
        public class RunRec
        {
            public int index;
            /// <summary>[計装 2026-11-10] ラン開始時に実際に入っていた整備パネルの段 ("種類:段" を , で連結・0 段は省く)。</summary>
            public string metaRanks = "";
            /// <summary>[計装 2026-11-12・#20/#41] ラン開始時の職業 (ClassType の名前)。 lockWeaponFamily の測定では武器の家系と同じ ──
            /// 家系ごとの被ダメ・クリアを、 早く死んだランも落とさずに分けるため (強化した武器の id で分けると、 強化前に死んだランが抜ける)。</summary>
            public string startClass = "";
            public Outcome outcome;
            public DeathCause cause = DeathCause.None;
            public int deathFloor;
            /// <summary>[計装 2026-11-04] 戦えない HP (危険度の閾値未満) で、 戦闘マス/種別の見えていない点へ踏み込んだ 1 歩の理由別の回数。
            /// 0 行き先そのもの / 1 行き先へ向かう途中 / 2 出口 (ボス) へ向かう途中 / 3 出口 (ボス) そのもの。</summary>
            public int[] lowHpRiskSteps = new int[4];
            public bool deathInBossFight;
            public string fatalEnemy = "";
            public int reachedFloor = 1;
            public bool reached6F;
            public int finalHP;
            public int finalMaxHP;
            public int finalCoins;
            public int peakCoins;
            public int totalGoldGained;
            /// <summary>[計装 2026-11-06] 金の内訳 (キー = 「深さ|経路」)。</summary>
            public string[] goldKeys;
            /// <summary>[計装 2026-11-06] 状態の記録 (<see cref="SnapState"/> の 10 列を平らに並べる)。 マクロの価値 V の表の元。</summary>
            public List<int> stateSnaps = new List<int>();
            /// <summary>[計装 2026-11-09] 航行マップの層ごとの踏破: 4 列ずつ (深さ, マスの種別 TileType, 層にあった数, 踏んだ数)。</summary>
            public List<int> mapVisit = new List<int>();
            /// <summary>[計装 2026-11-13] 嵐の層の不安定化: 層ごとに <see cref="StormLayerCols"/> (= 12) 列ずつ。 ボスの点の戦闘に入った時 (= 層を抜けた時) の値:
            /// (深さ, 累計, フェーズ 0〜4, 内訳 取る, 激化, 歩く, イベントのターン, 嵐を動かす, 崩れ始めた手番 (-1 = 崩れなかった・深さ 6 は 0), 嵐の中を歩いた歩数, 嵐で払った HP, 航行の手番)。
            /// 内訳は層で溜まった分だけ (深さ 6 の始まりの分は入らない = 累計 − 内訳の和)。 stormEnabled=False・stormClock=Turns の層と、 ボスへ届かなかった層は載らない。</summary>
            public List<int> stormLayers = new List<int>();
            /// <summary>[計装 2026-11-14] 嵐の層の航行の判断: 層ごとに <see cref="StormNavCols"/> (= 10) 列ずつ。 ボスの点の戦闘に入った時か、 その層で死んだ時の値:
            /// (0 深さ, 1 出口へ切り上げた時の累計 (-1 = 切り上げなかった = 行動の上限か点が尽きた以外で出口へ向かわなかった), 2 その時の航行の手番 (-1),
            /// 3 ボスの点が見えた航行の手番 (-1 = 見えないまま入った), 4 ボスを探して歩いた歩数 (口 navStormAware でボスが見えないまま出口へ向かった歩),
            /// 5 ボスに入った (1) / その層で死んだ (0), 6 切り上げた時の HP (最大 HP の %・-1), 7 切り上げた時の、 出口までの嵐の HP の BOT の見込み (navStormAware の時だけ・-1),
            /// 8 切り上げた後に嵐で払った HP, 9 その層で嵐で払った HP)。 第 2 巡 (2026-11-14) で 5 → 10 列に・死んだ層も載せる。
            /// 不安定化の時計でない層は載らない。 口 navStormAware が切れていても、 判断の時点は同じ定義で記録する。</summary>
            public List<int> stormNav = new List<int>();
            /// <summary>[計装 2026-11-13] 深さ 6 (画面の第 4 層) の〈灰に埋もれた基地〉: 0 = 起きていない / 1 = 最初に踏んだイベントマスで起きた
            /// / 2 = イベントマスを踏まずに終点へ着き、 安全弁がボス戦の前に出した。 深さ 6 へ届かなかったランと、 イベントなしの測定は 0。</summary>
            public int ashBase;
            /// <summary>[計装 2026-11-13] 基地の版 (true = 再訪 = 灰燼の王を倒したことがある記録 / false = はじめて)・選んだ選択肢の添字 (起きていなければ -1)・
            /// 起きた時の深さ 6 の航行の手番 (起きていなければ -1)・手記の最後の頁を開いた (〈真理〉を得た)。</summary>
            public bool ashBaseRevisit;
            public int ashBaseChoice = -1, ashBaseTurn = -1;
            public bool ashBaseOpened;
            /// <summary>[計装 2026-11-14] 〈徒労〉: 掛かった通常の戦闘の勝ちの数 (今の形 = 何も得られなかった戦闘 / 別の形 = 品を落とさなかった戦闘)・
            /// 止めた品のドロップ (当たったが配らなかった数・戦闘のドロップと強奪)・開けた宝箱 (宝の点・縄張りの宝・形によらない)・そのうち〈徒労〉で空だった数。</summary>
            public int toilNothingBattles, toilBlockedDrops, chestsOpened, toilEmptyChests;
            public int[] goldVals;
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
            public int provisionLateralLoss;
            public int provisionEvilLoss;
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
    }
}

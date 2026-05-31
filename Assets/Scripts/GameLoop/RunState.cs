using System.Collections.Generic;

namespace GameLoop
{
    /// <summary>
    /// 6層 SinAltar（祭壇マス）で支払えなかった儀式によって付与される永続デバフ。
    /// 6層ボス戦中のみ効果を発揮し、ボスのステータスとパッシブを変質させる。
    /// 複数同時所持可（フラグ）。
    /// </summary>
    [System.Flags]
    public enum SinDebuff
    {
        None              = 0,
        HeartOfGolgotha   = 1 << 0, // 「ゴルゴダの心」HP の儀を支払えなかった
        SeveredTime       = 1 << 1, // 「断絶した時間」金銭の儀を支払えなかった
        AshenBrand        = 1 << 2, // 「灰燼の烙印」遺品の儀を支払えなかった
    }

    /// <summary>
    /// 1ランの進行状態を保持するデータクラス（MonoBehaviour非依存）。
    /// マップベース進行: ボス撃破でフロアクリア。5層=通常クリア、6層=裏ボス。
    /// </summary>
    public class RunState
    {
        // === 進行 ===
        public int currentFloor = 1;
        public int maxFloor = 7;
        public int normalClearFloor = 5;
        public bool bossDefeatedThisFloor;

        // === 値下げ交渉(=強盗) ===
        /// <summary>強盗実行後、以降のショップマスを全てスキップする。</summary>
        public bool shopsBlocked;
        /// <summary>現在「怪しい商人戦」が進行中（HandleCombatEnd で勝利報酬／敗北脱出 を分岐）。</summary>
        public bool shopRobberyInProgress;
        /// <summary>強盗勝利時に付与するアイテムID一覧（成功した時点のショップ品出し内容）。</summary>
        public List<string> robberyPendingItems = new List<string>();

        // === プレイヤーステータス ===
        public int playerHP;
        public int playerMaxHP;

        // === 戦績 ===
        public int totalBattles;
        public int totalWins;
        public int totalTurns;
        public int coins;
        /// <summary>このラン中に支出した累積ゴールド (ショップ購入/リロール等)。
        /// 黄金卿の剣の与ダメ計算で参照: 消費1Gあたり +1% 与ダメ倍率。
        /// 加算は GameManager.SpendCoins() で一元集計。</summary>
        public int coinsSpent;

        // === 状態 ===
        public bool isRunActive;

        // === 6層儀式由来の永続デバフ（6層ボス戦のみ参照される） ===
        public SinDebuff sinDebuffs;
        public bool HasDebuff(SinDebuff d) => (sinDebuffs & d) != 0;
        public void AddDebuff(SinDebuff d) => sinDebuffs |= d;

        // === イベント由来の状態（軽量。完全な実装は将来のインベントリ統合で置換） ===

        /// <summary>カルマカウント。5層ボス前で清算される（永続デバフ化）。</summary>
        public int karma;

        /// <summary>武器強化素材（マグナイト等）の所持数。</summary>
        public int weaponMaterials;

        /// <summary>インベントリの解放列数 (初期 INITIAL_UNLOCKED_ROWS=4、 ショップ拡張で +1 ずつ MAX=8)。
        /// セル容量 = inventoryUnlockedRows × GRID_WIDTH(5)。</summary>
        public int inventoryUnlockedRows = InventorySystem.InventoryConstants.INITIAL_UNLOCKED_ROWS;

        /// <summary>フラグアイテム名のセット（例: "苦難の予言", "迷い犬の首輪"）。</summary>
        public HashSet<string> ownedFlags = new HashSet<string>();

        /// <summary>パッシブアイテム名のリスト（重複可）。匿名取得は "" 名で1個分計上。</summary>
        public List<string> ownedPassiveItems = new List<string>();

        /// <summary>各 ownedPassiveItems[i] に紐づく刻印 (取得時1回ロール、ラン中不変)。
        /// 並列配列。要素数は ownedPassiveItems と一致させる。
        /// 取得時に <see cref="InventorySystem.Helpers.PassiveAddHelper"/> 経由で追加すべき。</summary>
        public List<InventorySystem.Sigils.PassiveSigil> passiveSigils = new List<InventorySystem.Sigils.PassiveSigil>();

        /// <summary>消費アイテム名のリスト。</summary>
        public List<string> ownedConsumables = new List<string>();

        /// <summary>時限バフ: ID → 残り適用回数（次戦闘で1減算する想定）。</summary>
        public Dictionary<string, int> timedBuffs = new Dictionary<string, int>();

        /// <summary>時限デバフ: ID → 残り適用回数。</summary>
        public Dictionary<string, int> timedDebuffs = new Dictionary<string, int>();

        /// <summary>永続デバフID（5層清算時に効果発動）。</summary>
        public HashSet<string> permanentDebuffs = new HashSet<string>();

        /// <summary>「一度のみ」イベントの既出 ID 集合。</summary>
        public HashSet<string> seenOnceEvents = new HashSet<string>();

        /// <summary>ラストスタンド発動済みフラグ。ラン中1回のみ true。</summary>
        public bool lastStandActive;

        /// <summary>5層裏ボス（シュヴァリエ・サン=ジョリオラ）撃破済みフラグ。
        /// 立っている場合、シュヴァリエのレイピア解除時に会心+9 補正が付与される。</summary>
        public bool defeatedSaintGeorges;

        /// <summary>解脱: 覚者・妙覚のサドンデス勝利による特殊エンディング達成フラグ。</summary>
        public bool gedatsuVictory;

        // === Λ層（時間の狭間） ===

        /// <summary>Λ層（時間の狭間）に滞在中か。5層ボス撃破後、〈決意〉以上所持で強制突入し、
        /// 中央マス踏破で 6F 前哨基地へ着地する。滞在中は currentFloor は 5 のまま。</summary>
        public bool inLambda;

        /// <summary>Λ層で踏んだマスの累積数（=次元の乱れスタック）。
        /// 3 毎に <see cref="lambdaDebuffs"/> へランダムデバフを付与/段階上昇させる。</summary>
        public int dimensionalDisturbance;

        /// <summary>Λ層由来の恒久デバフ: ID → 段階(1〜3)。ラン中ずっと戦闘へ影響する。
        /// 同IDを再付与すると段階+1（最大3）。</summary>
        public Dictionary<string, int> lambdaDebuffs = new Dictionary<string, int>();

        /// <summary>指定 Λ デバフの段階(0=未所持,1〜3)。</summary>
        public int GetLambdaDebuffLevel(string id)
            => lambdaDebuffs != null && lambdaDebuffs.TryGetValue(id, out int lv) ? lv : 0;

        /// <summary>確信パッシブの段階。 災厄の予兆 で〈根拠のない確信〉取得時に 1 になり、
        /// 以降エリート戦勝利毎に +1。 3 で〈決意〉、 6+ で〈真理〉 にアイテム名が変化する。
        /// 6F進入には〈決意〉以上 (stage>=3)、 7F進入には〈真理〉(stage>=6) が必要。</summary>
        public int convictionStage;

        /// <summary>装備中の武器アイテムID（空=未装備=デフォルト2d6）。取得時に Loadout.TryAutoEquip で更新。</summary>
        public string equippedWeaponId = "";

        /// <summary>装備中のダイスアイテムID（空=武器ダイス使用）。取得時に Loadout.TryAutoEquip で更新。</summary>
        public string equippedDiceId = "";

        /// <summary>武器の"+"段階(0 or 1)。T_n と T_n+ を表す。休憩強化で 0→1、1の状態で次Tier武器へ置換し0に戻る。</summary>
        public int weaponPlus;

        /// <summary>業物(限界突破)の段階(0-10)。T4+到達後、休憩で上昇。1lvごとにダイス合計+2・与ダメージ+2(累積)。</summary>
        public int limitBreakStage;

        // === 消費アイテム: 次戦闘へ持ち越すバフ（マップ上で使用した場合） ===
        // 戦闘中に使用した場合は CombatContext へ直接書き込まれるため、これらは使われない。
        // 戦闘開始時に CombatContext へコピーされ、ここはクリアされる（1戦のみ）。
        public int  pendingConsAtkBurst;       // 攻撃力: 次戦闘の最初の勝利ターンに与ダメ+X
        public int  pendingConsDiceRoll;       // ダイス補正: 勝敗判定のみ+X（ダメージ非加算）
        public int  pendingConsShield;         // シールド吸収量
        public int  pendingConsShieldTurns;    // シールド持続(>0)/無制限(-1)/無(0)
        public int  pendingConsRegen;          // 継続回復: 初期値X（毎T後X回復しX-1）
        public int  pendingConsCrit;           // 会心率+X(/9)
        public int  pendingConsFlatReduce;     // 被ダメ毎ターン定数-X
        public int  pendingConsDmgMultPct;     // 与ダメ+X%（鬼火の油: 50）
        public bool pendingConsReflect;        // 鏡写し: 被メインダメを敵に反射
        public int  pendingConsEnemyDiceDebuff;// 敵弱体: 敵ダイス合計-X
        public int  pendingEnemyStartHpCutPct; // 奇襲: 敵開始HP-X%
        public bool pendingGamblerDice;        // 賭博師: 50%全最大/50%全1
        public int  pendingFirstRollTotal;     // 加速の粉: 次戦闘の初回ロールでダイス合計+X

        // === 消費アイテム: 戦闘外ユーティリティ用フラグ/カウンタ ===
        public int  nextLootMinRarity = -1;    // 鑑定の眼鏡: 次の宝箱/ショップ最低レア(ItemRarity int)。-1=無
        public bool nextShopHalfPrice;         // 商人の鈴: 次ショップ全価格半額
        public int  philStoneUsed;             // 賢者の石: このランでの使用回数(最大5)

        /// <summary>次戦闘持ち越しバフをすべて消去（戦闘開始時にコピー後 or リセット時）。</summary>
        public void ClearPendingCombatConsumables()
        {
            pendingConsAtkBurst = 0; pendingConsDiceRoll = 0;
            pendingConsShield = 0; pendingConsShieldTurns = 0;
            pendingConsRegen = 0; pendingConsCrit = 0;
            pendingConsFlatReduce = 0; pendingConsDmgMultPct = 0;
            pendingConsReflect = false; pendingConsEnemyDiceDebuff = 0;
            pendingEnemyStartHpCutPct = 0; pendingGamblerDice = false;
            pendingFirstRollTotal = 0;
        }

        // === 時限バフ・デバフ用ヘルパー ===

        public bool HasTimedBuff(string id)
            => timedBuffs != null && timedBuffs.TryGetValue(id, out int n) && n > 0;

        public bool HasTimedDebuff(string id)
            => timedDebuffs != null && timedDebuffs.TryGetValue(id, out int n) && n > 0;

        public int GetTimedBuffCharges(string id)
            => timedBuffs != null && timedBuffs.TryGetValue(id, out int n) ? n : 0;

        public int GetTimedDebuffCharges(string id)
            => timedDebuffs != null && timedDebuffs.TryGetValue(id, out int n) ? n : 0;

        /// <summary>新規ランの初期化</summary>
        public void Initialize(int startHP = 30)
        {
            currentFloor = 1;
            bossDefeatedThisFloor = false;
            shopsBlocked = false;
            shopRobberyInProgress = false;
            robberyPendingItems = new List<string>();
            playerMaxHP = startHP;
            playerHP = startHP;
            totalBattles = 0;
            totalWins = 0;
            totalTurns = 0;
            coins = 0;
            isRunActive = true;
            sinDebuffs = SinDebuff.None;

            karma = 0;
            weaponMaterials = 0;
            inventoryUnlockedRows = InventorySystem.InventoryConstants.INITIAL_UNLOCKED_ROWS;
            ownedFlags = new HashSet<string>();
            ownedPassiveItems = new List<string>();
            passiveSigils = new List<InventorySystem.Sigils.PassiveSigil>();
            ownedConsumables = new List<string>();
            timedBuffs = new Dictionary<string, int>();
            timedDebuffs = new Dictionary<string, int>();
            permanentDebuffs = new HashSet<string>();
            seenOnceEvents = new HashSet<string>();
            lastStandActive = false;
            defeatedSaintGeorges = false;
            gedatsuVictory = false;
            inLambda = false;
            dimensionalDisturbance = 0;
            lambdaDebuffs = new Dictionary<string, int>();
            convictionStage = 0;
            equippedWeaponId = "";
            equippedDiceId = "";
            weaponPlus = 0;
            limitBreakStage = 0;
            ClearPendingCombatConsumables();
            nextLootMinRarity = -1;
            nextShopHalfPrice = false;
            philStoneUsed = 0;
        }

        /// <summary>戦闘結果を反映</summary>
        public void ApplyBattleResult(bool playerWon, int remainingHP, int turnsUsed)
        {
            totalBattles++;
            totalTurns += turnsUsed;
            playerHP = remainingHP;

            if (playerWon)
                totalWins++;
        }

        /// <summary>フロアを進める</summary>
        public bool AdvanceFloor()
        {
            if (currentFloor >= maxFloor) return false;
            currentFloor++;
            bossDefeatedThisFloor = false;
            outpostUpgradeUsedThisFloor = false; // 層が変わるたびに前哨基地強化を再解禁
            return true;
        }

        /// <summary>この層の前哨基地で武器強化を1回使ったか。
        /// EnterFloor / AdvanceFloor でリセットされる。</summary>
        public bool outpostUpgradeUsedThisFloor;

        /// <summary>通常クリア（5層ボス撃破）</summary>
        public bool IsNormalClear => currentFloor >= normalClearFloor && bossDefeatedThisFloor && playerHP > 0;

        /// <summary>完全クリア（6層裏ボス撃破）</summary>
        public bool IsFullClear => currentFloor >= maxFloor && bossDefeatedThisFloor && playerHP > 0;

        /// <summary>ランが終了したか</summary>
        public bool IsRunOver => !isRunActive || playerHP <= 0;

        /// <summary>ラン終了</summary>
        public void EndRun()
        {
            isRunActive = false;
        }
    }
}

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
        public int maxFloor = 6;
        public int normalClearFloor = 5;
        public bool bossDefeatedThisFloor;

        // === プレイヤーステータス ===
        public int playerHP;
        public int playerMaxHP;

        // === 戦績 ===
        public int totalBattles;
        public int totalWins;
        public int totalTurns;
        public int coins;

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

        /// <summary>フラグアイテム名のセット（例: "苦難の予言", "迷い犬の首輪"）。</summary>
        public HashSet<string> ownedFlags = new HashSet<string>();

        /// <summary>パッシブアイテム名のリスト（重複可）。匿名取得は "" 名で1個分計上。</summary>
        public List<string> ownedPassiveItems = new List<string>();

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

        /// <summary>装備中の武器アイテムID（空=未装備=デフォルト2d6）。取得時に Loadout.TryAutoEquip で更新。</summary>
        public string equippedWeaponId = "";

        /// <summary>装備中のダイスアイテムID（空=武器ダイス使用）。取得時に Loadout.TryAutoEquip で更新。</summary>
        public string equippedDiceId = "";

        /// <summary>武器強化レベル。休憩マスで weaponMaterials を消費して上昇。戦闘値に反映。</summary>
        public int weaponUpgradeLevel;

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
            ownedFlags = new HashSet<string>();
            ownedPassiveItems = new List<string>();
            ownedConsumables = new List<string>();
            timedBuffs = new Dictionary<string, int>();
            timedDebuffs = new Dictionary<string, int>();
            permanentDebuffs = new HashSet<string>();
            seenOnceEvents = new HashSet<string>();
            lastStandActive = false;
            equippedWeaponId = "";
            equippedDiceId = "";
            weaponUpgradeLevel = 0;
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
            return true;
        }

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

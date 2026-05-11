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

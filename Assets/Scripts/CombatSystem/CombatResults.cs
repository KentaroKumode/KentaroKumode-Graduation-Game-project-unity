using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>
    /// 1ターンの戦闘結果をまとめた構造体
    /// </summary>
    public struct TurnResult
    {
        public int turnNumber;

        // ダイスロール
        public int[] playerDice;
        public int[] enemyDice;
        public int playerDiceTotal;
        public int enemyDiceTotal;

        // 勝敗
        public bool playerWon;
        public bool isDraw;

        // ダメージ
        public int mainDamage;          // メインダメージ（ダイス差）
        public int pursuitDamage;       // 追撃ダメージ（パッシブ由来）
        public int totalDamage;         // 合算ダメージ（クリティカル適用後）
        public int fixedDamage;         // 固定ダメージ（パッシブ由来）
        public int scratchDamage;       // scratch削りダメージ（threat由来）
        public bool isCritical;

        // HP経過
        public int playerHPAfter;
        public int enemyHPAfter;
    }

    /// <summary>
    /// 戦闘全体の最終結果
    /// </summary>
    public struct CombatResult
    {
        public string enemyId;
        public string enemyDisplayName;
        public bool playerWon;
        /// <summary>逃げて終わった (2026-09-28)。 playerWon は false・HP は残っている。</summary>
        public bool playerFled;
        public int totalTurns;
        public int playerHPRemaining;
        public int enemyHPRemaining;
        public List<TurnResult> turnLog;
        /// <summary>検証計測: この戦闘でプレイヤーが獲得した累計回復量／シールド量。</summary>
        public int healApplied;
        public int shieldGained;
        /// <summary>L1学習: プレイヤーが敵に与えた総ダメージ (enemyMaxHP - 残HP の単純差分)。</summary>
        public int damageDealt;
        /// <summary>L1学習: プレイヤーが受けた純粋な総ダメージ (healApplied 補正済み)。</summary>
        public int damageTaken;
        /// <summary>L1学習: 敵 maxHP（撃破率計算用）。</summary>
        public int enemyMaxHP;
        /// <summary>ボス難易度オートチューナー: プレイヤー敗北時の致死メカニズム分類 (勝利時は Normal)。</summary>
        public ItemSystem.PassiveSkills.DeathCause deathCause;
        /// <summary>ボス難易度オートチューナー: この戦闘のプレイヤーロール合計と回数 (平均出目算出用)。</summary>
        public long playerRollSum;
        public int playerRollCount;
        /// <summary>ボス難易度オートチューナー: この戦闘の総被ダメの **ソース別内訳** (キル時でなく支配率診断用)。</summary>
        public Dictionary<ItemSystem.PassiveSkills.DeathCause, int> playerDamageBySource;
        /// <summary>ボス難易度オートチューナー: スタンス別の「ボスがロール勝ちしたターン数/総ターン数」(強/弱別レンジ制御用)。</summary>
        public int strongRollTurns, strongRollBossWins, weakRollTurns, weakRollBossWins;
    }
}

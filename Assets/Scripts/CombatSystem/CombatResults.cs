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

        // ── 読むための欄 (実績・2026-11-17)。 戦闘の計算には使わない ──
        /// <summary>このターンの主攻撃を当てる直前の敵の HP (1 体目)。 主攻撃・固定ダメの前 (敵の HP は 0 で止まるので、 倒したターンの「残り HP」は enemyHPAfter から戻せない)。</summary>
        public int enemyHPBeforeAttack;
        /// <summary>このターンの予告の敵の攻撃の激化の段 (0〜4・Escalation.StageOf)。</summary>
        public int escalationStage;
        /// <summary>このターンの頭の 2 体目の HP (2 体戦でなければ 0)。</summary>
        public int secondaryHPAtStart;
        /// <summary>主攻撃が通り、 敵の防御を貫通で 0 にしていた (敵の防御 &gt; 0・貫通 ≥ 防御)。</summary>
        public bool defensePiercedToZero;
        /// <summary>このターンに出目を挿した端子の種類 (<see cref="WiredAttack"/> ほかの和)。</summary>
        public int wiredKinds;
        public const int WiredAttack = 1, WiredBlock = 2, WiredCharge = 4, WiredSupport = 8, WiredAll = 15;
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
        /// <summary>プレイヤー敗北時の致死メカニズム分類 (勝利時は Normal)。</summary>
        public ItemSystem.PassiveSkills.DeathCause deathCause;
        /// <summary>この戦闘の総被ダメの **ソース別内訳** (キル時でなく支配率診断用)。</summary>
        public Dictionary<ItemSystem.PassiveSkills.DeathCause, int> playerDamageBySource;
        /// <summary>[読むための欄・実績 2026-11-17] 戦闘の開始時の自 HP。 <see cref="damageTaken"/> は最大 HP 基準 (学習が読む) なので、
        /// 「この戦闘で HP を減らしたか」は 開始 HP − 終了 HP + 回復 で見る (<see cref="HpLostThisCombat"/>)。</summary>
        public int playerHPAtStart;
        /// <summary>[読むための欄・実績 2026-11-17] エリートとの戦闘だった (敵の elite か、 マスがエリート戦)。</summary>
        public bool enemyElite;
        /// <summary>この戦闘で失った HP (開始 HP − 終了 HP + 戦闘中の回復・下限 0)。</summary>
        public int HpLostThisCombat => System.Math.Max(0, playerHPAtStart - System.Math.Max(0, playerHPRemaining) + System.Math.Max(0, healApplied));
    }
}

using System.Collections.Generic;

namespace InventorySystem.PassiveSkills
{
    /// <summary>
    /// 戦闘中の全状態を保持するコンテキスト
    /// パッシブスキルはこのオブジェクトを読み書きしてゲーム状態を変更する
    /// 
    /// 設計意図：
    /// - スキルロジックが直接MonoBehaviourやManagerを参照しない
    /// - テスト時にモック可能
    /// - 1戦闘ごとに生成→破棄でメモリリークなし
    /// </summary>
    public class CombatContext
    {
        // ===== プレイヤー状態 =====
        public int playerCurrentHP;
        public int playerMaxHP;
        public int playerBaseMaxHP;     // 装備補正前の最大HP

        // ===== 敵状態 =====
        public int enemyCurrentHP;
        public int enemyMaxHP;

        // ===== ダイスロール結果 =====
        public int[] playerDice;        // プレイヤーが振った各ダイスの値
        public int[] enemyDice;         // 敵が振った各ダイスの値
        public int playerDiceTotal;     // プレイヤーのダイス合計値
        public int enemyDiceTotal;      // 敵のダイス合計値
        public int diceDifference;      // ダイス差（player - enemy）

        // ===== ダメージ計算 =====
        public int baseDamage;          // 基本ダメージ
        public int finalDamage;         // 最終ダメージ（スキル補正後）
        public int pursuitDamage;       // 追撃ダメージ
        public int criticalBonus;       // 会心ボーナスダイス補正値
        public bool isCritical;         // 会心判定結果
        public float criticalMultiplier; // 会心倍率（デフォルト2.0）
        public bool damageReduced;      // ダメージ軽減が発生したか

        // ===== ターン管理 =====
        public int currentTurn;         // 現在のターン数（1始まり）
        public bool isFirstRoll;        // 戦闘開始後の初回ロールか

        // ===== 蓄積/持続値（スキルが読み書き） =====
        /// <summary>
        /// スキルごとの蓄積データ（キー = スキルID）
        /// 例: "持久戦" → 蓄積HP増加量, "夜" → 蓄積ダメージ
        /// </summary>
        public Dictionary<string, float> accumulatedValues = new Dictionary<string, float>();

        /// <summary>
        /// 次ターンへのバフ/デバフ転送用（キー = バフ名）
        /// ターン終了時にcurrentBuffsへ移行
        /// </summary>
        public Dictionary<string, float> nextTurnBuffs = new Dictionary<string, float>();
        
        /// <summary>
        /// 現在ターンのバフ/デバフ（毎ターン開始時にnextTurnBuffsから移行）
        /// </summary>
        public Dictionary<string, float> currentBuffs = new Dictionary<string, float>();

        // ===== 敵のダイス制約（処刑/正義への妄執用） =====
        /// <summary>敵のダイスに対する固定値制約（index → 固定値）</summary>
        public Dictionary<int, int> enemyDiceOverrides = new Dictionary<int, int>();
        
        /// <summary>敵のダイスのどれを固定するかの指示（min/max）</summary>
        public List<DiceOverrideRequest> pendingDiceOverrides = new List<DiceOverrideRequest>();

        // ===== 出血・状態異常 =====
        public int enemyBleedStacks;    // 敵の出血スタック数
        public bool enemyHasFatalWound; // 敵に致命傷が付与されているか
        
        // ===== 勝敗フラグ =====
        public bool playerWonRoll;      // プレイヤーがロール勝利したか
        public bool playerLostRoll;     // プレイヤーがロール敗北したか

        // ===== 連続カウンター =====
        public int consecutiveWins;     // 連続勝利数
        public int consecutiveLosses;   // 連続敗北数

        // ===== ダメージ無効化フラグ =====
        public bool nullifyAllDamage;   // 双方ダメージ0にするフラグ
        public bool nullifyPursuitDamage; // 追撃ダメージ無効化フラグ
        public int damageShield;        // 次に受けるダメージの軽減値（天の加護用）

        // ===== オーバーダメージ蓄積（夜スキル用） =====
        public int overDamageAccumulated; // この戦闘中のオーバーダメージ蓄積値

        // ===== 固定ダメージ =====
        public int fixedDamageToEnemy;  // 軽減不可の固定ダメージ

        /// <summary>
        /// コンテキストを初期化
        /// </summary>
        public CombatContext(int playerMaxHP, int enemyMaxHP = 0)
        {
            this.playerMaxHP = playerMaxHP;
            playerBaseMaxHP = playerMaxHP;
            playerCurrentHP = playerMaxHP;
            this.enemyMaxHP = enemyMaxHP;
            enemyCurrentHP = enemyMaxHP;
            currentTurn = 0;
            isFirstRoll = true;
            criticalMultiplier = 2.0f;
            accumulatedValues = new Dictionary<string, float>();
            nextTurnBuffs = new Dictionary<string, float>();
            currentBuffs = new Dictionary<string, float>();
            enemyDiceOverrides = new Dictionary<int, int>();
            pendingDiceOverrides = new List<DiceOverrideRequest>();
        }

        /// <summary>
        /// ターン開始時の状態リセット
        /// </summary>
        public void BeginNewTurn()
        {
            currentTurn++;
            isFirstRoll = (currentTurn == 1);
            
            // 次ターンバフを現在バフへ移行
            currentBuffs.Clear();
            foreach (var kvp in nextTurnBuffs)
            {
                currentBuffs[kvp.Key] = kvp.Value;
            }
            nextTurnBuffs.Clear();

            // 単ターン限りのフラグをリセット
            nullifyAllDamage = false;
            nullifyPursuitDamage = false;
            damageShield = 0;
            fixedDamageToEnemy = 0;
            damageReduced = false;
            isCritical = false;
            criticalBonus = 0;
            criticalMultiplier = 2.0f;

            // ダイス制約を適用
            enemyDiceOverrides.Clear();
            // pendingDiceOverridesは次ターン開始時に評価→適用
        }

        /// <summary>
        /// 蓄積値を安全に取得（キーが無ければ0）
        /// </summary>
        public float GetAccumulated(string key)
        {
            return accumulatedValues.TryGetValue(key, out float val) ? val : 0f;
        }

        /// <summary>
        /// 蓄積値を加算
        /// </summary>
        public void AddAccumulated(string key, float amount)
        {
            if (!accumulatedValues.ContainsKey(key))
                accumulatedValues[key] = 0f;
            accumulatedValues[key] += amount;
        }

        /// <summary>
        /// 現在ターンのバフ値を取得（なければ0）
        /// </summary>
        public float GetBuff(string key)
        {
            return currentBuffs.TryGetValue(key, out float val) ? val : 0f;
        }
    }

    /// <summary>
    /// ダイス固定リクエスト（次ターンに適用される）
    /// </summary>
    public class DiceOverrideRequest
    {
        public enum TargetDice { Lowest, Highest }
        
        public TargetDice target;
        public int fixedValue;
        public string sourceSkill;

        public DiceOverrideRequest(TargetDice target, int fixedValue, string sourceSkill)
        {
            this.target = target;
            this.fixedValue = fixedValue;
            this.sourceSkill = sourceSkill;
        }
    }
}

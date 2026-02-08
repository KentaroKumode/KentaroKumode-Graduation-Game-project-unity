using System;
using System.Collections.Generic;
using UnityEngine;
using InventorySystem;
using InventorySystem.PassiveSkills;
using CombatSystem.DiceLED;

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
        public int pursuitDamage;       // 追撃ダメージ
        public int totalDamage;         // 合算ダメージ（クリティカル適用後）
        public int fixedDamage;         // 固定ダメージ（パッシブ由来）
        public int counterDamage;       // 防御側追撃（ダイス数差）
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
        public int totalTurns;
        public int playerHPRemaining;
        public int enemyHPRemaining;
        public List<TurnResult> turnLog;
    }

    /// <summary>
    /// 戦闘システム管理クラス
    /// 
    /// 戦闘ルール:
    /// (1) 双方のダイスをすべて振り、合計値でマッチ
    /// (2) 合計値の大きい方が勝利
    /// (3) 勝利者は [勝者合計 - 敗者合計] のメインダメージを与える
    /// (4) ダイス数差がある場合:
    ///     (4-1) 攻撃側(マッチ勝者)のダイスが多い → 差分ダイスをリロール、
    ///           その合計を追撃ダメージとして敗者に与える（軽減不可）
    ///     (4-2) 防御側(マッチ敗者)のダイスが多い → 差分ダイスをリロール、
    ///           その合計を反撃ダメージとして勝者に与える（軽減不可）
    /// (5) すべてのダメージを合算後、クリティカル判定（1回、確率: X/9）
    ///     クリティカル時は合算ダメージに倍率適用
    /// 
    /// 使い方:
    /// <code>
    /// CombatManager.Instance.OnCombatEnd += result => { ... };
    /// CombatManager.Instance.StartCombat("goblin", playerMaxHP, playerWeaponDice);
    /// </code>
    /// </summary>
    public class CombatManager : MonoBehaviour
    {
        // ===== シングルトン =====
        private static CombatManager instance;
        private static bool isApplicationQuitting;
        public static CombatManager Instance
        {
            get
            {
                if (isApplicationQuitting)
                    return null;

                if (instance == null)
                {
                    // シーン内の既存オブジェクトを検索
                    instance = FindObjectOfType<CombatManager>();
                    
                    // 見つからない場合のみ新規作成
                    if (instance == null)
                    {
                        var go = new GameObject("[CombatManager]");
                        instance = go.AddComponent<CombatManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        // ===== イベント =====
        /// <summary>戦闘開始イベント（enemyId）</summary>
        public event Action<string> OnCombatStart;
        /// <summary>ターン結果イベント</summary>
        public event Action<TurnResult> OnTurnEnd;
        /// <summary>戦闘終了イベント</summary>
        public event Action<CombatResult> OnCombatEnd;

        // ===== 戦闘状態 =====
        private EnemyData currentEnemy;
        private int playerHP;
        private int playerMaxHP;
        private int enemyHP;
        private int playerDiceCount;
        private int playerDiceMax;
        private int playerCriticalNumerator;
        private int enemyCriticalNumerator;
        private bool isCombatActive;
        private List<TurnResult> turnLog = new List<TurnResult>();

        // ===== LED演出管理 =====
        private DiceLEDManager ledManager;

        public bool IsCombatActive => isCombatActive;
        public EnemyData CurrentEnemy => currentEnemy;
        public int PlayerHP => playerHP;
        public int PlayerMaxHP => playerMaxHP;
        public int EnemyHP => enemyHP;
        public int EnemyMaxHP => currentEnemy != null ? currentEnemy.maxHP : 0;

        // ===========================================================
        //  外部アイテム効果用API
        // ===========================================================

        /// <summary>
        /// プレイヤーのHPを回復（戦闘外でも使用可能）
        /// </summary>
        /// <param name="amount">回復量</param>
        /// <returns>実際に回復した量</returns>
        public int HealPlayer(int amount)
        {
            if (amount <= 0) return 0;
            
            int oldHP = playerHP;
            int newHP = Math.Min(playerMaxHP, playerHP + amount);
            int actualHealed = newHP - oldHP;
            
            playerHP = newHP;
            
            // 戦闘中の場合、CombatContextも更新
            var psm = PassiveSkillManager.Instance;
            if (psm != null && psm.Context != null)
            {
                psm.Context.playerCurrentHP = playerHP;
            }
            
            Debug.Log($"[CombatManager] Player healed: {actualHealed} HP ({oldHP} → {playerHP})");
            return actualHealed;
        }

        /// <summary>
        /// プレイヤーの最大HPを一時的に増加（戦闘中のみ）
        /// </summary>
        /// <param name="amount">増加量</param>
        public void BoostPlayerMaxHP(int amount)
        {
            if (amount <= 0 || !isCombatActive) return;
            
            int oldMaxHP = playerMaxHP;
            playerMaxHP += amount;
            playerHP += amount; // 増加分は即回復
            
            // CombatContextも更新
            var psm = PassiveSkillManager.Instance;
            if (psm != null && psm.Context != null)
            {
                var ctx = psm.Context;
                ctx.playerMaxHP = playerMaxHP;
                ctx.playerCurrentHP = playerHP;
            }
            
            Debug.Log($"[CombatManager] Player MaxHP boosted: {oldMaxHP} → {playerMaxHP} (+{amount})");
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ===========================================================
        //  戦闘開始
        // ===========================================================

        /// <summary>
        /// モンスター名(ID)を指定して戦闘開始
        /// </summary>
        /// <param name="enemyId">enemies.json の id</param>
        /// <param name="playerMaxHP">プレイヤーの最大HP</param>
        /// <param name="playerDiceCount">プレイヤーのダイス数</param>
        /// <param name="playerDiceMax">プレイヤーのダイス最大出目</param>
        /// <param name="playerCritNumerator">プレイヤーのクリティカル確率分子 (0～9)</param>
        public void StartCombat(string enemyId, int playerMaxHP,
            int playerDiceCount, int playerDiceMax, int playerCritNumerator = 0)
        {
            var enemy = EnemyDatabase.Get(enemyId);
            if (enemy == null)
            {
                Debug.LogError($"[CombatManager] Enemy not found: {enemyId}");
                return;
            }

            StartCombatInternal(enemy, playerMaxHP, playerDiceCount, playerDiceMax, playerCritNumerator);
        }

        /// <summary>
        /// EnemyData を直接指定して戦闘開始
        /// </summary>
        public void StartCombat(EnemyData enemy, int playerMaxHP,
            int playerDiceCount, int playerDiceMax, int playerCritNumerator = 0)
        {
            if (enemy == null)
            {
                Debug.LogError("[CombatManager] Enemy data is null!");
                return;
            }

            StartCombatInternal(enemy, playerMaxHP, playerDiceCount, playerDiceMax, playerCritNumerator);
        }

        private void StartCombatInternal(EnemyData enemy, int pMaxHP,
            int pDiceCount, int pDiceMax, int pCritNumerator)
        {
            if (isCombatActive)
            {
                Debug.LogWarning("[CombatManager] Combat already active!");
                return;
            }

            currentEnemy = enemy;
            this.playerMaxHP = pMaxHP;
            playerHP = pMaxHP;
            enemyHP = enemy.maxHP;
            playerDiceCount = pDiceCount;
            playerDiceMax = pDiceMax;
            playerCriticalNumerator = pCritNumerator;
            enemyCriticalNumerator = enemy.criticalNumerator;
            isCombatActive = true;
            turnLog.Clear();

            // パッシブスキルマネージャーに敵スキルを登録
            var psm = PassiveSkillManager.Instance;
            if (enemy.passiveSkills != null)
            {
                psm.RegisterEnemySkills(enemy.passiveSkills);
            }
            else
            {
                psm.RegisterEnemySkills(null);
            }
            psm.BeginCombat(pMaxHP, enemy.maxHP, pDiceMax, enemy.diceMaxValue);

            // 魔王の威圧 等の戦闘開始時スキル処理
            psm.FireTrigger(PassiveSkillTrigger.OnBattleStart);
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnBattleStart);

            // 敵の戦闘開始スキルによるHP減少適用
            var ctx = psm.Context;
            if (ctx != null)
            {
                float hpReduction = ctx.GetAccumulated("enemyMaxHPReduction");
                if (hpReduction > 0)
                {
                    playerHP = Math.Max(1, playerHP - (int)hpReduction);
                    this.playerMaxHP = Math.Max(1, this.playerMaxHP - (int)hpReduction);
                    ctx.playerMaxHP = this.playerMaxHP;
                    ctx.playerCurrentHP = playerHP;
                }
            }

            OnCombatStart?.Invoke(enemy.id);
            
            // ===== LED演出システム初期化 =====
            ledManager = DiceLEDManager.Instance;
            if (ledManager != null)
            {
                // アクティブなダイス数を設定
                ledManager.SetActiveDiceCount(playerDiceCount, enemy.diceCount);
                
                // LED色をリセット（デフォルトカラーに戻す）
                ledManager.TurnOffAll();
                
                Debug.Log($"[CombatManager] DiceLED initialized - Player: {playerDiceCount}, Enemy: {enemy.diceCount}");
            }
            else
            {
                Debug.LogWarning("[CombatManager] DiceLEDManager not found! LED animations will be disabled.");
            }
            
            Debug.Log($"[CombatManager] ===== COMBAT START: {enemy.displayName} =====");
            Debug.Log($"  Player HP: {playerHP}/{this.playerMaxHP}, Dice: {playerDiceCount}d{playerDiceMax}, Crit: {playerCriticalNumerator}/9");
            Debug.Log($"  Enemy  HP: {enemyHP}/{enemy.maxHP}, Dice: {enemy.DiceNotation}, Crit: {enemyCriticalNumerator}/9");
        }

        // ===========================================================
        //  ターン実行
        // ===========================================================

        /// <summary>
        /// 1ターンを実行して結果を返す
        /// </summary>
        public TurnResult ExecuteTurn()
        {
            if (!isCombatActive)
            {
                Debug.LogError("[CombatManager] No active combat!");
                return default;
            }

            var psm = PassiveSkillManager.Instance;
            var ctx = psm.Context;

            // --- ターン開始 ---
            psm.BeginTurn();
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnTurnStart);

            // 敵側のターン開始処理を反映（再生、夜の王 等）
            // SwapPerspective の影響で敵HP変動がplayerCurrentHPに入っている場合があるので
            // context から最新値を同期
            SyncHPFromContext(ctx);

            // 敵のextraDice確認（夜の王 等）
            int enemyExtraDice = (int)ctx.GetAccumulated("extraDice");
            ctx.accumulatedValues["extraDice"] = 0; // リセット

            // --- ダイスロール ---
            int actualPlayerDiceCount = playerDiceCount;
            int actualEnemyDiceCount = currentEnemy.diceCount + enemyExtraDice;

            // enemyDiceDebuff（罠師/呪縛）の適用準備
            int playerDiceDebuff = (int)ctx.GetBuff("enemyDiceDebuff");

            int[] playerDice = RollDice(actualPlayerDiceCount, playerDiceMax);
            int[] enemyDice = RollDice(actualEnemyDiceCount, currentEnemy.diceMaxValue);

            // ===== LED演出実行 =====
            if (ledManager != null)
            {
                // アクティブなダイス数を更新（追加ダイスがある場合）
                ledManager.SetActiveDiceCount(actualPlayerDiceCount, actualEnemyDiceCount);
                
                // ローリングアニメーション開始（非同期）
                ledManager.PlayRollingAnimation(
                    playerDice, enemyDice, 
                    playerDiceMax, currentEnemy.diceMaxValue
                );
                
                Debug.Log($"[CombatManager] LED Animation started - P:{string.Join(",", playerDice)} E:{string.Join(",", enemyDice)}");
            }

            // パッシブスキルによるダイス処理
            psm.ProcessPostRoll(playerDice, enemyDice);

            // enemyDiceDebuff をプレイヤーのダイス合計から減算
            if (playerDiceDebuff > 0 && ctx != null)
            {
                ctx.playerDiceTotal = Math.Max(0, ctx.playerDiceTotal - playerDiceDebuff);
                // diceDifference再計算
                ctx.diceDifference = ctx.playerDiceTotal - ctx.enemyDiceTotal;
                ctx.playerWonRoll = ctx.diceDifference > 0;
                ctx.playerLostRoll = ctx.diceDifference < 0;
            }

            // 敵スキルのPostRoll発火
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostRoll);

            // 勝敗トリガー（敵側）
            if (ctx.playerWonRoll)
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollLose);
            else if (ctx.playerLostRoll)
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollWin);
            else
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollDraw);

            // --- ダメージ計算 ---
            var result = new TurnResult
            {
                turnNumber = ctx.currentTurn,
                playerDice = playerDice,
                enemyDice = enemyDice,
                playerDiceTotal = ctx.playerDiceTotal,
                enemyDiceTotal = ctx.enemyDiceTotal,
                playerWon = ctx.playerWonRoll,
                isDraw = !ctx.playerWonRoll && !ctx.playerLostRoll,
            };

            int diceDiff = Math.Abs(ctx.diceDifference);
            int diceCountDiff = actualPlayerDiceCount - actualEnemyDiceCount;

            if (!result.isDraw)
            {
                // (3) メインダメージ = ダイス合計差
                int mainDmg = diceDiff;

                // (4) 追撃/反撃ダメージ
                int pursuitDmg = 0;
                int counterDmg = 0;

                if (diceCountDiff > 0)
                {
                    // (4-1) プレイヤーのダイスが多い場合
                    if (result.playerWon)
                    {
                        // 勝者(プレイヤー)の追撃: 差分ダイスをリロール
                        pursuitDmg = RollPursuitDice(diceCountDiff, playerDiceMax, ctx);
                    }
                    else
                    {
                        // 敗者(プレイヤー)の反撃: 差分ダイスをリロール → プレイヤーが敵に与える
                        counterDmg = RollPursuitDice(diceCountDiff, playerDiceMax, ctx);
                    }
                }
                else if (diceCountDiff < 0)
                {
                    // (4-2) 敵のダイスが多い場合
                    int absDiff = Math.Abs(diceCountDiff);
                    if (!result.playerWon)
                    {
                        // 勝者(敵)の追撃: 差分ダイスをリロール
                        pursuitDmg = RollPursuitDice(absDiff, currentEnemy.diceMaxValue, ctx);
                    }
                    else
                    {
                        // 敗者(敵)の反撃: 差分ダイスをリロール → 敵がプレイヤーに与える
                        counterDmg = RollPursuitDice(absDiff, currentEnemy.diceMaxValue, ctx);
                    }
                }

                // 敵の多頭攻撃チェック（追撃ダイス追加）
                int extraPursuitDice = (int)ctx.GetAccumulated("extraPursuitDice");
                ctx.accumulatedValues["extraPursuitDice"] = 0;
                if (extraPursuitDice > 0 && !result.playerWon)
                {
                    // 敵が勝っている場合に追撃追加
                    pursuitDmg += RollPursuitDiceRaw(extraPursuitDice, currentEnemy.diceMaxValue);
                }

                if (result.playerWon)
                {
                    // プレイヤーが勝利 → 敵にダメージ
                    var (totalDmg, fixedDmg, isCrit) = psm.ProcessDamage(
                        mainDmg, pursuitDmg, playerCriticalNumerator);

                    // 敵側のダメージ軽減パッシブを発火
                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPreReceiveDamage);
                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPrePursuitDamage);

                    result.mainDamage = mainDmg;
                    result.pursuitDamage = pursuitDmg;
                    result.totalDamage = totalDmg;
                    result.fixedDamage = fixedDmg;
                    result.counterDamage = counterDmg;
                    result.isCritical = isCrit;

                    // 敵にダメージ適用
                    enemyHP = Math.Max(0, enemyHP - totalDmg - fixedDmg);

                    // 反撃ダメージ（敵のダイスが多い場合の敗者反撃）がある場合
                    if (counterDmg > 0)
                    {
                        playerHP = Math.Max(0, playerHP - counterDmg);
                    }

                    // 出血ダメージ
                    if (ctx.enemyBleedStacks > 0)
                    {
                        enemyHP = Math.Max(0, enemyHP - ctx.enemyBleedStacks);
                    }

                    // オーバーダメージ計算（夜スキル用）
                    if (enemyHP == 0 && totalDmg > 0)
                    {
                        ctx.overDamageAccumulated = totalDmg + fixedDmg; // 簡易計算
                    }

                    // 敵側PostDealDamageトリガー
                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostReceiveDamage);
                }
                else
                {
                    // 敵が勝利 → プレイヤーにダメージ
                    // 敵側の攻撃スキルを発火
                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPreDealDamage);

                    var (totalDmg, fixedDmg, isCrit) = psm.ProcessDamage(
                        mainDmg, pursuitDmg, enemyCriticalNumerator);

                    result.mainDamage = mainDmg;
                    result.pursuitDamage = pursuitDmg;
                    result.totalDamage = totalDmg;
                    result.fixedDamage = fixedDmg;
                    result.counterDamage = counterDmg;
                    result.isCritical = isCrit;

                    // プレイヤーにダメージ適用
                    playerHP = Math.Max(0, playerHP - totalDmg);

                    // 地獄の業火 等の固定ダメージ（敵側fixedDamageToEnemy → プレイヤーへ）
                    // 敵視点で fixedDamageToEnemy = プレイヤーへの固定ダメ
                    if (fixedDmg > 0)
                    {
                        playerHP = Math.Max(0, playerHP - fixedDmg);
                    }

                    // 反撃ダメージ（プレイヤーのダイスが多い場合の敗者反撃）
                    if (counterDmg > 0)
                    {
                        enemyHP = Math.Max(0, enemyHP - counterDmg);
                    }

                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostDealDamage);
                }
            }
            else
            {
                // 引き分け: ダメージなし
                result.mainDamage = 0;
                result.pursuitDamage = 0;
                result.totalDamage = 0;
                result.fixedDamage = 0;
                result.counterDamage = 0;
                result.isCritical = false;
            }

            // 死の宣告チェック（固定ダメージとして処理済み）
            if (ctx.fixedDamageToEnemy >= 999)
            {
                playerHP = 0;
            }

            // コンテキストにHP同期
            ctx.playerCurrentHP = playerHP;
            ctx.playerMaxHP = playerMaxHP;
            ctx.enemyCurrentHP = enemyHP;
            ctx.enemyMaxHP = currentEnemy.maxHP;

            // ターン終了トリガー
            psm.FireTrigger(PassiveSkillTrigger.OnTurnEnd);
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnTurnEnd);

            // ターン終了スキルによるHP変動をCombatManagerに反映（棘鎧等）
            SyncHPFromContext(ctx);

            result.playerHPAfter = playerHP;
            result.enemyHPAfter = enemyHP;
            turnLog.Add(result);

            OnTurnEnd?.Invoke(result);

            // ログ出力
            LogTurnResult(result);

            // 戦闘終了チェック
            if (playerHP <= 0 || enemyHP <= 0)
            {
                FinishCombat();
            }

            return result;
        }

        // ===========================================================
        //  自動戦闘（全ターン一括実行）
        // ===========================================================

        /// <summary>
        /// 決着がつくまで自動でターンを回す
        /// </summary>
        public CombatResult ExecuteFullCombat()
        {
            int maxTurns = 100; // 無限ループ防止
            int turn = 0;
            while (isCombatActive && turn < maxTurns)
            {
                ExecuteTurn();
                turn++;
            }

            if (isCombatActive)
            {
                Debug.LogWarning("[CombatManager] Combat exceeded max turns, forcing end.");
                FinishCombat();
            }

            return GetLastCombatResult();
        }

        // ===========================================================
        //  戦闘終了
        // ===========================================================

        private void FinishCombat()
        {
            isCombatActive = false;

            // ===== LED演出リセット =====
            if (ledManager != null)
            {
                // 全LEDを消灯（ローリングアニメーション停止も含む）
                ledManager.TurnOffAll();
                
                Debug.Log("[CombatManager] LED animations reset");
            }

            var result = GetLastCombatResult();
            PassiveSkillManager.Instance.EndCombat();

            OnCombatEnd?.Invoke(result);

            Debug.Log($"[CombatManager] ===== COMBAT END =====");
            Debug.Log($"  Result: {(result.playerWon ? "PLAYER WIN" : "PLAYER LOSE")}");
            Debug.Log($"  Player HP: {playerHP}/{playerMaxHP}");
            Debug.Log($"  Enemy  HP: {enemyHP}/{currentEnemy.maxHP}");
            Debug.Log($"  Total Turns: {result.totalTurns}");
        }

        private CombatResult GetLastCombatResult()
        {
            return new CombatResult
            {
                enemyId = currentEnemy.id,
                enemyDisplayName = currentEnemy.displayName,
                playerWon = playerHP > 0 && enemyHP <= 0,
                totalTurns = turnLog.Count,
                playerHPRemaining = playerHP,
                enemyHPRemaining = enemyHP,
                turnLog = new List<TurnResult>(turnLog)
            };
        }

        // ===========================================================
        //  ユーティリティ
        // ===========================================================

        private int[] RollDice(int count, int maxValue)
        {
            var results = new int[count];
            for (int i = 0; i < count; i++)
            {
                results[i] = UnityEngine.Random.Range(1, maxValue + 1);
            }
            return results;
        }

        /// <summary>
        /// 追撃ダイスをロール（パッシブ経由の追撃軽減は適用しない）
        /// </summary>
        private int RollPursuitDice(int count, int maxValue, CombatContext ctx)
        {
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                total += UnityEngine.Random.Range(1, maxValue + 1);
            }
            return total;
        }

        /// <summary>
        /// 追撃ダイスをロール（パッシブなし、純粋ロール）
        /// </summary>
        private int RollPursuitDiceRaw(int count, int maxValue)
        {
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                total += UnityEngine.Random.Range(1, maxValue + 1);
            }
            return total;
        }

        private void SyncHPFromContext(CombatContext ctx)
        {
            if (ctx == null) return;
            playerHP = ctx.playerCurrentHP;
            playerMaxHP = ctx.playerMaxHP;
            enemyHP = ctx.enemyCurrentHP;
        }

        private void LogTurnResult(TurnResult r)
        {
            string playerDiceStr = r.playerDice != null ? string.Join("+", r.playerDice) : "?";
            string enemyDiceStr = r.enemyDice != null ? string.Join("+", r.enemyDice) : "?";
            string critStr = r.isCritical ? " ★CRITICAL★" : "";
            string winStr = r.isDraw ? "DRAW" : (r.playerWon ? "Player WIN" : "Enemy WIN");

            Debug.Log($"[Turn {r.turnNumber}] {winStr}{critStr}");
            Debug.Log($"  Player Dice: [{playerDiceStr}] = {r.playerDiceTotal}");
            Debug.Log($"  Enemy  Dice: [{enemyDiceStr}] = {r.enemyDiceTotal}");
            if (!r.isDraw)
            {
                Debug.Log($"  Main: {r.mainDamage}, Pursuit: {r.pursuitDamage}, " +
                          $"Total: {r.totalDamage}, Fixed: {r.fixedDamage}, Counter: {r.counterDamage}");
            }
            Debug.Log($"  Player HP: {r.playerHPAfter}, Enemy HP: {r.enemyHPAfter}");
        }

        void OnDestroy()
        {
            if (instance == this) 
            {
                instance = null;
                // イベント購読者のクリーンアップ
                OnCombatStart = null;
                OnTurnEnd = null;
                OnCombatEnd = null;
            }
        }
        
        void OnApplicationQuit()
        {
            isApplicationQuitting = true;
            if (instance == this) instance = null;
        }
    }
}

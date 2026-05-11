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
    /// (4) 追撃/反撃はパッシブスキル（PursuitI-III / CounterI-III）由来の固定値
    /// (5) scratchは特定の敵パッシブ（ScratchAura）が付与する削りダメージ
    /// (6) すべてのダメージを合算後、クリティカル判定（1回、確率: X/9）
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
        private bool metaLethalSurviveUsed; // メタデバフ Lv10: 敵の初回致命傷を1HPで耐える
        private bool wrathDiceOverrideArmed; // 恒久デバフ「憤怒」: 1T目のダイス最大化トリガー
        private int turnStartPlayerHP;      // ラストスタンド用: ターン開始時のプレイヤーHP
        private int rollLossMainDamage;     // ラストスタンド用: 今ターンのロール敗北メインダメ確定値
        private int playerDiceCount;
        private int playerDiceMax;
        private int playerCriticalNumerator;
        private int enemyCriticalNumerator;
        private bool isCombatActive;
        private List<TurnResult> turnLog = new List<TurnResult>();

        /// <summary>
        /// 5層ボス戦中、毎ターン終了時にプレイヤーへ与える「カルマの呪い」ダメージ。
        /// GameManager から SetKarmaCurseForCombat で設定し、FinishCombat でリセット。
        /// </summary>
        private int karmaCurseDamagePerTurn;

        // ===== LED演出管理 =====
        private DiceLEDManager ledManager;

        /// <summary>
        /// 次の StartCombat に適用されるカルマの呪いダメージ量を設定する。
        /// 5層ボス戦開始直前に GameManager から呼ばれる想定。
        /// </summary>
        public void SetKarmaCurseForCombat(int amount)
        {
            karmaCurseDamagePerTurn = Math.Max(0, amount);
        }

        public int KarmaCurseDamagePerTurn => karmaCurseDamagePerTurn;

        /// <summary>
        /// 時限バフ（解放者）等の効果で、戦闘中の最大HPと現在HPを一時的に増やす。
        /// 戦闘終了で playerMaxHP は次戦闘で再設定されるため特別なリセット不要。
        /// </summary>
        public void GrantTemporaryHpBonus(int amount)
        {
            if (amount <= 0) return;
            playerMaxHP += amount;
            playerHP = Math.Min(playerMaxHP, playerHP + amount);
            var ctx = PassiveSkillManager.Instance?.Context;
            if (ctx != null)
            {
                ctx.playerMaxHP = playerMaxHP;
                ctx.playerCurrentHP = playerHP;
            }
        }

        /// <summary>
        /// 時限バフ（使命感）等の効果で、その戦闘のプレイヤーダイス数を一時的に増やす。
        /// </summary>
        public void GrantTemporaryDiceCountBonus(int delta)
        {
            if (delta == 0) return;
            playerDiceCount = Math.Max(1, playerDiceCount + delta);
        }

        /// <summary>
        /// 戦闘終了時にプレイヤーHPを最大値まで回復（泉の祝福）。
        /// FinishCombat の冒頭で呼ばれた場合、CombatResult にも反映される。
        /// </summary>
        public void HealPlayerToFull()
        {
            playerHP = playerMaxHP;
            var ctx = PassiveSkillManager.Instance?.Context;
            if (ctx != null) ctx.playerCurrentHP = playerHP;
        }

        /// <summary>戦闘中にプレイヤーへ直接ダメージ（中毒等の時限デバフから呼ばれる）。</summary>
        public void DamagePlayerDirect(int amount)
        {
            if (amount <= 0 || !isCombatActive) return;
            playerHP = Math.Max(0, playerHP - amount);
            var ctx = PassiveSkillManager.Instance?.Context;
            if (ctx != null) ctx.playerCurrentHP = playerHP;
        }

        public bool IsCombatActive => isCombatActive;
        public EnemyData CurrentEnemy => currentEnemy;
        public int PlayerHP => playerHP;
        public int PlayerMaxHP => playerMaxHP;
        public int EnemyHP => enemyHP;
        public int CurrentCombatTurn => ctx?.currentTurn ?? 0;
        public bool IsCombatActive => isCombatActive;
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

            // 呪いの渇き: HP回復効果を半減
            var psmCtxForHeal = PassiveSkillManager.Instance?.Context;
            if (psmCtxForHeal != null && psmCtxForHeal.healHalved)
                amount = Math.Max(1, amount / 2);

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
            int playerDiceCount, int playerDiceMax, int playerCritNumerator = 0, int[] equippedDiceFaces = null)
        {
            var enemy = EnemyDatabase.Get(enemyId);
            if (enemy == null)
            {
                Debug.LogError($"[CombatManager] Enemy not found: {enemyId}");
                return;
            }

            StartCombatInternal(enemy, playerMaxHP, playerDiceCount, playerDiceMax, playerCritNumerator, equippedDiceFaces);
        }

        /// <summary>
        /// EnemyData を直接指定して戦闘開始
        /// </summary>
        public void StartCombat(EnemyData enemy, int playerMaxHP,
            int playerDiceCount, int playerDiceMax, int playerCritNumerator = 0, int[] equippedDiceFaces = null)
        {
            if (enemy == null)
            {
                Debug.LogError("[CombatManager] Enemy data is null!");
                return;
            }

            StartCombatInternal(enemy, playerMaxHP, playerDiceCount, playerDiceMax, playerCritNumerator, equippedDiceFaces);
        }

        private void StartCombatInternal(EnemyData enemy, int pMaxHP,
            int pDiceCount, int pDiceMax, int pCritNumerator, int[] equippedDiceFaces = null)
        {
            if (isCombatActive)
            {
                Debug.LogWarning("[CombatManager] Combat already active!");
                return;
            }

            currentEnemy = enemy;

            // ラン中所持パッシブと装備品を PassiveSkillManager に同期（戦闘ごとに再構築）
            var equipHandler = UnityEngine.Object.FindObjectOfType<InventorySystem.ItemEquipHandler>();
            InventorySystem.PassiveSkills.RunPassiveSync.RefreshFromRun(
                GameLoop.GameManager.Instance?.Run, equipHandler);

            // SinDebuff による6層ボスへの動的注入は仕様変更で廃止

            this.playerMaxHP = pMaxHP;
            playerHP = pMaxHP;
            enemyHP = enemy.maxHP;
            playerDiceCount = pDiceCount;
            playerDiceMax = pDiceMax;
            playerCriticalNumerator = pCritNumerator;
            enemyCriticalNumerator = enemy.criticalNumerator;
            isCombatActive = true;
            metaLethalSurviveUsed = false;
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
            psm.BeginCombat(pMaxHP, enemy.maxHP, pDiceMax, enemy.diceMaxValue, enemy.threat);

            // 装備ダイスの面をコンテキストに設定
            var ctx = psm.Context;
            if (ctx != null && equippedDiceFaces != null)
            {
                ctx.equippedDiceFaces = equippedDiceFaces;
            }

            // 魔王の威圧 等の戦闘開始時スキル処理
            psm.FireTrigger(PassiveSkillTrigger.OnBattleStart);
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnBattleStart);

            // 敵の戦闘開始スキルによるHP減少適用
            ctx = psm.Context;
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

            // 時限バフ・デバフ（戦闘開始時系）の適用
            EventSystem.TimedEffects.TimedEffectManager.OnCombatStart(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // 名前付き固有パッシブアイテム（戦闘開始時系）
            InventorySystem.PassiveItems.PassiveItemManager.OnCombatStart(
                ctx, GameLoop.GameManager.Instance?.Run, this);

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
            
            // 恒久デバフ「コルヴェンの憤怒」: ボス戦の戦闘開始時、現在HP半減（1T目のダイス最大化は OnRoll で処理）
            var runForWrath = GameLoop.GameManager.Instance?.Run;
            var nodeForWrath = MapSystem.MapManager.Instance?.CurrentNode;
            wrathDiceOverrideArmed = false;
            if (runForWrath != null && nodeForWrath != null
                && nodeForWrath.type == MapSystem.TileType.Boss
                && MetaProgression.PermanentDebuffEffects.HasWrath(runForWrath))
            {
                int oldHp = playerHP;
                playerHP = Mathf.Max(1, playerHP / 2); // 割合計算が先
                wrathDiceOverrideArmed = true;
                Debug.Log($"[CombatManager] 恒久デバフ {MetaProgression.PermanentDebuffIds.Wrath}: HP {oldHp}→{playerHP}, 1T目ダイス最大化を予約");
            }

            Debug.Log($"[CombatManager] ===== COMBAT START: {enemy.displayName} =====");
            Debug.Log($"  Player HP: {playerHP}/{this.playerMaxHP}, Dice: {playerDiceCount}d{playerDiceMax}, Crit: {playerCriticalNumerator}/9");
            Debug.Log($"  Enemy  HP: {enemyHP}/{enemy.maxHP}, Dice: {enemy.DiceNotation}, Crit: {enemyCriticalNumerator}/9, Threat: {enemy.threat}");
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
            // ラストスタンド: ターン開始 HP をスナップ、ロール敗北メインダメ蓄積をリセット
            turnStartPlayerHP = playerHP;
            rollLossMainDamage = 0;

            psm.BeginTurn();
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnTurnStart);

            // 敵側のターン開始処理を反映（再生、夜の王 等）
            // SwapPerspective の影響で敵HP変動がplayerCurrentHPに入っている場合があるので
            // context から最新値を同期
            SyncHPFromContext(ctx);

            // フロアデバフ: 敵の毎ターンHP回復（6層 深淵の洗礼: enemy +3）
            var floorMod = GameLoop.GameManager.Instance?.ActiveModifier;
            if (floorMod != null && floorMod.enemyPerTurnHeal > 0 && enemyHP > 0)
            {
                int heal = Mathf.Min(currentEnemy.maxHP - enemyHP, floorMod.enemyPerTurnHeal);
                if (heal > 0)
                {
                    enemyHP += heal;
                    ctx.enemyCurrentHP = enemyHP;
                    Debug.Log($"[CombatManager] フロアデバフ: 敵HP+{heal} ({enemyHP}/{currentEnemy.maxHP})");
                }
            }

            // 敵のextraDice確認（夜の王 等）
            int enemyExtraDice = (int)ctx.GetAccumulated("extraDice");
            ctx.accumulatedValues["extraDice"] = 0; // リセット

            // --- ダイスロール ---
            int actualPlayerDiceCount = playerDiceCount;
            int actualEnemyDiceCount = currentEnemy.diceCount + enemyExtraDice;

            int[] playerDice = RollDice(actualPlayerDiceCount, playerDiceMax, ctx.equippedDiceFaces);
            int[] enemyDice = RollDice(actualEnemyDiceCount, currentEnemy.diceMaxValue);

            // 獣の恩義: 1ターン目の敵ロールを全て0にする（プレイヤー実質勝利確定）
            if (ctx.nullifyFirstEnemyRoll && ctx.currentTurn == 1)
            {
                for (int i = 0; i < enemyDice.Length; i++) enemyDice[i] = 0;
                ctx.nullifyFirstEnemyRoll = false;
                Debug.Log("[CombatManager] 獣の恩義発動: 敵の最初のロール無効化");
            }

            // 影の代償: 5層ボス戦中、毎ロール50%でプレイヤーダイス全出目-1
            var run = GameLoop.GameManager.Instance?.Run;
            if (run != null
                && run.currentFloor == run.normalClearFloor
                && currentEnemy != null
                && MapSystem.MapManager.Instance?.CurrentNode != null
                && MapSystem.MapManager.Instance.CurrentNode.type == MapSystem.TileType.Boss
                && run.permanentDebuffs.Contains("影の代償")
                && UnityEngine.Random.value < 0.5f)
            {
                for (int i = 0; i < playerDice.Length; i++)
                    playerDice[i] = Math.Max(1, playerDice[i] - 1);
                Debug.Log("[CombatManager] 影の代償発動 (50%): プレイヤー全出目-1");
            }

            // ロール時系時限効果がダイス配列を直接書き換えるため、ctx に参照を渡しておく
            ctx.playerDice = playerDice;
            ctx.enemyDice = enemyDice;
            ctx.playerDiceMax = playerDiceMax;
            EventSystem.TimedEffects.TimedEffectManager.OnRoll(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // 名前付き固有パッシブ（ロール時系）
            InventorySystem.PassiveItems.PassiveItemManager.OnRoll(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // 恒久デバフ「コルヴェンの憤怒」: ボス戦1T目に自ダイスを全て最大値化
            if (wrathDiceOverrideArmed && ctx.currentTurn == 1)
            {
                for (int i = 0; i < playerDice.Length; i++) playerDice[i] = playerDiceMax;
                wrathDiceOverrideArmed = false;
                Debug.Log($"[CombatManager] {MetaProgression.PermanentDebuffIds.Wrath}: 1T目ダイス全て最大値");
            }

            // メタバフ: ダイス合計値補正（一番低いダイスから +1 を順次振り分け、各ダイスは playerDiceMax 上限）
            int metaDiceBonus = MetaProgression.MetaBuffApplicator.GetDiceTotalBonus();
            int safety = metaDiceBonus * playerDice.Length;
            while (metaDiceBonus > 0 && safety-- > 0)
            {
                int minIdx = -1;
                for (int j = 0; j < playerDice.Length; j++)
                {
                    if (playerDice[j] >= playerDiceMax) continue;
                    if (minIdx < 0 || playerDice[j] < playerDice[minIdx]) minIdx = j;
                }
                if (minIdx < 0) break; // 全て上限到達
                playerDice[minIdx]++;
                metaDiceBonus--;
            }

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

            if (!result.isDraw)
            {
                // (3) メインダメージ = ダイス合計差
                int mainDmg = diceDiff;

                // (4) 追撃ダメージ（パッシブ由来: ctx.pursuitDamage はスキル発火時にセット済み）
                int pursuitDmg = ctx.pursuitDamage;

                if (result.playerWon)
                {
                    // プレイヤーが勝利 → 敵にダメージ
                    var (totalDmg, fixedDmg, isCrit) = psm.ProcessDamage(
                        mainDmg, pursuitDmg, playerCriticalNumerator);

                    // 与ダメ倍率（激情の刃 等のパッシブ由来）
                    if (ctx.outgoingDamageMultiplier > 0f
                        && Mathf.Abs(ctx.outgoingDamageMultiplier - 1f) > 0.001f)
                    {
                        int orig = totalDmg;
                        totalDmg = Mathf.CeilToInt(totalDmg * ctx.outgoingDamageMultiplier);
                        Debug.Log($"[CombatManager] 与ダメ補正 ×{ctx.outgoingDamageMultiplier:F2}: {orig}→{totalDmg}");
                    }

                    // メタバフ: 会心時の追加補正
                    if (isCrit)
                    {
                        int critBonus = MetaProgression.MetaBuffApplicator.GetCritBonus();
                        if (critBonus > 0) totalDmg += critBonus;
                    }

                    // メタデバフ Lv3 向かい風: -1（最低 1 は残す）
                    int metaPlayerDmgRed = MetaProgression.MetaDebuffApplicator.GetPlayerDamageReduction();
                    if (metaPlayerDmgRed > 0 && totalDmg > 0)
                        totalDmg = Mathf.Max(1, totalDmg - metaPlayerDmgRed);

                    // 敵側のダメージ軽減パッシブを発火
                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPreReceiveDamage);

                    result.mainDamage = mainDmg;
                    result.pursuitDamage = pursuitDmg;
                    result.totalDamage = totalDmg;
                    result.fixedDamage = fixedDmg;
                    result.isCritical = isCrit;

                    // 敵にダメージ適用（メイン＋プレイヤー→敵固定）
                    enemyHP = Math.Max(0, enemyHP - totalDmg - fixedDmg);

                    // メタデバフ Lv10: 敵の初回致命傷を1HPで耐える
                    if (enemyHP == 0
                        && !metaLethalSurviveUsed
                        && MetaProgression.MetaDebuffApplicator.EnemySurvivesFirstLethal())
                    {
                        enemyHP = 1;
                        metaLethalSurviveUsed = true;
                        Debug.Log("[CombatManager] メタデバフ Lv10: 敵が初回致命傷で1HPに踏みとどまった");
                    }

                    // 出血ダメージ
                    if (ctx.enemyBleedStacks > 0)
                    {
                        int bleedDmg = BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks);
                        enemyHP = Math.Max(0, enemyHP - bleedDmg);
                    }

                    // 敵→プレイヤー固定ダメージ（TailStrike/Hellfire等）
                    if (ctx.fixedDamageToPlayer > 0)
                        playerHP = Math.Max(0, playerHP - ctx.fixedDamageToPlayer);

                    // オーバーダメージ計算（蝕夜スキル用）
                    if (enemyHP == 0 && totalDmg > 0)
                        ctx.overDamageAccumulated = totalDmg + fixedDmg;

                    // === Scratch計算（敵パッシブScratchAuraがセット済みの場合のみ適用） ===
                    ctx.scratchDamage = BattleModifierManager.ApplyScratchModifiers(ctx, ctx.scratchDamage);
                    psm.FireTrigger(PassiveSkillTrigger.OnPreScratchDamage);
                    if (!ctx.nullifyScratchDamage && ctx.scratchDamage > 0)
                        playerHP = Math.Max(0, playerHP - ctx.scratchDamage);
                    result.scratchDamage = ctx.nullifyScratchDamage ? 0 : ctx.scratchDamage;

                    // 敵側PostDealDamageトリガー
                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostReceiveDamage);
                }
                else
                {
                    // 敵が勝利 → プレイヤーにダメージ
                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPreDealDamage);

                    var (totalDmg, fixedDmg, isCrit) = psm.ProcessDamage(
                        mainDmg, 0, enemyCriticalNumerator);

                    result.mainDamage = mainDmg;
                    result.pursuitDamage = 0;
                    result.totalDamage = totalDmg;
                    result.fixedDamage = fixedDmg;
                    result.isCritical = isCrit;

                    // メタデバフ Lv2 凶暴化: 50%で +1
                    int rageBonus = MetaProgression.MetaDebuffApplicator.RollEnemyDamageBonus();
                    if (rageBonus > 0) totalDmg += rageBonus;

                    // メタデバフ Lv5 狂った時計: 7T以降+1/T、最大+5
                    int madClock = MetaProgression.MetaDebuffApplicator.GetMadClockBonus(ctx.currentTurn);
                    if (madClock > 0) totalDmg += madClock;

                    // メタデバフ Lv10 天変地異: 敵ダメージ ×2.0
                    float enemyMul = MetaProgression.MetaDebuffApplicator.GetEnemyDamageMultiplier();
                    if (Mathf.Abs(enemyMul - 1f) > 0.001f)
                        totalDmg = Mathf.CeilToInt(totalDmg * enemyMul);

                    // メタバフ: 被ダメ -X（最大-2、ただし最低 1）
                    int metaDmgRed = MetaProgression.MetaBuffApplicator.GetDamageReduction();
                    if (metaDmgRed > 0 && totalDmg > 0)
                        totalDmg = Mathf.Max(1, totalDmg - metaDmgRed);

                    // 名前付きパッシブ由来の固定被ダメ削減（不屈の鎧・苦難の刻印 等の合算）
                    if (ctx.playerFlatDamageReduction > 0 && totalDmg > 0)
                        totalDmg = Mathf.Max(1, totalDmg - ctx.playerFlatDamageReduction);

                    // フロアデバフ: 敗北時の被ダメージ軽減（5層 地獄門: -2）
                    if (floorMod != null && floorMod.defeatDamageReduction > 0 && totalDmg > 0)
                    {
                        int reduced = Mathf.Min(totalDmg, floorMod.defeatDamageReduction);
                        totalDmg -= reduced;
                        Debug.Log($"[CombatManager] フロアデバフ: 敗北時被ダメ-{reduced} → {totalDmg}");
                    }

                    // 亡者の招待: 被ダメ +30%
                    if (ctx.receivedDamageBonus > 0f && totalDmg > 0)
                    {
                        int bonusDmg = Mathf.CeilToInt(totalDmg * ctx.receivedDamageBonus);
                        totalDmg += bonusDmg;
                        Debug.Log($"[CombatManager] 亡者の招待: 被ダメ+{bonusDmg} (合計{totalDmg})");
                    }

                    // 共助: 1ターン目のメインダメージ半減
                    if (ctx.halveFirstEnemyAttack && ctx.currentTurn == 1)
                    {
                        totalDmg = totalDmg / 2;
                        ctx.halveFirstEnemyAttack = false;
                        Debug.Log("[CombatManager] 共助発動: 敵の最初の攻撃を半減");
                    }

                    // 獣の絆: 被弾無効化チャージ消費
                    if (ctx.playerDamageNegateCharges > 0 && totalDmg > 0)
                    {
                        ctx.playerDamageNegateCharges--;
                        Debug.Log($"[CombatManager] 獣の絆発動: 被弾{totalDmg}を無効化（残チャージ{ctx.playerDamageNegateCharges}）");
                        totalDmg = 0;
                    }

                    // ラストスタンド用: 純粋なロール敗北メインダメ
                    rollLossMainDamage = totalDmg;

                    // プレイヤーにダメージ適用（メイン＋敵→プレイヤー固定）
                    playerHP = Math.Max(0, playerHP - totalDmg);
                    if (ctx.fixedDamageToPlayer > 0)
                        playerHP = Math.Max(0, playerHP - ctx.fixedDamageToPlayer);

                    // 敗北時でも反撃・固定ダメージは敵に適用（Counter/Riposte等）
                    if (fixedDmg > 0)
                        enemyHP = Math.Max(0, enemyHP - fixedDmg);

                    // 出血ダメージ（敗北時も適用）
                    if (ctx.enemyBleedStacks > 0)
                    {
                        int bleedDmg = BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks);
                        enemyHP = Math.Max(0, enemyHP - bleedDmg);
                    }

                    // scratchは敗北時なし（メインダメージに含有）
                    result.scratchDamage = 0;

                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostDealDamage);
                }
            }
            else
            {
                // 引き分け: メインダメージなし、scratchなし
                result.mainDamage = 0;
                result.pursuitDamage = 0;
                result.totalDamage = 0;
                result.fixedDamage = ctx.fixedDamageToEnemy;
                result.isCritical = false;
                result.scratchDamage = 0;

                // 引き分け時も固定ダメージは双方に適用
                if (ctx.fixedDamageToEnemy > 0)
                    enemyHP = Math.Max(0, enemyHP - ctx.fixedDamageToEnemy);
                if (ctx.fixedDamageToPlayer > 0)
                    playerHP = Math.Max(0, playerHP - ctx.fixedDamageToPlayer);

                // 出血ダメージ（引き分け時も適用）
                if (ctx.enemyBleedStacks > 0)
                {
                    int bleedDmg = BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks);
                    enemyHP = Math.Max(0, enemyHP - bleedDmg);
                }
            }

            // 死の宣告チェック（敵スキル由来の即死ダメージ）
            if (ctx.fixedDamageToPlayer >= 999)
                playerHP = 0;

            // コンテキストにHP同期
            ctx.playerCurrentHP = playerHP;
            ctx.playerMaxHP = playerMaxHP;
            ctx.enemyCurrentHP = enemyHP;
            ctx.enemyMaxHP = currentEnemy.maxHP;

            // ターン終了トリガー
            psm.FireTrigger(PassiveSkillTrigger.OnTurnEnd);
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnTurnEnd);

            // ターン終了系時限効果（中毒等。適用のみ、消費は戦闘終了時）
            EventSystem.TimedEffects.TimedEffectManager.OnTurnEnd(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // 名前付き固有パッシブ（ターン終了時系。現状は該当なし、フック確保のため呼び出し）
            InventorySystem.PassiveItems.PassiveItemManager.OnTurnEnd(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // フロアデバフ: 毎ターン自傷（6層 深淵の洗礼: -1）
            if (floorMod != null && floorMod.perTurnSelfDamage > 0 && playerHP > 0)
            {
                int dmg = floorMod.perTurnSelfDamage;
                playerHP = Math.Max(0, playerHP - dmg);
                ctx.playerCurrentHP = playerHP;
                Debug.Log($"[CombatManager] フロアデバフ: 自傷-{dmg} (HP: {playerHP}/{playerMaxHP})");
            }

            // ターン終了スキルによるHP変動をCombatManagerに反映（剣鎧等）
            SyncHPFromContext(ctx);

            // カルマの呪い（5層ボス戦専用）: 毎ターン終了時にカルマ点数分のダメージ
            if (karmaCurseDamagePerTurn > 0 && playerHP > 0)
            {
                int dmg = karmaCurseDamagePerTurn;
                playerHP = Math.Max(0, playerHP - dmg);
                ctx.playerCurrentHP = playerHP;
                Debug.Log($"[CombatManager] カルマの呪い: HP-{dmg} (現在 {playerHP}/{playerMaxHP})");
            }

            // ラストスタンド: 発動中はロール敗北メインダメ以外を巻き戻し（scratch/固定ダメ/敵パッシブ由来等を全て無効化）
            var lsRun = GameLoop.GameManager.Instance?.Run;
            if (lsRun != null && lsRun.lastStandActive)
            {
                int allowedHP = Math.Max(0, turnStartPlayerHP - rollLossMainDamage);
                if (playerHP < allowedHP)
                {
                    Debug.Log($"[ラストスタンド] 非ロール敗北ダメをリワインド: {playerHP} → {allowedHP}");
                    playerHP = allowedHP;
                    ctx.playerCurrentHP = playerHP;
                }
            }

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
            // 戦闘終了系時限効果（泉の祝福、芽吹きの祈り等）+ ロール/ターン系の消費
            var psmCtx = PassiveSkillManager.Instance?.Context;
            EventSystem.TimedEffects.TimedEffectManager.OnCombatEnd(
                psmCtx, GameLoop.GameManager.Instance?.Run, this);

            // 名前付き固有パッシブ（戦闘終了時系: 巡礼者の杖、希望の灯片）
            InventorySystem.PassiveItems.PassiveItemManager.OnCombatEnd(
                psmCtx, GameLoop.GameManager.Instance?.Run, this);

            isCombatActive = false;
            karmaCurseDamagePerTurn = 0;

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

        private int[] RollDice(int count, int maxValue, int[] diceFaces = null)
        {
            var results = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (diceFaces != null && diceFaces.Length > 0)
                {
                    results[i] = diceFaces[UnityEngine.Random.Range(0, diceFaces.Length)];
                }
                else
                {
                    results[i] = UnityEngine.Random.Range(1, maxValue + 1);
                }
            }
            return results;
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
                          $"Total: {r.totalDamage}, Fixed: {r.fixedDamage}, Scratch: {r.scratchDamage}");
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

        // ================================================================
        //  6層 SinAltar 由来の永続デバフ → ボス強化
        // ================================================================

        /// <summary>
        /// 敵が 6層ボス (id == "boss_layer6") かつ RunState に SinDebuff が立っている場合のみ、
        /// EnemyData の HP / dice / threat と passiveSkills を実行時に書き換える。
        /// 通常の敵には何もしない。
        /// </summary>
        private void ApplySinDebuffsToBossIfApplicable(EnemyData enemy)
        {
            if (enemy == null) return;
            if (enemy.id != "boss_layer6") return;

            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null || run.sinDebuffs == GameLoop.SinDebuff.None) return;

            // 追加パッシブを差し込めるよう、リストを必要なら新規化
            if (enemy.passiveSkills == null)
                enemy.passiveSkills = new System.Collections.Generic.List<EnemyPassiveEntry>();

            int hpAdd = 0;
            int diceAdd = 0;
            int threatAdd = 0;

            if (run.HasDebuff(GameLoop.SinDebuff.HeartOfGolgotha))
            {
                hpAdd += 20;
                threatAdd += 1;
                enemy.passiveSkills.Add(new EnemyPassiveEntry { internalName = "boss6_golgotha", skillName = "ゴルゴダの心" });
            }
            if (run.HasDebuff(GameLoop.SinDebuff.SeveredTime))
            {
                diceAdd += 1;
                threatAdd += 1;
                enemy.passiveSkills.Add(new EnemyPassiveEntry { internalName = "boss6_severed_time", skillName = "断絶した時間" });
            }
            if (run.HasDebuff(GameLoop.SinDebuff.AshenBrand))
            {
                hpAdd += 20;
                threatAdd += 1;
                enemy.passiveSkills.Add(new EnemyPassiveEntry { internalName = "boss6_ashen", skillName = "灰燼の烙印" });
            }

            enemy.maxHP += hpAdd;
            enemy.diceCount += diceAdd;
            enemy.threat += threatAdd;

            Debug.Log($"[CombatManager] 6層ボス強化: debuffs={run.sinDebuffs} → HP+{hpAdd}, dice+{diceAdd}, threat+{threatAdd}");
        }
    }
}

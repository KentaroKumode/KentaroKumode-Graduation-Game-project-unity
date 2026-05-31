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
        /// <summary>解脱 — 妙覚サドンデス勝利。playerWon=true と併用。</summary>
        public bool gedatsu;
        /// <summary>検証計測: この戦闘でプレイヤーが獲得した累計回復量／シールド量。</summary>
        public int healApplied;
        public int shieldGained;
        /// <summary>L1学習: プレイヤーが敵に与えた総ダメージ (enemyMaxHP - 残HP の単純差分)。</summary>
        public int damageDealt;
        /// <summary>L1学習: プレイヤーが受けた純粋な総ダメージ (healApplied 補正済み)。</summary>
        public int damageTaken;
        /// <summary>L1学習: 敵 maxHP（撃破率計算用）。</summary>
        public int enemyMaxHP;
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
        private bool metaLethalSurviveUsed; // メタデバフ Lv9: 敵の初回致命傷を1HPで耐える
        private bool metaAgilityDodgeUsed;  // メタデバフ Lv2 俊敏: 敵が初回被弾を回避済みか
        private int fleeAfterTurns;          // >0 のとき、このターン数を終えても未決着なら敵が逃走（偽の商人）
        private bool wrathDiceOverrideArmed; // 恒久デバフ「憤怒」: 1T目のダイス最大化トリガー
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

        /// <summary>戦闘中、敵に軽減不可ダメージを直接与える（記憶の砂時計・各種パッシブ用）。</summary>
        public void DealFixedDamageToEnemy(int amount)
        {
            if (amount <= 0 || !isCombatActive) return;
            enemyHP = Math.Max(0, enemyHP - amount);
            var ctx = PassiveSkillManager.Instance?.Context;
            if (ctx != null) ctx.enemyCurrentHP = enemyHP;
        }

        public bool IsCombatActive => isCombatActive;
        public EnemyData CurrentEnemy => currentEnemy;
        public int PlayerHP => playerHP;
        public int PlayerMaxHP => playerMaxHP;
        public int EnemyHP => enemyHP;
        public int CurrentCombatTurn => PassiveSkillManager.Instance?.Context?.currentTurn ?? 0;
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

            // 呪いの渇き: HP回復効果を半減 / 狂暴化: 回復完全封印
            var psmCtxForHeal = PassiveSkillManager.Instance?.Context;
            if (psmCtxForHeal != null && psmCtxForHeal.healBlocked)
            {
                Debug.Log("[CombatManager] 狂暴化: 回復が封じられている (回復0)");
                return 0;
            }
            if (psmCtxForHeal != null && psmCtxForHeal.healHalved)
                amount = Math.Max(1, amount / 2);
            // 覚者〈天衣無縫〉: 獲得回復量をスタック分減衰（0未満は0）
            if (psmCtxForHeal != null && psmCtxForHeal.healShieldReduction > 0)
            {
                amount = Math.Max(0, amount - psmCtxForHeal.healShieldReduction);
                if (amount <= 0) return 0;
            }

            int oldHP = playerHP;
            int newHP = Math.Min(playerMaxHP, playerHP + amount);
            int actualHealed = newHP - oldHP;
            
            playerHP = newHP;
            
            // 戦闘中の場合、CombatContextも更新
            var psm = PassiveSkillManager.Instance;
            if (psm != null && psm.Context != null)
            {
                psm.Context.playerCurrentHP = playerHP;
                psm.Context.healAppliedTotal += actualHealed; // 検証計測
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

        /// <summary>偽の商人戦用: 規定ターン数を終えても未決着なら敵が逃走する。
        /// StartCombat の後に呼ぶ（StartCombat 内で 0 にリセットされるため）。</summary>
        public void SetFleeAfterTurns(int turns) => fleeAfterTurns = Mathf.Max(0, turns);

        /// <summary>敵のダイス合計値へ毎ロール加算するボーナスを足す（偽の商人「貪欲」等）。
        /// StartCombat の後（ctx 生成後）に呼ぶ。</summary>
        public void AddEnemyDiceTotalBonus(int bonus)
        {
            var ctx = PassiveSkillManager.Instance?.Context;
            if (ctx != null && bonus > 0) ctx.enemyDiceTotalBonus += bonus;
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
            metaAgilityDodgeUsed = false;
            fleeAfterTurns = 0;
            turnLog.Clear();

            // パッシブスキルマネージャーに敵スキルを登録
            var psm = PassiveSkillManager.Instance;
            var enemySkills = enemy.passiveSkills != null
                ? new List<EnemyPassiveEntry>(enemy.passiveSkills)
                : new List<EnemyPassiveEntry>();

            // ボスノードの敵には全員「狂暴化」を付与（50T後のエンレイジ）
            var nodeForBerserk = MapSystem.MapManager.Instance?.CurrentNode;
            if (nodeForBerserk != null && nodeForBerserk.type == MapSystem.TileType.Boss
                && !enemySkills.Exists(e => e != null && e.internalName == "Berserk"))
            {
                enemySkills.Add(new EnemyPassiveEntry
                {
                    internalName = "Berserk",
                    skillName = "狂暴化",
                    description = "50ターン経過後、ダイス合計+10・回復封印・被ダメージ3倍",
                });
            }
            psm.RegisterEnemySkills(enemySkills);
            psm.BeginCombat(pMaxHP, enemy.maxHP, pDiceMax, enemy.diceMaxValue, enemy.threat);

            // 装備ダイスの面をコンテキストに設定
            var ctx = psm.Context;
            if (ctx != null && equippedDiceFaces != null)
            {
                ctx.equippedDiceFaces = equippedDiceFaces;
            }
            // 敵の基礎防御（被ダメ%軽減）を反映。エリート(EliteVigor)は OnBattleStart で +0.10 する。
            if (ctx != null)
            {
                ctx.enemyDamageReductionPct = enemy.baseDefenseRate;
            }

            // Λ層（時間の狭間）由来の恒久デバフを ctx へ設定（戦闘スコープで保持）
            if (ctx != null)
            {
                var runForLambda = GameLoop.GameManager.Instance?.Run;
                if (runForLambda != null && runForLambda.lambdaDebuffs != null && runForLambda.lambdaDebuffs.Count > 0)
                {
                    ctx.lambdaFirstTurnDiceDelta     = GameLoop.Lambda.LambdaDebuffEffects.GetFirstTurnDiceDelta(runForLambda);
                    ctx.lambdaIrritatingInterval     = GameLoop.Lambda.LambdaDebuffEffects.GetIrritatingInterval(runForLambda);
                    ctx.lambdaDamageDealtMult        = GameLoop.Lambda.LambdaDebuffEffects.GetDamageDealtMult(runForLambda);
                    ctx.lambdaCritNumeratorCap       = GameLoop.Lambda.LambdaDebuffEffects.GetCritNumeratorCap(runForLambda);
                    ctx.lambdaMercifulExecThreshold  = GameLoop.Lambda.LambdaDebuffEffects.GetMercifulExecThreshold(runForLambda);
                    ctx.lambdaConsumableLockUntilTurn= GameLoop.Lambda.LambdaDebuffEffects.GetConsumableLockUntilTurn(runForLambda);

                    // 迫りくる死(lv3): 戦闘開始時に HP を 1 にする（割合計算が先・ボス含む全戦闘）
                    if (GameLoop.Lambda.LambdaDebuffEffects.ImpendingDeathActive(runForLambda))
                    {
                        playerHP = 1;
                        ctx.playerCurrentHP = 1;
                        Debug.Log("[CombatManager] Λデバフ 迫りくる死(lv3): 戦闘開始時 HP=1");
                    }
                }
            }

            // 消費アイテム: マップで使用した「次戦闘バフ」を ctx へコピーし RunState 側をクリア
            if (ctx != null)
            {
                var rs = GameLoop.GameManager.Instance?.Run;
                if (rs != null)
                {
                    ctx.consAtkBurst        = rs.pendingConsAtkBurst;
                    ctx.consDiceRoll        = rs.pendingConsDiceRoll;
                    ctx.consShield          = rs.pendingConsShield;
                    ctx.shieldGainedTotal  += rs.pendingConsShield; // 検証計測
                    ctx.consShieldExpireTurn= rs.pendingConsShieldTurns;
                    ctx.consRegen           = rs.pendingConsRegen;
                    ctx.consCrit            = rs.pendingConsCrit;
                    ctx.consFlatReduce      = rs.pendingConsFlatReduce;
                    ctx.consDmgMultPct      = rs.pendingConsDmgMultPct;
                    ctx.consReflect         = rs.pendingConsReflect;
                    ctx.consEnemyDiceDebuff = rs.pendingConsEnemyDiceDebuff;
                    ctx.gamblerArmed        = rs.pendingGamblerDice;
                    if (rs.pendingFirstRollTotal > 0)
                    {
                        // 加速の粉: 初回ロール(turn1)のダイス合計+X。nextTurnBuffs→turn1でcurrentBuffsへ移行し適用。
                        ctx.nextTurnBuffs["diceBonus"] =
                            (ctx.nextTurnBuffs.TryGetValue("diceBonus", out var db) ? db : 0f) + rs.pendingFirstRollTotal;
                    }
                    if (rs.pendingEnemyStartHpCutPct > 0)
                    {
                        int cut = Mathf.CeilToInt(enemy.maxHP * rs.pendingEnemyStartHpCutPct / 100f);
                        enemyHP = Math.Max(1, enemyHP - cut);
                        Debug.Log($"[CombatManager] 奇襲: 敵開始HP-{cut} ({enemyHP}/{enemy.maxHP})");
                    }
                    rs.ClearPendingCombatConsumables();
                }
            }

            // 魔王の威圧 等の戦闘開始時スキル処理
            psm.FireTrigger(PassiveSkillTrigger.OnBattleStart);
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnBattleStart);

            // パッシブ刻印: 戦闘開始時の効果を適用 (HP回復・シールド付与・ダイス補正等)
            InventorySystem.Sigils.PassiveSigilActivator.ApplyOnBattleStart(
                GameLoop.GameManager.Instance?.Run, psm.Context);

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

        /// <summary>戦闘中にエネミーを差し替える（覚者の連戦などで使用）。
        /// プレイヤー状態(HP/ダイス/buffs/currentTurn) は完全保持。
        /// 敵側パッシブと敵固有の accumulatedValues キーは新エネミーのものに置換。</summary>
        /// <param name="newEnemyId">enemies.json の id</param>
        /// <param name="logLabel">遷移ログに使うラベル(例:"覚者第二形態へ")</param>
        /// <returns>差し替え成功なら true</returns>
        public bool SwapEnemy(string newEnemyId, string logLabel = null)
        {
            if (!isCombatActive) { Debug.LogWarning("[CombatManager.SwapEnemy] 戦闘中でない"); return false; }
            var newEnemy = EnemyDatabase.Get(newEnemyId);
            if (newEnemy == null) { Debug.LogError($"[CombatManager.SwapEnemy] enemy not found: {newEnemyId}"); return false; }

            string prevName = currentEnemy?.displayName ?? "?";
            currentEnemy = newEnemy;
            enemyHP = newEnemy.maxHP;
            enemyCriticalNumerator = newEnemy.criticalNumerator;

            var psm = PassiveSkillManager.Instance;
            if (psm == null) { Debug.LogError("[CombatManager.SwapEnemy] PSM null"); return false; }

            // 敵側パッシブを差し替え（プレイヤー側パッシブは保持）。
            // ボス連戦(覚者等)でも狂暴化を維持する。
            var swapSkills = newEnemy.passiveSkills != null
                ? new List<EnemyPassiveEntry>(newEnemy.passiveSkills)
                : new List<EnemyPassiveEntry>();
            var nodeForSwapBerserk = MapSystem.MapManager.Instance?.CurrentNode;
            if (nodeForSwapBerserk != null && nodeForSwapBerserk.type == MapSystem.TileType.Boss
                && !swapSkills.Exists(e => e != null && e.internalName == "Berserk"))
            {
                swapSkills.Add(new EnemyPassiveEntry
                {
                    internalName = "Berserk", skillName = "狂暴化",
                    description = "50ターン経過後、ダイス合計+10・回復封印・被ダメージ3倍",
                });
            }
            psm.RegisterEnemySkills(swapSkills);

            // 敵側固有 accumulatedValues キーを掃除（プレイヤー側補正は保持）
            // 既知の "enemy-side" prefix を持つキーを除去
            var ctx = psm.Context;
            if (ctx != null)
            {
                var toRemove = new List<string>();
                foreach (var k in ctx.accumulatedValues.Keys)
                {
                    if (k.StartsWith("sg_") || k.StartsWith("awakened_") || k.StartsWith("ashen_")
                        || k.StartsWith("boss6_") || k.StartsWith("starfire_") || k.StartsWith("judg")
                        || k.StartsWith("myokaku_") || k.StartsWith("ember_") || k.StartsWith("berserk_")
                        || k == "extraDice" || k == "enemyMaxHPReduction")
                        toRemove.Add(k);
                }
                foreach (var k in toRemove) ctx.accumulatedValues.Remove(k);

                // ctx 敵パラメータも更新（プレイヤー視点で書く: enemy = ボス、player = プレイヤー）
                ctx.enemyMaxHP = newEnemy.maxHP;
                ctx.enemyCurrentHP = newEnemy.maxHP;
                ctx.enemyThreat = newEnemy.threat;
                ctx.enemyDiceMax = newEnemy.diceMaxValue;
                ctx.enemyDiceTotalBonus = 0;
                ctx.bossDiceBonus = 0; // swapで一旦クリア。新形態の OnBattleStart パッシブ(刹那等)が再設定する
                ctx.enemyDamageReductionPct = newEnemy.baseDefenseRate; // 新形態の基礎防御%を反映
                ctx.ashenSuddenDeath = false;
                ctx.myokakuSuddenDeath = false;
                ctx.gedatsuPending = false;
            }

            // 新エネミーの OnBattleStart 発火
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnBattleStart);

            // LED アクティブダイス数も更新
            if (ledManager != null)
                ledManager.SetActiveDiceCount(playerDiceCount, newEnemy.diceCount);

            string label = string.IsNullOrEmpty(logLabel) ? newEnemy.displayName : logLabel;
            Debug.Log($"[CombatManager] ━━ エネミー差し替え: {prevName} → {label} (HP:{enemyHP}/{newEnemy.maxHP}, Dice:{newEnemy.DiceNotation}, Crit:{enemyCriticalNumerator}/9, Threat:{newEnemy.threat})");

            // 計測フック: 新フォームを「新エネミーとの遭遇」として通知
            // (AutoRunner 等がチェーン途中フォームのスタッツを取れるようにする)
            GameLoop.GameManager.Instance?.RaiseEnemyEncountered(newEnemy);
            return true;
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

            // 偽の商人: 規定ターン数を終えても未決着なら、敵が逃走して戦闘終了。
            // 両者生存のまま終わる（playerWon=false かつ playerHP>0 ＝ GameManager 側で「逃走」と判定）。
            if (fleeAfterTurns > 0 && ctx != null && ctx.currentTurn >= fleeAfterTurns
                && playerHP > 0 && enemyHP > 0)
            {
                Debug.Log($"[CombatManager] 偽の商人: {fleeAfterTurns}ターン経過 → 逃走（戦闘強制終了）");
                FinishCombat();
                return new TurnResult
                {
                    turnNumber = ctx.currentTurn,
                    playerWon = false,
                    isDraw = false,
                    playerHPAfter = playerHP,
                    enemyHPAfter = enemyHP,
                };
            }

            // --- ターン開始 ---

            psm.BeginTurn();

            // 安全弁: 戦闘が異常に長引いた場合は強制決着（プレイヤー敗北）。
            // 既知の挙動: 灰燼の王戦などで「ボスにダメージ通らない × プレイヤー再生で死なない」
            // の二重デッドロックが起きると、Boss6Ashen の発動条件(ボスHP→0)が満たされず
            // SeveredTime の累積ダイスボーナスだけが膨らんで AutoRunner で 75万ターン超に達した。
            // 戦闘単体での暴走は許容しない。300T を超えたらプレイヤーHPを0にして即座に終了。
            const int kHardTurnCap = 300;
            if (ctx != null && ctx.currentTurn > kHardTurnCap)
            {
                Debug.LogWarning($"[CombatManager] 戦闘ターン上限超過 ({ctx.currentTurn} > {kHardTurnCap}) — 強制決着（プレイヤー敗北）");
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                FinishCombat();
                return new TurnResult
                {
                    turnNumber = ctx.currentTurn,
                    playerWon = false,
                    isDraw = false,
                    playerHPAfter = 0,
                    enemyHPAfter = enemyHP,
                };
            }

            // 消費アイテム持続効果の毎ターン再適用（BeginNewTurn でリセットされるため）
            if (ctx != null)
            {
                if (ctx.consCrit > 0) ctx.criticalBonus += ctx.consCrit;
                if (ctx.consFlatReduce > 0) ctx.playerFlatDamageReduction += ctx.consFlatReduce;
            }

            psm.FireEnemyTrigger(PassiveSkillTrigger.OnTurnStart);

            // パッシブ刻印: T2 開始時に静寂の刻印(T1限定 -2軽減)を剥がす
            InventorySystem.Sigils.PassiveSigilActivator.ApplyOnTurnStart(ctx);

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

            // 〈灰燼の烙印〉サドンデス: 両者を 1d6 に強制（決着まで継続）
            if (ctx.ashenSuddenDeath)
            {
                actualPlayerDiceCount = 1;
                actualEnemyDiceCount = 1;
                playerDice = new[] { UnityEngine.Random.Range(1, 7) };
                enemyDice = new[] { UnityEngine.Random.Range(1, 7) };
                Debug.Log("[CombatManager] 灰燼の烙印サドンデス: 両者1d6強制");
            }

            // 〈妙覚〉サドンデス: 両者を 1d6 に強制（決着まで継続）。勝者が敗者を即死させる。
            // myokakuSuddenDeath はサドンデスが「ターン開始時点で既にアクティブ」=実際に1d2を振るターン
            // のみ true。開始ターン（99を6ターン耐えた直後）の敗北で即死させないための判別フラグ。
            bool myokakuSDRollThisTurn = ctx.myokakuSuddenDeath;
            if (ctx.myokakuSuddenDeath)
            {
                actualPlayerDiceCount = 1;
                actualEnemyDiceCount = 1;
                playerDice = new[] { UnityEngine.Random.Range(1, 3) }; // 1d2
                enemyDice = new[] { UnityEngine.Random.Range(1, 3) };
                Debug.Log("[CombatManager] 妙覚サドンデス: 両者1d2強制");
            }

            // シュヴァリエのレイピア コントラタック発動中: プレイヤーは 1d1 強制（必ず1を出す）
            if (ctx.GetAccumulated("player_contre") > 0)
            {
                actualPlayerDiceCount = 1;
                playerDice = new[] { 1 };
                Debug.Log("[CombatManager] コントラタック: プレイヤー 1d1 強制");
            }

            // シュヴァリエのレイピア 解除後の解放ターン: ダイス個数+1（クリ補正は currentBuffs 経由）
            if (ctx.GetAccumulated("rapier_release_pending") > 0)
            {
                actualPlayerDiceCount += 1;
                // 1個追加分の出目を生成して配列を拡張
                var extra = ctx.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0
                    ? ctx.equippedDiceFaces[UnityEngine.Random.Range(0, ctx.equippedDiceFaces.Length)]
                    : UnityEngine.Random.Range(1, playerDiceMax + 1);
                var newArr = new int[playerDice.Length + 1];
                for (int i = 0; i < playerDice.Length; i++) newArr[i] = playerDice[i];
                newArr[newArr.Length - 1] = extra;
                playerDice = newArr;
                ctx.accumulatedValues["rapier_release_pending"] = 0;
                Debug.Log($"[CombatManager] レイピア解放: ダイス+1 (追加出目={extra})、会心+9 はバフ経由");
            }

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
                && !ctx.rollPurity
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
            if (!ctx.rollPurity && wrathDiceOverrideArmed && ctx.currentTurn == 1)
            {
                for (int i = 0; i < playerDice.Length; i++) playerDice[i] = playerDiceMax;
                wrathDiceOverrideArmed = false;
                Debug.Log($"[CombatManager] {MetaProgression.PermanentDebuffIds.Wrath}: 1T目ダイス全て最大値");
            }

            // メタバフ: ダイス合計値補正（一番低いダイスから +1 を順次振り分け、各ダイスは playerDiceMax 上限）
            int metaDiceBonus = ctx.rollPurity ? 0 : MetaProgression.MetaBuffApplicator.GetDiceTotalBonus();
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

            // 〈サドンデス〉純粋ダイス判定: 業物/段階補正/ダイス補正/ボス威風/メタ等を一切排し、
            // ここで両者を新規に振り直して勝敗を決める（真の50/50）。妙覚=1d2 / 灰燼=1d6。
            if (ctx.myokakuSuddenDeath || ctx.ashenSuddenDeath)
            {
                int faces = ctx.myokakuSuddenDeath ? 2 : 6;
                int pd = UnityEngine.Random.Range(1, faces + 1);
                int ed = UnityEngine.Random.Range(1, faces + 1);
                if (playerDice != null && playerDice.Length > 0) playerDice[0] = pd;
                if (enemyDice != null && enemyDice.Length > 0) enemyDice[0] = ed;
                ctx.playerDiceTotal = pd;
                ctx.enemyDiceTotal = ed;
                // diceDifference は両合計から算出される読み取り専用プロパティ（=pd-ed）
                ctx.playerWonRoll = pd > ed;
                ctx.playerLostRoll = pd < ed;
            }

            // メタバフ〈神の加護〉: 1戦闘1回、ロール敗北を引分に変換する
            // (敵 OnPostRoll より前に行うことで、敵の OnRollWin 反応も発火しないようにする)
            // ※サドンデス中は無効（解脱の50/50を歪めないため）。
            if (ctx.playerLostRoll
                && !ctx.myokakuSuddenDeath && !ctx.ashenSuddenDeath
                && MetaProgression.MetaBuffApplicator.IsDivineProtectUnlocked()
                && ctx.GetAccumulated("divineProtect_used") == 0)
            {
                ctx.playerLostRoll = false;
                // playerLostRoll=false かつ playerWonRoll=false で引分扱い。
                // diceDifference は読み取り専用プロパティ化したため 0 強制は不可だが、
                // 引分分岐は diceDifference を参照しないので機能に影響なし。
                ctx.accumulatedValues["divineProtect_used"] = 1;
                Debug.Log("[MetaBuff] 神の加護: ロール敗北を引分に変換 (この戦闘の使用権を消費)");
            }

            // パッシブ刻印: per-roll 効果 (粘り・腐食) を fixedDamageToEnemy に積む
            if (ctx.playerWonRoll)
                InventorySystem.Sigils.PassiveSigilActivator.ApplyOnRollWin(ctx);

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
                // 〈灰燼の烙印〉サドンデスの一撃必殺はターン終端で敗者HPを直接0にする
                // （AshArmor/ImmortalEmber/シールド/LSを全てバイパスするためここでは加算しない）

                // (4) 追撃ダメージ（パッシブ由来: ctx.pursuitDamage はスキル発火時にセット済み）
                int pursuitDmg = ctx.pursuitDamage;

                if (result.playerWon)
                {
                    // プレイヤーが勝利 → 敵にダメージ
                    var (totalDmg, fixedDmg, isCrit) = psm.ProcessDamage(
                        mainDmg, pursuitDmg, playerCriticalNumerator);

                    // 与ダメージ修飾チェーン（順序厳守。詳細は ApplyWinDamageModifiers 参照）
                    int lbStage = GameLoop.GameManager.Instance?.Run?.limitBreakStage ?? 0;
                    totalDmg = ApplyWinDamageModifiers(totalDmg, ref fixedDmg, ref isCrit, lbStage, psm, ctx);

                    result.mainDamage = mainDmg;
                    result.pursuitDamage = pursuitDmg;
                    result.totalDamage = totalDmg;
                    result.fixedDamage = fixedDmg;
                    result.isCritical = isCrit;

                    // 敵にダメージ適用（メイン＋プレイヤー→敵固定）
                    enemyHP = Math.Max(0, enemyHP - totalDmg - fixedDmg);

                    // メタデバフ Lv9 鋼の皮膚: 敵の初回致命傷を1HPで耐える
                    if (enemyHP == 0
                        && !metaLethalSurviveUsed
                        && MetaProgression.MetaDebuffApplicator.EnemySurvivesFirstLethal())
                    {
                        enemyHP = 1;
                        metaLethalSurviveUsed = true;
                        Debug.Log("[CombatManager] メタデバフ Lv9 鋼の皮膚: 敵が初回致命傷で1HPに踏みとどまった");
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

                    // 貪欲のダイス/吸血: 与えたダメージの一定割合を回復（HealPlayer が負傷/回復封印を適用）
                    if (ctx.lifestealPct > 0f && totalDmg > 0)
                    {
                        int ls = Mathf.CeilToInt(totalDmg * ctx.lifestealPct);
                        if (ls > 0) HealPlayer(ls);
                    }

                    // シールドバッシュ: 与ダメの一定割合をシールド化（天衣無縫 healShieldReduction を適用）
                    if (ctx.shieldOnWinPct > 0f && totalDmg > 0)
                    {
                        int sh = Mathf.CeilToInt(totalDmg * ctx.shieldOnWinPct) - ctx.healShieldReduction;
                        if (sh > 0)
                        {
                            ctx.consShield += sh;
                            ctx.shieldGainedTotal += sh;
                        }
                    }

                    // === Scratch計算 ===
                    // 脅威システム: ロール勝利でも勝ち幅が脅威に満たない分を削りダメージとして受ける
                    //   scratch += max(0, 脅威 − 勝ち幅)。 大差勝ち(diff≥脅威)なら0。
                    if (ctx.enemyThreat > 0)
                        ctx.scratchDamage += Math.Max(0, ctx.enemyThreat - diceDiff);
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

                    // 脅威システム: ロール敗北時の被ダメは脅威を下回らない。
                    //   被ダメ基礎 = max(ダイス差, 脅威)。 大差負け(diff>脅威)なら差分がそのまま通る。
                    int lossBase = Math.Max(mainDmg, ctx.enemyThreat);
                    var (totalDmg, fixedDmg, isCrit) = psm.ProcessDamage(
                        lossBase, 0, enemyCriticalNumerator);

                    result.mainDamage = lossBase;
                    result.pursuitDamage = 0;
                    result.totalDamage = totalDmg;
                    result.fixedDamage = fixedDmg;
                    result.isCritical = isCrit;

                    // 被ダメージ修飾チェーン（順序厳守。詳細は ApplyLossDamageModifiers 参照）
                    totalDmg = ApplyLossDamageModifiers(totalDmg, floorMod, ctx);

                    // [DBG] 妙覚99フェーズの被ダメ消失箇所特定（procOut=ProcessDamage後/applied=修飾後）。特定後に削除。
                    if (currentEnemy != null && currentEnemy.id == "boss_layer7_p7" && !myokakuSDRollThisTurn)
                        Debug.Log($"[DBG] 妙覚LOSS lossBase={lossBase} procOut={result.totalDamage} applied={totalDmg} fixToPlayer={ctx.fixedDamageToPlayer} consShield={ctx.consShield} negate={ctx.playerDamageNegateCharges} HP={playerHP}/{playerMaxHP}");

                    // プレイヤーにダメージ適用（メイン＋敵→プレイヤー固定）
                    playerHP = Math.Max(0, playerHP - totalDmg);
                    ctx.playerDamageThisTurn += totalDmg; // 焦土用の被ダメ計測
                    if (ctx.fixedDamageToPlayer > 0)
                    {
                        playerHP = Math.Max(0, playerHP - ctx.fixedDamageToPlayer);
                        ctx.playerDamageThisTurn += ctx.fixedDamageToPlayer;
                    }

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

                // 引き分け時も固定ダメージは双方に適用（蒼白の槍騎士: 軽減無視ダメ増幅を停戦協定にも乗せる）
                if (ctx.fixedDamageToEnemy > 0)
                {
                    int drawFixed = ctx.fixedDamageMultiplier > 1f
                        ? Mathf.CeilToInt(ctx.fixedDamageToEnemy * ctx.fixedDamageMultiplier)
                        : ctx.fixedDamageToEnemy;
                    enemyHP = Math.Max(0, enemyHP - drawFixed);
                }
                if (ctx.fixedDamageToPlayer > 0)
                    playerHP = Math.Max(0, playerHP - ctx.fixedDamageToPlayer);

                // 出血ダメージ（引き分け時も適用）。停戦協定ターンは他効果を抑止するためスキップ。
                if (!ctx.truceThisTurn && ctx.enemyBleedStacks > 0)
                {
                    int bleedDmg = BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks);
                    enemyHP = Math.Max(0, enemyHP - bleedDmg);
                }
            }

            // 死の宣告チェック（敵スキル由来の即死ダメージ）
            if (ctx.fixedDamageToPlayer >= 999)
                playerHP = 0;

            // Λデバフ「慈悲の処刑」: 被弾後、HPが最大の5/10/15%以下なら即死。
            // combatLethalThisTurn 確定の前に処理し、ターン終了回復での蘇生を防ぐ。
            if (ctx.lambdaMercifulExecThreshold > 0f && playerHP > 0)
            {
                bool tookHit = ctx.playerDamageThisTurn > 0
                             || (ctx.scratchDamage > 0 && !ctx.nullifyScratchDamage)
                             || ctx.fixedDamageToPlayer > 0;
                if (tookHit && playerHP <= playerMaxHP * ctx.lambdaMercifulExecThreshold)
                {
                    Debug.Log($"[Λ] 慈悲の処刑: HP{playerHP}/{playerMaxHP} ≤ {ctx.lambdaMercifulExecThreshold:P0} → 即死");
                    playerHP = 0;
                }
            }

            // コンテキストにHP同期
            ctx.playerCurrentHP = playerHP;
            ctx.playerMaxHP = playerMaxHP;
            ctx.enemyCurrentHP = enemyHP;
            ctx.enemyMaxHP = currentEnemy.maxHP;

            // 戦闘ダメージ（メイン/固定/scratch/死の宣告）でこのターンに致死へ至ったか。
            // これ以降のターン終了回復(活力/継続回復/剣鎧等)で蘇生させないための確定フラグ。
            // 天命/深淵は被ダメを上限化してHPを1〜2残すため（HP=0にならず）ここでは false。
            bool combatLethalThisTurn = playerHP <= 0;

            // ターン終了トリガー
            psm.FireTrigger(PassiveSkillTrigger.OnTurnEnd);
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnTurnEnd);

            // 覚者連戦: 敵パッシブが予約した SwapEnemy を perspective 復帰後に実行
            if (!string.IsNullOrEmpty(ctx.pendingEnemySwapId))
            {
                string swapId = ctx.pendingEnemySwapId;
                string swapLabel = ctx.pendingEnemySwapLabel;
                ctx.pendingEnemySwapId = null;
                ctx.pendingEnemySwapLabel = null;
                SwapEnemy(swapId, swapLabel);
                // 敵HP は SwapEnemy で新形態の MaxHP に再設定済み。
                // 戦闘継続のため enemyHP <= 0 判定を回避する目的で SyncHP しなおす
                SyncHPFromContext(ctx);
            }

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

            // 消費: 継続回復（毎ターン終了時 +consRegen → consRegen--）。狂暴化中は封印。
            if (ctx.consRegen > 0 && playerHP > 0 && !ctx.healBlocked)
            {
                int regen = Math.Max(0, ctx.consRegen - ctx.healShieldReduction); // 天衣無縫減衰
                int heal = Math.Min(playerMaxHP - playerHP, regen);
                if (heal > 0) { playerHP += heal; ctx.playerCurrentHP = playerHP; ctx.healAppliedTotal += heal; }
                Debug.Log($"[CombatManager] 継続回復 +{heal} (次T {ctx.consRegen - 1})");
                ctx.consRegen--;
            }
            // 消費: シールド残ターン減算（-1=無制限 / 0で失効）
            if (ctx.consShieldExpireTurn > 0)
            {
                ctx.consShieldExpireTurn--;
                if (ctx.consShieldExpireTurn == 0)
                {
                    ctx.consShield = 0;
                    Debug.Log("[CombatManager] シールド失効");
                }
            }

            // 〈灰燼の烙印〉サドンデス: 決着ターンはロール勝者が敗者を即死させる
            // （AshArmor/ImmortalEmber/シールド/LS 等を全てバイパス。引き分けは継続）
            // 踏みとどまったその同ターンでは決着させない（記録ターン超のみ）
            if (ctx.ashenSuddenDeath && !result.isDraw
                && ctx.currentTurn > (int)ctx.GetAccumulated("ashen_endured_turn"))
            {
                if (result.playerWon) { enemyHP = 0; ctx.enemyCurrentHP = 0; }
                else { playerHP = 0; ctx.playerCurrentHP = 0; }
                Debug.Log($"[CombatManager] 灰燼の烙印サドンデス決着: {(result.playerWon ? "ボス" : "プレイヤー")} 即死");
            }

            // 〈妙覚〉サドンデス: 1d6 vs 1d6 で勝者が敗者を即死させる（引き分けは継続）。
            // プレイヤー勝利は AwakenedP7Myokaku.OnTurnEnd が gedatsuPending をセット済み。
            // ここではボス勝利時のプレイヤー即死だけを保証する（シールド/LS バイパス）。
            if (myokakuSDRollThisTurn && !result.isDraw && !result.playerWon)
            {
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                Debug.Log("[CombatManager] 妙覚サドンデス決着: プレイヤー即死");
            }

            result.playerHPAfter = playerHP;
            result.enemyHPAfter = enemyHP;
            turnLog.Add(result);

            // [DBG] 妙覚戦のターン終端トレース（HP復帰・SD・解脱の経路特定用。確定後に削除）
            if (currentEnemy != null && currentEnemy.id == "boss_layer7_p7")
            {
                string wl = result.isDraw ? "分" : (result.playerWon ? "勝" : "敗");
                Debug.Log($"[DBG] 妙覚EOT T={ctx.currentTurn} 勝敗={wl} P計={ctx.playerDiceTotal} E計={ctx.enemyDiceTotal} " +
                          $"Eボーナス={ctx.enemyDiceTotalBonus} playerHP={playerHP} enemyHP={enemyHP} " +
                          $"SD={ctx.myokakuSuddenDeath} SDroll={myokakuSDRollThisTurn} gedatsu={ctx.gedatsuPending}");
            }

            OnTurnEnd?.Invoke(result);

            // ログ出力
            LogTurnResult(result);

            // 戦闘ダメージで致死に至っていたら、ターン終了回復で蘇生していても死亡を確定させる
            // （オーバーキル消失バグの修正：致死ダメージは回復で帳消しにできない）。
            if (combatLethalThisTurn && playerHP > 0)
            {
                Debug.Log($"[CombatManager] 戦闘致死確定: ターン終了回復による蘇生を無効化 (HP {playerHP}→0)");
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                result.playerHPAfter = 0;
            }

            // 戦闘終了チェック
            if (playerHP <= 0 || enemyHP <= 0)
            {
                FinishCombat();
            }

            return result;
        }

        // ===========================================================
        //  ダメージ修飾チェーン（ExecuteTurn から分離。順序が結果を左右するため厳守）
        // ===========================================================

        /// <summary>
        /// プレイヤー勝利時の与ダメージ修飾（ProcessDamage後〜敵HP適用前）。適用順:
        /// 画竜点睛 → 与ダメ倍率 → 業物 → 鬼火 → 攻撃バースト → メタ会心 → 向かい風
        /// → 敵被ダメ軽減(灰塵等の OnPreReceiveDamage) → 基礎防御%(利刃で相殺) → 勝利時最低保証
        /// → 狂暴化 → メタ俊敏回避。
        /// </summary>
        private int ApplyWinDamageModifiers(int totalDmg, ref int fixedDmg, ref bool isCrit,
                                            int lbStage, PassiveSkillManager psm, CombatContext ctx)
        {
            // 画竜点睛: ダメージ＝(出目+10)×会心倍率、会心確定
            if (ctx.garyoProc)
            {
                totalDmg = Mathf.CeilToInt((ctx.garyoDieValue + 10) * ctx.criticalMultiplier);
                isCrit = true;
                Debug.Log($"[画竜点睛] 確定会心 {totalDmg} ダメ (出目{ctx.garyoDieValue}+10 ×{ctx.criticalMultiplier})");
            }

            // 業物: 与ダメ倍率 +20%/lv（outgoingDamageMultiplier に加算）
            if (lbStage > 0)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += 0.2f * lbStage;
            }

            // 与ダメ倍率（激情の刃 等のパッシブ由来 + 業物）
            if (ctx.outgoingDamageMultiplier > 0f
                && Mathf.Abs(ctx.outgoingDamageMultiplier - 1f) > 0.001f)
            {
                int orig = totalDmg;
                totalDmg = Mathf.CeilToInt(totalDmg * ctx.outgoingDamageMultiplier);
                Debug.Log($"[CombatManager] 与ダメ補正 ×{ctx.outgoingDamageMultiplier:F2}: {orig}→{totalDmg}");
            }

            // 消費: 鬼火の油 与ダメ+X%（全戦闘）
            if (ctx.consDmgMultPct > 0 && totalDmg > 0)
                totalDmg += Mathf.CeilToInt(totalDmg * ctx.consDmgMultPct / 100f);

            // 消費: 攻撃力バースト（この勝利ターンのみ・単発消費）
            if (ctx.consAtkBurst > 0)
            {
                totalDmg += ctx.consAtkBurst;
                Debug.Log($"[CombatManager] 攻撃バースト +{ctx.consAtkBurst}");
                ctx.consAtkBurst = 0;
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

            // 敵側のダメージ軽減パッシブを発火（前後で ctx.finalDamage と totalDmg を同期し、
            // 敵パッシブ（AshArmor 等やシュヴァリエのシールド経由処理）が
            // 実際の与ダメに反映されるようにする）
            ctx.finalDamage = totalDmg;
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnPreReceiveDamage);
            totalDmg = System.Math.Max(0, ctx.finalDamage);

            // 基礎防御%軽減（灰塵の鎧の後）。利刃で軽減率(pt)を相殺。倍率が全部乗った最終値に対して%カット。
            if (ctx.enemyDamageReductionPct > 0f && totalDmg > 0)
            {
                float effRate = Mathf.Max(0f, ctx.enemyDamageReductionPct - ctx.armorPenPct);
                if (effRate > 0f)
                {
                    int beforeDef = totalDmg;
                    totalDmg = Mathf.CeilToInt(totalDmg * (1f - effRate));
                    Debug.Log($"[基礎防御] 軽減{effRate:P0}（防{ctx.enemyDamageReductionPct:P0}/利刃{ctx.armorPenPct:P0}）{beforeDef}→{totalDmg}");
                }
            }

            // 利刃: 敵基礎防御を超えた貫通分(armorPen − 防御率)は腐らせず与ダメ%へ転用。
            // → 無装甲の敵にも armorPen 分の与ダメ増として常時機能する（対タンク以外でも腐らない）。
            float wastedPen = Mathf.Max(0f, ctx.armorPenPct - ctx.enemyDamageReductionPct);
            if (wastedPen > 0f && totalDmg > 0)
            {
                int beforePen = totalDmg;
                totalDmg = Mathf.CeilToInt(totalDmg * (1f + wastedPen));
                Debug.Log($"[利刃] 余剰貫通 +{wastedPen:P0} {beforePen}→{totalDmg}");
            }

            // Λデバフ「微妙な手応え」: 敵への最終ダメージを -5/-10/-15%（基礎防御の後、最低保証の前）
            if (ctx.lambdaDamageDealtMult < 1f && totalDmg > 0)
            {
                int beforeVague = totalDmg;
                totalDmg = Mathf.CeilToInt(totalDmg * ctx.lambdaDamageDealtMult);
                Debug.Log($"[Λ] 微妙な手応え ×{ctx.lambdaDamageDealtMult:F2} {beforeVague}→{totalDmg}");
            }

            // 勝利時の与ダメ最低保証（基本1、利刃Lvで1/2/3/4）。「勝ったのに0」を防止。
            if (totalDmg < ctx.winMinDamage)
                totalDmg = ctx.winMinDamage;

            // 狂暴化(ボス50T後): エネミーが受けるダメージを倍化（軽減処理の後・適用直前）
            if (ctx.enemyDamageTakenMultiplier > 1f && totalDmg > 0)
            {
                int orig = totalDmg;
                totalDmg = Mathf.CeilToInt(totalDmg * ctx.enemyDamageTakenMultiplier);
                Debug.Log($"[CombatManager] 狂暴化: 敵被ダメ ×{ctx.enemyDamageTakenMultiplier:F1} ({orig}→{totalDmg})");
            }

            // メタデバフ Lv2 俊敏: 各戦闘の最初の1回の被弾を必ず回避（メイン＋固定ダメを無効化）
            if (!metaAgilityDodgeUsed
                && (totalDmg + fixedDmg) > 0
                && MetaProgression.MetaDebuffApplicator.EnemyDodgesFirstHit())
            {
                metaAgilityDodgeUsed = true;
                Debug.Log($"[CombatManager] メタデバフ Lv2 俊敏: 初撃を回避 (与ダメ {totalDmg}+{fixedDmg} を無効化)");
                totalDmg = 0;
                fixedDmg = 0;
            }

            return totalDmg;
        }

        /// <summary>
        /// プレイヤー敗北時の被ダメージ修飾（ProcessDamage後〜プレイヤーHP適用前）。適用順:
        /// 天変地異(×2) → メタ被ダメ軽減 → 不屈の鎧/苦難の刻印 → 地獄門 → 亡者の招待(+30%)
        /// → 共助(T1半減) → 獣の絆 → コントラタック(50%軽減+反射) → 消費シールド → 鏡写し反射。
        /// enemyHP はメンバフィールドのため反射系はここで直接削る。
        /// </summary>
        private int ApplyLossDamageModifiers(int totalDmg, MapSystem.FloorModifier floorMod, CombatContext ctx)
        {
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

            // シュヴァリエのレイピア コントラタック: 被ダメ50%軽減 + 軽減量×2 を敵へ反射
            if (ctx.GetAccumulated("player_contre") > 0 && totalDmg > 0)
            {
                int mitigated = totalDmg / 2;
                totalDmg -= mitigated;
                int reflect = mitigated * 2;
                if (reflect > 0)
                {
                    enemyHP = Math.Max(0, enemyHP - reflect);
                    Debug.Log($"[コントラタック] 軽減{mitigated} → 被ダメ{totalDmg + mitigated}→{totalDmg}、反射{reflect} → 敵HP{enemyHP}");
                }
            }

            // 消費: シールド吸収（残量から差し引き）
            if (ctx.consShield > 0 && totalDmg > 0)
            {
                int absorbed = Math.Min(ctx.consShield, totalDmg);
                ctx.consShield -= absorbed;
                totalDmg -= absorbed;
                Debug.Log($"[CombatManager] シールド吸収 {absorbed} (残{ctx.consShield})");
            }

            // 消費: 鏡写しの水晶（吸収後の実被ダメと同量を敵へ反射）
            if (ctx.consReflect && totalDmg > 0)
            {
                enemyHP = Math.Max(0, enemyHP - totalDmg);
                Debug.Log($"[CombatManager] 鏡写し反射 {totalDmg} → 敵HP {enemyHP}");
            }

            return totalDmg;
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
            var ctx = PassiveSkillManager.Instance?.Context;
            return new CombatResult
            {
                enemyId = currentEnemy.id,
                enemyDisplayName = currentEnemy.displayName,
                playerWon = playerHP > 0 && enemyHP <= 0,
                totalTurns = turnLog.Count,
                playerHPRemaining = playerHP,
                enemyHPRemaining = enemyHP,
                turnLog = new List<TurnResult>(turnLog),
                gedatsu = ctx != null && ctx.gedatsuPending,
                healApplied = ctx?.healAppliedTotal ?? 0,
                shieldGained = ctx?.shieldGainedTotal ?? 0,
                damageDealt = Math.Max(0, (currentEnemy != null ? currentEnemy.maxHP : 0) - Math.Max(0, enemyHP)),
                damageTaken = Math.Max(0, playerMaxHP - Math.Max(0, playerHP)) + (ctx?.healAppliedTotal ?? 0),
                enemyMaxHP = currentEnemy != null ? currentEnemy.maxHP : 0,
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

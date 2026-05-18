using System.Collections.Generic;
using UnityEngine;

namespace InventorySystem.PassiveSkills
{
    /// <summary>
    /// パッシブスキルの発動管理マネージャー
    /// 
    /// 役割:
    /// - 装備中アイテムのパッシブスキルを収集
    /// - トリガー発火時に該当スキルを一括実行
    /// - CombatContext の生成・管理
    /// 
    /// 使い方（戦闘システム側）:
    /// <code>
    /// // 戦闘開始時
    /// var manager = PassiveSkillManager.Instance;
    /// manager.BeginCombat(playerMaxHP);
    /// manager.FireTrigger(PassiveSkillTrigger.OnBattleStart);
    /// 
    /// // 各ターン
    /// manager.FireTrigger(PassiveSkillTrigger.OnTurnStart);
    /// // ...ダイスロール...
    /// manager.Context.playerDice = rolledDice;
    /// manager.FireTrigger(PassiveSkillTrigger.OnPostRoll);
    /// // ...ダメージ計算...
    /// manager.FireTrigger(PassiveSkillTrigger.OnPreDealDamage);
    /// </code>
    /// </summary>
    public class PassiveSkillManager : MonoBehaviour
    {
        // ===== シングルトン =====
        private static PassiveSkillManager instance;
        private static bool isApplicationQuitting;
        public static PassiveSkillManager Instance
        {
            get
            {
                if (isApplicationQuitting)
                    return null;

                if (instance == null)
                {
                    var go = new GameObject("[PassiveSkillManager]");
                    instance = go.AddComponent<PassiveSkillManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        // ===== 戦闘コンテキスト =====
        private CombatContext context;
        public CombatContext Context => context;

        // ===== アクティブなスキル一覧 =====
        /// <summary>
        /// 現在有効なスキル群（トリガーごとにグループ化）
        /// O(1)でトリガーに該当するスキルだけを取得できる
        /// </summary>
        private Dictionary<PassiveSkillTrigger, List<IPassiveSkillEffect>> activeSkillsByTrigger 
            = new Dictionary<PassiveSkillTrigger, List<IPassiveSkillEffect>>();

        /// <summary>アクティブなスキル名一覧（デバッグ・UI用）</summary>
        private List<string> activeSkillNames = new List<string>();

        /// <summary>internalName → 日本語表示名のマッピング</summary>
        private Dictionary<string, string> skillDisplayNames = new Dictionary<string, string>();

        // ===== 敵スキル管理 =====
        private Dictionary<PassiveSkillTrigger, List<IPassiveSkillEffect>> enemySkillsByTrigger
            = new Dictionary<PassiveSkillTrigger, List<IPassiveSkillEffect>>();
        private List<string> enemySkillNames = new List<string>();

        // ===== スキル発動追跡（今ターン中に実際にExecuteされたスキル） =====
        private HashSet<string> triggeredPlayerSkills = new HashSet<string>();
        private HashSet<string> triggeredEnemySkills = new HashSet<string>();

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
        //  装備スキルの収集
        // ===========================================================

        /// <summary>
        /// 装備中の全アイテムからパッシブスキルを収集・登録
        /// 装備変更時に呼び出す
        /// </summary>
        public void RefreshActiveSkills(List<CompleteItemData> equippedItems)
        {
            ClearActiveSkills();

            if (equippedItems == null) return;

            // 完全同名アイテム (internalName 一致) は1個扱いで重複排除する。
            // 別アイテム間で同名スキルがある場合は RegisterSkill 側で重複登録を許可して合算動作させる。
            var seenItemIds = new HashSet<string>();
            foreach (var item in equippedItems)
            {
                if (item == null) continue;
                if (!string.IsNullOrEmpty(item.internalName) && !seenItemIds.Add(item.internalName)) continue;
                if (item.passiveSkills == null) continue;
                foreach (var ps in item.passiveSkills)
                {
                    RegisterSkill(ps.internalName);
                    if (!string.IsNullOrEmpty(ps.skillName))
                        skillDisplayNames[ps.internalName] = ps.skillName;
                }
            }

            Debug.Log($"[PassiveSkillManager] Active skills refreshed: {activeSkillNames.Count} skills");
        }

        /// <summary>
        /// 単一アイテムのスキルを追加登録
        /// </summary>
        public void AddItemSkills(CompleteItemData item)
        {
            if (item?.passiveSkills == null) return;
            foreach (var ps in item.passiveSkills)
            {
                RegisterSkill(ps.internalName);
                if (!string.IsNullOrEmpty(ps.skillName))
                    skillDisplayNames[ps.internalName] = ps.skillName;
            }
        }

        /// <summary>
        /// 単一アイテムのスキルを解除
        /// </summary>
        public void RemoveItemSkills(CompleteItemData item)
        {
            if (item?.passiveSkills == null) return;
            foreach (var ps in item.passiveSkills)
            {
                UnregisterSkill(ps.internalName);
            }
        }

        private void RegisterSkill(string skillName)
        {
            var effect = PassiveSkillRegistry.Get(skillName);
            if (effect == null)
            {
                // 未実装スキルはスキップ（将来追加予定）
                Debug.LogWarning($"[PassiveSkillManager] Unregistered skill: {skillName}");
                return;
            }

            // 同名スキルでも別アイテム由来であれば重複登録を許可 → 合算動作。
            // アイテム単位の重複排除は RefreshActiveSkills 側で行う。
            activeSkillNames.Add(skillName);

            // トリガーごとに分類して格納（同じ effect インスタンスを複数回追加することで N 回発火）
            foreach (var trigger in effect.Triggers)
            {
                if (!activeSkillsByTrigger.ContainsKey(trigger))
                    activeSkillsByTrigger[trigger] = new List<IPassiveSkillEffect>();

                activeSkillsByTrigger[trigger].Add(effect);
            }
        }

        private void UnregisterSkill(string skillName)
        {
            if (!activeSkillNames.Remove(skillName)) return;

            var effect = PassiveSkillRegistry.Get(skillName);
            if (effect == null) return;

            foreach (var trigger in effect.Triggers)
            {
                if (activeSkillsByTrigger.TryGetValue(trigger, out var list))
                {
                    list.Remove(effect);
                }
            }
        }

        private void ClearActiveSkills()
        {
            activeSkillsByTrigger.Clear();
            activeSkillNames.Clear();
            skillDisplayNames.Clear();
        }

        // ===========================================================
        //  戦闘ライフサイクル
        // ===========================================================

        /// <summary>
        /// 戦闘開始（CombatContext生成）
        /// </summary>
        public void BeginCombat(int playerMaxHP, int enemyMaxHP = 0, int playerDiceMax = 6, int enemyDiceMax = 6, int enemyThreat = 0)
        {
            context = new CombatContext(playerMaxHP, enemyMaxHP, enemyThreat);
            
            // ダイス設定値を設定
            context.playerDiceMax = playerDiceMax;
            context.enemyDiceMax = enemyDiceMax;
            
            triggeredPlayerSkills.Clear();
            triggeredEnemySkills.Clear();
            Debug.Log($"[PassiveSkillManager] Combat started. Player skills: {activeSkillNames.Count}, Enemy skills: {enemySkillNames.Count}");
            Debug.Log($"[PassiveSkillManager] Dice setup - Player: d{playerDiceMax}, Enemy: d{enemyDiceMax}, Threat: {enemyThreat}");
        }

        /// <summary>
        /// 敵のパッシブスキルを登録（戦闘開始前に呼ぶ）
        /// </summary>
        public void RegisterEnemySkills(List<CombatSystem.EnemyPassiveEntry> entries)
        {
            ClearEnemySkills();
            if (entries == null) return;

            foreach (var entry in entries)
            {
                var effect = PassiveSkillRegistry.Get(entry.internalName);
                if (effect == null)
                {
                    Debug.LogWarning($"[PassiveSkillManager] Unknown enemy skill: {entry.internalName}");
                    continue;
                }
                if (enemySkillNames.Contains(entry.internalName)) continue;
                enemySkillNames.Add(entry.internalName);

                // 日本語表示名を保存
                if (!string.IsNullOrEmpty(entry.skillName))
                    skillDisplayNames[entry.internalName] = entry.skillName;

                foreach (var trigger in effect.Triggers)
                {
                    if (!enemySkillsByTrigger.ContainsKey(trigger))
                        enemySkillsByTrigger[trigger] = new List<IPassiveSkillEffect>();
                    enemySkillsByTrigger[trigger].Add(effect);
                }
            }
        }

        private void ClearEnemySkills()
        {
            enemySkillsByTrigger.Clear();
            enemySkillNames.Clear();
        }

        /// <summary>
        /// 敵スキルのトリガー発火
        /// 敵視点での実行: context の playerWonRoll/playerLostRoll を
        /// 反転して実行し、元に戻す
        /// </summary>
        public void FireEnemyTrigger(PassiveSkillTrigger trigger)
        {
            if (context == null) return;
            // 静寂のローブ等で敵パッシブが無効化されているターン中はスキップ
            if (context.enemyPassivesDisabledTurns > 0) return;
            if (!enemySkillsByTrigger.TryGetValue(trigger, out var skills)) return;

            // 敵視点に反転
            SwapPerspective();

            for (int i = 0; i < skills.Count; i++)
            {
                try
                {
                    skills[i].Execute(trigger, context);
                    triggeredEnemySkills.Add(skills[i].SkillId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[PassiveSkillManager] Error in enemy skill '{skills[i].SkillId}': {e.Message}");
                }
            }

            // プレイヤー視点に戻す
            SwapPerspective();
        }

        /// <summary>
        /// プレイヤー/敵の視点を入れ替える
        /// </summary>
        private void SwapPerspective()
        {
            if (context == null) return;

            // HP入れ替え
            int tmpHP = context.playerCurrentHP;
            int tmpMaxHP = context.playerMaxHP;
            context.playerCurrentHP = context.enemyCurrentHP;
            context.playerMaxHP = context.enemyMaxHP;
            context.enemyCurrentHP = tmpHP;
            context.enemyMaxHP = tmpMaxHP;

            // ダイス入れ替え
            var tmpDice = context.playerDice;
            context.playerDice = context.enemyDice;
            context.enemyDice = tmpDice;

            int tmpTotal = context.playerDiceTotal;
            context.playerDiceTotal = context.enemyDiceTotal;
            context.enemyDiceTotal = tmpTotal;

            // 固定ダメージ入れ替え
            int tmpFixed = context.fixedDamageToEnemy;
            context.fixedDamageToEnemy = context.fixedDamageToPlayer;
            context.fixedDamageToPlayer = tmpFixed;

            // 勝敗反転
            bool tmpWon = context.playerWonRoll;
            context.playerWonRoll = context.playerLostRoll;
            context.playerLostRoll = tmpWon;
            context.diceDifference = -context.diceDifference;
        }

        /// <summary>
        /// 新しいターンを開始
        /// </summary>
        public void BeginTurn()
        {
            if (context == null) return;
            context.BeginNewTurn();

            // 今ターンの発動追跡をリセット
            triggeredPlayerSkills.Clear();
            triggeredEnemySkills.Clear();

            // ターン開始時のバフを適用
            ApplyCurrentTurnBuffs();

            FireTrigger(PassiveSkillTrigger.OnTurnStart);
        }

        /// <summary>
        /// 戦闘終了（CombatContext破棄）
        /// </summary>
        public void EndCombat()
        {
            FireTrigger(PassiveSkillTrigger.OnBattleEnd);
            ClearEnemySkills();
            context = null;
            Debug.Log("[PassiveSkillManager] Combat ended.");
        }

        // ===========================================================
        //  トリガー発火（コアロジック）
        // ===========================================================

        /// <summary>
        /// 指定トリガーに反応する全スキルを実行
        /// 計算量: O(そのトリガーに登録されたスキル数)
        /// </summary>
        public void FireTrigger(PassiveSkillTrigger trigger)
        {
            if (context == null) return;
            if (!activeSkillsByTrigger.TryGetValue(trigger, out var skills)) return;

            for (int i = 0; i < skills.Count; i++)
            {
                try
                {
                    skills[i].Execute(trigger, context);
                    triggeredPlayerSkills.Add(skills[i].SkillId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[PassiveSkillManager] Error in skill '{skills[i].SkillId}': {e.Message}");
                }
            }
        }

        // ===========================================================
        //  バフ適用
        // ===========================================================

        /// <summary>
        /// currentBuffsの値をCombatContextに反映
        /// </summary>
        private void ApplyCurrentTurnBuffs()
        {
            if (context == null) return;

            // ダイスボーナス → ProcessPostRoll内でplayerDiceTotalに加算
            // 会心ボーナス  → ProcessDamage内のOnCriticalCheckで加算
            // ダメージボーナス → ProcessDamage内で加算
            // ここでは追加の即時適用処理があれば記述
        }

        // ===========================================================
        //  ダイスロール統合
        // ===========================================================

        /// <summary>
        /// ダイスロール後にパッシブスキルを適用する統合メソッド
        /// 戦闘システムはこれ1つを呼べばよい
        /// </summary>
        public void ProcessPostRoll(int[] playerDice, int[] enemyDice)
        {
            if (context == null) return;

            // ダイス値を設定
            context.playerDice = playerDice;
            context.enemyDice = enemyDice;

            // 消費: 賭博師のダイス（50%で全最大 / 50%で全1・消費）
            if (context.gamblerArmed)
            {
                int pm = context.playerDiceMax > 0 ? context.playerDiceMax : 6;
                int v = UnityEngine.Random.value < 0.5f ? pm : 1;
                for (int i = 0; i < playerDice.Length; i++) playerDice[i] = v;
                context.gamblerArmed = false;
                UnityEngine.Debug.Log($"[Consumables] 賭博師のダイス: 全ダイス→{v}");
            }

            // 敵ダイスへの制約適用（処刑/正義への妄執）
            ApplyDiceOverrides(enemyDice);

            // 合計計算
            context.playerDiceTotal = Sum(playerDice);
            context.enemyDiceTotal = Sum(enemyDice);

            // バフによるダイスボーナス適用（無我無心: カスタムダイス以外の補正を拒否）
            if (!context.rollPurity)
                context.playerDiceTotal += (int)context.GetBuff("diceBonus");

            // 敵ダイスデバフ適用（正義への妄執など）
            int enemyDebuff = (int)context.GetBuff("enemyDiceDebuff");
            if (enemyDebuff > 0)
            {
                context.enemyDiceTotal = System.Math.Max(0, context.enemyDiceTotal - enemyDebuff);
            }

            // 消費: 敵弱体（敵ダイス合計-X・全戦闘）
            if (context.consEnemyDiceDebuff > 0)
                context.enemyDiceTotal = System.Math.Max(0, context.enemyDiceTotal - context.consEnemyDiceDebuff);

            // パッシブスキル実行（OnPostRoll）
            FireTrigger(PassiveSkillTrigger.OnPostRoll);

            // ダイス差計算（ダメージ算出はこの raw 差を使う）
            context.diceDifference = context.playerDiceTotal - context.enemyDiceTotal;

            // 勝敗判定（消費「ダイス補正」は勝敗のみ＝ダメージ非加算。無我無心中は無効）
            int effDiff = context.diceDifference + (context.rollPurity ? 0 : context.consDiceRoll);
            context.playerWonRoll = effDiff > 0;
            context.playerLostRoll = effDiff < 0;

            // 画竜点睛: 発動ターンはロール即勝利（敗北/引分を上書き）
            if (context.garyoProc)
            {
                context.playerWonRoll = true;
                context.playerLostRoll = false;
            }

            // 勝敗トリガー発火
            if (context.playerWonRoll)
            {
                context.consecutiveWins++;
                context.consecutiveLosses = 0;
                FireTrigger(PassiveSkillTrigger.OnRollWin);
            }
            else if (context.playerLostRoll)
            {
                context.consecutiveLosses++;
                context.consecutiveWins = 0;
                FireTrigger(PassiveSkillTrigger.OnRollLose);
            }
            else
            {
                FireTrigger(PassiveSkillTrigger.OnRollDraw);
            }
        }

        /// <summary>
        /// ダメージ計算にパッシブスキルを適用する統合メソッド
        /// 
        /// 処理順序:
        /// 1. バフによるダメージボーナス加算
        /// 2. ダメージ計算前スキル（OnPreDealDamage / OnPreReceiveDamage）
        /// 3. ダメージシールド適用
        /// 4. 追撃ダメージスキル
        /// 5. 無効化チェック
        /// 6. 全ダメージ合算（メイン＋追撃＋固定）
        /// 7. クリティカル判定（合算後に1回だけロール、全ダメージに適用）
        /// 8. ダメージ確定後トリガー
        /// </summary>
        /// <param name="baseDamage">基本ダメージ（ダイス差）</param>
        /// <param name="pursuitDamage">追撃ダメージ（余剰ダイスのリロール合計）</param>
        /// <param name="criticalNumerator">クリティカル確率の分子（0～9）</param>
        /// <returns>(totalDamage: クリティカル適用済み合計ダメージ, fixedDamage: 別枠固定ダメージ, isCritical)</returns>
        public (int totalDamage, int fixedDamage, bool isCritical) ProcessDamage(
            int baseDamage, int pursuitDamage, int criticalNumerator = 0)
        {
            if (context == null) return (baseDamage + pursuitDamage, 0, false);

            context.baseDamage = baseDamage;
            context.finalDamage = baseDamage;
            context.pursuitDamage = pursuitDamage;

            // 1. バフによるダメージボーナス（メインダメージに加算）
            context.finalDamage += (int)context.GetBuff("damageBonus");

            // 2. ダメージ計算前スキル（ダメージ値を増減するパッシブ）
            if (context.playerWonRoll)
                FireTrigger(PassiveSkillTrigger.OnPreDealDamage);
            else
                FireTrigger(PassiveSkillTrigger.OnPreReceiveDamage);

            // 3. 追撃ダメージスキル
            FireTrigger(PassiveSkillTrigger.OnPrePursuitDamage);

            // 4. 無効化チェック
            if (context.nullifyAllDamage)
            {
                context.finalDamage = 0;
                context.pursuitDamage = 0;
            }
            if (context.nullifyPursuitDamage)
            {
                context.pursuitDamage = 0;
            }

            // 5. 全ダメージ合算（メイン＋追撃）
            int totalDamage = context.finalDamage + context.pursuitDamage;

            // 6. クリティカル判定（合算後に1回だけ）
            FireTrigger(PassiveSkillTrigger.OnCriticalCheck);
            context.criticalBonus += (int)context.GetBuff("criticalBonus");

            int effectiveNumerator = System.Math.Min(9,
                System.Math.Max(0, criticalNumerator + context.criticalBonus));

            if (effectiveNumerator > 0)
            {
                int roll = UnityEngine.Random.Range(0, 9); // 0～8
                context.isCritical = roll < effectiveNumerator;
            }
            else
            {
                context.isCritical = false;
            }

            if (context.isCritical && totalDamage > 0)
            {
                FireTrigger(PassiveSkillTrigger.OnCriticalDamage);
                totalDamage = (int)(totalDamage * context.criticalMultiplier);
            }

            // 8. ダメージ確定後トリガー
            context.finalDamage = totalDamage; // 確定値をコンテキストに反映
            if (context.playerWonRoll)
                FireTrigger(PassiveSkillTrigger.OnPostDealDamage);
            else
                FireTrigger(PassiveSkillTrigger.OnPostReceiveDamage);

            return (totalDamage, context.fixedDamageToEnemy, context.isCritical);
        }

        // ===========================================================
        //  ユーティリティ
        // ===========================================================

        private void ApplyDiceOverrides(int[] enemyDice)
        {
            if (context.pendingDiceOverrides.Count == 0 || enemyDice == null || enemyDice.Length == 0)
                return;

            foreach (var req in context.pendingDiceOverrides)
            {
                int targetIndex = FindDiceIndex(enemyDice, req.target);
                if (targetIndex >= 0)
                {
                    enemyDice[targetIndex] = req.fixedValue;
                }
            }
            context.pendingDiceOverrides.Clear();
        }

        private int FindDiceIndex(int[] dice, DiceOverrideRequest.TargetDice target)
        {
            if (dice == null || dice.Length == 0) return -1;

            int resultIndex = 0;
            for (int i = 1; i < dice.Length; i++)
            {
                if (target == DiceOverrideRequest.TargetDice.Lowest && dice[i] < dice[resultIndex])
                    resultIndex = i;
                else if (target == DiceOverrideRequest.TargetDice.Highest && dice[i] > dice[resultIndex])
                    resultIndex = i;
            }
            return resultIndex;
        }

        private static int Sum(int[] arr)
        {
            if (arr == null) return 0;
            int total = 0;
            for (int i = 0; i < arr.Length; i++)
                total += arr[i];
            return total;
        }

        /// <summary>
        /// 内部名からスキルの日本語表示名を取得
        /// </summary>
        public string GetSkillDisplayName(string internalName)
        {
            if (skillDisplayNames.TryGetValue(internalName, out var displayName))
                return displayName;
            return internalName; // フォールバック
        }

        /// <summary>現在アクティブなプレイヤースキル名一覧を取得</summary>
        public IReadOnlyList<string> GetActiveSkillNames() => activeSkillNames;

        /// <summary>現在アクティブな敵スキル名一覧を取得</summary>
        public IReadOnlyList<string> GetEnemySkillNames() => enemySkillNames;

        /// <summary>今ターン中に発動したプレイヤースキルのID一覧</summary>
        public IReadOnlyCollection<string> GetTriggeredPlayerSkills() => triggeredPlayerSkills;

        /// <summary>今ターン中に発動した敵スキルのID一覧</summary>
        public IReadOnlyCollection<string> GetTriggeredEnemySkills() => triggeredEnemySkills;

        /// <summary>指定スキルが今ターン中に発動したか</summary>
        public bool IsPlayerSkillTriggered(string skillId) => triggeredPlayerSkills.Contains(skillId);

        /// <summary>指定敵スキルが今ターン中に発動したか</summary>
        public bool IsEnemySkillTriggered(string skillId) => triggeredEnemySkills.Contains(skillId);

        /// <summary>戦闘中かどうか</summary>
        public bool IsInCombat => context != null;

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void OnApplicationQuit()
        {
            isApplicationQuitting = true;
        }
    }
}

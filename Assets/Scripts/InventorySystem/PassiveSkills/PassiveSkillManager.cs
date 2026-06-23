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

        /// <summary>現在「発動している」プレイヤーパッシブスキル数（装備武器/ダイス由来＋所持パッシブ＋刻印で
        /// この戦闘に実際に登録された数。インベントリにあるだけの未装備品は含まない）。共鳴が参照する。</summary>
        public int ActivePlayerSkillCount => activeSkillNames.Count;

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

            // 2026-06-22 リワーク:
            //   同名パッシブ → 1 回のみ発動 (HashSet で dedup)
            //   同家系の Lv 違い (例: MightI + MightII) → 最高 Lv のみ発動 (下位は抑制)
            //   これにより 「弱い同名/下位互換を所持し続けるメリット = ゼロ」 が確定する。
            var seenItemIds = new HashSet<string>();
            var collected = new List<(string id, string display)>();
            foreach (var item in equippedItems)
            {
                if (item == null) continue;
                if (!string.IsNullOrEmpty(item.internalName) && !seenItemIds.Add(item.internalName)) continue;
                if (item.passiveSkills == null) continue;
                foreach (var ps in item.passiveSkills)
                {
                    if (string.IsNullOrEmpty(ps.internalName)) continue;
                    collected.Add((ps.internalName, ps.skillName));
                }
            }

            // 同名抑制: 一意な ID 集合に
            var uniqueIds = new HashSet<string>();
            foreach (var c in collected) uniqueIds.Add(c.id);

            // 上位 Lv 抑制: 同家系で上位がいれば下位を捨てる
            var finalIds = new HashSet<string>();
            foreach (var id in uniqueIds)
            {
                if (PassiveSkillRegistry.IsHigherTierPresent(id, uniqueIds))
                {
                    Debug.Log($"[PassiveSkillManager] 上位互換抑制: {id} (同家系の上位 Lv が存在)");
                    continue;
                }
                finalIds.Add(id);
            }

            // 登録
            foreach (var c in collected)
            {
                if (!finalIds.Contains(c.id)) continue;
                // dedup: 同一 ID は 1 回のみ登録 (重複は activeSkillNames が弾く)
                if (activeSkillNames.Contains(c.id)) continue;
                RegisterSkill(c.id);
                if (!string.IsNullOrEmpty(c.display))
                    skillDisplayNames[c.id] = c.display;
            }

            Debug.Log($"[PassiveSkillManager] Active skills refreshed: {activeSkillNames.Count} skills (収集{collected.Count}, 同名抑制後{uniqueIds.Count}, 上位抑制後{finalIds.Count})");
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

        /// <summary>internalName を直接指定してスキルを登録（武器の段階式動的付与用）。</summary>
        public void AddSkillById(string internalName, string displayName = null)
        {
            RegisterSkill(internalName);
            if (!string.IsNullOrEmpty(displayName))
                skillDisplayNames[internalName] = displayName;
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

        /// <summary>希望「迷妄」(絶望帯・ADR-0002): 戦闘開始時にプレイヤーのパッシブスキルを count 個
        /// ランダムで無効化する。PassiveItem(佯狂者・記憶の砂時計 等)は別系統(PassiveItemManager)のため
        /// activeSkillNames に含まれず、自動的に対象外。スキルは戦闘ごとに再登録されるため復元は不要。</summary>
        public void DisableRandomPlayerSkills(int count)
        {
            if (count <= 0 || activeSkillNames.Count == 0) return;
            var distinct = new List<string>(new HashSet<string>(activeSkillNames));
            int n = System.Math.Min(count, distinct.Count);
            for (int i = 0; i < n && distinct.Count > 0; i++)
            {
                int idx = UnityEngine.Random.Range(0, distinct.Count);
                string name = distinct[idx];
                distinct.RemoveAt(idx);
                while (activeSkillNames.Contains(name)) UnregisterSkill(name);
                string disp = skillDisplayNames.TryGetValue(name, out var d) ? d : name;
                Debug.Log($"[迷妄] パッシブ「{disp}」を無効化 (絶望帯)");
            }
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
            // diceDifference は playerDiceTotal/enemyDiceTotal から算出する読み取り専用プロパティ。
            // 上で両合計を入れ替えたので符号は自動反転する（明示反転は不要）。
        }

        /// <summary>
        /// 新しいターンを開始
        /// </summary>
        public void BeginTurn()
        {
            if (context == null) return;
            context.BeginNewTurn();
            context.TickStatuses(StatusTick.TurnStart); // #3 統一フレーム: 炎上等のDOT/減衰を一括（fixedDamage 0化後）

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

            // 合計計算（ダイス出目→各種ダイス補正までを集約。リロール時と共通）
            RecomputeDiceTotals(playerDice, enemyDice);

            // パッシブスキル実行（OnPostRoll）
            FireTrigger(PassiveSkillTrigger.OnPostRoll);

            // ダイス差は context.diceDifference（読み取り専用プロパティ）が常に最新を返す。

            // 勝敗判定（消費「ダイス補正」は勝敗のみ＝ダメージ非加算。無我無心中は無効）
            int effDiff = context.diceDifference + (context.rollPurity ? 0 : context.consDiceRoll);
            context.playerWonRoll = effDiff > 0;
            context.playerLostRoll = effDiff < 0;

            // 引き分けは再ロール（脅威システム下では引分でも脅威被弾するため、引分自体を解消する）。
            // OnPostRoll の再発火を避けるため、ダイスのみ振り直して合計・勝敗だけ再計算する。
            int rerollGuard = 20;
            while (!context.playerWonRoll && !context.playerLostRoll && !context.garyoProc && rerollGuard-- > 0)
            {
                RerollDiceForDraw(playerDice, enemyDice);
                ApplyDiceOverrides(enemyDice);

                // 素点＋各種ダイス補正を再集計（通常ロールと同じ集約関数）。
                // 注意: OnPostRoll パッシブ(軽量/星導/号令 等)は再発火しないためここでは乗らない
                //       ＝この refactor 前から同じ挙動。引分解消の素点判定のみを目的とする。
                RecomputeDiceTotals(playerDice, enemyDice);

                int reEff = context.diceDifference + (context.rollPurity ? 0 : context.consDiceRoll);
                context.playerWonRoll = reEff > 0;
                context.playerLostRoll = reEff < 0;
            }

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
                context.rollLossOccurredThisCombat = true; // 希望の灯片: 灯が一度でも陰る
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

            // Λデバフ「注意散漫」: 会心分子(X/9)の上限を 8/6/4 に制限（判定の Random(0,9) は不変）
            int critCap = context.lambdaCritNumeratorCap > 0 ? context.lambdaCritNumeratorCap : 9;
            int effectiveNumerator = System.Math.Min(critCap,
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

            // 末那識など: 会心強制（乱数・会心率capを無視して確定）
            if (context.forceCritical) context.isCritical = true;

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

            // 蒼白の槍騎士: 軽減無視ダメージ(fixedDamageToEnemy)を増幅
            if (context.fixedDamageMultiplier > 1f && context.fixedDamageToEnemy > 0)
                context.fixedDamageToEnemy = UnityEngine.Mathf.CeilToInt(context.fixedDamageToEnemy * context.fixedDamageMultiplier);

            return (totalDamage, context.fixedDamageToEnemy, context.isCritical);
        }

        // ===========================================================
        //  ユーティリティ
        // ===========================================================

        /// <summary>
        /// ダイス出目の合計を計算し、各種ダイス補正（diceBonus / enemyDiceDebuff /
        /// consEnemyDiceDebuff / enemyDiceTotalBonus / bossDiceBonus）を適用する。
        /// 通常ロールと引分リロールで共通。OnPostRoll パッシブによる加算はここでは扱わない。
        /// </summary>
        private void RecomputeDiceTotals(int[] playerDice, int[] enemyDice)
        {
            context.playerDiceTotal = Sum(playerDice);
            context.enemyDiceTotal = Sum(enemyDice);

            // バフによるダイスボーナス（無我無心: カスタムダイス以外の補正を拒否）
            if (!context.rollPurity)
                context.playerDiceTotal += (int)context.GetBuff("diceBonus");

            // Λデバフ「重い足取り」: 1ターン目のみプレイヤーダイス合計を減算(負値・無我無心中は無効)
            if (!context.rollPurity && context.currentTurn == 1 && context.lambdaFirstTurnDiceDelta != 0)
                context.playerDiceTotal = System.Math.Max(0, context.playerDiceTotal + context.lambdaFirstTurnDiceDelta);

            // 敵ダイスデバフ（正義への妄執など）
            int enemyDebuff = (int)context.GetBuff("enemyDiceDebuff");
            if (enemyDebuff > 0)
                context.enemyDiceTotal = System.Math.Max(0, context.enemyDiceTotal - enemyDebuff);

            // 消費: 敵弱体（敵ダイス合計-X・全戦闘）
            if (context.consEnemyDiceDebuff > 0)
                context.enemyDiceTotal = System.Math.Max(0, context.enemyDiceTotal - context.consEnemyDiceDebuff);

            // 沈黙の剣帯 等: 床なし減算（負値許容＝大差勝ち→基礎ダメ激増）
            if (context.enemyDiceTotalPenalty != 0)
                context.enemyDiceTotal -= context.enemyDiceTotalPenalty;
            // 敵スタンス（ADR-0005）の弱ロールは「面を縮めて実際に振る」方式（CombatManager の RollDice 時）。
            // ＝ここでの合計いじりは無し（事後倍率にしない方針）。

            // 〈妙覚〉T1自由攻撃ターンは敵ロールを確実に0に保つ（真我/星火等の加算を乗せない）。
            if (!context.myokakuFreeHit)
            {
                // 星火燎原 等: 敵(ボス)ダイス合計への加算（勝敗判定前＝ロールを実際に押し上げる）
                if (context.enemyDiceTotalBonus > 0)
                    context.enemyDiceTotal += context.enemyDiceTotalBonus;
                // ボス威風 + 真我（別枠・固定。 真我=7層覚者の素ロール加算でダイス上限超の難度調整）
                if (context.bossDiceBonus > 0)
                    context.enemyDiceTotal += context.bossDiceBonus;
            }

            // Λデバフ「苛立つ強敵」: 経過 interval ターン毎に敵ダイス合計 +1（累積）
            if (context.lambdaIrritatingInterval > 0 && context.currentTurn > 0)
            {
                int irr = context.currentTurn / context.lambdaIrritatingInterval;
                if (irr > 0) context.enemyDiceTotal += irr;
            }
        }

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

        /// <summary>引き分け解消用にダイス配列を in-place で振り直す。
        /// サドンデス中は両者1d6、コントラタック中はプレイヤーを1固定で維持する。
        /// 装備ダイス面(equippedDiceFaces)があればプレイヤー側はそこから抽選。</summary>
        private void RerollDiceForDraw(int[] playerDice, int[] enemyDice)
        {
            bool sudden = context.ashenSuddenDeath || context.myokakuSuddenDeath;
            bool contre = context.GetAccumulated("player_contre") > 0;
            int pMax = context.playerDiceMax > 0 ? context.playerDiceMax : 6;
            int eMax = context.enemyDiceMax > 0 ? context.enemyDiceMax : 6;

            if (playerDice != null && !contre)
            {
                for (int i = 0; i < playerDice.Length; i++)
                {
                    if (sudden)
                        playerDice[i] = UnityEngine.Random.Range(1, 7);
                    else if (context.equippedDiceFaces != null && context.equippedDiceFaces.Length > 0)
                        playerDice[i] = context.equippedDiceFaces[UnityEngine.Random.Range(0, context.equippedDiceFaces.Length)];
                    else
                        playerDice[i] = UnityEngine.Random.Range(1, pMax + 1);
                }
            }

            if (enemyDice != null)
            {
                for (int i = 0; i < enemyDice.Length; i++)
                    enemyDice[i] = sudden ? UnityEngine.Random.Range(1, 7) : UnityEngine.Random.Range(1, eMax + 1);
            }
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

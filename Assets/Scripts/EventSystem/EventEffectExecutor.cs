using System.Collections.Generic;
using UnityEngine;
using EventSystem.TimedEffects;
using GameLoop;
using InventorySystem;
using MapSystem;

namespace EventSystem
{
    /// <summary>
    /// 効果命令を RunState / HungerSystem に適用する。
    /// 戦闘トリガなど即時に解決できない効果は ExecutionResult に集約して返す。
    /// </summary>
    public static class EventEffectExecutor
    {
        public class ExecutionResult
        {
            public bool triggerCombat;
            public bool triggerEliteCombat;
            public bool triggerRandomEvent;
            /// <summary>戦闘勝利後に再適用すべき効果（「勝利後...」プレフィックス付き）</summary>
            public List<EventEffect> postCombatEffects = new List<EventEffect>();
            /// <summary>UI 表示用ログ</summary>
            public List<string> log = new List<string>();
        }

        /// <summary>
        /// 効果リストを実行する。
        /// </summary>
        public static ExecutionResult Execute(List<EventEffect> effects, RunState run, HungerSystem hunger)
        {
            var result = new ExecutionResult();
            if (effects == null || run == null) return result;

            foreach (var eff in effects)
            {
                if (eff == null) continue;

                if (eff.postCombat)
                {
                    // 戦闘後に再適用するよう保留（戦闘トリガがあれば後で発火する）
                    result.postCombatEffects.Add(eff);
                    continue;
                }

                ExecuteOne(eff, run, hunger, result);
            }
            return result;
        }

        private static void ExecuteOne(EventEffect eff, RunState run, HungerSystem hunger, ExecutionResult result)
        {
            switch (eff.type)
            {
                case EventEffectType.None:
                    break;

                case EventEffectType.HpDelta:
                    run.playerHP = Mathf.Clamp(run.playerHP + eff.amount, 0, run.playerMaxHP);
                    result.log.Add($"HP{eff.amount:+0;-0} (現在 {run.playerHP}/{run.playerMaxHP})");
                    break;

                case EventEffectType.HpFullHeal:
                    run.playerHP = run.playerMaxHP;
                    result.log.Add($"HP全回復 ({run.playerHP}/{run.playerMaxHP})");
                    break;

                case EventEffectType.HpSetTo:
                    run.playerHP = Mathf.Clamp(eff.amount, 0, run.playerMaxHP);
                    result.log.Add($"HP→{run.playerHP}");
                    break;

                // 現在HPの半分を支払う。 **端数は残す側へ切り上げ、 最低 1 を保証**するので
                // この効果単体では死なない。 HP を固定値 1 にする旧「災厄の予兆」は、
                // 高HPほど代償が軽くなる上に直後の雑魚戦でほぼ確実に死ぬため置き換えた
                // (2026-08-04: 全ランの 29.4% がこの経路で死亡していた)。
                case EventEffectType.HpHalve:
                {
                    int before = run.playerHP;
                    run.playerHP = Mathf.Max(1, (before + 1) / 2);
                    result.log.Add($"現在HPの半分を支払った ({before} → {run.playerHP})");
                    break;
                }

                case EventEffectType.MaxHpDelta:
                    run.playerMaxHP = Mathf.Max(1, run.playerMaxHP + eff.amount);
                    run.playerHP = Mathf.Min(run.playerHP, run.playerMaxHP);
                    result.log.Add($"最大HP{eff.amount:+0;-0} → {run.playerMaxHP}");
                    break;

                case EventEffectType.GoldDelta:
                {
                    int delta = eff.amount;
                    // 1/5 デノミ: イベント記載の値を素直に /5 (差を残すため Mathf.Max(1, ...))。
                    // 例: +15→+3, +8→+2, +6→+1, +5→+1, +3→+1, +1→+1
                    //     -5→-1, -1→-1
                    // **2026-08-10 経済リスケール**: 収入 ×3 / 支出 ×5。
                    //   旧 1/5 デノミの除算を、 収入は ×3/5、 支出 (負の delta) は等倍へ。
                    //   イベント記載値は 1/5 デノミ**前**のスケールで書かれている。
                    if (delta > 0)
                    {
                        delta = Mathf.Max(1, Mathf.RoundToInt(delta * 3f / 5f));
                        delta = GameLoop.LastStand.FilterGoldGain(run, delta);
                    }
                    else if (delta < 0)
                    {
                        delta = -Mathf.Max(1, Mathf.RoundToInt(-delta));
                    }
                    run.coins = Mathf.Max(0, run.coins + delta);
                    result.log.Add($"ゴールド{delta:+0;-0} (現在 {run.coins})");
                    break;
                }

                case EventEffectType.HungerDelta:
                {
                    // 飢餓→希望統合(ADR-0002): 旧「空腹度±N」を希望±N へ。
                    // (食通の懐刀 +1 フックは 2026-07-18 アイテム削除に伴い除去)
                    int amount = eff.amount;
                    if (amount > 0) GameLoop.HopeSystem.Recover(run, amount);
                    else if (amount < 0) GameLoop.HopeSystem.Reduce(run, -amount);
                    result.log.Add($"希望{amount:+0;-0} (現在 {run.hope}/{run.hopeCap})");
                    break;
                }

                case EventEffectType.HopeDelta:
                {
                    int amount = eff.amount;
                    if (amount > 0) GameLoop.HopeSystem.Recover(run, amount);
                    else if (amount < 0) GameLoop.HopeSystem.Reduce(run, -amount);
                    result.log.Add($"希望{amount:+0;-0} (現在 {run.hope}/{run.hopeCap})");
                    break;
                }

                case EventEffectType.MaterialDelta:
                    run.weaponMaterials = Mathf.Max(0, run.weaponMaterials + eff.amount);
                    result.log.Add($"武器強化素材{eff.amount:+0;-0} (現在 {run.weaponMaterials})");
                    break;

                case EventEffectType.ArmorDurabilityLoss:
                    // 防具耐久度システム未実装。ログのみ残す。
                    result.log.Add("防具の耐久値減少（システム未実装）");
                    break;

                case EventEffectType.TimedBuff:
                {
                    int charges = TimedEffectRegistry.GetDefaultCharges(eff.param);
                    AddOrIncrement(run.timedBuffs, eff.param, charges);
                    result.log.Add($"時限バフ獲得: {eff.param} ({charges}回)");
                    break;
                }

                case EventEffectType.TimedDebuff:
                {
                    int charges = TimedEffectRegistry.GetDefaultCharges(eff.param);
                    AddOrIncrement(run.timedDebuffs, eff.param, charges);
                    result.log.Add($"時限デバフ獲得: {eff.param} ({charges}回)");
                    break;
                }

                case EventEffectType.PermanentDebuff:
                    run.permanentDebuffs.Add(eff.param);
                    result.log.Add($"永続デバフ獲得: {eff.param}");
                    break;

                case EventEffectType.GainPassiveItem:
                {
                    string id = string.IsNullOrEmpty(eff.param)
                        ? PickRandomItemId(ItemCategory.Passive, run)
                        : eff.param;
                    if (!string.IsNullOrEmpty(id))
                    {
                        InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(run, id);
                        result.log.Add($"パッシブアイテム獲得: {ResolveLabel(id)}");
                        ApplyPassiveAcquisitionBonus(run, id, result);
                    }
                    break;
                }

                case EventEffectType.GainConsumableItem:
                {
                    string id = string.IsNullOrEmpty(eff.param)
                        ? PickRandomItemId(ItemCategory.Consumable)
                        : eff.param;
                    if (!string.IsNullOrEmpty(id))
                    {
                        run.TryAddConsumable(id);
                        result.log.Add($"消費アイテム獲得: {ResolveLabel(id)}");
                    }
                    break;
                }

                case EventEffectType.GainSpecificItem:
                    // カテゴリ未指定: パッシブ扱い（イベントで [固有名] 指定された場合）
                    if (!string.IsNullOrEmpty(eff.param))
                    {
                        InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(run, eff.param);
                        result.log.Add($"アイテム獲得: {ResolveLabel(eff.param)}");
                        ApplyPassiveAcquisitionBonus(run, eff.param, result);
                    }
                    break;

                case EventEffectType.GainFlag:
                    run.ownedFlags.Add(eff.param);
                    result.log.Add($"フラグアイテム獲得: {eff.param}");
                    break;

                case EventEffectType.ObservatoryTakeCopy:
                    GameLoop.ObservatoryState.TakeCopy();
                    result.log.Add("観測所の写しを持ち帰った");
                    break;

                case EventEffectType.DiscardFlag:
                    run.ownedFlags.Remove(eff.param);
                    result.log.Add($"フラグアイテム廃棄: {eff.param}");
                    break;

                case EventEffectType.DiscardPassiveItem:
                    if (!string.IsNullOrEmpty(eff.param))
                    {
                        int idx = run.ownedPassiveItems.IndexOf(eff.param);
                        if (idx >= 0)
                            InventorySystem.Helpers.PassiveAddHelper.RemoveAt(run, idx);
                        result.log.Add($"パッシブアイテム廃棄: {eff.param}");
                    }
                    break;

                case EventEffectType.EnterCombat:
                    result.triggerCombat = true;
                    result.log.Add("→ 戦闘発生");
                    break;

                case EventEffectType.EnterEliteCombat:
                    result.triggerEliteCombat = true;
                    result.log.Add("→ エリート戦闘発生");
                    break;

                case EventEffectType.RandomEvent:
                    result.triggerRandomEvent = true;
                    result.log.Add("→ ランダムイベント");
                    break;

                case EventEffectType.Probability:
                    ExecuteProbability(eff, run, hunger, result);
                    break;

                case EventEffectType.CircusHandover:
                    ExecuteCircusHandover(run, result);
                    break;
            }
        }

        /// <summary>[廃止] サーカス団引渡し。 旅団契約システムを 2026-08-11 に削除したため、
        /// 引き渡す契約自体が存在しない。 効果種別は enum 順序維持のため残置し、 実行は無効。
        /// 起点イベント〈別れのキャラバン〉もフラグ[サーカス団同行]が立たないので発生しない。</summary>
        private static void ExecuteCircusHandover(RunState run, ExecutionResult result)
        {
            result.log.Add("→ (廃止) サーカス団引渡し");
        }

        private static void ExecuteProbability(EventEffect eff, RunState run, HungerSystem hunger, ExecutionResult result)
        {
            if (eff.branches == null || eff.branchWeights == null) return;
            if (eff.branches.Count == 0) return;

            float total = 0f;
            for (int i = 0; i < eff.branchWeights.Count; i++) total += eff.branchWeights[i];
            if (total <= 0f) return;

            float r = GameLoop.GameRng.Value("EventEffectExecutor.1") * total;
            int picked = eff.branches.Count - 1;
            for (int i = 0; i < eff.branchWeights.Count; i++)
            {
                if ((r -= eff.branchWeights[i]) <= 0f) { picked = i; break; }
            }

            int pct = Mathf.RoundToInt(eff.branchWeights[picked] * 100f);
            result.log.Add($"確率分岐: {pct}% 抽選");

            foreach (var sub in eff.branches[picked])
                ExecuteOne(sub, run, hunger, result);
        }

        private static void AddOrIncrement(Dictionary<string, int> dict, string key, int delta)
        {
            if (string.IsNullOrEmpty(key)) return;
            dict.TryGetValue(key, out int v);
            dict[key] = v + delta;
        }

        /// <summary>ItemDatabase から指定カテゴリのアイテムをレア度重み付きで1個選出（イベント限定は除外）。</summary>
        /// <summary>〈さびれた観測所〉で写しを持ち帰っていた場合の**上乗せ報酬（固定）**。
        ///
        /// <para><b>選択肢にはしない。</b> 4 つ目の択にすると通常択と排他になり、
        /// パッシブ (最も厚い通貨・実測で 2 個 = 7層到達率 +10.5pt) が
        /// むしろ 1 個減る、という逆転が起きていた。 ラン跨ぎで持ち越した見返りなので
        /// **どの択を選んでも上乗せで入る**形にする。</para>
        ///
        /// <para>付与ロジックを GameManager 側に書き写さず、 ここに置いて
        /// <see cref="PickRandomItemId"/> と獲得時ボーナスを共用する ──
        /// 書き写すと抽選プールや取得時処理が静かに食い違う。</para></summary>
        public static void GrantObservatoryCopyBonus(GameLoop.RunState run)
        {
            if (run == null) return;
            var result = new ExecutionResult();

            string id = PickRandomItemId(ItemCategory.Passive, run);
            if (!string.IsNullOrEmpty(id))
            {
                InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(run, id);
                ApplyPassiveAcquisitionBonus(run, id, result);
            }
            run.weaponMaterials += 8;
            GameLoop.GoldIncome.Gain(run, 60, "観測所の写し");
            run.playerHP = run.playerMaxHP;

            UnityEngine.Debug.Log($"[観測所] 写しの上乗せ: パッシブ{(string.IsNullOrEmpty(id) ? "なし" : ResolveLabel(id))}"
                                + " / 素材+8 / ゴールド+60 / 全回復");
        }

        private static string PickRandomItemId(ItemCategory category, GameLoop.RunState run = null)
        {
            var db = ItemDatabase.Instance;
            if (db == null) return null;
            var pool = db.GetItemsByCategory(category);
            if (pool == null || pool.Count == 0) return null;

            var filtered = pool.FindAll(InventorySystem.Shop.EventOnlyItemFilter.IsAllowed);
            if (filtered.Count == 0) return null;

            // パッシブはラン重複排除（消費は使い切る前提なので重複可）。枯渇時は元プール（重複許可）。
            if (category == ItemCategory.Passive && run?.ownedPassiveItems != null)
            {
                // 重複禁止（捨てた物・昇華済みも含む）: seen を主軸に owned/ascended も合流。
                var owned = new System.Collections.Generic.HashSet<string>(run.ownedPassiveItems);
                if (run.ascendedPassiveIds != null) owned.UnionWith(run.ascendedPassiveIds);
                if (run.seenPassiveItemIds != null) owned.UnionWith(run.seenPassiveItemIds);
                var dd = filtered.FindAll(it => !owned.Contains(it.internalName));
                if (dd.Count > 0) filtered = dd;
            }

            var picked = InventorySystem.RarityWeightedPicker.Pick(filtered);
            return picked?.internalName;
        }

        /// <summary>id から表示名を解決。DBに見つからなければ id をそのまま返す。</summary>
        private static string ResolveLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return "（不明）";
            var data = ItemDatabase.Instance?.GetItem(id);
            return data != null ? data.displayName : id;
        }

        /// <summary>名前付きパッシブアイテム獲得時の即時ボーナス。</summary>
        private static void ApplyPassiveAcquisitionBonus(GameLoop.RunState run, string itemId, ExecutionResult result)
        {
            if (run == null || string.IsNullOrEmpty(itemId)) return;
            switch (itemId)
            {
                case "決意":
                {
                    int gain = GameLoop.GoldIncome.Gain(run, 1, "イベント(決意)");
                    result.log.Add($"決意の獲得ボーナス: +{gain}ゴールド");
                    break;
                }
                case "根拠のない確信":
                {
                    GameLoop.ConvictionSystem.OnFirstAcquired(run);
                    result.log.Add("根拠のない確信が芽吹いた (確信段階1)。エリート撃破毎に成長する。");
                    break;
                }
                case "脈なしの鋼心臓":
                {
                    int gain = GameLoop.LastStand.FilterMaxHPGain(run, 20);
                    if (gain > 0)
                    {
                        run.playerMaxHP += gain;
                        run.playerHP = UnityEngine.Mathf.Min(run.playerMaxHP, run.playerHP + gain);
                        result.log.Add($"鋼の心臓: 最大HP+{gain} → {run.playerMaxHP}");
                    }
                    break;
                }
            }
        }
    }
}

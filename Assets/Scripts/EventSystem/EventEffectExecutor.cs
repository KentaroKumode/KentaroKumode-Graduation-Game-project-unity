using System.Collections.Generic;
using UnityEngine;
using EventSystem.TimedEffects;
using GameLoop;
using ItemSystem;
using MapSystem;

namespace EventSystem
{
    /// <summary>
    /// 効果命令を RunState / ProvisionSystem に適用する。
    /// 戦闘トリガなど即時に解決できない効果は ExecutionResult に集約して返す。
    /// </summary>
    public static class EventEffectExecutor
    {
        public class ExecutionResult
        {
            public bool triggerCombat;
            public bool triggerEliteCombat;
            public bool triggerRandomEvent;
            /// <summary>この選択で使うターン (効果「ターン+N」の合計)。 実行はここではせず、
            /// イベントを閉じて地図へ戻った時に GameManager が進める (追跡者が動き、 捕まれば戦闘になる)。</summary>
            public int turnsToPass;
            /// <summary>航行の効果 (地図を明かす・追跡者を撒く・近道・前線 …・2026-11-12)。 ターンと同じく、 ここでは実行せず
            /// イベントを閉じて地図へ戻った時に GameManager が適用する (<c>GameManager.ApplyEventNav</c>)。 並びは書いた順。</summary>
            public List<EventEffect> navEffects = new List<EventEffect>();
            /// <summary>戦闘勝利後に再適用すべき効果（「勝利後...」プレフィックス付き）</summary>
            public List<EventEffect> postCombatEffects = new List<EventEffect>();
            /// <summary>UI 表示用ログ</summary>
            public List<string> log = new List<string>();
            /// <summary>引いた確率分岐の添字 (結果の欄の「N%で」の順・0 始まり)。 確率分岐が無ければ -1 (2026-11-12)。
            /// 画面 (選んだ後の文) と戦記 (記録の文) が分岐ごとの文を選ぶのに使う (<c>EventChoice.PostFlavorFor</c> / <c>DigestFor</c>)。
            /// 1 選択肢に確率分岐は 1 つ (解析がそう作る) なので、 最初に引いたものを残す。</summary>
            public int pickedBranch = -1;
        }

        /// <summary>
        /// 効果リストを実行する。
        /// </summary>
        public static ExecutionResult Execute(List<EventEffect> effects, RunState run)
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

                ExecuteOne(eff, run, result);
            }
            return result;
        }

        private static void ExecuteOne(EventEffect eff, RunState run, ExecutionResult result)
        {
            switch (eff.type)
            {
                case EventEffectType.None:
                    break;

                case EventEffectType.HpDelta:
                {
                    int hpBefore = run.playerHP;
                    run.playerHP = Mathf.Clamp(run.playerHP + eff.amount, 0, run.playerMaxHP);
                    GameLoop.HpOutsideDiag.Add("イベント:HP減", hpBefore - run.playerHP);   // [計装 2026-11-05]
                }
                    result.log.Add($"HP{eff.amount:+0;-0} (現在 {run.playerHP}/{run.playerMaxHP})");
                    break;

                case EventEffectType.HpFullHeal:
                    run.playerHP = run.playerMaxHP;
                    result.log.Add($"HP全回復 ({run.playerHP}/{run.playerMaxHP})");
                    break;

                case EventEffectType.HpSetTo:
                    GameLoop.HpOutsideDiag.Add("イベント:HP設定", run.playerHP - Mathf.Clamp(eff.amount, 0, run.playerMaxHP));   // [計装 2026-11-05]
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
                    GameLoop.HpOutsideDiag.Add("イベント:HP半減", before - run.playerHP);   // [計装 2026-11-05]
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
                    // 2026-11-11 (issue #17): 文面の値は今の大きさ (戦闘 1 回 = 10G)。 収入を ×3/5 する読み替え
                    //   (2026-08-10 の経済リスケールの名残) は外した。 支払いは選べるかの判定 (EventChoice.IsAvailable) と同じ額。
                    int delta = eff.amount;
                    run.coins = Mathf.Max(0, run.coins + delta);
                    result.log.Add($"ゴールド{delta:+0;-0} (現在 {run.coins})");
                    break;
                }

                case EventEffectType.HungerDelta:
                {
                    // 飢餓→物資統合(ADR-0002): 旧「空腹度±N」を物資±N へ。
                    // (食通の懐刀 +1 フックは 2026-07-18 アイテム削除に伴い除去)
                    int amount = eff.amount;
                    if (amount > 0) GameLoop.ProvisionSystem.Recover(run, amount);
                    else if (amount < 0) GameLoop.ProvisionSystem.Reduce(run, -amount);
                    result.log.Add($"物資{amount:+0;-0} (現在 {run.provision}/{run.provisionCap})");
                    break;
                }

                case EventEffectType.ProvisionDelta:
                {
                    // 2026-11-11 (issue #17): 文面の値は今の単位 (1 歩 = 1) で書いてある。 旧単位からの読み替えは外した。
                    int amount = eff.amount;
                    if (amount > 0)
                    {
                        GameLoop.ProvisionSystem.Recover(run, amount);
                        result.log.Add($"物資{amount:+0} (現在 {run.provision}/{run.provisionCap})");
                    }
                    else if (amount < 0)
                    {
                        // 2026-11-13 (作者の決定): **HP では払わない**。 選択肢の支払いの物資-N は、 足りなければ選べない
                        //   (EventChoice.UnavailableReason)。 ここで足りないのは確率分岐の中の「結果として失う」物資と、
                        //   全部が選べない時の安全弁で選んだ時だけ ── どちらも、 ある分だけ失う (文が「糧食を失った」と言うのと食い違わない)。
                        //   旧: 足りない分を HP で払っていた (物資 1 = HP 5・この効果では死なない)。
                        int need = -amount;
                        int paid = System.Math.Min(need, run.provision);
                        GameLoop.ProvisionSystem.Reduce(run, paid);
                        result.log.Add(paid == 0    ? "物資は手持ちが無く、 失わなかった"
                                     : paid < need ? $"物資-{paid} (手持ちの分だけ・現在 {run.provision}/{run.provisionCap})"
                                                   : $"物資-{paid} (現在 {run.provision}/{run.provisionCap})");
                    }
                    break;
                }

                case EventEffectType.TurnsPass:
                    result.turnsToPass += Mathf.Max(0, eff.amount);
                    result.log.Add($"{eff.amount} ターンを使う");
                    break;

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
                        ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(run, id);
                        result.log.Add($"秘宝獲得: {ResolveLabel(id)}");      // 画面に出す言い方 (データの書式「パッシブアイテム獲得」は EventParser が読むので変えない)
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
                        GameLoop.Loadout.Gain(run, eff.param);   // 武器なら 1 本だけ (入れ替えるかを聞く・2026-11-11)
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

                case EventEffectType.VescaClockTake:
                    GameLoop.VescaClock.Take(run);
                    result.log.Add("止まらない時計を預かった");
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
                            ItemSystem.Helpers.PassiveAddHelper.RemoveAt(run, idx);
                        result.log.Add($"秘宝廃棄: {eff.param}");
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
                    ExecuteProbability(eff, run, result);
                    break;

                case EventEffectType.CircusHandover:
                    ExecuteCircusHandover(run, result);
                    break;

                // ── 2026-11-12: 報酬を多様にする汎用の動詞 ──

                case EventEffectType.RevealMap:
                case EventEffectType.RevealEncounters:
                case EventEffectType.RevealSupplies:
                case EventEffectType.ShakeOffPursuer:
                case EventEffectType.PushPursuer:
                case EventEffectType.PullPursuer:
                case EventEffectType.Shortcut:
                case EventEffectType.ShiftFront:
                    // 航行の効果は地図へ戻った時に (ターン+N と同じ口)。 画面の文はここで出す (地図へ戻った直後に必ず適用されるので)
                    result.navEffects.Add(eff);
                    { string s = DescribeNav(eff, run); if (s != null) result.log.Add(s); }
                    break;

                case EventEffectType.OfferPassives:
                {
                    var cands = new List<string>();
                    for (int k = 0; k < Mathf.Max(1, eff.amount); k++)
                    {
                        string id = PickRandomItemId(ItemCategory.Passive, run, cands);
                        if (!string.IsNullOrEmpty(id) && !cands.Contains(id)) cands.Add(id);
                    }
                    if (cands.Count == 0) break;
                    var gm = GameLoop.GameManager.Instance;
                    if (gm != null)
                    {
                        // 人が遊んでいる時は地図へ戻ってからの選択画面へ預ける。 BOT は GameHooks で今選ぶ (乱数の消費は同じ)
                        string got = gm.OfferPassiveChoice(cands);
                        result.log.Add(got == null ? $"秘宝を {cands.Count} つから 1 つ選ぶ" : $"秘宝獲得: {ResolveLabel(got)}");
                    }
                    else
                    {
                        ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(run, cands[0]);
                        result.log.Add($"秘宝獲得: {ResolveLabel(cands[0])}");
                    }
                    break;
                }

                case EventEffectType.TradePassive:
                {
                    if (GameLoop.GameManager.TradeLowestPassive(run, out string given, out string got))
                        result.log.Add($"「{ResolveLabel(given)}」を渡し、「{ResolveLabel(got)}」を受け取った");
                    else
                        result.log.Add("取り替えは成り立たなかった");
                    break;
                }

                case EventEffectType.ForgeWeapon:
                {
                    int before = run.weaponMaterials;
                    if (GameLoop.GameManager.TryUpgradeWeapon(run))
                        result.log.Add($"武器を 1 段鍛えた: {ResolveLabel(run.equippedWeaponId)}{(run.weaponPlus > 0 ? "+" : "")} (素材-{before - run.weaponMaterials})");
                    else
                        result.log.Add("武器は鍛えられなかった");
                    break;
                }

                case EventEffectType.NextShopDiscount:
                {
                    // 重ねたら掛け合わせ (足し合わせて 100% を超える、 を上限で止めない形)。 小数は float へ確定させて持つ (Mono の精度)
                    float f = 1f - Mathf.Clamp(eff.amount, 0, 100) / 100f;
                    run.nextShopPriceMul *= f;
                    result.log.Add($"次の店の値段 -{eff.amount}%");
                    break;
                }

                case EventEffectType.NextRestBonus:
                    run.nextRestHealBonusPct += Mathf.Max(0, eff.amount);
                    result.log.Add($"次の休憩の回復 +{eff.amount}%（最大 HP の）");
                    break;
            }
        }

        /// <summary>航行の効果の画面の文 (選んだ直後の結果に並べる)。
        /// <para>2026-11-13: 起きない効果 (追跡者の居ない層の「追跡者を呼ぶ」など) は、 起きない理由を 1 行出す (「この層に追跡者は居ない」)。
        /// 選べるかの判定 (<c>EventChoice.UnavailableReason</c>) と同じ規則・同じ文 (<see cref="EventEffectRoles.NotHappeningReason"/>)。
        /// 代償だけが起きない選択肢・得の一部だけが起きない選択肢は選べるので、 ここで起きなかったことを見せる。
        /// 本体のランでない (航行を判定できない) 時は、 地図が無ければ何も書かない (これまでどおり)。</para></summary>
        private static string DescribeNav(EventEffect eff, RunState run)
        {
            string not = EventEffectRoles.NotHappeningReason(eff, EventChoice.Situation(run, false, false));
            if (not != null) return not;
            var sim = GameLoop.GameManager.Instance != null ? GameLoop.GameManager.Instance.FreeMap : null;
            if (sim == null) return null;
            // 縄張りを出ない番人 (既定) は、 縄張りの中の道だけで動く (FreeMapSim.PushFoe / PullFoe・縁で止まる)
            string leash = sim.Leashed ? "縄張りの中で " : "";
            switch (eff.type)
            {
                case EventEffectType.RevealMap:        return $"周りの {eff.amount} 歩先まで、点の種類が分かる";
                case EventEffectType.RevealEncounters: return $"周りの {eff.amount} 歩先まで、戦闘の相手が分かる";
                case EventEffectType.RevealSupplies:   return "この層の店と休憩の場所が分かる";
                case EventEffectType.ShakeOffPursuer:  return "追跡者の目から外れる";
                case EventEffectType.PushPursuer:      return sim.Leashed ? $"追跡者が{leash}{eff.amount} 歩まで遠のく" : $"追跡者が {eff.amount} 歩遠のく";
                case EventEffectType.PullPursuer:      return sim.Leashed ? $"追跡者が{leash}{eff.amount} 歩まで近づく" : $"追跡者が {eff.amount} 歩近づく";
                case EventEffectType.Shortcut:         return $"時限バフ獲得: 近道 ({eff.amount}回)";   // 時限バフと同じ言い方 (2026-11-13・作者の決定「近道というバフに統一」)
                case EventEffectType.ShiftFront:
                    return eff.amount < 0 ? $"天候の前線が {-eff.amount} 段退く" : $"天候の前線が {eff.amount} 段迫る";
            }
            return null;
        }

        /// <summary>[廃止] サーカス団引渡し。 旅団契約システムを 2026-08-11 に削除したため、
        /// 引き渡す契約自体が存在しない。 効果種別は enum 順序維持のため残置し、 実行は無効。
        /// 起点イベント〈別れのキャラバン〉もフラグ[サーカス団同行]が立たないので発生しない。</summary>
        private static void ExecuteCircusHandover(RunState run, ExecutionResult result)
        {
            result.log.Add("→ (廃止) サーカス団引渡し");
        }

        private static void ExecuteProbability(EventEffect eff, RunState run, ExecutionResult result)
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
            if (result.pickedBranch < 0) result.pickedBranch = picked;   // 引き方はそのまま、 どれを引いたかを渡すだけ

            foreach (var sub in eff.branches[picked])
                ExecuteOne(sub, run, result);
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
                ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(run, id);
                ApplyPassiveAcquisitionBonus(run, id, result);
            }
            run.weaponMaterials += 8;
            GameLoop.GoldIncome.Gain(run, 60, "観測所の写し");
            run.playerHP = run.playerMaxHP;

            UnityEngine.Debug.Log($"[観測所] 写しの上乗せ: パッシブ{(string.IsNullOrEmpty(id) ? "なし" : ResolveLabel(id))}"
                                + " / 素材+8 / ゴールド+60 / 全回復");
        }

        private static string PickRandomItemId(ItemCategory category, GameLoop.RunState run = null, List<string> alsoExclude = null)
        {
            var db = ItemDatabase.Instance;
            if (db == null) return null;
            var pool = db.GetItemsByCategory(category);
            if (pool == null || pool.Count == 0) return null;

            var filtered = pool.FindAll(ItemSystem.Shop.EventOnlyItemFilter.IsAllowed);
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
            // 同じ提示に同じ品を並べない (パッシブアイテムをNつから選ぶ・2026-11-12)
            if (alsoExclude != null && alsoExclude.Count > 0)
            {
                var ex = filtered.FindAll(it => !alsoExclude.Contains(it.internalName));
                if (ex.Count > 0) filtered = ex;
            }

            var picked = ItemSystem.RarityWeightedPicker.Pick(filtered);
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
                case GameLoop.ConvictionSystem.IdResolve:
                {
                    int gain = GameLoop.GoldIncome.Gain(run, 1, "イベント(決意)");
                    result.log.Add($"決意の獲得ボーナス: +{gain}ゴールド");
                    break;
                }
                case GameLoop.ConvictionSystem.IdConviction:
                {
                    GameLoop.ConvictionSystem.OnFirstAcquired(run);
                    result.log.Add("根拠のない確信が芽吹いた (確信段階1)。エリート撃破毎に成長する。");
                    break;
                }
            }
        }
    }
}

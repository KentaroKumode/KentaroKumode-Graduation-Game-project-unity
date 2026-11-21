using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  軸の品 (2026-11-06 → 11-07 組み直し)。 設計は docs/GAME.md §8-7。
    //
    //  再編 (2026-10-31) の品は大半が「軸 X を軸 Y に換える」変換器で、 安い変換器を全部買うと
    //  毒・出血・臨界・主攻撃が 1 つの機関に繋がった (クリアしたランの 64% が 3 経路を同時に使っていた)。
    //  軸 (会心・出血・毒・臨界・充電・シールド) ごとに 3 種の品を置く:
    //    燃料   — 配線を払って資源を得る。
    //    ① 数を参照 — その軸の品の数 (心眼 / 血晶 / 瘴気 / 炉温 / 電流 / 堅牢) に応じて資源が増える。
    //    ②【規則】— その軸の性質を変える。 強さは同じ数で決まる (① と連動して、 深く積むほど効く)。
    //  出目 (攻撃に挿した出目 +%) はどの構成にも効くので軸にしない。
    // ============================================================

    internal static class Ax
    {
        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);
        public static int AtkDice(CombatContext ctx) => Count(ctx, MutualKeys.AttackDiceCount);
        public static int BlockDice(CombatContext ctx) => Count(ctx, MutualKeys.BlockDiceCount);
        public static int DiceTotal(CombatContext ctx) => ctx.playerDice != null ? ctx.playerDice.Length : 3;
    }

    /// <summary>軸の品の種類 (BOT の購入判断が読む・2026-11-07)。 ITT は品をランダムに配った平均で序列を作るので、
    /// 絞った構成でだけ価値が出る ① 数を参照・②【規則】は低く出る。 Super はこの表で「軸が深ければ買う」を判断する。</summary>
    public static class AxisCatalog
    {
        public enum Kind { Count, Rule }
        public static readonly System.Collections.Generic.Dictionary<string, (string axis, Kind kind)> Items
            = new System.Collections.Generic.Dictionary<string, (string, Kind)>
        {
            ["研ぎ師の見立て書"] = (AxisRules.Crit, Kind.Count),   [PinpointEye.Id] = (AxisRules.Crit, Kind.Rule),
            // 〈重ね斬りの型〉〈凝血の小瓶〉は 2026-11-20 (段 1-a) に【規則】の品でなくなったので、 表から外した (軸の普通の品として買う)
            ["腐れ沼の苔玉"] = (AxisRules.Poison, Kind.Rule),
            // 追加 34 品のうち軸の品の数を読む品 (AxisItemEffects2.cs)
            ["千里眼の水晶"] = (AxisRules.Crit, Kind.Count),
            // 采配 (山札の軸・2026-11-20): ① 采配を読む・②【規則】
            [GameLoop.ItemIds.DeckBroom] = (AxisRules.Deck, Kind.Count), [GameLoop.ItemIds.DeckSeamless] = (AxisRules.Deck, Kind.Count),
            [GameLoop.ItemIds.DeckCrucible] = (AxisRules.Deck, Kind.Rule), [GameLoop.ItemIds.DeckBlueprint] = (AxisRules.Deck, Kind.Rule),
            // 賽目 (役の軸・2026-11-21): ① 賽目を読む・②【規則】
            [GameLoop.ItemIds.RoleNotchBoard] = (AxisRules.Role, Kind.Count), [GameLoop.ItemIds.RoleChipped] = (AxisRules.Role, Kind.Count),
            [GameLoop.ItemIds.RoleMirror] = (AxisRules.Role, Kind.Rule), [GameLoop.ItemIds.RoleOmni] = (AxisRules.Role, Kind.Rule),
        };
    }

    // ============================================================
    //  会心 (心眼)
    // ============================================================

    /// <summary>鷹匠の革手袋 (燃料・2026-11-20 作り直し): 攻撃の端子に 4 以上の目を挿したターン、 会心率 +20%・会心倍率 +0.30。
    /// 旧: 条件なしで会心率 +15%・会心倍率 +0.25 (毎ターン必ず働く形だった)。</summary>
    public class FalconerGlove : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "鷹匠の革手袋";
        public const int MinFace = 4;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Ax.Count(ctx, MutualKeys.AttackDiceMax) >= MinFace);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax.Count(ctx, MutualKeys.AttackDiceMax) < MinFace) return;
            if (t == PassiveSkillTrigger.OnWired) ctx.critRatePctAdd += 0.20f;
            else ctx.criticalMultiplier += 0.30f;   // 会心倍率は撃ごとに戻るので、 撃ごとに足す
        }
    }

    // 〈研ぎ師の見立て書〉(①: 心眼 1 につき会心倍率 +0.15) は 2026-11-11 に items.json の stats へ移した (段 3・per=axis:会心)。

    /// <summary>重ね斬りの型 (2026-11-20 作り直し): 同じターンに会心が 2 撃続いたら、 次の撃は会心倍率 +1.0 (その撃が会心した時に乗る)。
    /// 3 撃続けば 4 撃目にも乗る (直前の 2 撃が会心か で決める)。 旧【規則】: 会心率が 100% を超えた分の確率で、 会心がもう一度重なる (会心倍率 +1.0)。</summary>
    public class LayeredCutForm : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "重ね斬りの型";
        public const float BonusMul = 1.0f;
        /// <summary>[perTurn 相当] 続いた会心の数 / その数を数えたターンの番号 (ターンが変わったら 0 から)。</summary>
        private const string StreakKey = "ax.layeredCut.streak", TurnKey = "ax.layeredCut.turn";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Streak(ctx) >= 2);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            if (t == PassiveSkillTrigger.OnCriticalCheck)
            {
                if (Streak(ctx) >= 2) ctx.criticalMultiplier += BonusMul;   // 会心倍率は撃ごとに戻るので、 この撃だけに乗る
                return;
            }
            // OnPostDealDamage: この撃の結果で数え直す
            ctx.accumulatedValues[StreakKey] = ctx.isCritical && ctx.finalDamage > 0 ? Streak(ctx) + 1 : 0;
            ctx.accumulatedValues[TurnKey] = ctx.currentTurn;
        }

        private static int Streak(CombatContext ctx)
            => Ax.Count(ctx, TurnKey) == ctx.currentTurn ? Ax.Count(ctx, StreakKey) : 0;
    }

    /// <summary>斬り重ねの鞘: 会心を出すたび、 会心倍率 +0.25 が 1 つ積もる (2026-11-07: 旧 +0.15)。 ターン開始時に積もった分の 1/4 を失う。
    /// 2026-11-07: 減衰を足した (旧は戦闘の間ずっと積もり、 ヴェスカ 4 段で 30〜40 回分 = 倍率 +4.5〜6 になり得た)。</summary>
    public class LayeredScabbard : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "斬り重ねの鞘";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Ax.Count(ctx, Key));
        private const string Key = "ax.scabbard";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage, PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Ax.Count(ctx, Key);
            switch (t)
            {
                case PassiveSkillTrigger.OnCriticalDamage: ctx.accumulatedValues[Key] = n + 1; break;
                case PassiveSkillTrigger.OnTurnStart:      ctx.accumulatedValues[Key] = n - (n + 3) / 4; break;      // 積もった分の 1/4 を失う (端数は切り上げて減らす・2026-11-15 #320: 旧 n − n/4 は 3 から下へ減らなかった)
                default:                                   ctx.criticalMultiplier += 0.25f * n; break;
            }
        }
    }

    /// <summary>一点読みの片眼【規則】: 会心率 +30%。 代わりに会心しなかった攻撃の与ダメ −30%。
    /// 2026-11-07: 代価を「充電・臨界を封じる」から変更 ── 会心はどの軸にも重ねて使う横断の軸で、 他の軸と排他にしない。
    /// 代価は PassiveSkillManager の会心判定の後で掛ける (<see cref="ApplyCost"/> が <see cref="NonCritCutKey"/> を立てる)。</summary>
    public class PinpointEye : IPassiveSkillEffect
    {
        public const string Id = "一点読みの片眼";
        public const string NonCritCutKey = "ax.pinpoint.noncrit";
        public const int NonCritKeepPct = 70;
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critRatePctAdd += 0.30f;
        /// <summary>代価の印を<b>戦闘の情報を作った直後</b>に所持品から立てる (PassiveSkillManager.BeginCombat)。</summary>
        public static void ApplyCost(CombatContext ctx, GameLoop.RunState run)
        {
            if (ctx != null && AxisRules.Owns(run, Id)) ctx.accumulatedValues[NonCritCutKey] = 1;
        }
    }

}

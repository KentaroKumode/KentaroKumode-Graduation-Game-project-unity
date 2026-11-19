using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  軸の締めの LEGENDARY (2026-11-20)。 設計は docs/GAME.md §23-32。
    //
    //  6 軸の仕上げの品。 その軸だけの仕組みで、 安全装置を条件付きで 1 つ破るか、 ゲームの規則を書き換える。
    //  その軸を深く組まないと働かない (1 品で蹂躙にならない)。 数値はすべて仮。
    //  品の文に無い上限・回数制限は足さない (作者の方針)。
    // ============================================================

    internal static class Ax3
    {
        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);
    }

    // ============================================================
    //  会心
    // ============================================================

    /// <summary>死相の面頬: 会心したターン、 そのターンの敵の攻撃を (会心倍率 − 1) × 20% 減らす (最大 −50%)。
    /// 会心倍率は会心した時の <see cref="CombatContext.criticalMultiplier"/>。 減らすのは敵の攻撃の解決 (CombatManager.ApplyLossDamageModifiers)。</summary>
    public class DeathMaskVisor : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "死相の面頬";
        public string SkillId => Id;
        public const float PerMul = 0.20f, MaxCut = 0.50f;
        /// <summary>会心したターンの番号 / そのターンの減らす割合 (同じターンに何度会心しても大きい方 1 つ)。</summary>
        public const string TurnKey = "ax3.deathMask.turn", CutKey = "ax3.deathMask.cut";
        /// <summary>秘宝の列の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(CutNow(ctx) > 0f);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            float cut = System.Math.Min(MaxCut, System.Math.Max(0f, (ctx.criticalMultiplier - 1f) * PerMul));
            if (Ax3.Count(ctx, TurnKey) != ctx.currentTurn) { ctx.accumulatedValues[TurnKey] = ctx.currentTurn; ctx.accumulatedValues[CutKey] = 0f; }
            if (cut > ctx.GetAccumulated(CutKey)) ctx.accumulatedValues[CutKey] = cut;
        }

        /// <summary>このターンの敵の攻撃を減らす割合 (会心していないターンは 0)。</summary>
        public static float CutNow(CombatContext ctx)
            => ctx != null && ctx.currentTurn > 0 && Ax3.Count(ctx, TurnKey) == ctx.currentTurn ? ctx.GetAccumulated(CutKey) : 0f;
    }
}

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

    /// <summary>天眼の鍔: 会心の一撃 (主攻撃) のあと、 敵の HP が「会心倍率 × 5%」(最大 HP 比) 以下なら倒す (ボスはその半分・BountyHunterHelper.IsBoss = BossIds.IsBoss)。
    /// 会心倍率はその攻撃に掛かった倍率 (<see cref="Kw.LastCritMulKey"/>)。 判定と倒すのは CombatManager (主攻撃を HP から引いた直後・<see cref="KillLine"/>)。
    /// 倒し方は〈百人切りの鉈〉と同じ (〈回帰性真理〉の CanExecuteEnemy で止まる)。</summary>
    public class HeavenEyeGuard : IPassiveSkillEffect
    {
        public const string Id = "天眼の鍔";
        public string SkillId => Id;
        public const float PctPerMul = 0.05f;
        /// <summary>[persistent] この品を持っている (CombatManager が読む)。</summary>
        public const string HeldKey = "ax3.tengan.held";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.accumulatedValues[HeldKey] = 1;

        /// <summary>この品を持っている時の、 倒せる HP の線 (これ以下なら倒す・持っていない・会心倍率が無い時は −1)。</summary>
        public static int KillLine(CombatContext ctx)
        {
            if (ctx == null || ctx.GetAccumulated(HeldKey) <= 0f || ctx.enemyMaxHP <= 0) return -1;
            float frac = ctx.GetAccumulated(Kw.LastCritMulKey) * PctPerMul;
            if (BountyHunterHelper.IsBoss(ctx)) frac /= 2f;
            return frac > 0f ? (int)(ctx.enemyMaxHP * frac) : -1;
        }
    }

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

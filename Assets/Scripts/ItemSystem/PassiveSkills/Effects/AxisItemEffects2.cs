using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // 役の軸へ移った〈負け越しの賽〉の今の効果 (full-item-rework-ledger §⑧ の効果は Phase 3e で RoleEffects.cs へ)。

    internal static class Ax2
    {
        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);
    }

    /// <summary>負け越しの賽: 会心が出なかった攻撃のたび会心率 +15% (会心が出ると 0 に戻る・2026-11-07: 旧 +10%)。</summary>
    public class LosingStreakDie : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "負け越しの賽";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Ax2.Count(ctx, Key));
        private const string Key = "ax2.streak";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired) { ctx.critRatePctAdd += 0.15f * Ax2.Count(ctx, Key); return; }
            if (!Rw.PlayerHit(ctx)) return;
            if (ctx.isCritical) ctx.accumulatedValues[Key] = 0;
            else Kw.Add(ctx, Key, 1);
        }
    }

}

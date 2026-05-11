using System.Collections.Generic;
using EventSystem.TimedEffects;
using GameLoop;
using InventorySystem.PassiveSkills;

namespace InventorySystem.PassiveItems
{
    /// <summary>
    /// 名前付き固有パッシブアイテムの効果適用オーケストレータ。
    /// CombatManager のフックから呼ばれ、Run.ownedPassiveItems を走査して該当する
    /// 永続効果を Apply する（チャージ消費なし）。
    /// </summary>
    public static class PassiveItemManager
    {
        public static void OnCombatStart(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
            => Apply(TimedEffectTrigger.CombatStart, ctx, run, combat);

        public static void OnRoll(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
            => Apply(TimedEffectTrigger.OnRoll, ctx, run, combat);

        public static void OnTurnEnd(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
            => Apply(TimedEffectTrigger.OnTurnEnd, ctx, run, combat);

        public static void OnCombatEnd(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
            => Apply(TimedEffectTrigger.CombatEnd, ctx, run, combat);

        public static void OnMapMove(RunState run)
            => Apply(TimedEffectTrigger.OnMapMove, null, run, null);

        private static void Apply(TimedEffectTrigger trigger, CombatContext ctx, RunState run,
            CombatSystem.CombatManager combat)
        {
            if (run == null || run.ownedPassiveItems == null) return;
            PassiveItemRegistry.EnsureInitialized();

            // 同じアイテムを複数所持している可能性に対応（List なので重複あり得る）
            // 効果はそのまま重複適用される（複数回 Apply）
            var items = new List<string>(run.ownedPassiveItems);
            foreach (var id in items)
            {
                var effect = PassiveItemRegistry.Get(id);
                if (effect == null) continue;
                if (effect.Trigger != trigger) continue;
                effect.Apply(ctx, run, combat);
            }
        }
    }
}

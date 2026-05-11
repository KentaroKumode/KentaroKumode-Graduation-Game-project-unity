using UnityEngine;
using GameLoop;
using InventorySystem.PassiveSkills;

namespace EventSystem.TimedEffects.Effects
{
    /// <summary>
    /// 呪いの渇き（時限デバフ）: 戦闘開始時、HP回復効果を半減するフラグを立てる。
    /// 実適用は CombatManager.HealPlayer の呼び出し時に ctx.healHalved を参照。
    /// </summary>
    public class CursedThirstEffect : ITimedEffect
    {
        public string Id => "呪いの渇き";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatStart;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null) return;
            ctx.healHalved = true;
            Debug.Log("[TimedEffect] 呪いの渇き: HP回復半減");
        }
    }

    /// <summary>
    /// 亡者の招待（時限デバフ）: 戦闘開始時、被ダメージ+30% フラグを立てる。
    /// 実適用は CombatManager のダメージ処理で ctx.receivedDamageBonus を参照。
    /// </summary>
    public class DeadInvitationEffect : ITimedEffect
    {
        public string Id => "亡者の招待";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatStart;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null) return;
            ctx.receivedDamageBonus = 0.3f;
            Debug.Log("[TimedEffect] 亡者の招待: 被ダメ+30%");
        }
    }
}

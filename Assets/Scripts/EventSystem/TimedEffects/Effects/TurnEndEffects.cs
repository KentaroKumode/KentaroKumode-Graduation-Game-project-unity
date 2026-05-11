using UnityEngine;
using GameLoop;
using InventorySystem.PassiveSkills;

namespace EventSystem.TimedEffects.Effects
{
    /// <summary>
    /// 中毒（時限デバフ）: ターン終了時、HP-2。2戦闘持続。
    /// </summary>
    public class PoisonEffect : ITimedEffect
    {
        public string Id => "中毒";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnTurnEnd;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (combat == null) return;
            int dmg = 2;
            combat.DamagePlayerDirect(dmg);
            Debug.Log($"[TimedEffect] 中毒: HP-{dmg}");
        }
    }
}

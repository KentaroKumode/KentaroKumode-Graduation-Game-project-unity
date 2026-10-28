using UnityEngine;
using GameLoop;
using ItemSystem.PassiveSkills;
using MapSystem;

namespace EventSystem.TimedEffects.Effects
{
    /// <summary>
    /// 泉の祝福: 戦闘終了時、プレイヤーHPを最大値まで回復。
    /// </summary>
    public class SpringBlessingEffect : ITimedEffect
    {
        public string Id => "泉の祝福";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (combat == null) return;
            combat.HealPlayerToFull();
            Debug.Log("[TimedEffect] 泉の祝福: HP全回復");
        }
    }

    /// <summary>
    /// 芽吹きの祈り: 戦闘終了時、空腹度+1。
    /// </summary>
    public class SproutPrayerEffect : ITimedEffect
    {
        public string Id => "芽吹きの祈り";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            // 2026-10-17: 空腹の削除に伴い、 旧「空腹度 +1」を物資 +10 へ（物資は旧単位の ×10・GAME.md §10）
            if (run == null) return;
            GameLoop.ProvisionSystem.Recover(run, 10);
            Debug.Log($"[TimedEffect] 芽吹きの祈り: 物資+10 ({run.provision}/{run.provisionCap})");
        }
    }
}

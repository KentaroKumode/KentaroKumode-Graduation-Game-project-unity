using GameLoop;
using InventorySystem.PassiveSkills;

namespace EventSystem.TimedEffects
{
    /// <summary>
    /// 時限バフ・デバフ効果の単一実装。
    /// </summary>
    public interface ITimedEffect
    {
        /// <summary>バフID（イベントテキストの[]内表記と一致）</summary>
        string Id { get; }

        /// <summary>このバフが効果を発揮するタイミング</summary>
        TimedEffectTrigger Trigger { get; }

        /// <summary>
        /// 効果を適用する。CombatContext は CombatStart/OnRoll/OnTurnEnd/CombatEnd で非null、
        /// OnMapMove では null。
        /// </summary>
        void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat);
    }
}

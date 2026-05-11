using UnityEngine;
using GameLoop;
using InventorySystem.PassiveSkills;

namespace EventSystem.TimedEffects.Effects
{
    /// <summary>
    /// 解放者: 戦闘開始時、最大HPの10%分を上乗せして開始（実質一時的にHP+α）。
    /// </summary>
    public class LiberatorEffect : ITimedEffect
    {
        public string Id => "解放者";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatStart;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (combat == null) return;
            int bonus = Mathf.Max(1, Mathf.CeilToInt(combat.PlayerMaxHP * 0.1f));
            combat.GrantTemporaryHpBonus(bonus);
            Debug.Log($"[TimedEffect] 解放者: HP +{bonus}");
        }
    }

    /// <summary>
    /// 共助: 戦闘開始時、敵の最初の攻撃ダメージを半減するフラグを立てる。
    /// 実適用は CombatManager のダメージ計算で参照（ctx.halveFirstEnemyAttack）。
    /// </summary>
    public class MutualAidEffect : ITimedEffect
    {
        public string Id => "共助";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatStart;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null) return;
            ctx.halveFirstEnemyAttack = true;
            Debug.Log("[TimedEffect] 共助: 敵の最初の攻撃を半減");
        }
    }

    /// <summary>
    /// 獣の絆: 戦闘開始時、被弾を1回無効化するチャージを与える。
    /// 実適用は CombatManager のダメージ計算（ctx.playerDamageNegateCharges）。
    /// </summary>
    public class BeastBondEffect : ITimedEffect
    {
        public string Id => "獣の絆";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatStart;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null) return;
            ctx.playerDamageNegateCharges += 1;
            Debug.Log("[TimedEffect] 獣の絆: 被弾無効チャージ+1");
        }
    }

    /// <summary>
    /// 獣の恩義: 戦闘開始時、敵の最初のロールを無効化するフラグを立てる。
    /// 実適用は CombatManager のロール解決時（ctx.nullifyFirstEnemyRoll）。
    /// </summary>
    public class BeastFavorEffect : ITimedEffect
    {
        public string Id => "獣の恩義";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatStart;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null) return;
            ctx.nullifyFirstEnemyRoll = true;
            Debug.Log("[TimedEffect] 獣の恩義: 敵の最初のロールを無効化");
        }
    }

    /// <summary>
    /// 使命感: 戦闘開始時、その戦闘のプレイヤーダイス数を+1する。
    /// </summary>
    public class MissionEffect : ITimedEffect
    {
        public string Id => "使命感";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatStart;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (combat == null) return;
            combat.GrantTemporaryDiceCountBonus(1);
            Debug.Log("[TimedEffect] 使命感: ダイス+1");
        }
    }
}

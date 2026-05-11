using UnityEngine;
using GameLoop;
using InventorySystem.PassiveSkills;

namespace EventSystem.TimedEffects.Effects
{
    /// <summary>
    /// 星の加護: 戦闘1ターン目の最初のプレイヤーダイスを最大値に固定する。
    /// </summary>
    public class StarBlessingEffect : ITimedEffect
    {
        public string Id => "星の加護";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || ctx.playerDice == null || ctx.playerDice.Length == 0) return;
            if (ctx.currentTurn != 1) return;
            int original = ctx.playerDice[0];
            ctx.playerDice[0] = ctx.playerDiceMax;
            Debug.Log($"[TimedEffect] 星の加護: 最初のダイスを最大値に固定 ({original}→{ctx.playerDiceMax})");
        }
    }

    /// <summary>
    /// 啓示: 全てのプレイヤーダイス目を+1（最大値でクランプ）。3戦闘持続。
    /// </summary>
    public class RevelationEffect : ITimedEffect
    {
        public string Id => "啓示";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || ctx.playerDice == null) return;
            for (int i = 0; i < ctx.playerDice.Length; i++)
                ctx.playerDice[i] = Mathf.Min(ctx.playerDiceMax, ctx.playerDice[i] + 1);
        }
    }

    /// <summary>
    /// 感傷（時限デバフ）: 全てのプレイヤーダイス目を-1（最小1でクランプ）。
    /// </summary>
    public class SentimentEffect : ITimedEffect
    {
        public string Id => "感傷";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || ctx.playerDice == null) return;
            for (int i = 0; i < ctx.playerDice.Length; i++)
                ctx.playerDice[i] = Mathf.Max(1, ctx.playerDice[i] - 1);
        }
    }

    /// <summary>
    /// 時の凝視: 戦闘1ターン目、最低出目のプレイヤーダイスを最大値に置換（実質ベスト1個振り直し）。
    /// </summary>
    public class TimeGazeEffect : ITimedEffect
    {
        public string Id => "時の凝視";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || ctx.playerDice == null || ctx.playerDice.Length == 0) return;
            if (ctx.currentTurn != 1) return;

            int minIdx = 0;
            for (int i = 1; i < ctx.playerDice.Length; i++)
                if (ctx.playerDice[i] < ctx.playerDice[minIdx]) minIdx = i;

            int original = ctx.playerDice[minIdx];
            if (original < ctx.playerDiceMax)
            {
                ctx.playerDice[minIdx] = ctx.playerDiceMax;
                Debug.Log($"[TimedEffect] 時の凝視: ダイス[{minIdx}] {original}→{ctx.playerDiceMax}");
            }
        }
    }
}

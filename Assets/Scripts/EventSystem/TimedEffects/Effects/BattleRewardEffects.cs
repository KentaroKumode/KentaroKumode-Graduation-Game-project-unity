using GameLoop;
using ItemSystem.PassiveSkills;

namespace EventSystem.TimedEffects.Effects
{
    /// <summary>
    /// 〈徒労〉(時限デバフ・2026-11-13・docs/GAME.md §17): 深さ 6 の〈灰に埋もれた基地〉で受ける。 <see cref="BattleBoonRule.ToilCharges"/> つ持った状態になり、
    /// <b>通常の戦闘に勝つたびに 1 つ減り、 その戦闘では何も得られない</b>。 規則と数値の正本は <see cref="BattleBoonRule"/>。
    /// <para>効果そのもの (報酬を 0 にする) は報酬を配る側 (GameManager.TakeNormalBattleBoon) が減らす前の残りを見て決める。
    /// ここの <see cref="Apply"/> は何もしない (減らすのは <see cref="TimedEffectManager.OnNormalBattleWon"/>)。</para>
    /// </summary>
    public class ToilDebuffEffect : ITimedEffect
    {
        public string Id => BattleBoonRule.ToilName;
        public TimedEffectTrigger Trigger => TimedEffectTrigger.NormalBattleWon;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat) { }
    }

    /// <summary>
    /// 〈開眼〉(時限バフ・2026-11-13・docs/GAME.md §17): 〈徒労〉の対。 〈灰に埋もれた基地・再訪〉で手記の最後の頁を開くと得る。
    /// <see cref="BattleBoonRule.InsightCharges"/> つ持った状態になり、 <b>通常の戦闘に勝つたびに 1 つ減り、 その戦闘で得るゴールドとドロップと強化素材が
    /// <see cref="BattleBoonRule.InsightMultiplier"/> 倍</b>。 隠し倍率ではない ── 名前と説明を画面に出し、 報酬の結果の行にも出す。 規則と数値の正本は <see cref="BattleBoonRule"/>。
    /// </summary>
    public class InsightBuffEffect : ITimedEffect
    {
        public string Id => BattleBoonRule.InsightName;
        public TimedEffectTrigger Trigger => TimedEffectTrigger.NormalBattleWon;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat) { }
    }
}

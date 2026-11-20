using GameLoop;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  采配 (山札の軸) の品のうち、 出来事で書く品 (2026-11-20・段 1-e・item-overhaul-spec §4 / §12-4・名前は仮・数値は仮)。
    //  ここに置くのは端子の解決の外の効果 (敵へのダメージ・次の撃の会心率) だけ。 端子の出力・手札・山札を変える 16 品は効果クラスを持たない
    //  (CombatSystem.TerminalCards の DeckItems の旗・TerminalCards.DeckItemsOf / CombatManager.BeginTerminalDeck が所持を見る)。
    //  品の文に無い上限・回数制限は足さない (作者の方針)。
    // ============================================================

    /// <summary>焼き入れの挟み: 使い捨ての端子を使うたび (OnTerminalDispose)、 敵に 4 + 采配 の軽減不可ダメージ。
    /// 鳴るのは配線の解決の直後 (ターンの解決中) なので、 このターンの攻撃の固定ダメの枠で通る (Kw.DamageEnemy)。</summary>
    public class TemperTongs : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.DeckTongs;
        public const int Base = 4;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTerminalDispose };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int dmg = Base + ctx.AxisDepth(AxisRules.Deck);
            Kw.DamageEnemy(ctx, dmg);
            Rw.Log($"[焼き入れの挟み] 使い捨てを使った → 軽減不可 {dmg}");
        }
    }

    /// <summary>灰払いの箒: 使い捨ての端子を使うたび、 次の撃の会心率 +采配 × 5% (使った数だけ足し合わせて、 次の 1 撃で使い切る)。
    /// 使い捨ては配線の解決の直後に鳴るので、 攻撃するターンならこのターンの最初の撃に乗る (攻撃しなければ次に攻撃するターンの最初の撃)。
    /// 会心率は撃ごとに戻るので (CombatManager の撃の輪)、 足した分はその 1 撃だけに効く。</summary>
    public class AshBroom : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.DeckBroom;
        public const float PctPerDepth = 0.05f;
        /// <summary>[persistent] 次の撃に足す会心率 (0.05 = 5%)。</summary>
        private const string Key = "deck.broom";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx != null && ctx.GetAccumulated(Key) > 0f);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTerminalDispose, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnTerminalDispose)
            {
                float add = PctPerDepth * ctx.AxisDepth(AxisRules.Deck);
                if (add <= 0f) return;
                ctx.accumulatedValues[Key] = ctx.GetAccumulated(Key) + add;
                Rw.Log($"[灰払いの箒] 使い捨てを使った → 次の撃の会心率 +{add * 100f:0}% (計 {ctx.GetAccumulated(Key) * 100f:0}%)");
                return;
            }
            if (!ctx.IsRealHit) return;      // 攻撃しないターンの、 固定ダメの枠を取り出すだけの 1 回は撃ではない
            float pct = ctx.GetAccumulated(Key);
            if (pct <= 0f) return;
            ctx.critRatePctAdd += pct;
            ctx.accumulatedValues[Key] = 0f;
        }
    }
}

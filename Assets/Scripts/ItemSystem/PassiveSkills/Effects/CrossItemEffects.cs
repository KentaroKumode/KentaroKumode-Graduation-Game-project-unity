using GameLoop;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  クロス軸の 6 品 (2026-11-21・段 1-f・item-overhaul-spec §6-1 / §12-8・名前は仮・数値は仮)。
    //  どれも「その品の軸の偏重が 5 以上」の時だけ働き、 別の軸の資源や出来事と掛け合わせる (安い変換器の何でも買いを防ぐ)。
    //  組: 毒×臨界・出血×采配・充電×賽目・会心×采配・賽目×出血・毒×充電。 〈城塞の黒煙〉(シールド×臨界) は入れない (§12-8)。
    //  品の文に無い上限・回数制限は足さない (作者の方針)。
    // ============================================================

    internal static class Cross
    {
        /// <summary>働くのに要る、 品の軸の偏重。</summary>
        public const int Need = 5;
        public static bool On(CombatContext ctx, string axis) => ctx.AxisDepth(axis) >= Need;
    }

    /// <summary>劇毒の火薬庫 (毒): 瘴気 5 以上の時、 臨界が爆発するたび、 敵の毒 + 爆発のダメージ ÷ 10 (切り捨て・<see cref="Kw.BurstDamageNowKey"/>)、
    /// その場で毒のダメージを 1 回 (ターン開始の毒と同じ量・出したら毒が減る・<see cref="CombatContext.TickPoisonOnce"/>・Kw.DamageEnemy で入れる)。</summary>
    public class VenomPowderStore : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CrossVenomPowder;
        public const int Div = 10;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRinkaiBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Cross.On(ctx, AxisRules.Poison)) return;
            int add = (int)ctx.GetAccumulated(Kw.BurstDamageNowKey) / Div;
            Rw.Poison(ctx, add);
            int pd = ctx.TickPoisonOnce();
            if (pd > 0) Kw.DamageEnemy(ctx, pd);
            Rw.Log($"[劇毒の火薬庫] 爆発 → 毒 +{add}・毒のダメージ {pd}");
        }
    }

    /// <summary>血染めの歯車 (出血): 血晶 5 以上の時、 使い捨ての端子を使うたび、 敵に 出血 × 2 の軽減不可ダメージ (出血は使わない)、 そのあと出血 +采配。</summary>
    public class BloodstainedGear : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CrossBloodGear;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTerminalDispose };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Cross.On(ctx, AxisRules.Bleed)) return;
            int dmg = ctx.enemyBleedStacks * 2;
            Kw.DamageEnemy(ctx, dmg);
            ctx.AddEnemyBleed(ctx.AxisDepth(AxisRules.Deck));
            Rw.Log($"[血染めの歯車] 使い捨て → 軽減不可 {dmg}・出血 +{ctx.AxisDepth(AxisRules.Deck)}");
        }
    }

    /// <summary>雷鳴のゾロ目 (充電): 電流 5 以上の時、 役が成立するたび充電 +賽目 × 2。 ゾロ目なら、 充電を払わずに (足した後の) 充電 × 2 の軽減不可ダメージ。</summary>
    public class ThunderTriple : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CrossThunderDice;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRoleFired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Cross.On(ctx, AxisRules.Charge)) return;
            ctx.AddCharge(ctx.AxisDepth(AxisRules.Role) * 2);
            var k = RoleFx.Last(ctx);
            if (k == null || !CombatSystem.YachtRoles.IsTriple(k.Value)) return;
            int dmg = ctx.GetCharge() * 2;
            Kw.DamageEnemy(ctx, dmg);
            Rw.Log($"[雷鳴のゾロ目] ゾロ目 → 充電 {ctx.GetCharge()} × 2 = 軽減不可 {dmg}");
        }
    }

    /// <summary>必殺の照準器 (会心): 心眼 5 以上の時、 使い捨ての端子を使ったターン、 そのターンの攻撃はすべて会心 (forceCritical)・会心倍率 +采配 × 0.15
    /// (そのターンに何枚使っても 1 回)。 使い捨ての出来事は OnWired の前に鳴るので、 このターンの撃に乗る。</summary>
    public class FatalSight : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CrossSniper;
        public const float MulPerDepth = 0.15f;
        private const string TurnKey = "cross.sniper.turn";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTerminalDispose };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Cross.On(ctx, AxisRules.Crit) || !Rw.OncePerTurn(ctx, TurnKey)) return;
            ctx.forceCritical = true;
            ctx.criticalMultiplier += MulPerDepth * ctx.AxisDepth(AxisRules.Deck);
            Rw.Log($"[必殺の照準器] 使い捨て → このターンの攻撃は会心・会心倍率 +{MulPerDepth * ctx.AxisDepth(AxisRules.Deck):0.##}");
        }
    }

    /// <summary>血濡れの符丁 (賽目): 賽目 5 以上の時、 役〈追撃〉か〈攻め〉(3・4 の 2 個そろい・ゾロ目) が成立したターン、 敵の出血を使わずに 出血 × 2 の軽減不可ダメージ
    /// (ターンの解決中なので、 このターンの攻撃の固定ダメの枠で入る・Kw.DamageEnemy)。</summary>
    public class BloodCipher : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CrossBloodCode;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRoleFired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Cross.On(ctx, AxisRules.Role)) return;
            var k = RoleFx.Last(ctx);
            if (k == null || k.Value == CombatSystem.RoleKind.Straight) return;
            int face = CombatSystem.YachtRoles.FaceOf(k.Value);
            if (face != 3 && face != 4) return;
            int dmg = ctx.enemyBleedStacks * 2;
            Kw.DamageEnemy(ctx, dmg);
            Rw.Log($"[血濡れの符丁] 〈{CombatSystem.YachtRoles.NameOf(k.Value)}〉 → 出血 {ctx.enemyBleedStacks} × 2 = 軽減不可 {dmg}");
        }
    }

    /// <summary>雷毒の導管 (毒・§12-8 の毒×充電の橋): 瘴気 5 以上の時、 充電を払うたび (振り直しも・OnChargeSpend)、 払った量の半分 (切り捨て) だけ敵に毒。</summary>
    public class VenomConduit : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CrossVenomConduit;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnChargeSpend };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Cross.On(ctx, AxisRules.Poison)) return;
            int add = Kw.EventAmount(ctx) / 2;
            if (add <= 0) return;
            Rw.Poison(ctx, add);
            Rw.Log($"[雷毒の導管] 充電 −{Kw.EventAmount(ctx)} → 毒 +{add}");
        }
    }
}

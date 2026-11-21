using GameLoop;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  戦術の 5 品 (2026-11-21・段 1-f・item-overhaul-spec §6-2・名前は仮・数値は仮・〈静寂の構え〉は ShieldEffects.cs)。
    //  一撃の重さ・ボスのダウン・大技の周期・エスカレーションを読む品 (引きの悪い回を知識で乗り切る側)。
    //  〈逆境の砂時計〉の毎ターンのシールドは +8 → +4 (毎ターン働く守りは小さく・作者の方針)。
    //  品の文に無い上限・回数制限は足さない (作者の方針)。
    // ============================================================

    /// <summary>渾身の柄頭 (会心): 攻撃の端子を 1 枚だけ挿したターン (撃が 1 つ)、 その 1 撃の最終与ダメージ +25% (ctx.hitFinalDamagePct)・敵の防御を 8 無視 (貫通に足す)。</summary>
    public class HeftyPommel : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.TacPommel;
        public const int FinalPct = 25, Pen = 8;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit || ctx.hitCount != 1) return;
            ctx.hitFinalDamagePct += FinalPct;
            ctx.playerArmorPen += Pen;      // 撃は 1 つなので、 このターンの貫通に足してよい (BeginNewTurn で戻る)
        }
    }

    /// <summary>隙衝きの短槍 (軸なし): 敵がチャージ中 (大技の前で攻撃しない) かダウン中のターン、 攻撃の与ダメージ +40% (与ダメ % の池・撃ごとに乗る)。</summary>
    public class GapSpear : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.TacGapSpear;
        public const float Pct = 0.40f;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var cm = CombatSystem.CombatManager.Instance;
            if (cm == null || !cm.EnemyChargingOrDownNow(ctx.currentTurn)) return;
            ctx.AddOutgoingPct(Pct);
            Rw.Log("[隙衝きの短槍] 敵がチャージ中かダウン中 → 与ダメージ +40%");
        }
    }

    /// <summary>崩しの一手 (軸なし): ボスの目盛りを割ってダウンさせたターン (通常攻撃でもひびでも・1 ターンに 1 回)、 次のターンの手札 +2 枚・充電 +10 (すぐ足す)。</summary>
    public class BreakingMove : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.TacBreak;
        public const int Draw = 2, Charge = 10;
        private const string TurnKey = "tac.break.turn";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBossDown };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Rw.OncePerTurn(ctx, TurnKey)) return;
            Rw.DrawNext(Draw);
            ctx.AddCharge(Charge);
            Rw.Log("[崩しの一手] 目盛りを割った → 次のターン 手札 +2・充電 +10");
        }
    }

    /// <summary>大技殺しの楔 (軸なし): ダウンか〈封じ〉で敵の大技を取り消したとき (OnHeavyCanceled)、 敵に最大 HP の 10% の軽減不可ダメージ・シールド +15。</summary>
    public class HeavyBreakerWedge : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.TacWedge;
        public const int MaxHpPct = 10, ShieldAdd = 15;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnHeavyCanceled };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int dmg = (int)((long)ctx.enemyMaxHP * MaxHpPct / 100);
            Kw.DamageEnemy(ctx, dmg);
            Rw.Shield(ctx, ShieldAdd, "大技殺しの楔");
            Rw.Log($"[大技殺しの楔] 大技を取り消した → 軽減不可 {dmg}・シールド +{ShieldAdd}");
        }
    }

    /// <summary>逆境の砂時計 (軸なし): エスカレーションの段階が 2 以上の間、 ターン開始時にシールド +4・攻撃の与ダメージ +25% (与ダメ % の池)。
    /// 段階は〈鎮静香〉の遅延と段階の戻しを差し引いた今の段階 (CombatManager.EscalationStageNow・予告に出る段階)。</summary>
    public class AdversityHourglass : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.TacHourglass;
        public const int NeedStage = 2, ShieldAdd = 4;
        public const float Pct = 0.25f;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var cm = CombatSystem.CombatManager.Instance;
            if (cm == null || cm.EscalationStageNow(ctx) < NeedStage) return;
            if (t == PassiveSkillTrigger.OnTurnStart) Rw.Shield(ctx, ShieldAdd, "逆境の砂時計");
            else ctx.AddOutgoingPct(Pct);
        }
    }
}

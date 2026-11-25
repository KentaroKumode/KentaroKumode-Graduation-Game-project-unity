using CombatSystem;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  出血の軸 26 品 (2026-11-21・full-item-rework-ledger §④・docs/GAME.md §7-2)。
    //  撃 (攻撃の端子 1 枚 = 1 撃・ADR-0016) を重ねるほど出血が増え、 出血の多い敵を殴るほど 1 撃が重くなる。
    //  撃ごとに出血を与える 10 品と撃ごとの攻撃 +N の 3 品 (鉛入りの握斧・血日に澄む佩玉・医家の反り刃・返し刃の留め金・欠け刃のナイフ・
    //  裂け目の楔・鋸刃の山刀・先血の腕輪の最初の撃・血止め知らずの針箱・医書裏の開き針の攻撃・激情の刃・百人切りの鉈) は
    //  CombatSystem.BleedCore.ApplyToTerminals (Resolve の 5f・本体・画面の見込み・BOT が同じ)。 〈百人切りの鉈〉の処刑は本体の撃の輪 (BleedCore.CleaverKillLine)。
    //  出血のダメージは毎ターンの終わり (CombatManager.Turn・出血の量そのまま)・減り方は 1 + 出血 / (10 + 割る数の足し分) (AxisRules.BleedDecay)。
    // ============================================================

    // ── B: 起点 ── (〈鉛入りの握斧〉〈血日に澄む佩玉〉〈医家の反り刃〉〈返し刃の留め金〉〈欠け刃のナイフ〉〈裂け目の楔〉は端子の解決の中だけ・BleedCore の旗)

    /// <summary>棘付きの籠手: ブロックで敵の攻撃を受けきったターン (ブロック ≥ 敵の攻撃・HP を 1 も失わない)、 敵に出血 +3・次のターンの最初の 1 撃の攻撃 +4。</summary>
    public class ThornGlove : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "棘付きの籠手";
        public const int Bleed = 3, Atk = 4;
        /// <summary>[persistent] 受けきったターンの番号 (次のターンに読む)。</summary>
        private const string Key = "bld.thornGlove";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Shd.LastTurn(ctx, Key));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnTurnStart) { if (Shd.LastTurn(ctx, Key)) ctx.firstHitAttackBonus += Atk; return; }
            if (!Psn.Shutout(ctx) || ctx.blockSumThisTurn < Rw.Count(ctx, MutualKeys.EnemyAtk)) return;
            ctx.AddEnemyBleed(Bleed);
            ctx.accumulatedValues[Key] = ctx.currentTurn;
        }
    }

    /// <summary>血の滴る刃先: 敵の出血が 8 以上の間、 攻撃が当たるたび HP +1 (1 ターン最大 +3)。</summary>
    public class DrippingEdge : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "血の滴る刃先";
        public const int Need = 8, Heal = 1, MaxPerTurn = 3;
        private const string Key = "bld.dripping";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.enemyBleedStacks >= Need);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Psn.Hit(ctx) || ctx.enemyBleedStacks < Need) return;
            int n = System.Math.Min(Heal, MaxPerTurn - Psn.TurnN(ctx, Key));
            if (n <= 0) return;
            Psn.AddTurnN(ctx, Key, n);
            Rw.Heal(ctx, n);
        }
    }

    // ── S: エンジン・安定 ── (〈鋸刃の山刀〉〈血止め知らずの針箱〉は端子の解決の中だけ)

    /// <summary>魂喰らいの血石: 敵の出血が消費か自然減衰で減るたび、 減った量と同じブロックを得る (1 ターン最大 12)。
    /// 自然減衰 (ターンの頭) の分はそのターンのブロック、 攻撃の中で消費した分 (配線の後) は次のターンのブロックに入る。</summary>
    public class SoulBloodstone : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "魂喰らいの血石";
        public const int MaxPerTurn = 12;
        /// <summary>[ターンごと] このターンに得たブロック (Psn.TurnN) / [persistent] 次のターンに入れるブロック。</summary>
        private const string Key = "bld.soul", PendingKey = "bld.soul.pending";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Psn.TurnN(ctx, Key));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBleedDecay, PassiveSkillTrigger.OnBleedConsume, PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            switch (t)
            {
                case PassiveSkillTrigger.OnBleedConsume: Kw.Add(ctx, PendingKey, Kw.EventAmount(ctx)); break;
                case PassiveSkillTrigger.OnTurnStart:
                {
                    int p = Psn.Count(ctx, PendingKey);
                    if (p <= 0) return;
                    ctx.accumulatedValues[PendingKey] = 0;
                    Give(ctx, p);
                    break;
                }
                default: Give(ctx, Kw.EventAmount(ctx)); break;
            }
        }

        private static void Give(CombatContext ctx, int n)
        {
            n = System.Math.Min(n, MaxPerTurn - Psn.TurnN(ctx, Key));
            if (n <= 0) return;
            Psn.AddTurnN(ctx, Key, n);
            ctx.itemBlockBonus += n;
        }
    }

    /// <summary>先血の腕輪: 戦闘開始時、 敵に出血 +3。 各ターン最初に攻撃が当たったとき、 敵に出血 +1 (BleedCore の旗・5f)。</summary>
    public class BloodFate : IPassiveSkillEffect
    {
        public string SkillId => GameLoop.ItemIds.BleedFirstBlood;
       public const int Opening = 3;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.AddEnemyBleed(Opening);
    }

    /// <summary>鎧縫いの針: 敵の出血 1 につき、 攻撃が敵の防御を 1 無視する (items.json の stats・per=enemyBleed)。
    /// 敵の出血が 15 以上の間、 攻撃が当たるたび、 ボスの次の目盛りがその撃のダメージの 20% だけ上がる (目盛りへの削り +20%・CombatManager.RaiseBossNextNotch)。</summary>
    public class ArmorStitchNeedle : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "鎧縫いの針";
        public const int Need = 15, NotchPct = 20;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.enemyBleedStacks >= Need);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Psn.Hit(ctx) || ctx.enemyBleedStacks < Need) return;
            int add = ctx.finalDamage * NotchPct / 100;
            if (add > 0) CombatManager.Instance?.RaiseBossNextNotch(add);
        }
    }

    /// <summary>止血禁じの軟膏: 出血の自然減衰の割る数 +15 (AxisRules.BleedDecay)。</summary>
    public class NoClotSalve : IPassiveSkillEffect
    {
        public string SkillId => "止血禁じの軟膏";
        public const int DivisorBonus = 15;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.bleedDecayDivisorBonus += DivisorBonus;
    }

    // ── G: 分岐 A〈多段〉/ 分岐 B〈回収〉 ── (〈激情の刃〉〈百人切りの鉈〉は端子の解決と本体の撃の輪だけ)

    /// <summary>逆綴じの止血帯 (分岐 A): ターンの終わり、 このターン 2 撃以上の攻撃をしていたら敵の出血 × 1 の軽減不可ダメージ。 3 撃以上なら × 2 (final5: 0〜1 撃のターンは働かない)。</summary>
    public class Wringing : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "逆綴じの止血帯";
        public const int Mul = 1, MulMulti = 2, MinHits = 2, MultiHits = 3;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.enemyBleedStacks > 0 && ctx.hitCount >= MinHits);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int b = ctx.enemyBleedStacks;
            if (b <= 0 || ctx.enemyCurrentHP <= 0 || ctx.hitCount < MinHits) return;
            int mul = ctx.hitCount >= MultiHits ? MulMulti : Mul;
            Kw.DamageEnemy(ctx, b * mul);
            Rw.Log($"[逆綴じの止血帯] 出血 {b} × {mul} = {b * mul} の軽減不可 (撃 {ctx.hitCount})");
        }
    }

    /// <summary>医書裏の開き針【規則】(分岐 A): 敵の出血の自然減衰が無くなる。 敵の出血 10 につき、 撃ごとの攻撃 +2 (配線の時点の出血・BleedCore の旗・5f)。</summary>
    public class AntiClotting : IPassiveSkillEffect
    {
        public string SkillId => GameLoop.ItemIds.BleedOpenNeedle;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.bleedDecayDisabled = true;
    }

    /// <summary>血車 (分岐 B): 秘宝の効果で敵の出血を使うたび (Kw.ConsumeBleed)、 使った量 × 2 の軽減不可ダメージ。 さらに次のターンの手札 +1 枚 (手札は 1 ターン 1 回)。</summary>
    public class BloodWheel : IPassiveSkillEffect
    {
        public string SkillId => "血車";
        public const int Mul = 2, Draw = 1;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBleedConsume };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Kw.EventAmount(ctx);
            if (n <= 0) return;
            Kw.DamageEnemy(ctx, n * Mul);
            if (Rw.OncePerTurn(ctx, "bld.wheel")) Rw.DrawNext(Draw);
        }
    }

    /// <summary>凝血の小瓶: 敵の出血が 20 以上の間、 敵の攻撃 −20% (final5: 旧 16・−25%)。 30 以上なら、 さらにボスの大技の倍率 −0.5 (大技のターンの攻撃 × (倍率 − 0.5) / 倍率・ターン開始時の出血で見る)。</summary>
    public class ClotVial : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "凝血の小瓶";
        public const int Need = 20, HeavyNeed = 30;
        public const float Cut = 0.20f, HeavyMulCut = 0.5f;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.enemyBleedStacks >= Need);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int b = ctx.enemyBleedStacks;
            if (b < Need) return;
            ctx.enemyAtkCutPct += Cut;
            if (b < HeavyNeed || BossDown.ActiveNow || !Rw.IsBoss()) return;
            var e = CombatManager.Instance?.CurrentEnemy;
            if (e == null || !Escalation.IsHeavyTurn(e, ctx.currentTurn) || e.heavyMul <= 0f) return;
            float mul = e.heavyMul + MetaProgression.MetaDebuffApplicator.GetHeavyMulBonus();
            if (mul > HeavyMulCut) ctx.enemyAtkCutPct += HeavyMulCut / mul;
        }
    }

    // ── L: 規則破壊 ──

    /// <summary>血桜 (分岐 A): 敵の出血が 12 以上のとき、 そのターンの 2 撃目以降の攻撃で敵の出血を 2 使い、 その撃の最終与ダメージ + 消費前の出血 × 2% (最大 +120%・ctx.hitFinalDamagePct)。</summary>
    public class BloodStrike : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => GameLoop.ItemIds.BleedSakura;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.enemyBleedStacks >= BleedCore.SakuraNeed);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int b = ctx.enemyBleedStacks;
            if (!ctx.IsRealHit || ctx.hitIndex < 1 || b < BleedCore.SakuraNeed) return;
            int pct = BleedCore.SakuraPct(b);
            Kw.ConsumeBleed(ctx, BleedCore.SakuraSpend);
            ctx.hitFinalDamagePct += pct;
            Rw.Log($"[血桜] {ctx.hitIndex + 1} 撃目: 出血 {b} → {ctx.enemyBleedStacks} (2 使う)・最終与ダメ +{pct}%");
        }
    }

    /// <summary>血脈の災環 (分岐 B): 敵の出血が消費か自然減衰で減るたび、 減った量 × 3 の軽減不可ダメージ (2026-11-22・旧 × 5)。 そのダメージの 20% (切り捨て) だけ HP を回復 (1 ターン最大 10)。</summary>
    public class BloodlineRing : IPassiveSkillEffect
    {
        public string SkillId => "血脈の災環";
        public const int Mul = 3, HealPct = 20, HealMax = 10;
        private const string Key = "bld.bloodline";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBleedDecay, PassiveSkillTrigger.OnBleedConsume };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int dmg = Kw.EventAmount(ctx) * Mul;
            if (dmg <= 0) return;
            Kw.DamageEnemy(ctx, dmg);
            int heal = System.Math.Min(dmg * HealPct / 100, HealMax - Psn.TurnN(ctx, Key));
            if (heal <= 0) return;
            Psn.AddTurnN(ctx, Key, heal);
            Rw.Heal(ctx, heal);
        }
    }

    /// <summary>紅の搾り具 (分岐 B): 敵の出血が 15 以上の敵に攻撃が当たったとき、 出血の 1/3 (切り上げ) を使い、 使った量 × 8 の軽減不可ダメージ・ボスの次の目盛りをそのダメージの 100% ぶん上げる
    /// (1 ターン 1 回・2026-11-25 neon answer_gauge・旧: ひびを入れる)。</summary>
    public class CrimsonPress : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => GameLoop.ItemIds.BleedPress;
        /// <summary>[persistent] 搾ったターン (Rw.OncePerTurn・BOT の見積もりも読む)。</summary>
        public const string TurnKey = "bld.press";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.enemyBleedStacks >= BleedCore.PressNeed);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int b = ctx.enemyBleedStacks;
            if (!Psn.Hit(ctx) || b < BleedCore.PressNeed || !Rw.OncePerTurn(ctx, TurnKey)) return;
            int used = Kw.ConsumeBleed(ctx, BleedCore.PressSpend(b));
            if (used <= 0) return;
            Kw.DamageEnemy(ctx, used * BleedCore.PressMul);
            CombatManager.Instance?.RaiseBossNextNotch(used * BleedCore.PressMul);
            Rw.Log($"[紅の搾り具] 出血 {b} → {ctx.enemyBleedStacks} (使う {used}) → {used * BleedCore.PressMul} の軽減不可");
        }
    }

    /// <summary>涸れぬ血晶【規則】(分岐 A): 敵の出血が既に 5 以上ある時、 敵へ与える出血がすべて 2 倍 (ctx.bleedDouble・CombatContext.AddEnemyBleed)。</summary>
    public class EndlessSpringCrystal : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => GameLoop.ItemIds.BleedCrystal;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.enemyBleedStacks >= BleedCore.CrystalNeed);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.bleedDouble = true;
    }

    /// <summary>戻り血の古鎌 (分岐 B): 秘宝が敵の出血を使うたび (<see cref="Kw.ConsumeBleed"/> の出来事 OnBleedConsume)、 使った量の 60% (切り捨て) が
    /// 次のターンの頭に出血として戻る。 自然に減る分 (OnBleedDecay) は戻らない。 戻す出血は AddEnemyBleed を通す。</summary>
    public class ReturningBloodSickle : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "戻り血の古鎌";
        public const int ReturnPct = 60;
        /// <summary>[persistent] 次のターンの頭に戻す量 / このターンに使った分の 60% / その「このターン」の番号。</summary>
        private const string ReadyKey = "bld.sickle.ready", CurKey = "bld.sickle.cur", TurnKey = "bld.sickle.turn";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Psn.Count(ctx, ReadyKey) + Psn.Count(ctx, CurKey));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBleedConsume, PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBleedConsume)
            {
                int back = Kw.EventAmount(ctx) * ReturnPct / 100;
                if (back <= 0) return;
                if (Psn.Count(ctx, TurnKey) != ctx.currentTurn) Roll(ctx);
                Kw.Add(ctx, CurKey, back);
                return;
            }
            // OnTurnStart: 前のターンまでに使った分を戻す (このターンの頭に使った分は次のターンへ)
            if (Psn.Count(ctx, TurnKey) < ctx.currentTurn) Roll(ctx);
            int n = Psn.Count(ctx, ReadyKey);
            if (n <= 0) return;
            ctx.accumulatedValues[ReadyKey] = 0;
            ctx.AddEnemyBleed(n);
            Rw.Log($"[戻り血の古鎌] 使った出血の 60% が戻る: 出血 +{n} → {ctx.enemyBleedStacks}");
        }

        /// <summary>「このターン」の分を「次のターンの頭に戻す」側へ移し、 今のターンの番号にする。</summary>
        private static void Roll(CombatContext ctx)
        {
            Kw.Add(ctx, ReadyKey, Psn.Count(ctx, CurKey));
            ctx.accumulatedValues[CurKey] = 0;
            ctx.accumulatedValues[TurnKey] = ctx.currentTurn;
        }
    }

    /// <summary>血の楔 (分岐 A/B): 敵が得るシールドと回復の量、 敵の被ダメ軽減のパッシブで減るダメージを、 出血 ÷ (出血 + 30) の割合だけ弱める
    /// (<see cref="CombatContext.WedgeEnemyGain"/>・0 にはならない)。 敵の出血が 20 以上の間、 すべての撃の最終与ダメージ +40% (ctx.hitFinalDamagePct)。</summary>
    public class BloodWedge : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => GameLoop.ItemIds.BleedWedge;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.enemyBleedStacks >= BleedCore.WedgeNeed);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.bleedWedge = true; return; }
            if (ctx.IsRealHit && ctx.enemyBleedStacks >= BleedCore.WedgeNeed) ctx.hitFinalDamagePct += BleedCore.WedgePct;
        }
    }
}

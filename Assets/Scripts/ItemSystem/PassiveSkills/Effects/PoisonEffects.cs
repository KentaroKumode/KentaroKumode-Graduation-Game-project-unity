using CombatSystem;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  毒の軸 25 品 (2026-11-21・full-item-rework-ledger §②・docs/GAME.md §7-1)。
    //  毒 = 蝕み (敵の攻撃・防御を削る) と【起爆】(毒を 1/3 消費して消費量 × 4 の軽減不可ダメージ・ボスの次の目盛りをそのダメージの 35% ぶん上げる)。
    //  起爆の式は CombatSystem.PoisonCore (BOT の見積もりと同じ)。 端子の解決の中で効く 6 品の分 (緑染みの小刀・蛇血瓶・毒見役の小匙・
    //  毒棘の小盾のブロック・瘴気の外套の被ダメ・麻痺の吹き矢の防御無視) は PoisonCore.ApplyToTerminals (Resolve の 5d)。
    //  毒のダメージは毎ターン開始時 (StatusRegistry の poison)・ダメージを出した後に残る割合 50% (〈腐れ沼の苔玉〉で 75%)。
    // ============================================================

    /// <summary>毒の品の小さな道具と【起爆】の本体の口。</summary>
    public static class Psn
    {
        /// <summary>[persistent・戦闘で 0 から] この戦闘で起爆した回数 / 最後に起爆したターン / 〈遅効の毒刃〉の劇症処刑を判定するターン。</summary>
        public const string DetonateCountKey = "psn.detonates", DetonatedTurnKey = "psn.detonatedTurn", SlowBladeExecTurnKey = "psn.slowBladeExecTurn";

        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);

        /// <summary>【起爆】: 敵の毒を 1/3 (切り上げ) 消費し、 max(消費量 × 倍率, 消費量 × 敵の最大 HP × 0.25%) の軽減不可ダメージ (<see cref="PoisonCore.Damage"/>)。
        /// ボスの次の目盛りを、 そのダメージの 35% ぶん上げる (<see cref="PoisonCore.DetonateGauge"/>・ひびは割らない・2026-11-25)。 戻り値 = ダメージ。</summary>
        public static int Detonate(CombatContext ctx, string label)
        {
            int p = ctx.EnemyPoison;
            if (p <= 0) return 0;
            int spent = PoisonCore.Spend(p);
            int remPoison = p - spent;
            ctx.enemyStatusStacks["poison"] = remPoison;
            int before = Count(ctx, DetonateCountKey);
            var flags = PoisonCore.ItemsOf(Rw.Run);
            int dmg = PoisonCore.Damage(spent, flags, before, BossDown.ActiveNow, ctx.enemyMaxHP, 100, ctx.currentTurn, ctx.AxisDepth(AxisRules.Poison));
            ctx.accumulatedValues[DetonateCountKey] = before + 1;
            ctx.accumulatedValues[DetonatedTurnKey] = ctx.currentTurn;
            CombatManager.DetonateFired++;
            Kw.DamageEnemy(ctx, dmg);
            if ((flags & PoisonItems.SlowBlade) != 0)
            {
                if (ctx.GetAccumulated(Kw.ResolvingKey) > 0)
                {
                    ctx.accumulatedValues[SlowBladeExecTurnKey] = ctx.currentTurn;
                }
                else if (ctx.enemyCurrentHP > 0 && ctx.CanExecuteEnemy())
                {
                    int line = PoisonCore.SlowBladeKillLine(ctx.EnemyPoison, Rw.IsBoss());
                    if (line >= 0 && ctx.enemyCurrentHP <= line)
                    {
                        int left = ctx.enemyCurrentHP;
                        ctx.enemyCurrentHP = 0;
                        CombatSystem.HpLossDiag.Add("経路:処刑(遅効の毒刃)", left);
                        CombatSystem.EnemyHpLedger.Land("処刑(遅効の毒刃)", left, left);
                        Rw.Log($"[遅効の毒刃] 劇症後 敵 HP {left} ≤ {line} (毒 {ctx.EnemyPoison}) → 倒す");
                    }
                }
            }
            CombatManager.Instance?.AddBossGauge(PoisonCore.DetonateGauge);   // 2026-11-26: 目盛り → 混乱 +3
            Rw.Log($"[{label}] 【起爆】 毒 {p} → {remPoison} (消費 {spent}) → {dmg} の軽減不可ダメージ (この戦闘 {before + 1} 回目)");
            Kw.Raise(ctx, PassiveSkillTrigger.OnPoisonDetonate, dmg);
            return dmg;
        }

        /// <summary>このターン (key に今のターンを控え、 違うターンなら 0 から) の数。</summary>
        public static int TurnN(CombatContext ctx, string key)
            => Count(ctx, key + ".t") == ctx.currentTurn ? Count(ctx, key) : 0;
        public static void AddTurnN(CombatContext ctx, string key, int n)
        {
            int cur = TurnN(ctx, key);
            ctx.accumulatedValues[key + ".t"] = ctx.currentTurn;
            ctx.accumulatedValues[key] = cur + n;
        }

        /// <summary>敵が攻撃してきた / ブロックとシールドで受けきった (HP を 1 も失っていない)。 ターンの終わりに読む。</summary>
        public static bool EnemyAttacked(CombatContext ctx) => Rw.Count(ctx, MutualKeys.EnemyAtkPreDefense) > 0;
        public static bool Shutout(CombatContext ctx) => EnemyAttacked(ctx) && Rw.Count(ctx, MutualKeys.HpLossFromEnemy) <= 0;

        /// <summary>この撃が主攻撃の本物の撃で、 当たった (OnPostDealDamage の中)。</summary>
        public static bool Hit(CombatContext ctx) => ctx.IsRealHit && Rw.PlayerHit(ctx);
    }

    // ── B: 起点 ── (〈緑染みの小刀〉〈毒見役の小匙〉は端子の解決の中だけ・PoisonCore の旗)

    /// <summary>毒固めの腕環: ターン開始時、 敵の毒 6 につきこのターンのブロック +2 (最大 +10)。</summary>
    public class VenomBangle : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "毒固めの腕環";
        public const int Every = 6, Per = 2, Max = 10;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.EnemyPoison >= Every);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = System.Math.Min(Max, Per * (ctx.EnemyPoison / Every));
            if (n > 0) ctx.itemBlockBonus += n;
        }
    }

    /// <summary>毒塗りの楔: 防御の端子に 2 個以上挿したターン、 敵の攻撃を受けたとき、 敵に毒 +5。</summary>
    public class PoisonWedge : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "毒塗りの楔";
        public const int Poison = 5;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(env.Block(ctx) >= 2);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, MutualKeys.BlockDiceCount) >= 2 && Psn.EnemyAttacked(ctx)) Rw.Poison(ctx, Poison);
        }
    }

    /// <summary>酸の小瓶: 敵が攻撃したとき、 敵に毒 +2。 敵の毒が 8 以上ある間、 敵の防御 −4 (貫通 +4・配線の時点の毒で見る)。</summary>
    public class AcidVial : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "酸の小瓶";
        public const int Poison = 2, Need = 8, DefCut = 4;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.EnemyPoison >= Need);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired) { if (ctx.EnemyPoison >= Need) ctx.playerArmorPen += DefCut; return; }
            if (Psn.EnemyAttacked(ctx)) Rw.Poison(ctx, Poison);
        }
    }

    /// <summary>毒針の指輪: 各ターン最初に攻撃が当たったとき、 敵に毒 +3 (敵に毒が既にあれば +4)。</summary>
    public class NeedleRing : IPassiveSkillEffect
    {
        public string SkillId => "毒針の指輪";
        public const int Poison = 3, PoisonIfPoisoned = 4;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Psn.Hit(ctx) || !Rw.OncePerTurn(ctx, "psn.needle")) return;
            Rw.Poison(ctx, ctx.EnemyPoison > 0 ? PoisonIfPoisoned : Poison);
        }
    }

    /// <summary>腐葉土の袋: ターン開始時、 敵の毒が 12 未満なら敵に毒 +4、 12 以上ならこのターンの攻撃 +4 (撃ごと・OnPostRoll で足す)。</summary>
    public class LeafMoldBag : IPassiveSkillEffect
    {
        public string SkillId => "腐葉土の袋";
        public const int Line = 12, Poison = 4, Atk = 4;
        private const string AtkTurnKey = "psn.leafMoldAtkTurn";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnTurnStart)
            {
                if (ctx.EnemyPoison < Line) Rw.Poison(ctx, Poison);
                else ctx.accumulatedValues[AtkTurnKey] = ctx.currentTurn;
                return;
            }
            if (ctx.currentTurn > 0 && Psn.Count(ctx, AtkTurnKey) == ctx.currentTurn) ctx.AddPlayerAttackOrDiceBonus(Atk);
        }
    }

    // ── S: エンジン・安定 ── (〈蛇血瓶〉は端子の解決の中だけ)

    /// <summary>主を看取る香炉: 毒のダメージか【起爆】が入るたび、 この戦闘中の与ダメージ +6% (最大 +60%)。</summary>
    public class LongCenser : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "主を看取る香炉";
        public const float PctPer = 0.06f, MaxPct = 0.60f;
        /// <summary>[persistent・戦闘開始で 0] 積もった回数 (率 = 回数 × 6%・float の足し算を重ねない)。</summary>
        private const string Key = "psn.censer";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Pct(Pct(ctx));
        private static float Pct(CombatContext ctx) => System.Math.Min(MaxPct, Psn.Count(ctx, Key) * PctPer);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPoisonTick, PassiveSkillTrigger.OnPoisonDetonate, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            switch (t)
            {
                case PassiveSkillTrigger.OnBattleStart: ctx.accumulatedValues[Key] = 0f; break;
                case PassiveSkillTrigger.OnPostRoll: ctx.AddOutgoingPct(Pct(ctx)); break;
                default: if (Psn.Count(ctx, Key) * PctPer < MaxPct) Kw.Add(ctx, Key, 1); break;
            }
        }
    }

    /// <summary>廃庭の霧吹き【規則】: 敵が攻撃するときにも、 毒のダメージ (威力 50%・毒は減らない) が 1 回入る (1 ターン 1 回・CombatManager の敵の攻撃の解決)。</summary>
    public class RuinedGardenSprayer : IPassiveSkillEffect
    {
        public string SkillId => "廃庭の霧吹き";
        public const int PowerPct = 50;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.poisonTickOnEnemyAttack = true;
    }

    /// <summary>毒草摘みの杖 (分岐 B): マップを 1 歩進むごとに 1 溜まる (最大 16・2026-11-26: 12 → 16)。 次の戦闘の開始時、 溜まった分だけ敵に毒 (歩数は戦闘のたび 0 に戻る)。</summary>
    public class HerbStaff : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "毒草摘みの杖";
        public const int Max = 16;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(System.Math.Min(Max, Rw.Steps));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Poison(ctx, System.Math.Min(Max, Rw.Steps));
    }

    /// <summary>口縫いの毒糸: 敵の毒が 10 以上の間、 敵の大技のターンの攻撃 −20%。 毒が 20 以上なら、 さらに敵の攻撃 −2 (毎ターン)。
    /// OnPostRoll (予告の前) に積む。 大技のターン = <see cref="Escalation.IsHeavyTurn"/> (ダウンのターンは除く)。</summary>
    public class StitchThread : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "口縫いの毒糸";
        public const int HeavyNeed = 10, FlatNeed = 20, FlatCut = 2;
        public const float HeavyCut = 0.20f;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.EnemyPoison >= HeavyNeed);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int p = ctx.EnemyPoison;
            if (p >= FlatNeed) ctx.mutualEnemyAttackReduction += FlatCut;
            var e = CombatManager.Instance?.CurrentEnemy;
            if (p >= HeavyNeed && e != null && !BossDown.ActiveNow && Escalation.IsHeavyTurn(e, ctx.currentTurn)) ctx.enemyAtkCutPct += HeavyCut;
        }
    }

    /// <summary>瘴気溜まりの壺: ターン開始時の毒のダメージで敵の毒が減る直前、 敵に毒 +4 (毒が 1 以上ある時だけ・<see cref="PoisonCore.DecayTickKey"/>)。</summary>
    public class MiasmaJar : IPassiveSkillEffect
    {
        public string SkillId => "瘴気溜まりの壺";
        public const int Poison = 4;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPoisonTick };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Psn.Count(ctx, PoisonCore.DecayTickKey) > 0 && ctx.EnemyPoison >= 1) Rw.Poison(ctx, Poison);
        }
    }

    /// <summary>毒棘の小盾: 防御の端子に挿したターン、 ブロック +6 (2026-11-22・旧 +4・PoisonCore の旗・Resolve の 5d)。 そのターン、 ブロックで防いだダメージ 4 につき敵に毒 +1 (最大 +6)。</summary>
    public class ThornBuckler : IPassiveSkillEffect
    {
        public string SkillId => "毒棘の小盾";
        public const int Every = 4, Max = 6;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Psn.EnemyAttacked(ctx)) return;
            int blocked = System.Math.Min(ctx.blockSumThisTurn, Rw.Count(ctx, MutualKeys.EnemyAtk));
            int n = System.Math.Min(Max, blocked / Every);
            if (n > 0) Rw.Poison(ctx, n);
        }
    }

    // ── G: 分岐 A〈蝕み〉/ 分岐 B〈起爆〉 ──

    /// <summary>蓋付きの毒指輪 (分岐 B・2026-11-27 開幕暗殺特化): 戦闘開始から 5 ターン目まで敵へ与える毒 +2・【起爆】ダメージ +25%。 1〜5 ターン目に敵の毒が 26 以上 (<see cref="PoisonCore.RingNeed"/>) で攻撃が当たったとき、 自動で【起爆】(1 ターン 1 回)。</summary>
    public class HollowPoisonRing : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "蓋付きの毒指輪";
        /// <summary>[persistent] 起爆したターン (Rw.OncePerTurn・BOT の見積もりも読む)。</summary>
        public const string TurnKey = "psn.ring";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.currentTurn <= PoisonCore.RingAssassinTurns && ctx.EnemyPoison >= PoisonCore.RingNeed);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.poisonRingEarly = true; return; }
            if (ctx.currentTurn > PoisonCore.RingAssassinTurns || !Psn.Hit(ctx) || ctx.EnemyPoison < PoisonCore.RingNeed || !Rw.OncePerTurn(ctx, TurnKey)) return;
            Psn.Detonate(ctx, SkillId);
        }
    }

    /// <summary>水揚げの麻痺瓶 (分岐 A): 敵の毒 4 につき敵の攻撃 −1 (2026-11-22・旧 5 につき・最大 −10・予告の前)。 敵の攻撃を受けきったターン (HP を失わなかった)、 敵に毒 +5。</summary>
    public class Paralysis : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "水揚げの麻痺瓶";
        public const int Every = 4, MaxCut = 10, Poison = 5;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(System.Math.Min(MaxCut, ctx.EnemyPoison / Every));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnPostRoll) { ctx.mutualEnemyAttackReduction += System.Math.Min(MaxCut, ctx.EnemyPoison / Every); return; }
            if (Psn.Shutout(ctx)) Rw.Poison(ctx, Poison);
        }
    }

    /// <summary>腐し刃 (分岐 A): 敵の毒 1 につき、 攻撃の与ダメージ +1.5% (最大 +45%・配線の時点の毒)。 攻撃が当たるたび、 敵に毒 +1。</summary>
    public class RotBlade : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "腐し刃";
        public const float PctPer = 0.015f, MaxPct = 0.45f;
        public const int Poison = 1;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Pct(Pct(ctx));
        private static float Pct(CombatContext ctx) => System.Math.Min(MaxPct, ctx.EnemyPoison * PctPer);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired) { float p = Pct(ctx); if (p > 0f) ctx.AddOutgoingPct(p); return; }
            if (Psn.Hit(ctx)) Rw.Poison(ctx, Poison);
        }
    }

    /// <summary>腐れ沼の苔玉【規則】(分岐 A): 毒のダメージの後に残る毒の割合が 50% → 75% (起爆の消費には効かない)。</summary>
    public class RotMossBall : IPassiveSkillEffect
    {
        public string SkillId => "腐れ沼の苔玉";
        public const int KeepPct = 75;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.poisonKeepPct = KeepPct;
    }

    /// <summary>麻痺の吹き矢 (分岐 A): 敵の毒 2 につき敵の防御を 1 無視する (OnWired・貫通)。 毒 12 以上なら全部無視 (PoisonCore の旗・Resolve の 5d)。</summary>
    public class Blowpipe : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "麻痺の吹き矢";
       public const int Every = 2;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(ctx.EnemyPoison / Every);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.playerArmorPen += ctx.EnemyPoison / Every;
    }

    /// <summary>劇毒の火薬庫 (分岐 B・2026-11-27 即時起爆化): 【起爆】の倍率が ×4 → ×5 (PoisonCore の旗)。 1 ターンに敵へ毒を合計 8 以上与え、かつ敵の毒が 18 以上になったとき、 即座に自動で【起爆】(1 ターン 1 回)。</summary>
    public class PoisonMagazine : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "劇毒の火薬庫";
        /// <summary>[ターンごと] このターンに与えた毒 / 起爆したターン (BOT の見積もりも読む)。</summary>
        public const string GivenKey = "psn.magazine.given", TurnKey = "psn.magazine";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Psn.TurnN(ctx, GivenKey));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPoisonGain };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Psn.AddTurnN(ctx, GivenKey, Kw.EventAmount(ctx));
            if (ctx.currentTurn >= 1 && Psn.TurnN(ctx, GivenKey) >= PoisonCore.MagazineGiven && ctx.EnemyPoison >= PoisonCore.MagazineMinPoison && Rw.OncePerTurn(ctx, TurnKey))
                Psn.Detonate(ctx, SkillId);
        }
    }

    // ── L: 規則破壊 ──

    /// <summary>遅効の毒刃【規則】: 【起爆】のダメージが、 この戦闘で起爆した回数 1 回につき +30% (PoisonCore.Damage・旗)。 攻撃が当たるたび、 敵に毒 +2。</summary>
    public class SlowPoisonBlade : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "遅効の毒刃";
        public const int Poison = 2;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Psn.Count(ctx, Psn.DetonateCountKey));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Psn.Hit(ctx)) Rw.Poison(ctx, Poison);
        }
    }

    /// <summary>瘴気の外套: 敵の毒が 15 以上の間、 受けるダメージ −35% (敵の直接攻撃・固定ダメージを除く・PoisonCore の旗)。
    /// ターンの終わり、 敵の毒が 25 以上なら、 毒を消費せずに【起爆】の 50% のダメージ (起爆の回数には数えない・ボスの目盛りは上げない)。</summary>
    public class MiasmaMantle : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "瘴気の外套";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.EnemyPoison >= PoisonCore.MantleNeed);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int p = ctx.EnemyPoison;
            if (p < PoisonCore.MantleEchoNeed) return;
            int dmg = PoisonCore.Damage(PoisonCore.Spend(p), PoisonCore.ItemsOf(Rw.Run), Psn.Count(ctx, Psn.DetonateCountKey), BossDown.ActiveNow, ctx.enemyMaxHP, PoisonCore.MantleEchoPct, 99, ctx.AxisDepth(AxisRules.Poison));
            Kw.DamageEnemy(ctx, dmg);
            Rw.Log($"[瘴気の外套] 毒 {p} → 消費せずに起爆の 50% ({dmg})");
        }
    }

    /// <summary>毒溜まりの主: 敵の毒が 20 以上の間、 ターン開始時の毒のダメージが 2 回入り、 2 回目では毒が減らない (ctx.poisonDoubleTick・TickStatuses)。</summary>
    public class PoolKeeper : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "毒溜まりの主";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.EnemyPoison >= PoisonCore.PoolNeed);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.poisonDoubleTick = true;
    }

    /// <summary>腐蝕の王冠【規則】: 敵の毒の上限 (99) が無くなり、 敵へ与える毒がすべて 1.5 倍 (切り上げ)。 敵の段の交代で、 毒の半分が次の段へ残る (ctx.poisonCrown)。</summary>
    public class CorrosionCrown : IPassiveSkillEffect
    {
        public string SkillId => "腐蝕の王冠";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.poisonCrown = true;
    }

    /// <summary>骨溶かしの黒油 (2026-11-25 neon answer_gauge・旧: 毒のダメージか起爆のターンにひびを全部割る): よろめき中の敵にもダウンと同じ受けるダメージ ×1.5 が掛かり
    /// (CombatManager の BossDown.TakenMulNow が ctx.poisonDownDouble を読む)、 ダウン中・よろめき中の敵への毒のダメージと起爆のダメージ ×2 (ctx.poisonDownDouble・PoisonCore.Damage)。
    /// ボスを通常攻撃でダウンさせたとき (OnBossDown のひびの印 0)、 敵の毒 +8。</summary>
    public class BoneMeltOil : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "骨溶かしの黒油";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(BossDown.ActiveNow);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnBossDown };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.poisonDownDouble = true; return; }
            if (ctx.GetAccumulated(Kw.BossDownCrackKey) > 0) return;   // よろめき (ひびで割れた) では毒を足さない
            Rw.Poison(ctx, PoisonCore.OilDownPoison);
        }
    }
}

using CombatSystem;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  充電の軸 26 品 (2026-11-21・full-item-rework-ledger §①・docs/GAME.md §7-3)。
    //  充電 = 戦闘のマナ。 振り直し・【再起動】・【オーバークロック】・品へ払う。 過充電 = 充電 10 以上 (ChargeCore.OverchargeAt)。
    //  端子の解決の中で効く 4 品 (二股の導線・逆さ避雷針・灼線鞭・雷鳴の太鼓のオーバークロック) は ChargeCore の旗 (TerminalCards.Resolve の 5c)。
    //  「充電を使うたび」は ctx.ConsumeCharge の出来事 OnChargeSpend (量 = 払った量)。 支払いは 1 回ずつ配る (端子・再起動も・CombatManager.ApplyTerminalOutcome)。
    // ============================================================

    internal static class Chg
    {
        public static int SpentTurn(CombatContext ctx) => (int)ctx.GetAccumulated(CombatContext.ChargeSpentThisTurnKey);

        /// <summary>このターンの数 (key に今のターンを控え、 違うターンなら 0 から)。</summary>
        public static int TurnN(CombatContext ctx, string key)
            => (int)ctx.GetAccumulated(key + ".t") == ctx.currentTurn ? (int)ctx.GetAccumulated(key) : 0;
        public static void SetTurnN(CombatContext ctx, string key, int n)
        {
            ctx.accumulatedValues[key + ".t"] = ctx.currentTurn;
            ctx.accumulatedValues[key] = n;
        }

        /// <summary>このターンの消費 every ごとに 1 段 (最大 max 段)。 新しく満ちた段の数を返す。</summary>
        public static int NewSteps(CombatContext ctx, string key, int every, int max)
        {
            int steps = System.Math.Min(max, SpentTurn(ctx) / every), had = TurnN(ctx, key);
            if (steps <= had) return 0;
            SetTurnN(ctx, key, steps);
            return steps - had;
        }

        public static void Block(CombatContext ctx, int n, string label)
        {
            if (n <= 0) return;
            ctx.itemBlockBonus += n;
            Rw.Log($"[{label}] ブロック +{n}");
        }

        /// <summary>この撃が主攻撃の本物の撃か (OnPreDealDamage / OnPostDealDamage の中)。</summary>
        public static bool RealHit(CombatContext ctx) => ctx.IsRealHit;
    }

    // ── B: 起点 ──

    /// <summary>無限モーター: ブロックで敵の攻撃を完全に防いだターン、 余って消えたブロックと同量 (最大 +8) の充電を得る。</summary>
    public class InfiniteMotor : IPassiveSkillEffect
    {
        public string SkillId => "無限モーター";
        public const int Max = 8;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, MutualKeys.EnemyAtkPreDefense) <= 0) return;   // 敵が攻撃していない
            if (Rw.Count(ctx, MutualKeys.HpLossFromEnemy) > 0) return;        // 防ぎきれていない
            int spare = ctx.blockSumThisTurn - Rw.Count(ctx, MutualKeys.EnemyAtk);
            if (spare > 0) ctx.AddCharge(System.Math.Min(Max, spare));
        }
    }

    /// <summary>呼雷粉: 振り直しを 1 回行うたび、 充電 +4 (1 ターン最大 2 回)。 振り直しは OnPostRoll より前に済む。</summary>
    public class ThunderPowder : IPassiveSkillEffect
    {
        public string SkillId => "呼雷粉";
        public const int Per = 4, MaxPerTurn = 2;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = System.Math.Min(MaxPerTurn, ctx.rerollsThisTurn);
            if (n > 0) ctx.AddCharge(Per * n);
        }
    }

    /// <summary>帯電の胸甲: 充電を 5 消費するたび、 そのターンのブロック +5 (1 ターン最大 3 回)。</summary>
    public class ChargedCuirass : IPassiveSkillEffect
    {
        public string SkillId => "帯電の胸甲";
        public const int Every = 5, Block = 5, MaxPerTurn = 3;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnChargeSpend };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => Chg.Block(ctx, Block * Chg.NewSteps(ctx, "chg.cuirass", Every, MaxPerTurn), SkillId);
    }

    /// <summary>十五年目の計測器: 充電が上限に達したターン、 次の攻撃の撃の基礎攻撃 +8 (攻撃 +N は撃ごと・1 ターン 1 回)。</summary>
    public class OldGauge : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "十五年目の計測器";
        public const int Atk = 8;
        private const string Key = "chg.gauge";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Rw.Count(ctx, Key) > 0);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnChargeGain, PassiveSkillTrigger.OnOvercharge, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t != PassiveSkillTrigger.OnWired)
            {
                if (ctx.GetCharge() >= ctx.ChargeCap && Rw.OncePerTurn(ctx, "chg.gauge.turn")) ctx.accumulatedValues[Key] = 1f;
                return;
            }
            if (Rw.Count(ctx, Key) <= 0 || Rw.Count(ctx, MutualKeys.AttackDiceCount) <= 0) return;
            ctx.accumulatedValues[Key] = 0f;
            ctx.AddPlayerAttackOrDiceBonus(Atk);
        }
    }

    /// <summary>継ぎ足しの蓄電瓶: 充電の端子に挿したダイス 1 個につき、 充電 +3。</summary>
    public class ToppedJar : IPassiveSkillEffect
    {
        public string SkillId => "継ぎ足しの蓄電瓶";
        public const int PerDie = 3;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Rw.Count(ctx, MutualKeys.ChargeDiceCount);
            if (n > 0) ctx.AddCharge(PerDie * n);
        }
    }

    /// <summary>予備の蓄電池: 戦闘開始時、 充電 +10。</summary>
    public class SpareBattery : IPassiveSkillEffect
    {
        public string SkillId => "予備の蓄電池";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.AddCharge(10);
    }

    /// <summary>静電気の毛布: 防御の端子に挿したダイス 1 個につき、 充電 +3。</summary>
    public class StaticBlanket : IPassiveSkillEffect
    {
        public string SkillId => "静電気の毛布";
        public const int PerDie = 3;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Rw.Count(ctx, MutualKeys.BlockDiceCount);
            if (n > 0) ctx.AddCharge(PerDie * n);
        }
    }

    /// <summary>火打ちの針: 攻撃の端子を発動するたび (再起動も)、 充電 +2 (1 ターン最大 3 回)。 発動の数は端子の結果の撃の数。</summary>
    public class FlintNeedle : IPassiveSkillEffect
    {
        public string SkillId => "火打ちの針";
        public const int Per = 2, MaxPerTurn = 3;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var o = CombatManager.Instance?.LastTerminalOutcome;
            int n = o != null ? System.Math.Min(MaxPerTurn, o.attackHits.Count) : 0;
            if (n > 0) ctx.AddCharge(Per * n);
        }
    }

    // ── S: エンジン・安定 ──

    /// <summary>避雷の鉢金: 敵の攻撃を受けたターン終了時 (ブロックで防いだ分も)、 敵の予告攻撃 5 につき充電 +2 (最大 +8)。</summary>
    public class LightningHelm : IPassiveSkillEffect
    {
        public string SkillId => "避雷の鉢金";
        public const int Every = 5, Per = 2, Max = 8;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, MutualKeys.EnemyAtkPreDefense) <= 0) return;
            int n = System.Math.Min(Max, Rw.Count(ctx, MutualKeys.EnemyAtk) / Every * Per);
            if (n > 0) ctx.AddCharge(n);
        }
    }

    /// <summary>雷鯰の棘: 過充電 (充電 10 以上) の間、 攻撃は敵の防御を 8 無視し、 ボスの目盛りへの削り +20% (攻撃が通るたび、 与えた量の 20% だけ次の目盛りを上げる)。
    /// 過充電かどうかは配線の後 (OnWired・このターンの充電の獲得の後) で見る。</summary>
    public class CatfishSpine : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "雷鯰の棘";
        public string SkillId => Id;
        public const int Pen = 8, Gauge = 1;   // 2026-11-26: 目盛りへの削り 20% → 混乱 +1 (撃ごと)
        private const string Key = "chg.catfish";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.IsOvercharged());
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired)
            {
                bool on = ctx.IsOvercharged();
                Chg.SetTurnN(ctx, Key, on ? 1 : 0);
                if (on) ctx.playerArmorPen += Pen;
                return;
            }
            if (Chg.TurnN(ctx, Key) <= 0 || !Rw.PlayerHit(ctx) || !Chg.RealHit(ctx)) return;
            CombatManager.Instance?.AddBossGauge(Gauge);
        }
    }

    /// <summary>漏電する銅線: 充電を消費したターンの次のターン開始時、 前のターンの消費量の 30% (切り捨て・最大 +6) の充電を得る。</summary>
    public class LeakyCopperWire : IPassiveSkillEffect
    {
        public string SkillId => "漏電する銅線";
        public const int Pct = 30, Max = 6;
        private const string Key = "chg.leak";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnTurnEnd) { ctx.accumulatedValues[Key] = Chg.SpentTurn(ctx); return; }
            int n = System.Math.Min(Max, Rw.Count(ctx, Key) * Pct / 100);
            ctx.accumulatedValues[Key] = 0f;
            if (n > 0) ctx.AddCharge(n);
        }
    }

    /// <summary>蓄電の小手: このターン充電を 5 消費するたび、 このターンの全攻撃の撃の基礎攻撃 +4 (累積・攻撃 +N は撃ごと)。</summary>
    public class StorageGauntlet : IPassiveSkillEffect
    {
        public string SkillId => "蓄電の小手";
        public const int Every = 5, Atk = 4;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnChargeSpend };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Chg.NewSteps(ctx, "chg.gauntlet", Every, int.MaxValue);
            if (n > 0) ctx.AddPlayerAttackOrDiceBonus(Atk * n);
        }
    }

    /// <summary>引き直しの撃鉄: 振り直すたび、 このターンの次の 1 撃の基礎攻撃 +7 (そのターン累積・最初の 1 撃へ・ctx.firstHitAttackBonus)。</summary>
    public class RerollHammer : IPassiveSkillEffect
    {
        public string SkillId => "引き直しの撃鉄";
        public const int AtkPerReroll = 7;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.rerollsThisTurn > 0) ctx.firstHitAttackBonus += AtkPerReroll * ctx.rerollsThisTurn;
        }
    }

    /// <summary>絶縁の外套: 過充電 (充電 10 以上) でターンを開始したとき、 ブロック +5・このターンの被ダメージ −2 (answer14: ブロック +8 → +5)。</summary>
    public class InsulatedCloak : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "絶縁の外套";
        public const int Block = 5, Cut = 2;   // answer14: ブロック +8 → +5
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.IsOvercharged());
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsOvercharged()) return;
            Chg.Block(ctx, Block, SkillId);
            ctx.playerFlatDamageReduction += Cut;
        }
    }

    /// <summary>避雷の杭: 1 ターンに充電を 8 以上消費したとき、 ボスの次の目盛りを最大 HP の 5% ぶん上げ (2026-11-25 neon answer_gauge・旧: +5)、 ブロック +6 (1 ターン 1 回)。</summary>
    public class LightningStake : IPassiveSkillEffect
    {
        public const string Id = "避雷の杭";
        public string SkillId => Id;
        public const int Need = 8, Gauge = 3, Block = 6;   // 2026-11-26: 目盛り 最大 HP 5% → 混乱 +3
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnChargeSpend };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Chg.SpentTurn(ctx) < Need || !Rw.OncePerTurn(ctx, "chg.stake")) return;
            CombatManager.Instance?.AddBossGauge(Gauge);
            Chg.Block(ctx, Block, SkillId);
        }
    }

    // ── G: 分岐 ──

    /// <summary>過負荷チューナー (2026-11-26 還流型へ変更): 過充電 (充電 10 以上) の間、 充電を 3 以上消費する行動を行うたび、 直後に充電 +2 を得る (1 ターン最大 3 回)。
    /// 支払い前の充電は ctx.LastChargeBeforeSpend を読む。 端子の解決の見込み (TerminalCards.Resolve) も同じ条件で還流を反映する。</summary>
    public class OverloadTuner : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "過負荷チューナー";
        public const string TurnCountKey = "chg.tuner.n";
        public static int UsedThisTurn(CombatContext ctx) => ctx == null ? 0 : Chg.TurnN(ctx, TurnCountKey);
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(ChargeCore.TunerMaxPerTurn - UsedThisTurn(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnChargeSpend };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.overloadChargeCut = true; return; }
            int used = UsedThisTurn(ctx);
            if (used >= ChargeCore.TunerMaxPerTurn) return;
            if (ctx.LastChargeBeforeSpend < ChargeCore.OverchargeAt || Kw.EventAmount(ctx) < ChargeCore.TunerMinSpend) return;
            Chg.SetTurnN(ctx, TurnCountKey, used + 1);
            ctx.AddCharge(ChargeCore.TunerRefund);
        }
    }

    /// <summary>焦げ導線 (2026-11-26 再起動連動解放): 充電を消費するたび、 消費した量 × 3 の防御無視ダメージを敵に与える (基本 1 ターン最大 45)。
    /// このターン【再起動】が発動するたび、 このターンの上限 +20・追加で 12 の防御無視ダメージを与える (充電Bや他軸では上限 45 のまま)。</summary>
    public class ScorchedWire : IPassiveSkillEffect
    {
        public string SkillId => "焦げ導線";
        public const int Mul = 3, MaxPerTurn = 45, RebootCapAdd = 20, RebootZap = 12;
        public const string HeldKey = "chg.scorch.held";
        private const string Key = "chg.scorch";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnChargeSpend };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.accumulatedValues[HeldKey] = 1f; return; }
            int rebootsTurn = (int)ctx.GetAccumulated(ChargeCore.RebootsTurnNumKey) == ctx.currentTurn
                ? (int)ctx.GetAccumulated(ChargeCore.RebootsTurnKey) : 0;
            int cap = MaxPerTurn + RebootCapAdd * rebootsTurn;
            int done = Chg.TurnN(ctx, Key);
            int dmg = System.Math.Min(cap - done, Kw.EventAmount(ctx) * Mul);
            if (dmg <= 0 || ctx.enemyCurrentHP <= 0) return;
            Chg.SetTurnN(ctx, Key, done + dmg);
            Kw.DamageEnemy(ctx, dmg);
        }

        public static void OnReboot(CombatContext ctx, int count)
        {
            if (ctx == null || count <= 0 || ctx.GetAccumulated(HeldKey) <= 0f || ctx.enemyCurrentHP <= 0) return;
            Kw.DamageEnemy(ctx, RebootZap * count);
        }
    }

    /// <summary>雷壷【規則】: 充電の上限 (30) が無くなる。 代わりにターン終了時、 30 を超えている分の 1/4 (切り捨て) を失う (使ったことにはならない)。</summary>
    public class ThunderJar : IPassiveSkillEffect
    {
        public string SkillId => "雷壷";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.chargeCapRemoved = true; return; }
            int over = ctx.GetCharge() - CombatManager.ChargeMax;
            if (over >= 4) ctx.LoseCharge(over / 4);
        }
    }

    /// <summary>雷鳴の太鼓: 充電の上限 +15。 【オーバークロック】は ChargeCore (旗 Drum・TerminalCards.Resolve の 5c と本体の撃の輪)。</summary>
    public class ThunderDrum : IPassiveSkillEffect
    {
        public string SkillId => "雷鳴の太鼓";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.chargeCapBonus += ChargeCore.DrumCapAdd;
    }

    // ── L: 規則破壊 ──

    /// <summary>雷神槍: このターン充電を 8 以上消費したとき、 このターンの全攻撃の最終与ダメージ +(このターン消費した充電 × 6)% (2026-11-22・旧 × 5) (撃ごと・ctx.hitFinalDamagePct・8 未満では何もしない)。</summary>
    public class ThunderSpear : IPassiveSkillEffect
    {
        public string SkillId => "雷神槍";
        public const int Need = 8, PctPer = 6;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int spent = Chg.SpentTurn(ctx);
            if (spent >= Need && Chg.RealHit(ctx)) ctx.hitFinalDamagePct += spent * PctPer;
        }
    }

    /// <summary>貸与された時間【規則】: この戦闘で充電を累計 15 消費するたび、 エスカレーションの進行を 1 ターン遅らせ、 次のターンの手札 +1 枚 (1 戦闘最大 4 回)。</summary>
    public class LentTime : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "貸与された時間";
        public const int Every = 15, MaxPerBattle = 4;
        private const string Key = "chg.lent", DoneKey = "chg.lent.done";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(MaxPerBattle - Rw.Count(ctx, DoneKey));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnChargeSpend };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Kw.Tally(ctx, Key, Kw.EventAmount(ctx), Every);
            int done = Rw.Count(ctx, DoneKey);
            n = System.Math.Min(n, MaxPerBattle - done);
            if (n <= 0) return;
            ctx.accumulatedValues[DoneKey] = done + n;
            YachtRoleEffects.AddEscalationDelay(ctx, n);
            Rw.DrawNext(n);
            Rw.Log($"[貸与された時間] 充電を累計 {Rw.Count(ctx, Key)} 使った → 段階を {n} ターン遅らせ、 次のターンの手札 +{n} (この戦闘 {done + n}/{MaxPerBattle})");
        }
    }

    /// <summary>溜め撃ちの誘導コイル (2026-11-26 再起動累計型へ変更): この戦闘で充電を消費した回数を数え、 3 回ごとに「20 + この戦闘の【再起動】累計回数 × 12」(最大 140) の防御無視ダメージと、
    /// このターンの【再起動】の撃すべてと次の 1 撃の最終与ダメージ +50% (1 ターン最大 2 回)。</summary>
    public class FourthStrikeCore : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "溜め撃ちの誘導コイル";
        public const int Every = 3, BaseDmg = 20, PerRebootDmg = 12, MaxDmg = 140, NextPct = 50, MaxPerTurn = 2;
        public const string CountKey = "chg.four";
        private const string PendingKey = "chg.four.next", TurnKey = "chg.four.turn";
        public static int DamageFor(int rebootsBattle) => System.Math.Min(MaxDmg, BaseDmg + PerRebootDmg * System.Math.Max(0, rebootsBattle));
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Count(ctx, CountKey) % Every);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnChargeSpend, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnPreDealDamage)
            {
                if (!Chg.RealHit(ctx)) return;
                if (ctx.isRebootHit)
                {
                    int firedTurn = Chg.TurnN(ctx, TurnKey);
                    if (firedTurn > 0) ctx.hitFinalDamagePct += NextPct * firedTurn;
                    return;
                }
                if (Rw.Count(ctx, PendingKey) <= 0) return;
                ctx.accumulatedValues[PendingKey] = Rw.Count(ctx, PendingKey) - 1;
                ctx.hitFinalDamagePct += NextPct;
                return;
            }
            int c = Rw.Count(ctx, CountKey) + 1;
            ctx.accumulatedValues[CountKey] = c;
            if (c % Every != 0) return;
            int done = Chg.TurnN(ctx, TurnKey);
            if (done >= MaxPerTurn) return;
            Chg.SetTurnN(ctx, TurnKey, done + 1);
            int reboots = (int)ctx.GetAccumulated(ChargeCore.RebootsBattleKey);
            int dmg = DamageFor(reboots);
            if (dmg > 0 && ctx.enemyCurrentHP > 0) Kw.DamageEnemy(ctx, dmg);
            Kw.Add(ctx, PendingKey, 1);
            Rw.Log($"[溜め撃ちの誘導コイル] 充電を使った {c} 回目 (再起動累計 {reboots}) → {dmg} (防御無視)・再起動撃と次の 1 撃 +50%");
        }
    }

    /// <summary>黒錫のライデン瓶: 1 ターンに充電を 15 以上消費して終了したとき、 消費した量 × 1% (最大 12%) の敵の最大 HP の防御無視ダメージと、
    /// 次のダウンの受けるダメージの倍率 +0.5 (上限 +1.0)・そのダウンの開始時に充電 +10 (CombatManager.BuffNextBossDown・2026-11-25 neon answer_gauge・旧: ひびを直接割る)。 1 ターン 1 回 (ターン終了時に 1 回だけ見る)。</summary>
    public class ThunderReliquary : IPassiveSkillEffect
    {
        public string SkillId => "黒錫のライデン瓶";
        public const int Need = 15, MaxPct = 12, DownBonusPct = 50, DownCharge = 10;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int spent = Chg.SpentTurn(ctx);
            if (spent < Need || ctx.enemyCurrentHP <= 0) return;
            int pct = System.Math.Min(MaxPct, spent);
            int dmg = (int)((long)pct * ctx.enemyMaxHP / 100);
            if (dmg > 0) Kw.DamageEnemy(ctx, dmg);
            CombatManager.Instance?.BuffNextBossDown(SkillId, DownBonusPct, 0, DownCharge);
            Rw.Log($"[黒錫のライデン瓶] このターン充電 {spent} → 最大 HP の {pct}% = {dmg} (防御無視)");
        }
    }
}

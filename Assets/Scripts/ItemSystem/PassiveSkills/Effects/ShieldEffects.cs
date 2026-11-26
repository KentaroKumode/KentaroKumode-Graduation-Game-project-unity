using CombatSystem;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  シールドの軸 22 品 (2026-11-21・full-item-rework-ledger §③・docs/GAME.md §7-2)。
    //  シールド = 完全情報の【ジャスト防御】と要塞の反撃。 ジャスト防御の判定は CombatSystem.ShieldCore.IsJustGuard (本体・画面の見込み・BOT が同じ)。
    //  成立は CombatManager が敵の攻撃の解決の後に決め (Shd.NoteJustGuard)、 出来事 OnJustGuard (量 = 予告 E) を配る。
    //  端子の解決の中で効く 4 品 (肩当て・当て布の籠手・鋲の胸当て・擦り減った小盾) は ShieldCore の旗 (Resolve の 5e)。
    // ============================================================

    /// <summary>シールドの品の小さな道具と【ジャスト防御】の本体の口。</summary>
    public static class Shd
    {
        /// <summary>[persistent・戦闘で 0 から] 最後にジャスト防御が成立したターン / この戦闘で成立した回数。</summary>
        public const string JustGuardTurnKey = "shd.jgTurn", JustGuardCountKey = "shd.jgCount";

        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);

        /// <summary>【ジャスト防御】が成立した (CombatManager が敵の攻撃の解決の後に呼ぶ)。 出来事 OnJustGuard を配る。</summary>
        public static void NoteJustGuard(CombatContext ctx, int enemyAttack, int guard)
        {
            if (ctx == null) return;
            ctx.accumulatedValues[JustGuardTurnKey] = ctx.currentTurn;
            ctx.accumulatedValues[JustGuardCountKey] = Count(ctx, JustGuardCountKey) + 1;
            CombatManager.JustGuardFired++;
            Rw.Log($"[ジャスト防御] 予告 {enemyAttack} を防御 {guard} (余り {guard - enemyAttack}) で受け切った (この戦闘 {Count(ctx, JustGuardCountKey)} 回目)");
            Kw.Raise(ctx, PassiveSkillTrigger.OnJustGuard, enemyAttack);
        }

        /// <summary>このターンにジャスト防御が成立した (敵の攻撃の解決の後に読む)。</summary>
        public static bool JustGuardNow(CombatContext ctx) => ctx != null && ctx.currentTurn > 0 && Count(ctx, JustGuardTurnKey) == ctx.currentTurn;

        /// <summary>key に書いたターンが前のターン (次のターンの効果)。</summary>
        public static bool LastTurn(CombatContext ctx, string key) { int t = Count(ctx, key); return t > 0 && t == ctx.currentTurn - 1; }

        public static bool AttackedWithTerminal(CombatContext ctx) => Count(ctx, MutualKeys.AttackDiceCount) > 0;
    }

    // ── B: 起点 ── (〈肩当て〉〈当て布の籠手〉〈鋲の胸当て〉は端子の解決の中だけ・ShieldCore の旗)

    /// <summary>鏡返しの小盾: シールドまたはブロックで敵の攻撃を防いだとき、 防いだ量の 80% (【ジャスト防御】なら 130%) × (1 + 現在シールド ÷ 25) の軽減不可ダメージを敵へ返す (2026-11-27 後半反射スケール)。
    /// 防いだ量 = min(敵の攻撃, ブロック) + 敵の攻撃をシールドが吸った量。 ターンの終わりに軽減不可ダメージで返す (切り捨て)。</summary>
    public class MirrorBuckler : IPassiveSkillEffect
    {
        public const string Id = "鏡返しの小盾";
        public string SkillId => Id;
        public const int Pct = 80, JustPct = 130, ShieldScaleDiv = 25;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Psn.EnemyAttacked(ctx)) return;
            int defended = System.Math.Min(Rw.Count(ctx, MutualKeys.EnemyAtk), ctx.blockSumThisTurn) + ctx.shieldAbsorbedAttackThisTurn;
            int basePct = Shd.JustGuardNow(ctx) ? JustPct : Pct;
            float scale = 1f + System.Math.Max(0, ctx.consShield) / (float)ShieldScaleDiv;
            int dmg = (int)(defended * basePct / 100f * scale);
            if (dmg <= 0) return;
            Kw.DamageEnemy(ctx, dmg);
            Rw.Log($"[鏡返しの小盾] 防いだ {defended} (盾{ctx.consShield}・×{scale:F2}) → {dmg} を返す{(Shd.JustGuardNow(ctx) ? " (ジャスト防御)" : "")}");
        }
    }

    /// <summary>受け太刀の籠手: 【ジャスト防御】を成立させたターン、 次のターンの最初の 1 撃の基礎攻撃 +8 (ctx.firstHitAttackBonus・攻撃しなければ消える)。</summary>
    public class CatchBracer : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "受け太刀の籠手";
        public string SkillId => Id;
        public const int Atk = 8;
        private const string Key = "shd.catch";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Shd.LastTurn(ctx, Key));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnJustGuard, PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnJustGuard) { ctx.accumulatedValues[Key] = ctx.currentTurn; return; }
            if (Shd.LastTurn(ctx, Key)) ctx.firstHitAttackBonus += Atk;
        }
    }

    /// <summary>重ね札の盾: シールドが 12 以上ある間、 受ける直接ダメージ −3 (敵の攻撃を受けるその時のシールドで判定・2026-11-22 10 → 16・answer9: 16 / −2 → 12 / −3)。</summary>
    public class LayeredTagShield : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "重ね札の盾";
        public const int Need = 12, Cut = 3;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.consShield >= Need);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.playerWonRoll || ctx.finalDamage <= 0 || ctx.consShield < Need) return;
            ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - Cut);
        }
    }

    // ── S: エンジン・安定 ── (〈擦り減った小盾〉は端子の解決の中と許容幅だけ・ShieldCore の旗)

    /// <summary>聖路の白靴: マップを 2 歩進むごとに 1 蓄積 (最大 20・2026-11-22 1 歩 +1 → 2 歩 +1)。 次の戦闘開始時、 蓄積分だけシールドを得て 0 に戻す (歩数は戦闘の終わりに 0・RunState.stepsSinceBattle)。</summary>
    public class HolyRoadShoes : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "聖路の白靴";
        public const int Max = 20, StepsPer = 2;
        public static int Stored(int steps) => System.Math.Min(Max, steps / StepsPer);
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Stored(Rw.Steps));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Shield(ctx, Stored(Rw.Steps), SkillId);
    }

    /// <summary>英雄の意志: エリート戦・ボス戦で敵が大技の予告かチャージを出しているターン、 ターン開始時にシールド +18。
    /// そのターンに【ジャスト防御】が成立したら、 次のダウンの受けるダメージの倍率 +0.5 (上限 +1.0)・そのダウンの開始時にシールド +20
    /// (CombatManager.BuffNextBossDown・2026-11-25 neon answer_gauge・旧: 目盛りを HP に関係なく 1 段割る)。</summary>
    public class HeroWill : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "英雄の意志";
        public string SkillId => Id;
        public const int Shield = 18, DownBonusPct = 50, DownShield = 20;
        private const string Key = "shd.heroTurn";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Rw.StrongFoe() && (CombatManager.Instance?.EnemyHeavyOrChargeNow(ctx.currentTurn) ?? false));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnJustGuard };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnJustGuard)
            {
                if (Shd.Count(ctx, Key) == ctx.currentTurn && Rw.OncePerTurn(ctx, Key + ".jg"))
                    CombatManager.Instance?.BuffNextBossDown(SkillId, DownBonusPct, DownShield, 0);
                return;
            }
            if (!Rw.StrongFoe() || !(CombatManager.Instance?.EnemyHeavyOrChargeNow(ctx.currentTurn) ?? false)) return;
            ctx.accumulatedValues[Key] = ctx.currentTurn;
            Rw.Shield(ctx, Shield, SkillId);
            Rw.Log($"[英雄の意志] 敵の大技の予告 → シールド +{Shield}");
        }
    }

    /// <summary>油染みの盾革: 前のターンに【ジャスト防御】か完封 (敵の攻撃を受けて HP を 1 も失わなかった) をしていたら、 ターン開始時のシールドの減衰で残る割合が 50% → 70%
    /// (ctx.shieldKeepBonusPct をターンの終わりに立てる / 戻す・残る割合の上限 80% は変えない・2026-11-22 旧: 毎ターン 70%)。</summary>
    public class OiledShieldLeather : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "油染みの盾革";
        public const int KeepBonus = 20;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.shieldKeepBonusPct >= KeepBonus);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.shieldKeepBonusPct = Shd.JustGuardNow(ctx) || Psn.Shutout(ctx) ? KeepBonus : 0;   // 次のターンの頭の減衰 (BeginNewTurn) が読む
    }

    /// <summary>割れ盾の仕返し: シールドで敵の攻撃を吸収するたび、 吸収した量の 100% の軽減不可ダメージを敵へ返す (2026-11-27 反射強化)。
    /// さらにシールドが割られて 0 になったとき、 割られる直前のシールド量の軽減不可ダメージを敵へ返し、 次のターンの与ダメージ +35% (与ダメ % の池)。</summary>
    public class BrokenShieldGrudge : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "割れ盾の仕返し";
        public const float Pct = 0.35f;
        private const string Key = "shd.grudge";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Shd.LastTurn(ctx, Key));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldAbsorb, PassiveSkillTrigger.OnShieldBreak, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnShieldAbsorb)
            {
                int abs = Kw.EventAmount(ctx);
                if (abs > 0)
                {
                    float scale = 1f + System.Math.Max(0, ctx.consShield + abs) / 20f;
                    int dmg = (int)(abs * scale);
                    Kw.DamageEnemy(ctx, dmg);
                    Rw.Log($"[割れ盾の仕返し] シールド吸収 {abs} (×{scale:F2}) → {dmg} を返す");
                }
                return;
            }
            if (t == PassiveSkillTrigger.OnShieldBreak)
            {
                int before = Kw.EventAmount(ctx);
                Kw.DamageEnemy(ctx, before);
                ctx.accumulatedValues[Key] = ctx.currentTurn;
                Rw.Log($"[割れ盾の仕返し] シールド {before} が割られた → {before} を返す");
                return;
            }
            if (Shd.LastTurn(ctx, Key)) ctx.AddOutgoingPct(Pct);
        }
    }

    // ── G: 分岐 (A = ジャスト防御・見切り / B = 余剰の持ち越し・要塞) ──

    /// <summary>力みの小手: シールドを合計 10 得るたび【力み】+1。 攻撃の端子を挿したターン、 【力み】を全部使い、 1 つにつき全撃の基礎攻撃 +5 (攻撃 +N・撃ごとに乗る・answer9: 旧 12 / +4)。</summary>
    public class StrainGauntlet : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "力みの小手";
        public const int Every = 10, AtkPer = 5;
        private const string Stk = "shd.strain", Tally = "shd.strainTally";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Shd.Count(ctx, Stk));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldGain, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnShieldGain)
            {
                int n = Kw.Tally(ctx, Tally, Kw.EventAmount(ctx), Every);
                if (n > 0) Kw.Add(ctx, Stk, n);
                return;
            }
            int s = Shd.Count(ctx, Stk);
            if (s <= 0 || !Shd.AttackedWithTerminal(ctx)) return;
            ctx.AddPlayerAttackOrDiceBonus(AtkPer * s);
            ctx.accumulatedValues[Stk] = 0;
            Rw.Log($"[力みの小手] 力み {s} を使い 全撃の攻撃 +{AtkPer * s}");
        }
    }

    /// <summary>敗残兵の部隊章: シールドを消費・吸収・減衰で 8 失うたび【反骨】+1 (最大 10)。 【反骨】1 つにつき与ダメージ +10% (与ダメ % の池・戦闘の間・減らない)。</summary>
    public class VeteranPatch : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "敗残兵の部隊章";
        public const int Every = 8, Max = 10;
        public const float PctPer = 0.10f;
        private const string Stk = "shd.veteran", Tally = "shd.veteranTally";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Shd.Count(ctx, Stk));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldAbsorb, PassiveSkillTrigger.OnShieldDecay, PassiveSkillTrigger.OnShieldSpend, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnPostRoll)
            {
                int s = Shd.Count(ctx, Stk);
                if (s > 0) ctx.AddOutgoingPct(s * PctPer);
                return;
            }
            int n = Kw.Tally(ctx, Tally, Kw.EventAmount(ctx), Every);
            if (n > 0) ctx.accumulatedValues[Stk] = System.Math.Min(Max, Shd.Count(ctx, Stk) + n);
        }
    }

    /// <summary>重装兵の隠し刃: 【ジャスト防御】を成立させたとき、 敵の予告攻撃の 220% の軽減不可ダメージ (防御も無視) を返す (2026-11-27: 旧 150%)。
    /// さらにボスのダウン中はシールドがターン開始時に減衰しない (ctx.shieldKeepOnBossDown・BeginNewTurn)。</summary>
    public class KnightsTrump : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "重装兵の隠し刃";
        public string SkillId => Id;
        public const int ReturnPct = 220;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(BossDown.ActiveNow);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnJustGuard };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.shieldKeepOnBossDown = true; return; }
            int e = Kw.EventAmount(ctx) * ReturnPct / 100;
            Kw.DamageEnemy(ctx, e);
            Rw.Log($"[重装兵の隠し刃] ジャスト防御 → 予告の {ReturnPct}% = {e} を返す");
        }
    }

    /// <summary>相棒の魂: シールドが割られて 0 になったとき、 割られる前の量の 70% (切り捨て) で張り直す (1 戦闘 1 回)。 張り直した次のターン、 全撃の基礎攻撃 +10。</summary>
    public class CompanionSoul : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "相棒の魂";
        public const int Pct = 70, Atk = 10;
        private const string Once = "shd.companion", TurnKey = "shd.companionTurn";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Once(Shd.Count(ctx, Once) > 0, true);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldBreak, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired)
            {
                if (Shd.LastTurn(ctx, TurnKey) && Shd.AttackedWithTerminal(ctx)) ctx.AddPlayerAttackOrDiceBonus(Atk);
                return;
            }
            int n = Kw.EventAmount(ctx) * Pct / 100;
            if (n <= 0 || !Rw.OncePerBattle(ctx, Once)) return;
            Rw.Shield(ctx, n, SkillId);
            ctx.accumulatedValues[TurnKey] = ctx.currentTurn;
            Rw.Log($"[相棒の魂] 割られたシールドを {n} で張り直す");
        }
    }

    /// <summary>盾押しの心得【規則】: 攻撃の端子を挿したターン、 現在のシールドの 35% (切り捨て) が全撃の基礎攻撃に加わる (シールドは減らない)。</summary>
    public class ShieldPushArt : IPassiveSkillEffect
    {
        public string SkillId => "盾押しの心得";
        public const int Pct = 35;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Shd.AttackedWithTerminal(ctx) || ctx.consShield <= 0) return;
            int add = ctx.consShield * Pct / 100;
            if (add > 0) ctx.AddPlayerAttackOrDiceBonus(add);
        }
    }

    /// <summary>静止の振り子: 攻撃の端子に 1 つも挿さずに敵の攻撃を完封した (攻撃を受けて HP を 1 も失わなかった) 次のターン、 全撃の最終与ダメージ +60% (【ジャスト防御】だったなら +130%・answer14: 旧 +100%)。</summary>
    public class SilentStance : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "静止の振り子";
        public string SkillId => Id;
        public const int Pct = 60, JustPct = 130;   // answer14: ジャスト防御 +100% → +130%
        private const string TurnKey = "shd.still", PctKey = "shd.stillPct";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Shd.Count(ctx, TurnKey) == ctx.currentTurn);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnTurnEnd)
            {
                if (Shd.AttackedWithTerminal(ctx) || !Psn.Shutout(ctx)) return;
                ctx.accumulatedValues[TurnKey] = ctx.currentTurn + 1;
                ctx.accumulatedValues[PctKey] = Shd.JustGuardNow(ctx) ? JustPct : Pct;
                return;
            }
            if (ctx.IsRealHit && Shd.Count(ctx, TurnKey) == ctx.currentTurn) ctx.hitFinalDamagePct += Shd.Count(ctx, PctKey);
        }
    }

    // ── L: 規則破壊 ──

    /// <summary>破城斧: シールドが 20 以上のターン (最初の 1 撃の時のシールドで決める)、 攻撃の端子 1 枚につき (撃ごとに) シールドを 6 使い、 その撃の最終与ダメージ +130% (final6: 旧 +110%)・敵の防御を全部無視。
    /// 撃の途中でシールドが 6 を切ったら、 その後の撃には乗らない。 シールド 20 未満のターンは発動しない (2026-11-22 旧: 15 / 8 / +80%)。</summary>
    public class SiegeAxe : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "破城斧";
        public string SkillId => Id;
        public const int MinShield = 20, PerHitShield = 6, FinalPct = 130;
        private const string TurnKey = "shd.siegeAxe.turn";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.consShield >= MinShield);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            if (ctx.hitIndex == 0 && ctx.consShield >= MinShield) ctx.accumulatedValues[TurnKey] = ctx.currentTurn;
            if (Shd.Count(ctx, TurnKey) != ctx.currentTurn || ctx.consShield < PerHitShield) return;
            Kw.SpendShield(ctx, PerHitShield);
            ctx.hitFinalDamagePct += FinalPct;
            ctx.hitIgnoreDefense = true;
            Rw.Log($"[破城斧] {ctx.hitIndex + 1} 撃目: シールド {PerHitShield} を使い 最終与ダメージ +{FinalPct}%・防御無視 (シールド 残 {ctx.consShield})");
        }
    }

    /// <summary>二拍目の心臓: 戦闘中初めて HP が 50% 以下になった瞬間、 最大 HP の 30% (切り上げ) のシールドを得て、 以後この戦闘のシールドの獲得量 ×1.5 (ctx.shieldGainPct += 50・1 戦闘 1 回)。
    /// さらに発動後、 シールドで敵の攻撃を吸収するたび、 吸収した量の 100% の軽減不可ダメージを敵へ返す (2026-11-27 反射追加)。</summary>
    public class SecondBeatHeart : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "二拍目の心臓";
        public const int ShieldPct = 30, GainPct = 50;
        private const string Once = "shd.secondBeat";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Once(Shd.Count(ctx, Once) > 0, true);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostReceiveDamage, PassiveSkillTrigger.OnShieldAbsorb };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnShieldAbsorb)
            {
                if (Shd.Count(ctx, Once) <= 0) return;
                int abs = Kw.EventAmount(ctx);
                if (abs > 0)
                {
                    float scale = 1f + System.Math.Max(0, ctx.consShield + abs) / 15f;
                    int dmg = (int)(abs * scale);
                    Kw.DamageEnemy(ctx, dmg);
                    Rw.Log($"[二拍目の心臓] シールド吸収 {abs} (×{scale:F2}) → {dmg} を返す");
                }
                return;
            }
            if (ctx.playerMaxHP <= 0 || ctx.playerCurrentHP <= 0) return;
            if (ctx.playerCurrentHP * 100 > ctx.playerMaxHP * 50) return;
            if (!Rw.OncePerBattle(ctx, Once)) return;
            int shield = (ctx.playerMaxHP * ShieldPct + 99) / 100;
            Rw.Shield(ctx, shield, SkillId);
            ctx.shieldGainPct += GainPct;
            Rw.Log($"[二拍目の心臓] HP 50% 以下 → シールド +{shield}・以後シールドの獲得 ×1.5");
        }
    }

    /// <summary>脈打たぬ鋼の心臓: 戦闘開始時、 シールド +X (X = <see cref="GameLoop.RunState.pulselessHeartShield"/>・ランの始めに 4・持っている間に戦闘に勝つたび +1)。
    /// 現在のシールドが 50 以上の間、 ターン開始時にシールドが減衰しない (ctx.shieldNoDecayAt・2026-11-22 旧: 6 / 35)。 勝ちを数えるのは CombatManager.FinishCombat (<see cref="NoteWin"/>)。</summary>
    public class PulselessHeart : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "脈打たぬ鋼の心臓";
        public const int StartShield = 4, PerWin = 1, NoDecayAt = 50;
        public string SkillId => Id;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Run?.pulselessHeartShield ?? StartShield);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Rw.Shield(ctx, Rw.Run?.pulselessHeartShield ?? StartShield, SkillId);
            ctx.shieldNoDecayAt = NoDecayAt;
        }
        /// <summary>戦闘に勝った時に呼ぶ。 持っている間の勝利だけ X +1。</summary>
        public static void NoteWin(GameLoop.RunState run)
        {
            if (AxisRules.Owns(run, Id)) run.pulselessHeartShield += PerWin;
        }
    }

    /// <summary>落城の礎石: 吸収またはターン開始の減衰で 1 ターンにシールドを 12 以上 (2026-11-22 neon 収束: 旧 20) 失ったとき、 次のターンの全撃の基礎攻撃 +「そのターンに失ったシールドの半分」(攻撃 +N・撃ごと)・
    /// 全撃の最終与ダメージ +80%。 失ったシールド 1 につき、 ボスの次の目盛りを最大 HP の 0.5% ぶん上げる (1 ターン最大 10%・2026-11-25 neon answer_gauge・旧: 減るたびひびを入れる)。
    /// 2026-11-22 (neon 最終仕上げ §2-2) 旧: 減るたび 減った量 × 2 の軽減不可ダメージ。</summary>
    public class FallenCastleCornerstone : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "落城の礎石";
        public string SkillId => Id;
        public const int Need = 12, FinalPct = 80, GaugePerShield = 5, GaugeMaxPerTurn = 4;   // 2026-11-26: 目盛り → 失ったシールド 5 につき混乱 +1 (1 ターン 4 まで)   // 2026-11-22 neon 収束: 失うシールド 20 → 12・final6: +60% → +80%
        /// <summary>このターンに失ったシールド (Psn.TurnN) / 上乗せが働くターン / その攻撃 +N。</summary>
        private const string LostKey = "shd.castle.lost", TurnKey = "shd.castle.turn", AtkKey = "shd.castle.atk";
        public static bool Primed(CombatContext ctx) => ctx != null && ctx.currentTurn > 0 && Shd.Count(ctx, TurnKey) == ctx.currentTurn;
        public static int AttackNow(CombatContext ctx) => Primed(ctx) ? Shd.Count(ctx, AtkKey) : 0;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Primed(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldAbsorb, PassiveSkillTrigger.OnShieldDecay, PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            switch (t)
            {
                case PassiveSkillTrigger.OnTurnEnd:
                {
                    int lost = Psn.TurnN(ctx, LostKey);
                    if (lost < Need) return;
                    ctx.accumulatedValues[TurnKey] = ctx.currentTurn + 1;
                    ctx.accumulatedValues[AtkKey] = lost / 2;
                    Rw.Log($"[落城の礎石] このターン シールド −{lost} → 次のターン 全撃の攻撃 +{lost / 2}・最終与ダメージ +{FinalPct}%");
                    return;
                }
                case PassiveSkillTrigger.OnWired:
                    if (Primed(ctx) && Shd.AttackedWithTerminal(ctx)) ctx.AddPlayerAttackOrDiceBonus(Shd.Count(ctx, AtkKey));
                    return;
                case PassiveSkillTrigger.OnPreDealDamage:
                    if (ctx.IsRealHit && Primed(ctx)) ctx.hitFinalDamagePct += FinalPct;
                    return;
                default:
                {
                    int lost = Kw.EventAmount(ctx);
                    if (lost <= 0) return;
                    int had = Psn.TurnN(ctx, LostKey);
                    Psn.AddTurnN(ctx, LostKey, lost);
                    // 1 ターンの合計で最大 HP の 10% まで: 今までの分と足した後の分の差だけ上げる
                    int gn = System.Math.Min(GaugeMaxPerTurn, (had + lost) / GaugePerShield) - System.Math.Min(GaugeMaxPerTurn, had / GaugePerShield);
                    if (gn > 0) CombatManager.Instance?.AddBossGauge(gn);
                    return;
                }
            }
        }
    }

    /// <summary>防人の軍標: 【ジャスト防御】を成立させるたび、 この戦闘の最終与ダメージ倍率 +(持っているシールドの秘宝の数 × 9)% (この品も数える・ctx.AxisDepth・累積・ctx.finalDamagePct) と、
    /// 次のターンの手札 +1 枚 (Rw.DrawNext)。 2026-11-25 作者の決定: 「2 回目以降のジャスト防御の次のターン 全攻撃がもう 1 回」を撤去 (シールドの品なしでつまみ食いできた)・+45% 固定 → 秘宝の数に比例 (5 つで +45%)。</summary>
    public class BorderGuardStandard : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "防人の軍標";
        public string SkillId => Id;
        public const int PctPerShieldItem = 9, Draw = 1;
        /// <summary>ジャスト防御 1 回の上乗せ (%)。 本体と BOT の見積もりが同じ式を読む。</summary>
        public static int PctPerGuard(CombatContext ctx) => ctx != null ? PctPerShieldItem * ctx.AxisDepth(AxisRules.Shield) : 0;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Pct(ctx.finalDamagePct / 100f);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnJustGuard };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int pct = PctPerGuard(ctx);
            ctx.finalDamagePct += pct;
            Rw.DrawNext(Draw);
            Rw.Log($"[防人の軍標] ジャスト防御 → 最終与ダメージ倍率 +{pct}% (この戦闘 +{ctx.finalDamagePct}%)・次のターンの手札 +{Draw}");
        }
    }
}

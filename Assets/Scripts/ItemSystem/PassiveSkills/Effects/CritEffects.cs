using CombatSystem;
using GameLoop;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  会心の軸 21 品 (2026-11-21・full-item-rework-ledger §⑥・docs/GAME.md §7-6)。
    //  会心率を 100% へ近づけ、 会心をきっかけにブロック・回復・ダウン削り・処刑まで回す。 100% を超えた分は会心倍率へ変わる (CritCore・全員の規則)。
    //  分岐 A〈確定会心・攻防一体連打〉/ 分岐 B〈極大倍率・一撃必殺〉(攻撃の端子 1 枚の撃)。
    //  〈必殺の照準器〉は旗だけ (本体の撃の輪がもう 1 回発火する・CombatManager.Turn)。 ここは出来事で働く 20 品。
    // ============================================================

    /// <summary>会心の軸の小さな道具。 このターンに出した会心の数と、 会心を出した直近の 2 つのターンを数える (PassiveSkillManager が会心のたびに <see cref="Note"/>)。</summary>
    public static class Crit
    {
        /// <summary>[persistent・戦闘で 0 から] このターンの会心の数 (Psn.TurnN) / 会心を出した直近のターン / その前に会心を出したターン。</summary>
        public const string TurnNKey = "crit.n", LastKey = "crit.last", PrevKey = "crit.prev";

        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);

        /// <summary>プレイヤーの撃が会心した (OnCriticalDamage を配る直前に PassiveSkillManager が呼ぶ)。</summary>
        public static void Note(CombatContext ctx)
        {
            if (ctx == null) return;
            int last = Count(ctx, LastKey);
            if (last != ctx.currentTurn) { ctx.accumulatedValues[PrevKey] = last; ctx.accumulatedValues[LastKey] = ctx.currentTurn; }
            Psn.AddTurnN(ctx, TurnNKey, 1);
        }

        /// <summary>このターンに出した会心の数。</summary>
        public static int ThisTurn(CombatContext ctx) => Psn.TurnN(ctx, TurnNKey);

        /// <summary>前のターンに会心を出した。</summary>
        public static bool LastTurn(CombatContext ctx)
        {
            int cur = ctx.currentTurn, last = Count(ctx, LastKey);
            if (cur <= 1) return false;
            return last == cur - 1 || (last == cur && Count(ctx, PrevKey) == cur - 1);
        }

        /// <summary>攻撃の端子を 1 枚だけ発動したターンの、 その撃 (〈一の矢〉〈渾身の柄頭〉〈必殺の照準器〉)。</summary>
        public static bool SoloHit(CombatContext ctx) => ctx.IsRealHit && ctx.hitCount == 1;

        /// <summary>BOT の配線の見積もり: 配線の前に分かる、 このターンの全部の撃に乗る会心率と会心倍率 (所持品の条件は品と同じ判定・量は品の定数)。
        /// 配線で決まる分 (〈鷹匠の革手袋〉〈狙い目の刻み〉〈一の矢〉〈渾身の柄頭〉〈必殺の照準器〉) と〈抜き打ちの作法〉は BOT が撃ごとに見る。</summary>
        public static void ForecastTurn(CombatContext ctx, RunState run, out float rate, out float mul)
        {
            rate = 0f; mul = 0f;
            if (ctx == null || run == null) return;
            if (run.OwnsPassive("余韻の鈴") && LastTurn(ctx)) rate += LingeringBell.Rate;
            if (run.OwnsPassive("受け流しの扇") && ParryFan.Primed(ctx)) rate += ParryFan.Rate;
            if (run.OwnsPassive("瑠璃砥石")) { rate += LapisWhetstone.Rate; mul += LapisWhetstone.MulNow(ctx); }
            if (run.OwnsPassive("斬り重ねの鞘")) mul += LayeredScabbard.MulNow(ctx);
            if (run.OwnsPassive(ItemIds.CritPinpoint)) { rate += PinpointEye.Rate; mul += PinpointEye.Mul; }
            if (run.OwnsPassive(ItemIds.CritClairvoyant) && ClairvoyantCrystal.Primed(ctx)) rate += ClairvoyantCrystal.Rate;
            if (run.OwnsPassive("測量師の片眼鏡")) rate += SurveyorMonocle.RateNow(ctx);
        }
    }

    // ── B: 起点 ──

    /// <summary>鷹匠の革手袋: 攻撃の端子に 4 以上の目を挿したターン、 会心率 +25%・会心倍率 +0.25。</summary>
    public class FalconerGlove : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "鷹匠の革手袋";
        public const int MinFace = 4;
        public const float Rate = 0.25f, Mul = 0.25f;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Rw.Count(ctx, MutualKeys.AttackDiceMax) >= MinFace);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, MutualKeys.AttackDiceMax) < MinFace) return;
            ctx.critRatePctAdd += Rate;
            ctx.criticalMultiplier += Mul;     // 配線の後に足した倍率は、 そのターンの全部の撃に乗る (撃の輪がこの値へ戻す)
        }
    }

    /// <summary>狙い目の刻み: 攻撃の端子に 5 以上の目を挿したターン、 その撃の会心率 +40% (6 なら確定会心)。 撃の目は ctx.hitFaceMax (CritCore.HitFaceMax)。</summary>
    public class AimNotch : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritAim;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            ctx.critRatePctAdd += CritCore.AimRate(ctx.hitFaceMax);      // 会心率は撃ごとに戻るので、 この撃だけに乗る
            if (CritCore.AimForce(ctx.hitFaceMax)) ctx.forceCritThisHit = true;
        }
    }

    /// <summary>一の矢: 攻撃の端子を 1 枚だけ発動したターン、 その撃の会心率 +45%・会心倍率 +0.35。</summary>
    public class FirstArrow : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritFirstArrow;
        public const float Rate = 0.45f, Mul = 0.35f;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Crit.SoloHit(ctx)) return;
            ctx.critRatePctAdd += Rate;
            ctx.criticalMultiplier += Mul;
        }
    }

    /// <summary>余韻の鈴: 会心を出した次のターン、 会心率 +30%・ブロック +4。</summary>
    public class LingeringBell : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "余韻の鈴";
        public const float Rate = 0.30f;
        public const int Block = 4;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Crit.LastTurn(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Crit.LastTurn(ctx)) return;
            ctx.critRatePctAdd += Rate;
            ctx.itemBlockBonus += Block;
        }
    }

    /// <summary>受け流しの扇: ブロックとシールドで敵の攻撃を完封した (HP を 1 も失わなかった) 次のターン、 会心率 +40%・そのターンの最初の会心の撃で敵の防御 −6 (貫通 +6・そのターンの残りの撃にも)。</summary>
    public class ParryFan : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "受け流しの扇";
        public const float Rate = 0.40f;
        public const int DefCut = 6;
        private const string Key = "crit.parry", PenKey = "crit.parry.pen";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Primed(ctx));
        public static bool Primed(CombatContext ctx) => Shd.LastTurn(ctx, Key);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            switch (t)
            {
                case PassiveSkillTrigger.OnTurnEnd: if (Psn.Shutout(ctx)) ctx.accumulatedValues[Key] = ctx.currentTurn; break;
                case PassiveSkillTrigger.OnWired: if (Shd.LastTurn(ctx, Key)) ctx.critRatePctAdd += Rate; break;
                default:
                    if (ctx.IsRealHit && Shd.LastTurn(ctx, Key) && Rw.OncePerTurn(ctx, PenKey)) ctx.playerArmorPen += DefCut;   // 防御の計算はこの後 (ApplyWinDamageModifiers)
                    break;
            }
        }
    }

    // ── S: エンジン・安定 ──

    /// <summary>銑鉄棍【規則】: 会心しなかった撃に、 会心率 1% につき与ダメージ +1% (会心率は 100% を上限に数える・与ダメ % の池)。 会心は普通に出る (PassiveSkillManager.ProcessDamage)。</summary>
    public class UnwhettedClub : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritIronClub;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critToDamage = true;
    }

    /// <summary>測量師の片眼鏡: マップを 1 歩進むごとに会心率 +5% を蓄積 (最大 100%)。 次の戦闘の最初のターンの全部の撃に蓄積分の会心率を足す (100% を超えた分は会心倍率に変わる・歩数は戦闘で 0 に戻る)。</summary>
    public class SurveyorMonocle : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "測量師の片眼鏡";
        public const float PctPerStep = 0.05f, Max = 1f;
        /// <summary>[persistent] 戦闘の始めの蓄積 (会心率)。</summary>
        private const string Key = "crit.surveyor";
        public static float Stored(int steps) => System.Math.Min(Max, steps * PctPerStep);
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Pct(RateNow(ctx));
        /// <summary>このターンの撃に足す会心率 (最初のターンだけ)。</summary>
        public static float RateNow(CombatContext ctx) => ctx.currentTurn <= 1 ? ctx.GetAccumulated(Key) : 0f;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.accumulatedValues[Key] = Stored(Rw.Steps); return; }
            if (ctx.IsRealHit && ctx.currentTurn == 1) ctx.critRatePctAdd += ctx.GetAccumulated(Key);
        }
    }

    /// <summary>研ぎ師の見立て書: 会心を出すたび、 そのターンのブロック +4 (1 ターン最大 12)。 同じターンに 2 回以上会心を出したら HP +3 (1 ターン 1 回)。
    /// 攻撃の中で得たブロックは、 そのターンの敵の攻撃の前に足す (CombatManager.Turn の配線の後のブロック)。</summary>
    public class WhetAppraisal : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "研ぎ師の見立て書";
        public const int Block = 4, BlockMax = 12, Heal = 3;
        private const string BlockKey = "crit.whet.block", HealKey = "crit.whet.heal";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Psn.TurnN(ctx, BlockKey));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            int n = System.Math.Min(Block, BlockMax - Psn.TurnN(ctx, BlockKey));
            if (n > 0) { Psn.AddTurnN(ctx, BlockKey, n); ctx.itemBlockBonus += n; }
            if (Crit.ThisTurn(ctx) >= 2 && Rw.OncePerTurn(ctx, HealKey)) { Rw.Heal(ctx, Heal); Rw.Log($"[研ぎ師の見立て書] このターン会心 {Crit.ThisTurn(ctx)} 回 → HP +{Heal}"); }
        }
    }

    /// <summary>刃毀れの見立て: 会心した撃は敵の防御を 12 無視し (ctx.critArmorPen)、 その撃のダメージの 25% だけボスの次の目盛りを上げる (RaiseBossNextNotch)。</summary>
    public class NickAppraisal : IPassiveSkillEffect
    {
        public string SkillId => "刃毀れの見立て";
        public const int Pen = 12, NotchPct = 25;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.critArmorPen += Pen; return; }
            if (!Psn.Hit(ctx) || !ctx.isCritical) return;
            int add = ctx.finalDamage * NotchPct / 100;
            if (add > 0) CombatManager.Instance?.RaiseBossNextNotch(add);
        }
    }

    /// <summary>渾身の柄頭: 攻撃の端子を 1 枚だけ発動したターン、 その撃が会心なら最終与ダメージ +35%・ブロック +6。</summary>
    public class HeftyPommel : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritPommel;
        public const int FinalPct = 35, Block = 6;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Crit.SoloHit(ctx)) return;
            ctx.hitFinalDamagePct += FinalPct;   // 最終与ダメージ倍率はこの後 (ApplyWinDamageModifiers)
            ctx.itemBlockBonus += Block;
        }
    }

    // ── G: 分岐 A〈確定会心・攻防一体連打〉──

    /// <summary>瑠璃砥石: 会心率 +20%。 同じターンに会心が 2 回以上出たとき (1 ターン 1 回)、 この戦闘中の会心倍率 +0.15 (累積・最大 +1.50・次の撃から)。</summary>
    public class LapisWhetstone : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "瑠璃砥石";
        public const float Rate = 0.20f, MulPer = 0.15f, MulMax = 1.50f;
        /// <summary>[persistent] 積もった会心倍率。</summary>
        private const string Key = "crit.lapis", TurnKey = "crit.lapis.t";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count((int)System.Math.Round(MulNow(ctx) / MulPer));
        public static float MulNow(CombatContext ctx) => ctx.GetAccumulated(Key);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnCriticalCheck, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            switch (t)
            {
                case PassiveSkillTrigger.OnWired: ctx.critRatePctAdd += Rate; break;
                case PassiveSkillTrigger.OnCriticalCheck: ctx.criticalMultiplier += ctx.GetAccumulated(Key); break;   // 会心倍率は撃ごとに戻るので、 撃ごとに足す
                default:
                    if (Crit.ThisTurn(ctx) >= 2 && Rw.OncePerTurn(ctx, TurnKey))
                        ctx.accumulatedValues[Key] = System.Math.Min(MulMax, ctx.GetAccumulated(Key) + MulPer);
                    break;
            }
        }
    }

    /// <summary>斬り重ねの鞘: 会心を出すたび、 会心倍率 +0.30 が積もる。 ターン開始時、 積もった分の 1/4 を失う (前のターンに会心が 3 回以上出ていたら失わない)。</summary>
    public class LayeredScabbard : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "斬り重ねの鞘";
        public const float MulPer = 0.30f;
        public const int KeepAt = 3;
        private const string Key = "crit.scabbard";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Count(ctx, Key));
        public static float MulNow(CombatContext ctx) => MulPer * Rw.Count(ctx, Key);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage, PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Rw.Count(ctx, Key);
            switch (t)
            {
                case PassiveSkillTrigger.OnCriticalDamage: ctx.accumulatedValues[Key] = n + 1; break;
                case PassiveSkillTrigger.OnTurnStart:
                    bool keep = Crit.Count(ctx, Crit.LastKey) == ctx.currentTurn - 1 && Crit.Count(ctx, Crit.TurnNKey) >= KeepAt;   // 前のターンの会心の数 (このターンはまだ数えていない)
                    if (!keep) ctx.accumulatedValues[Key] = n - (n + 3) / 4;   // 1/4 を失う (端数は切り上げて減らす)
                    break;
                default: ctx.criticalMultiplier += MulPer * n; break;
            }
        }
    }

    /// <summary>重ね斬りの型: 同じターンに会心が 2 撃続いたとき、 3 撃目以降の撃は会心倍率 +1.0 (その撃が会心した時に乗る)・その撃が会心したらブロック +5。</summary>
    public class LayeredCutForm : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "重ね斬りの型";
        public const float BonusMul = 1.0f;
        public const int Block = 5;
        /// <summary>[perTurn 相当] 続いた会心の数 / その数を数えたターンの番号 (ターンが変わったら 0 から)。</summary>
        private const string StreakKey = "crit.layeredCut.streak", TurnKey = "crit.layeredCut.turn";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Streak(ctx) >= 2);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            if (t == PassiveSkillTrigger.OnCriticalCheck)
            {
                if (Streak(ctx) >= 2) ctx.criticalMultiplier += BonusMul;   // 会心倍率は撃ごとに戻るので、 この撃だけに乗る
                return;
            }
            bool crit = ctx.isCritical && ctx.finalDamage > 0;
            if (crit && Streak(ctx) >= 2) ctx.itemBlockBonus += Block;
            ctx.accumulatedValues[StreakKey] = crit ? Streak(ctx) + 1 : 0;
            ctx.accumulatedValues[TurnKey] = ctx.currentTurn;
        }

        private static int Streak(CombatContext ctx)
            => Rw.Count(ctx, TurnKey) == ctx.currentTurn ? Rw.Count(ctx, StreakKey) : 0;
    }

    /// <summary>連なる閃き: 前のターンに会心を出していて、 このターンも会心を出したとき、 その会心の撃とこのターンの後の撃の最終与ダメージ +45% (続く間は毎ターン)。</summary>
    public class ChainedFlash : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "連なる閃き";
        public const int FinalPct = 45;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Crit.LastTurn(ctx) && Crit.ThisTurn(ctx) > 0);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit || !Crit.LastTurn(ctx)) return;
            // OnPreDealDamage: この撃より前にこのターンの会心が出ている / OnCriticalDamage: この撃がこのターン最初の会心 (重ねない)
            int n = Crit.ThisTurn(ctx);
            if (t == PassiveSkillTrigger.OnPreDealDamage ? n > 0 : n == 1) ctx.hitFinalDamagePct += FinalPct;
        }
    }

    // ── G: 分岐 B〈極大倍率・一撃必殺〉──

    /// <summary>抜き打ちの作法: 各戦闘の 1 ターン目と、 敵のダウン中のターンの最初の 1 撃は確定会心・会心倍率 +1.0。</summary>
    public class QuickDrawArt : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.CritQuickDraw;
        public const float Mul = 1.0f;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.currentTurn <= 1 || (CombatManager.Instance?.EnemyDownNow ?? false));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!FirstHitForced(ctx, CombatManager.Instance?.EnemyDownNow ?? false)) return;
            ctx.forceCritThisHit = true;
            ctx.criticalMultiplier += Mul;
        }

        /// <summary>この撃が確定会心になる撃か (1 ターン目かダウン中の最初の 1 撃)。 BOT の見積もりも読む。</summary>
        public static bool FirstHitForced(CombatContext ctx, bool enemyDown)
            => ctx != null && ctx.hitIndex == 0 && ctx.currentTurn >= 1 && (ctx.currentTurn == 1 || enemyDown);
    }

    // ── L: 規則破壊 ──

    /// <summary>寸鉄【規則】: 超過会心率の会心倍率への変換が 2 倍 (超過 1% ごとに +0.02・CritCore.ExcessMul)。 会心した撃は敵の防御を完全に無視する。</summary>
    public class Suntetsu : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritSuntetsu;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            ctx.critExcessDouble = true;
            ctx.critArmorPen += DefenseFormula.IgnoreAll;
        }
    }

    /// <summary>一点読みの片眼【規則】: 会心率 +35%・会心倍率 +0.80。 代わりに会心しなかった撃の与ダメージ −40%。
    /// 代価は PassiveSkillManager の会心判定の後で掛ける (<see cref="ApplyCost"/> が <see cref="NonCritCutKey"/> を立てる)。</summary>
    public class PinpointEye : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritPinpoint;
        public const string NonCritCutKey = "crit.pinpoint.noncrit";
        public const int NonCritKeepPct = 60;
        public const float Rate = 0.35f, Mul = 0.80f;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            ctx.critRatePctAdd += Rate;
            ctx.criticalMultiplier += Mul;
        }
        /// <summary>代価の印を<b>戦闘の情報を作った直後</b>に所持品から立てる (PassiveSkillManager.BeginCombat)。</summary>
        public static void ApplyCost(CombatContext ctx, RunState run)
        {
            if (ctx != null && AxisRules.Owns(run, ItemIds.CritPinpoint)) ctx.accumulatedValues[NonCritCutKey] = 1;
        }
    }

    /// <summary>千里眼の水晶: 会心を 1 ターンに 2 回以上出したとき (1 ターン 1 回)、 次のターンの手札 +1 枚・次のターンの会心率 +40%・次のターンの全部の撃の最終与ダメージ +25%。</summary>
    public class ClairvoyantCrystal : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.CritClairvoyant;
        public const float Rate = 0.40f;
        public const int Draw = 1, FinalPct = 25;
        /// <summary>[persistent] 会心を 2 回以上出した直近のターン。</summary>
        private const string Key = "crit.clairvoyant";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Shd.LastTurn(ctx, Key));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage, PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            switch (t)
            {
                case PassiveSkillTrigger.OnCriticalDamage:
                    if (Crit.ThisTurn(ctx) >= 2 && Rw.OncePerTurn(ctx, Key)) { Rw.DrawNext(Draw); Rw.Log("[千里眼の水晶] このターン会心 2 回 → 次のターン 手札 +1・会心率 +40%・最終与ダメージ +25%"); }
                    break;
                case PassiveSkillTrigger.OnWired: if (Primed(ctx)) ctx.critRatePctAdd += Rate; break;
                default: if (ctx.IsRealHit && Primed(ctx)) ctx.hitFinalDamagePct += FinalPct; break;
            }
        }

        /// <summary>前のターンに会心を 2 回以上出した (このターンに上乗せが働く)。 BOT の見積もりも読む。</summary>
        public static bool Primed(CombatContext ctx) => ctx != null && Shd.LastTurn(ctx, Key);
    }

    /// <summary>天眼の鍔: 会心の一撃のあと、 敵の HP が「会心倍率 × 6%」(最大 30%・ボスはその半分) 以下なら倒す。 さらに確定会心 (会心率 100% 以上) の撃は会心倍率 ×1.25 (CritCore.EffectiveMul)。
    /// 会心倍率はその撃に掛かった倍率 (<see cref="Kw.LastCritMulKey"/>)。 判定と倒すのは CombatManager (撃を HP から引いた直後・<see cref="KillLine"/>)。</summary>
    public class HeavenEyeGuard : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritHeaven;
        public const float PctPerMul = 0.06f, MaxPct = 0.30f;
        /// <summary>[persistent] この品を持っている (CombatManager・PassiveSkillManager が読む)。</summary>
        public const string HeldKey = "crit.tengan.held";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.accumulatedValues[HeldKey] = 1;

        public static bool Held(CombatContext ctx) => ctx != null && ctx.GetAccumulated(HeldKey) > 0f;

        /// <summary>倒せる HP の線 (これ以下なら倒す・持っていない・会心倍率が無い時は −1)。</summary>
        public static int KillLine(CombatContext ctx)
        {
            if (!Held(ctx) || ctx.enemyMaxHP <= 0) return -1;
            float frac = System.Math.Min(MaxPct, ctx.GetAccumulated(Kw.LastCritMulKey) * PctPerMul);
            if (BountyHunterHelper.IsBoss(ctx)) frac /= 2f;
            return frac > 0f ? (int)(ctx.enemyMaxHP * frac) : -1;
        }
    }

    /// <summary>死相の面頬: 会心したターン、 そのターンの敵の攻撃を (会心倍率 − 1) × 25% 減らす (最大 −60%)。 会心した撃は敵のシールド (ヴェスカの守り) に吸われない。
    /// 減らすのは敵の攻撃の解決 (CombatManager.ApplyLossDamageModifiers)・吸われないのは CombatManager.ApplyVescaDefense。</summary>
    public class DeathMaskVisor : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.CritMask;
        public const float PerMul = 0.25f, MaxCut = 0.60f;
        /// <summary>会心したターンの番号 / そのターンの減らす割合 (同じターンに何度会心しても大きい方 1 つ) / この品を持っている。</summary>
        public const string TurnKey = "crit.deathMask.turn", CutKey = "crit.deathMask.cut", HeldKey = "crit.deathMask.held";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(CutNow(ctx) > 0f);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.accumulatedValues[HeldKey] = 1; return; }
            float cut = Cut(ctx.criticalMultiplier + CritCore.ExcessMul(ctx.lastCritAddPct, ctx.critExcessDouble));   // 超過会心率の分も会心倍率に数える
            if (Rw.Count(ctx, TurnKey) != ctx.currentTurn) { ctx.accumulatedValues[TurnKey] = ctx.currentTurn; ctx.accumulatedValues[CutKey] = 0f; }
            if (cut > ctx.GetAccumulated(CutKey)) ctx.accumulatedValues[CutKey] = cut;
        }

        /// <summary>会心倍率 <paramref name="critMul"/> の時に減らす割合。 BOT の見積もりも読む。</summary>
        public static float Cut(float critMul) => System.Math.Min(MaxCut, System.Math.Max(0f, (critMul - 1f) * PerMul));

        /// <summary>このターンの敵の攻撃を減らす割合 (会心していないターンは 0)。</summary>
        public static float CutNow(CombatContext ctx)
            => ctx != null && ctx.currentTurn > 0 && Rw.Count(ctx, TurnKey) == ctx.currentTurn ? ctx.GetAccumulated(CutKey) : 0f;

        /// <summary>会心した撃が敵のシールドに吸われない (この品を持っている)。</summary>
        public static bool Pierces(CombatContext ctx) => ctx != null && ctx.GetAccumulated(HeldKey) > 0f;
    }
}

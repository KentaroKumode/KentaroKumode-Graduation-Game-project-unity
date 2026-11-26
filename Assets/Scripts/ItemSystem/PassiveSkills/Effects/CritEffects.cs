using CombatSystem;
using GameLoop;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  会心の軸 27 品 (2026-11-21・full-item-rework-ledger §⑥・docs/GAME.md §7-6・2026-11-23 会心の仕組みの転換 = crit_redesign/neon/answer8_final_impl.md)。
    //  共通 9 品 / 分岐 A〈練気の維持〉9 品 / 分岐 B〈集中〉9 品。 会心の品が配る確定会心は 0。
    //  【練気】(会心で +1・戦闘の中だけ) と【集中】(集中の品 3 つ以上・攻撃の端子 1 枚の撃・会心率から賭け金を引く) の式は CritCore。
    //  〈必殺の照準器〉は旗だけ (本体の撃の輪がもう 1 回発火する・CombatManager.Turn)。 ここは出来事で働く 26 品。
    //  集中会心で倍率を足す品は OnCriticalDamage で足す ── PassiveSkillManager.ProcessDamage は OnCriticalDamage を鳴らしてから会心倍率を掛けるので、 その撃に乗る。
    // ============================================================

    /// <summary>会心の軸の小さな道具。 このターンに出した会心の数・会心を出した直近の 2 つのターン・【練気】を数える (PassiveSkillManager が会心のたびに <see cref="Note"/>)。</summary>
    public static class Crit
    {
        /// <summary>[persistent・戦闘で 0 から] このターンの会心の数 (Psn.TurnN) / 会心を出した直近のターン / その前に会心を出したターン。</summary>
        public const string TurnNKey = "crit.n", LastKey = "crit.last", PrevKey = "crit.prev";
        /// <summary>[persistent・戦闘で 0 から] この戦闘の会心の回数 (端子〈一閃〉〈弱点看破〉・2026-11-22) / 会心を出した直近のターンの会心の数 (〈余韻の鈴〉〈連なる閃き〉の「前のターンに 2 回以上」)。</summary>
        public const string TotalKey = "crit.total", LastNKey = "crit.lastN", PrevNKey = "crit.prevN";
        /// <summary>[persistent・戦闘で 0 から] 【練気】/ 直近の会心で +1 した後の【練気】(使う前・〈寸鉄〉〈死相の面頬〉が読む) /
        /// このターンの会心の【練気】の最大 (使う前) とそのターンの番号 (〈死相の面頬〉の軽減)。</summary>
        public const string RenkiKey = "crit.renki", HitRenkiKey = "crit.hitRenki", RenkiTurnKey = "crit.renki.turn", RenkiTurnMaxKey = "crit.renki.turnMax";

        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);

        /// <summary>今の【練気】の上限 (基本 12・〈寸鉄〉所持で 18)。</summary>
        public static int CapNow() => CritCore.RenkiCap + (Rw.Run != null && Rw.Run.OwnsPassive(ItemIds.CritSuntetsu) ? Suntetsu.CapAdd : 0);
        /// <summary>今の【練気】。</summary>
        public static int Renki(CombatContext ctx) => ctx == null ? 0 : System.Math.Min(CapNow(), Count(ctx, RenkiKey));
        public static void AddRenki(CombatContext ctx, int n)
        {
            if (ctx != null && n != 0) ctx.accumulatedValues[RenkiKey] = System.Math.Min(CapNow(), System.Math.Max(0, Renki(ctx) + n));
        }

        /// <summary>プレイヤーの撃が会心した時の【練気】(PassiveSkillManager.ProcessDamage が OnCriticalDamage の前に呼ぶ)。 +1 して、 集中の撃なら半分 (切り上げ) を使う。 戻り値 = 使った量。</summary>
        public static int GainOnCrit(CombatContext ctx)
        {
            if (ctx == null || CritCore.CritRelicCount(Rw.Run) <= 0) return 0;   // 会心の軸の秘宝を持たない戦闘では溜まらない (answer9)
            int after = System.Math.Min(CapNow(), Renki(ctx) + 1);
            ctx.accumulatedValues[RenkiKey] = after;
            ctx.accumulatedValues[HitRenkiKey] = after;
            if (Count(ctx, RenkiTurnKey) != ctx.currentTurn) { ctx.accumulatedValues[RenkiTurnKey] = ctx.currentTurn; ctx.accumulatedValues[RenkiTurnMaxKey] = after; }
            else if (after > Count(ctx, RenkiTurnMaxKey)) ctx.accumulatedValues[RenkiTurnMaxKey] = after;
            if (!ctx.hitIsFocused) return 0;
            int spent = CritCore.RenkiSpend(after);
            ctx.accumulatedValues[RenkiKey] = after - spent;
            ctx.hitFocusSpentRenki = spent;
            return spent;
        }
        /// <summary>直近の会心で +1 した後の【練気】(使う前)。</summary>
        public static int HitRenki(CombatContext ctx) => ctx == null ? 0 : Count(ctx, HitRenkiKey);
        /// <summary>このターンに会心した時の【練気】の最大 (使う前・会心していないターンは 0)。</summary>
        public static int CritRenkiThisTurn(CombatContext ctx)
            => ctx != null && ctx.currentTurn > 0 && Count(ctx, RenkiTurnKey) == ctx.currentTurn ? Count(ctx, RenkiTurnMaxKey) : 0;

        /// <summary>プレイヤーの撃が会心した (OnCriticalDamage を配る直前に PassiveSkillManager が呼ぶ)。</summary>
        public static void Note(CombatContext ctx)
        {
            if (ctx == null) return;
            int last = Count(ctx, LastKey);
            if (last != ctx.currentTurn)
            {
                ctx.accumulatedValues[PrevKey] = last; ctx.accumulatedValues[LastKey] = ctx.currentTurn;
                ctx.accumulatedValues[PrevNKey] = Count(ctx, LastNKey); ctx.accumulatedValues[LastNKey] = 0;
            }
            Psn.AddTurnN(ctx, TurnNKey, 1);
            ctx.accumulatedValues[LastNKey] = Count(ctx, LastNKey) + 1;
            ctx.accumulatedValues[TotalKey] = Count(ctx, TotalKey) + 1;
        }

        /// <summary>前のターンに出した会心の数 (このターンに会心が出た後でも前のターンの数を返す)。</summary>
        public static int LastTurnCount(CombatContext ctx)
        {
            int cur = ctx.currentTurn;
            if (cur <= 1) return 0;
            if (Count(ctx, LastKey) == cur - 1) return Count(ctx, LastNKey);
            if (Count(ctx, LastKey) == cur && Count(ctx, PrevKey) == cur - 1) return Count(ctx, PrevNKey);
            return 0;
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

        /// <summary>攻撃の端子を 1 枚だけ発動したターンの、 その撃 (【集中】の入口・〈必殺の照準器〉)。</summary>
        public static bool SoloHit(CombatContext ctx) => ctx.IsRealHit && ctx.hitCount == 1;

        /// <summary>この撃が集中会心 (OnCriticalDamage・OnPostDealDamage の中で読む)。</summary>
        public static bool FocusCrit(CombatContext ctx) => ctx.IsRealHit && ctx.hitIsFocused && ctx.isCritical;

        /// <summary>BOT の配線の見積もり: 配線の前に分かる、 このターンの全部の撃に乗る会心率・会心倍率・会心率の乗算 (所持品の条件は品と同じ判定・量は品の定数)。
        /// 撃ごとに決まる分 (〈余韻の鈴〉〈鷹匠の革手袋〉〈狙い目の刻み〉〈見切りの目印〉〈脈打ちの測り針〉〈初太刀の黒鞘〉・集中会心の品) は BOT が撃ごとに見る。</summary>
        public static void ForecastTurn(CombatContext ctx, RunState run, out float rate, out float mul, out float rateMul)
        {
            rate = 0f; mul = 0f; rateMul = 0f;
            if (ctx == null || run == null) return;
            if (run.OwnsPassive(ParryFan.Id) && ParryFan.Primed(ctx)) rate += ParryFan.Rate;
            if (run.OwnsPassive(ItemIds.CritLapis)) { rate += LapisWhetstone.RateNow(ctx); mul += LapisWhetstone.MulNow(ctx); }
            if (run.OwnsPassive(LayeredScabbard.Id)) mul += LayeredScabbard.MulNow(ctx);
            if (run.OwnsPassive(ItemIds.CritPinpoint)) { rate += PinpointEye.Rate; mul += PinpointEye.MulNow(ctx); }
            if (run.OwnsPassive(ItemIds.CritClairvoyant) && ClairvoyantCrystal.Primed(ctx)) rate += ClairvoyantCrystal.Rate;
            if (run.OwnsPassive(ItemIds.CritSurveyor)) { rateMul += SurveyorMonocle.RateMulNow(ctx); mul += SurveyorMonocle.MulNow(ctx); }
            if (run.OwnsPassive(ItemIds.CritOneFlash)) mul += OneFlashMastery.MulNow(ctx);
        }
        public static void ForecastTurn(CombatContext ctx, RunState run, out float rate, out float mul) => ForecastTurn(ctx, run, out rate, out mul, out _);
    }

    // ===================== 共通 9 品 =====================

    /// <summary>鷹匠の革手袋: 攻撃の端子に 4 以上の目を挿したターン、 会心率 +12%・会心倍率 +0.15 (answer9・旧 +10%)。</summary>
    public class FalconerGlove : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "鷹匠の革手袋";
        public string SkillId => Id;
        public const int MinFace = 4;
        public const float Rate = 0.12f, Mul = 0.15f;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Rw.Count(ctx, MutualKeys.AttackDiceMax) >= MinFace);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, MutualKeys.AttackDiceMax) < MinFace) return;
            ctx.critRatePctAdd += Rate;
            ctx.criticalMultiplier += Mul;     // 配線の後に足した倍率は、 そのターンの全部の撃に乗る (撃の輪がこの値へ戻す)
        }
    }

    /// <summary>狙い目の刻み: 攻撃の端子に 4 の目を挿した撃は会心率 +10%、 5 なら +16%、 6 なら +26% (answer9・確定会心なし)。 撃の目は ctx.hitFaceMax (CritCore.HitFaceMax)。</summary>
    public class AimNotch : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritAim;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            ctx.critRatePctAdd += CritCore.AimRate(ctx.hitFaceMax);      // 会心率は撃ごとに戻るので、 この撃だけに乗る
        }
    }

    /// <summary>受け流しの扇: 防御の端子を 1 枚以上発動して (【再起動】も数える)、 ブロックとシールドで敵の攻撃を完封した (HP を 1 も失わなかった) 次のターン、
    /// 会心率 +18%・ブロック +5・そのターンの最初の会心の撃で敵の防御 −8 (貫通 +8・そのターンの残りの撃にも) (answer9: 旧 2 枚以上・+14%・+4)。</summary>
    public class ParryFan : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "受け流しの扇";
        public string SkillId => Id;
        public const float Rate = 0.18f;
        public const int DefCut = 8, Block = 5, NeedBlockTerminals = 1;
        private const string Key = "crit.parry", PenKey = "crit.parry.pen";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Primed(ctx));
        public static bool Primed(CombatContext ctx) => Shd.LastTurn(ctx, Key);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            switch (t)
            {
                case PassiveSkillTrigger.OnTurnEnd: if (Psn.Shutout(ctx) && Fam.BlockTerminals() >= NeedBlockTerminals) ctx.accumulatedValues[Key] = ctx.currentTurn; break;
                case PassiveSkillTrigger.OnWired: if (Shd.LastTurn(ctx, Key)) { ctx.critRatePctAdd += Rate; ctx.itemBlockBonus += Block; } break;
                default:
                    if (ctx.IsRealHit && Shd.LastTurn(ctx, Key) && Rw.OncePerTurn(ctx, PenKey)) ctx.playerArmorPen += DefCut;   // 防御の計算はこの後 (ApplyWinDamageModifiers)
                    break;
            }
        }
    }

    /// <summary>測量師の片眼鏡 (2026-11-26 会心率純化): 前の戦闘からマップを 1 歩進むごとに蓄積する (歩数は戦闘で 0 に戻る)。 この戦闘の間ずっと、 1 歩につき会心率 ×(1 + 0.05) (倍率加算なし)。</summary>
    public class SurveyorMonocle : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.CritSurveyor;
        public const float RateMulPerStep = 0.05f, MulPerStep = 0f, MaxMul = 0f;
        /// <summary>[persistent] 戦闘の始めの歩数。</summary>
        private const string Key = "crit.surveyor.steps";
        public static int Steps(CombatContext ctx) => Rw.Count(ctx, Key);
        public static float RateMulNow(CombatContext ctx) => RateMulPerStep * Steps(ctx);
        public static float MulNow(CombatContext ctx) => 0f;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Steps(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.accumulatedValues[Key] = System.Math.Max(0, Rw.Steps); return; }
            if (!ctx.IsRealHit) return;
            ctx.hitCritRateMulAdd += RateMulNow(ctx);   // 撃ごとに戻る (CombatManager の撃の輪)
        }
    }

    /// <summary>銑鉄棍【規則】: 会心しなかった撃に、 会心率 1% につき与ダメージ +1% (会心率は 100% を上限に数える・与ダメ % の池)。 会心は普通に出る (PassiveSkillManager.ProcessDamage)。</summary>
    public class UnwhettedClub : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritIronClub;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critToDamage = true;
    }

    /// <summary>脈打ちの測り針 (2026-11-26 会心率寄りに調整): 直前の撃が会心だったとき、 次の 1 撃の会心率 +22%・会心倍率 +0.12 (ターンをまたいで持ち越す・会心しなかった撃で消える)。</summary>
    public class PulseNeedle : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.CritPulse;
        public const float Rate = 0.22f, Mul = 0.12f;
        /// <summary>[persistent] 直前の本物の撃が会心だった (1 / 0)。</summary>
        public const string Key = "crit.pulse";
        public static bool Primed(CombatContext ctx) => ctx != null && ctx.GetAccumulated(Key) > 0f;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Primed(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            if (t == PassiveSkillTrigger.OnCriticalCheck) { if (Primed(ctx)) { ctx.critRatePctAdd += Rate; ctx.criticalMultiplier += Mul; } return; }
            ctx.accumulatedValues[Key] = ctx.isCritical ? 1f : 0f;
        }
    }

    /// <summary>瑠璃砥石 (2026-11-26 上限 8 回追加): 会心率 +5%。 同じターンの 2 回目以降の会心か集中会心を出すたび、 この戦闘中の会心率 +4%・会心倍率 +0.04 (最大 8 回 = +32%・+0.32・次の撃から)。</summary>
    public class LapisWhetstone : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.CritLapis;
        public const float Rate = 0.05f, RatePer = 0.04f, MulPer = 0.04f;
        public const int MaxStacks = 8;
        /// <summary>[persistent] 積もった回数。</summary>
        private const string CountKey = "crit.lapis.n";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(System.Math.Min(MaxStacks, Rw.Count(ctx, CountKey)));
        /// <summary>次の撃に乗る会心率・会心倍率 (BOT の見積もり Crit.ForecastTurn・〈死相の面頬〉も読む)。</summary>
        public static float RateNow(CombatContext ctx) => Rate + RatePer * System.Math.Min(MaxStacks, Rw.Count(ctx, CountKey));
        public static float MulNow(CombatContext ctx) => MulPer * System.Math.Min(MaxStacks, Rw.Count(ctx, CountKey));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnCriticalCheck)
            {
                ctx.critRatePctAdd += RateNow(ctx);       // 会心率・会心倍率は撃ごとに戻るので、 撃ごとに足す
                ctx.criticalMultiplier += MulNow(ctx);
                return;
            }
            if (!ctx.IsRealHit || (Crit.ThisTurn(ctx) < 2 && !ctx.hitIsFocused)) return;
            int cur = Rw.Count(ctx, CountKey);
            if (cur < MaxStacks) ctx.accumulatedValues[CountKey] = cur + 1;
        }
    }

    /// <summary>一点読みの片眼【規則】: 会心率 +22%・会心倍率 +(0.25 + 今の【練気】× 0.08) (最大 +0.89)。 代わりに会心しなかった撃の与ダメージ −45%。</summary>
    public class PinpointEye : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritPinpoint;
        public const string NonCritCutKey = "crit.pinpoint.noncrit";
        public const int NonCritKeepPct = 55;
        public const float Rate = 0.22f, BaseMul = 0.25f, MulPerRenki = 0.08f, MaxMul = 0.89f;
        public static float MulNow(CombatContext ctx) => System.Math.Min(MaxMul, BaseMul + MulPerRenki * Crit.Renki(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            ctx.critRatePctAdd += Rate;
            ctx.criticalMultiplier += MulNow(ctx);
        }
        /// <summary>代価の印を<b>戦闘の情報を作った直後</b>に所持品から立てる (PassiveSkillManager.BeginCombat)。</summary>
        public static void ApplyCost(CombatContext ctx, RunState run)
        {
            if (ctx != null && AxisRules.Owns(run, ItemIds.CritPinpoint)) ctx.accumulatedValues[NonCritCutKey] = 1;
        }
    }

    /// <summary>死相の面頬 (2026-11-26 軽減上限追加): 会心を出したターン、 そのターンの敵の直接攻撃を「【練気】÷ (【練気】＋ 8)」だけ減らす (上限 45%、 敵の大技のターンは【練気】2 倍・上限 55%)。
    /// さらに【練気】3 以上の会心と集中会心は敵のシールド (ヴェスカの守り) に吸われない (CombatManager.ApplyVescaDefense)。</summary>
    public class DeathMaskVisor : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.CritMask;
        public const float Div = 8f, HeavyRenkiMul = 2f, MaxCut = 0.45f, MaxCutHeavy = 0.55f;
        public const int PierceRenki = 3;
        /// <summary>[persistent] この品を持っている。</summary>
        public const string HeldKey = "crit.deathMask.held";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(CutNow(ctx, false) > 0f);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.accumulatedValues[HeldKey] = 1;

        public static bool Held(CombatContext ctx) => ctx != null && ctx.GetAccumulated(HeldKey) > 0f;

        /// <summary>【練気】<paramref name="renki"/> の時に減らす割合 (大技のターンは 2 倍の練気で数え、 上限 45% / 55%)。 BOT の見積もりも読む。</summary>
        public static float Cut(int renki, bool heavy)
        {
            if (renki <= 0) return 0f;
            float x = heavy ? renki * HeavyRenkiMul : renki;
            float c = x / (x + Div);
            float cap = heavy ? MaxCutHeavy : MaxCut;
            return c > cap ? cap : c;
        }

        /// <summary>このターンの敵の直接攻撃を減らす割合 (会心していないターン・持っていない時は 0)。</summary>
        public static float CutNow(CombatContext ctx, bool heavy) => Held(ctx) ? Cut(Crit.CritRenkiThisTurn(ctx), heavy) : 0f;

        /// <summary>会心した撃が敵のシールドに吸われない (持っていて、 集中会心か【練気】5 以上の会心)。 <paramref name="hitRenki"/> = その会心で +1 した後の【練気】。</summary>
        public static bool PiercesHit(CombatContext ctx, bool focused, int hitRenki) => Held(ctx) && (focused || hitRenki >= PierceRenki);
    }

    // ===================== 分岐 A〈練気の維持〉9 品 =====================

    /// <summary>余韻の鈴: 前のターンに出した会心 1 回につき、 このターンの会心率 +10%・ブロック +4。</summary>
    public class LingeringBell : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "余韻の鈴";
        public string SkillId => Id;
        public const float RatePer = 0.10f;
        public const int BlockPer = 4, MinLastCrits = 1;
        public static int StacksNow(CombatContext ctx) => ctx == null ? 0 : System.Math.Max(0, Crit.LastTurnCount(ctx) - (MinLastCrits - 1));
        public static float RateNow(CombatContext ctx) => RatePer * StacksNow(ctx);
        public static int BlockNow(CombatContext ctx) => BlockPer * StacksNow(ctx);
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(StacksNow(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int s = StacksNow(ctx);
            if (s <= 0) return;
            ctx.critRatePctAdd += RatePer * s;
            ctx.itemBlockBonus += BlockPer * s;
        }
    }

    /// <summary>見切りの目印: 同じターンに攻撃の端子を 2 枚以上発動したとき、 2 撃目以降の撃の会心率 +14% (【練気】3 以上なら +20%)。</summary>
    public class ReadMark : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritReadMark;
        public const float Rate = 0.14f, RateHigh = 0.20f;
        public const int HighRenki = 3, NeedHits = 2;
        public static float RateFor(int renki) => renki >= HighRenki ? RateHigh : Rate;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit || ctx.hitCount < NeedHits || ctx.hitIndex < 1) return;
            ctx.critRatePctAdd += RateFor(Crit.Renki(ctx));
        }
    }

    /// <summary>研ぎ師の見立て書: 会心を出すたび、 そのターンのブロック +2。 同じターンに 2 回以上会心を出したら、 さらにブロック +6 (1 ターン 1 回)。</summary>
    public class WhetAppraisal : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "研ぎ師の見立て書";
        public const int Block = 2, BonusBlock = 6;
        private const string BlockKey = "crit.whet.block", BonusKey = "crit.whet.heal";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Psn.TurnN(ctx, BlockKey));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            Psn.AddTurnN(ctx, BlockKey, Block); ctx.itemBlockBonus += Block;
            if (Crit.ThisTurn(ctx) >= 2 && Rw.OncePerTurn(ctx, BonusKey))
            {
                Psn.AddTurnN(ctx, BlockKey, BonusBlock); ctx.itemBlockBonus += BonusBlock;
                Rw.Log($"[研ぎ師の見立て書] このターン会心 {Crit.ThisTurn(ctx)} 回 → ブロック +{BonusBlock}");
            }
        }
    }

    /// <summary>斬り重ねの鞘 (2026-11-26 ターン終了蓄積化): 会心を出したターン終了時、 会心倍率 +0.08・防御 +1 が 1 層蓄積する (最大 6 層)。
    /// 会心を出さなかった次のターン開始時に蓄積の半分 (切り上げ) を失うが、 【練気】5 以上なら失わない。</summary>
    public class LayeredScabbard : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "斬り重ねの鞘";
        public string SkillId => Id;
        public const float MulPer = 0.08f;
        public const int DefPer = 1, MaxStacks = 6, KeepRenki = 5;
        private const string Key = "crit.scabbard";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Count(ctx, Key));
        public static float MulNow(CombatContext ctx) => MulPer * System.Math.Min(MaxStacks, Rw.Count(ctx, Key));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = System.Math.Min(MaxStacks, Rw.Count(ctx, Key));
            switch (t)
            {
                case PassiveSkillTrigger.OnTurnStart:
                    if (ctx.currentTurn > 1 && Crit.Count(ctx, Crit.LastKey) != ctx.currentTurn - 1 && Crit.Renki(ctx) < KeepRenki && n > 0)
                        ctx.accumulatedValues[Key] = n - (n + 1) / 2;
                    break;
                case PassiveSkillTrigger.OnPostRoll:
                    if (n > 0) ctx.playerDefense += DefPer * n;
                    break;
                case PassiveSkillTrigger.OnTurnEnd:
                    if (Crit.ThisTurn(ctx) > 0 && n < MaxStacks) ctx.accumulatedValues[Key] = n + 1;
                    break;
                default:
                    if (n > 0) ctx.criticalMultiplier += MulPer * n;
                    break;
            }
        }
    }

    /// <summary>重ね斬りの型 (2026-11-26 練気比例化): 同じターンに会心が 2 撃続いたとき、 3 撃目以降の撃は会心率 +25%・会心倍率 +(今の【練気】× 0.03)・その撃が会心したらブロック +6。</summary>
    public class LayeredCutForm : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "重ね斬りの型";
        public const float BonusRate = 0.25f, MulPerRenki = 0.03f;
        public const int Block = 6;
        private const string StreakKey = "crit.layeredCut.streak", TurnKey = "crit.layeredCut.turn";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Streak(ctx) >= 2);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            if (t == PassiveSkillTrigger.OnCriticalCheck)
            {
                if (Streak(ctx) >= 2) { ctx.critRatePctAdd += BonusRate; ctx.criticalMultiplier += MulPerRenki * Crit.Renki(ctx); }
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

    /// <summary>連なる閃き (2026-11-26 練気比例化): 前のターンに会心を 1 回以上出していて、 このターンも会心を出したとき、 ブロック +6 (1 ターン 1 回)・その会心の撃と以降の撃の最終与ダメージ +(今の【練気】× 2)% (最大 +36%)。</summary>
    public class ChainedFlash : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "連なる閃き";
        public const int PctPerRenki = 2, MaxPct = 36, Block = 6, Need = 1;
        private const string BlockTurnKey = "crit.chainedFlash.blk";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Crit.LastTurnCount(ctx) >= Need && Crit.ThisTurn(ctx) > 0);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit || Crit.LastTurnCount(ctx) < Need) return;
            int n = Crit.ThisTurn(ctx);
            if (t == PassiveSkillTrigger.OnCriticalDamage && n == 1 && Rw.OncePerTurn(ctx, BlockTurnKey))
                ctx.itemBlockBonus += Block;
            if (t == PassiveSkillTrigger.OnPreDealDamage ? n > 0 : n == 1)
                ctx.hitFinalDamagePct += System.Math.Min(MaxPct, Crit.Renki(ctx) * PctPerRenki);
        }
    }

    /// <summary>白刃の鏡砥: 会心した撃は、 今の【練気】1 ごとに会心倍率 +0.04。 同じターンの 2 回目以降の会心を出すたび、 そのターンのブロック +6。</summary>
    public class WhiteBladeMirror : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritWhiteBlade;
        public const float MulPerRenki = 0.04f;
        public const int Block = 6;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            ctx.criticalMultiplier += MulPerRenki * Crit.Renki(ctx);   // この後で会心倍率を掛ける (PassiveSkillManager.ProcessDamage)
            if (Crit.ThisTurn(ctx) >= 2) ctx.itemBlockBonus += Block;
        }
    }

    /// <summary>寸鉄【規則】(2026-11-26 練気上限解放化): 【練気】の上限 +6 (12 → 18)。 【練気】4 以上の撃が会心したとき、 敵の防御を完全に無視する。</summary>
    public class Suntetsu : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritSuntetsu;
        public const int CapAdd = 6, IgnoreRenki = 4;
        public PassiveSkillTrigger[] Triggers => System.Array.Empty<PassiveSkillTrigger>();
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) { }
    }

    /// <summary>千里眼の水晶 (2026-11-26 安定機関化): 会心を 1 ターンに 2 回以上出したか、 【練気】4 以上で会心したとき (1 ターン 1 回)、 次のターンの手札 +1 枚・会心率 +35%・ブロック +8。</summary>
    public class ClairvoyantCrystal : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.CritClairvoyant;
        public const float Rate = 0.35f;
        public const int Draw = 1, Block = 8, NeedRenki = 4;
        /// <summary>[persistent] 会心を 2 回以上出した直近のターン。</summary>
        private const string Key = "crit.clairvoyant";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Shd.LastTurn(ctx, Key));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnCriticalDamage)
            {
                if ((Crit.ThisTurn(ctx) >= 2 || Crit.HitRenki(ctx) >= NeedRenki) && Rw.OncePerTurn(ctx, Key)) { Rw.DrawNext(Draw); Rw.Log("[千里眼の水晶] 会心 2 回か練気 4 以上の会心 → 次のターン 手札 +1・会心率 +35%・ブロック +8"); }
                return;
            }
            if (Primed(ctx)) { ctx.critRatePctAdd += Rate; ctx.itemBlockBonus += Block; }
        }

        /// <summary>前のターンに条件を満たした (このターンに上乗せが働く)。 BOT の見積もりも読む。</summary>
        public static bool Primed(CombatContext ctx) => ctx != null && Shd.LastTurn(ctx, Key);
    }

    // ===================== 分岐 B〈集中〉9 品 (賭け金は CritCore.FocusIds) =====================

    /// <summary>一の矢【集中】(賭け金 0・2026-11-26 パチンコ強化): 集中会心の撃の会心倍率 +(0.90 ＋ 使った【練気】× 0.25) (5 以上の目を挿していれば さらに +0.50)。</summary>
    public class FirstArrow : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritFirstArrow;
        public const float Mul = 0.90f, MulPerRenki = 0.25f, FaceMul = 0.50f;
        public const int MinFace = 5;
        public static float MulFor(int spent, int face) => Mul + MulPerRenki * spent + (face >= MinFace ? FaceMul : 0f);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Crit.FocusCrit(ctx)) return;
            ctx.criticalMultiplier += MulFor(ctx.hitFocusSpentRenki, ctx.hitFaceMax);   // この後で会心倍率を掛ける
        }
    }

    /// <summary>初太刀の黒鞘【集中】(賭け金 0・2026-11-26 パチンコ化: ブロック撤廃 → 次弾装填): 集中会心のとき、 次のターンの最初の 1 撃の基礎攻撃 +(6 ＋ 使った【練気】× 3)・会心率 +25%。</summary>
    public class FirstStrokePin : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.CritFirstStroke;
        public const int AtkBase = 6, AtkPerRenki = 3;
        public const float NextRate = 0.25f;
        /// <summary>[persistent] 会心率・攻撃が乗るターンの番号と攻撃加算量。</summary>
        public const string Key = "crit.firstStroke.turn", AtkKey = "crit.firstStroke.atk";
        public static bool Primed(CombatContext ctx) => ctx != null && ctx.currentTurn > 0 && Rw.Count(ctx, Key) == ctx.currentTurn;
        public static int AtkNow(CombatContext ctx) => Primed(ctx) ? Rw.Count(ctx, AtkKey) : 0;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Primed(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnCriticalCheck, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnPostRoll) { int a = AtkNow(ctx); if (a > 0) ctx.firstHitAttackBonus += a; return; }
            if (t == PassiveSkillTrigger.OnCriticalCheck) { if (ctx.IsRealHit && ctx.hitIndex == 0 && Primed(ctx)) ctx.critRatePctAdd += NextRate; return; }
            if (!Crit.FocusCrit(ctx)) return;
            ctx.accumulatedValues[Key] = ctx.currentTurn + 1;
            ctx.accumulatedValues[AtkKey] = AtkBase + AtkPerRenki * ctx.hitFocusSpentRenki;
        }
    }

    /// <summary>細目の覗き筒 (仮名・2026-11-23 新)【集中】(賭け金 0): 集中会心の撃は敵の防御を (15 ＋ 使った【練気】× 5) 無視し (ctx.hitArmorPen・防御の計算はこの後)、 会心倍率 +0.35 (answer9: 旧 10 / 3・倍率なし)。</summary>
    public class NarrowSpyglass : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritNarrow;
        public const int Pen = 15, PenPerRenki = 5;
        public const float Mul = 0.35f;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Crit.FocusCrit(ctx)) return;
            ctx.hitArmorPen += Pen + PenPerRenki * ctx.hitFocusSpentRenki;
            ctx.criticalMultiplier += Mul;   // この後で会心倍率を掛ける
        }
    }

    /// <summary>刃こぼれの見立て【集中】(賭け金 0・answer10): 集中会心の撃は敵の防御を 12 無視し、 その撃のダメージの (35 ＋ 使った【練気】× 10)% だけ、 ボスの次の目盛りを上げる (RaiseBossNextNotch)
    /// (answer9: 旧 25 / 8・防御の無視なし)。</summary>
    public class NickAppraisal : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritNick;
        public const int Gauge = 2, Pen = 12;   // 2026-11-26: 目盛りへの削り (35 + 練気 × 10)% → 混乱 +2 (撃ごと)
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnCriticalDamage) { if (Crit.FocusCrit(ctx)) ctx.hitArmorPen += Pen; return; }   // 防御の計算はこの後
            if (!Psn.Hit(ctx) || !Crit.FocusCrit(ctx)) return;
            CombatManager.Instance?.AddBossGauge(Gauge);
        }
    }

    /// <summary>渾身の鉄兜【集中】(賭け金 0・2026-11-26 パチンコ化: ブロック撤廃 → 最終与ダメ全振り): 集中会心の撃の最終与ダメージ +(75 ＋ 使った【練気】× 25)%。</summary>
    public class HeftyPommel : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritPommel;
        public const int FinalPct = 75, FinalPctPerRenki = 25;
        public static int FinalPctFor(int spent) => FinalPct + FinalPctPerRenki * spent;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Crit.FocusCrit(ctx)) return;
            ctx.hitFinalDamagePct += FinalPctFor(ctx.hitFocusSpentRenki);   // 最終与ダメージ倍率はこの後 (ApplyWinDamageModifiers)
        }
    }

    /// <summary>抜き打ちの作法【集中】(賭け金 18%・会心の秘宝 1 つにつき 2% 相殺・2026-11-26 パチンコ強化): 戦闘の始めに【練気】+5。 1 ターン目と敵のダウン中は集中の会心率 +12%。 集中会心の撃の会心倍率 +1.40。</summary>
    public class QuickDrawArt : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.CritQuickDraw;
        public const int StartRenki = 5;
        public const float FocusBonus = 0.12f, Mul = 1.40f;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.currentTurn <= 1 || (CombatManager.Instance?.EnemyDownNow ?? false));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Crit.AddRenki(ctx, StartRenki); return; }   // この品自体が会心の秘宝なので【練気】は溜まる
            if (Crit.FocusCrit(ctx)) ctx.criticalMultiplier += Mul;   // この後で会心倍率を掛ける
        }

        /// <summary>集中の会心率の上乗せ (持っていて、 1 ターン目か敵のダウン中)。 本体の集中の判定と BOT の見積もりが読む。</summary>
        public static float FocusRateBonus(CombatContext ctx, RunState run, bool enemyDown)
            => ctx != null && run != null && run.OwnsPassive(ItemIds.CritQuickDraw) && ctx.currentTurn >= 1 && (ctx.currentTurn == 1 || enemyDown) ? FocusBonus : 0f;
    }

    /// <summary>一閃の極意【集中】(賭け金 18%・会心の秘宝 1 つにつき 2% 相殺・2026-11-26 パチンコ化: HP回復撤廃・上限追加): 集中会心のとき、 使った【練気】1 ごとにこの戦闘中の会心倍率 +0.22 (最大 +1.76・次の撃から)。</summary>
    public class OneFlashMastery : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.CritOneFlash;
        public const float MulPerRenki = 0.22f, MaxMul = 1.76f;
        /// <summary>[persistent] 積もった会心倍率。</summary>
        private const string Key = "crit.oneFlash.mul";
        public static float MulNow(CombatContext ctx) => ctx == null ? 0f : System.Math.Min(MaxMul, ctx.GetAccumulated(Key));
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count((int)System.Math.Round(MulNow(ctx) / MulPerRenki));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnCriticalCheck) { ctx.criticalMultiplier += MulNow(ctx); return; }   // 撃ごとに戻るので撃ごとに足す
            if (!Crit.FocusCrit(ctx)) return;
            ctx.accumulatedValues[Key] = System.Math.Min(MaxMul, MulNow(ctx) + MulPerRenki * ctx.hitFocusSpentRenki);
        }
    }

    /// <summary>白濁の義眼【集中】(賭け金 24%・会心の秘宝 1 つにつき 3% 相殺・answer10): 集中会心の撃は敵の防御を完全に無視し (CombatManager.ApplyWinDamageModifiers)、 撃のあと敵の HP が
    /// 「(会心倍率 × 7 ＋ 使った【練気】× 4)%」(ボスはその半分・上限なし・answer9: 旧 × 5 / × 3) 以下なら倒す (CombatManager の撃の輪・<see cref="KillLine"/>)。
    /// 2026-11-23: 旧 確定会心 ×1.25・会心の撃のあと 会心倍率 × 6% (最大 30%)。</summary>
    public class HeavenEyeGuard : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.CritHeaven;
        public const float PctPerMul = 0.07f, PctPerRenki = 0.04f;
        /// <summary>[persistent] この品を持っている (CombatManager が読む)。</summary>
        public const string HeldKey = "crit.tengan.held";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.accumulatedValues[HeldKey] = 1;

        public static bool Held(CombatContext ctx) => ctx != null && ctx.GetAccumulated(HeldKey) > 0f;

        /// <summary>倒せる HP の線 (これ以下なら倒す・持っていない時は −1)。 <paramref name="spent"/> = その集中会心で使った【練気】。 会心倍率はその撃に掛かった倍率 (<see cref="Kw.LastCritMulKey"/>)。</summary>
        public static int KillLine(CombatContext ctx, int spent)
        {
            if (!Held(ctx) || ctx.enemyMaxHP <= 0) return -1;
            float frac = ctx.GetAccumulated(Kw.LastCritMulKey) * PctPerMul + spent * PctPerRenki;
            if (Rw.IsBoss()) frac /= 2f;
            return frac > 0f ? (int)(ctx.enemyMaxHP * frac) : -1;
        }
    }
}

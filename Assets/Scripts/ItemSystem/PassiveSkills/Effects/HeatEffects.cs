using CombatSystem;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  臨界の軸 26 品 (2026-11-21・full-item-rework-ledger §⑤・docs/GAME.md §7-4)。
    //  使い捨て・連続配線・リスク行動で臨界を溜め、 閾値 (既定 30) で即時の爆発 (基礎 30・敵の防御 50% 無視・ボスの次の目盛りにひび)。
    //  爆発が起きたターンは【過熱】(Kw.Overheated)。 分岐 A〈高速連鎖〉/ 分岐 B〈高圧超臨界〉。
    //  爆発の式と、 爆発の式・配線で決まる臨界に効く 16 品 (過熱石・燻りの革胸当て・火守りの火搔き棒・点火スパナと荒砥の撃ごとの臨界・恒温の竈・
    //  二度詰めの雷管・焚き足しの炭・溢れ取りの鋳型の爆発の分・帳簿外の爆薬・早沸かし釜の閾値・火花読みの眼鏡・八日目の灰燼の ×1.6・燧石の指輪・
    //  暴れ窯の火床・柱折りの発破筒) は CombatSystem.HeatCore の旗 (本体・画面の見込み・BOT が同じ式)。 ここは出来事で働く 15 品。
    // ============================================================

    // ── B: 起点 ── (〈過熱石〉〈燻りの革胸当て〉〈火守りの火搔き棒〉は端子の解決の 5g だけ・HeatCore の旗)

    /// <summary>工廠の材料箱: 爆発するたび、 この戦闘中の自分の防御 +2 (最大 +10)。</summary>
    public class ArsenalCrate : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "工廠の材料箱";
        public const int Per = 2, Max = 10;
        /// <summary>[persistent] この戦闘で得た防御。</summary>
        private const string Key = "heat.arsenal";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Count(ctx, Key));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRinkaiBurst, PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int cur = Rw.Count(ctx, Key);
            if (t == PassiveSkillTrigger.OnTurnStart) { ctx.playerDefense += cur; return; }   // 防御は毎ターン基礎値から組み直されるので積み直す
            int add = System.Math.Min(Per, Max - cur);
            if (add <= 0) return;
            Kw.Add(ctx, Key, add);
            ctx.playerDefense += add;
        }
    }

    /// <summary>乾き火口箱: 戦闘開始時、 臨界 +14。</summary>
    public class DryTinderbox : IPassiveSkillEffect
    {
        public string SkillId => "乾き火口箱";
        public const int Heat = 14;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Kw.AddHeat(ctx, Heat);
    }

    /// <summary>炉番の外套: 敵が攻撃したターンの終わり、 敵の攻撃 (ブロックで受けた分を含む・防御の前) の半分 (最大 +12) の臨界。</summary>
    public class HearthCloak : IPassiveSkillEffect
    {
        public string SkillId => "炉番の外套";
        public const int Max = 12;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int e = Rw.Count(ctx, MutualKeys.EnemyAtkPreDefense);
            if (e > 0) Kw.AddHeat(ctx, System.Math.Min(Max, e / 2));
        }
    }

    /// <summary>継ぎ革の旅靴: マップを 1 歩進むごとに臨界 +2 を蓄積 (最大 20)。 次の戦闘の開始時、 蓄積分の臨界を得て 0 に戻す (歩数は戦闘で 0 に戻る)。</summary>
    public class PatchedBoots : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "継ぎ革の旅靴";
        public const int PerStep = 2, Max = 20;
        public static int Stored => System.Math.Min(Max, Rw.Steps * PerStep);
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Stored);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Kw.AddHeat(ctx, Stored);
    }

    /// <summary>荒砥: 爆発するたび、 この戦闘中の与ダメージ +5% (最大 +30%・次のターンの振り直しの後から)。 撃ごとの臨界 +1 は HeatCore の旗 (5g)。</summary>
    public class RoughWhetstone : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => GameLoop.ItemIds.HeatWhetstone;
        public const int PctPerBurst = 5, MaxPct = 30;
        /// <summary>[persistent] 積もった与ダメージの % 。</summary>
        private const string Key = "heat.whet";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Pct(Rw.Count(ctx, Key) / 100f);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRinkaiBurst, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int cur = Rw.Count(ctx, Key);
            if (t == PassiveSkillTrigger.OnPostRoll) { if (cur > 0) ctx.AddOutgoingPct(cur / 100f); return; }
            if (cur < MaxPct) ctx.accumulatedValues[Key] = System.Math.Min(MaxPct, cur + PctPerBurst);
        }
    }

    // ── S: エンジン・安定 ── (〈恒温の竈〉〈二度詰めの雷管〉〈焚き足しの炭〉は HeatCore の旗)

    /// <summary>点火スパナ: 爆発したターン、 次のターンの手札 +1 枚 (1 ターン 1 回)。 撃ごとの臨界 +2 は HeatCore の旗 (5g)。</summary>
    public class IgnitionSpanner : IPassiveSkillEffect
    {
        public string SkillId => GameLoop.ItemIds.HeatSpanner;
        private const string TurnKey = "heat.spanner";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRinkaiBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.OncePerTurn(ctx, TurnKey)) Rw.DrawNext(1);
        }
    }

    /// <summary>恒熱の炉壁: 爆発するたび、 その爆発のダメージ (敵の防御の後) の 25% (最大 14) をそのターンのブロックとして得る (配線より後の爆発の分は CombatManager.Turn が敵の攻撃の前に足す)。</summary>
    public class HeatWall : IPassiveSkillEffect
    {
        public string SkillId => "恒熱の炉壁";
        public const int Pct = 25, Max = 14;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRinkaiBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.itemBlockBonus += System.Math.Min(Max, Rw.Count(ctx, Kw.BurstDamageNowKey) * Pct / 100);
    }

    /// <summary>二度目の打金: 【過熱】(爆発が起きたターン) と、 その次のターン、 すべての撃の攻撃 +3 (配線で起きた爆発はそのターンの撃に間に合う)。</summary>
    public class SecondStrike : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "二度目の打金";
        public string SkillId => Id;
       public const int Atk = 3;
        public static bool On(CombatContext ctx) { int b = Rw.Count(ctx, Kw.BurstTurnKey); return b > 0 && (b == ctx.currentTurn || b == ctx.currentTurn - 1); }
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(On(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (On(ctx)) ctx.AddPlayerAttackOrDiceBonus(Atk);   // 品の攻撃 +N は撃ごとに乗る
        }
    }

    // ── G: 分岐 A〈高速連鎖〉/ 分岐 B〈高圧超臨界〉 ── (〈帳簿外の爆薬〉〈火花読みの眼鏡〉は HeatCore の旗)

    /// <summary>溢れ取りの鋳型 (分岐 B): 爆発のとき、 閾値を超えて溢れていた臨界 1 につき、 その爆発 +3 (HeatCore の旗)・次の 1 撃の与ダメージ +3%。</summary>
    public class OverflowMold : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => GameLoop.ItemIds.HeatMold;
        public const int PctPerExcess = 3;
        /// <summary>[persistent・次の 1 撃で 0] 溜まっている与ダメージの % 。</summary>
        private const string Key = "heat.mold";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Pct(Rw.Count(ctx, Key) / 100f);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRinkaiBurst, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnRinkaiBurst) { Kw.Add(ctx, Key, PctPerExcess * Rw.Count(ctx, Kw.BurstExcessKey)); return; }
            int p = Rw.Count(ctx, Key);
            if (!ctx.IsRealHit || p <= 0) return;
            ctx.accumulatedValues[Key] = 0;
            ctx.AddOutgoingPct(p / 100f);   // 与ダメの池は撃ごとに戻るので、 この撃だけ
        }
    }

    /// <summary>早沸かし釜 (分岐 A): 敵が攻撃したターンの終わり、 臨界 +2。 閾値 −6 (下限 12) は HeatCore の旗 (2026-11-22 段 3 の較正 2: +3・−8 → +2・−6)。</summary>
    public class QuickKettle : IPassiveSkillEffect
    {
        public string SkillId => GameLoop.ItemIds.HeatKettle;
       public const int Heat = 2;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Psn.EnemyAttacked(ctx)) Kw.AddHeat(ctx, Heat);
        }
    }

    /// <summary>天工開物 (分岐 B): 武器を強化するたび、 このランの爆発の基礎 +12 (累積・この品を持っている間の強化・RunState.heavenlyCraftUpgrades)。
    /// 【過熱】のターン、 攻撃は敵の防御を 12 無視する (配線で起きた爆発はそのターンの撃に間に合う)。</summary>
    public class TenkouKaibutsu : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => GameLoop.ItemIds.HeavenlyCraft;
        public const int PerUpgrade = 12, Pen = 12;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(PerUpgrade * (Rw.Run?.heavenlyCraftUpgrades ?? 0));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.burstFlatBonus += PerUpgrade * (Rw.Run?.heavenlyCraftUpgrades ?? 0); return; }
            if (Kw.Overheated(ctx)) ctx.playerArmorPen += Pen;   // 貫通は毎ターン組み直される
        }
    }

    /// <summary>希望の灯片: 爆発を 2 回以上起こして戦闘に勝ったとき、 最大 HP +3 (永続)。 【過熱】のターンの終わり、 HP +3。</summary>
    public class HopeShard : IPassiveSkillEffect
    {
        public string SkillId => "希望の灯片";
        public const int NeedBursts = 2, MaxHp = 3, Heal = 3;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnTurnEnd) { if (Kw.Overheated(ctx)) Rw.Heal(ctx, Heal); return; }
            var run = Rw.Run;
            if (run == null || !Rw.Won(ctx) || Rw.Count(ctx, Kw.BurstTotalKey) < NeedBursts) return;
            run.playerMaxHP += MaxHp;
            run.playerHP = System.Math.Min(run.playerMaxHP, run.playerHP + MaxHp);
            Rw.Log($"[希望の灯片] 爆発 {Rw.Count(ctx, Kw.BurstTotalKey)} 回で勝利 → 最大HP +{MaxHp} ({run.playerMaxHP})");
        }
    }

    // ── L: 規則破壊 ── (〈燧石の指輪〉〈暴れ窯の火床〉〈柱折りの発破筒〉は HeatCore の旗)

    /// <summary>八日目の灰燼: 致死のダメージを受けたとき臨界が 25 以上あれば HP 1 で踏みとどまり、 臨界を全部使って 使った量 × 3 の防御無視のダメージを敵へ (1 戦闘 1 回)。
    /// 臨界 40 以上で起きた爆発 ×1.6 は HeatCore の旗。</summary>
    public class UnyieldingHeat : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => GameLoop.ItemIds.HeatAshen;
        public const int Need = 25, Mul = 3;
        /// <summary>[persistent] この戦闘で使った (1/0)。</summary>
        private const string UsedKey = "heat.ashen.used";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Once(Rw.Count(ctx, UsedKey) > 0, ctx.rinkaiMeter >= Need);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0 || ctx.finalDamage < ctx.playerCurrentHP || ctx.rinkaiMeter < Need || Rw.Count(ctx, UsedKey) > 0) return;
            ctx.accumulatedValues[UsedKey] = 1;
            int lethal = ctx.finalDamage, used = ctx.rinkaiMeter;
            ctx.finalDamage = System.Math.Max(0, ctx.playerCurrentHP - 1);
            ctx.rinkaiMeter = 0;
            CombatManager.Instance?.LandBurst(ctx, used * Mul);
            Rw.Log($"[八日目の灰燼] 致命ダメ {lethal} → HP 1 で踏みとどまる・臨界 {used} を使って {used * Mul} の防御無視");
        }
    }

    /// <summary>烈刃【規則】: 【過熱】の間、 すべての撃の最終与ダメージ +「この戦闘の爆発の回数 × 15%」(最大 +150%)。</summary>
    public class FierceBlade : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "烈刃";
        public string SkillId => Id;
        public const int PctPerBurst = 15, MaxPct = 150;
        public static int PctNow(CombatContext ctx) => System.Math.Min(MaxPct, PctPerBurst * Rw.Count(ctx, Kw.BurstTotalKey));
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Kw.Overheated(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.IsRealHit && Kw.Overheated(ctx)) ctx.hitFinalDamagePct += PctNow(ctx);   // この撃だけ (撃ごとに 0 へ戻る)
        }
    }

    /// <summary>無害灯【規則】: 臨界 15 以上のとき、 敵の直接攻撃で HP を失う代わりに臨界 15 を使って無効にする (1 戦闘 2 回・1 ターン 1 回・2026-11-22 Phase 4 較正: 旧 1 ターン 1 回だけ)。
    /// 無効にしたとき、 臨界を使わずに爆発の 30% のダメージ (基礎 30 + 上乗せの 30%・敵の防御 50% 無視) を敵へ返す。 掛ける所は CombatManager の敵の攻撃 (<see cref="Apply"/>)。</summary>
    public class HarmlessLamp : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "無害灯";
        public string SkillId => Id;
        public const int MinHeat = 15, Pay = 15, BackPct = 30, MaxPerBattle = 2;
        /// <summary>[persistent] この品を持っている / [1 ターン 1 回] 使ったターンの番号 / [persistent] この戦闘で使った回数。</summary>
        private const string HeldKey = "heat.lamp.held", TurnKey = "heat.lamp.turn", UsesKey = "heat.lamp.uses";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Ready(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.accumulatedValues[HeldKey] = 1;

        /// <summary>このターン、 次の敵の直接攻撃を無効にできる (BOT の見積もりも読む)。</summary>
        public static bool Ready(CombatContext ctx)
            => ctx != null && ctx.GetAccumulated(HeldKey) > 0f && ctx.rinkaiMeter >= MinHeat && Rw.Count(ctx, TurnKey) != ctx.currentTurn && Rw.Count(ctx, UsesKey) < MaxPerBattle;

        /// <summary>敵の直接攻撃で失う HP <paramref name="taken"/> に掛ける。 戻り値 = 実際に失う HP。</summary>
        public static int Apply(CombatContext ctx, int taken)
        {
            if (taken <= 0 || !Ready(ctx)) return taken;
           ctx.accumulatedValues[TurnKey] = ctx.currentTurn; ctx.accumulatedValues[UsesKey] = Rw.Count(ctx, UsesKey) + 1;
            ctx.rinkaiMeter -= Pay;
            var s = Kw.HeatState(ctx);
            int back = HeatCore.VsDefense((HeatCore.BurstBase + s.stack + s.flat) * BackPct / 100, s.enemyDefense, s.items);
            CombatManager.Instance?.LandBurst(ctx, back);
            Rw.Log($"[無害灯] 臨界 {Pay} を使って被ダメージ {taken} を無効 → 敵へ {back} (臨界 残 {ctx.rinkaiMeter})");
            return 0;
        }
    }
}

using UnityEngine;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  アイテム再編 (2026-10-31)。 設計は docs/GAME.md §8-6、 計算の位置は DAMAGE_CALC_REFERENCE.md §3-H。
    //
    //  品どうしが 1+1>2 になるよう、 どの品も「何かを増やす」か「何かを条件にする」形にした。
    //  増やすもの・条件にするものは 臨界・充電・シールド・毒・出血・会心・金 の 7 つに寄せてある。
    //  キーワードの出来事 (爆発・充電を得る/使う・過充電・シールドの吸収/半減/割れ・毒の増加/ダメージ/上限・
    //  出血の減衰/消費/0) は Keywords.Kw が配る。 1 つの連鎖の中で同じ品は 1 度しか働かない。
    //
    //  効果を据え置いた品 (八日目の灰燼・蛇血瓶・鋸刃の山刀・先血の腕輪・
    //  医家の反り刃・逆綴じの止血帯・血桜・医書裏の開き針・溶金の領主剣・黄金色の封蝋) は元のクラスのまま。
    // ============================================================

    /// <summary>この品群で使う小さな道具。</summary>
    internal static class Rw
    {
        public static GameLoop.RunState Run => GameLoop.GameManager.Instance?.Run;
        public static int Steps => Run?.stepsSinceBattle ?? 0;
        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);

        /// <summary>刻みを「与ダメージ +10%」に換える品の共通部 (2026-11-06・2026-11-10 に出目 +10% から変更)。 OnPostRoll で刻みの分の率を積み、
        /// OnWired で攻撃に 1 本でも挿していたら刻みを 0 に戻す (使い切る)。 攻撃しないターンは持ち越す。
        /// この 2 つのトリガーを処理したら true (呼び手は刻みを数える処理へ進まない)。</summary>
        public const float DiceStackPct = 0.1f;
        public static bool SpendDiceStacks(PassiveSkillTrigger t, CombatContext ctx, string key)
        {
            if (t == PassiveSkillTrigger.OnPostRoll)
            {
                int n = Count(ctx, key);
                if (n > 0) ctx.AddOutgoingPct(n * DiceStackPct);
                return true;
            }
            if (t == PassiveSkillTrigger.OnWired)
            {
                if (Count(ctx, MutualKeys.AttackDiceCount) > 0) ctx.accumulatedValues[key] = 0f;
                return true;
            }
            return false;
        }
        public static bool Won(CombatContext ctx) => ctx.enemyCurrentHP <= 0;

        /// <summary>プレイヤーの攻撃が通った (ダメージ後トリガーの中で使う)。</summary>
        public static bool PlayerHit(CombatContext ctx) => ctx.playerWonRoll && ctx.finalDamage > 0;

        public static void Gold(int n, string label)
        {
            var run = Run;
            if (run != null && n > 0) GameLoop.GoldIncome.Gain(run, n, label, GameLoop.GoldOrigin.Item);
        }

        /// <summary>このターン 1 回だけ (key に今のターン番号を書く)。</summary>
        public static bool OncePerTurn(CombatContext ctx, string key)
        {
            if (Count(ctx, key) == ctx.currentTurn) return false;
            ctx.accumulatedValues[key] = ctx.currentTurn;
            return true;
        }

        /// <summary>この戦闘で 1 回だけ。</summary>
        public static bool OncePerBattle(CombatContext ctx, string key)
        {
            if (Count(ctx, key) > 0) return false;
            ctx.accumulatedValues[key] = 1;
            return true;
        }

        public static void Shield(CombatContext ctx, int n, string label)
        {
            if (n <= 0) return;
            ctx.consShield += n;
            CombatSystem.ShieldDiag.Note(label, n);
            ctx.shieldGainedTotal += n;
        }

        public static void Poison(CombatContext ctx, int n)
        {
            if (n > 0) ctx.AddStatus(StatusTarget.Enemy, "poison", n);
        }

        public static void Log(string s) => Debug.Log(s);

        /// <summary>プレイヤーの HP を n 回復する (戦闘中は CombatManager.HealPlayer ── 手元の HP と ctx の両方・回復を削るデバフも通す)。</summary>
        public static void Heal(CombatContext ctx, int n)
        {
            if (n <= 0) return;
            var cm = CombatSystem.CombatManager.Instance;
            if (cm != null && cm.IsCombatActive) cm.HealPlayer(n);
            else if (ctx != null) ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + n);
        }

        /// <summary>次のターンに端子を n 枚多く引く (端子〈先読み〉と同じ口・TerminalBattleState.extraDrawNext)。</summary>
        public static void DrawNext(int n)
        {
            var deck = CombatSystem.CombatManager.Instance?.TerminalDeck;
            if (deck != null && n > 0) deck.state.extraDrawNext += n;
        }

        /// <summary>いまの戦闘がエリート戦かボス戦か (StatModifierEffect の strongFoe と同じ判定)。</summary>
        public static bool StrongFoe()
        {
            var type = MapSystem.MapManager.Instance?.CurrentNode?.type;
            return type == MapSystem.TileType.EliteBattle || type == MapSystem.TileType.Boss;
        }
    }

    // ============================================================
    //  臨界
    // ============================================================

    /// <summary>過熱石: 出目 3 個とも攻撃の端子を挿したターン、 臨界 +15。</summary>
    public class OverheatStone : IPassiveSkillEffect
    {
        public string SkillId => "過熱石";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            if (!Fam.AllAttack(ctx)) return;
            Kw.AddHeat(ctx, 15);
            Rw.Log($"[過熱石] 全部攻撃 → 臨界 +15 (現 {ctx.rinkaiMeter})");
        }
    }

    /// <summary>炉番の外套: 受けた被ダメージの半分を臨界にする。</summary>
    public class HearthCloak : IPassiveSkillEffect
    {
        public string SkillId => "炉番の外套";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            int taken = Rw.Count(ctx, MutualKeys.HpLossFromEnemy);
            if (taken / 2 > 0) Kw.AddHeat(ctx, taken / 2);
        }
    }

    /// <summary>乾き火口箱: 戦闘開始時 臨界 +12。</summary>
    public class DryTinderbox : IPassiveSkillEffect
    {
        public string SkillId => "乾き火口箱";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            Kw.AddHeat(ctx, 12);
        }
    }

    /// <summary>恒温の竈 (2026-11-20 作り直し): 戦闘開始時、 臨界が閾値の半分 (切り捨て) から始まる (それより多く溜まっていればそのまま・出来事は配らない)。
    /// 炉温 8 以上ならターン開始時 臨界 +3 (入り口・EntryEffect.Table)。 旧: 前の戦闘の終わりに残っていた臨界の半分から始まる (RunState.pendingRinkaiCarryover)。</summary>
    public class MorningOven : IPassiveSkillEffect
    {
        public string SkillId => "恒温の竈";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            ctx.rinkaiMeter = System.Math.Max(ctx.rinkaiMeter, ctx.rinkaiThreshold / 2);
        }
    }

    /// <summary>溢れ取りの鋳型: 爆発のとき、 閾値を超えて溜まっていた臨界 1 につき、 次の攻撃の与ダメージ +2% (攻撃したら使い切る)。
    /// 2026-11-10: 旧「超えた臨界 1 につき爆発の追加ダメージ +2」から主攻撃へ返す形に変えた (5 層ボス戦で爆発が主攻撃を押しのけていた)。</summary>
    public class OverflowMold : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "溢れ取りの鋳型";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Pct(ctx.GetAccumulated(PctKey));
        public const float PctPerExcess = 0.02f;
        /// <summary>[persistent・戦闘開始で 0・攻撃したら 0] 溜まっている与ダメージの率。</summary>
        public const string PctKey = "rw.moldPct";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst, PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); ctx.accumulatedValues[PctKey] = 0f; return; }
            if (t == PassiveSkillTrigger.OnRinkaiBurst)
            {
                int excess = Rw.Count(ctx, Kw.BurstExcessKey);
                if (excess > 0) ctx.accumulatedValues[PctKey] = ctx.GetAccumulated(PctKey) + excess * PctPerExcess;
                return;
            }
            if (t == PassiveSkillTrigger.OnPostRoll) { ctx.AddOutgoingPct(ctx.GetAccumulated(PctKey)); return; }
            if (Rw.Count(ctx, MutualKeys.AttackDiceCount) > 0) ctx.accumulatedValues[PctKey] = 0f;   // OnWired: 攻撃したら使い切る
        }
    }

    /// <summary>二度目の打金: 爆発した次のターン、 与ダメージ +30%。
    /// 2026-11-10: 旧「爆発した次のターンも、 追加ダメージの半分が乗る」(爆発の残響 = 固定ダメ枠) から主攻撃へ返す形に変えた。
    /// 2026-11-20 (ADR-0016): 爆発がその場でダメージを出すようになったので、 「爆発を攻撃で使ったターン」ではなく「爆発したターン」を読む。</summary>
    public class SecondStrike : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "二度目の打金";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Rw.Count(ctx, Kw.BurstTurnKey) > 0 && (Rw.Count(ctx, Kw.BurstTurnKey) == ctx.currentTurn || Rw.Count(ctx, Kw.BurstTurnKey) == ctx.currentTurn - 1));
        public const float Pct = 0.30f;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            int turn = Rw.Count(ctx, Kw.BurstTurnKey);
            if (turn <= 0 || turn != ctx.currentTurn - 1) return;
            ctx.AddOutgoingPct(Pct);
        }
    }

    /// <summary>帳簿外の爆薬【規則】: 爆発で臨界を 0 にせず閾値ぶんだけ減らす。 溜まっていれば同じターンに何度でも爆発する。</summary>
    public class OffBookChain : IPassiveSkillEffect
    {
        public string SkillId => "帳簿外の爆薬";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            ctx.rinkaiMultiBurst = true;
        }
    }

    /// <summary>恒熱の炉壁: 爆発のたび、 消えた臨界の 10% をシールドにする (2026-11-20: 旧 20%)。</summary>
    public class HeatWall : IPassiveSkillEffect
    {
        public string SkillId => "恒熱の炉壁";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            Rw.Shield(ctx, Kw.EventAmount(ctx) * 10 / 100, SkillId);
        }
    }

    /// <summary>早沸かし釜【規則】: 爆発の閾値が 15 下がる。 2026-11-06: 旧「50 から 35 に」は閾値が臨界の軸の深さで
    /// 決まるようになった (AxisRules) ので差で下げる ── 「35 に」のままだと、 この 1 品で深さの罰を丸ごと消せてしまう。</summary>
    public class QuickKettle : IPassiveSkillEffect
    {
        public string SkillId => "早沸かし釜";
        public const int Cut = 15;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            ctx.rinkaiThreshold -= Cut;
        }
    }

    /// <summary>点火スパナ: 爆発のたび充電 +3 (2026-11-20: 旧 +5)。 「攻撃が通るたび臨界 +2」は入り口 (EntryEffect.Table)。</summary>
    public class IgnitionSpanner : IPassiveSkillEffect
    {
        public string SkillId => "点火スパナ";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            ctx.AddCharge(3);
        }
    }

    /// <summary>荒砥: 爆発のたび、 敵に出血 +2。 爆発 1 回につき、 この戦闘中の与ダメージ +5% (積もる・上限なし)。
    /// 2026-11-10: 「攻撃が通るたび臨界 +3」を与ダメージへ差し替えた (爆発が主攻撃へ返る口)。
    /// 2026-11-20 (ADR-0016): 旧「爆発した攻撃が通ると出血 +4」→ 爆発がその場でダメージを出すようになったので「爆発のたび出血 +2」。</summary>
    public class RoughWhetstone : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "荒砥";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Pct(ctx.GetAccumulated(PctKey));
        public const float PctPerBurst = 0.05f;
        /// <summary>[persistent・戦闘開始で 0] 積もった与ダメージの率。</summary>
        public const string PctKey = "rw.aratoPct";
        public const int BleedPerBurst = 2;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); ctx.accumulatedValues[PctKey] = 0f; return; }
            if (t == PassiveSkillTrigger.OnRinkaiBurst)
            {
                ctx.accumulatedValues[PctKey] = ctx.GetAccumulated(PctKey) + PctPerBurst;
                ctx.AddEnemyBleed(BleedPerBurst);
                return;
            }
            ctx.AddOutgoingPct(ctx.GetAccumulated(PctKey));   // OnPostRoll
        }
    }

    /// <summary>無害灯【規則】(2026-11-20 作り直し): 臨界が 15 以上あるとき、 敵の直接攻撃で HP を失う代わりに、 その半分の臨界を使って被ダメージを半減する (1 ターン 1 回)。
    /// 減らす量 = 被ダメージの半分 (切り捨て)・使う臨界 = 同じ量 (足りなければあるだけ)。 掛ける所は CombatManager の敵の攻撃 (一撃の上限の後・HP を減らす直前・<see cref="Apply"/>)。
    /// 炉温 6 以上ならターン開始時 臨界 +3 (入り口・EntryEffect.Table)。 旧: 臨界が 10 以上の間、 受けるダメージ −2。</summary>
    public class HarmlessLamp : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "無害灯";
        public string SkillId => Id;
        public const int MinHeat = 15;
        /// <summary>[persistent] この品を持っている / [1 ターン 1 回] 使ったターンの番号。</summary>
        private const string HeldKey = "rw.lamp.held", TurnKey = "rw.lamp.turn";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.rinkaiMeter >= MinHeat && Rw.Count(ctx, TurnKey) != ctx.currentTurn);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) { Kw.EnableHeat(ctx); ctx.accumulatedValues[HeldKey] = 1; }

        /// <summary>敵の直接攻撃で失う HP <paramref name="taken"/> に掛ける。 戻り値 = 実際に失う HP。</summary>
        public static int Apply(CombatContext ctx, int taken)
        {
            if (ctx == null || taken <= 1 || ctx.GetAccumulated(HeldKey) <= 0f || ctx.rinkaiMeter < MinHeat) return taken;
            if (!Rw.OncePerTurn(ctx, TurnKey)) return taken;
            int cut = taken / 2;
            int pay = System.Math.Min(cut, ctx.rinkaiMeter);
            ctx.rinkaiMeter -= pay;
            Rw.Log($"[無害灯] 臨界 {pay} を使って被ダメージを半減: {taken} → {taken - cut} (臨界 残 {ctx.rinkaiMeter})");
            return taken - cut;
        }
    }

    /// <summary>工廠の材料箱: 爆発のたび、 この戦闘中の自分の防御 +1 (2026-11-20: 旧 +2)。</summary>
    public class ArsenalCrate : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "工廠の材料箱";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Count(ctx, Key));
        private const string Key = "rw.arsenal";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst, PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            if (t == PassiveSkillTrigger.OnRinkaiBurst) { Kw.Add(ctx, Key, 1); ctx.playerDefense += 1; return; }
            ctx.playerDefense += Rw.Count(ctx, Key);   // 防御は毎ターン基礎値から組み直されるので積み直す
        }
    }

    /// <summary>継ぎ革の旅靴: マップを 1 歩進むごとに、 次の戦闘の開始時の臨界 +2。
    /// 2026-11-06: 旧 +3 は配線を払わない燃料で、 クリアしたランの 94% が持っていた。</summary>
    public class PatchedBoots : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "継ぎ革の旅靴";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Steps);
        public const int PerStep = 2;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            if (Rw.Steps > 0) Kw.AddHeat(ctx, Rw.Steps * PerStep);
        }
    }

    /// <summary>希望の灯片: 戦闘終了時に臨界が 25 以上残っていれば、 最大 HP +2 (永続)。</summary>
    public class HopeShard : IPassiveSkillEffect
    {
        public string SkillId => "希望の灯片";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            var run = Rw.Run;
            if (run == null || !Rw.Won(ctx) || ctx.rinkaiMeter < 25) return;
            run.playerMaxHP += 2;
            run.playerHP = Mathf.Min(run.playerMaxHP, run.playerHP + 2);
            Rw.Log($"[希望の灯片] 臨界 {ctx.rinkaiMeter} を残して勝利 → 最大HP +2 ({run.playerMaxHP})");
        }
    }

    // ============================================================
    //  シールド
    // ============================================================

    /// <summary>擦り減った小盾: 防御の端子を挿したダイス 1 個につきシールド +1 (2026-11-06・+2 から。 シールド全体の 30% を占めていた)。</summary>
    public class WornBuckler : IPassiveSkillEffect
    {
        public string SkillId => "擦り減った小盾";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => Rw.Shield(ctx, Rw.Count(ctx, MutualKeys.BlockDiceCount), SkillId);
    }

    /// <summary>肩当て: 出目 3 個とも防御の端子を挿したターン、 シールド +15 (2026-11-07: 旧 +8)。</summary>
    public class Pauldron : IPassiveSkillEffect
    {
        public string SkillId => "肩当て";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Fam.AllBlock(ctx)) Rw.Shield(ctx, 15, SkillId);
        }
    }

    /// <summary>鏡返しの小盾: シールドが攻撃を吸収するたび、 吸収した量の 50% を敵に返す (2026-11-07: 旧 30%)。</summary>
    public class MirrorBuckler : IPassiveSkillEffect
    {
        public string SkillId => "鏡返しの小盾";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldAbsorb };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, Kw.AbsorbIsAttackKey) <= 0) return;   // 敵の直接攻撃だけ返す
            int r = Kw.EventAmount(ctx) * 50 / 100;
            if (r > 0) Kw.Add(ctx, Kw.ReflectKey, r);
        }
    }

    /// <summary>聖路の白靴: マップを 1 歩進むごとに、 次の戦闘の開始時のシールド +2。</summary>
    public class HolyRoadShoes : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "聖路の白靴";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Steps);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Shield(ctx, Rw.Steps * 2, SkillId);
    }

    /// <summary>相棒の魂: シールドが割られて 0 になったとき、 割られる前の量の半分で張り直す (1 戦闘 1 回)。</summary>
    public class CompanionSoul : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "相棒の魂";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Once(Rw.Count(ctx, "rw.companion") > 0, true);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldBreak };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int half = Kw.EventAmount(ctx) / 2;
            if (half <= 0 || !Rw.OncePerBattle(ctx, "rw.companion")) return;
            Rw.Shield(ctx, half, SkillId);
            Rw.Log($"[相棒の魂] 割られたシールドを {half} で張り直す");
        }
    }

    /// <summary>二拍目の心臓: HP が 50% を下回った瞬間にシールド (最大 HP の 50%) (1 戦闘 1 回)。
    /// 旧〈命脈〉から戦闘開始時の回復を外した。</summary>
    public class SecondBeatHeart : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "二拍目の心臓";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Once(Rw.Count(ctx, "rw.secondBeat") > 0, true);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0 || ctx.playerCurrentHP <= 0) return;
            if (ctx.playerCurrentHP * 100 > ctx.playerMaxHP * 50) return;
            if (!Rw.OncePerBattle(ctx, "rw.secondBeat")) return;
            int shield = Mathf.CeilToInt(ctx.playerMaxHP * 0.50f);
            Rw.Shield(ctx, shield, SkillId);
            Rw.Log($"[二拍目の心臓] HP50% を下回った → シールド +{shield}");
        }
    }

    /// <summary>重装兵の隠し刃 (2026-11-20 作り直し): ボスのダウン中 (そのターンがダウン)、 シールドがターン開始時に半減しない (CombatContext.BeginNewTurn・ctx.shieldKeepOnBossDown)。
    /// 旧: 敵の HP が 30% 以下になったターン、 シールドを最大 20 使い、 使った量 × 2 を攻撃に加える (1 戦闘 1 回)。</summary>
    public class KnightsTrump : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "重装兵の隠し刃";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(CombatSystem.BossDown.ActiveNow);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.shieldKeepOnBossDown = true;
    }

    /// <summary>敗残兵の部隊章: シールドを 5 失うたび (吸収・半減とも) 刻み +1。 攻撃の端子を挿したターン、
    /// 刻み 1 つにつき攻撃の端子を挿した出目 +10% (使い切る)。
    /// 2026-11-06: 旧「この戦闘中の攻撃 +1」は戦闘が長いほど積み上がり、 攻撃+N の 16% を占めた。</summary>
    public class VeteranPatch : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "敗残兵の部隊章";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Count(ctx, Stk));
        private const string Stk = "rw.veteranAtk";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldAbsorb, PassiveSkillTrigger.OnShieldDecay, PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.SpendDiceStacks(t, ctx, Stk)) return;
            int n = Kw.Tally(ctx, "rw.veteran", Kw.EventAmount(ctx), 5);
            if (n > 0) Kw.Add(ctx, Stk, n);
        }
    }

    /// <summary>脈なしの鋼心臓: 戦闘開始時、 シールド +X。 戦闘に勝利するたび X +1 (このランの間)。 X の最初の値は 10。
    /// X は <see cref="GameLoop.RunState.pulselessHeartShield"/> (ランの始めに 10)。 勝ちを数えるのは CombatManager.FinishCombat (<see cref="NoteWin"/>)。
    /// 2026-11-20 作り直し: 旧【規則】「シールドがターン開始時に半減しなくなる」。</summary>
    public class PulselessHeart : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "脈なしの鋼心臓";
        public const int StartShield = 10;
        public string SkillId => Id;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Run?.pulselessHeartShield ?? StartShield);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Shield(ctx, Rw.Run?.pulselessHeartShield ?? StartShield, SkillId);
        /// <summary>戦闘に勝った時に呼ぶ。 持っている間の勝利だけ X +1。</summary>
        public static void NoteWin(GameLoop.RunState run)
        {
            if (AxisRules.Owns(run, Id)) run.pulselessHeartShield++;
        }
    }

    /// <summary>燻りの革胸当て: ターン開始の減衰で失ったシールドの半分を臨界にする。
    /// 2026-11-06: 旧「失ったシールドを臨界に」は放っておけば毎ターン入る燃料で、 クリアしたランの 95% が持っていた。</summary>
    public class SmolderingBreastplate : IPassiveSkillEffect
    {
        public string SkillId => "燻りの革胸当て";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnShieldDecay };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            Kw.AddHeat(ctx, Kw.EventAmount(ctx) / 2);
        }
    }

    // 〈鎧縫いの針〉(敵の出血 1 につき貫通 +1・OnWired) は 2026-11-11 に items.json の stats へ移した (段 3・per=enemyBleed)。

    /// <summary>力みの小手: シールドを合計 10 得るたび 刻み +1。 攻撃の端子を挿したターン、
    /// 刻み 1 つにつき攻撃の端子を挿した出目 +10% (使い切る)。 2026-11-06: 旧「この戦闘中の攻撃 +1」。</summary>
    public class StrainGauntlet : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "力みの小手";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Count(ctx, Stk));
        private const string Stk = "rw.gauntletAtk";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldGain, PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.SpendDiceStacks(t, ctx, Stk)) return;
            int n = Kw.Tally(ctx, "rw.gauntlet", Kw.EventAmount(ctx), 10);
            if (n > 0) Kw.Add(ctx, Stk, n);
        }
    }

    // ============================================================
    //  出血へ移った品 (効果は Phase 3 で BleedEffects.cs へ)
    // ============================================================

    /// <summary>百人切りの鉈: ターン終了時、 敵 HP が「毒 5 につき 1%」以下なら倒す (ボスはその半分)。</summary>
    public class HundredCleaver : IPassiveSkillEffect
    {
        public string SkillId => "百人切りの鉈";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.enemyCurrentHP <= 0 || ctx.enemyMaxHP <= 0) return;
            int pct = ctx.EnemyPoison / 5;   // 2026-11-07: 毒 5 につき 1% (旧 毒 × 2%・毒 1 スタックの重みが 1/5 になったため ×1/10 で天井を据え置き)
            if (BountyHunterHelper.IsBoss(ctx)) pct /= 2;
            if (pct <= 0 || ctx.enemyCurrentHP * 100 > ctx.enemyMaxHP * pct) return;
            if (!ctx.CanExecuteEnemy()) return;
            ctx.enemyCurrentHP = 0;
            Rw.Log($"[百人切りの鉈] 敵HP {pct}% 以下 → 処刑");
        }
    }

    /// <summary>加重の拳套 (2026-11-20 作り直し): 同じターンに 2 撃目以降の攻撃が通るたび、 その撃の最終与ダメージ +15% (2 撃目 +15%・3 撃目 +30%・撃の番号 − 1 につき +15%)。
    /// この撃だけの最終与ダメージ倍率 (ctx.hitFinalDamagePct) を OnPreDealDamage で書く (CombatManager が撃ごとに 0 に戻す)。 軸なし。
    /// 旧: 攻撃が通るたび (1 ターンに 1 回) 刻み +1・刻み 1 つにつき与ダメージ +2% (戦闘の間)。
    /// 2026-11-11 (#100): 毒の品から作り直した (名前・フレーバー「留め紐は一撃ごとに長くなる。 外すと元へ戻る」と効果を合わせた)。</summary>
    public class WeightingGauntlet : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "加重の拳套";
        public string SkillId => Id;
        public const int PctPerHit = 15;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.hitCount >= 2);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.IsRealHit && ctx.hitIndex >= 1) ctx.hitFinalDamagePct += PctPerHit * ctx.hitIndex;
        }
    }

    // ============================================================
    //  出血
    // ============================================================

    // 〈血日に澄む佩玉〉(敵の出血 1 につき会心率 +2%・OnWired) は 2026-11-11 に items.json の stats へ移した (段 3・per=enemyBleed)。

    /// <summary>魂喰らいの血石: 出血を消費・減衰で失うたび、 失った分だけシールド。</summary>
    public class SoulBloodstone : IPassiveSkillEffect
    {
        public string SkillId => "魂喰らいの血石";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBleedDecay, PassiveSkillTrigger.OnBleedConsume };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Shield(ctx, Kw.EventAmount(ctx), SkillId);
    }

    /// <summary>血脈の災環 (2026-11-20): 出血が自然に減るたび、 減った量 × 血晶 の軽減不可ダメージ。 旧: 出血が自然に 1 減るたび、 軽減不可ダメージ 3。</summary>
    public class BloodlineRing : IPassiveSkillEffect
    {
        public string SkillId => "血脈の災環";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBleedDecay };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Kw.DamageEnemy(ctx, Kw.EventAmount(ctx) * ctx.AxisDepth(AxisRules.Bleed));
    }

    /// <summary>破城斧 (2026-11-20 作り直し): シールドが 10 以上のターン、 攻撃の端子 1 枚につき (= 撃ごと) シールドを 5 使い、 その撃の攻撃 +10。
    /// 「10 以上のターン」は最初の 1 撃の時のシールドで決める。 撃の途中でシールドが 5 を切ったら、 その後の撃には乗らない (払えないので)。
    /// 攻撃 +10 はこの撃の倍率の前の量 (ProcessDamage の OnPreDealDamage で finalDamage に足す)。 旧: シールドを 10 使って、 その攻撃は敵の防御を無視する。</summary>
    public class SiegeAxe : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "破城斧";
        public string SkillId => Id;
        public const int MinShield = 10, PerHitShield = 5, PerHitAtk = 10;
        /// <summary>シールドが 10 以上だったターンの番号 (最初の 1 撃で決める)。</summary>
        private const string TurnKey = "rw.siegeAxe.turn";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.consShield >= MinShield);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            if (ctx.hitIndex == 0 && ctx.consShield >= MinShield) ctx.accumulatedValues[TurnKey] = ctx.currentTurn;
            if (Rw.Count(ctx, TurnKey) != ctx.currentTurn || ctx.consShield < PerHitShield) return;
            ctx.consShield -= PerHitShield;
            ctx.finalDamage += PerHitAtk;
            Rw.Log($"[破城斧] {ctx.hitIndex + 1} 撃目: シールド {PerHitShield} を使い 攻撃 +{PerHitAtk} (シールド 残 {ctx.consShield})");
        }
    }

    /// <summary>血車 (2026-11-20 作り直し): 血晶 5 以上の時、 敵の出血が 10 減るたび (品が使う・自然に減る、 とも・端数は持ち越す)、 充電 +5。
    /// 旧: 敵の出血が 0 になったとき、 その戦闘の出血の最大値だけ充電。</summary>
    public class BloodWheel : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "血車";
        public const int MinDepth = 5, Every = 10, Charge = 5;
        /// <summary>[persistent] この戦闘で減った出血の累計 (Kw.Tally の端数)。</summary>
        private const string Key = "rw.bloodWheel";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.AxisDepth(AxisRules.Bleed) >= MinDepth);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBleedDecay, PassiveSkillTrigger.OnBleedConsume };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.AxisDepth(AxisRules.Bleed) < MinDepth) return;
            int n = Kw.Tally(ctx, Key, Kw.EventAmount(ctx), Every);
            if (n > 0) ctx.AddCharge(Charge * n);
        }
    }

    /// <summary>鉛入りの握斧: 非会心の攻撃が通るたび敵に出血 +2。</summary>
    public class LeadHandAxe : IPassiveSkillEffect
    {
        public string SkillId => "鉛入りの握斧";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.PlayerHit(ctx) && !ctx.isCritical) ctx.AddEnemyBleed(2);
        }
    }

    /// <summary>激情の刃: HP が 50% 以下の間、 攻撃が通るたび敵に出血 +2。</summary>
    public class FuryBlade : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "激情の刃";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.playerMaxHP > 0 && ctx.playerCurrentHP * 2 <= ctx.playerMaxHP);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.PlayerHit(ctx) && ctx.playerCurrentHP * 2 <= ctx.playerMaxHP) ctx.AddEnemyBleed(2);
        }
    }

    // ============================================================
    //  金
    // ============================================================

    /// <summary>銭皿: シールドが残った状態で勝った戦闘ごとに 2G。
    /// 2026-11-06: 旧「残りシールド 3 につき 1G」は、 護符をとどめのターンに使うだけで買値より多く戻り (〈惜別の護符〉+13G)、
    /// 金を増やし続けられた。 通常のプレイでも収入の 23% を占めた。 額を残量に比例させない ── 最安の護符 (4G) でも元が取れない。</summary>
    public class CoinDish : IPassiveSkillEffect
    {
        public string SkillId => "銭皿";
        public const int Gold = 2;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Won(ctx) && ctx.consShield > 0) Rw.Gold(Gold, SkillId);
        }
    }

    /// <summary>天工開物 (2026-11-20 作り直し): 武器を強化するたび、 以後の爆発のダメージ +5 (累積・このランの間・ctx.burstFlatBonus → Kw.BurstBase)。
    /// 強化の回数は RunState.heavenlyCraftUpgrades (この品を持っている間の強化だけ数える・GameManager.Rest)。 旧: 以後の戦闘の開始時の臨界 +5 (累積)。</summary>
    public class TenkouKaibutsu : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "天工開物";
        public const int PerUpgrade = 5;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(PerUpgrade * (Rw.Run?.heavenlyCraftUpgrades ?? 0));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            ctx.burstFlatBonus += PerUpgrade * (Rw.Run?.heavenlyCraftUpgrades ?? 0);
        }
    }

    /// <summary>幸運の硬貨: 同じ目が 2 個以上出たターン、 2G。</summary>
    public class LuckyCoin : IPassiveSkillEffect
    {
        public string SkillId => "幸運の硬貨";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var d = ctx.playerDice;
            if (d == null) return;
            for (int i = 0; i < d.Length; i++)
                for (int j = i + 1; j < d.Length; j++)
                    if (d[i] == d[j]) { Rw.Gold(2, SkillId); return; }
        }
    }

    /// <summary>戦果の天秤: 戦闘に勝ったとき、 その戦闘で出した会心の回数だけゴールド。</summary>
    public class SpoilsScale : IPassiveSkillEffect
    {
        public string SkillId => "戦果の天秤";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Won(ctx)) Rw.Gold(Rw.Count(ctx, Kw.CritCountKey), SkillId);
        }
    }

    /// <summary>爆破祭の小切手: 臨界の爆発でとどめを刺すたび +4G。 2026-11-20 (ADR-0016): 爆発のダメージそのもので敵が倒れた戦闘 (Kw.BurstKilledKey)。</summary>
    public class BlastPrize : IPassiveSkillEffect
    {
        public string SkillId => "爆破祭の小切手";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            if (Rw.Won(ctx) && Rw.Count(ctx, Kw.BurstKilledKey) > 0) Rw.Gold(4, SkillId);
        }
    }

    // ============================================================
    //  会心
    // ============================================================

    /// <summary>測量師の片眼鏡 (2026-11-20 作り直し): マップを 1 歩進むごとに、 次の戦闘の最初の 1 撃の会心率 +5% (その戦闘で最初に当てる撃・会心率は撃ごとに戻る)。
    /// 旧: 次の戦闘の会心率 +1% (その戦闘の間)。</summary>
    public class SurveyorMonocle : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "測量師の片眼鏡";
        public const float PctPerStep = 0.05f;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Once(Rw.Count(ctx, UsedKey) > 0, Rw.Count(ctx, Key) > 0);
        /// <summary>[persistent] 戦闘の始めの歩数 / 最初の 1 撃を撃った (1/0)。</summary>
        private const string Key = "rw.surveyor", UsedKey = "rw.surveyor.used";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.accumulatedValues[Key] = Rw.Steps; ctx.accumulatedValues[UsedKey] = 0; return; }
            if (!ctx.IsRealHit || Rw.Count(ctx, UsedKey) > 0) return;
            ctx.accumulatedValues[UsedKey] = 1;
            ctx.critRatePctAdd += Rw.Count(ctx, Key) * PctPerStep;
        }
    }

    /// <summary>烈刃: 臨界 2 につき会心率 +1%。</summary>
    public class FierceBlade : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "烈刃";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.rinkaiMeter / 2 > 0);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            ctx.critRatePctAdd += (ctx.rinkaiMeter / 2) / 100f;
        }
    }

    /// <summary>瑠璃砥石: 役が成立したターン、 会心率 +15%。</summary>
    public class LapisWhetstone : IPassiveSkillEffect
    {
        public string SkillId => "瑠璃砥石";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.firedRolesThisTurn.Count > 0) ctx.critRatePctAdd += 0.25f;
        }
    }

    /// <summary>燧石の指輪: 会心のたび臨界 +8。</summary>
    public class FlintRing : IPassiveSkillEffect
    {
        public string SkillId => "燧石の指輪";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            Kw.AddHeat(ctx, 8);
        }
    }

    /// <summary>寸鉄【規則】(2026-11-20 作り直し): 会心しなかった撃にも、 会心倍率の上乗せ分 × (会心率 ÷ 100) が乗る (会心率は 100% を上限に数える)。
    /// 掛けるのは PassiveSkillManager.ProcessDamage (ctx.critShareOnNonCrit)。 旧: 会心率が 100% を超えた分が会心ダメージの倍率になる (超えた 10% につき +0.1)。</summary>
    public class Suntetsu : IPassiveSkillEffect
    {
        public string SkillId => "寸鉄";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critShareOnNonCrit = true;
    }

    /// <summary>銑鉄棍【規則】(2026-11-20 作り直し): 会心が出なくなる。 代わりに、 会心率 1% につき与ダメージ +1% (会心率は 100% を上限に数える・与ダメ % の池・撃ごと)。
    /// 掛けるのは PassiveSkillManager.ProcessDamage (ctx.critToDamage)。 旧: 会心になるはずだった攻撃は敵の防御を無視し、 この戦闘中の敵の防御を 1 下げる。</summary>
    public class UnwhettedClub : IPassiveSkillEffect
    {
        public string SkillId => "銑鉄棍";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critToDamage = true;
    }
}

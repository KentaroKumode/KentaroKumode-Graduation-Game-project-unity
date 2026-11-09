using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  軸の品・追加 34 品 (2026-11-07)。 設計は docs/GAME.md §8-7。
    //
    //  軸ごとの品の数を臨界 (23 品・ブロンズ 7 / シルバー 6 / ゴールド 6 / レジェンダリー 4) にそろえた。
    //  品の数とレア度の内訳がそろわないと、 棚に出る確率が軸で 2 倍近く違い (臨界 21.2% 対 会心 11.5%)、
    //  店で自然に集まる軸が臨界に偏る。 どの品もその軸の資源を読むか作る (他の軸への変換器は足さない)。
    // ============================================================

    internal static class Ax2
    {
        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);
        /// <summary>このターン敵が攻撃してきて、 HP を 1 も減らさずに受けきった (OnTurnEnd で読む)。</summary>
        public static bool BlockedAll(CombatContext ctx)
            => Count(ctx, MutualKeys.EnemyAtkPreDefense) > 0 && Count(ctx, MutualKeys.HpLossFromEnemy) <= 0;
        public static bool LastTurn(CombatContext ctx, string key)
        {
            int t = Count(ctx, key);
            return t > 0 && t == ctx.currentTurn - 1;
        }
    }

    // ============================================================
    //  会心 (9)
    // ============================================================

    /// <summary>狙い目の刻み: 攻撃に挿した出目に 6 があるターン、 会心率 +15%。</summary>
    public class AimNotch : IPassiveSkillEffect
    {
        public string SkillId => "狙い目の刻み";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax2.Count(ctx, MutualKeys.AttackDiceMax) >= 6) ctx.critRatePctAdd += 0.25f;
        }
    }

    /// <summary>一の矢 (旧〈一本差しの型〉): 攻撃に挿したダイスが 1 個だけのターン、 会心率 +30%。</summary>
    public class FirstArrow : IPassiveSkillEffect
    {
        public string SkillId => "一の矢";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax2.Count(ctx, MutualKeys.AttackDiceCount) == 1) ctx.critRatePctAdd += 0.30f;
        }
    }

    /// <summary>余韻の鈴 (旧〈余韻の鍔〉): 会心が出た次のターン、 会心率 +20% (2026-11-07: 旧 +10%)。</summary>
    public class LingeringBell : IPassiveSkillEffect
    {
        public string SkillId => "余韻の鈴";
        private const string Key = "ax2.lingering";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnCriticalDamage) { ctx.accumulatedValues[Key] = ctx.currentTurn; return; }
            if (Ax2.LastTurn(ctx, Key)) ctx.critRatePctAdd += 0.30f;
        }
    }

    /// <summary>受け流しの扇 (旧〈受け流しの呼吸〉): ブロックで敵の攻撃を受けきった次のターン、 会心率 +25% (2026-11-07: 旧 +15%・ITT が既存の下位)。</summary>
    public class ParryFan : IPassiveSkillEffect
    {
        public string SkillId => "受け流しの扇";
        private const string Key = "ax2.parryBreath";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnTurnEnd) { if (Ax2.BlockedAll(ctx)) ctx.accumulatedValues[Key] = ctx.currentTurn; return; }
            if (Ax2.LastTurn(ctx, Key)) ctx.critRatePctAdd += 0.35f;
        }
    }

    /// <summary>刃毀れの見立て: 会心した攻撃は敵の防御を 10 無視する (ctx.critArmorPen・2026-11-07: 旧 3 → 6 → 10)。</summary>
    public class NickAppraisal : IPassiveSkillEffect
    {
        public string SkillId => "刃毀れの見立て";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critArmorPen += 10;
    }

    /// <summary>外れ続きの賽: 会心が出なかった攻撃のたび会心率 +15% (会心が出ると 0 に戻る・2026-11-07: 旧 +10%)。</summary>
    public class LosingStreakDie : IPassiveSkillEffect
    {
        public string SkillId => "外れ続きの賽";
        private const string Key = "ax2.streak";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired) { ctx.critRatePctAdd += 0.15f * Ax2.Count(ctx, Key); return; }
            if (!Rw.PlayerHit(ctx)) return;
            if (ctx.isCritical) ctx.accumulatedValues[Key] = 0;
            else Kw.Add(ctx, Key, 1);
        }
    }

    /// <summary>連なる閃き: 会心が 2 ターン続けて出たら、 次の攻撃の与ダメージ +50% (2026-11-07: 旧 +30%)。</summary>
    public class ChainedFlash : IPassiveSkillEffect
    {
        public string SkillId => "連なる閃き";
        private const string Last = "ax2.flash.last", Prev = "ax2.flash.prev";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnCriticalDamage)
            {
                int last = Ax2.Count(ctx, Last);
                if (last == ctx.currentTurn) return;
                ctx.accumulatedValues[Prev] = last;
                ctx.accumulatedValues[Last] = ctx.currentTurn;
                return;
            }
            if (!ctx.playerWonRoll) return;
            int l = Ax2.Count(ctx, Last), p = Ax2.Count(ctx, Prev);
            if (l > 0 && l == ctx.currentTurn - 1 && p == ctx.currentTurn - 2) Fam.Outgoing(ctx, 0.50f);
        }
    }

    /// <summary>抜き打ちの作法: 戦闘の最初の 2 回の攻撃は必ず会心 (2026-11-07: 旧 1 回)。</summary>
    public class QuickDrawArt : IPassiveSkillEffect
    {
        public string SkillId => "抜き打ちの作法";
        public const int Times = 2;
        private const string Key = "ax2.quickDraw";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax2.Count(ctx, Key) >= Times || Ax2.Count(ctx, MutualKeys.AttackDiceCount) <= 0) return;
            ctx.accumulatedValues[Key] = Ax2.Count(ctx, Key) + 1;
            ctx.forceCritical = true;
        }
    }

    /// <summary>千里眼の水晶: 心眼 1 につき会心倍率 +0.15 (2026-11-07: 旧 +0.05)。</summary>
    public class FarsightCrystal : IPassiveSkillEffect
    {
        public string SkillId => "千里眼の水晶";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.criticalMultiplier += 0.15f * ctx.AxisDepth(AxisRules.Crit);
    }

    // ============================================================
    //  充電 (8)
    // ============================================================

    /// <summary>予備のバッテリー (旧〈乾電池の束〉): 戦闘開始時 充電 +10 (2026-11-07: 旧 +5)。</summary>
    public class SpareBattery : IPassiveSkillEffect
    {
        public string SkillId => "予備のバッテリー";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.AddCharge(10);
    }

    /// <summary>静電気の毛布: ブロック端子に挿したダイス 1 個につき充電 +1。</summary>
    public class StaticBlanket : IPassiveSkillEffect
    {
        public string SkillId => "静電気の毛布";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Ax2.Count(ctx, MutualKeys.BlockDiceCount);
            if (n > 0) ctx.AddCharge(n);
        }
    }

    /// <summary>火打ちの針: 攻撃が通るたび充電 +1。</summary>
    public class FlintNeedle : IPassiveSkillEffect
    {
        public string SkillId => "火打ちの針";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.PlayerHit(ctx) && Rw.OncePerTurn(ctx, "ax2.flintNeedle")) ctx.AddCharge(1);
        }
    }

    /// <summary>蓄電の小手: 充電 5 につき、 攻撃に挿した出目 +7% (2026-11-07: 旧 充電 10 につき +5%)。</summary>
    public class StorageGauntlet : IPassiveSkillEffect
    {
        public string SkillId => "蓄電の小手";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = ctx.GetCharge() / 5;
            if (n > 0) ctx.AddAttackDicePct(0.07f * n);
        }
    }

    /// <summary>振り直しの撃鉄: リロールしたターン、 攻撃に挿した出目 +25% (2026-11-07: 旧 +10%)。</summary>
    public class RerollHammer : IPassiveSkillEffect
    {
        public string SkillId => "振り直しの撃鉄";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.rerollsThisTurn > 0) ctx.AddAttackDicePct(0.25f);
        }
    }

    /// <summary>絶縁の外套: 過充電の間、 ターンの始めにシールド +4 (2026-11-10・#40: 旧「受けるダメージ −3」。 充電軸に被ダメ −N が寄りすぎていた)。</summary>
    public class InsulatedCloak : IPassiveSkillEffect
    {
        public string SkillId => "絶縁の外套";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.IsOvercharged()) Rw.Shield(ctx, 4, "絶縁の外套");
        }
    }

    /// <summary>避雷の杭: 充電が 20 以上のターン、 充電を 5 使って攻撃に挿した出目 +25% (2026-11-07: 旧「上限のとき」)。</summary>
    public class LightningStake : IPassiveSkillEffect
    {
        public string SkillId => "避雷の杭";
        public const int MinCharge = 20;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.GetCharge() < MinCharge) return;
            if (ctx.ConsumeCharge(5)) ctx.AddAttackDicePct(0.25f);
        }
    }

    /// <summary>雷鳴の太鼓: 電流 1 につき充電の上限 +3 (2026-11-07: 旧 +2)。</summary>
    public class ThunderDrum : IPassiveSkillEffect
    {
        public string SkillId => "雷鳴の太鼓";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.chargeCapBonus = System.Math.Max(ctx.chargeCapBonus, 3 * ctx.AxisDepth(AxisRules.Charge));
    }

    // ============================================================
    //  出血 (7)
    // ============================================================

    /// <summary>欠け刃のナイフ: 攻撃が通るたび出血 +1。</summary>
    public class ChippedKnife : IPassiveSkillEffect
    {
        public string SkillId => "欠け刃のナイフ";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.PlayerHit(ctx) && Rw.OncePerTurn(ctx, "ax2.chipped")) ctx.AddEnemyBleed(1);
        }
    }

    /// <summary>棘付きの籠手: ブロックで敵の攻撃を受けきったターン、 出血 +2。</summary>
    public class ThornGlove : IPassiveSkillEffect
    {
        public string SkillId => "棘付きの籠手";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax2.BlockedAll(ctx)) ctx.AddEnemyBleed(2);
        }
    }

    /// <summary>裂け目の楔: 攻撃に挿した出目に 1 か 2 があるターン、 攻撃が通ると出血 +3 (2026-11-07: 旧 1 だけ)。</summary>
    public class RiftWedge : IPassiveSkillEffect
    {
        public string SkillId => "裂け目の楔";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int mn = Ax2.Count(ctx, MutualKeys.AttackDiceMin);
            if (!Rw.PlayerHit(ctx) || mn < 1 || mn > 2) return;
            if (Rw.OncePerTurn(ctx, "ax2.riftWedge")) ctx.AddEnemyBleed(3);
        }
    }

    /// <summary>血の滴る刃先: 敵の出血が 10 以上の間、 攻撃に挿した出目 +10%。</summary>
    public class DrippingEdge : IPassiveSkillEffect
    {
        public string SkillId => "血の滴る刃先";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.enemyBleedStacks >= 10) ctx.AddAttackDicePct(0.10f);
        }
    }

    /// <summary>止血禁じの軟膏: 出血の比例減衰の割る数 +6 (2026-11-07: 旧 +3)。</summary>
    public class NoClotSalve : IPassiveSkillEffect
    {
        public string SkillId => "止血禁じの軟膏";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.bleedDecayDivisorBonus += 6;
    }

    /// <summary>紅の搾り具: 出血が 15 以上の敵に攻撃が通ると、 出血の 1/3 を使い、 使った量 × 5 の軽減不可ダメージ (1 ターン 1 回)。
    /// 2026-11-07: 旧 30 以上・×3 は ITT が −0.7 (出血を使う分だけ毎ターンの出血のダメージが減り、 差し引きで損だった)。</summary>
    public class CrimsonPress : IPassiveSkillEffect
    {
        public string SkillId => "紅の搾り具";
        public const int Threshold = 15, Mul = 5;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Rw.PlayerHit(ctx) || ctx.enemyBleedStacks < Threshold) return;
            if (!Rw.OncePerTurn(ctx, "ax2.crimsonPress")) return;
            int used = Kw.ConsumeBleed(ctx, ctx.enemyBleedStacks / 3);
            if (used > 0) Kw.DamageEnemy(ctx, used * Mul);
        }
    }

    /// <summary>涸れぬ泉の血晶: 血晶 1 につき、 戦闘開始時に敵へ出血 +2。</summary>
    public class EndlessSpringCrystal : IPassiveSkillEffect
    {
        public string SkillId => "涸れぬ泉の血晶";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.AddEnemyBleed(2 * ctx.AxisDepth(AxisRules.Bleed));
    }

    // ============================================================
    //  毒 (6)  ── 毒のダメージ量そのものは増やさない
    // ============================================================

    /// <summary>酸の小瓶: 敵が攻撃するたび毒 +1。</summary>
    public class AcidVial : IPassiveSkillEffect
    {
        public string SkillId => "酸の小瓶";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax2.Count(ctx, MutualKeys.EnemyAtkPreDefense) > 0) Rw.Poison(ctx, 1);
        }
    }

    /// <summary>毒針の指輪: 攻撃が通ると、 攻撃に挿したダイス 1 個につき毒 +1。</summary>
    public class NeedleRing : IPassiveSkillEffect
    {
        public string SkillId => "毒針の指輪";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.PlayerHit(ctx) && Rw.OncePerTurn(ctx, "ax2.needleRing")) Rw.Poison(ctx, Ax2.Count(ctx, MutualKeys.AttackDiceCount));
        }
    }

    /// <summary>腐葉土の袋: ターン開始時、 敵の毒が 10 未満なら毒 +3 (2026-11-07: 旧「0 なら」)。</summary>
    public class LeafMoldBag : IPassiveSkillEffect
    {
        public string SkillId => "腐葉土の袋";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.EnemyPoison < 10) Rw.Poison(ctx, 3);
        }
    }

    /// <summary>麻痺の吹き矢: 敵の毒 5 につき、 敵の防御 −1 (貫通として足す・2026-11-07: 旧 毒 10 につき)。</summary>
    public class NumbDart : IPassiveSkillEffect
    {
        public string SkillId => "麻痺の吹き矢";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.playerArmorPen += ctx.EnemyPoison / 5;
    }

    /// <summary>瘴気の外套: 敵の毒が 10 以上の間、 受けるダメージ − 瘴気 (2026-11-07: 旧 毒 20 以上・瘴気の半分)。</summary>
    public class MiasmaMantle : IPassiveSkillEffect
    {
        public string SkillId => "瘴気の外套";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.EnemyPoison >= 10) ctx.playerFlatDamageReduction += ctx.AxisDepth(AxisRules.Poison);
        }
    }

    /// <summary>毒溜まりの主: 毒が 30 以上の敵の攻撃 −25% (ctx.enemyAtkCutPct・予告の前に掛かる・2026-11-07: 旧 50 以上・−20%)。</summary>
    public class PoolKeeper : IPassiveSkillEffect
    {
        public string SkillId => "毒溜まりの主";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.EnemyPoison >= 30) ctx.enemyAtkCutPct += 0.25f;
        }
    }

    // ============================================================
    //  シールド (4)
    // ============================================================

    /// <summary>鋲の胸当て: ターン開始時シールド +6 (2026-11-07: 旧 +2)。</summary>
    public class RivetPlate : IPassiveSkillEffect
    {
        public string SkillId => "鋲の胸当て";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Shield(ctx, 6, SkillId);
    }

    /// <summary>受け太刀の籠手: ブロックで敵の攻撃を受けきったターン、 シールド +10 (2026-11-07: 旧 +4)。</summary>
    public class CatchBracer : IPassiveSkillEffect
    {
        public string SkillId => "受け太刀の籠手";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax2.BlockedAll(ctx)) Rw.Shield(ctx, 10, SkillId);
        }
    }

    /// <summary>重ね札の盾: シールドが 8 以上の間、 受けるダメージ −5 (2026-11-07: 旧 10 以上・−2 → −3 → −5)。</summary>
    public class LayeredTagShield : IPassiveSkillEffect
    {
        public string SkillId => "重ね札の盾";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.consShield >= 8) ctx.playerFlatDamageReduction += 5;
        }
    }

    /// <summary>割れ盾の仕返し: シールドが割られた次のターン、 攻撃に挿した出目 +50% (2026-11-07: 旧 +30%)。</summary>
    public class BrokenShieldGrudge : IPassiveSkillEffect
    {
        public string SkillId => "割れ盾の仕返し";
        private const string Key = "ax2.grudge";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldBreak, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnShieldBreak) { ctx.accumulatedValues[Key] = ctx.currentTurn; return; }
            if (Ax2.LastTurn(ctx, Key)) ctx.AddAttackDicePct(0.50f);
        }
    }
}

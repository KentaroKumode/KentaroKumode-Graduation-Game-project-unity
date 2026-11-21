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

    /// <summary>狙い目の刻み: 攻撃の端子を挿した出目に 6 があるターン、 会心率 +40% (2026-11-20: 旧 +25%)。</summary>
    public class AimNotch : IPassiveSkillEffect
    {
        public string SkillId => "狙い目の刻み";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax2.Count(ctx, MutualKeys.AttackDiceMax) >= 6) ctx.critRatePctAdd += 0.40f;
        }
    }

    /// <summary>一の矢 (旧〈一本差しの型〉): 攻撃の端子を挿したダイスが 1 個だけのターン、 会心率 +30%。</summary>
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
    public class LingeringBell : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "余韻の鈴";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Ax2.Count(ctx, Key) > 0 && (Ax2.Count(ctx, Key) == ctx.currentTurn || Ax2.LastTurn(ctx, Key)));
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

    /// <summary>負け越しの賽: 会心が出なかった攻撃のたび会心率 +15% (会心が出ると 0 に戻る・2026-11-07: 旧 +10%)。</summary>
    public class LosingStreakDie : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "負け越しの賽";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Ax2.Count(ctx, Key));
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

    /// <summary>抜き打ちの作法 (2026-11-20 作り直し): 戦闘の最初の 2 ターン、 そのターンの最初の 1 撃は必ず会心 (ctx.forceCritThisHit)。
    /// 旧: 戦闘の最初の 2 回の攻撃 (OnWired で forceCritical = そのターンの全部の撃) ── 多段攻撃で 1 ターン目に尽きた。</summary>
    public class QuickDrawArt : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "抜き打ちの作法";
        public string SkillId => Id;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => new RelicView { known = true, lit = ctx.currentTurn <= Turns, count = System.Math.Max(0, Turns + 1 - System.Math.Max(1, ctx.currentTurn)), spent = ctx.currentTurn > Turns };
        public const int Turns = 2;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (FirstHitForced(ctx)) ctx.forceCritThisHit = true;
        }

        /// <summary>このターンの最初の 1 撃が必ず会心になるターンか (撃の中では今の撃が最初の 1 撃か)。 BOT の見積もりも読む。</summary>
        public static bool FirstHitForced(CombatContext ctx)
            => ctx != null && ctx.currentTurn >= 1 && ctx.currentTurn <= Turns && ctx.hitIndex == 0;
    }

    /// <summary>千里眼の水晶 (2026-11-20 作り直し): 会心が出たターン、 次のターンの手札 +1 枚 (そのターンに何度会心しても 1 枚)・
    /// 次のターンの最初の 1 撃の会心率 + 心眼 × 5%。 旧: 心眼 1 につき会心倍率 +0.15 (items.json の stats・〈研ぎ師の見立て書〉と同じ効果だった)。</summary>
    public class ClairvoyantCrystal : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "千里眼の水晶";
        public string SkillId => Id;
        public const float CritPerDepth = 0.05f;
        public const int Draw = 1;
        /// <summary>[persistent] 会心が出た直近のターンの番号。</summary>
        private const string Key = "ax2.clairvoyant";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Ax2.LastTurn(ctx, Key));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnCriticalDamage)
            {
                if (Ax2.Count(ctx, Key) == ctx.currentTurn) return;   // このターンはもう数えた
                ctx.accumulatedValues[Key] = ctx.currentTurn;
                Rw.DrawNext(Draw);
                return;
            }
            // OnCriticalCheck: 前のターンに会心が出ていたら、 このターンの最初の 1 撃だけ会心率を足す (会心率は撃ごとに戻る)
            if (FirstHitBonus(ctx) > 0f) ctx.critRatePctAdd += FirstHitBonus(ctx);
        }

        /// <summary>このターンの最初の 1 撃に足す会心率 (前のターンに会心が出ていなければ 0)。 撃の中では今の撃が最初の 1 撃の時だけ。 BOT の見積もりも読む。</summary>
        public static float FirstHitBonus(CombatContext ctx)
            => ctx != null && ctx.hitIndex == 0 && Ax2.LastTurn(ctx, Key) ? CritPerDepth * ctx.AxisDepth(AxisRules.Crit) : 0f;
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
            if (Rw.PlayerHit(ctx)) ctx.AddEnemyBleed(1);
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

    /// <summary>裂け目の楔: 攻撃の端子を挿した出目に 1 か 2 があるターン、 攻撃が通ると出血 +3 (2026-11-07: 旧 1 だけ)。</summary>
    public class RiftWedge : IPassiveSkillEffect
    {
        public string SkillId => "裂け目の楔";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int mn = Ax2.Count(ctx, MutualKeys.AttackDiceMin);
            if (!Rw.PlayerHit(ctx) || mn < 1 || mn > 2) return;
            ctx.AddEnemyBleed(3);
        }
    }

    /// <summary>血の滴る刃先 (2026-11-20 作り直し): 敵の出血が 10 以上の間、 攻撃が通るたび HP +2 (撃ごと)。 旧: 与ダメージ +10%。</summary>
    public class DrippingEdge : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "血の滴る刃先";
        public const int MinBleed = 10, Heal = 2;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.enemyBleedStacks >= MinBleed);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.PlayerHit(ctx) && ctx.IsRealHit && ctx.enemyBleedStacks >= MinBleed) Rw.Heal(ctx, Heal);
        }
    }

    /// <summary>止血禁じの軟膏: 出血の比例減衰の割る数 +10 (2026-11-20: 旧 +6・2026-11-07: 旧 +3)。</summary>
    public class NoClotSalve : IPassiveSkillEffect
    {
        public string SkillId => "止血禁じの軟膏";
        public const int DivisorBonus = 10;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.bleedDecayDivisorBonus += DivisorBonus;
    }

    /// <summary>紅の搾り具: 出血が 10 以上の敵に攻撃が通ると、 出血の 1/3 を使い、 使った量 × 8 の軽減不可ダメージ (1 ターン 1 回)。
    /// 2026-11-20: 旧 15 以上・×5 (ITT が 0 前後)。 2026-11-07: 旧 30 以上・×3 は ITT が −0.7 (出血を使う分だけ毎ターンの出血のダメージが減り、 差し引きで損だった)。</summary>
    public class CrimsonPress : IPassiveSkillEffect
    {
        public string SkillId => "紅の搾り具";
        public const int Threshold = 10, Mul = 8;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Rw.PlayerHit(ctx) || ctx.enemyBleedStacks < Threshold) return;
            if (!Rw.OncePerTurn(ctx, "ax2.crimsonPress")) return;
            int used = Kw.ConsumeBleed(ctx, ctx.enemyBleedStacks / 3);
            if (used > 0) Kw.DamageEnemy(ctx, used * Mul);
        }
    }

    /// <summary>涸れぬ血晶: 血晶 1 につき、 戦闘開始時に敵へ出血 +2。</summary>
    public class EndlessSpringCrystal : IPassiveSkillEffect
    {
        public string SkillId => "涸れぬ血晶";
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

    /// <summary>毒針の指輪 (2026-11-20): 攻撃が通るたび (撃ごと)、 敵に毒 +1。 旧: 攻撃が通ると、 攻撃の端子を挿したダイス 1 個につき毒 +1 ── 撃ごとに数えるので多段攻撃で 1 ターン +9 になっていた。</summary>
    public class NeedleRing : IPassiveSkillEffect
    {
        public string SkillId => "毒針の指輪";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.PlayerHit(ctx) && ctx.IsRealHit) Rw.Poison(ctx, 1);
        }
    }

    /// <summary>腐葉土の袋: ターン開始時、 敵の毒が 10 未満なら毒 +3、 10 以上ならそのターンの攻撃 +3 (2026-11-10: 攻撃 +3 を追加・2026-11-07: 旧「0 なら」)。
    /// 攻撃 +N は OnPostRoll で足す (ターン開始では配線前の値が上書きされる)。 ターン開始で決めた側を、 そのターンの番号で覚えておく。</summary>
    public class LeafMoldBag : IPassiveSkillEffect
    {
        public string SkillId => "腐葉土の袋";
        public const int Atk = 3;
        private const string AtkTurnKey = "rw.leafMoldAtkTurn";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnTurnStart)
            {
                if (ctx.EnemyPoison < 10) Rw.Poison(ctx, 3);
                else ctx.accumulatedValues[AtkTurnKey] = ctx.currentTurn;
                return;
            }
            if ((int)ctx.GetAccumulated(AtkTurnKey) == ctx.currentTurn && ctx.currentTurn > 0) ctx.AddPlayerAttackOrDiceBonus(Atk);
        }
    }

    // 〈麻痺の吹き矢〉(敵の毒 5 につき貫通 +1・OnWired) は 2026-11-11 に items.json の stats へ移した (段 3・per=enemyPoison・perEvery 5)。

    /// <summary>瘴気の外套: 敵の毒が 10 以上の間、 受けるダメージ − 瘴気 (2026-11-07: 旧 毒 20 以上・瘴気の半分)。</summary>
    public class MiasmaMantle : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "瘴気の外套";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.EnemyPoison >= 10);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.EnemyPoison >= 10) ctx.playerFlatDamageReduction += ctx.AxisDepth(AxisRules.Poison);
        }
    }

    /// <summary>毒溜まりの主: 毒が 30 以上の敵の攻撃 −25% (ctx.enemyAtkCutPct・予告の前に掛かる・2026-11-07: 旧 50 以上・−20%)。</summary>
    public class PoolKeeper : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "毒溜まりの主";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.EnemyPoison >= 30);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.EnemyPoison >= 30) ctx.enemyAtkCutPct += 0.25f;
        }
    }

    // ============================================================
    //  シールド (4)
    // ============================================================

    /// <summary>英雄の意志 (2026-11-20 作り直し): エリート戦・ボス戦で、 敵が大技の予告 (またはチャージ) を出しているターン、 ターン開始時にシールド +18。
    /// 予告の判定は CombatManager.EnemyHeavyOrChargeNow (このターンがダウンなら出さない)。 旧: エリート戦・ボス戦の開始時シールド +6 (items.json の stats)。</summary>
    public class HeroWill : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "英雄の意志";
        public const int Shield = 18;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Rw.StrongFoe() && (CombatSystem.CombatManager.Instance?.EnemyHeavyOrChargeNow(ctx.currentTurn) ?? false));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Rw.StrongFoe() || !(CombatSystem.CombatManager.Instance?.EnemyHeavyOrChargeNow(ctx.currentTurn) ?? false)) return;
            Rw.Shield(ctx, Shield, SkillId);
            Rw.Log($"[英雄の意志] 敵の大技の予告 → シールド +{Shield}");
        }
    }

    /// <summary>鋲の胸当て: ターン開始時シールド +1 (2026-11-20: 旧 +6・2026-11-07: 旧 +2)。</summary>
    public class RivetPlate : IPassiveSkillEffect
    {
        public string SkillId => "鋲の胸当て";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Shield(ctx, 1, SkillId);
    }

    /// <summary>受け太刀の籠手: ブロックで敵の攻撃を受けきったターン、 シールド +3 (2026-11-20: 旧 +10・2026-11-07: 旧 +4)。</summary>
    public class CatchBracer : IPassiveSkillEffect
    {
        public string SkillId => "受け太刀の籠手";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax2.BlockedAll(ctx)) Rw.Shield(ctx, 3, SkillId);
        }
    }

    /// <summary>重ね札の盾: シールドが 8 以上の間、 受けるダメージ −2 (2026-11-20: 旧 −5・2026-11-07: 旧 10 以上・−2 → −3 → −5)。
    /// 敵の攻撃を受けるその時のシールドで判定する (2026-11-20: 旧 ターンの頭に 1 回だけ判定し、 途中で 8 を切っても残っていた)。</summary>
    public class LayeredTagShield : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "重ね札の盾";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.consShield >= 8);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.playerWonRoll || ctx.finalDamage <= 0 || ctx.consShield < 8) return;
            ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 2);
        }
    }

    /// <summary>割れ盾の仕返し: シールドが割られた次のターン、 与ダメージ +30% (2026-11-10: 旧 出目 +50%)。</summary>
    public class BrokenShieldGrudge : IPassiveSkillEffect
    {
        public string SkillId => "割れ盾の仕返し";
        private const string Key = "ax2.grudge";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldBreak, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnShieldBreak) { ctx.accumulatedValues[Key] = ctx.currentTurn; return; }
            if (Ax2.LastTurn(ctx, Key)) ctx.AddOutgoingPct(0.30f);
        }
    }
}

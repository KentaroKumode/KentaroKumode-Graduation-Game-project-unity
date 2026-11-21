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
}

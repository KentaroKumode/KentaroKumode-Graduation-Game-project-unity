using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  軸の締めの LEGENDARY (2026-11-20)。 設計は docs/GAME.md §23-32。
    //
    //  6 軸の仕上げの品。 その軸だけの仕組みで、 安全装置を条件付きで 1 つ破るか、 ゲームの規則を書き換える。
    //  その軸を深く組まないと働かない (1 品で蹂躙にならない)。 数値はすべて仮。
    //  品の文に無い上限・回数制限は足さない (作者の方針)。
    // ============================================================

    internal static class Ax3
    {
        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);
    }

    // ============================================================
    //  会心
    // ============================================================

    /// <summary>天眼の鍔: 会心の一撃 (主攻撃) のあと、 敵の HP が「会心倍率 × 5%」(最大 HP 比) 以下なら倒す (ボスはその半分・BountyHunterHelper.IsBoss = BossIds.IsBoss)。
    /// 会心倍率はその攻撃に掛かった倍率 (<see cref="Kw.LastCritMulKey"/>)。 判定と倒すのは CombatManager (主攻撃を HP から引いた直後・<see cref="KillLine"/>)。
    /// 倒し方は〈百人切りの鉈〉と同じ (〈回帰性真理〉の CanExecuteEnemy で止まる)。</summary>
    public class HeavenEyeGuard : IPassiveSkillEffect
    {
        public const string Id = "天眼の鍔";
        public string SkillId => Id;
        public const float PctPerMul = 0.05f;
        /// <summary>[persistent] この品を持っている (CombatManager が読む)。</summary>
        public const string HeldKey = "ax3.tengan.held";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.accumulatedValues[HeldKey] = 1;

        /// <summary>この品を持っている時の、 倒せる HP の線 (これ以下なら倒す・持っていない・会心倍率が無い時は −1)。</summary>
        public static int KillLine(CombatContext ctx)
        {
            if (ctx == null || ctx.GetAccumulated(HeldKey) <= 0f || ctx.enemyMaxHP <= 0) return -1;
            float frac = ctx.GetAccumulated(Kw.LastCritMulKey) * PctPerMul;
            if (BountyHunterHelper.IsBoss(ctx)) frac /= 2f;
            return frac > 0f ? (int)(ctx.enemyMaxHP * frac) : -1;
        }
    }

    /// <summary>死相の面頬: 会心したターン、 そのターンの敵の攻撃を (会心倍率 − 1) × 20% 減らす (最大 −50%)。
    /// 会心倍率は会心した時の <see cref="CombatContext.criticalMultiplier"/>。 減らすのは敵の攻撃の解決 (CombatManager.ApplyLossDamageModifiers)。</summary>
    public class DeathMaskVisor : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "死相の面頬";
        public string SkillId => Id;
        public const float PerMul = 0.20f, MaxCut = 0.50f;
        /// <summary>会心したターンの番号 / そのターンの減らす割合 (同じターンに何度会心しても大きい方 1 つ)。</summary>
        public const string TurnKey = "ax3.deathMask.turn", CutKey = "ax3.deathMask.cut";
        /// <summary>秘宝の列の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(CutNow(ctx) > 0f);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            float cut = System.Math.Min(MaxCut, System.Math.Max(0f, (ctx.criticalMultiplier - 1f) * PerMul));
            if (Ax3.Count(ctx, TurnKey) != ctx.currentTurn) { ctx.accumulatedValues[TurnKey] = ctx.currentTurn; ctx.accumulatedValues[CutKey] = 0f; }
            if (cut > ctx.GetAccumulated(CutKey)) ctx.accumulatedValues[CutKey] = cut;
        }

        /// <summary>このターンの敵の攻撃を減らす割合 (会心していないターンは 0)。</summary>
        public static float CutNow(CombatContext ctx)
            => ctx != null && ctx.currentTurn > 0 && Ax3.Count(ctx, TurnKey) == ctx.currentTurn ? ctx.GetAccumulated(CutKey) : 0f;
    }

    // ============================================================
    //  出血
    // ============================================================

    /// <summary>戻り血の古鎌: 秘宝が敵の出血を使うたび (<see cref="Kw.ConsumeBleed"/> の出来事 OnBleedConsume)、 使った量の半分 (切り捨て) が
    /// 次のターンの頭に出血として戻る。 自然に減る分 (OnBleedDecay) は戻らない。 戻す出血は AddEnemyBleed を通す。</summary>
    public class ReturningBloodSickle : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "戻り血の古鎌";
        public string SkillId => Id;
        /// <summary>[persistent] 次のターンの頭に戻す量 / このターンに使った分の半分 / その「このターン」の番号。</summary>
        private const string ReadyKey = "ax3.sickle.ready", CurKey = "ax3.sickle.cur", TurnKey = "ax3.sickle.turn";
        /// <summary>秘宝の列の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Ax3.Count(ctx, ReadyKey) + Ax3.Count(ctx, CurKey));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBleedConsume, PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBleedConsume)
            {
                int half = Kw.EventAmount(ctx) / 2;
                if (half <= 0) return;
                if (Ax3.Count(ctx, TurnKey) != ctx.currentTurn) Roll(ctx);
                Kw.Add(ctx, CurKey, half);
                return;
            }
            // OnTurnStart: 前のターンまでに使った分を戻す (このターンの頭に使った分は次のターンへ)
            if (Ax3.Count(ctx, TurnKey) < ctx.currentTurn) Roll(ctx);
            int n = Ax3.Count(ctx, ReadyKey);
            if (n <= 0) return;
            ctx.accumulatedValues[ReadyKey] = 0;
            ctx.AddEnemyBleed(n);
            Rw.Log($"[戻り血の古鎌] 使った出血の半分が戻る: 出血 +{n} → {ctx.enemyBleedStacks}");
        }

        /// <summary>「このターン」の分を「次のターンの頭に戻す」側へ移し、 今のターンの番号にする。</summary>
        private static void Roll(CombatContext ctx)
        {
            Kw.Add(ctx, ReadyKey, Ax3.Count(ctx, CurKey));
            ctx.accumulatedValues[CurKey] = 0;
            ctx.accumulatedValues[TurnKey] = ctx.currentTurn;
        }
    }

    /// <summary>血の楔: 敵が得るシールドと回復の量、 敵の被ダメ軽減のパッシブで減るダメージを、 出血 ÷ (出血 + 40) の割合だけ弱める (0 にはならない)。
    /// 掛ける所は <see cref="CombatContext.WedgeEnemyGain"/> を呼ぶ口: vescaShield の増加・ReduceEnemyHeal・CombatManager の敵の回復 (フロアデバフ・挑戦カード〈再生〉)・
    /// 主攻撃の OnPreReceiveDamage の前後差 (CombatManager.Damage.cs)。</summary>
    public class BloodWedge : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "血の楔";
        public string SkillId => Id;
        /// <summary>秘宝の列の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.enemyBleedStacks > 0);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.bleedWedge = true;
    }

    // ============================================================
    //  臨界
    // ============================================================

    /// <summary>暴れ窯の火床: 超臨界爆発 (OnSuperBurst) のたび、 この戦闘の爆発の閾値が 3 下がる (下げた合計は最大 −15)。</summary>
    public class WildKilnHearth : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "暴れ窯の火床";
        public string SkillId => Id;
        public const int Step = 3, MaxCut = 15;
        /// <summary>[persistent] この戦闘で下げた閾値の合計。</summary>
        private const string CutKey = "ax3.kiln.cut";
        /// <summary>秘宝の列の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Ax3.Count(ctx, CutKey));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnSuperBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            int c = System.Math.Min(Step, MaxCut - Ax3.Count(ctx, CutKey));
            if (c <= 0) return;
            ctx.rinkaiThreshold -= c;
            Kw.Add(ctx, CutKey, c);
            Rw.Log($"[暴れ窯の火床] 超臨界爆発 → 閾値 −{c} → {ctx.rinkaiThreshold}");
        }
    }

    /// <summary>柱折りの発破筒: 爆発のたび、 ボスの次の目盛り (§6-15) が 爆発のダメージ × 炉温 × 10% (切り捨て) だけ上がる (今の HP の 1 下まで)。
    /// ダウンが明けると、 HP 0 の所に新しい目盛りが 1 本できる。 目盛りを持つ敵 (ボス) にだけ効く。
    /// 爆発のダメージ = その爆発 1 回の量 (<see cref="Kw.BurstDamageNowKey"/>・超臨界なら ×1.5)。 目盛りを動かすのは CombatManager (<c>RaiseBossNextNotch</c> / ダウンが明けた時)。</summary>
    public class PillarBreakerCharge : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "柱折りの発破筒";
        public string SkillId => Id;
        /// <summary>[persistent] この品を持っている (CombatManager がダウンが明けた時に目盛りを足すか読む)。</summary>
        public const string HeldKey = "ax3.pillar.held";
        /// <summary>秘宝の列の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(CombatSystem.CombatManager.Instance?.Down?.Enabled ?? false);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); ctx.accumulatedValues[HeldKey] = 1; return; }
            int add = (int)((long)Ax3.Count(ctx, Kw.BurstDamageNowKey) * ctx.AxisDepth(AxisRules.Heat) / 10);
            if (add > 0) CombatSystem.CombatManager.Instance?.RaiseBossNextNotch(add);
        }

        /// <summary>この戦闘でこの品が働いている。</summary>
        public static bool Held(CombatContext ctx) => ctx != null && ctx.GetAccumulated(HeldKey) > 0f;
    }

    // ============================================================
    //  シールド
    // ============================================================

    /// <summary>落城の礎石: 毎ターンの頭にシールドが半分になる時 (OnShieldDecay・量 = 失った分)、 消える分 × (1 + 堅牢 × 20%) (切り捨て) を
    /// 敵への軽減不可ダメージにする (Kw.DamageEnemy)。</summary>
    public class FallenCastleCornerstone : IPassiveSkillEffect
    {
        public const string Id = "落城の礎石";
        public string SkillId => Id;
        public const int PctPerDepth = 20;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldDecay };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int lost = Kw.EventAmount(ctx);
            if (lost <= 0) return;
            int dmg = (int)((long)lost * (100 + PctPerDepth * ctx.AxisDepth(AxisRules.Shield)) / 100);
            Kw.DamageEnemy(ctx, dmg);
            Rw.Log($"[落城の礎石] シールドの半減で消えた {lost} → 敵へ {dmg} (軽減不可)");
        }
    }

    /// <summary>防人の軍標: シールドを得るたび (OnShieldGain)、 この戦闘の最終与ダメージ倍率 +5% (ctx.finalDamagePct)。 回数で数える・上限なし (作者の決定)。</summary>
    public class BorderGuardStandard : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "防人の軍標";
        public string SkillId => Id;
        public const int PctPerGain = 5;
        /// <summary>秘宝の列の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Pct(ctx.finalDamagePct / 100f);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnShieldGain };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.finalDamagePct += PctPerGain;
    }
}

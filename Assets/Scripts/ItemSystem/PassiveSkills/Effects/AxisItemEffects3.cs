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
    /// 次のターンの頭に出血として戻る。 自然に減る分 (OnBleedDecay) は戻らない。 戻す出血は AddEnemyBleed を通す (誓約の封じ・〈赤錆の誓約〉の 2 倍も効く)。</summary>
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
    //  毒
    // ============================================================

    /// <summary>腐蝕の王冠【規則】: 敵の毒の上限 (99) が無くなる (CombatContext.AddStatus)。 上限が無いので OnPoisonCap は出ない。</summary>
    public class CorrosionCrown : IPassiveSkillEffect
    {
        public const string Id = "腐蝕の王冠";
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.poisonCapRemoved = true;
    }

    /// <summary>骨溶かしの黒油: 毒のダメージが 1 以上入ったターンにボスの目盛り (§6-15) に入ったひびは、 割れた時のダウンが弱くならない (受けるダメージ ×1.5 が付く)。
    /// 毒のダメージが入ったターンの番号をここで覚え、 CombatManager がターンの終わりにひびを入れる時に読む (<see cref="PoisonTurnNow"/>・BossDownState.NoteTurnEndCracks)。</summary>
    public class BoneMeltOil : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "骨溶かしの黒油";
        public string SkillId => Id;
        /// <summary>[persistent] 毒のダメージが最後に入ったターンの番号 (この品を持っている時だけ書く)。</summary>
        public const string PoisonTurnKey = "ax3.boneOil.poisonTurn";
        /// <summary>秘宝の列の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(PoisonTurnNow(ctx));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPoisonTick };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Kw.EventAmount(ctx) >= 1) ctx.accumulatedValues[PoisonTurnKey] = ctx.currentTurn;
        }

        /// <summary>このターンに毒のダメージが入り、 この品を持っている。</summary>
        public static bool PoisonTurnNow(CombatContext ctx)
            => ctx != null && ctx.currentTurn > 0 && Ax3.Count(ctx, PoisonTurnKey) == ctx.currentTurn;
    }
}

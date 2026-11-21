using GameLoop;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  消耗品の連動品 (2026-11-20・item-overhaul-spec §7-2・名前は仮・数値は仮)。
    //  出来事 OnConsumableUse (GameLoop.Consumables.Use が戦闘中に使えた時に配る・量 = Tier) を読む。
    //  品の文に無い上限・回数制限は足さない (作者の方針)。
    // ============================================================

    /// <summary>帯革の薬嚢: 戦闘中に消耗品を使うたび、 端子を 1 枚引いて手札に加え、 シールド +6。</summary>
    public class BeltPouch : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.LinkPouch;
        public const int Draw = 1, ShieldGain = 6;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnConsumableUse };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var dk = CombatSystem.CombatManager.Instance?.TerminalDeck;
            int got = dk != null ? dk.DrawIntoHand(Draw) : 0;
            Rw.Shield(ctx, ShieldGain, ItemIds.LinkPouch);
            Rw.Log($"[帯革の薬嚢] 消耗品を使った → 端子 {got} 枚引く・シールド +{ShieldGain}");
        }
    }

    /// <summary>底抜けの小瓶: 戦闘で最初に使った消耗品は消費されず、 その戦闘中にもう 1 回使える (1 戦闘 1 回)。
    /// もう 1 回使わなければ、 戦闘の終わりに消える (<see cref="Consumables.Consume"/>)。 使う時の判定は <see cref="Consumables.Use"/> が <see cref="TryKeep"/> で聞く。</summary>
    public class BottomlessVial : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.LinkBottomless;
        /// <summary>[persistent] 0 = まだ / 1 = 最初の品を残している / 2 = 使い終わった。</summary>
        private const string StateKey = "link.bottomless.state";
        /// <summary>残している品 (戦闘の間だけ・<see cref="_ctx"/> の戦闘のもの)。</summary>
        private static string _keptId;
        private static CombatContext _ctx;

        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Once(Rw.Count(ctx, StateKey) >= 2, true);

        /// <summary>消耗品を使った直後 (効果が出た後・所持から減らす前): 消費しないなら true。 <paramref name="ctx"/> が null (戦闘の外) なら false。</summary>
        public static bool TryKeep(CombatContext ctx, RunState run, string id)
        {
            if (ctx == null || !AxisRules.Owns(run, ItemIds.LinkBottomless)) return false;
            int st = Rw.Count(ctx, StateKey);
            if (st == 0)
            {
                ctx.accumulatedValues[StateKey] = 1; _keptId = id; _ctx = ctx;
                Rw.Log($"[底抜けの小瓶] 〈{id}〉は消費しない (この戦闘中にもう 1 回使える)");
                return true;
            }
            if (st == 1 && ReferenceEquals(_ctx, ctx) && id == _keptId) { ctx.accumulatedValues[StateKey] = 2; _keptId = null; _ctx = null; }
            return false;
        }

        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, StateKey) == 1 && ReferenceEquals(_ctx, ctx) && _keptId != null)
            {
                Consumables.Consume(Rw.Run, _keptId);
                Rw.Log($"[底抜けの小瓶] もう 1 回使わなかった〈{_keptId}〉は戦闘の終わりに消える");
            }
            _keptId = null; _ctx = null;
        }
    }

    /// <summary>行商人の見本箱: 店の戦術消耗品の値段 −50% (<see cref="PriceMul"/>・ShopManager が読む)。 戦闘の始めに消耗品を 1 つも持っていなければ、
    /// BRONZE か SILVER の戦術消耗品を 1 つ得る (その戦闘の間だけ ── 使わなければ戦闘の終わりに消える・乱数キー "link.sampleBox")。</summary>
    public class SampleBox : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.LinkSampleBox;
        public const float PriceMul = 0.5f;
        private static string _grantedId;
        private static CombatContext _ctx;

        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var run = Rw.Run;
            if (run?.ownedConsumables == null) return;
            if (t == PassiveSkillTrigger.OnBattleEnd)
            {
                if (ReferenceEquals(_ctx, ctx) && _grantedId != null && run.ownedConsumables.Remove(_grantedId))
                    Rw.Log($"[行商人の見本箱] 使わなかった〈{_grantedId}〉は戦闘の終わりに返す");
                _grantedId = null; _ctx = null;
                return;
            }
            _grantedId = null; _ctx = null;
            if (run.ownedConsumables.Count > 0) return;
            var pool = Consumables.TacticPool(ItemRarity.SILVER);
            if (pool.Count == 0) return;
            string id = pool[GameRng.Range(0, pool.Count, "link.sampleBox")].id;
            run.ownedConsumables.Add(id);
            _grantedId = id; _ctx = ctx;
            Rw.Log($"[行商人の見本箱] 消耗品が無い → 〈{id}〉をこの戦闘の間だけ得る");
        }
    }
}

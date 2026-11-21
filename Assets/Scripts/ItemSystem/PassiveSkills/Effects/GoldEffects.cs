using GameLoop;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  金の 12 品 (2026-11-21・full-item-rework-ledger §⑨・docs/GAME.md §7-9)。 枠外 (動的 4 枠ロックの対象外)・どの軸でも使える純経済。
    //  戦闘の中で働く分 (硬貨・小切手・領収書・算盤・見本箱) はこのファイルのクラス。 戦闘の外の分は持っているかを読む所が効かせる:
    //    封蝋 = PassiveItems の RoadMoneyBandEffect (層に入るたび) / 紹介状・合札・手形 = ShopManager / 銭皿 = AxisPool.SkipGoldFor (見送り換金) /
    //    天秤 = GameManager.Rewards (<see cref="SpoilsScale.Apply"/>) / 溶金 = items.json の stats (per = coins・swordShopSpent・数えるのは ShopManager.Close)。
    // ============================================================

    /// <summary>銭皿: 秘宝を見送り換金したとき、 獲得ゴールド +50% (切り上げ・AxisPool.SkipGoldFor が掛ける)。</summary>
    public static class CoinDish
    {
        public const int Pct = 50;
        /// <summary>見送り換金の額 <paramref name="gold"/> に〈銭皿〉を掛けた額 (持っていなければそのまま)。</summary>
        public static int Apply(RunState run, int gold)
            => gold > 0 && run != null && run.OwnsPassive(ItemIds.GoldCoinDish) ? (gold * (100 + Pct) + 99) / 100 : gold;
    }

    /// <summary>戦果の天秤: 戦闘勝利時の獲得ゴールド +30% (切り上げ)・エリートかボスを倒したならさらに +15G (GameManager.Rewards が報酬に掛ける)。</summary>
    public static class SpoilsScale
    {
        public const int Pct = 30, StrongBonus = 15;
        /// <summary>戦闘の報酬 <paramref name="coins"/> に〈戦果の天秤〉を掛けた額 (<paramref name="strong"/> = エリート戦かボス戦)。</summary>
        public static int Apply(RunState run, int coins, bool strong)
        {
            if (run == null || !run.OwnsPassive(ItemIds.GoldScale)) return coins;
            return (System.Math.Max(0, coins) * (100 + Pct) + 99) / 100 + (strong ? StrongBonus : 0);
        }
    }

    /// <summary>幸運の硬貨: 同じ目が 2 個以上出たターン、 ゴールド +3 (1 戦闘最大 12G)。</summary>
    public class LuckyCoin : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.GoldLuckyCoin;
        public const int Gold = 3, MaxPerBattle = 12;
        /// <summary>[persistent] この戦闘で得た額。</summary>
        private const string Key = "gold.luckyCoin";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var d = ctx.playerDice;
            int got = Rw.Count(ctx, Key);
            if (d == null || got >= MaxPerBattle) return;
            for (int i = 0; i < d.Length; i++)
                for (int j = i + 1; j < d.Length; j++)
                    if (d[i] == d[j])
                    {
                        int n = System.Math.Min(Gold, MaxPerBattle - got);
                        ctx.accumulatedValues[Key] = got + n;
                        Rw.Gold(n, SkillId);
                        return;
                    }
        }
    }

    /// <summary>爆破祭の小切手: 3 ターン以内に戦闘に勝ったとき、 ゴールド +12。</summary>
    public class BlastPrize : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.GoldBlastPrize;
        public const int Turns = 3, Gold = 12;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Won(ctx) && ctx.currentTurn <= Turns) Rw.Gold(Gold, SkillId);
        }
    }

    /// <summary>研ぎ代の領収書: 店または休憩で山札から端子を 1 枚抜くたび、 このラン中の全攻撃ヒットの基礎攻撃 +2 (累積・RunState.receiptRemovals・GameManager.NoteReceiptRemoval)。
    /// 攻撃 +N (ctx.mutualAttackBonus) は撃ごとに乗る。</summary>
    public class MacroReceipt : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.MacroReceipt;
        public const int PerRemoval = 2;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(PerRemoval * (Rw.Run?.receiptRemovals ?? 0));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Rw.Run?.receiptRemovals ?? 0;
            if (n > 0) ctx.AddPlayerAttackOrDiceBonus(PerRemoval * n);
        }
    }

    /// <summary>相場師の算盤: 店でリロール (軸指定を含む) を 1 回するたび 1 回分 (最大 <see cref="MaxStacks"/>・RunState.abacusStacks・ShopManager.TryReroll)。
    /// 戦闘開始時 (1 ターン目) のブロック +2・最初の 1 撃の基礎攻撃 +1 (どちらも × 回分)。</summary>
    public class MacroAbacus : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.MacroAbacus;
        public const int BlockPer = 2, AtkPer = 1, MaxStacks = 10;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Run?.abacusStacks ?? 0);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Rw.Run?.abacusStacks ?? 0;
            if (n <= 0 || ctx.currentTurn != 1) return;
            ctx.itemBlockBonus += BlockPer * n;
            ctx.firstHitAttackBonus += AtkPer * n;
            Rw.Log($"[相場師の算盤] {n} 回分: ブロック +{BlockPer * n}・最初の 1 撃 +{AtkPer * n}");
        }
    }

    /// <summary>行商人の見本箱: 店の戦術消耗品の値段 −50% (<see cref="PriceMul"/>・ShopManager が読む)。 戦闘の始めに消耗品を 1 つも持っていなければ、
    /// BRONZE か SILVER の戦術消耗品を 1 つ借りる (その戦闘の間だけ ── 使わなければ戦闘の終わりに返す・乱数キー "link.sampleBox")。</summary>
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
            Rw.Log($"[行商人の見本箱] 消耗品が無い → 〈{id}〉をこの戦闘の間だけ借りる");
        }
    }
}

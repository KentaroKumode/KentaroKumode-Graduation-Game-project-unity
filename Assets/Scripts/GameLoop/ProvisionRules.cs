namespace GameLoop
{
    /// <summary>物資の段階。 下に行くほどデバフが累積する (効果の中身は旧・希望の段階と同じ)。
    /// §10-1 の意味づけは「枯渇し始める → 払底」。</summary>
    public enum ProvisionTier
    {
        Ample = 0,      // 751〜   : 充足（デバフ無し）
        Dwindling = 1,  // 451〜750: 目減り（疲労）
        Depleting = 2,  // 201〜450: 枯渇し始める（＋苦悩）
        Scarce = 3,     // 1〜200  : 枯渇寸前（＋迷妄）
        Exhausted = 4,  // 0       : 払底（移動に HP を払う）
    }

    /// <summary>
    /// 物資の規則のうち、 UnityEngine にも RunState にも依存しない部分 (素の C# で単体テストする)。
    /// 状態を動かすのは <see cref="ProvisionSystem"/>。 正本: docs/GAME.md §10。
    ///
    /// <para>2026-09-28: 希望 → 物資。 固定値はすべて ×10。 <b>ラチェット (上限の一方向ロック) と
    /// 発狂 (0 で 5 移動後にラン終了) は撤廃</b> ── 物資は燃料のように補給できる資源で、
    /// 0 の間は移動に HP を払う (<c>MapSystem.FreeMove.FreeMapSim</c>)。</para>
    /// </summary>
    public static class ProvisionRules
    {
        public const int Max = 1000;

        // 段: この値「以下」で各段に入る
        public const int FloorDwindling = 750;
        public const int FloorDepleting = 450;
        public const int FloorScarce    = 200;

        // 戦闘の HP 収支マイナス 1 戦あたりの損 (段ごと定量)
        public const int LossAmple     = 40;
        public const int LossDwindling = 50;
        public const int LossDepleting = 70;
        public const int LossScarce    = 100;

        public const int EvilChoiceCost = 100;   // 悪選択
        public const int RerollCost     = 30;    // ダイス振り直し 1 回
        public const int DespairMarch   = 10;    // 絶望的な進軍: 点に着くたびの追加損

        public static ProvisionTier TierOf(int provision)
        {
            if (provision <= 0) return ProvisionTier.Exhausted;
            if (provision <= FloorScarce) return ProvisionTier.Scarce;
            if (provision <= FloorDepleting) return ProvisionTier.Depleting;
            if (provision <= FloorDwindling) return ProvisionTier.Dwindling;
            return ProvisionTier.Ample;
        }

        /// <summary>HP 収支マイナス 1 戦あたりの損。 払底では 0 (既に底)。</summary>
        public static int CombatLoss(ProvisionTier tier)
        {
            switch (tier)
            {
                case ProvisionTier.Ample:     return LossAmple;
                case ProvisionTier.Dwindling: return LossDwindling;
                case ProvisionTier.Depleting: return LossDepleting;
                case ProvisionTier.Scarce:    return LossScarce;
                default:                      return 0;
            }
        }

        /// <summary>逃げるの代償: 物資 −100 (0 で止まる) ＋ ゴールド半減 (切り捨て)。 報酬は無い。</summary>
        public static (int provision, int gold) ApplyFlee(int provision, int gold)
        {
            int p = provision - MapSystem.FreeMove.FreeMapParams.FleeProvision;
            return (p < 0 ? 0 : p, gold / 2);
        }
    }
}

namespace GameLoop
{
    /// <summary>物資の状態。 2026-11-09 に段 (充足・目減り・枯渇し始める・枯渇寸前) とそのデバフを撤去し、
    /// 「ある」か「払底 (0)」かの 2 つだけになった。 数値は旧の段の番号を残している (保存値・計測の互換)。</summary>
    public enum ProvisionTier
    {
        Ample = 0,      // 1 以上
        Exhausted = 4,  // 0: 払底 (移動に HP を払う)
    }

    /// <summary>
    /// 物資の規則のうち、 UnityEngine にも RunState にも依存しない部分 (素の C# で単体テストする)。
    /// 状態を動かすのは <see cref="ProvisionSystem"/>。 正本: docs/GAME.md §10。
    ///
    /// <para><b>2026-11-09: デノミ (1 歩 = 1)。</b> 旧単位 (1 歩 50・上限 1000) の 1/50。
    /// 同時に、 段とそのデバフ (疲労・苦悩・迷妄)・戦闘の物資の損・振り直しの物資コストを撤去した。
    /// 物資は「移動の燃料。 尽きたら HP で払う」だけ。</para>
    /// </summary>
    public static class ProvisionRules
    {
        /// <summary>素の上限 (固定・整備パネルで伸ばす口は無い)。 2026-11-10: 20 → 5 → 8 → 10。
        /// 実際の上限 9 / 11 / 13 で Super Balanced 14.0 / 24.5 / 34.0% (〈兵站〉r3 込み・HP で歩く BOT)。 〈兵站〉の撤去で素の値へ移した。</summary>
        public const int Max = 10;

        /// <summary>悪選択 (強盗など) の物資コスト。</summary>
        public const int EvilChoiceCost = 2;

        public static ProvisionTier TierOf(int provision)
            => provision <= 0 ? ProvisionTier.Exhausted : ProvisionTier.Ample;

        /// <summary>逃げるの代償: 物資 −2 (0 で止まる) ＋ ゴールド半減 (切り捨て)。 報酬は無い。</summary>
        public static (int provision, int gold) ApplyFlee(int provision, int gold)
        {
            int p = provision - MapSystem.FreeMove.FreeMapParams.FleeProvision;
            return (p < 0 ? 0 : p, gold / 2);
        }

        /// <summary>旧単位 (1 歩 50 の頃より前の「1 歩 10」の頃に作ったイベントの「物資±N」) を今の単位へ読み替える。
        /// N/50 を四捨五入し、 0 になるものは ±1。 イベントの再編 (issue #17) で文面の値を書き直したら外す。</summary>
        public static int FromLegacyEventAmount(int legacy)
        {
            if (legacy == 0) return 0;
            int a = (int)System.Math.Round(System.Math.Abs(legacy) / 50.0, System.MidpointRounding.AwayFromZero);
            if (a < 1) a = 1;
            return legacy < 0 ? -a : a;
        }
    }
}

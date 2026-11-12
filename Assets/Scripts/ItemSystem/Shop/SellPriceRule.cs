namespace ItemSystem.Shop
{
    /// <summary>
    /// 売値の決まり方 (2026-11-13)。 店の売却・売る画面・BOT・下取り・デバッグ表示は全部
    /// <see cref="ShopManager.SellRange"/> を通り、 その中身がこの規則。 UnityEngine に依存しない
    /// (Tools/event_branch_check がコンパイルして検算する)。
    /// <list type="bullet">
    /// <item>items.json の <c>noSellValue: true</c> の品は 0G (売ること自体はできる・BOT は売らない)。</item>
    /// <item>それ以外は items.json の <c>basePrice</c> から ItemDatabase が作った幅 (×0.50〜×0.75)。 下限は 1G
    /// (<c>basePrice</c> を持たない品も 1G ── 0G は <c>noSellValue</c> でだけ表す)。</item>
    /// </list>
    /// </summary>
    public static class SellPriceRule
    {
        /// <summary>カタログに無い id の売値。</summary>
        public const int UnknownItem = 3;

        /// <summary>売値の幅。 <paramref name="listedMin"/>/<paramref name="listedMax"/> は品の sellPrice (basePrice から作った幅)。</summary>
        public static void Range(bool noSellValue, int listedMin, int listedMax, out int min, out int max)
        {
            if (noSellValue) { min = 0; max = 0; return; }
            min = System.Math.Max(1, listedMin);
            max = System.Math.Max(min, listedMax);
        }
    }
}

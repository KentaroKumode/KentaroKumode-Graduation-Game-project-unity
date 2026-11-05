using System.Collections.Generic;

namespace GameLoop
{
    /// <summary>
    /// 戦闘の外で HP が減った量を、 出どころ × 層 (1〜5) で数える診断 (2026-11-05)。
    ///
    /// <para><b>なぜ要るか。</b> 層 1 で、 戦闘と戦闘の間に HP が減る間隔が 1 ランあたり 1.1 回・1 回あたり最大 HP の 25% あった。
    /// 通常戦 3 回分の消耗なのに、 物資切れの歩行・雹・層デバフ・イベントのどれかが分からない。</para>
    ///
    /// <para>計測専用。 ゲームの結果は変えない。</para>
    /// </summary>
    public static class HpOutsideDiag
    {
        /// <summary>"L{層}:{出どころ}" → 失った HP の累計 / 回数。</summary>
        public static readonly Dictionary<string, long> Amount = new Dictionary<string, long>();
        public static readonly Dictionary<string, long> Hits = new Dictionary<string, long>();

        public static void Reset() { Amount.Clear(); Hits.Clear(); }

        public static void Add(string source, int hpLost)
        {
            if (hpLost <= 0 || string.IsNullOrEmpty(source)) return;
            int depth = GameManager.Instance?.Run?.Depth ?? 1;
            string key = "L" + MapSystem.FreeMove.FloorPlan.OrdinalOf(depth) + ":" + source;
            Amount.TryGetValue(key, out long a); Amount[key] = a + hpLost;
            Hits.TryGetValue(key, out long h); Hits[key] = h + 1;
        }
    }
}

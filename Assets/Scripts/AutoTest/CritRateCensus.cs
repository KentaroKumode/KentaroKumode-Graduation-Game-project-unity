using System;
using System.Text;

namespace AutoTest
{
    /// <summary><b>会心率の分布を採る計装 (2026-09-10)。</b>
    ///
    /// <para><b>なぜ平均では足りないか。</b> 精密トラックを「会心率」から「会心倍率」へ
    /// 移す設計判断は、 <b>会心率がビルド間でどれだけばらつくか</b>に依存する。
    /// 倍率の価値は実効会心率に比例するので、 全ビルドが 40% 付近に固まっているなら
    /// 「会心ビルドなら精密が強い」という条件依存は成立せず、 単に出力の弱い版になる。
    /// 実測の平均は 41.8% だが、 <b>平均からは分位が分からない</b>
    /// (`project-mirror-twins-threshold` で同じ罠を踏んでいる ── 平均から閾値を置いて
    /// 発火率が 0% と 87% に跳ねた)。</para>
    ///
    /// <para><b>加算合計と実効率の両方を採り続ける。</b> 逓減を撤去した (2026-09-15) ので
    /// 両者は 100% 未満では一致するが、 <b>100% 到達でクランプが効き始める</b>ため
    /// 差がゼロでなくなる境目が見える。 会心を積み切ったビルドが何割いるかの指標になる。</para>
    ///
    /// <para>static はドメインリロードで消えるので、 バッチ開始時に <see cref="Reset"/> を
    /// 呼ぶこと。 集計は 1 ターンの会心判定ごとに 1 サンプル。</para></summary>
    public static class CritRateCensus
    {
        /// <summary>0〜300% を 5% 刻みで 60 段 + 溢れ。 加算合計 (クランプ前)。</summary>
        private const int Buckets = 61;
        private const float BucketWidth = 0.05f;

        private static readonly long[] _add = new long[Buckets];
        private static readonly long[] _eff = new long[Buckets];
        public static long Samples { get; private set; }
        private static double _addSum, _effSum;

        public static void Reset()
        {
            Array.Clear(_add, 0, Buckets);
            Array.Clear(_eff, 0, Buckets);
            Samples = 0; _addSum = 0; _effSum = 0;
        }

        public static void Note(float critAdd, float critRate)
        {
            if (critAdd < 0f) critAdd = 0f;
            if (critRate < 0f) critRate = 0f;
            int ia = Math.Min(Buckets - 1, (int)(critAdd / BucketWidth));
            int ie = Math.Min(Buckets - 1, (int)(critRate / BucketWidth));
            _add[ia]++; _eff[ie]++;
            _addSum += critAdd; _effSum += critRate;
            Samples++;
        }

        private static float Percentile(long[] h, double q)
        {
            if (Samples <= 0) return float.NaN;
            long target = (long)Math.Ceiling(q * Samples);
            long acc = 0;
            for (int i = 0; i < Buckets; i++)
            {
                acc += h[i];
                if (acc >= target) return (i + 0.5f) * BucketWidth;
            }
            return (Buckets - 0.5f) * BucketWidth;
        }

        /// <summary>分位を 1 行で。 <b>設計値を置く前にこれを読むこと</b> ──
        /// 平均だけを見て閾値や係数を決めると、 分布の形で結論がひっくり返る。</summary>
        public static string Describe()
        {
            if (Samples <= 0) return "【会心率分布】 サンプルなし";
            var sb = new StringBuilder();
            sb.AppendLine($"【会心率分布】 判定 {Samples:N0} 回 / 加算合計 平均 {_addSum / Samples * 100:F1}%"
                        + $" / 実効 平均 {_effSum / Samples * 100:F1}%");
            sb.AppendLine("           P10    P25    P50    P75    P90    P99");
            sb.Append("  加算合計 ");
            foreach (var q in new[] { 0.10, 0.25, 0.50, 0.75, 0.90, 0.99 })
                sb.Append($"{Percentile(_add, q) * 100,6:F1}%");
            sb.AppendLine();
            sb.Append("  実効率   ");
            foreach (var q in new[] { 0.10, 0.25, 0.50, 0.75, 0.90, 0.99 })
                sb.Append($"{Percentile(_eff, q) * 100,6:F1}%");
            sb.AppendLine();
            // 100% 天井で捨てられている割合 = (加算 − 実効) / 加算。
            // 逓減の撤去 (2026-09-15) でここは「積み過ぎ」だけを表す。 0% なら誰も天井に当たっていない。
            double lost = _addSum > 0 ? (_addSum - _effSum) / _addSum : 0;
            sb.AppendLine($"  100% 天井で捨てられた割合: {lost * 100:F1}%"
                        + "  ← 0% なら天井に当たっているビルドが居ない");
            // 会心率のばらつき。 会心を軸にしたビルドが他と区別できているかの指標。
            float p10 = Percentile(_eff, 0.10), p90 = Percentile(_eff, 0.90);
            sb.AppendLine($"  実効会心率の開き (P90/P10): {(p10 > 0 ? p90 / p10 : 0):F2} 倍"
                        + "  ← 1.0 に近いほど会心ビルドが他と区別できていない");
            return sb.ToString();
        }
    }
}

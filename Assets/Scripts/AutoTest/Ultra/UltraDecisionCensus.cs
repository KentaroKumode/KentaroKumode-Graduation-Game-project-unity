using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AutoTest.Ultra
{
    /// <summary>How many macro decisions a run actually contains — and how many of them are
    /// decisions at all.
    ///
    /// <para><b>Why this exists.</b> Every cost extrapolation so far has multiplied the measured
    /// cost of one decision by "30 macro decisions per run". That 30 is not a measurement; it is
    /// a constant typed into the cost probe. The whole plan for cutting rollout cost rests on
    /// which decision points to skip, so the first thing to establish is how many there are and
    /// what they look like.</para>
    ///
    /// <para>The interesting column is <c>手=1</c>: a decision point with a single legal action
    /// cannot be influenced by any amount of evaluation, so those are free to skip — not as a
    /// heuristic, but by definition. If they turn out to be a large share, the cheapest cut is
    /// already paid for.</para>
    ///
    /// <para>Off by default and counting only. It never changes a decision.</para></summary>
    public static class UltraDecisionCensus
    {
        public static bool Enabled;

        private sealed class Bucket
        {
            public int decisions;
            public long legalActionsTotal;
            public int single;      // 手=1: 選びようがない
            public int two;
            public int threePlus;
            public int maxLegal;
        }

        private static readonly Dictionary<string, Bucket> _byPoint =
            new Dictionary<string, Bucket>(StringComparer.Ordinal);
        private static readonly Dictionary<int, Bucket> _byFloor = new Dictionary<int, Bucket>();
        private static int _runs;

        public static int Runs { get { return _runs; } }

        public static void Reset()
        {
            _byPoint.Clear();
            _byFloor.Clear();
            _runs = 0;
        }

        public static void NoteRunCompleted()
        {
            if (Enabled) _runs++;
        }

        public static void Record(UltraDecisionPoint point, int floor, int legalActions)
        {
            if (!Enabled) return;
            Fill(GetOrAdd(_byPoint, point.ToString()), legalActions);
            Fill(GetOrAdd(_byFloor, floor), legalActions);
        }

        private static void Fill(Bucket b, int legalActions)
        {
            b.decisions++;
            b.legalActionsTotal += legalActions;
            if (legalActions <= 1) b.single++;
            else if (legalActions == 2) b.two++;
            else b.threePlus++;
            if (legalActions > b.maxLegal) b.maxLegal = legalActions;
        }

        private static Bucket GetOrAdd<TKey>(Dictionary<TKey, Bucket> map, TKey key)
        {
            if (!map.TryGetValue(key, out Bucket b)) { b = new Bucket(); map[key] = b; }
            return b;
        }

        /// <summary>One block for the batch summary. The per-run figures are what the cost
        /// extrapolation needs, so they lead.</summary>
        public static string Report()
        {
            var sb = new StringBuilder();
            int runs = Math.Max(1, _runs);
            int total = 0, single = 0;
            foreach (var kv in _byPoint) { total += kv.Value.decisions; single += kv.Value.single; }

            sb.Append("[Ultra決定 国勢調査] ラン ").Append(_runs.ToString(CultureInfo.InvariantCulture))
              .Append(" / マクロ決定 ").Append(total.ToString(CultureInfo.InvariantCulture))
              .Append(" (1ラン平均 ")
              .Append((total / (double)runs).ToString("F1", CultureInfo.InvariantCulture))
              .Append(")\n");
            if (total > 0)
                sb.Append("  うち 手=1 で rollout 不要: ")
                  .Append(single.ToString(CultureInfo.InvariantCulture))
                  .Append(" (").Append((100.0 * single / total).ToString("F1", CultureInfo.InvariantCulture))
                  .Append("%) → 実質 ")
                  .Append(((total - single) / (double)runs).ToString("F1", CultureInfo.InvariantCulture))
                  .Append(" 決定/ラン\n");

            sb.Append("  決定点別:\n");
            AppendTable(sb, _byPoint, runs);
            sb.Append("  層別:\n");
            var floors = new List<int>(_byFloor.Keys);
            floors.Sort();
            var ordered = new List<KeyValuePair<string, Bucket>>(floors.Count);
            for (int i = 0; i < floors.Count; i++)
                ordered.Add(new KeyValuePair<string, Bucket>(
                    floors[i].ToString(CultureInfo.InvariantCulture) + "層", _byFloor[floors[i]]));
            AppendRows(sb, ordered, runs);
            return sb.ToString();
        }

        private static void AppendTable(
            StringBuilder sb, Dictionary<string, Bucket> map, int runs)
        {
            var rows = new List<KeyValuePair<string, Bucket>>(map);
            rows.Sort((x, y) => y.Value.decisions.CompareTo(x.Value.decisions));
            AppendRows(sb, rows, runs);
        }

        private static void AppendRows(
            StringBuilder sb, List<KeyValuePair<string, Bucket>> rows, int runs)
        {
            sb.Append("    ").Append("名前".PadRight(20))
              .Append("決定/ラン".PadLeft(10)).Append("手=1".PadLeft(8))
              .Append("手=2".PadLeft(8)).Append("手≥3".PadLeft(8))
              .Append("平均手数".PadLeft(10)).Append("最大".PadLeft(6)).Append('\n');
            for (int i = 0; i < rows.Count; i++)
            {
                Bucket b = rows[i].Value;
                sb.Append("    ").Append(rows[i].Key.PadRight(20))
                  .Append((b.decisions / (double)runs).ToString("F2", CultureInfo.InvariantCulture).PadLeft(10))
                  .Append(b.single.ToString(CultureInfo.InvariantCulture).PadLeft(8))
                  .Append(b.two.ToString(CultureInfo.InvariantCulture).PadLeft(8))
                  .Append(b.threePlus.ToString(CultureInfo.InvariantCulture).PadLeft(8))
                  .Append((b.legalActionsTotal / (double)Math.Max(1, b.decisions))
                          .ToString("F2", CultureInfo.InvariantCulture).PadLeft(10))
                  .Append(b.maxLegal.ToString(CultureInfo.InvariantCulture).PadLeft(6))
                  .Append('\n');
            }
        }
    }
}

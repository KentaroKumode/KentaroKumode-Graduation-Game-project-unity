using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AutoTest.Ultra
{
    /// <summary>Accumulates named wall-clock spans so a rollout worker can report where its
    /// seven seconds went.
    ///
    /// <para><b>Why this exists.</b> A normal batch amortises every per-process initialisation
    /// over 1000 runs; a rollout amortises it over one. The same startup code is therefore
    /// roughly a thousand times heavier, in relative terms, inside a worker than inside a batch
    /// — which is exactly why it went unnoticed. Before cutting anything, the split has to be
    /// measured: guessing which half dominates is the mistake this stack has already made.</para>
    ///
    /// <para><b>Diagnostic only, and off by default.</b> Nothing reads these numbers to make a
    /// decision, and <see cref="Enabled"/> is false unless a worker turns it on, so a normal
    /// batch runs byte-identically with this compiled in. <see cref="Begin"/> returns 0 when
    /// disabled and <see cref="End"/> ignores a 0 handle, so instrumenting a method costs one
    /// branch.</para>
    ///
    /// <para>Spans may nest (a regression inside a reload). The report prints them in
    /// first-seen order and never subtracts, so an inner span is counted in its outer one as
    /// well — name inner spans with a leading marker and read the total accordingly.</para></summary>
    public static class UltraPhaseClock
    {
        /// <summary>Set by the worker bootstrap. False everywhere else.</summary>
        public static bool Enabled;

        private static readonly List<string> _order = new List<string>();
        private static readonly Dictionary<string, double> _millis =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> _calls =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Start a span. Returns 0 when disabled; pass the result to <see cref="End"/>.</summary>
        public static long Begin()
        {
            return Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        public static void End(string name, long begin)
        {
            if (!Enabled || begin == 0L || string.IsNullOrEmpty(name)) return;
            double ms = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;
            Add(name, ms);
        }

        /// <summary>Record a span measured elsewhere (e.g. engine startup, which is over before
        /// any of our code runs and can only be read off <c>Time.realtimeSinceStartup</c>).</summary>
        public static void Add(string name, double milliseconds)
        {
            if (!Enabled || string.IsNullOrEmpty(name)) return;
            if (!_millis.ContainsKey(name))
            {
                _order.Add(name);
                _millis[name] = 0.0;
                _calls[name] = 0;
            }
            _millis[name] += milliseconds;
            _calls[name] += 1;
        }

        public static void Reset()
        {
            _order.Clear();
            _millis.Clear();
            _calls.Clear();
        }

        public static double MillisOf(string name)
        {
            return _millis.TryGetValue(name, out double ms) ? ms : 0.0;
        }

        public static int CallsOf(string name)
        {
            return _calls.TryGetValue(name, out int n) ? n : 0;
        }

        /// <summary>One block, ready to paste into a handoff. <paramref name="totalMillis"/> is
        /// the whole process wall time, supplied by the caller — the clock cannot know it,
        /// because the interesting part starts before the first span.</summary>
        public static string Report(double totalMillis)
        {
            var sb = new StringBuilder();
            sb.Append("[UltraPhase] 合計 ")
              .Append(totalMillis.ToString("F0", CultureInfo.InvariantCulture)).Append("ms");
            double accounted = 0.0;
            for (int i = 0; i < _order.Count; i++)
            {
                string name = _order[i];
                double ms = _millis[name];
                bool nested = name.Length > 0 && name[0] == '└';   // └ = inner span
                if (!nested) accounted += ms;
                sb.Append('\n')
                  .Append("  ").Append(name.PadRight(22))
                  .Append(ms.ToString("F0", CultureInfo.InvariantCulture).PadLeft(7)).Append("ms")
                  .Append((totalMillis > 0.0 ? 100.0 * ms / totalMillis : 0.0)
                          .ToString("F1", CultureInfo.InvariantCulture).PadLeft(7)).Append('%')
                  .Append("  ×").Append(_calls[name].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append('\n').Append("  ").Append("(未計上)".PadRight(22))
              .Append(Math.Max(0.0, totalMillis - accounted)
                      .ToString("F0", CultureInfo.InvariantCulture).PadLeft(7)).Append("ms");
            sb.Append('\n').Append("  ※ └ で始まる行は直前の外側スパンの内数 (合計には二重計上しない)");
            return sb.ToString();
        }
    }
}

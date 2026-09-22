using System;
using System.Diagnostics;
using System.Threading;

namespace AutoTest.Ultra
{
    /// <summary>Why a bounded search stopped.</summary>
    public enum UltraStopReason
    {
        Running = 0,
        Completed,
        EvaluationCap,
        MemoryCap,
        TimeCap,
        Cancelled,
    }

    /// <summary>Hard bounds for one combat decision, plus cancellation.
    ///
    /// <para><b>Built before the search it bounds, on purpose</b> (handoff §10 Phase E:
    /// 「固定work/memory cap、cancel、fallbackを先に実装」). The previous attempt at a deep
    /// combat search froze the editor twice because the cap was expressed in a unit that did
    /// not match the work actually being done — the budget counted expanded states while the
    /// cost lived in the leaves. Here the unit is the evaluation itself, so
    /// <c>cap × cost-per-evaluation</c> is a real upper bound on time.</para>
    ///
    /// <para><b>The memory cap counts entries, not bytes.</b> A byte cap would need
    /// <c>GC.GetTotalMemory</c> on a hot path, which is both expensive and inexact — in other
    /// words a cap that does not hold. Bounding the containers that actually grow (memo tables,
    /// frontiers) is enforceable at no cost.</para></summary>
    public sealed class UltraCombatBudget
    {
        /// <summary>Maximum leaf evaluations. The unit is whatever the search calls
        /// <see cref="TryConsumeEvaluation"/> for; it must be the dominant cost.</summary>
        public long MaxEvaluations = 200_000;
        /// <summary>Maximum live entries across memo tables.</summary>
        public int MaxMemoEntries = 200_000;
        /// <summary>Wall-clock ceiling. The backstop for a cost model that turns out wrong —
        /// and it has turned out wrong before.</summary>
        public int MaxWallClockMs = 2_000;

        private long _evaluations;
        private int _cancelled;
        private UltraStopReason _stop = UltraStopReason.Running;
        private readonly Stopwatch _clock = new Stopwatch();
        /// <summary>Checking the clock every evaluation costs more than the evaluation.</summary>
        private const int ClockCheckInterval = 512;

        public long Evaluations { get { return Interlocked.Read(ref _evaluations); } }
        public UltraStopReason StopReason { get { return _stop; } }
        public bool IsCancelled { get { return Volatile.Read(ref _cancelled) != 0; } }
        public long ElapsedMs { get { return _clock.ElapsedMilliseconds; } }

        public void Begin()
        {
            Interlocked.Exchange(ref _evaluations, 0);
            Volatile.Write(ref _cancelled, 0);
            _stop = UltraStopReason.Running;
            _clock.Reset();
            _clock.Start();
        }

        public void Complete()
        {
            _clock.Stop();
            if (_stop == UltraStopReason.Running) _stop = UltraStopReason.Completed;
        }

        /// <summary>Cancel from any thread. Safe to call repeatedly and before
        /// <see cref="Begin"/>.</summary>
        public void Cancel()
        {
            Volatile.Write(ref _cancelled, 1);
        }

        /// <summary>Charge one unit of work. Returns false once the search must stop.
        ///
        /// <para>Call this <b>before</b> doing the work, never after: charging afterwards lets
        /// the last evaluation run unbounded, which is exactly the case that hangs when a
        /// single evaluation is itself expensive.</para></summary>
        public bool TryConsumeEvaluation()
        {
            if (IsCancelled) { _stop = UltraStopReason.Cancelled; return false; }

            long used = Interlocked.Increment(ref _evaluations);
            if (used > MaxEvaluations) { _stop = UltraStopReason.EvaluationCap; return false; }

            if ((used % ClockCheckInterval) == 0
                && MaxWallClockMs > 0 && _clock.ElapsedMilliseconds > MaxWallClockMs)
            {
                _stop = UltraStopReason.TimeCap;
                return false;
            }
            return true;
        }

        /// <summary>Whether a memo table may grow to <paramref name="wouldBeCount"/> entries.</summary>
        public bool TryGrowMemo(int wouldBeCount)
        {
            if (wouldBeCount <= MaxMemoEntries) return true;
            _stop = UltraStopReason.MemoryCap;
            return false;
        }

        /// <summary>Whether the search may continue at all. Cheap enough for an outer loop.</summary>
        public bool ShouldContinue
        {
            get
            {
                if (IsCancelled) { _stop = UltraStopReason.Cancelled; return false; }
                return _stop == UltraStopReason.Running;
            }
        }

        /// <summary>A stop that is not <see cref="UltraStopReason.Completed"/> means the answer
        /// is partial. Callers use this to decide whether to trust it or fall back.</summary>
        public bool CompletedWithinBudget
        {
            get { return _stop == UltraStopReason.Completed; }
        }

        public string Describe()
        {
            return string.Format(
                "{0} / 評価 {1}/{2} / {3}ms (上限 {4}ms) / メモ上限 {5}",
                _stop, Evaluations, MaxEvaluations, ElapsedMs, MaxWallClockMs, MaxMemoEntries);
        }

        /// <summary>Bounds small enough to be safe on a 10,000-run batch.
        ///
        /// <para>Derived from the measured cost of one turn evaluation (~1.7 µs, see
        /// docs/design-exact-ai.md §12-1): 200,000 evaluations ≈ 0.34 s of single-core work,
        /// and the 2 s wall-clock cap covers the case where that measurement is wrong
        /// again.</para></summary>
        public static UltraCombatBudget Batch()
        {
            return new UltraCombatBudget
            {
                MaxEvaluations = 200_000,
                MaxMemoEntries = 200_000,
                MaxWallClockMs = 2_000,
            };
        }

        /// <summary>Bounds for a one-off diagnostic run where latency does not matter.</summary>
        public static UltraCombatBudget Diagnostic()
        {
            return new UltraCombatBudget
            {
                MaxEvaluations = 20_000_000,
                MaxMemoEntries = 4_000_000,
                MaxWallClockMs = 120_000,
            };
        }
    }

    /// <summary>Aggregate view of how often bounded combat searches actually finished.
    ///
    /// <para>A search that always hits its cap is not a search — it is whatever its fallback
    /// heuristic says, dressed up as one. That is only visible if the ratio is recorded.</para></summary>
    public static class UltraCombatBudgetStats
    {
        public static long Searches;
        public static long Completed;
        public static long EvaluationCapped;
        public static long MemoryCapped;
        public static long TimeCapped;
        public static long Cancelled;
        public static long TotalEvaluations;

        public static void Reset()
        {
            Searches = Completed = EvaluationCapped = MemoryCapped = 0;
            TimeCapped = Cancelled = TotalEvaluations = 0;
        }

        public static void Note(UltraCombatBudget budget)
        {
            if (budget == null) return;
            Searches++;
            TotalEvaluations += budget.Evaluations;
            switch (budget.StopReason)
            {
                case UltraStopReason.Completed: Completed++; break;
                case UltraStopReason.EvaluationCap: EvaluationCapped++; break;
                case UltraStopReason.MemoryCap: MemoryCapped++; break;
                case UltraStopReason.TimeCap: TimeCapped++; break;
                case UltraStopReason.Cancelled: Cancelled++; break;
            }
        }

        public static string Describe()
        {
            if (Searches <= 0) return "Ultra戦闘探索: 実行なし";
            return string.Format(
                "Ultra戦闘探索: {0}回 / 完了 {1} ({2:F1}%) / 評価上限 {3} / メモリ上限 {4}"
                + " / 時間上限 {5} / 中止 {6} / 平均評価 {7}",
                Searches, Completed, 100.0 * Completed / Searches,
                EvaluationCapped, MemoryCapped, TimeCapped, Cancelled,
                TotalEvaluations / Math.Max(1L, Searches));
        }
    }
}

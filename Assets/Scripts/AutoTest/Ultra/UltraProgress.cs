using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace AutoTest.Ultra
{
    public enum UltraProgressState
    {
        Idle = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Fallback = 4,
        Cancelled = 5,
    }

    public enum UltraProgressPhase
    {
        Idle = 0,
        Preparing = 1,
        Search = 2,
        Confirmation = 3,
        Executing = 4,
        Completed = 5,
        Failed = 6,
        Fallback = 7,
        Cancelled = 8,
    }

    public sealed class UltraProgressPlan
    {
        public string label = "Ultra";
        public long totalRuns = 1;
        public int workerCount;
    }

    public readonly struct UltraProgressSession
    {
        internal UltraProgressSession(long generation) { Generation = generation; }
        public long Generation { get; }
        public bool IsValid => Generation > 0;
    }

    /// <summary>One immutable, thread-safe-to-read view of Ultra progress.</summary>
    public sealed class UltraProgressSnapshot
    {
        private readonly string[] _recent;

        internal UltraProgressSnapshot(
            long generation,
            long revision,
            UltraProgressState state,
            UltraProgressPhase phase,
            string label,
            string detail,
            long completedRuns,
            long totalRuns,
            long phaseCompleted,
            long phaseTotal,
            int activeWorkers,
            int workerCount,
            long startedTicks,
            long lastProgressTicks,
            long capturedTicks,
            long tickFrequency,
            string[] recent)
        {
            Generation = generation;
            Revision = revision;
            State = state;
            Phase = phase;
            Label = label ?? string.Empty;
            Detail = detail ?? string.Empty;
            CompletedRuns = completedRuns;
            TotalRuns = totalRuns;
            PhaseCompleted = phaseCompleted;
            PhaseTotal = phaseTotal;
            ActiveWorkers = activeWorkers;
            WorkerCount = workerCount;
            StartedTicks = startedTicks;
            LastProgressTicks = lastProgressTicks;
            CapturedTicks = capturedTicks;
            TickFrequency = Math.Max(1L, tickFrequency);
            _recent = recent ?? Array.Empty<string>();
        }

        public long Generation { get; }
        public long Revision { get; }
        public UltraProgressState State { get; }
        public UltraProgressPhase Phase { get; }
        public string Label { get; }
        public string Detail { get; }
        public long CompletedRuns { get; }
        public long TotalRuns { get; }
        public long PhaseCompleted { get; }
        public long PhaseTotal { get; }
        public int ActiveWorkers { get; }
        public int WorkerCount { get; }
        public long StartedTicks { get; }
        public long LastProgressTicks { get; }
        public long CapturedTicks { get; }
        public long TickFrequency { get; }

        public bool IsActive => State != UltraProgressState.Idle;
        public double OverallPercent => UltraProgressFormat.Percent(CompletedRuns, TotalRuns);
        public double PhasePercent => UltraProgressFormat.Percent(PhaseCompleted, PhaseTotal);
        public string PercentText => UltraProgressFormat.PercentText(OverallPercent);
        public string Bar => UltraProgressFormat.Bar(OverallPercent);
        public double ElapsedSeconds => SecondsBetween(StartedTicks, CapturedTicks);
        public double LastProgressAgeSeconds => SecondsBetween(LastProgressTicks, CapturedTicks);
        public double EtaSeconds => CompletedRuns > 0 && TotalRuns > CompletedRuns
            ? ElapsedSeconds * (TotalRuns - CompletedRuns) / CompletedRuns
            : 0.0;
        public int RecentCount => _recent.Length;

        public string RecentAt(int index)
        {
            if ((uint)index >= (uint)_recent.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return _recent[index];
        }

        public string[] CopyRecent()
        {
            var copy = new string[_recent.Length];
            Array.Copy(_recent, copy, _recent.Length);
            return copy;
        }

        private double SecondsBetween(long from, long to)
        {
            if (from <= 0 || to <= from) return 0.0;
            return (to - from) / (double)TickFrequency;
        }
    }

    /// <summary>
    /// Unity-free progress aggregator. Worker/IPC threads may call this type, but must not
    /// call Unity APIs. A generation token rejects late results from a previous Ultra job.
    /// </summary>
    public sealed class UltraProgressTracker
    {
        public const int RecentCapacity = 20;

        private readonly object _gate = new object();
        private readonly Func<long> _clock;
        private readonly long _tickFrequency;
        private readonly List<string> _recent = new List<string>(RecentCapacity);

        private long _generation;
        private long _revision;
        private UltraProgressState _state;
        private UltraProgressPhase _phase;
        private string _label = string.Empty;
        private string _detail = string.Empty;
        private long _completedRuns;
        private long _totalRuns;
        private long _phaseCompleted;
        private long _phaseTotal;
        private int _activeWorkers;
        private int _workerCount;
        private long _startedTicks;
        private long _lastProgressTicks;

        public UltraProgressTracker(Func<long> monotonicClock = null, long tickFrequency = 0)
        {
            _clock = monotonicClock ?? Stopwatch.GetTimestamp;
            _tickFrequency = tickFrequency > 0 ? tickFrequency : Stopwatch.Frequency;
        }

        public UltraProgressSession Begin(UltraProgressPlan plan)
        {
            plan = plan ?? new UltraProgressPlan();
            lock (_gate)
            {
                _generation = _generation == long.MaxValue ? 1 : _generation + 1;
                _revision++;
                _state = UltraProgressState.Running;
                _phase = UltraProgressPhase.Preparing;
                _label = Clean(plan.label, 96, "Ultra");
                _detail = string.Empty;
                _completedRuns = 0;
                _totalRuns = Math.Max(1L, plan.totalRuns);
                _phaseCompleted = 0;
                _phaseTotal = 0;
                _activeWorkers = 0;
                _workerCount = Math.Max(0, plan.workerCount);
                _startedTicks = _clock();
                _lastProgressTicks = _startedTicks;
                _recent.Clear();
                AddRecentLocked("0.0% PREPARING " + _label);
                return new UltraProgressSession(_generation);
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _generation = _generation == long.MaxValue ? 1 : _generation + 1;
                _revision++;
                _state = UltraProgressState.Idle;
                _phase = UltraProgressPhase.Idle;
                _label = string.Empty;
                _detail = string.Empty;
                _completedRuns = 0;
                _totalRuns = 0;
                _phaseCompleted = 0;
                _phaseTotal = 0;
                _activeWorkers = 0;
                _workerCount = 0;
                _startedTicks = 0;
                _lastProgressTicks = 0;
                _recent.Clear();
            }
        }

        public void SetPhase(
            UltraProgressSession session,
            UltraProgressPhase phase,
            long phaseTotal,
            string detail = null)
        {
            lock (_gate)
            {
                if (!CanUpdateLocked(session)) return;
                long boundedTotal = Math.Max(0L, phaseTotal);
                string cleanDetail = Clean(detail, 256, string.Empty);
                bool resetPhase = _phase != phase || _phaseTotal != boundedTotal;
                bool changed = resetPhase || !string.Equals(_detail, cleanDetail, StringComparison.Ordinal);
                if (!changed) return;
                _phase = phase;
                _phaseTotal = boundedTotal;
                if (resetPhase) _phaseCompleted = 0;
                _detail = cleanDetail;
                TouchLocked();
                AddRecentLocked(FormatRecentLocked(cleanDetail));
            }
        }

        public void ReportPhaseCompleted(
            UltraProgressSession session,
            long completed,
            string detail = null)
        {
            lock (_gate)
            {
                if (!CanUpdateLocked(session)) return;
                long upper = _phaseTotal > 0 ? _phaseTotal : long.MaxValue;
                long bounded = Math.Max(_phaseCompleted, Math.Max(0L, Math.Min(completed, upper)));
                string cleanDetail = detail == null ? _detail : Clean(detail, 256, string.Empty);
                if (bounded == _phaseCompleted && string.Equals(cleanDetail, _detail, StringComparison.Ordinal)) return;
                _phaseCompleted = bounded;
                _detail = cleanDetail;
                TouchLocked();
            }
        }

        public void AddPhaseCompleted(
            UltraProgressSession session,
            long delta = 1,
            string detail = null)
        {
            if (delta <= 0) return;
            lock (_gate)
            {
                if (!CanUpdateLocked(session)) return;
                long remaining = _phaseTotal > 0
                    ? Math.Max(0L, _phaseTotal - _phaseCompleted)
                    : long.MaxValue - _phaseCompleted;
                long add = Math.Min(delta, remaining);
                string cleanDetail = detail == null ? _detail : Clean(detail, 256, string.Empty);
                if (add <= 0 && string.Equals(cleanDetail, _detail, StringComparison.Ordinal)) return;
                _phaseCompleted += add;
                _detail = cleanDetail;
                TouchLocked();
            }
        }

        public void ReportRunCompleted(
            UltraProgressSession session,
            long completedRuns,
            string summary = null)
        {
            lock (_gate)
            {
                if (!CanUpdateLocked(session)) return;
                long bounded = Math.Max(_completedRuns, Math.Max(0L, Math.Min(completedRuns, _totalRuns)));
                string cleanSummary = Clean(summary, 256, string.Empty);
                bool progressed = bounded != _completedRuns;
                bool detailChanged = !string.IsNullOrEmpty(cleanSummary)
                    && !string.Equals(cleanSummary, _detail, StringComparison.Ordinal);
                if (!progressed && !detailChanged) return;
                _completedRuns = bounded;
                if (!string.IsNullOrEmpty(cleanSummary)) _detail = cleanSummary;
                TouchLocked();
                if (progressed) AddRecentLocked(FormatRecentLocked(cleanSummary));
            }
        }

        public void AddRunCompleted(
            UltraProgressSession session,
            long delta = 1,
            string summary = null)
        {
            if (delta <= 0) return;
            lock (_gate)
            {
                if (!CanUpdateLocked(session)) return;
                long add = Math.Min(delta, Math.Max(0L, _totalRuns - _completedRuns));
                string cleanSummary = Clean(summary, 256, string.Empty);
                bool detailChanged = !string.IsNullOrEmpty(cleanSummary)
                    && !string.Equals(cleanSummary, _detail, StringComparison.Ordinal);
                if (add <= 0 && !detailChanged) return;
                _completedRuns += add;
                if (!string.IsNullOrEmpty(cleanSummary)) _detail = cleanSummary;
                TouchLocked();
                if (add > 0) AddRecentLocked(FormatRecentLocked(cleanSummary));
            }
        }

        public void ReportActiveWorkers(
            UltraProgressSession session,
            int activeWorkers,
            int workerCount = -1,
            string detail = null)
        {
            lock (_gate)
            {
                if (!CanUpdateLocked(session)) return;
                int newTotal = workerCount >= 0 ? Math.Max(0, workerCount) : _workerCount;
                int newActive = Math.Max(0, Math.Min(activeWorkers, newTotal));
                string cleanDetail = detail == null ? _detail : Clean(detail, 256, string.Empty);
                if (newTotal == _workerCount && newActive == _activeWorkers
                    && string.Equals(cleanDetail, _detail, StringComparison.Ordinal)) return;
                _workerCount = newTotal;
                _activeWorkers = newActive;
                _detail = cleanDetail;
                TouchLocked();
            }
        }

        public void Heartbeat(UltraProgressSession session, string detail = null)
        {
            lock (_gate)
            {
                if (!CanUpdateLocked(session)) return;
                if (detail != null) _detail = Clean(detail, 256, string.Empty);
                TouchLocked();
            }
        }

        public void Complete(UltraProgressSession session, string detail = null)
            => Finish(session, UltraProgressState.Completed, UltraProgressPhase.Completed, true, detail);

        public void Fail(UltraProgressSession session, string detail)
            => Finish(session, UltraProgressState.Failed, UltraProgressPhase.Failed, false, detail);

        public void Fallback(UltraProgressSession session, string detail)
            => Finish(session, UltraProgressState.Fallback, UltraProgressPhase.Fallback, false, detail);

        public void Cancel(UltraProgressSession session, string detail = null)
            => Finish(session, UltraProgressState.Cancelled, UltraProgressPhase.Cancelled, false, detail);

        public UltraProgressSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new UltraProgressSnapshot(
                    _generation,
                    _revision,
                    _state,
                    _phase,
                    _label,
                    _detail,
                    _completedRuns,
                    _totalRuns,
                    _phaseCompleted,
                    _phaseTotal,
                    _activeWorkers,
                    _workerCount,
                    _startedTicks,
                    _lastProgressTicks,
                    _clock(),
                    _tickFrequency,
                    _recent.ToArray());
            }
        }

        private void Finish(
            UltraProgressSession session,
            UltraProgressState state,
            UltraProgressPhase phase,
            bool forceComplete,
            string detail)
        {
            lock (_gate)
            {
                if (!CanUpdateLocked(session)) return;
                _state = state;
                _phase = phase;
                if (forceComplete)
                {
                    _completedRuns = _totalRuns;
                    if (_phaseTotal > 0) _phaseCompleted = _phaseTotal;
                }
                _activeWorkers = 0;
                _detail = Clean(detail, 256, state.ToString());
                TouchLocked();
                AddRecentLocked(FormatRecentLocked(_detail));
            }
        }

        private bool CanUpdateLocked(UltraProgressSession session)
        {
            return session.IsValid
                && session.Generation == _generation
                && _state == UltraProgressState.Running;
        }

        private void TouchLocked()
        {
            _revision++;
            _lastProgressTicks = _clock();
        }

        private string FormatRecentLocked(string text)
        {
            string pct = UltraProgressFormat.PercentText(
                UltraProgressFormat.Percent(_completedRuns, _totalRuns));
            string suffix = string.IsNullOrEmpty(text) ? string.Empty : "  " + text;
            return pct + "  " + _phase.ToString().ToUpperInvariant() + suffix;
        }

        private void AddRecentLocked(string line)
        {
            if (_recent.Count == RecentCapacity) _recent.RemoveAt(0);
            _recent.Add(Clean(line, 384, string.Empty));
        }

        private static string Clean(string value, int maxLength, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback ?? string.Empty;
            char[] chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (char.IsControl(chars[i])) chars[i] = ' ';
            string clean = new string(chars);
            return clean.Length <= maxLength ? clean : clean.Substring(0, maxLength);
        }
    }

    public static class UltraProgressHub
    {
        private static readonly UltraProgressTracker Shared = new UltraProgressTracker();

        public static UltraProgressSession Begin(UltraProgressPlan plan) => Shared.Begin(plan);
        public static void SetPhase(UltraProgressSession s, UltraProgressPhase p, long total, string detail = null)
            => Shared.SetPhase(s, p, total, detail);
        public static void ReportPhaseCompleted(UltraProgressSession s, long done, string detail = null)
            => Shared.ReportPhaseCompleted(s, done, detail);
        public static void AddPhaseCompleted(UltraProgressSession s, long delta = 1, string detail = null)
            => Shared.AddPhaseCompleted(s, delta, detail);
        public static void ReportRunCompleted(UltraProgressSession s, long done, string summary = null)
            => Shared.ReportRunCompleted(s, done, summary);
        public static void AddRunCompleted(UltraProgressSession s, long delta = 1, string summary = null)
            => Shared.AddRunCompleted(s, delta, summary);
        public static void ReportActiveWorkers(UltraProgressSession s, int active, int total = -1, string detail = null)
            => Shared.ReportActiveWorkers(s, active, total, detail);
        public static void Heartbeat(UltraProgressSession s, string detail = null) => Shared.Heartbeat(s, detail);
        public static void Complete(UltraProgressSession s, string detail = null) => Shared.Complete(s, detail);
        public static void Fail(UltraProgressSession s, string detail) => Shared.Fail(s, detail);
        public static void Fallback(UltraProgressSession s, string detail) => Shared.Fallback(s, detail);
        public static void Cancel(UltraProgressSession s, string detail = null) => Shared.Cancel(s, detail);
        public static UltraProgressSnapshot Snapshot() => Shared.Snapshot();
        public static void Reset() => Shared.Reset();
    }

    public static class UltraProgressFormat
    {
        public const int BarCells = 10;

        public static double Percent(long completed, long total)
        {
            if (total <= 0) return 0.0;
            double value = completed * 100.0 / total;
            if (double.IsNaN(value) || value <= 0.0) return 0.0;
            return value >= 100.0 ? 100.0 : value;
        }

        public static string PercentText(double percent)
        {
            double bounded = double.IsNaN(percent) || percent < 0.0
                ? 0.0
                : Math.Min(100.0, percent);
            return bounded.ToString("F1", CultureInfo.InvariantCulture) + "%";
        }

        public static string Bar(double percent)
        {
            double bounded = double.IsNaN(percent) || percent < 0.0
                ? 0.0
                : Math.Min(100.0, percent);
            int filled = bounded >= 100.0 ? BarCells : (int)Math.Floor(bounded / 10.0);
            return new string('\u25A0', filled) + new string('\u25A1', BarCells - filled);
        }

        public static string ConsoleLine(UltraProgressSnapshot snapshot)
        {
            if (snapshot == null) return "[UltraProgress] unavailable";
            string phase = snapshot.Phase.ToString().ToUpperInvariant();
            string phaseText = snapshot.PhaseTotal > 0
                ? "  phase=" + UltraProgressFormat.PercentText(snapshot.PhasePercent)
                    + " (" + snapshot.PhaseCompleted + "/" + snapshot.PhaseTotal + ")"
                : string.Empty;
            string workers = snapshot.WorkerCount > 0
                ? "  workers=" + snapshot.ActiveWorkers + "/" + snapshot.WorkerCount
                : string.Empty;
            string detail = string.IsNullOrEmpty(snapshot.Detail) ? string.Empty : "  " + snapshot.Detail;
            return "[UltraProgress] [" + snapshot.Bar + "] " + snapshot.PercentText
                + "  " + snapshot.State.ToString().ToUpperInvariant()
                + "  " + phase
                + "  runs=" + snapshot.CompletedRuns + "/" + snapshot.TotalRuns
                + phaseText + workers
                + "  last=" + snapshot.LastProgressAgeSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s"
                + detail;
        }
    }

    /// <summary>Pure, deterministic console-rate limiter. Call only from a main-thread presenter.</summary>
    public sealed class UltraConsoleThrottle
    {
        private readonly long _tickFrequency;
        private readonly long _minimumTicks;
        private readonly long _heartbeatTicks;
        private readonly double _overallStep;
        private readonly double _phaseStep;

        private bool _hasEmission;
        private long _lastGeneration;
        private long _lastRevision;
        private long _lastEmitTicks;
        private double _lastOverallPercent;
        private double _lastPhasePercent;
        private UltraProgressState _lastState;
        private UltraProgressPhase _lastPhase;

        public UltraConsoleThrottle(
            double minimumSeconds = 10.0,
            double overallPercentStep = 5.0,
            double phasePercentStep = 10.0,
            double heartbeatSeconds = 30.0,
            long tickFrequency = 0)
        {
            _tickFrequency = tickFrequency > 0 ? tickFrequency : Stopwatch.Frequency;
            _minimumTicks = SecondsToTicks(Math.Max(0.0, minimumSeconds));
            _heartbeatTicks = SecondsToTicks(Math.Max(minimumSeconds, heartbeatSeconds));
            _overallStep = Math.Max(0.0, overallPercentStep);
            _phaseStep = Math.Max(0.0, phasePercentStep);
        }

        public bool ShouldEmit(UltraProgressSnapshot snapshot, long nowTicks)
        {
            if (snapshot == null || snapshot.State == UltraProgressState.Idle) return false;

            bool newGeneration = !_hasEmission || snapshot.Generation != _lastGeneration;
            bool terminal = snapshot.State != UltraProgressState.Running;
            bool stateChanged = !_hasEmission || snapshot.State != _lastState;
            if (newGeneration || (terminal && (stateChanged || snapshot.Revision != _lastRevision)))
                return Mark(snapshot, nowTicks);

            long since = Math.Max(0L, nowTicks - _lastEmitTicks);
            if (since < _minimumTicks) return false;

            bool phaseChanged = snapshot.Phase != _lastPhase;
            bool overallAdvanced = snapshot.OverallPercent >= _lastOverallPercent + _overallStep;
            bool phaseAdvanced = snapshot.Phase == _lastPhase
                && snapshot.PhasePercent >= _lastPhasePercent + _phaseStep;
            bool heartbeat = since >= _heartbeatTicks;
            if (!phaseChanged && !overallAdvanced && !phaseAdvanced && !heartbeat) return false;
            return Mark(snapshot, nowTicks);
        }

        public void Reset()
        {
            _hasEmission = false;
            _lastGeneration = 0;
            _lastRevision = 0;
            _lastEmitTicks = 0;
            _lastOverallPercent = 0.0;
            _lastPhasePercent = 0.0;
            _lastState = UltraProgressState.Idle;
            _lastPhase = UltraProgressPhase.Idle;
        }

        private bool Mark(UltraProgressSnapshot snapshot, long nowTicks)
        {
            _hasEmission = true;
            _lastGeneration = snapshot.Generation;
            _lastRevision = snapshot.Revision;
            _lastEmitTicks = nowTicks;
            _lastOverallPercent = snapshot.OverallPercent;
            _lastPhasePercent = snapshot.PhasePercent;
            _lastState = snapshot.State;
            _lastPhase = snapshot.Phase;
            return true;
        }

        private long SecondsToTicks(double seconds)
        {
            if (seconds <= 0.0) return 0L;
            double ticks = seconds * _tickFrequency;
            return ticks >= long.MaxValue ? long.MaxValue : (long)Math.Ceiling(ticks);
        }
    }
}

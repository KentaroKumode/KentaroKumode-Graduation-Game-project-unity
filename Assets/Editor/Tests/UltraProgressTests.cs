#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AutoTest.Ultra;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    [TestFixture]
    public sealed class UltraProgressTests
    {
        [Test]
        public void Format_UsesTenCellsAndInvariantOneDecimalPercent()
        {
            Assert.That(UltraProgressFormat.Bar(0.0),
                Is.EqualTo(new string('\u25A1', 10)));
            Assert.That(UltraProgressFormat.Bar(49.9),
                Is.EqualTo(new string('\u25A0', 4) + new string('\u25A1', 6)));
            Assert.That(UltraProgressFormat.Bar(50.0),
                Is.EqualTo(new string('\u25A0', 5) + new string('\u25A1', 5)));
            Assert.That(UltraProgressFormat.Bar(100.0),
                Is.EqualTo(new string('\u25A0', 10)));
            Assert.That(UltraProgressFormat.Bar(double.NaN).Length, Is.EqualTo(10));

            Assert.That(UltraProgressFormat.PercentText(100.0 / 3.0), Is.EqualTo("33.3%"));
            Assert.That(UltraProgressFormat.PercentText(-1.0), Is.EqualTo("0.0%"));
            Assert.That(UltraProgressFormat.PercentText(101.0), Is.EqualTo("100.0%"));
        }

        [Test]
        public void ReportsAreMonotonicAndClampToDeclaredTotals()
        {
            var tracker = NewTracker(totalRuns: 10, out UltraProgressSession session);
            tracker.SetPhase(session, UltraProgressPhase.Search, 20, "search");

            tracker.ReportRunCompleted(session, 7, "run 7");
            tracker.ReportRunCompleted(session, 3, "late run 3");
            tracker.ReportPhaseCompleted(session, 12);
            tracker.ReportPhaseCompleted(session, 2);

            UltraProgressSnapshot monotonic = tracker.Snapshot();
            Assert.That(monotonic.CompletedRuns, Is.EqualTo(7));
            Assert.That(monotonic.PhaseCompleted, Is.EqualTo(12));

            tracker.ReportRunCompleted(session, long.MaxValue);
            tracker.ReportPhaseCompleted(session, long.MaxValue);
            UltraProgressSnapshot clamped = tracker.Snapshot();
            Assert.That(clamped.CompletedRuns, Is.EqualTo(10));
            Assert.That(clamped.PhaseCompleted, Is.EqualTo(20));
            Assert.That(clamped.OverallPercent, Is.EqualTo(100.0));
            Assert.That(clamped.PhasePercent, Is.EqualTo(100.0));
        }

        [Test]
        public void NewPhaseResetsOnlyPhaseProgress()
        {
            var tracker = NewTracker(totalRuns: 4, out UltraProgressSession session);
            tracker.SetPhase(session, UltraProgressPhase.Search, 10, "search");
            tracker.AddPhaseCompleted(session, 8);
            tracker.AddRunCompleted(session, 2, "run 2");

            tracker.SetPhase(session, UltraProgressPhase.Confirmation, 6, "confirm");
            UltraProgressSnapshot snapshot = tracker.Snapshot();

            Assert.That(snapshot.Phase, Is.EqualTo(UltraProgressPhase.Confirmation));
            Assert.That(snapshot.PhaseCompleted, Is.Zero);
            Assert.That(snapshot.PhaseTotal, Is.EqualTo(6));
            Assert.That(snapshot.CompletedRuns, Is.EqualTo(2));
            Assert.That(snapshot.OverallPercent, Is.EqualTo(50.0));
        }

        [Test]
        public void StaleSessionAndNonPositiveDeltasCannotMutateCurrentJob()
        {
            var tracker = new UltraProgressTracker(() => 100, 100);
            UltraProgressSession stale = tracker.Begin(new UltraProgressPlan { label = "old", totalRuns = 10 });
            UltraProgressSession current = tracker.Begin(new UltraProgressPlan { label = "new", totalRuns = 10 });
            long revision = tracker.Snapshot().Revision;

            tracker.AddRunCompleted(stale, 9, "stale");
            tracker.AddPhaseCompleted(stale, 9, "stale");
            tracker.AddRunCompleted(current, 0, "ignored");
            tracker.AddRunCompleted(current, -1, "ignored");
            tracker.AddPhaseCompleted(current, 0, "ignored");
            tracker.AddPhaseCompleted(current, -1, "ignored");

            UltraProgressSnapshot snapshot = tracker.Snapshot();
            Assert.That(snapshot.Generation, Is.EqualTo(current.Generation));
            Assert.That(snapshot.Label, Is.EqualTo("new"));
            Assert.That(snapshot.CompletedRuns, Is.Zero);
            Assert.That(snapshot.PhaseCompleted, Is.Zero);
            Assert.That(snapshot.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void ConcurrentUpdatesAndSnapshotsLoseNoProgressAndRemainConsistent()
        {
            const int Writers = 8;
            const int PerWriter = 10_000;
            const int Total = Writers * PerWriter;
            var tracker = NewTracker(totalRuns: Total, out UltraProgressSession session);
            tracker.SetPhase(session, UltraProgressPhase.Search, Total, "parallel");
            var failures = new ConcurrentQueue<string>();
            int stopReader = 0;

            Task reader = Task.Run(() =>
            {
                long lastPhase = 0;
                long lastRuns = 0;
                while (Volatile.Read(ref stopReader) == 0)
                {
                    UltraProgressSnapshot snapshot = tracker.Snapshot();
                    if (snapshot.PhaseCompleted < lastPhase)
                        failures.Enqueue("phase progress regressed");
                    if (snapshot.CompletedRuns < lastRuns)
                        failures.Enqueue("run progress regressed");
                    if (snapshot.PhaseCompleted < 0 || snapshot.PhaseCompleted > snapshot.PhaseTotal)
                        failures.Enqueue("phase snapshot was out of range");
                    if (snapshot.CompletedRuns < 0 || snapshot.CompletedRuns > snapshot.TotalRuns)
                        failures.Enqueue("run snapshot was out of range");
                    lastPhase = snapshot.PhaseCompleted;
                    lastRuns = snapshot.CompletedRuns;
                }
            });

            try
            {
                Parallel.For(0, Writers, _ =>
                {
                    for (int i = 0; i < PerWriter; i++)
                    {
                        tracker.AddPhaseCompleted(session);
                        tracker.AddRunCompleted(session);
                    }
                });
            }
            finally
            {
                Volatile.Write(ref stopReader, 1);
                Assert.That(reader.Wait(TimeSpan.FromSeconds(10)), Is.True, "snapshot reader did not stop");
            }

            UltraProgressSnapshot final = tracker.Snapshot();
            Assert.That(failures, Is.Empty);
            Assert.That(final.PhaseCompleted, Is.EqualTo(Total));
            Assert.That(final.CompletedRuns, Is.EqualTo(Total));
            Assert.That(final.PhasePercent, Is.EqualTo(100.0));
            Assert.That(final.OverallPercent, Is.EqualTo(100.0));
        }

        [Test]
        public void RecentHistoryKeepsLatestTwentyAndReturnsDefensiveCopies()
        {
            var tracker = NewTracker(totalRuns: 25, out UltraProgressSession session);
            for (int i = 1; i <= 25; i++)
                tracker.AddRunCompleted(session, 1, "item-" + i);

            UltraProgressSnapshot snapshot = tracker.Snapshot();
            Assert.That(snapshot.RecentCount, Is.EqualTo(UltraProgressTracker.RecentCapacity));
            StringAssert.Contains("item-6", snapshot.RecentAt(0));
            StringAssert.Contains("item-25", snapshot.RecentAt(snapshot.RecentCount - 1));

            string original = snapshot.RecentAt(0);
            string[] copy = snapshot.CopyRecent();
            copy[0] = "mutated";
            Assert.That(snapshot.RecentAt(0), Is.EqualTo(original));
        }

        [Test]
        public void CompleteFailAndFallbackPublishTerminalStateAndRejectLateProgress()
        {
            AssertTerminal(
                (tracker, session) => tracker.Complete(session, "done"),
                UltraProgressState.Completed,
                UltraProgressPhase.Completed,
                expectForcedCompletion: true);
            AssertTerminal(
                (tracker, session) => tracker.Fail(session, "worker failed"),
                UltraProgressState.Failed,
                UltraProgressPhase.Failed,
                expectForcedCompletion: false);
            AssertTerminal(
                (tracker, session) => tracker.Fallback(session, "using Super"),
                UltraProgressState.Fallback,
                UltraProgressPhase.Fallback,
                expectForcedCompletion: false);
        }

        [Test]
        public void ConsoleThrottleIsLowFrequencyButTerminalTransitionsAreImmediate()
        {
            long clock = 1_000;
            const long Frequency = 1_000;
            var tracker = new UltraProgressTracker(() => clock, Frequency);
            UltraProgressSession session = tracker.Begin(new UltraProgressPlan
            {
                label = "throttle",
                totalRuns = 100,
            });
            tracker.SetPhase(session, UltraProgressPhase.Search, 100, "search");
            var throttle = new UltraConsoleThrottle(
                minimumSeconds: 10.0,
                overallPercentStep: 5.0,
                phasePercentStep: 10.0,
                heartbeatSeconds: 30.0,
                tickFrequency: Frequency);

            Assert.That(throttle.ShouldEmit(tracker.Snapshot(), clock), Is.True, "first line must be visible");

            clock = 2_000;
            tracker.ReportRunCompleted(session, 10, "10 runs");
            Assert.That(throttle.ShouldEmit(tracker.Snapshot(), clock), Is.False, "progress spam bypassed minimum interval");

            clock = 11_000;
            Assert.That(throttle.ShouldEmit(tracker.Snapshot(), clock), Is.True, "meaningful progress was not emitted after interval");
            Assert.That(throttle.ShouldEmit(tracker.Snapshot(), clock), Is.False, "same revision emitted twice");

            clock = 12_000;
            tracker.SetPhase(session, UltraProgressPhase.Confirmation, 20, "confirm");
            Assert.That(throttle.ShouldEmit(tracker.Snapshot(), clock), Is.False, "phase change bypassed minimum interval");
            clock = 21_000;
            Assert.That(throttle.ShouldEmit(tracker.Snapshot(), clock), Is.True, "phase change was not emitted after interval");

            clock = 50_000;
            Assert.That(throttle.ShouldEmit(tracker.Snapshot(), clock), Is.False, "heartbeat fired before its interval");
            clock = 51_000;
            Assert.That(throttle.ShouldEmit(tracker.Snapshot(), clock), Is.True, "heartbeat did not fire");

            clock = 52_000;
            tracker.Fallback(session, "safe fallback");
            UltraProgressSnapshot terminal = tracker.Snapshot();
            Assert.That(throttle.ShouldEmit(terminal, clock), Is.True, "terminal state was throttled");
            Assert.That(throttle.ShouldEmit(terminal, clock), Is.False, "terminal revision emitted twice");
            StringAssert.Contains("FALLBACK", UltraProgressFormat.ConsoleLine(terminal));
            StringAssert.Contains("safe fallback", UltraProgressFormat.ConsoleLine(terminal));
        }

        private static UltraProgressTracker NewTracker(
            long totalRuns,
            out UltraProgressSession session)
        {
            var tracker = new UltraProgressTracker(() => 1_000, 1_000);
            session = tracker.Begin(new UltraProgressPlan
            {
                label = "test",
                totalRuns = totalRuns,
                workerCount = 8,
            });
            return tracker;
        }

        private static void AssertTerminal(
            Action<UltraProgressTracker, UltraProgressSession> finish,
            UltraProgressState expectedState,
            UltraProgressPhase expectedPhase,
            bool expectForcedCompletion)
        {
            var tracker = NewTracker(totalRuns: 5, out UltraProgressSession session);
            tracker.SetPhase(session, UltraProgressPhase.Search, 10, "search");
            tracker.ReportActiveWorkers(session, 4);
            tracker.AddRunCompleted(session, 2);
            tracker.AddPhaseCompleted(session, 3);
            finish(tracker, session);

            UltraProgressSnapshot terminal = tracker.Snapshot();
            Assert.That(terminal.State, Is.EqualTo(expectedState));
            Assert.That(terminal.Phase, Is.EqualTo(expectedPhase));
            Assert.That(terminal.ActiveWorkers, Is.Zero);
            Assert.That(terminal.CompletedRuns, Is.EqualTo(expectForcedCompletion ? 5 : 2));
            Assert.That(terminal.PhaseCompleted, Is.EqualTo(expectForcedCompletion ? 10 : 3));
            long revision = terminal.Revision;

            tracker.AddRunCompleted(session, 1, "late");
            tracker.AddPhaseCompleted(session, 1, "late");
            Assert.That(tracker.Snapshot().Revision, Is.EqualTo(revision), "late worker result mutated a terminal job");
        }
    }
}
#endif

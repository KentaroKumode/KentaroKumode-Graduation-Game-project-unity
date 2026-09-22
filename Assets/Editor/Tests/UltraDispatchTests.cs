#if UNITY_EDITOR
using System;
using AutoTest.Ultra;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    /// <summary>
    /// Phase D: what happens when a controller answers well, badly, or not at all.
    ///
    /// <para>Every case here must end with the run continuing. A controller is an optional
    /// improvement, never a dependency ── if any of these could stall or corrupt a run, the
    /// whole dispatch path would be unsafe to enable on a 10,000-run batch.</para>
    /// </summary>
    [TestFixture]
    public sealed class UltraDispatchTests
    {
        private sealed class Sink : IUltraDecisionSink
        {
            public Func<UltraCheckpoint, string> answer;
            public bool decline;
            public Exception throws;
            public int calls;

            public bool TryDecide(UltraCheckpoint checkpoint, out string committedActionId, out string reason)
            {
                calls++;
                committedActionId = null;
                reason = null;
                if (throws != null) throw throws;
                if (decline) { reason = "declined by test"; return false; }
                committedActionId = answer != null ? answer(checkpoint) : null;
                return true;
            }
        }

        private static UltraObservation Observation()
        {
            return new UltraObservation
            {
                map = new UltraMapView
                {
                    currentNodeId = "n0",
                    nodes = new[]
                    {
                        new UltraMapNodeView { id = "n0", row = 0, view = UltraTileView.Outpost, revealed = true },
                        new UltraMapNodeView { id = "n1", row = 1, view = UltraTileView.Shop, revealed = true, reachableNow = true },
                        new UltraMapNodeView { id = "n2", row = 1, view = UltraTileView.Unknown, revealed = false, reachableNow = true },
                        new UltraMapNodeView { id = "far", row = 3, view = UltraTileView.Boss, revealed = true },
                    },
                },
            };
        }

        private static UltraCheckpoint Checkpoint(int epoch = 4)
        {
            UltraObservation observation = Observation();
            return UltraCheckpointProtocol.Create(
                epoch, UltraDecisionPoint.MapNavigation, observation,
                UltraCheckpointProtocol.MapNavigationActions(observation));
        }

        [SetUp]
        public void Reset() => UltraDispatchStats.Reset();

        [Test]
        public void ALegalAnswer_IsAccepted()
        {
            var sink = new Sink { answer = c => c.legalActions[0].ActionId };

            string id = UltraDispatch.TryResolve(
                sink, Checkpoint(), 4, UltraDecisionPoint.MapNavigation, out string reason);

            Assert.That(id, Is.Not.Null, reason);
            Assert.That(UltraDispatchStats.Offered, Is.EqualTo(1));
            Assert.That(UltraDispatchStats.Rejected, Is.EqualTo(0));
        }

        [Test]
        public void ADeclinedDecision_LeavesTheProductionChoice()
        {
            var sink = new Sink { decline = true };

            Assert.That(UltraDispatch.TryResolve(
                sink, Checkpoint(), 4, UltraDecisionPoint.MapNavigation, out string reason), Is.Null);
            Assert.That(reason, Is.EqualTo("declined by test"));
            Assert.That(UltraDispatchStats.Declined, Is.EqualTo(1));
        }

        /// <summary>A controller that throws must cost one decision, not the run.</summary>
        [Test]
        public void AThrowingController_IsContained()
        {
            var sink = new Sink { throws = new InvalidOperationException("controller exploded") };

            Assert.That(UltraDispatch.TryResolve(
                sink, Checkpoint(), 4, UltraDecisionPoint.MapNavigation, out string reason), Is.Null);
            Assert.That(UltraDispatchStats.Faulted, Is.EqualTo(1));
            Assert.That(reason, Does.Contain("controller exploded"));
        }

        [Test]
        public void AnActionThatWasNeverOffered_IsRejected()
        {
            var sink = new Sink
            { answer = _ => UltraLegalAction.Of(UltraActionKind.MoveTo, "far").ActionId };

            Assert.That(UltraDispatch.TryResolve(
                sink, Checkpoint(), 4, UltraDecisionPoint.MapNavigation, out string reason), Is.Null);
            Assert.That(UltraDispatchStats.Rejected, Is.EqualTo(1));
            Assert.That(reason, Is.EqualTo(UltraCheckpointProtocol.FailureIllegalAction));
        }

        [Test]
        public void AnAnswerForAnOlderEpoch_IsRejected()
        {
            UltraCheckpoint issued = Checkpoint(epoch: 4);
            var sink = new Sink { answer = c => c.legalActions[0].ActionId };

            Assert.That(UltraDispatch.TryResolve(
                sink, issued, 5, UltraDecisionPoint.MapNavigation, out string reason), Is.Null);
            Assert.That(reason, Is.EqualTo(UltraCheckpointProtocol.FailureStaleEpoch));
        }

        [Test]
        public void AnAnswerForAnotherDecisionPoint_IsRejected()
        {
            var sink = new Sink { answer = c => c.legalActions[0].ActionId };

            Assert.That(UltraDispatch.TryResolve(
                sink, Checkpoint(), 4, UltraDecisionPoint.ShopAction, out string reason), Is.Null);
            Assert.That(reason, Is.EqualTo(UltraCheckpointProtocol.FailureStalePoint));
        }

        [Test]
        public void ANullAnswer_IsRejectedRatherThanCommitted()
        {
            var sink = new Sink { answer = _ => null };

            Assert.That(UltraDispatch.TryResolve(
                sink, Checkpoint(), 4, UltraDecisionPoint.MapNavigation, out _), Is.Null);
            Assert.That(UltraDispatchStats.Rejected, Is.EqualTo(1));
        }

        [Test]
        public void NoSink_CostsNothingAndIsNotCounted()
        {
            Assert.That(UltraDispatch.TryResolve(
                null, Checkpoint(), 4, UltraDecisionPoint.MapNavigation, out _), Is.Null);
            Assert.That(UltraDispatchStats.Offered, Is.EqualTo(0),
                "an absent controller must not register as a missed opportunity");
        }

        /// <summary>Agreeing with the baseline and never answering produce identical run
        /// outcomes, so the counters are the only way to tell them apart.</summary>
        [Test]
        public void AgreementAndOverride_AreCountedSeparately()
        {
            UltraDispatch.NoteOutcome(differedFromProduction: true);
            UltraDispatch.NoteOutcome(differedFromProduction: false);
            UltraDispatch.NoteOutcome(differedFromProduction: false);

            Assert.That(UltraDispatchStats.Overrode, Is.EqualTo(1));
            Assert.That(UltraDispatchStats.Agreed, Is.EqualTo(2));
        }

        [Test]
        public void Describe_SaysWhenNoControllerEverAnswered()
        {
            StringAssert.Contains("controller 未接続", UltraDispatchStats.Describe());
        }

        /// <summary>The controller may walk into an unrevealed tile — it just may not be told
        /// what is there. Removing the option would be a different kind of information leak.</summary>
        [Test]
        public void AnUnrevealedTile_IsStillAChoosableAction()
        {
            var sink = new Sink
            { answer = _ => UltraLegalAction.Of(UltraActionKind.MoveTo, "n2").ActionId };

            Assert.That(UltraDispatch.TryResolve(
                sink, Checkpoint(), 4, UltraDecisionPoint.MapNavigation, out string reason),
                Is.Not.Null, reason);
        }

        /// <summary>The property every existing baseline depends on.
        ///
        /// <para>`ultraSink` is the only thing that can pull <c>DoNavigate</c> off its original
        /// path. If it were ever non-null by default, every Optimal and Super number measured
        /// before this hook existed would silently stop being comparable — and nothing would
        /// report an error.</para></summary>
        [Test]
        public void AutoRunner_HasNoControllerAttachedByDefault()
        {
            var go = new UnityEngine.GameObject("[Ultra dispatch default test]");
            try
            {
                var runner = go.AddComponent<AutoRunner>();
                Assert.That(runner.ultraSink, Is.Null,
                    "a controller attached by default would invalidate every existing baseline");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheSinkIsAskedExactlyOncePerDecision()
        {
            var sink = new Sink { answer = c => c.legalActions[0].ActionId };

            UltraDispatch.TryResolve(sink, Checkpoint(), 4, UltraDecisionPoint.MapNavigation, out _);

            Assert.That(sink.calls, Is.EqualTo(1));
        }
    }
}
#endif

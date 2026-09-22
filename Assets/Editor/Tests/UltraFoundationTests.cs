#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AutoTest.Ultra;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    [TestFixture]
    public sealed class UltraFoundationTests
    {
        private const string Fingerprint = "ultra-tests-build";

        [Test]
        public void ScenarioBank_IsDeterministicAndPhaseSeparated()
        {
            var bank = new UltraScenarioBank(0x1020304050607080UL);
            var search = new HashSet<ulong>();
            var confirmation = new HashSet<ulong>();
            for (int i = 0; i < 2048; i++)
            {
                ulong a = bank.GetScenarioSeed(0xCAFEBABEUL, 10, 20, UltraScenarioPhase.Search, i);
                ulong b = bank.GetScenarioSeed(0xCAFEBABEUL, 10, 20, UltraScenarioPhase.Search, i);
                ulong c = bank.GetScenarioSeed(0xCAFEBABEUL, 10, 20, UltraScenarioPhase.Confirmation, i);
                Assert.That(a, Is.EqualTo(b));
                Assert.That(a, Is.Not.EqualTo(c));
                Assert.That(search.Add(a), Is.True, "duplicate search scenario at " + i);
                Assert.That(confirmation.Add(c), Is.True, "duplicate confirmation scenario at " + i);
            }
            search.IntersectWith(confirmation);
            Assert.That(search, Is.Empty);
        }

        [Test]
        public void Decision_IsCandidateOrderInvariantAndPaired()
        {
            var oracleForward = new TestOracle(Fingerprint);
            var oracleReverse = new TestOracle(Fingerprint);
            UltraDecisionConfig config = EnabledConfig();

            UltraDecisionResult forward = new UltraDecisionEngine(config, oracleForward)
                .Decide(Request(reverse: false));
            UltraDecisionResult reverse = new UltraDecisionEngine(config, oracleReverse)
                .Decide(Request(reverse: true));

            Assert.That(forward.selectedActionId, Is.EqualTo("better"));
            Assert.That(reverse.selectedActionId, Is.EqualTo(forward.selectedActionId));
            Assert.That(oracleReverse.RequestTrace, Is.EqualTo(oracleForward.RequestTrace));
            Assert.That(oracleForward.HasPairedScenarioSeeds("search"), Is.True);
            Assert.That(oracleForward.HasPairedScenarioSeeds("confirm"), Is.True);
        }

        [Test]
        public void Shadow_AlwaysExecutesBaseline()
        {
            var config = EnabledConfig();
            config.mode = UltraExecutionMode.Shadow;
            var result = new UltraDecisionEngine(config, new TestOracle(Fingerprint)).Decide(Request(false));

            Assert.That(result.proposedActionId, Is.EqualTo("better"));
            Assert.That(result.selectedActionId, Is.EqualTo("baseline"));
            Assert.That(result.fallbackReason, Is.EqualTo(UltraFallbackReason.ShadowOnly));
        }

        [Test]
        public void InvalidWorkerStates_FailClosedToBaseline()
        {
            UltraDecisionRequest request = Request(false);

            var unhealthy = new TestOracle(Fingerprint) { IsHealthyValue = false };
            UltraDecisionResult noWorker = new UltraDecisionEngine(EnabledConfig(), unhealthy).Decide(request);
            Assert.That(noWorker.selectedActionId, Is.EqualTo("baseline"));
            Assert.That(noWorker.fallbackReason, Is.EqualTo(UltraFallbackReason.OracleUnavailable));

            UltraDecisionResult mismatch = new UltraDecisionEngine(
                EnabledConfig(), new TestOracle("different-build")).Decide(request);
            Assert.That(mismatch.selectedActionId, Is.EqualTo("baseline"));
            Assert.That(mismatch.fallbackReason, Is.EqualTo(UltraFallbackReason.BuildMismatch));

            var failed = new TestOracle(Fingerprint) { FailAll = true };
            UltraDecisionResult episodeFailure = new UltraDecisionEngine(EnabledConfig(), failed).Decide(request);
            Assert.That(episodeFailure.selectedActionId, Is.EqualTo("baseline"));
            Assert.That(episodeFailure.fallbackReason, Is.EqualTo(UltraFallbackReason.EpisodeFailure));
        }

        [Test]
        public void InsufficientEvidence_FailsClosedToBaseline()
        {
            var config = EnabledConfig();
            config.confirmationScenarios = 1;
            var result = new UltraDecisionEngine(config, new TestOracle(Fingerprint)).Decide(Request(false));
            Assert.That(result.proposedActionId, Is.EqualTo("better"));
            Assert.That(result.selectedActionId, Is.EqualTo("baseline"));
            Assert.That(result.fallbackReason, Is.EqualTo(UltraFallbackReason.InsufficientEvidence));
        }

        [Test]
        public void WorkerExceptionAndStepCapViolation_FailClosedToBaseline()
        {
            var throwing = new TestOracle(Fingerprint) { ThrowOnRun = true };
            UltraDecisionResult exceptionResult = new UltraDecisionEngine(EnabledConfig(), throwing)
                .Decide(Request(false));
            Assert.That(exceptionResult.selectedActionId, Is.EqualTo("baseline"));
            Assert.That(exceptionResult.fallbackReason, Is.EqualTo(UltraFallbackReason.EpisodeFailure));

            var overBudget = new TestOracle(Fingerprint) { ProductionStepsOverride = 1_000_001 };
            UltraDecisionResult capResult = new UltraDecisionEngine(EnabledConfig(), overBudget)
                .Decide(Request(false));
            Assert.That(capResult.selectedActionId, Is.EqualTo("baseline"));
            Assert.That(capResult.fallbackReason, Is.EqualTo(UltraFallbackReason.EpisodeFailure));

            var fractional = new TestOracle(Fingerprint) { RewardOverride = 0.5f };
            UltraDecisionResult fractionalResult = new UltraDecisionEngine(EnabledConfig(), fractional)
                .Decide(Request(false));
            Assert.That(fractionalResult.selectedActionId, Is.EqualTo("baseline"));
            Assert.That(fractionalResult.fallbackReason, Is.EqualTo(UltraFallbackReason.EpisodeFailure));
        }

        [Test]
        public void UnboundedConfigAndPayload_AreRejectedBeforeWorkerExecution()
        {
            UltraDecisionConfig config = EnabledConfig();
            config.searchScenariosPerCandidate = UltraDecisionEngine.AbsoluteMaxSearchScenariosPerCandidate + 1;
            UltraDecisionResult badBudget = new UltraDecisionEngine(config, new TestOracle(Fingerprint))
                .Decide(Request(false));
            Assert.That(badBudget.selectedActionId, Is.EqualTo("baseline"));
            Assert.That(badBudget.fallbackReason, Is.EqualTo(UltraFallbackReason.InvalidRequest));

            UltraDecisionRequest request = Request(false);
            request.candidates[0].payload = new string('x', UltraDecisionEngine.AbsoluteMaxActionPayloadChars + 1);
            UltraDecisionResult badPayload = new UltraDecisionEngine(EnabledConfig(), new TestOracle(Fingerprint))
                .Decide(request);
            Assert.That(badPayload.selectedActionId, Is.EqualTo("baseline"));
            Assert.That(badPayload.fallbackReason, Is.EqualTo(UltraFallbackReason.InvalidRequest));
        }

        [Test]
        public void ConstructorSnapshotsMutableConfig()
        {
            UltraDecisionConfig config = EnabledConfig();
            var engine = new UltraDecisionEngine(config, new TestOracle(Fingerprint));
            config.mode = UltraExecutionMode.Disabled;
            config.searchScenariosPerCandidate = int.MaxValue;

            UltraDecisionResult result = engine.Decide(Request(false));
            Assert.That(result.selectedActionId, Is.EqualTo("better"));
            Assert.That(result.fallbackReason, Is.EqualTo(UltraFallbackReason.None));
        }

        [Test]
        public void DecisionEnginePublishesInternalPhaseProgress()
        {
            UltraProgressHub.Reset();
            try
            {
                UltraProgressSession session = UltraProgressHub.Begin(new UltraProgressPlan
                {
                    label = "decision-progress",
                    totalRuns = 1,
                    workerCount = 4,
                });
                UltraDecisionRequest request = Request(false);
                request.progressSession = session;

                UltraDecisionResult result = new UltraDecisionEngine(
                    EnabledConfig(),
                    new TestOracle(Fingerprint)).Decide(request);
                UltraProgressSnapshot snapshot = UltraProgressHub.Snapshot();

                Assert.That(result.selectedActionId, Is.EqualTo("better"));
                Assert.That(snapshot.State, Is.EqualTo(UltraProgressState.Running));
                Assert.That(snapshot.Phase, Is.EqualTo(UltraProgressPhase.Executing));
                Assert.That(snapshot.PhaseCompleted, Is.EqualTo(1));
                Assert.That(snapshot.PhaseTotal, Is.EqualTo(1));
                StringAssert.Contains("accepted proposal", snapshot.Detail);
                Assert.That(snapshot.CompletedRuns, Is.Zero,
                    "the outer controller, not a single decision, owns run completion");
            }
            finally
            {
                UltraProgressHub.Reset();
            }
        }

        private static UltraDecisionConfig EnabledConfig()
        {
            return new UltraDecisionConfig
            {
                mode = UltraExecutionMode.Enabled,
                plannerSalt = 0xA1A2A3A4A5A6A7A8UL,
                searchScenariosPerCandidate = 8,
                confirmationScenarios = 64,
                confirmationAlpha = 0.01,
            };
        }

        private static UltraDecisionRequest Request(bool reverse)
        {
            var request = new UltraDecisionRequest
            {
                runOrdinal = 77,
                decisionOrdinal = 5,
                publicStateHash = 0x12345678UL,
                buildFingerprint = Fingerprint,
                checkpointSchema = "test-v1",
                publicCheckpointBase64 = "AA==",
                baselineActionId = "baseline",
            };
            if (reverse)
            {
                request.candidates.Add(new UltraCandidate("better"));
                request.candidates.Add(new UltraCandidate("baseline"));
            }
            else
            {
                request.candidates.Add(new UltraCandidate("baseline"));
                request.candidates.Add(new UltraCandidate("better"));
            }
            return request;
        }

        private sealed class TestOracle : IUltraProductionOracle
        {
            private readonly List<UltraEpisodeRequest> _requests = new List<UltraEpisodeRequest>();

            public TestOracle(string fingerprint) { BuildFingerprint = fingerprint; }
            public bool IsHealthyValue = true;
            public bool FailAll;
            public bool ThrowOnRun;
            public int ProductionStepsOverride = 1;
            public float RewardOverride = float.NaN;
            public bool IsHealthy => IsHealthyValue;
            public string BuildFingerprint { get; }

            public string RequestTrace
            {
                get
                {
                    var parts = new List<string>(_requests.Count);
                    for (int i = 0; i < _requests.Count; i++)
                    {
                        UltraEpisodeRequest r = _requests[i];
                        parts.Add(r.jobId + "@" + r.scenarioStartSeedHex);
                    }
                    return string.Join(";", parts);
                }
            }

            public bool TryRunEpisode(UltraEpisodeRequest request, out UltraEpisodeResult result)
            {
                _requests.Add(request);
                if (ThrowOnRun) throw new InvalidOperationException("synthetic worker failure");
                if (FailAll)
                {
                    result = null;
                    return false;
                }
                result = new UltraEpisodeResult
                {
                    success = true,
                    reachedTerminal = true,
                    primaryReward = float.IsNaN(RewardOverride)
                        ? (request.actionId == "better" ? 1f : 0f)
                        : RewardOverride,
                    finalPublicStateHash = "test",
                    productionSteps = ProductionStepsOverride,
                };
                return true;
            }

            public bool HasPairedScenarioSeeds(string stagePrefix)
            {
                var byScenario = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                for (int i = 0; i < _requests.Count; i++)
                {
                    UltraEpisodeRequest request = _requests[i];
                    if (!request.jobId.Contains(":" + stagePrefix)) continue;
                    if (!byScenario.TryGetValue(request.scenarioStartSeedHex, out HashSet<string> actions))
                    {
                        actions = new HashSet<string>(StringComparer.Ordinal);
                        byScenario.Add(request.scenarioStartSeedHex, actions);
                    }
                    actions.Add(request.actionId);
                }
                if (byScenario.Count == 0) return false;
                foreach (HashSet<string> actions in byScenario.Values)
                    if (!actions.Contains("baseline") || !actions.Contains("better")) return false;
                return true;
            }
        }
    }
}
#endif

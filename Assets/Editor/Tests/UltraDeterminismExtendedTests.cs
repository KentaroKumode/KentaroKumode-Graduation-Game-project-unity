#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using AutoTest.Ultra;
using GameLoop;
using NUnit.Framework;
using UnityEngine;

namespace AutoTest.EditorTests.Ultra
{
    [TestFixture]
    public sealed class UltraDeterminismExtendedTests
    {
        private const string Fingerprint = "ultra-determinism-extended";
        private const ulong PlannerSalt = 0xD37E4A91C52B680FUL;
        private const ulong PublicStateHash = 0xA4D9C73015BE682FUL;

        [Test]
        public void SameInputTwice_ProducesBitIdenticalDecisionAndEpisodeRequests()
        {
            UltraDecisionConfig firstConfig = EnabledConfig(searchCount: 7, confirmationCount: 64);
            UltraDecisionConfig secondConfig = EnabledConfig(searchCount: 7, confirmationCount: 64);
            UltraDecisionRequest request = MakeRequest();
            var firstOracle = new RecordingOracle(Fingerprint);
            var secondOracle = new RecordingOracle(Fingerprint);

            UltraDecisionResult first = new UltraDecisionEngine(firstConfig, firstOracle).Decide(request);
            UltraDecisionResult second = new UltraDecisionEngine(secondConfig, secondOracle).Decide(request);

            Assert.That(first.selectedActionId, Is.EqualTo("better"));
            Assert.That(second.selectedActionId, Is.EqualTo(first.selectedActionId));
            Assert.That(second.proposedActionId, Is.EqualTo(first.proposedActionId));
            Assert.That(second.fallbackReason, Is.EqualTo(first.fallbackReason));
            Assert.That(second.completedEpisodes, Is.EqualTo(first.completedEpisodes));
            Assert.That(
                BitConverter.DoubleToInt64Bits(second.proposedMean),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(first.proposedMean)));
            Assert.That(
                BitConverter.DoubleToInt64Bits(second.baselineMean),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(first.baselineMean)));
            Assert.That(
                BitConverter.DoubleToInt64Bits(second.pairedAdvantage),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(first.pairedAdvantage)));
            Assert.That(
                BitConverter.DoubleToInt64Bits(second.pairedLowerBound),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(first.pairedLowerBound)));
            Assert.That(second.diagnostic, Is.EqualTo(first.diagnostic));

            AssertEpisodeRequestsBitEqual(firstOracle.Requests, secondOracle.Requests);
        }

        [TestCase(UltraScenarioPhase.Search)]
        [TestCase(UltraScenarioPhase.Confirmation)]
        [TestCase(UltraScenarioPhase.Shadow)]
        public void IncreasingScenarioCount_PreservesExistingSeedPrefix(UltraScenarioPhase phase)
        {
            var bank = new UltraScenarioBank(PlannerSalt);
            List<ulong> shortBank = GenerateSeeds(bank, phase, 9);
            List<ulong> extendedBank = GenerateSeeds(bank, phase, 73);

            Assert.That(extendedBank.Count, Is.GreaterThan(shortBank.Count));
            for (int i = 0; i < shortBank.Count; i++)
                Assert.That(extendedBank[i], Is.EqualTo(shortBank[i]), "prefix mismatch at scenario " + i);
        }

        [Test]
        public void LiveGameRngSeedAndRunIndex_DoNotAffectScenarioBank()
        {
            var bank = new UltraScenarioBank(PlannerSalt);
            try
            {
                ResetLiveGameRng(0x1111222233334444UL, 17);
                List<ulong> first = GenerateSeeds(bank, UltraScenarioPhase.Search, 64);

                ResetLiveGameRng(0xAAAABBBBCCCCDDDDUL, 901);
                List<ulong> second = GenerateSeeds(bank, UltraScenarioPhase.Search, 64);

                CollectionAssert.AreEqual(first, second);
            }
            finally
            {
                // ClearSeed alone does not reset the current run salt/counters. Establish a
                // neutral run first so this static-state test leaves a deterministic baseline.
                ResetLiveGameRng(0UL, 0);
                GameRng.ClearSeed();
            }
        }

        [Test]
        public void SearchAndConfirmationSeedSets_DoNotOverlap()
        {
            const int Count = 4096;
            var bank = new UltraScenarioBank(PlannerSalt);
            var search = new HashSet<ulong>(GenerateSeeds(bank, UltraScenarioPhase.Search, Count));
            var confirmation = new HashSet<ulong>(GenerateSeeds(bank, UltraScenarioPhase.Confirmation, Count));

            Assert.That(search.Count, Is.EqualTo(Count), "duplicate seed in search bank");
            Assert.That(confirmation.Count, Is.EqualTo(Count), "duplicate seed in confirmation bank");
            Assert.That(search.Overlaps(confirmation), Is.False);
        }

        [Test]
        public void OracleFailure_ReturnsBaselineImmediatelyWithoutRetry()
        {
            UltraDecisionConfig config = EnabledConfig(searchCount: 2, confirmationCount: 64);
            // Two candidates x two search episodes = four successful calls. The first
            // confirmation episode fails; any retry or continued loop would make Calls > 5.
            var oracle = new RecordingOracle(Fingerprint) { FailOnCall = 5 };

            UltraDecisionResult result = new UltraDecisionEngine(config, oracle).Decide(MakeRequest());

            Assert.That(result.proposedActionId, Is.EqualTo("better"));
            Assert.That(result.selectedActionId, Is.EqualTo("baseline"));
            Assert.That(result.fallbackReason, Is.EqualTo(UltraFallbackReason.EpisodeFailure));
            Assert.That(result.completedEpisodes, Is.EqualTo(4));
            Assert.That(oracle.Calls, Is.EqualTo(5), "oracle failure was retried or the episode loop continued");
        }

        private static UltraDecisionConfig EnabledConfig(int searchCount, int confirmationCount)
        {
            return new UltraDecisionConfig
            {
                mode = UltraExecutionMode.Enabled,
                plannerSalt = PlannerSalt,
                searchScenariosPerCandidate = searchCount,
                confirmationScenarios = confirmationCount,
                maxProductionStepsPerEpisode = 1234,
                confirmationAlpha = 0.01,
                minimumAdvantage = 0.0,
            };
        }

        private static UltraDecisionRequest MakeRequest()
        {
            var request = new UltraDecisionRequest
            {
                runOrdinal = 31,
                decisionOrdinal = 12,
                publicStateHash = PublicStateHash,
                buildFingerprint = Fingerprint,
                checkpointSchema = "extended-v1",
                publicCheckpointBase64 = "AQIDBA==",
                baselineActionId = "baseline",
            };
            request.candidates.Add(new UltraCandidate("better", "payload-better"));
            request.candidates.Add(new UltraCandidate("baseline", "payload-baseline"));
            return request;
        }

        private static List<ulong> GenerateSeeds(
            UltraScenarioBank bank,
            UltraScenarioPhase phase,
            int count)
        {
            var result = new List<ulong>(count);
            for (int i = 0; i < count; i++)
                result.Add(bank.GetScenarioSeed(PublicStateHash, 31, 12, phase, i));
            return result;
        }

        private static void ResetLiveGameRng(ulong masterSeed, int runIndex)
        {
            GameRng.SetMasterSeed(masterSeed);
            GameRng.BeginRun(runIndex);
        }

        private static void AssertEpisodeRequestsBitEqual(
            IReadOnlyList<UltraEpisodeRequest> expected,
            IReadOnlyList<UltraEpisodeRequest> actual)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (int i = 0; i < expected.Count; i++)
            {
                byte[] expectedBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(expected[i]));
                byte[] actualBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(actual[i]));
                CollectionAssert.AreEqual(expectedBytes, actualBytes, "episode request mismatch at index " + i);
            }
        }

        private sealed class RecordingOracle : IUltraProductionOracle
        {
            private readonly List<UltraEpisodeRequest> _requests = new List<UltraEpisodeRequest>();

            public RecordingOracle(string fingerprint)
            {
                BuildFingerprint = fingerprint;
            }

            public int FailOnCall;
            public int Calls { get; private set; }
            public bool IsHealthy => true;
            public string BuildFingerprint { get; }
            public IReadOnlyList<UltraEpisodeRequest> Requests => _requests;

            public bool TryRunEpisode(UltraEpisodeRequest request, out UltraEpisodeResult result)
            {
                Calls++;
                _requests.Add(Clone(request));
                if (FailOnCall > 0 && Calls == FailOnCall)
                {
                    result = null;
                    return false;
                }

                result = new UltraEpisodeResult
                {
                    success = true,
                    reachedTerminal = true,
                    primaryReward = request.actionId == "better" ? 1f : 0f,
                    finalPublicStateHash = "extended-terminal",
                    productionSteps = 1,
                };
                return true;
            }

            private static UltraEpisodeRequest Clone(UltraEpisodeRequest source)
            {
                return new UltraEpisodeRequest
                {
                    protocolVersion = source.protocolVersion,
                    jobId = source.jobId,
                    expectedBuildFingerprint = source.expectedBuildFingerprint,
                    checkpointSchema = source.checkpointSchema,
                    publicCheckpointBase64 = source.publicCheckpointBase64,
                    actionId = source.actionId,
                    actionPayload = source.actionPayload,
                    scenarioStartSeedHex = source.scenarioStartSeedHex,
                    maxProductionSteps = source.maxProductionSteps,
                };
            }
        }
    }
}
#endif

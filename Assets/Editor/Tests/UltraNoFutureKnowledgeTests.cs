#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AutoTest.Ultra;
using GameLoop;
using MapSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    /// <summary>
    /// The property the whole Ultra design rests on: <b>a decision is a function of public
    /// information alone.</b>
    ///
    /// <para>Everything else — the observation whitelist, the veil, the synthetic seeds — is
    /// machinery in service of this one claim, and machinery can be argued about. These tests
    /// settle it mechanically instead: change the live random stream underneath a decision and
    /// the decision must not move; take a decision and the live stream must not move either.</para>
    ///
    /// <para>If future knowledge ever leaks in, one of these fails. That is the point of
    /// writing them as properties rather than as prose in a design document.</para>
    /// </summary>
    [TestFixture]
    public sealed class UltraNoFutureKnowledgeTests
    {
        /// <summary>Deterministic in the request only. Stands in for the production worker,
        /// which likewise never sees the live stream.</summary>
        private sealed class SeedOnlyOracle : IUltraProductionOracle
        {
            public bool IsHealthy { get { return true; } }
            public string BuildFingerprint { get { return new string('a', 64); } }

            public bool TryRunEpisode(UltraEpisodeRequest request, out UltraEpisodeResult result)
            {
                // Outcome depends on the scenario seed and the action, never on GameRng.
                int hash = (request.scenarioStartSeedHex + "|" + request.actionId).GetHashCode();
                bool prefersLater = request.actionId.EndsWith("b:-1", StringComparison.Ordinal);
                int bucket = Math.Abs(hash) % 100;
                result = new UltraEpisodeResult
                {
                    success = true,
                    reachedTerminal = true,
                    primaryReward = bucket < (prefersLater ? 70 : 30) ? 1f : 0f,
                    finalPublicStateHash = "h",
                    productionSteps = 1,
                };
                return true;
            }
        }

        private static UltraCheckpoint Checkpoint()
        {
            var observation = new UltraObservation
            {
                run = new UltraRunView { currentFloor = 4, playerHP = 44, playerMaxHP = 93 },
                map = new UltraMapView
                {
                    floor = 4,
                    currentNodeId = "start",
                    nodes = new[]
                    {
                        new UltraMapNodeView
                        { id = "a", row = 1, view = UltraTileView.Battle, revealed = true, reachableNow = true },
                        new UltraMapNodeView
                        { id = "b", row = 1, view = UltraTileView.Shop, revealed = true, reachableNow = true },
                    },
                },
            };
            return UltraCheckpointProtocol.Create(
                5, UltraDecisionPoint.MapNavigation, observation,
                UltraCheckpointProtocol.MapNavigationActions(observation));
        }

        private static UltraResumePayload Payload(int veilSeed)
        {
            return new UltraResumePayload
            {
                epoch = 5,
                decisionPoint = (int)UltraDecisionPoint.MapNavigation,
                veilSeed = veilSeed,
                run = new UltraRunSnapshot
                { fields = new[] { new UltraSnapshotField { name = "coins", kind = "int", value = "7" } } },
                map = new UltraMapSnapshot
                {
                    floor = 4,
                    nodes = new[] { new UltraMapNodeSnapshot { id = "a", row = 1 } },
                },
            };
        }

        private static string Decide()
        {
            var sink = new UltraRolloutSink(
                new UltraRolloutEvaluator(new SeedOnlyOracle()) { RolloutsPerAction = 24 },
                Payload, 0x1234_5678UL);
            sink.TryDecide(Checkpoint(), out string actionId, out _);
            return actionId;
        }

        /// <summary>Advance the live generator to a genuinely different state.</summary>
        private static void ChurnLiveRng(int draws)
        {
            for (int i = 0; i < draws; i++) GameRng.RangeAuto("test.churn", 0, 1_000_000);
        }

        // ------------------------------------------------------------------
        //  the decision does not read the live stream
        // ------------------------------------------------------------------

        [Test]
        public void TheSameObservationDecidesTheSameWayUnderADifferentLiveRngState()
        {
            GameRng.SetMasterSeed(1);
            GameRng.BeginRun(0);
            string first = Decide();

            // A completely different live stream: new seed, new run, thousands of draws burnt.
            GameRng.SetMasterSeed(0xDEAD_BEEFUL);
            GameRng.BeginRun(37);
            ChurnLiveRng(5000);
            string second = Decide();

            Assert.That(second, Is.EqualTo(first),
                "the decision moved when only the live random stream changed — that stream is "
                + "the future, and reading it is exactly what must not happen");
            Assert.That(first, Is.Not.Null);
        }

        [Test]
        public void TheDecisionIsStableAcrossManyDifferentLiveStates()
        {
            GameRng.SetMasterSeed(11);
            GameRng.BeginRun(0);
            string expected = Decide();

            for (int run = 1; run <= 8; run++)
            {
                GameRng.SetMasterSeed((ulong)(run * 7919));
                GameRng.BeginRun(run);
                ChurnLiveRng(run * 137);
                Assert.That(Decide(), Is.EqualTo(expected), "live run " + run);
            }
        }

        [Test]
        public void AnUnseededLiveGeneratorChangesNothingEither()
        {
            GameRng.SetMasterSeed(5);
            GameRng.BeginRun(0);
            string seeded = Decide();

            GameRng.ClearSeed();
            GameRng.BeginRun(0);
            Assert.That(Decide(), Is.EqualTo(seeded));

            GameRng.SetMasterSeed(5);   // leave the generator in a known state
            GameRng.BeginRun(0);
        }

        // ------------------------------------------------------------------
        //  the decision does not advance the live stream
        // ------------------------------------------------------------------

        /// <summary>The other half of the RNG contract. If thinking consumed draws, the live
        /// run's future would shift according to how long the controller thought — the paired
        /// seed comparisons the whole project rests on would stop being paired.</summary>
        [Test]
        public void ThinkingConsumesNoLiveRandomness()
        {
            GameRng.SetMasterSeed(3);
            GameRng.BeginRun(0);

            long before = GameRng.TotalDraws;
            Decide();
            Assert.That(GameRng.TotalDraws, Is.EqualTo(before),
                "the controller drew from the live generator while thinking");
        }

        [Test]
        public void CapturingAnObservationConsumesNoLiveRandomness()
        {
            GameRng.SetMasterSeed(3);
            GameRng.BeginRun(0);

            var map = new FloorMap { floor = 2, rowCount = 3, startNodeId = "n0" };
            map.AddNode(new MapNode("n0", 0, 0, TileType.Outpost));
            var go = new UnityEngine.GameObject("[Ultra rng probe]");
            try
            {
                var manager = go.AddComponent<MapManager>();
                manager.RestoreState(map, "n0", 3, 5);
                var run = new RunState { currentFloor = 2, playerHP = 30, playerMaxHP = 60 };

                long before = GameRng.TotalDraws;
                UltraObservationBuilder.Capture(run, manager, "MapNavigation", 1);
                UltraRunSnapshot.Capture(run);
                UltraMapSnapshot.Capture(manager);

                Assert.That(GameRng.TotalDraws, Is.EqualTo(before),
                    "an observation that perturbs the run it observes is not an observation");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>The veil samples beliefs, and it must do so from its own generator.</summary>
        [Test]
        public void VeilingConsumesNoLiveRandomness()
        {
            GameRng.SetMasterSeed(3);
            GameRng.BeginRun(0);

            var snapshot = new UltraMapSnapshot
            {
                floor = 5,
                nodes = new[]
                {
                    new UltraMapNodeSnapshot { id = "s", row = 2, type = (int)TileType.Shop },
                    new UltraMapNodeSnapshot { id = "f", row = 3, type = (int)TileType.Battle, revealed = false },
                },
            };

            long before = GameRng.TotalDraws;
            UltraSnapshotVeil.Apply(snapshot, seed: 9, falseMerchantChance: 0.3f);

            Assert.That(GameRng.TotalDraws, Is.EqualTo(before));
        }

        // ------------------------------------------------------------------
        //  the counter itself
        // ------------------------------------------------------------------

        [Test]
        public void TheDrawCounterActuallyCounts()
        {
            GameRng.SetMasterSeed(3);
            GameRng.BeginRun(0);

            long before = GameRng.TotalDraws;
            ChurnLiveRng(10);

            Assert.That(GameRng.TotalDraws, Is.EqualTo(before + 10),
                "if the probe cannot see consumption, the contract tests above prove nothing");
        }

        // ------------------------------------------------------------------
        //  what the rollout does depend on
        // ------------------------------------------------------------------

        /// <summary>The decision must respond to the board — a decision that ignores its input
        /// would pass every test above for the wrong reason.</summary>
        [Test]
        public void TheDecisionDoesDependOnTheObservation()
        {
            GameRng.SetMasterSeed(2);
            GameRng.BeginRun(0);

            string withBoth = Decide();

            var soloObservation = new UltraObservation
            {
                map = new UltraMapView
                {
                    currentNodeId = "start",
                    nodes = new[]
                    {
                        new UltraMapNodeView
                        { id = "a", row = 1, view = UltraTileView.Battle, revealed = true, reachableNow = true },
                    },
                },
            };
            UltraCheckpoint solo = UltraCheckpointProtocol.Create(
                5, UltraDecisionPoint.MapNavigation, soloObservation,
                UltraCheckpointProtocol.MapNavigationActions(soloObservation));

            var sink = new UltraRolloutSink(
                new UltraRolloutEvaluator(new SeedOnlyOracle()) { RolloutsPerAction = 24 },
                Payload, 0x1234_5678UL);
            sink.TryDecide(solo, out string soloAction, out _);

            Assert.That(soloAction, Is.Not.EqualTo(withBoth),
                "removing the better option must change the answer, or the decision is not "
                + "reading the board at all");
        }
    }
}
#endif

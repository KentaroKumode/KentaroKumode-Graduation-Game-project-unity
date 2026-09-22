#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutoTest.Ultra;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    /// <summary>
    /// Phase B gates: a checkpoint is reproducible, its action set is issued rather than
    /// requested, and a stale or altered commit is refused before it reaches the run.
    /// </summary>
    [TestFixture]
    public sealed class UltraCheckpointTests
    {
        private static UltraObservation Observation(int floor = 3, int hp = 40)
        {
            return new UltraObservation
            {
                phase = "Map",
                run = new UltraRunView { currentFloor = floor, playerHP = hp, playerMaxHP = 93 },
                map = new UltraMapView
                {
                    floor = floor,
                    rowCount = 4,
                    currentNodeId = "n0",
                    bossNodeId = "boss",
                    nodes = new[]
                    {
                        new UltraMapNodeView
                        { id = "n0", row = 0, view = UltraTileView.Outpost, revealed = true },
                        new UltraMapNodeView
                        { id = "n1", row = 1, view = UltraTileView.Shop, revealed = true, reachableNow = true },
                        new UltraMapNodeView
                        { id = "n2", row = 1, view = UltraTileView.Unknown, revealed = false, reachableNow = true },
                        new UltraMapNodeView
                        { id = "n3", row = 2, view = UltraTileView.Battle, revealed = true },
                    },
                },
            };
        }

        private static UltraCheckpoint Checkpoint(int epoch = 7,
            UltraDecisionPoint point = UltraDecisionPoint.MapNavigation)
        {
            UltraObservation observation = Observation();
            return UltraCheckpointProtocol.Create(
                epoch, point, observation,
                UltraCheckpointProtocol.MapNavigationActions(observation));
        }

        // ------------------------------------------------------------------
        //  reproducibility
        // ------------------------------------------------------------------

        [Test]
        public void SameState_ProducesTheSameHash()
        {
            Assert.That(Checkpoint().mechanicalHash, Is.EqualTo(Checkpoint().mechanicalHash));
        }

        [Test]
        public void ActionOrder_DoesNotChangeTheHash()
        {
            UltraObservation observation = Observation();
            List<UltraLegalAction> forward = UltraCheckpointProtocol.MapNavigationActions(observation);
            var reversed = new List<UltraLegalAction>(forward);
            reversed.Reverse();

            string a = UltraCheckpointProtocol.Create(1, UltraDecisionPoint.MapNavigation, observation, forward).mechanicalHash;
            string b = UltraCheckpointProtocol.Create(1, UltraDecisionPoint.MapNavigation, observation, reversed).mechanicalHash;

            Assert.That(b, Is.EqualTo(a),
                "the game's enumeration order must not leak into the hash");
        }

        [Test]
        public void DuplicateActions_AreCollapsed()
        {
            UltraObservation observation = Observation();
            var withDupes = new List<UltraLegalAction>
            {
                UltraLegalAction.Of(UltraActionKind.MoveTo, "n1"),
                UltraLegalAction.Of(UltraActionKind.MoveTo, "n1"),
                UltraLegalAction.Of(UltraActionKind.MoveTo, "n2"),
            };

            UltraCheckpoint checkpoint = UltraCheckpointProtocol.Create(
                1, UltraDecisionPoint.MapNavigation, observation, withDupes);

            Assert.That(checkpoint.legalActions.Length, Is.EqualTo(2));
        }

        [Test]
        public void RelabellingAnAction_DoesNotInvalidateTheCheckpoint()
        {
            UltraObservation observation = Observation();
            UltraCheckpoint plain = UltraCheckpointProtocol.Create(
                1, UltraDecisionPoint.MapNavigation, observation,
                new List<UltraLegalAction> { UltraLegalAction.Of(UltraActionKind.MoveTo, "n1", -1, "Shop") });
            UltraCheckpoint relabelled = UltraCheckpointProtocol.Create(
                1, UltraDecisionPoint.MapNavigation, observation,
                new List<UltraLegalAction> { UltraLegalAction.Of(UltraActionKind.MoveTo, "n1", -1, "商店") });

            Assert.That(relabelled.mechanicalHash, Is.EqualTo(plain.mechanicalHash),
                "labels are for humans; a copy-edit must not invalidate stored checkpoints");
        }

        /// <summary>The observation is hashed through its serialized form precisely so that a
        /// new observation field is covered without anyone remembering to list it — the
        /// failure mode that made portfolio profile fingerprint v1 useless.</summary>
        [Test]
        public void ChangingAnyObservationValue_ChangesTheHash()
        {
            UltraObservation a = Observation(floor: 3, hp: 40);
            UltraObservation b = Observation(floor: 3, hp: 41);

            Assert.That(
                UltraCheckpointProtocol.Create(1, UltraDecisionPoint.MapNavigation, b, null).mechanicalHash,
                Is.Not.EqualTo(
                UltraCheckpointProtocol.Create(1, UltraDecisionPoint.MapNavigation, a, null).mechanicalHash));
        }

        [Test]
        public void EpochAndDecisionPoint_AreInsideTheHash()
        {
            Assert.That(Checkpoint(epoch: 8).mechanicalHash,
                Is.Not.EqualTo(Checkpoint(epoch: 7).mechanicalHash));
            Assert.That(Checkpoint(point: UltraDecisionPoint.RestChoice).mechanicalHash,
                Is.Not.EqualTo(Checkpoint(point: UltraDecisionPoint.MapNavigation).mechanicalHash));
        }

        // ------------------------------------------------------------------
        //  legal action set
        // ------------------------------------------------------------------

        [Test]
        public void MapActions_OfferExactlyTheReachableNodes()
        {
            UltraCheckpoint checkpoint = Checkpoint();

            Assert.That(checkpoint.legalActions.Select(a => a.targetId),
                Is.EquivalentTo(new[] { "n1", "n2" }));
            Assert.That(checkpoint.legalActions.All(a => a.kind == UltraActionKind.MoveTo));
        }

        /// <summary>An unrevealed tile is still a legal destination — you may walk into the
        /// unknown. What must not happen is the action set being *chosen* using the hidden
        /// type, so the offered move carries only the Unknown view.</summary>
        [Test]
        public void AnUnrevealedNeighbour_IsOfferedWithoutRevealingItsType()
        {
            UltraCheckpoint checkpoint = Checkpoint();
            UltraLegalAction hidden = checkpoint.legalActions.Single(a => a.targetId == "n2");

            Assert.That(hidden.label, Does.StartWith(UltraTileView.Unknown.ToString()));
            Assert.That(hidden.label, Does.Not.Contain(UltraTileView.Boss.ToString()));
        }

        [Test]
        public void MapActions_ToleratesAnEmptyObservation()
        {
            Assert.That(UltraCheckpointProtocol.MapNavigationActions(null), Is.Empty);
            Assert.That(UltraCheckpointProtocol.MapNavigationActions(new UltraObservation()), Is.Empty);
        }

        // ------------------------------------------------------------------
        //  discrete choices (reward / rest / event)
        // ------------------------------------------------------------------

        private static UltraObservation WithChoice(params UltraChoiceOption[] options)
        {
            UltraObservation observation = Observation();
            observation.choice = new UltraChoiceView { kind = "rest", options = options };
            return observation;
        }

        [Test]
        public void ChoiceActions_OfferEveryAvailableOption()
        {
            UltraObservation observation = WithChoice(
                new UltraChoiceOption { index = 0, id = "heal", label = "HP回復" },
                new UltraChoiceOption { index = 1, id = "upgrade", label = "武器強化" });

            var actions = UltraCheckpointProtocol.ChoiceActions(observation, UltraActionKind.ChooseRest);

            Assert.That(actions.Count, Is.EqualTo(2));
            Assert.That(actions[0].kind, Is.EqualTo(UltraActionKind.ChooseRest));
        }

        /// <summary>An option the player cannot afford stays visible in the observation but is
        /// not an action. Offering it would let the controller "choose" something the game
        /// then refuses, which looks exactly like the controller declining.</summary>
        [Test]
        public void AnUnavailableOption_IsVisibleButNotOffered()
        {
            UltraObservation observation = WithChoice(
                new UltraChoiceOption { index = 0, id = "heal" },
                new UltraChoiceOption { index = 1, id = "upgrade", available = false, cost = 9 });

            var actions = UltraCheckpointProtocol.ChoiceActions(observation, UltraActionKind.ChooseRest);

            Assert.That(actions.Count, Is.EqualTo(1));
            Assert.That(actions[0].targetId, Is.EqualTo("heal"));
            Assert.That(observation.choice.options.Length, Is.EqualTo(2),
                "the option must still be observable, with its price");
            Assert.That(observation.choice.options[1].cost, Is.EqualTo(9));
        }

        [Test]
        public void ResolveChoiceIndex_FindsTheChosenOption()
        {
            UltraObservation observation = WithChoice(
                new UltraChoiceOption { index = 0, id = "heal" },
                new UltraChoiceOption { index = 1, id = "upgrade" });
            string actionId = UltraLegalAction.Of(UltraActionKind.ChooseRest, "upgrade", 1).ActionId;

            Assert.That(UltraCheckpointProtocol.ResolveChoiceIndex(
                observation, UltraActionKind.ChooseRest, actionId), Is.EqualTo(1));
        }

        /// <summary>Returning −1 rather than 0 matters: 0 is a valid option index, so a
        /// "not found" that looked like 0 would silently commit the first choice.</summary>
        [Test]
        public void ResolveChoiceIndex_ReturnsMinusOneWhenNothingMatches()
        {
            UltraObservation observation = WithChoice(
                new UltraChoiceOption { index = 0, id = "heal" });

            Assert.That(UltraCheckpointProtocol.ResolveChoiceIndex(
                observation, UltraActionKind.ChooseRest,
                UltraLegalAction.Of(UltraActionKind.ChooseRest, "upgrade", 1).ActionId),
                Is.EqualTo(-1));
            Assert.That(UltraCheckpointProtocol.ResolveChoiceIndex(
                observation, UltraActionKind.ChooseRest, null), Is.EqualTo(-1));
        }

        [Test]
        public void ResolveChoiceIndex_WillNotReturnAnUnavailableOption()
        {
            UltraObservation observation = WithChoice(
                new UltraChoiceOption { index = 0, id = "heal" },
                new UltraChoiceOption { index = 1, id = "upgrade", available = false });

            Assert.That(UltraCheckpointProtocol.ResolveChoiceIndex(
                observation, UltraActionKind.ChooseRest,
                UltraLegalAction.Of(UltraActionKind.ChooseRest, "upgrade", 1).ActionId),
                Is.EqualTo(-1));
        }

        [Test]
        public void TwoOptionsSharingAnId_StayDistinctByIndex()
        {
            UltraObservation observation = WithChoice(
                new UltraChoiceOption { index = 0, id = "百鳴りの共振箱" },
                new UltraChoiceOption { index = 1, id = "百鳴りの共振箱" });

            var actions = UltraCheckpointProtocol.ChoiceActions(observation, UltraActionKind.ChooseReward);

            Assert.That(actions.Count, Is.EqualTo(2),
                "a duplicated drop must not collapse into a single action");
            Assert.That(UltraCheckpointProtocol.ResolveChoiceIndex(
                observation, UltraActionKind.ChooseReward, actions[1].ActionId), Is.EqualTo(1));
        }

        [Test]
        public void NoChoiceOnScreen_MeansNoChoiceActions()
        {
            Assert.That(UltraCheckpointProtocol.ChoiceActions(
                Observation(), UltraActionKind.ChooseRest), Is.Empty);
            Assert.That(UltraCheckpointProtocol.ChoiceActions(
                null, UltraActionKind.ChooseRest), Is.Empty);
        }

        [Test]
        public void TheChoiceIsInsideTheHash()
        {
            UltraObservation a = WithChoice(new UltraChoiceOption { index = 0, id = "heal" });
            UltraObservation b = WithChoice(new UltraChoiceOption { index = 0, id = "upgrade" });

            Assert.That(
                UltraCheckpointProtocol.Create(1, UltraDecisionPoint.RestChoice, b, null).mechanicalHash,
                Is.Not.EqualTo(
                UltraCheckpointProtocol.Create(1, UltraDecisionPoint.RestChoice, a, null).mechanicalHash));
        }

        // ------------------------------------------------------------------
        //  the commit gate
        // ------------------------------------------------------------------

        [Test]
        public void ALegalActionAtTheIssuedEpoch_IsAccepted()
        {
            UltraCheckpoint checkpoint = Checkpoint(epoch: 7);
            string action = checkpoint.legalActions[0].ActionId;

            Assert.That(UltraCheckpointProtocol.TryValidateCommit(
                checkpoint, 7, UltraDecisionPoint.MapNavigation, action, out string failure),
                Is.True, failure);
        }

        [Test]
        public void AnActionFromAPreviousEpoch_IsRefused()
        {
            UltraCheckpoint checkpoint = Checkpoint(epoch: 7);
            string action = checkpoint.legalActions[0].ActionId;

            Assert.That(UltraCheckpointProtocol.TryValidateCommit(
                checkpoint, 8, UltraDecisionPoint.MapNavigation, action, out string failure), Is.False);
            Assert.That(failure, Is.EqualTo(UltraCheckpointProtocol.FailureStaleEpoch));
        }

        [Test]
        public void AnActionForADifferentDecisionPoint_IsRefused()
        {
            UltraCheckpoint checkpoint = Checkpoint(epoch: 7);
            string action = checkpoint.legalActions[0].ActionId;

            Assert.That(UltraCheckpointProtocol.TryValidateCommit(
                checkpoint, 7, UltraDecisionPoint.ShopAction, action, out string failure), Is.False);
            Assert.That(failure, Is.EqualTo(UltraCheckpointProtocol.FailureStalePoint));
        }

        [Test]
        public void AnActionThatWasNeverOffered_IsRefused()
        {
            UltraCheckpoint checkpoint = Checkpoint(epoch: 7);
            string invented = UltraLegalAction.Of(UltraActionKind.MoveTo, "boss").ActionId;

            Assert.That(UltraCheckpointProtocol.TryValidateCommit(
                checkpoint, 7, UltraDecisionPoint.MapNavigation, invented, out string failure), Is.False);
            Assert.That(failure, Is.EqualTo(UltraCheckpointProtocol.FailureIllegalAction));
        }

        /// <summary>A controller that edits the checkpoint to widen its own options is caught
        /// by the hash, not by trust.</summary>
        [Test]
        public void ACheckpointWithAnAddedAction_IsRefused()
        {
            UltraCheckpoint checkpoint = Checkpoint(epoch: 7);
            var widened = new List<UltraLegalAction>(checkpoint.legalActions)
            { UltraLegalAction.Of(UltraActionKind.MoveTo, "boss") };
            checkpoint.legalActions = widened.ToArray();

            Assert.That(UltraCheckpointProtocol.TryValidateCommit(
                checkpoint, 7, UltraDecisionPoint.MapNavigation,
                UltraLegalAction.Of(UltraActionKind.MoveTo, "boss").ActionId, out string failure), Is.False);
            Assert.That(failure, Is.EqualTo(UltraCheckpointProtocol.FailureTampered));
        }

        [Test]
        public void AMissingCheckpoint_IsRefused()
        {
            Assert.That(UltraCheckpointProtocol.TryValidateCommit(
                null, 1, UltraDecisionPoint.MapNavigation, "x", out string failure), Is.False);
            Assert.That(failure, Is.EqualTo(UltraCheckpointProtocol.FailureMissing));
        }

        [Test]
        public void ACheckpointWithNoLegalActions_AcceptsNothing()
        {
            UltraCheckpoint checkpoint = UltraCheckpointProtocol.Create(
                1, UltraDecisionPoint.RunDeclaration, Observation(), null);

            Assert.That(checkpoint.legalActions, Is.Empty);
            Assert.That(UltraCheckpointProtocol.TryValidateCommit(
                checkpoint, 1, UltraDecisionPoint.RunDeclaration, "", out string failure), Is.False);
            Assert.That(failure, Is.EqualTo(UltraCheckpointProtocol.FailureIllegalAction));
        }

        // ------------------------------------------------------------------
        //  registration guards
        // ------------------------------------------------------------------

        /// <summary>Enum values are hashed, so renumbering invalidates stored checkpoints
        /// without any error surfacing. Pin the wire values.</summary>
        [Test]
        public void DecisionPointValues_AreStable()
        {
            Assert.That((int)UltraDecisionPoint.None, Is.EqualTo(0));
            Assert.That((int)UltraDecisionPoint.RunDeclaration, Is.EqualTo(1));
            Assert.That((int)UltraDecisionPoint.MapNavigation, Is.EqualTo(2));
            Assert.That((int)UltraDecisionPoint.RewardChoice, Is.EqualTo(3));
            Assert.That((int)UltraDecisionPoint.EventChoice, Is.EqualTo(4));
            Assert.That((int)UltraDecisionPoint.RestChoice, Is.EqualTo(5));
            Assert.That((int)UltraDecisionPoint.ShopAction, Is.EqualTo(6));
            Assert.That((int)UltraDecisionPoint.CombatTurn, Is.EqualTo(7));
        }

        [Test]
        public void ActionKindValues_AreStable()
        {
            Assert.That((int)UltraActionKind.None, Is.EqualTo(0));
            Assert.That((int)UltraActionKind.DeclareRun, Is.EqualTo(1));
            Assert.That((int)UltraActionKind.MoveTo, Is.EqualTo(2));
            Assert.That((int)UltraActionKind.Pass, Is.EqualTo(13));
        }

        /// <summary>The Phase B "未登録fieldでtest fail" gate, applied to the checkpoint types:
        /// a new field must be consciously added to the hash story before it ships.</summary>
        [Test]
        public void CheckpointFields_AreRegistered()
        {
            var expected = new Dictionary<Type, string[]>
            {
                {
                    typeof(UltraCheckpoint),
                    new[] { "version", "epoch", "point", "observation", "legalActions", "mechanicalHash" }
                },
                {
                    typeof(UltraLegalAction),
                    new[] { "kind", "targetId", "index", "label" }
                },
            };

            foreach (var pair in expected)
            {
                string[] actual = pair.Key
                    .GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Select(f => f.Name)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToArray();

                Assert.That(actual, Is.EqualTo(pair.Value.OrderBy(n => n, StringComparer.Ordinal).ToArray()),
                    pair.Key.Name + " gained or lost a field. Decide how it participates in "
                    + "ComputeMechanicalHash and in TryValidateCommit, then update this list.");
            }
        }
    }
}
#endif

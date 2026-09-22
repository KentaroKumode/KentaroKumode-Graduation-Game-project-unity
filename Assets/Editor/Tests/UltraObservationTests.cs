#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutoTest.Ultra;
using MapSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    /// <summary>
    /// Guards the Ultra public-observation boundary (handoff §10 Phase A).
    ///
    /// <para>The threat these tests address is not a wrong value, it is a <b>silently widened
    /// boundary</b>: someone adds a field to <c>RunState</c> or <c>MapNode</c>, a helpful
    /// projection copies it, and the controller quietly starts reading something the player
    /// cannot see. Nothing else in the codebase would fail. So the triage list below is
    /// mandatory ── a new field fails the suite until a human classifies it.</para>
    /// </summary>
    [TestFixture]
    public sealed class UltraObservationTests
    {
        /// <summary>RunState fields the player can see on screen.
        ///
        /// <para><b>Adding a field to RunState will fail this test.</b> That is the intended
        /// behaviour: decide whether the player can see it, then add it to the matching set.
        /// Do not "fix" the failure by deleting the assertion.</para>
        ///
        /// <para>"Visible" is not the same as "surfaced" ── see
        /// <see cref="SurfacedRunStateFields"/>. This set is the ceiling on what the
        /// observation is ever allowed to carry; the observation currently carries less,
        /// and grows as later phases need it.</para>
        ///
        /// <para>Note on the <c>pending*</c> family: every one of them is an effect the
        /// player armed by consuming an item (鑑定の眼鏡 / 加速の粉 / 奇襲 / 賭博師 …). Knowing
        /// "I used a thing and it is still queued" is not future knowledge ── the player used
        /// it. They are visible.</para></summary>
        private static readonly HashSet<string> PlayerVisibleFields = new HashSet<string>(StringComparer.Ordinal)
        {
            // progress / resources
            "currentFloor", "maxFloor", "normalClearFloor", "bossDefeatedThisFloor",
            "playerHP", "playerMaxHP", "coins", "coinsSpent", "weaponMaterials",
            "hope", "hopeCap", "crownHopeLocked", "tilesThisFloor", "isRunActive",
            "totalBattles", "totalWins", "totalTurns", "totalCombatTurns", "combatStartHP",
            "baseMaxHPAtRunStart",
            // build / equipment
            "equippedWeaponId", "equippedDiceId", "equippedSpecialTerminalId", "weaponPlus",
            "playerClass", "limitBreakStage", "sublimationCount",
            "ownedPassiveItems", "ownedConsumables", "ownedFlags",
            "ascendedPassiveIds", "diceFaceParts",
            "diceEnhanceKeys", "diceEnhanceLevels", "diceEnhanceValues",
            // knowledge the player has accumulated
            "seenOnceEvents", "seenPassiveItemIds", "shopPurchasedCounts",
            // status / debuffs
            "timedBuffs", "timedDebuffs", "permanentDebuffs", "gateFlaws", "lambdaDebuffs",
            "sealedRoleMask", "lastStandActive", "madnessStack", "madnessMoveCounter",
            "breakdownCount", "lingeringWoundLost", "lingeringWoundTriggers",
            "lingeringWoundDamageAccum",
            // world / progression
            "convictionStage", "layer6Unlocked", "layer7Unlocked", "lastBossId",
            "defeatedSaintGeorges", "lostAtLayer7", "riftLeftOpen",
            "philStoneUsed", "outpostUpgradeUsedThisFloor",
            // shop
            "shopRobberyDone", "shopRobberyInProgress", "robberyPendingItems", "nextShopHalfPrice",
            // Λ layer
            "inLambda", "dimensionalDisturbance", "activePhenomena",
            "reverseFallsUsed", "eclipsedNightTriggered", "ironSunNextTurn",
            "lambdaRingsEntered", "lambdaRingElite", "lambdaRingEvent",
            "lambdaRingEventHeal", "lambdaRingEventItem", "lambdaCombatsFinished",
            "lambdaItemsAcquiredGross",
            // armed consumable effects (the player spent an item for each of these)
            "nextLootMinRarity", "pendingFirstRollTotal", "pendingEnemyStartHpCutPct",
            "pendingGamblerDice", "pendingDaggerArmed", "pendingOathArmed",
            "pendingPolishArmed", "pendingRinkaiCarryover",
            "pendingConsAtkBurst", "pendingConsCrit", "pendingConsCritBattles",
            "pendingConsDiceRoll", "pendingConsDiceRollBattles", "pendingConsDiceRollTurns",
            "pendingConsDmgMultPct", "pendingConsDmgMultTurns", "pendingConsEnemyDiceDebuff",
            "pendingConsFlatReduce", "pendingConsFlatReduceBattles", "pendingConsReflect",
            "pendingConsRegen", "pendingConsShield", "pendingConsShieldTurns",
        };

        /// <summary>Fields that must never reach the observation.
        ///
        /// <para><b>Read from production code, not restated here.</b> The same list drives the
        /// resume veil (<see cref="UltraSnapshotVeil.ApplyToRun"/>). A second copy in the test
        /// would let the observation stay airtight while the same facts rode into rollouts —
        /// which is the leak shape this whole area exists to prevent.</para></summary>
        private static readonly Dictionary<string, string> HiddenRunStateFields = BuildHidden();

        private static Dictionary<string, string> BuildHidden()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in UltraRunStateClassification.Hidden) map[pair.Key] = pair.Value;
            return map;
        }

        /// <summary>The subset actually carried by <see cref="UltraRunView"/> today.
        /// Must be a subset of <see cref="PlayerVisibleFields"/> ── that containment is the
        /// property that keeps hidden state out.</summary>
        private static readonly HashSet<string> SurfacedRunStateFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "currentFloor", "maxFloor", "normalClearFloor", "bossDefeatedThisFloor",
            "playerHP", "playerMaxHP", "coins", "weaponMaterials", "tilesThisFloor",
            "totalBattles", "totalWins", "totalTurns", "totalCombatTurns",
            "inLambda", "dimensionalDisturbance", "convictionStage",
            "layer6Unlocked", "layer7Unlocked", "shopRobberyDone", "lastStandActive",
            "sublimationCount", "sealedRoleMask",
            "lingeringWoundLost", "equippedWeaponId", "equippedDiceId",
            "ownedPassiveItems", "ownedConsumables", "permanentDebuffs",
            "activePhenomena", "timedBuffs", "timedDebuffs", "lambdaDebuffs",
            "diceFaceParts",
        };

        private static IEnumerable<string> RunStatePublicFieldNames()
        {
            return typeof(GameLoop.RunState)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(f => f.Name);
        }

        /// <summary>The triage guard. Fails on any RunState field nobody has classified.</summary>
        [Test]
        public void EveryRunStateField_IsClassifiedAsVisibleOrHidden()
        {
            var unclassified = RunStatePublicFieldNames()
                .Where(n => !PlayerVisibleFields.Contains(n) && !HiddenRunStateFields.ContainsKey(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.That(unclassified, Is.Empty,
                "RunState gained field(s) that no one classified for the Ultra observation "
                + "boundary: " + string.Join(", ", unclassified) + ". Decide whether the player "
                + "can see each one, then add it to PlayerVisibleFields or HiddenRunStateFields "
                + "(with a reason). Never widen the boundary by accident.");
        }

        /// <summary>The safety property: nothing the observation carries may be hidden state.
        /// Everything else in this fixture supports this one assertion.</summary>
        [Test]
        public void EverythingSurfaced_IsClassifiedAsPlayerVisible()
        {
            var leaked = SurfacedRunStateFields
                .Where(n => !PlayerVisibleFields.Contains(n) || HiddenRunStateFields.ContainsKey(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.That(leaked, Is.Empty,
                "the observation surfaces field(s) that are not classified as player-visible: "
                + string.Join(", ", leaked));
        }

        /// <summary>Catches the reverse drift: a name left in a list after the field is gone,
        /// which would make the triage quietly stop covering anything.</summary>
        [Test]
        public void ClassificationLists_DoNotNameFieldsThatNoLongerExist()
        {
            var live = new HashSet<string>(RunStatePublicFieldNames(), StringComparer.Ordinal);
            var stale = PlayerVisibleFields
                .Concat(HiddenRunStateFields.Keys)
                .Concat(SurfacedRunStateFields)
                .Distinct()
                .Where(n => !live.Contains(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.That(stale, Is.Empty,
                "these names are classified but no longer exist on RunState: "
                + string.Join(", ", stale));
        }

        [Test]
        public void Observation_ExposesNoFieldNamedLikeHiddenState()
        {
            string[] forbidden = { "seed", "rng", "random", "falsemerchant", "resolvedtype", "chronicle" };

            foreach (Type type in new[]
            {
                typeof(UltraObservation), typeof(UltraRunView),
                typeof(UltraMapView), typeof(UltraMapNodeView),
            })
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    string lower = field.Name.ToLowerInvariant();
                    foreach (string bad in forbidden)
                        Assert.That(lower.Contains(bad), Is.False,
                            type.Name + "." + field.Name + " looks like hidden state");
                }
            }
        }

        // ------------------------------------------------------------------
        //  reveal rule
        // ------------------------------------------------------------------

        private static MapNode Node(string id, int row, int lane, TileType type, bool revealed)
        {
            return new MapNode(id, row, lane, type) { revealed = revealed };
        }

        [Test]
        public void UnrevealedTile_ReportsUnknown_NotItsRealType()
        {
            var map = new FloorMap { floor = 3, rowCount = 4, bossNodeId = "b" };
            map.AddNode(Node("hidden", 1, 0, TileType.Boss, revealed: false));
            map.AddNode(Node("seen", 1, 1, TileType.Shop, revealed: true));

            UltraObservation observation = CaptureMapOnly(map, currentNodeId: null);

            UltraMapNodeView hidden = observation.map.nodes.Single(n => n.id == "hidden");
            UltraMapNodeView seen = observation.map.nodes.Single(n => n.id == "seen");

            Assert.That(hidden.view, Is.EqualTo(UltraTileView.Unknown));
            Assert.That(hidden.revealed, Is.False);
            Assert.That(seen.view, Is.EqualTo(UltraTileView.Shop),
                "a revealed tile must still report its type");
        }

        /// <summary>The specific leak that motivated this: a Shop the generator secretly marked
        /// as a false merchant is <b>revealed</b>, so a naive "copy revealed tiles" projection
        /// would carry the flag straight through.</summary>
        [Test]
        public void FalseMerchantShop_IsIndistinguishableFromARealShop()
        {
            var map = new FloorMap { floor = 5, rowCount = 4 };
            map.AddNode(Node("real", 1, 0, TileType.Shop, revealed: true));
            MapNode fake = Node("fake", 1, 1, TileType.Shop, revealed: true);
            fake.isFalseMerchant = true;
            map.AddNode(fake);

            UltraObservation observation = CaptureMapOnly(map, currentNodeId: null);

            UltraMapNodeView a = observation.map.nodes.Single(n => n.id == "real");
            UltraMapNodeView b = observation.map.nodes.Single(n => n.id == "fake");

            Assert.That(b.view, Is.EqualTo(a.view));
            Assert.That(b.revealed, Is.EqualTo(a.revealed));
            Assert.That(typeof(UltraMapNodeView).GetFields()
                    .Any(f => f.Name.IndexOf("merchant", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.False, "the view type must have nowhere to put the flag");
        }

        [Test]
        public void EveryTileType_MapsToADistinctView_SoNothingCollapsesIntoOther()
        {
            MethodInfo classify = typeof(UltraObservationBuilder)
                .GetMethod("Classify", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(classify, Is.Not.Null);

            var seen = new Dictionary<UltraTileView, TileType>();
            foreach (TileType type in Enum.GetValues(typeof(TileType)))
            {
                var view = (UltraTileView)classify.Invoke(null, new object[] { type });
                Assert.That(view, Is.Not.EqualTo(UltraTileView.Other),
                    type + " has no view mapping — it would be indistinguishable from other "
                    + "unmapped tiles, which hides information the player actually has");
                Assert.That(view, Is.Not.EqualTo(UltraTileView.Unknown),
                    type + " must not map onto the unrevealed marker");
                Assert.That(seen.ContainsKey(view), Is.False,
                    type + " and " + (seen.ContainsKey(view) ? seen[view].ToString() : "?")
                    + " share view " + view);
                seen[view] = type;
            }
        }

        [Test]
        public void Capture_IsStableForTheSameState()
        {
            var map = new FloorMap { floor = 2, rowCount = 3, bossNodeId = "b" };
            map.AddNode(Node("a", 0, 0, TileType.Outpost, revealed: true));
            map.AddNode(Node("b", 1, 0, TileType.Boss, revealed: true));

            UltraObservation first = CaptureMapOnly(map, currentNodeId: null);
            UltraObservation second = CaptureMapOnly(map, currentNodeId: null);

            Assert.That(UnityEngine.JsonUtility.ToJson(second),
                Is.EqualTo(UnityEngine.JsonUtility.ToJson(first)),
                "the same state must produce the same observation, or checkpoint comparison "
                + "and deduplication become unreliable");
        }

        [Test]
        public void Capture_ToleratesMissingRunAndMap()
        {
            UltraObservation observation = UltraObservationBuilder.Capture(null, null, "Boot", 0);

            Assert.That(observation, Is.Not.Null);
            Assert.That(observation.version, Is.EqualTo(UltraObservation.CurrentVersion));
            Assert.That(observation.map.nodes, Is.Empty);
            Assert.That(observation.run.ownedConsumables, Is.Empty);
        }

        [Test]
        public void DeliberateExclusions_AreDocumentedWithReasons()
        {
            Assert.That(UltraObservation.DeliberateExclusions, Is.Not.Empty);
            foreach (var pair in UltraObservation.DeliberateExclusions)
            {
                Assert.That(pair.Key, Is.Not.Null.And.Not.Empty);
                Assert.That(pair.Value, Is.Not.Null.And.Not.Empty,
                    pair.Key + " is excluded without a stated reason");
            }
        }

        /// <summary>Exercises the map path without a live <c>MapManager</c> singleton by
        /// invoking the private filler directly ── the reveal rule is what is under test, not
        /// Unity's component lifecycle.</summary>
        private static UltraObservation CaptureMapOnly(FloorMap floor, string currentNodeId)
        {
            var observation = new UltraObservation { phase = "Test" };
            MethodInfo fill = typeof(UltraObservationBuilder)
                .GetMethod("FillMapFrom", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(fill, Is.Not.Null,
                "UltraObservationBuilder must expose a MapManager-free filler for testing");
            fill.Invoke(null, new object[]
            {
                observation.map, floor, currentNodeId, new HashSet<string>(),
            });
            return observation;
        }
    }
}
#endif

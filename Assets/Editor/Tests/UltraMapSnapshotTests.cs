#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using AutoTest.Ultra;
using MapSystem;
using NUnit.Framework;
using UnityEngine;

namespace AutoTest.EditorTests.Ultra
{
    /// <summary>
    /// Map capture/restore. Together with <see cref="UltraRunSnapshotTests"/> this is what
    /// lets a worker resume from mid-run rather than only from a run start.
    /// </summary>
    [TestFixture]
    public sealed class UltraMapSnapshotTests
    {
        private static MapNode Node(string id, int row, int lane, TileType type,
                                    bool revealed = true, bool visited = false)
        {
            return new MapNode(id, row, lane, type) { revealed = revealed, visited = visited };
        }

        private static FloorMap Board()
        {
            var map = new FloorMap
            {
                floor = 5,
                laneCount = 3,
                rowCount = 4,
                startNodeId = "n0",
                bossNodeId = "boss",
            };
            MapNode start = Node("n0", 0, 1, TileType.Outpost, visited: true);
            start.connections.Add("shop");
            start.connections.Add("fight");
            map.AddNode(start);

            MapNode shop = Node("shop", 1, 0, TileType.Shop);
            shop.isFalseMerchant = true;               // hidden from the player, real on the board
            shop.connections.Add("boss");
            map.AddNode(shop);

            MapNode fight = Node("fight", 1, 2, TileType.Battle, revealed: false);
            fight.connections.Add("boss");
            map.AddNode(fight);

            MapNode mystery = Node("myst", 2, 1, TileType.Mystery);
            mystery.resolvedType = TileType.Treasure;  // already stepped on
            mystery.activated = true;
            map.AddNode(mystery);

            map.AddNode(Node("boss", 3, -1, TileType.Boss));
            return map;
        }

        private static MapManager Manager(FloorMap map, string currentNodeId,
                                          int hungerCurrent = 4, int hungerMax = 6)
        {
            var go = new GameObject("[Ultra map snapshot test]");
            var manager = go.AddComponent<MapManager>();
            manager.RestoreState(map, currentNodeId, hungerCurrent, hungerMax);
            return manager;
        }

        private static void Destroy(MapManager manager)
        {
            if (manager != null) UnityEngine.Object.DestroyImmediate(manager.gameObject);
        }

        [Test]
        public void ARoundTripReproducesTheBoard()
        {
            MapManager source = Manager(Board(), "shop");
            MapManager target = null;
            try
            {
                UltraMapSnapshot snapshot = UltraMapSnapshot.Capture(source);
                target = Manager(snapshot.BuildMap(), snapshot.currentNodeId,
                                 snapshot.hungerCurrent, snapshot.hungerMax);

                Assert.That(UltraMapSnapshot.Capture(target).ContentHash(),
                    Is.EqualTo(snapshot.ContentHash()));
                Assert.That(target.CurrentNode.id, Is.EqualTo("shop"));
                Assert.That(target.Hunger.Current, Is.EqualTo(4));
                Assert.That(target.Hunger.Max, Is.EqualTo(6));
            }
            finally { Destroy(source); Destroy(target); }
        }

        /// <summary>The distinction that makes snapshot and observation different objects:
        /// a replayed board must keep the false merchant, or the worker plays a different
        /// game than the one being evaluated.</summary>
        [Test]
        public void TheFalseMerchantSurvivesRestore_EvenThoughTheObservationHidesIt()
        {
            MapManager source = Manager(Board(), "n0");
            try
            {
                UltraMapSnapshot snapshot = UltraMapSnapshot.Capture(source);
                FloorMap rebuilt = snapshot.BuildMap();

                Assert.That(rebuilt.GetNode("shop").isFalseMerchant, Is.True,
                    "dropping it would turn the trap into a real shop");
                Assert.That(typeof(UltraMapNodeView).GetFields()
                        .Any(f => f.Name.IndexOf("merchant", StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False, "the observation must still have nowhere to put it");
            }
            finally { Destroy(source); }
        }

        [Test]
        public void RevealAndVisitStateSurvive()
        {
            MapManager source = Manager(Board(), "n0");
            try
            {
                FloorMap rebuilt = UltraMapSnapshot.Capture(source).BuildMap();

                Assert.That(rebuilt.GetNode("fight").revealed, Is.False);
                Assert.That(rebuilt.GetNode("shop").revealed, Is.True);
                Assert.That(rebuilt.GetNode("n0").visited, Is.True);
                Assert.That(rebuilt.GetNode("shop").visited, Is.False);
            }
            finally { Destroy(source); }
        }

        /// <summary>An unresolved Mystery and one already resolved to Treasure are different
        /// boards; −1 has to mean "not yet", not "zero".</summary>
        [Test]
        public void ResolvedMysteryIsDistinctFromUnresolved()
        {
            MapManager source = Manager(Board(), "n0");
            try
            {
                FloorMap rebuilt = UltraMapSnapshot.Capture(source).BuildMap();

                Assert.That(rebuilt.GetNode("myst").resolvedType, Is.EqualTo(TileType.Treasure));
                Assert.That(rebuilt.GetNode("myst").activated, Is.True);
                Assert.That(rebuilt.GetNode("shop").resolvedType, Is.Null,
                    "an unresolved tile must restore as unresolved, not as TileType 0");
                Assert.That(rebuilt.GetNode("shop").EffectiveType, Is.EqualTo(TileType.Shop));
            }
            finally { Destroy(source); }
        }

        [Test]
        public void ConnectionsSurvive()
        {
            MapManager source = Manager(Board(), "n0");
            try
            {
                FloorMap rebuilt = UltraMapSnapshot.Capture(source).BuildMap();

                Assert.That(rebuilt.GetNode("n0").connections, Is.EquivalentTo(new[] { "shop", "fight" }));
                Assert.That(rebuilt.GetNode("boss").connections, Is.Empty);
            }
            finally { Destroy(source); }
        }

        [Test]
        public void TheHashIsStableAndSensitive()
        {
            MapManager source = Manager(Board(), "n0");
            try
            {
                UltraMapSnapshot a = UltraMapSnapshot.Capture(source);
                UltraMapSnapshot b = UltraMapSnapshot.Capture(source);
                Assert.That(b.ContentHash(), Is.EqualTo(a.ContentHash()));

                source.CurrentMap.GetNode("fight").revealed = true;
                Assert.That(UltraMapSnapshot.Capture(source).ContentHash(),
                    Is.Not.EqualTo(a.ContentHash()), "reveal state must be inside the hash");
            }
            finally { Destroy(source); }
        }

        [Test]
        public void MovingChangesTheHash()
        {
            MapManager source = Manager(Board(), "n0");
            try
            {
                string before = UltraMapSnapshot.Capture(source).ContentHash();
                source.MoveTo("shop", 100);

                Assert.That(UltraMapSnapshot.Capture(source).ContentHash(), Is.Not.EqualTo(before));
            }
            finally { Destroy(source); }
        }

        [Test]
        public void TheSnapshotSurvivesJsonTransport()
        {
            MapManager source = Manager(Board(), "shop");
            try
            {
                UltraMapSnapshot sent = UltraMapSnapshot.Capture(source);
                var received = JsonUtility.FromJson<UltraMapSnapshot>(JsonUtility.ToJson(sent));

                Assert.That(received.ContentHash(), Is.EqualTo(sent.ContentHash()));
                Assert.That(received.BuildMap().GetNode("shop").isFalseMerchant, Is.True);
            }
            finally { Destroy(source); }
        }

        /// <summary>Restoring must not re-derive visibility from the current debuff state —
        /// the snapshot records what the player could see when it was taken.</summary>
        [Test]
        public void RestoreDoesNotRecomputeSight()
        {
            MapManager source = Manager(Board(), "n0");
            MapManager target = null;
            try
            {
                UltraMapSnapshot snapshot = UltraMapSnapshot.Capture(source);
                target = Manager(snapshot.BuildMap(), snapshot.currentNodeId,
                                 snapshot.hungerCurrent, snapshot.hungerMax);

                Assert.That(target.CurrentMap.GetNode("fight").revealed, Is.False,
                    "a tile recorded as unseen must stay unseen after restore");
            }
            finally { Destroy(source); Destroy(target); }
        }

        [Test]
        public void RestoringAnUnknownCurrentNodeFallsBackToTheStart()
        {
            MapManager manager = null;
            try
            {
                manager = Manager(Board(), "does-not-exist");
                Assert.That(manager.CurrentNode.id, Is.EqualTo("n0"));
            }
            finally { Destroy(manager); }
        }

        [Test]
        public void CapturingNothingIsAnError()
        {
            Assert.Throws<ArgumentNullException>(() => UltraMapSnapshot.Capture(null));

            var go = new GameObject("[Ultra empty map manager]");
            try
            {
                var empty = go.AddComponent<MapManager>();
                Assert.Throws<InvalidOperationException>(() => UltraMapSnapshot.Capture(empty));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void AWrongVersionIsRefused()
        {
            MapManager source = Manager(Board(), "n0");
            try
            {
                UltraMapSnapshot snapshot = UltraMapSnapshot.Capture(source);
                snapshot.version = UltraMapSnapshot.CurrentVersion + 1;

                Assert.Throws<InvalidOperationException>(() => snapshot.BuildMap());
            }
            finally { Destroy(source); }
        }

        /// <summary>Same registration guard as the other snapshot types: a new MapNode field
        /// must be consciously captured or consciously excluded.</summary>
        [Test]
        public void MapNodeFields_AreAllAccountedFor()
        {
            string[] expected =
            {
                "id", "row", "lane", "type", "resolvedType", "visited", "activated",
                "isFalseMerchant", "revealed", "connections",
            };
            string[] actual = typeof(MapNode)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(f => f.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.That(actual, Is.EqualTo(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray()),
                "MapNode changed. Decide whether the new field belongs in UltraMapNodeSnapshot "
                + "(ground truth) and/or UltraMapNodeView (what the player sees) — they are "
                + "different questions with different answers.");
        }
    }
}
#endif

#if UNITY_EDITOR
using AutoTest.Ultra;
using MapSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    /// <summary>
    /// The veil closes an information leak that the observation boundary cannot see.
    ///
    /// <para>Everything the controller is <em>shown</em> is public, and that is not enough: a
    /// rollout evaluator learns hidden facts from outcomes. Roll out into the fake merchant and
    /// the loss comes back as a number — the controller now knows, without anything having been
    /// disclosed. These tests pin the re-draw and, just as importantly, pin that the leaks
    /// still open are counted rather than quietly tolerated.</para>
    /// </summary>
    [TestFixture]
    public sealed class UltraSnapshotVeilTests
    {
        private static UltraMapSnapshot Board(int floor = 5)
        {
            return new UltraMapSnapshot
            {
                floor = floor,
                nodes = new[]
                {
                    new UltraMapNodeSnapshot
                    { id = "shopA", row = 2, type = (int)TileType.Shop, isFalseMerchant = true, revealed = true },
                    new UltraMapNodeSnapshot
                    { id = "shopB", row = 3, type = (int)TileType.Shop, isFalseMerchant = false, revealed = true },
                    new UltraMapNodeSnapshot
                    { id = "seenShop", row = 3, type = (int)TileType.Shop, isFalseMerchant = true, visited = true, revealed = true },
                    // Row 5 is a randomised row, so an unseen tile there is genuinely unknown.
                    new UltraMapNodeSnapshot
                    { id = "fog", row = 5, type = (int)TileType.Treasure, revealed = false },
                    // Row 9 is the guaranteed rest row: structural, known even under fog.
                    new UltraMapNodeSnapshot
                    { id = "restFog", row = 9, type = (int)TileType.Rest, revealed = false },
                    new UltraMapNodeSnapshot
                    { id = "myst", row = 4, type = (int)TileType.Mystery, resolvedType = (int)TileType.Treasure, revealed = true },
                },
            };
        }

        [Test]
        public void UnvisitedShops_AreRedrawn()
        {
            UltraMapSnapshot snapshot = Board();
            UltraVeilReport report = UltraSnapshotVeil.Apply(snapshot, seed: 1, falseMerchantChance: 0f);

            Assert.That(report.shopsRedrawn, Is.EqualTo(2), "shopA and shopB, not the visited one");
            Assert.That(snapshot.nodes[0].isFalseMerchant, Is.False,
                "with chance 0 every re-drawn shop must come back genuine");
            Assert.That(report.falseMerchantsBefore, Is.EqualTo(2));
        }

        /// <summary>Once the player has stepped on a shop they know what it was; re-drawing
        /// would erase knowledge they legitimately hold.</summary>
        [Test]
        public void AVisitedShopKeepsItsIdentity()
        {
            UltraMapSnapshot snapshot = Board();
            UltraSnapshotVeil.Apply(snapshot, seed: 1, falseMerchantChance: 0f);

            Assert.That(snapshot.nodes[2].isFalseMerchant, Is.True);
        }

        [Test]
        public void WithCertaintyEveryUnvisitedShopBecomesFake()
        {
            UltraMapSnapshot snapshot = Board();
            UltraVeilReport report = UltraSnapshotVeil.Apply(snapshot, seed: 7, falseMerchantChance: 1f);

            Assert.That(snapshot.nodes[0].isFalseMerchant, Is.True);
            Assert.That(snapshot.nodes[1].isFalseMerchant, Is.True);
            Assert.That(report.falseMerchantsAfter, Is.EqualTo(3), "including the visited one");
        }

        [Test]
        public void TheVeilIsDeterministicForASeed()
        {
            UltraMapSnapshot a = Board();
            UltraMapSnapshot b = Board();
            UltraSnapshotVeil.Apply(a, seed: 42, falseMerchantChance: 0.3f);
            UltraSnapshotVeil.Apply(b, seed: 42, falseMerchantChance: 0.3f);

            Assert.That(b.ContentHash(), Is.EqualTo(a.ContentHash()));
        }

        [Test]
        public void DifferentSeedsSampleDifferentBoards()
        {
            bool sawDifference = false;
            UltraMapSnapshot reference = Board();
            UltraSnapshotVeil.Apply(reference, seed: 1, falseMerchantChance: 0.5f);

            for (int seed = 2; seed < 30 && !sawDifference; seed++)
            {
                UltraMapSnapshot other = Board();
                UltraSnapshotVeil.Apply(other, seed, falseMerchantChance: 0.5f);
                sawDifference = other.ContentHash() != reference.ContentHash();
            }
            Assert.That(sawDifference, Is.True,
                "a veil that always produces the same board is not sampling a belief");
        }

        /// <summary>Below floor 3 the generator never places a false merchant, so the veil must
        /// not invent one.</summary>
        [Test]
        public void EarlyFloorsAreNotTouched()
        {
            UltraMapSnapshot snapshot = Board(floor: 2);
            UltraVeilReport report = UltraSnapshotVeil.Apply(snapshot, seed: 3, falseMerchantChance: 1f);

            Assert.That(report.shopsRedrawn, Is.EqualTo(0));
            Assert.That(snapshot.nodes[1].isFalseMerchant, Is.False);
        }

        // ------------------------------------------------------------------
        //  the leaks that remain
        // ------------------------------------------------------------------

        /// <summary>The leak this closes: a tile the player has not seen must not keep its real
        /// type, or a rollout that walks onto it returns the truth.</summary>
        [Test]
        public void AnUnseenTileOnARandomisedRow_IsRedrawn()
        {
            bool sawADifferentType = false;
            for (int seed = 1; seed < 40 && !sawADifferentType; seed++)
            {
                UltraMapSnapshot snapshot = Board();
                UltraVeilReport report = UltraSnapshotVeil.Apply(snapshot, seed, 0f);

                Assert.That(report.unrevealedTilesRedrawn, Is.EqualTo(1));
                UltraMapNodeSnapshot fog = System.Array.Find(snapshot.nodes, n => n.id == "fog");
                if (fog.type != (int)TileType.Treasure) sawADifferentType = true;
            }
            Assert.That(sawADifferentType, Is.True,
                "Treasure is 3/87 of the weight, so 40 draws that all return Treasure means "
                + "the tile is not actually being re-drawn");
        }

        /// <summary>Structural rows are not a leak. The outpost, the guaranteed shop row, the
        /// rest row and the boss are known from the floor layout whether or not fog covers
        /// them, so re-drawing them would remove knowledge rather than protect it.</summary>
        [Test]
        public void AStructuralRowIsLeftAloneAndIsNotCountedAsALeak()
        {
            UltraMapSnapshot snapshot = Board();
            UltraVeilReport report = UltraSnapshotVeil.Apply(snapshot, seed: 1, falseMerchantChance: 0f);

            UltraMapNodeSnapshot rest = System.Array.Find(snapshot.nodes, n => n.id == "restFog");
            Assert.That(rest.type, Is.EqualTo((int)TileType.Rest));
            Assert.That(report.unrevealedTilesLeftIntact, Is.EqualTo(1));
        }

        [Test]
        public void ARedrawnTileNeverKeepsAStaleMysteryResolution()
        {
            for (int seed = 1; seed < 10; seed++)
            {
                UltraMapSnapshot snapshot = Board();
                UltraSnapshotVeil.Apply(snapshot, seed, 0.3f);
                UltraMapNodeSnapshot fog = System.Array.Find(snapshot.nodes, n => n.id == "fog");
                Assert.That(fog.resolvedType, Is.EqualTo(-1));
            }
        }

        /// <summary>The weights must come from the generator, not from a copy that drifts.</summary>
        [Test]
        public void TheRedrawUsesTheGeneratorsOwnWeights()
        {
            // 層ごとに表が違う (4 層だけ精鋭が半分・2026-09-21)。 Board() は 5 層なので 5 層の表で見る。
            Assert.That(MapSystem.MapGenerator.RandomRowTileWeights(5), Is.Not.Null.And.Not.Empty);

            var counts = new System.Collections.Generic.Dictionary<int, int>();
            for (int seed = 0; seed < 400; seed++)
            {
                UltraMapSnapshot snapshot = Board();
                UltraSnapshotVeil.Apply(snapshot, seed, 0f);
                int type = System.Array.Find(snapshot.nodes, n => n.id == "fog").type;
                counts.TryGetValue(type, out int n);
                counts[type] = n + 1;
            }

            // Battle carries 29 of 87 weight; anything near that dominance is only possible if
            // the real table is in use.
            counts.TryGetValue((int)TileType.Battle, out int battles);
            Assert.That(battles, Is.GreaterThan(80).And.LessThan(220),
                "observed Battle share is inconsistent with the generator's weights");
            Assert.That(counts.ContainsKey((int)TileType.Boss), Is.False,
                "Boss is not in the randomised pool and must never be drawn");
        }

        [Test]
        public void AResolvedButUnsteppedMystery_IsReportedAsAKnownLeak()
        {
            UltraVeilReport report = UltraSnapshotVeil.Apply(Board(), seed: 1, falseMerchantChance: 0f);

            Assert.That(report.unsteppedResolvedMysteries, Is.EqualTo(1));
        }

        [Test]
        public void ABoardWithNothingHiddenReportsNoLeak()
        {
            var clean = new UltraMapSnapshot
            {
                floor = 5,
                nodes = new[]
                {
                    new UltraMapNodeSnapshot { id = "a", row = 2, type = (int)TileType.Battle, revealed = true },
                },
            };

            UltraVeilReport report = UltraSnapshotVeil.Apply(clean, seed: 1, falseMerchantChance: 0f);

            Assert.That(report.HasKnownLeak, Is.False);
            StringAssert.Contains("未veil なし", report.Describe());
        }

        [Test]
        public void Stats_TrackHowOftenRolloutsRanOverALeakyBoard()
        {
            UltraVeilStats.Reset();
            UltraVeilStats.Note(UltraSnapshotVeil.Apply(Board(), 1, 0f));   // has the ? leak
            UltraVeilStats.Note(UltraSnapshotVeil.Apply(
                new UltraMapSnapshot { floor = 5, nodes = new UltraMapNodeSnapshot[0] }, 1, 0f));

            Assert.That(UltraVeilStats.Applied, Is.EqualTo(2));
            Assert.That(UltraVeilStats.WithKnownLeak, Is.EqualTo(1));
            Assert.That(UltraVeilStats.TilesRedrawn, Is.EqualTo(1));
            StringAssert.Contains("50.0%", UltraVeilStats.Describe());
        }

        // ------------------------------------------------------------------
        //  the run half
        // ------------------------------------------------------------------

        /// <summary>The map veil closing its leak while the run half stayed open is the exact
        /// failure this guards: the resume snapshot carries every RunState field, so anything
        /// classified hidden would otherwise ride into the rollout.</summary>
        [Test]
        public void HiddenRunFieldsAreNeutralisedBeforeARollout()
        {
            var run = new GameLoop.RunState { coins = 42 };
            run.lambdaPoolRemainingAll = 17;
            run.lambdaPoolRemainingFloored = 5;
            run.judgmentPostDamage = 99;
            run.chronicle.Add("secret note");

            UltraRunSnapshot snapshot = UltraRunSnapshot.Capture(run);
            int neutralised = UltraSnapshotVeil.ApplyToRun(snapshot);

            var restored = new GameLoop.RunState();
            snapshot.RestoreInto(restored);

            Assert.That(neutralised, Is.GreaterThan(0));
            Assert.That(restored.lambdaPoolRemainingAll, Is.EqualTo(-1),
                "-1 is the game's own 'not yet counted' marker, so the run recomputes it");
            Assert.That(restored.lambdaPoolRemainingFloored, Is.EqualTo(-1));
            Assert.That(restored.judgmentPostDamage, Is.EqualTo(0));
            Assert.That(restored.chronicle, Is.Empty);
            Assert.That(restored.coins, Is.EqualTo(42), "public fields must survive untouched");
        }

        [Test]
        public void EveryHiddenFieldHasANeutralValue()
        {
            var run = new GameLoop.RunState();
            UltraRunSnapshot snapshot = UltraRunSnapshot.Capture(run);

            Assert.DoesNotThrow(() => UltraSnapshotVeil.ApplyToRun(snapshot),
                "a hidden field of an unhandled kind must be caught here, not in a batch");
        }

        /// <summary>The observation whitelist and the resume veil must be driven by the same
        /// list, or one can be tightened while the other silently is not.</summary>
        [Test]
        public void TheClassificationNamesOnlyLiveFields()
        {
            Assert.That(UltraRunStateClassification.StaleHiddenNames(), Is.Empty);
            Assert.That(UltraRunStateClassification.IsHidden("chronicle"), Is.True);
            Assert.That(UltraRunStateClassification.IsHidden("coins"), Is.False);
        }

        [Test]
        public void EveryHiddenFieldStatesWhyItIsHidden()
        {
            foreach (var pair in UltraRunStateClassification.Hidden)
                Assert.That(pair.Value, Is.Not.Null.And.Not.Empty, pair.Key);
        }

        [Test]
        public void AnEmptySnapshotIsHarmless()
        {
            Assert.That(UltraSnapshotVeil.Apply(null, 1, 0.3f).HasKnownLeak, Is.False);
            StringAssert.Contains("適用なし", VeilStatsAfterReset());
        }

        private static string VeilStatsAfterReset()
        {
            UltraVeilStats.Reset();
            return UltraVeilStats.Describe();
        }
    }
}
#endif

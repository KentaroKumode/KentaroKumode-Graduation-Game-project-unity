#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AutoTest.Ultra;
using GameLoop;
using NUnit.Framework;
using UnityEngine;

namespace AutoTest.EditorTests.Ultra
{
    /// <summary>
    /// Run-state capture/restore — the keystone the remaining phases sit on (Phase B's
    /// restore gate, Phase E worker rollouts, Phase G policy iteration).
    ///
    /// <para>The failure this fixture exists to prevent is a checkpoint that restores
    /// <em>almost</em> everything. <c>JsonUtility</c> drops sets and dictionaries without a
    /// word, and in <c>RunState</c> that silently loses owned flags, timed buffs, permanent
    /// debuffs, seen events and shop counts — the restored run would look fine and be
    /// wrong.</para>
    /// </summary>
    [TestFixture]
    public sealed class UltraRunSnapshotTests
    {
        /// <summary>The guard: every RunState field must be a type the snapshot understands.
        /// A new field of an unknown type fails here rather than vanishing from checkpoints.</summary>
        [Test]
        public void EveryRunStateFieldTypeIsSupported()
        {
            Assert.That(UltraRunSnapshot.TryDescribeCoverage(out List<string> unsupported), Is.True,
                "UltraRunSnapshot cannot capture: " + string.Join(", ", unsupported.ToArray())
                + ". Add support for the type — never skip the field.");
        }

        private static RunState Populated()
        {
            var run = new RunState
            {
                currentFloor = 4,
                playerHP = 51,
                playerMaxHP = 93,
                coins = 137,
                weaponMaterials = 9,
                equippedWeaponId = "血塗りの戦斧",
                equippedDiceId = "dice_star",
                inLambda = true,
                convictionStage = 2,
                sealedRoleMask = 0b1010,
            };
            run.ownedPassiveItems.Add("百鳴りの共振箱");
            run.ownedPassiveItems.Add("ブレイドダンス");
            run.ownedConsumables.Add("回復薬");
            run.ownedFlags.Add("flag_duel_won");
            run.ownedFlags.Add("flag_altar_paid");
            run.permanentDebuffs.Add("sin_pride");
            run.seenOnceEvents.Add("ev_ghost_duel");
            run.timedBuffs["buff_focus"] = 3;
            run.timedDebuffs["debuff_bleed"] = 2;
            run.lambdaDebuffs["lambda_slow"] = 1;
            run.shopPurchasedCounts["血塗りの戦斧"] = 1;
            run.diceFaceParts.Add(new DiceFaceParts.Part
            { face = 9, tier = DiceFaceParts.Tier.T4 });
            run.chronicle.Add("floor 4 entered");
            return run;
        }

        [Test]
        public void ARoundTripReproducesTheRunExactly()
        {
            RunState original = Populated();
            UltraRunSnapshot snapshot = UltraRunSnapshot.Capture(original);

            var restored = new RunState();
            snapshot.RestoreInto(restored);

            Assert.That(UltraRunSnapshot.Capture(restored).ContentHash(),
                Is.EqualTo(snapshot.ContentHash()));
        }

        /// <summary>Named explicitly because these are the containers a naive
        /// <c>JsonUtility</c> checkpoint drops without any error.</summary>
        [Test]
        public void SetsAndDictionariesSurvive()
        {
            RunState original = Populated();
            var restored = new RunState();
            UltraRunSnapshot.Capture(original).RestoreInto(restored);

            Assert.That(restored.ownedFlags, Is.EquivalentTo(original.ownedFlags));
            Assert.That(restored.permanentDebuffs, Is.EquivalentTo(original.permanentDebuffs));
            Assert.That(restored.seenOnceEvents, Is.EquivalentTo(original.seenOnceEvents));
            Assert.That(restored.timedBuffs["buff_focus"], Is.EqualTo(3));
            Assert.That(restored.timedDebuffs["debuff_bleed"], Is.EqualTo(2));
            Assert.That(restored.lambdaDebuffs["lambda_slow"], Is.EqualTo(1));
            Assert.That(restored.shopPurchasedCounts["血塗りの戦斧"], Is.EqualTo(1));
        }

        /// <summary>Proof that the naive approach really does lose data, so this fixture is
        /// guarding something real rather than a hypothetical.</summary>
        [Test]
        public void JsonUtilityAlone_WouldHaveLostThem()
        {
            RunState original = Populated();
            var viaJsonUtility = JsonUtility.FromJson<RunState>(JsonUtility.ToJson(original));

            Assert.That(viaJsonUtility.ownedFlags, Is.Empty,
                "if this ever passes, JsonUtility learned to serialize HashSet and the "
                + "reflection path may be simplified");
            Assert.That(viaJsonUtility.timedBuffs, Is.Empty);
        }

        [Test]
        public void ListsOfSerializableStructsSurvive()
        {
            RunState original = Populated();
            var restored = new RunState();
            UltraRunSnapshot.Capture(original).RestoreInto(restored);

            Assert.That(restored.diceFaceParts.Count, Is.EqualTo(1));
            Assert.That(restored.diceFaceParts[0].face, Is.EqualTo(9));
            Assert.That(restored.diceFaceParts[0].tier, Is.EqualTo(DiceFaceParts.Tier.T4));
        }

        [Test]
        public void EnumsAndScalarsSurvive()
        {
            RunState original = Populated();
            original.gateFlaws = original.gateFlaws;   // exercise the enum path explicitly
            var restored = new RunState();
            UltraRunSnapshot.Capture(original).RestoreInto(restored);

            Assert.That(restored.currentFloor, Is.EqualTo(4));
            Assert.That(restored.playerHP, Is.EqualTo(51));
            Assert.That(restored.coins, Is.EqualTo(137));
            Assert.That(restored.equippedWeaponId, Is.EqualTo("血塗りの戦斧"));
            Assert.That(restored.inLambda, Is.True);
            Assert.That(restored.sealedRoleMask, Is.EqualTo(0b1010));
            Assert.That(restored.gateFlaws, Is.EqualTo(original.gateFlaws));
        }

        /// <summary>Restoring must not leave the target's previous values showing through.</summary>
        [Test]
        public void RestoringOverADirtyTargetClearsWhatWasThere()
        {
            var dirty = new RunState { coins = 9999, currentFloor = 7 };
            dirty.ownedFlags.Add("stale_flag");
            dirty.timedBuffs["stale_buff"] = 5;

            UltraRunSnapshot.Capture(Populated()).RestoreInto(dirty);

            Assert.That(dirty.coins, Is.EqualTo(137));
            Assert.That(dirty.currentFloor, Is.EqualTo(4));
            Assert.That(dirty.ownedFlags, Does.Not.Contain("stale_flag"));
            Assert.That(dirty.timedBuffs.ContainsKey("stale_buff"), Is.False);
        }

        [Test]
        public void TheCaptureIsStableAcrossRepeatedCalls()
        {
            RunState run = Populated();
            Assert.That(UltraRunSnapshot.Capture(run).ContentHash(),
                Is.EqualTo(UltraRunSnapshot.Capture(run).ContentHash()),
                "reflection field order and unordered containers must not leak into the hash");
        }

        [Test]
        public void ChangingOneValueChangesTheHash()
        {
            RunState run = Populated();
            string before = UltraRunSnapshot.Capture(run).ContentHash();
            run.coins += 1;

            Assert.That(UltraRunSnapshot.Capture(run).ContentHash(), Is.Not.EqualTo(before));
        }

        [Test]
        public void AddingASetMemberChangesTheHash()
        {
            RunState run = Populated();
            string before = UltraRunSnapshot.Capture(run).ContentHash();
            run.ownedFlags.Add("flag_new");

            Assert.That(UltraRunSnapshot.Capture(run).ContentHash(), Is.Not.EqualTo(before),
                "a dropped set member must not be invisible to the hash");
        }

        [Test]
        public void TheSnapshotSurvivesJsonTransport()
        {
            UltraRunSnapshot sent = UltraRunSnapshot.Capture(Populated());
            var received = JsonUtility.FromJson<UltraRunSnapshot>(JsonUtility.ToJson(sent));

            Assert.That(received.ContentHash(), Is.EqualTo(sent.ContentHash()));

            var restored = new RunState();
            received.RestoreInto(restored);
            Assert.That(restored.ownedFlags, Is.EquivalentTo(Populated().ownedFlags));
        }

        /// <summary>A checkpoint from another build must be refused outright. Restoring it
        /// partially would produce a run that never existed in either build.</summary>
        [Test]
        public void ASnapshotNamingAnUnknownField_IsRefused()
        {
            UltraRunSnapshot snapshot = UltraRunSnapshot.Capture(Populated());
            var widened = new List<UltraSnapshotField>(snapshot.fields)
            { new UltraSnapshotField { name = "fieldFromTheFuture", kind = "int", value = "1" } };
            snapshot.fields = widened.ToArray();

            var ex = Assert.Throws<InvalidOperationException>(
                () => snapshot.RestoreInto(new RunState()));
            StringAssert.Contains("fieldFromTheFuture", ex.Message);
        }

        [Test]
        public void ASnapshotMissingAFieldIsRefused()
        {
            UltraRunSnapshot snapshot = UltraRunSnapshot.Capture(Populated());
            var trimmed = new List<UltraSnapshotField>(snapshot.fields);
            trimmed.RemoveAt(0);
            snapshot.fields = trimmed.ToArray();

            var ex = Assert.Throws<InvalidOperationException>(
                () => snapshot.RestoreInto(new RunState()));
            StringAssert.Contains("missing", ex.Message);
        }

        [Test]
        public void AWrongVersionIsRefused()
        {
            UltraRunSnapshot snapshot = UltraRunSnapshot.Capture(Populated());
            snapshot.version = UltraRunSnapshot.CurrentVersion + 1;

            Assert.Throws<InvalidOperationException>(() => snapshot.RestoreInto(new RunState()));
        }

        /// <summary>No field may carry the live generator across the boundary.</summary>
        [Test]
        public void TheSnapshotCarriesNoRandomState()
        {
            UltraRunSnapshot snapshot = UltraRunSnapshot.Capture(Populated());
            foreach (UltraSnapshotField field in snapshot.fields)
            {
                string lower = field.name.ToLowerInvariant();
                Assert.That(lower.Contains("rng"), Is.False, field.name);
                Assert.That(lower.Contains("seed"), Is.False, field.name);
            }
        }

        [Test]
        public void CapturingNothingIsAnError()
        {
            Assert.Throws<ArgumentNullException>(() => UltraRunSnapshot.Capture(null));
            Assert.Throws<ArgumentNullException>(
                () => UltraRunSnapshot.Capture(Populated()).RestoreInto(null));
        }
    }
}
#endif

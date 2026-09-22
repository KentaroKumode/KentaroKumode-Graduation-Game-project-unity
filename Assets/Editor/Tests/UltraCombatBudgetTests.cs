#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading;
using AutoTest.Ultra;
using CombatSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    /// <summary>
    /// Phase E-1: the bounds, cancellation and fallback that must exist before any combat
    /// search does. Two editor freezes came from a budget whose unit did not match the work,
    /// so these tests pin the unit as well as the limit.
    /// </summary>
    [TestFixture]
    public sealed class UltraCombatBudgetTests
    {
        [Test]
        public void AnUnusedBudget_CompletesCleanly()
        {
            var budget = new UltraCombatBudget();
            budget.Begin();
            budget.Complete();

            Assert.That(budget.StopReason, Is.EqualTo(UltraStopReason.Completed));
            Assert.That(budget.CompletedWithinBudget, Is.True);
        }

        [Test]
        public void TheEvaluationCap_StopsExactlyAtTheLimit()
        {
            var budget = new UltraCombatBudget { MaxEvaluations = 10, MaxWallClockMs = 0 };
            budget.Begin();

            int granted = 0;
            while (budget.TryConsumeEvaluation()) granted++;

            Assert.That(granted, Is.EqualTo(10));
            Assert.That(budget.StopReason, Is.EqualTo(UltraStopReason.EvaluationCap));
            Assert.That(budget.CompletedWithinBudget, Is.False,
                "a capped search returns a partial answer and the caller must know");
        }

        [Test]
        public void TheMemoryCap_CountsEntriesRatherThanBytes()
        {
            var budget = new UltraCombatBudget { MaxMemoEntries = 3 };
            budget.Begin();

            Assert.That(budget.TryGrowMemo(3), Is.True);
            Assert.That(budget.TryGrowMemo(4), Is.False);
            Assert.That(budget.StopReason, Is.EqualTo(UltraStopReason.MemoryCap));
        }

        [Test]
        public void Cancellation_TakesEffectOnTheNextCharge()
        {
            var budget = new UltraCombatBudget { MaxEvaluations = 1_000_000, MaxWallClockMs = 0 };
            budget.Begin();

            Assert.That(budget.TryConsumeEvaluation(), Is.True);
            budget.Cancel();

            Assert.That(budget.TryConsumeEvaluation(), Is.False);
            Assert.That(budget.StopReason, Is.EqualTo(UltraStopReason.Cancelled));
        }

        [Test]
        public void Cancellation_FromAnotherThread_IsObserved()
        {
            var budget = new UltraCombatBudget { MaxEvaluations = long.MaxValue, MaxWallClockMs = 0 };
            budget.Begin();

            var thread = new Thread(() => budget.Cancel());
            thread.Start();
            thread.Join();

            Assert.That(budget.TryConsumeEvaluation(), Is.False);
            Assert.That(budget.IsCancelled, Is.True);
        }

        [Test]
        public void CancellingBeforeBeginIsHarmless_AndBeginClearsIt()
        {
            var budget = new UltraCombatBudget { MaxWallClockMs = 0 };
            budget.Cancel();
            budget.Begin();

            Assert.That(budget.IsCancelled, Is.False);
            Assert.That(budget.TryConsumeEvaluation(), Is.True);
        }

        [Test]
        public void ShouldContinue_GoesFalseOnceACapTrips()
        {
            var budget = new UltraCombatBudget { MaxEvaluations = 1, MaxWallClockMs = 0 };
            budget.Begin();

            Assert.That(budget.ShouldContinue, Is.True);
            budget.TryConsumeEvaluation();
            budget.TryConsumeEvaluation();

            Assert.That(budget.ShouldContinue, Is.False);
        }

        [Test]
        public void BatchBoundsAreSmallEnoughForATenThousandRunBatch()
        {
            UltraCombatBudget batch = UltraCombatBudget.Batch();

            Assert.That(batch.MaxWallClockMs, Is.LessThanOrEqualTo(2_000),
                "a per-decision stall multiplies by ~380 decisions per run");
            Assert.That(batch.MaxEvaluations, Is.LessThanOrEqualTo(1_000_000));
            Assert.That(UltraCombatBudget.Diagnostic().MaxEvaluations,
                Is.GreaterThan(batch.MaxEvaluations));
        }

        /// <summary>A search that always hits its cap is its fallback heuristic wearing a
        /// costume. The ratio has to be recorded for that to be visible.</summary>
        [Test]
        public void Stats_SeparateCompletedSearchesFromCappedOnes()
        {
            UltraCombatBudgetStats.Reset();

            var completed = new UltraCombatBudget { MaxWallClockMs = 0 };
            completed.Begin();
            completed.Complete();
            UltraCombatBudgetStats.Note(completed);

            var capped = new UltraCombatBudget { MaxEvaluations = 1, MaxWallClockMs = 0 };
            capped.Begin();
            capped.TryConsumeEvaluation();
            capped.TryConsumeEvaluation();
            UltraCombatBudgetStats.Note(capped);

            Assert.That(UltraCombatBudgetStats.Searches, Is.EqualTo(2));
            Assert.That(UltraCombatBudgetStats.Completed, Is.EqualTo(1));
            Assert.That(UltraCombatBudgetStats.EvaluationCapped, Is.EqualTo(1));
            StringAssert.Contains("50.0%", UltraCombatBudgetStats.Describe());
        }

        [Test]
        public void Stats_SayWhenNoSearchEverRan()
        {
            UltraCombatBudgetStats.Reset();
            StringAssert.Contains("実行なし", UltraCombatBudgetStats.Describe());
        }

        // ------------------------------------------------------------------
        //  combat action encoding
        // ------------------------------------------------------------------

        [Test]
        public void WiringRoundTrips()
        {
            var plan = new[]
            {
                DiceTerminal.Attack, DiceTerminal.Block, DiceTerminal.Charge,
                DiceTerminal.Special, DiceTerminal.Attack,
            };

            string encoded = UltraCombatActions.EncodeWiring(plan);
            Assert.That(UltraCombatActions.TryDecodeWiring(encoded, 5, out DiceTerminal[] back), Is.True);
            Assert.That(back, Is.EqualTo(plan));
        }

        [Test]
        public void WiringOfTheWrongLength_IsRefused()
        {
            Assert.That(UltraCombatActions.TryDecodeWiring("0000", 5, out _), Is.False);
            Assert.That(UltraCombatActions.TryDecodeWiring("", 5, out _), Is.False);
            Assert.That(UltraCombatActions.TryDecodeWiring("0009", 4, out _), Is.False,
                "digit 9 is not a terminal");
        }

        [Test]
        public void RerollMaskRoundTrips_AndZeroMeansDeclining()
        {
            Assert.That(UltraCombatActions.TryDecodeReroll(
                UltraCombatActions.EncodeReroll(0b10101), 5, out int[] picked), Is.True);
            Assert.That(picked, Is.EqualTo(new[] { 0, 2, 4 }));

            Assert.That(UltraCombatActions.TryDecodeReroll("0", 5, out int[] none), Is.True);
            Assert.That(none, Is.Empty);
            Assert.That(UltraCombatActions.Reroll(0).label, Is.EqualTo("振り直さない"),
                "declining must be an offered action, not the absence of one");
        }

        [Test]
        public void ARerollMaskNamingADieThatDoesNotExist_IsRefused()
        {
            Assert.That(UltraCombatActions.TryDecodeReroll("32", 5, out _), Is.False);
            Assert.That(UltraCombatActions.TryDecodeReroll("-1", 5, out _), Is.False);
            Assert.That(UltraCombatActions.TryDecodeReroll("nonsense", 5, out _), Is.False);
        }

        [Test]
        public void RoleMaskRoundTrips_AndZeroMeansDeferEverything()
        {
            var candidates = new List<RoleKind> { RoleKind.Pair, RoleKind.Triple };
            int mask = (1 << (int)RoleKind.Pair) | (1 << (int)RoleKind.Triple);

            Assert.That(UltraCombatActions.TryDecodeRoles(
                UltraCombatActions.EncodeRoles(mask), candidates, out List<RoleKind> fire), Is.True);
            Assert.That(fire, Is.EquivalentTo(candidates));

            Assert.That(UltraCombatActions.TryDecodeRoles("0", candidates, out List<RoleKind> none), Is.True);
            Assert.That(none, Is.Empty);
            Assert.That(UltraCombatActions.Roles(0).label, Is.EqualTo("全て温存"),
                "roles are once per combat, so holding one back is a real play");
        }

        /// <summary>Naming a role that was not offered means the two sides disagree about the
        /// board. Dropping the bit silently would hide that.</summary>
        [Test]
        public void ARoleMaskNamingAnUnofferedRole_IsRefused()
        {
            var candidates = new List<RoleKind> { RoleKind.Pair };
            int mask = (1 << (int)RoleKind.Pair) | (1 << (int)RoleKind.Yacht);

            Assert.That(UltraCombatActions.TryDecodeRoles(
                UltraCombatActions.EncodeRoles(mask), candidates, out _), Is.False);
        }

        [Test]
        public void TheTelegraphCopiesAcrossVerbatim()
        {
            var tele = new MutualTurnTelegraph
            {
                turn = 5,
                enemyAttackValue = 42,
                escalationStage = 2,
                enemyDice = new[] { 3, 4 },
                enemyDiceTotal = 7,
                enemyShield = 12,
                enemyShieldReflectRate = 2f,
                enemyDodgeChance = 0.25f,
                enemyHalvesDamageThisTurn = true,
                playerBlockIgnored = true,
                executeArmed = true,
                sealedTerminal = 1,
                blazeStacks = 3,
                blazePenalizesBlock = true,
            };

            UltraCombatView view = UltraCombatView.FromTelegraph(tele);

            Assert.That(view.turn, Is.EqualTo(5));
            Assert.That(view.enemyAttackValue, Is.EqualTo(42));
            Assert.That(view.enemyDice, Is.EqualTo(new[] { 3, 4 }));
            Assert.That(view.enemyShieldReflectRate, Is.EqualTo(2f));
            Assert.That(view.executeArmed, Is.True);
            Assert.That(view.sealedTerminal, Is.EqualTo(1));
            Assert.That(view.blazePenalizesBlock, Is.True);
        }

        [Test]
        public void TheCombatView_CopiesArraysRatherThanAliasingThem()
        {
            var dice = new[] { 1, 2 };
            var tele = new MutualTurnTelegraph { enemyDice = dice };

            UltraCombatView view = UltraCombatView.FromTelegraph(tele);
            dice[0] = 99;

            Assert.That(view.enemyDice[0], Is.EqualTo(1),
                "an observation that aliases live state is not a snapshot");
        }
    }
}
#endif

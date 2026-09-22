#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using AutoTest.EditorTools;
using AutoTest.Ultra;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AutoTest.EditorTests.Ultra
{
    [TestFixture]
    public sealed class UltraAutoRunIntegrationTests
    {
        [Test]
        public void WiringSkill_UltraIsAppendedWithoutRenumberingExistingModes()
        {
            Assert.That((int)AutoRunner.WiringSkill.Naive, Is.EqualTo(0));
            Assert.That((int)AutoRunner.WiringSkill.Optimal, Is.EqualTo(1));
            Assert.That((int)AutoRunner.WiringSkill.Super, Is.EqualTo(2));
            Assert.That((int)AutoRunner.WiringSkill.Exact, Is.EqualTo(3));
            Assert.That((int)AutoRunner.WiringSkill.Ultra, Is.EqualTo(4));
        }

        [Test]
        public void CanonicalProfile_IsExactlyRequestedStandardRelic50Point10K()
        {
            UltraAutoRunProfile profile = UltraAutoRunProfile.CreateStandard50WithRelic10000();

            Assert.That(profile.IsCanonical(out string reason), Is.True, reason);
            Assert.That(profile.runCount, Is.EqualTo(10000));
            Assert.That(profile.challengeScore, Is.EqualTo(50));
            Assert.That(profile.theoreticalBestCursedRelic, Is.True);
            Assert.That(profile.persona, Is.EqualTo(BuildPersona.Standard));
            Assert.That(profile.metaBuffMode, Is.EqualTo(AutoRunner.MetaBuffMode.Standard));
            Assert.That(profile.itemPickMode, Is.EqualTo(AutoRunner.ItemPickMode.BuildFocused));

            // Ultra v1 fixed mechanical conditions (handoff 2026-08-18 section 2).
            Assert.That(profile.useMutualAttackPipeline, Is.True);
            Assert.That(profile.suppressFacePartOffers, Is.False,
                "the benchmark measures shipping items, not the measurement-only cut-out");
            Assert.That(profile.shieldAbsorbsUnmitigable, Is.True);
            Assert.That(profile.enableAllDebuffs, Is.False);
            Assert.That(profile.sweepAllMetaAxes, Is.False);
            Assert.That(profile.challengeSpec, Is.Empty);

            var loadout = AscensionLoop.BuildLoadoutAtScore(profile.challengeScore);
            Assert.That(MetaProgression.ChallengeResolver.Score(loadout), Is.EqualTo(50),
                "the requested score must be attainable exactly, not rounded to another tier combination");

            var relic = RelicPresets.Build(RelicPresets.Preset.TheoreticalBestCursed);
            Assert.That(relic, Is.Not.Null);
            Assert.That(relic.IsValid(), Is.True);
        }

        [Test]
        public void CanonicalProfile_AppliesAndRevalidatesRuntimeRunnerState()
        {
            GameObject go = new GameObject("[Ultra profile integration test]");
            try
            {
                AutoRunner runner = go.AddComponent<AutoRunner>();
                UltraAutoRunProfile profile = UltraAutoRunProfile.CreateStandard50WithRelic10000();

                // Dirty the runner the way a leftover menu action would, so the test proves
                // ApplyTo overrides inherited state instead of merely agreeing with defaults.
                runner.useMutualAttackPipeline = false;
                runner.suppressFacePartOffers = true;
                runner.shieldAbsorbsUnmitigable = false;
                runner.enableAllDebuffs = true;
                runner.sweepAllMetaAxes = true;
                runner.challengeSpec = "bogus";
                runner.personaSweep = true;
                runner.relicAxisSweep = true;
                runner.ascensionMode = true;

                profile.ApplyTo(runner);

                Assert.That(profile.Matches(runner, out string reason), Is.True, reason);
                Assert.That(runner.wiringSkill, Is.EqualTo(AutoRunner.WiringSkill.Ultra));
                Assert.That(runner.EffectiveWiringSkill, Is.EqualTo(AutoRunner.WiringSkill.Super),
                    "Super must be captured as the production baseline before any Ultra proposal");
                Assert.That(runner.challengeFixedSweep, Is.True);
                Assert.That(runner.challengeSweepScores, Is.EqualTo(new[] { 50 }));
                Assert.That(runner.challengeSweepRuns, Is.EqualTo(10000));
                Assert.That(runner.forceNoRelic, Is.False);
                Assert.That(runner.tuneBosses || runner.learnTier || runner.learnBotAi, Is.False,
                    "a benchmark must not mutate learning or balance data");

                Assert.That(runner.useMutualAttackPipeline, Is.True);
                Assert.That(runner.suppressFacePartOffers, Is.False);
                Assert.That(runner.shieldAbsorbsUnmitigable, Is.True);
                Assert.That(runner.enableAllDebuffs, Is.False);
                Assert.That(runner.sweepAllMetaAxes, Is.False);
                Assert.That(runner.challengeSpec, Is.Empty);
                Assert.That(runner.personaSweep || runner.relicAxisSweep || runner.ascensionMode,
                    Is.False, "a competing batch mode would replace the run loop");

                runner.forceNoRelic = true;
                Assert.That(profile.Matches(runner, out reason), Is.False);
                StringAssert.Contains("no-relic", reason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [TestCase(nameof(UltraAutoRunProfile.runCount), 9999)]
        [TestCase(nameof(UltraAutoRunProfile.challengeScore), 49)]
        public void CanonicalProfile_RejectsNumericDrift(string fieldName, int invalidValue)
        {
            UltraAutoRunProfile profile = UltraAutoRunProfile.CreateStandard50WithRelic10000();
            typeof(UltraAutoRunProfile).GetField(fieldName).SetValue(profile, invalidValue);

            Assert.That(profile.IsCanonical(out string reason), Is.False);
            Assert.That(reason, Is.Not.Empty);
        }

        [TestCase(nameof(UltraAutoRunProfile.useMutualAttackPipeline), false)]
        [TestCase(nameof(UltraAutoRunProfile.suppressFacePartOffers), true)]
        [TestCase(nameof(UltraAutoRunProfile.shieldAbsorbsUnmitigable), false)]
        [TestCase(nameof(UltraAutoRunProfile.enableAllDebuffs), true)]
        [TestCase(nameof(UltraAutoRunProfile.sweepAllMetaAxes), true)]
        public void CanonicalProfile_RejectsMechanicalDrift(string fieldName, bool invalidValue)
        {
            UltraAutoRunProfile profile = UltraAutoRunProfile.CreateStandard50WithRelic10000();
            typeof(UltraAutoRunProfile).GetField(fieldName).SetValue(profile, invalidValue);

            Assert.That(profile.IsCanonical(out string reason), Is.False,
                "a rule that changes outcomes must not pass validation silently");
            Assert.That(reason, Is.Not.Empty);
        }

        /// <summary>The 2026-08-17 failure in one assertion: the workers and the production
        /// run must describe the same game. Both sides now derive from the same source, and
        /// this test fails if anyone re-forks them.</summary>
        [Test]
        public void PortfolioProfile_MatchesTheProductionRunProfileFieldForField()
        {
            UltraAutoRunProfile run = UltraAutoRunProfile.CreateStandard50WithRelic10000();
            UltraPortfolioProfileSpec worker = UltraPortfolioProtocol.CanonicalProfile();

            Assert.That(worker.challengeScore, Is.EqualTo(run.challengeScore));
            Assert.That(worker.theoreticalBestCursedRelic, Is.EqualTo(run.theoreticalBestCursedRelic));
            Assert.That(worker.persona, Is.EqualTo((int)run.persona));
            Assert.That(worker.metaBuffMode, Is.EqualTo((int)run.metaBuffMode));
            Assert.That(worker.itemPickMode, Is.EqualTo((int)run.itemPickMode));

            Assert.That(worker.useMutualAttackPipeline, Is.EqualTo(run.useMutualAttackPipeline));
            Assert.That(worker.suppressFacePartOffers, Is.EqualTo(run.suppressFacePartOffers));
            Assert.That(worker.shieldAbsorbsUnmitigable, Is.EqualTo(run.shieldAbsorbsUnmitigable));
            Assert.That(worker.enableAllDebuffs, Is.EqualTo(run.enableAllDebuffs));
            Assert.That(worker.sweepAllMetaAxes, Is.EqualTo(run.sweepAllMetaAxes));
            Assert.That(worker.challengeSpec ?? string.Empty,
                Is.EqualTo(run.challengeSpec ?? string.Empty));
        }

        /// <summary>The fingerprint has to notice a rule change, otherwise the protocol
        /// cannot tell a mismatched worker from a matching one — which is exactly what
        /// happened under profile fingerprint v1.</summary>
        [Test]
        public void ProfileFingerprint_ChangesWhenAMechanicalRuleChanges()
        {
            UltraPortfolioProfileSpec baseline = UltraPortfolioProtocol.CanonicalProfile();
            string baselineHash = UltraPortfolioProtocol.ComputeProfileFingerprint(baseline);
            Assert.That(baselineHash, Is.Not.Null.And.Not.Empty);

            foreach (string field in new[]
            {
                nameof(UltraPortfolioProfileSpec.useMutualAttackPipeline),
                nameof(UltraPortfolioProfileSpec.suppressFacePartOffers),
                nameof(UltraPortfolioProfileSpec.shieldAbsorbsUnmitigable),
                nameof(UltraPortfolioProfileSpec.enableAllDebuffs),
                nameof(UltraPortfolioProfileSpec.sweepAllMetaAxes),
            })
            {
                UltraPortfolioProfileSpec drifted = UltraPortfolioProtocol.CanonicalProfile();
                FieldInfo info = typeof(UltraPortfolioProfileSpec).GetField(field);
                info.SetValue(drifted, !(bool)info.GetValue(drifted));

                Assert.That(UltraPortfolioProtocol.ComputeProfileFingerprint(drifted),
                    Is.Not.EqualTo(baselineHash), field + " must be inside the fingerprint");
            }
        }

        /// <summary>Builds candidate vectors where <paramref name="agreeCount"/> of
        /// <paramref name="n"/> runs share the baseline's fingerprint.</summary>
        private static UltraPolicyCandidateResult[] TwoCandidates(int n, int agreeCount)
        {
            var baseValid = new bool[n];
            var baseFp = new int[n];
            var chalFp = new int[n];
            for (int i = 0; i < n; i++)
            {
                baseValid[i] = true;
                baseFp[i] = 1000 + i;
                chalFp[i] = i < agreeCount ? baseFp[i] : 900000 + i;
            }
            return new[]
            {
                new UltraPolicyCandidateResult
                { id = "super-baseline-v1", validRuns = baseValid, runFingerprints = baseFp },
                new UltraPolicyCandidateResult
                { id = "challenger-v1", validRuns = (bool[])baseValid.Clone(), runFingerprints = chalFp },
            };
        }

        /// <summary>The 2026-08-17 numbers exactly: 988 of 1000 runs identical. The originally
        /// proposed "warn only when digests are 100% identical" rule would have stayed silent
        /// here, which is why the threshold is 0.95.</summary>
        [Test]
        public void NoOpDetector_FlagsThe988Of1000CaseThatActuallyHappened()
        {
            UltraPortfolioSelector.DetectConfigurationNoOps(
                TwoCandidates(1000, 988), out string[] suspects, out double agreement);

            Assert.That(suspects, Is.EquivalentTo(new[] { "challenger-v1" }));
            Assert.That(agreement, Is.EqualTo(0.988).Within(1e-9));
            StringAssert.Contains("NO-OP", UltraPortfolioSelector.DescribeNoOpCheck(suspects, agreement));
        }

        /// <summary>The healthy 2026-08-18 re-run. The highest legitimate agreement was 86.8%
        /// (a candidate differing only at layer 6), so none of these may trip.</summary>
        [TestCase(12, TestName = "NoOpDetector_Silent_DifferentCombatAi_1pct")]
        [TestCase(747, TestName = "NoOpDetector_Silent_DiffersFromLambdaOnward_75pct")]
        [TestCase(867, TestName = "NoOpDetector_Silent_DiffersAtLayer6Only_87pct")]
        public void NoOpDetector_StaysSilentOnGenuinelyDistinctCandidates(int agreeCount)
        {
            UltraPortfolioSelector.DetectConfigurationNoOps(
                TwoCandidates(999, agreeCount), out string[] suspects, out double agreement);

            Assert.That(suspects, Is.Empty,
                "a legitimately distinct candidate must not be reported as a no-op");
            Assert.That(agreement, Is.LessThan(UltraPortfolioSelector.NoOpAgreementThreshold));
        }

        [Test]
        public void NoOpDetector_IgnoresSamplesTooSmallToMeanAnything()
        {
            UltraPortfolioSelector.DetectConfigurationNoOps(
                TwoCandidates(UltraPortfolioSelector.NoOpMinComparableRuns - 1, 0),
                out string[] suspects, out double agreement);

            Assert.That(suspects, Is.Empty);
            Assert.That(agreement, Is.EqualTo(0.0));
        }

        /// <summary>Guards the trap found while writing the detector: the protocol's own
        /// per-run digest is salted with <c>policyId</c>, so candidates never share one and a
        /// check built on it would report "healthy" no matter how broken the run was.</summary>
        [Test]
        public void RunDigest_IsPolicySalted_SoItCannotBeUsedForNoOpDetection()
        {
            var record = new UltraProductionRunRecord
            { valid = true, fullClear = true, crash = false, deadlock = false, fingerprint = 4242 };

            string a = UltraPortfolioProtocol.ComputeRunDigest("super-baseline-v1", "0000000000000000", 0, record);
            string b = UltraPortfolioProtocol.ComputeRunDigest("optimal-v1", "0000000000000000", 0, record);

            Assert.That(a, Is.Not.EqualTo(b),
                "identical outcomes must still hash differently per policy — hence the detector "
                + "uses runFingerprints instead");
        }

        [Test]
        public void DedicatedMenuEntry_ExistsAndNamesTheCompleteProfile()
        {
            MethodInfo method = typeof(AutoRunMenu).GetMethod(
                "RunUltra50WithRelic",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            MenuItem attribute = method.GetCustomAttributes(typeof(MenuItem), false)
                .Cast<MenuItem>()
                .SingleOrDefault();
            Assert.That(attribute, Is.Not.Null);
            StringAssert.Contains("Ultra AI", attribute.menuItem);
            StringAssert.Contains("遺物アリ", attribute.menuItem);
            StringAssert.Contains("Standard", attribute.menuItem);
            StringAssert.Contains("50pt", attribute.menuItem);
            StringAssert.Contains("10000", attribute.menuItem);
        }
    }
}
#endif

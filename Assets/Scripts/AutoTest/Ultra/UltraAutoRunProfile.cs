using System;

namespace AutoTest.Ultra
{
    /// <summary>
    /// The one production benchmark profile requested for the first Ultra run.
    /// Keeping it as data, rather than a collection of Editor SessionState keys,
    /// lets runtime validation prove the effective conditions before run 1 starts.
    ///
    /// <para><b>Mechanical fields are part of the profile, not inherited from EditorPrefs.</b>
    /// The 2026-08-17 portfolio session ran the workers on AutoRunner defaults while the
    /// parent 10K run had inherited mutual-attack ON / face-part suppression ON /
    /// shield-absorbs-unmitigable ON from EditorPrefs. Two arms measured under different
    /// rules cannot be compared, so every rule that changes outcomes now travels with the
    /// profile and is re-verified by <see cref="Matches"/> before run 1.</para>
    /// </summary>
    [Serializable]
    public sealed class UltraAutoRunProfile
    {
        public const int Standard50WithRelicRunCount = 10000;
        public const int Standard50ChallengeScore = 50;

        public int runCount;
        public int challengeScore;
        public bool theoreticalBestCursedRelic;
        public BuildPersona persona;
        public AutoRunner.MetaBuffMode metaBuffMode;
        public AutoRunner.ItemPickMode itemPickMode;

        // ---- mechanical rules (Ultra v1 fixed conditions) ----

        /// <summary>ADR-0009 mutual attack pipeline. Ultra v1 measures the shipping rules.</summary>
        public bool useMutualAttackPipeline;
        /// <summary>Face-part shop suppression. <b>false</b> for Ultra v1: the benchmark must
        /// include shipping items, not the measurement-only cut-out.</summary>
        public bool suppressFacePartOffers;
        /// <summary>2026-08-15 rule: shields absorb unmitigable damage.</summary>
        public bool shieldAbsorbsUnmitigable;
        /// <summary>Meta debuffs are driven by the challenge score, never by the blanket toggle.</summary>
        public bool enableAllDebuffs;
        /// <summary>Meta axis round-robin. A benchmark fixes one persona instead.</summary>
        public bool sweepAllMetaAxes;
        /// <summary>Explicit challenge loadout override. Empty = derive from the score.</summary>
        public string challengeSpec;

        public static UltraAutoRunProfile CreateStandard50WithRelic10000()
        {
            return new UltraAutoRunProfile
            {
                runCount = Standard50WithRelicRunCount,
                challengeScore = Standard50ChallengeScore,
                theoreticalBestCursedRelic = true,
                persona = BuildPersona.Standard,
                metaBuffMode = AutoRunner.MetaBuffMode.Standard,
                itemPickMode = AutoRunner.ItemPickMode.BuildFocused,

                useMutualAttackPipeline = true,
                suppressFacePartOffers = false,
                shieldAbsorbsUnmitigable = true,
                enableAllDebuffs = false,
                sweepAllMetaAxes = false,
                challengeSpec = string.Empty,
            };
        }

        public void ApplyTo(AutoRunner runner)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (!IsCanonical(out string reason))
                throw new InvalidOperationException("Invalid Ultra AutoRun profile: " + reason);

            runner.wiringSkill = AutoRunner.WiringSkill.Ultra;
            runner.runCount = runCount;
            runner.metaBuffMode = metaBuffMode;
            runner.itemPickMode = itemPickMode;
            runner.forceNoRelic = !theoreticalBestCursedRelic;
            runner.challengeFixedSweep = true;
            runner.challengeSweepScores = new[] { challengeScore };
            runner.challengeSweepRuns = runCount;
            runner.challengeSweepDiagnostics = false;

            // A benchmark must not rewrite the policy, item tiers, or boss balance
            // after the measurement. The fixed sweep itself locks the run persona to
            // Standard and installs TheoreticalBestCursed after every ResetAll.
            runner.tuneBosses = false;
            runner.learnTier = false;
            runner.learnBotAi = false;
            runner.autoLoopBatches = 1;
            runner.writeRunsJsonl = false;
            runner.stepsPerYield = 1000;
            runner.runsPerYield = 1;

            // ---- mechanical rules: assign explicitly, never inherit ----
            //   These decide outcomes, so the worker and the 10K run must agree on them.
            //   Assigning here (Launch calls ApplyTo last) also overrides anything the
            //   menu's SessionState left behind.
            runner.useMutualAttackPipeline = useMutualAttackPipeline;
            runner.suppressFacePartOffers = suppressFacePartOffers;
            runner.shieldAbsorbsUnmitigable = shieldAbsorbsUnmitigable;
            runner.enableAllDebuffs = enableAllDebuffs;
            runner.sweepAllMetaAxes = sweepAllMetaAxes;
            runner.challengeSpec = challengeSpec ?? string.Empty;

            // ---- every other batch mode off ----
            //   Each of these replaces the run loop or the loadout. One left on from a
            //   previous menu action would silently measure something else entirely.
            runner.simBoss5Sweep = false;
            runner.lambdaFarmSweep = false;
            runner.relicPresetSweep = false;
            runner.relicSweepBaselineOnly = false;
            runner.relicAxisSweep = false;
            runner.challengeAxisSweep = false;
            runner.challengeCategorySweep = false;
            runner.personaSweep = false;
            runner.ascensionMode = false;
            runner.wiringSkillCompare = false;
        }

        public bool IsCanonical(out string reason)
        {
            if (runCount != Standard50WithRelicRunCount)
                return Fail("runCount must be 10000", out reason);
            if (challengeScore != Standard50ChallengeScore)
                return Fail("challengeScore must be 50", out reason);
            if (!theoreticalBestCursedRelic)
                return Fail("TheoreticalBestCursed relic must be enabled", out reason);
            if (persona != BuildPersona.Standard)
                return Fail("persona must be Standard", out reason);
            if (metaBuffMode != AutoRunner.MetaBuffMode.Standard)
                return Fail("meta buff mode must be Standard", out reason);
            if (itemPickMode != AutoRunner.ItemPickMode.BuildFocused)
                return Fail("item selection must be BuildFocused", out reason);

            if (!useMutualAttackPipeline)
                return Fail("mutual attack pipeline must be enabled", out reason);
            if (suppressFacePartOffers)
                return Fail("face part offers must not be suppressed", out reason);
            if (!shieldAbsorbsUnmitigable)
                return Fail("shields must absorb unmitigable damage", out reason);
            if (enableAllDebuffs)
                return Fail("blanket meta debuffs must be disabled", out reason);
            if (sweepAllMetaAxes)
                return Fail("meta axis sweep must be disabled", out reason);
            if (!string.IsNullOrEmpty(challengeSpec))
                return Fail("challenge spec must be empty (derived from the score)", out reason);

            reason = string.Empty;
            return true;
        }

        public bool Matches(AutoRunner runner, out string reason)
        {
            if (runner == null) return Fail("runner is null", out reason);
            if (!IsCanonical(out reason)) return false;
            if (runner.wiringSkill != AutoRunner.WiringSkill.Ultra)
                return Fail("effective runner skill is not Ultra", out reason);
            if (!runner.challengeFixedSweep)
                return Fail("fixed challenge sweep is disabled", out reason);
            if (runner.challengeSweepRuns != runCount)
                return Fail("runtime run count mismatch", out reason);
            if (runner.runCount != runCount)
                return Fail("runtime batch count mismatch", out reason);
            if (runner.challengeSweepScores == null
                || runner.challengeSweepScores.Length != 1
                || runner.challengeSweepScores[0] != challengeScore)
                return Fail("runtime challenge score mismatch", out reason);
            if (runner.forceNoRelic)
                return Fail("runtime forced no-relic mode is enabled", out reason);
            if (runner.metaBuffMode != metaBuffMode || runner.itemPickMode != itemPickMode)
                return Fail("runtime Standard profile mismatch", out reason);

            // ---- mechanical parity ----
            //   This is the check that the 2026-08-17 session did not have. Without it a
            //   worker and the 10K run can disagree and still both report "started".
            if (runner.useMutualAttackPipeline != useMutualAttackPipeline)
                return Fail("runtime mutual attack pipeline mismatch", out reason);
            if (runner.suppressFacePartOffers != suppressFacePartOffers)
                return Fail("runtime face part suppression mismatch", out reason);
            if (runner.shieldAbsorbsUnmitigable != shieldAbsorbsUnmitigable)
                return Fail("runtime shield absorption mismatch", out reason);
            if (runner.enableAllDebuffs != enableAllDebuffs)
                return Fail("runtime blanket debuff mismatch", out reason);
            if (runner.sweepAllMetaAxes != sweepAllMetaAxes)
                return Fail("runtime meta axis sweep mismatch", out reason);
            if (!string.Equals(runner.challengeSpec ?? string.Empty,
                               challengeSpec ?? string.Empty, StringComparison.Ordinal))
                return Fail("runtime challenge spec mismatch", out reason);

            if (runner.simBoss5Sweep || runner.lambdaFarmSweep || runner.relicPresetSweep
                || runner.relicSweepBaselineOnly || runner.relicAxisSweep
                || runner.challengeAxisSweep || runner.challengeCategorySweep
                || runner.personaSweep || runner.ascensionMode || runner.wiringSkillCompare)
                return Fail("a competing batch mode is still enabled", out reason);

            reason = string.Empty;
            return true;
        }

        private static bool Fail(string message, out string reason)
        {
            reason = message;
            return false;
        }
    }
}

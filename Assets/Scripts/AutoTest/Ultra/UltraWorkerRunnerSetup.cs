using System;

namespace AutoTest.Ultra
{
    /// <summary>Configures an <see cref="AutoRunner"/> inside a worker process.
    ///
    /// <para><b>Shared by every worker kind on purpose.</b> The 2026-08-17 failure was two
    /// places configuring the same thing and drifting apart: the portfolio workers ran on
    /// AutoRunner defaults while the parent had inherited different rules, and four candidates
    /// silently became one. A second copy of this method for episode workers would reproduce
    /// that exactly, one level down.</para></summary>
    public static class UltraWorkerRunnerSetup
    {
        /// <summary>Apply the measured conditions and switch off everything that would replace
        /// the run loop. Policy and seeds are the caller's business.</summary>
        public static void ApplyProfile(AutoRunner runner, UltraPortfolioProfileSpec profile)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            runner.metaBuffMode = (AutoRunner.MetaBuffMode)profile.metaBuffMode;
            runner.itemPickMode = (AutoRunner.ItemPickMode)profile.itemPickMode;
            runner.forceNoRelic = !profile.theoreticalBestCursedRelic;

            // ---- mechanical rules: from the request, never from defaults ----
            runner.useMutualAttackPipeline = profile.useMutualAttackPipeline;
            runner.suppressFacePartOffers = profile.suppressFacePartOffers;
            runner.shieldAbsorbsUnmitigable = profile.shieldAbsorbsUnmitigable;
            runner.enableAllDebuffs = profile.enableAllDebuffs;
            runner.sweepAllMetaAxes = profile.sweepAllMetaAxes;
            runner.challengeSpec = profile.challengeSpec ?? string.Empty;

            // ---- a benchmark must not rewrite learning or balance data ----
            runner.tuneBosses = false;
            runner.learnTier = false;
            runner.learnBotAi = false;

            // ---- every competing batch mode off ----
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

            runner.autoStart = false;
            runner.exitPlayModeWhenDone = false;
            runner.autoLoopBatches = 1;
            runner.writeRunsJsonl = false;
            runner.clearConsoleAfterBatch = false;
        }

        /// <summary>Apply a frozen candidate policy.</summary>
        public static void ApplyPolicy(AutoRunner runner, UltraPortfolioPolicySpec policy)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            runner.wiringSkill = (AutoRunner.WiringSkill)policy.wiringSkill;
            runner.superHighDifficultyTailMode = policy.superHighDifficultyTailMode;
            runner.superLayer6OptimalCombatRoutine = policy.superLayer6OptimalCombatRoutine;
            runner.lambdaFarmTiles = policy.lambdaFarmTiles;
        }
    }
}

using System;
using System.Collections.Generic;

namespace AutoTest.Ultra
{
    /// <summary>Stable production decision boundaries supported by the Ultra controller.</summary>
    public enum UltraLiveDecisionKind
    {
        Route,
        Shop,
        Event,
        Rest,
        Lambda,
        CombatWiring,
        CombatReroll,
        CombatRole,
    }

    /// <summary>
    /// Public action envelope only. It must not contain future live RNG values or
    /// hidden production state. The checkpoint is exported by the production
    /// checkpoint provider and validated separately.
    /// </summary>
    public sealed class UltraLiveDecisionRequest
    {
        public UltraLiveDecisionKind kind;
        public int runIndex;
        public long runEpoch;
        public string phase;
        public string publicCheckpointBase64;
        public ulong publicStateHash;
        public string baselineActionId;
        public readonly List<UltraCandidate> candidates = new List<UltraCandidate>();
        public UltraProgressSession progressSession;
    }

    public sealed class UltraLiveDecisionResult
    {
        public string actionId;
        public bool useBaseline;
        public string diagnostic;
    }

    public enum UltraPlanningCoverage
    {
        None,
        /// <summary>
        /// Production full-run policy portfolio is evaluated once before the live
        /// batch; the selected policy is then frozen for all 10000 live runs.
        /// </summary>
        RunStartOnly,
    }

    public sealed class UltraPolicyPortfolioRequest
    {
        public UltraAutoRunProfile profile;
        public PolicyParameters baselinePolicy;
        public int productionRunsPerCandidate = 1000;
        public UltraProgressSession progressSession;
        public string expectedBuildFingerprint;
        public string profileFingerprint;
        public string scenarioSeedHash;
        public ulong[] scenarioSeeds;
        public UltraPolicyCandidate[] candidates;
    }

    public sealed class UltraPolicyCandidate
    {
        public string id;
        public string policyFingerprint;
        public PolicyParameters policy;
    }

    public sealed class UltraPolicyCandidateResult
    {
        public string id;
        public string policyFingerprint;
        public string buildFingerprint;
        public string profileFingerprint;
        public string scenarioSeedHash;
        public bool[] validRuns;
        public bool[] fullClearRuns;
        public int[] runFingerprints;
        public string outputPath;
    }

    public sealed class UltraPolicyPortfolioResult
    {
        public string selectedPolicyId;
        public PolicyParameters selectedPolicy;
        public UltraPortfolioPolicySpec selectedRunPolicy;
        public int candidateCount;
        public int productionRunsPerCandidate;
        public UltraPolicyCandidateResult[] candidateResults;
        public string outputPath;
        public string diagnostic;

        /// <summary>Challengers whose per-run fingerprints are almost identical to the
        /// baseline's ── the signature of a configuration difference that never took effect.
        /// Empty is the healthy case. Populated by
        /// <see cref="UltraPortfolioSelector.DetectConfigurationNoOps"/>.</summary>
        public string[] configurationNoOpPolicyIds = new string[0];
        /// <summary>Highest per-run fingerprint agreement against the baseline (0..1).
        /// Reported even when nothing trips, so a drift toward "all candidates identical"
        /// is visible before it crosses the threshold.</summary>
        public double maxDigestAgreement;

        public bool IsUsable(out string reason)
        {
            if (candidateCount <= 0 || productionRunsPerCandidate <= 0)
            {
                reason = "production portfolio did not run";
                return false;
            }
            if (candidateResults == null || candidateResults.Length != candidateCount)
            {
                reason = "production per-candidate vectors are missing or inconsistent";
                return false;
            }
            if (string.IsNullOrEmpty(outputPath))
            {
                reason = "production portfolio output path is empty";
                return false;
            }
            if (selectedPolicy == null || selectedRunPolicy == null
                || !string.Equals(selectedPolicyId, selectedRunPolicy.policyId,
                    StringComparison.Ordinal))
            {
                reason = "trusted selected policy is missing or inconsistent";
                return false;
            }
            reason = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// Pollable boundary between AutoRunner and the production worker controller.
    /// BeginDecision must return quickly; expensive production episodes run outside
    /// Unity's main thread/process. AutoRunner commits only a completed legal result.
    /// </summary>
    public interface IUltraAutoRunController : IDisposable
    {
        UltraPlanningCoverage Coverage { get; }
        string ProductionBuildFingerprint { get; }
        bool TryValidateProduction(out string reason);
        void BindProgress(UltraProgressSession session);
        bool TryBeginPolicyPortfolio(UltraPolicyPortfolioRequest request, out string reason);
        bool TryPollPolicyPortfolio(out bool completed, out UltraPolicyPortfolioResult result, out string reason);
        bool TryBeginRun(int runIndex, out string reason);
        bool TryBeginDecision(UltraLiveDecisionRequest request, out long ticket, out string reason);
        bool TryPollDecision(long ticket, out bool completed, out UltraLiveDecisionResult result, out string reason);
        void CompleteRun(int runIndex, string summary);
        void CompleteBatch(string summary);
        void Cancel(string reason);
    }

    /// <summary>
    /// Runtime registration point for the production controller. No default or fake
    /// controller is installed: an absent/unverified factory is a hard startup failure.
    /// </summary>
    public static class UltraAutoRunBridge
    {
        private static readonly object Gate = new object();
        private static Func<UltraAutoRunProfile, IUltraAutoRunController> _productionFactory;

        public static bool HasProductionFactory
        {
            get { lock (Gate) return _productionFactory != null; }
        }

        public static void InstallProductionFactory(
            Func<UltraAutoRunProfile, IUltraAutoRunController> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (Gate)
            {
                if (_productionFactory != null)
                    throw new InvalidOperationException("Ultra production factory is already installed.");
                _productionFactory = factory;
            }
        }

        public static void RemoveProductionFactory()
        {
            lock (Gate) _productionFactory = null;
        }

        public static bool TryOpen(
            UltraAutoRunProfile profile,
            out IUltraAutoRunController controller,
            out string reason)
        {
            controller = null;
            if (profile == null)
            {
                reason = "Ultra profile is null.";
                return false;
            }
            if (!profile.IsCanonical(out reason)) return false;

            Func<UltraAutoRunProfile, IUltraAutoRunController> factory;
            lock (Gate) factory = _productionFactory;
            if (factory == null)
            {
                reason = "production controller factory is not installed";
                return false;
            }

            try
            {
                controller = factory(profile);
                if (controller == null)
                {
                    reason = "production controller factory returned null";
                    return false;
                }
                if (!controller.TryValidateProduction(out reason))
                {
                    controller.Dispose();
                    controller = null;
                    reason = "production readiness rejected: " + (reason ?? "unknown");
                    return false;
                }
                if (controller.Coverage != UltraPlanningCoverage.RunStartOnly)
                {
                    reason = "unsupported or unreported Ultra coverage: " + controller.Coverage;
                    controller.Dispose();
                    controller = null;
                    return false;
                }
                reason = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                try { controller?.Dispose(); } catch { }
                controller = null;
                reason = "production controller startup failed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}

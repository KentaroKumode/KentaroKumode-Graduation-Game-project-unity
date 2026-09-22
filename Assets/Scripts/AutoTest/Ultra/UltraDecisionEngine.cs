using System;
using System.Collections.Generic;

namespace AutoTest.Ultra
{
    public enum UltraExecutionMode
    {
        Disabled = 0,
        Shadow = 1,
        Enabled = 2,
    }

    public enum UltraFallbackReason
    {
        None = 0,
        Disabled,
        ShadowOnly,
        InvalidRequest,
        OracleUnavailable,
        BuildMismatch,
        EpisodeFailure,
        InsufficientEvidence,
    }

    [Serializable]
    public sealed class UltraDecisionConfig
    {
        public UltraExecutionMode mode = UltraExecutionMode.Shadow;
        public ulong plannerSalt = 0x554C5452415F5631UL; // "ULTRA_V1"。live seed ではない。
        public int searchScenariosPerCandidate = 16;
        public int confirmationScenarios = 64;
        public int maxProductionStepsPerEpisode = 20000;
        public double confirmationAlpha = 0.01;
        public double minimumAdvantage = 0.0;
    }

    [Serializable]
    public sealed class UltraCandidate
    {
        public string id;
        public string payload;

        public UltraCandidate(string id, string payload = null)
        {
            this.id = id;
            this.payload = payload;
        }
    }

    [Serializable]
    public sealed class UltraDecisionRequest
    {
        public int runOrdinal;
        public int decisionOrdinal;
        public ulong publicStateHash;
        public string buildFingerprint;
        public string checkpointSchema;
        public string publicCheckpointBase64;
        public string baselineActionId;
        public List<UltraCandidate> candidates = new List<UltraCandidate>();
        [NonSerialized] public UltraProgressSession progressSession;
    }

    [Serializable]
    public sealed class UltraDecisionResult
    {
        public string selectedActionId;
        public string proposedActionId;
        public UltraFallbackReason fallbackReason;
        public int completedEpisodes;
        public double proposedMean;
        public double baselineMean;
        public double pairedAdvantage;
        public double pairedLowerBound;
        public string diagnostic;
    }

    /// <summary>
    /// Production episode の結果だけを集計する決定器。
    /// Shadow は常に baseline を返す。Enabled も fresh confirmation の下側境界が
    /// baseline を上回らなければ baseline を返す。
    /// </summary>
    public sealed class UltraDecisionEngine
    {
        public const int AbsoluteMaxCandidates = 256;
        public const int AbsoluteMaxSearchScenariosPerCandidate = 2048;
        public const int AbsoluteMaxConfirmationScenarios = 8192;
        public const int AbsoluteMaxProductionStepsPerEpisode = 1_000_000;
        public const int AbsoluteMaxCheckpointChars = 32 * 1024 * 1024;
        public const int AbsoluteMaxActionPayloadChars = 1024 * 1024;
        public const int AbsoluteMaxIdentifierChars = 256;

        private readonly UltraDecisionConfig _config;
        private readonly IUltraProductionOracle _oracle;
        private readonly UltraScenarioBank _scenarios;

        public UltraDecisionEngine(UltraDecisionConfig config, IUltraProductionOracle oracle)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            // Callers may keep and mutate inspector/config instances. A decision engine must
            // operate on one immutable configuration snapshot for deterministic replay.
            _config = new UltraDecisionConfig
            {
                mode = config.mode,
                plannerSalt = config.plannerSalt,
                searchScenariosPerCandidate = config.searchScenariosPerCandidate,
                confirmationScenarios = config.confirmationScenarios,
                maxProductionStepsPerEpisode = config.maxProductionStepsPerEpisode,
                confirmationAlpha = config.confirmationAlpha,
                minimumAdvantage = config.minimumAdvantage,
            };
            _oracle = oracle;
            _scenarios = new UltraScenarioBank(_config.plannerSalt);
        }

        public UltraDecisionResult Decide(UltraDecisionRequest request)
        {
            string baseline = request?.baselineActionId;
            var answer = new UltraDecisionResult
            {
                selectedActionId = baseline,
                proposedActionId = baseline,
                fallbackReason = UltraFallbackReason.InvalidRequest,
            };

            List<UltraCandidate> candidates;
            if (!TryValidate(request, out candidates, out string validationError))
            {
                answer.diagnostic = validationError;
                ReportExecuting(request, validationError);
                return answer;
            }

            request = SnapshotRequest(request);

            if (_config.mode == UltraExecutionMode.Disabled)
            {
                answer.fallbackReason = UltraFallbackReason.Disabled;
                answer.diagnostic = "Ultra disabled";
                ReportExecuting(request, answer.diagnostic);
                return answer;
            }

            if (_oracle == null)
            {
                answer.fallbackReason = UltraFallbackReason.OracleUnavailable;
                answer.diagnostic = "production oracle unavailable";
                ReportExecuting(request, answer.diagnostic);
                return answer;
            }

            bool oracleHealthy;
            string oracleFingerprint;
            try
            {
                oracleHealthy = _oracle.IsHealthy;
                oracleFingerprint = _oracle.BuildFingerprint;
            }
            catch (Exception ex)
            {
                answer.fallbackReason = UltraFallbackReason.OracleUnavailable;
                answer.diagnostic = "production oracle health check failed: " + ex.GetType().Name;
                ReportExecuting(request, answer.diagnostic);
                return answer;
            }

            if (!oracleHealthy)
            {
                answer.fallbackReason = UltraFallbackReason.OracleUnavailable;
                answer.diagnostic = "production oracle unavailable";
                ReportExecuting(request, answer.diagnostic);
                return answer;
            }

            if (!string.Equals(oracleFingerprint, request.buildFingerprint, StringComparison.Ordinal))
            {
                answer.fallbackReason = UltraFallbackReason.BuildMismatch;
                answer.diagnostic = "live/worker build fingerprint mismatch";
                ReportExecuting(request, answer.diagnostic);
                return answer;
            }

            int searchN = Math.Max(1, _config.searchScenariosPerCandidate);
            ReportPhase(
                request,
                UltraProgressPhase.Search,
                (long)candidates.Count * searchN,
                "decision " + request.decisionOrdinal + " / candidate search");
            var searchSums = new double[candidates.Count];
            for (int i = 0; i < searchN; i++)
            {
                ulong seed = _scenarios.GetScenarioSeed(
                    request.publicStateHash,
                    request.runOrdinal,
                    request.decisionOrdinal,
                    UltraScenarioPhase.Search,
                    i);
                // Complete a whole common-scenario round before moving to the next one.
                // This avoids candidate-major warm-up/order bias and gives future parallel
                // implementations an unambiguous deterministic merge boundary.
                for (int c = 0; c < candidates.Count; c++)
                {
                    if (!TryEpisode(request, candidates[c], seed, "search", i, out float reward, out string error))
                        return Fail(answer, UltraFallbackReason.EpisodeFailure, error, request);
                    searchSums[c] += reward;
                    answer.completedEpisodes++;
                    ReportEpisodeCompleted(request, answer.completedEpisodes);
                }
            }

            var searchMeans = new double[candidates.Count];
            for (int c = 0; c < candidates.Count; c++)
                searchMeans[c] = searchSums[c] / searchN;

            int best = 0;
            for (int i = 1; i < candidates.Count; i++)
            {
                if (searchMeans[i] > searchMeans[best]
                    || (searchMeans[i] == searchMeans[best]
                        && string.CompareOrdinal(candidates[i].id, candidates[best].id) < 0))
                    best = i;
            }

            UltraCandidate proposal = candidates[best];
            UltraCandidate baselineCandidate = FindCandidate(candidates, baseline);
            answer.proposedActionId = proposal.id;
            answer.proposedMean = searchMeans[best];
            answer.baselineMean = searchMeans[candidates.IndexOf(baselineCandidate)];

            if (string.Equals(proposal.id, baseline, StringComparison.Ordinal))
            {
                answer.fallbackReason = _config.mode == UltraExecutionMode.Shadow
                    ? UltraFallbackReason.ShadowOnly
                    : UltraFallbackReason.None;
                answer.diagnostic = "baseline ranked first";
                ReportExecuting(request, answer.diagnostic);
                return answer;
            }

            int confirmN = Math.Max(1, _config.confirmationScenarios);
            ReportPhase(
                request,
                UltraProgressPhase.Confirmation,
                (long)confirmN * 2L,
                "fresh proposal vs baseline confirmation");
            long confirmationCompleted = 0;
            double deltaSum = 0.0;
            for (int i = 0; i < confirmN; i++)
            {
                ulong seed = _scenarios.GetScenarioSeed(
                    request.publicStateHash,
                    request.runOrdinal,
                    request.decisionOrdinal,
                    UltraScenarioPhase.Confirmation,
                    i);
                if (!TryEpisode(request, proposal, seed, "confirm-best", i, out float proposed, out string bestError))
                    return Fail(answer, UltraFallbackReason.EpisodeFailure, bestError, request);
                confirmationCompleted++;
                ReportPhaseCompleted(request, confirmationCompleted);
                if (!TryEpisode(request, baselineCandidate, seed, "confirm-base", i, out float baseReward, out string baseError))
                    return Fail(answer, UltraFallbackReason.EpisodeFailure, baseError, request);
                confirmationCompleted++;
                ReportPhaseCompleted(request, confirmationCompleted);
                deltaSum += proposed - baseReward;
                answer.completedEpisodes += 2;
            }

            double meanDelta = deltaSum / confirmN;
            double alpha = Math.Min(0.5, Math.Max(1e-12, _config.confirmationAlpha));
            // Paired delta is in [-1, 1]. One-sided Hoeffding lower bound.
            double radius = Math.Sqrt(2.0 * Math.Log(1.0 / alpha) / confirmN);
            double lower = meanDelta - radius;
            answer.pairedAdvantage = meanDelta;
            answer.pairedLowerBound = lower;

            if (_config.mode == UltraExecutionMode.Shadow)
            {
                answer.fallbackReason = UltraFallbackReason.ShadowOnly;
                answer.diagnostic = "shadow mode: proposal measured but baseline executed";
                ReportExecuting(request, answer.diagnostic);
                return answer;
            }

            if (lower <= _config.minimumAdvantage)
            {
                answer.fallbackReason = UltraFallbackReason.InsufficientEvidence;
                answer.diagnostic = "fresh production evidence did not clear the safety margin";
                ReportExecuting(request, answer.diagnostic);
                return answer;
            }

            answer.selectedActionId = proposal.id;
            answer.fallbackReason = UltraFallbackReason.None;
            answer.diagnostic = "fresh production evidence accepted proposal";
            ReportExecuting(request, answer.diagnostic);
            return answer;
        }

        private bool TryValidate(
            UltraDecisionRequest request,
            out List<UltraCandidate> candidates,
            out string error)
        {
            candidates = null;
            error = null;
            if (_config.mode != UltraExecutionMode.Disabled
                && _config.mode != UltraExecutionMode.Shadow
                && _config.mode != UltraExecutionMode.Enabled)
            { error = "execution mode invalid"; return false; }
            if (_config.searchScenariosPerCandidate < 1
                || _config.searchScenariosPerCandidate > AbsoluteMaxSearchScenariosPerCandidate)
            { error = "search scenario budget out of range"; return false; }
            if (_config.confirmationScenarios < 1
                || _config.confirmationScenarios > AbsoluteMaxConfirmationScenarios)
            { error = "confirmation scenario budget out of range"; return false; }
            if (_config.maxProductionStepsPerEpisode < 1
                || _config.maxProductionStepsPerEpisode > AbsoluteMaxProductionStepsPerEpisode)
            { error = "production step budget out of range"; return false; }
            if (double.IsNaN(_config.confirmationAlpha)
                || double.IsInfinity(_config.confirmationAlpha)
                || _config.confirmationAlpha <= 0.0
                || _config.confirmationAlpha > 0.5)
            { error = "confirmation alpha out of range"; return false; }
            if (double.IsNaN(_config.minimumAdvantage)
                || double.IsInfinity(_config.minimumAdvantage)
                || _config.minimumAdvantage < -1.0
                || _config.minimumAdvantage > 1.0)
            { error = "minimum advantage out of range"; return false; }
            if (request == null) { error = "request is null"; return false; }
            if (request.runOrdinal < 0 || request.decisionOrdinal < 0)
            { error = "negative run/decision ordinal"; return false; }
            if (string.IsNullOrEmpty(request.baselineActionId))
            { error = "baseline action missing"; return false; }
            if (request.baselineActionId.Length > AbsoluteMaxIdentifierChars
                || ContainsControlCharacter(request.baselineActionId))
            { error = "baseline action id invalid"; return false; }
            if (string.IsNullOrEmpty(request.buildFingerprint))
            { error = "build fingerprint missing"; return false; }
            if (request.buildFingerprint.Length > AbsoluteMaxIdentifierChars
                || ContainsControlCharacter(request.buildFingerprint))
            { error = "build fingerprint invalid"; return false; }
            if (string.IsNullOrEmpty(request.checkpointSchema)
                || request.checkpointSchema.Length > AbsoluteMaxIdentifierChars
                || ContainsControlCharacter(request.checkpointSchema))
            { error = "checkpoint schema invalid"; return false; }
            if (request.publicCheckpointBase64 == null
                || request.publicCheckpointBase64.Length > AbsoluteMaxCheckpointChars)
            { error = "public checkpoint invalid"; return false; }
            if (request.candidates == null || request.candidates.Count == 0)
            { error = "candidate list empty"; return false; }
            if (request.candidates.Count > AbsoluteMaxCandidates)
            { error = "candidate count exceeds hard cap"; return false; }

            candidates = new List<UltraCandidate>(request.candidates.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool foundBaseline = false;
            for (int i = 0; i < request.candidates.Count; i++)
            {
                UltraCandidate candidate = request.candidates[i];
                if (candidate == null || string.IsNullOrEmpty(candidate.id))
                { error = "candidate id missing"; return false; }
                if (candidate.id.Length > AbsoluteMaxIdentifierChars
                    || ContainsControlCharacter(candidate.id))
                { error = "candidate id invalid"; return false; }
                if (candidate.payload != null
                    && candidate.payload.Length > AbsoluteMaxActionPayloadChars)
                { error = "candidate payload exceeds hard cap"; return false; }
                if (!seen.Add(candidate.id))
                { error = "duplicate candidate id: " + candidate.id; return false; }
                if (candidate.id == request.baselineActionId) foundBaseline = true;
                candidates.Add(new UltraCandidate(candidate.id, candidate.payload));
            }
            if (!foundBaseline) { error = "baseline is not in candidate list"; return false; }

            // Enumeration order must not decide ties or worker order.
            candidates.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return true;
        }

        private bool TryEpisode(
            UltraDecisionRequest decision,
            UltraCandidate candidate,
            ulong scenarioSeed,
            string stage,
            int scenarioIndex,
            out float reward,
            out string error)
        {
            reward = 0f;
            error = null;
            var episode = new UltraEpisodeRequest
            {
                jobId = decision.runOrdinal + ":" + decision.decisionOrdinal + ":" + stage + ":" + scenarioIndex + ":" + candidate.id,
                expectedBuildFingerprint = decision.buildFingerprint,
                checkpointSchema = decision.checkpointSchema,
                publicCheckpointBase64 = decision.publicCheckpointBase64,
                actionId = candidate.id,
                actionPayload = candidate.payload,
                scenarioStartSeedHex = scenarioSeed.ToString("X16"),
                maxProductionSteps = Math.Max(1, _config.maxProductionStepsPerEpisode),
            };

            UltraEpisodeResult result;
            try
            {
                if (!_oracle.TryRunEpisode(episode, out result))
                {
                    error = "worker call failed for " + episode.jobId;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "worker threw for " + episode.jobId + ": " + ex.GetType().Name;
                return false;
            }
            if (result == null || !result.IsUsable)
            {
                error = "worker returned unusable result for " + episode.jobId
                    + (result == null ? string.Empty : ": " + result.failureCode);
                return false;
            }
            if (result.productionSteps < 0
                || result.productionSteps > episode.maxProductionSteps)
            {
                error = "worker step count exceeded cap for " + episode.jobId;
                return false;
            }
            reward = result.primaryReward;
            return true;
        }

        private static bool ContainsControlCharacter(string value)
        {
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i])) return true;
            return false;
        }

        private static UltraDecisionRequest SnapshotRequest(UltraDecisionRequest source)
        {
            return new UltraDecisionRequest
            {
                runOrdinal = source.runOrdinal,
                decisionOrdinal = source.decisionOrdinal,
                publicStateHash = source.publicStateHash,
                buildFingerprint = source.buildFingerprint,
                checkpointSchema = source.checkpointSchema,
                publicCheckpointBase64 = source.publicCheckpointBase64,
                baselineActionId = source.baselineActionId,
                progressSession = source.progressSession,
            };
        }

        private static void ReportPhase(
            UltraDecisionRequest request,
            UltraProgressPhase phase,
            long total,
            string detail)
        {
            if (request.progressSession.IsValid)
                UltraProgressHub.SetPhase(request.progressSession, phase, total, detail);
        }

        private static void ReportEpisodeCompleted(UltraDecisionRequest request, long completed)
        {
            if (request.progressSession.IsValid)
                UltraProgressHub.ReportPhaseCompleted(request.progressSession, completed);
        }

        private static void ReportPhaseCompleted(UltraDecisionRequest request, long completed)
        {
            if (request.progressSession.IsValid)
                UltraProgressHub.ReportPhaseCompleted(request.progressSession, completed);
        }

        private static void ReportExecuting(UltraDecisionRequest request, string detail)
        {
            if (request == null || !request.progressSession.IsValid) return;
            UltraProgressHub.SetPhase(request.progressSession, UltraProgressPhase.Executing, 1, detail);
            UltraProgressHub.ReportPhaseCompleted(request.progressSession, 1, detail);
        }

        private static UltraCandidate FindCandidate(List<UltraCandidate> candidates, string id)
        {
            for (int i = 0; i < candidates.Count; i++)
                if (string.Equals(candidates[i].id, id, StringComparison.Ordinal))
                    return candidates[i];
            return null;
        }

        private static UltraDecisionResult Fail(
            UltraDecisionResult answer,
            UltraFallbackReason reason,
            string diagnostic,
            UltraDecisionRequest request = null)
        {
            answer.fallbackReason = reason;
            answer.diagnostic = diagnostic;
            ReportExecuting(request, diagnostic);
            return answer;
        }
    }
}

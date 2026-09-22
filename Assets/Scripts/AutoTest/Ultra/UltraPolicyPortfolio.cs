using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AutoTest.Ultra
{
    /// <summary>
    /// The first production Ultra boundary. It deliberately covers only a run start:
    /// workers execute complete production AutoRunner runs and never pretend that an
    /// arbitrary mid-run checkpoint can be restored.
    /// </summary>
    public enum UltraProductionCoverage
    {
        RunStartOnly = 1,
    }

    /// <summary>The measured conditions, carried in the request so a worker can never
    /// fall back on its own defaults.
    ///
    /// <para><b>The mechanical fields exist because of a real failure.</b> On 2026-08-17 the
    /// portfolio workers used AutoRunner defaults while the parent 10K run had inherited
    /// mutual-attack ON / face-part suppression ON / shield-absorption ON from EditorPrefs.
    /// Both sides reported healthy progress; the comparison was meaningless. Anything that
    /// changes an outcome now travels in the profile and is covered by the fingerprint.</para></summary>
    [Serializable]
    public sealed class UltraPortfolioProfileSpec
    {
        public int challengeScore;
        public bool theoreticalBestCursedRelic;
        public int persona;
        public int metaBuffMode;
        public int itemPickMode;
        public int evaluationRuns;

        // ---- mechanical rules (must match UltraAutoRunProfile) ----
        public bool useMutualAttackPipeline;
        public bool suppressFacePartOffers;
        public bool shieldAbsorbsUnmitigable;
        public bool enableAllDebuffs;
        public bool sweepAllMetaAxes;
        public string challengeSpec;
    }

    /// <summary>A frozen, production AutoRunner policy. No learned transition model exists here.</summary>
    [Serializable]
    public sealed class UltraPortfolioPolicySpec
    {
        public string policyId;
        public int wiringSkill;
        public bool superHighDifficultyTailMode;
        public bool superLayer6OptimalCombatRoutine;
        public int lambdaFarmTiles;

        public UltraPortfolioPolicySpec Clone()
        {
            return new UltraPortfolioPolicySpec
            {
                policyId = policyId,
                wiringSkill = wiringSkill,
                superHighDifficultyTailMode = superHighDifficultyTailMode,
                superLayer6OptimalCombatRoutine = superLayer6OptimalCombatRoutine,
                lambdaFarmTiles = lambdaFarmTiles,
            };
        }
    }

    [Serializable]
    public sealed class UltraPortfolioWorkerRequest
    {
        public int protocolVersion = UltraPortfolioProtocol.CurrentProtocolVersion;
        public string kind = UltraPortfolioProtocol.RequestKind;
        public string requestId;
        public string decisionNonce;
        public int coverage = (int)UltraProductionCoverage.RunStartOnly;
        public UltraFingerprintSet expectedFingerprints;
        public UltraPortfolioProfileSpec profile;
        public string profileFingerprint;
        public string syntheticMasterSeedHex;
        public int[] runOrdinals = new int[0];
        public string seedVectorHash;
        public UltraPortfolioPolicySpec[] candidates = new UltraPortfolioPolicySpec[0];
        public string candidateSetHash;
        public string baselinePolicyId;
    }

    [Serializable]
    public sealed class UltraPortfolioEvaluation
    {
        public string policyId;
        public bool[] valid = new bool[0];
        public bool[] fullClear = new bool[0];
        public string[] runDigestSha256 = new string[0];
    }

    [Serializable]
    public sealed class UltraPortfolioWorkerResponse
    {
        public int protocolVersion = UltraPortfolioProtocol.CurrentProtocolVersion;
        public string kind = UltraPortfolioProtocol.ResponseKind;
        public string requestId;
        public string decisionNonce;
        public bool accepted;
        public UltraFingerprintSet workerFingerprints;
        public string profileFingerprint;
        public string seedVectorHash;
        public string candidateSetHash;
        public UltraPortfolioEvaluation[] evaluations = new UltraPortfolioEvaluation[0];
        /// <summary>Canonical path relative to the controller-approved artifact root.</summary>
        public string artifactRelativePath;
        public string artifactSha256;
        public UltraWorkerFailure failure;
    }

    public static class UltraPortfolioProtocol
    {
        public const int CurrentProtocolVersion = 1;
        public const string RequestKind = "ultra.portfolio.request";
        public const string ResponseKind = "ultra.portfolio.response";
        public const string CoverageId = "run-start-only-v1";
        public const string BaselinePolicyId = "super-baseline-v1";
        public const int CanonicalEvaluationRuns = 1000;
        public const int MaxJsonBytes = 32 * 1024 * 1024;

        private static readonly string[] RequestProperties =
        {
            "protocolVersion", "kind", "requestId", "decisionNonce", "coverage",
            "expectedFingerprints", "buildFingerprint", "dataFingerprint",
            "checkpointFingerprint", "actionFingerprint", "objectiveFingerprint",
            "profile", "challengeScore", "theoreticalBestCursedRelic", "persona",
            "metaBuffMode", "itemPickMode", "evaluationRuns",
            "useMutualAttackPipeline", "suppressFacePartOffers",
            "shieldAbsorbsUnmitigable", "enableAllDebuffs", "sweepAllMetaAxes",
            "challengeSpec", "profileFingerprint",
            "syntheticMasterSeedHex", "runOrdinals", "seedVectorHash", "candidates",
            "policyId", "wiringSkill", "superHighDifficultyTailMode",
            "superLayer6OptimalCombatRoutine", "lambdaFarmTiles", "candidateSetHash",
            "baselinePolicyId",
        };

        private static readonly string[] ResponseProperties =
        {
            "protocolVersion", "kind", "requestId", "decisionNonce", "accepted",
            "workerFingerprints", "buildFingerprint", "dataFingerprint",
            "checkpointFingerprint", "actionFingerprint", "objectiveFingerprint",
            "profileFingerprint", "seedVectorHash", "candidateSetHash", "evaluations",
            "policyId", "valid", "fullClear", "runDigestSha256", "artifactRelativePath",
            "artifactSha256", "failure", "code", "detail",
        };

        public static UltraPortfolioWorkerRequest CreateCanonicalRequest(
            string requestId,
            string decisionNonce,
            UltraFingerprintSet expectedFingerprints,
            ulong syntheticMasterSeed,
            int evaluationRuns = CanonicalEvaluationRuns)
        {
            if (evaluationRuns != CanonicalEvaluationRuns)
                throw new ArgumentOutOfRangeException(nameof(evaluationRuns),
                    "The first production gate uses exactly 1000 paired runs per policy.");

            var ordinals = new int[evaluationRuns];
            for (int i = 0; i < ordinals.Length; i++) ordinals[i] = i;
            var profile = CanonicalProfile(evaluationRuns);
            var candidates = CanonicalCandidates();
            string master = syntheticMasterSeed.ToString("X16", CultureInfo.InvariantCulture);
            return new UltraPortfolioWorkerRequest
            {
                requestId = requestId,
                decisionNonce = decisionNonce,
                expectedFingerprints = expectedFingerprints?.Clone(),
                profile = profile,
                profileFingerprint = ComputeProfileFingerprint(profile),
                syntheticMasterSeedHex = master,
                runOrdinals = ordinals,
                seedVectorHash = ComputeSeedVectorHash(master, ordinals),
                candidates = candidates,
                candidateSetHash = ComputeCandidateSetHash(candidates),
                baselinePolicyId = BaselinePolicyId,
            };
        }

        /// <summary>The canonical profile is derived from <see cref="UltraAutoRunProfile"/>
        /// rather than restated. Two hand-written copies of the same conditions is exactly
        /// how the worker/parent mismatch happened in the first place.</summary>
        public static UltraPortfolioProfileSpec CanonicalProfile(int evaluationRuns = CanonicalEvaluationRuns)
        {
            UltraAutoRunProfile run = UltraAutoRunProfile.CreateStandard50WithRelic10000();
            return new UltraPortfolioProfileSpec
            {
                challengeScore = run.challengeScore,
                theoreticalBestCursedRelic = run.theoreticalBestCursedRelic,
                persona = (int)run.persona,
                metaBuffMode = (int)run.metaBuffMode,
                itemPickMode = (int)run.itemPickMode,
                evaluationRuns = evaluationRuns,

                useMutualAttackPipeline = run.useMutualAttackPipeline,
                suppressFacePartOffers = run.suppressFacePartOffers,
                shieldAbsorbsUnmitigable = run.shieldAbsorbsUnmitigable,
                enableAllDebuffs = run.enableAllDebuffs,
                sweepAllMetaAxes = run.sweepAllMetaAxes,
                challengeSpec = run.challengeSpec ?? string.Empty,
            };
        }

        public static UltraPortfolioPolicySpec[] CanonicalCandidates()
        {
            // Candidate order is a protocol property, not an implementation detail.
            return new[]
            {
                Policy(BaselinePolicyId, AutoRunner.WiringSkill.Super, true, true, 6),
                Policy("optimal-v1", AutoRunner.WiringSkill.Optimal, false, false, 6),
                Policy("super-pure-v1", AutoRunner.WiringSkill.Super, true, false, 6),
                Policy("super-high-variance-v1", AutoRunner.WiringSkill.Super, true, true, -3),
            };
        }

        public static bool TryValidateRequest(UltraPortfolioWorkerRequest request, out string failure)
        {
            failure = null;
            if (request == null) return Fail("request_missing", out failure);
            if (request.protocolVersion != CurrentProtocolVersion) return Fail("protocol_version", out failure);
            if (!string.Equals(request.kind, RequestKind, StringComparison.Ordinal)) return Fail("request_kind", out failure);
            if (!IsToken(request.requestId, 256)) return Fail("request_id", out failure);
            if (!IsSha256(request.decisionNonce)) return Fail("decision_nonce", out failure);
            if (request.coverage != (int)UltraProductionCoverage.RunStartOnly) return Fail("coverage", out failure);
            if (!UltraBuildManifestValidation.TryValidateFingerprints(
                    request.expectedFingerprints, out string code, out _))
                return Fail(code ?? "fingerprints", out failure);
            if (!TryValidateCanonicalProfile(request.profile, out failure)) return false;
            if (!Same(request.profileFingerprint, ComputeProfileFingerprint(request.profile)))
                return Fail("profile_fingerprint", out failure);
            if (!IsHex64(request.syntheticMasterSeedHex)) return Fail("synthetic_master_seed", out failure);
            if (!TryValidateOrdinals(request.runOrdinals, request.profile.evaluationRuns, out failure)) return false;
            if (!Same(request.seedVectorHash,
                    ComputeSeedVectorHash(request.syntheticMasterSeedHex, request.runOrdinals)))
                return Fail("seed_vector_hash", out failure);
            if (!TryValidateCanonicalCandidates(request.candidates, out failure)) return false;
            if (!Same(request.candidateSetHash, ComputeCandidateSetHash(request.candidates)))
                return Fail("candidate_set_hash", out failure);
            if (!Same(request.baselinePolicyId, BaselinePolicyId)) return Fail("baseline_policy", out failure);
            return true;
        }

        public static bool TryValidateResponse(
            UltraPortfolioWorkerResponse response,
            UltraPortfolioWorkerRequest request,
            out string failure)
        {
            failure = null;
            if (!TryValidateRequest(request, out failure)) return false;
            if (response == null) return Fail("response_missing", out failure);
            if (response.protocolVersion != CurrentProtocolVersion) return Fail("protocol_version", out failure);
            if (!Same(response.kind, ResponseKind)) return Fail("response_kind", out failure);
            if (!Same(response.requestId, request.requestId)) return Fail("request_id_mismatch", out failure);
            if (!Same(response.decisionNonce, request.decisionNonce)) return Fail("decision_nonce_mismatch", out failure);
            if (!UltraBuildManifestValidation.TryMatchFingerprintsExactly(
                    request.expectedFingerprints,
                    response.workerFingerprints,
                    out string fingerprintCode,
                    out _))
                return Fail(fingerprintCode ?? "fingerprint_mismatch", out failure);
            if (!response.accepted)
                return Fail(response.failure != null && IsToken(response.failure.code, 128)
                    ? "worker_rejected:" + response.failure.code : "worker_rejected", out failure);
            if (response.failure != null && !string.IsNullOrEmpty(response.failure.code))
                return Fail("accepted_with_failure", out failure);
            if (!Same(response.profileFingerprint, request.profileFingerprint))
                return Fail("profile_fingerprint_mismatch", out failure);
            if (!Same(response.seedVectorHash, request.seedVectorHash))
                return Fail("seed_vector_hash_mismatch", out failure);
            if (!Same(response.candidateSetHash, request.candidateSetHash))
                return Fail("candidate_set_hash_mismatch", out failure);
            if (!IsCanonicalRelativePath(response.artifactRelativePath))
                return Fail("artifact_path", out failure);
            if (!IsSha256(response.artifactSha256)) return Fail("artifact_hash", out failure);
            if (response.evaluations == null || response.evaluations.Length != request.candidates.Length)
                return Fail("evaluation_count", out failure);

            var seenPolicies = new HashSet<string>(StringComparer.Ordinal);
            for (int c = 0; c < request.candidates.Length; c++)
            {
                UltraPortfolioEvaluation evaluation = response.evaluations[c];
                if (evaluation == null) return Fail("evaluation_missing", out failure);
                if (!Same(evaluation.policyId, request.candidates[c].policyId))
                    return Fail("evaluation_order", out failure);
                if (!seenPolicies.Add(evaluation.policyId)) return Fail("evaluation_duplicate", out failure);
                int n = request.runOrdinals.Length;
                if (evaluation.valid == null || evaluation.valid.Length != n
                    || evaluation.fullClear == null || evaluation.fullClear.Length != n
                    || evaluation.runDigestSha256 == null || evaluation.runDigestSha256.Length != n)
                    return Fail("evaluation_vector_length", out failure);

                var digests = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < n; i++)
                {
                    if (evaluation.fullClear[i] && !evaluation.valid[i])
                        return Fail("clear_without_valid", out failure);
                    if (!IsSha256(evaluation.runDigestSha256[i]))
                        return Fail("run_digest", out failure);
                    // Digest includes the ordinal by contract, hence duplicate values prove
                    // that the worker did not emit the required per-run production digest.
                    if (!digests.Add(evaluation.runDigestSha256[i]))
                        return Fail("run_digest_duplicate", out failure);
                }
            }

            // A production seed may reproducibly hit the runner's own deadlock/crash
            // guard.  It is still usable as portfolio evidence only when every policy
            // reports that same ordinal invalid; candidate-specific failures would bias
            // the paired comparison and remain a hard rejection.
            int commonInvalid = 0;
            int runCount = request.runOrdinals.Length;
            for (int i = 0; i < runCount; i++)
            {
                bool valid = response.evaluations[0].valid[i];
                for (int c = 1; c < response.evaluations.Length; c++)
                    if (response.evaluations[c].valid[i] != valid)
                        return Fail("candidate_specific_invalid_run", out failure);
                if (!valid) commonInvalid++;
            }
            // Preserve a useful evidence quorum while tolerating a small shared set.
            if (commonInvalid > Math.Max(5, runCount / 20))
                return Fail("too_many_common_invalid_runs", out failure);
            return true;
        }

        public static bool TryVerifyArtifact(
            string approvedArtifactRoot,
            UltraPortfolioWorkerResponse response,
            out string fullPath,
            out string failure)
        {
            fullPath = null;
            failure = null;
            if (string.IsNullOrWhiteSpace(approvedArtifactRoot)) return Fail("artifact_root", out failure);
            if (response == null || !IsCanonicalRelativePath(response.artifactRelativePath)
                || !IsSha256(response.artifactSha256))
                return Fail("artifact_metadata", out failure);
            try
            {
                string root = Path.GetFullPath(approvedArtifactRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(
                    root, response.artifactRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return Fail("artifact_escape", out failure);
                if (!File.Exists(candidate)) return Fail("artifact_missing", out failure);
                string actual = Sha256File(candidate);
                if (!Same(actual, response.artifactSha256)) return Fail("artifact_content_mismatch", out failure);
                fullPath = candidate;
                return true;
            }
            catch (Exception ex)
            {
                return Fail("artifact_verify:" + ex.GetType().Name, out failure);
            }
        }

        public static bool TryRequestToJson(
            UltraPortfolioWorkerRequest request, out string json, out string failure)
        {
            json = null;
            if (!TryValidateRequest(request, out failure)) return false;
            try { json = JsonUtility.ToJson(request, false); }
            catch (Exception ex) { return Fail("request_serialize:" + ex.GetType().Name, out failure); }
            if (!UltraWorkerProtocol.IsUtf8SizeWithin(json, MaxJsonBytes))
            { json = null; return Fail("request_size", out failure); }
            return true;
        }

        public static bool TryRequestFromJson(
            string json, out UltraPortfolioWorkerRequest request, out string failure)
        {
            request = null;
            failure = null;
            if (!UltraWorkerProtocol.IsUtf8SizeWithin(json, MaxJsonBytes)) return Fail("request_size", out failure);
            if (!UltraWorkerProtocol.TryEnsureKnownJsonProperties(json, RequestProperties, out failure)) return false;
            try { request = JsonUtility.FromJson<UltraPortfolioWorkerRequest>(json); }
            catch (Exception ex) { return Fail("request_parse:" + ex.GetType().Name, out failure); }
            if (!TryValidateRequest(request, out failure)) { request = null; return false; }
            return true;
        }

        public static bool TryResponseToJson(
            UltraPortfolioWorkerResponse response,
            UltraPortfolioWorkerRequest request,
            out string json,
            out string failure)
        {
            json = null;
            if (!TryValidateResponse(response, request, out failure)) return false;
            try { json = JsonUtility.ToJson(response, false); }
            catch (Exception ex) { return Fail("response_serialize:" + ex.GetType().Name, out failure); }
            if (!UltraWorkerProtocol.IsUtf8SizeWithin(json, MaxJsonBytes))
            { json = null; return Fail("response_size", out failure); }
            return true;
        }

        public static bool TryResponseFromJson(
            string json,
            UltraPortfolioWorkerRequest request,
            out UltraPortfolioWorkerResponse response,
            out string failure)
        {
            response = null;
            failure = null;
            if (!UltraWorkerProtocol.IsUtf8SizeWithin(json, MaxJsonBytes)) return Fail("response_size", out failure);
            if (!UltraWorkerProtocol.TryEnsureKnownJsonProperties(json, ResponseProperties, out failure)) return false;
            try { response = JsonUtility.FromJson<UltraPortfolioWorkerResponse>(json); }
            catch (Exception ex) { return Fail("response_parse:" + ex.GetType().Name, out failure); }
            if (!TryValidateResponse(response, request, out failure)) { response = null; return false; }
            return true;
        }

        /// <summary>Profile fingerprint.
        ///
        /// <para><b>v2 (2026-08-18): the mechanical rules are inside the hash.</b> Under v1 a
        /// worker running different combat rules produced a matching fingerprint, so the
        /// mismatch could not be detected by the protocol. Bumping the version also
        /// invalidates every v1 request/response left on disk, which is intended — those
        /// artifacts were produced under unverified conditions.</para></summary>
        public static string ComputeProfileFingerprint(UltraPortfolioProfileSpec profile)
        {
            if (profile == null) return null;
            return Sha256Text(
                "ultra-portfolio-profile-v2\n"
                + profile.challengeScore + "\n"
                + (profile.theoreticalBestCursedRelic ? "1" : "0") + "\n"
                + profile.persona + "\n" + profile.metaBuffMode + "\n"
                + profile.itemPickMode + "\n" + profile.evaluationRuns + "\n"
                + (profile.useMutualAttackPipeline ? "1" : "0") + "\n"
                + (profile.suppressFacePartOffers ? "1" : "0") + "\n"
                + (profile.shieldAbsorbsUnmitigable ? "1" : "0") + "\n"
                + (profile.enableAllDebuffs ? "1" : "0") + "\n"
                + (profile.sweepAllMetaAxes ? "1" : "0") + "\n"
                + (profile.challengeSpec ?? string.Empty) + "\n");
        }

        public static string ComputeSeedVectorHash(string masterSeedHex, int[] runOrdinals)
        {
            if (masterSeedHex == null || runOrdinals == null) return null;
            var sb = new StringBuilder(64 + runOrdinals.Length * 6);
            sb.Append("ultra-portfolio-seeds-v1\n").Append(masterSeedHex.ToUpperInvariant()).Append('\n');
            for (int i = 0; i < runOrdinals.Length; i++)
                sb.Append(i).Append(':').Append(runOrdinals[i]).Append('\n');
            return Sha256Text(sb.ToString());
        }

        public static ulong[] DeriveScenarioSeeds(string masterSeedHex, int[] runOrdinals)
        {
            if (!IsHex64(masterSeedHex) || runOrdinals == null)
                throw new ArgumentException("Invalid Ultra scenario seed input.");
            ulong master = ulong.Parse(masterSeedHex, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
            var result = new ulong[runOrdinals.Length];
            for (int i = 0; i < runOrdinals.Length; i++)
            {
                unchecked
                {
                    ulong z = master + 0x9E3779B97F4A7C15UL
                        * ((ulong)(uint)runOrdinals[i] + 1UL);
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    result[i] = z ^ (z >> 31);
                }
            }
            return result;
        }

        public static string ComputeCandidateSetHash(UltraPortfolioPolicySpec[] candidates)
        {
            if (candidates == null) return null;
            var sb = new StringBuilder(512);
            sb.Append("ultra-portfolio-candidates-v1\n");
            for (int i = 0; i < candidates.Length; i++)
            {
                UltraPortfolioPolicySpec p = candidates[i];
                if (p == null) { sb.Append("null\n"); continue; }
                sb.Append(i).Append('|').Append(p.policyId).Append('|').Append(p.wiringSkill).Append('|')
                    .Append(p.superHighDifficultyTailMode ? '1' : '0').Append('|')
                    .Append(p.superLayer6OptimalCombatRoutine ? '1' : '0').Append('|')
                    .Append(p.lambdaFarmTiles).Append('\n');
            }
            return Sha256Text(sb.ToString());
        }

        public static string ComputeRunDigest(
            string policyId,
            string syntheticMasterSeedHex,
            int runOrdinal,
            UltraProductionRunRecord record)
        {
            if (record == null) return null;
            return Sha256Text("ultra-production-run-v1\n" + policyId + "\n"
                + syntheticMasterSeedHex + "\n" + runOrdinal + "\n"
                + (record.valid ? "1" : "0") + (record.fullClear ? "1" : "0")
                + (record.crash ? "1" : "0") + (record.deadlock ? "1" : "0")
                + "\n" + record.fingerprint + "\n");
        }

        private static UltraPortfolioPolicySpec Policy(
            string id, AutoRunner.WiringSkill skill, bool tail, bool layer6, int lambda)
        {
            return new UltraPortfolioPolicySpec
            {
                policyId = id,
                wiringSkill = (int)skill,
                superHighDifficultyTailMode = tail,
                superLayer6OptimalCombatRoutine = layer6,
                lambdaFarmTiles = lambda,
            };
        }

        private static bool TryValidateCanonicalProfile(UltraPortfolioProfileSpec profile, out string failure)
        {
            failure = null;
            UltraPortfolioProfileSpec expected = CanonicalProfile();
            if (profile == null
                || profile.challengeScore != expected.challengeScore
                || profile.theoreticalBestCursedRelic != expected.theoreticalBestCursedRelic
                || profile.persona != expected.persona
                || profile.metaBuffMode != expected.metaBuffMode
                || profile.itemPickMode != expected.itemPickMode
                || profile.evaluationRuns != expected.evaluationRuns)
                return Fail("profile_not_canonical", out failure);
            if (profile.useMutualAttackPipeline != expected.useMutualAttackPipeline
                || profile.suppressFacePartOffers != expected.suppressFacePartOffers
                || profile.shieldAbsorbsUnmitigable != expected.shieldAbsorbsUnmitigable
                || profile.enableAllDebuffs != expected.enableAllDebuffs
                || profile.sweepAllMetaAxes != expected.sweepAllMetaAxes
                || !Same(profile.challengeSpec ?? string.Empty,
                         expected.challengeSpec ?? string.Empty))
                return Fail("profile_mechanics_not_canonical", out failure);
            return true;
        }

        private static bool TryValidateOrdinals(int[] ordinals, int expected, out string failure)
        {
            failure = null;
            if (ordinals == null || ordinals.Length != expected) return Fail("run_ordinal_count", out failure);
            for (int i = 0; i < ordinals.Length; i++)
                if (ordinals[i] != i) return Fail("run_ordinal_order", out failure);
            return true;
        }

        private static bool TryValidateCanonicalCandidates(
            UltraPortfolioPolicySpec[] candidates, out string failure)
        {
            failure = null;
            UltraPortfolioPolicySpec[] expected = CanonicalCandidates();
            if (candidates == null || candidates.Length != expected.Length)
                return Fail("candidate_count", out failure);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < expected.Length; i++)
            {
                UltraPortfolioPolicySpec a = candidates[i];
                UltraPortfolioPolicySpec b = expected[i];
                if (a == null || !ids.Add(a.policyId)
                    || !Same(a.policyId, b.policyId)
                    || a.wiringSkill != b.wiringSkill
                    || a.superHighDifficultyTailMode != b.superHighDifficultyTailMode
                    || a.superLayer6OptimalCombatRoutine != b.superLayer6OptimalCombatRoutine
                    || a.lambdaFarmTiles != b.lambdaFarmTiles)
                    return Fail("candidate_not_canonical", out failure);
            }
            return true;
        }

        private static bool IsToken(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maxChars) return false;
            for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return false;
            return true;
        }

        internal static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        private static bool IsHex64(string value)
        {
            if (value == null || value.Length != 16) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        internal static bool IsCanonicalRelativePath(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 1024 || value[0] == '/'
                || value[value.Length - 1] == '/' || value.IndexOf('\\') >= 0
                || value.IndexOf(':') >= 0 || value.IndexOf("//", StringComparison.Ordinal) >= 0)
                return false;
            string[] parts = value.Split('/');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length == 0 || parts[i] == "." || parts[i] == "..") return false;
            return true;
        }

        internal static string Sha256Text(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return Hex(bytes);
            }
        }

        internal static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path)) return Hex(sha.ComputeHash(stream));
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

        private static bool Fail(string code, out string failure)
        {
            failure = code;
            return false;
        }
    }

    [Serializable]
    public sealed class UltraPortfolioSelectionResult
    {
        public bool accepted;
        public string selectedPolicyId;
        public string baselinePolicyId;
        public int baselineClears;
        public int selectedClears;
        public int pairedCandidateOnly;
        public int pairedBaselineOnly;
        public double oneSidedP;
        public double correctedAlpha;
        public string diagnostic;
    }

    /// <summary>
    /// Trusted controller-side selector. A worker returns observations only and can
    /// never nominate the production policy that will be committed to the 10K batch.
    /// </summary>
    public static class UltraPortfolioSelector
    {
        public static UltraPortfolioSelectionResult SelectTrusted(
            UltraPortfolioWorkerRequest request,
            UltraPortfolioWorkerResponse response,
            double familyAlpha = 0.01)
        {
            var answer = new UltraPortfolioSelectionResult
            {
                accepted = false,
                baselinePolicyId = UltraPortfolioProtocol.BaselinePolicyId,
                selectedPolicyId = UltraPortfolioProtocol.BaselinePolicyId,
                oneSidedP = 1.0,
            };
            if (double.IsNaN(familyAlpha) || double.IsInfinity(familyAlpha)
                || familyAlpha <= 0.0 || familyAlpha > 0.5)
            {
                answer.diagnostic = "family alpha is invalid";
                return answer;
            }
            if (!UltraPortfolioProtocol.TryValidateResponse(response, request, out string failure))
            {
                answer.diagnostic = "portfolio evidence rejected: " + failure;
                return answer;
            }

            UltraPortfolioEvaluation baseline = response.evaluations[0];
            int baselineClears = CountTrue(baseline.fullClear);
            int pairedValidCount = CountTrue(baseline.valid);
            int comparisons = request.candidates.Length - 1;
            double corrected = familyAlpha / Math.Max(1, comparisons);
            answer.correctedAlpha = corrected;
            answer.baselineClears = baselineClears;
            answer.selectedClears = baselineClears;
            answer.accepted = true;
            answer.diagnostic = "baseline retained: no challenger cleared the corrected paired gate";

            double bestDelta = 0.0;
            double bestP = 1.0;
            string bestId = request.baselinePolicyId;
            int bestB = 0, bestC = 0, bestClears = baselineClears;
            for (int c = 1; c < response.evaluations.Length; c++)
            {
                UltraPortfolioEvaluation challenger = response.evaluations[c];
                int b = 0, against = 0;
                for (int i = 0; i < baseline.fullClear.Length; i++)
                {
                    if (!baseline.valid[i]) continue;
                    if (challenger.fullClear[i] && !baseline.fullClear[i]) b++;
                    else if (!challenger.fullClear[i] && baseline.fullClear[i]) against++;
                }
                if (b <= against) continue;
                double p = OneSidedExactMcNemarP(b, against);
                if (p > corrected) continue;
                double delta = (b - against) / (double)Math.Max(1, pairedValidCount);
                if (delta > bestDelta
                    || (delta == bestDelta && (p < bestP
                        || (p == bestP && string.CompareOrdinal(challenger.policyId, bestId) < 0))))
                {
                    bestDelta = delta;
                    bestP = p;
                    bestId = challenger.policyId;
                    bestB = b;
                    bestC = against;
                    bestClears = CountTrue(challenger.fullClear);
                }
            }

            if (!string.Equals(bestId, request.baselinePolicyId, StringComparison.Ordinal))
            {
                answer.selectedPolicyId = bestId;
                answer.selectedClears = bestClears;
                answer.pairedCandidateOnly = bestB;
                answer.pairedBaselineOnly = bestC;
                answer.oneSidedP = bestP;
                answer.diagnostic = "challenger accepted by controller-side paired exact McNemar gate";
            }

            return answer;
        }

        /// <summary>Digest agreement above this fraction means the candidate is probably not a
        /// distinct configuration at all.
        ///
        /// <para><b>Calibrated from the 2026-08-17 failure, not chosen for roundness.</b> That
        /// session ran every candidate with the mutual-attack pipeline off, so no wiring policy
        /// was ever installed and all four behaved identically — yet agreement measured
        /// <b>98.8%</b>, not 100%, because a handful of runs still diverged. The originally
        /// proposed "warn when digests are 100% identical" test would therefore have stayed
        /// silent through the exact event it was meant to catch.</para>
        ///
        /// <para>The healthy re-run separates cleanly: 1.2% (different combat AI), 74.8%
        /// (differs from the Λ decision on), 86.8% (differs only at layer 6). 0.95 sits above
        /// every legitimate value and below the broken one. A candidate that legitimately
        /// differs only in a rarely-reached branch could still trip it — this is a warning,
        /// never a gate, for that reason.</para></summary>
        public const double NoOpAgreementThreshold = 0.95;

        /// <summary>Minimum comparable runs before the agreement figure means anything.</summary>
        public const int NoOpMinComparableRuns = 50;

        /// <summary>Flag challengers whose per-run outcomes are almost identical to the
        /// baseline's — the sign that a policy difference never reached the game.
        ///
        /// <para><b>This must use the run fingerprint, not <c>runDigestSha256</c>.</b>
        /// <see cref="UltraPortfolioProtocol.ComputeRunDigest"/> salts the hash with
        /// <c>policyId</c>, so those digests differ between candidates by construction and
        /// agreement is always 0% — a check built on them would look healthy no matter how
        /// broken the run was. <see cref="UltraPolicyCandidateResult.runFingerprints"/> is
        /// policy-independent and is what actually diverges when the policies do.</para></summary>
        /// <param name="candidates">Per-candidate vectors, baseline at index 0.</param>
        /// <param name="suspects">Policy ids at or above <see cref="NoOpAgreementThreshold"/>.</param>
        /// <param name="maxAgreement">Highest agreement seen (0..1), reported even when nothing trips.</param>
        public static void DetectConfigurationNoOps(
            UltraPolicyCandidateResult[] candidates,
            out string[] suspects,
            out double maxAgreement)
        {
            suspects = new string[0];
            maxAgreement = 0.0;
            if (candidates == null || candidates.Length < 2) return;

            UltraPolicyCandidateResult baseline = candidates[0];
            if (baseline?.runFingerprints == null || baseline.validRuns == null) return;

            var found = new List<string>();
            for (int c = 1; c < candidates.Length; c++)
            {
                UltraPolicyCandidateResult challenger = candidates[c];
                if (challenger?.runFingerprints == null || challenger.validRuns == null) continue;

                int n = Math.Min(baseline.runFingerprints.Length, challenger.runFingerprints.Length);
                n = Math.Min(n, Math.Min(baseline.validRuns.Length, challenger.validRuns.Length));
                int compared = 0, same = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!baseline.validRuns[i] || !challenger.validRuns[i]) continue;
                    compared++;
                    if (baseline.runFingerprints[i] == challenger.runFingerprints[i]) same++;
                }
                if (compared < NoOpMinComparableRuns) continue;

                double agreement = same / (double)compared;
                if (agreement > maxAgreement) maxAgreement = agreement;
                if (agreement >= NoOpAgreementThreshold) found.Add(challenger.id);
            }
            suspects = found.ToArray();
        }

        /// <summary>One line describing the no-op check, for the console and the diagnostic.</summary>
        public static string DescribeNoOpCheck(string[] suspects, double maxAgreement)
        {
            string agree = string.Format(CultureInfo.InvariantCulture,
                "max digest agreement vs baseline {0:P1}", maxAgreement);
            if (suspects == null || suspects.Length == 0) return "candidates diverge (" + agree + ")";
            return "CONFIGURATION NO-OP SUSPECTED: " + string.Join(", ", suspects)
                + " (" + agree + "; the policy difference probably never took effect,"
                + " so the comparison measures nothing)";
        }

        public static UltraPortfolioPolicySpec GetSelectedPolicyClone(
            UltraPortfolioWorkerRequest request,
            UltraPortfolioSelectionResult selection)
        {
            if (request == null || selection == null || !selection.accepted) return null;
            for (int i = 0; i < request.candidates.Length; i++)
                if (string.Equals(request.candidates[i].policyId,
                    selection.selectedPolicyId, StringComparison.Ordinal))
                    return request.candidates[i].Clone();
            return null;
        }

        internal static double OneSidedExactMcNemarP(int candidateOnly, int baselineOnly)
        {
            int n = candidateOnly + baselineOnly;
            if (n <= 0 || candidateOnly <= baselineOnly) return 1.0;
            // P[X >= candidateOnly], X~Binomial(n, .5). n is capped at 1000.
            double probability = Math.Pow(0.5, n);
            double sum = 0.0;
            for (int k = 0; k <= n; k++)
            {
                if (k >= candidateOnly) sum += probability;
                if (k < n) probability *= (n - k) / (double)(k + 1);
            }
            return Math.Min(1.0, Math.Max(0.0, sum));
        }

        private static int CountTrue(bool[] values)
        {
            int n = 0;
            for (int i = 0; i < values.Length; i++) if (values[i]) n++;
            return n;
        }
    }
}

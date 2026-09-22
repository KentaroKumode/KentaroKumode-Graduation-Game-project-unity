using System;
using UnityEngine;

namespace AutoTest.Ultra
{
    /// <summary>
    /// Compatibility identity shared by the live controller and every production worker.
    /// All fields are mandatory and comparisons are ordinal, full-string comparisons.
    /// These identify schemas/rules, not an individual episode's live RNG state.
    /// </summary>
    [Serializable]
    public sealed class UltraFingerprintSet
    {
        public string buildFingerprint;
        public string dataFingerprint;
        public string checkpointFingerprint;
        public string actionFingerprint;
        public string objectiveFingerprint;

        public UltraFingerprintSet Clone()
        {
            return new UltraFingerprintSet
            {
                buildFingerprint = buildFingerprint,
                dataFingerprint = dataFingerprint,
                checkpointFingerprint = checkpointFingerprint,
                actionFingerprint = actionFingerprint,
                objectiveFingerprint = objectiveFingerprint,
            };
        }
    }

    /// <summary>
    /// Optional audit entry. The aggregate data/build fingerprints remain authoritative;
    /// entries make a mismatch diagnosable without weakening exact-match validation.
    /// </summary>
    [Serializable]
    public sealed class UltraBuildArtifact
    {
        public string category;
        public string relativePath;
        public string contentFingerprint;
        public long byteLength;
    }

    /// <summary>
    /// Immutable manifest copied beside the worker executable.
    /// The build pipeline must populate it from the exact executable and data snapshot
    /// that the worker will use.
    /// </summary>
    [Serializable]
    public sealed class UltraBuildManifest
    {
        public const int CurrentManifestVersion = 1;

        public string kind = UltraWorkerProtocol.BuildManifestKind;
        public int manifestVersion = CurrentManifestVersion;
        public int protocolVersion = UltraWorkerProtocol.CurrentProtocolVersion;
        public string workerBuildId;
        public string unityVersion;
        public string buildTarget;
        public string generatedUtc;
        public UltraFingerprintSet fingerprints = new UltraFingerprintSet();
        public UltraBuildArtifact[] artifacts = new UltraBuildArtifact[0];
    }

    /// <summary>
    /// Strict, fail-closed validation for build manifests and compatibility identities.
    /// </summary>
    public static class UltraBuildManifestValidation
    {
        public const int MaxManifestJsonBytes = 2 * 1024 * 1024;
        public const int MaxArtifacts = 8192;
        public const int Sha256HexChars = 64;
        public const int MaxIdentifierChars = 256;
        public const int MaxRelativePathChars = 1024;
        public const long MaxArtifactBytes = 64L * 1024L * 1024L * 1024L;

        private static readonly string[] ManifestJsonProperties =
        {
            "kind",
            "manifestVersion",
            "protocolVersion",
            "workerBuildId",
            "unityVersion",
            "buildTarget",
            "generatedUtc",
            "fingerprints",
            "artifacts",
            "buildFingerprint",
            "dataFingerprint",
            "checkpointFingerprint",
            "actionFingerprint",
            "objectiveFingerprint",
            "category",
            "relativePath",
            "contentFingerprint",
            "byteLength",
        };

        public static bool TryValidate(
            UltraBuildManifest manifest,
            out string failureCode,
            out string failureDetail)
        {
            failureCode = null;
            failureDetail = null;

            if (manifest == null)
                return Fail("manifest_missing", "build manifest is null", out failureCode, out failureDetail);
            if (!string.Equals(manifest.kind, UltraWorkerProtocol.BuildManifestKind, StringComparison.Ordinal))
                return Fail("manifest_kind", "build manifest kind is invalid", out failureCode, out failureDetail);
            if (manifest.manifestVersion != UltraBuildManifest.CurrentManifestVersion)
                return Fail("manifest_version", "build manifest version is unsupported", out failureCode, out failureDetail);
            if (manifest.protocolVersion != UltraWorkerProtocol.CurrentProtocolVersion)
                return Fail("protocol_version", "build manifest protocol version is unsupported", out failureCode, out failureDetail);
            if (!IsRequiredIdentifier(manifest.workerBuildId, MaxIdentifierChars))
                return Fail("manifest_build_id", "worker build id is missing or invalid", out failureCode, out failureDetail);
            if (!IsRequiredIdentifier(manifest.unityVersion, MaxIdentifierChars))
                return Fail("manifest_unity_version", "Unity version is missing or invalid", out failureCode, out failureDetail);
            if (!IsRequiredIdentifier(manifest.buildTarget, MaxIdentifierChars))
                return Fail("manifest_build_target", "build target is missing or invalid", out failureCode, out failureDetail);
            if (!IsRequiredIdentifier(manifest.generatedUtc, 64))
                return Fail("manifest_generated_utc", "manifest timestamp is missing or invalid", out failureCode, out failureDetail);
            if (!TryValidateFingerprints(manifest.fingerprints, out failureCode, out failureDetail))
                return false;

            if (manifest.artifacts == null)
                return Fail("manifest_artifacts", "artifact list is null", out failureCode, out failureDetail);
            if (manifest.artifacts.Length > MaxArtifacts)
                return Fail("manifest_artifacts", "artifact count exceeds the hard cap", out failureCode, out failureDetail);

            string previousPath = null;
            for (int i = 0; i < manifest.artifacts.Length; i++)
            {
                UltraBuildArtifact artifact = manifest.artifacts[i];
                if (artifact == null)
                    return Fail("manifest_artifact", "artifact entry is null", out failureCode, out failureDetail);
                if (!IsRequiredIdentifier(artifact.category, MaxIdentifierChars))
                    return Fail("manifest_artifact_category", "artifact category is missing or invalid", out failureCode, out failureDetail);
                if (!IsCanonicalRelativePath(artifact.relativePath))
                    return Fail("manifest_artifact_path", "artifact path is not canonical and relative", out failureCode, out failureDetail);
                if (!IsFingerprint(artifact.contentFingerprint))
                    return Fail("manifest_artifact_fingerprint", "artifact fingerprint is missing or invalid", out failureCode, out failureDetail);
                if (artifact.byteLength < 0 || artifact.byteLength > MaxArtifactBytes)
                    return Fail("manifest_artifact_size", "artifact size is outside the hard bounds", out failureCode, out failureDetail);
                if (previousPath != null
                    && string.CompareOrdinal(previousPath, artifact.relativePath) >= 0)
                {
                    return Fail(
                        "manifest_artifact_order",
                        "artifact paths must be unique and sorted with ordinal comparison",
                        out failureCode,
                        out failureDetail);
                }
                previousPath = artifact.relativePath;
            }

            return true;
        }

        public static bool TryValidateFingerprints(
            UltraFingerprintSet fingerprints,
            out string failureCode,
            out string failureDetail)
        {
            failureCode = null;
            failureDetail = null;
            if (fingerprints == null)
                return Fail("fingerprints_missing", "fingerprint set is null", out failureCode, out failureDetail);
            if (!IsFingerprint(fingerprints.buildFingerprint))
                return Fail("build_fingerprint", "build fingerprint is missing or invalid", out failureCode, out failureDetail);
            if (!IsFingerprint(fingerprints.dataFingerprint))
                return Fail("data_fingerprint", "data fingerprint is missing or invalid", out failureCode, out failureDetail);
            if (!IsFingerprint(fingerprints.checkpointFingerprint))
                return Fail("checkpoint_fingerprint", "checkpoint fingerprint is missing or invalid", out failureCode, out failureDetail);
            if (!IsFingerprint(fingerprints.actionFingerprint))
                return Fail("action_fingerprint", "action fingerprint is missing or invalid", out failureCode, out failureDetail);
            if (!IsFingerprint(fingerprints.objectiveFingerprint))
                return Fail("objective_fingerprint", "objective fingerprint is missing or invalid", out failureCode, out failureDetail);
            return true;
        }

        /// <summary>
        /// Requires all five fingerprints to match completely. Prefix, case-insensitive,
        /// and partial matches are deliberately not accepted.
        /// </summary>
        public static bool TryMatchFingerprintsExactly(
            UltraFingerprintSet expected,
            UltraFingerprintSet actual,
            out string failureCode,
            out string failureDetail)
        {
            failureCode = null;
            failureDetail = null;
            if (!TryValidateFingerprints(expected, out failureCode, out failureDetail)) return false;
            if (!TryValidateFingerprints(actual, out failureCode, out failureDetail)) return false;

            if (!string.Equals(expected.buildFingerprint, actual.buildFingerprint, StringComparison.Ordinal))
                return Fail("build_mismatch", "build fingerprint mismatch", out failureCode, out failureDetail);
            if (!string.Equals(expected.dataFingerprint, actual.dataFingerprint, StringComparison.Ordinal))
                return Fail("data_mismatch", "data fingerprint mismatch", out failureCode, out failureDetail);
            if (!string.Equals(expected.checkpointFingerprint, actual.checkpointFingerprint, StringComparison.Ordinal))
                return Fail("checkpoint_mismatch", "checkpoint fingerprint mismatch", out failureCode, out failureDetail);
            if (!string.Equals(expected.actionFingerprint, actual.actionFingerprint, StringComparison.Ordinal))
                return Fail("action_mismatch", "action fingerprint mismatch", out failureCode, out failureDetail);
            if (!string.Equals(expected.objectiveFingerprint, actual.objectiveFingerprint, StringComparison.Ordinal))
                return Fail("objective_mismatch", "objective fingerprint mismatch", out failureCode, out failureDetail);
            return true;
        }

        /// <summary>
        /// Compares the complete compatibility manifest used by a handshake. Artifact
        /// order is canonical, so equality is deterministic across processes.
        /// </summary>
        public static bool TryMatchExactly(
            UltraBuildManifest expected,
            UltraBuildManifest actual,
            out string failureCode,
            out string failureDetail)
        {
            failureCode = null;
            failureDetail = null;
            if (!TryValidate(expected, out failureCode, out failureDetail)) return false;
            if (!TryValidate(actual, out failureCode, out failureDetail)) return false;

            if (!string.Equals(expected.kind, actual.kind, StringComparison.Ordinal)
                || expected.manifestVersion != actual.manifestVersion
                || expected.protocolVersion != actual.protocolVersion
                || !string.Equals(expected.workerBuildId, actual.workerBuildId, StringComparison.Ordinal)
                || !string.Equals(expected.unityVersion, actual.unityVersion, StringComparison.Ordinal)
                || !string.Equals(expected.buildTarget, actual.buildTarget, StringComparison.Ordinal)
                || !string.Equals(expected.generatedUtc, actual.generatedUtc, StringComparison.Ordinal))
            {
                return Fail("manifest_identity_mismatch", "build manifest identity mismatch", out failureCode, out failureDetail);
            }
            if (!TryMatchFingerprintsExactly(expected.fingerprints, actual.fingerprints, out failureCode, out failureDetail))
                return false;
            if (expected.artifacts.Length != actual.artifacts.Length)
                return Fail("manifest_artifact_mismatch", "artifact count mismatch", out failureCode, out failureDetail);

            for (int i = 0; i < expected.artifacts.Length; i++)
            {
                UltraBuildArtifact left = expected.artifacts[i];
                UltraBuildArtifact right = actual.artifacts[i];
                if (!string.Equals(left.category, right.category, StringComparison.Ordinal)
                    || !string.Equals(left.relativePath, right.relativePath, StringComparison.Ordinal)
                    || !string.Equals(left.contentFingerprint, right.contentFingerprint, StringComparison.Ordinal)
                    || left.byteLength != right.byteLength)
                {
                    return Fail("manifest_artifact_mismatch", "artifact entry mismatch at index " + i, out failureCode, out failureDetail);
                }
            }
            return true;
        }

        public static bool TryToJson(
            UltraBuildManifest manifest,
            out string json,
            out string failureCode,
            out string failureDetail)
        {
            json = null;
            if (!TryValidate(manifest, out failureCode, out failureDetail)) return false;
            try
            {
                json = JsonUtility.ToJson(manifest, false);
            }
            catch (Exception ex)
            {
                return Fail("manifest_serialize", ex.GetType().Name, out failureCode, out failureDetail);
            }
            if (!UltraWorkerProtocol.IsUtf8SizeWithin(json, MaxManifestJsonBytes))
            {
                json = null;
                return Fail("manifest_size", "serialized manifest exceeds the hard cap", out failureCode, out failureDetail);
            }
            return true;
        }

        public static bool TryFromJson(
            string json,
            out UltraBuildManifest manifest,
            out string failureCode,
            out string failureDetail)
        {
            manifest = null;
            failureCode = null;
            failureDetail = null;
            if (!UltraWorkerProtocol.IsUtf8SizeWithin(json, MaxManifestJsonBytes))
                return Fail("manifest_size", "manifest JSON is empty or exceeds the hard cap", out failureCode, out failureDetail);
            if (!UltraWorkerProtocol.TryEnsureKnownJsonProperties(json, ManifestJsonProperties, out string propertyError))
                return Fail("manifest_schema", propertyError, out failureCode, out failureDetail);
            try
            {
                manifest = JsonUtility.FromJson<UltraBuildManifest>(json);
            }
            catch (Exception ex)
            {
                return Fail("manifest_parse", ex.GetType().Name, out failureCode, out failureDetail);
            }
            if (!TryValidate(manifest, out failureCode, out failureDetail))
            {
                manifest = null;
                return false;
            }
            return true;
        }

        internal static bool IsRequiredIdentifier(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maxChars) return false;
            if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1])) return false;
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i])) return false;
            return true;
        }

        private static bool IsFingerprint(string value)
        {
            // One canonical representation prevents case-only identities from being treated
            // as different builds by ordinal comparison.
            if (value == null || value.Length != Sha256HexChars) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'))) return false;
            }
            return true;
        }

        private static bool IsCanonicalRelativePath(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxRelativePathChars) return false;
            if (value[0] == '/' || value[value.Length - 1] == '/') return false;
            if (value.IndexOf('\\') >= 0 || value.IndexOf(':') >= 0 || value.IndexOf("//", StringComparison.Ordinal) >= 0)
                return false;

            string[] segments = value.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length == 0
                    || string.Equals(segments[i], ".", StringComparison.Ordinal)
                    || string.Equals(segments[i], "..", StringComparison.Ordinal))
                    return false;
                for (int c = 0; c < segments[i].Length; c++)
                    if (char.IsControl(segments[i][c])) return false;
            }
            return true;
        }

        private static bool Fail(
            string code,
            string detail,
            out string failureCode,
            out string failureDetail)
        {
            failureCode = code;
            failureDetail = detail;
            return false;
        }
    }
}

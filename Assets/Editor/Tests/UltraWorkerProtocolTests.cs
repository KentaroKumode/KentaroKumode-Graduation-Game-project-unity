#if UNITY_EDITOR
using System;
using System.Reflection;
using AutoTest.Ultra;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    [TestFixture]
    public sealed class UltraWorkerProtocolTests
    {
        [Test]
        public void Fingerprints_RequireCanonicalSha256AndMatchAllFiveExactly()
        {
            UltraBuildManifest manifest = MakeManifest();
            Assert.That(
                UltraBuildManifestValidation.TryValidate(manifest, out string code, out string detail),
                Is.True,
                code + ": " + detail);

            UltraFingerprintSet actual = manifest.fingerprints.Clone();
            actual.objectiveFingerprint = Hash('1');
            Assert.That(
                UltraBuildManifestValidation.TryMatchFingerprintsExactly(
                    manifest.fingerprints,
                    actual,
                    out code,
                    out detail),
                Is.False);
            Assert.That(code, Is.EqualTo("objective_mismatch"));

            UltraBuildManifest shortHash = MakeManifest();
            shortHash.fingerprints.buildFingerprint = new string('a', 63);
            Assert.That(UltraBuildManifestValidation.TryValidate(shortHash, out _, out _), Is.False);

            UltraBuildManifest uppercaseHash = MakeManifest();
            uppercaseHash.fingerprints.dataFingerprint = new string('A', 64);
            Assert.That(UltraBuildManifestValidation.TryValidate(uppercaseHash, out _, out _), Is.False);

            UltraBuildManifest nonHexHash = MakeManifest();
            nonHexHash.fingerprints.checkpointFingerprint = new string('g', 64);
            Assert.That(UltraBuildManifestValidation.TryValidate(nonHexHash, out _, out _), Is.False);
        }

        [Test]
        public void EpisodeRequest_RoundTripsWithOnlySyntheticStartSeed()
        {
            UltraBuildManifest manifest = MakeManifest();
            UltraWorkerRequest request = MakeEpisodeRequest(manifest);

            Assert.That(
                UltraWorkerProtocol.TryRequestToJson(request, manifest, out string json, out UltraWorkerFailure failure),
                Is.True,
                FailureText(failure));
            StringAssert.Contains("\"scenarioStartSeedHex\":", json);
            StringAssert.DoesNotContain("\"scenarioSeed\":", json);
            StringAssert.DoesNotContain("\"masterSeed\":", json);
            StringAssert.DoesNotContain("\"rngState\":", json);

            Assert.That(
                UltraWorkerProtocol.TryRequestFromJson(json, manifest, out UltraWorkerRequest roundTrip, out failure),
                Is.True,
                FailureText(failure));
            Assert.That(roundTrip.episode.scenarioStartSeedHex, Is.EqualTo("0123456789ABCDEF"));

            UltraEpisodeRequest oracleRequest;
            Assert.That(
                UltraWorkerProtocol.TryCreateOracleEpisodeRequest(
                    roundTrip,
                    manifest,
                    out oracleRequest,
                    out failure),
                Is.True,
                FailureText(failure));
            Assert.That(oracleRequest.scenarioStartSeedHex, Is.EqualTo(roundTrip.episode.scenarioStartSeedHex));
        }

        [Test]
        public void Request_RejectsUnknownRngFieldAndHardLimitViolations()
        {
            UltraBuildManifest manifest = MakeManifest();
            UltraWorkerRequest request = MakeEpisodeRequest(manifest);
            Assert.That(
                UltraWorkerProtocol.TryRequestToJson(request, manifest, out string json, out UltraWorkerFailure failure),
                Is.True,
                FailureText(failure));

            string injected = json.Insert(json.Length - 1, ",\"liveSeed\":\"not-allowed\"");
            Assert.That(
                UltraWorkerProtocol.TryRequestFromJson(injected, manifest, out _, out failure),
                Is.False);
            Assert.That(failure.code, Is.EqualTo("request_schema"));

            request.episode.maxProductionSteps = UltraWorkerProtocol.MaxProductionSteps + 1;
            Assert.That(UltraWorkerProtocol.TryValidateRequest(request, manifest, out failure), Is.False);
            Assert.That(failure.code, Is.EqualTo("step_limit"));

            request = MakeEpisodeRequest(manifest);
            request.episode.actionPayload = new string('x', UltraWorkerProtocol.MaxActionPayloadChars + 1);
            Assert.That(UltraWorkerProtocol.TryValidateRequest(request, manifest, out failure), Is.False);
            Assert.That(failure.code, Is.EqualTo("action_payload"));
        }

        [Test]
        public void CheckpointBase64_RequiresCanonicalPaddingBits()
        {
            UltraBuildManifest manifest = MakeManifest();
            UltraWorkerRequest request = MakeEpisodeRequest(manifest);
            request.episode.publicCheckpointBase64 = "AA==";
            Assert.That(UltraWorkerProtocol.TryValidateRequest(request, manifest, out _), Is.True);

            // Both strings decode on permissive decoders, but AB== has non-zero pad bits.
            request.episode.publicCheckpointBase64 = "AB==";
            Assert.That(
                UltraWorkerProtocol.TryValidateRequest(request, manifest, out UltraWorkerFailure failure),
                Is.False);
            Assert.That(failure.code, Is.EqualTo("checkpoint_payload"));
        }

        [Test]
        public void Rejection_HasDedicatedSafeJsonRoundTrip()
        {
            UltraBuildManifest manifest = MakeManifest();
            UltraWorkerRequest request = MakeEpisodeRequest(manifest);
            UltraWorkerResponse rejection = UltraWorkerProtocol.CreateRejectedResponse(
                request.requestId,
                UltraWorkerProtocol.EpisodeResponseKind,
                manifest,
                "worker_timeout",
                "episode timed out");

            Assert.That(
                UltraWorkerProtocol.TryRejectedResponseToJson(
                    rejection,
                    request.requestId,
                    UltraWorkerProtocol.EpisodeResponseKind,
                    manifest,
                    out string json,
                    out UltraWorkerFailure failure),
                Is.True,
                FailureText(failure));
            Assert.That(
                UltraWorkerProtocol.TryRejectedResponseFromJson(
                    json,
                    request.requestId,
                    UltraWorkerProtocol.EpisodeResponseKind,
                    manifest,
                    out UltraWorkerResponse parsed,
                    out failure),
                Is.True,
                FailureText(failure));
            Assert.That(parsed.accepted, Is.False);
            Assert.That(parsed.failure.code, Is.EqualTo("worker_timeout"));

            Assert.That(
                UltraWorkerProtocol.TryResponseFromJson(json, request, manifest, out _, out _),
                Is.False,
                "a rejection must never be accepted by the gameplay-result codec");
            Assert.That(
                UltraWorkerProtocol.TryRejectedResponseFromJson(
                    json,
                    request.requestId,
                    UltraWorkerProtocol.HandshakeResponseKind,
                    manifest,
                    out _,
                    out _),
                Is.False,
                "response kind must match exactly");
        }

        [Test]
        public void AcceptedResponse_RejectsAnyFingerprintMismatch()
        {
            UltraBuildManifest manifest = MakeManifest();
            UltraWorkerRequest request = MakeEpisodeRequest(manifest);
            var result = new UltraEpisodeResult
            {
                success = true,
                reachedTerminal = true,
                primaryReward = 1f,
                finalPublicStateHash = Hash('2'),
                productionSteps = 10,
            };
            Assert.That(
                UltraWorkerProtocol.TryCreateEpisodeResponse(
                    request,
                    manifest,
                    result,
                    out UltraWorkerResponse response,
                    out UltraWorkerFailure failure),
                Is.True,
                FailureText(failure));

            response.workerFingerprints.actionFingerprint = Hash('3');
            Assert.That(
                UltraWorkerProtocol.TryValidateResponse(response, request, manifest, out failure),
                Is.False);
            Assert.That(failure.code, Is.EqualTo("action_mismatch"));
        }

        [Test]
        public void ManifestJson_RejectsUnknownFields()
        {
            UltraBuildManifest manifest = MakeManifest();
            Assert.That(
                UltraBuildManifestValidation.TryToJson(
                    manifest,
                    out string json,
                    out string code,
                    out string detail),
                Is.True,
                code + ": " + detail);

            string injected = json.Insert(json.Length - 1, ",\"rngState\":\"forbidden\"");
            Assert.That(
                UltraBuildManifestValidation.TryFromJson(
                    injected,
                    out _,
                    out code,
                    out detail),
                Is.False);
            Assert.That(code, Is.EqualTo("manifest_schema"));
        }

        [Test]
        public void WireDto_ContainsNoLiveRngStateField()
        {
            Type[] transportTypes =
            {
                typeof(UltraWorkerRequest),
                typeof(UltraWorkerResponse),
                typeof(UltraFingerprintSet),
                typeof(UltraBuildManifest),
            };
            for (int t = 0; t < transportTypes.Length; t++)
            {
                FieldInfo[] fields = transportTypes[t].GetFields(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < fields.Length; i++)
                {
                    string name = fields[i].Name.ToLowerInvariant();
                    Assert.That(name.Contains("seed"), Is.False, transportTypes[t].Name + "." + fields[i].Name);
                    Assert.That(name.Contains("rng"), Is.False, transportTypes[t].Name + "." + fields[i].Name);
                }
            }

            FieldInfo[] episodeFields = typeof(UltraWorkerEpisodePayload)
                .GetFields(BindingFlags.Public | BindingFlags.Instance);
            int seedFields = 0;
            for (int i = 0; i < episodeFields.Length; i++)
            {
                string lower = episodeFields[i].Name.ToLowerInvariant();
                Assert.That(lower.Contains("rng"), Is.False, episodeFields[i].Name);
                if (!lower.Contains("seed")) continue;
                seedFields++;
                Assert.That(episodeFields[i].Name, Is.EqualTo("scenarioStartSeedHex"));
            }
            Assert.That(seedFields, Is.EqualTo(1));
        }

        private static UltraBuildManifest MakeManifest()
        {
            return new UltraBuildManifest
            {
                workerBuildId = "ultra-worker-test-build",
                unityVersion = "2022.3.22f1",
                buildTarget = "StandaloneWindows64",
                generatedUtc = "2026-08-17T00:00:00Z",
                fingerprints = new UltraFingerprintSet
                {
                    buildFingerprint = Hash('a'),
                    dataFingerprint = Hash('b'),
                    checkpointFingerprint = Hash('c'),
                    actionFingerprint = Hash('d'),
                    objectiveFingerprint = Hash('e'),
                },
                artifacts = new[]
                {
                    new UltraBuildArtifact
                    {
                        category = "assembly",
                        relativePath = "Managed/Assembly-CSharp.dll",
                        contentFingerprint = Hash('f'),
                        byteLength = 1234,
                    },
                },
            };
        }

        private static UltraWorkerRequest MakeEpisodeRequest(UltraBuildManifest manifest)
        {
            var legacy = new UltraEpisodeRequest
            {
                jobId = "test:episode:1",
                expectedBuildFingerprint = manifest.fingerprints.buildFingerprint,
                checkpointSchema = "public-checkpoint-v1",
                publicCheckpointBase64 = "AA==",
                actionId = "choose-class:warrior",
                actionPayload = "{}",
                scenarioStartSeedHex = "0123456789ABCDEF",
                maxProductionSteps = 100,
            };
            Assert.That(
                UltraWorkerProtocol.TryCreateEpisodeRequest(
                    legacy,
                    "terminal-clear-v1",
                    manifest,
                    out UltraWorkerRequest request,
                    out UltraWorkerFailure failure),
                Is.True,
                FailureText(failure));
            return request;
        }

        private static string Hash(char ch)
        {
            return new string(ch, UltraBuildManifestValidation.Sha256HexChars);
        }

        private static string FailureText(UltraWorkerFailure failure)
        {
            return failure == null ? string.Empty : failure.code + ": " + failure.detail;
        }
    }
}
#endif

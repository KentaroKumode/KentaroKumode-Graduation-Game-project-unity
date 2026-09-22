using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace AutoTest.Ultra
{
    /// <summary>
    /// JSON transport envelope sent from an Ultra controller to a production worker.
    /// The only randomness input in this schema is episode.scenarioStartSeedHex.
    /// Live-run seeds, next draws, GameRng counters, and RNG snapshots are intentionally
    /// absent and must never be added to this DTO.
    /// </summary>
    [Serializable]
    public sealed class UltraWorkerRequest
    {
        public int protocolVersion = UltraWorkerProtocol.CurrentProtocolVersion;
        public string kind;
        public string requestId;
        public UltraFingerprintSet expectedFingerprints;
        public UltraWorkerEpisodePayload episode;
        /// <summary>Payload for the control kinds (legal actions / cancel / heartbeat).
        /// Null for handshake and episode requests.</summary>
        public UltraWorkerControlPayload control;
        /// <summary>SHA-256 over the mechanical content (see
        /// <see cref="UltraWorkerProtocol.ComputeRequestChecksum"/>). Empty is rejected:
        /// a truncated pipe write otherwise arrives as a shorter but still well-formed
        /// message, and length caps alone do not notice.</summary>
        public string payloadChecksum;
    }

    /// <summary>Control-message payload. Deliberately tiny ── these messages must stay
    /// answerable while the worker is busy, so they carry no checkpoint-sized data unless the
    /// message is specifically about a checkpoint.</summary>
    [Serializable]
    public sealed class UltraWorkerControlPayload
    {
        public string checkpointSchema;
        public string publicCheckpointBase64;
        /// <summary>Epoch the caller believes the run is at. Echoed back so a late reply can
        /// be discarded rather than acted on.</summary>
        public int epoch;
        public int decisionPoint;
    }

    [Serializable]
    public sealed class UltraWorkerEpisodePayload
    {
        public string checkpointSchema;
        public string publicCheckpointBase64;
        public string actionId;
        public string actionPayload;
        public string objectiveId;

        // Synthetic production-episode start seed. It is not copied from the live run.
        public string scenarioStartSeedHex;
        public int maxProductionSteps;
    }

    /// <summary>
    /// JSON transport envelope returned by a production worker.
    /// A rejected or malformed response is never a gameplay loss; callers must fall back.
    /// </summary>
    [Serializable]
    public sealed class UltraWorkerResponse
    {
        public int protocolVersion = UltraWorkerProtocol.CurrentProtocolVersion;
        public string kind;
        public string requestId;
        public bool accepted;
        public UltraFingerprintSet workerFingerprints;
        public UltraBuildManifest workerManifest;
        public UltraEpisodeResult episodeResult;
        public UltraWorkerControlResult controlResult;
        public UltraWorkerFailure failure;
        /// <summary>SHA-256 over the mechanical content, same reasoning as on the request.</summary>
        public string payloadChecksum;
    }

    /// <summary>Answer to a control message.</summary>
    [Serializable]
    public sealed class UltraWorkerControlResult
    {
        /// <summary>Legal action ids the worker derived from the supplied checkpoint.
        ///
        /// <para><b>Used to compare, not to authorise.</b> The parent already issued its own
        /// set; a disagreement means the two sides are running different rules, which is the
        /// class of fault that made the 2026-08-17 portfolio meaningless.</para></summary>
        public string[] legalActionIds = new string[0];
        /// <summary>Hash of the checkpoint the worker actually restored. Compared against the
        /// one the parent sent.</summary>
        public string checkpointMechanicalHash = "";
        public int epoch;
        public bool busy;
        public bool cancelled;
        public long uptimeMs;
        public long lastProgressUnixMs;
    }

    [Serializable]
    public sealed class UltraWorkerFailure
    {
        public string code;
        public string detail;
    }

    /// <summary>
    /// Versioned wire protocol and strict validators for the out-of-process production oracle.
    /// This layer adapts to UltraEpisodeRequest/UltraEpisodeResult rather than replacing the
    /// existing IUltraProductionOracle domain boundary.
    /// </summary>
    public static class UltraWorkerProtocol
    {
        public const int CurrentProtocolVersion = UltraEpisodeRequest.CurrentProtocolVersion;

        public const string BuildManifestKind = "ultra.build-manifest";
        public const string HandshakeRequestKind = "ultra.handshake.request";
        public const string HandshakeResponseKind = "ultra.handshake.response";
        public const string EpisodeRequestKind = "ultra.episode.request";
        public const string EpisodeResponseKind = "ultra.episode.response";

        // ---- control kinds (handoff §10 Phase C) ----
        //   Separate kinds rather than flags on the episode message: heartbeat and cancel have
        //   to be answerable **while an episode is running**, so they cannot share a code path
        //   that assumes the worker is idle.
        public const string LegalActionsRequestKind = "ultra.legal-actions.request";
        public const string LegalActionsResponseKind = "ultra.legal-actions.response";
        public const string HeartbeatRequestKind = "ultra.heartbeat.request";
        public const string HeartbeatResponseKind = "ultra.heartbeat.response";
        public const string CancelRequestKind = "ultra.cancel.request";
        public const string CancelResponseKind = "ultra.cancel.response";

        public const int MaxLegalActionCount = 4096;
        public const int MaxChecksumChars = 64;

        public const int MaxRequestJsonBytes = 40 * 1024 * 1024;
        public const int MaxResponseJsonBytes = 4 * 1024 * 1024;
        public const int MaxRequestIdChars = 256;
        public const int MaxSchemaChars = 256;
        public const int MaxActionIdChars = 256;
        public const int MaxObjectiveIdChars = 256;
        public const int MaxCheckpointBase64Chars = 32 * 1024 * 1024;
        public const int MaxCheckpointDecodedBytes = 24 * 1024 * 1024;
        public const int MaxActionPayloadChars = 1024 * 1024;
        public const int MaxFailureCodeChars = 128;
        public const int MaxFailureDetailChars = 4096;
        public const int MaxFinalStateHashChars = 256;
        public const int MaxProductionSteps = 1_000_000;

        private static readonly string[] RequestJsonProperties =
        {
            "protocolVersion",
            "kind",
            "requestId",
            "expectedFingerprints",
            "episode",
            "buildFingerprint",
            "dataFingerprint",
            "checkpointFingerprint",
            "actionFingerprint",
            "objectiveFingerprint",
            "checkpointSchema",
            "publicCheckpointBase64",
            "actionId",
            "actionPayload",
            "objectiveId",
            "scenarioStartSeedHex",
            "maxProductionSteps",
            "control",
            "epoch",
            "decisionPoint",
            "payloadChecksum",
        };

        private static readonly string[] ResponseJsonProperties =
        {
            "protocolVersion",
            "kind",
            "requestId",
            "accepted",
            "workerFingerprints",
            "workerManifest",
            "episodeResult",
            "failure",
            "buildFingerprint",
            "dataFingerprint",
            "checkpointFingerprint",
            "actionFingerprint",
            "objectiveFingerprint",
            "success",
            "reachedTerminal",
            "primaryReward",
            "finalPublicStateHash",
            "productionSteps",
            "failureCode",
            "failureDetail",
            "code",
            "detail",
            "manifestVersion",
            "workerBuildId",
            "unityVersion",
            "buildTarget",
            "generatedUtc",
            "fingerprints",
            "artifacts",
            "category",
            "relativePath",
            "contentFingerprint",
            "byteLength",
            "controlResult",
            "legalActionIds",
            "checkpointMechanicalHash",
            "epoch",
            "busy",
            "cancelled",
            "uptimeMs",
            "lastProgressUnixMs",
            "payloadChecksum",
        };

        public static bool TryCreateHandshakeRequest(
            string requestId,
            UltraBuildManifest expectedManifest,
            out UltraWorkerRequest request,
            out UltraWorkerFailure failure)
        {
            request = null;
            if (!UltraBuildManifestValidation.TryValidate(
                    expectedManifest,
                    out string code,
                    out string detail))
            {
                failure = NewFailure(code, detail);
                return false;
            }

            request = new UltraWorkerRequest
            {
                kind = HandshakeRequestKind,
                requestId = requestId,
                expectedFingerprints = expectedManifest.fingerprints.Clone(),
                episode = null,
            };
            return TryValidateRequest(request, expectedManifest, out failure);
        }

        /// <summary>
        /// Adapts the existing production-oracle request to the worker wire contract.
        /// legacyRequest.scenarioStartSeedHex is solely the synthetic episode start seed.
        /// </summary>
        public static bool TryCreateEpisodeRequest(
            UltraEpisodeRequest legacyRequest,
            string objectiveId,
            UltraBuildManifest expectedManifest,
            out UltraWorkerRequest request,
            out UltraWorkerFailure failure)
        {
            request = null;
            if (legacyRequest == null)
            {
                failure = NewFailure("episode_missing", "legacy episode request is null");
                return false;
            }
            if (!UltraBuildManifestValidation.TryValidate(
                    expectedManifest,
                    out string manifestCode,
                    out string manifestDetail))
            {
                failure = NewFailure(manifestCode, manifestDetail);
                return false;
            }
            if (legacyRequest.protocolVersion != CurrentProtocolVersion)
            {
                failure = NewFailure("protocol_version", "legacy episode protocol version is unsupported");
                return false;
            }
            if (!string.Equals(
                    legacyRequest.expectedBuildFingerprint,
                    expectedManifest.fingerprints.buildFingerprint,
                    StringComparison.Ordinal))
            {
                failure = NewFailure("build_mismatch", "legacy request and expected manifest build fingerprints differ");
                return false;
            }

            request = new UltraWorkerRequest
            {
                kind = EpisodeRequestKind,
                requestId = legacyRequest.jobId,
                expectedFingerprints = expectedManifest.fingerprints.Clone(),
                episode = new UltraWorkerEpisodePayload
                {
                    checkpointSchema = legacyRequest.checkpointSchema,
                    publicCheckpointBase64 = legacyRequest.publicCheckpointBase64,
                    actionId = legacyRequest.actionId,
                    actionPayload = legacyRequest.actionPayload,
                    objectiveId = objectiveId,
                    scenarioStartSeedHex = legacyRequest.scenarioStartSeedHex,
                    maxProductionSteps = legacyRequest.maxProductionSteps,
                },
            };
            return TryValidateRequest(request, expectedManifest, out failure);
        }

        /// <summary>
        /// Worker-side adapter. It emits the already-established production oracle DTO and
        /// never introduces a live seed or serialized RNG state.
        /// </summary>
        public static bool TryCreateOracleEpisodeRequest(
            UltraWorkerRequest request,
            UltraBuildManifest workerManifest,
            out UltraEpisodeRequest episode,
            out UltraWorkerFailure failure)
        {
            episode = null;
            if (!TryValidateRequest(request, workerManifest, out failure)) return false;
            if (!string.Equals(request.kind, EpisodeRequestKind, StringComparison.Ordinal))
            {
                failure = NewFailure("request_kind", "request is not an episode request");
                return false;
            }

            episode = new UltraEpisodeRequest
            {
                protocolVersion = CurrentProtocolVersion,
                jobId = request.requestId,
                expectedBuildFingerprint = request.expectedFingerprints.buildFingerprint,
                checkpointSchema = request.episode.checkpointSchema,
                publicCheckpointBase64 = request.episode.publicCheckpointBase64,
                actionId = request.episode.actionId,
                actionPayload = request.episode.actionPayload,
                scenarioStartSeedHex = request.episode.scenarioStartSeedHex,
                maxProductionSteps = request.episode.maxProductionSteps,
            };
            return true;
        }

        public static bool TryValidateRequest(
            UltraWorkerRequest request,
            UltraBuildManifest localManifest,
            out UltraWorkerFailure failure)
        {
            failure = null;
            if (!UltraBuildManifestValidation.TryValidate(localManifest, out string code, out string detail))
            {
                failure = NewFailure(code, detail);
                return false;
            }
            if (request == null)
            {
                failure = NewFailure("request_missing", "worker request is null");
                return false;
            }
            if (request.protocolVersion != CurrentProtocolVersion)
            {
                failure = NewFailure("protocol_version", "request protocol version is unsupported");
                return false;
            }
            if (!IsToken(request.requestId, MaxRequestIdChars))
            {
                failure = NewFailure("request_id", "request id is missing or invalid");
                return false;
            }
            if (!UltraBuildManifestValidation.TryMatchFingerprintsExactly(
                    localManifest.fingerprints,
                    request.expectedFingerprints,
                    out code,
                    out detail))
            {
                failure = NewFailure(code, detail);
                return false;
            }

            if (string.Equals(request.kind, HandshakeRequestKind, StringComparison.Ordinal))
            {
                if (request.episode != null)
                {
                    failure = NewFailure("request_shape", "handshake request contains an episode payload");
                    return false;
                }
                if (!IsControlPayloadEmpty(request.control))
                {
                    failure = NewFailure("request_shape", "handshake request contains a control payload");
                    return false;
                }
                return true;
            }
            if (IsControlKind(request.kind)) return TryValidateControlRequest(request, out failure);
            if (!string.Equals(request.kind, EpisodeRequestKind, StringComparison.Ordinal))
            {
                failure = NewFailure("request_kind", "request kind is unsupported");
                return false;
            }
            if (!IsControlPayloadEmpty(request.control))
            {
                failure = NewFailure("request_shape", "episode request contains a control payload");
                return false;
            }
            return TryValidateEpisodePayload(request.episode, out failure);
        }

        public static bool TryCreateHandshakeResponse(
            UltraWorkerRequest request,
            UltraBuildManifest workerManifest,
            out UltraWorkerResponse response,
            out UltraWorkerFailure failure)
        {
            response = null;
            if (!TryValidateRequest(request, workerManifest, out failure)) return false;
            if (!string.Equals(request.kind, HandshakeRequestKind, StringComparison.Ordinal))
            {
                failure = NewFailure("request_kind", "request is not a handshake request");
                return false;
            }

            response = new UltraWorkerResponse
            {
                kind = HandshakeResponseKind,
                requestId = request.requestId,
                accepted = true,
                workerFingerprints = workerManifest.fingerprints.Clone(),
                workerManifest = workerManifest,
                episodeResult = null,
                failure = null,
            };
            return true;
        }

        public static bool TryCreateEpisodeResponse(
            UltraWorkerRequest request,
            UltraBuildManifest workerManifest,
            UltraEpisodeResult result,
            out UltraWorkerResponse response,
            out UltraWorkerFailure failure)
        {
            response = null;
            if (!TryValidateRequest(request, workerManifest, out failure)) return false;
            if (!string.Equals(request.kind, EpisodeRequestKind, StringComparison.Ordinal))
            {
                failure = NewFailure("request_kind", "request is not an episode request");
                return false;
            }
            if (!TryValidateEpisodeResult(result, request.episode.maxProductionSteps, out failure))
                return false;

            response = new UltraWorkerResponse
            {
                kind = EpisodeResponseKind,
                requestId = request.requestId,
                accepted = true,
                workerFingerprints = workerManifest.fingerprints.Clone(),
                workerManifest = null,
                episodeResult = CloneResult(result),
                failure = null,
            };
            return true;
        }

        /// <summary>
        /// Validates a response against both the originating request and the controller's
        /// immutable manifest. Any rejection or mismatch returns false (fail closed).
        /// </summary>
        public static bool TryValidateResponse(
            UltraWorkerResponse response,
            UltraWorkerRequest request,
            UltraBuildManifest expectedManifest,
            out UltraWorkerFailure failure)
        {
            failure = null;
            if (!TryValidateRequest(request, expectedManifest, out failure)) return false;
            if (response == null)
            {
                failure = NewFailure("response_missing", "worker response is null");
                return false;
            }
            if (response.protocolVersion != CurrentProtocolVersion)
            {
                failure = NewFailure("protocol_version", "response protocol version is unsupported");
                return false;
            }
            if (!string.Equals(response.requestId, request.requestId, StringComparison.Ordinal))
            {
                failure = NewFailure("request_id_mismatch", "response request id does not match");
                return false;
            }
            if (!UltraBuildManifestValidation.TryMatchFingerprintsExactly(
                    expectedManifest.fingerprints,
                    response.workerFingerprints,
                    out string code,
                    out string detail))
            {
                failure = NewFailure(code, detail);
                return false;
            }
            if (!UltraBuildManifestValidation.TryMatchFingerprintsExactly(
                    request.expectedFingerprints,
                    response.workerFingerprints,
                    out code,
                    out detail))
            {
                failure = NewFailure(code, detail);
                return false;
            }

            if (!response.accepted)
            {
                if (!IsValidFailure(response.failure))
                    failure = NewFailure("worker_rejected", "worker rejected the request without a valid failure");
                else
                    failure = NewFailure(response.failure.code, response.failure.detail);
                return false;
            }
            if (response.failure != null)
            {
                failure = NewFailure("response_shape", "accepted response contains a failure payload");
                return false;
            }

            if (string.Equals(request.kind, HandshakeRequestKind, StringComparison.Ordinal))
            {
                if (!string.Equals(response.kind, HandshakeResponseKind, StringComparison.Ordinal))
                {
                    failure = NewFailure("response_kind", "handshake response kind mismatch");
                    return false;
                }
                if (response.episodeResult != null)
                {
                    failure = NewFailure("response_shape", "handshake response contains an episode result");
                    return false;
                }
                if (!UltraBuildManifestValidation.TryMatchExactly(
                        expectedManifest,
                        response.workerManifest,
                        out code,
                        out detail))
                {
                    failure = NewFailure(code, detail);
                    return false;
                }
                return true;
            }

            if (!string.Equals(response.kind, EpisodeResponseKind, StringComparison.Ordinal))
            {
                failure = NewFailure("response_kind", "episode response kind mismatch");
                return false;
            }
            if (response.workerManifest != null)
            {
                failure = NewFailure("response_shape", "episode response unexpectedly contains a manifest");
                return false;
            }
            return TryValidateEpisodeResult(response.episodeResult, request.episode.maxProductionSteps, out failure);
        }

        public static bool TryReadOracleResult(
            UltraWorkerResponse response,
            UltraWorkerRequest request,
            UltraBuildManifest expectedManifest,
            out UltraEpisodeResult result,
            out UltraWorkerFailure failure)
        {
            result = null;
            if (!TryValidateResponse(response, request, expectedManifest, out failure)) return false;
            if (!string.Equals(request.kind, EpisodeRequestKind, StringComparison.Ordinal))
            {
                failure = NewFailure("request_kind", "request is not an episode request");
                return false;
            }
            result = CloneResult(response.episodeResult);
            return true;
        }

        public static UltraWorkerResponse CreateRejectedResponse(
            string requestId,
            string responseKind,
            UltraBuildManifest workerManifest,
            string failureCode,
            string failureDetail)
        {
            UltraFingerprintSet fingerprints = workerManifest != null && workerManifest.fingerprints != null
                ? workerManifest.fingerprints.Clone()
                : null;
            return new UltraWorkerResponse
            {
                kind = responseKind,
                requestId = requestId,
                accepted = false,
                workerFingerprints = fingerprints,
                workerManifest = null,
                episodeResult = null,
                failure = NewFailure(failureCode, Truncate(failureDetail, MaxFailureDetailChars)),
            };
        }

        public static bool TryRequestToJson(
            UltraWorkerRequest request,
            UltraBuildManifest expectedManifest,
            out string json,
            out UltraWorkerFailure failure)
        {
            json = null;
            if (!TryValidateRequest(request, expectedManifest, out failure)) return false;
            try
            {
                json = JsonUtility.ToJson(request, false);
            }
            catch (Exception ex)
            {
                failure = NewFailure("request_serialize", ex.GetType().Name);
                return false;
            }
            if (!IsUtf8SizeWithin(json, MaxRequestJsonBytes))
            {
                json = null;
                failure = NewFailure("request_size", "serialized request exceeds the hard cap");
                return false;
            }
            return true;
        }

        public static bool TryRequestFromJson(
            string json,
            UltraBuildManifest localManifest,
            out UltraWorkerRequest request,
            out UltraWorkerFailure failure)
        {
            request = null;
            failure = null;
            if (!IsUtf8SizeWithin(json, MaxRequestJsonBytes))
            {
                failure = NewFailure("request_size", "request JSON is empty or exceeds the hard cap");
                return false;
            }
            if (!TryEnsureKnownJsonProperties(json, RequestJsonProperties, out string propertyError))
            {
                failure = NewFailure("request_schema", propertyError);
                return false;
            }
            try
            {
                request = JsonUtility.FromJson<UltraWorkerRequest>(json);
            }
            catch (Exception ex)
            {
                failure = NewFailure("request_parse", ex.GetType().Name);
                return false;
            }
            if (!TryValidateRequest(request, localManifest, out failure))
            {
                request = null;
                return false;
            }
            return true;
        }

        public static bool TryResponseToJson(
            UltraWorkerResponse response,
            UltraWorkerRequest request,
            UltraBuildManifest expectedManifest,
            out string json,
            out UltraWorkerFailure failure)
        {
            json = null;
            if (!TryValidateResponse(response, request, expectedManifest, out failure)) return false;
            try
            {
                json = JsonUtility.ToJson(response, false);
            }
            catch (Exception ex)
            {
                failure = NewFailure("response_serialize", ex.GetType().Name);
                return false;
            }
            if (!IsUtf8SizeWithin(json, MaxResponseJsonBytes))
            {
                json = null;
                failure = NewFailure("response_size", "serialized response exceeds the hard cap");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Serializes a well-formed rejection without treating it as an accepted gameplay
        /// result. This path is intentionally separate from TryResponseToJson.
        /// </summary>
        public static bool TryRejectedResponseToJson(
            UltraWorkerResponse response,
            string expectedRequestId,
            string expectedResponseKind,
            UltraBuildManifest workerManifest,
            out string json,
            out UltraWorkerFailure failure)
        {
            json = null;
            if (!TryValidateRejectedResponse(
                    response,
                    expectedRequestId,
                    expectedResponseKind,
                    workerManifest,
                    out failure))
                return false;
            try
            {
                json = JsonUtility.ToJson(response, false);
            }
            catch (Exception ex)
            {
                failure = NewFailure("response_serialize", ex.GetType().Name);
                return false;
            }
            if (!IsUtf8SizeWithin(json, MaxResponseJsonBytes))
            {
                json = null;
                failure = NewFailure("response_size", "serialized rejection exceeds the hard cap");
                return false;
            }
            return true;
        }

        public static bool TryResponseFromJson(
            string json,
            UltraWorkerRequest request,
            UltraBuildManifest expectedManifest,
            out UltraWorkerResponse response,
            out UltraWorkerFailure failure)
        {
            response = null;
            failure = null;
            if (!IsUtf8SizeWithin(json, MaxResponseJsonBytes))
            {
                failure = NewFailure("response_size", "response JSON is empty or exceeds the hard cap");
                return false;
            }
            if (!TryEnsureKnownJsonProperties(json, ResponseJsonProperties, out string propertyError))
            {
                failure = NewFailure("response_schema", propertyError);
                return false;
            }
            try
            {
                response = JsonUtility.FromJson<UltraWorkerResponse>(json);
            }
            catch (Exception ex)
            {
                failure = NewFailure("response_parse", ex.GetType().Name);
                return false;
            }
            if (!TryValidateResponse(response, request, expectedManifest, out failure))
            {
                response = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Parses a rejection as diagnostic transport only. It still requires the complete
        /// worker fingerprint set to match the expected manifest exactly. A mismatch remains
        /// an untrusted response and therefore returns false.
        /// </summary>
        public static bool TryRejectedResponseFromJson(
            string json,
            string expectedRequestId,
            string expectedResponseKind,
            UltraBuildManifest expectedManifest,
            out UltraWorkerResponse response,
            out UltraWorkerFailure failure)
        {
            response = null;
            failure = null;
            if (!IsUtf8SizeWithin(json, MaxResponseJsonBytes))
            {
                failure = NewFailure("response_size", "rejection JSON is empty or exceeds the hard cap");
                return false;
            }
            if (!TryEnsureKnownJsonProperties(json, ResponseJsonProperties, out string propertyError))
            {
                failure = NewFailure("response_schema", propertyError);
                return false;
            }
            try
            {
                response = JsonUtility.FromJson<UltraWorkerResponse>(json);
            }
            catch (Exception ex)
            {
                failure = NewFailure("response_parse", ex.GetType().Name);
                return false;
            }
            if (!TryValidateRejectedResponse(
                    response,
                    expectedRequestId,
                    expectedResponseKind,
                    expectedManifest,
                    out failure))
            {
                response = null;
                return false;
            }
            return true;
        }

        // ==================================================================
        //  checksum (handoff §10 Phase C: IPC は checksum 必須)
        // ==================================================================

        /// <summary>Checksum over the mechanically meaningful request content.
        ///
        /// <para><b>Why a checksum on top of length caps and property whitelists.</b> Those
        /// catch a message that is too big or has the wrong shape. Neither notices a pipe
        /// write that was cut short at a value boundary, or a file half-written when a worker
        /// was killed ── that arrives as a smaller, well-formed, entirely believable message.
        /// The checksum is the only check that distinguishes "complete" from "plausible".</para></summary>
        public static string ComputeRequestChecksum(UltraWorkerRequest request)
        {
            if (request == null) return "";
            var sb = new StringBuilder(512);
            sb.Append("ultra-request-checksum-v1\n")
              .Append(request.protocolVersion).Append('\n')
              .Append(request.kind ?? "").Append('\n')
              .Append(request.requestId ?? "").Append('\n');
            AppendFingerprints(sb, request.expectedFingerprints);

            // **An absent payload and an all-default payload must hash the same.**
            //   JsonUtility does not round-trip null for a nested serializable field, so a
            //   message sealed with `episode = null` comes back with a default instance. If
            //   those hashed differently, every message would fail its own checksum after
            //   crossing the very boundary the checksum exists to protect.
            UltraWorkerEpisodePayload e = request.episode;
            sb.Append(IsEpisodePayloadEmpty(e) ? "-\n" : string.Concat(
                e.checkpointSchema ?? "", "\n",
                e.publicCheckpointBase64 ?? "", "\n",
                e.actionId ?? "", "\n",
                e.actionPayload ?? "", "\n",
                e.objectiveId ?? "", "\n",
                e.scenarioStartSeedHex ?? "", "\n",
                e.maxProductionSteps.ToString(CultureInfo.InvariantCulture), "\n"));

            UltraWorkerControlPayload c = request.control;
            sb.Append(IsControlPayloadEmpty(c) ? "-\n" : string.Concat(
                c.checkpointSchema ?? "", "\n",
                c.publicCheckpointBase64 ?? "", "\n",
                c.epoch.ToString(CultureInfo.InvariantCulture), "\n",
                c.decisionPoint.ToString(CultureInfo.InvariantCulture), "\n"));

            return UltraPortfolioProtocol.Sha256Text(sb.ToString());
        }

        public static string ComputeResponseChecksum(UltraWorkerResponse response)
        {
            if (response == null) return "";
            var sb = new StringBuilder(512);
            sb.Append("ultra-response-checksum-v1\n")
              .Append(response.protocolVersion).Append('\n')
              .Append(response.kind ?? "").Append('\n')
              .Append(response.requestId ?? "").Append('\n')
              .Append(response.accepted ? "1" : "0").Append('\n');
            AppendFingerprints(sb, response.workerFingerprints);

            UltraWorkerControlResult r = response.controlResult;
            if (r == null) sb.Append("-\n");
            else
            {
                string[] ids = r.legalActionIds ?? new string[0];
                sb.Append(ids.Length).Append('\n');
                for (int i = 0; i < ids.Length; i++) sb.Append(ids[i] ?? "").Append('\n');
                sb.Append(r.checkpointMechanicalHash ?? "").Append('\n')
                  .Append(r.epoch).Append('\n')
                  .Append(r.busy ? "1" : "0").Append('\n')
                  .Append(r.cancelled ? "1" : "0").Append('\n');
                // uptime / lastProgress are excluded: they change between composing and
                // sending, so including them would make a correct message fail its own check.
            }
            return UltraPortfolioProtocol.Sha256Text(sb.ToString());
        }

        /// <summary>Whether a fingerprint set carries anything.
        ///
        /// <para>Same JsonUtility behaviour as the payload checks: a message sealed with
        /// <c>expectedFingerprints = null</c> comes back with a default-constructed set, and
        /// hashing "absent" differently from "all empty" makes a correct message fail its own
        /// checksum after transport. This is the fourth place that behaviour has had to be
        /// handled — see also <see cref="IsControlPayloadEmpty"/>,
        /// <see cref="IsEpisodePayloadEmpty"/>, and <c>UltraResumePayload.TryFromBase64</c>.</para></summary>
        public static bool IsFingerprintSetEmpty(UltraFingerprintSet set)
        {
            return set == null
                || (string.IsNullOrEmpty(set.buildFingerprint)
                    && string.IsNullOrEmpty(set.dataFingerprint)
                    && string.IsNullOrEmpty(set.checkpointFingerprint)
                    && string.IsNullOrEmpty(set.actionFingerprint)
                    && string.IsNullOrEmpty(set.objectiveFingerprint));
        }

        private static void AppendFingerprints(StringBuilder sb, UltraFingerprintSet set)
        {
            if (IsFingerprintSetEmpty(set)) { sb.Append("-\n"); return; }
            sb.Append(set.buildFingerprint ?? "").Append('\n')
              .Append(set.dataFingerprint ?? "").Append('\n')
              .Append(set.checkpointFingerprint ?? "").Append('\n')
              .Append(set.actionFingerprint ?? "").Append('\n')
              .Append(set.objectiveFingerprint ?? "").Append('\n');
        }

        public static void SealRequest(UltraWorkerRequest request)
        {
            if (request != null) request.payloadChecksum = ComputeRequestChecksum(request);
        }

        public static void SealResponse(UltraWorkerResponse response)
        {
            if (response != null) response.payloadChecksum = ComputeResponseChecksum(response);
        }

        public const string FailureChecksumMissing = "checksum_missing";
        public const string FailureChecksumMismatch = "checksum_mismatch";

        public static bool TryVerifyRequestChecksum(
            UltraWorkerRequest request, out UltraWorkerFailure failure)
        {
            failure = null;
            if (request == null)
            {
                failure = NewFailure("request_missing", "worker request is null");
                return false;
            }
            if (string.IsNullOrEmpty(request.payloadChecksum))
            {
                failure = NewFailure(FailureChecksumMissing, "request carries no payload checksum");
                return false;
            }
            if (!string.Equals(request.payloadChecksum, ComputeRequestChecksum(request),
                    StringComparison.Ordinal))
            {
                failure = NewFailure(FailureChecksumMismatch,
                    "request payload checksum does not match its content");
                return false;
            }
            return true;
        }

        public static bool TryVerifyResponseChecksum(
            UltraWorkerResponse response, out UltraWorkerFailure failure)
        {
            failure = null;
            if (response == null)
            {
                failure = NewFailure("response_missing", "worker response is null");
                return false;
            }
            if (string.IsNullOrEmpty(response.payloadChecksum))
            {
                failure = NewFailure(FailureChecksumMissing, "response carries no payload checksum");
                return false;
            }
            if (!string.Equals(response.payloadChecksum, ComputeResponseChecksum(response),
                    StringComparison.Ordinal))
            {
                failure = NewFailure(FailureChecksumMismatch,
                    "response payload checksum does not match its content");
                return false;
            }
            return true;
        }

        // ==================================================================
        //  control messages
        // ==================================================================

        /// <summary>Whether a control payload actually carries anything.
        ///
        /// <para><b>`!= null` is not usable here.</b> <see cref="JsonUtility"/> does not
        /// round-trip null for a nested serializable field ── a message that omitted the
        /// property deserializes with a default-constructed instance. Checking for null would
        /// therefore reject every episode request that had been through JSON, which is all of
        /// them.</para></summary>
        /// <summary>Same "absent or all-default" rule as
        /// <see cref="IsControlPayloadEmpty"/>, for the episode payload.</summary>
        public static bool IsEpisodePayloadEmpty(UltraWorkerEpisodePayload episode)
        {
            return episode == null
                || (string.IsNullOrEmpty(episode.checkpointSchema)
                    && string.IsNullOrEmpty(episode.publicCheckpointBase64)
                    && string.IsNullOrEmpty(episode.actionId)
                    && string.IsNullOrEmpty(episode.actionPayload)
                    && string.IsNullOrEmpty(episode.objectiveId)
                    && string.IsNullOrEmpty(episode.scenarioStartSeedHex)
                    && episode.maxProductionSteps == 0);
        }

        public static bool IsControlPayloadEmpty(UltraWorkerControlPayload control)
        {
            return control == null
                || (string.IsNullOrEmpty(control.checkpointSchema)
                    && string.IsNullOrEmpty(control.publicCheckpointBase64)
                    && control.epoch == 0
                    && control.decisionPoint == 0);
        }

        public static bool IsControlKind(string kind)
        {
            return string.Equals(kind, LegalActionsRequestKind, StringComparison.Ordinal)
                || string.Equals(kind, HeartbeatRequestKind, StringComparison.Ordinal)
                || string.Equals(kind, CancelRequestKind, StringComparison.Ordinal);
        }

        public static UltraWorkerRequest CreateControlRequest(
            string kind, string requestId, UltraFingerprintSet expectedFingerprints,
            UltraWorkerControlPayload control)
        {
            var request = new UltraWorkerRequest
            {
                kind = kind,
                requestId = requestId,
                expectedFingerprints = expectedFingerprints?.Clone(),
                episode = null,
                control = control,
            };
            SealRequest(request);
            return request;
        }

        /// <summary>Shape rules for a control request. Kept separate from the episode
        /// validator because the requirements genuinely differ ── a heartbeat that carried a
        /// checkpoint would defeat the purpose of having a cheap liveness probe.</summary>
        public static bool TryValidateControlRequest(
            UltraWorkerRequest request, out UltraWorkerFailure failure)
        {
            failure = null;
            if (!IsControlKind(request?.kind))
            {
                failure = NewFailure("request_kind", "request kind is not a control kind");
                return false;
            }
            if (request.episode != null)
            {
                failure = NewFailure("request_shape", "control request carries an episode payload");
                return false;
            }

            bool needsCheckpoint = string.Equals(
                request.kind, LegalActionsRequestKind, StringComparison.Ordinal);
            UltraWorkerControlPayload control = request.control;

            if (!needsCheckpoint)
            {
                if (control != null && !string.IsNullOrEmpty(control.publicCheckpointBase64))
                {
                    failure = NewFailure("request_shape",
                        "heartbeat/cancel must not carry a checkpoint");
                    return false;
                }
                return true;
            }

            if (IsControlPayloadEmpty(control))
            {
                failure = NewFailure("request_shape", "legal-actions request has no control payload");
                return false;
            }
            if (!IsUtf8SizeWithin(control.checkpointSchema, MaxSchemaChars))
            {
                failure = NewFailure("checkpoint_schema", "checkpoint schema is missing or invalid");
                return false;
            }
            if (!IsUtf8SizeWithin(control.publicCheckpointBase64, MaxCheckpointBase64Chars))
            {
                failure = NewFailure("checkpoint_payload", "checkpoint payload is missing or too large");
                return false;
            }
            if (control.epoch < 0)
            {
                failure = NewFailure("epoch", "epoch must not be negative");
                return false;
            }
            return true;
        }

        public static bool TryCreateControlResponse(
            UltraWorkerRequest request, UltraBuildManifest workerManifest,
            UltraWorkerControlResult result,
            out UltraWorkerResponse response, out UltraWorkerFailure failure)
        {
            response = null;
            if (!TryValidateRequest(request, workerManifest, out failure)) return false;
            if (!TryValidateControlRequest(request, out failure)) return false;
            if (result == null)
            {
                failure = NewFailure("control_result", "control result is null");
                return false;
            }
            if ((result.legalActionIds?.Length ?? 0) > MaxLegalActionCount)
            {
                failure = NewFailure("control_result", "legal action list is too large");
                return false;
            }

            response = new UltraWorkerResponse
            {
                kind = ResponseKindFor(request.kind),
                requestId = request.requestId,
                accepted = true,
                workerFingerprints = workerManifest.fingerprints.Clone(),
                workerManifest = null,
                episodeResult = null,
                controlResult = result,
                failure = null,
            };
            SealResponse(response);
            return true;
        }

        public static string ResponseKindFor(string requestKind)
        {
            if (string.Equals(requestKind, LegalActionsRequestKind, StringComparison.Ordinal))
                return LegalActionsResponseKind;
            if (string.Equals(requestKind, HeartbeatRequestKind, StringComparison.Ordinal))
                return HeartbeatResponseKind;
            if (string.Equals(requestKind, CancelRequestKind, StringComparison.Ordinal))
                return CancelResponseKind;
            if (string.Equals(requestKind, HandshakeRequestKind, StringComparison.Ordinal))
                return HandshakeResponseKind;
            if (string.Equals(requestKind, EpisodeRequestKind, StringComparison.Ordinal))
                return EpisodeResponseKind;
            return "";
        }

        /// <summary>Compares the worker's legal action set against the parent's.
        ///
        /// <para>Order and duplicates are ignored ── the sets are what matter. A mismatch is
        /// reported as the symmetric difference so the log names the actual disagreement
        /// instead of just saying the two sides differ.</para></summary>
        public static bool LegalActionSetsAgree(
            string[] parentActionIds, string[] workerActionIds, out string difference)
        {
            var parent = new System.Collections.Generic.HashSet<string>(
                parentActionIds ?? new string[0], StringComparer.Ordinal);
            var worker = new System.Collections.Generic.HashSet<string>(
                workerActionIds ?? new string[0], StringComparer.Ordinal);

            var parentOnly = new System.Collections.Generic.List<string>();
            foreach (string id in parent) if (!worker.Contains(id)) parentOnly.Add(id);
            var workerOnly = new System.Collections.Generic.List<string>();
            foreach (string id in worker) if (!parent.Contains(id)) workerOnly.Add(id);

            if (parentOnly.Count == 0 && workerOnly.Count == 0)
            {
                difference = "";
                return true;
            }
            parentOnly.Sort(StringComparer.Ordinal);
            workerOnly.Sort(StringComparer.Ordinal);
            difference = "parent-only=[" + string.Join(",", parentOnly.ToArray())
                       + "] worker-only=[" + string.Join(",", workerOnly.ToArray()) + "]";
            return false;
        }

        internal static bool IsUtf8SizeWithin(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value) || maxBytes < 1 || value.Length > maxBytes) return false;
            try
            {
                return Encoding.UTF8.GetByteCount(value) <= maxBytes;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// JsonUtility ignores unknown fields. The worker transport cannot safely do that,
        /// so raw JSON is scanned first and any unknown or escaped property name is rejected.
        /// This also prevents an added live-seed/RNG-state property from crossing the boundary.
        /// </summary>
        internal static bool TryEnsureKnownJsonProperties(
            string json,
            string[] allowedProperties,
            out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(json))
            {
                error = "JSON is empty";
                return false;
            }
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] != '\"') continue;
                int start = ++i;
                bool escaped = false;
                bool closed = false;
                for (; i < json.Length; i++)
                {
                    char ch = json[i];
                    if (ch == '\\')
                    {
                        escaped = true;
                        i++;
                        if (i >= json.Length)
                        {
                            error = "unterminated JSON escape";
                            return false;
                        }
                        continue;
                    }
                    if (ch == '\"')
                    {
                        closed = true;
                        break;
                    }
                }
                if (!closed)
                {
                    error = "unterminated JSON string";
                    return false;
                }

                int end = i;
                int next = i + 1;
                while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                if (next >= json.Length || json[next] != ':') continue;
                if (escaped)
                {
                    error = "escaped JSON property names are not permitted";
                    return false;
                }

                string property = json.Substring(start, end - start);
                bool known = false;
                for (int p = 0; p < allowedProperties.Length; p++)
                {
                    if (string.Equals(property, allowedProperties[p], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }
                if (!known)
                {
                    error = "unknown JSON property: " + Truncate(property, 128);
                    return false;
                }
            }
            return true;
        }

        private static bool TryValidateEpisodePayload(
            UltraWorkerEpisodePayload episode,
            out UltraWorkerFailure failure)
        {
            failure = null;
            if (episode == null)
            {
                failure = NewFailure("episode_missing", "episode payload is null");
                return false;
            }
            if (!IsToken(episode.checkpointSchema, MaxSchemaChars))
            {
                failure = NewFailure("checkpoint_schema", "checkpoint schema is missing or invalid");
                return false;
            }
            if (!IsCanonicalBase64(
                    episode.publicCheckpointBase64,
                    MaxCheckpointBase64Chars,
                    MaxCheckpointDecodedBytes))
            {
                failure = NewFailure("checkpoint_payload", "public checkpoint is not bounded canonical base64");
                return false;
            }
            if (!IsToken(episode.actionId, MaxActionIdChars))
            {
                failure = NewFailure("action_id", "action id is missing or invalid");
                return false;
            }
            if (episode.actionPayload != null && episode.actionPayload.Length > MaxActionPayloadChars)
            {
                failure = NewFailure("action_payload", "action payload exceeds the hard cap");
                return false;
            }
            if (!IsToken(episode.objectiveId, MaxObjectiveIdChars))
            {
                failure = NewFailure("objective_id", "objective id is missing or invalid");
                return false;
            }
            if (!IsFixedHex64(episode.scenarioStartSeedHex))
            {
                failure = NewFailure("scenario_start_seed", "scenario start seed must be exactly 16 hexadecimal characters");
                return false;
            }
            if (episode.maxProductionSteps < 1 || episode.maxProductionSteps > MaxProductionSteps)
            {
                failure = NewFailure("step_limit", "production step limit is outside the hard bounds");
                return false;
            }
            return true;
        }

        private static bool TryValidateRejectedResponse(
            UltraWorkerResponse response,
            string expectedRequestId,
            string expectedResponseKind,
            UltraBuildManifest expectedManifest,
            out UltraWorkerFailure failure)
        {
            failure = null;
            if (!UltraBuildManifestValidation.TryValidate(expectedManifest, out string code, out string detail))
            {
                failure = NewFailure(code, detail);
                return false;
            }
            if (!IsToken(expectedRequestId, MaxRequestIdChars))
            {
                failure = NewFailure("request_id", "expected request id is missing or invalid");
                return false;
            }
            if (!string.Equals(expectedResponseKind, HandshakeResponseKind, StringComparison.Ordinal)
                && !string.Equals(expectedResponseKind, EpisodeResponseKind, StringComparison.Ordinal))
            {
                failure = NewFailure("response_kind", "expected rejection response kind is unsupported");
                return false;
            }
            if (response == null)
            {
                failure = NewFailure("response_missing", "worker rejection is null");
                return false;
            }
            if (response.protocolVersion != CurrentProtocolVersion)
            {
                failure = NewFailure("protocol_version", "rejection protocol version is unsupported");
                return false;
            }
            if (!string.Equals(response.requestId, expectedRequestId, StringComparison.Ordinal))
            {
                failure = NewFailure("request_id_mismatch", "rejection request id does not match");
                return false;
            }
            if (!string.Equals(response.kind, expectedResponseKind, StringComparison.Ordinal))
            {
                failure = NewFailure("response_kind", "rejection response kind does not match");
                return false;
            }
            if (response.accepted)
            {
                failure = NewFailure("response_shape", "rejection is marked accepted");
                return false;
            }
            // JsonUtility may materialize an omitted nested reference as a default empty
            // object on round-trip. Reject only a semantically usable success payload;
            // accepted=false is handled on this diagnostic-only codec and can never feed
            // an episode result to gameplay.
            bool containsUsableManifest = response.workerManifest != null
                && UltraBuildManifestValidation.TryValidate(
                    response.workerManifest,
                    out _,
                    out _);
            bool containsUsableEpisode = response.episodeResult != null
                && response.episodeResult.IsUsable;
            if (containsUsableManifest || containsUsableEpisode)
            {
                failure = NewFailure("response_shape", "rejection contains a success payload");
                return false;
            }
            if (!UltraBuildManifestValidation.TryMatchFingerprintsExactly(
                    expectedManifest.fingerprints,
                    response.workerFingerprints,
                    out code,
                    out detail))
            {
                failure = NewFailure(code, detail);
                return false;
            }
            if (!IsValidFailure(response.failure))
            {
                failure = NewFailure("worker_rejected", "rejection failure payload is invalid");
                return false;
            }
            return true;
        }

        private static bool TryValidateEpisodeResult(
            UltraEpisodeResult result,
            int requestedStepLimit,
            out UltraWorkerFailure failure)
        {
            failure = null;
            if (result == null || !result.IsUsable)
            {
                failure = NewFailure("episode_result", "episode result is absent or unusable");
                return false;
            }
            if (requestedStepLimit < 1 || requestedStepLimit > MaxProductionSteps
                || result.productionSteps < 0
                || result.productionSteps > requestedStepLimit
                || result.productionSteps > MaxProductionSteps)
            {
                failure = NewFailure("step_limit", "episode result exceeded the requested step limit");
                return false;
            }
            if (!IsToken(result.finalPublicStateHash, MaxFinalStateHashChars))
            {
                failure = NewFailure("final_state_hash", "final public state hash is missing or invalid");
                return false;
            }
            if (!string.IsNullOrEmpty(result.failureCode) || !string.IsNullOrEmpty(result.failureDetail))
            {
                failure = NewFailure("episode_result", "successful episode result contains failure information");
                return false;
            }
            return true;
        }

        private static bool IsValidFailure(UltraWorkerFailure failure)
        {
            return failure != null
                && IsToken(failure.code, MaxFailureCodeChars)
                && (failure.detail == null || failure.detail.Length <= MaxFailureDetailChars);
        }

        private static bool IsToken(string value, int maxChars)
        {
            return UltraBuildManifestValidation.IsRequiredIdentifier(value, maxChars);
        }

        private static bool IsFixedHex64(string value)
        {
            if (value == null || value.Length != 16) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                bool hex = (ch >= '0' && ch <= '9')
                    || (ch >= 'a' && ch <= 'f')
                    || (ch >= 'A' && ch <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static bool IsCanonicalBase64(string value, int maxChars, int maxDecodedBytes)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maxChars || (value.Length & 3) != 0)
                return false;

            int padding = 0;
            if (value[value.Length - 1] == '=') padding++;
            if (value.Length > 1 && value[value.Length - 2] == '=') padding++;
            long decodedBytes = ((long)value.Length / 4L) * 3L - padding;
            if (decodedBytes < 1 || decodedBytes > maxDecodedBytes) return false;

            int dataEnd = value.Length - padding;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (i >= dataEnd)
                {
                    if (ch != '=') return false;
                    continue;
                }
                bool valid = (ch >= 'A' && ch <= 'Z')
                    || (ch >= 'a' && ch <= 'z')
                    || (ch >= '0' && ch <= '9')
                    || ch == '+'
                    || ch == '/';
                if (!valid) return false;
            }

            // RFC 4648 canonical encoding requires unused low bits in the final sextet
            // to be zero. Without this, multiple strings can represent the same bytes.
            if (padding == 2 && (Base64Value(value[dataEnd - 1]) & 0x0F) != 0) return false;
            if (padding == 1 && (Base64Value(value[dataEnd - 1]) & 0x03) != 0) return false;
            return true;
        }

        private static int Base64Value(char ch)
        {
            if (ch >= 'A' && ch <= 'Z') return ch - 'A';
            if (ch >= 'a' && ch <= 'z') return ch - 'a' + 26;
            if (ch >= '0' && ch <= '9') return ch - '0' + 52;
            if (ch == '+') return 62;
            if (ch == '/') return 63;
            return -1;
        }

        private static UltraEpisodeResult CloneResult(UltraEpisodeResult source)
        {
            if (source == null) return null;
            return new UltraEpisodeResult
            {
                success = source.success,
                reachedTerminal = source.reachedTerminal,
                primaryReward = source.primaryReward,
                finalPublicStateHash = source.finalPublicStateHash,
                productionSteps = source.productionSteps,
                failureCode = source.failureCode,
                failureDetail = source.failureDetail,
            };
        }

        private static UltraWorkerFailure NewFailure(string code, string detail)
        {
            return new UltraWorkerFailure
            {
                code = string.IsNullOrEmpty(code) ? "protocol_failure" : Truncate(code, MaxFailureCodeChars),
                detail = Truncate(detail, MaxFailureDetailChars),
            };
        }

        private static string Truncate(string value, int maxChars)
        {
            if (value == null || value.Length <= maxChars) return value;
            return value.Substring(0, maxChars);
        }
    }
}

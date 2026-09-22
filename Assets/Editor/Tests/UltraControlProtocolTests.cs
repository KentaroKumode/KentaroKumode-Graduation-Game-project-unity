#if UNITY_EDITOR
using System;
using AutoTest.Ultra;
using NUnit.Framework;
using UnityEngine;

namespace AutoTest.EditorTests.Ultra
{
    /// <summary>
    /// Phase C: the control message kinds (legal actions / heartbeat / cancel) and the
    /// payload checksum that every IPC message now carries.
    /// </summary>
    [TestFixture]
    public sealed class UltraControlProtocolTests
    {
        private static UltraFingerprintSet Fingerprints()
        {
            return new UltraFingerprintSet
            {
                buildFingerprint = new string('a', 64),
                dataFingerprint = new string('b', 64),
                checkpointFingerprint = new string('c', 64),
                actionFingerprint = new string('d', 64),
                objectiveFingerprint = new string('e', 64),
            };
        }

        private static UltraWorkerRequest LegalActionsRequest(int epoch = 3)
        {
            return UltraWorkerProtocol.CreateControlRequest(
                UltraWorkerProtocol.LegalActionsRequestKind,
                "req-legal-1",
                Fingerprints(),
                new UltraWorkerControlPayload
                {
                    checkpointSchema = "ultra.checkpoint.v1",
                    publicCheckpointBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
                    epoch = epoch,
                    decisionPoint = (int)UltraDecisionPoint.MapNavigation,
                });
        }

        // ------------------------------------------------------------------
        //  checksum
        // ------------------------------------------------------------------

        [Test]
        public void ASealedRequest_VerifiesAgainstItsOwnContent()
        {
            Assert.That(UltraWorkerProtocol.TryVerifyRequestChecksum(
                LegalActionsRequest(), out UltraWorkerFailure failure), Is.True,
                failure != null ? failure.code : "");
        }

        [Test]
        public void ARequestWithNoChecksum_IsRefused()
        {
            UltraWorkerRequest request = LegalActionsRequest();
            request.payloadChecksum = "";

            Assert.That(UltraWorkerProtocol.TryVerifyRequestChecksum(request, out UltraWorkerFailure failure), Is.False);
            Assert.That(failure.code, Is.EqualTo(UltraWorkerProtocol.FailureChecksumMissing));
        }

        /// <summary>The case length caps and property whitelists cannot see: a message that is
        /// smaller, well-formed and completely believable because the writer was cut short.</summary>
        [Test]
        public void ATruncatedButWellFormedRequest_IsRefused()
        {
            UltraWorkerRequest request = LegalActionsRequest();
            request.control.publicCheckpointBase64 =
                request.control.publicCheckpointBase64.Substring(0, 2);

            Assert.That(UltraWorkerProtocol.TryVerifyRequestChecksum(request, out UltraWorkerFailure failure), Is.False);
            Assert.That(failure.code, Is.EqualTo(UltraWorkerProtocol.FailureChecksumMismatch));
        }

        [Test]
        public void EditingAnyMechanicalField_BreaksTheChecksum()
        {
            foreach (Action<UltraWorkerRequest> edit in new Action<UltraWorkerRequest>[]
            {
                r => r.kind = UltraWorkerProtocol.HeartbeatRequestKind,
                r => r.requestId = "someone-elses-id",
                r => r.control.epoch += 1,
                r => r.control.decisionPoint += 1,
                r => r.control.checkpointSchema = "other.schema",
                r => r.expectedFingerprints.buildFingerprint = new string('f', 64),
            })
            {
                UltraWorkerRequest request = LegalActionsRequest();
                edit(request);
                Assert.That(UltraWorkerProtocol.TryVerifyRequestChecksum(request, out _), Is.False);
            }
        }

        [Test]
        public void TheChecksumSurvivesAJsonRoundTrip()
        {
            UltraWorkerRequest sent = LegalActionsRequest();
            var received = JsonUtility.FromJson<UltraWorkerRequest>(JsonUtility.ToJson(sent));

            Assert.That(received.payloadChecksum, Is.EqualTo(sent.payloadChecksum));
            Assert.That(UltraWorkerProtocol.TryVerifyRequestChecksum(received, out _), Is.True);
        }

        /// <summary>Found by the checksum itself when the first real episode job crossed the
        /// process boundary: a request sealed without fingerprints came back with a
        /// default-constructed set, and "absent" hashed differently from "all empty".</summary>
        [Test]
        public void ARequestSealedWithoutFingerprints_StillVerifiesAfterTransport()
        {
            UltraWorkerRequest sent = UltraWorkerProtocol.CreateControlRequest(
                UltraWorkerProtocol.HeartbeatRequestKind, "req-nofp", null, null);

            var received = JsonUtility.FromJson<UltraWorkerRequest>(JsonUtility.ToJson(sent));

            Assert.That(UltraWorkerProtocol.TryVerifyRequestChecksum(
                received, out UltraWorkerFailure failure), Is.True,
                failure != null ? failure.code : "");
        }

        [Test]
        public void AnEmptyFingerprintSetCountsAsAbsent()
        {
            Assert.That(UltraWorkerProtocol.IsFingerprintSetEmpty(null), Is.True);
            Assert.That(UltraWorkerProtocol.IsFingerprintSetEmpty(new UltraFingerprintSet()), Is.True);
            Assert.That(UltraWorkerProtocol.IsFingerprintSetEmpty(Fingerprints()), Is.False);
        }

        /// <summary>Uptime and last-progress move between composing and sending a message, so
        /// they are excluded from the response checksum — otherwise a correct heartbeat would
        /// fail its own verification.</summary>
        [Test]
        public void ResponseChecksum_IgnoresTheClockFields()
        {
            var a = new UltraWorkerResponse
            {
                kind = UltraWorkerProtocol.HeartbeatResponseKind,
                requestId = "req-1",
                accepted = true,
                workerFingerprints = Fingerprints(),
                controlResult = new UltraWorkerControlResult { busy = true, uptimeMs = 10 },
            };
            var b = new UltraWorkerResponse
            {
                kind = a.kind,
                requestId = a.requestId,
                accepted = true,
                workerFingerprints = Fingerprints(),
                controlResult = new UltraWorkerControlResult { busy = true, uptimeMs = 99999 },
            };

            Assert.That(UltraWorkerProtocol.ComputeResponseChecksum(b),
                Is.EqualTo(UltraWorkerProtocol.ComputeResponseChecksum(a)));
        }

        [Test]
        public void ResponseChecksum_CoversTheLegalActionList()
        {
            var a = new UltraWorkerResponse
            {
                kind = UltraWorkerProtocol.LegalActionsResponseKind,
                requestId = "req-1",
                accepted = true,
                workerFingerprints = Fingerprints(),
                controlResult = new UltraWorkerControlResult { legalActionIds = new[] { "2::-1" } },
            };
            string before = UltraWorkerProtocol.ComputeResponseChecksum(a);
            a.controlResult.legalActionIds = new[] { "2::-1", "2:boss:-1" };

            Assert.That(UltraWorkerProtocol.ComputeResponseChecksum(a), Is.Not.EqualTo(before));
        }

        // ------------------------------------------------------------------
        //  message shape
        // ------------------------------------------------------------------

        [Test]
        public void AllThreeControlKinds_AreRecognised()
        {
            Assert.That(UltraWorkerProtocol.IsControlKind(UltraWorkerProtocol.LegalActionsRequestKind), Is.True);
            Assert.That(UltraWorkerProtocol.IsControlKind(UltraWorkerProtocol.HeartbeatRequestKind), Is.True);
            Assert.That(UltraWorkerProtocol.IsControlKind(UltraWorkerProtocol.CancelRequestKind), Is.True);
            Assert.That(UltraWorkerProtocol.IsControlKind(UltraWorkerProtocol.EpisodeRequestKind), Is.False);
            Assert.That(UltraWorkerProtocol.IsControlKind(null), Is.False);
        }

        [Test]
        public void EveryRequestKind_HasAMatchingResponseKind()
        {
            foreach (string kind in new[]
            {
                UltraWorkerProtocol.HandshakeRequestKind,
                UltraWorkerProtocol.EpisodeRequestKind,
                UltraWorkerProtocol.LegalActionsRequestKind,
                UltraWorkerProtocol.HeartbeatRequestKind,
                UltraWorkerProtocol.CancelRequestKind,
            })
                Assert.That(UltraWorkerProtocol.ResponseKindFor(kind), Is.Not.Empty, kind);

            Assert.That(UltraWorkerProtocol.ResponseKindFor("ultra.nonsense"), Is.Empty);
        }

        [Test]
        public void ALegalActionsRequest_IsWellFormed()
        {
            Assert.That(UltraWorkerProtocol.TryValidateControlRequest(
                LegalActionsRequest(), out UltraWorkerFailure failure), Is.True,
                failure != null ? failure.detail : "");
        }

        [Test]
        public void ALegalActionsRequestWithNoCheckpoint_IsRefused()
        {
            UltraWorkerRequest request = LegalActionsRequest();
            request.control = new UltraWorkerControlPayload();

            Assert.That(UltraWorkerProtocol.TryValidateControlRequest(request, out UltraWorkerFailure failure), Is.False);
            Assert.That(failure.code, Is.EqualTo("request_shape"));
        }

        /// <summary>A heartbeat has to stay answerable while the worker is busy, so it must not
        /// drag a checkpoint-sized payload along.</summary>
        [Test]
        public void AHeartbeatCarryingACheckpoint_IsRefused()
        {
            UltraWorkerRequest request = UltraWorkerProtocol.CreateControlRequest(
                UltraWorkerProtocol.HeartbeatRequestKind, "req-hb", Fingerprints(),
                new UltraWorkerControlPayload
                { publicCheckpointBase64 = Convert.ToBase64String(new byte[] { 9 }) });

            Assert.That(UltraWorkerProtocol.TryValidateControlRequest(request, out UltraWorkerFailure failure), Is.False);
            Assert.That(failure.code, Is.EqualTo("request_shape"));
        }

        [Test]
        public void AHeartbeatAndACancel_NeedNoPayloadAtAll()
        {
            foreach (string kind in new[]
            {
                UltraWorkerProtocol.HeartbeatRequestKind,
                UltraWorkerProtocol.CancelRequestKind,
            })
            {
                UltraWorkerRequest request = UltraWorkerProtocol.CreateControlRequest(
                    kind, "req-" + kind, Fingerprints(), null);
                Assert.That(UltraWorkerProtocol.TryValidateControlRequest(request, out UltraWorkerFailure f),
                    Is.True, f != null ? f.detail : "");
            }
        }

        [Test]
        public void AControlRequestCarryingAnEpisode_IsRefused()
        {
            UltraWorkerRequest request = LegalActionsRequest();
            request.episode = new UltraWorkerEpisodePayload();

            Assert.That(UltraWorkerProtocol.TryValidateControlRequest(request, out UltraWorkerFailure failure), Is.False);
            Assert.That(failure.code, Is.EqualTo("request_shape"));
        }

        /// <summary>JsonUtility gives absent nested objects a default instance rather than
        /// null, so "carries a control payload" has to mean "carries a non-empty one".</summary>
        [Test]
        public void AnEmptyControlPayload_CountsAsAbsent()
        {
            Assert.That(UltraWorkerProtocol.IsControlPayloadEmpty(null), Is.True);
            Assert.That(UltraWorkerProtocol.IsControlPayloadEmpty(new UltraWorkerControlPayload()), Is.True);
            Assert.That(UltraWorkerProtocol.IsControlPayloadEmpty(
                new UltraWorkerControlPayload { epoch = 1 }), Is.False);
        }

        // ------------------------------------------------------------------
        //  legal action set comparison
        // ------------------------------------------------------------------

        [Test]
        public void IdenticalSets_Agree_RegardlessOfOrderOrDuplicates()
        {
            Assert.That(UltraWorkerProtocol.LegalActionSetsAgree(
                new[] { "2:n1:-1", "2:n2:-1" },
                new[] { "2:n2:-1", "2:n1:-1", "2:n1:-1" },
                out string difference), Is.True, difference);
        }

        [Test]
        public void DisagreeingSets_ReportWhichSideHasWhat()
        {
            Assert.That(UltraWorkerProtocol.LegalActionSetsAgree(
                new[] { "2:n1:-1", "2:n2:-1" },
                new[] { "2:n1:-1", "2:boss:-1" },
                out string difference), Is.False);

            Assert.That(difference, Does.Contain("2:n2:-1"));
            Assert.That(difference, Does.Contain("2:boss:-1"));
        }

        [Test]
        public void EmptySetsAgree()
        {
            Assert.That(UltraWorkerProtocol.LegalActionSetsAgree(null, null, out _), Is.True);
            Assert.That(UltraWorkerProtocol.LegalActionSetsAgree(new string[0], null, out _), Is.True);
        }

        /// <summary>The worker's set is evidence, not permission: comparing it is how a
        /// parent/worker rules mismatch becomes visible instead of silently producing a
        /// meaningless comparison.</summary>
        [Test]
        public void AWorkerThatOffersAnExtraMove_IsDetected()
        {
            UltraObservation observation = new UltraObservation
            {
                map = new UltraMapView
                {
                    nodes = new[]
                    {
                        new UltraMapNodeView { id = "n1", reachableNow = true },
                        new UltraMapNodeView { id = "n2", reachableNow = false },
                    },
                },
            };
            UltraCheckpoint parent = UltraCheckpointProtocol.Create(
                1, UltraDecisionPoint.MapNavigation, observation,
                UltraCheckpointProtocol.MapNavigationActions(observation));

            var parentIds = new string[parent.legalActions.Length];
            for (int i = 0; i < parentIds.Length; i++) parentIds[i] = parent.legalActions[i].ActionId;

            string[] workerIds = { parentIds[0], UltraLegalAction.Of(UltraActionKind.MoveTo, "n2").ActionId };

            Assert.That(UltraWorkerProtocol.LegalActionSetsAgree(parentIds, workerIds, out string difference),
                Is.False, "an unreachable node offered by the worker must be caught");
            Assert.That(difference, Does.Contain("n2"));
        }
    }
}
#endif

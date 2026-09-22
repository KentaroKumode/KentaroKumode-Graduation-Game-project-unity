using System;
using System.Text;
using UnityEngine;

namespace AutoTest.Ultra
{
    /// <summary>Everything a worker needs to resume a run from mid-flight.
    ///
    /// <para><b>The veil is applied here, in the parent, before the blob exists.</b> That
    /// ordering is what makes the payload honest: it is not the true board, it is one sample
    /// from what the player could believe the board to be. A controller that holds this blob —
    /// or a thousand rollouts taken from it — never holds the truth, so the evaluation channel
    /// cannot carry hidden facts back to it (see <see cref="UltraSnapshotVeil"/>).</para>
    ///
    /// <para>Varying <c>veilSeed</c> across rollouts samples different possible boards, which
    /// is the point: an action's value should be its value under uncertainty, not its value on
    /// the one board the parent happened to know about.</para>
    ///
    /// <para>Carries no RNG state. The worker draws its own future from
    /// <c>scenarioStartSeedHex</c>.</para></summary>
    [Serializable]
    public sealed class UltraResumePayload
    {
        public const string Schema = "ultra.resume.v1";
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public int epoch;
        public int decisionPoint;
        /// <summary>Which veil sample this is. Recorded so a rollout set can be checked for
        /// having actually varied its beliefs rather than repeating one board.</summary>
        public int veilSeed;
        public UltraRunSnapshot run;
        public UltraMapSnapshot map;

        /// <summary>Capture and veil in one step.
        ///
        /// <para>There is deliberately no overload that skips the veil. An unveiled resume
        /// payload is a correctness bug that produces better-looking numbers, which is the
        /// kind that survives review.</para></summary>
        public static UltraResumePayload Create(
            GameLoop.RunState run, MapSystem.MapManager map,
            int epoch, UltraDecisionPoint point, int veilSeed,
            out UltraVeilReport veil)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (map == null) throw new ArgumentNullException(nameof(map));

            UltraMapSnapshot mapSnapshot = UltraMapSnapshot.Capture(map);
            veil = UltraSnapshotVeil.Apply(
                mapSnapshot, veilSeed, UltraSnapshotVeil.CurrentFalseMerchantChance());

            // The run half needs veiling too. It carries every RunState field — it must, or a
            // restore would be a different run — so anything classified hidden would otherwise
            // ride into the rollout while the map veil looked like it had closed the leak.
            UltraRunSnapshot runSnapshot = UltraRunSnapshot.Capture(run);
            veil.hiddenRunFieldsNeutralised = UltraSnapshotVeil.ApplyToRun(runSnapshot);

            return new UltraResumePayload
            {
                epoch = epoch,
                decisionPoint = (int)point,
                veilSeed = veilSeed,
                run = runSnapshot,
                map = mapSnapshot,
            };
        }

        public string ToBase64()
        {
            return Convert.ToBase64String(
                new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(this)));
        }

        public static bool TryFromBase64(string encoded, out UltraResumePayload payload, out string failure)
        {
            payload = null;
            failure = null;
            if (string.IsNullOrEmpty(encoded)) { failure = "payload is empty"; return false; }

            try
            {
                string json = new UTF8Encoding(false).GetString(Convert.FromBase64String(encoded));
                payload = JsonUtility.FromJson<UltraResumePayload>(json);
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": " + ex.Message;
                return false;
            }

            if (payload == null) { failure = "payload did not deserialise"; return false; }
            if (payload.version != CurrentVersion)
            {
                failure = "payload version " + payload.version + " != " + CurrentVersion;
                payload = null;
                return false;
            }
            // A payload whose halves are missing would restore a partial run and report success.
            //
            // **Emptiness is the test, not null.** JsonUtility does not round-trip null for a
            // nested serializable field: a payload sent with `map = null` arrives as a
            // default-constructed map with an empty node array. Checking for null would pass it
            // and the worker would resume onto a board with no tiles. This is the third place
            // the same JsonUtility behaviour has had to be handled (see also
            // UltraWorkerProtocol.IsControlPayloadEmpty / IsEpisodePayloadEmpty).
            if (payload.run == null || payload.run.fields == null || payload.run.fields.Length == 0)
            {
                failure = "payload carries no run state";
                payload = null;
                return false;
            }
            if (payload.map == null || payload.map.nodes == null || payload.map.nodes.Length == 0)
            {
                failure = "payload carries no map";
                payload = null;
                return false;
            }
            return true;
        }

        /// <summary>Hash over both halves. Two payloads describing the same resumption point
        /// hash the same, which is what lets a worker's restore be checked rather than trusted.</summary>
        public string ContentHash()
        {
            return UltraPortfolioProtocol.Sha256Text(
                "ultra-resume-v" + version + "\n" + epoch + "\n" + decisionPoint + "\n"
                + (run != null ? run.ContentHash() : "-") + "\n"
                + (map != null ? map.ContentHash() : "-") + "\n");
        }
    }
}

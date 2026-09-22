using System;

namespace AutoTest.Ultra
{
    /// <summary>Where <see cref="AutoRunner"/> offers a decision to an Ultra controller.
    ///
    /// <para><b>The runner asks; it never obeys.</b> An implementation returns an action id and
    /// nothing else — no state, no side effects, no way to advance the run. Everything it
    /// returns is re-checked on the main thread against the checkpoint that was issued, and
    /// anything short of a clean answer falls back to the production policy. A controller that
    /// times out, crashes, or returns nonsense costs one decision's latency and nothing
    /// else.</para>
    ///
    /// <para>Implementations must not touch <see cref="GameLoop.GameRng"/>, the live run, or any
    /// Unity object: they may run off the main thread or in another process.</para></summary>
    public interface IUltraDecisionSink
    {
        /// <summary>Offer one decision. Return false to decline (the runner then uses the
        /// production choice). <paramref name="reason"/> is for logging only.</summary>
        bool TryDecide(UltraCheckpoint checkpoint, out string committedActionId, out string reason);
    }

    /// <summary>Counters for how often the Ultra path actually decided anything.
    ///
    /// <para><b>Falling back is silent by nature</b> — the run still completes, the numbers
    /// still look plausible, and a controller that never answers is indistinguishable from one
    /// that agrees with the baseline every time. That was the shape of the 2026-08-17 failure
    /// (four candidates that were secretly the same). These counters make the difference
    /// observable, so "Ultra ran" is a measurement rather than an assumption.</para></summary>
    public static class UltraDispatchStats
    {
        public static long Offered;
        /// <summary>Committed an action that differed from the production choice.</summary>
        public static long Overrode;
        /// <summary>Committed the same action the production policy would have taken.</summary>
        public static long Agreed;
        public static long Declined;
        public static long Rejected;
        public static long Faulted;

        public static void Reset()
        {
            Offered = Overrode = Agreed = Declined = Rejected = Faulted = 0;
        }

        public static string Describe()
        {
            if (Offered <= 0) return "Ultra dispatch: 提示 0 (controller 未接続)";
            return string.Format(
                "Ultra dispatch: 提示 {0} / 上書き {1} ({2:F1}%) / 同意 {3} / 辞退 {4} / 却下 {5} / 例外 {6}",
                Offered, Overrode, 100.0 * Overrode / Offered, Agreed, Declined, Rejected, Faulted);
        }
    }

    /// <summary>Runs a sink safely: exceptions become a decline, and the answer is only
    /// accepted if it validates against the checkpoint that was issued.
    ///
    /// <para>Kept out of <see cref="AutoRunner"/> so that the fallback rules live in one place
    /// and every future decision point gets them for free rather than re-deriving them.</para></summary>
    public static class UltraDispatch
    {
        /// <summary>Ask the sink for an action at this checkpoint.</summary>
        /// <returns>The validated action id, or null when the production choice should stand.</returns>
        public static string TryResolve(
            IUltraDecisionSink sink, UltraCheckpoint checkpoint,
            int currentEpoch, UltraDecisionPoint currentPoint, out string reason)
        {
            reason = null;
            if (sink == null || checkpoint == null) return null;

            UltraDispatchStats.Offered++;
            string actionId;
            try
            {
                if (!sink.TryDecide(checkpoint, out actionId, out reason))
                {
                    UltraDispatchStats.Declined++;
                    return null;
                }
            }
            catch (Exception ex)
            {
                // A controller fault is not a gameplay loss. Count it and carry on.
                UltraDispatchStats.Faulted++;
                reason = ex.GetType().Name + ": " + ex.Message;
                return null;
            }

            if (!UltraCheckpointProtocol.TryValidateCommit(
                    checkpoint, currentEpoch, currentPoint, actionId, out string failure))
            {
                UltraDispatchStats.Rejected++;
                reason = failure;
                return null;
            }
            return actionId;
        }

        /// <summary>Offer half of a decision that will finish on a later frame.
        ///
        /// <para>The blocking path counts the offer inside <see cref="TryResolve"/>. When the
        /// answer arrives across frames the two halves are split, so the counters have to be
        /// too — otherwise a batch that runs entirely on the async path reports "提示 0
        /// (controller 未接続)" while it is in fact deciding everything.</para></summary>
        public static void NoteOffered()
        {
            UltraDispatchStats.Offered++;
        }

        /// <summary>Accept half. **Applies exactly the same rules as <see cref="TryResolve"/>** —
        /// a decline is counted, and the answer is validated against the checkpoint that was
        /// issued before it can reach the run. Splitting the wait across frames must not be a
        /// way to skip the commit gate.</summary>
        /// <returns>The validated action id, or null when the production choice should stand.</returns>
        public static string CompleteResolve(
            UltraCheckpoint checkpoint, int currentEpoch, UltraDecisionPoint currentPoint,
            string actionId, ref string reason)
        {
            if (actionId == null)
            {
                UltraDispatchStats.Declined++;
                return null;
            }
            if (!UltraCheckpointProtocol.TryValidateCommit(
                    checkpoint, currentEpoch, currentPoint, actionId, out string failure))
            {
                UltraDispatchStats.Rejected++;
                reason = failure;
                return null;
            }
            return actionId;
        }

        /// <summary>A controller fault during an across-frames decision. Not a gameplay loss.</summary>
        public static void NoteFault(Exception ex, ref string reason)
        {
            UltraDispatchStats.Faulted++;
            reason = ex == null ? "fault" : ex.GetType().Name + ": " + ex.Message;
        }

        /// <summary>Record whether the accepted action changed anything. Call after the action
        /// has been resolved to a concrete move, so "agreed" means agreed in effect.</summary>
        public static void NoteOutcome(bool differedFromProduction)
        {
            if (differedFromProduction) UltraDispatchStats.Overrode++;
            else UltraDispatchStats.Agreed++;
        }
    }
}

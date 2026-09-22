using System;

namespace AutoTest.Ultra
{
    /// <summary>A controller that answers every decision with the first legal action.
    ///
    /// <para><b>Plumbing test, not a policy.</b> Playing the first option is usually bad, and
    /// that is the point: if the dispatch path works, a run with this attached must differ
    /// visibly from the baseline. A smoke sink that agreed with production would prove
    /// nothing — "it worked" and "it never ran" would look identical, which is precisely the
    /// confusion that hid the 2026-08-17 portfolio failure for a full session.</para>
    ///
    /// <para>Deterministic and allocation-free, so it can be attached to a large batch without
    /// perturbing timing or the run seed.</para></summary>
    public sealed class UltraFirstLegalSink : IUltraDecisionSink
    {
        /// <summary>Decision points this sink will answer. Anything else is declined, which
        /// lets a smoke run isolate one boundary at a time.</summary>
        public UltraDecisionPoint[] answerOnly;

        public bool TryDecide(UltraCheckpoint checkpoint, out string committedActionId, out string reason)
        {
            committedActionId = null;
            reason = null;

            if (checkpoint == null) { reason = "no checkpoint"; return false; }
            if (answerOnly != null && answerOnly.Length > 0)
            {
                bool wanted = false;
                for (int i = 0; i < answerOnly.Length; i++)
                    if (answerOnly[i] == checkpoint.point) { wanted = true; break; }
                if (!wanted) { reason = "not this decision point"; return false; }
            }
            if (checkpoint.legalActions == null || checkpoint.legalActions.Length == 0)
            {
                reason = "no legal actions offered";
                return false;
            }

            committedActionId = checkpoint.legalActions[0].ActionId;
            return true;
        }
    }

    /// <summary>Forces one specific action, once, then gets out of the way.
    ///
    /// <para>This is what makes a rollout measure the right quantity. The estimand is
    /// "P(clear | take action A now, <b>then the production policy plays</b>)", so exactly one
    /// decision may be dictated. Holding the controller on for the rest of the run would
    /// measure a different policy entirely and the comparison between candidate actions would
    /// stop meaning anything.</para></summary>
    public sealed class UltraForcedFirstActionSink : IUltraDecisionSink
    {
        private readonly string _actionId;
        private bool _spent;

        public UltraForcedFirstActionSink(string actionId) { _actionId = actionId; }

        /// <summary>Whether the forced action was ever actually committed. A rollout whose
        /// action never fired is measuring the baseline, not the candidate — the caller has to
        /// be able to tell those apart.</summary>
        public bool Fired { get { return _spent; } }

        public bool TryDecide(UltraCheckpoint checkpoint, out string committedActionId, out string reason)
        {
            committedActionId = null;
            reason = null;
            if (_spent) { reason = "forced action already spent"; return false; }
            if (checkpoint?.legalActions == null) { reason = "no legal actions"; return false; }

            for (int i = 0; i < checkpoint.legalActions.Length; i++)
            {
                if (!string.Equals(checkpoint.legalActions[i].ActionId, _actionId,
                        StringComparison.Ordinal)) continue;
                _spent = true;
                committedActionId = _actionId;
                return true;
            }
            // Not on offer here. Stay armed rather than firing at the wrong decision point —
            // committing the action somewhere else would silently measure something else.
            reason = "forced action is not legal at this checkpoint";
            return false;
        }
    }

    /// <summary>A controller that answers with the <em>last</em> legal action.
    ///
    /// <para>Paired with <see cref="UltraFirstLegalSink"/> it distinguishes "the hook runs" from
    /// "the hook runs and the choice actually reaches the game": the two must produce different
    /// runs on the same seed. Identical results would mean the committed action is being
    /// discarded somewhere downstream.</para></summary>
    public sealed class UltraLastLegalSink : IUltraDecisionSink
    {
        public UltraDecisionPoint[] answerOnly;

        public bool TryDecide(UltraCheckpoint checkpoint, out string committedActionId, out string reason)
        {
            committedActionId = null;
            reason = null;

            if (checkpoint == null) { reason = "no checkpoint"; return false; }
            if (answerOnly != null && answerOnly.Length > 0)
            {
                bool wanted = false;
                for (int i = 0; i < answerOnly.Length; i++)
                    if (answerOnly[i] == checkpoint.point) { wanted = true; break; }
                if (!wanted) { reason = "not this decision point"; return false; }
            }
            if (checkpoint.legalActions == null || checkpoint.legalActions.Length == 0)
            {
                reason = "no legal actions offered";
                return false;
            }

            committedActionId = checkpoint.legalActions[checkpoint.legalActions.Length - 1].ActionId;
            return true;
        }
    }
}

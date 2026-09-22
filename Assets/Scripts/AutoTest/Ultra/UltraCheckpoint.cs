using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AutoTest.Ultra
{
    /// <summary>A stable point in a run where control can be handed out and a single action
    /// taken. Ordered as the handoff's Phase B list; each is enabled separately.
    ///
    /// <para><b>Append only.</b> The value is written into the mechanical hash, so renumbering
    /// silently invalidates every stored checkpoint.</para></summary>
    public enum UltraDecisionPoint
    {
        None = 0,
        RunDeclaration = 1,
        MapNavigation = 2,
        RewardChoice = 3,
        EventChoice = 4,
        RestChoice = 5,
        ShopAction = 6,
        CombatTurn = 7,
    }

    /// <summary><b>Append only</b> — same hashing reason as <see cref="UltraDecisionPoint"/>.</summary>
    public enum UltraActionKind
    {
        None = 0,
        DeclareRun = 1,
        MoveTo = 2,
        ChooseReward = 3,
        ChooseEventOption = 4,
        ChooseRest = 5,
        ShopBuy = 6,
        ShopSell = 7,
        ShopLeave = 8,
        CombatWiring = 9,
        CombatReroll = 10,
        CombatRole = 11,
        UseConsumable = 12,
        Pass = 13,
    }

    /// <summary>One action the player may legally take at a checkpoint.
    ///
    /// <para>Identity is <see cref="ActionId"/>, not object reference: the controller runs in
    /// another process, so a committed action arrives as text and has to be matched back to
    /// the issued set by value.</para></summary>
    [Serializable]
    public sealed class UltraLegalAction
    {
        public UltraActionKind kind;
        /// <summary>Node id / item id / empty when the action is identified by index.</summary>
        public string targetId = "";
        /// <summary>Option index for choices that have no stable id. −1 when unused.</summary>
        public int index = -1;
        /// <summary>Diagnostics only. **Never** part of the id or the hash — a label change
        /// must not invalidate a checkpoint.</summary>
        public string label = "";

        public string ActionId
        {
            get
            {
                return ((int)kind).ToString(CultureInfo.InvariantCulture)
                     + ":" + (targetId ?? "")
                     + ":" + index.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static UltraLegalAction Of(UltraActionKind kind, string targetId = "",
                                          int index = -1, string label = "")
        {
            return new UltraLegalAction
            {
                kind = kind,
                targetId = targetId ?? "",
                index = index,
                label = label ?? "",
            };
        }
    }

    /// <summary>What is handed to a controller at a decision point: everything it may see,
    /// everything it may do, and nothing else.
    ///
    /// <para><b>The legal action set is issued, not requested.</b> A controller cannot invent
    /// an action and have it accepted ── <see cref="UltraCheckpointProtocol.TryValidateCommit"/>
    /// matches the committed id against the set that was issued with this exact epoch. That is
    /// what stops a stale or hallucinated action from reaching the run.</para></summary>
    [Serializable]
    public sealed class UltraCheckpoint
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        /// <summary>Strictly increasing within a run. One epoch = one commit opportunity.</summary>
        public int epoch;
        public UltraDecisionPoint point;
        public UltraObservation observation = new UltraObservation();
        public UltraLegalAction[] legalActions = new UltraLegalAction[0];
        /// <summary>SHA-256 over the mechanical content. Recomputed on commit; a mismatch
        /// means the checkpoint was altered in transit and the commit is refused.</summary>
        public string mechanicalHash = "";
    }

    /// <summary>Checkpoint construction, hashing, and the commit gate.</summary>
    public static class UltraCheckpointProtocol
    {
        public const string HashPrefix = "ultra-checkpoint-v1";

        public static UltraCheckpoint Create(
            int epoch, UltraDecisionPoint point,
            UltraObservation observation, IList<UltraLegalAction> legalActions)
        {
            var checkpoint = new UltraCheckpoint
            {
                epoch = epoch,
                point = point,
                observation = observation ?? new UltraObservation(),
                legalActions = Normalize(legalActions),
            };
            checkpoint.mechanicalHash = ComputeMechanicalHash(checkpoint);
            return checkpoint;
        }

        /// <summary>Sorted and de-duplicated so that two captures of the same state produce
        /// byte-identical checkpoints. Enumeration order inside the game must not leak into
        /// the hash, or comparing checkpoints stops meaning anything.</summary>
        private static UltraLegalAction[] Normalize(IList<UltraLegalAction> actions)
        {
            if (actions == null || actions.Count == 0) return new UltraLegalAction[0];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<UltraLegalAction>(actions.Count);
            for (int i = 0; i < actions.Count; i++)
            {
                UltraLegalAction action = actions[i];
                if (action == null) continue;
                if (!seen.Add(action.ActionId)) continue;
                kept.Add(action);
            }
            kept.Sort((a, b) => string.CompareOrdinal(a.ActionId, b.ActionId));
            return kept.ToArray();
        }

        /// <summary>Hash of everything mechanically meaningful.
        ///
        /// <para><b>Labels are excluded on purpose.</b> They exist for humans reading logs;
        /// including them would make a copy-edit invalidate stored checkpoints, and the first
        /// person to hit that would be tempted to loosen the check instead.</para></summary>
        public static string ComputeMechanicalHash(UltraCheckpoint checkpoint)
        {
            if (checkpoint == null) return "";
            var sb = new StringBuilder(1024);
            sb.Append(HashPrefix).Append('\n')
              .Append(checkpoint.version).Append('\n')
              .Append(checkpoint.epoch).Append('\n')
              .Append((int)checkpoint.point).Append('\n');

            UltraLegalAction[] actions = checkpoint.legalActions ?? new UltraLegalAction[0];
            sb.Append(actions.Length).Append('\n');
            for (int i = 0; i < actions.Length; i++)
                sb.Append(actions[i].ActionId).Append('\n');

            // The observation is hashed through its serialized form so that adding a field to
            // the observation automatically changes the hash. A hand-written field list here
            // would silently stop covering new fields — the exact failure mode that made the
            // profile fingerprint useless (see the portfolio profile v1 → v2 change).
            sb.Append(UnityEngine.JsonUtility.ToJson(
                checkpoint.observation ?? new UltraObservation())).Append('\n');

            return UltraPortfolioProtocol.Sha256Text(sb.ToString());
        }

        /// <summary>Reasons a commit is refused. Text is compared in tests, so treat these as
        /// part of the contract rather than as log strings.</summary>
        public const string FailureStaleEpoch = "stale_epoch";
        public const string FailureStalePoint = "stale_decision_point";
        public const string FailureIllegalAction = "illegal_action";
        public const string FailureTampered = "checkpoint_hash_mismatch";
        public const string FailureMissing = "checkpoint_missing";

        /// <summary>The commit gate. Everything a controller returns passes through here.
        ///
        /// <para>Checked in order: the checkpoint is intact, the run is still at the epoch and
        /// decision point that were issued, and the action is one that was offered. A failure
        /// is <b>fail-closed</b> ── the caller falls back to the production policy rather than
        /// committing something unverified.</para></summary>
        public static bool TryValidateCommit(
            UltraCheckpoint issued,
            int currentEpoch,
            UltraDecisionPoint currentPoint,
            string committedActionId,
            out string failure)
        {
            failure = null;
            if (issued == null) { failure = FailureMissing; return false; }

            if (!string.Equals(issued.mechanicalHash, ComputeMechanicalHash(issued),
                    StringComparison.Ordinal))
            { failure = FailureTampered; return false; }

            if (issued.epoch != currentEpoch) { failure = FailureStaleEpoch; return false; }
            if (issued.point != currentPoint) { failure = FailureStalePoint; return false; }

            UltraLegalAction[] actions = issued.legalActions ?? new UltraLegalAction[0];
            for (int i = 0; i < actions.Length; i++)
                if (string.Equals(actions[i].ActionId, committedActionId, StringComparison.Ordinal))
                    return true;

            failure = FailureIllegalAction;
            return false;
        }

        /// <summary>Legal moves at the map-navigation boundary, taken from the observation
        /// rather than from the live map.
        ///
        /// <para><b>This is why it reads the observation.</b> The map itself knows the type of
        /// unrevealed tiles; <c>AutoRunner.DoNavigate</c> reads <c>EffectiveType</c> directly
        /// and is therefore unusable for Ultra continuation (handoff §10 Phase A). Deriving the
        /// action set from the observation means the boundary cannot offer a move that was
        /// chosen using information the player does not have.</para></summary>
        /// <summary>Legal actions for a discrete choice, taken from the observation's own
        /// option list.
        ///
        /// <para><b>Unavailable options are not offered.</b> They stay visible in the
        /// observation (the player can see the greyed-out button and its price) but they are
        /// not actions, because committing one would be refused by the game and the run would
        /// silently fall back — an outcome indistinguishable from the controller declining.</para></summary>
        public static List<UltraLegalAction> ChoiceActions(
            UltraObservation observation, UltraActionKind kind)
        {
            var actions = new List<UltraLegalAction>();
            UltraChoiceOption[] options = observation?.choice?.options;
            if (options == null) return actions;

            for (int i = 0; i < options.Length; i++)
            {
                UltraChoiceOption option = options[i];
                if (option == null || !option.available) continue;
                actions.Add(UltraLegalAction.Of(kind, option.id ?? "", option.index, option.label));
            }
            return actions;
        }

        /// <summary>Finds which option an accepted action id refers to. Returns −1 when it
        /// matches nothing, which the caller must treat as a fallback rather than as index 0.</summary>
        public static int ResolveChoiceIndex(
            UltraObservation observation, UltraActionKind kind, string actionId)
        {
            UltraChoiceOption[] options = observation?.choice?.options;
            if (options == null || string.IsNullOrEmpty(actionId)) return -1;

            for (int i = 0; i < options.Length; i++)
            {
                UltraChoiceOption option = options[i];
                if (option == null || !option.available) continue;
                string candidate = UltraLegalAction.Of(kind, option.id ?? "", option.index).ActionId;
                if (string.Equals(candidate, actionId, StringComparison.Ordinal)) return option.index;
            }
            return -1;
        }

        public static List<UltraLegalAction> MapNavigationActions(UltraObservation observation)
        {
            var actions = new List<UltraLegalAction>();
            if (observation?.map?.nodes == null) return actions;

            UltraMapNodeView[] nodes = observation.map.nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                UltraMapNodeView node = nodes[i];
                if (node == null || !node.reachableNow) continue;
                actions.Add(UltraLegalAction.Of(
                    UltraActionKind.MoveTo, node.id, -1,
                    node.view.ToString() + "@r" + node.row));
            }
            return actions;
        }
    }
}

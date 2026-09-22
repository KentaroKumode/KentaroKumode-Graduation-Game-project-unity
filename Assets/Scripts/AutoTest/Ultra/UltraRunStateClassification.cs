using System;
using System.Collections.Generic;
using System.Reflection;

namespace AutoTest.Ultra
{
    /// <summary>Which <see cref="GameLoop.RunState"/> fields the player cannot see.
    ///
    /// <para><b>One list, two consumers.</b> The observation must never surface these, and the
    /// resume snapshot must neutralise them before a rollout runs over it. Keeping the
    /// classification in one place is the whole point: when the two drifted apart the
    /// observation stayed airtight while the same facts flowed through the evaluation channel
    /// instead.</para>
    ///
    /// <para>Nothing currently listed is exploitable — the pool counts are derivable from the
    /// player's own inventory, and the rest is instrumentation. The list exists so that the
    /// <em>next</em> genuinely hidden field cannot slip through unnoticed.</para></summary>
    public static class UltraRunStateClassification
    {
        private static readonly Dictionary<string, string> HiddenFields =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "lambdaPoolRemainingAll",
              "undrawn Λ pool size — derivable from owned items, but not shown, so treat as hidden" },
            { "lambdaPoolRemainingFloored",
              "same pool with the depth rarity floor applied" },
            { "chronicle",
              "diagnostic log, not a game-visible quantity" },
            { "judgmentHealRequested", "断罪 accounting instrumentation" },
            { "judgmentHealActual", "断罪 accounting instrumentation" },
            { "judgmentHealPrevented", "断罪 accounting instrumentation" },
            { "judgmentPostDamage", "断罪 accounting instrumentation" },
        };

        public static IEnumerable<KeyValuePair<string, string>> Hidden { get { return HiddenFields; } }

        public static bool IsHidden(string fieldName)
        {
            return fieldName != null && HiddenFields.ContainsKey(fieldName);
        }

        public static bool TryGetReason(string fieldName, out string reason)
        {
            reason = null;
            return fieldName != null && HiddenFields.TryGetValue(fieldName, out reason);
        }

        /// <summary>Every public instance field of <c>RunState</c>. Used by the coverage tests
        /// so a newly added field has to be classified before it ships.</summary>
        public static IEnumerable<string> AllRunStateFieldNames()
        {
            FieldInfo[] fields = typeof(GameLoop.RunState)
                .GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++) yield return fields[i].Name;
        }

        /// <summary>Names listed as hidden that no longer exist. Catches the reverse drift, in
        /// which the list keeps growing stale entries and quietly stops covering anything.</summary>
        public static List<string> StaleHiddenNames()
        {
            var live = new HashSet<string>(AllRunStateFieldNames(), StringComparer.Ordinal);
            var stale = new List<string>();
            foreach (var pair in HiddenFields) if (!live.Contains(pair.Key)) stale.Add(pair.Key);
            stale.Sort(StringComparer.Ordinal);
            return stale;
        }
    }
}

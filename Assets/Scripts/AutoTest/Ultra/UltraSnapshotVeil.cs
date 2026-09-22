using System;
using System.Collections.Generic;
using MapSystem;

namespace AutoTest.Ultra
{
    /// <summary>What a veil pass changed, and what it knowingly did not.</summary>
    [Serializable]
    public sealed class UltraVeilReport
    {
        /// <summary>Shop tiles whose false-merchant flag was re-drawn.</summary>
        public int shopsRedrawn;
        public int falseMerchantsBefore;
        public int falseMerchantsAfter;
        /// <summary>Unrevealed tiles whose type was re-drawn from the generator's weights.</summary>
        public int unrevealedTilesRedrawn;
        /// <summary>Unrevealed tiles deliberately left alone: structural rows (outpost /
        /// guaranteed shop / rest / boss) and tiles the player has already stepped on. Not a
        /// leak — the player knows these regardless of fog.</summary>
        public int unrevealedTilesLeftIntact;
        /// <summary><b>Known leak.</b> Mystery tiles already resolved on the board but not yet
        /// stepped on by the player.</summary>
        public int unsteppedResolvedMysteries;
        /// <summary>RunState fields classified hidden that were reset before the rollout.</summary>
        public int hiddenRunFieldsNeutralised;

        /// <summary>Whether hidden truth may still be reachable through a rollout over this
        /// board. Structural tiles left intact do not count — the player knows those.</summary>
        public bool HasKnownLeak
        {
            get { return unsteppedResolvedMysteries > 0; }
        }

        public string Describe()
        {
            return string.Format(
                "veil: 商店 {0} 件再抽選 (偽 {1}→{2}) / 未公開タイル {3} 件再抽選・{4} 件は構造上既知"
                + (unsteppedResolvedMysteries > 0
                    ? " / **未veil: 未踏の解決済み? {5} 件**" : " / 未veil なし"),
                shopsRedrawn, falseMerchantsBefore, falseMerchantsAfter,
                unrevealedTilesRedrawn, unrevealedTilesLeftIntact, unsteppedResolvedMysteries);
        }
    }

    /// <summary>Re-randomises facts the player cannot see, before a snapshot is rolled out.
    ///
    /// <para><b>Why this has to exist.</b> A rollout evaluator asks "what happens if I take
    /// this action?" and reads the answer. If the board it rolls out on still contains the
    /// hidden truth, the answer encodes that truth: walking into the fake merchant comes back
    /// as a loss, so the controller learns the shop is fake <em>without ever being told</em>.
    /// The observation boundary would still look airtight — every field it exposes is public —
    /// while hidden information flows through the evaluation channel instead. Veiling makes
    /// the rollout sample the player's belief rather than the truth.</para>
    ///
    /// <para><b>What is not veiled is counted, not hidden.</b> Unrevealed tile types would
    /// need the generator's per-floor distribution to re-draw honestly; until that exists the
    /// leak is real, so <see cref="UltraVeilReport"/> reports it and callers can refuse to
    /// trust a rollout taken over a leaky board.</para></summary>
    public static class UltraSnapshotVeil
    {
        /// <summary>Re-draw the hidden facts on <paramref name="snapshot"/> in place.</summary>
        /// <param name="seed">Worker-local seed. Never the live run's.</param>
        /// <param name="falseMerchantChance">Probability the generator uses, 0 to disable.</param>
        public static UltraVeilReport Apply(
            UltraMapSnapshot snapshot, int seed, float falseMerchantChance)
        {
            var report = new UltraVeilReport();
            if (snapshot?.nodes == null) return report;

            var random = new System.Random(seed);

            for (int i = 0; i < snapshot.nodes.Length; i++)
            {
                UltraMapNodeSnapshot node = snapshot.nodes[i];
                if (node == null) continue;

                if (node.isFalseMerchant) report.falseMerchantsBefore++;

                bool isShop = EffectiveType(node) == (int)TileType.Shop;
                // Only unvisited shops are re-drawn: once the player has stepped on one they
                // know what it was, and re-drawing would erase knowledge they legitimately have.
                if (isShop && !node.visited && snapshot.floor >= 3)
                {
                    node.isFalseMerchant = falseMerchantChance > 0f
                        && random.NextDouble() < falseMerchantChance;
                    report.shopsRedrawn++;
                }

                if (node.isFalseMerchant) report.falseMerchantsAfter++;

                if (node.type == (int)TileType.Mystery && node.resolvedType >= 0 && !node.visited)
                    report.unsteppedResolvedMysteries++;
            }

            RedrawUnrevealed(snapshot, random, report);
            return report;
        }

        /// <summary>Re-draw the type of tiles the player has not seen.
        ///
        /// <para>Only rows the generator actually randomises are touched. The outpost, the
        /// guaranteed shop row, the rest row and the boss are structural: the player knows what
        /// is there whether or not fog is covering it, so re-drawing them would <em>remove</em>
        /// knowledge rather than protect it.</para>
        ///
        /// <para>The weights come from <see cref="MapGenerator.RandomRowTileWeights"/> rather
        /// than a copy. A second table here would drift the moment the generator is tuned, and
        /// the veil would start sampling a distribution the board was never built from.</para></summary>
        private static void RedrawUnrevealed(
            UltraMapSnapshot snapshot, System.Random random, UltraVeilReport report)
        {
            IReadOnlyList<(TileType type, float weight)> weights =
                MapGenerator.RandomRowTileWeights(snapshot.floor);
            if (weights == null || weights.Count == 0)
            {
                for (int i = 0; i < snapshot.nodes.Length; i++)
                    if (snapshot.nodes[i] != null && !snapshot.nodes[i].revealed)
                        report.unrevealedTilesLeftIntact++;
                return;
            }

            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += weights[i].weight;
            int lastRandomRow = MapGenerator.LastRandomRow(snapshot.floor);

            for (int i = 0; i < snapshot.nodes.Length; i++)
            {
                UltraMapNodeSnapshot node = snapshot.nodes[i];
                if (node == null || node.revealed) continue;

                // Visited tiles are known even if fog later covered them again.
                if (node.visited || node.row < 1 || node.row > lastRandomRow)
                {
                    report.unrevealedTilesLeftIntact++;
                    continue;
                }

                double roll = random.NextDouble() * total;
                TileType drawn = weights[weights.Count - 1].type;
                for (int w = 0; w < weights.Count; w++)
                {
                    roll -= weights[w].weight;
                    if (roll <= 0) { drawn = weights[w].type; break; }
                }
                node.type = (int)drawn;
                node.resolvedType = -1;
                node.isFalseMerchant = drawn == TileType.Shop
                    && snapshot.floor >= 3
                    && random.NextDouble() < CurrentFalseMerchantChance();
                report.unrevealedTilesRedrawn++;
            }
        }

        private static int EffectiveType(UltraMapNodeSnapshot node)
        {
            return node.resolvedType >= 0 ? node.resolvedType : node.type;
        }

        /// <summary>Neutralise the run fields the player cannot see, before the snapshot is
        /// rolled out.
        ///
        /// <para><b>The map is not the only place hidden state lives.</b> The run snapshot
        /// captures every <c>RunState</c> field — it has to, or a restore would be a different
        /// run — which means anything classified hidden would otherwise ride straight into the
        /// rollout. The map veil closing its leak while this one stayed open is exactly the
        /// shape of failure worth guarding against.</para>
        ///
        /// <para>Values are reset to what the game itself uses as "unknown / not yet computed"
        /// rather than to zero, so a restored run recomputes them the same way a fresh one
        /// does.</para></summary>
        public static int ApplyToRun(UltraRunSnapshot snapshot)
        {
            if (snapshot?.fields == null) return 0;

            int neutralised = 0;
            for (int i = 0; i < snapshot.fields.Length; i++)
            {
                UltraSnapshotField field = snapshot.fields[i];
                if (field == null || !UltraRunStateClassification.IsHidden(field.name)) continue;
                field.value = NeutralValue(field.name, field.kind);
                neutralised++;
            }
            return neutralised;
        }

        private static string NeutralValue(string name, string kind)
        {
            // -1 is the game's own "not yet counted" marker for the Λ pool (RunState resets it
            // to -1), so the restored run recomputes rather than trusting a stale number.
            if (name == "lambdaPoolRemainingAll" || name == "lambdaPoolRemainingFloored")
                return "-1";

            switch (kind)
            {
                case "int":
                case "long":
                case "enum": return "0";
                case "float": return "0";
                case "bool": return "0";
                case "string": return "";
                case "list<string>":
                case "set<string>":
                case "list<obj>": return UnityEngine.JsonUtility.ToJson(new UltraStringArray());
                case "list<int>":
                case "list<enum>": return UnityEngine.JsonUtility.ToJson(new UltraIntArray());
                case "map<string,int>":
                    return UnityEngine.JsonUtility.ToJson(new UltraStringArray())
                         + "\n" + UnityEngine.JsonUtility.ToJson(new UltraIntArray());
                default:
                    throw new NotSupportedException(
                        "no neutral value defined for hidden field " + name + " of kind " + kind);
            }
        }

        /// <summary>The generator's current false-merchant probability, so the veil re-draws
        /// from the same distribution the board was built with instead of a second constant.</summary>
        public static float CurrentFalseMerchantChance()
        {
            return MetaProgression.MetaDebuffApplicator.GetFalseMerchantChance();
        }
    }

    /// <summary>Aggregate veil accounting for a batch.</summary>
    public static class UltraVeilStats
    {
        public static long Applied;
        public static long WithKnownLeak;
        public static long ShopsRedrawn;
        public static long TilesRedrawn;

        public static void Reset() { Applied = WithKnownLeak = ShopsRedrawn = TilesRedrawn = 0; }

        public static void Note(UltraVeilReport report)
        {
            if (report == null) return;
            Applied++;
            if (report.HasKnownLeak) WithKnownLeak++;
            ShopsRedrawn += report.shopsRedrawn;
            TilesRedrawn += report.unrevealedTilesRedrawn;
        }

        public static string Describe()
        {
            if (Applied <= 0) return "veil: 適用なし";
            return string.Format(
                "veil: {0}回 / 既知の漏れあり {1} ({2:F1}%) / 商店再抽選 {3} / 未公開タイル再抽選 {4}",
                Applied, WithKnownLeak, 100.0 * WithKnownLeak / Applied, ShopsRedrawn, TilesRedrawn);
        }
    }
}

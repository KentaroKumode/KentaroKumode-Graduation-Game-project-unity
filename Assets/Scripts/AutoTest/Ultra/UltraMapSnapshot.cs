using System;
using System.Collections.Generic;
using MapSystem;

namespace AutoTest.Ultra
{
    /// <summary>One node, captured as ground truth.
    ///
    /// <para><b>This carries <c>isFalseMerchant</c>, and <see cref="UltraMapNodeView"/> must
    /// not.</b> They look like the same data and obey opposite rules: a snapshot exists so a
    /// worker can replay the identical board, and dropping the flag would make the replayed
    /// shop genuine — a different game. An observation exists so a controller can decide
    /// without seeing what the player cannot, and carrying the flag would hand it the
    /// answer.</para></summary>
    [Serializable]
    public sealed class UltraMapNodeSnapshot
    {
        public string id = "";
        public int row;
        public int lane;
        public int type;
        /// <summary>Resolved Mystery type, or −1 when unresolved. −1 is meaningful: it says
        /// the tile has not been stepped on yet.</summary>
        public int resolvedType = -1;
        public bool visited;
        public bool activated;
        public bool isFalseMerchant;
        public bool revealed = true;
        public string[] connections = new string[0];
    }

    /// <summary>A floor's map, restorable into a fresh <see cref="MapManager"/>.</summary>
    [Serializable]
    public sealed class UltraMapSnapshot
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public int floor;
        public int laneCount;
        public int rowCount;
        public string startNodeId = "";
        public string bossNodeId = "";
        public string currentNodeId = "";
        public UltraMapNodeSnapshot[] nodes = new UltraMapNodeSnapshot[0];

        /// <summary>Hunger lives on <see cref="MapManager"/> rather than in the run, so it
        /// would be lost if the map snapshot did not carry it.</summary>
        public int hungerCurrent;
        public int hungerMax;

        public static UltraMapSnapshot Capture(MapManager manager)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            FloorMap map = manager.CurrentMap;
            if (map == null) throw new InvalidOperationException("MapManager has no current map");

            var snapshot = new UltraMapSnapshot
            {
                floor = map.floor,
                laneCount = map.laneCount,
                rowCount = map.rowCount,
                startNodeId = map.startNodeId ?? "",
                bossNodeId = map.bossNodeId ?? "",
                currentNodeId = manager.CurrentNode?.id ?? "",
                hungerCurrent = manager.Hunger != null ? manager.Hunger.Current : 0,
                hungerMax = manager.Hunger != null ? manager.Hunger.Max : 0,
            };

            List<MapNode> all = map.GetAllNodes();
            // Sorted by id so the same board always produces the same bytes — dictionary
            // enumeration order is not a property of the map.
            all.Sort((a, b) => string.CompareOrdinal(a?.id, b?.id));

            var captured = new List<UltraMapNodeSnapshot>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                MapNode node = all[i];
                if (node == null) continue;
                var connections = node.connections != null
                    ? node.connections.ToArray() : new string[0];
                Array.Sort(connections, StringComparer.Ordinal);
                captured.Add(new UltraMapNodeSnapshot
                {
                    id = node.id,
                    row = node.row,
                    lane = node.lane,
                    type = (int)node.type,
                    resolvedType = node.resolvedType.HasValue ? (int)node.resolvedType.Value : -1,
                    visited = node.visited,
                    activated = node.activated,
                    isFalseMerchant = node.isFalseMerchant,
                    revealed = node.revealed,
                    connections = connections,
                });
            }
            snapshot.nodes = captured.ToArray();
            return snapshot;
        }

        /// <summary>Rebuild the <see cref="FloorMap"/> this snapshot describes.</summary>
        public FloorMap BuildMap()
        {
            if (version != CurrentVersion)
                throw new InvalidOperationException(
                    "map snapshot version " + version + " != " + CurrentVersion);

            var map = new FloorMap
            {
                floor = floor,
                laneCount = laneCount,
                rowCount = rowCount,
                startNodeId = startNodeId ?? "",
                bossNodeId = bossNodeId ?? "",
            };
            for (int i = 0; i < (nodes?.Length ?? 0); i++)
            {
                UltraMapNodeSnapshot entry = nodes[i];
                if (entry == null) continue;
                var node = new MapNode(entry.id, entry.row, entry.lane, (TileType)entry.type)
                {
                    resolvedType = entry.resolvedType >= 0
                        ? (TileType?)(TileType)entry.resolvedType : null,
                    visited = entry.visited,
                    activated = entry.activated,
                    isFalseMerchant = entry.isFalseMerchant,
                    revealed = entry.revealed,
                };
                node.connections.Clear();
                if (entry.connections != null)
                    for (int c = 0; c < entry.connections.Length; c++)
                        node.connections.Add(entry.connections[c]);
                map.AddNode(node);
            }
            return map;
        }

        /// <summary>Content hash, for checking a restore against its capture.</summary>
        public string ContentHash()
        {
            var sb = new System.Text.StringBuilder(2048);
            const char sep = (char)0x1F;
            sb.Append("ultra-map-snapshot-v").Append(version).Append('\n')
              .Append(floor).Append(sep).Append(laneCount).Append(sep).Append(rowCount).Append(sep)
              .Append(startNodeId ?? "").Append(sep).Append(bossNodeId ?? "").Append(sep)
              .Append(currentNodeId ?? "").Append(sep)
              .Append(hungerCurrent).Append(sep).Append(hungerMax).Append('\n');

            for (int i = 0; i < (nodes?.Length ?? 0); i++)
            {
                UltraMapNodeSnapshot n = nodes[i];
                if (n == null) continue;
                sb.Append(n.id).Append(sep).Append(n.row).Append(sep).Append(n.lane).Append(sep)
                  .Append(n.type).Append(sep).Append(n.resolvedType).Append(sep)
                  .Append(n.visited ? '1' : '0').Append(n.activated ? '1' : '0')
                  .Append(n.isFalseMerchant ? '1' : '0').Append(n.revealed ? '1' : '0').Append(sep);
                if (n.connections != null)
                    for (int c = 0; c < n.connections.Length; c++)
                        sb.Append(n.connections[c]).Append(',');
                sb.Append('\n');
            }
            return UltraPortfolioProtocol.Sha256Text(sb.ToString());
        }
    }
}

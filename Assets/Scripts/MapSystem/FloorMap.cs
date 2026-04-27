using System.Collections.Generic;
using System.Linq;

namespace MapSystem
{
    /// <summary>1フロア分のマップデータ（ノード群＋接続情報）</summary>
    public class FloorMap
    {
        public int floor;
        public int laneCount;
        public int rowCount;       // Row 0(前哨基地) 〜 Row rowCount-1(休憩)、Boss は rowCount
        public string startNodeId;
        public string bossNodeId;

        private Dictionary<string, MapNode> nodes = new Dictionary<string, MapNode>();

        public void AddNode(MapNode node)
        {
            nodes[node.id] = node;
        }

        /// <summary>接続を追加（デフォルトは前方向のみ、bidirectional=true で双方向）</summary>
        public void AddConnection(string fromId, string toId, bool bidirectional = false)
        {
            if (!nodes.ContainsKey(fromId) || !nodes.ContainsKey(toId)) return;

            if (!nodes[fromId].connections.Contains(toId))
                nodes[fromId].connections.Add(toId);

            if (bidirectional && !nodes[toId].connections.Contains(fromId))
                nodes[toId].connections.Add(fromId);
        }

        public MapNode GetNode(string id)
        {
            nodes.TryGetValue(id, out var node);
            return node;
        }

        public IReadOnlyDictionary<string, MapNode> AllNodes => nodes;
        public List<MapNode> GetAllNodes() => nodes.Values.ToList();

        public List<MapNode> GetNodesAtRow(int row) =>
            nodes.Values.Where(n => n.row == row).OrderBy(n => n.lane).ToList();

        /// <summary>指定ノードから移動可能な全ノード</summary>
        public List<MapNode> GetReachableFrom(string nodeId)
        {
            if (!nodes.ContainsKey(nodeId)) return new List<MapNode>();
            return nodes[nodeId].connections
                .Where(id => nodes.ContainsKey(id))
                .Select(id => nodes[id])
                .ToList();
        }

        /// <summary>到達先を前進（斜め含む）と横移動に分類</summary>
        public (List<MapNode> forward, List<MapNode> lateral) CategorizeMovesFrom(string nodeId)
        {
            var current = GetNode(nodeId);
            if (current == null) return (new List<MapNode>(), new List<MapNode>());

            var reachable = GetReachableFrom(nodeId);
            var forward = reachable.Where(n => n.row > current.row).ToList();
            var lateral = reachable.Where(n => n.row == current.row).ToList();
            return (forward, lateral);
        }

        public int MaxRow => nodes.Count > 0 ? nodes.Values.Max(n => n.row) : 0;
    }
}

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

        /// <summary>自由移動の層の形 (点・道・山岳・縄張り)。 Λ 環状線・8 層 (ボス戦だけ) では null。</summary>
        public FreeMove.FreeMapLayout layout;
        public bool IsFreeMap => layout != null;

        private readonly List<string> _idByIndex = new List<string>();

        private Dictionary<string, MapNode> nodes = new Dictionary<string, MapNode>();

        public void AddNode(MapNode node)
        {
            nodes[node.id] = node;
            if (node.index >= 0)
            {
                while (_idByIndex.Count <= node.index) _idByIndex.Add(null);
                _idByIndex[node.index] = node.id;
            }
        }

        /// <summary>自由移動の層の点の添字 → ノード ID。</summary>
        public string NodeIdAt(int index)
            => index >= 0 && index < _idByIndex.Count ? _idByIndex[index] : null;

        public MapNode GetNodeByIndex(int index)
        {
            string id = NodeIdAt(index);
            return id != null ? GetNode(id) : null;
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

        // 2026-09-28: CategorizeMovesFrom（前進 / 横移動の分類）は 3 列マップと一緒に削除した。
        //   自由移動には「前」も「横」も無い (docs/GAME.md §24)。

        public int MaxRow => nodes.Count > 0 ? nodes.Values.Max(n => n.row) : 0;
    }
}

using System.Collections.Generic;

namespace MapSystem
{
    /// <summary>マップ上の1マス</summary>
    public class MapNode
    {
        public string id;
        public int row;
        public int lane;           // 0-2 for normal lanes, -1 for convergence nodes (outpost/boss)
        public TileType type;
        public TileType? resolvedType; // Mystery解決後のタイプ
        public bool visited;
        public bool activated;         // タイル効果を一度起動済みか（再訪時の再発火を防ぐ）
        public bool isFalseMerchant;   // メタデバフ Lv5: このShopマスは偽の商人（踏むと特殊エリート戦）
        public bool revealed = true;   // 難易度0では全可視

        /// <summary>このノードから移動可能なノードのID一覧</summary>
        public List<string> connections = new List<string>();

        public MapNode(string id, int row, int lane, TileType type)
        {
            this.id = id;
            this.row = row;
            this.lane = lane;
            this.type = type;
        }

        /// <summary>実効タイルタイプ（Mystery解決済みなら解決後のタイプ）</summary>
        public TileType EffectiveType => resolvedType ?? type;
    }
}

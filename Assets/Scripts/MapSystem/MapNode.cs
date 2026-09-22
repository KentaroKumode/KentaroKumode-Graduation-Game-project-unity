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

        /// <summary>このマスに割り当てられた戦闘プリセットの ID (<c>GameLoop.EncounterPresets</c>)。
        /// null = 未解決。 戦闘系タイル以外では常に null。
        /// **解決はノード添字で決まるので順序非依存** ── 開示時に解いても踏んだ時に解いても同じ。</summary>
        public string encounterId;

        /// <summary>プリセットの敵。 通常抽選 (<c>enc_generic</c>) の場合もここに焼き付ける。
        /// 「踏むまで中身が無い」状態だと 1 マス手前で名前を出せないため。</summary>
        public string encounterEnemyA;
        public string encounterEnemyB;   // null = 単体戦

        /// <summary>戦闘名が開示済みか。 **1 マス手前のノードへ到着した時点**で立つ。
        /// 遠くからは見えないので、 危険なプリセットを何手も前から迂回することはできない。</summary>
        public bool encounterRevealed;

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

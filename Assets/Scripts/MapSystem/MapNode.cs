using System.Collections.Generic;

namespace MapSystem
{
    /// <summary>マップ上の1マス</summary>
    public class MapNode
    {
        public string id;
        public int row;            // 離散マップ (Λ) の行。 自由移動の層では x 座標の整数部 (表示コードの互換用)
        public int lane;           // 離散マップ (Λ) の列。 -1 は収束ノード。 自由移動の層では点の添字 (表示コードの互換用)
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

        // ── 自由移動のマップ (2026-09-28) ──
        /// <summary>自由移動の層の点か。 false は Λ 環状線・8 層 (ボス戦だけ) の離散マップ。</summary>
        public bool freeMap;
        /// <summary>自由移動の層での添字 (<c>FreeMapLayout</c> の点の番号)。 離散マップでは -1。</summary>
        public int index = -1;
        /// <summary>座標 (距離の単位。 点どうしの標準の間隔 ≒ 1)。 離散マップでは 0。</summary>
        public float x, y;
        /// <summary>6 層の「裂け目の記録」の固定の点。</summary>
        public bool isFixedEvent;

        /// <summary>このノードから移動可能なノードのID一覧。
        /// 自由移動の層では<b>道で結ばれた点</b>（双方向）── 移動そのものはどの点へも直接できる。</summary>
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

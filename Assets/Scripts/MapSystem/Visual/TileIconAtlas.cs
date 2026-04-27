using UnityEngine;
using System.Collections.Generic;

namespace MapSystem.Visual
{
    /// <summary>
    /// 4×4 のアイコンアトラステクスチャから TileType ごとの Sprite を切り出す。
    /// 
    /// 使い方:
    ///   1. 正方形テクスチャ（例: 512×512）を用意し、4×4 に区切って各セルにアイコンを描く
    ///   2. テクスチャの Import Settings → Read/Write を有効にする
    ///   3. ScriptableObject を作成: Assets > Create > MapSystem > TileIconAtlas
    ///   4. atlasTexture にテクスチャをアサイン
    ///
    /// デフォルトのグリッド配置 (左上が row=0, col=0):
    ///   行0: Battle | EliteBattle | Shop  | Outpost
    ///   行1: Trap   | Treasure    | Boss  | Mystery
    ///   行2: Event  | Rest        | (空)  | (空)
    ///   行3: (空×4)
    /// </summary>
    [CreateAssetMenu(fileName = "TileIconAtlas", menuName = "MapSystem/TileIconAtlas")]
    public class TileIconAtlas : ScriptableObject
    {
        [Header("アトラステクスチャ (4×4 グリッド)")]
        [Tooltip("正方形推奨。Import Settings で Read/Write Enabled を ON にしてください。")]
        public Texture2D atlasTexture;

        [Header("グリッドサイズ")]
        [Tooltip("通常は 4×4 のまま。拡張する場合のみ変更")]
        public int columns = 4;
        public int rows    = 4;

        [Header("タイル配置 (グリッド座標)")]
        [Tooltip("左上が (0,0)。配列順は TileType enum 順。Inspectorで並び替え可能")]
        [SerializeField] private TileSlot[] slots = new TileSlot[]
        {
            // 行0
            new TileSlot(TileType.Battle,      0, 0),
            new TileSlot(TileType.EliteBattle, 0, 1),
            new TileSlot(TileType.Shop,        0, 2),
            new TileSlot(TileType.Outpost,     0, 3),
            // 行1
            new TileSlot(TileType.Trap,        1, 0),
            new TileSlot(TileType.Treasure,    1, 1),
            new TileSlot(TileType.Boss,        1, 2),
            new TileSlot(TileType.Mystery,     1, 3),
            // 行2
            new TileSlot(TileType.Event,       2, 0),
            new TileSlot(TileType.Rest,        2, 1),
        };

        // ランタイムキャッシュ
        private Dictionary<TileType, Sprite> spriteCache;

        /// <summary>指定 TileType の Sprite を返す。テクスチャ未設定やスロット未登録なら null。</summary>
        public Sprite GetSprite(TileType type)
        {
            if (atlasTexture == null) return null;
            BuildCacheIfNeeded();
            spriteCache.TryGetValue(type, out var sprite);
            return sprite;
        }

        /// <summary>キャッシュを破棄（テクスチャ差し替え時などに呼ぶ）</summary>
        public void InvalidateCache()
        {
            spriteCache = null;
        }

        private void BuildCacheIfNeeded()
        {
            if (spriteCache != null) return;
            spriteCache = new Dictionary<TileType, Sprite>();

            if (atlasTexture == null || slots == null) return;

            float cellW = atlasTexture.width  / (float)columns;
            float cellH = atlasTexture.height / (float)rows;

            foreach (var slot in slots)
            {
                // Unity テクスチャ座標は左下原点なので Y を反転
                float x = slot.col * cellW;
                float y = (rows - 1 - slot.row) * cellH;

                var rect = new Rect(x, y, cellW, cellH);
                var pivot = new Vector2(0.5f, 0.5f);
                float ppu = Mathf.Max(cellW, cellH); // 1セル = 1Unit

                var sprite = Sprite.Create(
                    atlasTexture,
                    rect,
                    pivot,
                    ppu
                );
                sprite.name = $"TileIcon_{slot.type}";

                spriteCache[slot.type] = sprite;
            }
        }

        private void OnValidate()
        {
            // Inspector で値を変更したらキャッシュクリア
            spriteCache = null;
        }

        [System.Serializable]
        public struct TileSlot
        {
            public TileType type;
            [Tooltip("行番号 (0 = 最上段)")]
            public int row;
            [Tooltip("列番号 (0 = 最左)")]
            public int col;

            public TileSlot(TileType type, int row, int col)
            {
                this.type = type;
                this.row = row;
                this.col = col;
            }
        }
    }
}

using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// インジケーター用テクスチャを動的生成するユーティリティ
    /// </summary>
    public static class TextureGenerator
    {
        /// <summary>
        /// 緑色の配置可能インジケーターテクスチャを作成
        /// </summary>
        public static Texture2D CreateValidPlacementTexture()
        {
            int size = 18;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, // ピクセルパーフェクト
                wrapMode = TextureWrapMode.Clamp
            };
            
            Color validColor = new Color(0.2f, 0.9f, 0.2f, 0.9f); // 鮮やかな緑
            Color edgeColor = new Color(0.1f, 0.4f, 0.1f, 1f); // 濃い緑の枠
            
            // 18x18ピクセルの円形パターンを手動定義
            bool[,] circlePattern = new bool[18, 18]
            {
                {false,false,false,false,false,false,true,true,true,true,true,true,false,false,false,false,false,false},
                {false,false,false,false,true,true,true,true,true,true,true,true,true,true,false,false,false,false},
                {false,false,false,true,true,true,true,true,true,true,true,true,true,true,true,false,false,false},
                {false,false,true,true,true,true,true,true,true,true,true,true,true,true,true,true,false,false},
                {false,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,false},
                {false,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,false},
                {true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true},
                {true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true},
                {true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true},
                {true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true},
                {true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true},
                {true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true},
                {false,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,false},
                {false,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,false},
                {false,false,true,true,true,true,true,true,true,true,true,true,true,true,true,true,false,false},
                {false,false,false,true,true,true,true,true,true,true,true,true,true,true,true,false,false,false},
                {false,false,false,false,true,true,true,true,true,true,true,true,true,true,false,false,false,false},
                {false,false,false,false,false,false,true,true,true,true,true,true,false,false,false,false,false,false}
            };
            
            // 枠パターン（外側1ピクセル）
            bool[,] edgePattern = new bool[18, 18];
            for (int x = 0; x < 18; x++)
            {
                for (int y = 0; y < 18; y++)
                {
                    if (circlePattern[x, y])
                    {
                        // 隣接する透明ピクセルがあれば枠
                        bool isEdge = false;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if (nx >= 0 && nx < 18 && ny >= 0 && ny < 18)
                                {
                                    if (!circlePattern[nx, ny])
                                    {
                                        isEdge = true;
                                        break;
                                    }
                                }
                                else
                                {
                                    isEdge = true;
                                    break;
                                }
                            }
                            if (isEdge) break;
                        }
                        edgePattern[x, y] = isEdge;
                    }
                }
            }
            
            // ピクセルを設定
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    if (circlePattern[x, y])
                    {
                        texture.SetPixel(x, y, edgePattern[x, y] ? edgeColor : validColor);
                    }
                    else
                    {
                        texture.SetPixel(x, y, Color.clear);
                    }
                }
            }
            
            texture.Apply();
            texture.name = "ValidPlacementTexture";
            return texture;
        }
        
        /// <summary>
        /// 赤色の配置不可インジケーターテクスチャを作成
        /// </summary>
        public static Texture2D CreateInvalidPlacementTexture()
        {
            int size = 18;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, // ピクセルパーフェクト
                wrapMode = TextureWrapMode.Clamp
            };
            
            Color invalidColor = new Color(0.9f, 0.2f, 0.2f, 0.9f); // 鮮やかな赤
            Color edgeColor = new Color(0.5f, 0.1f, 0.1f, 1f); // 濃い赤の枠
            
            // 18x18のX印パターンを手動定義
            bool[,] xPattern = new bool[18, 18];
            
            // 対角線でX印を描画
            for (int i = 0; i < 18; i++)
            {
                // 主対角線 (左上から右下)
                if (i - 1 >= 0) xPattern[i - 1, i] = true;
                xPattern[i, i] = true;
                if (i + 1 < 18) xPattern[i + 1, i] = true;
                
                // 副対角線 (右上から左下)
                if (i - 1 >= 0) xPattern[i - 1, 17 - i] = true;
                xPattern[i, 17 - i] = true;
                if (i + 1 < 18) xPattern[i + 1, 17 - i] = true;
            }
            
            // ピクセルを設定
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    if (xPattern[x, y])
                    {
                        texture.SetPixel(x, y, invalidColor);
                    }
                    else
                    {
                        texture.SetPixel(x, y, Color.clear);
                    }
                }
            }
            
            texture.Apply();
            texture.name = "InvalidPlacementTexture";
            return texture;
        }
    }
}
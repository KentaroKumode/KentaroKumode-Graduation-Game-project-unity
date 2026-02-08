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

        /// <summary>
        /// ゴミ箱アイコンテクスチャを作成（32x32ピクセル、半透明背景付き）
        /// </summary>
        public static Texture2D CreateTrashIconTexture(int size = 64)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color bg = new Color(0.15f, 0.15f, 0.15f, 0.75f);       // 暗い半透明背景
            Color iconColor = new Color(1f, 0.35f, 0.2f, 1f);        // 赤オレンジ
            Color lidColor = new Color(1f, 0.5f, 0.3f, 1f);          // 蓋（やや明るい）

            // 全ピクセル初期化
            Color[] pixels = new Color[size * size];
            
            // 背景: 角丸矩形
            float radius = size * 0.15f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(x - size / 2f) - (size / 2f - radius));
                    float dy = Mathf.Max(0, Mathf.Abs(y - size / 2f) - (size / 2f - radius));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * size + x] = dist <= radius ? bg : Color.clear;
                }
            }

            // 正規化座標系でゴミ箱を描画
            // 蓋: 上部の横棒
            FillRect(pixels, size, 0.20f, 0.78f, 0.80f, 0.85f, lidColor);
            // 蓋のつまみ
            FillRect(pixels, size, 0.38f, 0.85f, 0.62f, 0.92f, lidColor);

            // 本体: 台形（矩形で近似）
            FillRect(pixels, size, 0.22f, 0.18f, 0.78f, 0.76f, iconColor);
            // 下側を少し狭める
            FillRect(pixels, size, 0.22f, 0.18f, 0.27f, 0.50f, Color.clear); // 左下カット
            FillRect(pixels, size, 0.73f, 0.18f, 0.78f, 0.50f, Color.clear); // 右下カット

            // 縦の削除ライン（3本）
            FillRect(pixels, size, 0.36f, 0.25f, 0.40f, 0.70f, bg);
            FillRect(pixels, size, 0.48f, 0.25f, 0.52f, 0.70f, bg);
            FillRect(pixels, size, 0.60f, 0.25f, 0.64f, 0.70f, bg);

            texture.SetPixels(pixels);
            texture.Apply();
            texture.name = "TrashIconTexture";
            return texture;
        }

        /// <summary>正規化座標でピクセル矩形を塗りつぶす</summary>
        private static void FillRect(Color[] pixels, int size, float x0, float y0, float x1, float y1, Color color)
        {
            int px0 = Mathf.RoundToInt(x0 * size);
            int py0 = Mathf.RoundToInt(y0 * size);
            int px1 = Mathf.RoundToInt(x1 * size);
            int py1 = Mathf.RoundToInt(y1 * size);

            for (int y = py0; y < py1 && y < size; y++)
            {
                for (int x = px0; x < px1 && x < size; x++)
                {
                    if (x >= 0 && y >= 0)
                        pixels[y * size + x] = color;
                }
            }
        }
    }
}

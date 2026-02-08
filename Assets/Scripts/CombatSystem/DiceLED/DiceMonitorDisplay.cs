using UnityEngine;
using System;

namespace CombatSystem.DiceLED
{
    /// <summary>
    /// ダイス合計値を表示するモニターディスプレイ。
    /// 
    /// <para><b>特徴:</b></para>
    /// <list type="bullet">
    ///   <item>Plane の縦横比に依存しないアスペクト比補正描画</item>
    ///   <item>ベベル（角丸）対応</item>
    ///   <item>タイルシートベースのピクセルフォント表示</item>
    ///   <item>液晶エフェクト（スキャンライン、ピクセルギャップ等）</item>
    ///   <item>Emission 対応で自発光</item>
    /// </list>
    /// 
    /// <para><b>使い方:</b></para>
    /// <code>
    /// monitor.Initialize();
    /// monitor.DisplayNumber(42);
    /// monitor.SetGlowColor(new Color(0.2f, 0.8f, 1f)); // 水色
    /// </code>
    /// 
    /// <para><b>アスペクト比補正の仕組み:</b></para>
    /// テクスチャは常に正方形で生成し、Mesh の UV を調整するのではなく、
    /// テクスチャ内の描画領域を Plane の実際のアスペクト比に合わせて計算する。
    /// これにより Plane を自由にスケーリングしても文字が引き伸ばされない。
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class DiceMonitorDisplay : MonoBehaviour
    {
        // =================================================================
        //  Inspector
        // =================================================================

        [Header("=== タイル設定 ===")]
        [Tooltip("数字タイルシート（4×4 グリッド、0–9）")]
        [SerializeField] private Texture2D tileSheet;

        [Tooltip("タイルシートの列数")]
        [SerializeField] private int tilesPerRow = 4;

        [Tooltip("1 タイルの幅（px）")]
        [SerializeField] private int tileWidth = 8;

        [Tooltip("1 タイルの高さ（px）")]
        [SerializeField] private int tileHeight = 8;

        [Header("=== ディスプレイ設定 ===")]
        [Tooltip("テクスチャ解像度（正方形の一辺）")]
        [SerializeField, Range(64, 2048)] private int textureResolution = 512;

        [Tooltip("最大表示桁数")]
        [SerializeField, Range(1, 6)] private int maxDigits = 2;

        [Tooltip("数字の表示スケール（テクスチャ解像度に対する割合）")]
        [SerializeField, Range(0.1f, 0.9f)] private float digitScale = 0.6f;

        [Tooltip("数字間のスペーシング（タイル幅に対する割合）")]
        [SerializeField, Range(-0.5f, 1f)] private float digitSpacingRatio = 0.15f;

        [Tooltip("表示位置")]
        [SerializeField] private TextAnchor alignment = TextAnchor.MiddleCenter;

        [Header("=== カラー ===")]
        [Tooltip("背景色")]
        [SerializeField] private Color backgroundColor = new Color(0.02f, 0.02f, 0.05f);

        [Tooltip("文字色（発光色）")]
        [SerializeField] private Color textColor = new Color(0.2f, 0.8f, 1f);

        [Tooltip("Emission 強度")]
        [SerializeField, Range(0.5f, 10f)] private float emissiveIntensity = 2f;

        [Header("=== ベベル（角丸）===")]
        [Tooltip("ベベルを有効化")]
        [SerializeField] private bool enableBevel = true;

        [Tooltip("ベベル半径（テクスチャ解像度に対する割合）")]
        [SerializeField, Range(0f, 0.25f)] private float bevelRadius = 0.08f;

        [Tooltip("ベベルのスムーズ幅（アンチエイリアス用、px）")]
        [SerializeField, Range(0f, 8f)] private float bevelSmoothness = 2f;

        [Header("=== 液晶エフェクト ===")]
        [Tooltip("液晶エフェクトを有効化")]
        [SerializeField] private bool enableLCDEffect = true;

        [Tooltip("ピクセル間ギャップ（0–1）")]
        [SerializeField, Range(0f, 0.5f)] private float pixelGap = 0.12f;

        [Tooltip("スキャンライン")]
        [SerializeField] private bool enableScanlines = true;

        [Tooltip("スキャンライン強度")]
        [SerializeField, Range(0f, 0.5f)] private float scanlineIntensity = 0.15f;

        [Tooltip("スキャンライン間隔（px）")]
        [SerializeField, Range(1, 20)] private int scanlineSpacing = 3;

        [Header("=== アンチエイリアス ===")]
        [SerializeField] private bool enableMipmaps = true;
        [SerializeField] private FilterMode textureFilterMode = FilterMode.Trilinear;
        [SerializeField, Range(0, 16)] private int anisotropicLevel = 4;

        // =================================================================
        //  状態
        // =================================================================

        private Texture2D displayTexture;
        private Material displayMaterial;
        private MeshRenderer meshRenderer;
        private Color32[] clearBuffer;
        private bool isInitialized;

        // キャッシュ: Plane の実アスペクト比から算出した描画領域
        private int drawWidth;
        private int drawHeight;
        private int drawOffsetX;
        private int drawOffsetY;

        // =================================================================
        //  プロパティ / イベント
        // =================================================================

        /// <summary>初期化済みか</summary>
        public bool IsInitialized => isInitialized;

        /// <summary>表示が更新されたとき</summary>
        public event Action<int> OnDisplayUpdate;

        // =================================================================
        //  Unity Lifecycle
        // =================================================================

        void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        void Start()
        {
            if (tileSheet != null && !isInitialized)
                Initialize();
        }

        void OnDestroy()
        {
            if (displayTexture != null) Destroy(displayTexture);
            if (displayMaterial != null) Destroy(displayMaterial);
        }

        // =================================================================
        //  初期化
        // =================================================================

        /// <summary>ディスプレイシステムを初期化</summary>
        public void Initialize()
        {
            if (isInitialized) return;

            if (tileSheet == null)
            {
                Debug.LogWarning($"[DiceMonitorDisplay] {gameObject.name}: タイルシートが未設定");
                return;
            }

            if (meshRenderer == null)
                meshRenderer = GetComponent<MeshRenderer>();

            // テクスチャ読み取り可能性チェック
            try { tileSheet.GetPixel(0, 0); }
            catch (UnityException)
            {
                Debug.LogError($"[DiceMonitorDisplay] Texture '{tileSheet.name}' is not readable! " +
                    "Inspector → Advanced → Read/Write Enabled をチェックしてください");
                return;
            }

            ComputeDrawArea();
            CreateDisplayTexture();
            CreateDisplayMaterial();
            ClearDisplay();

            isInitialized = true;
        }

        /// <summary>
        /// Plane のワールドスケールからアスペクト比を取得し、
        /// テクスチャ内の描画領域を計算する。
        /// Plane をどんな縦横比にしても引き伸ばしが起きない。
        /// </summary>
        private void ComputeDrawArea()
        {
            // Plane のワールドスケールからアスペクト比を取得
            Vector3 scale = transform.lossyScale;

            // Unity の Plane は X-Z 平面（10×10 units at scale 1）
            // X → 横幅、Z → 縦高さ
            float worldW = Mathf.Abs(scale.x);
            float worldH = Mathf.Abs(scale.z);

            if (worldW <= 0.0001f || worldH <= 0.0001f)
            {
                // フォールバック: 正方形
                drawWidth = textureResolution;
                drawHeight = textureResolution;
                drawOffsetX = 0;
                drawOffsetY = 0;
                return;
            }

            float planeAspect = worldW / worldH; // >1 = 横長、<1 = 縦長

            if (planeAspect >= 1f)
            {
                // 横長: テクスチャ幅をフル使用、高さを縮める
                drawWidth = textureResolution;
                drawHeight = Mathf.RoundToInt(textureResolution / planeAspect);
            }
            else
            {
                // 縦長: テクスチャ高さをフル使用、幅を縮める
                drawHeight = textureResolution;
                drawWidth = Mathf.RoundToInt(textureResolution * planeAspect);
            }

            drawWidth  = Mathf.Clamp(drawWidth, 1, textureResolution);
            drawHeight = Mathf.Clamp(drawHeight, 1, textureResolution);

            // テクスチャ中央に配置
            drawOffsetX = (textureResolution - drawWidth) / 2;
            drawOffsetY = (textureResolution - drawHeight) / 2;
        }

        private void CreateDisplayTexture()
        {
            displayTexture = new Texture2D(
                textureResolution, textureResolution,
                TextureFormat.RGBA32, enableMipmaps);
            displayTexture.filterMode = textureFilterMode;
            displayTexture.wrapMode = TextureWrapMode.Clamp;
            displayTexture.anisoLevel = anisotropicLevel;

            // クリアバッファ（全面透明黒）
            int total = textureResolution * textureResolution;
            clearBuffer = new Color32[total];
            var transparent = new Color32(0, 0, 0, 0);
            for (int i = 0; i < total; i++)
                clearBuffer[i] = transparent;
        }

        private void CreateDisplayMaterial()
        {
            // Emission + 透過対応（ベベルで角を透明にするため）
            displayMaterial = new Material(Shader.Find("Standard"));
            displayMaterial.SetFloat("_Mode", 3); // Transparent
            displayMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            displayMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            displayMaterial.SetInt("_ZWrite", 0);
            displayMaterial.DisableKeyword("_ALPHATEST_ON");
            displayMaterial.EnableKeyword("_ALPHABLEND_ON");
            displayMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            displayMaterial.renderQueue = 3000;

            displayMaterial.EnableKeyword("_EMISSION");
            displayMaterial.SetTexture("_MainTex", displayTexture);
            displayMaterial.SetColor("_Color", Color.white);
            displayMaterial.SetTexture("_EmissionMap", displayTexture);
            displayMaterial.SetColor("_EmissionColor", Color.white * emissiveIntensity);
            displayMaterial.SetFloat("_Metallic", 0f);
            displayMaterial.SetFloat("_Glossiness", 0f);
            displayMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

            meshRenderer.material = displayMaterial;
        }

        // =================================================================
        //  公開 API
        // =================================================================

        /// <summary>数値を表示</summary>
        public void DisplayNumber(int value)
        {
            if (!isInitialized) return;

            int maxValue = (int)Mathf.Pow(10, maxDigits) - 1;
            int clamped = Mathf.Clamp(value, 0, maxValue);
            string text = clamped.ToString();

            RenderText(text);
            OnDisplayUpdate?.Invoke(value);
        }

        /// <summary>テキストを表示（数字のみ対応）</summary>
        public void DisplayText(string text)
        {
            if (!isInitialized) return;
            RenderText(text ?? "");
        }

        /// <summary>表示をクリア</summary>
        public void ClearDisplay()
        {
            if (!isInitialized && displayTexture == null) return;

            displayTexture.SetPixels32(clearBuffer);
            FillBackground();
            ApplyBevel();
            displayTexture.Apply();
        }

        /// <summary>発光色を変更（ダイス色と揃える用）</summary>
        public void SetGlowColor(Color color)
        {
            textColor = color;
            if (displayMaterial != null)
            {
                displayMaterial.SetColor("_EmissionColor",
                    Color.white * emissiveIntensity);
            }
        }

        /// <summary>背景色を変更</summary>
        public void SetBackgroundColor(Color color)
        {
            backgroundColor = color;
        }

        /// <summary>Emission 強度を変更</summary>
        public void SetEmissiveIntensity(float intensity)
        {
            emissiveIntensity = intensity;
            if (displayMaterial != null)
            {
                displayMaterial.SetColor("_EmissionColor",
                    Color.white * emissiveIntensity);
            }
        }

        /// <summary>タイルシートを外部から設定（Initialize前に呼ぶ）</summary>
        public void SetTileSheet(Texture2D sheet)
        {
            tileSheet = sheet;
        }

        // =================================================================
        //  描画コア
        // =================================================================

        private void RenderText(string text)
        {
            // テクスチャをクリア
            displayTexture.SetPixels32(clearBuffer);
            FillBackground();

            if (text.Length == 0)
            {
                ApplyBevel();
                displayTexture.Apply();
                return;
            }

            // 描画領域内でのタイルスケールを計算
            // digitScale は描画領域の高さに対する数字の高さの割合
            int scaledTileH = Mathf.RoundToInt(drawHeight * digitScale);
            int pixelScale = Mathf.Max(1, scaledTileH / tileHeight);
            int scaledTileW = tileWidth * pixelScale;
            scaledTileH = tileHeight * pixelScale; // 整数倍に丸める

            int spacing = Mathf.RoundToInt(scaledTileW * digitSpacingRatio);
            int digitStride = scaledTileW + spacing;
            int totalWidth = text.Length * digitStride - spacing;

            // 描画領域内でのアライメント計算
            int startX, startY;
            ComputeAlignment(totalWidth, scaledTileH, out startX, out startY);

            // 各文字を描画
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i])) continue;
                int digit = text[i] - '0';
                int x = startX + i * digitStride;
                DrawTile(digit, x, startY, pixelScale);
            }

            // ベベル適用
            ApplyBevel();

            displayTexture.Apply();
        }

        /// <summary>描画領域を背景色で塗りつぶす</summary>
        private void FillBackground()
        {
            Color32 bg = backgroundColor;
            Color32[] pixels = displayTexture.GetPixels32();

            for (int y = 0; y < textureResolution; y++)
            {
                for (int x = 0; x < textureResolution; x++)
                {
                    // 描画領域内のみ背景色、外は透明
                    if (IsInsideDrawArea(x, y))
                        pixels[y * textureResolution + x] = bg;
                }
            }

            displayTexture.SetPixels32(pixels);
        }

        /// <summary>テクスチャ座標が描画領域内か</summary>
        private bool IsInsideDrawArea(int x, int y)
        {
            return x >= drawOffsetX && x < drawOffsetX + drawWidth
                && y >= drawOffsetY && y < drawOffsetY + drawHeight;
        }

        /// <summary>アライメントに基づいて描画開始位置を計算</summary>
        private void ComputeAlignment(int contentW, int contentH,
                                       out int startX, out int startY)
        {
            // 水平
            switch (alignment)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.MiddleLeft:
                case TextAnchor.LowerLeft:
                    startX = drawOffsetX + Mathf.RoundToInt(drawWidth * 0.05f);
                    break;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    startX = drawOffsetX + drawWidth - contentW
                             - Mathf.RoundToInt(drawWidth * 0.05f);
                    break;
                default: // Center
                    startX = drawOffsetX + (drawWidth - contentW) / 2;
                    break;
            }

            // 垂直
            switch (alignment)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.UpperCenter:
                case TextAnchor.UpperRight:
                    startY = drawOffsetY + drawHeight - contentH
                             - Mathf.RoundToInt(drawHeight * 0.05f);
                    break;
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    startY = drawOffsetY + Mathf.RoundToInt(drawHeight * 0.05f);
                    break;
                default: // Middle
                    startY = drawOffsetY + (drawHeight - contentH) / 2;
                    break;
            }
        }

        // =================================================================
        //  タイル描画
        // =================================================================

        /// <summary>タイルシートから数字を描画</summary>
        private void DrawTile(int tileIndex, int x, int y, int scale)
        {
            if (tileIndex < 0 || tileIndex > 9) return;

            // タイルシートのグリッド位置（TiledPixelDisplay と同じレイアウト）
            // Row 0 (top of image → gridY=3): 1,2,3,4
            // Row 1 (gridY=2): 5,6,7,8
            // Row 2 (gridY=1): 9,0
            int gridX, gridY;
            if (tileIndex == 0)      { gridX = 1; gridY = 1; }
            else if (tileIndex <= 4) { gridX = tileIndex - 1; gridY = 3; }
            else if (tileIndex <= 8) { gridX = tileIndex - 5; gridY = 2; }
            else                     { gridX = 0; gridY = 1; }

            int srcX = gridX * tileWidth;
            int srcY = gridY * tileHeight;

            // ソースピクセル取得
            Color[] srcPixels = tileSheet.GetPixels(srcX, srcY, tileWidth, tileHeight);

            int scaledW = tileWidth * scale;
            int scaledH = tileHeight * scale;

            for (int py = 0; py < scaledH; py++)
            {
                for (int px = 0; px < scaledW; px++)
                {
                    int si = (py / scale) * tileWidth + (px / scale);
                    Color src = srcPixels[si];

                    // 輝度判定（明るい = 文字、暗い = 背景）
                    float brightness = (src.r + src.g + src.b) / 3f;
                    if (brightness <= 0.5f) continue;

                    int destX = x + px;
                    int destY = y + py;

                    if (!IsInsideDrawArea(destX, destY)) continue;

                    Color32 finalColor;
                    if (enableLCDEffect)
                        finalColor = ApplyLCDEffect(px, py, scale, destX, destY);
                    else
                        finalColor = (Color32)textColor;

                    displayTexture.SetPixel(destX, destY, finalColor);
                }
            }
        }

        // =================================================================
        //  液晶エフェクト
        // =================================================================

        private Color32 ApplyLCDEffect(int px, int py, int scale,
                                        int destX, int destY)
        {
            float normX = (px % scale) / (float)scale;
            float normY = (py % scale) / (float)scale;

            // ピクセルギャップ
            float gapFactor = 1f;
            if (pixelGap > 0f)
            {
                float gx = Mathf.Abs(normX - 0.5f) * 2f;
                float gy = Mathf.Abs(normY - 0.5f) * 2f;
                float gap = Mathf.Max(gx, gy);
                float threshold = 1f - pixelGap;
                if (gap > threshold)
                {
                    gapFactor = 1f - (gap - threshold) / pixelGap;
                    gapFactor = Mathf.Clamp01(gapFactor);
                }
            }

            // スキャンライン
            float scanFactor = 1f;
            if (enableScanlines && scanlineSpacing > 0)
            {
                if (destY % scanlineSpacing == 0)
                    scanFactor = 1f - scanlineIntensity;
            }

            float intensity = gapFactor * scanFactor;
            return new Color32(
                (byte)(textColor.r * 255 * intensity),
                (byte)(textColor.g * 255 * intensity),
                (byte)(textColor.b * 255 * intensity),
                255);
        }

        // =================================================================
        //  ベベル（角丸）
        // =================================================================

        /// <summary>
        /// 描画領域の四隅にベベル（角丸）を適用。
        /// 領域外 → 透明、角の曲線境界 → alpha 減衰でアンチエイリアス。
        /// </summary>
        private void ApplyBevel()
        {
            if (!enableBevel || bevelRadius <= 0f) return;

            int radiusPx = Mathf.RoundToInt(
                Mathf.Min(drawWidth, drawHeight) * 0.5f * bevelRadius);
            if (radiusPx <= 0) return;

            Color32[] pixels = displayTexture.GetPixels32();

            // 四隅のみ処理（全ピクセル走査ではなく角だけ）
            ProcessCorner(pixels, drawOffsetX, drawOffsetY,
                          radiusPx, false, false);                    // 左下
            ProcessCorner(pixels, drawOffsetX + drawWidth - 1, drawOffsetY,
                          radiusPx, true, false);                     // 右下
            ProcessCorner(pixels, drawOffsetX, drawOffsetY + drawHeight - 1,
                          radiusPx, false, true);                     // 左上
            ProcessCorner(pixels, drawOffsetX + drawWidth - 1,
                          drawOffsetY + drawHeight - 1,
                          radiusPx, true, true);                      // 右上

            displayTexture.SetPixels32(pixels);
        }

        /// <summary>1 つの角にベベルを適用</summary>
        private void ProcessCorner(Color32[] pixels,
                                    int cornerX, int cornerY,
                                    int radius,
                                    bool flipX, bool flipY)
        {
            // 角の「中心」（円弧の中心点）
            int cx = flipX ? cornerX - radius : cornerX + radius;
            int cy = flipY ? cornerY - radius : cornerY + radius;

            int xStart = flipX ? cornerX - radius : cornerX;
            int xEnd   = flipX ? cornerX : cornerX + radius;
            int yStart = flipY ? cornerY - radius : cornerY;
            int yEnd   = flipY ? cornerY : cornerY + radius;

            xStart = Mathf.Clamp(xStart, 0, textureResolution - 1);
            xEnd   = Mathf.Clamp(xEnd,   0, textureResolution - 1);
            yStart = Mathf.Clamp(yStart, 0, textureResolution - 1);
            yEnd   = Mathf.Clamp(yEnd,   0, textureResolution - 1);

            float smooth = Mathf.Max(bevelSmoothness, 0.5f);

            for (int y = yStart; y <= yEnd; y++)
            {
                for (int x = xStart; x <= xEnd; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > radius)
                    {
                        // 角丸の外側 → 透明化（アンチエイリアス付き）
                        float alpha = Mathf.Clamp01(1f - (dist - radius) / smooth);
                        int idx = y * textureResolution + x;
                        if (idx >= 0 && idx < pixels.Length)
                        {
                            Color32 p = pixels[idx];
                            p.a = (byte)(p.a * alpha);
                            pixels[idx] = p;
                        }
                    }
                }
            }
        }

        // =================================================================
        //  エディタ用
        // =================================================================

#if UNITY_EDITOR
        /// <summary>Inspector 値変更時にアスペクト比を再計算</summary>
        void OnValidate()
        {
            if (isInitialized)
            {
                ComputeDrawArea();
            }
        }

        [ContextMenu("Re-Initialize")]
        private void ReInitialize()
        {
            if (displayTexture != null) DestroyImmediate(displayTexture);
            if (displayMaterial != null) DestroyImmediate(displayMaterial);
            isInitialized = false;
            Initialize();
        }

        [ContextMenu("Test: Show 42")]
        private void TestShow42()
        {
            if (!isInitialized) Initialize();
            DisplayNumber(42);
        }

        [ContextMenu("Test: Show 99")]
        private void TestShow99()
        {
            if (!isInitialized) Initialize();
            DisplayNumber(99);
        }
#endif
    }
}

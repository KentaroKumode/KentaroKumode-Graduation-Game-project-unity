using UnityEngine;
using System;

namespace CoinSystem
{
    /// <summary>
    /// タイル状の画像を使用してピクセルディスプレイを実現する独立コンポーネント
    /// 4x4グリッドの数字タイル（0-9）を使用して液晶風の表示を作成
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class TiledPixelDisplay : MonoBehaviour
    {
        #region Serialized Fields
        [Header("タイル設定")]
        [SerializeField] private Texture2D tileSheet;
        [SerializeField] private int tilesPerRow = 4;
        [SerializeField] private int tilesPerColumn = 4;
        [SerializeField] private int tileWidth = 8;
        [SerializeField] private int tileHeight = 8;

        [Header("ディスプレイ設定")]
        [SerializeField] private int maxDigits = 3;
        [SerializeField] private int displayWidth = 344;  // 172 * 2 (高解像度化)
        [SerializeField] private int displayHeight = 1110; // 555 * 2 (アスペクト比 172:555)
        [SerializeField] [Range(-20f, 50f)] private float digitSpacing = 4f; // 数字間のスペーシング（負数で詰める、正数で広げる）
        [SerializeField] [Range(1, 64)] private int digitScaleX = 8; // 横方向スケール（整数倍）
        [SerializeField] [Range(1, 64)] private int digitScaleY = 8; // 縦方向スケール（整数倍）
        
        [Header("アイコン設定")]
        [SerializeField] private Texture2D iconTexture; // アイコン画像（例：コイン）
        [SerializeField] [Range(1, 32)] private int iconScaleX = 4; // アイコンの横スケール
        [SerializeField] [Range(1, 32)] private int iconScaleY = 4; // アイコンの縦スケール
        [SerializeField] [Range(-2500, 2500)] private int iconMarginX = 4; // アイコンの横マージン（画面端から、負の数可）
        [SerializeField] [Range(-2500, 2500)] private int iconMarginY = 4; // アイコンの縦マージン（画面端から、負の数可）
        [SerializeField] [Range(-2500, 2500)] private int iconSpacing = 10; // アイコンと数字の間隔（負の数可）
        
        [Header("表示位置設定")]
        [SerializeField] private TextAnchor alignment = TextAnchor.UpperRight; // 表示位置（UnityのTextAnchor使用）
        [SerializeField] [Range(-2500, 2500)] private int marginX = 4; // 横マージン（ピクセル、負の数可）
        [SerializeField] [Range(-2500, 2500)] private int marginY = 4; // 縦マージン（ピクセル、負の数可）

        [Header("マテリアル設定")]
        [SerializeField] private bool useEmissive = true;
        [SerializeField] private float emissiveIntensity = 1.0f;
        [SerializeField] private Color displayColor = Color.green; // ディスプレイ全体の色（背景色として機能）
        [SerializeField] private Color textColor = Color.white; // 文字（数字）の色
        
        [Header("液晶エフェクト設定")]
        [SerializeField] private bool enableLCDEffect = true; // 液晶エフェクトを有効化
        [SerializeField] [Range(0f, 1.0f)] private float pixelGap = 0.15f; // ピクセル間のギャップ（0-1.0）
        [SerializeField] private bool enableEdgeGradient = false; // 数字のフチにグラデーションをかける
        [SerializeField] [Range(0f, 1.0f)] private float edgeGradientStrength = 0.3f; // フチグラデーションの強さ
        [SerializeField] [Range(0f, 1f)] private float glowIntensity = 0.0f; // グロー強度（パフォーマンス重視でデフォルトOFF）
        [SerializeField] private bool enableScanlines = true; // スキャンライン効果
        [SerializeField] [Range(0f, 1.0f)] private float scanlineIntensity = 0.2f; // スキャンライン暗さ（0-1.0）
        [SerializeField] [Range(1, 100)] private int scanlineWidth = 2; // スキャンラインの間隔（ピクセル数）
        [SerializeField] [Range(1, 50)] private int scanlineThickness = 1; // スキャンラインの太さ（ピクセル数）
        [SerializeField] private bool scanlineGradient = true; // スキャンラインにグラデーションをかける
        [SerializeField] [Range(0f, 0.3f)] private float colorTint = 0.1f; // 色温度（青緑がかり）
        
        [Header("アウトライングロー設定")]
        [SerializeField] private bool enableOutlineGlow = false; // アウトライングローを有効化（パフォーマンス重視でデフォルトOFF）
        [SerializeField] [Range(1, 5)] private int outlineGlowRadius = 2; // グロー半径
        [SerializeField] [Range(0f, 1f)] private float outlineGlowIntensity = 0.5f; // グロー強度
        [SerializeField] private Color outlineGlowColor = Color.white; // グロー色
        
        [Header("アンチエイリアス・モアレ対策")]
        [SerializeField] private bool enableMipmaps = true; // ミップマップでモアレ軽減
        [SerializeField] private FilterMode textureFilterMode = FilterMode.Trilinear; // Point=シャープ、Bilinear=滑らか、Trilinear=最高品質
        [SerializeField] [Range(0, 16)] private int anisotropicLevel = 4; // 異方性フィルタリング（斜めから見たときの品質向上）
        #endregion

        #region Private Fields
        private Texture2D displayTexture;
        private Material displayMaterial;
        private MeshRenderer meshRenderer;
        private Color32[] clearColors;
        private bool isInitialized = false;
        #endregion

        #region Events
        public event Action<string> OnDisplayUpdate;
        public event Action OnDisplayClear;
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        private void Start()
        {
            // CoinSystemManagerから明示的に初期化される場合はスキップ
            // タイルシートが既に設定されている場合のみ自動初期化
            if (tileSheet != null && !isInitialized)
            {
                Initialize();
            }
        }
        #endregion

        #region Initialization
        /// <summary>
        /// ディスプレイシステムを初期化
        /// </summary>
        public void Initialize()
        {
            if (isInitialized)
            {
                Debug.LogWarning($"[TiledPixelDisplay] {gameObject.name} は既に初期化されています");
                return;
            }

            if (tileSheet == null)
            {
                Debug.LogWarning($"[TiledPixelDisplay] {gameObject.name}: タイルシートが設定されていません。CoinSystemManagerから設定されるまで待機します。");
                return;
            }
            
            // テクスチャの読み込み可能性をチェック
            try
            {
                Color testPixel = tileSheet.GetPixel(0, 0);
            }
            catch (UnityException e)
            {
                Debug.LogError($"[TiledPixelDisplay] Texture '{tileSheet.name}' is not readable!\n" +
                    $"Solution: Select the texture in Project window -> Inspector -> Advanced -> Check 'Read/Write Enabled' -> Apply");
                Debug.LogError(e);
                return;
            }

            if (meshRenderer == null)
            {
                Debug.LogError($"[TiledPixelDisplay] {gameObject.name}: MeshRendererが見つかりません");
                return;
            }

            CreateDisplayTexture();
            CreateDisplayMaterial();
            ClearDisplay();

            isInitialized = true;
            Debug.Log($"[TiledPixelDisplay] {gameObject.name} を初期化しました ({displayWidth}x{displayHeight})");
            Debug.Log($"[TiledPixelDisplay] テクスチャは全範囲(0,0)-({displayWidth},{displayHeight})に描画されます。メッシュ側のUV座標で表示範囲が決まります。");
        }

        /// <summary>
        /// ディスプレイテクスチャを作成
        /// </summary>
        private void CreateDisplayTexture()
        {
            // テクスチャは正方形で作成（長辺に合わせる）
            int textureSize = Mathf.Max(displayWidth, displayHeight);
            
            // ミップマップを有効化してモアレを軽減
            displayTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, enableMipmaps);
            
            // フィルタリング設定（モアレ対策）
            displayTexture.filterMode = textureFilterMode;
            displayTexture.wrapMode = TextureWrapMode.Clamp;
            
            // 異方性フィルタリング（斜めから見たときの品質向上）
            displayTexture.anisoLevel = anisotropicLevel;

            // クリアカラー配列を事前に作成（正方形全体）- 背景色を使用
            clearColors = new Color32[textureSize * textureSize];
            Color32 clearColor = new Color32(
                (byte)(displayColor.r * 255),
                (byte)(displayColor.g * 255),
                (byte)(displayColor.b * 255),
                255
            );
            for (int i = 0; i < clearColors.Length; i++)
            {
                clearColors[i] = clearColor;
            }

            Debug.Log($"[TiledPixelDisplay] テクスチャを作成: {textureSize}x{textureSize} (正方形), 使用領域: {displayWidth}x{displayHeight}");
            Debug.Log($"[TiledPixelDisplay] モアレ対策: Mipmaps={enableMipmaps}, FilterMode={textureFilterMode}, Aniso={anisotropicLevel}");
        }

        /// <summary>
        /// ディスプレイマテリアルを作成
        /// </summary>
        private void CreateDisplayMaterial()
        {
            if (useEmissive)
            {
                displayMaterial = new Material(Shader.Find("Standard"));
                displayMaterial.EnableKeyword("_EMISSION");
                
                // メインテクスチャ設定
                displayMaterial.SetTexture("_MainTex", displayTexture);
                displayMaterial.SetColor("_Color", Color.black); // メインカラーは黒（エミッシブのみで発光）
                
                // Emissive設定（テクスチャ全体がエミッシブ発光）
                displayMaterial.SetTexture("_EmissionMap", displayTexture);
                displayMaterial.SetColor("_EmissionColor", Color.white * emissiveIntensity); // テクスチャの色をそのまま使用
                
                // マテリアル特性を調整（完全にマットな表面で照明の影響を最小化）
                displayMaterial.SetFloat("_Metallic", 0f);
                displayMaterial.SetFloat("_Glossiness", 0f);
                
                // 環境光の影響を無効化
                displayMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                
                // テクスチャのタイリングとオフセットを設定
                displayMaterial.SetTextureScale("_MainTex", Vector2.one);
                displayMaterial.SetTextureOffset("_MainTex", Vector2.zero);
                displayMaterial.SetTextureScale("_EmissionMap", Vector2.one);
                displayMaterial.SetTextureOffset("_EmissionMap", Vector2.zero);
            }
            else
            {
                displayMaterial = new Material(Shader.Find("Unlit/Texture"));
                displayMaterial.mainTexture = displayTexture;
                displayMaterial.color = Color.white; // テクスチャの色をそのまま使用
                displayMaterial.SetTextureScale("_MainTex", Vector2.one);
                displayMaterial.SetTextureOffset("_MainTex", Vector2.zero);
            }

            meshRenderer.material = displayMaterial;
            
            // メッシュのUV座標を確認
            MeshFilter meshFilter = meshRenderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.mesh != null)
            {
                Debug.Log($"[TiledPixelDisplay] Mesh UV count: {meshFilter.mesh.uv.Length}");
            }
            
            Debug.Log($"[TiledPixelDisplay] マテリアルを作成: {(useEmissive ? "Emissive (Standard)" : "Unlit")}, Shader: {displayMaterial.shader.name}");
        }
        #endregion

        #region Display Control
        /// <summary>
        /// 数値を表示（右詰め）
        /// </summary>
        /// <param name="value">表示する数値</param>
        public void DisplayNumber(int value)
        {
            if (!ValidateInitialization()) return;

            // 最大桁数を超える場合は切り詰め
            int maxValue = (int)Mathf.Pow(10, maxDigits) - 1;
            int clampedValue = Mathf.Clamp(value, 0, maxValue);
            
            // 常にmaxDigits桁でゼロパディング
            string numberStr = clampedValue.ToString($"D{maxDigits}");
            Debug.Log($"[TiledPixelDisplay] DisplayNumber({value}) -> '{numberStr}' (length={numberStr.Length})");
            DisplayText(numberStr);

            OnDisplayUpdate?.Invoke(numberStr);
        }

        /// <summary>
        /// テキストを表示
        /// </summary>
        /// <param name="text">表示するテキスト（数字のみ対応）</param>
        public void DisplayText(string text)
        {
            if (!ValidateInitialization()) return;

            ClearDisplay();

            if (string.IsNullOrEmpty(text))
            {
                UpdateTexture();
                return;
            }

            // 表示位置を計算（alignment設定に基づく）
            int scaledTileWidth = tileWidth * digitScaleX;
            int scaledTileHeight = tileHeight * digitScaleY;
            int spacing = Mathf.RoundToInt(digitSpacing);
            int digitWidth = scaledTileWidth + spacing;
            int totalWidth = text.Length * digitWidth - spacing;

            int startX = 0;
            int startY = 0;
            
            // 水平方向の位置決定
            switch (alignment)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.MiddleLeft:
                case TextAnchor.LowerLeft:
                    startX = marginX; // 左揃え
                    break;
                case TextAnchor.UpperCenter:
                case TextAnchor.MiddleCenter:
                case TextAnchor.LowerCenter:
                    startX = (displayWidth - totalWidth) / 2; // 中央揃え
                    break;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    startX = displayWidth - totalWidth - marginX; // 右揃え
                    break;
            }
            
            // 垂直方向の位置決定
            switch (alignment)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.UpperCenter:
                case TextAnchor.UpperRight:
                    startY = displayHeight - scaledTileHeight - marginY; // 上揃え
                    break;
                case TextAnchor.MiddleLeft:
                case TextAnchor.MiddleCenter:
                case TextAnchor.MiddleRight:
                    startY = (displayHeight - scaledTileHeight) / 2 + marginY; // 中央揃え + 下方向オフセット
                    break;
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    startY = marginY; // 下揃え
                    break;
            }
            
            // 範囲チェック
            if (startX < 0)
            {
                Debug.LogWarning($"[TiledPixelDisplay] startX is negative ({startX})! Text too wide. Using left alignment.");
                startX = marginX;
            }
            
            // アイコンを描画（設定されている場合、数字の左側に）
            int iconWidth = 0;
            if (iconTexture != null)
            {
                int scaledIconWidth = iconTexture.width * iconScaleX;
                int scaledIconHeight = iconTexture.height * iconScaleY;
                
                // アイコンのX位置を計算（alignmentに基づいて画面端からの絶対位置）
                int iconX = 0;
                switch (alignment)
                {
                    case TextAnchor.UpperLeft:
                    case TextAnchor.MiddleLeft:
                    case TextAnchor.LowerLeft:
                        iconX = iconMarginX; // 左揃え
                        break;
                    case TextAnchor.UpperCenter:
                    case TextAnchor.MiddleCenter:
                    case TextAnchor.LowerCenter:
                        iconX = (displayWidth - scaledIconWidth) / 2 + iconMarginX; // 中央揃え
                        break;
                    case TextAnchor.UpperRight:
                    case TextAnchor.MiddleRight:
                    case TextAnchor.LowerRight:
                        iconX = displayWidth - scaledIconWidth - iconMarginX; // 右揃え
                        break;
                }
                
                // アイコンのY位置を計算（alignmentに基づいて画面端からの絶対位置）
                int iconY = 0;
                switch (alignment)
                {
                    case TextAnchor.UpperLeft:
                    case TextAnchor.UpperCenter:
                    case TextAnchor.UpperRight:
                        iconY = displayHeight - scaledIconHeight - iconMarginY; // 上揃え
                        break;
                    case TextAnchor.MiddleLeft:
                    case TextAnchor.MiddleCenter:
                    case TextAnchor.MiddleRight:
                        iconY = (displayHeight - scaledIconHeight) / 2 + iconMarginY; // 中央揃え
                        break;
                    case TextAnchor.LowerLeft:
                    case TextAnchor.LowerCenter:
                    case TextAnchor.LowerRight:
                        iconY = iconMarginY; // 下揃え
                        break;
                }
                
                // アイコンを描画
                DrawIcon(iconX, iconY, iconScaleX, iconScaleY);
                
                // 数字の位置をアイコンとiconSpacing分だけずらす（左揃え/中央揃え/右揃えに応じて）
                switch (alignment)
                {
                    case TextAnchor.UpperLeft:
                    case TextAnchor.MiddleLeft:
                    case TextAnchor.LowerLeft:
                        startX = iconX + scaledIconWidth + iconSpacing; // アイコンの右側
                        break;
                    case TextAnchor.UpperRight:
                    case TextAnchor.MiddleRight:
                    case TextAnchor.LowerRight:
                        startX -= (scaledIconWidth + iconSpacing); // アイコン分左にずらす
                        break;
                    // 中央揃えの場合は数字の位置はそのまま（アイコンも中央）
                }
                
                iconWidth = scaledIconWidth + iconSpacing;
            }
            
            // 各文字を描画
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsDigit(c))
                {
                    int digitValue = c - '0';
                    int x = startX + (i * digitWidth);
                    DrawTile(digitValue, x, startY, digitScaleX, digitScaleY);
                }
            }

            // アウトライングローを適用（全ての文字を描画した後）
            if (enableOutlineGlow && outlineGlowIntensity > 0)
            {
                ApplyOutlineGlow();
            }

            UpdateTexture();
        }

        /// <summary>
        /// ディスプレイをクリア
        /// </summary>
        public void ClearDisplay()
        {
            if (!ValidateInitialization()) return;

            displayTexture.SetPixels32(clearColors);
            displayTexture.Apply();
            
            Debug.Log($"[TiledPixelDisplay] Display cleared with {clearColors.Length} black pixels");

            OnDisplayClear?.Invoke();
        }
        
        /// <summary>
        /// 背景色を変更
        /// </summary>
        /// <param name="newColor">新しい背景色</param>
        public void SetDisplayColor(Color newColor)
        {
            displayColor = newColor;
            
            if (displayMaterial != null)
            {
                displayMaterial.SetColor("_Color", displayColor);
                Debug.Log($"[TiledPixelDisplay] Background color changed to {newColor}");
            }
        }
        
        /// <summary>
        /// 文字（数字）の色を変更
        /// </summary>
        /// <param name="newColor">新しい文字色</param>
        public void SetTextColor(Color newColor)
        {
            textColor = newColor;
            
            if (displayMaterial != null && useEmissive)
            {
                displayMaterial.SetColor("_EmissionColor", textColor * emissiveIntensity);
                Debug.Log($"[TiledPixelDisplay] Text color changed to {newColor}");
            }
        }
        
        /// <summary>
        /// 発光強度を変更
        /// </summary>
        /// <param name="intensity">発光強度</param>
        public void SetEmissiveIntensity(float intensity)
        {
            emissiveIntensity = intensity;
            
            if (displayMaterial != null && useEmissive)
            {
                displayMaterial.SetColor("_EmissionColor", textColor * emissiveIntensity);
                Debug.Log($"[TiledPixelDisplay] Emissive intensity changed to {intensity}");
            }
        }
        #endregion

        #region Drawing Methods
        /// <summary>
        /// タイルシートから指定のタイルを描画
        /// </summary>
        /// <param name="tileIndex">タイルインデックス（0-9）</param>
        /// <param name="x">描画位置X</param>
        /// <param name="y">描画位置Y</param>
        /// <param name="scaleX">横方向の整数倍スケール</param>
        /// <param name="scaleY">縦方向の整数倍スケール</param>
        private void DrawTile(int tileIndex, int x, int y, int scaleX, int scaleY)
        {
            if (tileIndex < 0 || tileIndex > 9)
            {
                Debug.LogWarning($"[TiledPixelDisplay] 無効なタイルインデックス: {tileIndex}");
                return;
            }

            // 数字からグリッド位置へのマッピング
            // タイルシート画像レイアウト（上から）:
            // Row 0: 1, 2, 3, 4
            // Row 1: 5, 6, 7, 8  
            // Row 2: 9, 0, 空, 空
            // Row 3: 空
            //
            // UnityのGetPixels()は左下原点なので:
            // gridY=3: 1, 2, 3, 4 (画像最上段)
            // gridY=2: 5, 6, 7, 8 (画像2段目)
            // gridY=1: 9, 0, 空, 空 (画像3段目)
            // gridY=0: 空 (画像最下段)
            int gridX, gridY;
            if (tileIndex == 0)
            {
                gridX = 1; gridY = 1; // 0は画像3段目2列目
            }
            else if (tileIndex <= 4)
            {
                gridX = tileIndex - 1; gridY = 3; // 1-4は画像最上段
            }
            else if (tileIndex <= 8)
            {
                gridX = tileIndex - 5; gridY = 2; // 5-8は画像2段目
            }
            else // tileIndex == 9
            {
                gridX = 0; gridY = 1; // 9は画像3段目1列目
            }

            // タイルシートからタイルを抽出
            int tileX = gridX * tileWidth;
            int tileY = gridY * tileHeight;
            
            Debug.Log($"[TiledPixelDisplay] DrawTile({tileIndex}) - Grid:({gridX},{gridY}), TilePos:({tileX},{tileY}), Scale:{scaleX}x{scaleY}");
            
            // 座標が範囲内かチェック
            if (tileX + tileWidth > tileSheet.width || tileY + tileHeight > tileSheet.height)
            {
                Debug.LogError($"[TiledPixelDisplay] Tile coordinates out of bounds! TilePos:({tileX},{tileY}), TileSize:({tileWidth},{tileHeight}), SheetSize:({tileSheet.width},{tileSheet.height})");
                return;
            }

            Color[] tilePixelsColor = tileSheet.GetPixels(tileX, tileY, tileWidth, tileHeight);
            Color32[] tilePixels = new Color32[tilePixelsColor.Length];
            for (int i = 0; i < tilePixelsColor.Length; i++)
            {
                tilePixels[i] = tilePixelsColor[i];
            }
            
            // 最初の数ピクセルの値をデバッグ出力
            if (tilePixels.Length > 0)
            {
                Debug.Log($"[TiledPixelDisplay] Tile {tileIndex} - Extracted {tilePixels.Length} pixels (expected {tileWidth * tileHeight})");
                Debug.Log($"[TiledPixelDisplay] Tile {tileIndex} sample pixels - [0]: R={tilePixels[0].r}, G={tilePixels[0].g}, B={tilePixels[0].b}, A={tilePixels[0].a}");
                if (tilePixels.Length > 32)
                {
                    Debug.Log($"[TiledPixelDisplay] Tile {tileIndex} sample pixels - [32]: R={tilePixels[32].r}, G={tilePixels[32].g}, B={tilePixels[32].b}, A={tilePixels[32].a}");
                }
            }

            // スケーリングして描画（縦横独立スケール + 液晶エフェクト）
            int scaledWidth = tileWidth * scaleX;
            int scaledHeight = tileHeight * scaleY;
            
            int pixelsDrawn = 0;

            for (int py = 0; py < scaledHeight; py++)
            {
                for (int px = 0; px < scaledWidth; px++)
                {
                    int sourceX = px / scaleX;
                    int sourceY = py / scaleY;
                    int sourceIndex = sourceY * tileWidth + sourceX;

                    Color32 pixel = tilePixels[sourceIndex];
                    
                    // 輝度で判定（明るいピクセル = 数字、暗いピクセル = 背景）
                    int brightness = (pixel.r + pixel.g + pixel.b) / 3;
                    bool shouldDraw = brightness > 128; // 128超の明るいピクセルを描画
                    
                    if (shouldDraw)
                    {
                        int destX = x + px;
                        int destY = y + py;
                        
                        if (destX >= 0 && destX < displayWidth && destY >= 0 && destY < displayHeight)
                        {
                            Color32 finalColor;
                            
                            if (enableLCDEffect)
                            {
                                // 液晶エフェクト適用（textColorベース）
                                finalColor = ApplyLCDEffect(px, py, scaleX, scaleY, destX, destY);
                            }
                            else
                            {
                                // 通常描画（textColorを使用）
                                finalColor = new Color32(
                                    (byte)(textColor.r * 255),
                                    (byte)(textColor.g * 255),
                                    (byte)(textColor.b * 255),
                                    255
                                );
                            }
                            
                            displayTexture.SetPixel(destX, destY, finalColor);
                            pixelsDrawn++;
                            
                            // グロー効果（周囲のピクセルを薄く光らせる）
                            if (enableLCDEffect && glowIntensity > 0)
                            {
                                ApplyGlowEffect(destX, destY, finalColor);
                            }
                        }
                    }
                }
            }
            
            // ピクセル描画数の確認（エラー時のみログ）
            if (pixelsDrawn == 0)
            {
                Debug.LogWarning($"[TiledPixelDisplay] No pixels drawn for digit {tileIndex}! Tile sheet may be empty or format incorrect.");
            }
        }

        /// <summary>
        /// アイコンを描画
        /// </summary>
        private void DrawIcon(int x, int y, int scaleX, int scaleY)
        {
            if (iconTexture == null) return;
            
            int iconWidth = iconTexture.width;
            int iconHeight = iconTexture.height;
            
            // アイコンのピクセルを取得
            Color[] iconPixelsColor = iconTexture.GetPixels();
            Color32[] iconPixels = new Color32[iconPixelsColor.Length];
            for (int i = 0; i < iconPixelsColor.Length; i++)
            {
                iconPixels[i] = iconPixelsColor[i];
            }
            
            int scaledWidth = iconWidth * scaleX;
            int scaledHeight = iconHeight * scaleY;
            int pixelsDrawn = 0;
            
            for (int py = 0; py < scaledHeight; py++)
            {
                for (int px = 0; px < scaledWidth; px++)
                {
                    int sourceX = px / scaleX;
                    int sourceY = py / scaleY;
                    int sourceIndex = sourceY * iconWidth + sourceX;
                    
                    if (sourceIndex >= iconPixels.Length) continue;
                    
                    Color32 pixel = iconPixels[sourceIndex];
                    
                    // アルファ値で透明度判定
                    if (pixel.a < 128) continue;
                    
                    // 輝度で判定（明るいピクセルを描画）
                    int brightness = (pixel.r + pixel.g + pixel.b) / 3;
                    bool shouldDraw = brightness > 128;
                    
                    if (shouldDraw)
                    {
                        int destX = x + px;
                        int destY = y + py;
                        
                        if (destX >= 0 && destX < displayWidth && destY >= 0 && destY < displayHeight)
                        {
                            Color32 finalColor;
                            
                            if (enableLCDEffect)
                            {
                                // 液晶エフェクト適用
                                finalColor = ApplyLCDEffect(px, py, scaleX, scaleY, destX, destY);
                            }
                            else
                            {
                                // 通常描画（textColorを使用）
                                finalColor = new Color32(
                                    (byte)(textColor.r * 255),
                                    (byte)(textColor.g * 255),
                                    (byte)(textColor.b * 255),
                                    255
                                );
                            }
                            
                            displayTexture.SetPixel(destX, destY, finalColor);
                            pixelsDrawn++;
                            
                            // グロー効果
                            if (enableLCDEffect && glowIntensity > 0)
                            {
                                ApplyGlowEffect(destX, destY, finalColor);
                            }
                        }
                    }
                }
            }
            
            if (pixelsDrawn > 0)
            {
                Debug.Log($"[TiledPixelDisplay] Drew {pixelsDrawn} pixels for icon");
            }
        }

        /// <summary>
        /// 液晶エフェクトを適用
        /// </summary>
        private Color32 ApplyLCDEffect(int px, int py, int scaleX, int scaleY, int destX, int destY)
        {
            float normalizedX = (px % scaleX) / (float)scaleX;
            float normalizedY = (py % scaleY) / (float)scaleY;
            
            // ピクセル間ギャップ効果
            float gapFactor = 1.0f;
            if (pixelGap > 0)
            {
                // 中央付近は明るく、端は暗く
                float gapX = Mathf.Abs(normalizedX - 0.5f) * 2f; // 0(中央) to 1(端)
                float gapY = Mathf.Abs(normalizedY - 0.5f) * 2f;
                float gap = Mathf.Max(gapX, gapY);
                
                if (gap > (1f - pixelGap))
                {
                    gapFactor = 1f - ((gap - (1f - pixelGap)) / pixelGap);
                    gapFactor = Mathf.Clamp01(gapFactor);
                }
            }
            
            // エッジグラデーション効果（数字のフチを滑らかに）
            float edgeFactor = 1.0f;
            if (enableEdgeGradient)
            {
                // ピクセルの端からの距離に基づくグラデーション
                float edgeX = Mathf.Abs(normalizedX - 0.5f) * 2f; // 0(中央) to 1(端)
                float edgeY = Mathf.Abs(normalizedY - 0.5f) * 2f;
                float distanceFromEdge = Mathf.Max(edgeX, edgeY); // 端に近いほど1
                
                // 端に近いほど暗くする
                edgeFactor = 1f - (distanceFromEdge * edgeGradientStrength);
                edgeFactor = Mathf.Clamp01(edgeFactor);
            }
            
            // スキャンライン効果（横線）
            float scanlineFactor = 1.0f;
            if (enableScanlines)
            {
                int linePosition = destY % scanlineWidth;
                
                if (linePosition < scanlineThickness)
                {
                    if (scanlineGradient)
                    {
                        // グラデーション：ラインの中心が最も暗く、端に向かって明るくなる
                        float normalizedPos = linePosition / (float)scanlineThickness;
                        
                        // 中心からの距離を計算（0.5が中心）
                        float distanceFromCenter = Mathf.Abs(normalizedPos - 0.5f) * 2f; // 0(中心) to 1(端)
                        
                        // 中心で最大、端で0になるグラデーション
                        float gradientFactor = 1f - distanceFromCenter;
                        
                        scanlineFactor = 1f - (scanlineIntensity * gradientFactor);
                    }
                    else
                    {
                        // グラデーションなし：一定の暗さ
                        scanlineFactor = 1f - scanlineIntensity;
                    }
                }
            }
            
            // 色温度調整（青緑がかり - 液晶風）
            float r = 1.0f - colorTint * 0.3f; // 赤を少し減らす
            float g = 1.0f - colorTint * 0.1f; // 緑をわずかに減らす
            float b = 1.0f; // 青は維持
            
            // 最終色を計算（textColorをベースに）
            float finalIntensity = gapFactor * edgeFactor * scanlineFactor;
            byte red = (byte)(textColor.r * 255 * r * finalIntensity);
            byte green = (byte)(textColor.g * 255 * g * finalIntensity);
            byte blue = (byte)(textColor.b * 255 * b * finalIntensity);
            
            return new Color32(red, green, blue, 255);
        }

        /// <summary>
        /// グロー効果を適用（周囲のピクセルを薄く光らせる）
        /// </summary>
        private void ApplyGlowEffect(int centerX, int centerY, Color32 centerColor)
        {
            int glowRadius = 1; // グロー半径
            
            for (int dy = -glowRadius; dy <= glowRadius; dy++)
            {
                for (int dx = -glowRadius; dx <= glowRadius; dx++)
                {
                    if (dx == 0 && dy == 0) continue; // 中心は既に描画済み
                    
                    int glowX = centerX + dx;
                    int glowY = centerY + dy;
                    
                    if (glowX >= 0 && glowX < displayWidth && glowY >= 0 && glowY < displayHeight)
                    {
                        // 距離に応じた減衰
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        float attenuation = Mathf.Clamp01(1f - (distance / (glowRadius + 1f)));
                        float glowStrength = glowIntensity * attenuation;
                        
                        // 既存ピクセルと加算合成
                        Color32 existingPixel = displayTexture.GetPixel(glowX, glowY);
                        
                        byte newR = (byte)Mathf.Min(255, existingPixel.r + centerColor.r * glowStrength);
                        byte newG = (byte)Mathf.Min(255, existingPixel.g + centerColor.g * glowStrength);
                        byte newB = (byte)Mathf.Min(255, existingPixel.b + centerColor.b * glowStrength);
                        
                        displayTexture.SetPixel(glowX, glowY, new Color32(newR, newG, newB, 255));
                    }
                }
            }
        }

        /// <summary>
        /// テクスチャを更新
        /// </summary>
        private void UpdateTexture()
        {
            displayTexture.Apply();
        }
        
        /// <summary>
        /// アウトライングローを適用（文字のエッジを検出してグローさせる）- 最適化版
        /// </summary>
        private void ApplyOutlineGlow()
        {
            if (outlineGlowRadius <= 0) return;
            
            // テクスチャ全体を一度に取得（バッチ処理）
            Color32[] pixels = displayTexture.GetPixels32();
            Color32[] glowBuffer = new Color32[pixels.Length];
            System.Array.Copy(pixels, glowBuffer, pixels.Length);
            
            int texWidth = displayTexture.width;
            
            // エッジ検出とグロー適用を同時に実行（最適化）
            for (int y = 0; y < displayHeight; y++)
            {
                for (int x = 0; x < displayWidth; x++)
                {
                    int index = y * texWidth + x;
                    if (index >= pixels.Length) continue;
                    
                    Color32 pixel = pixels[index];
                    
                    // 白いピクセル（文字部分）かチェック
                    bool isLit = (pixel.r + pixel.g + pixel.b) > 128 * 3;
                    
                    if (!isLit) continue;
                    
                    // 簡易エッジ判定（4方向のみチェック - 高速化）
                    bool isEdge = false;
                    int[] dx = { -1, 1, 0, 0 };
                    int[] dy = { 0, 0, -1, 1 };
                    
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int nx = x + dx[dir];
                        int ny = y + dy[dir];
                        
                        if (nx >= 0 && nx < displayWidth && ny >= 0 && ny < displayHeight)
                        {
                            int neighborIndex = ny * texWidth + nx;
                            if (neighborIndex >= pixels.Length) continue;
                            
                            Color32 neighborPixel = pixels[neighborIndex];
                            bool neighborIsLit = (neighborPixel.r + neighborPixel.g + neighborPixel.b) > 128 * 3;
                            
                            if (!neighborIsLit)
                            {
                                isEdge = true;
                                break;
                            }
                        }
                    }
                    
                    if (!isEdge) continue;
                    
                    // このエッジピクセルの周囲にグローを適用（最小範囲のみ）
                    int minX = Mathf.Max(0, x - outlineGlowRadius);
                    int maxX = Mathf.Min(displayWidth - 1, x + outlineGlowRadius);
                    int minY = Mathf.Max(0, y - outlineGlowRadius);
                    int maxY = Mathf.Min(displayHeight - 1, y + outlineGlowRadius);
                    
                    for (int gy = minY; gy <= maxY; gy++)
                    {
                        for (int gx = minX; gx <= maxX; gx++)
                        {
                            int dx2 = gx - x;
                            int dy2 = gy - y;
                            float distance = Mathf.Sqrt(dx2 * dx2 + dy2 * dy2);
                            
                            if (distance > outlineGlowRadius) continue;
                            
                            float attenuation = 1f - (distance / (outlineGlowRadius + 1f));
                            float glowStrength = outlineGlowIntensity * attenuation;
                            
                            int glowIndex = gy * texWidth + gx;
                            if (glowIndex >= glowBuffer.Length) continue;
                            
                            Color32 existingPixel = glowBuffer[glowIndex];
                            
                            glowBuffer[glowIndex] = new Color32(
                                (byte)Mathf.Min(255, existingPixel.r + outlineGlowColor.r * 255 * glowStrength),
                                (byte)Mathf.Min(255, existingPixel.g + outlineGlowColor.g * 255 * glowStrength),
                                (byte)Mathf.Min(255, existingPixel.b + outlineGlowColor.b * 255 * glowStrength),
                                255
                            );
                        }
                    }
                }
            }
            
            // 一括でテクスチャに書き戻し（高速化）
            displayTexture.SetPixels32(glowBuffer);
        }
        #endregion

        #region Validation
        /// <summary>
        /// 初期化状態を検証
        /// </summary>
        private bool ValidateInitialization()
        {
            if (!isInitialized)
            {
                Debug.LogWarning($"[TiledPixelDisplay] {gameObject.name} は初期化されていません");
                return false;
            }
            return true;
        }
        #endregion

        #region Public Accessors
        /// <summary>
        /// 初期化状態を取得
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// ディスプレイテクスチャを取得
        /// </summary>
        public Texture2D DisplayTexture => displayTexture;
        #endregion

        #region Cleanup
        private void OnDestroy()
        {
            if (displayTexture != null)
            {
                Destroy(displayTexture);
            }

            if (displayMaterial != null)
            {
                Destroy(displayMaterial);
            }
        }
        #endregion
    }
}

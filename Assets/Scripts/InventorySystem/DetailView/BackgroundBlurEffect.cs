using UnityEngine;
using UnityEngine.UI;

namespace InventorySystem
{
    /// <summary>
    /// RenderTexture を使用した背景ぼかし効果（Built-in RP 対応）
    /// </summary>
    public class BackgroundBlurEffect : MonoBehaviour
    {
        [Header("UI要素")]
        [SerializeField] private Image blurOverlay;
        [SerializeField] private RawImage blurredBackgroundImage;
        
        [Header("ブラー設定")]
        [SerializeField, Range(5, 50)] private int blurRadius = 10;
        [SerializeField, Range(0.1f, 5f)] private float blurIntensity = 1f;
        [SerializeField, Range(1, 8)] private int downsampling = 2;
        [SerializeField] private string previewCardLayerName = "PreviewCard"; // プレビューカード用レイヤー（ブラーから除外）
        
        [Header("フェード設定")]
        [SerializeField] private float fadeDuration = 0.2f;
        [SerializeField] private Color overlayColor = new Color(0, 0, 0, 0.3f);
        
        private RenderTexture sourceTexture;
        private RenderTexture blurTexture;
        private Material blurMaterial;
        private Camera targetCamera;
        private bool isBlurred = false;
        private float currentAlpha = 0f;
        private Canvas parentCanvas;
        private int originalSortingOrder = 0;
        
        void Start()
        {
            // Canvas を取得（自身が Canvas の子要素であることを前提）
            parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                Debug.LogError("[BackgroundBlurEffect] Canvas が見つかりません");
                return;
            }
            
            // Canvas の初期 Sorting Order を保存
            originalSortingOrder = parentCanvas.sortingOrder;
            
            // Canvas RenderMode をログ出力（デバッグ用）
            Debug.Log($"[BackgroundBlurEffect] Canvas RenderMode: {parentCanvas.renderMode}");
            
            // Canvas が Screen Space Overlay の場合は警告
            if (parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                Debug.LogWarning("[BackgroundBlurEffect] Canvas は Screen Space Overlay です。3D オブジェクトが背後に描画されます。Screen Space Camera に変更してください。");
            }
            
            // BlurOverlay がない場合は自動生成
            if (blurOverlay == null)
            {
                blurOverlay = CreateBlurOverlay(parentCanvas.transform);
                Debug.Log("[BackgroundBlurEffect] BlurOverlay を自動生成しました");
            }
            else
            {
                blurOverlay.gameObject.SetActive(false);
            }
            
            // BlurredBackgroundImage がない場合は自動生成
            if (blurredBackgroundImage == null)
            {
                blurredBackgroundImage = CreateBlurredBackgroundImage(parentCanvas.transform);
                Debug.Log("[BackgroundBlurEffect] BlurredBackgroundImage を自動生成しました");
            }
            else
            {
                blurredBackgroundImage.gameObject.SetActive(false);
            }
            
            // カメラを取得
            targetCamera = Camera.main;
            if (targetCamera == null)
            {
                Debug.LogError("[BackgroundBlurEffect] メインカメラが見つかりません");
            }
            
            // ブラーマテリアルを作成
            CreateBlurMaterial();
        }
        
        /// <summary>
        /// BlurOverlay（Image）を自動生成
        /// </summary>
        private Image CreateBlurOverlay(Transform parentCanvas)
        {
            GameObject overlayObj = new GameObject("BlurOverlay");
            overlayObj.transform.SetParent(parentCanvas, false);
            
            Image image = overlayObj.AddComponent<Image>();
            image.color = overlayColor;
            
            RectTransform rectTransform = overlayObj.GetComponent<RectTransform>();
            SetFullScreenRect(rectTransform);
            
            // Canvas に Image を追加した場合、GraphicRaycaster との互換性を確保
            if (parentCanvas.GetComponent<GraphicRaycaster>() == null)
            {
                parentCanvas.gameObject.AddComponent<GraphicRaycaster>();
            }
            
            overlayObj.SetActive(false);
            return image;
        }
        
        /// <summary>
        /// BlurredBackgroundImage（RawImage）を自動生成
        /// </summary>
        private RawImage CreateBlurredBackgroundImage(Transform parentCanvas)
        {
            GameObject rawImageObj = new GameObject("BlurredBackgroundImage");
            rawImageObj.transform.SetParent(parentCanvas, false);
            
            // BlurOverlay より後ろに配置（先に描画）
            rawImageObj.transform.SetAsFirstSibling();
            
            RawImage rawImage = rawImageObj.AddComponent<RawImage>();
            rawImage.color = Color.white;
            
            RectTransform rectTransform = rawImageObj.GetComponent<RectTransform>();
            SetFullScreenRect(rectTransform);
            
            rawImageObj.SetActive(false);
            return rawImage;
        }
        
        /// <summary>
        /// RectTransform を Full Screen に設定
        /// </summary>
        private void SetFullScreenRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }
        
        void CreateBlurMaterial()
        {
            // シンプルなブラーシェーダーをコード生成
            Shader blurShader = Shader.Find("Hidden/FastBlur");
            if (blurShader == null)
            {
                // デフォルトシェーダーで代用
                blurShader = Shader.Find("UI/Default");
            }
            blurMaterial = new Material(blurShader);
        }
        
        /// <summary>
        /// ぼかしを有効化
        /// </summary>
        public void EnableBlur()
        {
            if (!isBlurred)
            {
                isBlurred = true;
                
                if (blurOverlay != null)
                {
                    blurOverlay.gameObject.SetActive(true);
                }
                
                if (blurredBackgroundImage != null)
                {
                    blurredBackgroundImage.gameObject.SetActive(true);
                }
                
                // Canvas の Sorting Order を低く設定（プレビューカードを手前に）
                if (parentCanvas != null)
                {
                    parentCanvas.sortingOrder = -1;
                }
                
                // 次のフレームで背景をキャプチャ
                if (targetCamera != null)
                {
                    CaptureAndBlur();
                }
            }
        }
        
        /// <summary>
        /// ぼかしを無効化
        /// </summary>
        public void DisableBlur()
        {
            isBlurred = false;
            
            if (blurOverlay != null)
            {
                blurOverlay.gameObject.SetActive(false);
            }
            
            if (blurredBackgroundImage != null)
            {
                blurredBackgroundImage.gameObject.SetActive(false);
                blurredBackgroundImage.texture = null; // RenderTexture解放後の参照クリア
            }
            
            // Canvas の Sorting Order を元に戻す
            if (parentCanvas != null)
            {
                parentCanvas.sortingOrder = originalSortingOrder;
            }

            // RenderTextureを解放（Disable時にもリークを防止）
            if (sourceTexture != null)
            {
                RenderTexture.ReleaseTemporary(sourceTexture);
                sourceTexture = null;
            }
            if (blurTexture != null)
            {
                RenderTexture.ReleaseTemporary(blurTexture);
                blurTexture = null;
            }
        }
        
        void CaptureAndBlur()
        {
            if (targetCamera == null) return;
            
            // プレビューカードレイヤーの culling mask を計算
            int previewCardLayer = LayerMask.NameToLayer(previewCardLayerName);
            int originalMask = targetCamera.cullingMask;
            
            // ブラーキャプチャ時はプレビューカードを除外
            int blurCaptureMask = originalMask & ~(1 << previewCardLayer);
            
            int width = Screen.width / downsampling;
            int height = Screen.height / downsampling;
            
            // RenderTexture を作成
            if (sourceTexture != null)
            {
                RenderTexture.ReleaseTemporary(sourceTexture);
            }
            sourceTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            
            // Canvas を一時的に無効化（キャプチャに含めない）
            bool wasCanvasEnabled = parentCanvas != null && parentCanvas.enabled;
            if (wasCanvasEnabled)
            {
                parentCanvas.enabled = false;
            }
            
            // カメラから背景をキャプチャ（プレビューカード除外）
            RenderTexture previousRT = targetCamera.targetTexture;
            targetCamera.targetTexture = sourceTexture;
            targetCamera.cullingMask = blurCaptureMask;
            targetCamera.Render();
            targetCamera.targetTexture = previousRT;
            targetCamera.cullingMask = originalMask; // マスクを復元
            
            // Canvas を再有効化
            if (wasCanvasEnabled && parentCanvas != null)
            {
                parentCanvas.enabled = true;
            }
            
            // 強力なブラー処理（複数パス × Blur Radius倍）
            if (blurTexture != null)
            {
                RenderTexture.ReleaseTemporary(blurTexture);
            }
            blurTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            
            // Blur Radius × 2 回のブラーパスを実行（ガッツリブラー）
            int totalPasses = blurRadius * 2;
            for (int i = 0; i < totalPasses; i++)
            {
                // 奇数回目は sourceTexture → blurTexture
                // 偶数回目は blurTexture → sourceTexture
                if (i % 2 == 0)
                {
                    Graphics.Blit(sourceTexture, blurTexture);
                }
                else
                {
                    Graphics.Blit(blurTexture, sourceTexture);
                }
            }
            
            // 最終結果は sourceTexture に格納されている
            // UI Image にテクスチャを割り当て
            if (blurredBackgroundImage != null)
            {
                blurredBackgroundImage.texture = sourceTexture;
            }
            
            Debug.Log($"[BackgroundBlurEffect] ブラー処理完了: パス数={totalPasses}, ダウンサンプリング={downsampling}x, プレビューカード除外レイヤー={previewCardLayerName}");
        }
        
        void Update()
        {
            // オーバーレイのフェード処理
            if (blurOverlay != null)
            {
                float targetAlpha = isBlurred ? overlayColor.a : 0f;
                currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.deltaTime / fadeDuration);
                
                Color color = overlayColor;
                color.a = currentAlpha;
                blurOverlay.color = color;
            }
        }
        
        void OnDestroy()
        {
            // RenderTexture をクリーンアップ
            if (sourceTexture != null)
            {
                RenderTexture.ReleaseTemporary(sourceTexture);
            }
            if (blurTexture != null)
            {
                RenderTexture.ReleaseTemporary(blurTexture);
            }
            if (blurMaterial != null)
            {
                Destroy(blurMaterial);
            }
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace InventorySystem
{
    /// <summary>
    /// 3D平面モデルにテキストを描画するシステム
    /// RenderTextureを使用してテキストを生成し、平面に適用
    /// </summary>
    public class TextRenderer3D : MonoBehaviour
    {
        [Header("テキスト設定")]
        [SerializeField] private string displayText = "Sample Text";
        [SerializeField] private Font customFont;
        [SerializeField] private TMP_FontAsset tmpFont; // TextMeshPro用フォント
        [SerializeField] [Range(12, 200)] private int fontSize = 24;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color backgroundColor = Color.clear;
        
        [Header("描画設定")]
        [SerializeField] private TextAnchor textAlignment = TextAnchor.MiddleCenter;
        [SerializeField] private int renderTextureWidth = 512;
        [SerializeField] private int renderTextureHeight = 256;
        [SerializeField] private bool useTextMeshPro = true;
        
        [Header("平面設定")]
        [SerializeField] private GameObject targetPlane; // テキストを適用する平面
        [SerializeField] private bool autoCreatePlane = true;
        [SerializeField] private Vector3 planeScale = Vector3.one;
        [SerializeField] private string materialName = "TextMaterial";
        
        // 内部コンポーネント
        private RenderTexture renderTexture;
        private Camera renderCamera;
        private Canvas renderCanvas;
        private GameObject textObject;
        private Material textMaterial;
        
        void Start()
        {
            InitializeTextRenderer();
        }
        
        /// <summary>
        /// チE��ストレンダラーシスチE��を�E期化
        /// </summary>
        private void InitializeTextRenderer()
        {
            CreateRenderTexture();
            CreateRenderCamera();
            CreateRenderCanvas();
            CreateTextObject();
            
            if (targetPlane == null && autoCreatePlane)
            {
                CreateTargetPlane();
            }
            
            if (targetPlane != null)
            {
                ApplyTextToPlane();
            }
            
            RenderText();
        }
        
        /// <summary>
        /// RenderTextureを作�E
        /// </summary>
        private void CreateRenderTexture()
        {
            renderTexture = new RenderTexture(renderTextureWidth, renderTextureHeight, 0);
            renderTexture.format = RenderTextureFormat.ARGB32;
            renderTexture.name = $"TextRenderTexture_{gameObject.name}";
        }
        
        /// <summary>
        /// レンダリング用カメラを作�E
        /// </summary>
        private void CreateRenderCamera()
        {
            GameObject cameraObj = new GameObject($"TextRenderCamera_{gameObject.name}");
            cameraObj.transform.SetParent(transform);
            
            renderCamera = cameraObj.AddComponent<Camera>();
            renderCamera.targetTexture = renderTexture;
            renderCamera.orthographic = true;
            renderCamera.orthographicSize = 1;
            renderCamera.clearFlags = CameraClearFlags.SolidColor;
            renderCamera.backgroundColor = backgroundColor;
            renderCamera.cullingMask = 1 << 31; // 専用レイヤーを使用
            renderCamera.depth = -100; // メインカメラより先に描画
            
            cameraObj.transform.position = new Vector3(0, 0, -10);
            cameraObj.SetActive(false); // 通常時�E非表示
        }
        
        /// <summary>
        /// レンダリング用Canvasを作�E
        /// </summary>
        private void CreateRenderCanvas()
        {
            GameObject canvasObj = new GameObject($"TextRenderCanvas_{gameObject.name}");
            canvasObj.transform.SetParent(renderCamera.transform);
            canvasObj.layer = 31; // 専用レイヤー
            
            renderCanvas = canvasObj.AddComponent<Canvas>();
            renderCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            renderCanvas.worldCamera = renderCamera;
            renderCanvas.planeDistance = 1;
            
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(renderTextureWidth, renderTextureHeight);
            
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        
        /// <summary>
        /// チE��ストオブジェクトを作�E
        /// </summary>
        private void CreateTextObject()
        {
            GameObject textObj = new GameObject($"TextObject_{gameObject.name}");
            textObj.transform.SetParent(renderCanvas.transform, false);
            textObj.layer = 31;
            
            // RectTransformを�E示皁E��追加
            RectTransform rectTransform = textObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            
            if (useTextMeshPro)
            {
                CreateTextMeshProComponent(textObj, rectTransform);
            }
            else
            {
                CreateUnityTextComponent(textObj, rectTransform);
            }
            
            textObject = textObj;
        }
        
        /// <summary>
        /// TextMeshProコンポ�Eネントを作�E
        /// </summary>
        private void CreateTextMeshProComponent(GameObject textObj, RectTransform rectTransform)
        {
            TextMeshProUGUI textComponent = textObj.AddComponent<TextMeshProUGUI>();
            textComponent.text = displayText;
            textComponent.font = tmpFont;
            textComponent.fontSize = fontSize;
            textComponent.color = textColor;
            textComponent.alignment = ConvertToTMPAlignment(textAlignment);
            textComponent.enableWordWrapping = true;
        }
        
        /// <summary>
        /// Unity標準Textコンポ�Eネントを作�E
        /// </summary>
        private void CreateUnityTextComponent(GameObject textObj, RectTransform rectTransform)
        {
            Text textComponent = textObj.AddComponent<Text>();
            textComponent.text = displayText;
            textComponent.font = customFont != null ? customFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComponent.fontSize = fontSize;
            textComponent.color = textColor;
            textComponent.alignment = textAlignment;
            textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComponent.verticalOverflow = VerticalWrapMode.Truncate;
        }
        
        /// <summary>
        /// TextAnchorをTextMeshProのアライメントに変換
        /// </summary>
        private TextAlignmentOptions ConvertToTMPAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }
        
        /// <summary>
        /// ターゲチE��平面を作�E
        /// </summary>
        private void CreateTargetPlane()
        {
            targetPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            targetPlane.name = $"TextPlane_{gameObject.name}";
            targetPlane.transform.SetParent(transform);
            targetPlane.transform.localPosition = Vector3.zero;
            targetPlane.transform.localRotation = Quaternion.identity;
            targetPlane.transform.localScale = planeScale;
            
            // チE��ォルト�EColliderを削除�E�忁E��に応じて�E�E
            Collider collider = targetPlane.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyImmediate(collider);
            }
        }
        
        /// <summary>
        /// チE��ストを平面に適用
        /// </summary>
        private void ApplyTextToPlane()
        {
            if (targetPlane == null) return;
            
            Renderer planeRenderer = targetPlane.GetComponent<Renderer>();
            if (planeRenderer == null) return;
            
            // チE��スト用マテリアルを作�E
            textMaterial = new Material(Shader.Find("Standard"));
            textMaterial.name = materialName;
            textMaterial.mainTexture = renderTexture;
            
            // 透�E度サポ�EチE
            if (backgroundColor.a < 1.0f)
            {
                textMaterial.SetFloat("_Mode", 3); // Transparent mode
                textMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                textMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                textMaterial.SetInt("_ZWrite", 0);
                textMaterial.DisableKeyword("_ALPHATEST_ON");
                textMaterial.EnableKeyword("_ALPHABLEND_ON");
                textMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                textMaterial.renderQueue = 3000;
            }
            
            planeRenderer.material = textMaterial;
        }
        
        /// <summary>
        /// チE��ストをレンダリング
        /// </summary>
        public void RenderText()
        {
            if (renderCamera == null) return;
            
            renderCamera.gameObject.SetActive(true);
            renderCamera.Render();
            renderCamera.gameObject.SetActive(false);
        }
        
        /// <summary>
        /// 表示チE��ストを更新
        /// </summary>
        public void UpdateDisplayText(string newText)
        {
            displayText = newText;
            
            if (textObject != null)
            {
                if (useTextMeshPro)
                {
                    TextMeshProUGUI tmpText = textObject.GetComponent<TextMeshProUGUI>();
                    if (tmpText != null) tmpText.text = displayText;
                }
                else
                {
                    Text unityText = textObject.GetComponent<Text>();
                    if (unityText != null) unityText.text = displayText;
                }
                
                RenderText();
            }
        }
        
        /// <summary>
        /// フォントサイズを更新
        /// </summary>
        public void UpdateFontSize(int newSize)
        {
            fontSize = newSize;
            
            if (textObject != null)
            {
                if (useTextMeshPro)
                {
                    TextMeshProUGUI tmpText = textObject.GetComponent<TextMeshProUGUI>();
                    if (tmpText != null) tmpText.fontSize = fontSize;
                }
                else
                {
                    Text unityText = textObject.GetComponent<Text>();
                    if (unityText != null) unityText.fontSize = fontSize;
                }
                
                RenderText();
            }
        }
        
        /// <summary>
        /// チE��スト色を更新
        /// </summary>
        public void UpdateTextColor(Color newColor)
        {
            textColor = newColor;
            
            if (textObject != null)
            {
                if (useTextMeshPro)
                {
                    TextMeshProUGUI tmpText = textObject.GetComponent<TextMeshProUGUI>();
                    if (tmpText != null) tmpText.color = textColor;
                }
                else
                {
                    Text unityText = textObject.GetComponent<Text>();
                    if (unityText != null) unityText.color = textColor;
                }
                
                RenderText();
            }
        }
        
        /// <summary>
        /// 手動でチE��ストを再描画�E�Enspector上での変更を反映�E�E
        /// </summary>
        [ContextMenu("Refresh Text Display")]
        public void RefreshDisplay()
        {
            if (Application.isPlaying)
            {
                UpdateDisplayText(displayText);
            }
        }
        
        void OnDestroy()
        {
            // リソースのクリーンアチE�E
            if (renderTexture != null)
            {
                renderTexture.Release();
                DestroyImmediate(renderTexture);
            }
            
            if (textMaterial != null)
            {
                DestroyImmediate(textMaterial);
            }
        }
        
        /// <summary>
        /// エチE��タ上でのリアルタイム更新
        /// </summary>
        void OnValidate()
        {
            if (Application.isPlaying && textObject != null)
            {
                // 次のフレームで更新
                StartCoroutine(DelayedUpdate());
            }
        }
        
        private System.Collections.IEnumerator DelayedUpdate()
        {
            yield return null; // 1フレーム征E��E
            RefreshDisplay();
        }
    }
}

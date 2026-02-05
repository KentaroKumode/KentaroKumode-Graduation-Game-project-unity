using UnityEngine;
using TMPro;

namespace InventorySystem
{
    /// <summary>
    /// 最適化された3D空間テキスト表示システム
    /// TextMeshPro 3Dを使用してパフォーマンス最適化
    /// </summary>
    public class Optimized3DTextRenderer : MonoBehaviour
    {
        [Header("3Dテキスト設定")]
        [SerializeField] private string displayText = "Sample Text";
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] [Range(0.1f, 10f)] private float fontSize = 2f; // 3D空間でのサイズ
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Material textMaterial; // カスタムマテリアル（オプション）
        
        [Header("配置設定")]
        [SerializeField] private Vector3 worldOffset = new Vector3(0, 0, 0.1f); // 背景から少し前に配置
        [SerializeField] private bool lookAtCamera = true; // カメラを向く
        [SerializeField] private bool keepUpright = true; // 上向きを維持
        [SerializeField] private TextAlignmentOptions alignment = TextAlignmentOptions.Center;
        
        [Header("最適化設定")]
        [SerializeField] private bool enableCulling = true; // カリング有効
        [SerializeField] private float cullingDistance = 50f; // カリング距離
        [SerializeField] private bool useObjectPooling = true; // オブジェクトプール使用
        [SerializeField] private LayerMask textLayer = 1 << 5; // テキスト専用レイヤー
        
        [Header("背景プレーン設定")]
        [SerializeField] private bool enableBackground = true; // 背景プレーン有効
        [SerializeField] private Color backgroundColor = Color.green; // テキスト用真緑色
        [SerializeField] private Vector2 backgroundPadding = new Vector2(0.2f, 0.1f);
        
        private TextMeshPro textMeshPro;
        private GameObject backgroundPlane;
        private Renderer backgroundRenderer;
        private Camera targetCamera;
        private bool isInitialized = false;
        
        // 最適化用
        private Vector3 lastCameraPosition;
        private Quaternion lastCameraRotation;
        private bool isVisible = true;
        
        void Start()
        {
            InitializeTextRenderer();
        }
        
        void LateUpdate()
        {
            if (!isInitialized || !isVisible) return;
            
            UpdateCameraFacing();
            UpdateCulling();
        }
        
        /// <summary>
        /// チE��ストレンダラーを�E期化
        /// </summary>
        private void InitializeTextRenderer()
        {
            if (isInitialized) return;
            
            targetCamera = Camera.main;
            if (targetCamera == null)
            {
                Debug.LogWarning("[Optimized3DTextRenderer] Main camera not found");
                return;
            }
            
            CreateTextMeshPro();
            
            if (enableBackground)
            {
                CreateBackgroundPlane();
            }
            
            UpdateTextContent();
            isInitialized = true;
        }
        
        /// <summary>
        /// TextMeshPro 3Dオブジェクトを作�E
        /// </summary>
        private void CreateTextMeshPro()
        {
            GameObject textObj = new GameObject($"3DText_{gameObject.name}");
            textObj.transform.SetParent(transform);
            textObj.transform.localPosition = worldOffset;
            textObj.layer = GetLayerFromMask(textLayer);
            
            textMeshPro = textObj.AddComponent<TextMeshPro>();
            
            // TextMeshPro設宁E
            textMeshPro.text = displayText;
            textMeshPro.font = font;
            textMeshPro.fontSize = fontSize;
            textMeshPro.color = textColor;
            textMeshPro.alignment = alignment;
            textMeshPro.autoSizeTextContainer = true;
            
            // カスタムマテリアル適用
            if (textMaterial != null)
            {
                textMeshPro.fontSharedMaterial = textMaterial;
            }
            else
            {
                // チE��ォルトで最適化されたマテリアル設宁E
                Material defaultMaterial = textMeshPro.fontSharedMaterial;
                if (defaultMaterial != null)
                {
                    defaultMaterial.shader = Shader.Find("TextMeshPro/Distance Field");
                    defaultMaterial.EnableKeyword("_ALPHATEST_ON");
                }
            }
            
            // レンダリング設宁E
            textMeshPro.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textMeshPro.renderer.receiveShadows = false;
        }
        
        /// <summary>
        /// 背景プレーンを作�E
        /// </summary>
        private void CreateBackgroundPlane()
        {
            Debug.Log($"[Optimized3DTextRenderer] Creating background plane for: {gameObject.name}");
            
            backgroundPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            backgroundPlane.name = $"TextBackground_{gameObject.name}";
            backgroundPlane.transform.SetParent(transform);
            backgroundPlane.layer = GetLayerFromMask(textLayer);
            
            Debug.Log($"[Optimized3DTextRenderer] Background plane created: {backgroundPlane.name}");
            Debug.Log($"[Optimized3DTextRenderer] Background plane position: {backgroundPlane.transform.position}");
            Debug.Log($"[Optimized3DTextRenderer] Background plane active: {backgroundPlane.activeInHierarchy}");
            
            // プレーンのColliderを削除�E�不要E��E
            DestroyImmediate(backgroundPlane.GetComponent<Collider>());
            
            // 位置を文字より少し後ろに設置
            Vector3 bgOffset = worldOffset;
            bgOffset.z -= 0.01f; // 斁E���E後ろに配置
            backgroundPlane.transform.localPosition = bgOffset;
            
            Debug.Log($"[Optimized3DTextRenderer] Background plane local position set to: {bgOffset}");
            
            // マテリアル作�E
            backgroundRenderer = backgroundPlane.GetComponent<Renderer>();
            Material bgMaterial = CreateBackgroundMaterial();
            backgroundRenderer.material = bgMaterial;
            backgroundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            backgroundRenderer.receiveShadows = false;
            
            Debug.Log($"[Optimized3DTextRenderer] Background material color: {backgroundColor}");
            Debug.Log($"[Optimized3DTextRenderer] Background plane setup complete");
        }
        
        /// <summary>
        /// 背景用マテリアルを作�E
        /// </summary>
        private Material CreateBackgroundMaterial()
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = backgroundColor;
            
            // 透�E度サポ�EチE
            if (backgroundColor.a < 1f)
            {
                mat.SetFloat("_Mode", 3); // Transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.renderQueue = 3000;
            }
            
            // エミッション無効化（最適化！E
            mat.DisableKeyword("_EMISSION");
            
            return mat;
        }
        
        /// <summary>
        /// LayerMaskからレイヤー番号を取征E
        /// </summary>
        private int GetLayerFromMask(LayerMask layerMask)
        {
            int layerNumber = 0;
            int layer = layerMask.value;
            while (layer > 1)
            {
                layer >>= 1;
                layerNumber++;
            }
            return layerNumber;
        }
        
        /// <summary>
        /// カメラに向く処琁E
        /// </summary>
        private void UpdateCameraFacing()
        {
            if (!lookAtCamera || targetCamera == null) return;
            
            Vector3 cameraPosition = targetCamera.transform.position;
            Vector3 cameraRotation = targetCamera.transform.eulerAngles;
            
            // 最適化：カメラが動ぁE��ぁE��ぁE��合�EスキチE�E
            if (Vector3.Distance(cameraPosition, lastCameraPosition) < 0.01f &&
                Quaternion.Angle(targetCamera.transform.rotation, lastCameraRotation) < 0.1f)
            {
                return;
            }
            
            Vector3 lookDirection = cameraPosition - transform.position;
            
            if (keepUpright)
            {
                // Y軸回転のみ�E�上向きを維持E��E
                lookDirection.y = 0;
                if (lookDirection != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(lookDirection);
                }
            }
            else
            {
                // 完�Eにカメラを向ぁE
                if (lookDirection != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(lookDirection);
                }
            }
            
            lastCameraPosition = cameraPosition;
            lastCameraRotation = targetCamera.transform.rotation;
        }
        
        /// <summary>
        /// カリング処琁E
        /// </summary>
        private void UpdateCulling()
        {
            if (!enableCulling || targetCamera == null) return;
            
            float distanceToCamera = Vector3.Distance(transform.position, targetCamera.transform.position);
            bool shouldBeVisible = distanceToCamera <= cullingDistance;
            
            if (shouldBeVisible != isVisible)
            {
                SetVisibility(shouldBeVisible);
            }
        }
        
        /// <summary>
        /// 表示/非表示を設宁E
        /// </summary>
        private void SetVisibility(bool visible)
        {
            isVisible = visible;
            
            if (textMeshPro != null)
            {
                textMeshPro.gameObject.SetActive(visible);
            }
            
            if (backgroundPlane != null)
            {
                backgroundPlane.SetActive(visible);
            }
        }
        
        /// <summary>
        /// チE��スト�E容を更新
        /// </summary>
        public void UpdateText(string newText)
        {
            displayText = newText;
            if (textMeshPro != null)
            {
                textMeshPro.text = displayText;
                UpdateBackgroundSize();
            }
        }
        
        /// <summary>
        /// チE��スト�E容を更新�E�文字色も同時に変更�E�E
        /// </summary>
        public void UpdateText(string newText, Color color)
        {
            displayText = newText;
            textColor = color;
            
            if (textMeshPro != null)
            {
                textMeshPro.text = displayText;
                textMeshPro.color = textColor;
                UpdateBackgroundSize();
            }
        }
        
        /// <summary>
        /// 背景サイズを文字に合わせて調整
        /// </summary>
        private void UpdateBackgroundSize()
        {
            if (backgroundPlane == null || textMeshPro == null) 
            {
                Debug.LogWarning($"[Optimized3DTextRenderer] UpdateBackgroundSize failed - backgroundPlane: {backgroundPlane != null}, textMeshPro: {textMeshPro != null}");
                return;
            }
            
            // TextMeshProの墁E��を取征E
            Bounds textBounds = textMeshPro.bounds;
            
            Debug.Log($"[Optimized3DTextRenderer] UpdateBackgroundSize for: {gameObject.name}");
            Debug.Log($"[Optimized3DTextRenderer] Text bounds: {textBounds.size}");
            Debug.Log($"[Optimized3DTextRenderer] Background padding: {backgroundPadding}");
            
            // パディングを適用したサイズを計箁E
            Vector3 bgScale = new Vector3(
                (textBounds.size.x + backgroundPadding.x * 2f) * 0.1f, // プレーンサイズを�Eに戻す！E0%�E�E
                1f,
                (textBounds.size.y + backgroundPadding.y * 2f) * 0.1f
            );
            
            backgroundPlane.transform.localScale = bgScale;
            
            Debug.Log($"[Optimized3DTextRenderer] Background plane scale set to: {bgScale}");
            Debug.Log($"[Optimized3DTextRenderer] Background plane world scale: {backgroundPlane.transform.lossyScale}");
        }
        
        /// <summary>
        /// 位置オフセチE��を設宁E
        /// </summary>
        public void SetWorldOffset(Vector3 offset)
        {
            worldOffset = offset;
            
            if (textMeshPro != null)
            {
                textMeshPro.transform.localPosition = worldOffset;
            }
            
            if (backgroundPlane != null)
            {
                Vector3 bgOffset = worldOffset;
                bgOffset.z -= 0.01f;
                backgroundPlane.transform.localPosition = bgOffset;
            }
        }
        
        /// <summary>
        /// フォントサイズを設宁E
        /// </summary>
        public void SetFontSize(float size)
        {
            fontSize = size;
            if (textMeshPro != null)
            {
                textMeshPro.fontSize = fontSize;
                UpdateBackgroundSize();
            }
        }
        
        /// <summary>
        /// チE��スト色を設宁E
        /// </summary>
        public void SetTextColor(Color color)
        {
            textColor = color;
            if (textMeshPro != null)
            {
                textMeshPro.color = textColor;
            }
        }
        
        /// <summary>
        /// 背景色を設宁E
        /// </summary>
        public void SetBackgroundColor(Color color)
        {
            backgroundColor = color;
            if (backgroundRenderer != null)
            {
                backgroundRenderer.material.color = backgroundColor;
            }
        }
        
        /// <summary>
        /// 背景プレーンの表示/非表示を設宁E
        /// </summary>
        public void SetBackgroundVisible(bool visible)
        {
            if (backgroundPlane != null)
            {
                backgroundPlane.SetActive(visible);
            }
            // enableBackgroundも更新
            enableBackground = visible;
        }
        
        /// <summary>
        /// チE��スト�E置を設宁E
        /// </summary>
        public void SetAlignment(TextAlignmentOptions newAlignment)
        {
            alignment = newAlignment;
            if (textMeshPro != null)
            {
                textMeshPro.alignment = alignment;
            }
        }
        
        /// <summary>
        /// カメラ参�Eを更新
        /// </summary>
        public void SetTargetCamera(Camera camera)
        {
            targetCamera = camera;
        }
        
        /// <summary>
        /// リソースのクリーンアチE�E
        /// </summary>
        void OnDestroy()
        {
            if (backgroundRenderer != null && backgroundRenderer.material != null)
            {
                DestroyImmediate(backgroundRenderer.material);
            }
        }
        
        /// <summary>
        /// チE��スト�E容をエチE��タで即座に更新
        /// </summary>
        void OnValidate()
        {
            if (Application.isPlaying && isInitialized)
            {
                UpdateTextContent();
            }
        }
        
        /// <summary>
        /// チE��スト�E容を更新�E��E部用�E�E
        /// </summary>
        private void UpdateTextContent()
        {
            if (textMeshPro != null)
            {
                textMeshPro.text = displayText;
                textMeshPro.fontSize = fontSize;
                textMeshPro.color = textColor;
                textMeshPro.alignment = alignment;
                
                UpdateBackgroundSize();
            }
            
            if (backgroundRenderer != null)
            {
                backgroundRenderer.material.color = backgroundColor;
            }
        }
    }
}

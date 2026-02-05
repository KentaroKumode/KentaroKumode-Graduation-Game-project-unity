using UnityEngine;


namespace InventorySystem
{
    /// <summary>
    /// アイテムプレビュー時に背景装備用Planeを表示するシステム（シンプル版）
    /// DragDropHandlerのCreateDragPreviewと同じシンプルなInstantiateロジックを使用
    /// </summary>
    public class ItemPreviewStatusUI : MonoBehaviour
    {
        [Header("プレハブ設定")]
        [SerializeField] private GameObject backgroundPlanePrefab; // 背景プレーンプレハブ（必須指定）
        [Tooltip("背景として表示したいプレハブを指定してください。半透明プレーン、3Dモデル、UIパネル等が使用可能です。")]
        // [SerializeField] private bool autoCreatePrefab = true;     // 自動作成機能（廃止）
        [SerializeField] private bool showItemCard = true;        // アイテムカードを表示するか
        
        [Header("カメラ設定")]
        [SerializeField] private Camera targetCamera;             // 背景プレーンとアイテムカードの親カメラ
        [Tooltip("背景プレーンとアイテムカードを配置するカメラ。未指定の場合はCamera.mainを使用します。")]
        
        [Header("位置・回転設定")]
        [SerializeField] private Vector3 positionOffset = Vector3.zero; // 位置オフセット（カメラ相対座標）
        [SerializeField] private Vector3 rotationOffset = Vector3.zero; // 回転オフセット（度数）
        [SerializeField] private Vector3 scale = Vector3.one; // スケール
        
        [Header("アニメーション設定")]
        [SerializeField] private bool enableSlideAnimation = true; // slideアニメーションを有効にする
        [SerializeField] private float slideAnimationDuration = 0.8f; // アニメーション時間
        [SerializeField] private float slideStartOffsetY = -3.0f; // カメラ下方からの開始オフセット
        [SerializeField] private AnimationCurve slideAnimationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1); // アニメーション曲線
        
        [Header("カード位置調整")]
        [SerializeField] private Vector3 cardPositionOffset = Vector3.zero; // プレビューカードの位置オフセット
        [SerializeField] private float cardSlideStartOffsetY = -4.0f; // カードの開始位置オフセット
        
        [Header("情報パネル連携")]
        [SerializeField] private bool enableInfoPanel = false; // 情報パネル表示を有効にする（デバッグ版、非推奨）
        [SerializeField] private ItemPreviewInfoPanel infoPanel; // 情報パネル参照
        [SerializeField] private bool autoCreateInfoPanel = false; // 情報パネルの自動生成
        
        [Header("テキスト表示設定")]
        [SerializeField] private bool enableTextDisplay = false; // テキスト表示を有効にする（従来システム、非推奨）
        [SerializeField] private ItemPreviewTextRenderer textRenderer; // テキストレンダラー
        [SerializeField] private bool autoCreateTextRenderer = false; // テキストレンダラーの自動生成（非推奨）
        [SerializeField] private Vector3 textOffset = new Vector3(0, 0, -0.5f); // テキスト表示位置のオフセット
        
        [Header("3Dテキスト表示設定（最適化版）")]
        [SerializeField] private bool enable3DTextDisplay = true; // 3Dテキスト表示を有効にする（推奨）
        [SerializeField] private ItemPreview3DTextDisplay text3DDisplay; // 3Dテキスト表示システム
        [SerializeField] private bool autoCreate3DTextDisplay = true; // 3Dテキストの自動生成
        [SerializeField] private Vector3 text3DPosition = Vector3.zero; // テキスト用（0,0,0)に固定
        
        [Header("カードサイズ別スケール設定")]
        [SerializeField] private float scale1x1 = 3.5f; // 1x1カードのスケール倍率
        [SerializeField] private float scale1x2 = 3.0f; // 1x2カードのスケール倍率
        [SerializeField] private float scale1x3 = 2.5f; // 1x3カードのスケール倍率
        [SerializeField] private float scale2x1 = 2.5f; // 2x1カードのスケール倍率
        [SerializeField] private float scale2x2 = 2.0f; // 2x2カードのスケール倍率
        [SerializeField] private float scale2x3 = 1.8f; // 2x3カードのスケール倍率
        [SerializeField] private float scale3x1 = 2.0f; // 3x1カードのスケール倍率
        [SerializeField] private float scale3x2 = 1.8f; // 3x2カードのスケール倍率
        [SerializeField] private float scale3x3 = 1.5f; // 3x3カードのスケール倍率
        [SerializeField] private float scaleOther = 1.0f; // その他サイズのデフォルトスケール
        
        private GameObject spritePlane; // 背景プレーン
        private GameObject itemCardObject; // アイテムカードオブジェクト
        private CompleteItemData currentItemData; // 現在表示中のアイテムデータ
        
        /// <summary>
        /// カード位置オフセットを他のシステムから参照するためのプロパティ
        /// </summary>
        public Vector3 CardPositionOffset => cardPositionOffset;
        
        /// <summary>
        /// カードアニメーション設定を他のシステムから参照するためのプロパティ
        /// </summary>
        public bool EnableSlideAnimation => enableSlideAnimation;
        public float SlideAnimationDuration => slideAnimationDuration;
        public float CardSlideStartOffsetY => cardSlideStartOffsetY;
        public AnimationCurve SlideAnimationCurve => slideAnimationCurve;
        
        /// <summary>
        /// カードサイズに応じたスケール倍率を取得
        /// </summary>
        public float GetCardScaleFactor(int sizeX, int sizeY)
        {
            if (sizeX == 1 && sizeY == 1) return scale1x1;
            if (sizeX == 1 && sizeY == 2) return scale1x2;
            if (sizeX == 1 && sizeY == 3) return scale1x3;
            if (sizeX == 2 && sizeY == 1) return scale2x1;
            if (sizeX == 2 && sizeY == 2) return scale2x2;
            if (sizeX == 2 && sizeY == 3) return scale2x3;
            if (sizeX == 3 && sizeY == 1) return scale3x1;
            if (sizeX == 3 && sizeY == 2) return scale3x2;
            if (sizeX == 3 && sizeY == 3) return scale3x3;
            return scaleOther;
        }
        
        /// <summary>
        /// アイテムプレビュー時に背景プレーンを表示
        /// </summary>
        public void ShowItemPreview(CompleteItemData itemData)
        {
            Debug.Log($"[ItemPreviewStatusUI] 🎯 ShowItemPreview START - Item: {itemData?.displayName ?? "null"}");
            Debug.Log($"[ItemPreviewStatusUI] 🎯 enableSlideAnimation: {enableSlideAnimation}");
            Debug.Log($"[ItemPreviewStatusUI] 🎯 enable3DTextDisplay: {enable3DTextDisplay}");
            Debug.Log($"[ItemPreviewStatusUI] 🎯 showItemCard: {showItemCard}");
            Debug.Log($"[ItemPreviewStatusUI] 🎯 backgroundPlanePrefab: {(backgroundPlanePrefab != null ? backgroundPlanePrefab.name : "NULL")}");
            Debug.Log($"[ItemPreviewStatusUI] 🎯 targetCamera: {(targetCamera != null ? targetCamera.name : "NULL")}");
            
            if (itemData == null)
            {
                Debug.LogError("[ItemPreviewStatusUI] ❌ itemData is null! Cannot show preview.");
                return;
            }
            
            // AudioListener重複エラー・修正
            CheckAndFixAudioListeners();
            
            currentItemData = itemData;
            
            Debug.Log($"[ItemPreviewStatusUI] 🎯 Branch decision - enableSlideAnimation: {enableSlideAnimation}");
            
            if (enableSlideAnimation)
            {
                Debug.Log("[ItemPreviewStatusUI] 🎯 Starting slide animation...");
                StartCoroutine(ShowWithSlideAnimation());
            }
            else
            {
                Debug.Log("[ItemPreviewStatusUI] 🎯 Creating background plane directly...");
                CreateBackgroundPlane();
            }
            
            // アイテムカードを表示
            if (showItemCard)
            {
                Debug.Log("[ItemPreviewStatusUI] 🃏 Creating item card...");
                CreateItemCard(itemData);
            }
            
            // 3Dテキスト表示（最適化システム）
            Debug.Log("[ItemPreviewStatusUI] About to call Show3DItemText");
            Show3DItemText(itemData);
            Debug.Log("[ItemPreviewStatusUI] Show3DItemText call completed");
        }
        
        /// <summary>
        /// 背景プレーンとアイテムカードを非表示にする
        /// </summary>
        public void HideItemPreview()
        {
            if (spritePlane != null)
            {
                Destroy(spritePlane);
                spritePlane = null;
            }
            
            // アイテムカードも削除
            if (itemCardObject != null)
            {
                Debug.Log("[ItemPreviewStatusUI] 🗑️ Destroying item card on hide");
                Destroy(itemCardObject);
                itemCardObject = null;
            }
            
            // 3Dテキスト表示をクリア
            Hide3DItemText();
            
            currentItemData = null;
        }
        
        /// <summary>
        /// アイテムカードを作成（DragDropHandlerのロジックを流用）
        /// </summary>
        private void CreateItemCard(CompleteItemData itemData)
        {
            // 既存のカードを削除
            if (itemCardObject != null)
            {
                Debug.Log("[ItemPreviewStatusUI] 🗑️ Destroying existing item card");
                Destroy(itemCardObject);
                itemCardObject = null;
            }
            
            Debug.Log("[ItemPreviewStatusUI] 🃏 ========== CreateItemCard START ==========");
            
            try
            {
                // アイテムのFBXモデルからカードオブジェクトを作成
                if (itemData.fbxModel != null)
                {
                    Debug.Log($"[ItemPreviewStatusUI] 🃏 Creating card from FBX: {itemData.fbxModel.name}");
                    itemCardObject = Instantiate(itemData.fbxModel);
                    itemCardObject.name = $"ItemCard_{itemData.displayName}";
                    
                    // カメラの子として設定
                    Camera cam = GetTargetCamera();
                    if (cam != null)
                    {
                        itemCardObject.transform.SetParent(cam.transform, false);
                        
                        // 親子関係の強制確認・再設定
                        if (itemCardObject.transform.parent != cam.transform)
                        {
                            Debug.LogWarning("[ItemPreviewStatusUI] ⚠️ ItemCard: 親子関係設定が失敗、再試行");
                            itemCardObject.transform.parent = cam.transform;
                        }
                        
                        // 位置設定（背景プレーンより少し手前）
                        itemCardObject.transform.localPosition = cardPositionOffset;
                        itemCardObject.transform.localRotation = Quaternion.identity;
                        
                        // スケール設定
                        float scaleFactor = GetCardScaleFactor((int)itemData.size.x, (int)itemData.size.y);
                        itemCardObject.transform.localScale = Vector3.one * scaleFactor;
                        
                        Debug.Log($"[ItemPreviewStatusUI] ✅ ItemCard parent set to: {itemCardObject.transform.parent?.name}");
                        Debug.Log($"[ItemPreviewStatusUI] ✅ ItemCard local position: {itemCardObject.transform.localPosition}");
                        Debug.Log($"[ItemPreviewStatusUI] ✅ ItemCard scale factor: {scaleFactor}");
                        
                        // 物理演算を無効にする（DragDropHandlerロジック流用）
                        var rigidbodies = itemCardObject.GetComponentsInChildren<Rigidbody>();
                        foreach (var rb in rigidbodies)
                        {
                            rb.isKinematic = true;
                        }
                    }
                    else
                    {
                        Debug.LogError("[ItemPreviewStatusUI] ❌ Camera.main が見つかりません（ItemCard）");
                        Destroy(itemCardObject);
                        itemCardObject = null;
                        return;
                    }
                }
                else
                {
                    Debug.LogWarning($"[ItemPreviewStatusUI] ⚠️ No FBX model found for: {itemData.displayName}");
                    
                    // プレースホルダーを作成（DragDropHandlerロジック流用）
                    itemCardObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    itemCardObject.name = $"ItemCard_Placeholder_{itemData.displayName}";
                    
                    Camera cam = GetTargetCamera();
                    if (cam != null)
                    {
                        itemCardObject.transform.SetParent(cam.transform, false);
                        itemCardObject.transform.localPosition = cardPositionOffset;
                        itemCardObject.transform.localScale = Vector3.one * 0.5f;
                        
                        // 色を設定
                        var renderer = itemCardObject.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            renderer.material.color = new Color(0.5f, 0.8f, 1f, 0.8f); // 薄い青色
                        }
                        
                        Debug.Log($"[ItemPreviewStatusUI] 📦 Placeholder card created for: {itemData.displayName}");
                    }
                }
                
                Debug.Log($"[ItemPreviewStatusUI] ✅ ItemCard creation completed: {itemCardObject?.name}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ItemPreviewStatusUI] ❌ ItemCard creation error: {ex.Message}");
                if (itemCardObject != null)
                {
                    Destroy(itemCardObject);
                    itemCardObject = null;
                }
            }
        }
        
        /// <summary>
        /// 使用するカメラを取得（Inspector指定を優先、未指定時はCamera.main）
        /// </summary>
        private Camera GetTargetCamera()
        {
            if (targetCamera != null)
            {
                return targetCamera;
            }
            
            return Camera.main;
        }
        
        /// <summary>
        /// シンプルな背景プレハブを実行時に作成
        /// </summary>
        private GameObject CreateSimpleBackgroundPrefab()
        {
            try
            {
                Debug.Log("[ItemPreviewStatusUI] 🔧 Creating simple background prefab...");
                
                // Planeオブジェクトを作成
                GameObject backgroundObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
                backgroundObj.name = "SimpleBackgroundPlane";
                
                // 不要なCollider削除
                Collider collider = backgroundObj.GetComponent<Collider>();
                if (collider != null)
                {
                    DestroyImmediate(collider);
                }
                
                // マテリアルを設定（半透明）
                Renderer renderer = backgroundObj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // 新しいマテリアルを作成
                    Material material = new Material(Shader.Find("Standard"));
                    material.color = new Color(0.1f, 0.1f, 0.1f, 0.8f); // 暗いグレーの半透明
                    
                    // アルファブレンド設定
                    material.SetFloat("_Mode", 3); // Transparent mode
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = 3000;
                    
                    renderer.material = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                
                // スケール設定
                backgroundObj.transform.localScale = new Vector3(2f, 1f, 2f);
                
                Debug.Log("[ItemPreviewStatusUI] ✅ Simple background prefab created successfully");
                return backgroundObj;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ItemPreviewStatusUI] ❌ Failed to create simple background prefab: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 背景用プレーンを作成（シンプル版 - DragDropHandlerと同じロジック）
        /// </summary>
        private void CreateBackgroundPlane()
        {
            // 既存を削除
            if (spritePlane != null) 
            {
                Debug.Log("[ItemPreviewStatusUI] 🗑️ Destroying existing spritePlane");
                Destroy(spritePlane);
            }
            
            Debug.Log("[ItemPreviewStatusUI] 🎯 ========== CreateBackgroundPlane START ==========");
            
            // プレハブチェック（指定プレハブを優先、未設定時は自動作成）
            if (backgroundPlanePrefab == null)
            {
                Debug.LogWarning("[ItemPreviewStatusUI] ⚠️ backgroundPlanePrefab が未設定のため、シンプルな背景を自動作成します");
                
                // シンプルな背景プレハブを実行時に作成
                backgroundPlanePrefab = CreateSimpleBackgroundPrefab();
                
                if (backgroundPlanePrefab == null)
                {
                    Debug.LogError("[ItemPreviewStatusUI] ❌ 背景プレハブの自動作成に失敗しました");
                    return;
                }
            }
            
            Debug.Log($"[ItemPreviewStatusUI] 🎯 Using prefab: {backgroundPlanePrefab.name}");
            
            // カメラ取得
            Camera cam = GetTargetCamera();
            if (cam == null)
            {
                Debug.LogError("[ItemPreviewStatusUI] ❌ カメラが見つかりません。InspectorでtargetCameraを指定するか、MainCameraタグを付けたカメラを配置してください。");
                return;
            }
            
            // シンプルにInstantiate�E�EragDropHandlerのCreateDragPreviewと同じ�E�E
            spritePlane = Instantiate(backgroundPlanePrefab);
            spritePlane.name = "BackgroundPlane";
            
            // カメラの子に設定（ワールド保持しない）
            spritePlane.transform.SetParent(cam.transform, false);
            
            // 親子関係の強制確認・再設定
            if (spritePlane.transform.parent != cam.transform)
            {
                Debug.LogWarning("[ItemPreviewStatusUI] ⚠️ 親子関係設定が失敗、再試行");
                spritePlane.transform.parent = cam.transform;
            }
            
            spritePlane.transform.localPosition = positionOffset;
            spritePlane.transform.localRotation = Quaternion.Euler(rotationOffset);
            spritePlane.transform.localScale = scale;            
            // 親子関係確認ログ
            Debug.Log($"[ItemPreviewStatusUI] ✅ BackgroundPlane parent set to: {spritePlane.transform.parent?.name}");
            Debug.Log($"[ItemPreviewStatusUI] ✅ Camera name: {cam.name}");
            Debug.Log($"[ItemPreviewStatusUI] ✅ Local position: {spritePlane.transform.localPosition}");
            
            // ヒエラルキー確認
            Debug.Log($"[ItemPreviewStatusUI] 📋 Created object: '{spritePlane.name}' under '{spritePlane.transform.parent?.name}'");
            Debug.Log($"[ItemPreviewStatusUI] 🌍 World position: {spritePlane.transform.position}");
            Debug.Log($"[ItemPreviewStatusUI] 📏 Local scale: {spritePlane.transform.localScale}");            
            Debug.Log("[ItemPreviewStatusUI] 🎯 ========== CreateBackgroundPlane COMPLETE ==========");            
            // Debug.Log($"[ItemPreviewStatusUI] ✁EPlane作�E完亁E);
            // Debug.Log($"  Name: {spritePlane.name}");
            // Debug.Log($"  Parent: {cam.name}");
            // Debug.Log($"  Camera Rotation: {cam.transform.rotation.eulerAngles}");
            // Debug.Log($"  World Position: {spritePlane.transform.position}");
            // Debug.Log($"  Local Position: {spritePlane.transform.localPosition}");
            // Debug.Log($"  World Rotation: {spritePlane.transform.rotation.eulerAngles}");
            // Debug.Log($"  Local Rotation: {spritePlane.transform.localRotation.eulerAngles}");
            
            // RendererチェチE��
            // Renderer renderer = spritePlane.GetComponent<Renderer>();
            // if (renderer != null)
            // {
            //     Debug.Log($"  Renderer.enabled: {renderer.enabled}");
            //     if (renderer.material != null)
            //     {
            //         Debug.Log($"  Material: {renderer.material.name}");
            //         Debug.Log($"  Shader: {renderer.material.shader.name}");
            //     }
            // }
            Debug.Log("[ItemPreviewStatusUI] ========== 作�E完亁E==========");
        }
        
        /// <summary>
        /// slideアニメーション付きで背景プレーンを表示
        /// </summary>
        private System.Collections.IEnumerator ShowWithSlideAnimation()
        {
            // 背景プレーンを作�E、E��始位置に配置
            CreateBackgroundPlaneAtStartPosition();
            
            if (spritePlane == null) yield break;
            
            Camera cam = Camera.main;
            if (cam == null) yield break;
            
            // 開始位置�E�カメラ下�E�E�と最終位置を計箁E
            Vector3 startPos = positionOffset + new Vector3(0, slideStartOffsetY, 0);
            Vector3 endPos = positionOffset;
            
            float elapsedTime = 0f;
            
            while (elapsedTime < slideAnimationDuration)
            {
                // 途中でspriteePlaneが破棁E��れた場合�E安�EチェチE��
                if (spritePlane == null) yield break;
                
                elapsedTime += Time.deltaTime;
                float t = elapsedTime / slideAnimationDuration;
                float curveValue = slideAnimationCurve.Evaluate(t);
                
                Vector3 currentPos = Vector3.Lerp(startPos, endPos, curveValue);
                spritePlane.transform.localPosition = currentPos;
                
                yield return null;
            }
            
            // 最後も安�EチェチE��
            if (spritePlane != null)
            {
                // 最終位置で精寁E��固宁E
                spritePlane.transform.localPosition = endPos;
            }
            
            // スライドアニメーション完亁E��に3DチE��ストを表示
            Debug.Log("[ItemPreviewStatusUI] Slide animation completed, showing 3D text");
            Show3DItemText(currentItemData);
        }
        
        /// <summary>
        /// 背景プレーンを開始位置に作�E
        /// </summary>
        private void CreateBackgroundPlaneAtStartPosition()
        {
            // 既存を削除
            if (spritePlane != null) Destroy(spritePlane);
            
            if (backgroundPlanePrefab == null)
            {
                Debug.LogWarning("[ItemPreviewStatusUI] ⚠️ backgroundPlanePrefab が未設定のため、シンプルな背景を自動作成します（StartPosition）");
                
                // シンプルな背景プレハブを実行時に作成
                backgroundPlanePrefab = CreateSimpleBackgroundPrefab();
                
                if (backgroundPlanePrefab == null)
                {
                    Debug.LogError("[ItemPreviewStatusUI] ❌ 背景プレハブの自動作成に失敗しました（StartPosition）");
                    spritePlane = null;
                    return;
                }
            }
            
            Debug.Log($"[ItemPreviewStatusUI] 🎯 Using prefab for StartPosition: {backgroundPlanePrefab.name}");
            
            // カメラ取得
            Camera cam = GetTargetCamera();
            if (cam == null)
            {
                Debug.LogError("[ItemPreviewStatusUI] ❌ カメラが見つかりません（StartPosition）");
                spritePlane = null;
                return;
            }
            
            spritePlane = Instantiate(backgroundPlanePrefab);
            spritePlane.name = "BackgroundPlane";
            
            spritePlane.transform.SetParent(cam.transform, false);
            
            // 親子関係の強制確認・再設定
            if (spritePlane.transform.parent != cam.transform)
            {
                Debug.LogWarning("[ItemPreviewStatusUI] ⚠️ StartPosition: 親子関係設定が失敗、再試行");
                spritePlane.transform.parent = cam.transform;
            }
            
            spritePlane.transform.localPosition = positionOffset + new Vector3(0, slideStartOffsetY, 0); // 開始位置
            spritePlane.transform.localRotation = Quaternion.Euler(rotationOffset);
            spritePlane.transform.localScale = scale;
            
            // 親子関係確認ログ
            Debug.Log($"[ItemPreviewStatusUI] ✅ BackgroundPlane (StartPosition) parent set to: {spritePlane.transform.parent?.name}");
            Debug.Log($"[ItemPreviewStatusUI] ✅ Start position local: {spritePlane.transform.localPosition}");
        }
        
        // =======================================================
        // 以下は従来の2D UIシステム（現在は無効化）
        // 3D空間テキスト表示を使用するため、これらのメソッドは使用しない
        // =======================================================
        
        /*
        /// <summary>
        /// テキスト表示を初期化
        /// </summary>
        private void InitializeTextRenderer()
        {
            if (textRenderer == null && autoCreateTextRenderer)
            {
                GameObject textObj = new GameObject("ItemPreviewTextRenderer");
                textObj.transform.SetParent(transform);
                textRenderer = textObj.AddComponent<ItemPreviewTextRenderer>();
            }
        }
        
        /// <summary>
        /// アイテム情報をテキストで表示
        /// </summary>
        private void ShowItemText(CompleteItemData itemData)
        {
            if (!enableTextDisplay) return;
            
            if (textRenderer == null)
            {
                InitializeTextRenderer();
            }
            
            if (textRenderer != null)
            {
                // チE��ストレンダラーの位置を調整
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 textPosition = cam.transform.position + cam.transform.TransformDirection(positionOffset + textOffset);
                    textRenderer.transform.position = textPosition;
                    textRenderer.transform.SetParent(cam.transform);
                }
                
                textRenderer.DisplayItem(itemData);
            }
        }
        
        /// <summary>
        /// チE��スト表示を非表示にする
        /// </summary>
        private void HideItemText()
        {
            if (textRenderer != null)
            {
                textRenderer.ClearDisplay();
            }
        }
        
        /// <summary>
        /// チE��スト表示設定を刁E��替ぁE
        /// </summary>
        public void SetTextDisplayEnabled(bool enabled)
        {
            enableTextDisplay = enabled;
            
            if (enabled && currentItemData != null)
            {
                ShowItemText(currentItemData);
            }
            else if (!enabled)
            {
                HideItemText();
            }
        }
        
        /// <summary>
        /// チE��スト位置オフセチE��を設宁E
        /// </summary>
        public void SetTextOffset(Vector3 offset)
        {
            textOffset = offset;
            
            if (enableTextDisplay && currentItemData != null)
            {
                ShowItemText(currentItemData);
            }
        }
        
        /// <summary>
        /// 惁E��パネルを�E期化
        /// </summary>
        private void InitializeInfoPanel()
        {
            if (infoPanel == null && autoCreateInfoPanel)
            {
                GameObject infoPanelObj = new GameObject("ItemPreviewInfoPanel");
                infoPanelObj.transform.SetParent(transform);
                infoPanel = infoPanelObj.AddComponent<ItemPreviewInfoPanel>();
            }
        }
        
        /// <summary>
        /// アイチE��惁E��パネルを表示
        /// </summary>
        private void ShowInfoPanel(CompleteItemData itemData)
        {
            if (!enableInfoPanel) return;
            
            if (infoPanel == null)
            {
                InitializeInfoPanel();
            }
            
            if (infoPanel != null)
            {
                infoPanel.ShowItemInfo(itemData);
            }
        }
        
        /// <summary>
        /// 惁E��パネルを非表示
        /// </summary>
        private void HideInfoPanel()
        {
            if (infoPanel != null)
            {
                infoPanel.HideItemInfo();
            }
        }
        
        /// <summary>
        /// 情報パネル表示設定を変更
        /// </summary>
        public void SetInfoPanelEnabled(bool enabled)
        {
            enableInfoPanel = enabled;
            
            if (enabled && currentItemData != null)
            {
                ShowInfoPanel(currentItemData);
            }
            else if (!enabled)
            {
                HideInfoPanel();
            }
        }
        */
        
        /// <summary>
        /// 3Dテキスト表示システムを初期化（BackgroundPlaneと同じロジック）
        /// </summary>
        private void Initialize3DTextDisplay()
        {
            Debug.Log($"[ItemPreviewStatusUI] Initialize3DTextDisplay - text3DDisplay is null: {text3DDisplay == null}");
            Debug.Log($"[ItemPreviewStatusUI] Initialize3DTextDisplay - autoCreate3DTextDisplay: {autoCreate3DTextDisplay}");
            
            if (text3DDisplay == null && autoCreate3DTextDisplay)
            {
                try
                {
                    Debug.Log("[ItemPreviewStatusUI] Creating new 3D text display object...");
                    
                    // BackgroundPlane（spritePlane）が存在するかチェック
                    if (spritePlane == null)
                    {
                        Debug.LogError("[ItemPreviewStatusUI] spritePlane not found, cannot create text as child");
                        return;
                    }
                    
                    // 3Dテキスト表示用オブジェクトを作成
                    GameObject text3DObj = new GameObject("ItemPreview3DTextDisplay");
                    Debug.Log("[ItemPreviewStatusUI] GameObject created successfully");
                    
                    // AudioListenerが誤って追加されることを防ぐ
                    AudioListener[] listeners = text3DObj.GetComponents<AudioListener>();
                    if (listeners.Length > 0)
                    {
                        Debug.LogWarning($"[ItemPreviewStatusUI] Removing {listeners.Length} AudioListener(s) from 3D text object");
                        foreach (var listener in listeners)
                        {
                            DestroyImmediate(listener);
                        }
                    }
                    
                    // BackgroundPlane�E�EpritePlane�E��E子要素として設宁E
                    text3DObj.transform.SetParent(spritePlane.transform, false);
                    text3DObj.transform.localPosition = Vector3.zero; // (0,0,0)
                    text3DObj.transform.localRotation = Quaternion.identity;
                    text3DObj.transform.localScale = Vector3.one;
                    
                    Debug.Log($"[ItemPreviewStatusUI] Text3D object set as BackgroundPlane child");
                    Debug.Log($"[ItemPreviewStatusUI] BackgroundPlane position: {spritePlane.transform.position}");
                    Debug.Log($"[ItemPreviewStatusUI] Text object local position: {text3DObj.transform.localPosition}");
                    Debug.Log($"[ItemPreviewStatusUI] Text object world position: {text3DObj.transform.position}");
                    
                    text3DDisplay = text3DObj.AddComponent<ItemPreview3DTextDisplay>();
                    Debug.Log("[ItemPreviewStatusUI] Component added successfully");
                    
                    // チE��チE��用�E��E期�EアクチE��ブにしておく
                    Debug.Log($"[ItemPreviewStatusUI] Text3D object active state: {text3DObj.activeInHierarchy}");
                    
                    Debug.Log($"[ItemPreviewStatusUI] 3D text display created successfully as BackgroundPlane child");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[ItemPreviewStatusUI] Error creating 3D text display: {e.Message}");
                    Debug.LogError($"[ItemPreviewStatusUI] Stack trace: {e.StackTrace}");
                }
            }
            else
            {
                Debug.Log($"[ItemPreviewStatusUI] 3D text display not created");
                Debug.Log($"[ItemPreviewStatusUI] - text3DDisplay == null: {text3DDisplay == null}");
                Debug.Log($"[ItemPreviewStatusUI] - autoCreate3DTextDisplay: {autoCreate3DTextDisplay}");
                if (text3DDisplay != null)
                {
                    Debug.Log($"[ItemPreviewStatusUI] - text3DDisplay name: {text3DDisplay.name}");
                    Debug.Log($"[ItemPreviewStatusUI] - text3DDisplay gameObject active: {text3DDisplay.gameObject.activeInHierarchy}");
                }
            }
        }
        
        /// <summary>
        /// AudioListener重褁E��ェチE��・修正
        /// </summary>
        private void CheckAndFixAudioListeners()
        {
            AudioListener[] allListeners = FindObjectsOfType<AudioListener>();
            if (allListeners.Length > 1)
            {
                Debug.LogWarning($"[ItemPreviewStatusUI] Found {allListeners.Length} AudioListeners in scene!");
                
                // Camera.mainのAudioListenerを保持し、その他�E削除
                AudioListener mainCameraListener = null;
                if (Camera.main != null)
                {
                    mainCameraListener = Camera.main.GetComponent<AudioListener>();
                }
                
                foreach (var listener in allListeners)
                {
                    if (listener != mainCameraListener)
                    {
                        Debug.LogWarning($"[ItemPreviewStatusUI] Removing extra AudioListener from: {listener.gameObject.name}");
                        DestroyImmediate(listener);
                    }
                }
            }
        }
        
        /// <summary>
        /// 3DアイチE��チE��ストを表示�E�デバッグ用�E�E
        /// </summary>
        private void Show3DItemText(CompleteItemData itemData)
        {
            Debug.Log($"[ItemPreviewStatusUI] Show3DItemText called - enable3DTextDisplay: {enable3DTextDisplay}");
            
            if (!enable3DTextDisplay) 
            {
                Debug.Log("[ItemPreviewStatusUI] 3D text display is disabled, aborting");
                return;
            }
            
            // BackgroundPlaneが存在しなぁE��合�E作�Eを征E��
            if (spritePlane == null)
            {
                Debug.LogWarning("[ItemPreviewStatusUI] spritePlane not found, cannot show 3D text");
                return;
            }
            
            if (text3DDisplay == null)
            {
                Debug.Log("[ItemPreviewStatusUI] text3DDisplay is null, initializing...");
                Initialize3DTextDisplay();
            }
            
            if (text3DDisplay != null)
            {
                Debug.Log("[ItemPreviewStatusUI] Forcefully activating text3DDisplay hierarchy...");
                
                // 統一されたアクティブ化処理
                ActivateText3DDisplay();
                
                // 実際のアイテム名を取得
                string itemName = itemData?.displayName ?? "アイテム名不明";
                Debug.Log($"[ItemPreviewStatusUI] Displaying item name: {itemName}");
                
                // アイテム名ごとのオフセットと文字サイズ設定（BackgroundPlaneからの相対座標）
                Vector3 textOffset = GetTextOffset(itemName);
                float fontSize = GetTextFontSize(itemName);
                
                // ShowTextを呼び出す（アクティブ化済み）
                text3DDisplay.ShowText(itemName, textOffset, fontSize);
                
                Debug.Log("[ItemPreviewStatusUI] Show3DItemText process completed");
            }
            else
            {
                Debug.LogError("[ItemPreviewStatusUI] text3DDisplay is still null after initialization!");
            }
        }
        
        /// <summary>
        /// Text3DDisplayを統一してアクティブ化
        /// </summary>
        private void ActivateText3DDisplay()
        {
            if (text3DDisplay == null) return;
            
            Debug.Log($"[ItemPreviewStatusUI] Before activation - text3DDisplay active: {text3DDisplay.gameObject.activeInHierarchy}");
            
            // メインオブジェクトをアクティブ化
            text3DDisplay.gameObject.SetActive(true);
            
            // コンポーネントを有効化
            text3DDisplay.enabled = true;
            
            Debug.Log($"[ItemPreviewStatusUI] After activation - text3DDisplay active: {text3DDisplay.gameObject.activeInHierarchy}");
            Debug.Log($"[ItemPreviewStatusUI] text3DDisplay component enabled: {text3DDisplay.enabled}");
        }
        
        /// <summary>
        /// 文字ごとのオフセットを取得（BackgroundPlaneからの相対座標）
        /// </summary>
        private Vector3 GetTextOffset(string text)
        {
            // アイテム名に関係なく一定のオフセットを使用
            return new Vector3(0, 0, -0.01f); // BackgroundPlaneから少し前に配置
        }
        
        /// <summary>
        /// 斁E���Eごとのフォントサイズを取征E
        /// </summary>
        private float GetTextFontSize(string text)
        {
            // アイチE��名�E長さに応じてサイズを調整
            if (string.IsNullOrEmpty(text))
                return 0.3f;
                
            // 斁E��数に応じてサイズを少し調整
            if (text.Length > 10)
                return 0.25f; // 長ぁE��前�E小さぁE
            else if (text.Length > 6)
                return 0.35f; // 中程度
            else
                return 0.5f; // 短ぁE��前�E大きく
        }
        
        /// <summary>
        /// 3DチE��スト表示を非表示
        /// </summary>
        private void Hide3DItemText()
        {
            Debug.Log("[ItemPreviewStatusUI] Hide3DItemText called");
            
            if (text3DDisplay != null)
            {
                Debug.Log("[ItemPreviewStatusUI] Deactivating text3DDisplay...");
                
                // 統一された非アクチE��ブ化処琁E
                DeactivateText3DDisplay();
                
                Debug.Log("[ItemPreviewStatusUI] 3D text display deactivation completed");
            }
            else
            {
                Debug.LogWarning("[ItemPreviewStatusUI] text3DDisplay is null, cannot hide");
            }
        }
        
        /// <summary>
        /// Text3DDisplayを統一して非アクチE��ブ化
        /// </summary>
        private void DeactivateText3DDisplay()
        {
            if (text3DDisplay == null) return;
            
            Debug.Log($"[ItemPreviewStatusUI] Before deactivation - text3DDisplay active: {text3DDisplay.gameObject.activeInHierarchy}");
            
            // HideDisplayで冁E��処琁E��実衁E
            text3DDisplay.HideDisplay();
            
            // メインオブジェクトを非アクチE��ブ化
            text3DDisplay.gameObject.SetActive(false);
            
            Debug.Log($"[ItemPreviewStatusUI] After deactivation - text3DDisplay active: {text3DDisplay.gameObject.activeInHierarchy}");
        }
        
        /// <summary>
        /// 3DチE��スト表示設定を変更
        /// </summary>
        public void Set3DTextDisplayEnabled(bool enabled)
        {
            enable3DTextDisplay = enabled;
            
            if (enabled && currentItemData != null)
            {
                Show3DItemText(currentItemData);
            }
            else if (!enabled)
            {
                Hide3DItemText();
            }
        }
        
        /// <summary>
        /// 3DチE��スト位置を設宁E
        /// </summary>
        public void Set3DTextPosition(Vector3 position)
        {
            text3DPosition = position;
            
            if (text3DDisplay != null)
            {
                text3DDisplay.SetTextPosition(position);
            }
        }
    }
}

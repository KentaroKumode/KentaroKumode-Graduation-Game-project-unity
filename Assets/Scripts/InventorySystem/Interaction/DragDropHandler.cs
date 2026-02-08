using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace InventorySystem
{
    /// <summary>
    /// アイテムのドラッグアンドドロップ機能を管理
    /// </summary>
    public class DragDropHandler : MonoBehaviour
    {
        [Header("ドラッグ設定")]
        [SerializeField] private GridManager gridManager;
        [SerializeField] private Camera inventoryCamera;
        [SerializeField] private PlacementIndicator placementIndicator;
        [Header("カメラ制御")]
        [SerializeField] private CameraLockController cameraLockController;
        [SerializeField] private CameraMouseFollow cameraMouseFollowFallback; // CameraLockControllerが無い場合の直接制御

        [Header("プレビュービジュアル")]
        [SerializeField] private BackgroundBlurEffect previewBackgroundBlur; // プレビュー時に背景をぼかす
        [SerializeField] private Light previewLight; // プレビュー時に点灯するライト
        [SerializeField] private bool autoCreatePreviewLight = true; // プレビューライトを自動生成
        [SerializeField] private float previewLightIntensity = 2f; // ライトの強度
        [SerializeField] private float previewLightRange = 10f; // ライトの範囲
        [SerializeField] private Color previewLightColor = Color.white; // ライトの色
        [SerializeField] private float previewEmissionIntensity = 0.3f; // エミッション（自己発光）強度
        
        [Header("ドラッグ動作設定")]
        [SerializeField] private float dragHeightOffset = 0.1f;  // 持ち上げ時のY座標オフセット
        
        [Header("HoldingArea")]
        [SerializeField] private ItemHoldingArea holdingArea;
        
        [Header("インジケーターテクスチャ")]
        [Tooltip("配置可能時に使用するテクスチャ（18x18ピクセル推奨）")]
        [SerializeField] private Texture2D validPlacementTexture;   // 配置可能時のテクスチャ
        
        [Tooltip("配置不可時に使用するテクスチャ（18x18ピクセル推奨）")]
        [SerializeField] private Texture2D invalidPlacementTexture; // 配置不可時のテクスチャ
        
        // ドラッグ状態
        private bool isDragging = false;
        private CompleteItemData currentDragItem = null;
        private Vector2Int originalGridPosition;
        private GameObject dragPreview = null;
        private VirtualItemSlot currentVirtualSlot;
        private GameObject originalObject = null;  // 元のオブジェクトの参照
        private Vector3 targetPosition;  // アニメーション目標位置
        private bool isAnimatingToMouse = false;  // マウスへの移動アニメーション中か
        
        // プレビュースピン表示
        private bool isPreviewSpinning = false; // プレビュースピン中か
        private GameObject previewSpinInstance; // 一時的な回転プレビュー
        private GameObject previewSpinPivot;    // ピボット調整用の親
        private GameObject previewSpinSourceObject; // スピン対象の元のオブジェクト参照
        [SerializeField] private float previewSpinDuration = 0.8f; // スピン時間
        [SerializeField] private float previewSpinAngle = 360f;    // 回転角度
        [SerializeField] private Vector3 cardPositionOffset = Vector3.zero; // プレビューカードの位置オフセット（カメラローカル座標）
        [SerializeField] private Vector3 previewSpinAxis = Vector3.up; // 回転軸（XYZで指定）
        [SerializeField] private float previewSpinDistance = 2f;   // カメラ前方への距離
        private bool cameraLockedForSpin = false;
        private bool cameraLockedFallback = false;
        private bool blurEnabledBySpin = false;
        
        // プレビュー中アイテム削除UI
        private GameObject trashIconPlane;    // ゴミ箱アイコンPlane
        private Collider trashIconCollider;   // クリック判定用
        private CompleteItemData previewSpinItemData;  // プレビュー中のアイテムデータ
        private int previewSpinGridX;         // プレビュー中アイテムのグリッド位置X
        private int previewSpinGridY;         // プレビュー中アイテムのグリッド位置Y
        
        [Header("図鑑プレビュー背景")]
        [SerializeField] private GameObject previewBookPrefab;  // 図鑑背景Prefab
        [SerializeField] private Vector3 bookLocalOffset = Vector3.zero;     // カメラローカル座標でのオフセット
        [SerializeField] private float bookSlideDistance = 3f;   // スライドイン距離（カメラ下方向）
        [SerializeField] private float bookSlideInDuration = 0.4f; // スライドインアニメーション時間
        [SerializeField] private AnimationCurve bookSlideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        private GameObject previewBookInstance;  // 生成された図鑑背景インスタンス
        
        [Header("プレビューサイズ設定（アイテムグリッドサイズ別スケール係数）")]
        [SerializeField] private float previewScale1x1 = 3.0f;
        [SerializeField] private float previewScale1x2 = 2.5f;
        [SerializeField] private float previewScale1x3 = 2.0f;
        [SerializeField] private float previewScale2x2 = 2.0f;
        [SerializeField] private float previewScale2x3 = 1.5f;
        [SerializeField] private float previewScale3x3 = 1.5f;
        [SerializeField] private float previewScale4x4 = 1.0f;
        [SerializeField] private float previewScale5x5 = 0.7f;
        [SerializeField] private float previewScaleDefault = 1.0f;
        
        [Header("プレビューアイテム名表示")]
        [SerializeField] private TMP_FontAsset previewNameFont;        // カスタムフォント
        [SerializeField] private Color previewNameColor = Color.white;  // 文字色
        [SerializeField] private float previewNameFontSize = 0.8f;      // 基準フォントサイズ（4文字時）
        [SerializeField] private int previewNameBaseChars = 4;           // 基準文字数（この文字数でbaseFontSize）
        [SerializeField] private Vector3 previewNameOffset = new Vector3(0f, -0.8f, 0f); // カメラローカル座標でのオフセット
        private GameObject previewNameObject;  // アイテム名表示オブジェクト
        
        [Header("削除演出")]
        [SerializeField] private float burnDuration = 1.5f;    // 燃え尽き演出時間
        [SerializeField] private float trashIconScale = 0.3f;  // ゴミ箱アイコンの大きさ
        [SerializeField] private Vector3 trashIconOffset = new Vector3(1.2f, -0.8f, 0f); // カメラローカル座標でのオフセット
        
        // ドラッグソース追跡
        private enum DragSource { Grid, HoldingArea }
        private DragSource currentDragSource = DragSource.Grid;

        private void LockCameraMovement(bool forSpin = false)
        {
            if (cameraLockController != null)
            {
                cameraLockController.LockCamera(null);
            }
            else if (cameraMouseFollowFallback != null && cameraMouseFollowFallback.enabled)
            {
                cameraMouseFollowFallback.enabled = false;
                cameraLockedFallback = true;
            }
            cameraLockedForSpin = forSpin;
        }
        
        private void UnlockCameraMovement()
        {
            if (cameraLockController != null)
            {
                cameraLockController.UnlockCamera();
            }
            if (cameraLockedFallback && cameraMouseFollowFallback != null)
            {
                cameraMouseFollowFallback.enabled = true;
                cameraLockedFallback = false;
            }
            cameraLockedForSpin = false;
        }

        /// <summary>
        /// オブジェクトとその子要素すべてをレイヤーに設定
        /// </summary>
        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
        
        /// <summary>
        /// オブジェクトとその子要素すべてのReceiveShadowsを設定
        /// </summary>
        private void SetReceiveShadowsRecursively(GameObject obj, bool receiveShadows)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.receiveShadows = receiveShadows;
            }
            
            foreach (Transform child in obj.transform)
            {
                SetReceiveShadowsRecursively(child.gameObject, receiveShadows);
            }
        }
        
        /// <summary>
        /// オブジェクトとその子要素すべてのマテリアルにEmissionを設定（シーンの暗さに影響されない）
        /// </summary>
        private void EnableEmissionRecursively(GameObject obj, float emissionStrength)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                // マテリアルをインスタンス化（元のマテリアルを変更しない）
                Material[] materials = renderer.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material mat = materials[i];
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.EnableKeyword("_EMISSION");
                        // 元のアルベドカラーを使ってEmissionを設定
                        Color baseColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                        mat.SetColor("_EmissionColor", baseColor * emissionStrength);
                    }
                }
            }
            
            foreach (Transform child in obj.transform)
            {
                EnableEmissionRecursively(child.gameObject, emissionStrength);
            }
        }
        
        /// <summary>
        /// プレビュー用のライトを自動生成
        /// </summary>
        private void CreatePreviewLight()
        {
            GameObject lightObj = new GameObject("PreviewLight");
            previewLight = lightObj.AddComponent<Light>();
            
            // ライト設定（ポイントライト：全方向に光を放つ）
            previewLight.type = LightType.Point;
            previewLight.color = previewLightColor;
            previewLight.intensity = previewLightIntensity;
            previewLight.range = previewLightRange;
            previewLight.enabled = false;
            
            // カメラの子要素として配置（カメラと同じ位置＋前方オフセット）
            if (inventoryCamera != null)
            {
                lightObj.transform.SetParent(inventoryCamera.transform, false);
                lightObj.transform.localPosition = Vector3.zero; // カメラと同じ位置
                lightObj.transform.localRotation = Quaternion.identity; // カメラと同じ向き
            }
            
            Debug.Log($"[DragDropHandler] プレビューライトを自動生成しました（Point Light, Intensity={previewLightIntensity}, Range={previewLightRange}, Color={previewLightColor})");
        }
        
        void Start()
        {
            if (gridManager == null)
            {
                gridManager = FindObjectOfType<GridManager>();
                Debug.Log($"[DragDropHandler] GridManager auto-detect: {(gridManager != null ? "Success" : "Failed")}");
            }
            
            if (inventoryCamera == null)
            {
                inventoryCamera = Camera.main;
                Debug.Log($"[DragDropHandler] Camera auto-detect: {(inventoryCamera != null ? "Success" : "Failed")}");
            }
            
            if (placementIndicator == null)
            {
                placementIndicator = FindObjectOfType<PlacementIndicator>();
                Debug.Log($"[DragDropHandler] PlacementIndicator auto-detect: {(placementIndicator != null ? "Success" : "Failed")}");
            }

            if (cameraLockController == null)
            {
                cameraLockController = FindObjectOfType<CameraLockController>();
                Debug.Log($"[DragDropHandler] CameraLockController auto-detect: {(cameraLockController != null ? "Success" : "Failed")}");
            }
            if (cameraMouseFollowFallback == null)
            {
                cameraMouseFollowFallback = FindObjectOfType<CameraMouseFollow>();
                Debug.Log($"[DragDropHandler] CameraMouseFollow auto-detect: {(cameraMouseFollowFallback != null ? "Success" : "Failed")}");
            }
            if (previewBackgroundBlur == null)
            {
                previewBackgroundBlur = FindObjectOfType<BackgroundBlurEffect>();
                Debug.Log($"[DragDropHandler] BackgroundBlurEffect auto-detect: {(previewBackgroundBlur != null ? "Success" : "Failed")}");
            }
            
            if (holdingArea == null)
            {
                holdingArea = ItemHoldingArea.Instance ?? FindObjectOfType<ItemHoldingArea>();
                Debug.Log($"[DragDropHandler] ItemHoldingArea auto-detect: {(holdingArea != null ? "Success" : "Failed")}");
            }
            
            // プレビューライトを自動生成または検出
            if (previewLight == null && autoCreatePreviewLight)
            {
                CreatePreviewLight();
            }
            else if (previewLight == null)
            {
                // 既存のライトを検索
                Light[] lights = FindObjectsOfType<Light>();
                foreach (var light in lights)
                {
                    if (light.name.Contains("PreviewLight"))
                    {
                        previewLight = light;
                        Debug.Log($"[DragDropHandler] PreviewLight found: {light.name}");
                        break;
                    }
                }
            }
            
            // プレビューライトの初期状態を無効化
            if (previewLight != null)
            {
                previewLight.enabled = false;
            }
            
            // テクスチャが設定されていない場合は自動生成
            if (validPlacementTexture == null)
            {
                validPlacementTexture = TextureGenerator.CreateValidPlacementTexture();
                Debug.Log("[DragDropHandler] ValidPlacementTexture 自動生成完了");
            }
            
            if (invalidPlacementTexture == null)
            {
                invalidPlacementTexture = TextureGenerator.CreateInvalidPlacementTexture();
                Debug.Log("[DragDropHandler] InvalidPlacementTexture 自動生成完了");
            }
        }
        
        void Update()
        {
            HandleMouseInput();
            
            if (isDragging && dragPreview != null)
            {
                UpdateDragPreview();
                UpdatePlacementIndicator(); // 配置インジケータを更新
            }
        }
        
        /// <summary>
        /// マウス入力の処理
        /// </summary>
        private void HandleMouseInput()
        {
            // 左クリック開始
            if (Input.GetMouseButtonDown(0) && !isDragging)
            {
                Debug.Log("[DragDropHandler] 左クリック検出！");
                TryStartDragFrom3DObject();
            }
            // 左クリック終了（ドロップ）
            else if (Input.GetMouseButtonUp(0) && isDragging)
            {
                Debug.Log("[DragDropHandler] 左クリック終了 - ドロップ処理");
                TryDropItem();
            }
            // 右クリック（インベントリ上のアイテムを画面中央で回転プレビュー）
            else if (Input.GetMouseButtonDown(1) && !isDragging && !isPreviewSpinning)
            {
                Debug.Log("[DragDropHandler] 右クリック検出 - インベントリアイテムのプレビュースピン試行");
                TryPreviewSpinFromInventory();
            }
        }
        
        /// <summary>
        /// 3Dオブジェクトからのドラッグ開始を試行
        /// </summary>
        private void TryStartDragFrom3DObject()
        {
            try
            {
                Debug.Log("[DragDropHandler] 3Dオブジェクトからのドラッグ開始を試行中");
                
                // GridManagerが見つからない場合は自動検索
                if (gridManager == null)
                {
                    gridManager = FindObjectOfType<GridManager>();
                    if (gridManager == null)
                    {
                        Debug.LogError("[DragDropHandler] GridManagerが見つかりません");
                        return;
                    }
                    Debug.Log("[DragDropHandler] GridManagerを自動検出しました");
                }
                
                // カメラが設定されていない場合はメインカメラを使用
                if (inventoryCamera == null)
                {
                    inventoryCamera = Camera.main;
                    if (inventoryCamera == null)
                    {
                        Debug.LogError("[DragDropHandler] カメラが見つかりません");
                        return;
                    }
                    Debug.Log("[DragDropHandler] メインカメラを自動検出しました");
                }
                
                // マウス位置からレイを作成
                Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);
                Debug.Log($"[DragDropHandler] レイキャスト開始: マウス座標 {Input.mousePosition}");
                
                // レイキャストを実行（最大距離100、全レイヤー対象）
                RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
                Debug.Log($"[DragDropHandler] レイキャスト結果: {hits.Length}個のオブジェクトにヒット");
                
                // 全てのヒットしたオブジェクトをチェックして、アイテムを優先的に探す
                foreach (var hit in hits)
                {
                    Debug.Log($"[DragDropHandler] チェック中: {hit.collider.name}");
                    
                    // アイテムオブジェクトかチェック
                    var itemObject = hit.collider.GetComponent<Transform>();
                    if (itemObject != null)
                    {
                        string objectName = itemObject.name;
                        Debug.Log($"[DragDropHandler] オブジェクト名: '{objectName}' (_Grid_を含む: {objectName.Contains("_Grid_")})");
                        
                        if (objectName.Contains("_Grid_"))
                        {
                            // アイテム名を抽出（{itemName}_Grid_{x}_{y}_{ticks} 形式）
                            int gridIndex = objectName.IndexOf("_Grid_");
                            string itemName = objectName.Substring(0, gridIndex);
                            
                            Debug.Log($"[DragDropHandler] 抽出されたアイテム名: '{itemName}'");
                            
                            // GridManagerから該当アイテムの位置を検索
                            if (gridManager.TryGetItemPosition(itemName, out int gridX, out int gridY, out CompleteItemData itemData))
                            {
                                Debug.Log($"[DragDropHandler] アイテム位置発見: {itemName} at ({gridX}, {gridY})");
                                
                                // 元オブジェクトの参照を保存
                                originalObject = itemObject.gameObject;
                                currentDragSource = DragSource.Grid;
                                
                                StartDragFromGridPosition(itemData, gridX, gridY);
                                return; // アイテムが見つかったら処理終了
                            }
                            else
                            {
                                Debug.LogWarning($"[DragDropHandler] アイテム位置が見つかりません: {itemName}");
                            }
                        }
                        else
                        {
                            // 親オブジェクトもチェック
                            if (itemObject.parent != null && itemObject.parent.name.Contains("_Grid_"))
                            {
                                string parentName = itemObject.parent.name;
                                int gridIndex = parentName.IndexOf("_Grid_");
                                string itemName = parentName.Substring(0, gridIndex);
                                Debug.Log($"[DragDropHandler] 親から抽出されたアイテム名: '{itemName}'");
                                
                                if (gridManager.TryGetItemPosition(itemName, out int gridX, out int gridY, out CompleteItemData itemData))
                                {
                                    Debug.Log($"[DragDropHandler] 親経由でアイテム位置発見: {itemName} at ({gridX}, {gridY})");
                                    
                                    // 元オブジェクトの参照を保存（親オブジェクト）
                                    originalObject = itemObject.parent.gameObject;
                                    currentDragSource = DragSource.Grid;
                                    
                                    StartDragFromGridPosition(itemData, gridX, gridY);
                                    return; // アイテムが見つかったら処理終了
                                }
                            }
                        }
                    }
                }
                
                Debug.Log("[DragDropHandler] グリッド上にアイテムが見つからなかった。HoldingAreaをチェック...");
                
                // HoldingAreaのカードかチェック
                if (holdingArea == null) holdingArea = ItemHoldingArea.Instance;
                if (holdingArea != null)
                {
                    foreach (var hit in hits)
                    {
                        GameObject hitObj = hit.collider.gameObject;
                        // 親も含めてHoldingAreaのカードか判定
                        Transform checkTr = hitObj.transform;
                        while (checkTr != null)
                        {
                            if (holdingArea.IsHeldCard(checkTr.gameObject))
                            {
                                CompleteItemData heldItem = holdingArea.GetItemByCard(checkTr.gameObject);
                                if (heldItem != null)
                                {
                                    Debug.Log($"[DragDropHandler] HoldingAreaカード検出: {heldItem.displayName}");
                                    originalObject = checkTr.gameObject;
                                    currentDragSource = DragSource.HoldingArea;
                                    holdingArea.RemoveItem(heldItem);
                                    StartDragFromHoldingArea(heldItem);
                                    return;
                                }
                            }
                            checkTr = checkTr.parent;
                        }
                    }
                }
                
                Debug.Log("[DragDropHandler] アイテムオブジェクトが見つかりませんでした");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DragDropHandler] TryStartDragFrom3DObjectでエラー: {ex.Message}");
                Debug.LogError($"[DragDropHandler] スタックトレース: {ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// グリッド位置からドラッグ開始
        /// </summary>
        private void StartDragFromGridPosition(CompleteItemData item, int gridX, int gridY)
        {
            if (gridManager == null)
            {
                Debug.LogError("[DragDropHandler] GridManagerがnullです！");
                return;
            }
            
            if (item == null)
            {
                Debug.LogError("[DragDropHandler] CompleteItemDataがnullです！");
                return;
            }
            
            Debug.Log($"[DragDropHandler] ドラッグ開始: {item.displayName} at ({gridX}, {gridY})");
            
            // ドラッグ状態を設定
            currentDragItem = item;
            originalGridPosition = new Vector2Int(gridX, gridY);
            isDragging = true;
            LockCameraMovement();
            
            // 仮想ItemSlotを作成
            currentVirtualSlot = new VirtualItemSlot(item, gridX, gridY);
            
            // 元の位置からアイテムを削除
            gridManager.RemoveItem(gridX, gridY, item.size.x, item.size.y);
            Debug.Log($"[DragDropHandler] 元の位置からアイテムを削除しました: ({gridX}, {gridY})");
            
            // 元のオブジェクトを非表示にする
            if (originalObject != null)
            {
                originalObject.SetActive(false);
                Debug.Log($"[DragDropHandler] 元オブジェクトを非表示: {originalObject.name}");
            }
            
            // ドラッグプレビューを作成
            CreateDragPreview(item);
        }
        
        /// <summary>
        /// HoldingAreaからのドラッグ開始
        /// </summary>
        private void StartDragFromHoldingArea(CompleteItemData item)
        {
            if (item == null)
            {
                Debug.LogError("[DragDropHandler] StartDragFromHoldingArea: CompleteItemDataがnullです！");
                return;
            }
            
            Debug.Log($"[DragDropHandler] HoldingAreaからドラッグ開始: {item.displayName}");
            
            // ドラッグ状態を設定
            currentDragItem = item;
            originalGridPosition = new Vector2Int(-1, -1); // グリッド外を示す
            isDragging = true;
            LockCameraMovement();
            
            // 元のオブジェクトを非表示
            if (originalObject != null)
            {
                originalObject.SetActive(false);
            }
            
            // ドラッグプレビューを作成
            CreateDragPreview(item);
        }
        
        /// <summary>
        /// ドラッグプレビューの作成
        /// </summary>
        private void CreateDragPreview(CompleteItemData item)
        {
            try
            {
                // アイテムのプレハブからプレビューオブジェクトを作成
                if (item.fbxModel != null)
                {
                    dragPreview = Instantiate(item.fbxModel);
                    dragPreview.name = $"DragPreview_{item.displayName}";
                    
                    // 元オブジェクトのスケールを保持
                    if (originalObject != null)
                    {
                        dragPreview.transform.localScale = originalObject.transform.localScale;
                        Debug.Log($"[DragDropHandler] スケール設定: {dragPreview.transform.localScale}");
                    }
                    
                    // 物理演算を無効にする
                    var rigidbodies = dragPreview.GetComponentsInChildren<Rigidbody>();
                    foreach (var rb in rigidbodies)
                    {
                        rb.isKinematic = true;
                    }
                    
                    // コライダーを無効にする
                    var colliders = dragPreview.GetComponentsInChildren<Collider>();
                    foreach (var collider in colliders)
                    {
                        collider.enabled = false;
                    }
                    
                    // 初期位置、回転、スケールを元オブジェクトに合わせる
                    if (originalObject != null)
                    {
                        dragPreview.transform.position = originalObject.transform.position;
                        dragPreview.transform.rotation = originalObject.transform.rotation;
                        dragPreview.transform.localScale = originalObject.transform.localScale;
                        Debug.Log($"[DragDropHandler] プレビュー位置設定: {dragPreview.transform.position}, スケール: {dragPreview.transform.localScale}");
                    }
                    
                    // マウス位置への移動アニメーションを開始
                    StartCoroutine(AnimateToMousePosition());
                    
                    Debug.Log($"[DragDropHandler] ドラッグプレビュー作成: {dragPreview.name}");
                }
                else
                {
                    Debug.LogWarning($"[DragDropHandler] アイテムcardModelがnull: {item.displayName}");
                    
                    // cardModelがnullの場合、簡単なプレースホルダーを作成
                    dragPreview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    dragPreview.name = $"DragPreview_Placeholder_{item.displayName}";
                    dragPreview.transform.localScale = Vector3.one * 0.5f;
                    
                    // 色を設定（不透明）
                    var renderer = dragPreview.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.material.color = new Color(1f, 0.5f, 0.5f, 1f); // 完全不透明
                    }
                    
                    if (originalObject != null)
                    {
                        dragPreview.transform.position = originalObject.transform.position;
                        dragPreview.transform.rotation = originalObject.transform.rotation;
                    }
                    
                    Debug.Log($"[DragDropHandler] プレースホルダープレビュー作成: {dragPreview.name}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DragDropHandler] ドラッグプレビュー作成エラー: {ex.Message}");
            }
        }
        
        /// <summary>
        /// ドラッグプレビューの位置更新
        /// </summary>
        private void UpdateDragPreview()
        {
            if (dragPreview == null || inventoryCamera == null || isAnimatingToMouse) return;
            
            // 元のスケールを保持
            Vector3 originalScale = dragPreview.transform.localScale;
            
            // マウス位置から3D座標を計算
            Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            
            if (groundPlane.Raycast(ray, out float distance))
            {
                Vector3 worldPos = ray.GetPoint(distance);
                targetPosition = worldPos + Vector3.up * dragHeightOffset; // インスペクターで調整可能
                
                // スムーズに追従
                dragPreview.transform.position = Vector3.Lerp(dragPreview.transform.position, targetPosition, Time.deltaTime * 8f);
                
                // スケールを強制的に保持
                dragPreview.transform.localScale = originalScale;
            }
        }
        
        /// <summary>
        /// ドラッグ中の配置インジケータを更新
        /// </summary>
        private void UpdatePlacementIndicator()
        {
            if (currentDragItem == null || inventoryCamera == null || gridManager == null) return;
            
            // マウス位置からグリッド位置を計算
            Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            
            if (groundPlane.Raycast(ray, out float distance))
            {
                Vector3 worldPos = ray.GetPoint(distance);
                Vector2Int gridPos = WorldToGridPosition(worldPos);
                
                // 前回のハイライトをクリア
                gridManager.ClearAllHighlights();
                
                // グリッド範囲内の場合のみインジケータを表示
                if (gridPos.x >= 0 && gridPos.y >= 0 && 
                    gridPos.x + currentDragItem.size.x <= InventoryConstants.GRID_WIDTH && 
                    gridPos.y + currentDragItem.size.y <= InventoryConstants.GRID_HEIGHT)
                {
                    // セルをハイライト
                    Debug.Log($"[DragDropHandler] ハイライト開始: 位置({gridPos.x}, {gridPos.y}) サイズ({currentDragItem.size.x}x{currentDragItem.size.y})");
                    
                    // 既に表示したアイテムを追跡（重複防止）
                    System.Collections.Generic.HashSet<CompleteItemData> highlightedItems = new System.Collections.Generic.HashSet<CompleteItemData>();
                    
                    for (int y = gridPos.y; y < gridPos.y + currentDragItem.size.y; y++)
                    {
                        for (int x = gridPos.x; x < gridPos.x + currentDragItem.size.x; x++)
                        {
                            var cell = gridManager.GetCell(x, y);
                            if (cell != null)
                            {
                                // 各セルが配置可能かを個別にチェック
                                bool cellAvailable = !cell.IsLocked && !cell.IsOccupied;
                                
                                if (cellAvailable)
                                {
                                    // このセルは配置可能 → 緑インジケーター
                                    cell.ShowValidIndicator();
                                    Debug.Log($"[DragDropHandler] セル({x}, {y})に配置可能インジケーター表示");
                                }
                                else
                                {
                                    // このセルは配置不可
                                    if (cell.IsOccupied && cell.OccupiedItem != null)
                                    {
                                        // 占有済みの場合、占有しているカード全体を表示
                                        CompleteItemData occupiedItem = cell.OccupiedItem;
                                        
                                        // まだハイライトしていないアイテムの場合のみ処理
                                        if (!highlightedItems.Contains(occupiedItem))
                                        {
                                            highlightedItems.Add(occupiedItem);
                                            
                                            // 占有しているアイテムの位置を検索
                                            if (gridManager.TryGetItemPosition(occupiedItem.displayName, out int itemX, out int itemY, out CompleteItemData foundItem))
                                            {
                                                // アイテムが占有している全セルに配置不可インジケーターを表示
                                                for (int iy = itemY; iy < itemY + occupiedItem.size.y; iy++)
                                                {
                                                    for (int ix = itemX; ix < itemX + occupiedItem.size.x; ix++)
                                                    {
                                                        var occupiedCell = gridManager.GetCell(ix, iy);
                                                        if (occupiedCell != null)
                                                        {
                                                            occupiedCell.ShowInvalidIndicator();
                                                        }
                                                    }
                                                }
                                                Debug.Log($"[DragDropHandler] 占有カード '{occupiedItem.displayName}' 全体({itemX},{itemY} {occupiedItem.size.x}x{occupiedItem.size.y})に配置不可インジケーター表示");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // 占有されていない配置不可セル（ロック等）は個別表示
                                        cell.ShowInvalidIndicator();
                                        Debug.Log($"[DragDropHandler] セル({x}, {y})に配置不可インジケーター表示（ロック等）");
                                    }
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"[DragDropHandler] セル({x}, {y})が見つかりません");
                            }
                        }
                    }
                }
            }
            else
            {
                // レイキャストが失敗した場合はハイライトをクリア
                gridManager.ClearAllHighlights();
            }
        }
        
        /// <summary>
        /// アイテムのドロップを試行
        /// </summary>
        private void TryDropItem()
        {
            if (!isDragging || currentDragItem == null)
            {
                Debug.Log("[DragDropHandler] ドラッグ中ではありません");
                return;
            }
            
            Debug.Log("[DragDropHandler] ドロップ処理開始");
            
            try
            {
                // マウス位置からグリッド位置を計算
                Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                
                if (groundPlane.Raycast(ray, out float distance))
                {
                    Vector3 worldPos = ray.GetPoint(distance);
                    
                    // ワールド座標をグリッド座標に変換
                    Vector2Int gridPos = WorldToGridPosition(worldPos);
                    int dropX = gridPos.x;
                    int dropY = gridPos.y;
                    
                    Debug.Log($"[DragDropHandler] ワールド座標: {worldPos}");
                    Debug.Log($"[DragDropHandler] 変換されたグリッド座標: ({dropX}, {dropY})");
                    Debug.Log($"[DragDropHandler] 元の位置: ({originalGridPosition.x}, {originalGridPosition.y})");
                    Debug.Log($"[DragDropHandler] アイテムサイズ: {currentDragItem.size.x}x{currentDragItem.size.y}");
                    
                    if (dropX >= 0 && dropY >= 0 && 
                        dropX + currentDragItem.size.x <= InventoryConstants.GRID_WIDTH && 
                        dropY + currentDragItem.size.y <= InventoryConstants.GRID_HEIGHT)
                    {
                        // 配置可能かチェック
                        var placementValidator = FindObjectOfType<PlacementValidator>();
                        string reason = "未知のエラー"; // デフォルト値を設定
                        bool canMove = true; // デフォルトで配置可能とする
                        
                        Debug.Log($"[DragDropHandler] PlacementValidator検索: {(placementValidator != null ? "見つかった" : "見つからない")}");
                        
                        if (currentDragSource == DragSource.HoldingArea)
                        {
                            // HoldingAreaからの配置は元位置が無いため、常に新規配置として扱う
                            canMove = true;
                            reason = "HoldingAreaからの新規配置";
                            Debug.Log("[DragDropHandler] HoldingAreaソース: PlacementValidator スキップ");
                        }
                        else if (placementValidator != null)
                        {
                            canMove = placementValidator.CanMoveItem(currentDragItem, originalGridPosition.x, originalGridPosition.y, dropX, dropY, out reason);
                            Debug.Log($"[DragDropHandler] CanMoveItem結果: {canMove}, 理由: {reason}");
                        }
                        else
                        {
                            // PlacementValidatorがない場合は基本的なチェックのみ行う
                            Debug.Log("[DragDropHandler] PlacementValidatorが見つからないため、基本チェックのみ実行");
                            
                            // 元の位置と同じ場合は常に配置可能
                            if (dropX == originalGridPosition.x && dropY == originalGridPosition.y)
                            {
                                canMove = true;
                                reason = "元の位置";
                            }
                            else
                            {
                                // 他の位置への移動も基本的に許可（GridManagerで詳細チェック）
                                canMove = true;
                                reason = "基本チェック通過";
                            }
                        }
                        
                        if (canMove)
                        {
                            // アイテムを配置
                            Debug.Log($"[DragDropHandler] GridManager.CanPlaceItem呼び出し: ({dropX}, {dropY}) サイズ {currentDragItem.size.x}x{currentDragItem.size.y}");
                            
                            bool canPlace = gridManager.CanPlaceItem(dropX, dropY, currentDragItem.size.x, currentDragItem.size.y);
                            Debug.Log($"[DragDropHandler] CanPlaceItem結果: {canPlace}");
                            
                            if (canPlace)
                            {
                                Debug.Log($"[DragDropHandler] PlaceItem実行: ({dropX}, {dropY})");
                                gridManager.PlaceItem(dropX, dropY, currentDragItem.size.x, currentDragItem.size.y, currentDragItem);
                                Debug.Log($"[DragDropHandler] アイテム配置成功: {currentDragItem.displayName} at ({dropX}, {dropY})");
                                
                                // ハイライトとインジケータを非表示
                                gridManager.ClearAllHighlights();
                                if (placementIndicator != null)
                                {
                                    placementIndicator.HideIndicator();
                                }
                                
                                CompleteDrag();
                                return;
                            }
                            else
                            {
                                Debug.LogWarning($"[DragDropHandler] GridManager.CanPlaceItem失敗: ({dropX}, {dropY}) - セルの状態を確認してください");
                                
                                // デバッグ: 指定範囲のセル状態をログ出力
                                for (int y = dropY; y < dropY + currentDragItem.size.y && y < InventoryConstants.GRID_HEIGHT; y++)
                                {
                                    for (int x = dropX; x < dropX + currentDragItem.size.x && x < InventoryConstants.GRID_WIDTH; x++)
                                    {
                                        var cell = gridManager.GetCell(x, y);
                                        if (cell != null)
                                        {
                                            Debug.Log($"[DragDropHandler] セル({x}, {y}): 占有={cell.IsOccupied}, ロック={cell.IsLocked}, アイテム={(cell.OccupiedItem != null ? cell.OccupiedItem.displayName : "なし")}");
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[DragDropHandler] PlacementValidator.CanMoveItem失敗: ({dropX}, {dropY}) - {reason}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[DragDropHandler] グリッド範囲外またはサイズ制限: dropX={dropX}, dropY={dropY}");
                        Debug.LogWarning($"[DragDropHandler] グリッド制限: 0-{InventoryConstants.GRID_WIDTH-1} x 0-{InventoryConstants.GRID_HEIGHT-1}");
                        Debug.LogWarning($"[DragDropHandler] 必要範囲: {dropX}-{dropX + currentDragItem.size.x - 1} x {dropY}-{dropY + currentDragItem.size.y - 1}");
                    }
                }
                else
                {
                    Debug.LogWarning("[DragDropHandler] レイキャスト失敗");
                }
                
                // 配置に失敗した場合は元の位置に戻す
                Debug.Log("[DragDropHandler] 配置失敗 - 元の位置に戻します");
                RestoreOriginalPosition();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DragDropHandler] ドロップ処理エラー: {ex.Message}");
                RestoreOriginalPosition();
            }
        }
        
        /// <summary>
        /// ドラッグを完了
        /// </summary>
        private void CompleteDrag()
        {
            Debug.Log("[DragDropHandler] ドラッグ完了");
            
            // 元のオブジェクトを削除（新しい位置に配置されるため）
            if (originalObject != null)
            {
                Debug.Log($"[DragDropHandler] 元オブジェクトを削除: {originalObject.name}");
                Destroy(originalObject);
            }
            
            CleanupDrag();
        }
        
        /// <summary>
        /// 元の位置に戻す
        /// </summary>
        private void RestoreOriginalPosition()
        {
            // インジケータを非表示
            if (placementIndicator != null)
            {
                placementIndicator.HideIndicator();
            }
            
            if (currentDragItem != null)
            {
                // HoldingAreaソースの場合はHoldingAreaに戻す
                if (currentDragSource == DragSource.HoldingArea)
                {
                    Debug.Log($"[DragDropHandler] HoldingAreaに復元: {currentDragItem.displayName}");
                    if (holdingArea == null) holdingArea = ItemHoldingArea.Instance;
                    if (holdingArea != null)
                    {
                        holdingArea.AddItem(currentDragItem);
                    }
                    else
                    {
                        Debug.LogError("[DragDropHandler] HoldingAreaが見つからず復元失敗！");
                    }
                }
                else
                {
                    // グリッドソースの場合は元のグリッド位置に戻す
                    Debug.Log($"[DragDropHandler] 元の位置に復元: {currentDragItem.displayName} at ({originalGridPosition.x}, {originalGridPosition.y})");
                    
                    if (gridManager.CanPlaceItem(originalGridPosition.x, originalGridPosition.y, currentDragItem.size.x, currentDragItem.size.y))
                    {
                        gridManager.PlaceItem(originalGridPosition.x, originalGridPosition.y, currentDragItem.size.x, currentDragItem.size.y, currentDragItem);
                        
                        // 元のオブジェクトを再表示
                        if (originalObject != null)
                        {
                            originalObject.SetActive(true);
                            Debug.Log($"[DragDropHandler] 元オブジェクトを再表示: {originalObject.name}");
                        }
                    }
                    else
                    {
                        Debug.LogError("[DragDropHandler] 元の位置への復元に失敗！");
                    }
                }
            }
            
            CleanupDrag();
        }
        
        /// <summary>
        /// ドラッグをキャンセル
        /// </summary>
        private void CancelDrag()
        {
            Debug.Log("[DragDropHandler] ドラッグキャンセル");
            
            // インジケータを非表示
            if (placementIndicator != null)
            {
                placementIndicator.HideIndicator();
            }
            
            RestoreOriginalPosition();
        }
        
        /// <summary>
        /// ドラッグ関連のクリーンアップ
        /// </summary>
        private void CleanupDrag()
        {
            // ハイライトをクリア
            if (gridManager != null)
            {
                gridManager.ClearAllHighlights();
            }
            
            // インジケータを非表示
            if (placementIndicator != null)
            {
                placementIndicator.HideIndicator();
            }
            
            // 念のため元オブジェクトを再表示（稀に可視状態が戻らないケース対策）
            if (originalObject != null)
            {
                originalObject.SetActive(true);
            }

            isDragging = false;
            currentDragItem = null;
            currentVirtualSlot = null;
            originalObject = null;
            isAnimatingToMouse = false;
            isPreviewSpinning = false;
            cameraLockedForSpin = false;
            blurEnabledBySpin = false;
            currentDragSource = DragSource.Grid;
            UnlockCameraMovement();
            if (previewSpinInstance != null)
            {
                Destroy(previewSpinInstance);
                previewSpinInstance = null;
            }
            if (previewSpinPivot != null)
            {
                Destroy(previewSpinPivot);
                previewSpinPivot = null;
            }
            if (previewBackgroundBlur != null)
            {
                previewBackgroundBlur.DisableBlur();
            }
            
            // ゴミ箱アイコンクリーンアップ
            DestroyTrashIconPlane();
            // アイテム名クリーンアップ
            DestroyPreviewNameText();
            // 図鑑背景クリーンアップ
            DestroyBookBackground();
            previewSpinItemData = null;
            
            // スピン対象だった元のオブジェクトを再表示
            if (previewSpinSourceObject != null)
            {
                previewSpinSourceObject.SetActive(true);
                Debug.Log($"[DragDropHandler] スピン対象を再表示しました");
                previewSpinSourceObject = null;
            }
            
            if (dragPreview != null)
            {
                Destroy(dragPreview);
                dragPreview = null;
            }
            
            Debug.Log("[DragDropHandler] ドラッグクリーンアップ完了");
        }

        /// <summary>
        /// インベントリ内のアイテムを右クリックして画面中央でスピン表示
        /// </summary>
        private void TryPreviewSpinFromInventory()
        {
            // 依存の自動検出
            if (gridManager == null)
            {
                gridManager = FindObjectOfType<GridManager>();
            }
            if (inventoryCamera == null)
            {
                inventoryCamera = Camera.main;
            }
            
            if (gridManager == null || inventoryCamera == null)
            {
                Debug.LogWarning("[DragDropHandler] PreviewSpin失敗: GridManagerまたはCameraが見つかりません");
                return;
            }
            
            Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
            
            foreach (var hit in hits)
            {
                Transform targetTransform = hit.collider.transform;
                Transform gridTransform = targetTransform;
                
                // _Grid_ 付きの親を優先
                if (!gridTransform.name.Contains("_Grid_") && gridTransform.parent != null && gridTransform.parent.name.Contains("_Grid_"))
                {
                    gridTransform = gridTransform.parent;
                }
                
                if (gridTransform.name.Contains("_Grid_"))
                {
                    int gridIndex = gridTransform.name.IndexOf("_Grid_");
                    if (gridIndex > 0)
                    {
                        string itemName = gridTransform.name.Substring(0, gridIndex);
                        if (gridManager.TryGetItemPosition(itemName, out int gx, out int gy, out CompleteItemData itemData))
                        {
                            GameObject source = itemData.fbxModel != null ? itemData.fbxModel : gridTransform.gameObject;
                            Quaternion rot = gridTransform.rotation;
                            Vector3 scale = gridTransform.localScale;
                            
                            // カードサイズに応じてInspectorで設定したスケール係数を適用
                            float scaleFactor = GetPreviewScaleForSize(itemData.size.x, itemData.size.y);
                            Vector3 originalScale = scale;
                            scale *= scaleFactor;
                            
                            Debug.Log($"[DragDropHandler] プレビュースピン開始: {itemName} at ({gx},{gy}), サイズ {itemData.size.x}x{itemData.size.y}, スケール係数={scaleFactor}倍, 元スケール: {originalScale}, 最終スケール: {scale}");
                            
                            // グリッド上の元のオブジェクトを一時的に非表示
                            previewSpinSourceObject = gridTransform.gameObject;
                            previewSpinSourceObject.SetActive(false);
                            Debug.Log($"[DragDropHandler] スピン対象 {itemName} を非表示にしました");
                            
                            // プレビュー中アイテム情報を保存（削除UI用）
                            previewSpinItemData = itemData;
                            previewSpinGridX = gx;
                            previewSpinGridY = gy;
                            
                            LockCameraMovement(forSpin: true);
                            StartCoroutine(SpinPreviewCoroutine(source, rot, scale));
                            return;
                        }
                    }
                }
            }
            
            Debug.LogWarning("[DragDropHandler] プレビュースピン対象アイテムが見つかりません");
        }
        
        /// <summary>
        /// ワールド座標をグリッド座標に変換
        /// </summary>
        private Vector2Int WorldToGridPosition(Vector3 worldPos)
        {
            Debug.Log($"[DragDropHandler] WorldToGridPosition入力: {worldPos}");
            
            // 各グリッドセルの位置を確認して最も近いセルを見つける
            float minDistance = float.MaxValue;
            int closestX = -1;
            int closestY = -1;
            
            for (int y = 0; y < InventoryConstants.GRID_HEIGHT; y++)
            {
                for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
                {
                    Vector3 cellPos = gridManager.GetCellWorldPosition(x, y);
                    float distance = Vector3.Distance(worldPos, cellPos);
                    
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestX = x;
                        closestY = y;
                    }
                }
            }
            
            Debug.Log($"[DragDropHandler] 最も近いセル: ({closestX}, {closestY}), 距離: {minDistance}");
            
            // 距離が合理的な範囲内かチェック（セルサイズの2倍以内）
            if (minDistance > 2.0f)
            {
                Debug.LogWarning($"[DragDropHandler] グリッドから遠すぎる位置: 距離 {minDistance}");
                return new Vector2Int(-1, -1);
            }
            
            return new Vector2Int(closestX, closestY);
        }
        
        /// <summary>
        /// 見た目用の回転プレビュー（画面中央に一時的なクローンを表示しスピン）
        /// </summary>
        private System.Collections.IEnumerator SpinPreviewCoroutine(GameObject source, Quaternion initialRotation, Vector3 initialScale)
        {
            if (source == null || inventoryCamera == null) yield break;
            
            isPreviewSpinning = true;
            float duration = Mathf.Max(0.2f, previewSpinDuration); // 最低継続時間を確保
            float angle = Mathf.Abs(previewSpinAngle) > Mathf.Epsilon ? previewSpinAngle : 360f; // 角度が0ならデフォルト

            // 画面中央のワールド座標を正確に計算
            Vector3 screenCenter = new Vector3(Screen.width / 2f, Screen.height / 2f, 0);
            Ray centerRay = inventoryCamera.ScreenPointToRay(screenCenter);
            
            // カメラから前方に一定距離の位置（確実に画面中央）+ カード位置オフセット
            Vector3 centerWorldPos = centerRay.GetPoint(previewSpinDistance)
                + inventoryCamera.transform.right * cardPositionOffset.x
                + inventoryCamera.transform.up * cardPositionOffset.y
                + inventoryCamera.transform.forward * cardPositionOffset.z;
            
            Debug.Log($"[DragDropHandler] プレビュー位置: スクリーン中央={screenCenter}, ワールド={centerWorldPos}");
            Debug.Log($"[DragDropHandler] SpinPreviewCoroutine開始: initialScale={initialScale}, プレハブ={source.name}");
            
            // クローンを作成（見た目専用）
            previewSpinInstance = Instantiate(source);
            previewSpinInstance.name = "PreviewSpinInstance";
            previewSpinInstance.transform.position = centerWorldPos;
            previewSpinInstance.transform.rotation = initialRotation;
            previewSpinInstance.transform.localScale = initialScale;
            
            Debug.Log($"[DragDropHandler] クローン作成後のスケール: {previewSpinInstance.transform.localScale}");
            
            // プレビューカードレイヤーを設定（ブラーから除外）
            SetLayerRecursively(previewSpinInstance, LayerMask.NameToLayer("PreviewCard"));
            Debug.Log("[DragDropHandler] プレビューカードを PreviewCard レイヤーに設定しました");
            
            // 影を受けないように設定（光の影響は受ける）
            SetReceiveShadowsRecursively(previewSpinInstance, false);
            
            // シーンの暗さに影響されないようEmissionを有効化
            EnableEmissionRecursively(previewSpinInstance, previewEmissionIntensity);
            
            // ピボット用の親を作成（画面中央に固定）
            previewSpinPivot = new GameObject("PreviewSpinPivot");
            previewSpinPivot.transform.position = centerWorldPos; // 画面中央
            previewSpinPivot.transform.rotation = initialRotation;
            
            // モデルの中心オフセットを計算
            Vector3 centerOffset = Vector3.zero;
            var renderers = previewSpinInstance.GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    combined.Encapsulate(renderers[i].bounds);
                }
                centerOffset = combined.center - previewSpinInstance.transform.position;
            }
            
            // プレビューインスタンスを親にセット
            previewSpinInstance.transform.SetParent(previewSpinPivot.transform, true);
            Debug.Log($"[DragDropHandler] SetParent後のスケール: {previewSpinInstance.transform.localScale}");
            
            // ピボット中心で回転するように、モデルをオフセット分移動
            previewSpinInstance.transform.position = centerWorldPos - centerOffset;
            Debug.Log($"[DragDropHandler] 位置調整後のスケール: {previewSpinInstance.transform.localScale}");
            
            Debug.Log($"[DragDropHandler] ピボット位置（画面中央）: {centerWorldPos}, モデルオフセット: {centerOffset}");
            
            // コライダー/剛体を無効化（プレビュー用）
            foreach (var col in previewSpinInstance.GetComponentsInChildren<Collider>()) col.enabled = false;
            var rb = previewSpinInstance.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            // 背景ぼかしを有効化
            if (previewBackgroundBlur != null)
            {
                previewBackgroundBlur.EnableBlur();
                blurEnabledBySpin = true;
            }
            
            // プレビューライトを点灯
            if (previewLight != null)
            {
                previewLight.enabled = true;
                Debug.Log("[DragDropHandler] プレビューライトを点灯しました");
            }
            
            // === 図鑑背景とプレビューカードを同時スライドインで表示 ===
            yield return StartCoroutine(SlideInPreview(centerWorldPos));
            
            // === ゴミ箱アイコンをカメラ子として表示 ===
            CreateTrashIconPlane(centerWorldPos);
            
            // === アイテム名をカメラ子として表示 ===
            if (previewSpinItemData != null)
            {
                CreatePreviewNameText(previewSpinItemData.displayName);
            }
            
            // 継続スピン: 一定角速度で回し、次のキー入力で停止
            float angularSpeed = angle / duration; // 度/秒
            float startTime = Time.time;
            const float stopCheckDelay = 0.1f; // すぐ停止しないように最小遅延
            Vector3 axis = previewSpinAxis.sqrMagnitude > Mathf.Epsilon ? previewSpinAxis.normalized : Vector3.up;
            bool burnTriggered = false;
            
            while (previewSpinPivot != null)
            {
                float deltaAngle = angularSpeed * Time.deltaTime;
                previewSpinPivot.transform.Rotate(axis, deltaAngle, Space.Self);
                
                bool canStop = Time.time - startTime >= stopCheckDelay;
                
                if (canStop)
                {
                    // 左クリック: ゴミ箱アイコン判定
                    if (Input.GetMouseButtonDown(0) && IsTrashIconClicked())
                    {
                        Debug.Log("[DragDropHandler] 🗑️ ゴミ箱アイコンがクリックされました");
                        burnTriggered = true;
                        break;
                    }
                    
                    // その他の入力: プレビュー終了（ただし左クリックは非ゴミ箱なら終了）
                    if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Escape) || 
                        Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Space))
                    {
                        break;
                    }
                }
                
                yield return null;
            }
            
            // === ゴミ箱アイコン破棄 ===
            DestroyTrashIconPlane();
            
            // === アイテム名破棄 ===
            DestroyPreviewNameText();
            
            // === 図鑑背景を即座に破棄 ===
            DestroyBookBackground();
            
            if (burnTriggered && previewSpinInstance != null)
            {
                // 回転を停止
                // ピボットからプレビューを外す（燃え演出中に位置を固定するため）
                if (previewSpinPivot != null)
                {
                    previewSpinInstance.transform.SetParent(null, true);
                    Destroy(previewSpinPivot);
                    previewSpinPivot = null;
                }
                
                // === 燃え尽き演出 ===
                Debug.Log($"[DragDropHandler] 🔥 燃え尽き演出開始: {previewSpinItemData?.displayName}");
                
                bool burnComplete = false;
                ItemBurnEffect.Play(previewSpinInstance, burnDuration, () => { burnComplete = true; });
                
                // 効果音
                try { InventorySoundManager.Instance?.PlayItemDiscard(); }
                catch (System.Exception) { /* ignore */ }
                
                // 燃え尽きアニメーション完了を待つ
                while (!burnComplete)
                {
                    yield return null;
                }
                
                // インベントリからアイテム削除  
                if (previewSpinItemData != null)
                {
                    // InventoryManager.RemoveItem がグリッド解放＋OnItemRemovedイベント発火を一括処理
                    InventoryManager.Instance?.RemoveItem(previewSpinGridX, previewSpinGridY, previewSpinItemData);
                    
                    Debug.Log($"[DragDropHandler] 🗑️✅ アイテム削除完了: {previewSpinItemData.displayName} at ({previewSpinGridX},{previewSpinGridY})");
                }
                
                // 元のグリッドオブジェクトを破棄（再表示しない）
                if (previewSpinSourceObject != null)
                {
                    Destroy(previewSpinSourceObject);
                    previewSpinSourceObject = null;
                }
                
                // プレビューインスタンス破棄
                if (previewSpinInstance != null)
                {
                    Destroy(previewSpinInstance);
                    previewSpinInstance = null;
                }
            }
            else
            {
                // 通常終了: プレビュークリーンアップ
                if (previewSpinInstance != null)
                {
                    Destroy(previewSpinInstance);
                    previewSpinInstance = null;
                }
                if (previewSpinPivot != null)
                {
                    Destroy(previewSpinPivot);
                    previewSpinPivot = null;
                }
                
                // スピン対象だった元のオブジェクトを再表示
                if (previewSpinSourceObject != null)
                {
                    previewSpinSourceObject.SetActive(true);
                    Debug.Log($"[DragDropHandler] スピン完了: 対象オブジェクトを再表示しました");
                    previewSpinSourceObject = null;
                }
            }
            
            // 共通クリーンアップ
            isPreviewSpinning = false;
            previewSpinItemData = null;
            
            if (cameraLockedForSpin)
            {
                UnlockCameraMovement();
            }
            if (blurEnabledBySpin && previewBackgroundBlur != null)
            {
                previewBackgroundBlur.DisableBlur();
                blurEnabledBySpin = false;
            }
            
            // プレビューライトを消灯
            if (previewLight != null)
            {
                previewLight.enabled = false;
                Debug.Log("[DragDropHandler] プレビューライトを消灯しました");
            }
        }
        
        // =================================================================
        //  図鑑背景 UI
        // =================================================================

        /// <summary>
        /// 図鑑背景とプレビューカードを同時にカメラ下方からスライドイン
        /// </summary>
        private IEnumerator SlideInPreview(Vector3 targetWorldPos)
        {
            // カメラのローカル下方向をワールド空間で計算
            Vector3 slideOffsetWorld = inventoryCamera.transform.up * (-bookSlideDistance);
            
            // ピボットのスライド開始位置
            Vector3 pivotStartPos = targetWorldPos + slideOffsetWorld;
            if (previewSpinPivot != null)
                previewSpinPivot.transform.position = pivotStartPos;
            
            // 図鑑背景のセットアップ
            if (previewBookPrefab != null)
            {
                DestroyBookBackground();
                
                previewBookInstance = Instantiate(previewBookPrefab);
                previewBookInstance.name = "PreviewBookBackground";
                previewBookInstance.transform.SetParent(inventoryCamera.transform, false);
                previewBookInstance.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
                
                int previewLayer = LayerMask.NameToLayer("PreviewCard");
                if (previewLayer >= 0)
                    SetLayerRecursively(previewBookInstance, previewLayer);
            }
            
            // 図鑑背景のスライド位置
            Vector3 bookTargetLocal = bookLocalOffset;
            Vector3 bookStartLocal = bookTargetLocal + Vector3.down * bookSlideDistance;
            
            // スライドインアニメーション
            float elapsed = 0f;
            while (elapsed < bookSlideInDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / bookSlideInDuration);
                float curve = bookSlideCurve.Evaluate(t);
                
                // プレビューカード（ピボット）をスライド
                if (previewSpinPivot != null)
                {
                    previewSpinPivot.transform.position = Vector3.Lerp(pivotStartPos, targetWorldPos, curve);
                }
                
                // 図鑑背景をスライド
                if (previewBookInstance != null)
                {
                    previewBookInstance.transform.localPosition = Vector3.Lerp(bookStartLocal, bookTargetLocal, curve);
                }
                
                if (previewSpinPivot == null && previewBookInstance == null)
                    yield break;
                
                yield return null;
            }
            
            // 最終位置を確定
            if (previewSpinPivot != null)
                previewSpinPivot.transform.position = targetWorldPos;
            if (previewBookInstance != null)
                previewBookInstance.transform.localPosition = bookTargetLocal;
            
            Debug.Log($"[DragDropHandler] プレビュースライドイン完了");
        }
        
        /// <summary>図鑑背景を即座に破棄</summary>
        private void DestroyBookBackground()
        {
            if (previewBookInstance != null)
            {
                Destroy(previewBookInstance);
                previewBookInstance = null;
            }
        }

        // =================================================================
        //  プレビューサイズヘルパー
        // =================================================================

        /// <summary>
        /// アイテムのグリッドサイズに応じたプレビュースケール係数を取得
        /// </summary>
        private float GetPreviewScaleForSize(int sizeX, int sizeY)
        {
            // 小さい方をX、大きい方をYに正規化（例: 3x1 → 1x3 と同じ扱い）
            int minS = Mathf.Min(sizeX, sizeY);
            int maxS = Mathf.Max(sizeX, sizeY);
            
            // 1xN系
            if (minS == 1 && maxS == 1) return previewScale1x1;
            if (minS == 1 && maxS == 2) return previewScale1x2;
            if (minS == 1 && maxS == 3) return previewScale1x3;
            // 2xN系
            if (minS == 2 && maxS == 2) return previewScale2x2;
            if (minS == 2 && maxS == 3) return previewScale2x3;
            // 3x3
            if (minS == 3 && maxS == 3) return previewScale3x3;
            // 4x4
            if (minS == 4 && maxS == 4) return previewScale4x4;
            // 5x5
            if (minS >= 5) return previewScale5x5;
            
            // その他（中間サイズ）
            return previewScaleDefault;
        }

        // =================================================================
        //  アイテム名表示（TextMeshPro 3D）
        // =================================================================

        /// <summary>
        /// TextMeshProを使用してアイテム名を3D空間上に表示（カメラ子）
        /// </summary>
        private void CreatePreviewNameText(string itemName)
        {
            if (string.IsNullOrEmpty(itemName) || inventoryCamera == null) return;
            
            DestroyPreviewNameText();
            
            previewNameObject = new GameObject("PreviewItemName");
            previewNameObject.transform.SetParent(inventoryCamera.transform, false);
            previewNameObject.transform.localPosition = previewNameOffset;
            // カメラ正面を向く
            previewNameObject.transform.localRotation = Quaternion.identity;
            
            // TextMeshPro 3Dコンポーネントを追加
            var tmp = previewNameObject.AddComponent<TextMeshPro>();
            tmp.text = itemName;
            tmp.fontSize = GetScaledFontSize(itemName.Length);
            tmp.color = previewNameColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            
            // カスタムフォントが設定されていれば適用
            if (previewNameFont != null)
            {
                tmp.font = previewNameFont;
            }
            
            // RectTransformのサイズ設定（十分な幅を確保）
            var rt = previewNameObject.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(10f, 2f);
            }
            
            // PreviewCardレイヤーに設定
            int previewLayer = LayerMask.NameToLayer("PreviewCard");
            if (previewLayer >= 0)
                previewNameObject.layer = previewLayer;
            
            Debug.Log($"[DragDropHandler] アイテム名表示: '{itemName}' offset={previewNameOffset}");
        }
        
        /// <summary>
        /// 文字数に応じたフォントサイズを算出。
        /// テキスト幅 ∝ fontSize × charCount なので、
        /// 同じ幅に収めるため fontSize = baseSize × baseChars / charCount（反比例）で減衰。
        /// baseChars以下の文字数ではbaseFontSizeをそのまま使用。
        /// </summary>
        private float GetScaledFontSize(int charCount)
        {
            int baseChars = Mathf.Max(1, previewNameBaseChars);
            if (charCount <= baseChars) return previewNameFontSize;
            return previewNameFontSize * baseChars / (float)charCount;
        }

        /// <summary>アイテム名表示を破棄</summary>
        private void DestroyPreviewNameText()
        {
            if (previewNameObject != null)
            {
                Destroy(previewNameObject);
                previewNameObject = null;
            }
        }

        // =================================================================
        //  ゴミ箱アイコン UI
        // =================================================================

        /// <summary>
        /// ゴミ箱アイコンPlaneをカメラの子として配置
        /// </summary>
        private void CreateTrashIconPlane(Vector3 centerWorldPos)
        {
            // Quad生成
            trashIconPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            trashIconPlane.name = "TrashIconPlane";
            
            // カメラの子として配置（ローカルオフセットで位置決定）
            trashIconPlane.transform.SetParent(inventoryCamera.transform, false);
            trashIconPlane.transform.localPosition = trashIconOffset;
            trashIconPlane.transform.localRotation = Quaternion.identity; // カメラ正面を向く
            trashIconPlane.transform.localScale = Vector3.one * trashIconScale;
            
            // テクスチャ設定 — Unlit/Texture + 透過対応
            var renderer = trashIconPlane.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Built-in RPで確実に動くシェーダーを選択
                Shader shader = Shader.Find("Unlit/Transparent");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("UI/Default");
                
                var mat = new Material(shader);
                mat.mainTexture = TextureGenerator.CreateTrashIconTexture(64);
                mat.color = Color.white;
                
                // 透過ブレンドを明示設定
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3100; // Transparent キュー
                
                renderer.material = mat;
                renderer.receiveShadows = false;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            
            // PreviewCardレイヤー
            int previewLayer = LayerMask.NameToLayer("PreviewCard");
            if (previewLayer >= 0)
                trashIconPlane.layer = previewLayer;
            
            // コライダーをクリック判定用に保持
            trashIconCollider = trashIconPlane.GetComponent<Collider>();
            
            Debug.Log($"[DragDropHandler] ゴミ箱アイコン表示: localOffset={trashIconOffset}, scale={trashIconScale}");
        }
        
        /// <summary>ゴミ箱アイコンがクリックされたかチェック</summary>
        private bool IsTrashIconClicked()
        {
            if (trashIconCollider == null || inventoryCamera == null) return false;
            
            Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
            
            foreach (var hit in hits)
            {
                if (hit.collider == trashIconCollider)
                    return true;
            }
            
            return false;
        }
        
        /// <summary>ゴミ箱アイコンPlaneを破棄</summary>
        private void DestroyTrashIconPlane()
        {
            if (trashIconPlane != null)
            {
                // マテリアルとテクスチャのクリーンアップ
                var renderer = trashIconPlane.GetComponent<Renderer>();
                if (renderer != null && renderer.material != null)
                {
                    if (renderer.material.mainTexture != null)
                        Destroy(renderer.material.mainTexture);
                    Destroy(renderer.material);
                }
                
                Destroy(trashIconPlane);
                trashIconPlane = null;
                trashIconCollider = null;
            }
        }
        
        /// <summary>
        /// ドラッグプレビューをマウス位置にスムーズに移動させるアニメーション
        /// </summary>
        private System.Collections.IEnumerator AnimateToMousePosition()
        {
            if (dragPreview == null || inventoryCamera == null) yield break;
            
            if (dragPreview == null)
            {
                Debug.LogWarning("[DragDropHandler] AnimateToMousePosition: dragPreview is null");
                yield break;
            }

            if (inventoryCamera == null)
            {
                Debug.LogWarning("[DragDropHandler] AnimateToMousePosition: inventoryCamera is null");
                yield break;
            }

            isAnimatingToMouse = true;
            Vector3 startPos = dragPreview.transform.position;
            Vector3 originalScale = dragPreview.transform.localScale; // スケールを保持
            float animationTime = 0.1f; // 0.1秒のアニメーション
            float elapsed = 0f;
            
            Debug.Log($"[DragDropHandler] アニメーション開始: スケール {originalScale}");
            
            while (elapsed < animationTime)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / animationTime;
                
                if (dragPreview == null)
                {
                    isAnimatingToMouse = false;
                    Debug.LogWarning("[DragDropHandler] AnimateToMousePosition: dragPreview destroyed during animation");
                    yield break;
                }

                // マウス位置を取得
                Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                
                if (groundPlane.Raycast(ray, out float distance))
                {
                    Vector3 mouseWorldPos = ray.GetPoint(distance) + Vector3.up * dragHeightOffset;
                    
                    // スムーズに補間（位置のみ、スケールは保持）
                    dragPreview.transform.position = Vector3.Lerp(startPos, mouseWorldPos, t);
                    dragPreview.transform.localScale = originalScale; // スケールを強制的に保持
                }
                
                yield return null;
            }
            
            if (dragPreview == null)
            {
                isAnimatingToMouse = false;
                Debug.LogWarning("[DragDropHandler] AnimateToMousePosition: dragPreview destroyed before final log");
                yield break;
            }

            isAnimatingToMouse = false;
            Debug.Log($"[DragDropHandler] アニメーション完了: 最終スケール {dragPreview.transform.localScale}");
        }
    }
}

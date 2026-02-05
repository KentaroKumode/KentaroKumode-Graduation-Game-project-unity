using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// 個別のグリッドセル
    /// ロック状態と座標を管理
    /// </summary>
    public class GridCell : MonoBehaviour
    {
        [Header("セル情報")]
        [SerializeField] private int gridX;
        [SerializeField] private int gridY;
        [SerializeField] private bool isLocked = true;
        [SerializeField] private bool isOccupied = false;
        
        [Header("ビジュアル")]
        [SerializeField] private GameObject lockVisual;
        [SerializeField] private MeshRenderer cellRenderer;
        
        [Space(10)]
        [Header("デバッグ情報")]
        [Tooltip("現在のグリッド座標とロック状態")]
        [SerializeField] private bool showDebugInfo = false;
        
        [Space(5)]
        [Header("インジケーター調整")]
        [Tooltip("インジケーターのY座標オフセット")]
        [SerializeField] private float indicatorYOffset = 0.2f;
        
        private Material cellMaterial;
        private GameObject currentIndicatorInstance; // 現在使用中のインジケーター
        private bool isValidIndicator = false; // 現在のインジケータータイプ
        private static GridManager cachedGridManager; // GridManagerのキャッシュ
        private Color normalColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        private Color lockedColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        private Color occupiedColor = new Color(0.6f, 0.6f, 0.2f, 0.8f);
        private Color highlightColor = new Color(0.3f, 0.8f, 0.3f, 0.8f);
        private Color invalidColor = new Color(0.8f, 0.2f, 0.2f, 0.8f);
        
        private CompleteItemData occupiedItem;
        
        public int GridX => gridX;
        public int GridY => gridY;
        public bool IsLocked => isLocked;
        public bool IsOccupied => isOccupied;
        public CompleteItemData OccupiedItem => occupiedItem;
        
        /// <summary>
        /// GridManagerを効率的に取得（キャッシュ付き）
        /// </summary>
        private GridManager GetGridManager()
        {
            if (cachedGridManager == null)
            {
                cachedGridManager = FindObjectOfType<GridManager>();
            }
            return cachedGridManager;
        }
        
        /// <summary>
        /// セルを初期化
        /// </summary>
        public void Initialize(int x, int y, bool locked)
        {
            gridX = x;
            gridY = y;
            isLocked = locked;
            
            // cellRendererが設定されていない場合は自動検出
            if (cellRenderer == null)
            {
                cellRenderer = GetComponent<MeshRenderer>();
                if (cellRenderer == null)
                {
                    cellRenderer = GetComponentInChildren<MeshRenderer>();
                }
                
                if (cellRenderer == null)
                {
                    Debug.LogError($"[GridCell] MeshRendererが見つかりません: セル({x}, {y})");
                    return;
                }
            }
            
            // マテリアルを取得（インスタンス化）
            if (cellRenderer != null)
            {
                cellMaterial = cellRenderer.material; // これによりマテリアルがインスタンス化される
                
                if (cellMaterial != null)
                {
                    UpdateVisual();
                }
                else
                {
                    Debug.LogError($"[GridCell] マテリアル取得失敗: セル({x}, {y})");
                }
            }
            
            // ロックビジュアル更新
            if (lockVisual != null)
            {
                lockVisual.SetActive(isLocked);
                Debug.Log($"[GridCell] ロックビジュアル設定: セル({x}, {y}), ロック状態: {isLocked}");
            }
            else
            {
                Debug.Log($"[GridCell] ロックビジュアルなし: セル({x}, {y})");
            }
        }
        
        /// <summary>
        /// セルをアンロック
        /// </summary>
        public void Unlock()
        {
            if (!isLocked) return;
            
            isLocked = false;
            UpdateVisual();
            
            if (lockVisual != null)
            {
                lockVisual.SetActive(false);
            }
            
            Debug.Log($"[GridCell] Cell ({gridX}, {gridY}) unlocked");
        }
        
        /// <summary>
        /// セルをロック
        /// </summary>
        public void Lock()
        {
            isLocked = true;
            UpdateVisual();
            
            if (lockVisual != null)
            {
                lockVisual.SetActive(true);
            }
        }
        
        /// <summary>
        /// 占有状態を設定
        /// </summary>
        public void SetOccupied(bool occupied, CompleteItemData item = null)
        {
            Debug.Log($"[GridCell] SetOccupied呼び出し: Cell({gridX},{gridY}) 現在占有={isOccupied} → 新状態={occupied}");
            
            if (isOccupied && occupied && occupiedItem != null && item != null)
            {
                Debug.LogWarning($"[GridCell] 重複配置警告: Cell({gridX},{gridY}) 既存={occupiedItem.displayName} → 新規={item.displayName}");
            }
            
            isOccupied = occupied;
            occupiedItem = item;
            UpdateVisual();
            
            if (occupied && item != null)
            {
                Debug.Log($"[GridCell] Cell ({gridX}, {gridY}) occupied by {item.displayName}");
            }
            else
            {
                Debug.Log($"[GridCell] Cell ({gridX}, {gridY}) freed");
            }
        }
        
        /// <summary>
        /// セルをハイライト（配置可能）
        /// </summary>
        public void HighlightValid()
        {
            Debug.Log($"[GridCell] HighlightValid呼び出し: セル({gridX}, {gridY}), マテリアル: {(cellMaterial != null ? "あり" : "なし")}");
            
            // マテリアルがnullの場合は再取得を試行
            if (cellMaterial == null)
            {
                TryGetMaterial();
            }
            
            if (cellMaterial != null)
            {
                cellMaterial.color = highlightColor;
                Debug.Log($"[GridCell] 色変更成功: {highlightColor}");
            }
            else
            {
                Debug.LogWarning($"[GridCell] マテリアル取得失敗: セル({gridX}, {gridY})");
            }
        }
        
        /// <summary>
        /// セルをハイライト（配置不可）
        /// </summary>
        public void HighlightInvalid()
        {
            Debug.Log($"[GridCell] HighlightInvalid呼び出し: セル({gridX}, {gridY}), マテリアル: {(cellMaterial != null ? "あり" : "なし")}");
            
            // マテリアルがnullの場合は再取得を試行
            if (cellMaterial == null)
            {
                TryGetMaterial();
            }
            
            if (cellMaterial != null)
            {
                cellMaterial.color = invalidColor;
                Debug.Log($"[GridCell] 色変更成功: {invalidColor}");
            }
            else
            {
                Debug.LogWarning($"[GridCell] マテリアル取得失敗: セル({gridX}, {gridY})");
            }
        }
        
        /// <summary>
        /// ハイライト解除
        /// </summary>
        public void ClearHighlight()
        {
            Debug.Log($"[GridCell] ClearHighlight呼び出し: セル({gridX}, {gridY})");
            UpdateVisual();
        }
        
        /// <summary>
        /// マテリアル取得を試行
        /// </summary>
        private void TryGetMaterial()
        {
            Debug.Log($"[GridCell] マテリアル再取得試行: セル({gridX}, {gridY})");
            
            // cellRendererの状態を確認
            Debug.Log($"[GridCell] 現在のcellRenderer: {cellRenderer != null}");
            if (cellRenderer != null)
            {
                Debug.Log($"[GridCell] cellRenderer.material: {cellRenderer.material != null}");
                if (cellRenderer.material != null)
                {
                    Debug.Log($"[GridCell] material.name: {cellRenderer.material.name}");
                }
            }
            
            if (cellRenderer == null)
            {
                Debug.Log($"[GridCell] cellRendererがnull、コンポーネント検索開始");
                
                // このGameObjectから直接検索
                cellRenderer = GetComponent<MeshRenderer>();
                if (cellRenderer != null)
                {
                    Debug.Log($"[GridCell] MeshRenderer発見（GameObject直接）");
                }
                else
                {
                    // 子オブジェクトから検索
                    cellRenderer = GetComponentInChildren<MeshRenderer>();
                    if (cellRenderer != null)
                    {
                        Debug.Log($"[GridCell] MeshRenderer発見（子オブジェクト）");
                    }
                    else
                    {
                        Debug.LogError($"[GridCell] MeshRendererが見つかりません: セル({gridX}, {gridY})");
                        
                        // GameObject の詳細情報を出力
                        Debug.Log($"[GridCell] GameObject名: {gameObject.name}");
                        Debug.Log($"[GridCell] アクティブ状態: {gameObject.activeInHierarchy}");
                        Debug.Log($"[GridCell] コンポーネント一覧:");
                        foreach (Component comp in GetComponents<Component>())
                        {
                            Debug.Log($"  - {comp?.GetType().Name ?? "null"}");
                        }
                        
                        // 子オブジェクト情報
                        Debug.Log($"[GridCell] 子オブジェクト数: {transform.childCount}");
                        for (int i = 0; i < transform.childCount; i++)
                        {
                            Transform child = transform.GetChild(i);
                            Debug.Log($"  子{i}: {child.name} (Active: {child.gameObject.activeInHierarchy})");
                            MeshRenderer childRenderer = child.GetComponent<MeshRenderer>();
                            if (childRenderer != null)
                            {
                                Debug.Log($"    → MeshRenderer発見！");
                            }
                        }
                        return;
                    }
                }
            }
            
            if (cellRenderer != null && cellMaterial == null)
            {
                cellMaterial = cellRenderer.material;
                if (cellMaterial != null)
                {
                    Debug.Log($"[GridCell] マテリアル再取得成功: {cellMaterial.name}");
                }
                else
                {
                    Debug.LogError($"[GridCell] cellRenderer.materialがnull");
                }
            }
            else if (cellRenderer == null)
            {
                Debug.LogError($"[GridCell] cellRenderer取得失敗: セル({gridX}, {gridY})");
            }
            else if (cellMaterial != null)
            {
                Debug.Log($"[GridCell] マテリアルは既に存在: {cellMaterial.name}");
            }
        }
        
        /// <summary>
        /// ビジュアル更新
        /// </summary>
        private void UpdateVisual()
        {
            if (cellMaterial != null)
            {
                if (isLocked)
                {
                    cellMaterial.color = lockedColor;
                }
                else if (isOccupied)
                {
                    cellMaterial.color = occupiedColor;
                }
                else
                {
                    cellMaterial.color = normalColor;
                }
            }
        }
        
        /// <summary>
        /// セルクリック時の処理
        /// </summary>
        void OnMouseDown()
        {
            if (isLocked)
            {
                Debug.Log($"[GridCell] Cell ({gridX}, {gridY}) is locked!");
                // TODO: ロック警告メッセージ表示
            }
        }
        
        /// <summary>
        /// メモリリーク防止のためのクリーンアップ
        /// </summary>
        void OnDestroy()
        {
            Debug.Log($"[GridCell] OnDestroy - Cell ({gridX}, {gridY}) メモリクリーンアップ開始");
            
            // 占有アイテム参照をクリア
            occupiedItem = null;
            
            // マテリアル参照をクリア（共有マテリアルの場合は破棄しない）
            if (cellMaterial != null)
            {
                cellMaterial = null;
            }
            
            // レンダラー参照をクリア
            if (cellRenderer != null)
            {
                cellRenderer = null;
            }
            
            // インジケーター関連をクリーンアップ
            if (currentIndicatorInstance != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(currentIndicatorInstance);
                }
                else
                {
                    DestroyImmediate(currentIndicatorInstance);
                }
                currentIndicatorInstance = null;
            }
            
            // ロック表示オブジェクトの参照をクリア
            if (lockVisual != null)
            {
                lockVisual = null;
            }
            
            Debug.Log($"[GridCell] OnDestroy - Cell ({gridX}, {gridY}) メモリクリーンアップ完了");
        }
        
        /// <summary>
        /// 配置可能インジケーターを表示（プレファブベース）
        /// </summary>
        public void ShowValidIndicator()
        {
            ShowIndicator(true);
        }
        
        /// <summary>
        /// 配置不可インジケーターを表示（プレファブベース）
        /// </summary>
        public void ShowInvalidIndicator()
        {
            ShowIndicator(false);
        }
        
        /// <summary>
        /// 指定したタイプのインジケーターを表示（プレファブベース）
        /// </summary>
        private void ShowIndicator(bool isValid)
        {
            // 既存のインジケーターとタイプが違う場合は再作成
            if (currentIndicatorInstance == null || isValidIndicator != isValid)
            {
                if (currentIndicatorInstance != null)
                {
                    DestroyIndicatorInstance();
                }
                CreateIndicatorFromPrefab(isValid);
            }
            
            if (currentIndicatorInstance != null)
            {
                currentIndicatorInstance.SetActive(true);
            }
        }
        
        /// <summary>
        /// インジケーターを非表示
        /// </summary>
        public void HideIndicatorTexture()
        {
            if (currentIndicatorInstance != null)
            {
                currentIndicatorInstance.SetActive(false);
                Debug.Log($"[GridCell] インジケーター非表示: セル({gridX}, {gridY})");
            }
        }
        
        /// <summary>
        /// Prefabからインジケーターを作成
        /// </summary>
        private void CreateIndicatorFromPrefab(bool isValid)
        {
            // GridManagerからデフォルトPrefabを取得
            GridManager gridManager = GetGridManager();
            if (gridManager == null)
            {
                Debug.LogError("[GridCell] GridManagerが見つかりません");
                return;
            }
            
            GameObject prefabToUse = gridManager.GetIndicatorPrefab(isValid);
            string indicatorType = isValid ? "配置可能" : "配置不可";
            
            if (prefabToUse == null)
            {
                Debug.LogError($"[GridCell] {indicatorType}インジケーターPrefabが設定されていません。GridManagerにインジケーターPrefabを設定してください: セル({gridX}, {gridY})");
                return;
            }
            
            // Prefabからインスタンス化
            currentIndicatorInstance = Instantiate(prefabToUse, transform);
            currentIndicatorInstance.name = $"{indicatorType}Indicator_Cell_{gridX}_{gridY}";
            isValidIndicator = isValid;

            // 位置とサイズを設定（カードと同じ座標計算）
            Vector3 position = GetIndicatorPosition();
            currentIndicatorInstance.transform.localPosition = position;
            currentIndicatorInstance.transform.localRotation = Quaternion.identity; // 回転なし
            currentIndicatorInstance.transform.localScale = Vector3.one; // フルサイズ

            // 初期状態では非表示
            currentIndicatorInstance.SetActive(false);
        }
        
        /// <summary>
        /// インジケーターインスタンスを削除
        /// </summary>
        private void DestroyIndicatorInstance()
        {
            if (currentIndicatorInstance != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(currentIndicatorInstance);
                }
                else
                {
                    DestroyImmediate(currentIndicatorInstance);
                }
                currentIndicatorInstance = null;
                Debug.Log($"[GridCell] インジケーターインスタンス削除: セル({gridX}, {gridY})");
            }
        }
        
        /// <summary>
        /// インジケーターの位置を取得（GridManagerのカード配置ロジックを使用）
        /// </summary>
        private Vector3 GetIndicatorPosition()
        {
            // GridManagerのカード配置と同じ座標計算を使用
            GridManager gridManager = GetGridManager();
            if (gridManager != null)
            {
                // カードと同じ座標系でセル中心を計算
                Vector3 cellWorldPosition = gridManager.GetCellWorldPosition(gridX, gridY);
                
                // GridManagerのオフセット設定を適用
                Vector3 managerOffset = gridManager.GetIndicatorPositionOffset();
                
                // ワールド座標からローカル座標に変換
                Vector3 localPosition = transform.InverseTransformPoint(cellWorldPosition + managerOffset);
                
                return localPosition;
            }
            
            // フォールバック: ローカル設定を使用
            return new Vector3(0, indicatorYOffset, 0);
        }
        
        /// <summary>
        /// インジケーターの位置を更新
        /// </summary>
        public void UpdateIndicatorPosition()
        {
            if (currentIndicatorInstance != null)
            {
                Vector3 newPosition = GetIndicatorPosition();
                currentIndicatorInstance.transform.localPosition = newPosition;
            }
        }
    }
}

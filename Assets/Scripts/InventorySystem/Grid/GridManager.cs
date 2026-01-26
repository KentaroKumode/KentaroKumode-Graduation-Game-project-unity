using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// グリッド全体を管理
    /// セルの生成、ロック/アンロック制御
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        [Header("グリッド設定")]
        [SerializeField] private GameObject cellPrefab;
        [SerializeField] private float cellSize = 1.0f;
        [SerializeField] private float cellSpacing = 0.1f;
        
        [Header("初期状態")]
        [SerializeField] private int initialUnlockedRows = InventoryConstants.INITIAL_UNLOCKED_ROWS;
        
        [Header("デフォルトインジケーター設定")]
        [Tooltip("全セルに適用するデフォルト配置可能インジケーターPrefab")]
        [SerializeField] private GameObject defaultValidIndicatorPrefab;
        
        [Tooltip("全セルに適用するデフォルト配置不可インジケーターPrefab")]
        [SerializeField] private GameObject defaultInvalidIndicatorPrefab;
        
        [Space(10)]
        [Header("インジケーター位置設定")]
        [Tooltip("インジケーターの位置オフセット（XYZ）カード配置と同じ座標系")]
        [SerializeField] private Vector3 indicatorPositionOffset = new Vector3(0, 0.1f, 0);
        
        private GridCell[,] cells;
        private int currentUnlockedRows;
        
        public int GetUnlockedRows() => currentUnlockedRows;
        public Vector3 GetIndicatorPositionOffset() => indicatorPositionOffset;
        
        /// <summary>
        /// 指定したタイプのインジケーターPrefabを取得
        /// </summary>
        public GameObject GetIndicatorPrefab(bool isValid)
        {
            return isValid ? defaultValidIndicatorPrefab : defaultInvalidIndicatorPrefab;
        }
        
        void Update()
        {
            // デバッグ用：Dキーで占有セル情報をダンプ
            if (Input.GetKeyDown(KeyCode.D))
            {
                DumpOccupiedCells();
            }
        }
        
        void Start()
        {
            InitializeGrid();
        }
        
        /// <summary>
        /// グリッドを初期化
        /// </summary>
        public void InitializeGrid()
        {
            if (cellPrefab == null)
            {
                Debug.LogError("[GridManager] Cell prefab is not assigned!");
                return;
            }
            
            // 既存セルをクリーンアップ
            ClearExistingCells();
            
            cells = new GridCell[InventoryConstants.GRID_WIDTH, InventoryConstants.GRID_HEIGHT];
            currentUnlockedRows = initialUnlockedRows;
            
            Debug.Log($"[GridManager] 初期化: initialUnlockedRows={initialUnlockedRows}, currentUnlockedRows={currentUnlockedRows}, GRID_HEIGHT={InventoryConstants.GRID_HEIGHT}");
            
            // グリッドの中心を計算
            float gridWidth = InventoryConstants.GRID_WIDTH * (cellSize + cellSpacing) - cellSpacing;
            float gridHeight = InventoryConstants.GRID_HEIGHT * (cellSize + cellSpacing) - cellSpacing;
            
            // セルを生成（transform.positionを中心として配置）
            for (int y = 0; y < InventoryConstants.GRID_HEIGHT; y++)
            {
                for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
                {
                    // 各セルの位置を左上基準で計算（左上が0,0）
                    // 座標系: X+ = 左, X- = 右, Z+ = 下, Z- = 上
                    Vector3 cellPos = transform.position + new Vector3(
                        // X座標: (0,0)が左上なので、x増加で右（X軸マイナス方向）
                        (InventoryConstants.GRID_WIDTH - 1) * (cellSize + cellSpacing) / 2f - x * (cellSize + cellSpacing),
                        0, // セルは常にGridManagerと同じY座標
                        // Z座標: (0,0)が左上なので、y増加で下（Z軸プラス方向）  
                        -(InventoryConstants.GRID_HEIGHT - 1) * (cellSize + cellSpacing) / 2f + y * (cellSize + cellSpacing)
                    );
                    
                    GameObject cellObj = Instantiate(cellPrefab, cellPos, Quaternion.identity, transform);
                    cellObj.name = $"Cell_{x}_{y}";
                    
                    GridCell cell = cellObj.GetComponent<GridCell>();
                    if (cell == null)
                    {
                        cell = cellObj.AddComponent<GridCell>();
                    }
                    
                    // セルの初期化（初期解放行数以下はアンロック）
                    bool isLocked = y >= currentUnlockedRows;
                    cell.Initialize(x, y, isLocked);
                    

                    cells[x, y] = cell;
                }
            }
            
            Debug.Log($"[GridManager] Grid initialized: {InventoryConstants.GRID_WIDTH}x{InventoryConstants.GRID_HEIGHT}, Unlocked rows: {currentUnlockedRows}");
        }
        
        /// <summary>
        /// セル位置情報をデバッグ出力
        /// </summary>
        public void LogCellPositions()
        {
            Debug.Log("[GridManager] === セル位置情報 ===");
            for (int y = 0; y < 3 && y < InventoryConstants.GRID_HEIGHT; y++) // 最初の3行のみ
            {
                for (int x = 0; x < 3 && x < InventoryConstants.GRID_WIDTH; x++) // 最初の3列のみ
                {
                    Vector3 calculatedPos = GetCellWorldPosition(x, y);
                    GridCell cell = GetCell(x, y);
                    Vector3 actualPos = cell != null ? cell.transform.position : Vector3.zero;
                    
                    Debug.Log($"[GridManager] Cell({x},{y}): 計算位置={calculatedPos}, 実際位置={actualPos}, 差={Vector3.Distance(calculatedPos, actualPos):F3}");
                }
            }
        }
        
        /// <summary>
        /// 指定行をアンロック
        /// </summary>
        public void UnlockRow(int rowCount)
        {
            if (rowCount <= currentUnlockedRows || rowCount > InventoryConstants.GRID_HEIGHT)
            {
                Debug.LogWarning($"[GridManager] Invalid row count: {rowCount}");
                return;
            }
            
            // 新しい行をアンロック
            int rowToUnlock = rowCount - 1;
            for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
            {
                if (cells[x, rowToUnlock] != null)
                {
                    cells[x, rowToUnlock].Unlock();
                }
            }
            
            currentUnlockedRows = rowCount;
            Debug.Log($"[GridManager] Row {rowToUnlock} unlocked. Total unlocked rows: {currentUnlockedRows}");
        }
        
        /// <summary>
        /// セルを取得
        /// </summary>
        public GridCell GetCell(int x, int y)
        {
            if (x < 0 || x >= InventoryConstants.GRID_WIDTH || y < 0 || y >= InventoryConstants.GRID_HEIGHT)
            {
                return null;
            }
            
            return cells[x, y];
        }
        
        /// <summary>
        /// セルのワールド中心座標を取得（左上基準座標系）
        /// 座標系: X+ = 左, X- = 右, Z+ = 下, Z- = 上
        /// </summary>
        public Vector3 GetCellWorldPosition(int x, int y)
        {
            if (x < 0 || x >= InventoryConstants.GRID_WIDTH || y < 0 || y >= InventoryConstants.GRID_HEIGHT)
            {
                Debug.LogError($"[GridManager] 無効なグリッド座標: ({x}, {y})");
                return Vector3.zero;
            }
            
            // セルの中心座標を計算（左上基準：(0,0)が左上）
            Vector3 cellCenter = transform.position + new Vector3(
                // X座標: (0,0)が左上なので、x増加で右（X軸マイナス方向）
                (InventoryConstants.GRID_WIDTH - 1) * (cellSize + cellSpacing) / 2f - x * (cellSize + cellSpacing),
                0,
                // Z座標: (0,0)が左上なので、y増加で下（Z軸プラス方向）
                -(InventoryConstants.GRID_HEIGHT - 1) * (cellSize + cellSpacing) / 2f + y * (cellSize + cellSpacing)
            );
            
            return cellCenter;
        }
        
        /// <summary>
        /// セルのワールド左上角座標を取得（ピボット位置用）
        /// 座標系: X+ = 左, X- = 右, Z+ = 下, Z- = 上
        /// </summary>
        public Vector3 GetCellTopLeftPosition(int x, int y)
        {
            if (x < 0 || x >= InventoryConstants.GRID_WIDTH || y < 0 || y >= InventoryConstants.GRID_HEIGHT)
            {
                Debug.LogError($"[GridManager] 無効なグリッド座標: ({x}, {y})");
                return Vector3.zero;
            }
            
            // セルの中心座標を取得（左上基準座標系）
            Vector3 cellCenter = transform.position + new Vector3(
                // X座標: (0,0)が左上なので、x増加で右（X軸マイナス方向）
                (InventoryConstants.GRID_WIDTH - 1) * (cellSize + cellSpacing) / 2f - x * (cellSize + cellSpacing),
                0,
                // Z座標: (0,0)が左上なので、y増加で下（Z軸プラス方向）
                -(InventoryConstants.GRID_HEIGHT - 1) * (cellSize + cellSpacing) / 2f + y * (cellSize + cellSpacing)
            );
            
            // セルの左上角座標を計算（カードピボット用）
            // 座標系: X+ = 左, Z- = 上なので
            Vector3 topLeft = cellCenter + new Vector3(
                +cellSize / 2f,  // 左に移動（X軸プラス方向）
                0,
                -cellSize / 2f   // 上に移動（Z軸マイナス方向）
            );
            
            Debug.Log($"[GridManager] セル({x},{y}): 中心={cellCenter} → 左上角={topLeft} [X+=左, Z-=上]");
            
            return topLeft;
        }
        
        /// <summary>
        /// アイテムの左上角座標を計算（ピボットが左上角の場合）
        /// </summary>
        public Vector3 GetItemTopLeftPosition(int gridX, int gridY, int sizeX, int sizeY)
        {
            Debug.Log($"[GridManager] === アイテム左上角座標計算 ===");
            Debug.Log($"[GridManager] Input: gridX={gridX}, gridY={gridY}, sizeX={sizeX}, sizeY={sizeY}");
            
            // 基準セルの左上角座標を取得
            Vector3 baseCellTopLeft = GetCellTopLeftPosition(gridX, gridY);
            
            Debug.Log($"[GridManager] 基準セル({gridX},{gridY})の左上角: {baseCellTopLeft}");
            Debug.Log($"[GridManager] GridManager中心: {transform.position}");
            Debug.Log($"[GridManager] セルサイズ: {cellSize}, 間隔: {cellSpacing}");
            
            // 座標計算の検証
            Vector3 gridCenter = transform.position;
            float totalGridWidth = InventoryConstants.GRID_WIDTH * (cellSize + cellSpacing) - cellSpacing;
            float totalGridHeight = InventoryConstants.GRID_HEIGHT * (cellSize + cellSpacing) - cellSpacing;
            Vector3 gridTopLeft = gridCenter - new Vector3(totalGridWidth/2f, 0, -totalGridHeight/2f);
            
            Debug.Log($"[GridManager] グリッド全体サイズ: {totalGridWidth} x {totalGridHeight}");
            Debug.Log($"[GridManager] グリッド左上角: {gridTopLeft}");
            Debug.Log($"[GridManager] アイテム配置位置: {baseCellTopLeft}");
            
            return baseCellTopLeft;
        }
        
        /// <summary>
        /// 複数セルにまたがるアイテムの中心座標を計算
        /// </summary>
        public Vector3 GetItemCenterPosition(int gridX, int gridY, int sizeX, int sizeY)
        {
            Debug.Log($"[GridManager] === アイテム中心座標計算 ===");
            Debug.Log($"[GridManager] Input: gridX={gridX}, gridY={gridY}, sizeX={sizeX}, sizeY={sizeY}");
            
            // アイテムが占有する範囲の中心を計算
            float centerX = gridX + (sizeX - 1) / 2f;
            float centerY = gridY + (sizeY - 1) / 2f;
            Debug.Log($"[GridManager] Grid center offset: centerX={centerX}, centerY={centerY}");
            
            // グリッド全体の中心オフセット
            float gridCenterOffsetX = (InventoryConstants.GRID_WIDTH - 1) / 2f;
            float gridCenterOffsetY = (InventoryConstants.GRID_HEIGHT - 1) / 2f;
            Debug.Log($"[GridManager] Grid center offsets: X={gridCenterOffsetX}, Y={gridCenterOffsetY}");
            
            Vector3 itemCenter = transform.position + new Vector3(
                (centerX - gridCenterOffsetX) * (cellSize + cellSpacing),
                0,
                (centerY - gridCenterOffsetY) * (cellSize + cellSpacing)
            );
            
            Debug.Log($"[GridManager] Calculated item center: {itemCenter}");
            Debug.Log($"[GridManager] Transform position: {transform.position}");
            
            return itemCenter;
        }
        
        /// <summary>
        /// 指定位置にアイテムを配置可能かチェック
        /// </summary>
        public bool CanPlaceItem(int gridX, int gridY, int sizeX, int sizeY)
        {
            Debug.Log($"[GridManager] CanPlaceItem チェック: ({gridX}, {gridY}) サイズ {sizeX}x{sizeY}");
            Debug.Log($"[GridManager] 現在のアンロック行数: {currentUnlockedRows} / {InventoryConstants.GRID_HEIGHT}");
            
            // 範囲チェック
            if (gridX < 0 || gridY < 0)
            {
                Debug.Log($"[GridManager] 範囲外: 負の座標");
                return false;
            }
            
            if (gridX + sizeX > InventoryConstants.GRID_WIDTH)
            {
                Debug.Log($"[GridManager] 範囲外: X方向 {gridX + sizeX} > {InventoryConstants.GRID_WIDTH}");
                return false;
            }
            
            if (gridY + sizeY > InventoryConstants.GRID_HEIGHT)
            {
                Debug.Log($"[GridManager] 範囲外: Y方向 {gridY + sizeY} > {InventoryConstants.GRID_HEIGHT}");
                return false;
            }
            
            // アンロック状態チェック
            Debug.Log($"[GridManager] アンロックチェック: 配置終了行 {gridY + sizeY} <= アンロック行数 {currentUnlockedRows}");
            if (gridY + sizeY > currentUnlockedRows)
            {
                Debug.Log($"[GridManager] ロック行: {gridY + sizeY} > {currentUnlockedRows}");
                return false;
            }
            
            // セルの占有状態チェック
            for (int y = gridY; y < gridY + sizeY; y++)
            {
                for (int x = gridX; x < gridX + sizeX; x++)
                {
                    GridCell cell = GetCell(x, y);
                    if (cell == null)
                    {
                        Debug.Log($"[GridManager] セルがnull: ({x}, {y})");
                        return false;
                    }
                    
                    Debug.Log($"[GridManager] セル ({x}, {y}) チェック: 占有={cell.IsOccupied}, ロック={cell.IsLocked}");
                    
                    if (cell.IsOccupied)
                    {
                        Debug.Log($"[GridManager] セル占有済み: ({x}, {y}) by {cell.OccupiedItem?.itemName ?? "不明"}");
                        return false;
                    }
                    if (cell.IsLocked)
                    {
                        Debug.Log($"[GridManager] セルロック中: ({x}, {y})");
                        return false;
                    }
                }
            }
            
            Debug.Log($"[GridManager] 配置可能: ({gridX}, {gridY})");
            return true;
        }
        
        /// <summary>
        /// アイテムを配置（セルの占有状態を更新 + 3Dオブジェクト配置）
        /// </summary>
        public void PlaceItem(int gridX, int gridY, int sizeX, int sizeY, ItemData item)
        {
            Debug.Log($"[GridManager] === PlaceItem開始 ===");
            
            // Nullチェック
            if (item == null)
            {
                Debug.LogError("[GridManager] ItemDataがnullです！");
                return;
            }
            
            if (string.IsNullOrEmpty(item.itemName))
            {
                Debug.LogError("[GridManager] ItemData.itemNameがnullまたは空です！");
                return;
            }
            
            Debug.Log($"[GridManager] PlaceItem called: gridX={gridX}, gridY={gridY}, sizeX={sizeX}, sizeY={sizeY}, item={item.itemName}");
            
            // 既存のアイテムオブジェクトをクリーンアップ（重複防止）
            CleanupExistingItemObjects(item.itemName);
            
            // セルの占有状態を設定
            Debug.Log($"[GridManager] 占有状態設定開始: {item.itemName} 範囲({gridX},{gridY})～({gridX+sizeX-1},{gridY+sizeY-1})");
            for (int y = gridY; y < gridY + sizeY; y++)
            {
                for (int x = gridX; x < gridX + sizeX; x++)
                {
                    GridCell cell = GetCell(x, y);
                    if (cell != null)
                    {
                        Debug.Log($"[GridManager] セル ({x}, {y}) に {item.itemName} を配置開始");
                        cell.SetOccupied(true, item);
                        Debug.Log($"[GridManager] セル ({x}, {y}) 配置完了: 占有={cell.IsOccupied}");
                    }
                    else
                    {
                        Debug.LogError($"[GridManager] セル ({x}, {y}) が見つかりません！");
                    }
                }
            }
            
            // 3Dオブジェクトの自動配置
            Place3DObject(gridX, gridY, sizeX, sizeY, item);
            
            Debug.Log($"[GridManager] === PlaceItem完了 ===");
            Debug.Log($"[GridManager] Item placed: {item.itemName} at ({gridX}, {gridY})");
        }
        
        /// <summary>
        /// 既存のアイテムオブジェクトをクリーンアップ（重複防止）
        /// </summary>
        private void CleanupExistingItemObjects(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
            {
                Debug.LogWarning("[GridManager] itemNameがnullまたは空です。クリーンアップをスキップします。");
                return;
            }
            
            Debug.Log($"[GridManager] === 既存アイテムクリーンアップ開始: {itemName} ===");
            
            int cleanupCount = 0;
            // GridManager配下の子オブジェクトをチェック
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                
                // child.nameもnullチェック
                if (child != null && !string.IsNullOrEmpty(child.name))
                {
                    // アイテム名でフィルタリング（セル以外のオブジェクトを対象）
                    if (child.name.Contains(itemName) && !child.name.StartsWith("Cell_"))
                    {
                        Debug.Log($"[GridManager] 既存アイテムを削除: {child.name} at {child.position}");
                        
                        if (Application.isPlaying)
                        {
                            Destroy(child.gameObject);
                        }
                        else
                        {
                            DestroyImmediate(child.gameObject);
                        }
                        cleanupCount++;
                    }
                }
            }
            
            Debug.Log($"[GridManager] === 既存アイテムクリーンアップ完了: {cleanupCount}個削除 ===");
        }
        
        /// <summary>
        /// 3Dオブジェクトをグリッド上に正確に配置（ピボット左上角対応）
        /// </summary>
        private void Place3DObject(int gridX, int gridY, int sizeX, int sizeY, ItemData item)
        {
            if (item.cardModel == null)
            {
                Debug.LogWarning($"[GridManager] {item.itemName}の3Dモデルが設定されていません");
                return;
            }
            
            Debug.Log($"[GridManager] === 配置デバッグ: {item.itemName} at Grid({gridX},{gridY}) Size({sizeX}x{sizeY}) ===");
            
            // 既存の同名アイテム数をチェック
            int existingCount = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null && !string.IsNullOrEmpty(child.name) && 
                    child.name.Contains(item.itemName) && !child.name.StartsWith("Cell_"))
                {
                    existingCount++;
                    Debug.Log($"[GridManager] 既存アイテム#{existingCount}: {child.name} at {child.position}");
                }
            }
            
            // アイテムの左上角座標を計算
            Vector3 itemPosition = GetItemTopLeftPosition(gridX, gridY, sizeX, sizeY);
            
            // Y座標を適切に設定（セル位置に合わせる）
            GridCell baseCell = GetCell(gridX, gridY);
            if (baseCell != null)
            {
                // 基準セルのY座標を使用し、少し上に配置
                itemPosition.y = baseCell.transform.position.y + 0.1f;
                Debug.Log($"[GridManager] Y座標をセル基準に調整: セルY={baseCell.transform.position.y} → アイテムY={itemPosition.y}");
            }
            else
            {
                // フォールバック：GridManager基準
                itemPosition.y = transform.position.y + 0.1f;
                Debug.LogWarning($"[GridManager] 基準セルが見つからないため、GridManager基準でY座標設定: {itemPosition.y}");
            }
            
            Debug.Log($"[GridManager] 計算された配置位置: {itemPosition}");
            Debug.Log($"[GridManager] GridManager中心: {transform.position}");
            Debug.Log($"[GridManager] セルサイズ: {cellSize}, 間隔: {cellSpacing}");
            
            // Y座標の検証情報
            Debug.Log($"[GridManager] === Y座標詳細 ===");
            Debug.Log($"[GridManager] GridManager Y座標: {transform.position.y}");
            if (baseCell != null)
            {
                Debug.Log($"[GridManager] 基準セル Y座標: {baseCell.transform.position.y}");
            }
            Debug.Log($"[GridManager] 最終アイテム Y座標: {itemPosition.y}");
            Debug.Log($"[GridManager] Y座標オフセット: +0.1f（セル上配置）");
            
            // 3Dオブジェクトを生成
            GameObject itemObject = Instantiate(item.cardModel, itemPosition, Quaternion.identity, transform);
            itemObject.name = $"{item.itemName}_Grid_{gridX}_{gridY}_{System.DateTime.Now.Ticks}";
            
            // コライダーを確保（ドラッグ操作用）
            EnsureCollider(itemObject);
            
            Debug.Log($"[GridManager] *** 生成完了: {itemObject.name} at {itemObject.transform.position} ***");
            
            // スケールをセルサイズに合わせて自動調整
            AdjustObjectScale(itemObject, sizeX, sizeY);
            
            // 最終結果確認
            Debug.Log($"[GridManager] 最終位置: {itemObject.transform.position}");
            Debug.Log($"[GridManager] 最終スケール: {itemObject.transform.localScale}");
        }
        
        /// <summary>
        /// 3Dオブジェクトのスケールをprefab基準の0.5倍に統一調整
        /// </summary>
        private void AdjustObjectScale(GameObject itemObject, int sizeX, int sizeY)
        {
            Debug.Log($"[GridManager] スケール調整: セル {sizeX}x{sizeY} → prefab基準0.5倍に統一");
            
            // prefabの基準スケール（通常1.0）の0.5倍に統一
            Vector3 uniformScale = Vector3.one * 0.5f;
            itemObject.transform.localScale = uniformScale;
            
            Debug.Log($"[GridManager] 統一スケール適用: prefab基準 → 0.5倍 = {uniformScale}");
        }
        
        /// <summary>
        /// アイテムを削除（セルの占有状態をクリア）
        /// </summary>
        public void RemoveItem(int gridX, int gridY, int sizeX, int sizeY)
        {
            for (int y = gridY; y < gridY + sizeY; y++)
            {
                for (int x = gridX; x < gridX + sizeX; x++)
                {
                    GridCell cell = GetCell(x, y);
                    if (cell != null)
                    {
                        cell.SetOccupied(false, null);
                    }
                }
            }
            
            Debug.Log($"[GridManager] Item removed from ({gridX}, {gridY})");
        }
        
        /// <summary>
        /// オブジェクトにコライダーを確保（ドラッグ操作用）
        /// </summary>
        private void EnsureCollider(GameObject itemObject)
        {
            Collider collider = itemObject.GetComponent<Collider>();
            if (collider == null)
            {
                // 子オブジェクトからコライダーを検索
                collider = itemObject.GetComponentInChildren<Collider>();
            }
            
            if (collider == null)
            {
                // コライダーがない場合はBoxColliderを追加
                BoxCollider boxCollider = itemObject.AddComponent<BoxCollider>();
                Debug.Log($"[GridManager] BoxCollider追加: {itemObject.name}");
            }
            else
            {
                Debug.Log($"[GridManager] 既存Collider確認: {collider.GetType().Name} on {itemObject.name}");
            }
        }
        
        /// <summary>
        /// 指定範囲のセルをハイライト
        /// </summary>
        public void HighlightCells(int startX, int startY, int sizeX, int sizeY, bool isValid)
        {
            for (int y = startY; y < startY + sizeY; y++)
            {
                for (int x = startX; x < startX + sizeX; x++)
                {
                    GridCell cell = GetCell(x, y);
                    if (cell != null)
                    {
                        if (isValid)
                        {
                            cell.HighlightValid();
                        }
                        else
                        {
                            cell.HighlightInvalid();
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// 全セルのハイライトをクリア
        /// </summary>
        public void ClearAllHighlights()
        {
            for (int y = 0; y < InventoryConstants.GRID_HEIGHT; y++)
            {
                for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
                {
                    if (cells[x, y] != null)
                    {
                        cells[x, y].ClearHighlight();
                        cells[x, y].HideIndicatorTexture(); // テクスチャも非表示
                    }
                }
            }
        }
        
        /// <summary>
        /// デバッグ用：特定行をアンロック
        /// </summary>
        [ContextMenu("Unlock Next Row")]
        public void UnlockNextRow()
        {
            if (currentUnlockedRows < InventoryConstants.GRID_HEIGHT)
            {
                UnlockRow(currentUnlockedRows + 1);
            }
        }
        
        /// <summary>
        /// デバッグ用：グリッド配置の詳細情報を出力
        /// </summary>
        [ContextMenu("Debug Grid Layout")]
        public void DebugGridLayout()
        {
            Debug.Log("=== GRID LAYOUT DEBUG ===");
            Debug.Log($"GridManager Transform: {transform.position}");
            Debug.Log($"Cell Size: {cellSize}, Cell Spacing: {cellSpacing}");
            Debug.Log($"Grid Dimensions: {InventoryConstants.GRID_WIDTH}x{InventoryConstants.GRID_HEIGHT}");
            
            float gridWidth = InventoryConstants.GRID_WIDTH * (cellSize + cellSpacing) - cellSpacing;
            float gridHeight = InventoryConstants.GRID_HEIGHT * (cellSize + cellSpacing) - cellSpacing;
            Debug.Log($"Total Grid Size: {gridWidth}x{gridHeight}");
            
            // 各セルの詳細位置を出力
            LogCellPositions();
            
            // テストアイテムの配置予測
            Debug.Log("=== TEST ITEM PLACEMENT ===");
            for (int testSize = 1; testSize <= 3; testSize++)
            {
                Vector3 testPos = GetItemTopLeftPosition(0, 0, testSize, testSize);
                Debug.Log($"{testSize}x{testSize} item at (0,0) would be placed at: {testPos}");
            }
        }
        
        /// <summary>
        /// デバッグ用：現在配置されているアイテムを表示
        /// </summary>
        [ContextMenu("Debug Placed Items")]
        public void DebugPlacedItems()
        {
            Debug.Log("=== PLACED ITEMS DEBUG ===");
            int itemCount = 0;
            
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null && !child.name.StartsWith("Cell_"))
                {
                    itemCount++;
                    Debug.Log($"Item #{itemCount}: {child.name} at {child.position} (scale: {child.localScale})");
                }
            }
            
            Debug.Log($"Total items found: {itemCount}");
        }
        
        /// <summary>
        /// ギズモ表示（エディタでグリッド位置を可視化）
        /// 座標系: X+ = 左, X- = 右, Z+ = 下, Z- = 上
        /// </summary>
        void OnDrawGizmos()
        {
            // グリッドの範囲を計算
            float gridWidth = InventoryConstants.GRID_WIDTH * (cellSize + cellSpacing) - cellSpacing;
            float gridHeight = InventoryConstants.GRID_HEIGHT * (cellSize + cellSpacing) - cellSpacing;
            
            // グリッド全体の枠を描画（transform.positionを中心として）
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, new Vector3(gridWidth, 0.1f, gridHeight));
            
            // 各セル位置を描画（左上基準座標系）
            for (int y = 0; y < InventoryConstants.GRID_HEIGHT; y++)
            {
                for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
                {
                    Vector3 cellPos = transform.position + new Vector3(
                        // X座標: (0,0)が左上なので、x増加で右（X軸マイナス方向）
                        (InventoryConstants.GRID_WIDTH - 1) * (cellSize + cellSpacing) / 2f - x * (cellSize + cellSpacing),
                        0,
                        // Z座標: (0,0)が左上なので、y増加で下（Z軸プラス方向）
                        -(InventoryConstants.GRID_HEIGHT - 1) * (cellSize + cellSpacing) / 2f + y * (cellSize + cellSpacing)
                    );
                    
                    // ロック/アンロック状態で色分け
                    if (y < initialUnlockedRows)
                    {
                        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.5f); // 緑：アンロック
                    }
                    else
                    {
                        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f); // 赤：ロック
                    }
                    
                    Gizmos.DrawWireCube(cellPos, new Vector3(cellSize, 0.1f, cellSize));
                    
                    // (0,0)セルを特別に表示
                    if (x == 0 && y == 0)
                    {
                        Gizmos.color = Color.magenta;
                        Gizmos.DrawSphere(cellPos, 0.1f); // 左上(0,0)を紫で表示
                    }
                }
            }
            
            // 中心点を描画
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(transform.position, 0.2f);
        }
        
        /// <summary>
        /// 既存セルをクリーンアップ
        /// </summary>
        private void ClearExistingCells()
        {
            if (cells != null)
            {
                for (int y = 0; y < cells.GetLength(1); y++)
                {
                    for (int x = 0; x < cells.GetLength(0); x++)
                    {
                        if (cells[x, y] != null && cells[x, y].gameObject != null)
                        {
                            if (Application.isPlaying)
                            {
                                Destroy(cells[x, y].gameObject);
                            }
                            else
                            {
                                DestroyImmediate(cells[x, y].gameObject);
                            }
                        }
                    }
                }
            }
            
            // 子オブジェクトのクリーンアップ（念のため）
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name.StartsWith("Cell_"))
                {
                    if (Application.isPlaying)
                    {
                        Destroy(child.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(child.gameObject);
                    }
                }
            }
            
            Debug.Log("[GridManager] 既存セルをクリーンアップしました");
        }
        
        /// <summary>
        /// アイテム名からグリッド位置を検索
        /// </summary>
        public bool TryGetItemPosition(string itemName, out int gridX, out int gridY, out ItemData itemData)
        {
            gridX = -1;
            gridY = -1;
            itemData = null;
            
            Debug.Log($"[GridManager] TryGetItemPosition: '{itemName}' を検索中...");
            
            for (int y = 0; y < InventoryConstants.GRID_HEIGHT; y++)
            {
                for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
                {
                    GridCell cell = GetCell(x, y);
                    if (cell != null && cell.IsOccupied && cell.OccupiedItem != null)
                    {
                        Debug.Log($"[GridManager] セル({x},{y}): アイテム '{cell.OccupiedItem.itemName}' が占有中");
                        
                        if (cell.OccupiedItem.itemName == itemName)
                        {
                            Debug.Log($"[GridManager] アイテム発見！ '{itemName}' at ({x}, {y})");
                            gridX = x;
                            gridY = y;
                            itemData = cell.OccupiedItem;
                            return true;
                        }
                    }
                    else if (cell != null && cell.IsOccupied)
                    {
                        Debug.Log($"[GridManager] セル({x},{y}): 占有中だがOccupiedItemがnull");
                    }
                }
            }
            
            Debug.LogWarning($"[GridManager] アイテム '{itemName}' が見つかりませんでした");
            return false;
        }
        
        /// <summary>
        /// デバッグ用：全占有セルの情報を出力
        /// </summary>
        [ContextMenu("占有セル情報をダンプ")]
        public void DumpOccupiedCells()
        {
            Debug.Log("=== 占有セル情報ダンプ ===");
            int occupiedCount = 0;
            
            for (int y = 0; y < InventoryConstants.GRID_HEIGHT; y++)
            {
                for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
                {
                    GridCell cell = GetCell(x, y);
                    if (cell != null && cell.IsOccupied)
                    {
                        occupiedCount++;
                        string itemName = cell.OccupiedItem?.itemName ?? "NULL";
                        Debug.Log($"セル({x},{y}): '{itemName}' (OccupiedItem: {(cell.OccupiedItem != null ? "あり" : "なし")})");
                    }
                }
            }
            
            Debug.Log($"=== 合計 {occupiedCount} セルが占有中 ===");
        }
        
        /// <summary>
        /// メモリリーク防止のためのクリーンアップ
        /// </summary>
        void OnDestroy()
        {
            Debug.Log("[GridManager] OnDestroy - メモリクリーンアップ開始");
            
            // セル配列をクリア
            if (cells != null)
            {
                for (int y = 0; y < cells.GetLength(1); y++)
                {
                    for (int x = 0; x < cells.GetLength(0); x++)
                    {
                        if (cells[x, y] != null)
                        {
                            cells[x, y] = null;
                        }
                    }
                }
                cells = null;
            }
            
            Debug.Log("[GridManager] OnDestroy - メモリクリーンアップ完了");
        }
        
        /// <summary>
        /// 全セルのインジケーター位置を一括更新
        /// </summary>
        [ContextMenu("Update All Indicator Positions")]
        public void UpdateAllIndicatorPositions()
        {
            if (cells == null) return;
            
            int updatedCount = 0;
            for (int y = 0; y < InventoryConstants.GRID_HEIGHT; y++)
            {
                for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
                {
                    if (cells[x, y] != null)
                    {
                        cells[x, y].UpdateIndicatorPosition();
                        updatedCount++;
                    }
                }
            }
            
            Debug.Log($"[GridManager] {updatedCount}個のセルのインジケーター位置を更新しました");
        }
    }
}

using UnityEngine;
using InventorySystem;

namespace InventorySystem
{
    /// <summary>
    /// InventorySystemのテスト用コントローラー
    /// キー操作でインベントリの各機能をテスト
    /// </summary>
    public class InventoryTestController : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private GridManager gridManager;
        [SerializeField] private ItemDatabase itemDatabase;
        
        [Header("テスト設定")]
        [SerializeField] private bool showDebugInfo = true;
        [SerializeField] private int testItemCount = 5;
        
        private bool isInitialized = false;
        private int currentExpandedRows = 2;

        void Start()
        {
            InitializeInventory();
        }

        void Update()
        {
            if (!isInitialized) 
            {
                return;
            }

            // I: インベントリ開閉
            if (Input.GetKeyDown(KeyCode.I))
            {
                ToggleInventory();
            }

            // Space: ランダムアイテム追加
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Debug.Log("[InventoryTest] スペースキーが押されました！");
                AddRandomItem();
            }

            // R: グリッドリセット
            if (Input.GetKeyDown(KeyCode.R))
            {
                ResetGrid();
            }

            // U: 次の行をアンロック
            if (Input.GetKeyDown(KeyCode.U))
            {
                UnlockNextRow();
            }

            // T: テストアイテム一括追加
            if (Input.GetKeyDown(KeyCode.T))
            {
                AddTestItems();
            }

            // C: 全アイテムクリア
            if (Input.GetKeyDown(KeyCode.C))
            {
                ClearAllItems();
            }

            // D: アイテムデータベース情報表示
            if (Input.GetKeyDown(KeyCode.D))
            {
                ShowDatabaseInfo();
            }
        }

        /// <summary>
        /// インベントリシステム初期化
        /// </summary>
        void InitializeInventory()
        {
            Debug.Log("[InventoryTest] システム初期化開始");

            // ItemDatabase初期化（ScriptableObject版）
            if (itemDatabase == null)
            {
                Debug.Log("[InventoryTest] itemDatabaseがnullなので、Resourcesから読み込み中...");
                itemDatabase = Resources.Load<ItemDatabase>("ItemDatabase");
            }

            if (itemDatabase != null)
            {
                Debug.Log("[InventoryTest] ItemDatabase見つかりました。LoadFromJson()を呼び出し中...");
                itemDatabase.LoadFromJson();
                var allItems = itemDatabase.GetAllItems();
                Debug.Log($"[InventoryTest] ItemDatabase読み込み完了: {allItems?.Count ?? 0}個のアイテム");
                
                // 詳細情報を出力
                if (allItems != null && allItems.Count > 0)
                {
                    Debug.Log($"[InventoryTest] 最初のアイテム: {allItems[0]?.displayName ?? "null"}");
                }
                else
                {
                    Debug.LogError("[InventoryTest] LoadFromJson後にアイテムが0個です！");
                }
            }
            else
            {
                Debug.LogError("[InventoryTest] ItemDatabaseが見つかりません！");
                return;
            }

            // GridManager初期化
            if (gridManager == null)
            {
                gridManager = FindObjectOfType<GridManager>();
            }

            if (gridManager != null)
            {
                gridManager.InitializeGrid();
                Debug.Log("[InventoryTest] GridManager初期化完了");
            }
            else
            {
                Debug.LogError("[InventoryTest] GridManagerが見つかりません！");
                return;
            }

            // InventoryManager確認
            if (InventoryManager.Instance != null)
            {
                Debug.Log("[InventoryTest] InventoryManager準備完了");
            }
            else
            {
                Debug.LogWarning("[InventoryTest] InventoryManagerが見つかりません");
            }

            isInitialized = true;
            Debug.Log("[InventoryTest] 初期化完了！テスト操作が可能です");
            PrintControls();
        }

        /// <summary>
        /// インベントリ開閉
        /// </summary>
        void ToggleInventory()
        {
            if (InventoryManager.Instance != null)
            {
                // TODO: 実際の開閉処理を実装
                Debug.Log("[InventoryTest] インベントリ開閉");
            }
        }

        /// <summary>
        /// ランダムアイテム追加
        /// </summary>
        void AddRandomItem()
        {
            Debug.Log("[InventoryTest] AddRandomItem() 呼び出し開始");
            
            if (itemDatabase == null)
            {
                Debug.LogError("[InventoryTest] itemDatabaseがnullです！");
                return;
            }

            var allItems = itemDatabase.GetAllItems();
            Debug.Log($"[InventoryTest] 取得したアイテム数: {allItems?.Count ?? 0}");
            
            if (allItems == null || allItems.Count == 0)
            {
                Debug.LogWarning("[InventoryTest] アイテムデータがありません");
                return;
            }

            CompleteItemData randomItem = allItems[Random.Range(0, allItems.Count)];
            Debug.Log($"[InventoryTest] 選択されたアイテム: {randomItem?.displayName ?? "null"}");
            Debug.Log($"[InventoryTest] アイテムの3Dモデル: {(randomItem?.fbxModel != null ? randomItem.fbxModel.name : "null")}");
            
            if (randomItem == null)
            {
                Debug.LogError("[InventoryTest] ランダムアイテムがnullです！");
                return;
            }
            
            if (randomItem.fbxModel == null)
            {
                Debug.LogWarning($"[InventoryTest] アイテム '{randomItem.displayName}' に3Dモデルが設定されていません！");
            }
            
            // ランダム位置に配置を試行
            int randomX = Random.Range(0, InventoryConstants.GRID_WIDTH - randomItem.size.x + 1);
            int randomY = Random.Range(0, currentExpandedRows - randomItem.size.y + 1);

            Debug.Log($"[InventoryTest] アイテム追加: {randomItem.displayName} (ID: {randomItem.id}) at ({randomX}, {randomY})");
            
            // GridCellにアイテムを表示
            if (gridManager != null)
            {
                for (int y = 0; y < randomItem.size.y; y++)
                {
                    for (int x = 0; x < randomItem.size.x; x++)
                    {
                        GridCell cell = gridManager.GetCell(randomX + x, randomY + y);
                        if (cell != null)
                        {
                            // 左上セルにアイテム名を表示
                            if (x == 0 && y == 0)
                            {
                                GridCellItemDisplay display = cell.GetComponent<GridCellItemDisplay>();
                                if (display == null)
                                {
                                    display = cell.gameObject.AddComponent<GridCellItemDisplay>();
                                }
                                display.DisplayItem(randomItem);
                            }
                            
                            // セルを緑色にハイライト（アイテム配置を示す）
                            cell.HighlightValid();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// グリッドリセット
        /// </summary>
        void ResetGrid()
        {
            if (gridManager != null)
            {
                // 既存のグリッドを破棄して再生成
                gridManager.InitializeGrid();
                currentExpandedRows = InventoryConstants.INITIAL_UNLOCKED_ROWS;
                Debug.Log("[InventoryTest] グリッドをリセットしました");
            }
        }

        /// <summary>
        /// 次の行をアンロック
        /// </summary>
        void UnlockNextRow()
        {
            if (gridManager != null && currentExpandedRows < InventoryConstants.GRID_HEIGHT)
            {
                currentExpandedRows++;
                gridManager.UnlockRow(1);
                Debug.Log($"[InventoryTest] 行をアンロック: 現在{currentExpandedRows}行");
            }
            else
            {
                Debug.Log("[InventoryTest] これ以上アンロックできません");
            }
        }

        /// <summary>
        /// テストアイテム一括追加
        /// </summary>
        void AddTestItems()
        {
            if (itemDatabase == null) return;

            var allItems = itemDatabase.GetAllItems();
            int addedCount = 0;
            int currentX = 0;
            int currentY = 0;

            for (int i = 0; i < Mathf.Min(testItemCount, allItems.Count); i++)
            {
                CompleteItemData item = allItems[i];
                
                // グリッドに収まるか確認
                if (currentX + item.size.x > InventoryConstants.GRID_WIDTH)
                {
                    currentX = 0;
                    currentY += 1;
                }
                
                if (currentY + item.size.y > currentExpandedRows)
                {
                    Debug.LogWarning($"[InventoryTest] グリッドがいっぱいです。{addedCount}個追加しました。");
                    break;
                }
                
                // アイテムを配置
                if (gridManager != null)
                {
                    for (int y = 0; y < item.size.y; y++)
                    {
                        for (int x = 0; x < item.size.x; x++)
                        {
                            GridCell cell = gridManager.GetCell(currentX + x, currentY + y);
                            if (cell != null)
                            {
                                // 左上セルにアイテム名を表示
                                if (x == 0 && y == 0)
                                {
                                    GridCellItemDisplay display = cell.GetComponent<GridCellItemDisplay>();
                                    if (display == null)
                                    {
                                        display = cell.gameObject.AddComponent<GridCellItemDisplay>();
                                    }
                                    display.DisplayItem(item);
                                }
                                
                                cell.HighlightValid();
                            }
                        }
                    }
                }
                
                Debug.Log($"[InventoryTest] テストアイテム追加 [{i + 1}]: {item.displayName} at ({currentX}, {currentY})");
                
                currentX += item.size.x;
                addedCount++;
            }

            Debug.Log($"[InventoryTest] {addedCount}個のテストアイテムを追加しました");
        }

        /// <summary>
        /// 全アイテムクリア
        /// </summary>
        void ClearAllItems()
        {
            Debug.Log("[InventoryTest] 全アイテムをクリア");
            
            // すべてのアイテム表示をクリア
            if (gridManager != null)
            {
                for (int y = 0; y < InventoryConstants.GRID_HEIGHT; y++)
                {
                    for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
                    {
                        GridCell cell = gridManager.GetCell(x, y);
                        if (cell != null)
                        {
                            GridCellItemDisplay display = cell.GetComponent<GridCellItemDisplay>();
                            if (display != null)
                            {
                                display.HideItem();
                            }
                        }
                    }
                }
            }
            
            // 全セルのハイライトを解除
            if (gridManager != null)
            {
                gridManager.ClearAllHighlights();
            }
        }

        /// <summary>
        /// データベース情報表示
        /// </summary>
        void ShowDatabaseInfo()
        {
            if (itemDatabase == null) return;

            var allItems = itemDatabase.GetAllItems();
            Debug.Log($"=== アイテムデータベース情報 ===");
            Debug.Log($"総アイテム数: {allItems.Count}");

            foreach (CompleteItemData item in allItems)
            {
                Debug.Log($"- {item.displayName} ({item.category}) [{item.size.x}x{item.size.y}] Rarity: {item.rarity}");
            }
        }

        /// <summary>
        /// 操作説明を表示
        /// </summary>
        void PrintControls()
        {
            Debug.Log("=== InventorySystem テスト操作 ===");
            Debug.Log("I: インベントリ開閉");
            Debug.Log("Space: ランダムアイテム追加");
            Debug.Log("T: テストアイテム一括追加");
            Debug.Log("U: 次の行をアンロック");
            Debug.Log("R: グリッドリセット");
            Debug.Log("C: 全アイテムクリア");
            Debug.Log("D: データベース情報表示");
            Debug.Log("================================");
        }

        /// <summary>
        /// デバッグ情報表示
        /// </summary>
        void OnGUI()
        {
            // 完全に無効化（TLS Allocatorエラー防止）
            return;
        }
    }
}

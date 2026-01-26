using UnityEngine;
using System.Collections;
using InventorySystem;

/// <summary>
/// インベントリシステム 3Dビジュアルテスト用コントローラー
/// </summary>
public class InventoryVisualTester : MonoBehaviour
{
    [Header("テスト設定")]
    [SerializeField] private bool enableKeyboardInput = true;
    [SerializeField] private float spawnHeight = 2.0f;
    [SerializeField] private float spawnSpacing = 2.5f; // アイテム間の基本間隔
    
    [Header("3D表示設定")]
    [SerializeField] private Transform displayArea;
    [SerializeField] private int maxItemsPerRow = 5;
    [SerializeField] [Range(0.1f, 2.0f)] private float cardScale = 1.0f; // カードサイズスケール（調整用）
    [SerializeField] private bool showItemText = true; // アイテム情報テキスト表示
    
    private int currentTestIndex = 0;
    private Vector3 nextSpawnPosition;
    
    // テスト用アイテムID配列
    private string[] testItemIds = {
        "sword_small",      // 1x1
        "sword_long",       // 1x2
        "spear",           // 1x3
        "hammer",          // 2x1
        "greatsword",      // 3x1
        "shield",          // 2x2
        "tower_shield",    // 2x3
        "plate_armor",     // 3x2
        "magic_scroll"     // 3x3
    };
    
    void Start()
    {
        InitializeDisplayArea();
        nextSpawnPosition = Vector3.zero;
        
        Debug.Log("[InventoryVisualTester] テスト開始");
        Debug.Log("操作方法:");
        Debug.Log("  SPACE: 次のアイテムをインベントリに自動配置");
        Debug.Log("  R: 全アイテムをクリア");
        Debug.Log("  1-9: 特定のアイテムをインベントリに配置");
        Debug.Log("  A: 全アイテムを一度にインベントリに配置");
    }
    
    void InitializeDisplayArea()
    {
        if (displayArea == null)
        {
            // 表示エリアが未設定の場合は自動作成
            GameObject area = new GameObject("ItemDisplayArea");
            displayArea = area.transform;
            displayArea.position = Vector3.zero;
        }
    }
    
    void Update()
    {
        if (!enableKeyboardInput) return;
        
        HandleKeyboardInput();
    }
    
    void HandleKeyboardInput()
    {
        // SPACE: 次のアイテムを順番に生成
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SpawnNextTestItem();
        }
        
        // R: 全クリア
        if (Input.GetKeyDown(KeyCode.R))
        {
            ClearAllItems();
        }
        
        // A: 全アイテムを一度に生成
        if (Input.GetKeyDown(KeyCode.A))
        {
            SpawnAllTestItems();
        }
        
        // 1-9: 特定のアイテムを生成
        for (int i = 1; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i))
            {
                SpawnTestItem(i - 1);
            }
        }
        
        // テスト情報表示切り替え
        if (Input.GetKeyDown(KeyCode.F1))
        {
            ShowItemDatabaseInfo();
        }
    }
    
    /// <summary>
    /// 次のテストアイテムを生成
    /// </summary>
    void SpawnNextTestItem()
    {
        if (currentTestIndex >= testItemIds.Length)
        {
            Debug.Log("[InventoryVisualTester] 全アイテムのテスト完了。リセットします。");
            currentTestIndex = 0;
            ClearAllItems();
        }
        
        SpawnTestItem(currentTestIndex);
        currentTestIndex++;
    }
    
    /// <summary>
    /// 指定インデックスのアイテムを生成
    /// </summary>
    void SpawnTestItem(int index)
    {
        if (index < 0 || index >= testItemIds.Length)
        {
            Debug.LogWarning($"[InventoryVisualTester] 無効なインデックス: {index}");
            return;
        }
        
        string itemId = testItemIds[index];
        
        // ItemDatabaseからアイテムデータを取得
        if (ItemDatabase.Instance == null)
        {
            Debug.LogError("[InventoryVisualTester] ItemDatabase.Instanceが見つかりません");
            return;
        }
        
        Debug.Log($"[InventoryVisualTester] ItemDatabase取得中: itemId='{itemId}'");
        
        // テスト用にアイテムIDを確認
        Debug.Log($"[InventoryVisualTester] testItemIds配列: [{string.Join(", ", testItemIds)}]");
        Debug.Log($"[InventoryVisualTester] 要求インデックス: {index}, アイテムID: '{itemId}'");
        
        ItemData itemData = ItemDatabase.Instance.GetItem(itemId);
        if (itemData == null)
        {
            Debug.LogError($"[InventoryVisualTester] アイテムが見つかりません: {itemId}");
            return;
        }
        
        // ItemDataの詳細をデバッグ出力
        Debug.Log($"[InventoryVisualTester] === ItemData詳細 ===");
        Debug.Log($"[InventoryVisualTester] itemData: {itemData}");
        Debug.Log($"[InventoryVisualTester] itemData.id: '{itemData.id}'");
        Debug.Log($"[InventoryVisualTester] itemData.itemName: '{itemData.itemName}'");
        Debug.Log($"[InventoryVisualTester] itemData.sizeX: {itemData.sizeX}");
        Debug.Log($"[InventoryVisualTester] itemData.sizeY: {itemData.sizeY}");
        Debug.Log($"[InventoryVisualTester] itemData.cardModel: {itemData.cardModel}");
        
        // itemNameがnullまたは空の場合の対処
        if (string.IsNullOrEmpty(itemData.itemName))
        {
            Debug.LogWarning($"[InventoryVisualTester] itemNameがnull/空です。idを代用: {itemData.id}");
            itemData.itemName = itemData.id ?? $"UnknownItem_{index}";
        }
        
        Debug.Log($"[InventoryVisualTester] === インベントリ自動配置のみ実行 ===");
        
        // NOTE: 重複生成防止のため、InventoryVisualTester独自の3D生成は無効化
        // GridManagerが自動的に適切な位置とスケールで3Dオブジェクトを生成します
        
        // インベントリに自動配置（GridManagerが3Dオブジェクトも生成）
        InventoryManager inventoryManager = InventoryManager.Instance;
        if (inventoryManager != null)
        {
            bool success = inventoryManager.TryAddItemAuto(itemData);
            if (success)
            {
                Debug.Log($"[InventoryVisualTester] インベントリ配置成功: {itemData.itemName} ({itemData.sizeX}x{itemData.sizeY})");
            }
            else
            {
                Debug.LogWarning($"[InventoryVisualTester] インベントリ配置失敗: {itemData.itemName} - スペース不足");
            }
        }
        else
        {
            Debug.LogError("[InventoryVisualTester] InventoryManager が見つかりません");
        }
    }
    
    /// <summary>
    /// 3Dアイテムモデルを作成
    /// </summary>
    GameObject Create3DItemModel(ItemData itemData)
    {
        // ItemDatabaseから実際の3Dモデルを取得
        ItemDatabase itemDB = ItemDatabase.Instance;
        
        Debug.Log($"[InventoryVisualTester] アイテム '{itemData.id}' の3Dモデル取得開始");
        Debug.Log($"[InventoryVisualTester] ItemDatabase.Instance: {(itemDB != null ? "見つかった" : "null")}");
        
        GameObject prefab = itemDB?.GetCardModel(itemData.id);
        Debug.Log($"[InventoryVisualTester] 取得したプレハブ: {(prefab != null ? prefab.name : "null")}");
        
        GameObject itemObject;
        
        if (prefab != null)
        {
            // 実際の3Dプレハブを使用
            Debug.Log($"[InventoryVisualTester] 実際のプレハブを使用: {prefab.name}");
            itemObject = Instantiate(prefab);
            
            // プレハブが等倍前提で作成されているため、カードスケールを適用
            itemObject.transform.localScale = Vector3.one * cardScale;
        }
        else
        {
            // フォールバック：Cubeで代用
            Debug.Log($"[InventoryVisualTester] プレハブが見つからないためCubeで代用");
            itemObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Vector3 baseScale = new Vector3(itemData.sizeX, 0.1f, itemData.sizeY);
            itemObject.transform.localScale = baseScale * cardScale;
            
            // 色分け（カテゴリー別）
            Renderer renderer = itemObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = GetCategoryColor(itemData.category);
                renderer.material = mat;
            }
        }
        
        // 名前設定
        itemObject.name = $"Item3D_{itemData.itemName}_{itemData.sizeX}x{itemData.sizeY}";
        
        // 親設定
        itemObject.transform.SetParent(displayArea);
        
        // アイテム情報を表示するTextコンポーネントを追加（オプション）
        if (showItemText)
        {
            AddItemInfoDisplay(itemObject, itemData);
        }
        
        return itemObject;
    }
    
    /// <summary>
    /// カテゴリー別の色を取得
    /// </summary>
    Color GetCategoryColor(ItemCategory category)
    {
        switch (category)
        {
            case ItemCategory.Weapon: return Color.red;
            case ItemCategory.Armor: return Color.blue;
            case ItemCategory.Consumable: return Color.green;
            case ItemCategory.Material: return Color.yellow;
            case ItemCategory.PassiveItem: return Color.magenta;
            case ItemCategory.Quest: return Color.cyan;
            default: return Color.white;
        }
    }
    
    /// <summary>
    /// アイテム情報表示を追加
    /// </summary>
    void AddItemInfoDisplay(GameObject itemObject, ItemData itemData)
    {
        // 3D Text として情報を表示
        GameObject textObj = new GameObject("ItemInfo");
        textObj.transform.SetParent(itemObject.transform);
        
        // アイテムの上部に配置（スケールに応じて調整）
        float textHeight = (itemData.sizeY * cardScale * 0.5f) + (0.3f * cardScale);
        textObj.transform.localPosition = Vector3.up * textHeight;
        
        TextMesh textMesh = textObj.AddComponent<TextMesh>();
        textMesh.text = $"{itemData.itemName}\n{itemData.sizeX}x{itemData.sizeY}";
        
        // cardScaleに応じてフォントサイズを調整（より読みやすく）
        textMesh.fontSize = Mathf.RoundToInt(20 + (30 * cardScale)); 
        textMesh.color = Color.white;
        textMesh.anchor = TextAnchor.MiddleCenter;
        
        // テキストのスケールを固定（読みやすいサイズに）
        textObj.transform.localScale = Vector3.one * (0.005f);
        
        // カメラの方向を向くようにする
        textObj.transform.LookAt(Camera.main.transform);
        textObj.transform.Rotate(0, 180, 0);
    }
    
    /// <summary>
    /// 配置位置を計算
    /// </summary>
    Vector3 CalculateSpawnPosition(int sizeX, int sizeY)
    {
        Vector3 position = nextSpawnPosition;
        
        // アイテムサイズとスケールを考慮した間隔計算
        float itemWidth = sizeX * cardScale;
        float actualSpacing = spawnSpacing * cardScale;
        
        // 次の配置位置を更新
        nextSpawnPosition.x += itemWidth + actualSpacing;
        
        // 行の最大幅を超えた場合は次の行へ
        float maxRowWidth = maxItemsPerRow * (2.0f * cardScale + actualSpacing);
        if (nextSpawnPosition.x > maxRowWidth)
        {
            nextSpawnPosition.x = 0;
            nextSpawnPosition.z += (3.0f * cardScale) + actualSpacing; // 次の行（スケール考慮）
        }
        
        return position;
    }
    
    /// <summary>
    /// 全アイテムを一度に生成
    /// </summary>
    void SpawnAllTestItems()
    {
        Debug.Log("[InventoryVisualTester] 全アイテムを生成開始");
        
        ClearAllItems();
        
        for (int i = 0; i < testItemIds.Length; i++)
        {
            SpawnTestItem(i);
        }
        
        Debug.Log("[InventoryVisualTester] 全アイテム生成完了");
    }
    
    /// <summary>
    /// 全アイテムをクリア
    /// </summary>
    void ClearAllItems()
    {
        if (displayArea == null) return;
        
        int childCount = displayArea.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(displayArea.GetChild(i).gameObject);
        }
        
        nextSpawnPosition = Vector3.zero;
        currentTestIndex = 0;
        
        Debug.Log($"[InventoryVisualTester] {childCount}個のアイテムをクリアしました");
    }
    
    /// <summary>
    /// ItemDatabase情報を表示
    /// </summary>
    void ShowItemDatabaseInfo()
    {
        if (ItemDatabase.Instance == null)
        {
            Debug.LogWarning("[InventoryVisualTester] ItemDatabaseが見つかりません");
            return;
        }
        
        var allItems = ItemDatabase.Instance.GetAllItems();
        Debug.Log($"[InventoryVisualTester] データベース情報:");
        Debug.Log($"  登録アイテム数: {allItems.Count}");
        
        foreach (var item in allItems)
        {
            Debug.Log($"  - {item.id}: {item.itemName} ({item.sizeX}x{item.sizeY}) [{item.category}]");
        }
    }
    
    /// <summary>
    /// GUI表示（操作ガイド）
    /// </summary>
    void OnGUI()
    {
        if (!enableKeyboardInput) return;
        
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = 12;
        style.normal.textColor = Color.white;
        
        string guideText = "インベントリ 3Dビジュアルテスト\\n" +
                          "━━━━━━━━━━━━━━━━━━━\\n" +
                          "SPACE: 次のアイテム生成\\n" +
                          "R: 全クリア\\n" +
                          "A: 全アイテム生成\\n" +
                          "1-9: 特定アイテム生成\\n" +
                          "F1: データベース情報表示\\n" +
                          $"\\n現在のテストインデックス: {currentTestIndex}/{testItemIds.Length}";
        
        GUI.Box(new Rect(10, Screen.height - 200, 250, 190), guideText, style);
    }
    
    /// <summary>
    /// デバッグ用：アイテムリスト表示
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    void ShowTestItemList()
    {
        Debug.Log("[InventoryVisualTester] テストアイテムリスト:");
        for (int i = 0; i < testItemIds.Length; i++)
        {
            Debug.Log($"  {i + 1}: {testItemIds[i]}");
        }
    }
}
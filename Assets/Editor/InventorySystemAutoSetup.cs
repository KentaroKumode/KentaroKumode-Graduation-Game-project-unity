using UnityEngine;
using UnityEditor;
using System.IO;
using InventorySystem;

/// <summary>
/// インベントリシステム 3D統合の自動セットアップツール
/// 手動作業以外を全て自動化
/// </summary>
public class InventorySystemAutoSetup : EditorWindow
{
    [MenuItem("Tools/Inventory System/Complete Auto Setup")]
    public static void ShowWindow()
    {
        GetWindow<InventorySystemAutoSetup>("Inventory Complete Auto Setup");
    }
    
    private void OnGUI()
    {
        EditorGUILayout.LabelField("インベントリシステム 自動セットアップ", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        EditorGUILayout.HelpBox(
            "このツールは以下を自動実行します:\n" +
            "• フォルダ構造作成\n" +
            "• items.json作成\n" +
            "• 統合版ItemDatabase作成\n" +
            "• テストシーン作成\n" +
            "• システムオブジェクト配置\n" +
            "• テストスクリプト配置\n" +
            "• メモリ監視システム配置", 
            MessageType.Info);
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("🚀 完全自動セットアップ実行", GUILayout.Height(40)))
        {
            ExecuteAutoSetup();
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("手動作業（後で実行）:", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. 3Dカードプレハブの作成（9個）\n" +
            "2. ItemDatabaseでのFBXマッピング設定", 
            MessageType.Warning);
    }
    
    private void ExecuteAutoSetup()
    {
        try
        {
            Debug.Log("🚀 インベントリシステム自動セットアップ開始");
            
            // 1. フォルダ構造作成
            CreateFolderStructure();
            
            // 2. JSONファイル作成
            CreateItemsJson();
            
            // 3. 統合版ItemDatabase作成
            CreateItemDatabase();
            
            // 4. 現在のシーンにシステム配置
            SetupInCurrentScene();
            
            // 5. システムオブジェクト配置
            SetupInventorySystem();
            
            // 6. テストスクリプト配置
            SetupTester();
            
            // 7. GridCellプレハブ作成
            CreateGridCellPrefab();
            
            // 8. メモリ監視システム配置
            SetupMemoryMonitor();
            
            Debug.Log("✅ 自動セットアップ完了！");
            EditorUtility.DisplayDialog("セットアップ完了", 
                "自動セットアップが完了しました！\n\n" +
                "次に手動作業を行ってください:\n" +
                "1. 3Dカードプレハブの作成\n" +
                "2. ItemDatabaseでのFBXマッピング設定", 
                "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ セットアップエラー: {e.Message}");
            EditorUtility.DisplayDialog("エラー", $"セットアップ中にエラーが発生しました:\n{e.Message}", "OK");
        }
    }
    
    private void CreateFolderStructure()
    {
        Debug.Log("📁 フォルダ構造作成中...");
        
        string[] folders = {
            "Assets/Data/InventorySystem",
            "Assets/Prefabs/InventorySystem/Cards",
            "Assets/Prefabs/InventorySystem/UI"
        };
        
        foreach (string folder in folders)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parentFolder = Path.GetDirectoryName(folder);
                string folderName = Path.GetFileName(folder);
                AssetDatabase.CreateFolder(parentFolder, folderName);
                Debug.Log($"  • 作成: {folder}");
            }
        }
        
        AssetDatabase.Refresh();
    }
    
    private void CreateItemsJson()
    {
        Debug.Log("📄 items.json作成中...");
        
        string jsonContent = @"{
  ""items"": [
    {
      ""id"": ""sword_small"",
      ""name"": ""小剣"",
      ""description"": ""基本的な武器。攻撃力+5"",
      ""category"": ""Weapon"",
      ""rarity"": ""Common"",
      ""sizeX"": 1,
      ""sizeY"": 1,
      ""attack"": 5,
      ""defense"": 0,
      ""health"": 0,
      ""mana"": 0,
      ""sellValue"": 10,
      ""passiveSkills"": []
    },
    {
      ""id"": ""sword_long"",
      ""name"": ""長剣"",
      ""description"": ""リーチの長い武器。攻撃力+12"",
      ""category"": ""Weapon"",
      ""rarity"": ""Uncommon"",
      ""sizeX"": 1,
      ""sizeY"": 2,
      ""attack"": 12,
      ""defense"": 0,
      ""health"": 0,
      ""mana"": 0,
      ""sellValue"": 25,
      ""passiveSkills"": []
    },
    {
      ""id"": ""spear"",
      ""name"": ""槍"",
      ""description"": ""長いリーチの武器。攻撃力+18"",
      ""category"": ""Weapon"",
      ""rarity"": ""Rare"",
      ""sizeX"": 1,
      ""sizeY"": 3,
      ""attack"": 18,
      ""defense"": 0,
      ""health"": 0,
      ""mana"": 0,
      ""sellValue"": 45,
      ""passiveSkills"": []
    },
    {
      ""id"": ""hammer"",
      ""name"": ""ハンマー"",
      ""description"": ""重い武器。攻撃力+15"",
      ""category"": ""Weapon"",
      ""rarity"": ""Uncommon"",
      ""sizeX"": 2,
      ""sizeY"": 1,
      ""attack"": 15,
      ""defense"": 0,
      ""health"": 0,
      ""mana"": 0,
      ""sellValue"": 30,
      ""passiveSkills"": []
    },
    {
      ""id"": ""greatsword"",
      ""name"": ""大剣"",
      ""description"": ""巨大な武器。攻撃力+35"",
      ""category"": ""Weapon"",
      ""rarity"": ""Epic"",
      ""sizeX"": 3,
      ""sizeY"": 1,
      ""attack"": 35,
      ""defense"": 0,
      ""health"": 0,
      ""mana"": 0,
      ""sellValue"": 100,
      ""passiveSkills"": []
    },
    {
      ""id"": ""shield"",
      ""name"": ""盾"",
      ""description"": ""基本的な防具。防御力+8"",
      ""category"": ""Armor"",
      ""rarity"": ""Common"",
      ""sizeX"": 2,
      ""sizeY"": 2,
      ""attack"": 0,
      ""defense"": 8,
      ""health"": 0,
      ""mana"": 0,
      ""sellValue"": 20,
      ""passiveSkills"": []
    },
    {
      ""id"": ""tower_shield"",
      ""name"": ""タワーシールド"",
      ""description"": ""大型の盾。防御力+15"",
      ""category"": ""Armor"",
      ""rarity"": ""Rare"",
      ""sizeX"": 2,
      ""sizeY"": 3,
      ""attack"": 0,
      ""defense"": 15,
      ""health"": 0,
      ""mana"": 0,
      ""sellValue"": 50,
      ""passiveSkills"": []
    },
    {
      ""id"": ""plate_armor"",
      ""name"": ""プレートアーマー"",
      ""description"": ""重装鎧。防御力+20、HP+25"",
      ""category"": ""Armor"",
      ""rarity"": ""Epic"",
      ""sizeX"": 3,
      ""sizeY"": 2,
      ""attack"": 0,
      ""defense"": 20,
      ""health"": 25,
      ""mana"": 0,
      ""sellValue"": 120,
      ""passiveSkills"": []
    },
    {
      ""id"": ""magic_scroll"",
      ""name"": ""魔法の巻物"",
      ""description"": ""強力な魔法アイテム。マナ+50"",
      ""category"": ""Consumable"",
      ""rarity"": ""Legendary"",
      ""sizeX"": 3,
      ""sizeY"": 3,
      ""attack"": 0,
      ""defense"": 0,
      ""health"": 0,
      ""mana"": 50,
      ""sellValue"": 200,
      ""passiveSkills"": []
    }
  ]
}";
        
        string filePath = "Assets/Data/InventorySystem/items.json";
        File.WriteAllText(filePath, jsonContent);
        AssetDatabase.Refresh();
        
        Debug.Log($"  • 作成: {filePath}");
    }
    
    /// <summary>
    /// 統合版ItemDatabaseを作成（ScriptableObject）
    /// </summary>
    private void CreateItemDatabase()
    {
        Debug.Log("📂 統合版ItemDatabase作成中...");
        
        // ScriptableObjectを作成
        ItemDatabase database = ScriptableObject.CreateInstance<ItemDatabase>();
        
        // JSONファイルを設定（もし存在したら）
        TextAsset jsonFile = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/ItemDatabase.json");
        if (jsonFile != null)
        {
            database.itemsJsonFile = jsonFile;
            Debug.Log("✅ JSONファイルを設定しました");
        }
        else
        {
            Debug.LogWarning("⚠️ JSONファイルが見つかりません: Assets/Data/ItemDatabase.json");
        }
        
        // アセットとして保存
        string path = "Assets/Resources/ItemDatabase.asset";
        AssetDatabase.CreateAsset(database, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"✅ ItemDatabaseを作成: {path}");
    }
    
    private void SetupInCurrentScene()
    {
        Debug.Log("🎬 現在のシーンにシステム配置準備中...");
        
        // 現在のシーン名を取得
        var currentScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        Debug.Log($"  • 対象シーン: {currentScene.name}");
        
        // シーンが未保存の場合は保存を促す
        if (currentScene.isDirty || string.IsNullOrEmpty(currentScene.path))
        {
            if (EditorUtility.DisplayDialog("シーン保存", 
                "現在のシーンが未保存です。\n" +
                "インベントリシステムを追加する前にシーンを保存しますか？", 
                "保存", "スキップ"))
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(currentScene);
            }
        }
        
        Debug.Log("  • 現在のシーンに追加準備完了");
    }
    
    private void SetupInventorySystem()
    {
        Debug.Log("⚙️ InventorySystem配置中...");
        
        // InventorySystemオブジェクト作成
        GameObject inventorySystem = new GameObject("InventorySystem");
        
        // コンポーネント追加
        inventorySystem.AddComponent<InventoryManager>();
        inventorySystem.AddComponent<GridManager>();
        
        // ItemDatabaseはScriptableObjectなので、Resourcesから読み込み
        ItemDatabase database = Resources.Load<ItemDatabase>("ItemDatabase");
        if (database == null)
        {
            Debug.Log("ItemDatabaseが見つからないため、新しく作成します");
            CreateItemDatabase();
        }
        
        Debug.Log("  • InventorySystem配置完了");
    }
    
    private void SetupTester()
    {
        Debug.Log("🎮 InventoryTester配置中...");
        
        GameObject tester = new GameObject("InventoryTester");
        tester.AddComponent<InventoryVisualTester>();
        
        Debug.Log("  • InventoryTester配置完了");
    }
    
    private void SetupMemoryMonitor()
    {
        Debug.Log("👁️ MemoryMonitor配置中...");
        
        GameObject monitor = new GameObject("MemoryMonitor");
        monitor.AddComponent<MemoryLeakPreventionFramework>();
        monitor.AddComponent<TLSAllocatorErrorMonitor>();
        
        Debug.Log("  • MemoryMonitor配置完了");
    }
    
    private void CreateGridCellPrefab()
    {
        Debug.Log("🎯 GridCellプレハブ作成中...");
        
        // GridCellプレハブが既にある場合はスキップ
        string prefabPath = "Assets/Prefabs/InventorySystem/GridCell.prefab";
        if (File.Exists(prefabPath))
        {
            Debug.Log("  • スキップ: GridCellプレハブが既に存在");
            return;
        }
        
        // GridCellオブジェクト作成
        GameObject gridCellObject = new GameObject("GridCell");
        
        // Cubeメッシュ追加
        var meshFilter = gridCellObject.AddComponent<MeshFilter>();
        var meshRenderer = gridCellObject.AddComponent<MeshRenderer>();
        meshFilter.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        
        // デフォルトマテリアル設定
        Material defaultMat = new Material(Shader.Find("Standard"));
        defaultMat.color = new Color(0.8f, 0.8f, 0.8f, 0.5f); // 半透明グレー
        meshRenderer.material = defaultMat;
        
        // GridCellスクリプト追加
        gridCellObject.AddComponent<InventorySystem.GridCell>();
        
        // Collider追加
        var boxCollider = gridCellObject.AddComponent<BoxCollider>();
        boxCollider.isTrigger = true;
        
        // プレハブ化
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(gridCellObject, prefabPath);
        
        // 一時オブジェクト削除
        DestroyImmediate(gridCellObject);
        
        Debug.Log($"  • 作成: {prefabPath}");
        
        // GridManagerに自動設定
        AssignGridCellPrefabToManager(prefab);
    }
    
    private void AssignGridCellPrefabToManager(GameObject gridCellPrefab)
    {
        // GridManagerを探して、cellPrefabを設定
        GridManager gridManager = FindObjectOfType<GridManager>();
        if (gridManager != null)
        {
            var serializedObject = new SerializedObject(gridManager);
            var cellPrefabProperty = serializedObject.FindProperty("cellPrefab");
            cellPrefabProperty.objectReferenceValue = gridCellPrefab;
            serializedObject.ApplyModifiedProperties();
            
            Debug.Log("  • GridManagerにcellPrefabを設定完了");
        }
        else
        {
            Debug.LogWarning("  • GridManagerが見つからないため、手動設定が必要");
        }
    }
}
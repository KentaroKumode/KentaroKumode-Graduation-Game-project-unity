using UnityEngine;
using UnityEditor;
using System.IO;
using InventorySystem;

/// <summary>
/// ItemDatabaseアセットをResourcesフォルダに自動配置するエディタユーティリティ
/// Resources.Load で確実に読み込めるようにする
/// </summary>
[InitializeOnLoad]
public static class ItemDatabaseLocationFixer
{
    private const string CorrectPath = "Assets/Resources/ItemDatabase.asset";
    
    static ItemDatabaseLocationFixer()
    {
        // エディタ起動時に自動チェック
        EditorApplication.delayCall += AutoFixLocation;
    }
    
    [MenuItem("Tools/Inventory System/Fix ItemDatabase Location")]
    public static void FixFromMenu()
    {
        if (FixLocation())
        {
            EditorUtility.DisplayDialog("完了", 
                "ItemDatabase を Resources フォルダに配置しました。\n" +
                "Resources.Load で正常にロードできます。", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("情報", 
                "ItemDatabase は既に正しい場所にあるか、アセットが見つかりませんでした。", "OK");
        }
    }
    
    private static void AutoFixLocation()
    {
        // Resourcesフォルダに既にあるか確認
        var existing = AssetDatabase.LoadAssetAtPath<ItemDatabase>(CorrectPath);
        if (existing != null) return;
        
        // 他の場所にあるか検索
        string[] guids = AssetDatabase.FindAssets("t:ItemDatabase");
        if (guids.Length == 0)
        {
            // アセットが存在しない場合は新規作成
            CreateNewItemDatabase();
            return;
        }
        
        // 見つかったアセットを移動
        string currentPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        if (currentPath != CorrectPath)
        {
            Debug.Log($"[ItemDatabaseLocationFixer] ItemDatabase を自動移動: {currentPath} → {CorrectPath}");
            FixLocation();
        }
    }
    
    private static bool FixLocation()
    {
        // Resourcesフォルダに既にあるか確認
        var existing = AssetDatabase.LoadAssetAtPath<ItemDatabase>(CorrectPath);
        if (existing != null)
        {
            Debug.Log("[ItemDatabaseLocationFixer] ItemDatabase は既に正しい場所にあります");
            return false;
        }
        
        // 他の場所にあるか検索
        string[] guids = AssetDatabase.FindAssets("t:ItemDatabase");
        if (guids.Length == 0)
        {
            Debug.LogWarning("[ItemDatabaseLocationFixer] ItemDatabase アセットが見つかりません。新規作成します。");
            CreateNewItemDatabase();
            return true;
        }
        
        // Resourcesフォルダが存在するか確認
        EnsureResourcesFolder();
        
        // アセットを移動
        string currentPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        string result = AssetDatabase.MoveAsset(currentPath, CorrectPath);
        
        if (string.IsNullOrEmpty(result))
        {
            Debug.Log($"[ItemDatabaseLocationFixer] ✅ ItemDatabase を移動しました: {currentPath} → {CorrectPath}");
            AssetDatabase.Refresh();
            return true;
        }
        else
        {
            Debug.LogError($"[ItemDatabaseLocationFixer] ❌ 移動失敗: {result}");
            
            // 移動失敗時はコピーを作成
            var source = AssetDatabase.LoadAssetAtPath<ItemDatabase>(currentPath);
            if (source != null)
            {
                var copy = ScriptableObject.CreateInstance<ItemDatabase>();
                EditorUtility.CopySerialized(source, copy);
                AssetDatabase.CreateAsset(copy, CorrectPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[ItemDatabaseLocationFixer] ✅ ItemDatabase のコピーを作成しました: {CorrectPath}");
                return true;
            }
            return false;
        }
    }
    
    private static void CreateNewItemDatabase()
    {
        EnsureResourcesFolder();
        
        var database = ScriptableObject.CreateInstance<ItemDatabase>();
        
        // JSONファイルを自動検索して設定
        TextAsset jsonFile = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/InventorySystem/items.json");
        if (jsonFile == null)
        {
            jsonFile = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/ItemDatabase.json");
        }
        
        if (jsonFile != null)
        {
            database.itemsJsonFile = jsonFile;
            Debug.Log("[ItemDatabaseLocationFixer] JSONファイルを自動設定しました");
        }
        
        AssetDatabase.CreateAsset(database, CorrectPath);
        AssetDatabase.SaveAssets();
        
        // JSONが設定されていればロード実行
        if (jsonFile != null)
        {
            database.LoadFromJson();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
        }
        
        Debug.Log($"[ItemDatabaseLocationFixer] ✅ ItemDatabase を新規作成しました: {CorrectPath}");
    }
    
    private static void EnsureResourcesFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }
    }
}

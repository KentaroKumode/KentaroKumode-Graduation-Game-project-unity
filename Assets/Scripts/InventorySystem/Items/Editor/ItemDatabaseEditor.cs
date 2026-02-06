using UnityEngine;
using UnityEditor;
using System.Linq;

namespace InventorySystem
{
    [CustomEditor(typeof(ItemDatabase))]
    public class ItemDatabaseEditor : UnityEditor.Editor
    {
        private ItemDatabase database;
        private Vector2 scrollPosition;
        
        void OnEnable()
        {
            database = (ItemDatabase)target;
        }
        
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("統合アイテム管理", EditorStyles.boldLabel);
            
            // JSON読み込みボタン
            if (GUILayout.Button("JSONからアイテムを読み込み", GUILayout.Height(30)))
            {
                if (database.itemsJsonFile == null)
                {
                    EditorUtility.DisplayDialog("エラー", "JSONファイルが設定されていません", "OK");
                    return;
                }
                
                if (EditorUtility.DisplayDialog(
                    "JSON読み込み確認",
                    "JSONファイルからアイテムを読み込みますか？\n既存のFBX割り当ては保持されます。",
                    "実行",
                    "キャンセル"))
                {
                    database.LoadFromJson();
                    EditorUtility.SetDirty(database);
                    AssetDatabase.SaveAssets();
                }
            }
            
            EditorGUILayout.Space();
            
            // 統計情報
            EditorGUILayout.LabelField("統計情報", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"登録アイテム数: {database.items.Count}");
            
            int fbxAssignedCount = database.items.Count(item => item.cardModel != null);
            
            EditorGUILayout.LabelField($"3Dモデル割り当て済み: {fbxAssignedCount}/{database.items.Count}");
            
            // プログレスバー
            if (database.items.Count > 0)
            {
                float fbxProgress = (float)fbxAssignedCount / database.items.Count;
                
                EditorGUI.ProgressBar(
                    GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true)), 
                    fbxProgress, 
                    $"3Dモデル進行度: {fbxProgress:P0}"
                );
            }
            
            EditorGUILayout.Space();
            
            // アイテム一覧表示
            if (database.items.Count > 0)
            {
                EditorGUILayout.LabelField("アイテム一覧 (3Dモデル割り当て)", EditorStyles.boldLabel);
                
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(300));
                
                foreach (var item in database.items)
                {
                    EditorGUILayout.BeginVertical("box");
                    
                    // アイテム基本情報（ReadOnly）
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.TextField("ID", item.itemId);
                    EditorGUILayout.TextField("名前", item.displayName);
                    EditorGUILayout.TextField("説明", item.description);
                    EditorGUI.EndDisabledGroup();
                    
                    item.category = (ItemCategory)EditorGUILayout.EnumPopup("カテゴリー", item.category);
                    item.rarity = (ItemRarity)EditorGUILayout.EnumPopup("レアリティ", item.rarity);
                    
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.Vector2IntField("サイズ", item.size);
                    EditorGUI.EndDisabledGroup();
                    
                    EditorGUILayout.Space(5);
                    
                    // FBX割り当て（編集可能）
                    EditorGUILayout.LabelField("Unity Assets", EditorStyles.boldLabel);
                    
                    // 3Dモデル設定（変更検知付き）
                    GameObject previousModel = item.cardModel;
                    item.cardModel = EditorGUILayout.ObjectField("3Dモデル", item.cardModel, typeof(GameObject), false) as GameObject;
                    
                    // 3Dモデルが変更されたら、アイコンも自動設定
                    if (item.cardModel != previousModel && item.cardModel != null)
                    {
                        // プレハブからスプライトを取得しようと試みる
                        var spriteRenderer = item.cardModel.GetComponentInChildren<SpriteRenderer>();
                        if (spriteRenderer != null && spriteRenderer.sprite != null)
                        {
                            item.icon = spriteRenderer.sprite;
                            Debug.Log($"[ItemDatabaseEditor] {item.displayName}: 3Dモデルからアイコンを自動設定");
                        }
                    }
                    
                    // アイコン表示（自動設定されたものまたは手動設定）
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ObjectField("アイコン（自動設定）", item.icon, typeof(Sprite), false);
                    EditorGUI.EndDisabledGroup();
                    
                    item.equipMarkPrefab = EditorGUILayout.ObjectField("装備マーク", item.equipMarkPrefab, typeof(GameObject), false) as GameObject;
                    
                    // パッシブスキル表示
                    if (item.completeData != null && item.completeData.passiveSkills != null && item.completeData.passiveSkills.Count > 0)
                    {
                        EditorGUILayout.Space(3);
                        EditorGUILayout.LabelField("パッシブスキル", EditorStyles.boldLabel);
                        EditorGUI.BeginDisabledGroup(true);
                        foreach (var skill in item.completeData.passiveSkills)
                        {
                            EditorGUILayout.LabelField($"  {skill.skillName}", skill.description);
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                    
                    // 割り当て状況表示
                    string status = "";
                    if (item.cardModel != null) status += "🎮"; // 3Dモデル設定済み
                    if (item.equipMarkPrefab != null) status += "⚡"; // 装備マーク設定済み
                    if (string.IsNullOrEmpty(status)) status = "❌未設定";
                    
                    EditorGUILayout.LabelField("状況", status);
                    
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }
                
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.HelpBox("JSONファイルを設定して「JSONからアイテムを読み込み」ボタンを押してください。", MessageType.Info);
            }
            
            // 変更を保存
            if (GUI.changed)
            {
                // CompleteItemDataを更新
                foreach (var item in database.items)
                {
                    if (item.completeData != null)
                    {
                        item.completeData.fbxModel = item.cardModel;
                        item.completeData.icon = item.icon;
                        item.completeData.equipMarkPrefab = item.equipMarkPrefab;
                    }
                }
                
                EditorUtility.SetDirty(database);
            }
        }
    }
}
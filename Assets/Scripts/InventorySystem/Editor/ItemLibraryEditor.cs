using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace InventorySystem.Editor
{
    [CustomEditor(typeof(ItemLibrary))]
    public class ItemLibraryEditor : UnityEditor.Editor
    {
        private ItemLibrary library;
        private string newItemName = "";
        private Vector2 scrollPosition;
        
        void OnEnable()
        {
            library = (ItemLibrary)target;
        }
        
        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("アイテムライブラリ", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"登録アイテム数: {library.Count}");
            
            EditorGUILayout.Space();
            
            // 新しいアイテム追加
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("新規アイテム追加:", GUILayout.Width(120));
            newItemName = EditorGUILayout.TextField(newItemName);
            if (GUILayout.Button("追加", GUILayout.Width(50)))
            {
                if (!string.IsNullOrEmpty(newItemName))
                {
                    library.AddItem(newItemName);
                    newItemName = "";
                    EditorUtility.SetDirty(library);
                }
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            
            // アイテム一覧
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            for (int i = 0; i < library.items.Count; i++)
            {
                var entry = library.items[i];
                EditorGUILayout.BeginVertical("box");
                
                // ヘッダー行
                EditorGUILayout.BeginHorizontal();
                
                // 展開/折りたたみボタン
                entry.isExpanded = EditorGUILayout.Foldout(entry.isExpanded, "", true);
                
                // 内部名
                string newInternalName = EditorGUILayout.TextField("内部名:", entry.internalName, GUILayout.Width(200));
                if (newInternalName != entry.internalName)
                {
                    entry.internalName = newInternalName;
                    if (entry.itemData != null)
                        entry.itemData.internalName = newInternalName;
                    EditorUtility.SetDirty(library);
                }
                
                // 表示名（読み取り専用）
                EditorGUILayout.LabelField($"表示名: {entry.itemData?.displayName ?? "未設定"}", GUILayout.Width(150));
                
                // レアリティ（読み取り専用）
                EditorGUILayout.LabelField($"レアリティ: {entry.itemData?.rarity.ToString() ?? "未設定"}", GUILayout.Width(120));
                
                // 削除ボタン
                if (GUILayout.Button("削除", GUILayout.Width(50)))
                {
                    library.items.RemoveAt(i);
                    EditorUtility.SetDirty(library);
                    continue;
                }
                
                EditorGUILayout.EndHorizontal();
                
                // 詳細表示
                if (entry.isExpanded)
                {
                    EditorGUILayout.Space();
                    EditorGUI.indentLevel++;
                    
                    if (entry.itemData != null)
                    {
                        DrawItemDataEditor(entry.itemData);
                    }
                    
                    EditorGUI.indentLevel--;
                }
                
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            
            EditorGUILayout.EndScrollView();
            
            // 保存とテスト機能
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("ランダムアイテム テスト"))
            {
                var randomItem = library.GetRandomItem();
                if (randomItem != null)
                {
                    Debug.Log($"ランダムアイテム: {randomItem.displayName} ({randomItem.internalName})");
                }
                else
                {
                    Debug.Log("アイテムがありません");
                }
            }
            
            if (GUILayout.Button("すべて保存"))
            {
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();
            }
            EditorGUILayout.EndHorizontal();
            
            if (GUI.changed)
            {
                EditorUtility.SetDirty(library);
            }
        }
        
        void DrawItemDataEditor(ItemDataV2 itemData)
        {
            // 基本情報
            EditorGUILayout.LabelField("基本情報", EditorStyles.boldLabel);
            itemData.displayName = EditorGUILayout.TextField("表示名", itemData.displayName);
            itemData.category = (ItemCategory)EditorGUILayout.EnumPopup("カテゴリ", itemData.category);
            itemData.rarity = (ItemRarity)EditorGUILayout.EnumPopup("レアリティ", itemData.rarity);
            itemData.fbxModel = (GameObject)EditorGUILayout.ObjectField("FBXモデル", itemData.fbxModel, typeof(GameObject), false);
            
            // 説明
            EditorGUILayout.LabelField("説明", EditorStyles.boldLabel);
            itemData.description = EditorGUILayout.TextArea(itemData.description, GUILayout.Height(40));
            
            // サイズ
            EditorGUILayout.LabelField("サイズ", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            itemData.size.x = EditorGUILayout.IntField("X", itemData.size.x, GUILayout.Width(60));
            itemData.size.y = EditorGUILayout.IntField("Y", itemData.size.y, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();
            
            // 価格設定
            EditorGUILayout.LabelField("価格設定", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("購入価格:", GUILayout.Width(80));
            itemData.buyPrice.min = EditorGUILayout.IntField("最小", itemData.buyPrice.min, GUILayout.Width(60));
            itemData.buyPrice.max = EditorGUILayout.IntField("最大", itemData.buyPrice.max, GUILayout.Width(60));
            EditorGUILayout.LabelField(itemData.buyPrice.ToString(), GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("売却価格:", GUILayout.Width(80));
            itemData.sellPrice.min = EditorGUILayout.IntField("最小", itemData.sellPrice.min, GUILayout.Width(60));
            itemData.sellPrice.max = EditorGUILayout.IntField("最大", itemData.sellPrice.max, GUILayout.Width(60));
            EditorGUILayout.LabelField(itemData.sellPrice.ToString(), GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
            
            // カテゴリ別の専用フィールド
            if (itemData.IsWeapon)
            {
                EditorGUILayout.LabelField("武器データ", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("ダイス:", GUILayout.Width(50));
                itemData.weaponDice.count = EditorGUILayout.IntField("数", itemData.weaponDice.count, GUILayout.Width(40));
                itemData.weaponDice.minValue = EditorGUILayout.IntField("最小", itemData.weaponDice.minValue, GUILayout.Width(40));
                itemData.weaponDice.maxValue = EditorGUILayout.IntField("最大", itemData.weaponDice.maxValue, GUILayout.Width(40));
                EditorGUILayout.LabelField(itemData.weaponDice.ToString(), GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
                
                // 武器のパッシブ効果
                DrawPassiveEffectsList("武器パッシブ効果", itemData.weaponPassives);
            }
            
            if (itemData.IsPassive)
            {
                DrawPassiveEffectsList("パッシブ効果", itemData.passiveEffects);
            }
            
            if (itemData.IsQuest)
            {
                EditorGUILayout.LabelField("クエストデータ", EditorStyles.boldLabel);
                itemData.flavorText = EditorGUILayout.TextArea(itemData.flavorText, GUILayout.Height(30));
                itemData.skillName = EditorGUILayout.TextField("スキル名", itemData.skillName);
            }
        }
        
        void DrawPassiveEffectsList(string title, List<PassiveEffect> effects)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            
            for (int i = 0; i < effects.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                effects[i].effectName = EditorGUILayout.TextField("効果名", effects[i].effectName);
                effects[i].value = EditorGUILayout.FloatField("値", effects[i].value, GUILayout.Width(60));
                if (GUILayout.Button("-", GUILayout.Width(20)))
                {
                    effects.RemoveAt(i);
                    continue;
                }
                EditorGUILayout.EndHorizontal();
                effects[i].description = EditorGUILayout.TextField("説明", effects[i].description);
            }
            
            if (GUILayout.Button("パッシブ効果追加"))
            {
                effects.Add(new PassiveEffect());
            }
        }
    }
}
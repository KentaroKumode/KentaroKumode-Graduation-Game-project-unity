using UnityEngine;
using System.Collections.Generic;

namespace InventorySystem
{
    /// <summary>
    /// ReadOnly属性（エディター用）
    /// </summary>
    public class ReadOnlyAttribute : PropertyAttribute { }

    /// <summary>
    /// 統合型アイテムデータベース
    /// JSON読み込み + FBX管理を一元化
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Inventory/Item Database")]
    public class ItemDatabase : ScriptableObject
    {
        [System.Serializable]
        public class ItemEntry
        {
            [Header("JSON Data")]
            [ReadOnly] public string itemId;
            [ReadOnly] public string displayName;
            [ReadOnly] public string description;
            [ReadOnly] public ItemCategory category;
            [ReadOnly] public ItemRarity rarity;
            [ReadOnly] public Vector2Int size;
            
            [Header("Unity Assets (手動設定)")]
            [Tooltip("3Dカードモデル")] 
            public GameObject cardModel;
            [Tooltip("アイコンスプライト")] 
            public Sprite icon;
            [Tooltip("装備マークプレハブ")] 
            public GameObject equipMarkPrefab;
            
            [HideInInspector] public CompleteItemData completeData;
        }
        
        [Header("データソース")]
        [Tooltip("JSONファイルまたはフォルダを設定")]
        public TextAsset itemsJsonFile;
        
        [Header("登録済みアイテム")]
        public List<ItemEntry> items = new List<ItemEntry>();
        
        private Dictionary<string, ItemEntry> itemDict;
        private static ItemDatabase instance;
        
        public static ItemDatabase Instance 
        { 
            get 
            {
                if (instance == null)
                {
                    // 1. Resourcesフォルダから検索
                    instance = Resources.Load<ItemDatabase>("ItemDatabase");
                    
                    // 2. 見つからない場合、全Resourcesフォルダを検索
                    if (instance == null)
                    {
                        var all = Resources.LoadAll<ItemDatabase>("");
                        if (all.Length > 0)
                        {
                            instance = all[0];
                        }
                    }
                    
                    #if UNITY_EDITOR
                    // 3. エディタ専用: AssetDatabaseから検索
                    if (instance == null)
                    {
                        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ItemDatabase");
                        if (guids.Length > 0)
                        {
                            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                            instance = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDatabase>(path);
                            
                            if (instance != null)
                            {
                                Debug.LogWarning($"[ItemDatabase] Resourcesフォルダ外で発見: {path}\n" +
                                    "Tools > Inventory System > Fix ItemDatabase Location で修正できます");
                            }
                        }
                    }
                    #endif
                    
                    // 4. 初期化
                    if (instance != null)
                    {
                        instance.Initialize();
                    }
                }
                return instance;
            } 
        }
        
        /// <summary>
        /// 初期化（辞書構築 + completeData がなければJSONから再構築）
        /// </summary>
        public void Initialize()
        {
            // completeData はシリアライズされないため、ランタイム起動時に再構築が必要
            bool needsRebuild = items.Count > 0 && items[0].completeData == null;

            if (needsRebuild && itemsJsonFile != null)
            {
                itemDict = null; // LoadFromJson 内で再構築される
                LoadFromJson();
                return;
            }

            if (itemDict == null)
            {
                itemDict = new Dictionary<string, ItemEntry>();
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.itemId))
                    {
                        itemDict[item.itemId] = item;
                    }
                }
            }
        }
        
        /// <summary>
        /// JSONからアイテムを読み込み・登録
        /// </summary>
        public void LoadFromJson()
        {
            if (itemsJsonFile == null)
            {
                Debug.LogWarning("[ItemDatabase] JSON file not assigned");
                return;
            }
            
            try
            {
                var jsonData = JsonUtility.FromJson<ItemDataListJson>(itemsJsonFile.text);
                if (jsonData?.items == null)
                {
                    Debug.LogError("[ItemDatabase] Failed to parse JSON");
                    return;
                }
                
                // 既存エントリを辞書化（FBX割り当てを保持するため）
                var existingEntries = new Dictionary<string, ItemEntry>();
                foreach (var existingItem in items)
                {
                    if (!string.IsNullOrEmpty(existingItem.itemId))
                    {
                        existingEntries[existingItem.itemId] = existingItem;
                    }
                }
                
                // 新しいリストを作成
                items.Clear();
                
                foreach (var jsonItem in jsonData.items)
                {
                    ItemEntry entry;
                    
                    // 既存エントリがあればFBX割り当てを保持
                    if (existingEntries.TryGetValue(jsonItem.id, out var existing))
                    {
                        entry = existing;
                    }
                    else
                    {
                        entry = new ItemEntry();
                    }
                    
                    // JSONデータを設定
                    entry.itemId = jsonItem.id;
                    entry.displayName = jsonItem.name;
                    entry.description = jsonItem.description;
                    
                    // Enum変換
                    System.Enum.TryParse(jsonItem.category, out entry.category);
                    System.Enum.TryParse(jsonItem.rarity, out entry.rarity);
                    entry.size = new Vector2Int(jsonItem.sizeX, jsonItem.sizeY);
                    
                    // CompleteItemDataを作成
                    entry.completeData = ConvertToCompleteItemData(jsonItem, entry);
                    
                    items.Add(entry);
                }
                
                // 辞書を再構築
                itemDict = null;
                Initialize();
                
                Debug.Log($"[ItemDatabase] Loaded {items.Count} items from JSON");
                
                #if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
                #endif
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ItemDatabase] Error loading JSON: {e.Message}");
            }
        }
        
        /// <summary>
        /// JSONデータをCompleteItemDataに変換
        /// </summary>
        private CompleteItemData ConvertToCompleteItemData(ItemDataJson jsonItem, ItemEntry entry)
        {
            var item = new CompleteItemData();
            
            // 基本データ設定（プロパティではなくフィールドに直接設定）
            item.internalName = jsonItem.id;
            item.displayName = jsonItem.name;
            item.description = jsonItem.description;
            item.flavorText = jsonItem.flavorText;
            
            // Enum変換
            System.Enum.TryParse(jsonItem.category, out item.category);
            System.Enum.TryParse(jsonItem.rarity, out item.rarity);
            
            item.size = new ItemSize { x = jsonItem.sizeX, y = jsonItem.sizeY };
            
            // Unity Assets設定
            item.fbxModel = entry.cardModel;
            item.icon = entry.icon;
            item.equipMarkPrefab = entry.equipMarkPrefab;
            
            // 価格設定（中央価格から±25%の範囲で購入/売却額を算出）
            if (jsonItem.basePrice > 0)
            {
                int buyMin = Mathf.RoundToInt(jsonItem.basePrice * 1.00f);
                int buyMax = Mathf.RoundToInt(jsonItem.basePrice * 1.25f);
                int sellMin = Mathf.RoundToInt(jsonItem.basePrice * 0.50f);
                int sellMax = Mathf.RoundToInt(jsonItem.basePrice * 0.75f);
                item.buyPrice = new PriceRange { min = buyMin, max = buyMax };
                item.sellPrice = new PriceRange { min = sellMin, max = sellMax };
            }
            
            // 武器ダイス設定（JSON の diceCount / diceMax から生成）
            if (jsonItem.diceCount > 0)
            {
                item.weaponDice = new DiceConfig 
                { 
                    count = jsonItem.diceCount,
                    minValue = 1,
                    maxValue = jsonItem.diceMax 
                };
            }
            
            // 会心率設定（1～9、分母は9）
            item.criticalRate = Mathf.Clamp(jsonItem.criticalRate, 0, 9);
            
            // パッシブスキル設定
            item.passiveSkills = new System.Collections.Generic.List<PassiveSkill>();
            if (jsonItem.passiveSkills != null)
            {
                foreach (var ps in jsonItem.passiveSkills)
                {
                    item.passiveSkills.Add(new PassiveSkill(ps.internalName, ps.skillName, ps.description));
                }
            }
            
            return item;
        }
        
        /// <summary>
        /// アイテムを取得
        /// </summary>
        public CompleteItemData GetItem(string itemId)
        {
            Initialize();
            
            if (itemDict != null && itemDict.TryGetValue(itemId, out ItemEntry entry))
            {
                return entry.completeData;
            }
            
            Debug.LogWarning($"[ItemDatabase] Item not found: {itemId}");
            return null;
        }
        
        /// <summary>
        /// 全アイテムを取得
        /// </summary>
        public List<CompleteItemData> GetAllItems()
        {
            Initialize();
            
            var result = new List<CompleteItemData>();
            foreach (var entry in items)
            {
                if (entry.completeData != null)
                {
                    result.Add(entry.completeData);
                }
            }
            return result;
        }
        
        /// <summary>
        /// カテゴリーでフィルタリング
        /// </summary>
        public List<CompleteItemData> GetItemsByCategory(ItemCategory category)
        {
            Initialize();
            
            var result = new List<CompleteItemData>();
            foreach (var entry in items)
            {
                if (entry.completeData != null && entry.category == category)
                {
                    result.Add(entry.completeData);
                }
            }
            return result;
        }
        
        /// <summary>
        /// アイテムのカードモデル（FBX）を取得
        /// </summary>
        public GameObject GetCardModel(string itemId)
        {
            var item = GetItem(itemId);
            return item?.fbxModel;
        }
    }
}

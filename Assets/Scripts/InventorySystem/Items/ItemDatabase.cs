using UnityEngine;
using System.Collections.Generic;

namespace InventorySystem
{
    /// <summary>
    /// アイテムデータベース
    /// JSONとScriptableObjectを統合してアイテムデータを管理
    /// </summary>
    public class ItemDatabase : MonoBehaviour
    {
        [Header("データソース")]
        [SerializeField] private TextAsset itemsJsonFile;
        [SerializeField] private ItemAssetDatabase assetDatabase;
        
        private Dictionary<string, ItemData> itemDict;
        private static ItemDatabase instance;
        
        public static ItemDatabase Instance => instance;
        
        void Awake()
        {
            // シングルトン
            if (instance != null && instance != this)
            {
                Debug.LogWarning($"[ItemDatabase] Duplicate instance detected. Destroying {gameObject.name}");
                
                // メモリリーク防止フレームワークに通知
                if (MemoryLeakPreventionFramework.Instance != null)
                {
                    MemoryLeakPreventionFramework.RegisterStaticInstance(typeof(ItemDatabase), this);
                }
                
                Destroy(gameObject);
                return;
            }
            instance = this;
            
            // メモリリーク防止フレームワークに登録
            if (MemoryLeakPreventionFramework.Instance != null)
            {
                MemoryLeakPreventionFramework.RegisterStaticInstance(typeof(ItemDatabase), this);
            }
            
            LoadItems();
        }
        
        /// <summary>
        /// アイテムデータを読み込み
        /// </summary>
        public void LoadItems()
        {
            itemDict = new Dictionary<string, ItemData>();
            
            if (itemsJsonFile == null)
            {
                Debug.LogWarning("[ItemDatabase] Items JSON file not assigned. Creating sample data.");
                CreateSampleData();
                return;
            }
            
            // JSON読み込み
            try
            {
                ItemDataListJson jsonData = JsonUtility.FromJson<ItemDataListJson>(itemsJsonFile.text);
                
                if (jsonData == null || jsonData.items == null)
                {
                    Debug.LogError("[ItemDatabase] Failed to parse JSON");
                    return;
                }
                
                // アセットデータベース初期化
                if (assetDatabase != null)
                {
                    assetDatabase.Initialize();
                }
                
                // 各アイテムを変換
                foreach (var jsonItem in jsonData.items)
                {
                    ItemData item = ConvertFromJson(jsonItem);
                    
                    // アセット参照を設定
                    if (assetDatabase != null)
                    {
                        Debug.Log($"[ItemDatabase] アセットマッピング確認中: {item.id}");
                        var assetMapping = assetDatabase.GetAssetMapping(item.id);
                        if (assetMapping != null)
                        {
                            item.cardModel = assetMapping.cardModel;
                            item.icon = assetMapping.icon;
                            item.equipMarkPrefab = assetMapping.equipMarkPrefab;
                            Debug.Log($"[ItemDatabase] マッピング成功: {item.id} -> cardModel: {(item.cardModel != null ? item.cardModel.name : "null")}");
                        }
                        else
                        {
                            Debug.LogWarning($"[ItemDatabase] マッピング見つからず: {item.id}");
                        }
                    }
                    else
                    {
                        Debug.LogError("[ItemDatabase] ItemAssetDatabase が null です");
                    }
                    
                    itemDict[item.id] = item;
                }
                
                Debug.Log($"[ItemDatabase] Loaded {itemDict.Count} items from JSON");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ItemDatabase] Error loading items: {e.Message}");
                CreateSampleData();
            }
        }
        
        /// <summary>
        /// JSONデータをItemDataに変換
        /// </summary>
        private ItemData ConvertFromJson(ItemDataJson jsonItem)
        {
            ItemData item = new ItemData
            {
                id = jsonItem.id,
                itemName = jsonItem.itemName,
                description = jsonItem.description,
                flavorText = jsonItem.flavorText,
                sizeX = jsonItem.sizeX,
                sizeY = jsonItem.sizeY,
                attack = jsonItem.attack,
                defense = jsonItem.defense,
                health = jsonItem.health,
                mana = jsonItem.mana,
                sellValue = jsonItem.sellValue
            };
            
            // Enum変換
            System.Enum.TryParse(jsonItem.category, out item.category);
            System.Enum.TryParse(jsonItem.rarity, out item.rarity);
            
            return item;
        }
        
        /// <summary>
        /// サンプルデータを作成（テスト用）
        /// </summary>
        private void CreateSampleData()
        {
            // ブロンズソード
            ItemData bronzeSword = new ItemData
            {
                id = "sword_bronze_001",
                itemName = "ブロンズソード",
                description = "初心者用の剣",
                flavorText = "誰もが最初に手にする武器",
                category = ItemCategory.Weapon,
                rarity = ItemRarity.Bronze,
                sizeX = 1,
                sizeY = 3,
                attack = 46,
                sellValue = 50
            };
            itemDict[bronzeSword.id] = bronzeSword;
            
            // アイアンソード
            ItemData ironSword = new ItemData
            {
                id = "sword_iron_001",
                itemName = "アイアンソード",
                description = "鉄製の頑丈な剣",
                flavorText = "冒険者の必需品",
                category = ItemCategory.Weapon,
                rarity = ItemRarity.Silver,
                sizeX = 1,
                sizeY = 3,
                attack = 52,
                sellValue = 100
            };
            itemDict[ironSword.id] = ironSword;
            
            // 体力ポーション
            ItemData healthPotion = new ItemData
            {
                id = "potion_health_001",
                itemName = "体力ポーション",
                description = "HPを50回復する",
                flavorText = "赤い液体が入った小瓶",
                category = ItemCategory.Consumable,
                rarity = ItemRarity.Bronze,
                sizeX = 1,
                sizeY = 1,
                health = 50,
                sellValue = 20
            };
            itemDict[healthPotion.id] = healthPotion;
            
            Debug.Log($"[ItemDatabase] Created {itemDict.Count} sample items");
        }
        
        /// <summary>
        /// アイテムを取得
        /// </summary>
        public ItemData GetItem(string itemId)
        {
            if (itemDict.TryGetValue(itemId, out ItemData item))
            {
                return item;
            }
            
            Debug.LogWarning($"[ItemDatabase] Item not found: {itemId}");
            return null;
        }
        
        /// <summary>
        /// アイテムのカードモデルを取得
        /// </summary>
        public GameObject GetCardModel(string itemId)
        {
            Debug.Log($"[ItemDatabase] GetCardModel called for: {itemId}");
            
            ItemData item = GetItem(itemId);
            if (item == null)
            {
                Debug.LogWarning($"[ItemDatabase] Item not found: {itemId}");
                return null;
            }
            
            Debug.Log($"[ItemDatabase] Item found: {item.itemName}, cardModel: {(item.cardModel != null ? item.cardModel.name : "null")}");
            return item?.cardModel;
        }
        
        /// <summary>
        /// 全アイテムを取得
        /// </summary>
        public List<ItemData> GetAllItems()
        {
            return new List<ItemData>(itemDict.Values);
        }
        
        /// <summary>
        /// カテゴリーでフィルタリング
        /// </summary>
        public List<ItemData> GetItemsByCategory(ItemCategory category)
        {
            List<ItemData> result = new List<ItemData>();
            foreach (var item in itemDict.Values)
            {
                if (item.category == category)
                {
                    result.Add(item);
                }
            }
            return result;
        }
        
        /// <summary>
        /// メモリリーク防止：静的インスタンスの安全な解除
        /// </summary>
        void OnDestroy()
        {
            if (instance == this)
            {
                // メモリリーク防止フレームワークから解除
                if (MemoryLeakPreventionFramework.Instance != null)
                {
                    MemoryLeakPreventionFramework.UnregisterStaticInstance(typeof(ItemDatabase));
                }
                
                instance = null;
                Debug.Log("[ItemDatabase] Static instance safely cleared");
            }
        }
    }
}

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
        
        private Dictionary<string, CompleteItemData> itemDict;
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
            itemDict = new Dictionary<string, CompleteItemData>();
            
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
                    CompleteItemData item = ConvertFromJson(jsonItem);
                    
                    // アセット参照を設定
                    if (assetDatabase != null)
                    {
                        Debug.Log($"[ItemDatabase] アセットマッピング確認中: {item.id}");
                        var assetMapping = assetDatabase.GetAssetMapping(item.id);
                        if (assetMapping != null)
                        {
                            // フィールドに直接代入（プロパティではなく）
                            item.icon = assetMapping.icon;
                            item.equipMarkPrefab = assetMapping.equipMarkPrefab;
                            Debug.Log($"[ItemDatabase] マッピング成功: {item.id} -> cardModel: {(assetMapping.cardModel != null ? assetMapping.cardModel.name : "null")}");
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
        /// JSONデータをCompleteItemDataに変換
        /// </summary>
        private CompleteItemData ConvertFromJson(ItemDataJson jsonItem)
        {
            CompleteItemData item = new CompleteItemData
            {
                internalName = jsonItem.id,
                displayName = jsonItem.itemName,
                description = jsonItem.description,
                flavorText = jsonItem.flavorText
            };
            
            // サイズ設定
            item.size = new ItemSize { x = jsonItem.sizeX, y = jsonItem.sizeY };
            
            // 売却価格設定
            item.sellPrice = new PriceRange { min = jsonItem.sellValue, max = jsonItem.sellValue };
            
            // Enum変換
            System.Enum.TryParse(jsonItem.category, out item.category);
            System.Enum.TryParse(jsonItem.rarity, out item.rarity);
            
            // 武器の場合、ダイス設定を作成
            if (item.category == ItemCategory.Weapon)
            {
                item.weaponDice = new DiceConfig
                {
                    minValue = jsonItem.attack - 5, // サンプル変換
                    maxValue = jsonItem.attack + 5,
                    count = 2
                };
            }
            
            return item;
        }
        
        /// <summary>
        /// サンプルデータを作成（テスト用）
        /// </summary>
        private void CreateSampleData()
        {
            // ブロンズソード
            CompleteItemData bronzeSword = new CompleteItemData
            {
                internalName = "sword_bronze_001",
                displayName = "ブロンズソード",
                description = "初心者用の剣",
                flavorText = "誰もが最初に手にする武器",
                category = ItemCategory.Weapon,
                rarity = ItemRarity.BRONZE
            };
            bronzeSword.size = new ItemSize { x = 1, y = 3 };
            bronzeSword.sellPrice = new PriceRange { min = 50, max = 50 };
            bronzeSword.weaponDice = new DiceConfig { minValue = 41, maxValue = 51, count = 2 };
            itemDict[bronzeSword.internalName] = bronzeSword;
            
            // アイアンソード
            CompleteItemData ironSword = new CompleteItemData
            {
                internalName = "sword_iron_001",
                displayName = "アイアンソード",
                description = "鉄製の頑丈な剣",
                flavorText = "冒険者の必需品",
                category = ItemCategory.Weapon,
                rarity = ItemRarity.SILVER
            };
            ironSword.size = new ItemSize { x = 1, y = 3 };
            ironSword.sellPrice = new PriceRange { min = 100, max = 100 };
            ironSword.weaponDice = new DiceConfig { minValue = 47, maxValue = 57, count = 2 };
            itemDict[ironSword.internalName] = ironSword;
            
            // 体力ポーション
            CompleteItemData healthPotion = new CompleteItemData
            {
                internalName = "potion_health_001",
                displayName = "体力ポーション",
                description = "HPを50回復する",
                flavorText = "赤い液体が入った小瓶",
                category = ItemCategory.Consumable,
                rarity = ItemRarity.BRONZE
            };
            healthPotion.size = new ItemSize { x = 1, y = 1 };
            healthPotion.sellPrice = new PriceRange { min = 20, max = 20 };
            itemDict[healthPotion.internalName] = healthPotion;
            
            Debug.Log($"[ItemDatabase] Created {itemDict.Count} sample items");
        }
        
        /// <summary>
        /// アイテムを取得
        /// </summary>
        public CompleteItemData GetItem(string itemId)
        {
            if (itemDict.TryGetValue(itemId, out CompleteItemData item))
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
            
            CompleteItemData item = GetItem(itemId);
            if (item == null)
            {
                Debug.LogWarning($"[ItemDatabase] Item not found: {itemId}");
                return null;
            }
            
            Debug.Log($"[ItemDatabase] Item found: {item.displayName}, cardModel: {(item.cardModel != null ? item.cardModel.name : "null")}");
            return item?.cardModel;
        }
        
        /// <summary>
        /// 全アイテムを取得
        /// </summary>
        public List<CompleteItemData> GetAllItems()
        {
            return new List<CompleteItemData>(itemDict.Values);
        }
        
        /// <summary>
        /// カテゴリーでフィルタリング
        /// </summary>
        public List<CompleteItemData> GetItemsByCategory(ItemCategory category)
        {
            List<CompleteItemData> result = new List<CompleteItemData>();
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

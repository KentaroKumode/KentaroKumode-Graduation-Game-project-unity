using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテムの基本データ
    /// JSONから読み込まれ、アセット参照と統合される
    /// </summary>
    [System.Serializable]
    public class ItemData
    {
        // 基本情報（JSONから）
        public string id;
        public string itemName;
        public string description;
        public string flavorText;
        public ItemCategory category;
        public ItemRarity rarity;
        
        // サイズ
        public int sizeX;
        public int sizeY;
        
        // ステータス（武器・防具・パッシブアイテム用）
        public int attack;
        public int defense;
        public int health;
        public int mana;
        
        // 価値
        public int sellValue;
        
        // アセット参照（ItemAssetDatabaseから）
        [System.NonSerialized] public GameObject cardModel;
        [System.NonSerialized] public Sprite icon;
        [System.NonSerialized] public GameObject equipMarkPrefab;
        
        /// <summary>
        /// ステータスを持つアイテムか
        /// </summary>
        public bool HasStats()
        {
            return category == ItemCategory.Weapon || 
                   category == ItemCategory.Armor || 
                   category == ItemCategory.PassiveItem;
        }
        
        /// <summary>
        /// 使用可能なアイテムか
        /// </summary>
        public bool IsUsable()
        {
            return category == ItemCategory.Consumable;
        }
        
        /// <summary>
        /// 装備可能なアイテムか
        /// </summary>
        public bool IsEquippable()
        {
            return category == ItemCategory.Weapon || category == ItemCategory.Armor;
        }
    }
    
    /// <summary>
    /// JSONからのデシリアライズ用データ構造
    /// </summary>
    [System.Serializable]
    public class ItemDataJson
    {
        public string id;
        public string itemName;
        public string description;
        public string flavorText;
        public string category;
        public string rarity;
        public int sizeX;
        public int sizeY;
        public int attack;
        public int defense;
        public int health;
        public int mana;
        public int sellValue;
    }
    
    /// <summary>
    /// JSON配列のルート
    /// </summary>
    [System.Serializable]
    public class ItemDataListJson
    {
        public ItemDataJson[] items;
    }
}

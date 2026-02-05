using UnityEngine;
using System.Collections.Generic;

namespace InventorySystem
{
    /// <summary>
    /// アイテムライブラリ - インスペクターでアイテムとFBXを1:1で紐づけ
    /// </summary>
    [CreateAssetMenu(fileName = "ItemLibrary", menuName = "Inventory System/Item Library")]
    public class ItemLibrary : ScriptableObject
    {
        [System.Serializable]
        public class ItemEntry
        {
            public string internalName;
            public ItemDataV2 itemData;
            
            [Header("プレビュー情報")]
            public bool isExpanded = false;
            
            public ItemEntry()
            {
                itemData = new ItemDataV2();
            }
            
            public ItemEntry(string name)
            {
                internalName = name;
                itemData = new ItemDataV2();
                itemData.internalName = name;
            }
        }
        
        [Header("アイテムライブラリ")]
        public List<ItemEntry> items = new List<ItemEntry>();
        
        /// <summary>
        /// 内部名でアイテムを取得
        /// </summary>
        public ItemDataV2 GetItem(string internalName)
        {
            var entry = items.Find(item => item.internalName == internalName);
            return entry?.itemData;
        }
        
        /// <summary>
        /// ランダムなアイテムを取得
        /// </summary>
        public ItemDataV2 GetRandomItem()
        {
            if (items.Count == 0) return null;
            int randomIndex = Random.Range(0, items.Count);
            return items[randomIndex].itemData;
        }
        
        /// <summary>
        /// 指定されたレアリティのランダムアイテムを取得
        /// </summary>
        public ItemDataV2 GetRandomItemByRarity(ItemRarity rarity)
        {
            var filteredItems = items.FindAll(item => item.itemData.rarity == rarity);
            if (filteredItems.Count == 0) return null;
            
            int randomIndex = Random.Range(0, filteredItems.Count);
            return filteredItems[randomIndex].itemData;
        }
        
        /// <summary>
        /// すべてのアイテムを取得
        /// </summary>
        public List<ItemDataV2> GetAllItems()
        {
            var result = new List<ItemDataV2>();
            foreach (var entry in items)
            {
                if (entry.itemData != null)
                    result.Add(entry.itemData);
            }
            return result;
        }
        
        /// <summary>
        /// アイテムを追加
        /// </summary>
        public void AddItem(string internalName)
        {
            if (items.Exists(item => item.internalName == internalName))
            {
                Debug.LogWarning($"Item with internal name '{internalName}' already exists!");
                return;
            }
            
            items.Add(new ItemEntry(internalName));
        }
        
        /// <summary>
        /// アイテムを削除
        /// </summary>
        public void RemoveItem(string internalName)
        {
            items.RemoveAll(item => item.internalName == internalName);
        }
        
        /// <summary>
        /// アイテム数を取得
        /// </summary>
        public int Count => items.Count;
        
        private void OnValidate()
        {
            // 内部名とアイテムデータの内部名を同期
            foreach (var entry in items)
            {
                if (entry.itemData != null && entry.itemData.internalName != entry.internalName)
                {
                    entry.itemData.internalName = entry.internalName;
                }
            }
        }
    }
}
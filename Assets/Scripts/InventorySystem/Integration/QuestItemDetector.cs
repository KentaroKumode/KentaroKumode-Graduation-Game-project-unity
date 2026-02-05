using UnityEngine;
using System.Collections.Generic;

namespace InventorySystem
{
    /// <summary>
    /// クエストアイテムの検知
    /// 外部のクエストシステムから呼び出される
    /// </summary>
    public class QuestItemDetector : MonoBehaviour
    {
        private List<CompleteItemData> inventoryItems = new List<CompleteItemData>();
        
        void Start()
        {
            // InventoryManagerのイベントに登録
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.OnItemAdded += OnItemAdded;
                InventoryManager.Instance.OnItemRemoved += OnItemRemoved;
            }
        }
        
        void OnDestroy()
        {
            // イベント解除
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.OnItemAdded -= OnItemAdded;
                InventoryManager.Instance.OnItemRemoved -= OnItemRemoved;
            }
        }
        
        /// <summary>
        /// アイテム追加時
        /// </summary>
        private void OnItemAdded(CompleteItemData item, int x, int y)
        {
            if (item != null)
            {
                inventoryItems.Add(item);
            }
        }
        
        /// <summary>
        /// アイテム削除時
        /// </summary>
        private void OnItemRemoved(int x, int y)
        {
            // TODO: 座標から該当アイテムを特定して削除
        }
        
        /// <summary>
        /// 特定のアイテムを所持しているか
        /// </summary>
        public bool HasItem(string itemId)
        {
            foreach (var item in inventoryItems)
            {
                if (item.id == itemId)
                {
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// 特定カテゴリーのアイテム数を取得
        /// </summary>
        public int GetItemCountByCategory(ItemCategory category)
        {
            int count = 0;
            foreach (var item in inventoryItems)
            {
                if (item.category == category)
                {
                    count++;
                }
            }
            return count;
        }
        
        /// <summary>
        /// クエストアイテムのリストを取得
        /// </summary>
        public List<CompleteItemData> GetQuestItems()
        {
            List<CompleteItemData> questItems = new List<CompleteItemData>();
            foreach (var item in inventoryItems)
            {
                if (item.category == ItemCategory.Quest)
                {
                    questItems.Add(item);
                }
            }
            return questItems;
        }
    }
}

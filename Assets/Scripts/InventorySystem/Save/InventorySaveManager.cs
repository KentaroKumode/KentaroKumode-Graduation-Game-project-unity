using UnityEngine;
using System.IO;

namespace InventorySystem
{
    /// <summary>
    /// インベントリのセーブ/ロード管理
    /// JSON形式で保存
    /// </summary>
    public class InventorySaveManager : MonoBehaviour
    {
        [Header("設定")]
        [SerializeField] private string saveFileName = "inventory_save.json";
        [SerializeField] private bool useEncryption = false;  // 将来の暗号化用
        
        private string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);
        
        /// <summary>
        /// インベントリを保存
        /// </summary>
        public void SaveInventory(InventoryData data)
        {
            try
            {
                // JSON化
                string json = JsonUtility.ToJson(data, true);
                
                // 暗号化（将来実装）
                if (useEncryption)
                {
                    // TODO: AES暗号化
                }
                
                // ファイルに書き込み
                File.WriteAllText(SavePath, json);
                
                Debug.Log($"[InventorySaveManager] Inventory saved to: {SavePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[InventorySaveManager] Save failed: {e.Message}");
            }
        }
        
        /// <summary>
        /// インベントリを読み込み
        /// </summary>
        public InventoryData LoadInventory()
        {
            if (!File.Exists(SavePath))
            {
                Debug.Log("[InventorySaveManager] No save file found. Creating new data.");
                return new InventoryData();
            }
            
            try
            {
                // ファイルから読み込み
                string json = File.ReadAllText(SavePath);
                
                // 復号化（将来実装）
                if (useEncryption)
                {
                    // TODO: AES復号化
                }
                
                // JSONからデシリアライズ
                InventoryData data = JsonUtility.FromJson<InventoryData>(json);
                
                Debug.Log($"[InventorySaveManager] Inventory loaded from: {SavePath}");
                return data;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[InventorySaveManager] Load failed: {e.Message}");
                return new InventoryData();
            }
        }
        
        /// <summary>
        /// セーブファイルを削除
        /// </summary>
        public void DeleteSave()
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
                Debug.Log("[InventorySaveManager] Save file deleted");
            }
        }
        
        /// <summary>
        /// セーブファイルが存在するか
        /// </summary>
        public bool SaveExists()
        {
            return File.Exists(SavePath);
        }
        
        /// <summary>
        /// 現在のインベントリ状態からセーブデータを作成
        /// </summary>
        public InventoryData CreateSaveData(GridManager gridManager, ItemSlot[] itemSlots)
        {
            InventoryData data = new InventoryData
            {
                expandedRows = gridManager.GetUnlockedRows()
            };
            
            // アイテムスロットから保存データを作成
            foreach (var slot in itemSlots)
            {
                if (slot != null && slot.ItemData != null)
                {
                    SavedItem savedItem = new SavedItem(
                        slot.ItemData.id,
                        slot.GridX,
                        slot.GridY,
                        slot.IsEquipped
                    );
                    data.items.Add(savedItem);
                }
            }
            
            return data;
        }
        
        /// <summary>
        /// セーブデータから インベントリを復元
        /// </summary>
        public void RestoreInventory(InventoryData data, GridManager gridManager, ItemDatabase itemDatabase)
        {
            if (data == null)
            {
                Debug.LogWarning("[InventorySaveManager] No data to restore");
                return;
            }
            
            // グリッド拡張を復元
            for (int i = InventoryConstants.INITIAL_UNLOCKED_ROWS; i < data.expandedRows; i++)
            {
                gridManager.UnlockRow(i + 1);
            }
            
            // アイテムを復元
            foreach (var savedItem in data.items)
            {
                CompleteItemData item = itemDatabase.GetItem(savedItem.itemId);
                if (item != null)
                {
                    // InventoryManagerに追加
                    InventoryManager.Instance?.AddItem(item, savedItem.gridX, savedItem.gridY);
                    
                    // 装備状態を復元
                    if (savedItem.isEquipped)
                    {
                        InventoryManager.Instance?.EquipItem(item);
                    }
                }
                else
                {
                    Debug.LogWarning($"[InventorySaveManager] Item not found: {savedItem.itemId}");
                }
            }
            
            Debug.Log($"[InventorySaveManager] Restored {data.items.Count} items, {data.expandedRows} rows");
        }
    }
}

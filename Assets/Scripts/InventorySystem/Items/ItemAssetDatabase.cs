using UnityEngine;
using System.Collections.Generic;

namespace InventorySystem
{
    /// <summary>
    /// アイテムのアセット参照を管理するScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "ItemAssetDatabase", menuName = "Inventory/Item Asset Database")]
    public class ItemAssetDatabase : ScriptableObject
    {
        [System.Serializable]
        public class ItemAssetMapping
        {
            [Tooltip("アイテムID（items.jsonのidと一致させる）")]
            public string itemId;
            
            [Tooltip("3Dカードモデル")]
            public GameObject cardModel;
            
            [Tooltip("アイコンスプライト")]
            public Sprite icon;
            
            [Tooltip("装備マークプレハブ")]
            public GameObject equipMarkPrefab;
        }
        
        [Header("アイテムアセットマッピング")]
        public List<ItemAssetMapping> assetMappings = new List<ItemAssetMapping>();
        
        private Dictionary<string, ItemAssetMapping> assetDict;
        
        /// <summary>
        /// 初期化（辞書構築）
        /// </summary>
        public void Initialize()
        {
            assetDict = new Dictionary<string, ItemAssetMapping>();
            foreach (var mapping in assetMappings)
            {
                if (!string.IsNullOrEmpty(mapping.itemId))
                {
                    assetDict[mapping.itemId] = mapping;
                }
            }
            
            Debug.Log($"[ItemAssetDatabase] Initialized with {assetDict.Count} asset mappings");
        }
        
        /// <summary>
        /// アセット参照を取得
        /// </summary>
        public ItemAssetMapping GetAssetMapping(string itemId)
        {
            if (assetDict == null)
            {
                Initialize();
            }
            
            if (assetDict.TryGetValue(itemId, out var mapping))
            {
                return mapping;
            }
            
            Debug.LogWarning($"[ItemAssetDatabase] Asset mapping not found for item: {itemId}");
            return null;
        }
    }
}

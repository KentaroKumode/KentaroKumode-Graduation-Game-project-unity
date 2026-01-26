using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace InventorySystem
{
    /// <summary>
    /// カテゴリーフィルターパネル
    /// </summary>
    public class FilterPanel : MonoBehaviour
    {
        [Header("UI要素")]
        [SerializeField] private Toggle weaponToggle;
        [SerializeField] private Toggle armorToggle;
        [SerializeField] private Toggle passiveToggle;
        [SerializeField] private Toggle materialToggle;
        [SerializeField] private Toggle consumableToggle;
        [SerializeField] private Toggle questToggle;
        [SerializeField] private Button clearFilterButton;
        
        private ItemCategory? currentFilter = null;
        private Dictionary<ItemCategory, Toggle> categoryToggles;
        
        public event System.Action<ItemCategory?> OnFilterChanged;
        
        void Start()
        {
            // トグルマッピング
            categoryToggles = new Dictionary<ItemCategory, Toggle>
            {
                { ItemCategory.Weapon, weaponToggle },
                { ItemCategory.Armor, armorToggle },
                { ItemCategory.PassiveItem, passiveToggle },
                { ItemCategory.Material, materialToggle },
                { ItemCategory.Consumable, consumableToggle },
                { ItemCategory.Quest, questToggle }
            };
            
            // イベント登録
            foreach (var kvp in categoryToggles)
            {
                if (kvp.Value != null)
                {
                    ItemCategory category = kvp.Key;
                    kvp.Value.onValueChanged.AddListener((isOn) => OnToggleChanged(category, isOn));
                }
            }
            
            if (clearFilterButton != null)
            {
                clearFilterButton.onClick.AddListener(ClearFilter);
            }
        }
        
        /// <summary>
        /// トグル変更時
        /// </summary>
        private void OnToggleChanged(ItemCategory category, bool isOn)
        {
            if (isOn)
            {
                // 他のトグルをオフ
                foreach (var kvp in categoryToggles)
                {
                    if (kvp.Key != category && kvp.Value != null)
                    {
                        kvp.Value.SetIsOnWithoutNotify(false);
                    }
                }
                
                currentFilter = category;
                OnFilterChanged?.Invoke(currentFilter);
                Debug.Log($"[FilterPanel] Filter set to: {category}");
            }
            else
            {
                // すべてオフの場合はフィルター解除
                bool anyOn = false;
                foreach (var toggle in categoryToggles.Values)
                {
                    if (toggle != null && toggle.isOn)
                    {
                        anyOn = true;
                        break;
                    }
                }
                
                if (!anyOn)
                {
                    ClearFilter();
                }
            }
        }
        
        /// <summary>
        /// フィルターをクリア
        /// </summary>
        public void ClearFilter()
        {
            currentFilter = null;
            
            // 全トグルをオフ
            foreach (var toggle in categoryToggles.Values)
            {
                if (toggle != null)
                {
                    toggle.SetIsOnWithoutNotify(false);
                }
            }
            
            OnFilterChanged?.Invoke(null);
            Debug.Log("[FilterPanel] Filter cleared");
        }
        
        /// <summary>
        /// 現在のフィルターを取得
        /// </summary>
        public ItemCategory? GetCurrentFilter()
        {
            return currentFilter;
        }
        
        /// <summary>
        /// メモリリーク防止：イベントリスナーの解除
        /// </summary>
        void OnDestroy()
        {
            // イベントリスナーを解除してメモリリークを防止
            if (categoryToggles != null)
            {
                foreach (var kvp in categoryToggles)
                {
                    if (kvp.Value != null)
                    {
                        kvp.Value.onValueChanged.RemoveAllListeners();
                    }
                }
            }
            else
            {
                // Startが走る前にDestroyされた場合のフォールバック
                SafeRemoveListener(weaponToggle);
                SafeRemoveListener(armorToggle);
                SafeRemoveListener(passiveToggle);
                SafeRemoveListener(materialToggle);
                SafeRemoveListener(consumableToggle);
                SafeRemoveListener(questToggle);
            }

            if (clearFilterButton != null)
            {
                clearFilterButton.onClick.RemoveAllListeners();
            }
        }

        private void SafeRemoveListener(Toggle toggle)
        {
            if (toggle != null)
            {
                toggle.onValueChanged.RemoveAllListeners();
            }
        }
    }
}

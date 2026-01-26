using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace InventorySystem
{
    /// <summary>
    /// マウスホバー時のツールチップ表示
    /// </summary>
    public class ItemTooltip : MonoBehaviour
    {
        [Header("UI要素")]
        [SerializeField] private GameObject tooltipPanel;
        [SerializeField] private TextMeshProUGUI itemNameText;
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private TextMeshProUGUI statsText;
        [SerializeField] private Image rarityIcon;
        
        [Header("設定")]
        [SerializeField] private float showDelay = InventoryConstants.TOOLTIP_DELAY;
        [SerializeField] private Vector2 offset = new Vector2(10, -10);
        
        private ItemData currentItem;
        private Coroutine showCoroutine;
        private bool isShowing = false;
        
        void Start()
        {
            if (tooltipPanel != null)
            {
                tooltipPanel.SetActive(false);
            }
        }
        
        /// <summary>
        /// ツールチップ表示開始
        /// </summary>
        public void ShowTooltip(ItemData item, Vector3 position)
        {
            if (item == null) return;
            
            currentItem = item;
            
            // 既存のコルーチンをキャンセル
            if (showCoroutine != null)
            {
                StopCoroutine(showCoroutine);
            }
            
            // 遅延表示
            showCoroutine = StartCoroutine(ShowTooltipDelayed(position));
        }
        
        /// <summary>
        /// ツールチップ非表示
        /// </summary>
        public void HideTooltip()
        {
            if (showCoroutine != null)
            {
                StopCoroutine(showCoroutine);
                showCoroutine = null;
            }
            
            if (tooltipPanel != null)
            {
                tooltipPanel.SetActive(false);
            }
            
            isShowing = false;
            currentItem = null;
        }
        
        /// <summary>
        /// 遅延表示コルーチン
        /// </summary>
        private IEnumerator ShowTooltipDelayed(Vector3 position)
        {
            yield return new WaitForSeconds(showDelay);
            
            if (currentItem == null) yield break;
            
            // 位置設定
            if (tooltipPanel != null)
            {
                tooltipPanel.transform.position = position + (Vector3)offset;
                tooltipPanel.SetActive(true);
            }
            
            // テキスト設定
            UpdateTooltipContent();
            
            isShowing = true;
        }
        
        /// <summary>
        /// ツールチップ内容を更新
        /// </summary>
        private void UpdateTooltipContent()
        {
            if (currentItem == null) return;
            
            // 名前
            if (itemNameText != null)
            {
                itemNameText.text = currentItem.itemName;
                itemNameText.color = GetRarityColor(currentItem.rarity);
            }
            
            // 説明
            if (descriptionText != null)
            {
                descriptionText.text = currentItem.description;
            }
            
            // ステータス（簡易版）
            if (statsText != null && currentItem.HasStats())
            {
                string stats = "";
                if (currentItem.attack > 0)
                    stats += $"攻撃: {currentItem.attack} ";
                if (currentItem.defense > 0)
                    stats += $"防御: {currentItem.defense} ";
                
                statsText.text = stats;
            }
            else if (statsText != null)
            {
                statsText.text = "";
            }
            
            // レアリティアイコン
            if (rarityIcon != null)
            {
                rarityIcon.color = GetRarityColor(currentItem.rarity);
            }
        }
        
        /// <summary>
        /// レアリティ色取得
        /// </summary>
        private Color GetRarityColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Bronze: return new Color(0.8f, 0.5f, 0.3f);
                case ItemRarity.Silver: return new Color(0.75f, 0.75f, 0.75f);
                case ItemRarity.Gold: return new Color(1f, 0.84f, 0f);
                case ItemRarity.Mythic: return new Color(0.8f, 0.2f, 0.8f);
                default: return Color.white;
            }
        }
        
        void Update()
        {
            if (isShowing && tooltipPanel != null)
            {
                // マウスに追従
                Vector3 mousePos = Input.mousePosition;
                tooltipPanel.transform.position = mousePos + (Vector3)offset;
            }
        }
        
        /// <summary>
        /// メモリリーク防止のクリーンアップ
        /// </summary>
        void OnDestroy()
        {
            // 実行中のコルーチンを停止
            if (showCoroutine != null)
            {
                StopCoroutine(showCoroutine);
                showCoroutine = null;
            }
            
            // 参照をクリア
            currentItem = null;
        }
    }
}

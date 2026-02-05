using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace InventorySystem
{
    /// <summary>
    /// アイテム情報表示パネル
    /// UI上でアイテムの詳細情報を表示するパネルシステム
    /// </summary>
    public class ItemPreviewInfoPanel : MonoBehaviour
    {
        [Header("UI要素")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI itemNameText;
        [SerializeField] private TextMeshProUGUI itemDescriptionText;
        [SerializeField] private TextMeshProUGUI itemStatsText;
        [SerializeField] private TextMeshProUGUI itemFlavorText;
        [SerializeField] private Image itemIcon;
        [SerializeField] private Image backgroundPanel;
        
        [Header("レアリティカラー")]
        [SerializeField] private bool useRarityColors = true;
        [SerializeField] private Color commonColor = Color.white;
        [SerializeField] private Color rareColor = Color.blue;
        [SerializeField] private Color epicColor = Color.magenta;
        [SerializeField] private Color legendaryColor = new Color(1f, 0.5f, 0f);
        
        [Header("パネル設定")]
        [SerializeField] private bool autoHidePanel = true;
        [SerializeField] private float autoHideDelay = 3.0f;
        [SerializeField] private bool showItemIcon = true;
        [SerializeField] private bool showFlavorText = true;
        
        [Header("アニメーション")]
        [SerializeField] private bool useSlideAnimation = true;
        [SerializeField] private float animationDuration = 0.3f;
        [SerializeField] private AnimationCurve slideInCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        
        // プライベ�EトフィールチE
        private CompleteItemData currentItem;
        private bool isVisible = false;
        private Coroutine hideCoroutine;
        private Vector3 hiddenPosition;
        private Vector3 shownPosition;
        
        void Start()
        {
            InitializePanel();
            
            if (panelRoot != null)
            {
                shownPosition = panelRoot.transform.localPosition;
                hiddenPosition = shownPosition + new Vector3(-Screen.width, 0, 0);
                
                if (autoHidePanel)
                {
                    HidePanel();
                }
            }
        }
        
        /// <summary>
        /// パネル初期匁E
        /// </summary>
        private void InitializePanel()
        {
            if (panelRoot == null)
            {
                panelRoot = gameObject;
            }
            
            // 忁E��なコンポ�Eネントを自動検索
            if (itemNameText == null)
                itemNameText = transform.Find("ItemName")?.GetComponent<TextMeshProUGUI>();
            
            if (itemDescriptionText == null)
                itemDescriptionText = transform.Find("Description")?.GetComponent<TextMeshProUGUI>();
            
            if (itemStatsText == null)
                itemStatsText = transform.Find("Stats")?.GetComponent<TextMeshProUGUI>();
            
            if (itemFlavorText == null)
                itemFlavorText = transform.Find("FlavorText")?.GetComponent<TextMeshProUGUI>();
            
            if (itemIcon == null)
                itemIcon = transform.Find("Icon")?.GetComponent<Image>();
            
            if (backgroundPanel == null)
                backgroundPanel = GetComponent<Image>();
        }
        
        /// <summary>
        /// アイチE��惁E��を表示
        /// </summary>
        public void ShowItemInfo(CompleteItemData itemData)
        {
            currentItem = itemData;
            
            UpdatePanelContent(itemData);
            ShowPanel();
            
            Debug.Log($"[ItemPreviewInfoPanel] Showing item: {itemData.displayName}");
        }
        
        /// <summary>
        /// パネル内容を更新
        /// </summary>
        private void UpdatePanelContent(CompleteItemData itemData)
        {
            // アイテム名
            if (itemNameText != null && itemData.displayName != null)
            {
                itemNameText.text = itemData.displayName;
                
                if (useRarityColors)
                {
                    itemNameText.color = RarityColorUtility.GetRarityColor(itemData.rarity);
                }
            }
            
            // 説明文
            if (itemDescriptionText != null && !string.IsNullOrEmpty(itemData.description))
            {
                itemDescriptionText.text = itemData.description;
            }
            
            // ステータス
            if (itemStatsText != null)
            {
                itemStatsText.text = GenerateStatsText(itemData);
            }
            
            // フレーバーテキスト
            if (itemFlavorText != null && showFlavorText)
            {
                itemFlavorText.text = !string.IsNullOrEmpty(itemData.flavorText) ? 
                    $"\"{itemData.flavorText}\"" : "";
            }
            
            // アイコン
            if (itemIcon != null && showItemIcon && itemData.itemIcon != null)
            {
                itemIcon.sprite = itemData.itemIcon;
                itemIcon.gameObject.SetActive(true);
            }
            else if (itemIcon != null)
            {
                itemIcon.gameObject.SetActive(false);
            }
            
            // 背景色�E�レアリチE���E�E
            if (backgroundPanel != null && useRarityColors)
            {
                Color bgColor = RarityColorUtility.GetRarityColor(itemData.rarity);
                bgColor.a = 0.3f;
                backgroundPanel.color = bgColor;
            }
        }
        
        /// <summary>
        /// スチE�EタスチE��ストを生�E
        /// </summary>
        private string GenerateStatsText(CompleteItemData itemData)
        {
            List<string> stats = new List<string>();
            
            // サイズ惁E��
            stats.Add($"サイズ: {itemData.size.x}x{itemData.size.y}");
            
            // ダイス情報表示（武器のみ）
            if (itemData.hasWeaponStats)
            {
                var dice = itemData.weaponStats;
                stats.Add($"攻撃力: {dice.count}d({dice.minValue}-{dice.maxValue})");
            }
            
            // レアリティとカテゴリ
            stats.Add($"レアリティ: {itemData.rarity}");
            stats.Add($"カテゴリ: {itemData.category}");
            
            // 売却価格
            if (itemData.economy != null && itemData.economy.baseValue > 0)
                stats.Add($"売却価格: {itemData.economy.baseValue}G");
            
            return string.Join("\n", stats);
        }
        
        /// <summary>
        /// <summary>
        /// パネルを表示
        /// </summary>
        public void ShowPanel()
        {
            if (isVisible) return;
            
            isVisible = true;
            
            if (hideCoroutine != null)
            {
                StopCoroutine(hideCoroutine);
                hideCoroutine = null;
            }
            
            if (useSlideAnimation && panelRoot != null)
            {
                StartCoroutine(SlideAnimation(hiddenPosition, shownPosition));
            }
            else if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }
            
            if (autoHidePanel)
            {
                hideCoroutine = StartCoroutine(AutoHideCoroutine());
            }
        }
        
        /// <summary>
        /// パネルを非表示
        /// </summary>
        public void HidePanel()
        {
            if (!isVisible) return;
            
            isVisible = false;
            
            if (hideCoroutine != null)
            {
                StopCoroutine(hideCoroutine);
                hideCoroutine = null;
            }
            
            if (useSlideAnimation && panelRoot != null)
            {
                StartCoroutine(SlideAnimation(shownPosition, hiddenPosition));
            }
            else if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }
        
        /// <summary>
        /// スライドアニメーション
        /// </summary>
        private System.Collections.IEnumerator SlideAnimation(Vector3 from, Vector3 to)
        {
            if (panelRoot == null) yield break;
            
            float elapsed = 0;
            
            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / animationDuration;
                float curveValue = slideInCurve.Evaluate(t);
                
                panelRoot.transform.localPosition = Vector3.Lerp(from, to, curveValue);
                
                yield return null;
            }
            
            panelRoot.transform.localPosition = to;
            
            if (!isVisible)
            {
                panelRoot.SetActive(false);
            }
        }
        
        /// <summary>
        /// 自動非表示コルーチン
        /// </summary>
        private System.Collections.IEnumerator AutoHideCoroutine()
        {
            yield return new WaitForSeconds(autoHideDelay);
            HidePanel();
        }
        
        /// <summary>
        /// 表示/非表示を�Eり替ぁE
        /// </summary>
        public void TogglePanel()
        {
            if (isVisible)
                HidePanel();
            else
                ShowPanel();
        }
        
        /// <summary>
        /// パネル表示の可視性を確誁E
        /// </summary>
        public bool IsVisible => isVisible;
        
        /// <summary>
        /// 現在表示中のアイチE��を取征E
        /// </summary>
        public CompleteItemData GetCurrentItem() => currentItem;
        
        [ContextMenu("Show Test Item")]
        public void ShowTestItem()
        {
            // チE��ト用のダミ�EアイチE��チE�Eタを作�Eして表示
            Debug.Log("[ItemPreviewInfoPanel] Test item display requested");
        }
    }
}

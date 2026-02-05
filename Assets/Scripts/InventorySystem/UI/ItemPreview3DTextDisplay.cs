using UnityEngine;
using TMPro;

namespace InventorySystem
{
    /// <summary>
    /// アイテムプレビュー用最適化3Dテキストシステム
    /// プレビュー時に背景より少し前の3D空闓にテキスト情報を表示
    /// </summary>
    public class ItemPreview3DTextDisplay : MonoBehaviour
    {
        [Header("表示設定")]
        [SerializeField] private bool enableDisplay = true;
        [SerializeField] private Vector3 textPosition = new Vector3(0, 0, 2f);
        [SerializeField] private Vector3 textRotation = Vector3.zero;
        [SerializeField] private float textScale = 5f;
        
        [Header("テキスト構成")]
        [SerializeField] private bool showItemName = true;
        [SerializeField] private bool showDescription = true;
        [SerializeField] private bool showStats = true;
        [SerializeField] private bool showCategory = true;
        [SerializeField] private bool showFlavorText = false;
        [SerializeField] private bool showDetailedInfo = true;
        
        [Header("3Dテキスト設定")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private float nameFontSize = 3f;
        [SerializeField] private float infoFontSize = 2f;
        [SerializeField] private Color nameColor = Color.white;
        [SerializeField] private Color infoColor = Color.gray;
        [SerializeField] private Color statsColor = Color.yellow;
        
        [Header("背景設定")]
        [SerializeField] private bool enableBackground = true;
        [SerializeField] private GameObject backgroundPrefab;
        
        // プライベ�EトフィールチE
        private CompleteItemData currentItem;
        private GameObject nameTextObj;
        private GameObject infoTextObj;
        private GameObject backgroundObj;
        private TextMeshPro nameTextMesh;
        private TextMeshPro infoTextMesh;
        
        void Awake()
        {
            if (enableDisplay)
            {
                InitializeComponents();
            }
            
            gameObject.SetActive(false);
        }
        
        public void ShowItemInfo(CompleteItemData itemData)
        {
            if (!enableDisplay || itemData == null) return;
            
            currentItem = itemData;
            gameObject.SetActive(true);
            
            UpdateNameText(itemData);
            UpdateInfoText(itemData);
            
            ApplyRarityEffects(itemData.rarity);
            PositionInFrontOfCamera();
        }
        
        public void HideDisplay()
        {
            gameObject.SetActive(false);
            currentItem = null;
        }
        
        private void InitializeComponents()
        {
            try
            {
                CreateNameText();
                CreateInfoText();
                CreateBackground();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ItemPreview3DTextDisplay] 初期化エラー: {e.Message}");
            }
        }
        
        private void CreateNameText()
        {
            nameTextObj = new GameObject("NameText");
            nameTextObj.transform.SetParent(transform, false);
            
            nameTextMesh = nameTextObj.AddComponent<TextMeshPro>();
            nameTextMesh.text = "アイテム名";
            nameTextMesh.fontSize = nameFontSize;
            nameTextMesh.color = nameColor;
            nameTextMesh.alignment = TextAlignmentOptions.Center;
            nameTextMesh.fontStyle = FontStyles.Bold;
            
            if (font != null)
            {
                nameTextMesh.font = font;
            }
        }
        
        private void CreateInfoText()
        {
            infoTextObj = new GameObject("InfoText");
            infoTextObj.transform.SetParent(transform, false);
            infoTextObj.transform.localPosition = new Vector3(0, -1f, 0);
            
            infoTextMesh = infoTextObj.AddComponent<TextMeshPro>();
            infoTextMesh.text = "惁E��";
            infoTextMesh.fontSize = infoFontSize;
            infoTextMesh.color = infoColor;
            infoTextMesh.alignment = TextAlignmentOptions.Center;
            
            if (font != null)
            {
                infoTextMesh.font = font;
            }
        }
        
        private void CreateBackground()
        {
            if (enableBackground && backgroundPrefab != null)
            {
                backgroundObj = Instantiate(backgroundPrefab, transform);
                backgroundObj.transform.localPosition = new Vector3(0, 0, 0.1f);
            }
        }
        
        private void PositionInFrontOfCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            
            Vector3 cameraForward = cam.transform.forward;
            Vector3 worldPosition = cam.transform.position + cameraForward * textPosition.z;
            
            transform.position = worldPosition;
            transform.rotation = Quaternion.LookRotation(cameraForward);
            transform.localScale = Vector3.one * textScale;
        }
        
        private void UpdateNameText(CompleteItemData itemData)
        {
            if (nameTextMesh == null) return;
            
            string nameText = itemData.displayName;
            
            if (showCategory)
            {
                nameText += $"\n<size=70%><color=#CCCCCC>[{itemData.rarity} {itemData.category}]</color></size>";
            }
            
            nameTextMesh.text = nameText;
        }
        
        private void UpdateInfoText(CompleteItemData itemData)
        {
            if (infoTextMesh == null) return;
            
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            
            if (showDescription && !string.IsNullOrEmpty(itemData.description))
            {
                sb.AppendLine($"<color=#CCCCCC>{itemData.description}</color>");
                sb.AppendLine();
            }
            
            if (showStats)
            {
                System.Collections.Generic.List<string> stats = new System.Collections.Generic.List<string>();
                
                if (showDetailedInfo)
                {
                    if (itemData.hasWeaponStats)
                    {
                        var dice = itemData.weaponStats;
                        stats.Add($"<color=#FFFF00>ATK {dice.count}d({dice.minValue}-{dice.maxValue})</color>");
                    }
                    
                    if (showCategory)
                    {
                        stats.Add($"Size {itemData.size.x}×{itemData.size.y}");
                        stats.Add($"Value {itemData.economy?.baseValue ?? 0}G");
                    }
                }
                else
                {
                    stats.Add($"Size {itemData.size.x}×{itemData.size.y}");
                    stats.Add($"Value {itemData.economy?.baseValue ?? 0}G");
                }
                
                if (stats.Count > 0)
                {
                    sb.AppendLine(string.Join("  ", stats));
                }
            }
            
            infoTextMesh.text = sb.ToString();
        }
        
        private void ApplyRarityEffects(ItemRarity rarity)
        {
            Color rarityColor = RarityColorUtility.GetRarityColor(rarity);
            
            if (backgroundObj != null)
            {
                Renderer bgRenderer = backgroundObj.GetComponent<Renderer>();
                if (bgRenderer != null)
                {
                    bgRenderer.material.color = rarityColor;
                }
            }
        }
        
        public void Hide()
        {
            gameObject.SetActive(false);
        }
        
        /// <summary>
        /// テキストを表示する
        /// </summary>
        /// <param name="text">表示するテキスト</param>
        /// <param name="offset">表示位置のオフセット（オプション）</param>
        /// <param name="fontSize">フォントサイズ（オプション）</param>
        public void ShowText(string text, Vector3? offset = null, float? fontSize = null)
        {
            if (nameTextMesh != null)
            {
                nameTextMesh.text = text;
                if (fontSize.HasValue)
                    nameTextMesh.fontSize = fontSize.Value;
            }
            
            if (offset.HasValue)
                SetTextPosition(offset.Value);
                
            gameObject.SetActive(true);
        }
        
        /// <summary>
        /// テキストの位置を設定する
        /// </summary>
        /// <param name="position">新しい位置</param>
        public void SetTextPosition(Vector3 position)
        {
            textPosition = position;
            if (nameTextObj != null)
            {
                nameTextObj.transform.localPosition = textPosition;
            }
            if (infoTextObj != null)
            {
                infoTextObj.transform.localPosition = textPosition + Vector3.down * 1f;
            }
        }
        
        void OnValidate()
        {
            if (Application.isPlaying && currentItem != null)
            {
                ShowItemInfo(currentItem);
            }
        }
    }
}

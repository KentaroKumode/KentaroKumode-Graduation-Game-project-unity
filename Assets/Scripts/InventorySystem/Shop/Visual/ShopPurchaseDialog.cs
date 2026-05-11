using UnityEngine;
using GameLoop;

namespace InventorySystem.Shop.Visual
{
    /// <summary>
    /// 商品クリック時の確認ダイアログ。
    /// アイテム情報と「購入する/しない」を表示。3Dカードプレビューはインベントリ既存系を流用想定。
    ///
    /// 現状は IMGUI 描画ベース（最小実装）。後から本格的な UI に差し替え可能。
    /// </summary>
    public class ShopPurchaseDialog : MonoBehaviour
    {
        public static ShopPurchaseDialog Instance { get; private set; }

        [Header("オプション")]
        [SerializeField] private bool useIMGUIFallback = true;
        [SerializeField] private int fontSize = 16;

        public bool IsOpen { get; private set; }

        private int slotIndex = -1;
        private ShopSlot slot;
        private CompleteItemData itemData;
        private GameObject previewCardObject;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void Open(int slotIndex, ShopSlot slot)
        {
            if (slot == null) return;
            this.slotIndex = slotIndex;
            this.slot = slot;
            this.itemData = string.IsNullOrEmpty(slot.itemId)
                ? null
                : ItemDatabase.Instance?.GetItem(slot.itemId);
            IsOpen = true;
            SpawnPreviewCard();
        }

        public void Close()
        {
            IsOpen = false;
            slotIndex = -1;
            slot = null;
            itemData = null;
            DestroyPreviewCard();
        }

        public void ConfirmPurchase()
        {
            if (!IsOpen) return;
            ShopManager.Instance?.TryBuy(slotIndex, GameManager.Instance?.Run);
            Close();
        }

        // ============================================================
        //  3D プレビューカード（インベントリの cardModel を流用）
        // ============================================================

        private void SpawnPreviewCard()
        {
            DestroyPreviewCard();
            if (itemData?.fbxModel == null) return;

            previewCardObject = Instantiate(itemData.fbxModel);
            previewCardObject.name = "ShopPurchasePreview";
            // カメラ前方に配置（簡易）
            var cam = Camera.main;
            if (cam != null)
            {
                previewCardObject.transform.position = cam.transform.position + cam.transform.forward * 2f;
                previewCardObject.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
            }
            previewCardObject.transform.localScale = Vector3.one * 1.5f;
        }

        private void DestroyPreviewCard()
        {
            if (previewCardObject != null)
            {
                Destroy(previewCardObject);
                previewCardObject = null;
            }
        }

        // ============================================================
        //  IMGUI フォールバック描画（仮UI）
        // ============================================================

        void OnGUI()
        {
            if (!IsOpen || !useIMGUIFallback) return;

            var style = new GUIStyle(GUI.skin.box) { fontSize = fontSize, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 10, 10) };
            var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = fontSize };

            float w = 380f, h = 220f;
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(rect, "", GUI.skin.box);

            GUILayout.BeginArea(rect, style);
            string name = itemData != null ? itemData.displayName : (slot?.itemId ?? "（不明）");
            string desc = itemData != null ? itemData.description : "";
            string rarity = itemData != null ? itemData.rarity.ToString() : "-";
            int price = slot?.price ?? 0;
            int coins = GameManager.Instance?.Run?.coins ?? 0;
            bool canAfford = coins >= price;

            GUILayout.Label($"<b>{name}</b>  [{rarity}]", new GUIStyle(GUI.skin.label) { richText = true, fontSize = fontSize + 2 });
            GUILayout.Space(4);
            GUILayout.Label(desc, new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = fontSize });
            GUILayout.Space(8);
            GUILayout.Label($"価格: {price}G   /   所持: {coins}G",
                new GUIStyle(GUI.skin.label) { fontSize = fontSize, normal = { textColor = canAfford ? Color.white : Color.red } });
            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            GUI.enabled = canAfford && (slot != null && !slot.sold);
            if (GUILayout.Button("購入する", btnStyle, GUILayout.Height(36)))
                ConfirmPurchase();
            GUI.enabled = true;
            if (GUILayout.Button("やめる", btnStyle, GUILayout.Height(36)))
                Close();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}

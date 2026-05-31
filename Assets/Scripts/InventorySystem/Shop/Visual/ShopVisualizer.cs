using System.Collections.Generic;
using UnityEngine;
using GameLoop;

namespace InventorySystem.Shop.Visual
{
    /// <summary>
    /// ショップマス突入時に展開するビジュアル。
    /// 下地テクスチャ平面 + 7スロットの枠を生成し、各枠は ShopSlotVisual を持つ。
    /// クリックで購入ダイアログ (ShopPurchaseDialog) を開く。
    /// </summary>
    public class ShopVisualizer : MonoBehaviour
    {
        public static ShopVisualizer Instance { get; private set; }

        [Header("下地")]
        [SerializeField] private Texture2D backgroundTexture;
        [SerializeField] private float backgroundSizePx = 1024f;
        [SerializeField] private float pixelsPerUnit = 32f;
        [SerializeField] private int backgroundSortingOrder = -10;
        [SerializeField] private Shader backgroundShader;     // null なら Sprites/Default

        [Header("枠")]
        [Tooltip("ShopSlotVisual を持つプレハブ。null時は基本枠を自動生成")]
        [SerializeField] private GameObject slotFramePrefab;
        [SerializeField] private float slotSizeUnits = 1.6f;
        [Tooltip("レイアウト: 上段4枠（パッシブ2,武器,ダイス）, 下段3枠（消費2,強化素材）")]
        [SerializeField] private Vector2 slotSpacing = new Vector2(1.9f, 2.1f);

        [Header("Tier 別 枠テクスチャ")]
        [Tooltip("BRONZE (Tier1)")] [SerializeField] private Texture2D frameBronze;
        [Tooltip("SILVER (Tier2)")] [SerializeField] private Texture2D frameSilver;
        [Tooltip("GOLD (Tier3) — 強化素材枠もここを使用")] [SerializeField] private Texture2D frameGold;
        [Tooltip("LEGENDARY (Tier4)")] [SerializeField] private Texture2D frameLegendary;

        [Header("3Dモデル表示")]
        [Tooltip("枠内3Dモデルの Y軸回転速度（度/秒）")]
        [SerializeField] private float modelRotationSpeedDegPerSec = 60f;
        [Tooltip("枠内3Dモデルのスケール倍率")]
        [SerializeField] private float modelScale = 0.5f;

        [Header("ルート")]
        [SerializeField] private Transform shopRoot;          // null時は自動生成

        private readonly List<ShopSlotVisual> spawnedSlots = new List<ShopSlotVisual>();
        private ShopRerollButton spawnedRerollButton;
        private ShopRobberyButton spawnedRobberyButton;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable()
        {
            if (ShopManager.Instance != null)
            {
                ShopManager.Instance.OnShopOpened += HandleShopOpened;
                ShopManager.Instance.OnShopUpdated += HandleShopUpdated;
                ShopManager.Instance.OnShopClosed += HandleShopClosed;
            }
        }

        void OnDisable()
        {
            if (ShopManager.Instance != null)
            {
                ShopManager.Instance.OnShopOpened -= HandleShopOpened;
                ShopManager.Instance.OnShopUpdated -= HandleShopUpdated;
                ShopManager.Instance.OnShopClosed -= HandleShopClosed;
            }
        }

        // ============================================================
        //  公開
        // ============================================================

        public void Hide()
        {
            if (shopRoot != null) shopRoot.gameObject.SetActive(false);
        }

        public void Show()
        {
            if (shopRoot != null) shopRoot.gameObject.SetActive(true);
        }

        // ============================================================
        //  生成
        // ============================================================

        private void HandleShopOpened(ShopInventory inv)
        {
            EnsureRoot();
            ClearSlots();
            SpawnBackground();
            SpawnSlots(inv);
            SpawnRerollButton();
            SpawnRobberyButtonIfUnlocked();
            Show();
        }

        private void HandleShopUpdated()
        {
            // 価格や売却済みなど、各スロットに再描画を依頼
            foreach (var s in spawnedSlots)
                if (s != null) s.RefreshDisplay();
            if (spawnedRerollButton != null) spawnedRerollButton.Refresh();
        }

        private void HandleShopClosed()
        {
            ClearSlots();
            Hide();
        }

        private void EnsureRoot()
        {
            if (shopRoot != null) return;
            var go = new GameObject("[ShopRoot]");
            go.transform.SetParent(transform, false);
            // マップと同じく XZ 平面に寝かせる（X=-90°）
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            shopRoot = go.transform;
        }

        private void SpawnBackground()
        {
            // 既存の背景があれば再利用しない（クリアしている）
            float worldSize = backgroundSizePx / pixelsPerUnit;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "ShopBackground";
            // Quad のコライダ削除（背景なのでクリック不要）
            var col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            quad.transform.SetParent(shopRoot, false);
            quad.transform.localPosition = Vector3.zero;
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(worldSize, worldSize, 1f);

            var sr = quad.GetComponent<MeshRenderer>();
            var shader = backgroundShader != null ? backgroundShader : Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (backgroundTexture != null)
            {
                backgroundTexture.filterMode = FilterMode.Point;
                mat.mainTexture = backgroundTexture;
            }
            sr.sharedMaterial = mat;
            sr.sortingOrder = backgroundSortingOrder;
        }

        private void SpawnSlots(ShopInventory inv)
        {
            if (inv == null) return;

            // レイアウト: 上段4 (index 0-3), 下段3 (index 4-6)
            // 上段X: -1.5, -0.5, +0.5, +1.5 (× spacing.x)
            // 下段X: -1.0, 0, +1.0
            // 上段Y: +0.5*spacing.y, 下段Y: -0.5*spacing.y
            int upper = Mathf.Min(4, inv.slots.Count);
            int lower = inv.slots.Count - upper;

            for (int i = 0; i < upper; i++)
            {
                float x = (i - (upper - 1) * 0.5f) * slotSpacing.x;
                float y = +0.5f * slotSpacing.y;
                SpawnSlot(i, inv.slots[i], new Vector2(x, y));
            }
            for (int j = 0; j < lower; j++)
            {
                int idx = upper + j;
                float x = (j - (lower - 1) * 0.5f) * slotSpacing.x;
                float y = -0.5f * slotSpacing.y;
                SpawnSlot(idx, inv.slots[idx], new Vector2(x, y));
            }
        }

        private void SpawnSlot(int slotIndex, ShopSlot slot, Vector2 localPos)
        {
            GameObject go;
            if (slotFramePrefab != null)
            {
                go = Instantiate(slotFramePrefab, shopRoot);
            }
            else
            {
                // フォールバック: Quadベースの簡易枠
                go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var collider = go.GetComponent<MeshCollider>();
                if (collider != null) Destroy(collider);
                go.AddComponent<BoxCollider>();
                go.transform.SetParent(shopRoot, false);
            }

            go.name = $"ShopSlot_{slotIndex}";
            go.transform.localPosition = new Vector3(localPos.x, localPos.y, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(slotSizeUnits, slotSizeUnits, 1f);

            var visual = go.GetComponent<ShopSlotVisual>();
            if (visual == null) visual = go.AddComponent<ShopSlotVisual>();
            visual.Initialize(slotIndex, slot, ResolveFrameTexture(slot), modelRotationSpeedDegPerSec, modelScale);
            spawnedSlots.Add(visual);
        }

        /// <summary>下段の右側にリロールボタンを生成。</summary>
        private void SpawnRerollButton()
        {
            // 下段3スロットの右端よりさらに外側に配置
            float x = 2.0f * slotSpacing.x;
            float y = -0.5f * slotSpacing.y;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var meshCol = go.GetComponent<MeshCollider>();
            if (meshCol != null) Destroy(meshCol);
            go.AddComponent<BoxCollider>();
            go.transform.SetParent(shopRoot, false);
            go.name = "ShopRerollButton";
            go.transform.localPosition = new Vector3(x, y, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(slotSizeUnits * 0.9f, slotSizeUnits * 0.6f, 1f);

            var btn = go.AddComponent<ShopRerollButton>();
            // 枠テクスチャは Gold を流用（特別感）
            btn.Initialize(frameGold);
            spawnedRerollButton = btn;
        }

        /// <summary>メタバフ〈値下げ交渉〉アンロック時のみ、 強盗ボタンを下段の左外側に生成。</summary>
        private void SpawnRobberyButtonIfUnlocked()
        {
            if (!MetaProgression.MetaBuffApplicator.IsShopRobberyUnlocked()) return;

            float x = -2.0f * slotSpacing.x;
            float y = -0.5f * slotSpacing.y;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var meshCol = go.GetComponent<MeshCollider>();
            if (meshCol != null) Destroy(meshCol);
            go.AddComponent<BoxCollider>();
            go.transform.SetParent(shopRoot, false);
            go.name = "ShopRobberyButton";
            go.transform.localPosition = new Vector3(x, y, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(slotSizeUnits * 0.9f, slotSizeUnits * 0.6f, 1f);

            var btn = go.AddComponent<ShopRobberyButton>();
            btn.Initialize(frameLegendary); // 怪しい光を表現するため LEG 枠
            spawnedRobberyButton = btn;
        }

        /// <summary>スロットの Tier に応じた枠テクスチャを返す。</summary>
        private Texture2D ResolveFrameTexture(ShopSlot slot)
        {
            // 強化素材は Tier3 (GOLD) として扱う
            if (slot.kind == ShopSlotKind.WeaponMaterial) return frameGold;

            if (string.IsNullOrEmpty(slot.itemId)) return frameBronze;
            var data = ItemDatabase.Instance != null ? ItemDatabase.Instance.GetItem(slot.itemId) : null;
            if (data == null) return frameBronze;

            switch (data.rarity)
            {
                case ItemRarity.BRONZE:    return frameBronze;
                case ItemRarity.SILVER:    return frameSilver;
                case ItemRarity.GOLD:      return frameGold;
                case ItemRarity.LEGENDARY: return frameLegendary;
                default: return frameBronze;
            }
        }

        private void ClearSlots()
        {
            foreach (var s in spawnedSlots)
                if (s != null) Destroy(s.gameObject);
            spawnedSlots.Clear();
            if (spawnedRerollButton != null)
            {
                Destroy(spawnedRerollButton.gameObject);
                spawnedRerollButton = null;
            }
            if (spawnedRobberyButton != null)
            {
                Destroy(spawnedRobberyButton.gameObject);
                spawnedRobberyButton = null;
            }

            if (shopRoot != null)
            {
                // 背景も再生成するため削除
                for (int i = shopRoot.childCount - 1; i >= 0; i--)
                    Destroy(shopRoot.GetChild(i).gameObject);
            }
        }
    }
}

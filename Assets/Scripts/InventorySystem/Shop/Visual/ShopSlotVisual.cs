using UnityEngine;
using GameLoop;

namespace InventorySystem.Shop.Visual
{
    /// <summary>
    /// ショップ1スロットのビジュアル。
    /// 構造: 自身の Quad に Tier 別の枠テクスチャを貼り、その上に
    /// アイテムの 3D モデル (fbxModel を Instantiate) を Y軸回転させて配置する。
    /// クリックで購入ダイアログを開く。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ShopSlotVisual : MonoBehaviour
    {
        public int SlotIndex { get; private set; }
        public ShopSlot Slot { get; private set; }

        private MeshRenderer frameRenderer;
        private GameObject itemModelInstance;
        private Transform itemModelPivot;
        private TextMesh priceText;

        private float modelRotationSpeed;
        private float modelScale;

        public void Initialize(int slotIndex, ShopSlot slot,
            Texture2D frameTexture, float rotationSpeedDegPerSec, float modelScaleMul)
        {
            SlotIndex = slotIndex;
            Slot = slot;
            modelRotationSpeed = rotationSpeedDegPerSec;
            modelScale = modelScaleMul;

            BuildVisuals(frameTexture);
            BuildItemModel();
            RefreshDisplay();
        }

        // ============================================================
        //  ビジュアル構築
        // ============================================================

        private void BuildVisuals(Texture2D frameTexture)
        {
            // 自身の Quad に枠テクスチャを貼る
            frameRenderer = GetComponent<MeshRenderer>();
            if (frameRenderer != null && frameTexture != null)
            {
                var mat = new Material(Shader.Find("Sprites/Default"));
                frameTexture.filterMode = FilterMode.Point;
                mat.mainTexture = frameTexture;
                frameRenderer.sharedMaterial = mat;
            }

            // 価格テキスト
            var textGo = new GameObject("Price");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0f, -0.4f, -0.01f);
            textGo.transform.localScale = new Vector3(0.04f, 0.04f, 1f);
            priceText = textGo.AddComponent<TextMesh>();
            priceText.alignment = TextAlignment.Center;
            priceText.anchor = TextAnchor.MiddleCenter;
            priceText.fontSize = 32;
            priceText.color = Color.white;
            var mr = textGo.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 2;
        }

        private void BuildItemModel()
        {
            // pivot は自身の親空間で回転させる（自身がショップ平面に寝かせられているので、
            // 親回転を逆引きしてワールド Y 軸で回転するよう調整）
            var pivotGo = new GameObject("ModelPivot");
            pivotGo.transform.SetParent(transform, false);
            pivotGo.transform.localPosition = new Vector3(0f, 0.05f, -0.5f);
            // 自身がショップ平面 (X=-90°) に寝ているのでモデルを立たせるため、X+90° 戻す
            pivotGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            itemModelPivot = pivotGo.transform;

            var prefab = ResolveItemPrefab();
            if (prefab == null) return;

            itemModelInstance = Instantiate(prefab, itemModelPivot);
            itemModelInstance.name = "ShopItemModel";
            itemModelInstance.transform.localPosition = Vector3.zero;
            itemModelInstance.transform.localRotation = Quaternion.identity;
            itemModelInstance.transform.localScale = prefab.transform.localScale * modelScale;

            // クリック判定は親の BoxCollider が担当するので、子モデル側のコライダは無効化
            foreach (var col in itemModelInstance.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
        }

        private GameObject ResolveItemPrefab()
        {
            if (Slot == null) return null;
            if (Slot.kind == ShopSlotKind.WeaponMaterial) return null; // 素材枠は枠のみ
            if (string.IsNullOrEmpty(Slot.itemId)) return null;

            var data = ItemDatabase.Instance != null ? ItemDatabase.Instance.GetItem(Slot.itemId) : null;
            return data?.fbxModel;
        }

        // ============================================================
        //  毎フレーム
        // ============================================================

        void Update()
        {
            if (itemModelPivot == null || modelRotationSpeed == 0f) return;
            if (Slot == null || Slot.sold) return; // 売却済みは非表示中なので回さない
            // pivot のローカル空間で Y 軸回転（pivot は親空間で X+90 されているので、
            // pivot ローカル Y 軸 = ワールド Y 軸）
            itemModelPivot.Rotate(Vector3.up, modelRotationSpeed * Time.deltaTime, Space.Self);
        }

        // ============================================================
        //  状態更新
        // ============================================================

        public void RefreshDisplay()
        {
            if (Slot == null) return;

            // 売却済みは 3D モデルを非表示（枠だけ残す）
            if (itemModelInstance != null)
            {
                bool show = !Slot.sold && Slot.kind != ShopSlotKind.WeaponMaterial;
                if (Slot.kind == ShopSlotKind.WeaponMaterial) show = false; // 素材は元々モデル無し
                itemModelInstance.SetActive(show);
            }

            // 価格テキスト
            if (priceText != null)
            {
                if (Slot.kind == ShopSlotKind.WeaponMaterial)
                {
                    int price = ShopManager.Instance?.Current?.CurrentMaterialPrice ?? Slot.price;
                    priceText.text = $"{price}G ∞";
                    priceText.color = Color.cyan;
                }
                else if (Slot.sold)
                {
                    priceText.text = "売切";
                    priceText.color = Color.gray;
                }
                else
                {
                    priceText.text = $"{Slot.price}G";
                    priceText.color = CanAfford() ? Color.white : Color.red;
                }
            }
        }

        private bool CanAfford()
        {
            var run = GameManager.Instance?.Run;
            return run != null && run.coins >= Slot.price;
        }

        // ============================================================
        //  クリック検出
        // ============================================================

        void OnMouseDown()
        {
            if (Slot == null) return;
            if (GameManager.Instance == null) return;
            if (GameManager.Instance.CurrentPhase != GameManager.GamePhase.ShopVisit) return;
            if (GameManager.Instance.ShopSellMode) return; // 売却モード中は別UI

            ShopPurchaseDialog.Instance?.Open(SlotIndex, Slot);
        }
    }
}

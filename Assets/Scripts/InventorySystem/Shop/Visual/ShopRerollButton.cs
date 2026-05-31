using UnityEngine;
using GameLoop;

namespace InventorySystem.Shop.Visual
{
    /// <summary>
    /// ショップのリロールボタン。クリックで GameManager.ShopReroll() を呼ぶ。
    /// 価格は ShopInventory.CurrentRerollPrice を毎更新時に取得し、TextMesh に反映。
    /// 自身（Quad）に BoxCollider を付けてクリック検出する。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ShopRerollButton : MonoBehaviour
    {
        private TextMesh labelText;
        private MeshRenderer frameRenderer;

        public void Initialize(Texture2D frameTexture)
        {
            frameRenderer = GetComponent<MeshRenderer>();
            if (frameRenderer != null && frameTexture != null)
            {
                var mat = new Material(Shader.Find("Sprites/Default"));
                frameTexture.filterMode = FilterMode.Point;
                mat.mainTexture = frameTexture;
                frameRenderer.sharedMaterial = mat;
            }

            // ラベル
            var textGo = new GameObject("RerollLabel");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            textGo.transform.localScale = new Vector3(0.035f, 0.035f, 1f);
            labelText = textGo.AddComponent<TextMesh>();
            labelText.alignment = TextAlignment.Center;
            labelText.anchor = TextAnchor.MiddleCenter;
            labelText.fontSize = 36;
            labelText.color = Color.white;
            var mr = textGo.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 2;

            Refresh();
        }

        /// <summary>表示文言更新。価格不足時はグレー表示。</summary>
        public void Refresh()
        {
            var inv = ShopManager.Instance != null ? ShopManager.Instance.Current : null;
            if (inv == null || labelText == null) return;
            int price = inv.CurrentRerollPrice;
            var run = GameManager.Instance != null ? GameManager.Instance.Run : null;
            bool affordable = run != null && run.coins >= price;
            labelText.text = $"リロール\n{price}G";
            labelText.color = affordable ? Color.white : new Color(0.55f, 0.55f, 0.55f);
        }

        void OnMouseDown()
        {
            if (GameManager.Instance == null) return;
            if (GameManager.Instance.CurrentPhase != GameManager.GamePhase.ShopVisit) return;
            if (GameManager.Instance.ShopSellMode) return; // 売却モード中は無効
            GameManager.Instance.ShopReroll();
        }
    }
}

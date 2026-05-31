using UnityEngine;
using GameLoop;

namespace InventorySystem.Shop.Visual
{
    /// <summary>
    /// 「値下げを願う」ボタン。
    /// メタバフ〈値下げ交渉〉アンロック時のみショップ画面に表示される。
    /// クリック=GameManager.ShopRobbery() を呼ぶ → 内部的には強盗判定(カルマ+1, shopsBlocked, 戦闘突入)。
    /// 自身(Quad)に BoxCollider を付けてクリック検出する。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ShopRobberyButton : MonoBehaviour
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

            var textGo = new GameObject("RobberyLabel");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            textGo.transform.localScale = new Vector3(0.035f, 0.035f, 1f);
            labelText = textGo.AddComponent<TextMesh>();
            labelText.alignment = TextAlignment.Center;
            labelText.anchor = TextAnchor.MiddleCenter;
            labelText.fontSize = 36;
            labelText.color = new Color(1f, 0.85f, 0.55f); // 怪しい黄金色
            var mr = textGo.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 2;
            labelText.text = "値下げを\n願う";
        }

        void OnMouseDown()
        {
            if (GameManager.Instance == null) return;
            if (GameManager.Instance.CurrentPhase != GameManager.GamePhase.ShopVisit) return;
            if (GameManager.Instance.ShopSellMode) return;
            GameManager.Instance.ShopRobbery();
        }
    }
}

using UnityEngine;

namespace UI.Lcd
{
    /// <summary>
    /// 液晶内ボタンの入力経路。 ワールドカメラのマウスレイが「液晶面(Quad)」に当たった点の UV を、
    /// コンテンツカメラのスクリーン座標へ変換し、 コンテンツ空間の <see cref="SpriteButton"/> を解決して
    /// ホバー状態を供給する（押下/クリック自体は SpriteButton が Input から判定）。
    ///
    /// 前提:
    ///   ・液晶面 Quad に **MeshCollider**（RaycastHit.textureCoord に UV が要るため。BoxCollider 不可）。
    ///   ・液晶内ボタンは <see cref="SpriteButton.selfRaycast"/> = false にしておく。
    ///   ・コンテンツカメラは RenderTexture へ描画中（LcdScreen 経由）。
    ///
    /// 付け先: ワールドカメラ（または任意の常駐 GameObject）。
    /// </summary>
    [DisallowMultipleComponent]
    public class LcdPointer : MonoBehaviour
    {
        [Tooltip("部屋/携帯機を映す透視カメラ")]
        public Camera worldCamera;
        [Tooltip("液晶面 Quad の MeshCollider")]
        public Collider surfaceCollider;
        [Tooltip("液晶の中身を描くコンテンツカメラ（RenderTexture へ描画）")]
        public Camera contentCamera;
        [Tooltip("液晶面を拾うレイヤー（ワールド側レイキャスト用）")]
        public LayerMask surfaceMask = ~0;
        [Tooltip("液晶内ボタンのレイヤー（コンテンツ側レイキャスト用）")]
        public LayerMask contentMask = ~0;
        [Tooltip("ワールドレイの最大距離")]
        public float maxDistance = 1000f;

        private SpriteButton _current;

        private void OnDisable()
        {
            if (_current != null) { _current.SetPointerOver(false); _current = null; }
        }

        private void Update()
        {
            var btn = Resolve();
            if (btn != _current)
            {
                if (_current != null) _current.SetPointerOver(false);
                if (btn != null) btn.SetPointerOver(true);
                _current = btn;
            }
        }

        private SpriteButton Resolve()
        {
            if (worldCamera == null || surfaceCollider == null || contentCamera == null) return null;

            Vector3 mp = Input.mousePosition;
            if (mp.x < 0 || mp.y < 0 || mp.x > Screen.width || mp.y > Screen.height) return null;

            // ① ワールドカメラのレイが液晶面に当たるか（UV が要るので MeshCollider 前提）。
            Ray wray = worldCamera.ScreenPointToRay(mp);
            if (!Physics.Raycast(wray, out RaycastHit hit, maxDistance, surfaceMask)) return null;
            if (hit.collider != surfaceCollider) return null;

            Vector2 uv = hit.textureCoord; // MeshCollider のみ有効
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return null;

            // ② UV → コンテンツカメラの描画先(RT)ピクセル座標。 RT-target カメラの pixelWidth/Height は RT 解像度。
            float px = uv.x * contentCamera.pixelWidth;
            float py = uv.y * contentCamera.pixelHeight;

            // ③ コンテンツ空間でレイ交差 → 当たった SpriteButton を返す。
            Ray cray = contentCamera.ScreenPointToRay(new Vector3(px, py, 0f));
            var h2 = Physics2D.GetRayIntersection(cray, Mathf.Infinity, contentMask);
            if (h2.collider == null) return null;
            return h2.collider.GetComponent<SpriteButton>();
        }
    }
}

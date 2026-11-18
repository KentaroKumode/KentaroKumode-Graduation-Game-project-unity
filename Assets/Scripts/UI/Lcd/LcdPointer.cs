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
            // 2026-07-10: 単一 Raycast だと Layer 0 に配置された他 Collider (インベントリ Cell 等) が
            // 液晶面より手前に来て Quad が拾えないケースがある。 RaycastAll で全 hit を確認し、
            // surfaceCollider を含む hit だけを採用する。
            Ray wray = worldCamera.ScreenPointToRay(mp);
            var hits = Physics.RaycastAll(wray, maxDistance, surfaceMask);
            RaycastHit hit = default;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
                if (hits[i].collider == surfaceCollider) { hit = hits[i]; found = true; break; }
            if (!found) return null;

            Vector2 uv = hit.textureCoord; // MeshCollider のみ有効
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return null;
            // 液晶は樽型に湾曲している (Game2D/LcdScreen): 面の uv = 見えている位置なので、 湾曲を戻した絵の uv にする (2026-11-16・式は Game2DPointer.LcdDot の 1 か所)。
            //   今までは戻していなかったので、 タイトル・職業選択のボタンの当たりが湾曲の分だけずれていた
            {
                var r = surfaceCollider.GetComponent<MeshRenderer>(); var m = r != null ? r.sharedMaterial : null;
                float k = m != null && m.HasProperty("_Curve") ? m.GetFloat("_Curve") : 0f, fit = m != null && m.HasProperty("_CurveFit") ? m.GetFloat("_CurveFit") : 0f;
                const float W = UI.Game2D.Game2DLayout.Width, H = UI.Game2D.Game2DLayout.Height;
                var d = UI.Game2D.Game2DPointer.LcdDot(new Vector2(uv.x * W, (1f - uv.y) * H), k, fit);
                uv = new Vector2(d.x / W, 1f - d.y / H);
                if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return null;      // 絵の外 (四隅の楔)
            }

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

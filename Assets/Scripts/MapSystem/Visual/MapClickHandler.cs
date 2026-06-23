using UnityEngine;
using GameLoop;

namespace MapSystem.Visual
{
    /// <summary>
    /// マップノードへのクリック入力を処理する。
    /// MapVisualizer と同じ GameObject にアタッチする。
    ///
    /// 座標マッピング優先順位:
    ///   1. mapCamera + displayImage (RawImage) が設定されている場合:
    ///      スクリーン座標 → RawImage UV → mapCamera Viewport → レイ
    ///   2. fallbackCamera (未設定なら Camera.main):
    ///      スクリーン座標から直接レイ（ノードが main camera から見える構成向け）
    ///
    /// ホバーエフェクト:
    ///   Reachable ノードにカーソルが乗ると明るくなる。
    ///   クリックで GameManager.MoveToNode を呼ぶ。
    /// </summary>
    public class MapClickHandler : MonoBehaviour
    {
        [Header("カメラ設定")]
        [Tooltip("マップ専用カメラ。 PixelPerfectMapCamera でも素のCameraでも可。 LCD content 経由なら ContentCamera を渡す。")]
        [SerializeField] private Camera mapCamera;

        [Header("表示先 (どちらか一方)")]
        [Tooltip("RawImage に RenderTexture を表示している場合")]
        [SerializeField] private UnityEngine.UI.RawImage displayImage;
        [Tooltip("3D Quad にRenderTextureを表示している場合")]
        [SerializeField] private MeshRenderer displayQuad;

        [Header("フォールバック")]
        [Tooltip("mapCamera 未設定時に使うカメラ（null = Camera.main）")]
        [SerializeField] private Camera fallbackCamera;

        [Header("ホバー色")]
        [SerializeField] private Color hoverColor = new Color(1f, 1f, 0.6f, 1f);

        // ホバー中のノード管理
        private MapNodeVisual hoveredVisual;
        private Color hoveredOriginalColor;

        void Update()
        {
            // GameManager がある場合はフェーズチェック、無い場合 (テストシーン等) は素通し
            if (GameManager.Instance != null &&
                GameManager.Instance.CurrentPhase != GameManager.GamePhase.MapNavigation)
                return;

            HandleHover();

            if (Input.GetMouseButtonDown(0))
                HandleClick();
        }

        // ================================================================
        //  クリック処理
        // ================================================================

        private void HandleClick()
        {
            var nodeVisual = RaycastNodeVisual();
            nodeVisual?.OnClicked();
        }

        // ================================================================
        //  ホバー処理
        // ================================================================

        private void HandleHover()
        {
            var nodeVisual = RaycastNodeVisual();

            // ホバー対象が変わった場合
            if (nodeVisual != hoveredVisual)
            {
                // 前のホバーを元に戻す
                ClearHover();

                // 新しいホバーを適用（Reachableのみ）
                if (nodeVisual != null &&
                    nodeVisual.GetComponent<SpriteRenderer>() is SpriteRenderer sr &&
                    nodeVisual.CurrentState == NodeVisualState.Reachable)
                {
                    hoveredVisual = nodeVisual;
                    hoveredOriginalColor = sr.color;
                    sr.color = hoverColor;
                }
            }
        }

        private void ClearHover()
        {
            if (hoveredVisual == null) return;
            var sr = hoveredVisual.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = hoveredOriginalColor;
            hoveredVisual = null;
        }

        // ================================================================
        //  レイキャスト
        // ================================================================

        private MapNodeVisual RaycastNodeVisual()
        {
            if (!TryGetRay(out Ray ray)) return null;

            if (Physics.Raycast(ray, out RaycastHit hit, 500f))
                return hit.collider.GetComponentInParent<MapNodeVisual>();

            return null;
        }

        /// <summary>
        /// 入力（マウス）位置から、ノードに当てるためのワールドレイを生成する。
        /// 表示パイプラインに応じて RawImage/Quad/フォールバックの順で解決する。
        /// マップが XZ 平面に寝かされていても 3D Raycast なので問題なく当たる。
        /// </summary>
        private bool TryGetRay(out Ray ray)
        {
            ray = default;

            // --- 方法1: mapCamera + RawImage (RenderTexture経由) ---
            if (mapCamera != null && displayImage != null)
            {
                var cam = mapCamera;
                if (cam == null) goto Fallback;

                RectTransform rt = displayImage.rectTransform;
                Camera eventCam = displayImage.canvas != null ? displayImage.canvas.worldCamera : null;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        rt, Input.mousePosition, eventCam, out Vector2 localPoint))
                    return false;

                Rect r = rt.rect;
                float u = (localPoint.x - r.xMin) / r.width;
                float v = (localPoint.y - r.yMin) / r.height;

                var uvRect = displayImage.uvRect;
                float mappedU = uvRect.x + u * uvRect.width;
                float mappedV = uvRect.y + v * uvRect.height;

                if (mappedU < 0f || mappedU > 1f || mappedV < 0f || mappedV > 1f) return false;

                ray = cam.ViewportPointToRay(new Vector3(mappedU, mappedV, 0f));
                return true;
            }

            // --- 方法2: mapCamera + Quad (3DワールドにQuadを置きRTを貼っている場合) ---
            if (mapCamera != null && displayQuad != null)
            {
                Camera mainCam = Camera.main;
                if (mainCam == null) goto Fallback;

                Ray mainRay = mainCam.ScreenPointToRay(Input.mousePosition);
                if (!Physics.Raycast(mainRay, out RaycastHit quadHit, 500f)) return false;
                if (quadHit.collider == null || quadHit.collider.gameObject != displayQuad.gameObject) return false;

                var cam = mapCamera;
                if (cam == null) goto Fallback;

                Vector2 uv = quadHit.textureCoord;
                ray = cam.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
                return true;
            }

            Fallback:
            // --- 方法3: フォールバックカメラ（ノードがメインカメラから直接見える構成） ---
            {
                Camera fc = fallbackCamera != null ? fallbackCamera : Camera.main;
                if (fc == null) return false;
                ray = fc.ScreenPointToRay(Input.mousePosition);
                return true;
            }
        }
    }
}

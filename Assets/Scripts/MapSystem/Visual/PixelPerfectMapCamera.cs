using UnityEngine;

namespace MapSystem.Visual
{
    /// <summary>
    /// マップ専用カメラ。ゲーム機の「画面」サイズ固定の RenderTexture に出力し、
    /// Point Filtering で拡大表示することでピクセル密度を統一する。
    /// メインカメラはゲーム機全体を固定から映す。
    /// スクロールは MapCamera の Y 座標を動かすことで実現する。
    ///
    /// セットアップ:
    ///   1. マップ専用 Camera を作成し、このコンポーネントをアタッチ
    ///   2. screenPixelWidth / screenPixelHeight にゲーム機画面のピクセルサイズを入力
    ///   3. TileIconAtlas をアサイン（PPU 自動計算）
    ///   4. displayQuad または displayImage に出力先をアサイン
    ///   5. MapScrollController を同じ GameObject にアタッチして入力を受け取る
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PixelPerfectMapCamera : MonoBehaviour
    {
        [Header("ゲーム機画面サイズ (ピクセル)")]
        [Tooltip("ゲーム機の画面領域の横ピクセル数")]
        [SerializeField] private int screenPixelWidth  = 160;
        [Tooltip("ゲーム機の画面領域の縦ピクセル数")]
        [SerializeField] private int screenPixelHeight = 240;

        [Header("ピクセル密度ソース")]
        [Tooltip("アトラスの 1セルピクセル数 = PPU として使用")]
        [SerializeField] private TileIconAtlas atlas;
        [Tooltip("アトラス未設定時のフォールバック PPU")]
        [SerializeField] private int fallbackPPU = 16;

        [Header("表示先 (どちらか一方)")]
        [Tooltip("3D空間の Quad/Plane に表示する場合")]
        [SerializeField] private MeshRenderer displayQuad;
        [Tooltip("UI の RawImage に表示する場合")]
        [SerializeField] private UnityEngine.UI.RawImage displayImage;

        // === 内部 ===
        private Camera mapCamera;
        private RenderTexture rt;

        // スクロール範囲
        private float scrollMinY;
        private float scrollMaxY;
        private float currentScrollY;

        // === 公開プロパティ ===
        /// <summary>現在のピクセル密度 (Pixels Per Unit)</summary>
        public int PPU { get; private set; }

        /// <summary>カメラの半垂直範囲 (ワールド単位)</summary>
        public float CamHalfHeight { get; private set; }

        /// <summary>ワールド座標をピクセルグリッドにスナップ</summary>
        public Vector3 SnapToPixel(Vector3 world)
        {
            float inv = PPU;
            world.x = Mathf.Round(world.x * inv) / inv;
            world.y = Mathf.Round(world.y * inv) / inv;
            return world;
        }

        void Awake()
        {
            mapCamera = GetComponent<Camera>();
            mapCamera.orthographic = true;
            ComputePPU();
        }

        // ================================================================
        //  公開 API
        // ================================================================

        /// <summary>
        /// マップ生成後に呼ぶ。
        /// 画面サイズ固定の RenderTexture を割り当て、スクロール範囲を計算する。
        /// </summary>
        public void SetupForMap(Bounds mapBounds)
        {
            ComputePPU();

            float worldW = (float)screenPixelWidth  / PPU;
            float worldH = (float)screenPixelHeight / PPU;

            CamHalfHeight = worldH * 0.5f;

            // RenderTexture: 画面サイズ固定
            RebuildRenderTexture(screenPixelWidth, screenPixelHeight);

            // カメラのサイズ指定
            mapCamera.orthographicSize = CamHalfHeight;
            mapCamera.aspect = (float)screenPixelWidth / screenPixelHeight;

            // スクロール範囲を計算
            // 下限: マップ上端 + パディング / 上限: マップ下端 - パディング
            scrollMinY = mapBounds.min.y + CamHalfHeight;
            scrollMaxY = mapBounds.max.y - CamHalfHeight;

            // マップが画面より短い場合は中心固定
            if (scrollMaxY < scrollMinY)
            {
                float mid = (mapBounds.min.y + mapBounds.max.y) * 0.5f;
                scrollMinY = scrollMaxY = mid;
            }

            // 初期位置: マップ下端（スタート附近）
            ScrollTo(scrollMinY);
        }

        /// <summary>指定のワールドY座標にカメラをㇻスクロール</summary>
        public void ScrollTo(float worldY)
        {
            currentScrollY = Mathf.Clamp(worldY, scrollMinY, scrollMaxY);

            // ピクセルグリッドにスナップ（ザラ防止）
            float snapped = Mathf.Round(currentScrollY * PPU) / PPU;

            var pos = transform.position;
            pos.y = snapped;
            transform.position = pos;
        }

        /// <summary>現在位置から相対移動</summary>
        public void ScrollBy(float deltaY)
        {
            ScrollTo(currentScrollY + deltaY);
        }

        /// <summary>指定ノードが画面中央に来るようにスクロール</summary>
        public void CenterOn(Vector3 worldPos)
        {
            ScrollTo(worldPos.y);
        }

        /// <summary>現在のスクロール Y 位置</summary>
        public float CurrentScrollY => currentScrollY;

        /// <summary>0-1 のスクロール割合</summary>
        public float ScrollRatio
        {
            get
            {
                float range = scrollMaxY - scrollMinY;
                return range > 0f ? (currentScrollY - scrollMinY) / range : 0f;
            }
        }

        // ================================================================
        //  内部
        // ================================================================

        private void RebuildRenderTexture(int w, int h)
        {
            if (rt != null)
            {
                mapCamera.targetTexture = null;
                rt.Release();
                Destroy(rt);
            }

            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Clamp,
                autoGenerateMips = false,
                useMipMap        = false,
            };
            rt.Create();
            mapCamera.targetTexture = rt;

            if (displayQuad != null)
            {
                displayQuad.material.mainTexture = rt;
                displayQuad.material.mainTexture.filterMode = FilterMode.Point;
            }
            if (displayImage != null)
                displayImage.texture = rt;
        }

        private void ComputePPU()
        {
            PPU = (atlas != null && atlas.atlasTexture != null)
                ? atlas.atlasTexture.width / atlas.columns
                : fallbackPPU;
        }

        void OnDestroy()
        {
            if (rt != null)
            {
                mapCamera.targetTexture = null;
                rt.Release();
                Destroy(rt);
            }
        }
    }
}

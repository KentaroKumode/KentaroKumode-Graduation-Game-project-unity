using System.Collections.Generic;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// 砂塵（ピクセルアート版）。 縁の硬い単色スクエアを矩形領域にばら撒き、 風でゆっくり流す（端で反対側へラップ）。
    /// 自己完結（ドット用スプライトをコード生成）＝外部アセット不要・ローコスト。
    ///
    /// ピクセル整合:
    ///   ・粒は「整数ピクセル(px)の単色テクスチャ」を PPU=pixelsPerUnit で作る＝拡大しない（localScale 不使用）。
    ///   ・位置を 1/pixelsPerUnit グリッドへスナップ＝サブピクセルのボケ/にじみを排除。
    ///   ・コンテンツ撮影の縦は 17.375=278/32 で整合。 横も RT 幅を偶数にすると境界が揃う（profile=924×556 推奨）。
    ///
    /// 配置: LcdContent レイヤー（コンテンツカメラが撮る面）に置き、 背景とタイトルの中間 sortingOrder に。
    /// </summary>
    [DisallowMultipleComponent]
    public class DustField : MonoBehaviour
    {
        [Header("量・領域")]
        [Min(0)] public int count = 40;
        [Tooltip("ばら撒く矩形(world)。 画面より少し広めにすると端の湧き出しが目立たない。")]
        public Vector2 area = new Vector2(31f, 17.5f);

        [Header("風・動き")]
        [Tooltip("基本の風(world/sec)。 主に横。")]
        public Vector2 wind = new Vector2(-1.2f, 0.12f);
        [Tooltip("粒ごとの速度ゆらぎ幅(world/sec)")]
        public float windJitter = 0.5f;

        [Header("ピクセル見た目")]
        [Tooltip("コンテンツと揃える解像度（背景と同じ 32 推奨）")]
        public int pixelsPerUnit = 32;
        [Tooltip("粒の一辺(px)の最小")]
        [Min(1)] public int pixelSizeMin = 1;
        [Tooltip("粒の一辺(px)の最大")]
        [Min(1)] public int pixelSizeMax = 2;
        [Tooltip("色のバリエーション（A=不透明度）")]
        public Color[] palette = new Color[]
        {
            new Color(0.88f, 0.82f, 0.64f, 0.70f),
            new Color(0.74f, 0.66f, 0.50f, 0.60f),
            new Color(0.95f, 0.90f, 0.78f, 0.55f),
        };
        [Tooltip("位置をピクセルグリッドへスナップ（ボケ防止・推奨）")]
        public bool snapToPixelGrid = true;

        [Header("描画順")]
        public int sortingOrder = 5;
        public string sortingLayer = "Default";

        [Tooltip("ポーズ中(timeScale=0)でも動かす")]
        public bool useUnscaledTime = true;

        private struct P { public Transform t; public Vector2 vel; public Vector2 fpos; }
        private readonly List<P> _ps = new List<P>();
        private readonly Dictionary<int, Sprite> _sprites = new Dictionary<int, Sprite>();

        private void OnEnable() { Build(); }
        private void OnDisable() { Clear(); }

        private void Build()
        {
            Clear();
            int ppu = Mathf.Max(1, pixelsPerUnit);
            int pmin = Mathf.Max(1, pixelSizeMin);
            int pmax = Mathf.Max(pmin, pixelSizeMax);

            for (int i = 0; i < count; i++)
            {
                int px = Random.Range(pmin, pmax + 1);
                var go = new GameObject("dust");
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = GetSprite(px, ppu);
                sr.color = (palette != null && palette.Length > 0) ? palette[Random.Range(0, palette.Length)] : Color.white;
                sr.sortingOrder = sortingOrder;
                if (!string.IsNullOrEmpty(sortingLayer)) sr.sortingLayerName = sortingLayer;

                Vector2 fp = new Vector2(
                    Random.Range(-area.x * 0.5f, area.x * 0.5f),
                    Random.Range(-area.y * 0.5f, area.y * 0.5f));
                go.transform.localPosition = Place(fp, ppu);

                _ps.Add(new P { t = go.transform, vel = wind + Random.insideUnitCircle * windJitter, fpos = fp });
            }
        }

        private void Update()
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (dt <= 0f) return;
            int ppu = Mathf.Max(1, pixelsPerUnit);
            Vector2 half = area * 0.5f;
            for (int i = 0; i < _ps.Count; i++)
            {
                var p = _ps[i];
                p.fpos += p.vel * dt;
                if (p.fpos.x < -half.x) p.fpos.x += area.x; else if (p.fpos.x > half.x) p.fpos.x -= area.x;
                if (p.fpos.y < -half.y) p.fpos.y += area.y; else if (p.fpos.y > half.y) p.fpos.y -= area.y;
                p.t.localPosition = Place(p.fpos, ppu);
                _ps[i] = p;
            }
        }

        // float位置 → スナップした localPosition（z は元のまま）
        private Vector3 Place(Vector2 fp, int ppu)
        {
            if (snapToPixelGrid)
                return new Vector3(Mathf.Round(fp.x * ppu) / ppu, Mathf.Round(fp.y * ppu) / ppu, 0f);
            return new Vector3(fp.x, fp.y, 0f);
        }

        private Sprite GetSprite(int px, int ppu)
        {
            Sprite s;
            if (_sprites.TryGetValue(px, out s) && s != null) return s;
            int n = Mathf.Max(1, px);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "DustPx" + n };
            var cols = new Color32[n * n];
            for (int k = 0; k < cols.Length; k++) cols[k] = new Color32(255, 255, 255, 255); // 単色・縁硬い
            tex.SetPixels32(cols);
            tex.Apply();
            // 左下ピボット＝スナップ時に N×N のピクセルへぴったり乗る
            s = Sprite.Create(tex, new Rect(0, 0, n, n), Vector2.zero, ppu);
            _sprites[px] = s;
            return s;
        }

        private void Clear()
        {
            for (int i = 0; i < _ps.Count; i++)
                if (_ps[i].t != null)
                {
                    if (Application.isPlaying) Destroy(_ps[i].t.gameObject);
                    else DestroyImmediate(_ps[i].t.gameObject);
                }
            _ps.Clear();
            _sprites.Clear();
        }
    }
}

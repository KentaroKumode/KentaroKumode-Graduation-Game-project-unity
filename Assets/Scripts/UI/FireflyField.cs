using System.Collections.Generic;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// 蛍/精霊光（ピクセルアート版）。 DustField と同じピクセル整合で、 各粒に
    /// サイン波の α 明滅を持たせる＝夜の静かな揺らぎ用。 砂塵より粒数少なめ・速度緩めが推奨。
    ///
    /// 配置: LcdContent レイヤー、 背景〜タイトルの中間 sortingOrder。
    /// 規約: localScale は使わない（pixelsPerUnit + 整数 px サイズで世界寸法を決める）。
    /// </summary>
    [DisallowMultipleComponent]
    public class FireflyField : MonoBehaviour
    {
        [Header("量・領域")]
        [Min(0)] public int count = 18;
        public Vector2 area = new Vector2(31f, 17.5f);

        [Header("風・動き（DustFieldより穏やかに）")]
        public Vector2 wind = new Vector2(-0.25f, 0.15f);
        public float windJitter = 0.35f;

        [Header("ピクセル見た目")]
        public int pixelsPerUnit = 32;
        [Min(1)] public int pixelSizeMin = 1;
        [Min(1)] public int pixelSizeMax = 2;
        [Tooltip("発光色のバリエーション（A はベース不透明度、 明滅で上下に動く）")]
        public Color[] palette = new Color[]
        {
            new Color(0.95f, 0.95f, 0.65f, 0.90f), // 蛍黄
            new Color(0.75f, 1.00f, 0.80f, 0.85f), // 緑白
            new Color(0.85f, 0.92f, 1.00f, 0.80f), // 青白
        };
        public bool snapToPixelGrid = true;

        [Header("明滅（α サイン波）")]
        [Tooltip("ベース α からの振幅（0.4 で ±40%）")]
        [Range(0f, 0.9f)] public float blinkAmplitude = 0.5f;
        [Tooltip("明滅速度(rad/sec)。 1〜3 程度がそれっぽい。")]
        public float blinkSpeed = 1.8f;

        [Header("描画順")]
        public int sortingOrder = 5;
        public string sortingLayer = "Default";

        public bool useUnscaledTime = true;

        private struct P { public Transform t; public SpriteRenderer sr; public Vector2 vel; public Vector2 fpos; public float baseA; public float phase; }
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
                var go = new GameObject("firefly");
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = GetSprite(px, ppu);
                Color c = (palette != null && palette.Length > 0) ? palette[Random.Range(0, palette.Length)] : Color.white;
                sr.color = c;
                sr.sortingOrder = sortingOrder;
                if (!string.IsNullOrEmpty(sortingLayer)) sr.sortingLayerName = sortingLayer;

                Vector2 fp = new Vector2(
                    Random.Range(-area.x * 0.5f, area.x * 0.5f),
                    Random.Range(-area.y * 0.5f, area.y * 0.5f));
                go.transform.localPosition = Place(fp, ppu);

                _ps.Add(new P
                {
                    t = go.transform, sr = sr,
                    vel = wind + Random.insideUnitCircle * windJitter, fpos = fp,
                    baseA = c.a, phase = Random.Range(0f, Mathf.PI * 2f)
                });
            }
        }

        private void Update()
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (dt <= 0f) return;
            int ppu = Mathf.Max(1, pixelsPerUnit);
            Vector2 half = area * 0.5f;
            float t = useUnscaledTime ? Time.unscaledTime : Time.time;
            for (int i = 0; i < _ps.Count; i++)
            {
                var p = _ps[i];
                p.fpos += p.vel * dt;
                if (p.fpos.x < -half.x) p.fpos.x += area.x; else if (p.fpos.x > half.x) p.fpos.x -= area.x;
                if (p.fpos.y < -half.y) p.fpos.y += area.y; else if (p.fpos.y > half.y) p.fpos.y -= area.y;
                p.t.localPosition = Place(p.fpos, ppu);

                // α 明滅
                float k = 0.5f + 0.5f * Mathf.Sin(t * blinkSpeed + p.phase);
                float a = Mathf.Clamp01(p.baseA + (k - 0.5f) * 2f * blinkAmplitude * p.baseA);
                var c = p.sr.color; c.a = a; p.sr.color = c;

                _ps[i] = p;
            }
        }

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
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "FireflyPx" + n };
            var cols = new Color32[n * n];
            for (int k = 0; k < cols.Length; k++) cols[k] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(cols); tex.Apply();
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

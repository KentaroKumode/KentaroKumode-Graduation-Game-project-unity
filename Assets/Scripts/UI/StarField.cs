using System.Collections.Generic;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// 星の瞬き（ピクセルアート版）。 矩形領域に単色ドットの星を固定配置し、 各星の「不透明度」を
    /// サイン波でゆっくり明滅させる（位置は動かさない＝ドットがにじまない）。 ローコスト・外部アセット不要。
    ///
    /// ピクセル整合: DustField と同様に PPU=pixelsPerUnit の単色 px テクスチャ＋位置を 1/ppu グリッドへスナップ。
    /// 規約: localScale は使わない。 明滅は色(アルファ)のみで表現。
    ///
    /// 配置: LcdContent レイヤー。 sortingOrder は BG(0) より前・砂塵(5)/メニュー(10) より後ろの 1〜2 を推奨（夜空の奥）。
    /// </summary>
    [DisallowMultipleComponent]
    public class StarField : MonoBehaviour
    {
        [Header("量・領域")]
        [Min(0)] public int count = 70;
        [Tooltip("ばら撒く矩形のサイズ(world)")]
        public Vector2 area = new Vector2(26f, 5.2f);
        [Tooltip("矩形の中心(local)。 空帯のみに出すなら上寄りに（流れ星の消失点 killY より上）。")]
        public Vector2 areaCenter = new Vector2(0f, 6.1f);

        [Header("瞬き")]
        [Tooltip("明滅の最小/最大不透明度")]
        [Range(0f, 1f)] public float alphaMin = 0.15f;
        [Range(0f, 1f)] public float alphaMax = 0.95f;
        [Tooltip("明滅周期(秒)の最小/最大（星ごとにランダム）")]
        public float periodMin = 1.2f;
        public float periodMax = 4.0f;

        [Header("ピクセル見た目")]
        [Tooltip("背景と同じ 32 推奨")]
        public int pixelsPerUnit = 32;
        [Tooltip("星の一辺(px) 最小/最大")]
        [Min(1)] public int pixelSizeMin = 1;
        [Min(1)] public int pixelSizeMax = 2;
        [Tooltip("星の色バリエーション")]
        public Color[] palette = new Color[]
        {
            new Color(1.00f, 1.00f, 1.00f, 1f),
            new Color(0.85f, 0.90f, 1.00f, 1f),
            new Color(1.00f, 0.95f, 0.80f, 1f),
        };
        public bool snapToPixelGrid = true;

        [Header("描画順")]
        public int sortingOrder = 1;
        public string sortingLayer = "Default";

        [Tooltip("ポーズ中(timeScale=0)でも瞬く")]
        public bool useUnscaledTime = true;

        private struct S { public SpriteRenderer sr; public Color baseCol; public float phase; public float freq; }
        private readonly List<S> _stars = new List<S>();
        private readonly Dictionary<int, Sprite> _sprites = new Dictionary<int, Sprite>();

        private void OnEnable() { Build(); }
        private void OnDisable() { Clear(); }

        private void Build()
        {
            Clear();
            int ppu = Mathf.Max(1, pixelsPerUnit);
            int pmin = Mathf.Max(1, pixelSizeMin);
            int pmax = Mathf.Max(pmin, pixelSizeMax);
            Vector2 half = area * 0.5f;

            for (int i = 0; i < count; i++)
            {
                int px = Random.Range(pmin, pmax + 1);
                var go = new GameObject("star");
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = GetSprite(px, ppu);
                Color c = (palette != null && palette.Length > 0) ? palette[Random.Range(0, palette.Length)] : Color.white;
                sr.color = c;
                sr.sortingOrder = sortingOrder;
                if (!string.IsNullOrEmpty(sortingLayer)) sr.sortingLayerName = sortingLayer;

                Vector2 fp = areaCenter + new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
                go.transform.localPosition = snapToPixelGrid
                    ? new Vector3(Mathf.Round(fp.x * ppu) / ppu, Mathf.Round(fp.y * ppu) / ppu, 0f)
                    : new Vector3(fp.x, fp.y, 0f);

                float period = Random.Range(Mathf.Min(periodMin, periodMax), Mathf.Max(periodMin, periodMax));
                _stars.Add(new S { sr = sr, baseCol = c, phase = Random.value * Mathf.PI * 2f, freq = (Mathf.PI * 2f) / Mathf.Max(0.05f, period) });
            }
        }

        private void Update()
        {
            float t = useUnscaledTime ? Time.unscaledTime : Time.time;
            float lo = Mathf.Min(alphaMin, alphaMax), hi = Mathf.Max(alphaMin, alphaMax);
            for (int i = 0; i < _stars.Count; i++)
            {
                var s = _stars[i];
                if (s.sr == null) continue;
                float k = 0.5f + 0.5f * Mathf.Sin(t * s.freq + s.phase); // 0..1
                float a = Mathf.Lerp(lo, hi, k);
                var c = s.baseCol; c.a = a;
                s.sr.color = c;
            }
        }

        private Sprite GetSprite(int px, int ppu)
        {
            Sprite s;
            if (_sprites.TryGetValue(px, out s) && s != null) return s;
            int n = Mathf.Max(1, px);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "StarPx" + n };
            var cols = new Color32[n * n];
            for (int k = 0; k < cols.Length; k++) cols[k] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(cols);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, n, n), Vector2.zero, ppu);
            _sprites[px] = s;
            return s;
        }

        private void Clear()
        {
            for (int i = 0; i < _stars.Count; i++)
                if (_stars[i].sr != null)
                {
                    if (Application.isPlaying) Destroy(_stars[i].sr.gameObject);
                    else DestroyImmediate(_stars[i].sr.gameObject);
                }
            _stars.Clear();
            _sprites.Clear();
        }
    }
}

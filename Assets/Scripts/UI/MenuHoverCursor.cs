using UnityEngine;

namespace UI
{
    /// <summary>
    /// ホバー中のメニュー項目の「両端」にカーソル（マーカー）を表示する。
    /// 各 <see cref="SpriteButton"/> の <see cref="SpriteButton.PointerOverChanged"/> を購読し、
    /// 乗っている項目の左右端（コライダー境界）へ左右2つのカーソルスプライトを置く。
    ///
    /// 使い方:
    ///   1) メニューをまとめた親（例 Content/Menu）にこのコンポーネントを付ける。
    ///   2) cursorSprite に用意した PNG（PPU はメニューと揃える）を割り当てる。
    ///   3) items は空なら子から SpriteButton を自動収集。
    ///
    /// 注意: カーソルは実行時に子として生成（編集中は出ない）。 サイズは Transform でなくスプライト/SpriteRenderer 側で決める（PPU 規約）。
    /// </summary>
    [DisallowMultipleComponent]
    public class MenuHoverCursor : MonoBehaviour
    {
        [Header("対象")]
        [Tooltip("監視する項目。 空なら子から SpriteButton を自動収集。")]
        public SpriteButton[] items;

        [Header("カーソル")]
        [Tooltip("左端カーソルのスプライト。 未指定なら cursorSprite を使う。")]
        public Sprite leftSprite;
        [Tooltip("右端カーソルのスプライト。 未指定なら cursorSprite を使う（mirrorRight で反転）。")]
        public Sprite rightSprite;
        [Tooltip("左右共通スプライト（個別未指定時のフォールバック）。")]
        public Sprite cursorSprite;
        [Tooltip("右側を左右反転（rightSprite 未指定で cursorSprite を流用する時のみ有効）")]
        public bool mirrorRight = true;
        public Color color = Color.white;

        [Header("配置")]
        [Tooltip("項目の端からの隙間(world)")]
        public float gap = 0.2f;
        [Tooltip("矢印の不透明な所と項目の端の間 (液晶の dot)。 0 以下なら旧い置き方 (矢印の中心を端から gap だけ離す)")]
        public float textGapDots = 3f;
        [Tooltip("液晶の 1 unit の dot 数 (矢印の位置を dot の格子にそろえる)。 0 ならそろえない")]
        public float dotsPerUnit = 16f;
        [Tooltip("位置の微調整(world)")]
        public Vector2 nudge = Vector2.zero;
        [Tooltip("描画順（メニュー文字より前に）")]
        public int sortingOrder = 12;
        public string sortingLayer = "Default";

        private SpriteRenderer _left, _right;
        private SpriteButton _current;

        private void OnEnable()
        {
            if (items == null || items.Length == 0)
                items = GetComponentsInChildren<SpriteButton>(true);

            EnsureCursors();
            if (items != null)
                foreach (var it in items)
                    if (it != null) it.PointerOverChanged += OnHover;

            SetVisible(false);
        }

        private void OnDisable()
        {
            if (items != null)
                foreach (var it in items)
                    if (it != null) it.PointerOverChanged -= OnHover;
            _current = null;
            SetVisible(false);
        }

        /// <summary>実行時に作った項目を足す (ホーム画面の SETTINGS・<see cref="TitleMenuLayout"/>・2026-11-16)。 有効な間に足したらすぐ購読する。</summary>
        public void AddItem(SpriteButton b)
        {
            if (b == null) return;
            var list = new System.Collections.Generic.List<SpriteButton>(items ?? new SpriteButton[0]);
            if (list.Contains(b)) return;
            list.Add(b); items = list.ToArray();
            if (isActiveAndEnabled) { b.PointerOverChanged -= OnHover; b.PointerOverChanged += OnHover; }
        }

        private void OnHover(SpriteButton b, bool over)
        {
            if (over)
            {
                _current = b;
                Place(b);
                SetVisible((_left != null && _left.sprite != null) || (_right != null && _right.sprite != null));
            }
            else if (_current == b)
            {
                _current = null;
                SetVisible(false);
            }
        }

        private void LateUpdate()
        {
            // 項目が動く場合（パララックス等）に追従。
            if (_current != null) Place(_current);
        }

        private void Place(SpriteButton b)
        {
            if (_left == null || _right == null || b == null) return;

            Bounds bd;
            var col = b.GetComponent<Collider2D>();
            if (col != null) bd = col.bounds;
            else { var sr = b.GetComponent<SpriteRenderer>(); if (sr == null) return; bd = sr.bounds; }

            float y = bd.center.y + nudge.y;
            float lx = bd.min.x - gap + nudge.x, rx = bd.max.x + gap + nudge.x;
            // 2026-11-17: 矢印の不透明な所と項目の端 (当たり = 字の外縁・TitleMenuLayout.FitCollider) の間をちょうど textGapDots にする。
            //   矢印の絵は左右で中身の位置が違う (左は先端が中心から 8 dot・右は左端が中心から 7 dot) ので、 絵の不透明な範囲を実行時に測って求める (絵を替えても崩れない)。
            //   旧い置き方 (中心を端から gap) は、 字の絵の当たりが字の外縁になってから (ed7af5c8) 矢印が字に 4.8 / 3.8 dot 食い込んでいた
            if (textGapDots > 0f && TryOpaqueX(_left, out _, out float lMax) && TryOpaqueX(_right, out float rMin, out _))
            {
                float g = textGapDots / Mathf.Max(1f, dotsPerUnit);
                lx = bd.min.x - g - lMax + nudge.x;      // 左の矢印の不透明な右端 = 項目の左端 − 間
                rx = bd.max.x + g - rMin + nudge.x;      // 右の矢印の不透明な左端 = 項目の右端 + 間
            }
            // 液晶の dot の格子にそろえる (半端な 0.5 dot に置かない)。 左右は字から離れる向きへ丸め (間が textGapDots より狭くならない)。
            //   上下は、 矢印の絵の高さが偶数 dot なので中心を整数の dot に置く。 項目の中心が x.5 の時はいつも上へ寄せる (Mathf.Round は偶数へ丸めるので行ごとに向きが割れる)
            if (dotsPerUnit > 0f)
            {
                lx = Mathf.Floor(lx * dotsPerUnit + 1e-4f) / dotsPerUnit;
                rx = Mathf.Ceil(rx * dotsPerUnit - 1e-4f) / dotsPerUnit;
                y = Mathf.Floor(y * dotsPerUnit + 0.5f) / dotsPerUnit;
            }
            _left.transform.position = new Vector3(lx, y, b.transform.position.z);
            _right.transform.position = new Vector3(rx, y, b.transform.position.z);
        }

        // 矢印の絵の不透明な範囲 (基準点からの横の位置・unit)。 絵ごとに 1 度だけ測る。 取り込みの設定で読めない (isReadable = 0) 絵でも測れるよう、 GPU で写して読む
        private static readonly System.Collections.Generic.Dictionary<Sprite, Vector2> _opaque = new System.Collections.Generic.Dictionary<Sprite, Vector2>();

        /// <summary>矢印 sr の絵の不透明 (alpha ≥ 250) な所の左端・右端 (矢印の中心からの横の距離・unit・左右反転を含む)。 測れなければ false。</summary>
        private static bool TryOpaqueX(SpriteRenderer sr, out float min, out float max)
        {
            min = max = 0f;
            var sp = sr != null ? sr.sprite : null;
            if (sp == null || sp.texture == null) return false;
            if (!_opaque.TryGetValue(sp, out var r))
            {
                r = MeasureOpaque(sp);
                _opaque[sp] = r;
            }
            if (r.x > r.y) return false;
            min = r.x; max = r.y;
            if (sr.flipX) { float a = -max, b = -min; min = a; max = b; }
            return true;
        }

        private static Vector2 MeasureOpaque(Sprite sp)
        {
            var bad = new Vector2(1f, 0f);
            var tex = sp.texture; var rect = sp.textureRect;
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
            if (w <= 0 || h <= 0) return bad;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0); t.Apply(false);
                var px = t.GetPixels32(); Object.Destroy(t);
                int x0 = int.MaxValue, x1 = -1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (px[y * w + x].a >= 250) { if (x < x0) x0 = x; if (x > x1) x1 = x; }
                if (x1 < 0) return bad;
                float ppu = sp.pixelsPerUnit, piv = sp.pivot.x;      // pivot は絵の左下からの px
                return new Vector2((x0 - piv) / ppu, (x1 + 1 - piv) / ppu);
            }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
        }

        /// <summary>left/rightSprite を変更した後、 生成済みカーソルへ反映する（昼夜切替など）。</summary>
        public void RefreshCursors()
        {
            if (_left == null || _right == null) return; // 未生成（編集中）なら OnEnable 時に反映
            _left.sprite = leftSprite != null ? leftSprite : cursorSprite;
            Sprite rs = rightSprite != null ? rightSprite : cursorSprite;
            _right.sprite = rs;
            _right.flipX = rightSprite != null ? false : mirrorRight;
        }

        private void EnsureCursors()
        {
            Sprite ls = leftSprite != null ? leftSprite : cursorSprite;
            Sprite rs = rightSprite != null ? rightSprite : cursorSprite;
            bool rflip = rightSprite != null ? false : mirrorRight; // 個別指定があれば反転しない
            int lyr = ResolveCursorLayer();
            _left = MakeCursor("Cursor_L", ls, false, lyr);
            _right = MakeCursor("Cursor_R", rs, rflip, lyr);
        }

        // カーソルは「項目（ボタン）と同じレイヤー」に置く。
        // 親(Menu)が Default でも、液晶を撮る ContentCamera が拾えるよう LcdContent 等へ合わせる。
        private int ResolveCursorLayer()
        {
            if (items != null)
                foreach (var it in items)
                    if (it != null) return it.gameObject.layer;
            return gameObject.layer;
        }

        private SpriteRenderer MakeCursor(string n, Sprite spr, bool flip, int layer)
        {
            var ex = transform.Find(n);
            GameObject go = ex != null ? ex.gameObject : new GameObject(n);
            if (ex == null) go.transform.SetParent(transform, false);
            go.layer = layer;

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spr;
            sr.color = color;
            sr.flipX = flip;
            sr.sortingOrder = sortingOrder;
            if (!string.IsNullOrEmpty(sortingLayer)) sr.sortingLayerName = sortingLayer;
            return sr;
        }

        private void SetVisible(bool v)
        {
            if (_left != null) _left.enabled = v;
            if (_right != null) _right.enabled = v;
        }
    }
}

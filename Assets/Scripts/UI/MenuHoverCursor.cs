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
            _left.transform.position = new Vector3(bd.min.x - gap + nudge.x, y, b.transform.position.z);
            _right.transform.position = new Vector3(bd.max.x + gap + nudge.x, y, b.transform.position.z);
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

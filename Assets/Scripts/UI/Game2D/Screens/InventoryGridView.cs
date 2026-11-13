using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 持ち物の画面の中身 (2026-11-13): 持っている物を全部、 種類ごとの見出しの下にアイコンの升目で並べ、 右に詳細の欄を置く。
    /// 窓 (<see cref="Game2DModal"/>) の行の場所に描く部品 (<see cref="IModalCanvas"/>)。 寸法の式は <see cref="InventoryLayout"/> (Unity 非依存・検算 Tools/inventory_check)。
    /// <para><b>見るだけ</b>: ゲームの状態は読むだけで書かない (使う・ステータスへ等は呼ぶ側のボタンが受ける)。 フォーカス・送りの位置は画面だけが持つ。</para>
    /// <para>操作: ポインタを升目に乗せる = 吹き出し (品名 + 効果の説明文) / 升目を押す = フォーカス (もう一度押すと外れる)・詳細の欄に出す /
    /// 矢印 = フォーカスを動かす (見えない所へ行くと送る) / ホイール = 升目の欄を送る (詳細の欄の上では頁) / Space・Enter・詳細の欄を押す = 詳細の次の頁 /
    /// Esc = 閉じる (<see cref="onClose"/>)。</para>
    /// </summary>
    public sealed class InventoryGridView : IModalCanvas
    {
        private List<List<ItemCardInfo>> _sections = new List<List<ItemCardInfo>>();
        private readonly List<ItemCardInfo> _flat = new List<ItemCardInfo>();
        private List<InventoryLayout.Line> _lines = new List<InventoryLayout.Line>();
        private readonly List<KeyValuePair<int, InventoryLayout.Box>> _cells = new List<KeyValuePair<int, InventoryLayout.Box>>();
        private int _top, _focus = -1, _hover = -1;
        private readonly ItemDetailPane _pane = new ItemDetailPane();

        /// <summary>Esc で呼ぶ。</summary>
        public System.Action onClose;
        /// <summary>フォーカスが替わった時に呼ぶ (呼ぶ側がボタン「使う」の有無を替える)。</summary>
        public System.Action onFocusChanged;
        /// <summary>何も持っていない時の字。</summary>
        public string emptyText = "何も持っていない。";

        private static int LH => Game2DJpFont.LineH;

        /// <summary>フォーカスしている品 (無ければ null)。</summary>
        public ItemCardInfo Focused => _focus >= 0 && _focus < _flat.Count ? _flat[_focus] : null;
        public int Count => _flat.Count;

        /// <summary>並べる物を差し替える (区切り = <see cref="ItemCardInfo.Kind"/> の順)。 フォーカスは同じ品 (key) に残す。 無くなったら同じ位置の品へ。</summary>
        public void SetItems(List<List<ItemCardInfo>> sections)
        {
            string keep = Focused?.key; int keepIdx = _focus;
            _sections = sections ?? new List<List<ItemCardInfo>>();
            _flat.Clear(); var counts = new List<int>();
            foreach (var s in _sections) { _flat.AddRange(s); counts.Add(s.Count); }
            _lines = InventoryLayout.BuildLines(counts, InventoryLayout.Cols(LH));
            _focus = -1;
            if (keep != null) { for (int i = 0; i < _flat.Count; i++) if (_flat[i].key == keep) { _focus = i; break; } if (_focus < 0 && _flat.Count > 0) _focus = Mathf.Clamp(keepIdx, 0, _flat.Count - 1); }
            if (_hover >= _flat.Count) _hover = -1;      // ポインタの下の升目は残す (押すたびに吹き出しと音をやり直さない)
            var g = InventoryLayout.Grid(LH);
            _top = _focus >= 0 ? InventoryLayout.EnsureVisible(_lines, _top, _focus, g.h, LH) : InventoryLayout.ClampTop(_lines, _top, g.h, LH);
            _pane.Show(Focused);
        }

        /// <summary>フォーカスと送りを最初に戻す (開き直した時)。</summary>
        public void ResetView() { _focus = -1; _hover = -1; _top = 0; _pane.Show(null); }

        private void SetFocus(int f)
        {
            if (f == _focus) return;
            _focus = f; _pane.Show(Focused);
            if (_focus >= 0) { var g = InventoryLayout.Grid(LH); _top = InventoryLayout.EnsureVisible(_lines, _top, _focus, g.h, LH); }
            onFocusChanged?.Invoke();
        }

        // ── 描く ──

        public bool Draw(KitPix p, RectInt area, int glintTick)
        {
            var g = InventoryLayout.Grid(LH); var dbox = InventoryLayout.Detail(LH);
            bool glint = false;
            _cells.Clear();
            if (_flat.Count == 0) Game2DJpFont.Draw(p, emptyText, g.x + 2, g.y + 2, Game2DKit.Dim);
            else
            {
                _top = InventoryLayout.ClampTop(_lines, _top, g.h, LH);
                int y = g.y, n = InventoryLayout.VisibleCount(_lines, _top, g.h, LH);
                for (int i = _top; i < _top + n; i++)
                {
                    var ln = _lines[i];
                    if (ln.heading)
                    {
                        string h = ItemCardInfo.KindNames[ln.section] + " " + ln.count;
                        int hw = Game2DJpFont.Draw(p, h, g.x + 1, y + 1, Game2DKit.Dim);
                        p.Rect(g.x + 1 + hw + 4, y + 1 + Game2DJpFont.Cell / 2, g.w - InventoryLayout.BarW - 2 - hw - 6, 1, Game2DKit.Faint);
                    }
                    y += ln.Height(LH);
                }
                _cells.AddRange(InventoryLayout.CellBoxes(_lines, _top, g, LH));
                foreach (var kv in _cells)
                    if (ItemSlot.Draw(p, _flat[kv.Key], kv.Value.x, kv.Value.y, kv.Key == _focus, kv.Key == _hover && kv.Key != _focus, glintTick)) glint = true;
                // 送りの棒 (全部が入らない時だけ)
                int max = InventoryLayout.MaxTop(_lines, g.h, LH);
                if (max > 0)
                {
                    int bx = g.Right - InventoryLayout.BarW;
                    p.Rect(bx, g.y, InventoryLayout.BarW, g.h, Game2DKit.Off);
                    int th = Mathf.Max(8, g.h * n / Mathf.Max(1, _lines.Count)), ty = g.y + (g.h - th) * _top / max;
                    p.Rect(bx, ty, InventoryLayout.BarW, th, Game2DKit.Dim);
                }
            }
            _pane.Draw(p, dbox);
            return glint;
        }

        public void DrawOverlay(KitPix p)
        {
            if (_hover < 0 || _hover == _focus || _hover >= _flat.Count) return;
            foreach (var kv in _cells) if (kv.Key == _hover) { ItemTooltip.Draw(p, _flat[_hover], kv.Value); return; }
        }

        // ── 入力 ──

        public bool Pointer(Vector2 local, bool inside, bool click)
        {
            int hv = -1; bool dirty = false;
            int lx = Mathf.FloorToInt(local.x), ly = Mathf.FloorToInt(local.y);
            if (inside) foreach (var kv in _cells) { var b = kv.Value; if (new InventoryLayout.Box(b.x - 1, b.y - 1, b.w + 2, b.h + 2).Contains(lx, ly)) { hv = kv.Key; break; } }
            if (hv != _hover) { if (hv >= 0) Game2DSfx.Play("ui_hover", 0.7f); _hover = hv; dirty = true; }
            if (!inside) return dirty;
            var g = InventoryLayout.Grid(LH); var dbox = InventoryLayout.Detail(LH);
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f)
            {
                if (g.Contains(lx, ly)) { int t = InventoryLayout.ClampTop(_lines, _top - (int)Mathf.Sign(wheel), g.h, LH); if (t != _top) { _top = t; _hover = -1; dirty = true; } }
                else if (dbox.Contains(lx, ly) && _pane.Next()) dirty = true;
            }
            if (!click) return dirty;
            if (hv >= 0) { Game2DSfx.Play("ui_click"); SetFocus(hv == _focus ? -1 : hv); return true; }
            if (dbox.Contains(lx, ly) && _pane.Next()) { Game2DSfx.Play("ui_click"); return true; }
            return dirty;
        }

        public bool Keys()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { onClose?.Invoke(); return false; }
            int dx = Input.GetKeyDown(KeyCode.LeftArrow) ? -1 : Input.GetKeyDown(KeyCode.RightArrow) ? 1 : 0;
            int dy = Input.GetKeyDown(KeyCode.UpArrow) ? -1 : Input.GetKeyDown(KeyCode.DownArrow) ? 1 : 0;
            if (dx != 0 || dy != 0)
            {
                int f = InventoryLayout.MoveFocus(_lines, _focus, dx, dy, _flat.Count);
                if (f != _focus) { Game2DSfx.Play("ui_hover", 0.7f); SetFocus(f); return true; }
                return false;
            }
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_pane.Next()) { Game2DSfx.Play("ui_click"); return true; }
            }
            return false;
        }
    }
}

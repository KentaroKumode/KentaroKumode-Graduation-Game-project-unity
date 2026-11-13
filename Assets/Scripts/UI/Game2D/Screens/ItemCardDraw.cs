using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 品の升目 1 つ (18×18) を描く (2026-11-13・持ち物の画面)。 地 → アイコン (16×16) か仮の字 → レア度の枠 → 数 → 強調の枠。
    /// アイコンの無い品 (<see cref="Game2DKit.ItemIcon"/> が null) は、 品名の頭の字 (<see cref="ItemCardInfo.glyph"/>) を枠の中に出す ── 新しい絵は使わない。
    /// 状態 (フォーカス・ホバー) は升目の外側 1 dot の枠の色で見せる (大きさは変えない・localScale は使わない)。
    /// </summary>
    public static class ItemSlot
    {
        /// <summary>(x, y) = 升目の左上 (枠を含む 18×18)。 focused = 琥珀の外枠 / hovered = 明るい外枠。 legendary の光を描いたら true。</summary>
        public static bool Draw(KitPix p, ItemCardInfo c, int x, int y, bool focused, bool hovered, int glintTick)
        {
            const int S = InventoryLayout.Cell, I = InventoryLayout.CellInner;
            bool glint = false;
            p.Rect(x + 1, y + 1, I, I, Game2DKit.Off);
            var ic = c.iconId != null ? Game2DKit.ItemIcon(c.iconId) : null;
            if (ic != null) p.Sprite(ic, x + 1, y + 1, I, I);
            else if (!string.IsNullOrEmpty(c.glyph))
            {
                int gw = Game2DJpFont.Width(c.glyph);
                var col = c.kind == ItemCardInfo.Kind.FacePart ? Game2DKit.Ivory : Game2DKit.Ink;
                Game2DJpFont.Draw(p, c.glyph, x + 1 + (I - gw) / 2, y + 1 + (I - Game2DJpFont.Cell) / 2 - (c.pips > 0 ? 1 : 0), col);
            }
            if (c.pips > 0)          // 出目パーツの段: 下の辺に点を段の数だけ
                for (int k = 0; k < c.pips; k++) p.Rect(x + 1 + (I - (c.pips * 3 - 1)) / 2 + k * 3, y + S - 4, 2, 2, Game2DKit.Gold);
            var fr = c.rarity >= 0 ? Game2DKit.RarityFrame((ItemSystem.ItemRarity)c.rarity) : null;
            if (fr != null)
            {
                p.Sprite(fr, x, y, S, S);
                if (c.rarity == (int)ItemSystem.ItemRarity.LEGENDARY) { Game2DKit.FrameGlint(p, fr, x, y, glintTick); glint = true; }
            }
            else
            {
                var e = Game2DKit.Dim;
                p.Rect(x, y, S, 1, e); p.Rect(x, y + S - 1, S, 1, e); p.Rect(x, y, 1, S, e); p.Rect(x + S - 1, y, 1, S, e);
            }
            // 数 (2 以上): 右下に暗い縁取りの字で。 升目に入らない桁は印だけ (詳細の欄に「個数」で出る)
            if (c.count >= 2)
            {
                string n = ItemCardInfo.CountLabel(c.count, I, Game2DJpFont.Advance);
                if (n != null)
                {
                    int nw = Game2DJpFont.Width(n), nx = x + S - 1 - nw, ny = y + S - Game2DJpFont.Cell;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (dx != 0 || dy != 0) Game2DJpFont.Draw(p, n, nx + dx, ny + dy, Game2DKit.Dark);
                    Game2DJpFont.Draw(p, n, nx, ny, Game2DKit.Ivory);
                }
                else p.Rect(x + S - 4, y + S - 4, 3, 3, Game2DKit.Amber);
            }
            if (focused || hovered)
            {
                var o = focused ? Game2DKit.Amber : Game2DKit.Ink;
                p.Rect(x - 1, y - 1, S + 2, 1, o); p.Rect(x - 1, y + S, S + 2, 1, o); p.Rect(x - 1, y - 1, 1, S + 2, o); p.Rect(x + S, y - 1, 1, S + 2, o);
            }
            return glint;
        }
    }

    /// <summary>
    /// ホバーの吹き出し (簡略説明 = 品名 + 効果の説明文・フレーバー文は出さない・2026-11-13)。 升目の近くの、 窓の中に収まる所に出す
    /// (置き場の式は <see cref="InventoryLayout.TipBox"/>)。 店・報酬の画面でも、 升目の箱と <see cref="ItemCardInfo"/> を渡せば同じ物が出る。
    /// </summary>
    public static class ItemTooltip
    {
        /// <summary>anchor = 升目の箱 (窓の座標)。</summary>
        public static void Draw(KitPix p, ItemCardInfo c, InventoryLayout.Box anchor)
        {
            if (c == null) return;
            int LH = Game2DJpFont.LineH;
            var lines = c.TooltipLines(InventoryLayout.TipTextW, Game2DJpFont.Advance);
            var texts = new List<string>(); foreach (var l in lines) texts.Add(l.text);
            InventoryLayout.TipSize(texts, Game2DJpFont.Advance, LH, out int w, out int h);
            var b = InventoryLayout.TipBox(anchor, w, h);
            p.Box(b.x, b.y, b.w, b.h, Game2DKit.Dark, Game2DKit.Dim);
            for (int i = 0; i < lines.Count; i++)
                Game2DJpFont.Draw(p, lines[i].text, b.x + InventoryLayout.Pad, b.y + InventoryLayout.Pad + i * LH, lines[i].role == 0 ? Game2DKit.Amber : Game2DKit.Ink);
        }
    }

    /// <summary>
    /// フォーカスした品の詳細の欄 (2026-11-13)。 品名・種類・レア度・軸・家系・段・個数・売値・印・効果の説明文・フレーバー文
    /// (<see cref="ItemCardInfo.DetailLines"/>)。 長い文は欄の中で頁に割り、 頁の位置「1/2」を右下に出す (<see cref="Next"/> で送る)。
    /// 窓の決まった場所 (<see cref="InventoryLayout.Detail"/>) に描くが、 箱を渡せばほかの画面でも使える。
    /// </summary>
    public sealed class ItemDetailPane
    {
        private ItemCardInfo _card; private int _page, _pages = 1;
        /// <summary>何も選んでいない時の字。</summary>
        public string emptyText = "升目を押すと、 ここに詳しく出る。";

        public int Page => _page;
        public int Pages => _pages;

        /// <summary>出す品を替える (違う品なら最初の頁へ)。</summary>
        public void Show(ItemCardInfo c) { if (c == null || _card == null || c.key != _card.key) { _page = 0; _pages = 1; } _card = c; }

        /// <summary>次の頁へ (最後の頁からは最初へ)。 頁が 1 つなら false。</summary>
        public bool Next() { if (_pages <= 1) return false; _page = (_page + 1) % _pages; return true; }

        public void Draw(KitPix p, InventoryLayout.Box b)
        {
            int LH = Game2DJpFont.LineH, pad = InventoryLayout.Pad;
            p.Box(b.x, b.y, b.w, b.h, Game2DKit.Well, Game2DKit.Faint);
            if (_card == null) { _pages = 1; _page = 0; Game2DJpFont.Wrap(p, emptyText, b.x + pad, b.y + pad, b.w - 2 * pad, Game2DKit.Dim); return; }
            var lines = _card.DetailLines(b.w - 2 * pad, Game2DJpFont.Advance);
            var pages = InventoryLayout.Pages(lines, InventoryLayout.PerPage(b.h, LH), l => l.text.Length == 0);
            _pages = pages.Count; _page = Mathf.Clamp(_page, 0, _pages - 1);
            var pg = pages[_page];
            for (int i = 0; i < pg.Count; i++)
            {
                var l = pg[i];
                var col = l.role == 0 ? Game2DKit.Amber : l.role == 1 ? Game2DKit.Dim : l.role == 3 ? Game2DKit.Dim : l.role == 4 ? Game2DKit.Red : Game2DKit.Ink;
                Game2DJpFont.Draw(p, l.text, b.x + pad, b.y + pad + i * LH, col);
            }
            if (_pages > 1)
            {
                string m = (_page + 1) + "/" + _pages;
                Game2DJpFont.Draw(p, m, b.Right - pad - Game2DJpFont.Width(m), b.Bottom - pad - LH + 1, Game2DKit.Dim);
            }
        }
    }
}

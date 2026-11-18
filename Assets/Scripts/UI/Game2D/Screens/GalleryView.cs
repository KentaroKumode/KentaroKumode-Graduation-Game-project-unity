using System.Collections.Generic;
using Codex;
using GameLoop;
using MetaProgression.Achievements;
using UI.Game2D.Battle;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// ギャラリーの画面の絵と操作 (2026-11-17・作者が見本 Captures/gallery_layout/sheet2_*.png で決めた形・決定は同じ所の decisions.md)。
    /// 液晶の中身 (480×270) をまるごと描く。 液晶へ写すのは <see cref="Game2DGalleryScreen"/> (液晶の効果が掛かる・タイトルとは別の画面)。
    ///
    /// <para>入口 (品・本・実績の 3 つの升) → それぞれの画面。 Esc で入口へ・入口で Esc でタイトルへ (<see cref="closeRequested"/>)。
    /// 背景は書庫の絵 (<see cref="BattleBackdrops.Library"/>)。 板は青灰 (見本の色・作者は未決)。 余白は見本どおり左右 8・上下 9 (液晶の湾曲で端が欠けないように)。</para>
    /// <list type="bullet">
    /// <item><b>品</b>: 図鑑の格子。 升目は持ち物の画面 (18) の 2 倍 (36)・武器はさらに 2 倍 (72)。 武器・秘宝・消耗品の見出しで区切り、 見出しの横に埋まり具合。
    ///   未入手はアイコンの形の影 (枠は灰・レア度も伏せる)・押しても開かない。 手に入れるまで影も出さない品は <see cref="ItemIds.HiddenInCodexUntilOwned"/> (結末に関わる品)。
    ///   押すと詳細の板を升目の反対側に出す (名前・付記・効果・武器の技・フレーバー)。</item>
    /// <item><b>本</b>: 左に細い一覧 (100)・右に本文 (スクロール・右端に細いバー)。 読んだ割合は出さない。 未解禁は「？？？」と手がかり (<see cref="VignetteUnlockState.Hint"/>)。</item>
    /// <item><b>実績</b>: 左に一覧 (系統の見出しで区切る)・右に中身。 行の頭の印は 取得 = 塗り / 未取得 = 枠 / 隠しの未取得 = 点線の枠。 隠しは取るまで「？？？」 (<see cref="AchievementService.IsMasked"/>)。</item>
    /// </list>
    /// </summary>
    public sealed class GalleryView
    {
        public enum Page { Entry = 0, Items = 1, Books = 2, Achievements = 3 }

        // ── 色 (見本 gallery_mock2.py の P) ──
        private static readonly Color32 Ink = new Color32(180, 200, 206, 255), Dim = new Color32(120, 136, 144, 255), Off = new Color32(84, 94, 102, 255),
            PlateCol = new Color32(10, 14, 20, 255), Edge = new Color32(70, 84, 96, 255), SelCol = new Color32(52, 70, 90, 255), BarCol = new Color32(216, 228, 232, 255),
            Chip = new Color32(196, 206, 150, 255), Boss = new Color32(210, 150, 120, 255), Amber = new Color32(214, 168, 92, 255),
            ShadowCol = new Color32(30, 38, 48, 255), ShadowEdge = new Color32(44, 54, 66, 255), Rim = new Color32(2, 5, 7, 255),
            NoBackdrop = new Color32(16, 20, 26, 255), Dark = new Color32(4, 6, 10, 255);

        // ── 配置 (dot・見本どおり) ──
        /// <summary>左右・上下の余白 (液晶の湾曲で端が欠けないように・見本の L 8 / R 472 / T 9 / B 259)。</summary>
        public const int L = 8, R = 472, T = 9, B = 259;
        /// <summary>上の段 (画面の名・Esc) の下から板が始まる高さ。</summary>
        public const int Y0 = 29;
        public const int Pad = 8, RowH = 15;
        private const byte PlateA = 236, TopA = 200;

        // 入口の升
        private const int TileW = 104, TileH = 104, TileGap = 20, TileY = 76, IconSize = 48;
        // 品の格子: 升目の大きさ (持ち物の画面 18 の 2 倍・武器は 4 倍)・升目の間・見出しの高さ・区切りの間
        public const int ItemCell = 36, WeaponCell = 72, CellGap = 4, HeadH = 16, SecGap = 4;
        private const int GridX = L + Pad + 1, GridRight = R - 10, GridTop = Y0 + 6, GridBot = B - 25, ViewH = GridBot - GridTop + 1;
        private const int DetailW = 212;
        // 本
        private const int BookListW = 100;
        // 実績
        private const int AchListW = 188;

        public Page page;
        /// <summary>入口で Esc (か「タイトルへ」) を押した ── 呼び手 (<see cref="Game2DGalleryScreen"/>) が閉じる。</summary>
        public bool closeRequested;

        // ── 中身 (開いた時に 1 度作る) ──
        private sealed class Cell { public ItemCardInfo card; public bool owned; public int sec, x, y, s; public List<string> skills; }
        private sealed class Section { public string name; public int y, owned, total; }
        private readonly List<Cell> _cells = new List<Cell>();
        private readonly List<Section> _secs = new List<Section>();
        private int _contentH, _itemOwned;

        private sealed class AchRow { public AchievementCategory cat; public AchievementDefinition def; }
        private readonly List<AchRow> _achRows = new List<AchRow>();
        private readonly List<int> _achRowOf = new List<int>();          // 実績の通し番号 → 行
        private readonly List<AchievementDefinition> _achDefs = new List<AchievementDefinition>();

        // ── 状態 ──
        private int _entrySel;
        private int _itemSel, _itemScroll, _detailScroll; private bool _detail; private bool _itemMsg;
        private int _bookSel, _bookScroll, _bookListTop; private bool _bookBody;
        private int _achSel, _achTop;

        // ── 当たり (描くたびに作り直す) ──
        private enum HitKind { Tile, Back, ItemCell, Detail, BookRow, BookBody, BookList, AchRow, AchList }
        private struct Hit { public RectInt r; public HitKind kind; public int idx; }
        private readonly List<Hit> _hits = new List<Hit>();
        private Hit? _hover;

        private Color32[] _bg;
        private KitPix _layer;
        private int _lastLineH = -1;

        // ============================================================
        //  中身を作る
        // ============================================================

        /// <summary>今の記録・実績から中身を作り直し、 入口から始める。</summary>
        public void Reset()
        {
            page = Page.Entry; closeRequested = false; _entrySel = 0;
            _itemSel = 0; _itemScroll = 0; _detail = false; _detailScroll = 0; _itemMsg = false;
            _bookSel = 0; _bookScroll = 0; _bookListTop = 0; _bookBody = false;
            _achSel = 0; _achTop = 0; _hover = null;
            LoadItems(); LoadAchievements();
        }

        private static int FamilyOrder(string f) => f == "sword" ? 0 : f == "axe" ? 1 : f == "dagger" ? 2 : f == "shield" ? 3 : 9;

        private void LoadItems()
        {
            _cells.Clear(); _secs.Clear(); _contentH = 0; _itemOwned = 0;
            var db = ItemSystem.ItemDatabase.Instance;
            if (db == null) return;
            var all = db.GetAllItems();
            var cats = new[] { ItemSystem.ItemCategory.Weapon, ItemSystem.ItemCategory.Passive, ItemSystem.ItemCategory.Consumable };
            var names = new[] { "武器", "秘宝", "消耗品" };
            int y = 0;
            for (int k = 0; k < cats.Length; k++)
            {
                var list = new List<KeyValuePair<int, ItemSystem.CompleteItemData>>();
                for (int i = 0; i < all.Count; i++)
                {
                    var it = all[i];
                    if (it == null || it.category != cats[k]) continue;
                    if (!CodexRecord.ItemOwned(it.id) && ItemIds.HiddenInCodexUntilOwned(it.id)) continue;   // 結末に関わる品は手に入れるまで影も出さない
                    list.Add(new KeyValuePair<int, ItemSystem.CompleteItemData>(i, it));
                }
                // 武器は家系 → 段・秘宝と消耗品はレア度の順 (同じ中は items.json の順)
                if (k == 0) list.Sort((a, b) => { int c = FamilyOrder(a.Value.family).CompareTo(FamilyOrder(b.Value.family)); if (c == 0) c = a.Value.tier.CompareTo(b.Value.tier); return c != 0 ? c : a.Key.CompareTo(b.Key); });
                else list.Sort((a, b) => { int c = ((int)a.Value.rarity).CompareTo((int)b.Value.rarity); return c != 0 ? c : a.Key.CompareTo(b.Key); });
                if (list.Count == 0) continue;
                var sec = new Section { name = names[k], y = y, total = list.Count };
                _secs.Add(sec);
                y += HeadH;
                int s = k == 0 ? WeaponCell : ItemCell, pitch = s + CellGap, cols = Mathf.Max(1, (GridRight - GridX + CellGap) / pitch);
                for (int i = 0; i < list.Count; i++)
                {
                    var it = list[i].Value;
                    bool owned = CodexRecord.ItemOwned(it.id);
                    if (owned) { sec.owned++; _itemOwned++; }
                    string cat = k == 0 ? "Weapon" : k == 2 ? "Consumable" : "Passive";
                    var card = ItemCardInfo.ForItem(it.id, ItemSystem.ItemDatabase.DisplayNameOf(it, it.id), cat, (int)it.rarity, it.description, it.flavorText,
                        it.axis, it.family, it.tier, null, null, 1, Game2DJpFont.Advance);     // 売値・個数は出さない (図鑑)
                    List<string> skills = null;
                    if (k == 0 && it.passiveSkills != null)
                        foreach (var ps in it.passiveSkills)
                        {
                            if (ps == null || ps.internalName == it.id || string.IsNullOrEmpty(ps.skillName)) continue;     // 品そのもののパッシブ (説明文と同じ) は重ねない
                            (skills ??= new List<string>()).Add("〈" + ps.skillName + "〉 " + ps.description);
                        }
                    _cells.Add(new Cell { card = card, owned = owned, sec = _secs.Count - 1, s = s, x = (i % cols) * pitch, y = y + (i / cols) * pitch, skills = skills });
                }
                int rows = (list.Count + cols - 1) / cols;
                y += rows * pitch - CellGap + SecGap;
            }
            _contentH = Mathf.Max(0, y - SecGap);
        }

        private void LoadAchievements()
        {
            _achRows.Clear(); _achRowOf.Clear(); _achDefs.Clear();
            foreach (var c in AchievementCatalog.Categories)
            {
                var l = AchievementCatalog.InCategory(c);
                if (l.Count == 0) continue;
                _achRows.Add(new AchRow { cat = c });
                foreach (var d in l) { _achRowOf.Add(_achRows.Count); _achDefs.Add(d); _achRows.Add(new AchRow { cat = c, def = d }); }
            }
        }

        private int BookOpenCount() { int n = 0; foreach (var v in WorldVignettes.All) if (v != null && VignetteUnlockState.IsUnlocked(v)) n++; return n; }

        // ============================================================
        //  描く
        // ============================================================

        public void Draw(KitPix p)
        {
            if (_layer == null || _layer.w != p.w || _layer.h != p.h) _layer = new KitPix(p.w, p.h);
            if (_lastLineH != Game2DJpFont.LineH) { _lastLineH = Game2DJpFont.LineH; }
            _hits.Clear();
            Backdrop(p);
            switch (page)
            {
                case Page.Entry: DrawEntry(p); break;
                case Page.Items: DrawItems(p); break;
                case Page.Books: DrawBooks(p); break;
                case Page.Achievements: DrawAch(p); break;
            }
        }

        private void Backdrop(KitPix p)
        {
            if (_bg == null)
            {
                var src = BattleBackdrops.Load(BattleBackdrops.Library);
                if (src != null && p.w == BattleBackdrops.W && p.h == BattleBackdrops.H)
                {
                    _bg = new Color32[p.w * p.h];
                    for (int y = 0; y < p.h; y++)
                        for (int x = 0; x < p.w; x++)
                        {
                            uint v = src[y * p.w + x];      // BattleCanvas.Pack: r | g << 8 | b << 16 (上の行から)
                            _bg[(p.h - 1 - y) * p.w + x] = new Color32((byte)v, (byte)(v >> 8), (byte)(v >> 16), 255);
                        }
                }
            }
            if (_bg != null) System.Array.Copy(_bg, p.px, _bg.Length);
            else p.Rect(0, 0, p.w, p.h, NoBackdrop);
        }

        /// <summary>上の段: 「GALLERY ／ 品」と埋まり具合・右に Esc (押せる)。 name が null なら入口 (「GALLERY」だけ・右は「Esc タイトルへ」)。</summary>
        private void TopBar(KitPix p, string name, string count)
        {
            Shade(p, L, T, R - L, 17, PlateCol, TopA, Edge);
            int ty = T + 3;
            if (name == null) Text(p, "GALLERY", L + 6, ty, Ink);
            else
            {
                int x = L + 6 + Text(p, "GALLERY", L + 6, ty, Dim);
                x += 6 + Text(p, "／", x + 6, ty, Off);
                x += 6 + Text(p, name, x + 6, ty, Ink);
                if (!string.IsNullOrEmpty(count)) Text(p, count, x + 10, ty, Off);
            }
            string esc = name == null ? "Esc タイトルへ" : "Esc 入口へ";
            int w = Game2DJpFont.Width(esc), ex = R - 6 - w;
            bool hv = IsHover(HitKind.Back, 0);
            Text(p, esc, ex, ty, hv ? Ink : Dim);
            _hits.Add(new Hit { r = new RectInt(ex - 3, T, w + 6, 17), kind = HitKind.Back });
        }

        // ── 入口 ──

        private void DrawEntry(KitPix p)
        {
            TopBar(p, null, null);
            var names = new[] { "品", "本", "実績" };
            var counts = new[]
            {
                _itemOwned + "/" + _cells.Count,
                BookOpenCount() + "/" + WorldVignettes.All.Count,
                AchievementService.UnlockedCount() + "/" + AchievementCatalog.All.Count,
            };
            int x0 = (Game2DLayout.Width - (3 * TileW + 2 * TileGap)) / 2;
            for (int k = 0; k < 3; k++)
            {
                bool on = k == _entrySel, hv = IsHover(HitKind.Tile, k);
                int x = x0 + k * (TileW + TileGap), y = TileY;
                Shade(p, x, y, TileW, TileH, on ? SelCol : PlateCol, (byte)(on ? 250 : PlateA), on ? Ink : hv ? Dim : Edge);
                if (on) p.Rect(x, y, TileW, 2, BarCol);      // 上の辺に帯 (大きさは変えない)
                Text(p, (k + 1).ToString(), x + 6, y + 5, Dim);
                GalleryIcons.Draw(p, k, x + (TileW - IconSize) / 2, y + 12, on, Ink, Dim, Off, SelCol, ShadowCol);
                TextC(p, names[k], x + TileW / 2, y + 66, on ? Ink : Dim);
                TextC(p, counts[k], x + TileW / 2, y + 83, on ? Dim : Off);
                _hits.Add(new Hit { r = new RectInt(x, y, TileW, TileH), kind = HitKind.Tile, idx = k });
            }
            string hint = "1〜3 で選ぶ";
            int hw = Game2DJpFont.Width(hint);
            Shade(p, L, B - 21, hw + 12, 21, PlateCol, 220, Edge);
            Text(p, hint, L + 6, B - 17, Dim);
        }

        // ── 品 ──

        private void DrawItems(KitPix p)
        {
            TopBar(p, "品", _itemOwned + "/" + _cells.Count);
            Shade(p, L, Y0, R - L, B - Y0, PlateCol, PlateA, Edge);
            ClampItemScroll();
            System.Array.Clear(_layer.px, 0, _layer.px.Length);
            int focusX = -1, focusY = -1;
            foreach (var sec in _secs)
            {
                int y = GridTop + sec.y - _itemScroll;
                if (y + HeadH < GridTop - 1 || y > GridBot + 1) continue;
                int nx = GridX + Text(_layer, sec.name, GridX, y + 1, Dim);
                nx += 6 + Text(_layer, sec.owned + "/" + sec.total, nx + 6, y + 1, Off);
                _layer.Rect(nx + 6, y + 7, Mathf.Max(0, GridRight - 3 - (nx + 6)), 1, Edge);
            }
            for (int i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                int sx = GridX + c.x, sy = GridTop + c.y - _itemScroll;
                if (sy + c.s < GridTop - 1 || sy > GridBot + 1) continue;
                bool f = i == _itemSel;
                DrawCell(_layer, c, sx, sy, c.s, f, !f && IsHover(HitKind.ItemCell, i));
                if (f) { focusX = sx; focusY = sy; }
                int top = Mathf.Max(sy, GridTop), bot = Mathf.Min(sy + c.s, GridBot + 1);
                if (bot > top) _hits.Add(new Hit { r = new RectInt(sx, top, c.s, bot - top), kind = HitKind.ItemCell, idx = i });
            }
            Composite(p, _layer, L + 1, GridTop - 1, R - L - 2, ViewH + 2);
            Scrollbar(p, R - 6, GridTop, GridBot, _contentH, ViewH, _itemScroll);
            // 下の段: フォーカスの品の名前と種類
            p.Rect(L + Pad, B - 21, R - L - 2 * Pad, 1, Edge);
            if (_cells.Count == 0) Text(p, "品が無い", L + Pad, B - 17, Off);
            else
            {
                var c = _cells[Mathf.Clamp(_itemSel, 0, _cells.Count - 1)];
                if (c.owned)
                {
                    int nx = L + Pad + Text(p, Game2DJpFont.Clip(c.card.name, 220), L + Pad, B - 17, Ink);
                    Text(p, KindLabel(c.card), nx + 8, B - 17, Dim);
                    if (!_detail) TextR(p, "Enter で詳しく", R - Pad, B - 17, Dim);
                }
                else
                {
                    Text(p, AchievementService.MaskText, L + Pad, B - 17, Off);
                    TextR(p, "まだ手に入れていない", R - Pad, B - 17, _itemMsg ? Dim : Off);
                }
            }
            if (_detail && _cells.Count > 0 && _cells[_itemSel].owned) DrawDetail(p, focusX, focusY);
        }

        private static string KindLabel(ItemCardInfo c) { string r = ItemCardInfo.RarityName(c.rarity); return r != null ? c.kindLabel + "・" + r : c.kindLabel; }

        /// <summary>升目 1 つ (s×s・枠を含む)。 持っている = 地 → アイコン (なければ頭の字) → レア度の枠 / 未入手 = アイコンの形の影 (なければ「？」)・灰の枠 (レア度を伏せる)。
        /// 升目は持ち物の画面の升目 (18) の整数倍 k で描く (絵も字も k 倍の点で打つ・localScale は使わない)。</summary>
        private static void DrawCell(KitPix q, Cell c, int x, int y, int s, bool focused, bool hovered)
        {
            int k = Mathf.Max(1, s / InventoryLayout.Cell), inner = s - 2 * k;
            var ic = Game2DKit.ItemIcon(c.card.iconId);
            if (ic != null && ic.px == null) ic = null;
            if (c.owned)
            {
                q.Rect(x + k, y + k, inner, inner, Off);
                if (ic != null) q.Sprite(ic, x + k, y + k, inner, inner);
                else if (!string.IsNullOrEmpty(c.card.glyph))
                {
                    int gw = Game2DJpFont.Width(c.card.glyph) * k;
                    Game2DJpFont.DrawLarge(q, c.card.glyph, x + (s - gw) / 2, y + (s - Game2DJpFont.Cell * k) / 2, k, 0, Ink);
                }
                var fr = c.card.rarity >= 0 ? Game2DKit.RarityFrame((ItemSystem.ItemRarity)c.card.rarity) : null;
                if (fr != null) q.Sprite(fr, x, y, s, s);
                else Outline(q, x, y, s, s, Dim);
            }
            else
            {
                q.Rect(x + 1, y + 1, s - 2, s - 2, PlateCol);
                if (ic != null) Silhouette(q, ic, x + k, y + k, inner, inner, ShadowCol);
                else
                {
                    string g = "？"; int gw = Game2DJpFont.Width(g) * k;
                    Game2DJpFont.DrawLarge(q, g, x + (s - gw) / 2, y + (s - Game2DJpFont.Cell * k) / 2, k, 0, Off);
                }
                Outline(q, x, y, s, s, ShadowEdge);
            }
            if (focused || hovered) Outline(q, x - 1, y - 1, s + 2, s + 2, focused ? Amber : Ink);
        }

        /// <summary>詳細の板: 選んだ升目と反対側の半分に出す (升目を隠さない)。 後ろの格子は暗くする。</summary>
        private void DrawDetail(KitPix p, int fx, int fy)
        {
            var c = _cells[_itemSel];
            bool left = fx >= 0 ? fx + c.s / 2 > Game2DLayout.Width / 2 : false;
            int x0 = left ? L + 2 : R - 2 - DetailW, top = Y0 + 2, h = B - 3 - top + 1;
            Shade(p, L + 1, Y0 + 1, R - L - 2, B - Y0 - 2, Dark, 120, null);
            if (fx >= 0)
            {
                // 選んだ升目は暗くしない (格子の見える範囲の中だけ打ち直す)
                System.Array.Clear(_layer.px, 0, _layer.px.Length);
                DrawCell(_layer, c, fx, fy, c.s, true, false);
                Composite(p, _layer, L + 1, GridTop - 1, R - L - 2, ViewH + 2);
            }
            Shade(p, x0, top, DetailW, h, PlateCol, 250, BarCol);
            _hits.Add(new Hit { r = new RectInt(x0, top, DetailW, h), kind = HitKind.Detail });
            DrawCell(p, c, x0 + Pad, Y0 + 9, InventoryLayout.Cell, false, false);
            int W = DetailW - 2 * Pad;
            Text(p, Game2DJpFont.Clip(c.card.name, W - 24), x0 + Pad + 24, Y0 + 12, Ink);
            var ls = DetailLines(c, W - 6);
            int y = Y0 + 32, LH = Game2DJpFont.LineH, nl = Mathf.Max(1, (B - 26 - y) / LH);
            _detailScroll = Mathf.Clamp(_detailScroll, 0, Mathf.Max(0, ls.Count - nl));
            for (int i = 0; i < nl && _detailScroll + i < ls.Count; i++)
            {
                var l = ls[_detailScroll + i];
                if (string.IsNullOrEmpty(l.Key)) continue;
                int ty = y + i * LH;
                if (l.Value == 5)
                {
                    int e = l.Key.IndexOf('〉');
                    if (l.Key.StartsWith("〈") && e > 0)
                    {
                        int nx = x0 + Pad + Text(p, l.Key.Substring(0, e + 1), x0 + Pad, ty, Chip);
                        Text(p, l.Key.Substring(e + 1), nx, ty, Ink);
                        continue;
                    }
                    Text(p, l.Key, x0 + Pad, ty, Ink);
                }
                else Text(p, l.Key, x0 + Pad, ty, l.Value == 1 || l.Value == 3 ? Dim : Ink);
            }
            if (ls.Count > nl) Scrollbar(p, x0 + DetailW - 5, y, y + nl * LH - 2, ls.Count, nl, _detailScroll);
            p.Rect(x0 + Pad, B - 23, DetailW - 2 * Pad, 1, Edge);
            Text(p, Game2DJpFont.Clip(ls.Count > nl ? "← → 隣の品  ↑ ↓ 送る  Esc 閉じる" : "← → 隣の品  Esc 閉じる", W), x0 + Pad, B - 19, Dim);
        }

        /// <summary>詳細の行 (文, 役) ── 1 = 付記 / 2 = 効果 / 5 = 武器の技 / 3 = フレーバー。 空の文は区切り。 折り返しは <see cref="JpTextLayout.Lines"/>。</summary>
        private static List<KeyValuePair<string, int>> DetailLines(Cell c, int w)
        {
            var l = new List<KeyValuePair<string, int>>();
            void Add(string s, int role) { foreach (var x in Game2DJpFont.Lines(s, w)) l.Add(new KeyValuePair<string, int>(x, role)); }
            void Blank() { if (l.Count > 0 && l[l.Count - 1].Key.Length > 0) l.Add(new KeyValuePair<string, int>("", 0)); }
            // 付記 (ItemCardInfo.MetaParts と同じ並び・売値と個数は持たせていない)。 項目の区切りで折り返す
            string cur = "";
            foreach (var part in c.card.MetaParts())
            {
                string cand = cur.Length == 0 ? part : cur + "  " + part;
                if (Game2DJpFont.Width(cand) <= w) { cur = cand; continue; }
                if (cur.Length > 0) l.Add(new KeyValuePair<string, int>(cur, 1));
                if (Game2DJpFont.Width(part) <= w) cur = part; else { Add(part, 1); cur = ""; }
            }
            if (cur.Length > 0) l.Add(new KeyValuePair<string, int>(cur, 1));
            if (!string.IsNullOrEmpty(c.card.description)) { Blank(); Add(c.card.description, 2); }
            if (c.skills != null) foreach (var s in c.skills) { Blank(); Add(s, 5); }
            if (!string.IsNullOrEmpty(c.card.flavor)) { Blank(); Add(c.card.flavor, 3); }
            return l;
        }

        private void ClampItemScroll() => _itemScroll = Mathf.Clamp(_itemScroll, 0, Mathf.Max(0, _contentH - ViewH));

        /// <summary>フォーカスの升目が見えるように送る (区切りの 1 行目なら見出しごと)。</summary>
        private void RevealItem()
        {
            if (_cells.Count == 0) return;
            var c = _cells[_itemSel];
            int top = c.y - 1, bot = c.y + c.s + 1;
            var sec = _secs[c.sec];
            if (c.y - sec.y - HeadH < c.s) top = sec.y;      // 区切りの 1 行目
            if (top < _itemScroll) _itemScroll = top;
            else if (bot > _itemScroll + ViewH) _itemScroll = bot - ViewH;
            ClampItemScroll();
        }

        /// <summary>↑ ↓: 今の升目より上 (下) の行のうち一番近い行で、 横の位置が一番近い升目。 大きさの違う区切りをまたいでも動く。</summary>
        private int VerticalNeighbor(int dir)
        {
            var cur = _cells[_itemSel];
            int cx = cur.x + cur.s / 2, cy = cur.y + cur.s / 2, bestRow = int.MinValue, best = -1, bestDx = int.MaxValue;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < _cells.Count; i++)
                {
                    var c = _cells[i]; int ccy = c.y + c.s / 2;
                    if (dir < 0 ? ccy >= cy : ccy <= cy) continue;
                    int rowKey = dir < 0 ? c.y : -c.y;
                    if (pass == 0) { if (rowKey > bestRow) bestRow = rowKey; continue; }
                    if (rowKey != bestRow) continue;
                    int dx = Mathf.Abs(c.x + c.s / 2 - cx);
                    if (dx < bestDx) { bestDx = dx; best = i; }
                }
            return best;
        }

        private int NextOwned(int from, int dir)
        {
            for (int i = from + dir; i >= 0 && i < _cells.Count; i += dir) if (_cells[i].owned) return i;
            return -1;
        }

        // ── 本 ──

        private void DrawBooks(KitPix p)
        {
            var all = WorldVignettes.All;
            TopBar(p, "本", BookOpenCount() + "/" + all.Count);
            int n = all.Count, LH = Game2DJpFont.LineH;
            _bookSel = Mathf.Clamp(_bookSel, 0, Mathf.Max(0, n - 1));
            // 左: 細い一覧
            Shade(p, L, Y0, BookListW, B - Y0, PlateCol, PlateA, Edge);
            _hits.Add(new Hit { r = new RectInt(L, Y0, BookListW, B - Y0), kind = HitKind.BookList });
            int rows = (B - 4 - (Y0 + 4)) / RowH;
            if (_bookSel < _bookListTop) _bookListTop = _bookSel; else if (_bookSel >= _bookListTop + rows) _bookListTop = _bookSel - rows + 1;
            _bookListTop = Mathf.Clamp(_bookListTop, 0, Mathf.Max(0, n - rows));
            for (int i = 0; i < rows && _bookListTop + i < n; i++)
            {
                int k = _bookListTop + i, y = Y0 + 4 + i * RowH;
                var v = all[k]; bool open = VignetteUnlockState.IsUnlocked(v), sel = k == _bookSel;
                if (sel) RowSel(p, L + 3, y, BookListW - 6, RowH, !_bookBody);
                else if (IsHover(HitKind.BookRow, k)) Outline(p, L + 3, y, BookListW - 6, RowH, Edge);
                var col = !open ? Off : (sel || !_bookBody ? Ink : Dim);
                if (sel && !open) col = Dim;      // 選んだ行は地が明るいので 1 段明るく
                Text(p, Game2DJpFont.Clip(open ? v.title : AchievementService.MaskText, BookListW - 16), L + 8, y + 2, col);
                _hits.Add(new Hit { r = new RectInt(L + 3, y, BookListW - 6, RowH), kind = HitKind.BookRow, idx = k });
            }
            if (n > rows) Scrollbar(p, L + BookListW - 4, Y0 + 5, Y0 + 4 + rows * RowH - 2, n, rows, _bookListTop);
            // 右: 本文 (スクロール)
            int rx = L + BookListW + 4, rw = R - rx;
            Shade(p, rx, Y0, rw, B - Y0, PlateCol, PlateA, _bookBody ? BarCol : Edge);
            _hits.Add(new Hit { r = new RectInt(rx, Y0, rw, B - Y0), kind = HitKind.BookBody });
            if (n == 0) return;
            var cur = all[_bookSel]; bool curOpen = VignetteUnlockState.IsUnlocked(cur);
            int bw = rw - 2 * Pad - 8;
            Text(p, Game2DJpFont.Clip(curOpen ? cur.title : AchievementService.MaskText, bw), rx + Pad, Y0 + 6, Ink);
            p.Rect(rx + Pad, Y0 + 21, rw - 2 * Pad, 1, Edge);
            int top = Y0 + 26, bot = B - 24, nl = Mathf.Max(1, (bot - top + 1) / LH);
            if (!curOpen)
            {
                var hl = Game2DJpFont.Lines(VignetteUnlockState.Hint(cur), bw);
                for (int i = 0; i < hl.Count && i < nl; i++) Text(p, hl[i], rx + Pad, top + i * LH, Dim);
                Text(p, "まだ読めない", rx + Pad, B - 17, Off);
                return;
            }
            var ls = BookLines(cur, bw);
            _bookScroll = Mathf.Clamp(_bookScroll, 0, Mathf.Max(0, ls.Count - nl));
            for (int i = 0; i < nl && _bookScroll + i < ls.Count; i++) Text(p, ls[_bookScroll + i], rx + Pad, top + i * LH, Ink);
            Scrollbar(p, R - 6, top, top + nl * LH - 2, ls.Count, nl, _bookScroll);
            p.Rect(rx + Pad, B - 21, rw - 2 * Pad, 1, Edge);
            Text(p, _bookBody ? "↑ ↓ でスクロール  ← 一覧へ" : "↑ ↓ で選ぶ  → 本文へ", rx + Pad, B - 17, Dim);
        }

        // 本文の折り返しは本と幅で決まるので、 最後に作った物を持っておく (スクロールのたびに折り返し直さない)。 字間・行間の設定が変わったら作り直す
        private WorldVignettes.Vignette _linesFor; private int _linesW = -1, _linesAdv = -1; private List<string> _lines;
        private List<string> BookLines(WorldVignettes.Vignette v, int w)
        {
            int adv = Game2DJpFont.Advance('あ');
            if (_lines == null || _linesFor != v || _linesW != w || _linesAdv != adv) { _lines = Game2DJpFont.Lines(v.body ?? "", w); _linesFor = v; _linesW = w; _linesAdv = adv; }
            return _lines;
        }

        private int BookBodyLinesVisible() => Mathf.Max(1, (B - 24 - (Y0 + 26) + 1) / Game2DJpFont.LineH);

        // ── 実績 ──

        private int AchListRows => (B - 22 - (Y0 + 4)) / RowH;

        private void RevealAch()
        {
            if (_achDefs.Count == 0) return;
            int r = _achRowOf[_achSel], n = AchListRows;
            int want = r > 0 && _achRows[r - 1].def == null ? r - 1 : r;      // 系統の 1 つ目なら見出しごと
            if (want < _achTop) _achTop = want; else if (r >= _achTop + n) _achTop = r - n + 1;
            _achTop = Mathf.Clamp(_achTop, 0, Mathf.Max(0, _achRows.Count - n));
        }

        private void DrawAch(KitPix p)
        {
            TopBar(p, "実績", AchievementService.UnlockedCount() + "/" + AchievementCatalog.All.Count);
            Shade(p, L, Y0, AchListW, B - Y0, PlateCol, PlateA, Edge);
            _hits.Add(new Hit { r = new RectInt(L, Y0, AchListW, B - Y0), kind = HitKind.AchList });
            int n = AchListRows;
            _achTop = Mathf.Clamp(_achTop, 0, Mathf.Max(0, _achRows.Count - n));
            for (int i = 0; i < n && _achTop + i < _achRows.Count; i++)
            {
                var row = _achRows[_achTop + i]; int y = Y0 + 4 + i * RowH;
                if (row.def == null)
                {
                    int nx = L + 9 + Text(p, AchievementCatalog.CategoryName(row.cat), L + 9, y + 2, Off);
                    p.Rect(nx + 6, y + 8, Mathf.Max(0, L + AchListW - 12 - (nx + 6)), 1, Edge);
                    continue;
                }
                int k = _achDefs.IndexOf(row.def);
                bool sel = k == _achSel, got = AchievementService.IsUnlocked(row.def.id), masked = AchievementService.IsMasked(row.def);
                if (sel) RowSel(p, L + 3, y, AchListW - 10, RowH, true);
                else if (IsHover(HitKind.AchRow, k)) Outline(p, L + 3, y, AchListW - 10, RowH, Edge);
                AchMark(p, L + 9, y + 4, got, row.def.hidden);
                var col = got ? Ink : !masked ? Dim : Off;
                if (sel) col = masked ? Dim : Ink;      // 選んだ行は地が明るいので 1 段明るく
                Text(p, Game2DJpFont.Clip(AchievementService.ShownName(row.def), AchListW - 36), L + 22, y + 2, col);
                _hits.Add(new Hit { r = new RectInt(L + 3, y, AchListW - 10, RowH), kind = HitKind.AchRow, idx = k });
            }
            Scrollbar(p, L + AchListW - 6, Y0 + 5, Y0 + 4 + n * RowH - 2, _achRows.Count, n, _achTop);
            p.Rect(L + Pad, B - 21, AchListW - 2 * Pad, 1, Edge);
            Text(p, "↑ ↓ で選ぶ", L + Pad, B - 17, Dim);
            // 右: 中身
            int rx = L + AchListW + 4, rw = R - rx, w = rw - 2 * Pad, LH = Game2DJpFont.LineH;
            Shade(p, rx, Y0, rw, B - Y0, PlateCol, PlateA, Edge);
            if (_achDefs.Count == 0) return;
            var d = _achDefs[Mathf.Clamp(_achSel, 0, _achDefs.Count - 1)];
            bool g = AchievementService.IsUnlocked(d.id);
            int ty = Y0 + 6;
            foreach (var l in Game2DJpFont.Lines(AchievementService.ShownName(d), w)) { Text(p, l, rx + Pad, ty, g ? Ink : Dim); ty += LH; }
            ty += 2;
            int sx = rx + Pad + Text(p, g ? "取得済み" : "未取得", rx + Pad, ty, g ? Chip : Off);
            if (d.hidden) Text(p, "隠し実績", sx + 10, ty, g ? Boss : Off);
            ty += LH + 4;
            p.Rect(rx + Pad, ty, w, 1, Edge); ty += 6;
            Text(p, "条件", rx + Pad, ty, Dim); ty += LH;
            foreach (var l in Game2DJpFont.Lines(AchievementService.ShownDescription(d), w))
            {
                if (ty + LH > B - 22) break;
                Text(p, l, rx + Pad, ty, g ? Ink : Dim); ty += LH;
            }
            // 取得した日 (控えめに・右の板の下の段)
            var at = g ? AchievementService.UnlockedAtUtc(d.id) : null;
            if (at.HasValue)
            {
                p.Rect(rx + Pad, B - 21, w, 1, Edge);
                Text(p, "取得 " + at.Value.ToLocalTime().ToString("yyyy-MM-dd"), rx + Pad, B - 17, Off);
            }
        }

        /// <summary>一覧の行の頭の印 (8×8)。 取得 = 塗り / 未取得 = 枠だけ / 隠しの未取得 = 点線の枠。</summary>
        private static void AchMark(KitPix p, int x, int y, bool got, bool hidden)
        {
            if (got) { p.Rect(x, y, 8, 8, Chip); return; }
            if (hidden)
            {
                for (int k = 0; k < 8; k += 2) { p.Set(x + k, y, Off); p.Set(x + k, y + 7, Off); p.Set(x, y + k, Off); p.Set(x + 7, y + k, Off); }
                return;
            }
            Outline(p, x, y, 8, 8, Dim);
        }

        // ============================================================
        //  操作
        // ============================================================

        /// <summary>キー。 描き直しが要れば true。</summary>
        public bool Keys()
        {
            bool esc = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace);
            bool ok = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
            bool lf = Input.GetKeyDown(KeyCode.LeftArrow), rt = Input.GetKeyDown(KeyCode.RightArrow), up = Input.GetKeyDown(KeyCode.UpArrow), dn = Input.GetKeyDown(KeyCode.DownArrow);
            bool pu = Input.GetKeyDown(KeyCode.PageUp), pd = Input.GetKeyDown(KeyCode.PageDown);
            switch (page)
            {
                case Page.Entry:
                    if (esc) { closeRequested = true; return true; }
                    for (int k = 0; k < 3; k++)
                        if (Input.GetKeyDown(KeyCode.Alpha1 + k) || Input.GetKeyDown(KeyCode.Keypad1 + k)) { _entrySel = k; Go((Page)(k + 1)); return true; }
                    if (lf || rt) { int s = Mathf.Clamp(_entrySel + (lf ? -1 : 1), 0, 2); if (s != _entrySel) { _entrySel = s; Game2DSfx.Play("ui_click"); } return true; }
                    if (ok) { Go((Page)(_entrySel + 1)); return true; }
                    return false;
                case Page.Items:
                    if (_cells.Count == 0) { if (esc) { Back(); return true; } return false; }
                    if (_detail)
                    {
                        if (esc || ok) { _detail = false; Game2DSfx.Play("ui_close"); return true; }
                        if (lf || rt) { int j = NextOwned(_itemSel, lf ? -1 : 1); if (j >= 0) { _itemSel = j; _detailScroll = 0; RevealItem(); Game2DSfx.Play("ui_click"); } return true; }
                        if (up || dn) { _detailScroll += up ? -1 : 1; return true; }
                        return false;
                    }
                    if (esc) { Back(); return true; }
                    if (ok) { OpenDetail(); return true; }
                    if (pu || pd) { _itemScroll += (pu ? -1 : 1) * (ViewH - ItemCell); ClampItemScroll(); return true; }
                    int to = _itemSel;
                    if (lf) to = Mathf.Max(0, _itemSel - 1);
                    else if (rt) to = Mathf.Min(_cells.Count - 1, _itemSel + 1);
                    else if (up || dn) { int j = VerticalNeighbor(up ? -1 : 1); if (j >= 0) to = j; }
                    else return false;
                    if (to != _itemSel) { _itemSel = to; _itemMsg = false; Game2DSfx.Play("ui_hover", 0.7f); }
                    RevealItem();
                    return true;
                case Page.Books:
                {
                    int n = WorldVignettes.All.Count;
                    if (esc) { Back(); return true; }
                    if (_bookBody)
                    {
                        if (lf) { _bookBody = false; Game2DSfx.Play("ui_click"); return true; }
                        int nl = BookBodyLinesVisible();
                        if (up || dn) { _bookScroll += up ? -1 : 1; return true; }
                        if (pu || pd) { _bookScroll += (pu ? -1 : 1) * Mathf.Max(1, nl - 1); return true; }
                        return false;
                    }
                    if ((rt || ok) && n > 0 && VignetteUnlockState.IsUnlocked(WorldVignettes.All[_bookSel])) { _bookBody = true; Game2DSfx.Play("ui_click"); return true; }
                    if (rt || ok) { Game2DSfx.Error(); return true; }
                    if (up || dn) { int s = Mathf.Clamp(_bookSel + (up ? -1 : 1), 0, Mathf.Max(0, n - 1)); if (s != _bookSel) { _bookSel = s; _bookScroll = 0; Game2DSfx.Play("ui_hover", 0.7f); } return true; }
                    return false;
                }
                case Page.Achievements:
                    if (esc) { Back(); return true; }
                    if ((up || dn || pu || pd) && _achDefs.Count > 0)
                    {
                        int step = pu || pd ? AchListRows - 1 : 1;
                        int s = Mathf.Clamp(_achSel + ((up || pu) ? -step : step), 0, _achDefs.Count - 1);
                        if (s != _achSel) { _achSel = s; Game2DSfx.Play("ui_hover", 0.7f); }
                        RevealAch();
                        return true;
                    }
                    return false;
            }
            return false;
        }

        private void Go(Page pg)
        {
            page = pg; _hover = null; Game2DSfx.Play("ui_confirm");
            if (pg == Page.Items) { _detail = false; RevealItem(); }
            if (pg == Page.Books) _bookBody = false;
            if (pg == Page.Achievements) RevealAch();
        }

        private void Back() { page = Page.Entry; _detail = false; _hover = null; Game2DSfx.Play("ui_close"); }

        private void OpenDetail()
        {
            if (_cells.Count == 0) return;
            if (!_cells[_itemSel].owned) { _itemMsg = true; Game2DSfx.Error(); return; }
            _detail = true; _detailScroll = 0; RevealItem(); Game2DSfx.Play("ui_open");
        }

        /// <summary>マウス (dot = 湾曲を戻した液晶の dot)。 描き直しが要れば true。</summary>
        public bool Pointer(Vector2 dot, bool inside, bool click, float wheel)
        {
            Hit? h = null;
            if (inside)
                for (int i = _hits.Count - 1; i >= 0; i--)      // 後に描いた物 (詳細の板) が上
                {
                    var r = _hits[i].r;
                    if (dot.x >= r.x && dot.x < r.x + r.width && dot.y >= r.y && dot.y < r.y + r.height) { h = _hits[i]; break; }
                }
            bool redraw = false;
            if (!Same(h, _hover))
            {
                bool clickable = h.HasValue && (h.Value.kind == HitKind.Tile || h.Value.kind == HitKind.Back || h.Value.kind == HitKind.ItemCell || h.Value.kind == HitKind.BookRow || h.Value.kind == HitKind.AchRow);
                if (clickable) Game2DSfx.Play("ui_hover", 0.5f);
                _hover = h; redraw = true;
            }
            if (Mathf.Abs(wheel) > 0.01f && inside) redraw |= Wheel(h, wheel > 0 ? -1 : 1);
            if (!click) return redraw;
            if (!h.HasValue)
            {
                if (page == Page.Items && _detail) { _detail = false; Game2DSfx.Play("ui_close"); return true; }
                return redraw;
            }
            var hit = h.Value;
            switch (hit.kind)
            {
                case HitKind.Back: if (page == Page.Entry) closeRequested = true; else Back(); return true;
                case HitKind.Tile: _entrySel = hit.idx; Go((Page)(hit.idx + 1)); return true;
                case HitKind.ItemCell:
                    _itemSel = hit.idx; _itemMsg = false;
                    if (_cells[hit.idx].owned) { bool was = _detail; _detail = true; _detailScroll = 0; Game2DSfx.Play(was ? "ui_click" : "ui_open"); }
                    else { _detail = false; _itemMsg = true; Game2DSfx.Error(); }
                    return true;
                case HitKind.Detail: return redraw;
                case HitKind.BookRow:
                    if (hit.idx != _bookSel) { _bookSel = hit.idx; _bookScroll = 0; }
                    _bookBody = false; Game2DSfx.Play("ui_click"); return true;
                case HitKind.BookBody:
                    if (WorldVignettes.All.Count > 0 && VignetteUnlockState.IsUnlocked(WorldVignettes.All[_bookSel]) && !_bookBody) { _bookBody = true; Game2DSfx.Play("ui_click"); return true; }
                    return redraw;
                case HitKind.AchRow: _achSel = hit.idx; Game2DSfx.Play("ui_click"); return true;
            }
            if (page == Page.Items && _detail) { _detail = false; Game2DSfx.Play("ui_close"); return true; }
            return redraw;
        }

        /// <summary>ホイール (dir = −1 上へ / +1 下へ)。 指している所を送る。</summary>
        private bool Wheel(Hit? h, int dir)
        {
            var k = h.HasValue ? h.Value.kind : (HitKind)(-1);
            switch (page)
            {
                case Page.Items:
                    if (_detail && k == HitKind.Detail) { _detailScroll += dir; return true; }
                    _itemScroll += dir * (ItemCell + CellGap) / 2; ClampItemScroll(); return true;
                case Page.Books:
                    if (k == HitKind.BookList || k == HitKind.BookRow)
                    {
                        int n = WorldVignettes.All.Count, s = Mathf.Clamp(_bookSel + dir, 0, Mathf.Max(0, n - 1));
                        if (s != _bookSel) { _bookSel = s; _bookScroll = 0; _bookBody = false; }
                        return true;
                    }
                    _bookScroll += dir * 2; return true;
                case Page.Achievements:
                    if (k == HitKind.AchList || k == HitKind.AchRow) { _achTop = Mathf.Clamp(_achTop + dir * 2, 0, Mathf.Max(0, _achRows.Count - AchListRows)); return true; }
                    return false;
            }
            return false;
        }

        private static bool Same(Hit? a, Hit? b) => a.HasValue == b.HasValue && (!a.HasValue || (a.Value.kind == b.Value.kind && a.Value.idx == b.Value.idx));
        private bool IsHover(HitKind k, int idx) => _hover.HasValue && _hover.Value.kind == k && _hover.Value.idx == idx;

        // ============================================================
        //  描く道具
        // ============================================================

        /// <summary>字を打つ: 先に周り 1 dot (8 方向) を暗い縁取り、 その上に字 (イベントの画面・結末の画面と同じ・液晶のにじみ対策)。 戻り値 = 幅。</summary>
        private static int Text(KitPix p, string s, int x, int y, Color32 c)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            Game2DJpFont.Draw((px, py) => p.Rect(px - 1, py - 1, 3, 3, Rim), s, x, y);
            return Game2DJpFont.Draw(p, s, x, y, c);
        }

        private static void TextC(KitPix p, string s, int xc, int y, Color32 c) => Text(p, s, xc - Game2DJpFont.Width(s) / 2, y, c);
        private static void TextR(KitPix p, string s, int xr, int y, Color32 c) => Text(p, s, xr - Game2DJpFont.Width(s), y, c);

        /// <summary>板: 地の色を a の濃さで重ね、 1 dot の縁 (edge が null なら縁なし) を描く。</summary>
        private static void Shade(KitPix p, int x, int y, int w, int h, Color32 fill, byte a, Color32? edge)
        {
            int b = 255 - a;
            int i0 = edge.HasValue ? 1 : 0;
            for (int j = i0; j < h - i0; j++)
                for (int i = i0; i < w - i0; i++)
                {
                    int X = x + i, Y = y + j; if (X < 0 || Y < 0 || X >= p.w || Y >= p.h) continue;
                    int k = (p.h - 1 - Y) * p.w + X; var c = p.px[k];
                    p.px[k] = new Color32((byte)((c.r * b + fill.r * a) / 255), (byte)((c.g * b + fill.g * a) / 255), (byte)((c.b * b + fill.b * a) / 255), 255);
                }
            if (edge.HasValue) Outline(p, x, y, w, h, edge.Value);
        }

        private static void Outline(KitPix p, int x, int y, int w, int h, Color32 c)
        {
            p.Rect(x, y, w, 1, c); p.Rect(x, y + h - 1, w, 1, c); p.Rect(x, y, 1, h, c); p.Rect(x + w - 1, y, 1, h, c);
        }

        private static void RowSel(KitPix p, int x, int y, int w, int h, bool focus)
        {
            Shade(p, x, y, w, h, SelCol, 255, focus ? Ink : Edge);
            if (focus) p.Rect(x, y, 2, h, BarCol);
        }

        /// <summary>細いスクロールバー (2 dot)。 total = 中身の量・view = 見える量・pos = 見えている頭。</summary>
        private static void Scrollbar(KitPix p, int x, int y0, int y1, int total, int view, int pos)
        {
            p.Rect(x, y0, 2, y1 - y0 + 1, ShadowEdge);
            if (total <= view) return;
            int len = y1 - y0 + 1, h = Mathf.Max(8, len * view / total);
            int ty = y0 + (len - h) * Mathf.Clamp(pos, 0, total - view) / (total - view);
            p.Rect(x, ty, 2, h, BarCol);
        }

        /// <summary>絵の形 (α 128 以上の画素) だけを色 c で打つ (未入手の影)。</summary>
        private static void Silhouette(KitPix q, KitSrc s, int x, int y, int tw, int th, Color32 c)
        {
            for (int j = 0; j < th; j++)
                for (int i = 0; i < tw; i++)
                    if (s.px[(j * s.h / th) * s.w + (i * s.w / tw)].a >= 128) q.Set(x + i, y + j, c);
        }

        /// <summary>重ねの層 (α 0 = 何も無い) を、 箱 (x, y, w, h) の中だけ写す (格子のスクロールで、 板の外へはみ出した升目を切る)。</summary>
        private static void Composite(KitPix p, KitPix layer, int x, int y, int w, int h)
        {
            for (int j = Mathf.Max(0, y); j < Mathf.Min(p.h, y + h); j++)
            {
                int row = (p.h - 1 - j) * p.w;
                for (int i = Mathf.Max(0, x); i < Mathf.Min(p.w, x + w); i++)
                {
                    var c = layer.px[row + i]; if (c.a != 0) p.px[row + i] = c;
                }
            }
        }
    }

    /// <summary>
    /// ギャラリーの入口の 3 つのアイコン (各 48×48・品 / 本 / 実績)。 <b>絵はまだ無い</b> (UI の絵は生成してから組む決まり・2026-11-17)。
    /// 置き場: Resources/<see cref="Dir"/>icon_items.png.bytes・icon_books.png.bytes・icon_achievements.png.bytes
    /// (戦闘の背景と同じく PNG をテキストの資産として読む ── .meta が git 管理外で、 取り込みの設定に左右されないように)。
    /// 無い時は、 見本 (gallery_mock2.py の icon_placeholder) と同じ簡単な形をコードで描いて代わりにする。
    /// </summary>
    public static class GalleryIcons
    {
        public const string Dir = "Game2DGallery/";
        public static readonly string[] Names = { "icon_items", "icon_books", "icon_achievements" };
        private static readonly KitSrc[] _cache = new KitSrc[3];
        private static readonly bool[] _tried = new bool[3];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { for (int i = 0; i < 3; i++) { _cache[i] = null; _tried[i] = false; } }

        private static KitSrc Load(int k)
        {
            if (_tried[k]) return _cache[k];
            _tried[k] = true;
            var a = Resources.Load<TextAsset>(Dir + Names[k] + ".png");
            if (a == null) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (t.LoadImage(a.bytes)) _cache[k] = KitSrc.From(t);
            else Debug.LogWarning("[GalleryIcons] " + Names[k] + " を読めない");
            Object.Destroy(t); Resources.UnloadAsset(a);
            return _cache[k];
        }

        /// <summary>アイコン k (0 品 / 1 本 / 2 実績) を (x, y) に 48×48 で打つ。 on = 選んでいる升。</summary>
        public static void Draw(KitPix p, int k, int x, int y, bool on, Color32 ink, Color32 dim, Color32 off, Color32 sel, Color32 shadow)
        {
            var s = Load(k);
            if (s != null) { p.Sprite(s, x, y, 48, 48); return; }
            Color32 a = on ? ink : dim, b = on ? dim : off, c = on ? sel : shadow;
            switch (k)
            {
                case 0:      // 宝石 (切子)
                {
                    var poly = new[] { new Vector2Int(10, 18), new Vector2Int(18, 8), new Vector2Int(30, 8), new Vector2Int(38, 18), new Vector2Int(24, 40) };
                    FillPoly(p, x, y, poly, c); PolyLine(p, x, y, poly, a);
                    Line(p, x + 10, y + 18, x + 38, y + 18, a); Line(p, x + 18, y + 8, x + 21, y + 18, b); Line(p, x + 30, y + 8, x + 27, y + 18, b);
                    Line(p, x + 21, y + 18, x + 24, y + 40, b); Line(p, x + 27, y + 18, x + 24, y + 40, b);
                    break;
                }
                case 1:      // 閉じた本 (背と小口)
                    p.Box(x + 11, y + 7, 26, 35, c, a); p.Rect(x + 11, y + 7, 5, 35, b);
                    p.Rect(x + 33, y + 9, 4, 31, shadow); Line(p, x + 33, y + 9, x + 33, y + 39, a);
                    p.Rect(x + 19, y + 14, 12, 1, b); p.Rect(x + 19, y + 19, 12, 1, b); p.Rect(x + 19, y + 14, 1, 6, b); p.Rect(x + 30, y + 14, 1, 6, b);
                    break;
                default:     // 杯 (トロフィー)
                {
                    var cup = new[] { new Vector2Int(13, 7), new Vector2Int(35, 7), new Vector2Int(33, 22), new Vector2Int(28, 27), new Vector2Int(20, 27), new Vector2Int(15, 22) };
                    FillPoly(p, x, y, cup, c); PolyLine(p, x, y, cup, a);
                    HalfEllipse(p, x + 11, y + 15, 5, 6, -1, a); HalfEllipse(p, x + 37, y + 15, 5, 6, 1, a);
                    p.Rect(x + 22, y + 27, 4, 8, b); p.Box(x + 16, y + 35, 16, 6, c, a);
                    break;
                }
            }
        }

        private static void Line(KitPix p, int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, e = dx + dy;
            while (true)
            {
                p.Set(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * e;
                if (e2 >= dy) { e += dy; x0 += sx; }
                if (e2 <= dx) { e += dx; y0 += sy; }
            }
        }

        private static void PolyLine(KitPix p, int ox, int oy, Vector2Int[] v, Color32 c)
        {
            for (int i = 0; i < v.Length; i++) { var a = v[i]; var b = v[(i + 1) % v.Length]; Line(p, ox + a.x, oy + a.y, ox + b.x, oy + b.y, c); }
        }

        private static void FillPoly(KitPix p, int ox, int oy, Vector2Int[] v, Color32 c)
        {
            int minY = int.MaxValue, maxY = int.MinValue, minX = int.MaxValue, maxX = int.MinValue;
            foreach (var q in v) { minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y); minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); }
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    bool inside = false; float px = x + 0.5f, py = y + 0.5f;
                    for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                        if ((v[i].y > py) != (v[j].y > py) && px < (v[j].x - v[i].x) * (py - v[i].y) / (float)(v[j].y - v[i].y) + v[i].x) inside = !inside;
                    if (inside) p.Set(ox + x, oy + y, c);
                }
        }

        /// <summary>楕円の左半分 (side −1) か右半分 (+1) の線 (杯の取っ手)。</summary>
        private static void HalfEllipse(KitPix p, int cx, int cy, int rx, int ry, int side, Color32 c)
        {
            for (int t = 0; t <= 64; t++)
            {
                float a = Mathf.PI * (t / 64f - 0.5f);      // −90°〜90°
                p.Set(cx + side * Mathf.RoundToInt(Mathf.Cos(a) * rx), cy + Mathf.RoundToInt(Mathf.Sin(a) * ry), c);
            }
        }
    }
}

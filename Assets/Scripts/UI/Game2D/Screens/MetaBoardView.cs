using System.Collections.Generic;
using MetaProgression;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 整備盤の画面の部品の絵 (2026-11-23・PixelLab・部品表 Captures/ui/meta_board/parts.md)。 Resources/Game2DMetaBoard/&lt;名前&gt;.png.bytes を読む
    /// (シーンに絵を挿さない・Game2DIcons と同じ読み方)。 マスの絵は透明な余白を切り落として持つ (置く時にマスの真ん中へ寄せる)。
    /// </summary>
    internal static class MetaBoardArt
    {
        private static readonly Dictionary<string, KitSrc> _cache = new Dictionary<string, KitSrc>();

        /// <summary>部品の絵 (無ければ null・呼ぶ側は長方形で代える)。 trim = 透明な余白を切り落とす。</summary>
        public static KitSrc Get(string name, bool trim = true)
        {
            string key = name + (trim ? "#t" : "");
            if (_cache.TryGetValue(key, out var s)) return s;
            var ta = Resources.Load<TextAsset>("Game2DMetaBoard/" + name + ".png");
            if (ta != null)
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                if (t.LoadImage(ta.bytes)) s = KitSrc.From(t);
                Object.Destroy(t);
                if (s != null && trim) s = Trim(s);
            }
            _cache[key] = s;
            return s;
        }

        private static KitSrc Trim(KitSrc s)
        {
            int x0 = s.w, y0 = s.h, x1 = -1, y1 = -1;
            for (int y = 0; y < s.h; y++) for (int x = 0; x < s.w; x++) if (s.px[y * s.w + x].a >= 128) { x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x); y1 = Mathf.Max(y1, y); }
            if (x1 < 0) return s;
            var r = new KitSrc { w = x1 - x0 + 1, h = y1 - y0 + 1 };
            r.px = new Color32[r.w * r.h];
            for (int y = 0; y < r.h; y++) System.Array.Copy(s.px, (y0 + y) * s.w + x0, r.px, y * r.w, r.w);
            return r;
        }

        /// <summary>2×2 の面の縁 (明るいドット) を軸の色に染めた写し。 明るさは元のドットのまま (色だけ替える)。</summary>
        public static KitSrc Tinted(string name, Color32 c)
        {
            string key = name + "#" + c.r + "," + c.g + "," + c.b;
            if (_cache.TryGetValue(key, out var s)) return s;
            var b = Get(name);
            if (b == null) return null;
            int maxL = 1; foreach (var p in b.px) if (p.a >= 128) maxL = Mathf.Max(maxL, p.r + p.g + p.b);
            s = new KitSrc { w = b.w, h = b.h, px = new Color32[b.px.Length] };
            for (int i = 0; i < b.px.Length; i++)
            {
                var p = b.px[i]; int l = p.r + p.g + p.b;
                if (p.a < 128 || l * 100 < maxL * 55) { s.px[i] = p; continue; }
                float k = l / (float)maxL;
                s.px[i] = new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), 255);
            }
            _cache[key] = s;
            return s;
        }

        /// <summary>絵の平均の色 (棚の形の小さな絵に使う)。</summary>
        public static Color32 Mean(KitSrc s, Color32 fallback)
        {
            if (s == null) return fallback;
            long r = 0, g = 0, b = 0, n = 0;
            foreach (var p in s.px) if (p.a >= 128) { r += p.r; g += p.g; b += p.b; n++; }
            return n == 0 ? fallback : new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
        }
    }

    /// <summary>
    /// 整備盤の画面の中身 (2026-11-23・docs/GAME.md §15-1)。 窓 (<see cref="Game2DModal"/>) の中を自分で描く部品 (<see cref="IModalCanvas"/>)。
    /// <para>左: 3×3 の盤 (置いたパネル・閉じたマスは錠と値段) と盤全体の効果の合計。 右上: 形で絞るタブと棚。 右下: 選んだパネルの名前・形・効果と [回す] [置く] [外す] [買う]。</para>
    /// <para>操作: 棚で選ぶ → 盤の上に影 (置ける = 白・置けない = 赤) がポインタ / 矢印で動く → R / 右クリックで 90 度回す → クリック / Enter で置く。
    /// 置いたパネルをクリックで外す。 閉じたマスをクリック → 「開けますか？」の確かめ (Enter / はい)。 Esc で閉じる (確かめの間は確かめを閉じる)。</para>
    /// <para>ゲームの処理は <see cref="MetaProgressManager"/> と <see cref="MetaBoard"/> の口だけ (置ける判定・回転・購入・開放)。</para>
    /// </summary>
    public sealed class MetaBoardView : IModalCanvas
    {
        // ── 寸法 (窓の座標・480×270 の液晶の窓 472×258) ──
        public const int W = 472, H = 258;
        private const int PlateX = 20, PlateY = 40, PlateS = 112, CellX = PlateX + 11, CellY = PlateY + 11, Pitch = 30, CellS = 28;
        private const int SumX = 18, SumY = PlateY + PlateS + 4, SumW = 128;
        private const int RX = 152, RW = 304, TabY = 40, TabH = 16, ShelfY = 60, RowH = 20, ShelfRows = 5;
        private const int DetY = ShelfY + ShelfRows * RowH + 6, BtnY = 222, BtnW = 70, BtnH = 20, BtnGap = 6;

        private static readonly string[] TabNames = { "全部", "1", "2", "3", "T", "2×2", "S・Z" };

        private int _tab, _top, _sel = -1, _hoverRow = -1, _hoverBtn = -1, _hoverTab = -1;
        private int _rot, _ar = 1, _ac = 1;          // 影の回転と、 影の左上 (盤の行・列)
        private bool _ghostOn;                        // 影を盤の上に出している (ポインタが盤の上か、 キーで動かした)
        private int _confirmCell = -1, _confirmHover = -1;
        private readonly List<BoardPanelDef> _list = new List<BoardPanelDef>();
        private readonly RectInt[] _btnR = new RectInt[5];
        private readonly RectInt[] _confirmR = new RectInt[2];

        /// <summary>Esc で呼ぶ (窓を閉じる)。</summary>
        public System.Action onClose;
        /// <summary>「挑戦」を押した時 (挑戦の窓へ)。</summary>
        public System.Action onChallenge;

        private static MetaProgressManager Mgr => MetaProgressManager.Instance;
        private static MetaProgressState St => Mgr?.State;

        public MetaBoardView() { Rebuild(); }

        private static int TabOf(BoardPanelDef d) => d.shape switch { "1" => 1, "I2" => 2, "I3" or "L3" => 3, "T" => 4, "O" => 5, "S" or "Z" => 6, _ => 0 };

        private void Rebuild()
        {
            string keep = Selected?.id;
            _list.Clear();
            foreach (var d in MetaBoard.All) if (_tab == 0 || TabOf(d) == _tab) _list.Add(d);
            _sel = keep != null ? _list.FindIndex(d => d.id == keep) : -1;
            _top = Mathf.Clamp(_top, 0, Mathf.Max(0, _list.Count - ShelfRows));
        }

        private BoardPanelDef Selected => _sel >= 0 && _sel < _list.Count ? _list[_sel] : null;
        private bool Unknown(BoardPanelDef d) => d.FromAchievement && !(St?.OwnsPanel(d.id) ?? false);

        // ── 盤の判定 (MetaBoard の口) ──

        private BoardPlacement Ghost => Selected != null ? new BoardPlacement(Selected.id, _rot, _ar, _ac) : null;

        /// <summary>影の置き方が盤に収まるように左上を寄せる (はみ出す分だけ戻す)。</summary>
        private void ClampAnchor()
        {
            if (Selected == null) return;
            var cells = MetaBoard.Rotated(Selected.shape, _rot);
            int hr = 0, hc = 0; foreach (var c in cells) { hr = Mathf.Max(hr, c.r); hc = Mathf.Max(hc, c.c); }
            _ar = Mathf.Clamp(_ar, 0, MetaBoard.Size - 1 - hr); _ac = Mathf.Clamp(_ac, 0, MetaBoard.Size - 1 - hc);
        }

        /// <summary>影がその場所に置けるか (持っている・開いたマスだけ・ほかのパネルと重ならない)。 PlacePanel と同じ条件。</summary>
        private bool GhostFits()
        {
            var st = St; var g = Ghost;
            if (st == null || g == null || !st.OwnsPanel(g.id)) return false;
            int m = MetaBoard.MaskOf(MetaBoard.CellsOf(g));
            if (m == 0 || (m & ~st.boardOpenMask) != 0) return false;
            foreach (var q in st.ValidPlacements) if (q.id != g.id && (MetaBoard.MaskOf(MetaBoard.CellsOf(q)) & m) != 0) return false;
            return true;
        }

        private string PanelAt(int cell)
        {
            var st = St; if (st == null) return null;
            foreach (var q in st.ValidPlacements) if (System.Array.IndexOf(MetaBoard.CellsOf(q), cell) >= 0) return q.id;
            return null;
        }

        private static RectInt CellRect(int cell) => new RectInt(CellX + (cell % 3) * Pitch, CellY + (cell / 3) * Pitch, CellS, CellS);

        // ── 描く ──

        private static KitSrc FaceOf(BoardPanelDef d)
        {
            switch (d.shape)
            {
                case "1": return MetaBoardArt.Get("face_gray");
                case "I2": return MetaBoardArt.Get("face_blue");
                case "I3": case "L3": return MetaBoardArt.Get("face_green");
                case "T": return MetaBoardArt.Get("face_amber");
                case "S": case "Z": return MetaBoardArt.Get("face_purple");
                case "O": return MetaBoardArt.Tinted("face_spark", SparkColor(d.Module));
                default: return MetaBoardArt.Get("face_gray");
            }
        }

        /// <summary>種火の軸の色 (画面の既存の色・Game2DKit)。</summary>
        private static Color32 SparkColor(MetaModule? m) => m switch
        {
            MetaModule.SparkCharge => Game2DKit.Blue, MetaModule.SparkRinkai => Game2DKit.Amber, MetaModule.SparkPoison => Game2DKit.Green,
            MetaModule.SparkBleed => Game2DKit.Red, MetaModule.SparkShield => Game2DKit.Ivory, MetaModule.SparkCrit => Game2DKit.Gold,
            MetaModule.SparkDeck => Game2DKit.Dim, MetaModule.SparkRole => Game2DKit.Ink, _ => Game2DKit.Ink,
        };

        /// <summary>絵をマスの箱の真ん中に、 箱からはみ出す所を切って打つ (縮めない)。</summary>
        private static void Center(KitPix p, KitSrc s, RectInt box)
        {
            if (s == null) { p.Box(box.x, box.y, box.width, box.height, Game2DKit.Off, Game2DKit.Faint); return; }
            int ox = box.x + (box.width - s.w) / 2, oy = box.y + (box.height - s.h) / 2;
            for (int y = 0; y < s.h; y++) for (int x = 0; x < s.w; x++)
            {
                int px = ox + x, py = oy + y; if (px < box.x || py < box.y || px >= box.xMax || py >= box.yMax) continue;
                var c = s.px[y * s.w + x]; if (c.a >= 128) p.Set(px, py, new Color32(c.r, c.g, c.b, 255));
            }
        }

        /// <summary>半透明に重ねる (影)。 tint の色へ寄せてから a の割合で混ぜる。</summary>
        private static void Overlay(KitPix p, KitSrc s, RectInt box, Color32 tint, float a)
        {
            if (s == null) return;
            int ox = box.x + (box.width - s.w) / 2, oy = box.y + (box.height - s.h) / 2;
            for (int y = 0; y < s.h; y++) for (int x = 0; x < s.w; x++)
            {
                var c = s.px[y * s.w + x]; if (c.a < 128) continue;
                int px = ox + x, py = oy + y; if (px < 0 || py < 0 || px >= p.w || py >= p.h) continue;
                int i = (p.h - 1 - py) * p.w + px; var d = p.px[i];
                float l = (c.r + c.g + c.b) / 765f;
                byte Mix(byte dst, byte t) => (byte)Mathf.Clamp(dst + (t * (0.6f + 0.4f * l) - dst) * a, 0, 255);
                p.px[i] = new Color32(Mix(d.r, tint.r), Mix(d.g, tint.g), Mix(d.b, tint.b), 255);
            }
        }

        private static void Plate(KitPix p, string name, int x, int y, int w, int h, int b = 5)
            => p.NineSlice(MetaBoardArt.Get(name, false), b, Game2DKit.Off, x, y, w, h);

        private static void Token(KitPix p, int x, int y) => Center(p, MetaBoardArt.Get("token"), new RectInt(x, y, 12, 10));

        public bool Draw(KitPix p, RectInt area, int glintTick)
        {
            var st = St; if (st == null) return false;
            // 盤
            var plate = MetaBoardArt.Get("board_plate", false);
            if (plate != null) p.Sprite(plate, PlateX, PlateY, plate.w, plate.h); else p.Box(PlateX, PlateY, PlateS, PlateS, Game2DKit.Off, Game2DKit.Faint);
            for (int c = 0; c < MetaBoard.Cells; c++)
            {
                var r = CellRect(c);
                bool open = (st.boardOpenMask & 1 << c) != 0;
                string pid = open ? PanelAt(c) : null;
                if (!open)
                {
                    Center(p, MetaBoardArt.Get("cell_locked"), r);
                    int cost = MetaBoard.OpenCost(st.boardOpenMask, c);
                    if (cost >= 0) { string s = cost.ToString(); int tw = Game2DKit.TextWidth(s); p.Rect(r.x + (r.width - tw) / 2 - 1, r.yMax - Game2DJpFont.Cell - 1, tw + 2, Game2DJpFont.Cell + 1, Game2DKit.Dark); p.Text(s, r.x + (r.width - tw) / 2, r.yMax - Game2DJpFont.Cell, Game2DKit.Amber); }
                }
                else if (pid == null) Center(p, MetaBoardArt.Get("cell_empty"), r);
                else { Center(p, MetaBoardArt.Get("cell_base"), r); Center(p, FaceOf(MetaBoard.Get(pid)), r); }
            }
            // 影
            if (_ghostOn && Selected != null && !Unknown(Selected) && _confirmCell < 0)
            {
                bool ok = GhostFits();
                var cells = MetaBoard.CellsOf(Ghost);
                if (cells != null) foreach (int c in cells) Overlay(p, MetaBoardArt.Get(ok ? "ghost_ok" : "ghost_bad"), CellRect(c), ok ? Game2DKit.Ivory : Game2DKit.Red, 0.55f);
            }
            // 盤全体の効果の合計
            var e = st.BoardEffects;
            var parts = new List<string>();
            if (e.hp > 0) parts.Add("HP+" + e.hp); if (e.defense > 0) parts.Add("防御+" + e.defense); if (e.damagePct > 0) parts.Add("与ダメ+" + e.damagePct + "%");
            if (e.saleSlots > 0) parts.Add("特売+" + e.saleSlots); if (e.gold > 0) parts.Add("開幕+" + e.gold + "G");
            var mods = new List<string>();
            foreach (var q in st.ValidPlacements) { var d = MetaBoard.Get(q.id); if (d?.Module != null) mods.Add(d.name); }
            string sum = (parts.Count > 0 ? string.Join(" ", parts) : "効果なし") + (mods.Count > 0 ? "\n" + string.Join("・", mods) : "");
            Game2DJpFont.Wrap(p, sum, SumX, SumY, SumW, Game2DKit.Ink, 4);
            // 上: トークンと挑戦
            string tok = st.tokens.ToString(); int tw2 = Game2DKit.TextWidth(tok);
            p.Text(tok, RX + RW - tw2, 22, Game2DKit.Gold); Token(p, RX + RW - tw2 - 14, 23);
            _btnR[4] = new RectInt(RX + RW - tw2 - 62, 18, 44, 18);
            Plate(p, _hoverBtn == 4 ? "btn_board_hot" : "btn_board", _btnR[4].x, _btnR[4].y, _btnR[4].width, _btnR[4].height);
            p.TextC("挑戦", _btnR[4], Game2DKit.Ink);
            // タブ
            int tx = RX;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int w = Mathf.Max(26, Game2DKit.TextWidth(TabNames[i]) + 10);
                var r = new RectInt(tx, TabY, w, TabH);
                Plate(p, i == _tab ? "shelf_card_sel" : "shelf_card", r.x, r.y, r.width, r.height, 3);
                p.TextC(TabNames[i], r, i == _tab ? Game2DKit.Dark : i == _hoverTab ? Game2DKit.Ivory : Game2DKit.Dim);
                tx += w + 2;
            }
            // 棚
            for (int k = 0; k < ShelfRows; k++)
            {
                int i = _top + k; if (i >= _list.Count) break;
                var d = _list[i]; var r = ShelfRow(k);
                bool sel = i == _sel, own = st.OwnsPanel(d.id), unknown = Unknown(d), placed = st.ValidPlacements.Exists(q => q.id == d.id);
                Plate(p, sel ? "shelf_card_sel" : "shelf_card", r.x, r.y, r.width, r.height, 4);
                var ink = sel ? Game2DKit.Ivory : unknown ? Game2DKit.Faint : i == _hoverRow ? Game2DKit.Ivory : Game2DKit.Ink;
                MiniShape(p, d, r.x + 4, r.y + 3, unknown ? Game2DKit.Faint : MetaBoardArt.Mean(FaceOf(d), Game2DKit.Dim));
                p.Text(unknown ? "？？？" : d.name, r.x + 22, r.y + (r.height - Game2DJpFont.Cell) / 2 + 1, ink);
                string v = placed ? "置いた" : own ? "持" : unknown ? "実績" : d.cost.ToString();
                p.TextR(v, r.xMax - (own || unknown ? 6 : 18), r.y + (r.height - Game2DJpFont.Cell) / 2 + 1, own ? Game2DKit.Green : Game2DKit.Gold);
                if (!own && !unknown) Token(p, r.xMax - 16, r.y + 5);
            }
            if (_list.Count > ShelfRows)
            {
                int bh = ShelfRows * RowH, th = Mathf.Max(8, bh * ShelfRows / _list.Count), ty = ShelfY + (bh - th) * _top / Mathf.Max(1, _list.Count - ShelfRows);
                p.Rect(RX + RW + 2, ShelfY, 2, bh, Game2DKit.Off); p.Rect(RX + RW + 2, ty, 2, th, Game2DKit.Dim);
            }
            // 詳細
            var s2 = Selected;
            if (s2 == null) Game2DJpFont.Wrap(p, "棚からパネルを選ぶと、 盤の上に影が出る。 R・右クリックで回す・クリック・Enter で置く。 閉じたマスを押すと開ける。", RX, DetY, RW, Game2DKit.Dim, 3);
            else if (Unknown(s2))
                Game2DJpFont.Wrap(p, "？？？ ― 実績〈" + (MetaProgression.Achievements.AchievementCatalog.Get(s2.achievement)?.displayName ?? s2.achievement) + "〉で手に入る。", RX, DetY, RW, Game2DKit.Dim, 3);
            else
            {
                p.Text(s2.name + "  " + ShapeName(s2.shape), RX, DetY, Game2DKit.Ivory);
                Game2DJpFont.Wrap(p, Game2DMetaPanel.Describe(s2), RX, DetY + Game2DJpFont.LineH, RW, Game2DKit.Ink, 2);
            }
            // ボタン
            string[] labels = { "回す", "置く", "外す", BuyLabel() };
            for (int i = 0; i < 4; i++)
            {
                _btnR[i] = new RectInt(RX + i * (BtnW + BtnGap), BtnY, BtnW, BtnH);
                bool on = ButtonOn(i);
                Plate(p, !on ? "btn_board_off" : i == _hoverBtn || (i == 3 && on) ? "btn_board_hot" : "btn_board", _btnR[i].x, _btnR[i].y, BtnW, BtnH);
                var col = !on ? Game2DKit.Faint : i == 3 ? Game2DKit.Dark : Game2DKit.Ink;
                if (i == 3 && on)
                {
                    int lw = Game2DKit.TextWidth(labels[3]);
                    p.Text(labels[3], _btnR[3].x + (BtnW - lw - 12) / 2, BtnY + (BtnH - Game2DJpFont.Cell) / 2 + 1, col);
                    Token(p, _btnR[3].x + (BtnW - lw - 12) / 2 + lw + 1, BtnY + 5);
                }
                else p.TextC(i == 3 ? "買う" : labels[i], _btnR[i], col);
            }
            return false;
        }

        public void DrawOverlay(KitPix p)
        {
            if (_confirmCell < 0 || St == null) return;
            int cost = MetaBoard.OpenCost(St.boardOpenMask, _confirmCell);
            var box = new RectInt(PlateX + 4, PlateY + 30, 150, 58);
            Plate(p, "shelf_card", box.x, box.y, box.width, box.height, 4);
            string q = "開けますか？ " + cost; int qw = Game2DKit.TextWidth(q);
            p.Text(q, box.x + (box.width - qw - 12) / 2, box.y + 8, Game2DKit.Ivory); Token(p, box.x + (box.width - qw - 12) / 2 + qw + 1, box.y + 9);
            bool afford = St.tokens >= cost;
            for (int i = 0; i < 2; i++)
            {
                _confirmR[i] = new RectInt(box.x + 10 + i * 68, box.y + 30, 62, 20);
                bool on = i == 1 || afford;
                Plate(p, !on ? "btn_board_off" : i == _confirmHover ? "btn_board_hot" : "btn_board", _confirmR[i].x, _confirmR[i].y, 62, 20);
                p.TextC(i == 0 ? (afford ? "開ける" : "足りない") : "やめる", _confirmR[i], on ? Game2DKit.Ink : Game2DKit.Faint);
            }
        }

        private static string ShapeName(string shape) => shape switch
        {
            "1" => "1 マス", "I2" => "2 マス", "I3" => "3 マス (I)", "L3" => "3 マス (L)", "T" => "4 マス (T)", "O" => "4 マス (2×2)", "S" => "4 マス (S)", "Z" => "4 マス (Z)", _ => shape,
        };

        private static void MiniShape(KitPix p, BoardPanelDef d, int x, int y, Color32 col)
        {
            foreach (var c in MetaBoard.Rotated(d.shape, 0)) p.Rect(x + c.c * 5, y + c.r * 5, 4, 4, col);
        }

        private RectInt ShelfRow(int k) => new RectInt(RX, ShelfY + k * RowH, RW, RowH - 1);

        private string BuyLabel() { var d = Selected; return d != null && !d.FromAchievement && !(St?.OwnsPanel(d.id) ?? true) ? "買う " + d.cost : "買う"; }

        private bool ButtonOn(int i)
        {
            var st = St; var d = Selected; if (st == null || d == null) return false;
            bool own = st.OwnsPanel(d.id);
            switch (i)
            {
                case 0: return own;
                case 1: return own && GhostFitsSomewhere();
                case 2: return st.ValidPlacements.Exists(q => q.id == d.id);
                case 3: return !own && !d.FromAchievement && st.tokens >= d.cost;
                default: return false;
            }
        }

        /// <summary>[置く] を押せるか: 影が今の場所に置ける (影を盤に出していなければ、 どこかに置ける所がある)。</summary>
        private bool GhostFitsSomewhere()
        {
            if (_ghostOn) return GhostFits();
            int keepR = _ar, keepC = _ac;
            for (_ar = 0; _ar < 3; _ar++) for (_ac = 0; _ac < 3; _ac++) if (GhostFits()) { _ar = keepR; _ac = keepC; return true; }
            _ar = keepR; _ac = keepC; return false;
        }

        // ── 操作 ──

        private void Select(int i)
        {
            if (i == _sel) return;
            _sel = i; _rot = 0; ClampAnchor();
            Game2DSfx.Play("ui_click");
        }

        private void Rotate() { if (Selected == null || Unknown(Selected)) return; _rot = (_rot + 1) % 4; ClampAnchor(); _ghostOn = true; Game2DSfx.Play("ui_hover", 0.7f); }

        private void Place()
        {
            var g = Ghost; if (g == null) return;
            if (!_ghostOn)
            {   // 影を出していない時は、 置ける最初の所へ (盤の左上から)
                for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) { _ar = r; _ac = c; ClampAnchor(); if (GhostFits()) goto found; }
                Game2DSfx.Play("ui_error"); return;
                found: g = Ghost;
            }
            if (GhostFits() && Mgr.TryPlacePanel(g)) { Game2DSfx.Play("ui_confirm"); _ghostOn = false; }
            else Game2DSfx.Play("ui_error");
        }

        private void Remove(string id) { if (id == null) return; Mgr.RemovePanel(id); Game2DSfx.Play("ui_click"); }

        private void Buy()
        {
            var d = Selected; if (d == null) return;
            if (Mgr.TryBuyPanel(d.id)) Game2DSfx.Play("ui_confirm"); else Game2DSfx.Play("ui_error");
        }

        private void Press(int b)
        {
            if (!ButtonOn(b) && b != 4) { Game2DSfx.Play("ui_error"); return; }
            switch (b)
            {
                case 0: Rotate(); break;
                case 1: Place(); break;
                case 2: Remove(Selected?.id); break;
                case 3: Buy(); break;
                case 4: onChallenge?.Invoke(); break;
            }
        }

        private int CellAt(int x, int y)
        {
            for (int c = 0; c < MetaBoard.Cells; c++) { var r = CellRect(c); if (x >= r.x - 1 && y >= r.y - 1 && x < r.xMax + 1 && y < r.yMax + 1) return c; }
            return -1;
        }

        public bool Pointer(Vector2 local, bool inside, bool click)
        {
            int x = Mathf.FloorToInt(local.x), y = Mathf.FloorToInt(local.y);
            bool dirty = false;
            if (_confirmCell >= 0)
            {
                int h = -1; for (int i = 0; i < 2; i++) if (inside && _confirmR[i].Contains(new Vector2Int(x, y))) h = i;
                if (h != _confirmHover) { _confirmHover = h; dirty = true; }
                if (click && h >= 0) { if (h == 0) ConfirmOpen(); else { _confirmCell = -1; Game2DSfx.Play("ui_click"); } return true; }
                return dirty;
            }
            int hb = -1; for (int i = 0; i < 5; i++) if (inside && _btnR[i].Contains(new Vector2Int(x, y))) hb = i;
            int hr = -1; for (int k = 0; k < ShelfRows; k++) if (inside && ShelfRow(k).Contains(new Vector2Int(x, y)) && _top + k < _list.Count) hr = _top + k;
            int ht = -1; { int tx = RX; for (int i = 0; i < TabNames.Length; i++) { int w = Mathf.Max(26, Game2DKit.TextWidth(TabNames[i]) + 10); if (inside && new RectInt(tx, TabY, w, TabH).Contains(new Vector2Int(x, y))) ht = i; tx += w + 2; } }
            if (hb != _hoverBtn || hr != _hoverRow || ht != _hoverTab) { _hoverBtn = hb; _hoverRow = hr; _hoverTab = ht; dirty = true; }
            int cell = inside ? CellAt(x, y) : -1;
            if (cell >= 0 && Selected != null && !Unknown(Selected) && (St?.OwnsPanel(Selected.id) ?? false))
            {   // 影の左上 = ポインタのマス (盤に収まるように寄せる)
                int r0 = cell / 3, c0 = cell % 3;
                if (!_ghostOn || r0 != _ar || c0 != _ac) { _ar = r0; _ac = c0; ClampAnchor(); _ghostOn = true; dirty = true; }
            }
            else if (_ghostOn && inside && cell < 0 && x < RX) { _ghostOn = false; dirty = true; }
            if (inside && Input.GetMouseButtonDown(1)) { Rotate(); return true; }
            float wheel = Input.mouseScrollDelta.y;
            if (inside && wheel != 0f && x >= RX) { int t = Mathf.Clamp(_top - (int)Mathf.Sign(wheel), 0, Mathf.Max(0, _list.Count - ShelfRows)); if (t != _top) { _top = t; dirty = true; } }
            if (!click) return dirty;
            if (hb >= 0) { Press(hb); return true; }
            if (ht >= 0) { _tab = ht; _top = 0; Rebuild(); Game2DSfx.Play("ui_click"); return true; }
            if (hr >= 0) { Select(hr); return true; }
            if (cell >= 0)
            {
                var st = St; if (st == null) return true;
                if ((st.boardOpenMask & 1 << cell) == 0)
                {
                    if (MetaBoard.OpenCost(st.boardOpenMask, cell) >= 0) { _confirmCell = cell; _confirmHover = -1; Game2DSfx.Play("ui_click"); }
                    else Game2DSfx.Play("ui_error");
                    return true;
                }
                if (Selected != null && St.OwnsPanel(Selected.id) && _ghostOn && GhostFits()) { Place(); return true; }
                string pid = PanelAt(cell);
                if (pid != null) { Remove(pid); return true; }
                Game2DSfx.Play("ui_error");
                return true;
            }
            return dirty;
        }

        private void ConfirmOpen()
        {
            int c = _confirmCell; _confirmCell = -1;
            if (c >= 0 && Mgr.TryOpenCell(c)) Game2DSfx.Play("ui_confirm"); else Game2DSfx.Play("ui_error");
        }

        public bool Keys()
        {
            if (_confirmCell >= 0)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { _confirmCell = -1; return true; }
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { ConfirmOpen(); return true; }
                return false;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) { onClose?.Invoke(); return false; }
            if (Input.GetKeyDown(KeyCode.R)) { Rotate(); return true; }
            int dx = Input.GetKeyDown(KeyCode.LeftArrow) ? -1 : Input.GetKeyDown(KeyCode.RightArrow) ? 1 : 0;
            int dy = Input.GetKeyDown(KeyCode.UpArrow) ? -1 : Input.GetKeyDown(KeyCode.DownArrow) ? 1 : 0;
            if (dx != 0 || dy != 0)
            {
                if (Selected != null && !Unknown(Selected) && St.OwnsPanel(Selected.id))
                {   // 影を動かす
                    if (!_ghostOn) { _ghostOn = true; } else { _ar += dy; _ac += dx; }
                    ClampAnchor(); Game2DSfx.Play("ui_hover", 0.7f); return true;
                }
                if (dy != 0 && _list.Count > 0)
                {   // 棚を選ぶ
                    int s = Mathf.Clamp((_sel < 0 ? -1 : _sel) + dy, 0, _list.Count - 1);
                    Select(s); if (_sel < _top) _top = _sel; if (_sel >= _top + ShelfRows) _top = _sel - ShelfRows + 1;
                    return true;
                }
                return false;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                var d = Selected; if (d == null) return false;
                if (!St.OwnsPanel(d.id)) Buy(); else Place();
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Tab)) { _tab = (_tab + 1) % TabNames.Length; _top = 0; Rebuild(); return true; }
            return false;
        }

        // ── 確かめ用 (スクリーンショット) ──

        /// <summary>[確かめ用] 棚の選択と影の場所を置く (EditMode の確かめ・スクリーンショット)。</summary>
        internal void DebugGhost(string id, int rot, int row, int col)
        {
            _tab = 0; Rebuild(); _sel = _list.FindIndex(d => d.id == id); _rot = rot; _ar = row; _ac = col; ClampAnchor(); _ghostOn = _sel >= 0;
        }
        /// <summary>[確かめ用] 開けますか？ の確かめを出す。</summary>
        internal void DebugConfirm(int cell) { _confirmCell = cell; }
    }
}

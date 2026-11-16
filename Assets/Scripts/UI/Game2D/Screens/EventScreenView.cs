using System.Collections.Generic;
using EventSystem;
using UI.Game2D.Battle;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// イベントの画面の絵 (2026-11-16・作者が見本 Captures/event_layout/sheet_v2.png で承認した配置)。 液晶の中身 (480×270 の作図面) をまるごと描く:
    /// 背景の絵 → 左下に本文の板 → 右に選択肢の列。 寸法は <see cref="EventLayout"/>。 液晶へ写すのは <see cref="Game2DEventScreen"/> (液晶の効果が掛かる)。
    /// 中身 (題・頁・選択肢・ボタン) は <see cref="Game2DPhaseScreens"/> が詰める。 <b>見せ方だけ</b> ── 選ぶ・頁を送るは onAction で画面へ返す。
    ///
    /// <para>操作: 箱を押すと<b>選ぶだけ</b> (確定しない・誤って即決しないため)。 確定は操作の行の [選ぶ] (か数字のキー・<see cref="Game2DPhaseScreens"/>)。
    /// 選べない箱も選べる (箱の下に理由を出す) が、 [選ぶ] は失敗の音を鳴らして何もしない。</para>
    /// </summary>
    public sealed class EventScreenView
    {
        // ── 色 (見本 mock2.py の色。 字の色だけ結末の画面と同じ対処で落とした) ──
        //   読みやすさ (結末の画面 Game2DEndingScreen と同じ対処・2026-11-16): 液晶のにじみで字が読みにくくなるので、 液晶の効果は弱めずに字の側で抑える ──
        //   ① 字の色を明るさ 8 割ほどに落とす (見本の白 240,244,245 → 結末の本文と同じ b4c8ce・白に近い字ほどにじみが広がる)
        //   ② 字の周り 1 dot (8 方向) に地より暗い縁取り (Rim) を打ち、 にじみが字の間を埋めないようにする (<see cref="Text"/>)
        private static readonly Color32 Ink = new Color32(0xb4, 0xc8, 0xce, 255), Rim = new Color32(0x02, 0x05, 0x07, 255), Dim = new Color32(140, 156, 164, 255), Edge = new Color32(70, 84, 96, 255),
            PlateFill = new Color32(10, 14, 20, 255), SelFill = new Color32(36, 48, 62, 255), ChipCol = new Color32(196, 206, 150, 255),
            Warn = new Color32(236, 170, 90, 255), Bad = new Color32(214, 96, 86, 255), Off = new Color32(84, 94, 102, 255), NoBackdrop = new Color32(16, 20, 26, 255);

        /// <summary><b>板と箱の濃さ</b> (0〜255・地の色 <see cref="PlateFill"/> を背景の絵へ重ねる割合)。 背景が明るくても字が読める濃さにする
        /// (見本は 220 で、 明るい層の絵の上で字が沈む所があった)。 液晶のブルームと自動露出で見え方が変わるので、 実機を見て調整する ── ここ 1 か所で決める。
        /// 確認中は <see cref="QaPlateAlpha"/> で上書きできる (エディタと開発ビルドだけ)。</summary>
        public const byte PlateAlpha = 236;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>[確認用] 0 以上なら <see cref="PlateAlpha"/> の代わりに使う (<c>UI.Game2D.Battle.QaBattle.EventPlateAlpha</c> が立てる)。</summary>
        public static int QaPlateAlpha = -1;
        private static int Alpha => QaPlateAlpha >= 0 ? Mathf.Clamp(QaPlateAlpha, 0, 255) : PlateAlpha;
#else
        private static int Alpha => PlateAlpha;
#endif

        /// <summary>ターンの札の砂時計 (7×7・'#' を札の色で打つ)。 <b>仮の絵</b> (見本 mock2.py の HOURGLASS)。 アイコンのファイルへ差し替える時は
        /// <see cref="DrawHourglass"/> の 1 か所を直す。</summary>
        private static readonly string[] Hourglass = { "#######", " ##### ", "  ###  ", "   #   ", "  # #  ", " # # # ", "#######" };

        /// <summary><b>イベントの背景の絵の名前</b> (Resources/Game2DBackdrops の名前)。 イベントごとの絵へ差し替える時はここ 1 か所を直す。
        /// 今は仮に、 今いる層の戦闘の背景 (<see cref="BattleBackdrops.ForFloor"/>・見せる層の番号 <c>RunState.currentFloor</c>)。</summary>
        public static string BackdropName(EventDefinition ev)
            => BattleBackdrops.ForFloor(GameLoop.GameManager.Instance?.Run?.currentFloor ?? MapSystem.FreeMove.FloorPlan.FarmFloor);

        public sealed class Choice
        {
            public string name;
            public List<EventChoice.CostChip> chips;
            /// <summary>選べない (名前を暗く・札を赤く)。</summary>
            public bool disabled;
            /// <summary>ターンの札を警告の色に (補足されていて、 使うターンのうちに捕まる見込み)。</summary>
            public bool warnTurns;
            /// <summary>選んでいる時に箱の下に出す説明 (改行で行を分ける・null = 板を出さない)。</summary>
            public string note;
        }

        public sealed class Btn { public string label, action; public bool primary; }

        // ── 中身 (Game2DPhaseScreens が詰める) ──
        public string backdrop, title, pageMark, hint;
        public List<string> lines = new List<string>();
        /// <summary>選択肢 (null か空 = 右の列を出さず背景を見せる)。</summary>
        public List<Choice> choices;
        public readonly List<Btn> buttons = new List<Btn>();
        /// <summary>選んでいる箱。 画面 (Game2DPhaseScreens) がイベントの替わり目で戻す。</summary>
        public int sel;
        /// <summary>ボタンが押された (action, 選んでいる箱)。</summary>
        public System.Action<string, int> onAction;

        private readonly List<RectInt> _boxR = new List<RectInt>(), _btnR = new List<RectInt>();
        private int _hoverBox = -1, _hoverBtn = -1;

        // ── 絵 ──

        /// <summary>480×270 をまるごと描く。 area・glintTick は使わない (呼び手の形に合わせた名残)。</summary>
        public bool Draw(KitPix p, RectInt area, int glintTick)
        {
            int LH = Game2DJpFont.LineH, cell = Game2DJpFont.Cell;
            Backdrop(p);
            // 本文の板
            int px = EventLayout.PlateX, py = EventLayout.PlateY, x0 = px + EventLayout.Pad;
            Shade(p, px, py, EventLayout.PlateW, EventLayout.PlateH, PlateFill, Edge);
            int markW = string.IsNullOrEmpty(pageMark) ? 0 : Game2DJpFont.Width(pageMark);
            if (markW > 0) Text(p, pageMark, x0 + EventLayout.BodyW - markW, py + EventLayout.TitleDy, Dim);
            Text(p, Game2DJpFont.Clip(title ?? "", EventLayout.BodyW - (markW > 0 ? markW + 8 : 0)), x0, py + EventLayout.TitleDy, Ink);
            p.Rect(x0, py + EventLayout.RuleDy, EventLayout.BodyW, 1, Edge);
            if (lines != null)
                for (int i = 0; i < lines.Count && i < EventPaging.TextLines; i++) Text(p, lines[i], x0, py + EventLayout.BodyDy + i * LH, Ink);
            // 操作の行: 左に案内・右にボタン
            int oy = EventLayout.OpY, ty = oy + EventLayout.TextDy(EventLayout.OpH, cell);
            _btnR.Clear();
            int bx = px + EventLayout.PlateW - EventLayout.Pad;
            for (int i = buttons.Count - 1; i >= 0; i--)
            {
                int bw = Game2DJpFont.Width(buttons[i].label) + 2 * EventLayout.BtnPad; bx -= bw;
                _btnR.Insert(0, new RectInt(bx, oy, bw, EventLayout.OpH)); bx -= EventLayout.BtnGap;
            }
            if (!string.IsNullOrEmpty(hint)) Text(p, Game2DJpFont.Clip(hint, bx - x0 - 4), x0, ty, Dim);
            if (choices != null && choices.Count > 0) sel = Mathf.Clamp(sel, 0, choices.Count - 1);
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i]; var r = _btnR[i]; bool off = IsOff(b), hv = i == _hoverBtn && !off;
                Shade(p, r.x, r.y, r.width, r.height, b.primary && !off ? SelFill : PlateFill, hv ? Ink : b.primary && !off ? Dim : Edge);
                Text(p, b.label, r.x + EventLayout.BtnPad, ty, off ? Off : b.primary ? Ink : Dim);
            }
            // 選択肢の列
            _boxR.Clear();
            if (choices != null && choices.Count > 0) Column(p, LH, cell);
            return false;
        }

        private void Column(KitPix p, int LH, int cell)
        {
            int n = choices.Count, maxNote = 0;
            foreach (var c in choices) maxNote = Mathf.Max(maxNote, NoteLinesOf(c).Count);
            int y = EventLayout.ColumnTop(n, EventLayout.NoteH(maxNote, LH)), x = EventLayout.ColX, w = EventLayout.ColW;
            sel = Mathf.Clamp(sel, 0, n - 1);
            for (int i = 0; i < n; i++)
            {
                var c = choices[i]; bool on = i == sel, hv = i == _hoverBox;
                var r = new RectInt(x, y, w, EventLayout.BoxH); _boxR.Add(r);
                Shade(p, r.x, r.y, r.width, r.height, on ? SelFill : PlateFill, on ? Ink : hv ? Dim : Edge);
                int ty = y + EventLayout.TextDy(EventLayout.BoxH, cell);
                Text(p, (i + 1).ToString(), x + EventLayout.NumDx, ty, Dim);
                // 札 (右寄せ・効果の並びの順)
                int cx = x + w - EventLayout.ChipR;
                if (c.chips != null)
                    for (int k = c.chips.Count - 1; k >= 0; k--)
                    {
                        var ch = c.chips[k];
                        cx -= EventLayout.ChipW(ch, Game2DJpFont.Advance);
                        var col = c.disabled ? Bad : (ch.turns && c.warnTurns ? Warn : ChipCol);
                        int tx = cx;
                        if (ch.turns) { DrawHourglass(p, tx, ty + 2, col); tx += EventLayout.HourglassW + EventLayout.HourglassGap; }
                        Text(p, ch.text, tx, ty, col);
                        cx -= EventLayout.ChipGap;
                    }
                int room = EventLayout.NameRoom(EventLayout.ChipsW(c.chips, Game2DJpFont.Advance));
                Text(p, Game2DJpFont.Clip(c.name ?? "", room), x + EventLayout.NameDx, ty, c.disabled ? Off : Ink);
                y += EventLayout.BoxH + EventLayout.BoxGap;
                // 選んでいる箱の下の説明 (補足中の見込み・選べない理由)
                if (on)
                {
                    var nl = NoteLinesOf(c);
                    if (nl.Count > 0)
                    {
                        int nh = EventLayout.NoteH(nl.Count, LH);
                        Shade(p, x, y, w, nh, PlateFill, Edge);
                        for (int k = 0; k < nl.Count; k++) Text(p, nl[k], x + EventLayout.Pad, y + EventLayout.NotePadY + k * LH, c.disabled ? Bad : Warn);
                        y += nh + EventLayout.BoxGap;
                    }
                }
            }
        }

        /// <summary>字を打つ: 先に周り 1 dot (8 方向) を暗い縁取り (<see cref="Rim"/>)、 その上に字 (結末の画面の本文と同じ対処)。</summary>
        private static void Text(KitPix p, string s, int x, int y, Color32 c)
        {
            if (string.IsNullOrEmpty(s)) return;
            Game2DJpFont.Draw((px, py) => p.Rect(px - 1, py - 1, 3, 3, Rim), s, x, y);
            Game2DJpFont.Draw(p, s, x, y, c);
        }

        private static List<string> NoteLinesOf(Choice c)
        {
            var l = string.IsNullOrEmpty(c.note) ? new List<string>() : Game2DJpFont.Lines(c.note, EventLayout.NoteW);
            if (l.Count > EventLayout.NoteLines) l.RemoveRange(EventLayout.NoteLines, l.Count - EventLayout.NoteLines);
            return l;
        }

        /// <summary>砂時計の絵を打つ (左上 x, y・7×7)。 <b>読む所はここだけ</b> (アイコンのファイルへ差し替える時はここを直す)。</summary>
        private static void DrawHourglass(KitPix p, int x, int y, Color32 c)
        {
            for (int j = 0; j < Hourglass.Length; j++)
                for (int i = 0; i < Hourglass[j].Length; i++)
                    if (Hourglass[j][i] == '#') p.Set(x + i, y + j, c);
        }

        /// <summary>背景の絵を 1 画素ずつそのまま写す (加工しない・<see cref="BattleBackdrops.Load"/> と同じ読み方)。 読めなければ暗い地。</summary>
        private void Backdrop(KitPix p)
        {
            var src = BattleBackdrops.Load(backdrop);
            if (src == null || p.w != BattleBackdrops.W || p.h != BattleBackdrops.H) { p.Rect(0, 0, p.w, p.h, NoBackdrop); return; }
            for (int y = 0; y < p.h; y++)
                for (int x = 0; x < p.w; x++)
                {
                    uint v = src[y * p.w + x];      // BattleCanvas.Pack: r | g << 8 | b << 16 (上の行から)
                    p.px[(p.h - 1 - y) * p.w + x] = new Color32((byte)v, (byte)(v >> 8), (byte)(v >> 16), 255);
                }
        }

        /// <summary>板・箱: 地の色を <see cref="PlateAlpha"/> の濃さで重ね、 1 dot の縁を描く。</summary>
        private static void Shade(KitPix p, int x, int y, int w, int h, Color32 fill, Color32 edge)
        {
            int a = Alpha, b = 255 - a;
            for (int j = 1; j < h - 1; j++)
                for (int i = 1; i < w - 1; i++)
                {
                    int X = x + i, Y = y + j; if (X < 0 || Y < 0 || X >= p.w || Y >= p.h) continue;
                    int k = (p.h - 1 - Y) * p.w + X; var c = p.px[k];
                    p.px[k] = new Color32((byte)((c.r * b + fill.r * a) / 255), (byte)((c.g * b + fill.g * a) / 255), (byte)((c.b * b + fill.b * a) / 255), 255);
                }
            p.Rect(x, y, w, 1, edge); p.Rect(x, y + h - 1, w, 1, edge); p.Rect(x, y, 1, h, edge); p.Rect(x + w - 1, y, 1, h, edge);
        }

        // ── 操作 ──

        public bool Pointer(Vector2 local, bool inside, bool click)
        {
            int hb = -1, hk = -1;
            if (inside)
            {
                for (int i = 0; i < _boxR.Count; i++) if (Hit(local, _boxR[i])) { hb = i; break; }
                for (int i = 0; i < _btnR.Count && i < buttons.Count; i++) if (Hit(local, _btnR[i])) { hk = i; break; }
            }
            bool redraw = false;
            if (hb != _hoverBox || hk != _hoverBtn)
            {
                if ((hb >= 0 && hb != _hoverBox) || (hk >= 0 && hk != _hoverBtn)) Game2DSfx.Play("ui_hover", 0.7f);
                _hoverBox = hb; _hoverBtn = hk; redraw = true;
            }
            if (!click) return redraw;
            if (hb >= 0)
            {
                // 箱を押すと選ぶだけ (確定は [選ぶ])
                if (hb != sel) { sel = hb; Game2DSfx.Play("ui_click"); }
                return true;
            }
            if (hk >= 0)
            {
                var b = buttons[hk];
                if (IsOff(b)) { Game2DSfx.Error(); return redraw; }
                Game2DSfx.Play(b.primary ? "ui_confirm" : "ui_click");
                onAction?.Invoke(b.action, sel);
                return true;
            }
            return redraw;
        }

        /// <summary>[選ぶ] は、 選んでいる箱が選べない時は暗く出して押しても何もしない (失敗の音)。</summary>
        private bool IsOff(Btn b) => b.action == "pick" && choices != null && sel >= 0 && sel < choices.Count && choices[sel].disabled;

        private static bool Hit(Vector2 d, RectInt r) => d.x >= r.x && d.x < r.x + r.width && d.y >= r.y && d.y < r.y + r.height;

        /// <summary>替わり目 (別のイベント・導入 → 結果) で、 ホバーと選んでいる箱を戻す。 first = 最初に選んでおく箱。</summary>
        public void ResetSelection(int first) { sel = first; _hoverBox = _hoverBtn = -1; }
    }
}

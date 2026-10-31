using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    public sealed class ModalItem
    {
        public string icon, name, desc, value, tag; public Color32? tone; public bool disabled;
        /// <summary>count / max が付くと [+] [-] で増減できる (整備パネル・設定の音量など)。 max = 0 は数のない行。</summary>
        public int count, max;
    }

    public sealed class ModalBtn { public string label, action; public bool primary; }

    /// <summary>仮置きの画面 1 枚ぶんの中身 (題・タブ・行・ボタン)。</summary>
    public sealed class ScreenDef
    {
        public string id, title, sub, icon, footer;
        public int w = 360, h = 0, cols = 1, budget;       // h = 0 は中身から自動で決める       // budget > 0 の時は、 行の count の合計 / budget を足元に出す (整備パネル)
        public string[] tabs; public int tab;
        public List<List<ModalItem>> tabItems = new List<List<ModalItem>>();
        public List<ModalBtn> buttons = new List<ModalBtn>();
        public string note;                                  // 帯の注意書き (強盗で店の値段が 2 倍 など)
        public List<ModalItem> Items { get { while (tabItems.Count <= tab) tabItems.Add(new List<ModalItem>()); return tabItems[tab]; } }

        /// <summary>日本語のドット字 (<see cref="Game2DJpFont"/>・1 行 16 ドット) で描く。 実データの画面 (<see cref="Game2DPhaseScreens"/>) はこちら。</summary>
        public bool jp;
        /// <summary>題の下に出す本文 (イベントの文・結果の記録など)。 jp のときだけ。</summary>
        public string body;
        /// <summary>本文の最大行数。</summary>
        public int bodyLines = 5;
        /// <summary>ボタンが押された (action, 選んでいる行)。 null なら見本の動き (トーストを出すだけ)。 設定した画面は Esc で閉じない。</summary>
        public System.Action<string, int> onAction;
    }

    /// <summary>
    /// 仮置きの画面 (ショップ・イベント・報酬・休憩・秘宝・整備パネル・エンディング・設定・所持品・敵の詳細・脱出の確認 ほか) を、 画面の中央に出す窓 (2026-10-24)。
    /// 枠・ボタンは Game2DKit の絵 (PixelLab の仮の絵)、 文字は 3×5 のドット字 (英数字だけ)。 中身は <see cref="Game2DScreens"/> が持つ見本の値で、 ゲームの状態にはつないでいない。
    /// 窓はばねで下から入る (行き過ぎは 3 dot 以内)。 操作: 行・タブ・ボタンをクリック / Esc で閉じる。
    /// 試験用のキー (エディタ・開発ビルド): F1 ショップ / F2 イベント / F3 報酬 / F4 休憩 / F5 秘宝 / F6 整備パネル / F7 エンディング / F10 設定 / F11 所持品 /
    /// Shift+F1 補給拠点 / Shift+F2 〈門〉 / Shift+F3 脱出の確認 / Shift+F4 敵の詳細。
    /// </summary>
    public class Game2DModal : MonoBehaviour
    {
        public Game2DScreen screen;
        [SerializeField] private RectTransform _rt;
        private RawImage _img; private Texture2D _tex; private ScreenDef _def; private KitPix _lastPix;
        private Game2DSpring _sp; private bool _open; private int _sel, _hoverRow = -1, _hoverTab = -1, _hoverBtn = -1; private string _toast = ""; private float _toastT;
        private readonly List<RectInt> _rowR = new List<RectInt>(), _tabR = new List<RectInt>(), _btnR = new List<RectInt>();
        private bool _dirty; private int _px, _py;

        public static bool Active { get; private set; }
        private const int Inset = 16, RowH = 22, Gap = 2, FrameB = 12;

        /// <summary>窓の高さ (def.h が 0 の時): 題・注意書き・タブ・行 (一番多いタブに合わせる)・詳細・足元・ボタンが収まる高さ。</summary>
        public static int AutoHeight(ScreenDef d)
        {
            if (d.jp) return AutoHeightJp(d);
            int y = Inset + 28 + 4; if (!string.IsNullOrEmpty(d.note)) y += 15; if (d.tabs != null && d.tabs.Length > 0) y += 18;
            int rows = 1; foreach (var t in d.tabItems) rows = Mathf.Max(rows, (t.Count + Mathf.Max(1, d.cols) - 1) / Mathf.Max(1, d.cols));
            if (d.tabItems.Count == 0) rows = 0;
            return Mathf.Min(266, y + rows * (RowH + Gap) + 4 + 28 + 12 + 22 + Inset + 4);
        }
        public bool IsOpen => _open;

        /// <summary>見た目を作る (builder から 1 度呼ぶ)。 親は 480×270 の格子の枠。</summary>
        public void Build()
        {
            var go = new GameObject("modal", typeof(RectTransform)); go.layer = 5;
            _rt = (RectTransform)go.transform; _rt.SetParent(transform, false);
            _rt.anchorMin = _rt.anchorMax = _rt.pivot = new Vector2(0, 1); _rt.sizeDelta = new Vector2(1, 1);
            var ri = go.AddComponent<RawImage>(); ri.raycastTarget = false; ri.enabled = false;
        }

        private void OnDestroy() { if (_tex != null) Destroy(_tex); Active = false; }

        public void Open(ScreenDef def)
        {
            if (def == null || _rt == null) return;
            if (def.h <= 0) def.h = AutoHeight(def);
            _def = def; _sel = 0; _hoverRow = _hoverTab = _hoverBtn = -1; _toast = ""; _open = true; Active = true; _dirty = true;
            if (_img == null) _img = _rt.GetComponent<RawImage>();
            _tex = Resize(_tex, def.w, def.h); _img.texture = _tex; _img.enabled = true; _rt.sizeDelta = new Vector2(def.w, def.h);
            _px = (Game2DLayout.Width - def.w) / 2; _py = (Game2DLayout.Height - def.h) / 2;
            _sp.Set(0); _sp.Go(1f, 0f);
            Game2DSfx.Play("ui_open");
        }

        public void Close() { if (_open) Game2DSfx.Play("ui_close"); _open = false; _sp.Go(0f, 0f); }

        public void Open(string id) => Open(Game2DScreens.Make(id));

        /// <summary>開いている窓の中身だけを差し替える (入りの動きをやり直さない・選んでいる行は範囲内なら残す)。 開いていなければ開く。</summary>
        public void Replace(ScreenDef def)
        {
            if (!_open || _def == null || def == null || def.w != _def.w) { Open(def); return; }
            if (def.h <= 0) def.h = AutoHeight(def);
            int keepSel = _sel; string keepToast = _toast; float keepT = _toastT;
            _def = def; _tex = Resize(_tex, def.w, def.h); _img.texture = _tex; _rt.sizeDelta = new Vector2(def.w, def.h);
            _px = (Game2DLayout.Width - def.w) / 2; _py = (Game2DLayout.Height - def.h) / 2;
            _sel = Mathf.Clamp(keepSel, 0, Mathf.Max(0, def.Items.Count - 1)); _toast = keepToast; _toastT = keepT; _dirty = true;
        }

        /// <summary>足元に一言出す (買えない・足りない など)。</summary>
        public void ShowToast(string s) => Toast(s);

        private static Texture2D Resize(Texture2D t, int w, int h)
        {
            if (t != null && t.width == w && t.height == h) return t;
            if (t != null) Destroy(t);
            return new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DModal" };
        }

        private void Update()
        {
            if (_rt == null) return;
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime); _sp.Step(dt);
            if (_toastT > 0) { _toastT -= dt; if (_toastT <= 0) { _toast = ""; _dirty = true; } }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugKeys();
#endif
            if (_def != null && _img != null)
            {
                bool visible = _open || !_sp.Settled || _sp.x > 0.002f;
                _img.enabled = visible;
                if (!visible) { Active = false; }
                else
                {
                    if (_open && _sp.x > 0.5f) HandlePointer();
                    if (_dirty) { Redraw(); _dirty = false; }
                    _rt.anchoredPosition = new Vector2(_px, -(_py + Mathf.Round((1f - _sp.Value(60f)) * 60f)));
                }
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void DebugKeys()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            string id = null;
            if (!shift)
            {
                if (Input.GetKeyDown(KeyCode.F1)) id = "shop"; else if (Input.GetKeyDown(KeyCode.F2)) id = "event"; else if (Input.GetKeyDown(KeyCode.F3)) id = "reward";
                else if (Input.GetKeyDown(KeyCode.F4)) id = "rest"; else if (Input.GetKeyDown(KeyCode.F5)) id = "treasure"; else if (Input.GetKeyDown(KeyCode.F6)) id = "meta";
                else if (Input.GetKeyDown(KeyCode.F7)) id = "ending"; else if (Input.GetKeyDown(KeyCode.F10)) id = "settings"; else if (Input.GetKeyDown(KeyCode.F11)) id = "inventory";
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.F1)) id = "depot"; else if (Input.GetKeyDown(KeyCode.F2)) id = "gate"; else if (Input.GetKeyDown(KeyCode.F3)) id = "exit"; else if (Input.GetKeyDown(KeyCode.F4)) id = "enemy";
            }
            if (id != null && (_def == null || _def.onAction == null || !_open)) Open(id);
            if (_open && _def != null && _def.onAction == null && Input.GetKeyDown(KeyCode.Escape)) Close();
        }
#endif

        // ── 入力 ──

        private bool Hit(Vector2 d, RectInt r) => d.x >= _px + r.x && d.x < _px + r.x + r.width && d.y >= _py + r.y && d.y < _py + r.y + r.height;

        private void HandlePointer()
        {
            if (screen == null || !Game2DPointer.TryDot(screen, out var d)) { SetHover(-1, -1, -1); return; }
            int hr = -1, ht = -1, hb = -1;
            for (int i = 0; i < _rowR.Count; i++) if (Hit(d, _rowR[i])) { hr = i; break; }
            for (int i = 0; i < _tabR.Count; i++) if (Hit(d, _tabR[i])) { ht = i; break; }
            for (int i = 0; i < _btnR.Count; i++) if (Hit(d, _btnR[i])) { hb = i; break; }
            SetHover(hr, ht, hb);
            if (!Input.GetMouseButtonDown(0)) return;
            if (hb >= 0) { if (_def.buttons[hb].action != "close") Game2DSfx.Play(_def.buttons[hb].primary ? "ui_confirm" : "ui_click"); Press(_def.buttons[hb]); }
            else if (ht >= 0 && ht != _def.tab) { _def.tab = ht; _sel = 0; _dirty = true; Game2DSfx.Play("ui_tab"); }
            else if (hr >= 0) { _sel = hr; _dirty = true; Game2DSfx.Play("ui_click"); }
        }

        private void SetHover(int r, int t, int b) { if (r != _hoverRow || t != _hoverTab || b != _hoverBtn) { if (r >= 0 || t >= 0 || b >= 0) Game2DSfx.Play("ui_hover", 0.7f); _hoverRow = r; _hoverTab = t; _hoverBtn = b; _dirty = true; } }

        private void Toast(string s) { _toast = s; _toastT = 2.2f; _dirty = true; }

        private void Press(ModalBtn b)
        {
            var items = _def.Items; var it = _sel >= 0 && _sel < items.Count ? items[_sel] : null;
            if (_def.onAction != null) { _def.onAction(b.action, _sel); return; }
            switch (b.action)
            {
                case "close": Close(); return;
                case "inc":
                    if (it != null && it.max > 0) { int total = 0; foreach (var x in items) total += x.count; if (it.count < it.max && (_def.budget <= 0 || total < _def.budget)) it.count++; else Toast(_def.budget > 0 && it.count < it.max ? "POINTS FULL" : "MAX"); _dirty = true; }
                    return;
                case "dec": if (it != null && it.max > 0 && it.count > 0) { it.count--; _dirty = true; } return;
                default: Toast("[" + b.label + "] " + (it != null ? it.name : "") + "  (PLACEHOLDER)"); return;
            }
        }

        // ── 絵 ──

        private void Redraw()
        {
            if (_def.jp) { RedrawJp(); return; }
            var d = _def; var p = new KitPix(d.w, d.h); _lastPix = p;
            _rowR.Clear(); _tabR.Clear(); _btnR.Clear();
            p.NineSlice(Game2DKit.Frame, FrameB, Game2DKit.Well);
            int x0 = Inset, y = Inset, iw = d.w - 2 * Inset;
            // 題
            int tx0 = x0 + (d.icon != null ? 26 : 0);
            if (d.icon != null) p.AnyIcon(d.icon, x0, y - 3, 20, Game2DKit.Amber);
            p.Text(d.title, tx0, y, Game2DKit.Ink, 2);
            if (!string.IsNullOrEmpty(d.sub)) p.Text(d.sub, tx0, y + 13, Game2DKit.Dim);
            y += 28;
            p.Rect(x0, y, iw, 1, Game2DKit.Faint); y += 4;
            // 帯の注意書き
            if (!string.IsNullOrEmpty(d.note)) { if (Game2DKit.Banner != null) p.NineSlice(Game2DKit.Banner, 5, new Color32(0x3a, 0x22, 0x14, 255), x0, y, iw, 12); else p.Box(x0, y, iw, 12, new Color32(0x3a, 0x22, 0x14, 255), Game2DKit.Dark);
                p.Text(d.note, x0 + 4, y + 4, Game2DKit.Red); y += 15; }
            // タブ
            if (d.tabs != null && d.tabs.Length > 0)
            {
                int tx = x0;
                for (int i = 0; i < d.tabs.Length; i++)
                {
                    int tw = Game2DKit.TextWidth(d.tabs[i]) + 14; var r = new RectInt(tx, y, tw, 14); _tabR.Add(r);
                    bool on = i == d.tab, hv = i == _hoverTab;
                    if (Game2DKit.Tab != null) p.NineSlice(Game2DKit.Tab, 5, Game2DKit.Well, r.x, r.y, r.width, r.height); else p.Plate(r.x, r.y, r.width, r.height, on ? 1.5f : hv ? 1.25f : 0.8f);
                    if (on || hv) { var ol = on ? Game2DKit.Amber : Game2DKit.Ink; p.Rect(r.x, r.y + r.height - 1, r.width, 1, ol); }      // 選択・ホバーは下線 (絵は加工しない)
                    p.TextC(d.tabs[i], r, on ? Game2DKit.Ink : Game2DKit.Dim); tx += tw + 3;
                }
                y += 18;
            }
            // 下から順に: ボタン → 足元 → 詳細 の置き場を決める
            int btnY = d.h - Inset - 22, footY = btnY - 12, detailY = footY - 30, rowsBottom = detailY - 4;
            // 行
            var items = d.Items;
            int cols = Mathf.Max(1, d.cols), rw = (iw - (cols - 1) * 4) / cols, rows = Mathf.Max(1, (rowsBottom - y + Gap) / (RowH + Gap));
            for (int i = 0; i < items.Count && i < rows * cols; i++)
            {
                int cx = x0 + (i % cols) * (rw + 4), cy = y + (i / cols) * (RowH + Gap); var it = items[i]; var r = new RectInt(cx, cy, rw, RowH); _rowR.Add(r);
                bool sel = i == _sel, hv = i == _hoverRow;
                if (Game2DKit.Row != null) p.NineSlice(Game2DKit.Row, 6, Game2DKit.Well, r.x, r.y, r.width, r.height); else p.Plate(r.x, r.y, r.width, r.height, it.disabled ? 0.55f : sel ? 1.55f : hv ? 1.3f : 1f);
                if (hv && !sel) { p.Rect(r.x, r.y, r.width, 1, Game2DKit.Ink); }
                if (sel) { p.Rect(r.x, r.y, r.width, 1, Game2DKit.Amber); p.Rect(r.x, r.y + r.height - 1, r.width, 1, Game2DKit.Amber); p.Rect(r.x, r.y, 1, r.height, Game2DKit.Amber); p.Rect(r.x + r.width - 1, r.y, 1, r.height, Game2DKit.Amber); }
                int tx = r.x + 6;
                if (it.icon != null) { p.AnyIcon(it.icon, r.x + 4, r.y + 2, 18, it.disabled ? Game2DKit.Faint : (it.tone ?? Game2DKit.Ink)); tx = r.x + 26; }
                var nameCol = it.disabled ? Game2DKit.Faint : (it.tone ?? Game2DKit.Ink);
                p.Text(it.name ?? "", tx, r.y + 4, nameCol);
                if (!string.IsNullOrEmpty(it.desc)) p.Text(Clip(it.desc, (r.width - (tx - r.x) - 56) / 4), tx, r.y + 13, Game2DKit.Dim);
                string val = it.max > 0 ? it.count + "/" + it.max : it.value;
                if (!string.IsNullOrEmpty(val)) p.TextR(val, r.x + r.width - 6, r.y + 4, Game2DKit.Gold);
                if (!string.IsNullOrEmpty(it.tag)) p.TextR(it.tag, r.x + r.width - 6, r.y + 13, Game2DKit.Dim);
            }
            // 詳細 (選んだ行の説明)
            p.Rect(x0, detailY - 3, iw, 1, Game2DKit.Faint);
            if (_sel >= 0 && _sel < items.Count && !string.IsNullOrEmpty(items[_sel].desc)) p.Wrap(items[_sel].desc, x0 + 2, detailY + 1, iw - 4, Game2DKit.Ink);
            // ボタン
            int bx = d.w - Inset;
            for (int i = d.buttons.Count - 1; i >= 0; i--)
            {
                var b = d.buttons[i]; int bw = Mathf.Max(52, Game2DKit.TextWidth(b.label, 1) + 20); bx -= bw; var r = new RectInt(bx, btnY, bw, 22); bx -= 5; _btnR.Insert(0, r);
            }
            for (int i = 0; i < d.buttons.Count; i++)
            {
                var b = d.buttons[i]; var r = _btnR[i]; bool hv = i == _hoverBtn;
                p.Sprite(b.primary ? Game2DKit.Go : Game2DKit.Btn, r.x, r.y, r.width, r.height);
                if (hv) { p.Rect(r.x, r.y + r.height - 1, r.width, 1, Game2DKit.Amber); }
                p.TextC(b.label, r, Game2DKit.Ink, 1);
            }
            // 足元 (見込み・ポイント・トースト)
            string foot = _toast.Length > 0 ? _toast : d.footer;
            if (d.budget > 0) { int total = 0; foreach (var x in items) total += x.count; foot = "POINTS " + total + "/" + d.budget + (_toast.Length > 0 ? "   " + _toast : ""); }
            if (!string.IsNullOrEmpty(foot)) p.Text(foot, x0 + 2, footY, _toast.Length > 0 ? Game2DKit.Amber : Game2DKit.Dim);
            p.Apply(_tex);
        }

        private static string Clip(string s, int chars) => chars <= 0 ? "" : s.Length <= chars ? s : s.Substring(0, chars);

        // ── 日本語の窓 (実データの画面) ──
        //   1 行 16 ドットの字なので、 英数字の窓より行が高い。 題 → 本文 → 行 → 詳細 (選んだ行の説明 2 行) → 足元 → ボタン。

        private const int JRowH = 20, JGap = 2, JDetail = 2;

        private static bool HasDetail(ScreenDef d) { foreach (var it in d.Items) if (!string.IsNullOrEmpty(it.desc)) return true; return false; }

        private static int AutoHeightJp(ScreenDef d)
        {
            int iw = d.w - 2 * Inset, y = Inset + 20 + 4;
            if (!string.IsNullOrEmpty(d.body)) y += Mathf.Min(d.bodyLines, Game2DJpFont.Lines(d.body, iw - 4).Count) * Game2DJpFont.LineH + 4;
            int cols = Mathf.Max(1, d.cols), rows = (d.Items.Count + cols - 1) / cols;
            y += rows * (JRowH + JGap);
            if (HasDetail(d)) y += JDetail * Game2DJpFont.LineH + 6;
            y += 4 + Game2DJpFont.LineH + 4 + 22 + Inset;
            return Mathf.Min(266, y);
        }

        private void RedrawJp()
        {
            var d = _def; var p = new KitPix(d.w, d.h); _lastPix = p;
            _rowR.Clear(); _tabR.Clear(); _btnR.Clear();
            p.NineSlice(Game2DKit.Frame, FrameB, Game2DKit.Well);
            int x0 = Inset, y = Inset - 4, iw = d.w - 2 * Inset, LH = Game2DJpFont.LineH;
            // 題 (と、 その右に小さく副題)
            int tw = Game2DJpFont.Draw(p, d.title, x0, y, Game2DKit.Ink);
            if (!string.IsNullOrEmpty(d.sub)) Game2DJpFont.Draw(p, Game2DJpFont.Clip(d.sub, iw - tw - 12), x0 + tw + 12, y, Game2DKit.Gold);
            y += 20; p.Rect(x0, y, iw, 1, Game2DKit.Faint); y += 4;
            // 本文
            if (!string.IsNullOrEmpty(d.body)) { int n = Game2DJpFont.Wrap(p, d.body, x0 + 2, y, iw - 4, Game2DKit.Ink, d.bodyLines); y += n * LH + 4; }
            // 下から順に: ボタン → 足元 → 詳細
            int btnY = d.h - Inset - 22, footY = btnY - 4 - LH;
            bool detail = HasDetail(d); int detailY = footY - (detail ? JDetail * LH + 6 : 0), rowsBottom = detailY - 2;
            // 行
            var items = d.Items;
            int cols = Mathf.Max(1, d.cols), rw = (iw - (cols - 1) * 4) / cols, maxRows = Mathf.Max(1, (rowsBottom - y + JGap) / (JRowH + JGap));
            for (int i = 0; i < items.Count && i < maxRows * cols; i++)
            {
                int cx = x0 + (i % cols) * (rw + 4), cy = y + (i / cols) * (JRowH + JGap); var it = items[i]; var r = new RectInt(cx, cy, rw, JRowH); _rowR.Add(r);
                bool sel = i == _sel, hv = i == _hoverRow;
                if (Game2DKit.Row != null) p.NineSlice(Game2DKit.Row, 6, Game2DKit.Well, r.x, r.y, r.width, r.height); else p.Plate(r.x, r.y, r.width, r.height, it.disabled ? 0.55f : sel ? 1.55f : hv ? 1.3f : 1f);
                if (hv && !sel) p.Rect(r.x, r.y, r.width, 1, Game2DKit.Ink);
                if (sel) { p.Rect(r.x, r.y, r.width, 1, Game2DKit.Amber); p.Rect(r.x, r.y + r.height - 1, r.width, 1, Game2DKit.Amber); p.Rect(r.x, r.y, 1, r.height, Game2DKit.Amber); p.Rect(r.x + r.width - 1, r.y, 1, r.height, Game2DKit.Amber); }
                string val = it.max > 0 ? it.count + "/" + it.max : it.value;
                int vw = string.IsNullOrEmpty(val) ? 0 : Game2DJpFont.Width(val);
                if (vw > 0) Game2DJpFont.Draw(p, val, r.x + r.width - 6 - vw, r.y + 2, it.disabled ? Game2DKit.Faint : Game2DKit.Gold);
                var nameCol = it.disabled ? Game2DKit.Faint : (it.tone ?? Game2DKit.Ink);
                Game2DJpFont.Draw(p, Game2DJpFont.Clip(it.name ?? "", r.width - 12 - (vw > 0 ? vw + 8 : 0)), r.x + 6, r.y + 2, nameCol);
            }
            // 詳細 (選んだ行の説明)
            if (detail)
            {
                p.Rect(x0, detailY - 3, iw, 1, Game2DKit.Faint);
                if (_sel >= 0 && _sel < items.Count && !string.IsNullOrEmpty(items[_sel].desc)) Game2DJpFont.Wrap(p, items[_sel].desc, x0 + 2, detailY, iw - 4, Game2DKit.Ink, JDetail);
            }
            // ボタン (右寄せ)
            int bx = d.w - Inset;
            for (int i = d.buttons.Count - 1; i >= 0; i--)
            {
                var b = d.buttons[i]; int bw = Mathf.Max(52, Game2DJpFont.Width(b.label) + 20); bx -= bw; _btnR.Insert(0, new RectInt(bx, btnY, bw, 22)); bx -= 5;
            }
            for (int i = 0; i < d.buttons.Count; i++)
            {
                var b = d.buttons[i]; var r = _btnR[i]; bool hv = i == _hoverBtn;
                p.Sprite(b.primary ? Game2DKit.Go : Game2DKit.Btn, r.x, r.y, r.width, r.height);
                if (hv) p.Rect(r.x, r.y + r.height - 1, r.width, 1, Game2DKit.Amber);
                Game2DJpFont.Draw(p, b.label, r.x + (r.width - Game2DJpFont.Width(b.label)) / 2, r.y + 3, Game2DKit.Ink);
            }
            // 足元 (トーストが優先)
            string foot = _toast.Length > 0 ? _toast : d.footer;
            if (!string.IsNullOrEmpty(foot)) Game2DJpFont.Draw(p, Game2DJpFont.Clip(foot, iw - 4), x0 + 2, footY, _toast.Length > 0 ? Game2DKit.Amber : Game2DKit.Dim);
            p.Apply(_tex);
        }
    }
}

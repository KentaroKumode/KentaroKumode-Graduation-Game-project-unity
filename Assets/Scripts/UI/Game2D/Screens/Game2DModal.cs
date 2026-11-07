using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    public sealed class ModalItem
    {
        public string icon, name, desc, value, tag; public Color32? tone; public bool disabled;
        /// <summary>品のレアリティ (2026-11-08)。 付いていると、 アイコンの枠をレアリティの絵 (<see cref="Game2DKit.RarityFrame"/>) で描く。
        /// 字では出さない ── 枠の絵だけで見分ける。 伝説は光が枠を回る。 null は従来の灰色の枠。</summary>
        public ItemSystem.ItemRarity? rarity;
        /// <summary>count / max が付くと [+] [-] で増減できる (整備パネル・設定の音量など)。 max = 0 は数のない行。</summary>
        public int count, max;
        /// <summary>スライダーの行 (<see cref="ScreenDef.sliders"/>) で、 バーの右に出す今の効果。</summary>
        public string effect;
        /// <summary>スライダーの行で、 段数の欄に count/max の代わりに出す字 (挑戦の「2pt」など)。</summary>
        public string numText;
        /// <summary>分離式ボタンの行 (<see cref="ScreenDef.segments"/>) の列ごとの字。 null の列はボタンを置かない (空けて列をそろえる)。</summary>
        public string[] segs;
        /// <summary>押されている列 (-1 = どれも押されていない)。</summary>
        public int segOn = -1;
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
        /// <summary>行の高さ (jp のときだけ・0 = 既定)。</summary>
        public int rowH;
        /// <summary>行を「名前・段数・スライド式のバー・今の効果」で描く (jp・count/max の行)。 バーを押す・引くと onAction("slide", 行) が呼ばれ、
        /// 目標の段は <see cref="Game2DModal.SlideTarget"/> に入る。</summary>
        public bool sliders;
        /// <summary>スライダーの行の名前の欄の幅 (dot)。 分離式ボタンの行も同じ欄を使う。</summary>
        public int sliderNameW = 64;
        /// <summary>行を「名前・列にそろえた分離式ボタン・今の効果」で描く (jp・<see cref="ModalItem.segs"/> の行)。 ボタンを押すと onAction("seg", 行) が呼ばれ、
        /// 押した列は <see cref="Game2DModal.SegTarget"/> に入る (トグルは呼ぶ側が決める)。</summary>
        public bool segments;
        /// <summary>分離式ボタン 1 つの幅 (dot)。</summary>
        public int segW = 34;
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
        private readonly List<RectInt> _rowR = new List<RectInt>(), _tabR = new List<RectInt>(), _btnR = new List<RectInt>(), _barR = new List<RectInt>();
        private int _dragRow = -1;

        /// <summary>スライダーを押した・引いた時の目標の段 (onAction("slide", 行) の中で読む)。</summary>
        public static int SlideTarget { get; private set; }

        /// <summary>分離式ボタンで押された列 (onAction("seg", 行) の中で読む)。</summary>
        public static int SegTarget { get; private set; }
        private readonly List<RectInt[]> _segR = new List<RectInt[]>();
        private int _hoverSegRow = -1, _hoverSeg = -1;
        private bool _dirty; private int _px, _py;
        /// <summary>伝説の枠を描いている間だけ 8 コマ/秒で描き直す (光を回すため)。 <see cref="_glintTick"/> が光の位置。</summary>
        private bool _glintOn; private int _glintTick;

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
                    if (_glintOn) { int g = (int)(Time.unscaledTime * 8f); if (g != _glintTick) { _glintTick = g; _dirty = true; } }
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
            if (Seg(d)) return;
            if (Slide(d)) return;
            if (!Input.GetMouseButtonDown(0)) return;
            if (hb >= 0) { if (_def.buttons[hb].action != "close") Game2DSfx.Play(_def.buttons[hb].primary ? "ui_confirm" : "ui_click"); Press(_def.buttons[hb]); }
            else if (ht >= 0 && ht != _def.tab) { _def.tab = ht; _sel = 0; _dirty = true; Game2DSfx.Play("ui_tab"); }
            else if (hr >= 0) { _sel = hr; _dirty = true; Game2DSfx.Play("ui_click"); }
        }

        /// <summary>分離式ボタン: ホバーを追い、 押したら onAction("seg", 行)。 押した時は true。</summary>
        private bool Seg(Vector2 d)
        {
            if (_def == null || !_def.segments) return false;
            int hr = -1, hs = -1;
            for (int i = 0; i < _segR.Count && hr < 0; i++)
            {
                var rs = _segR[i]; if (rs == null) continue;
                for (int k = 0; k < rs.Length; k++) if (rs[k].width > 0 && Hit(d, rs[k])) { hr = i; hs = k; break; }
            }
            if (hr != _hoverSegRow || hs != _hoverSeg) { _hoverSegRow = hr; _hoverSeg = hs; _dirty = true; }
            if (hr < 0 || !Input.GetMouseButtonDown(0) || _def.onAction == null) return false;
            _sel = hr; _dirty = true; SegTarget = hs;
            _def.onAction("seg", hr);
            return true;
        }

        /// <summary>スライダー: バーの上で押したら、 離すまでその行の段を指の位置へ合わせる。 扱ったら true。</summary>
        private bool Slide(Vector2 d)
        {
            if (_def == null || !_def.sliders || _def.onAction == null || !Input.GetMouseButton(0)) { _dragRow = -1; return false; }
            if (Input.GetMouseButtonDown(0))
            {
                _dragRow = -1;
                for (int i = 0; i < _barR.Count; i++) { var b = _barR[i]; if (b.width > 0 && Hit(d, new RectInt(b.x - 6, b.y - 4, b.width + 12, b.height + 8))) { _dragRow = i; break; } }
                if (_dragRow < 0) return false;
                if (_sel != _dragRow) { _sel = _dragRow; _dirty = true; Game2DSfx.Play("ui_click"); }
            }
            if (_dragRow < 0) return false;
            if (_dragRow >= _barR.Count || _dragRow >= _def.Items.Count) return true;
            var it = _def.Items[_dragRow]; var r = _barR[_dragRow];
            if (it.max <= 0) return true;
            float fx = d.x - _px - r.x;
            int target = fx < 2 ? 0 : Mathf.Clamp(Mathf.FloorToInt(fx * it.max / r.width) + 1, 0, it.max);      // 左端より左 = 0 段
            if (target != it.count) { SlideTarget = target; _def.onAction("slide", _dragRow); }
            return true;
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
        /// <summary>高さ h の行・ボタンの中で字を上下の真ん中に置く下げ幅 (字の高さ = Game2DJpFont.Cell・漢字は上 11 ドット)。</summary>
        private static int TextDy(int h) => (h - Game2DJpFont.Cell + 1) / 2;
        private static int JBtnDy => TextDy(22);
        private static int RowHOf(ScreenDef d) => d.rowH > 0 ? d.rowH : JRowH;
        private static int GapOf(ScreenDef d) => d.rowH > 0 && d.rowH < JRowH ? 1 : JGap;

        private static bool HasDetail(ScreenDef d) { foreach (var it in d.Items) if (!string.IsNullOrEmpty(it.desc)) return true; return false; }

        private static int AutoHeightJp(ScreenDef d)
        {
            int iw = d.w - 2 * Inset, y = Inset + 20 + 4;
            if (!string.IsNullOrEmpty(d.body)) y += Mathf.Min(d.bodyLines, Game2DJpFont.Lines(d.body, iw - 4).Count) * Game2DJpFont.LineH + 4;
            int cols = Mathf.Max(1, d.cols), rows = (d.Items.Count + cols - 1) / cols;
            y += rows * (RowHOf(d) + GapOf(d));
            if (HasDetail(d)) y += JDetail * Game2DJpFont.LineH + 6;
            y += 4 + Game2DJpFont.LineH + 4 + 22 + Inset;
            return Mathf.Min(266, y);
        }

        /// <summary>分離式ボタンの行: 名前 → 列にそろえたボタン → 今の効果。 押されたボタンは金で塗り、 字を暗くする。 戻り値 = 列ごとのボタンの位置。</summary>
        private static RectInt[] SegRow(KitPix p, ScreenDef d, ModalItem it, RectInt r, int hover)
        {
            int ty = r.y + TextDy(r.height), x = r.x + 6;
            var ink = it.disabled ? Game2DKit.Faint : (it.tone ?? Game2DKit.Ink);
            Game2DJpFont.Draw(p, Game2DJpFont.Clip(it.name ?? "", d.sliderNameW - 4), x, ty, ink); x += d.sliderNameW;
            var rs = new RectInt[it.segs.Length];
            for (int k = 0; k < it.segs.Length; k++)
            {
                var b = new RectInt(x + k * (d.segW + 2), r.y + 1, d.segW, r.height - 2);
                string lab = it.segs[k];
                if (lab == null) { rs[k] = default; continue; }
                rs[k] = b;
                bool on = k == it.segOn, hv = k == hover;
                var fill = on ? Game2DKit.Gold : Game2DKit.Off;
                var edge = on ? Game2DKit.Amber : hv ? Game2DKit.Ink : it.disabled ? Game2DKit.Off : Game2DKit.Faint;
                p.Box(b.x, b.y, b.width, b.height, fill, edge);
                var tc = on ? Game2DKit.Dark : it.disabled ? Game2DKit.Faint : hv ? Game2DKit.Ink : Game2DKit.Dim;
                Game2DJpFont.Draw(p, lab, b.x + (b.width - Game2DJpFont.Width(lab)) / 2, b.y + TextDy(b.height), tc);
            }
            x += it.segs.Length * (d.segW + 2) + 8;
            if (!string.IsNullOrEmpty(it.effect))
                Game2DJpFont.Draw(p, Game2DJpFont.Clip(it.effect, r.x + r.width - 6 - x), x, ty, it.segOn >= 0 ? Game2DKit.Ink : Game2DKit.Dim);
            return rs;
        }

        /// <summary>スライダーの行: 名前 → 段数 (左) → バー → 今の効果 (右)。 戻り値 = バーの位置 (押す判定に使う)。</summary>
        private static RectInt SliderRow(KitPix p, ScreenDef d, ModalItem it, RectInt r, bool sel)
        {
            int ty = r.y + TextDy(r.height), x = r.x + 6;
            var ink = it.disabled ? Game2DKit.Faint : (it.tone ?? Game2DKit.Ink);
            Game2DJpFont.Draw(p, Game2DJpFont.Clip(it.name ?? "", d.sliderNameW - 4), x, ty, ink); x += d.sliderNameW;
            string num = it.numText ?? it.count + "/" + it.max; int numW = Game2DJpFont.Width("10/10");
            Game2DJpFont.Draw(p, num, x + numW - Game2DJpFont.Width(num), ty, it.count > 0 ? Game2DKit.Gold : Game2DKit.Dim); x += numW + 8;
            // バー: 段の数だけ区切った溝。 埋まった段は金、 つまみは今の段の右端
            const int barW = 110, barH = 6; var bar = new RectInt(x, r.y + (r.height - barH) / 2, barW, barH);
            p.Box(bar.x, bar.y, bar.width, bar.height, Game2DKit.Dark, Game2DKit.Faint);
            int fill = (bar.width - 2) * it.count / it.max;
            if (fill > 0) p.Rect(bar.x + 1, bar.y + 1, fill, bar.height - 2, Game2DKit.Gold);
            for (int k = 1; k < it.max; k++) { int tx = bar.x + 1 + (bar.width - 2) * k / it.max; p.Rect(tx, bar.y + 1, 1, bar.height - 2, k < it.count ? new Color32(0x8a, 0x70, 0x22, 255) : Game2DKit.Off); }
            int kx = Mathf.Clamp(bar.x + 1 + fill - 2, bar.x - 1, bar.x + bar.width - 3);
            p.Box(kx, bar.y - 3, 4, bar.height + 6, sel ? Game2DKit.Amber : Game2DKit.Ink, Game2DKit.Dark);
            x += barW + 10;
            if (!string.IsNullOrEmpty(it.effect))
                Game2DJpFont.Draw(p, Game2DJpFont.Clip(it.effect, r.x + r.width - 6 - x), x, ty, it.count > 0 ? Game2DKit.Ink : Game2DKit.Dim);
            return bar;
        }

        private void RedrawJp()
        {
            _glintOn = false;   // 描くたびに数え直す (伝説の枠を描いたら立つ)
            var d = _def; var p = new KitPix(d.w, d.h); _lastPix = p;
            _rowR.Clear(); _tabR.Clear(); _btnR.Clear(); _barR.Clear(); _segR.Clear();
            p.NineSlice(Game2DKit.Frame, FrameB, Game2DKit.Well);
            int x0 = Inset, y = Inset - 4, iw = d.w - 2 * Inset, LH = Game2DJpFont.LineH, RH = RowHOf(d), RG = GapOf(d);
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
            int cols = Mathf.Max(1, d.cols), rw = (iw - (cols - 1) * 4) / cols, maxRows = Mathf.Max(1, (rowsBottom - y + RG) / (RH + RG));
            for (int i = 0; i < items.Count && i < maxRows * cols; i++)
            {
                int cx = x0 + (i % cols) * (rw + 4), cy = y + (i / cols) * (RH + RG); var it = items[i]; var r = new RectInt(cx, cy, rw, RH); _rowR.Add(r);
                bool sel = i == _sel, hv = i == _hoverRow;
                if (Game2DKit.Row != null) p.NineSlice(Game2DKit.Row, 6, Game2DKit.Well, r.x, r.y, r.width, r.height); else p.Plate(r.x, r.y, r.width, r.height, it.disabled ? 0.55f : sel ? 1.55f : hv ? 1.3f : 1f);
                if (hv && !sel) p.Rect(r.x, r.y, r.width, 1, Game2DKit.Ink);
                if (sel) { p.Rect(r.x, r.y, r.width, 1, Game2DKit.Amber); p.Rect(r.x, r.y + r.height - 1, r.width, 1, Game2DKit.Amber); p.Rect(r.x, r.y, 1, r.height, Game2DKit.Amber); p.Rect(r.x + r.width - 1, r.y, 1, r.height, Game2DKit.Amber); }
                if (d.segments && it.segs != null) { _barR.Add(default); _segR.Add(SegRow(p, d, it, r, i == _hoverSegRow ? _hoverSeg : -1)); continue; }
                _segR.Add(null);
                if (d.sliders && it.max > 0) { _barR.Add(SliderRow(p, d, it, r, sel)); continue; }
                _barR.Add(default);
                string val = it.max > 0 ? it.count + "/" + it.max : it.value;
                int vw = string.IsNullOrEmpty(val) ? 0 : Game2DJpFont.Width(val), ty = r.y + TextDy(RH), nx = r.x + 6;
                if (vw > 0) Game2DJpFont.Draw(p, val, r.x + r.width - 6 - vw, ty, it.disabled ? Game2DKit.Faint : Game2DKit.Gold);
                if (it.icon != null)          // アイコンの枠 (icon = 品の id・"" は枠だけ)
                {
                    int ix = r.x + 3, iy = r.y + (RH - 18) / 2;
                    var fr = it.rarity.HasValue ? Game2DKit.RarityFrame(it.rarity.Value) : null;
                    if (fr != null)           // レアリティの枠 (2026-11-08): 地 → アイコン → 枠の絵 (角は透けて行の地が見える)
                    {
                        p.Rect(ix + 1, iy + 1, 16, 16, Game2DKit.Off);
                        var ic = Game2DKit.ItemIcon(it.icon); if (ic != null) p.Sprite(ic, ix + 1, iy + 1, 16, 16);
                        p.Sprite(fr, ix, iy, 18, 18);
                        if (it.rarity.Value == ItemSystem.ItemRarity.LEGENDARY && !it.disabled) { Game2DKit.FrameGlint(p, fr, ix, iy, _glintTick); _glintOn = true; }
                    }
                    else
                    {
                        p.Box(ix, iy, 18, 18, Game2DKit.Off, it.disabled ? Game2DKit.Faint : Game2DKit.Dim);
                        var ic = Game2DKit.ItemIcon(it.icon); if (ic != null) p.Sprite(ic, ix + 1, iy + 1, 16, 16);
                    }
                    nx = ix + 18 + 5;
                }
                var nameCol = it.disabled ? Game2DKit.Faint : (it.tone ?? Game2DKit.Ink);
                Game2DJpFont.Draw(p, Game2DJpFont.Clip(it.name ?? "", r.x + r.width - nx - 6 - (vw > 0 ? vw + 8 : 0)), nx, ty, nameCol);
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
                Game2DJpFont.Draw(p, b.label, r.x + (r.width - Game2DJpFont.Width(b.label)) / 2, r.y + JBtnDy, Game2DKit.Ink);
            }
            // 足元 (トーストが優先)
            string foot = _toast.Length > 0 ? _toast : d.footer;
            if (!string.IsNullOrEmpty(foot)) Game2DJpFont.Draw(p, Game2DJpFont.Clip(foot, iw - 4), x0 + 2, footY, _toast.Length > 0 ? Game2DKit.Amber : Game2DKit.Dim);
            p.Apply(_tex);
        }
    }
}

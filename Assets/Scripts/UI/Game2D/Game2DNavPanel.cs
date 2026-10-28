using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 操作パネル (TRAP / FLARE / RECON・89×124)。 正本は docs/design/map-prototype.html の <c>BTNP</c>。
    ///
    /// <para>素材はどれも 89×124 で、 座標はそのまま重ねるだけ: <b>下 = 3 つのボタン</b> (押している間は <c>_push</c> の絵) → <b>上 = 枠</b> (button_frame・3 行目から) →
    /// その上に<b>残数ランプ</b> (枠の黄緑の 2×2 が 3 つ × 2 段: 上段 = 罠・下段 = 照明。 残数だけ点灯、 左から最大 3)。
    /// 押せない状態 (disabled) は半透明にせず (後ろの地図が透けるため)、 ボタンの画素だけを暗くする。</para>
    /// <para><b>当たり判定</b>は uGUI を使わず、 HTML と同じに<b>画素</b>で判定する: 通常 / 押した絵の不透明な画素 (どれか 1 つ) が当たり。
    /// GOLD の入れ物 (層 4) の上は入れ物が優先で、 ボタンは当たらない。 押して離した時に、 同じボタンの上なら処理を呼ぶ (押した見た目は 110ms 残す)。</para>
    /// <para>GOLD の入れ物が開いて重なる時は (入れ物が上・パネルが下)、 パネル全体を少し暗くして、 入れ物の下端の直下に濃い影を落とす (パネルの画素の上だけ)。</para>
    /// </summary>
    public class Game2DNavPanel : MonoBehaviour
    {
        public const int W = 89, H = 124, FrameTop = 3;

        [Header("絵 (builder が作る)")]
        public RawImage[] normal = new RawImage[3], push = new RawImage[3];
        public Image[] trapLamps = new Image[3], flareLamps = new Image[3];
        public RawImage shade;
        [Header("素材 (isReadable)")]
        public Texture2D[] normalTex = new Texture2D[3], pushTex = new Texture2D[3];
        public Texture2D frameTex;
        public Sprite lampOn, lampOff;
        [Header("配線")]
        public Game2DGoldPot pot;
        public Game2DScreen screen;
        public Game2DMapView view;
        public UnityEvent[] actions = { new UnityEvent(), new UnityEvent(), new UnityEvent() };
        /// <summary>ボタンが見えている (航行) 間だけ当たる。 戦闘では GOLD の入れ物だけが触れる。</summary>
        public bool buttonsActive = true;

        private bool[][] _mask;                         // [ボタン][y*W+x]
        private bool[] _panelMask;                      // 枠 + ボタン (影を落とす範囲)
        private readonly bool[] _disabled = new bool[3];
        private int _pressed = -1;
        private readonly float[] _holdUntil = new float[3];
        private Texture2D _shadeTex; private byte[] _rowAlpha = new byte[H];
        private int _lastTop = int.MinValue; private float _lastShade = -1f;

        /// <summary>パネルの PNG の上端の y (固定): 枠の上端が、 閉じた入れ物の下端に接する。</summary>
        public int PanelY => Game2DGoldPot.H - 42 - FrameTop;

        private void OnDestroy() { if (_shadeTex != null) Destroy(_shadeTex); }

        private void BuildMasks()
        {
            if (_mask != null) return;
            _mask = new bool[3][]; _panelMask = new bool[W * H];
            for (int k = 0; k < 3; k++)
            {
                var a = Game2DGoldCore.Img.From(normalTex[k]); var b = Game2DGoldCore.Img.From(pushTex[k]); _mask[k] = new bool[W * H];
                for (int i = 0; i < W * H; i++) { _mask[k][i] = a.Opaque(i) || b.Opaque(i); if (a.Opaque(i)) _panelMask[i] = true; }
            }
            var f = Game2DGoldCore.Img.From(frameTex);
            for (int i = 0; i < W * H; i++) if (f.Opaque(i)) _panelMask[i] = true;
        }

        /// <summary>画素で当たったボタン (0 罠 / 1 照明 / 2 偵察)。 当たらなければ -1。 GOLD の入れ物の上は入れ物が優先で -1。</summary>
        public int HitButton(float lx, float ly)
        {
            BuildMasks();
            if (!buttonsActive || (pot != null && pot.Hit(lx, ly))) return -1;
            int x = Mathf.FloorToInt(lx), y = Mathf.FloorToInt(ly - PanelY);
            if (x < 0 || y < 0 || x >= W || y >= H) return -1;
            for (int k = 0; k < 3; k++) if (_mask[k][y * W + x]) return k;
            return -1;
        }

        /// <summary>地図のクリックを奪うか: GOLD の入れ物の層 4 か、 ボタンの上。</summary>
        public bool Consumes(float lx, float ly) => (pot != null && pot.Hit(lx, ly)) || HitButton(lx, ly) >= 0;

        public void SetDisabled(int k, bool on) { _disabled[k] = on; }

        /// <summary>残数ランプ: 罠 (上段) と照明 (下段)。 残数だけ点灯 (左から、 最大 3)。</summary>
        public void SetCounts(int trap, int flare)
        {
            for (int i = 0; i < 3; i++)
            {
                if (trapLamps[i] != null) trapLamps[i].sprite = i < trap ? lampOn : lampOff;
                if (flareLamps[i] != null) flareLamps[i].sprite = i < flare ? lampOn : lampOff;
            }
        }

        private void Update()
        {
            if (screen != null) HandlePointer();
            float now = Time.unscaledTime;
            for (int k = 0; k < 3; k++)
            {
                bool p = _pressed == k || now < _holdUntil[k];
                var c = _disabled[k] ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;
                if (normal[k] != null) { normal[k].enabled = !p; normal[k].color = c; }
                if (push[k] != null) { push[k].enabled = p; push[k].color = c; }
            }
            UpdateShade();
        }

        private void HandlePointer()
        {
            bool down = Input.GetMouseButtonDown(0), up = Input.GetMouseButtonUp(0);
            if (!down && !up) return;
            if (Game2DModal.Active) { _pressed = -1; return; }
            if (!Game2DPointer.TryDot(screen, out var d)) { if (up) _pressed = -1; return; }
            if (down)
            {
                int k = HitButton(d.x, d.y);
                if (k >= 0) { _pressed = k; Game2DSfx.Play("nav_press", _disabled[k] ? 0.4f : 1f); }                                  // 押せない状態でも押した見た目にはなる (処理だけ呼ばれない)
            }
            if (up)
            {
                int was = _pressed; _pressed = -1;
                int k = HitButton(d.x, d.y);
                if (was >= 0 && was == k) { _holdUntil[k] = Time.unscaledTime + 0.11f; if (!_disabled[k]) actions[k].Invoke(); return; }
                // クリック: GOLD の入れ物の層 4 を触ると開く / 閉じる (地図をドラッグした時は除く)
                if (pot != null && pot.Hit(d.x, d.y) && (view == null || !view.DragMoved)) pot.Toggle();
            }
        }

        /// <summary>GOLD の入れ物の下に入る影: 入れ物が開いて重なる時、 パネル全体を少し暗くし、 入れ物の下端の直下に濃い影を落とす。 パネルの画素の上だけ。</summary>
        private void UpdateShade()
        {
            if (shade == null || pot == null) return;
            BuildMasks();
            float open = pot.Open01, sh = 0.26f * Mathf.Clamp01(open * 1.4f);
            int top = pot.Bottom - PanelY;
            int q = Mathf.RoundToInt(sh * 64f);
            if (q == Mathf.RoundToInt(_lastShade * 64f) && top == _lastTop && _shadeTex != null) return;
            _lastShade = sh; _lastTop = top;
            if (_shadeTex == null)
            {
                _shadeTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DNavPanelShade" };
                shade.texture = _shadeTex;
            }
            float g0 = 0.5f * Mathf.Clamp01(open * 1.6f);
            var buf = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float a = sh;                                                   // 全体の暗さ
                if (y >= Mathf.Max(0, top) && y < top + 18) { float g = g0 * (1f - (y - top) / 18f); a = 1f - (1f - a) * (1f - g); }   // 入れ物の下端の直下の影
                byte ab = (byte)Mathf.Round(Mathf.Clamp01(a) * 255f);
                if (ab == 0) continue;
                for (int x = 0; x < W; x++) if (_panelMask[y * W + x]) buf[(H - 1 - y) * W + x] = new Color32(0, 0, 0, ab);
            }
            _shadeTex.SetPixels32(buf); _shadeTex.Apply(false);
            shade.enabled = sh > 0.005f;
        }
    }

    /// <summary>マウスの位置 → GUI の dot (480×270・左上が原点・y 下向き)。</summary>
    public static class Game2DPointer
    {
        public static bool TryDot(Game2DScreen screen, out Vector2 dot)
        {
            dot = default;
            if (screen == null) return false;
            var mp = Input.mousePosition;
            if (mp.x < 0 || mp.y < 0 || mp.x > Screen.width || mp.y > Screen.height) return false;
            float ppd = screen.PixelsPerGuiDot;
            dot = new Vector2((mp.x - Screen.width / 2f) / ppd + Game2DLayout.Width / 2f, Game2DLayout.Height / 2f - (mp.y - Screen.height / 2f) / ppd);
            return dot.x >= 0 && dot.y >= 0 && dot.x <= Game2DLayout.Width && dot.y <= Game2DLayout.Height;
        }
    }
}

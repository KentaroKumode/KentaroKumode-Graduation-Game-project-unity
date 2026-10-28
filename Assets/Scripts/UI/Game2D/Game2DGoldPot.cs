using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Core = UI.Game2D.Game2DGoldCore;

namespace UI.Game2D
{
    /// <summary>
    /// GOLD の入れ物 (76×96・6 層・2026-10-11)。 正本は docs/design/map-prototype.html の <c>GOLDPOT</c> (設計は docs/design/gui-gold-pot.html)。
    ///
    /// <para><b>状態</b>: 初期は<b>閉じている</b> (層 5・4・3・1 が 42 dot 上がり、 層 2 = 上のフレームだけ動かない)。 手前のパネル (層 4) を触る / G キーで開閉する。
    /// 閉じる = 一定の加速で全閉 (ばねなし) / 開く = ばね (全開の壁で跳ね返る)。 重ね順は 下から 1・3・5・2・4。 層 3 (金貨) は層 5 の横の範囲でだけ見える。</para>
    /// <para><b>金貨 (層 3)</b> の高さが GOLD を表す。 層 4 の窓には 7 セグメントの数字 (橙) が光る (位置は層 6 のマーカーから読む)。 開き始め・全開の壁で数字が接触不良のようにちらつく。
    /// 金貨は入れ物が動く時にばねで少し揺れる (縦 ±4・横 ±3 dot)。</para>
    /// <para><b>金貨の粒</b>: GOLD が増えるとコインが上から降り (山に着くと消える・山は数字より少し遅れて盛り上がる)、 開いた状態で金貨がフレームにかぶっている時に全閉でぶつかると、
    /// かぶった領域がコインの形の粒になって弾ける (その領域は層 3 から消え、 開き始めで元に戻る)。 戦闘の勝利などで GOLD が増えた時は、 閉じていれば自動で開く →
    /// 開き切ったら GOLD が入る → 降り終わって少し置いてから、 元が閉じていれば閉じる (<see cref="Payout"/>)。</para>
    /// </summary>
    public class Game2DGoldPot : MonoBehaviour
    {
        [Header("層 (下から 1・3・5・2・4 の順に描く)")]
        public RectTransform layer1, layer2, layer4, layer5;
        [Tooltip("層 3 を載せる RectMask2D (層 5 の横の範囲)")] public RectTransform layer3Clip;
        [Tooltip("層 3 (金貨)。 粒になった所を消すので、 実行時に複製したテクスチャを映す")] public RawImage layer3;
        [Tooltip("層 4 の子。 数字の光を重ねる")] public RawImage display;
        [Tooltip("コインの粒を描く (最前面)")] public RawImage particles;
        [Header("素材 (isReadable)")]
        public Texture2D tex2, tex3, tex4, tex5, marker, coinSprite;

        public const int W = 76, H = 96;
        private const int Drop = 48, Vis = Drop - 7, Hood = 16, Close = 55 - Hood + 3, DisplayFull = 200, DropTop = 24;
        private const float CloseAcc = 60f, SpringK = 140f, SpringC = 6f, Bounce = 0.6f;
        private static readonly Color32 Orange = new Color32(255, 132, 28, 255);
        private const float BloomMax = 0.2f;
        private static readonly string[] Seg7 = { "abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };

        // BURST (HTML の値)
        private const float Spacing = 1.0f, SpreadX = 1.2f, JitterX = 24f, UpMin = 28f, UpMax = 70f, Gravity = 150f, LifeMin = 0.9f, LifeMax = 1.6f, FadeFrac = 0.35f,
                            MinWidth = 0.18f, RadiusJitter = 0f, RadiusScale = 1.3f, SpinHzMin = 0.8f, SpinHzMax = 3.4f, TiltVelMax = 2.5f, ContrastMin = 0.85f, ContrastMax = 1.6f,
                            GlintPower = 4f, Thickness = 0.18f, Saturation = 1.35f, DropSpread = 0.5f, DropRatio = 3f, DropDelayMax = 0.5f, DropVx = 8f, DropVyMin = 20f, DropVyMax = 60f, PileDelay = 0.6f;
        private const int DropFull = 10;
        private const uint Seed = 20261011;

        // 描画領域 (粒は入れ物の外へも出る): 入れ物の座標 (0,0) を基準に、 x -30..126・y -50..126
        private const int PX0 = -30, PY0 = -50, PW = 156, PH = 176;

        // ── 状態 (HTML の st) ──
        private float _shown = -1f, _prev = -1f, _closeT = 1f, _closeV, _wait, _gx, _gy, _gvx, _gvy, _flickT;
        private bool _goal = true;                              // true = 閉じる
        private int _target, _pending;
        private float[] _flick;
        private Pay _pay;
        private class Pay { public string phase; public float t; public bool wasOpen; }
        private int _lastGold = -1;

        private Core.Rng _rng = new Core.Rng(Seed);
        private Core.Img _i2, _i3, _i4, _i5, _coinImg;
        private Core.Coin? _coin; private Vector3 _coinMean, _coinGold;
        private bool[] _g3, _vis2, _hit4; private int[] _colTop; private int _xMin = 76, _xMax = -1, _x5a = W, _x5b = -1;
        private byte[] _eat;                                    // 粒になった画素 (1 = 層 3 から消す)
        private Texture2D _l3Tex; private Color32[] _l3Base;
        private bool _eatDirty;

        private readonly List<Particle> _parts = new List<Particle>();
        private struct Particle
        {
            public bool drop; public float delay, x, y, vx, vy, scale, gl, life, max, ph, spin, th, thv;
        }
        private Texture2D _partTex; private Color32[] _partBuf; private bool _partsDrawn;

        // 数字
        private List<Vector2Int>[][] _segs; private readonly List<Vector2Int> _always = new List<Vector2Int>(); private int _digitCount;
        private Texture2D _dispTex; private int _drawnValue = -1; private float _drawnLevel = -1f;

        // ── 公開 ──
        public bool IsClosed => _goal;
        /// <summary>いまの見た目の下端 (閉じると上がる)。 操作パネルの影の位置に使う。</summary>
        public int Bottom => H - Mathf.RoundToInt(Close * _closeT);
        public int ClosedBottom => H - Close;
        /// <summary>0 = 閉じている … 1 = 全開。</summary>
        public float Open01 => 1f - _closeT;

        private void OnDestroy() { foreach (var t in new[] { _l3Tex, _partTex, _dispTex }) if (t != null) Destroy(t); }

        /// <summary>実際の GOLD (毎フレーム入れる)。 増えた分の「入る演出」は <see cref="Payout"/>。</summary>
        public void SetGold(int gold)
        {
            if (_lastGold >= 0 && gold > _lastGold) Payout(gold - _lastGold);
            _lastGold = gold; _target = Mathf.Max(0, gold);
        }

        /// <summary>手前のパネルを触った / G キー。 開いたなら false を返す (HTML の toggle は「閉じたか」を返す)。</summary>
        public bool Toggle()
        {
            if (_goal) { ClearEat(); Jolt(-55f, 14f); StartFlicker(1f); }
            _goal = !_goal; Game2DSfx.Play(_goal ? "pot_close" : "pot_open"); return _goal;
        }

        /// <summary>入れ物の層 4 (手前のパネル) を触ったか: いまの位置 (閉じ具合で上がった分) の不透明な画素の上か。 lx, ly = GUI の dot。</summary>
        public bool Hit(float lx, float ly)
        {
            if (_hit4 == null) return false;
            int x = Mathf.FloorToInt(lx), y = Mathf.FloorToInt(ly - (0 - Mathf.RoundToInt(Close * _closeT)));
            return x >= 0 && y >= 0 && x < W && y < H && _hit4[y * W + x];
        }

        /// <summary>GOLD が増えた時の振り込み: 閉じていれば入れ物が開く → 開き切ったら GOLD が入る → 降り終わって少し置いてから、 元が閉じていれば閉じる。
        /// 表示だけを、 入れ物が開くまで pending 分遅らせる。 途中で手で閉じたら、 すぐ入れて終わる。</summary>
        public void Payout(int delta)
        {
            if (delta <= 0) return;
            Game2DSfx.DiceCoin(delta);
            _pending += delta;
            if (_pay == null) { _pay = new Pay { phase = "open", t = 0, wasOpen = !_goal }; if (_goal) Toggle(); }
            else if (_pay.phase != "open") { _pending = 0; _pay.phase = "fall"; _pay.t = 0; }
        }

        private void PayStep(float dt)
        {
            var q = _pay; if (q == null) return; q.t += dt;
            if (q.phase == "open")
            {
                if (_goal) { _pending = 0; _pay = null; return; }
                if (q.t >= 0.45f && _closeT < 0.3f) { _pending = 0; q.phase = "fall"; q.t = 0; }
            }
            else if (q.phase == "fall")
            {
                bool anyDrop = false; foreach (var p in _parts) if (p.drop) { anyDrop = true; break; }
                if ((q.t > PileDelay + 0.9f && !anyDrop) || q.t > 3.5f) { q.phase = "hold"; q.t = 0; }
            }
            else if (q.phase == "hold" && q.t > 0.5f)
            {
                if (!q.wasOpen && !_goal) Toggle();
                _pay = null;
            }
        }

        // =====================================================================
        //  初期化 (素材の読み取り)
        // =====================================================================

        private void Init()
        {
            if (_i3 != null) return;
            _i2 = Core.Img.From(tex2); _i3 = Core.Img.From(tex3); _i4 = Core.Img.From(tex4); _i5 = Core.Img.From(tex5);
            _coinImg = coinSprite != null ? Core.Img.From(coinSprite) : null;
            if (_coinImg != null) { _coinMean = Core.SpriteMean(_coinImg); _coinGold = Core.SpriteGold(_coinImg); }
            _g3 = new bool[W * H]; _vis2 = new bool[W * H]; _hit4 = new bool[W * H]; _colTop = new int[W];
            for (int i = 0; i < W * H; i++)
            {
                _g3[i] = _i3.Opaque(i);
                _vis2[i] = _i2.Opaque(i) && _i2.Luma(i) > 8;          // 層 2 の不透明で暗くない画素 (黒い縁取りは見えないので数えない)
                _hit4[i] = _i4.Opaque(i);
            }
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (_i5.Opaque(y * W + x)) { _x5a = Mathf.Min(_x5a, x); _x5b = Mathf.Max(_x5b, x); }
            for (int x = 0; x < W; x++)
            {
                _colTop[x] = -1;
                for (int y = 0; y < H; y++) if (_g3[y * W + x]) { _colTop[x] = y; _xMin = Mathf.Min(_xMin, x); _xMax = Mathf.Max(_xMax, x); break; }
            }
            _eat = new byte[W * H];
            _l3Base = tex3.GetPixels32();
            _l3Tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DGoldLayer3" };
            _l3Tex.SetPixels32(_l3Base); _l3Tex.Apply(false);
            if (layer3 != null) layer3.texture = _l3Tex;
            if (layer3Clip != null) layer3Clip.sizeDelta = new Vector2(_x5b - _x5a + 1, H);       // 横 = 層 5 の幅 / 縦 = 入れ物の高さ (HTML は 76×96 の作業キャンバスに描くので、 山を下げても入れ物の下へははみ出さない)
            _partTex = new Texture2D(PW, PH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DGoldParticles" };
            _partBuf = new Color32[PW * PH];
            _partTex.SetPixels32(_partBuf); _partTex.Apply(false);              // 作りたての Texture2D は不透明の白なので、 すぐ透明で塗る
            if (particles != null) { particles.enabled = true; particles.texture = _partTex; var rt = particles.rectTransform; rt.anchoredPosition = new Vector2(PX0, -PY0); rt.sizeDelta = new Vector2(PW, PH); }
            ParseMarker();
        }

        /// <summary>再生していない時 (シーンビュー・ビルダーの直後) の姿勢: 閉じた状態で、 GOLD 0 の位置に層 3 を置く。
        /// 実行時は <see cref="Update"/> が毎フレーム置き直すので、 ここは見た目の確認用 (層 3 の横の範囲 = 層 5 の幅の枠も入れる)。</summary>
        public void ApplyEditorPose()
        {
            if (tex3 == null || tex5 == null) return;
            var i5 = Core.Img.From(tex5); int a = W, b = -1;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (i5.Opaque(y * W + x)) { a = Mathf.Min(a, x); b = Mathf.Max(b, x); }
            if (layer3Clip != null) { layer3Clip.anchoredPosition = new Vector2(a, 0f); layer3Clip.sizeDelta = new Vector2(b - a + 1, H - Close); }
            int dy = Mathf.Max(0, OffsetFor(0f, DisplayFull) - Close);
            Place(layer1, 0, Close); Place(layer5, 0, Close); Place(layer4, 0, Close); Place(layer2, 0, 0);
            if (layer3 != null) layer3.rectTransform.anchoredPosition = new Vector2(-a, -dy);
        }

        private static int OffsetFor(float v, float full)
        {
            float f = Mathf.Min(1f, v / full);
            return v < 1f ? Mathf.RoundToInt(Drop - 7 * v) : Mathf.RoundToInt(Vis * (1f - f));
        }

        // =====================================================================
        //  毎フレーム
        // =====================================================================

        private void Update()
        {
            if (tex3 == null) return;
            Init();
            float dt = Mathf.Min(0.25f, Time.unscaledDeltaTime);
            if (Input.GetKeyDown(KeyCode.G)) Toggle();
            PayStep(dt);
            float full = DisplayFull;
            float target = Mathf.Max(0, _target - _pending);            // 振り込み待ちの分は、 入れ物が開くまで表示に入れない
            if (_shown < 0) { _shown = target; _prev = target; }
            if (target > _prev && _closeT < 0.5f) { DropCoins((int)(target - _prev)); _wait = PileDelay; }     // GOLD が増えたら、 増えた分のコインを上から降らせる (閉じている間は降らせない)
            _prev = target;
            if (_wait > 0 && target > _shown) _wait -= dt;              // 山と数字は、 コインが山に届くまで少し待ってから増える
            else _shown += (target - _shown) * (1f - Mathf.Exp(-dt / 0.25f));
            if (Mathf.Abs(target - _shown) < 0.05f) _shown = target;

            if (_goal)                                                  // 閉じる: ばねなし。 一定の加速で閉まり、 全閉で止まる
            {
                if (_closeT < 1f)
                {
                    _closeV = Mathf.Max(0f, _closeV) + CloseAcc * dt; _closeT += _closeV * dt;
                    if (_closeT >= 1f)
                    {
                        Jolt(3f * _closeV, 2f * _closeV);
                        if (_closeV > 0.3f && OverlapsHood(OffsetFor(_shown, full))) Burst(_shown, full, Close);
                        _closeT = 1f; _closeV = 0f;
                    }
                }
            }
            else                                                        // 開く: ばね (全開の壁にぶつかって跳ね返る)
            {
                float h = dt / 4f; const float pull = -0.08f;
                for (int i = 0; i < 4; i++)
                {
                    _closeV += (SpringK * (pull - _closeT) - SpringC * _closeV) * h; _closeT += _closeV * h;
                    if (_closeT < 0f)
                    {
                        _closeT = 0f; Jolt(7f * Mathf.Abs(_closeV), 5f * Mathf.Abs(_closeV));
                        if (Mathf.Abs(_closeV) > 0.8f) StartFlicker(0.7f);
                        _closeV = -_closeV * Bounce;
                    }
                    if ((_closeT == 1f || _closeT == 0f) && Mathf.Abs(_closeV) < 0.12f) _closeV = 0f;
                }
            }
            // 金貨の慣性: 入れ物が動く時に、 山が張り付いたままだと不自然なので、 ばねで少しだけ揺らす
            {
                const float GK = 320f, GC = 11f; float hh = dt / 3f;
                for (int i = 0; i < 3; i++)
                {
                    _gvy += (-GK * _gy - GC * _gvy) * hh; _gy += _gvy * hh; _gvx += (-GK * _gx - GC * _gvx) * hh; _gx += _gvx * hh;
                }
                _gy = Mathf.Clamp(_gy, -4f, 4f); _gx = Mathf.Clamp(_gx, -3f, 3f);
            }
            if (_flick != null) _flickT += dt;

            int up = Mathf.RoundToInt(Close * _closeT), dyAbs = Mathf.Max(0, OffsetFor(_shown, full) - up), winBottom = 55 - up - 1;
            _landFn = x => { int c = _colTop[Mathf.Clamp(Mathf.RoundToInt(x), 0, W - 1)]; return Mathf.Min(winBottom, (c < 0 ? H : c) + dyAbs); };

            // 層の配置 (UI は y 下向きが負)
            Place(layer1, 0, up); Place(layer5, 0, up); Place(layer4, 0, up); Place(layer2, 0, 0);
            if (layer3 != null)
            {
                // 作業キャンバス (76×96) は金貨の揺れ (gx, gy) と一緒に動く。 横の枠 (層 5 の幅) だけは動かない
                int gxr = Mathf.RoundToInt(_gx), gyr = Mathf.RoundToInt(_gy);
                // 縦の枠は入れ物の下端 (閉じると上がる: 96 − up) まで。 閉じている時に、 山が入れ物の下 (パネルの上) へはみ出さない
                if (layer3Clip != null) { layer3Clip.anchoredPosition = new Vector2(_x5a, -gyr); layer3Clip.sizeDelta = new Vector2(_x5b - _x5a + 1, Mathf.Max(0, H - up - gyr)); }     // 下端は揺れ (gy) に連れて動かさない = 入れ物の下端 (96 − up) に固定
                layer3.rectTransform.anchoredPosition = new Vector2(gxr - _x5a, -dyAbs);           // 枠の左上を原点にした位置
            }
            if (_eatDirty) { PushEat(); _eatDirty = false; }
            DrawDisplay();
            StepParts(dt);
            DrawParts();
        }

        private System.Func<float, float> _landFn = x => H;

        private static void Place(RectTransform r, int x, int up) { if (r != null) r.anchoredPosition = new Vector2(x, up); }

        // =====================================================================
        //  金貨の粒
        // =====================================================================

        private bool EnsureCoin()
        {
            if (_coin != null) return true;
            if (_coinImg == null) return false;
            _coin = Core.EstimateCoin(_i3);          // コイン 1 枚の大きさは層 3 から推定 (burst と共通)
            return true;
        }

        private static int DropCount(int delta) => delta <= DropFull ? delta : DropFull + Mathf.FloorToInt((delta - DropFull) / DropRatio);

        private void DropCoins(int delta)
        {
            if (!EnsureCoin()) return;
            int n = DropCount(delta); float rBase = _coin.Value.radius * RadiusScale; var R = _rng;
            for (int i = 0; i < n; i++)
            {
                float rr = rBase * (1f + (R.Next() - 0.5f) * 2f * RadiusJitter);
                var p = new Particle { drop = true };
                p.delay = R.Next() * DropDelayMax;
                p.x = (_xMin + _xMax) / 2f + (R.Next() - 0.5f) * (_xMax - _xMin) * DropSpread;
                p.y = -DropTop + R.Next() * 6f;
                p.vx = (R.Next() - 0.5f) * 2f * DropVx;
                p.vy = DropVyMin + R.Next() * (DropVyMax - DropVyMin);
                p.scale = 2f * rr / Mathf.Max(_coinImg.w, _coinImg.h);
                p.gl = R.Next() * Mathf.PI * 2f; p.life = 0f; p.max = float.PositiveInfinity;
                p.ph = R.Next() * Mathf.PI * 2f;
                p.spin = (SpinHzMin + R.Next() * (SpinHzMax - SpinHzMin)) * (R.Next() < 0.5f ? -1f : 1f);
                p.th = R.Next() * Mathf.PI; p.thv = (R.Next() - 0.5f) * 2f * TiltVelMax;
                _parts.Add(p);
            }
        }

        /// <summary>層 3 (金貨) が層 2 (上のフレーム) の「見える」画素に重なるか (dy だけ下げた位置で)。</summary>
        private readonly Dictionary<int, bool> _overlapCache = new Dictionary<int, bool>();
        private bool OverlapsHood(int dy)
        {
            if (_overlapCache.TryGetValue(dy, out var c)) return c;
            bool hit = false;
            for (int y = 0; y < H && !hit; y++)
            {
                int yy = y + dy; if (yy < 0 || yy >= H) continue;
                for (int x = 0; x < W; x++) if (_g3[y * W + x] && _vis2[yy * W + x]) { hit = true; break; }
            }
            _overlapCache[dy] = hit; return hit;
        }

        /// <summary>全閉にぶつかった瞬間: 層 3 のうち、 フレームの見える画素に重なる領域を、 コインの形の粒にする。</summary>
        private void Burst(float v, float full, int up)
        {
            int dy = Mathf.Max(0, OffsetFor(v, full) - up);
            if (!EnsureCoin()) return;
            var region = new bool[W * H];
            for (int y = 0; y < H; y++)
            {
                int yy = y + dy; if (yy < 0 || yy >= H) continue;
                for (int x = 0; x < W; x++) { int i = y * W + x; if (_g3[i] && _vis2[yy * W + x] && _eat[i] == 0) region[i] = true; }
            }
            float rBase = _coin.Value.radius * RadiusScale; var R = _rng;
            var pts = Core.PickPoints(region, W, H, (rBase * 2f + 1f) * Spacing, R);
            foreach (var pt in pts)
            {
                float rr = rBase * (1f + (R.Next() - 0.5f) * 2f * RadiusJitter);
                var p = new Particle { drop = false };
                p.x = pt.x; p.y = pt.y + dy;
                p.vx = (pt.x - W / 2f) * SpreadX + (R.Next() - 0.5f) * JitterX;
                p.vy = -(UpMin + R.Next() * (UpMax - UpMin));
                p.scale = 2f * rr / Mathf.Max(_coinImg.w, _coinImg.h);
                p.gl = R.Next() * Mathf.PI * 2f; p.life = 0f;
                p.max = LifeMin + R.Next() * (LifeMax - LifeMin);
                p.ph = R.Next() * Mathf.PI * 2f;
                p.spin = (SpinHzMin + R.Next() * (SpinHzMax - SpinHzMin)) * (R.Next() < 0.5f ? -1f : 1f);
                p.th = R.Next() * Mathf.PI; p.thv = (R.Next() - 0.5f) * 2f * TiltVelMax;
                _parts.Add(p);
            }
            for (int i = 0; i < W * H; i++) if (region[i]) _eat[i] = 1;
            _eatDirty = true;
        }

        private void ClearEat() { System.Array.Clear(_eat, 0, _eat.Length); _eatDirty = true; }

        /// <summary>粒になった画素を層 3 のテクスチャから消す (destination-out)。</summary>
        private void PushEat()
        {
            var buf = (Color32[])_l3Base.Clone();
            for (int i = 0; i < W * H; i++)
                if (_eat[i] != 0) { int y = i / W, x = i % W; buf[(H - 1 - y) * W + x] = new Color32(0, 0, 0, 0); }
            _l3Tex.SetPixels32(buf); _l3Tex.Apply(false);
        }

        private void StepParts(float dt)
        {
            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                var p = _parts[i];
                if (p.drop)
                {
                    if (p.delay > 0) { p.delay -= dt; _parts[i] = p; continue; }
                    p.life += dt; p.vy += Gravity * dt; p.x += p.vx * dt; p.y += p.vy * dt;
                    if (p.y < _landFn(p.x)) _parts[i] = p; else _parts.RemoveAt(i);          // 山に着いたら消える
                    continue;
                }
                p.life += dt; p.vy += Gravity * dt; p.x += p.vx * dt; p.y += p.vy * dt;
                if (p.life < p.max) _parts[i] = p; else _parts.RemoveAt(i);
            }
        }

        private void DrawParts()
        {
            if (particles == null || _coinImg == null) return;
            if (_parts.Count == 0) { if (_partsDrawn) { System.Array.Clear(_partBuf, 0, _partBuf.Length); _partTex.SetPixels32(_partBuf); _partTex.Apply(false); _partsDrawn = false; } return; }
            System.Array.Clear(_partBuf, 0, _partBuf.Length);
            foreach (var p in _parts)
            {
                if (p.drop && p.delay > 0) continue;
                float a = p.drop ? 1f : Mathf.Min(1f, (p.max - p.life) / (p.max * FadeFrac));
                float ph = p.ph + p.life * p.spin * Mathf.PI * 2f, c = Mathf.Cos(ph), wd = Mathf.Max(MinWidth, Mathf.Abs(c));
                // 反射: 面が光源へ向く瞬間 (半周ごとに 1 回・コインごとに位相がずれる) だけコントラストを大きく上げて、 きらっと光らせる
                float glint = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2f * ph + p.gl), GlintPower), k = ContrastMin + (ContrastMax - ContrastMin) * glint;
                float lean = Mathf.Sin(ph) * (c < 0 ? -1f : 1f), thick = Thickness * Mathf.Max(_coinImg.w, _coinImg.h) * p.scale;
                var frame = Core.CoinFrame(_coinImg, _coinMean, _coinGold, p.scale, wd, p.th + p.thv * p.life, c < 0, k, thick, lean, Saturation);
                foreach (var px in frame)
                {
                    int x = Mathf.FloorToInt(p.x + px.dx + 0.5f) - PX0, y = Mathf.FloorToInt(p.y + px.dy + 0.5f) - PY0;
                    if (x < 0 || y < 0 || x >= PW || y >= PH) continue;
                    int idx = (PH - 1 - y) * PW + x; var d = _partBuf[idx];
                    // 後ろに書いたものが手前 (通常の合成)
                    float al = a, da = d.a / 255f, oa = al + da * (1f - al);
                    if (oa <= 0f) continue;
                    byte r = (byte)((px.col.r * al + d.r * da * (1f - al)) / oa), g = (byte)((px.col.g * al + d.g * da * (1f - al)) / oa), b = (byte)((px.col.b * al + d.b * da * (1f - al)) / oa);
                    _partBuf[idx] = new Color32(r, g, b, (byte)Mathf.Round(oa * 255f));
                }
            }
            _partTex.SetPixels32(_partBuf); _partTex.Apply(false); _partsDrawn = true;
        }

        // =====================================================================
        //  接触不良のちらつき・揺れ
        // =====================================================================

        private void Jolt(float vy, float vx) { _gvy += vy; _gvx += vx * (_rng.Next() < 0.5f ? -1f : 1f); }

        private void StartFlicker(float strength)
        {
            const int count = 9; const float offProb = 0.55f, dimProb = 0.25f, dim = 0.35f;
            var a = new float[count];
            for (int i = 0; i < count; i++)
            {
                float k = (1f - (float)i / count) * strength, r = _rng.Next();
                a[i] = (i == 0 && strength >= 1f) ? 0f : r < offProb * k ? 0f : r < (offProb + dimProb) * k ? dim : 1f;
            }
            _flick = a; _flickT = 0f;
        }

        private float FlickLevel()
        {
            if (_flick == null) return 1f;
            int i = Mathf.FloorToInt(_flickT / 0.025f);
            if (i >= _flick.Length) { _flick = null; return 1f; }
            return _flick[i];
        }

        // =====================================================================
        //  数字 (GoldDisplayCore)
        // =====================================================================

        private class Comp { public List<Vector2Int> pts; public int x0, y0, x1, y1; }
        private class Glyph { public List<Comp> comps; public int x0, y0, x1, y1; }

        private void ParseMarker()
        {
            _segs = new List<Vector2Int>[0][];
            if (marker == null) return;
            var px = marker.GetPixels32(); int w = marker.width, h = marker.height;
            bool On(int x, int y) => px[(h - 1 - y) * w + x].a > 0;
            var seen = new bool[w * h]; var comps = new List<Comp>();
            for (int y0 = 0; y0 < h; y0++)
                for (int x0 = 0; x0 < w; x0++)
                {
                    if (!On(x0, y0) || seen[y0 * w + x0]) continue;
                    var c = new Comp { pts = new List<Vector2Int>(), x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1 };
                    var st = new Stack<Vector2Int>(); st.Push(new Vector2Int(x0, y0)); seen[y0 * w + x0] = true;
                    while (st.Count > 0)
                    {
                        var p = st.Pop(); c.pts.Add(p);
                        c.x0 = Mathf.Min(c.x0, p.x); c.x1 = Mathf.Max(c.x1, p.x); c.y0 = Mathf.Min(c.y0, p.y); c.y1 = Mathf.Max(c.y1, p.y);
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = p.x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = p.y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[ny * w + nx] || !On(nx, ny)) continue;
                            seen[ny * w + nx] = true; st.Push(new Vector2Int(nx, ny));
                        }
                    }
                    comps.Add(c);
                }
            comps.Sort((a, b) => a.x0.CompareTo(b.x0));
            var glyphs = new List<Glyph>();
            foreach (var c in comps)
            {
                var g = glyphs.Count > 0 ? glyphs[glyphs.Count - 1] : null;
                if (g != null && c.x0 <= g.x1 + 1) { g.comps.Add(c); g.x1 = Mathf.Max(g.x1, c.x1); g.y0 = Mathf.Min(g.y0, c.y0); g.y1 = Mathf.Max(g.y1, c.y1); }
                else glyphs.Add(new Glyph { comps = new List<Comp> { c }, x0 = c.x0, x1 = c.x1, y0 = c.y0, y1 = c.y1 });
            }
            _digitCount = Mathf.Max(0, glyphs.Count - 1);
            _segs = new List<Vector2Int>[_digitCount][];
            for (int k = 0; k < _digitCount; k++)
            {
                var g = glyphs[k]; _segs[k] = new List<Vector2Int>[7];
                for (int s = 0; s < 7; s++) _segs[k][s] = new List<Vector2Int>();
                foreach (var c in g.comps)
                {
                    bool wide = c.x1 - c.x0 + 1 > c.y1 - c.y0 + 1;
                    float rx = ((c.x0 + c.x1) / 2f - g.x0) / Mathf.Max(1, g.x1 - g.x0), ry = ((c.y0 + c.y1) / 2f - g.y0) / Mathf.Max(1, g.y1 - g.y0);
                    char name = wide ? (ry < 0.25f ? 'a' : ry > 0.75f ? 'd' : 'g') : (rx < 0.5f ? (ry < 0.5f ? 'f' : 'e') : (ry < 0.5f ? 'b' : 'c'));
                    _segs[k][name - 'a'].AddRange(c.pts);
                }
            }
            if (glyphs.Count > 0) foreach (var c in glyphs[glyphs.Count - 1].comps) _always.AddRange(c.pts);
        }

        private void DrawDisplay()
        {
            if (display == null || _segs == null || (_segs.Length == 0 && _always.Count == 0)) return;
            int value = Mathf.Clamp(Mathf.RoundToInt(_shown), 0, (int)Mathf.Pow(10, _digitCount) - 1);
            float level = FlickLevel();
            if (value == _drawnValue && Mathf.Approximately(level, _drawnLevel) && _dispTex != null) return;
            _drawnValue = value; _drawnLevel = level;
            if (_dispTex == null)
            {
                _dispTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DGoldDisplay" };
                display.texture = _dispTex; display.enabled = true;
            }
            var buf = new Color32[W * H];
            if (level > 0f)
            {
                var lit = new List<Vector2Int>(_always);
                string s = value.ToString();
                for (int k = 0; k < _digitCount; k++)
                {
                    int idx = k - (_digitCount - s.Length); if (idx < 0) continue;
                    foreach (char ch in Seg7[s[idx] - '0']) lit.AddRange(_segs[k][ch - 'a']);
                }
                var on = new HashSet<int>(); foreach (var p in lit) on.Add(p.y * W + p.x);
                var acc = new Dictionary<int, float>();
                foreach (var p in lit)
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = p.x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = p.y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H || on.Contains(ny * W + nx)) continue;
                        int key = ny * W + nx; acc[key] = (acc.TryGetValue(key, out var v) ? v : 0f) + 1f;
                    }
                foreach (var kv in acc)
                {
                    float w = Mathf.Min(1f, kv.Value * 0.5f);
                    buf[(H - 1 - kv.Key / W) * W + kv.Key % W] = new Color32(Orange.r, Orange.g, Orange.b, (byte)Mathf.Round(255f * BloomMax * w * level * level));
                }
                foreach (var p in lit) buf[(H - 1 - p.y) * W + p.x] = new Color32(Orange.r, Orange.g, Orange.b, (byte)Mathf.Round(255f * level));
            }
            _dispTex.SetPixels32(buf); _dispTex.Apply(false);
        }
    }
}

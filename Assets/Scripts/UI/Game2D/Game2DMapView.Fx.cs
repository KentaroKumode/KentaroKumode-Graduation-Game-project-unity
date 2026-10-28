using System.Collections.Generic;
using GameLoop;
using MapSystem.FreeMove;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 航行の画面演出 (2026-10-19)。 正本は docs/design/map-prototype.html の <c>FLAREFX</c> (照明弾) / <c>RECONFX</c> (偵察) /
    /// <c>WXFX</c> (天候 7 種)。 値は HTML の cfg をそのまま持ってくる。 液晶のバッファへ 1 dot ずつ、 地図の上に描く。
    /// 色は液晶のインク (共通) と層の地の色の混色だけで、 半透明は使わない (濃さはディザの密度と大きさで表す)。 乱数は種つき xorshift32。 実時間で動く。
    /// </summary>
    public partial class Game2DMapView
    {
        // ── HTML の cfg ──
        private const float RiseMs = 320, RiseH = 72, FadeMs = 700, Drift = 26, Sway = 4, GlowR = 44, GlowA = 0.55f,
                            IgnMs = 130, IgnScale = 1.5f, SmokeMs = 1300, RayLong = 16, RayShort = 8;
        private const int Embers = 16;
        private const float MsPerTurn = 4000f;            // 照明の燃焼の時計 1 手番 = 実時間 (演出の目安)
        private const float WedgeA = 1.05f, ReconFlashMs = 380, ReconMinR = 3, ReconDensity = 11, ReconSweepMs = 1400;
        private const int ReconTicks = 12;
        private const float FogInner = 70, FogOuter = 260, FogMax = 0.30f, FogDrift = 5, RingEveryMs = 2600, RingLifeMs = 2200, RingReach = 3.2f,
                            HailRate = 7, ShakeMs = 380, RippleEveryMs = 1250, RippleLifeMs = 1900, RippleReach = 26;
        private const int SandN = 110, SnowN = 130, DustN = 70;

        // ── 乱数 (種つき xorshift32) ──
        private uint _q = 20261013;
        private float Rnd() { _q ^= _q << 13; _q ^= _q >> 17; _q ^= _q << 5; return _q / 4294967296f; }
        private float Rr(float a, float b) => a + Rnd() * (b - a);

        public Vector2 ShakeDots { get; private set; }

        // =====================================================================
        //  合図
        // =====================================================================

        private bool _flareActive;
        private float _riseT, _burnClock, _flareRt, _flareRtLast, _subTurn;
        private int _flareLeftSeen = -1;
        private Vector2 _flareAnchor;          // 打ち上げた点 (配置の座標)
        private readonly List<Ember> _embers = new List<Ember>();
        private struct Ember { public float at, vx, life, dx; }

        private bool _reconActive;
        private float _reconT0, _reconDoneAt = -1e9f;
        private Vector2 _reconAnchor;

        private void OnNavFx(GameManager.NavFx fx)
        {
            var sim = GameManager.Instance != null ? GameManager.Instance.FreeMap : null;
            if (sim == null) return;
            var p = sim.Layout.pos[sim.Me]; var anchor = new Vector2((float)p.x, (float)p.y);
            if (fx == GameManager.NavFx.Flare)
            {
                _flareActive = true; _riseT = 0; _burnClock = 0; _flareRt = 0; _flareRtLast = Time.unscaledTime;
                _flareLeftSeen = sim.FlareTurnsLeft; _subTurn = 0; _flareAnchor = anchor;
                _embers.Clear();
                for (int i = 0; i < Embers; i++) _embers.Add(new Ember { at = 0.05f + Rnd() * 0.95f, vx = (Rnd() - 0.5f) * 16, life = 0.5f + Rnd() * 0.5f, dx = (Rnd() - 0.5f) * 8 });
            }
            else { _reconActive = true; _reconT0 = Time.unscaledTime; _reconAnchor = anchor; }
        }

        // =====================================================================
        //  照明弾 (FLAREFX)
        // =====================================================================

        private static float Eo(float u) => 1 - (1 - u) * (1 - u);
        private static float Flick(float t) => 0.82f + 0.18f * Mathf.Sin(t * 0.045f + 3 * Mathf.Sin(t * 0.017f)) + 0.06f * Mathf.Sin(t * 0.13f);

        private readonly Dictionary<int, Color32[]> _glow = new Dictionary<int, Color32[]>();

        private Color32[] GlowSprite(int r, float a)
        {
            int key = r * 64 + Mathf.RoundToInt(a * 20);
            if (_glow.TryGetValue(key, out var c)) return c;
            int n = 2 * r + 1; c = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float rr = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                    if (rr >= 1) continue;
                    float lv = (1 - rr) * (1 - rr) * a;
                    if (lv * 16 > Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) c[y * n + x] = rr < 0.4f ? InkHi : InkMid;
                }
            _glow[key] = c;
            return c;
        }

        private void FlareFx(FreeMapSim sim, float dt)
        {
            if (!_flareActive) return;
            if (!sim.FlareOn) { _flareActive = false; return; }
            float dtMs = Mathf.Min(0.1f, _now - _flareRtLast) * 1000f; _flareRtLast = _now;
            _flareRt += dtMs;
            if (_riseT < RiseMs) _riseT += dtMs; else _burnClock += dtMs;

            // 燃焼の経過 (ms 換算) = 手番ぶんの階段 + 手番の中での実時間のゆっくりした進み
            if (sim.FlareTurnsLeft != _flareLeftSeen) { _flareLeftSeen = sim.FlareTurnsLeft; _subTurn = 0; }
            _subTurn = Mathf.Min(0.9f, _subTurn + dtMs / MsPerTurn);
            float totalTurns = FreeMapParams.FlareTurns;
            float total = totalTurns * MsPerTurn, bt = ((totalTurns - sim.FlareTurnsLeft) + _subTurn) * MsPerTurn;

            var o0 = Sp(_flareAnchor.x, _flareAnchor.y);
            float topY = o0.y - RiseH;
            float head = Mathf.Min(_riseT, RiseMs), since = _riseT >= RiseMs ? bt : 0;
            for (int i = 1; i <= 7; i++)                                                    // 昇った筋に残る煙
            {
                if (since + (1 - i / 7f) * RiseMs > SmokeMs) continue;
                PxC(o0.x + (i % 2 == 1 ? 1 : -1), o0.y - RiseH * Eo((i / 7f) * head / RiseMs), 1, InkMid);
            }
            if (_riseT < RiseMs)                                                            // ① 打ち上げ
            {
                float u = _riseT / RiseMs;
                for (int i = 4; i >= 0; i--)
                    PxC(o0.x, o0.y - RiseH * Eo(Mathf.Max(0, u - i * 0.04f)), i == 0 ? 2 : 1, i == 0 ? InkHot : i < 3 ? InkHi : InkMid);
                return;
            }
            float fadeAt = total - FadeMs, fade = bt > fadeAt ? 1 - (bt - fadeAt) / FadeMs : 1, env = fade * fade;
            float ax = o0.x + Mathf.Sin(_flareRt / 380f) * Sway, ay = topY + Drift * (bt / total) + (1 - fade) * 12;
            float f = Flick(_flareRt) * env;
            bool ign = _burnClock < IgnMs;
            int R = Mathf.Max(4, Mathf.RoundToInt(GlowR * (0.9f + 0.1f * f) * (0.4f + 0.6f * env) * (ign ? IgnScale : 1f) / 2f) * 2);
            float a = Mathf.Round(GlowA * (ign ? 1.4f : 1f) * f * 20f) / 20f;
            var g = GlowSprite(R, a); int gn = 2 * R + 1; int gx = Mathf.RoundToInt(ax) - R, gy = Mathf.RoundToInt(ay) - R;
            for (int y = 0; y < gn; y++) for (int x = 0; x < gn; x++) { var c = g[y * gn + x]; if (c.a != 0) Put(gx + x, gy + y, c); }       // 光の暈
            float k = 0.7f + 0.5f * f; int ix = Mathf.RoundToInt(ax), iy = Mathf.RoundToInt(ay);
            for (int m = 2; m < RayLong * k * env; m++) { var c = m < 5 ? InkHot : InkHi; Put(ix, iy - m, c); Put(ix, iy + m, c); Put(ix - m, iy, c); Put(ix + m, iy, c); }
            for (int m = 2; m < RayShort * k * env; m++) { var c = m < 4 ? InkHot : InkHi; Put(ix - m, iy - m, c); Put(ix + m, iy - m, c); Put(ix - m, iy + m, c); Put(ix + m, iy + m, c); }
            PxC(ax, ay, ign ? 7 : 5, InkHi); PxC(ax, ay, ign ? 5 : 3, InkHot);              // 核
            foreach (var e in _embers)                                                      // 火の粉
            {
                float et = (bt - e.at * total) / 1000f;
                if (et < 0 || et > e.life) continue;
                float u = et / e.life;
                PxC(ax + e.dx + e.vx * et, ay + 14 * et + 40 * et * et, u < 0.4f ? 2 : 1, u < 0.25f ? InkHot : u < 0.65f ? InkHi : InkMid);
            }
        }

        // =====================================================================
        //  偵察 (RECONFX)
        // =====================================================================

        private void ReconFx()
        {
            float rr = FreeMapParams.ReconRange * _hopUnit * _ls;
            var center = Sp(_reconAnchor.x, _reconAnchor.y);
            if (!_reconActive)
            {
                float u0 = (_now - _reconDoneAt) * 1000f / ReconFlashMs;
                if (u0 >= 0 && u0 < 1)
                {
                    var col = u0 < 0.5f ? InkHot : InkHi;
                    int steps = Mathf.Max(16, Mathf.RoundToInt(2 * Mathf.PI * rr / 2));
                    for (int s = 0; s < steps; s++) { float ang = s * 2 * Mathf.PI / steps; Rect(Mathf.RoundToInt(center.x + Mathf.Cos(ang) * rr), Mathf.RoundToInt(center.y + Mathf.Sin(ang) * rr), 2, 2, col); }
                }
                return;
            }
            float progress = (_now - _reconT0) * 1000f / ReconSweepMs;
            if (progress >= 1f) { _reconActive = false; _reconDoneAt = _now; return; }
            int R = Mathf.RoundToInt(rr), cx0 = Mathf.RoundToInt(center.x), cy0 = Mathf.RoundToInt(center.y);
            float th = -Mathf.PI / 2 + progress * Mathf.PI * 2;                              // 12 時から時計回りに 1 周
            Ellipse(center, rr, rr, 0, _lc.hi, new[] { 1, 1 });                              // 偵察の範囲の輪 (1 dot おき)
            for (int k = 0; k < ReconTicks; k++)                                              // 縁の目盛り
            {
                float ang = (float)k / ReconTicks * Mathf.PI * 2 - Mathf.PI / 2;
                for (int d = 0; d < 4; d++) Put(Mathf.RoundToInt(cx0 + Mathf.Cos(ang) * (R - d)), Mathf.RoundToInt(cy0 + Mathf.Sin(ang) * (R - d)), k % 3 == 0 ? InkHi : InkDim);
            }
            int y0 = Mathf.Max(-R, -cy0), y1 = Mathf.Min(R, H - 1 - cy0), x0 = Mathf.Max(-R, -cx0), x1 = Mathf.Min(R, W - 1 - cx0);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float r2 = x * x + y * y;
                    if (r2 > R * R || r2 < ReconMinR * ReconMinR) continue;
                    float behind = th - Mathf.Atan2(y, x); behind = ((behind % (Mathf.PI * 2)) + Mathf.PI * 2) % (Mathf.PI * 2);
                    if (behind >= WedgeA) continue;
                    float lv = Mathf.Pow(1 - behind / WedgeA, 1.6f) * ReconDensity * (0.45f + 0.55f * Mathf.Sqrt(r2) / R);
                    int px = cx0 + x, py = cy0 + y;
                    if (lv > Bayer[(py & 3) * 4 + (px & 3)] + 0.5f) Put(px, py, behind < WedgeA * 0.2f ? InkMid : InkDim);
                }
            for (int r = (int)ReconMinR; r <= R; r++) Put(Mathf.RoundToInt(cx0 + Mathf.Cos(th) * r), Mathf.RoundToInt(cy0 + Mathf.Sin(th) * r), r > R - 3 ? InkHot : InkHi);   // 走査線
        }

        // =====================================================================
        //  天候 (WXFX)
        // =====================================================================

        private struct Grain { public float x, y, v, len, ph, amp; public bool big; }
        private struct HailDrop { public float x, y, vx, vy, gy; public bool dead; }
        private struct Hit { public float x, y, t0; }
        private struct Ring { public float t0, x, y, reach; }      // x, y は地図に固定 (視点が動いても付いてくる) = 液晶の原点からの差

        private List<Grain> _sand, _snow, _dust;
        private readonly List<HailDrop> _hail = new List<HailDrop>();
        private readonly List<Hit> _hits = new List<Hit>();
        private readonly List<Ring> _rings = new List<Ring>(), _ripples = new List<Ring>();
        private float _nextRing, _nextRipple, _nextShake, _shakeUntil;
        private bool _wxBuilt;
        private bool[] _prevSunk;
        private int _prevSunkFloor = -1;
        private float _lastLox, _lastLoy; private bool _hasLast;

        private void BuildWeather()
        {
            _sand = new List<Grain>(); for (int i = 0; i < SandN; i++) _sand.Add(new Grain { x = Rnd() * W, y = Rnd() * H, v = Rr(70, 190), len = 2 + Mathf.Floor(Rnd() * 5), ph = Rnd() * 6.28f });
            _snow = new List<Grain>(); for (int i = 0; i < SnowN; i++) _snow.Add(new Grain { x = Rnd() * W, y = Rnd() * H, v = Rr(14, 34), ph = Rnd() * 6.28f, amp = Rr(2, 7), big = Rnd() < 0.18f });
            _dust = new List<Grain>(); for (int i = 0; i < DustN; i++) _dust.Add(new Grain { x = Rnd() * W, y = Rnd() * H, v = Rr(8, 22), ph = Rnd() * 6.28f, big = Rnd() < 0.25f });
            _wxBuilt = true;
        }

        private static bool On(FreeMapSim sim, WeatherKind k) => sim.Weather == k && !double.IsNegativeInfinity(sim.Front);

        /// <summary>ピクセルの輪: 2×2 の四角をおきに並べた破線の輪。 半径は 3 dot 刻みで段階的に、 位置は 2 dot の格子に合わせる。</summary>
        private void BlockRing(float cx0, float cy0, float rx, float ry, Color32 col)
        {
            const int B = 2;
            float qx = Mathf.Max(B, Mathf.Round(rx / 3) * 3), qy = Mathf.Max(1, Mathf.Round(ry / 3) * 3);
            int n = Mathf.Max(8, Mathf.CeilToInt(Mathf.PI * (qx + qy) / (B * 2)));
            int lx = int.MinValue, ly = int.MinValue, k = 0;
            for (int i = 0; i < n; i++)
            {
                float a = (float)i / n * 6.2832f;
                int x = Mathf.RoundToInt((cx0 + Mathf.Cos(a) * qx) / B) * B, y = Mathf.RoundToInt((cy0 + Mathf.Sin(a) * qy) / B) * B;
                if (x == lx && y == ly) continue;
                lx = x; ly = y;
                if ((k++ & 1) == 0) Rect(x, y, B, B, col);
            }
        }

        private void WeatherFx(FreeMapSim sim, float dt)
        {
            if (!_wxBuilt) BuildWeather();
            float tNow = _now * 1000f, t = _now;

            // 粒は地図に固定: 視点を動かしたぶん粒も動かし、 画面にくっついてこないようにする (視点が大きく飛んだ時は、 ずらさない)
            if (_hasLast)
            {
                float cdx = _lox - _lastLox, cdy = _loy - _lastLoy;
                if ((cdx != 0 || cdy != 0) && Mathf.Abs(cdx) < 120 && Mathf.Abs(cdy) < 120)
                {
                    foreach (var arr in new[] { _sand, _snow, _dust })
                        for (int i = 0; i < arr.Count; i++) { var p = arr[i]; p.x = ((p.x + cdx) % W + W) % W; p.y = ((p.y + cdy) % H + H) % H; arr[i] = p; }
                    for (int i = 0; i < _hail.Count; i++) { var h = _hail[i]; h.x += cdx; h.y += cdy; h.gy += cdy; _hail[i] = h; }
                    for (int i = 0; i < _hits.Count; i++) { var h = _hits[i]; h.x += cdx; h.y += cdy; _hits[i] = h; }
                }
            }
            _lastLox = _lox; _lastLoy = _loy; _hasLast = true;

            // 沈んだ点の合図: 沈んだ瞬間に、 その場所へ三重の波紋
            if (_prevSunkFloor != sim.Floor || _prevSunk == null || _prevSunk.Length != sim.Layout.Count)
            {
                _prevSunk = new bool[sim.Layout.Count]; for (int i = 0; i < _prevSunk.Length; i++) _prevSunk[i] = sim.IsSunk(i); _prevSunkFloor = sim.Floor;
            }
            else
                for (int i = 0; i < _prevSunk.Length; i++)
                    if (sim.IsSunk(i) && !_prevSunk[i])
                    {
                        _prevSunk[i] = true;
                        var d = Sp(sim.Layout.pos[i]);
                        for (int k = 0; k < 3; k++) _ripples.Add(new Ring { t0 = tNow + k * 220, x = d.x - _lox, y = d.y - _loy, reach = 30 + k * 6 });
                    }

            if (On(sim, WeatherKind.Fog)) Fog(tNow);

            if (On(sim, WeatherKind.Still))
            {
                if (tNow >= _nextRing) { _rings.Add(new Ring { t0 = tNow, x = _me.x - _lox, y = _me.y - _loy }); _nextRing = tNow + RingEveryMs; }
                foreach (var r in _rings) { float u = (tNow - r.t0) / RingLifeMs; if (u >= 1) continue; float rad = 4 + Mathf.Floor(u * 9) / 9 * RingReach * _ls; BlockRing(r.x + _lox, r.y + _loy, rad, rad, u < 0.6f ? InkMid : InkDim); }
                _rings.RemoveAll(r => tNow - r.t0 >= RingLifeMs);
            }
            else _rings.Clear();

            if (On(sim, WeatherKind.Sand))
            {
                for (int i = 0; i < _sand.Count; i++)
                {
                    var p = _sand[i]; p.x += p.v * dt; if (p.x > W) { p.x = -p.len; p.y = Rnd() * H; }
                    _sand[i] = p;
                    int y = Mathf.FloorToInt(p.y + Mathf.Sin(t * 2 + p.ph) * 0.6f);
                    Rect(Mathf.FloorToInt(p.x), y, (int)p.len, 1, p.len > 3 ? InkMid : InkDim);
                }
            }

            if (On(sim, WeatherKind.Snow))
            {
                var col = Mix(InkRed, InkHi, 0.42f);
                for (int i = 0; i < _snow.Count; i++)
                {
                    var p = _snow[i]; p.y += p.v * dt; if (p.y > H + 2) { p.y = -2; p.x = Rnd() * W; }
                    _snow[i] = p;
                    int x = Mathf.FloorToInt(p.x + Mathf.Sin(t * 0.7f + p.ph) * p.amp);
                    Rect(x, Mathf.FloorToInt(p.y), p.big ? 2 : 1, p.big ? 2 : 1, col);
                }
            }

            if (On(sim, WeatherKind.Hail))
            {
                if (Rnd() < HailRate * dt) _hail.Add(new HailDrop { x = Rnd() * W, y = -4, vy = Rr(150, 215), vx = Rr(24, 46), gy = Rr(H * 0.2f, H * 0.97f) });
                for (int i = 0; i < _hail.Count; i++)
                {
                    var h = _hail[i]; h.x += h.vx * dt; h.y += h.vy * dt;
                    if (h.y >= h.gy) { h.dead = true; _hits.Add(new Hit { x = h.x, y = h.gy, t0 = tNow }); _hail[i] = h; continue; }
                    _hail[i] = h;
                    Rect(Mathf.FloorToInt(h.x - h.vx * 0.03f), Mathf.FloorToInt(h.y - h.vy * 0.03f), 1, 2, InkDim);
                    Rect(Mathf.FloorToInt(h.x), Mathf.FloorToInt(h.y), 2, 2, InkHot);
                }
                _hail.RemoveAll(h => h.dead);
                foreach (var h in _hits)
                {
                    float u = (tNow - h.t0) / 220f; if (u >= 1) continue;
                    int r = 1 + Mathf.FloorToInt(u * 3); var col = u < 0.5f ? InkHi : InkMid; int hx = Mathf.FloorToInt(h.x), hy = Mathf.FloorToInt(h.y);
                    Put(hx - r, hy, col); Put(hx + r, hy, col); Put(hx, hy - r, col); Put(hx, hy + r, col); Put(hx + r, hy - r, col); Put(hx - r, hy - r, col);
                }
                _hits.RemoveAll(h => tNow - h.t0 >= 220);
            }
            else { _hail.Clear(); _hits.Clear(); }

            if (On(sim, WeatherKind.Crumble))
            {
                var col = Mix(_lc.landE, InkHi, 0.55f);
                for (int i = 0; i < _dust.Count; i++)
                {
                    var p = _dust[i]; p.y += p.v * dt; if (p.y > H + 2) { p.y = -2; p.x = Rnd() * W; }
                    _dust[i] = p;
                    Rect(Mathf.FloorToInt(p.x + Mathf.Sin(t * 0.9f + p.ph) * 3), Mathf.FloorToInt(p.y), p.big ? 2 : 1, 1, col);
                }
                if (_nextShake == 0) _nextShake = tNow + Rr(3800, 6200);
                if (tNow >= _nextShake) { _shakeUntil = tNow + ShakeMs; _nextShake = tNow + Rr(3800, 6200); }
            }
            else { _shakeUntil = 0; _nextShake = 0; }
            if (tNow >= _shakeUntil) ShakeDots = Vector2.zero;
            else { int k = Mathf.FloorToInt(tNow / 55f); ShakeDots = new Vector2(k % 2 == 1 ? 1 : -1, (k >> 1) % 2 == 1 ? 1 : 0); }

            if (On(sim, WeatherKind.Sink))
            {
                if (tNow >= _nextRipple) { _ripples.Add(new Ring { t0 = tNow, x = Rnd() * W - _lox, y = H * 0.18f + Rnd() * H * 0.8f - _loy, reach = RippleReach }); _nextRipple = tNow + RippleEveryMs; }
            }
            foreach (var r in _ripples)
            {
                float u = (tNow - r.t0) / RippleLifeMs; if (u < 0 || u >= 1) continue;
                float x = r.x + _lox, y = r.y + _loy, rr0 = 2 + Mathf.Floor(u * 8) / 8 * r.reach;
                BlockRing(x, y, rr0, rr0 * 0.5f, u < 0.5f ? InkMid : _lc.landE);
                if (u > 0.25f) BlockRing(x, y, rr0 * 0.55f, rr0 * 0.28f, u < 0.6f ? _lc.landE : _lc.grid);
                if (u < 0.12f) Rect(Mathf.RoundToInt(x), Mathf.RoundToInt(y), 2, 2, InkHi);
            }
            _ripples.RemoveAll(r => tNow - r.t0 >= RippleLifeMs);
        }

        /// <summary>霧: 自艦から離れるほど濃くなる、 ゆっくり流れる点のかすみ (ディザ)。</summary>
        private void Fog(float tNow)
        {
            var col = Mix(_lc.fill2, _lc.landE, 0.85f);
            int sh = Mathf.FloorToInt(tNow / 1000f * FogDrift);
            float cx0 = _me.x, cy0 = _me.y;
            int bx = -(int)_lox, by = -(int)_loy;           // 視点の動き (模様を地図に固定する)
            for (int y = 0; y < H; y++)
            {
                float dy = y - cy0, dy2 = dy * dy;
                for (int x = 0; x < W; x++)
                {
                    float dx = x - cx0, r = Mathf.Sqrt(dx * dx + dy2);
                    if (r <= FogInner) continue;
                    float t = Mathf.Min(1, (r - FogInner) / (FogOuter - FogInner)), lv = t * t * (3 - 2 * t) * FogMax * 16;
                    if (lv > Bayer[((y - by + (sh >> 1)) & 3) * 4 + ((x - bx + sh) & 3)] + 0.5f) _buf[(H - 1 - y) * W + x] = col;
                }
            }
        }
    }
}

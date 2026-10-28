using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 地図の作図 (map-prototype.html の <c>PLOT</c> と <c>drawLCD</c> の中身)。
    /// 地: 暗い色に、 中央ほど明るい点 (ディザ)。 陸 (山) は網点の面 + 明るい輪郭。 航路は影つきの細線。 点は 9×9 の絵。
    /// 自分は中心 1 dot ＋ 十字の腕。 追跡者・使い魔は赤、 囮罠は + の印。 液晶の端に読み出し (EXIT / SCALE / OWN / T+) と天候の名前。
    /// 色は層ごとの地の色 (<see cref="Pals"/>) と共通のインクだけで、 半透明は照明・魔石の範囲の淡い塗りだけ。
    /// </summary>
    public partial class Game2DMapView
    {
        private Color32[] _bg;
        private int _bgFloor = -1;

        // ── 地 ──
        private void Background()
        {
            if (_bg != null && _bgFloor == _viewFloor) { System.Array.Copy(_bg, _buf, _buf.Length); return; }
            _bg = new Color32[W * H]; _bgFloor = _viewFloor;
            const float rx = 300f, ry = 190f, pow = 1.5f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float r = Mathf.Sqrt(Mathf.Pow((x - W / 2f) / rx, 2) + Mathf.Pow((y - H / 2f) / ry, 2));
                    float lv = Mathf.Pow(Mathf.Max(0, 1 - r), pow) * 16;
                    _bg[(H - 1 - y) * W + x] = lv > Bayer[(y & 3) * 4 + (x & 3)] ? _lc.lite : _lc.bg;
                }
            System.Array.Copy(_bg, _buf, _buf.Length);
        }

        // ── 点の絵・色 ──
        private enum Ico { Unknown, Done, Normal }

        private string[] IconFor(FreeMapSim sim, int i, out Color32 col)
        {
            var lay = sim.Layout; var t = lay.types[i];
            bool isStart = i == lay.start, isGoal = i == lay.goal;
            bool known = sim.Known[i] || isStart || isGoal;
            col = _lc.mid;
            if (!known) return IcoUnknown;                                   // 種別が分からない点 = 疑問符
            // 種別の絵 (使い切った点も同じ絵で、 暗い色にするだけ)
            string[] icon; col = _lc.hi;
            if (isStart) { col = _lc.hot; icon = IcoStart; }
            else if (isGoal) { col = _lc.red; icon = IcoExit; }
            else switch (t)
            {
                case TileType.Battle: icon = IcoBattle; break;
                case TileType.EliteBattle: col = _lc.red; icon = IcoElite; break;
                case TileType.Event: icon = IcoEvent; break;
                case TileType.Shop: icon = IcoShop; break;
                case TileType.Rest: icon = IcoRest; break;
                case TileType.Treasure: icon = IcoTreasure; break;
                case TileType.Boss: case TileType.Gate: col = _lc.red; icon = IcoExit; break;
                case TileType.Outpost: col = _lc.hot; icon = IcoStart; break;
                case TileType.SupplyDepot: icon = IcoDepot; break;
                default: icon = IcoDone; break;
            }
            if (sim.Done[i] && t != TileType.Shop && !isStart && !isGoal) col = _lc.dim;
            return icon;
        }

        /// <summary>ボス (出口) の脈動: 心臓の 2 拍 (ドクン・ドクン) で絵が明るい赤へ光り、 背後の赤い霞が膨らみ、 輪が外へ広がって消える。 絵の色を返す。</summary>
        private Color32 BossPulse(Vector2 q)
        {
            const float Period = 1.5f;
            float ph = (_now % Period) / Period;
            float b1 = ph < 0.14f ? 1f - ph / 0.14f : 0f;
            float b2 = ph >= 0.24f && ph < 0.42f ? 0.65f * (1f - (ph - 0.24f) / 0.18f) : 0f;
            float beat = Mathf.Max(b1, b2);
            Disc(q.x, q.y, 15f + 3f * beat, _lc.red, 0.10f + 0.14f * beat);                  // 赤い霞
            float rp = Mathf.Clamp01(ph / 0.8f);                                              // 広がる輪
            if (rp < 1f)
            {
                float r = 12f + 14f * rp;
                Ellipse(q, r, r, 0, Color32.Lerp(_lc.red, _lc.bg, rp));
            }
            return Color32.Lerp(new Color32(176, 48, 42, 255), new Color32(255, 120, 96, 255), beat);
        }

        /// <summary>点の絵の半分の大きさ + 1 dot (道は、 絵の外側 1 dot より内側には引かない)。</summary>
        private int NodeMargin(FreeMapSim sim, int i)
        {
            var rows = IconFor(sim, i, out _);
            return Mathf.CeilToInt(Mathf.Max(rows.Length, rows[0].Length) / 2f) + 1;
        }

        private void Plot(FreeMapSim sim)
        {
            var lay = sim.Layout;
            Background();

            // 陸 (山 = 危険区域): 網点の面 + 明るい輪郭
            foreach (var m in lay.mountains)
            {
                var q = Sp(m.x, m.y);
                float R = (float)System.Math.Max(m.a, m.b) * _ls + 1, co = (float)m.cos, si = (float)m.sin;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(q.x - R)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(q.x + R));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(q.y - R)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(q.y + R));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - q.x, dy = y + 0.5f - q.y;
                        float u = (dx * co + dy * si) / ((float)m.a * _ls), v = (-dx * si + dy * co) / ((float)m.b * _ls);
                        if (u * u + v * v <= 1 && ((x + y) & 1) == 0) Put(x, y, _lc.fill2);
                    }
                Ellipse(q, (float)m.a * _ls, (float)m.b * _ls, Mathf.Atan2((float)m.sin, (float)m.cos), _lc.landE);
            }

            // 航路: 影つきの細線。 点の絵の外側 1 dot より内側には引かない
            foreach (var (a, b) in sim.Roads())
            {
                var A = Sp(lay.pos[a]); var B = Sp(lay.pos[b]);
                float L = Vector2.Distance(A, B); int ma = NodeMargin(sim, a), mb = NodeMargin(sim, b);
                if (L <= ma + mb) continue;
                Vector2 u = (B - A) / L, A2 = A + u * ma, B2 = B - u * mb;
                Line(A2 + Vector2.one, B2 + Vector2.one, _lc.shadow);
                Line(A2, B2, _lc.frameHi);
            }

            // 自艦中心の距離環 (4 と 8)
            for (int r = 4; r <= 8; r += 4) Ellipse(_me, r * _ls, r * _ls, 0, _lc.grid);

            bool blink = Mathf.FloorToInt(_now / 0.4f) % 2 == 0;

            // 範囲: 照明の広がり (淡い塗り + 破線の輪)
            if (sim.FlareOn)
            {
                float fr = FreeMapParams.FlareRange * _hopUnit * _ls;
                Disc(_me.x, _me.y, fr, _lc.hi, 0.10f);
                Ellipse(_me, fr, fr, 0, _lc.mid, new[] { 2, 1 });
            }

            // 気配 = 察知距離の円の波 (外へだけ振れるギザギザの 1 本線)。 段 = 察知距離 (2 / 3 / 4 以上)
            {
                int hops = sim.FlareOn ? FreeMapParams.FlareRange : FreeMapParams.DetectRange;
                int L = Mathf.Clamp(hops - 2, 0, 2);
                DetectNoise(hops * _hopUnit * _ls, L, L >= 2 ? _lc.red : L >= 1 ? _lc.warn : _lc.mid);
            }

            // 行き先の見込み (カーソルを乗せた点)
            if (_hover >= 0 && _hover != sim.Me && _hover < lay.Count && !sim.IsSunk(_hover))
            {
                var q = Sp(lay.pos[_hover]); bool ok = sim.CanMoveTo(_hover);
                if (ok) Line(_me, q, _lc.hot);
                Bracket(q, 6, ok ? _lc.hot : _lc.dim);
            }

            // 点
            for (int i = 0; i < lay.Count; i++)
            {
                if (sim.IsSunk(i)) continue;
                var q = Sp(lay.pos[i]);
                var ic = IconFor(sim, i, out var col);
                if (ic == IcoExit && !sim.Done[i]) col = BossPulse(q);
                Glyph(ic, q.x, q.y, col);
            }

            // 囮罠
            foreach (var lure in sim.Lures)
            {
                var q = Sp(lay.pos[lure.node]);
                if (blink || lure.turnsLeft > 2) Glyph(GlyphPlus, q.x, q.y, blink ? _lc.hot : _lc.mid, Pxs);
            }

            // 追跡者
            if (sim.HasFoe && sim.FoeAlive && sim.FoeVisible)
            {
                var q = Sp(lay.pos[sim.FoeNode]);
                Glyph(IcoFoe, q.x, q.y, _lc.red);
                string st = sim.FoeFreezeTurns > 0 ? "HOLD" : sim.FoeMode == FoeMode.Chase ? "CHSE" : sim.FoeMode == FoeMode.Alert ? "ALRT" : "PTRL";
                Text(st, q.x, q.y - 6 * Pxs, _lc.red);
            }
            // 使い魔
            foreach (var f in sim.VisibleFamiliars())
            {
                var q = Sp(lay.pos[f.pos]);
                Rect(Mathf.FloorToInt(q.x) - (Pxs - 1), Mathf.FloorToInt(q.y) - (Pxs - 1), 2 * Pxs - 1, 2 * Pxs - 1, _lc.red);
            }

            Readout(sim);
        }

        /// <summary>自分: 青い十字 (中心 1 dot ＋ 腕)。 ノード (生成り) と見分けがつくよう青にし、 天候・照明・偵察の演出よりも<b>手前</b> (一番上) に描く。</summary>
        private static readonly Color32 SelfBlue = new Color32(84, 170, 255, 255), SelfBlueHi = new Color32(150, 208, 255, 255);

        private void DrawSelf()
        {
            int cxp = Mathf.FloorToInt(_me.x), cyp = Mathf.FloorToInt(_me.y), u = Pxs;
            // 下に黒の縁 (1 dot) を敷いて、 明るいノード・線の上でも埋もれないようにする
            var edge = new Color32(4, 10, 24, 255);
            for (int dx = -4 * u - 1; dx <= 4 * u + 1; dx++) for (int dy = -1; dy <= 1; dy++) if (Mathf.Abs(dx) > u - 1 || dy != 0) Put(cxp + dx, cyp + dy, edge);
            for (int dy = -4 * u - 1; dy <= 4 * u + 1; dy++) for (int dx = -1; dx <= 1; dx++) if (Mathf.Abs(dy) > u - 1 || dx != 0) Put(cxp + dx, cyp + dy, edge);
            Rect(cxp - (u - 1), cyp - (u - 1), 2 * u - 1, 2 * u - 1, SelfBlueHi);
            Rect(cxp - 4 * u, cyp, 2 * u, 1, SelfBlue); Rect(cxp + 2 * u + 1, cyp, 2 * u, 1, SelfBlue);
            Rect(cxp, cyp - 4 * u, 1, 2 * u, SelfBlue); Rect(cxp, cyp + 2 * u + 1, 1, 2 * u, SelfBlue);
        }

        /// <summary>察知距離の円: 円周を 3 dot おきに区切り、 各点の高さを乱数で決めて直線でつなぐ。 その場で震える (段ごとに 4 / 10 / 20 回/秒)。</summary>
        private void DetectNoise(float R, int level, Color32 col)
        {
            int[] fps = { 4, 10, 20 }; float[] amps = { 1.5f, 4f, 8f };
            int frame = Mathf.FloorToInt(_now * fps[level]);
            float amp = amps[level]; int n = Mathf.Max(48, Mathf.RoundToInt(Mathf.PI * 2 * R / 3));
            bool has = false; Vector2 prev = default;
            for (int i = 0; i <= n; i++)
            {
                float a = (float)i / n * Mathf.PI * 2, s = Rnd01(i % n, frame);
                float h = amp * (s > 0.92f ? 1.6f + s : s * s);
                var q = new Vector2(_me.x + Mathf.Cos(a) * (R + h), _me.y + Mathf.Sin(a) * (R + h));
                if (has) Line(prev, q, col);
                prev = q; has = true;
            }
        }

        private static float Rnd01(int i, int frame)
        {
            unchecked
            {
                int v = i * 374761393 + frame * 668265263;
                v = (v ^ (int)((uint)v >> 13)) * 1274126177;
                return ((uint)(v ^ (int)((uint)v >> 16))) / 4294967296f;
            }
        }

        // ── 読み出し (左下: EXIT の方角・縮尺・自艦の格子位置・経過手番 / 上: 天候) ──
        private static readonly string[] Compass = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };

        private void Readout(FreeMapSim sim)
        {
            var lay = sim.Layout; var me = lay.pos[sim.Me]; var ex = lay.pos[lay.goal];
            // 出口のコンパス (16 方向): 出口の点が隠れていても、 方角だけ分かる。 距離は出さない
            float b = (Mathf.Atan2((float)(ex.x - me.x), -(float)(ex.y - me.y)) + Mathf.PI * 2) % (Mathf.PI * 2);
            int sec = Mathf.RoundToInt(b / (Mathf.PI * 2 / 16)) % 16;
            float ang = sec * Mathf.PI * 2 / 16 - Mathf.PI / 2;
            float cx0 = 96 + 118, cy0 = H - 17, Rr = 9;
            for (int k = 0; k < 16; k++) { float ak = k * Mathf.PI * 2 / 16 - Mathf.PI / 2; Put(Mathf.RoundToInt(cx0 + Mathf.Cos(ak) * Rr), Mathf.RoundToInt(cy0 + Mathf.Sin(ak) * Rr), _lc.dim); }
            Rect((int)cx0, (int)(cy0 - Rr - 1), 1, 2, _lc.hi);                                     // 北の目印
            Rect(Mathf.RoundToInt(cx0 + Mathf.Cos(ang) * Rr) - 1, Mathf.RoundToInt(cy0 + Mathf.Sin(ang) * Rr) - 1, 3, 3, _lc.hot);
            for (int r = 1; r < Rr - 1; r++) Put(Mathf.RoundToInt(cx0 + Mathf.Cos(ang) * r), Mathf.RoundToInt(cy0 + Mathf.Sin(ang) * r), _lc.dim);   // 針
            Text("EXIT " + Compass[sec], 96, H - 35, _lc.mid, Align.Left);
            Text("SCALE X" + _zoom, 96, H - 22, _lc.dim, Align.Left);
            char col = (char)('A' + Mathf.Clamp(Mathf.FloorToInt((float)me.x / 2f), 0, 25));
            int row = Mathf.FloorToInt((float)me.y / 2f) + 1;
            Text("OWN " + col + row, 96, H - 9, _lc.dim, Align.Left);
            Text("T+" + Mathf.Min(999, sim.Turn).ToString("000"), 96 + 8 * 4 * Pxs, H - 9, _lc.dim, Align.Left);

            string wx = sim.Weather != WeatherKind.None && !double.IsNegativeInfinity(sim.Front) ? WxCode(sim.Weather) : "CALM";
            Text("WX " + wx, W - 8 - 89, 6, wx == "CALM" ? _lc.dim : _lc.hi, Align.Right);
        }

        private static string WxCode(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Fog: return "FOG";
                case WeatherKind.Still: return "STILL";
                case WeatherKind.Sand: return "SAND";
                case WeatherKind.Snow: return "SNOW";
                case WeatherKind.Hail: return "HAIL";
                case WeatherKind.Crumble: return "CRMB";
                case WeatherKind.Sink: return "SINK";
                default: return "CALM";
            }
        }
    }
}

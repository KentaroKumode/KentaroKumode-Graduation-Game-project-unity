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
            Ground(_bg, _lc);
            System.Array.Copy(_bg, _buf, _buf.Length);
        }

        /// <summary>層の地 (暗い色に、 中央ほど明るい 4×4 の網点) を W×H の画素へ描く。 ロード画面も同じ地を敷く。</summary>
        internal static void Ground(Color32[] dst, int floor) => Ground(dst, Pals[Mathf.Clamp(floor, 1, 7)]);

        private static void Ground(Color32[] dst, Lc lc)
        {
            const float rx = 300f, ry = 190f, pow = 1.5f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float r = Mathf.Sqrt(Mathf.Pow((x - W / 2f) / rx, 2) + Mathf.Pow((y - H / 2f) / ry, 2));
                    float lv = Mathf.Pow(Mathf.Max(0, 1 - r), pow) * 16;
                    dst[(H - 1 - y) * W + x] = lv > Bayer[(y & 3) * 4 + (x & 3)] ? lc.lite : lc.bg;
                }
        }

        // ── 点の絵・色 ──
        private enum Ico { Unknown, Done, Normal }

        // 点の色 (2026-11-02): 青白い。 線・文字の生成りのインクとは別にした。 液晶の仕上げの寒色の乗算 (赤を落とす) が掛かった後で青白く見える値。
        //   未到達 (疑問符) も到達済みの点と同じ色。 使い切った点は暗転 (暗い青灰)。
        private static readonly Color32 NodeInk = Hex("#e6f2fa"), NodeHot = Hex("#f0f8ff"), NodeDone = Hex("#364255");

        // 縄張りの点の色 (2026-11-12・仮の印): 青白い点の色と同じくらいの明るさで、 赤みに振った (彩度で区別する)。 使い切った点は暗い赤灰。
        private static readonly Color32 TerrInk = Hex("#f0a08c"), TerrDone = Hex("#553636");

        /// <summary>縄張りの点の絵の色: 青白い点 (未到達・種別) は赤みのインクへ、 使い切った点は暗い赤灰へ。 もともと赤い絵 (エリート) はそのまま。</summary>
        private static Color32 TerritoryTint(Color32 col)
        {
            if (Same(col, NodeInk) || Same(col, NodeHot)) return TerrInk;
            if (Same(col, NodeDone)) return TerrDone;
            return col;
        }

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        /// <summary>嵐の中の点の絵の色 (2026-11-13・仮置き): 明度を落とす (大きさは変えない)。</summary>
        private static Color32 StormDim(Color32 c) => new Color32((byte)(c.r * 9 / 20), (byte)(c.g * 9 / 20), (byte)(c.b * 9 / 20), c.a);

        private string[] IconFor(FreeMapSim sim, int i, out Color32 col)
        {
            var lay = sim.Layout; var t = lay.types[i];
            bool isStart = i == lay.start, isGoal = i == lay.goal;
            // 嵐の層 (2026-11-13) の終点は、 照明・隣・嵐のフェーズ 3 で種別が分かるまで、 ほかの未確認の点と同じ「?」で描く
            bool known = sim.Known[i] || isStart || (isGoal && !lay.storm);
            col = NodeInk;
            if (!known) return IcoUnknown;                                   // 種別が分からない点 = 疑問符 (色は到達済みと同じ)
            // 種別の絵 (使い切った点も同じ絵で、 暗転させるだけ)
            string[] icon; col = NodeInk;
            if (isStart) { col = NodeHot; icon = IcoStart; }
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
                case TileType.Outpost: col = NodeHot; icon = IcoStart; break;
                case TileType.SupplyDepot: icon = IcoDepot; break;
                default: icon = IcoDone; break;
            }
            if (sim.Done[i] && t != TileType.Shop && !isStart && !isGoal) col = NodeDone;      // 使い切った点 = 暗転
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

            // 縄張り (2026-11-12・仮の印): 追跡者が生きている間、 縄張りの点の絵そのものを赤みのインク (TerrInk) で描き、
            //   縄張りの点どうしの道を赤で引き、 縄張りの点に赤の破線の輪を付ける。 点の絵が白いままだと、 道と輪を暗い赤にしても
            //   白い道と「?」の光に負けて見分けにくかった (画面確認 2026-11-12)。 強調は彩度で (明度は上げない・原色は使わない)。
            //   追跡者は縄張りの外へ出ないので、 遊ぶ人が「どこから先が危ないか」を読めるように。 本番の絵は未定 (仮置き)
            bool showTerr = sim.HasFoe && sim.FoeAlive;

            // 航路: 影つきの細線。 点の絵の外側 1 dot より内側には引かない
            foreach (var (a, b) in sim.Roads())
            {
                var A = Sp(lay.pos[a]); var B = Sp(lay.pos[b]);
                float L = Vector2.Distance(A, B); int ma = NodeMargin(sim, a), mb = NodeMargin(sim, b);
                if (L <= ma + mb) continue;
                Vector2 u = (B - A) / L, A2 = A + u * ma, B2 = B - u * mb;
                Line(A2 + Vector2.one, B2 + Vector2.one, _lc.shadow);
                Line(A2, B2, showTerr && sim.InTerritory(a) && sim.InTerritory(b) ? _lc.red : _lc.frameHi);
            }
            if (showTerr)
                foreach (int i in sim.Territory)   // いま効いている縄張り (避ける道のために削った後・2026-11-13)。 形の縄張り (lay.territory) は描かない
                    if (!sim.IsSunk(i)) { var q = Sp(lay.pos[i]); float r = NodeMargin(sim, i) + 1; Ellipse(q, r, r, 0, _lc.red, new[] { 1, 2 }); }

            // 嵐の輪 (2026-11-13・仮の印): 今の安全な円を破線で描く (静か・フェーズ 4 は描かない)。 次の輪は描かない。
            //   二版 (2026-11-13): 崩れ始めた後は、 輪の線と嵐の中の領域を絶えずうごめかせ、 輪が縮む時は 0.5 秒かけて縮む (Game2DMapView.Storm.cs・見た目だけ)
            StormRingFx(sim);

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
                int hops = sim.DetectHops;
                int L = Mathf.Clamp(hops - 2, 0, 2);
                DetectNoise(hops * _hopUnit * _ls, L, L >= 2 ? _lc.red : L >= 1 ? _lc.warn : _lc.mid);
            }

            // 行き先の見込み (カーソルを乗せた点)
            if (_hover >= 0 && _hover != sim.Me && _hover < lay.Count && !sim.IsSunk(_hover))
            {
                var q = Sp(lay.pos[_hover]); bool ok = sim.CanMoveTo(_hover);
                // 〈補給線〉で跳べる点は、 線を引かずに括弧だけ強調する (道を通らない)
                if (!ok && GameLoop.GameManager.SupplyLineUnlocked && sim.CanRelayTo(_hover)) ok = true;
                else if (ok) Line(_me, q, _lc.hot);
                Bracket(q, 6, ok ? _lc.hot : _lc.dim);
                // 嵐の中の点への移動の見積もり (2026-11-13): 払う HP を点の下に出す
                int stormHp = ok ? sim.StormHpFor(_hover) : 0;
                float ly = q.y + 8 * Pxs;
                if (stormHp > 0) { Text("HP-" + stormHp, q.x, ly, _lc.red); ly += Game2DJpFont.Cell + 1; }
                // 不安定化の見積もり (2026-11-13 二版): その点を踏む / 開けると何ポイント溜まるか。 境目を越えるなら「崩れ始める」「フェーズ k へ」
                if (ok && sim.InstabilityMode)
                {
                    string est = InstabilityEstimate(sim, _hover, out string cross);
                    if (est != null) { Text(est, q.x, ly, _lc.warn); ly += Game2DJpFont.Cell + 1; }
                    if (cross != null) Text(cross, q.x, ly, StormHot);
                }
            }

            // 点
            for (int i = 0; i < lay.Count; i++)
            {
                if (sim.IsSunk(i)) continue;
                var q = Sp(lay.pos[i]);
                var ic = IconFor(sim, i, out var col);
                if (ic == IcoExit && !sim.Done[i]) col = BossPulse(q);
                else if (showTerr && sim.InTerritory(i)) col = TerritoryTint(col);
                if (sim.InStorm(i)) col = StormDim(col);   // 嵐の中の点は暗く (2026-11-13)
                Glyph(ic, q.x, q.y, col);
            }

            // 縄張りの宝 (2026-11-12・仮の印): 層に入った時から、 点の種類が見えていなくても「宝」の字だけを点の上に出す。 取ったら消す
            if (sim.TreasureAvailable && !sim.IsSunk(sim.TreasureNode))
            {
                var q = Sp(lay.pos[sim.TreasureNode]);
                Text("宝", q.x, q.y - 6 * Pxs, _lc.warn);
            }

            // 囮罠
            foreach (var lure in sim.Lures)
            {
                var q = Sp(lay.pos[lure.node]);
                if (blink || lure.turnsLeft > 2) Glyph(GlyphPlus, q.x, q.y, blink ? _lc.hot : _lc.mid, Pxs);
            }

            // 〈前哨の旗〉の拠点 (2026-11-11): 見張っている範囲の破線の輪と、 点の上の「旗」の字
            if (sim.Beacon >= 0 && sim.Beacon < lay.Count)
            {
                var q = Sp(lay.pos[sim.Beacon]); float br = sim.Perks.beaconRange * _hopUnit * _ls;
                Ellipse(q, br, br, 0, _lc.mid, new[] { 1, 3 });
                Text("旗", q.x, q.y - 6 * Pxs, _lc.hi);
            }

            // 追跡者
            if (sim.HasFoe && sim.FoeAlive && sim.FoeVisible)
            {
                var q = Sp(lay.pos[sim.FoeNode]);
                Glyph(IcoFoe, q.x, q.y, _lc.red);
                string st = sim.FoeFreezeTurns > 0 ? "停止" : sim.FoeMode == FoeMode.Chase ? "追跡" : sim.FoeMode == FoeMode.Alert ? "警戒" : "巡回";
                Text(st, q.x, q.y - 6 * Pxs, _lc.red);
            }
            // 使い魔
            foreach (var f in sim.VisibleFamiliars())
            {
                var q = Sp(lay.pos[f.pos]);
                Rect(Mathf.FloorToInt(q.x) - (Pxs - 1), Mathf.FloorToInt(q.y) - (Pxs - 1), 2 * Pxs - 1, 2 * Pxs - 1, _lc.red);
            }

            // 戦闘名 (2026-11-05): 種別が見えていて (視界・照明・偵察で発見済み) 戦闘名も開示済みの点は、
            //   カーソルを乗せると戦闘名を出す。 敵の名前は出さない ── 何が待っているかの「当たり」だけを渡す。
            //   名前は 2 体戦のプリセット → 単体戦・ボスの対応表 (EncounterPresets.SoloTitles) の順。 どちらにも無ければ出さない
            //   (FloorManager.EncounterTitle は敵の表示名へ落ちるが、 ここでは敵の名前を出さないので使わない)。
            if (_hover >= 0 && _hover < lay.Count && !sim.IsSunk(_hover))
            {
                string title = EncounterTitleAt(sim, _hover);
                if (!string.IsNullOrEmpty(title))
                {
                    var q = Sp(lay.pos[_hover]);
                    int tw = Game2DJpFont.Width(title), th = Game2DJpFont.Cell;
                    int bx = Mathf.Clamp(Mathf.RoundToInt(q.x - tw / 2f) - 2, 0, W - tw - 4);
                    int by = Mathf.RoundToInt(q.y) - 8 - th - 3;
                    if (by < 0) by = Mathf.RoundToInt(q.y) + 8;   // 上に入らなければ下へ
                    Rect(bx, by, tw + 4, th + 3, _lc.bg);
                    JpText(title, bx + 2, by + 1, _lc.hi);
                }
            }

            Readout(sim);
        }

        /// <summary>その点の戦闘名。 種別が見えていない・戦闘名が未開示・名前の無い戦闘なら null (敵の名前には落とさない)。</summary>
        private static string EncounterTitleAt(FreeMapSim sim, int i)
        {
            if (sim == null || i < 0 || i >= sim.Known.Length || !sim.Known[i]) return null;
            var node = MapSystem.MapManager.Instance?.CurrentMap?.GetNodeByIndex(i);
            if (node == null || !node.encounterRevealed) return null;
            var preset = GameLoop.EncounterPresets.Get(node.encounterId);
            if (preset != null && !string.IsNullOrEmpty(preset.title)) return preset.title;
            return GameLoop.EncounterPresets.SoloTitle(node.encounterEnemyA);
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

        // ── 読み出し (左下: EXIT の方角・縮尺・自艦の格子位置・経過ターン / 上: 天候) ──
        private static readonly string[] Compass = { "北", "北北東", "北東", "東北東", "東", "東南東", "南東", "南南東", "南", "南南西", "南西", "西南西", "西", "西北西", "北西", "北北西" };

        /// <summary>縄張りの宝の知らせの左上 (dot)。 上の帯 (高さ 30・Game2DNavExtras.TopR) の下 4 dot。</summary>
        private const int TreasureNoteX = 96, TreasureNoteTop = 34;

        private void Readout(FreeMapSim sim)
        {
            var lay = sim.Layout; var me = lay.pos[sim.Me]; var ex = lay.pos[lay.goal];
            // 出口のコンパス (16 方向): 出口の点が隠れていても、 方角だけ分かる。 距離は出さない
            //   嵐の層 (2026-11-13) は、 ボスの点の種別が見えるまで方角も出さない (針を描かず「出口 ？」)
            bool exitHidden = lay.storm && !sim.Known[lay.goal];
            float b = (Mathf.Atan2((float)(ex.x - me.x), -(float)(ex.y - me.y)) + Mathf.PI * 2) % (Mathf.PI * 2);
            int sec = Mathf.RoundToInt(b / (Mathf.PI * 2 / 16)) % 16;
            float ang = sec * Mathf.PI * 2 / 16 - Mathf.PI / 2;
            float cx0 = 96 + 118, cy0 = H - 17, Rr = 9;
            for (int k = 0; k < 16; k++) { float ak = k * Mathf.PI * 2 / 16 - Mathf.PI / 2; Put(Mathf.RoundToInt(cx0 + Mathf.Cos(ak) * Rr), Mathf.RoundToInt(cy0 + Mathf.Sin(ak) * Rr), _lc.dim); }
            Rect((int)cx0, (int)(cy0 - Rr - 1), 1, 2, _lc.hi);                                     // 北の目印
            if (!exitHidden)
            {
                Rect(Mathf.RoundToInt(cx0 + Mathf.Cos(ang) * Rr) - 1, Mathf.RoundToInt(cy0 + Mathf.Sin(ang) * Rr) - 1, 3, 3, _lc.hot);
                for (int r = 1; r < Rr - 1; r++) Put(Mathf.RoundToInt(cx0 + Mathf.Cos(ang) * r), Mathf.RoundToInt(cy0 + Mathf.Sin(ang) * r), _lc.dim);   // 針
            }
            Text("出口 " + (exitHidden ? "？" : Compass[sec]), 96, H - 35, _lc.mid, Align.Left);
            Text("縮尺 " + _zoom + "倍", 96, H - 22, _lc.dim, Align.Left);
            char col = (char)('A' + Mathf.Clamp(Mathf.FloorToInt((float)me.x / 2f), 0, 25));
            int row = Mathf.FloorToInt((float)me.y / 2f) + 1;
            Text("現在 " + col + row, 96, H - 9, _lc.dim, Align.Left);
            Text("ターン " + Mathf.Min(999, sim.Turn).ToString("000"), 96 + 8 * 4 * Pxs, H - 9, _lc.dim, Align.Left);      // 「ターン」とは書かない (2026-11-11)

            if (sim.StormMode)
            {
                // 嵐 (2026-11-13): 静かな時間は「嵐まで N」、 嵐の間は「嵐 フェーズ k・次まで N」と種類の名前
                Text(StormLabel(sim), W - 8 - 89, 8, sim.StormPhase == 0 ? _lc.mid : _lc.warn, Align.Right);
            }
            else
            {
                bool calm = !(sim.Weather != WeatherKind.None && !double.IsNegativeInfinity(sim.Front));
                Text("天気 " + (calm ? "凪" : sim.Weather.DisplayName()), W - 8 - 89, 8, calm ? _lc.dim : _lc.hi, Align.Right);
            }
            // 縄張りの宝を取った後 (2026-11-12・仮置き): 何を得たかを左上に出す (航行のまま渡すので、 専用の画面は挟まない)
            //   液晶の上端 (y 0〜29) は x 96〜387 が上の帯 (Game2DNavExtras の TopR / StatusR) に覆われるので、 その真下に、
            //   地図の点や道と重なっても読めるよう地の色の下敷きを敷いて出す (y = 8 だと上の帯の裏で見えなかった)。
            if (sim.TreasureTaken)
            {
                string note = GameLoop.GameManager.Instance?.TerritoryTreasureNote;
                if (!string.IsNullOrEmpty(note))
                {
                    int tw = Game2DJpFont.Width(note), th = Game2DJpFont.Cell;
                    Rect(TreasureNoteX - 2, TreasureNoteTop - 1, tw + 4, th + 2, _lc.bg);
                    Text(note, TreasureNoteX, TreasureNoteTop + th / 2f, _lc.warn, Align.Left);
                }
            }
        }

        /// <summary>嵐の帯の文 (2026-11-13): 「霧 嵐まで 12」/「嵐 フェーズ 2・次まで 4 霧」/「嵐 フェーズ 4 霧」。 種類なしは「嵐」だけ。
        /// 二版 (不安定化の時計): 静かな間は「不安定 7/15」(境目は FreeMapParams.InstabilityPhase1At・あと 8 で崩れ始める)、 崩れ始めたら「嵐 フェーズ k・次まで N」(N は不安定化ポイント)。</summary>
        internal static string StormLabel(FreeMapSim sim)
        {
            int ph = sim.StormPhase, n = sim.StormTurnsToNext;
            string kind = sim.Weather == WeatherKind.None ? "" : " " + Storm.KindName(sim.Weather);
            if (ph == 0 && sim.InstabilityMode) return "不安定 " + sim.Instability + "/" + FreeMapParams.InstabilityPhase1At + kind;
            if (ph == 0) return "嵐まで " + n + kind;
            return ph >= 4 ? "嵐 フェーズ " + ph + kind : "嵐 フェーズ " + ph + "・次まで " + n + kind;
        }

        private static string WxCode(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Fog: return "FOG";
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

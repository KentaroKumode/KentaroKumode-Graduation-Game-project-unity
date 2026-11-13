using GameLoop;
using MapSystem.FreeMove;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 嵐の輪の見た目 (2026-11-13 二版・GAME.md §5-12)。 <b>見た目だけ実時間で動き、 規則は行動で進む</b>:
    /// 輪の判定 (<see cref="FreeMapSim.InStorm"/>) は論理の位置 (不安定化ポイント / 手番で決まる) だけを使い、 ここで揺らした線や縮む途中の輪は判定に入らない。
    ///
    /// <para>・崩れ始めた後 (フェーズ 1〜4): 輪の線を揺らし (半径の揺らぎ・破線の流れ・彩度の明滅)、 嵐の側に加算の粒を散らす。
    /// コマ送りは 8 コマ/秒 (このプロジェクトの決まり)。 フェーズが進むほど揺らぎを大きく・速く・粒を多く。 静かな間は描かない (対比)。<br/>
    /// ・輪が縮む時 (累計が増えた・手番が進んだ): 新しい輪へ <see cref="ShrinkSec"/> 秒かけて動く。 縮む間も操作は止めない。<br/>
    /// ・乱数は見た目専用のハッシュ (<see cref="Hash01"/>) で、 GameRng を引かない。 BOT が動かしている間・バッチでは揺らさず、 論理の輪をそのまま描く。<br/>
    /// ・大きさの状態表現に localScale を使わない (液晶のバッファへ 1 dot ずつ打つ)。 新しい絵は無い (線と点だけ)。</para>
    /// </summary>
    public partial class Game2DMapView
    {
        /// <summary>輪が新しい位置へ縮む時間 (秒)。</summary>
        private const float ShrinkSec = 0.5f;
        /// <summary>見た目のコマ送り (コマ/秒)。</summary>
        private const float StormFps = 8f;

        /// <summary>輪の線の明滅の色: 警告の橙 (<c>_lc.warn</c> #d9a24a) と同じくらいの明るさで、 彩度を上げた橙 (明度は上げない・原色ではない)。</summary>
        private static readonly Color32 StormHot = Hex("#e8902c");
        /// <summary>嵐の側の粒の色 (加算で重ねる・暗い橙)。</summary>
        private static readonly Color32 StormGrain = Hex("#3a2410");

        // ── 縮む動き (見た目の輪) ──
        private FreeMapSim _ringSim;
        private bool _ringHas;
        private Vector2 _ringFromC, _ringToC; private float _ringFromR, _ringToR, _ringT0 = -1e9f;

        /// <summary>見た目を動かさない時 (BOT が動かしている・バッチ)。</summary>
        private static bool StormStatic => GameHooks.BotDriving || Application.isBatchMode;

        /// <summary>輪の線と嵐の中の領域を描く (<c>Plot</c> から・点より奥)。</summary>
        private void StormRingFx(FreeMapSim sim)
        {
            if (!sim.StormMode) { _ringSim = null; return; }
            int phase = sim.StormPhase;
            bool has = sim.StormRing(out var c, out double r);
            var toC = has ? new Vector2((float)c.x, (float)c.y) : Vector2.zero; float toR = has ? (float)r : 0f;
            if (StormStatic)
            {
                _ringSim = null;
                if (has) { float rr = toR * _ls; Ellipse(Sp(c), rr, rr, 0, _lc.warn, new[] { 3, 2 }); }
                return;
            }
            // 行き先が変わったら、 今見えている輪から新しい輪へ縮む動きを始める (層が変わった・初めて崩れた時は、 全部を含む輪から)
            if (_ringSim != sim) { _ringSim = sim; _ringHas = has; _ringFromC = _ringToC = toC; _ringFromR = _ringToR = toR; _ringT0 = -1e9f; }
            else if (has && (!_ringHas || (toC - _ringToC).sqrMagnitude > 1e-8f || Mathf.Abs(toR - _ringToR) > 1e-5f))
            {
                if (!_ringHas)
                {
                    var m = Storm.MapCenter; _ringFromC = new Vector2((float)m.x, (float)m.y); _ringFromR = (float)Storm.FullRadius(sim.Layout);
                }
                else ShownRing(out _ringFromC, out _ringFromR);
                _ringToC = toC; _ringToR = toR; _ringT0 = _now; _ringHas = true;
            }
            else if (!has && _ringHas && phase == 4)
            {
                // フェーズ 4: 輪はボスの点へ消えるように縮む
                ShownRing(out _ringFromC, out _ringFromR);
                var b = sim.Layout.pos[sim.Layout.goal];
                _ringToC = new Vector2((float)b.x, (float)b.y); _ringToR = 0f; _ringT0 = _now; _ringHas = false;
            }
            else if (!has && phase == 0) { _ringHas = false; _ringT0 = -1e9f; }   // 静かに戻った (遠ざけた): 描かない

            if (phase == 0) return;                      // 静かな間は動かさない (対比)
            int frame = Mathf.FloorToInt(_now * StormFps);
            bool tweening = _now - _ringT0 < ShrinkSec;
            ShownRing(out var sc, out float sr);
            bool drawRing = has || tweening;
            // 嵐の側の粒 (加算): 見えている輪の外だけ。 フェーズ 4 は (ボスの点の周りを除く) 全面
            StormGrainFx(sim, phase, frame, drawRing ? Sp(sc.x, sc.y) : new Vector2(-1e4f, -1e4f), drawRing ? sr * _ls : 0f);
            if (drawRing && sr > 0.01f) WobbleRing(Sp(sc.x, sc.y), sr * _ls, phase, frame);
        }

        /// <summary>いま見えている輪 (縮む途中なら補間・終わったら行き先)。 補間は ease-out。</summary>
        private void ShownRing(out Vector2 c, out float r)
        {
            float u = Mathf.Clamp01((_now - _ringT0) / ShrinkSec);
            u = 1f - (1f - u) * (1f - u);
            c = Vector2.Lerp(_ringFromC, _ringToC, u); r = Mathf.Lerp(_ringFromR, _ringToR, u);
        }

        /// <summary>揺らぐ輪の線: 半径に 2 本の正弦と粒のゆらぎを足した折れ線。 破線はコマごとに流れ、 一部の区間が彩度の高い橙に明滅する。</summary>
        private void WobbleRing(Vector2 q, float R, int phase, int frame)
        {
            float amp = phase >= 3 ? 2.6f : phase == 2 ? 1.6f : 0.9f;          // 揺らぎの大きさ (dot)
            float sp = phase >= 3 ? 1.9f : phase == 2 ? 1.25f : 0.7f;           // 1 コマで動く量
            int n = Mathf.Max(48, Mathf.CeilToInt(Mathf.PI * 2 * R / 2f));
            int lx = int.MinValue, ly = int.MinValue, dash = frame % 5;
            for (int k = 0; k <= n; k++)
            {
                float a = (float)k / n * Mathf.PI * 2;
                float w = amp * (0.6f * Mathf.Sin(5 * a + frame * 0.7f * sp) + 0.4f * Mathf.Sin(9 * a - frame * 1.1f * sp + 1.3f))
                        + (Hash01(k, frame) - 0.5f) * amp * 0.5f;
                int x = Mathf.FloorToInt(q.x + Mathf.Cos(a) * (R + w)), y = Mathf.FloorToInt(q.y + Mathf.Sin(a) * (R + w));
                if (x == lx && y == ly) continue;
                lx = x; ly = y; dash++;
                if (dash % 5 >= 3) continue;                                    // 破線 (3 描いて 2 空ける) がコマごとに流れる
                bool hot = Hash01(k / 6, frame + 17) < (phase >= 3 ? 0.45f : 0.25f);
                Put(x, y, hot ? StormHot : _lc.warn);
            }
        }

        /// <summary>嵐の側の粒 (加算合成)。 見えている輪 (中心 q・半径 R dot) の外にだけ置く。 フェーズが進むほど多い。</summary>
        private void StormGrainFx(FreeMapSim sim, int phase, int frame, Vector2 q, float R)
        {
            int count = phase >= 4 ? 420 : phase == 3 ? 320 : phase == 2 ? 220 : 130;
            var boss = Sp(sim.Layout.pos[sim.Layout.goal]);
            float r2 = R * R, keep2 = 14f * 14f;
            for (int k = 0; k < count; k++)
            {
                int x = Mathf.FloorToInt(Hash01(k, frame) * W), y = Mathf.FloorToInt(Hash01(k + 7919, frame) * H);
                float dx = x - q.x, dy = y - q.y;
                if (dx * dx + dy * dy <= r2) continue;                          // 輪の中には置かない
                float bx = x - boss.x, by = y - boss.y;
                if (bx * bx + by * by <= keep2) continue;                        // ボスの点は常に嵐の外
                PutAdd(x, y, StormGrain);
                if (phase >= 3 && Hash01(k, frame + 3) < 0.3f) PutAdd(x + 1, y, StormGrain);
            }
        }

        /// <summary>下の色に足して置く (発光は加算・飽和は 255 で止める)。</summary>
        private void PutAdd(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int i = (H - 1 - y) * W + x; var d = _buf[i];
            _buf[i] = new Color32((byte)Mathf.Min(255, d.r + c.r), (byte)Mathf.Min(255, d.g + c.g), (byte)Mathf.Min(255, d.b + c.b), 255);
        }

        /// <summary>見た目専用のハッシュ (0..1)。 GameRng を引かない。</summary>
        private static float Hash01(int i, int frame) => Rnd01(i * 31 + 977, frame * 13 + 5);

        // =====================================================================
        //  不安定化の見積もり (カーソルを乗せた点)
        // =====================================================================

        /// <summary>点 i を踏む / 開けると溜まる不安定化ポイントの見積もり (「不安定 +3」・戦闘やイベントは「+1〜」のように下限)。 溜まらなければ null。
        /// <paramref name="cross"/> = それで境目を越える時の印 (「崩れ始める」「フェーズ 2 へ」・下限で越える時だけ)。</summary>
        private static string InstabilityEstimate(FreeMapSim sim, int i, out string cross)
        {
            cross = null;
            if (sim == null || !sim.InstabilityMode || i < 0 || i >= sim.Layout.Count) return null;
            int add = sim.CanMoveTo(i) ? sim.InstabilityForStep(i) : 0;
            bool more = false;
            if (!sim.Done[i] && i != sim.Layout.goal)
            {
                if (sim.Known[i])
                {
                    var node = MapSystem.MapManager.Instance?.CurrentMap?.GetNodeByIndex(i);
                    var t = node != null ? node.EffectiveType : sim.Layout.types[i];
                    add += Instability.ArrivalPoints(t, out more);
                }
                else more = true;                                                // 種別が分からない点は下限 (歩く分) だけ
            }
            if (sim.TreasureAvailable && i == sim.TreasureNode) add += Instability.PointsOf(InstabilitySource.Treasure);
            if (add <= 0 && !more) return null;
            int ph0 = Instability.PhaseOf(sim.Instability), ph1 = Instability.PhaseOf(sim.Instability + add);
            if (ph1 > ph0) cross = ph0 == 0 ? "崩れ始める" : "フェーズ " + ph1 + " へ";
            return "不安定 +" + add + (more ? "〜" : "");
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>武器の姿勢 (x, y は回転軸が来る画面の位置)。 HTML の pose。</summary>
    public sealed class Pose
    {
        public float x, y, rot, glow, sy = 1, bend, a = 1, yel = 1, vis = 1;
        public bool charge, big; public Seg seg;
        public Pose Copy() => (Pose)MemberwiseClone();
    }

    /// <summary>動きの 1 区間 (HTML の segs の要素)。</summary>
    public sealed class Seg { public float t0, d; public Func<float, Pose> pose; public bool trail, fade; }

    /// <summary>着弾 (HTML の hits の要素)。</summary>
    public sealed class Hit { public float t, frac, ang; public string kind; public bool fs = true; public float[] off; /// <summary>true = ang をそのまま斬撃痕の向きに使う (刃先の速度から測らない)。</summary>
        public bool angFixed; }

    public sealed class Plan
    {
        public List<Seg> segs = new List<Seg>(); public List<Hit> hits = new List<Hit>();
        public float total; public string fam; public bool flip;
    }

    /// <summary>
    /// 武器の攻撃モーション (docs/design/battle-prototype.src.html の <c>plan / curve / poseAt / tipOf / weaponCenter</c>)。
    /// 動きは「止まらない 1 本の軌跡」: キーの姿勢を単調な C1 スプライン (Fritsch–Carlson) で結ぶ。
    /// 剣 = 横一文字 / 斧 = 叩きつけ / 盾 = チャージ → 叩きつけ / 短刀 = 2 回 (会心は複数回) / 竜閃 = 会心だけの一刀両断。
    /// </summary>
    public static class BattleMotion
    {
        public const int W = 480, FloorY = 204, EX = 240, EY = 140, WSC = 2;
        public static readonly Vector2 Rest = new Vector2(214, 202);

        private struct Key { public float t; public float[] v; }          // v = x, y, rot, glow, sy, bend
        private static Key K(float t, float x, float y, float rot, float glow, float bend = 0) => new Key { t = t, v = new[] { x, y, rot, glow, 1f, bend } };

        /// <summary>
        /// 剣の振り抜き (0 〜 0.25 秒) の鍵。 <b>刃先</b>の道筋を滑らかな弧で決め、 そこから軸 (握り) の位置を逆算する。
        /// 刃先の x は終わりへ向けて減速 (べき 3.2)、 y は x の 1.25 乗で下へ向かい、 角度は x の進みに合わせて滑らかに回る。
        /// align = true (会心) は、 刃の向きを刃先の進む向き (道筋の接線) にそろえる (刃が進行方向を指し、 握りが後ろから追う)。
        /// </summary>
        private static Key SweepKey(float t, Func<float, float> X, float rotAdd, float glow, float keyTime = -1, bool align = false)
        {
            float T = 0.25f, p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / T), 3.2f);
            float tx = 47 + 426 * p, ty = 48 + 156 * Mathf.Pow(p, 1.25f), rot = align ? Mathf.Atan2(156f * 1.25f * Mathf.Pow(p, 0.25f), 426f) * 57.29578f + 45f : 316 + 154 * Ease(p), th = rot * Mathf.PI / 180f, c = Mathf.Cos(th), sn = Mathf.Sin(th);
            float px = tx - (52 * c + 52 * sn), py = ty - (52 * sn - 52 * c);
            return K(keyTime < 0 ? t : keyTime, X(px), py, rot + rotAdd, glow);
        }

        /// <summary>振り抜きの弧の、 時刻 t での進行方向 (度・画面で右 = 0、 下 = +90)。</summary>
        private static float SweepHeading(float t)
        {
            float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.25f), 3.2f);
            return Mathf.Atan2(156f * 1.25f * Mathf.Pow(p, 0.25f), 426f) * 57.29578f;
        }

        private static float SweepGlow(float t) => t <= 0.05f ? 1f : t < 0.08f ? Lerp(1f, 0.7f, (t - 0.05f) / 0.03f) : t < 0.15f ? Lerp(0.7f, 0.4f, (t - 0.08f) / 0.07f) : t < 0.2f ? Lerp(0.4f, 0.15f, (t - 0.15f) / 0.05f) : 0.15f;

        private static float Ease(float t) => t * t * (3 - 2 * t);
        private static float EaseIn(float t) => t * t;
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Tang(List<Key> k, int f, int i)
        {
            int n = k.Count;
            Func<int, float> sl = j => (k[j + 1].v[f] - k[j].v[f]) / (k[j + 1].t - k[j].t);
            if (i == 0) return sl(0); if (i == n - 1) return sl(n - 2);
            float d0 = sl(i - 1), d1 = sl(i); if (d0 * d1 <= 0) return 0;
            float h0 = k[i].t - k[i - 1].t, h1 = k[i + 1].t - k[i].t, w1 = 2 * h1 + h0, w2 = h1 + 2 * h0;
            return (w1 + w2) / (w1 / d0 + w2 / d1);
        }

        private static Pose Curve(List<Key> k, float t)
        {
            int n = k.Count, i = 0; while (i < n - 2 && t > k[i + 1].t) i++;
            var k0 = k[i]; var k1 = k[i + 1]; float h = k1.t - k0.t, u = Mathf.Clamp01((t - k0.t) / h), u2 = u * u, u3 = u2 * u;
            var o = new float[6];
            for (int f = 0; f < 6; f++)
            {
                float p0 = k0.v[f], p1 = k1.v[f], m0 = Tang(k, f, i), m1 = Tang(k, f, i + 1);
                o[f] = (2 * u3 - 3 * u2 + 1) * p0 + (u3 - 2 * u2 + u) * h * m0 + (-2 * u3 + 3 * u2) * p1 + (u3 - u2) * h * m1;
            }
            return new Pose { x = o[0], y = o[1], rot = o[2], glow = o[3], sy = o[4], bend = o[5] };
        }

        /// <summary>絵の中心の画面位置 (集光・光だまりの基準)。 軸 → 中心のベクトルを姿勢の角度で回す。</summary>
        public static Vector2 WeaponCenter(Pose p, string fam, bool flip)
        {
            if (fam == "shield") return new Vector2(p.x, p.y);
            float vx = (16 - (fam == "axe" ? 29 : 3)) * WSC, vy = (16 - 29) * WSC, th = p.rot * Mathf.PI / 180f, c = Mathf.Cos(th), sn = Mathf.Sin(th), fl = flip ? -1 : 1;
            return new Vector2(p.x + fl * (vx * c - vy * sn), p.y + vx * sn + vy * c);
        }

        public static Vector2 TipOf(Pose q, bool flip)
        {
            float vx = 26 * WSC, vy = -26 * WSC, th = q.rot * Mathf.PI / 180f, c = Mathf.Cos(th), sn = Mathf.Sin(th);
            return new Vector2(q.x + (flip ? -1 : 1) * (vx * c - vy * sn), q.y + (vx * sn + vy * c) * (q.sy <= 0 ? 1 : q.sy));
        }

        /// <summary>斬る瞬間に刃先が進む向き (度)。</summary>
        public static float TipAngle(Plan A, float t)
        {
            var a = PoseAt(A, Mathf.Max(0, t - 0.006f)); var b = PoseAt(A, t + 0.006f);
            var pa = TipOf(a, A.flip); var pb = TipOf(b, A.flip);
            return Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * 180f / Mathf.PI;
        }

        public static Pose PoseAt(Plan A, float t)
        {
            for (int i = 0; i < A.segs.Count; i++)
            {
                var sg = A.segs[i];
                if (t < sg.t0 + sg.d || i == A.segs.Count - 1)
                {
                    float u = Mathf.Clamp01((t - sg.t0) / sg.d);
                    if (sg.fade)
                    {
                        var last = A.segs[i - 1].pose(1f).Copy();
                        last.a = 1 - u; last.yel = 1; last.glow = last.glow * (1 - u); last.charge = false; return last;
                    }
                    var pz = sg.pose(u); pz.seg = sg; return pz;
                }
            }
            return null;
        }

        public static Plan MakePlan(string fam, bool crit, bool flip)
        {
            var P = new Plan { fam = fam, flip = flip };
            float t0 = 0;
            Action<float, Func<float, Pose>, bool, bool> add = (d, pose, trail, fade) => { P.segs.Add(new Seg { t0 = t0, d = d, pose = pose, trail = trail, fade = fade }); t0 += d; };
            float t00 = t0, dur = 0; var keys = new List<Key>(); bool custom = false;
            float chFrom = -1, chTo = -1; bool chBig = false;

            if (fam == "sword")
            {
                // 剣: 画面の左 (右) 端付近から、 横一文字に斬る。 軸 (握り) を端から敵へ走らせながら、 刃を後ろ上がりの構えから水平まで時計回りに振る
                Func<float, float> X = x => flip ? W - x : x; float Y = 134, st = 46, r0 = 290;
                if (!crit)
                {
                    // 刃先の道筋は 1 本の滑らかな弧 (SweepKey)。 以前は鍵の姿勢を手で並べていて、 刃先の傾きが途中で一度増えて戻り、 波打って見えた
                    float ti = 0.05f;
                    for (int i = 0; i <= 20; i++) { float t = i * 0.0125f; keys.Add(SweepKey(t, X, 0, SweepGlow(t))); }
                    dur = keys[keys.Count - 1].t; P.hits.Add(new Hit { t = t00 + ti, frac = 1, kind = "chop", ang = 0 });
                }
                else
                {
                    // 会心: 逆回転 (反時計回り) に二回転 → 回りきったその場で一拍のタメ → 時計回りに一気に振り抜く (振り抜きは通常と同じ弧)
                    float B = -720, L0 = 0.46f, TM = L0 + 0.07f, t1 = TM, ti = t1 + 0.001f;
                    keys.Add(K(0, X(st), Y - 12, r0, 0.7f)); keys.Add(K(0.14f, X(st), Y - 22, r0 - 250, 0.85f, 8)); keys.Add(K(0.3f, X(st), Y - 12, r0 - 640, 0.95f, 8));
                    keys.Add(K(L0, X(st), Y - 12, r0 + B, 1, 2)); keys.Add(K(TM, X(st), Y - 12, r0 + B, 1.2f));
                    for (int i = 0; i <= 16; i++) { float tau = i * 0.0125f, ts = 0.05f + tau; keys.Add(SweepKey(ts, X, B, SweepGlow(ts), ti + tau, true)); }
                    keys.Add(SweepKey(0.25f, X, B, 0.05f, ti + 1, true));      // 剣は透明になった後も、 会心エフェクトまで姿勢だけ保つ
                    dur = keys[keys.Count - 1].t;
                    // 斬撃痕の向き = その時の刃の向き (振り抜きの弧の接線)。 刃先の速度から測ると、 構えから振り抜きへの 0 フレームの跳びを測って向きがずれる
                    float aCut = SweepHeading(0.05f), aEnd = SweepHeading(0.25f);
                    P.hits.Add(new Hit { t = t00 + ti, frac = 0, kind = "cut", ang = flip ? 180 - aCut : aCut, angFixed = true }); P.hits.Add(new Hit { t = t00 + ti + 1, frac = 1, kind = "chop", ang = flip ? 180 - aEnd : aEnd, angFixed = true });
                }
            }
            else if (fam == "axe")
            {
                // 軸を持ち上げながら回り (会心だけ 2 回)、 反らして溜め、 軸を支点に大きく振り切る (切り下し)
                int rev = crit ? 2 : 0; float swing = rev * 360;
                float back = crit ? swing - 60 : swing, hitRot = swing + 135;
                if (!crit) { back = -85; hitRot = 135; }                                 // 通常: 構えを大きく振り上げ (刃が後ろ上へ -85°) → 時計回りに 220° 振り下ろす
                float tSpin = crit ? 0.50f : 0.22f, tBack = tSpin + (crit ? 0.2f : 0.12f);
                float fillRot = rev > 0 ? 0 : back, r1 = rev > 0 ? back * 0.5f : back, r2 = rev > 0 ? back * 0.93f : back;
                keys.Add(K(0, Rest.x, Rest.y, fillRot, 0.5f)); keys.Add(K(tSpin * 0.5f, 232, 150, r1, 0.7f)); keys.Add(K(tSpin, 240, 116, r2, 0.8f)); keys.Add(K(tBack, 244, 98, back, 0.9f));
                if (crit) { keys.Add(K(tBack + 0.32f, 241, 94, back + 6, 1.1f)); keys.Add(K(tBack + 0.64f, 239, 91, back + 12, 1.25f)); chFrom = tBack; chTo = tBack + 0.64f; chBig = true; }
                float tb = keys[keys.Count - 1].t, tImpact = tb + 0.12f, dr = hitRot - back;
                keys.Add(K(tb + (tImpact - tb) * 0.55f, 246, 98, back + dr * 0.6f, 1)); keys.Add(K(tImpact, 246, 100, hitRot, 1)); keys.Add(K(tImpact + 0.09f, 246, 103, hitRot + 26, 0.8f));
                // 斧の絵は左右を鏡写しにして使う (軸は右下)。 刃が時計回りの振りの先に来る (背のほうで殴らない)。 鏡写しで絵の向きが 90° ずれるので、 角度に 90° 足す
                for (int i = 0; i < keys.Count; i++) { var kk = keys[i]; kk.v[2] += 90; keys[i] = kk; }
                dur = keys[keys.Count - 1].t; P.hits.Add(new Hit { t = t00 + tImpact, frac = 1, kind = "chop", ang = 90 });
            }
            else if (fam == "shield")
            {
                // チャージ → 叩きつける: 持ち上げ、 溜めの間もじわじわ上がり続け、 速度を保ったまま叩きつけて、 そのまま押し込む
                float tc = crit ? 0.95f : 0.55f;
                keys.Add(K(0, 240, 176, 0, 0.5f)); keys.Add(K(0.2f, 240, 112, 0, 0.7f)); keys.Add(K(tc, 240, 98, 0, crit ? 1.3f : 0.9f)); keys.Add(K(tc + 0.085f, 240, 142, 0, 1.1f)); keys.Add(K(tc + 0.15f, 240, 150, 0, 0.8f));
                chFrom = 0.18f; chTo = tc; chBig = crit; dur = tc + 0.15f; P.hits.Add(new Hit { t = t00 + tc + 0.085f, frac = 1, kind = "slam", ang = 90 });
            }
            else if (fam == "dragon")
            {
                // 竜閃 (会心のみ): 剣は出さず、 細い光の筋が真横に走って敵を一刀両断 → 遅れて一斉にヒット
                custom = true;
                add(0.85f, u => new Pose { vis = 0 }, false, false);
                P.hits.Add(new Hit { t = t00 + 0.7f, frac = 1, kind = "dragon", ang = 0 }); dur = 0;
            }
            else
            {
                // 短刀: 軌跡は切れ目なく往復する (素早く 2 回 / 会心は複数回)
                float[][] L = { new float[] { 130, 40, 176, 76 }, new float[] { 350, 40, 304, 76 }, new float[] { 170, 30, 205, 92 }, new float[] { 320, 30, 285, 92 }, new float[] { 150, 200, 215, 163 } };
                int n = crit ? 5 : 2; float sl = crit ? 0.075f : 0.1f, gap = crit ? 0.03f : 0.05f, t = 0; custom = true;
                float[][] OFFS = { new float[] { -30, -20, 16 }, new float[] { 28, -8, -16 }, new float[] { -12, 20, 14 }, new float[] { 24, 24, -14 }, new float[] { 0, -2, 12 } };
                for (int i = 0; i < n; i++)
                {
                    float sx = L[i][0], sy = L[i][1], ex = L[i][2], ey = L[i][3], ang = Mathf.Atan2(ey - sy, ex - sx) * 180f / Mathf.PI, sr = ang + 45, er = ang + 45;   // 刃は進行方向と平行 (回転 = 進む向き + 45°)
                    add(sl, u => new Pose { x = Lerp(sx, ex, EaseIn(u)), y = Lerp(sy, ey, EaseIn(u)), rot = Lerp(sr, er, u), glow = 1, a = u < 0.12f ? u / 0.12f : u > 0.75f ? (1 - u) / 0.25f : 1 }, false, false);   // 出て → 斬って → 消える (独立)
                    var off = OFFS[crit ? i : (i == 0 ? 0 : 1)];
                    if (crit) P.hits.Add(new Hit { t = t00 + t + sl * 0.55f, frac = 0, kind = "cut", ang = ang, fs = i == n - 1, off = off });      // 会心: 各斬撃は薄い痕だけ
                    else P.hits.Add(new Hit { t = t00 + t + sl * 0.55f, frac = 1f / n, kind = "slash", ang = ang, off = off });
                    t += sl;
                    if (i < n - 1) { add(gap, u => new Pose { x = ex, y = ey, rot = er, vis = 0 }, false, false); t += gap; }
                }
                if (crit) P.hits.Add(new Hit { t = t00 + t, frac = 1, kind = "finish", ang = 0 });     // 最後の斬撃が終わった瞬間に、 蓄積した全部の痕がヒット
                dur = 0;
            }

            if (!custom)
            {
                float d = dur, cf = chFrom, ct = chTo; bool cb = chBig;
                add(d, u => { var p = Curve(keys, u * d); p.charge = cf >= 0 && u * d >= cf && u * d <= ct; p.big = cf >= 0 && cb; return p; }, true, false);
            }
            add(0.05f, u => null, false, true);
            P.total = t0;
            return P;
        }

    }
}

using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>出目の面の絵と、 振る動き (2026-11-15・作者の案)。
    ///
    /// <para><b>面の絵</b>: 欠け・しみ・むらのある象牙色の板 (18×18・左右対称に点が並ぶ) に、 丸い点で目を打つ。 1〜9 まで。
    /// 元は gui2d/battle_ui/die.png を取った 17×17 + 右と下の影。 影があると点が板の中心から半ドットずれ、 回すとぶれるので、
    /// 2026-11-16 に影をやめて 18×18 へ広げた (作者「右辺と下辺にシェーディング」「左右対称じゃない」)。</para>
    ///
    /// <para><b>振る動き</b>: 立体にしない。 板が平面の上で回り、 <b>90 度回るたびに、 持っている面のどれかへ目が替わる</b>。
    /// だんだん遅くなり、 最後の 90 度は止まる目そのものが回り込んで止まる (止まる瞬間に目は替わらない)。
    /// 回っている間は、 角が通った跡の細い線で回る向きを見せる (<see cref="Render"/>)。
    /// 回る板は Ports の板の上に 1 個ずつ重ねて出す (<see cref="TexSize"/> 四方・ドットの格子に乗せる)。 止まったら、 いつもの 18×18 の絵に戻る。</para></summary>
    public static class Game2DDiceRoll
    {
        public const int Face = 18;
        /// <summary>回す絵の、 1 ドットあたりの画素 (1 = ドットの格子に乗せる)。</summary>
        public const int K = 1;
        /// <summary>回っている板が届く範囲 (中心から・dot)。 18 の対角の半分 12.7 より大きく。</summary>
        public const int Reach = 14;
        public const int TexSize = Reach * 2 * K;

        // 動きの値 (見本 Captures/dice_roll/final_d・作者が選んだ組み合わせ・2026-11-16)
        /// <summary>90 度の回数。</summary>
        public const int Quarters = 7;
        /// <summary>最初のダイスが止まるまで (秒)。</summary>
        public const float Dur = 0.8f;
        /// <summary>後のダイスほど遅く止まる (秒・4 個目からは同じ)。</summary>
        public const float Stagger = 0.125f;
        /// <summary>減速の強さ。</summary>
        public const float Ease = 2.2f;
        /// <summary>コマ/秒。 作風の決まりは 8 だが、 この振りは作者の指定で 24 (2026-11-16)。</summary>
        public const int Fps = 24;
        /// <summary>尾の長さを決める時間 (秒)。 この間に回った角度が尾の長さ。</summary>
        public const float TailSec = 0.05f;
        /// <summary>最後のこの回数の 90 度だけ、 本当の目をそのまま見せる。 それより前 (速い間) は、 中央の点を濃さの変化で見せる (<see cref="Render"/>)。</summary>
        public const int SlowQuarters = 2;
        public static float DurOf(int i) => Dur + Stagger * Mathf.Clamp(i, 0, 3);

        // ── 面の絵 ──
        // 板の絵 (18×18): a・d・f = 暗い欠け / e・g・i = 灰色の欠け / b = 板 / c = しみ / h = むら。 上と左の辺の欠けは元の板の物、 面のしみとむらは 2026-11-16 に足した
        private static readonly Color32[] Pal =
        {
            new Color32(38, 40, 39, 255), new Color32(211, 202, 176, 255), new Color32(198, 187, 159, 255), new Color32(43, 44, 40, 255), new Color32(108, 111, 109, 255),
            new Color32(38, 43, 39, 255), new Color32(130, 135, 131, 255), new Color32(209, 202, 179, 255), new Color32(109, 116, 112, 255),
        };
        private static readonly string[] Plate =
        {
            "abbcbbbbbbcdabeeea",
            "bhhbbbhhcbbbbbbbbb",
            "bhbbcchhhbbbbbbbbb",
            "bbbbcchhhbbbbhhbbb",
            "bbbbbbbbbbbbbhbbbb",
            "bbbbbbbbbbbbbhhhbb",
            "ebbbbbbbbbbbcchhgb",
            "hcbbbbbbbccccbhhbb",
            "bcbbbbbbbbbbbbbbbb",
            "bccbbbbbbbbbbbbbbb",
            "cbbbbbbbbbbbbbbbbb",
            "hccccbbhcccbbbbbbb",
            "egbbbbbhcbbbbhbbbb",
            "ibbbhhhhhbbbhhhbbb",
            "ibbbbhbbbbbbbhhhbb",
            "ebhbbbbbbbbbhhbhbb",
            "ebhhbbbbbbhhchbbbb",
            "abbbbbbbbbbbbbbbba",
        };
        private static readonly int[][] Pips =
        {
            new int[0], new[] { 4 }, new[] { 0, 8 }, new[] { 0, 4, 8 }, new[] { 0, 2, 6, 8 }, new[] { 0, 2, 4, 6, 8 },
            new[] { 0, 2, 3, 5, 6, 8 }, new[] { 0, 2, 3, 4, 5, 6, 8 }, new[] { 0, 1, 2, 3, 5, 6, 7, 8 }, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 },
        };
        /// <summary>点 (4×4) の左上。 2・7・12 で、 左右の余白が 2 ドットずつ・真ん中の点の中心が板の中心 (9, 9)。</summary>
        private static readonly int[] PipAt = { 2, 7, 12 };
        private static readonly Color32 PipCol = new Color32(38, 40, 39, 255), PipHi = new Color32(121, 126, 122, 255);
        private static readonly Dictionary<int, Color32[]> _faces = new Dictionary<int, Color32[]>();

        private static bool HasCenter(int v) => v >= 1 && v < Pips.Length && System.Array.IndexOf(Pips[v], 4) >= 0;

        /// <summary>出目 v (1〜9) の面の絵 (18×18・上の行から)。 enemy = 敵の出目 (赤みへ寄せる)。 範囲の外の v は点の無い板。</summary>
        public static Color32[] FaceImg(int v, bool enemy = false)
        {
            int key = v * 2 + (enemy ? 1 : 0);
            if (_faces.TryGetValue(key, out var a)) return a;
            a = new Color32[Face * Face];
            for (int y = 0; y < Face; y++)
                for (int x = 0; x < Face; x++)
                {
                    var c = Pal[Plate[y][x] - 'a'];
                    if (enemy) c = new Color32((byte)(c.r * 0.95f), (byte)(c.g * 0.84f), (byte)(c.b * 0.9f), 255);
                    a[y * Face + x] = c;
                }
            if (v >= 1 && v < Pips.Length)
                foreach (int k in Pips[v])
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++)
                        {
                            if ((x == 0 || x == 3) && (y == 0 || y == 3)) continue;      // 角を落として丸く
                            a[(PipAt[k / 3] + y) * Face + PipAt[k % 3] + x] = x == 2 && y == 2 ? PipHi : PipCol;      // 右下に 1 dot の照り
                        }
            _faces[key] = a; return a;
        }

        /// <summary>止まっている出目を板の絵へ描く (左上 x, y・18×18)。</summary>
        public static void DrawFace(KitPix p, int x, int y, int v, bool enemy = false)
        {
            var a = FaceImg(v, enemy);
            for (int j = 0; j < Face; j++) for (int i = 0; i < Face; i++) p.Set(x + i, y + j, a[j * Face + i]);
        }

        // ── 振る動き ──

        /// <summary>90 度の区切りごとの目の列を作る (長さ <see cref="Quarters"/> + 1)。 最後が止まる目 final、 その前は faces からランダム (同じ目を続けない)。
        /// 先頭は使わない (回り始めから、 次に正面へ来る目を見せるため・<see cref="Pose"/>)。
        /// 見た目だけの乱数なので UnityEngine.Random を使う (GameRng は引かない)。</summary>
        public static int[] MakeSeq(int[] faces, int final)
        {
            var seq = new int[Quarters + 1]; seq[Quarters] = final; int next = final;
            for (int k = Quarters - 1; k >= 0; k--)
            {
                int v = next;
                for (int tries = 0; tries < 8; tries++)
                {
                    v = faces != null && faces.Length > 0 ? faces[Random.Range(0, faces.Length)] : Random.Range(1, 7);
                    if (v != next || (faces != null && faces.Length == 1)) break;
                }
                seq[k] = v; next = v;
            }
            return seq;
        }

        /// <summary>時刻 tt の姿。 v = 見せる目、 ang = 角度、 q = いま何回目の 90 度か (0 始まり・止まったら <see cref="Quarters"/>)。
        /// 90 度ごとに「次に正面へ来る目」を、 90 度手前から回し入れる。 最後の 90 度は止まる目そのものが回るので、 止まる瞬間に目が替わらない
        /// (2026-11-16・作者「減速中のダイスから一瞬で出目が違うのに切り替わる」)。</summary>
        private static void Pose(int[] seq, float tt, float dur, out int v, out float ang, out int q)
        {
            float k = Mathf.Clamp01(tt / dur); float turned = Turned(tt, dur);
            q = Mathf.Min(Quarters, Mathf.FloorToInt(turned / (Mathf.PI * 0.5f) + 1e-5f));
            if (k >= 1f) { v = seq[seq.Length - 1]; ang = 0f; q = Quarters; return; }
            v = seq[Mathf.Min(q + 1, seq.Length - 1)]; ang = turned - (q + 1) * Mathf.PI * 0.5f;
        }

        // 4×4 の並べ方 (ベイヤー)
        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        /// <summary>尾の長さの上限 (度)。</summary>
        public const float MaxSpanDeg = 80f;
        /// <summary>尾の太さ (dot) と、 細くなり方 (0 = 太さ一定)。 太い先細りの帯は「リボンを振り回している」ように見えたので、 1 ドットの線にした (2026-11-16・作者)。</summary>
        public const float TailWidth = 1.2f, TailTaper = 0f;
        /// <summary>回っている板の半分 (dot)。</summary>
        private const float PlateHalf = Face * 0.5f;
        private static readonly Color32 TailHead = new Color32(211, 202, 176, 255), TailBody = new Color32(130, 135, 131, 255);
        // 速い間の、 中央の点の濃さ 3 段 (薄い・中・濃い)。 それぞれ 点の色・照りの色。 板と尾にある色だけ
        private static readonly Color32[] CenterBody = { new Color32(130, 135, 131, 255), new Color32(108, 111, 109, 255), new Color32(38, 40, 39, 255) };
        private static readonly Color32[] CenterHi = { new Color32(198, 187, 159, 255), new Color32(130, 135, 131, 255), new Color32(121, 126, 122, 255) };

        private static float Turned(float tt, float dur) { float k = Mathf.Clamp01(tt / dur); return Quarters * Mathf.PI * 0.5f * (1f - Mathf.Pow(1f - k, Ease)); }

        /// <summary>振っている 1 コマを dst (<see cref="TexSize"/> 四方・Texture2D の並び = 下の行から) へ描く。 t = 振り始めからの秒 (コマに丸めた値)、 dur = このダイスが止まるまでの秒。
        /// 見本は Captures/dice_roll/final_d (作者が選んだ組み合わせ・2026-11-16)。
        ///
        /// <para><b>板</b>: 回した正方形を式で切り出す (辺がまっすぐ・中心は板の真ん中)。 中は、 点の無い板の絵 (欠け・しみ) を取る。 時計回り。</para>
        /// <para><b>点</b>: 板と一緒に回して取り直さない。 丸いので、 回した先の位置へ、 正位置の点の絵をそのまま押す。
        /// 回して格子に乗せ直すと、 45 度付近で 4×4 の点が風車の形に崩れた (作者「やばいシンボル」)。 照りの位置も動かない。</para>
        /// <para><b>中央の点</b>: 板の中心にあって動かないので、 目が替わると同じ場所で点いたり消えたりだけが見える。 速い間 (最後の <see cref="SlowQuarters"/> 回より前) は消さず、
        /// 照りを保ったまま濃さを 3 段で動かす ── 中央の点がある目なら濃い方へ、 無い目なら薄い方へ、 1 コマに 1 段ずつ。</para>
        /// <para><b>尾</b> (回る向きの流れ): 4 つの角それぞれから、 回ってきた側へ、 角が通った円に沿って伸びる細い線。
        /// 外側は、 そのコマで描いた板のいちばん外のドットの半径で切る (角が描く円の外へ出さない)。
        /// 長さは速さに比例 (<see cref="TailSec"/> の間に回った角度・上限 <see cref="MaxSpanDeg"/>)。
        /// 色は 象牙 → 灰 → 無し の 3 段で、 <b>段の境目だけディザ</b> (ベイヤー)。 混色・半透明なし。 止まると消える。</para></summary>
        public static void Render(Color32[] dst, int[] seq, float t, float dur, bool ccw)
        {
            int n = TexSize, r = Reach;
            Pose(seq, t, dur, out int v, out float ang, out int quarterNo);
            float span = Mathf.Min(Turned(t, dur) - Turned(t - TailSec, dur), MaxSpanDeg * Mathf.Deg2Rad);
            float c0 = Mathf.Cos(-ang), s0 = Mathf.Sin(-ang); var f = FaceImg(0); const float quarter = Mathf.PI * 0.5f;
            // 板。 ついでに、 描いたドットのいちばん外の半径を取る (尾の外側の線)
            float rc = 0f;
            for (int py = -r; py < r; py++)
                for (int px = -r; px < r; px++)
                {
                    float X = ccw ? -(px + 0.5f) : px + 0.5f, Y = py + 0.5f; int o = (n - 1 - (py + r)) * n + (px + r);
                    float lx = X * c0 - Y * s0, ly = X * s0 + Y * c0;
                    if (Mathf.Max(Mathf.Abs(lx), Mathf.Abs(ly)) > PlateHalf) { dst[o] = new Color32(0, 0, 0, 0); continue; }
                    int u = Mathf.Clamp(Mathf.FloorToInt(lx + PlateHalf), 0, Face - 1), w = Mathf.Clamp(Mathf.FloorToInt(ly + PlateHalf), 0, Face - 1);
                    dst[o] = f[w * Face + u]; rc = Mathf.Max(rc, Mathf.Sqrt(X * X + Y * Y));
                }
            // 尾
            if (span >= 0.05f)
                for (int py = -r; py < r; py++)
                    for (int px = -r; px < r; px++)
                    {
                        int o = (n - 1 - (py + r)) * n + (px + r); if (dst[o].a > 0) continue;
                        float X = ccw ? -(px + 0.5f) : px + 0.5f, Y = py + 0.5f;
                        float rho = Mathf.Sqrt(X * X + Y * Y), phi = Mathf.Atan2(Y, X);
                        float d = Mathf.Repeat(ang + Mathf.PI * 0.25f - phi, quarter);      // 回る向きの先にある角からの遅れ
                        if (d > span) continue;
                        float q = d / span, wd = TailTaper > 0f ? TailWidth * Mathf.Pow(1f - q, TailTaper) : TailWidth;
                        if (rho > rc || rho < rc - wd) continue;
                        float th = (Bayer[((py + r) & 3) * 4 + ((px + r) & 3)] + 0.5f) / 16f;
                        if (q < 0.22f) dst[o] = TailHead;
                        else if (q < 0.45f) dst[o] = (0.45f - q) / 0.23f > th ? TailHead : TailBody;
                        else if (q < 0.68f) dst[o] = TailBody;
                        else if ((1f - q) / 0.32f > th) dst[o] = TailBody;
                    }
            // 点
            bool fast = quarterNo < Quarters - SlowQuarters; int lv = 2;
            if (fast)
            {
                // 濃さは、 振り始めからコマを順にたどって決める (1 コマに 1 段)。 コマ数は多くて 30 ほど
                Pose(seq, 0f, dur, out int v0, out _, out _); lv = HasCenter(v0) ? 2 : 0;
                int frames = Mathf.RoundToInt(t * Fps);
                for (int i = 1; i <= frames; i++)
                {
                    Pose(seq, (float)i / Fps, dur, out int vi, out _, out _); int target = HasCenter(vi) ? 2 : 0;
                    lv += target > lv ? 1 : target < lv ? -1 : 0;
                }
            }
            if (v < 1 || v >= Pips.Length) return;
            bool drewCenter = false;
            foreach (int k in Pips[v]) { StampPip(dst, k, c0, s0, ccw, k == 4 ? lv : 2); if (k == 4) drewCenter = true; }
            if (fast && !drewCenter) StampPip(dst, 4, c0, s0, ccw, lv);
        }

        /// <summary>点 1 個を、 回した先の位置へ正位置の絵のまま押す。 lv = 濃さ (2 = いつもの色)。</summary>
        private static void StampPip(Color32[] dst, int k, float c0, float s0, bool ccw, int lv)
        {
            int n = TexSize, r = Reach;
            float lx = PipAt[k % 3] + 2 - PlateHalf, ly = PipAt[k / 3] + 2 - PlateHalf;
            float X = lx * c0 + ly * s0, Y = -lx * s0 + ly * c0; if (ccw) X = -X;
            int ox = Mathf.FloorToInt(X - 2f + 0.5f), oy = Mathf.FloorToInt(Y - 2f + 0.5f);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    if ((x == 0 || x == 3) && (y == 0 || y == 3)) continue;
                    int px = ox + x + r, py = oy + y + r; if (px < 0 || py < 0 || px >= n || py >= n) continue;
                    dst[(n - 1 - py) * n + px] = x == 2 && y == 2 ? CenterHi[lv] : CenterBody[lv];
                }
        }
    }
}

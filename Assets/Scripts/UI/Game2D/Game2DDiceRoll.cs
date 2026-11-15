using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>出目の面の絵と、 振る動き (2026-11-15・作者の案)。
    ///
    /// <para><b>面の絵</b>: 欠けのある象牙色の板 (元は gui2d/battle_ui/die.png を 18×18 に取った物) に、 丸い点で目を打つ。 1〜9 まで。</para>
    ///
    /// <para><b>振る動き</b>: 立体にしない。 板が平面の上で回り、 <b>90 度回るたびに、 持っている面のどれかへ目が替わる</b>。
    /// だんだん遅くなり、 最後の 90 度で止まる目になる。 回っている間のブラーは、 色を混ぜずにディザで出す (<see cref="Render"/>)。
    /// 回す絵は <b>ドットの格子に乗せ直さない</b> (作者の決定・1 ドット = <see cref="K"/> 画素の板を、 画素の細かさで回す) ので、
    /// 板の絵 (KitPix) ではなく、 細かい絵 (<see cref="TexSize"/> 四方) を別に重ねて出す。 止まったら、 いつもの 18×18 の絵に戻る。</para></summary>
    public static class Game2DDiceRoll
    {
        public const int Face = 18;
        /// <summary>回す絵の、 1 ドットあたりの画素。</summary>
        public const int K = 4;
        /// <summary>回っている板が届く範囲 (中心から・dot)。 18 の対角の半分 12.7 より大きく。</summary>
        public const int Reach = 14;
        public const int TexSize = Reach * 2 * K;

        // 動きの値 (見本 Captures/dice_roll/v4/index.html の初めの値)
        /// <summary>90 度の回数。</summary>
        public const int Quarters = 7;
        /// <summary>最初のダイスが止まるまで (秒)。</summary>
        public const float Dur = 0.8f;
        /// <summary>後のダイスほど遅く止まる (秒・4 個目からは同じ)。</summary>
        public const float Stagger = 0.125f;
        /// <summary>減速の強さ。</summary>
        public const float Ease = 2.2f;
        public const int Fps = 12;
        /// <summary>ブラーの長さ (コマ)。</summary>
        public const float Blur = 0.6f;
        /// <summary>ブラーの重ね数。</summary>
        public const int Samples = 16;

        public static float DurOf(int i) => Dur + Stagger * Mathf.Clamp(i, 0, 3);

        // ── 面の絵 ──
        // 板の絵: 見本 (Captures の die_faces_style) と同じ取り方。 '.' = 透明 (上の 1 行と左の 1 列は空き・右と下に影の縁)
        private static readonly Color32[] Pal =
        {
            new Color32(38, 40, 39, 255), new Color32(211, 202, 176, 255), new Color32(198, 187, 159, 255), new Color32(43, 44, 40, 255), new Color32(108, 111, 109, 255),
            new Color32(38, 43, 39, 255), new Color32(130, 135, 131, 255), new Color32(209, 202, 179, 255), new Color32(109, 116, 112, 255),
        };
        private static readonly string[] Plate =
        {
            "..................",
            ".abbcbbbbbcdabeeea",
            ".bbbbbbbbbbbbbbbba",
            ".bbbbbbbbbbbbbbbbf",
            ".bbbbbbbbbbbbbbbba",
            ".bbbbbbbbbbbbbbbba",
            ".bbbbbbbbbbbbbbbbf",
            ".ebbbbbbbbbbbbbbgf",
            ".hbbbbbbbbbbbbbbba",
            ".bbbbbbbbbbbbbbbba",
            ".cbbbbbbbbbbbbbbba",
            ".hbbbbbbbbbbbbbbbf",
            ".egbbbbbbbbbbbbbba",
            ".ibbbbbbbbbbbbbbba",
            ".ibbbbbbbbbbbbbbbf",
            ".ebhbbbbbbbbbbbbba",
            ".ebhhbbbbbbbcbbbba",
            ".afffdfaaaaadaaaab",
        };
        private static readonly int[][] Pips =
        {
            new int[0], new[] { 4 }, new[] { 0, 8 }, new[] { 0, 4, 8 }, new[] { 0, 2, 6, 8 }, new[] { 0, 2, 4, 6, 8 },
            new[] { 0, 2, 3, 5, 6, 8 }, new[] { 0, 2, 3, 4, 5, 6, 8 }, new[] { 0, 1, 2, 3, 5, 6, 7, 8 }, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 },
        };
        private static readonly int[] PipAt = { 2, 7, 12 };
        private static readonly Color32 PipCol = new Color32(38, 40, 39, 255), PipHi = new Color32(121, 126, 122, 255);
        private static readonly Dictionary<int, Color32[]> _faces = new Dictionary<int, Color32[]>();

        /// <summary>出目 v (1〜9) の面の絵 (18×18・上の行から)。 enemy = 敵の出目 (赤みへ寄せる)。 範囲の外の v は点の無い板。</summary>
        public static Color32[] FaceImg(int v, bool enemy = false)
        {
            int key = v * 2 + (enemy ? 1 : 0);
            if (_faces.TryGetValue(key, out var a)) return a;
            a = new Color32[Face * Face];
            for (int y = 0; y < Face; y++)
                for (int x = 0; x < Face; x++)
                {
                    if (Plate[y][x] == '.') { a[y * Face + x] = new Color32(0, 0, 0, 0); continue; }
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
            for (int j = 0; j < Face; j++) for (int i = 0; i < Face; i++) if (a[j * Face + i].a > 0) p.Set(x + i, y + j, a[j * Face + i]);
        }

        // ── 振る動き ──

        /// <summary>90 度の区切りごとの目の列を作る (長さ <see cref="Quarters"/> + 1)。 最後が止まる目 final、 その前は faces からランダム (同じ目を続けない)。
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

        private static void Pose(int[] seq, float tt, float dur, out int v, out float ang)
        {
            float k = Mathf.Clamp01(tt / dur); float total = Quarters * Mathf.PI * 0.5f;
            float turned = total * (1f - Mathf.Pow(1f - k, Ease));
            int q = Mathf.Min(Quarters, Mathf.FloorToInt(turned / (Mathf.PI * 0.5f) + 1e-5f));
            ang = k >= 1f ? 0f : turned - q * Mathf.PI * 0.5f;
            v = seq[Mathf.Min(q, seq.Length - 1)];
        }

        // 4×4 の並べ方 (ベイヤー)。 ドットの格子 (1 ドット = K 画素) ごとに 1 つの値
        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        private static readonly int[] _pv = new int[Samples]; private static readonly float[] _pc = new float[Samples], _ps = new float[Samples];

        /// <summary>振っている 1 コマを dst (<see cref="TexSize"/> 四方・Texture2D の並び = 下の行から) へ描く。 t = 振り始めからの秒 (コマに丸めた値)、 dur = このダイスが止まるまでの秒。
        ///
        /// <para><b>ブラーは色を混ぜない</b> (作風・2026-11-15 作者): 1 コマの間の姿を <see cref="Samples"/> 個用意し、 ドットごとに「そのうちのどれを見せるか」をベイヤーの並びで決める。
        /// 出る色は板の絵の色だけ・半透明なし。 速い所ほど姿がばらけてディザの流れに見え、 止まると 1 枚の絵に揃う。</para></summary>
        public static void Render(Color32[] dst, int[] seq, float t, float dur, bool ccw)
        {
            int n = TexSize; float h = Face * 0.5f; int r = Reach * K;
            for (int s = 0; s < Samples; s++)
            {
                float tt = t - (Blur / Fps) * (s / (float)(Samples - 1));
                Pose(seq, tt, dur, out _pv[s], out float ang); if (ccw) ang = -ang;
                _pc[s] = Mathf.Cos(-ang); _ps[s] = Mathf.Sin(-ang);
            }
            for (int py = -r; py < r; py++)
                for (int px = -r; px < r; px++)
                {
                    int dx = (px + r) / K, dy = (py + r) / K; int s = Bayer[(dy & 3) * 4 + (dx & 3)] * Samples / 16;
                    float x = (px + 0.5f) / K, y = (py + 0.5f) / K;
                    int u = Mathf.FloorToInt(x * _pc[s] - y * _ps[s] + h), w = Mathf.FloorToInt(x * _ps[s] + y * _pc[s] + h);
                    int o = (n - 1 - (py + r)) * n + (px + r);
                    if (u < 0 || w < 0 || u >= Face || w >= Face) { dst[o] = new Color32(0, 0, 0, 0); continue; }
                    dst[o] = FaceImg(_pv[s])[w * Face + u];
                }
        }
    }
}

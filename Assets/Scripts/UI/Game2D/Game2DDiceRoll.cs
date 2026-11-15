using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>出目が立体で転がる絵 (2026-11-15)。 立方体を 1 コマずつドットへ直接描く (画素ごとに光線を当てる・縮小も補間もしない)。
    ///
    /// <para><b>面は実行時に貼る</b>: 面の絵 (正面の出目の絵と同じ <see cref="Face"/>×<see cref="Face"/>) を、 そのダイスの面の一覧から取る。
    /// 面の数は 6 とは限らない (出目パーツは 2〜9 面・<see cref="GameLoop.DiceFaceParts"/>) ので、 <b>形はいつも立方体</b>にして、
    /// 裏へ回った面の絵を一覧の次の面へ差し替える。 同時に見える面は多くて 3 つなので、 面が何枚でも同じ作りで回り、 回っている間に持っている面が順に出る。
    /// 最後のコマは、 止まった面が正面を向いた向き (呼ぶ側はそこからいつもの出目の絵を描く)。</para>
    ///
    /// <para>1 秒 8 コマ (<see cref="Fps"/>)。 大きさは変えない (回転で見える幅が変わるだけ)。 明るさは面ごとに 3 段。</para></summary>
    public static class Game2DDiceRoll
    {
        public const int Fps = 8;
        /// <summary>面の絵の 1 辺 (正面の出目の絵と同じ)。</summary>
        public const int Face = 18;
        /// <summary>回っている間に絵が届く範囲の半分 (中心から)。</summary>
        public const int Reach = 16;

        // 回す軸 (ダイスごとに変える) と、 回る量 (周)
        private static readonly Vector3[] Axes = { new Vector3(1f, 0.55f, 0.25f), new Vector3(-0.6f, 1f, 0.3f), new Vector3(1f, -0.45f, -0.3f), new Vector3(0.5f, 1f, -0.25f) };
        // 立方体の面: 0 = +z (正面で止まる面)・1 = -z・2 = +x・3 = -x・4 = +y・5 = -y
        private static readonly Vector3[] Normals = { new Vector3(0, 0, 1), new Vector3(0, 0, -1), new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, -1, 0) };

        private static Quaternion Pose(int frame, int frames, int seed, out int hop)
        {
            float t = frames <= 1 ? 1f : Mathf.Clamp01(frame / (float)(frames - 1));
            float e = 1f - Mathf.Pow(1f - t, 2.2f);
            float turns = 1.3f + 0.15f * (seed % 3);
            hop = -Mathf.RoundToInt(3f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * (1f - t));
            return Quaternion.AngleAxis((1f - e) * turns * 360f, Axes[((seed % Axes.Length) + Axes.Length) % Axes.Length].normalized);
        }

        /// <summary>そのコマで、 立方体の 6 面がそれぞれ一覧の何番目の面を見せるか。 面は裏へ回るたびに次の面へ替わり、 最後のコマで正面の面が landed になる。</summary>
        private static void Contents(int frame, int frames, int seed, int faceCount, int landed, int[] into)
        {
            // 視点は +z 側。 面が見える = 回した後の法線の z が正
            var passAt = new int[6]; var total = new int[6]; var vis = new bool[6];
            for (int f = 0; f < frames; f++)
            {
                var q = Pose(f, frames, seed, out _);
                for (int s = 0; s < 6; s++)
                {
                    bool v = (q * Normals[s]).z > 0.02f;
                    if (f > 0 && vis[s] && !v) total[s]++;      // 見えていた面が裏へ回った
                    vis[s] = v;
                    if (f == frame) passAt[s] = total[s];
                }
            }
            for (int s = 0; s < 6; s++)
            {
                int end = s == 0 ? landed : landed + s;      // 最後のコマで見せる面 (正面 = 止まった面・ほかは一覧の続き)
                into[s] = (((end - (total[s] - passAt[s])) % faceCount) + faceCount) % faceCount;
            }
        }

        /// <summary>転がる 1 コマを描く。 faces = 面の絵 (<see cref="Face"/>×<see cref="Face"/>・上の行から・透明は縁の色で埋める) の一覧、
        /// landed = 止まる面の添字、 frame / frames = 今のコマ / 全コマ (最後のコマは正面)、 seed = ダイスの番号 (軸と回る量を変える)。</summary>
        public static void Draw(KitPix p, int cx, int cy, IList<Color32[]> faces, int landed, int frame, int frames, int seed)
        {
            if (p == null || faces == null || faces.Count == 0) return;
            var slot = new int[6]; Contents(frame, frames, seed, faces.Count, landed, slot);
            var q = Pose(frame, frames, seed, out int hop); var qi = Quaternion.Inverse(q);
            var d = qi * new Vector3(0, 0, -1); float h = Face * 0.5f; var light = new Vector3(-0.35f, 0.55f, 0.76f);      // 光は左上の手前 (y は上が正)
            int n = Reach * 2; var hit = new Color32[n * n]; var on = new bool[n * n];
            for (int py = 0; py < n; py++)
                for (int px = 0; px < n; px++)
                {
                    // 画面は y が下向き・立体は y が上向き
                    var o = qi * new Vector3(px + 0.5f - Reach, -(py + 0.5f - Reach - hop), 100f);
                    float t0 = -1e9f, t1 = 1e9f; int ax = -1, sg = 0; bool miss = false;
                    for (int k = 0; k < 3; k++)
                    {
                        float dk = d[k], ok = o[k];
                        if (Mathf.Abs(dk) < 1e-6f) { if (Mathf.Abs(ok) > h) { miss = true; break; } continue; }
                        float a = (-h - ok) / dk, b = (h - ok) / dk; int s = a < b ? -1 : 1;
                        if (a > b) { float w = a; a = b; b = w; }
                        if (a > t0) { t0 = a; ax = k; sg = s; }
                        if (b < t1) t1 = b;
                    }
                    if (miss || ax < 0 || t0 > t1) continue;
                    var pt = o + d * t0; int fi = ax == 2 ? (sg > 0 ? 0 : 1) : ax == 0 ? (sg > 0 ? 2 : 3) : (sg > 0 ? 4 : 5);
                    // 面の中の位置 (0..Face-1)。 正面の面は u = x・v = 上から
                    float u, v;
                    if (ax == 2) { u = sg > 0 ? pt.x : -pt.x; v = -pt.y; }
                    else if (ax == 0) { u = sg > 0 ? -pt.z : pt.z; v = -pt.y; }
                    else { u = pt.x; v = sg > 0 ? pt.z : -pt.z; }
                    int iu = Mathf.Clamp(Mathf.FloorToInt(u + h), 0, Face - 1), iv = Mathf.Clamp(Mathf.FloorToInt(v + h), 0, Face - 1);
                    var img = faces[slot[fi]]; var c = img != null && img.Length >= Face * Face ? img[iv * Face + iu] : Game2DKit.Ivory;
                    if (c.a < 128) c = Game2DKit.Dark;
                    float lum = Vector3.Dot(q * Normals[fi], light); float m = lum > 0.62f ? 1f : lum > 0.3f ? 0.72f : 0.5f;
                    hit[py * n + px] = new Color32((byte)(c.r * m), (byte)(c.g * m), (byte)(c.b * m), 255); on[py * n + px] = true;
                }
            for (int py = 0; py < n; py++)
                for (int px = 0; px < n; px++)
                {
                    int i = py * n + px;
                    if (on[i]) { p.Set(cx - Reach + px, cy - Reach + py, hit[i]); continue; }
                    bool edge = (px > 0 && on[i - 1]) || (px < n - 1 && on[i + 1]) || (py > 0 && on[i - n]) || (py < n - 1 && on[i + n]);
                    if (edge) p.Set(cx - Reach + px, cy - Reach + py, Game2DKit.Dark);
                }
        }
    }
}

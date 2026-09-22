using System.Collections.Generic;
using System.Text;
using CombatSystem;
using UnityEditor;
using UnityEngine;

namespace GameEditor
{
    /// <summary>
    /// ADR-0010 Phase 1 の検算。 <see cref="YachtRoles"/> が
    /// docs/design-yacht.md のダイス表を再現するかを全数列挙で確かめる。
    ///
    /// **設計表は Python で先に計算した値**なので、 C# 実装がそれと一致しなければ
    /// どちらかが間違っている。 数値をいじる前に「実装が設計どおりか」を先に固定する。
    /// </summary>
    public static class YachtRolesVerify
    {
        private struct DiceDef
        {
            public string name;
            public int tier;
            public int[] faces;
            public DiceDef(string n, int t, int[] f) { name = n; tier = t; faces = f; }
        }

        // ADR-0010 柱5 のダイス 10 種
        private static readonly DiceDef[] Dice =
        {
            new DiceDef("木",   1, new[] { 1, 2, 3, 4, 5, 6 }),
            new DiceDef("拾",   1, new[] { 1, 2, 3, 4, 5 }),
            new DiceDef("鉄",   2, new[] { 2, 3, 4, 5, 6, 7 }),
            new DiceDef("奇",   2, new[] { 1, 3, 5, 7, 9 }),
            new DiceDef("磐",   2, new[] { 3, 4, 5, 6, 7 }),
            new DiceDef("星",   3, new[] { 3, 4, 5, 6, 7, 8 }),
            new DiceDef("偶",   3, new[] { 2, 4, 6, 8, 10 }),
            new DiceDef("無銘", 3, new[] { 4, 5, 6, 7, 8, 9 }),
            new DiceDef("竜",   4, new[] { 5, 6, 7, 8, 9, 10 }),
            new DiceDef("天",   4, new[] { 2, 4, 6, 8, 10, 12, 14 }),
        };

        private const int DiceCount = 5;

        [MenuItem("Tools/AutoRun/検算: 役の成立率 (ADR-0010 Phase1)", priority = 15)]
        public static void Verify()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 役の成立率 検算 (ADR-0010 Phase 1) ===");
            sb.AppendLine($"{DiceCount} 個・全数列挙。 docs/design-yacht.md のダイス表と突き合わせること。");
            sb.AppendLine();
            sb.AppendLine("| T | 名 | 平均 | 対 | 二対 | 束 | 大束 | 極 | 満 | 散 | 小階 | 中階 | 大階 | 飛階 | 偶 | 奇 |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            var order = new[]
            {
                RoleKind.Pair, RoleKind.TwoPair, RoleKind.Triple, RoleKind.Quad, RoleKind.Yacht,
                RoleKind.FullHouse, RoleKind.AllDifferent, RoleKind.SmallRun, RoleKind.MediumRun,
                RoleKind.LargeRun, RoleKind.SkipRun, RoleKind.AllEven, RoleKind.AllOdd,
            };

            foreach (var d in Dice)
            {
                var hit = new Dictionary<RoleKind, long>();
                foreach (var k in order) hit[k] = 0;
                long total = 0, sum = 0;

                var hand = new int[DiceCount];
                var found = new List<RoleKind>(8);
                Enumerate(d.faces, hand, 0, ref total, ref sum, hit, found);

                sb.Append($"| T{d.tier} | **{d.name}** | {(double)sum / total / DiceCount:F1} |");
                foreach (var k in order)
                {
                    double pct = 100.0 * hit[k] / total;
                    // 極だけ 3 桁 (0.077% の桁を落とさないため)
                    sb.Append(k == RoleKind.Yacht ? $" {pct:F3} |"
                            : k == RoleKind.Quad || k == RoleKind.LargeRun ? $" {pct:F2} |"
                            : $" {pct:F1} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("※ 端子役 (対/束/小階/飛階/偶/奇/散) は **「作れるか」** で測っている ──");
            sb.AppendLine("   プレイヤーはどのダイスを同じ端子へ置くか選べるので、 部分集合を全走査して");
            sb.AppendLine("   1 つでも成立するなら成立とする。 「5個すべてを 1 端子」で測ると");
            sb.AppendLine("   〈偶〉が 3.1% になり、 実際に狙える頻度 (50%) と 16 倍ずれる。");
            sb.AppendLine("※ 手札役 (二対/大束/極/満/中階/大階) は 5 個全体で判定。 配線に依存しない。");
            sb.AppendLine("※ 配線役 (均/相殺/拮抗) は敵の数値に依存するのでここでは測れない。");
            sb.AppendLine("※ 〈無銘〉は端子役が成立しない設定だが、 ここでは面の素の確率を出している。");

            // **ファイルへ出す。** Unity のコンソールは複数行ログの 1 行目しか
            // MCP 経由で取り出せず、 表の照合ができないため。
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "AutoRunLogs");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "yacht_roles_verify.txt");
            System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
            Debug.Log($"[役の検算] 出力: {path}");
        }

        /// <summary>faces^DiceCount を全数列挙して各役の成立数を数える。</summary>
        private static void Enumerate(int[] faces, int[] hand, int depth,
                                      ref long total, ref long sum,
                                      Dictionary<RoleKind, long> hit, List<RoleKind> found)
        {
            if (depth == hand.Length)
            {
                total++;
                for (int i = 0; i < hand.Length; i++) sum += hand[i];

                found.Clear();
                YachtRoles.EvaluateHand(hand, found);

                // **端子役は「作れるか」で測る。** プレイヤーはどのダイスを同じ端子へ置くかを
                //   選べるので、 手札に偶数が 3 個あれば その 3 個を 1 端子に集めて〈偶〉を作れる。
                //   「5個すべてを 1 端子に置いた場合」で測ると〈偶〉が 3.1% になり、
                //   実際に狙える頻度 (50%) と 16 倍ずれる。 部分集合の全走査で判定する。
                CollectFormableTerminalRoles(hand, found);

                for (int i = 0; i < found.Count; i++)
                    if (hit.ContainsKey(found[i])) hit[found[i]]++;
                return;
            }
            for (int f = 0; f < faces.Length; f++)
            {
                hand[depth] = faces[f];
                Enumerate(faces, hand, depth + 1, ref total, ref sum, hit, found);
            }
        }

        /// <summary>手札の**部分集合を全走査**して、 1 端子に集めれば成立させられる端子役を集める。
        /// 5 個なら 32 通りなので全探索で足りる。</summary>
        private static void CollectFormableTerminalRoles(int[] hand, List<RoleKind> into)
        {
            int n = hand.Length;
            var buf = new int[n];
            var sub = new List<RoleKind>(8);

            for (int mask = 1; mask < (1 << n); mask++)
            {
                int m = 0;
                for (int i = 0; i < n; i++)
                    if ((mask & (1 << i)) != 0) buf[m++] = hand[i];

                var group = new int[m];
                System.Array.Copy(buf, group, m);

                sub.Clear();
                YachtRoles.EvaluateTerminal(group, sub);
                for (int i = 0; i < sub.Count; i++)
                    if (!into.Contains(sub[i])) into.Add(sub[i]);
            }
        }
    }
}

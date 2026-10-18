using System.Text;
using System.Collections.Generic;
using CombatSystem;
using GameLoop;
using UnityEditor;
using UnityEngine;

namespace GameEditor
{
    /// <summary>
    /// ADR-0011 の検算。 <see cref="YachtRoles.Evaluate"/> が、 ADR の確率表
    /// (面数 n に対する 2 個そろい・ゾロ目の成立率) を全数列挙で再現するかを確かめる。
    ///
    /// 設計表は先に計算した値なので、 C# 実装がそれと一致しなければどちらかが間違っている。
    /// 数値をいじる前に「実装が設計どおりか」を先に固定する。 全ての面を T4 として評価する
    /// (Tier による成立可否は <see cref="YachtRoles.Evaluate"/> の別の話で、 ここは確率の検算)。
    /// </summary>
    public static class YachtRolesVerify
    {
        private const int DiceCount = 3;

        [MenuItem("Tools/AutoRun/検算: 役の成立率 (ADR-0011)", priority = 15)]
        public static void Verify()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 役の成立率 検算 (ADR-0011) ===");
            sb.AppendLine($"{DiceCount} 個・全数列挙。 全ての面を T4 として評価。 振り直しは全部振り直しを 2 回まで (計 3 回振る)。");
            sb.AppendLine();
            sb.AppendLine("| 面数 n | 何かそろう | ゾロ目(どれか) | 特定の目の2個そろい | 特定の目のゾロ目 | 2個そろい(3回) | ゾロ目(3回) |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");

            for (int n = 2; n <= 9; n++)
            {
                // 面は先頭 n 個 (2,3,4,5,1,6,7,8,9 の順)。 確率は面数だけで決まるので並びは検算に影響しない。
                var order = new[] { 2, 3, 4, 5, 1, 6, 7, 8, 9 };
                var parts = new List<DiceFaceParts.Part>(n);
                for (int i = 0; i < n; i++) parts.Add(new DiceFaceParts.Part(order[i], DiceFaceParts.Tier.T4));
                DiceFaceParts.Build(parts, out int[] faces, out DiceFaceParts.Tier[] tiers);

                long total = 0, any = 0, anyTriple = 0, pairOfFirst = 0, tripleOfFirst = 0;
                var hand = new int[DiceCount];
                var idx = new int[DiceCount];
                var found = new List<RoleKind>(4);
                int first = faces[0];
                Enumerate(faces, tiers, hand, idx, 0, found, ref total, ref any, ref anyTriple, ref pairOfFirst, ref tripleOfFirst, first);

                double pAny = (double)any / total, pTriple = (double)anyTriple / total;
                double pPair1 = (double)pairOfFirst / total, pTri1 = (double)tripleOfFirst / total;
                double r3(double p) => 1.0 - System.Math.Pow(1.0 - p, 3);
                sb.AppendLine($"| {n} | {100 * pAny:F1}% | {100 * pTriple:F2}% | {100 * pPair1:F1}% | {100 * pTri1:F2}% "
                            + $"| {100 * r3(pPair1):F1}% | {100 * r3(pTri1):F1}% |");
            }

            sb.AppendLine();
            sb.AppendLine("※ 期待値: ゾロ目(どれか) = 1/n²、 特定の目のゾロ目 = 1/n³、 特定の目のちょうど 2 個 = 3(1/n)²(1−1/n)。");
            sb.AppendLine("※ 2 個そろいは「ちょうど 2 個」。 3 個そろった手は T3 以上ならゾロ目、 T2 のままなら 2 個そろい扱い。");

            string dir = System.IO.Path.Combine(Application.dataPath, "..", "AutoRunLogs");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "yacht_roles_verify.txt");
            System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
            Debug.Log($"[役の検算] 出力: {path}");
        }

        private static void Enumerate(int[] faces, DiceFaceParts.Tier[] tiers, int[] hand, int[] idx, int depth,
                                      List<RoleKind> found, ref long total, ref long any, ref long anyTriple,
                                      ref long pairOfFirst, ref long tripleOfFirst, int firstFace)
        {
            if (depth == hand.Length)
            {
                total++;
                found.Clear();
                YachtRoles.Evaluate(hand, idx, tiers, found);
                if (found.Count > 0) any++;
                for (int i = 0; i < found.Count; i++)
                {
                    if (YachtRoles.IsTriple(found[i])) anyTriple++;
                    if (found[i] == YachtRoles.Of(firstFace, false)) pairOfFirst++;
                    if (found[i] == YachtRoles.Of(firstFace, true)) tripleOfFirst++;
                }
                return;
            }
            for (int f = 0; f < faces.Length; f++)
            {
                hand[depth] = faces[f];
                idx[depth] = f;
                Enumerate(faces, tiers, hand, idx, depth + 1, found, ref total, ref any, ref anyTriple,
                          ref pairOfFirst, ref tripleOfFirst, firstFace);
            }
        }
    }
}

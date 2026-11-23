using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary><b>群 × 届いた深さの倍率 (2026-11-22・ハイブリッド案)。</b>
    ///
    /// <para>ITT の品ごとの Δ (細かい値) に、 群ごと・取得の時期ごとの倍率 (粗い値) を掛けて BOT の序列に使う。
    /// 倍率は ITT の同じランから当て直す:</para>
    /// <code>logit h_k = α_k + Σ_i (β_i + γ_{群(i), 時期(g_i)}) · [g_i ≤ FloorOfLevel[k]]</code>
    /// <para>時期 = 付与層 1 → 深さ 1、 2・3 → 深さ 3、 4・5 → 深さ 5 (<c>Run.Depth &gt;= 付与層</c> で配る)。
    /// γ は深さ 1・3 の付与に群ごとの仮想の列を足して当てる (深さ 5 が基準 = 0)。 β・γ・α を同時に IRLS で当てる
    /// (Captures/overhaul/itt_design/timing_groups/20_timing_groups.py と同じ式)。</para>
    /// <para>倍率 M_{群,t} = Σ_{i∈群} Δ_{i,t} ÷ Σ_{i∈群} Δ̄_i (Δ̄ = 時期の付与の重み 1:2:2 の平均)。
    /// <b>掛けるのは群ごとの Wald (γ_d1 = γ_d3 = 0・自由度 2) が Bonferroni の p &lt; 0.05 / 群数 の群だけ</b>
    /// (Δ̄ が小さい群は比が不安定なので 1 のまま・作者の決定)。 深さ 0 は深さ 1、 深さ 6・7 は深さ 5 の倍率 (A 案)。</para>
    /// <para>群は items.json の category / axis / consFamily で決める (id の綴りから読まない)。</para></summary>
    public static partial class GrantItt
    {
        private const int TimingIters = 30;
        private const string GoldGroup = "金の付与";
        /// <summary>時期 (深さ 1 / 3 / 5) の付与の重み (付与層 1〜5 一様 → 1:2:2)。</summary>
        private static readonly double[] TimeWeight = { 0.2, 0.4, 0.4 };
        private static readonly int[][] FloorsOfTime = { new[] { 1 }, new[] { 2, 3 }, new[] { 4, 5 } };

        /// <summary>付与層 → 時期の添字 (0 = 深さ 1・1 = 深さ 3・2 = 深さ 5)。</summary>
        private static int TimeOfFloor(int floor) => floor <= 1 ? 0 : floor <= 3 ? 1 : 2;
        /// <summary>今の深さ → 時期の添字。 深さ 0 は深さ 1、 6・7 は深さ 5 と同じ。</summary>
        public static int TimeOfDepth(int depth) => depth <= 1 ? 0 : depth <= 3 ? 1 : 2;

        /// <summary>品の群。 秘宝は軸 (無ければ汎用)・消耗品は回復とそれ以外・賽・武器。 items.json に無ければ "?"。</summary>
        public static string TimingGroupOf(string id)
        {
            var d = string.IsNullOrEmpty(id) ? null : ItemSystem.ItemDatabase.Instance?.GetItem(id);
            if (d == null) return "?";
            switch (d.category)
            {
                case ItemSystem.ItemCategory.Passive: return "秘宝・" + (string.IsNullOrEmpty(d.axis) ? "汎用" : d.axis);
                case ItemSystem.ItemCategory.Consumable: return d.consFamily == GameLoop.ItemIds.ConsHealFamily ? "消耗品・回復" : "消耗品・他";
                case ItemSystem.ItemCategory.Dice: return "賽";
                case ItemSystem.ItemCategory.Weapon: return "武器";
                default: return d.category.ToString();
            }
        }

        [Serializable] private class TimingGroup
        {
            public string name; public int items; public double wald; public double p; public bool significant;
            public double[] mult;      // 深さ 1 / 3 / 5 の倍率 (当てたまま)
            public double[] applied;   // BOT が掛ける値 (有意でなければ 1)
            public double[] gamma;     // 中心化した γ (log オッズ・負 = その時期に取ると死ににくい)
            public double[] deltaMean; // 群の平均 Δ (pp) 深さ 1 / 3 / 5
            public double deltaBar;    // 群の平均 Δ̄ (pp)
        }
        [Serializable] private class TimingFile { public int n; public double pThreshold; public double baselineClear; public int iterations; public List<TimingGroup> groups = new List<TimingGroup>(); }

        public static string TimingPath(string learningRoot) => Path.Combine(learningRoot, string.IsNullOrEmpty(Variant) ? "itt_timing.json" : "itt_timing." + Variant + ".json");

        private static void RecomputeTiming(List<Run> runs, List<string> cols, List<int> goldCols, string learningRoot)
        {
            int NI = cols.Count, NG = goldCols.Count, NS = MaxLevel - 1, NC = NI + NG;
            var gname = new string[NC];
            for (int i = 0; i < NI; i++) gname[i] = TimingGroupOf(cols[i]);
            for (int g = 0; g < NG; g++) gname[NI + g] = GoldGroup;
            var G = new List<string>(new SortedSet<string>(gname, StringComparer.Ordinal));
            int NGRP = G.Count;
            var gid = new int[NC];
            for (int c = 0; c < NC; c++) gid[c] = G.IndexOf(gname[c]);
            int nFree = NC + 2 * NGRP;

            // 深さ 1・3 の付与に、 群の γ 列を同じ付与層で足す (深さ 5 は基準)
            var aug = new List<Run>(runs.Count);
            foreach (var r in runs)
            {
                var gi = new List<int>(r.gItem); var gf = new List<int>(r.gFloor);
                for (int g = 0; g < r.gItem.Length; g++)
                {
                    int t = TimeOfFloor(r.gFloor[g]);
                    if (t < 2) { gi.Add(NC + 2 * gid[r.gItem[g]] + t); gf.Add(r.gFloor[g]); }
                }
                if (r.goldCol >= 0)
                {
                    int t = TimeOfFloor(r.goldFloor);
                    if (t < 2) { gi.Add(NC + 2 * gid[NI + r.goldCol] + t); gf.Add(r.goldFloor); }
                }
                aug.Add(new Run { band = r.band, gItem = gi.ToArray(), gFloor = gf.ToArray(), goldCol = r.goldCol, goldFloor = r.goldFloor });
            }
            var x = new double[nFree + NS];
            for (int s = 0; s < NS; s++) x[nFree + s] = -2.0;
            if (!Irls(aug, NI, nFree, NS, TimingIters, x, out double[,] cov, out int _))
            { Debug.LogWarning("[GrantItt] 時期の倍率: ヘッセ行列が特異"); return; }

            var alpha = new double[MaxLevel + 1];
            for (int k = 2; k <= MaxLevel; k++) alpha[k] = x[nFree + (k - 2)];
            double p0 = ClearProb(alpha, 0.0, 0);
            var num = new double[NGRP, 3]; var den = new double[NGRP]; var cnt = new int[NGRP];
            for (int c = 0; c < NC; c++)
            {
                int j = gid[c]; cnt[j]++;
                for (int t = 0; t < 3; t++)
                {
                    double b = x[c] + (t < 2 ? x[NC + 2 * j + t] : 0.0), s = 0;
                    foreach (int f in FloorsOfTime[t]) s += ClearProb(alpha, b, f);
                    double d = (s / FloorsOfTime[t].Length - p0) * 100.0;
                    num[j, t] += d; den[j] += TimeWeight[t] * d;
                }
            }
            double pThr = 0.05 / NGRP;
            var file = new TimingFile { n = runs.Count, pThreshold = pThr, baselineClear = p0, iterations = TimingIters };
            for (int j = 0; j < NGRP; j++)
            {
                int a = NC + 2 * j;
                double c00 = cov[a, a], c01 = cov[a, a + 1], c11 = cov[a + 1, a + 1], v0 = x[a], v1 = x[a + 1];
                double det = c00 * c11 - c01 * c01;
                double w = (v0 * v0 * c11 - 2 * v0 * v1 * c01 + v1 * v1 * c00) / det;   // vᵀ C⁻¹ v
                double p = Math.Exp(-w / 2);                                             // χ² (自由度 2) の上側
                double gm = TimeWeight[0] * v0 + TimeWeight[1] * v1;
                var tg = new TimingGroup
                {
                    name = G[j], items = cnt[j], wald = w, p = p, significant = p < pThr,
                    mult = new double[3], applied = new double[3], deltaMean = new double[3],
                    gamma = new[] { v0 - gm, v1 - gm, -gm }, deltaBar = den[j] / cnt[j],
                };
                for (int t = 0; t < 3; t++)
                {
                    tg.mult[t] = num[j, t] / den[j];
                    tg.applied[t] = tg.significant ? tg.mult[t] : 1.0;
                    tg.deltaMean[t] = num[j, t] / cnt[j];
                }
                file.groups.Add(tg);
            }
            Directory.CreateDirectory(learningRoot);
            File.WriteAllText(TimingPath(learningRoot), JsonUtility.ToJson(file, true), Encoding.UTF8);

            var sb = new StringBuilder();
            sb.AppendLine("=== ITT 群 × 届いた深さの倍率 (ハイブリッド案) ===");
            sb.AppendLine($"ラン {runs.Count}・群 {NGRP}・反復 {TimingIters}・基準クリア率 {p0 * 100:F2}%・有意の線 p < {pThr:G3} (Bonferroni 0.05/{NGRP})");
            sb.AppendLine("倍率 M = 群の Δ 合計 (その時期に取った時) ÷ 群の Δ̄ 合計 (時期の重み 1:2:2)。 BOT が掛けるのは「掛ける」列 (有意でない群は 1)。");
            sb.AppendLine("深さ 0 は深さ 1、 深さ 6・7 は深さ 5 の倍率を使う。");
            sb.AppendLine();
            sb.AppendLine("群                品数    χ²(2)        p  有意 |  M 深1  M 深3  M 深5 | 掛ける 1/3/5      | Δ̄(pp)  Δ 深1/3/5 (pp)");
            foreach (var g in file.groups)
                sb.AppendLine($"  {g.name,-14} {g.items,4} {g.wald,8:F1} {g.p,9:G2}  {(g.significant ? "○" : "－")}  | {g.mult[0],6:F2} {g.mult[1],6:F2} {g.mult[2],6:F2} |"
                            + $" {g.applied[0]:F2}/{g.applied[1]:F2}/{g.applied[2]:F2}  | {g.deltaBar,6:F2}  {g.deltaMean[0]:F2}/{g.deltaMean[1]:F2}/{g.deltaMean[2]:F2}");
            File.WriteAllText(Path.ChangeExtension(TimingPath(learningRoot), ".txt"), sb.ToString(), Encoding.UTF8);
            int nsig = 0; foreach (var g in file.groups) if (g.significant) nsig++;
            Debug.Log($"[GrantItt] 時期の倍率: 群 {NGRP} (有意 {nsig}) → {TimingPath(learningRoot)}");
        }

        // ===== BOT の読み =====

        private static readonly Dictionary<string, double[]> _timingApplied = new Dictionary<string, double[]>(StringComparer.Ordinal);
        public static bool TimingLoaded { get; private set; }
        public static string TimingSummary { get; private set; } = "(未読み込み)";

        /// <summary>itt_timing(.版).json を読む。 無ければ false (倍率はすべて 1)。</summary>
        public static bool TryLoadTiming(string learningRoot)
        {
            _timingApplied.Clear(); TimingLoaded = false; TimingSummary = "(無し)";
            string p = TimingPath(learningRoot);
            if (!File.Exists(p)) { TimingSummary = "(無し: " + Path.GetFileName(p) + ")"; return false; }
            try
            {
                var f = JsonUtility.FromJson<TimingFile>(File.ReadAllText(p, Encoding.UTF8));
                if (f?.groups == null || f.groups.Count == 0) return false;
                var sig = new List<string>();
                foreach (var g in f.groups)
                {
                    if (g.applied == null || g.applied.Length != 3) continue;
                    _timingApplied[g.name] = g.applied;
                    if (g.significant) sig.Add($"{g.name} {g.applied[0]:F2}/{g.applied[1]:F2}/{g.applied[2]:F2}");
                }
                TimingLoaded = true;
                TimingSummary = $"N={f.n} 群 {f.groups.Count} 有意 {sig.Count} (深さ 1/3/5): " + (sig.Count == 0 ? "なし" : string.Join(" | ", sig));
                return true;
            }
            catch (Exception e) { Debug.LogWarning($"[GrantItt] 時期の倍率の読込失敗: {e.Message}"); return false; }
        }

        /// <summary>品 <paramref name="id"/> を時期 <paramref name="t"/> (<see cref="TimeOfDepth"/>) に取る時の倍率。 読んでいない・有意でない群は 1。</summary>
        public static double TimingMul(string id, int t)
        {
            if (!TimingLoaded || t < 0 || t > 2) return 1.0;
            string g = TimingGroupOf(id);
            if (!_timingApplied.TryGetValue(g, out double[] m)) return 1.0;
            // 寄せの軸 (buyAxisFocus) の秘宝は上げる側だけ効かせる (1 未満は 1): 寄せた軸を序盤に捨てないため (2026-11-22・作者の決定)
            return FocusAxes.Contains(g) ? System.Math.Max(1.0, m[t]) : m[t];
        }

        /// <summary>倍率を「上げる側だけ」にする群 (<c>秘宝・&lt;軸&gt;</c>)。 AutoRunner が buyAxisFocus から流し込む。</summary>
        public static readonly HashSet<string> FocusAxes = new HashSet<string>(StringComparer.Ordinal);
    }
}

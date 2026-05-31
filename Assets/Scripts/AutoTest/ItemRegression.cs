using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// 全アイテム同時 Ridge 回帰 (λ=1.0):
    ///   bandScore_run ~ β0 + Σ_i β_i × I(取得 item_i)
    /// 6F到達ラン群で解く。 各 β_i は「他アイテム取得を統制した上での item_i の純粋寄与」。
    /// |β| > 2×SE なら統計的に意味のある寄与とみなせる。
    ///
    /// 仕組み:
    ///   X^T X (D×D, D=K+1) は疎更新で直接組み立て (O(N × maxItemsPerRun²))。
    ///   逆行列は Gauss-Jordan (D=80〜120 程度で十分)。
    ///   SE = sqrt(σ̂² × diag((X^T X + λI)^-1))
    ///
    /// 入出力:
    ///   入力: AutoRunLogs/learning/regression_runs.csv (RunDataLogger が追記)
    ///   出力: 静的 Dictionary キャッシュ。 TryGetCoef(id, out β, out se)
    /// </summary>
    public static class ItemRegression
    {
        private const double Ridge = 1.0;
        private const int MinAcqForInclude = 30;
        private const int MinRowsForFit = 200;

        private static Dictionary<string, Coef> _coefs = new Dictionary<string, Coef>();
        private static string _lastSummary = "(未計算)";
        public static string LastSummary => _lastSummary;
        public static int FeatureCount => _coefs.Count;

        public struct Coef { public double beta; public double se; }

        public static bool TryGetCoef(string id, out double beta, out double se)
        {
            if (_coefs.TryGetValue(id, out var c)) { beta = c.beta; se = c.se; return true; }
            beta = 0; se = 0; return false;
        }

        public static void Recompute(string learningRoot)
        {
            try
            {
                var rows = RunDataLogger.LoadAll(learningRoot);
                // 6F到達のみで fit (lift6F と整合)
                var filtered = new List<RunDataLogger.RunRow>(rows.Count);
                foreach (var r in rows) if (r.reachedFloor >= 6) filtered.Add(r);
                if (filtered.Count < MinRowsForFit)
                {
                    _lastSummary = $"行数不足 ({filtered.Count}/{MinRowsForFit})";
                    Debug.Log($"[ItemRegression] {_lastSummary}");
                    return;
                }

                // 出現回数カウント (除外リスト除く)
                var counts = new Dictionary<string, int>();
                foreach (var r in filtered)
                    foreach (var id in r.items)
                        if (!string.IsNullOrEmpty(id) && !ItemLearningStats.ExcludedFromLift.Contains(id))
                            counts[id] = counts.TryGetValue(id, out int c) ? c + 1 : 1;

                // 採用 feature: 取得 ≥ MinAcqForInclude かつ 未取得 ≥ MinAcqForInclude (両群で動かないと推定不可)
                var features = new List<string>();
                foreach (var kv in counts)
                    if (kv.Value >= MinAcqForInclude && kv.Value <= filtered.Count - MinAcqForInclude)
                        features.Add(kv.Key);
                features.Sort(StringComparer.Ordinal);
                int K = features.Count;
                if (K == 0) { _lastSummary = "feature 0"; return; }

                var idx = new Dictionary<string, int>(K);
                for (int i = 0; i < K; i++) idx[features[i]] = i;

                int N = filtered.Count;
                int D = K + 1; // +intercept(列 0)

                double[,] xtx = new double[D, D];
                double[] xty = new double[D];
                double[] ys = new double[N];

                xtx[0, 0] = N;
                var seenList = new List<int>(32);
                for (int r = 0; r < N; r++)
                {
                    var rec = filtered[r];
                    double y = rec.bandScore;
                    ys[r] = y;
                    xty[0] += y;

                    seenList.Clear();
                    foreach (var id in rec.items)
                        if (idx.TryGetValue(id, out int j)) seenList.Add(j + 1);

                    int M = seenList.Count;
                    for (int a = 0; a < M; a++)
                    {
                        int ja = seenList[a];
                        xtx[0, ja] += 1;
                        xtx[ja, 0] += 1;
                        xtx[ja, ja] += 1;
                        xty[ja] += y;
                        for (int b = a + 1; b < M; b++)
                        {
                            int jb = seenList[b];
                            xtx[ja, jb] += 1;
                            xtx[jb, ja] += 1;
                        }
                    }
                }

                // Ridge: 切片以外の対角に λ を加算
                for (int j = 1; j < D; j++) xtx[j, j] += Ridge;

                double[,] inv = Invert(xtx, D);
                if (inv == null) { _lastSummary = "逆行列失敗 (特異)"; return; }

                // β = inv × X^T y
                double[] beta = new double[D];
                for (int i = 0; i < D; i++)
                {
                    double s = 0;
                    for (int j = 0; j < D; j++) s += inv[i, j] * xty[j];
                    beta[i] = s;
                }

                // 残差 → σ̂²
                double rss = 0;
                for (int r = 0; r < N; r++)
                {
                    var rec = filtered[r];
                    double pred = beta[0];
                    foreach (var id in rec.items)
                        if (idx.TryGetValue(id, out int j)) pred += beta[j + 1];
                    double e = ys[r] - pred;
                    rss += e * e;
                }
                double sigma2 = rss / Math.Max(1, N - D);

                var newCoefs = new Dictionary<string, Coef>(K);
                for (int j = 0; j < K; j++)
                {
                    double v = sigma2 * inv[j + 1, j + 1];
                    double se = v > 0 ? Math.Sqrt(v) : 0;
                    newCoefs[features[j]] = new Coef { beta = beta[j + 1], se = se };
                }
                _coefs = newCoefs;
                _lastSummary = $"N={N} K={K} σ={Math.Sqrt(sigma2):F2} intercept={beta[0]:F2}";
                Debug.Log($"[ItemRegression] {_lastSummary}");
            }
            catch (Exception e)
            {
                _lastSummary = $"fail: {e.Message}";
                Debug.LogWarning($"[ItemRegression] {_lastSummary}");
            }
        }

        // Gauss-Jordan 部分ピボット逆行列。 失敗時 null。
        private static double[,] Invert(double[,] m, int n)
        {
            var a = new double[n, 2 * n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) a[i, j] = m[i, j];
                a[i, n + i] = 1;
            }
            for (int col = 0; col < n; col++)
            {
                int piv = col;
                double max = Math.Abs(a[col, col]);
                for (int r = col + 1; r < n; r++)
                {
                    double v = Math.Abs(a[r, col]);
                    if (v > max) { max = v; piv = r; }
                }
                if (max < 1e-12) return null;
                if (piv != col)
                {
                    for (int j = 0; j < 2 * n; j++) { double t = a[col, j]; a[col, j] = a[piv, j]; a[piv, j] = t; }
                }
                double d = a[col, col];
                double invD = 1.0 / d;
                for (int j = 0; j < 2 * n; j++) a[col, j] *= invD;
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = a[r, col];
                    if (f == 0) continue;
                    for (int j = 0; j < 2 * n; j++) a[r, j] -= f * a[col, j];
                }
            }
            var inv = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) inv[i, j] = a[i, n + j];
            return inv;
        }
    }
}

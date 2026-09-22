using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// 全アイテム同時 Ridge 回帰。 <c>band ~ β0 + Σ_i β_i × I(取得 item_i)</c>
    ///
    /// <para><b>この β が「純粋寄与」である理由。</b> 取得はランの成否と同時に決まる内生変数で、
    /// 長く生きて金が回ったランほど何でも取得している ── lift 系 (取得群 vs 非取得群) は
    /// この共通因子をまるごと拾うため、 強さではなく<b>取得選択性</b>を測ってしまう
    /// (プールの lift5F は μ=+0.856 / σ=0.407 で、 共通成分が品目間のばらつきの 2 倍あった)。
    /// 多品目同時回帰はここが根本的に違う: <b>他の全品の所持を統制している</b>ので、
    /// ランの豊かさ (= Σ_j x_j = 所持品数) は特徴量の線形結合として既にモデルに入っている。
    /// 実測でも <c>corr(取得率, β) = +0.007</c> と選択性から独立だった。
    /// <b>所持品数を明示的な統制列として足すのは誤り</b> ── 既存列と共線なので SE だけ
    /// 跳ね上がり (中央 0.011 → 0.026)、 β も因果一致度も動かない。 2026-09-04 に実測して棄却。</para>
    ///
    /// <para><b>行を 6F 到達で絞らない。</b> 旧実装は <c>reachedFloor≥6</c> で fit していたが、
    /// 到達は取得の**下流**なのでコライダー条件付けになる。 さらに 6F 到達群の band はほぼ
    /// 動かない (σ=0.58) ため、 <b>「6F まで連れて行く力」が測定対象から外れていた</b>。
    /// 無作為化ホールドアウト (ExploreLift) を正解とした一致度で全ラン版が上回る ──
    /// 順位相関 <b>+0.596 (全ラン) vs +0.494 (6F限定)</b>。 独立に答えのわかる 4 品
    /// (武器チェーン T3→T4 の実測差) の順位も、 全ラン版だけが再現する。
    ///   ・<see cref="TryGetCoef"/>   = <b>全ラン</b>モデル (主系列・準パワーの 0.55)
    ///   ・<see cref="TryGetCoef6F"/> = 6F 到達限定モデル (終盤性能・表示のみ)</para>
    ///
    /// <para><b>列標準化リッジ。</b> 生の二値列に一律 λ を掛けると、 取得数の少ない品ほど強く
    /// 0 へ縮む ── 取得率依存のバイアスを自分で作ることになる (λ=10000 で武器 4 品の順位が
    /// 実測と食い違い始めるのを確認)。 そこで列を <c>s_j=√(p(1−p))</c> で標準化した空間で
    /// 罰則を掛ける。 これは生スケールでの<b>対角 λ·s_j²</b> と等価なので、 実装は
    /// 対角に足す値を変えるだけでよい。 λ 自体は小さく保つ (N=14.5万に対し 100 = ほぼ OLS)。</para>
    ///
    /// <para><b>入力を切らない。</b> 旧実装は <c>RunDataLogger.LoadAll</c> 経由で末尾 5 万行に
    /// 切られ、 そこから 6F 到達だけ残して <c>N=7891</c> ── 蓄積 14.5 万行の 5.4% しか
    /// 使っていなかった。 現在は csv を**ストリームで 2 パス**舐めるので上限が無く、
    /// メモリも行数に依存しない。</para>
    ///
    /// <para><b>信頼性の自己診断。</b> 行を偶奇で 2 分割して独立に fit し、 β の Pearson r を
    /// <see cref="SplitHalfR"/> に出す。 これが低いうちは係数はノイズなので、
    /// 重みを上げてはいけない。</para>
    ///
    /// 仕組み:
    ///   X^T X (D×D, D=K+2) は疎更新で直接組み立て (O(N × maxItemsPerRun²))。
    ///   逆行列は Gauss-Jordan。 SE = sqrt(σ̂² × diag((X^T X + λI)^-1))。
    ///   RSS は Σy² − 2βᵀX^Ty + βᵀ(X^TX)β で閉じるので、 y を保持しない。
    ///
    /// 入出力:
    ///   入力: AutoRunLogs/learning/&lt;帯&gt;/regression_runs.csv (RunDataLogger が追記)
    ///   出力: 静的 Dictionary キャッシュ + regression_fit.txt (v2)
    /// </summary>
    public static class ItemRegression
    {
        /// <summary>標準化空間での罰則。 N に対して十分小さく取る (実質 OLS)。
        /// 大きくすると取得数依存の縮小が入り、 順位が実測と食い違い始める。</summary>
        private const double Ridge = 100.0;
        /// <summary>**モデルに入れる**最低取得数。 低めに保つ ── 落とすと欠落変数バイアスに
        /// なるので、 推定できる限り列として残す。</summary>
        private const int MinAcqForInclude = 30;
        /// <summary>**β を信用する**最低取得数の絶対下限。 実効値は max(これ, N/100)。
        /// 取得率 1% 未満の品は split-half r が 0 近傍 (実測 −0.045) で、 係数がノイズしかない。
        /// ここを割った品は <see cref="IsTrusted"/> が false になり、 準パワー側で
        /// regβ の重みが lift6F へ転送される。</summary>
        private const int MinAcqForTrustFloor = 200;
        /// <summary>分割半分ごとの最低取得数。 これを割る品は split-half r の計算から外す。</summary>
        private const int MinAcqPerHalf = 15;
        private const int MinRowsForFit = 200;

        private static Dictionary<string, Coef> _coefs = new Dictionary<string, Coef>();
        private static Dictionary<string, Coef> _coefs6F = new Dictionary<string, Coef>();
        private static HashSet<string> _trusted = new HashSet<string>(StringComparer.Ordinal);
        private static string _lastSummary = "(未計算)";
        public static string LastSummary => _lastSummary;
        public static int FeatureCount => _coefs.Count;

        /// <summary>偶奇分割で独立に fit した β の Pearson r。 未計算/算出不能なら NaN。
        /// **これが 0.80 を超えるまで、 準パワーの regβ 重みを上げてはいけない。**</summary>
        public static double SplitHalfR { get; private set; } = double.NaN;

        /// <summary>主系列 (全ラン) モデルの N。 診断表示用。</summary>
        public static int LastN { get; private set; }

        public struct Coef { public double beta; public double se; }

        /// <summary>主系列 = <b>全ラン・所持品数統制</b>モデルの係数。</summary>
        public static bool TryGetCoef(string id, out double beta, out double se)
        {
            if (_coefs.TryGetValue(id, out var c)) { beta = c.beta; se = c.se; return true; }
            beta = 0; se = 0; return false;
        }

        /// <summary>6F 到達限定モデルの係数 (終盤性能)。 <b>表示専用</b> ── 準パワーには
        /// 入れない。 無作為化基準との一致度が全ラン版に劣り、 混ぜると合成スコアが悪化する
        /// (順位相関 0.669 → 0.658、 2026-09-04 実測)。</summary>
        public static bool TryGetCoef6F(string id, out double beta, out double se)
        {
            if (_coefs6F.TryGetValue(id, out var c)) { beta = c.beta; se = c.se; return true; }
            beta = 0; se = 0; return false;
        }

        /// <summary>この品の β を信用してよいか (取得数がしきい値以上)。
        /// false の品は係数自体は返るが、 準パワーでは regβ の重みを lift6F へ転送する。</summary>
        public static bool IsTrusted(string id) => _trusted.Contains(id);

        /// <summary>直近に fit した入力の指紋。 同一プロセス内での再計算を避けるためだけに使う。</summary>
        private static string _fitInputKey = "";
        /// <summary>キャッシュ命中回数 (診断用)。</summary>
        public static int CacheHits { get; private set; }

        /// <summary>入力 csv の指紋 (絶対パス + 長さ + 最終更新)。 読めなければ空文字を返し、
        /// その場合キャッシュは成立させない。</summary>
        private static string FitInputKey(string learningRoot)
        {
            try
            {
                var fi = new FileInfo(Path.Combine(learningRoot ?? "", RunDataLogger.FileName));
                if (!fi.Exists) return "";
                return fi.FullName + "|" + fi.Length.ToString(CultureInfo.InvariantCulture)
                     + "|" + fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            }
            catch { return ""; }
        }

        public static void Recompute(string learningRoot)
        {
            // **入力が 1 バイトも変わっていなければ再計算しない。**
            // fit は同じ行集合に対して完全に決定的なので、 スキップしても係数は同一 ──
            // BOT の判断は 1 ビットも変わらない。
            //
            // これが効くのは rollout worker。 通常バッチはこの固定費を 1000 ラン に償却するが、
            // worker は 1 ラン にしか償却できず、 しかも 学習Reload は 1 プロセスに 4 回走る。
            long phase = AutoTest.Ultra.UltraPhaseClock.Begin();
            try
            {
                string key = FitInputKey(learningRoot);
                if (key.Length > 0 && key == _fitInputKey && _coefs.Count > 0)
                {
                    CacheHits++;
                    return;
                }

                if (key.Length > 0 && TryLoadFit(learningRoot, key))
                {
                    DiskHits++;
                    _fitInputKey = key;
                    return;
                }

                RecomputeCore(learningRoot);
                // 行数不足で早期 return した場合も含めて記録する。 同じ入力なら結果 (係数を
                // 更新しなかったという結果も含め) は同じなので、 再実行に意味が無い。
                _fitInputKey = key;
                if (key.Length > 0) TrySaveFit(learningRoot, key);
            }
            finally { AutoTest.Ultra.UltraPhaseClock.End("└ └ 回帰OLS", phase); }
        }

        /// <summary>ディスクキャッシュ命中回数 (診断用)。</summary>
        public static int DiskHits { get; private set; }

        private const string FitCacheFileName = "regression_fit.txt";
        /// <summary>v3 = 2 モデル (全ラン / 6F限定) + split-half r + 信用フラグ。 旧版は読まない。</summary>
        private const string FitCacheVersion = "v3";

        /// <summary>係数キャッシュのパス。 入力 csv と同じディレクトリに置く ── 帯ごとに
        /// learningRoot が分かれているので、 これで帯を取り違えようがない。</summary>
        private static string FitCachePath(string learningRoot)
        {
            return Path.Combine(learningRoot ?? "", FitCacheFileName);
        }

        /// <summary><c>double</c> は "R" 書式で往復させる。 JsonUtility は使わない ──
        /// このリポジトリで 4 回事故を起こしており、 かつ double 配列の扱いが版依存。</summary>
        private static bool TryLoadFit(string learningRoot, string expectedKey)
        {
            try
            {
                string path = FitCachePath(learningRoot);
                if (!File.Exists(path)) return false;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 5) return false;
                if (!string.Equals(lines[0], FitCacheVersion, StringComparison.Ordinal)) return false;
                if (!string.Equals(lines[1], expectedKey, StringComparison.Ordinal)) return false;
                // **件数を必ず突き合わせる。** 差し替えは原子的ではないので、 行の途中まで
                // しか書かれていないファイルを読みうる。 それが「係数が少し足りない表」として
                // 通ってしまうと、 BOT の判断が黙って変わる ── 一番たちの悪い壊れ方なので、
                // 数が合わなければキャッシュ不成立にして計算し直す。
                if (!int.TryParse(lines[2], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int expectedCount)) return false;
                if (!double.TryParse(lines[3], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double shr)) shr = double.NaN;

                var loaded = new Dictionary<string, Coef>(expectedCount);
                var loaded6 = new Dictionary<string, Coef>(expectedCount);
                var loadedT = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 5; i < lines.Length; i++)
                {
                    if (string.IsNullOrEmpty(lines[i])) continue;
                    string[] parts = lines[i].Split('\t');
                    if (parts.Length != 6) return false;
                    if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double beta)) return false;
                    if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double se)) return false;
                    if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double beta6)) return false;
                    if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double se6)) return false;
                    loaded[parts[0]] = new Coef { beta = beta, se = se };
                    loaded6[parts[0]] = new Coef { beta = beta6, se = se6 };
                    if (parts[5] == "1") loadedT.Add(parts[0]);
                }
                if (loaded.Count != expectedCount) return false;
                _coefs = loaded;
                _coefs6F = loaded6;
                _trusted = loadedT;
                SplitHalfR = shr;
                _lastSummary = lines[4] + " (cache)";
                return true;
            }
            catch { return false; }   // 壊れていたら黙って計算し直す
        }

        private static void TrySaveFit(string learningRoot, string key)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(FitCacheVersion).Append('\n').Append(key).Append('\n')
                  .Append(_coefs.Count.ToString(CultureInfo.InvariantCulture)).Append('\n')
                  .Append(SplitHalfR.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
                  .Append((_lastSummary ?? "").Replace('\n', ' ')).Append('\n');
                foreach (var kv in _coefs)
                {
                    _coefs6F.TryGetValue(kv.Key, out var c6);
                    sb.Append(kv.Key).Append('\t')
                      .Append(kv.Value.beta.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(kv.Value.se.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(c6.beta.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(c6.se.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(_trusted.Contains(kv.Key) ? '1' : '0').Append('\n');
                }

                // 12 個の worker が同時に書きうる。 内容は全て同一なので、 一時ファイル →
                // 差し替えで、 途中まで書かれたファイルを誰かが読む事態だけを防げばよい。
                string path = FitCachePath(learningRoot);
                string temp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
                try { File.Copy(temp, path, true); } finally { File.Delete(temp); }
            }
            catch { /* キャッシュを書けなくても正しさには影響しない */ }
        }

        // ============================================================
        //  ストリーム入力 ── 行を溜めない。
        //  行形式: bandScore|reachedFloor|id,id,...
        // ============================================================

        private static IEnumerable<KeyValuePair<int, string[]>> StreamRows(string path, int minFloor)
        {
            if (!File.Exists(path)) yield break;
            using (var sr = new StreamReader(path, Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    var parts = line.Split('|');
                    if (parts.Length < 3) continue;
                    if (!int.TryParse(parts[0], out int bs)) continue;
                    if (!int.TryParse(parts[1], out int rf)) continue;
                    if (rf < minFloor) continue;
                    var ids = parts[2].Length == 0 ? Array.Empty<string>() : parts[2].Split(',');
                    yield return new KeyValuePair<int, string[]>(bs, ids);
                }
            }
        }

        /// <summary>1 モデルぶんの正規方程式。 偶奇の半分ごとに 1 個ずつ持ち、
        /// 全体は 2 つの要素和として得る (ストリームを 1 回しか舐めないため)。</summary>
        private sealed class Normal
        {
            public readonly int D;
            public double[,] xtx;
            public double[] xty;
            public double ysq;
            public int n;
            public Normal(int d) { D = d; xtx = new double[d, d]; xty = new double[d]; }

            public void Add(Normal o)
            {
                for (int i = 0; i < D; i++)
                {
                    xty[i] += o.xty[i];
                    for (int j = 0; j < D; j++) xtx[i, j] += o.xtx[i, j];
                }
                ysq += o.ysq; n += o.n;
            }
        }

        private struct Fit
        {
            public double[] beta;   // 長さ D
            public double[] se;     // 長さ D
            public double sigma;
            public int n;
            public bool ok;
        }

        /// <summary>正規方程式を解く。 品目列の対角に <c>λ·s_j²</c> を足す ── これは
        /// 「列を s_j で標準化した空間で一律 λ を掛ける」ことと等価で、 縮小率が
        /// 取得率に依存しなくなる (生スケールで一律 λ にすると希少品ほど強く 0 へ寄る)。
        /// 切片 (列 0) は縮めない。</summary>
        private static Fit Solve(Normal nm, double[] colPenalty)
        {
            var f = new Fit { ok = false, n = nm.n };
            int D = nm.D;
            if (nm.n <= D) return f;

            var a = (double[,])nm.xtx.Clone();
            for (int j = 1; j < D; j++) a[j, j] += colPenalty[j];
            var inv = Invert(a, D);
            if (inv == null) return f;

            var beta = new double[D];
            for (int i = 0; i < D; i++)
            {
                double s = 0;
                for (int j = 0; j < D; j++) s += inv[i, j] * nm.xty[j];
                beta[i] = s;
            }

            // RSS = Σy² − 2βᵀ(Xᵀy) + βᵀ(XᵀX)β   ※ XᵀX は罰則前の生の行列
            double bXy = 0;
            for (int i = 0; i < D; i++) bXy += beta[i] * nm.xty[i];
            double bXXb = 0;
            for (int i = 0; i < D; i++)
            {
                double row = 0;
                for (int j = 0; j < D; j++) row += nm.xtx[i, j] * beta[j];
                bXXb += beta[i] * row;
            }
            double rss = nm.ysq - 2 * bXy + bXXb;
            if (rss < 0) rss = 0;
            double sigma2 = rss / Math.Max(1, nm.n - D);

            var se = new double[D];
            for (int i = 0; i < D; i++)
            {
                double v = sigma2 * inv[i, i];
                se[i] = v > 0 ? Math.Sqrt(v) : 0;
            }

            f.beta = beta; f.se = se; f.sigma = Math.Sqrt(sigma2); f.ok = true;
            return f;
        }

        /// <summary>1 モデル (行フィルタ = minFloor) を組んで解く。 split-half も同時に返す。
        /// ストリーム 1 パス。 <paramref name="colCount"/> にはモデル内での取得数が返る
        /// (信用しきい値と split-half の対象選定に使う)。</summary>
        private static bool FitModel(string path, int minFloor, List<string> features,
                                     out Fit full, out Fit halfA, out Fit halfB,
                                     out int[] colCount, out int[] halfCount)
        {
            full = default; halfA = default; halfB = default;

            int K = features.Count;
            int D = K + 1;                       // 0=切片 / 1..=品目
            colCount = new int[D]; halfCount = new int[D];
            var idx = new Dictionary<string, int>(K, StringComparer.Ordinal);
            for (int i = 0; i < K; i++) idx[features[i]] = i + 1;

            var A = new Normal(D); var B = new Normal(D);
            var seen = new List<int>(64);
            long r = 0;
            foreach (var row in StreamRows(path, minFloor))
            {
                bool even = (r++ & 1) == 0;
                var nm = even ? A : B;
                double y = row.Key;

                seen.Clear();
                foreach (var id in row.Value)
                    if (idx.TryGetValue(id, out int j)) seen.Add(j);

                nm.n++;
                nm.ysq += y * y;
                nm.xtx[0, 0] += 1;
                nm.xty[0] += y;

                int M = seen.Count;
                for (int x = 0; x < M; x++)
                {
                    int ja = seen[x];
                    colCount[ja]++; if (even) halfCount[ja]++;
                    nm.xtx[0, ja] += 1;  nm.xtx[ja, 0] += 1;
                    nm.xtx[ja, ja] += 1;
                    nm.xty[ja] += y;
                    for (int b = x + 1; b < M; b++)
                    {
                        int jb = seen[b];
                        nm.xtx[ja, jb] += 1;
                        nm.xtx[jb, ja] += 1;
                    }
                }
            }

            var whole = new Normal(D);
            whole.Add(A); whole.Add(B);
            if (whole.n < MinRowsForFit) return false;

            // 列標準化リッジ = 生スケールでの対角 λ·s_j² (s_j² = p(1−p))
            var pen = new double[D];
            for (int j = 1; j < D; j++)
            {
                double p = colCount[j] / (double)whole.n;
                pen[j] = Ridge * Math.Max(p * (1 - p), 1e-9);
            }

            full  = Solve(whole, pen);
            halfA = Solve(A, pen);
            halfB = Solve(B, pen);
            return full.ok;
        }

        private static void RecomputeCore(string learningRoot)
        {
            try
            {
                string path = Path.Combine(learningRoot ?? "", RunDataLogger.FileName);
                if (!File.Exists(path)) { _lastSummary = "csv 無し"; return; }

                // --- feature 選定は**全ラン**で行い、 2 モデルで同一集合を使う ---
                //     (集合が違うと 6F 版と全ラン版を並べて読めない)
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                int total = 0;
                foreach (var row in StreamRows(path, 0))
                {
                    total++;
                    foreach (var id in row.Value)
                    {
                        if (string.IsNullOrEmpty(id)) continue;
                        if (ItemLearningStats.ExcludedFromLift.Contains(id)) continue;
                        counts[id] = counts.TryGetValue(id, out int c) ? c + 1 : 1;
                    }
                }
                if (total < MinRowsForFit)
                {
                    _lastSummary = $"行数不足 ({total}/{MinRowsForFit})";
                    Debug.Log($"[ItemRegression] {_lastSummary}");
                    return;
                }

                // 採用 feature: 取得 ≥ MinAcqForInclude かつ 未取得 ≥ MinAcqForInclude
                // (両群で動かないと推定不可)
                var features = new List<string>();
                foreach (var kv in counts)
                    if (kv.Value >= MinAcqForInclude && kv.Value <= total - MinAcqForInclude)
                        features.Add(kv.Key);
                features.Sort(StringComparer.Ordinal);
                int K = features.Count;
                if (K == 0) { _lastSummary = "feature 0"; return; }

                // --- 主系列: 全ラン ---
                if (!FitModel(path, 0, features, out Fit fAll, out Fit hA, out Fit hB,
                              out int[] cc, out int[] hc))
                { _lastSummary = "全ランモデル fit 失敗"; Debug.LogWarning($"[ItemRegression] {_lastSummary}"); return; }

                // --- 参考: 6F 到達限定 (表示のみ・準パワーには入れない) ---
                bool ok6 = FitModel(path, 6, features, out Fit f6, out _, out _, out _, out _);

                // β を信用する下限。 取得率 1% 未満は split-half r が 0 近傍だった。
                int trustMin = Math.Max(MinAcqForTrustFloor, fAll.n / 100);

                var newAll = new Dictionary<string, Coef>(K);
                var new6F  = new Dictionary<string, Coef>(K);
                var newTrust = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < K; j++)
                {
                    newAll[features[j]] = new Coef { beta = fAll.beta[j + 1], se = fAll.se[j + 1] };
                    new6F[features[j]]  = ok6 ? new Coef { beta = f6.beta[j + 1], se = f6.se[j + 1] }
                                              : new Coef { beta = 0, se = 0 };
                    if (cc[j + 1] >= trustMin) newTrust.Add(features[j]);
                }
                _coefs = newAll;
                _coefs6F = new6F;
                _trusted = newTrust;
                LastN = fAll.n;

                // --- split-half 信頼性: **信用する品だけ**で測る ---
                //     (取得率 1% 未満の品を混ぜると全体 r が 0.92 → 0.54 に落ちる。
                //      準パワーはそれらの β を使わないので、 混ぜて測る意味が無い)
                SplitHalfR = double.NaN;
                if (hA.ok && hB.ok)
                {
                    var xs = new List<double>(K); var ys = new List<double>(K);
                    for (int j = 0; j < K; j++)
                    {
                        if (cc[j + 1] < trustMin) continue;
                        if (hc[j + 1] < MinAcqPerHalf || (cc[j + 1] - hc[j + 1]) < MinAcqPerHalf) continue;
                        xs.Add(hA.beta[j + 1]); ys.Add(hB.beta[j + 1]);
                    }
                    if (xs.Count >= 10) SplitHalfR = Pearson(xs, ys);
                }

                // SE 中央値 (信用する品のみ)
                var ses = new List<double>(K);
                for (int j = 0; j < K; j++) if (cc[j + 1] >= trustMin) ses.Add(fAll.se[j + 1]);
                ses.Sort();
                double medSe = ses.Count > 0 ? ses[ses.Count / 2] : 0;

                _lastSummary = $"N={fAll.n} K={K} σ={fAll.sigma:F2} 切片={fAll.beta[0]:F2} "
                             + $"信用 {newTrust.Count}品(取得≥{trustMin}) SE中央={medSe:F4} "
                             + $"split-half r={(double.IsNaN(SplitHalfR) ? "—" : SplitHalfR.ToString("F3"))} "
                             + $"/ 6F版 N={(ok6 ? f6.n : 0)}";
                Debug.Log($"[ItemRegression] {_lastSummary}");
            }
            catch (Exception e)
            {
                _lastSummary = $"fail: {e.Message}";
                Debug.LogWarning($"[ItemRegression] {_lastSummary}");
            }
        }

        private static double Pearson(List<double> a, List<double> b)
        {
            int n = Math.Min(a.Count, b.Count);
            if (n < 3) return double.NaN;
            double ma = 0, mb = 0;
            for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
            ma /= n; mb /= n;
            double sa = 0, sb = 0, sab = 0;
            for (int i = 0; i < n; i++)
            {
                double da = a[i] - ma, db = b[i] - mb;
                sa += da * da; sb += db * db; sab += da * db;
            }
            if (sa <= 1e-12 || sb <= 1e-12) return double.NaN;
            return sab / Math.Sqrt(sa * sb);
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

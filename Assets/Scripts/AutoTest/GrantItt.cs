using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary><b>ランダム付与 (ITT) の効果量。</b> 2026-09-09。
    ///
    /// <para><b>なぜ要るか。</b> 従来の序列を決めていた <see cref="ItemRegression"/> の regβ は
    /// 「そのランが<b>実際に取得した</b>品」を説明変数に置く ── BOT が選んで取った品なので
    /// <b>内生</b>で、 交絡が信号の約 30 倍あることを実測済み
    /// (`project-pure-contribution-estimator`)。 250,000 ラン 積み直しても交絡は消えない。</para>
    ///
    /// <para><b>ここは式を変えない。 説明変数だけ差し替える。</b>
    /// <see cref="AutoRunner"/> のランダム付与試行は各パッシブを独立に確率
    /// <c>randomGrantExpectedItems / プール数</c> ≈ 2.9% で、 ランダムな層 (1〜5F) に配る
    /// 疎な factorial 設計。 <b>割り当ては BOT の判断と独立</b>なので、
    /// 「割り当てられたか」の指示変数で band を回帰すれば ITT ＝ 不偏の因果効果になる。
    /// 単位は regβ と同じ <b>band</b> なので、 <c>PowerCuts</c> の絶対しきい値がそのまま使える。</para>
    ///
    /// <para><b>推定量の定義に注意。</b> 出るのは
    /// 「<b>層 1〜5 のどこかランダムな時点で、その品を渡されること</b>の効果」。
    /// 「BOT が選んで買うこと」の効果ではないし、 層別の効き方も潰れている
    /// (層を交互作用に入れると 1 品あたりのセルが 1/5 になるので入れていない)。</para>
    ///
    /// <para><b>共変量を足してはいけない。</b> 与ダメ・戦闘数などの列は
    /// <b>処置の後に決まる</b> 中間変数なので、 説明変数に入れると合流点で条件付けて
    /// 因果効果を壊す。 ここは band ~ 割り当て のみ。</para></summary>
    public static class GrantItt
    {
        /// <summary>数値安定のためだけの ridge。 設計がランダム化されていて D ≪ n なので
        /// 縮小はほぼ不要 ── 大きくすると効果量が 0 へ引っ張られて band 単位が壊れる。</summary>
        private const double Ridge = 1.0;

        /// <summary>この処置ラン数に満たない品は信用しない。 p≈2.9% なので
        /// 30,000 ラン で 1 品 約 860 処置。 300 は「1万ラン ぶん」に相当する下限。</summary>
        public const int MinTreatedRuns = 300;

        private const int MinRowsForFit = 2000;

        public struct Coef { public double beta; public double se; public int treated; }

        private static readonly Dictionary<string, Coef> _coefs =
            new Dictionary<string, Coef>(StringComparer.Ordinal);
        private static string _lastSummary = "(未計算)";
        private static bool _loaded;

        public static string LastSummary => _lastSummary;
        public static bool Loaded => _loaded;
        public static int Count => _coefs.Count;
        /// <summary>残差 σ (band)。 必要ラン数の見積りに使う。</summary>
        public static double Sigma { get; private set; } = double.NaN;
        public static int LastN { get; private set; }

        public static bool TryGetCoef(string id, out double beta, out double se)
        {
            beta = 0; se = 0;
            if (string.IsNullOrEmpty(id) || !_coefs.TryGetValue(id, out var c)) return false;
            beta = c.beta; se = c.se; return true;
        }

        public static bool IsTrusted(string id) =>
            !string.IsNullOrEmpty(id) && _coefs.TryGetValue(id, out var c) && c.treated >= MinTreatedRuns;

        /// <summary>ITT がその品について<b>意見を持っているか</b>（＝付与プールに入っていたか）。
        /// 武器・消耗品・出目パーツ・イベント専用品は列そのものが無い ── 標本不足とは別物なので、
        /// 呼び出し側は「落とす」ではなく「別の推定量へ回す」こと。</summary>
        public static bool HasColumn(string id) =>
            !string.IsNullOrEmpty(id) && _coefs.ContainsKey(id);

        public static int TreatedRuns(string id) =>
            !string.IsNullOrEmpty(id) && _coefs.TryGetValue(id, out var c) ? c.treated : 0;

        public static void Clear() { _coefs.Clear(); _loaded = false; _lastSummary = "(未計算)"; }

        public static string EffectsPath(string learningRoot) =>
            Path.Combine(learningRoot, "itt_effects.json");
        public static string ReportPath(string learningRoot) =>
            Path.Combine(learningRoot, "itt_fit.txt");

        // ==========================================================================
        //  行フォーマット (AutoRunner.RecordGrantRow):
        //     band | gold@floor | id@floor,id@floor,... | 与ダメ,被ダメ,戦闘,勝,回復,盾
        //  4 列目は**処置後**に決まる量なので読まない (合流点)。
        // ==========================================================================
        private static IEnumerable<(int band, List<string> ids, int gold)> StreamRows(string path)
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
                    if (!int.TryParse(parts[0], out int band) || band < 0) continue;

                    int gold = 0;
                    int at = parts[1].IndexOf('@');
                    if (at > 0) int.TryParse(parts[1].Substring(0, at), out gold);

                    var ids = new List<string>();
                    if (parts[2].Length > 0)
                        foreach (var tok in parts[2].Split(','))
                        {
                            int a2 = tok.IndexOf('@');
                            string id = a2 > 0 ? tok.Substring(0, a2) : tok;
                            if (id.Length > 0) ids.Add(id);
                        }
                    yield return (band, ids, gold);
                }
            }
        }

        /// <summary>grant_runs.csv を読んで ITT 効果量を推定し、 <paramref name="learningRoot"/> へ書く。
        /// 行数が足りなければ何もせず false。</summary>
        public static bool Recompute(string grantCsvPath, string learningRoot)
        {
            _coefs.Clear(); _loaded = false;
            if (!File.Exists(grantCsvPath))
            { _lastSummary = $"grant_runs.csv が無い: {grantCsvPath}"; return false; }

            // ---- 1 巡目: 列の語彙と処置ラン数 ----
            var treated = new Dictionary<string, int>(StringComparer.Ordinal);
            var goldSeen = new SortedSet<int>();
            int rows = 0;
            foreach (var r in StreamRows(grantCsvPath))
            {
                rows++;
                foreach (var id in r.ids)
                    treated[id] = treated.TryGetValue(id, out int c) ? c + 1 : 1;
                if (r.gold > 0) goldSeen.Add(r.gold);
            }
            if (rows < MinRowsForFit)
            { _lastSummary = $"行数 {rows} < {MinRowsForFit} → 推定しない"; return false; }

            // 列を確定。 処置が 1 度も無い品は列に入れない (特異になる)。
            var cols = new List<string>();
            foreach (var kv in treated) if (kv.Value > 0) cols.Add(kv.Key);
            cols.Sort(StringComparer.Ordinal);              // 決定的な列順
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < cols.Count; i++) index[cols[i]] = i;
            var goldCols = new List<int>(goldSeen);
            int D = cols.Count + goldCols.Count + 1;        // +1 = 切片 (末尾)
            int INTERCEPT = D - 1;

            // ---- 2 巡目: 正規方程式 ----
            //   1 行の非ゼロ列は 4〜6 個しかないので、 疎に足す (D² を毎行触らない)。
            var xtx = new double[D, D];
            var xty = new double[D];
            double ysum = 0, ysq = 0;
            int n = 0;
            var active = new List<int>(8);
            foreach (var r in StreamRows(grantCsvPath))
            {
                active.Clear();
                foreach (var id in r.ids) if (index.TryGetValue(id, out int j)) active.Add(j);
                if (r.gold > 0)
                {
                    int gi = goldCols.IndexOf(r.gold);
                    if (gi >= 0) active.Add(cols.Count + gi);
                }
                active.Add(INTERCEPT);

                double y = r.band;
                for (int a = 0; a < active.Count; a++)
                {
                    int ja = active[a];
                    xty[ja] += y;
                    for (int b = 0; b < active.Count; b++) xtx[ja, active[b]] += 1.0;
                }
                ysum += y; ysq += y * y; n++;
            }
            LastN = n;

            // ---- ridge 解 + 逆行列 (SE 用) ----
            for (int i = 0; i < D; i++) if (i != INTERCEPT) xtx[i, i] += Ridge;
            if (!InvertInPlace(xtx, D, out double[,] inv))
            { _lastSummary = "正規方程式が特異 — 推定できない"; return false; }

            var beta = new double[D];
            for (int i = 0; i < D; i++)
            {
                double s = 0;
                for (int j = 0; j < D; j++) s += inv[i, j] * xty[j];
                beta[i] = s;
            }

            // 残差平方和 = yᵀy − 2βᵀXᵀy + βᵀ(XᵀX)β。 XᵀX は ridge 加算後なので
            //   厳密には近似 (ridge ぶんだけ RSS を過大に見積もる ＝ SE は保守側)。
            double rss = ysq;
            for (int i = 0; i < D; i++) rss -= 2.0 * beta[i] * xty[i];
            for (int i = 0; i < D; i++)
            {
                double s = 0;
                for (int j = 0; j < D; j++) s += xtx[i, j] * beta[j];
                rss += beta[i] * s;
            }
            int dof = Math.Max(1, n - D);
            double s2 = Math.Max(0, rss) / dof;
            Sigma = Math.Sqrt(s2);

            for (int i = 0; i < cols.Count; i++)
                _coefs[cols[i]] = new Coef
                {
                    beta = beta[i],
                    se = Math.Sqrt(Math.Max(0, s2 * inv[i, i])),
                    treated = treated[cols[i]],
                };

            int trustedCount = 0;
            foreach (var kv in _coefs) if (kv.Value.treated >= MinTreatedRuns) trustedCount++;
            var gsb = new StringBuilder();
            for (int g = 0; g < goldCols.Count; g++)
                gsb.Append(g > 0 ? " / " : "").Append(goldCols[g]).Append("G=")
                   .Append(beta[cols.Count + g].ToString("F4", CultureInfo.InvariantCulture));
            _lastSummary = $"ITT: N={n} 品={cols.Count} (信用 {trustedCount} / 処置≥{MinTreatedRuns})"
                         + $" σ={Sigma:F2} 中央SE={MedianSe():F4} 金[{gsb}]";
            _loaded = true;

            try { Write(learningRoot, cols, goldCols, beta, inv, s2); }
            catch (Exception e) { Debug.LogWarning($"[GrantItt] 書込失敗: {e.Message}"); }
            Debug.Log("[GrantItt] " + _lastSummary);
            return true;
        }

        private static double MedianSe()
        {
            var v = new List<double>();
            foreach (var kv in _coefs) if (kv.Value.treated >= MinTreatedRuns) v.Add(kv.Value.se);
            if (v.Count == 0) return double.NaN;
            v.Sort();
            return v[v.Count / 2];
        }

        /// <summary>Gauss-Jordan。 D ≈ 160 なので素直に全逆行列を作る (SE に対角が要る)。</summary>
        private static bool InvertInPlace(double[,] a, int D, out double[,] inv)
        {
            inv = new double[D, D];
            var m = new double[D, D];
            for (int i = 0; i < D; i++) { for (int j = 0; j < D; j++) m[i, j] = a[i, j]; inv[i, i] = 1.0; }

            for (int c = 0; c < D; c++)
            {
                int piv = c; double best = Math.Abs(m[c, c]);
                for (int r = c + 1; r < D; r++)
                { double v = Math.Abs(m[r, c]); if (v > best) { best = v; piv = r; } }
                if (best < 1e-12) return false;
                if (piv != c)
                    for (int j = 0; j < D; j++)
                    {
                        double t = m[c, j]; m[c, j] = m[piv, j]; m[piv, j] = t;
                        t = inv[c, j]; inv[c, j] = inv[piv, j]; inv[piv, j] = t;
                    }
                double d = m[c, c];
                for (int j = 0; j < D; j++) { m[c, j] /= d; inv[c, j] /= d; }
                for (int r = 0; r < D; r++)
                {
                    if (r == c) continue;
                    double f = m[r, c];
                    if (f == 0) continue;
                    for (int j = 0; j < D; j++) { m[r, j] -= f * m[c, j]; inv[r, j] -= f * inv[c, j]; }
                }
            }
            return true;
        }

        // ===== 永続化 =====

        [Serializable] private class Row { public string id; public double beta; public double se; public int treated; }
        [Serializable] private class File_ { public int n; public double sigma; public List<Row> items = new List<Row>(); }

        private static void Write(string learningRoot, List<string> cols, List<int> goldCols,
                                  double[] beta, double[,] inv, double s2)
        {
            Directory.CreateDirectory(learningRoot);
            var f = new File_ { n = LastN, sigma = Sigma };
            foreach (var kv in _coefs)
                f.items.Add(new Row { id = kv.Key, beta = kv.Value.beta, se = kv.Value.se, treated = kv.Value.treated });
            f.items.Sort((x, y) => y.beta.CompareTo(x.beta));
            System.IO.File.WriteAllText(EffectsPath(learningRoot), JsonUtility.ToJson(f, true), Encoding.UTF8);

            var sb = new StringBuilder();
            sb.AppendLine("=== ランダム付与 ITT 効果量 (band 単位) ===");
            sb.AppendLine(_lastSummary);
            sb.AppendLine("推定量: 「層 1〜5 のランダムな時点でこの品を渡されること」の効果。");
            sb.AppendLine("        BOT が選んで買うことの効果ではない。 層別の効き方は潰れている。");
            sb.AppendLine();
            for (int g = 0; g < goldCols.Count; g++)
                sb.AppendLine($"  [金 {goldCols[g]}G]  {beta[cols.Count + g]:F4}"
                            + $"  (1G あたり {beta[cols.Count + g] / goldCols[g]:F5})");
            sb.AppendLine();
            sb.AppendLine("品                              効果      SE      t     処置ラン");
            foreach (var r in f.items)
                sb.AppendLine($"  {r.id,-28} {r.beta,7:F4} {r.se,7:F4} {(r.se > 0 ? r.beta / r.se : 0),6:F2} {r.treated,8}"
                            + (r.treated >= MinTreatedRuns ? "" : "  ※標本不足"));
            System.IO.File.WriteAllText(ReportPath(learningRoot), sb.ToString(), Encoding.UTF8);
        }

        // ==========================================================================
        //  離散時間生存モデル ── **クリア率を直接の目的関数にする** (2026-09-09)
        //
        //  bandScore は 0〜11 の連続値ではなく「どこまで進んで死んだか」＝**打ち切りつきの
        //  離散生存データ**。 線形回帰を当てると
        //    「3層で死ぬランを4層へ運ぶ」 と 「6層のランをクリアさせる」 が**同点**になる。
        //  band 平均を上げる買い物と、 クリア率を上げる買い物は別物なので、
        //  目的がクリア率なら段ごとのハザードを推定して掛け合わせる。
        //
        //      logit h_k = α_k + Σ βᵢ·zᵢ_k        h_k = P(段 k で止まる | 段 k に到達)
        //      P(クリア)  = Π_k (1 − h_k)
        //
        //  <b>二値 1[band==11] に落とすより効率が良い。</b> 3層で死んだランも
        //  1〜3 段のハザードに寄与するので、 全ランが全段の推定に効く。
        //
        //  <b>付与層が自然に入る。</b> zᵢ_k は「段 k を踏む時点でその品を持っていたか」。
        //  5層で配られた品は 1〜4 段のハザードに影響しない ── 線形モデルでは
        //  この希釈が効果量に混ざっていた。 副産物として**層ごとの価値**が出る
        //  (段をまたいで β が一定という比例仮定のもとで)。
        // ==========================================================================

        /// <summary>band レベル k を踏む階層。 R1a〜R1d(=band 1) は 1〜2層をまとめている。
        /// band 5 (4Fボス死) と 8 (5F/6Fクリア) は実測で出現しないが、 欠けても壊れない。</summary>
        private static readonly int[] FloorOfLevel = { 0, 2, 3, 3, 4, 4, 5, 5, 5, 6, 7, 7 };
        private const int MaxLevel = 11;
        /// <summary>付与層の台。 <see cref="AutoRunner"/> は 1〜5 から一様に引く。</summary>
        private static readonly int[] GrantFloors = { 1, 2, 3, 4, 5 };

        private static readonly Dictionary<string, double> _clearEffect =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private static readonly Dictionary<string, double> _clearSe =
            new Dictionary<string, double>(StringComparer.Ordinal);
        /// <summary>付与層別の Δクリア率 (pt)。 [id][層1..5]。</summary>
        private static readonly Dictionary<string, double[]> _clearByFloor =
            new Dictionary<string, double[]>(StringComparer.Ordinal);

        /// <summary>基準クリア率 (何も配られなかったランの推定値)。</summary>
        public static double BaselineClear { get; private set; } = double.NaN;
        public static bool SurvivalLoaded { get; private set; }
        public static string SurvivalSummary { get; private set; } = "(未計算)";

        /// <summary><b>この品を配られるとクリア率が何 pt 動くか。</b> 付与層 1〜5 の一様平均。
        /// これが目的関数と同じ単位の唯一の量。</summary>
        public static bool TryGetClearEffect(string id, out double deltaPt, out double sePt)
        {
            deltaPt = 0; sePt = 0;
            if (string.IsNullOrEmpty(id) || !_clearEffect.TryGetValue(id, out deltaPt)) return false;
            _clearSe.TryGetValue(id, out sePt);
            return true;
        }

        /// <summary>指定した層で配られた場合の Δクリア率 (pt)。 層は 1〜5。</summary>
        public static double ClearEffectAtFloor(string id, int floor)
        {
            if (string.IsNullOrEmpty(id) || !_clearByFloor.TryGetValue(id, out var arr)) return 0;
            int i = Mathf.Clamp(floor, 1, 5) - 1;
            return arr[i];
        }

        private sealed class Run
        {
            public int band;
            public int[] gItem;      // 列インデックス
            public int[] gFloor;     // 付与層 (1〜5)
            public int goldCol;      // 無ければ -1
            public int goldFloor;
        }

        /// <summary>離散時間ハザードを当て、 Δクリア率へ変換する。
        /// <see cref="Recompute"/> の後に呼ぶこと (列の語彙を共有する)。</summary>
        public static bool RecomputeSurvival(string grantCsvPath, string learningRoot)
        {
            _clearEffect.Clear(); _clearSe.Clear(); _clearByFloor.Clear();
            SurvivalLoaded = false;
            if (!File.Exists(grantCsvPath)) { SurvivalSummary = "grant_runs.csv が無い"; return false; }

            // ---- 語彙 (Recompute と同じ列) ----
            var cols = new List<string>(_coefs.Keys);
            cols.Sort(StringComparer.Ordinal);
            if (cols.Count == 0) { SurvivalSummary = "先に Recompute を呼ぶこと"; return false; }
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < cols.Count; i++) index[cols[i]] = i;

            var goldSeen = new SortedSet<int>();
            var runs = new List<Run>(70000);
            foreach (var r in StreamRowsFull(grantCsvPath))
            {
                if (r.band < 1 || r.band > MaxLevel) continue;
                var gi = new List<int>(6); var gf = new List<int>(6);
                for (int i = 0; i < r.ids.Count; i++)
                    if (index.TryGetValue(r.ids[i], out int j)) { gi.Add(j); gf.Add(r.floors[i]); }
                if (r.gold > 0) goldSeen.Add(r.gold);
                runs.Add(new Run { band = r.band, gItem = gi.ToArray(), gFloor = gf.ToArray(),
                                   goldCol = r.gold, goldFloor = r.goldFloor });
            }
            if (runs.Count < MinRowsForFit) { SurvivalSummary = $"行数 {runs.Count} < {MinRowsForFit}"; return false; }

            var goldCols = new List<int>(goldSeen);
            int NI = cols.Count, NG = goldCols.Count, NS = MaxLevel - 1;   // 段 k=2..11
            int D = NI + NG + NS;
            int SBASE = NI + NG;                                            // 段切片の先頭
            for (int i = 0; i < runs.Count; i++)
                if (runs[i].goldCol > 0) runs[i].goldCol = goldCols.IndexOf(runs[i].goldCol);
                else runs[i].goldCol = -1;

            // ---- IRLS ----
            var beta = new double[D];
            for (int s = 0; s < NS; s++) beta[SBASE + s] = -2.0;   // 素直な初期値
            var act = new int[16];
            double[,] hess = null; double[] grad = null;
            int obsTotal = 0;
            for (int iter = 0; iter < 25; iter++)
            {
                hess = new double[D, D]; grad = new double[D];
                obsTotal = 0;
                foreach (var run in runs)
                {
                    int lastStage = Math.Min(run.band + 1, MaxLevel);
                    for (int k = 2; k <= lastStage; k++)
                    {
                        int na = 0;
                        act[na++] = SBASE + (k - 2);
                        int fk = FloorOfLevel[k];
                        for (int g = 0; g < run.gItem.Length; g++)
                            if (run.gFloor[g] <= fk && na < act.Length) act[na++] = run.gItem[g];
                        if (run.goldCol >= 0 && run.goldFloor <= fk && na < act.Length)
                            act[na++] = NI + run.goldCol;

                        double eta = 0;
                        for (int a = 0; a < na; a++) eta += beta[act[a]];
                        double mu = 1.0 / (1.0 + Math.Exp(-eta));
                        double w = Math.Max(1e-6, mu * (1 - mu));
                        double y = (run.band == k - 1) ? 1.0 : 0.0;
                        double resid = y - mu;
                        for (int a = 0; a < na; a++)
                        {
                            grad[act[a]] += resid;
                            for (int b = 0; b < na; b++) hess[act[a], act[b]] += w;
                        }
                        obsTotal++;
                    }
                }
                for (int i = 0; i < NI + NG; i++) hess[i, i] += Ridge;   // 数値安定のみ
                if (!InvertInPlace(hess, D, out double[,] hinv))
                { SurvivalSummary = "ヘッセ行列が特異"; return false; }

                double maxStep = 0;
                for (int i = 0; i < D; i++)
                {
                    double d = 0;
                    for (int j = 0; j < D; j++) d += hinv[i, j] * grad[j];
                    // 発散防止のダンピング。 疎な列で 1 歩が飛ぶことがある。
                    d = Math.Max(-1.0, Math.Min(1.0, d));
                    beta[i] += d;
                    maxStep = Math.Max(maxStep, Math.Abs(d));
                }
                if (maxStep < 1e-6) break;
            }

            // ---- 分散 (最終ヘッセの逆) ----
            for (int i = 0; i < NI + NG; i++) hess[i, i] += Ridge;
            InvertInPlace(hess, D, out double[,] cov);

            // ---- Δクリア率へ変換 ----
            var alpha = new double[MaxLevel + 1];
            for (int k = 2; k <= MaxLevel; k++) alpha[k] = beta[SBASE + (k - 2)];
            double p0 = ClearProb(alpha, 0.0, 0);
            BaselineClear = p0;

            for (int i = 0; i < NI; i++)
            {
                double b = beta[i];
                var perFloor = new double[5];
                double mean = 0;
                for (int f = 0; f < GrantFloors.Length; f++)
                {
                    double d = (ClearProb(alpha, b, GrantFloors[f]) - p0) * 100.0;   // pt
                    perFloor[f] = d; mean += d;
                }
                mean /= GrantFloors.Length;
                // デルタ法: ∂Δ/∂β を数値微分し SE(β) を伝播させる (段切片の共分散は無視)。
                double h = 1e-3, up = 0, dn = 0;
                for (int f = 0; f < GrantFloors.Length; f++)
                {
                    up += ClearProb(alpha, b + h, GrantFloors[f]);
                    dn += ClearProb(alpha, b - h, GrantFloors[f]);
                }
                double deriv = (up - dn) / (2 * h * GrantFloors.Length) * 100.0;
                double seB = Math.Sqrt(Math.Max(0, cov[i, i]));
                _clearEffect[cols[i]] = mean;
                _clearSe[cols[i]] = Math.Abs(deriv) * seB;
                _clearByFloor[cols[i]] = perFloor;
            }

            int trusted = 0; foreach (var kv in _coefs) if (kv.Value.treated >= MinTreatedRuns) trusted++;
            SurvivalSummary = $"生存ITT: ラン={runs.Count} 段観測={obsTotal} 品={NI} (信用 {trusted})"
                            + $" 基準クリア率={p0 * 100:F1}%";
            SurvivalLoaded = true;
            SurvivalN = runs.Count;
            try { WriteSurvival(learningRoot, cols, goldCols, beta, NI, alpha, p0, runs.Count); }
            catch (Exception e) { Debug.LogWarning($"[GrantItt] 生存レポート書込失敗: {e.Message}"); }
            Debug.Log("[GrantItt] " + SurvivalSummary);
            return true;
        }

        /// <summary>付与層 <paramref name="grantFloor"/> で係数 <paramref name="b"/> を持つときのクリア確率。
        /// grantFloor=0 は「配られない」。</summary>
        private static double ClearProb(double[] alpha, double b, int grantFloor)
        {
            double p = 1.0;
            for (int k = 2; k <= MaxLevel; k++)
            {
                double eta = alpha[k];
                if (grantFloor > 0 && grantFloor <= FloorOfLevel[k]) eta += b;
                p *= 1.0 - 1.0 / (1.0 + Math.Exp(-eta));
            }
            return p;
        }

        private struct FullRow { public int band; public List<string> ids; public List<int> floors; public int gold; public int goldFloor; }

        private static IEnumerable<FullRow> StreamRowsFull(string path)
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
                    if (!int.TryParse(parts[0], out int band) || band < 0) continue;
                    int gold = 0, gfl = 0, at = parts[1].IndexOf('@');
                    if (at > 0)
                    {
                        int.TryParse(parts[1].Substring(0, at), out gold);
                        int.TryParse(parts[1].Substring(at + 1), out gfl);
                    }
                    var ids = new List<string>(); var fls = new List<int>();
                    if (parts[2].Length > 0)
                        foreach (var tok in parts[2].Split(','))
                        {
                            int a2 = tok.IndexOf('@');
                            if (a2 <= 0) continue;
                            ids.Add(tok.Substring(0, a2));
                            int.TryParse(tok.Substring(a2 + 1), out int f);
                            fls.Add(f <= 0 ? 1 : f);
                        }
                    yield return new FullRow { band = band, ids = ids, floors = fls, gold = gold, goldFloor = gfl };
                }
            }
        }

        [Serializable] private class ClearRow { public string id; public double dClearPt; public double sePt; public int treated; public double[] byFloor; }
        [Serializable] private class ClearFile { public int n; public double baselineClear; public List<ClearRow> items = new List<ClearRow>(); }

        /// <summary>生存 fit に使ったラン数。 <b>序列を決めているのはこちら</b>なので、
        /// 条件表示にはこの値を出すこと ── <see cref="LastN"/> は線形 fit (診断用) の n で、
        /// 別ファイルから来るため一致するとは限らない (2026-09-10 に実際に食い違い、
        /// 15,000ラン の表で測ったバッチが「N=30000」と表示されていた)。</summary>
        public static int SurvivalN { get; private set; }

        public static string ClearPath(string learningRoot) => Path.Combine(learningRoot, "itt_clear.json");

        private static void WriteSurvival(string learningRoot, List<string> cols, List<int> goldCols,
                                          double[] beta, int NI, double[] alpha, double p0, int runs)
        {
            Directory.CreateDirectory(learningRoot);
            var f = new ClearFile { n = runs, baselineClear = p0 };
            foreach (var kv in _clearEffect)
                f.items.Add(new ClearRow
                {
                    id = kv.Key, dClearPt = kv.Value,
                    sePt = _clearSe.TryGetValue(kv.Key, out double s) ? s : 0,
                    treated = TreatedRuns(kv.Key),
                    byFloor = _clearByFloor[kv.Key],
                });
            f.items.Sort((x, y) => y.dClearPt.CompareTo(x.dClearPt));
            System.IO.File.WriteAllText(ClearPath(learningRoot), JsonUtility.ToJson(f, true), Encoding.UTF8);

            var sb = new StringBuilder();
            sb.AppendLine("=== ランダム付与 ITT / 離散時間生存 ── Δクリア率 (pt) ===");
            sb.AppendLine(SurvivalSummary);
            sb.AppendLine("単位はクリア率のパーセントポイント。 「この品を層 g で渡されると");
            sb.AppendLine("クリア率が基準から何 pt 動くか」。 段をまたいで log オッズ効果が");
            sb.AppendLine("一定という比例仮定のもとで、 層別の値が出ている。");
            sb.AppendLine();
            for (int g = 0; g < goldCols.Count; g++)
            {
                double bg = beta[NI + g];
                sb.AppendLine($"  [金 {goldCols[g]}G]  Δクリア {(ClearProb(alpha, bg, 3) - p0) * 100:F3}pt (層3で受領時)");
            }
            sb.AppendLine();
            sb.AppendLine("品                             Δクリア    SE      t   |  層1    層2    層3    層4    層5   処置");
            foreach (var r in f.items)
                sb.AppendLine($"  {r.id,-28} {r.dClearPt,7:F3} {r.sePt,6:F3} {(r.sePt > 0 ? r.dClearPt / r.sePt : 0),6:F2}  |"
                            + $"{r.byFloor[0],6:F2} {r.byFloor[1],6:F2} {r.byFloor[2],6:F2} {r.byFloor[3],6:F2} {r.byFloor[4],6:F2} {r.treated,7}"
                            + (r.treated >= MinTreatedRuns ? "" : "  ※標本不足"));
            System.IO.File.WriteAllText(Path.Combine(learningRoot, "itt_clear.txt"), sb.ToString(), Encoding.UTF8);
        }

        public static bool TryLoadClear(string learningRoot)
        {
            string p = ClearPath(learningRoot);
            if (!System.IO.File.Exists(p)) return false;
            try
            {
                var f = JsonUtility.FromJson<ClearFile>(System.IO.File.ReadAllText(p, Encoding.UTF8));
                if (f?.items == null || f.items.Count == 0) return false;
                _clearEffect.Clear(); _clearSe.Clear(); _clearByFloor.Clear();
                foreach (var r in f.items)
                {
                    _clearEffect[r.id] = r.dClearPt;
                    _clearSe[r.id] = r.sePt;
                    _clearByFloor[r.id] = r.byFloor ?? new double[5];
                }
                BaselineClear = f.baselineClear;
                SurvivalN = f.n;
                SurvivalLoaded = true;
                SurvivalSummary = $"生存ITT (ディスク): N={f.n} 品={_clearEffect.Count} 基準クリア率={f.baselineClear * 100:F1}%";
                return true;
            }
            catch (Exception e) { Debug.LogWarning($"[GrantItt] 生存読込失敗: {e.Message}"); return false; }
        }

        /// <summary>ディスクの itt_effects.json を読む (再計算せずに使いたい場合)。</summary>
        public static bool TryLoad(string learningRoot)
        {
            string p = EffectsPath(learningRoot);
            if (!System.IO.File.Exists(p)) return false;
            try
            {
                var f = JsonUtility.FromJson<File_>(System.IO.File.ReadAllText(p, Encoding.UTF8));
                if (f?.items == null || f.items.Count == 0) return false;
                _coefs.Clear();
                foreach (var r in f.items)
                    _coefs[r.id] = new Coef { beta = r.beta, se = r.se, treated = r.treated };
                LastN = f.n; Sigma = f.sigma; _loaded = true;
                int t = 0; foreach (var kv in _coefs) if (kv.Value.treated >= MinTreatedRuns) t++;
                _lastSummary = $"ITT (ディスク): N={f.n} 品={_coefs.Count} 信用 {t} σ={f.sigma:F2}";
                return true;
            }
            catch (Exception e) { Debug.LogWarning($"[GrantItt] 読込失敗: {e.Message}"); return false; }
        }
    }
}

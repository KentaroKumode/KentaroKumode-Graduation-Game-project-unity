using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// PolicyParameters の自動探索 (改良版)。
    ///
    /// 改良点:
    ///   (A) **ペアテスト**: バッチを baseline/challenger で交互実行、 同シードペアの diff で評価
    ///       → シード起因ノイズが消えて SEM が 1/5〜1/10 に下がる
    ///   (B) **多軸同時摂動**: 1回のバッチで複数軸 (デフォルト2-3軸) を同時にガウスサンプリング
    ///       → 軸間の相互作用を捉える、 探索効率UP
    ///   (C) **composite score**: bandScore + 6F/7F/clear/解脱 の加重ブレンドを評価軸に使用
    ///   (D) **スタール脱出**: K回連続棄却で摂動幅を拡大 (温度上昇)
    ///
    /// 動作フロー:
    ///   1. バッチ開始前: 現policy を baseline として固定、 多軸摂動した challenger を生成
    ///   2. AutoRunner.PairedChallengerPolicy にセット → ペアテスト ON
    ///   3. バッチ実行 (1000ランなら 500 baseline + 500 challenger 交互)
    ///   4. バッチ終了: ペア diff の平均 / paired SEM で採否判定
    ///   5. 採用なら challenger を policy.json + policy_best.json に書き込み
    ///   6. 次回も新 challenger を生成 → 繰り返し
    /// </summary>
    public static class PolicyExplorer
    {
        // ===== ファイル =====
        private static string PolicyDir(string root)
            => string.IsNullOrEmpty(root)
                ? MetaProfileHelper.LearningRoot()
                : root;
        private static string HistoryPath(string root) => Path.Combine(PolicyDir(root), "policy_history.jsonl");
        private static string BestPath(string root)    => Path.Combine(PolicyDir(root), "policy_best.json");
        private static string HealthPath(string root)  => Path.Combine(PolicyDir(root), "policy_health.json");
        private static string StallStatePath(string root) => Path.Combine(PolicyDir(root), "policy_stall.json");

        // ===== ゲート/閾値 =====
        public const int MinBatchForL2 = 200;
        public const int MinBatchForL1 = 50;
        public const float SEMConfidenceK = 1.96f;
        /// <summary>1バッチでの多軸同時摂動軸数 (デフォルト)。</summary>
        public const int AxesPerBatchDefault = 2;
        /// <summary>K回連続棄却でスタール脱出ブースト (step を ×1.5)。</summary>
        public const int StallRejectionLimit = 5;
        /// <summary>スタール時の step 倍率。</summary>
        public const float StallStepMultiplier = 1.5f;

        // ===== 摂動軸 =====
        private struct Axis
        {
            public string name;
            public Action<PolicyParameters, float> apply;
            public float step;
            public Func<PolicyParameters, float> read;
        }

        private static readonly List<Axis> Axes = new List<Axis>
        {
            new Axis { name = "rerollCostRatio",          step = 0.05f,
                       apply = (p, d) => p.rerollCostRatio += d,
                       read  = p => p.rerollCostRatio },
            new Axis { name = "consumableStockMax",       step = 1f,
                       apply = (p, d) => p.consumableStockMax = Mathf.RoundToInt(p.consumableStockMax + d),
                       read  = p => p.consumableStockMax },
            new Axis { name = "robberyMinHpRatio",        step = 0.05f,
                       apply = (p, d) => p.robberyMinHpRatio += d,
                       read  = p => p.robberyMinHpRatio },
            new Axis { name = "eventExplorationRate",     step = 0.03f,
                       apply = (p, d) => p.eventExplorationRate += d,
                       read  = p => p.eventExplorationRate },
            new Axis { name = "importantThreatThreshold", step = 1f,
                       apply = (p, d) => p.importantThreatThreshold = Mathf.RoundToInt(p.importantThreatThreshold + d),
                       read  = p => p.importantThreatThreshold },
            new Axis { name = "emergencyHealRatio",       step = 0.10f,
                       apply = (p, d) => p.emergencyHealRatio += d,
                       read  = p => p.emergencyHealRatio },
            new Axis { name = "hpLowThreshold",           step = 0.05f,
                       apply = (p, d) => p.hpLowThreshold += d,
                       read  = p => p.hpLowThreshold },
            new Axis { name = "hpCritThreshold",          step = 0.05f,
                       apply = (p, d) => p.hpCritThreshold += d,
                       read  = p => p.hpCritThreshold },
            new Axis { name = "lateralHopeFloor",         step = 5f,
                       apply = (p, d) => p.lateralHopeFloor += d,
                       read  = p => p.lateralHopeFloor },
            new Axis { name = "hopeRefillFloor",          step = 5f,
                       apply = (p, d) => p.hopeRefillFloor += d,
                       read  = p => p.hopeRefillFloor },
            new Axis { name = "sublimationReserve",       step = 1f,
                       apply = (p, d) => p.sublimationReserve += d,
                       read  = p => p.sublimationReserve },
            new Axis { name = "stanceDefendWinProb",      step = 0.05f,
                       apply = (p, d) => p.stanceDefendWinProb += d,
                       read  = p => p.stanceDefendWinProb },
            new Axis { name = "stanceDefendHpBias",       step = 0.05f,
                       apply = (p, d) => p.stanceDefendHpBias += d,
                       read  = p => p.stanceDefendHpBias },
        };

        // ===== スタール状態 =====
        [Serializable] private class StallState { public int rejectionStreak; }
        private static StallState LoadStall(string root)
        {
            try
            {
                string p = StallStatePath(root);
                if (File.Exists(p)) return JsonUtility.FromJson<StallState>(File.ReadAllText(p, Encoding.UTF8));
            }
            catch { }
            return new StallState();
        }
        private static void SaveStall(string root, StallState s)
        {
            try
            {
                string p = StallStatePath(root);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonUtility.ToJson(s), new UTF8Encoding(false));
            }
            catch { }
        }

        // ============================================================
        //  バッチ開始時: 挑戦者ポリシーを生成して AutoRunner にセット
        // ============================================================

        /// <summary>
        /// 多軸ガウス摂動で挑戦者ポリシーを生成、 AutoRunner.PairedChallengerPolicy にセット。
        /// バッチ開始直前 (RunBatch の policy ロード後) に呼ぶ。
        /// </summary>
        public static void PrepareChallenger(string learningRoot = null)
        {
            var baseline = PolicyParameters.Current.Clone();
            var stall = LoadStall(learningRoot);
            float stepMul = stall.rejectionStreak >= StallRejectionLimit ? StallStepMultiplier : 1.0f;

            // 摂動する軸を AxesPerBatchDefault 個ランダム選択
            var rng = new System.Random();
            var idxs = new List<int>(Axes.Count);
            for (int i = 0; i < Axes.Count; i++) idxs.Add(i);
            // shuffle
            for (int i = idxs.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (idxs[i], idxs[j]) = (idxs[j], idxs[i]);
            }
            int axesUsed = Mathf.Min(AxesPerBatchDefault, Axes.Count);
            var challenger = baseline.Clone();
            var perturbInfo = new List<string>();
            for (int k = 0; k < axesUsed; k++)
            {
                var axis = Axes[idxs[k]];
                // ガウスサンプリング (Box-Muller風だが簡略化)
                float u1 = (float)rng.NextDouble();
                float u2 = (float)rng.NextDouble();
                float gauss = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-6f, u1)))
                              * Mathf.Cos(2f * Mathf.PI * u2);
                gauss = Mathf.Clamp(gauss, -2f, 2f); // 極端値カット
                float delta = gauss * axis.step * stepMul;
                float before = axis.read(challenger);
                axis.apply(challenger, delta);
                challenger.Clamp();
                float after = axis.read(challenger);
                perturbInfo.Add($"{axis.name}:{before:F2}→{after:F2}({delta:+0.00;-0.00})");
            }

            AutoRunner.PairedChallengerPolicy = challenger;
            string stallTag = stall.rejectionStreak >= StallRejectionLimit ? $" [STALL BOOST ×{StallStepMultiplier}]" : "";
            Debug.Log($"[PolicyExplorer] 挑戦者生成 ({axesUsed}軸){stallTag}: {string.Join(" / ", perturbInfo)}");
        }

        // ============================================================
        //  バッチ終了時: ペア diff で評価
        // ============================================================

        public static void AssessAndPropose(IList<AutoRunner.RunRec> recs, string learningRoot = null)
        {
            try
            {
                if (recs == null || recs.Count == 0) return;
                var stall = LoadStall(learningRoot);

                // ペア diff の計算: 同 pairedSeed の baseline と challenger をマッチング
                var byPair = new Dictionary<int, (float baseline, float challenger)>();
                int valid = 0;
                foreach (var r in recs)
                {
                    if (r == null) continue;
                    float s = PolicyObjective.Compute(r);
                    if (float.IsNaN(s)) continue;
                    valid++;
                    if (string.IsNullOrEmpty(r.policyVariant)) continue; // ペアテスト OFF時
                    if (!byPair.TryGetValue(r.pairedSeed, out var pair))
                        pair = (float.NaN, float.NaN);
                    if (r.policyVariant == "baseline")   pair.baseline = s;
                    if (r.policyVariant == "challenger") pair.challenger = s;
                    byPair[r.pairedSeed] = pair;
                }

                bool pairedMode = AutoRunner.PairedChallengerPolicy != null && byPair.Count > 0;
                float compositeMean = 0;
                if (valid > 0)
                {
                    double s2 = 0;
                    foreach (var r in recs)
                    {
                        float x = PolicyObjective.Compute(r);
                        if (!float.IsNaN(x)) s2 += x;
                    }
                    compositeMean = (float)(s2 / valid);
                }

                if (valid < MinBatchForL2)
                {
                    Debug.Log($"[PolicyExplorer] サイズゲート: 有効{valid}ラン < {MinBatchForL2} → L2スキップ");
                    WriteHealth(learningRoot, compositeMean, 0f, valid, null, "skipped:size_gate", "n/a", 0f, 0f, pairedMode);
                    AutoRunner.PairedChallengerPolicy = null;
                    return;
                }

                string bestPath = BestPath(learningRoot);
                PolicyParameters best = null;
                if (File.Exists(bestPath))
                {
                    try { best = JsonUtility.FromJson<PolicyParameters>(File.ReadAllText(bestPath, Encoding.UTF8)); }
                    catch { best = null; }
                }

                bool accepted;
                string decision;
                float effectiveMargin;
                float diff = 0f, pairedSEM = 0f;
                PolicyParameters adopted = null;

                if (pairedMode)
                {
                    // ペアサンプル: diff = challenger - baseline 各ペア
                    var diffs = new List<float>();
                    foreach (var kv in byPair)
                    {
                        var p = kv.Value;
                        if (float.IsNaN(p.baseline) || float.IsNaN(p.challenger)) continue;
                        diffs.Add(p.challenger - p.baseline);
                    }
                    int n = diffs.Count;
                    if (n < MinBatchForL2 / 2)
                    {
                        Debug.LogWarning($"[PolicyExplorer] ペア成立 {n} 件 < {MinBatchForL2 / 2} → スキップ");
                        WriteHealth(learningRoot, compositeMean, 0f, valid, best, "skipped:few_pairs", "n/a", 0f, 0f, true);
                        AutoRunner.PairedChallengerPolicy = null;
                        return;
                    }
                    double sumD = 0, sumD2 = 0;
                    foreach (var d in diffs) { sumD += d; sumD2 += (double)d * d; }
                    diff = (float)(sumD / n);
                    double var = n > 1 ? (sumD2 - sumD * sumD / n) / (n - 1) : 0;
                    pairedSEM = (float)Math.Sqrt(var / n);
                    effectiveMargin = Mathf.Max(0.02f, SEMConfidenceK * pairedSEM);
                    accepted = diff > effectiveMargin;
                    decision = accepted
                        ? $"accepted-paired (Δ={diff:+0.00;-0.00} > margin {effectiveMargin:F3})"
                        : $"rejected-paired (Δ={diff:+0.00;-0.00} ≤ margin {effectiveMargin:F3})";
                    if (accepted) adopted = AutoRunner.PairedChallengerPolicy.Clone();
                }
                else
                {
                    // フォールバック: 旧来の絶対値比較 (composite で)
                    effectiveMargin = 0.05f;
                    if (best == null) { accepted = true; decision = "accepted:first_best (non-paired)"; }
                    else if (compositeMean >= best.lastBandScoreAvg + effectiveMargin)
                    { accepted = true; decision = $"accepted (Δ={compositeMean - best.lastBandScoreAvg:+0.00;-0.00} ≥ {effectiveMargin:F2})"; }
                    else
                    { accepted = false; decision = $"rejected (Δ={compositeMean - (best?.lastBandScoreAvg ?? 0):+0.00;-0.00} < {effectiveMargin:F2})"; }
                    if (accepted) adopted = PolicyParameters.Current.Clone();
                }

                if (accepted && adopted != null)
                {
                    adopted.lastBandScoreAvg = compositeMean;
                    adopted.trialBatches++;
                    Directory.CreateDirectory(PolicyDir(learningRoot));
                    File.WriteAllText(bestPath, JsonUtility.ToJson(adopted, true), new UTF8Encoding(false));
                    PolicyParameters.SetCurrent(adopted);
                    PolicyParameters.SaveToDisk(learningRoot);
                    Debug.Log($"[PolicyExplorer] {decision} (composite={compositeMean:F2}, pairedSEM={pairedSEM:F3})");
                    stall.rejectionStreak = 0;
                    LogAcceptedToChangelog(best, adopted, compositeMean, pairedSEM, diff, decision);
                }
                else
                {
                    Debug.Log($"[PolicyExplorer] {decision}");
                    stall.rejectionStreak++;
                    // ベースラインを再書き込み (current が challenger に汚染されている可能性ガード)
                    if (best != null) { PolicyParameters.SetCurrent(best); PolicyParameters.SaveToDisk(learningRoot); }
                }
                SaveStall(learningRoot, stall);
                AppendHistory(learningRoot, adopted ?? best, compositeMean, accepted, "multi-axis", diff);
                WriteHealth(learningRoot, compositeMean, pairedSEM, valid, best, decision, "multi-axis", diff, effectiveMargin, pairedMode);
                // クリーンアップ
                AutoRunner.PairedChallengerPolicy = null;
            }
            catch (Exception e) { Debug.LogWarning($"[PolicyExplorer] 例外: {e.Message}\n{e.StackTrace}"); }
        }

        // ============================================================
        //  Changelog 追記 (採用時のみ)
        // ============================================================
        private static void LogAcceptedToChangelog(PolicyParameters before, PolicyParameters after,
            float compositeMean, float pairedSEM, float diff, string decision)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"## {BotJudgmentLog.Now()} — L2 policy 更新採用 (composite={compositeMean:F2}, pairedSEM={pairedSEM:F3}, Δ={diff:+0.00;-0.00})");
            sb.AppendLine();
            sb.AppendLine($"- **判定**: {decision}");
            if (before != null)
            {
                AppendDiff(sb, "rerollCostRatio",          before.rerollCostRatio,          after.rerollCostRatio);
                AppendDiff(sb, "consumableStockMax",       before.consumableStockMax,       after.consumableStockMax);
                AppendDiff(sb, "robberyMinHpRatio",        before.robberyMinHpRatio,        after.robberyMinHpRatio);
                AppendDiff(sb, "eventExplorationRate",     before.eventExplorationRate,     after.eventExplorationRate);
                AppendDiff(sb, "importantThreatThreshold", before.importantThreatThreshold, after.importantThreatThreshold);
                AppendDiff(sb, "emergencyHealRatio",       before.emergencyHealRatio,       after.emergencyHealRatio);
                AppendDiff(sb, "hpLowThreshold",           before.hpLowThreshold,           after.hpLowThreshold);
                AppendDiff(sb, "hpCritThreshold",          before.hpCritThreshold,          after.hpCritThreshold);
                AppendDiff(sb, "lateralHopeFloor",         before.lateralHopeFloor,         after.lateralHopeFloor);
                AppendDiff(sb, "hopeRefillFloor",          before.hopeRefillFloor,          after.hopeRefillFloor);
                AppendDiff(sb, "sublimationReserve",       before.sublimationReserve,       after.sublimationReserve);
                AppendDiff(sb, "stanceDefendWinProb",      before.stanceDefendWinProb,      after.stanceDefendWinProb);
                AppendDiff(sb, "stanceDefendHpBias",       before.stanceDefendHpBias,       after.stanceDefendHpBias);
            }
            else sb.AppendLine("- (初回ベスト)");
            sb.AppendLine();
            sb.AppendLine("---");
            BotJudgmentLog.Append(sb.ToString());
        }

        private static void AppendDiff<T>(StringBuilder sb, string name, T before, T after) where T : IComparable
        {
            if (before.CompareTo(after) == 0) return;
            sb.AppendLine($"- **{name}**: `{before}` → `{after}`");
        }

        // ============================================================
        //  履歴 & ヘルス
        // ============================================================
        private static void AppendHistory(string root, PolicyParameters pol, float compositeMean,
            bool accepted, string axis, float diff)
        {
            try
            {
                if (pol == null) return;
                string path = HistoryPath(root);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"t\":\"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\",");
                sb.Append("\"comp\":").Append(compositeMean.ToString("F3")).Append(",");
                sb.Append("\"diff\":").Append(diff.ToString("F3")).Append(",");
                sb.Append("\"acc\":").Append(accepted ? "1" : "0").Append(",");
                sb.Append("\"ax\":\"").Append(axis).Append("\",");
                sb.Append("\"p\":\"").Append(pol.Summary().Replace("\"", "")).Append("\"");
                sb.Append("}\n");
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static void WriteHealth(string root, float compositeMean, float pairedSEM, int n,
            PolicyParameters best, string decision, string axisName, float diff, float effectiveMargin, bool paired)
        {
            try
            {
                string path = HealthPath(root);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"updatedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
                sb.AppendLine($"  \"mode\": \"{(paired ? "paired" : "absolute")}\",");
                sb.AppendLine($"  \"lastBatch\": {{");
                sb.AppendLine($"    \"n\": {n},");
                sb.AppendLine($"    \"compositeMean\": {compositeMean.ToString("F3")},");
                sb.AppendLine($"    \"pairedDiff\": {diff.ToString("F3")},");
                sb.AppendLine($"    \"pairedSEM\": {pairedSEM.ToString("F3")},");
                sb.AppendLine($"    \"ci95\": [{(diff - 1.96f * pairedSEM).ToString("F2")}, {(diff + 1.96f * pairedSEM).ToString("F2")}]");
                sb.AppendLine($"  }},");
                sb.AppendLine($"  \"best\": {{");
                sb.AppendLine($"    \"score\": {(best?.lastBandScoreAvg ?? 0f).ToString("F3")},");
                sb.AppendLine($"    \"summary\": \"{(best != null ? best.Summary().Replace("\"", "") : "n/a")}\"");
                sb.AppendLine($"  }},");
                sb.AppendLine($"  \"decision\": \"{decision}\",");
                sb.AppendLine($"  \"effectiveMargin\": {effectiveMargin.ToString("F3")},");
                sb.AppendLine($"  \"nextPerturb\": {{ \"axis\": \"{axisName}\", \"delta\": {diff.ToString("F3")} }},");
                sb.AppendLine($"  \"gates\": {{ \"minBatchForL1\": {MinBatchForL1}, \"minBatchForL2\": {MinBatchForL2}, \"semConfidenceK\": {SEMConfidenceK} }},");
                sb.Append("  \"diagnostics\": [");
                var diags = new List<string>();
                if (paired) diags.Add("\"OK: ペアテストでノイズ低減中\"");
                else        diags.Add("\"WARN: ペアテストOFF (絶対値比較。 ノイズ床高い)\"");
                if (n < MinBatchForL2)   diags.Add("\"WARN: バッチサイズ不足\"");
                else if (n < 500)        diags.Add("\"INFO: バッチ500未満\"");
                else                     diags.Add("\"OK: サイズ十分\"");
                if (pairedSEM > 0 && effectiveMargin > 0.5f)
                    diags.Add($"\"INFO: 採否マージン {effectiveMargin:F2} — まだ感度が荒い\"");
                sb.Append(string.Join(", ", diags));
                sb.AppendLine("]");
                sb.AppendLine("}");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning($"[PolicyExplorer] health write fail: {e.Message}"); }
        }
    }
}

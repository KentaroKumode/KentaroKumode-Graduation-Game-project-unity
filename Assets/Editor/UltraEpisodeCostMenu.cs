#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AutoTest.Ultra;
using MapSystem;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AutoTest.EditorTools
{
    /// <summary>Measures what one Ultra decision actually costs.
    ///
    /// <para><b>Measured, not estimated.</b> The compute budget for this stack has been
    /// mis-estimated four times, and every time the error came from guessing the unit rather
    /// than the number. Here the two costs that matter — process startup and the run itself —
    /// are separated by construction: a batch of N rollouts against a batch of 1 tells you the
    /// marginal cost, and the difference tells you the fixed one.</para>
    ///
    /// <para>Runs entirely outside play mode. The checkpoint is synthesised from a generated
    /// floor, which is enough for a cost measurement: what is being timed is the worker round
    /// trip, not the quality of the decision.</para></summary>
    public static class UltraEpisodeCostMenu
    {
        [MenuItem("Tools/AutoRun/Ultra AI/診断: rollout 1 決定のコスト実測", priority = 32)]
        public static void MeasureOneDecision()
        {
            if (!UltraProductionWorkerBuild.EnsureBuilt(out string buildError))
            {
                Debug.LogError("[UltraCost] worker build failed: " + buildError);
                return;
            }

            string workRoot = Path.Combine(Path.GetTempPath(),
                "ultra_cost_" + DateTime.Now.ToString("HHmmss"));

            // **checkpoint を採った条件と揃える。** 採取メニューは 0pt・遺物なしで走るので、
            //   canonical (50pt・理論値遺物) をそのまま使うと、 0pt のランを 50pt の
            //   ロードアウトへ復元することになる ── 条件が両側で食い違うという、
            //   2026-08-17 の portfolio 障害とまったく同じ形。
            UltraPortfolioProfileSpec profile = UltraPortfolioProtocol.CanonicalProfile();
            profile.challengeScore = 0;
            profile.theoreticalBestCursedRelic = false;
            UltraPortfolioPolicySpec policy = UltraPortfolioProtocol.CanonicalCandidates()[0];

            var oracle = new UltraProcessEpisodeOracle(
                UltraProductionWorkerBuild.ExecutablePath,
                UltraProductionWorkerBuild.BuildRoot,
                workRoot, profile, policy,
                AutoTest.PolicyParameters.Current?.Clone(),
                new string('a', 64))
            {
                EpisodeTimeoutSeconds = 300,
                // Keep job/response/worker.log. A cost probe whose rollouts fail is a
                // correctness probe, and the log is the only place the reason lives.
                KeepArtifacts = true,
            };
            Debug.Log("[UltraCost] artifacts: " + workRoot);

            if (!oracle.IsHealthy)
            {
                Debug.LogError("[UltraCost] oracle is unhealthy — worker missing at "
                    + UltraProductionWorkerBuild.ExecutablePath);
                return;
            }

            try
            {
                if (!TryBuildCheckpoint(out UltraCheckpoint checkpoint,
                        out Func<int, UltraResumePayload> payloads, out string why))
                {
                    Debug.LogError("[UltraCost] could not build a checkpoint: " + why);
                    return;
                }

                Debug.Log("[UltraCost] 合法手 " + checkpoint.legalActions.Length
                    + " / 並列 " + oracle.MaxParallelEpisodes
                    + " / worker=" + UltraProductionWorkerBuild.ExecutablePath);

                // ---- 1本だけ: 起動コスト + 1ラン ----
                var single = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 1 };
                var clock = Stopwatch.StartNew();
                UltraEvaluationResult first = single.Evaluate(checkpoint, payloads, 1UL);
                clock.Stop();
                double oneBatch = clock.Elapsed.TotalSeconds;
                int oneCount = Math.Max(1, first.totalRollouts);

                Debug.Log(string.Format(
                    "[UltraCost] ① rollout {0} 本 (各手1本): {1:F1} 秒 / 1本あたり {2:F2} 秒"
                    + " / 使えた {3} 失敗 {4}",
                    oneCount, oneBatch, oneBatch / oneCount,
                    oneCount - first.failedRollouts, first.failedRollouts));

                // ---- 4本ずつ: 並列でどれだけ重なるか ----
                var four = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 4 };
                clock.Restart();
                UltraEvaluationResult second = four.Evaluate(checkpoint, payloads, 2UL);
                clock.Stop();
                double fourBatch = clock.Elapsed.TotalSeconds;
                int fourCount = Math.Max(1, second.totalRollouts);

                Debug.Log(string.Format(
                    "[UltraCost] ② rollout {0} 本 (各手4本): {1:F1} 秒 / 1本あたり {2:F2} 秒"
                    + " / 使えた {3} 失敗 {4}",
                    fourCount, fourBatch, fourBatch / fourCount,
                    fourCount - second.failedRollouts, second.failedRollouts));

                // 直列なら本数に比例するはず。 比例より緩ければ並列が効いている。
                double scale = (fourBatch / fourCount) / Math.Max(0.001, oneBatch / oneCount);
                Debug.Log(string.Format(
                    "[UltraCost] 1本あたりコスト比 ②/① = {0:F2}"
                    + "  (1.00 に近い=並列が効いていない / 小さいほど起動コストが償却できている)",
                    scale));

                Debug.Log("[UltraCost] 判定: " + second.diagnostic);
                if (second.failedRollouts > 0)
                    Debug.LogWarning("[UltraCost] **失敗した rollout がある。**"
                        + " 速度の前に resume 経路の正しさを疑うこと"
                        + " (KeepArtifacts=true にして worker.log を読む)");

                // ---- ラン全体への外挿 ----
                //
                // **決定数は実測値を使う。** ここには長らく「30回/ラン」と直書きされていて、
                // それが全てのコスト見積もりの土台になっていた。 2026-08-18 の国勢調査
                // (Tools/AutoRun/Ultra AI/診断: マクロ決定の国勢調査) の実測は **74.8回/ラン**、
                // 手が1つで rollout 不要なものを除いて **56.7回/ラン**。 2 倍近く外していた。
                //
                // 外挿は本数で行う。 決定あたりの秒数は合法手の数に比例するので、
                // 「1決定 N 秒」を決定数に掛けると、 手が 3 つの計測地点の値を
                // 平均 2.1 手の盤面へ広げてしまう。
                const double DecisionsPerRun = 56.7;        // 手≥2 のみ (実測)
                const double MeanLegalActions = 2.10;       // MapNavigation 実測
                double perRollout = fourBatch / fourCount;
                double rolloutsPerRun = DecisionsPerRun * MeanLegalActions * four.RolloutsPerAction;
                double secondsPerRun = rolloutsPerRun * perRollout;
                Debug.Log(string.Format(
                    "[UltraCost] 外挿: 1本 {0:F2} 秒 × {1:F0} 本/ラン"
                    + " (決定 {2:F1} × 平均 {3:F2} 手 × {4} 本)"
                    + " ≒ {5:F1} 分/ラン → 1000ラン = {6:F0} 時間",
                    perRollout, rolloutsPerRun, DecisionsPerRun, MeanLegalActions,
                    four.RolloutsPerAction, secondsPerRun / 60.0, secondsPerRun * 1000 / 3600.0));
                Debug.Log("[UltraCost] **どの決定点で rollout を撃つかを絞らないと 1000ラン は届かない。**"
                    + " 国勢調査の内訳: MapNavigation 46.5/ラン (62%) / RewardChoice 14.8 /"
                    + " EventChoice 8.0 / RestChoice 5.6。 6-7層はほぼ手が1つ (省略済)");
            }
            finally
            {
                oracle.Dispose();
            }
        }

        /// <summary>Synthesise a mid-run checkpoint without entering play mode.</summary>
        private static bool TryBuildCheckpoint(
            out UltraCheckpoint checkpoint,
            out Func<int, UltraResumePayload> payloads,
            out string failure)
        {
            checkpoint = null;
            payloads = null;
            failure = null;

            // **Captured from a real run, not synthesised.** A hand-built RunState has no
            // weapon, no dice and default hope, so the worker resumes and immediately stalls in
            // combat — the first attempt at this measured deadlocks rather than rollouts.
            string path = AutoRunMenu.UltraCheckpointFile;
            if (!File.Exists(path))
            {
                failure = "checkpoint が無い。 先に "
                    + "Tools/AutoRun/Ultra AI/診断: 実ランから checkpoint を採取 (1ラン) "
                    + "を実行すること (" + path + ")";
                return false;
            }

            UltraResumePayload captured;
            try
            {
                captured = JsonUtility.FromJson<UltraResumePayload>(
                    File.ReadAllText(path, System.Text.Encoding.UTF8));
            }
            catch (Exception ex)
            {
                failure = "checkpoint 読み込み失敗: " + ex.Message;
                return false;
            }
            if (captured?.map?.nodes == null || captured.map.nodes.Length == 0)
            {
                failure = "checkpoint にマップが無い";
                return false;
            }

            // Rebuild the observation from the captured board so the legal moves are the ones
            // the run actually had.
            var go = new GameObject("[Ultra cost probe]");
            try
            {
                var manager = go.AddComponent<MapManager>();
                manager.RestoreState(captured.map.BuildMap(), captured.map.currentNodeId,
                                     captured.map.hungerCurrent, captured.map.hungerMax);

                var run = new GameLoop.RunState();
                captured.run.RestoreInto(run);

                UltraObservation observation = UltraObservationBuilder.Capture(
                    run, manager, "MapNavigation", captured.epoch);
                List<UltraLegalAction> actions =
                    UltraCheckpointProtocol.MapNavigationActions(observation);
                if (actions.Count == 0) { failure = "採取地点に合法手が無い"; return false; }

                checkpoint = UltraCheckpointProtocol.Create(
                    captured.epoch, UltraDecisionPoint.MapNavigation, observation, actions);

                Debug.Log(string.Format(
                    "[UltraCost] checkpoint: {0}層 HP {1}/{2} 所持金 {3} / 合法手 {4}",
                    run.currentFloor, run.playerHP, run.playerMaxHP, run.coins, actions.Count));

                // Re-veil per rollout index: each sample must see a differently-believed board.
                var cache = new Dictionary<int, UltraResumePayload>();
                for (int i = 0; i < 8; i++)
                    cache[i] = UltraResumePayload.Create(
                        run, manager, captured.epoch, UltraDecisionPoint.MapNavigation, i, out _);
                payloads = seed => cache.TryGetValue(seed, out UltraResumePayload p) ? p : null;
                return true;
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
#endif

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest.Ultra
{
    /// <summary>One rollout job: resume from a checkpoint, take one action, play to the end.</summary>
    [Serializable]
    public sealed class UltraEpisodeJob
    {
        public UltraWorkerRequest request;
        public UltraPortfolioProfileSpec profile;
        public UltraPortfolioPolicySpec continuationPolicy;
        public PolicyParameters baselinePolicy;
        public string responsePath;
        public string outputRoot;
    }

    /// <summary>Standalone Player entry point for a single production rollout.
    ///
    /// <para>This is the last link in the Ultra chain: everything upstream produces a
    /// checkpoint and a candidate action, and this runs the <b>real game</b> forward from it.
    /// No transition model, no surrogate — the production <c>GameManager</c>,
    /// <c>CombatManager</c> and <c>AutoRunner</c> do exactly what they do in a normal run.</para>
    ///
    /// <para><b>The reward is 0 or 1.</b> Full clear or not. Weighting intermediate progress
    /// would let a rollout claim credit for reaching layer 6, and the objective would quietly
    /// stop being "clear the run".</para></summary>
    public sealed class UltraEpisodeWorkerBootstrap : MonoBehaviour
    {
        public const string JobArgument = "--ultra-episode-job";

        private UltraEpisodeJob _job;
        private AutoRunner _runner;
        private bool _terminalWritten;
        private UltraProductionRunRecord _record;

        // 時間の行き先を測るための時刻。 **どこを削るかを決める前に、 どこに時間が居るかを
        // 測る。** 起動費は 1 プロセス 1 ラン の rollout でだけ相対的に効くので、 通常バッチ
        // の速度からは推し量れない。
        private long _runBegan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfEpisodeWorker()
        {
            string path = FindArgument(Environment.GetCommandLineArgs(), JobArgument);
            if (string.IsNullOrEmpty(path)) return;

            // ここに来るまでの時間 = エンジン起動 + アセンブリ読込 + シーン読込。
            // 我々のコードが 1 行も走っていない区間なので、 Stopwatch では捉えられず
            // realtimeSinceStartup から読むしかない。
            UltraPhaseClock.Reset();
            UltraPhaseClock.Enabled = true;
            UltraPhaseClock.Add("エンジン起動", Time.realtimeSinceStartup * 1000.0);

            var go = new GameObject("[Ultra Episode Worker]");
            DontDestroyOnLoad(go);
            go.AddComponent<UltraEpisodeWorkerBootstrap>().LoadAndStart(path);
        }

        private void LoadAndStart(string jobPath)
        {
            long setupBegan = UltraPhaseClock.Begin();
            try
            {
                _job = JsonUtility.FromJson<UltraEpisodeJob>(
                    File.ReadAllText(Path.GetFullPath(jobPath), Encoding.UTF8));
                if (_job == null) throw new InvalidDataException("job is null");
                if (_job.request == null || _job.request.episode == null)
                    throw new InvalidDataException("job carries no episode request");
                if (_job.profile == null) throw new InvalidDataException("job carries no profile");
                if (_job.continuationPolicy == null)
                    throw new InvalidDataException("job carries no continuation policy");
                if (string.IsNullOrWhiteSpace(_job.responsePath))
                    throw new InvalidDataException("job carries no response path");

                // The checksum is the only check that separates "complete" from "plausible" —
                // a half-written job file is otherwise a smaller, valid-looking job.
                if (!string.IsNullOrEmpty(_job.request.payloadChecksum)
                    && !UltraWorkerProtocol.TryVerifyRequestChecksum(_job.request, out UltraWorkerFailure bad))
                    throw new InvalidDataException("request checksum: " + bad.code);

                UltraWorkerEpisodePayload episode = _job.request.episode;
                if (!string.Equals(episode.checkpointSchema, UltraResumePayload.Schema,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "unexpected checkpoint schema '" + episode.checkpointSchema + "'");
                if (!UltraResumePayload.TryFromBase64(
                        episode.publicCheckpointBase64, out UltraResumePayload resume, out string why))
                    throw new InvalidDataException("checkpoint: " + why);
                if (!ulong.TryParse(episode.scenarioStartSeedHex, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out ulong scenarioSeed))
                    throw new InvalidDataException("scenario seed is not 16-digit hex");

                Directory.CreateDirectory(Path.GetDirectoryName(
                    Path.GetFullPath(_job.responsePath)) ?? ".");
                if (!string.IsNullOrWhiteSpace(_job.outputRoot))
                    Directory.CreateDirectory(Path.GetFullPath(_job.outputRoot));

                var runnerObject = new GameObject("[Ultra Episode AutoRunner]");
                DontDestroyOnLoad(runnerObject);
                _runner = runnerObject.AddComponent<AutoRunner>();

                UltraWorkerRunnerSetup.ApplyProfile(_runner, _job.profile);
                UltraWorkerRunnerSetup.ApplyPolicy(_runner, _job.continuationPolicy);

                // Exactly one run: this job measures one continuation from one checkpoint.
                _runner.runCount = 0;
                _runner.challengeFixedSweep = true;
                _runner.challengeSweepScores = new[] { _job.profile.challengeScore };
                _runner.challengeSweepRuns = 1;
                _runner.challengeSweepDiagnostics = false;
                _runner.stepsPerYield = 1000;
                _runner.runsPerYield = 1;
                if (_job.baselinePolicy != null)
                    _runner.forcedPolicyParameters = _job.baselinePolicy.Clone();

                _runner.ultraProductionWorkerMode = true;
                _runner.ultraProductionExpectedRuns = 1;
                _runner.ultraProductionPolicyId =
                    string.IsNullOrEmpty(_job.continuationPolicy.policyId)
                        ? "episode" : _job.continuationPolicy.policyId;
                // The production worker mode requires a seed-vector hash and refuses to start
                // without one. An episode has a vector of exactly one seed, so hash that —
                // leaving it empty is a silent "START REJECTED" that surfaces only as a missing
                // run record much later.
                _runner.ultraProductionScenarioSeedVectorHash =
                    UltraPortfolioProtocol.ComputeSeedVectorHash(
                        episode.scenarioStartSeedHex.ToUpperInvariant(), new[] { 0 });
                _runner.ultraProductionScenarioSeeds = new[] { scenarioSeed };
                if (string.IsNullOrWhiteSpace(_job.outputRoot))
                    throw new InvalidDataException("job carries no output root");
                _runner.productionOutputRootOverride = Path.GetFullPath(_job.outputRoot);

                // The two fields that make this a rollout rather than a fresh run.
                _runner.ultraResumeFrom = resume;
                _runner.ultraResumeForcedActionId = episode.actionId ?? "";

                // Logged from the bootstrap, not from inside the run: AutoRunner routes
                // per-run Debug.Log into its own buffer, so anything logged during RunOne does
                // not reach worker.log — which is the only output a worker process has.
                Debug.Log("[UltraEpisodeWorker] 準備完了: resume="
                    + (_runner.ultraResumeFrom != null)
                    + " 強制手='" + _runner.ultraResumeForcedActionId + "'"
                    + " 現在地=" + resume.map.currentNodeId
                    + " ノード=" + resume.map.nodes.Length
                    + " seed=" + episode.scenarioStartSeedHex
                    + " 技量=" + _runner.wiringSkill
                    + " 相互攻撃=" + _runner.useMutualAttackPipeline);

                _runner.UltraProductionRunCompleted += OnRunCompleted;
                _runner.UltraProductionBatchCompleted += OnBatchCompleted;

                UltraPhaseClock.End("worker準備", setupBegan);
                _runBegan = UltraPhaseClock.Begin();
                _runner.Begin();
            }
            catch (Exception ex)
            {
                WriteFailure("EPISODE_BOOTSTRAP_FAILED", ex.GetType().Name + ": " + ex.Message);
                Application.Quit(2);
            }
        }

        private void OnRunCompleted(UltraProductionRunRecord record)
        {
            _record = record;
            UltraPhaseClock.End("実ラン", _runBegan);
            // 以降 (WriteLogs / 2 回目の Reload / プロセス終了) は rollout の答えに一切
            // 寄与しない。 分けて計上する。
            _runBegan = UltraPhaseClock.Begin();
            Debug.Log("[UltraEpisodeWorker] run 完了: valid=" + (record != null && record.valid)
                + " fullClear=" + (record != null && record.fullClear)
                + " crash=" + (record != null && record.crash)
                + " deadlock=" + (record != null && record.deadlock));
        }

        private void OnBatchCompleted(UltraProductionBatchRecord batch)
        {
            if (_terminalWritten) return;
            _terminalWritten = true;

            UltraPhaseClock.End("終了処理", _runBegan);
            // **Error で出す。** バッチ実行中は filterLogType = LogType.Error なので、
            // Log / Warning はこの時点では worker.log に届かない。
            Debug.LogError(UltraPhaseClock.Report(Time.realtimeSinceStartup * 1000.0)
                + "\n  回帰キャッシュ命中: プロセス内 ×" + ItemRegression.CacheHits
                + " / ディスク ×" + ItemRegression.DiskHits
                + " / " + ItemRegression.LastSummary);

            // A rollout that crashed or deadlocked is **not** a loss. Reporting it as
            // primaryReward=0 would let worker faults masquerade as evidence that the action
            // is bad, and the evaluator would learn to avoid whatever tends to break the
            // worker. It is reported unusable so the evaluator discards it instead.
            bool crashed = _record == null || _record.crash || _record.deadlock || !_record.valid;
            var result = new UltraEpisodeResult
            {
                success = !crashed,
                reachedTerminal = !crashed,
                primaryReward = (!crashed && _record.fullClear) ? 1f : 0f,
                finalPublicStateHash = _record != null ? (_record.deterministicDigest ?? "") : "",
                productionSteps = 1,
                failureCode = crashed ? "EPISODE_NOT_USABLE" : "",
                failureDetail = crashed
                    ? (_record == null ? "no run record"
                       : "crash=" + _record.crash + " deadlock=" + _record.deadlock
                         + " valid=" + _record.valid)
                    : "",
            };
            Write(result);
            Application.Quit(crashed ? 3 : 0);
        }

        private void WriteFailure(string code, string detail)
        {
            if (_terminalWritten) return;
            _terminalWritten = true;
            Write(new UltraEpisodeResult
            {
                success = false,
                reachedTerminal = false,
                primaryReward = 0f,
                failureCode = code,
                failureDetail = detail,
            });
        }

        private void Write(UltraEpisodeResult result)
        {
            try
            {
                if (_job == null || string.IsNullOrWhiteSpace(_job.responsePath)) return;
                string full = Path.GetFullPath(_job.responsePath);
                string directory = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string temp = full + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(result, false), new UTF8Encoding(false));
                if (File.Exists(full)) File.Delete(full);
                File.Move(temp, full);
            }
            catch (Exception ex)
            {
                Debug.LogError("[UltraEpisodeWorker] response write failed: " + ex.Message);
            }
        }

        private static string FindArgument(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal) && i + 1 < args.Length)
                    return args[i + 1];
                string prefix = name + "=";
                if (args[i] != null && args[i].StartsWith(prefix, StringComparison.Ordinal))
                    return args[i].Substring(prefix.Length);
            }
            return null;
        }
    }
}

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest.Ultra
{
    [Serializable]
    public sealed class UltraPortfolioCandidateJob
    {
        public UltraPortfolioWorkerRequest request;
        public int candidateIndex;
        public PolicyParameters baselinePolicy;
        public string responsePath;
        public string progressPath;
        public string outputRoot;
    }

    /// <summary>
    /// Standalone Player entry point.  It does not calculate game mechanics: it
    /// configures the normal AutoRunner and lets the production game execute every
    /// run.  The parent process supplies only synthetic seeds and a frozen policy.
    /// </summary>
    public sealed class UltraProductionWorkerBootstrap : MonoBehaviour
    {
        public const string JobArgument = "--ultra-portfolio-job";

        private UltraPortfolioCandidateJob _job;
        private AutoRunner _runner;
        private int _completed;
        private bool _terminalWritten;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfWorker()
        {
            string path = FindArgument(Environment.GetCommandLineArgs(), JobArgument);
            if (string.IsNullOrEmpty(path)) return;
            var go = new GameObject("[Ultra Production Worker]");
            DontDestroyOnLoad(go);
            var bootstrap = go.AddComponent<UltraProductionWorkerBootstrap>();
            bootstrap.LoadAndStart(path);
        }

        private void LoadAndStart(string jobPath)
        {
            try
            {
                string json = File.ReadAllText(Path.GetFullPath(jobPath), Encoding.UTF8);
                _job = JsonUtility.FromJson<UltraPortfolioCandidateJob>(json);
                string requestFailure = "job is null";
                if (_job == null || !UltraPortfolioProtocol.TryValidateRequest(
                        _job.request, out requestFailure))
                    throw new InvalidDataException("request: " + requestFailure);
                if (_job.candidateIndex < 0
                    || _job.candidateIndex >= _job.request.candidates.Length)
                    throw new InvalidDataException("candidate index is outside the request");
                if (_job.baselinePolicy == null)
                    throw new InvalidDataException("baseline PolicyParameters missing");
                if (string.IsNullOrWhiteSpace(_job.responsePath)
                    || string.IsNullOrWhiteSpace(_job.progressPath)
                    || string.IsNullOrWhiteSpace(_job.outputRoot))
                    throw new InvalidDataException("worker paths missing");

                Directory.CreateDirectory(Path.GetDirectoryName(
                    Path.GetFullPath(_job.responsePath)) ?? ".");
                Directory.CreateDirectory(Path.GetFullPath(_job.outputRoot));

                UltraPortfolioPolicySpec policy =
                    _job.request.candidates[_job.candidateIndex];
                ulong[] scenarioSeeds = UltraPortfolioProtocol.DeriveScenarioSeeds(
                    _job.request.syntheticMasterSeedHex, _job.request.runOrdinals);

                var runnerObject = new GameObject("[Ultra Production AutoRunner]");
                DontDestroyOnLoad(runnerObject);
                _runner = runnerObject.AddComponent<AutoRunner>();
                _runner.runCount = 0;
                _runner.challengeFixedSweep = true;
                _runner.challengeSweepScores = new[] { _job.request.profile.challengeScore };
                _runner.challengeSweepRuns = scenarioSeeds.Length;
                _runner.challengeSweepDiagnostics = false;
                // **Shared with the episode worker.** Two copies of this configuration is
                // precisely how the 2026-08-17 mismatch happened one level up.
                UltraWorkerRunnerSetup.ApplyProfile(_runner, _job.request.profile);
                UltraWorkerRunnerSetup.ApplyPolicy(_runner, policy);

                _runner.forcedPolicyParameters = _job.baselinePolicy.Clone();
                _runner.stepsPerYield = 1000;
                _runner.runsPerYield = 20;
                _runner.ultraProductionWorkerMode = true;
                _runner.ultraProductionExpectedRuns = scenarioSeeds.Length;
                _runner.ultraProductionPolicyId = policy.policyId;
                _runner.ultraProductionScenarioSeedVectorHash = _job.request.seedVectorHash;
                _runner.ultraProductionScenarioSeeds = scenarioSeeds;
                _runner.productionOutputRootOverride = Path.GetFullPath(_job.outputRoot);
                _runner.UltraProductionRunCompleted += OnRunCompleted;
                _runner.UltraProductionBatchCompleted += OnBatchCompleted;
                WriteProgress(0, "started");
                _runner.Begin();
            }
            catch (Exception ex)
            {
                WriteFailure("BOOTSTRAP_FAILED", ex.GetType().Name + ": " + ex.Message);
                Application.Quit(2);
            }
        }

        private void OnRunCompleted(UltraProductionRunRecord record)
        {
            _completed++;
            if (_completed == 1 || (_completed % 10) == 0
                || (_job != null && _completed == _job.request.runOrdinals.Length))
                WriteProgress(_completed, record != null && record.fullClear ? "clear7" : "running");
        }

        private void OnBatchCompleted(UltraProductionBatchRecord batch)
        {
            if (_terminalWritten) return;
            _terminalWritten = true;
            try
            {
                WriteAtomic(_job.responsePath, JsonUtility.ToJson(batch, false));
                WriteProgress(_completed, batch != null && batch.completedNormally
                    ? "completed" : "failed");
            }
            catch (Exception ex)
            {
                Debug.LogError("[UltraWorker] response write failed: " + ex.Message);
            }
        }

        private void WriteFailure(string code, string detail)
        {
            if (_terminalWritten) return;
            _terminalWritten = true;
            try
            {
                var batch = new UltraProductionBatchRecord
                {
                    policyId = _job != null && _job.request != null
                        && _job.candidateIndex >= 0
                        && _job.candidateIndex < _job.request.candidates.Length
                        ? _job.request.candidates[_job.candidateIndex].policyId : "",
                    scenarioSeedVectorHash = _job != null && _job.request != null
                        ? _job.request.seedVectorHash : "",
                    runs = new UltraProductionRunRecord[0],
                    completedNormally = false,
                    failureCode = code + ": " + detail,
                };
                if (_job != null && !string.IsNullOrWhiteSpace(_job.responsePath))
                    WriteAtomic(_job.responsePath, JsonUtility.ToJson(batch, false));
            }
            catch { }
        }

        private void WriteProgress(int completed, string state)
        {
            try
            {
                if (_job == null || string.IsNullOrWhiteSpace(_job.progressPath)) return;
                WriteAtomic(_job.progressPath,
                    "completed=" + completed + "\nstate=" + (state ?? "") + "\n");
            }
            catch { }
        }

        private static string FindArgument(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal)
                    && i + 1 < args.Length) return args[i + 1];
                string prefix = name + "=";
                if (args[i] != null && args[i].StartsWith(prefix, StringComparison.Ordinal))
                    return args[i].Substring(prefix.Length);
            }
            return null;
        }

        private static void WriteAtomic(string path, string text)
        {
            string full = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = full + ".tmp";
            File.WriteAllText(temp, text ?? string.Empty, new UTF8Encoding(false));
            if (File.Exists(full)) File.Delete(full);
            File.Move(temp, full);
        }
    }
}

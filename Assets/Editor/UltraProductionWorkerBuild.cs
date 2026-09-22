using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AutoTest;
using AutoTest.Ultra;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Process = System.Diagnostics.Process;

namespace AutoTest.EditorTools
{
    public static class UltraProductionWorkerBuild
    {
        public const string ScenePath = "Assets/Scenes/SampleScene2.unity";

        // Unity rejects Player builds under the project's internal Library folder.
        // Keep the disposable worker outside Assets/Library so it can be rebuilt
        // without triggering an import or violating BuildPipeline's path guard.
        public static string BuildRoot => Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "UltraProductionWorkerBuild"));
        public static string ExecutablePath => Path.Combine(BuildRoot,
            "UltraProductionWorker.exe");
        public static string DescriptorPath => Path.Combine(BuildRoot,
            "ultra-production-worker.json");

        /// <summary>コマンドライン (-batchmode -executeMethod) 用の入口。
        /// Editor を開かずに worker を作り直す ── スイープは worker でしか回らないので、
        /// Editor が落ちているとコード変更を測る手段が無くなる。 終了コード 0 = 成功。</summary>
        public static void BuildFromCommandLine()
        {
            bool ok = EnsureBuilt(out string reason);
            Debug.Log("[UltraProductionWorkerBuild] CLI build: " + (ok ? "OK" : "FAILED: " + reason));
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool EnsureBuilt(out string reason)
        {
            reason = null;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                reason = "Unity is compiling or importing; refresh must finish first";
                return false;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                reason = "worker build must finish before Play Mode";
                return false;
            }
            if (!File.Exists(Path.GetFullPath(ScenePath)))
            {
                reason = "worker scene missing: " + ScenePath;
                return false;
            }

            string oldCompany = PlayerSettings.companyName;
            string oldProduct = PlayerSettings.productName;
            try
            {
                Directory.CreateDirectory(BuildRoot);
                PlayerSettings.companyName = "UltraAI.IsolatedWorker";
                PlayerSettings.productName = "UltraAI Production Worker";
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = ExecutablePath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.StrictMode,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report == null || report.summary.result != BuildResult.Succeeded)
                {
                    reason = report == null ? "BuildPipeline returned no report"
                        : "worker build failed: " + report.summary.result;
                    return false;
                }

                RemoveCrashHandler();
                CopyFrozenLearningSnapshot();
                UltraProductionWorkerDescriptor descriptor = CreateDescriptor();
                File.WriteAllText(DescriptorPath, JsonUtility.ToJson(descriptor, true),
                    new UTF8Encoding(false));
                Debug.Log("[Ultra] production worker ready: " + ExecutablePath
                    + " / build=" + descriptor.fingerprints.buildFingerprint);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                PlayerSettings.companyName = oldCompany;
                PlayerSettings.productName = oldProduct;
            }
        }

        /// <summary>Delete the crash handler that ships next to the player.
        ///
        /// <para>Every worker process spawns <c>UnityCrashHandler64.exe</c> alongside itself, so
        /// a rollout costs <b>two</b> process creations rather than one, and a decision that
        /// launches 16 workers makes 32 processes appear and vanish in Task Manager — which
        /// looks like a leak and is real overhead either way.</para>
        ///
        /// <para>Nothing is lost by removing it. These are throwaway processes whose only output
        /// is a JSON file, and a crash is already a first-class outcome: the oracle reports it
        /// as "no answer" rather than as a loss, so a post-mortem dump would go unread.</para>
        ///
        /// <para>Deleted rather than skipped at build time because the build pipeline copies it
        /// unconditionally for a Windows standalone player.</para></summary>
        private static void RemoveCrashHandler()
        {
            try
            {
                string path = Path.Combine(BuildRoot, "UnityCrashHandler64.exe");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                // 消せなくても worker は動く。 プロセスが 1 つ余計に立つだけ。
                Debug.LogWarning("[Ultra] クラッシュハンドラを消せなかった: " + ex.Message);
            }
        }

        public static bool TryLoadDescriptor(out UltraProductionWorkerDescriptor descriptor,
            out string reason)
        {
            descriptor = null;
            reason = null;
            try
            {
                if (!File.Exists(DescriptorPath) || !File.Exists(ExecutablePath))
                {
                    reason = "production worker has not been built";
                    return false;
                }
                descriptor = JsonUtility.FromJson<UltraProductionWorkerDescriptor>(
                    File.ReadAllText(DescriptorPath, Encoding.UTF8));
                if (descriptor == null
                    || descriptor.version != UltraProductionWorkerDescriptor.CurrentVersion
                    || descriptor.fingerprints == null)
                {
                    reason = "worker descriptor is invalid";
                    return false;
                }
                if (!string.Equals(descriptor.executableSha256,
                    Sha256File(ExecutablePath), StringComparison.Ordinal))
                {
                    reason = "worker executable hash mismatch";
                    return false;
                }
                if (!UltraBuildManifestValidation.TryValidateFingerprints(
                    descriptor.fingerprints, out string code, out string detail))
                {
                    reason = (code ?? "fingerprint") + ": " + detail;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                descriptor = null;
                return false;
            }
        }

        private static UltraProductionWorkerDescriptor CreateDescriptor()
        {
            string exeHash = Sha256File(ExecutablePath);
            string managed = Path.Combine(BuildRoot,
                "UltraProductionWorker_Data", "Managed", "Assembly-CSharp.dll");
            string assemblyHash = File.Exists(managed) ? Sha256File(managed) : exeHash;
            string actionHash = UltraPortfolioProtocol.ComputeCandidateSetHash(
                UltraPortfolioProtocol.CanonicalCandidates());
            return new UltraProductionWorkerDescriptor
            {
                executableFile = Path.GetFileName(ExecutablePath),
                executableSha256 = exeHash,
                assemblySha256 = assemblyHash,
                builtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                fingerprints = new UltraFingerprintSet
                {
                    buildFingerprint = Sha256Text("ultra-production-build-v1\n"
                        + exeHash + "\n" + assemblyHash),
                    dataFingerprint = assemblyHash,
                    checkpointFingerprint = Sha256Text(
                        "run-start-only-no-midrun-checkpoint-v1"),
                    actionFingerprint = actionHash,
                    objectiveFingerprint = Sha256Text(
                        "objective:p7-full-clear;tie:baseline"),
                },
            };
        }

        private static void CopyFrozenLearningSnapshot()
        {
            string source = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "AutoRunLogs", "learning"));
            string destination = Path.Combine(BuildRoot, "AutoRunLogs", "learning");
            if (!Directory.Exists(source)) return;
            if (Directory.Exists(destination)) FileUtil.DeleteFileOrDirectory(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            FileUtil.CopyFileOrDirectory(source, destination);
        }

        internal static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path)) return Hex(sha.ComputeHash(stream));
        }

        internal static string Sha256Text(string value)
        {
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")));
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }

    [Serializable]
    internal sealed class UltraPortfolioEvidenceFile
    {
        public UltraPortfolioWorkerRequest request;
        public UltraProductionBatchRecord[] candidates;
    }

    internal sealed class UltraProcessAutoRunController : IUltraAutoRunController
    {
        private sealed class WorkerSlot
        {
            public int index;
            public string directory;
            public string responsePath;
            public string progressPath;
            public Process process;
            public UltraProductionBatchRecord batch;
        }

        private readonly UltraAutoRunProfile _profile;
        private readonly UltraProductionWorkerDescriptor _descriptor;
        private UltraProgressSession _progress;
        private UltraPolicyPortfolioRequest _bridgeRequest;
        private UltraPortfolioWorkerRequest _request;
        private readonly List<WorkerSlot> _workers = new List<WorkerSlot>();
        private string _sessionRoot;
        private bool _started;
        private bool _finished;
        private bool _cancelled;

        public UltraProcessAutoRunController(UltraAutoRunProfile profile,
            UltraProductionWorkerDescriptor descriptor)
        {
            _profile = profile;
            _descriptor = descriptor;
        }

        public UltraPlanningCoverage Coverage => UltraPlanningCoverage.RunStartOnly;
        public string ProductionBuildFingerprint => _descriptor != null
            && _descriptor.fingerprints != null
            ? _descriptor.fingerprints.buildFingerprint : "";

        public bool TryValidateProduction(out string reason)
        {
            reason = "profile is null";
            if (_profile == null || !_profile.IsCanonical(out reason)) return false;
            if (!UltraProductionWorkerBuild.TryLoadDescriptor(out var current, out reason))
                return false;
            if (_descriptor == null || !string.Equals(_descriptor.executableSha256,
                current.executableSha256, StringComparison.Ordinal))
            {
                reason = "controller descriptor is stale";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        public void BindProgress(UltraProgressSession session) { _progress = session; }

        public bool TryBeginPolicyPortfolio(UltraPolicyPortfolioRequest request,
            out string reason)
        {
            reason = null;
            if (_started) { reason = "portfolio already started"; return false; }
            if (request == null || request.profile == null
                || !request.profile.IsCanonical(out reason)) return false;
            if (request.baselinePolicy == null)
            { reason = "baseline policy missing"; return false; }
            if (request.productionRunsPerCandidate != UltraPortfolioProtocol.CanonicalEvaluationRuns)
            { reason = "production portfolio requires 1000 runs per candidate"; return false; }
            if (!TryValidateProduction(out reason)) return false;

            try
            {
                _bridgeRequest = request;
                string requestId = DateTime.UtcNow.ToString("yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
                string nonce = UltraProductionWorkerBuild.Sha256Text(
                    requestId + "|" + Guid.NewGuid().ToString("N"));
                ulong master = ulong.Parse(
                    _descriptor.fingerprints.buildFingerprint.Substring(0, 16),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    ^ 0x554C5452415F5631UL;
                _request = UltraPortfolioProtocol.CreateCanonicalRequest(
                    requestId, nonce, _descriptor.fingerprints, master,
                    request.productionRunsPerCandidate);

                _sessionRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                    "AutoRunLogs", "ultra_planning", requestId));
                Directory.CreateDirectory(_sessionRoot);
                string requestPath = Path.Combine(_sessionRoot, "portfolio_request.json");
                if (!UltraPortfolioProtocol.TryRequestToJson(_request,
                    out string requestJson, out reason)) return false;
                File.WriteAllText(requestPath, requestJson, new UTF8Encoding(false));

                for (int i = 0; i < _request.candidates.Length; i++)
                {
                    string directory = Path.Combine(_sessionRoot,
                        i.ToString("D2") + "_" + _request.candidates[i].policyId);
                    Directory.CreateDirectory(directory);
                    var slot = new WorkerSlot
                    {
                        index = i,
                        directory = directory,
                        responsePath = Path.Combine(directory, "response.json"),
                        progressPath = Path.Combine(directory, "progress.txt"),
                    };
                    var job = new UltraPortfolioCandidateJob
                    {
                        request = _request,
                        candidateIndex = i,
                        baselinePolicy = request.baselinePolicy.Clone(),
                        responsePath = slot.responsePath,
                        progressPath = slot.progressPath,
                        outputRoot = Path.Combine(directory, "AutoRunLogs"),
                    };
                    string jobPath = Path.Combine(directory, "job.json");
                    File.WriteAllText(jobPath, JsonUtility.ToJson(job, false),
                        new UTF8Encoding(false));
                    string logPath = Path.Combine(directory, "worker.log");
                    var start = new ProcessStartInfo
                    {
                        FileName = UltraProductionWorkerBuild.ExecutablePath,
                        Arguments = "-batchmode -nographics -logFile " + Quote(logPath)
                            + " " + UltraProductionWorkerBootstrap.JobArgument + " "
                            + Quote(jobPath),
                        WorkingDirectory = UltraProductionWorkerBuild.BuildRoot,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    };
                    slot.process = Process.Start(start);
                    if (slot.process == null) throw new InvalidOperationException(
                        "failed to start worker " + i);
                    _workers.Add(slot);
                }

                _started = true;
                UltraProgressHub.SetPhase(_progress, UltraProgressPhase.Search,
                    (long)_request.candidates.Length * _request.runOrdinals.Length,
                    "production full-run portfolio");
                UltraProgressHub.ReportActiveWorkers(_progress, _workers.Count,
                    _workers.Count, "workers started");
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                Cancel(reason);
                return false;
            }
        }

        public bool TryPollPolicyPortfolio(out bool completed,
            out UltraPolicyPortfolioResult result, out string reason)
        {
            completed = false;
            result = null;
            reason = null;
            if (!_started || _cancelled)
            { reason = _cancelled ? "portfolio cancelled" : "portfolio not started"; return false; }
            if (_finished) { reason = "portfolio result was already consumed"; return false; }

            try
            {
                long progress = 0;
                int active = 0;
                for (int i = 0; i < _workers.Count; i++)
                {
                    WorkerSlot slot = _workers[i];
                    progress += ReadCompleted(slot.progressPath,
                        _request.runOrdinals.Length);
                    if (slot.batch != null) continue;
                    if (!slot.process.HasExited) { active++; continue; }
                    if (!File.Exists(slot.responsePath))
                    {
                        reason = "worker " + i + " exited without response (code "
                            + slot.process.ExitCode + ")";
                        Cancel(reason);
                        return false;
                    }
                    slot.batch = JsonUtility.FromJson<UltraProductionBatchRecord>(
                        File.ReadAllText(slot.responsePath, Encoding.UTF8));
                    if (!ValidateCandidateBatch(slot, out reason))
                    {
                        Cancel(reason);
                        return false;
                    }
                }
                UltraProgressHub.ReportPhaseCompleted(_progress, progress,
                    "production episodes " + progress + "/"
                    + ((long)_request.candidates.Length * _request.runOrdinals.Length));
                UltraProgressHub.ReportActiveWorkers(_progress, active, _workers.Count);
                if (active > 0) return true;

                UltraPortfolioWorkerResponse evidence = BuildEvidence(out string evidencePath);
                if (!UltraPortfolioProtocol.TryValidateResponse(evidence, _request,
                    out reason)) return false;
                if (!UltraPortfolioProtocol.TryVerifyArtifact(_sessionRoot, evidence,
                    out _, out reason)) return false;
                UltraPortfolioSelectionResult selection =
                    UltraPortfolioSelector.SelectTrusted(_request, evidence);
                UltraPortfolioPolicySpec selected =
                    UltraPortfolioSelector.GetSelectedPolicyClone(_request, selection);
                if (selected == null)
                { reason = "trusted selector did not return a policy"; return false; }

                var candidateResults = new UltraPolicyCandidateResult[_workers.Count];
                for (int i = 0; i < _workers.Count; i++)
                {
                    UltraProductionBatchRecord batch = _workers[i].batch;
                    int n = batch.runs.Length;
                    var valid = new bool[n];
                    var clear = new bool[n];
                    var fingerprints = new int[n];
                    for (int r = 0; r < n; r++)
                    {
                        valid[r] = batch.runs[r].valid;
                        clear[r] = batch.runs[r].fullClear;
                        fingerprints[r] = batch.runs[r].fingerprint;
                    }
                    candidateResults[i] = new UltraPolicyCandidateResult
                    {
                        id = _request.candidates[i].policyId,
                        policyFingerprint = UltraProductionWorkerBuild.Sha256Text(
                            _request.candidateSetHash + "|" + i),
                        buildFingerprint = ProductionBuildFingerprint,
                        profileFingerprint = _request.profileFingerprint,
                        scenarioSeedHash = _request.seedVectorHash,
                        validRuns = valid,
                        fullClearRuns = clear,
                        runFingerprints = fingerprints,
                        outputPath = batch.outputPath,
                    };
                }

                // **候補が本当に別物として走ったかを数える。** 2026-08-17 の事故では
                //   4 候補が同一挙動 (fingerprint 98.8% 一致) だったのに、 進捗も evidence も
                //   健全に見えていた。 選択の後に必ず通す。
                UltraPortfolioSelector.DetectConfigurationNoOps(
                    candidateResults, out string[] noOpIds, out double maxAgreement);

                result = new UltraPolicyPortfolioResult
                {
                    selectedPolicyId = selected.policyId,
                    selectedPolicy = _bridgeRequest.baselinePolicy.Clone(),
                    selectedRunPolicy = selected,
                    candidateCount = candidateResults.Length,
                    productionRunsPerCandidate = _request.runOrdinals.Length,
                    candidateResults = candidateResults,
                    outputPath = evidencePath,
                    configurationNoOpPolicyIds = noOpIds,
                    maxDigestAgreement = maxAgreement,
                    diagnostic = selection.diagnostic + " / p="
                        + selection.oneSidedP.ToString("G4", CultureInfo.InvariantCulture)
                        + " / " + UltraPortfolioSelector.DescribeNoOpCheck(noOpIds, maxAgreement),
                };
                _finished = true;
                completed = true;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                Cancel(reason);
                return false;
            }
        }

        public bool TryBeginRun(int runIndex, out string reason)
        {
            reason = null;
            if (!_finished || _cancelled)
            { reason = "production portfolio is not healthy"; return false; }
            return true;
        }

        public bool TryBeginDecision(UltraLiveDecisionRequest request,
            out long ticket, out string reason)
        {
            ticket = 0;
            reason = "RunStartOnly controller has no mid-run decision hook";
            return false;
        }

        public bool TryPollDecision(long ticket, out bool completed,
            out UltraLiveDecisionResult result, out string reason)
        {
            completed = false; result = null;
            reason = "RunStartOnly controller has no mid-run decision hook";
            return false;
        }

        public void CompleteRun(int runIndex, string summary) { }

        public void CompleteBatch(string summary)
        {
            if (string.IsNullOrEmpty(_sessionRoot)) return;
            try
            {
                File.WriteAllText(Path.Combine(_sessionRoot, "live_batch_complete.txt"),
                    DateTime.Now.ToString("O") + "\n" + (summary ?? ""),
                    new UTF8Encoding(false));
            }
            catch { }
        }

        public void Cancel(string reason)
        {
            _cancelled = true;
            for (int i = 0; i < _workers.Count; i++)
            {
                try
                {
                    if (_workers[i].process != null && !_workers[i].process.HasExited)
                        _workers[i].process.Kill();
                }
                catch { }
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _workers.Count; i++)
                try { _workers[i].process?.Dispose(); } catch { }
        }

        private bool ValidateCandidateBatch(WorkerSlot slot, out string reason)
        {
            reason = null;
            UltraProductionBatchRecord batch = slot.batch;
            UltraPortfolioPolicySpec policy = _request.candidates[slot.index];
            ulong[] seeds = UltraPortfolioProtocol.DeriveScenarioSeeds(
                _request.syntheticMasterSeedHex, _request.runOrdinals);
            if (batch == null || !batch.completedNormally)
            { reason = "worker " + slot.index + " failed: " + batch?.failureCode; return false; }
            if (!string.Equals(batch.policyId, policy.policyId, StringComparison.Ordinal)
                || !string.Equals(batch.scenarioSeedVectorHash, _request.seedVectorHash,
                    StringComparison.Ordinal)
                || batch.runs == null || batch.runs.Length != seeds.Length)
            { reason = "worker " + slot.index + " evidence header mismatch"; return false; }
            for (int i = 0; i < seeds.Length; i++)
            {
                UltraProductionRunRecord run = batch.runs[i];
                if (run == null || run.runOrdinal != i || run.runIndex != 10000 + i
                    || run.scenarioSeed != seeds[i]
                    || !string.Equals(run.scenarioSeedHex,
                        seeds[i].ToString("x16"), StringComparison.Ordinal)
                    || string.IsNullOrEmpty(run.deterministicDigest))
                { reason = "worker " + slot.index + " run evidence mismatch at " + i; return false; }
            }
            if (string.IsNullOrEmpty(batch.artifactPath)
                || !File.Exists(batch.artifactPath)
                || !string.Equals(batch.artifactSha256,
                    UltraProductionWorkerBuild.Sha256File(batch.artifactPath),
                    StringComparison.Ordinal))
            { reason = "worker " + slot.index + " output artifact mismatch"; return false; }
            return true;
        }

        private UltraPortfolioWorkerResponse BuildEvidence(out string evidencePath)
        {
            var evaluations = new UltraPortfolioEvaluation[_workers.Count];
            var batches = new UltraProductionBatchRecord[_workers.Count];
            for (int i = 0; i < _workers.Count; i++)
            {
                UltraProductionBatchRecord batch = _workers[i].batch;
                batches[i] = batch;
                int n = batch.runs.Length;
                var evaluation = new UltraPortfolioEvaluation
                {
                    policyId = _request.candidates[i].policyId,
                    valid = new bool[n],
                    fullClear = new bool[n],
                    runDigestSha256 = new string[n],
                };
                for (int r = 0; r < n; r++)
                {
                    evaluation.valid[r] = batch.runs[r].valid;
                    evaluation.fullClear[r] = batch.runs[r].fullClear;
                    evaluation.runDigestSha256[r] = UltraPortfolioProtocol.ComputeRunDigest(
                        evaluation.policyId, _request.syntheticMasterSeedHex, r, batch.runs[r]);
                }
                evaluations[i] = evaluation;
            }
            evidencePath = Path.Combine(_sessionRoot, "portfolio_evidence.json");
            File.WriteAllText(evidencePath, JsonUtility.ToJson(new UltraPortfolioEvidenceFile
            {
                request = _request,
                candidates = batches,
            }, false), new UTF8Encoding(false));
            return new UltraPortfolioWorkerResponse
            {
                requestId = _request.requestId,
                decisionNonce = _request.decisionNonce,
                accepted = true,
                workerFingerprints = _descriptor.fingerprints.Clone(),
                profileFingerprint = _request.profileFingerprint,
                seedVectorHash = _request.seedVectorHash,
                candidateSetHash = _request.candidateSetHash,
                evaluations = evaluations,
                artifactRelativePath = Path.GetFileName(evidencePath),
                artifactSha256 = UltraProductionWorkerBuild.Sha256File(evidencePath),
            };
        }

        private static int ReadCompleted(string path, int max)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].StartsWith("completed=", StringComparison.Ordinal)
                        && int.TryParse(lines[i].Substring(10), out int value))
                        return Mathf.Clamp(value, 0, max);
            }
            catch { }
            return 0;
        }

        private static string Quote(string value)
            => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
    }

    [InitializeOnLoad]
    internal static class UltraProductionControllerInstaller
    {
        static UltraProductionControllerInstaller()
        {
            Install();
            AssemblyReloadEvents.afterAssemblyReload += Install;
        }

        private static void Install()
        {
            UltraAutoRunBridge.RemoveProductionFactory();
            UltraAutoRunBridge.InstallProductionFactory(profile =>
            {
                if (!UltraProductionWorkerBuild.TryLoadDescriptor(
                    out UltraProductionWorkerDescriptor descriptor, out string reason))
                    throw new InvalidOperationException(reason);
                return new UltraProcessAutoRunController(profile, descriptor);
            });
        }
    }
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using AutoTest.Ultra;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Process = System.Diagnostics.Process;

namespace AutoTest.EditorTools
{
    /// <summary>Runs rollouts by launching production worker processes.
    ///
    /// <para>The last link in the Ultra chain, and the only implementation of
    /// <see cref="IUltraProductionOracle"/> that is allowed to exist: it produces outcomes by
    /// running the shipped game, not by modelling it.</para>
    ///
    /// <para><b>Batched because a process is expensive.</b> Starting a Unity player costs
    /// roughly as much as a short run, so a serial evaluation of 5 actions × 16 rollouts would
    /// spend most of its time in startup. Several run at once and the cost is paid in
    /// parallel.</para>
    ///
    /// <para><b>Every failure mode ends the same way: a null result.</b> Timeout, crash,
    /// missing response, unparseable response — all become "this rollout produced no answer",
    /// never "this action lost". The distinction matters: reporting faults as losses would
    /// teach the evaluator to avoid whatever tends to break the worker.</para></summary>
    public sealed class UltraProcessEpisodeOracle
        : IUltraProductionOracleBatch, IUltraProductionOracleAsync, IDisposable
    {
        private readonly string _executablePath;
        private readonly string _workingDirectory;
        private readonly string _workRoot;
        private readonly UltraPortfolioProfileSpec _profile;
        private readonly UltraPortfolioPolicySpec _continuationPolicy;
        private readonly PolicyParameters _baselinePolicy;
        private readonly string _buildFingerprint;
        private int _sequence;

        /// <summary>Seconds a single rollout may take before it is abandoned.</summary>
        public int EpisodeTimeoutSeconds = 180;
        /// <summary>Keep the job/response files of finished rollouts. Off by default: an
        /// evaluation produces thousands of small files.</summary>
        public bool KeepArtifacts;

        public int MaxParallelEpisodes { get; set; }

        public UltraProcessEpisodeOracle(
            string executablePath, string workingDirectory, string workRoot,
            UltraPortfolioProfileSpec profile, UltraPortfolioPolicySpec continuationPolicy,
            PolicyParameters baselinePolicy, string buildFingerprint, int maxParallelEpisodes = 0)
        {
            _executablePath = executablePath;
            _workingDirectory = workingDirectory;
            _workRoot = workRoot;
            _profile = profile;
            _continuationPolicy = continuationPolicy;
            _baselinePolicy = baselinePolicy;
            _buildFingerprint = buildFingerprint;
            MaxParallelEpisodes = maxParallelEpisodes > 0
                ? maxParallelEpisodes
                : Math.Max(1, Environment.ProcessorCount - 1);
        }

        public bool IsHealthy
        {
            get
            {
                return !string.IsNullOrEmpty(_executablePath)
                    && File.Exists(_executablePath)
                    && _profile != null
                    && _continuationPolicy != null;
            }
        }

        public string BuildFingerprint { get { return _buildFingerprint ?? ""; } }

        public bool TryRunEpisode(UltraEpisodeRequest request, out UltraEpisodeResult result)
        {
            result = null;
            if (request == null) return false;
            var one = new UltraEpisodeResult[1];
            if (!TryRunEpisodes(new List<UltraEpisodeRequest> { request }, one)) return false;
            result = one[0];
            return result != null;
        }

        private sealed class Slot
        {
            public int index;
            public string directory;
            public string responsePath;
            public Process process;
            public Stopwatch clock;
        }

        /// <summary>In-flight batch. One <see cref="Poll"/> is a single non-blocking sweep:
        /// start what fits, harvest what finished, return.</summary>
        private sealed class BatchRun : IUltraEpisodeBatchRun
        {
            public UltraProcessEpisodeOracle owner;
            public IList<UltraEpisodeRequest> requests;
            public UltraEpisodeResult[] collected;
            public readonly List<Slot> running = new List<Slot>();
            public int next;
            public bool done;
            public bool failed;

            public bool Poll(UltraEpisodeResult[] results) { return owner.PollRun(this, results); }
            public void Cancel() { owner.CancelRun(this); }
        }

        /// <summary>Start a batch without waiting for it.
        ///
        /// <para><b>Why this exists.</b> The blocking form parks whichever thread calls it, and
        /// the caller is Unity's main thread inside the run loop — so the editor renders no
        /// frames for the several seconds a decision takes. Splitting launch from collection
        /// lets AutoRunner give a frame back between sweeps.</para>
        ///
        /// <para>No background thread is involved on purpose. Moving the wait off-thread would
        /// drag <c>JsonUtility</c> parsing with it, and that is main-thread territory — a whole
        /// new class of failure for a problem that only needs the loop turned inside out.</para></summary>
        public IUltraEpisodeBatchRun BeginEpisodes(IList<UltraEpisodeRequest> requests)
        {
            if (requests == null || requests.Count == 0 || !IsHealthy) return null;
            try { Directory.CreateDirectory(_workRoot); }
            catch (Exception ex)
            {
                Debug.LogError("[UltraEpisodeOracle] work root failed: " + ex.Message);
                return null;
            }
            return new BatchRun
            {
                owner = this,
                requests = requests,
                collected = new UltraEpisodeResult[requests.Count],
            };
        }

        private bool PollRun(BatchRun run, UltraEpisodeResult[] results)
        {
            if (run == null) return true;
            if (run.done) { Publish(run, results); return true; }
            try
            {
                while (run.running.Count < MaxParallelEpisodes && run.next < run.requests.Count)
                {
                    Slot slot = Launch(run.requests[run.next], run.next);
                    if (slot == null) run.collected[run.next] = null;   // launch failed = no answer
                    else run.running.Add(slot);
                    run.next++;
                }

                for (int i = run.running.Count - 1; i >= 0; i--)
                {
                    Slot slot = run.running[i];
                    bool exited = slot.process == null || slot.process.HasExited;
                    bool timedOut = EpisodeTimeoutSeconds > 0
                        && slot.clock.Elapsed.TotalSeconds > EpisodeTimeoutSeconds;

                    if (!exited && !timedOut) continue;
                    if (!exited && timedOut)
                    {
                        try { slot.process.Kill(); } catch { }
                        Debug.LogWarning("[UltraEpisodeOracle] rollout " + slot.index
                            + " timed out after " + EpisodeTimeoutSeconds + "s");
                    }

                    run.collected[slot.index] = ReadResult(slot);
                    Cleanup(slot);
                    run.running.RemoveAt(i);
                }

                if (run.next >= run.requests.Count && run.running.Count == 0) run.done = true;
                if (run.done) Publish(run, results);
                return run.done;
            }
            catch (Exception ex)
            {
                Debug.LogError("[UltraEpisodeOracle] batch failed: " + ex.Message);
                CancelRun(run);
                run.failed = true;
                run.done = true;
                Publish(run, results);
                return true;
            }
        }

        private static void Publish(BatchRun run, UltraEpisodeResult[] results)
        {
            if (results == null) return;
            int n = Math.Min(results.Length, run.collected.Length);
            for (int i = 0; i < n; i++) results[i] = run.collected[i];
        }

        private void CancelRun(BatchRun run)
        {
            if (run == null) return;
            for (int i = 0; i < run.running.Count; i++)
            {
                try { if (!run.running[i].process.HasExited) run.running[i].process.Kill(); } catch { }
                Cleanup(run.running[i]);
            }
            run.running.Clear();
            run.done = true;
        }

        public bool TryRunEpisodes(IList<UltraEpisodeRequest> requests, UltraEpisodeResult[] results)
        {
            if (requests == null || results == null) return false;
            if (requests.Count == 0) return true;
            if (results.Length < requests.Count) return false;
            if (!IsHealthy) return false;

            IUltraEpisodeBatchRun run = BeginEpisodes(requests);
            if (run == null) return false;
            // ブロッキング形は残す ── コスト実測メニューのように、 呼び出し元が
            // メインスレッドを止めても構わない場面がある。
            while (!run.Poll(results)) System.Threading.Thread.Sleep(25);
            return true;
        }

        private Slot Launch(UltraEpisodeRequest request, int index)
        {
            try
            {
                string directory = Path.Combine(
                    _workRoot, "ep_" + (_sequence++).ToString("D6"));
                Directory.CreateDirectory(directory);

                var slot = new Slot
                {
                    index = index,
                    directory = directory,
                    responsePath = Path.Combine(directory, "result.json"),
                    clock = Stopwatch.StartNew(),
                };

                var envelope = new UltraWorkerRequest
                {
                    kind = UltraWorkerProtocol.EpisodeRequestKind,
                    requestId = request.jobId,
                    episode = new UltraWorkerEpisodePayload
                    {
                        checkpointSchema = request.checkpointSchema,
                        publicCheckpointBase64 = request.publicCheckpointBase64,
                        actionId = request.actionId,
                        actionPayload = request.actionPayload,
                        scenarioStartSeedHex = request.scenarioStartSeedHex,
                        maxProductionSteps = request.maxProductionSteps,
                    },
                };
                UltraWorkerProtocol.SealRequest(envelope);

                var job = new UltraEpisodeJob
                {
                    request = envelope,
                    profile = _profile,
                    continuationPolicy = _continuationPolicy,
                    baselinePolicy = _baselinePolicy?.Clone(),
                    responsePath = slot.responsePath,
                    outputRoot = Path.Combine(directory, "AutoRunLogs"),
                };

                string jobPath = Path.Combine(directory, "job.json");
                File.WriteAllText(jobPath, JsonUtility.ToJson(job, false), new UTF8Encoding(false));

                var start = new ProcessStartInfo
                {
                    FileName = _executablePath,
                    Arguments = "-batchmode -nographics -logFile "
                        + Quote(Path.Combine(directory, "worker.log")) + " "
                        + UltraEpisodeWorkerBootstrap.JobArgument + " " + Quote(jobPath),
                    WorkingDirectory = _workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                slot.process = Process.Start(start);
                return slot.process != null ? slot : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UltraEpisodeOracle] launch failed: " + ex.Message);
                return null;
            }
        }

        private static UltraEpisodeResult ReadResult(Slot slot)
        {
            try
            {
                if (!File.Exists(slot.responsePath)) return null;
                var result = JsonUtility.FromJson<UltraEpisodeResult>(
                    File.ReadAllText(slot.responsePath, Encoding.UTF8));
                // IsUsable is checked by the caller; returning an unusable result and returning
                // null are equivalent to it, so no reason to distinguish here.
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UltraEpisodeOracle] unreadable response: " + ex.Message);
                return null;
            }
        }

        private void Cleanup(Slot slot)
        {
            try { slot.process?.Dispose(); } catch { }
            if (KeepArtifacts) return;
            try { if (Directory.Exists(slot.directory)) Directory.Delete(slot.directory, true); }
            catch { }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        public void Dispose()
        {
            if (KeepArtifacts) return;
            try { if (Directory.Exists(_workRoot)) Directory.Delete(_workRoot, true); }
            catch { }
        }
    }
}
#endif

using System;

namespace AutoTest.Ultra
{
    /// <summary>
    /// Ultra が許可する唯一の結果生成境界。
    /// 実装は同一 production build の隔離 worker で実ゲームを走らせること。
    /// 学習モデルや Ultra 独自の遷移計算をこの interface の実装にしてはならない。
    /// </summary>
    public interface IUltraProductionOracle
    {
        bool IsHealthy { get; }
        string BuildFingerprint { get; }

        bool TryRunEpisode(UltraEpisodeRequest request, out UltraEpisodeResult result);
    }

    /// <summary>An oracle that can run many episodes at once.
    ///
    /// <para><b>Why the interface has to admit batching.</b> A rollout is a whole production
    /// run in a separate process, and process startup for a Unity player costs about as much
    /// as a short run. Dispatched one at a time, an evaluation of 5 actions × 16 rollouts pays
    /// that price 80 times in series. Submitting the whole set lets the implementation start
    /// several at once and pay it in parallel — the difference between "usable in a batch" and
    /// "usable once, as a demo".</para>
    ///
    /// <para>Results are written positionally: <c>results[i]</c> belongs to
    /// <c>requests[i]</c>. An entry left null means that episode did not produce a usable
    /// answer, which the caller must treat as a failed rollout rather than a loss.</para></summary>
    public interface IUltraProductionOracleBatch : IUltraProductionOracle
    {
        int MaxParallelEpisodes { get; }

        /// <summary>Run every request. Returns false only if the batch could not be attempted
        /// at all; individual failures are reported as null entries in
        /// <paramref name="results"/>.</summary>
        bool TryRunEpisodes(
            System.Collections.Generic.IList<UltraEpisodeRequest> requests,
            UltraEpisodeResult[] results);
    }

    /// <summary>A batch of rollouts that has been started but not waited on.</summary>
    public interface IUltraEpisodeBatchRun
    {
        /// <summary>One non-blocking sweep: start what fits, harvest what finished.
        /// Returns true once every episode has an answer, and only then are
        /// <paramref name="results"/> written.</summary>
        bool Poll(UltraEpisodeResult[] results);

        /// <summary>Abandon the batch and kill anything still running.</summary>
        void Cancel();
    }

    /// <summary>An oracle whose waiting can be spread across frames.
    ///
    /// <para><b>Why this exists.</b> The blocking form parks whichever thread calls it, and the
    /// caller is Unity's main thread inside the run loop — so the editor renders nothing for the
    /// seconds a decision takes, and a long batch looks like a hang. Separating "start" from
    /// "collect" lets the run loop hand a frame back between sweeps.</para>
    ///
    /// <para>This is not threading. The work still happens on the main thread, one sweep at a
    /// time; only the waiting is given back.</para></summary>
    public interface IUltraProductionOracleAsync : IUltraProductionOracle
    {
        /// <summary>Start the batch. Null means it could not be started at all.</summary>
        IUltraEpisodeBatchRun BeginEpisodes(
            System.Collections.Generic.IList<UltraEpisodeRequest> requests);
    }

    [Serializable]
    public sealed class UltraEpisodeRequest
    {
        public const int CurrentProtocolVersion = 1;

        public int protocolVersion = CurrentProtocolVersion;
        public string jobId;
        public string expectedBuildFingerprint;
        public string checkpointSchema;
        public string publicCheckpointBase64;
        public string actionId;
        public string actionPayload;
        /// <summary>本ランとは無関係なproduction episode開始seed（16桁hex）。</summary>
        public string scenarioStartSeedHex;
        public int maxProductionSteps;
    }

    [Serializable]
    public sealed class UltraEpisodeResult
    {
        public bool success;
        public bool reachedTerminal;
        /// <summary>7層完全クリア=1、それ以外=0。中間到達点との加重和は禁止。</summary>
        public float primaryReward;
        public string finalPublicStateHash;
        public int productionSteps;
        public string failureCode;
        public string failureDetail;

        public bool IsUsable
            => success
            && reachedTerminal
            && !float.IsNaN(primaryReward)
            && !float.IsInfinity(primaryReward)
            && (primaryReward == 0f || primaryReward == 1f);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutoTest.Ultra
{
    /// <summary>How one candidate action scored.</summary>
    public sealed class UltraActionValue
    {
        public string actionId = "";
        public int rollouts;
        public int clears;
        public int failedRollouts;
        public double ClearRate { get { return rollouts > 0 ? clears / (double)rollouts : 0.0; } }

        /// <summary>Wilson lower bound at ~95%. Used for ranking instead of the raw rate: with
        /// a handful of rollouts a 1-for-1 action would otherwise outrank a 40-for-100 one.</summary>
        public double LowerBound
        {
            get
            {
                if (rollouts <= 0) return 0.0;
                const double z = 1.96;
                double n = rollouts, p = ClearRate;
                double denominator = 1 + z * z / n;
                double centre = p + z * z / (2 * n);
                double margin = z * Math.Sqrt(p * (1 - p) / n + z * z / (4 * n * n));
                return Math.Max(0.0, (centre - margin) / denominator);
            }
        }
    }

    /// <summary>An evaluation that has been started but not finished.
    ///
    /// <para>Held by the caller across frames. <see cref="UltraRolloutEvaluator.PollEvaluate"/>
    /// advances it; until that returns true the result is not meaningful.</para></summary>
    public sealed class UltraPendingEvaluation
    {
        internal UltraEvaluationResult result = new UltraEvaluationResult();
        internal bool complete;
        internal IUltraEpisodeBatchRun run;
        internal UltraEpisodeResult[] episodes;
        internal List<int> owner;
        internal UltraActionValue[] values;
        internal int rolloutsPerAction;

        public bool IsComplete { get { return complete; } }
    }

    public sealed class UltraEvaluationResult
    {
        public string bestActionId = "";
        public bool usable;
        public string diagnostic = "";
        public List<UltraActionValue> values = new List<UltraActionValue>();
        public int totalRollouts;
        public int failedRollouts;
    }

    /// <summary>One-step production rollout improvement (handoff §10 Phase G).
    ///
    /// <para>For each legal action: take it, then let the <b>production</b> policy play the run
    /// out, and count full clears. The action with the best lower bound wins. No learned
    /// transition model is involved anywhere — the only thing that generates outcomes is the
    /// real game running in a worker.</para>
    ///
    /// <para><b>Every rollout gets its own veil seed.</b> Evaluating all candidates against one
    /// sampled board would measure "which action is best if the world happens to be exactly
    /// this" — and since the parent's board is the true one, that quietly reintroduces the leak
    /// the veil exists to close.</para>
    ///
    /// <para><b>Refusing to answer is a supported outcome.</b> If too few rollouts come back
    /// usable, the result is marked unusable and the caller keeps the production choice. An
    /// evaluator that always returns something would turn worker failures into confident
    /// nonsense.</para></summary>
    public sealed class UltraRolloutEvaluator
    {
        /// <summary>Rollouts per candidate action.</summary>
        public int RolloutsPerAction = 16;
        /// <summary>Fraction of rollouts that must succeed before the answer is trusted.</summary>
        public double MinUsableFraction = 0.75;
        /// <summary>Steps a single production rollout may take before it is abandoned.</summary>
        public int MaxProductionSteps = 100_000;

        /// <summary>手が 1 つしかなかったので rollout を撃たずに済んだ決定点の数 (診断用)。</summary>
        public static int SkippedTrivialDecisions { get; private set; }

        // ------------------------------------------------------------------
        //  「本当に評価できていたか」の計装
        //
        //  0pt のクリア率は 13% 程度なので、 1手 4本 なら両方の手が 0/4 になる決定が
        //  珍しくない。 そのとき 2 手の下限はどちらも 0.000 の同点で、 Rank の同点処理
        //  (CompareOrdinal) が **アルファベット順で先のノード id** を返す。
        //  それは評価の結果ではなくタイブレークの結果であって、 「Super と違う手を
        //  36.5% で打った」という数字の中身が入れ替わってしまう。
        //  **勝ったか負けたかではなく、 そもそも差がついたのかを数える。**
        // ------------------------------------------------------------------

        /// <summary>rollout を実際に撃って順位を付けた決定の数。</summary>
        public static int DecisionsEvaluated { get; private set; }
        /// <summary>最良手の下限が 2 位より **厳密に大きかった** 決定の数 (＝差がついた)。</summary>
        public static int DecisionsWithSeparation { get; private set; }
        /// <summary>どの手も 1 回もクリアしなかった決定の数 (＝情報ゼロ)。</summary>
        public static int DecisionsAllZero { get; private set; }

        public static void ResetCounters()
        {
            SkippedTrivialDecisions = 0;
            DecisionsEvaluated = 0;
            DecisionsWithSeparation = 0;
            DecisionsAllZero = 0;
        }

        public static string DescribeCounters()
        {
            if (DecisionsEvaluated <= 0)
                return "rollout 評価 0 件 (手=1 で省略 " + SkippedTrivialDecisions + ")";
            return string.Format(CultureInfo.InvariantCulture,
                "rollout 評価 {0} 件 / **差がついた {1} 件 ({2:F1}%)** / 全手0クリア {3} 件 ({4:F1}%)"
                + " / 手=1 で省略 {5}",
                DecisionsEvaluated, DecisionsWithSeparation,
                100.0 * DecisionsWithSeparation / DecisionsEvaluated,
                DecisionsAllZero, 100.0 * DecisionsAllZero / DecisionsEvaluated,
                SkippedTrivialDecisions);
        }

        private readonly IUltraProductionOracle _oracle;

        public UltraRolloutEvaluator(IUltraProductionOracle oracle)
        {
            _oracle = oracle;
        }

        /// <summary>Blocking form. Convenient where parking the caller is fine (the cost probe,
        /// tests); the run loop uses <see cref="BeginEvaluate"/> instead so the editor keeps
        /// drawing frames.</summary>
        public UltraEvaluationResult Evaluate(
            UltraCheckpoint checkpoint,
            Func<int, UltraResumePayload> payloadForVeilSeed,
            ulong scenarioSeedBase)
        {
            UltraPendingEvaluation pending =
                BeginEvaluate(checkpoint, payloadForVeilSeed, scenarioSeedBase);
            UltraEvaluationResult finished;
            while (!PollEvaluate(pending, out finished))
                System.Threading.Thread.Sleep(25);
            return finished;
        }

        /// <summary>Start an evaluation. The result may already be settled (a single legal
        /// action, an unusable oracle), in which case the first poll returns immediately.</summary>
        public UltraPendingEvaluation BeginEvaluate(
            UltraCheckpoint checkpoint,
            Func<int, UltraResumePayload> payloadForVeilSeed,
            ulong scenarioSeedBase)
        {
            var pending = new UltraPendingEvaluation();
            var result = new UltraEvaluationResult();
            pending.result = result;
            if (_oracle == null || !_oracle.IsHealthy)
            {
                result.diagnostic = "oracle unavailable";
                pending.complete = true;
                return pending;
            }
            if (checkpoint?.legalActions == null || checkpoint.legalActions.Length == 0)
            {
                result.diagnostic = "no legal actions to evaluate";
                pending.complete = true;
                return pending;
            }
            if (payloadForVeilSeed == null)
            {
                result.diagnostic = "no payload source";
                pending.complete = true;
                return pending;
            }

            // 手が 1 つしかない決定点に rollout を撃っても、 結果がどうであれ選ぶ手は同じ。
            // **これは発見的な間引きではなく、 定義上ただ働き**なので、 方策を 1 ミリも
            // 変えずに丸ごと省ける。 マップは車線構造なので、 この形の決定点は珍しくない。
            if (checkpoint.legalActions.Length == 1)
            {
                result.usable = true;
                result.bestActionId = checkpoint.legalActions[0].ActionId;
                result.values.Add(new UltraActionValue { actionId = result.bestActionId });
                result.diagnostic = "single legal action — no rollouts spent";
                SkippedTrivialDecisions++;
                pending.complete = true;
                return pending;
            }

            int rollouts = Math.Max(1, RolloutsPerAction);

            // Build the whole set first. Every (action, rollout) pair is independent, so
            // enumerating them up front is what lets a batching oracle start several at once —
            // with a process per episode, dispatching serially would dominate the cost.
            var requests = new List<UltraEpisodeRequest>(
                checkpoint.legalActions.Length * rollouts);
            var owner = new List<int>(requests.Capacity);
            var values = new UltraActionValue[checkpoint.legalActions.Length];

            // Payloads are built once per rollout index and shared across actions: the veil
            // seed depends on the rollout only, so every candidate is measured against the
            // *same set* of sampled boards (common random numbers). Comparing actions on
            // different boards would measure the boards.
            var payloads = new UltraResumePayload[rollouts];
            for (int r = 0; r < rollouts; r++) payloads[r] = payloadForVeilSeed(r);

            for (int a = 0; a < checkpoint.legalActions.Length; a++)
            {
                UltraLegalAction action = checkpoint.legalActions[a];
                values[a] = new UltraActionValue { actionId = action.ActionId };

                for (int r = 0; r < rollouts; r++)
                {
                    if (payloads[r] == null) { values[a].failedRollouts++; continue; }
                    requests.Add(new UltraEpisodeRequest
                    {
                        jobId = checkpoint.epoch.ToString(CultureInfo.InvariantCulture)
                              + ":" + a.ToString(CultureInfo.InvariantCulture)
                              + ":" + r.ToString(CultureInfo.InvariantCulture),
                        expectedBuildFingerprint = _oracle.BuildFingerprint,
                        checkpointSchema = UltraResumePayload.Schema,
                        publicCheckpointBase64 = payloads[r].ToBase64(),
                        actionId = action.ActionId,
                        actionPayload = "",
                        scenarioStartSeedHex = ScenarioSeed(scenarioSeedBase, a, r),
                        maxProductionSteps = MaxProductionSteps,
                    });
                    owner.Add(a);
                }
            }

            pending.episodes = new UltraEpisodeResult[requests.Count];
            pending.owner = owner;
            pending.values = values;
            pending.rolloutsPerAction = rollouts;

            if (requests.Count == 0)
            {
                Settle(pending);
                return pending;
            }

            // 非同期に走れるオラクルなら待たない。 走れないなら従来どおりここでブロックする
            // ── テストのスタブや、 メインスレッドを止めて構わない呼び出し元のため。
            if (_oracle is IUltraProductionOracleAsync async)
            {
                pending.run = async.BeginEpisodes(requests);
                if (pending.run != null) return pending;
            }
            Dispatch(requests, pending.episodes);
            Settle(pending);
            return pending;
        }

        /// <summary>Advance a pending evaluation by one sweep. True when it is finished, and
        /// only then is <paramref name="result"/> meaningful.</summary>
        public bool PollEvaluate(UltraPendingEvaluation pending, out UltraEvaluationResult result)
        {
            if (pending == null) { result = new UltraEvaluationResult(); return true; }
            result = pending.result;
            if (pending.complete) return true;
            if (pending.run == null) { Settle(pending); return true; }
            if (!pending.run.Poll(pending.episodes)) return false;
            Settle(pending);
            return true;
        }

        /// <summary>Abandon an evaluation in flight (the run ended, the batch was stopped).</summary>
        public static void CancelEvaluation(UltraPendingEvaluation pending)
        {
            if (pending == null || pending.complete) return;
            try { pending.run?.Cancel(); } catch { }
            pending.complete = true;
            pending.result.usable = false;
            pending.result.diagnostic = "evaluation cancelled";
        }

        /// <summary>Tally the episodes and rank. Idempotent guard: a settled pending is done.</summary>
        private void Settle(UltraPendingEvaluation pending)
        {
            if (pending.complete) return;
            pending.complete = true;

            UltraEvaluationResult result = pending.result;
            UltraEpisodeResult[] episodes = pending.episodes ?? new UltraEpisodeResult[0];
            for (int i = 0; i < episodes.Length; i++)
            {
                UltraActionValue value = pending.values[pending.owner[i]];
                result.totalRollouts++;
                UltraEpisodeResult episode = episodes[i];
                if (episode == null || !episode.IsUsable)
                {
                    value.failedRollouts++;
                    result.failedRollouts++;
                    continue;
                }
                value.rollouts++;
                if (episode.primaryReward >= 1f) value.clears++;
            }

            for (int a = 0; a < pending.values.Length; a++) result.values.Add(pending.values[a]);
            Rank(result, pending.rolloutsPerAction);
        }

        private void Dispatch(IList<UltraEpisodeRequest> requests, UltraEpisodeResult[] results)
        {
            if (_oracle is IUltraProductionOracleBatch batch)
            {
                // A batch that cannot even be attempted leaves every entry null, which the
                // caller already treats as failed rollouts — no separate error path needed.
                batch.TryRunEpisodes(requests, results);
                return;
            }
            for (int i = 0; i < requests.Count; i++)
                results[i] = _oracle.TryRunEpisode(requests[i], out UltraEpisodeResult one) ? one : null;
        }

        private UltraEvaluationResult Rank(UltraEvaluationResult result, int rolloutsPerAction)
        {
            int required = (int)Math.Ceiling(rolloutsPerAction * MinUsableFraction);
            UltraActionValue best = null;
            int starved = 0;

            for (int i = 0; i < result.values.Count; i++)
            {
                UltraActionValue value = result.values[i];
                if (value.rollouts < required) { starved++; continue; }
                if (best == null
                    || value.LowerBound > best.LowerBound
                    || (value.LowerBound == best.LowerBound
                        && string.CompareOrdinal(value.actionId, best.actionId) < 0))
                    best = value;
            }

            if (best == null)
            {
                result.usable = false;
                result.diagnostic = "every action fell short of " + required
                    + " usable rollouts (" + starved + " starved)";
                return result;
            }
            // An action cannot be called better than the rest if some rivals were never
            // properly measured — the comparison would be against silence.
            if (starved > 0)
            {
                result.usable = false;
                result.diagnostic = starved + " action(s) never reached " + required
                    + " usable rollouts, so the comparison is incomplete";
                return result;
            }

            // **差がついたのかを記録する。** 同点なら選ばれたのは「評価で勝った手」ではなく
            // 「id が辞書順で先の手」。 その区別を持たないと、 上書き率を強さと読み違える。
            double runnerUp = -1.0;
            int totalClears = 0;
            for (int i = 0; i < result.values.Count; i++)
            {
                UltraActionValue value = result.values[i];
                totalClears += value.clears;
                if (ReferenceEquals(value, best)) continue;
                if (value.LowerBound > runnerUp) runnerUp = value.LowerBound;
            }
            bool separated = result.values.Count <= 1 || best.LowerBound > runnerUp;
            DecisionsEvaluated++;
            if (separated && result.values.Count > 1) DecisionsWithSeparation++;
            if (totalClears == 0) DecisionsAllZero++;

            // **差がつかなかったら辞退する。** 下限が並んだ手のあいだで順位を付ける根拠は
            // 何も無く、 現行の同点処理は id の辞書順で決めている。 それは方策ではなく、
            // 方策の恰好をした事故 ── 「Super と違う手を N% で打った」の中身が
            // ノード id の並び順に化ける。 辞退すれば生産方策がそのまま通る。
            //
            // 2026-08-18 実測 (0pt・1手4本): 評価 335 件のうち同点 177 件 (52.8%)、
            // うち全手0クリア 104 件。 クリア率 13% の盤面で 4 本では、
            // ほとんどの決定が 0/4 対 0/4 になる。
            if (!separated)
            {
                result.usable = false;
                result.diagnostic = string.Format(CultureInfo.InvariantCulture,
                    "no separation: 下限 {0:F3} が 2 位と並んだ (全手クリア {1})",
                    best.LowerBound, totalClears);
                return result;
            }

            result.usable = true;
            result.bestActionId = best.actionId;
            result.diagnostic = string.Format(CultureInfo.InvariantCulture,
                "best={0} clear {1}/{2} (lower bound {3:F3}, 2位 {4:F3}) over {5} actions",
                best.actionId, best.clears, best.rollouts, best.LowerBound,
                Math.Max(0.0, runnerUp), result.values.Count);
            return result;
        }

        private static string ScenarioSeed(ulong baseSeed, int actionIndex, int rolloutIndex)
        {
            unchecked
            {
                ulong z = baseSeed
                    + 0x9E3779B97F4A7C15UL * ((ulong)(uint)actionIndex + 1UL)
                    + 0xBF58476D1CE4E5B9UL * ((ulong)(uint)rolloutIndex + 1UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return (z ^ (z >> 31)).ToString("X16", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>An <see cref="IUltraDecisionSink"/> that decides by rollout.
    ///
    /// <para>The whole point of the Ultra stack reduced to one class: observe, enumerate legal
    /// actions, play each one out in the real game, take the best. Everything else exists to
    /// make this safe.</para></summary>
    /// <summary>A decision in flight. Held by the run loop across frames.</summary>
    public sealed class UltraPendingDecision
    {
        internal UltraPendingEvaluation evaluation;
        internal bool settled;
        internal string reason;

        public bool IsSettled { get { return settled; } }
    }

    public sealed class UltraRolloutSink : IUltraDecisionSink
    {
        private readonly UltraRolloutEvaluator _evaluator;
        private readonly Func<UltraCheckpoint, int, UltraResumePayload> _payloads;
        private readonly ulong _scenarioSeedBase;

        public UltraEvaluationResult LastResult { get; private set; }

        /// <summary>rollout を撃つ決定点。 空なら全部。
        ///
        /// <para><b>絞りは方策の一部であって最適化ではない。</b> 撃たなかった決定点では
        /// 生産方策がそのまま通るので、 ここを変えると測っている対象が変わる。
        /// どこで撃ったかはサマリに残すこと。</para></summary>
        public readonly HashSet<UltraDecisionPoint> AllowedPoints =
            new HashSet<UltraDecisionPoint>();

        /// <summary>絞りによって見送った決定点の数 (診断用)。</summary>
        public int GatedOut { get; private set; }

        public UltraRolloutSink(
            UltraRolloutEvaluator evaluator,
            Func<UltraCheckpoint, int, UltraResumePayload> payloads,
            ulong scenarioSeedBase)
        {
            _evaluator = evaluator;
            _payloads = payloads;
            _scenarioSeedBase = scenarioSeedBase;
        }

        /// <summary>盤面が固定された場面 (テスト・コスト計測) 用。</summary>
        public UltraRolloutSink(
            UltraRolloutEvaluator evaluator,
            Func<int, UltraResumePayload> payloads,
            ulong scenarioSeedBase)
            : this(evaluator,
                   payloads == null ? (Func<UltraCheckpoint, int, UltraResumePayload>)null
                                    : (_, seed) => payloads(seed),
                   scenarioSeedBase)
        {
        }

        public bool TryDecide(UltraCheckpoint checkpoint, out string committedActionId, out string reason)
        {
            UltraPendingDecision pending = BeginDecide(checkpoint);
            while (!PollDecide(pending, out committedActionId, out reason))
                System.Threading.Thread.Sleep(25);
            return committedActionId != null;
        }

        /// <summary>Start a decision without waiting. Some outcomes settle immediately
        /// (gated out, no evaluator), and the first poll then returns them.</summary>
        public UltraPendingDecision BeginDecide(UltraCheckpoint checkpoint)
        {
            var pending = new UltraPendingDecision();
            if (_evaluator == null) { pending.reason = "no evaluator"; pending.settled = true; return pending; }
            if (_payloads == null) { pending.reason = "no payload source"; pending.settled = true; return pending; }

            if (AllowedPoints.Count > 0 && checkpoint != null
                && !AllowedPoints.Contains(checkpoint.point))
            {
                GatedOut++;
                pending.reason = "decision point not in the rollout budget";
                pending.settled = true;
                return pending;
            }

            // **payload は決定のたびに作り直す。** 盤面は毎回違うので、 sink の生成時に
            // 固定した payload を使い回すと、 全ての決定を同じ古い盤面から評価することになる。
            pending.evaluation = _evaluator.BeginEvaluate(
                checkpoint, seed => _payloads(checkpoint, seed),
                Mix(_scenarioSeedBase, checkpoint != null ? checkpoint.epoch : 0));
            return pending;
        }

        public bool PollDecide(
            UltraPendingDecision pending, out string committedActionId, out string reason)
        {
            committedActionId = null;
            reason = null;
            if (pending == null) return true;
            if (pending.settled) { reason = pending.reason; return true; }

            if (!_evaluator.PollEvaluate(pending.evaluation, out UltraEvaluationResult result))
                return false;

            pending.settled = true;
            LastResult = result;
            if (!result.usable) { pending.reason = reason = result.diagnostic; return true; }
            committedActionId = result.bestActionId;
            return true;
        }

        public static void CancelDecision(UltraPendingDecision pending)
        {
            if (pending == null || pending.settled) return;
            UltraRolloutEvaluator.CancelEvaluation(pending.evaluation);
            pending.settled = true;
            pending.reason = "decision cancelled";
        }

        /// <summary>決定ごとに別の scenario seed を張る。 同じ base のままだと、
        /// ラン中の全決定が同じ未来の列を引き直すことになる。</summary>
        private static ulong Mix(ulong baseSeed, long epoch)
        {
            unchecked
            {
                ulong z = baseSeed + 0x9E3779B97F4A7C15UL * ((ulong)epoch + 1UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}

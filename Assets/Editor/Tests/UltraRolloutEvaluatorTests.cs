#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AutoTest.Ultra;
using MapSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    /// <summary>
    /// Phase G: one-step production rollout improvement — the first point at which Ultra
    /// actually evaluates anything.
    ///
    /// <para>The oracle is stubbed here because what is under test is the decision logic:
    /// which action wins, when the evaluator refuses to answer, and whether beliefs are
    /// re-sampled. Wiring the real production worker behind
    /// <see cref="IUltraProductionOracle"/> does not change any of it.</para>
    /// </summary>
    [TestFixture]
    public sealed class UltraRolloutEvaluatorTests
    {
        private sealed class StubOracle : IUltraProductionOracle
        {
            /// <summary>actionId → probability of a full clear.</summary>
            public Dictionary<string, double> clearRateByAction = new Dictionary<string, double>();
            public HashSet<string> failFor = new HashSet<string>();
            public bool healthy = true;
            public List<UltraEpisodeRequest> seen = new List<UltraEpisodeRequest>();

            public bool IsHealthy { get { return healthy; } }
            public string BuildFingerprint { get { return new string('a', 64); } }

            public bool TryRunEpisode(UltraEpisodeRequest request, out UltraEpisodeResult result)
            {
                seen.Add(request);
                result = null;
                if (failFor.Contains(request.actionId)) return false;

                clearRateByAction.TryGetValue(request.actionId, out double rate);
                // Deterministic in the scenario seed, so a test never flakes.
                int bucket = Math.Abs(request.scenarioStartSeedHex.GetHashCode()) % 1000;
                result = new UltraEpisodeResult
                {
                    success = true,
                    reachedTerminal = true,
                    primaryReward = bucket < rate * 1000 ? 1f : 0f,
                    finalPublicStateHash = "hash",
                    productionSteps = 10,
                };
                return true;
            }
        }

        private static UltraCheckpoint Checkpoint(params string[] nodeIds)
        {
            var nodes = new List<UltraMapNodeView>();
            for (int i = 0; i < nodeIds.Length; i++)
                nodes.Add(new UltraMapNodeView
                { id = nodeIds[i], row = 1, view = UltraTileView.Battle, revealed = true, reachableNow = true });

            var observation = new UltraObservation
            { map = new UltraMapView { currentNodeId = "start", nodes = nodes.ToArray() } };

            return UltraCheckpointProtocol.Create(
                3, UltraDecisionPoint.MapNavigation, observation,
                UltraCheckpointProtocol.MapNavigationActions(observation));
        }

        private static UltraResumePayload Payload(int veilSeed)
        {
            return new UltraResumePayload
            {
                epoch = 3,
                decisionPoint = (int)UltraDecisionPoint.MapNavigation,
                veilSeed = veilSeed,
                run = new UltraRunSnapshot
                { fields = new[] { new UltraSnapshotField { name = "coins", kind = "int", value = "10" } } },
                map = new UltraMapSnapshot
                {
                    floor = 5,
                    nodes = new[] { new UltraMapNodeSnapshot { id = "n0", row = 0 } },
                },
            };
        }

        private static string ActionFor(string nodeId)
        {
            return UltraLegalAction.Of(UltraActionKind.MoveTo, nodeId).ActionId;
        }

        [Test]
        public void TheActionWithTheHigherClearRateWins()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("good")] = 0.9;
            oracle.clearRateByAction[ActionFor("bad")] = 0.1;

            var evaluator = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 40 };
            UltraEvaluationResult result = evaluator.Evaluate(
                Checkpoint("good", "bad"), Payload, 0xABCDEF01UL);

            Assert.That(result.usable, Is.True, result.diagnostic);
            Assert.That(result.bestActionId, Is.EqualTo(ActionFor("good")));
        }

        /// <summary>Ranking on the raw rate would let a 1-for-1 action beat a 40-for-100 one.</summary>
        [Test]
        public void ASmallSampleDoesNotBeatAWellMeasuredAction()
        {
            var lucky = new UltraActionValue { rollouts = 1, clears = 1 };
            var solid = new UltraActionValue { rollouts = 100, clears = 40 };

            Assert.That(lucky.ClearRate, Is.GreaterThan(solid.ClearRate));
            Assert.That(lucky.LowerBound, Is.LessThan(solid.LowerBound),
                "the lower bound is what ranking must use");
        }

        [Test]
        public void EveryActionIsMeasuredAgainstTheSameSampledBoards()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("a")] = 0.5;
            oracle.clearRateByAction[ActionFor("b")] = 0.5;

            var seedsSeen = new Dictionary<string, List<int>>();
            Func<int, UltraResumePayload> payloads = seed =>
            {
                UltraResumePayload payload = Payload(seed);
                return payload;
            };

            var evaluator = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 5 };
            evaluator.Evaluate(Checkpoint("a", "b"), payloads, 1UL);

            // Each action should have been run against veil seeds 0..4 — common random numbers.
            foreach (UltraEpisodeRequest request in oracle.seen)
            {
                UltraResumePayload decoded;
                Assert.That(UltraResumePayload.TryFromBase64(
                    request.publicCheckpointBase64, out decoded, out string failure), Is.True, failure);
                if (!seedsSeen.TryGetValue(request.actionId, out List<int> list))
                    seedsSeen[request.actionId] = list = new List<int>();
                list.Add(decoded.veilSeed);
            }

            Assert.That(seedsSeen.Count, Is.EqualTo(2));
            foreach (var pair in seedsSeen)
                Assert.That(pair.Value, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }),
                    "actions compared on different boards would measure the boards, not the actions");
        }

        [Test]
        public void EachRolloutGetsItsOwnScenarioSeed()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("a")] = 0.5;

            oracle.clearRateByAction[ActionFor("b")] = 0.5;

            // 手は 2 つ要る。 手が 1 つの盤面は rollout を撃たずに即答する経路に入るので、
            // ここで見たい「1 本ごとに別の未来を引く」性質が測れない。
            new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 8 }
                .Evaluate(Checkpoint("a", "b"), Payload, 99UL);

            var distinct = new HashSet<string>();
            foreach (UltraEpisodeRequest request in oracle.seen)
                distinct.Add(request.scenarioStartSeedHex);

            Assert.That(oracle.seen.Count, Is.EqualTo(16), "2 手 × 8 本");
            Assert.That(distinct.Count, Is.EqualTo(16),
                "repeating a scenario seed would count the same future sixteen times");
        }

        /// <summary>手が 1 つしかない決定点で rollout を撃つのは、 発見的な間引きの問題では
        /// なく定義上のただ働き ── 結果がどうであれ選ぶ手は同じ。</summary>
        [Test]
        public void ASingleLegalAction_IsAnsweredWithoutSpendingRollouts()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("only")] = 0.5;

            UltraEvaluationResult result = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 16 }
                .Evaluate(Checkpoint("only"), Payload, 1UL);

            Assert.That(result.usable, Is.True, result.diagnostic);
            Assert.That(result.bestActionId, Is.EqualTo(ActionFor("only")));
            Assert.That(oracle.seen, Is.Empty, "一手しか無いのに worker を起動している");
            Assert.That(result.totalRollouts, Is.EqualTo(0));
        }

        /// <summary>省略しても答えは同じでなければならない。 速くなる代わりに手が変わるなら、
        /// それは間引きではなく方策変更。</summary>
        [Test]
        public void ASingleLegalAction_GivesTheSameAnswerAsSpendingRollouts()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("only")] = 0.5;
            UltraCheckpoint checkpoint = Checkpoint("only");

            UltraEvaluationResult skipped = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 16 }
                .Evaluate(checkpoint, Payload, 1UL);

            // 唯一の合法手 = 提示された手そのもの。 rollout を撃っても行き先はここしかない。
            Assert.That(skipped.bestActionId,
                Is.EqualTo(checkpoint.legalActions[0].ActionId));
            Assert.That(UltraCheckpointProtocol.TryValidateCommit(
                checkpoint, 3, UltraDecisionPoint.MapNavigation, skipped.bestActionId,
                out string failure), Is.True, failure);
        }

        // ------------------------------------------------------------------
        //  refusing to answer
        // ------------------------------------------------------------------

        /// <summary>A worker that keeps failing must not be turned into a confident answer.</summary>
        [Test]
        public void TooFewUsableRollouts_MakeTheResultUnusable()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("a")] = 0.5;
            oracle.failFor.Add(ActionFor("b"));

            UltraEvaluationResult result = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 8 }
                .Evaluate(Checkpoint("a", "b"), Payload, 1UL);

            Assert.That(result.usable, Is.False);
            StringAssert.Contains("incomplete", result.diagnostic);
        }

        [Test]
        public void AnUnhealthyOracleDeclinesImmediately()
        {
            var oracle = new StubOracle { healthy = false };

            UltraEvaluationResult result = new UltraRolloutEvaluator(oracle)
                .Evaluate(Checkpoint("a"), Payload, 1UL);

            Assert.That(result.usable, Is.False);
            Assert.That(oracle.seen, Is.Empty, "no work should be attempted");
        }

        [Test]
        public void NoLegalActionsMeansNoAnswer()
        {
            var observation = new UltraObservation();
            UltraCheckpoint empty = UltraCheckpointProtocol.Create(
                1, UltraDecisionPoint.MapNavigation, observation, null);

            UltraEvaluationResult result = new UltraRolloutEvaluator(new StubOracle())
                .Evaluate(empty, Payload, 1UL);

            Assert.That(result.usable, Is.False);
        }

        [Test]
        public void AMissingPayloadCountsAsAFailedRollout_NotACrash()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("a")] = 1.0;
            oracle.clearRateByAction[ActionFor("b")] = 1.0;

            // 手は 2 つ。 1 手だけの盤面は rollout を撃たずに即答するので、
            // payload が壊れていることをそもそも見に行かない。
            UltraEvaluationResult result = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 4 }
                .Evaluate(Checkpoint("a", "b"), _ => null, 1UL);

            Assert.That(result.usable, Is.False);
            Assert.That(oracle.seen, Is.Empty);
        }

        // ------------------------------------------------------------------
        //  as a decision sink
        // ------------------------------------------------------------------

        [Test]
        public void TheSinkCommitsTheBestAction()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("good")] = 0.95;
            oracle.clearRateByAction[ActionFor("bad")] = 0.05;

            var sink = new UltraRolloutSink(
                new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 40 }, Payload, 7UL);

            Assert.That(sink.TryDecide(Checkpoint("good", "bad"),
                out string actionId, out string reason), Is.True, reason);
            Assert.That(actionId, Is.EqualTo(ActionFor("good")));
        }

        /// <summary>Declining is a first-class outcome: AutoRunner then keeps the production
        /// choice, and the run is unaffected.</summary>
        [Test]
        public void TheSinkDeclinesRatherThanGuessing()
        {
            var oracle = new StubOracle { healthy = false };
            var sink = new UltraRolloutSink(new UltraRolloutEvaluator(oracle), Payload, 1UL);

            Assert.That(sink.TryDecide(Checkpoint("a"), out string actionId, out string reason), Is.False);
            Assert.That(actionId, Is.Null);
            Assert.That(reason, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void TheSinkAnswersOnlyWithAnActionItWasOffered()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("a")] = 0.5;
            oracle.clearRateByAction[ActionFor("b")] = 0.5;

            UltraCheckpoint checkpoint = Checkpoint("a", "b");
            var sink = new UltraRolloutSink(
                new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 8 }, Payload, 5UL);
            sink.TryDecide(checkpoint, out string actionId, out _);

            Assert.That(UltraCheckpointProtocol.TryValidateCommit(
                checkpoint, 3, UltraDecisionPoint.MapNavigation, actionId, out string failure),
                Is.True, failure);
        }

        // ------------------------------------------------------------------
        //  フレームを跨ぐ決定
        // ------------------------------------------------------------------

        /// <summary>N 回 Poll されるまで終わらないオラクル。 worker プロセスの完了待ちを
        /// 決定論的に模す。</summary>
        private sealed class DeferredOracle : IUltraProductionOracle, IUltraProductionOracleAsync
        {
            public readonly StubOracle inner = new StubOracle();
            public int pollsBeforeDone = 3;
            public int cancels;

            public bool IsHealthy { get { return inner.IsHealthy; } }
            public string BuildFingerprint { get { return inner.BuildFingerprint; } }
            public bool TryRunEpisode(UltraEpisodeRequest request, out UltraEpisodeResult result)
            {
                return inner.TryRunEpisode(request, out result);
            }

            private sealed class Run : IUltraEpisodeBatchRun
            {
                public DeferredOracle owner;
                public IList<UltraEpisodeRequest> requests;
                public int polls;

                public bool Poll(UltraEpisodeResult[] results)
                {
                    if (++polls < owner.pollsBeforeDone) return false;
                    for (int i = 0; i < requests.Count; i++)
                        results[i] = owner.inner.TryRunEpisode(requests[i], out UltraEpisodeResult one)
                            ? one : null;
                    return true;
                }

                public void Cancel() { owner.cancels++; }
            }

            public IUltraEpisodeBatchRun BeginEpisodes(IList<UltraEpisodeRequest> requests)
            {
                return new Run { owner = this, requests = requests };
            }
        }

        /// <summary>**待っている間は答えを出さない。** ここが崩れると、 run ループが
        /// 未完了の評価を採用して盤面を進めてしまう。</summary>
        [Test]
        public void AnAsyncEvaluation_DoesNotAnswerUntilItsEpisodesFinish()
        {
            var oracle = new DeferredOracle { pollsBeforeDone = 4 };
            oracle.inner.clearRateByAction[ActionFor("good")] = 0.95;
            oracle.inner.clearRateByAction[ActionFor("bad")] = 0.05;

            var evaluator = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 40 };
            UltraPendingEvaluation pending = evaluator.BeginEvaluate(
                Checkpoint("good", "bad"), Payload, 1UL);

            for (int i = 0; i < 3; i++)
                Assert.That(evaluator.PollEvaluate(pending, out _), Is.False,
                    "episode が終わっていないのに完了を返した (掃引 " + (i + 1) + ")");

            Assert.That(evaluator.PollEvaluate(pending, out UltraEvaluationResult result), Is.True);
            Assert.That(result.usable, Is.True, result.diagnostic);
            Assert.That(result.bestActionId, Is.EqualTo(ActionFor("good")));
        }

        /// <summary>非同期にしたことで答えが変わってはいけない。 変わるなら、
        /// 速くなった代わりに方策が別物になっている。</summary>
        [Test]
        public void AsyncAndBlocking_GiveTheSameAnswer()
        {
            var async = new DeferredOracle { pollsBeforeDone = 3 };
            async.inner.clearRateByAction[ActionFor("good")] = 0.9;
            async.inner.clearRateByAction[ActionFor("bad")] = 0.1;

            var sync = new StubOracle();
            sync.clearRateByAction[ActionFor("good")] = 0.9;
            sync.clearRateByAction[ActionFor("bad")] = 0.1;

            UltraEvaluationResult blocking = new UltraRolloutEvaluator(sync) { RolloutsPerAction = 40 }
                .Evaluate(Checkpoint("good", "bad"), Payload, 5UL);

            var evaluator = new UltraRolloutEvaluator(async) { RolloutsPerAction = 40 };
            UltraPendingEvaluation pending = evaluator.BeginEvaluate(
                Checkpoint("good", "bad"), Payload, 5UL);
            UltraEvaluationResult deferred;
            while (!evaluator.PollEvaluate(pending, out deferred)) { }

            Assert.That(deferred.usable, Is.EqualTo(blocking.usable));
            Assert.That(deferred.bestActionId, Is.EqualTo(blocking.bestActionId));
            Assert.That(deferred.totalRollouts, Is.EqualTo(blocking.totalRollouts));
        }

        /// <summary>ラン終了やバッチ停止で捨てたとき、 走っている worker を殺すこと。
        /// 殺し忘れると、 誰も答えを待っていないプロセスが取り残される。</summary>
        [Test]
        public void CancellingAnEvaluation_StopsTheWorkers()
        {
            var oracle = new DeferredOracle { pollsBeforeDone = 99 };
            oracle.inner.clearRateByAction[ActionFor("a")] = 0.5;
            oracle.inner.clearRateByAction[ActionFor("b")] = 0.5;

            var evaluator = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 4 };
            UltraPendingEvaluation pending = evaluator.BeginEvaluate(
                Checkpoint("a", "b"), Payload, 1UL);

            Assert.That(evaluator.PollEvaluate(pending, out _), Is.False);
            UltraRolloutEvaluator.CancelEvaluation(pending);

            Assert.That(oracle.cancels, Is.EqualTo(1), "worker を殺していない");
            Assert.That(evaluator.PollEvaluate(pending, out UltraEvaluationResult result), Is.True);
            Assert.That(result.usable, Is.False, "捨てた評価を採用してはいけない");
        }

        [Test]
        public void TheSink_DefersAndThenCommits()
        {
            var oracle = new DeferredOracle { pollsBeforeDone = 3 };
            oracle.inner.clearRateByAction[ActionFor("good")] = 0.95;
            oracle.inner.clearRateByAction[ActionFor("bad")] = 0.05;

            var sink = new UltraRolloutSink(
                new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 40 }, Payload, 1UL);
            sink.AllowedPoints.Add(UltraDecisionPoint.MapNavigation);

            UltraPendingDecision pending = sink.BeginDecide(Checkpoint("good", "bad"));
            Assert.That(sink.PollDecide(pending, out _, out _), Is.False);
            Assert.That(sink.PollDecide(pending, out _, out _), Is.False);
            Assert.That(sink.PollDecide(pending, out string actionId, out string reason), Is.True, reason);
            Assert.That(actionId, Is.EqualTo(ActionFor("good")));
        }

        /// <summary>絞りで外した決定点は待たずに即辞退する。 待たせると、 撃たない決定点の
        /// ぶんだけラン が実時間で伸びる。</summary>
        [Test]
        public void AGatedOutDecision_SettlesOnTheFirstPoll()
        {
            var oracle = new DeferredOracle { pollsBeforeDone = 99 };
            oracle.inner.clearRateByAction[ActionFor("a")] = 0.5;

            var sink = new UltraRolloutSink(
                new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 8 }, Payload, 1UL);
            sink.AllowedPoints.Add(UltraDecisionPoint.RewardChoice);

            UltraPendingDecision pending = sink.BeginDecide(Checkpoint("a", "b"));
            Assert.That(sink.PollDecide(pending, out string actionId, out _), Is.True);
            Assert.That(actionId, Is.Null);
        }

        // ------------------------------------------------------------------
        //  差がつかなかったら答えない
        // ------------------------------------------------------------------

        /// <summary>下限が並んだら辞退する。 並んだ手のあいだで順位を付ける根拠は無く、
        /// 同点処理は id の辞書順 ── **方策の恰好をしたタイブレーク**になる。</summary>
        [Test]
        public void WhenNoActionClears_TheEvaluatorDeclinesInsteadOfPickingAlphabetically()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("aaa")] = 0.0;
            oracle.clearRateByAction[ActionFor("zzz")] = 0.0;

            UltraEvaluationResult result = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 8 }
                .Evaluate(Checkpoint("aaa", "zzz"), Payload, 1UL);

            Assert.That(result.usable, Is.False,
                "全手0クリアなのに手を選んでいる ── 選ばれたのは id の辞書順");
            StringAssert.Contains("no separation", result.diagnostic);
        }

        /// <summary>クリアはしているが差が無い場合も同じ。 0 かどうかではなく、
        /// **下限が並んだかどうか**で決める。</summary>
        [Test]
        public void EquallyGoodActions_AreDeclined_NotTieBroken()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("aaa")] = 1.0;
            oracle.clearRateByAction[ActionFor("zzz")] = 1.0;

            UltraEvaluationResult result = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 8 }
                .Evaluate(Checkpoint("aaa", "zzz"), Payload, 1UL);

            Assert.That(result.usable, Is.False, "同じ強さの 2 手に順位を付けている");
        }

        [Test]
        public void AMeasuredDifference_StillDecides()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("zzz")] = 0.95;   // 辞書順では後ろ
            oracle.clearRateByAction[ActionFor("aaa")] = 0.05;

            UltraEvaluationResult result = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 40 }
                .Evaluate(Checkpoint("aaa", "zzz"), Payload, 1UL);

            Assert.That(result.usable, Is.True, result.diagnostic);
            Assert.That(result.bestActionId, Is.EqualTo(ActionFor("zzz")),
                "差がついているのに辞書順に負けている");
        }

        // ------------------------------------------------------------------
        //  絞り込みと、 決定ごとの盤面
        // ------------------------------------------------------------------

        /// <summary>撃たない決定点では**辞退**する ── 生産方策がそのまま通る。
        /// ここで適当な手を返すと、 絞ったつもりが方策の差し替えになる。</summary>
        [Test]
        public void AGatedOutDecisionPoint_DeclinesWithoutSpendingRollouts()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("a")] = 0.9;
            oracle.clearRateByAction[ActionFor("b")] = 0.1;

            var sink = new UltraRolloutSink(
                new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 8 }, Payload, 1UL);
            sink.AllowedPoints.Add(UltraDecisionPoint.RewardChoice);   // MapNavigation は撃たない

            Assert.That(sink.TryDecide(Checkpoint("a", "b"), out string actionId, out string reason),
                Is.False, "絞ったはずの決定点で答えてしまっている");
            Assert.That(actionId, Is.Null);
            Assert.That(oracle.seen, Is.Empty, "辞退するのに worker を起動している");
            Assert.That(sink.GatedOut, Is.EqualTo(1));
        }

        [Test]
        public void AnAllowedDecisionPoint_StillDecides()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("good")] = 0.95;
            oracle.clearRateByAction[ActionFor("bad")] = 0.05;

            var sink = new UltraRolloutSink(
                new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 40 }, Payload, 1UL);
            sink.AllowedPoints.Add(UltraDecisionPoint.MapNavigation);

            Assert.That(sink.TryDecide(Checkpoint("good", "bad"),
                out string actionId, out string reason), Is.True, reason);
            Assert.That(actionId, Is.EqualTo(ActionFor("good")));
            Assert.That(sink.GatedOut, Is.EqualTo(0));
        }

        /// <summary>盤面は決定のたびに作り直されなければならない。 sink の生成時に固定すると、
        /// ラン中の全決定を**同じ古い盤面**から評価することになる。</summary>
        [Test]
        public void ThePayloadIsRebuiltPerDecision_NotFixedAtConstruction()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("a")] = 0.5;
            oracle.clearRateByAction[ActionFor("b")] = 0.5;

            var epochsSeen = new List<int>();
            var sink = new UltraRolloutSink(
                new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 2 },
                (checkpoint, veilSeed) =>
                {
                    epochsSeen.Add(checkpoint.epoch);
                    return Payload(veilSeed);
                },
                1UL);

            sink.TryDecide(Checkpoint("a", "b"), out _, out _);

            Assert.That(epochsSeen, Is.Not.Empty, "payload 生成に checkpoint が渡っていない");
            foreach (int epoch in epochsSeen)
                Assert.That(epoch, Is.EqualTo(3), "その決定の checkpoint が渡っていない");
        }

        /// <summary>決定ごとに未来の列を張り替える。 同じ base のままだと、 ラン中の全決定が
        /// 同じ乱数列を引き直し、 独立した標本のふりをした重複になる。</summary>
        [Test]
        public void DifferentDecisions_DrawDifferentFutures()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("a")] = 0.5;
            oracle.clearRateByAction[ActionFor("b")] = 0.5;

            var sink = new UltraRolloutSink(
                new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 3 }, Payload, 42UL);

            sink.TryDecide(Checkpoint("a", "b"), out _, out _);
            var first = new HashSet<string>();
            foreach (UltraEpisodeRequest request in oracle.seen)
                first.Add(request.scenarioStartSeedHex);

            oracle.seen.Clear();
            // 別の決定 = 別の epoch。
            UltraCheckpoint later = UltraCheckpointProtocol.Create(
                9, UltraDecisionPoint.MapNavigation, Checkpoint("a", "b").observation,
                Checkpoint("a", "b").legalActions);
            sink.TryDecide(later, out _, out _);

            foreach (UltraEpisodeRequest request in oracle.seen)
                Assert.That(first.Contains(request.scenarioStartSeedHex), Is.False,
                    "別の決定が前の決定と同じ未来を引いている");
        }

        // ------------------------------------------------------------------
        //  payload
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        //  batching
        // ------------------------------------------------------------------

        private sealed class BatchOracle : IUltraProductionOracleBatch
        {
            public readonly StubOracle inner = new StubOracle();
            public int batchCalls;
            public int largestBatch;
            public int singleCalls;

            public bool IsHealthy { get { return inner.IsHealthy; } }
            public string BuildFingerprint { get { return inner.BuildFingerprint; } }
            public int MaxParallelEpisodes { get { return 8; } }

            public bool TryRunEpisode(UltraEpisodeRequest request, out UltraEpisodeResult result)
            {
                singleCalls++;
                return inner.TryRunEpisode(request, out result);
            }

            public bool TryRunEpisodes(IList<UltraEpisodeRequest> requests, UltraEpisodeResult[] results)
            {
                batchCalls++;
                largestBatch = Math.Max(largestBatch, requests.Count);
                for (int i = 0; i < requests.Count; i++)
                    results[i] = inner.TryRunEpisode(requests[i], out UltraEpisodeResult one) ? one : null;
                return true;
            }
        }

        /// <summary>Serial dispatch would pay Unity player startup once per rollout. The
        /// evaluator has to hand the whole set over at once for a batching oracle to be able
        /// to overlap them.</summary>
        [Test]
        public void ABatchingOracleReceivesEveryRolloutInOneCall()
        {
            var oracle = new BatchOracle();
            oracle.inner.clearRateByAction[ActionFor("a")] = 0.5;
            oracle.inner.clearRateByAction[ActionFor("b")] = 0.5;

            new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 6 }
                .Evaluate(Checkpoint("a", "b"), Payload, 1UL);

            Assert.That(oracle.batchCalls, Is.EqualTo(1));
            Assert.That(oracle.largestBatch, Is.EqualTo(12), "2 actions × 6 rollouts");
            Assert.That(oracle.singleCalls, Is.EqualTo(0));
        }

        [Test]
        public void ANonBatchingOracleStillWorks()
        {
            var oracle = new StubOracle();
            oracle.clearRateByAction[ActionFor("good")] = 0.95;
            oracle.clearRateByAction[ActionFor("bad")] = 0.05;

            UltraEvaluationResult result = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 40 }
                .Evaluate(Checkpoint("good", "bad"), Payload, 3UL);

            Assert.That(result.usable, Is.True, result.diagnostic);
            Assert.That(result.bestActionId, Is.EqualTo(ActionFor("good")));
        }

        /// <summary>A null entry means "no answer", never "this action lost".</summary>
        [Test]
        public void NullBatchEntriesCountAsFailedRolloutsNotLosses()
        {
            var oracle = new BatchOracle();
            oracle.inner.clearRateByAction[ActionFor("a")] = 1.0;
            oracle.inner.clearRateByAction[ActionFor("b")] = 1.0;
            oracle.inner.failFor.Add(ActionFor("a"));
            oracle.inner.failFor.Add(ActionFor("b"));

            // 手が 1 つの盤面は rollout を撃たないので、 失敗の数え方はここでは測れない。
            UltraEvaluationResult result = new UltraRolloutEvaluator(oracle) { RolloutsPerAction = 4 }
                .Evaluate(Checkpoint("a", "b"), Payload, 1UL);

            Assert.That(result.usable, Is.False);
            Assert.That(result.failedRollouts, Is.EqualTo(8), "2 手 × 4 本");
            Assert.That(result.values[0].clears, Is.EqualTo(0));
            Assert.That(result.values[0].rollouts, Is.EqualTo(0),
                "a failed rollout must not enter the denominator either");
        }

        [Test]
        public void ThePayloadRoundTripsThroughBase64()
        {
            UltraResumePayload sent = Payload(11);
            Assert.That(UltraResumePayload.TryFromBase64(
                sent.ToBase64(), out UltraResumePayload received, out string failure), Is.True, failure);

            Assert.That(received.ContentHash(), Is.EqualTo(sent.ContentHash()));
            Assert.That(received.veilSeed, Is.EqualTo(11));
        }

        /// <summary>A half-empty payload would restore a partial run and report success.</summary>
        [Test]
        public void APayloadMissingEitherHalfIsRefused()
        {
            UltraResumePayload noRun = Payload(1);
            noRun.run = new UltraRunSnapshot();
            Assert.That(UltraResumePayload.TryFromBase64(
                noRun.ToBase64(), out _, out string runFailure), Is.False);
            StringAssert.Contains("run state", runFailure);

            UltraResumePayload noMap = Payload(1);
            noMap.map = null;
            Assert.That(UltraResumePayload.TryFromBase64(
                noMap.ToBase64(), out _, out string mapFailure), Is.False);
            StringAssert.Contains("map", mapFailure);
        }

        [Test]
        public void GarbageIsRefusedRatherThanThrown()
        {
            Assert.That(UltraResumePayload.TryFromBase64("not base64!!", out _, out string a), Is.False);
            Assert.That(a, Is.Not.Empty);
            Assert.That(UltraResumePayload.TryFromBase64("", out _, out string b), Is.False);
            Assert.That(b, Is.Not.Empty);
        }

        [Test]
        public void AWrongPayloadVersionIsRefused()
        {
            UltraResumePayload payload = Payload(1);
            payload.version = UltraResumePayload.CurrentVersion + 1;

            Assert.That(UltraResumePayload.TryFromBase64(payload.ToBase64(), out _, out string failure), Is.False);
            StringAssert.Contains("version", failure);
        }
    }
}
#endif

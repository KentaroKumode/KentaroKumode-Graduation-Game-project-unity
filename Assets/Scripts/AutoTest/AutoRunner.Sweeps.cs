using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using GameLoop;
using CombatSystem;
using MapSystem;
using EventSystem;
using ItemSystem;
using ItemSystem.Shop;
using AutoTest.Ultra;

namespace AutoTest
{
    /// <summary>AutoRunner: スイープ・較正バッチ (Tier 較正 / 固定難易度 / 遺物軸 / 要石 / ランク差 / アブレーション / 技量比較 / 5F ボス)。</summary>
    public partial class AutoRunner
    {
        /// <summary>
        /// Item Tier を挑戦難易度ごとに独立更新する。戦闘方策は Optimal、取得ペルソナは
        /// Standard に固定し、メタ遺物は通常バッチと同じ状態を使う。各スコアは同じ run index
        /// を使うため、masterSeed 指定時は 0/30/50pt を同一シードで比較できる。
        /// </summary>
        private IEnumerator RunTierCalibration()
        {
            var prevPickMode = itemPickMode;
            var prevSkill = wiringSkill;
            bool prevLock = _ascensionPersonaLock;
            var prevChallenge = _pendingChallengeLoadout;

            itemPickMode = ItemPickMode.BuildFocused;
            wiringSkill = WiringSkill.Optimal;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;
            _tierCalibrationLogsWritten = false;
            int runs = Mathf.Max(1, tierCalibrationRuns);

            try
            {
                for (int scoreIndex = 0; scoreIndex < tierCalibrationScores.Length; scoreIndex++)
                {
                    int requestedScore = tierCalibrationScores[scoreIndex];
                    _activeTierCalibrationScore = Mathf.Max(0, requestedScore);
                    var loadout = AscensionLoop.BuildLoadoutAtScore(_activeTierCalibrationScore);
                    int actualScore = MetaProgression.ChallengeResolver.Score(loadout);
                    _activeTierCalibrationScore = actualScore;
                    _pendingChallengeLoadout = loadout;
                    _currentPersona = BuildPersona.Standard;

                    string learningRoot = TierLearningRoot;
                    Directory.CreateDirectory(learningRoot);
                    int batches = tierCalibrationBatchCounts != null
                               && tierCalibrationBatchCounts.Length == tierCalibrationScores.Length
                        ? Mathf.Max(1, tierCalibrationBatchCounts[scoreIndex])
                        : Mathf.Max(1, tierCalibrationBatches);
                    for (int batch = 0; batch < batches; batch++)
                    {
                        _records.Clear();
                        _detail.Clear();
                        // 前セットまでのTierを次セットのStandard判断へ反映する。
                        LearnedPriorityProvider.Reload(learningRoot, writeMarkdown: false);
                        ResetScreenProgress(runs, $"Item Tier {actualScore}pt set {batch + 1}/{batches}");
                        Debug.Log($"[AutoRunner] Item Tier更新 START: {actualScore}pt / Standard / Optimal / "
                                + $"set {batch + 1}/{batches} / {runs}ラン / {learningRoot}");

                        for (int i = 0; i < runs; i++)
                        {
                            curRunInArm = i + 1;
                            int runIndex = batch * runs + i;
                            GameLoop.GameRng.BeginRun(runIndex);
                            if (GameLoop.GameRng.IsSeeded)
                                _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIndex));
                            _currentPersona = BuildPersona.Standard;
                            yield return RunOne(runIndex);
                            if ((i + 1) % Mathf.Max(1, runsPerYield) == 0)
                                yield return null;
                        }

                        _tierCalibrationLastDir = WriteLogs();
                        _tierCalibrationLogsWritten = true;
                        Debug.Log($"[AutoRunner] Item Tier更新 END: {actualScore}pt / set {batch + 1}/{batches} "
                                + $"/ {_records.Count}ラン / {_tierCalibrationLastDir}");
                        yield return null;
                    }
                }
            }
            finally
            {
                _pendingChallengeLoadout = prevChallenge;
                _ascensionPersonaLock = prevLock;
                itemPickMode = prevPickMode;
                wiringSkill = prevSkill;
                _activeTierCalibrationScore = -1;
            }
        }

        private void EmitUltraProductionBatchCompletion(string outputDirectory)
        {
            if (!ultraProductionWorkerMode && UltraProductionBatchCompleted == null) return;

            int expected = ultraProductionExpectedRuns > 0
                ? ultraProductionExpectedRuns
                : _ultraProductionRuns.Count;
            bool countMatches = _ultraProductionRuns.Count == expected;
            var batch = new UltraProductionBatchRecord
            {
                policyId = ultraProductionPolicyId ?? "",
                scenarioSeedVectorHash = ultraProductionScenarioSeedVectorHash ?? "",
                runs = _ultraProductionRuns.ToArray(),
                outputPath = outputDirectory ?? "",
                artifactPath = _lastSummaryPath ?? "",
                artifactSha256 = Sha256File(_lastSummaryPath),
                completedNormally = countMatches,
                failureCode = countMatches ? "" : "RUN_COUNT_MISMATCH",
            };

            try { UltraProductionBatchCompleted?.Invoke(batch); }
            catch (Exception ex)
            {
                batch.completedNormally = false;
                batch.failureCode = "COMPLETION_CALLBACK_FAILED";
                Debug.LogError("[AutoRunner][UltraWorker] batch completion callback failed: " + ex.Message);
            }

            if (ultraProductionWorkerMode && !Application.isEditor)
                Application.Quit(batch.completedNormally ? 0 : 2);
        }

        private IEnumerator RunChallengeFixedSweep()
        {
            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;
            _pendingRelicExplicit = true;
            // 既定は理論最良遺物。 forceNoRelic (⑥) を立てると**遺物なし**で同じ挑戦ptを測る。
            //   遺物の寄与を挑戦ptと分離して読むための対照アーム (2026-08-17)。
            _pendingPresetRelic = forceNoRelic
                ? null : RelicPresets.Build(RelicPresets.Preset.TheoreticalBestCursed);
            // 5層裏ボスは BOT のレイピア運で 5層の難度を二分するため、 挑戦スコアの効きを
            // 測る上では純粋な分散源。 基準値測定と同じく遮断する (2026-08-05)。
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = true;
            bool prevBD = GameLoop.GameManager.GrantBladeDanceOnFloor5Clear;
            GameLoop.GameManager.GrantBladeDanceOnFloor5Clear = challengeSweepGrantBladeDance;

            int runs = Mathf.Max(1, challengeSweepRuns);
            ResetScreenProgress(challengeSweepScores.Length * runs, "固定難易度");
            curArmCount = challengeSweepScores.Length;
            curRunsPerArm = runs;
            string liveProgressPath = Path.Combine(ResolveAutoRunOutputRoot(), "challenge_sweep_progress.txt");
            float sweepStartRt = Time.realtimeSinceStartup;
            var sb = new StringBuilder();
            sb.AppendLine("=== 固定難易度スイープ ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"配線AI  : {wiringSkill}");
            sb.AppendLine($"ペルソナ Standard 固定 / 遺物 "
                        + (forceNoRelic ? "**なし** (対照アーム)"
                           : $"TheoreticalBestCursed ({MetaProgression.Relics.RelicRoller.TotalCap}pt/5枠+刻印) 固定"));
            sb.AppendLine($"1 段階 {runs} ラン / 決定論シード: 0000 0000 0000 0000");
            sb.AppendLine($"※ 全軸最高 Tier = {MetaProgression.ChallengeCatalog.BaseMaxScore}pt "
                        + $"(T4 込みの満点は {MetaProgression.ChallengeCatalog.TotalMaxScore}pt)");
            sb.AppendLine();
            sb.AppendLine("挑戦pt(実) | 平均到達層 | 5層クリア | 7層クリア | 到達層分布 1F〜7F");

            for (int si = 0; si < challengeSweepScores.Length; si++)
            {
                var loadout = AscensionLoop.BuildLoadoutAtScore(challengeSweepScores[si]);
                int actual = MetaProgression.ChallengeResolver.Score(loadout);
                _pendingChallengeLoadout = loadout;
                curArmIndex = si + 1;
                curArmLabel = $"{actual}pt/{wiringSkill}";

                var reach = new int[9];
                int c5 = 0, c7 = 0; double sumFloor = 0;
                for (int i = 0; i < runs; i++)
                {
                    curRunInArm = i + 1;
                    // **BeginRun を必ず呼ぶ。** メインのバッチループと同じ前処理で、
                    //   これが無いと GameRng の counters が clear されず run 塩も derive されない。
                    //   結果、 ラン間で乱数列が引き継がれて挙動が壊れる (2026-08-04 修正)。
                    int runIdx = 10000 + si * runs + i;
                    if (_ultraBatchActive)
                    {
                        bool beganRun;
                        string beginRunReason;
                        try { beganRun = _ultraController.TryBeginRun(runIdx, out beginRunReason); }
                        catch (Exception ex)
                        {
                            beganRun = false;
                            beginRunReason = ex.GetType().Name + ": " + ex.Message;
                        }
                        if (!beganRun)
                        {
                            FailUltraDuringBatch("run " + runIdx + " rejected: " + beginRunReason);
                            RestoreChallengeFixedSweepState(prevSuppress, prevPickMode, prevHidden5, prevBD);
                            yield break;
                        }
                    }
                    int productionOrdinal = si * runs + i;
                    if (ultraProductionWorkerMode)
                    {
                        BeginUltraProductionScenario(productionOrdinal, runIdx);
                    }
                    else
                    {
                        _currentUltraProductionScenarioSeed = 0UL;
                        GameLoop.GameRng.BeginRun(runIdx);
                    }
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    int cf = died ? reached - 1 : reached;
                    reach[Mathf.Clamp(reached, 0, 8)]++; sumFloor += reached;
                    if (cf >= 5) c5++;
                    if (cf >= 7) c7++;
                    if (_ultraBatchActive)
                    {
                        int completedRuns = si * runs + i + 1;
                        string runSummary = "run=" + runIdx
                            + " floor=" + reached
                            + " outcome=" + (rec != null ? rec.outcome.ToString() : "NO_RECORD");
                        UltraProgressHub.ReportRunCompleted(
                            _ultraProgressSession, completedRuns, runSummary);
                        try { _ultraController.CompleteRun(runIdx, runSummary); }
                        catch (Exception ex)
                        {
                            FailUltraDuringBatch("run completion callback failed: "
                                + ex.GetType().Name + ": " + ex.Message);
                            RestoreChallengeFixedSweepState(prevSuppress, prevPickMode, prevHidden5, prevBD);
                            yield break;
                        }
                    }
                    if ((i + 1) == 1 || (i + 1) % 10 == 0 || (i + 1) == runs)
                    {
                        try
                        {
                            float elapsed = Time.realtimeSinceStartup - sweepStartRt;
                            float rate = elapsed > 0f ? (i + 1) / elapsed : 0f;
                            float eta = rate > 0f ? (runs - i - 1) / rate : 0f;
                            File.WriteAllText(liveProgressPath,
                                $"updated={DateTime.Now:yyyy-MM-dd HH:mm:ss}\n"
                              + $"skill={wiringSkill}\nscore={actual}\n"
                              + $"arm={si + 1}/{challengeSweepScores.Length}\n"
                              + $"runs={i + 1}/{runs}\nrecords={_records.Count}\n"
                              + $"elapsedSeconds={elapsed:F1}\netaSeconds={eta:F1}\n"
                              + $"heartbeat={heartbeat}\nphase={lastPhaseSeen}\n"
                              + $"superWiringDecisions={superAI.wiringDecisions}\n"
                              + $"superRerollDecisions={superAI.rerollDecisions}\n"
                              + $"superRolloutEvaluations={superAI.rolloutEvaluations}\n",
                                new UTF8Encoding(false));
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[AutoRunner] 進捗ファイル出力失敗: {ex.Message}");
                        }
                    }
                    // Let OnGUI repaint after every completed run. This also makes a
                    // frozen last-completed line immediately visible during long batches.
                    yield return null;
                }
                var dist = new StringBuilder();
                for (int f = 1; f <= 7; f++) dist.Append($"{reach[f] * 100.0 / runs,5:F1}%");
                sb.AppendLine($"{actual,10} | {sumFloor / runs,10:F2} | {c5 * 100.0 / runs,8:F1}% | "
                            + $"{c7 * 100.0 / runs,8:F1}% |{dist}");
            }
            sb.AppendLine();
            sb.AppendLine("※ 7層クリアが 0% になる地点が『理論上の壁』。 BOT の探索限界とは別物。");

            // ── 診断セクション (軸単独 + 組み合わせ) ──
            //   状態汚染バグ (共有 EnemyData の破壊) を追うために足した足場で、 階段測定の
            //   1.25 倍のラン数を食う。 バグは 2026-08-04 に解消したので **既定 OFF**。
            //   軸ごとの重さを測り直したくなったら true に戻す。
            if (!challengeSweepDiagnostics)
            {
                _challengeSweepReport = sb.ToString();
                // **末尾の後始末をここでも行う。** yield break で抜けると下の復元に到達せず、
                // SuppressRelicGrant / itemPickMode / 裏ボス遮断が立ちっぱなしで
                // 後続のバッチへ漏れる (2026-08-05 修正)。
                RestoreChallengeFixedSweepState(prevSuppress, prevPickMode, prevHidden5, prevBD);
                yield break;
            }

            // ── 軸ごとの単独試験 (plan §6: 特定 1 軸だけでクリアが消滅しないこと) ──
            //   累積スイープだけでは「どの軸が壊しているか」が分からない。 1 軸ずつ T3 で回す。
            int isoRuns = Mathf.Max(50, runs / 2);
            sb.AppendLine();
            sb.AppendLine($"=== 軸ごとの単独試験 (その軸だけ T3 = 3pt / 各 {isoRuns} ラン) ===");
            sb.AppendLine("軸                 | 平均到達層 | 5層クリア | 1F終了");
            for (int ai = 0; ai < MetaProgression.ChallengeCatalog.Axes.Count; ai++)
            {
                var def = MetaProgression.ChallengeCatalog.Axes[ai];
                var lo = new MetaProgression.ChallengeLoadout();
                lo.SetTier(def.axis, 3);
                _pendingChallengeLoadout = lo;

                int c5 = 0, died1F = 0; double sumFloor = 0;
                for (int i = 0; i < isoRuns; i++)
                {
                    int runIdx = 90000 + ai * isoRuns + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    sumFloor += reached;
                    if (reached <= 1) died1F++;
                    if ((died ? reached - 1 : reached) >= 5) c5++;
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                sb.AppendLine($"{def.displayName,-18} | {sumFloor / isoRuns,10:F2} | "
                            + $"{c5 * 100.0 / isoRuns,8:F1}% | {died1F * 100.0 / isoRuns,5:F1}%");
            }
            sb.AppendLine("※ 0pt の 5層クリア率と比べて突出して低い軸が犯人。");

            // ── 組み合わせ試験 ──
            //   「軸単独は健全なのに累積スイープでは崩壊する」現象が、
            //   **組み合わせ固有**なのか **スイープ位置依存 (状態汚染)** なのかを切り分ける。
            //   単独試験と同じ index 帯・同じ手順で、 手組みの組み合わせを回す。
            var combos = new List<(string name, MetaProgression.ChallengeAxis[] axes, int tier)>
            {
                ("空 (対照群 0pt)",       new MetaProgression.ChallengeAxis[0], 0),
                ("A負傷 単独T1",          new[]{ MetaProgression.ChallengeAxis.長引く負傷 }, 1),
                ("C不器用 単独T1",        new[]{ MetaProgression.ChallengeAxis.綻び }, 1),
                ("E絶望 単独T1",          new[]{ MetaProgression.ChallengeAxis.絶望的な戦闘 }, 1),
                ("5軸すべてT1",           new[]{ MetaProgression.ChallengeAxis.長引く負傷, MetaProgression.ChallengeAxis.厚い皮膚,
                                                 MetaProgression.ChallengeAxis.綻び, MetaProgression.ChallengeAxis.搾取経済,
                                                 MetaProgression.ChallengeAxis.絶望的な戦闘 }, 1),
            };
            sb.AppendLine();
            sb.AppendLine($"=== 組み合わせ試験 (単独試験と同じ index 帯 / 各 {isoRuns} ラン) ===");
            sb.AppendLine("構成                 | 平均到達層 | 5層クリア | 1F終了");
            for (int ci = 0; ci < combos.Count; ci++)
            {
                var lo = new MetaProgression.ChallengeLoadout();
                foreach (var ax in combos[ci].axes) lo.SetTier(ax, combos[ci].tier);
                _pendingChallengeLoadout = lo;

                int c5 = 0, died1F = 0; double sumFloor = 0;
                for (int i = 0; i < isoRuns; i++)
                {
                    int runIdx = 95000 + ci * isoRuns + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    sumFloor += reached;
                    if (reached <= 1) died1F++;
                    if ((died ? reached - 1 : reached) >= 5) c5++;
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                sb.AppendLine($"{combos[ci].name,-20} | {sumFloor / isoRuns,10:F2} | "
                            + $"{c5 * 100.0 / isoRuns,8:F1}% | {died1F * 100.0 / isoRuns,5:F1}%");
            }
            sb.AppendLine("※「空」が 0pt 相当にならないならスイープ側の状態汚染。");
            sb.AppendLine("※「5軸すべてT1」だけが崩れるなら組み合わせ固有の相互作用。");

            _challengeSweepReport = sb.ToString();

            _pendingChallengeLoadout = null;
            _pendingPresetRelic = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;
            GameLoop.GameManager.GrantBladeDanceOnFloor5Clear = prevBD;
        }

        private void RestoreChallengeFixedSweepState(
            bool previousRelicSuppression,
            ItemPickMode previousPickMode,
            bool previousHiddenBossSuppression,
            bool previousBladeDanceGrant)
        {
            _pendingChallengeLoadout = null;
            _pendingPresetRelic = null;
            _ascensionPersonaLock = false;
            itemPickMode = previousPickMode;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = previousRelicSuppression;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = previousHiddenBossSuppression;
            GameLoop.GameManager.GrantBladeDanceOnFloor5Clear = previousBladeDanceGrant;
        }

        private void CompleteUltraBatchIfActive(string summary)
        {
            if (!_ultraBatchActive) return;
            UltraProgressHub.Complete(_ultraProgressSession, summary);
            try { _ultraController?.CompleteBatch(summary); }
            catch (Exception ex)
            {
                Debug.LogError("[AutoRunner][Ultra] completion callback failed: " + ex.Message);
            }
            try { _ultraController?.Dispose(); } catch { }
            _ultraController = null;
            _ultraBatchActive = false;
        }

        private void FailUltraDuringBatch(string reason)
        {
            string detail = string.IsNullOrEmpty(reason) ? "unknown Ultra batch failure" : reason;
            UltraProgressHub.Fail(_ultraProgressSession, detail);
            try { _ultraController?.Cancel(detail); } catch { }
            try { _ultraController?.Dispose(); } catch { }
            _ultraController = null;
            _ultraBatchActive = false;
            Debug.LogError("[AutoRunner][Ultra] BATCH ABORTED: " + detail);
        }

        /// <summary>遺物 単軸スイープ。 **軸 1 本だけを載せて、その軸の寄与を切り出す。**
        ///
        /// プリセットスイープは「遺物の総量」を振るので、 個々の軸が単位 (期待与ダメ 6%) に
        /// 揃っているかは分からない。 ここでは **メイン枠 1 本のみ・段N** の遺物を全軸ぶん作り、
        /// 遺物なし (None) を基準に差分を見る。 軸間の強弱が直接読める。
        ///
        /// **1〜3F の与ダメ/被ダメも採る。** 到達層だけだと、 攻撃系と防御系が同じ深度に
        /// 落ち着いたときに「両方 6 単位」なのか「両方測れていない」のかを区別できない。
        ///
        /// 高難易度限定軸 (渇き/刻限/背水) は条件付きなので、 挑戦 0 では条件が
        /// 揃わず低く出る可能性がある ── その場合は軸が弱いのではなく **BOT が条件を
        /// 踏みに行かない**（回復目標が高く低HP/低物資に留まらない）ことを疑うこと。</summary>
        private IEnumerator RunRelicAxisSweep()
        {
            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;
            // 裏ボスは遮断する。 レイピアを引いたランだけ 5層の難度が跳ね上がり、
            // 軸間の差より大きい分散源になるため。
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = true;

            int runs = Mathf.Max(50, relicAxisSweepRuns);
            int step = Mathf.Clamp(relicAxisSweepStep, 1, MetaProgression.Relics.RelicAxisCatalog.CursedStep);
            var axes = MetaProgression.Relics.RelicAxisCatalog.All;

            var loadout = AscensionLoop.BuildLoadoutAtScore(relicAxisSweepChallengeScore);
            int actualScore = MetaProgression.ChallengeResolver.Score(loadout);

            var sb = new StringBuilder();
            sb.AppendLine("=== 遺物 単軸スイープ (§15-5) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ペルソナ Standard 固定 / 挑戦スコア {actualScore} 固定 / 5層裏ボス遮断");
            sb.AppendLine($"遺物は **メイン枠 = 対象軸@段{step} + 全アーム共通の埋め草サブ 2 枠** / 1 軸 {runs} ラン");
            sb.AppendLine($"ペア比較の基準は [基準]攻撃3 (メインだけ段3 へ落とした同一構成)。 遺物なしは文脈用。");
            sb.AppendLine("決定論シード: 0000 0000 0000 0000");
            sb.AppendLine();
            sb.AppendLine("軸            段の効果                              | 平均到達層 | 5層クリア | 7層クリア | 1攻撃与ダメ | 被ダメ/戦 | 5F改善/悪化 (McNemar p) | 7F改善/悪化 (McNemar p)");

            // ペア比較の基準の 1 ラン単位の結果。 **同一シードのペア比較**に使う。
            //   平均どうしの比較は使わない ── 500 ランでも 5層クリアの標準誤差が ±2.2pt あり、
            //   軸 1 本の寄与 (数pt) が埋もれるため (§13-5 / paircmp と同じ方針)。
            bool[] baseC5 = new bool[runs];
            // **7層クリアでも取る。** 5層クリア後にしか効かない軸は 5F 指標では「効果ゼロ」と測定される
            //   (2026-08-08 に旧〈Λ共鳴〉で実際に誤読した。 Λ共鳴は 2026-10-28 に引退)。
            bool[] baseC7 = new bool[runs];
            // 追加区分 (会心群/渇き群) はビルドごと条件を変えるので、 **その区分の基準**と対にする。
            //   全体基準と比べると「軸の寄与」と「ビルドを変えた効果」が混ざるため。
            bool[] grpC5 = new bool[runs], grpC7 = new bool[runs];

            // ── 追加区分: **その軸を主軸に据えたビルドで測り直す** ──
            //   軸によっては「噛み合わせて初めて働く」ので、 Standard/Balanced では実力が出ない。
            //   ただし条件を変えたら**基準も同じ条件で取り直す**こと ── そうしないと
            //   「軸の寄与」と「ビルドを変えた効果」が混ざる。 区分ごとに基準を先頭へ置く。
            //     会心群: ペルソナ Crit (メタ側の会心軸は 2026-09-10 に撤去)
            //     渇き群: provisionRefillFloor = 0 ＝ **物資を買い戻さない** = 低物資を維持する命令
            int extraCrit = 3;   // [会心基準]攻撃3 / 会心率 / 会心倍率
            int extraProvision = 2;   // [渇き基準]攻撃3 / 渇き
            int total = axes.Length + extraCrit + extraProvision;

            // 診断モード: [基準]攻撃3 と指定軸の 2 アームだけ。 追加区分は回さない。
            bool diagOne = relicAxisSweepOnlyAxis >= 0 && relicAxisSweepOnlyAxis < axes.Length;
            if (diagOne) { extraCrit = 0; extraProvision = 0; total = axes.Length; }
            ResetScreenProgress((diagOne ? 3 : total + 2) * runs, "遺物単軸");

            var prevPolicy = PolicyParameters.Current;
            var prevMetaAxis = metaBuildAxis;

            // ai == -2: 遺物なし (文脈用・ペア比較の基準にはしない)
            // ai == -1: **ペア比較の基準** = メイン 攻撃@段3 + 全アーム共通の埋め草サブ 2 枠
            //           枠構成を揃えないと「軸の差」と「埋め草の有無」が混ざる。
            //           段3 はメインの構造下限なので、 ここからの差分が軸の寄与になる。
            // ai >= 0 : メインを対象軸@段N へ差し替え (末尾 5 本は上記の追加区分)
            int armOrdinal = 0;
            for (int ai = -2; ai < total; ai++)
            {
                // ── この区分の測定条件を決める ──
                var armPersona = BuildPersona.Standard;
                var armMeta    = prevMetaAxis;
                float armProvisionRefill = -1f;                 // <0 = 既定のまま
                var armAxis = MetaProgression.Relics.RelicAxis.Attack;
                int  armStep = MetaProgression.Relics.RelicAxisCatalog.MainStepMin;
                string groupTag = "";
                float armProvisionCeil = -1f;                   // <0 = 既定 (上限まで満たす)

                // 診断モードでは指定軸以外の本体アームを飛ばす (基準 ai==-1 は残す)。
                if (diagOne && ai >= 0 && ai != relicAxisSweepOnlyAxis) continue;
                curArmIndex = ++armOrdinal;
                curArmCount = diagOne ? 3 : total + 2;
                curRunsPerArm = runs;

                int ci = ai - axes.Length;                  // 追加区分のインデックス
                if (ai >= 0 && ci < 0) { armAxis = axes[ai]; armStep = step; }
                else if (ci >= 0 && ci < extraCrit)
                {
                    armPersona = BuildPersona.Crit;
                    // 2026-09-10: 精密トラック撤去に伴い PrecisionApex を廃止。 会心群は
                    //   ペルソナ Crit + 標準配分 (Balanced) で測る ── メタ側に会心軸が無くなった。
                    armMeta = MetaAllocationPresets.Preset.Balanced;
                    groupTag = "会心";
                    if (ci == 1) { armAxis = MetaProgression.Relics.RelicAxis.CritRatePct; armStep = step; }
                    if (ci == 2) { armAxis = MetaProgression.Relics.RelicAxis.CritMultPct; armStep = step; }
                }
                else if (ci >= extraCrit)
                {
                    // **帯を維持する**。 補充を止める (refill=0) と物資が 0 まで落ちて発狂で
                    //   ランが終わり、 上限まで満たすと〈渇き〉の発動条件から外れる。
                    //   実測ではどちらも軸が死んだので、 発動閾値 (provisionCap×40%) の少し下を
                    //   天井に、 発狂を避けられる高さを床に置いて **帯 [18, 38] を維持**する。
                    armProvisionRefill = 180f;   // 2026-09-28: 物資 ×10
                    armProvisionCeil   = 0.38f;
                    groupTag = "渇き";
                    if (ci == extraCrit + 1) { armAxis = MetaProgression.Relics.RelicAxis.ProvisionBurn; armStep = step; }
                }

                metaBuildAxis = armMeta;
                var pol = prevPolicy.Clone();
                if (armProvisionRefill >= 0f) pol.provisionRefillFloor = armProvisionRefill;
                if (armProvisionCeil   >= 0f) pol.provisionBandCeilRatio = armProvisionCeil;
                PolicyParameters.SetCurrent(pol);

                bool isGroupBase = ci >= 0 && (ci == 0 || ci == extraCrit);

                var relic = ai == -2 ? null
                          : RelicPresets.BuildSingleAxis(armAxis, armStep);
                string label = ai == -2 ? "(遺物なし)"
                             : ai == -1 ? "[基準]攻撃3"
                             : isGroupBase ? $"[{groupTag}基準]攻撃3"
                             : (groupTag != "" ? groupTag + ":" : "")
                               + MetaProgression.Relics.RelicAxisCatalog.NameOf(armAxis);
                curArmLabel = label;
                string effect = ai == -2 ? "—"
                              : (ai == -1 || isGroupBase) ? "攻撃+3 (+埋め草サブ2枠)"
                              : MetaProgression.Relics.RelicAxisCatalog.Describe(armAxis, armStep);
                // **装備できているかを毎行に出す。** 枠数 1 の遺物は IsValid() で弾かれ、
                //   EquippedRelic が null を返して効果が一切乗らない ── これを黙って通すと
                //   「全軸が横並び」という読める形の嘘になる (2026-08-08 に実際に起きた)。
                if (relic != null && !relic.IsValid()) effect = "!!装備不可!! " + effect;

                var reach = new int[9];
                int c5 = 0, c7 = 0; double sumFloor = 0;
                int recBase = _records.Count;
                var thisC5 = new bool[runs];
                var thisC7 = new bool[runs];
                CombatSystem.CombatManager.ResetFloorDamage();
                MetaProgression.Relics.RelicApplicator.ResetProvisionBurnStats();

                for (int i = 0; i < runs; i++)
                {
                    // **全軸で同じ runIdx を使う ＝ ペア比較にする。** (ai を混ぜない)
                    //   軸ごとに別シード帯を割ると軸間の差にシード分散が丸ごと乗り、
                    //   300 ラン (5層クリアの標準誤差 ±2.9pt) では軸の寄与が完全に埋もれる。
                    //   実測: 軸ごとに別帯にした版では 攻撃+9 が与ダメを下げ、
                    //   攻撃に無関係な 被ダメージ−45% が与ダメを上げる、という構造上ありえない
                    //   並びが出た (2026-08-08)。 同一シードなら差は軸だけに帰属する。
                    //
                    // **BeginRun を必ず呼ぶ。** 抜けるとラン間で乱数列が引き継がれる (2026-08-04)。
                    int runIdx = 40000 + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = armPersona;
                    _pendingChallengeLoadout = loadout;
                    _pendingPresetRelic = relic;
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    int cf = died ? reached - 1 : reached;
                    reach[Mathf.Clamp(reached, 0, 8)]++; sumFloor += reached;
                    if (cf >= 5) { c5++; thisC5[i] = true; }
                    if (cf >= 7) { c7++; thisC7[i] = true; }
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                if (ai == -1)
                {
                    System.Array.Copy(thisC5, baseC5, runs);
                    System.Array.Copy(thisC7, baseC7, runs);
                }
                if (isGroupBase)
                {
                    System.Array.Copy(thisC5, grpC5, runs);
                    System.Array.Copy(thisC7, grpC7, runs);
                }
                // 追加区分の軸は自分の区分基準と、 それ以外は全体基準と対にする。
                bool inGroup = ci >= 0;
                var refC5 = inGroup ? grpC5 : baseC5;
                var refC7 = inGroup ? grpC7 : baseC7;

                // ── ペア比較 (McNemar)。 同一シードなので「同じランがどう転んだか」で見る ──
                //   b = 軸ありだけ 5層クリア / c = 基準だけ 5層クリア。
                //   一致したランは検定に寄与しない ＝ 分散が落ちて数pt の差が読める。
                int b = 0, cc = 0, b7 = 0, c7b = 0;
                for (int i = 0; i < runs; i++)
                {
                    if (thisC5[i] && !refC5[i]) b++;
                    else if (!thisC5[i] && refC5[i]) cc++;
                    if (thisC7[i] && !refC7[i]) b7++;
                    else if (!thisC7[i] && refC7[i]) c7b++;
                }
                bool isBaseRow = ai < 0 || isGroupBase;
                string mc  = isBaseRow ? "—" : $"{b,3}/{cc,3} (p={McNemarP(b, cc),6:F3})";
                string mc7 = isBaseRow ? "—" : $"{b7,3}/{c7b,3} (p={McNemarP(b7, c7b),6:F3})";

                // 1〜3F の通常+エリート戦だけ (ボスは長さの性質が違うので除く)。
                //   FloorDamage の第3添字は 0=攻撃回数 / 1=与ダメ合計 / 2=最小 / 3=最大 /
                //   4=敵maxHP合計 / 5=ターン合計 / 6=戦闘数。 **被ダメは入っていない**ので
                //   そちらは _records の戦闘明細から拾う。
                var fd = CombatSystem.CombatManager.FloorDamage;
                double atk = 0, dmgOut = 0;
                for (int f = 1; f <= 3; f++)
                    for (int k = 0; k < 2; k++)
                    { atk += fd[f, k, 0]; dmgOut += fd[f, k, 1]; }

                double dmgIn = 0; int fights = 0;
                for (int r = recBase; r < _records.Count; r++)
                {
                    var cl = _records[r].combats;
                    if (cl == null) continue;
                    for (int c = 0; c < cl.Count; c++)
                        if (!cl[c].isBoss && cl[c].floor >= 1 && cl[c].floor <= 3)
                        { dmgIn += cl[c].damageTaken; fights++; }
                }

                sb.AppendLine($"{label,-12} {Trunc(effect, 36),-36} | {sumFloor / runs,10:F2} | "
                            + $"{c5 * 100.0 / runs,8:F1}% | {c7 * 100.0 / runs,8:F1}% | "
                            + $"{(atk > 0 ? dmgOut / atk : 0),11:F1} | {(fights > 0 ? dmgIn / fights : 0),9:F1} | {mc} | {mc7}");
                // 〈渇き〉のアームだけ、 発動率を追記する。 「弱い」と「発動していない」を分けるため。
                if (armAxis == MetaProgression.Relics.RelicAxis.ProvisionBurn)
                    sb.AppendLine($"             └ {MetaProgression.Relics.RelicApplicator.DescribeProvisionBurnStats()}");
            }

            sb.AppendLine();
            sb.AppendLine("※ 基準は 1 行目 (遺物なし)。 単位の定義は『参照状態での期待与ダメ 6% = 攻撃+1』(§15-5)。");
            sb.AppendLine("※ **全軸が同一シード**。 判断は『5F改善/悪化』の McNemar p 値で行い、");
            sb.AppendLine("   平均どうしの比較はしない (300ランの標準誤差 ±2.9pt に軸の寄与が埋もれるため)。");
            sb.AppendLine("※ 末尾 5 行は測定条件を変えた区分。 会心群=ペルソナCrit /");
            sb.AppendLine("   渇き群=物資を帯 [18, provisionCap×38%] に維持 (発動閾値 40% の下に留めつつ発狂を避ける)。");
            sb.AppendLine("   **各区分の先頭が対の基準**。");
            _relicAxisSweepReport = sb.ToString();

            _pendingChallengeLoadout = null;
            _pendingPresetRelic = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            metaBuildAxis = prevMetaAxis;
            PolicyParameters.SetCurrent(prevPolicy);
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
        }

        // ============================================================
        //  ランダム付与試行 (2026-09-08)
        // ============================================================

        /// <summary>アイテム アブレーション。 **同一シードで「開幕付与あり / なし」を対で走らせる。**
        ///
        /// <para>出すのは効果量ではなく <b>対の差の SD</b> ── これが全品アブレーションの
        /// 実現可能性を決める。 差の SD を s とすると、 真の効果 0.15 band を SE 0.05 で
        /// 見るのに必要な対の数は <c>(s/0.05)²</c>。 band の素の SD 2.83 のままなら
        /// 3,200 対/品 × 163 品 = 52 万ラン、 s が 1.0 まで落ちれば 400 対/品 = 6.5 万ラン。
        /// <b>桁が変わる</b>ので、 先にここを測る。</para>
        ///
        /// <para>先頭 2 アームは <b>A/A (両方とも付与なし)</b>。 ここで 1 ランでも差が出たら
        /// シミュレーションが決定的でないということで、 ペアリングの前提が崩れる ──
        /// その場合は下の SD を読む意味がないので、 まず決定性を直すこと。</para></summary>
        /// <summary><b>極点 (r10) 同士の比較 (2026-09-10)。</b> 数値系 8 トラックを 1 本ずつ
        /// r10 まで振り切り、 残り 23pt を共通の順序で埋めて比べる。
        ///
        /// <para><b>なぜ要るか。</b> r10 の極点 (ラストスタンド / 宝箱ゴールド / 強盗解禁 …) は
        /// 「1 トラックを振り切る理由」として 2026-07-28〜08-04 に意図的に置かれたのに、
        /// <b>一度も測られていない</b>。 drop-one は全アームが r6 以下だったので極点に触れていない。
        /// 「MAX を 9 段階へ」の可否も、 特化が報われるかどうかも、 ここが分からないと決められない。</para>
        ///
        /// <para><b>埋め方を固定する。</b> 各アームは [焦点 r10] + [共通の順序で 23pt]。
        /// 焦点になったトラックは埋め草から飛ばす。 埋め草の中身がアーム間で少し違うのは
        /// 避けられないが、 <b>どのアームも「1 本振り切って残りを薄く」</b>という同じ形になる。
        /// 比較対象は Balanced (どの極点にも届かない標準配分)。</para></summary>
        private IEnumerator RunKeystoneSweep()
        {
            int runs = Mathf.Max(200, metaAblationRuns);
            var K = typeof(MetaProgression.MetaPanelKind);
            // 数値系のみ (max 10)。 精密は撤去済みなので MaxRank 0 で自動的に外れる。
            var focus = new List<MetaProgression.MetaPanelKind>();
            foreach (MetaProgression.MetaPanelKind k in Enum.GetValues(K))
                if (MetaProgression.MetaPanelKindExt.MaxRank(k) == 10) focus.Add(k);

            // **土台は「焦点 10pt + 他 7 本を 2pt ずつ」(2026-09-12・予算 24pt)。**
            //   Balanced (数値系 8 本 × 3pt) と合計が一致し、 極点の代償が
            //   「全軸を 1 段ずつ削る」という均一な形になる。 詳細は RunRankMarginSweep。
            var arms = new List<(string label, Dictionary<MetaProgression.MetaPanelKind, int> spec)>();
            arms.Add(("Balanced (極点なし)", MetaAllocationPresets.Ranks(MetaAllocationPresets.Preset.Balanced)));
            foreach (var f in focus)
            {
                var d = new Dictionary<MetaProgression.MetaPanelKind, int> { { f, 10 } };
                int left = MetaProgression.MetaPanel.MaxPoints - 10;
                foreach (var k in focus)
                {
                    if (k == f || left <= 0) continue;
                    int r = Math.Min(2, left);
                    d[k] = r; left -= r;
                }
                arms.Add(($"{f} r10", d));
            }

            GameManager.PlunderDrops = 0;
            ResetScreenProgress(arms.Count * runs, "極点比較");
            var sb = new StringBuilder();
            sb.AppendLine("=== 極点 (r10) 比較 ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss} / 1 アーム {runs} ラン / シードは全アーム共通");
            sb.AppendLine($"各アーム = [焦点トラック r10 = 10pt] + [共通順序で残り {MetaProgression.MetaPanel.MaxPoints - 10}pt]");
            sb.AppendLine("比較対象 = Balanced (どの極点にも届かない標準配分)");
            sb.AppendLine();
            sb.AppendLine("アーム                  |  クリア率 |  Balanced比 | 平均到達段 | 極点の内容");

            var note = new Dictionary<string, string>
            {
                { "Shell",   "ラストスタンド解禁 + HP+3" },
                { "Output",  $"〈戦意〉勝利ごとに与ダメ +{MetaProgression.MetaPanel.BattleSpiritPctPerWin}% (恒久)" },
                { "Guard",   "シールドの戦闘間持ち越し" },
                { "Vault",   "宝箱ゴールド復活" },
                { "Plunder", "ショップ強盗 解禁" },
                { "Trade",   "99%引きの特売枠が出る" },
                { "Supply",  "開幕パッシブが 2択" },
                { "Lantern", "横移動の物資消費 5→2" },
            };

            double baseClear = 0;
            for (int ai = 0; ai < arms.Count; ai++)
            {
                var (label, spec) = arms[ai];
                curArmLabel = label; curArmIndex = ai + 1; curArmCount = arms.Count; curRunsPerArm = runs;
                _metaAblationSpec = spec;

                int clears = 0, valid = 0; double stageSum = 0;
                for (int i = 0; i < runs; i++)
                {
                    int runIdx = 60000 + i;   // 全アーム共通シード
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    if (rec != null && rec.bandScore >= 0)
                    { valid++; stageSum += rec.bandScore; if (rec.bandScore >= 11) clears++; }
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                double clear = valid > 0 ? clears / (double)valid * 100.0 : 0;
                double stage = valid > 0 ? stageSum / valid : 0;
                if (ai == 0) baseClear = clear;
                string key = label.Replace(" r10", "");
                sb.AppendLine($"{label,-22} | {clear,8:F2}% | {(ai == 0 ? 0 : clear - baseClear),+10:F2}pt | {stage,9:F2} | "
                            + (note.TryGetValue(key, out string n) ? n : (ai == 0 ? "── 基準" : "")));
            }
            _metaAblationSpec = null;
            sb.AppendLine();
            sb.AppendLine($"※ 1 アーム {runs} ラン。 クリア率 {baseClear:F0}% 帯の MDE は概ね "
                        + $"{2.8 * Math.Sqrt(2 * baseClear * (100 - baseClear) / runs):F2}pt。 これ未満の差は読まないこと。");
            sb.AppendLine($"※ 強奪の道中パッシブドロップ (全アーム累積): {GameManager.PlunderDrops:N0} 件");
            {
                long ra = ItemSystem.Shop.ShopManager.RobberyAttempts;
                long rw = GameManager.RobberyWins, rl = GameManager.RobberyLosses;
                sb.AppendLine($"※ 強盗の罰: {(robberyBlocksShops ? "出禁 (旧仕様・切り分け用)" : "以降のショップ価格 ×2")}"
                            + $" / 発動 {ra:N0} 回 (勝 {rw:N0} 敗 {rl:N0}"
                            + $" 平均戦利品 {(rw > 0 ? GameManager.RobberyLootTotal / (double)rw : 0):F1} 件)");
                sb.AppendLine("   ↑ 発動 0 なら 商才 r10 の差は強盗以外 (割引下限40%) の取り分。 先にここを読むこと。");
            }
            sb.AppendLine("※ Balanced 比が正 = 「1 本振り切る」ほうが「薄く広く」より良い ＝ 極点に価値がある。");
            sb.AppendLine("※ 全アームが負なら特化そのものが損 ＝ 極点は現状機能していない。");
            _metaAblationReport = sb.ToString();
            Debug.Log("[AutoRunner] 極点比較 完了\n" + _metaAblationReport);
        }

        /// <summary><b>スループット計測 (2026-09-13)。</b> <see cref="runsPerYield"/> だけを振って
        /// ラン/秒 を実測する。 <see cref="stepsPerYield"/> は固定 (ラン内の yield を条件から外すため)。
        ///
        /// <para>同じシード列を毎条件で使い回すので、 <b>条件間で仕事量は完全に同じ</b>。
        /// 差が出たらそれは待ち時間の差。 クリア率も併記して、 条件で結果が変わっていないことを確かめる
        /// (変わっていたら計測が壊れている)。</para></summary>
        private IEnumerator RunYieldBenchmark()
        {
            int runs = Mathf.Max(50, yieldBenchRuns);
            int[] cadences = { 1, 5, 25, 100 };
            int savedRpy = runsPerYield, savedSpy = stepsPerYield;
            stepsPerYield = 1000;   // ラン内 yield を実質無効化して条件から外す

            var sb = new StringBuilder();
            sb.AppendLine("=== スループット計測 (runsPerYield) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss} / 1 条件 {runs} ラン / stepsPerYield={stepsPerYield} 固定");
            sb.AppendLine("同一シード列を毎条件で再利用 ── 仕事量は同じなので、 差は待ち時間の差");
            sb.AppendLine();
            sb.AppendLine("runsPerYield |    秒 | ラン/秒 | 相対 | クリア率");

            double baseRate = 0;
            foreach (int c in cadences)
            {
                runsPerYield = c;
                curArmLabel = $"rpy={c}"; curArmIndex = Array.IndexOf(cadences, c) + 1;
                curArmCount = cadences.Length; curRunsPerArm = runs;

                int clears = 0, valid = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < runs; i++)
                {
                    int runIdx = 70000 + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    if (rec != null && rec.bandScore >= 0) { valid++; if (rec.bandScore >= 11) clears++; }
                    if ((i + 1) % c == 0) yield return null;
                }
                sw.Stop();
                double sec = sw.Elapsed.TotalSeconds;
                double rate = sec > 0 ? runs / sec : 0;
                if (baseRate <= 0) baseRate = rate;
                double clear = valid > 0 ? clears / (double)valid * 100.0 : 0;
                sb.AppendLine($"{c,12} | {sec,5:F1} | {rate,7:F1} | {rate / baseRate,4:F2}x | {clear,7:F2}%");
            }

            runsPerYield = savedRpy; stepsPerYield = savedSpy;
            sb.AppendLine();
            sb.AppendLine("※ 相対が 1.00 付近で頭打ち = 計算律速。 刻みを増やしても速くならない。");
            sb.AppendLine("※ 大きく伸びる = フレーム待ち律速。 スイープの runsPerYield=1 を上げるべき。");
            sb.AppendLine("※ クリア率が条件間でズレたら計測が壊れている (同じシード・同じ仕事のはず)。");
            _metaAblationReport = sb.ToString();
            Debug.Log("[AutoRunner] スループット計測 完了\n" + _metaAblationReport);
        }

        /// <summary><b>r9 vs r10 の限界比較 (2026-09-12)。</b> 極点比較は
        /// 「そのトラックに 10pt 使うこと」と「極点そのもの」を足し合わせた数字しか出さない。
        /// 同じトラックの r9 と r10 を並べると、 差が<b>極点 1 個ぶんの取り分</b>になる。
        ///
        /// <para><b>完全な分離ではない。</b> r10 は数値効果も 1 段ぶん増えるし、
        /// r9 で浮いた 1pt は埋め草へ回る。 測っているのは
        /// 「10 段目に 1pt 足すか、 埋め草に 1pt 足すか」という<b>実際の選択</b>なので、
        /// 配点を決めるにはこの形が正しい。</para>
        ///
        /// <para>各トラック 2 アーム。 Balanced は置かない ── 比較はトラック内で閉じており、
        /// 基準アームを足すと 1 本ぶん余計に走る (2026-09-12 の方針: 判断に要らないアームは立てない)。</para></summary>
        private IEnumerator RunRankMarginSweep()
        {
            int runs = Mathf.Max(200, metaAblationRuns);
            // **土台は「焦点 10pt + 他 7 本を 2pt ずつ」で固定 (2026-09-12・予算 24pt)。**
            //
            //   Balanced (数値系 8 本 × 3pt) と合計が一致し、 極点を取る代償が
            //   <b>全軸を 1 段ずつ削る</b>という均一な形になる。 どのアームも
            //   「1 本が 10 / 7 本が 2」で形が同じなので、 違うのは**どのラベルが 10 か**だけ。
            //
            //   旧版は Balanced を土台に流用していたため、 Balanced が 0 に置いていた
            //   強奪/商才だけが 10pt 全額を新規に払い、 **出力を 4 段まるごと失っていた**。
            //   犠牲の不均一がトラックの弱さと混ざって読めなかった。
            var tracks = new List<MetaProgression.MetaPanelKind>();
            string spec0 = (rankMarginTracks ?? "").Trim();
            if (spec0.Length == 0 || spec0 == "*")
            {
                foreach (MetaProgression.MetaPanelKind k in Enum.GetValues(typeof(MetaProgression.MetaPanelKind)))
                    if (MetaProgression.MetaPanelKindExt.MaxRank(k) == 10) tracks.Add(k);
            }
            else foreach (var tok in spec0.Split(','))
            {
                if (!Enum.TryParse(tok.Trim(), true, out MetaProgression.MetaPanelKind k)) continue;
                if (MetaProgression.MetaPanelKindExt.MaxRank(k) != 10)
                { Debug.LogWarning($"[AutoRunner] r9/r10 比較: {k} は 10 段トラックではない。 飛ばす"); continue; }
                tracks.Add(k);
            }
            if (tracks.Count == 0) { Debug.LogError("[AutoRunner] r9/r10 比較: 対象トラックが空"); yield break; }

            var ranks = new List<int>();
            foreach (var tok in (rankMarginRanks ?? "9,10").Split(','))
                if (int.TryParse(tok.Trim(), out int r) && r >= 1 && r <= 10) ranks.Add(r);
            if (ranks.Count == 0) ranks.Add(9);

            var arms = new List<(string label, Dictionary<MetaProgression.MetaPanelKind, int> spec)>();
            // **Balanced を同じバッチに入れる。** 判定基準が Balanced 比で書かれているので、
            //   別バッチの古い値と突き合わせると今日のような「基準が陳腐化していた」事故になる。
            arms.Add(("Balanced", MetaAllocationPresets.Ranks(MetaAllocationPresets.Preset.Balanced)));
            foreach (var t in tracks)
                foreach (int r in ranks) arms.Add(($"{t} r{r}", BuildRankMarginArmSpec(t, r)));

            GameManager.PlunderDrops = 0;
            ItemSystem.Shop.ShopManager.RobberyAttempts = 0;
            GameManager.RobberyWins = GameManager.RobberyLosses = GameManager.RobberyLootTotal = 0;
            ResetScreenProgress(arms.Count * runs, "全軸 r9/r10");
            var sb = new StringBuilder();
            sb.AppendLine("=== 全軸 r9 / r10 (Balanced 同梱) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss} / 1 アーム {runs} ラン / シードは全アーム共通");
            sb.AppendLine($"予算 {MetaProgression.MetaPanel.MaxPoints}pt / "
                        + "各アーム = [焦点トラック r9 or r10] + [他の数値系 7 本を 2pt ずつ]");
            sb.AppendLine("  r10 = 10+14 = 24pt ちょうど / r9 = 9+14 = 23pt (余り 1pt は**使わない**)");
            sb.AppendLine("  → r10 と r9 の違いは焦点トラックの 1 段だけ。 埋め草は完全に同一");
            sb.AppendLine("判定基準: r10 は Balanced ±3pt");
            sb.AppendLine();
            sb.AppendLine("アーム                  |  クリア率 |  Bal比 |   r10−r9 | 判定 | 平均到達段");

            var clearOf = new Dictionary<string, double>();
            double baseClear = 0;
            for (int ai = 0; ai < arms.Count; ai++)
            {
                var (label, spec) = arms[ai];
                curArmLabel = label; curArmIndex = ai + 1; curArmCount = arms.Count; curRunsPerArm = runs;

                var tally = new RankMarginArmTally();
                yield return RunRankMarginArm(spec, runs, RankMarginSeedBase, tally);
                double clear = tally.ClearPct;
                double stage = tally.MeanStage;
                clearOf[label] = clear;
                if (ai == 0) baseClear = clear;

                double bal = clear - baseClear;
                string delta = "", verdict = "";
                if (ai == 0) { verdict = "基準"; }
                else if (label.EndsWith(" r9"))
                {
                    verdict = Math.Abs(bal) <= 2.0 ? "○" : (bal > 0 ? "強" : "弱");
                }
                else if (label.EndsWith(" r10")
                         && clearOf.TryGetValue(label.Replace(" r10", " r9"), out double r9c))
                {
                    double d = clear - r9c;
                    delta = $"{d,+9:F2}pt";
                    verdict = (d > 0 && Math.Abs(bal) <= 3.0) ? "○" : (d <= 0 ? "極点×" : (bal > 0 ? "強" : "弱"));
                }
                sb.AppendLine($"{label,-22} | {clear,8:F2}% | {(ai == 0 ? 0 : bal),+6:F2} | {delta,9} "
                            + $"| {verdict,-4} | {stage,9:F2}");
            }
            _metaAblationSpec = null;
            sb.AppendLine();
            sb.AppendLine($"※ 1 アーム {runs} ラン。 差の 95%CI は概ね ±"
                        + $"{1.96 * Math.Sqrt(2 * baseClear * (100 - baseClear) / runs):F2}pt。"
                        + " **r9 の基準 ±2pt はこの分解能とほぼ同じ**なので、 ○/× は目安。");
            sb.AppendLine("※ 判定  ○=基準内 / 強=上振れ / 弱=下振れ / 極点×= r10 が r9 以下 (極点が無価値か有害)。");
            sb.AppendLine("※ r9 が弱い → 1 段あたりの増加量を上げるか内容を変える。");
            sb.AppendLine("※ r10 が未達 → 極点をリワークかバフ。 r9 は基準内なのに r10 が外れているなら原因は極点だけ。");
            sb.AppendLine($"※ 強奪の道中パッシブドロップ: {GameManager.PlunderDrops:N0} 件");
            {
                long ra = ItemSystem.Shop.ShopManager.RobberyAttempts;
                sb.AppendLine($"※ 強盗 (強奪r10 へ移設): 発動 {ra:N0} 回 / 勝 {GameManager.RobberyWins:N0}"
                            + $" 敗 {GameManager.RobberyLosses:N0} / 平均戦利品 "
                            + $"{(GameManager.RobberyWins > 0 ? GameManager.RobberyLootTotal / (double)GameManager.RobberyWins : 0):F1} 件");
            }
            _metaAblationReport = sb.ToString();
            Debug.Log("[AutoRunner] 全軸 r9/r10 比較 完了\n" + _metaAblationReport);
        }

        /// <summary>r9/r10 比較のシード列の先頭。 全アーム共通。</summary>
        public const int RankMarginSeedBase = 60000;

        /// <summary>各アーム/各プロセスの頭で 1 本だけ空回しするラン。 集計に入れない。
        /// 理由は <c>RunRankMarginArm</c> の捨てランのコメント。</summary>
        public const int RankMarginWarmupSeed = RankMarginSeedBase - 1;

        /// <summary>r9/r10 比較の配点 — 焦点トラック <paramref name="rank"/> + 他の数値系 7 本を 2pt。
        ///
        /// <para>余りは配らない。 r9 は 9+2×7 = 23pt で 1pt 未使用のまま置く。
        /// 端数を他所へ回すと「10 段目 1pt の価値」を測っているはずが
        /// 「10 段目 1pt と埋め草 1pt の差」になる。 r10 と r9 の違いを
        /// <b>焦点トラックの 1 段だけ</b>に閉じるため、 余りは捨てる。</para>
        ///
        /// <para>逐次スイープと並列ワーカーが同じ配点を組むための唯一の実体。</para></summary>
        public static Dictionary<MetaProgression.MetaPanelKind, int> BuildRankMarginArmSpec(
            MetaProgression.MetaPanelKind focus, int rank)
        {
            var d = new Dictionary<MetaProgression.MetaPanelKind, int> { { focus, rank } };
            int left = MetaProgression.MetaPanel.MaxPoints - rank;   // 数値系は 1pt/段
            foreach (MetaProgression.MetaPanelKind k in Enum.GetValues(typeof(MetaProgression.MetaPanelKind)))
            {
                if (MetaProgression.MetaPanelKindExt.MaxRank(k) != 10) continue;
                if (k == focus || left <= 0) continue;
                int r = Math.Min(2, left);
                d[k] = r; left -= r;
            }
            return d;
        }

        /// <summary>1 アーム分の集計。 並列ワーカーはこれを JSON にして親へ返す。</summary>
        public sealed class RankMarginArmTally
        {
            public int valid;
            public int clears;
            public double stageSum;
            /// <summary>ラン毎の (runIdx, bandScore, digest)。 ペア比較と決定論の検算に使う。</summary>
            public readonly List<int> runIndices = new List<int>();
            public readonly List<int> bandScores = new List<int>();
            public readonly List<string> digests = new List<string>();
            public double ClearPct => valid > 0 ? clears / (double)valid * 100.0 : 0;
            public double MeanStage => valid > 0 ? stageSum / valid : 0;

            public void Add(int runIdx, RunRec rec)
            {
                int band = rec != null ? rec.bandScore : -1;
                runIndices.Add(runIdx);
                bandScores.Add(band);
                digests.Add(rec != null ? (rec.deterministicDigest ?? "") : "");
                if (band < 0) return;
                valid++;
                stageSum += band;
                if (band >= 11) clears++;
            }
        }

        /// <summary>並列ワーカー 1 プロセス分 — 担当アームの担当シード区間だけを走らせる。</summary>
        private IEnumerator RunRankMarginWorkerArm()
        {
            if (rankMarginWorkerSpec == null || rankMarginWorkerRuns <= 0)
            {
                Debug.LogError("[AutoRunner] r9/r10 ワーカー: 配点かラン数が未設定");
                try { RankMarginArmCompleted?.Invoke(null); } catch { }
                yield break;
            }
            curArmLabel = rankMarginWorkerLabel; curArmIndex = 1; curArmCount = 1;
            curRunsPerArm = rankMarginWorkerRuns;
            ResetScreenProgress(rankMarginWorkerRuns, rankMarginWorkerLabel);

            var tally = new RankMarginArmTally();
            yield return RunRankMarginArm(rankMarginWorkerSpec, rankMarginWorkerRuns,
                rankMarginWorkerSeedStart, tally);
            _metaAblationSpec = null;
            Debug.Log($"[AutoRunner] r9/r10 ワーカー完了: {rankMarginWorkerLabel} "
                    + $"seed {rankMarginWorkerSeedStart}..{rankMarginWorkerSeedStart + rankMarginWorkerRuns - 1} "
                    + $"/ 有効 {tally.valid} / クリア {tally.clears}");
            try { RankMarginArmCompleted?.Invoke(tally); } catch (Exception ex)
            { Debug.LogError("[AutoRunner] r9/r10 ワーカー: 応答書き出しで例外 " + ex.Message); }
        }

        /// <summary>r9/r10 比較の 1 アーム。
        /// <b>逐次スイープと並列ワーカーが共有する唯一の実体。</b>
        ///
        /// <para>アームの中身を 2 か所に写すと必ず食い違う (2026-08-17 の前例)。
        /// 並列版の検算は「逐次版と digest が一致すること」で行うので、
        /// <b>比較する 2 つが同じコードを通っていなければ検算にならない</b>。</para>
        ///
        /// <para><c>seedStart</c> でシード区間を切り出せるのは、 ラン i の結果が
        /// <c>runIdx</c> だけの関数だから (digest_cmp.py で 1,000/1,000 一致を確認済)。
        /// この前提が崩れたら並列化そのものが無効になる。</para></summary>
        private IEnumerator RunRankMarginArm(Dictionary<MetaProgression.MetaPanelKind, int> spec,
            int runs, int seedStart, RankMarginArmTally tally)
        {
            _metaAblationSpec = spec;

            // **捨てラン 1 本を先に回す。**
            //
            //   プロセス内の**先頭ランだけ**が、 2 本目以降と違う結果になる。 実測で
            //   chunks=1 と chunks=4 を突き合わせると、 ずれるのは各チャンクの先頭
            //   ちょうど 9/900 件で、 2 本目以降は全一致した。 同じチャンク数どうしは
            //   0/900 で完全再現するので、 タイミング由来のゆらぎではなく決定的な差。
            //
            //   効いているのは**前のランの中身ではなく「前のランが在ったか」の二値**。
            //   根拠: run#60151 は直前の 60150 が両者で違う結果になっているのに一致する。
            //   つまり 1 本空回しすれば、 先頭でも 2 本目以降と同じ土俵に乗る。
            //
            //   原因の初期化そのものは未特定。 特定できたらこの空回しは消すこと ──
            //   **これは症状を揃えているだけで、 原因を直してはいない。**
            GameLoop.GameRng.BeginRun(RankMarginWarmupSeed);
            UnityEngine.Random.InitState(RankMarginWarmupSeed);
            if (GameLoop.GameRng.IsSeeded)
                _rng = new System.Random(GameLoop.GameRng.Range(
                    0, int.MaxValue, "autorun.eventTiebreak", RankMarginWarmupSeed));
            yield return RunOne(RankMarginWarmupSeed);

            for (int i = 0; i < runs; i++)
            {
                int runIdx = seedStart + i;
                GameLoop.GameRng.BeginRun(runIdx);
                // **UnityEngine.Random もラン単位で固定する** (2026-08-10 の修正をここへ展開)。
                //   GameRng は BeginRun で完全にリセットされるが、 UnityEngine.Random の
                //   静的状態はラン跨ぎで残り、 **それまでに何回引かれたか**に依存する。
                //   同じプロセスで先頭から回す限り履歴が揃うので逐次では再現してしまい、
                //   **プロセスへ分割した瞬間に先頭ランだけ割れる** (実測 9/900・境界ちょうど)。
                //   ここが抜けていると並列版と逐次版が一致しない。
                UnityEngine.Random.InitState(runIdx);
                if (GameLoop.GameRng.IsSeeded)
                    _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                yield return RunOne(runIdx);
                var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                // **打ち切りは理由ごと残す。** 無効ランは母数から外れるので、
                //   黙って捨てると「そういう分布なのだ」と読んでしまう。
                //   しかも打ち切りは長生きしたランに偏るので、 捨てた分だけ結果が歪む。
                if (rec != null && (rec.outcome == Outcome.Deadlock || rec.outcome == Outcome.Crash))
                    LogAlways($"[r9/r10] 打切 run#{runIdx} — {rec.band}: {rec.note}", LogType.Warning);
                tally.Add(runIdx, rec);
                // **毎ラン報告する** (2026-09-19)。 50 ランごとだと重い方策 (Super) で
                //   最初の報告まで数分かかり、 止まっているのか区別できなかった。
                //   書くのは数十バイトの progress.txt 1 つなので毎ランでも負担にならない。
                {
                    try { RankMarginArmProgress?.Invoke(i + 1); }
                    catch (Exception ex)
                    { Debug.LogWarning("[AutoRunner] 進捗通知で例外: " + ex.Message); }
                }
                if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
            }
        }

        /// <summary><b>整備パネルの drop-one アブレーション (2026-09-10)。</b>
        /// Balanced から 1 軸ずつ抜いて、 どの軸がクリア率を支配しているかを測る。
        ///
        /// <para><b>なぜ要るか。</b> 整備パネルは実測でオッズ比 14〜21 と全強化軸で最大
        /// (遺物 29pt でさえ 5.4)。 ところが個々のボーナスは +30% 与ダメ / +18HP / +15%会心 と
        /// 中庸で、 <b>数字と効果が釣り合っていない</b>。 釣り合わないときは実装を読むか
        /// 内訳を測る ── 内訳が無いまま「強すぎる/妥当」は論じられない。</para>
        ///
        /// <para><b>drop-one にする理由。</b> 「その軸だけ」を測ると他軸との相乗が落ちるし、
        /// 予算も揃わない。 <b>限界寄与</b> (満額から 1 軸抜いたときの落ち幅) が
        /// 「この軸に振る価値」の定義として素直。 抜いた pt 数で割れば pt あたりの効率も出る。</para></summary>
        private IEnumerator RunMetaAxisAblation()
        {
            int runs = Mathf.Max(200, metaAblationRuns);
            var full = MetaAllocationPresets.Ranks(MetaAllocationPresets.Preset.Balanced);

            // アーム: [0]=満額 Balanced / [1..n]=1 軸抜き / 最後=全部無し (0pt)
            var arms = new List<(string label, Dictionary<MetaProgression.MetaPanelKind, int> spec, int dropped)>();
            arms.Add(("満額 Balanced", new Dictionary<MetaProgression.MetaPanelKind, int>(full), 0));
            foreach (var kv in full)
            {
                var d = new Dictionary<MetaProgression.MetaPanelKind, int>(full);
                d.Remove(kv.Key);
                arms.Add(($"−{kv.Key}({kv.Value}pt)", d, kv.Value));
            }
            arms.Add(("全無し 0pt", new Dictionary<MetaProgression.MetaPanelKind, int>(), 36));

            ResetScreenProgress(arms.Count * runs, "パネルAblation");
            var sb = new StringBuilder();
            sb.AppendLine("=== 整備パネル drop-one アブレーション ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss} / 1 アーム {runs} ラン / シードは全アーム共通");
            sb.AppendLine("満額 Balanced から 1 軸ずつ抜いた落ち幅 = その軸の限界寄与。");
            sb.AppendLine("**抜いた pt は再配分しない** ── 再配分すると『どの軸か』と『何ptか』が混ざる。");
            sb.AppendLine();
            sb.AppendLine("アーム                  |  クリア率 |    落ち幅 |  抜いたpt | pt あたり | 平均到達段");

            double baseClear = 0, baseStage = 0;
            for (int ai = 0; ai < arms.Count; ai++)
            {
                var (label, spec, dropped) = arms[ai];
                curArmLabel = label; curArmIndex = ai + 1; curArmCount = arms.Count; curRunsPerArm = runs;
                _metaAblationSpec = spec;

                int clears = 0; double stageSum = 0; int valid = 0;
                for (int i = 0; i < runs; i++)
                {
                    // **全アームで同じ runIdx** ── ここを崩すと差にシード分散が丸ごと乗る。
                    int runIdx = 50000 + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    yield return RunOne(runIdx);
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    if (rec != null && rec.bandScore >= 0)
                    { valid++; stageSum += rec.bandScore; if (rec.bandScore >= 11) clears++; }
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                double clear = valid > 0 ? clears / (double)valid * 100.0 : 0;
                double stage = valid > 0 ? stageSum / valid : 0;
                if (ai == 0) { baseClear = clear; baseStage = stage; }
                double drop = baseClear - clear;
                sb.AppendLine($"{label,-22} | {clear,8:F2}% | {(ai == 0 ? 0 : drop),8:F2}pt | {dropped,8} | "
                            + $"{(dropped > 0 ? drop / dropped : 0),8:F3} | {stage,8:F2}"
                            + (ai == 0 ? "  ← 基準" : ""));
            }
            _metaAblationSpec = null;
            sb.AppendLine();
            sb.AppendLine("※ 落ち幅が大きい軸ほど支配的。 pt あたりで見ると「効率」が出る。");
            sb.AppendLine($"※ 1 アーム {runs} ラン なので、 クリア率 {baseClear:F0}% 帯の MDE は概ね "
                        + $"{2.8 * Math.Sqrt(2 * baseClear * (100 - baseClear) / runs):F2}pt。 これ未満の差は読まないこと。");
            _metaAblationReport = sb.ToString();
            Debug.Log("[AutoRunner] パネル drop-one 完了\n" + _metaAblationReport);
        }

        private IEnumerator RunItemAblationSweep()
        {
            int runs = Mathf.Max(20, itemAblationRuns);
            var ids = new List<string>();
            if (!string.IsNullOrEmpty(itemAblationIds))
                foreach (var s in itemAblationIds.Split(','))
                    if (!string.IsNullOrWhiteSpace(s)) ids.Add(s.Trim());

            var db = ItemSystem.ItemDatabase.Instance;
            var sb = new StringBuilder();
            sb.AppendLine("=== アイテム アブレーション (同一シードのペア) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"1 アーム {runs} 対 / 開幕付与あり vs なし / シードは全アーム共通");
            sb.AppendLine("目的は効果量ではなく **対の差の SD** ── 全品アブレーションの必要ラン数を決める");
            sb.AppendLine();

            // **進捗の分母を張り替える。** 忘れると通常バッチの runCount (=1) が残り、
            //   完了数だけ増えて 100% を超え続ける (2026-09-08 に実際に出した)。
            ResetScreenProgress((ids.Count + 2) * runs, "アイテムAblation");

            // アーム -2 / -1 が A/A (両方とも付与なし)。 決定性の検査。
            var baseBand = new int[runs];
            var sb2 = new StringBuilder();
            sb2.AppendLine("アーム              | 平均band | 差の平均 | 差のSD | 差が出た対 | 7F改善/悪化 (McNemar p) | 0.05 に必要な対数");

            for (int ai = -2; ai < ids.Count; ai++)
            {
                string grant = ai < 0 ? null : ids[ai];
                string label = ai == -2 ? "[A] 付与なし"
                             : ai == -1 ? "[A] 付与なし(再)"
                             : (db?.GetItem(grant)?.displayName ?? grant);
                curArmLabel = label;
                curArmIndex = ai + 3;
                curArmCount = ids.Count + 2;
                curRunsPerArm = runs;

                var band = new int[runs];
                var c7 = new bool[runs];
                for (int i = 0; i < runs; i++)
                {
                    // **全アームで同じ runIdx。** ここを崩すと差にシード分散が丸ごと乗る。
                    int runIdx = 70000 + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _pendingGrantItemId = grant;
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    band[i] = rec != null ? rec.bandScore : -1;
                    c7[i] = band[i] >= 11;
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }

                if (ai == -2) { System.Array.Copy(band, baseBand, runs); }

                double mean = 0; for (int i = 0; i < runs; i++) mean += band[i];
                mean /= runs;

                string row;
                if (ai == -2)
                {
                    row = $"{label,-19} | {mean,8:F2} | {"—",8} | {"—",6} | {"—",10} | {"—",23} | {"—",16}";
                }
                else
                {
                    double dm = 0; int moved = 0;
                    for (int i = 0; i < runs; i++) { dm += band[i] - baseBand[i]; if (band[i] != baseBand[i]) moved++; }
                    dm /= runs;
                    double dv = 0;
                    for (int i = 0; i < runs; i++) { double d = (band[i] - baseBand[i]) - dm; dv += d * d; }
                    double sd = Math.Sqrt(dv / Math.Max(1, runs - 1));
                    int b = 0, c = 0;
                    for (int i = 0; i < runs; i++)
                    {
                        bool bb = baseBand[i] >= 11;
                        if (c7[i] && !bb) b++;
                        else if (!c7[i] && bb) c++;
                    }
                    // 真の効果 0.15 band を SE 0.05 で見るのに要る対の数
                    string need = sd > 0 ? $"{Mathf.CeilToInt((float)((sd / 0.05) * (sd / 0.05))),16}" : $"{"(差ゼロ)",16}";
                    row = $"{label,-19} | {mean,8:F2} | {dm,+8:F3} | {sd,6:F3} | {moved,4}/{runs,-5} | "
                        + $"{b,3}/{c,3} (p={McNemarP(b, c),6:F3})      | {need}";
                }
                sb2.AppendLine(row);
            }

            sb.Append(sb2);
            sb.AppendLine();
            sb.AppendLine("※ 1 行目と 2 行目は **同じ条件 (A/A)**。 ここで『差が出た対』が 0 でなければ");
            sb.AppendLine("   シミュレーションが決定的でない ── ペアリングが効かないので下の SD は読めない。");
            sb.AppendLine("※ 『0.05 に必要な対数』= (差のSD / 0.05)²。 アイテム間の真のばらつきは SD 0.15 band");
            sb.AppendLine("   (2026-09-07 / ホールドアウトの信頼性から逆算) なので、 これを 1/3 の精度で見る想定。");
            sb.AppendLine("※ 対照アームは**自然なラン**なので、 対象品を途中で買うことがある。 その分だけ");
            sb.AppendLine("   効果は薄まる (推定量としては『開幕から確実に持つこと』の効果)。");
            _itemAblationReport = sb.ToString();
            _pendingGrantItemId = null;
        }

        /// <summary>挑戦 単軸スイープ。 **挑戦デバフ 1 段だけを載せて、その段の重さを切り出す。**
        ///
        /// 固定難易度スイープは「合計 Npt」を振るので、 30pt が重すぎる時に
        /// **どの軸が値段に見合わないか**が分からない。 ここでは (軸, Tier) の組を
        /// 1 つずつ単独で載せ、 **0pt を基準にした同一シードのペア比較**で差を採る。
        ///
        /// 読み方: 「1pt あたり何 pt 分の 7層クリアを削るか」で軸を横並びにする。
        /// 3pt の軸が 1pt の軸の 3 倍削っていれば値付けは正しい。 大きく外れた軸が
        /// 調整対象。 **平均到達層では読まない** ── 崩れ方 (深く進んで死ぬ / 早期に詰む)
        /// が軸ごとに違うので、 到達層分布と 3F 止まり率を併記する。
        ///
        /// 条件は固定難易度スイープと揃える (Standard / TheoreticalBestCursed / 裏ボス遮断)
        /// ── そうしないと階段の測定値と直接つき合わせられない。</summary>
        private IEnumerator RunChallengeAxisSweep()
        {
            bool prevParallelWiring = parallelizeOptimalWiring;
            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;
            _pendingRelicExplicit = true;
            // 遺物なしは**バランスの正式な基準** (周回引継ぎなので初回プレイヤーは 0 個)。
            //   2026-08-10: このフラグ分岐が**固定難易度スイープ側にしか入っていなかった**ため、
            //   レポートは「遺物なし (基準条件)」と表示しながら理論最良遺物込みで走っていた。
            //   結果、 同じ 0pt 基準値が単軸 53.3% / 技量帯 16.6% に割れた
            //   (実測: 同一シード・同一職業で最大HP が 113 と 93。 差は遺物の +20 だけ)。
            //   **表示と実体を分けて持つと、 嘘が静かに通る。**
            _pendingPresetRelic = challengeAxisSweepNoRelic
                ? null : RelicPresets.Build(RelicPresets.Preset.TheoreticalBestCursed);
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = true;

            // **下限は 1**。 他のスイープは Max(50,…) で床を張っているが、 ここは
            //   動作確認 (各3ラン) が主要な使い方なので床を張ると「3 と指定したのに
            //   50 走る」ことになる (2026-08-10 に実際にやった)。
            int runs = Mathf.Max(1, challengeAxisSweepRuns);

            // ── アームの列挙 ──
            //   先頭は必ず 0pt (ペア比較の基準)。 以降は (軸, Tier) の全組。
            //   **必ず def.tiers を回すこと。** v4.1 で Tier 番号が飛び飛びになったので、
            //   1..MaxTier で回すと存在しない段のアームを走らせる (2026-08-10 に実際にやった)。
            var arms = new List<(MetaProgression.AxisDef def, int tier)>();
            arms.Add((null, 0));
            // 診断モード: T4 の解禁条件 (カテゴリ 6pt) を外す。 **このスイープの間だけ**。
            MetaProgression.ChallengeResolver.IgnoreT4Unlock = challengeCategorySweepT4Alone;

            // T4 モード: 軸ではなくカテゴリ単位のアームを組む (下の catArms を使う)。
            var catArms = new List<(MetaProgression.ChallengeCategory cat, bool withT4)>();
            if (challengeCategorySweep)
                foreach (MetaProgression.ChallengeCategory c in
                         System.Enum.GetValues(typeof(MetaProgression.ChallengeCategory)))
                {
                    // **基礎軸を変えていない回は 6pt アームを省ける** ── 前回と同じ値を
                    //   再現するだけなので、 前回の実測を参照値に使う。 その代わり
                    //   **基準アームが前回と一致するか**でバッチ間の地続きを確認すること。
                    if (challengeCategorySweepOnlyCat >= 0 && (int)c != challengeCategorySweepOnlyCat) continue;
                    if (!challengeCategorySweepT4Only && !challengeCategorySweepT4Alone) catArms.Add((c, false));
                    catArms.Add((c, true));
                }
            // 指定があればその軸だけに絞る。 基準アームとノイズ床は常に残す。
            var axisFilter = (challengeAxisSweepAxisNames != null && challengeAxisSweepAxisNames.Length > 0)
                           ? new HashSet<string>(challengeAxisSweepAxisNames) : null;
            // 価格曲線モード: 挑戦デバフは 0pt のまま、 価格倍率だけを振る。
            bool priceCurve = shopPriceCurve != null && shopPriceCurve.Length > 0;
            if (priceCurve)
            {
                for (int k = 0; k < shopPriceCurve.Length; k++) arms.Add((null, 0));
            }
            // 決定性診断: 0pt だけを N 本。 軸は一切載せない。
            else if (challengeAxisSweepDeterminismArms > 0)
            {
                for (int k = 1; k < challengeAxisSweepDeterminismArms; k++) arms.Add((null, 0));
            }
            else if (challengeCategorySweep)
            {
                // アームの中身は下の catArms が持つ。 ここでは個数だけ合わせる。
                for (int ci = 0; ci < catArms.Count; ci++) arms.Add((null, -1 - ci));
            }
            else
            for (int ai = 0; ai < MetaProgression.ChallengeCatalog.Axes.Count; ai++)
            {
                if (challengeAxisSweepOnlyAxis >= 0 && ai != challengeAxisSweepOnlyAxis) continue;
                var def = MetaProgression.ChallengeCatalog.Axes[ai];
                if (axisFilter != null && !axisFilter.Contains(def.axis.ToString())) continue;
                foreach (int t in def.tiers)
                {
                    if (challengeAxisSweepMaxTierOnly && t != def.MaxTier) continue;
                    arms.Add((def, t));
                }
            }
            // **末尾に基準の複製を置く。** 設定が 1 行目と完全に同一・同一シードなのに
            //   結果がどれだけ動くか ＝ **アーム間ノイズの実測床**。 これが無いと、
            //   ノイズを軸の効果と読み違える (2026-08-10 に偶然の重複アームで 4.0pt と判明)。
            //   軸の差がこの床より小さいなら「差は測れていない」と判定すること。
            arms.Add((null, 0));

            var sb = new StringBuilder();
            sb.AppendLine("=== 挑戦 単軸スイープ (v4.0) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ペルソナ Standard 固定 / 遺物 {(challengeAxisSweepNoRelic ? "なし (基準条件)" : "TheoreticalBestCursed 固定")} / 5層裏ボス遮断 / 技量 {wiringSkill}");
            sb.AppendLine($"1 アーム {runs} ラン / **全アーム同一シード** (ペア比較)");
            sb.AppendLine(_stateLineAtStart);
            _probeTrace = "";
            _probeTrace = "";
            sb.AppendLine();
            sb.AppendLine("軸              段  点 | 平均到達層 | 5層クリア | 7層クリア | 7F差分 | 1pt当り | 3F止まり | 獲得G | 支出G | 残金 | 購入計 | パッシブ| 消耗品| 武器 | ダイス| 素材 | 装備力 | 打切 | 到達層分布 1F〜7F | 7F改善/悪化 (McNemar p)");

            // 固定難易度1000ランと同じ画面内進捗。単軸スイープは全アーム合計を100%とする。
            ResetScreenProgress(arms.Count * runs, "挑戦単軸");

            bool[] baseC5 = new bool[runs], baseC7 = new bool[runs];
            double base7Pct = 0;
            int missingRec = 0;   // RunOne 後に _records から結果を拾えなかった回数
            float swStart = Time.realtimeSinceStartup;   // 進捗行の経過/ETA 用
            // 決定性検査用。 アームごとのラン指紋を丸ごと保持する。
            var fpBase = new int[runs];
            var fpDiag = new List<(string label, int[] fp)>();
            // 1ラン差分モード: アームごとのナラティブを退避する (_detail は runIdx キーなので
            //   後のアームが上書きしてしまい、 そのままでは 2 アームを比べられない)。
            bool narrateDiff = challengeAxisSweepDeterminismArms > 0 && runs <= 2;
            var narr = new List<List<string>>();

            for (int armIdx = 0; armIdx < arms.Count; armIdx++)
            {
                if (challengeAxisSweepParallelCompare)
                    parallelizeOptimalWiring = armIdx > 0;
                float armStartRt = Time.realtimeSinceStartup;
                var (def, tier) = arms[armIdx];
                var lo = new MetaProgression.ChallengeLoadout();
                int pts = def != null ? def.PointsOf(tier) : 0;
                string catLabel = null;
                if (def != null) lo.SetTier(def.axis, tier);
                else if (tier < 0)
                {
                    // T4 モード: カテゴリの全軸を最上位へ。 withT4 ならその上に T4 を載せる。
                    var (cat, withT4) = catArms[-1 - tier];
                    // 単独モードではカテゴリ基礎軸を載せない (基準 + T4 のみ)。
                    if (!challengeCategorySweepT4Alone)
                        foreach (var ax in MetaProgression.ChallengeCatalog.AxesOf(cat))
                        { lo.SetTier(ax.axis, ax.MaxTier); pts += ax.MaxPoints; }
                    if (withT4)
                    {
                        var t4 = MetaProgression.ChallengeCatalog.T4s.Find(x => x.category == cat);
                        if (t4 != null) { lo.SetT4(t4.t4, true); pts += t4.points; }
                        catLabel = challengeCategorySweepT4Alone
                                 ? "単独:" + (t4 != null ? t4.displayName : "T4")
                                 : cat + "+" + (t4 != null ? t4.displayName : "T4");
                    }
                    else catLabel = cat + " 6pt";
                }

                // 救済 (灯火/ラストスタンド/フルーレ) の発動をアーム単位で数える。
                GameLoop.LastStand.ResetStats();

                var reach = new int[9];
                int c5 = 0, c7 = 0; double sumFloor = 0;
                var thisC5 = new bool[runs];
                var thisC7 = new bool[runs];
                var thisFp = new int[runs];
                int recBase = _records.Count;   // 経済統計の集計開始位置
                int abortedSoFar = 0;           // 進捗行に出す打ち切り件数
                // 価格曲線モードでは倍率を差し込む。 基準アーム (armIdx 0) は上書きしない。
                float priceMul = 0f;
                if (priceCurve && armIdx > 0 && armIdx - 1 < shopPriceCurve.Length)
                    priceMul = shopPriceCurve[armIdx - 1];
                MetaProgression.MetaDebuffApplicator.ShopPriceOverride = priceMul;

                string armLabel = catLabel != null ? catLabel
                                : def != null ? def.displayName + " T" + tier
                                : priceMul > 0f ? $"価格 ×{priceMul:F2}"
                                : challengeAxisSweepParallelCompare
                                    ? (armIdx == 0 ? "0pt直列" : "0pt並列")
                                : (armIdx == 0 ? "0pt基準" : "0pt複製");
                // 外部観測用。 「どのアームの何ラン目で止まったか」を反射に頼らず読めるように。
                curArmIndex = armIdx + 1; curArmCount = arms.Count;
                curRunsPerArm = runs; curArmLabel = armLabel;

                for (int i = 0; i < runs; i++)
                {
                    // **全アームで同じ runIdx** ＝ ペア比較。 アームごとに別シード帯を割ると
                    //   軸間の差にシード分散が丸ごと乗り、 数pt の寄与が埋もれる (§13-5)。
                    curRunInArm = i + 1;
                    int runIdx = challengeAxisSweepIndexBase + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    // **UnityEngine.Random もラン単位で固定する** (2026-08-10)。
                    //   GameRng は BeginRun で完全にリセットされるが、 UnityEngine.Random の
                    //   静的状態はラン跨ぎで残り、 **それまでに何回引かれたか**に依存する。
                    //   スイープ内は呼び出し履歴が同一なので再現する (ノイズ床 0/300) 一方、
                    //   別のスイープ関数から回すと履歴が変わって同一シードのランが割れていた。
                    UnityEngine.Random.InitState(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    _pendingChallengeLoadout = lo;
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    // **rec が拾えないまま黙って進まない。** 拾えなければ全ランが「1F 敗北」に
                    //   計上され、 表は形になるのに中身が全部 0 という**読める形の嘘**になる。
                    if (rec == null) missingRec++;
                    if (rec != null && (rec.outcome == Outcome.Deadlock || rec.outcome == Outcome.Crash))
                    {
                        abortedSoFar++;
                        // **打ち切りは即座に出す。** 後でまとめて数えるだけだと、
                        //   走行中に「いま何が起きているか」が分からない。
                        LogAlways($"[挑戦単軸] 打切 {armLabel} run#{i} — {rec.note}", LogType.Warning);
                    }
                    thisFp[i] = rec != null ? rec.fingerprint : 0;
                    if (narrateDiff && i == 0)
                        narr.Add(_detail.TryGetValue(runIdx, out var dl)
                                 ? new List<string>(dl) : new List<string>());
                    int reached = rec != null ? rec.reachedFloor : 1;
                    // 乖離調査 (2026-08-10): 同一 runIdx のランが技量帯スイープと一致するか。
                    if ( armIdx == 0 && i < 10)
                        _probeTrace += $" #{runIdx}:{reached}{((rec == null || rec.outcome == Outcome.GameOver) ? "x" : "o")}"
                                     + $"[{_probeRunConfig}]";
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    int cf = died ? reached - 1 : reached;
                    reach[Mathf.Clamp(reached, 0, 8)]++; sumFloor += reached;
                    if (cf >= 5) { c5++; thisC5[i] = true; }
                    if (cf >= 7) { c7++; thisC7[i] = true; }
                    // **アーム内でも進捗を出す。** アーム末尾だけだと 1 アーム 300 ラン の間
                    //   無音になり、 「遅い」と「止まった」を区別できない (2026-08-10)。
                    if (challengeAxisSweepProgressEvery > 0
                        && (i + 1) % challengeAxisSweepProgressEvery == 0 && i + 1 < runs)
                    {
                        float el = Time.realtimeSinceStartup - swStart;
                        int doneRuns = armIdx * runs + i + 1;
                        int allRuns = arms.Count * runs;
                        float eta = doneRuns > 0 ? el / doneRuns * (allRuns - doneRuns) : 0f;
                        LogAlways($"[挑戦単軸]   {armIdx + 1}/{arms.Count} {armLabel} "
                                + $"{i + 1}/{runs} ラン … 経過 {el / 60f:F1}分 / 残り {eta / 60f:F1}分 "
                                + $"(暫定 7F {c7 * 100.0 / (i + 1):F1}%, 打切 {abortedSoFar})");
                    }
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }

                // アーム完了。 経過と残り見込みを添える。
                {
                    float el = Time.realtimeSinceStartup - swStart;
                    int doneRuns = (armIdx + 1) * runs;
                    int allRuns = arms.Count * runs;
                    float eta = el / doneRuns * (allRuns - doneRuns);
                    LogAlways($"[挑戦単軸] {armIdx + 1}/{arms.Count} 完了 {armLabel} "
                            + $"→ 5F {c5 * 100.0 / runs:F1}% / 7F {c7 * 100.0 / runs:F1}% "
                            + $"| 打切 {abortedSoFar} / rec欠落 {missingRec} "
                            + $"| アーム {Time.realtimeSinceStartup - armStartRt:F1}秒 "
                            + $"| 経過 {el / 60f:F1}分 残り {eta / 60f:F1}分");
                }

                double pct7 = c7 * 100.0 / runs;
                if (armIdx == 0)
                {
                    System.Array.Copy(thisC5, baseC5, runs);
                    System.Array.Copy(thisC7, baseC7, runs);
                    System.Array.Copy(thisFp, fpBase, runs);
                    base7Pct = pct7;
                }
                else
                {
                    var keep = new int[runs];
                    System.Array.Copy(thisFp, keep, runs);
                    fpDiag.Add((def != null ? def.displayName + " T" + tier : "0pt複製#" + armIdx, keep));
                }

                int b7 = 0, c7b = 0;
                for (int i = 0; i < runs; i++)
                {
                    if (thisC7[i] && !baseC7[i]) b7++;
                    else if (!thisC7[i] && baseC7[i]) c7b++;
                }

                double delta7 = pct7 - base7Pct;
                // **1pt 当たりの削り量**。 値付けが揃っているかはこの列だけで読める。
                string perPt = (armIdx == 0 || pts <= 0) ? "—" : $"{delta7 / pts,6:F2}";
                string mc7 = armIdx == 0 ? "—" : $"{b7,3}/{c7b,3} (p={McNemarP(b7, c7b),6:F3})";

                // ── 経済の実挙動 ──
                //   価格倍率を上げたのに 7F 差分が動かない、 といった飽和が
                //   「買えなくなった」のか「買い方が変わった」のかは、
                //   到達層だけでは絶対に区別できない。 購入数・獲得GOLD・
                //   最終装備力を並べて初めて読める (2026-08-10 に説明を憶測で埋めて指摘された)。
                double gold = 0, buys = 0, power = 0, spent = 0, left = 0;
                double bPas = 0, bCon = 0, bWep = 0, bDic = 0, bMat = 0;
                double wTrig = 0, wLost = 0; int wRuns = 0;
                int en = 0, aborted = 0;
                for (int r = recBase; r < _records.Count; r++)
                {
                    var rr = _records[r];
                    if (rr.outcome == Outcome.Deadlock || rr.outcome == Outcome.Crash) aborted++;
                    gold  += rr.totalGoldGained;
                    buys  += rr.shopPurchases;
                    power += rr.finalInventoryPower;
                    spent += rr.totalCoinsSpent;
                    left  += rr.finalCoins;
                    bPas += rr.shopBuyPassive;  bCon += rr.shopBuyConsumable;
                    bWep += rr.shopBuyWeapon;   bDic += rr.shopBuyDice;
                    bMat += rr.shopBuyMaterial;
                    wTrig += rr.woundTriggers; wLost += rr.woundHpLost;
                    if (rr.woundTriggers > 0) wRuns++;
                    en++;
                }
                string econ = en > 0
                    ? $"{gold / en,6:F0} |{spent / en,6:F0} |{left / en,5:F1} |{buys / en,6:F2} |"
                      + $"{bPas / en,5:F2} |{bCon / en,5:F2} |{bWep / en,5:F2} |{bDic / en,5:F2} |"
                      + $"{bMat / en,5:F2} |{power / en,7:F1} |{aborted,4}"
                    : "     ? |     ? |    ? |     ? |    ? |    ? |    ? |    ? |    ? |      ? |   ?";

                var dist = new StringBuilder();
                for (int f = 1; f <= 7; f++) dist.Append($"{reach[f] * 100.0 / runs,5:F1}%");

                // **T4 モードのアームは tier < 0 で区別する。** これを見ないと
                //   カテゴリアームが全部「基準複製」と表示され、 表が読めない (2026-08-11 に踏んだ)。
                bool isNoiseFloor = def == null && tier == 0 && armIdx > 0;
                string label = def != null ? def.displayName
                             : catLabel != null ? catLabel
                             : (isNoiseFloor ? "[ノイズ床] 基準複製" : "(挑戦なし 0pt)");
                string seg = def != null ? $"T{tier}{(tier == def.MaxTier ? "*" : " ")}{pts,2}pt"
                           : catLabel != null ? $"    {pts,2}pt" : "     0pt";

                sb.AppendLine($"{PadR(label, 14)} {seg} | {sumFloor / runs,10:F2} | "
                            + $"{c5 * 100.0 / runs,8:F1}% | {pct7,8:F1}% | "
                            + $"{(armIdx == 0 ? "—" : $"{delta7,6:F1}"),6} | {perPt,7} | "
                            + $"{reach[3] * 100.0 / runs,7:F1}% |{econ} |{dist} | {mc7}");
                // 〈長引く負傷〉の行にだけ発動状況を添える。 **「弱い」と「発動していない」は
                //   到達層からは区別できない** ── 実際 2026-08-10 にそこで判断を誤った。
                // 救済の発動元。 **HP0 は「死」ではなく「全回復の引き金」**でもあるので、
                //   HP を削るデバフの効き方はここを見ないと読めない (2026-08-11)。
                if (en > 0)
                {
                    var rc = GameLoop.LastStand.RevivalCount;
                    long rtot = rc[0] + rc[1] + rc[2];
                    sb.AppendLine($"             └ 救済: 灯火 {rc[0]} / ラストスタンド {rc[1]} / フルーレ {rc[2]} "
                                + $"= 計 {rtot} 回 ({rtot * 100.0 / en:F1}% のラン) "
                                + $"/ 戻した HP 計 {GameLoop.LastStand.RevivalHpRestored}");
                }
                // **閾値・上限は印字しない (2026-08-17)。** 2026-08-10 のリワークで
                //   〈長引く負傷〉は「被ダメ比例」になり、 閾値も回数上限も持たなくなったのに
                //   定数だけが残り、 ログが廃止済みの旧仕様を出し続けていた。
                //   実測の平均損失 32.4 が「上限 20」を超えていて矛盾しており、
                //   **上限が壊れているのではなく上限が存在しない**のが実態。
                //   効いている値 (被ダメの何 %) だけを出す。
                if (def != null && def.axis == MetaProgression.ChallengeAxis.長引く負傷 && en > 0)
                    sb.AppendLine($"             └ 発動: {wRuns * 100.0 / en:F1}% のランで平均 {wTrig / en:F2} 回 "
                                + $"/ 失った最大HP 平均 {wLost / en:F1} "
                                + $"(被ダメの {MetaProgression.MetaDebuffApplicator.GetLingeringWoundRatio():P0})");
            }

            // **診断バイパスを必ず戻す。** 立てっぱなしだと以後の全測定が不正条件になる。
            MetaProgression.ChallengeResolver.IgnoreT4Unlock = false;
            parallelizeOptimalWiring = prevParallelWiring;

            // ── 決定性検査 ──
            //   1 行目と設定が同じアーム (0pt 複製) は、 同一シードなら**全ラン指紋一致**が正。
            //   一致しないなら、 それは統計のばらつきではなく **状態の持ち越し**。
            sb.AppendLine();
            sb.AppendLine("=== 決定性検査 (基準アームとのラン単位 指紋照合) ===");
            for (int k = 0; k < fpDiag.Count; k++)
            {
                var (lbl, fp) = fpDiag[k];
                int diff = 0, first = -1;
                for (int i = 0; i < runs; i++)
                    if (fp[i] != fpBase[i]) { diff++; if (first < 0) first = i; }
                sb.AppendLine($"{PadR(lbl, 22)} 不一致 {diff,4}/{runs} ({diff * 100.0 / runs,5:F1}%)"
                            + (first < 0 ? "  — 完全一致" : $"  初回発散 = ラン #{first}"));
            }
            sb.AppendLine("※ **0pt複製 は 1 行目と設定が完全に同一**。 ここが完全一致でないなら");
            sb.AppendLine("   ラン間で状態が持ち越されている ＝ 全ての軸間比較が信用できない。");
            sb.AppendLine("   **初回発散 #0 は「アーム切替の初期化漏れ」を意味しない** ── アーム 1 が");
            sb.AppendLine("   始まる時点で既に前アーム分のランが経過しており、 累積状態でも #0 で出る。");
            sb.AppendLine("   両者の区別には 1 ラン単位の詳細ログ差分が要る (2026-08-10 の反省)。");

            // ── 1ラン差分: 同一設定・同一 runIdx の 2 アームで、 **最初に分岐した行**を出す ──
            //   ここが「何が状態を持ち越しているか」への唯一の直接証拠になる。
            if (narrateDiff && narr.Count >= 2)
            {
                var a = narr[0]; var b = narr[1];
                int at = -1;
                int n = Math.Min(a.Count, b.Count);
                for (int i = 0; i < n; i++) if (a[i] != b[i]) { at = i; break; }
                if (at < 0 && a.Count != b.Count) at = n;
                sb.AppendLine();
                sb.AppendLine("=== 1ラン差分 (アーム1 vs アーム2 / 同一 runIdx・同一設定) ===");
                sb.AppendLine($"行数: アーム1 {a.Count} / アーム2 {b.Count}");
                if (at < 0) sb.AppendLine("→ **完全一致**。 このランでは分岐していない。");
                else
                {
                    sb.AppendLine($"→ **最初の分岐は {at} 行目**。 直前 6 行と両者の該当行:");
                    for (int i = Math.Max(0, at - 6); i < at; i++) sb.AppendLine($"   共通 | {a[i]}");
                    sb.AppendLine($"   A>>> | {(at < a.Count ? a[at] : "(行なし)")}");
                    sb.AppendLine($"   B>>> | {(at < b.Count ? b[at] : "(行なし)")}");
                    for (int i = at + 1; i < Math.Min(at + 5, n); i++)
                    {
                        sb.AppendLine($"   A    | {a[i]}");
                        sb.AppendLine($"   B    | {b[i]}");
                    }
                }
            }

            sb.AppendLine();
            if (missingRec > 0)
                sb.AppendLine($"!!! rec 欠落 {missingRec} 件 — この表は信用できない (欠落ランは全て『1F 敗北』に計上されている) !!!");
            sb.AppendLine("※ **打切** = Deadlock/Crash で終わったラン数 (番犬による実時間打ち切りを含む)。");
            sb.AppendLine("   0 でないアームの数値は、 その分だけ『1F 敗北』側に歪んでいる。");
            sb.AppendLine($"[先頭10ラン 到達層]{_probeTrace}");
            {
                long calls = MetaProgression.MetaDebuffApplicator.HealSpotCalls;
                long raw   = MetaProgression.MetaDebuffApplicator.HealSpotRawHeal;
                long capn  = MetaProgression.MetaDebuffApplicator.HealSpotCapped;
                long lost  = MetaProgression.MetaDebuffApplicator.HealSpotLostHeal;
                sb.AppendLine($"[回復スポット計装] 呼出 {calls} 回 / 実回復 {raw} HP"
                            + $" / 上限で削られた {capn} 回・{lost} HP"
                            + (calls > 0 ? $" (1 回あたり 実回復 {raw / (float)calls:F1} / 損失 {lost / (float)calls:F1})" : ""));
            }
            {
                var M = typeof(MetaProgression.MetaDebuffApplicator);
                long tt = MetaProgression.MetaDebuffApplicator.JudgmentTurnsTotal;
                long ov = MetaProgression.MetaDebuffApplicator.JudgmentRunsOverThreshold;
                long tk = MetaProgression.MetaDebuffApplicator.JudgmentTicks;
                long hl = MetaProgression.MetaDebuffApplicator.JudgmentHpLost;
                long bd = MetaProgression.MetaDebuffApplicator.BreakdownTriggers;
                long totalRuns = (long)runs * arms.Count;
                sb.AppendLine($"[T4計装] 累計戦闘ターン {tt} / 全 {totalRuns} ラン ＝ 1ラン {tt / (double)Math.Max(1, totalRuns):F1} ターン"
                            + $" / 刻限({MetaProgression.MetaDebuffApplicator.GetJudgmentTurnThreshold()}T)超え {ov} ラン ({100.0 * ov / Math.Max(1, totalRuns):F1}%)"
                            + $" / 被ダメ増幅 {tk} 回・+{hl} HP");
                long vc = MetaProgression.MetaDebuffApplicator.VoidTileChecks;
                long vt = MetaProgression.MetaDebuffApplicator.VoidTileTriggers;
                sb.AppendLine($"[T4計装] 破綻: 有利マス判定 {vc} 回 (1ラン {vc / (double)Math.Max(1, runs):F1} 個)"
                            + $" / 空白化 {vt} 回 (1ラン {vt / (double)Math.Max(1, runs):F2} 個 / 実効 {100.0 * vt / Math.Max(1, vc):F1}%)");
                {
                    long jk = MetaProgression.MetaDebuffApplicator.JudgmentKills;
                    long jd = MetaProgression.MetaDebuffApplicator.JudgmentPostDamageTaken;
                    long jh = MetaProgression.MetaDebuffApplicator.JudgmentPostHeal;
                    var rf = MetaProgression.MetaDebuffApplicator.JudgmentReachByFloor;
                    long rfTot = 0; for (int f = 0; f < rf.Length; f++) rfTot += rf[f];
                    var rfs = new System.Text.StringBuilder();
                    for (int f = 1; f <= 7; f++)
                        rfs.Append($" {f}F {100.0 * rf[f] / Math.Max(1, rfTot):F1}%");
                    long hp = MetaProgression.MetaDebuffApplicator.JudgmentHealPrevented;
                    sb.AppendLine($"[T4計装] 審判: 致命打 {jk} 回"
                                + $" / 超過後 被ダメ {jd}・実回復 {jh}・被ダメ増幅 +{hl}"
                                + $" (増幅分は超過後被ダメの {100.0 * hl / Math.Max(1, jd):F1}%)");
                    sb.AppendLine($"[T4計装] 審判: 刻限超過ランの到達層 ({rfTot} ラン)" + rfs);
                    long dn = MetaProgression.MetaDebuffApplicator.JudgmentDeadlineDeaths;
                    long sn = MetaProgression.MetaDebuffApplicator.JudgmentDeadlineSurvivors;
                    long dd = MetaProgression.MetaDebuffApplicator.JudgmentDeathDamage;
                    long dr = MetaProgression.MetaDebuffApplicator.JudgmentDeathHealRequested;
                    long da = MetaProgression.MetaDebuffApplicator.JudgmentDeathHealActual;
                    long sd = MetaProgression.MetaDebuffApplicator.JudgmentSurvivorDamage;
                    long sr = MetaProgression.MetaDebuffApplicator.JudgmentSurvivorHealRequested;
                    long sa = MetaProgression.MetaDebuffApplicator.JudgmentSurvivorHealActual;
                    sb.AppendLine($"[T4計装] 審判: 死亡 {dn}ラン 平均 被ダメ {dd / (double)Math.Max(1, dn):F1} / 回復要求 {dr / (double)Math.Max(1, dn):F1} / 実回復 {da / (double)Math.Max(1, dn):F1}");
                    sb.AppendLine($"[T4計装] 審判: 生存 {sn}ラン 平均 被ダメ {sd / (double)Math.Max(1, sn):F1} / 回復要求 {sr / (double)Math.Max(1, sn):F1} / 実回復 {sa / (double)Math.Max(1, sn):F1}");
                }
            }
            sb.AppendLine("※ 基準は 1 行目 (挑戦なし 0pt)。 * が付く段はその軸の最上位。");
            sb.AppendLine("※ **最終行 [ノイズ床] は 1 行目と設定が完全に同一・同一シード**。 その 7F差分が");
            sb.AppendLine("   このバッチのアーム間ノイズの実測値。 **これより小さい差は読まないこと**。");
            sb.AppendLine("   (2026-08-10 実測で 4.0pt。 軸の効果と誤認しかけたため常設化した)");
            sb.AppendLine("※ **判断は McNemar p で行う**。 7F差分の絶対値だけを見ない (同一シードでも");
            sb.AppendLine("   7層クリアは低頻度事象なので、 有意でない ±数pt は普通に出る)。");
            sb.AppendLine("※ 『1pt当り』が他軸より大きく負の軸が **値段に見合わず重い**軸。");
            sb.AppendLine("   同じ軸の T1→T2→T3 でこの列が悪化するなら、 その軸は段が超線形に効いている。");
            sb.AppendLine("※ 3F止まりが基準より突出する軸は、 天井を下げるのでなく **序盤で詰ませて**いる。");
            sb.AppendLine("※ 経済列の読み方 (2026-08-10 に追加)。 価格倍率を上げても 7F 差分が動かないとき:");
            sb.AppendLine("   ・**残金が増えている** → 買えないのではなく **買う対象が尽きている**");
            sb.AppendLine("     (陳列は 12 枠しかない)。 価格では段を作れず、 触るべきは陳列枠。");
            sb.AppendLine("   ・**種別ごとの弾性が違う** → 価格に鈍い枠が分母を薄めている。");
            sb.AppendLine("     リロールは倍率非適用、 素材は 3×2^N×倍率 で log 弾性、 特売品は 20〜60% 引き。");
            sb.AppendLine("   ・購入計だけ見ると上の 2 つを区別できない。 **必ず内訳と残金を併せて読む**。");
            _challengeAxisSweepReport = sb.ToString();

            _pendingChallengeLoadout = null;
            _pendingPresetRelic = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
            // **計測用の上書きは必ず戻す。** 立ちっぱなしだと後続の測定が全部汚れる。
            MetaProgression.MetaDebuffApplicator.ShopPriceOverride = 0f;
        }

        /// <summary>ビルド別勝率スイープ。 **ペルソナだけを振って 5層/7層クリア率を比べる。**
        ///
        /// 周回モード (ascensionMode) は難易度ラチェットが入るので「そのビルドがどこまで
        /// 上げられたか」しか分からない。 ここは **条件を全部固定**して素の勝率を出す:
        ///   メタバフ Standard (Balanced 配分) / 遺物なし / 挑戦 0pt / 5層裏ボス遮断。
        ///
        /// **全ペルソナで同一シード** ＝ ペア比較。 判断は Standard を基準にした McNemar で行う
        /// (500 ランでも 5層クリアの標準誤差は ±2.2pt あり、 平均どうしでは数pt の差が読めない)。</summary>
        private IEnumerator RunPersonaSweep()
        {
            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;   // ペルソナ選好を効かせるため
            var prevMetaMode = metaBuffMode;
            metaBuffMode = MetaBuffMode.Standard;       // 「スタンダードで」= Balanced 配分に固定
            _ascensionPersonaLock = true;
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = true;

            int runs = Mathf.Max(50, personaSweepRuns);
            var personas = (BuildPersona[])Enum.GetValues(typeof(BuildPersona));
            ResetScreenProgress(personas.Length * runs, "ビルド別");
            var loadout = AscensionLoop.BuildLoadoutAtScore(0);

            var sb = new StringBuilder();
            sb.AppendLine("=== ビルド別勝率スイープ ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("メタバフ Standard (Balanced) / 遺物なし / 挑戦 0pt / 5層裏ボス遮断");
            sb.AppendLine($"1 ペルソナ {runs} ラン / 全ペルソナ同一シード (ペア比較)");
            sb.AppendLine("決定論シード: 0000 0000 0000 0000");
            sb.AppendLine();
            sb.AppendLine("ビルド      | 平均到達層 | 5層クリア | 7層クリア | 到達層分布 1F〜7F                          | 対Standard 5F改善/悪化 (p) | 7F改善/悪化 (p)");

            bool[] baseC5 = new bool[runs], baseC7 = new bool[runs];
            var rows = new List<(string label, string line, bool[] c5, bool[] c7)>();

            for (int pi = 0; pi < personas.Length; pi++)
            {
                var persona = personas[pi];
                curArmIndex = pi + 1; curArmCount = personas.Length;
                curRunsPerArm = runs; curArmLabel = persona.ToString();
                var reach = new int[9];
                int c5 = 0, c7 = 0; double sumFloor = 0;
                var thisC5 = new bool[runs];
                var thisC7 = new bool[runs];

                for (int i = 0; i < runs; i++)
                {
                    curRunInArm = i + 1;
                    curRunInArm = i + 1;
                    // **全ペルソナで同じ runIdx** ＝ ペア比較 (pi を混ぜない)。
                    int runIdx = 70000 + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = persona;
                    _pendingChallengeLoadout = loadout;
                    _pendingPresetRelic = null;
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    int cf = died ? reached - 1 : reached;
                    reach[Mathf.Clamp(reached, 0, 8)]++; sumFloor += reached;
                    if (cf >= 5) { c5++; thisC5[i] = true; }
                    if (cf >= 7) { c7++; thisC7[i] = true; }
                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }

                if (persona == BuildPersona.Standard)
                {
                    System.Array.Copy(thisC5, baseC5, runs);
                    System.Array.Copy(thisC7, baseC7, runs);
                }

                var dist = new StringBuilder();
                for (int f = 1; f <= 7; f++) dist.Append($"{reach[f] * 100.0 / runs,5:F1}%");
                string line = $"{persona,-11} | {sumFloor / runs,10:F2} | {c5 * 100.0 / runs,8:F1}% | "
                            + $"{c7 * 100.0 / runs,8:F1}% |{dist} |";
                rows.Add((persona.ToString(), line, thisC5, thisC7));
            }

            // McNemar は Standard を全ペルソナぶん走らせ終えてから当てる (基準の順序に依存しないように)。
            foreach (var (label, line, c5arr, c7arr) in rows)
            {
                int b = 0, c = 0, b7 = 0, c7b = 0;
                for (int i = 0; i < runs; i++)
                {
                    if (c5arr[i] && !baseC5[i]) b++; else if (!c5arr[i] && baseC5[i]) c++;
                    if (c7arr[i] && !baseC7[i]) b7++; else if (!c7arr[i] && baseC7[i]) c7b++;
                }
                bool isBase = label == BuildPersona.Standard.ToString();
                sb.AppendLine(line + (isBase ? " —（基準） | —"
                    : $" {b,3}/{c,3} (p={McNemarP(b, c),6:F3}) | {b7,3}/{c7b,3} (p={McNemarP(b7, c7b),6:F3})"));
            }

            sb.AppendLine();
            sb.AppendLine("※ 基準は Standard。 平均どうしの比較はせず McNemar p で判断する。");
            sb.AppendLine("※ RawTier は Tier 表そのまま = ビルド選好なし。 ペルソナ選好の効果量はこことの差で読む。");
            _personaSweepReport = sb.ToString();

            _pendingChallengeLoadout = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            metaBuffMode = prevMetaMode;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
        }

        private static string Trunc(string s, int n)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));

        /// <summary>McNemar 検定の両側 p 値 (二項検定の厳密版)。 b / c は不一致ペアの数。
        ///
        /// 不一致ペアだけを見て「b が二項(n=b+c, p=0.5) からどれだけ外れているか」を測る。
        /// 一致ペア (両方クリア / 両方失敗) は情報を持たないので分母から落ちる ──
        /// これが同一シードのペア比較で分散が下がる理屈。</summary>
        private static double McNemarP(int b, int c)
        {
            int n = b + c;
            if (n == 0) return 1.0;
            int k = Mathf.Min(b, c);
            // 片側 = Σ_{i=0..k} C(n,i) / 2^n を対数で。 n が数百でも溢れない。
            double logHalfPow = -n * System.Math.Log(2.0);
            double sum = 0.0, logC = 0.0;              // logC = log C(n,0) = 0
            for (int i = 0; i <= k; i++)
            {
                if (i > 0) logC += System.Math.Log((double)(n - i + 1) / i);
                sum += System.Math.Exp(logC + logHalfPow);
            }
            return System.Math.Min(1.0, 2.0 * sum);    // 両側
        }

        /// <summary>遺物プリセットスイープ。 **遺物の強さ以外を全部固定して深度が動くかを測る。**
        ///
        /// 周回モードの「挑戦上限」はペルソナの素の強さで決まってしまい (序盤クリア率との相関
        /// r=+0.92 に対し 遺物効用とは r=+0.13)、 遺物の寄与を切り出せない。 ここでは
        /// **ペルソナも挑戦スコアも固定**し、 遺物プリセットだけを None→Mid→High→天井 と振る。
        /// これで「強い遺物を持ち込めば深く潜れるのか」に直接答えが出る。</summary>
        /// <summary>ADR-0010 Verification ①: 技量帯の実測。 **配線方策だけを振る**。
        ///
        /// 条件は基準値測定と完全に同一 (遺物なし・挑戦0pt・ペルソナ Standard・5層裏ボス遮断)。
        /// **同一の runIdx を各アームで使い回す** ので、 ラン単位で対応が付く ──
        /// 平均どうしを引き算するのではなく、 改善/悪化の件数を McNemar にかける
        /// (平均比較は分散に埋もれて数ポイントの差を判定できない)。
        ///
        /// 差が出ないなら本 ADR の目的 (技量帯の構築) を達成していない、 という判定に使う。</summary>
        private IEnumerator RunWiringSkillCompare()
        {
            var arms = (wiringSkillCompareArms != null && wiringSkillCompareArms.Length > 0)
                     ? wiringSkillCompareArms
                     : new[] { WiringSkill.Optimal, WiringSkill.Super };

            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            var prevSkill = wiringSkill;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = true;

            // 挑戦スコアを載せるときは**固定難易度スイープと同条件**へ揃える (遺物 理論最良)。
            //   0pt の基準値条件と混ぜると「遺物なしの 30pt」という別物を測ってしまう。
            _pendingRelicExplicit = true;   // 0pt 側は **遺物なしを強制** する (null = 触らない ではない)
            var cmpRelic = wiringSkillCompareScore > 0
                ? RelicPresets.Build(RelicPresets.Preset.TheoreticalBestCursed) : null;
            // **0pt でも必ず非 null を渡す。** null は「0pt」ではなく「挑戦設定に触らない」を
            //   意味するので、 前の状態が残る。 単軸スイープは常に代入していて、
            //   それが両者で唯一残った前処理の差だった (2026-08-10 の乖離調査)。
            var cmpLoadout = AscensionLoop.BuildLoadoutAtScore(wiringSkillCompareScore);

            int runs = Mathf.Max(20, wiringSkillCompareRuns);
            ResetScreenProgress(arms.Length * runs, "技量比較");
            // ラン単位の結果 [アーム][ラン] = クリアした層 (死亡なら到達層-1)
            var cf = new int[arms.Length][];
            var reachAll = new int[arms.Length][];
            var elapsed = new double[arms.Length];

            var sb = new StringBuilder();
            sb.AppendLine("=== 配線技量スイープ (ADR-0010 Verification ①) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(wiringSkillCompareScore > 0
                ? $"遺物 TheoreticalBestCursed / 挑戦{wiringSkillCompareScore}pt / ペルソナ Standard 固定 / 5層裏ボス遮断 / 各 {runs} ラン"
                : $"遺物なし / 挑戦0pt / ペルソナ Standard 固定 / 5層裏ボス遮断 / 各 {runs} ラン");
            sb.AppendLine("**全アームで同一 runIdx** ＝ ラン単位で対応が付く (McNemar 可能)");
            sb.AppendLine(_stateLineAtStart);
            _probeTrace = "";
            sb.AppendLine();

            for (int ai = 0; ai < arms.Length; ai++)
            {
                wiringSkill = arms[ai];
                curArmIndex = ai + 1; curArmCount = arms.Length;
                curRunsPerArm = runs; curArmLabel = wiringSkill.ToString();
                usePowerBudget = wiringSkillCompareBudget == null
                              || ai >= wiringSkillCompareBudget.Length
                              || wiringSkillCompareBudget[ai];
                cf[ai] = new int[runs];
                reachAll[ai] = new int[runs];
                var t0 = DateTime.Now;

                for (int i = 0; i < runs; i++)
                {
                    curRunInArm = i + 1;
                    // **同じ runIdx を全アームで使う。** これがペア比較の土台。
                    //   2026-08-10: シード帯を単軸スイープ (60000+i) へ揃えた。 同一条件のはずの
                    //   両スイープで基準値が 53.3% と 16.6% に割れ、 差の残りがシード帯だけだったため。
                    //   **0 始まりの低い runIdx で乱数列が縮退している疑い**を切り分ける。
                    int runIdx = wiringSkillCompareIndexBase + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    // **UnityEngine.Random もラン単位で固定する** (2026-08-10)。
                    //   GameRng は BeginRun で完全にリセットされるが、 UnityEngine.Random の
                    //   静的状態はラン跨ぎで残り、 **それまでに何回引かれたか**に依存する。
                    //   スイープ内は呼び出し履歴が同一なので再現する (ノイズ床 0/300) 一方、
                    //   別のスイープ関数から回すと履歴が変わって同一シードのランが割れていた。
                    UnityEngine.Random.InitState(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    _pendingPresetRelic = cmpRelic;
                    _pendingChallengeLoadout = cmpLoadout;
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    reachAll[ai][i] = reached;
                    cf[ai][i] = died ? reached - 1 : reached;
                    // 乖離調査 (2026-08-10): 同一 runIdx のランが両スイープで同じ結果になるか。
                    if (ai == 0 && i < 10)
                        _probeTrace += $" #{runIdx}:{reached}{(died ? "x" : "o")}[{_probeRunConfig}]";

                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                elapsed[ai] = (DateTime.Now - t0).TotalSeconds;
                Debug.Log($"[技量スイープ] {arms[ai]} 完了 ({ai + 1}/{arms.Length}) "
                        + $"{elapsed[ai]:F0}秒 / {elapsed[ai] / runs:F2}秒per ラン");
            }

            // ---- 集計 ----
            sb.AppendLine("技量       | 平均到達層 | 5層クリア | 7層クリア | 秒/ラン");
            for (int ai = 0; ai < arms.Length; ai++)
            {
                double sum = 0; int c5 = 0, c7 = 0;
                for (int i = 0; i < runs; i++)
                {
                    sum += reachAll[ai][i];
                    if (cf[ai][i] >= 5) c5++;
                    if (cf[ai][i] >= 7) c7++;
                }
                string label = arms[ai] + (wiringSkillCompareBudget != null
                    && ai < wiringSkillCompareBudget.Length && wiringSkillCompareBudget[ai] ? "+要求戦力" : "");
                sb.AppendLine($"{label,-14} | {sum / runs,10:F2} | {100.0 * c5 / runs,8:F1}% | "
                            + $"{100.0 * c7 / runs,8:F1}% | {elapsed[ai] / runs,7:F2}");
            }

            // ---- ペア比較 (McNemar) ----
            //   b = 前者だけがクリア / c = 後者だけがクリア。 χ² = (|b−c|−1)² / (b+c) (連続補正)。
            sb.AppendLine();
            sb.AppendLine("【ペア比較 (McNemar・連続補正)】 同一シードのラン単位で改善/悪化を数える");
            sb.AppendLine("比較                     | 指標   | 後者のみ | 前者のみ |    χ² |     p | 到達層 改善/悪化");
            for (int x = 0; x < arms.Length; x++)
                for (int y = x + 1; y < arms.Length; y++)
                {
                    for (int m = 0; m < 2; m++)
                    {
                        int need = m == 0 ? 5 : 7;
                        int b = 0, c = 0;
                        for (int i = 0; i < runs; i++)
                        {
                            bool px = cf[x][i] >= need, py = cf[y][i] >= need;
                            if (px && !py) c++; else if (!px && py) b++;
                        }
                        double chi = (b + c) > 0 ? Math.Pow(Math.Abs(b - c) - 1, 2) / (b + c) : 0.0;
                        double p = ChiSqP1(chi);
                        int up = 0, dn = 0;
                        for (int i = 0; i < runs; i++)
                        {
                            if (reachAll[y][i] > reachAll[x][i]) up++;
                            else if (reachAll[y][i] < reachAll[x][i]) dn++;
                        }
                        sb.AppendLine($"{arms[x]}→{arms[y],-12} | {need}層   | {b,8} | {c,8} | "
                                    + $"{chi,5:F2} | {p,5:F3} | {up}/{dn}");
                    }
                }
            sb.AppendLine();
            sb.AppendLine($"[先頭10ラン 到達層]{_probeTrace}");
            sb.AppendLine("※ 「後者のみ」が「前者のみ」を上回れば後者が強い。 p<0.05 で有意。");
            sb.AppendLine("※ 到達層 改善/悪化 は同一シードで到達層が上がった/下がったラン数。");

            wiringSkill = prevSkill;
            usePowerBudget = true;
            itemPickMode = prevPickMode;
            _ascensionPersonaLock = false;
            _pendingPresetRelic = null;
            _pendingRelicExplicit = false;
            _pendingChallengeLoadout = null;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;

            _skillSweepReport = sb.ToString();
            Debug.Log($"[技量スイープ] 完了\n{_skillSweepReport}");
        }

        /// <summary>自由度 1 のカイ二乗 上側確率。 誤差関数の近似 (Abramowitz-Stegun 7.1.26) を使う。
        /// p = erfc(sqrt(χ²/2))。 有意判定の目安が出れば十分なので、 これ以上の精度は要らない。</summary>
        private static double ChiSqP1(double chi)
        {
            if (chi <= 0) return 1.0;
            double x = Math.Sqrt(chi / 2.0);
            double t = 1.0 / (1.0 + 0.3275911 * x);
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t
                              - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return Math.Max(0.0, Math.Min(1.0, 1.0 - y));
        }

        private IEnumerator RunRelicPresetSweep()
        {
            // **遺物なしがバランスの基準値** (2026-08-04 決定)。 遺物は周回引継ぎなので初回
            //   プレイヤーは 0 個。 最良遺物を前提に調整すると、 遺物が報酬ではなく
            //   「持っていないと基準に届かない前提条件」になる。 素の 7 層クリア率の目標は 2 割強。
            //   baselineOnly は調整ループ用 ── 1/5 のラン数で基準値だけを速く出す。
            var presets = relicSweepBaselineOnly
                ? new[] { RelicPresets.Preset.None }
                : new[]
                {
                    RelicPresets.Preset.None,
                    RelicPresets.Preset.Mid,
                    RelicPresets.Preset.High,
                    RelicPresets.Preset.TheoreticalBest,
                    RelicPresets.Preset.TheoreticalBestCursed,
                };

            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            _ascensionPersonaLock = true;
            _currentPersona = BuildPersona.Standard;      // ペルソナ固定 = 交絡を断つ
            // 5層裏ボスを遮断する。 レイピアを引いたランだけ 5層の難度が跳ね上がり、
            // 基準値の分散源になるため (2026-08-05)。 **基準値測定のときだけ。**
            // 2026-08-15: Begin() が既定 true を敷くようになったので、 非基準アームは
            //   ここで明示的に false へ戻す (遺物の効きを裏ボス込みで見るのが本来の意図)。
            bool prevHidden5 = GameLoop.GameManager.SuppressLayer5HiddenBoss;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = relicSweepBaselineOnly;

            int runs = Mathf.Max(50, relicSweepRuns);
            ResetScreenProgress(presets.Length * runs, "遺物プリセット");
            var sb = new StringBuilder();
            sb.AppendLine("=== 遺物プリセットスイープ (§15-5) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ペルソナ Standard 固定 / 挑戦スコア 0 固定 / 1 プリセット {runs} ラン");
            sb.AppendLine("決定論シード: 0000 0000 0000 0000");
            sb.AppendLine();
            sb.AppendLine("プリセット             pt/枠 | 平均到達層 | 5層クリア | 7層クリア | 到達層分布 1F〜7F");
            var effRows = new List<string>();

            for (int pi = 0; pi < presets.Length; pi++)
            {
                var preset = presets[pi];
                curArmIndex = pi + 1; curArmCount = presets.Length;
                curRunsPerArm = runs; curArmLabel = preset.ToString();
                var relic = RelicPresets.Build(preset);
                var reach = new int[9];
                int cleared5 = 0, cleared7 = 0;
                double sumFloor = 0;
                // **序盤ファーム効率**をプリセットごとに切り出す。 生存だけでは
                // 「強い遺物で序盤を効率よく回せるのか」に答えられない。
                int recBase = _records.Count;
                CombatSystem.CombatManager.ResetFloorDamage();

                for (int i = 0; i < runs; i++)
                {
                    curRunInArm = i + 1;
                    // **BeginRun を必ず呼ぶ** (理由は RunChallengeFixedSweep 側のコメント参照)。
                    int runIdx = pi * runs + i;
                    GameLoop.GameRng.BeginRun(runIdx);
                    if (GameLoop.GameRng.IsSeeded)
                        _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                    _currentPersona = BuildPersona.Standard;
                    _pendingPresetRelic = relic;             // RunOne 内の ResetAll 直後に適用される
                    yield return RunOne(runIdx);

                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;
                    int clearedFloor = died ? reached - 1 : reached;
                    reach[Mathf.Clamp(reached, 0, 8)]++;
                    sumFloor += reached;
                    if (clearedFloor >= 5) cleared5++;
                    if (clearedFloor >= 7) cleared7++;

                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }

                int pts = relic != null ? relic.TotalPoints : 0;
                int slots = relic != null ? relic.SlotCount : 0;
                var dist = new StringBuilder();
                for (int f = 1; f <= 7; f++) dist.Append($"{reach[f] * 100.0 / runs,5:F1}%");
                sb.AppendLine($"{preset,-22} {pts,2}/{slots} | {sumFloor / runs,10:F2} | "
                            + $"{cleared5 * 100.0 / runs,8:F1}% | {cleared7 * 100.0 / runs,8:F1}% |{dist}");

                // ── 序盤ファーム効率 ──
                double gold = 0, buys = 0; int n = 0;
                for (int r = recBase; r < _records.Count; r++)
                { gold += _records[r].totalGoldGained; buys += _records[r].shopPurchases; n++; }
                var fd = CombatSystem.CombatManager.FloorDamage;
                // 1〜3F の通常+エリート戦だけを見る (ボスは長さの性質が違うので除く)
                double atk = 0, fights = 0, dmgSum = 0;
                for (int f = 1; f <= 3; f++)
                    for (int k = 0; k < 2; k++)
                    { atk += fd[f, k, 0]; fights += fd[f, k, 6]; dmgSum += fd[f, k, 1]; }
                effRows.Add($"{preset,-22} {pts,2}/{slots} | {(n > 0 ? gold / n : 0),9:F1} | "
                          + $"{(n > 0 ? buys / n : 0),8:F2} | {(fights > 0 ? atk / fights : 0),9:F2} | "
                          + $"{(atk > 0 ? dmgSum / atk : 0),10:F1}");
            }

            sb.AppendLine();
            sb.AppendLine("=== 経済と序盤効率 (GOLD/購入は**ラン全体**・ターン/与ダメは1〜3F通常+エリートのみ) ===");
            sb.AppendLine("プリセット             pt/枠 | GOLD総獲得 | 購入回数 | 平均ターン | 1攻撃与ダメ");
            foreach (var row in effRows) sb.AppendLine(row);

            sb.AppendLine();
            sb.AppendLine("※ 遺物以外は完全に同一。 深度が動かないなら「強い遺物で押し切る」は成立しない。");
            _relicSweepReport = sb.ToString();

            _pendingPresetRelic = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
            GameLoop.GameManager.SuppressLayer5HiddenBoss = prevHidden5;
        }

        /// <summary>周回モード (§15-5): ペルソナごとに独立したラインを走らせる。
        ///
        /// 1 ラインの中では **ペルソナを固定**する ── 遺物はそのペルソナの重みで蓄積するので、
        /// 途中で入れ替わると効用の比較が意味を失う。
        ///
        /// ラン前に遺物と挑戦構成を State へ入れ、 ラン後に遺物を引いて乗り換え判定と
        /// 難易度判定を行う。 到達層とクリア判定は **実ゲームの結果** (_cur) を使う。</summary>
        /// <summary>サマリーに出す計装 static をバッチ頭で落とす。
        ///
        /// **プロセス横断の static なので、 落とさないと前バッチの値が混ざる**
        /// (2026-08-10 の <c>_combatSeq</c> と同じ罠)。 かつては <see cref="RunAscension"/> だけが
        /// この処理を持っていて、 **通常バッチは落としていなかった** ── 同じ Editor セッションで
        /// 2 回目以降に回したバッチの【役の実測】【充電経済】は、 前のバッチと**プールされた値**
        /// だったことになる。 両方の入口から必ずここを通す。</summary>
        private void ResetBatchStatics()
        {
            CombatSystem.YachtRoleEffects.ResetStats();
            GameLoop.RunChronicle.ResetCounts();
            ItemSystem.PassiveSkills.CombatContext.ResetChargeStats();
            CombatSystem.CombatManager.PassiveUpkeepDue = CombatSystem.CombatManager.PassiveUpkeepPaid = 0;
            System.Array.Clear(CombatSystem.CombatManager.RerollPerTurn, 0, CombatSystem.CombatManager.RerollPerTurn.Length);
            System.Array.Clear(CombatSystem.CombatManager.RerollPerTurnYacht, 0, CombatSystem.CombatManager.RerollPerTurnYacht.Length);
            CombatSystem.CombatManager.ResetFirstRollStats();
            System.Array.Clear(CombatSystem.CombatManager.RerollStats, 0,
                               CombatSystem.CombatManager.RerollStats.Length);
            CombatSystem.CombatManager.ResetDamageBreakdown();
            SuperCombatAI.ResetSearchStats();
            ResetNavStats();
            AutoTest.FamilyTierStats.Reset();
        }

        /// <summary>実ランから5F到達ビルドを採取し、全(武器×ダイス)で5Fボス勝率を総当たり計測。</summary>
        private IEnumerator RunBoss5Sweep(GameManager gm)
        {
            // --- Phase A: 実ランから「5F到達時ビルド」を採取 ---
            _simHarvestArmed = true;
            int attempts = 0;
            int cap = Mathf.Max(runCount, simSampleBuilds * 30);
            ResetScreenProgress(cap, "5Fビルド採取");
            while (_simBases.Count < simSampleBuilds && attempts < cap)
            {
                curRunInArm = attempts + 1;
                yield return RunOne(attempts);
                attempts++;
                yield return null;
            }
            _simHarvestArmed = false;
            Debug.Log($"[AutoRunner] ビルド採取: {_simBases.Count}件 / {attempts}ラン試行");
            if (_simBases.Count == 0)
            {
                _simReport = "5F到達ビルドを採取できませんでした（到達率0）。simSampleBuilds やメタ設定を見直してください。\n";
                yield break;
            }

            // --- 有効な武器/ダイスIDに絞る ---
            var db = ItemDatabase.Instance;
            var weapons = new List<string>();
            foreach (var w in simWeapons) if (db?.GetItem(w) != null) weapons.Add(w);
            var dice = new List<string>();
            foreach (var d in simDice) if (db?.GetItem(d) != null) dice.Add(d);
            if (weapons.Count == 0 || dice.Count == 0)
            {
                _simReport = "有効な武器/ダイスIDがありません。simWeapons / simDice を確認してください。\n";
                yield break;
            }

            // --- クリーンな Run を1つ用意し、毎試行で土台ビルドを上書き ---
            // 2026-06-28: シミュレータも職業をランダム化 (build シミュレーションでクラス間バランスも測定)
            GameLoop.GameManager.SelectedClass = (GameLoop.ClassType)GameLoop.GameRng.RangeAuto("AutoRunner.2", 0, 4);
            gm.StartNewRun();

            // --- Phase B: 全(武器×ダイス)スイープ ---
            var winPct = new Dictionary<string, double>();
            int comboCount = weapons.Count * dice.Count;
            int comboIdx = 0;
            foreach (var weapon in weapons)
            {
                foreach (var d in dice)
                {
                    int wins = 0;
                    for (int t = 0; t < simTrialsPerCombo; t++)
                    {
                        var b = _simBases[t % _simBases.Count];
                        var run = gm.Run;
                        run.playerHP = simBaseHP;
                        run.playerMaxHP = simBaseHP;
                        run.weaponPlus = b.weaponPlus;
                        run.limitBreakStage = b.limitBreakStage;
                        run.equippedWeaponId = weapon;
                        run.equippedDiceId = d;
                        run.ownedPassiveItems = new List<string>(b.passives);

                        var res = gm.SimulateBossFight(simBossFloor);
                        if (res.playerWon) wins++;

                        if ((t & 127) == 0) yield return null; // フレーム譲り
                    }
                    winPct[weapon + "|" + d] = 100.0 * wins / Mathf.Max(1, simTrialsPerCombo);
                    comboIdx++;
                    if ((comboIdx & 3) == 0) Debug.Log($"[AutoRunner] スイープ {comboIdx}/{comboCount}");
                    yield return null;
                }
            }

            _simReport = BuildSweepReport(weapons, dice, winPct);
        }

        /// <summary>ダイスIDの短縮表示コード（マトリクス列見出し用）。</summary>
        private static string DiceCode(string id)
        {
            switch (id)
            {
                case "dice_wood": return "Wo";
                case "dice_bone": return "Bo";
                case "dice_copper": return "Co";
                case "dice_iron": return "Ir";
                case "dice_biased": return "Bi";
                case "dice_gem": return "Ge";
                case "dice_flame": return "Fl";
                case "dice_stable": return "Sb";
                case "dice_twinsnake": return "Tw";
                case "dice_star": return "Sr";
                case "dice_destiny": return "De";
                case "dice_greed": return "Gr";
                case "dice_moroha": return "Mo";
                case "dice_perfection": return "Pf";
                default: return id.StartsWith("dice_") ? id.Substring(5, Math.Min(2, id.Length - 5)) : id;
            }
        }

        private string BuildSweepReport(List<string> weapons, List<string> dice, Dictionary<string, double> winPct)
        {
            var sb = new StringBuilder();
            sb.AppendLine("================ 5Fボス 勝率スイープ ================");
            sb.AppendLine($"日時          : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"対象ボス      : boss_layer{simBossFloor}");
            sb.AppendLine($"採取ビルド数  : {_simBases.Count}（実ランの5F到達時パッシブ/強化段階を土台。武器・ダイスのみ差し替え）");
            sb.AppendLine($"戦闘開始HP    : {simBaseHP}（固定。採取ビルドの現在HPは不使用）");
            sb.AppendLine($"試行/組合せ   : {simTrialsPerCombo}（採取ビルドをラウンドロビンで均等使用）");
            sb.AppendLine("※消費アイテムは不使用（武器×ダイスの素の勝率を比較）");
            sb.AppendLine();

            // 凡例
            sb.AppendLine("---- ダイス略号 ----");
            var legend = new StringBuilder("  ");
            foreach (var d in dice) legend.Append($"{DiceCode(d)}={d.Replace("dice_", "")}  ");
            sb.AppendLine(legend.ToString());
            sb.AppendLine();

            // マトリクス（行=武器, 列=ダイス, 値=勝率%）
            sb.AppendLine("---- 勝率マトリクス（行=武器 / 列=ダイス） ----");
            var header = new StringBuilder();
            header.Append(PadR("武器", 12));
            foreach (var d in dice) header.Append(PadL(DiceCode(d), 5));
            sb.AppendLine(header.ToString());
            foreach (var w in weapons)
            {
                var row = new StringBuilder();
                row.Append(PadR(TruncDisp(w, 11), 12));
                foreach (var d in dice)
                {
                    double v = winPct.TryGetValue(w + "|" + d, out var p) ? p : -1;
                    row.Append(PadL(v < 0 ? "-" : v.ToString("F0") + "%", 5));
                }
                sb.AppendLine(row.ToString());
            }
            sb.AppendLine();

            // 上位/下位 組み合わせ
            var all = new List<KeyValuePair<string, double>>(winPct);
            all.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.AppendLine("---- 勝率トップ10（強コンボ） ----");
            for (int i = 0; i < all.Count && i < 10; i++)
            {
                var k = all[i].Key; int bar = k.IndexOf('|');
                sb.AppendLine($"  {PadR(k.Substring(0, bar), 12)}{PadR(k.Substring(bar + 1).Replace("dice_", ""), 14)}{PadL(all[i].Value.ToString("F0") + "%", 5)}");
            }
            sb.AppendLine();
            sb.AppendLine("---- 勝率ワースト10（弱コンボ） ----");
            for (int i = all.Count - 1; i >= 0 && i >= all.Count - 10; i--)
            {
                var k = all[i].Key; int bar = k.IndexOf('|');
                sb.AppendLine($"  {PadR(k.Substring(0, bar), 12)}{PadR(k.Substring(bar + 1).Replace("dice_", ""), 14)}{PadL(all[i].Value.ToString("F0") + "%", 5)}");
            }
            sb.AppendLine();
            return sb.ToString();
        }

        // ===== 計測フック =====
    }
}

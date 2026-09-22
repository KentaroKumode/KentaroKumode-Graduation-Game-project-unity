using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest.ParallelSweep
{
    /// <summary>r9/r10 比較の並列ワーカー。 Standalone Player の入口。
    ///
    /// <para><b>ゲームの計算は一切しない。</b> 通常の AutoRunner を組み立てて、
    /// 担当アーム・担当シード区間を走らせ、 集計を JSON で返すだけ。
    /// 逐次スイープと同じ <c>RunRankMarginArm</c> を通る。</para>
    ///
    /// <para><b>分割して良い根拠。</b> ラン i の結果は <c>runIdx</c> だけの関数である
    /// (<c>Tools/digest_cmp.py</c> で長さの違う 2 バッチの digest が 1,000/1,000 一致)。
    /// この前提が崩れると担当区間の並びで結果が変わるので、 親は必ず
    /// 逐次版と digest を突き合わせてから数字を採用すること。</para></summary>
    public sealed class RankMarginWorkerBootstrap : MonoBehaviour
    {
        public const string JobArgument = "--rank-margin-job";

        /// <summary>このプロセスが並列ワーカーか (2026-09-22)。 起動引数に <see cref="JobArgument"/> があれば true。
        /// <b>ワーカーは測定専用</b> ── 共有ファイル (BALANCE_CHANGELOG など) へ書いてはいけない。
        /// 22 プロセスが同じファイルへ追記して共有違反を起こし、 ビルド直下に 18MB 溜まっていた。</summary>
        public static bool IsWorkerProcess
            => !string.IsNullOrEmpty(FindArgument(Environment.GetCommandLineArgs(), JobArgument));

        private RankMarginWorkerJob _job;
        private bool _terminalWritten;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfWorker()
        {
            string path = FindArgument(Environment.GetCommandLineArgs(), JobArgument);
            if (string.IsNullOrEmpty(path)) return;
            var go = new GameObject("[RankMargin Worker]");
            DontDestroyOnLoad(go);
            go.AddComponent<RankMarginWorkerBootstrap>().LoadAndStart(path);
        }

        private void LoadAndStart(string jobPath)
        {
            try
            {
                _job = JsonUtility.FromJson<RankMarginWorkerJob>(
                    File.ReadAllText(Path.GetFullPath(jobPath), Encoding.UTF8));
                if (_job == null) throw new InvalidDataException("job is null");
                if (_job.runs <= 0) throw new InvalidDataException("runs must be positive");
                if (string.IsNullOrWhiteSpace(_job.responsePath)
                    || string.IsNullOrWhiteSpace(_job.outputRoot))
                    throw new InvalidDataException("worker paths missing");

                Dictionary<MetaProgression.MetaPanelKind, int> spec = BuildSpec(_job);

                Directory.CreateDirectory(Path.GetDirectoryName(
                    Path.GetFullPath(_job.responsePath)) ?? ".");
                Directory.CreateDirectory(Path.GetFullPath(_job.outputRoot));

                var runnerObject = new GameObject("[RankMargin AutoRunner]");
                DontDestroyOnLoad(runnerObject);
                var runner = runnerObject.AddComponent<AutoRunner>();
                runner.autoStart = false;
                RankMarginWorkerConfig.Apply(runner, _job.configKeys, _job.configValues);
                // **worker 内ではスレッド並列を切る** (2026-09-19)。 並列化はプロセス数 (≒コア数) で
                //   既に取っている。 Super は判断ごとに コア数−1 本、 Optimal の配線列挙は最大 8 本の
                //   Parallel.For を立てるので、 22 プロセスが同時にやると 500 本超のスレッドが
                //   24 コアを奪い合う。 実測で Super は単独 0.5 秒/ラン が、 22 並列では
                //   3 分経っても 1 ラン も終わらなかった (50 ランごとの進捗が 0 のまま)。
                runner.superAI.maxParallelism = 1;
                runner.optimalWiringMaxParallelism = 1;
                if (runner.logWiringDiff)
                    runner.wiringDiffPath = Path.Combine(Path.GetDirectoryName(_job.responsePath) ?? ".", "wiring_diff.csv");

                // **本体ループは空にする。** 逐次版と同じく、 アームは RunBatch 後段の
                //   分岐から回す。 ここで runCount を立てると素の N ラン が余計に走る。
                runner.runCount = 0;
                // 学習は絶対に更新しない。 ワーカーは凍結スナップショットを読むだけで、
                //   書き戻すと**プロセスごとに違う学習が育つ**。
                runner.learnBotAi = false;
                runner.learnTier = false;
                runner.tuneBosses = false;
                runner.writeRunsJsonl = false;
                runner.exitPlayModeWhenDone = false;
                runner.stepsPerYield = 1000;
                runner.runsPerYield = 25;
                runner.productionOutputRootOverride = Path.GetFullPath(_job.outputRoot);

                // ── 付与試行モード (2026-09-17) ────────────────────────────────
                //   ランダム付与 ITT は Editor 逐次で 60,000 ラン ≈ 1.7 時間 かかる。
                //   アームの実体は RunBatch 後段の分岐なので、 **rankMarginWorkerMode を切って
                //   付与アームだけ回せば**そのまま並列化できる。 出力先は
                //   productionOutputRootOverride でプロセスごとに分かれるので、
                //   grant_trial/*.csv も衝突しない (後段でマージする)。
                //
                //   **配点 (spec) はこちらでも効かせる。** メタ配分は付与の効果量に効くので、
                //   逐次版 (Standard/Balanced) と同条件でなければ比較できない。
                if (runner.randomGrantTrial)
                {
                    runner.rankMarginWorkerMode = false;
                    runner.rankMarginWorkerSpec = spec;
                    runner.randomGrantRuns = _job.runs;
                    runner.BatchCompleted += OnGrantBatchCompleted;
                    GameLoop.LastStand.ResetStats();
                    _effectiveState = RankMarginWorkerConfig.DescribeEffective(runner);
                    WriteProgress(0, "started");
                    runner.Begin();
                    return;
                }

                runner.rankMarginWorkerMode = true;
                runner.rankMarginWorkerLabel = _job.label ?? "";
                runner.rankMarginWorkerSpec = spec;
                runner.rankMarginWorkerRuns = _job.runs;
                runner.rankMarginWorkerSeedStart = _job.seedStart;
                runner.RankMarginArmCompleted += OnArmCompleted;
                runner.RankMarginArmProgress += done => WriteProgress(done, "running");

                GameLoop.LastStand.ResetStats();
                GameLoop.HopePayment.ResetStats();
                MetaProgression.VaultBank.ResetStats();
                CombatSystem.GuardDiag.Reset();
                CombatSystem.SkillDiag.Reset();
                CombatSystem.ShieldDiag.Reset();
                CombatSystem.DmgSourceDiag.Reset();
                AutoTest.SuperCombatAI.ResetProf();
                GameLoop.GameManager.PlunderDrops = 0;
                GameLoop.GameManager.ValorEliteDrops = 0;
                GameLoop.GameManager.ValorEliteFights = 0;
                InventorySystem.Shop.ShopManager.RobberyAttempts = 0;
                Array.Clear(InventorySystem.Shop.ShopManager.RobberyByFloor, 0,
                    InventorySystem.Shop.ShopManager.RobberyByFloor.Length);
                GameLoop.GameManager.RobberyWins = 0;
                GameLoop.GameManager.RobberyLosses = 0;
                GameLoop.GameManager.RobberyLootTotal = 0;
                GameLoop.GameManager.RobberyLootApplied = 0;
                GameLoop.GameManager.ResetBossEntryStats();
                GameLoop.GameManager.ResetGateStats();
                InventorySystem.PassiveSkills.CombatContext.ResetAttackBonusStats();
                GameLoop.SwordDanceSet.ResetStats();
                GameLoop.CombatRewards.ResetStats();
                AutoTest.AutoRunner.ResetGoldPressureStats();
                AutoTest.AutoRunner.ResetDangerStats();
                AutoTest.AutoRunner.ResetEndGoldStats();
                AutoTest.AutoRunner.ResetSurplusExitStats();
                AutoTest.AutoRunner.ResetRerollPathStats();
                AutoTest.AutoRunner.ResetRerollRuleStats();
                AutoTest.AutoRunner.ResetShopExitStats();
                GameLoop.GameManager.ResetRerollOutcome();
                InventorySystem.Shop.ShopManager.ResetRerollFailStats();
                GameLoop.GoldIncome.ResetStats();
                GameLoop.RunState.ResetConsumableStats();
                InventorySystem.Helpers.PassiveSourceAudit.Reset();
                InventorySystem.Shop.ShopManager.ResetSlotStats();
                InventorySystem.Shop.ShopManager.ResetSaleStats();
                InventorySystem.Helpers.PassiveAddHelper.ResetAcquiredStats();
                GameLoop.Consumables.ResetVescaPhaseStats();
                CombatSystem.BossDmgDiag.ResetPhaseEntry();

                // **Begin() の直前に読む。** Begin() は CombatManager の static を
                //   このフィールドで上書きするので、 ここの値がそのまま戦闘経路を決める。
                _effectiveState = RankMarginWorkerConfig.DescribeEffective(runner);

                WriteProgress(0, "started");
                runner.Begin();
            }
            catch (Exception ex)
            {
                WriteFailure("BOOTSTRAP_FAILED", ex.GetType().Name + ": " + ex.Message);
                Application.Quit(2);
            }
        }

        /// <summary>配点を組む。 <b>逐次版と同じ関数を呼ぶ</b> ── 写すと必ずずれる。</summary>
        private static Dictionary<MetaProgression.MetaPanelKind, int> BuildSpec(
            RankMarginWorkerJob job)
        {
            // **配分の直接指定が最優先。** 焦点 1 本の形では書けない配分 (Balanced から
            //   1 軸だけ −1 等) を測るための注入口。
            if (!string.IsNullOrWhiteSpace(job.rankSpec))
            {
                var parsed = MetaAllocationPresets.ParseSpec(job.rankSpec);
                if (parsed.Count == 0)
                    throw new InvalidDataException("rankSpec を解釈できない: " + job.rankSpec);
                return parsed;
            }
            if (string.IsNullOrEmpty(job.focusTrack))
                return MetaAllocationPresets.Ranks(MetaAllocationPresets.Preset.Balanced);
            if (!Enum.TryParse(job.focusTrack, true,
                out MetaProgression.MetaPanelKind focus))
                throw new InvalidDataException("unknown track: " + job.focusTrack);
            return AutoRunner.BuildRankMarginArmSpec(focus, job.focusRank);
        }

        private string _effectiveState = "";

        /// <summary>付与試行モードの終了。 <b>bandScores は返さない</b> ──
        /// ITT の入力は grant_trial/*.csv の割り当て行であって、 このプロセスの
        /// クリア率ではない。 driver がプロセスの終了を待つためだけの response を書く。</summary>
        private void OnGrantBatchCompleted(string dir)
        {
            if (_terminalWritten) return;
            _terminalWritten = true;
            try
            {
                var response = new RankMarginWorkerResponse
                {
                    label = _job.label ?? "",
                    seedStart = _job.seedStart,
                    runs = _job.runs,
                    bandScores = new int[0],
                    effectiveState = _effectiveState,
                };
                File.WriteAllText(Path.GetFullPath(_job.responsePath),
                    JsonUtility.ToJson(response, true), new UTF8Encoding(false));
                WriteProgress(_job.runs, "done");
            }
            catch (Exception ex)
            {
                WriteFailure("GRANT_RESPONSE_FAILED", ex.GetType().Name + ": " + ex.Message);
                Application.Quit(2);
                return;
            }
            Application.Quit(0);
        }

        private void OnArmCompleted(AutoRunner.RankMarginArmTally tally)
        {
            if (_terminalWritten) return;
            _terminalWritten = true;
            FindObjectOfType<AutoRunner>()?.CloseWiringDiff();
            // バッチ中は logEnabled=false なので Debug.Log は届かない。 結果の隣へファイルで残す。
            try
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(_job.responsePath) ?? ".", "super_prof.txt"),
                                  AutoTest.SuperCombatAI.DescribeProf());
            }
            catch { }
            try
            {
                // [計装] ヴェスカ連戦の段別 突入数 / 突入時 残HP%。
                //   BossDmgDiag は集めていたのに DumpPhaseEntry がどこからも呼ばれておらず、
                //   データが出口を持っていなかった。
                var _phaseN = new long[4];
                var _phaseHp = new double[4];
                CombatSystem.BossDmgDiag.ReadPhaseEntry(_phaseN, _phaseHp);

                RankMarginWorkerResponse response = tally == null
                    ? Failed("ARM_RETURNED_NULL", "")
                    : new RankMarginWorkerResponse
                    {
                        label = _job.label ?? "",
                        seedStart = _job.seedStart,
                        runs = _job.runs,
                        valid = tally.valid,
                        clears = tally.clears,
                        stageSum = tally.stageSum,
                        bandScores = tally.bandScores.ToArray(),
                        lambdaRunDeaths = tally.lambdaRunDeaths,
                        digests = tally.digests.ToArray(),
                        completedNormally = true,
                        failureCode = "",
                        buildFingerprint = _job.buildFingerprint ?? "",
                        effectiveState = _effectiveState,
                        torchRevivals = GameLoop.LastStand.RevivalCount[0],
                        lastStandRevivals = GameLoop.LastStand.RevivalCount[1],
                        fleuretRevivals = GameLoop.LastStand.RevivalCount[2],
                        revivalHpRestored = GameLoop.LastStand.RevivalHpRestored,
                        hopePayments = GameLoop.HopePayment.PaymentCount,
                        hopeSpent = GameLoop.HopePayment.HopeSpentTotal,
                        vaultDoublings = MetaProgression.VaultBank.Doublings,
                        vaultGoldGained = MetaProgression.VaultBank.GoldGainedTotal,
                        vaultCapHits = MetaProgression.VaultBank.CapHits,
                        guardAtkBefore = CombatSystem.GuardDiag.AtkBefore,
                        guardAtkAfter = CombatSystem.GuardDiag.AtkAfter,
                        guardBlockSum = CombatSystem.GuardDiag.BlockSum,
                        guardLossBase = CombatSystem.GuardDiag.LossBase,
                        guardAttacks = CombatSystem.GuardDiag.Attacks,
                        guardFullyBlocked = CombatSystem.GuardDiag.FullyBlocked,
                        skillFights = (long[])CombatSystem.SkillDiag.Fights.Clone(),
                        skillDeaths = (long[])CombatSystem.SkillDiag.Deaths.Clone(),
                        skillTurns = (long[])CombatSystem.SkillDiag.Turns.Clone(),
                        skillHpLostPct = (double[])CombatSystem.SkillDiag.HpLostPct.Clone(),
                        valorEliteDrops = GameLoop.GameManager.ValorEliteDrops,
                        valorEliteFights = GameLoop.GameManager.ValorEliteFights,
                        plunderDrops = GameLoop.GameManager.PlunderDrops,
                        robberyAttempts = InventorySystem.Shop.ShopManager.RobberyAttempts,
                        robberyWins = GameLoop.GameManager.RobberyWins,
                        robberyLosses = GameLoop.GameManager.RobberyLosses,
                        robberyLootTotal = GameLoop.GameManager.RobberyLootTotal,
                        robberyByFloor = (long[])InventorySystem.Shop.ShopManager.RobberyByFloor.Clone(),
                        bossEntryCount = (long[])GameLoop.GameManager.BossEntryCount.Clone(),
                        bossEntryHope = (long[])GameLoop.GameManager.BossEntryHope.Clone(),
                        bossEntryHopeTier = (long[])GameLoop.GameManager.BossEntryHopeTierSum.Clone(),
                        bossEntryConsumables = (long[])GameLoop.GameManager.BossEntryConsumables.Clone(),
                        bossEntryHpPct = (long[])GameLoop.GameManager.BossEntryHpPct.Clone(),
                        bossEntryPassives = (long[])GameLoop.GameManager.BossEntryPassives.Clone(),
                        bossEntryPower = (long[])GameLoop.GameManager.BossEntryPower.Clone(),
                        robberyLootApplied = GameLoop.GameManager.RobberyLootApplied,
                        gateReached = GameLoop.GameManager.GateReached,
                        gateBloodPaid = GameLoop.GameManager.GateBloodPaid,
                        gateRelicsPaid = GameLoop.GameManager.GateRelicsPaid,
                        gateTransferPaid = GameLoop.GameManager.GateTransferPaid,
                        gateHopeBefore = GameLoop.GameManager.GateHopeBefore,
                        gateHopeAfter = GameLoop.GameManager.GateHopeAfter,
                        gateMaxHpPaid = GameLoop.GameManager.GateMaxHpPaid,
                        gateRelicsBurned = GameLoop.GameManager.GateRelicsBurned,
                        // [計装] atkBase の「パッシブ加算」を誰が積んだか。 key=SkillId。
                        damageBreakdown = (double[])CombatSystem.CombatManager.DamageBreakdown.Clone(),
                        damageBreakdown7F = (double[])CombatSystem.CombatManager.DamageBreakdown7F.Clone(),
                        attackBonus7FSkills = System.Linq.Enumerable.ToArray(
                            InventorySystem.PassiveSkills.CombatContext.AttackBonusBySkill7F.Keys),
                        attackBonus7FAmounts = System.Linq.Enumerable.ToArray(
                            InventorySystem.PassiveSkills.CombatContext.AttackBonusBySkill7F.Values),
                        attackBonus7FCalls = System.Linq.Enumerable.ToArray(
                            System.Linq.Enumerable.Select(
                                InventorySystem.PassiveSkills.CombatContext.AttackBonusBySkill7F.Keys,
                                k => InventorySystem.PassiveSkills.CombatContext.AttackBonusCalls7F[k])),
                        goldGained = GameLoop.GoldIncome.GainedTotal,
                        consumablesAcquired = GameLoop.RunState.ConsumablesAcquired,
                        combatTurnsTotal = 0,   // 総ターンは tally に無い。 今回の切り分けには不要
                        passiveSourceNames = System.Linq.Enumerable.ToArray(
                            InventorySystem.Helpers.PassiveSourceAudit.Counts.Keys),
                        passiveSourceCounts = System.Linq.Enumerable.ToArray(
                            System.Linq.Enumerable.Select(
                                InventorySystem.Helpers.PassiveSourceAudit.Counts.Values, v => (long)v)),
                        swordDanceTransforms = GameLoop.SwordDanceSet.Transforms,
                        passiveSlotsRolled = InventorySystem.Shop.ShopManager.PassiveSlotsRolled,
                        passiveSlotsLegendary = InventorySystem.Shop.ShopManager.PassiveSlotsLegendary,
                        danceSwaps = InventorySystem.Shop.ShopManager.DanceSwaps,
                        danceOffered = InventorySystem.Shop.ShopManager.DanceOffered,
                        danceAcquired = GameLoop.SwordDanceSet.Acquired,
                        offeredByRarity = (long[])InventorySystem.Shop.ShopManager.OfferedByRarity.Clone(),
                        acquiredByRarity = (long[])InventorySystem.Helpers.PassiveAddHelper.AcquiredByRarity.Clone(),
                        vescaPhaseConsumables = (long[])GameLoop.Consumables.VescaPhaseConsumables.Clone(),
                        vescaPhaseEntries = _phaseN, vescaPhaseHpSum = _phaseHp,
                        swordDanceByFloor = (long[])GameLoop.SwordDanceSet.TransformsByFloor.Clone(),
                        attackBonusSkills = System.Linq.Enumerable.ToArray(
                            InventorySystem.PassiveSkills.CombatContext.AttackBonusBySkill.Keys),
                        attackBonusAmounts = System.Linq.Enumerable.ToArray(
                            InventorySystem.PassiveSkills.CombatContext.AttackBonusBySkill.Values),
                        attackBonusCalls = System.Linq.Enumerable.ToArray(
                            System.Linq.Enumerable.Select(
                                InventorySystem.PassiveSkills.CombatContext.AttackBonusBySkill.Keys,
                                k => InventorySystem.PassiveSkills.CombatContext.AttackBonusCalls[k])),
                    };
                CombatSystem.BossCombatTrace.Flush();
                CombatSystem.ShieldDiag.Export(out response.shieldSrcNames,
                                               out response.shieldSrcAmounts, out response.shieldSrcCounts);
                {
                    var bn = new List<string>(CombatSystem.SkillDiag.ByBoss.Keys);
                    response.bossNames = bn.ToArray();
                    response.bossStats = new long[bn.Count * 4];
                    for (int i = 0; i < bn.Count; i++)
                        Array.Copy(CombatSystem.SkillDiag.ByBoss[bn[i]], 0, response.bossStats, i * 4, 4);
                    var dn = new List<string>(CombatSystem.SkillDiag.BossDmg.Keys);
                    int F = CombatSystem.SkillDiag.BossDmgFields;
                    response.bossDmgNames = dn.ToArray();
                    response.bossDmg = new double[dn.Count * F];
                    for (int i = 0; i < dn.Count; i++)
                        Array.Copy(CombatSystem.SkillDiag.BossDmg[dn[i]], 0, response.bossDmg, i * F, F);
                    response.floorKind = (long[])CombatSystem.SkillDiag.ByFloorKind.Clone();
                    response.floorKindHpLost = (double[])CombatSystem.SkillDiag.ByFloorKindHpLost.Clone();
                    response.eliteReason = (double[])CombatSystem.SkillDiag.EliteReason.Clone();
                    response.finishCombatReentry = CombatSystem.SkillDiag.FinishCombatReentry;
                    var dsAll = CombatSystem.DmgSourceDiag.Stats[0];
                    var dsBoss = CombatSystem.DmgSourceDiag.Stats[1];
                    var dsn = new List<string>(dsAll.Keys);
                    int W = CombatSystem.DmgSourceDiag.Width;
                    response.dmgSrcNames = dsn.ToArray();
                    response.dmgSrcAll = new double[dsn.Count * W];
                    response.dmgSrcBoss = new double[dsn.Count * W];
                    for (int i = 0; i < dsn.Count; i++)
                    {
                        Array.Copy(dsAll[dsn[i]], 0, response.dmgSrcAll, i * W, W);
                        if (dsBoss.TryGetValue(dsn[i], out var bRow)) Array.Copy(bRow, 0, response.dmgSrcBoss, i * W, W);
                    }
                }
                WriteAtomic(_job.responsePath, JsonUtility.ToJson(response, false));
                WriteProgress(response.valid, response.completedNormally ? "completed" : "failed");
            }
            catch (Exception ex)
            {
                Debug.LogError("[RankMarginWorker] 応答の書き出しに失敗: " + ex.Message);
            }
            Application.Quit(0);
        }

        private RankMarginWorkerResponse Failed(string code, string detail)
        {
            return new RankMarginWorkerResponse
            {
                label = _job != null ? (_job.label ?? "") : "",
                seedStart = _job != null ? _job.seedStart : 0,
                runs = _job != null ? _job.runs : 0,
                completedNormally = false,
                failureCode = code + (string.IsNullOrEmpty(detail) ? "" : ": " + detail),
                buildFingerprint = _job != null ? (_job.buildFingerprint ?? "") : "",
            };
        }

        private void WriteFailure(string code, string detail)
        {
            if (_terminalWritten) return;
            _terminalWritten = true;
            try
            {
                if (_job == null || string.IsNullOrWhiteSpace(_job.responsePath)) return;
                WriteAtomic(_job.responsePath, JsonUtility.ToJson(Failed(code, detail), false));
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

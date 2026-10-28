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
    /// <summary>AutoRunner: ログ出力・サマリーの各ブロック・ゲーム体験メトリクス・整列ヘルパー。</summary>
    public partial class AutoRunner
    {
        private string WriteLogs()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string root = ResolveAutoRunOutputRoot();
            // バッチ命名: プロファイルサフィックスを使用 (旧 cowardly/fullmeta + debuff タグは廃止)
            string profileTag = TierOutputSuffix;
            string dir = Path.Combine(root, $"batch_{stamp}_n{_records.Count}_{profileTag}");
            Directory.CreateDirectory(dir);
            PruneOldBatchDirs(root, dir);

            // L1学習: プロファイル別サブディレクトリに分離して累積
            string learningRoot = TierLearningRoot;
            ItemLearningStats.StatsFile learnStats = null;
            Debug.Log($"[AutoRunner] 学習: ボスチューナー={tuneBosses} / Tier={learnTier} / BOT AI={learnBotAi}");
            if (UpdatesTier)
            {
                try
                {
                    learnStats = ItemLearningStats.IngestBatch(learningRoot, _records);
                    File.WriteAllText(Path.Combine(dir, "ai_stats.json"),
                        ItemLearningStats.BuildAiCompact(learnStats), new UTF8Encoding(false));
                }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] L1学習出力失敗: {e.Message}"); }

                // 回帰用ラン単位生データを永続化 (次バッチ起動時に ItemRegression.Recompute が読む)
                try { RunDataLogger.AppendBatch(learningRoot, _records); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] RunDataLogger 失敗: {e.Message}"); }
            }

            // L1.5: イベント選択肢の bandScore 集計を更新 (AIルーチン側)
            // L2自動探索: 今バッチの bandScore で policy を評価し、 次バッチへ向けて1軸摂動
            if (UpdatesAi)
            {
                try { EventChoiceLearningStats.IngestBatch(learningRoot, _records); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] イベント学習失敗: {e.Message}"); }

                try { PolicyExplorer.AssessAndPropose(_records, learningRoot); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] L2探索失敗: {e.Message}"); }
            }

            // L3: ボス難易度オートチューナー (突破率→目標ファネルへ寄せる)。 他の学習と同時実行可。
            if (BossAutoTune)
            {
                try { BossBalanceTuner.AssessAndAdjust(_records, MetaProfileHelper.CurrentDebuffOn, learningRoot, learnStats?.totalBatches ?? 0); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] L3ボス調整失敗: {e.Message}"); }
            }

            // ファイル検索で最新サマリーを開きやすいよう、 summary に profile + 時刻を埋め込む
            string summaryName = $"summary_{TierOutputSuffix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            _lastSummaryPath = Path.Combine(dir, summaryName);
            File.WriteAllText(_lastSummaryPath,
                BuildSummary() + (learnStats != null ? "\n" + ItemLearningStats.BuildHumanLiftTable(learnStats) : ""),
                new UTF8Encoding(false));
            if (writeRunsJsonl)
                File.WriteAllText(Path.Combine(dir, "runs.jsonl"), BuildJsonl(), new UTF8Encoding(false));
            if (writeDetailLog)
                File.WriteAllText(Path.Combine(dir, "detail.log"), BuildDetail(), new UTF8Encoding(false));

            // バッチ完了直後に Reload。 Tier更新モードのみ BALANCE_TIER_LIST.md を再生成する。
            // AIルーチン学習モードでは Tier表を凍結 (MDを書かない) が、 BOTが読む in-memory の S/A/B は更新しておく。
            try
            {
                LearnedPriorityProvider.Reload(learningRoot, writeMarkdown: UpdatesTier);
                Debug.Log($"[AutoRunner] WriteLogs後 Reload (MD書込={UpdatesTier}): {LearnedPriorityProvider.LastLoadedSummary}");

                // 2026-06-23: InventoryPower 系ブロックを Tier 表 MD に追記 (Tier更新モードのみ)
                AppendInventoryPowerBlocksToTierList();
            }
            catch (Exception e) { Debug.LogWarning($"[AutoRunner] WriteLogs後 Reload失敗: {e.Message}"); }

            return dir;
        }

        /// <summary>2026-06-23: Power 系ブロック (層別 + Item別寄与) を BALANCE_TIER_LIST_*.md に追記。
        /// 既存の Power セクションが残っていれば置換 (重複防止)。 Tier更新モード/AI学習モード双方で実行。</summary>
        private void AppendInventoryPowerBlocksToTierList()
        {
            try
            {
                if (_records == null || _records.Count == 0)
                {
                    Debug.Log("[AutoRunner] _records 空のため Power 追記スキップ");
                    return;
                }
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                    $"BALANCE_TIER_LIST_{TierOutputSuffix}.md"));
                if (!File.Exists(path))
                {
                    Debug.Log($"[AutoRunner] Tier表MD未生成のため Power 追記スキップ: {path}");
                    return;
                }

                // 既存 Power セクションを除去 (重複防止) ── マーカーは "# インベントリパワー指標"
                string content = File.ReadAllText(path, new UTF8Encoding(false));
                const string marker = "# インベントリパワー指標";
                int markerIdx = content.IndexOf(marker);
                if (markerIdx >= 0)
                {
                    // マーカーの直前にある "---" 区切り (前後の空行込み) も除去したい。 簡易処理: マーカーから -8 文字程度前を捜す。
                    int trimFrom = markerIdx;
                    int sepIdx = content.LastIndexOf("\n---", markerIdx);
                    if (sepIdx > 0 && markerIdx - sepIdx < 20) trimFrom = sepIdx;
                    content = content.Substring(0, trimFrom).TrimEnd('\r', '\n', ' ');
                }

                var sb = new StringBuilder();
                sb.Append(content);
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("# インベントリパワー指標 (Tier 表ベース戦力)");
                sb.AppendLine();
                sb.AppendLine($"> 集計範囲: 本バッチ {_records.Count} ラン");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.Append(BuildInventoryPowerBlock());
                sb.AppendLine();
                sb.Append(BuildItemPowerContributionBlock());
                sb.AppendLine();
                sb.Append(BuildPickRetentionBlock());
                sb.AppendLine("```");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Debug.Log($"[AutoRunner] Power ブロックを Tier 表MDに反映: {path} (置換={markerIdx >= 0})");
            }
            catch (Exception e) { Debug.LogWarning($"[AutoRunner] Power ブロック追記失敗: {e.Message}\n{e.StackTrace}"); }
        }

        private string Pct(int n, int total) =>
            total == 0 ? "0.0%" : (100.0 * n / total).ToString("F1", CultureInfo.InvariantCulture) + "%";

        /// <summary>同一ファイル内で、行動ルーチン別（戦闘貪欲/戦闘回避）に
        /// 集計ブロックを分離して出力する。各ブロックは自己完結の全集計。</summary>
        private string BuildSummary()
        {
            var greedy = _records.FindAll(r => r.profile == "貪欲");
            var averse = _records.FindAll(r => r.profile == "回避");
            var other  = _records.FindAll(r => r.profile != "貪欲" && r.profile != "回避");

            var sb = new StringBuilder();
            string metaLabel = metaPattern switch
            {
                MetaPattern.Cowardly        => "臆病(メタ全リセット)",
                MetaPattern.FullProgression => $"全有効化(メタLv{MetaProgression.MetaBuffTrack.TotalSteps})",
                MetaPattern.Untouched       => "保存値そのまま",
                _                           => metaPattern.ToString(),
            };
            sb.AppendLine("################ AutoRun サマリ ################");
            sb.AppendLine($"日時      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"メタ進行  : {metaLabel}");
            sb.AppendLine($"メタデバフ: {(enableAllDebuffs ? "全ON (Lv1-10, 最高難易度)" : "全OFF")}");
            sb.AppendLine($"実行設定  : メタ={metaBuffMode} / 軸={(sweepAllMetaAxes ? "全軸走査" : metaAllocation.ToString())} / アイテム={itemPickMode}");
            sb.AppendLine($"配線AI    : {wiringSkill}");
            sb.AppendLine($"戦闘経路  : {(useMutualAttackPipeline ? "ADR-0009 相互攻撃" : "旧戦闘経路")} / 役=ADR-0010");
            // **実効状態を必ず印字する。** EditorPrefs 由来のトグルは黙って持ち越されるので、
            //   サマリを後から読んだときに条件が復元できないと測定が丸ごと無駄になる。
            //   (技量帯が別メニューの設定を持ち越したまま走った事故が実際に起きている)
            sb.AppendLine($"5層裏ボス : {(suppressLayer5HiddenBoss ? "遮断" : "出現あり")}"
                        + $" / 軽減無視の盾肩代わり: {(shieldAbsorbsUnmitigable ? "あり (製品挙動)" : "**なし (対照群)**")}");
            // **結果を変えるトグルを 1 行にまとめて必ず出す (2026-08-18)。**
            //   Ultra の portfolio で、 worker 側が既定値のまま走り本ラン側は EditorPrefs 継承、
            //   という不一致が起きた。 どちらも「正常に進行中」と表示され、 4 候補が同一挙動に
            //   なっていたことに **1000ラン×4 を回し終えるまで誰も気付かなかった**。
            //   個別の行から推測させる形 (パーツ抑止は「平均面数」から逆算する等) では
            //   後から条件を突き合わせられない。 **値そのものを書く。**
            if (!string.IsNullOrEmpty(_ultraResumeLine))
                sb.AppendLine("Ultra resume: " + _ultraResumeLine);
            // Ultra の委譲が実際に効いたか。 **落ちても run は完走するので、
            //   印字しないと「一度も効いていない Ultra」を成果と取り違える。**
            if (AutoTest.Ultra.UltraDispatchStats.Offered > 0
                || wiringSkill == WiringSkill.Ultra)
                sb.AppendLine("Ultra委譲  : " + AutoTest.Ultra.UltraDispatchStats.Describe()
                    + (string.IsNullOrEmpty(_ultraLastFallbackReason)
                        ? "" : "  直近の落ちた理由=" + _ultraLastFallbackReason));
            if (_ultraThinkingSecondsTotal > 0f)
            {
                sb.AppendLine($"Ultra思考  : 計 {_ultraThinkingSecondsTotal / 60f:F1}分"
                    + $" ({_ultraThinkingSecondsTotal / Math.Max(1, _records.Count):F1}秒/ラン)"
                    + "  ※番犬からは控除済み");
                // **上書き率だけでは強さを読めない。** 全手0クリアの決定では 2 手の下限が
                //   どちらも 0.000 の同点になり、 選ばれるのは「評価で勝った手」ではなく
                //   「id が辞書順で先の手」。 差がついた件数を必ず並べて出す。
                sb.AppendLine("Ultra評価  : " + AutoTest.Ultra.UltraRolloutEvaluator.DescribeCounters());
            }
            sb.AppendLine($"実効トグル: 相互攻撃={useMutualAttackPipeline}"
                        + $" / パーツ陳列抑止={suppressFacePartOffers}"
                        + $" / 盾肩代わり={shieldAbsorbsUnmitigable}"
                        + $" / メタデバフ全ON={enableAllDebuffs}"
                        + $" / 全軸走査={sweepAllMetaAxes}"
                        + $" / 購入序列={(useIttBeta ? $"ITT(生存N={GrantItt.SurvivalN}/{LearnedPriorityProvider.IttMode})" : "観測regβ")}"
                        + $" / 挑戦spec={(string.IsNullOrEmpty(challengeSpec) ? "(空=スコアから導出)" : challengeSpec)}");
            // 出目パーツの評価係数。 **掃引中はこの行が唯一の「どの値で回したか」の記録**。
            sb.AppendLine($"出目パーツ: 較正={AutoTest.InventoryPower.PowerCalibration:F2}"
                        + $" / 対={AutoTest.InventoryPower.TierGainPair:F1}"
                        + $" 揃={AutoTest.InventoryPower.TierGainTriple:F1}"
                        + $" T4+={AutoTest.InventoryPower.TierGainT4Extra:F1}");
            bool validSeed = GameLoop.GameRng.TryParseSeed(masterSeed, out ulong parsedSeed);
            sb.AppendLine($"決定seed  : {(validSeed ? GameLoop.GameRng.FormatSeed(parsedSeed) : string.IsNullOrWhiteSpace(masterSeed) ? "なし（ランダム）" : $"不正 '{masterSeed}'（ランダムへフォールバック）")}");
            sb.AppendLine($"総ラン数  : {_records.Count}  (戦闘貪欲={greedy.Count} / 戦闘回避={averse.Count})");
            sb.AppendLine(ArmCompositionLine());
            sb.AppendLine(PrimaryObjectiveLine());
            sb.Append(AutoTest.CritRateCensus.Describe());
            // **実効状態を通常バッチでも必ず印字する (2026-08-17)。**
            //   従来はスイープのレポートにしか出しておらず、 通常バッチのサマリーからは
            //   遺物の有無が読めなかった。 `_pendingRelicExplicit` が false のとき
            //   遺物は **「無し」ではなく「触らない」** ＝ 前の状態を引き継ぐ構造なので、
            //   印字が無いと「遺物なしのつもりだった」を後から検証できない。
            //   2026-08-10 に同じ穴で 0pt 基準値が 53.3% / 16.6% に割れている。
            sb.AppendLine(EffectiveStateLine());
            sb.AppendLine("比較軸    : 航行Rankのみ差し替え（消費/ショップ/戦闘実行/イベントは共通固定）");
            sb.AppendLine("################################################");
            if (AutoTest.Ultra.UltraDecisionCensus.Enabled
                && AutoTest.Ultra.UltraDecisionCensus.Runs > 0)
            {
                sb.AppendLine();
                sb.Append(AutoTest.Ultra.UltraDecisionCensus.Report());
            }
            sb.AppendLine();
            sb.Append(BuildPersonaBlock());
            sb.AppendLine();
            sb.Append(BuildMetaAxisBlock());
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(BuildWeaponProgressionBlock());
            sb.AppendLine();
            sb.Append(BuildBossWinRateBlock());
            sb.AppendLine();
            sb.Append(BuildRoleBlock());
            sb.AppendLine();
            sb.Append(BuildTensionCurveBlock());
            sb.AppendLine();
            sb.Append(BuildBuildDiversityBlock());
            sb.AppendLine();
            sb.Append(BuildDeathCauseQualityBlock());
            sb.AppendLine();
            sb.Append(BuildDecisionWeightBlock());
            sb.AppendLine();
            sb.Append(BuildDeathHoardingBlock());
            sb.AppendLine();
            // 2026-06-23: InventoryPower 系ブロックは BALANCE_TIER_LIST_*.md に移植 (summary から削除)
            sb.Append(BuildSummaryBlock("【前半 50% ─ 戦闘貪欲（戦闘マスを最優先で選ぶ）】", greedy));
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(BuildSummaryBlock("【後半 50% ─ 戦闘回避（戦闘以外があれば必ず回避）】", averse));
            if (other.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append(BuildSummaryBlock("【プロファイル未設定（保険）】", other));
            }
            return sb.ToString();
        }

        /// <summary>死亡時に即使用できる資源を残していた割合。
        /// Gold は戦闘中に使えないため抱え落ち本体から分離し、経済余剰として併記する。</summary>
        private string BuildDeathHoardingBlock()
        {
            var deaths = _records.FindAll(r => r.outcome == Outcome.GameOver);
            var pre6 = deaths.FindAll(r => r.deathFloor < 6);
            var floor5 = deaths.FindAll(r => r.deathFloor == 5);
            var floor6Plus = deaths.FindAll(r => r.deathFloor >= 6);
            var sb = new StringBuilder();
            sb.AppendLine("【全死亡・抱え落ち率】");
            sb.AppendLine("  抱え落ち = 死亡時に戦闘使用可能な 回復/与ダメ油/盾 を1個以上所持。Goldは別集計。");

            void Append(string label, List<RunRec> rows)
            {
                int n = rows.Count;
                int any = 0, heal = 0, dmg = 0, shield = 0, provision = 0;
                int gold10 = 0, gold30 = 0, upgrade = 0;
                long consSum = 0, goldSum = 0, matSum = 0;
                foreach (var r in rows)
                {
                    if (r.finalHealCount + r.finalDamageBuffCount + r.finalShieldCount > 0) any++;
                    if (r.finalHealCount > 0) heal++;
                    if (r.finalDamageBuffCount > 0) dmg++;
                    if (r.finalShieldCount > 0) shield++;
                    if (r.finalProvisionCount > 0) provision++;
                    if (r.finalCoins >= 10) gold10++;
                    if (r.finalCoins >= 30) gold30++;
                    if (r.finalUpgradeReady) upgrade++;
                    consSum += r.finalConsumableCount;
                    goldSum += r.finalCoins;
                    matSum += r.finalMaterials;
                }
                sb.AppendLine($"  {label,-12}: {n,4}死 / 戦闘資源あり {Pct(any,n),6}"
                    + $" (回復{Pct(heal,n)}, 油{Pct(dmg,n)}, 盾{Pct(shield,n)})"
                    + $" / 物資薬{Pct(provision,n)} / 強化可能{Pct(upgrade,n)}");
                sb.AppendLine($"  {"",-12}  平均残: 消耗品{(n > 0 ? consSum/(double)n : 0):F2}個"
                    + $" Gold{(n > 0 ? goldSum/(double)n : 0):F1} (10+:{Pct(gold10,n)}, 30+:{Pct(gold30,n)})"
                    + $" 素材{(n > 0 ? matSum/(double)n : 0):F1}");
            }

            Append("全死亡", deaths);
            Append("6層未突入", pre6);
            Append("5層内死亡", floor5);
            Append("6層以降死亡", floor6Plus);
            return sb.ToString();
        }

        /// <summary>役とリロールの実測 (ADR-0010 Verification ④)。
        ///
        /// **数値を調整する前に「効いているか」を数える**。 〈渇き〉の発動率が 6.6% しか
        /// なかったことに、 3 回も数値をいじってから気づいた反省による (§24)。
        /// 実測 0% に近い役があればそれは設計上存在しないのと同じ。</summary>
        private string BuildRoleBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【役の実測 (ADR-0010)】");
            sb.AppendLine(CombatSystem.YachtRoleEffects.DescribeStats());
            sb.Append(CombatSystem.YachtRoleEffects.DescribeYachtFaces());
            // **計装はサマリに書く。** メモリ上の static を実行後に読む方式だと
            //   ヘッドレス実行 (プロセスが終わると静的変数も消える) で採れなくなる。
            {
                // 充電経済。 **リロールが安いのか、 財布が溢れているのか**を分ける。
                var C = typeof(ItemSystem.PassiveSkills.CombatContext);
                long g = ItemSystem.PassiveSkills.CombatContext.ChargeGained;
                long w = ItemSystem.PassiveSkills.CombatContext.ChargeWasted;
                long s = ItemSystem.PassiveSkills.CombatContext.ChargeSpentTotal;
                long rrC = CombatSystem.CombatManager.RerollStats[2];
                double t = System.Math.Max(1, CombatSystem.YachtRoleEffects.TurnsEvaluated);
                sb.Append(AutoTest.FamilyTierStats.Dump());
                sb.AppendLine("【充電経済】");
                sb.AppendLine($"  収入 {g:N0} (1ターン {g / t:F2}) / 上限で破棄 {w:N0} (1ターン {w / t:F2}"
                            + $" ・収入比 {(g + w > 0 ? 100.0 * w / (g + w) : 0):F1}%)");
                sb.AppendLine($"  支出 {s:N0} (1ターン {s / t:F2}) / うちリロール {rrC:N0}"
                            + $" ({(s > 0 ? 100.0 * rrC / s : 0):F1}%)");
                sb.AppendLine($"  収支 {(g - s):N0} (1ターン {(g - s) / t:F2})  ※プラスなら使い切れていない");
                long ud = CombatSystem.CombatManager.PassiveUpkeepDue;
                long up = CombatSystem.CombatManager.PassiveUpkeepPaid;
                sb.AppendLine($"  装備維持費 請求 {ud:N0} (1ターン {ud / t:F2}) / 実収 {up:N0}"
                            + $" (踏み倒し {(ud > 0 ? 100.0 * (ud - up) / ud : 0):F1}%)");

                {
                    long fc = CombatSystem.CombatManager.FirstRollCount;
                    long fy = CombatSystem.CombatManager.FirstRollYacht;
                    sb.AppendLine("  初回ロール (振り直し前)");
                    sb.AppendLine($"    回数 {fc:N0} / 5個同値 {fy:N0} = {(fc > 0 ? 100.0 * fy / fc : 0):F3}%"
                                + "   ※5個の d6 の理論値 0.077%");
                    sb.AppendLine($"    平均ダイス本数 {(fc > 0 ? CombatSystem.CombatManager.FirstRollDiceCountSum / (double)fc : 0):F2}"
                                + $" / 平均面数 {(fc > 0 ? CombatSystem.CombatManager.FirstRollFaceLenSum / (double)fc : 0):F2}"
                                + $" / 面配列なし {CombatSystem.CombatManager.FirstRollNoFaces:N0}");
                    var bc = CombatSystem.CombatManager.FirstRollByDiceCount;
                    var by = CombatSystem.CombatManager.FirstRollYachtByDiceCount;
                    for (int i = 0; i < bc.Length; i++)
                        if (bc[i] > 0)
                            sb.AppendLine($"    {i}本: {bc[i],9} 回 / 極 {by[i],7} = {(100.0 * by[i] / bc[i]),6:F3}%");
                    var fh = CombatSystem.CombatManager.FirstRollFaceHist;
                    long fhTot = 0; foreach (var v in fh) fhTot += v;
                    sb.Append("    出目分布:");
                    for (int i = 0; i < fh.Length; i++)
                        if (fh[i] > 0) sb.Append($" {i}={(fhTot > 0 ? 100.0 * fh[i] / fhTot : 0):F1}%");
                    sb.AppendLine();
                }
                var rpt = CombatSystem.CombatManager.RerollPerTurn;
                var rpy = CombatSystem.CombatManager.RerollPerTurnYacht;
                long rptTot = 0, rpyTot = 0;
                for (int i = 0; i < rpt.Length; i++) { rptTot += rpt[i]; rpyTot += rpy[i]; }
                sb.AppendLine("  リロール回数の分布 (全ターン / 〈極〉成立ターン)");
                for (int i = 0; i < rpt.Length; i++)
                {
                    if (rpt[i] == 0 && rpy[i] == 0) continue;
                    sb.AppendLine($"    {i}{(i == 8 ? "回以上" : "回    ")} {rpt[i],9} ({(rptTot > 0 ? 100.0 * rpt[i] / rptTot : 0),5:F1}%)"
                                + $"  |  {rpy[i],7} ({(rpyTot > 0 ? 100.0 * rpy[i] / rpyTot : 0),5:F1}%)");
                }
            }
            sb.AppendLine(CombatSystem.YachtRoleEffects.DescribeByDice(_records.Count));
            sb.AppendLine(GameLoop.RunChronicle.DescribeRest());
            sb.AppendLine(GameLoop.RunChronicle.DescribeShieldSplit());
            sb.AppendLine(GameLoop.RunChronicle.DescribeShieldBash());
            sb.AppendLine(GameLoop.RunChronicle.DescribeP4());
            sb.AppendLine(GameLoop.RunChronicle.DescribePhaseDuration());
            sb.AppendLine(GameLoop.RunChronicle.DescribePhaseWiring());
            var rr = CombatSystem.CombatManager.RerollStats;
            sb.AppendLine($"  リロール: {rr[0]:N0} 回 / {rr[1]:N0} 個 / 充電 {rr[2]:N0}"
                        + (rr[0] > 0 ? $"  (1回あたり {rr[1] / (double)rr[0]:F2}個 / {rr[2] / (double)rr[0]:F2}充電)" : ""));
            // 先読みが順位を変えているか。 Super 以外の技量では 0 件なので何も出ない。
            sb.Append(SuperCombatAI.DescribeSearch());
            sb.Append(DescribeNavTies());
            sb.Append(DescribeDamageBreakdown7F());
            return sb.ToString();
        }

        /// <summary>ビルド軸ペルソナ別の勝率・平均ラン深度を集計 (2026-07-15 追加)。
        /// useBuildPersonas=false の場合は「【ペルソナ】使用なし」だけ 1 行出して終わる。</summary>
        private string BuildPersonaBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ビルド軸ペルソナ別成績】");
            if (!useBuildPersonas)
            {
                sb.AppendLine("  ペルソナ機能: OFF (全ラン RawTier = Tier表信奉)");
                return sb.ToString();
            }
            sb.AppendLine($"  ペルソナ機能: ON (RawTier率={rawTierRatio:P0}・非RawTier9種から等確率)");
            sb.AppendLine($"  {"ペルソナ",-10} | {"ラン数",5} | {"クリア率",7} | {"6F到達",7} | {"平均R帯",7}");
            var byPersona = new Dictionary<string, List<RunRec>>();
            foreach (var r in _records)
            {
                string key = string.IsNullOrEmpty(r.persona) ? "-" : r.persona;
                if (!byPersona.TryGetValue(key, out var list)) { list = new List<RunRec>(); byPersona[key] = list; }
                list.Add(r);
            }
            foreach (var kv in byPersona)
            {
                var recs = kv.Value;
                if (recs.Count == 0) continue;
                int cleared = 0, reach6 = 0;
                float bandSum = 0f; int bandN = 0;
                foreach (var r in recs)
                {
                    if (r.band != null && r.band.StartsWith("R"))
                    {
                        // 数字部分のみ抽出 ("R8b" 等の接尾辞付きも拾う。旧 TryParse は R8b をパース失敗で全集計から除外していた)
                        int rn = 0, ci = 1;
                        while (ci < r.band.Length && char.IsDigit(r.band[ci])) { rn = rn * 10 + (r.band[ci] - '0'); ci++; }
                        if (rn > 0)
                        { bandSum += rn; bandN++; if (rn >= 11) cleared++; if (rn >= 7) reach6++; } // R11=7層クリア
                    }
                }
                float clr = recs.Count > 0 ? (float)cleared / recs.Count * 100f : 0f;
                float r6  = recs.Count > 0 ? (float)reach6 / recs.Count * 100f : 0f;
                float avgB = bandN > 0 ? bandSum / bandN : 0f;
                sb.AppendLine($"  {kv.Key,-10} | {recs.Count,5} | {clr,6:F1}% | {r6,6:F1}% | R{avgB,5:F1}");
            }
            return sb.ToString();
        }

        /// <summary>メタバフ配分 (整備パネル v6・36pt) 別の成績。
        /// 一斉走査 (sweepAllMetaAxes) 時はラン単位でラウンドロビンしているので、
        /// ここが **軸同士の直接比較表** になる。 単一軸/標準/オフ時は 1 行だけ出る。</summary>
        private string BuildMetaAxisBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【メタバフ配分別成績】");
            if (metaBuffMode == MetaBuffMode.Off)
            {
                sb.AppendLine("  メタバフ: OFF (0pt = 新規プレイヤーの床)");
                return sb.ToString();
            }
            sb.AppendLine(sweepAllMetaAxes && metaBuffMode == MetaBuffMode.BuildFocused
                ? $"  一斉走査: ON ({SweepAxes.Length} 軸をラン単位でラウンドロビン)"
                : $"  単一配分: {metaAllocation}");
            sb.AppendLine($"  {"配分",-14} | {"ラン数",5} | {"クリア率",7} | {"6F到達",7} | {"平均R帯",7}");

            var byAxis = new Dictionary<string, List<RunRec>>();
            foreach (var r in _records)
            {
                string key = string.IsNullOrEmpty(r.metaAxis) ? "-" : r.metaAxis;
                if (!byAxis.TryGetValue(key, out var list)) { list = new List<RunRec>(); byAxis[key] = list; }
                list.Add(r);
            }
            foreach (var kv in byAxis)
            {
                var recs = kv.Value;
                int cleared = 0, reach6 = 0;
                float bandSum = 0f; int bandN = 0;
                foreach (var r in recs)
                {
                    if (r.band == null || !r.band.StartsWith("R")) continue;
                    int rn = 0, ci = 1;
                    while (ci < r.band.Length && char.IsDigit(r.band[ci])) { rn = rn * 10 + (r.band[ci] - '0'); ci++; }
                    if (rn > 0)
                    { bandSum += rn; bandN++; if (rn >= 11) cleared++; if (rn >= 7) reach6++; }
                }
                float clr  = recs.Count > 0 ? (float)cleared / recs.Count * 100f : 0f;
                float r6   = recs.Count > 0 ? (float)reach6 / recs.Count * 100f : 0f;
                float avgB = bandN > 0 ? bandSum / bandN : 0f;
                sb.AppendLine($"  {kv.Key,-14} | {recs.Count,5} | {clr,6:F1}% | {r6,6:F1}% | R{avgB,5:F1}");
            }
            return sb.ToString();
        }

        /// <summary>ボス別の戦闘勝率ブロック。 1〜6層 + 5裏 + 7層各形態を遭遇順に並べ、
        /// 勝率/遭遇数/平均ターン/ロール勝率/主死因を出す (難易度調整の確認用)。</summary>
        private string BuildBossWinRateBlock()
        {
            // enemyId 別に集計
            var agg = new Dictionary<string, BossWinAgg>();
            foreach (var r in _records)
            {
                if (r?.combats == null) continue;
                foreach (var c in r.combats)
                {
                    if (c == null || string.IsNullOrEmpty(c.enemyId) || !BossTuning.IsBoss(c.enemyId)) continue;
                    if (!agg.TryGetValue(c.enemyId, out var a)) { a = new BossWinAgg(); agg[c.enemyId] = a; }
                    a.enc++;
                    if (c.won) a.wins++;
                    a.tWin += c.tWin; a.tLoss += c.tLoss; a.tDraw += c.tDraw; a.turns += c.turns;
                    if (c.won) a.turnsOnWin += c.turns; else a.turnsOnLoss += c.turns;
                    if (!c.won)
                    {
                        string dc = c.deathCause.ToString();
                        a.causes[dc] = a.causes.TryGetValue(dc, out int n) ? n + 1 : 1;
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("【ボス別 戦闘勝率】");
            if (agg.Count == 0) { sb.AppendLine("  (ボス戦の記録なし)"); return sb.ToString(); }

            // 表示順: 1,2,3,4,5,5裏,6, 7層各形態(p1..p7), その他
            string[] ordered = {
                "boss_layer1", "boss_layer2", "boss_layer3", "boss_layer4",
                "boss_layer5", "boss_layer5_hidden", "boss_layer6",
                "boss_layer7", "boss_layer7_p2", "boss_layer7_p3", "boss_layer7_p4",
                "boss_layer7_p5", "boss_layer7_p6", "boss_layer7_p7",
            };
            var shown = new HashSet<string>();
            foreach (var id in ordered)
                if (agg.TryGetValue(id, out var a)) { sb.AppendLine(FormatBossRow(BossLabel(id), a)); shown.Add(id); }
            // 既知順に無い未知ボスを末尾に追加
            foreach (var kv in agg)
                if (!shown.Contains(kv.Key)) sb.AppendLine(FormatBossRow(BossLabel(kv.Key), kv.Value));

            return sb.ToString();
        }

        private string BuildInventoryPowerBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験⑤ インベントリパワー (Tier 表ベース戦力)】");
            sb.AppendLine("  Power = 装備武器 Tier 係数 (T1=1/T2=3/T3=6/T4=10) + 所持品 Tier スコア合算 (S=4/A=3/B=2/C=1/D-E=0)");
            sb.AppendLine("  装備武器・装備ダイス・所持パッシブ・昇華済みパッシブを集計 (同名 1 個 dedup)");
            sb.AppendLine();

            int n = _records.Count;
            if (n == 0) { sb.AppendLine("  (データなし)"); return sb.ToString(); }

            // 層別平均
            var floorPow = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                var r = _records[i];
                if (r?.inventoryPowerByFloor == null) continue;
                foreach (var kv in r.inventoryPowerByFloor)
                {
                    if (!floorPow.TryGetValue(kv.Key, out var list)) { list = new List<int>(); floorPow[kv.Key] = list; }
                    list.Add(kv.Value);
                }
            }

            var floors = new List<int>(floorPow.Keys);
            floors.Sort();
            sb.AppendLine($"  {PadR("層", 4)}{PadR("ラン数", 7)}{PadR("平均Power", 10)}{PadR("中央値", 8)}{PadR("最大", 6)}");
            foreach (int f in floors)
            {
                var list = floorPow[f];
                if (list.Count == 0) continue;
                long sum = 0; int max = 0;
                for (int j = 0; j < list.Count; j++) { sum += list[j]; if (list[j] > max) max = list[j]; }
                float avg = (float)sum / list.Count;
                list.Sort();
                int median = list[list.Count / 2];
                sb.AppendLine($"  {PadR(f.ToString()+"F", 4)}{PadR(list.Count.ToString(), 7)}{PadR(avg.ToString("F1"), 10)}{PadR(median.ToString(), 8)}{PadR(max.ToString(), 6)}");
            }

            // ラン終了時の Power 分布
            long finSum = 0; int finMax = 0, finCnt = 0;
            var finList = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                int p = _records[i].finalInventoryPower;
                finList.Add(p);
                finSum += p; finCnt++; if (p > finMax) finMax = p;
            }
            finList.Sort();
            float finAvg = finCnt > 0 ? (float)finSum / finCnt : 0f;
            int finMed = finCnt > 0 ? finList[finCnt / 2] : 0;
            sb.AppendLine();
            sb.AppendLine($"  ラン終了時 Power: 平均 {finAvg:F1} / 中央値 {finMed} / 最大 {finMax}");
            sb.AppendLine($"  ラン終了時 帯分布: " + ComputePowerBandDistribution(finList));

            // 6F 到達時 vs 死亡時の Power 比較
            long aliveSum = 0, deadSum = 0; int aliveN = 0, deadN = 0;
            for (int i = 0; i < n; i++)
            {
                var r = _records[i];
                if (r.reached6F) { aliveSum += r.finalInventoryPower; aliveN++; }
                else { deadSum += r.finalInventoryPower; deadN++; }
            }
            if (aliveN > 0) sb.AppendLine($"  6F 到達ラン平均 Power: {(float)aliveSum / aliveN:F1} ({aliveN} ラン)");
            if (deadN > 0) sb.AppendLine($"  5F 以下死亡ラン平均 Power: {(float)deadSum / deadN:F1} ({deadN} ラン)");
            sb.AppendLine("  → 差が大きいほど Power が突破力の予測子として有効");

            // Power 別 6F 到達率テーブル (5F snapshot をキーに 6F到達率を予測)
            sb.AppendLine();
            sb.AppendLine("  ── Power 別 6F 到達率予測 (5F snapshot 基準) ──");
            // 帯: [0,20), [20,40), [40,60), [60,80), [80,100), [100,+∞)
            int[] bandLowers = { 0, 20, 40, 60, 80, 100 };
            int bands = bandLowers.Length;
            int[] bandTotal = new int[bands];
            int[] bandReached6 = new int[bands];
            for (int i = 0; i < n; i++)
            {
                var r = _records[i];
                if (r == null || r.inventoryPowerByFloor == null) continue;
                if (!r.inventoryPowerByFloor.TryGetValue(5, out int p5)) continue;
                int b = bands - 1;
                for (int k = 0; k < bands - 1; k++)
                    if (p5 < bandLowers[k + 1]) { b = k; break; }
                bandTotal[b]++;
                if (r.reached6F) bandReached6[b]++;
            }
            sb.AppendLine($"  {PadR("Power 帯", 14)}{PadR("ラン数", 7)}{PadR("6F到達", 7)}{PadR("到達率", 8)}");
            for (int b = 0; b < bands; b++)
            {
                if (bandTotal[b] == 0) continue;
                string label = (b == bands - 1)
                    ? $"{bandLowers[b]}+"
                    : $"{bandLowers[b]}-{bandLowers[b + 1] - 1}";
                float rate = 100f * bandReached6[b] / bandTotal[b];
                sb.AppendLine($"  {PadR(label, 14)}{PadR(bandTotal[b].ToString(), 7)}{PadR(bandReached6[b].ToString(), 7)}{PadR(rate.ToString("F1") + "%", 8)}");
            }
            return sb.ToString();
        }

        /// <summary>⑥ アイテム別 Power 寄与 (2026-06-22 Phase a)。
        /// 各アイテム ID について、 取得ランと非取得ランの finalInventoryPower 差分を出す。
        /// 2026-06-23 案 A: 「ΔPower 提示時」 (offered cohort) を併記。
        ///   ΔPower 全体は selection bias が大きい (非取得側 = 早期死亡で店に辿り着けなかった群が混入)。
        ///   提示時 ΔPower はその品を offerd された run 限定で比較 → 真の貢献に近い。</summary>
        private string BuildItemPowerContributionBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験⑥ アイテム別 Power 寄与 (上位 30、 提示時 ΔPower 降順)】");
            sb.AppendLine("  ΔPower全体 = avg(finalPower | 取得) − avg(finalPower | 非取得)  ← selection bias 込み");
            sb.AppendLine("  ΔPower提示 = avg(finalPower | 取得 ∧ 提示) − avg(finalPower | 非取得 ∧ 提示)  ← bias 緩和");
            sb.AppendLine("  (取得ラン≥30 のみ表示、 ExcludedFromLift 除外)");
            sb.AppendLine();

            int n = _records.Count;
            if (n == 0) { sb.AppendLine("  (データなし)"); return sb.ToString(); }

            // 全アイテム ID 集合
            var allIds = new HashSet<string>();
            for (int i = 0; i < n; i++)
            {
                var r = _records[i];
                if (r?.acquiredItemsEver != null)
                    foreach (var id in r.acquiredItemsEver) allIds.Add(id);
            }

            // 各 ID の Power 寄与を計算 (全体 / 提示時 の 2 系統)
            var rows = new List<(string id, int acqN, int offeredN, float deltaAll, float deltaOffered)>();
            foreach (var id in allIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (ItemLearningStats.ExcludedFromLift.Contains(id)) continue;
                long acqSum = 0, noacqSum = 0;
                int acqN = 0, noacqN = 0;
                long offAcqSum = 0, offNoacqSum = 0;
                int offAcqN = 0, offNoacqN = 0;
                for (int i = 0; i < n; i++)
                {
                    var r = _records[i];
                    if (r == null) continue;
                    bool acq = r.acquiredItemsEver != null && r.acquiredItemsEver.Contains(id);
                    bool offered = r.offeredItemsEver != null && r.offeredItemsEver.Contains(id);
                    if (acq) { acqSum += r.finalInventoryPower; acqN++; }
                    else { noacqSum += r.finalInventoryPower; noacqN++; }
                    // 提示コホート: offered または acq (取得=提示扱い、 ショップ以外の経路を含む)
                    if (offered || acq)
                    {
                        if (acq) { offAcqSum += r.finalInventoryPower; offAcqN++; }
                        else { offNoacqSum += r.finalInventoryPower; offNoacqN++; }
                    }
                }
                if (acqN < 30 || noacqN < 30) continue; // 全体の最低サンプル
                float deltaAll = (float)(acqSum / (double)acqN - noacqSum / (double)noacqN);
                float deltaOffered = (offAcqN >= 15 && offNoacqN >= 15)
                    ? (float)(offAcqSum / (double)offAcqN - offNoacqSum / (double)offNoacqN)
                    : float.NaN; // 提示時サンプル不足は NaN
                rows.Add((id, acqN, offAcqN + offNoacqN, deltaAll, deltaOffered));
            }
            // 提示時 ΔPower 降順 (NaN は末尾)
            rows.Sort((x, y) =>
            {
                bool xn = float.IsNaN(x.deltaOffered), yn = float.IsNaN(y.deltaOffered);
                if (xn && yn) return y.deltaAll.CompareTo(x.deltaAll);
                if (xn) return 1;
                if (yn) return -1;
                return y.deltaOffered.CompareTo(x.deltaOffered);
            });

            int show = Math.Min(30, rows.Count);
            sb.AppendLine($"  {PadR("アイテム ID", 26)}{PadR("取得数", 7)}{PadR("提示母数", 9)}{PadR("ΔPower全体", 12)}{PadR("ΔPower提示", 12)}");
            for (int i = 0; i < show; i++)
            {
                var r = rows[i];
                string offStr = float.IsNaN(r.deltaOffered) ? "n/a" : ((r.deltaOffered >= 0 ? "+" : "") + r.deltaOffered.ToString("F1"));
                sb.AppendLine($"  {PadR(TruncDisp(r.id, 24), 26)}{PadR(r.acqN.ToString(), 7)}{PadR(r.offeredN.ToString(), 9)}{PadR((r.deltaAll >= 0 ? "+" : "") + r.deltaAll.ToString("F1"), 12)}{PadR(offStr, 12)}");
            }
            sb.AppendLine();
            sb.AppendLine("  注: 提示時 ΔPower がより信頼可。 大幅差がある品は selection bias による誤評価の可能性。");
            sb.AppendLine("  動的効果品 (BD/双蛇/不屈系等) の真価は bandScore lift6F も併読推奨。");
            return sb.ToString();
        }

        /// <summary>Power 帯ごとのラン数分布を文字列化。 Weak/Early/Mid/Late/Apex の 5 帯。</summary>
        private static string ComputePowerBandDistribution(List<int> powers)
        {
            if (powers == null || powers.Count == 0) return "(データなし)";
            int[] counts = new int[5];
            foreach (var p in powers) counts[AutoTest.InventoryPower.GetPowerBandRank(p)]++;
            int total = powers.Count;
            string[] labels = { "Weak", "Early", "Mid", "Late", "Apex" };
            var parts = new List<string>(5);
            for (int i = 0; i < 5; i++)
                if (counts[i] > 0)
                    parts.Add($"{labels[i]}={counts[i]}({100f * counts[i] / total:F1}%)");
            return string.Join(" / ", parts);
        }

        /// <summary>⑦ アイテム別 pick率 / retention率 (2026-06-23)。
        ///   pick率   = 取得数 / 提示数 (提示された時にどれだけ拾うか)
        ///   保持率   = 最終所持数 / 取得数 (取った後どれだけ残すか)
        /// 高 pick / 低 retain = 「序盤強いが終盤お役御免」 の典型パターン抽出に有効。</summary>
        private string BuildPickRetentionBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験⑦ アイテム別 pick率 / 保持率 (上位 50、 提示数≥30、 pick率降順)】");
            sb.AppendLine("  pick率 = 取得数 / 提示数 (提示時の採用率)");
            sb.AppendLine("  保持率 = 最終所持数 / 取得数 (取った後の残存率)");
            sb.AppendLine("  高pick/低保持 = 序盤強・終盤お役御免の典型パターン");
            sb.AppendLine();

            int n = _records.Count;
            if (n == 0) { sb.AppendLine("  (データなし)"); return sb.ToString(); }

            // id → (offered, picked, retained) を集計
            var stats = new Dictionary<string, int[]>(); // [offered, picked, retained]
            for (int i = 0; i < n; i++)
            {
                var r = _records[i];
                if (r == null) continue;
                // 提示集合 (offered) と取得集合 (acquired) の和を「offered (機会あり)」 として扱う
                //   ─ 取得品は必ず提示扱い (ショップ以外の経路を含む)
                var offered = new HashSet<string>();
                if (r.offeredItemsEver != null) foreach (var id in r.offeredItemsEver) offered.Add(id);
                if (r.acquiredItemsEver != null) foreach (var id in r.acquiredItemsEver) offered.Add(id);
                foreach (var id in offered)
                {
                    if (!stats.TryGetValue(id, out var a)) { a = new int[3]; stats[id] = a; }
                    a[0]++; // offered
                    if (r.acquiredItemsEver != null && r.acquiredItemsEver.Contains(id)) a[1]++; // picked
                    if (r.finalOwnedItemIds != null && r.finalOwnedItemIds.Contains(id)) a[2]++; // retained
                }
            }

            // 集計 → 行リスト化 (提示数≥30 のみ)
            var rows = new List<(string id, int offered, int picked, int retained, float pickRate, float retentionRate)>();
            foreach (var kv in stats)
            {
                int off = kv.Value[0], pick = kv.Value[1], ret = kv.Value[2];
                if (off < 30) continue;
                float pickRate = (float)pick / off;
                float retRate = pick > 0 ? (float)ret / pick : 0f;
                rows.Add((kv.Key, off, pick, ret, pickRate, retRate));
            }
            // pick率 降順
            rows.Sort((x, y) => y.pickRate.CompareTo(x.pickRate));

            int show = Math.Min(50, rows.Count);
            sb.AppendLine($"  {PadR("アイテム ID", 26)}{PadR("提示", 6)}{PadR("取得", 6)}{PadR("最終", 6)}{PadR("pick率", 8)}{PadR("保持率", 8)}{PadR("性質", 18)}");
            for (int i = 0; i < show; i++)
            {
                var r = rows[i];
                string trait = ClassifyPickRetention(r.pickRate, r.retentionRate);
                sb.AppendLine($"  {PadR(TruncDisp(r.id, 24), 26)}{PadR(r.offered.ToString(), 6)}{PadR(r.picked.ToString(), 6)}{PadR(r.retained.ToString(), 6)}{PadR((r.pickRate * 100).ToString("F1") + "%", 8)}{PadR((r.retentionRate * 100).ToString("F1") + "%", 8)}{PadR(trait, 18)}");
            }
            return sb.ToString();
        }

        /// <summary>pick率/保持率の組合せから性質ラベル付与。</summary>
        private static string ClassifyPickRetention(float pickRate, float retentionRate)
        {
            if (pickRate >= 0.7f && retentionRate >= 0.7f) return "★定番 (高取得・残)";
            if (pickRate >= 0.7f && retentionRate < 0.4f) return "⚠序盤要員 (拾うが捨)";
            if (pickRate >= 0.7f) return "○常用品";
            if (pickRate < 0.2f && retentionRate >= 0.7f) return "◇隠れ強 (拾えば残)";
            if (pickRate < 0.2f) return "△マイナー";
            return "─普通";
        }

        // ===========================================================
        //  ゲーム体験 4 軸メトリクス (2026-06-21)
        //  - 緊張感曲線 / ビルド多様性 / 死因分布質 / 選択意味度
        //  全て既存 RunRec/CombatRec を集計するのみ (新規イベント収集なし)
        // ===========================================================

        /// <summary>1. 緊張感曲線 ── 各層末の HP%、 knife-edge (僅差勝利)、 blowout (大差敗北) を集計。
        /// 「ギリギリ勝った気持ち良さ」 と「不公平な瞬殺」 の発生率で体験のメリハリを測る。</summary>
        private string BuildTensionCurveBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験① 緊張感曲線 (Tension Curve)】");
            sb.AppendLine("  各層の戦闘終了 HP%、 knife-edge 勝利 (HP≤20% で勝った戦闘)、 blowout 敗北 (HP>80% から 1 戦で死亡)");
            sb.AppendLine();

            // floor → 集計
            var byFloor = new Dictionary<int, int[]>(); // [n, hpPctSum_x100, knifeWin, blowoutLoss, winN, lossN]
            for (int ri = 0; ri < _records.Count; ri++)
            {
                var rr = _records[ri];
                if (rr?.combats == null) continue;
                for (int ci = 0; ci < rr.combats.Count; ci++)
                {
                    var c = rr.combats[ci];
                    if (c == null || !c.isFightEnd) continue;
                    int max = c.playerMaxHpEnd > 0 ? c.playerMaxHpEnd : 1;
                    if (!byFloor.TryGetValue(c.floor, out var a)) { a = new int[6]; byFloor[c.floor] = a; }
                    a[0]++;
                    int hpPct = Mathf.Clamp(c.hpAfter * 100 / max, 0, 100);
                    a[1] += hpPct;
                    if (c.won)
                    {
                        a[4]++;
                        if (hpPct <= 20) a[2]++; // knife-edge: HP残≤20% で勝った
                    }
                    else
                    {
                        a[5]++;
                        int hpBeforePct = Mathf.Clamp(c.hpBefore * 100 / max, 0, 100);
                        if (hpBeforePct > 80) a[3]++; // blowout: HP>80% スタートで死亡
                    }
                }
            }

            var floors = new List<int>(byFloor.Keys);
            floors.Sort();
            sb.AppendLine($"  {PadR("層",4)}{PadR("戦闘数",7)}{PadR("平均残HP%",10)}{PadR("knife勝率",10)}{PadR("blowout率",10)}");
            foreach (int f in floors)
            {
                var a = byFloor[f];
                if (a[0] == 0) continue;
                float avgHp = (float)a[1] / a[0];
                float knife = a[4] > 0 ? 100f * a[2] / a[4] : 0f;
                float blow  = a[5] > 0 ? 100f * a[3] / a[5] : 0f;
                sb.AppendLine($"  {PadR(f.ToString()+"F",4)}{PadR(a[0].ToString(),7)}{PadR(avgHp.ToString("F1")+"%",10)}{PadR(knife.ToString("F1")+"%",10)}{PadR(blow.ToString("F1")+"%",10)}");
            }
            sb.AppendLine("  knife勝率 = 勝利戦闘のうち残HP≤20% で勝った割合 (高=緊張感ある勝利)");
            sb.AppendLine("  blowout率 = 敗北戦闘のうち HP>80% から負けた割合 (高=理不尽な瞬殺)");

            // 全層合算の comeback (HP<20% から 6F 到達)
            int comebackRuns = 0, comebackEligible = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                var rr = _records[i];
                if (rr?.combats == null) continue;
                bool wasCritical = false;
                bool reached6F = false;
                for (int ci = 0; ci < rr.combats.Count; ci++)
                {
                    var c = rr.combats[ci];
                    if (c == null) continue;
                    int max = c.playerMaxHpEnd > 0 ? c.playerMaxHpEnd : 1;
                    if (c.floor <= 4 && c.hpAfter * 5 < max) wasCritical = true;
                    if (c.floor >= 6) reached6F = true;
                }
                if (wasCritical) { comebackEligible++; if (reached6F) comebackRuns++; }
            }
            sb.AppendLine();
            sb.AppendLine($"  Comeback: 1-4層で HP<20% を経験 → 6F到達: {comebackRuns} / {comebackEligible} ラン ({(comebackEligible > 0 ? 100f*comebackRuns/comebackEligible : 0f):F1}%)");
            sb.AppendLine("  (高=逆転シナリオが多い、 低=ピンチ=即死パターン)");
            return sb.ToString();
        }

        /// <summary>2. ビルド多様性 ── 5F到達時の「武器Tier + 装備ダイス + 主要パッシブ」 を組として集計し、
        /// Top-K 集中度・Shannon エントロピー・ユニーク数で多様性を測る。</summary>
        private string BuildBuildDiversityBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験② ビルド多様性 (Build Diversity)】");
            sb.AppendLine("  5F到達ランの「最終武器Tier × 装備ダイス」 で組成し、 集中度を測る");
            sb.AppendLine();

            var buildCount = new Dictionary<string, int>();
            int reached5F = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                var rr = _records[i];
                if (rr == null) continue;
                if (rr.deathFloor < 5 && !rr.reached6F) continue; // 5F到達のみ
                reached5F++;
                string wpn = string.IsNullOrEmpty(rr.finalWeaponTier) ? "(無)" : rr.finalWeaponTier;
                // 5F到達時点のダイスは記録されていない → 最終ダイスで近似
                string dice = "";
                if (rr.combats != null)
                {
                    for (int ci = rr.combats.Count - 1; ci >= 0; ci--)
                    {
                        if (rr.combats[ci]?.floor == 5)
                        {
                            dice = rr.combats[ci].diceId ?? "";
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(dice)) dice = "(武器ダイス)";
                string key = wpn + " × " + dice;
                buildCount.TryGetValue(key, out int c);
                buildCount[key] = c + 1;
            }

            if (reached5F == 0)
            {
                sb.AppendLine("  (5F到達ランなし)");
                return sb.ToString();
            }

            var sorted = new List<KeyValuePair<string, int>>(buildCount);
            sorted.Sort((x, y) => y.Value.CompareTo(x.Value));

            // 集中度
            int top10Share = 0;
            int topN = Math.Min(10, sorted.Count);
            for (int i = 0; i < topN; i++) top10Share += sorted[i].Value;

            // ユニーク (1 回しか出ないビルド)
            int hapax = 0;
            foreach (var kv in sorted) if (kv.Value == 1) hapax++;

            // Shannon entropy (bit)
            double H = 0;
            foreach (var kv in sorted)
            {
                double p = (double)kv.Value / reached5F;
                if (p > 0) H -= p * Math.Log(p, 2);
            }
            double maxH = sorted.Count > 1 ? Math.Log(sorted.Count, 2) : 1;

            sb.AppendLine($"  5F到達ラン: {reached5F}");
            sb.AppendLine($"  ユニークビルド数: {sorted.Count} (1回限り: {hapax})");
            sb.AppendLine($"  上位10ビルド集中度: {100f*top10Share/reached5F:F1}% (低=広く分散、 高=収束)");
            sb.AppendLine($"  Shannon エントロピー: {H:F2} bit / 最大 {maxH:F2} bit (正規化 {H/maxH:F2})");
            sb.AppendLine();
            sb.AppendLine("  上位 10 ビルド:");
            for (int i = 0; i < topN; i++)
            {
                var kv = sorted[i];
                sb.AppendLine($"    {PadR(kv.Key, 36)} {kv.Value,5} ({100f*kv.Value/reached5F:F1}%)");
            }
            return sb.ToString();
        }

        /// <summary>3. 死因分布質 ── 死因を「公平死 (累積攻撃/スリップ)」「理不尽死 (反射/サドンデス/烙印一撃)」 に分類し、
        /// 各層の理不尽死率を出す。 高い層 = 調整候補。</summary>
        private string BuildDeathCauseQualityBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験③ 死因分布質 (Death Cause Quality)】");
            sb.AppendLine("  公平死 = Normal/Chip (累積攻撃や持続)");
            sb.AppendLine("  理不尽死 = Reflect (反射自滅) / SuddenDeath (純運勝負) / Garyo (画竜点睛) / その他突発要因");
            sb.AppendLine();

            // 死因の分類 (CombatRec.deathCause を使う、 deathCauseは戦闘の死因)
            // 公平: Normal, Chip
            // 理不尽: Reflect, SuddenDeath, Garyo, Pursuit (突発反撃), Threat (脅威でハメ殺し)
            var byFloor = new Dictionary<int, int[]>(); // [死戦闘総数, 公平, 理不尽]
            for (int i = 0; i < _records.Count; i++)
            {
                var rr = _records[i];
                if (rr?.combats == null) continue;
                for (int ci = 0; ci < rr.combats.Count; ci++)
                {
                    var c = rr.combats[ci];
                    if (c == null || c.won) continue;
                    if (!c.isFightEnd) continue;
                    string dc = c.deathCause.ToString();
                    bool fair = dc == "Normal" || dc == "Chip" || dc == "None";
                    bool unfair = dc == "Reflect" || dc == "SuddenDeath" || dc == "Garyo"
                               || dc == "Pursuit" || dc == "Threat" || dc == "Judgment";
                    if (!byFloor.TryGetValue(c.floor, out var a)) { a = new int[3]; byFloor[c.floor] = a; }
                    a[0]++;
                    if (fair) a[1]++;
                    else if (unfair) a[2]++;
                }
            }

            var floors = new List<int>(byFloor.Keys);
            floors.Sort();
            sb.AppendLine($"  {PadR("層",4)}{PadR("死戦闘",7)}{PadR("公平死",8)}{PadR("理不尽死",10)}{PadR("理不尽率",10)}");
            foreach (int f in floors)
            {
                var a = byFloor[f];
                if (a[0] == 0) continue;
                float unfairPct = 100f * a[2] / a[0];
                sb.AppendLine($"  {PadR(f.ToString()+"F",4)}{PadR(a[0].ToString(),7)}{PadR(a[1].ToString(),8)}{PadR(a[2].ToString(),10)}{PadR(unfairPct.ToString("F1")+"%",10)}");
            }
            sb.AppendLine("  理不尽率が高い層は調整候補 (Reflect=反射火力過剰 / SuddenDeath=運勝負化過剰 等)");
            return sb.ToString();
        }

        /// <summary>4. 選択意味度 ── イベント選択肢で「常に同じ選択肢」 = 等価/つまらない分岐、
        /// 「選択直後 3 戦闘以内に死亡」 = ハズレ選択肢を可視化。</summary>
        private string BuildDecisionWeightBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【ゲーム体験④ 選択意味度 (Decision Weight)】");
            sb.AppendLine("  各イベントの選択肢分布。 偏りが極端 (1選択肢が ≥80%) = AI が等価でないと判断 = ジレンマ無し");
            sb.AppendLine();

            // EventChoiceLearningStats / RunRec.eventChoicesMade から集計
            // 各 eventId につき「最頻選択肢の割合」「分岐数」「合計回数」
            var perEvent = new Dictionary<string, Dictionary<int, int>>(); // eventId -> (choiceIdx -> count)
            for (int i = 0; i < _records.Count; i++)
            {
                var rr = _records[i];
                if (rr?.eventChoicesMade == null) continue;
                for (int j = 0; j < rr.eventChoicesMade.Count; j++)
                {
                    string item = rr.eventChoicesMade[j];
                    int pipe = item.IndexOf('|');
                    if (pipe <= 0 || pipe >= item.Length - 1) continue;
                    string eid = item.Substring(0, pipe);
                    if (!int.TryParse(item.Substring(pipe + 1), out int idx)) continue;
                    if (!perEvent.TryGetValue(eid, out var dict)) { dict = new Dictionary<int, int>(); perEvent[eid] = dict; }
                    dict.TryGetValue(idx, out int c);
                    dict[idx] = c + 1;
                }
            }

            if (perEvent.Count == 0)
            {
                sb.AppendLine("  (イベント選択記録なし)");
                return sb.ToString();
            }

            // 各イベントごとの最頻選択肢割合
            var rows = new List<(string id, int total, int branches, float topPct)>();
            int dominantCount = 0, balancedCount = 0;
            foreach (var kv in perEvent)
            {
                int total = 0, top = 0;
                foreach (var v in kv.Value.Values) { total += v; if (v > top) top = v; }
                if (total == 0) continue;
                float topPct = 100f * top / total;
                rows.Add((kv.Key, total, kv.Value.Count, topPct));
                if (topPct >= 80f) dominantCount++;
                else if (topPct <= 50f + (50f / Math.Max(1, kv.Value.Count - 1))) balancedCount++;
            }
            rows.Sort((x, y) => y.topPct.CompareTo(x.topPct));

            sb.AppendLine($"  イベント総数: {rows.Count}");
            sb.AppendLine($"  Dominant (最頻≥80%): {dominantCount} ({100f*dominantCount/rows.Count:F1}%) ── ジレンマ無しイベント");
            sb.AppendLine($"  Balanced (拮抗): {balancedCount} ({100f*balancedCount/rows.Count:F1}%) ── 選択肢が意味を持つイベント");
            sb.AppendLine();
            sb.AppendLine("  最も偏った 10 イベント (= AI が常に同じ選択肢):");
            int show = Math.Min(10, rows.Count);
            for (int i = 0; i < show; i++)
            {
                var r = rows[i];
                sb.AppendLine($"    {PadR(TruncDisp(r.id, 22), 24)} 回数{r.total,5} / 分岐{r.branches} / 最頻{r.topPct:F1}%");
            }
            return sb.ToString();
        }

        private class BossWinAgg
        {
            public int enc, wins;
            public long tWin, tLoss, tDraw, turns;
            /// <summary>撃破できた戦闘だけのターン合計 / 力尽きた戦闘だけのターン合計。
            /// 2026-07-28: 全遭遇平均だけでは「ボスHPが律速か、プレイヤー生存が律速か」を
            /// 区別できないため分離した。 勝T = ボスを削り切るのに要した時間、
            /// 敗T = プレイヤーが保った時間。 前者は火力、 後者は加害のスケールを示す。</summary>
            public long turnsOnWin, turnsOnLoss;
            public Dictionary<string, int> causes = new Dictionary<string, int>();
            public float WinRate => enc > 0 ? (float)wins / enc : 0f;
            public float RollWinRate { get { long t = tWin + tLoss + tDraw; return t > 0 ? (float)tWin / t : 0f; } }
            public float AvgTurns => enc > 0 ? (float)turns / enc : 0f;
            public float AvgTurnsWin => wins > 0 ? (float)turnsOnWin / wins : 0f;
            public float AvgTurnsLoss => Losses > 0 ? (float)turnsOnLoss / Losses : 0f;
            public int Losses => enc - wins;
        }

        private static string FormatBossRow(string label, BossWinAgg a)
        {
            string cause = "敗北なし";
            if (a.Losses > 0)
            {
                string dom = ""; int domN = 0;
                foreach (var kv in a.causes) if (kv.Value > domN) { dom = kv.Key; domN = kv.Value; }
                cause = domN > 0 ? $"主死因{dom} {(float)domN / a.Losses:P0}" : "—";
            }
            // 「収支勝率」= 収支プラス (与ダメ-被ダメ>0) で終えたターンの割合。ADR-0009 で「ロール勝率」から再定義。
            // 勝T/敗T の分離 (2026-07-28): 勝T=ボスを削り切る時間 (＝ボスHPの尺度)、
            // 敗T=プレイヤーが保つ時間 (＝ボス加害の尺度)。 全遭遇平均だけだと両者が混ざり、
            // 「HPを下げてもターンが動かない」現象の原因が読めなくなる。
            string winT  = a.wins   > 0 ? $"{a.AvgTurnsWin,4:F1}T" : "   -";
            string lossT = a.Losses > 0 ? $"{a.AvgTurnsLoss,4:F1}T" : "   -";
            return $"  {label,-7}: 勝率 {a.WinRate,6:P1}  (遭遇 {a.enc,5} / 勝 {a.wins,5})"
                 + $"  平均{a.AvgTurns,4:F1}T (勝{winT}/敗{lossT})  収支勝率{a.RollWinRate,5:P0}  {cause}";
        }

        private static string BossLabel(string id)
        {
            switch (id)
            {
                case "boss_layer1": return "1層";
                case "boss_layer2": return "2層";
                case "boss_layer3": return "3層";
                case "boss_layer4": return "4層";
                case "boss_layer5": return "5層";
                case "boss_layer5_hidden": return "5裏";
                case "boss_layer6": return "6層";
                case "boss_layer7": return "7層p1";
                case "boss_layer7_p2": return "7層p2";
                case "boss_layer7_p3": return "7層p3";
                case "boss_layer7_p4": return "7層p4";
                case "boss_layer7_p5": return "7層p5";
                case "boss_layer7_p6": return "7層p6";
                case "boss_layer7_p7": return "7層p7";
                default: return id;
            }
        }

        /// <summary>武器強化経路の到達状況。 T4集計バグ修正の効果検証用。
        /// OnWeaponTierUpgraded 発火回数とラン終了時の武器Tier分布を出す。</summary>
        private string BuildWeaponProgressionBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【武器強化経路 (集計修正バグ検証)】");
            int n = _records.Count;
            if (n == 0) { sb.AppendLine("  (データなし)"); return sb.ToString(); }

            // 強化回数分布
            int totalUpgrades = 0;
            int upgradeUsers = 0;
            int reachT4 = 0;
            var tierCount = new Dictionary<string, int>();
            foreach (var r in _records)
            {
                if (r == null) continue;
                totalUpgrades += r.tierUpgradeCount;
                if (r.tierUpgradeCount > 0) upgradeUsers++;
                if (!string.IsNullOrEmpty(r.finalWeaponTier))
                {
                    tierCount[r.finalWeaponTier] = tierCount.TryGetValue(r.finalWeaponTier, out int c) ? c + 1 : 1;
                    if (r.finalWeaponTier.EndsWith("_t4")) reachT4++;
                }
            }
            sb.AppendLine($"  強化発火合計   : {totalUpgrades} 回 ({(double)totalUpgrades / n:F2}/ラン)");
            sb.AppendLine($"  強化を行ったラン: {upgradeUsers} / {n} ({100.0 * upgradeUsers / n:F1}%)");
            sb.AppendLine($"  最終T4到達ラン : {reachT4} / {n} ({100.0 * reachT4 / n:F1}%)");
            sb.AppendLine($"  最終武器Tier分布:");
            var sorted = new List<KeyValuePair<string, int>>(tierCount);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in sorted)
                sb.AppendLine($"    {kv.Key,-20} : {kv.Value} ({100.0 * kv.Value / n:F1}%)");
            return sb.ToString();
        }

        /// <summary>1コホート分の自己完結サマリブロック。</summary>
        private string BuildSummaryBlock(string title, List<RunRec> recs)
        {
            int n = recs.Count;
            var sb = new StringBuilder();
            sb.AppendLine("================ " + title + " ================");
            sb.AppendLine($"ラン数    : {n}");
            sb.AppendLine($"行動方針  : 前進貪欲 + 生存重視");
            sb.AppendLine();
            if (n == 0) { sb.AppendLine("（該当ランなし）"); return sb.ToString(); }

            // --- バンド分布 (R1-R11, R8b 含む) ---
            // R8b = 6Fクリアして 7F進入不可で終了 (真理未所持) を R9 と R10 の間に挿入。
            sb.AppendLine("---- 結果バンド分布（上位%＝その結果『以上』に到達したランの割合） ----");
            string[] bands = { "R1a","R1b","R1c","R1d","R2","R3","R4","R5","R6","R7","R8","R9","R8b","R10","R11" };
            string[] labels = {
                "1F道中で死亡","1Fボスで死亡","2F道中で死亡","2Fボスで死亡",
                "3F道中で死亡","3Fボスで死亡","4F道中で死亡","4Fボスで死亡",
                "5F道中で死亡","5Fボスで死亡","5Fクリア(決意未所持)","6Fボスで死亡",
                "6Fクリア(真理未所持)","7Fで死亡(7層到達)","7層クリア(完全クリア)" };
            int crash = recs.FindAll(r => r.band == "CRASH").Count;
            int dead  = recs.FindAll(r => r.band == "DEADLOCK").Count;
            int valid = n - crash - dead;
            // bands は進行の浅い→深い順。各バンドの「以上(=そのバンド＋それより良い全て)」を
            // ベスト側から累積し、valid に対する割合＝上位% として表示する。
            int[] counts = new int[bands.Length];
            for (int i = 0; i < bands.Length; i++)
                counts[i] = recs.FindAll(r => r.band == bands[i]).Count;
            int[] topCum = new int[bands.Length];
            int cum = 0;
            for (int i = bands.Length - 1; i >= 0; i--) { cum += counts[i]; topCum[i] = cum; }
            for (int i = 0; i < bands.Length; i++)
            {
                sb.AppendLine($"  {PadR(bands[i],4)}{PadR(labels[i],22)}: {PadL(counts[i].ToString(),4)}  上位 {PadL(Pct(topCum[i], valid),6)}");
            }
            sb.AppendLine("  （例: 「7層クリア 上位X%」= 全ランの上位X%が7層クリア以上を達成）");
            sb.AppendLine();
            sb.AppendLine($"  {PadR("CRASH",4)}{PadR("クラッシュ(例外)",22)}: {PadL(crash.ToString(),4)}  ({Pct(crash, n)} of all)");
            sb.AppendLine($"  {PadR("DEAD",4)}{PadR("デッドロック",22)}: {PadL(dead.ToString(),4)}  ({Pct(dead, n)} of all)");
            sb.AppendLine();

            // --- 死亡階層・死因 ---
            sb.AppendLine("---- 死亡階層・死因 ----");
            var deaths = recs.FindAll(r => r.outcome == Outcome.GameOver);
            sb.AppendLine($"  総死亡数: {deaths.Count} / {n}");
            for (int f = 1; f <= 6; f++)
            {
                int c = deaths.FindAll(r => r.deathFloor == f).Count;
                if (c > 0) sb.AppendLine($"   {PadR("Floor " + f, 16)}: {PadL(c.ToString(),4)}  ({Pct(c, deaths.Count)})");
            }
            foreach (DeathCause dc in Enum.GetValues(typeof(DeathCause)))
            {
                if (dc == DeathCause.None) continue;
                int c = deaths.FindAll(r => r.cause == dc).Count;
                if (c > 0) sb.AppendLine($"   {PadR(dc.ToString(), 16)}: {PadL(c.ToString(),4)}  ({Pct(c, deaths.Count)})");
            }
            sb.AppendLine();

            // --- ラストスタンド ---
            sb.AppendLine("---- ラストスタンド ----");
            var ls = recs.FindAll(r => r.lastStandUsed);
            sb.AppendLine($"  発動ラン数: {ls.Count} / {n}  ({Pct(ls.Count, n)})");
            for (int f = 1; f <= 6; f++)
            {
                int c = ls.FindAll(r => r.lastStandFloor == f).Count;
                if (c > 0) sb.AppendLine($"   {PadR("発動Floor " + f, 16)}: {PadL(c.ToString(),4)}  ({Pct(c, ls.Count)})");
            }
            if (ls.Count > 0)
            {
                double avgAfter = 0, avgWin = 0;
                foreach (var r in ls) { avgAfter += r.combatsAfterLastStand; avgWin += r.winsAfterLastStand; }
                int maxAfter = 0; foreach (var r in ls) maxAfter = Math.Max(maxAfter, r.combatsAfterLastStand);
                sb.AppendLine($"   {PadR("発動後の平均戦闘数", 20)}: {(avgAfter/ls.Count):F2}");
                sb.AppendLine($"   {PadR("発動後の平均勝利数", 20)}: {(avgWin/ls.Count):F2}");
                sb.AppendLine($"   {PadR("発動後の最大潜り抜け", 20)}: {maxAfter} 戦");
            }
            sb.AppendLine();

            // --- ラストスタンド直接死因 ---
            sb.AppendLine("---- ラストスタンド直接死因 ----");
            var lsDead = ls.FindAll(r => r.outcome == Outcome.GameOver);
            sb.AppendLine($"  発動後に死亡したラン: {lsDead.Count} / {ls.Count}");
            if (lsDead.Count > 0)
            {
                // 発動後 何戦目で死亡したか（combatsAfterLastStand のバケット）
                int b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0;
                foreach (var r in lsDead)
                {
                    int c = r.combatsAfterLastStand;
                    if (c <= 0) b0++; else if (c == 1) b1++; else if (c == 2) b2++;
                    else if (c == 3) b3++; else b4++;
                }
                sb.AppendLine($"   {PadR("発動と同戦闘で即死(0戦)", 24)}: {PadL(b0.ToString(),4)}  ({Pct(b0, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後1戦で死亡", 24)}: {PadL(b1.ToString(),4)}  ({Pct(b1, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後2戦で死亡", 24)}: {PadL(b2.ToString(),4)}  ({Pct(b2, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後3戦で死亡", 24)}: {PadL(b3.ToString(),4)}  ({Pct(b3, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後4戦以上で死亡", 24)}: {PadL(b4.ToString(),4)}  ({Pct(b4, lsDead.Count)})");

                // 死亡階層
                for (int f = 1; f <= 6; f++)
                {
                    int c = lsDead.FindAll(r => r.deathFloor == f).Count;
                    if (c > 0) sb.AppendLine($"   {PadR("Floor " + f + " で死亡", 24)}: {PadL(c.ToString(),4)}  ({Pct(c, lsDead.Count)})");
                }

                // 致命を与えた敵（各ランの最後の敗北戦から）
                var fatalAgg = new Dictionary<string, int>();
                foreach (var r in lsDead)
                {
                    if (r.combats == null) continue;
                    CombatRec fc = null;
                    for (int i = r.combats.Count - 1; i >= 0; i--)
                        if (!r.combats[i].won) { fc = r.combats[i]; break; }
                    if (fc == null) continue;
                    bool boss = fc.isBoss || GameLoop.BossIds.IsBoss(fc.enemyId);
                    string nm = (string.IsNullOrEmpty(fc.enemy) ? "?" : fc.enemy) + (boss ? "(BOSS)" : "");
                    fatalAgg.TryGetValue(nm, out var v);
                    fatalAgg[nm] = v + 1;
                }
                var fk = new List<string>(fatalAgg.Keys);
                fk.Sort((x, y) => fatalAgg[y].CompareTo(fatalAgg[x]));
                sb.AppendLine("   ── 致命を与えた敵 ──");
                foreach (var k in fk)
                    sb.AppendLine($"   {PadR(TruncDisp(k,18), 22)}: {PadL(fatalAgg[k].ToString(),4)}  ({Pct(fatalAgg[k], lsDead.Count)})");
            }
            sb.AppendLine("（発動後は基本ロール敗北のメインダメのみ＝致命敵＝その止めを刺した敵）");
            sb.AppendLine();

            // --- 経済・燃費 ---
            sb.AppendLine("---- 経済・燃費バランス ----");
            double sCoins=0, sPeak=0, sGain=0, sStarv=0, sStarvHit=0, sShop=0;
            double sReroll=0, sRerollG=0, sPrio=0, sMatGain=0;
            double sFinalProvision=0, sMinProvision=0; int madnessCount=0;   // 物資(ADR-0002)
            double sHCombat=0, sHComposure=0, sHLateral=0, sHMarch=0, sHEvil=0, sHFood=0, sHReroll=0; // 物資 発生源別収支
            foreach (var r in recs)
            { sCoins+=r.finalCoins; sPeak+=r.peakCoins; sGain+=r.totalGoldGained;
              sStarv+=r.starvationTotal; sStarvHit+=r.starvationHits; sShop+=r.shopPurchases;
              sReroll+=r.shopRerolls; sRerollG+=r.shopRerollCoins; sPrio+=r.priorityItemsAcquired; sMatGain+=r.materialsGainedTotal;
              sFinalProvision+=r.finalProvision; sMinProvision+=r.minProvision; if (r.reachedMadness) madnessCount++;
              sHCombat+=r.provisionCombatLoss; sHComposure+=r.provisionComposureGain; sHLateral+=r.provisionLateralLoss;
              sHMarch+=r.provisionMarchLoss; sHEvil+=r.provisionEvilLoss; sHFood+=r.provisionFoodGain; sHReroll+=r.provisionRerollLoss; }
            int dn = Math.Max(1, n);
            sb.AppendLine($"  {PadR("平均最終ゴールド", 20)}: {(sCoins/dn):F1}");
            sb.AppendLine($"  {PadR("平均ピークゴールド", 20)}: {(sPeak/dn):F1}");
            sb.AppendLine($"  {PadR("平均総獲得ゴールド", 20)}: {(sGain/dn):F1}");
            sb.AppendLine($"  {PadR("平均総獲得素材(pt基準)", 20)}: {(sMatGain/dn):F1}");
            // 値下げ交渉(=強盗)。 1 ラン 1 回までなので 試行数 = 実行したラン数。
            {
                long ra = ItemSystem.Shop.ShopManager.RobberyAttempts;
                long rw = GameManager.RobberyWins, rl = GameManager.RobberyLosses;
                sb.AppendLine($"  {PadR("値下げ交渉", 20)}: 実行 {ra}/{n} ラン ({Pct((int)ra, n)})"
                            + (ra > 0
                               ? $"  勝 {rw} / 敗 {rl} (勝率 {Pct((int)rw, (int)(rw + rl))})"
                                 + $"  平均戦利品 {(rw > 0 ? GameManager.RobberyLootTotal / (double)rw : 0):F1} 件"
                               : "  ※未発動"));
            }
            sb.AppendLine($"  {PadR("平均最終物資", 20)}: {(sFinalProvision/dn):F1}");
            sb.AppendLine($"  {PadR("平均最低物資", 20)}: {(sMinProvision/dn):F1}  (発狂到達 {madnessCount}/{n} = {Pct(madnessCount, n)})");
            sb.AppendLine($"  ── 物資 発生源別収支（1ラン平均・損は−） ──");
            sb.AppendLine($"  {PadR("  戦闘損", 20)}: -{(sHCombat/dn):F1}   {PadR("被弾0回復", 12)}: +{(sHComposure/dn):F1}");
            sb.AppendLine($"  {PadR("  横移動損", 20)}: -{(sHLateral/dn):F1}   {PadR("絶望進軍損", 12)}: -{(sHMarch/dn):F1}");
            sb.AppendLine($"  {PadR("  悪選択損", 20)}: -{(sHEvil/dn):F1}   {PadR("食料回復", 12)}: +{(sHFood/dn):F1}");
            sb.AppendLine($"  {PadR("  振り直し損", 20)}: -{(sHReroll/dn):F1}");
            sb.AppendLine($"  {PadR("ショップ購入数", 20)}: {(sShop/dn):F2}");
            // --- パッシブ取得の経路別内訳 (2026-09-09) ---
            //   供給の 2/3 が無料経路で、 価格を上げても届かない。 どこが配っているかを出す。
            {
                var au = ItemSystem.Helpers.PassiveSourceAudit.Counts;
                if (au.Count > 0)
                {
                    int tot = 0; foreach (var kv in au) tot += kv.Value;
                    // **(double) を必ず挟む。** dn が整数だと int/int で切り捨てられ、
                    //   全部 "8.00 品" のような整数になって内訳が読めなくなる (2026-09-09 に実際に出した)。
                    double srcRuns = Math.Max(1, n);
                    sb.AppendLine($"  ── パッシブ取得の経路別 (1ラン平均 / 計 {(tot / srcRuns):F2} 品) ──");
                    var srcKeys = new List<string>(au.Keys);
                    srcKeys.Sort((a, b) => au[b].CompareTo(au[a]));
                    foreach (var k in srcKeys)
                        sb.AppendLine($"  {PadR("  " + k, 20)}: {(au[k] / srcRuns):F2} 品  ({au[k] * 100.0 / tot:F1}%)");
                }
            }
            // --- 戦闘ドロップの実効率 (2026-09-15) ---
            //   設定値ではなく**実際に落ちた率**を出す。 エリートと通常を分けて数えないと、
            //   「どちらの経路が供給しているか」が合算に埋もれる。
            {
                long er = GameLoop.CombatRewards.EliteRolls, ed = GameLoop.CombatRewards.EliteDrops;
                long nr = GameLoop.CombatRewards.NormalRolls, nd = GameLoop.CombatRewards.NormalDrops;
                if (er + nr > 0)
                    sb.AppendLine($"  {PadR("戦闘ドロップ実効率", 20)}: "
                        + $"エリート {(er > 0 ? ed * 100.0 / er : 0):F1}% ({ed}/{er})"
                        + $" / 通常 {(nr > 0 ? nd * 100.0 / nr : 0):F1}% ({nd}/{nr})"
                        + $"  期待比 {(nr > 0 && nd > 0 ? (ed / (double)er) / (nd / (double)nr) : 0):F1} 倍");
            }
            {
                var RO = GameLoop.GameManager.RerollOutcome;
                long rtot = 0; foreach (var v in RO) rtot += v;
                if (rtot > 0)
                    sb.AppendLine($"  {PadR("リロール要求の結果", 20)}: 成功 {100.0*RO[3]/rtot:F1}%"
                        + $" / フェーズ不一致 {100.0*RO[0]/rtot:F1}%"
                        + $" / Instance無し {100.0*RO[1]/rtot:F1}%"
                        + $" / TryReroll=false {100.0*RO[2]/rtot:F1}%"
                        + $"  (内訳: 店無し {ItemSystem.Shop.ShopManager.RerollFailNoShop / dn:F2}"
                        + $" / 金不足 {ItemSystem.Shop.ShopManager.RerollFailNoGold / dn:F2}"
                        + $" / 上限到達 {ItemSystem.Shop.ShopManager.RerollFailCapped / dn:F2}"
                        + $" / **無料リロール {ItemSystem.Shop.ShopManager.FreeRolls / dn:F2}** 回/ラン)");
            }
            {
                long gt = 0;
                for (int i = 0; i < 4; i++) gt += GrantOk[i] + GrantNg[i];
                if (gt > 0)
                {
                    sb.AppendLine($"  ── ランダム付与の配布結果 (計 {(double)gt / dn:F2} 品/ラン) ──");
                    for (int i = 0; i < 4; i++)
                    {
                        long tot = GrantOk[i] + GrantNg[i];
                        if (tot <= 0) continue;
                        sb.AppendLine($"  {PadR("  " + GrantKindName[i], 22)}: 届いた {(double)GrantOk[i] / dn,6:F3}"
                            + $" / 空振り {(double)GrantNg[i] / dn,6:F3} 品/ラン"
                            + $"  (成功 {100.0 * GrantOk[i] / tot,5:F1}%)");
                    }
                }
            }
            {
                long rt = 0; double rg = 0;
                for (int i = 0; i < 6; i++) { rt += RerollPathCount[i]; rg += RerollPathCoins[i]; }
                if (rt > 0)
                {
                    sb.AppendLine($"  ── リロールの経路別 (計 {(double)rt / dn:F2} 回 / {rg / dn:F1}G) ──");
                    for (int i = 0; i < 6; i++)
                    {
                        if (RerollPathCount[i] <= 0) continue;
                        // **(double) を落とさないこと。** RerollPathCount は long、 dn は int なので
                        //   キャストが無いと整数除算で 0.91 回/ラン が 0 と印字される (2026-09-17 に踏んだ)。
                        sb.AppendLine($"  {PadR("  " + RerollPathName[i], 22)}: {(double)RerollPathCount[i] / dn,6:F3} 回/ラン"
                            + $" ({100.0 * RerollPathCount[i] / rt,4:F1}%)"
                            + $"  {RerollPathCoins[i] / dn,6:F1}G ({100.0 * RerollPathCoins[i] / System.Math.Max(1e-9, rg),4:F1}%)"
                            + $"  1回あたり {RerollPathCoins[i] / RerollPathCount[i],5:F1}G");
                    }
                }
            }
            {
                long st = 0; foreach (var v in SurplusExit) st += v;
                if (st > 0)
                {
                    string[] why = { "リロール不可", "水位/買えない", "リロール失敗", "回数上限", "価値で停止" };
                    sb.AppendLine($"  ── 余剰リロールの出口 (店 {(double)st / dn:F1}/ラン) ──");
                    for (int i = 0; i < 5; i++)
                        if (SurplusExit[i] > 0)
                            sb.AppendLine($"  {PadR("  " + why[i], 22)}: {100.0 * SurplusExit[i] / st,5:F1}%"
                                + $"  そのときの残金 平均 {SurplusExitCoins[i] / SurplusExit[i],6:F1}G");
                }
            }
            if (DiscardRolls > 0)
            {
                long dAll = DiscardPassive + DiscardPart + DiscardWeapon;
                sb.AppendLine($"  {PadR("リロールが捨てた棚", 20)}: {(double)DiscardRolls / dn:F2} 回/ラン で"
                    + $" フェーズ3 購入対象を {(double)dAll / dn:F2} 品/ラン"
                    + $" (1 回あたり {(double)dAll / DiscardRolls:F2}: パッシブ {(double)DiscardPassive / DiscardRolls:F2}"
                    + $" / パーツ {(double)DiscardPart / DiscardRolls:F2} / 武器 {(double)DiscardWeapon / DiscardRolls:F2})"
                    + $"  準パワー平均 {(dAll > 0 ? DiscardPower / dAll : 0):F3}"
                    + $" / スコア足切り品 {(dAll > 0 ? 100.0 * DiscardJunk / dAll : 0):F0}%");
                string[] nthName = { "1回目", "2回目", "3回目", "4回目以降" };
                var nsb = new System.Text.StringBuilder();
                for (int k = 0; k < 4; k++)
                    if (DiscardRollsByNth[k] > 0)
                        nsb.Append($" {nthName[k]} {(double)DiscardRollsByNth[k] / dn:F2}回×{(double)DiscardItemsByNth[k] / DiscardRollsByNth[k]:F2}品");
                sb.AppendLine($"  {PadR("  何回目で捨てたか", 20)}:{nsb}");
            }
            if (RuleGo + RuleStop > 0)
            {
                long rn = RuleGo + RuleStop;
                sb.AppendLine($"  {PadR("停止則の判断", 20)}: 回す {100.0 * RuleGo / rn:F1}% / 止める {100.0 * RuleStop / rn:F1}%"
                    + $"  見込み平均 {RuleGainSum / rn:F3} band / 費用平均 {RuleCostSum / rn:F3} band"
                    + $"  (1G={GoldBandRate:F4} / 事前値 {RerollShelfValue:F2}×{RerollPriorWeight:F0})");
            }
            if (GameLoop.GoldIncome.BySource.Count > 0)
            {
                var src = new List<KeyValuePair<string, long>>(GameLoop.GoldIncome.BySource);
                src.Sort((a, b) => b.Value.CompareTo(a.Value));
                long gtot = 0; foreach (var kv in src) gtot += kv.Value;
                sb.AppendLine($"  ── ゴールド獲得の経路別 (1ラン平均 / 計 {gtot / dn:F1}G) ──");
                foreach (var kv in src)
                    sb.AppendLine($"  {PadR("  " + kv.Key, 20)}: {kv.Value / dn,7:F1}G"
                                + $"  ({kv.Value * 100.0 / System.Math.Max(1, gtot):F1}%)");
            }
            if (ShopExitRuns > 0)
                sb.AppendLine($"  {PadR("最後に店を出た時点", 20)}: 残金 {ShopExitCoinsSum / ShopExitRuns:F1}G"
                    + $" → 終了時までの増分 {PostShopGainSum / ShopExitRuns:+0.0;-0.0}G"
                    + $"  (店を踏んだラン {100.0 * ShopExitRuns / dn:F0}%)");
            for (int k = 0; k < 2; k++)
            {
                if (ExitByOutcomeN[k] <= 0) continue;
                double en = ExitByOutcomeN[k];
                string f7 = Floor7EntryN[k] > 0
                    ? $" / 7層の店に入った時 {Floor7EntryCoins[k] / Floor7EntryN[k]:F1}G (n={Floor7EntryN[k]})" : "";
                sb.AppendLine($"  {PadR(k == 0 ? "  └ クリアしたラン" : "  └ しなかったラン", 20)}: "
                    + $"最終店 (平均 {ExitByOutcomeFloor[k] / en:F1} 層) を出た時 {ExitByOutcomeCoins[k] / en:F1}G"
                    + $" → 増分 {ExitByOutcomeGain[k] / en:+0.0;-0.0}G{f7}  (n={ExitByOutcomeN[k]})");
            }
            if (EndGoldClearN + EndGoldDeadN > 0)
                sb.AppendLine($"  {PadR("ラン終了時の残金", 20)}: "
                    + $"クリア {(EndGoldClearN > 0 ? EndGoldClearSum / EndGoldClearN : 0):F1}G"
                    + $" (30G以上 {(EndGoldClearN > 0 ? 100.0 * EndGoldClear30 / EndGoldClearN : 0):F0}%)"
                    + $" / 非クリア {(EndGoldDeadN > 0 ? EndGoldDeadSum / EndGoldDeadN : 0):F1}G"
                    + $" (30G以上 {(EndGoldDeadN > 0 ? 100.0 * EndGoldDead30 / EndGoldDeadN : 0):F0}%)"
                    + $"  ※店を出る基準 = 残金 < リロール価格 + {SurplusBuyReserve}G");
            {
                long shops = ItemSystem.Shop.ShopManager.SaleShops;
                if (shops > 0)
                {
                    double want = (double)ItemSystem.Shop.ShopManager.SaleWanted / shops;
                    double cand = (double)ItemSystem.Shop.ShopManager.SaleCandidates / shops;
                    double appl = (double)ItemSystem.Shop.ShopManager.SaleApplied / shops;
                    double pct  = 100.0 * ItemSystem.Shop.ShopManager.SaleApplied
                                / System.Math.Max(1L, ItemSystem.Shop.ShopManager.SaleWanted);
                    double saved = ItemSystem.Shop.ShopManager.SaleDiscountSum / dn;
                    sb.AppendLine($"  {PadR("商才〈特売〉", 20)}: 店 {shops / dn:F1}/ラン"
                        + $" / 物資枠 {want:F1} / 候補 {cand:F1}"
                        + $" / **実際に乗った {appl:F1}** ({pct:F0}% 消化)"
                        + $" / 浮いた金 {saved:F1}G/ラン");
                }
            }
            if (DangerSamples > 0)
                sb.AppendLine($"  {PadR("危険度の内訳", 20)}: budget 平均 {DangerBudgetSum / DangerSamples:F1}"
                    + $" / tail 平均 {DangerTailSum / DangerSamples:F1}"
                    + $" / **budget が勝つ {100.0 * DangerBudgetWins / DangerSamples:F1}%**"
                    + $" / 出力 {DangerOutSum / DangerSamples:F3}"
                    + $" (下限張付 {100.0 * DangerClampLo / DangerSamples:F1}% 上限張付 {100.0 * DangerClampHi / DangerSamples:F1}%)");
            if (GpSamples > 0)
                sb.AppendLine($"  {PadR("金逼迫度 gp の分布", 20)}: 平均 {GpSum / GpSamples:F3}"
                    + $" / gp=0 (所持{(int)GoldComfort}G以上) {100.0 * GpZero / GpSamples:F1}%"
                    + $" / gp=1 (無一文) {100.0 * GpOne / GpSamples:F1}%"
                    + $"  ※0 のとき稼ぎ項とエリート報酬項が消える");
            sb.AppendLine($"  {PadR("平均リロール回数", 20)}: {(sReroll/dn):F2}  (平均消費 {(sRerollG/dn):F1}G)");
            sb.AppendLine($"  {PadR("平均優先アイテム取得", 20)}: {(sPrio/dn):F2}");
            sb.AppendLine();

            // ================================================================
            //  敵別 脅威度 (2026-09-09)
            //
            //  **撃破数や死因だけでは「弱い敵」は見つからない。** 死因は「とどめを刺した敵」の
            //  集計なので、 一度も殺さないが毎回削ってくる敵と、 何もせず溶ける敵が同じ 0 になる。
            //  脅威は **プレイヤー最大HP に対する被ダメ%** で測る ── 生の被ダメだと層が進んで
            //  HP が伸びた分だけ大きく出て、 1層の敵が過小評価される。
            //  速度は **平均ターン数と 1〜2 ターン撃破率**。 両方低い敵が「強化対象」。
            // ================================================================
            {
                var eAgg = new Dictionary<string, int[]>();     // n, turns, fast12, won
                var eAggD = new Dictionary<string, double[]>(); // taken, takenPct, dealt, eMaxHP, floor
                foreach (var r in recs)
                {
                    if (r?.combats == null) continue;
                    foreach (var c in r.combats)
                    {
                        if (c == null || string.IsNullOrEmpty(c.enemyId)) continue;
                        if (!eAgg.TryGetValue(c.enemyId, out var a))
                        { a = new int[4]; eAgg[c.enemyId] = a; eAggD[c.enemyId] = new double[5]; }
                        var dv = eAggD[c.enemyId];
                        a[0]++; a[1] += c.turns;
                        if (c.turns <= 2) a[2]++;
                        if (c.won) a[3]++;
                        dv[0] += c.damageTaken;
                        dv[1] += c.playerMaxHpEnd > 0 ? 100.0 * c.damageTaken / c.playerMaxHpEnd : 0;
                        dv[2] += c.damageDealt;
                        dv[3] += c.enemyMaxHP;
                        dv[4] += c.floor;
                    }
                }
                if (eAgg.Count > 0)
                {
                    var ek = new List<string>(eAgg.Keys);
                    // 脅威の低い順 = 被ダメ%HP の小さい順
                    ek.Sort((x, y) => (eAggD[x][1] / eAgg[x][0]).CompareTo(eAggD[y][1] / eAgg[y][0]));
                    sb.AppendLine();
                    sb.AppendLine("---- 敵別 脅威度 (被ダメ%HP の低い順 / 遭遇 100 以上) ----");
                    sb.AppendLine("  敵ID                       層   遭遇   勝率  平均T  ≤2T率  被ダメ  被ダメ%HP  敵HP  与ダメ");
                    foreach (var k in ek)
                    {
                        var a = eAgg[k]; var dv = eAggD[k];
                        if (a[0] < 100) continue;
                        double en = a[0];
                        sb.AppendLine($"  {PadR(k, 26)} {dv[4] / en,3:F1} {a[0],6} {100.0 * a[3] / en,5:F1}% "
                                    + $"{a[1] / en,6:F1} {100.0 * a[2] / en,5:F0}% {dv[0] / en,7:F1} "
                                    + $"{dv[1] / en,9:F1}% {dv[3] / en,5:F0} {dv[2] / en,6:F0}");
                    }
                    // 〈鏡映の応答〉(4層ボス) の発火率。 閾値が高すぎ/低すぎを一目で判る形にする。
                    {
                        long h = ItemSystem.PassiveSkills.Effects.MirrorTwinsResponse.Hits;
                        long f = ItemSystem.PassiveSkills.Effects.MirrorTwinsResponse.Fires;
                        long rd = ItemSystem.PassiveSkills.Effects.MirrorTwinsResponse.ReflectDamage;
                        if (h > 0)
                        {
                            sb.AppendLine($"  [鏡映の応答] 被弾 {h} 回中 {f} 回発火 ({100.0 * f / h:F1}%) / "
                                        + $"反射合計 {rd} (1発火あたり {(f > 0 ? (double)rd / f : 0):F1})");
                            sb.AppendLine("    1ヒットの威力 (ボス最大HP比) の分位: "
                                        + ItemSystem.PassiveSkills.Effects.MirrorTwinsResponse.DescribePercentiles()
                                        + "  ← 閾値をここから選ぶ");
                        }
                        else
                            sb.AppendLine("  [鏡映の応答] 被弾イベント 0 ── 4層ボスに到達していないか計装が繋がっていない");
                    }
                    sb.AppendLine("  ※ 被ダメ%HP = その戦闘の被ダメ ÷ 戦闘終了時のプレイヤー最大HP。");
                    sb.AppendLine("     **被ダメ%HP が低く、 かつ ≤2T率 が高い敵が強化対象** ── 何もせず溶けている。");
                }
            }

            // --- 5F突入時 確信チェーン進行状況 ---
            // 5F到達ランのみ対象。 各種フラグ/パッシブ所持割合を出して、 どこで詰まったか分析する。
            var arrived5F = recs.FindAll(r => r.convictionStageAt5F >= 0);
            if (arrived5F.Count > 0)
            {
                sb.AppendLine("---- 5F突入時 確信チェーン進行 ----");
                int total5F = arrived5F.Count;
                int yogen   = arrived5F.FindAll(r => r.hadFlagYogenAt5F).Count;
                int kakushin= arrived5F.FindAll(r => r.hadFlagKakushinAt5F).Count;
                int gen     = arrived5F.FindAll(r => r.hadConvictionItem5F).Count;
                int ketsui  = arrived5F.FindAll(r => r.hadResolveAt5F).Count;
                int shinri  = arrived5F.FindAll(r => r.hadTruthAt5F).Count;
                int s0 = arrived5F.FindAll(r => r.convictionStageAt5F == 0).Count;
                int s1 = arrived5F.FindAll(r => r.convictionStageAt5F == 1).Count;
                int s2 = arrived5F.FindAll(r => r.convictionStageAt5F == 2).Count;
                int s3 = arrived5F.FindAll(r => r.convictionStageAt5F == 3).Count;
                int s4plus = arrived5F.FindAll(r => r.convictionStageAt5F >= 4).Count;
                sb.AppendLine($"  5F到達ラン数      : {total5F}");
                sb.AppendLine($"  フラグ[苦難の予言]: {PadL(yogen.ToString(),4)}  ({Pct(yogen, total5F)})  ※チェーン1段目完了");
                sb.AppendLine($"  フラグ[苦難の確信]: {PadL(kakushin.ToString(),4)}  ({Pct(kakushin, total5F)})  ※チェーン2段目完了");
                sb.AppendLine($"  〈根拠のない確信〉: {PadL(gen.ToString(),4)}  ({Pct(gen, total5F)})  ※チェーン3段目完了 (stage 1)");
                sb.AppendLine($"  〈決意〉所持      : {PadL(ketsui.ToString(),4)}  ({Pct(ketsui, total5F)})  ※stage 2-3 = 6F進入可");
                sb.AppendLine($"  〈真理〉所持      : {PadL(shinri.ToString(),4)}  ({Pct(shinri, total5F)})  ※stage 4+ = 7F進入可");
                sb.AppendLine($"  stage 0           : {PadL(s0.ToString(),4)}  ({Pct(s0, total5F)})");
                sb.AppendLine($"  stage 1           : {PadL(s1.ToString(),4)}  ({Pct(s1, total5F)})");
                sb.AppendLine($"  stage 2           : {PadL(s2.ToString(),4)}  ({Pct(s2, total5F)})");
                sb.AppendLine($"  stage 3           : {PadL(s3.ToString(),4)}  ({Pct(s3, total5F)})");
                sb.AppendLine($"  stage 4+          : {PadL(s4plus.ToString(),4)}  ({Pct(s4plus, total5F)})");
                sb.AppendLine();
            }

            // --- タイル踏破分布（イベント実遭遇数の検証用） ---
            // 1ランあたり、各タイル種別を実際に何回起動したか（再訪・消化済みは含まない）。
            // 「イベントを順に3回踏むのが難しい」仮説の真偽を実数で確認する。
            if (recs.Count > 0)
            {
                int runN = recs.Count;
                sb.AppendLine("---- タイル踏破分布（1ランあたり平均・実起動回数） ----");
                var order = new[]
                {
                    TileType.Battle, TileType.EliteBattle, TileType.Event, TileType.Exchange,
                    TileType.Shop, TileType.Treasure, TileType.Rest, TileType.Trap,
                };
                foreach (var tt in order)
                {
                    long sum = 0; int runsWithAny = 0;
                    foreach (var r in recs)
                    {
                        r.tileVisits.TryGetValue(tt, out int c);
                        sum += c;
                        if (c > 0) runsWithAny++;
                    }
                    double avg = (double)sum / runN;
                    sb.AppendLine($"  {PadR(tt.ToString(), 12)}: 平均 {avg:F2}/ラン  (総{sum}, 1回以上踏破 {Pct(runsWithAny, runN)})");
                }
                sb.AppendLine();
            }

            // --- 6/7層ボス戦のプレイヤー回復・シールド量（1戦平均・検証用） ---
            // 回復/シールド依存ビルドが各層ボスでどれだけ"延命資源"を獲得しているかを可視化。
            // 1戦 = OnBattleEnded で確定した戦闘（7層ヴェスカ連戦は最終段の1件に連戦全体の累計を計上）。
            {
                long h6 = 0, s6 = 0, h7 = 0, s7 = 0; int n6 = 0, n7 = 0;
                foreach (var r in recs)
                    foreach (var cb in r.combats)
                    {
                        if (!cb.isFightEnd || !cb.isBoss) continue;
                        if (cb.floor == 6) { h6 += cb.healApplied; s6 += cb.shieldGained; n6++; }
                        else if (cb.floor == 7) { h7 += cb.healApplied; s7 += cb.shieldGained; n7++; }
                    }
                sb.AppendLine("---- 6/7層ボス戦 プレイヤー回復・シールド量（1戦平均） ----");
                sb.AppendLine($"  6層ボス: {n6}戦  回復 {(n6 > 0 ? (double)h6 / n6 : 0):F1}/戦  シールド {(n6 > 0 ? (double)s6 / n6 : 0):F1}/戦");
                sb.AppendLine($"  7層ボス: {n7}戦  回復 {(n7 > 0 ? (double)h7 / n7 : 0):F1}/戦  シールド {(n7 > 0 ? (double)s7 / n7 : 0):F1}/戦");
                sb.AppendLine("  （7層はヴェスカ連戦全体の累計を1戦として計上）");
                sb.AppendLine();
            }

            // --- 敵・ボス脅威度 ---
            // key = displayName。boss(enemyId が boss_layer*)は別エントリとして分離集計。
            sb.AppendLine("---- 敵・ボス脅威度 ----");
            var agg = new Dictionary<string, int[]>();    // key -> [0enc,1wins,2losses,3turns,4hpLost,5fatal, 6tWin,7tDraw,8tLoss,9tLossAbs]
            var isBossKey = new Dictionary<string, bool>(); // key -> boss か
            var dispName = new Dictionary<string, string>(); // key -> 表示名

            string KeyOf(CombatRec c)
            {
                bool boss = c.isBoss || GameLoop.BossIds.IsBoss(c.enemyId);
                string nm = string.IsNullOrEmpty(c.enemy) ? (c.enemyId ?? "?") : c.enemy;
                string key = boss ? nm + "BOSS" : nm;
                isBossKey[key] = boss;
                dispName[key] = nm;
                return key;
            }

            foreach (var r in recs)
            {
                foreach (var c in r.combats)
                {
                    string key = KeyOf(c);
                    if (!agg.TryGetValue(key, out var a)) { a = new int[12]; agg[key] = a; }
                    a[0]++;
                    if (c.won) a[1]++; else a[2]++;
                    a[3] += c.turns;
                    a[4] += Math.Max(0, c.hpBefore - c.hpAfter);
                    a[6] += c.tWin; a[7] += c.tDraw; a[8] += c.tLoss; a[9] += c.tLossAbs;
                    // 勝T/敗T の分離: 勝T=削り切る時間(敵HPの尺度) / 敗T=保つ時間(敵加害の尺度)
                    if (c.won) a[10] += c.turns; else a[11] += c.turns;
                }
                // 致命: そのランの最後の「敗北」戦闘をゲームオーバー要因と見なす
                if (r.outcome == Outcome.GameOver && r.combats != null && r.combats.Count > 0)
                {
                    CombatRec fatal = null;
                    for (int i = r.combats.Count - 1; i >= 0; i--)
                        if (!r.combats[i].won) { fatal = r.combats[i]; break; }
                    if (fatal != null)
                    {
                        string key = KeyOf(fatal);
                        if (!agg.TryGetValue(key, out var a)) { a = new int[12]; agg[key] = a; }
                        a[5]++;
                    }
                }
            }

            var keys = new List<string>(agg.Keys);
            keys.Sort((x, y) =>
            {
                // 勝率の低い順（危険＝主指標）。同率は致命数の多い順→遭遇数の多い順。
                double wx = agg[x][0] == 0 ? 1.0 : (double)agg[x][1] / agg[x][0];
                double wy = agg[y][0] == 0 ? 1.0 : (double)agg[y][1] / agg[y][0];
                int c = wx.CompareTo(wy);
                if (c != 0) return c;
                int f = agg[y][5].CompareTo(agg[x][5]);
                return f != 0 ? f : agg[y][0].CompareTo(agg[x][0]);
            });
            sb.AppendLine($"  {PadR("敵名",18)}{PadR("種別",6)}{PadL("遭遇",5)} {PadL("勝率",6)} {PadL("平均T",7)} {PadL("勝T",6)} {PadL("敗T",6)} {PadL("平均被ダメ",11)} {PadL("致命",5)}");
            foreach (var k in keys)
            {
                var a = agg[k];
                string wr = a[0] == 0 ? "-" : (100.0*a[1]/a[0]).ToString("F0")+"%";
                string at = a[0] == 0 ? "-" : ((double)a[3]/a[0]).ToString("F1");
                string wt = a[1] == 0 ? "-" : ((double)a[10]/a[1]).ToString("F1");
                string lt = a[2] == 0 ? "-" : ((double)a[11]/a[2]).ToString("F1");
                string ad = a[0] == 0 ? "-" : ((double)a[4]/a[0]).ToString("F1");
                string nm = dispName.TryGetValue(k, out var dn2) ? dn2 : k;
                string kind = isBossKey.TryGetValue(k, out var b) && b ? "BOSS"
                            : (!string.IsNullOrEmpty(nm) && nm.StartsWith("精鋭")) ? "精鋭"
                            : "雑魚";
                sb.AppendLine($"  {PadR(TruncDisp(nm,16),18)}{PadR(kind,6)}{PadL(a[0].ToString(),5)} {PadL(wr,6)} {PadL(at,7)} {PadL(wt,6)} {PadL(lt,6)} {PadL(ad,11)} {PadL(a[5].ToString(),5)}");
            }
            sb.AppendLine();
            sb.AppendLine("（勝率の低い順にソート。致命=そのランをゲームオーバーに導いた回数）");
            sb.AppendLine("（勝T=撃破できた戦闘の平均ターン＝敵HPの尺度 / 敗T=力尽きた戦闘の平均ターン＝敵加害の尺度）");
            sb.AppendLine();

            // 武器×ダイス 組み合わせ別 戦闘勝率は 2026-06-21 削除 (バンドが肥大化、 ビルド多様性ブロックで代替)。

            // --- ボス戦ターン内訳（非解決グラインドの原因特定） ---
            // ADR-0009 でロール勝負は存在しない。 ここでの 収支+/0/− は
            //   balance = (与ダメ + 固定ダメ) − 被ダメ  の符号 (CombatManager.cs の result.playerWon/isDraw)。
            // 「無為T」= 収支マイナスのターンのうち **プレイヤーの与ダメが 0** だったターン。
            //   旧ラベルは「吸収敗＝シールドで被弾を防いだ」と読める書き方だったが、
            //   実際に見ているのは tr.totalDamage (= プレイヤーが与えた量) なので主語が逆だった。
            sb.AppendLine("---- ボス戦ターン内訳（収支の符号別 ％・無為T=収支マイナスかつ与ダメ0） ----");
            sb.AppendLine($"  {PadR("ボス名",16)}{PadL("総T",6)} {PadL("収支+",6)} {PadL("収支0",6)} {PadL("収支-",6)} {PadL("無為T%",8)}");
            foreach (var k in keys)
            {
                if (!(isBossKey.TryGetValue(k, out var b) && b)) continue;
                var a = agg[k];
                int tot = a[6] + a[7] + a[8];
                if (tot == 0) continue;
                string nm = dispName.TryGetValue(k, out var dn3) ? dn3 : k;
                string pw = (100.0*a[6]/tot).ToString("F0")+"%";
                string pd = (100.0*a[7]/tot).ToString("F0")+"%";
                string pl = (100.0*a[8]/tot).ToString("F0")+"%";
                string pa = a[8]==0 ? "-" : (100.0*a[9]/a[8]).ToString("F0")+"%";
                sb.AppendLine($"  {PadR(TruncDisp(nm,16),16)}{PadL(tot.ToString(),6)} {PadL(pw,6)} {PadL(pd,6)} {PadL(pl,6)} {PadL(pa,8)}");
            }
            sb.AppendLine("（無為T% = 収支マイナスのターンのうち、プレイヤーが1もダメージを与えられなかった割合。");
            sb.AppendLine("　高い = 一方的に殴られている＝手が出ていない。 低い = 削り合いにはなっている）");
            sb.AppendLine();

            // --- 6Fクリア時ビルドスナップショット（サルベージ用） ---
            // 一旦オフ: ログが煩雑になるため出力を抑止（再有効化は EmitClearBuildSnapshot=true）。
            const bool EmitClearBuildSnapshot = false;
            var snaps = recs.FindAll(r => !string.IsNullOrEmpty(r.clear6FSnapshot));
            if (EmitClearBuildSnapshot && snaps.Count > 0)
            {
                sb.AppendLine($"---- 6Fクリア時ビルドスナップショット ({snaps.Count}件・サルベージ用) ----");
                foreach (var r in snaps)
                {
                    sb.AppendLine($"  ● RUN {r.index} [{r.bandLabel}]");
                    sb.AppendLine($"      {r.clear6FSnapshot}");
                }
                sb.AppendLine();
            }

            // --- デッドロック発生時の状況（原因特定用・最下段） ---
            var dls = recs.FindAll(r => r.band == "DEADLOCK");
            if (dls.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"---- デッドロック発生時の状況（{dls.Count}件・原因特定用） ----");
                foreach (var r in dls)
                {
                    sb.AppendLine($"  ● RUN {r.index} : {r.note}");
                    sb.AppendLine($"    reached F{r.reachedFloor}  HP {r.finalHP}/{r.finalMaxHP}  " +
                                  $"coins {r.finalCoins}  combats {r.totalWins}/{r.totalCombats}  " +
                                  $"LS={r.lastStandUsed}");
                    if (_detail.TryGetValue(r.index, out var lines) && lines.Count > 0)
                    {
                        const int tail = 40;
                        int start = Math.Max(0, lines.Count - tail);
                        sb.AppendLine($"    ── 発生時点周辺ログ（末尾 {lines.Count - start} 行 / 総 {lines.Count} 行） ──");
                        for (int i = start; i < lines.Count; i++)
                            sb.AppendLine($"    | {lines[i]}");
                    }
                    else
                    {
                        sb.AppendLine("    （ログ未取得）");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("=============================================");
            return sb.ToString();
        }

        // ===== 表示幅(全角=2)対応の整列ヘルパー =====

        private static int CharW(char ch)
        {
            if (ch < 0x1100) return 1;
            return
                (ch >= 0x1100 && ch <= 0x115F) ||                       // Hangul Jamo
                (ch >= 0x2E80 && ch <= 0xA4CF && ch != 0x303F) ||       // CJK..Yi
                (ch >= 0xAC00 && ch <= 0xD7A3) ||                       // Hangul Syllables
                (ch >= 0xF900 && ch <= 0xFAFF) ||                       // CJK Compat Ideographs
                (ch >= 0xFE30 && ch <= 0xFE4F) ||                       // CJK Compat Forms
                (ch >= 0xFF00 && ch <= 0xFF60) ||                       // Fullwidth Forms
                (ch >= 0xFFE0 && ch <= 0xFFE6)
                ? 2 : 1;
        }

        private static int DispWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            foreach (var c in s) w += CharW(c);
            return w;
        }

        /// <summary>表示幅で左詰めパディング。</summary>
        private static string PadR(string s, int w)
        {
            s = s ?? "";
            int d = DispWidth(s);
            return d >= w ? s : s + new string(' ', w - d);
        }

        /// <summary>表示幅で右詰めパディング。</summary>
        private static string PadL(string s, int w)
        {
            s = s ?? "";
            int d = DispWidth(s);
            return d >= w ? s : new string(' ', w - d) + s;
        }

        /// <summary>表示幅で切り詰め。</summary>
        private static string TruncDisp(string s, int w)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            int acc = 0;
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                int cw = CharW(c);
                if (acc + cw > w) break;
                sb.Append(c);
                acc += cw;
            }
            return sb.ToString();
        }

        private string BuildJsonl()
        {
            var sb = new StringBuilder();
            foreach (var r in _records)
                sb.AppendLine(JsonUtility.ToJson(r));
            return sb.ToString();
        }

        private string BuildDetail()
        {
            var sb = new StringBuilder();
            foreach (var r in _records)
            {
                sb.AppendLine($"########## RUN {r.index}  [{r.band} {r.bandLabel}]  {r.note}");
                sb.AppendLine($"# reached F{r.reachedFloor} HP {r.finalHP}/{r.finalMaxHP} coins {r.finalCoins} " +
                              $"combats {r.totalWins}/{r.totalCombats} LS={r.lastStandUsed}");
                if (_detail.TryGetValue(r.index, out var lines))
                    foreach (var l in lines) sb.AppendLine(l);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}

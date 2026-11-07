using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using AutoTest;
using AutoTest.ParallelSweep;
using AutoTest.Ultra;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Process = System.Diagnostics.Process;

namespace AutoTest.EditorTools
{
    /// <summary>アイテム学習の本走を<b>プロセス並列</b>で回す (2026-11-04)。
    ///
    /// <para><b>逐次版との違いは「1 バッチを何プロセスで走らせるか」だけ。</b> 1 ラウンド = 逐次版の 1 バッチ
    /// (5,000 ラン)。 ラウンドの間に学習を更新し、 次のラウンドはその学習を読んで買う ── 逐次版が
    /// バッチの後に <c>LearnedPriorityProvider.Reload</c> するのと同じ順序を守る。</para>
    ///
    /// <para><b>学習ファイルへ書くのは親だけ。</b> ワーカーはラン記録 (<see cref="LearnRunFile"/>) を返すだけで、
    /// 親がラウンド分をまとめて <c>ItemLearningStats.IngestBatch</c> と <c>RunDataLogger.AppendBatch</c> へ通す。
    /// ワーカーが各自で書くと、 プロセスごとに別の学習が育つ。</para>
    ///
    /// <para><b>ワーカーが読む学習はラウンドごとに張り替える。</b> ワーカーはビルド直下の
    /// <c>AutoRunLogs/learning</c> (凍結スナップショット) を読むので、 張り替えないと 50 ラウンドとも
    /// 最初の学習で買い物をする。 ビルドは最初に 1 回 (<see cref="RankMarginParallelRunner.EnsureFreshBuild"/>)。</para></summary>
    [InitializeOnLoad]
    public static class ItemLearningParallelRunner
    {
        private const string ActiveKey = "AutoRun.LearnParallel.Root";
        private const int RunsPerRound = 5000;
        private const int Rounds = 50;
        /// <summary>シード区間の起点。 r9/r10 (60,000〜) や付与試行と重ならない所に置く。</summary>
        private const int SeedBase = 1_000_000;

        [Serializable]
        private sealed class State
        {
            public int round;          // 走らせている (または次に走らせる) ラウンド
            public int rounds;
            public int runsPerRound;
            public string learningRoot = "";
            public string configKeysJoined = "";
            public string configValuesJoined = "";
            public string buildFingerprint = "";
            public string startedUtc = "";
            /// <summary>"learn" = アイテム学習 / "itt" = 全種プールのランダム付与 (ITT) を 1 ラウンドで回して序列を作る。</summary>
            public string mode = "learn";
            /// <summary>true のとき、 結果を学習へ合流させない (A/B 用: 腕どうしが同じ学習状態で走る)。</summary>
            public bool noMerge;
        }

        private sealed class Slot
        {
            public string jobPath, responsePath, progressPath, logPath, learnPath;
            public int runs;
            public Process process;
            public bool done, failed;
        }

        private static State _st;
        private static string _root = "";
        private static readonly List<Slot> _slots = new List<Slot>();
        private static readonly Queue<Slot> _pending = new Queue<Slot>();
        private static int _maxParallel;
        private static bool _running;
        private static DateTime _roundStartUtc;
        private static double _lastReport;

        static ItemLearningParallelRunner()
        {
            string root = SessionState.GetString(ActiveKey, "");
            if (string.IsNullOrEmpty(root) || !File.Exists(Path.Combine(root, "state.json"))) return;
            EditorApplication.delayCall += () => Resume(root);
        }

        /// <summary>**標準の測定 (2026-11-05〜): ITT の序列で買う。** 先に「ITT 並列」で itt_clear.json を作っておくこと。
        /// 観測の回帰 (regβ) で買う旧方式は、 同じ 5,000 シードで学習なしより悪く (11.9% 対 17.2%)、 ITT は 27.2% だった。</summary>
        [MenuItem("Tools/AutoRun/■ 並列 5000ラン × 50回 (遺物なし・Optimal・ITT の序列で買う)", priority = 8)]
        private static void Launch() => LaunchWith(0);

        /// <summary>診断: 1 層の戦闘をワーカーごとに先頭 8 ランぶんターン単位で記録する (fight_trace.tsv・ラウンドは 50 回のまま、 1 回で中止する)。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 学習ラウンド + 1層の戦闘のターン記録", priority = 14)]
        private static void LaunchWithFightTrace() => LaunchWith(8);

        /// <summary>診断: 先読みの技量 (Super) で 1,000 ラン。 1 層の戦闘のターン記録つき。 Optimal の配線の弱さを切り分ける (2026-11-05)。
        /// 学習ファイルには 1 ラウンド分が積まれるので、 終わったら中止して学習を消すこと。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 Super 1000ラン + 1層の戦闘のターン記録", priority = 14)]
        private static void LaunchSuperDiag() => LaunchWith(8, AutoRunner.WiringSkill.Super, 1000);

        /// <summary>診断: 航行で偵察・照明を使う学習ラウンド。 使わない既定との比較用 (2026-11-05 は使う方が悪化)。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 学習ラウンド (偵察・照明あり)", priority = 14)]
        private static void LaunchWithScout() => LaunchWith(0, AutoRunner.WiringSkill.Optimal, RunsPerRound, scout: true);

        /// <summary>診断: 戦闘名の危険度の学習値で足切りする学習ラウンド (2026-11-05)。 学習値を先に積んでから使うこと。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 学習ラウンド (戦闘名の学習値を使う)", priority = 14)]
        private static void LaunchLearnedDanger() => LaunchWith(0, AutoRunner.WiringSkill.Optimal, RunsPerRound, scout: false, learnedDanger: true);

        /// <summary>診断: 偵察・照明を使い、 戦闘名の学習値で足切りする学習ラウンド (2026-11-05)。 戦闘名は偵察・照明で見つけた点にしか出ないので、 学習値だけでは効かない。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 学習ラウンド (偵察・照明あり + 戦闘名の学習値)", priority = 14)]
        private static void LaunchScoutAndLearned() => LaunchWith(0, AutoRunner.WiringSkill.Optimal, RunsPerRound, scout: true, learnedDanger: true);

        /// <summary>**ITT 本走・全種プール (並列・2026-11-05)。** 消耗品・武器・出目パーツまで 1 ランあたり期待 7 品を無作為に付与し、
        /// 100,000 ラン。 全チャンクの割り当てを 1 本へまとめて <c>GrantItt</c> で再計算し、 購入の序列 (itt_clear.json / itt_effects.json) を作る。
        /// 旧い序列は <c>*.preallkinds</c> 風に退避する。 序列を使うのは別のメニュー ([ITT の序列で学習ラウンド])。</summary>
        [MenuItem("Tools/AutoRun/■ ITT 並列: 全種プール 100000ラン → 購入の序列", priority = 8)]
        private static void LaunchItt() => LaunchWith(0, runsPerRound: 100000, itt: true);

        /// <summary>ITT を Super の方策で取る (2026-11-06)。 配布試行の間も今の ITT の序列で買う (配布は無作為なので割り当ては交絡しない)。
        /// 品の価値は使う方策の下で測る ── Optimal + 古い観測の序列の上で測った値は、 Super の判断とずれる。</summary>
        [MenuItem("Tools/AutoRun/■ ITT 並列 (Super の方策・今の序列で買う): 100000ラン", priority = 8)]
        private static void LaunchIttSuper()
        {
            AutoTest.GrantItt.Variant = "super";   // 書き先を itt_clear.super.json にする (合流の後に戻す)
            LaunchWith(0, AutoRunner.WiringSkill.Super, 100000, itt: true,
                overrides: new Dictionary<string, string> { ["useIttBeta"] = "True" });
        }

        /// <summary>診断: 観測の回帰 (regβ) で買う旧方式。 ITT の序列との比較用 (2026-11-05: 旧方式は学習なしより悪い)。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 学習ラウンド (観測の回帰 regβ で買う・旧)", priority = 14)]
        private static void LaunchObservational() => LaunchWith(0, useItt: false);

        /// <summary>難易度の床・天井の確認 (2026-11-05): 整備パネルの配点を 0pt にする (学習・ITT の序列は残す)。
        /// メタ Off と違い <c>buffOff_debuffOff</c> の学習ディレクトリを作らないので、 「配点だけ 0」を分離して測れる。 1 ラウンドで中止すること。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 5000ラン (メタ配点0pt・Optimal・ITT の序列で買う)", priority = 15)]
        private static void LaunchMetaZero() => LaunchWith(0, overrides: new Dictionary<string, string>
        {
            ["metaBuffMode"] = ((int)AutoRunner.MetaBuffMode.BuildFocused).ToString(CultureInfo.InvariantCulture),
            ["metaBuildAxis"] = ((int)MetaAllocationPresets.Preset.None).ToString(CultureInfo.InvariantCulture),
        });

        /// <summary>難易度の確認: Super・メタ標準・挑戦 0pt を 1,000 ラン。 1 ラウンドで中止すること。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 Super 1000ラン (メタ標準・挑戦0pt)", priority = 15)]
        private static void LaunchSuperBase() => LaunchWith(0, AutoRunner.WiringSkill.Super, 1000);

        /// <summary>難易度の確認: Super・メタ標準・挑戦 全開 (現行の 50pt) を 1,000 ラン。 1 ラウンドで中止すること。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 Super 1000ラン (メタ標準・挑戦全開50pt)", priority = 15)]
        private static void LaunchSuperMaxChallenge() => LaunchWith(0, AutoRunner.WiringSkill.Super, 1000,
            overrides: new Dictionary<string, string> { ["enableAllDebuffs"] = "True" });

        /// <summary>診断: 防ぎ切りの規則 (<c>FreeBlockRule</c>・Optimal 専用) を切って回す。 Super が Optimal より弱い原因の切り分け (2026-11-05)。
        /// 規則ありの Optimal と同じシードで比べる。 1 ラウンドで中止すること。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 5000ラン (防ぎ切りの規則 OFF・Optimal・ITT の序列で買う)", priority = 15)]
        private static void LaunchNoFreeBlock() => LaunchWith(0, overrides: new Dictionary<string, string> { ["FreeBlockRule"] = "False" });

        /// <summary>診断: Super の勝利時の残り HP の重み (<c>superAI.winHpWeight</c>・既定 0.5) を 1.0 にして 1,000 ラン (2026-11-05)。
        /// 防ぎ切りの規則を持つ Optimal との差が、 HP の重みで縮むかを見る。 1 ラウンドで中止すること。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 Super 1000ラン (HP の重み 1.0・メタ標準・挑戦0pt)", priority = 15)]
        private static void LaunchSuperHpWeight() => LaunchWith(0, AutoRunner.WiringSkill.Super, 1000,
            overrides: new Dictionary<string, string> { ["superAI.winHpWeight"] = "1.0" });

        /// <summary>診断: Super の配線候補数 (<c>superAI.candidateWirings</c>・既定 6) を 12 にして 1,000 ラン (2026-11-05)。
        /// 探索を広げると Optimal との差が縮むかを見る。 1 ラウンドで中止すること。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 Super 1000ラン (配線候補 12・メタ標準・挑戦0pt)", priority = 15)]
        private static void LaunchSuperCand12() => LaunchWith(0, AutoRunner.WiringSkill.Super, 1000,
            overrides: new Dictionary<string, string> { ["superAI.candidateWirings"] = "12" });

        /// <summary>診断: Super の新方式 V2 (<c>superAI.useV2</c>・<c>SuperCombatAI.V2.cs</c>) で 1,000 ラン (2026-11-05)。
        /// 旧方式 (同じ学習状態の Super / Optimal) と同じシードで比べる。 1 ラウンドで中止すること。</summary>
        [MenuItem("Tools/AutoRun/診断: 並列 Super V2 1000ラン (メタ標準・挑戦0pt)", priority = 15)]
        private static void LaunchSuperV2() => LaunchWith(0, AutoRunner.WiringSkill.Super, 1000,
            overrides: new Dictionary<string, string> { ["superAI.useV2"] = "True" });

        /// <summary><b>SuperV3 の A/B (2026-11-05)。</b> <c>AutoRunLogs/v3_arm.txt</c> の設定で 1 ラウンドだけ回し、
        /// <b>学習へ合流させない</b>。 1 行 1 設定 (<c>キー=値</c>・<c>#</c> は注釈)。 特別なキー: <c>_runs</c> (ラン数・既定 5000) /
        /// <c>_skill</c> (Optimal か Super・既定 Optimal)。 それ以外は AutoRunner のフィールド (例 <c>v3.rerollEv=True</c> / <c>FreeBlockRule=False</c>)。
        /// 基準アームは設定なし。 腕どうしを <c>Tools/v3_ab.py</c> で同じシードの対にして比べる。</summary>
        [MenuItem("Tools/AutoRun/SuperV3 A/B: v3_arm.txt の設定で 1 ラウンド (学習に合流しない)", priority = 16)]
        private static void LaunchV3Arm()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AutoRunLogs", "v3_arm.txt"));
            LaunchV3ArmFrom(File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8) : new string[0]);
        }

        // ---- キュー: 複数の腕を順番に自動で回す (2026-11-05) ----
        private static readonly List<string[]> _queue = new List<string[]>();

        /// <summary><c>AutoRunLogs/v3_queue.txt</c> を読み、 <c>---</c> 行で区切られた腕を<b>順番に</b>回す。
        /// 1 つ終わるごとに次を自動で始める。 各腕の書式は v3_arm.txt と同じ。 腕の結果は実行ディレクトリごとに残る
        /// (<c>config.txt</c> に設定が載る)。</summary>
        [MenuItem("Tools/AutoRun/SuperV3 A/B: v3_queue.txt の腕を順番に全部回す", priority = 16)]
        private static void LaunchV3Queue()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AutoRunLogs", "v3_queue.txt"));
            if (!File.Exists(path)) { Debug.LogError("[V3] v3_queue.txt が無い"); return; }
            _queue.Clear();
            var cur = new List<string>();
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (raw.Trim() == "---") { _queue.Add(cur.ToArray()); cur = new List<string>(); }
                else cur.Add(raw);
            }
            _queue.Add(cur.ToArray());
            Debug.Log($"[V3] キュー {_queue.Count} 本");
            LaunchNextQueued();
        }

        private static void LaunchNextQueued()
        {
            if (_queue.Count == 0) return;
            var arm = _queue[0]; _queue.RemoveAt(0);
            LaunchV3ArmFrom(arm);
        }

        private static void LaunchV3ArmFrom(string[] lines)
        {
            var ov = new Dictionary<string, string>();
            int runs = RunsPerRound; var skill = AutoRunner.WiringSkill.Optimal;
            foreach (var raw in lines)
                {
                    string ln = raw.Trim();
                    if (ln.Length == 0 || ln[0] == '#') continue;
                    int eq = ln.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = ln.Substring(0, eq).Trim(), v = ln.Substring(eq + 1).Trim();
                    if (k == "_runs") int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out runs);
                    else if (k == "_skill") Enum.TryParse(v, true, out skill);
                    else ov[k] = v;
                }
            LaunchWith(0, skill, runs, overrides: ov, ab: true);
        }

        private static void LaunchWith(int fightTraceRuns,
            AutoRunner.WiringSkill skill = AutoRunner.WiringSkill.Optimal, int runsPerRound = RunsPerRound, bool scout = false,
            bool learnedDanger = false, bool itt = false, bool useItt = true, Dictionary<string, string> overrides = null, bool ab = false)
        {
            if (_running) { Debug.LogWarning("[学習並列] すでに走っている"); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogError("[学習並列] Play Mode 中は起動しない"); return; }
            if (!RankMarginParallelRunner.EnsureFreshBuild(out string reason))
            { Debug.LogError("[学習並列] 中止: " + reason); return; }
            if (!UltraProductionWorkerBuild.TryLoadDescriptor(out var desc, out reason))
            { Debug.LogError("[学習並列] 中止: " + reason); return; }

            RankMarginParallelRunner.SnapshotConfig(out string[] keys, out string[] values);
            var cfg = new Dictionary<string, string>();
            for (int i = 0; i < keys.Length; i++) cfg[keys[i]] = values[i];
            // **逐次版 (AutoRunMenu.RunItemLearning) と同じ条件をここで決める。** EditorPrefs の残りに頼らない。
            cfg["wiringSkill"] = ((int)skill).ToString(CultureInfo.InvariantCulture);
            cfg["metaBuffMode"] = ((int)AutoRunner.MetaBuffMode.Standard).ToString(CultureInfo.InvariantCulture);
            cfg["itemPickMode"] = ((int)AutoRunner.ItemPickMode.TierBased).ToString(CultureInfo.InvariantCulture);
            cfg["forceNoRelic"] = "True";
            cfg["forceTheoreticalRelic"] = "False";
            cfg["lockWeaponFamily"] = "True";
            cfg["enableAllDebuffs"] = "False";
            cfg["sweepAllMetaAxes"] = "False";
            cfg["suppressFacePartOffers"] = "False";
            cfg["challengeScoreTarget"] = "0";
            cfg["challengeSpec"] = "";
            cfg["metaRankSpec"] = "";
            // ITT は付与試行で別に作る推定量。 学習し直しの最中は観測の序列 (regβ) で買う
            //   ── 古い ITT を読むと、 品目の入れ替わった今のカタログを旧品の効果で並べてしまう。
            cfg["useIttBeta"] = (useItt && !itt) ? "True" : "False";   // 付与試行の最中は序列を使わない (処置の割り当てに交絡させない)
            if (itt)
            {
                // 付与試行: 1 ランに複数品を無作為に配る。 条件は学習ラウンドと同じ (遺物なし・イベントなし・Optimal・Standard)。
                cfg["randomGrantTrial"] = "True";
                cfg["randomGrantAllKinds"] = "True";
                cfg["randomGrantExpectedItems"] = "7";
                cfg["lockWeaponFamily"] = "False";
                cfg["learnTier"] = "False";
            }
            cfg["fightTraceRuns"] = fightTraceRuns.ToString(CultureInfo.InvariantCulture);
            // 2026-11-05: 一旦イベントを切る (ユーザー指示)。 確信の連鎖 (層 1 で HP 半減 + 啓示のエリート) が
            //   層 1 の死亡を押し上げていたため、 バランス測定から外す。 確信の資格は本体が無償で与える。
            cfg["suppressEvents"] = "True";
            if (ab) cfg["measureNoExplore"] = "True";   // 学習に合流しない測定にランダムな買い物を混ぜない
            cfg["freeNavScout"] = scout ? "True" : "False";
            cfg["navLearnedDanger"] = learnedDanger ? "True" : "False";
            if (overrides != null) foreach (var kv in overrides) cfg[kv.Key] = kv.Value;

            var ks = new List<string>(cfg.Keys);
            var vs = new List<string>(); foreach (var k in ks) vs.Add(cfg[k]);

            _root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AutoRunLogs",
                "learn_parallel", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(_root);
            _st = new State
            {
                round = 0, rounds = (itt || ab) ? 1 : Rounds, runsPerRound = runsPerRound, mode = itt ? "itt" : "learn", noMerge = ab,
                learningRoot = MetaProfileHelper.BotLearningRoot(0),
                configKeysJoined = string.Join("\n", ks),
                configValuesJoined = string.Join("\n", vs),
                buildFingerprint = desc.fingerprints?.buildFingerprint ?? "",
                startedUtc = DateTime.UtcNow.ToString("O"),
            };
            SaveState();
            SessionState.SetString(ActiveKey, _root);
            var sb = new StringBuilder("=== アイテム学習 並列: ワーカーへ渡した設定 ===\n");
            for (int i = 0; i < ks.Count; i++) sb.AppendLine($"  {ks[i],-26} = {vs[i]}");
            sb.AppendLine($"  学習の書き先 = {_st.learningRoot}");
            File.WriteAllText(Path.Combine(_root, "config.txt"), sb.ToString(), new UTF8Encoding(false));

            _running = true;
            EditorApplication.update += Poll;
            StartRound();
        }

        [MenuItem("Tools/AutoRun/■ アイテム学習 並列: 中止", priority = 8)]
        private static void Abort()
        {
            if (!_running) { Debug.Log("[学習並列] 走っていない"); return; }
            foreach (var s in _slots) try { if (s.process != null && !s.process.HasExited) s.process.Kill(); } catch { }
            Stop("中止した (このラウンドは合流させていない)");
        }

        private static void Resume(string root)
        {
            if (_running) return;
            try
            {
                _root = root;
                _st = JsonUtility.FromJson<State>(File.ReadAllText(Path.Combine(root, "state.json"), Encoding.UTF8));
                if (_st == null || _st.round >= _st.rounds) { SessionState.EraseString(ActiveKey); return; }
                // 走っていたラウンドの枠を job.json から組み直す。 起動前のものだけ積み直し、
                //   起動済みはプロセスが OS 側で生きているので response を待つ。
                _slots.Clear(); _pending.Clear();
                string dir = RoundDir(_st.round);
                if (!Directory.Exists(dir)) { _running = true; EditorApplication.update += Poll; StartRound(); return; }
                foreach (string d in Directory.GetDirectories(dir))
                {
                    var s = MakeSlot(d, 0);
                    if (!File.Exists(s.jobPath)) continue;
                    s.runs = JsonUtility.FromJson<RankMarginWorkerJob>(File.ReadAllText(s.jobPath, Encoding.UTF8)).runs;
                    _slots.Add(s);
                    if (!File.Exists(s.responsePath) && !File.Exists(s.progressPath)) _pending.Enqueue(s);
                }
                _maxParallel = Mathf.Max(1, SystemInfo.processorCount - 2);
                _roundStartUtc = DateTime.UtcNow;
                _running = true;
                Debug.LogWarning($"[学習並列] ドメインリロードから復帰: ラウンド {_st.round + 1}/{_st.rounds}\n  {root}");
                EditorApplication.update += Poll;
                Pump();
            }
            catch (Exception ex)
            {
                Debug.LogError("[学習並列] 復帰に失敗: " + ex.Message);
                SessionState.EraseString(ActiveKey);
            }
        }

        private static string RoundDir(int round) => Path.Combine(_root, "round_" + round.ToString("D3"));

        private static Slot MakeSlot(string dir, int runs) => new Slot
        {
            runs = runs,
            jobPath = Path.Combine(dir, "job.json"),
            responsePath = Path.Combine(dir, "response.json"),
            progressPath = Path.Combine(dir, "progress.txt"),
            logPath = Path.Combine(dir, "worker.log"),
            learnPath = Path.Combine(dir, "learn_runs.json"),
        };

        private static void StartRound()
        {
            RefreshWorkerSnapshot();
            _slots.Clear(); _pending.Clear();
            _maxParallel = Mathf.Max(1, SystemInfo.processorCount - 2);
            int chunks = _maxParallel;
            int baseRuns = _st.runsPerRound / chunks, extra = _st.runsPerRound % chunks, offset = 0;
            string[] keys = _st.configKeysJoined.Split('\n');
            string[] values = _st.configValuesJoined.Split('\n');
            for (int c = 0; c < chunks; c++)
            {
                int size = baseRuns + (c < extra ? 1 : 0);
                if (size <= 0) continue;
                string dir = Path.Combine(RoundDir(_st.round), "c" + c.ToString("D2"));
                Directory.CreateDirectory(dir);
                var slot = MakeSlot(dir, size);
                var jobKeys = keys; var jobValues = values;
                if (_st.mode == "itt")   // 付与試行の ラン番号 = 90000 + 開始位置 + i ── チャンクごとに区間を分ける
                {
                    jobKeys = new string[keys.Length + 1]; jobValues = new string[values.Length + 1];
                    System.Array.Copy(keys, jobKeys, keys.Length); System.Array.Copy(values, jobValues, values.Length);
                    jobKeys[keys.Length] = "randomGrantStartIndex"; jobValues[values.Length] = offset.ToString(CultureInfo.InvariantCulture);
                }
                var job = new RankMarginWorkerJob
                {
                    label = $"learn r{_st.round + 1}",
                    runs = size,
                    seedStart = SeedBase + _st.round * _st.runsPerRound + offset,
                    configKeys = jobKeys,
                    configValues = jobValues,
                    responsePath = slot.responsePath,
                    progressPath = slot.progressPath,
                    outputRoot = Path.Combine(dir, "AutoRunLogs"),
                    buildFingerprint = _st.buildFingerprint,
                    learnRunsPath = _st.mode == "itt" ? "" : slot.learnPath,
                };
                File.WriteAllText(slot.jobPath, JsonUtility.ToJson(job, true), new UTF8Encoding(false));
                _slots.Add(slot);
                _pending.Enqueue(slot);
                offset += size;
            }
            _roundStartUtc = DateTime.UtcNow;
            _lastReport = 0;
            Debug.Log($"[学習並列] ラウンド {_st.round + 1}/{_st.rounds} 開始: {_st.runsPerRound:N0} ラン / {_slots.Count} プロセス");
            Pump();
        }

        /// <summary>ワーカーが読む学習 (ビルド直下の写し) を、 親の学習の今の中身で置き換える。
        /// 写すのは band_0 の直下のファイルだけ (帯別のサブフォルダは今回の学習に関係しない)。</summary>
        private static void RefreshWorkerSnapshot()
        {
            string learningBase = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AutoRunLogs", "learning"));
            string rel = _st.learningRoot.Substring(learningBase.Length).TrimStart('\\', '/');
            string dst = Path.Combine(UltraProductionWorkerBuild.BuildRoot, "AutoRunLogs", "learning", rel);
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(dst)) File.Delete(f);
            if (!Directory.Exists(_st.learningRoot)) return;
            foreach (string f in Directory.GetFiles(_st.learningRoot))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        }

        private static void Pump()
        {
            int active = 0;
            foreach (var s in _slots) if (s.process != null && !s.process.HasExited) active++;
            while (active < _maxParallel && _pending.Count > 0)
            {
                Slot s = _pending.Dequeue();
                s.process = Process.Start(new ProcessStartInfo
                {
                    FileName = UltraProductionWorkerBuild.ExecutablePath,
                    Arguments = "-batchmode -nographics -logFile " + Quote(s.logPath)
                              + " " + RankMarginWorkerBootstrap.JobArgument + " " + Quote(s.jobPath),
                    WorkingDirectory = UltraProductionWorkerBuild.BuildRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                if (s.process == null) { Debug.LogError("[学習並列] 起動失敗: " + s.jobPath); s.done = s.failed = true; continue; }
                active++;
            }
        }

        private static void Poll()
        {
            if (!_running) return;
            try
            {
                int done = 0; long completed = 0, total = 0;
                foreach (var s in _slots)
                {
                    total += s.runs;
                    if (s.done) { done++; completed += s.runs; continue; }
                    completed += ReadProgress(s.progressPath);
                    if (!File.Exists(s.responsePath))
                    {
                        // 復帰後はハンドルが無いので response の出現だけで判定する
                        if (s.process != null && s.process.HasExited)
                        { Debug.LogError("[学習並列] 応答なしで終了: " + s.logPath); s.done = s.failed = true; done++; }
                        continue;
                    }
                    // ワーカーが書いている最中だと共有違反になる (2026-11-06 に 1 回で測定ごと止まった)。 次の確認で読み直す。
                    string respText;
                    try { respText = File.ReadAllText(s.responsePath, Encoding.UTF8); }
                    catch (IOException) { continue; }
                    var resp = JsonUtility.FromJson<RankMarginWorkerResponse>(respText);
                    s.done = true; done++;
                    bool ok = _st.mode == "itt" ? (resp != null && File.Exists(GrantCsvOf(s))) : (resp != null && resp.completedNormally && File.Exists(s.learnPath));
                    if (!ok)
                    {
                        s.failed = true;
                        Debug.LogError("[学習並列] ワーカー失敗: " + (resp?.failureCode ?? "応答が壊れている") + " / " + s.logPath);
                    }
                }
                Pump();

                double elapsed = (DateTime.UtcNow - _roundStartUtc).TotalSeconds;
                if (elapsed - _lastReport >= 30)
                {
                    _lastReport = elapsed;
                    Debug.Log($"[学習並列] ラウンド {_st.round + 1}/{_st.rounds}: {completed:N0}/{total:N0} ラン"
                            + $" / {completed / Math.Max(1.0, elapsed):F1} ラン/秒");
                }
                if (done >= _slots.Count) FinishRound();
            }
            catch (Exception ex)
            {
                Debug.LogError("[学習並列] ポーリングで例外: " + ex);
                Stop("例外で停止");
            }
        }

        /// <summary>ラウンドの記録を合流させる。 <b>1 本でも欠けたら止める</b> ──
        /// 欠けたまま積むと、 落ちやすい区間 (= 長いラン) だけ抜けた偏った学習になる。</summary>
        private static void FinishRound()
        {
            foreach (var s in _slots)
                if (s.failed) { Stop($"ラウンド {_st.round + 1} でワーカーが失敗。 合流させずに停止"); return; }

            if (_st.mode == "itt") { FinishItt(); return; }

            var recs = new List<AutoRunner.RunRec>(_st.runsPerRound);
            var danger = new List<(string, double, bool)>();   // 戦闘名ごとの危険度の学習 (2026-11-05)
            foreach (var s in _slots)
            {
                var file = JsonUtility.FromJson<LearnRunFile>(File.ReadAllText(s.learnPath, Encoding.UTF8));
                if (file?.runs == null || file.runs.Length != s.runs)
                { Stop($"記録の本数が合わない: {s.learnPath} ({file?.runs?.Length ?? 0}/{s.runs})"); return; }
                foreach (var row in file.runs)
                {
                    recs.Add(row.ToRunRec());
                    if (row.fights != null)
                        foreach (var f in row.fights)
                            if (f.end && !string.IsNullOrEmpty(f.key) && f.maxHp > 0)
                                danger.Add((f.key, (double)(f.hpBefore - f.hpAfter) / f.maxHp, !f.won));
                }
            }

            int valid = 0, clears = 0;
            foreach (var r in recs) if (r.bandScore >= 0) { valid++; if (r.bandScore >= 11) clears++; }
            Directory.CreateDirectory(_st.learningRoot);
            ItemLearningStats.StatsFile stats = null;
            if (!_st.noMerge)      // A/B のときは学習を動かさない (学習は 1 バッチで約 8% のランを変えるため、 腕が同じ状態で走る必要がある)
            {
                stats = ItemLearningStats.IngestBatch(_st.learningRoot, recs);
                RunDataLogger.AppendBatch(_st.learningRoot, recs);
                AutoTest.EncounterDangerStats.Ingest(_st.learningRoot, danger);
                LearnedPriorityProvider.Reload(_st.learningRoot, writeMarkdown: false);
            }

            double min = (DateTime.UtcNow - _roundStartUtc).TotalMinutes;
            string line = $"round {_st.round + 1}/{_st.rounds}: 有効 {valid}/{recs.Count} / 7層クリア {clears} ({100.0 * clears / Math.Max(1, valid):F1}%)"
                        + $" / 学習 {stats?.totalBatches ?? 0} バッチ {stats?.totalRuns ?? 0} ラン / {min:F1} 分";
            File.AppendAllText(Path.Combine(_root, "rounds.txt"), line + "\n", new UTF8Encoding(false));
            Debug.Log("[学習並列] " + line + "\n  " + LearnedPriorityProvider.LastLoadedSummary);
            // 記録 (learn_runs.json) は残す ── 死因・戦闘ごとの診断に読む。 生成物は prune_outputs.py が刈る。

            _st.round++;
            SaveState();
            if (_st.round >= _st.rounds) { Stop("完了"); if (_st.noMerge) LaunchNextQueued(); return; }
            StartRound();
        }

        private static string GrantCsvOf(Slot s)
            => Path.Combine(Path.GetDirectoryName(s.jobPath) ?? "", "AutoRunLogs", "grant_trial", "allkinds_grant_runs.csv");

        /// <summary>ITT: 全チャンクの割り当てを 1 本にまとめ (旧いものは退避)、 親で再計算して序列を学習フォルダへ書く。</summary>
        private static void FinishItt()
        {
            var rows = new List<string>();
            foreach (var s in _slots)
                foreach (var gl in File.ReadAllLines(GrantCsvOf(s), Encoding.UTF8)) if (gl.Length > 0) rows.Add(gl);
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AutoRunLogs", "grant_trial"));
            Directory.CreateDirectory(outDir);
            string csv = Path.Combine(outDir, "allkinds_grant_runs.csv");
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            if (File.Exists(csv)) File.Move(csv, csv + "." + stamp + ".bak");
            File.WriteAllLines(csv, rows, new UTF8Encoding(false));

            // 旧い序列は上書きせず退避する (新しい fit が悪かったときに戻せるように)
            Directory.CreateDirectory(_st.learningRoot);
            foreach (string name in new[] { "itt_fit.txt", "itt_clear.txt", "itt_clear.json", "itt_effects.json" })
            {
                string p = Path.Combine(_st.learningRoot, name);
                if (File.Exists(p)) File.Copy(p, p + "." + stamp + ".preITT", true);
            }
            bool ok1 = GrantItt.Recompute(csv, _st.learningRoot);
            bool ok2 = ok1 && GrantItt.RecomputeSurvival(csv, _st.learningRoot);
            GrantItt.Variant = "";
            double min = (DateTime.UtcNow - _roundStartUtc).TotalMinutes;
            string line = $"ITT: 割り当て {rows.Count:N0} 行 / 回帰 {(ok1 ? "OK" : "失敗")} / 生存 {(ok2 ? "OK" : "失敗")} / {min:F1} 分 / {csv}";
            File.AppendAllText(Path.Combine(_root, "rounds.txt"), line + "\n", new UTF8Encoding(false));
            Debug.Log("[学習並列] " + line);
            _st.round++;
            SaveState();
            Stop(ok2 ? "完了" : "ITT の再計算に失敗");
        }

        private static void Stop(string note)
        {
            EditorApplication.update -= Poll;
            _running = false;
            SessionState.EraseString(ActiveKey);
            string msg = $"[学習並列] {note} — {_st?.round ?? 0}/{_st?.rounds ?? 0} ラウンド" + (_st != null && _st.noMerge ? "完了 (学習には合流していない)" : "合流済み") + $"\n  {_root}";
            try { File.AppendAllText(Path.Combine(_root, "rounds.txt"), "# " + note + "\n", new UTF8Encoding(false)); } catch { }
            if (note == "完了") Debug.Log(msg); else Debug.LogError(msg);
        }

        private static void SaveState()
            => File.WriteAllText(Path.Combine(_root, "state.json"), JsonUtility.ToJson(_st, true), new UTF8Encoding(false));

        private static long ReadProgress(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                foreach (string line in File.ReadAllLines(path))
                    if (line.StartsWith("completed=", StringComparison.Ordinal)
                        && long.TryParse(line.Substring(10), out long v)) return v;
            }
            catch { }
            return 0;
        }

        private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
    }
}

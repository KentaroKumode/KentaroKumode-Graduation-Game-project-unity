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
    /// <summary>r9/r10 比較を<b>プロセス並列</b>で回す。
    ///
    /// <para><b>なぜ要るか。</b> 9 アーム × 10,000 ラン は逐次で約 119 分。 Unity の
    /// シングルトン群のせいでスレッド並列は不可能なので、 <c>-batchmode -nographics</c> の
    /// Standalone Player を複数立てるしかない。</para>
    ///
    /// <para><b>正しさの根拠。</b> ラン i の結果は <c>runIdx</c> だけの関数なので
    /// (<c>Tools/digest_cmp.py</c> で確認済)、 シードを区間へ切ってプロセスへ配っても
    /// 結果は変わらない。 各ワーカーは digest を返すので、 <b>逐次版と突き合わせて
    /// 一致を確認してから数字を使うこと。</b></para>
    ///
    /// <para><b>ビルド鮮度を必ず見る。</b> Player のアセンブリが古いと、 今日の仕様ではなく
    /// ビルドした日の仕様を測ることになる ── 並列だと速い分、 気付かないまま
    /// 大量の無効データが出る。 起動前に descriptor の <c>builtUtc</c> と
    /// <c>Assets/**/*.cs</c> の最終更新を比べ、 古ければ<b>止める</b>。</para></summary>
    [InitializeOnLoad]
    public static class RankMarginParallelRunner
    {
        /// <summary>実行中セッションの出力先。 <b>SessionState はドメインリロードを跨ぐ。</b></summary>
        private const string ActiveSessionKey = "AutoRun.Parallel.ActiveSession";

        /// <summary><b>ドメインリロードから復帰する。</b>
        ///
        /// <para>スイープ中に <c>.cs</c> を 1 行でも触ると Unity は再コンパイル＋ドメインリロードを行い、
        /// <c>EditorApplication.update</c> の購読も静的フィールドも消える。 すると
        /// <b>起動済みのワーカーは走り切るのに、 残りが二度と起動されない</b> ──
        /// 実測で 68 プロセス中 22 で沈黙し、 進捗表示だけが 0.0 ラン/秒 のまま残った。
        /// プロセスは OS 側なのでリロードでは死なず、 親だけが記憶を失う形になる。</para>
        ///
        /// <para>状態はディスクに全部ある (job.json / progress.txt / response.json) ので、
        /// セッション出力先さえ覚えておけば組み直せる。</para></summary>
        static RankMarginParallelRunner()
        {
            string root = SessionState.GetString(ActiveSessionKey, "");
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            EditorApplication.delayCall += () => TryResume(root);
        }

        private static void TryResume(string root)
        {
            if (_running) return;
            try
            {
                _sessionRoot = root;
                _slots.Clear(); _pending.Clear();
                foreach (string dir in Directory.GetDirectories(root))
                {
                    string jobPath = Path.Combine(dir, "job.json");
                    if (!File.Exists(jobPath)) continue;
                    var job = JsonUtility.FromJson<RankMarginWorkerJob>(
                        File.ReadAllText(jobPath, Encoding.UTF8));
                    if (job == null) continue;
                    var slot = new Slot
                    {
                        armLabel = job.label,
                        seedStart = job.seedStart,
                        runs = job.runs,
                        jobPath = jobPath,
                        responsePath = Path.Combine(dir, "response.json"),
                        progressPath = Path.Combine(dir, "progress.txt"),
                        logPath = Path.Combine(dir, "worker.log"),
                    };
                    _slots.Add(slot);
                    // progress.txt が無い ＝ 一度も起動していない。 それだけを積み直す。
                    //   起動済みで未完了のものはプロセスが生きているので触らない
                    //   (ハンドルは失っているが、 response.json で完了を拾える)。
                    if (!File.Exists(slot.responsePath) && !File.Exists(slot.progressPath))
                        _pending.Enqueue(slot);
                }
                if (_slots.Count == 0) { SessionState.EraseString(ActiveSessionKey); return; }

                _buildFingerprint = "";
                _maxParallel = Mathf.Max(1, Mathf.Min(_slots.Count, SystemInfo.processorCount - 2));
                _startedUtc = DateTime.UtcNow;
                _running = true;
                Debug.LogWarning($"[並列] ドメインリロードから復帰: {_slots.Count} プロセス中"
                    + $" 未起動 {_pending.Count} 件を再開する\n  {root}"
                    + "\n  **スイープ中にコードを編集しないこと** ── 起動済みワーカーは"
                    + "編集前のビルドで走っており、 新旧が混ざる。");
                EditorApplication.update += Poll;
                PumpQueue();
            }
            catch (Exception ex)
            {
                Debug.LogError("[並列] 復帰に失敗: " + ex.Message);
                SessionState.EraseString(ActiveSessionKey);
            }
        }

        // AutoRunMenu と同じ EditorPrefs キー。 逐次版と同じ条件で走らせるため。
        private const string MetaModeKey   = "AutoRun.MetaBuffMode";
        private const string MetaAxisKey   = "AutoRun.MetaBuildAxis";
        private const string SweepAxisKey  = "AutoRun.SweepAllMetaAxes";
        private const string DebuffKey     = "AutoRun.EnableAllDebuffs";
        private const string ItemModeKey   = "AutoRun.ItemPickMode";
        private const string SkillKey      = "AutoRun.WiringSkill";
        private const string ShieldChipKey = "AutoRun.ShieldAbsorbsUnmitigable";
        private const string NoRelicKey    = "AutoRun.ForceNoRelic";
        private const string TheoRelicKey  = "AutoRun.ForceTheoreticalRelic";
        private const string LockFamKey    = "AutoRun.LockWeaponFamily";
        private const string IttKey        = "AutoRun.UseIttBeta";
        private const string ChalTargetKey = "AutoRun.ChallengeTarget";
        private const string RankSpecKey   = "AutoRun.MetaRankSpec";
        private const string RobBlockKey   = "AutoRun.RobberyBlocksShops";
        private const string MutualKey     = "AutoRun.UseMutualAttack";
        private const string RatioKey      = "AutoRun.PersonaRawTierRatio";
        private const string NoPartsKey    = "AutoRun.SuppressFaceParts";

        private const string RunsKey    = "AutoRun.Parallel.Runs";
        private const string ChunksKey  = "AutoRun.Parallel.ChunksPerArm";
        private const string TracksKey  = "AutoRun.Parallel.Tracks";
        private const string RanksKey   = "AutoRun.Parallel.Ranks";

        private sealed class Slot
        {
            public string armLabel;
            public int chunkIndex;
            public int seedStart;
            public int runs;
            public string jobPath, responsePath, progressPath, logPath;
            public Process process;
            public RankMarginWorkerResponse response;
        }

        private static readonly List<Slot> _slots = new List<Slot>();
        private static readonly Queue<Slot> _pending = new Queue<Slot>();
        private static string _sessionRoot = "";
        private static int _maxParallel;
        private static DateTime _startedUtc;
        private static bool _running;
        private static string _buildFingerprint = "";

        [MenuItem("Tools/AutoRun/■ 並列: 全軸 r9/r10 (プロセス並列)", priority = 9)]
        private static void Launch()
        {
            if (_running)
            { Debug.LogWarning("[並列] すでに走っている。 先に中止すること"); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogError("[並列] Play Mode 中は起動しない。 停止してから"); return; }

            int runs = EditorPrefs.GetInt(RunsKey, 10000);
            int chunks = Mathf.Clamp(EditorPrefs.GetInt(ChunksKey, 2), 1, 16);
            string tracks = EditorPrefs.GetString(TracksKey, "*");
            string ranks = EditorPrefs.GetString(RanksKey, "10");

            // Ultra は Begin() から専用の経路 (PrepareUltraAndRun) へ抜けるので、
            //   RunBatch 後段の分岐に置いたワーカーモードに届かない。
            if (WiringSkillPref() == (int)AutoRunner.WiringSkill.Ultra)
            {
                Debug.LogError("[並列] 配線技量が Ultra。 Ultra は自前の production worker を"
                    + "持っており、 この経路では走らない。 ④ で Optimal などへ戻すこと");
                return;
            }

            if (!EnsureFreshBuild(out string buildReason))
            { Debug.LogError("[並列] 中止: " + buildReason); return; }

            var arms = BuildArms(tracks, ranks);
            if (arms.Count == 0) { Debug.LogError("[並列] 対象アームが空"); return; }

            _sessionRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "AutoRunLogs", "rank_margin_parallel",
                DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(_sessionRoot);

            string[] configKeys, configValues;
            SnapshotConfig(out configKeys, out configValues);
            File.WriteAllText(Path.Combine(_sessionRoot, "config.txt"),
                DescribeConfig(configKeys, configValues), new UTF8Encoding(false));

            _slots.Clear(); _pending.Clear();
            foreach (var (label, track, rank) in arms)
            {
                int baseRuns = runs / chunks, extra = runs % chunks, offset = 0;
                for (int c = 0; c < chunks; c++)
                {
                    int size = baseRuns + (c < extra ? 1 : 0);
                    if (size <= 0) continue;
                    string dir = Path.Combine(_sessionRoot,
                        Sanitize(label) + "_c" + c.ToString("D2"));
                    Directory.CreateDirectory(dir);
                    var slot = new Slot
                    {
                        armLabel = label,
                        chunkIndex = c,
                        seedStart = AutoRunner.RankMarginSeedBase + offset,
                        runs = size,
                        jobPath = Path.Combine(dir, "job.json"),
                        responsePath = Path.Combine(dir, "response.json"),
                        progressPath = Path.Combine(dir, "progress.txt"),
                        logPath = Path.Combine(dir, "worker.log"),
                    };
                    var job = new RankMarginWorkerJob
                    {
                        label = label,
                        focusTrack = track,
                        focusRank = rank,
                        runs = size,
                        seedStart = slot.seedStart,
                        configKeys = configKeys,
                        configValues = configValues,
                        responsePath = slot.responsePath,
                        progressPath = slot.progressPath,
                        outputRoot = Path.Combine(dir, "AutoRunLogs"),
                        buildFingerprint = _buildFingerprint,
                    };
                    File.WriteAllText(slot.jobPath, JsonUtility.ToJson(job, true),
                        new UTF8Encoding(false));
                    _slots.Add(slot);
                    _pending.Enqueue(slot);
                    offset += size;
                }
            }

            // **1 プロセス 1 コアを素直に割り当てる。** Player は同期ループなので
            //   1 論理コアを埋め切る。 論理コア数より多く立てると取り合いになるだけ。
            //   OS とエディタのために 2 コア残す。
            _maxParallel = Mathf.Max(1, Mathf.Min(_slots.Count,
                SystemInfo.processorCount - 2));
            _startedUtc = DateTime.UtcNow;
            _running = true;
            // ドメインリロードを跨いで復帰できるよう、 セッション出力先だけ覚えておく。
            SessionState.SetString(ActiveSessionKey, _sessionRoot);

            Debug.Log($"[並列] 開始: {arms.Count} アーム × {runs:N0} ラン"
                    + $" = {(long)arms.Count * runs:N0} ラン"
                    + $" / {_slots.Count} プロセス (同時 {_maxParallel})"
                    + $"\n  出力: {_sessionRoot}"
                    + $"\n  build={_buildFingerprint.Substring(0, 12)}");

            EditorApplication.update += Poll;
            PumpQueue();
        }

        [MenuItem("Tools/AutoRun/■ 並列: 中止", priority = 10)]
        private static void Abort()
        {
            if (!_running) { Debug.Log("[並列] 走っていない"); return; }
            foreach (var s in _slots)
            {
                try { if (s.process != null && !s.process.HasExited) s.process.Kill(); }
                catch { }
            }
            Finish("中止した");
        }

        /// <summary><b>毎回ビルドしてから走る。</b> 鮮度を推測しない。
        ///
        /// <para><b>なぜ「推測しない」か。</b> ワーカーはゲームロジックの再実装ではなく、
        /// 同じソースから作った Player なので、 危険は「挙動が違う」ではなく
        /// <b>「古い」</b>だけ。 ところが何が焼き込まれるかは <c>.cs</c> に限らない ──
        /// 敵ステータス (<c>Assets/Resources/enemies.json</c>) もアイテム
        /// (<c>Assets/Data/InventorySystem/items.json</c>) も <c>Resources.Load</c> 経由で
        /// <b>ビルド時にプレイヤーへ焼き込まれる</b>。 <c>.cs</c> だけ見る鮮度判定は
        /// これらを素通しし、 <b>古いステータスで大量に測って気付かない</b>。</para>
        ///
        /// <para>増分ビルドは実測 20 秒。 13 分のスイープに対して 2.5% なので、
        /// 判定を賢くするより毎回作り直す方が安い。 <see cref="UltraProductionWorkerBuild.EnsureBuilt"/>
        /// は学習の凍結スナップショットも張り替えるので、 そちらの取り違えも同時に消える。</para>
        ///
        /// <para>ビルド後の照合は<b>表明</b>として残す ── 作り直したのに descriptor が
        /// ソースより古いなら、 ビルドが何かを拾い損ねている。</para></summary>
        private static bool EnsureFreshBuild(out string reason)
        {
            if (!UltraProductionWorkerBuild.EnsureBuilt(out reason))
            {
                reason = "Player のビルドに失敗: " + reason;
                return false;
            }
            if (!UltraProductionWorkerBuild.TryLoadDescriptor(
                out UltraProductionWorkerDescriptor descriptor, out reason))
            {
                reason = "ビルド直後なのに descriptor を読めない (" + reason + ")";
                return false;
            }
            if (!DateTime.TryParse(descriptor.builtUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTime built))
            { reason = "descriptor の builtUtc が読めない: " + descriptor.builtUtc; return false; }

            // **`.cs` に限らず Assets 配下の全てを見る。** ステータス値は json / asset に居る。
            //   偽陽性 (無関係な素材を触っただけで作り直す) の代償は 20 秒、
            //   偽陰性 (古い値で測る) の代償はスイープ 1 本まるごとなので、 広く取る。
            DateTime newest = DateTime.MinValue;
            string newestPath = "";
            foreach (string path in Directory.EnumerateFiles(
                Application.dataPath, "*", SearchOption.AllDirectories))
            {
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                DateTime t = File.GetLastWriteTimeUtc(path);
                if (t > newest) { newest = t; newestPath = path; }
            }
            if (newest > built.ToUniversalTime())
            {
                reason = "作り直したのに Player がソースより古い。 **ビルドが拾い損ねている**\n"
                       + "    ビルド: " + built.ToUniversalTime().ToString("O") + "\n"
                       + "    最新ソース: " + newest.ToString("O") + "  "
                       + newestPath.Replace(Application.dataPath, "Assets") + "\n"
                       + "    → そのファイルが Player に含まれる経路を確認すること";
                return false;
            }
            _buildFingerprint = descriptor.fingerprints.buildFingerprint ?? "";
            reason = "";
            return true;
        }

        private static List<(string label, string track, int rank)> BuildArms(
            string trackSpec, string rankSpec)
        {
            var arms = new List<(string, string, int)>();
            // Balanced は必ず同じバッチに入れる。 判定基準が Balanced 比で書かれているので、
            //   別バッチの古い値と突き合わせると「基準が陳腐化していた」事故になる。
            arms.Add(("Balanced", "", 0));

            var tracks = new List<MetaProgression.MetaPanelKind>();
            string spec = (trackSpec ?? "").Trim();
            if (spec.Length == 0 || spec == "*")
            {
                foreach (MetaProgression.MetaPanelKind k in
                    Enum.GetValues(typeof(MetaProgression.MetaPanelKind)))
                    if (MetaProgression.MetaPanelKindExt.MaxRank(k) == 10) tracks.Add(k);
            }
            else foreach (string tok in spec.Split(','))
            {
                if (!Enum.TryParse(tok.Trim(), true, out MetaProgression.MetaPanelKind k)) continue;
                if (MetaProgression.MetaPanelKindExt.MaxRank(k) != 10)
                { Debug.LogWarning("[並列] " + k + " は 10 段トラックではない。 飛ばす"); continue; }
                tracks.Add(k);
            }

            var ranks = new List<int>();
            foreach (string tok in (rankSpec ?? "10").Split(','))
                if (int.TryParse(tok.Trim(), out int r) && r >= 1 && r <= 10) ranks.Add(r);
            if (ranks.Count == 0) ranks.Add(10);

            foreach (var t in tracks)
                foreach (int r in ranks) arms.Add(($"{t} r{r}", t.ToString(), r));
            return arms;
        }

        private static void SnapshotConfig(out string[] keys, out string[] values)
        {
            var k = new List<string>();
            var v = new List<string>();
            void Add(string field, string value) { k.Add(field); v.Add(value); }

            Add("masterSeed", EditorPrefs.GetString("AutoRun.MasterSeed", ""));
            Add("challengeSpec", EditorPrefs.GetString("AutoRun.ChallengeSpec", ""));
            // **既定値は AutoRunMenu と一字一句そろえる。** 0 で代用すると、
            //   設定を一度も触っていない環境で逐次版と違う条件を測ることになる。
            Add("metaBuffMode", MetaModePref().ToString(CultureInfo.InvariantCulture));
            Add("metaBuildAxis", EditorPrefs.GetInt(MetaAxisKey,
                (int)MetaAllocationPresets.Preset.Offense).ToString(CultureInfo.InvariantCulture));
            Add("sweepAllMetaAxes", EditorPrefs.GetBool(SweepAxisKey, false).ToString());
            Add("enableAllDebuffs", EditorPrefs.GetBool(DebuffKey, false).ToString());
            Add("itemPickMode", EditorPrefs.GetInt(ItemModeKey,
                (int)AutoRunner.ItemPickMode.TierBased).ToString(CultureInfo.InvariantCulture));
            Add("wiringSkill", WiringSkillPref().ToString(CultureInfo.InvariantCulture));
            Add("shieldAbsorbsUnmitigable",
                (!EditorPrefs.GetBool(ShieldChipKey, false)).ToString());
            Add("forceNoRelic", EditorPrefs.GetBool(NoRelicKey, false).ToString());
            Add("forceTheoreticalRelic", EditorPrefs.GetBool(TheoRelicKey, false).ToString());
            Add("lockWeaponFamily", EditorPrefs.GetBool(LockFamKey, false).ToString());
            Add("useIttBeta", EditorPrefs.GetBool(IttKey, false).ToString());
            Add("challengeScoreTarget",
                EditorPrefs.GetInt(ChalTargetKey, 0).ToString(CultureInfo.InvariantCulture));
            Add("metaRankSpec", EditorPrefs.GetString(RankSpecKey, ""));
            Add("robberyBlocksShops", EditorPrefs.GetBool(RobBlockKey, false).ToString());
            Add("robberySurcharge", EditorPrefs.GetFloat("AutoRun.RobberySurcharge", 0f)
                .ToString(CultureInfo.InvariantCulture));
            Add("robberyFinalShopOnly",
                EditorPrefs.GetBool("AutoRun.RobberyFinalShopOnly", false).ToString());
            Add("robberyHopeCost", EditorPrefs.GetInt("AutoRun.RobberyHopeCost", -1)
                .ToString(CultureInfo.InvariantCulture));
            Add("bossTraceFloor", EditorPrefs.GetInt("AutoRun.BossTraceFloor", 0)
                .ToString(CultureInfo.InvariantCulture));
            // 〈門〉の 3 工程を BOT が払うか (2026-09-14)。 既定はすべて true = 欠陥を避ける。
            //   **アームごとに切り替えて測るための穴。** 旧儀式では「払う方が −4.33pt」
            //   という結果が出ており、 門でも払うのが最適とは限らない。
            Add("gateBotPaysBlood",
                EditorPrefs.GetBool("AutoRun.GateBotPaysBlood", true).ToString());
            Add("gateBotPaysRelics",
                EditorPrefs.GetBool("AutoRun.GateBotPaysRelics", true).ToString());
            Add("gateBotPaysTransfer",
                EditorPrefs.GetBool("AutoRun.GateBotPaysTransfer", true).ToString());

            // **2026-09-13: 運び漏れで 170,000 ラン を捨てた 3 件。**
            //   useMutualAttackPipeline は AutoRunner のフィールド既定が false で、
            //   Begin() が CombatManager の static (既定 true) を**それで上書きする**。
            //   運ばないとワーカーだけ旧戦闘経路で走り、 エディタと別のゲームを測る。
            //   **ここに足し忘れたキーは黙って既定値になる** ── だから下の
            //   DescribeEffectiveState で毎回サマリーへ印字する。
            Add("useMutualAttackPipeline", EditorPrefs.GetBool(MutualKey, false).ToString());
            Add("rawTierRatio", (EditorPrefs.GetInt(RatioKey, 50) / 100f)
                .ToString(CultureInfo.InvariantCulture));
            Add("suppressFacePartOffers", EditorPrefs.GetBool(NoPartsKey, false).ToString());

            keys = k.ToArray(); values = v.ToArray();
        }

        private static int MetaModePref() =>
            EditorPrefs.GetInt(MetaModeKey, (int)AutoRunner.MetaBuffMode.Standard);

        private static int WiringSkillPref() =>
            EditorPrefs.GetInt(SkillKey, (int)AutoRunner.WiringSkill.Optimal);

        private static string DescribeConfig(string[] keys, string[] values)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 並列 r9/r10: ワーカーへ渡した実効設定 ===");
            sb.AppendLine("これが逐次版と食い違っていたら、 数字は比較できない。");
            sb.AppendLine();
            for (int i = 0; i < keys.Length; i++)
                sb.AppendLine($"  {keys[i],-26} = {values[i]}");
            return sb.ToString();
        }

        private static void PumpQueue()
        {
            int active = 0;
            foreach (var s in _slots)
                if (s.process != null && !s.process.HasExited) active++;
            while (active < _maxParallel && _pending.Count > 0)
            {
                Slot slot = _pending.Dequeue();
                var start = new ProcessStartInfo
                {
                    FileName = UltraProductionWorkerBuild.ExecutablePath,
                    Arguments = "-batchmode -nographics -logFile " + Quote(slot.logPath)
                              + " " + RankMarginWorkerBootstrap.JobArgument + " "
                              + Quote(slot.jobPath),
                    WorkingDirectory = UltraProductionWorkerBuild.BuildRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                slot.process = Process.Start(start);
                if (slot.process == null)
                { Debug.LogError("[並列] プロセス起動に失敗: " + slot.armLabel); continue; }
                active++;
            }
        }

        private static double _lastReport;

        private static void Poll()
        {
            if (!_running) return;
            try
            {
                int done = 0, failed = 0;
                long completedRuns = 0;
                foreach (var s in _slots)
                {
                    if (s.response != null) { done++; completedRuns += s.runs; continue; }
                    completedRuns += ReadProgress(s.progressPath);
                    if (s.process == null || !s.process.HasExited) continue;
                    if (!File.Exists(s.responsePath))
                    {
                        Debug.LogError($"[並列] {s.armLabel} c{s.chunkIndex}: 応答なしで終了"
                            + $" (exit {s.process.ExitCode}) / log: {s.logPath}");
                        s.response = new RankMarginWorkerResponse
                        { label = s.armLabel, completedNormally = false,
                          failureCode = "NO_RESPONSE" };
                        failed++; done++; continue;
                    }
                    s.response = JsonUtility.FromJson<RankMarginWorkerResponse>(
                        File.ReadAllText(s.responsePath, Encoding.UTF8));
                    if (s.response == null || !s.response.completedNormally)
                    {
                        Debug.LogError($"[並列] {s.armLabel} c{s.chunkIndex}: 失敗 "
                            + (s.response != null ? s.response.failureCode : "応答が壊れている"));
                        failed++;
                    }
                    done++;
                }

                PumpQueue();

                double elapsed = (DateTime.UtcNow - _startedUtc).TotalSeconds;
                if (elapsed - _lastReport >= 30)
                {
                    _lastReport = elapsed;
                    long total = 0;
                    foreach (var s in _slots) total += s.runs;
                    double rate = completedRuns / Math.Max(1.0, elapsed);
                    double eta = rate > 0 ? (total - completedRuns) / rate : 0;
                    Debug.Log($"[並列] {completedRuns:N0}/{total:N0} ラン"
                        + $" / 完了 {done}/{_slots.Count} プロセス"
                        + (failed > 0 ? $" / **失敗 {failed}**" : "")
                        + $" / {rate:F1} ラン/秒 / 残り {eta / 60:F1} 分");
                }

                if (done >= _slots.Count) Finish(failed > 0 ? $"失敗 {failed} 件あり" : "完了");
            }
            catch (Exception ex)
            {
                Debug.LogError("[並列] ポーリングで例外: " + ex);
                Finish("例外で停止");
            }
        }

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

        private static void Finish(string note)
        {
            EditorApplication.update -= Poll;
            _running = false;
            SessionState.EraseString(ActiveSessionKey);
            double minutes = (DateTime.UtcNow - _startedUtc).TotalMinutes;
            string report = Merge(note, minutes);
            string path = Path.Combine(_sessionRoot, "summary.md");
            try { File.WriteAllText(path, report, new UTF8Encoding(false)); } catch { }
            Debug.Log("[並列] " + note + $" ({minutes:F1} 分)\n" + report + "\n→ " + path);
        }

        /// <summary>チャンクをアームへ畳んで、 逐次版と同じ形の表を出す。</summary>
        private static string Merge(string note, double minutes)
        {
            var order = new List<string>();
            var valid = new Dictionary<string, int>();
            var clears = new Dictionary<string, int>();
            var stage = new Dictionary<string, double>();
            var bands = new Dictionary<string, List<(int seed, int band, string digest)>>();
            var revivals = new Dictionary<string, (long torch, long lastStand, long fleuret, long hp)>();
            var hopePay = new Dictionary<string, (long count, long spent)>();
            var vault = new Dictionary<string, (long doublings, long gained, long capHits)>();
            long plunder = 0, robAttempts = 0, robWins = 0, robLosses = 0, robLoot = 0, robApplied = 0;

            foreach (var s in _slots)
            {
                if (s.response == null || !s.response.completedNormally) continue;
                string label = s.armLabel;
                if (!valid.ContainsKey(label))
                {
                    order.Add(label);
                    valid[label] = 0; clears[label] = 0; stage[label] = 0;
                    bands[label] = new List<(int, int, string)>();
                    revivals[label] = (0, 0, 0, 0);
                    hopePay[label] = (0, 0);
                    vault[label] = (0, 0, 0);
                }
                var hp = hopePay[label];
                hopePay[label] = (hp.count + s.response.hopePayments,
                    hp.spent + s.response.hopeSpent);
                var vb = vault[label];
                vault[label] = (vb.doublings + s.response.vaultDoublings,
                    vb.gained + s.response.vaultGoldGained,
                    vb.capHits + s.response.vaultCapHits);
                var rv = revivals[label];
                revivals[label] = (rv.torch + s.response.torchRevivals,
                    rv.lastStand + s.response.lastStandRevivals,
                    rv.fleuret + s.response.fleuretRevivals,
                    rv.hp + s.response.revivalHpRestored);
                valid[label] += s.response.valid;
                clears[label] += s.response.clears;
                stage[label] += s.response.stageSum;
                for (int i = 0; i < s.response.bandScores.Length; i++)
                    bands[label].Add((s.response.seedStart + i, s.response.bandScores[i],
                        i < s.response.digests.Length ? s.response.digests[i] : ""));
                plunder += s.response.plunderDrops;
                robAttempts += s.response.robberyAttempts;
                robWins += s.response.robberyWins;
                robLosses += s.response.robberyLosses;
                robLoot += s.response.robberyLootTotal;
                robApplied += s.response.robberyLootApplied;
            }

            // ラン毎の行を残す。 逐次版との digest 突き合わせと、 後からのペア比較に要る。
            try
            {
                using (var w = new StreamWriter(Path.Combine(_sessionRoot, "runs.tsv"),
                    false, new UTF8Encoding(false)))
                {
                    w.WriteLine("arm\trunIdx\tbandScore\tdigest");
                    foreach (string label in order)
                    {
                        var rows = bands[label];
                        rows.Sort((a, b) => a.seed.CompareTo(b.seed));
                        foreach (var r in rows)
                            w.WriteLine($"{label}\t{r.seed}\t{r.band}\t{r.digest}");
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[並列] runs.tsv を書けなかった: " + ex.Message); }

            var sb = new StringBuilder();
            sb.AppendLine("=== 全軸 r9 / r10 (プロセス並列・Balanced 同梱) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss} / {note} / 実時間 {minutes:F1} 分");
            sb.AppendLine($"build   : {_buildFingerprint}");
            sb.AppendLine($"予算 {MetaProgression.MetaPanel.MaxPoints}pt / "
                        + "各アーム = [焦点トラック r9 or r10] + [他の数値系 7 本を 2pt ずつ]");
            sb.AppendLine("判定基準: r10 は Balanced ±3pt");

            // **[実効状態] ワーカーが実際に走った設定。** 親が「送ったつもり」の値ではない。
            //   2026-09-13 に useMutualAttackPipeline の運び漏れで 170,000 ラン を
            //   旧戦闘経路で測って捨てた。 毎回印字して、 黙って既定値で走るのを防ぐ。
            foreach (var s in _slots)
            {
                if (s.response == null || string.IsNullOrEmpty(s.response.effectiveState)) continue;
                sb.AppendLine();
                sb.AppendLine("[実効状態] ワーカーが実際に走った設定 (送った値ではなく入っていた値)");
                foreach (string line in s.response.effectiveState.Split('\n'))
                    if (line.Length > 0) sb.AppendLine("  " + line);
                break;
            }
            sb.AppendLine();
            sb.AppendLine("アーム                  |  クリア率 |  Bal比 |   r10−r9 | 判定 | 平均到達段 |     n");

            var clearOf = new Dictionary<string, double>();
            double baseClear = 0;
            for (int i = 0; i < order.Count; i++)
            {
                string label = order[i];
                int n = valid[label];
                double clear = n > 0 ? clears[label] / (double)n * 100.0 : 0;
                double mean = n > 0 ? stage[label] / n : 0;
                clearOf[label] = clear;
                if (i == 0) baseClear = clear;
                double bal = clear - baseClear;

                string delta = "", verdict = "";
                if (i == 0) verdict = "基準";
                else if (label.EndsWith(" r9", StringComparison.Ordinal))
                    verdict = Math.Abs(bal) <= 2.0 ? "○" : (bal > 0 ? "強" : "弱");
                else if (label.EndsWith(" r10", StringComparison.Ordinal)
                         && clearOf.TryGetValue(label.Replace(" r10", " r9"), out double r9c))
                {
                    double d = clear - r9c;
                    delta = $"{d,+9:F2}pt";
                    verdict = (d > 0 && Math.Abs(bal) <= 3.0) ? "○"
                            : (d <= 0 ? "極点×" : (bal > 0 ? "強" : "弱"));
                }
                else verdict = Math.Abs(bal) <= 3.0 ? "○" : (bal > 0 ? "強" : "弱");

                sb.AppendLine($"{label,-22} | {clear,8:F2}% | {(i == 0 ? 0 : bal),+6:F2} | {delta,9}"
                            + $" | {verdict,-4} | {mean,9:F2} | {n,6:N0}");
            }

            // **救済の発動数はログでは数えられない。** バッチ中は logEnabled=false なので
            //   「ログに出ていない＝発動していない」は成り立たない。 計数で出す。
            sb.AppendLine();
            sb.AppendLine("救済の発動 (アームごと・n に対する率)");
            sb.AppendLine("アーム                  |      灯火 | ラストスタンド |   フルーレ | 1回あたり戻したHP");
            foreach (string label in order)
            {
                var rv = revivals[label];
                int n = Mathf.Max(1, valid[label]);
                long hpEvents = rv.torch + rv.lastStand;   // フルーレは HP を戻さない
                sb.AppendLine($"{label,-22} | {rv.torch,5:N0} {rv.torch * 100.0 / n,5:F1}%"
                            + $" | {rv.lastStand,7:N0} {rv.lastStand * 100.0 / n,5:F1}%"
                            + $" | {rv.fleuret,5:N0} {rv.fleuret * 100.0 / n,5:F1}%"
                            + $" | {(hpEvents > 0 ? rv.hp / (double)hpEvents : 0),8:F1}");
            }

            // 燈火 r10。 **0 件なら機構が使われていない** ── 効果の有無を論じる前にここを見る。
            long anyHopePay = 0;
            foreach (var kv in hopePay) anyHopePay += kv.Value.count;
            if (anyHopePay > 0)
            {
                sb.AppendLine();
                sb.AppendLine("希望での支払い (燈火 r10)");
                sb.AppendLine("アーム                  |     回数 | ラン当たり | 1回あたり希望");
                foreach (string label in order)
                {
                    var hp = hopePay[label];
                    if (hp.count == 0) continue;
                    int n = Mathf.Max(1, valid[label]);
                    sb.AppendLine($"{label,-22} | {hp.count,8:N0} | {hp.count / (double)n,9:F2}"
                                + $" | {hp.spent / (double)hp.count,12:F1}");
                }
            }

            long anyVault = 0;
            foreach (var kv in vault) anyVault += kv.Value.doublings;
            if (anyVault > 0)
            {
                sb.AppendLine();
                sb.AppendLine("金庫 r10: 層跨ぎの所持金倍化");
                sb.AppendLine("アーム                  |   倍化回数 | ラン当たり | 増えたG/ラン | 上限到達");
                foreach (string label in order)
                {
                    var vb = vault[label];
                    if (vb.doublings == 0) continue;
                    int n = Mathf.Max(1, valid[label]);
                    sb.AppendLine($"{label,-22} | {vb.doublings,10:N0} | {vb.doublings / (double)n,9:F2}"
                                + $" | {vb.gained / (double)n,12:F1} | {vb.capHits,8:N0}");
                }
                sb.AppendLine($"  ※ 上限到達が 0 なら Cap ({MetaProgression.VaultBank.Cap}G) は効いていない。");
            }

            // 〈防御〉が触れている量。 **「効かない」と「届いていない」を分ける。**
            {
                var g = new Dictionary<string, long[]>();
                foreach (var s in _slots)
                {
                    if (s.response == null || !s.response.completedNormally) continue;
                    if (!g.TryGetValue(s.armLabel, out long[] a))
                        g[s.armLabel] = a = new long[10];
                    a[0] += s.response.guardAtkBefore;
                    a[1] += s.response.guardAtkAfter;
                    a[2] += s.response.guardBlockSum;
                    a[3] += s.response.guardLossBase;
                    a[4] += s.response.guardAttacks;
                    a[5] += s.response.guardFullyBlocked;
                    a[6] += s.response.valorEliteFights;
                    a[7] += s.response.valorEliteDrops;
                }
                bool any = false;
                foreach (var kv in g) if (kv.Value[4] > 0) { any = true; break; }
                if (any)
                {
                    sb.AppendLine();
                    sb.AppendLine("被ダメの内訳と〈剛胆〉(1 攻撃あたり / エリートはラン当たり)");
                    sb.AppendLine("アーム                  |  敵攻撃 | ブロック | 抜けた分 | 全吸収率"
                                + " | エリート戦/ラン | 精鋭ドロップ");
                    foreach (string label in order)
                    {
                        if (!g.TryGetValue(label, out long[] a) || a[4] == 0) continue;
                        double n = a[4];
                        double runs = Mathf.Max(1, valid[label]);
                        sb.AppendLine($"{label,-22} | {a[0] / n,7:F2} | {a[2] / n,8:F2}"
                                    + $" | {a[3] / n,8:F2} | {a[5] * 100.0 / n,7:F1}%"
                                    + $" | {a[6] / runs,14:F2} | {a[7] / runs,12:F2}");
                    }
                    sb.AppendLine("  ※ 「エリート戦/ラン」が〈剛胆〉の格上げの効き。 段 0 のアームが基準。");
                }
            }

            // ボス突入時の状態。 **休憩で戻る HP と、 戻らない希望・消耗品を並べる。**
            {
                var be = new Dictionary<string, long[][]>();
                foreach (var s3 in _slots)
                {
                    if (s3.response?.bossEntryCount == null) continue;
                    if (!be.TryGetValue(s3.armLabel, out long[][] a))
                        be[s3.armLabel] = a = new long[7][] {
                            new long[9], new long[9], new long[9], new long[9],
                            new long[9], new long[9], new long[9] };
                    for (int f = 0; f < 9 && f < s3.response.bossEntryCount.Length; f++)
                    {
                        a[0][f] += s3.response.bossEntryCount[f];
                        a[1][f] += s3.response.bossEntryHope[f];
                        a[2][f] += s3.response.bossEntryHopeTier[f];
                        a[3][f] += s3.response.bossEntryConsumables[f];
                        a[4][f] += s3.response.bossEntryHpPct[f];
                        a[5][f] += s3.response.bossEntryPassives[f];
                        a[6][f] += s3.response.bossEntryPower[f];
                    }
                }
                if (be.Count > 0)
                {
                    sb.AppendLine();
                    // **7 層にボスは無い (2026-09-14)。** 最終戦は 8 層 Null Point。
                    sb.AppendLine("ボス突入時の状態 (6層 / 8層 Null Point)");
                    sb.AppendLine("アーム                  | 6F到達 | 希望 | 帯 | 消耗品 |  HP%"
                                + " | パッシブ | 装備力 || 8F到達 | 希望 | 帯 | 消耗品 |  HP%"
                                + " | パッシブ | 装備力");
                    foreach (string label in order)
                    {
                        if (!be.TryGetValue(label, out long[][] a)) continue;
                        string Cell(int f)
                        {
                            double n = a[0][f] > 0 ? a[0][f] : 1;
                            return $"{a[0][f],6:N0} | {a[1][f] / n,4:F0} | {a[2][f] / n,2:F1}"
                                 + $" | {a[3][f] / n,6:F2} | {a[4][f] / n,4:F0}%"
                                 + $" | {a[5][f] / n,8:F2} | {a[6][f] / n,6:F0}";
                        }
                        sb.AppendLine($"{label,-22} | {Cell(6)} || {Cell(8)}");
                    }
                    sb.AppendLine("  ※ 帯: 0=平穏 1=焦燥 2=悲観 3=絶望。 2 以上で苦悩(会心倍率−0.3)、"
                                + "3 で迷妄(開幕パッシブ 1〜3 個無効)。");
                }
            }

            sb.AppendLine();
            long totalRuns = 0;
            foreach (var kv in valid) totalRuns += kv.Value;
            sb.AppendLine($"※ 合計 {totalRuns:N0} ラン / {_slots.Count} プロセス (同時 {_maxParallel})"
                        + $" / スループット {totalRuns / Math.Max(1.0, minutes * 60):F1} ラン/秒");
            if (baseClear > 0 && order.Count > 0 && valid[order[0]] > 0)
                sb.AppendLine($"※ 差の 95%CI は概ね ±"
                    + $"{1.96 * Math.Sqrt(2 * baseClear * (100 - baseClear) / valid[order[0]]):F2}pt。");
            sb.AppendLine($"※ 強奪の道中パッシブドロップ: {plunder:N0} 件");
            sb.AppendLine($"※ 強盗: 発動 {robAttempts:N0} 回 / 勝 {robWins:N0} 敗 {robLosses:N0}"
                        + $" (勝率 {(robAttempts > 0 ? robWins * 100.0 / robAttempts : 0):F1}%)"
                        + $" / 平均戦利品 {(robWins > 0 ? robLoot / (double)robWins : 0):F1} 件"
                        + $" (うち**実際に所持へ入った**のは {(robWins > 0 ? robApplied / (double)robWins : 0):F1} 件)");
            {
                // **何層で撃っているかを必ず出す。** 口で「6 層で撃つ」と言っても、
                //   条件に階層ゲートが無ければ最初の店で撃つ。 分布だけが答えを持つ。
                var byFloor = new long[7];
                foreach (var s2 in _slots)
                {
                    if (s2.response?.robberyByFloor == null) continue;
                    for (int i = 0; i < byFloor.Length && i < s2.response.robberyByFloor.Length; i++)
                        byFloor[i] += s2.response.robberyByFloor[i];
                }
                long tot = 0; foreach (long v in byFloor) tot += v;
                if (tot > 0)
                {
                    var line = new StringBuilder("※ 強盗を撃った層: ");
                    for (int i = 0; i < byFloor.Length; i++)
                        line.Append($"F{i + 1}={byFloor[i] * 100.0 / tot:F1}% ");
                    sb.AppendLine(line.ToString().TrimEnd());
                }
            }
            sb.AppendLine();
            sb.AppendLine("※ **検算**: runs.tsv の digest を逐次版と突き合わせること。");
            sb.AppendLine("   一致しなければラン跨ぎの状態依存が残っており、 この数字は無効。");
            return sb.ToString();
        }

        private static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        private static string Quote(string s) => "\"" + s + "\"";
    }
}

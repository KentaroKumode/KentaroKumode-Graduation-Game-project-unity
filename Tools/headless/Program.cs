// DICE BOUND の並列ワーカー (Unity を使わない .NET 版)。 IL2CPP のワーカー (UltraProductionWorker.exe) と同じ起動引数・同じ約束で動く:
//   DiceBoundHeadless.exe [-batchmode -nographics] -logFile <worker.log> --rank-margin-job <job.json>
// 入口の中身は Player と同じ: [RuntimeInitializeOnLoadMethod] (BeforeSceneLoad) → シーン (SampleScene2 = GameManager + MapManager)
// → AfterSceneLoad (RankMarginWorkerBootstrap.StartIfWorker が job を読んで AutoRunner を回す) → フレームを回す → Application.Quit。
// 1 プロセス 1 スレッド。 並列は親 (エディタのランナー) がプロセスを並べる。
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DiceBoundHeadless
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            string logFile = Arg(args, "-logFile");
            if (args.Contains("--selftest")) return SelfTest.Run();
            // ITT の合流 (エディタの ItemLearningParallelRunner.FinishItt と同じ再計算): --itt-recompute <合流した grant_runs.csv> <学習フォルダ> [版 (super など)]
            int ir = Array.IndexOf(args, "--itt-recompute");
            if (ir >= 0 && ir + 2 < args.Length)
            {
                AutoTest.GrantItt.Variant = ir + 3 < args.Length ? args[ir + 3] : "";
                bool ok = AutoTest.GrantItt.Recompute(args[ir + 1], args[ir + 2]) && AutoTest.GrantItt.RecomputeSurvival(args[ir + 1], args[ir + 2]);
                Console.WriteLine((ok ? "OK " : "NG ") + AutoTest.GrantItt.SurvivalSummary);
                return ok ? 0 : 1;
            }
            if (string.IsNullOrEmpty(Arg(args, AutoTest.ParallelSweep.RankMarginWorkerBootstrap.JobArgument)))
            {
                Console.Error.WriteLine("usage: DiceBoundHeadless -logFile <log> " + AutoTest.ParallelSweep.RankMarginWorkerBootstrap.JobArgument + " <job.json>  |  --selftest");
                return 64;
            }
            Headless.Engine.OpenLog(logFile);
            try
            {
                RegisterAssets();
                var asm = typeof(GameLoop.GameManager).Assembly;
                foreach (var stage in new[] { RuntimeInitializeLoadType.SubsystemRegistration, RuntimeInitializeLoadType.AfterAssembliesLoaded,
                                              RuntimeInitializeLoadType.BeforeSplashScreen, RuntimeInitializeLoadType.BeforeSceneLoad })
                    Headless.Engine.RunInitializers(asm, stage);
                LoadWorkerScene();
                Headless.Engine.RunInitializers(asm, RuntimeInitializeLoadType.AfterSceneLoad);
                while (!Headless.Engine.QuitRequested) Headless.Engine.Tick();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Headless.Engine.ExitCode = 2;
            }
            Headless.Engine.Shutdown();
            return Headless.Engine.ExitCode;
        }

        /// <summary>ワーカーのシーン (UltraProductionWorkerBuild.ScenePath = SampleScene2) の中身。 シーンの値はどれもコードの既定と同じ
        /// (GameManager: autoStartRun=0・logPhaseChanges=1 / MapManager の 2 欄はコードから削除済み)。</summary>
        static void LoadWorkerScene()
        {
            new GameObject("GameManager").AddComponent<GameLoop.GameManager>();
            new GameObject("MapManager").AddComponent<MapSystem.MapManager>();
        }

        /// <summary>Resources の ScriptableObject。 Player は ItemDatabase.asset (items.json を指す) を焼き込む。
        /// 実行時は items[0].completeData が null なので必ず items.json から作り直す (ItemDatabase.Initialize) ── 写しの中身は使われない。</summary>
        internal static void RegisterAssets()
        {
            Resources.RegisterFactory("ItemDatabase", () =>
            {
                var db = ScriptableObject.CreateInstance<ItemSystem.ItemDatabase>();
                db.name = "ItemDatabase";
                db.itemsJsonFile = new TextAsset(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "items.json"))) { name = "items" };
                db.items.Add(new ItemSystem.ItemDatabase.ItemEntry());   // 再構築の合図 (completeData == null)
                return db;
            });
        }

        static string Arg(string[] a, string name)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == name && i + 1 < a.Length) return a[i + 1];
                if (a[i].StartsWith(name + "=", StringComparison.Ordinal)) return a[i].Substring(name.Length + 1);
            }
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AutoTest;
using AutoTest.Ultra;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Process = System.Diagnostics.Process;

namespace AutoTest.EditorTools
{
    public static class UltraProductionWorkerBuild
    {
        public const string ScenePath = "Assets/Scenes/SampleScene2.unity";

        // Unity rejects Player builds under the project's internal Library folder.
        // Keep the disposable worker outside Assets/Library so it can be rebuilt
        // without triggering an import or violating BuildPipeline's path guard.
        public static string BuildRoot => Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "UltraProductionWorkerBuild"));
        public static string ExecutablePath => Path.Combine(BuildRoot,
            "UltraProductionWorker.exe");
        public static string DescriptorPath => Path.Combine(BuildRoot,
            "ultra-production-worker.json");

        /// <summary>descriptor の actionFingerprint に入れる固定の印 (2026-11-13〜・#135)。
        /// 旧 Ultra の方策の組の候補集合のハッシュの代わり。 照合する側は無い。</summary>
        public const string ActionFingerprintTag = "action:no-policy-portfolio-v1";

        /// <summary>コマンドライン (-batchmode -executeMethod) 用の入口。
        /// Editor を開かずに worker を作り直す ── スイープは worker でしか回らないので、
        /// Editor が落ちているとコード変更を測る手段が無くなる。 終了コード 0 = 成功。</summary>
        public static void BuildFromCommandLine()
        {
            bool ok = EnsureBuilt(out string reason);
            Debug.Log("[UltraProductionWorkerBuild] CLI build: " + (ok ? "OK" : "FAILED: " + reason));
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool EnsureBuilt(out string reason)
        {
            reason = null;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                reason = "Unity is compiling or importing; refresh must finish first";
                return false;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                reason = "worker build must finish before Play Mode";
                return false;
            }
            if (!File.Exists(Path.GetFullPath(ScenePath)))
            {
                reason = "worker scene missing: " + ScenePath;
                return false;
            }

            string oldCompany = PlayerSettings.companyName;
            string oldProduct = PlayerSettings.productName;
            try
            {
                Directory.CreateDirectory(BuildRoot);
                PlayerSettings.companyName = "UltraAI.IsolatedWorker";
                PlayerSettings.productName = "UltraAI Production Worker";
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = ExecutablePath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.StrictMode,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report == null || report.summary.result != BuildResult.Succeeded)
                {
                    reason = report == null ? "BuildPipeline returned no report"
                        : "worker build failed: " + report.summary.result;
                    return false;
                }

                RemoveCrashHandler();
                CopyFrozenLearningSnapshot();
                UltraProductionWorkerDescriptor descriptor = CreateDescriptor();
                File.WriteAllText(DescriptorPath, JsonUtility.ToJson(descriptor, true),
                    new UTF8Encoding(false));
                Debug.Log("[Ultra] production worker ready: " + ExecutablePath
                    + " / build=" + descriptor.fingerprints.buildFingerprint);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                PlayerSettings.companyName = oldCompany;
                PlayerSettings.productName = oldProduct;
            }
        }

        /// <summary>Delete the crash handler that ships next to the player.
        ///
        /// <para>Every worker process spawns <c>UnityCrashHandler64.exe</c> alongside itself, so
        /// a rollout costs <b>two</b> process creations rather than one, and a decision that
        /// launches 16 workers makes 32 processes appear and vanish in Task Manager — which
        /// looks like a leak and is real overhead either way.</para>
        ///
        /// <para>Nothing is lost by removing it. These are throwaway processes whose only output
        /// is a JSON file, and a crash is already a first-class outcome: the oracle reports it
        /// as "no answer" rather than as a loss, so a post-mortem dump would go unread.</para>
        ///
        /// <para>Deleted rather than skipped at build time because the build pipeline copies it
        /// unconditionally for a Windows standalone player.</para></summary>
        private static void RemoveCrashHandler()
        {
            try
            {
                string path = Path.Combine(BuildRoot, "UnityCrashHandler64.exe");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                // 消せなくても worker は動く。 プロセスが 1 つ余計に立つだけ。
                Debug.LogWarning("[Ultra] クラッシュハンドラを消せなかった: " + ex.Message);
            }
        }

        public static bool TryLoadDescriptor(out UltraProductionWorkerDescriptor descriptor,
            out string reason)
        {
            descriptor = null;
            reason = null;
            try
            {
                if (!File.Exists(DescriptorPath) || !File.Exists(ExecutablePath))
                {
                    reason = "production worker has not been built";
                    return false;
                }
                descriptor = JsonUtility.FromJson<UltraProductionWorkerDescriptor>(
                    File.ReadAllText(DescriptorPath, Encoding.UTF8));
                if (descriptor == null
                    || descriptor.version != UltraProductionWorkerDescriptor.CurrentVersion
                    || descriptor.fingerprints == null)
                {
                    reason = "worker descriptor is invalid";
                    return false;
                }
                if (!string.Equals(descriptor.executableSha256,
                    Sha256File(ExecutablePath), StringComparison.Ordinal))
                {
                    reason = "worker executable hash mismatch";
                    return false;
                }
                if (!UltraBuildManifestValidation.TryValidateFingerprints(
                    descriptor.fingerprints, out string code, out string detail))
                {
                    reason = (code ?? "fingerprint") + ": " + detail;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                descriptor = null;
                return false;
            }
        }

        private static UltraProductionWorkerDescriptor CreateDescriptor()
        {
            string exeHash = Sha256File(ExecutablePath);
            string managed = Path.Combine(BuildRoot,
                "UltraProductionWorker_Data", "Managed", "Assembly-CSharp.dll");
            string assemblyHash = File.Exists(managed) ? Sha256File(managed) : exeHash;
            // 旧: Ultra の方策の組の候補集合のハッシュ (UltraPortfolioProtocol.ComputeCandidateSetHash)。
            //   技量の Ultra ごと候補集合を消した (2026-11-13・#135) ので、 固定の印のハッシュを置く。
            //   照合している所は無く、 TryLoadDescriptor の書式検査 (16 進 64 桁) を通すためだけの値。
            //   いまの測定が照合に使うのは buildFingerprint (exe と Assembly-CSharp.dll の hash) だけ。
            string actionHash = Sha256Text(ActionFingerprintTag);
            return new UltraProductionWorkerDescriptor
            {
                executableFile = Path.GetFileName(ExecutablePath),
                executableSha256 = exeHash,
                assemblySha256 = assemblyHash,
                builtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                fingerprints = new UltraFingerprintSet
                {
                    buildFingerprint = Sha256Text("ultra-production-build-v1\n"
                        + exeHash + "\n" + assemblyHash),
                    dataFingerprint = assemblyHash,
                    checkpointFingerprint = Sha256Text(
                        "run-start-only-no-midrun-checkpoint-v1"),
                    actionFingerprint = actionHash,
                    objectiveFingerprint = Sha256Text(
                        "objective:p7-full-clear;tie:baseline"),
                },
            };
        }

        private static void CopyFrozenLearningSnapshot()
        {
            string source = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "AutoRunLogs", "learning"));
            string destination = Path.Combine(BuildRoot, "AutoRunLogs", "learning");
            if (!Directory.Exists(source)) return;
            if (Directory.Exists(destination)) FileUtil.DeleteFileOrDirectory(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            FileUtil.CopyFileOrDirectory(source, destination);
        }

        internal static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path)) return Hex(sha.ComputeHash(stream));
        }

        internal static string Sha256Text(string value)
        {
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")));
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}

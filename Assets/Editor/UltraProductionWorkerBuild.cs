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
            Application.dataPath, "..", UseDotnetWorker ? "HeadlessWorkerBuild" : "UltraProductionWorkerBuild"));
        public static string ExecutablePath => Path.Combine(BuildRoot,
            UseDotnetWorker ? "DiceBoundHeadless.exe" : "UltraProductionWorker.exe");

        /// <summary>並列のワーカーを Unity を使わない .NET 8 のコンソールアプリ (Tools/headless/DiceBoundHeadless.csproj) で作る (2026-11-22)。
        /// false = 今までどおり IL2CPP の Player。 起動引数・job.json・書き出すファイルはどちらも同じ (RankMarginWorkerBootstrap がそのまま走る)。
        /// .NET 版は Assets/Scripts の .cs をリンクしてビルドするだけで、 シーンも PlayerSettings も触らない。 要: .NET SDK 8。</summary>
        public const bool UseDotnetWorker = false;
        public static string DescriptorPath => Path.Combine(BuildRoot,
            "ultra-production-worker.json");

        /// <summary>worker を IL2CPP で作る (false = Mono)。 作者が遊ぶ本体の PlayerSettings はビルド後に戻す。</summary>
        public const bool UseIl2cpp = true;

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
            if (UseDotnetWorker) return BuildDotnetWorker(out reason);
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
            ScriptingImplementation oldBackend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
            ManagedStrippingLevel oldStripping = PlayerSettings.GetManagedStrippingLevel(BuildTargetGroup.Standalone);
            // backend を変えると Unity が ProjectSettings.asset をその場で書き出すので、 ファイルごと戻す。
            const string settingsPath = "ProjectSettings/ProjectSettings.asset";
            byte[] oldSettingsFile = File.ReadAllBytes(settingsPath);
            try
            {
                // 別の backend で作った出力先には Unity がビルドを拒むので、 切り替えた時だけ消す。
                string otherBackendMark = Path.Combine(BuildRoot, UseIl2cpp ? "MonoBleedingEdge" : "GameAssembly.dll");
                if (Directory.Exists(otherBackendMark) || File.Exists(otherBackendMark))
                    FileUtil.DeleteFileOrDirectory(BuildRoot);
                Directory.CreateDirectory(BuildRoot);
                PlayerSettings.companyName = "UltraAI.IsolatedWorker";
                PlayerSettings.productName = "UltraAI Production Worker";
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,
                    UseIl2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
                // 反射でフィールド名を引く (RankMarginWorkerProtocol) ので stripping は最小。
                PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Minimal);
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
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, oldBackend);
                PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, oldStripping);
                File.WriteAllBytes(settingsPath, oldSettingsFile);
                AssetDatabase.ImportAsset(settingsPath, ImportAssetOptions.ForceUpdate);
            }
        }

        /// <summary>.NET 版のワーカーを dotnet build で BuildRoot へ作る。 学習の写しと descriptor は IL2CPP 版と同じ手順。</summary>
        private static bool BuildDotnetWorker(out string reason)
        {
            reason = null;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                reason = "Unity is compiling or importing; refresh must finish first";
                return false;
            }
            try
            {
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "headless", "DiceBoundHeadless.csproj"));
                string dotnet = File.Exists(@"C:\Program Files\dotnet\dotnet.exe") ? @"C:\Program Files\dotnet\dotnet.exe" : "dotnet";
                Directory.CreateDirectory(BuildRoot);
                var psi = new System.Diagnostics.ProcessStartInfo(dotnet,
                    "build \"" + project + "\" -c Release -nologo -v q -o \"" + BuildRoot + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    var stderr = p.StandardError.ReadToEndAsync();
                    string stdout = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                    {
                        reason = "dotnet build failed (" + p.ExitCode + "): " + stdout + stderr.Result;
                        return false;
                    }
                }
                CopyFrozenLearningSnapshot();
                UltraProductionWorkerDescriptor descriptor = CreateDescriptor();
                File.WriteAllText(DescriptorPath, JsonUtility.ToJson(descriptor, true), new UTF8Encoding(false));
                Debug.Log("[Ultra] .NET worker ready: " + ExecutablePath + " / build=" + descriptor.fingerprints.buildFingerprint);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
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
            // IL2CPP ではコードが GameAssembly.dll に入る (Managed/ は前の Mono ビルドの残りかもしれない)。
            string managed = UseDotnetWorker ? Path.Combine(BuildRoot, "DiceBoundHeadless.dll")
                : UseIl2cpp ? Path.Combine(BuildRoot, "GameAssembly.dll")
                : Path.Combine(BuildRoot, "UltraProductionWorker_Data", "Managed", "Assembly-CSharp.dll");
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

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AutoTest.Editor.Ultra
{
    /// <summary>
    /// Ultra worker を build する直前に、production 側と worker 側が共有すべき入力を
    /// canonical SHA-256 manifest にする Editor 専用 utility。
    ///
    /// <para>
    /// この型は runtime protocol / worker 実装へ依存しない。build orchestration は
    /// <see cref="CaptureCurrentProject"/> の結果を JSON として worker へ埋め込み、live 側の
    /// <see cref="Manifest.compatibilityFingerprint"/> と文字列比較すればよい。
    /// </para>
    ///
    /// <para>
    /// 学習データはルール本体とは別 fingerprint にする。呼出側が明示的に凍結した directory
    /// だけを <see cref="CaptureOptions.learningSnapshotRoot"/> に渡す。live の learning directory
    /// を暗黙に読むことはしないため、manifest 生成と同時に学習ファイルが更新される境界を作らない。
    /// </para>
    /// </summary>
    public static class UltraWorkerBuildManifest
    {
        public const int CurrentFormatVersion = 1;
        public const string NoLearningBoundary = "none-v1";
        public const string FrozenLearningBoundary = "explicit-frozen-learning-tree-v1";

        private const string MechanicsDomain = "ultra-worker/mechanics/v1";
        private const string LearningDomain = "ultra-worker/learning/v1";
        private const string AssemblyDomain = "ultra-worker/assembly-csharp/v1";
        private const string BuildDomain = "ultra-worker/build-settings/v1";
        private const string CompatibilityDomain = "ultra-worker/compatibility/v1";

        // Runtime の判断へ現在直接入り得る学習入力だけを固定する。policy_health / markdown /
        // progress log 等の観測出力は含めない。新しい学習入力を runtime が読むようにした場合は、
        // この allow-list と FrozenLearningBoundary の版を同時に更新する。
        private static readonly HashSet<string> LearningInputLeafNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "boss_tuning.json",
                "event_stats.json",
                "item_stats.json",
                "policy.json",
                "regression_runs.csv",
                "score_assignment.json",
            };

        private static readonly string[] CoreRequiredPaths =
        {
            "ProjectSettings/ProjectVersion.txt",
            "ProjectSettings/ProjectSettings.asset",
            "Packages/manifest.json",
            "Packages/packages-lock.json",
            "Assets/Data/InventorySystem/items.json",
            "Assets/Resources/enemies.json",
            "Assets/Resources/Events/event_list.txt",
        };

        [Serializable]
        public sealed class CaptureOptions
        {
            /// <summary>空なら現在開いている Unity project root。</summary>
            public string projectRoot;

            /// <summary>
            /// build orchestration が先に凍結した learning tree。空なら学習データを使わない構成。
            /// AutoRunLogs/learning を暗黙に採用しないことが snapshot 境界。
            /// </summary>
            public string learningSnapshotRoot;

            /// <summary>worker-prod / worker-dev 等、呼出側が区別したい build flavor。</summary>
            public string buildFlavor = "worker-prod";

            /// <summary>
            /// The exact ordered scene list that the worker BuildPlayerOptions will receive.
            /// Null means "use the enabled EditorBuildSettings scenes"; an empty list is an
            /// explicit empty worker build. Capturing this never mutates EditorBuildSettings.
            /// </summary>
            public List<string> workerBuildScenes;

            /// <summary>
            /// Effective PlayerSettings identity used by the worker build. Null falls back to
            /// the current PlayerSettings value; an empty string is an explicit override.
            /// </summary>
            public string workerCompanyName;
            public string workerProductName;

            public bool includeDefaultMechanicalInputs = true;
            public bool includeEnabledSceneDependencies = true;
            public bool captureEditorBuildSettings = true;
            public bool captureAssemblyCSharp = true;
            public bool requireCoreInputs = true;
            public bool requireStableEditor = true;

            /// <summary>project root 内の追加ファイルまたはdirectory。絶対pathもroot内なら許可。</summary>
            public List<string> additionalMechanicalPaths = new List<string>();
        }

        [Serializable]
        public sealed class FileRecord
        {
            public string path;
            public long byteLength;
            public string sha256;
        }

        [Serializable]
        public sealed class AssemblySourceRecord
        {
            public string path;
            public long byteLength;
            public string sha256;
        }

        [Serializable]
        public sealed class AssemblyCSharpRecord
        {
            public bool present;
            public string name;
            public string flags;
            public string outputFileName;
            public string compiledAssemblySha256;
            public string graphFingerprint;
            public List<string> defines = new List<string>();
            public List<string> assemblyReferences = new List<string>();
            public List<string> compiledReferenceFileNames = new List<string>();
            public List<AssemblySourceRecord> sources = new List<AssemblySourceRecord>();
        }

        [Serializable]
        public sealed class BuildSettingsRecord
        {
            public string unityVersion;
            public string activeBuildTarget;
            public string buildTargetGroup;
            public string scriptingBackend;
            public string apiCompatibilityLevel;
            public string buildFlavor;
            public string companyName;
            public string productName;
            public bool developmentBuild;
            public bool buildScenesOverridden;
            public bool companyNameOverridden;
            public bool productNameOverridden;
            public List<string> scriptingDefines = new List<string>();
            /// <summary>
            /// BuildPlayerOptions に渡す実効順序を "index|path" で保持する。
            /// EditorBuildSettings の disabled 項目は実ビルド入力ではないため含めない。
            /// </summary>
            public List<string> buildScenes = new List<string>();
            public string fingerprint;
        }

        [Serializable]
        public sealed class Manifest
        {
            public int formatVersion = CurrentFormatVersion;
            public string generatedUtc;
            public string projectName;
            public string learningBoundary;
            public bool assemblyCSharpRequired;
            public bool editorBuildSettingsCaptured;
            public List<string> missingRequiredInputs = new List<string>();
            public List<FileRecord> mechanicalInputs = new List<FileRecord>();
            public List<FileRecord> learningInputs = new List<FileRecord>();
            public AssemblyCSharpRecord assemblyCSharp = new AssemblyCSharpRecord();
            public BuildSettingsRecord buildSettings = new BuildSettingsRecord();
            public string mechanicsFingerprint;
            public string learningFingerprint;
            public string assemblyFingerprint;
            public string buildSettingsFingerprint;
            /// <summary>
            /// Ultra strict compatibility の正本。mechanics / assembly graph / build settings /
            /// frozen learning snapshot の全てを含む。compiledAssemblySha256 は診断値であり含めない。
            /// </summary>
            public string compatibilityFingerprint;

            public bool IsUsable()
            {
                return formatVersion == CurrentFormatVersion
                    && missingRequiredInputs != null
                    && missingRequiredInputs.Count == 0
                    && (!assemblyCSharpRequired || (assemblyCSharp != null && assemblyCSharp.present))
                    && IsSha256(mechanicsFingerprint)
                    && IsSha256(learningFingerprint)
                    && IsSha256(assemblyFingerprint)
                    && IsSha256(buildSettingsFingerprint)
                    && IsSha256(compatibilityFingerprint);
            }
        }

        [Serializable]
        public sealed class FileDifference
        {
            public string path;
            public string leftSha256;
            public string rightSha256;
            public string kind;
        }

        [Serializable]
        public sealed class Comparison
        {
            public bool compatible;
            public bool learningCompared;
            public List<string> reasons = new List<string>();
            public List<FileDifference> mechanicalDifferences = new List<FileDifference>();
            public List<FileDifference> learningDifferences = new List<FileDifference>();
        }

        /// <summary>現在開いているprojectをcaptureする。learning root は明示指定時だけ読む。</summary>
        public static Manifest CaptureCurrentProject(
            string frozenLearningSnapshotRoot = null,
            string buildFlavor = "worker-prod")
        {
            return Capture(new CaptureOptions
            {
                projectRoot = ProjectRoot(),
                learningSnapshotRoot = frozenLearningSnapshotRoot,
                buildFlavor = string.IsNullOrEmpty(buildFlavor) ? "worker-prod" : buildFlavor,
            });
        }

        /// <summary>
        /// 指定入力からmanifestを作る。BuildPipeline は呼ばず、EditorBuildSettings も変更しない。
        /// </summary>
        public static Manifest Capture(CaptureOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.requireStableEditor && (EditorApplication.isCompiling || EditorApplication.isUpdating))
                throw new InvalidOperationException(
                    "Ultra manifest capture requires an idle Editor (compile/import is in progress).");

            string root = CanonicalDirectory(
                string.IsNullOrWhiteSpace(options.projectRoot) ? ProjectRoot() : options.projectRoot);
            bool buildScenesOverridden = options.workerBuildScenes != null;
            List<string> workerBuildScenes = ResolveWorkerBuildScenes(options.workerBuildScenes);
            var manifest = new Manifest
            {
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                projectName = new DirectoryInfo(root).Name,
                learningBoundary = string.IsNullOrWhiteSpace(options.learningSnapshotRoot)
                    ? NoLearningBoundary
                    : FrozenLearningBoundary,
                assemblyCSharpRequired = options.captureAssemblyCSharp,
                editorBuildSettingsCaptured = options.captureEditorBuildSettings,
            };

            var files = new HashSet<string>(PathComparer);
            if (options.includeDefaultMechanicalInputs)
                CollectDefaultMechanicalInputs(
                    root,
                    options.includeEnabledSceneDependencies,
                    workerBuildScenes,
                    files);

            if (options.additionalMechanicalPaths != null)
            {
                for (int i = 0; i < options.additionalMechanicalPaths.Count; i++)
                    CollectExplicitPath(root, options.additionalMechanicalPaths[i], files);
            }

            if (options.requireCoreInputs)
            {
                for (int i = 0; i < CoreRequiredPaths.Length; i++)
                {
                    string full = ResolveInside(root, CoreRequiredPaths[i]);
                    if (!File.Exists(full)) manifest.missingRequiredInputs.Add(CoreRequiredPaths[i]);
                    else files.Add(full);
                }
            }

            manifest.mechanicalInputs = HashProjectFiles(root, files);

            if (!string.IsNullOrWhiteSpace(options.learningSnapshotRoot))
            {
                string learningRoot = CanonicalDirectory(options.learningSnapshotRoot);
                if (!Directory.Exists(learningRoot))
                    throw new DirectoryNotFoundException("Frozen learning snapshot not found: " + learningRoot);
                manifest.learningInputs = HashLearningTree(learningRoot);
            }

            manifest.buildSettings = options.captureEditorBuildSettings
                ? CaptureBuildSettings(options, workerBuildScenes, buildScenesOverridden)
                : EmptyBuildSettings(options.buildFlavor);

            manifest.assemblyCSharp = options.captureAssemblyCSharp
                ? CaptureAssemblyCSharp(root)
                : EmptyAssemblyRecord();

            manifest.mechanicsFingerprint = HashEntries(MechanicsDomain, manifest.mechanicalInputs);
            manifest.learningFingerprint = HashEntries(
                LearningDomain + "/" + manifest.learningBoundary,
                manifest.learningInputs);
            manifest.assemblyFingerprint = manifest.assemblyCSharp.graphFingerprint;
            manifest.buildSettingsFingerprint = manifest.buildSettings.fingerprint;
            manifest.compatibilityFingerprint = HashStrings(
                CompatibilityDomain,
                manifest.formatVersion.ToString(CultureInfo.InvariantCulture),
                manifest.mechanicsFingerprint,
                manifest.assemblyFingerprint,
                manifest.buildSettingsFingerprint,
                manifest.learningBoundary,
                manifest.learningFingerprint);

            manifest.missingRequiredInputs.Sort(StringComparer.Ordinal);
            return manifest;
        }

        /// <summary>
        /// 二つのmanifestを比較する。Ultra実行時は requireLearningMatch=true を使う。
        /// false はルール/buildだけを監査する診断用途。
        /// </summary>
        public static Comparison Compare(Manifest left, Manifest right, bool requireLearningMatch = true)
        {
            var result = new Comparison { learningCompared = requireLearningMatch };
            if (left == null || right == null)
            {
                result.reasons.Add("manifest-null");
                return result;
            }
            if (!left.IsUsable()) result.reasons.Add("left-manifest-invalid");
            if (!right.IsUsable()) result.reasons.Add("right-manifest-invalid");
            if (left.formatVersion != right.formatVersion) result.reasons.Add("format-version-mismatch");

            if (!Same(left.mechanicsFingerprint, right.mechanicsFingerprint))
            {
                result.reasons.Add("mechanics-mismatch");
                result.mechanicalDifferences = Diff(left.mechanicalInputs, right.mechanicalInputs);
            }
            if (!Same(left.assemblyFingerprint, right.assemblyFingerprint))
                result.reasons.Add("assembly-csharp-graph-mismatch");
            if (!Same(left.buildSettingsFingerprint, right.buildSettingsFingerprint))
                result.reasons.Add("build-settings-mismatch");

            if (requireLearningMatch)
            {
                if (!Same(left.learningBoundary, right.learningBoundary))
                    result.reasons.Add("learning-boundary-mismatch");
                if (!Same(left.learningFingerprint, right.learningFingerprint))
                {
                    result.reasons.Add("learning-snapshot-mismatch");
                    result.learningDifferences = Diff(left.learningInputs, right.learningInputs);
                }
            }

            result.compatible = result.reasons.Count == 0;
            return result;
        }

        public static string ToJson(Manifest manifest, bool prettyPrint = true)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            return JsonUtility.ToJson(manifest, prettyPrint);
        }

        public static Manifest FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Manifest JSON is empty.", nameof(json));
            Manifest manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest == null) throw new InvalidDataException("Manifest JSON could not be parsed.");
            return manifest;
        }

        public static void WriteJson(string path, Manifest manifest, bool prettyPrint = true)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Output path is empty.", nameof(path));
            string full = Path.GetFullPath(path);
            string parent = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(full, ToJson(manifest, prettyPrint), new UTF8Encoding(false));
        }

        private static void CollectDefaultMechanicalInputs(
            string root,
            bool includeEnabledSceneDependencies,
            IReadOnlyList<string> workerBuildScenes,
            HashSet<string> files)
        {
            CollectDirectory(root, "Assets/Scripts", RuntimeCodeInput, files);
            CollectDirectory(root, "Assets/Data", GeneralMechanicalInput, files);
            CollectDirectory(root, "Assets/Resources", GeneralMechanicalInput, files);
            CollectDirectory(root, "Assets/StreamingAssets", GeneralMechanicalInput, files);
            CollectDirectory(root, "Assets/Plugins", GeneralMechanicalInput, files);
            CollectDirectory(root, "Assets/Scenes", GeneralMechanicalInput, files);
            CollectDirectory(root, "ProjectSettings", GeneralMechanicalInput, files);

            AddIfPresent(root, "Packages/manifest.json", files);
            AddIfPresent(root, "Packages/packages-lock.json", files);

            // asmdef/asmref が Scripts 外へ追加されてもassembly境界の変更を拾う。
            string assets = ResolveInside(root, "Assets");
            if (Directory.Exists(assets))
            {
                foreach (string path in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(path);
                    if (ext.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".asmref", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".asmdef.meta", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".asmref.meta", StringComparison.OrdinalIgnoreCase))
                        files.Add(CheckedInside(root, path));
                }
            }

            if (!includeEnabledSceneDependencies || !Same(root, ProjectRoot())) return;

            string[] enabledScenes = workerBuildScenes == null
                ? new string[0]
                : workerBuildScenes.ToArray();
            if (enabledScenes.Length == 0) return;

            string[] dependencies = AssetDatabase.GetDependencies(enabledScenes, true);
            for (int i = 0; i < dependencies.Length; i++)
            {
                string assetPath = dependencies[i];
                if (string.IsNullOrEmpty(assetPath)
                    || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                string full = ResolveInside(root, assetPath);
                if (File.Exists(full) && GeneralMechanicalInput(full)) files.Add(full);
            }
        }

        private static void CollectExplicitPath(string root, string path, HashSet<string> files)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : ResolveInside(root, path);
            full = CheckedInside(root, full);
            if (File.Exists(full))
            {
                if (GeneralMechanicalInput(full)) files.Add(full);
                return;
            }
            if (!Directory.Exists(full))
                throw new FileNotFoundException("Additional mechanical input not found.", full);
            foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                if (GeneralMechanicalInput(file)) files.Add(CheckedInside(root, file));
        }

        private static void CollectDirectory(
            string root,
            string relative,
            Func<string, bool> include,
            HashSet<string> files)
        {
            string full = ResolveInside(root, relative);
            if (!Directory.Exists(full)) return;
            foreach (string path in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                if (include(path)) files.Add(CheckedInside(root, path));
        }

        private static bool RuntimeCodeInput(string path)
        {
            if (IgnoredInput(path)) return false;
            string ext = Path.GetExtension(path);
            return ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".asmref", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".meta", StringComparison.OrdinalIgnoreCase);
        }

        private static bool GeneralMechanicalInput(string path) => !IgnoredInput(path);

        private static bool IgnoredInput(string path)
        {
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return true;
            return name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("~", StringComparison.Ordinal)
                || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".orig", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".rej", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddIfPresent(string root, string relative, HashSet<string> files)
        {
            string full = ResolveInside(root, relative);
            if (File.Exists(full)) files.Add(full);
        }

        private static List<FileRecord> HashProjectFiles(string root, IEnumerable<string> files)
        {
            var result = new List<FileRecord>();
            foreach (string full in files)
            {
                string checkedPath = CheckedInside(root, full);
                result.Add(HashFile(checkedPath, ProjectRelative(root, checkedPath)));
            }
            result.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            return result;
        }

        private static List<FileRecord> HashLearningTree(string learningRoot)
        {
            var result = new List<FileRecord>();
            foreach (string full in Directory.EnumerateFiles(learningRoot, "*", SearchOption.AllDirectories))
            {
                if (!LearningInputLeafNames.Contains(Path.GetFileName(full))) continue;
                string checkedPath = CheckedInside(learningRoot, full);
                result.Add(HashFile(checkedPath, "learning/" + ProjectRelative(learningRoot, checkedPath)));
            }
            result.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            return result;
        }

        private static FileRecord HashFile(string fullPath, string logicalPath)
        {
            var before = new FileInfo(fullPath);
            long length = before.Length;
            long writeTicks = before.LastWriteTimeUtc.Ticks;
            string hash;
            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
                hash = Hex(sha.ComputeHash(stream));

            var after = new FileInfo(fullPath);
            if (after.Length != length || after.LastWriteTimeUtc.Ticks != writeTicks)
                throw new IOException("Input changed while Ultra manifest was being captured: " + fullPath);

            return new FileRecord
            {
                path = NormalizeLogicalPath(logicalPath),
                byteLength = length,
                sha256 = hash,
            };
        }

        private static AssemblyCSharpRecord CaptureAssemblyCSharp(string root)
        {
            var record = EmptyAssemblyRecord();
            UnityEditor.Compilation.Assembly[] assemblies =
                CompilationPipeline.GetAssemblies(AssembliesType.Player);
            UnityEditor.Compilation.Assembly assembly = assemblies == null
                ? null
                : assemblies.FirstOrDefault(a => a != null && a.name == "Assembly-CSharp");
            if (assembly == null) return record;

            record.present = true;
            record.name = assembly.name;
            record.flags = assembly.flags.ToString();
            record.outputFileName = Path.GetFileName(assembly.outputPath ?? "");
            record.defines = SortedDistinct(assembly.defines);
            record.compiledReferenceFileNames = SortedDistinct(
                assembly.compiledAssemblyReferences == null
                    ? null
                    : assembly.compiledAssemblyReferences.Select(Path.GetFileName));
            record.assemblyReferences = SortedDistinct(
                assembly.assemblyReferences == null
                    ? null
                    : assembly.assemblyReferences.Where(a => a != null).Select(a => a.name));

            if (assembly.sourceFiles != null)
            {
                for (int i = 0; i < assembly.sourceFiles.Length; i++)
                {
                    string source = assembly.sourceFiles[i];
                    if (string.IsNullOrEmpty(source)) continue;
                    string full = Path.IsPathRooted(source)
                        ? Path.GetFullPath(source)
                        : Path.GetFullPath(Path.Combine(root, source));
                    if (!File.Exists(full)) continue;
                    FileRecord file = HashFile(full, LogicalSourcePath(root, full));
                    record.sources.Add(new AssemblySourceRecord
                    {
                        path = file.path,
                        byteLength = file.byteLength,
                        sha256 = file.sha256,
                    });
                }
            }
            record.sources.Sort((a, b) => string.CompareOrdinal(a.path, b.path));

            string output = assembly.outputPath;
            if (!string.IsNullOrEmpty(output))
            {
                string fullOutput = Path.IsPathRooted(output)
                    ? output
                    : Path.Combine(root, output);
                if (File.Exists(fullOutput))
                    record.compiledAssemblySha256 = HashFile(fullOutput, Path.GetFileName(fullOutput)).sha256;
            }

            var graphParts = new List<string>
            {
                record.name ?? "",
                record.flags ?? "",
                record.outputFileName ?? "",
            };
            graphParts.AddRange(record.defines.Select(v => "define:" + v));
            graphParts.AddRange(record.assemblyReferences.Select(v => "assembly-ref:" + v));
            graphParts.AddRange(record.compiledReferenceFileNames.Select(v => "compiled-ref:" + v));
            graphParts.AddRange(record.sources.Select(
                v => "source:" + v.path + ":" + v.byteLength.ToString(CultureInfo.InvariantCulture) + ":" + v.sha256));
            record.graphFingerprint = HashStrings(AssemblyDomain, graphParts.ToArray());
            return record;
        }

        private static AssemblyCSharpRecord EmptyAssemblyRecord()
        {
            var record = new AssemblyCSharpRecord
            {
                present = false,
                name = "",
                flags = "",
                outputFileName = "",
                compiledAssemblySha256 = "",
            };
            record.graphFingerprint = HashStrings(AssemblyDomain, "absent");
            return record;
        }

        private static List<string> ResolveWorkerBuildScenes(IReadOnlyList<string> sceneOverride)
        {
            IEnumerable<string> source;
            if (sceneOverride != null)
            {
                source = sceneOverride;
            }
            else
            {
                source = (EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0])
                    .Where(scene => scene != null && scene.enabled)
                    .Select(scene => scene.path);
            }

            var result = new List<string>();
            foreach (string path in source) result.Add(CanonicalBuildScenePath(path));
            return result;
        }

        private static string CanonicalBuildScenePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Worker build scene path is empty.", nameof(path));
            if (Path.IsPathRooted(path))
                throw new ArgumentException("Worker build scene path must be project-relative: " + path, nameof(path));

            string normalized = path.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment == "." || segment == ".."))
                throw new ArgumentException("Worker build scene path is not canonical: " + path, nameof(path));
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal)
                && !normalized.StartsWith("Packages/", StringComparison.Ordinal))
                throw new ArgumentException(
                    "Worker build scene must use an Assets/ or Packages/ asset path: " + path,
                    nameof(path));
            if (!normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Worker build scene is not a .unity asset: " + path, nameof(path));
            return normalized;
        }

        private static BuildSettingsRecord CaptureBuildSettings(
            CaptureOptions options,
            IReadOnlyList<string> workerBuildScenes,
            bool buildScenesOverridden)
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            string defines = "";
            string backend = "unknown";
            string api = "unknown";
#pragma warning disable 618
            try { defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group) ?? ""; } catch { }
            try { backend = PlayerSettings.GetScriptingBackend(group).ToString(); } catch { }
            try { api = PlayerSettings.GetApiCompatibilityLevel(group).ToString(); } catch { }
#pragma warning restore 618

            var record = new BuildSettingsRecord
            {
                unityVersion = Application.unityVersion,
                activeBuildTarget = target.ToString(),
                buildTargetGroup = group.ToString(),
                scriptingBackend = backend,
                apiCompatibilityLevel = api,
                buildFlavor = string.IsNullOrEmpty(options.buildFlavor) ? "worker-prod" : options.buildFlavor,
                companyName = options.workerCompanyName ?? PlayerSettings.companyName ?? "",
                productName = options.workerProductName ?? PlayerSettings.productName ?? "",
                developmentBuild = EditorUserBuildSettings.development,
                buildScenesOverridden = buildScenesOverridden,
                companyNameOverridden = options.workerCompanyName != null,
                productNameOverridden = options.workerProductName != null,
                scriptingDefines = SortedDistinct(
                    defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)),
            };

            for (int i = 0; i < workerBuildScenes.Count; i++)
            {
                record.buildScenes.Add(
                    i.ToString(CultureInfo.InvariantCulture) + "|"
                    + workerBuildScenes[i]);
            }

            var parts = new List<string>
            {
                record.unityVersion ?? "",
                record.activeBuildTarget ?? "",
                record.buildTargetGroup ?? "",
                record.scriptingBackend ?? "",
                record.apiCompatibilityLevel ?? "",
                record.buildFlavor ?? "",
                record.companyName ?? "",
                record.productName ?? "",
                record.developmentBuild ? "development" : "release",
            };
            parts.AddRange(record.scriptingDefines.Select(v => "define:" + v));
            parts.AddRange(record.buildScenes.Select(v => "scene:" + v));
            record.fingerprint = HashStrings(BuildDomain, parts.ToArray());
            return record;
        }

        private static BuildSettingsRecord EmptyBuildSettings(string buildFlavor)
        {
            var record = new BuildSettingsRecord
            {
                buildFlavor = string.IsNullOrEmpty(buildFlavor) ? "worker-prod" : buildFlavor,
            };
            record.fingerprint = HashStrings(BuildDomain, "not-captured", record.buildFlavor);
            return record;
        }

        private static List<FileDifference> Diff(List<FileRecord> left, List<FileRecord> right)
        {
            var a = ToFileMap(left);
            var b = ToFileMap(right);
            var paths = new SortedSet<string>(a.Keys, StringComparer.Ordinal);
            paths.UnionWith(b.Keys);
            var result = new List<FileDifference>();
            foreach (string path in paths)
            {
                a.TryGetValue(path, out FileRecord av);
                b.TryGetValue(path, out FileRecord bv);
                if (av == null)
                {
                    result.Add(new FileDifference
                    {
                        path = path, kind = "right-only", leftSha256 = "", rightSha256 = bv.sha256,
                    });
                }
                else if (bv == null)
                {
                    result.Add(new FileDifference
                    {
                        path = path, kind = "left-only", leftSha256 = av.sha256, rightSha256 = "",
                    });
                }
                else if (!Same(av.sha256, bv.sha256) || av.byteLength != bv.byteLength)
                {
                    result.Add(new FileDifference
                    {
                        path = path, kind = "changed", leftSha256 = av.sha256, rightSha256 = bv.sha256,
                    });
                }
            }
            return result;
        }

        private static Dictionary<string, FileRecord> ToFileMap(List<FileRecord> entries)
        {
            var map = new Dictionary<string, FileRecord>(StringComparer.Ordinal);
            if (entries == null) return map;
            for (int i = 0; i < entries.Count; i++)
            {
                FileRecord entry = entries[i];
                if (entry != null && !string.IsNullOrEmpty(entry.path)) map[entry.path] = entry;
            }
            return map;
        }

        private static string HashEntries(string domain, List<FileRecord> entries)
        {
            var parts = new List<string>();
            if (entries != null)
            {
                var sorted = entries.Where(e => e != null)
                    .OrderBy(e => e.path, StringComparer.Ordinal);
                foreach (FileRecord entry in sorted)
                {
                    parts.Add(entry.path ?? "");
                    parts.Add(entry.byteLength.ToString(CultureInfo.InvariantCulture));
                    parts.Add(entry.sha256 ?? "");
                }
            }
            return HashStrings(domain, parts.ToArray());
        }

        private static string HashStrings(string domain, params string[] values)
        {
            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer, new UTF8Encoding(false), true))
            {
                writer.Write(domain ?? "");
                writer.Write(values == null ? 0 : values.Length);
                if (values != null)
                    for (int i = 0; i < values.Length; i++) writer.Write(values[i] ?? "");
                writer.Flush();
                buffer.Position = 0;
                using (SHA256 sha = SHA256.Create()) return Hex(sha.ComputeHash(buffer));
            }
        }

        private static List<string> SortedDistinct(IEnumerable<string> values)
        {
            if (values == null) return new List<string>();
            return values.Where(v => !string.IsNullOrEmpty(v))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
        }

        private static string LogicalSourcePath(string root, string full)
        {
            if (IsInside(root, full)) return ProjectRelative(root, full);
            // Package source 等がproject外へ解決される環境でも、絶対pathはmanifestへ入れない。
            return "external-source/" + Path.GetFileName(full);
        }

        private static string ProjectRoot()
        {
            return CanonicalDirectory(Path.Combine(Application.dataPath, ".."));
        }

        private static string CanonicalDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Directory path is empty.");
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string ResolveInside(string root, string relative)
        {
            return CheckedInside(root, Path.GetFullPath(Path.Combine(root, relative)));
        }

        private static string CheckedInside(string root, string path)
        {
            string full = Path.GetFullPath(path);
            if (!IsInside(root, full))
                throw new InvalidOperationException("Manifest input escaped its declared root: " + full);
            return full;
        }

        private static bool IsInside(string root, string path)
        {
            string canonicalRoot = CanonicalDirectory(root);
            string full = Path.GetFullPath(path);
            if (string.Equals(canonicalRoot, full, PathComparison)) return true;
            string prefix = canonicalRoot + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, PathComparison);
        }

        private static string ProjectRelative(string root, string full)
        {
            string canonicalRoot = CanonicalDirectory(root);
            string canonicalFull = CheckedInside(canonicalRoot, full);
            string relative = canonicalFull.Substring(canonicalRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return NormalizeLogicalPath(relative);
        }

        private static string NormalizeLogicalPath(string path)
        {
            return (path ?? "").Replace('\\', '/').TrimStart('/');
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

        private static StringComparer PathComparer
            => Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private static StringComparison PathComparison
            => Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
    }
}
#endif

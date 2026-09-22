#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using AutoTest.Editor.Ultra;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    [TestFixture]
    public sealed class UltraWorkerBuildManifestTests
    {
        private readonly List<string> _tempRoots = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _tempRoots.Count; i++)
            {
                string root = _tempRoots[i];
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            _tempRoots.Clear();
        }

        [Test]
        public void CanonicalFingerprint_IgnoresCreationOrderAndAbsoluteRoot()
        {
            string left = NewTree(reverseCreationOrder: false);
            string right = NewTree(reverseCreationOrder: true);

            UltraWorkerBuildManifest.Manifest a = Capture(left);
            UltraWorkerBuildManifest.Manifest b = Capture(right);
            UltraWorkerBuildManifest.Comparison comparison =
                UltraWorkerBuildManifest.Compare(a, b, requireLearningMatch: true);

            Assert.That(a.IsUsable(), Is.True);
            Assert.That(b.IsUsable(), Is.True);
            Assert.That(a.mechanicsFingerprint, Is.EqualTo(b.mechanicsFingerprint));
            Assert.That(a.learningFingerprint, Is.EqualTo(b.learningFingerprint));
            Assert.That(a.compatibilityFingerprint, Is.EqualTo(b.compatibilityFingerprint));
            Assert.That(comparison.compatible, Is.True);
        }

        [Test]
        public void MechanicalMutation_FailsClosedAndReportsPath()
        {
            string left = NewTree(false);
            string right = NewTree(false);
            File.WriteAllText(Path.Combine(right, "mechanics", "a.txt"), "changed");

            UltraWorkerBuildManifest.Comparison comparison =
                UltraWorkerBuildManifest.Compare(Capture(left), Capture(right), true);

            Assert.That(comparison.compatible, Is.False);
            Assert.That(comparison.reasons, Does.Contain("mechanics-mismatch"));
            Assert.That(comparison.mechanicalDifferences.Count, Is.EqualTo(1));
            Assert.That(comparison.mechanicalDifferences[0].path, Is.EqualTo("mechanics/a.txt"));
        }

        [Test]
        public void LearningSnapshot_IsSeparateAndUsesBehaviorAllowList()
        {
            string left = NewTree(false);
            string right = NewTree(false);
            File.WriteAllText(Path.Combine(right, "learning", "policy.json"), "{\"x\":2}");
            // 観測出力はruntime判断入力ではないのでsnapshot fingerprintへ入らない。
            File.WriteAllText(Path.Combine(right, "learning", "policy_health.json"), "different diagnostic");

            UltraWorkerBuildManifest.Manifest a = Capture(left);
            UltraWorkerBuildManifest.Manifest b = Capture(right);
            UltraWorkerBuildManifest.Comparison strict = UltraWorkerBuildManifest.Compare(a, b, true);
            UltraWorkerBuildManifest.Comparison rulesOnly = UltraWorkerBuildManifest.Compare(a, b, false);

            Assert.That(a.mechanicsFingerprint, Is.EqualTo(b.mechanicsFingerprint));
            Assert.That(a.learningFingerprint, Is.Not.EqualTo(b.learningFingerprint));
            Assert.That(strict.compatible, Is.False);
            Assert.That(strict.reasons, Does.Contain("learning-snapshot-mismatch"));
            Assert.That(strict.learningDifferences.Count, Is.EqualTo(1));
            Assert.That(strict.learningDifferences[0].path, Is.EqualTo("learning/policy.json"));
            Assert.That(rulesOnly.compatible, Is.True);
        }

        [Test]
        public void JsonRoundTrip_PreservesCompatibilityFingerprint()
        {
            UltraWorkerBuildManifest.Manifest source = Capture(NewTree(false));
            string json = UltraWorkerBuildManifest.ToJson(source, false);
            UltraWorkerBuildManifest.Manifest restored = UltraWorkerBuildManifest.FromJson(json);

            Assert.That(restored.IsUsable(), Is.True);
            Assert.That(restored.compatibilityFingerprint, Is.EqualTo(source.compatibilityFingerprint));
            Assert.That(UltraWorkerBuildManifest.Compare(source, restored, true).compatible, Is.True);
        }

        [Test]
        public void WorkerBuildOverrides_RecordEffectiveInputsAndAffectCompatibility()
        {
            string root = NewTree(false);
            var forward = new List<string>
            {
                "Assets/Scenes/SampleScene2.unity",
                "Assets/Scenes/SampleScene.unity",
            };
            var reverse = new List<string>(forward);
            reverse.Reverse();

            UltraWorkerBuildManifest.Manifest a = CaptureWithWorkerBuild(root, forward, "UltraWorker");
            UltraWorkerBuildManifest.Manifest b = CaptureWithWorkerBuild(root, reverse, "UltraWorker");
            UltraWorkerBuildManifest.Manifest c = CaptureWithWorkerBuild(root, forward, "OtherWorker");

            Assert.That(a.buildSettings.buildScenesOverridden, Is.True);
            Assert.That(a.buildSettings.companyNameOverridden, Is.True);
            Assert.That(a.buildSettings.productNameOverridden, Is.True);
            Assert.That(a.buildSettings.companyName, Is.EqualTo("Ultra Tests"));
            Assert.That(a.buildSettings.productName, Is.EqualTo("UltraWorker"));
            Assert.That(a.buildSettings.buildScenes, Is.EqualTo(new[]
            {
                "0|Assets/Scenes/SampleScene2.unity",
                "1|Assets/Scenes/SampleScene.unity",
            }));
            Assert.That(UltraWorkerBuildManifest.Compare(a, b, true).reasons,
                Does.Contain("build-settings-mismatch"));
            Assert.That(UltraWorkerBuildManifest.Compare(a, c, true).reasons,
                Does.Contain("build-settings-mismatch"));
        }

        private UltraWorkerBuildManifest.Manifest Capture(string root)
        {
            return UltraWorkerBuildManifest.Capture(new UltraWorkerBuildManifest.CaptureOptions
            {
                projectRoot = root,
                learningSnapshotRoot = Path.Combine(root, "learning"),
                buildFlavor = "test-worker",
                includeDefaultMechanicalInputs = false,
                includeEnabledSceneDependencies = false,
                captureEditorBuildSettings = false,
                captureAssemblyCSharp = false,
                requireCoreInputs = false,
                requireStableEditor = false,
                additionalMechanicalPaths = new List<string> { "mechanics" },
            });
        }

        private UltraWorkerBuildManifest.Manifest CaptureWithWorkerBuild(
            string root,
            List<string> scenes,
            string productName)
        {
            return UltraWorkerBuildManifest.Capture(new UltraWorkerBuildManifest.CaptureOptions
            {
                projectRoot = root,
                learningSnapshotRoot = Path.Combine(root, "learning"),
                buildFlavor = "test-worker",
                workerBuildScenes = scenes,
                workerCompanyName = "Ultra Tests",
                workerProductName = productName,
                includeDefaultMechanicalInputs = false,
                includeEnabledSceneDependencies = false,
                captureEditorBuildSettings = true,
                captureAssemblyCSharp = false,
                requireCoreInputs = false,
                requireStableEditor = false,
                additionalMechanicalPaths = new List<string> { "mechanics" },
            });
        }

        private string NewTree(bool reverseCreationOrder)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "ultra-manifest-tests-" + Guid.NewGuid().ToString("N"));
            _tempRoots.Add(root);
            Directory.CreateDirectory(Path.Combine(root, "mechanics", "nested"));
            Directory.CreateDirectory(Path.Combine(root, "learning", "band_50"));

            string a = Path.Combine(root, "mechanics", "a.txt");
            string b = Path.Combine(root, "mechanics", "nested", "b.txt");
            if (reverseCreationOrder)
            {
                File.WriteAllText(b, "beta");
                File.WriteAllText(a, "alpha");
            }
            else
            {
                File.WriteAllText(a, "alpha");
                File.WriteAllText(b, "beta");
            }

            File.WriteAllText(Path.Combine(root, "learning", "policy.json"), "{\"x\":1}");
            File.WriteAllText(Path.Combine(root, "learning", "band_50", "item_stats.json"), "{\"items\":[]}");
            File.WriteAllText(Path.Combine(root, "learning", "policy_health.json"), "ignored diagnostic");
            return root;
        }
    }
}
#endif

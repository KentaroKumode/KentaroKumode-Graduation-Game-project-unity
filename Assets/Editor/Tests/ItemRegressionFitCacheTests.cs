#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>The regression fit cache.
    ///
    /// <para><b>Why this is worth testing.</b> The cache exists because a rollout worker is a
    /// fresh process per run, so the OLS was being paid on every rollout — measured at roughly
    /// 70% of a worker's wall time. But a coefficient table is the input to every item decision
    /// the BOT makes, so a cache that returns a <em>subtly wrong</em> table is far worse than no
    /// cache: the runs still complete, the numbers still look plausible, and the policy has
    /// quietly changed. The dangerous shape is a half-written file — the replace is not atomic,
    /// and a truncated table parses perfectly well as a shorter one.</para>
    ///
    /// <para>So the properties under test are: a hit reproduces the fit exactly, a stale input
    /// invalidates, and a truncated file is refused rather than believed.</para></summary>
    [TestFixture]
    public sealed class ItemRegressionFitCacheTests
    {
        private const string CacheFile = "regression_fit.txt";
        private const string CsvFile = "regression_runs.csv";

        private string _rootA;
        private string _rootB;

        [SetUp]
        public void SetUp()
        {
            string baseDir = Path.Combine(Path.GetTempPath(),
                "itemreg_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            _rootA = Path.Combine(baseDir, "a");
            _rootB = Path.Combine(baseDir, "b");
            WriteCorpus(_rootA, seed: 1);
            WriteCorpus(_rootB, seed: 2);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                string baseDir = Path.GetDirectoryName(_rootA);
                if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
                    Directory.Delete(baseDir, true);
            }
            catch { }
        }

        /// <summary>A corpus big enough to fit: <see cref="ItemRegression"/> needs 200 rows at
        /// floor 6+ and only admits items acquired at least 30 times.</summary>
        private static void WriteCorpus(string root, int seed)
        {
            Directory.CreateDirectory(root);
            string[] items = { "alpha", "beta", "gamma", "delta" };
            var rng = new System.Random(seed);
            var sb = new StringBuilder();
            for (int i = 0; i < 400; i++)
            {
                string a = items[rng.Next(items.Length)];
                string b = items[rng.Next(items.Length)];
                int band = 10 + rng.Next(40) + (a == "alpha" ? 20 : 0);
                sb.Append(band.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(6 + rng.Next(2)).Append('|')
                  .Append(a).Append(',').Append(b).Append('\n');
            }
            File.WriteAllText(Path.Combine(root, CsvFile), sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Force the next <c>Recompute</c> for <paramref name="root"/> to go past the
        /// in-process guard, without exposing a reset just for tests: fitting a different root
        /// replaces the remembered input key.</summary>
        private void DropInProcessCache(string root)
        {
            ItemRegression.Recompute(root == _rootA ? _rootB : _rootA);
        }

        private static double BetaOf(string id)
        {
            Assert.That(ItemRegression.TryGetCoef(id, out double beta, out _), Is.True,
                "係数 '" + id + "' が無い");
            return beta;
        }

        [Test]
        public void FirstFit_WritesCache_AndSecondProcessReadsItBackExactly()
        {
            ItemRegression.Recompute(_rootA);
            double direct = BetaOf("alpha");
            int features = ItemRegression.FeatureCount;
            Assert.That(File.Exists(Path.Combine(_rootA, CacheFile)), Is.True,
                "fit 後にキャッシュが書かれていない");

            DropInProcessCache(_rootA);
            ItemRegression.Recompute(_rootA);

            // 往復書式 ("R") なので、 近い値ではなく **完全一致** でなければならない。
            // 少しでもずれるなら、 キャッシュ有無で BOT の判断が変わりうる。
            Assert.That(BetaOf("alpha"), Is.EqualTo(direct),
                "キャッシュ経由の係数が再計算と一致しない");
            Assert.That(ItemRegression.FeatureCount, Is.EqualTo(features));
            Assert.That(ItemRegression.LastSummary, Does.Contain("(cache)"),
                "ディスクキャッシュが使われていない");
        }

        [Test]
        public void InputChanged_InvalidatesCache()
        {
            ItemRegression.Recompute(_rootA);
            DropInProcessCache(_rootA);

            // 入力に 1 行足す = 長さも更新時刻も変わる → 指紋が変わる。
            File.AppendAllText(Path.Combine(_rootA, CsvFile), "99|7|alpha,beta\n",
                new UTF8Encoding(false));

            ItemRegression.Recompute(_rootA);
            Assert.That(ItemRegression.LastSummary, Does.Not.Contain("(cache)"),
                "入力が変わったのに古い係数を使っている");
        }

        [Test]
        public void TruncatedCache_IsRefused_NotSilentlyBelieved()
        {
            ItemRegression.Recompute(_rootA);
            double direct = BetaOf("alpha");
            int features = ItemRegression.FeatureCount;

            // 差し替えは原子的でないので、 行の途中まで書かれたファイルを読みうる。
            // それは「係数が少し足りない、 一見正しい表」であり、 黙って通ると BOT の
            // 判断だけが静かに変わる ── 一番見つけにくい壊れ方。
            string cachePath = Path.Combine(_rootA, CacheFile);
            string[] lines = File.ReadAllLines(cachePath, Encoding.UTF8);
            Assert.That(lines.Length, Is.GreaterThan(5), "キャッシュ行数が想定より少ない");
            File.WriteAllLines(cachePath, TakeAllButLast(lines), new UTF8Encoding(false));

            DropInProcessCache(_rootA);
            ItemRegression.Recompute(_rootA);

            Assert.That(ItemRegression.LastSummary, Does.Not.Contain("(cache)"),
                "切り詰められたキャッシュを信用してしまっている");
            Assert.That(ItemRegression.FeatureCount, Is.EqualTo(features),
                "係数の本数が欠けたまま採用されている");
            Assert.That(BetaOf("alpha"), Is.EqualTo(direct));
        }

        [Test]
        public void CorruptCache_FallsBackToFitting()
        {
            ItemRegression.Recompute(_rootA);
            int features = ItemRegression.FeatureCount;

            File.WriteAllText(Path.Combine(_rootA, CacheFile), "これはキャッシュではない",
                new UTF8Encoding(false));

            DropInProcessCache(_rootA);
            Assert.DoesNotThrow(() => ItemRegression.Recompute(_rootA));
            Assert.That(ItemRegression.FeatureCount, Is.EqualTo(features));
        }

        [Test]
        public void RepeatedFitForSameRoot_DoesNotRefit()
        {
            ItemRegression.Recompute(_rootA);
            int before = ItemRegression.CacheHits;
            ItemRegression.Recompute(_rootA);
            ItemRegression.Recompute(_rootA);
            Assert.That(ItemRegression.CacheHits, Is.EqualTo(before + 2),
                "同一入力に対してプロセス内キャッシュが効いていない");
        }

        private static string[] TakeAllButLast(string[] lines)
        {
            var kept = new string[lines.Length - 1];
            Array.Copy(lines, kept, kept.Length);
            return kept;
        }
    }
}
#endif

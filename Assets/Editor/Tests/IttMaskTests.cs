#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>品の知識を一部だけ持つ人 (AutoRunner.ittMaskPct・2026-11-23): 隠す品の選び方と、 隠した品の値。</summary>
    [TestFixture]
    public sealed class IttMaskTests
    {
        static readonly string[] Ids = { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l" };

        [Test]
        public void SameSeedSameMask_ZeroPctMasksNothing_RarityMeanElseMedian()
        {
            var first = new List<bool>(); int hidden = 0;
            foreach (var id in Ids) { bool m = LearnedPriorityProvider.IsMasked(id, 40, 1234UL, 7); first.Add(m); if (m) hidden++; }
            for (int i = 0; i < Ids.Length; i++)
            {
                Assert.That(LearnedPriorityProvider.IsMasked(Ids[i], 40, 1234UL, 7), Is.EqualTo(first[i]), "同じ種なら同じ品が隠れる");
                Assert.That(LearnedPriorityProvider.IsMasked(Ids[i], 0, 1234UL, 7), Is.False, "0% なら何も隠さない (序列は変わらない)");
                Assert.That(LearnedPriorityProvider.IsMasked(Ids[i], 100, 1234UL, 7), Is.True);
            }
            Assert.That(hidden, Is.InRange(1, Ids.Length - 1), "40% で全部 / 何も隠れないのはおかしい (12 品)");

            // 隠した品の値 = 同じレアリティの全品の平均・レアリティが取れなければ中央の値
            var table = new Dictionary<string, double> { { "a", 1.0 }, { "b", 3.0 }, { "c", 10.0 }, { "d", -2.0 } };
            System.Func<string, int?> rar = id => id == "a" || id == "b" ? 1 : id == "c" ? 2 : id == "d" ? 2 : (int?)null;
            Assert.That(LearnedPriorityProvider.MaskedValue(table, "a", rar), Is.EqualTo(2.0));
            Assert.That(LearnedPriorityProvider.MaskedValue(table, "c", rar), Is.EqualTo(4.0));
            Assert.That(LearnedPriorityProvider.MaskedValue(table, "zz", rar), Is.EqualTo(2.0), "レアリティが無い品は中央 (−2, 1, 3, 10 → 2)");
        }
    }
}
#endif

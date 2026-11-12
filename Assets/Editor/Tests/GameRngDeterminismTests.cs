#if UNITY_EDITOR
using GameLoop;
using NUnit.Framework;

namespace AutoTest.EditorTests.Ultra
{
    /// <summary>
    /// 本番乱数 (GameRng) のキー別の分離契約を固定する回帰テスト。
    /// もとは旧 Ultra (2026-11-13 削除・#135) のために書いたが、 同じシードの突き合わせ (BOT の測定・中断セーブ) が同じ契約に乗っている。
    /// (名前空間とキーの文字列 "ultra.*" はテストの中だけの名前なので残す)
    /// </summary>
    [TestFixture]
    public sealed class GameRngDeterminismTests
    {
        private const ulong Seed = 0x1020304050607080UL;
        private const int RunIndex = 37;

        [SetUp]
        public void SetUp()
        {
            Reset();
        }

        [TearDown]
        public void TearDown()
        {
            GameRng.ClearSeed();
        }

        [Test]
        public void SameSeedRunAndSchedule_ReplaysExactly()
        {
            int firstA = GameRng.RangeAuto("ultra.test.a", 0, 1_000_000);
            float firstB = GameRng.Value("ultra.test.b");
            int secondA = GameRng.RangeAuto("ultra.test.a", 0, 1_000_000);

            Reset();

            Assert.That(GameRng.RangeAuto("ultra.test.a", 0, 1_000_000), Is.EqualTo(firstA));
            Assert.That(GameRng.Value("ultra.test.b"), Is.EqualTo(firstB));
            Assert.That(GameRng.RangeAuto("ultra.test.a", 0, 1_000_000), Is.EqualTo(secondA));
        }

        [Test]
        public void DrawsFromOtherKeys_DoNotShiftTargetKey()
        {
            int expectedFirst = GameRng.RangeAuto("ultra.target", 0, int.MaxValue);
            int expectedSecond = GameRng.RangeAuto("ultra.target", 0, int.MaxValue);

            Reset();
            for (int i = 0; i < 100; i++)
                GameRng.RangeAuto("ultra.unrelated." + i, 0, int.MaxValue);

            Assert.That(GameRng.RangeAuto("ultra.target", 0, int.MaxValue), Is.EqualTo(expectedFirst));
            Assert.That(GameRng.RangeAuto("ultra.target", 0, int.MaxValue), Is.EqualTo(expectedSecond));
        }

        [Test]
        public void ExplicitIndex_DoesNotAdvanceAutomaticCounter()
        {
            int expectedAutomaticZero = GameRng.RangeAuto("ultra.indexed", 0, int.MaxValue);

            Reset();
            _ = GameRng.Range(0, int.MaxValue, "ultra.indexed", 999);
            _ = GameRng.Value("ultra.indexed", 12345);

            Assert.That(
                GameRng.RangeAuto("ultra.indexed", 0, int.MaxValue),
                Is.EqualTo(expectedAutomaticZero));
        }

        [Test]
        public void ExtraAutomaticDraw_ShiftsOnlyThatKeysLaterAutomaticIndex()
        {
            _ = GameRng.RangeAuto("ultra.same-key", 0, int.MaxValue);
            int expectedTargetAtIndexOne = GameRng.RangeAuto("ultra.same-key", 0, int.MaxValue);
            int expectedOther = GameRng.RangeAuto("ultra.other-key", 0, int.MaxValue);

            Reset();
            _ = GameRng.RangeAuto("ultra.same-key", 0, int.MaxValue);
            int shiftedTarget = GameRng.RangeAuto("ultra.same-key", 0, int.MaxValue);
            int unchangedOther = GameRng.RangeAuto("ultra.other-key", 0, int.MaxValue);

            Assert.That(shiftedTarget, Is.EqualTo(expectedTargetAtIndexOne));
            Assert.That(unchangedOther, Is.EqualTo(expectedOther));
        }

        private static void Reset()
        {
            GameRng.SetMasterSeed(Seed);
            GameRng.BeginRun(RunIndex);
        }
    }
}
#endif

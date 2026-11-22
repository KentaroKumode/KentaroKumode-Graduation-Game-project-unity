#if UNITY_EDITOR
using System.Collections.Generic;
using GameLoop;
using ItemSystem;
using ItemSystem.PassiveSkills;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>動的 4 枠ロックと軸均等の抽選 (ItemSystem.AxisPool・full-item-rework-spec §1) の契約。</summary>
    [TestFixture]
    public sealed class AxisPoolTests
    {
        [SetUp] public void SetUp() { GameRng.SetMasterSeed(0xA11CE5UL); GameRng.BeginRun(1); }
        [TearDown] public void TearDown() { GameRng.ClearSeed(); }

        /// <summary>軸ごとに品の数を変えた候補 (会心 30 品・役 3 品…) と枠外 10 品。</summary>
        private static List<CompleteItemData> Pool()
        {
            var l = new List<CompleteItemData>();
            int k = 0;
            foreach (var a in AxisRules.Ranked)
                for (int i = 0; i < 3 + 4 * k; i++) l.Add(new CompleteItemData { internalName = a + i, axis = a, category = ItemCategory.Passive });
            for (int i = 0; i < 10; i++) l.Add(new CompleteItemData { internalName = "out" + i, axis = i % 2 == 0 ? "" : AxisRules.Gold, category = ItemCategory.Passive });
            return l;
        }

        [Test]
        public void Lock_FillsFourThenStops()
        {
            var run = new RunState();
            foreach (var a in AxisRules.Ranked) AxisPool.NoteAcquired(run, a);
            AxisPool.NoteAcquired(run, AxisRules.Gold);
            Assert.That(run.lockedAxes, Is.EqualTo(new List<string>(AxisRules.Ranked).GetRange(0, 4)));
            Assert.That(AxisPool.IsLocked(run));
        }

        [Test]
        public void Narrow_EqualAxisShareRegardlessOfItemCount()
        {
            var run = new RunState();
            var pool = Pool();
            var n = new Dictionary<string, int>();
            const int draws = 16000;
            for (int i = 0; i < draws; i++)
            {
                var got = AxisPool.Narrow(run, pool, "test.axis");
                string a = AxisPool.IsRanked(got[0].axis) ? got[0].axis : AxisPool.Outside;
                foreach (var it in got) Assert.That(AxisPool.IsRanked(it.axis) ? it.axis : AxisPool.Outside, Is.EqualTo(a));   // 1 つの軸か枠外だけ
                n.TryGetValue(a, out int c); n[a] = c + 1;
            }
            Assert.That(n[AxisPool.Outside] / (float)draws, Is.EqualTo(1f - AxisPool.AxisShare).Within(0.02f));
            foreach (var a in AxisRules.Ranked) Assert.That(n[a] / (float)draws, Is.EqualTo(AxisPool.AxisShare / 8f).Within(0.015f), a);
        }

        [Test]
        public void Narrow_AfterLock_OnlyLockedAxesAndOutside()
        {
            var run = new RunState();
            run.lockedAxes = new List<string> { AxisRules.Role, AxisRules.Crit, AxisRules.Deck, AxisRules.Poison };
            var pool = Pool();
            for (int i = 0; i < 4000; i++)
                foreach (var it in AxisPool.Narrow(run, pool, "test.axis"))
                    Assert.That(!AxisPool.IsRanked(it.axis) || run.lockedAxes.Contains(it.axis), it.axis);
            Assert.That(AxisPool.AxisOpen(run, AxisRules.Bleed), Is.False);
            Assert.That(AxisPool.AxisOpen(run, AxisRules.Gold), Is.True);
        }

        [Test]
        public void Narrow_ForceFocus_ReturnsFocusAxis()
        {
            var run = new RunState();
            var got = AxisPool.Narrow(run, Pool(), "test.axis", AxisRules.Role, forceFocus: true);
            Assert.That(got.TrueForAll(it => it.axis == AxisRules.Role));
            var gold = AxisPool.Narrow(run, Pool(), "test.axis", AxisRules.Gold, forceFocus: true);
            Assert.That(gold.TrueForAll(it => it.axis == AxisRules.Gold));
        }

        [Test]
        public void SkipGold_Table()
        {
            Assert.That(AxisPool.SkipGold(ItemRarity.BRONZE), Is.EqualTo(15));
            Assert.That(AxisPool.SkipGold(ItemRarity.SILVER), Is.EqualTo(22));
            Assert.That(AxisPool.SkipGold(ItemRarity.GOLD), Is.EqualTo(35));
            Assert.That(AxisPool.SkipGold(ItemRarity.LEGENDARY), Is.EqualTo(50));
        }
    }
}
#endif

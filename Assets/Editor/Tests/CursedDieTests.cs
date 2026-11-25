#if UNITY_EDITOR
using System.Collections.Generic;
using GameLoop;
using ItemSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>〈呪物の賽〉の呪いの置き換え (2026-11-25・CombatSystem.DiePassives.MaybeCurse) の確率の元 (RarityWeightedPicker.LegendaryChance)。</summary>
    [TestFixture]
    public sealed class CursedDieTests
    {
        private static List<CompleteItemData> Pool(params ItemRarity[] rs)
        {
            var l = new List<CompleteItemData>();
            int k = 0;
            foreach (var r in rs) l.Add(new CompleteItemData { internalName = "p" + k++, rarity = r, category = ItemCategory.Passive });
            return l;
        }

        [Test]
        public void LegendaryChance_FromSourceWeights()
        {
            var all = Pool(ItemRarity.BRONZE, ItemRarity.SILVER, ItemRarity.GOLD, ItemRarity.LEGENDARY);
            Assert.That(RarityWeightedPicker.LegendaryChance(all), Is.EqualTo(0.01f).Within(1e-5f));                        // 報酬・宝箱・イベント
            Assert.That(RarityWeightedPicker.LegendaryChance(all, ItemRarity.GOLD), Is.EqualTo(0.01f / 0.11f).Within(1e-5f)); // ボス (GOLD 以上)
            var exBronze = Pool(ItemRarity.SILVER, ItemRarity.GOLD, ItemRarity.LEGENDARY);                                    // 交換マス (BRONZE を差し出す)
            Assert.That(RarityWeightedPicker.LegendaryChance(exBronze), Is.EqualTo(0.01f / 0.46f).Within(1e-5f));
            Assert.That(RarityWeightedPicker.LegendaryChance(Pool(ItemRarity.BRONZE, ItemRarity.GOLD)), Is.EqualTo(0f));      // LEGENDARY の無い並び
        }

        [Test]
        public void MaybeCurse_WithoutDie_KeepsPickAndClearsStaleMark()
        {
            var run = new RunState();
            run.cursedOfferIds.Add("p3");
            string got = CombatSystem.DiePassives.MaybeCurse(run, "p3", Pool(ItemRarity.LEGENDARY), 1f, "test");
            Assert.That(got, Is.EqualTo("p3"));
            Assert.That(CombatSystem.DiePassives.IsCursedOffer(run, "p3"), Is.False);   // 呪い無しで出し直した品は印が外れる
        }
    }
}
#endif

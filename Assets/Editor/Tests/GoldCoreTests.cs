#if UNITY_EDITOR
using GameLoop;
using ItemSystem.PassiveSkills.Effects;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>金の品のうち戦闘の外の式 (〈銭皿〉の見送り換金 +80%・〈戦果の天秤〉の報酬 +30% と強敵 +15G) の契約 (full-item-rework-ledger §⑨)。</summary>
    [TestFixture]
    public sealed class GoldCoreTests
    {
        [Test]
        public void CoinDishRoundsUpOnlyWhenOwned()
        {
            var run = new RunState();
            Assert.That(CoinDish.Apply(run, 35), Is.EqualTo(35));
            run.ownedPassiveItems.Add(ItemIds.GoldCoinDish);
            Assert.That(CoinDish.Apply(run, 35), Is.EqualTo(63));   // +80% (2026-11-22 neon 収束)
            Assert.That(CoinDish.Apply(run, 22), Is.EqualTo(40));   // 39.6 の切り上げ
            Assert.That(CoinDish.Apply(run, 0), Is.EqualTo(0));      // 秘宝の無い提示は 0 のまま
        }

        [Test]
        public void SpoilsScaleAddsPctAndStrongBonus()
        {
            var run = new RunState();
            Assert.That(SpoilsScale.Apply(run, 21, true), Is.EqualTo(21));
            run.ownedPassiveItems.Add(ItemIds.GoldScale);
            Assert.That(SpoilsScale.Apply(run, 21, false), Is.EqualTo(24));                          // 23.1 の切り上げ
            Assert.That(SpoilsScale.Apply(run, 21, true), Is.EqualTo(24 + SpoilsScale.StrongBonus));
            Assert.That(SpoilsScale.Apply(run, 0, false), Is.EqualTo(0));
        }
    }
}
#endif

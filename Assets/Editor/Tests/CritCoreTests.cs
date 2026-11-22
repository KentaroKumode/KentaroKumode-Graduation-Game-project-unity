#if UNITY_EDITOR
using CombatSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>会心の軸の式 (超過会心率の変換・〈寸鉄〉の 2 倍・〈天眼の鍔〉の確定会心 ×1.25・〈狙い目の刻み〉) の契約。</summary>
    [TestFixture]
    public sealed class CritCoreTests
    {
        [Test]
        public void Excess_ConvertsToMultiplier()
        {
            Assert.That(CritCore.ExcessMul(0.8f, false), Is.EqualTo(0f));
            Assert.That(CritCore.ExcessMul(1.3f, false), Is.EqualTo(0.3f).Within(1e-5f));    // 超過 30% → +0.30
            Assert.That(CritCore.ExcessMul(1.3f, true), Is.EqualTo(0.6f).Within(1e-5f));     // 〈寸鉄〉 +0.02 / 1%
            // 敵の会心耐性 (下限 1.0) → 超過 → 〈天眼の鍔〉×1.25 (会心率 100% 以上の時だけ)
            Assert.That(CritCore.EffectiveMul(2f, 0.5f, 1.2f, false, false), Is.EqualTo(1.7f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(2f, 0f, 1.2f, false, true), Is.EqualTo(2.75f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(2f, 0f, 0.9f, false, true), Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void Aim_FiveAndSix()
        {
            Assert.That(CritCore.AimRate(4), Is.EqualTo(0f));
            Assert.That(CritCore.AimRate(5), Is.EqualTo(CritCore.AimPct));
            Assert.That(CritCore.AimForce(5), Is.False);
            Assert.That(CritCore.AimForce(6), Is.True);
        }
    }
}
#endif

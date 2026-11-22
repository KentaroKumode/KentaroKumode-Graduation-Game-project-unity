#if UNITY_EDITOR
using CombatSystem;
using ItemSystem.PassiveSkills.Effects;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>会心の軸の式 (超過会心率の変換・〈寸鉄〉の 2 倍・〈天眼の鍔〉の確定会心 ×1.25・〈狙い目の刻み〉) と、 会心の品の契約 (2026-11-22 final5)。</summary>
    [TestFixture]
    public sealed class CritCoreTests
    {
        [Test]
        public void Excess_ConvertsToMultiplier()
        {
            Assert.That(CritCore.ExcessMul(0.8f, false), Is.EqualTo(0f));
            Assert.That(CritCore.ExcessMul(1.3f, false), Is.EqualTo(0.6f).Within(1e-5f));    // 超過 30% → +0.60 (1% ごとに +0.02・final5)
            Assert.That(CritCore.ExcessMul(1.3f, true), Is.EqualTo(1.2f).Within(1e-5f));     // 〈寸鉄〉 +0.04 / 1%
            Assert.That(CritCore.ExcessMul(1.5f, false), Is.EqualTo(1.0f).Within(1e-5f));    // critAdd 1.50 → +1.00
            Assert.That(CritCore.ExcessMul(1.5f, true), Is.EqualTo(2.0f).Within(1e-5f));     // 〈寸鉄〉 → +2.00
            // 敵の会心耐性 (下限 1.0) → 超過 → 〈天眼の鍔〉×1.25 (会心率 100% 以上の時だけ)
            Assert.That(CritCore.EffectiveMul(2f, 0.5f, 1.2f, false, false), Is.EqualTo(1.9f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(2f, 0f, 1.2f, false, true), Is.EqualTo(3.0f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(2f, 0f, 0.9f, false, true), Is.EqualTo(2f).Within(1e-5f));
        }

        /// <summary>基礎の会心倍率 1.6 (final5)。 〈回帰性真理〉(−0.5) を受けても 1.10 で、 1.0 に潰れない。</summary>
        [Test]
        public void BaseMultiplier_SurvivesRegression()
        {
            float m0 = MetaProgression.MetaBuffApplicator.GetCriticalMultiplier();
            Assert.That(m0, Is.EqualTo(1.6f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(m0, 0.5f, 0.3f, false, false), Is.EqualTo(1.1f).Within(1e-5f));
        }

        /// <summary>〈抜き打ちの作法〉の量: その撃の会心率 +50%。 素の会心率 10% なら超過 0 (×1.6)、 100% なら超過 +50% で +1.00。
        /// Execute は CombatManager.Instance を読むので、 撃での確かめはプレイモード (execute_code) で行う。</summary>
        [Test]
        public void QuickDraw_RateBecomesExcess()
        {
            float m0 = MetaProgression.MetaBuffApplicator.GetCriticalMultiplier();
            Assert.That(QuickDrawArt.Rate, Is.EqualTo(0.50f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(m0, 0f, 0.10f + QuickDrawArt.Rate, false, false), Is.EqualTo(1.6f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(m0, 0f, 1.0f + QuickDrawArt.Rate, false, false) - m0, Is.EqualTo(1.0f).Within(1e-5f));
        }
    }
}
#endif

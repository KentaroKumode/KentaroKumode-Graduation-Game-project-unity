#if UNITY_EDITOR
using CombatSystem;
using ItemSystem.PassiveSkills.Effects;
using NUnit.Framework;
using F = CombatSystem.CritCore.FocusItems;

namespace AutoTest.EditorTests
{
    /// <summary>会心の軸の式 (超過会心率の変換・〈寸鉄〉の 2 倍・〈狙い目の刻み〉・【集中】の入口と賭け金・【練気】の消費・〈死相の面頬〉) の契約
    /// (2026-11-23 会心の仕組みの転換・answer9 / answer10 の較正)。</summary>
    [TestFixture]
    public sealed class CritCoreTests
    {
        [Test]
        public void Excess_ConvertsToMultiplier()
        {
            Assert.That(CritCore.ExcessMul(0.8f, false), Is.EqualTo(0f));
            Assert.That(CritCore.ExcessMul(1.3f, false), Is.EqualTo(0.45f).Within(1e-5f));   // 超過 30% → +0.45 (1% ごとに +0.015)
            Assert.That(CritCore.ExcessMul(1.3f, true), Is.EqualTo(0.9f).Within(1e-5f));     // 〈寸鉄〉 +0.03 / 1%
            Assert.That(CritCore.EffectiveMul(2f, 0.5f, 1.2f, false), Is.EqualTo(1.8f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(2f, 0f, 1.2f, false), Is.EqualTo(2.3f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(2f, 0f, 0.9f, false), Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void Aim_FourFiveSix()
        {
            Assert.That(CritCore.AimRate(3), Is.EqualTo(0f));
            Assert.That(CritCore.AimRate(4), Is.EqualTo(0.10f).Within(1e-5f));
            Assert.That(CritCore.AimRate(5), Is.EqualTo(0.16f).Within(1e-5f));
            Assert.That(CritCore.AimRate(6), Is.EqualTo(0.26f).Within(1e-5f));
        }

        private static float C(F f, int k) => CritCore.FocusCost(f, k);

        /// <summary>answer10 §3(C) ②: 賭け金 = max(0, 25% ＋ Σ 品の賭け金 − Σ 相殺 × 会心の秘宝の数) の典型の表 (k = 6 / 8 / 10)。</summary>
        [Test]
        public void FocusCost_Table()
        {
            const F b3 = F.FirstArrow | F.FirstStrokePin | F.NarrowSpyglass;
            const F b2g1 = F.FirstArrow | F.FirstStrokePin | F.QuickDrawArt;
            const F b2g1l1 = F.FirstArrow | F.FirstStrokePin | F.QuickDrawArt | F.HeavenEyeGuard;
            const F b1g2l2 = F.FirstArrow | F.QuickDrawArt | F.OneFlashMastery | F.HeavenEyeGuard | F.SniperScope;
            foreach (int k in new[] { 6, 8, 10 }) Assert.That(C(b3, k), Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(C(b2g1, 6), Is.EqualTo(0.31f).Within(1e-4f));
            Assert.That(C(b2g1, 8), Is.EqualTo(0.27f).Within(1e-4f));
            Assert.That(C(b2g1, 10), Is.EqualTo(0.23f).Within(1e-4f));
            Assert.That(C(b2g1l1, 6), Is.EqualTo(0.37f).Within(1e-4f));
            Assert.That(C(b2g1l1, 8), Is.EqualTo(0.27f).Within(1e-4f));
            Assert.That(C(b2g1l1, 10), Is.EqualTo(0.17f).Within(1e-4f));
            Assert.That(C(b1g2l2, 6), Is.EqualTo(0.49f).Within(1e-4f));
            Assert.That(C(b1g2l2, 8), Is.EqualTo(0.29f).Within(1e-4f));
            Assert.That(C(b1g2l2, 10), Is.EqualTo(0.09f).Within(1e-4f));
            // SILVER の集中の品 (刃毀れ・渾身) は賭け金 0 (answer10)
            Assert.That(CritCore.RawFocusItemCost(F.NickAppraisal | F.HeftyPommel), Is.EqualTo(0f));
        }

        /// <summary>answer10 §3(C) ③: プールは 0 で止まり、 会心率の加点にならない。</summary>
        [Test]
        public void FocusCost_NeverNegative()
        {
            const F b1g2l2 = F.FirstArrow | F.QuickDrawArt | F.OneFlashMastery | F.HeavenEyeGuard | F.SniperScope;
            Assert.That(C(b1g2l2, 11), Is.EqualTo(0f));
            Assert.That(C(b1g2l2, 14), Is.EqualTo(0f));
            Assert.That(CritCore.FocusedRate(0.90f, true, false, b1g2l2, 14, 0f), Is.EqualTo(0.90f).Within(1e-5f));   // 加点されない
        }

        /// <summary>answer10 §3(C) ①: 武器 25%・会心の秘宝 3 つ (k = 3) では、 どの集中の品 3 つの組み合わせでも集中にならない (〈抜き打ちの作法〉の 1 ターン目 +12% を足しても)。
        /// ほかの入口: 集中の品 2 つ・攻撃の端子 2 枚・照準器のもう 1 回では集中にならない。</summary>
        [Test]
        public void Focus_Entrance()
        {
            var all = new[] { F.FirstArrow, F.FirstStrokePin, F.NarrowSpyglass, F.NickAppraisal, F.HeftyPommel, F.QuickDrawArt, F.OneFlashMastery, F.HeavenEyeGuard, F.SniperScope };
            for (int a = 0; a < all.Length; a++)
                for (int b = a + 1; b < all.Length; b++)
                    for (int c = b + 1; c < all.Length; c++)
                    {
                        var f = all[a] | all[b] | all[c];
                        float bonus = (f & F.QuickDrawArt) != 0 ? QuickDrawArt.FocusBonus : 0f;
                        Assert.That(CritCore.FocusedRate(0.25f, true, false, f, 3, bonus), Is.EqualTo(-1f), f.ToString());
                    }
            const F b3 = F.FirstArrow | F.FirstStrokePin | F.NarrowSpyglass;
            Assert.That(CritCore.FocusedRate(0.60f, true, false, F.FirstArrow | F.FirstStrokePin, 5, 0f), Is.EqualTo(-1f));   // 2 品
            Assert.That(CritCore.FocusedRate(0.60f, false, false, b3, 5, 0f), Is.EqualTo(-1f));                               // 攻撃の端子 2 枚
            Assert.That(CritCore.FocusedRate(0.60f, true, true, b3, 5, 0f), Is.EqualTo(-1f));                                 // 照準器のもう 1 回
            Assert.That(CritCore.FocusedRate(0.95f, true, false, F.FirstArrow | F.FirstStrokePin | F.QuickDrawArt, 7, 0f), Is.EqualTo(0.66f).Within(1e-4f));
        }

        [Test]
        public void RenkiSpend_HalfRoundUp()
        {
            Assert.That(CritCore.RenkiSpend(0), Is.EqualTo(0));
            Assert.That(CritCore.RenkiSpend(1), Is.EqualTo(1));
            Assert.That(CritCore.RenkiSpend(4), Is.EqualTo(2));
            Assert.That(CritCore.RenkiSpend(5), Is.EqualTo(3));
            Assert.That(CritCore.RenkiSpend(12), Is.EqualTo(6));
            Assert.That(CritCore.SniperMulAdd(3), Is.EqualTo(1.70f).Within(1e-5f));   // 0.80 + 0.30 × 3
            Assert.That(CritCore.RenkiMulPer, Is.EqualTo(0.06f).Within(1e-6f));
        }

        /// <summary>〈死相の面頬〉: 練気 ÷ (練気 + 8)・大技のターンは練気を 2 倍で数える・上限なし (answer9)。</summary>
        [Test]
        public void DeathMask_Cut()
        {
            Assert.That(DeathMaskVisor.Cut(0, false), Is.EqualTo(0f));
            Assert.That(DeathMaskVisor.Cut(4, false), Is.EqualTo(4f / 12f).Within(1e-5f));
            Assert.That(DeathMaskVisor.Cut(4, true), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(DeathMaskVisor.Cut(8, false), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(DeathMaskVisor.PierceRenki, Is.EqualTo(3));
        }

        /// <summary>基礎の会心倍率 2.0。 〈回帰性真理〉(−0.5) を受けて 1.50。</summary>
        [Test]
        public void BaseMultiplier_SurvivesRegression()
        {
            float m0 = MetaProgression.MetaBuffApplicator.GetCriticalMultiplier();
            Assert.That(m0, Is.EqualTo(2.0f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(m0, 0.5f, 0.3f, false), Is.EqualTo(1.5f).Within(1e-5f));
        }
    }
}
#endif

#if UNITY_EDITOR
using CombatSystem;
using ItemSystem.PassiveSkills.Effects;
using NUnit.Framework;
using F = CombatSystem.CritCore.FocusItems;

namespace AutoTest.EditorTests
{
    /// <summary>会心の軸の式 (超過会心率の変換・〈寸鉄〉の 2 倍・〈狙い目の刻み〉・【集中】の入口と賭け金・【練気】の消費・〈死相の面頬〉) の契約 (2026-11-23 会心の仕組みの転換)。</summary>
    [TestFixture]
    public sealed class CritCoreTests
    {
        [Test]
        public void Excess_ConvertsToMultiplier()
        {
            Assert.That(CritCore.ExcessMul(0.8f, false), Is.EqualTo(0f));
            Assert.That(CritCore.ExcessMul(1.3f, false), Is.EqualTo(0.3f).Within(1e-5f));    // 超過 30% → +0.30 (1% ごとに +0.01)
            Assert.That(CritCore.ExcessMul(1.3f, true), Is.EqualTo(0.6f).Within(1e-5f));     // 〈寸鉄〉 +0.02 / 1%
            // 敵の会心耐性 (下限 1.0) → 超過。 確定会心 ×1.25 (旧〈天眼の鍔〉) は無い
            Assert.That(CritCore.EffectiveMul(2f, 0.5f, 1.2f, false), Is.EqualTo(1.7f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(2f, 0f, 1.2f, false), Is.EqualTo(2.2f).Within(1e-5f));
            Assert.That(CritCore.EffectiveMul(2f, 0f, 0.9f, false), Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void Aim_FiveAndSix()
        {
            Assert.That(CritCore.AimRate(4), Is.EqualTo(0f));
            Assert.That(CritCore.AimRate(5), Is.EqualTo(0.12f).Within(1e-5f));
            Assert.That(CritCore.AimRate(6), Is.EqualTo(0.24f).Within(1e-5f));
        }

        /// <summary>賭け金 = (25% ＋ 品の賭け金) ÷ (1 ＋ 0.2 × max(0, 会心の品の数 − 3))。 指示の表 (B3 / B2＋S1＋G1＋L1) と同じ値。</summary>
        [Test]
        public void FocusCost_Table()
        {
            const F b3 = F.FirstArrow | F.FirstStrokePin | F.NarrowSpyglass;
            const F five = F.FirstArrow | F.FirstStrokePin | F.NickAppraisal | F.QuickDrawArt | F.HeavenEyeGuard;   // B2 + S1 + G1 + L1
            Assert.That(CritCore.RawFocusItemCost(b3), Is.EqualTo(0f));
            Assert.That(CritCore.RawFocusItemCost(five), Is.EqualTo(0.56f).Within(1e-5f));
            float C(F f, int k) => CritCore.FocusCost(CritCore.RawFocusItemCost(f), k);
            Assert.That(C(b3, 3), Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(C(b3, 4), Is.EqualTo(0.2083f).Within(1e-4f));
            Assert.That(C(b3, 6), Is.EqualTo(0.15625f).Within(1e-4f));
            Assert.That(C(b3, 10), Is.EqualTo(0.10417f).Within(1e-4f));
            Assert.That(C(b3, 14), Is.EqualTo(0.078125f).Within(1e-4f));
            Assert.That(C(five, 6), Is.EqualTo(0.50625f).Within(1e-4f));
            Assert.That(C(five, 10), Is.EqualTo(0.3375f).Within(1e-4f));
            Assert.That(C(five, 14), Is.EqualTo(0.253125f).Within(1e-4f));
        }

        /// <summary>【集中】の入口: 集中の品 3 つ以上・攻撃の端子 1 枚の撃・照準器のもう 1 回でない・集中の会心率 1% 以上。 満たさなければ −1 (賭け金も効果も働かない)。</summary>
        [Test]
        public void Focus_Entrance()
        {
            const F b3 = F.FirstArrow | F.FirstStrokePin | F.NarrowSpyglass;
            const F b2 = F.FirstArrow | F.FirstStrokePin;
            Assert.That(CritCore.FocusedRate(0.60f, true, false, b2, 5, 0f), Is.EqualTo(-1f));       // 2 品
            Assert.That(CritCore.FocusedRate(0.60f, false, false, b3, 5, 0f), Is.EqualTo(-1f));      // 攻撃の端子 2 枚
            Assert.That(CritCore.FocusedRate(0.60f, true, true, b3, 5, 0f), Is.EqualTo(-1f));        // 照準器のもう 1 回
            Assert.That(CritCore.FocusedRate(0.50f, true, false, b3, 4, 0f), Is.EqualTo(0.2917f).Within(1e-4f));
            // 他軸の T4 武器 25% と集中の品 3 つ (会心の品 3 つ): どの組み合わせでも 0% 以下で不成立
            foreach (var f in new[] { b3, F.FirstArrow | F.FirstStrokePin | F.NickAppraisal, F.FirstArrow | F.FirstStrokePin | F.QuickDrawArt,
                                      F.QuickDrawArt | F.OneFlashMastery | F.HeavenEyeGuard, F.QuickDrawArt | F.HeavenEyeGuard | F.SniperScope })
                Assert.That(CritCore.FocusedRate(0.25f, true, false, f, 3, 0f), Is.EqualTo(-1f));
            // 〈抜き打ちの作法〉の 1 ターン目の +20% を足しても B2 + 抜き打ちは 0% で不成立
            Assert.That(CritCore.FocusedRate(0.25f, true, false, F.FirstArrow | F.FirstStrokePin | F.QuickDrawArt, 3, QuickDrawArt.FocusBonus), Is.EqualTo(-1f));
        }

        [Test]
        public void RenkiSpend_HalfRoundUp()
        {
            Assert.That(CritCore.RenkiSpend(0), Is.EqualTo(0));
            Assert.That(CritCore.RenkiSpend(1), Is.EqualTo(1));
            Assert.That(CritCore.RenkiSpend(4), Is.EqualTo(2));
            Assert.That(CritCore.RenkiSpend(5), Is.EqualTo(3));
            Assert.That(CritCore.RenkiSpend(12), Is.EqualTo(6));
            Assert.That(CritCore.SniperMulAdd(3), Is.EqualTo(1.25f).Within(1e-5f));
        }

        /// <summary>〈死相の面頬〉: 練気 ÷ (練気 + 12)・大技のターンは練気を 2 倍で数える・上限なし。</summary>
        [Test]
        public void DeathMask_Cut()
        {
            Assert.That(DeathMaskVisor.Cut(0, false), Is.EqualTo(0f));
            Assert.That(DeathMaskVisor.Cut(4, false), Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(DeathMaskVisor.Cut(4, true), Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(DeathMaskVisor.Cut(12, false), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(DeathMaskVisor.Cut(120, true), Is.EqualTo(240f / 252f).Within(1e-5f));
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

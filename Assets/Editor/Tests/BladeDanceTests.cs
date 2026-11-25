#if UNITY_EDITOR
using ItemSystem.PassiveSkills;
using ItemSystem.PassiveSkills.Effects;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>〈無銘の剣舞譜〉の剣先 (2026-11-25 作者の決定): 取得時 層 × 15・開戦と当たった撃ごとに +1・上限なし・中断セーブで戻る。</summary>
    [TestFixture]
    public sealed class BladeDanceTests
    {
        [Test]
        public void Kensaki_SeedGrowNoCap()
        {
            var bd = new BladeDance();
            bd.SeedOnAcquire(5);
            Assert.That(bd.Kensaki, Is.EqualTo(5 * BladeDance.SeedPerFloor));   // 75

            var ctx = new CombatContext(100) { playerCurrentHP = 50 };
            bd.Execute(PassiveSkillTrigger.OnBattleStart, ctx);
            Assert.That(bd.Kensaki, Is.EqualTo(76));

            // 本物の撃が当たるたび +1 (HP は剣先分戻る)
            ctx.playerWonRoll = true; ctx.hitCount = 2; ctx.hitIndex = 0; ctx.finalDamage = 10;
            bd.Execute(PassiveSkillTrigger.OnPostDealDamage, ctx);
            Assert.That(bd.Kensaki, Is.EqualTo(77));
            Assert.That(ctx.playerCurrentHP, Is.EqualTo(100));
            // 当たらなかった撃・攻撃しないターンの空の 1 回は数えない
            ctx.finalDamage = 0;
            bd.Execute(PassiveSkillTrigger.OnPostDealDamage, ctx);
            ctx.finalDamage = 10; ctx.hitCount = 0;
            bd.Execute(PassiveSkillTrigger.OnPostDealDamage, ctx);
            Assert.That(bd.Kensaki, Is.EqualTo(77));

            // 上限なし (旧 99)・中断セーブで戻る
            Assert.That(bd.RestoreRunState("250"), Is.True);
            bd.Execute(PassiveSkillTrigger.OnBattleStart, ctx);
            Assert.That(bd.Kensaki, Is.EqualTo(251));
            Assert.That(bd.CaptureRunState(), Is.EqualTo("251"));
        }
    }
}
#endif

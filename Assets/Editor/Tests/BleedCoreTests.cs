#if UNITY_EDITOR
using System.Collections.Generic;
using CombatSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>出血の軸の式 (撃ごとの出血・攻撃 +N = TerminalCards.Resolve の 5f / 〈百人切りの鉈〉の処刑の線) の契約。</summary>
    [TestFixture]
    public sealed class BleedCoreTests
    {
        private static readonly List<string> Board = new List<string> { TerminalIds.Attack, TerminalIds.Attack, TerminalIds.Block };

        private static TerminalOutcome Run(BleedItems items, int[] dice, int[] slots, int enemyBleed = 0, int enemyHp = 100)
        {
            var st = new TerminalBattleState { bleedItems = items };
            var env = new TerminalEnv { charge = 0, chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 10, enemyBleed = enemyBleed, enemyHp = enemyHp, enemyMaxHp = 100 };
            return TerminalCards.Resolve(Board, slots, dice, env, st);
        }

        [Test]
        public void Terminals_BleedPerHit()
        {
            // 攻撃 2 枚 (目 5 と 2)・防御 1: 握斧は 4 以上の撃だけ・楔は 1〜3 の撃だけ・留め金は攻撃 2 枚で両方・ナイフは両方・針箱は 2 撃目 +1
            var o = Run(BleedItems.LeadAxe | BleedItems.Rift | BleedItems.Clasp | BleedItems.Chipped | BleedItems.Needles, new[] { 5, 2, 3 }, new[] { 0, 1, 2 });
            Assert.That(o.attackHitBleed.Count, Is.EqualTo(2));
            Assert.That(o.attackHitBleed[0], Is.EqualTo(BleedCore.LeadAxeBleed + BleedCore.ClaspBleed + BleedCore.ChippedBleed));
            Assert.That(o.attackHitBleed[1], Is.EqualTo(BleedCore.RiftBleed + BleedCore.ClaspBleed + BleedCore.ChippedBleed + BleedCore.NeedlesBleed));
            // 攻撃 1 枚なら留め金は鳴らない
            Assert.That(Run(BleedItems.Clasp, new[] { 5, 2, 3 }, new[] { 0, -1, 2 }).attackHitBleed[0], Is.EqualTo(0));
            // 山刀は敵の HP で量が変わる (配線の時点)
            Assert.That(Run(BleedItems.Saw, new[] { 5, 2, 3 }, new[] { 0, -1, 2 }, 0, 20).attackHitBleed[0], Is.EqualTo(BleedCore.SawLow));
        }

        [Test]
        public void Terminals_AttackAddsAndCleaverLine()
        {
            // 佩玉 (出血 6 以上で最初の撃 +3)・開き針 (出血 10 につき撃ごと +2): 出血 20 なら 1 撃目 +3+4・2 撃目 +4
            var baseO = Run(BleedItems.None, new[] { 5, 2, 3 }, new[] { 0, 1, 2 }, 20);
            var o = Run(BleedItems.Pendant | BleedItems.OpenNeedle, new[] { 5, 2, 3 }, new[] { 0, 1, 2 }, 20);
            Assert.That(o.attackHits[0] - baseO.attackHits[0], Is.EqualTo(BleedCore.PendantAtk + 2 * BleedCore.OpenNeedleAtk));
            Assert.That(o.attackHits[1] - baseO.attackHits[1], Is.EqualTo(2 * BleedCore.OpenNeedleAtk));
            Assert.That(o.attackAdd - baseO.attackAdd, Is.EqualTo(BleedCore.PendantAtk + 4 * BleedCore.OpenNeedleAtk));
            // 鉈: 出血 12 未満は −1・出血 20 で 24%・ボスは半分・上限 25%
            Assert.That(BleedCore.CleaverKillLine(11, 1000, false), Is.EqualTo(-1));
            Assert.That(BleedCore.CleaverKillLine(20, 1000, false), Is.EqualTo(240));
            Assert.That(BleedCore.CleaverKillLine(20, 1000, true), Is.EqualTo(120));
            Assert.That(BleedCore.CleaverKillLine(40, 1000, false), Is.EqualTo(250));
            Assert.That(BleedCore.CrystalGain(3, BleedCore.CrystalNeed, true), Is.EqualTo(6));
            Assert.That(BleedCore.CrystalGain(3, BleedCore.CrystalNeed - 1, true), Is.EqualTo(3));
            Assert.That(BleedCore.SakuraPct(12), Is.EqualTo(24));
            Assert.That(BleedCore.SakuraPct(80), Is.EqualTo(BleedCore.SakuraMaxPct));
            Assert.That(BleedCore.PressSpend(16), Is.EqualTo(6));
        }
    }
}
#endif

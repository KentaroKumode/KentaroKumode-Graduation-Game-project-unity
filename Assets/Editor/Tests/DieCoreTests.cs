#if UNITY_EDITOR
using System.Collections.Generic;
using CombatSystem;
using GameLoop;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>装備の賽の調整 (full-item-rework-ledger §⑪) の契約。 賽は 1 個を 3 回振る (ADR-0015)。</summary>
    [TestFixture]
    public sealed class DieCoreTests
    {
        private static TerminalOutcome Run(string die, int[] dice)
        {
            var board = new List<string> { TerminalIds.Attack, TerminalIds.Block, TerminalIds.Attack };
            var env = new TerminalEnv { chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 10, enemyHp = 100, enemyMaxHp = 100, dieId = die };
            return TerminalCards.Resolve(board, new[] { 0, 1, 2 }, dice, env, new TerminalBattleState());
        }

        [Test]
        public void DullAddsTwoWhenParityMatches()
        {
            int[] dice = { 3, 4, 2 };   // 奇数を攻撃・偶数を防御・偶数を攻撃 (合わない)
            var b = Run(null, dice);
            var o = Run(DieIds.Dull, dice);
            Assert.That(o.attackAdd - b.attackAdd, Is.EqualTo(DiePassives.DullBonus));
            Assert.That(o.blockAdd - b.blockAdd, Is.EqualTo(DiePassives.DullBonus));
            Assert.That(DiePassives.DullBonus, Is.EqualTo(2));
        }

        [Test]
        public void LedgerNumbers()
        {
            Assert.That(DiePassives.ScavengerGold, Is.EqualTo(2));
            Assert.That(DiePassives.OldCoinGold, Is.EqualTo(4));
            Assert.That(DiePassives.OldCoinHeal, Is.EqualTo(3));
            Assert.That(DiePassives.RoleHealBonus(null), Is.EqualTo(0));
            var rusty = ItemSystem.ItemDatabase.Instance?.GetItem(DieIds.RustyNail);
            if (rusty != null) Assert.That(rusty.diceFaces, Is.EqualTo(new[] { 1, 2, 4, 5, 6 }));
        }
    }
}
#endif

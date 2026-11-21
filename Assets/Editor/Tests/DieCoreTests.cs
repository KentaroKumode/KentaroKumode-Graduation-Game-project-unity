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
            Assert.That(DiePassives.OldCoinGold, Is.EqualTo(2));
            Assert.That(DiePassives.OldCoinHeal, Is.EqualTo(1));
            Assert.That(DiePassives.RoleHealBonus(null), Is.EqualTo(0));
            var rusty = ItemSystem.ItemDatabase.Instance?.GetItem(DieIds.RustyNail);
            if (rusty != null) Assert.That(rusty.diceFaces, Is.EqualTo(new[] { 1, 2, 4, 5, 6 }));
        }

        /// <summary>BOT の賽の見積もり (InventoryPower.DieRaw) は賽のパッシブを数える (Phase 3i)。 効果なしの賽は 0、 面だけ同じ賽はパッシブの分だけ高い。</summary>
        [Test]
        public void BotDieValueCountsPassives()
        {
            int[] six = { 1, 2, 3, 4, 5, 6 };
            Assert.That(AutoTest.InventoryPower.DiePassiveRaw(DieIds.Starting, new[] { 2, 3, 4, 5 }), Is.EqualTo(0f));
            Assert.That(AutoTest.InventoryPower.DiePassiveRaw(DieIds.Dull, six), Is.GreaterThan(0f));
            Assert.That(AutoTest.InventoryPower.DiePassiveRaw(DieIds.Scale, six), Is.EqualTo(120f / 216f * DiePassives.ScaleBonus).Within(1e-4));
            Assert.That(AutoTest.InventoryPower.DiePassiveRaw(DieIds.Reverse, six), Is.EqualTo(3 * AutoTest.InventoryPower.AttackShare * 1.5f).Within(1e-4));
        }
    }
}
#endif

#if UNITY_EDITOR
using System.Collections.Generic;
using CombatSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>充電のコアアクション (【再起動】【オーバークロック】・ChargeCore / TerminalCards.Resolve の 5c) の契約。</summary>
    [TestFixture]
    public sealed class ChargeCoreTests
    {
        private static readonly List<string> Board = new List<string> { TerminalIds.Attack, TerminalIds.Block };

        private static TerminalOutcome Run(ChargeItems items, int charge, int reboot = ChargeCore.RebootNone, bool overclock = false, int cut = 0, int spendsBefore = 0)
        {
            var st = new TerminalBattleState { chargeItems = items };
            var env = new TerminalEnv { charge = charge, chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 0, chargeCostCut = cut, chargeSpendsTurn = spendsBefore };
            return TerminalCards.Resolve(Board, new[] { 0, 1 }, new[] { 5, 3 }, env, st, null, false, reboot, overclock);
        }

        [Test]
        public void Reboot_AddsHitAndPaysOnce()
        {
            var o = Run(ChargeItems.Fork, 10, reboot: 0);
            Assert.That(o.rebootSlot, Is.EqualTo(0));
            Assert.That(o.attackHits, Is.EqualTo(new List<int> { 5, 5 }));      // 撃が 1 つ増える (同じ量)
            Assert.That(o.attackDice, Is.EqualTo(1));                            // ダイスは増えない
            Assert.That(o.chargePays, Is.EqualTo(new List<int> { ChargeCore.RebootCost }));
        }

        [Test]
        public void Reboot_NeedsChargeAndItem()
        {
            Assert.That(Run(ChargeItems.Fork, ChargeCore.RebootCost - 1, reboot: 0).rebootSlot, Is.EqualTo(-1));
            Assert.That(Run(ChargeItems.None, 30, reboot: 0).rebootSlot, Is.EqualTo(-1));
            Assert.That(Run(ChargeItems.Fork, 30, reboot: ChargeCore.RebootNone).chargeSpend, Is.EqualTo(0));
        }

        [Test]
        public void Reboot_AutoPicksAttackWhenNoEnemyAttack_AndTunerCuts()
        {
            var o = Run(ChargeItems.Fork, 10, reboot: ChargeCore.RebootAuto, cut: ChargeCore.TunerCut);
            Assert.That(o.rebootSlot, Is.EqualTo(0));
            Assert.That(o.chargeSpend, Is.EqualTo(ChargeCore.RebootCost - ChargeCore.TunerCut));
        }

        [Test]
        public void Overclock_NeedsFifteenAndPaysTwelve()
        {
            var o = Run(ChargeItems.Drum, ChargeCore.OverclockNeed, overclock: true);
            Assert.That(o.overclock, Is.True);
            Assert.That(o.chargeSpend, Is.EqualTo(ChargeCore.OverclockCost));
            Assert.That(Run(ChargeItems.Drum, ChargeCore.OverclockNeed - 1, overclock: true).overclock, Is.False);
        }

        [Test]
        public void Whip_FreeRebootAfterTwoSpends()
        {
            var o = Run(ChargeItems.Whip, 0, spendsBefore: 2);
            Assert.That(o.rebooted, Is.EqualTo(new List<int> { 0 }));
            Assert.That(o.chargeSpend, Is.EqualTo(0));
            Assert.That(Run(ChargeItems.Whip, 0, spendsBefore: 1).rebooted, Is.Empty);
        }
    }
}
#endif

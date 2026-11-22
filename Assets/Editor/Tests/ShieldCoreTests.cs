#if UNITY_EDITOR
using System.Collections.Generic;
using CombatSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>シールドの軸の式 (【ジャスト防御】の判定・ShieldCore / TerminalCards.Resolve の 5e・目盛りを直接割る / ひびを入れる) の契約。</summary>
    [TestFixture]
    public sealed class ShieldCoreTests
    {
        private static readonly List<string> Board = new List<string> { TerminalIds.Block, TerminalIds.Block, TerminalIds.Block };

        private static TerminalOutcome Run(ShieldItems items, int[] slots, int enemyAttack = 10)
        {
            var st = new TerminalBattleState { shieldItems = items };
            var env = new TerminalEnv { chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = enemyAttack };
            return TerminalCards.Resolve(Board, slots, new[] { 3, 3, 3 }, env, st);
        }

        [Test]
        public void JustGuard_WindowIsAttackToAttackPlusWidth()
        {
            int w = ShieldCore.Width(ShieldItems.None);
            Assert.That(w, Is.EqualTo(2));
            Assert.That(ShieldCore.Width(ShieldItems.Worn), Is.EqualTo(4));                 // 〈擦り減った小盾〉+2
            Assert.That(ShieldCore.IsJustGuard(10, 9, w), Is.False);                        // 足りない (HP を失う)
            Assert.That(ShieldCore.IsJustGuard(10, 10, w), Is.True);                        // ちょうど
            Assert.That(ShieldCore.IsJustGuard(10, 12, w), Is.True);                        // +2 まで
            Assert.That(ShieldCore.IsJustGuard(10, 13, w), Is.False);                       // 余りすぎ
            Assert.That(ShieldCore.IsJustGuard(10, 14, ShieldCore.Width(ShieldItems.Worn)), Is.True);
            Assert.That(ShieldCore.IsJustGuard(0, 0, w), Is.False);                         // 敵が攻撃しないターンは対象外
            Assert.That(ShieldCore.IsJustGuard(0, 2, w), Is.False);
        }

        [Test]
        public void Terminals_ShieldItemsAddToOutcome()
        {
            var all3 = new[] { 0, 1, 2 };
            Assert.That(Run(ShieldItems.None, all3).shield, Is.EqualTo(0));
            Assert.That(Run(ShieldItems.Pauldron, all3).shield, Is.EqualTo(ShieldCore.PauldronShield));          // 3 個とも防御
            Assert.That(Run(ShieldItems.Pauldron, new[] { 0, 1, -1 }).shield, Is.EqualTo(0));
            Assert.That(Run(ShieldItems.Padded, new[] { 0, 1, -1 }).shield, Is.EqualTo(ShieldCore.PaddedShield));  // 2 個以上
            Assert.That(Run(ShieldItems.Padded, new[] { 0, -1, -1 }).shield, Is.EqualTo(0));
            Assert.That(Run(ShieldItems.Worn, new[] { 0, 1, -1 }).shield, Is.EqualTo(2 * ShieldCore.WornPerDie));
            Assert.That(Run(ShieldItems.Rivet, new[] { -1, -1, -1 }, ShieldCore.RivetNeed).shield, Is.EqualTo(ShieldCore.RivetHigh));
            Assert.That(Run(ShieldItems.Rivet, new[] { -1, -1, -1 }, ShieldCore.RivetNeed - 1).shield, Is.EqualTo(ShieldCore.RivetLow));
            // 見込みのシールド = 今のシールド + 端子の分
            var env = new TerminalEnv { shield = 5, hp = 50, maxHp = 50 };
            Assert.That(ShieldCore.ShieldAfterWiring(Run(ShieldItems.Padded, new[] { 0, 1, -1 }), in env), Is.EqualTo(5 + ShieldCore.PaddedShield));
        }

        [Test]
        public void BossNotch_BreakAndCrackTop()
        {
            var d = new BossDownState();
            d.Reset(new[] { 0.5f, 0.25f });
            Assert.That(d.CrackTopNotch(3), Is.True);
            Assert.That(d.CrackTopNotch(4), Is.False);       // 既にひびがある
            Assert.That(d.TopCracked, Is.True);
            Assert.That(d.BreakTopNotch(), Is.True);          // HP に関係なく割る (満額のダウン)
            Assert.That(d.broken, Is.EqualTo(1));
            Assert.That(d.pending, Is.EqualTo(1));
            Assert.That(d.TopCracked, Is.False);
        }
    }
}
#endif

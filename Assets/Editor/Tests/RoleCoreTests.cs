#if UNITY_EDITOR
using System.Collections.Generic;
using CombatSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>役の軸の品 (Resolve の 6d・〈万象のゾロ目〉の確率) の契約 (full-item-rework-ledger §⑧)。</summary>
    [TestFixture]
    public sealed class RoleCoreTests
    {
        private static TerminalOutcome Run(RoleItems f, int[] dice, int roleCount = 0, int streak = 0, bool rerolled = false)
        {
            var board = new List<string> { TerminalIds.Attack, TerminalIds.Attack, TerminalIds.Block };
            var st = new TerminalBattleState { roleItems = f, roleCount = roleCount, roleStreak = streak };
            var env = new TerminalEnv { chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 10, enemyHp = 100, enemyMaxHp = 100, diceRerolled = rerolled };
            return TerminalCards.Resolve(board, new[] { 0, 1, 2 }, dice, env, st);
        }

        [Test]
        public void PairHitsBlockPenAndCount()
        {
            int[] pair = { 2, 2, 5 };
            var b = Run(RoleItems.None, pair);
            Assert.That(b.roleNow, Is.EqualTo(1));
            var o = Run(RoleItems.WoodTag | RoleItems.Gear | RoleItems.NotchBoard | RoleItems.Chipped, pair, roleCount: 3, rerolled: true);
            // 撃ごとに 木札 4 + 歯車 6 + 刻み板 min(16, 2 × (3 + 1)) = 18・攻撃の撃は 2 つ
            int each = TerminalCards.WoodTagAttack + TerminalCards.GearAttack + TerminalCards.NotchBoardPer * 4;
            Assert.That(o.attackAdd - b.attackAdd, Is.EqualTo(each * 2));
            Assert.That(o.blockAdd - b.blockAdd, Is.EqualTo(TerminalCards.WoodTagBlock));
            Assert.That(o.armorPen - b.armorPen, Is.EqualTo(TerminalCards.ChippedPen));      // 2 の 2 個そろい = 低い目
            // 振り直していなければ歯車なし・刻み板は 16 で頭打ち・役が無ければ何もしない
            var o2 = Run(RoleItems.Gear | RoleItems.NotchBoard, pair, roleCount: 20);
            Assert.That(o2.attackAdd - b.attackAdd, Is.EqualTo(TerminalCards.NotchBoardMax * 2));
            Assert.That(Run(RoleItems.WoodTag, new[] { 1, 3, 5 }).attackAdd, Is.EqualTo(Run(RoleItems.None, new[] { 1, 3, 5 }).attackAdd));
            // 高い目の 2 個そろいは〈欠け目の見立て〉の対象外
            Assert.That(Run(RoleItems.Chipped, new[] { 5, 5, 2 }).armorPen, Is.EqualTo(0));
        }

        [Test]
        public void MultipliersAndOmniChance()
        {
            int[] triple = { 4, 4, 4 };
            var b = Run(RoleItems.None, triple);
            Assert.That(b.roleNow, Is.EqualTo(2));
            var t = Run(RoleItems.Thunder, triple);
            Assert.That(t.damageMul, Is.EqualTo(b.damageMul * (1f + TerminalCards.ThunderFinalPct)).Within(1e-4));
            Assert.That(t.blockAdd - b.blockAdd, Is.EqualTo(TerminalCards.ThunderBlock));
            // 血濡れの符丁: 回数 (1 + 1) × 10% + 〈攻め〉(目 4) の 30%
            var m = Run(RoleItems.Bloodmark, triple, roleCount: 1);
            Assert.That(m.damageMul, Is.EqualTo(b.damageMul * (1f + 2 * TerminalCards.BloodmarkPer + TerminalCards.BloodmarkBonus)).Within(1e-4));
            // 拍子木: 前のターンに役が立っていれば与ダメージ +35%
            Assert.That(Run(RoleItems.Clapper, triple, streak: 1).outgoingPctAdd - b.outgoingPctAdd, Is.EqualTo(TerminalCards.ClapperPct).Within(1e-4));
            Assert.That(Run(RoleItems.Clapper, triple).outgoingPctAdd, Is.EqualTo(b.outgoingPctAdd).Within(1e-4));
            Assert.That(CombatManager.OmniPairChance(2), Is.EqualTo(0f));
            Assert.That(CombatManager.OmniPairChance(3), Is.EqualTo(CombatManager.OmniPctMid));
            Assert.That(CombatManager.OmniPairChance(5), Is.EqualTo(1f));
        }

        /// <summary>役の端子〈役落とし〉〈賽振り台〉は賽目でなく、 この戦闘の役の回数 (このターンを含む) を読む (Phase 3i)。</summary>
        [Test]
        public void RoleTerminalsReadRoleCount()
        {
            var env = new TerminalEnv { chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 10, enemyHp = 100, enemyMaxHp = 100 };
            TerminalOutcome R(string id, int[] dice, int count)
                => TerminalCards.Resolve(new List<string> { id, TerminalIds.Block, TerminalIds.Block }, new[] { 0, 1, 2 }, dice,
                                         env, new TerminalBattleState { roleCount = count });
            int[] pair = { 3, 3, 5 }, triple = { 4, 4, 4 }, none = { 1, 3, 6 };
            Assert.That(R(TerminalIds.RoleDrop, pair, 2).attackAdd - R(TerminalIds.RoleDrop, pair, 0).attackAdd, Is.EqualTo(2 * TerminalCards.RoleDropPer));
            Assert.That(R(TerminalIds.RoleDrop, triple, 1).attackAdd - R(TerminalIds.RoleDrop, triple, 0).attackAdd, Is.EqualTo(2 * TerminalCards.RoleDropPer));
            Assert.That(R(TerminalIds.RoleDrop, none, 5).attackAdd, Is.EqualTo(R(TerminalIds.RoleDrop, none, 0).attackAdd));
            Assert.That(R(TerminalIds.DiceTable, pair, 2).rerollDiscountNext, Is.EqualTo(3));
            Assert.That(R(TerminalIds.DiceTable, none, 2).rerollDiscountNext, Is.EqualTo(2));
            Assert.That(R(TerminalIds.DiceTable, pair, 20).rerollDiscountNext, Is.EqualTo(TerminalCards.DiceTableCap));
        }
    }
}
#endif

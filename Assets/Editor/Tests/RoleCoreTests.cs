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
        private static TerminalOutcome Run(RoleItems f, int[] dice, int roleCount = 0, int lastFace = 0, bool rerolled = false)
        {
            var board = new List<string> { TerminalIds.Attack, TerminalIds.Attack, TerminalIds.Block };
            var st = new TerminalBattleState { roleItems = f, roleCount = roleCount, roleLastFace = lastFace };
            var env = new TerminalEnv { chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 10, enemyHp = 100, enemyMaxHp = 100, diceRerolled = rerolled };
            return TerminalCards.Resolve(board, new[] { 0, 1, 2 }, dice, env, st);
        }

        /// <summary>役の品は指定の目の役でだけ働く (2026-11-22・同日 neon 収束で目を広げた: 木札 4・5 / 刻み板 3・4 / 見立て 1・2)。</summary>
        [Test]
        public void ItemsFireOnlyOnTheirFace()
        {
            int[] p1 = { 1, 1, 5 }, p2 = { 2, 2, 5 }, p3 = { 3, 3, 5 }, p4 = { 4, 4, 1 }, p5 = { 5, 5, 2 }, p6 = { 6, 6, 2 };
            int Atk(RoleItems f, int[] d, int c = 0, bool rr = false) => Run(f, d, c, rerolled: rr).attackAdd - Run(RoleItems.None, d).attackAdd;
            Assert.That(Run(RoleItems.None, p2).roleNow, Is.EqualTo(1));
            // 見切りの木札: 4・5 の役
            Assert.That(Atk(RoleItems.WoodTag, p5), Is.EqualTo(TerminalCards.WoodTagAttack * 2));
            Assert.That(Atk(RoleItems.WoodTag, p4), Is.EqualTo(TerminalCards.WoodTagAttack * 2));
            Assert.That(Run(RoleItems.WoodTag, p5).blockAdd - Run(RoleItems.None, p5).blockAdd, Is.EqualTo(TerminalCards.WoodTagBlock));
            Assert.That(Atk(RoleItems.WoodTag, p3), Is.EqualTo(0));
            Assert.That(Atk(RoleItems.WoodTag, p6), Is.EqualTo(0));
            // 符丁の刻み板: 3・4 の役・回数 (3 + 1)・上限
            Assert.That(Atk(RoleItems.NotchBoard, p4, 3), Is.EqualTo(TerminalCards.NotchBoardPer * 4 * 2));
            Assert.That(Atk(RoleItems.NotchBoard, p3, 3), Is.EqualTo(TerminalCards.NotchBoardPer * 4 * 2));
            Assert.That(Atk(RoleItems.NotchBoard, p4, 20), Is.EqualTo(TerminalCards.NotchBoardMax * 2));
            Assert.That(Atk(RoleItems.NotchBoard, p5, 3), Is.EqualTo(0));
            // 血染めの歯車: 振り直し + 3 の役
            Assert.That(Atk(RoleItems.Gear, p3, rr: true), Is.EqualTo(TerminalCards.GearAttack * 2));
            Assert.That(Atk(RoleItems.Gear, p3), Is.EqualTo(0));
            Assert.That(Atk(RoleItems.Gear, p5, rr: true), Is.EqualTo(0));
            // 欠け目の見立て: 1・2 の役だけ
            Assert.That(Run(RoleItems.Chipped, p1).armorPen - Run(RoleItems.None, p1).armorPen, Is.EqualTo(TerminalCards.ChippedPen));
            Assert.That(Run(RoleItems.Chipped, p2).armorPen - Run(RoleItems.None, p2).armorPen, Is.EqualTo(TerminalCards.ChippedPen));
            Assert.That(Run(RoleItems.Chipped, p3).armorPen, Is.EqualTo(Run(RoleItems.None, p3).armorPen));
            // 雷毒の導管の目 (RoleEffects.cs): 1・2・3
            Assert.That(TerminalCards.FaceIn(TerminalCards.ConduitFaces, 3) && !TerminalCards.FaceIn(TerminalCards.ConduitFaces, 4) && !TerminalCards.FaceIn(TerminalCards.ConduitFaces, 0), Is.True);
            // 役が無ければ何もしない
            Assert.That(Atk(RoleItems.WoodTag | RoleItems.NotchBoard, new[] { 1, 3, 5 }, 3), Is.EqualTo(0));
        }

        [Test]
        public void MultipliersAndOmniChance()
        {
            int[] triple = { 4, 4, 4 }, t5 = { 5, 5, 5 };
            var b = Run(RoleItems.None, triple);
            Assert.That(b.roleNow, Is.EqualTo(2));
            var t = Run(RoleItems.Thunder, triple);
            Assert.That(t.damageMul, Is.EqualTo(b.damageMul * (1f + TerminalCards.ThunderFinalPct)).Within(1e-4));
            Assert.That(t.blockAdd - b.blockAdd, Is.EqualTo(TerminalCards.ThunderBlock));
            // 血濡れの符丁: 2 個そろいだけ・回数 (1 + 1) × BloodmarkPer (2026-11-22 final4)・ゾロ目では働かない
            int[] pair = { 2, 2, 5 };
            Assert.That(Run(RoleItems.Bloodmark, pair, roleCount: 1).damageMul, Is.EqualTo(Run(RoleItems.None, pair).damageMul * (1f + 2 * TerminalCards.BloodmarkPer)).Within(1e-4));
            Assert.That(Run(RoleItems.Bloodmark, triple, roleCount: 1).damageMul, Is.EqualTo(b.damageMul).Within(1e-4));
            // final5: × 7%・上限なし ── 役 10 回目のペアで ×1.70
            Assert.That(Run(RoleItems.Bloodmark, pair, roleCount: 9).damageMul, Is.EqualTo(Run(RoleItems.None, pair).damageMul * 1.70f).Within(1e-4));
            Assert.That(Run(RoleItems.Bloodmark, triple, roleCount: 9).damageMul, Is.EqualTo(b.damageMul).Within(1e-4));
            Assert.That(Run(RoleItems.Bloodmark, t5, roleCount: 1).damageMul, Is.EqualTo(Run(RoleItems.None, t5).damageMul).Within(1e-4));
            Assert.That(Run(RoleItems.Bloodmark, new[] { 1, 3, 5 }, roleCount: 1).damageMul, Is.EqualTo(Run(RoleItems.None, new[] { 1, 3, 5 }).damageMul).Within(1e-4));   // 役なし
            // 拍子木: 前のターンに同じ目の役が立っていれば与ダメージ +N%
            Assert.That(Run(RoleItems.Clapper, triple, lastFace: 4).outgoingPctAdd - b.outgoingPctAdd, Is.EqualTo(TerminalCards.ClapperPct).Within(1e-4));
            Assert.That(Run(RoleItems.Clapper, triple, lastFace: 3).outgoingPctAdd, Is.EqualTo(b.outgoingPctAdd).Within(1e-4));
            Assert.That(Run(RoleItems.Clapper, triple).outgoingPctAdd, Is.EqualTo(b.outgoingPctAdd).Within(1e-4));
            // 〈万象のゾロ目〉(final6): 役 3 回で 0%・4 回で 50%・6 回で 50%・7 回で 100%
            Assert.That(CombatManager.OmniPairChance(3), Is.EqualTo(0f));
            Assert.That(CombatManager.OmniPairChance(4), Is.EqualTo(0.5f));
            Assert.That(CombatManager.OmniPairChance(6), Is.EqualTo(0.5f));
            Assert.That(CombatManager.OmniPairChance(7), Is.EqualTo(1f));
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

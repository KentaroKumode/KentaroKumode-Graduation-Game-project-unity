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
        public void BossNotch_RaiseAndNextDownBuffs()
        {
            var d = new BossDownState();
            d.Reset(new[] { 0.5f, 0.25f });
            d.ResetBuffs();
            // 目盛りを上げても今の HP の 1 下まで (上げるだけでは割れない・2026-11-25)
            Assert.That(d.RaiseNext(100, 59, 30), Is.EqualTo(59));
            Assert.That(d.broken, Is.EqualTo(0));
            // 次のダウンを強める: 上限 +100%・よろめきでは使わない・ダウンの最初のターンに使う
            d.BuffNext(50, 20, 0); d.BuffNext(50, 0, 10); d.BuffNext(50, 0, 0);
            Assert.That(d.bonusPct, Is.EqualTo(BossDown.TakenBonusMaxPct));
            Assert.That(d.ExtendNext(), Is.True);
            Assert.That(d.ExtendNext(), Is.False);              // 1 回のダウンにつき 1 ターン
            d.pending = 1; d.pendingWeak.Add(true);             // よろめき
            d.BeginTurn();
            Assert.That(d.TakeBuffs(out _, out _, out _), Is.False);
            Assert.That(d.NoteMainHit(40, 100, 10, 2, out _, out _), Is.EqualTo(1));   // 通常攻撃で割った = ダウン
            d.BeginTurn();
            Assert.That(d.TakeBuffs(out int sh, out int ch, out bool ext), Is.True);
            Assert.That(sh, Is.EqualTo(20)); Assert.That(ch, Is.EqualTo(10)); Assert.That(ext, Is.True);
            Assert.That(d.bonusNowPct, Is.EqualTo(100));
            Assert.That(d.pending, Is.EqualTo(1));             // 延長で次のターンもダウン
            d.BeginTurn();
            Assert.That(d.TakeBuffs(out _, out _, out _), Is.False);   // 同じダウンの続きでは使わない
            Assert.That(d.bonusNowPct, Is.EqualTo(100));
            d.BeginTurn();
            d.TakeBuffs(out _, out _, out _);
            Assert.That(d.bonusNowPct, Is.EqualTo(0));         // ダウンが明けたら上乗せは消える
        }
    }
}
#endif

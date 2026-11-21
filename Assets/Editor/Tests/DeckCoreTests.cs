#if UNITY_EDITOR
using System.Collections.Generic;
using CombatSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>山札の軸の品 (Resolve の 6c・切り直し・使い捨ての戻り) の契約 (full-item-rework-ledger §⑦)。</summary>
    [TestFixture]
    public sealed class DeckCoreTests
    {
        private static TerminalOutcome Run(DeckItems f, int disposedBefore)
        {
            var board = new List<string> { TerminalIds.Detonator, TerminalIds.Attack, TerminalIds.Block };
            var st = new TerminalBattleState { deckItems = f, disposedTotal = disposedBefore };
            var env = new TerminalEnv { chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 10, enemyHp = 100, enemyMaxHp = 100 };
            return TerminalCards.Resolve(board, new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, env, st);
        }

        [Test]
        public void Disposal_HitsBlockAndAshMultiplier()
        {
            var b = Run(DeckItems.None, 1);
            var o = Run(DeckItems.Tongs | DeckItems.Pride | DeckItems.Crucible | DeckItems.Ash, 1);
            Assert.That(o.disposedNow, Is.EqualTo(1));
            // 撃ごとに 挟み 2 + 矜持 4 + るつぼ min(32, 4 × 累計 2) = 14・攻撃の撃は 2 つ
            int each = TerminalCards.TongsAttack + TerminalCards.PrideAttack + TerminalCards.CruciblePer * 2;
            Assert.That(o.attackAdd - b.attackAdd, Is.EqualTo(each * 2));
            Assert.That(o.blockAdd - b.blockAdd, Is.EqualTo(TerminalCards.PrideBlock));
            Assert.That(o.damageMul, Is.EqualTo(b.damageMul * (1f + TerminalCards.AshPer * 2)).Within(1e-4));
            // 〈灰から組む機関〉は累計 2 回から (1 回なら倍率なし)
            Assert.That(Run(DeckItems.Ash, 0).damageMul, Is.EqualTo(b.damageMul).Within(1e-4));
        }

        [Test]
        public void Engine_DrawCapAndBagCap()
        {
            var st = new TerminalBattleState { deckItems = DeckItems.Engine };
            st.BeginTurn();
            var discard = new List<string> { TerminalIds.Attack, TerminalIds.Block };
            int more = 0;
            for (int i = 0; i < 3; i++) more += TerminalDeckState.BeforeShuffle(discard, new List<string>(), 10, st, null, null, null);
            Assert.That(more, Is.EqualTo(TerminalCards.EngineDrawMax));      // 1 ターンに 2 枚まで
            Assert.That(st.doubleAll, Is.True);                              // 切り直し 2 回目で倍化
            // 〈解体屋の袋〉: 3 回ごとに 1 枚・1 戦闘 5 回まで
            var bag = new TerminalBattleState { deckItems = DeckItems.Bag, disposedTotal = 3 };
            var o = new TerminalOutcome { disposedNow = 3 };
            var exiled = new List<string> { TerminalIds.Detonator };
            var ret = new List<string>();
            TerminalDeckState.DisposeFired(new List<string>(), o, new List<string>(), exiled, ret, bag, new List<string>(), new List<string>(), new List<string>());
            Assert.That(ret, Is.EquivalentTo(new[] { TerminalIds.Detonator }));
            bag.bagReturns = TerminalCards.BagMax; ret.Clear(); exiled.Add(TerminalIds.Detonator);
            TerminalDeckState.DisposeFired(new List<string>(), o, new List<string>(), exiled, ret, bag, new List<string>(), new List<string>(), new List<string>());
            Assert.That(ret, Is.Empty);
        }
    }
}
#endif

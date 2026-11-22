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
            return TerminalCards.Resolve(board, new[] { 0, 1, 2 }, new[] { 5, 4, 3 }, env, st);      // 〈火薬樽〉は 5 以上 (2026-11-22)
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
           Assert.That(st.doubleAll, Is.True);                              // 切り直し EngineEvery (3) 回目で倍化
           // 〈解体屋の袋〉: BagEvery (4) 回ごとに 1 枚・1 戦闘 5 回まで
            var bag = new TerminalBattleState { deckItems = DeckItems.Bag, disposedTotal = TerminalCards.BagEvery };
            var o = new TerminalOutcome { disposedNow = TerminalCards.BagEvery };
            var exiled = new List<string> { TerminalIds.Detonator };
            var ret = new List<string>();
            TerminalDeckState.DisposeFired(new List<string>(), o, new List<string>(), exiled, ret, bag, new List<string>(), new List<string>(), new List<string>());
            Assert.That(ret, Is.EquivalentTo(new[] { TerminalIds.Detonator }));
            bag.bagReturns = TerminalCards.BagMax; ret.Clear(); exiled.Add(TerminalIds.Detonator);
            TerminalDeckState.DisposeFired(new List<string>(), o, new List<string>(), exiled, ret, bag, new List<string>(), new List<string>(), new List<string>());
            Assert.That(ret, Is.Empty);
        }

        private static TerminalOutcome One(string id, int die, TerminalEnv env, TerminalBattleState st = null)
            => TerminalCards.Resolve(new List<string> { id }, new[] { 0 }, new[] { die }, env, st ?? new TerminalBattleState());

        /// <summary>使い捨ての発動の条件 (2026-11-22・neon 最終仕上げ §2-1)。 合わなければ不発で、 使い捨ても消費しない。</summary>
        [Test]
        public void Disposables_SituationGates()
        {
            var env = new TerminalEnv { chargeCap = 30, hp = 40, maxHp = 50, enemyAttack = 10, enemyHp = 100, enemyMaxHp = 100 };
            Assert.That(One(TerminalIds.FirstAid, 3, env).fired, Is.Empty);                 // まだ HP を失っていない
            env.hpLostBattle = true;
            Assert.That(One(TerminalIds.FirstAid, 3, env).heal, Is.EqualTo(3 * TerminalCards.FirstAidMul));
            Assert.That(One(TerminalIds.FirstAid, 4, env).fired, Is.Empty);                 // 3 以下
            Assert.That(One(TerminalIds.Detonator, 4, env).fired, Is.Empty);                // 5 以上
            Assert.That(One(TerminalIds.Detonator, 5, env).attackAdd, Is.EqualTo(5 * TerminalCards.DetonatorMul));
            Assert.That(One(TerminalIds.Piercer, 4, env).fired, Is.Empty);                  // 奇数
            Assert.That(One(TerminalIds.Reload, 4, env).fired, Is.Empty);                   // 5 以上
            var quiet = env; quiet.enemyAttack = 0;
            Assert.That(One(TerminalIds.ReliefValve, 2, quiet).fired, Is.Empty);            // 予告の無いターン
            Assert.That(One(TerminalIds.ReliefValve, 2, env).fired.Count, Is.EqualTo(1));
            Assert.That(One(TerminalIds.BurnSlash, 4, env).fired, Is.Empty);                // 前のターンまでに使い捨て 0 回
            var st = new TerminalBattleState { disposedTotal = 2 };
            Assert.That(One(TerminalIds.BurnSlash, 4, env, st).attackAdd, Is.EqualTo(4 * TerminalCards.BurnSlashMul + 2 * TerminalCards.BurnSlashPer));
        }

        /// <summary>軸の端子は偏重でなく戦闘の状態を読む (2026-11-22・§3-2)。</summary>
        [Test]
        public void AxisTerminals_ReadBattleState()
        {
            var env = new TerminalEnv { chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 10, enemyHp = 100, enemyMaxHp = 100, shield = 20 };
            Assert.That(One(TerminalIds.Patch, 3, env).shield, Is.EqualTo(TerminalCards.PatchBase));
            var jg = env; jg.justGuardLast = true;
            Assert.That(One(TerminalIds.Patch, 3, jg).shield, Is.EqualTo(TerminalCards.PatchJust));
            var push = One(TerminalIds.ShieldPush, 3, env);
            Assert.That(push.shieldSpend, Is.EqualTo(10));
            Assert.That(push.attackAdd, Is.EqualTo(3 + 10));
            Assert.That(One(TerminalIds.Mister, 3, env).poison, Is.EqualTo(3 + TerminalCards.MisterBase));
            var psn = env; psn.enemyPoison = 1;
            Assert.That(One(TerminalIds.Mister, 3, psn).poison, Is.EqualTo(3 + TerminalCards.MisterPoisoned));
            var bl = env; bl.enemyBleed = 20;
            Assert.That(One(TerminalIds.Barb, 3, bl).bleedOnHit, Is.EqualTo(5));
            Assert.That(One(TerminalIds.Barb, 3, env).bleedOnHit, Is.EqualTo(TerminalCards.BarbMin));
            var crit = env; crit.critsBattle = 5;
            Assert.That(One(TerminalIds.Sight, 3, crit).critMultAdd, Is.EqualTo(5 * TerminalCards.SightPerCrit).Within(1e-4));
            var over = env; over.charge = ChargeCore.OverchargeAt;
            Assert.That(One(TerminalIds.Leak, 3, over).chargeAdd, Is.EqualTo(3 + TerminalCards.LeakOver));
            Assert.That(One(TerminalIds.Leak, 3, env).chargeAdd, Is.EqualTo(3 + TerminalCards.LeakBase));
            var hot = env; hot.heatHot = true;
            Assert.That(One(TerminalIds.Stoker, 3, hot).heat, Is.EqualTo(3 + TerminalCards.StokerHot));
            var dis = env; dis.charge = 10; dis.chargeSpendsTurn = 2;      // 振り直しで 2 回使った後に〈裁きの雷〉: 10 × 1.5
            Assert.That(One(TerminalIds.BigDischarge, 3, dis).attackAdd, Is.EqualTo(15));
        }
    }
}
#endif

#if UNITY_EDITOR
using System.Collections.Generic;
using CombatSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>臨界の軸の式 (爆発のダメージ・防御 50% 無視・連鎖の上限・超臨界・5g の配線の臨界) の契約。</summary>
    [TestFixture]
    public sealed class HeatCoreTests
    {
        private static HeatCore.State S(HeatItems f, int meter, int def = 0)
            => new HeatCore.State { items = f, meter = meter, threshold = HeatCore.StartThreshold(f), enemyDefense = def };

        [Test]
        public void Burst_BaseAndDefensePen()
        {
            var s = S(HeatItems.None, 30, 10);
            Assert.That(HeatCore.TryBurst(ref s, out var b), Is.True);
            Assert.That(b.damage, Is.EqualTo(30));
            Assert.That(b.landed, Is.EqualTo(DefenseFormula.Apply(30, 5)));   // 防御 10 の半分を無視
            Assert.That(s.meter, Is.EqualTo(0));
            Assert.That(HeatCore.TryBurst(ref s, out _), Is.False);
            // 〈火花読みの眼鏡〉: 防御を全部無視・1 回目 ×1.15
            var g = S(HeatItems.Spark, 30, 10);
            HeatCore.TryBurst(ref g, out var bg);
            Assert.That(bg.landed, Is.EqualTo(30 * 115 / 100));
        }

        [Test]
        public void Burst_SuperKilnLedger()
        {
            // 超臨界 (閾値の 1.5 倍以上) ×1.5・〈暴れ窯の火床〉×2.0 と閾値 −4
            var s = S(HeatItems.None, 45);
            HeatCore.TryBurst(ref s, out var b);
            Assert.That(b.super && b.damage == 45, Is.True);
            var k = S(HeatItems.Kiln, 45);
            HeatCore.TryBurst(ref k, out var bk);
            Assert.That(bk.damage, Is.EqualTo(60));
            Assert.That(k.threshold, Is.EqualTo(26));
            // 〈帳簿外の爆薬〉: 閾値ぶんだけ減らして続けて・1 ターン 5 回まで
            var l = S(HeatItems.Ledger, 300);
            int landed = 0;
            Assert.That(HeatCore.Step(ref l, 0, ref landed) + CountBursts(ref l), Is.EqualTo(HeatCore.LedgerMax));
            Assert.That(l.meter, Is.EqualTo(300 - 30 * HeatCore.LedgerMax));
        }

        private static int CountBursts(ref HeatCore.State s) { int n = 0; while (HeatCore.TryBurst(ref s, out _)) n++; return n; }

        [Test]
        public void Terminals_WiredHeat()
        {
            var board = new List<string> { TerminalIds.Attack, TerminalIds.Attack, TerminalIds.Block };
            var st = new TerminalBattleState { heatItems = HeatItems.Stone | HeatItems.Spanner | HeatItems.Breastplate | HeatItems.Flint };
            var env = new TerminalEnv { chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 10, enemyHp = 100, enemyMaxHp = 100 };
            var o = TerminalCards.Resolve(board, new[] { 0, 1, 2 }, new[] { 5, 4, 3 }, env, st);
            // 過熱石 +10 (攻撃に 2 個)・燻り +4 (防御 1 枚)・スパナ +4 × 2 撃・燧石の倍 (攻撃 2 枚)
            Assert.That(o.heatGains, Is.EquivalentTo(new[] { HeatCore.StoneTwo, HeatCore.BreastHeat, HeatCore.SpannerPerHit, HeatCore.SpannerPerHit }));
            Assert.That(o.heatDouble, Is.True);
            var f = HeatCore.Forecast(new HeatCore.State { items = st.heatItems, threshold = 30 }, o, 0);
            Assert.That(f.burstsWired, Is.EqualTo(1));   // (10+4+4+4) × 2 = 44 で 1 回 (30 を越えた時に 0 へ)
        }
    }
}
#endif

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
            var s = S(HeatItems.None, HeatCore.Threshold, 10);
            Assert.That(HeatCore.TryBurst(ref s, out var b), Is.True);
            Assert.That(b.damage, Is.EqualTo(30));
            Assert.That(b.landed, Is.EqualTo(DefenseFormula.Apply(30, 5)));   // 防御 10 の半分を無視
            Assert.That(s.meter, Is.EqualTo(0));
            Assert.That(HeatCore.TryBurst(ref s, out _), Is.False);
            // 〈火花読みの眼鏡〉: 防御を全部無視・1 回目 ×(1 + 回数 × 5%)
            var g = S(HeatItems.Spark, HeatCore.Threshold, 10);
            HeatCore.TryBurst(ref g, out var bg);
            Assert.That(bg.landed, Is.EqualTo(30 * (100 + HeatCore.SparkPctPerBurst) / 100));
        }

        [Test]
        public void Burst_SuperKilnLedger()
        {
            // 超臨界 (閾値の 1.5 倍以上) ×1.5・〈暴れ窯の火床〉×KilnSuperMulPct (2.0・final5) と閾値 −4
            var s = S(HeatItems.None, HeatCore.Threshold * 3 / 2);
            HeatCore.TryBurst(ref s, out var b);
            Assert.That(b.super && b.damage == 45, Is.True);   // 基礎 30 × 1.5
            var k = S(HeatItems.Kiln, HeatCore.Threshold * 3 / 2);
            HeatCore.TryBurst(ref k, out var bk);
            Assert.That(bk.damage, Is.EqualTo(HeatCore.BurstBase * HeatCore.KilnSuperMulPct / 100));
            Assert.That(k.threshold, Is.EqualTo(HeatCore.Threshold - 4));
            // 〈帳簿外の爆薬〉: 閾値ぶんだけ減らして続けて・1 ターン 5 回まで
            var l = S(HeatItems.Ledger, 300);
            int landed = 0;
            Assert.That(HeatCore.Step(ref l, 0, ref landed) + CountBursts(ref l), Is.EqualTo(HeatCore.LedgerMax));
            Assert.That(l.meter, Is.EqualTo(300 - HeatCore.Threshold * HeatCore.LedgerMax));
        }

        private static int CountBursts(ref HeatCore.State s) { int n = 0; while (HeatCore.TryBurst(ref s, out _)) n++; return n; }

        [Test]
        public void Terminals_WiredHeat()
        {
            var board = new List<string> { TerminalIds.Attack, TerminalIds.Attack, TerminalIds.Block };
            var st = new TerminalBattleState { heatItems = HeatItems.Stone | HeatItems.Spanner | HeatItems.Breastplate | HeatItems.Flint };
            var env = new TerminalEnv { chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 10, enemyHp = 100, enemyMaxHp = 100 };
            var o = TerminalCards.Resolve(board, new[] { 0, 1, 2 }, new[] { 5, 4, 3 }, env, st);
           // 過熱石 (攻撃に 2 個)・燻り (防御 1 枚)・スパナ × 2 撃。 燧石の倍は 3 撃から (2026-11-22 neon 収束) なので 2 撃では立たない
            Assert.That(o.heatGains, Is.EquivalentTo(new[] { HeatCore.StoneTwo, HeatCore.BreastHeat, HeatCore.SpannerPerHit, HeatCore.SpannerPerHit }));
            Assert.That(o.heatDouble, Is.False);
            Assert.That(o.attackPips, Is.EqualTo(9));                  // 攻撃に挿した 5 + 4
            Assert.That(HeatCore.EndGain(o), Is.EqualTo(4));           // ターンの終わりは 9 の半分 (切り捨て)
            // 攻撃 3 枚 (5・4・3): 過熱石 (3 個とも攻撃)・スパナ × 3 撃・燧石の倍
            var board3 = new List<string> { TerminalIds.Attack, TerminalIds.Attack, TerminalIds.Attack };
            var o3 = TerminalCards.Resolve(board3, new[] { 0, 1, 2 }, new[] { 5, 4, 3 }, env, st);
            Assert.That(o3.heatGains, Is.EquivalentTo(new[] { HeatCore.StoneAll, HeatCore.SpannerPerHit, HeatCore.SpannerPerHit, HeatCore.SpannerPerHit }));
            Assert.That(o3.heatDouble, Is.True);
            var f = HeatCore.Forecast(new HeatCore.State { items = st.heatItems, threshold = 40 }, o3);
            // 過熱石 16 × 2 = 32 → スパナ 4 × 2 で 40 (閾値 40 に届いて 0 へ・1 回) → +8 +8 = 16 → ターンの終わり 12 / 2 = 6 × 2 = 12 → 28 (final4 の値)
            Assert.That(f.burstsWired, Is.EqualTo(1));
            Assert.That(f.burstsEnd, Is.EqualTo(0));
            Assert.That(f.meterAfter, Is.EqualTo(28));
        }
    }
}
#endif

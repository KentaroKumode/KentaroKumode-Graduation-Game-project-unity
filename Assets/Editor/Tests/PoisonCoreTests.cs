#if UNITY_EDITOR
using System.Collections.Generic;
using CombatSystem;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>毒の軸の式 (【起爆】の消費とダメージ・PoisonCore / TerminalCards.Resolve の 5d) の契約。</summary>
    [TestFixture]
    public sealed class PoisonCoreTests
    {
        private static readonly List<string> Board = new List<string> { TerminalIds.Attack, TerminalIds.Block };

        private static TerminalOutcome Run(PoisonItems items, int[] dice, int[] slots, int enemyPoison = 0)
        {
            var st = new TerminalBattleState { poisonItems = items };
            var env = new TerminalEnv { charge = 0, chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 10, enemyPoison = enemyPoison };
            return TerminalCards.Resolve(Board, slots, dice, env, st);
        }

        [Test]
        public void Detonate_SpendsThirdRoundedUp_AndMultiplies()
        {
            // 消費は 1/3 (切り上げ・2026-11-22 neon 収束: 旧 半分)
            Assert.That(PoisonCore.Spend(25), Is.EqualTo(9));
            Assert.That(PoisonCore.Spend(24), Is.EqualTo(8));
            Assert.That(PoisonCore.Spend(1), Is.EqualTo(1));
            Assert.That(PoisonCore.Spend(0), Is.EqualTo(0));
            // 最大 HP の小さい敵: 消費 × 倍率の方が大きい (最大 HP 100 なら 12 × 100 × 0.25% = 3)
            Assert.That(PoisonCore.Damage(12, PoisonItems.None, 0, false, 100), Is.EqualTo(48));                       // ×4
            Assert.That(PoisonCore.Damage(12, PoisonItems.Magazine, 0, false, 100), Is.EqualTo(60));                   // 〈劇毒の火薬庫〉×5
            Assert.That(PoisonCore.Damage(12, PoisonItems.SlowBlade, 2, false, 100), Is.EqualTo(76));                  // 〈遅効の毒刃〉+30% × 2 回 (48 × 1.6 = 76.8 → 76)
            Assert.That(PoisonCore.Damage(12, PoisonItems.Oil, 0, true, 100), Is.EqualTo(96));                         // 〈骨溶かしの黒油〉ダウン中 ×2
            Assert.That(PoisonCore.Damage(12, PoisonItems.Oil, 0, false, 100), Is.EqualTo(48));
            Assert.That(PoisonCore.Damage(12, PoisonItems.None, 0, false, 100, PoisonCore.MantleEchoPct), Is.EqualTo(24));   // 〈瘴気の外套〉50%
            // 最大 HP の大きい敵 (2026-11-22 neon 収束): floor(消費 × 最大 HP × 25 / 10000)・〈劇毒の火薬庫〉は 32
            Assert.That(PoisonCore.Damage(12, PoisonItems.None, 0, false, 3000), Is.EqualTo(12 * 3000 * PoisonCore.DetonateMaxHpBp / 10000));   // 90
            Assert.That(PoisonCore.Damage(12, PoisonItems.Magazine, 0, false, 3000), Is.EqualTo(12 * 3000 * PoisonCore.MagazineMaxHpBp / 10000));   // 115
            Assert.That(PoisonCore.Damage(12, PoisonItems.None, 0, false, 3000, PoisonCore.MantleEchoPct), Is.EqualTo(45));
        }

        [Test]
        public void Terminals_PoisonItemsAddToOutcome()
        {
            // 攻撃に 5・防御に 3: 小刀 (撃 1 つ +2)・蛇血瓶 (4 以上の目の攻撃 +3)・小匙 (両方 +3・HP +1)・小盾 (ブロック +4)
            var all = PoisonItems.GreenKnife | PoisonItems.Serpent | PoisonItems.Taster | PoisonItems.Thorn;
            var o = Run(all, new[] { 5, 3 }, new[] { 0, 1 });
            Assert.That(o.poison, Is.EqualTo(PoisonCore.KnifePoison + PoisonCore.SerpentPoison + PoisonCore.TasterPoison));
            Assert.That(o.heal, Is.EqualTo(PoisonCore.TasterHeal));
            Assert.That(o.blockAdd, Is.EqualTo(Run(PoisonItems.None, new[] { 5, 3 }, new[] { 0, 1 }).blockAdd + PoisonCore.ThornBlock));
            // 攻撃の目が 3 なら蛇血瓶は鳴らない・防御に挿さなければ小匙も鳴らない
            Assert.That(Run(PoisonItems.Serpent | PoisonItems.Taster, new[] { 3, 3 }, new[] { 0, -1 }).poison, Is.EqualTo(0));
        }

        [Test]
        public void Terminals_MantleAndBlowpipeNeedPoison()
        {
            Assert.That(Run(PoisonItems.Mantle, new[] { 5, 3 }, new[] { 0, 1 }, PoisonCore.MantleNeed).takenMul, Is.EqualTo(0.65f).Within(1e-4f));
            Assert.That(Run(PoisonItems.Mantle, new[] { 5, 3 }, new[] { 0, 1 }, PoisonCore.MantleNeed - 1).takenMul, Is.EqualTo(1f));
            Assert.That(Run(PoisonItems.Blowpipe, new[] { 5, 3 }, new[] { 0, 1 }, PoisonCore.BlowpipeIgnoreAt).ignoreDefense, Is.True);
            Assert.That(Run(PoisonItems.Blowpipe, new[] { 5, 3 }, new[] { 0, 1 }, PoisonCore.BlowpipeIgnoreAt - 1).ignoreDefense, Is.False);
        }
    }
}
#endif

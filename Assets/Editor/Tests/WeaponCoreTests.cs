#if UNITY_EDITOR
using CombatSystem;
using ItemSystem.PassiveSkills.Effects;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>武器 13 品のリワーク (full-item-rework-ledger §⑫) の式の契約。 本体の効果クラスと BOT が同じ CombatRules を呼ぶ。</summary>
    [TestFixture]
    public sealed class WeaponCoreTests
    {
        [Test]
        public void RulesMatchLedger()
        {
            Assert.That(CombatRules.GuardCutBonus(9), Is.EqualTo(6));            // 9 × 0.6 = 5.4 → 切り上げ 6
            Assert.That(CombatRules.ChainHitBase(5, 120), Is.EqualTo(6));        // 曲刀 5 × 1.2 (float なら 6.0000002 → 7 に化ける)
            Assert.That(CombatRules.ChainHitBase(7, 160), Is.EqualTo(12));       // ノクタリア 7 × 1.6 = 11.2 → 12
            Assert.That(CombatRules.UndyingPct(3), Is.EqualTo(36));
            Assert.That(CombatRules.UndyingPct(9), Is.EqualTo(72));             // 最大 +72%
            Assert.That(CombatRules.BashSpend(7, false), Is.EqualTo(4));         // 半分・切り上げ
            Assert.That(CombatRules.BashSpend(7, true), Is.EqualTo(7));
            Assert.That(CombatRules.BashBonus(4, 90), Is.EqualTo(4));            // 3.6 → 4
            Assert.That(CombatRules.BulwarkGain(15, 10, 120, false, 16), Is.EqualTo(6));
            Assert.That(CombatRules.BulwarkGain(11, 10, 120, true, 16), Is.EqualTo(16));   // ジャスト防御は余りに関係なく固定
            Assert.That(CombatRules.BulwarkGain(8, 10, 120, false, 16), Is.EqualTo(0));
            Assert.That(CombatRules.DaybreakCut(1000, 2, false), Is.EqualTo(100));
            Assert.That(CombatRules.DaybreakCut(1000, 2, true), Is.EqualTo(160));
            Assert.That(CombatRules.ExecutionKills(15, 100, false), Is.True);
            Assert.That(CombatRules.ExecutionKills(15, 100, true), Is.False);    // ボスは 10% 以下
            Assert.That(CombatRules.ExecutionBoostOn(40, 100), Is.True);
            Assert.That(CombatRules.ExecutionBoostOn(41, 100), Is.False);
            Assert.That(CombatRules.MugaBonus(7), Is.EqualTo(11));               // 10.5 → 11
            Assert.That(CombatRules.MugaBlock(2), Is.EqualTo(6));                // 防御の出目 2 個 × 3 (2026-11-22)
            // 〈画竜点睛〉(2026-11-22 final5): 振った目の最大 (タイ含む) で +100%・装備ダイスの最大の目で +200%・最大でなければ 0
            Assert.That(CombatRules.GaryoPct(5, 5, 6), Is.EqualTo(100));
            Assert.That(CombatRules.GaryoPct(6, 6, 6), Is.EqualTo(200));
            Assert.That(CombatRules.GaryoPct(4, 5, 6), Is.EqualTo(0));
        }

        [Test]
        public void BotRungReadsWeaponSkills()
        {
            var m = WeaponRungModel.From(new[] { "一対一II", "捨て身", "処刑", "鎧砕きII", "不滅の刃", "シールドバッシュII", "城壁III", "連刃I" });
            Assert.That(m.bashRatePct, Is.EqualTo(150)); Assert.That(m.bashAll, Is.True);
            Assert.That(m.bulwarkRatePct, Is.EqualTo(120)); Assert.That(m.bulwarkJust, Is.EqualTo(16));
            Assert.That(m.chainRatePct, Is.EqualTo(120));
            // 単体 18 + 捨て身 35 (防御なし) + 処刑 50 + 鎧砕き 20 = 123 / 両方に挿すと捨て身が外れ不滅の刃 2 連続 24 (2026-11-22 の値)
            Assert.That(m.FinalPct(true, 2, 0, true, true, 2), Is.EqualTo(123));
            Assert.That(m.FinalPct(true, 1, 1, false, false, 2), Is.EqualTo(18 + 24));
            Assert.That(m.FinalPct(false, 1, 1, false, false, 0), Is.EqualTo(0));
        }
    }
}
#endif

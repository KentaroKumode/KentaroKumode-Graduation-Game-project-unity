#if UNITY_EDITOR
using NUnit.Framework;
using UI.Game2D.Battle;
using UnityEngine;

namespace AutoTest.EditorTests
{
    /// <summary>武器の攻撃の演出 (2026-11-24): 段 2〜4 の 12 品それぞれに、 絵 (通常・会心) と当たる時刻の表がそろっている。 段 4 は名のある 4 本の絵。</summary>
    [TestFixture]
    public sealed class WeaponFxTests
    {
        [Test]
        public void EveryWeaponHasSheetsAndHits()
        {
            foreach (var fam in new[] { "sword", "axe", "dagger", "shield" })
                for (int tier = 2; tier <= 4; tier++)
                {
                    var w = ItemSystem.ItemDatabase.Instance.GetWeapon(fam, tier);
                    Assert.That(w, Is.Not.Null, $"{fam} 段 {tier}");
                    string key = FxLayerPreload.WeaponFamilyOf(w.id);
                    Assert.That(key, Is.EqualTo(tier == 4 ? fam + "-legend" : fam), w.id);
                    Assert.That(FxLayerPreload.BaseFamily(key), Is.EqualTo(fam));
                    foreach (var k in new[] { "-normal", "-crit" })
                    {
                        Assert.That(Resources.Load<TextAsset>("Game2DFx/weapon-" + key + k + ".fxb"), Is.Not.Null, "絵 weapon-" + key + k);
                        Assert.That(BattleMotion.WeaponSheetHits.ContainsKey(key + k), "当たる時刻 " + key + k);
                    }
                }
        }
    }
}
#endif

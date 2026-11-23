#if UNITY_EDITOR
using System.Collections.Generic;
using MetaProgression;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>整備盤 (2026-11-23・docs/GAME.md §15-1): マスの値段の階段・形と回転・置き方の検査・標準の完成盤の数値・実績のパネル。</summary>
    [TestFixture]
    public sealed class MetaBoardTests
    {
        static List<BoardPlacement> Standard() => new List<BoardPlacement>
        {
            new BoardPlacement("core_last_stand", 0, 0, 0),   // T: 上の行 + 中心
            new BoardPlacement("mod_heavy_frame", 0, 1, 0),   // L3: 左下
            new BoardPlacement("unit_atk_tune", 1, 1, 2),     // I2 (縦): 右の 2 マス
        };

        [Test]
        public void CellCosts_CenterThenEdgesThenCorners_Ladder()
        {
            int m = 0;
            Assert.That(MetaBoard.OpenCost(m, 1), Is.EqualTo(-1), "中心より先に辺は開かない");
            Assert.That(MetaBoard.OpenCost(m, 4), Is.EqualTo(1));
            m |= 1 << 4;
            int total = 1;
            int[] edges = { 1, 3, 5, 7 };
            for (int i = 0; i < 4; i++)
            {
                Assert.That(MetaBoard.OpenCost(m, 0), Is.EqualTo(-1), "辺が全部開くまで角は開かない");
                int c = MetaBoard.OpenCost(m, edges[i]);
                Assert.That(c, Is.EqualTo(MetaBoard.EdgeCost[i]));
                total += c; m |= 1 << edges[i];
            }
            int[] corners = { 8, 0, 6, 2 };
            for (int i = 0; i < 4; i++) { int c = MetaBoard.OpenCost(m, corners[i]); Assert.That(c, Is.EqualTo(MetaBoard.CornerCost[i])); total += c; m |= 1 << corners[i]; }
            Assert.That(total, Is.EqualTo(2781), "盤だけの累計 (answer23 §3)");
            Assert.That(MetaBoard.OpenCost(m, 4), Is.EqualTo(-1));
        }

        [Test]
        public void FourCellShapes_AlwaysUseCenter_RotationsStayInBoard()
        {
            foreach (var id in new[] { "core_last_stand", "spark_poison", "core_parallel", "core_balance" })
            {
                var ps = MetaBoard.AllPlacements(id);
                Assert.That(ps.Count, Is.GreaterThan(0), id);
                foreach (var p in ps) Assert.That(System.Array.IndexOf(MetaBoard.CellsOf(p), MetaBoard.Center), Is.GreaterThanOrEqualTo(0), id + " は中心を使う");
            }
            Assert.That(MetaBoard.AllPlacements("chip_hp").Count, Is.EqualTo(9));
            Assert.That(MetaBoard.AllPlacements("unit_atk_tune").Count, Is.EqualTo(12));
        }

        [Test]
        public void Placement_RejectsOverlapClosedAndUnowned_StandardBoardSums()
        {
            var s = new MetaProgressState();
            s.ForceBoard((1 << 9) - 1, Standard());
            var e = s.BoardEffects;
            Assert.That(s.ValidPlacements.Count, Is.EqualTo(3));
            Assert.That(e.hp, Is.EqualTo(15)); Assert.That(e.defense, Is.EqualTo(3)); Assert.That(e.damagePct, Is.EqualTo(10));
            Assert.That(s.BoardHas(MetaModule.LastStand), Is.True); Assert.That(s.BoardHas(MetaModule.AttackTune), Is.True);
            Assert.That(s.BoardSpark(), Is.Null);

            s.GrantPanel("chip_hp");
            Assert.That(s.PlacePanel(new BoardPlacement("chip_hp", 0, 1, 1)), Is.False, "中心は T が使っている");
            Assert.That(s.PlacePanel(new BoardPlacement("chip_def", 0, 2, 2)), Is.False, "持っていない");

            var t = new MetaProgressState();
            t.ForceBoard(1 << 4, null);
            t.GrantPanel("chip_hp");
            Assert.That(t.PlacePanel(new BoardPlacement("chip_hp", 0, 0, 0)), Is.False, "閉じたマス");
            Assert.That(t.PlacePanel(new BoardPlacement("chip_hp", 0, 1, 1)), Is.True);
            Assert.That(t.BoardEffects.hp, Is.EqualTo(5));
            Assert.That(t.PlacePanel(new BoardPlacement("chip_hp", 0, 1, 1)), Is.True, "置き直しは同じパネルを差し替える");
            Assert.That(t.ValidPlacements.Count, Is.EqualTo(1));
        }

        [Test]
        public void MeasureSpecs_RepresentativeBoardsAllFit()
        {
            foreach (var (spec, n, cells) in new[] { ("board:empty", 0, 0), ("board:s3", 2, 3), ("board:s5", 2, 5), ("board:s6", 2, 6), ("board:standard", 3, 9),
                                                     ("stage:full", 3, 9), ("stage:none", 0, 0), ("spark:poison", 3, 9), ("board:standard,main:Valor", 3, 9),
                                                     ("open:all,place:core_immovable@0.0r0,place:mod_heavy_frame@1.0r0,place:unit_blk_tune@1.2r1", 3, 9) })
            {
                var lo = MetaAllocationPresets.ParseSpec(spec);
                Assert.That(lo, Is.Not.Null, spec);
                var s = new MetaProgressState();
                s.ForceBoard(lo.openMask, lo.placements);
                Assert.That(s.ValidPlacements.Count, Is.EqualTo(n), spec + " → " + s.DescribeBoard());
                Assert.That(MetaBoard.OpenCount(s.boardOpenMask), Is.EqualTo(cells), spec);
            }
            var v = MetaAllocationPresets.ParseSpec("board:standard,main:Valor");
            Assert.That(v.placements.Exists(p => p.id == "core_valor"), Is.True);
            var sp = new MetaProgressState(); var pl = MetaAllocationPresets.ParseSpec("spark:poison"); sp.ForceBoard(pl.openMask, pl.placements);
            Assert.That(sp.BoardSpark(), Is.EqualTo(MetaModule.SparkPoison));
        }

        [Test]
        public void Migration_RefundsOldPanelAndRelics_Idempotent()
        {
            // 旧いセーブ: 基礎 2 段・枠 1・モジュール 2 つ (主方針 120 + 補助 80)・遺物 2 個・素材 1
            var s = UnityEngine.JsonUtility.FromJson<MetaProgressState>(
                "{\"tokens\":5,\"baseTier\":2,\"slotsOpen\":1,\"unlockedModules\":[0,14],\"ownedRelics\":[{\"slot\":0,\"stars\":2},{\"slot\":1}],\"relicMaterial\":1}");
            Assert.That(s.ownedRelics.Count, Is.EqualTo(2), "旧い遺物の数は読める");
            s.MigrateToBoardIfNeeded();
            Assert.That(s.tokens, Is.EqualTo(5 + 40 + 60 + 50 + 120 + 80 + 2 * MetaProgressState.LegacyRelicRefund + MetaProgressState.LegacyMaterialRefund));
            int after = s.tokens;
            s.MigrateToBoardIfNeeded();
            Assert.That(s.tokens, Is.EqualTo(after), "2 回目は何も返さない");
            Assert.That(s.baseTier + s.slotsOpen + s.unlockedModules.Count + s.ownedRelics.Count + s.relicMaterial, Is.EqualTo(0));
        }

        [Test]
        public void Catalog_41Panels_AchievementPanelsPointAtRealAchievements()
        {
            Assert.That(MetaBoard.All.Count, Is.EqualTo(41));
            int ach = 0;
            foreach (var d in MetaBoard.All)
            {
                Assert.That(d.CellCount, Is.InRange(1, 4), d.id);
                if (!string.IsNullOrEmpty(d.module)) Assert.That(d.Module, Is.Not.Null, d.id);
                if (d.FromAchievement) { ach++; Assert.That(MetaProgression.Achievements.AchievementCatalog.Get(d.achievement), Is.Not.Null, d.id + " の実績 " + d.achievement); }
                else Assert.That(d.cost, Is.GreaterThan(0), d.id);
            }
            Assert.That(ach, Is.EqualTo(10));
        }
    }
}
#endif

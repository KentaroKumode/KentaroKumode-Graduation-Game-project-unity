#if UNITY_EDITOR
using System.IO;
using GameLoop;
using MapSystem;
using MapSystem.FreeMove;
using MetaProgression;
using NUnit.Framework;
using UnityEngine;
using Slides = UI.Game2D.Game2DEndingScreen.SlideClock;

namespace AutoTest.EditorTests
{
    /// <summary>
    /// ラン指定 (RunSpec・2026-11-24・チュートリアルのラン「街に着く前」docs/GAME.md §23-35) の回帰テスト:
    /// 読み込み・一本道の地図と敵の焼き付け・負けた時のやり直しの判定・乱数の戻し・初回の印・絵の列の時間割 (slideshow_draft.py の値と突き合わせ)。
    /// </summary>
    [TestFixture]
    public sealed class RunSpecTests
    {
        private const string TutorialPath = "Assets/Resources/RunSpecs/tutorial.json";

        private static RunSpec Tutorial()
        {
            var s = RunSpec.Parse(File.ReadAllText(TutorialPath), "tutorial");
            Assert.That(s, Is.Not.Null, "tutorial.json が読めない");
            return s;
        }

        [TearDown]
        public void TearDown()
        {
            RunSpec.End();
            GameRng.ClearSeed();
        }

        // ── 読み込み ──

        [Test]
        public void Tutorial_Loads_WithExpectedShape()
        {
            var s = Tutorial();
            Assert.That(s.Validate(), Is.Null);
            CollectionAssert.AreEqual(new[] { "攻撃", "攻撃", "防御", "防御", "充電" }, s.deck, "山札は本編の初期山札 5 枚");
            CollectionAssert.AreEqual(CombatSystem.TerminalCards.StartingDeck(), s.deck);
            Assert.That(s.noStarterItems && s.noMetaBuffs && s.noRewards, Is.True);
            Assert.That(s.lures, Is.EqualTo(0));
            Assert.That(s.onDeath, Is.EqualTo(RunSpec.OnDeathRetry));
            Assert.That(s.setsTutorialDone, Is.True);
            Assert.That(GameRng.TryParseSeed(s.seed, out _), Is.True, "シードは 10 進の数字");
            CollectionAssert.Contains(s.hide, RunSpec.HideEscalation);
        }

        [Test]
        public void Validate_RejectsBossNotLast_AndUnknownEntry()
        {
            Assert.That(new RunSpec { map = new[] { "boss:x", "battle:y" } }.Validate(), Is.Not.Null);
            Assert.That(new RunSpec { map = new[] { "battle:y", "boss:x", "boss:z" } }.Validate(), Is.Not.Null);
            Assert.That(new RunSpec { map = new[] { "road", "boss:x" } }.Validate(), Is.Not.Null);
            Assert.That(new RunSpec { map = new[] { "battle:", "boss:x" } }.Validate(), Is.Not.Null);
            Assert.That(new RunSpec { map = new[] { "walk", "boss:x" } }.Validate(), Is.Null);
            Assert.That(new RunSpec().Validate(), Is.Null, "地図の無い指定は本編の地図");
        }

        [Test]
        public void Parse_BrokenJson_ReturnsNull()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.That(RunSpec.Parse("{ not json", "broken"), Is.Null);
            Assert.That(RunSpec.Parse("{\"map\":[\"boss:a\",\"walk\"]}", "bad"), Is.Null);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        // ── 一本道の地図 ──

        [Test]
        public void Tutorial_Map_IsOneRoad_WolfWalkBoss()
        {
            var s = Tutorial();
            var L = s.BuildLayout(FloorPlan.FarmFloor);
            Assert.That(L.Count, Is.EqualTo(4));
            Assert.That(L.types[L.start], Is.EqualTo(TileType.Outpost));
            Assert.That(L.types[L.goal], Is.EqualTo(TileType.Boss));
            // 道の順: 始点 0 → 2 (狼) → 3 (道) → 1 (ボス)。 枝分かれ無し
            Assert.That(L.roads.Count, Is.EqualTo(3));
            CollectionAssert.AreEquivalent(new[] { 2 }, L.adj[0]);
            CollectionAssert.AreEquivalent(new[] { 0, 3 }, L.adj[2]);
            CollectionAssert.AreEquivalent(new[] { 2, 1 }, L.adj[3]);
            CollectionAssert.AreEquivalent(new[] { 3 }, L.adj[1]);
            Assert.That(L.types[2], Is.EqualTo(TileType.Battle));
            Assert.That(L.types[3], Is.EqualTo(TileType.Outpost), "walk は何も起きない点 (前哨基地の種別)");
            Assert.That(L.storm, Is.False);
            Assert.That(L.mountains.Count, Is.EqualTo(0));
        }

        [Test]
        public void Tutorial_Map_BakesEnemies_AndEnsureEncounterKeepsThem()
        {
            var s = Tutorial();
            RunSpec.Begin(s);      // 整備盤なしの間に作る (FromLayout が整備盤〈剛胆〉を読む・EditMode でメタ進行の部品を作らない)
            var m = MapGenerator.FromLayout(s.BuildLayout(FloorPlan.FarmFloor));
            s.ApplyEncounters(m);
            var wolf = m.GetNodeByIndex(2); var walk = m.GetNodeByIndex(3); var boss = m.GetNode(m.bossNodeId);
            Assert.That(wolf.encounterEnemyA, Is.EqualTo("tut_wolf_snow"));
            Assert.That(boss.encounterEnemyA, Is.EqualTo("boss_layer_tut"));
            Assert.That(walk.encounterEnemyA, Is.Null);
            // 本編の抽選 (ファーム層なら木箱・古い宝箱) で引き直されない
            FloorManager.EnsureEncounter(FloorPlan.FarmFloor, wolf);
            FloorManager.EnsureEncounter(FloorPlan.FarmFloor, boss);
            Assert.That(wolf.encounterEnemyA, Is.EqualTo("tut_wolf_snow"));
            Assert.That(boss.encounterEnemyA, Is.EqualTo("boss_layer_tut"));
            Assert.That(BossIds.IsBoss(boss.encounterEnemyA), Is.True, "ボスはボスの扱い (boss_layer の名前)");
        }

        [Test]
        public void Generate_UsesSpecMap_OnlyWhileSpecActive()
        {
            RunSpec.Begin(Tutorial());
            var m = MapGenerator.Generate(FloorPlan.FarmFloor);
            Assert.That(m.GetNodeByIndex(2).encounterEnemyA, Is.EqualTo("tut_wolf_snow"));
            Assert.That(m.layout.Count, Is.EqualTo(4));
            RunSpec.End();
            // 指定が無ければ本編の 0 層 (5 つの点)。 MapGenerator.Generate は整備盤を読むので、 形だけ FreeMapGenerator で見る
            Assert.That(FreeMapGenerator.Generate(FloorPlan.FarmFloor).Count, Is.EqualTo(5));
            Assert.That(RunSpec.Current, Is.Null);
        }

        [Test]
        public void Line_KeepsFarmAndLinearShapes()
        {
            // 0 層・5 層の 1 本道は共通の Line で作る (2026-11-24 の整理)。 形が前と同じか
            var farm = FreeMapGenerator.Generate(FloorPlan.FarmFloor);
            Assert.That(farm.types[0], Is.EqualTo(TileType.Outpost));
            Assert.That(farm.types[2], Is.EqualTo(TileType.Battle));
            Assert.That(farm.types[3], Is.EqualTo(TileType.Treasure));
            Assert.That(farm.types[4], Is.EqualTo(TileType.Shop));
            Assert.That(farm.types[1], Is.EqualTo(FloorPlan.GoalType(FloorPlan.FarmFloor)));
            Assert.That(farm.home, Is.EqualTo(3));
            var lin = FreeMapGenerator.Generate(FloorPlan.LinearFloor);
            Assert.That(lin.Count, Is.EqualTo(8));
            Assert.That(lin.gate, Is.EqualTo(5));
            Assert.That(lin.types[lin.gate], Is.EqualTo(TileType.Event));
            Assert.That(lin.home, Is.EqualTo(5));
        }

        // ── 負けた時・乱数・指定の欄 ──

        [Test]
        public void RetryOnDeath_OnlyWhileTutorialActive()
        {
            Assert.That(RunSpec.RetryOnDeath, Is.False, "指定なし (本編) はゲームオーバー");
            RunSpec.Begin(Tutorial());
            Assert.That(RunSpec.RetryOnDeath, Is.True);
            Assert.That(RunSpec.NoRewards && RunSpec.NoMeta, Is.True);
            Assert.That(RunSpec.Hides(RunSpec.HideEscalation), Is.True);
            RunSpec.End();
            Assert.That(RunSpec.RetryOnDeath, Is.False);
            Assert.That(RunSpec.Hides(RunSpec.HideEscalation), Is.False);
        }

        [Test]
        public void Seed_IsUsedDuringSpec_AndRestoredAfter()
        {
            Assert.That(GameRng.IsSeeded, Is.False);
            var spec = Tutorial();
            RunSpec.Begin(spec);
            Assert.That(GameRng.IsSeeded, Is.True);
            int a = GameRng.Range(0, 1_000_000, "runspec.test", 1);
            RunSpec.End();
            Assert.That(GameRng.IsSeeded, Is.False, "終わったら前の乱数の状態 (シードなし) へ戻す");
            RunSpec.Begin(spec);
            Assert.That(GameRng.Range(0, 1_000_000, "runspec.test", 1), Is.EqualTo(a), "同じシードなら同じ目");
        }

        [Test]
        public void Tutorial_DiceHintsCharge_MatchTheLesson()
        {
            RunSpec.Begin(Tutorial());
            CollectionAssert.AreEqual(new[] { 1, 2, 1 }, RunSpec.ForcedDice("tut_wolf_snow", 2), "2 ターン目は悪い目");
            Assert.That(RunSpec.ForcedDice("tut_wolf_snow", 1), Is.Null);
            Assert.That(RunSpec.ForcedDice("boss_layer_tut", 2), Is.Null);
            Assert.That(RunSpec.OpeningCharge("tut_wolf_snow"), Is.GreaterThanOrEqualTo(3), "2 ターン目に振り直せる充電 (3 個 × 1 回目 = 3)");
            Assert.That(RunSpec.OpeningCharge("boss_layer_tut"), Is.EqualTo(0));
            CollectionAssert.AreEqual(new[] { "dice", "attack", "go" }, RunSpec.HintParts("tut_wolf_snow", 1, false, false));
            CollectionAssert.AreEqual(new[] { "orb", "reroll" }, RunSpec.HintParts("tut_wolf_snow", 2, true, false));
            CollectionAssert.AreEqual(new[] { "intent", "block" }, RunSpec.HintParts("tut_wolf_snow", 3, false, true));
            Assert.That(RunSpec.HintParts("tut_wolf_snow", 4, false, false), Is.Null);
            CollectionAssert.AreEqual(new[] { "intent" }, RunSpec.HintParts("boss_layer_tut", 2, true, false));
            CollectionAssert.AreEqual(new[] { "intent", "block" }, RunSpec.HintParts("boss_layer_tut", 3, false, true));
            Assert.That(RunSpec.HintParts("boss_layer_tut", 1, false, false), Is.Null);
        }

        // ── 初回の印 ──

        [Test]
        public void TutorialDone_DefaultsFalse_AndSurvivesSave()
        {
            Assert.That(new MetaProgressState().tutorialDone, Is.False);
            Assert.That(JsonUtility.FromJson<MetaProgressState>("{\"tokens\":5}").tutorialDone, Is.False, "印の無い旧いセーブは未了 (初回の PLAY でチュートリアル)");
            var st = new MetaProgressState { tutorialDone = true };
            Assert.That(JsonUtility.FromJson<MetaProgressState>(JsonUtility.ToJson(st)).tutorialDone, Is.True);
        }

        // ── 絵の列の時間割 (slideshow_draft.py と同じ値) ──

        [Test]
        public void Slides_Total_Is32_5Seconds()
        {
            var s = Tutorial();
            Assert.That(Slides.Total(s.slides), Is.EqualTo(780));
            Assert.That(Slides.Total(s.slides) / (float)Slides.Fps, Is.EqualTo(32.5f));
            Assert.That(Slides.Locate(s.slides, 131, out int k, out int i) && k == 0 && i == 131, Is.True);
            Assert.That(Slides.Locate(s.slides, 132, out k, out i) && k == 1 && i == 0, Is.True);
            Assert.That(Slides.Locate(s.slides, 779, out k, out i) && k == 5 && i == 119, Is.True);
            Assert.That(Slides.Locate(s.slides, 780, out _, out _), Is.False);
        }

        [Test]
        public void Slides_PanAndLevel_MatchDraft()
        {
            var win1 = new RunSpec.Slide { mid = 150 };
            int[] at1 = { 0, 1, 2, 3, 6, 59, 119 }, want1 = { 104, 106, 106, 106, 110, 148, 194 };
            for (int n = 0; n < at1.Length; n++) Assert.That(Slides.PanX(win1, at1[n]), Is.EqualTo(want1[n]), "win1 i=" + at1[n]);
            var win4 = new RunSpec.Slide { mid = 100, yoff = -50, arrive = true };
            int[] at4 = { 0, 83, 84, 85, 100, 118, 119 }, want4 = { 54, 116, 118, 118, 128, 130, 132 };
            for (int n = 0; n < at4.Length; n++) Assert.That(Slides.PanX(win4, at4[n]), Is.EqualTo(want4[n]), "win4 i=" + at4[n]);
            Assert.That(Slides.Level(0, Slides.Show), Is.EqualTo(1 / 6.0).Within(1e-12));
            Assert.That(Slides.Level(3, Slides.Show), Is.EqualTo(2 / 6.0).Within(1e-12));
            Assert.That(Slides.Level(18, Slides.Show), Is.EqualTo(1.0));
            Assert.That(Slides.Level(102, Slides.Show), Is.EqualTo(5 / 6.0).Within(1e-12));
            Assert.That(Slides.Level(119, Slides.Show), Is.EqualTo(0.0));
            Assert.That(Slides.Dim(new Color32(255, 100, 7, 255), Slides.Level(5, Slides.Show)), Is.EqualTo(new Color32(85, 33, 2, 255)));
            Assert.That(Slides.AdaptK(47), Is.EqualTo(0.0).Within(1e-12));
            var k0 = Slides.AdaptK(0);
            Assert.That(Slides.Bright(new Color32(0, 100, 200, 255), k0).r, Is.EqualTo(239));
            Assert.That(Slides.Bright(new Color32(0, 0, 100, 255), 0.5).b, Is.EqualTo(199));
        }
    }
}
#endif

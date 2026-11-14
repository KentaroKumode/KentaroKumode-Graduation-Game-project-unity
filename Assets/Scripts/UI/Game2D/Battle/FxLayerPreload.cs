using System.Collections.Generic;
using GameLoop;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// 層に入った時に、 その層で使い得る演出の絵を全部読む係 (2026-11-15・作者の決定「全部を層の頭で読む。 層の頭は完全な待機時間なので、 そこで読む」)。
    /// 実行時に 1 つ作る・シーンを跨いで残る。
    ///
    /// <para><b>いつ</b>: 航行に入った瞬間に、 ランか層が変わっていたら (<see cref="Game2DLoadingScreen"/> がロード画面を出すのと同じ条件・ロード画面の側からも呼ぶ)。
    /// 読み込みは止まらない (<see cref="FxSheet.Pump"/>)。 ロード画面は、 読み終わるまで「クリックで進む」を受けない (<see cref="Busy"/>)。
    /// 層を出る時 (次の層・タイトルへ戻った) に、 次の層で使わない絵を捨てる。</para>
    ///
    /// <para><b>何を</b> (<see cref="NamesFor"/>): 毒・出血・会心・今の武器の家系の通常と会心は常に。 その層で戦い得るボス
    /// (<see cref="FloorManager.BossCandidatesForFloor"/>: 5 層 = 審判官とシュヴァリエ / 6 層 = 灰燼の王 / 8 層 = ヴェスカの 4 段) の、
    /// 技の絵・チャージと待機・大技の全部 (王の烈炎の数ごとの版 a0〜a8・ヴェスカの形態移行と天与の 3 つ × 発動の 4 版を含む・<see cref="BossFx.AllSheetNames"/>) と、 ヴェスカの破片と帯の表。
    /// 層の途中で武器の家系が替わったら (店・報酬)、 その時に武器の絵 2 枚だけ読み足す。</para>
    ///
    /// <para>BOT がランを動かしている間 (<see cref="GameHooks.BotDriving"/>)・バッチモードでは何もしない (戦闘の画面を使わない)。
    /// QA の口 (層を経ずに戦闘へ入る) は <see cref="LoadForEnemyNow"/> で、 その場で止まって読む。</para>
    /// </summary>
    public class FxLayerPreload : MonoBehaviour
    {
        private static FxLayerPreload _instance;
        private static bool _shuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _shuttingDown = false; _instance = null; _lastRun = null; _lastFloor = int.MinValue; _weaponFam = _weaponId = null; QaLayerMs = 0f; QaLayerCount = 0; QaLayerName = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_shuttingDown || _instance != null || Application.isBatchMode) return;
            var go = new GameObject("FxLayerPreload") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FxLayerPreload>();
        }

        private void OnApplicationQuit() { _shuttingDown = true; }
        private void OnDestroy() { if (_instance == this) _instance = null; }

        private static RunState _lastRun; private static int _lastFloor = int.MinValue;
        private static string _weaponFam, _weaponId;

        /// <summary>層の頭で頼んだ絵がまだ読み終わっていないか (ロード画面がクリックを待つ条件)。</summary>
        public static bool Busy => FxSheet.PendingCount > 0;

        /// <summary>確認用: 直近の層の頭の読み込み (頼んでから全部読めるまでの秒・ミリ秒) と枚数と層。</summary>
        public static float QaLayerMs; public static int QaLayerCount; public static string QaLayerName;
        private static System.Diagnostics.Stopwatch _layerSw; private static bool _layerTiming;

        private void Update()
        {
            if (GameHooks.BotDriving || Application.isBatchMode) return;
            var gm = GameManager.Instance;
            if (gm != null)
            {
                var run = gm.Run;
                if (gm.CurrentPhase == GameManager.GamePhase.Title || run == null) { if (_lastRun != null) LeaveLayer(); }
                else
                {
                    if (gm.CurrentPhase == GameManager.GamePhase.MapNavigation) BeginLayer(run);
                    // 層の途中で武器の家系が替わった (店・報酬): 武器の絵 2 枚だけ読み足す
                    if (_lastRun == run && run.equippedWeaponId != _weaponId)
                    {
                        _weaponId = run.equippedWeaponId;
                        string fam = WeaponFamilyOf(_weaponId);
                        if (fam != _weaponFam) { _weaponFam = fam; foreach (var n in WeaponNames(fam)) FxSheet.AddToLayer(n); }
                    }
                }
            }
            FxSheet.Pump();
            if (_layerTiming && !Busy) { _layerTiming = false; QaLayerMs = (float)_layerSw.Elapsed.TotalMilliseconds; }
        }

        /// <summary>層に入った (ランか層が変わっていたら): この層の絵を頼み、 前の層の絵を捨てる。 同じ層なら何もしない。 ロード画面が出る瞬間にも呼ぶ。</summary>
        public static void BeginLayer(RunState run)
        {
            if (run == null || GameHooks.BotDriving || Application.isBatchMode) return;
            if (run == _lastRun && run.currentFloor == _lastFloor) return;
            _lastRun = run; _lastFloor = run.currentFloor;
            _weaponId = run.equippedWeaponId; _weaponFam = WeaponFamilyOf(_weaponId);
            var names = NamesFor(run.Depth, _weaponFam);
            bool vesca = false;
            foreach (var id in FloorManager.BossCandidatesForFloor(run.Depth)) { var k = BossFx.KindOf(id); if (k == BossFx.Kind.Vesca || k == BossFx.Kind.VescaFinal) vesca = true; }
            _layerSw = System.Diagnostics.Stopwatch.StartNew(); _layerTiming = true; QaLayerCount = names.Count; QaLayerName = "floor " + run.currentFloor + " (depth " + run.Depth + ")";
            FxSheet.SetLayerSet(names);
            if (vesca) BossFx.PreloadTables(); else BossFx.DropTables();      // 表は小さい json (破片 0.5 MB)・止まってよい所なのでその場で
        }

        /// <summary>層を出た (タイトルへ戻った・ランが終わった): 層の絵を捨てる (武器・会心・毒・出血は残す)。</summary>
        private static void LeaveLayer()
        {
            _lastRun = null; _lastFloor = int.MinValue; _layerTiming = false;
            FxSheet.SetLayerSet(new string[0]); BossFx.DropTables();
        }

        /// <summary>その層 (深さ) で使い得る絵の名前。 fam = 今の武器の家系 (null = 素手・竜閃など絵の無い武器)。</summary>
        public static List<string> NamesFor(int depth, string fam)
        {
            var l = new List<string> { "poison-tick", "bleed-tick", "crit-hit" };
            l.AddRange(WeaponNames(fam));
            foreach (var id in FloorManager.BossCandidatesForFloor(depth)) foreach (var n in BossFx.AllSheetNames(BossFx.KindOf(id))) if (!l.Contains(n)) l.Add(n);
            return l;
        }

        private static IEnumerable<string> WeaponNames(string fam)
        {
            if (fam == null) yield break;
            yield return "weapon-" + fam + "-normal"; yield return "weapon-" + fam + "-crit";
        }

        /// <summary>武器の絵の家系: items.json の family が剣・斧・短剣・盾の時だけ (id を綴りで切らない)。 素手・竜閃・それ以外は null (手描きの演出のまま)。</summary>
        public static string WeaponFamilyOf(string id)
        {
            if (string.IsNullOrEmpty(id) || id == ItemIds.Ryusen) return null;
            var f = ItemSystem.ItemDatabase.Instance?.GetItem(id)?.family;
            return f == "sword" || f == "axe" || f == "dagger" || f == "shield" ? f : null;
        }

        /// <summary>QA の口 (層を経ずに戦闘へ入る): その敵と今の武器の絵を、 その場で止まって読む (戦闘中に読み込みで止まらないように)。 層の一覧には入れない (戦闘が終われば捨てる)。</summary>
        public static void LoadForEnemyNow(string enemyId, string weaponId)
        {
            var l = new List<string> { "poison-tick", "bleed-tick", "crit-hit" };
            l.AddRange(WeaponNames(WeaponFamilyOf(weaponId)));
            var k = BossFx.KindOf(enemyId);
            l.AddRange(BossFx.AllSheetNames(k));
            FxSheet.LoadAllNow(l);
            if (k == BossFx.Kind.Vesca || k == BossFx.Kind.VescaFinal) BossFx.PreloadTables();
        }
    }
}

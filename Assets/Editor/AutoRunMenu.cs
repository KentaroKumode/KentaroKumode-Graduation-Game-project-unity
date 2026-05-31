using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AutoTest;

namespace AutoTest.EditorTools
{
    /// <summary>
    /// Tools > AutoRun メニュー。SampleScene を開いて PlayMode に入り、
    /// AutoRunner を自動生成して N ラン連続実行 → ログ出力 → PlayMode を抜ける。
    /// </summary>
    [InitializeOnLoad]
    public static class AutoRunMenu
    {
        private const string ScenePath = "Assets/Scenes/SampleScene2.unity";
        private const string PendingKey = "AutoRun.PendingCount";
        private const string ProfileKey = "AutoRun.MetaProfile";   // EditorPrefs (プロファイル選択)
        private const string SweepKey   = "AutoRun.Boss5Sweep";    // SessionState (5Fボス勝率スイープ)
        private const string LambdaKey  = "AutoRun.LambdaFarmSweep"; // SessionState (Λファーム量スイープ)
        private const string LoopKey    = "AutoRun.AutoLoopBatches"; // SessionState (自動周回バッチ数)

        // メニュー項目パス (プロファイル選択)
        private const string MenuProfA = "Tools/AutoRun/プロファイル: 素プレイ (バフOFF・デバフOFF)";
        private const string MenuProfB = "Tools/AutoRun/プロファイル: バフのみ (バフON・デバフOFF)";
        private const string MenuProfC = "Tools/AutoRun/プロファイル: フル設定 (バフON・デバフON)";

        static AutoRunMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        // ===== プロファイル選択 (主軸) =====

        private static MetaProfile CurrentProfile
        {
            get
            {
                int v = EditorPrefs.GetInt(ProfileKey, (int)MetaProfile.BuffOn_DebuffOff);
                return (MetaProfile)v;
            }
            set => EditorPrefs.SetInt(ProfileKey, (int)value);
        }

        [MenuItem(MenuProfA, priority = 50)]
        private static void SetProfA() { CurrentProfile = MetaProfile.BuffOff_DebuffOff; }
        [MenuItem(MenuProfA, validate = true)]
        private static bool SetProfAValidate()
        { Menu.SetChecked(MenuProfA, CurrentProfile == MetaProfile.BuffOff_DebuffOff); return true; }

        [MenuItem(MenuProfB, priority = 51)]
        private static void SetProfB() { CurrentProfile = MetaProfile.BuffOn_DebuffOff; }
        [MenuItem(MenuProfB, validate = true)]
        private static bool SetProfBValidate()
        { Menu.SetChecked(MenuProfB, CurrentProfile == MetaProfile.BuffOn_DebuffOff); return true; }

        [MenuItem(MenuProfC, priority = 52)]
        private static void SetProfC() { CurrentProfile = MetaProfile.BuffOn_DebuffOn; }
        [MenuItem(MenuProfC, validate = true)]
        private static bool SetProfCValidate()
        { Menu.SetChecked(MenuProfC, CurrentProfile == MetaProfile.BuffOn_DebuffOn); return true; }

        // ===== ラン起動 =====

        [MenuItem("Tools/AutoRun/Run 10 runs", priority = 0)]
        public static void Run10() => Launch(10);

        [MenuItem("Tools/AutoRun/Run 100 runs", priority = 1)]
        public static void Run100() => Launch(100);

        [MenuItem("Tools/AutoRun/Run 1000 runs", priority = 2)]
        public static void Run1000() => Launch(1000);

        [MenuItem("Tools/AutoRun/Run custom...", priority = 3)]
        public static void RunCustom()
        {
            int n = AutoRunCountWindow.Ask(50);
            if (n > 0) Launch(n);
        }

        // ===== 自動周回モード (1000ラン × N回、 各バッチ間で L1/L2 自動学習) =====

        [MenuItem("Tools/AutoRun/自動周回: 1000ラン × 5回 (約5-10分)", priority = 5)]
        public static void RunAutoLoop5() => Launch(1000, loopBatches: 5);

        [MenuItem("Tools/AutoRun/自動周回: 1000ラン × 10回 (約10-20分)", priority = 6)]
        public static void RunAutoLoop10() => Launch(1000, loopBatches: 10);

        [MenuItem("Tools/AutoRun/自動周回: 1000ラン × 30回 (約30-60分)", priority = 7)]
        public static void RunAutoLoop30() => Launch(1000, loopBatches: 30);

        [MenuItem("Tools/AutoRun/自動周回: カスタム...", priority = 8)]
        public static void RunAutoLoopCustom()
        {
            var (runs, batches) = AutoLoopConfigWindow.Ask(1000, 10);
            if (runs > 0 && batches > 0) Launch(runs, loopBatches: batches);
        }

        [MenuItem("Tools/AutoRun/5Fボス勝率スイープ", priority = 10)]
        public static void RunBoss5Sweep() => Launch(300, sweep: true);

        [MenuItem("Tools/AutoRun/Λファーム量スイープ (各100ラン)", priority = 11)]
        public static void RunLambdaFarmSweep() => Launch(100, lambdaSweep: true);

        [MenuItem("Tools/AutoRun/Open log folder", priority = 20)]
        public static void OpenLogFolder()
        {
            string dir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "AutoRunLogs"));
            System.IO.Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        private static void Launch(int count, bool sweep = false, bool lambdaSweep = false, int loopBatches = 1)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("AutoRun", "既に PlayMode 中です。停止してから実行してください。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetInt(PendingKey, count);
            SessionState.SetInt(ProfileKey, (int)CurrentProfile);   // プロファイル (Meta系の自動切替に使用)
            SessionState.SetBool(SweepKey, sweep);
            SessionState.SetBool(LambdaKey, lambdaSweep);
            SessionState.SetInt(LoopKey, loopBatches);
            string modeLabel = sweep ? "5Fボス勝率スイープ"
                              : lambdaSweep ? "Λファーム量スイープ"
                              : loopBatches >= 2 ? $"自動周回 {count}ラン × {loopBatches}回"
                              : count + " ラン";
            Debug.Log($"[AutoRunMenu] {modeLabel}予約 (プロファイル: {MetaProfileHelper.DisplayName(CurrentProfile)}) → PlayMode 開始");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;

            int count = SessionState.GetInt(PendingKey, 0);
            if (count <= 0) return;
            int profileInt = SessionState.GetInt(ProfileKey, (int)MetaProfile.BuffOn_DebuffOff);
            bool sweep = SessionState.GetBool(SweepKey, false);
            bool lambdaSweep = SessionState.GetBool(LambdaKey, false);
            int loopBatches = SessionState.GetInt(LoopKey, 1);
            SessionState.EraseInt(PendingKey);
            SessionState.EraseInt(ProfileKey);
            SessionState.EraseBool(SweepKey);
            SessionState.EraseBool(LambdaKey);
            SessionState.EraseInt(LoopKey);

            var go = new GameObject("[AutoRunner]");
            var runner = go.AddComponent<AutoRunner>();
            runner.runCount = count;
            runner.autoStart = false;
            runner.exitPlayModeWhenDone = true;
            // プロファイルから metaPattern / enableAllDebuffs は Begin() 内で自動上書きされる
            runner.metaProfile = (MetaProfile)profileInt;
            runner.simBoss5Sweep = sweep;
            runner.lambdaFarmSweep = lambdaSweep;
            runner.autoLoopBatches = loopBatches;
            runner.Begin();
            string startLabel = sweep ? "5Fボス勝率スイープ"
                              : lambdaSweep ? "Λファーム量スイープ"
                              : loopBatches >= 2 ? $"自動周回 {count}ラン × {loopBatches}回"
                              : count + " ラン";
            Debug.Log($"[AutoRunMenu] AutoRunner 起動 ({startLabel}, プロファイル: {MetaProfileHelper.DisplayName(runner.metaProfile)})");
        }
    }

    /// <summary>カスタム回数入力用の簡易モーダル。</summary>
    public class AutoRunCountWindow : EditorWindow
    {
        private int _value;
        private bool _done;
        private int _result;

        public static int Ask(int initial)
        {
            var w = CreateInstance<AutoRunCountWindow>();
            w._value = initial;
            w.titleContent = new GUIContent("AutoRun");
            w.position = new Rect(Screen.width / 2f, Screen.height / 2f, 260, 90);
            w.ShowModalUtility();
            return w._done ? w._result : 0;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("実行ラン数を入力");
            _value = EditorGUILayout.IntField("ラン数", Mathf.Max(1, _value));
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("実行")) { _result = _value; _done = true; Close(); }
                if (GUILayout.Button("キャンセル")) { _done = false; Close(); }
            }
        }
    }

    /// <summary>自動周回モード用: ラン数とバッチ数の同時入力ウィンドウ。</summary>
    public class AutoLoopConfigWindow : EditorWindow
    {
        private int _runs;
        private int _batches;
        private bool _done;
        private int _resultRuns;
        private int _resultBatches;

        public static (int runs, int batches) Ask(int initialRuns, int initialBatches)
        {
            var w = CreateInstance<AutoLoopConfigWindow>();
            w._runs = initialRuns;
            w._batches = initialBatches;
            w.titleContent = new GUIContent("AutoRun 自動周回");
            w.position = new Rect(Screen.width / 2f, Screen.height / 2f, 320, 140);
            w.ShowModalUtility();
            return w._done ? (w._resultRuns, w._resultBatches) : (0, 0);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("自動周回モード", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "指定ラン数 × 指定バッチ数を連続実行。\n" +
                "各バッチ間で L1 (アイテム勝率) と L2 (パラメータ) が自動学習される。\n" +
                "推奨: 1000ラン × 10-30回。",
                MessageType.Info);
            _runs    = EditorGUILayout.IntField("1バッチのラン数", Mathf.Max(1, _runs));
            _batches = EditorGUILayout.IntField("バッチ数 (周回回数)", Mathf.Max(1, _batches));
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("実行")) { _resultRuns = _runs; _resultBatches = _batches; _done = true; Close(); }
                if (GUILayout.Button("キャンセル")) { _done = false; Close(); }
            }
        }
    }
}

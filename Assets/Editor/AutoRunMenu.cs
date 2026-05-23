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
        private const string MetaKey    = "AutoRun.MetaPattern";   // EditorPrefs (恒久進行)
        private const string DebuffKey  = "AutoRun.EnableAllDebuffs"; // EditorPrefs (デバフ全ON)

        // メニュー項目パス
        private const string MenuMetaCowardly = "Tools/AutoRun/メタ進行: 臆病(全リセット)";
        private const string MenuMetaFull     = "Tools/AutoRun/メタ進行: 全有効化(全段解放)";
        private const string MenuMetaUntouched= "Tools/AutoRun/メタ進行: 保存値そのまま";
        private const string MenuDebuffsOff   = "Tools/AutoRun/メタデバフ: 全OFF (難易度標準)";
        private const string MenuDebuffsOn    = "Tools/AutoRun/メタデバフ: 全ON (Lv1-10, 最高難易度)";

        static AutoRunMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        // ===== メタモード切替 (チェック付きメニュー) =====

        private static AutoRunner.MetaPattern CurrentMetaPattern
        {
            get
            {
                int v = EditorPrefs.GetInt(MetaKey, (int)AutoRunner.MetaPattern.Cowardly);
                return (AutoRunner.MetaPattern)v;
            }
            set => EditorPrefs.SetInt(MetaKey, (int)value);
        }

        [MenuItem(MenuMetaCowardly, priority = 30)]
        private static void SetMetaCowardly()  { CurrentMetaPattern = AutoRunner.MetaPattern.Cowardly; }
        [MenuItem(MenuMetaCowardly, validate = true)]
        private static bool SetMetaCowardlyValidate()
        { Menu.SetChecked(MenuMetaCowardly, CurrentMetaPattern == AutoRunner.MetaPattern.Cowardly); return true; }

        [MenuItem(MenuMetaFull, priority = 31)]
        private static void SetMetaFull()      { CurrentMetaPattern = AutoRunner.MetaPattern.FullProgression; }
        [MenuItem(MenuMetaFull, validate = true)]
        private static bool SetMetaFullValidate()
        { Menu.SetChecked(MenuMetaFull, CurrentMetaPattern == AutoRunner.MetaPattern.FullProgression); return true; }

        [MenuItem(MenuMetaUntouched, priority = 32)]
        private static void SetMetaUntouched() { CurrentMetaPattern = AutoRunner.MetaPattern.Untouched; }
        [MenuItem(MenuMetaUntouched, validate = true)]
        private static bool SetMetaUntouchedValidate()
        { Menu.SetChecked(MenuMetaUntouched, CurrentMetaPattern == AutoRunner.MetaPattern.Untouched); return true; }

        // ===== メタデバフトグル =====

        private static bool EnableAllDebuffsPref
        {
            get => EditorPrefs.GetBool(DebuffKey, false);
            set => EditorPrefs.SetBool(DebuffKey, value);
        }

        [MenuItem(MenuDebuffsOff, priority = 40)]
        private static void SetDebuffsOff() { EnableAllDebuffsPref = false; }
        [MenuItem(MenuDebuffsOff, validate = true)]
        private static bool SetDebuffsOffValidate()
        { Menu.SetChecked(MenuDebuffsOff, !EnableAllDebuffsPref); return true; }

        [MenuItem(MenuDebuffsOn, priority = 41)]
        private static void SetDebuffsOn() { EnableAllDebuffsPref = true; }
        [MenuItem(MenuDebuffsOn, validate = true)]
        private static bool SetDebuffsOnValidate()
        { Menu.SetChecked(MenuDebuffsOn, EnableAllDebuffsPref); return true; }

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

        [MenuItem("Tools/AutoRun/Open log folder", priority = 20)]
        public static void OpenLogFolder()
        {
            string dir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "AutoRunLogs"));
            System.IO.Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        private static void Launch(int count)
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
            SessionState.SetInt(MetaKey, (int)CurrentMetaPattern); // 起動時にスナップショット
            SessionState.SetBool(DebuffKey, EnableAllDebuffsPref);
            Debug.Log($"[AutoRunMenu] {count} ラン予約 (メタ: {CurrentMetaPattern}, デバフ全ON: {EnableAllDebuffsPref}) → PlayMode 開始");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;

            int count = SessionState.GetInt(PendingKey, 0);
            if (count <= 0) return;
            int metaInt = SessionState.GetInt(MetaKey, (int)AutoRunner.MetaPattern.Cowardly);
            bool debuffsOn = SessionState.GetBool(DebuffKey, false);
            SessionState.EraseInt(PendingKey);
            SessionState.EraseInt(MetaKey);
            SessionState.EraseBool(DebuffKey);

            var go = new GameObject("[AutoRunner]");
            var runner = go.AddComponent<AutoRunner>();
            runner.runCount = count;
            runner.autoStart = false;
            runner.exitPlayModeWhenDone = true;
            runner.metaPattern = (AutoRunner.MetaPattern)metaInt;
            runner.enableAllDebuffs = debuffsOn;
            runner.Begin();
            Debug.Log($"[AutoRunMenu] AutoRunner 起動 ({count} ラン, メタ: {runner.metaPattern}, デバフ全ON: {runner.enableAllDebuffs})");
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
}

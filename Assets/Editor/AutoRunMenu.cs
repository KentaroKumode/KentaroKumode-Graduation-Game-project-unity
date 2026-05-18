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

        static AutoRunMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

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
            Debug.Log($"[AutoRunMenu] {count} ラン予約 → PlayMode 開始");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;

            int count = SessionState.GetInt(PendingKey, 0);
            if (count <= 0) return;
            SessionState.EraseInt(PendingKey);

            var go = new GameObject("[AutoRunner]");
            var runner = go.AddComponent<AutoRunner>();
            runner.runCount = count;
            runner.autoStart = false;
            runner.exitPlayModeWhenDone = true;
            runner.Begin();
            Debug.Log($"[AutoRunMenu] AutoRunner 起動 ({count} ラン)");
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

// Unity を使わない .NET ワーカー用の UnityEngine の薄いシム。 ゲームの .cs が使う分だけ (使われていない API は作らない)。
// 振る舞いは Unity の Player (-batchmode) に合わせる: 偽 null (Destroy 後の == null)・Awake/OnEnable は AddComponent の中・
// Start は次のフレームの Update の前・コルーチンは入れ子の IEnumerator を待つ・それ以外の yield は 1 フレーム待つ。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace UnityEngine
{
    // ───── 属性 (中身は使わない・JsonUtility は SerializeField / NonSerialized だけ読む) ─────
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public class PropertyAttribute : Attribute { }
    public sealed class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string t) { } }
    public sealed class HeaderAttribute : PropertyAttribute { public HeaderAttribute(string h) { } }
    public sealed class RangeAttribute : PropertyAttribute { public RangeAttribute(float a, float b) { } }
    public sealed class TextAreaAttribute : PropertyAttribute { public TextAreaAttribute() { } public TextAreaAttribute(int a, int b) { } }
    public sealed class HideInInspector : Attribute { }
    public sealed class DisallowMultipleComponent : Attribute { }
    public sealed class CreateAssetMenuAttribute : Attribute { public string fileName, menuName; public int order; }
    public enum RuntimeInitializeLoadType { AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4 }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeLoadType loadType;
        public RuntimeInitializeOnLoadMethodAttribute() { loadType = RuntimeInitializeLoadType.AfterSceneLoad; }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { loadType = t; }
    }

    // ───── ログ ─────
    public enum LogType { Error = 0, Assert = 1, Warning = 2, Log = 3, Exception = 4 }
    public enum StackTraceLogType { None, ScriptOnly, Full }

    public sealed class Logger
    {
        public bool logEnabled = true;
        public LogType filterLogType = LogType.Log;
        // Unity の Logger.IsLogTypeAllowed と同じ: 例外は filter に関係なく通る
        public bool IsLogTypeAllowed(LogType t)
            => logEnabled && (t == LogType.Exception || filterLogType == LogType.Exception || t <= filterLogType);
    }

    public static class Debug
    {
        public static readonly Logger unityLogger = new Logger();
        public static void Log(object m) => Emit(LogType.Log, m);
        public static void Log(object m, Object ctx) => Emit(LogType.Log, m);
        public static void LogWarning(object m) => Emit(LogType.Warning, m);
        public static void LogWarning(object m, Object ctx) => Emit(LogType.Warning, m);
        public static void LogError(object m) => Emit(LogType.Error, m);
        public static void LogError(object m, Object ctx) => Emit(LogType.Error, m);
        public static void LogException(Exception e) => Emit(LogType.Exception, e.GetType().Name + ": " + e.Message, e.StackTrace);

        internal static void Emit(LogType t, object m, string stack = "")
        {
            if (!unityLogger.IsLogTypeAllowed(t)) return;
            string s = m == null ? "Null" : m.ToString();
            Headless.Engine.WriteLog(t, s, stack);
            Application.RaiseLog(s, stack ?? "", t);
        }
    }

    // ───── 時間・アプリ ─────
    public static class Time
    {
        static readonly Stopwatch _sw = Stopwatch.StartNew();
        public static float realtimeSinceStartup => (float)_sw.Elapsed.TotalSeconds;
        public static double realtimeSinceStartupAsDouble => _sw.Elapsed.TotalSeconds;
        public static float timeScale = 1f;
        // shortcut: 1 フレーム = 1/60 秒と見なす。 待ちの上限 (10 秒など) を数えるのにしか使われていない
        public static float unscaledDeltaTime => 1f / 60f;
        public static float deltaTime => unscaledDeltaTime * timeScale;
        public static int frameCount => Headless.Engine.FrameCount;
    }

    public static class QualitySettings { public static int vSyncCount; }

    public static class Application
    {
        public delegate void LogCallback(string condition, string stackTrace, LogType type);
        public static event LogCallback logMessageReceived;
        public static event Action quitting;
        public static event Func<bool> wantsToQuit;
        public static bool isBatchMode => true;
        public static bool isEditor => false;
        public static bool isPlaying => true;
        public static int targetFrameRate = -1;
        /// <summary>Player と同じく exe の隣の &lt;名前&gt;_Data。 ゲームは Path.Combine(dataPath, "..", "AutoRunLogs") で exe の隣を引く。</summary>
        public static string dataPath => Headless.Engine.DataPath;
        public static string persistentDataPath => Headless.Engine.PersistentDataPath;
        static readonly Dictionary<LogType, StackTraceLogType> _stack = new Dictionary<LogType, StackTraceLogType>();
        public static StackTraceLogType GetStackTraceLogType(LogType t) => _stack.TryGetValue(t, out var v) ? v : StackTraceLogType.ScriptOnly;
        public static void SetStackTraceLogType(LogType t, StackTraceLogType v) => _stack[t] = v;
        public static void Quit() => Quit(0);
        public static void Quit(int exitCode) => Headless.Engine.RequestQuit(exitCode);

        internal static void RaiseLog(string c, string s, LogType t) => logMessageReceived?.Invoke(c, s, t);
        internal static bool RaiseWantsToQuit()
        {
            var h = wantsToQuit; if (h == null) return true;
            foreach (Func<bool> f in h.GetInvocationList()) if (!f()) return false;
            return true;
        }
        internal static void RaiseQuitting() => quitting?.Invoke();
    }

    public static class AudioListener { public static float volume = 1f; public static bool pause; }

    public static class PlayerPrefs
    {
        // ワーカーはメタ進行を PlayerPrefs へ保存しない (GameHooks.MetaPersistenceDisabled)。 プロセス内の辞書だけ・ディスクへ書かない。
        static readonly Dictionary<string, object> _d = new Dictionary<string, object>();
        public static void SetString(string k, string v) => _d[k] = v;
        public static string GetString(string k, string def = "") => _d.TryGetValue(k, out var v) && v is string s ? s : def;
        public static void SetInt(string k, int v) => _d[k] = v;
        public static int GetInt(string k, int def = 0) => _d.TryGetValue(k, out var v) && v is int i ? i : def;
        public static bool HasKey(string k) => _d.ContainsKey(k);
        public static void DeleteKey(string k) => _d.Remove(k);
        public static void Save() { }
    }

    public enum KeyCode { None, Space, Escape, A, D, F, G, L, R, S, T, W, Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9 }
    public static class Input { public static bool GetKeyDown(KeyCode k) => false; }

    // ───── 乱数 (結果の一致は求めない・System.Random で代用) ─────
    public static class Random
    {
        static System.Random _r = new System.Random();
        public static void InitState(int seed) => _r = new System.Random(seed);
        public static int Range(int min, int max) => max <= min ? min : _r.Next(min, max);
        public static float Range(float min, float max) => min + (float)_r.NextDouble() * (max - min);
        public static float value => (float)_r.NextDouble();
    }

    // ───── 数学 ─────
    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Epsilon = float.Epsilon;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Max(params float[] v) { if (v.Length == 0) return 0; float m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] > m) m = v[i]; return m; }
        public static int Max(params int[] v) { if (v.Length == 0) return 0; int m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] > m) m = v[i]; return m; }
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Min(params float[] v) { if (v.Length == 0) return 0; float m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] < m) m = v[i]; return m; }
        public static int Min(params int[] v) { if (v.Length == 0) return 0; int m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] < m) m = v[i]; return m; }
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        // Unity の RoundToInt は Math.Round (偶数丸め) を float→double で呼ぶ
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static float Round(float f) => (float)Math.Round(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static float Ceil(float f) => (float)Math.Ceiling(f);
        public static float Floor(float f) => (float)Math.Floor(f);
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int f) => Math.Abs(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Log(float f) => (float)Math.Log(f);
        public static float Log(float f, float b) => (float)Math.Log(f, b);
        public static float Exp(float f) => (float)Math.Exp(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
        public static bool Approximately(float a, float b)
            => Math.Abs(b - a) < Math.Max(1E-06f * Math.Max(Math.Abs(a), Math.Abs(b)), Epsilon * 8f);
    }

    // ───── オブジェクト・部品 ─────
    public class Object
    {
        internal bool _destroyed;
        public string name { get; set; } = "";
        public HideFlags hideFlags { get; set; }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a._destroyed, bn = ReferenceEquals(b, null) || b._destroyed;
            if (an || bn) return an && bn;
            return ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public static implicit operator bool(Object o) => !(o == null);
        public override bool Equals(object o) => o is Object u ? this == u : (o == null && this == null);
        public override int GetHashCode() => base.GetHashCode();
        public override string ToString() => name;

        public static void DontDestroyOnLoad(Object o) { }
        public static void Destroy(Object o) { if (!(o == null)) Headless.Engine.Destroy(o); }
        public static void Destroy(Object o, float t) => Destroy(o);
        public static void DestroyImmediate(Object o) => Destroy(o);
        public static T FindObjectOfType<T>() where T : Object => Headless.Engine.Find<T>(false);
        public static T FindObjectOfType<T>(bool includeInactive) where T : Object => Headless.Engine.Find<T>(includeInactive);
        public static T[] FindObjectsOfType<T>() where T : Object => Headless.Engine.FindAll<T>(false);
        public static T[] FindObjectsOfType<T>(bool includeInactive) where T : Object => Headless.Engine.FindAll<T>(includeInactive);
    }

    public sealed class GameObject : Object
    {
        internal readonly List<Component> _components = new List<Component>();
        bool _active = true;
        public GameObject() : this("New Game Object") { }
        public GameObject(string name) { this.name = name; transform = new Transform(this); }
        public Transform transform { get; }
        public GameObject gameObject => this;
        public bool activeSelf => _active;
        public bool activeInHierarchy => _active && !_destroyed;
        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        public Component AddComponent(Type t)
        {
            var c = (Component)Activator.CreateInstance(t);
            c.gameObject = this;
            c.name = name;
            _components.Add(c);
            Headless.Engine.Register(c);
            if (_active && c is Behaviour b) Headless.Engine.Awaken(b);
            return c;
        }
        public T GetComponent<T>() where T : class
        {
            foreach (var c in _components) if (!c._destroyed && c is T t) return t;
            return null;
        }
        public void SetActive(bool v)
        {
            if (_active == v) return;
            _active = v;
            foreach (var c in _components.ToArray()) if (c is Behaviour b) { if (v) Headless.Engine.Awaken(b); else Headless.Engine.Disable(b); }
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }

    public sealed class Transform : Component
    {
        internal Transform(GameObject g) { gameObject = g; }
    }

    public class Behaviour : Component
    {
        bool _enabled = true;
        internal bool _awoken, _started, _onEnabled;
        public bool enabled
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; if (value) Headless.Engine.Awaken(this); else Headless.Engine.Disable(this); }
        }
        public bool isActiveAndEnabled => _enabled && !_destroyed && gameObject.activeInHierarchy;
    }

    public class YieldInstruction { }
    public sealed class Coroutine : YieldInstruction { internal bool done; }
    public abstract class CustomYieldInstruction : IEnumerator
    {
        public abstract bool keepWaiting { get; }
        public object Current => null;
        public bool MoveNext() => keepWaiting;
        public void Reset() { }
    }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => Headless.Engine.StartCoroutine(this, routine);
        public void StopCoroutine(Coroutine c) => Headless.Engine.StopCoroutine(c);
        public void StopAllCoroutines() => Headless.Engine.StopAll(this);
        public static void print(object m) => Debug.Log(m);
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }

    /// <summary>Resources の .txt / .json。 中身は読み込み時の文字列。</summary>
    public sealed class TextAsset : Object
    {
        public TextAsset(string text) { this.text = text ?? ""; }
        public string text { get; }
    }

    // 絵・カメラはワーカーでは使わない (型として参照されるだけ)
    public sealed class Sprite : Object { }

    /// <summary>Unity の Resources フォルダ。 ワーカーのビルドが exe の隣の Resources/ へ写した実ファイルを読む。
    /// ScriptableObject は .asset を読まず、 入口 (Program) が登録した作り方で作る。</summary>
    public static class Resources
    {
        static readonly Dictionary<string, Func<Object>> _factories = new Dictionary<string, Func<Object>>(StringComparer.Ordinal);
        static readonly Dictionary<string, Object> _cache = new Dictionary<string, Object>(StringComparer.Ordinal);
        public static void RegisterFactory(string path, Func<Object> make) => _factories[path] = make;

        public static T Load<T>(string path) where T : Object
        {
            if (_cache.TryGetValue(path, out var hit) && !(hit == null)) return hit as T;
            Object made = null;
            if (_factories.TryGetValue(path, out var make)) made = make();
            else if (typeof(T) == typeof(TextAsset))
            {
                string baseP = Path.Combine(Headless.Engine.ResourcesRoot, path.Replace('/', Path.DirectorySeparatorChar));
                foreach (var ext in new[] { ".txt", ".json", ".bytes", ".csv", ".xml" })
                    if (File.Exists(baseP + ext)) { made = new TextAsset(File.ReadAllText(baseP + ext)) { name = Path.GetFileName(path) }; break; }
            }
            if (made != null) _cache[path] = made;
            return made as T;
        }
        public static T[] LoadAll<T>(string path) where T : Object { var one = Load<T>(path); return one == null ? new T[0] : new[] { one }; }
        public static void UnloadUnusedAssets() { }
    }

    // ───── IMGUI (AutoRunner の OnGUI がコンパイルできるだけ・ワーカーは OnGUI を呼ばない) ─────
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color red => new Color(1, 0, 0); public static Color green => new Color(0, 1, 0); public static Color blue => new Color(0, 0, 1);
        public static Color yellow => new Color(1, 0.92f, 0.016f); public static Color white => new Color(1, 1, 1); public static Color black => new Color(0, 0, 0); }
    public struct Rect { public float x, y, width, height; public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; } }
    [Flags] public enum HideFlags { None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }
    public sealed class RectOffset { public RectOffset() { } public RectOffset(int l, int r, int t, int b) { } }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public sealed class GUIStyleState { public Color textColor; }
    public sealed class GUIStyle
    {
        public GUIStyle() { } public GUIStyle(GUIStyle o) { }
        public int fontSize; public FontStyle fontStyle; public TextAnchor alignment; public bool wordWrap, richText;
        public GUIStyleState normal = new GUIStyleState(); public RectOffset padding = new RectOffset();
    }
    public sealed class GUISkin { public GUIStyle box = new GUIStyle(), label = new GUIStyle(); }
    public sealed class GUIContent { public static readonly GUIContent none = new GUIContent(); }
    public static class Screen { public static int width => 1920; public static int height => 1080; }
    public static class GUI
    {
        public static GUISkin skin = new GUISkin();
        public static Color color;
        public static void Label(Rect r, string t) { } public static void Label(Rect r, string t, GUIStyle s) { }
        public static void Box(Rect r, string t) { } public static void Box(Rect r, string t, GUIStyle s) { } public static void Box(Rect r, GUIContent c, GUIStyle s) { }
    }
    public sealed class GUILayoutOption { }
    public static class GUILayout
    {
        public static void Label(string t, params GUILayoutOption[] o) { } public static void Label(string t, GUIStyle s, params GUILayoutOption[] o) { }
        public static void Space(float p) { }
        public static void BeginArea(Rect r) { } public static void BeginArea(Rect r, GUIStyle s) { } public static void EndArea() { }
        public static void BeginVertical(params GUILayoutOption[] o) { } public static void BeginVertical(GUIStyle s, params GUILayoutOption[] o) { } public static void EndVertical() { }
    }
}

namespace UnityEngine.Serialization
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public sealed class FormerlySerializedAsAttribute : Attribute { public FormerlySerializedAsAttribute(string n) { } }
}

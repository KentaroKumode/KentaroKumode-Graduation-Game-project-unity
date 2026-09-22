using System;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace AutoTest.Ultra
{
    /// <summary>
    /// Main-thread presenter for UltraProgressHub. Worker processes/threads report only to
    /// the Unity-free hub; this component owns every GUI and Debug API call.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class UltraProgressHud : MonoBehaviour
    {
        private static UltraProgressHud _instance;

        public static bool ScreenEnabled { get; set; } = true;
        public static bool ConsoleEnabled { get; set; } = true;

        [SerializeField, Min(0.1f)] private float screenRefreshSeconds = 0.5f;
        [SerializeField, Min(1f)] private float consoleMinimumSeconds = 10f;
        [SerializeField, Min(0.1f)] private float consoleOverallPercentStep = 5f;
        [SerializeField, Min(0.1f)] private float consolePhasePercentStep = 10f;
        [SerializeField, Min(5f)] private float consoleHeartbeatSeconds = 30f;

        private UltraConsoleThrottle _consoleThrottle;
        private UltraProgressSnapshot _cachedSnapshot;
        private string _header = string.Empty;
        private string _status = string.Empty;
        private string _detail = string.Empty;
        private string[] _recent = Array.Empty<string>();
        private float _nextScreenRefresh;
        private long _cachedRevision = -1;
        private long _cachedGeneration = -1;

        private GUIStyle _panelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _textStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            ScreenEnabled = true;
            ConsoleEnabled = true;
            UltraProgressHub.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // A production worker writes framed protocol responses. Progress logs from the
            // child process could corrupt that transport, so only the live controller owns HUD/logs.
            if (IsWorkerProcess()) return;
            EnsureCreated();
        }

        /// <summary>Must be called on Unity's main thread.</summary>
        public static UltraProgressHud EnsureCreated()
        {
            if (_instance != null) return _instance;
            _instance = FindObjectOfType<UltraProgressHud>();
            if (_instance != null) return _instance;

            var go = new GameObject("[Ultra Progress HUD]")
            {
                hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave,
            };
            DontDestroyOnLoad(go);
            return go.AddComponent<UltraProgressHud>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            RebuildThrottle();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OnValidate()
        {
            screenRefreshSeconds = Mathf.Max(0.1f, screenRefreshSeconds);
            consoleMinimumSeconds = Mathf.Max(1f, consoleMinimumSeconds);
            consoleOverallPercentStep = Mathf.Max(0.1f, consoleOverallPercentStep);
            consolePhasePercentStep = Mathf.Max(0.1f, consolePhasePercentStep);
            consoleHeartbeatSeconds = Mathf.Max(consoleMinimumSeconds, consoleHeartbeatSeconds);
            if (Application.isPlaying) RebuildThrottle();
        }

        private void Update()
        {
            UltraProgressSnapshot latest = UltraProgressHub.Snapshot();
            if (!latest.IsActive)
            {
                if (_cachedSnapshot != null)
                {
                    _cachedSnapshot = null;
                    _cachedRevision = -1;
                    _cachedGeneration = -1;
                    _consoleThrottle?.Reset();
                }
                return;
            }

            if (_consoleThrottle == null) RebuildThrottle();
            if (ConsoleEnabled && _consoleThrottle.ShouldEmit(latest, Stopwatch.GetTimestamp()))
                LogAlways(UltraProgressFormat.ConsoleLine(latest));

            float now = Time.realtimeSinceStartup;
            bool newJob = latest.Generation != _cachedGeneration;
            bool terminalChange = latest.State != UltraProgressState.Running
                && latest.Revision != _cachedRevision;
            if (!newJob && !terminalChange && now < _nextScreenRefresh) return;

            CacheForScreen(latest);
            _nextScreenRefresh = now + screenRefreshSeconds;
        }

        private void CacheForScreen(UltraProgressSnapshot snapshot)
        {
            _cachedSnapshot = snapshot;
            _cachedGeneration = snapshot.Generation;
            _cachedRevision = snapshot.Revision;
            _header = "ULTRA  " + snapshot.Label + "  [" + snapshot.Bar + "]  " + snapshot.PercentText;

            string phaseProgress = snapshot.PhaseTotal > 0
                ? "  phase=" + UltraProgressFormat.PercentText(snapshot.PhasePercent)
                    + " (" + snapshot.PhaseCompleted + "/" + snapshot.PhaseTotal + ")"
                : string.Empty;
            string workers = snapshot.WorkerCount > 0
                ? "  workers=" + snapshot.ActiveWorkers + "/" + snapshot.WorkerCount
                : string.Empty;
            _status = "state=" + snapshot.State.ToString().ToUpperInvariant()
                + "  phase=" + snapshot.Phase.ToString().ToUpperInvariant()
                + "  runs=" + snapshot.CompletedRuns + "/" + snapshot.TotalRuns
                + phaseProgress + workers;
            _detail = "elapsed=" + snapshot.ElapsedSeconds.ToString("F1") + "s"
                + "  ETA=" + snapshot.EtaSeconds.ToString("F1") + "s"
                + "  last=" + snapshot.LastProgressAgeSeconds.ToString("F1") + "s"
                + (string.IsNullOrEmpty(snapshot.Detail) ? string.Empty : "  " + snapshot.Detail);
            _recent = snapshot.CopyRecent();
        }

        private void OnGUI()
        {
            if (!ScreenEnabled || _cachedSnapshot == null || !_cachedSnapshot.IsActive) return;
            EnsureStyles();

            float width = Mathf.Min(720f, Mathf.Max(440f, Screen.width - 20f));
            int maxRecent = Mathf.Clamp(Mathf.FloorToInt((Screen.height - 120f) / 19f), 0, 20);
            int recentCount = Mathf.Min(maxRecent, _recent.Length);
            float height = 88f + recentCount * 19f;
            float x = Mathf.Max(10f, Screen.width - width - 10f);
            float y = Mathf.Max(10f, Screen.height - height - 10f);
            var panel = new Rect(x, y, width, height);
            GUI.Box(panel, GUIContent.none, _panelStyle);

            float lineX = x + 12f;
            float lineY = y + 8f;
            float lineWidth = width - 24f;
            GUI.Label(new Rect(lineX, lineY, lineWidth, 23f), _header, _headerStyle);
            lineY += 22f;
            GUI.Label(new Rect(lineX, lineY, lineWidth, 21f), _status, _textStyle);
            lineY += 20f;
            GUI.Label(new Rect(lineX, lineY, lineWidth, 21f), _detail, _textStyle);
            lineY += 22f;

            int first = Math.Max(0, _recent.Length - recentCount);
            for (int i = first; i < _recent.Length; i++, lineY += 19f)
                GUI.Label(new Rect(lineX, lineY, lineWidth, 19f), _recent[i], _textStyle);
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null) return;
            Color green = new Color(0.25f, 1f, 0.35f);
            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(12, 12, 10, 10),
            };
            _textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = green },
            };
            _headerStyle = new GUIStyle(_textStyle)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = green },
            };
        }

        private void RebuildThrottle()
        {
            _consoleThrottle = new UltraConsoleThrottle(
                consoleMinimumSeconds,
                consoleOverallPercentStep,
                consolePhasePercentStep,
                consoleHeartbeatSeconds);
        }

        private static void LogAlways(string message)
        {
            ILogger logger = UnityEngine.Debug.unityLogger;
            bool previousEnabled = logger.logEnabled;
            LogType previousFilter = logger.filterLogType;
            try
            {
                logger.logEnabled = true;
                logger.filterLogType = LogType.Log;
                UnityEngine.Debug.Log(message);
            }
            finally
            {
                logger.logEnabled = previousEnabled;
                logger.filterLogType = previousFilter;
            }
        }

        private static bool IsWorkerProcess()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--ultra-worker", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "-ultra-worker", StringComparison.OrdinalIgnoreCase)
                    || (arg != null && arg.StartsWith("--ultra-worker=", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }
    }
}

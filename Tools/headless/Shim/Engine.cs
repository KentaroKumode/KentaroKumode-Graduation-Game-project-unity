// Unity のプレイヤーループの代わり。 1 スレッドで「Start → Update → コルーチン」を 1 フレームとして回す。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Headless
{
    public static class Engine
    {
        public static int FrameCount;
        public static string DataPath = Path.Combine(AppContext.BaseDirectory, "DiceBoundHeadless_Data");
        public static string PersistentDataPath = Path.Combine(AppContext.BaseDirectory, "persistent");
        public static string ResourcesRoot = Path.Combine(AppContext.BaseDirectory, "Resources");
        public static bool QuitRequested;
        public static int ExitCode;
        public static long ExceptionCount;

        static readonly List<Component> _all = new List<Component>();
        static readonly List<Behaviour> _behaviours = new List<Behaviour>();
        static readonly List<Routine> _routines = new List<Routine>();
        static TextWriter _log = TextWriter.Null;

        sealed class Routine
        {
            public MonoBehaviour owner;
            public Coroutine handle;
            public readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
            public Coroutine waitFor;
        }

        // ── ログ ──
        public static void OpenLog(string path)
        {
            if (string.IsNullOrEmpty(path)) { _log = Console.Out; return; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            _log = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16);
        }
        public static void FlushLog() { try { _log.Flush(); } catch { } }
        internal static void WriteLog(LogType t, string msg, string stack)
        {
            if (t == LogType.Exception) ExceptionCount++;
            _log.WriteLine(msg);
            if (t == LogType.Exception || t == LogType.Error) { if (!string.IsNullOrEmpty(stack)) _log.WriteLine(stack); _log.Flush(); }
        }

        // ── 部品 ──
        internal static void Register(Component c)
        {
            _all.Add(c);
            if (c is Behaviour b) _behaviours.Add(b);
        }

        internal static void Awaken(Behaviour b)
        {
            if (b._destroyed || !b.gameObject.activeSelf) return;
            if (!b._awoken)
            {
                b._awoken = true;
                Call(b, "Awake");
                if (b._destroyed) return;
            }
            if (b.enabled && !b._onEnabled)
            {
                b._onEnabled = true;
                Call(b, "OnEnable");
            }
        }

        internal static void Disable(Behaviour b)
        {
            if (!b._onEnabled) return;
            b._onEnabled = false;
            Call(b, "OnDisable");
        }

        internal static void Destroy(Object o)
        {
            if (o is GameObject g)
            {
                foreach (var c in g._components.ToArray()) Destroy(c);
                g._destroyed = true;
                return;
            }
            if (o is Component c2)
            {
                if (c2._destroyed) return;
                if (c2 is Behaviour b)
                {
                    Disable(b);
                    if (b._awoken) Call(b, "OnDestroy");
                    StopAll(b as MonoBehaviour);
                    _behaviours.Remove(b);
                }
                c2._destroyed = true;
                _all.Remove(c2);
                c2.gameObject?._components.Remove(c2);
                return;
            }
            o._destroyed = true;
        }

        internal static T Find<T>(bool includeInactive) where T : Object
        {
            foreach (var c in _all)
                if (!c._destroyed && c is T t && (includeInactive || (c.gameObject.activeInHierarchy && (!(c is Behaviour b) || b.enabled)))) return t;
            return null;
        }
        internal static T[] FindAll<T>(bool includeInactive) where T : Object
            => _all.Where(c => !c._destroyed && c is T && (includeInactive || c.gameObject.activeInHierarchy)).Cast<T>().ToArray();

        // ── メッセージ (Awake など) を名前で呼ぶ・型ごとに引いておく ──
        static readonly Dictionary<(Type, string), MethodInfo> _methods = new Dictionary<(Type, string), MethodInfo>();
        static MethodInfo Method(Type t, string name)
        {
            if (_methods.TryGetValue((t, name), out var m)) return m;
            for (var x = t; x != null && x != typeof(MonoBehaviour); x = x.BaseType)
            {
                m = x.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m != null) break;
            }
            _methods[(t, name)] = m;
            return m;
        }
        static void Call(Behaviour b, string name)
        {
            var m = Method(b.GetType(), name);
            if (m == null) return;
            try
            {
                object r = m.Invoke(b, null);
                if (r is IEnumerator e && b is MonoBehaviour mb) StartCoroutine(mb, e);
            }
            catch (TargetInvocationException ex) { LogException(ex.InnerException ?? ex); }
        }
        static void LogException(Exception e) => Debug.LogException(e);

        // ── コルーチン ──
        internal static Coroutine StartCoroutine(MonoBehaviour owner, IEnumerator e)
        {
            if (e == null) return null;
            var r = new Routine { owner = owner, handle = new Coroutine() };
            r.stack.Push(e);
            _routines.Add(r);
            Step(r);   // Unity と同じく最初の yield まではその場で進む
            return r.handle;
        }
        internal static void StopCoroutine(Coroutine c)
        {
            if (c == null) return;
            foreach (var r in _routines) if (r.handle == c) { r.stack.Clear(); c.done = true; }
        }
        internal static void StopAll(MonoBehaviour owner)
        {
            if (ReferenceEquals(owner, null)) return;
            foreach (var r in _routines) if (ReferenceEquals(r.owner, owner)) { r.stack.Clear(); r.handle.done = true; }
        }

        /// <summary>1 本を次の「1 フレーム待ち」まで進める。 入れ子の IEnumerator はその場で入って、 終われば親へ戻る。</summary>
        static void Step(Routine r)
        {
            if (r.waitFor != null) { if (!r.waitFor.done) return; r.waitFor = null; }
            while (r.stack.Count > 0)
            {
                if (r.owner._destroyed) { r.stack.Clear(); break; }
                var top = r.stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception ex)
                {
                    // Unity: 例外はログに出て、 そのコルーチンだけが止まる
                    LogException(ex);
                    r.stack.Clear();
                    break;
                }
                if (!more) { r.stack.Pop(); continue; }
                object cur = top.Current;
                if (cur is IEnumerator nested) { r.stack.Push(nested); continue; }
                if (cur is Coroutine co) { if (co.done) continue; r.waitFor = co; return; }
                return;   // null・その他は 1 フレーム待つ
            }
            r.handle.done = true;
        }

        public static void Tick()
        {
            FrameCount++;
            foreach (var b in _behaviours.ToArray())
            {
                if (b._destroyed || !b._onEnabled || b._started) continue;
                b._started = true;
                Call(b, "Start");
            }
            foreach (var b in _behaviours.ToArray())
                if (!b._destroyed && b._onEnabled && b._started) Call(b, "Update");
            var snapshot = _routines.ToArray();
            foreach (var r in snapshot) if (!r.handle.done) Step(r);
            _routines.RemoveAll(r => r.handle.done);
        }

        internal static void RequestQuit(int code) { if (QuitRequested) return; QuitRequested = true; ExitCode = code; }

        /// <summary>Unity の終了: wantsToQuit → OnApplicationQuit → quitting。</summary>
        public static void Shutdown()
        {
            foreach (var b in _behaviours.ToArray()) if (!b._destroyed) Call(b, "OnApplicationQuit");
            try { Application.RaiseQuitting(); }
            catch (Exception ex) { LogException(ex); }
            FlushLog();
        }

        /// <summary>[RuntimeInitializeOnLoadMethod] を Unity の段の順で呼ぶ。</summary>
        public static void RunInitializers(Assembly asm, RuntimeInitializeLoadType stage)
        {
            foreach (var t in asm.GetTypes())
                foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var a = m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                    if (a == null || a.loadType != stage || m.GetParameters().Length != 0) continue;
                    try { m.Invoke(null, null); }
                    catch (TargetInvocationException ex) { LogException(ex.InnerException ?? ex); }
                }
        }
    }
}

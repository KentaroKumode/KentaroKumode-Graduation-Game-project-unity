// Unity の JsonUtility と同じ規則で読み書きする。 ずれると items.json などの読み込みが黙って壊れるので、
// 規則は Unity の直列化に合わせる (自己テストは Program の --selftest):
//   ・公開フィールドと [SerializeField] の非公開フィールドだけ (static / readonly / const / [NonSerialized] は除く)・基底クラスの分が先
//   ・プロパティ・辞書・インターフェース・抽象型・UnityEngine.Object の参照は無視
//   ・配列 / List<T> (入れ子の配列は不可)・入れ子は [Serializable] の class / struct だけ・enum は数値
//   ・書き出し: null の文字列は ""、 null の配列 / List は []、 null の入れ子は既定の中身で書く (Unity は null を持てない)
//   ・読み込み: 既定のコンストラクタで作って (フィールドの初期値が残る)、 JSON にある鍵だけを上書きする
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace UnityEngine
{
    public static class JsonUtility
    {
        const int MaxDepth = 10;   // Unity の直列化の入れ子の上限
        static readonly Dictionary<Type, FieldInfo[]> _fields = new Dictionary<Type, FieldInfo[]>();

        internal static FieldInfo[] FieldsOf(Type t)
        {
            lock (_fields)
            {
                if (_fields.TryGetValue(t, out var f)) return f;
                var chain = new List<Type>();
                for (var x = t; x != null && x != typeof(object) && x != typeof(ValueType); x = x.BaseType) chain.Insert(0, x);
                var list = new List<FieldInfo>();
                foreach (var x in chain)
                    foreach (var fi in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (fi.IsInitOnly || fi.IsLiteral || fi.IsNotSerialized) continue;
                        if (!fi.IsPublic && fi.GetCustomAttribute<SerializeField>() == null) continue;
                        if (fi.Name.Contains("k__BackingField")) continue;
                        if (!IsSupported(fi.FieldType, false)) continue;
                        list.Add(fi);
                    }
                f = list.ToArray();
                _fields[t] = f;
                return f;
            }
        }

        static bool IsPrimitive(Type t)
            => t.IsPrimitive || t == typeof(string) || t.IsEnum;

        static Type ElementOf(Type t)
        {
            if (t.IsArray) return t.GetArrayRank() == 1 ? t.GetElementType() : null;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return t.GetGenericArguments()[0];
            return null;
        }

        static bool IsSupported(Type t, bool inCollection)
        {
            if (t == typeof(IntPtr) || t == typeof(UIntPtr)) return false;
            if (IsPrimitive(t)) return true;
            var el = ElementOf(t);
            if (el != null) return !inCollection && ElementOf(el) == null && IsSupported(el, true);
            if (typeof(Object).IsAssignableFrom(t)) return false;   // 絵・資産の参照は JSON に載せない
            if (t.IsAbstract || t.IsInterface || t.IsPointer) return false;
            if (t.IsGenericType && !t.IsValueType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return false;
            return t.GetCustomAttribute<SerializableAttribute>() != null && (t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null || true);
        }

        // ───────────── 書き出し ─────────────
        public static string ToJson(object obj) => ToJson(obj, false);
        public static string ToJson(object obj, bool prettyPrint)
        {
            if (obj == null) return "";
            var sb = new StringBuilder(256);
            WriteObject(sb, obj, obj.GetType(), prettyPrint, 0);
            return sb.ToString();
        }

        static void NewLine(StringBuilder sb, bool pretty, int indent)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', indent * 4);
        }

        static void WriteObject(StringBuilder sb, object obj, Type t, bool pretty, int depth)
        {
            sb.Append('{');
            if (depth > MaxDepth) { sb.Append('}'); return; }
            var fields = FieldsOf(t);
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                NewLine(sb, pretty, depth + 1);
                WriteString(sb, fields[i].Name);
                sb.Append(pretty ? ": " : ":");
                WriteValue(sb, fields[i].GetValue(obj), fields[i].FieldType, pretty, depth + 1);
            }
            if (fields.Length > 0) NewLine(sb, pretty, depth);
            sb.Append('}');
        }

        static void WriteValue(StringBuilder sb, object v, Type t, bool pretty, int depth)
        {
            if (t == typeof(string)) { WriteString(sb, (string)v ?? ""); return; }
            if (t.IsEnum) { sb.Append(Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return; }
            if (t == typeof(bool)) { sb.Append((bool)v ? "true" : "false"); return; }
            if (t == typeof(float)) { sb.Append(FormatFloat((float)v)); return; }
            if (t == typeof(double)) { sb.Append(FormatDouble((double)v)); return; }
            if (t == typeof(char)) { sb.Append(((int)(char)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (t.IsPrimitive) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            var el = ElementOf(t);
            if (el != null)
            {
                sb.Append('[');
                var list = v as IList;
                if (list != null && list.Count > 0)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        NewLine(sb, pretty, depth + 1);
                        WriteValue(sb, list[i], el, pretty, depth + 1);
                    }
                    NewLine(sb, pretty, depth);
                }
                sb.Append(']');
                return;
            }
            if (v == null) v = Activator.CreateInstance(t);   // Unity は null を持てないので既定の中身で書く
            WriteObject(sb, v, t, pretty, depth);
        }

        static string FormatFloat(float f)
        {
            if (float.IsNaN(f)) return "NaN";
            if (float.IsInfinity(f)) return f > 0 ? "Infinity" : "-Infinity";
            return FormatDouble(f);   // Unity は float を double に広げて書く (27.4f → 27.399999618530273)
        }
        // Mono / .NET Framework の "R" と同じ桁数: 15 桁で戻らなければ 17 桁 (.NET Core の "R" は最短桁で float の値が変わる)。
        // 17 桁の末尾は Mono の古い変換と 1〜2 違うことがあるが、 読み戻した double は同じ (どちらも往復が保証される)
        static string LegacyRoundTrip(double d)
        {
            string s = d.ToString("G15", CultureInfo.InvariantCulture);
            return double.Parse(s, CultureInfo.InvariantCulture) == d ? s : d.ToString("G17", CultureInfo.InvariantCulture);
        }
        // Unity は整数値の float / double も 100.0 と書く
        static string WholeDot(string s) => s.IndexOfAny(new[] { '.', 'E', 'e' }) < 0 ? s + ".0" : s;
        static string FormatDouble(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsInfinity(d)) return d > 0 ? "Infinity" : "-Infinity";
            return WholeDot(LegacyRoundTrip(d));
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ───────────── 読み込み ─────────────
        static readonly JsonDocumentOptions _opt = new JsonDocumentOptions
        { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 256 };

        public static T FromJson<T>(string json) => (T)FromJson(json, typeof(T));
        public static object FromJson(string json, Type type)
        {
            if (string.IsNullOrEmpty(json)) return null;
            using (var doc = JsonDocument.Parse(json, _opt))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("JSON parse error: root is not an object.");
                object obj = Activator.CreateInstance(type);
                Fill(obj, type, doc.RootElement, 0);
                return obj;
            }
        }

        public static void FromJsonOverwrite(string json, object target)
        {
            using (var doc = JsonDocument.Parse(json, _opt)) Fill(target, target.GetType(), doc.RootElement, 0);
        }

        static void Fill(object obj, Type t, JsonElement e, int depth)
        {
            if (depth > MaxDepth) return;
            foreach (var f in FieldsOf(t))
            {
                if (!e.TryGetProperty(f.Name, out var v)) continue;
                object cur = f.GetValue(obj);
                if (TryRead(v, f.FieldType, cur, depth, out object val)) f.SetValue(obj, val);
            }
        }

        static bool TryRead(JsonElement v, Type t, object current, int depth, out object result)
        {
            result = null;
            if (t == typeof(string))
            {
                if (v.ValueKind == JsonValueKind.String) { result = v.GetString(); return true; }
                if (v.ValueKind == JsonValueKind.Null) { result = ""; return true; }
                if (v.ValueKind == JsonValueKind.Number) { result = v.GetRawText(); return true; }
                return false;
            }
            if (t == typeof(bool))
            {
                if (v.ValueKind == JsonValueKind.True) { result = true; return true; }
                if (v.ValueKind == JsonValueKind.False) { result = false; return true; }
                if (v.ValueKind == JsonValueKind.Number) { result = v.GetDouble() != 0; return true; }
                return false;
            }
            if (t.IsEnum || t.IsPrimitive)
            {
                if (v.ValueKind != JsonValueKind.Number) return false;
                Type nt = t.IsEnum ? Enum.GetUnderlyingType(t) : t;
                object n;
                if (nt == typeof(float)) n = (float)v.GetDouble();
                else if (nt == typeof(double)) n = v.GetDouble();
                else if (v.TryGetInt64(out long l)) n = Convert.ChangeType(nt == typeof(ulong) ? (object)(ulong)l : l, nt, CultureInfo.InvariantCulture);
                else if (nt == typeof(ulong) && v.TryGetUInt64(out ulong ul)) n = ul;
                else n = Convert.ChangeType(Math.Truncate(v.GetDouble()), nt, CultureInfo.InvariantCulture);
                result = t.IsEnum ? Enum.ToObject(t, n) : n;
                return true;
            }
            var el = ElementOf(t);
            if (el != null)
            {
                if (v.ValueKind != JsonValueKind.Array) return false;
                var items = new List<object>();
                foreach (var x in v.EnumerateArray())
                {
                    if (TryRead(x, el, null, depth + 1, out object item)) items.Add(item);
                    else items.Add(el.IsValueType ? Activator.CreateInstance(el) : (el == typeof(string) ? "" : (IsPrimitive(el) ? null : Activator.CreateInstance(el))));
                }
                if (t.IsArray)
                {
                    var arr = Array.CreateInstance(el, items.Count);
                    for (int i = 0; i < items.Count; i++) arr.SetValue(items[i], i);
                    result = arr;
                }
                else
                {
                    var list = (IList)Activator.CreateInstance(t);
                    foreach (var x in items) list.Add(x);
                    result = list;
                }
                return true;
            }
            if (v.ValueKind != JsonValueKind.Object) return false;
            object o = Activator.CreateInstance(t);
            Fill(o, t, v, depth + 1);
            result = o;
            return true;
        }
    }
}

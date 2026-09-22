using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace AutoTest.Ultra
{
    /// <summary>One captured field.</summary>
    [Serializable]
    public sealed class UltraSnapshotField
    {
        public string name = "";
        public string kind = "";
        /// <summary>Encoded value. Scalars are plain text; collections are JSON.</summary>
        public string value = "";
    }

    [Serializable]
    internal sealed class UltraStringArray { public string[] v = new string[0]; }
    [Serializable]
    internal sealed class UltraIntArray { public int[] v = new int[0]; }

    /// <summary>A complete, restorable copy of <see cref="GameLoop.RunState"/>.
    ///
    /// <para><b>Reflection, not a hand-written field list, and that is the safety property.</b>
    /// The obvious implementation — <c>JsonUtility.ToJson(run)</c> — silently drops every
    /// <c>HashSet</c> and <c>Dictionary</c>, which in this class means owned flags, timed
    /// buffs, permanent debuffs, seen events and shop purchase counts. Nothing would report an
    /// error; the restored run would simply be a different run. Walking the fields means a new
    /// field is captured automatically, and a field of a type nobody taught this class about
    /// <b>throws</b> rather than being skipped.</para>
    ///
    /// <para>Deliberately absent: the RNG. A restored run is a starting point for sampling
    /// futures under a synthetic seed, not a replay of the original — copying the live
    /// generator into a worker is what the run-start-only protocol exists to prevent.</para></summary>
    [Serializable]
    public sealed class UltraRunSnapshot
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public UltraSnapshotField[] fields = new UltraSnapshotField[0];

        private const string KindInt = "int";
        private const string KindLong = "long";
        private const string KindBool = "bool";
        private const string KindFloat = "float";
        private const string KindString = "string";
        private const string KindEnum = "enum";
        private const string KindStringList = "list<string>";
        private const string KindStringSet = "set<string>";
        private const string KindIntList = "list<int>";
        private const string KindStringIntMap = "map<string,int>";
        private const string KindEnumList = "list<enum>";
        private const string KindObjectList = "list<obj>";

        private static FieldInfo[] Fields()
        {
            return FieldsOf(typeof(GameLoop.RunState));
        }

        /// <summary>Public instance fields of any type.
        ///
        /// <para>Exposed because the same capture/restore discipline is needed for combat state
        /// (<c>CombatContext</c> has 225 fields), and re-implementing it there would mean two
        /// codecs that can drift apart — the second one silently dropping a field is exactly the
        /// failure this class exists to prevent.</para></summary>
        internal static FieldInfo[] FieldsOf(Type type)
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        }

        /// <summary>Capture an arbitrary object's fields. <paramref name="label"/> only shapes
        /// the error text, but a snapshot that fails without naming the type is a snapshot
        /// nobody can fix.</summary>
        internal static UltraSnapshotField[] CaptureFields(
            object target, FieldInfo[] fields, string label)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var captured = new List<UltraSnapshotField>(fields.Length);
            foreach (FieldInfo field in fields)
            {
                string kind = KindOf(field.FieldType);
                if (kind == null)
                    throw new NotSupportedException(
                        label + "." + field.Name + " is a " + field.FieldType.Name
                        + ", which the snapshot codec cannot capture. Add support for the type "
                        + "rather than skipping the field — a silently dropped field restores "
                        + "as a different state with no error.");

                captured.Add(new UltraSnapshotField
                {
                    name = field.Name,
                    kind = kind,
                    value = Encode(kind, field.FieldType, field.GetValue(target)),
                });
            }
            // Sorted so the same state always yields byte-identical JSON: reflection field order
            // is not guaranteed stable across runtimes, and an unstable checkpoint hash would
            // make the restore gate meaningless.
            captured.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return captured.ToArray();
        }

        /// <summary>Write captured fields back. Refuses both unknown and missing names — a
        /// partial restore produces a state that never existed, and does it quietly.</summary>
        internal static void RestoreFields(
            object target, FieldInfo[] fields, UltraSnapshotField[] entries, string label)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var live = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            foreach (FieldInfo field in fields) live[field.Name] = field;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < (entries?.Length ?? 0); i++)
            {
                UltraSnapshotField entry = entries[i];
                if (entry == null) continue;
                if (!live.TryGetValue(entry.name, out FieldInfo field))
                    throw new InvalidOperationException(
                        "snapshot names " + label + "." + entry.name + ", which this build does "
                        + "not have — the checkpoint came from a different build");
                field.SetValue(target, Decode(entry.kind, field.FieldType, entry.value));
                seen.Add(entry.name);
            }

            var missing = new List<string>();
            foreach (var pair in live) if (!seen.Contains(pair.Key)) missing.Add(pair.Key);
            if (missing.Count > 0)
            {
                missing.Sort(StringComparer.Ordinal);
                throw new InvalidOperationException(
                    "snapshot is missing " + label + " field(s): "
                    + string.Join(", ", missing.ToArray())
                    + " — restoring would leave them at whatever the target already held");
            }
        }

        /// <summary>Whether every field of <see cref="GameLoop.RunState"/> can be captured.
        /// Used by tests so an unsupported new field fails at build time rather than by
        /// quietly vanishing from checkpoints.</summary>
        public static bool TryDescribeCoverage(out List<string> unsupported)
        {
            unsupported = new List<string>();
            foreach (FieldInfo field in Fields())
                if (KindOf(field.FieldType) == null)
                    unsupported.Add(field.Name + " : " + field.FieldType.Name);
            return unsupported.Count == 0;
        }

        internal static string KindOf(Type type)
        {
            if (type == typeof(int)) return KindInt;
            if (type == typeof(long)) return KindLong;
            if (type == typeof(bool)) return KindBool;
            if (type == typeof(float)) return KindFloat;
            if (type == typeof(string)) return KindString;
            if (type.IsEnum) return KindEnum;

            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                Type[] args = type.GetGenericArguments();

                if (definition == typeof(List<>))
                {
                    if (args[0] == typeof(string)) return KindStringList;
                    if (args[0] == typeof(int)) return KindIntList;
                    if (args[0].IsEnum) return KindEnumList;
                    if (IsSerializableObject(args[0])) return KindObjectList;
                    return null;
                }
                if (definition == typeof(HashSet<>) && args[0] == typeof(string))
                    return KindStringSet;
                if (definition == typeof(Dictionary<,>)
                    && args[0] == typeof(string) && args[1] == typeof(int))
                    return KindStringIntMap;
            }
            return null;
        }

        private static bool IsSerializableObject(Type type)
        {
            return (type.IsClass || (type.IsValueType && !type.IsPrimitive))
                && type.IsDefined(typeof(SerializableAttribute), false);
        }

        // ==================================================================
        //  capture
        // ==================================================================

        public static UltraRunSnapshot Capture(GameLoop.RunState run)
        {
            return new UltraRunSnapshot
            {
                fields = CaptureFields(run, Fields(), "RunState"),
            };
        }

        internal static string Encode(string kind, Type type, object value)
        {
            switch (kind)
            {
                case KindInt: return ((int)value).ToString(CultureInfo.InvariantCulture);
                case KindLong: return ((long)value).ToString(CultureInfo.InvariantCulture);
                case KindBool: return ((bool)value) ? "1" : "0";
                case KindFloat: return ((float)value).ToString("R", CultureInfo.InvariantCulture);
                case KindString: return (string)value ?? "";
                case KindEnum:
                    return Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture);

                case KindStringList:
                case KindStringSet:
                {
                    var items = new List<string>();
                    if (value is IEnumerable enumerable)
                        foreach (object item in enumerable) items.Add((string)item ?? "");
                    // A HashSet has no order of its own; sorting makes the capture reproducible.
                    if (kind == KindStringSet) items.Sort(StringComparer.Ordinal);
                    return JsonUtility.ToJson(new UltraStringArray { v = items.ToArray() });
                }

                case KindIntList:
                {
                    var items = new List<int>();
                    if (value is IEnumerable enumerable)
                        foreach (object item in enumerable) items.Add((int)item);
                    return JsonUtility.ToJson(new UltraIntArray { v = items.ToArray() });
                }

                case KindEnumList:
                {
                    var items = new List<int>();
                    if (value is IEnumerable enumerable)
                        foreach (object item in enumerable) items.Add(Convert.ToInt32(item));
                    return JsonUtility.ToJson(new UltraIntArray { v = items.ToArray() });
                }

                case KindStringIntMap:
                {
                    var keys = new List<string>();
                    if (value is IDictionary map)
                    {
                        foreach (object key in map.Keys) keys.Add((string)key);
                        keys.Sort(StringComparer.Ordinal);   // dictionaries have no order either
                        var values = new int[keys.Count];
                        for (int i = 0; i < keys.Count; i++) values[i] = (int)map[keys[i]];
                        return JsonUtility.ToJson(new UltraStringArray { v = keys.ToArray() })
                             + "\n" + JsonUtility.ToJson(new UltraIntArray { v = values });
                    }
                    return JsonUtility.ToJson(new UltraStringArray())
                         + "\n" + JsonUtility.ToJson(new UltraIntArray());
                }

                case KindObjectList:
                {
                    var items = new List<string>();
                    if (value is IEnumerable enumerable)
                        foreach (object item in enumerable)
                            items.Add(item == null ? "" : JsonUtility.ToJson(item));
                    return JsonUtility.ToJson(new UltraStringArray { v = items.ToArray() });
                }
            }
            throw new NotSupportedException("unencodable kind " + kind);
        }

        // ==================================================================
        //  restore
        // ==================================================================

        /// <summary>Write this snapshot over <paramref name="run"/>.
        ///
        /// <para>Fails on an unknown field name rather than ignoring it: a checkpoint that
        /// mentions something the current build does not have was produced by a different
        /// build, and restoring it partially would create a run that never existed.</para></summary>
        public void RestoreInto(GameLoop.RunState run)
        {
            if (version != CurrentVersion)
                throw new InvalidOperationException(
                    "snapshot version " + version + " != " + CurrentVersion);
            RestoreFields(run, Fields(), fields, "RunState");
        }

        internal static object Decode(string kind, Type type, string encoded)
        {
            encoded = encoded ?? "";
            switch (kind)
            {
                case KindInt: return int.Parse(encoded, CultureInfo.InvariantCulture);
                case KindLong: return long.Parse(encoded, CultureInfo.InvariantCulture);
                case KindBool: return encoded == "1";
                case KindFloat: return float.Parse(encoded, NumberStyles.Float, CultureInfo.InvariantCulture);
                case KindString: return encoded;
                case KindEnum:
                    return Enum.ToObject(type, long.Parse(encoded, CultureInfo.InvariantCulture));

                case KindStringList:
                {
                    string[] items = JsonUtility.FromJson<UltraStringArray>(encoded).v ?? new string[0];
                    var list = (IList)Activator.CreateInstance(type);
                    for (int i = 0; i < items.Length; i++) list.Add(items[i]);
                    return list;
                }

                case KindStringSet:
                {
                    string[] items = JsonUtility.FromJson<UltraStringArray>(encoded).v ?? new string[0];
                    object set = Activator.CreateInstance(type);
                    MethodInfo add = type.GetMethod("Add", new[] { typeof(string) });
                    for (int i = 0; i < items.Length; i++) add.Invoke(set, new object[] { items[i] });
                    return set;
                }

                case KindIntList:
                {
                    int[] items = JsonUtility.FromJson<UltraIntArray>(encoded).v ?? new int[0];
                    var list = (IList)Activator.CreateInstance(type);
                    for (int i = 0; i < items.Length; i++) list.Add(items[i]);
                    return list;
                }

                case KindEnumList:
                {
                    int[] items = JsonUtility.FromJson<UltraIntArray>(encoded).v ?? new int[0];
                    Type element = type.GetGenericArguments()[0];
                    var list = (IList)Activator.CreateInstance(type);
                    for (int i = 0; i < items.Length; i++) list.Add(Enum.ToObject(element, items[i]));
                    return list;
                }

                case KindStringIntMap:
                {
                    int split = encoded.IndexOf('\n');
                    string keyJson = split >= 0 ? encoded.Substring(0, split) : encoded;
                    string valueJson = split >= 0 ? encoded.Substring(split + 1) : "";
                    string[] keys = JsonUtility.FromJson<UltraStringArray>(keyJson).v ?? new string[0];
                    int[] values = string.IsNullOrEmpty(valueJson)
                        ? new int[0] : (JsonUtility.FromJson<UltraIntArray>(valueJson).v ?? new int[0]);
                    var map = (IDictionary)Activator.CreateInstance(type);
                    for (int i = 0; i < keys.Length && i < values.Length; i++) map[keys[i]] = values[i];
                    return map;
                }

                case KindObjectList:
                {
                    string[] items = JsonUtility.FromJson<UltraStringArray>(encoded).v ?? new string[0];
                    Type element = type.GetGenericArguments()[0];
                    var list = (IList)Activator.CreateInstance(type);
                    for (int i = 0; i < items.Length; i++)
                        list.Add(string.IsNullOrEmpty(items[i])
                            ? Activator.CreateInstance(element)
                            : JsonUtility.FromJson(items[i], element));
                    return list;
                }
            }
            throw new NotSupportedException("undecodable kind " + kind);
        }

        /// <summary>Content hash. Two runs with the same state hash the same, so a restore can
        /// be checked against the capture rather than assumed correct.</summary>
        /// <summary>Unit separator. Cannot appear in a field name, a kind, or a JSON payload,
        /// so two different snapshots cannot collide by concatenating into the same string.</summary>
        private const char Separator = (char)0x1F;
        private const char NewLine = '\n';

        public string ContentHash()
        {
            var sb = new System.Text.StringBuilder(4096);
            sb.Append("ultra-run-snapshot-v").Append(version).Append('\n');
            for (int i = 0; i < (fields?.Length ?? 0); i++)
            {
                UltraSnapshotField entry = fields[i];
                if (entry == null) continue;
                // Unit separator: cannot appear in a field name, a kind, or a JSON
                // payload, so two different snapshots cannot collide by concatenation.
                sb.Append(entry.name).Append(Separator)
                  .Append(entry.kind).Append(Separator)
                  .Append(entry.value).Append(NewLine);
            }
            return UltraPortfolioProtocol.Sha256Text(sb.ToString());
        }
    }
}

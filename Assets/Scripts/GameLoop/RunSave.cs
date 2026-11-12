using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// ランの中断セーブ (issue #84・docs/GAME.md §4-5)。 <b>1 枠</b>。 ランの途中で中断し、 後で続きから再開する。
    ///
    /// <para><b>セーブできる時点</b>は航行のマップ (<see cref="GameManager.GamePhase.MapNavigation"/>) で、 窓 (店・イベント・報酬・
    /// 挑戦カード・開幕パッシブの選択) が出ておらず、 追跡者・使い魔と接触していない時だけ (<see cref="GameManager.CanSuspendNow"/>)。
    /// 戦闘・店・イベントの途中は保存しない ── そこまでの状態 (戦闘の文脈・店の棚・イベントの選択肢) を写す口を持たないため。</para>
    ///
    /// <para><b>再開は 1 回きり。</b> 読み込んだらファイルを消す (ローグライクの中断セーブ)。 続けるには、 また中断して保存する。
    /// ゲームを閉じた時にセーブできる時点にいれば、 自動で中断セーブする (<see cref="GameManager"/> の OnApplicationQuit)。</para>
    ///
    /// <para><b>決定性:</b> 乱数 (<see cref="GameRng"/>) は塩とキー別カウンタを、 戦闘の通し番号 (<c>CombatManager._combatSeq</c>) も写す。
    /// 中断せずに続けた場合と同じ乱数の続きになる (BOT の同一シード比較の前提を壊さない)。</para>
    ///
    /// <para><b>RunState はリフレクションで全部写す</b> (旧 Ultra の <c>UltraRunSnapshot</c> (2026-11-13 削除・#135) と同じ考え方)。 手書きの一覧にすると、
    /// 新しく足したフィールドが黙って抜け落ち、 再開したランが「別のラン」になっても誰も気付けない。 写せない型のフィールドが
    /// 足されたらセーブを断って error を出す。 フィールドの名前と型の並びの指紋 (<see cref="Data.schema"/>) が違うセーブは読まずに捨てる。</para>
    ///
    /// <para>メタ進行 (整備パネル・遺物・実績の解除済み) は <c>MetaProgressManager</c> が PlayerPrefs に持つ。 ここはそれと混ぜない
    /// (別ファイル・<see cref="Application.persistentDataPath"/>)。 BOT の実行中 (<see cref="GameHooks.BotDriving"/>・バッチ実行・
    /// 遺物の付与を止めた計測) は保存も読み込みも削除もしない。</para>
    ///
    /// <para><b>書き込みの安全</b> (2026-11-13・G-24): 書きかけ (.tmp) → 控え (.bak) → 本番の順に名前を変える (<see cref="SafeFile"/>)。
    /// 読む時は本番が無い・壊れていれば控え → 書きかけの順に試し、 消す時は 3 つとも消す。</para>
    /// </summary>
    public static class RunSave
    {
        /// <summary>セーブ形式の版。 形式 (下の Data の形・写し方) を変えたら上げる。 違う版のセーブは捨てて新しく始める。</summary>
        public const int Version = 1;
        public const string FileName = "run_suspend.json";

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        // ============================================================
        //  形式
        // ============================================================

        [Serializable]
        public sealed class Field
        {
            public string name = "";
            public string kind = "";
            public string value = "";
            /// <summary>値が null (JsonUtility は null の文字列を "" に変えるので別に持つ)。</summary>
            public bool isNull;
        }

        [Serializable]
        public sealed class Node
        {
            public string id = "";
            public int row, lane, type, resolvedType = -1;   // resolvedType: -1 = 未解決
            public bool visited, activated, isFalseMerchant, revealed, encounterRevealed, freeMap, isFixedEvent;
            /// <summary>深さ 6 の終点に隣り合う「裂け目の記録」の点 (2026-11-13)。 これより前のセーブには無い (読むと false)。</summary>
            public bool isRiftGate;
            public string encounterId, encounterEnemyA, encounterEnemyB;   // null と "" を区別する (下の Has* で)
            public bool hasEncounterId, hasEnemyA, hasEnemyB;
            public int index = -1;
            public int xBits, yBits;
            public List<string> connections = new List<string>();
        }

        [Serializable]
        public sealed class Layout
        {
            public int floor, start, goal, fixedEvent = -1, gate = -1, home;
            public long[] posX = new long[0], posY = new long[0];
            public int[] types = new int[0];
            public int[] adjCounts = new int[0], adjItems = new int[0];
            public int[] roadA = new int[0], roadB = new int[0];
            public long[] mX = new long[0], mY = new long[0], mA = new long[0], mB = new long[0], mCos = new long[0], mSin = new long[0];
            public int[] territory = new int[0];
            public int[] waypoints = new int[0];
            /// <summary>深さ 6 の終点に隣り合う「裂け目の記録」の点 (2026-11-13・FreeMapLayout.riftGate)。 これより前のセーブには無い (読むと空)。</summary>
            public int[] riftGate = new int[0];
        }

        [Serializable]
        public sealed class Map
        {
            public int floor, laneCount, rowCount;
            public string startNodeId = "", bossNodeId = "";
            public bool hasBossNode;
            public string currentNodeId = "";
            public List<Node> nodes = new List<Node>();
            public bool hasLayout;
            public Layout layout = new Layout();
            public FreeMapSim.SavedState sim = new FreeMapSim.SavedState();
        }

        [Serializable]
        public sealed class Data
        {
            public int version = Version;
            /// <summary>RunState のフィールド (名前と型) の指紋。 合わなければ読まない。</summary>
            public string schema = "";
            public string savedAtUtc = "";
            // ── 一覧に出す要約 (中身を読まずに見せる) ──
            public int sumFloor, sumHp, sumMaxHp, sumCoins, sumClass, sumDifficulty;
            // ── 本体 ──
            public List<Field> run = new List<Field>();
            public Map map = new Map();
            public GameRng.State rng = new GameRng.State();
            public int combatSeq;
            public int pursuerFightCount, familiarFightCount;
            public List<string> skillIds = new List<string>();
            public List<string> skillStates = new List<string>();
            public MetaProgression.Achievements.AchievementService.RunProgressState achievements
                = new MetaProgression.Achievements.AchievementService.RunProgressState();
            /// <summary>中断した時の整備パネル・遺物・難易度の指紋。 再開時に違えば知らせる (止めはしない)。</summary>
            public string metaFingerprint = "";
        }

        // ============================================================
        //  ファイル
        // ============================================================

        /// <summary>BOT・計測の最中か (保存・読み込み・削除をしない)。</summary>
        public static bool Disabled
            => Application.isBatchMode || GameHooks.BotDriving || MetaProgression.MetaBuffApplicator.SuppressRelicGrant;

        /// <summary>中断セーブのファイル (本番・控え・書きかけのどれか) があるか (中身が読めるかは <see cref="TryRead"/> で確かめる)。
        /// 控え・書きかけだけが残るのは、 書き込みの途中で落ちた時 (<see cref="Write"/>)。</summary>
        public static bool Exists
        {
            get
            {
                if (Disabled) return false;
                try { return SafeFile.AnyExists(FilePath); } catch { return false; }
            }
        }

        /// <summary>中断セーブを消す (新しいランを始める時・読み込んだ後)。 <b>本番・控え (.bak)・書きかけ (.tmp) を全部消す</b>
        /// ── 控えが残ると、 次の起動で読み込みが控えから復帰して、 終わったランが生き返る。</summary>
        public static void Delete()
        {
            if (Disabled) return;
            if (!SafeFile.DeleteAll(FilePath, out var err)) Debug.LogWarning($"[RunSave] 中断セーブを消せない: {err}");
        }

        /// <summary>ファイルを読む。 無い・壊れている・版や指紋が違う時は false (壊れたファイルは消す)。
        /// <para>本番が無い・読めない (JSON として読めない・版や指紋が違う) 時は、 控え (.bak) → 書きかけ (.tmp) の順に試す
        /// (<see cref="Write"/> の途中で落ちた時に残る)。 控えから読めたら、 それを本番の名前へ戻してログに出す。</para></summary>
        public static bool TryRead(out Data data, out string why)
        {
            data = null; why = null;
            if (Disabled) { why = "BOT の実行中"; return false; }
            string schema = Schema();
            Data parsed = null;
            var res = SafeFile.TryReadFirst(FilePath, json => Validate(json, schema, out parsed), out _, out var used, out var label, out var firstWhy,
                                            w => Debug.LogWarning($"[RunSave] {w}"));
            switch (res)
            {
                case SafeFile.ReadResult.Ok:
                    data = parsed;
                    if (used != FilePath)
                    {
                        Debug.LogWarning($"[RunSave] 本番の中断セーブが無い・壊れているので、 {label} ({Path.GetFileName(used)}) から復帰した");
                        if (!SafeFile.Promote(FilePath, used, out var perr)) Debug.LogWarning($"[RunSave] 復帰した記録を本番の名前へ戻せない (中身は使う): {perr}");
                    }
                    return true;
                case SafeFile.ReadResult.Missing:
                    why = "中断セーブが無い";
                    return false;
                case SafeFile.ReadResult.IoError:
                    // ファイルを開けなかった (他のプロセスが掴んでいる等)。 次の起動で読めるかもしれないので消さない (旧い作りと同じ)。
                    why = firstWhy;
                    return false;
                default:
                    why = firstWhy ?? "形式が壊れている";
                    Discard(why);   // 本番・控え・書きかけのどれも中身が使えない
                    return false;
            }
        }

        /// <summary>JSON を Data にして版と指紋を確かめる。 使えなければ理由、 使えれば null。</summary>
        private static string Validate(string json, string schema, out Data data)
        {
            data = null;
            try { data = JsonUtility.FromJson<Data>(json); }
            catch (Exception e) { data = null; return "形式が壊れている: " + e.Message; }
            if (data == null) return "形式が壊れている";
            if (data.version != Version) { int v = data.version; data = null; return $"版が違う (ファイル {v} / いま {Version})"; }
            if (data.schema != schema) { data = null; return "ランの中身の形が変わった (このセーブを作った版とフィールドが違う)"; }
            return null;
        }

        private static void Discard(string why)
        {
            Debug.LogWarning($"[RunSave] 中断セーブを捨てる: {why}");
            Delete();
        }

        /// <summary>書く。 書きかけ (.tmp) に書いて<b>ディスクまで落としてから</b>、 元を控え (.bak) へ退け、 書きかけを本番へ移し、 控えを消す
        /// (<see cref="SafeFile.WriteAtomic"/>)。 どの時点で落ちても、 本番・控え・書きかけのどれかに完全な記録が残る。</summary>
        public static bool Write(Data data, out string why)
        {
            why = null;
            if (Disabled) { why = "BOT の実行中"; return false; }
            try
            {
                SafeFile.WriteAtomic(FilePath, JsonUtility.ToJson(data));
                return true;
            }
            catch (Exception e) { why = "書けない: " + e.Message; Debug.LogError($"[RunSave] {why}"); return false; }
        }

        // ============================================================
        //  RunState の写し (リフレクション)
        // ============================================================

        private const string KInt = "int", KLong = "long", KBool = "bool", KFloat = "float", KString = "string", KEnum = "enum";
        private const string KStrList = "list<string>", KIntList = "list<int>", KStrSet = "set<string>", KStrIntMap = "map<string,int>", KObjList = "list<obj>";

        [Serializable] private sealed class StrArr { public string[] v = new string[0]; }
        [Serializable] private sealed class IntArr { public int[] v = new int[0]; }

        private static FieldInfo[] RunFields()
        {
            var fs = typeof(RunState).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fs, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            return fs;
        }

        private static string KindOf(Type t)
        {
            if (t == typeof(int)) return KInt;
            if (t == typeof(long)) return KLong;
            if (t == typeof(bool)) return KBool;
            if (t == typeof(float)) return KFloat;
            if (t == typeof(string)) return KString;
            if (t.IsEnum) return KEnum;
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition(); var args = t.GetGenericArguments();
                if (def == typeof(List<>))
                {
                    if (args[0] == typeof(string)) return KStrList;
                    if (args[0] == typeof(int)) return KIntList;
                    if ((args[0].IsClass || (args[0].IsValueType && !args[0].IsPrimitive && !args[0].IsEnum))
                        && args[0].IsDefined(typeof(SerializableAttribute), false)) return KObjList;
                    return null;
                }
                if (def == typeof(HashSet<>) && args[0] == typeof(string)) return KStrSet;
                if (def == typeof(Dictionary<,>) && args[0] == typeof(string) && args[1] == typeof(int)) return KStrIntMap;
            }
            return null;
        }

        /// <summary>RunState のフィールドの名前と型の指紋。 フィールドを足す・消す・型を変えると変わる。</summary>
        public static string Schema()
        {
            var sb = new System.Text.StringBuilder();
            // 型は写し方の種類 + 列挙・要素の型名で表す (FullName は総称型にアセンブリの版が入り、 実行環境で変わり得る)
            foreach (var f in RunFields())
            {
                var t = f.FieldType;
                sb.Append(f.Name).Append(':').Append(KindOf(t) ?? t.Name);
                if (t.IsEnum) sb.Append('/').Append(t.FullName);
                else if (t.IsGenericType) foreach (var a in t.GetGenericArguments()) sb.Append('/').Append(a.FullName);
                sb.Append(';');
            }
            ulong h = 14695981039346656037UL;
            string s = sb.ToString();
            unchecked { for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 1099511628211UL; } }
            return h.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>写せない型のフィールドがあれば例外 (黙って抜かすと、 再開したランが別のランになる)。</summary>
        public static List<Field> CaptureRun(RunState run)
        {
            var list = new List<Field>();
            foreach (var f in RunFields())
            {
                string kind = KindOf(f.FieldType);
                if (kind == null)
                    throw new NotSupportedException($"RunState.{f.Name} ({f.FieldType.Name}) は中断セーブに写せない型。 RunSave.KindOf に型を足すこと (抜かすと再開したランが別のランになる)");
                object v = f.GetValue(run);
                list.Add(new Field { name = f.Name, kind = kind, isNull = v == null, value = v == null ? "" : Encode(kind, v) });
            }
            return list;
        }

        /// <summary>写しを run へ書き戻す。 写しに無いフィールド・知らないフィールドがあれば例外 (半分だけ戻ったランは作らない)。</summary>
        public static void RestoreRun(RunState run, List<Field> fields)
        {
            var live = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            foreach (var f in RunFields()) live[f.Name] = f;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in fields ?? new List<Field>())
            {
                if (e == null) continue;
                if (!live.TryGetValue(e.name, out var f)) throw new InvalidOperationException($"セーブに知らないフィールド RunState.{e.name} がある");
                string kind = KindOf(f.FieldType);
                if (kind != e.kind) throw new InvalidOperationException($"RunState.{e.name} の型が違う ({e.kind} / いま {kind})");
                f.SetValue(run, e.isNull ? null : Decode(kind, f.FieldType, e.value));
                seen.Add(e.name);
            }
            foreach (var name in live.Keys)
                if (!seen.Contains(name)) throw new InvalidOperationException($"セーブに RunState.{name} が無い");
            // 端子の改名 (2026-11-13・〈保持器〉→〈呼び戻し〉): 旧い名前で保存された山札を今の id に揃える
            CombatSystem.TerminalCards.NormalizeIds(run.terminalDeck);
        }

        private static string Encode(string kind, object v)
        {
            switch (kind)
            {
                case KInt: return ((int)v).ToString(Inv);
                case KLong: return ((long)v).ToString(Inv);
                case KBool: return (bool)v ? "1" : "0";
                case KFloat: return BitConverter.ToInt32(BitConverter.GetBytes((float)v), 0).ToString(Inv);   // ビット列 (丸めない)
                case KString: return (string)v;
                case KEnum: return Convert.ToInt64(v, Inv).ToString(Inv);
                case KStrList:
                case KStrSet:
                {
                    var items = new List<string>();
                    if (v is IEnumerable en) foreach (var o in en) items.Add((string)o ?? "");
                    if (kind == KStrSet) items.Sort(StringComparer.Ordinal);
                    return JsonUtility.ToJson(new StrArr { v = items.ToArray() });
                }
                case KIntList:
                {
                    var items = new List<int>();
                    if (v is IEnumerable en) foreach (var o in en) items.Add((int)o);
                    return JsonUtility.ToJson(new IntArr { v = items.ToArray() });
                }
                case KStrIntMap:
                {
                    var map = (IDictionary)v; var keys = new List<string>();
                    foreach (var k in map.Keys) keys.Add((string)k);
                    keys.Sort(StringComparer.Ordinal);
                    var vals = new int[keys.Count];
                    for (int i = 0; i < keys.Count; i++) vals[i] = (int)map[keys[i]];
                    return JsonUtility.ToJson(new StrArr { v = keys.ToArray() }) + "\n" + JsonUtility.ToJson(new IntArr { v = vals });
                }
                case KObjList:
                {
                    var items = new List<string>();
                    if (v is IEnumerable en) foreach (var o in en) items.Add(o == null ? "" : JsonUtility.ToJson(o));
                    return JsonUtility.ToJson(new StrArr { v = items.ToArray() });
                }
            }
            throw new NotSupportedException(kind);
        }

        private static object Decode(string kind, Type t, string s)
        {
            s ??= "";
            switch (kind)
            {
                case KInt: return int.Parse(s, NumberStyles.Integer, Inv);
                case KLong: return long.Parse(s, NumberStyles.Integer, Inv);
                case KBool: return s == "1";
                case KFloat: return BitConverter.ToSingle(BitConverter.GetBytes(int.Parse(s, NumberStyles.Integer, Inv)), 0);
                case KString: return s;
                case KEnum: return Enum.ToObject(t, long.Parse(s, NumberStyles.Integer, Inv));
                case KStrList:
                {
                    var list = (IList)Activator.CreateInstance(t);
                    foreach (var x in JsonUtility.FromJson<StrArr>(s).v ?? new string[0]) list.Add(x);
                    return list;
                }
                case KStrSet:
                {
                    var result = (HashSet<string>)Activator.CreateInstance(t);
                    foreach (var x in JsonUtility.FromJson<StrArr>(s).v ?? new string[0]) result.Add(x);
                    return result;
                }
                case KIntList:
                {
                    var list = (IList)Activator.CreateInstance(t);
                    foreach (var x in JsonUtility.FromJson<IntArr>(s).v ?? new int[0]) list.Add(x);
                    return list;
                }
                case KStrIntMap:
                {
                    int split = s.IndexOf('\n');
                    var keys = JsonUtility.FromJson<StrArr>(split >= 0 ? s.Substring(0, split) : s).v ?? new string[0];
                    var vals = split >= 0 ? (JsonUtility.FromJson<IntArr>(s.Substring(split + 1)).v ?? new int[0]) : new int[0];
                    if (keys.Length != vals.Length) throw new InvalidOperationException("辞書のキーと値の数が合わない");
                    var map = (IDictionary)Activator.CreateInstance(t);
                    for (int i = 0; i < keys.Length; i++) map[keys[i]] = vals[i];
                    return map;
                }
                case KObjList:
                {
                    var elem = t.GetGenericArguments()[0];
                    var list = (IList)Activator.CreateInstance(t);
                    foreach (var x in JsonUtility.FromJson<StrArr>(s).v ?? new string[0])
                        list.Add(string.IsNullOrEmpty(x) ? Activator.CreateInstance(elem) : JsonUtility.FromJson(x, elem));
                    return list;
                }
            }
            throw new NotSupportedException(kind);
        }

        // ============================================================
        //  盤面の写し
        // ============================================================

        private static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);
        private static double Dbl(long b) => BitConverter.Int64BitsToDouble(b);
        private static int FBits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
        private static float Flt(int b) => BitConverter.ToSingle(BitConverter.GetBytes(b), 0);

        public static Map CaptureMap(MapManager mm)
        {
            var map = mm.CurrentMap;
            var m = new Map
            {
                floor = map.floor, laneCount = map.laneCount, rowCount = map.rowCount,
                startNodeId = map.startNodeId ?? "", hasBossNode = map.bossNodeId != null, bossNodeId = map.bossNodeId ?? "",
                currentNodeId = mm.CurrentNode?.id ?? map.startNodeId ?? "",
            };
            foreach (var kv in map.AllNodes)   // 辞書の並び (足した順) のまま写す ── 一覧を回す処理の順序を変えない
            {
                var n = kv.Value;
                m.nodes.Add(new Node
                {
                    id = n.id, row = n.row, lane = n.lane, type = (int)n.type, resolvedType = n.resolvedType.HasValue ? (int)n.resolvedType.Value : -1,
                    visited = n.visited, activated = n.activated, isFalseMerchant = n.isFalseMerchant, revealed = n.revealed,
                    encounterRevealed = n.encounterRevealed, freeMap = n.freeMap, isFixedEvent = n.isFixedEvent, isRiftGate = n.isRiftGate,
                    encounterId = n.encounterId ?? "", hasEncounterId = n.encounterId != null,
                    encounterEnemyA = n.encounterEnemyA ?? "", hasEnemyA = n.encounterEnemyA != null,
                    encounterEnemyB = n.encounterEnemyB ?? "", hasEnemyB = n.encounterEnemyB != null,
                    index = n.index, xBits = FBits(n.x), yBits = FBits(n.y),
                    connections = new List<string>(n.connections),
                });
            }
            if (map.layout != null)
            {
                m.hasLayout = true;
                m.layout = CaptureLayout(map.layout);
                m.sim = mm.Sim != null ? mm.Sim.CaptureState() : null;
            }
            return m;
        }

        private static Layout CaptureLayout(FreeMapLayout l)
        {
            int n = l.Count;
            var o = new Layout { floor = l.floor, start = l.start, goal = l.goal, fixedEvent = l.fixedEvent, gate = l.gate, home = l.home };
            o.posX = new long[n]; o.posY = new long[n]; o.types = new int[n]; o.adjCounts = new int[n];
            var adj = new List<int>();
            for (int i = 0; i < n; i++)
            {
                o.posX[i] = Bits(l.pos[i].x); o.posY[i] = Bits(l.pos[i].y); o.types[i] = (int)l.types[i];
                o.adjCounts[i] = l.adj[i].Count; adj.AddRange(l.adj[i]);
            }
            o.adjItems = adj.ToArray();
            o.roadA = new int[l.roads.Count]; o.roadB = new int[l.roads.Count];
            for (int i = 0; i < l.roads.Count; i++) { o.roadA[i] = l.roads[i].a; o.roadB[i] = l.roads[i].b; }
            int mc = l.mountains.Count;
            o.mX = new long[mc]; o.mY = new long[mc]; o.mA = new long[mc]; o.mB = new long[mc]; o.mCos = new long[mc]; o.mSin = new long[mc];
            for (int i = 0; i < mc; i++)
            {
                var mt = l.mountains[i];
                o.mX[i] = Bits(mt.x); o.mY[i] = Bits(mt.y); o.mA[i] = Bits(mt.a); o.mB[i] = Bits(mt.b); o.mCos[i] = Bits(mt.cos); o.mSin[i] = Bits(mt.sin);
            }
            var terr = new List<int>(l.territory); terr.Sort(); o.territory = terr.ToArray();
            o.waypoints = l.waypoints.ToArray();
            o.riftGate = l.riftGate.ToArray();
            return o;
        }

        public static FloorMap RestoreMap(Map m)
        {
            var map = new FloorMap
            {
                floor = m.floor, laneCount = m.laneCount, rowCount = m.rowCount,
                startNodeId = m.startNodeId, bossNodeId = m.hasBossNode ? m.bossNodeId : null,
            };
            if (m.hasLayout) map.layout = RestoreLayout(m.layout);
            foreach (var s in m.nodes ?? new List<Node>())
            {
                var n = new MapNode(s.id, s.row, s.lane, (TileType)s.type)
                {
                    resolvedType = s.resolvedType >= 0 ? (TileType?)(TileType)s.resolvedType : null,
                    visited = s.visited, activated = s.activated, isFalseMerchant = s.isFalseMerchant, revealed = s.revealed,
                    encounterRevealed = s.encounterRevealed, freeMap = s.freeMap, isFixedEvent = s.isFixedEvent, isRiftGate = s.isRiftGate,
                    encounterId = s.hasEncounterId ? s.encounterId : null,
                    encounterEnemyA = s.hasEnemyA ? s.encounterEnemyA : null,
                    encounterEnemyB = s.hasEnemyB ? s.encounterEnemyB : null,
                    index = s.index, x = Flt(s.xBits), y = Flt(s.yBits),
                    connections = new List<string>(s.connections ?? new List<string>()),
                };
                map.AddNode(n);
            }
            return map;
        }

        private static FreeMapLayout RestoreLayout(Layout o)
        {
            int n = o.types?.Length ?? 0;
            if (o.posX?.Length != n || o.posY?.Length != n || o.adjCounts?.Length != n) throw new InvalidOperationException("航行マップの形の写しが壊れている");
            var l = new FreeMapLayout
            {
                floor = o.floor, start = o.start, goal = o.goal, fixedEvent = o.fixedEvent, gate = o.gate, home = o.home,
                pos = new Vec2[n], types = new TileType[n], adj = new List<int>[n],
            };
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                l.pos[i] = new Vec2(Dbl(o.posX[i]), Dbl(o.posY[i]));
                l.types[i] = (TileType)o.types[i];
                l.adj[i] = new List<int>(o.adjCounts[i]);
                for (int c = 0; c < o.adjCounts[i]; c++) l.adj[i].Add(o.adjItems[k++]);
            }
            for (int i = 0; i < (o.roadA?.Length ?? 0); i++) l.roads.Add((o.roadA[i], o.roadB[i]));
            for (int i = 0; i < (o.mX?.Length ?? 0); i++)
                l.mountains.Add(new Mountain { x = Dbl(o.mX[i]), y = Dbl(o.mY[i]), a = Dbl(o.mA[i]), b = Dbl(o.mB[i]), cos = Dbl(o.mCos[i]), sin = Dbl(o.mSin[i]) });
            if (o.territory != null) foreach (int t in o.territory) l.territory.Add(t);
            if (o.waypoints != null) l.waypoints.AddRange(o.waypoints);
            if (o.riftGate != null) l.riftGate.AddRange(o.riftGate);
            return l;
        }

        // ============================================================
        //  メタの指紋
        // ============================================================

        /// <summary>整備パネルの配点・装備中の遺物・難易度の指紋。 中断中に変えると再開したランの効き方が変わるので、 再開時に知らせるために持つ。</summary>
        public static string MetaFingerprint()
        {
            var st = MetaProgression.MetaProgressManager.Instance?.State;
            if (st == null) return "";
            var sb = new System.Text.StringBuilder();
            if (st.panelRanks != null) foreach (int r in st.panelRanks) sb.Append(r).Append(',');
            sb.Append('|').Append(st.challengeDifficulty).Append('|');
            var relics = st.EquippedRelics();
            if (relics != null) foreach (var r in relics) sb.Append(r != null ? JsonUtility.ToJson(r) : "-").Append(';');
            return sb.ToString();
        }
    }
}

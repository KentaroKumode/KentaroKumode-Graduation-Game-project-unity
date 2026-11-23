// --selftest: JsonUtility のシムが Unity と同じ規則で読むかを確かめる。 ずれると items.json の読み込みが黙って壊れる。
//   1) Unity の規則 (公開 / [SerializeField] / プロパティと辞書は無視 / enum は数値 / null の書き方)
//   2) items.json: シムで読んだ ItemDataJson の各フィールドを、 生の JSON の値と 1 つずつ突き合わせる・品の数
//      (IL2CPP のワーカーの worker.log は「[ItemDatabase] Loaded 285 items from JSON」── 作者のデータで数が変われば合わせて見る)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace DiceBoundHeadless
{
    static class SelfTest
    {
        enum E { A = 0, B = 7 }
        [Serializable] class Inner { public int v = 5; public string s; }
        [Serializable] class Probe
        {
            public int pub = 1; [SerializeField] private int ser = 2; private int hidden = 3; [NonSerialized] public int nonSer = 4;
            public int Prop { get; set; } = 9; public Dictionary<string, int> dict = new Dictionary<string, int> { ["a"] = 1 };
            public E e = E.B; public float f = 100f, g = 27.4f; public string nul; public int[] arr; public List<Inner> list = new List<Inner> { new Inner() };
            public Inner inner; public readonly int ro = 6;
            public int Ser => ser; public int Hidden => hidden;
        }

        static int _fail;
        static void Check(bool ok, string what) { if (!ok) { _fail++; Console.WriteLine("NG " + what); } }

        public static int Run()
        {
            string j = JsonUtility.ToJson(new Probe());
            Check(j == "{\"pub\":1,\"ser\":2,\"e\":7,\"f\":100.0,\"g\":27.399999618530273,\"nul\":\"\",\"arr\":[],\"list\":[{\"v\":5,\"s\":\"\"}],\"inner\":{\"v\":5,\"s\":\"\"}}", "ToJson の規則: " + j);
            var p = JsonUtility.FromJson<Probe>("{\"pub\":10,\"ser\":20,\"hidden\":30,\"nonSer\":40,\"Prop\":90,\"e\":0,\"f\":1.5,\"arr\":[1,2],\"list\":[{\"v\":8}],\"ro\":60}");
            Check(p.pub == 10 && p.Ser == 20 && p.Hidden == 3 && p.nonSer == 4 && p.Prop == 9 && p.e == E.A && p.f == 1.5f && p.ro == 6, "FromJson の規則");
            Check(p.arr.SequenceEqual(new[] { 1, 2 }) && p.list.Count == 1 && p.list[0].v == 8 && p.inner == null, "FromJson の配列・入れ子");   // JSON に無い入れ子は null のまま (Unity 側は未確認・ゲームの読み手はどれも null を見ている)

            string path = Path.Combine(AppContext.BaseDirectory, "Data", "items.json");
            string text = File.ReadAllText(path);
            var parsed = JsonUtility.FromJson<ItemSystem.ItemDataListJson>(text);
            using var doc = JsonDocument.Parse(text);
            var raw = doc.RootElement.GetProperty("items");
            Check(parsed.items != null && parsed.items.Length == raw.GetArrayLength(), "品の数");
            int compared = 0;
            for (int i = 0; i < raw.GetArrayLength(); i++)
                foreach (var f in typeof(ItemSystem.ItemDataJson).GetFields())
                {
                    if (!raw[i].TryGetProperty(f.Name, out var rv)) continue;
                    object got = f.GetValue(parsed.items[i]);
                    string a = got is Array arr ? string.Join(",", arr.Cast<object>().Select(Norm)) : Norm(got);
                    string b = rv.ValueKind == JsonValueKind.Array ? string.Join(",", rv.EnumerateArray().Select(NormRaw)) : NormRaw(rv);
                    if (rv.ValueKind == JsonValueKind.Array && rv.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.Object)) continue;   // 入れ子は件数だけ
                    Check(a == b, $"items[{i}].{f.Name}: シム={a} 生={b}");
                    compared++;
                }
            var db = LoadDb();
            Check(db.GetAllItems().Count == raw.GetArrayLength(), "ItemDatabase の品の数");
            Console.WriteLine($"items={raw.GetArrayLength()} 突き合わせたフィールド={compared} 失敗={_fail}");
            return _fail == 0 ? 0 : 1;
        }

        static ItemSystem.ItemDatabase LoadDb() { Program.RegisterAssets(); return ItemSystem.ItemDatabase.Instance; }
        static string Norm(object o) => o switch
        {
            null => "",
            bool b => b ? "true" : "false",
            float f => ((double)f).ToString("G6", System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString("G6", System.Globalization.CultureInfo.InvariantCulture),
            Enum e => Convert.ToInt64(e).ToString(),
            _ => o.ToString(),
        };
        static string NormRaw(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => e.GetDouble().ToString("G6", System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.Null => "",
            _ => e.GetRawText(),
        };
    }
}

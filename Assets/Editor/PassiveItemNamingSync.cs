using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using InventorySystem;
using UnityEditor;
using UnityEngine;

public static class PassiveItemNamingSync
{
    private static readonly Dictionary<string, string> ExpectedNames = new Dictionary<string, string>
    {
        { "Indomitable_1", "退かずの鉢巻" }, { "Indomitable_2", "傷兵の護符" },
        { "敗残兵の部隊章", "折れ旗の肩章" }, { "Indomitable_4", "最後の軍旗" },
        { "Insight_1", "鑑定所の曇り眼鏡" }, { "測量師の片眼鏡", "測量師の片眼鏡" },
        { "Insight_3", "大穴測量鏡" }, { "Insight_4", "逆さ天蓋鏡" },
        { "継ぎ革の旅靴", "継ぎ革の旅靴" }, { "癒しの靴", "湧水仕込みの旅靴" },
        { "聖路の白靴", "聖路の白靴" }, { "帝国伝令の三日靴", "帝国伝令の三日靴" },
        { "天工開物", "工房主の省打ち帳" }, { "天極", "六面天頂儀" },
        { "二拍目の心臓", "二拍目の心臓" }, { "百鳴りの共振箱", "百鳴りの共振箱" },
        { "末那識の仮面", "末那識の仮面" }, { "医家の反り刃", "医家の反り刃" },
        { "鏡返しの小盾", "鏡返しの小盾" }, { "鎧縫いの針", "鎧縫いの針" },
        { "連環の指輪", "連環の指輪" }, { "剣舞譜「円舞」", "剣舞譜「円舞」" },
        { "エスパーダ・パソドブレ", "剣舞譜「対歩」" }, { "フルーレ・バレエ", "剣舞譜「倒花」" },
        { "ファコン・タンゴ", "剣舞譜「換手」" }, { "ブレイドダンス", "無銘の剣舞譜" },
    };

    [MenuItem("Tools/Items/Sync And Verify Passive Naming")]
    public static void SyncAndVerify()
    {
        var db = ItemDatabase.Instance;
        Require(db != null, "ItemDatabase not found");
        db.LoadFromJson();

        var ids = new HashSet<string>();
        var passiveNames = new HashSet<string>();
        var passiveFlavors = new HashSet<string>();
        int passiveCount = 0;
        foreach (var entry in db.items)
        {
            Require(entry != null && !string.IsNullOrEmpty(entry.itemId), "empty item entry/id");
            Require(ids.Add(entry.itemId), "duplicate item id: " + entry.itemId);
            if (entry.category != ItemCategory.Passive && entry.category != ItemCategory.PassiveItem) continue;
            passiveCount++;
            Require(passiveNames.Add(entry.displayName), "duplicate passive display name: " + entry.displayName);
            Require(entry.displayName.Length <= 14, "passive display name exceeds 14 characters: " + entry.displayName);
            Require(entry.completeData != null, "missing complete data: " + entry.itemId);
            Require(!string.IsNullOrWhiteSpace(entry.completeData.flavorText), "missing flavor: " + entry.itemId);
            Require(entry.completeData.flavorText.Length >= 24, "passive flavor shorter than 24 characters: " + entry.itemId);
            Require(entry.completeData.flavorText.Length <= 100, "passive flavor exceeds 100 characters: " + entry.itemId);
            Require(passiveFlavors.Add(entry.completeData.flavorText), "duplicate passive flavor: " + entry.itemId);
        }

        Require(passiveCount == 137, "passive count " + passiveCount + " != 137");
        foreach (var pair in ExpectedNames)
        {
            var item = db.GetItem(pair.Key);
            Require(item != null, "missing renamed item: " + pair.Key);
            Require(item.displayName == pair.Value,
                pair.Key + " display name " + item.displayName + " != " + pair.Value);
        }

        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
        SyncLegacyCatalogNames(db);
        GeneratePassiveCatalog(db);
        AssetDatabase.Refresh();
        Debug.Log("[PassiveItemNaming] PASS: 137 passives, 26 core mappings, unique IDs/names/flavors, names <=14 chars, flavors 24-100 chars, database/docs synced");
    }

    private static void SyncLegacyCatalogNames(ItemDatabase db)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string[] relativePaths = { "docs/items_catalog.md", "docs/items_uniques.md" };
        foreach (string relative in relativePaths)
        {
            string path = Path.Combine(projectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;
            string text = File.ReadAllText(path, Encoding.UTF8);
            foreach (var entry in db.items)
            {
                if (entry.category != ItemCategory.Passive && entry.category != ItemCategory.PassiveItem) continue;
                string id = Regex.Escape(entry.itemId);
                string name = entry.displayName.Replace("$", "$$");
                text = Regex.Replace(text, "(?m)^(\\| " + id + " \\| )[^|]+(\\|)", "$1" + name + " $2");
                text = Regex.Replace(text, "(?m)^(\\| [BSGL] \\| " + id + " \\| )[^|]+(\\|)", "$1" + name + " $2");
            }
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }

    private static void GeneratePassiveCatalog(ItemDatabase db)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string path = Path.Combine(projectRoot, "docs", "passive_items_catalog.md");
        var sb = new StringBuilder();
        sb.AppendLine("# パッシブアイテム一覧（自動生成）");
        sb.AppendLine();
        sb.AppendLine("正本: `Assets/Data/InventorySystem/items.json`");
        sb.AppendLine();
        sb.AppendLine("内部 ID はセーブと効果実装の互換用であり、プレイヤーには表示しない。");

        ItemRarity[] rarities = { ItemRarity.BRONZE, ItemRarity.SILVER, ItemRarity.GOLD, ItemRarity.LEGENDARY };
        foreach (var rarity in rarities)
        {
            sb.AppendLine();
            sb.AppendLine("## " + rarity);
            sb.AppendLine();
            sb.AppendLine("| ID | 名前 | 効果 | フレーバー |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var entry in db.items)
            {
                if (entry.rarity != rarity) continue;
                if (entry.category != ItemCategory.Passive && entry.category != ItemCategory.PassiveItem) continue;
                var item = entry.completeData;
                sb.Append("| ").Append(Escape(entry.itemId)).Append(" | ")
                  .Append(Escape(entry.displayName)).Append(" | ")
                  .Append(Escape(entry.description)).Append(" | ")
                  .Append(Escape(item != null ? item.flavorText : "")).AppendLine(" |");
            }
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Escape(string value)
        => (value ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[PassiveItemNaming] " + message);
    }
}

using System.Collections.Generic;
using InventorySystem;
using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// 種火 r3 (キーワード開幕所持) 用のピッカー。
    /// 2026-07-25 v6. 分類テーブルは docs/tools/item-dex.html の KW_MAP と一致 (正本の同期対象)。
    /// パッシブ internalName から充電/臨界/毒/出血に紐付ける。
    ///
    /// ピック仕様: 該当キーワードのパッシブを 1 つ以上持つアイテムから 1 個ランダム。
    /// **LEGENDARY 以上は抽選対象外** (2026-07-28 に BRONZE 固定から変更)。
    /// 既に所持済み or 一度取得済み (seenPassiveItemIds) は除外。
    /// </summary>
    public static class SparkStarterPicker
    {
        private static readonly Dictionary<string, HashSet<string>> KwToInternalNames = new()
        {
            ["charge"] = new HashSet<string>
            {
                "工廠の材料箱","焦げ柄の点火スパナ","逆さ避雷針","過負荷チューナー","雷壺",
                "Thundercloud","無限モーター","呼雷粉",
            },
            ["rinkai"] = new HashSet<string>
            {
                "余分に乾いた火口箱","炉番の火床外套","過熱石","溢れを取る鋳型","帳簿外の連鎖爆発",
                "朝にも熱い竈","気短な早沸かし釜","恒熱の炉壁","七日目の熾",
                "三度不良の銅線",
            },
            ["poison"] = new HashSet<string>
            {
                "緑染みの下拵え小刀","岸上げ用の麻痺瓶","抜かずの控え脇差","跡地庭師の霧吹き","主より長い香炉",
                "SlowVenomCurse","素手禁じの蛇血瓶","CorrosiveStrike","石抜きの毒指輪",
            },
            ["bleed"] = new HashSet<string>
            {
                "Sting","末頁の血花太刀","血日に澄む佩玉","退路喰いの狂刃","手負い追いの山刀",
                "医書裏の開き針","GrievousI","GrievousII","先血の腕輪","血令",
            },
        };

        /// <summary>与えられたパッシブアイテムが keyword に該当するか。</summary>
        public static bool HasKeyword(CompleteItemData item, string keyword)
        {
            if (item == null || item.passiveSkills == null) return false;
            if (!KwToInternalNames.TryGetValue(keyword, out var set)) return false;
            foreach (var p in item.passiveSkills)
                if (p != null && set.Contains(p.internalName)) return true;
            return false;
        }

        /// <summary>keyword に該当するアイテムから 1 個ランダムピック。 所持済/取得済は除外。 該当なし null。
        /// **抽選対象は LEGENDARY を除く全等級**（2026-07-28 に BRONZE 固定から変更）。
        /// 開幕から LEG が出るとビルドが確定しすぎるため、 そこだけ落としてある。</summary>
        public static string PickStarterFor(string keyword, RunState run)
        {
            var db = ItemDatabase.Instance;
            if (db == null) return null;
            var pool = db.GetItemsByCategory(ItemCategory.Passive) ?? db.GetItemsByCategory(ItemCategory.PassiveItem);
            if (pool == null || pool.Count == 0) return null;

            var candidates = new List<CompleteItemData>();
            var owned = new HashSet<string>();
            if (run?.ownedPassiveItems != null) foreach (var x in run.ownedPassiveItems) owned.Add(x);
            if (run?.seenPassiveItemIds != null) foreach (var x in run.seenPassiveItemIds) owned.Add(x);

            foreach (var it in pool)
            {
                if (it == null) continue;
                // LEGENDARY のみ除外。 MYTHIC/DIVINE は 7層ヴェスカ専用でプールに存在しないが、
                // 万一混ざっても開幕所持させないよう併せて弾く。
                if (it.rarity == ItemRarity.LEGENDARY
                    || it.rarity == ItemRarity.MYTHIC
                    || it.rarity == ItemRarity.DIVINE) continue;
                if (owned.Contains(it.internalName)) continue;
                if (!HasKeyword(it, keyword)) continue;
                candidates.Add(it);
            }
            if (candidates.Count == 0) return null;
            var pick = candidates[GameLoop.GameRng.RangeAuto("SparkStarterPicker.1", 0, candidates.Count)];
            return pick.internalName;
        }

        /// <summary>抽選重みバイアス用: アイテムがどのキーワードに属するかを返す (最初にヒットしたもの)。
        /// ShopManager の Tier 抽選前に呼び、該当キーワードの r1 バフから重みを乗算する用途。</summary>
        public static string KeywordOf(CompleteItemData item)
        {
            if (item == null) return null;
            foreach (var kv in KwToInternalNames)
                if (HasKeyword(item, kv.Key)) return kv.Key;
            return null;
        }
    }
}


using System.Collections.Generic;
using ItemSystem;
using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// 種火 r3 (キーワード開幕所持) 用のピッカー。
    /// 2026-07-25 v6. 2026-11-07〜 品の軸 (items.json の axis) で充電/臨界/毒/出血に紐付ける。
    ///
    /// ピック仕様: 該当キーワードのパッシブを 1 つ以上持つアイテムから 1 個ランダム。
    /// **LEGENDARY 以上は抽選対象外** (2026-07-28 に BRONZE 固定から変更)。
    /// 既に所持済み or 一度取得済み (seenPassiveItemIds) は除外。
    /// </summary>
    public static class SparkStarterPicker
    {
        /// <summary>種火のキーワード → 品の軸 (items.json の axis)。
        /// 2026-11-07: 旧は品の id を直書きした表で、 軸とずれていた (工廠の材料箱・点火スパナが充電、 雷神槍が臨界、
        /// 雷鯰の棘・蛇血瓶が毒など) うえに新しい品が入っていなかった。 構造は items.json から読む (CLAUDE.md)。</summary>
        private static readonly Dictionary<string, string> KwToAxis = new()
        {
            ["charge"] = ItemSystem.PassiveSkills.AxisRules.Charge,
            ["rinkai"] = ItemSystem.PassiveSkills.AxisRules.Heat,
            ["poison"] = ItemSystem.PassiveSkills.AxisRules.Poison,
            ["bleed"]  = ItemSystem.PassiveSkills.AxisRules.Bleed,
            ["crit"]   = ItemSystem.PassiveSkills.AxisRules.Crit,
            ["shield"] = ItemSystem.PassiveSkills.AxisRules.Shield,
        };

        /// <summary>与えられたパッシブアイテムが keyword に該当するか (品の軸で判定)。</summary>
        public static bool HasKeyword(CompleteItemData item, string keyword)
            => item != null && KwToAxis.TryGetValue(keyword, out var axis) && item.axis == axis;

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
            foreach (var kv in KwToAxis)
                if (HasKeyword(item, kv.Key)) return kv.Key;
            return null;
        }
    }
}


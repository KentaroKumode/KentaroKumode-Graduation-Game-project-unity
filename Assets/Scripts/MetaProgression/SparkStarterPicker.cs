using System.Collections.Generic;
using ItemSystem;
using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// 種火のキーワード ("charge" など・<see cref="MetaPanel.SparkKeyword"/>) と品の軸 (items.json の axis) の対応。
    /// 店の秘宝の枠の出現の重み (ShopManager.PickWithSparkBias → <see cref="KeywordOf"/>) が読む。
    /// (旧: 種火 r3 の開幕の品の抽選 PickStarterFor は、 2026-11-20 に種火を作戦の枠のモジュールにした時に外した)
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
            // 新しい軸の種火 (山札・役) はここにキーワード → 軸を足す (MetaPanel.SparkKeyword と同じ綴り)
        };

        /// <summary>与えられたパッシブアイテムが keyword に該当するか (品の軸で判定)。</summary>
        public static bool HasKeyword(CompleteItemData item, string keyword)
            => item != null && KwToAxis.TryGetValue(keyword, out var axis) && item.axis == axis;

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


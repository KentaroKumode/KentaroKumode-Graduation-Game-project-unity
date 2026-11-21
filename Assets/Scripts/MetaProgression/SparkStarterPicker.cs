using System.Collections.Generic;
using ItemSystem;
using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// 種火のキーワード ("charge" など・<see cref="MetaPanel.SparkKeyword"/>) と品の軸 (items.json の axis) の対応。
    /// ランの始めの出現の枠 (RunState.lockedAxes の 1 つ目) が読む (2026-11-21・旧 店の出現の重み ×2)。
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
            ["deck"]   = ItemSystem.PassiveSkills.AxisRules.Deck,   // 2026-11-21
            ["role"]   = ItemSystem.PassiveSkills.AxisRules.Role,
        };

        /// <summary>種火のキーワード → 軸 (知らないキーワード・null は null)。 ランの始めの出現の枠 (MetaBuffApplicator.GetSparkAxis) が読む。</summary>
        public static string AxisOf(string keyword)
            => keyword != null && KwToAxis.TryGetValue(keyword, out var axis) ? axis : null;
    }
}


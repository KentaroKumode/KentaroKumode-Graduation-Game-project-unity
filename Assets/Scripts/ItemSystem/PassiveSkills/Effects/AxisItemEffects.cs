namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  軸の品 (2026-11-06 → 11-07 組み直し)。 設計は docs/GAME.md §8-7。
    //
    //  再編 (2026-10-31) の品は大半が「軸 X を軸 Y に換える」変換器で、 安い変換器を全部買うと
    //  毒・出血・臨界・主攻撃が 1 つの機関に繋がった (クリアしたランの 64% が 3 経路を同時に使っていた)。
    //  軸 (会心・出血・毒・臨界・充電・シールド) ごとに 3 種の品を置く:
    //    燃料   — 配線を払って資源を得る。
    //    ① 数を参照 — その軸の品の数 (心眼 / 血晶 / 瘴気 / 炉温 / 電流 / 堅牢) に応じて資源が増える。
    //    ②【規則】— その軸の性質を変える。 強さは同じ数で決まる (① と連動して、 深く積むほど効く)。
    //  出目 (攻撃に挿した出目 +%) はどの構成にも効くので軸にしない。
    // ============================================================

    /// <summary>軸の品の種類 (BOT の購入判断が読む・2026-11-07)。 ITT は品をランダムに配った平均で序列を作るので、
    /// 絞った構成でだけ価値が出る ① 数を参照・②【規則】は低く出る。 Super はこの表で「軸が深ければ買う」を判断する。</summary>
    public static class AxisCatalog
    {
        public enum Kind { Count, Rule }
        public static readonly System.Collections.Generic.Dictionary<string, (string axis, Kind kind)> Items
            = new System.Collections.Generic.Dictionary<string, (string, Kind)>
        {
            ["腐れ沼の苔玉"] = (AxisRules.Poison, Kind.Rule),
            // 賽目 (役の軸・2026-11-21): ① 賽目を読む・②【規則】
            [GameLoop.ItemIds.RoleNotchBoard] = (AxisRules.Role, Kind.Count), [GameLoop.ItemIds.RoleChipped] = (AxisRules.Role, Kind.Count),
            [GameLoop.ItemIds.RoleMirror] = (AxisRules.Role, Kind.Rule), [GameLoop.ItemIds.RoleOmni] = (AxisRules.Role, Kind.Rule),
        };
    }

}

namespace ItemSystem.PassiveSkills
{
    /// <summary><b>軸と、 軸の品の数 (2026-11-07・GAME.md §8-7)。</b>
    ///
    /// <para>品の軸は items.json の <c>axis</c>。 品が<b>読む</b>資源 (発動の条件) の軸、 条件の無い品は作る資源の軸。
    /// 出目 (攻撃に挿した出目 +%) はどの構成にも効くので軸にしない。</para>
    ///
    /// <para>軸の品を持っている数には呼び名がある (心眼 / 血晶 / 瘴気 / 炉温 / 電流 / 堅牢)。
    /// 「深く積むと得」は全体の規則ではなく、 この数を読む品 (各軸 ① 数を参照・②【規則】) が配る。
    /// 2026-11-06 の「深さで資源の効き方を変える規則」(閾値・毒の上限・出血の減衰・シールド・会心倍率) は 11-07 に撤去した。</para></summary>
    public static class AxisRules
    {
        public const string Crit = "会心", Bleed = "出血", Poison = "毒", Heat = "臨界", Charge = "充電", Shield = "シールド", Gold = "金";

        /// <summary>軸の品の数の呼び名 (説明文・画面に出す)。</summary>
        public static string WordOf(string axis) => axis switch
        {
            Crit => "心眼", Bleed => "血晶", Poison => "瘴気", Heat => "炉温", Charge => "電流", Shield => "堅牢", _ => "",
        };

        /// <summary>出血の自然減衰 (2026-11-07): 毎ターン 1 + 出血 / (10 + 品の上乗せ)。 上限ではなく、
        /// 1 ターンの付与 P に対して約 (10 + 上乗せ) × (P − 1) で落ち着く。 毒 (約 2P) より高く遅い ＝ 溜まりにくく減りにくい。</summary>
        public const int BleedDecayDivisor = 10;
        public static int BleedDecay(int stacks, int divisorBonus)
            => stacks <= 0 ? 0 : System.Math.Min(stacks, 1 + stacks / (BleedDecayDivisor + System.Math.Max(0, divisorBonus)));

        /// <summary>軸の品の数を数える (所持パッシブの axis)。 戦闘中は CombatContext.AxisDepth がこれを 1 度だけ呼ぶ。</summary>
        public static int Count(GameLoop.RunState run, string axis)
        {
            if (run?.ownedPassiveItems == null || string.IsNullOrEmpty(axis)) return 0;
            var db = ItemDatabase.Instance;
            if (db == null) return 0;
            int n = 0;
            foreach (var id in run.ownedPassiveItems)
                if (db.GetItem(id)?.axis == axis) n++;
            return n;
        }

        /// <summary>所持しているか (誓約の封じを戦闘の情報を作る時点で決めるため)。</summary>
        public static bool Owns(GameLoop.RunState run, string id)
            => run?.ownedPassiveItems != null && run.ownedPassiveItems.Contains(id);
    }
}

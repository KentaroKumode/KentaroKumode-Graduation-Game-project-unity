namespace GameLoop
{
    /// <summary>
    /// [佯狂者] セット（鈴/杖/衣/冠）の定義と所持数ヘルパ。発狂(希望0)連動アイテム群。
    /// 設計: docs/adr/0002-hope-system.md（佯狂者シリーズ）。状態は RunState.ownedPassiveItems を読むだけ。
    /// </summary>
    public static class YokyoSet
    {
        public const string Bell  = "佯狂者の鈴";  // BRONZE: 発狂中、他の佯狂者がショップに出やすい
        public const string Staff = "佯狂者の杖";  // SILVER: 発狂中、他の佯狂者数×1 ダイス合計
        public const string Garb  = "佯狂者の衣";  // GOLD:   発狂中、他の佯狂者数×30% 与ダメ
        public const string Crown = "佯狂者の冠";  // LEGENDARY: 発狂で希望0固定・フルセットで燃え尽き＋スケール

        public static readonly string[] All = { Bell, Staff, Garb, Crown };

        // 〈昇華〉済みも所持扱い（効果は発動しているためセット判定に含める）。run.OwnsPassive を使う。
        public static bool Has(RunState run, string id)
            => run != null && run.OwnsPassive(id);

        /// <summary>所持している佯狂者アイテムの種類数(0-4)。昇華済みも含む。</summary>
        public static int OwnedCount(RunState run)
        {
            if (run == null) return 0;
            int n = 0;
            foreach (var id in All) if (run.OwnsPassive(id)) n++;
            return n;
        }

        /// <summary>self を除いた「他の佯狂者」所持数。</summary>
        public static int OtherCount(RunState run, string self)
        {
            int n = OwnedCount(run);
            if (Has(run, self)) n--;
            return n < 0 ? 0 : n;
        }

        public static bool HasCrown(RunState run) => Has(run, Crown);

        /// <summary>冠＋他3種を全て所持（フルセット）。</summary>
        public static bool IsFullSet(RunState run) => OwnedCount(run) >= All.Length;
    }
}

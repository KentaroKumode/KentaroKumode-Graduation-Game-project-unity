using GameLoop;

namespace AutoTest
{
    /// <summary>
    /// BOT 専用: ショップでの売却経路。
    ///
    /// <para><b>所持数に上限は無い。</b> パッシブも消耗品も好きなだけ抱えられ、
    /// 取得によって既存の品が失われることはない。 売却はここから明示的に呼んだときだけ起きる。
    /// (廃止済み機構の経緯は git と docs/GAME.md §24。 ここには書かない ──
    ///  コードに残った説明が、 存在しない制約の知識を再生産するため)</para>
    /// </summary>
    public static class BotSelling
    {
        /// <summary>BOT 用: 指定 index のパッシブをショップ売却。 成功時 true。
        /// 2026-06-23a: ショップ滞在中のみ売却可 (移動中は false → 呼出側で諦める)。
        /// 2026-06-23b: ショップ由来在庫 (shopPurchasedCounts) 制限を撤廃 ── 非ショップ由来 (イベント/戦闘ドロップ等) も売却可。</summary>
        public static bool TrySellFromBot(RunState run, int passiveIndex)
        {
            if (run == null || run.ownedPassiveItems == null) return false;
            if (passiveIndex < 0 || passiveIndex >= run.ownedPassiveItems.Count) return false;
            // ショップ滞在中でなければ売却不可 (= ショップに行かないと売れない、 自然な仕様)
            var sm = InventorySystem.Shop.ShopManager.Instance;
            if (sm == null || sm.Current == null) return false;
            // (商人の符牒 売却阻止フックは 2026-07-18 アイテム削除に伴い除去)
            // ShopManager.TrySell を呼ぶ (在庫減算・コイン加算を委譲、 由来問わず)
            return sm.TrySell(InventorySystem.Shop.ShopManager.SellSource.Passive, passiveIndex, run);
        }
    }
}

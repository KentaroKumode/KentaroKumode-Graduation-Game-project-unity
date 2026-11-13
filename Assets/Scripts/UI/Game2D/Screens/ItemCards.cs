using System.Collections.Generic;
using GameLoop;
using ItemSystem;

namespace UI.Game2D
{
    /// <summary>
    /// ゲームの状態から <see cref="ItemCardInfo"/> を作る (2026-11-13・持ち物の画面)。 店・報酬の画面でも品 1 つを
    /// <see cref="Of"/> で作れば、 同じ吹き出し・詳細の欄 (<see cref="ItemTooltip"/>・<see cref="ItemDetailPane"/>) で出せる。
    /// <b>読むだけ</b> ── 乱数を引かない・RunState を書き換えない (売値は幅の字 <see cref="ItemSystem.Shop.ShopManager.SellPriceLabel"/>)。
    /// </summary>
    public static class ItemCards
    {
        private static readonly System.Func<char, int> Adv = Game2DJpFont.Advance;

        /// <summary>items.json の品 1 つ (秘宝・武器・消耗品)。 count = 同じ品の数。 run があれば消耗品の「今は使えない理由」を付ける。</summary>
        public static ItemCardInfo Of(string id, int count = 1, RunState run = null)
        {
            var data = string.IsNullOrEmpty(id) ? null : ItemDatabase.Instance?.GetItem(id);
            string cat = data == null ? "Passive" : data.category == ItemCategory.Weapon ? "Weapon" : data.category == ItemCategory.Consumable ? "Consumable" : "Passive";
            // 売値: 売れる品 (手放せない品でない) だけ。 武器は売る画面に並ばない (下取りは別の口) ので出さない
            string sell = cat != "Weapon" && data != null && ItemIds.CanSell(id) ? ItemSystem.Shop.ShopManager.SellPriceLabel(id) : null;
            string mark = ItemIds.IsEntrusted(id) ? "預かり物 (手放せない)" : !ItemIds.CanSell(id) && data != null ? "手放せない" : null;
            var c = ItemCardInfo.ForItem(data != null ? data.id : id, ItemDatabase.DisplayNameOf(data, id), cat,
                data != null ? (int)data.rarity : -1, data?.description, data?.flavorText, data?.axis, data?.family, data != null ? data.tier : 0,
                sell, mark, count, Adv);
            c.key = id;      // 持っているリストの綴りのまま (使う・フォーカスを戻す時に、 リストの中の品をそのまま指す)
            if (c.usable && run != null) c.block = Consumables.BlockReason(run, id);
            return c;
        }

        /// <summary>強化素材 (RunState.weaponMaterials・品の id は無い)。 売値は 1 つの値。</summary>
        public static ItemCardInfo Material(int n)
            => new ItemCardInfo
            {
                kind = ItemCardInfo.Kind.Weapon, kindLabel = "強化素材", key = "#material", name = "強化素材", count = n, glyph = "素",
                description = "休憩で武器を強化する時に払う。",
                sell = ItemSystem.Shop.ShopManager.WeaponMaterialSellPrice + "G (1 つ)",
            };

        /// <summary>出目パーツ (サイコロの面 1 つ)。 字は目の数・段は点の数。</summary>
        public static ItemCardInfo Part(DiceFaceParts.Part p)
            => new ItemCardInfo
            {
                kind = ItemCardInfo.Kind.FacePart, kindLabel = "出目パーツ", key = DiceFaceParts.Id(p), name = DiceFaceParts.Label(p),
                description = DiceFaceParts.Describe(p.tier), tier = (int)p.tier, glyph = p.face.ToString(), pips = (int)p.tier,
            };

        /// <summary>大事な物 (ownedFlags のフラグの品・items.json に無い)。 説明文は無い。</summary>
        public static ItemCardInfo Keepsake(string flag)
            => new ItemCardInfo
            {
                kind = ItemCardInfo.Kind.Keepsake, kindLabel = "大事な物", key = "#flag:" + flag, name = flag,
                glyph = ItemCardInfo.Glyph(flag, InventoryLayout.CellInner - 2, Adv),
            };

        /// <summary>
        /// 持ち物の画面に並べる物 (区切り = <see cref="ItemCardInfo.Kind"/> の順)。 区切りの中は持っている順 (リストの順)。
        /// 同じ品は 1 つの升目にまとめて数を付ける (消耗品は今の一覧と同じ「×N」)。
        /// <list type="bullet">
        /// <item>秘宝: ownedPassiveItems (空の名前 "" は数えない ── 今の追加の口 PassiveAddHelper は "" を入れない)</item>
        /// <item>武器: 装備している武器 1 本 + 強化素材 (1 つ以上ある時・数を付ける)</item>
        /// <item>消耗品: ownedConsumables</item>
        /// <item>出目パーツ: diceFaceParts (サイコロの面の全部・素の面も)</item>
        /// <item>大事な物: ownedFlags (〈さびれた観測所〉を見た印 <see cref="GameManager.ObservatorySeenFlag"/> は品でないので出さない)</item>
        /// </list>
        /// </summary>
        public static List<List<ItemCardInfo>> Collect(RunState run)
        {
            var s = new List<List<ItemCardInfo>>();
            for (int i = 0; i < ItemCardInfo.KindCount; i++) s.Add(new List<ItemCardInfo>());
            if (run == null) return s;
            Grouped(run.ownedPassiveItems, s[(int)ItemCardInfo.Kind.Relic], run);
            if (!string.IsNullOrEmpty(run.equippedWeaponId)) s[(int)ItemCardInfo.Kind.Weapon].Add(Of(run.equippedWeaponId, 1, run));
            if (run.weaponMaterials > 0) s[(int)ItemCardInfo.Kind.Weapon].Add(Material(run.weaponMaterials));
            Grouped(run.ownedConsumables, s[(int)ItemCardInfo.Kind.Consumable], run);
            if (run.diceFaceParts != null) foreach (var p in run.diceFaceParts) s[(int)ItemCardInfo.Kind.FacePart].Add(Part(p));
            if (run.ownedFlags != null)
                foreach (var f in run.ownedFlags)
                    if (!string.IsNullOrEmpty(f) && f != GameManager.ObservatorySeenFlag) s[(int)ItemCardInfo.Kind.Keepsake].Add(Keepsake(f));
            return s;
        }

        private static void Grouped(List<string> src, List<ItemCardInfo> dst, RunState run)
        {
            if (src == null) return;
            var order = new List<string>(); var n = new Dictionary<string, int>();
            foreach (var id in src)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (n.TryGetValue(id, out int k)) n[id] = k + 1; else { n[id] = 1; order.Add(id); }
            }
            foreach (var id in order) dst.Add(Of(id, n[id], run));
        }
    }
}

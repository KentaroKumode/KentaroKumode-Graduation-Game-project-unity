using System.Collections.Generic;
using UnityEngine;
using GameLoop;

namespace InventorySystem.Shop
{
    /// <summary>
    /// ショップの在庫生成と取引処理を担当するシングルトン。
    /// GameManager がショップマス到達時に Generate() を呼び、UI/入力が TryBuy/TrySell を呼ぶ。
    /// </summary>
    public class ShopManager : MonoBehaviour
    {
        private static ShopManager _instance;
        private static bool _shuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _shuttingDown = false; _instance = null; }

        public static ShopManager Instance
        {
            get
            {
                if (_shuttingDown) return null;
                if (_instance == null)
                {
                    var go = new GameObject("ShopManager");
                    _instance = go.AddComponent<ShopManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        void OnApplicationQuit() { _shuttingDown = true; }
        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public ShopInventory Current { get; private set; }

        public event System.Action<ShopInventory> OnShopOpened;
        public event System.Action OnShopUpdated;
        public event System.Action OnShopClosed;

        // ============================================================
        //  Tier 重み（A3）
        // ============================================================

                // 武器枠のみ WeaponShopFilter で LEGENDARY を別途除外（強化最終形対策）
        // MYTHIC は全カテゴリで排出しない
        private static readonly (ItemRarity rarity, float weight)[] tierWeights = new[]
        {
            (ItemRarity.BRONZE,    0.54f),
            (ItemRarity.SILVER,    0.35f),
            (ItemRarity.GOLD,      0.10f),
            (ItemRarity.LEGENDARY, 0.01f),
        };

        // ============================================================
        //  在庫生成
        // ============================================================

        /// <summary>ショップマス入店時に在庫を生成。</summary>
        public ShopInventory Generate(int floor)
        {
            var inv = new ShopInventory();

            // フロア価格倍率（FloorModifier.shopPriceMultiplier）× メタデバフ Lv1 × 商人の符牒
            var floorMod = MapSystem.FloorModifierDatabase.Get(floor);
            float baseMul = floorMod != null ? floorMod.shopPriceMultiplier : 1f;
            inv.priceMultiplier = baseMul * MetaProgression.MetaDebuffApplicator.GetShopPriceMultiplier();
            // 商人の符牒: 価格半額（割合計算が先）
            var runForSeal = GameLoop.GameManager.Instance?.Run;
            if (runForSeal != null && runForSeal.ownedPassiveItems != null
                && runForSeal.ownedPassiveItems.Contains("商人の符牒"))
            {
                inv.priceMultiplier *= 0.5f;
                Debug.Log("[ShopManager] 商人の符牒適用: 価格半額");
            }

            // パッシブ ×2
            for (int i = 0; i < 2; i++)
                inv.slots.Add(BuildSlot(ShopSlotKind.Passive, inv.priceMultiplier));

            // 消費 ×2
            for (int i = 0; i < 2; i++)
                inv.slots.Add(BuildSlot(ShopSlotKind.Consumable, inv.priceMultiplier));

            // 武器 ×1
            inv.slots.Add(BuildSlot(ShopSlotKind.Weapon, inv.priceMultiplier));

            // ダイス ×1
            inv.slots.Add(BuildSlot(ShopSlotKind.Dice, inv.priceMultiplier));

            // 武器強化素材 ×1（在庫無限、価格は base × 2^N × priceMultiplier）
            inv.slots.Add(new ShopSlot
            {
                kind = ShopSlotKind.WeaponMaterial,
                itemId = null,
                price = inv.CurrentMaterialPrice,
                sold = false,
            });

            Current = inv;
            OnShopOpened?.Invoke(inv);
            Debug.Log($"[ShopManager] 入店: フロア{floor}, スロット{inv.slots.Count}");

            // 恒久デバフ「クァディルの色欲」: 入店時、買える中で最も高価な品を強制購入
            var run = GameLoop.GameManager.Instance?.Run;
            if (run != null && MetaProgression.PermanentDebuffEffects.HasLust(run))
                ForceBuyHighestAffordable(inv, run);

            return inv;
        }

        private void ForceBuyHighestAffordable(ShopInventory inv, GameLoop.RunState run)
        {
            int bestIdx = -1;
            int bestPrice = -1;
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var slot = inv.slots[i];
                if (slot == null || slot.sold) continue;
                int price = slot.kind == ShopSlotKind.WeaponMaterial ? inv.CurrentMaterialPrice : slot.price;
                if (string.IsNullOrEmpty(slot.itemId) && slot.kind != ShopSlotKind.WeaponMaterial) continue;
                if (price > run.coins) continue;
                if (price > bestPrice) { bestPrice = price; bestIdx = i; }
            }
            if (bestIdx < 0)
            {
                Debug.Log($"[ShopManager] {MetaProgression.PermanentDebuffIds.Lust}: 強制購入対象なし（金欠 or 在庫無し）");
                return;
            }
            Debug.Log($"[ShopManager] {MetaProgression.PermanentDebuffIds.Lust}: 強制購入 slot={bestIdx} price={bestPrice}G");
            TryBuy(bestIdx, run);
        }

        private ShopSlot BuildSlot(ShopSlotKind kind, float priceMultiplier)
        {
            var item = PickItemByKind(kind);
            int price = 0;
            string id = null;
            if (item != null)
            {
                id = item.internalName;
                int basePrice;
                if (item.buyPrice != null && item.buyPrice.max >= item.buyPrice.min)
                    basePrice = Random.Range(item.buyPrice.min, item.buyPrice.max + 1);
                else
                    basePrice = 10;
                price = Mathf.CeilToInt(basePrice * priceMultiplier);
            }
            return new ShopSlot { kind = kind, itemId = id, price = price, sold = false };
        }

        /// <summary>カテゴリ + Tier重みでアイテムを1個選出。</summary>
        private CompleteItemData PickItemByKind(ShopSlotKind kind)
        {
            var db = ItemDatabase.Instance;
            if (db == null) return null;

            ItemCategory? category = kind switch
            {
                ShopSlotKind.Passive     => ItemCategory.Passive,
                ShopSlotKind.Consumable  => ItemCategory.Consumable,
                ShopSlotKind.Weapon      => ItemCategory.Weapon,
                ShopSlotKind.Dice        => ItemCategory.Dice,
                _ => (ItemCategory?)null,
            };
            if (category == null) return null;

            var pool = db.GetItemsByCategory(category.Value);
            if (pool == null || pool.Count == 0) return null;

            // イベント限定アイテムを除外（ちいさな灯火・決意 等）
            pool = pool.FindAll(EventOnlyItemFilter.IsAllowed);
            if (pool.Count == 0) return null;

            // 武器のみ: 強化ルート最終LEGENDARYを除外
            if (kind == ShopSlotKind.Weapon)
            {
                pool = pool.FindAll(WeaponShopFilter.IsShopAllowed);
                if (pool.Count == 0) return null;
            }

            // Tier重みで抽選
            ItemRarity targetRarity = RollTier(pool);
            var byTier = pool.FindAll(it => it.rarity == targetRarity);
            if (byTier.Count == 0)
            {
                // フォールバック: そのTierが存在しなければ全体からランダム
                return pool[Random.Range(0, pool.Count)];
            }
            return byTier[Random.Range(0, byTier.Count)];
        }

        private ItemRarity RollTier(List<CompleteItemData> pool)
        {
            // pool 内に存在する rarity だけで重み付き抽選
            var availableRarities = new HashSet<ItemRarity>();
            foreach (var it in pool) availableRarities.Add(it.rarity);

            float total = 0f;
            foreach (var (rarity, weight) in tierWeights)
                if (availableRarities.Contains(rarity)) total += weight;

            if (total <= 0f)
                return pool.Count > 0 ? pool[0].rarity : ItemRarity.BRONZE;

            float r = Random.value * total;
            foreach (var (rarity, weight) in tierWeights)
            {
                if (!availableRarities.Contains(rarity)) continue;
                if ((r -= weight) <= 0f) return rarity;
            }
            return ItemRarity.BRONZE;
        }

        // ============================================================
        //  購入
        // ============================================================

        /// <summary>スロット index の商品を購入。</summary>
        public bool TryBuy(int slotIndex, RunState run)
        {
            if (Current == null || run == null) return false;
            if (slotIndex < 0 || slotIndex >= Current.slots.Count) return false;

            var slot = Current.slots[slotIndex];

            if (slot.kind == ShopSlotKind.WeaponMaterial)
            {
                int price = Current.CurrentMaterialPrice;
                if (run.coins < price) { Log("ゴールド不足"); return false; }
                run.coins -= price;
                run.weaponMaterials++;
                Current.materialPurchaseCount++;
                slot.price = Current.CurrentMaterialPrice; // 表示価格を更新
                Debug.Log($"[ShopManager] 強化素材購入: -{price}G (次回 {Current.CurrentMaterialPrice}G)");
                MetaProgression.MetaBuffApplicator.RollRefund(price, run);
                OnShopUpdated?.Invoke();
                return true;
            }

            if (slot.sold) { Log("売り切れ"); return false; }
            if (string.IsNullOrEmpty(slot.itemId)) { Log("空スロット"); return false; }
            if (run.coins < slot.price) { Log("ゴールド不足"); return false; }

            // 恒久デバフ「ヤルノクの嫉妬」: 1ショップで通常品は1個まで
            if (MetaProgression.PermanentDebuffEffects.HasEnvy(run) && Current.purchaseCount >= 1)
            {
                Log($"恒久デバフ {MetaProgression.PermanentDebuffIds.Envy}: このショップでは既に1個購入済み");
                return false;
            }

            run.coins -= slot.price;
            Current.purchaseCount++;
            switch (slot.kind)
            {
                case ShopSlotKind.Passive:
                case ShopSlotKind.Weapon:
                case ShopSlotKind.Dice:
                    run.ownedPassiveItems.Add(slot.itemId);
                    GameLoop.Loadout.TryAutoEquip(run, slot.itemId);
                    break;
                case ShopSlotKind.Consumable:
                    run.ownedConsumables.Add(slot.itemId);
                    break;
            }
            slot.sold = true;

            var data = ItemDatabase.Instance?.GetItem(slot.itemId);
            string label = data != null ? data.displayName : slot.itemId;
            Debug.Log($"[ShopManager] 購入: {label} -{slot.price}G");
            MetaProgression.MetaBuffApplicator.RollRefund(slot.price, run);
            OnShopUpdated?.Invoke();
            return true;
        }

        // ============================================================
        //  売却
        // ============================================================

        public enum SellSource { Passive, Consumable, WeaponMaterial }

        /// <summary>所持アイテムを売却。listIndex は対応リストのインデックス（強化素材時は無視）。</summary>
        public bool TrySell(SellSource source, int listIndex, RunState run)
        {
            if (run == null) return false;

            // 商人の符牒: アイテム売却不可
            if (run.ownedPassiveItems != null && run.ownedPassiveItems.Contains("商人の符牒"))
            {
                Log("商人の符牒の誓いにより売却不可");
                return false;
            }

            if (source == SellSource.WeaponMaterial)
            {
                if (run.weaponMaterials <= 0) { Log("素材がない"); return false; }
                int sellPrice = 15; // 基準売値（後で調整可）
                run.weaponMaterials--;
                int gain = GameLoop.LastStand.FilterGoldGain(run, sellPrice);
                run.coins += gain;
                Debug.Log($"[ShopManager] 強化素材売却: +{gain}G");
                OnShopUpdated?.Invoke();
                return true;
            }

            var list = source == SellSource.Passive ? run.ownedPassiveItems : run.ownedConsumables;
            if (list == null || listIndex < 0 || listIndex >= list.Count) return false;

            string id = list[listIndex];
            int price = ResolveSellPrice(id);
            list.RemoveAt(listIndex);
            int gainPrice = GameLoop.LastStand.FilterGoldGain(run, price);
            run.coins += gainPrice;
            Debug.Log($"[ShopManager] 売却: {id} +{gainPrice}G");
            OnShopUpdated?.Invoke();
            return true;
        }

        private int ResolveSellPrice(string id)
        {
            if (string.IsNullOrEmpty(id)) return 5;
            var data = ItemDatabase.Instance?.GetItem(id);
            if (data?.sellPrice == null) return 5;
            return Random.Range(data.sellPrice.min, data.sellPrice.max + 1);
        }

        // ============================================================
        //  退店
        // ============================================================

        public void Close()
        {
            Current = null;
            OnShopClosed?.Invoke();
        }

        private void Log(string msg) => Debug.Log($"[ShopManager] {msg}");
    }
}

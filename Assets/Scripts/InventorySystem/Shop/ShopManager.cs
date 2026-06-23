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

                // 武器枠のみ別の重みテーブル (T4 を超レア出現に)
        // MYTHIC は全カテゴリで排出しない
        // 2026-06-22: 価格カーブ圧縮 (BRONZE 8G / LEGENDARY 14G、 1:1.75) に合わせて
        //             LEGENDARY 出現率を 0.12 → 0.05 に下げ、 希少性を出現頻度で担保。
        //             安価で買いやすくなった上位 Tier の取得を 「運の良いラン」 に限定する設計。
        private static readonly (ItemRarity rarity, float weight)[] tierWeights = new[]
        {
            (ItemRarity.BRONZE,    0.45f),
            (ItemRarity.SILVER,    0.32f),
            (ItemRarity.GOLD,      0.18f),
            (ItemRarity.LEGENDARY, 0.05f),
        };

        // 2026-06-22: 武器枠用の Tier 重み。 T4 (LEGENDARY) を 0.005 (0.5%/枠) に絞り、
        // フルラン (~6 ショップ × 1.5 武器枠 = 9 武器枠) × 10 ラン = 90 枠中 0.5 枠出現 ≒ 10 ランに 1 回ペース。
        // 強化ルート完走の代替路線として 「直接 T4 を引く運要素」 を残す設計。
        private static readonly (ItemRarity rarity, float weight)[] weaponTierWeights = new[]
        {
            (ItemRarity.BRONZE,    0.475f),
            (ItemRarity.SILVER,    0.335f),
            (ItemRarity.GOLD,      0.185f),
            (ItemRarity.LEGENDARY, 0.005f),
        };

        // ============================================================
        //  在庫生成
        // ============================================================

        /// <summary>鑑定の眼鏡: この入店中の最低レア保証（null=無）。</summary>
        private ItemRarity? _apprMinRarity;

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
            if (runForSeal != null && runForSeal.OwnsPassive("商人の符牒"))
            {
                inv.priceMultiplier *= 0.5f;
                Debug.Log("[ShopManager] 商人の符牒適用: 価格半額");
            }

            // 消費: 商人の鈴（次ショップ全価格半額）/ 鑑定の眼鏡（最低レア保証）
            var rsCons = GameLoop.GameManager.Instance?.Run;
            if (rsCons != null && rsCons.nextShopHalfPrice)
            {
                inv.priceMultiplier *= 0.5f;
                rsCons.nextShopHalfPrice = false;
                Debug.Log("[ShopManager] 商人の鈴: 全価格-50%");
            }
            _apprMinRarity = null;
            if (rsCons != null && rsCons.nextLootMinRarity >= 0)
            {
                _apprMinRarity = (ItemRarity)rsCons.nextLootMinRarity;
                rsCons.nextLootMinRarity = -1; // ショップで消費
                Debug.Log($"[ShopManager] 鑑定の眼鏡: 最低レア {_apprMinRarity}");
            }

            // パッシブ ×3
            for (int i = 0; i < 3; i++)
                inv.slots.Add(BuildSlot(ShopSlotKind.Passive, inv.priceMultiplier));

            // 消費 ×4 (2026-05-31 増枠: 消費価格半減と合わせて消費活用を促進)
            for (int i = 0; i < 4; i++)
                inv.slots.Add(BuildSlot(ShopSlotKind.Consumable, inv.priceMultiplier));

            // 武器 ×2
            for (int i = 0; i < 2; i++)
                inv.slots.Add(BuildSlot(ShopSlotKind.Weapon, inv.priceMultiplier));

            // ダイス ×2
            for (int i = 0; i < 2; i++)
                inv.slots.Add(BuildSlot(ShopSlotKind.Dice, inv.priceMultiplier));

            // 武器強化素材 ×1（在庫無限、価格は base × 2^N × priceMultiplier）
            inv.slots.Add(new ShopSlot
            {
                kind = ShopSlotKind.WeaponMaterial,
                itemId = null,
                price = inv.CurrentMaterialPrice,
                sold = false,
            });

            // インベントリ拡張 ×1 (現在の解放列が < MAX のときのみ。 既に最大なら追加しない)
            var runForExpand = GameLoop.GameManager.Instance?.Run;
            int expandCost = runForExpand != null
                ? InventorySystem.Helpers.InventoryCapacity.NextExpansionCost(runForExpand) : int.MaxValue;
            if (expandCost != int.MaxValue)
            {
                inv.slots.Add(new ShopSlot
                {
                    kind = ShopSlotKind.InventoryExpansion,
                    itemId = null,
                    price = Mathf.CeilToInt(expandCost * inv.priceMultiplier),
                    sold = false,
                });
            }

            // 2026-06-23: 上位互換アップグレード割引 (同家系下位所持時、 1G/Tier段の超格安)
            //   ── 上位 Tier を取らない方が得という設計上の罠を撲滅
            ApplyUpgradeDiscounts(inv, GameLoop.GameManager.Instance?.Run);

            // 2026-06-22: メタバフ「特売品」 を 1/2/3 個ランダム枠に付与 (Passive/Consumable/Weapon/Dice のみ対象)
            ApplySaleDiscounts(inv);

            Current = inv;
            OnShopOpened?.Invoke(inv);
            Debug.Log($"[ShopManager] 入店: フロア{floor}, スロット{inv.slots.Count}");

            // 恒久デバフ「クァディルの色欲」: 入店時、買える中で最も高価な品を強制購入
            var run = GameLoop.GameManager.Instance?.Run;
            if (run != null && MetaProgression.PermanentDebuffEffects.HasLust(run))
                ForceBuyHighestAffordable(inv, run);

            return inv;
        }

        /// <summary>2026-06-23: 同家系下位 Lv を所持している場合の上位互換アップグレード割引。
        /// 「Tier 1 段差 = 1G」 の超格安に。 上位を取らない方が得という設計上の罠を撲滅。
        /// 例: LV2 (SILVER) 所持 + LV4 (LEGENDARY) 提示 → 価格 = 2G (差 2 段)。
        /// 特売品との重複時は、 より安い方を採用 (= 通常 upgrade 割引が優位)。</summary>
        private void ApplyUpgradeDiscounts(ShopInventory inv, GameLoop.RunState run)
        {
            if (run == null || inv?.slots == null) return;
            var db = ItemDatabase.Instance;
            if (db == null) return;
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var s = inv.slots[i];
                if (s == null || s.sold) continue;
                if (string.IsNullOrEmpty(s.itemId)) continue;
                if (s.kind == ShopSlotKind.WeaponMaterial || s.kind == ShopSlotKind.InventoryExpansion) continue;
                var slotData = db.GetItem(s.itemId);
                if (slotData?.passiveSkills == null) continue;

                // 候補の家系→最高 Lv マップ
                var candFamilyLv = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var ps in slotData.passiveSkills)
                {
                    if (string.IsNullOrEmpty(ps.internalName)) continue;
                    var (fam, lv) = InventorySystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ps.internalName);
                    if (lv > 0 && (!candFamilyLv.TryGetValue(fam, out int prev) || lv > prev))
                        candFamilyLv[fam] = lv;
                }
                if (candFamilyLv.Count == 0) continue;

                // 所持品から同家系の最高 Lv を探す (装備武器/ダイス含む)
                int maxStep = 0;
                void CheckOwned(string ownedId)
                {
                    if (string.IsNullOrEmpty(ownedId)) return;
                    var od = db.GetItem(ownedId);
                    if (od?.passiveSkills == null) return;
                    foreach (var ops in od.passiveSkills)
                    {
                        if (string.IsNullOrEmpty(ops.internalName)) continue;
                        var (ofam, olv) = InventorySystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ops.internalName);
                        if (olv <= 0) continue;
                        if (!candFamilyLv.TryGetValue(ofam, out int candLv)) continue;
                        if (olv >= candLv) continue;
                        int step = candLv - olv;
                        if (step > maxStep) maxStep = step;
                    }
                }
                CheckOwned(run.equippedWeaponId);
                CheckOwned(run.equippedDiceId);
                if (run.ownedPassiveItems != null)
                    foreach (var id in run.ownedPassiveItems) CheckOwned(id);

                if (maxStep <= 0) continue;
                // 割引適用 (1G/Tier段、 最低 1G、 既存より安くなる場合のみ反映)
                int newPrice = System.Math.Max(1, maxStep);
                if (newPrice >= s.price) continue;
                s.originalPrice = s.price;
                s.discountPct = (int)(100f * (1f - (float)newPrice / s.price));
                s.price = newPrice;
                Debug.Log($"[ShopManager] 上位互換アップグレード割引 slot={i} {s.itemId} ({maxStep}段差) {s.originalPrice}G→{s.price}G ({s.discountPct}%off)");
            }
        }

        /// <summary>2026-06-22: メタバフ refundLevel に応じて 1/2/3 個の特売品をランダム選出。
        /// 特売品は 20-60% の範囲で price を割引、 元価格を originalPrice に保持。
        /// 対象は Passive/Consumable/Weapon/Dice (アイテム枠) のみ、 強化素材/拡張は除外。</summary>
        private void ApplySaleDiscounts(ShopInventory inv)
        {
            int n = MetaProgression.MetaBuffApplicator.GetSaleItemCount();
            if (n <= 0 || inv?.slots == null) return;
            // 特売対象になり得るスロットを抽出 (商品枠かつ未売却かつ itemId あり)
            var candidates = new System.Collections.Generic.List<int>();
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var s = inv.slots[i];
                if (s == null || s.sold) continue;
                if (string.IsNullOrEmpty(s.itemId)) continue;
                if (s.kind == ShopSlotKind.WeaponMaterial || s.kind == ShopSlotKind.InventoryExpansion) continue;
                if (s.discountPct > 0) continue; // 2026-06-23: 上位互換割引済は特売対象外 (二重割引防止)
                candidates.Add(i);
            }
            // Fisher-Yates シャッフルで n 個ランダム選出
            int pick = Mathf.Min(n, candidates.Count);
            for (int i = 0; i < pick; i++)
            {
                int j = Random.Range(i, candidates.Count);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                var s = inv.slots[candidates[i]];
                int discount = Random.Range(
                    MetaProgression.MetaBuffApplicator.SaleDiscountMinPct,
                    MetaProgression.MetaBuffApplicator.SaleDiscountMaxPct + 1);
                s.originalPrice = s.price;
                s.discountPct = discount;
                s.price = Mathf.Max(1, Mathf.CeilToInt(s.price * (1f - discount / 100f)));
                Debug.Log($"[ShopManager] 特売 slot={candidates[i]} {s.itemId} -{discount}% ({s.originalPrice}G→{s.price}G)");
            }
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

            // パッシブはラン重複排除（所持済みは並べない）。枯渇時は元プール（重複許可）。
            if (category.Value == ItemCategory.Passive)
            {
                var run = GameLoop.GameManager.Instance?.Run;
                if (run?.ownedPassiveItems != null)
                {
                    // 現所持に加え「このランで一度取得した(=捨てた物も含む)」パッシブも陳列しない（重複禁止）。
                    var owned = new HashSet<string>(run.ownedPassiveItems);
                    if (run.seenPassiveItemIds != null) owned.UnionWith(run.seenPassiveItemIds);
                    var dd = pool.FindAll(it => !owned.Contains(it.internalName));
                    if (dd.Count > 0) pool = dd;

                    // 佯狂者の鈴(ADR-0002): 絶望(≤20)/発狂中、他の[佯狂者]アイテムがショップに出やすい。
                    // 未所持の佯狂者が在庫候補にあれば 60% で優先排出（セットの組み立てを助ける）。
                    if (owned.Contains(GameLoop.YokyoSet.Bell)
                        && GameLoop.HopeSystem.GetTier(run) >= GameLoop.HopeTier.Despair)
                    {
                        var yokyo = pool.FindAll(it => System.Array.IndexOf(GameLoop.YokyoSet.All, it.internalName) >= 0);
                        if (yokyo.Count > 0 && Random.value < 0.6f)
                            return yokyo[Random.Range(0, yokyo.Count)];
                    }

                    // サーベル・ワルツ([剣の舞]): 所持(昇華含む)時、未所持の[剣の舞]が出やすい(60%優先排出)。
                    // セットの組み立てを助ける（佯狂者の鈴と同型・希望条件なし）。
                    if (run.OwnsPassive(GameLoop.SwordDanceSet.SaberWaltz))
                    {
                        var dance = pool.FindAll(it => GameLoop.SwordDanceSet.IsDance(it.internalName));
                        if (dance.Count > 0 && Random.value < 0.6f)
                            return dance[Random.Range(0, dance.Count)];
                    }
                }
            }

            // 鑑定の眼鏡: 最低レア保証（該当無しなら無視）
            if (_apprMinRarity.HasValue)
            {
                var hi = pool.FindAll(p => p.rarity >= _apprMinRarity.Value);
                if (hi.Count > 0) pool = hi;
            }

            // 武器のみ: WeaponShopFilter のチェック (LEGENDARY を含めて出現可、 出現率は別重みで制御)
            if (kind == ShopSlotKind.Weapon)
            {
                pool = pool.FindAll(WeaponShopFilter.IsShopAllowed);
                if (pool.Count == 0) return null;
            }

            // Tier重みで抽選 (武器は専用重みで T4 を超レア化)
            var weights = (kind == ShopSlotKind.Weapon) ? weaponTierWeights : tierWeights;
            ItemRarity targetRarity = RollTier(pool, weights);
            var byTier = pool.FindAll(it => it.rarity == targetRarity);
            if (byTier.Count == 0)
            {
                // フォールバック: そのTierが存在しなければ全体からランダム
                return pool[Random.Range(0, pool.Count)];
            }
            return byTier[Random.Range(0, byTier.Count)];
        }

        private ItemRarity RollTier(List<CompleteItemData> pool, (ItemRarity rarity, float weight)[] weights = null)
        {
            var table = weights ?? tierWeights;
            // pool 内に存在する rarity だけで重み付き抽選
            var availableRarities = new HashSet<ItemRarity>();
            foreach (var it in pool) availableRarities.Add(it.rarity);

            float total = 0f;
            foreach (var (rarity, weight) in table)
                if (availableRarities.Contains(rarity)) total += weight;

            if (total <= 0f)
                return pool.Count > 0 ? pool[0].rarity : ItemRarity.BRONZE;

            float r = Random.value * total;
            foreach (var (rarity, weight) in table)
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
                run.coinsSpent += price;
                run.weaponMaterials++;
                Current.materialPurchaseCount++;
                slot.price = Current.CurrentMaterialPrice; // 表示価格を更新
                Debug.Log($"[ShopManager] 強化素材購入: -{price}G (次回 {Current.CurrentMaterialPrice}G)");
                MetaProgression.MetaBuffApplicator.RollRefund(price, run);
                OnShopUpdated?.Invoke();
                return true;
            }

            if (slot.kind == ShopSlotKind.InventoryExpansion)
            {
                if (slot.sold) { Log("売り切れ"); return false; }
                if (run.coins < slot.price) { Log("ゴールド不足"); return false; }
                if (run.inventoryUnlockedRows >= InventoryConstants.MAX_UNLOCKED_ROWS)
                { Log("インベントリ最大解放済み"); return false; }
                run.coins -= slot.price;
                run.coinsSpent += slot.price;
                run.inventoryUnlockedRows++;
                slot.sold = true;
                Debug.Log($"[ShopManager] インベントリ拡張: -{slot.price}G → {run.inventoryUnlockedRows}列 (容量{run.inventoryUnlockedRows * InventoryConstants.GRID_WIDTH}マス)");
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
            run.coinsSpent += slot.price;
            Current.purchaseCount++;
            switch (slot.kind)
            {
                case ShopSlotKind.Passive:
                case ShopSlotKind.Weapon:
                case ShopSlotKind.Dice:
                    InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(run, slot.itemId);
                    GameLoop.Loadout.TryAutoEquip(run, slot.itemId);
                    break;
                case ShopSlotKind.Consumable:
                    run.ownedConsumables.Add(slot.itemId);
                    break;
            }
            // ショップ購入記録 (BOT 売却判定で「ショップ由来」 のみ売却可)
            if (slot.kind == ShopSlotKind.Passive || slot.kind == ShopSlotKind.Consumable
                || slot.kind == ShopSlotKind.Weapon || slot.kind == ShopSlotKind.Dice)
            {
                run.shopPurchasedCounts.TryGetValue(slot.itemId, out int prev);
                run.shopPurchasedCounts[slot.itemId] = prev + 1;
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
        //  リロール
        // ============================================================

        /// <summary>
        /// 強化素材スロットを除く未売却枠を全部振り直す。
        /// コストは ShopInventory.CurrentRerollPrice。売却済み枠はそのまま（戻らない）。
        /// </summary>
        public bool TryReroll(RunState run)
        {
            if (Current == null || run == null) return false;
            int price = Current.CurrentRerollPrice;
            if (run.coins < price) { Log($"リロール: ゴールド不足 ({price}G 必要)"); return false; }

            run.coins -= price;
            run.coinsSpent += price;
            Current.rerollCount++;

            int rerolled = 0;
            for (int i = 0; i < Current.slots.Count; i++)
            {
                var s = Current.slots[i];
                if (s == null) continue;
                if (s.kind == ShopSlotKind.WeaponMaterial) continue; // 価格カーブ独立
                if (s.sold) continue;
                var fresh = BuildSlot(s.kind, Current.priceMultiplier);
                Current.slots[i] = fresh;
                rerolled++;
            }

            Debug.Log($"[ShopManager] リロール#{Current.rerollCount}: -{price}G / {rerolled}枠更新 / 次回{Current.CurrentRerollPrice}G");
            MetaProgression.MetaBuffApplicator.RollRefund(price, run);
            OnShopUpdated?.Invoke();
            return true;
        }

        // ============================================================
        //  値下げ交渉 (=強盗)
        // ============================================================

        /// <summary>
        /// 値下げ交渉を試みる(実態は強盗)。
        /// ・現在ショップの未売却アイテムIDをスナップショットして run.robberyPendingItems に格納
        /// ・希望コスト適用、shopsBlocked = true (以降ショップ進入不可)
        /// ・shopRobberyInProgress = true
        /// ・ショップを閉じ、戻り値 true なら呼び出し側が「怪しい商人」戦闘を開始する
        /// </summary>
        public bool TryRobbery(RunState run)
        {
            if (Current == null || run == null) return false;
            if (!MetaProgression.MetaBuffApplicator.IsShopRobberyUnlocked())
            {
                Log("値下げ交渉: アンロックされていない");
                return false;
            }
            if (run.shopsBlocked) { Log("値下げ交渉: 既に交渉済み（追放中）"); return false; }

            // 在庫スナップショット (未売却・実アイテムのみ。 強化素材枠は除外)
            var loot = new List<string>();
            foreach (var s in Current.slots)
            {
                if (s == null || s.sold) continue;
                if (s.kind == ShopSlotKind.WeaponMaterial) continue;
                if (string.IsNullOrEmpty(s.itemId)) continue;
                loot.Add(s.itemId);
            }
            if (run.robberyPendingItems == null)
                run.robberyPendingItems = new List<string>();
            else
                run.robberyPendingItems.Clear();
            run.robberyPendingItems.AddRange(loot);

            GameLoop.HopeSystem.ApplyEvilChoice(run, GameLoop.HopeSystem.EvilChoiceCost);
            run.shopsBlocked = true;
            run.shopRobberyInProgress = true;

            Debug.Log($"[ShopManager] 値下げ交渉(強盗): 希望-{GameLoop.HopeSystem.EvilChoiceCost}, 在庫{loot.Count}件をスナップ, 以降ショップ進入不可");
            // ショップを閉じる (呼び出し側がエリート戦闘を開始する)
            Close();
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
            if (run != null && run.OwnsPassive("商人の符牒"))
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
            // 並列リスト同期のため PassiveAddHelper.RemoveAt を使う (passiveSigils も外す)
            if (source == SellSource.Passive)
                InventorySystem.Helpers.PassiveAddHelper.RemoveAt(run, listIndex);
            else
                list.RemoveAt(listIndex);
            // ショップ購入記録があれば在庫を 1 減らす (BOT 用、 売却可能在庫トラッキング)
            if (run.shopPurchasedCounts.TryGetValue(id, out int shopStock) && shopStock > 0)
                run.shopPurchasedCounts[id] = shopStock - 1;
            int gainPrice = GameLoop.LastStand.FilterGoldGain(run, price);
            run.coins += gainPrice;
            Debug.Log($"[ShopManager] 売却: {id} +{gainPrice}G");
            OnShopUpdated?.Invoke();
            return true;
        }

        /// <summary>2026-06-23: 売却額をレアリティに応じてスケール。
        /// 個別 sellPrice が定義されていれば優先 (旧仕様維持)。
        /// 未定義時は BRONZE 3G / SILVER 5G / GOLD 7G / LEGENDARY 9G を返す。
        /// 圧縮後の購入価格 (8/10/12/14) に対して おおよそ 37-64% の回収率。</summary>
        private int ResolveSellPrice(string id)
        {
            if (string.IsNullOrEmpty(id)) return 3;
            var data = ItemDatabase.Instance?.GetItem(id);
            if (data == null) return 3;
            if (data.sellPrice != null)
                return Random.Range(data.sellPrice.min, data.sellPrice.max + 1);
            switch (data.rarity)
            {
                case ItemRarity.BRONZE:    return 3;
                case ItemRarity.SILVER:    return 5;
                case ItemRarity.GOLD:      return 7;
                case ItemRarity.LEGENDARY: return 9;
                case ItemRarity.MYTHIC:    return 12;
                default:                   return 3;
            }
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

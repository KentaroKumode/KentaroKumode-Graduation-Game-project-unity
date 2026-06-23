using System.Collections.Generic;
using GameLoop;
using InventorySystem;

namespace AutoTest
{
    /// <summary>
    /// 2026-06-22 新設: 「インベントリパワー」 指標。
    ///
    /// 目的: アイテム売買のコスパ評価軸として、 Tier 表ベースで現在のインベントリ総戦力を数値化する。
    /// 同名/下位互換抑制 (PassiveSkillManager の dedup ロジック) を反映し、 実際に発動する分のみカウント。
    ///
    /// 算式:
    ///   Power = Σ (装備武器 + 装備ダイス + 所持パッシブ で 「実際発動する」 パッシブ ID の Tier スコア)
    ///         + 武器 Tier 係数 (T1=1, T2=3, T3=6, T4=10)
    ///
    /// 用途:
    ///   - summary 出力で各層平均/中央値を観測
    ///   - 売買時のコスパ計算: (購入後 Power - 売却後 Power) / G
    /// </summary>
    public static class InventoryPower
    {
        // 2026-06-22: TierWeight 関数は未使用化 (Compute から呼び出していたデッドコード一掃で削除)

        /// <summary>2026-06-23: Power 帯表示。 BOT/UI 双方で「現在どの段階か」 を即座に把握する用。
        /// 帯境界は実測 (前回バッチで 5F到達=43.3 / 6F到達=54.1 / 7F到達=79.8) から逆算。</summary>
        public static string GetPowerBand(int power)
        {
            if (power < 10) return "Weak (雑魚装備)";
            if (power < 25) return "Early (序盤)";
            if (power < 50) return "Mid (中盤)";
            if (power < 80) return "Late (終盤入口)";
            return "Apex (高みに至る)";
        }

        /// <summary>数値帯のみ (BOT 判断用、 文字列より高速)。 0=Weak/1=Early/2=Mid/3=Late/4=Apex。</summary>
        public static int GetPowerBandRank(int power)
        {
            if (power < 10) return 0;
            if (power < 25) return 1;
            if (power < 50) return 2;
            if (power < 80) return 3;
            return 4;
        }

        // 武器 Tier 係数 ( T1〜T4 のみ。 ユニーク武器は 5 を返す)
        private static int WeaponTierBonus(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return 0;
            if (weaponId.EndsWith("_t1")) return 1;
            if (weaponId.EndsWith("_t2")) return 3;
            if (weaponId.EndsWith("_t3")) return 6;
            if (weaponId.EndsWith("_t4")) return 10;
            // ユニーク武器 (ryusen / dead_staff / 黄金卿の剣 等) は中間扱い
            return 5;
        }

        /// <summary>現在の RunState のインベントリパワーを計算。
        /// 2026-06-22 高速化: 旧版は未使用の CollectFiringSkillIds 呼び出しがあり大きな浪費。 削除。</summary>
        public static int Compute(RunState run)
        {
            if (run == null) return 0;
            // 装備武器の Tier 係数 + アイテム単位の Tier スコア合算
            return WeaponTierBonus(run.equippedWeaponId) + PowerByOwnedItems(run);
        }

        /// <summary>所持アイテム (装備品 + ownedPassiveItems + 昇華済み) の Tier スコア合算。
        /// 2026-06-22b: 重複ペナルティを含まない RawScore を使う (所持価値の素の合算)。
        /// 2026-06-22d: 昇華済み (ascendedPassiveIds) も Power に算入。
        /// 同名 ID は 1 個分のみカウント (HashSet dedup)。</summary>
        public static int PowerByOwnedItems(RunState run)
        {
            if (run == null) return 0;
            var counted = new HashSet<string>();
            int sum = 0;
            void Add(string id)
            {
                if (string.IsNullOrEmpty(id)) return;
                if (!counted.Add(id)) return; // 同名 dedup
                int sc = LearnedPriorityProvider.RawScore(id);
                if (sc > 50) sc = 50; // 剣の舞 forced top (100) を抑制
                sum += sc;
            }
            Add(run.equippedWeaponId);
            Add(run.equippedDiceId);
            if (run.ownedPassiveItems != null)
                foreach (var id in run.ownedPassiveItems) Add(id);
            // 昇華済みパッシブ (グリッド外永久) も実発動するため Power に含める
            if (run.ascendedPassiveIds != null)
                foreach (var id in run.ascendedPassiveIds) Add(id);
            return sum;
        }

        /// <summary>Phase D (2026-06-22): 仮想的に「アイテム X を購入したら Power がどれだけ増えるか」 を算出。
        /// 2026-06-22 高速化: 旧版は仮装着+Compute×2 で O(items)。 ホットパスのため O(1) に短縮:
        /// - 既所持/装備中ID なら 0
        /// - 武器なら WeaponTierBonus 差分 + RawScore
        /// - その他は RawScore</summary>
        public static int SimulateAddItemDelta(RunState run, string itemId)
        {
            if (run == null || string.IsNullOrEmpty(itemId)) return 0;
            // 既所持なら delta = 0 (HashSet dedup される)
            if (itemId == run.equippedWeaponId || itemId == run.equippedDiceId) return 0;
            if (run.ownedPassiveItems != null && run.ownedPassiveItems.Contains(itemId)) return 0;
            if (run.ascendedPassiveIds != null && run.ascendedPassiveIds.Contains(itemId)) return 0;

            int delta = LearnedPriorityProvider.RawScore(itemId);
            if (delta > 50) delta = 50; // forced top 抑制

            // 武器なら Tier 係数も加算 (旧武器との差分)
            var db = ItemDatabase.Instance;
            var data = db?.GetItem(itemId);
            if (data != null && data.category == ItemCategory.Weapon)
            {
                delta += WeaponTierBonus(itemId) - WeaponTierBonus(run.equippedWeaponId);
            }
            return delta;
        }

        /// <summary>Phase D: 売買のコスパ = (ΔPower) / G。 G=0 (無料) なら ΔPower × 100 を返す (上限処理込み)。
        /// BOT 学習や Score 補正に使う指標。</summary>
        public static float CostEfficiency(int deltaPower, int goldCost)
        {
            if (goldCost <= 0) return deltaPower * 100f;
            return (float)deltaPower / goldCost;
        }

        /// <summary>実際に発動するパッシブ ID 集合 (同名/下位互換抑制適用後)。
        /// PassiveSkillManager.RefreshActiveSkills と同じロジックでオフライン算出。</summary>
        public static HashSet<string> CollectFiringSkillIds(RunState run, ItemDatabase db)
            => CollectFiringSkillIdsExcluding(run, db, null);

        /// <summary>2026-06-22b: 指定 itemId を 1 個分除外した上で発動するパッシブ ID 集合を返す。
        /// 「この id が無い世界線」 を仮想的に作る。 重複ペナルティ判定 (自分自身で発動中の循環参照防止) に使う。</summary>
        public static HashSet<string> CollectFiringSkillIdsExcluding(RunState run, ItemDatabase db, string excludeItemIdOnce)
        {
            var result = new HashSet<string>();
            if (run == null || db == null) return result;

            // 候補アイテム ID 一覧 (装備武器・装備ダイス・所持パッシブ・昇華済み)
            var itemIds = new List<string>();
            if (!string.IsNullOrEmpty(run.equippedWeaponId)) itemIds.Add(run.equippedWeaponId);
            if (!string.IsNullOrEmpty(run.equippedDiceId)) itemIds.Add(run.equippedDiceId);
            if (run.ownedPassiveItems != null) itemIds.AddRange(run.ownedPassiveItems);
            if (run.ascendedPassiveIds != null) itemIds.AddRange(run.ascendedPassiveIds);

            // 除外: 最初に出会った exclude id を 1 個だけ除外
            bool excludedOne = false;
            var filtered = new List<string>(itemIds.Count);
            foreach (var iid in itemIds)
            {
                if (!excludedOne && !string.IsNullOrEmpty(excludeItemIdOnce) && iid == excludeItemIdOnce)
                {
                    excludedOne = true;
                    continue;
                }
                filtered.Add(iid);
            }

            // 同一 itemId は 1 個扱いに dedup (= PassiveSkillManager のロジックと同じ)
            var seenItem = new HashSet<string>();
            var allSkillIds = new HashSet<string>();
            foreach (var iid in filtered)
            {
                if (string.IsNullOrEmpty(iid) || !seenItem.Add(iid)) continue;
                var data = db.GetItem(iid);
                if (data?.passiveSkills == null) continue;
                foreach (var ps in data.passiveSkills)
                {
                    if (!string.IsNullOrEmpty(ps.internalName))
                        allSkillIds.Add(ps.internalName);
                }
            }

            // 上位 Lv 抑制 (2026-06-22 高速化: 旧版は O(skills²)、 family→maxLv マップで O(skills) に短縮)
            var familyMaxLv = new Dictionary<string, int>();
            foreach (var sid in allSkillIds)
            {
                var (fam, lv) = InventorySystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(sid);
                if (lv > 0 && (!familyMaxLv.TryGetValue(fam, out int prev) || lv > prev))
                    familyMaxLv[fam] = lv;
            }
            foreach (var sid in allSkillIds)
            {
                var (fam, lv) = InventorySystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(sid);
                if (lv > 0 && familyMaxLv.TryGetValue(fam, out int maxLv) && maxLv > lv) continue;
                result.Add(sid);
            }
            return result;
        }
    }
}

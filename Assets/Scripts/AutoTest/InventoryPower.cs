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

        // 武器 Tier 係数。 2026-06-23c: items.json (正本) の rarity フィールドを直接参照する。
        // CLAUDE.md「アイテムカタログの正本は items.json」 に従い、 ID suffix パースを廃止。
        // BRONZE=T1, SILVER=T2, GOLD=T3, LEGENDARY=T4。 取得不能時のみ ID suffix フォールバック。
        private static int WeaponTierBonus(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return 0;
            var data = ItemDatabase.Instance?.GetItem(weaponId);
            if (data != null)
            {
                switch (data.rarity)
                {
                    case ItemRarity.BRONZE:    return 1;
                    case ItemRarity.SILVER:    return 3;
                    case ItemRarity.GOLD:      return 6;
                    case ItemRarity.LEGENDARY: return 10;
                    case ItemRarity.MYTHIC:    return 14;
                    default:                   return 5;
                }
            }
            // フォールバック: ItemDatabase 未初期化時 (Editor テスト等) のみ ID suffix で推定
            if (weaponId.EndsWith("_t1")) return 1;
            if (weaponId.EndsWith("_t2")) return 3;
            if (weaponId.EndsWith("_t3")) return 6;
            if (weaponId.EndsWith("_t4")) return 10;
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
                // 2026-06-23c: 剣の舞 forced top (100) は Power 帯認識を破壊するため S+2 (=6) に圧縮。
                // 5 ピース揃え時の Power 寄与は +30 程度 → Apex 帯 (≥80) 早期突破を回避しつつセット価値は残る。
                if (sc > 6) sc = 6;
                sum += sc;
            }
            Add(run.equippedWeaponId);
            Add(run.equippedDiceId);
            // 所持している出目パーツ (2026-08-17)。 **足した順に評価する** ──
            //   面が増えるほど 1 枚あたりの平均押し上げは小さくなるので、
            //   途中経過の面構成を再現しないと過大評価になる。
            if (run.diceFaceParts != null && run.diceFaceParts.Count > 0)
            {
                var acc = new List<GameLoop.DiceFaceParts.Part>(run.diceFaceParts.Count);
                var tmp = new RunState { diceFaceParts = acc };
                for (int i = 0; i < run.diceFaceParts.Count; i++)
                {
                    sum += FacePartPower(tmp, run.diceFaceParts[i]);
                    acc.Add(run.diceFaceParts[i]);
                }
            }
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
            // 出目パーツ: ID から現物へ戻して面の増分を評価する (2026-08-17b)。
            //   **呼び出し側で場合分けしない** ── ΔPower の入口をここ 1 箇所に保つ。
            if (GameLoop.DiceFaceParts.TryParseId(itemId, out var part))
            {
                if (run.diceFaceParts != null)
                    for (int i = 0; i < run.diceFaceParts.Count; i++)
                        if (run.diceFaceParts[i].Key == part.Key) return 0;   // 既所持
                return FacePartPower(run, part);
            }
            // 既所持なら delta = 0 (HashSet dedup される)
            if (itemId == run.equippedWeaponId || itemId == run.equippedDiceId) return 0;
            if (run.ownedPassiveItems != null && run.ownedPassiveItems.Contains(itemId)) return 0;
            if (run.ascendedPassiveIds != null && run.ascendedPassiveIds.Contains(itemId)) return 0;

            int delta = LearnedPriorityProvider.RawScore(itemId);
            if (delta > 6) delta = 6; // 2026-06-23c: PowerByOwnedItems と同じ cap (forced top 圧縮)

            // 武器なら Tier 係数も加算 (旧武器との差分)
            var db = ItemDatabase.Instance;
            var data = db?.GetItem(itemId);
            if (data != null && data.category == ItemCategory.Weapon)
            {
                delta += WeaponTierBonus(itemId) - WeaponTierBonus(run.equippedWeaponId);
            }
            return delta;
        }

        /// <summary>出目パーツ 1 枚を足したときの Power 増分 (2026-08-17)。
        ///
        /// <para><b>パーツには学習スコアが無い。</b> `LearnedPriorityProvider` は itemId 基準で、
        /// パーツは itemId を持たない。 手置きのスコアを与えると 233,500 ラン分の実測序列を
        /// 根拠の無い数値で汚すので、 **Power 側で表現して同じ土俵に乗せる**。</para>
        ///
        /// <para>内訳は 2 つ。
        /// ① <b>面を 1 枚足したときの期待出目の変化 × ダイス個数</b>。
        ///    面 <c>v</c> を足すと平均は <c>(Σ+v)/(n+1)</c> になるので、
        ///    <b>低い面を足すと平均が下がって負になる</b> ── 出目 1 の T2 を
        ///    素の 6 面へ足すと −1.8 で、 買わない判断が自然に出る。
        /// ② <b>Tier の効果</b>。 T3/T4 は 1 個が 2 端子へ乗るので、
        ///    そのダイスの寄与がおおむね倍になる ＝ 平均 1 個ぶんの上乗せ。
        ///    T1 は「振り直せない」代償で −1。</para>
        ///
        /// <para><b><see cref="PowerCalibration"/> は較正定数で、 実測していない。</b>
        /// 出目換算の値を Power スケール (学習スコア 0〜6・武器 Tier 1〜14) へ落とすための
        /// 割り算で、 現在は「出目 9 の T4 を素のダイスへ足すと Power +4」
        /// (最上位パッシブ 6 より下・中央値より上) になるよう置いてある。
        /// **パーツの購入頻度が想定と食い違ったら、 まずここを疑うこと。**</para></summary>
        /// <para><b>掃引のため const ではなく静的フィールドにしてある。</b>
        /// 値を変えるたびにコンパイル + ドメインリロードを挟むと 1 点あたり数分の無駄が出るので、
        /// AutoRunner が起動時に流し込む。 **実効値はサマリー冒頭に必ず印字すること** ──
        /// 掃引の途中で「どの値で回したか」が分からなくなると全部やり直しになる。</para>
        public static float PowerCalibration = 2f;

        /// <summary>T1〈振り直せない〉の代償。 負の値。</summary>
        public static float TierGainT1 = -1f;
        /// <summary>T3〈2端子接続〉の上乗せ係数 (平均面に対する倍率)。</summary>
        public static float TierGainT3 = 0.5f;
        /// <summary>T4 の上乗せ = 平均面 × <see cref="TierGainT3"/> + この定数。</summary>
        public static float TierGainT4Extra = 1.5f;

        public static int FacePartPower(RunState run, GameLoop.DiceFaceParts.Part part)
        {
            int[] faces;
            GameLoop.DiceFaceParts.Tier[] tiers;
            GameLoop.DiceFaceParts.Build(GameLoop.DiceFaceParts.BaseFaces,
                                         run != null ? run.diceFaceParts : null, out faces, out tiers);
            if (faces == null || faces.Length == 0) return 0;

            int sum = 0;
            for (int i = 0; i < faces.Length; i++) sum += faces[i];
            float meanBefore = sum / (float)faces.Length;
            float meanAfter = (sum + part.face) / (float)(faces.Length + 1);

            const int DiceCount = 5;   // 武器はすべて 5 個 (ADR-0010 柱1)
            float gain = (meanAfter - meanBefore) * DiceCount;

            float tierGain;
            switch (part.tier)
            {
                case GameLoop.DiceFaceParts.Tier.T1: tierGain = TierGainT1; break;
                case GameLoop.DiceFaceParts.Tier.T3: tierGain = meanAfter * TierGainT3; break;
                case GameLoop.DiceFaceParts.Tier.T4: tierGain = meanAfter * TierGainT3 + TierGainT4Extra; break;
                default:                             tierGain = 0f; break;
            }
            return UnityEngine.Mathf.RoundToInt((gain + tierGain) / PowerCalibration);
        }

        /// <summary>Power 換算値 (0〜6) を**準パワースケール**へ写す (2026-08-17b)。
        ///
        /// <para>学習統計がまだ無い品を、 学習済みの品と同じ土俵で比較するための橋。
        /// <see cref="LearnedPriorityProvider.PowerCuts"/> の逆写像になるよう置いてある
        /// (Power 4 → 絶対取得の下端、 Power 1 → 中立の下端、 Power 0 → 罠側)。</para>
        ///
        /// <para><b>これは暫定値であって実測ではない。</b> 学習が乗れば自動的に使われなくなるので、
        /// 「いつまでもここが効いている」なら統計が溜まっていない疑い。</para></summary>
        public static float BootstrapPowerScore(int power)
        {
            var cuts = LearnedPriorityProvider.PowerCuts;   // 降順: [0]=最上段 … [n-1]=最下段
            int p = UnityEngine.Mathf.Clamp(power, 0, cuts.Length);
            if (p <= 0) return cuts[cuts.Length - 1] - 0.5f;  // 罠側へ落とす
            return cuts[cuts.Length - p];                     // Power 4 → cuts[0], Power 1 → cuts[3]
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

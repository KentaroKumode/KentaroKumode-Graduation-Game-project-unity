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
        /// <summary>〈サーベル・ワルツ〉所持時に、 ショップのパッシブ抽選プールを
        /// [剣の舞] だけに絞る確率。 <b>既定 0.6。</b> 0 で自己加速を止める。
        ///
        /// <para>較正用に static にしてある (書き換えるのは <c>AutoRunner.saberWaltzShopBias</c> だけ)。
        /// 実効値は <c>[実効状態]</c> に印字される。</para></summary>
        public static float SaberWaltzShopBias = 0.6f;

        /// <summary>[計装 2026-09-14] パッシブ枠が何回出て、 そのうち何回 LEGENDARY 帯を引いたか。
        /// <b>差し替えの天井はここで決まる。</b> 4 枚集約に必要なのは「LEGENDARY 枠 4 回」で、
        /// 1 ラン あたりの LEGENDARY 枠がそれを下回るなら、 差し替え率を上げても届かない
        /// (差し替えは枠の<b>中身</b>を変えるだけで、 枠の<b>数</b>は増やさない)。</summary>
        public static long PassiveSlotsRolled, PassiveSlotsLegendary, DanceSwaps, DanceOffered;
        /// <summary>[計装] パッシブ枠に並んだ数 (レアリティ別・index = ItemRarity)。
        /// 取得側は <see cref="Helpers.PassiveAddHelper.AcquiredByRarity"/>。
        /// <b>提示と取得を対で見ないと「出ていないのか、 買われていないのか」が分かれない。</b></summary>
        public static readonly long[] OfferedByRarity = new long[8];
        public static void ResetSlotStats()
        {
            PassiveSlotsRolled = PassiveSlotsLegendary = DanceSwaps = DanceOffered = 0;
            System.Array.Clear(OfferedByRarity, 0, OfferedByRarity.Length);
        }

        private static ShopManager _instance;
        private static bool _shuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _shuttingDown = false; _instance = null; }

        /// <summary>**計測専用**: 出目パーツを陳列しない (2026-08-17)。 既定 false ＝ 製品挙動。
        /// 天井AI のロールアウトがパーツの面添字を持たない問題を切り分けるための遮断スイッチ。
        /// **立てっぱなしにしないこと** ── 以後の全測定がパーツ不在の条件になる。</summary>
        public static bool SuppressFacePartOffers = false;

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

        /// <summary>通常枠の Tier 提示重み。 <b>層化ホールドアウトが逆数を取るための唯一の出所</b>
        /// （AutoRunner が重みを直書きすると、 ここを変えたとき静かに食い違う）。
        /// 未知の rarity は 0.25 (4 帯均等相当) を返す。</summary>
        public static float TierOfferWeight(ItemRarity r)
        {
            foreach (var (rarity, weight) in tierWeights)
                if (rarity == r) return weight;
            return 0.25f;
        }

        // ============================================================
        //  在庫生成
        // ============================================================

        /// <summary>鑑定の眼鏡: この入店中の最低レア保証（null=無）。</summary>
        private ItemRarity? _apprMinRarity;

        /// <summary>ショップマス入店時に在庫を生成。</summary>
        public ShopInventory Generate(int floor)
        {
            var inv = new ShopInventory();

            // フロア価格倍率（FloorModifier.shopPriceMultiplier）× メタデバフ Lv1
            var floorMod = MapSystem.FloorModifierDatabase.Get(floor);
            float baseMul = floorMod != null ? floorMod.shopPriceMultiplier : 1f;
            inv.priceMultiplier = baseMul * MetaProgression.MetaDebuffApplicator.GetShopPriceMultiplier();
            // (商人の符牒 半額フックは 2026-07-18 アイテム削除に伴い除去)

            // 値下げ交渉(=強盗) の代償: 以降のショップは足元を見られる (2026-09-12)。
            //   旧仕様の「出禁」を置き換えたもの。 詳細は RunState.shopRobberyDone。
            var rsRob = GameLoop.GameManager.Instance?.Run;
            if (rsRob != null && rsRob.shopRobberyDone && RobberySurcharge != 1f)
            {
                inv.priceMultiplier *= RobberySurcharge;
                Debug.Log($"[ShopManager] 値下げ交渉の代償: 全価格 ×{RobberySurcharge:F2}");
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

            // パッシブ ×5 (2026-09-03: 3 → 5)
            //   2026-08-24: 形見〈潜行申請書〉のショップ効果はここに在ったが撤去した。
            //   段3 (5〜6層) で受け取る形見なので、 効く店がほぼ残っておらず
            //   実測で −6.7pt (足を引っ張る側) だった。 7層戦限定の効果へ振り替えた。
            //
            //   **枠を武器 (購入 0.12/ラン) と旧・拡張枠 (0.00/ラン) から移した。** 陳列数 12 は据え置き。
            //   パッシブは購入 6.12/ラン の主力枠で、 パッシブ購入数はクリア率の主要駆動源
            //   (旧測定: 7.30 → 9.37 個で 57.8% → 68.3%)。 死に枠を主力枠へ振り替える。
            for (int i = 0; i < 5; i++)
                inv.slots.Add(BuildSlot(ShopSlotKind.Passive, inv.priceMultiplier));

            // 消費 ×3 ── **回復は固定、 残り 2 枠は他 3 系統から重複なしで抽選** (2026-08-05)。
            //   回復が並ばない店があると消費で耐久を賄う設計が成立しないので 1 枠目は固定。
            //   希望回復を 4 系統目に足したため全系統は並べきれず、 残り 2 枠を
            //   シールド / 攻撃強化 / 希望 から引く。 Tier は T1〜4 から一様。
            {
                int tier0 = GameLoop.GameRng.Range(1, GameLoop.ItemIds.ConsumableMaxTier + 1, "shop.consTier");
                inv.slots.Add(BuildConsumableSlot(
                    GameLoop.ItemIds.ConsId(GameLoop.ItemIds.ConsumableFixedFamily, tier0), inv.priceMultiplier));

                // Fisher-Yates で 3 系統を並べ替え、 先頭 2 つを採用 (重複なし)。
                var pool = (string[])GameLoop.ItemIds.ConsumableRandomFamilies.Clone();
                for (int i = pool.Length - 1; i > 0; i--)
                {
                    int j = GameLoop.GameRng.Range(0, i + 1, "shop.consFamily");
                    var tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
                }
                for (int i = 0; i < 2 && i < pool.Length; i++)
                {
                    int tier = GameLoop.GameRng.Range(1, GameLoop.ItemIds.ConsumableMaxTier + 1, "shop.consTier");
                    inv.slots.Add(BuildConsumableSlot(GameLoop.ItemIds.ConsId(pool[i], tier), inv.priceMultiplier));
                }
            }

            // 武器 ×1 (2026-09-03: 2 → 1)
            //   **実測で 1 ラン 0.12 個しか買われていなかった** ── 16 訪問に 1 個。
            //   死に枠ではなく `Loadout.WouldUpgrade` が「装備更新にならない武器は買わない」と
            //   明示的に弾いているためで、 武器の主経路が素材強化 (T1→T4) だから。
            //   2 枠は過剰なので 1 枠へ。 直引き LEGENDARY の確率は §16-2 の MUST を守り据え置き。
            inv.slots.Add(BuildSlot(ShopSlotKind.Weapon, inv.priceMultiplier));

            // 出目パーツ ×2 (2026-08-17: 旧ダイス枠の置き換え。 陳列数は据え置き)
            //   **所持済みを弾いたプールから重複なしで引く。** 「出現済み」ではなく所持済みなので、
            //   序盤に見送った品は何度でも並ぶ ── 買わない判断がランを縛らないようにする。
            // **計測用の遮断 (2026-08-17)。** 出目パーツを一切並べない条件を作る。
            //   天井AI のロールアウトは面の「値」しか持たず添字を持たないので、
            //   未来のターンで T1(振り直し禁止)/T3/T4(ゴースト配線) を評価できない。
            //   パーツ 0 個なら誤差が消えるので、 技量差が回復するかで仮説を切り分ける。
            if (!SuppressFacePartOffers)
            {
                var ownedParts = GameLoop.GameManager.Instance?.Run?.diceFaceParts;
                var pool = GameLoop.DiceFaceParts.RemainingKinds(ownedParts);
                for (int i = 0; i < 2 && pool.Count > 0; i++)
                {
                    int pick = GameLoop.GameRng.Range(0, pool.Count, "shop.facePart");
                    inv.slots.Add(BuildFacePartSlot(pool[pick], inv.priceMultiplier));
                    pool.RemoveAt(pick);   // 同じ店に同じパーツを 2 つ並べない
                }
            }

            // 武器強化素材 ×1（在庫無限、価格は base × 2^N × priceMultiplier）
            inv.slots.Add(new ShopSlot
            {
                kind = ShopSlotKind.WeaponMaterial,
                itemId = null,
                price = inv.CurrentMaterialPrice,
                sold = false,
            });

            // [撤去 2026-09-03 / 完全削除 2026-09-11] インベントリ拡張 ×1。
            //   容量制限が 2026-07-29 に撤廃されていたので **買っても何も起きない**枠だった。
            //   実測 1000 ラン で購入 0.00 個 ── §24 が廃止した〈ダイス 10 種〉と同じ死に枠。
            //   枠はパッシブへ振り替え済み。 2026-09-11 に容量機構ごと削除した。

            // (旧〈売り渋り〉の陳列削減・値上げは 2026-08-10 に撤去。 1000 ラン で −0.1pt / p=1.000 の
            //  完全な無効だった ── 陳列 12 枠に対し BOT は 1 ラン 20 個買うので、 枠を減らしても
            //  代わりの品が並ぶだけ。 軸は〈通行料〉へリワークし、 効果はショップの外へ移した。)

            // 2026-06-23: 上位互換アップグレード割引 (同家系下位所持時、 1G/Tier段の超格安)
            //   ── 上位 Tier を取らない方が得という設計上の罠を撲滅
            ApplyUpgradeDiscounts(inv, GameLoop.GameManager.Instance?.Run);

            // 2026-06-22: メタバフ「特売品」 を 1/2/3 個ランダム枠に付与 (Passive/Consumable/Weapon/Dice のみ対象)
            ApplySaleDiscounts(inv);

            // [計装] 家系品の提示を段別に数える (§ FamilyTierStats ①)。
            //   取得の分布が「需要」なのか「供給」なのかを分けるための分母。
            //   割引の後に置く ── 提示された事実だけを数えるので価格には依存しない。
            {
                var fdb = ItemDatabase.Instance;
                for (int i = 0; i < inv.slots.Count; i++)
                {
                    var s = inv.slots[i];
                    if (s == null || string.IsNullOrEmpty(s.itemId)) continue;
                    // **パッシブ枠だけを数える (2026-09-03 修正)。** 初版はカテゴリを見ずに
                    //   passiveSkills を走査していたため、 **武器枠を家系提示として数えていた** ──
                    //   武器 17 品のうち多くが家系パッシブを内蔵しており (延べ 36 件で、
                    //   パッシブ側の 15 件より多い)、 ① の数字は武器が支配していた。
                    //   武器は 1 つしか装備できないので、 家系の入手経路としては別物。 混ぜない。
                    if (s.kind != ShopSlotKind.Passive) continue;
                    var data = fdb?.GetItem(s.itemId);
                    if (data?.passiveSkills == null) continue;
                    string fam = null; int lv = 0;
                    foreach (var ps in data.passiveSkills)
                    {
                        if (string.IsNullOrEmpty(ps.internalName)) continue;
                        var (f, l) = InventorySystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ps.internalName);
                        if (l > lv) { lv = l; fam = f; }
                    }
                    if (lv > 0) AutoTest.FamilyTierStats.NoteOffer(fam, lv);
                }
            }

            Current = inv;
            MetaProgression.Achievements.AchievementService.BeginShop();
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
                if (s.kind == ShopSlotKind.WeaponMaterial) continue;
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
                // 2026-06-23c: 昇華 (ascended) 済み下位品も upgrade 割引の根拠とする
                if (run.ascendedPassiveIds != null)
                    foreach (var id in run.ascendedPassiveIds) CheckOwned(id);

                if (maxStep <= 0) continue;
                // 割引適用 (1G/Tier段、 最低 1G、 既存より安くなる場合のみ反映)
                int newPrice = System.Math.Max(1, maxStep);
                if (newPrice >= s.price) continue;
                s.originalPrice = s.price;
                s.discountPct = (int)(100f * (1f - (float)newPrice / s.price));
                s.price = newPrice;
                // [計装] 梯子が登られているか (§ FamilyTierStats)。 提示と購入を別に数える ──
                //   差が「1G なのに見送られた」量で、 そこが大きいなら段は枠の問題ではなく需要の問題。
                s.upgradeStep = maxStep;
                AutoTest.FamilyTierStats.NoteUpgradeOffered();
                Debug.Log($"[ShopManager] 上位互換アップグレード割引 slot={i} {s.itemId} ({maxStep}段差) {s.originalPrice}G→{s.price}G ({s.discountPct}%off)");
            }
        }

        /// <summary>2026-06-22: メタバフ refundLevel に応じた個数の特売品をランダム選出。
        /// 割引率は <b>固定段からの重み付き抽選</b> (2026-09-12・MetaPanel.TradeSaleTiers)、
        /// 元価格を originalPrice に保持。
        /// 対象は Passive/Consumable/Weapon/Dice (アイテム枠) のみ、 強化素材は除外。</summary>
        /// <param name="saltBase">乱数の塩。 <b>リロール後の再適用では必ず変えること</b> ──
        /// GameRng はキー+塩のハッシュなので、 同じ塩で呼ぶと<b>同じ枠に同じ割引</b>が出る。</param>
        private void ApplySaleDiscounts(ShopInventory inv, int saltBase = 0)
        {
            int n = MetaProgression.MetaBuffApplicator.GetSaleItemCount();
            if (n <= 0 || inv?.slots == null) return;
            SaleShops++; SaleWanted += n;
            // 特売対象になり得るスロットを抽出 (商品枠かつ未売却かつ itemId あり)
            var candidates = new System.Collections.Generic.List<int>();
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var s = inv.slots[i];
                if (s == null || s.sold) continue;
                if (string.IsNullOrEmpty(s.itemId)) continue;
                if (s.kind == ShopSlotKind.WeaponMaterial) continue;
                if (s.discountPct > 0) continue; // 2026-06-23: 上位互換割引済は特売対象外 (二重割引防止)
                candidates.Add(i);
            }
            // Fisher-Yates シャッフルで n 個ランダム選出
            int pick = Mathf.Min(n, candidates.Count);
            // [計装 2026-09-17] 商才の枠が**実際に何個乗ったか**。
            //   候補は「未売却 かつ 商品枠 かつ 既に割引されていない」スロットに限られるので、
            //   段を積んでも棚の空きが上限になる (＝飽和) 疑いを数字で見る。
            SaleCandidates += candidates.Count; SaleApplied += pick;
            for (int i = 0; i < pick; i++)
            {
                int j = GameLoop.GameRng.Range(i, candidates.Count, "shop.shuffle", saltBase + i);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                var s = inv.slots[candidates[i]];
                // 2026-09-12: 一様 20〜60% → **重み付きの固定段**。
                //   既定 {15,30,50} 50:35:15 = 期待値 25.50% / 商才r10 は 99% が加わり 29.70%。
                //   詳細は MetaPanel.TradeSaleTiers。
                int discount = MetaProgression.MetaBuffApplicator.RollSaleDiscountPct("shop.discount", saltBase + i);
                s.originalPrice = s.price;
                s.discountPct = discount;
                s.price = Mathf.Max(1, Mathf.CeilToInt(s.price * (1f - discount / 100f)));
                SaleDiscountSum += s.originalPrice - s.price;   // 実際に浮いたゴールド
                Debug.Log($"[ShopManager] 特売 slot={candidates[i]} {s.itemId} -{discount}% ({s.originalPrice}G→{s.price}G)");
            }
        }

        /// <summary>[計装] TryReroll が false を返した理由の内訳。</summary>
        public static long RerollFailNoShop, RerollFailNoGold, FreeRolls, RerollFailCapped;
        public static void ResetRerollFailStats()
        { RerollFailNoShop = RerollFailNoGold = FreeRolls = RerollFailCapped = 0; }

        /// <summary>[A/B 用ノブ] <b>1 店あたりのリロール上限。 -1 = 無制限 (製品)</b> (2026-09-17)。
        ///
        /// <para>ここ (TryReroll の入口) 1 箇所で効かせる ── AutoRunner のリロール経路は 5 箇所あり
        /// (品質リロール / 余剰再投資 / 最終店の消耗品 / Super 全賭け / 強盗直前)、
        /// どれも <b>失敗を break として扱う</b>ので呼び出し側の改修は要らない。</para>
        ///
        /// <para>〈試し振りの符牒〉の無料リロールも rerollCount を進めるので上限に含まれる。</para>
        /// </summary>
        public static int RerollHardCap = -1;

        /// <summary>リロール後に特売を再適用するか。 <b>既定 false = 従来 (消える)。</b>
        /// A/B で測ってから畳む。 true にすると<b>リロールで特売を引き直せる</b>ので、
        /// リロール自体が強くなりすぎる懸念がある ── そこも一緒に見ること。</summary>
        public static bool RerollKeepsSale = false;

        /// <summary>[計装] 商才〈特売〉の飽和を見る。 希望した枠数 / 候補数 / 実際に乗った数。</summary>
        public static long SaleShops, SaleWanted, SaleCandidates, SaleApplied, SaleDiscountSum;
        public static void ResetSaleStats()
        { SaleShops = SaleWanted = SaleCandidates = SaleApplied = SaleDiscountSum = 0; }

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
                    basePrice = GameLoop.GameRng.Range(item.buyPrice.min, item.buyPrice.max + 1, "shop.price");
                else
                    basePrice = 10;
                price = Mathf.CeilToInt(basePrice * priceMultiplier) + ItemPriceAdd;
            }
            return new ShopSlot { kind = kind, itemId = id, price = price, sold = false };
        }

        /// <summary>出目パーツ枠を組む。 価格は Tier だけで決まる ── 出目の値では変えない。
        ///
        /// <para><b>面が 1 枚増えること自体の価値をカタログ中央値の 11 に置き、
        /// Tier が 1 段動くごとに ±3。</b> 実カタログの basePrice は
        /// 消費 0〜13 (中央 5) / パッシブ 0〜16 (中央 11) / 武器 8〜20 なので、
        /// 8〜17 はその帯に収まる。 パーツはラン中ずっと残る恒久強化なので、
        /// 1 回で消える消費 (中央 5) より高く、 同じく恒久のパッシブ (中央 11) と同格に置く。</para>
        ///
        /// <para><b>T3 → T4 を +3 に留めたのは、 効果差が「同一端子に重ねたとき +3」
        /// という条件付きの上乗せだけだから。</b> 段差の幅と効果の幅を対応させておくと、
        /// 後から値を動かすときに根拠を辿れる。</para></summary>
        /// <summary>全アイテムの店頭価格に一律で足す額 (2026-09-17)。 <b>倍率ではなく加算</b> ──
        /// 安い品ほど相対的に効くので、 「安物を数で買う」経路を狙って締められる。
        /// 素の帯は BRONZE 7 / SILVER 8 / GOLD 9 / LEGENDARY 10 G。</summary>
        public static int ItemPriceAdd = 0;

        private ShopSlot BuildFacePartSlot(GameLoop.DiceFaceParts.Part part, float priceMultiplier)
        {
            int basePrice;
            switch (part.tier)
            {
                case GameLoop.DiceFaceParts.Tier.T1: basePrice =  8; break;   // 代償 (振り直せない) 込み
                case GameLoop.DiceFaceParts.Tier.T2: basePrice = 11; break;   // 素の増設 = カタログ中央値
                case GameLoop.DiceFaceParts.Tier.T3: basePrice = 14; break;
                default:                             basePrice = 17; break;
            }
            return new ShopSlot
            {
                kind = ShopSlotKind.FacePart,
                facePart = part,
                // **itemId を持たせる** (2026-08-17b)。 パーツは items.json に無いが、
                //   提示/取得の記録も学習の集計も itemId をキーにしている。
                //   null のままだと学習経路に一切乗らず、 手置きの評価式に頼るしかなくなる。
                itemId = GameLoop.DiceFaceParts.Id(part),
                price = Mathf.CeilToInt(basePrice * priceMultiplier) + ItemPriceAdd,
                sold = false,
            };
        }

        /// <summary>消費枠を id 直指定で組む (2026-08-04)。 系統ごとに 1 枠を保証するため、
        /// レア重み抽選 (PickItemByKind) を通さず、 呼び出し側が決めた id をそのまま並べる。
        /// 価格は items.json の basePrice を採用 ── Tier に比例 (5/12/25/45G)。</summary>
        private ShopSlot BuildConsumableSlot(string id, float priceMultiplier)
        {
            var item = ItemDatabase.Instance?.GetItem(id);
            int basePrice = 10;
            if (item?.buyPrice != null && item.buyPrice.max >= item.buyPrice.min)
                basePrice = GameLoop.GameRng.Range(item.buyPrice.min, item.buyPrice.max + 1, "shop.price");
            return new ShopSlot
            {
                kind = ShopSlotKind.Consumable,
                itemId = item != null ? id : null,
                price = Mathf.CeilToInt(basePrice * priceMultiplier) + ItemPriceAdd,
                sold = false,
            };
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
                    // 2026-06-23c: 60% 短絡で全レア無差別ピックではなく、 60% でプール自体を佯狂者に絞る (Tier 重みは下流の RollTier に委譲)。
                    // これにより [佯狂者] セット組み立てを後押ししつつ LEG 出現率は 0.05 のまま維持される。
                    if (owned.Contains(GameLoop.YokyoSet.Bell)
                        && GameLoop.HopeSystem.GetTier(run) >= GameLoop.HopeTier.Despair)
                    {
                        var yokyo = pool.FindAll(it => System.Array.IndexOf(GameLoop.YokyoSet.All, it.internalName) >= 0);
                        if (yokyo.Count > 0 && GameLoop.GameRng.Chance(0.6f, "shop.yokyo")) pool = yokyo;
                    }

                    // [撤去 2026-09-14] サーベル・ワルツの「プール全体を剣の舞へ絞る」。
                    //   **帯をまたぐ差し替えだったのが自己加速の正体。** 剣の舞は当時
                    //   BRONZE 2 / SILVER 1 / LEGENDARY 1 で、 プールを絞ると RollTier が
                    //   BRONZE を 47.5% で引く ＝ 7G の札が次々出て 4 枚が揃ってしまう。
                    //   実測 4 枚集約 27.9% (3 層で既に 86/2000 成立)。 撤去すると 3.1%。
                    //   差し替えは **同一レアリティ内**へ移した (下の RollTier の後)。
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

            // サーベル・ワルツ([剣の舞]): 所持(昇華含む)時、 <b>LEGENDARY 帯に限って</b>
            //   一定確率で剣の舞へ差し替える (2026-09-14)。
            //
            //   **帯をまたがない**のが要点。 旧実装は抽選<b>前</b>にプールごと絞っており、
            //   安い札 (当時 BRONZE 7G が 2 枚) が入口になって残り 3 枚を呼び込んでいた。
            //   ここなら「LEGENDARY が出る枠が、 たまに剣の舞になる」だけで、
            //   BRONZE/SILVER/GOLD の枠は一切動かない ＝ 出現率の天井は LEGENDARY 重み (0.05) のまま。
            //
            //   **Chance は確率 0 でも必ず引く** ── 引く回数を条件で変えると
            //   同一シードのペア比較が壊れる。
            if (kind == ShopSlotKind.Passive)
            {
                PassiveSlotsRolled++;
                if (targetRarity == ItemRarity.LEGENDARY) PassiveSlotsLegendary++;
            }
            if (kind == ShopSlotKind.Passive && targetRarity == ItemRarity.LEGENDARY)
            {
                var run2 = GameLoop.GameManager.Instance?.Run;
                // **入口は「剣の舞を 1 枚でも持っていれば」** (2026-09-14)。
                //   〈サーベル・ワルツ〉所持を条件にしていたが、 4 枚とも LEGENDARY にした結果
                //   そのワルツ自体が出ず<b>差し替えが一度も起動しなかった</b> (実測 集約 0/4000、
                //   確率を 0.0→0.6 に上げても 0 のまま＝鶏と卵)。 どの 1 枚からでも始まる形にする。
                if (run2 != null && GameLoop.SwordDanceSet.OwnedCount(run2) > 0)
                {
                    var dance = byTier.FindAll(it => GameLoop.SwordDanceSet.IsDance(it.internalName));
                    if (dance.Count > 0
                        && GameLoop.GameRng.Chance(SaberWaltzShopBias, "shop.dance"))
                    { byTier = dance; DanceSwaps++; }
                }
            }

            if (byTier.Count == 0)
            {
                // フォールバック: そのTierが存在しなければ全体からランダム
                return PickWithSparkBias(pool, kind);
            }
            var picked = PickWithSparkBias(byTier, kind);
            // [計装] 剣の舞が「棚に並んだ」回数。 取得 (SwordDanceSet.Acquired) と分けて数える。
            if (picked != null && GameLoop.SwordDanceSet.IsDance(picked.internalName)) DanceOffered++;
            if (picked != null && kind == ShopSlotKind.Passive)
            {
                int ri = (int)picked.rarity;
                if (ri >= 0 && ri < OfferedByRarity.Length) OfferedByRarity[ri]++;
            }
            return picked;
        }

        /// <summary>2026-07-25 v6: 種火 r1 で対応キーワード系アイテムの抽選重み +60%。
        /// Passive カテゴリのみに作用。 該当キーワードに属さないアイテムは重み 1.0 のまま。</summary>
        private CompleteItemData PickWithSparkBias(List<CompleteItemData> list, ShopSlotKind kind)
        {
            if (list == null || list.Count == 0) return null;
            if (kind != ShopSlotKind.Passive) return list[GameLoop.GameRng.Range(0, list.Count, "shop.pickAny")];

            float total = 0f;
            var weights = new float[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                string kw = MetaProgression.SparkStarterPicker.KeywordOf(list[i]);
                float w = string.IsNullOrEmpty(kw)
                    ? 1.0f
                    : MetaProgression.MetaBuffApplicator.GetSparkAppearanceWeightMul(kw);
                weights[i] = w;
                total += w;
            }
            if (total <= 0f) return list[GameLoop.GameRng.Range(0, list.Count, "shop.pickFlat")];
            float r = GameLoop.GameRng.Value("shop.pickWeighted") * total;
            for (int i = 0; i < list.Count; i++)
            {
                r -= weights[i];
                if (r <= 0f) return list[i];
            }
            return list[list.Count - 1];
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

            float r = GameLoop.GameRng.Value("shop.rarity") * total;
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
                // 燈火 r10: 不足分は希望で払える。 未解禁ならゴールドのみで判定する。
                if (!GameLoop.HopePayment.TryPay(run, price, "強化素材"))
                { Log("ゴールド不足"); return false; }
                MetaProgression.Achievements.AchievementService.NoteShopPurchase(run.coins);
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
            // 燈火 r10 を含めた支払い可能額で判定する。 未解禁なら run.coins と同じ。
            if (GameLoop.HopePayment.Affordable(run) < slot.price)
            {
                // 行動台帳〈見送り〉: **買おうとして届かなかった**ときだけ積む。
                //   棚を眺めただけの回は通らない ── ここまで来たのは購入の意思があった証拠。
                GameLoop.RunChronicle.Buy(run, GameLoop.RunChronicle.BuyForgo, slot.itemId, slot.price);
                Log("ゴールド不足"); return false;
            }

            // 恒久デバフ「ヤルノクの嫉妬」: 1ショップで通常品は1個まで
            if (MetaProgression.PermanentDebuffEffects.HasEnvy(run) && Current.purchaseCount >= 1)
            {
                Log($"恒久デバフ {MetaProgression.PermanentDebuffIds.Envy}: このショップでは既に1個購入済み");
                return false;
            }

            if (!GameLoop.HopePayment.TryPay(run, slot.price, slot.itemId))
            { Log("支払いに失敗"); return false; }
            MetaProgression.Achievements.AchievementService.NoteShopPurchase(run.coins);
            Current.purchaseCount++;

            // 〈釣り銭の受け皿〉: 買うたび 希望+1 (2026-09-15)。 **払った直後に鳴らす** ──
            //   希望払い (HopePayment) で希望を使った場合でも、 買い物そのものへの見返りは出す。
            //   1 ラン の購入は 24.6 件、 戦闘損は −38.2/ラン なので実質の回復源になる。
            if (run.OwnsPassive(ItemIds.ChangeTray))
                GameLoop.HopeSystem.ApplyFood(run, 1);

            switch (slot.kind)
            {
                case ShopSlotKind.Passive:
                case ShopSlotKind.Weapon:
                    InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(run, slot.itemId);
                    GameLoop.Loadout.TryAutoEquip(run, slot.itemId);
                    break;
                case ShopSlotKind.FacePart:
                    // **所持リストがそのまま抽選プールの除外集合**になる (追加の状態を持たない)。
                    run.diceFaceParts.Add(slot.facePart);
                    Debug.Log($"[ShopManager] 出目パーツ装着: {GameLoop.DiceFaceParts.Label(slot.facePart)}"
                            + $" ({GameLoop.DiceFaceParts.Describe(slot.facePart.tier)})");
                    break;
                case ShopSlotKind.Consumable:
                    run.TryAddConsumable(slot.itemId);
                    break;
            }
            // 行動台帳 (RunChronicle): 買った品を 1 行積む。
            //   強化素材は上で早期 return しているのでここには来ない
            //   ──「物語の山」にならないので台帳にも載せない。
            {
                string code = null;
                switch (slot.kind)
                {
                    case ShopSlotKind.Weapon:     code = GameLoop.RunChronicle.BuyWeapon;     break;
                    case ShopSlotKind.FacePart:   code = GameLoop.RunChronicle.BuyDice;       break;
                    case ShopSlotKind.Consumable: code = GameLoop.RunChronicle.BuyConsumable; break;
                    case ShopSlotKind.Passive:    code = GameLoop.RunChronicle.BuyPassive;    break;
                }
                if (code != null)
                    GameLoop.RunChronicle.Buy(run, code,
                        slot.kind == ShopSlotKind.FacePart
                            ? GameLoop.DiceFaceParts.Label(slot.facePart) : slot.itemId,
                        slot.price);
            }

            // ショップ購入記録 (BOT 売却判定で「ショップ由来」 のみ売却可)
            if (slot.kind == ShopSlotKind.Passive || slot.kind == ShopSlotKind.Consumable
                || slot.kind == ShopSlotKind.Weapon)
            {
                run.shopPurchasedCounts.TryGetValue(slot.itemId, out int prev);
                run.shopPurchasedCounts[slot.itemId] = prev + 1;
            }
            slot.sold = true;
            // [計装] 1G アップグレードが**成約**した回数 (§ FamilyTierStats ②)。
            //   提示だけ数えても「並んでいた」しか分からない。 梯子を登った回数はこちら。
            if (slot.upgradeStep > 0) AutoTest.FamilyTierStats.NoteUpgradeBought(slot.upgradeStep);

            var data = ItemDatabase.Instance?.GetItem(slot.itemId);
            // パーツは ItemDatabase に無い ── GetItem は null を返すので表示名は自前で引く。
            string label = data != null ? data.displayName
                         : (GameLoop.DiceFaceParts.LabelOfId(slot.itemId) ?? slot.itemId);
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
            if (Current == null || run == null) { RerollFailNoShop++; return false; }
            // A/B: 1 店あたりの上限。 既定 -1 は無制限なのでここは素通りする。
            if (RerollHardCap >= 0 && Current.rerollCount >= RerollHardCap)
            { RerollFailCapped++; return false; }
            int price = Current.CurrentRerollPrice;

            // 〈試し振りの符牒〉: **各ショップの最初の 1 回だけ**リロードが無料 (2026-09-15)。
            //   店ごとにリセットされる (rerollCount は Generate で 0 に戻る)。
            //   1 ラン のリロールは 5.56 回・86G なので、 店数ぶん ≒ 30G 相当。
            bool freeRoll = price > 0 && Current.rerollCount == 0
                            && run.OwnsPassive(ItemIds.TrialTossToken);
            if (freeRoll) { price = 0; FreeRolls++; }

            if (run.coins < price) { RerollFailNoGold++; Log($"リロール: ゴールド不足 ({price}G 必要)"); return false; }
            if (freeRoll) Log("〈試し振りの符牒〉: この店の初回リロールは無料");

            run.coins -= price;
            run.coinsSpent += price;
            MetaProgression.Achievements.AchievementService.NoteShopReroll();
            GameLoop.RunChronicle.Reroll(run, price);
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

            // **リロールは特売を消していた** (2026-09-17 発見)。 ApplySaleDiscounts は棚生成時に
            //   1 回だけ呼ばれ、 TryReroll は BuildSlot で作り直すだけなので割引が失われる。
            //   BOT は 1 ラン 5.56 回リロールするので、 商才を積むほど大きくなる割引を
            //   そのたび捨てていた (商才 r9 が Bal 比 −7.58pt だった一因の候補)。
            //   **塩を変える** ── 同じ塩だと毎回同じ枠に同じ割引が出る。
            if (RerollKeepsSale) ApplySaleDiscounts(Current, Current.rerollCount * 64);

            Debug.Log($"[ShopManager] リロール#{Current.rerollCount}: -{price}G / {rerolled}枠更新 / 次回{Current.CurrentRerollPrice}G");
            MetaProgression.MetaBuffApplicator.RollRefund(price, run);
            OnShopUpdated?.Invoke();
            return true;
        }

        // ============================================================
        //  値下げ交渉 (=強盗)
        // ============================================================

        /// <summary><b>強盗後のショップ価格倍率 (2026-09-12)。</b> 既定 2.0 = +100%。
        ///
        /// <para>旧仕様の「出禁」(以降のショップマスを素通り) を置き換えたもの。 出禁は
        /// <b>強盗報酬の 90〜180G と同じ資源を指して打ち消し合っていた</b> ── 現金を渡してから
        /// 使い道を全部閉じる設計だった。 価格割増なら報酬と罰が同じ通貨で釣り合う。</para>
        ///
        /// <para><b>1.0 にすると罰が無くなる。</b> 「BOT の方策修正だけ」と
        /// 「方策修正 + 価格割増」 を切り分けて測るための退避口。
        /// <b>static はドメインリロードで既定へ戻る</b>ので、 バッチ側が毎回設定すること。</para></summary>
        public static float RobberySurcharge = 2.0f;
        /// <summary>強盗の希望コスト。 -1 = 既定 (HopeSystem.EvilChoiceCost)。 切り分け用。</summary>
        public static int RobberyHopeCost = -1;

        /// <summary><b>旧仕様の「出禁」へ戻す退避経路 (既定 false)。</b> true にすると強盗後の
        /// ショップマスを素通りする ── 2026-09-12 以前の挙動。
        ///
        /// <para><b>比較スイープ専用。</b> 今回は BOT の方策修正 (買う前に撃つ / ランで最後の店に限定) と
        /// 罰の設計変更 (出禁 → 価格割増) を同時に入れたので、 <b>どちらが効いたのか</b>を
        /// 分けるにはこの経路が要る。 true + 新方策 = 方策修正だけの効果。
        /// <see cref="RobberySurcharge"/> と併用しないこと (true のときは割増を 1.0 にする)。</para></summary>
        public static bool RobberyBlocksShops;

        /// <summary>[計装] 強盗の試行回数。 勝敗は <c>GameManager.RobberyWins/RobberyLosses</c>。
        /// static なのでバッチ開始時に 0 へ戻すこと。</summary>
        public static long RobberyAttempts;
        /// <summary>[計装] 強盗を撃った層の分布 (1..7 を index 0..6)。</summary>
        public static readonly long[] RobberyByFloor = new long[7];

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
            // 1 ラン 1 回。 **上限の撤廃は保留 (2026-09-13)。**
            //   撤廃案 (毎回エリート戦を代償に何度でも撃てる) は、 発動率 11% では
            //   極点が +3pt に届かないという算数から出したもの。 ただし その 11% は
            //   **BOT が「ラン最後の店」でしか撃たなかった方策の欠陥**でも説明がつく
            //   (最後の店で撃つと以降に店が無く、 手持ちのゴールドが死ぬ)。
            //   方策の欠陥を先に直して測り、 それでも届かないときだけ規則を触る。
            if (run.shopRobberyDone) { Log("値下げ交渉: 既に 1 回実行済み"); return false; }

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

            GameLoop.HopeSystem.ApplyEvilChoice(run,
                RobberyHopeCost >= 0 ? RobberyHopeCost : GameLoop.HopeSystem.EvilChoiceCost);
            run.shopRobberyDone = true;
            run.shopRobberyInProgress = true;
            RobberyAttempts++;
            // [計装] **何層で撃っているか**。 これが無いと「弱い段階で誤爆している」を
            //   口で言うだけになる ── 実際 2026-09-13 に、 階層ゲートが 1 つも無い条件を
            //   「6 層で撃つ」と説明していた。 ログはバッチ中に無効化されるので計数で持つ。
            {
                int f = Mathf.Clamp(run.currentFloor, 1, RobberyByFloor.Length) - 1;
                RobberyByFloor[f]++;
            }

            Debug.Log($"[ShopManager] 値下げ交渉(強盗): 希望-{GameLoop.HopeSystem.EvilChoiceCost}, "
                    + $"在庫{loot.Count}件をスナップ, 以降のショップ価格 ×{RobberySurcharge:F2}");
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
            // (商人の符牒 売却不可フックは 2026-07-18 アイテム削除に伴い除去)

            if (source == SellSource.WeaponMaterial)
            {
                if (run.weaponMaterials <= 0) { Log("素材がない"); return false; }
                int sellPrice = 15; // 基準売値（後で調整可）
                run.weaponMaterials--;
                int gain = GameLoop.GoldIncome.GainExempt(run, sellPrice, "強化素材売却");
                MetaProgression.Achievements.AchievementService.NoteShopSale();
                Debug.Log($"[ShopManager] 強化素材売却: +{gain}G");
                OnShopUpdated?.Invoke();
                return true;
            }

            var list = source == SellSource.Passive ? run.ownedPassiveItems : run.ownedConsumables;
            if (list == null || listIndex < 0 || listIndex >= list.Count) return false;

            string id = list[listIndex];
            // T4-D〈破産〉: 売却額 −50% (§15-2 v3.0)。 **売却不能にはしない** ── 経路は残す。
            int price = Mathf.Max(1, Mathf.RoundToInt(
                ResolveSellPrice(id) * MetaProgression.MetaDebuffApplicator.GetSellPriceMultiplier()));
            // 除去は PassiveAddHelper.RemoveAt に寄せる
            if (source == SellSource.Passive)
                InventorySystem.Helpers.PassiveAddHelper.RemoveAt(run, listIndex);
            else
                list.RemoveAt(listIndex);
            // ショップ購入記録があれば在庫を 1 減らす (BOT 用、 売却可能在庫トラッキング)
            if (run.shopPurchasedCounts.TryGetValue(id, out int shopStock) && shopStock > 0)
                run.shopPurchasedCounts[id] = shopStock - 1;
            int gainPrice = GameLoop.GoldIncome.GainExempt(run, price, "売却");
            MetaProgression.Achievements.AchievementService.NoteShopSale();
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
                return GameLoop.GameRng.Range(data.sellPrice.min, data.sellPrice.max + 1, "shop.sellPrice");
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

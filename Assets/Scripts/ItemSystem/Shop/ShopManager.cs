using System.Collections.Generic;
using UnityEngine;
using GameLoop;

namespace ItemSystem.Shop
{
    /// <summary>
    /// ショップの在庫生成と取引処理を担当するシングルトン。
    /// GameManager がショップマス到達時に Generate() を呼び、UI/入力が TryBuy/TrySell を呼ぶ。
    /// </summary>
    public class ShopManager : MonoBehaviour
    {
        /// <summary>〈サーベル・ワルツ〉所持時に、 ショップのパッシブ抽選プールを
        /// [剣の舞] だけに絞る確率。 <b>既定 0.15</b> (2026-11-04: 0.6 から)。 0 で自己加速を止める。
        ///
        /// <para>0.6 だと 1 枚目を取った瞬間に LEGENDARY 枠の 6 割が剣の舞になり、 棚に並ぶ剣の舞の約半分が差し替え由来・
        /// 4 枚の成立が 1 ランの 52.8% に達していた (説明文の「まれに」と食い違っていた)。</para>
        ///
        /// <para>較正用に static にしてある (書き換えるのは <c>AutoRunner.saberWaltzShopBias</c> だけ)。
        /// 実効値は <c>[実効状態]</c> に印字される。</para></summary>
        public static float SaberWaltzShopBias = 0.15f;

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

        /// <summary>**計測専用**: 賽の枠を陳列しない (2026-08-17 は出目パーツ・2026-11-18 から賽)。 既定 false ＝ 製品挙動。
        /// 名前は測定の設定 (suppressFacePartOffers) と合わせて据え置き。
        /// **立てっぱなしにしないこと** ── 以後の全測定が賽の枠が無い条件になる。</summary>
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

        // 2026-11-22 (neon 最終仕上げ §1-3): 秘宝の枠は深さ (Run.Depth) で重みを替える (段 1 は画面の層だった・段 3 で深さへ)。
        //   序盤 深さ 1 B55/S32/G11/L2・中盤 深さ 3 B35/S35/G22/L8・終盤 深さ 5〜7 B20/S30/G32/L18。 上の tierWeights (B45/S32/G18/L5) は消耗品の枠と層の分からない時。
        private static readonly (ItemRarity rarity, float weight)[] passiveTierEarly =
            { (ItemRarity.BRONZE, 0.55f), (ItemRarity.SILVER, 0.32f), (ItemRarity.GOLD, 0.11f), (ItemRarity.LEGENDARY, 0.02f) };
        private static readonly (ItemRarity rarity, float weight)[] passiveTierMid =
            { (ItemRarity.BRONZE, 0.35f), (ItemRarity.SILVER, 0.35f), (ItemRarity.GOLD, 0.22f), (ItemRarity.LEGENDARY, 0.08f) };
        private static readonly (ItemRarity rarity, float weight)[] passiveTierLate =
            { (ItemRarity.BRONZE, 0.20f), (ItemRarity.SILVER, 0.30f), (ItemRarity.GOLD, 0.32f), (ItemRarity.LEGENDARY, 0.18f) };

        /// <summary>秘宝の枠のレア度の重み (<b>深さ</b>で替える・neon の「序盤 1〜2 / 中盤 3〜4 / 終盤 5〜6 層」は深さの番号: 深さ 1 = 序盤・3 = 中盤・5/6/7 = 終盤)。
        /// depth ≤ 0 (ファーム層・分からない) は <see cref="tierWeights"/>。</summary>
        private static (ItemRarity rarity, float weight)[] PassiveTierWeights(int depth)
            => depth <= 0 ? tierWeights : depth <= 2 ? passiveTierEarly : depth <= 4 ? passiveTierMid : passiveTierLate;

        // 2026-11-07: 武器枠は BRONZE 90% / SILVER 10% / GOLD 0 / LEGENDARY 0 (パッシブより控えめに)。
        //   武器のレアリティ自体は SILVER / GOLD / LEGENDARY のまま (段 2 / 3 / 4)・BRONZE の武器は無い。 RollTier はプールに実在する
        //   レアリティだけで正規化するので、 実質は SILVER (段 2) だけが出る。 段 3 を時々出したければ GOLD に小さな重みを足す。
        // 2026-06-22: 武器枠用の Tier 重み。 T4 (LEGENDARY) を 0.005 (0.5%/枠) に絞り、
        // フルラン (~6 ショップ × 1.5 武器枠 = 9 武器枠) × 10 ラン = 90 枠中 0.5 枠出現 ≒ 10 ランに 1 回ペース。
        // 強化ルート完走の代替路線として 「直接 T4 を引く運要素」 を残す設計。
        private static readonly (ItemRarity rarity, float weight)[] weaponTierWeights = new[]
        {
            (ItemRarity.BRONZE,    0.90f),
            (ItemRarity.SILVER,    0.10f),
            (ItemRarity.GOLD,      0f),
            (ItemRarity.LEGENDARY, 0f),
        };

        /// <summary>通常枠の Tier 提示重み。 <b>層化ホールドアウトが逆数を取るための唯一の出所</b>
        /// （AutoRunner が重みを直書きすると、 ここを変えたとき静かに食い違う）。
        /// 未知の rarity は 0.25 (4 帯均等相当) を返す。 <paramref name="passiveFloor"/> = 秘宝の枠の重みを引く深さ (Run.Depth・0 = 消耗品の枠・層別でない重み)。</summary>
        public static float TierOfferWeight(ItemRarity r, int passiveFloor = 0)
        {
            foreach (var (rarity, weight) in PassiveTierWeights(passiveFloor))
                if (rarity == r) return weight;
            return 0.25f;
        }

        // ============================================================
        //  在庫生成
        // ============================================================

        /// <summary>鑑定の眼鏡: この入店中の最低レア保証（null=無）。</summary>
        private ItemRarity? _apprMinRarity;
        /// <summary>いま棚に並んでいる品の id (2026-11-04)。 <see cref="PickItemByKind"/> はここにある品を引かない
        /// ── 同じ店に同じ品が 2 つ並んでいた (武器を 2 本買えた)。 棚を作り始める時と、 リロールの前に詰め直す。</summary>
        private readonly HashSet<string> _shelfIds = new HashSet<string>();

        /// <summary>軸指定リロール (2026-11-21・full-item-rework-spec §1-4・数値は仮): 通常のリロールの値段に足す額 (回数は通常と共有・旧 15G)。</summary>
        public const int AxisRerollSurcharge = 6;
        /// <summary>軸指定リロールで、 指定した軸の端子 (TerminalDef.axis) の出現の重みに掛ける倍率。 秘宝は <see cref="AxisPool.Narrow"/> の軸の重み (同じ値)。</summary>
        public const float AxisRerollWeightMul = AxisPool.FocusWeight;
        /// <summary>軸指定リロールで、 振り直す秘宝の枠のうち必ず指定の軸の品にする枠の数 (前から)。 残りの枠は指定の軸の重み ×<see cref="AxisRerollWeightMul"/>。</summary>
        public const int AxisRerollGuaranteed = 3;   // 2026-11-22: 2 → 3 (neon 最終仕上げ §1-2)
        /// <summary>〈目利きの紹介状〉: 軸指定リロールの追加から引く額 (仮)。</summary>
        public const int LetterAxisDiscount = 3;
        /// <summary>軸指定リロールで指定できる軸: 出現の枠 (RunState.lockedAxes) に入った軸と金。</summary>
        public static bool AxisRerollAllowed(RunState run, string axis)
            => axis == ItemSystem.PassiveSkills.AxisRules.Gold || (run?.lockedAxes != null && run.lockedAxes.Contains(axis));
        /// <summary>軸指定リロールの追加 (〈目利きの紹介状〉を持っていれば −<see cref="LetterAxisDiscount"/>・整備パネルの〈軸引きの手形〉で −N・下限 0)。</summary>
        public static int AxisRerollSurchargeFor(RunState run)
            => System.Math.Max(0, AxisRerollSurcharge
                - (run != null && run.OwnsPassive(GameLoop.ItemIds.MacroLetter) ? LetterAxisDiscount : 0)
                - MetaProgression.MetaBuffApplicator.GetAxisRerollDiscount());
        /// <summary>軸指定リロールの値段 (今のリロールの値段 + 追加)。 店が無ければ 0。</summary>
        public int AxisRerollPrice => Current != null ? Current.CurrentRerollPrice + AxisRerollSurchargeFor(GameLoop.GameManager.Instance?.Run) : 0;
        /// <summary>〈目利きの紹介状〉: この店の最初の通常リロールが無料か。</summary>
        private bool LetterFreeReroll(RunState run)
            => Current != null && !Current.freeRerollUsed && run != null && run.OwnsPassive(GameLoop.ItemIds.MacroLetter);
        /// <summary>通常のリロールで払う額 (画面の表示用・〈目利きの紹介状〉の無料なら 0)。 BOT の「まだ回せる」の判定は 0 にならない <see cref="ShopInventory.CurrentRerollPrice"/> を読む。</summary>
        public int RerollPrice => Current == null ? 0 : LetterFreeReroll(GameLoop.GameManager.Instance?.Run) ? 0 : Current.CurrentRerollPrice;

        /// <summary>〈黄金の通行手形〉: 店で使ったゴールド 50G ごとに、 次の店 1 軒の秘宝の棚が 1 段上のレア度で並ぶ。</summary>
        public const int PassSpendStep = 50;   // 2026-11-22 neon 収束: 80 → 50
        /// <summary>この店の秘宝の棚を 1 段上のレア度で並べるか (入店で決める・リロールにも効く)。</summary>
        private bool _passRarityUp;
        /// <summary>入店した時の run.coinsSpent (店を出る時に、 この店で使った額を出す)。</summary>
        private int _spentAtOpen;
        /// <summary>いま振り直している秘宝の枠の指定の軸 (null = 指定なし)。 TryReroll の間だけ立つ。</summary>
        private string _focusAxis;
        /// <summary>軸指定リロールで、 あと何枠を指定の軸で確定させるか (<see cref="AxisRerollGuaranteed"/> から減る)。</summary>
        private int _focusForceLeft;

        /// <summary>ショップマス入店時に在庫を生成。</summary>
        // ================================================================
        //  自由移動のマップの補給 (2026-09-28・docs/GAME.md §16-1)
        //  棚 (スロット) とは別の、 どの店にもある売り物。 値段は層で変えない。
        // ================================================================

        /// <summary>物資 1 包の量と値段 (1G ＝ 10 物資)。</summary>
        public const int ProvisionPackAmount = 1;   // 2026-11-09 デノミ: 1 包 = 物資 1
        /// <para>〈夜目の指輪〉〈補給商の割符〉で −50% (重ねない・GameLoop.NavItems.ProvisionPrice)。</para>
        public static int ProvisionPackPrice => GameLoop.NavItems.ProvisionPrice(GameLoop.GameManager.Instance?.Run);
        /// <summary>〈合札〉: 同じショップで 2 品目以降を買うときの値引き (G)。</summary>
        public const int TallyDiscount = 10;

        /// <summary>シールド枠の LEGENDARY が〈古い手紙〉に替わる確率 (%・2026-10-31 仮置き)。</summary>
        public const int OldLetterShopChancePct = 50;

        /// <summary>1 つの店で買える物資の包の数 (2026-11-04: 3 → 30 ＝ 物資 3000)。
        /// 3 包 (300) だと金がいくらあっても物資を買い足せず、 金が品だけに流れるか、 物資が尽きて探索が止まるかの両極端になった。</summary>
        public const int ProvisionPackStock = 60;   // 2026-11-09 デノミ: 旧 30 包 × 100 = 3000 → 物資 60
        /// <summary>この店に残っている物資の包。 <see cref="Generate"/> で戻る。</summary>
        public int ProvisionPacksLeft { get; private set; } = ProvisionPackStock;

        /// <summary>物資を 1 包買う (ゴールドのみ・兵站の物資払いは使わない ── 物資を物資で買うことになる)。</summary>
        public bool TryBuyProvisionPack(RunState run)
        {
            if (run == null || ProvisionPacksLeft <= 0 || run.coins < ProvisionPackPrice) return false;
            if (run.provision >= run.provisionCap) return false;
            run.coins -= ProvisionPackPrice;
            run.coinsSpent += ProvisionPackPrice;
            ProvisionPacksLeft--;
            GameLoop.ProvisionSystem.Supply(run, ProvisionPackAmount);
            MetaProgression.Achievements.AchievementService.NoteShopPurchase(run.coins, countsAsItem: false, surcharged: false);   // 物資の包は品ではない (清貧に数えない)
            if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] 物資を買った +{ProvisionPackAmount} (-{ProvisionPackPrice}G / 残り {ProvisionPacksLeft} 包)");
            return true;
        }

        /// <summary>囮罠 (8G) を買う。 在庫の上限は無い。</summary>
        public bool TryBuyLure(RunState run)
        {
            if (!TryBuyMapTool(run, GameLoop.ItemIds.Lure, MapSystem.FreeMove.FreeMapParams.ShopLurePrice)) return false;
            MetaProgression.Achievements.AchievementService.NoteShopPurchase(run.coins, countsAsItem: true, surcharged: false);   // 囮罠は品 (清貧に数える・作者 2026-11-17)
            return true;
        }

        /// <summary>値下げ交渉の後の割増 (<see cref="RobberySurcharge"/>) が今の店の品の値段に掛かっているか (実績〈面の皮〉が読む)。</summary>
        public static bool RobberySurchargeActive(RunState run)
            => run != null && run.shopRobberyDone && !RobberyBlocksShops && RobberySurcharge > 1f;

        private static bool TryBuyMapTool(RunState run, string id, int price)
        {
            if (run == null || run.coins < price) return false;
            if (!run.TryAddConsumable(id)) return false;   // 〈穴の空いた鞄〉の上限
            run.coins -= price;
            run.coinsSpent += price;
            if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] {id} を買った (-{price}G)");
            return true;
        }

        /// <summary><b>進行に合わせた価格の倍率 (2026-11-05)</b>: 1 + K × p²。 p = 層の番号 ÷ 最後の層の番号 (0 層 = 0・5 層 = 1)。
        ///
        /// <para>実測でクリアしたランは約 800G (獲得の約 43%) を使わずに終わっていた。 序盤は金が足りず (退店の 4 割は残金約 15G)、
        /// 後半は余る。 p² にして序盤はほぼ据え置き、 後半だけ高くする。 リロール価格は対象外 (在庫操作のコストなので一律・ShopInventory)。</para>
        ///
        /// <para>K = 0 で無効。 値は A/B で決める (AutoRunner の <c>shopProgressPriceK</c> で上書き)。</para></summary>
        public static float ProgressPriceK = 3f;   // 2026-11-05 決定 (掃引: K=3 で クリアしたランの残金 819G → 154G)

        /// <summary><b>レア度による価格の傾き (2026-11-05)</b>: 基準価格 × (1 + s × レア度の段)。 BRONZE = 段 0 は据え置き。
        ///
        /// <para>items.json の基準価格はレア度でほとんど差が無かった (パッシブ 7 / 8 / 9 / 10G・LEGENDARY が BRONZE の 1.4 倍)。
        /// 金が余る中で全部買えてしまい、 強い品を選ぶことに金の重みが無い。 上のレア度ほど高くして、 買い物を選択にする。</para>
        ///
        /// <para>s = 0 で無効。 出目パーツ (Tier で値付け) は対象外。 値は A/B で決める (AutoRunner の <c>shopRaritySlope</c>)。</para></summary>
        public static float RaritySlope = 0.25f;   // 2026-11-05 決定 (K=3 と組みで クリア 21.2%・LEGENDARY の取得 12→7 個/ラン)

        /// <summary><b>品物の価格の倍率 (2026-11-06・2 倍)</b>。 パッシブ・消耗品・武器・出目パーツの店頭価格に掛ける。
        /// リロール・物資の包・囮罠・強化素材は対象外 (在庫操作と資源)。 売値は items.json の基準価格のままで変えない
        /// (基準価格を倍にすると売値も倍になり、 売却で取り戻せてしまう)。
        /// クリアしたランはパッシブを約 71 個集めて最終ボスに挑んでおり (店のパッシブは全 82 種)、 買い物が選択になっていなかった。</summary>
        public static float ItemPriceScale = 2f;   // 2026-11-06 決定 (最低でも 2 倍)。 敵の攻撃を出目の幅へ戻した後の値。 それ以前の測定 (2.5 で 2 層までに品が 13 個) は敵が違う

        /// <summary>レア度による価格の倍率。</summary>
        public static float RarityPriceMul(ItemRarity rarity) => 1f + RaritySlope * (int)rarity;

        /// <summary>今の層の、 進行に合わせた価格の倍率。</summary>
        public static float ProgressPriceMultiplier(GameLoop.RunState run)
        {
            if (run == null || ProgressPriceK == 0f) return 1f;
            float p = UnityEngine.Mathf.Clamp01(run.currentFloor / (float)MapSystem.FreeMove.FloorPlan.LastOrdinal);
            return 1f + ProgressPriceK * p * p;
        }

        public ShopInventory Generate(int floor)
        {
            ProvisionPacksLeft = ProvisionPackStock;
            var inv = new ShopInventory();
            _shelfIds.Clear();

            // フロア価格倍率（FloorModifier.shopPriceMultiplier）× メタデバフ Lv1
            var floorMod = MapSystem.FloorModifierDatabase.Get(floor);
            float baseMul = floorMod != null ? floorMod.shopPriceMultiplier : 1f;
            inv.priceMultiplier = baseMul * MetaProgression.MetaDebuffApplicator.GetShopPriceMultiplier();
            inv.priceMultiplier *= ProgressPriceMultiplier(GameLoop.GameManager.Instance?.Run);   // 進行に合わせた値上げ
            // (商人の符牒 半額フックは 2026-07-18 アイテム削除に伴い除去)

            // 値下げ交渉(=強盗) の代償: 以降のショップは足元を見られる (2026-09-12)。
            //   旧仕様の「出禁」を置き換えたもの。 詳細は RunState.shopRobberyDone。
            var rsRob = GameLoop.GameManager.Instance?.Run;
            if (rsRob != null && rsRob.shopRobberyDone && RobberySurcharge != 1f)
            {
                inv.priceMultiplier *= RobberySurcharge;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] 値下げ交渉の代償: 全価格 ×{RobberySurcharge:F2}");
            }

            // 消費: 商人の鈴（次ショップ全価格半額）/ 鑑定の眼鏡（最低レア保証）
            var rsCons = GameLoop.GameManager.Instance?.Run;
            if (rsCons != null && rsCons.nextShopHalfPrice)
            {
                inv.priceMultiplier *= 0.5f;
                rsCons.nextShopHalfPrice = false;
                Debug.Log("[ShopManager] 商人の鈴: 全価格-50%");
            }
            // イベント「次の店の値段-N%」(2026-11-12): 次の 1 店だけ。 使ったら戻す
            if (rsCons != null && rsCons.nextShopPriceMul != 1f)
            {
                inv.priceMultiplier *= rsCons.nextShopPriceMul;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] イベントの値引き: 全価格 ×{rsCons.nextShopPriceMul:F2}");
                rsCons.nextShopPriceMul = 1f;
            }
            // 〈黄金の通行手形〉: 前の店までに使った PassSpendStep (50G) ごとに 1 軒、 秘宝の棚が 1 段上のレア度で並ぶ
            _passRarityUp = false;
            _spentAtOpen = rsCons != null ? rsCons.coinsSpent : 0;
            if (rsCons != null && rsCons.passShopsUp > 0)
            {
                rsCons.passShopsUp--;
                _passRarityUp = true;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] {GameLoop.ItemIds.MacroPass}: 秘宝の棚が 1 段上のレア度 (残り {rsCons.passShopsUp} 軒)");
            }
            _apprMinRarity = null;
            if (rsCons != null && rsCons.nextLootMinRarity >= 0)
            {
                _apprMinRarity = (ItemRarity)rsCons.nextLootMinRarity;
                rsCons.nextLootMinRarity = -1; // ショップで消費
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] 鑑定の眼鏡: 最低レア {_apprMinRarity}");
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

            // 消費 ×3 (2026-11-20・item-overhaul-spec §2-4): 枠 0 = 回復 (固定) / 枠 1 = シールドか攻撃強化から 1 系統を抽選 / 枠 2 = 戦術消耗品から 1 種。
            //   回復が並ばない店があると消費で耐久を賄う設計が成立しないので 1 枠目は固定。 Tier は T1〜4 から一様。 物資は棚ではなく「物資の包」で買う。
            //   旧 (2026-08-05〜11-20): 枠 1・2 にシールドと攻撃強化を両方並べた。
            {
                int tier0 = GameLoop.GameRng.Range(1, GameLoop.ItemIds.ConsumableMaxTier + 1, "shop.consTier");
                inv.slots.Add(BuildConsumableSlot(
                    GameLoop.ItemIds.ConsId(GameLoop.ItemIds.ConsumableFixedFamily, tier0), inv.priceMultiplier));
                inv.slots.Add(BuildConsumableSlot(RollGuardOrPowerId(), inv.priceMultiplier));
                string tactic = RollTacticId();
                if (tactic != null) inv.slots.Add(BuildConsumableSlot(tactic, inv.priceMultiplier));
            }

            // 武器 ×1 (2026-09-03: 2 → 1)
            //   **実測で 1 ラン 0.12 個しか買われていなかった** ── 16 訪問に 1 個。
            //   死に枠ではなく `Loadout.WouldUpgrade` が「装備更新にならない武器は買わない」と
            //   明示的に弾いているためで、 武器の主経路が素材強化 (T1→T4) だから。
            //   2 枠は過剰なので 1 枠へ。 直引き LEGENDARY の確率は §16-2 の MUST を守り据え置き。
            inv.slots.Add(BuildSlot(ShopSlotKind.Weapon, inv.priceMultiplier));

            // 賽 ×2 (ADR-0015・2026-11-18: 出目パーツの枠の置き換え。 陳列数は据え置き)
            //   最初の賽と、 いま装備している賽は並べない (EquippedDie.Offerable)。 同じ店に同じ賽は 2 つ並べない (棚の重複排除)。
            //   買うと装備が替わる (持つ賽は 1 個)。
            if (!SuppressFacePartOffers)
            {
                for (int i = 0; i < 2; i++)
                {
                    var slot = BuildSlot(ShopSlotKind.Dice, inv.priceMultiplier);
                    if (slot.itemId != null) inv.slots.Add(slot);
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

            // 2026-06-22: メタバフ「特売品」 を 1/2/3 個ランダム枠に付与 (Passive/Consumable/Weapon/Dice のみ対象)
            ApplySaleDiscounts(inv);

            Current = inv;
            MetaProgression.Achievements.AchievementService.BeginShop();
            OnShopOpened?.Invoke(inv);
            if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] 入店: フロア{floor}, スロット{inv.slots.Count}");

            return inv;
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
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] 特売 slot={candidates[i]} {s.itemId} -{discount}% ({s.originalPrice}G→{s.price}G)");
            }
        }

        /// <summary>[計装] TryReroll が false を返した理由の内訳。</summary>
        public static long RerollFailNoShop, RerollFailNoGold, TallyDiscounts, RerollFailCapped;
        public static void ResetRerollFailStats()
        { RerollFailNoShop = RerollFailNoGold = TallyDiscounts = RerollFailCapped = 0; }

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
                price = Mathf.CeilToInt(basePrice * ItemPriceScale * RarityPriceMul(item.rarity) * priceMultiplier) + ItemPriceAdd;
            }
            if (id != null) _shelfIds.Add(id);
            return new ShopSlot { kind = kind, itemId = id, price = price, sold = false };
        }

        /// <summary>全アイテムの店頭価格に一律で足す額 (2026-09-17)。 <b>倍率ではなく加算</b> ──
        /// 安い品ほど相対的に効くので、 「安物を数で買う」経路を狙って締められる。
        /// 素の帯は BRONZE 7 / SILVER 8 / GOLD 9 / LEGENDARY 10 G。</summary>
        public static int ItemPriceAdd = 0;

        /// <summary>今の層でこの品を店で買う時の価格 (棚の抽選の幅の中央・割引なし)。 品の価値を「買う値段込み」で測る ITT が読む。</summary>
        public static int ExpectedShopPrice(string id, GameLoop.RunState run)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            float prog = ProgressPriceMultiplier(run);
            var item = ItemDatabase.Instance?.GetItem(id);
            if (item == null) return 0;
            float basePrice = item.buyPrice != null ? (item.buyPrice.min + item.buyPrice.max) / 2f : 10f;
            return Mathf.CeilToInt(basePrice * ItemPriceScale * RarityPriceMul(item.rarity) * prog) + ItemPriceAdd;
        }

        /// <summary>消費の枠 1: シールドか攻撃強化 (<see cref="GameLoop.ItemIds.ConsumableRandomFamilies"/>) から 1 系統 ("shop.consFamily")、 Tier は一様 ("shop.consTier")。
        /// 古い手紙 (2026-10-31): シールドで LEGENDARY を引いたとき、 OldLetterShopChance で〈惜別の護符〉と入れ替わる
        /// (系統を持たない単品なので、 抽選に入る道はここだけ・乱数は専用キー "shop.oldLetter")。</summary>
        private static string RollGuardOrPowerId()
        {
            var fams = GameLoop.ItemIds.ConsumableRandomFamilies;
            string fam = fams[GameLoop.GameRng.Range(0, fams.Length, "shop.consFamily")];
            int tier = GameLoop.GameRng.Range(1, GameLoop.ItemIds.ConsumableMaxTier + 1, "shop.consTier");
            string consId = GameLoop.ItemIds.ConsId(fam, tier);
            if (fam == GameLoop.ItemIds.ConsShieldFamily && tier == GameLoop.ItemIds.ConsumableMaxTier
                && GameLoop.GameRng.Range(0, 100, "shop.oldLetter") < OldLetterShopChancePct)
                consId = GameLoop.ItemIds.OldLetter;
            return consId;
        }

        /// <summary>消費の枠 2: 戦術消耗品から 1 種 (2026-11-20・乱数キー "shop.consTactic")。 1 品の重み = レア度の重み (<see cref="TierOfferWeight"/>) ÷ そのレア度の品の数
        /// (レア度の出方は秘宝の枠と同じ 45 / 32 / 18 / 5%)。 戦術消耗品が無ければ null (枠を並べない)。</summary>
        private static string RollTacticId()
        {
            var pool = GameLoop.Consumables.TacticPool();
            if (pool.Count == 0) return null;
            var n = new Dictionary<ItemRarity, int>();
            foreach (var it in pool) { n.TryGetValue(it.rarity, out int c); n[it.rarity] = c + 1; }
            float total = 0f; var w = new float[pool.Count];
            for (int i = 0; i < pool.Count; i++) { w[i] = TierOfferWeight(pool[i].rarity) / n[pool[i].rarity]; total += w[i]; }
            float r = GameLoop.GameRng.Value("shop.consTactic") * total;
            for (int i = 0; i < pool.Count; i++) { r -= w[i]; if (r <= 0f) return pool[i].id; }
            return pool[pool.Count - 1].id;
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
            int price = Mathf.CeilToInt(basePrice * ItemPriceScale * (item != null ? RarityPriceMul(item.rarity) : 1f) * priceMultiplier) + ItemPriceAdd;
            // 〈行商人の見本箱〉(2026-11-20): 戦術消耗品の値段 −50% (切り上げ・下限 1G)
            if (item != null && item.consFamily == GameLoop.ItemIds.ConsTacticFamily
                && ItemSystem.PassiveSkills.AxisRules.Owns(GameLoop.GameManager.Instance?.Run, GameLoop.ItemIds.LinkSampleBox))
                price = Mathf.Max(1, Mathf.CeilToInt(price * ItemSystem.PassiveSkills.Effects.SampleBox.PriceMul));
            return new ShopSlot
            {
                kind = ShopSlotKind.Consumable,
                itemId = item != null ? id : null,
                price = price,
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
                ShopSlotKind.Dice        => ItemCategory.Dice,
                _ => (ItemCategory?)null,
            };
            if (category == null) return null;

            var pool = db.GetItemsByCategory(category.Value);
            if (pool == null || pool.Count == 0) return null;

            // イベント限定アイテムを除外（ちいさな灯火・決意 等）
            pool = pool.FindAll(EventOnlyItemFilter.IsAllowed);
            if (pool.Count == 0) return null;

            // 賽 (ADR-0015): 最初の賽と、 いま装備している賽は並べない。
            if (kind == ShopSlotKind.Dice)
            {
                var runD = GameLoop.GameManager.Instance?.Run;
                pool = pool.FindAll(it => GameLoop.EquippedDie.Offerable(it, runD));
                if (pool.Count == 0) return null;
            }

            // 棚に並んでいる品は引かない (重複陳列の防止)。 尽きた時だけ重複を許す。
            if (_shelfIds.Count > 0)
            {
                var notShelved = pool.FindAll(it => !_shelfIds.Contains(it.internalName));
                if (notShelved.Count > 0) pool = notShelved;
            }

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

                    // 佯狂者の鈴(ADR-0002): 払底中 (物資 0)、他の[佯狂者]アイテムがショップに出やすい (2026-11-09: 段の撤去で「絶望帯」→「払底」)。
                    // 2026-06-23c: 60% 短絡で全レア無差別ピックではなく、 60% でプール自体を佯狂者に絞る (Tier 重みは下流の RollTier に委譲)。
                    // これにより [佯狂者] セット組み立てを後押ししつつ LEG 出現率は 0.05 のまま維持される。
                    if (owned.Contains(GameLoop.YokyoSet.Bell)
                        && GameLoop.ProvisionSystem.IsExhausted(run))
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

            // 秘宝: 出現の枠と 2 段階の軸均等の抽選 (軸か枠外に絞ってからレア度で引く・full-item-rework-spec §1-2)。 軸指定リロールは指定の軸を重み ×4・前の AxisRerollGuaranteed 枠は確定
            if (kind == ShopSlotKind.Passive)
            {
                pool = AxisPool.Narrow(GameLoop.GameManager.Instance?.Run, pool, "shop.axis", _focusAxis, _focusForceLeft > 0);
                if (_focusForceLeft > 0) _focusForceLeft--;
                if (pool.Count == 0) return null;
            }

            // 武器のみ: WeaponShopFilter のチェック (LEGENDARY を含めて出現可、 出現率は別重みで制御)
            if (kind == ShopSlotKind.Weapon)
            {
                pool = pool.FindAll(WeaponShopFilter.IsShopAllowed);
                if (pool.Count == 0) return null;
            }

            // Tier重みで抽選 (武器は専用重みで T4 を超レア化)
            var weights = kind == ShopSlotKind.Weapon ? weaponTierWeights
                        : kind == ShopSlotKind.Passive ? PassiveTierWeights(GameLoop.GameManager.Instance?.Run?.Depth ?? 0) : tierWeights;
            // 呪物の賽 (ADR-0016): 秘宝の枠は LEGENDARY の重み ×3 (他のレア度の重みはそのまま)
            float legMul = kind == ShopSlotKind.Passive ? CombatSystem.DiePassives.LegendaryOfferMul(GameLoop.GameManager.Instance?.Run) : 1f;
            if (legMul != 1f)
            {
                weights = ((ItemRarity, float)[])weights.Clone();
                for (int i = 0; i < weights.Length; i++)
                    if (weights[i].rarity == ItemRarity.LEGENDARY) weights[i].weight *= legMul;
            }
            ItemRarity targetRarity = RollTier(pool, weights);
            // 〈黄金の通行手形〉: 引いたレア度の 1 段上 (LEGENDARY は据え置き・その段の品が棚に出せなければ引いた段のまま)。 乱数は引き足さない
            if (kind == ShopSlotKind.Passive && _passRarityUp && targetRarity < ItemRarity.LEGENDARY)
            {
                var up = targetRarity + 1;
                if (pool.Exists(it => it.rarity == up)) targetRarity = up;
            }
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
                return PickOne(pool);
            }
            var picked = PickOne(byTier);
            // [計装] 剣の舞が「棚に並んだ」回数。 取得 (SwordDanceSet.Acquired) と分けて数える。
            if (picked != null && GameLoop.SwordDanceSet.IsDance(picked.internalName)) DanceOffered++;
            if (picked != null && kind == ShopSlotKind.Passive)
            {
                int ri = (int)picked.rarity;
                if (ri >= 0 && ri < OfferedByRarity.Length) OfferedByRarity[ri]++;
            }
            return picked;
        }

        /// <summary>同じレア度の候補から 1 つ (一様)。 種火の出現の重み・軸指定リロールの品の重みは 2026-11-21 に <see cref="AxisPool.Narrow"/> の軸の段へ移した。</summary>
        private static CompleteItemData PickOne(List<CompleteItemData> list)
            => list == null || list.Count == 0 ? null : list[GameLoop.GameRng.Range(0, list.Count, "shop.pickAny")];

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
                // 兵站 r10: 不足分は物資で払える。 未解禁ならゴールドのみで判定する。
                if (!GameLoop.ProvisionPayment.TryPay(run, price, "強化素材"))
                { Log("ゴールド不足"); return false; }
                MetaProgression.Achievements.AchievementService.NoteShopPurchase(run.coins, countsAsItem: false, surcharged: RobberySurchargeActive(run));   // 強化素材は品ではない (清貧に数えない)
                run.weaponMaterials++;
                Current.materialPurchaseCount++;
                slot.price = Current.CurrentMaterialPrice; // 表示価格を更新
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] 強化素材購入: -{price}G (次回 {Current.CurrentMaterialPrice}G)");
                OnShopUpdated?.Invoke();
                return true;
            }

            if (slot.sold) { Log("売り切れ"); return false; }
            if (string.IsNullOrEmpty(slot.itemId)) { Log("空スロット"); return false; }
            // 〈合札〉: 同じショップで 2 品目以降を買うとき 10G 安い (下限 1G)。
            int pay = slot.price;
            bool tally = Current.purchaseCount >= 1 && run.OwnsPassive(ItemIds.TrialTossToken);
            if (tally) pay = Mathf.Max(Mathf.Min(pay, 1), pay - TallyDiscount);
            // 武器は 1 本だけ (2026-11-11): 人が別の武器を持って武器を買う時は、 今の武器を下取りに出して差額で乗り換える
            int tradeIn = WeaponTradeIn(slotIndex, run);
            if (tradeIn > 0) pay = Mathf.Max(0, pay - tradeIn);
            // 兵站 r10 を含めた支払い可能額で判定する。 未解禁なら run.coins と同じ。
            if (GameLoop.ProvisionPayment.Affordable(run) < pay)
            {
                // 行動台帳〈見送り〉: **買おうとして届かなかった**ときだけ積む。
                //   棚を眺めただけの回は通らない ── ここまで来たのは購入の意思があった証拠。
                GameLoop.RunChronicle.Buy(run, GameLoop.RunChronicle.BuyForgo, slot.itemId, pay);
                Log("ゴールド不足"); return false;
            }

            if (!GameLoop.ProvisionPayment.TryPay(run, pay, slot.itemId))
            { Log("支払いに失敗"); return false; }
            // 実績: 清貧の「買う」は品だけ (秘宝・武器・消耗品・囮罠)。 賽は数えない (作者 2026-11-17「アイテムのみ」は出目パーツを除く意味・賽も同じ扱いで据え置き)
            MetaProgression.Achievements.AchievementService.NoteShopPurchase(run.coins, countsAsItem: slot.kind != ShopSlotKind.Dice, surcharged: RobberySurchargeActive(run));
            Current.purchaseCount++;
            if (tally && pay < slot.price) { TallyDiscounts++; Log($"〈合札〉: 2 品目以降 {TallyDiscount}G 引き"); }
            if (tradeIn > 0) Log($"下取り: {run.equippedWeaponId} を {tradeIn}G で引き取り、 差額 {pay}G で乗り換え");

            switch (slot.kind)
            {
                case ShopSlotKind.Passive:
                case ShopSlotKind.Weapon:
                    // 武器は 1 本だけ (2026-11-11): 店で買う武器は入れ替えを承知して買ったもの (画面は「買う」の 2 度押しで確かめる)
                    GameLoop.Loadout.Gain(run, slot.itemId, confirmed: slot.kind == ShopSlotKind.Weapon && GameLoop.Loadout.HumanPlaying);
                    break;
                case ShopSlotKind.Dice:
                    // 持つ賽は 1 個 (ADR-0015)。 買うと装備が替わり、 前の賽は手元に残らない。
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] 賽を替えた: {GameLoop.EquippedDie.Label(GameLoop.EquippedDie.IdOf(run))} → {GameLoop.EquippedDie.Label(slot.itemId)}");
                    GameLoop.EquippedDie.Equip(run, slot.itemId);
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
                    case ShopSlotKind.Dice:       code = GameLoop.RunChronicle.BuyDice;       break;
                    case ShopSlotKind.Consumable: code = GameLoop.RunChronicle.BuyConsumable; break;
                    case ShopSlotKind.Passive:    code = GameLoop.RunChronicle.BuyPassive;    break;
                }
                if (code != null)
                    GameLoop.RunChronicle.Buy(run, code, slot.itemId, pay);
            }

            // ショップ購入記録 (BOT 売却判定で「ショップ由来」 のみ売却可)
            if (slot.kind == ShopSlotKind.Passive || slot.kind == ShopSlotKind.Consumable
                || slot.kind == ShopSlotKind.Weapon)
            {
                run.shopPurchasedCounts.TryGetValue(slot.itemId, out int prev);
                run.shopPurchasedCounts[slot.itemId] = prev + 1;
            }
            slot.sold = true;

            var data = ItemDatabase.Instance?.GetItem(slot.itemId);
            string label = data != null ? data.displayName : slot.itemId;
            if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] 購入: {label} -{slot.price}G");
            OnShopUpdated?.Invoke();
            return true;
        }

        // ============================================================
        //  リロール
        // ============================================================

        /// <summary>
        /// 強化素材スロットを除く未売却枠を全部振り直す。
        /// コストは ShopInventory.CurrentRerollPrice。売却済み枠はそのまま（戻らない）。
        /// <para><paramref name="focusAxis"/> (軸指定リロール・2026-11-21・full-item-rework-spec §1-4): 出現の枠の軸か金 (<see cref="AxisRerollAllowed"/>)。
        /// 値段は <see cref="AxisRerollPrice"/> (回数は通常と共有)。 振り直す秘宝の枠の前の <see cref="AxisRerollGuaranteed"/> 枠はその軸の品で確定、 残りはその軸の重み ×<see cref="AxisRerollWeightMul"/>
        /// (<see cref="AxisPool.Narrow"/>)。 端子の棚の振り直しは呼び手 (GameManager.ShopReroll)。 null = 通常のリロール。</para>
        /// </summary>
        public bool TryReroll(RunState run, string focusAxis = null)
        {
            if (Current == null || run == null) { RerollFailNoShop++; return false; }
            // A/B: 1 店あたりの上限。 既定 -1 は無制限なのでここは素通りする。
            if (RerollHardCap >= 0 && Current.rerollCount >= RerollHardCap)
            { RerollFailCapped++; return false; }
            if (focusAxis != null && !AxisRerollAllowed(run, focusAxis)) focusAxis = null;   // 出現の枠に無い軸は指定なし
            bool letterFree = focusAxis == null && LetterFreeReroll(run);   // 〈目利きの紹介状〉: 店の最初の通常リロールは無料
            int price = focusAxis != null ? AxisRerollPrice : letterFree ? 0 : Current.CurrentRerollPrice;

            if (run.coins < price) { RerollFailNoGold++; Log($"リロール: ゴールド不足 ({price}G 必要)"); return false; }

            run.coins -= price;
            run.coinsSpent += price;
            MetaProgression.Achievements.AchievementService.NoteShopReroll();
            GameLoop.RunChronicle.Reroll(run, price);
            if (letterFree) { Current.freeRerollUsed = true; Log($"〈{GameLoop.ItemIds.MacroLetter}〉: 最初のリロールは無料"); }
            // 〈相場師の算盤〉: リロール (軸指定も) 1 回ごとに 1 回分積もる (最大 10・戦闘開始時のブロック +2・最初の 1 撃 +1)
            if (run.abacusStacks < ItemSystem.PassiveSkills.Effects.MacroAbacus.MaxStacks && run.OwnsPassive(GameLoop.ItemIds.MacroAbacus))
            {
                run.abacusStacks++;
                Log($"〈{GameLoop.ItemIds.MacroAbacus}〉: {run.abacusStacks} 回分");
            }
            Current.rerollCount++;

            // 振り直さない枠 (売却済み) の品は棚に残る。 振り直す枠の品は棚から外す (同じ品が引き直されてよい)。
            _shelfIds.Clear();
            foreach (var s in Current.slots)
                if (s != null && s.sold && !string.IsNullOrEmpty(s.itemId)) _shelfIds.Add(s.itemId);

            int rerolled = 0;
            _focusAxis = focusAxis;
            _focusForceLeft = focusAxis != null ? AxisRerollGuaranteed : 0;
            try {
            for (int i = 0; i < Current.slots.Count; i++)
            {
                var s = Current.slots[i];
                if (s == null) continue;
                if (s.kind == ShopSlotKind.WeaponMaterial) continue; // 価格カーブ独立
                if (s.sold) continue;
                ShopSlot fresh;
                switch (s.kind)
                {
                    // 消費は 3 枠の形 (回復 / シールドか攻撃強化 / 戦術消耗品・2026-11-20) を保つ。
                    //   回復は Tier だけ・枠 1 は系統から引き直す・枠 2 は戦術消耗品から引き直す (Generate と同じ関数)。
                    //   以前は全消費アイテムから引き直しており、 回復が消えたり同じ系統が 2 枠並んだりした。
                    case ShopSlotKind.Consumable:
                    {
                        string fam = GameLoop.ItemIds.ConsFamilyOf(s.itemId) ?? GameLoop.ItemIds.ConsShieldFamily;   // 惜別の護符はシールド枠の差し替え
                        string consId;
                        if (fam == GameLoop.ItemIds.ConsTacticFamily) consId = RollTacticId();
                        else if (fam == GameLoop.ItemIds.ConsumableFixedFamily)
                            consId = GameLoop.ItemIds.ConsId(fam, GameLoop.GameRng.Range(1, GameLoop.ItemIds.ConsumableMaxTier + 1, "shop.consTier"));
                        else consId = RollGuardOrPowerId();
                        fresh = consId != null ? BuildConsumableSlot(consId, Current.priceMultiplier) : s;
                        break;
                    }
                    default:
                        fresh = BuildSlot(s.kind, Current.priceMultiplier);
                        break;
                }
                Current.slots[i] = fresh;
                rerolled++;
            }
            } finally { _focusAxis = null; _focusForceLeft = 0; }

            // **リロールは特売を消していた** (2026-09-17 発見)。 ApplySaleDiscounts は棚生成時に
            //   1 回だけ呼ばれ、 TryReroll は BuildSlot で作り直すだけなので割引が失われる。
            //   BOT は 1 ラン 5.56 回リロールするので、 商才を積むほど大きくなる割引を
            //   そのたび捨てていた (商才 r9 が Bal 比 −7.58pt だった一因の候補)。
            //   **塩を変える** ── 同じ塩だと毎回同じ枠に同じ割引が出る。
            if (RerollKeepsSale) ApplySaleDiscounts(Current, Current.rerollCount * 64);

            if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] リロール#{Current.rerollCount}{(focusAxis != null ? " (軸: " + focusAxis + ")" : "")}: -{price}G / {rerolled}枠更新 / 次回{Current.CurrentRerollPrice}G");
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
        /// <summary>強盗の物資コスト。 -1 = 既定 (ProvisionSystem.EvilChoiceCost)。 切り分け用。</summary>
        public static int RobberyProvisionCost = -1;

        /// <summary><b>旧仕様の「出禁」へ戻す退避経路 (既定 false)。</b> true にすると強盗後の
        /// ショップマスを素通りする ── 2026-09-12 以前の挙動。
        ///
        /// <para><b>比較スイープ専用。</b> 今回は BOT の方策修正 (買う前に撃つ / ランで最後の店に限定) と
        /// 罰の設計変更 (出禁 → 価格割増) を同時に入れたので、 <b>どちらが効いたのか</b>を
        /// 分けるにはこの経路が要る。 true + 新方策 = 方策修正だけの効果。
        /// <see cref="RobberySurcharge"/> と併用しないこと (true のときは割増を 1.0 にする)。</para></summary>
        public static bool RobberyBlocksShops;

        /// <summary>[計装] 強盗の試行回数。 勝敗は <c>RunStats.RobberyWins/RobberyLosses</c>。
        /// static なのでバッチ開始時に 0 へ戻すこと。</summary>
        public static long RobberyAttempts;
        /// <summary>[計装] 強盗を撃った層の分布 (1..7 を index 0..6)。</summary>
        public static readonly long[] RobberyByFloor = new long[7];

        /// <summary>
        /// 値下げ交渉を試みる(実態は強盗)。
        /// ・現在ショップの未売却アイテムIDをスナップショットして run.robberyPendingItems に格納
        /// ・物資コスト適用、shopRobberyDone = true (1 ラン 1 回。 以降のショップ価格は RobberySurcharge で割増)
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

            GameLoop.ProvisionSystem.ApplyEvilChoice(run,
                RobberyProvisionCost >= 0 ? RobberyProvisionCost : GameLoop.ProvisionSystem.EvilChoiceCost);
            run.shopRobberyDone = true;
            run.shopRobberyInProgress = true;
            RobberyAttempts++;
            // [計装] **何層で撃っているか**。 これが無いと「弱い段階で誤爆している」を
            //   口で言うだけになる ── 実際 2026-09-13 に、 階層ゲートが 1 つも無い条件を
            //   「6 層で撃つ」と説明していた。 ログはバッチ中に無効化されるので計数で持つ。
            {
                int f = Mathf.Clamp(run.Depth, 1, RobberyByFloor.Length) - 1;
                RobberyByFloor[f]++;
            }

            if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] 値下げ交渉(強盗): 物資-{GameLoop.ProvisionSystem.EvilChoiceCost}, "
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
                run.weaponMaterials--;
                int gain = GameLoop.GoldIncome.GainExempt(run, WeaponMaterialSellPrice, "強化素材売却");
                MetaProgression.Achievements.AchievementService.NoteShopSale();
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] 強化素材売却: +{gain}G");
                OnShopUpdated?.Invoke();
                return true;
            }

            var list = source == SellSource.Passive ? run.ownedPassiveItems : run.ownedConsumables;
            if (list == null || listIndex < 0 || listIndex >= list.Count) return false;

            string id = list[listIndex];
            // 手放せない品 (確信の 3 品・預かり物・GameLoop.ItemIds.Undroppable・2026-11-13) は売れない。 売る画面は並べず、
            //   キー操作 (GameManager.ShopSell) と BOT の売却 (BotSelling) もここで断られる (BOT は次の品へ進む)。 乱数を引く前に断る
            if (source == SellSource.Passive && !GameLoop.ItemIds.CanSell(id)) { Log(GameLoop.ItemIds.RefuseSellText(id)); return false; }
            // 売値 0G の品 (items.json の noSellValue) も売れる (0G が入る)。 BOT は売らない (AutoRunner の SellOneScore0 が SellsForNothing で飛ばす)
            int price = ResolveSellPrice(id);
            // 除去は PassiveAddHelper.RemoveAt に寄せる
            if (source == SellSource.Passive)
                ItemSystem.Helpers.PassiveAddHelper.RemoveAt(run, listIndex);
            else
                list.RemoveAt(listIndex);
            // ショップ購入記録があれば在庫を 1 減らす (BOT 用、 売却可能在庫トラッキング)
            if (run.shopPurchasedCounts.TryGetValue(id, out int shopStock) && shopStock > 0)
                run.shopPurchasedCounts[id] = shopStock - 1;
            int gainPrice = GameLoop.GoldIncome.GainExempt(run, price, "売却");
            MetaProgression.Achievements.AchievementService.NoteShopSale();
            if (Debug.unityLogger.logEnabled) Debug.Log($"[ShopManager] 売却: {id} +{gainPrice}G");
            OnShopUpdated?.Invoke();
            return true;
        }

        /// <summary>武器の下取り額 (2026-11-11)。 人が別の武器を装備したまま武器の枠を買う時だけ 0 より大きい。
        /// 額はその武器のレア度の売値 (乱数を引かない ── 画面に出す値と払う値を揃えるため・個別の売値は下限)。</summary>
        public int WeaponTradeIn(int slotIndex, RunState run)
        {
            if (Current == null || run == null || !GameLoop.Loadout.HumanPlaying) return 0;
            if (slotIndex < 0 || slotIndex >= Current.slots.Count) return 0;
            var slot = Current.slots[slotIndex];
            if (slot.kind != ShopSlotKind.Weapon || string.IsNullOrEmpty(run.equippedWeaponId) || run.equippedWeaponId == slot.itemId) return 0;
            if (ItemDatabase.Instance?.GetItem(run.equippedWeaponId) == null) return 0;
            SellRange(run.equippedWeaponId, out int min, out _);
            return min;
        }

        /// <summary>強化素材 1 つの売値。</summary>
        public const int WeaponMaterialSellPrice = 15;

        /// <summary>品の売値の幅 (2026-11-13 に 1 つへまとめた)。 売却 (<see cref="TrySell"/>)・下取り・売る画面・BOT・デバッグ表示は全部ここを通る。
        /// 規則は <see cref="SellPriceRule"/>: items.json の <c>noSellValue</c> なら 0G、 それ以外は <c>basePrice</c> の ×0.50〜×0.75 (下限 1G)。
        /// <para>2026-11-13 まで: 「sellPrice が無ければレア度で BRONZE 3G〜MYTHIC 12G」の分岐があったが、 sellPrice は
        /// ItemDataV2 の構築子が必ず作るので一度も通らない死んだ経路だった (basePrice の無い品は幅 0〜0 → 売却の Max(1) で 1G)。 消した。</para></summary>
        public static void SellRange(string id, out int min, out int max)
        {
            var data = string.IsNullOrEmpty(id) ? null : ItemDatabase.Instance?.GetItem(id);
            if (data == null) { min = max = SellPriceRule.UnknownItem; return; }
            SellPriceRule.Range(data.noSellValue, data.sellPrice != null ? data.sellPrice.min : 0, data.sellPrice != null ? data.sellPrice.max : 0, out min, out max);
        }

        /// <summary>売っても 0G の品か (BOT はこの品を売らない・人は売れる)。 売れるか (<see cref="GameLoop.ItemIds.CanSell"/>) とは別の条件。</summary>
        public static bool SellsForNothing(string id)
        {
            SellRange(id, out _, out int max);
            return max <= 0;
        }

        /// <summary>売る画面に出す売値 (「4G」「4〜6G」)。</summary>
        public static string SellPriceLabel(string id)
        {
            SellRange(id, out int min, out int max);
            return min == max ? min + "G" : min + "〜" + max + "G";
        }

        /// <summary>売却で実際に入る額。 <see cref="SellRange"/> の幅から乱数で 1 つ引く (幅が 1 点でも引く ── 乱数の消費を品で変えない)。</summary>
        private int ResolveSellPrice(string id)
        {
            SellRange(id, out int min, out int max);
            if (ItemDatabase.Instance?.GetItem(id) == null) return min;
            return GameLoop.GameRng.Range(min, max + 1, "shop.sellPrice");
        }

        // ============================================================
        //  退店
        // ============================================================

        public void Close()
        {
            // 〈黄金の通行手形〉: この店で使った額を足し、 PassSpendStep (50G) ごとに次の店 1 軒 (持っている時に出た店だけ)
            var run = GameLoop.GameManager.Instance?.Run;
            // 〈溶金の領主剣〉: この店で使った額を積む (100G につき基礎攻撃 +2・items.json の stats・per = swordShopSpent)
            if (Current != null && run != null && run.OwnsPassive(GameLoop.ItemIds.GoldenKingSword))
                run.swordShopSpent += System.Math.Max(0, run.coinsSpent - _spentAtOpen);
            if (Current != null && run != null && run.OwnsPassive(GameLoop.ItemIds.MacroPass))
            {
                run.passShopSpent += System.Math.Max(0, run.coinsSpent - _spentAtOpen);
                int add = run.passShopSpent / PassSpendStep;
                run.passShopSpent -= add * PassSpendStep;
                run.passShopsUp += add;
                if (add > 0) Log($"〈{GameLoop.ItemIds.MacroPass}〉: 次の店から {run.passShopsUp} 軒、 秘宝の棚が 1 段上");
            }
            Current = null;
            OnShopClosed?.Invoke();
        }

        private void Log(string msg) => Debug.Log($"[ShopManager] {msg}");
    }
}

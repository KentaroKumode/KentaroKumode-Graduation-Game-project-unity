namespace GameLoop
{
    /// <summary>
    /// 2026-06-28 新設: コード中に散在していた ID 文字列リテラルの定数化。
    ///
    /// 目的: ID の typo をコンパイルエラーで捕捉する (文字列直書きは実行時まで気づけない)。
    /// 規約: 「複数ファイルから参照される ID」 「日本語 ID」 「ロジック分岐に使う ID/prefix」 は
    ///       必ずここ (または BossIds / ClassStarter) の定数を経由する。
    ///       items.json 内の表示・価格データの正本性は変わらない (ここはコード参照用のミラー)。
    /// </summary>
    public static class ItemIds
    {
        // === 名前付きパッシブ (日本語 ID ── items.json の id と一致必須) ===
        // MerchantSeal (商人の符牒) / GourmetKnife (食通の懐刀) は 2026-07-18 削除 (アイテム削除に伴う)
        /// <summary>黄金卿の剣: AutoRunner の装備判断が参照。</summary>
        public const string GoldenKingSword = "溶かし金の領主剣";
        /// <summary>天工開物: GameManager の強化フックが参照。</summary>
        public const string HeavenlyCraft = "天工開物";
        /// <summary>シュヴァリエのレイピア: コントラタック切替 (Consumables.TryUseRapier)。</summary>
        public const string ChevalierRapier = "シュヴァリエのレイピア";

        // === 店連動パッシブ (2026-09-15) ===
        //   ShopManager が所持判定に使う ＝ 跨ファイル参照なのでここを経由する。
        //   下の帯 (BRONZE/SILVER) に店の機能へ触る品が 1 つも無かったのを埋めるもの。
        /// <summary>試し振りの符牒: 各ショップの初回リロールが無料 (ShopManager.TryReroll)。</summary>
        public const string TrialTossToken = "合札";
        /// <summary>釣り銭の受け皿: ショップで買うたび 希望+1 (ShopManager.TryBuy)。</summary>
        public const string ChangeTray = "銭皿";
        /// <summary>精鋭首の請取証: エリート勝利だけ ゴールド+2 (GameManager の戦闘報酬)。</summary>
        public const string EliteBountyReceipt = "首級控え";

        // === 初期装備 ===
        public const string DiceWood = "dice_wood";

        // === ユニーク消耗品 (uniq_*) ===
        public const string PhilStone    = "uniq_phil_stone";
        // 2026-07-21: 消費アイテム全面再編で ForgeElixir/Mirror/Gambler/Ambush/Appraise/MerchantBell/OniOil/HastePowder
        //             を削除。 消費アイテムは 5 カテゴリ (cons_heal/atk/def/dmg/food) + 賢者の石 に集約。
        // 職業スターター 4 種は ClassStarter (RunState.cs) が正本。

        // === 消費アイテム 3 系統 × Tier1〜4 (2026-08-04 再編) ===
        //   旧 5 系統 (heal/atk/def/dmg/food) から 3 系統へ。 削除したのは:
        //     ・cons_food_* (希望回復) …… 希望の供給はイベント/報酬側へ寄せた
        //     ・cons_atk_*  (攻撃+N)   …… 与ダメ% の cons_dmg_* へ統合
        //   **Tier 軸は「効果量」のみ。持続を Tier 軸に混ぜない。**
        //   旧設計は「効果量↓ × 持続↑」で Tier を上げていたため、 実効量 (%·ターン) が
        //   戦闘の長さで逆転していた ── 通常戦 1T の時代は T1 > T2 > T3 と完全に逆順、
        //   通常戦 3.6T でも T2 > T3。 高 Tier ほど弱いという状態が長く放置されていた。
        //   全 Tier「その戦闘中」で統一し、 数字が単調増加するようにしてある。
        //   **接頭辞ではなく系統名** (2026-09-22)。 id は表示名になったので、 綴りで判定できない。
        //   items.json の consFamily がこの値を持ち、 判定は ItemIds.ConsFamilyOf() で行う。
        public const string ConsHealFamily   = "heal";   // 回復:     最大HP の 25/40/60/100%
        public const string ConsShieldFamily = "def";    // シールド: 15/30/50/80 (戦闘中に使用可)
        public const string ConsPowerFamily  = "dmg";    // 攻撃強化: 与ダメ +15/30/50/75% (戦闘中)
        public const string ConsHopeFamily   = "hope";   // 希望回復: +5/10/15/20 (2026-08-05 追加)

        /// <summary>**回復は毎回並ぶ。** 消費枠は 3 で、 1 枠目を回復で固定する。
        /// 回復が並ばない店があると消費で耐久を賄う設計自体が成立しないため (2026-08-04)。</summary>
        public static readonly string ConsumableFixedFamily = ConsHealFamily;

        /// <summary>残り 2 枠をここから重複なしで抽選する 3 系統 (2026-08-05)。
        /// 希望は横移動・戦闘で一方的に減る一方で回復源が無く、 進路判断の自由度を奪っていた。
        /// 前哨基地での自動回復は「毎層リセット」になり発狂到達率が 0.4% まで落ちて棄却したので、
        /// **ゴールドを払って買う**形にして資源のやり取りとして残す。</summary>
        public static readonly string[] ConsumableRandomFamilies =
            { ConsShieldFamily, ConsPowerFamily, ConsHopeFamily };

        /// <summary>全系統 (効果解決・カタログ用)。</summary>
        public static readonly string[] ConsumableFamilies =
            { ConsHealFamily, ConsShieldFamily, ConsPowerFamily, ConsHopeFamily };

        /// <summary>消費アイテムの Tier 段数 (1〜4)。</summary>
        public const int ConsumableMaxTier = 4;

        /// <summary>系統と Tier(1〜4) から消費アイテムの id を引く。 無ければ null。
        ///
        /// <para><b>id を組み立てない</b> (2026-09-22)。 id は表示名になったので
        /// prefix + 数字 では作れない。 items.json の <c>consFamily</c> / <c>tier</c> を引く。</para></summary>
        public static string ConsId(string family, int tier)
            => InventorySystem.ItemDatabase.Instance?.GetConsumable(family, tier)?.id;

        /// <summary>消費アイテム id から Tier(1〜4) を取る。 消費アイテムでなければ 0。
        /// <b>id を綴りで切らない</b> (2026-09-22) ── items.json の tier を読む。</summary>
        public static int ConsTierOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            var d = InventorySystem.ItemDatabase.Instance?.GetItem(itemId);
            if (d == null || string.IsNullOrEmpty(d.consFamily)) return 0;
            return d.tier >= 1 && d.tier <= ConsumableMaxTier ? d.tier : 0;
        }

        /// <summary>その id が消費アイテム系統 (旧 cons_*) か。</summary>
        public static bool IsConsumableFamilyItem(string itemId)
        {
            var d = string.IsNullOrEmpty(itemId) ? null : InventorySystem.ItemDatabase.Instance?.GetItem(itemId);
            return d != null && !string.IsNullOrEmpty(d.consFamily);
        }

        /// <summary>その id の消費アイテム系統 (heal/def/dmg/hope)。 消費アイテムでなければ null。
        /// <b>接頭辞では判定しない</b> (2026-09-22)。</summary>
        public static string ConsFamilyOf(string itemId)
        {
            var d = string.IsNullOrEmpty(itemId) ? null : InventorySystem.ItemDatabase.Instance?.GetItem(itemId);
            return string.IsNullOrEmpty(d?.consFamily) ? null : d.consFamily;
        }

        /// <summary>その id がユニーク品 (旧 uniq_*) か。 <b>昇華の対象外</b>。</summary>
        public static bool IsUniqueItem(string itemId)
        {
            var d = string.IsNullOrEmpty(itemId) ? null : InventorySystem.ItemDatabase.Instance?.GetItem(itemId);
            return d != null && d.unique;
        }
    }

    /// <summary>ボス ID の prefix / 特定 ID。 「boss_layer」 直書き分岐の置き換え先。</summary>
    public static class BossIds
    {
        /// <summary>全ボス共通 prefix。 ボス判定は IsBoss() を使う。
        /// <b>末尾の数字は層番号とは限らない</b> (Layer3Marsh/Goblin/Mirror・Layer7Prefix を参照)。</summary>
        public const string LayerPrefix = "boss_layer";
        /// <summary>ヴェスカ 4 段連戦の prefix (boss_layer7 / _p2 / _p3 / _p4)。
        ///
        /// <para><b>「7」は層番号ではない (2026-09-14〜)。</b> ヴェスカは 7 層から
        /// 8 層 Null Point へ移った (7 層の終端は〈門〉になった) が、 <b>ID は据え置いた</b>
        /// ── 学習ファイル・Tier 表・boss_tuning.json のキーが ID 基準で、 改名すると
        /// 過去データと切れる。 3 層プールの <c>boss_layer2</c>/<c>boss_layer4</c> と同じ扱い。
        /// 層 → ボス ID の対応の正本は <c>FloorManager.BossIdForFloor</c>。</para></summary>
        public const string Layer7Prefix = "boss_layer7";

        // ---- 3 層ボスプール (2026-07-29) ----
        // ボス配置を 1/3/5/6/7 に絞った際、 2 層 (ゴブリン王) / 4 層 (鏡の双子) を捨てず
        // 3 層難度へ再調整して流用する。 id は据え置き ── 学習ファイル・Tier 表・
        // boss_tuning.json のキーが id 基準なので、 改名すると過去データと切れる。
        /// <summary>3 層ボス: 毒沼の主 (従来の 3 層ボス)。</summary>
        public const string Layer3Marsh  = "boss_layer3";
        /// <summary>3 層ボス: ゴブリン王 (旧 2 層ボスを流用)。</summary>
        public const string Layer3Goblin = "boss_layer2";
        /// <summary>3 層ボス: 鏡の双子 (旧 4 層ボスを流用)。</summary>
        public const string Layer3Mirror = "boss_layer4";
        /// <summary>5 層ボス: 業火の審判官。 〈審判の炎〉(軽減無視の毎ターン確定ダメ) を持つ。</summary>
        public const string Layer5Judgment = "boss_layer5";
        /// <summary>5 層 隠しボス: シュヴァリエ・サン=ジョリオラ。 レイピア所持時のみ 5 層ボスと差し替わる。
        /// 計測では <c>GameManager.SuppressLayer5HiddenBoss</c> で遮断する (既定 ON)。</summary>
        public const string Layer5Hidden = "boss_layer5_hidden";
        /// <summary>最終段 (ヴェスカ・天与)。 8 層 Null Point。</summary>
        public const string VescaFinal = "boss_layer7_p4";

        /// <summary>敵 ID がボスか (null 安全)。</summary>
        public static bool IsBoss(string enemyId)
            => !string.IsNullOrEmpty(enemyId) && enemyId.StartsWith(LayerPrefix);
    }
}

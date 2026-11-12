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
        public const string GoldenKingSword = "溶金の領主剣";
        /// <summary>天工開物: GameManager の強化フックが参照。</summary>
        public const string HeavenlyCraft = "天工開物";
        /// <summary>竜閃: items.json に family / tier を持たない特殊武器。 戦闘演出 (Game2DBattleSim) が竜閃の絵とモーションを選ぶ判定に使う。</summary>
        public const string Ryusen = "竜閃";
        /// <summary>シュヴァリエのレイピア: コントラタック切替 (Consumables.TryUseRapier)。</summary>
        public const string ChevalierRapier = "シュヴァリエのレイピア";

        // === 店連動パッシブ (2026-09-15) ===
        //   ShopManager が所持判定に使う ＝ 跨ファイル参照なのでここを経由する。
        //   下の帯 (BRONZE/SILVER) に店の機能へ触る品が 1 つも無かったのを埋めるもの。
        /// <summary>合札: 同じショップで 2 品目以降を買うとき 10G 安い (ShopManager.TryBuy・2026-10-31)。</summary>
        public const string TrialTossToken = "合札";

        // === 確信チェーンのフラグ品 (5F-6F の進路・2026-11-12 に定数化) ===
        //   イベント文 (Resources/Events/event_list.txt) の [] 内の表記と一致必須。
        //   チェーンのパッシブ (決意 / 根拠のない確信 / 真理) は ConvictionSystem.Id* が持つ。
        /// <summary>苦難の予言: 預言者の石板で得るフラグ。 次段〈啓示〉の条件。</summary>
        public const string HardshipProphecy = "苦難の予言";
        /// <summary>苦難の確信: 〈啓示〉で得るフラグ。 次段〈災厄の予兆〉の条件。</summary>
        public const string HardshipConviction = "苦難の確信";

        // === 〈さびれた観測所〉の時計 (2026-11-13) ===
        /// <summary>止まらない時計: 写しを持った再訪の 5 つ目の選択肢 (効果〈止まらない時計を預かる〉) で得る<b>秘宝</b> (items.json の同名の品・
        /// RunState.ownedPassiveItems・2026-11-13 にフラグから秘宝へ)。 持ったまま七層のヴェスカの戦闘に入ると、 各段のダウンの目盛りに 1 つ足す
        /// (<see cref="GameLoop.VescaClock"/>・CombatSystem.BossParams.VescaClockNotch)。 配布の山には出ない (EventOnlyItemFilter)・預かり物 (<see cref="Entrusted"/>)。</summary>
        public const string VescaClock = "止まらない時計";

        // === 秘宝の守りの 3 つの集合 (2026-11-13 に整理) ===
        //   経路の側は集合を直接読まず、 下の判定メソッドを呼ぶ (集合の取り違えを防ぐため)。 id の綴りでは判定しない。
        //
        //   | 集合                     | 中身                                   | 読む判定 → 経路                                                          |
        //   |--------------------------|----------------------------------------|--------------------------------------------------------------------------|
        //   | Undroppable (手放せない) | 確信の 3 品 + Entrusted                | CanSell      → ShopManager.TrySell (人・BOT・キー操作)・売る画面 SellList |
        //   |                          |                                        | CanTradeAway → GameManager.LowestTierPassiveIndex (交換マス・イベントの取り替え) |
        //   |                          |                                        | CanBeLost    → GameManager.LoseRandomPassiveItem (偽の商人に敗北)           |
        //   | Entrusted (預かり物)     | 止まらない時計                         | HiddenInCodexUntilOwned → Game2DCodexPanel (手に入れるまで枠も出さない)    |
        //   | ProgressionItems (進行用) | 確信の 3 品・時計・灯火・レイピア・    | CanOfferAtGate   → GameManager.OfferGateRelics (〈門〉で捧げない)          |
        //   |                          | 初期装備・武器階梯 T2/T3 ほか          | CanBeTakenBySin  → GameManager.Challenge (大罪の強欲・色欲)               |
        //   |                          |                                        | (直接) AutoTest.ItemLearningStats.ExcludedFromLift (BOT の学習から外す)  |
        //
        //   名指しで外す経路 (イベントの「廃棄」・灯火/フルーレ・バレエの自壊・剣の舞の集約) は集合を読まない。
        //   〈ちいさな灯火〉〈シュヴァリエのレイピア〉は手放せない品ではなく、 売値 0G (items.json の noSellValue・BOT は売らない・ShopManager.SellsForNothing)。

        /// <summary>**預かり物** (2026-11-13): 人から預かった秘宝。 この集合が<b>単独で</b>決めるのは「記録 (図鑑) には手に入れるまで
        /// 「？？？」の枠も並べない」(超低確率のイベントの種明かしになるため・<see cref="HiddenInCodexUntilOwned"/>) だけ。
        /// 売れない・取り替えに出さない・ランダムに失わないは <see cref="Undroppable"/> (この集合を含む) が決める。
        /// 〈門〉・大罪 (強欲・色欲)・BOT の学習から外すのは <see cref="ProgressionItems"/> の側 (両方に入れる)。</summary>
        public static readonly System.Collections.Generic.HashSet<string> Entrusted =
            new System.Collections.Generic.HashSet<string> { VescaClock };

        /// <summary>**手放せない品** (2026-11-13): 売れない (<see cref="CanSell"/>)・売る画面に並ばない・取り替えに出さない
        /// (<see cref="CanTradeAway"/>)・ランダムに失わない (<see cref="CanBeLost"/>)。 中身は確信の 3 品 (〈根拠のない確信〉〈決意〉〈真理〉・
        /// 進行の資格を表す品) と預かり物 (<see cref="Entrusted"/>)。 確信の 3 品は図鑑で枠を隠さない (Entrusted には入れない)。
        /// <para>資格そのものはフラグ (<c>layer6Unlocked</c>・<c>layer7Unlocked</c>・<c>convictionStage</c>) が持つ。 2026-11-13 までは
        /// 確信を売っても資格は残り、 品だけが消えて〈決意〉〈真理〉が持ち物と図鑑に出なかった (GAME.md §24)。</para>
        /// <para><see cref="Entrusted"/> より後に置く (静的フィールドの初期化は書いた順)。</para></summary>
        public static readonly System.Collections.Generic.HashSet<string> Undroppable =
            new System.Collections.Generic.HashSet<string>(Entrusted)
        {
            ConvictionSystem.IdConviction, ConvictionSystem.IdResolve, ConvictionSystem.IdTruth,
        };

        /// <summary>店で売れるか (ShopManager.TrySell・売る画面)。 手放せない品 (<see cref="Undroppable"/>) は false。</summary>
        public static bool CanSell(string id) => !string.IsNullOrEmpty(id) && !Undroppable.Contains(id);
        /// <summary>売れない時の断りの文 (売れるなら null)。 預かり物は「預かった品は売れない」、 確信の 3 品は「これは手放せない」。</summary>
        public static string RefuseSellText(string id)
            => CanSell(id) ? null : Entrusted.Contains(id) ? "預かった品は売れない" : "これは手放せない";
        /// <summary>取り替え (交換マス・イベント「パッシブを差し出して替える」) に出せるか。 手放せない品は false。</summary>
        public static bool CanTradeAway(string id) => !string.IsNullOrEmpty(id) && !Undroppable.Contains(id);
        /// <summary>ランダムな喪失 (偽の商人に敗北) で失われ得るか。 手放せない品は false。</summary>
        public static bool CanBeLost(string id) => !string.IsNullOrEmpty(id) && !Undroppable.Contains(id);
        /// <summary>図鑑で、 手に入れるまで「？？？」の枠も出さない品か (預かり物 <see cref="Entrusted"/> だけ)。</summary>
        public static bool HiddenInCodexUntilOwned(string id) => !string.IsNullOrEmpty(id) && Entrusted.Contains(id);
        /// <summary>〈門〉で秘宝として捧げられるか。 進行用の品 (<see cref="ProgressionItems"/>) は false。</summary>
        public static bool CanOfferAtGate(string id) => !string.IsNullOrEmpty(id) && !ProgressionItems.Contains(id);
        /// <summary>大罪 (強欲・色欲) が封じる・奪う対象になり得るか。 進行用の品 (<see cref="ProgressionItems"/>) は false。</summary>
        public static bool CanBeTakenBySin(string id) => !string.IsNullOrEmpty(id) && !ProgressionItems.Contains(id);

        // === 初期装備 ===
        public const string DiceWood = "dice_wood";

        // === ユニーク消耗品 (uniq_*) ===
        public const string PhilStone    = "uniq_phil_stone";
        // 2026-07-21: 消費アイテム全面再編で ForgeElixir/Mirror/Gambler/Ambush/Appraise/MerchantBell/OniOil/HastePowder
        //             を削除。 消費アイテムは 5 カテゴリ (cons_heal/atk/def/dmg/food) + 賢者の石 に集約。
        // 職業スターター 4 種は ClassStarter (RunState.cs) が正本。

        // === 消費アイテム 3 系統 × Tier1〜4 (2026-08-04 再編) ===
        //   旧 5 系統 (heal/atk/def/dmg/food) から 3 系統へ。 削除したのは:
        //     ・cons_food_* (物資回復) …… 物資の供給はイベント/報酬側へ寄せた
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
        //   各系統の 4 段の id (= 表示名)。 効果量の正本は Consumables (HealRatio / ShieldAmount / Apply の攻撃強化)。
        //   BOT が段の順に選ぶので、 本体と BOT はこの定数で品を指す (2026-11-12・#129)。
        public const string HealPotion1 = "小回復薬", HealPotion2 = "回復薬", HealPotion3 = "上回復薬", HealPotion4 = "完全回復薬";
        public const string Talisman1 = "木の護符", Talisman2 = "鉄の護符", Talisman3 = "銀の護符", Talisman4 = "惜別の護符";
        public const string PowerOil1 = "鬼火の油", PowerOil2 = "燐の油", PowerOil3 = "業火の膏薬", PowerOil4 = "天火の膏薬";
        //   物資回復の系統 (provision) は 2026-10-31 に廃止。 物資はショップの「物資の包」で買う
        //   (ShopManager.TryBuyProvisionPack)。 品を挟んで買わせる段は冗長だった。 4 品のうち
        //   〈古い手紙〉だけ効果を替えて残した (OldLetter)。

        /// <summary>古い手紙 (2026-10-31): 次のターンの被ダメを 0 にする。 1 枚で <see cref="OldLetterUses"/> 回使える。
        /// 使用回数を数えるので Consumables.Use と RunState から参照する。</summary>
        public const string OldLetter = "古い手紙";
        public const int OldLetterUses = 3;

        // === 航行マップで使う消耗品 (2026-09-28・2026-10-18 に囮と罠を統合) ===
        //   **系統で判定する** (ConsFamilyOf)。 下の id 定数は「開始時に配る」「店で買う」の
        //   跨ファイル参照のためだけにあり、 効果の分岐には使わない。
        public const string ConsLureFamily = "lure";   // 囮罠: 近くの追跡者の気を引き、 踏んだ追跡者を足止めする
        /// <summary>囮罠 (items.json の id ＝ 表示名)。 旧〈囮〉〈罠〉は LegacyIdMap が解決する。</summary>
        public const string Lure = "囮罠";

        // === 航行アイテム 13 品 (2026-11-11・docs/design/nav-items.md) ===
        //   どれもパッシブ (〈前哨の旗〉だけ層に 1 回「使う」)。 効果は GameLoop.NavItems が係数 (FreeMapPerks) にして航行の規則へ渡す。
        //   FreeMapSim はこれらの id を見ない。
        public const string NavSmokeBall    = "煙玉";
        public const string NavOiledBoots   = "油布の靴";
        public const string NavStoneShard   = "魔石の欠片";
        public const string NavMapSketch    = "地図売りの走り書き";
        public const string NavCompassNeedle = "羅針の針";
        public const string NavStealthCloak = "忍びの外套";
        public const string NavMirrorCharm  = "鏡の護符";
        public const string NavNightRing    = "夜目の指輪";
        public const string NavSupplyTally  = "補給商の割符";
        public const string NavBirdCompass  = "渡り鳥の羅盤";
        public const string NavFleetCharm   = "逃げ足の護符";
        public const string NavHunterBlade  = "追っ手殺しの刃";
        public const string NavOutpostFlag  = "前哨の旗";

        /// <summary>航行アイテム 13 品の id。</summary>
        public static readonly System.Collections.Generic.HashSet<string> NavItems =
            new System.Collections.Generic.HashSet<string>
        {
            NavSmokeBall, NavOiledBoots, NavStoneShard, NavMapSketch, NavCompassNeedle, NavStealthCloak, NavMirrorCharm,
            NavNightRing, NavSupplyTally, NavBirdCompass, NavFleetCharm, NavHunterBlade, NavOutpostFlag,
        };

        // === 進行用の品 ===
        /// <summary>**進行用の品**: 強さではなく進行度や配布条件を表す品。 〈門〉で秘宝として捧げられない
        /// (<see cref="CanOfferAtGate"/>)・大罪 (強欲・色欲) の対象にならない (<see cref="CanBeTakenBySin"/>)。
        /// BOT の lift 評価もこれを除外する (AutoTest.ItemLearningStats.ExcludedFromLift)。
        /// <b>売れる・取り替えに出せるかはこの集合では決まらない</b> (<see cref="Undroppable"/>) ── 〈ちいさな灯火〉〈シュヴァリエのレイピア〉は売れる (売値 0G)。</summary>
        public static readonly System.Collections.Generic.HashSet<string> ProgressionItems =
            new System.Collections.Generic.HashSet<string>
        {
            // 確信チェーン (5F-6F の進路フラグ・アイテム)
            ConvictionSystem.IdTruth, ConvictionSystem.IdResolve, ConvictionSystem.IdConviction,
            HardshipProphecy, HardshipConviction,
            // デフォルト/初期装備
            DiceWood,
            // 起動でしか取れないイベント専用アイテム
            ItemSystem.PassiveItems.TorchRevival.TorchId,
            // 預かり物 (2026-11-13・Entrusted): 博士の時計。 効くのはヴェスカの戦闘だけで、 出る条件が数百ランに 1 度 ── 学習に乗せても何も言えない
            VescaClock,
            // フラグ系武器 (進路条件で配布)
            ChevalierRapier,
            // 武器階梯 T2/T3 (職業配布と進化の途中段)。 T4 は終端なので含めない
            "鍛鉄の剣",   "銀の長剣",
            "鉄盾",  "聖騎士の盾",
            "猛斧",     "血塗りの戦斧",
            "盗賊の短刀",  "処刑人の曲刀",
        };

        /// <summary>**回復は毎回並ぶ。** 消費枠は 3 で、 1 枠目を回復で固定する。
        /// 回復が並ばない店があると消費で耐久を賄う設計自体が成立しないため (2026-08-04)。</summary>
        public static readonly string ConsumableFixedFamily = ConsHealFamily;

        /// <summary>残り 2 枠の系統。 物資回復の系統を 2026-10-31 に外したので、 シールドと攻撃強化が毎回並ぶ
        /// (物資はゴールドで「物資の包」を買う)。</summary>
        public static readonly string[] ConsumableRandomFamilies =
            { ConsShieldFamily, ConsPowerFamily };

        /// <summary>全系統 (効果解決・カタログ用)。</summary>
        public static readonly string[] ConsumableFamilies =
            { ConsHealFamily, ConsShieldFamily, ConsPowerFamily };

        /// <summary>消費アイテムの Tier 段数 (1〜4)。</summary>
        public const int ConsumableMaxTier = 4;

        /// <summary>系統と Tier(1〜4) から消費アイテムの id を引く。 無ければ null。
        ///
        /// <para><b>id を組み立てない</b> (2026-09-22)。 id は表示名になったので
        /// prefix + 数字 では作れない。 items.json の <c>consFamily</c> / <c>tier</c> を引く。</para></summary>
        public static string ConsId(string family, int tier)
            => ItemSystem.ItemDatabase.Instance?.GetConsumable(family, tier)?.id;

        /// <summary>消費アイテム id から Tier(1〜4) を取る。 消費アイテムでなければ 0。
        /// <b>id を綴りで切らない</b> (2026-09-22) ── items.json の tier を読む。</summary>
        public static int ConsTierOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            var d = ItemSystem.ItemDatabase.Instance?.GetItem(itemId);
            if (d == null || string.IsNullOrEmpty(d.consFamily)) return 0;
            return d.tier >= 1 && d.tier <= ConsumableMaxTier ? d.tier : 0;
        }

        /// <summary>その id の消費アイテム系統 (heal/def/dmg/provision)。 消費アイテムでなければ null。
        /// <b>接頭辞では判定しない</b> (2026-09-22)。</summary>
        public static string ConsFamilyOf(string itemId)
        {
            var d = string.IsNullOrEmpty(itemId) ? null : ItemSystem.ItemDatabase.Instance?.GetItem(itemId);
            return string.IsNullOrEmpty(d?.consFamily) ? null : d.consFamily;
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
        /// <summary>6 層ボス: 灰燼の王。</summary>
        public const string Layer6Ash = "boss_layer6";
        /// <summary>最終段 (ヴェスカ・天与)。 8 層 Null Point。</summary>
        public const string VescaFinal = "boss_layer7_p4";
        /// <summary>ヴェスカの段 1〜3 (段 4 は <see cref="VescaFinal"/>)。</summary>
        public const string VescaP1 = "boss_layer7", VescaP2 = "boss_layer7_p2", VescaP3 = "boss_layer7_p3";

        /// <summary>0 層 (ファーム層・2026-11-05) のボス: 古い宝箱 (HP100・攻撃なし)。 <see cref="IsBoss"/> に掛かるよう prefix を揃えた。</summary>
        public const string FarmBoss = "boss_layer0";
        /// <summary>0 層の雑魚: 古いクレート (HP1・攻撃なし)。</summary>
        public const string FarmCrate = "farm_crate";

        /// <summary>ヴェスカの段の番号 (1〜4)。 ヴェスカでない id (null を含む) は 0。
        /// <b>id → 段の表を引く。 綴り (末尾の _p2 など) で切らない</b> (issue #116)。</summary>
        public static int VescaStageOf(string id)
        {
            switch (id)
            {
                case VescaP1: return 1;
                case VescaP2: return 2;
                case VescaP3: return 3;
                case VescaFinal: return 4;
                default: return 0;
            }
        }

        /// <summary>ヴェスカの次の段の id。 最終段とヴェスカでない id は null。</summary>
        public static string VescaNextStage(string id)
        {
            switch (VescaStageOf(id))
            {
                case 1: return VescaP2;
                case 2: return VescaP3;
                case 3: return VescaFinal;
                default: return null;
            }
        }

        /// <summary>敵 ID がボスか (null 安全)。</summary>
        public static bool IsBoss(string enemyId)
            => !string.IsNullOrEmpty(enemyId) && enemyId.StartsWith(LayerPrefix);
    }
}

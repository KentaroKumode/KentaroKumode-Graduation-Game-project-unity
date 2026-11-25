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
    /// <summary>賽の id (ADR-0015・2026-11-18)。 id = 表示名 (items.json の category Dice)。 賽のパッシブ (CombatSystem.DiePassives) が分岐に使う。</summary>
    public static class DieIds
    {
        public const string Starting   = "角の取れた賽";
        public const string RustyNail  = "錆釘の賽";
        public const string Scavenger  = "拾い物の賽";
        public const string Dull       = "鈍色の賽";
        public const string Ember      = "残り火の賽";
        public const string Ferry      = "渡し守の賽";
        public const string NightWatch = "夜番の賽";
        public const string Scale      = "秤の賽";
        public const string Reverse    = "逆目の賽";
        public const string OldCoin    = "古銭の賽";
        public const string Link       = "繋ぎの賽";
        public const string Appraiser  = "目利きの賽";
        public const string HeavyRead  = "兆し読みの賽";
        public const string Gravestone = "墓標の賽";
        public const string Rewind     = "逆巻きの賽";
        public const string Clock      = "時計の賽";
        public const string Gamble     = "賭場の賽";
        public const string Straight   = "連なりの賽";
        public const string Cursed     = "呪物の賽";
    }

    public static class ItemIds
    {
        // === 名前付きパッシブ (日本語 ID ── items.json の id と一致必須) ===
        // MerchantSeal (商人の符牒) / GourmetKnife (食通の懐刀) は 2026-07-18 削除 (アイテム削除に伴う)
        /// <summary>溶金の領主剣: ShopManager.Close が店で使った額を数える (RunState.swordShopSpent)。</summary>
        public const string GoldenKingSword = "溶金の領主剣";
        /// <summary>金の品 (2026-11-21・full-item-rework-ledger §⑨・GoldEffects.cs)。 銭皿 = AxisPool.SkipGoldFor / 天秤 = GameManager.Rewards。</summary>
        public const string GoldCoinDish = "銭皿", GoldScale = "戦果の天秤", GoldLuckyCoin = "幸運の硬貨", GoldBlastPrize = "爆破祭の小切手";
        /// <summary>天工開物: GameManager の強化フックが参照。</summary>
        public const string HeavenlyCraft = "天工開物";
        /// <summary>竜閃: items.json に family / tier を持たない特殊武器。 戦闘演出 (Game2DBattleSim) が竜閃の絵とモーションを選ぶ判定に使う。</summary>
        public const string Ryusen = "竜閃";
        /// <summary>シュヴァリエのレイピア: コントラタック切替 (Consumables.TryUseRapier)。</summary>
        public const string ChevalierRapier = "シュヴァリエのレイピア";

        /// <summary>最初に持つ賽 (ADR-0015・2026-11-18)。 面 2,3,4,5・効果なし。 店には並ばない。
        /// 墓標の賽が砕けた後もこれに戻る。</summary>
        public const string StartingDie = DieIds.Starting;

        // === 店連動パッシブ (2026-09-15) ===
        //   ShopManager が所持判定に使う ＝ 跨ファイル参照なのでここを経由する。
        //   下の帯 (BRONZE/SILVER) に店の機能へ触る品が 1 つも無かったのを埋めるもの。
        /// <summary>合札: 同じショップで 2 品目以降を買うとき 10G 安い (下限 1G・ShopManager.TryBuy)。</summary>
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
        //   | Entrusted (預かり物)     | 止まらない時計                         | IsEntrusted  → ItemCards (持ち物の画面の印「預かり物」)                    |
        //   | ProgressionItems (進行用) | 確信の 3 品・時計・灯火・レイピア・    | CanOfferAtGate   → GameManager.OfferGateRelics (〈門〉で捧げない)          |
        //   |                          | 初期装備・武器階梯 T2/T3 ほか          |                                                                          |
        //   |                          |                                        | (直接) AutoTest.ItemLearningStats.ExcludedFromLift (BOT の学習から外す)  |
        //
        //   | EndingKeyItems (結末に関わる品) | 確信の 3 品・時計               | HiddenInCodexUntilOwned → GalleryView (手に入れるまで影も出さない)・      |
        //   |                          |                                        |   AchievementService.CheckCodex (埋まり具合の分母に入れない)              |
        //
        //   名指しで外す経路 (イベントの「廃棄」・灯火/フルーレ・バレエの自壊・剣の舞の集約) は集合を読まない。
        //   〈ちいさな灯火〉〈シュヴァリエのレイピア〉は手放せない品ではなく、 売値 0G (items.json の noSellValue・BOT は売らない・ShopManager.SellsForNothing)。

        /// <summary>**預かり物** (2026-11-13): 人から預かった秘宝。 この集合が<b>単独で</b>決めるのは持ち物の画面の印「預かり物」(<see cref="IsEntrusted"/>) だけ。
        /// 図鑑で手に入れるまで隠すのは <see cref="EndingKeyItems"/> (この集合を含む・2026-11-17〜)。
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

        /// <summary>**結末に関わる品** (2026-11-17・作者の決定 (a)): 結末の道の種明かしになる品。 図鑑 (ギャラリーの品) では<b>手に入れるまで影も出さない</b>
        /// (<see cref="HiddenInCodexUntilOwned"/>)。 中身は確信の 3 品 (〈根拠のない確信〉〈決意〉〈真理〉) と預かり物 (〈止まらない時計〉・<see cref="Entrusted"/>)。
        /// 結末に関わる品を足す時はここに足す。 <see cref="Undroppable"/> と同じ中身だが、 「手放せない」と「図鑑で隠す」は別の決まりなので集合を分けておく。
        /// <para><see cref="Entrusted"/> より後に置く (静的フィールドの初期化は書いた順)。</para></summary>
        public static readonly System.Collections.Generic.HashSet<string> EndingKeyItems =
            new System.Collections.Generic.HashSet<string>(Entrusted)
        {
            ConvictionSystem.IdConviction, ConvictionSystem.IdResolve, ConvictionSystem.IdTruth,
        };

        /// <summary>**進行の資格を表す品** (2026-11-13): 確信の 3 品 (〈根拠のない確信〉〈決意〉〈真理〉)。 <see cref="Undroppable"/> の一部。
        /// 嵐の不安定化 (GAME.md §5-12) で「秘宝を得た +3」に数えない (<see cref="CountsAsTakenRelic"/>) ── 大穴から取った物ではなく、 資格を見せる物なので。</summary>
        public static readonly System.Collections.Generic.HashSet<string> QualificationItems =
            new System.Collections.Generic.HashSet<string> { ConvictionSystem.IdConviction, ConvictionSystem.IdResolve, ConvictionSystem.IdTruth };

        /// <summary>得た秘宝を、 嵐の不安定化の「秘宝を得た」に数えるか。 進行の資格の品 (<see cref="QualificationItems"/>) は数えない。
        /// 秘宝か (category) は呼ぶ側が見る。</summary>
        public static bool CountsAsTakenRelic(string id) => !string.IsNullOrEmpty(id) && !QualificationItems.Contains(id);

        /// <summary>店で売れるか (ShopManager.TrySell・売る画面)。 手放せない品 (<see cref="Undroppable"/>) は false。</summary>
        public static bool CanSell(string id) => !string.IsNullOrEmpty(id) && !Undroppable.Contains(id);
        /// <summary>売れない時の断りの文 (売れるなら null)。 預かり物は「預かった品は売れない」、 確信の 3 品は「これは手放せない」。</summary>
        public static string RefuseSellText(string id)
            => CanSell(id) ? null : Entrusted.Contains(id) ? "預かった品は売れない" : "これは手放せない";
        /// <summary>取り替え (交換マス・イベント「パッシブを差し出して替える」) に出せるか。 手放せない品は false。</summary>
        public static bool CanTradeAway(string id) => !string.IsNullOrEmpty(id) && !Undroppable.Contains(id);
        /// <summary>ランダムな喪失 (偽の商人に敗北) で失われ得るか。 手放せない品は false。</summary>
        public static bool CanBeLost(string id) => !string.IsNullOrEmpty(id) && !Undroppable.Contains(id);
        /// <summary>預かり物か (<see cref="Entrusted"/>)。 持ち物の画面の詳細の欄が印「預かり物」を出すのに読む (2026-11-13)。 売れるか等は <see cref="CanSell"/> の側。</summary>
        public static bool IsEntrusted(string id) => !string.IsNullOrEmpty(id) && Entrusted.Contains(id);
        /// <summary>図鑑 (ギャラリーの品) で、 手に入れるまで影も出さない品か (<see cref="EndingKeyItems"/>)。</summary>
        public static bool HiddenInCodexUntilOwned(string id) => !string.IsNullOrEmpty(id) && EndingKeyItems.Contains(id);
        /// <summary>〈門〉で秘宝として捧げられるか。 進行用の品 (<see cref="ProgressionItems"/>) は false。</summary>
        public static bool CanOfferAtGate(string id) => !string.IsNullOrEmpty(id) && !ProgressionItems.Contains(id);

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

        // === 戦術消耗品 (2026-11-20・item-overhaul-spec §7-1・名前は仮) ===
        //   items.json の consFamily = ConsTacticFamily・tier 1 (出来事 OnConsumableUse の量)。 効果の正本は Consumables.ApplyTactic。
        //   店の消費の 3 枠目に 1 種 (ShopManager・乱数キー "shop.consTactic")。 戦闘中だけ使える。
        public const string ConsTacticFamily = "tactic";
        public const string TacticSwap = "入れ替えの符", TacticOil = "無銘の潤滑油", TacticAcid = "鎧割りの酸瓶", TacticNeedle = "急所突きの針",
            TacticChisel = "目盛り削りのノミ", TacticSand = "反転の砂", TacticOrb = "倍化の共鳴玉", TacticMercury = "複製の水銀";

        // === 消耗品の連動品 (帯革・小瓶 = GeneralEffects.cs / 見本箱 = GoldEffects.cs) ===
        public const string LinkPouch = "帯革の薬嚢", LinkBottomless = "二度点きの燐寸", LinkSampleBox = "行商人の見本箱";

        // === 嵐・店・金のマクロ秘宝 (2026-11-20・item-overhaul-spec §9・名前は仮) ===
        //   嵐: 風見鶏・天幕 = GameManager.Instability / 気圧計 = GeneralEffects。 店: 算盤・手形・紹介状 = ShopManager (算盤の戦闘の側は GoldEffects) / 研ぎ代 = GameManager.Terminals + GoldEffects。
        //   報酬: 鑑定書 = GameManager.OfferTerminalReward / 交換帳 = GameManager.Tiles (交換マス)。
        public const string MacroVane = "嵐読みの風見鶏", MacroBarometer = "気圧計のペンダント", MacroTent = "防嵐の天幕";
        public const string MacroReceipt = "研ぎ代の領収書", MacroAbacus = "相場師の算盤", MacroPass = "黄金の通行手形";
        public const string MacroLetter = "目利きの紹介状", MacroAppraisal = "戦利の目録", MacroLedger = "古物商の交換帳";

        // === 山札の軸の 19 品 (full-item-rework-ledger §⑦) ===
        //   端子の解決・山札を変える分 = CombatSystem.TerminalCards の DeckItems の旗 (TerminalCards.DeckItemsOf)。 組み置きの配線束・整理された工具箱 = CombatManager.BeginTerminalDeck。
        //   出来事で書く分 (焼き入れの挟み の敵へのダメージ・仕込みの切替器 の目盛りの削り) = DeckEffects.cs。
        public const string DeckTongs = "焼き入れの挟み", DeckSparePlug = "組み置きの配線束", DeckClasp = "留め金の栞", DeckAuxGear = "補助歯車", DeckIdleWeight = "空回りの錘";
        public const string DeckBroom = "灰払いの箒", DeckRewind = "早回しのゼンマイ", DeckSwitchboard = "仕込みの切替器", DeckMold = "打ち抜きの金型", DeckToolbox = "整理された工具箱";
        public const string DeckCrucible = "再利用のるつぼ", DeckBlueprint = "無銘の設計図", DeckSeamless = "継ぎ目のない導線", DeckFolded = "取っておきの銀弾";
        public const string DeckPride = "切り捨ての半券";
        public const string DeckBag = "解体屋の袋", DeckWorkbench = "常設の作業台", DeckAsh = "廃材組みの蒸気機関", DeckEngine = "永久機関の主軸";

        // === 役の軸の 13 品 (2026-11-21・full-item-rework-ledger §⑧) ===
        //   出来事で書く品 (欠け目の見立て・揃いのメトロノーム・負け越しの賽・小口の錠前) = RoleEffects.cs。 端子の解決の中の分 = CombatSystem.TerminalCards の RoleItems の旗。
        //   擦り切れた賽袋 = CombatManager.FreeRerollsLeft。 曇りの合わせ鏡・宿命の音叉・万象の賽盆 = CombatManager.ResolveRoles / DecideOmniPair。
        public const string RoleBag = "擦り切れた賽袋", RoleWoodTag = "潮目の木札", RoleNotchBoard = "符丁の刻み板", RoleChipped = "欠け目の見立て";
        public const string RoleClapper = "揃いのメトロノーム", RoleMirror = "曇りの合わせ鏡", RoleFate = "宿命の音叉", RoleOmni = "万象の賽盆";
        public const string RoleLosing = "負け越しの賽", RoleConduit = "小口の錠前", RoleGear = "追い目の賽筒", RoleThunder = "轟きの大賽", RoleBloodmark = "連勝の銀杯";

        // === 充電の軸の品で、 端子の解決の中で効く 4 品 (2026-11-21・full-item-rework-ledger §①) = CombatSystem.ChargeCore の ChargeItems の旗 ===
        public const string ChargeFork = "二股の導線", ChargeRod = "逆さ避雷針", ChargeWhip = "灼線鞭", ChargeDrum = "雷鳴の太鼓";
        // === 毒の軸の品で、 端子の解決・起爆の式に効く 10 品 (2026-11-21・full-item-rework-ledger §②) = CombatSystem.PoisonCore の PoisonItems の旗 ===
        public const string PoisonGreenKnife = "緑染みの小刀", PoisonSerpent = "蛇血瓶", PoisonTaster = "毒見役の小匙", PoisonThorn = "毒棘の小盾",
            PoisonMantle = "瘴気の外套", PoisonBlowpipe = "麻痺の吹き矢", PoisonRing = "蓋付きの毒指輪", PoisonMagazine = "劇毒の火薬庫",
            PoisonSlowBlade = "遅効の毒刃", PoisonOil = "骨溶かしの黒油";
        // === シールドの軸の品で、 端子の解決 (5e)・ジャスト防御の許容幅に効く 4 品 (2026-11-21・full-item-rework-ledger §③) = CombatSystem.ShieldCore の ShieldItems の旗 ===
        public const string ShieldPauldron = "肩当て", ShieldPadded = "当て布の籠手", ShieldRivet = "鋲の胸当て", ShieldWorn = "擦り減った小盾";
        // === 出血の軸の品で、 端子の解決 (5f)・本体の撃の輪・BOT の見積もりに効く 16 品 (2026-11-21・full-item-rework-ledger §④) = CombatSystem.BleedCore の BleedItems の旗 ===
        public const string BleedLeadAxe = "鉛入りの手斧", BleedPendant = "血に澄む紅玉", BleedSurgeon = "医家の反り刃", BleedClasp = "返し刃の留め金",
            BleedChipped = "欠け刃のナイフ", BleedRift = "裂け目の楔", BleedSaw = "鋸刃の山刀", BleedFirstBlood = "血呼びの腕輪",
            BleedNeedles = "血止め知らずの針箱", BleedOpenNeedle = "医書の隠し針", BleedFury = "激情の刃", BleedCleaver = "百人切りの鉈",
            BleedSakura = "血桜", BleedPress = "紅の搾り具", BleedCrystal = "涸れぬ血晶", BleedWedge = "血の楔";
        // === 臨界の軸の品で、 爆発の式・端子の解決 (5g)・BOT と画面の見込みに効く 16 品 (2026-11-21・full-item-rework-ledger §⑤) = CombatSystem.HeatCore の HeatItems の旗 ===
        public const string HeatStone = "過熱石", HeatBreastplate = "燻りの革胸当て", HeatPoker = "火守りの火掻き棒", HeatSpanner = "点火スパナ",
            HeatWhetstone = "荒砥", HeatOven = "恒温の竈", HeatPrimer = "二度詰めの雷管", HeatCoal = "焚き足しの炭",
            HeatMold = "溢れ取りの鋳型", HeatLedger = "帳簿外の爆薬", HeatKettle = "早沸かし釜", HeatSpark = "火花読みの眼鏡",
            HeatAshen = "八日目の灰燼", HeatFlint = "燧石の指輪", HeatKiln = "暴れ窯の火床", HeatPillar = "柱折りの発破筒";

        // === 会心の軸の品で、 会心の式 (CombatSystem.CritCore)・本体の撃の輪・BOT の見積もりが読む品 (2026-11-21・full-item-rework-ledger §⑥) ===
        public const string CritFirstArrow = "一の矢", CritPommel = "渾身の鉄兜", CritSniper = "必殺の照準器", CritHeaven = "白濁の義眼",
            CritSuntetsu = "寸鉄", CritIronClub = "銑鉄棍", CritPinpoint = "一点読みの片眼", CritQuickDraw = "抜き打ちの作法",
            CritClairvoyant = "千里眼の水晶", CritMask = "死相の面頬", CritAim = "狙い目の刻み",
            CritNick = "刃こぼれの見立て", CritLapis = "瑠璃砥石", CritSurveyor = "測量師の片眼鏡";
        /// <summary>会心の軸に 2026-11-23 (会心の仕組みの転換・crit_redesign answer8) に足した 6 品 (仮名・作者が名前を決めるまで)。</summary>
        public const string CritPulse = "脈打ちの測り針", CritReadMark = "見切りの目印", CritWhiteBlade = "白刃の鏡砥",
            CritFirstStroke = "初太刀の黒鞘", CritNarrow = "細目の覗き筒", CritOneFlash = "一閃の極意";
        // === ボス・大技の戦術の 4 品 (軸なし汎用) = GeneralEffects.cs ===
        public const string TacGapSpear = "隙突きの短槍";
        public const string TacBreak = "崩しの一手", TacWedge = "構え崩しの鉄杭", TacHourglass = "背水の錨";

        // === 航行マップで使う消耗品 (2026-09-28・2026-10-18 に囮と罠を統合) ===
        //   **系統で判定する** (ConsFamilyOf)。 下の id 定数は「開始時に配る」「店で買う」の
        //   跨ファイル参照のためだけにあり、 効果の分岐には使わない。
        public const string ConsLureFamily = "lure";   // 囮罠: 近くの追跡者の気を引き、 踏んだ追跡者を足止めする
        /// <summary>囮罠 (items.json の id ＝ 表示名)。 2026-10-18 に旧〈囮〉〈罠〉を統合 (旧 id の対応表は 2026-11-14 に削除)。</summary>
        public const string Lure = "囮罠";

        // === 航行アイテム 10 品 (2026-11-11・docs/design/nav-items.md・2026-11-24 に〈魔石の欠片〉〈羅盤の針〉〈忍びの外套〉を削除 ── 魔石と痕跡が無くなり効果が成り立たない) ===
        //   どれもパッシブ (〈前哨の旗〉だけ層に 1 回「使う」)。 効果は GameLoop.NavItems が係数 (FreeMapPerks) にして航行の規則へ渡す。
        //   FreeMapSim はこれらの id を見ない。
        public const string NavSmokeBall    = "煙玉";
        public const string NavOiledBoots   = "油布の靴";
        public const string NavMapSketch    = "地図売りの走り書き";
        public const string NavMirrorCharm  = "鏡の護符";
        public const string NavNightRing    = "夜目の指輪";
        public const string NavSupplyTally  = "補給商の割符";
        public const string NavBirdCompass  = "渡り鳥の羅盤";
        public const string NavFleetCharm   = "逃げ足の護符";
        public const string NavHunterBlade  = "追っ手殺しの刃";
        public const string NavOutpostFlag  = "前哨の旗";

        /// <summary>航行アイテム 10 品の id。</summary>
        public static readonly System.Collections.Generic.HashSet<string> NavItems =
            new System.Collections.Generic.HashSet<string>
        {
            NavSmokeBall, NavOiledBoots, NavMapSketch, NavMirrorCharm,
            NavNightRing, NavSupplyTally, NavBirdCompass, NavFleetCharm, NavHunterBlade, NavOutpostFlag,
        };

        // === 進行用の品 ===
        /// <summary>**進行用の品**: 強さではなく進行度や配布条件を表す品。 〈門〉で秘宝として捧げられない
        /// (<see cref="CanOfferAtGate"/>)。 (旧: 大罪の強欲・色欲が奪う・封じる対象から外す ── 2026-11-22 に秘宝を奪う効果ごと撤去)
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

        // === 武器の家系 (items.json の family・2026-11-17 に定数化) ===
        //   段階式の武器 (WeaponProgression) と実績〈四つの得物〉が読む。 家系は id の綴りでなく items.json の family で読む。
        public const string FamilySword = "sword", FamilyAxe = "axe", FamilyDagger = "dagger", FamilyShield = "shield";
        /// <summary>段階式の武器の 4 家系 (剣・斧・短剣・盾)。</summary>
        public static readonly string[] WeaponFamilies = { FamilySword, FamilyAxe, FamilyDagger, FamilyShield };

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
        /// <summary>5 層ボス: 審判官。 〈審判の炎〉(軽減無視の毎ターン確定ダメ) を持つ。</summary>
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

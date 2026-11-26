using System.Collections.Generic;
using C = MetaProgression.Achievements.AchievementCategory;

namespace MetaProgression.Achievements
{
    /// <summary>
    /// 実績の一覧の<b>正本</b> (2026-11-17 再編・docs/GAME.md §15-6 はこの写し)。 判定は <see cref="AchievementService"/>。
    ///
    /// <para><b>id は保存に使う</b> (<see cref="MetaProgressState.unlockedAchievementIds"/>)。 変えると解除済みが消える。
    /// 発売前なので旧 id の対応表は作らない。 発売後に変える時は対応表を作る。 id の定数はここだけに置く (ほかのファイルは定数を通す)。</para>
    ///
    /// <para><b>文の決まり</b>: 画面の層の番号で書く (判定は Run.Depth・<see cref="MapSystem.FreeMove.FloorPlan.DepthOf"/>)。 「最終層」と書かない。
    /// 画面の語を使う (振り直す・逃げる・遺物を持ち込む・挑戦カード・「大罪:○○」の札・ランの持ち物は秘宝)。 「ダイス合計」は使わない。
    /// <b>隠しでない実績</b>の名前と文には、 4 層・5 層・灰燼の王・ヴェスカ・結末の名前・結末に関わる品を出さない (検査 AchievementVerify)。</para>
    /// </summary>
    public static class AchievementCatalog
    {
        // ===== id (保存に使う・変えない) =====
        // 進行
        public const string FirstRun = "first_run", Boss1 = "boss_1", Boss3 = "boss_3", CombatWins100 = "combat_wins_100", Boss3All = "boss3_all";
        // 結末
        public const string Conviction = "conviction", Boss5 = "boss_5", HiddenSaintGeorges = "hidden_saint_georges", Reach6 = "reach_6",
            AshKing = "ash_king", HiddenNoBlazeBoss6 = "hidden_no_blaze_boss_6", Reach7 = "reach_7", GateFlawless = "gate_flawless",
            Clear7 = "clear_7", EndGood = "end_good", EndTrue = "end_true", VescaClock = "vesca_clock";
        // 挑戦カード
        public const string ChallengeD3 = "challenge_d3", ChallengeD6 = "challenge_d6", ChallengeD10 = "challenge_d10", SinsAll = "sins_all",
            BuffsAll = "buffs_all", ChallengeKingD10 = "challenge_king_d10", ChallengeVescaD10 = "challenge_vesca_d10";
        // 戦闘の腕前
        public const string WinAt1Hp = "win_at_1_hp", BossNoHpDamage = "boss_no_hp_damage", BossOneTurn = "boss_one_turn",
            DownCancel = "down_cancel", DownDouble = "down_double", Escalation4 = "escalation_4", BerserkWin = "berserk_win",
            Crit100 = "crit_100", PierceZero = "pierce_zero", Elite20 = "elite_20";
        // キーワード
        public const string Poison99 = "poison_99", BleedBig = "bleed_big", Charge30 = "charge_30", Burst5 = "burst_5";
        // 出目と役
        public const string FirstRole = "first_role", AllRoles = "all_roles", RoleZoro = "role_zoro", RoleTop = "role_top";
        // 端子
        public const string DeckMin = "deck_min", Deck30 = "deck_30", Terminal5Types = "terminal_5types";
        // 職業と武器
        public const string ClassSword = "class_sword", ClassKnight = "class_knight", ClassBerserker = "class_berserker",
            ClassAssassin = "class_assassin", WeaponTop = "weapon_top", WeaponAllTop = "weapon_all_top";
        // 航行
        public const string PursuerWin = "pursuer_win", PursuerUnseen = "pursuer_unseen", FamiliarWin = "familiar_win",
            LureHit = "lure_hit", StormBoss = "storm_boss", ProvisionZeroBoss = "provision_zero_boss";
        // 店と経済
        public const string ZeroGoldBoss6 = "zero_gold_boss_6", RobberyWin = "robbery_win", Gold1000 = "gold_1000",
            HiddenShopStubborn = "hidden_shop_stubborn", HiddenExactZeroPurchase = "hidden_exact_zero_purchase", RobberyRebuy = "robbery_rebuy";
        // 縛り
        public const string NoConsumableBoss5 = "no_consumable_boss_5", NoFleeBoss5 = "no_flee_boss_5", NoPurchaseBoss6 = "no_purchase_boss_6",
            NoRerollBoss6 = "no_reroll_boss_6", NoRelicConsumableClear7 = "no_relic_consumable_clear_7";
        // 遺物と整備パネル
        // 整備盤 (2026-11-23・遺物の 6 つと〈整備完了〉panel_full から差し替え。 持たざる者 no_relic_consumable_clear_7 は id を残して盤の条件に)
        public const string BoardOpenCenter = "board_open_center", BoardFirstPanel = "board_first_panel", BoardCross = "board_cross",
            BoardAllOpen = "board_all_open", BoardCoreClear = "board_core_clear", BoardFullClear = "board_full_clear", BoardShopAll = "board_shop_all";
        // 記録とイベント
        public const string Events50 = "events_50", EventsAll = "events_all", Observatory = "observatory", CodexHalf = "codex_half", CodexFull = "codex_full";
        // 遊び
        public const string HiddenOverkill = "hidden_overkill", HiddenThirteenthEnd = "hidden_thirteenth_end", HiddenTheseus = "hidden_theseus",
            HiddenCalmDown = "hidden_calm_down", HiddenPerpetualMotion = "hidden_perpetual_motion", HiddenTurn124 = "hidden_turn_124",
            HiddenReturningEnemy = "hidden_returning_enemy", CrateSlow = "crate_slow", BossOnlyFloor = "boss_only_floor", PairOneTurn = "pair_one_turn";

        // ===== しきい値 (仮・測らずに決めた・作者 2026-11-17)。 文と判定の両方がここを読む =====
        public const int CombatWinsGoal = 100, EliteGoal = 20, EventsGoal = 50, GoldGoal = 1000;
        /// <summary>挑戦の難易度の段 (腕試し・向かい風・底の底)。 底の底・燃え残り・底を割るは <see cref="ChallengeCards.MaxDifficulty"/>。</summary>
        public const int DifficultyLow = 3, DifficultyMid = 6;
        /// <summary>毒の上限 (StatusEffectSystem の毒の maxStacks = 99) と同じ値。</summary>
        public const int PoisonGoal = 99;
        public const int BleedGoal = 50, ChargeGoal = 30, BurstGoal = 5, CritGoal = 100, OverkillGoal = 100;
        // CrateSlowTurns: 0 層の古い宝箱が攻撃 2 になった (2026-11-20) ので 30 → 10。 30 ターンでは開始 HP 30 が持たない (守りに挿せば 10 ターンは耐えられる)
        public const int DeckBigGoal = 30, CrateSlowTurns = 10, ThirteenTurn = 13, ThirteenDamage = 13;
        public const int CalmDownTurns = 3, PerpetualHealTimes = 3, TheseusTimes = 10, StubbornCount = 3;
        // [2026-11-18] TopFaceTier (出目パーツの最上段 T4) は削除。 ADR-0015 で段が無くなり、 「奥の手」は LEGENDARY の賽で役を出すことにした (仮・作者の確認待ち)。

        // ===== 系統 =====

        /// <summary>系統の並び (ギャラリーの見出しの順)。</summary>
        public static readonly IReadOnlyList<AchievementCategory> Categories = (AchievementCategory[])System.Enum.GetValues(typeof(AchievementCategory));

        /// <summary>系統の名前 (画面の見出し)。</summary>
        public static string CategoryName(AchievementCategory c)
        {
            switch (c)
            {
                case C.Progress: return "進行";
                case C.Ending: return "結末";
                case C.Challenge: return "挑戦カード";
                case C.CombatSkill: return "戦闘の腕前";
                case C.Keyword: return "キーワード";
                case C.DiceRole: return "出目と役";
                case C.Terminal: return "カード";
                case C.ClassWeapon: return "職業と武器";
                case C.Navigation: return "航行";
                case C.ShopEconomy: return "店と経済";
                case C.Restriction: return "縛り";
                case C.RelicPanel: return "整備盤";
                case C.RecordEvent: return "記録とイベント";
                case C.Play: return "遊び";
                default: return c.ToString();
            }
        }

        // ===== 一覧 (この並びが並び順 order) =====

        private static int _order;
        private static AchievementDefinition D(string id, C cat, int stars, string name, string desc, bool hidden = false, int endingNo = 0)
            => new AchievementDefinition(id, name, desc, cat, _order++, stars, hidden, endingNo);

        private const bool H = true;   // 隠し

        public static readonly IReadOnlyList<AchievementDefinition> All = new List<AchievementDefinition>
        {
            // ── 進行 ──
            D(FirstRun, C.Progress, 1, "最初の一歩", "初めてランを始める"),
            D(Boss1, C.Progress, 1, "最初の壁", "1層のボスを倒す"),
            D(Boss3, C.Progress, 2, "三つ目の顔", "2層のボスを倒す"),
            D(CombatWins100, C.Progress, 2, "百戦錬磨", $"戦闘に通算{CombatWinsGoal}回勝つ"),
            D(Boss3All, C.Progress, 3, "三つの玉座", "2層の3種のボスを、すべて倒す（通算）"),

            // ── 結末 (END1 の凱旋級だけ隠さない・作者 2026-11-17) ──
            D(Conviction, C.Ending, 3, "根拠は要らない", "預言者の石板から始まる三つの出来事をたどる", H),
            D(Boss5, C.Ending, 2, "凱旋級", "3層で審判官を倒して、地上へ帰る", endingNo: 1),
            D(HiddenSaintGeorges, C.Ending, 3, "剣との対話", "結末「無明の剣」を見る（3層で、シュヴァリエ・サン=ジョリオラを倒す）", H, 2),
            D(Reach6, C.Ending, 3, "記録に残らない深さ", "4層へ降りる", H),
            D(AshKing, C.Ending, 4, "灰の上の玉座", "結末「灰の玉座」を見る（灰燼の王を倒す）", H, 3),
            D(HiddenNoBlazeBoss6, C.Ending, 5, "灰は灰へ", "烈炎を一度も積ませずに、灰燼の王を倒す", H),
            D(Reach7, C.Ending, 4, "裂け目の縁", "5層へ降りる", H),
            D(GateFlawless, C.Ending, 4, "欠けのない起動", "〈門〉の三つの工程を、どれも欠かさずに済ませる", H),
            D(Clear7, C.Ending, 5, "掘り止め", "ヴェスカを倒す", H),
            D(EndGood, C.Ending, 5, "孫の孫より遠い", "結末「帰路」を見る（裂け目を閉じずに帰る）", H, 4),
            D(EndTrue, C.Ending, 5, "閉じる者", "結末「終始」を見る（裂け目を閉じる）", H, 5),
            D(VescaClock, C.Ending, 5, "止まった針", "博士から預かった時計を持って、ヴェスカを倒す", H),

            // ── 挑戦カード ──
            D(ChallengeD3, C.Challenge, 3, "腕試し", $"難易度{DifficultyLow}以上で、3層のボスを倒す"),
            D(ChallengeD6, C.Challenge, 4, "向かい風", $"難易度{DifficultyMid}以上で、3層のボスを倒す"),
            D(ChallengeD10, C.Challenge, 5, "底の底", $"難易度{ChallengeCards.MaxDifficulty}で、3層のボスを倒す"),
            // 注意: 大罪は IV の札で、 IV は 5 層の入口でしか配られない ── 今の配り方では 3 層のボスの時に背負っていない (docs/GAME.md §23-31・作者の判断待ち)
            D(SinsAll, C.Challenge, 5, "七つの重荷", "「大罪:○○」の札を7種それぞれ背負って、ヴェスカを倒す（通算）", H),   // 2026-11-17 作者の決定: 3 層のボス → ヴェスカ・隠し (大罪は 5 層の入口でしか配られない)
            D(BuffsAll, C.Challenge, 3, "品揃え", "挑戦カードのバフを、全種類受け取る（通算）"),
            D(ChallengeKingD10, C.Challenge, 5, "燃え残り", $"難易度{ChallengeCards.MaxDifficulty}で、灰燼の王を倒す", H),   // 名前は仮 (作者 2026-11-17 の決定待ち)
            D(ChallengeVescaD10, C.Challenge, 5, "底を割る", $"難易度{ChallengeCards.MaxDifficulty}で、ヴェスカを倒す", H),   // 名前は仮 (作者 2026-11-17 の決定待ち)

            // ── 戦闘の腕前 ──
            D(WinAt1Hp, C.CombatSkill, 2, "紙一重", "HP1で戦闘に勝つ"),
            D(BossNoHpDamage, C.CombatSkill, 4, "傷一つなく", "2層以降のボスを、HPを1も減らさずに倒す"),
            D(BossOneTurn, C.CombatSkill, 5, "一擲", "2層以降のボスを、1ターンで倒す"),
            D(DownCancel, C.CombatSkill, 2, "出鼻をくじく", "ボスのダウンで、大技を取り消す"),
            D(DownDouble, C.CombatSkill, 3, "二枚抜き", "1回の戦闘で、ボスの混乱を2回満たす"),
            D(Escalation4, C.CombatSkill, 3, "長丁場", "敵の攻撃の激化が4段階（最大）まで上がった戦闘に勝つ"),
            D(BerserkWin, C.CombatSkill, 4, "根比べ", "狂暴化したボスに勝つ"),
            D(Crit100, C.CombatSkill, 3, "芯を食う", $"会心の一撃で、{CritGoal}以上のダメージを与える"),
            D(PierceZero, C.CombatSkill, 2, "鎧通し", "敵の防御を貫通で0にして攻撃する"),
            D(Elite20, C.CombatSkill, 3, "精鋭狩り", $"エリートを通算{EliteGoal}体倒す"),

            // ── キーワード ──
            D(Poison99, C.Keyword, 3, "致死量", $"敵の毒を{PoisonGoal}まで重ねる"),
            D(BleedBig, C.Keyword, 3, "血の川", $"1戦闘で、敵の出血を{BleedGoal}まで積む"),
            D(Charge30, C.Keyword, 2, "満ちる", $"充電を{ChargeGoal}以上ためる"),
            D(Burst5, C.Keyword, 3, "連鎖反応", $"1戦闘で、臨界の爆発を{BurstGoal}回起こす"),

            // ── 出目と役 ──
            D(FirstRole, C.DiceRole, 1, "型を知る", "初めて役を発動する"),
            D(AllRoles, C.DiceRole, 5, "型を極める", "賽で出せる役を、すべて発動する（通算）"),
            D(RoleZoro, C.DiceRole, 2, "三つ揃い", "ゾロ目の役（「・揃」の役）を発動する"),
            D(RoleTop, C.DiceRole, 3, "奥の手", "LEGENDARY の賽で、役を発動する"),

            // ── 端子 ──
            D(DeckMin, C.Terminal, 3, "身軽", $"山札を、これ以上抜けない枚数（{CombatSystem.TerminalCards.MinDeckSize}枚）まで減らして、戦闘に勝つ"),
            D(Deck30, C.Terminal, 2, "紙の城", $"山札を{DeckBigGoal}枚以上にする"),
            // 出目 4 個の時 (時限バフ〈使命感〉など) だけ成り立つ。 作者は承知でこのまま (2026-11-17)
            D(Terminal5Types, C.Terminal, 4, "総動員", "1ターンに、攻撃・防御・充電・補助のカードをすべて挿す"),

            // ── 職業と武器 ──
            D(ClassSword, C.ClassWeapon, 2, "抜き身", "剣士で、3層のボスを倒す"),
            D(ClassKnight, C.ClassWeapon, 2, "盾の向こう", "騎士で、3層のボスを倒す"),
            D(ClassBerserker, C.ClassWeapon, 2, "血が沸く", "狂戦士で、3層のボスを倒す"),
            D(ClassAssassin, C.ClassWeapon, 2, "足音なし", "暗殺者で、3層のボスを倒す"),
            D(WeaponTop, C.ClassWeapon, 2, "鍛え上げ", "武器を最後の段まで鍛える"),
            D(WeaponAllTop, C.ClassWeapon, 4, "四つの得物", "剣・斧・短剣・盾のすべてを、最後の段まで鍛える（通算）"),

            // ── 航行 ──
            D(PursuerWin, C.Navigation, 2, "狩る側", "追跡者を倒す"),
            D(PursuerUnseen, C.Navigation, 3, "忍び足", "縄張りの宝を取り、一度も見つからずにボスまで抜ける"),      // 2026-11-24 仮 (縄張りは必ず避けられる + 隣で見つかるだけでは緩いので宝を条件に・作者の承認待ち)
            D(FamiliarWin, C.Navigation, 1, "猟犬払い", "使い魔を倒す"),
            D(LureHit, C.Navigation, 2, "罠師", "囮罠で、追跡者を足止めする"),
            D(StormBoss, C.Navigation, 4, "嵐の目", "嵐が最後の段まで進んだ層で、ボスを倒す"),
            D(ProvisionZeroBoss, C.Navigation, 3, "荷は空", "物資が0のまま、ボスを倒す"),

            // ── 店と経済 ──
            D(ZeroGoldBoss6, C.ShopEconomy, 3, "空の財布", "所持金0で、3層のボスを倒す"),
            D(RobberyWin, C.ShopEconomy, 2, "値切りの腕", "値下げ交渉に勝つ"),
            D(Gold1000, C.ShopEconomy, 3, "懐が重い", $"所持金を{GoldGoal}以上にする"),
            D(HiddenShopStubborn, C.ShopEconomy, 2, "しつこい交渉", $"1つの店で、買う・売る・品を振り直すを{StubbornCount}回ずつ行う", H),
            D(HiddenExactZeroPurchase, C.ShopEconomy, 2, "買い物上手", "買い物で、所持金をちょうど0にする", H),
            D(RobberyRebuy, C.ShopEconomy, 2, "面の皮", "値下げ交渉の後、値段が倍の店で買い物をする", H),

            // ── 縛り ──
            D(NoConsumableBoss5, C.Restriction, 3, "節約家", "消耗品を使わずに、3層のボスを倒す"),
            D(NoFleeBoss5, C.Restriction, 2, "猪突猛進", "一度も逃げずに、3層のボスを倒す"),
            D(NoPurchaseBoss6, C.Restriction, 3, "清貧", "店で品を何も買わずに、3層のボスを倒す"),
            D(NoRerollBoss6, C.Restriction, 3, "運命を受け入れるもの", "戦闘でも店でも振り直さずに、3層のボスを倒す"),
            D(NoRelicConsumableClear7, C.Restriction, 4, "持たざる者", "整備盤にパネルを置かず、消耗品も使わずに、3層のボスを倒す"),

            // ── 遺物と整備パネル ──
            D(BoardOpenCenter, C.RelicPanel, 1, "火入れ", "整備盤の中心のマスを開く"),
            D(BoardFirstPanel, C.RelicPanel, 1, "一枚目", "整備盤にパネルを初めて置く"),
            D(BoardCross, C.RelicPanel, 2, "十字", "整備盤の中心と上下左右の5マスを開く"),
            D(BoardAllOpen, C.RelicPanel, 3, "全開放", $"整備盤の{MetaBoard.Cells}マスをすべて開く"),
            D(BoardCoreClear, C.RelicPanel, 2, "主柱", "4マスのパネルを整備盤に置いて、3層のボスを倒す"),
            D(BoardFullClear, C.RelicPanel, 3, "隙間なし", $"整備盤の{MetaBoard.Cells}マスをすべてパネルで埋めて、3層のボスを倒す"),
            D(BoardShopAll, C.RelicPanel, 4, "棚の全部", "トークンで買えるパネルを、すべて持つ"),

            // ── 記録とイベント ──
            D(Events50, C.RecordEvent, 2, "寄り道の常連", $"イベントを通算{EventsGoal}回終える"),
            D(EventsAll, C.RecordEvent, 4, "旅の目録", "出会えるイベントに、すべて出会う"),
            D(Observatory, C.RecordEvent, 3, "星を数える人", "さびれた観測所で、博士に会う", H),
            D(CodexHalf, C.RecordEvent, 3, "書き留める癖", "記録の品と本を、それぞれ半分埋める"),
            D(CodexFull, C.RecordEvent, 5, "目録の完成", "記録の品と本を、すべて埋める"),

            // ── 遊び (全部隠し) ──
            D(HiddenOverkill, C.Play, 4, "やりすぎ", $"残りHP1の敵に、一撃で{OverkillGoal}以上を与えてとどめを刺す", H),
            D(HiddenThirteenthEnd, C.Play, 4, "十三番目の終わり", $"ラン通算{ThirteenTurn}ターン目（0層は数えない）に、主攻撃と固定ダメージでちょうど{ThirteenDamage}を与えて倒す", H),
            D(HiddenTheseus, C.Play, 4, "テセウスの船", $"開始時の最大HPの{TheseusTimes}倍を超えるダメージを受けて、ランをクリアする", H),
            D(HiddenCalmDown, C.Play, 4, "落ち着けよ、兄弟", $"回復せずに、HP1のまま{CalmDownTurns}ターン耐えて勝つ", H),
            D(HiddenPerpetualMotion, C.Play, 4, "ノーベル賞は私のモノ", $"1戦闘で、最大HPの{PerpetualHealTimes}倍以上を回復して勝つ（あふれた分も数える）", H),
            // id は旧〈最後の審判〉の刻限 124 の名残 (条件は作り直した・2026-11-17)。 id は保存に使うので据え置き
            D(HiddenTurn124, C.Play, 4, "あと一ターンだけ", "〈刻限〉の札を背負って、段が上がる直前のターンにボスを倒す", H),
            D(HiddenReturningEnemy, C.Play, 3, "昨日の敵は今日も敵", "逃げた相手と同じ敵に、同じランのうちに勝つ", H),
            D(CrateSlow, C.Play, 2, "鍵を探している", $"0層の古い宝箱を、{CrateSlowTurns}ターン以上かけて開ける", H),
            D(BossOnlyFloor, C.Play, 3, "素通り", "2層以降の1つの層で、ボスのほかに一度も戦わずに抜ける", H),
            D(PairOneTurn, C.Play, 3, "一網打尽", "2体の敵を、1ターンでまとめて倒す", H),
        };

        private static readonly Dictionary<string, AchievementDefinition> ById = BuildIndex();

        private static Dictionary<string, AchievementDefinition> BuildIndex()
        {
            var d = new Dictionary<string, AchievementDefinition>();
            for (int i = 0; i < All.Count; i++) d[All[i].id] = All[i];
            return d;
        }

        /// <summary>id → 定義。 無ければ null。</summary>
        public static AchievementDefinition Get(string id) { ById.TryGetValue(id ?? "", out var d); return d; }

        /// <summary>系統の実績 (並び順)。</summary>
        public static List<AchievementDefinition> InCategory(AchievementCategory c)
        {
            var l = new List<AchievementDefinition>();
            for (int i = 0; i < All.Count; i++) if (All[i].category == c) l.Add(All[i]);
            return l;
        }

        /// <summary>結末 END<paramref name="n"/> の実績 (無ければ null)。</summary>
        public static AchievementDefinition ForEnding(int n)
        {
            if (n <= 0) return null;
            for (int i = 0; i < All.Count; i++) if (All[i].endingNo == n) return All[i];
            return null;
        }
    }
}

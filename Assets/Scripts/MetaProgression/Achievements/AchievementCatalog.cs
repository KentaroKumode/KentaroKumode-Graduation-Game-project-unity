using System.Collections.Generic;

namespace MetaProgression.Achievements
{
    /// <summary>Stable achievement IDs. Never rename an ID after release.</summary>
    public static class AchievementCatalog
    {
        public const string FirstRun = "first_run", Boss1 = "boss_1", Reach3 = "reach_3";
        public const string Boss5 = "boss_5", Reach6 = "reach_6", Reach7 = "reach_7", Clear7 = "clear_7";
        public const string CombatWins100 = "combat_wins_100", FirstRole = "first_role", AllRoles = "all_roles";
        public const string WinAt1Hp = "win_at_1_hp", BossNoHpDamage = "boss_no_hp_damage", BossOneTurn = "boss_one_turn";
        public const string Challenge10 = "challenge_10", Challenge20 = "challenge_20", Challenge30 = "challenge_30";
        public const string Challenge40 = "challenge_40", Challenge50 = "challenge_50";
        public const string CategoryAMax = "category_a_max", CategoryBMax = "category_b_max", CategoryCMax = "category_c_max";
        public const string CategoryDMax = "category_d_max", CategoryEMax = "category_e_max";
        public const string NoConsumableBoss5 = "no_consumable_boss_5", NoPurchaseBoss6 = "no_purchase_boss_6";
        public const string NoRerollBoss6 = "no_reroll_boss_6", NoRelicConsumableClear7 = "no_relic_consumable_clear_7";
        public const string NoFleeBoss5 = "no_flee_boss_5";
        public const string FirstRelic = "first_relic", RelicMainMax = "relic_main_max", RelicSubMax = "relic_sub_max";
        public const string RelicAllMax = "relic_all_max", ZeroGoldBoss6 = "zero_gold_boss_6", CursedRelicClear7 = "cursed_relic_clear_7";
        public const string HiddenSaintGeorges = "hidden_saint_georges", HiddenFinalPage = "hidden_final_page";
        public const string HiddenNoBlazeBoss6 = "hidden_no_blaze_boss_6", HiddenThirteenthEnd = "hidden_thirteenth_end";
        public const string HiddenTheseus = "hidden_theseus", HiddenOverkill = "hidden_overkill", HiddenCalmDown = "hidden_calm_down";
        public const string HiddenPerpetualMotion = "hidden_perpetual_motion", HiddenTurn124 = "hidden_turn_124";
        public const string HiddenShopStubborn = "hidden_shop_stubborn", HiddenExactZeroPurchase = "hidden_exact_zero_purchase";
        public const string HiddenReturningEnemy = "hidden_returning_enemy";

        public static readonly IReadOnlyList<AchievementDefinition> All = new List<AchievementDefinition>
        {
            D(FirstRun,"最初の一歩","初めてランを開始する"), D(Boss1,"第一の門","1層ボスを撃破する"),
            D(Reach3,"地上は遠く","3層へ到達する"), D(Boss5,"災厄の先へ","5層ボスを撃破する"),
            D(Reach6,"〈決意〉","6層へ突入する"), D(Reach7,"〈真理〉","7層へ突入する"), D(Clear7,"覚者","7層をクリアする"),
            D(CombatWins100,"百戦錬磨","累計100回、戦闘に勝利する"), D(FirstRole,"型を知る","初めて役を発動する"),
            D(AllRoles,"型を極める","全種類の役を累計で発動する"), D(WinAt1Hp,"紙一重","HP1で戦闘に勝利する"),
            D(BossNoHpDamage,"傷一つなく","HPダメージを受けずにボスを撃破する"), D(BossOneTurn,"一擲","3層以降のボスを1ターンで撃破する"),
            D(Challenge10,"CLASS: CAUTION // COMPLETE","挑戦ポイント10以上で7層をクリアする"),
            D(Challenge20,"CLASS: DANGER // COMPLETE","挑戦ポイント20以上で7層をクリアする"),
            D(Challenge30,"CLASS: CRITICAL-SITUATION // COMPLETE","挑戦ポイント30以上で7層をクリアする"),
            D(Challenge40,"CLASS: FATAL-ERROR // COMPLETE","挑戦ポイント40以上で7層をクリアする"),
            D(Challenge50,"CLASS: CATASTROPHIC-ERROR // COMPLETE","全挑戦有効の50ポイントで7層をクリアする"),
            D(CategoryAMax,"成立","生存圧MAXと破綻だけで7層をクリアする"), D(CategoryBMax,"鋼断ち","敵強化MAXと鋼の皮膚だけで7層をクリアする"),
            D(CategoryCMax,"運命への反証","戦闘妨害MAXと凶運だけで7層をクリアする"), D(CategoryDMax,"金貨なんて必要ない","経済MAXと破産だけで7層をクリアする"),
            D(CategoryEMax,"上告","崩壊MAXと最後の審判だけで7層をクリアする"),
            D(NoConsumableBoss5,"節約家","消耗品を使わず5層ボスを撃破する"), D(NoPurchaseBoss6,"清貧","購入せず6層ボスを撃破する"),
            D(NoRerollBoss6,"運命を受け入れるもの","再抽選せず6層ボスを撃破する"), D(NoRelicConsumableClear7,"持たざる者","遺物を装備せず消耗品も使わず7層をクリアする"),
            D(NoFleeBoss5,"猪突猛進","逃亡せず5層ボスを撃破する"),
            D(FirstRelic,"小さなお守り","初めて遺物を入手する"), D(RelicMainMax,"主役の器","メインステータス最大の遺物を入手する"),
            D(RelicSubMax,"極致の一片","サブステータスのいずれかが最大の遺物を入手する"), D(RelicAllMax,"完全無欠","全ステータス最高の遺物を入手する"),
            D(ZeroGoldBoss6,"空の財布、満ちた決意","所持金0で6層ボスを撃破する"), D(CursedRelicClear7,"呪われた至宝","呪われた遺物を装備して7層をクリアする"),
            D(HiddenSaintGeorges,"剣との対話","5層隠しボスを撃破する",true), D(HiddenFinalPage,"最後の頁","最後の頁を開く",true),
            D(HiddenNoBlazeBoss6,"灰は灰へ","烈炎を増加させず6層ボスを撃破する",true), D(HiddenThirteenthEnd,"十三番目の終わり","ラン累積13戦闘ターン目にちょうど13ダメージで敵を撃破する",true),
            D(HiddenTheseus,"テセウスの船","開始時最大HPの1000%を超える累積ダメージを受けて生還する",true),
            D(HiddenOverkill,"やりすぎ","残りHP1の敵へ一撃100以上でとどめを刺す",true), D(HiddenCalmDown,"落ち着けよ、兄弟","HP1のまま3ターン生存して勝利する",true),
            D(HiddenPerpetualMotion,"ノーベル賞は私のモノ","1戦闘で最大HPの300%以上を累積回復して勝利する",true),
            D(HiddenTurn124,"あと一ターンだけ","累積124ターン目にボスを撃破する",true), D(HiddenShopStubborn,"しつこい交渉","1店で購入・売却・再抽選を各3回行う",true),
            D(HiddenExactZeroPurchase,"買い物上手","購入で所持金をちょうど0にする",true), D(HiddenReturningEnemy,"昨日の敵は今日も敵","逃亡した敵と同種の敵へ再遭遇して撃破する",true),
        };

        private static readonly Dictionary<string, AchievementDefinition> ById = BuildIndex();
        private static AchievementDefinition D(string id, string name, string desc, bool hidden = false) => new AchievementDefinition(id, name, desc, hidden);
        private static Dictionary<string, AchievementDefinition> BuildIndex()
        {
            var d = new Dictionary<string, AchievementDefinition>();
            for (int i = 0; i < All.Count; i++) d[All[i].id] = All[i];
            return d;
        }
        public static AchievementDefinition Get(string id) { ById.TryGetValue(id ?? "", out var d); return d; }
    }
}

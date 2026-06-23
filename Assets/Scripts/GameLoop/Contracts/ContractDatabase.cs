using System.Collections.Generic;

namespace GameLoop.Contracts
{
    /// <summary>
    /// 全 12 旅団の ContractDefinition を保持する中央レジストリ。
    /// 現状は C# ハードコード (台詞は docs/specs/contracts.md と同期)。
    /// 将来 Assets/Data/Contracts/contracts.json 化する場合はここを読み込みに差し替える。
    /// </summary>
    public static class ContractDatabase
    {
        private static readonly Dictionary<ContractKind, ContractDefinition> _defs = BuildDefs();

        public static ContractDefinition Get(ContractKind kind) => _defs[kind];

        public static IEnumerable<ContractDefinition> All() => _defs.Values;

        private static Dictionary<ContractKind, ContractDefinition> BuildDefs()
        {
            var d = new Dictionary<ContractKind, ContractDefinition>();
            void Add(ContractDefinition def) => d[def.kind] = def;

            Add(new ContractDefinition
            {
                kind = ContractKind.Mercenaries,
                displayName = "傭兵団",
                axisLabel = "DoT",
                flavorIntro = "戦は商売だ。 ターンが回るたび、 釣りは出さない。",
                effectByLevel = new[]
                {
                    "ターン終了時、 敵に最大HPの4%軽減不能ダメ (上限50)",
                    "ターン終了時、 敵に最大HPの8%軽減不能ダメ (上限50)",
                    "ターン終了時、 敵に最大HPの12%軽減不能ダメ (上限50)",
                },
                rivalryQuote = "だから! お前らは机上の空論なんだよ! 現場で何人死んだ!",
            });

            Add(new ContractDefinition
            {
                kind = ContractKind.SupplyCaravan,
                displayName = "補給キャラバン",
                axisLabel = "システム解放",
                flavorIntro = "拠点は分け合うものだ。 鍋も、 火も、 不寝番の交代も。",
                effectByLevel = new[]
                {
                    "層に1回ショップを任意で開ける",
                    "層に1回ずつショップと強化を任意で使える",
                    "層に1回ずつショップ・強化・休息を使える (休息はマップ画面のみ)",
                },
                rivalryQuote = "お前が浪費する医療物資のせいでみんな餓死するぞ!",
            });

            Add(new ContractDefinition
            {
                kind = ContractKind.MerchantsLeague,
                displayName = "商業連合隊",
                axisLabel = "リスク連動収入",
                flavorIntro = "商人は血の臭いには近寄らぬ。 帳簿に飛沫がかかる。",
                effectByLevel = new[]
                {
                    "層終了時 +5G。 ただしその層内戦闘でHP50%↓になると収入0",
                    "層終了時 +10G。 ただしその層内戦闘でHP50%↓になると収入0",
                    "層終了時 +20G (HP50%↓ペナルティ免除)",
                },
                rivalryQuote = "石を金塊に!? 商売あがったりだ!",
            });

            Add(new ContractDefinition
            {
                kind = ContractKind.Missionaries,
                displayName = "宣教師",
                axisLabel = "希望保護",
                flavorIntro = "主が見ておられる、 と言うだけで、 死にたい者が一日延びる。",
                effectByLevel = new[]
                {
                    "戦闘以外の希望減少を -1 (0にはならない)",
                    "戦闘以外の希望減少を -2 (0にはならない)",
                    "戦闘以外の希望減少を -3 (0にはならない)",
                },
                rivalryQuote = "殺人を教義とする宗教など! 主はお認めになっておらん!",
            });

            Add(new ContractDefinition
            {
                kind = ContractKind.Knights,
                displayName = "騎士",
                axisLabel = "防御",
                flavorIntro = "盾の意味を知らぬ者には見えぬ景色がある。",
                effectByLevel = new[]
                {
                    "受けるダメージ -1 (最低1通す)",
                    "受けるダメージ -2 (最低1通す)",
                    "受けるダメージ -3 (最低1通す)",
                },
                rivalryQuote = "悪党の身代わりをして何のつもりだ?",
            });

            Add(new ContractDefinition
            {
                kind = ContractKind.Assassins,
                displayName = "暗殺教団",
                axisLabel = "雑魚処理",
                flavorIntro = "彼らに名乗らせる暇があれば、 もう死んでいる。",
                effectByLevel = new[]
                {
                    "通常戦闘の敵に開始時、 HPの33%軽減不能ダメ",
                    "通常戦闘の敵に開始時、 HPの66%軽減不能ダメ",
                    "通常戦闘の敵に開始時、 HPの99%軽減不能ダメ",
                },
                rivalryQuote = "お前らの主とやらはどうして我々に罰を与えないのだ?",
            });

            Add(new ContractDefinition
            {
                kind = ContractKind.Alchemist,
                displayName = "旅する錬金術師",
                axisLabel = "ドロップ",
                flavorIntro = "鉛は金になる、 やり方さえ間違わなければ。 やり方は、 間違える。",
                effectByLevel = new[]
                {
                    "戦闘終了時10%でパッシブ錬金 (BRONZE)",
                    "戦闘終了時20%でパッシブ錬金 (BRONZE/SILVER)",
                    "戦闘終了時30%でパッシブ錬金 (BRONZE/SILVER/GOLD)",
                },
                rivalryQuote = "いちいち煩いなぁ...",
            });

            Add(new ContractDefinition
            {
                kind = ContractKind.WanderingDoctor,
                displayName = "放浪医術官",
                axisLabel = "回復",
                flavorIntro = "効能書きより、 ともかく止血だ。 効能はあとで読め。",
                effectByLevel = new[]
                {
                    "戦闘終了時、 減少HPの10%を回復",
                    "戦闘終了時、 減少HPの20%を回復",
                    "戦闘終了時、 減少HPの30%を回復",
                },
                rivalryQuote = "足が壊死して苦しみながら死ぬのとどっちがマシでしょうね?",
            });

            Add(new ContractDefinition
            {
                kind = ContractKind.OrphanCircus,
                displayName = "捨て子のサーカス団",
                axisLabel = "ロアwindfall",
                flavorIntro = "曲芸の練習音と、 飯の煮える音。 同じ位の重さ。",
                effectByLevel = new[]
                {
                    "効果なし。 引渡しイベントで報酬 (小)",
                    "効果なし。 引渡しイベントで報酬 (中)",
                    "効果なし。 引渡しイベントで報酬 (大)",
                },
                rivalryQuote = "なんだアイツら? 邪険にしやがって",
            });

            Add(new ContractDefinition
            {
                kind = ContractKind.BodyDoubles,
                displayName = "影武者一座",
                axisLabel = "緊急保険",
                flavorIntro = "死にすぎた男たちが、 もう一度死に方を売り歩いている。",
                effectByLevel = new[]
                {
                    "HP0時、 maxHP×10%で復活 (ラン全体で1回)",
                    "HP0時、 maxHP×10%で復活 (ラン全体で2回)",
                    "HP0時、 maxHP×10%で復活 (ラン全体で3回)",
                },
                rivalryQuote = "命賭けの仕事中に騎士様の説教なんざ御免だね",
            });

            Add(new ContractDefinition
            {
                kind = ContractKind.Hunters,
                displayName = "狩猟旅団",
                axisLabel = "状態異常",
                flavorIntro = "獣を屠るのも人を屠るのも、 刃の角度は同じだ。",
                effectByLevel = new[]
                {
                    "戦闘開始時敵に脆弱付与。 会心ダメに×1.15倍率",
                    "戦闘開始時敵に脆弱付与。 会心ダメに×1.30倍率",
                    "戦闘開始時敵に脆弱付与。 会心ダメに×1.45倍率",
                },
                rivalryQuote = "子供は煩い、 臭いで獣が逃げるだろう",
            });

            Add(new ContractDefinition
            {
                kind = ContractKind.Tacticians,
                displayName = "戦術家",
                axisLabel = "ロール救済",
                flavorIntro = "最初の手は読まれている、 二手目は読ませない。",
                effectByLevel = new[]
                {
                    "戦闘中、 自分のロールを1回振り直し",
                    "戦闘中、 自分のロールを2回振り直し",
                    "戦闘中、 自分のロールを3回振り直し",
                },
                rivalryQuote = "戦術通りにミスなく動けば誰も死なない!",
            });

            return d;
        }
    }
}

using System.Collections.Generic;

namespace AutoTest
{
    /// <summary>
    /// ラン開始時に BOT が採用するビルド軸ペルソナ (2026-07-15 追加)。
    /// 従来の Tier 表信奉 (RawTier) に加えて、特定キーワードに寄せた行動を取らせることで
    /// 「ビルド軸としての成立チェック」と「ペルソナ別勝率計測」を可能にする。
    /// AutoRunner が RunOne 冒頭で確率抽選し、以下に影響を与える:
    ///   1. アイテムピック優先度 (BuildPersonaProfiles.ScoreBonus で加点)
    ///   2. MutualWiringPolicy の端子重み (充電/シールドバッシュ等)
    ///   3. サマリ集計 (persona 別勝率)
    /// </summary>
    public enum BuildPersona
    {
        RawTier,   // 従来型: Tier 表そのまま (ベースライン計測用)
        Standard,  // 通常火力: Might/Pursuit 中心 (会心/DoTなし)
        Crit,      // 会心特化
        Bleed,     // 出血
        Rinkai,    // 臨界 (メーター蓄積→爆発型・2026-07-16 Burn を廃止して新軸に置換)
        Poison,    // 毒 (拘束/妨害)
        Bludgeon,  // 鈍器 (非会心強化・会心排他)
        Charge,    // 充電経済 (要 mutual pipeline)
        Shield,    // シールドタンク
        Berserk,   // 窮地バースト (HP低下トリガー)
    }

    /// <summary>ビルド軸ペルソナの選好パッシブ ID セット + スコアボーナス計算。</summary>
    public static class BuildPersonaProfiles
    {
        // 各ペルソナの選好パッシブ ID (SkillId ベース)。
        // アイテムの passiveSkills[*].internalName とマッチしたら該当ペルソナに +Bonus を加算。
        // Score(id) は 0〜5 帯なので +2〜+3 の加点で「一段上げ」相当のバイアス。
        private static readonly Dictionary<BuildPersona, HashSet<string>> Preferred =
            new Dictionary<BuildPersona, HashSet<string>>
            {
                // Standard は 2026-07-16 改定: 「特定パッシブに寄せない・ひたすら Tier 高アイテムを取る」軸に転換。
                // 選好パッシブは持たず、 ScoreBonusForItem 特別分岐 (S/A ランクの学習結果を大きく反映) で表現。
                [BuildPersona.Standard] = new HashSet<string>(),
                [BuildPersona.Crit] = new HashSet<string>
                {
                    "十五年目の計測器", "測量師の片眼鏡", "InsightIII", "InsightIV",
                    // TwinDice / SinglePoint / LifeFang は 2026-08-09 削除 (items.json に無い死にID)
                    // "ApexCrit" は 2026-09-05 削除 (六面天頂儀ごと撤去・下記)
                    "連環の指輪",
                    "医家の反り刃", "鏡返しの小盾", "鎧縫いの針",
                    "血日に澄む佩玉", "素手禁じの蛇血瓶", // FlameEdge/Kindling は Burn 全廃 (2026-07-18) で削除
                },
                [BuildPersona.Bleed] = new HashSet<string>
                {
                    "Sting", "医家の反り刃",   // BloodPathBanner は 2026-08-09 削除 (死にID)
                    "血日に澄む佩玉", "手負い追いの山刀", "逆綴じの止血帯", "末頁の血花太刀",
                    "医書裏の開き針", "先血の腕輪",
                },
                // 臨界 (Rinkai) 2026-07-16 追加: メーター蓄積型 (attackSum を積み、閾値到達で爆発)
                // 選好は Rinkai キーワードパッシブ 9種 (Ignition/Conduction/Radiation/CriticalPressure/
                // ChainCombustion/Afterglow/LowerThreshold/NoCooldown/UnyieldingHeat)
                [BuildPersona.Rinkai] = new HashSet<string>
                {
                    "余分に乾いた火口箱", "炉番の火床外套", "過熱石",
                    "溢れを取る鋳型", "帳簿外の連鎖爆発", "朝にも熱い竈",
                    "気短な早沸かし釜", "恒熱の炉壁", "七日目の熾",
                },
                [BuildPersona.Poison] = new HashSet<string>
                {
                    "緑染みの下拵え小刀", "CorrosiveStrike", "素手禁じの蛇血瓶", "主より長い香炉",
                    "石抜きの毒指輪", "跡地庭師の霧吹き",
                    "岸上げ用の麻痺瓶", "抜かずの控え脇差", "SlowVenomCurse",
                    // CurseBind は curse 系列 (2026-07-17 削除) 依存で参照消失、除去。
                },
                [BuildPersona.Bludgeon] = new HashSet<string>
                {
                    "鉛入りの握斧", "一人抱えの破城槌",
                    "研ぎ知らずの銑鉄棍", "読めずの無心刃",
                    "千日振りの鉢巻", "重さを増す拳套", "直さずの鉢金",
                    "半歩深めの力帯", "MightII", "MightIII", "MightIV", // 基礎火力の土台
                    "PursuitI", "PursuitII", "PursuitIII", "烈刃",
                },
                [BuildPersona.Charge] = new HashSet<string>
                {
                    "工廠の材料箱", "無限モーター", "呼雷粉", "Thundercloud",
                    "焦げ柄の点火スパナ", "逆さ避雷針",
                    "過負荷チューナー", "三度不良の銅線", "雷壺",
                },
                [BuildPersona.Shield] = new HashSet<string>
                {
                    "二拍目の心臓",
                    "IndomitableI", "IndomitableII", "敗残兵の部隊章", "IndomitableIV",
                    "ShieldBashI", "ShieldBashII", "ShieldBashIII", "内から落ちた城盾",
                    "鏡返しの小盾", "衛士の慣い", "パリィ",
                    "戻り数なき革胸当て", "FortitudeII", "FortitudeIII", "FortitudeIV",
                },
                [BuildPersona.Berserk] = new HashSet<string>
                {
                    "退路喰いの狂刃", "衛士の慣い",
                    "直さずの鉢金", "二拍目の心臓", "復讐",
                    "MightIV", "烈刃", "一人抱えの破城槌", // 火力積み合わせ
                    // Abyss は curse_t4 (2026-07-17 削除) 依存で参照消失、除去。
                },
            };

        /// <summary>アイテムに含まれる passive の SkillId 群を渡すと、persona に対する加点を返す。
        /// マッチしたパッシブ 1 種で最大加点 (2 種以上マッチしても飽和)。
        /// UniversalBoost (Might/Pursuit) は全ペルソナ (RawTier 除く) 共通で加点。</summary>
        public static int ScoreBonus(BuildPersona persona, System.Collections.Generic.IEnumerable<string> skillIds)
        {
            if (persona == BuildPersona.RawTier || skillIds == null) return 0;
            Preferred.TryGetValue(persona, out var set);
            Keystone.TryGetValue(persona, out var keystone);
            int best = 0;
            foreach (var id in skillIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (keystone != null && keystone.Contains(id)) return KeystoneBonus; // 即返し (最大加点)
                if (UniversalBoost.Contains(id)) best = System.Math.Max(best, PersonaMatchBonus);
                else if (set != null && set.Contains(id)) best = System.Math.Max(best, PersonaMatchBonus);
            }
            return best;
        }

        /// <summary>1 種でも選好パッシブが含まれる場合の加点。Score 帯 (0〜5) に対して +2 = 1段上げ相当。</summary>
        public const int PersonaMatchBonus = 2;

        /// <summary>キーストーン加点 (2026-07-16 追加)。 ビルドの根幹をなす「これがないと成立しない」パッシブに与える強加点。
        /// 通常の PersonaMatchBonus (+2) より強く +4、 見つかった時点で即返しして他の選好を上書き。</summary>
        public const int KeystoneBonus = 4;

        /// <summary>ペルソナ毎のキーストーン (存在すれば必ず引く)。 選好より優先される最上位加点。
        /// Shield: ShieldBash 系 (シールドを攻撃力に変換する唯一のペイオフ)</summary>
        private static readonly Dictionary<BuildPersona, HashSet<string>> Keystone =
            new Dictionary<BuildPersona, HashSet<string>>
            {
                [BuildPersona.Shield] = new HashSet<string>
                {
                    "ShieldBashI", "ShieldBashII", "ShieldBashIII", "内から落ちた城盾",
                    "捨盾", // 2026-07-16: 撃破局面のフィニッシャー
                },
            };

        /// <summary>全ビルド共通の基礎火力ブースト対象 (2026-07-16 追加)。
        /// Might/Pursuit は DoT/会心/鈍器すべての攻撃系ビルドの土台になるが、 選好パッシブ +2 加点で
        /// 同Tier帯で常に競り負けるため実質敬遠されていた。 全ペルソナ (RawTier 除く) に一律 +Bonus。
        /// Shield/Charge も攻撃力不足解消に寄与。</summary>
        private static readonly HashSet<string> UniversalBoost = new HashSet<string>
        {
            "半歩深めの力帯", "MightII", "MightIII", "MightIV",
            "PursuitI", "PursuitII", "PursuitIII", "烈刃",
        };

        /// <summary>アイテム ID からその内部の passive SkillId を列挙 (ItemDatabase 経由・null 安全)。</summary>
        public static IEnumerable<string> GetPassiveIds(string itemId)
        {
            var db = InventorySystem.ItemDatabase.Instance;
            var data = db != null ? db.GetItem(itemId) : null;
            if (data?.passiveSkills == null) yield break;
            foreach (var p in data.passiveSkills)
                if (p != null && !string.IsNullOrEmpty(p.internalName))
                    yield return p.internalName;
        }

        /// <summary>アイテム ID + persona からスコア加点を直接算出 (呼び出し側の簡略化)。
        /// Standard は特別分岐: 学習済み S ランク=+4, A ランク=+2, それ以外は -3 (敬遠)。
        /// = 「高 Tier アイテムのみをひたすら取り、 中位以下は避ける」超保守派。</summary>
        public static int ScoreBonusForItem(BuildPersona persona, string itemId)
        {
            if (persona == BuildPersona.RawTier || string.IsNullOrEmpty(itemId)) return 0;
            if (persona == BuildPersona.Standard)
            {
                if (LearnedPriorityProvider.IsSRank(itemId)) return +4;
                if (LearnedPriorityProvider.IsARank(itemId)) return +2;
                return -3;
            }
            return ScoreBonus(persona, GetPassiveIds(itemId));
        }

        /// <summary>ペルソナ抽選 (RawTier 比率と非RawTier 比率で分配)。
        /// rawTierRatio=0.5 の場合、50% が RawTier、残り50% が非RawTier 9種から等確率。</summary>
        public static BuildPersona Roll(float rawTierRatio, System.Random rng)
        {
            if (rng.NextDouble() < rawTierRatio) return BuildPersona.RawTier;
            // 非RawTier: 9種から等確率
            var vals = new[]
            {
                BuildPersona.Standard, BuildPersona.Crit, BuildPersona.Bleed,
                BuildPersona.Rinkai, BuildPersona.Poison, BuildPersona.Bludgeon,
                BuildPersona.Charge, BuildPersona.Shield, BuildPersona.Berserk,
            };
            return vals[rng.Next(vals.Length)];
        }
    }
}

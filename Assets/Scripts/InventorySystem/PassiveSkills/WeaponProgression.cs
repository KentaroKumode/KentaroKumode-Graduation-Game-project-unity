using System.Collections.Generic;

namespace InventorySystem.PassiveSkills
{
    /// <summary>
    /// 武器の強化段階に応じてパッシブを動的に決定する。
    /// 武器アイテム(items.json)の静的 passiveSkills は使わず、家系＋段階から算出する。
    ///
    /// 段階(stage 0-7)= (tierNum-1)*2 + plus :
    ///   T1   : 共通A-I
    ///   T1+  : 共通A-II
    ///   T2   : 共通A-II + 共通B-I
    ///   T2+  : 共通A-II + 共通B-II
    ///   T3   : 共通A-II + 共通B-II + 固有1
    ///   T3+  : 共通A-III + 共通B-II + 固有1
    ///   T4   : 共通A-III + 共通B-II + 固有1 + 固有2
    ///   T4+  : 共通A-III + 共通B-III + 固有1 + 固有2
    ///   (T4+2〜+10 : 上記＋業物lv1〜10。業物はパッシブでなくステータス補正＝CombatManagerで処理)
    ///
    /// 対象家系: 剣/斧/短剣/盾/呪い。聖剣(invest)・竜閃・行き止まり等は対象外（従来通り静的）。
    /// </summary>
    public static class WeaponProgression
    {
        private struct Family
        {
            public string[] aLine;   // 共通A I/II/III
            public string[] bLine;   // 共通B I/II/III
            public string uniq1;     // 固有1 (T3〜)
            public string uniq2;     // 固有2 (T4〜)
        }

        private static readonly string[] M = { "MightI", "MightII", "MightIII" };       // 筋力/剛力
        private static readonly string[] P = { "PursuitI", "PursuitII", "PursuitIII" }; // 追撃
        private static readonly string[] I = { "InsightI", "InsightII", "InsightIII" }; // 心眼/慧眼
        private static readonly string[] F = { "FortitudeI", "FortitudeII", "FortitudeIII" }; // 頑強/堅忍
        private static readonly string[] V = { "VitalityI", "VitalityII", "VitalityIII" };    // 活力

        // 家系プレフィックス → 構成（M/P/I/F/V より後に初期化されるよう宣言順に注意）
        private static readonly Dictionary<string, Family> Families = new Dictionary<string, Family>
        {
            ["sword"]  = new Family { aLine = M, bLine = P, uniq1 = "Riposte",    uniq2 = "Destiny" },
            ["axe"]    = new Family { aLine = M, bLine = I, uniq1 = "Frenzy",     uniq2 = "BloodDecree" },
            ["dagger"] = new Family { aLine = P, bLine = I, uniq1 = "Execute",    uniq2 = "Nightfall" },
            ["shield"] = new Family { aLine = F, bLine = V, uniq1 = "Parry",      uniq2 = "HolyShield" },
            ["curse"]  = new Family { aLine = M, bLine = I, uniq1 = "CurseBind",  uniq2 = "Abyss" },
        };

        // 動的付与パッシブの表示名（PassiveSkillManager の表示用）
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["MightI"]="筋力I", ["MightII"]="筋力II", ["MightIII"]="剛力III",
            ["PursuitI"]="追撃I", ["PursuitII"]="追撃II", ["PursuitIII"]="追撃III",
            ["InsightI"]="心眼I", ["InsightII"]="心眼II", ["InsightIII"]="慧眼III",
            ["FortitudeI"]="頑強I", ["FortitudeII"]="頑強II", ["FortitudeIII"]="堅忍III",
            ["VitalityI"]="活力I", ["VitalityII"]="活力II", ["VitalityIII"]="活力III",
            ["Riposte"]="切り返し", ["Destiny"]="運命", ["Frenzy"]="復讐", ["BloodDecree"]="血令",
            ["Execute"]="処刑", ["Nightfall"]="蝕夜", ["Parry"]="パリィ", ["HolyShield"]="聖なる守り",
            ["CurseBind"]="呪縛", ["Abyss"]="刹那の惜別",
            ["Lightweight"]="軽量", ["Mastery"]="熟練", ["Skill"]="技量",
        };

        /// <summary>この武器が段階式パッシブ進行の対象か（剣/斧/短剣/盾/呪いの _tN）。</summary>
        public static bool IsProgressionWeapon(string weaponId)
            => TryParse(weaponId, out _, out _);

        /// <summary>weaponId と +段階(plus 0/1)から、現在アクティブなパッシブ internalName を算出。</summary>
        public static List<string> Compute(string weaponId, int plus)
        {
            var result = new List<string>();
            if (!TryParse(weaponId, out string famKey, out int tierNum)) return result;
            var fam = Families[famKey];

            int stage = (tierNum - 1) * 2 + (plus > 0 ? 1 : 0); // 0..7 (T1..T4+)

            int aLv = stage == 0 ? 1 : (stage <= 4 ? 2 : 3);            // T1=1 / T1+〜T3=2 / T3+〜T4+=3
            int bLv = stage < 2 ? 0 : (stage == 2 ? 1 : (stage < 7 ? 2 : 3)); // none/T2=1/〜T4=2/T4+=3

            result.Add(fam.aLine[aLv - 1]);
            if (bLv > 0) result.Add(fam.bLine[bLv - 1]);
            // curse 系列は uniq1 (CurseBind) を T1 から付与 = 自傷リスクを進化前から負わせる
            int uniq1Stage = famKey == "curse" ? 0 : 4;
            if (stage >= uniq1Stage) result.Add(fam.uniq1);
            if (stage >= 6) result.Add(fam.uniq2); // T4〜

            // Tier段階のダイス合計補正（少ダイス低Tierの底上げ。T4は補正なし）
            if (tierNum == 1) result.Add("Lightweight");      // +3
            else if (tierNum == 2) result.Add("Mastery");     // +2
            else if (tierNum == 3) result.Add("Skill");       // +1
            return result;
        }

        public static string DisplayName(string internalName)
            => Names.TryGetValue(internalName, out var n) ? n : internalName;

        /// <summary>weaponId が "sword_t2" 等なら家系キーと Tier番号を返す。</summary>
        private static bool TryParse(string weaponId, out string famKey, out int tierNum)
        {
            famKey = null; tierNum = 0;
            if (string.IsNullOrEmpty(weaponId)) return false;
            int us = weaponId.LastIndexOf("_t");
            if (us < 0) return false;
            string prefix = weaponId.Substring(0, us);
            if (!Families.ContainsKey(prefix)) return false;
            if (!int.TryParse(weaponId.Substring(us + 2), out tierNum)) return false;
            famKey = prefix;
            return true;
        }
    }
}

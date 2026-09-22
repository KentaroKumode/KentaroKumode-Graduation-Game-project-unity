using System.Collections.Generic;

namespace InventorySystem.PassiveSkills
{
    /// <summary>
    /// 武器の強化段階に応じてパッシブを動的に決定する。
    /// 武器アイテム(items.json)の静的 passiveSkills は使わず、家系＋段階から算出する。
    ///
    /// 段階(stage 0-5)= (tierNum-2)*2 + plus  ── **T1 は 2026-09-05 に廃止 (職業配布が T2)**:
    ///   T2   : A-I
    ///   T2+  : A-II
    ///   T3   : A-II + B-I
    ///   T3+  : A-II + B-II + 固有1
    ///   T4   : A-III + B-II + 固有1 + 固有2
    ///   T4+  : A-III + B-III + 固有1 + 固有2
    ///
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

        // ============================================================
        //  家系専用ラダー (2026-09-05 リワーク)
        //
        //  **旧構成は共通ラダー (筋力/追撃/心眼/頑強/活力) の借用だった。**
        //  これはアイテム側の家系システムの名残で、 剣=筋力+追撃 / 斧=筋力+心眼 と
        //  半分が同じ ── 家系の違いが数値の大小でしか出せなかった。
        //  効果の正本は WeaponFamilyEffects.cs。
        //
        //  | 家系 | 性格 | A (T1〜) | B (T2〜) | 固有1 (T3) | 固有2 (T4) |
        //  |---|---|---|---|---|---|
        //  | 短剣 | 短期決戦・一撃必殺 + DOT | 疾手 | 毒手 | 処刑 | 蝕夜 |
        //  | 剣   | タイマン力 + バランス     | 間合 | 一対一 | 切り返し | 果たし合い |
        //  | 盾   | 生存 + カウンター         | 城壁 | 反攻 | パリィ | 衛士の慣い |
        //  | 斧   | 削り合いレース + 自己バフ | 猛り | 大鉈 | 復讐 | 血令 |
        //
        //  **2026-09-22: スキル id を表示名へ統一した**。 以前は英数の内部名
        //  (SwordReachI / Riposte / Destiny …) と日本語の表示名を別々に持っていた。
        // ============================================================

        private static readonly string[] SwiftHand  = { "疾手I",   "疾手II",   "疾手III"   };
        private static readonly string[] VenomHand  = { "毒手I",   "毒手II",   "毒手III"   };
        private static readonly string[] SwordReach = { "間合I",   "間合II",   "間合III"   };
        private static readonly string[] Duelist    = { "一対一I", "一対一II", "一対一III" };
        private static readonly string[] Bulwark    = { "城壁I",   "城壁II",   "城壁III"   };
        private static readonly string[] Retaliate  = { "反攻I",   "反攻II",   "反攻III"   };
        private static readonly string[] Fervor     = { "猛りI",   "猛りII",   "猛りIII"   };
        private static readonly string[] Cleaver    = { "大鉈I",   "大鉈II",   "大鉈III"   };

        // 家系プレフィックス → 構成（上のラダー配列より後に初期化されるよう宣言順に注意）
        private static readonly Dictionary<string, Family> Families = new Dictionary<string, Family>
        {
            ["sword"]  = new Family { aLine = SwordReach, bLine = Duelist,   uniq1 = "切り返し", uniq2 = "果たし合い" },
            ["axe"]    = new Family { aLine = Fervor,     bLine = Cleaver,   uniq1 = "復讐",  uniq2 = "血令" },
            ["dagger"] = new Family { aLine = SwiftHand,  bLine = VenomHand, uniq1 = "処刑", uniq2 = "蝕夜" },
            ["shield"] = new Family { aLine = Bulwark,    bLine = Retaliate, uniq1 = "パリィ",   uniq2 = "衛士の慣い" },
            // curse 系列 (curse_t1〜t4) は 2026-07-17 削除。 uniq1=CurseBind / uniq2=Abyss は class 残置・アイテムから参照なし。
            // 複合武器 6 品 (swordaxe/swordshield/sworddagger/axeshield/axedagger/shielddagger) は
            //   2026-09-21 削除。 T2〜T4 の純ラダーで足りる (GAME.md §24)。
        };

        // 表示名表 (Names) は 2026-09-22 撤去。 スキルの id を表示名へ統一したので、
        //   id → 表示名 の写像が恒等になった (GAME.md §24)。

        /// <summary>この武器が段階式パッシブ進行の対象か（剣/斧/短剣/盾/呪いの _tN）。</summary>
        public static bool IsProgressionWeapon(string weaponId)
            => TryParse(weaponId, out _, out _);

        /// <summary>weaponId と +段階(plus 0/1)から、現在アクティブなパッシブ internalName を算出。</summary>
        public static List<string> Compute(string weaponId, int plus)
        {
            var result = new List<string>();
            if (!TryParse(weaponId, out string famKey, out int tierNum)) return result;
            var fam = Families[famKey];

            // **T1 は 2026-09-05 に廃止**。 職業配布が T2 になったので stage の起点をずらす。
            //   stage 0..5 = T2 / T2+ / T3 / T3+ / T4 / T4+
            int stage = (tierNum - 2) * 2 + (plus > 0 ? 1 : 0);
            if (stage < 0) stage = 0;   // 万一 T1 の残骸 ID が来ても落とさない

            //   T2  : A-I
            //   T2+ : A-II
            //   T3  : A-II + B-I
            //   T3+ : A-II + B-II + 固有1
            //   T4  : A-III + B-II + 固有1 + 固有2
            //   T4+ : A-III + B-III + 固有1 + 固有2
            int aLv = stage == 0 ? 1 : (stage <= 3 ? 2 : 3);
            int bLv = stage < 2 ? 0 : (stage == 2 ? 1 : (stage < 5 ? 2 : 3));

            result.Add(fam.aLine[aLv - 1]);
            if (bLv > 0) result.Add(fam.bLine[bLv - 1]);
            if (stage >= 3 && !string.IsNullOrEmpty(fam.uniq1)) result.Add(fam.uniq1); // T3+〜
            if (stage >= 4 && !string.IsNullOrEmpty(fam.uniq2)) result.Add(fam.uniq2); // T4〜

            // 2026-08-09: Tier段階のダイス合計補正 (Lightweight/Mastery/Skill) をここからも削除。
            //   効果クラスの登録は 2026-07-15 に廃止済みだったのに **ID の生成だけが残っており**、
            //   T1〜T3 の武器が「効果のないパッシブ名」を表示し続けていた (UI が嘘をつく状態)。
            //   廃止理由は当時のコメント通り「筋力とロールが重複」。
            return result;
        }

        /// <summary>表示名。 <b>id がそのまま表示名</b> (2026-09-22)。 呼び出し側の互換のために残す。</summary>
        public static string DisplayName(string internalName) => internalName;

        /// <summary>その武器の家系キーと段を返す。 進行武器でなければ false。
        ///
        /// <para><b>id を綴りで切らない</b> (2026-09-22)。 items.json の <c>family</c> / <c>tier</c> を読む。
        /// 以前は <c>sword_t2</c> のような id を <c>_t</c> で分割していたが、 id は表示名になったので
        /// 綴りに意味は無い ── 綴りへ戻すと、 調整で品名を変えた瞬間に
        /// 武器のパッシブが黙って消える。</para></summary>
        private static bool TryParse(string weaponId, out string famKey, out int tierNum)
        {
            famKey = null; tierNum = 0;
            if (string.IsNullOrEmpty(weaponId)) return false;
            var def = InventorySystem.ItemDatabase.Instance?.GetItem(weaponId);
            if (def == null || string.IsNullOrEmpty(def.family) || def.tier <= 0) return false;
            if (!Families.ContainsKey(def.family)) return false;
            famKey = def.family; tierNum = def.tier;
            return true;
        }
    }
}

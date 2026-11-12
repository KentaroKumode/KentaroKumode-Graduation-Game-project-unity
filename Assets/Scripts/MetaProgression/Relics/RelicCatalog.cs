using System;

namespace MetaProgression.Relics
{
    // ============================================================
    //  遺物 (周回進行・docs/GAME.md §15-5・2026-11-10 再編)
    //
    //  3 部位 (冠=攻め / 環=守り / 符=軸) × 6 セット。 主ステはセットと部位で固定、 段は★で決まる (最大 4)。
    //  サブは汎用 8 種から最大 3 個・各最大 2 段 (強化 +1〜+4 で増える・伸びる)。 セットに寄せて抽選する。
    //  数値はすべて仮置き (バランス調整は別 issue)。 enum は int でセーブに入るので、 末尾へ足すだけにする。
    // ============================================================

    public enum RelicSlot { Crown = 0, Ring = 1, Charm = 2 }

    /// <summary>セット = 軸。 呼び名: 会心 = 心眼 / 出血 = 血晶 / 毒 = 瘴気 / 臨界 = 炉温 / 充電 = 電流 / シールド = 堅牢。</summary>
    public enum RelicSet { Shingan = 0, Kessho = 1, Shoki = 2, Roon = 3, Denryu = 4, Kenro = 5 }

    public enum RelicStat
    {
        // ── サブにも出る汎用 8 種 ──
        Attack = 0,          // 攻撃 +N
        DamagePct = 1,       // 与ダメ +N%
        CritMult = 2,        // 会心倍率 +N% (基準 2.0 に対する %)
        ArmorPen = 3,        // 防御貫通 +N
        MaxHp = 4,           // 最大 HP +N
        DamageReduction = 5, // 被ダメ −N% (割合軽減)
        Defense = 6,         // 防御 +N
        Lifesteal = 7,       // 吸血 +N% (主攻撃のダメージにだけ掛かる)
        // ── 主ステ専用 ──
        CritRate = 8,        // 会心率 +N%
        BloodOffering = 9,   // 血贄 +N% (敵味方問わず、 出血が与えたダメージの N% を回復)
        EscalationDelay = 10,// 敵の段階の遅れ +N ターン
        BlockDicePct = 11,   // 防御の端子を挿した出目 +N%
        OpeningBleed = 12,   // 開幕出血 +N
        OpeningPoison = 13,  // 開幕毒 +N
        Rinkai = 14,         // 爆発時に敵の現在 HP の N% を固定ダメ枠へ
        Charge = 15,         // 充電 (開幕・毎ターン)
        ShieldGainPct = 16,  // シールドの獲得量 +N%
    }

    public static class RelicCatalog
    {
        public const int SlotCount = 3, SetCount = 6;
        public const int MaxStars = 4, MaxEnhance = 4, MaxSubs = 3, SubStepMax = 2;
        /// <summary>難易度ボーナスの頭打ち (#7)。 9・10 は見返りの増えない純粋な挑戦。</summary>
        public const int DifficultyBonusCap = 8;

        public static readonly RelicStat[] SubPool =
        {
            RelicStat.Attack, RelicStat.DamagePct, RelicStat.CritMult, RelicStat.ArmorPen,
            RelicStat.MaxHp, RelicStat.DamageReduction, RelicStat.Defense, RelicStat.Lifesteal,
        };

        // ── セット × 部位 → 主ステ (§15-5) ──
        private static readonly RelicStat[,] Mains =
        {
            //            冠                       環                          符
            /* 心眼 */ { RelicStat.CritRate,  RelicStat.DamageReduction, RelicStat.CritMult },
            /* 血晶 */ { RelicStat.Attack,    RelicStat.BloodOffering,   RelicStat.OpeningBleed },
            /* 瘴気 */ { RelicStat.ArmorPen,  RelicStat.EscalationDelay, RelicStat.OpeningPoison },
            /* 炉温 */ { RelicStat.DamagePct, RelicStat.Defense,         RelicStat.Rinkai },
            /* 電流 */ { RelicStat.DamagePct, RelicStat.DamageReduction, RelicStat.Charge },
            /* 堅牢 */ { RelicStat.Attack,    RelicStat.BlockDicePct,    RelicStat.ShieldGainPct },
        };
        public static RelicStat MainOf(RelicSet set, RelicSlot slot) => Mains[(int)set, (int)slot];

        /// <summary>★ → 主ステの段。 ★1〜2 = 2 / ★3 = 3 / ★4 = 4。</summary>
        public static int MainStepOf(int stars) => stars >= 4 ? 4 : stars >= 3 ? 3 : 2;

        // ── サブのセット寄せ: 出やすい ×3 / 普通 ×1 / 出にくい ×0.25 ──
        public const float WeightLikely = 3f, WeightNormal = 1f, WeightUnlikely = 0.25f;
        private static readonly RelicStat[][] Likely =
        {
            new[] { RelicStat.CritMult, RelicStat.Attack, RelicStat.Lifesteal },        // 心眼
            new[] { RelicStat.Attack, RelicStat.Lifesteal, RelicStat.MaxHp },           // 血晶
            new[] { RelicStat.MaxHp, RelicStat.DamageReduction, RelicStat.Defense },    // 瘴気
            new[] { RelicStat.Defense, RelicStat.MaxHp, RelicStat.DamagePct },          // 炉温
            new[] { RelicStat.DamagePct, RelicStat.Attack, RelicStat.DamageReduction }, // 電流
            new[] { RelicStat.Defense, RelicStat.Attack, RelicStat.MaxHp },             // 堅牢
        };
        private static readonly RelicStat[][] Unlikely =
        {
            new[] { RelicStat.Defense },                                // 心眼
            new[] { RelicStat.CritMult, RelicStat.Defense },            // 血晶
            new[] { RelicStat.CritMult, RelicStat.Lifesteal },          // 瘴気
            new[] { RelicStat.CritMult, RelicStat.Lifesteal },          // 炉温
            new[] { RelicStat.Defense },                                // 電流
            new[] { RelicStat.Lifesteal },                              // 堅牢
        };
        public static float SubWeight(RelicSet set, RelicStat s)
        {
            if (Array.IndexOf(Likely[(int)set], s) >= 0) return WeightLikely;
            if (Array.IndexOf(Unlikely[(int)set], s) >= 0) return WeightUnlikely;
            return WeightNormal;
        }
        public static bool IsLikely(RelicSet set, RelicStat s) => Array.IndexOf(Likely[(int)set], s) >= 0;

        // ── 段 → 実数値 (仮置き。 汎用の値は旧遺物の段の表を引き継ぐ) ──
        public static int ValueOf(RelicStat s, int step)
        {
            if (step <= 0) return 0;
            switch (s)
            {
                case RelicStat.Attack:          return step;
                case RelicStat.DamagePct:       return 5 * step;
                case RelicStat.CritMult:        return 11 * step;
                case RelicStat.ArmorPen:        return step;
                case RelicStat.MaxHp:           return 4 * step;
                case RelicStat.DamageReduction: return 3 * step;
                case RelicStat.Defense:         return 2 * step;     // 仮: 整備パネル〈装甲〉1 段と同じ
                case RelicStat.Lifesteal:       return 2 * step;     // 被ダメ −5% と同じ回復量 ≒ 1.8% を 2% に丸めた
                case RelicStat.CritRate:        return 4 * step;
                case RelicStat.BloodOffering:   return 5 * step;     // 仮
                case RelicStat.EscalationDelay: return step;         // 仮 (ターン)
                case RelicStat.BlockDicePct:    return 5 * step;     // 仮
                case RelicStat.OpeningBleed:    return 2 + step;     // 旧表 3,4,5,6
                case RelicStat.OpeningPoison:   return step;
                case RelicStat.Rinkai:          return step;         // 現在 HP の %
                case RelicStat.Charge:          return ChargeOpening(step);
                case RelicStat.ShieldGainPct:   return 10 * step;    // 仮
            }
            return 0;
        }
        public static int ChargeOpening(int step) => step <= 0 ? 0 : step == 1 ? 2 : step == 2 ? 3 : 4;
        public static int ChargePerTurn(int step) => step >= 4 ? 2 : 0;

        // ── 名前・表示 ──
        public static readonly string[] SetNames = { "心眼", "血晶", "瘴気", "炉温", "電流", "堅牢" };
        public static readonly string[] SlotNames = { "冠", "環", "符" };
        /// <summary>「セット名 ＋ 部位名」。 つなぎはセットごとに固定: 心眼の / 血晶 / 瘴気の / 炉温の / 電流 / 堅牢な。</summary>
        private static readonly string[] Joiner = { "の", "", "の", "の", "", "な" };
        public static string NameOf(RelicSet set, RelicSlot slot) => SetNames[(int)set] + Joiner[(int)set] + SlotNames[(int)slot];

        public static string StatName(RelicStat s)
        {
            switch (s)
            {
                case RelicStat.Attack: return "攻撃";
                case RelicStat.DamagePct: return "与ダメ";
                case RelicStat.CritMult: return "会心倍率";
                case RelicStat.ArmorPen: return "防御貫通";
                case RelicStat.MaxHp: return "最大HP";
                case RelicStat.DamageReduction: return "被ダメ";
                case RelicStat.Defense: return "防御";
                case RelicStat.Lifesteal: return "吸血";
                case RelicStat.CritRate: return "会心率";
                case RelicStat.BloodOffering: return "血贄";
                case RelicStat.EscalationDelay: return "敵の段階の遅れ";
                case RelicStat.BlockDicePct: return "防御の端子を挿した出目";
                case RelicStat.OpeningBleed: return "開幕出血";
                case RelicStat.OpeningPoison: return "開幕毒";
                case RelicStat.Rinkai: return "臨界";
                case RelicStat.Charge: return "充電";
                case RelicStat.ShieldGainPct: return "シールドの獲得量";
            }
            return s.ToString();
        }

        public static string Describe(RelicStat s, int step)
        {
            int v = ValueOf(s, step);
            switch (s)
            {
                case RelicStat.DamagePct: case RelicStat.CritMult: case RelicStat.Lifesteal: case RelicStat.CritRate:
                case RelicStat.BloodOffering: case RelicStat.BlockDicePct: case RelicStat.ShieldGainPct:
                    return StatName(s) + " +" + v + "%";
                case RelicStat.DamageReduction: return "被ダメ −" + v + "%";
                case RelicStat.EscalationDelay: return "敵の段階の遅れ +" + v + "T";
                case RelicStat.Rinkai: return "臨界 (爆発時 現在HP " + v + "%)";
                case RelicStat.Charge: return "充電 開幕 +" + v + (ChargePerTurn(step) > 0 ? "・毎T +" + ChargePerTurn(step) : "");
            }
            return StatName(s) + " +" + v;
        }

        // ── セット効果 (2 個 / 3 個・仮置き) ──
        public static readonly string[] Set2Desc =
        {
            "会心倍率 +30%", "開幕出血 +4", "開幕毒 +10", "臨界のメーター +20%", "毎ターン充電 +2", "シールドの獲得量 +20%",
        };
        public static readonly string[] Set3Desc =
        {
            "会心のターン、 次のターンの会心率 +20%",
            "出血が 10 以上の敵へ与ダメ +20%",
            "毒が残る割合 +20% (上限 90%)",
            "爆発のたびにシールド +6",
            "過充電の間、 被ダメ −3",
            "ターン始めの減衰で残るシールド +25%",
        };
        public const int Shingan2CritMultPct = 30, Shingan3NextCritPct = 20;
        public const int Kessho2Bleed = 4, Kessho3BleedMin = 10, Kessho3DmgPct = 20;
        public const int Shoki2Poison = 10, Shoki3KeepPct = 20, PoisonKeepCap = 90;
        public const int Roon2HeatPct = 20, Roon3BurstShield = 6;
        public const int Denryu2ChargePerTurn = 2, Denryu3FlatReduce = 3;
        public const int Kenro2ShieldGainPct = 20, Kenro3ShieldKeepPct = 25;
    }
}

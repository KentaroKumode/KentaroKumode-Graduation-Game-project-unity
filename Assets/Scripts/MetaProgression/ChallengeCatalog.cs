using System.Collections.Generic;

namespace MetaProgression
{
    /// <summary>1 軸の定義。 **「どの Tier が存在し、それぞれ何点か」だけ**を持つ。</summary>
    public sealed class AxisDef
    {
        public ChallengeAxis      axis;
        public string             displayName;
        public ChallengeCategory  category;

        /// <summary>この軸に存在する Tier。 **Tier 番号がそのまま点数**。
        ///
        /// **v4.1 (2026-08-10) で「番号 = 点数」へ戻した。** v4.0 は
        /// 「index+1 が段番号 / 値が点数」と分離していたが、 分離で得られるものが
        /// 何も無かった (下記)。 段数を効果に合わせたいだけなら、 **存在する Tier 番号を
        /// 飛ばせばよい** ── `{2,3}` は「T2 と T3 があり、 T1 は無い」の意味。
        ///
        /// v4.0 の分離を棄却した理由 (§24):
        ///   - 「段数を選べる」は番号を飛ばせば済み、 分離は不要だった
        ///   - 「カテゴリ 6pt を組める」は点数の話で、 番号との対応と無関係
        ///   - 「1 段だけの軸に 3pt を払わせる」は **T3 と呼べば済む**。
        ///     『Tier は 1 から連番』は仕様ではなく実装者の思い込みだった
        ///   - 「点数を独立に較正できる」は、 番号 = 点数なら較正がそのまま段の較正
        ///   - 実害: `T2* 3pt` を T3 と読み違える (実際に起きた)
        ///
        /// これで **Tier4 も例外でなくなる** ── 4pt の段、 というだけ。
        /// 違いは解禁条件 (カテゴリ 6pt) が付くことだけ。
        ///
        /// **昇順・重複なし・基礎軸は 1〜3 の範囲**。 検査は
        /// <see cref="ChallengeResolver.Validate"/>。</summary>
        public int[] tiers;

        /// <summary>この軸の最上位 Tier 番号 (= 最大点数)。 **段数ではない**
        /// ── `{1,3}` の MaxTier は 3 で、 段数は 2。</summary>
        public int MaxTier => tiers[tiers.Length - 1];

        /// <summary>この軸に払える最大点数。 番号 = 点数なので <see cref="MaxTier"/> と同じ。
        /// **段を足し上げない** ── 1 軸で選べる Tier は 1 つだけなので。</summary>
        public int MaxPoints => MaxTier;

        /// <summary>その Tier の点数。 存在しない Tier は 0。</summary>
        public int PointsOf(int tier) => HasTier(tier) ? tier : 0;

        public bool HasTier(int tier)
        {
            for (int i = 0; i < tiers.Length; i++) if (tiers[i] == tier) return true;
            return false;
        }

        /// <summary>存在する Tier 番号。 UI の順送りなど「段の列挙」に使う。
        /// 番号 = 点数なので、 そのまま点数の列挙でもある。</summary>
        public int[] availableTiers => tiers;
    }

    /// <summary>1 つの Tier4 の定義。</summary>
    public sealed class T4Def
    {
        public ChallengeT4       t4;
        public string            displayName;
        /// <summary>このカテゴリの**基礎軸で 6pt 払っている**ときだけ解禁される。</summary>
        public ChallengeCategory category;
        /// <summary>Tier4 自身の点数。 **番号 = 点数の規則どおり 4pt** (v4.1)。</summary>
        public int               points;
    }

    /// <summary>
    /// 挑戦デバフの定義表（**v4.1 / 12 軸 30pt + Tier4 20pt**・2026-08-10）。
    /// **UI もスコア計算も検証もここだけを読む。**
    ///
    /// **効果値 (×1.25 / −5 など) をここに書かないこと。**
    /// 効果値は <see cref="MetaDebuffApplicator"/> が持つ。 両方に書くと、
    /// UI の点数表示とゲーム内の実効果がずれた時にどちらが正本か分からなくなる。
    ///
    /// **v3.0 からの変更点** (docs/GAME.md §15-2):
    ///   - 「全軸 T1/T2/T3」を破棄。 段数は 1〜3 で軸ごとに違う
    ///   - カテゴリの軸数は 2〜3 で不揃い。 **カテゴリ最大は 6pt で不変**
    ///   - Tier4 の解禁条件を「2軸ともT3」→「カテゴリで 6pt」へ (段数が揃わないため)
    ///
    /// **v4.1 の変更点**:
    ///   - **Tier 番号 = 点数**へ戻した。 段数を減らしたい軸は**番号を飛ばす**
    ///     (例: 〈天変地異〉は T3 のみ / 〈厚い皮膚〉は T2 と T3)
    ///   - Tier4 が例外でなくなった (4pt の段。 解禁条件が付くだけ)
    ///
    /// **旧ロードアウトは破棄する** (v3.0 移行時と同じ)。 軸の増減があるので互換変換しない。
    /// </summary>
    public static class ChallengeCatalog
    {
        /// <summary>基礎軸だけの満点 = 5 カテゴリ × 6pt = **30pt**。</summary>
        public const int BaseMaxScore = 30;
        /// <summary>Tier4 を含む満点 = 30 + 4×5 = **50pt**。</summary>
        public const int TotalMaxScore = 50;
        /// <summary>Tier4 1 個あたりの点数。</summary>
        public const int T4Points = 4;
        /// <summary>1 カテゴリの基礎軸の満点。 Tier4 の解禁コストでもある。</summary>
        public const int CategoryBaseMax = 6;
        /// <summary>基礎軸 1 本が取れる最大点数。 4pt は Tier4 の専有。</summary>
        public const int AxisMaxPoints = 3;

        /// <summary>軸を 1 本定義する。 <paramref name="tiers"/> は **存在する Tier 番号**
        /// (= そのまま点数)。 昇順・重複なし・1〜3。 飛ばしてよい (例: 2,3 / 1,3 / 3)。</summary>
        private static AxisDef A(ChallengeAxis ax, string name, ChallengeCategory cat, params int[] tiers)
            => new AxisDef { axis = ax, displayName = name, category = cat, tiers = tiers };

        /// <summary>全 12 軸。 カテゴリごとに最大 6pt、 合計 30pt。
        /// 数字は **Tier 番号 = 点数**。 `2, 3` は「T2 と T3 があり T1 は無い」。</summary>
        public static readonly List<AxisDef> Axes = new List<AxisDef>
        {
            // A. 生存圧 — 3 + 2 + 1 = 6pt
            A(ChallengeAxis.長引く負傷,   "長引く負傷",   ChallengeCategory.A生存圧, 1, 2, 3),
            A(ChallengeAxis.穴の空いた鞄, "穴の空いた鞄", ChallengeCategory.A生存圧, 1, 2),
            A(ChallengeAxis.遅い回復,     "遅い回復",     ChallengeCategory.A生存圧, 1),

            // B. 敵強化 — 3 + 3 = 6pt
            A(ChallengeAxis.厚い皮膚,     "厚い皮膚",     ChallengeCategory.B敵強化, 2, 3),
            A(ChallengeAxis.狂暴化,       "狂暴化",       ChallengeCategory.B敵強化, 1, 2, 3),

            // C. 戦闘則 — 3 + 3 = 6pt
            A(ChallengeAxis.綻び,         "綻び",         ChallengeCategory.C戦闘則, 1, 2, 3),
            A(ChallengeAxis.見放された運, "見放された運", ChallengeCategory.C戦闘則, 1, 3),

            // D. 経済 — 3 + 2 + 1 = 6pt
            A(ChallengeAxis.搾取経済,     "搾取経済",     ChallengeCategory.D経済,   2, 3),
            A(ChallengeAxis.通行料,       "通行料",       ChallengeCategory.D経済,   1),
            A(ChallengeAxis.宿屋連合,     "宿屋連合",     ChallengeCategory.D経済,   2),

            // E. 崩壊 — 3 + 3 = 6pt
            A(ChallengeAxis.絶望的な戦闘, "絶望的な戦闘", ChallengeCategory.E崩壊,   1, 2, 3),
            A(ChallengeAxis.天変地異,     "天変地異",     ChallengeCategory.E崩壊,   3),
        };

        /// <summary>全 5 種の Tier4。 各 4pt で合計 20pt。</summary>
        public static readonly List<T4Def> T4s = new List<T4Def>
        {
            new T4Def { t4 = ChallengeT4.破綻,       displayName = "破綻",       category = ChallengeCategory.A生存圧, points = T4Points },
            new T4Def { t4 = ChallengeT4.鋼の皮膚,   displayName = "鋼の皮膚",   category = ChallengeCategory.B敵強化, points = T4Points },
            new T4Def { t4 = ChallengeT4.凶運,       displayName = "凶運",       category = ChallengeCategory.C戦闘則, points = T4Points },
            new T4Def { t4 = ChallengeT4.破産,       displayName = "破産",       category = ChallengeCategory.D経済,   points = T4Points },
            new T4Def { t4 = ChallengeT4.最後の審判, displayName = "最後の審判", category = ChallengeCategory.E崩壊,   points = T4Points },
        };

        public static AxisDef Get(ChallengeAxis axis)
        {
            for (int i = 0; i < Axes.Count; i++) if (Axes[i].axis == axis) return Axes[i];
            return null;
        }

        public static T4Def Get(ChallengeT4 t4)
        {
            for (int i = 0; i < T4s.Count; i++) if (T4s[i].t4 == t4) return T4s[i];
            return null;
        }

        /// <summary>そのカテゴリの全軸を最上位にしたときの合計点（= Tier4 の解禁コスト・6pt）。</summary>
        public static int CategoryMaxScore(ChallengeCategory cat)
        {
            int sum = 0;
            for (int i = 0; i < Axes.Count; i++) if (Axes[i].category == cat) sum += Axes[i].MaxPoints;
            return sum;
        }

        /// <summary>そのカテゴリに属する軸（v4.0 では 2〜3 個）。</summary>
        public static List<AxisDef> AxesOf(ChallengeCategory cat)
        {
            var list = new List<AxisDef>(3);
            for (int i = 0; i < Axes.Count; i++) if (Axes[i].category == cat) list.Add(Axes[i]);
            return list;
        }

        // ============================================================
        //  スコア帯 (docs/GAME.md §15-2)
        // ============================================================

        /// <summary>満点が 50pt になったので、 帯も 50pt を上限として引き直した。</summary>
        public static string ScoreBandName(int score)
        {
            if (score <= 0)  return "無印";
            if (score <= 5)  return "探検";
            if (score <= 12) return "極地探査";
            if (score <= 22) return "前途多難";
            if (score <= 34) return "絶望的";
            if (score <= 45) return "十死零生";
            return "混沌";
        }
    }
}

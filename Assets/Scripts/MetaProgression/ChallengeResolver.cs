using System.Collections.Generic;

namespace MetaProgression
{
    /// <summary>
    /// 解決済みの挑戦構成。 **ラン開始時に 1 回だけ作り、 以降は読み取り専用**で回す。
    /// ラン中に挑戦設定が変わることはないので、 参照のたびにリストを走査する必要はない。
    ///
    /// 各システムはここの <see cref="Tier"/> / <see cref="Has"/> だけを見る。
    /// 「T4 が下位軸を最高 Tier へ引き上げる」処理は
    /// <see cref="ChallengeResolver"/> 内で済んでいるので、 呼ぶ側は T4 を意識しなくてよい。
    /// </summary>
    public sealed class ResolvedChallenge
    {
        /// <summary>index = (int)ChallengeAxis、 値 = 解決後の Tier (0 = 無効)。</summary>
        private readonly int[] _tiers;
        private readonly bool[] _t4;

        /// <summary>合計挑戦スコア (0..100)。 軸の Tier 合計 + T4 の点数。</summary>
        public int Score { get; private set; }
        /// <summary>スコア帯の名前 (探検 / 極地探査 / … / 混沌)。</summary>
        public string BandName => ChallengeDifficultyClass.NameOf(Score);
        /// <summary>何も有効になっていないか。 通常プレイはこれ。</summary>
        public bool IsEmpty => Score == 0;

        internal ResolvedChallenge(int[] tiers, bool[] t4, int score)
        {
            _tiers = tiers; _t4 = t4; Score = score;
        }

        /// <summary>解決後の Tier (0 = 無効)。</summary>
        public int Tier(ChallengeAxis axis)
        {
            int i = (int)axis;
            return (i >= 0 && i < _tiers.Length) ? _tiers[i] : 0;
        }

        /// <summary>その軸が Tier 以上で有効か。</summary>
        public bool Has(ChallengeAxis axis, int minTier = 1) => Tier(axis) >= minTier;

        public bool Has(ChallengeT4 v)
        {
            int i = (int)v;
            return (i >= 0 && i < _t4.Length) && _t4[i];
        }

        /// <summary>何も有効でない構成。 通常プレイ / メタデバフ全 OFF の既定値。</summary>
        public static readonly ResolvedChallenge None = new ResolvedChallenge(
            new int[System.Enum.GetValues(typeof(ChallengeAxis)).Length * 4],
            new bool[System.Enum.GetValues(typeof(ChallengeT4)).Length + 1],
            0);

        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"挑戦スコア {Score}pt [{BandName}]");
            for (int i = 0; i < ChallengeCatalog.Axes.Count; i++)
            {
                var d = ChallengeCatalog.Axes[i];
                int t = Tier(d.axis);
                if (t > 0) sb.Append($" / {d.displayName}T{t}");
            }
            for (int i = 0; i < ChallengeCatalog.T4s.Count; i++)
                if (Has(ChallengeCatalog.T4s[i].t4)) sb.Append($" / 【{ChallengeCatalog.T4s[i].displayName}】");
            return sb.ToString();
        }
    }

    /// <summary>
    /// <see cref="ChallengeLoadout"/> (セーブ値) を <see cref="ResolvedChallenge"/> へ畳む。
    ///
    /// **解決順序**:
    ///   1. 軸の Tier を確定する (軸内排他は Loadout 側で保証済み)
    ///   2. Tier4 は **解禁条件を満たしている時だけ**有効化する
    ///      ── 同カテゴリの基礎軸で **6pt 払っていること**
    ///   3. スコア = Σ Tier番号 + 有効な Tier4 の点数
    ///
    /// **v4.1 で「Tier 番号 = 点数」に戻した** (§24)。 段数が軸ごとに違うのは
    /// **存在する番号を飛ばす**ことで表す (〈天変地異〉は T3 のみ、 等)。
    /// 解禁条件はカテゴリの払った点数 ＝ Tier 番号の合計で判定する。
    ///
    /// **旧 v1 との違い**: v1 は「T4 を選ぶとカテゴリ全軸を最高 Tier へ強制」する
    /// 自動引き上げ方式だった。 v3.0 は **プレイヤーが先に 6pt を払った状態でのみ解禁**する
    /// ゲート方式なので、 引き上げは行わない。 条件を満たさない T4 は
    /// **黙って無効**にする (点数も入らない) ── 不正なセーブや UI の取りこぼしで
    /// 「点数だけ入って効果が出ない」状態を作らないため。
    /// </summary>
    public static class ChallengeResolver
    {
        public static ResolvedChallenge Build(ChallengeLoadout loadout)
        {
            int axisCount = 0;
            foreach (ChallengeAxis a in System.Enum.GetValues(typeof(ChallengeAxis)))
                if ((int)a > axisCount) axisCount = (int)a;
            var tiers = new int[axisCount + 1];
            var t4    = new bool[System.Enum.GetValues(typeof(ChallengeT4)).Length + 1];

            if (loadout == null) return new ResolvedChallenge(tiers, t4, 0);

            // --- 1. 軸の Tier を確定 ---
            for (int i = 0; i < ChallengeCatalog.Axes.Count; i++)
            {
                var d = ChallengeCatalog.Axes[i];
                int t = loadout.GetTier(d.axis);
                if (t > 0 && d.HasTier(t)) tiers[(int)d.axis] = t;
            }

            // --- 2. Tier4: 同カテゴリの基礎軸で 6pt 払っている時だけ解禁 ---
            int t4Points = 0;
            for (int i = 0; i < ChallengeCatalog.T4s.Count; i++)
            {
                var def = ChallengeCatalog.T4s[i];
                if (!loadout.HasT4(def.t4)) continue;
                if (!IgnoreT4Unlock && !IsT4Unlocked(def.category, tiers))
                {
                    // **黙って落とさない。** 2026-08-11、 診断スイープで T4 だけを載せたら
                    //   全アームが基準と完全一致し、 計装が全部 0 になった ── 原因が
                    //   「効果が無い」ではなく「解禁条件未達で無効化されていた」ことに
                    //   気づくまで 1 バッチ無駄にした。 要求されたのに落としたなら言う。
                    UnityEngine.Debug.LogWarning(
                        $"[挑戦] T4〈{def.displayName}〉は要求されたが解禁条件未達で無効 "
                        + $"(カテゴリ {def.category} の基礎軸が "
                        + $"{BasePoints(tiers, def.category)}pt / 必要 {ChallengeCatalog.CategoryBaseMax}pt)");
                    continue;
                }
                t4[(int)def.t4] = true;
                t4Points += def.points;
            }

            // --- 3. スコア ---
            int score = t4Points + BasePoints(tiers);

            return new ResolvedChallenge(tiers, t4, score);
        }

        /// <summary>基礎軸で払っている点数の合計 (= 選んだ Tier 番号の合計)。
        /// **存在しない Tier は 0 点**として弾かれる (PointsOf が HasTier を見る)。
        /// <paramref name="only"/> を渡すとそのカテゴリぶんだけ数える。</summary>
        private static int BasePoints(int[] tiers, ChallengeCategory? only = null)
        {
            int sum = 0;
            for (int i = 0; i < ChallengeCatalog.Axes.Count; i++)
            {
                var ax = ChallengeCatalog.Axes[i];
                if (only.HasValue && ax.category != only.Value) continue;
                sum += ax.PointsOf(tiers[(int)ax.axis]);
            }
            return sum;
        }

        /// <summary>**診断専用**: T4 の解禁条件 (カテゴリ 6pt) を無視する。
        ///
        /// 通常は配点として不正なので false。 AutoRunner の
        /// `challengeCategorySweepT4Alone` (T4 と基礎軸の相互作用を分離する診断) だけが立てる。
        /// **測定が終わったら必ず false へ戻すこと** ── 立てっぱなしだと以後の全測定で
        /// 「6pt を払っていないのに T4 が乗る」不正な条件になる。</summary>
        public static bool IgnoreT4Unlock = false;

        /// <summary>そのカテゴリの Tier4 が解禁されているか（基礎軸で 6pt 払っている）。</summary>
        private static bool IsT4Unlocked(ChallengeCategory cat, int[] tiers)
            => BasePoints(tiers, cat) >= ChallengeCatalog.CategoryBaseMax;

        /// <summary>UI 用。 いま Tier4 を選べる状態か。</summary>
        public static bool CanSelectT4(ChallengeLoadout loadout, ChallengeT4 v)
        {
            var def = ChallengeCatalog.Get(v);
            if (def == null || loadout == null) return false;
            int sum = 0;
            var axes = ChallengeCatalog.AxesOf(def.category);
            for (int i = 0; i < axes.Count; i++) sum += axes[i].PointsOf(loadout.GetTier(axes[i].axis));
            return sum >= ChallengeCatalog.CategoryBaseMax;
        }

        /// <summary>Loadout のスコアだけを知りたいとき (UI のプレビュー用)。</summary>
        public static int Score(ChallengeLoadout loadout) => Build(loadout).Score;

        /// <summary>定義表の自己検査。 起動時 or テストから呼ぶ。 問題があれば説明を返す (無ければ null)。</summary>
        public static string Validate()
        {
            var sb = new System.Text.StringBuilder();
            int baseSum = 0;
            for (int i = 0; i < ChallengeCatalog.Axes.Count; i++)
            {
                var d = ChallengeCatalog.Axes[i];
                baseSum += d.MaxPoints;
                if (d.tiers == null || d.tiers.Length == 0)
                { sb.Append($"{d.displayName}: 段が空\n"); continue; }
                // 昇順・重複なし。 番号 = 点数なので、 これは点数の昇順検査でもある。
                for (int k = 1; k < d.tiers.Length; k++)
                    if (d.tiers[k] <= d.tiers[k - 1])
                        sb.Append($"{d.displayName}: Tier が昇順でない ({string.Join(",", d.tiers)})\n");
                if (d.tiers[0] < 1)
                    sb.Append($"{d.displayName}: Tier が 1 未満 ({d.tiers[0]})\n");
                // **Tier4 は基礎軸に置けない。** 解禁条件付きの専用段なので。
                if (d.MaxTier > ChallengeCatalog.AxisMaxPoints)
                    sb.Append($"{d.displayName}: 最上位が T{d.MaxTier} (基礎軸の上限 T{ChallengeCatalog.AxisMaxPoints})\n");
            }
            if (baseSum != ChallengeCatalog.BaseMaxScore)
                sb.Append($"軸の満点が {baseSum} (期待 {ChallengeCatalog.BaseMaxScore})\n");

            int t4Sum = 0;
            var seenCat = new HashSet<ChallengeCategory>();
            for (int i = 0; i < ChallengeCatalog.T4s.Count; i++)
            {
                var d = ChallengeCatalog.T4s[i];
                t4Sum += d.points;
                if (!seenCat.Add(d.category)) sb.Append($"{d.displayName}: カテゴリ重複\n");
                if (d.points != ChallengeCatalog.T4Points)
                    sb.Append($"{d.displayName}: 点数が {ChallengeCatalog.T4Points}pt でない\n");
            }
            if (baseSum + t4Sum != ChallengeCatalog.TotalMaxScore)
                sb.Append($"総満点が {baseSum + t4Sum} (期待 {ChallengeCatalog.TotalMaxScore})\n");

            // 軸数は不揃いでよいが、 **カテゴリの基礎満点は 6pt** で揃える。
            foreach (ChallengeCategory cat in System.Enum.GetValues(typeof(ChallengeCategory)))
            {
                var axes = ChallengeCatalog.AxesOf(cat);
                if (axes.Count < 2) sb.Append($"{cat}: 軸が {axes.Count} 個 (最低 2)\n");
                if (ChallengeCatalog.CategoryMaxScore(cat) != ChallengeCatalog.CategoryBaseMax)
                    sb.Append($"{cat}: 基礎満点が {ChallengeCatalog.CategoryMaxScore(cat)}pt "
                            + $"(期待 {ChallengeCatalog.CategoryBaseMax})\n");
            }

            // **Tier4 は解禁条件を満たさないと入らない。** T4 だけ選んでも 0pt であること。
            var t4Only = new ChallengeLoadout();
            for (int i = 0; i < ChallengeCatalog.T4s.Count; i++) t4Only.SetT4(ChallengeCatalog.T4s[i].t4, true);
            int t4OnlyScore = Build(t4Only).Score;
            if (t4OnlyScore != 0)
                sb.Append($"T4 のみ選択が {t4OnlyScore}pt (期待 0 ── 解禁条件未達は無効)\n");

            // 全部盛り = 全軸を最上位 + 全 T4 = 満点になること。
            var full = new ChallengeLoadout();
            for (int i = 0; i < ChallengeCatalog.Axes.Count; i++)
                full.SetTier(ChallengeCatalog.Axes[i].axis, ChallengeCatalog.Axes[i].MaxTier);
            for (int i = 0; i < ChallengeCatalog.T4s.Count; i++) full.SetT4(ChallengeCatalog.T4s[i].t4, true);
            int fullScore = Build(full).Score;
            if (fullScore != ChallengeCatalog.TotalMaxScore)
                sb.Append($"全部盛りの解決後スコアが {fullScore} (期待 {ChallengeCatalog.TotalMaxScore})\n");

            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}

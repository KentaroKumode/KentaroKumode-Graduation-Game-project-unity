using MetaProgression.Relics;

namespace AutoTest
{
    /// <summary>
    /// 遺物の効用関数。 周回モード（<see cref="AscensionLoop"/>）が「乗り換えるか」を決めるのに使う。
    ///
    /// **総点そのままでは測れない。** 枠が増えるほど総点は上がるが、増えた枠が要らない軸に
    /// 配られていれば実際の強さは変わらない。 §15-5 の「層の支配性」を検証したときも、
    /// 期待総点で比べると逆転して見えて、効用で測り直して初めて正しい順序になった。
    ///
    /// そこで **ペルソナごとに軸へ重みを置き、段 × 重み の総和**を効用とする。
    /// 基準は「全軸 1.0 ＝ 総点と一致」で、ペルソナの選好だけが重みを動かす。
    ///
    /// 重みは設計上の想定であって実測値ではない。 **結論が重みに依存する可能性がある**ので、
    /// 周回モードの結果を読むときは「この重みのもとでの話」であることを忘れないこと。
    /// </summary>
    public static class RelicUtility
    {
        /// <summary>臨界軸は**臨界パッシブを持っていないと効果が 0**（メーターが動かないため）。
        /// Rinkai ペルソナ以外はパッシブを引く保証がないので、期待値として大きく割り引く。</summary>
        private const float RinkaiWithoutBuild = 0.3f;

        /// <summary>そのペルソナにとっての軸の重み。 1.0 が基準（＝総点と同じ扱い）。</summary>
        public static float Weight(RelicAxis axis, BuildPersona persona)
        {
            // 会心排他ビルドでは会心系が**完全に無価値**。 12 軸のうち 2 軸が死ぬ。
            if (persona == BuildPersona.Bludgeon &&
                (axis == RelicAxis.CritRatePct || axis == RelicAxis.CritMultPct))
                return 0f;

            // 臨界は専用パッシブが要る。 Rinkai ビルド以外では期待値を大きく落とす。
            if (axis == RelicAxis.Rinkai && persona != BuildPersona.Rinkai)
                return RinkaiWithoutBuild;

            switch (persona)
            {
                case BuildPersona.Standard:
                    if (axis == RelicAxis.Attack || axis == RelicAxis.DamagePct) return 1.3f;
                    break;
                case BuildPersona.Crit:
                    if (axis == RelicAxis.CritRatePct) return 2.0f;
                    if (axis == RelicAxis.CritMultPct) return 2.0f;
                    if (axis == RelicAxis.DamagePct)   return 1.2f;
                    break;
                case BuildPersona.Bleed:
                    if (axis == RelicAxis.OpeningBleed) return 2.5f;
                    break;
                case BuildPersona.Rinkai:
                    if (axis == RelicAxis.Rinkai) return 3.0f;   // 専用パッシブ前提なので跳ねる
                    break;
                case BuildPersona.Poison:
                    if (axis == RelicAxis.OpeningPoison) return 2.5f;
                    break;
                case BuildPersona.Bludgeon:
                    // 会心を捨てた分を素の火力で取り返す
                    if (axis == RelicAxis.Attack)    return 1.5f;
                    if (axis == RelicAxis.DamagePct) return 1.5f;
                    break;
                case BuildPersona.Charge:
                    if (axis == RelicAxis.Charge) return 2.5f;
                    break;
                case BuildPersona.Shield:
                    if (axis == RelicAxis.OpeningShield)      return 2.0f;
                    if (axis == RelicAxis.DamageReductionPct) return 1.5f;
                    if (axis == RelicAxis.MaxHp)              return 1.5f;
                    break;
                case BuildPersona.Berserk:
                    // 低HP を維持して火力を出す型なので、最大HP はむしろ邪魔寄り
                    if (axis == RelicAxis.MaxHp)      return 0.5f;
                    if (axis == RelicAxis.DamagePct)  return 1.5f;
                    break;
                default:
                    break;   // RawTier は中立 (全軸 1.0)
            }
            return 1.0f;
        }

        /// <summary>遺物の効用。 段 × 重み の総和。
        /// **刻印は段 7 として数えるが、条件を満たせない確率は織り込まない** ──
        /// 条件は「守ろうと思えば常に守れる」ものに限ってあるため（§15-5）。</summary>
        public static float Score(RolledRelic relic, BuildPersona persona)
        {
            if (relic == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < relic.SlotCount; i++)
                sum += relic.EffectiveStepAt(i) * Weight(relic.AxisAt(i), persona);
            return sum;
        }

        /// <summary>効用の内訳を 1 行で。 レポート用。</summary>
        public static string Explain(RolledRelic relic, BuildPersona persona)
        {
            if (relic == null) return "遺物なし (効用 0.0)";
            var sb = new System.Text.StringBuilder();
            sb.Append($"効用 {Score(relic, persona):F1}  ({persona}) ");
            for (int i = 0; i < relic.SlotCount; i++)
            {
                float w = Weight(relic.AxisAt(i), persona);
                sb.Append(i == 0 ? " ★" : " / ");
                sb.Append($"{RelicAxisCatalog.NameOf(relic.AxisAt(i))}"
                        + $"{relic.EffectiveStepAt(i)}×{w:F1}");
            }
            return sb.ToString();
        }
    }
}

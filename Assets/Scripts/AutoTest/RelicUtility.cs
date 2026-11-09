using System.Collections.Generic;
using System.Text;
using MetaProgression.Relics;

namespace AutoTest
{
    /// <summary>
    /// 遺物の効用関数 (2026-11-10 再編)。 周回モード (<see cref="AscensionLoop"/>) が部位ごとに持ち込む遺物を選び、 強化する順を決めるのに使う。
    /// 効用 = Σ 段 × そのペルソナにとっての重み ＋ セットのボーナス。 重みは設計上の想定であって実測値ではない
    /// ── 周回モードの結果は「この重みのもとでの話」として読むこと。 バランス調整は別 issue。
    /// </summary>
    public static class RelicUtility
    {
        /// <summary>ペルソナ → 相性のよいセット (3 個揃えを狙う先)。</summary>
        public static RelicSet PreferredSet(BuildPersona p)
        {
            switch (p)
            {
                case BuildPersona.Crit:   return RelicSet.Shingan;
                case BuildPersona.Bleed:  return RelicSet.Kessho;
                case BuildPersona.Poison: return RelicSet.Shoki;
                case BuildPersona.Rinkai: return RelicSet.Roon;
                case BuildPersona.Charge: return RelicSet.Denryu;
                case BuildPersona.Shield: return RelicSet.Kenro;
                default:                  return RelicSet.Shingan;
            }
        }

        /// <summary>そのペルソナにとってのステータスの重み (1.0 が基準)。</summary>
        public static float Weight(RelicStat s, BuildPersona persona)
        {
            if (persona == BuildPersona.Bludgeon && (s == RelicStat.CritRate || s == RelicStat.CritMult)) return 0f;   // 会心排他
            var pref = PreferredSet(persona);
            // 軸の資源は、 その軸のビルドでなければ大きく割り引く
            switch (s)
            {
                case RelicStat.OpeningBleed:  case RelicStat.BloodOffering: return pref == RelicSet.Kessho ? 1.5f : 0.4f;
                case RelicStat.OpeningPoison: case RelicStat.EscalationDelay: return pref == RelicSet.Shoki ? 1.5f : 0.6f;
                case RelicStat.Rinkai:        return pref == RelicSet.Roon ? 1.5f : 0.3f;
                case RelicStat.Charge:        return pref == RelicSet.Denryu ? 1.5f : 0.6f;
                case RelicStat.ShieldGainPct: case RelicStat.BlockDicePct: return pref == RelicSet.Kenro ? 1.5f : 0.4f;
                case RelicStat.CritRate: case RelicStat.CritMult: return pref == RelicSet.Shingan ? 1.5f : 0.8f;
            }
            return RelicCatalog.IsLikely(pref, s) ? 1.2f : 1.0f;
        }

        /// <summary>1 個の効用 (セットのボーナスを除く)。</summary>
        public static float Score(Relic r, BuildPersona persona)
        {
            if (r == null) return 0f;
            float u = r.MainStep * Weight(r.MainStat, persona);
            for (int i = 0; i < r.SubCount; i++) u += r.subSteps[i] * Weight((RelicStat)r.subStats[i], persona);
            return u;
        }

        /// <summary>持ち込み 3 個の効用 (セット 2 個 +2・3 個 +5 を足す。 相性のよいセットなら 1.5 倍)。</summary>
        public static float ScoreLoadout(IList<Relic> eq, BuildPersona persona)
        {
            float u = 0f; var cnt = new int[RelicCatalog.SetCount];
            foreach (var r in eq) { if (r == null) continue; u += Score(r, persona); cnt[r.set]++; }
            var pref = PreferredSet(persona);
            for (int s = 0; s < cnt.Length; s++)
            {
                float b = cnt[s] >= 3 ? 5f : cnt[s] >= 2 ? 2f : 0f;
                u += (RelicSet)s == pref ? b * 1.5f : b;
            }
            return u;
        }

        /// <summary>所持品から部位ごとの持ち込みを選ぶ。 候補は「相性のセットで 3 個」「部位ごとに単体の最高」「相性のセット 2 個 ＋ 単体の最高」の 3 通り。</summary>
        public static Relic[] ChooseLoadout(IList<Relic> owned, BuildPersona persona)
        {
            var pref = (int)PreferredSet(persona);
            var best = new Relic[3]; var bestSet = new Relic[3];
            foreach (var r in owned)
            {
                if (r == null || !r.IsValid()) continue;
                if (best[r.slot] == null || Score(r, persona) > Score(best[r.slot], persona)) best[r.slot] = r;
                if (r.set == pref && (bestSet[r.slot] == null || Score(r, persona) > Score(bestSet[r.slot], persona))) bestSet[r.slot] = r;
            }
            var cands = new List<Relic[]> { best, bestSet };
            for (int k = 0; k < 3; k++)
            {
                var mix = (Relic[])bestSet.Clone(); mix[k] = best[k]; cands.Add(mix);
            }
            Relic[] pick = best; float pu = float.MinValue;
            foreach (var c in cands)
            {
                var l = new List<Relic>(); foreach (var r in c) if (r != null) l.Add(r);
                float u = ScoreLoadout(l, persona);
                if (u > pu) { pu = u; pick = c; }
            }
            return pick;
        }

        public static string Explain(IList<Relic> eq, BuildPersona persona)
        {
            var sb = new StringBuilder();
            foreach (var r in eq) if (r != null) sb.Append(r.Describe()).Append(" (u=").Append(Score(r, persona).ToString("F1")).Append(") ");
            sb.Append("計 ").Append(ScoreLoadout(eq, persona).ToString("F1"));
            return sb.ToString();
        }
    }
}

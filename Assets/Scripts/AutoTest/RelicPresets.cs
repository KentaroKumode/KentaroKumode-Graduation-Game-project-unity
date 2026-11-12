using System.Collections.Generic;
using MetaProgression.Relics;

namespace AutoTest
{
    /// <summary>
    /// AutoRunner 用の遺物プリセット (持ち込み 3 個の組)。 正本: docs/GAME.md §15-5 (2026-11-10 再編)。
    /// 遺物を固定しないと tier list が「どのプレイヤーを基準にした値か」を失う
    /// (MetaAllocationPresets が整備パネルに対して果たしている役割と同じ)。 乱数は使わない ── 比較可能性を保つため常に同じ組を返す。
    /// <para>セットは心眼 (会心・主攻撃) で固定。 どのビルドでも主攻撃は振るので、 セットの選び方で結果が割れにくい。</para>
    /// </summary>
    public static class RelicPresets
    {
        public enum Preset
        {
            /// <summary>遺物なし。 バランスの床。</summary>
            None,
            /// <summary>一通り使える頃 (★3・サブ 2 個 1 段・心眼 3 個)。 ボス調整の基準。</summary>
            Mid,
            /// <summary>★4・サブ 3 個 (出やすい 2 個が 2 段・残り 1 段)。</summary>
            High,
            /// <summary>理論値 (★4・+4・出やすいサブ 3 個がすべて 2 段・心眼 3 個)。 天井の測定用。</summary>
            TheoreticalBest,
            /// <summary>[旧] 刻印付きの理論最良。 刻印の廃止 (2026-11-10) で TheoreticalBest と同じ組を返す (呼び出し側の互換のため残す)。</summary>
            TheoreticalBestCursed,
        }

        public const RelicSet PresetSet = RelicSet.Shingan;

        public static List<Relic> Build(Preset p) => Build(p, PresetSet);

        public static List<Relic> Build(Preset p, RelicSet set)
        {
            if (p == Preset.None) return null;
            var list = new List<Relic>(3);
            for (int k = 0; k < RelicCatalog.SlotCount; k++)
            {
                var r = new Relic { slot = k, set = (int)set };
                var main = r.MainStat;
                // 出やすいサブ → 普通の順に、 主ステと重ならないものを 3 つ
                var subs = new List<RelicStat>();
                foreach (var s in RelicCatalog.SubPool) if (s != main && RelicCatalog.IsLikely(set, s)) subs.Add(s);
                foreach (var s in RelicCatalog.SubPool) if (s != main && !subs.Contains(s) && RelicCatalog.SubWeight(set, s) >= RelicCatalog.WeightNormal) subs.Add(s);
                switch (p)
                {
                    case Preset.Mid:
                        r.stars = 3; r.enhance = 0;
                        Add(r, subs[0], 1); Add(r, subs[1], 1);
                        break;
                    case Preset.High:
                        r.stars = 4; r.enhance = 2;
                        Add(r, subs[0], 2); Add(r, subs[1], 2); Add(r, subs[2], 1);
                        break;
                    default:   // TheoreticalBest / TheoreticalBestCursed
                        r.stars = 4; r.enhance = RelicCatalog.MaxEnhance;
                        Add(r, subs[0], 2); Add(r, subs[1], 2); Add(r, subs[2], 2);
                        break;
                }
                list.Add(r);
            }
            return list;
        }

        /// <summary>"プリセット:セット" を組にする (空・解釈できなければ null)。</summary>
        public static List<Relic> Parse(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            var parts = spec.Split(':');
            if (!System.Enum.TryParse(parts[0], true, out Preset p)) return null;
            var set = PresetSet;
            if (parts.Length > 1 && !System.Enum.TryParse(parts[1], true, out set)) return null;
            return Build(p, set);
        }

        private static void Add(Relic r, RelicStat s, int step) { r.subStats.Add((int)s); r.subSteps.Add(step); }

        /// <summary>段の合計 (3 個ぶん)。 表示用。</summary>
        public static int TotalSteps(List<Relic> list)
        {
            int n = 0; if (list != null) foreach (var r in list) n += r.TotalSteps; return n;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using GameLoop;
using UnityEngine;

namespace MapSystem.AbyssPhenomena
{
    /// <summary>
    /// 層突入時の異常現象抽選。 種別の重みは現在の希望に線形連動する。
    /// 算式: P(BUFF) = 20 + 0.30·h、 P(MIXED) = 30、 P(DEBUFF) = 50 − 0.30·h (h = 希望 0〜100)
    /// 正本: docs/specs/abyss-phenomena.md §抽選ルール
    /// </summary>
    public static class AbyssPhenomenonRoller
    {
        /// <summary>2 件抽選される確率。</summary>
        public const float TwoRollChance = 0.30f;

        /// <summary>層突入時に呼ぶ。 1〜2 件を抽選し run.activePhenomena に格納する。</summary>
        public static void RollForFloor(RunState run)
        {
            if (run == null) return;
            run.activePhenomena.Clear();
            run.reverseFallsUsed = false;

            var first = RollOne(run);
            if (first == AbyssPhenomenon.None) return;
            run.activePhenomena.Add(first);

            if (Random.value < TwoRollChance)
            {
                var exclude = new HashSet<AbyssPhenomenon> { first };
                // MIXED + MIXED は禁止 (中立同士の重複を避ける)
                AbyssPhenomenonKind? forbid = AbyssPhenomenonDatabase.Get(first).kind == AbyssPhenomenonKind.Mixed
                    ? (AbyssPhenomenonKind?)AbyssPhenomenonKind.Mixed
                    : null;
                var second = RollOne(run, exclude, forbid);
                if (second != AbyssPhenomenon.None) run.activePhenomena.Add(second);
            }
        }

        /// <summary>1 件抽選。 種別を希望連動で選び、 種別内のエントリは均等抽選。</summary>
        public static AbyssPhenomenon RollOne(RunState run, HashSet<AbyssPhenomenon> exclude = null, AbyssPhenomenonKind? forbidKind = null)
        {
            int h = Mathf.Clamp(run?.hope ?? 0, 0, HopeSystem.HopeMax);
            float pBuff   = 20f + 0.30f * h;
            float pMixed  = 30f;
            float pDebuff = 50f - 0.30f * h;

            var kind = RollKind(pBuff, pMixed, pDebuff, forbidKind);

            var candidates = AbyssPhenomenonDatabase.All
                .Where(p => AbyssPhenomenonDatabase.Get(p).kind == kind
                            && (exclude == null || !exclude.Contains(p)))
                .ToList();

            if (candidates.Count == 0) return AbyssPhenomenon.None;
            return candidates[Random.Range(0, candidates.Count)];
        }

        private static AbyssPhenomenonKind RollKind(float pBuff, float pMixed, float pDebuff, AbyssPhenomenonKind? forbid)
        {
            if (forbid == AbyssPhenomenonKind.Buff)   pBuff = 0f;
            if (forbid == AbyssPhenomenonKind.Mixed)  pMixed = 0f;
            if (forbid == AbyssPhenomenonKind.Debuff) pDebuff = 0f;

            float total = pBuff + pMixed + pDebuff;
            if (total <= 0f) return AbyssPhenomenonKind.Mixed;

            float r = Random.Range(0f, total);
            if (r < pBuff) return AbyssPhenomenonKind.Buff;
            r -= pBuff;
            if (r < pMixed) return AbyssPhenomenonKind.Mixed;
            return AbyssPhenomenonKind.Debuff;
        }
    }
}

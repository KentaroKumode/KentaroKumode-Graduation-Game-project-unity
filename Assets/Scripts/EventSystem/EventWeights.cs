using GameLoop;
using MapSystem.FreeMove;

namespace EventSystem
{
    /// <summary>
    /// イベントの出やすさの重み (2026-11-12・docs/GAME.md §17「出やすさの重み」)。 **倍率と閾値はここ 1 か所**。
    ///
    /// <para>イベントの出現条件に札 (「重み[追跡]」など) を書くと、 今の状況で札の条件が立っている時だけ、
    /// 抽選の重み (稀 = 0.33・ふつう = 1) にこの倍率が掛かる。 札が複数なら掛け合わせ。
    /// 条件の判定は汎用の状態 (補足・魔石の反応・明かした点の割合・素材の手持ち) だけを読み、 イベントの名前は見ない。</para>
    ///
    /// <para>数値は全部仮置き・未測定 (イベントはまだ測定に戻していない)。 重みは乱数の消費本数を変えない
    /// (<see cref="EventDatabase.Pick"/> は重みに関わらず 2 本引く) ので、 同一シードのペア比較は保たれる。</para>
    /// </summary>
    public static class EventWeights
    {
        /// <summary>追跡者を撒く・遠ざける系。 補足されている時ほど出やすい。</summary>
        public const string TagPursuit = "追跡";
        /// <summary>地図を明かす系。 この層で明かした点が少ないほど出やすい。</summary>
        public const string TagMap = "地図";
        /// <summary>素材を払う系。 素材の手持ちが多いほど出やすい。</summary>
        public const string TagMaterial = "素材";

        // ── 追跡 ──
        /// <summary>補足されている (追跡者・猟犬・使い魔に追われている) 時の倍率。</summary>
        public const float PursuitChasedMul = 3f;
        /// <summary>補足はされていないが、 追跡者が魔石の反応する距離 (道で <see cref="FreeMapParams.StoneFar"/> 歩以内) に居る時の倍率。
        /// 縄張りを出ない番人 (既定) では、 自分が縄張りの中に居る時だけ (外では補足されないので・2026-11-13)。</summary>
        public const float PursuitNearMul = 2f;

        // ── 地図 ──
        /// <summary>この層で種類が分かっている点の割合がこれ未満なら <see cref="MapLowMul"/>。</summary>
        public const float MapLowFraction = 0.3f;
        public const float MapLowMul = 2f;

        // ── 素材 ──
        /// <summary>素材の手持ちがこれ以上なら <see cref="MaterialSomeMul"/>、 <see cref="MaterialManyAt"/> 以上なら <see cref="MaterialManyMul"/>。
        /// 1 ランの素材は約 56 入って 16 しか使われない (§17 の値段表)。</summary>
        public const int MaterialSomeAt = 10;
        public const float MaterialSomeMul = 1.5f;
        public const int MaterialManyAt = 20;
        public const float MaterialManyMul = 2f;

        public static bool IsKnownTag(string tag) => tag == TagPursuit || tag == TagMap || tag == TagMaterial;

        /// <summary>このイベントの重みに掛ける倍率 (札が無ければ 1)。 sim は航行マップ (無い層では null)。</summary>
        public static float Multiplier(EventCondition c, RunState run, FreeMapSim sim)
        {
            if (c?.weightTags == null || c.weightTags.Count == 0) return 1f;
            float mul = 1f;
            foreach (var tag in c.weightTags) mul *= TagMultiplier(tag, run, sim);
            return mul;
        }

        public static float TagMultiplier(string tag, RunState run, FreeMapSim sim)
        {
            switch (tag)
            {
                case TagPursuit:
                {
                    if (sim == null || !sim.FoeAlive) return 1f;
                    if (sim.Eta().Valid) return PursuitChasedMul;
                    // 補足されていないが近い: 補足は縄張りの中でしか起きないので、 縄張りの外に居る時は掛けない (2026-11-13・縄張りの番人)
                    return sim.Stone().level >= 1 && !sim.OutsideFoeReach ? PursuitNearMul : 1f;
                }
                case TagMap:
                    return sim != null && sim.KnownFraction < MapLowFraction ? MapLowMul : 1f;
                case TagMaterial:
                {
                    int m = run != null ? run.weaponMaterials : 0;
                    return m >= MaterialManyAt ? MaterialManyMul : m >= MaterialSomeAt ? MaterialSomeMul : 1f;
                }
            }
            return 1f;
        }
    }
}

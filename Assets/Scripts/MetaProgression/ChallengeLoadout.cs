using System.Collections.Generic;

namespace MetaProgression
{
    /// <summary>
    /// プレイヤーが組んだ挑戦デバフの構成。 **セーブ対象はこれだけ。**
    ///
    /// **効果値を持たせないこと。** ここが持つのは「どの軸を Tier いくつで有効にしたか」だけで、
    /// −5 / −15 / −30 といった実効果は <see cref="ChallengeEffects"/> 側にある。
    /// 状態に効果値を焼き込むと、 バランス調整のたびにセーブ互換が壊れる。
    ///
    /// JsonUtility は Dictionary を非対応なので、 軸と Tier を 1 つの int へ詰めた並びで持つ。
    /// </summary>
    [System.Serializable]
    public class ChallengeLoadout
    {
        /// <summary>packed = (軸ID * 10 + Tier)。 1 軸につき最大 1 要素。</summary>
        public List<int> axisTiers = new List<int>();
        /// <summary>有効な <see cref="ChallengeT4"/> の値。</summary>
        public List<int> t4 = new List<int>();

        private static int Pack(ChallengeAxis axis, int tier) => (int)axis * 10 + tier;

        // ============================================================
        //  参照
        // ============================================================

        /// <summary>その軸の Tier。 未選択なら 0。</summary>
        public int GetTier(ChallengeAxis axis)
        {
            if (axisTiers == null) return 0;
            int baseId = (int)axis * 10;
            for (int i = 0; i < axisTiers.Count; i++)
            {
                int t = axisTiers[i] - baseId;
                if (t >= 1 && t <= 3) return t;
            }
            return 0;
        }

        public bool HasT4(ChallengeT4 v) => t4 != null && t4.Contains((int)v);

        // ============================================================
        //  編集 (UI / スイープ用)
        // ============================================================

        /// <summary>軸の Tier を設定する。 tier = 0 で解除。
        /// **軸内排他はここで保証する** ── 同じ軸の既存要素を必ず落としてから足す。
        /// 存在しない Tier を渡した場合は false を返して何もしない。</summary>
        public bool SetTier(ChallengeAxis axis, int tier)
        {
            var def = ChallengeCatalog.Get(axis);
            if (def == null) return false;
            if (tier != 0 && !def.HasTier(tier)) return false;

            if (axisTiers == null) axisTiers = new List<int>();
            int baseId = (int)axis * 10;
            for (int i = axisTiers.Count - 1; i >= 0; i--)
            {
                int t = axisTiers[i] - baseId;
                if (t >= 1 && t <= 3) axisTiers.RemoveAt(i);
            }
            if (tier != 0) axisTiers.Add(Pack(axis, tier));
            return true;
        }

        public void SetT4(ChallengeT4 v, bool on)
        {
            if (t4 == null) t4 = new List<int>();
            if (on) { if (!t4.Contains((int)v)) t4.Add((int)v); }
            else t4.Remove((int)v);
        }

        public void Clear()
        {
            axisTiers?.Clear();
            t4?.Clear();
        }

        public ChallengeLoadout Clone()
        {
            var c = new ChallengeLoadout();
            if (axisTiers != null) c.axisTiers = new List<int>(axisTiers);
            if (t4 != null)        c.t4        = new List<int>(t4);
            return c;
        }

        /// <summary>不正な要素 (未知の軸 / 存在しない Tier / 軸の重複) を落とす。
        /// セーブ復元直後に 1 回呼ぶ。 冪等。</summary>
        public void Sanitize()
        {
            if (axisTiers == null) axisTiers = new List<int>();
            if (t4 == null) t4 = new List<int>();

            var seen = new HashSet<int>();
            for (int i = axisTiers.Count - 1; i >= 0; i--)
            {
                int packed = axisTiers[i];
                int axisId = packed / 10;
                int tier   = packed % 10;
                var def = ChallengeCatalog.Get((ChallengeAxis)axisId);
                if (def == null || !def.HasTier(tier) || !seen.Add(axisId))
                    axisTiers.RemoveAt(i);
            }
            for (int i = t4.Count - 1; i >= 0; i--)
                if (ChallengeCatalog.Get((ChallengeT4)t4[i]) == null) t4.RemoveAt(i);
        }
    }
}

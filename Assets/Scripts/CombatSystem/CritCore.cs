namespace CombatSystem
{
    /// <summary>
    /// 会心の軸 (2026-11-21・full-item-rework-spec §3 軸6・full-item-rework-ledger §⑥) の式の<b>正本</b>。
    /// 本体 (PassiveSkillManager.ProcessDamage・CombatManager の撃の輪)・BOT の配線の見積もり・画面の表示は同じ関数を通る。
    ///
    /// <para><b>超過会心率</b>: 会心率が 100% を超えた分は、 1% ごとに会心倍率 +0.01 (〈寸鉄〉で +0.02) になる (会心した撃だけ)。</para>
    /// <para><b>確定会心の撃</b> (〈天眼の鍔〉): 会心率 100% 以上の撃が会心したら、 会心倍率 ×1.25 (超過の変換の後)。</para>
    /// <para>このファイルは UnityEngine に依存しない。</para>
    /// </summary>
    public static class CritCore
    {
        /// <summary>超過会心率 1 (= 100%) あたりの会心倍率 (1% につき +0.01) / 〈寸鉄〉の倍。</summary>
        public const float ExcessPerRate = 1f, SuntetsuExcessMul = 2f;
        /// <summary>〈天眼の鍔〉: 確定会心の撃の会心倍率 ×1.25。</summary>
        public const float HeavenSureMul = 1.25f;

        /// <summary>超過会心率で足す会心倍率 (<paramref name="critAdd"/> = 合算した会心率・1 = 100%)。</summary>
        public static float ExcessMul(float critAdd, bool suntetsu)
            => critAdd > 1f ? (critAdd - 1f) * ExcessPerRate * (suntetsu ? SuntetsuExcessMul : 1f) : 0f;

        /// <summary>会心した撃に実際に掛かる会心倍率。 順: 敵の会心耐性 (下限 1.0) → 超過会心率の変換 → 〈天眼の鍔〉の確定会心 ×1.25。</summary>
        public static float EffectiveMul(float critMul, float enemyReduction, float critAdd, bool suntetsu, bool heaven)
        {
            float m = critMul;
            if (enemyReduction > 0f) m = System.Math.Max(1f, m - enemyReduction);
            m += ExcessMul(critAdd, suntetsu);
            if (heaven && critAdd >= 1f) m *= HeavenSureMul;
            return m;
        }

        /// <summary>撃 <paramref name="h"/> (攻撃の端子 1 枚) に挿した目の一番大きい値 (挿していない撃・再起動の撃は元の端子の目・0 = 目なし)。 〈狙い目の刻み〉が読む。</summary>
        public static int HitFaceMax(TerminalOutcome o, int[] dice, int h)
        {
            if (o == null || dice == null || h < 0 || h >= o.attackHitSlot.Count) return 0;
            int slot = o.attackHitSlot[h], mx = 0;
            for (int i = 0; i < dice.Length && i < o.slotOfDie.Length; i++)
                if (o.slotOfDie[i] == slot && dice[i] > mx) mx = dice[i];
            return mx;
        }

        /// <summary>〈狙い目の刻み〉: この撃に挿した目が 5 以上なら会心率 +20%、 6 なら確定会心。</summary>
        public const int AimNeed = 5, AimSure = 6;
        public const float AimPct = 0.20f;
        public static float AimRate(int faceMax) => faceMax >= AimNeed ? AimPct : 0f;
        public static bool AimForce(int faceMax) => faceMax >= AimSure;

        /// <summary>〈必殺の照準器〉: もう 1 回発火した撃の会心倍率の上乗せ (会心率 100% 以上の時)。</summary>
        public const float SniperMulAdd = 1.0f;
    }
}

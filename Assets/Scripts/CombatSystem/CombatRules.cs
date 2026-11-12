using UnityEngine;
using ItemSystem.PassiveSkills.Effects;

namespace CombatSystem
{
    /// <summary>1 ターンの<b>物理的な規則の式</b>の正本 (2026-11-12・リファクタリング段 2 の 2-1)。
    ///
    /// <para>本体の効果クラス (<see cref="GuardCut"/>・シールドバッシュ・城壁・<see cref="Daybreak"/>・処刑人の烙印・
    /// 攻撃に挿した出目の上乗せ) と、 BOT の見積もり (<c>AutoTest.TurnRules</c> 経由の Optimal / Super) が
    /// <b>同じ関数</b>を呼ぶ。 以前は同じ式が本体と BOT に別々に書かれていて、 片方だけ直ると黙ってずれた。</para>
    ///
    /// <para><b>式の並び (掛ける順・切り上げ/切り捨ての位置) を変えないこと。</b> 小数の定数は const のまま持ち、
    /// 実行時の割り算 (v/100f など) に置き換えない (Mono の float の精度で結果が割れる)。</para></summary>
    public static class CombatRules
    {
        /// <summary>剣〈切り返し〉: ブロック合計 × <see cref="GuardCut.Rate"/> (切り上げ) を攻撃へ。</summary>
        public static int GuardCutBonus(int blockSum) => Mathf.CeilToInt(blockSum * GuardCut.Rate);

        /// <summary>盾〈シールドバッシュ〉: 使い切ったシールド × 率 (切り上げ) を攻撃へ。</summary>
        public static int BashBonus(int shield, float rate) => Mathf.CeilToInt(shield * rate);

        /// <summary>盾〈城壁〉: 完全に受け止めたら (ブロック ≥ 敵の攻撃)、 余ったブロック × 率 (切り捨て) をシールドへ。 受け止められなければ 0。</summary>
        public static int BulwarkGain(int block, int enemyAtk, float rate)
            => block >= enemyAtk ? Mathf.FloorToInt((block - enemyAtk) * rate) : 0;

        /// <summary>盾〈黎明〉: 敵の最大 HP を削る量 (最低 1)。 <paramref name="units"/> = 軽減前の敵の攻撃 / <see cref="Daybreak.PerUnit"/>。
        /// units が 0 以下のときに呼ばないこと (呼ぶ側が先に弾く)。</summary>
        public static int DaybreakCut(int enemyMaxHp, int units)
            => System.Math.Max(1, enemyMaxHp * Daybreak.PctPerUnit * units / 100);

        /// <summary>処刑人の烙印のしきい値 (最大 HP 比)。</summary>
        public const float ExecuteHpRatio = 0.20f;

        /// <summary>処刑人の烙印: 攻撃終了時の HP が最大の <see cref="ExecuteHpRatio"/> 以下なら即死。 HP が 0 より大きいかは呼ぶ側が見る。</summary>
        public static bool ExecuteKills(int hp, int maxHp) => hp <= Mathf.CeilToInt(maxHp * ExecuteHpRatio);

        /// <summary>攻撃に挿した出目への上乗せ (ctx.attackDicePct): 攻撃の出目の合計 × 率 (切り上げ)。</summary>
        public static int AttackDiceScaled(int attackSum, float pct) => Mathf.CeilToInt(attackSum * pct);
    }
}

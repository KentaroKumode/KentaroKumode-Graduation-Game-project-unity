using System;

namespace CombatSystem
{
    /// <summary>防御値 → 軽減 (ADR-0013・2026-10-28)。 <b>プレイヤーと敵で同じ式を使う。</b>
    ///
    /// <para>軽減率 R(D) = Rmax × D / (D + K)。 Rmax &lt; 1 なので、 積むほど実効 HP の伸びが落ちる。
    /// 素の D / (D + K) は軽減率だけ見ると逓減して見えるが、 実効 HP = 1 + D/K で線形に伸びるので逓減しない。</para>
    ///
    /// <para><b>貫通は防御値を固定値で引く</b> (<see cref="Effective"/>)。 余った貫通は何にもならない
    /// (旧〈利刃〉の余剰貫通→与ダメ転用は撤去・GAME.md §24)。</para>
    ///
    /// <para><b>整数で割り切る。</b> Rmax を float の 0.6 で持つと、 10 × (1 − 0.1) が 9.0000001 になって
    /// 切り上げで 10 に化ける境目が出る。 Rmax を分数で持ち、 切り上げは整数の割り算で行う。</para>
    ///
    /// <para>数値は全部仮置き (GAME.md §23-22)。 動かす時は採否を取ってから測定する。</para></summary>
    public static class DefenseFormula
    {
        /// <summary>軽減率の漸近線 Rmax = <see cref="RmaxNum"/> / <see cref="RmaxDen"/> (仮置き 0.6)。</summary>
        public const int RmaxNum = 3;
        public const int RmaxDen = 5;

        /// <summary>軽減率が Rmax の半分に届く防御値 (仮置き 10)。</summary>
        public const int K = 10;

        /// <summary>プレイヤーの初期防御 (仮置き 2)。 武器・アイテムの防御はこの上に足す。</summary>
        public const int PlayerBaseDefense = 2;

        /// <summary>防御を丸ごと無視する貫通 (役〈攻め〉T4)。 どんな防御値もこれより小さい。</summary>
        public const int IgnoreAll = 999;

        /// <summary>貫通を引いた後の防御値 (下限 0)。</summary>
        public static int Effective(int defense, int pen) => Math.Max(0, defense - Math.Max(0, pen));

        /// <summary>軽減率 (0 〜 Rmax)。 表示・BOT の見積り用。 ダメージの計算は <see cref="Apply"/> を使う。</summary>
        public static float Reduction(int defense)
            => defense <= 0 ? 0f : (float)RmaxNum * defense / (RmaxDen * (defense + K));

        /// <summary>防御で軽減した後のダメージ。 端数は切り上げ (1 以上のダメージは 0 にならない)。</summary>
        public static int Apply(int damage, int defense)
        {
            if (damage <= 0 || defense <= 0) return damage;
            // damage × (1 − R) = damage × (RmaxDen(D+K) − RmaxNum·D) / (RmaxDen(D+K))
            long den = (long)RmaxDen * (defense + K);
            long num = den - (long)RmaxNum * defense;
            return (int)((damage * num + den - 1) / den);
        }
    }
}

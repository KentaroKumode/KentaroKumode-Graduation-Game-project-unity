using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// ゴールドの支払いの窓口。 店・鍛冶・休憩の面外しなど、 ゴールドで払う所はここを通す。
    ///
    /// <para><b>2026-11-09: 物資払い (旧〈燈火〉r10・ゴールドの不足分を物資で 1:1) を撤去。</b>
    /// 物資 (航行の燃料) と金 (店) の境目がぼやけ、 さらに判定が r1 から通っていた。
    /// 呼び出し側の互換のために API の形だけ残し、 中身はゴールドだけで払う。</para>
    /// </summary>
    public static class ProvisionPayment
    {
        /// <summary>[計測] 互換のため残置 (常に 0)。</summary>
        public static long PaymentCount;
        /// <summary>[計測] 互換のため残置 (常に 0)。</summary>
        public static long ProvisionSpentTotal;
        public static void ResetStats() { PaymentCount = 0; ProvisionSpentTotal = 0; }

        /// <summary>払える額 (= 所持ゴールド)。</summary>
        public static int Affordable(RunState run) => run == null ? 0 : run.coins;

        /// <summary>[方策] BOT が払ってよい額 (= 所持ゴールド)。</summary>
        public static int PolicyAffordable(RunState run) => Affordable(run);

        /// <summary>ゴールドで払う。 足りなければ何もせず false。</summary>
        public static bool TryPay(RunState run, int price, string label)
        {
            if (run == null || price < 0) return false;
            if (price == 0) return true;
            if (run.coins < price) return false;
            run.coins -= price;
            run.coinsSpent += price;
            return true;
        }
    }
}

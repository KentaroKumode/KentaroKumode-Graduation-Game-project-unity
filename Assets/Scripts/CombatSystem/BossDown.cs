using System;

namespace CombatSystem
{
    /// <summary>
    /// ボスのダウン (HP の目盛り・2026-11-13・GAME.md §6-15) の<b>規則の正本</b>。
    ///
    /// <para>ボスの HP の棒に目盛り (最大 HP の割合の一覧・<see cref="BossParams.DownNotches"/>) を置く。 HP が目盛り以下になったターンの
    /// <b>次のターン</b>、 ボスはダウンする: 攻撃しない・受けるダメージ ×<see cref="TakenMul"/> (主攻撃・毒・出血・爆発・固定ダメージの枠・反射・品の直接、 全部)・
    /// 溜めていた大技は取り消し。 ダウンが明けると大技の周期は頭から (<see cref="Escalation.HeavyCycleOffset"/>)。</para>
    ///
    /// <para><b>連続</b>: 割った目盛りの数だけダウンのターンが続く (1 ターンで 2 つ割れば 2 ターン・ダウン中に次を割ればもう 1 ターン)。
    /// 上限で止めない。</para>
    ///
    /// <para>本体 (<c>CombatManager</c>) と BOT (<c>AutoRunner</c> の効用・先読み) が<b>同じ関数</b>を呼ぶ。 式を 2 か所に書かない。
    /// このファイルは UnityEngine に依存しない (Unity なしの検算 <c>Tools/rules_check</c> がそのままコンパイルする)。
    /// 小数の定数は const の float で持つ (実行時の割り算に置き換えない)。</para>
    /// </summary>
    public static class BossDown
    {
        /// <summary>ダウン中に受けるダメージの倍率。</summary>
        public const float TakenMul = 1.5f;

        /// <summary>目盛り <paramref name="frac"/> (最大 HP の割合) の HP。 HP がこれ<b>以下</b>になったら割れた。</summary>
        public static int NotchHp(int maxHp, float frac) => Math.Max(0, (int)(maxHp * frac));

        /// <summary>HP が <paramref name="hp"/> の時に割れている目盛りの数 (目盛りは大きい順・割れた数は減らない)。
        /// <paramref name="broken"/> = これまでに割った数。 <paramref name="notches"/> が null なら <paramref name="broken"/> のまま。</summary>
        public static int BrokenCount(float[] notches, int maxHp, int hp, int broken)
        {
            if (notches == null) return broken;
            while (broken < notches.Length && hp <= NotchHp(maxHp, notches[broken])) broken++;
            return broken;
        }

        /// <summary>次に割る目盛りの HP (−1 = もう目盛りが無い・ダウンの無い敵)。</summary>
        public static int NextNotchHp(float[] notches, int maxHp, int broken)
            => notches != null && broken >= 0 && broken < notches.Length ? NotchHp(maxHp, notches[broken]) : -1;

        /// <summary>このターンの終わりの HP が <paramref name="hpAfter"/> になった時、 次のターンがダウンか。
        /// <paramref name="pendingNow"/> = このターンの頭でダウンを 1 つ使った後に残っているダウンの数 (<see cref="BossDownState.pending"/>)。</summary>
        public static bool NextTurnIsDown(float[] notches, int maxHp, int broken, int pendingNow, int hpAfter)
            => notches != null && hpAfter > 0 && pendingNow + (BrokenCount(notches, maxHp, hpAfter, broken) - broken) > 0;

        /// <summary>受けるダメージにダウンの倍率を掛ける (<paramref name="down"/> = このターンはダウン・切り捨て)。</summary>
        public static int ApplyTaken(int dmg, bool down) => down && dmg > 0 ? (int)(dmg * TakenMul) : dmg;

        /// <summary>いまの戦闘のこのターンがダウンか (CombatManager がターンの頭に立て、 次のターンの頭・戦闘の始めと終わりに戻す)。
        /// 敵の HP を減らす口 (<c>HpLossDiag.Lose</c>・主攻撃の <c>ApplyWinDamageModifiers</c>・品の直接の削り) が <see cref="ApplyTakenNow"/> で読む。</summary>
        public static bool ActiveNow;

        /// <summary>このターンがダウンなら ×<see cref="TakenMul"/>。</summary>
        public static int ApplyTakenNow(int dmg) => ApplyTaken(dmg, ActiveNow);
    }

    /// <summary>1 戦闘 (ヴェスカは 1 段) のダウンの状態。 CombatManager が持つ (BOT は読むだけ)。 区分はすべて persistent (戦闘を通して持つ・形態の交代で作り直す)。</summary>
    public sealed class BossDownState
    {
        /// <summary>目盛り (最大 HP の割合・大きい順)。 null = ダウンしない敵 (または測定の口で止めた)。</summary>
        public float[] notches;
        /// <summary>割った目盛りの数。</summary>
        public int broken;
        /// <summary>まだ来ていないダウンのターンの数。</summary>
        public int pending;
        /// <summary>このターンはダウン。</summary>
        public bool downNow;

        public bool Enabled => notches != null && notches.Length > 0;

        /// <summary>戦闘の始め・形態の交代: 目盛りを入れ直し、 割った数と待ちを 0 に。</summary>
        public void Reset(float[] n) { notches = n != null && n.Length > 0 ? n : null; broken = 0; pending = 0; downNow = false; }

        /// <summary>ターンの頭: 待ちがあればこのターンをダウンにして 1 つ使う。 戻り値 = このターンはダウンか。</summary>
        public bool BeginTurn()
        {
            downNow = pending > 0;
            if (downNow) pending--;
            return downNow;
        }

        /// <summary>ターンの終わり: HP から新しく割った目盛りを数え、 その数だけダウンを待ちに積む。 戻り値 = 新しく割った数。 倒れた (HP ≤ 0) なら数えない。</summary>
        public int NoteTurnEnd(int hp, int maxHp)
        {
            if (!Enabled || hp <= 0) return 0;
            int nb = BossDown.BrokenCount(notches, maxHp, hp, broken);
            int d = nb - broken;
            broken = nb; pending += d;
            return d;
        }

        /// <summary>次に割る目盛りの HP (−1 = 無し)。</summary>
        public int NextNotchHp(int maxHp) => BossDown.NextNotchHp(notches, maxHp, broken);
    }
}

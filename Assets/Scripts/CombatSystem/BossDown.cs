using System;

namespace CombatSystem
{
    /// <summary>
    /// ボスのダウン (HP の目盛り・2026-11-13・GAME.md §6-15) の<b>規則の正本</b>。
    ///
    /// <para>ボスの HP の棒に目盛り (最大 HP の割合の一覧・<see cref="BossParams.DownNotches"/>) を置く。 HP が目盛り以下になったターンの
    /// <b>次のターン</b>、 ボスはダウンする: 攻撃しない・受けるダメージ ×<see cref="TakenMul"/> (主攻撃・毒・出血・爆発・固定ダメージの枠・反射・品の直接、 全部)・
    /// ダウンのターンが大技の<b>チャージのターン (大技の 1 ターン前・画面に「次: 大技」) か大技のターン</b>に重なった時だけ、 その大技は取り消し、
    /// 周期はダウンの次のターンから頭に戻す (<see cref="Escalation.HeavyCycleOffset"/>)。 それ以外のダウンは、 そのターンの敵の行動を 1 つ潰すだけで、
    /// 周期は進み続ける (2026-11-14・作者の決定・<see cref="CancelsCycle"/>)。</para>
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

        /// <summary>目盛りの並び <paramref name="notches"/> (大きい順) に <paramref name="extra"/> を 1 つ足した新しい並び (大きい順を保つ)。
        /// <paramref name="notches"/> が null (ダウンの無い敵・測定の口で止めた) なら null のまま ── 目盛りの無い戦闘に目盛りを生やさない。
        /// 元の配列は書き換えない (<c>BossParams</c> の表を共有しているため)。 使う所: ヴェスカの「止まらない時計」(<c>BossParams.DownNotches</c>)。</summary>
        public static float[] WithExtraNotch(float[] notches, float extra)
        {
            if (notches == null || notches.Length == 0) return notches;
            var r = new float[notches.Length + 1];
            int j = 0; bool put = false;
            for (int i = 0; i < notches.Length; i++)
            {
                if (!put && extra > notches[i]) { r[j++] = extra; put = true; }
                r[j++] = notches[i];
            }
            if (!put) r[j] = extra;
            return r;
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

        /// <summary>ダウンのターンが、 敵の周期の山場を取り消すか (= 周期の起点をそのターンへ置き直すか・2026-11-14 の作者の決定)。
        /// 引数は、 起点を動かす前の周期で数えた「山場まで何ターンか」(0 = このターンが山場・1 = 次のターンが山場 = チャージのターン・−1 = その周期を持たない)。
        /// 山場は 大技 (<see cref="Escalation.TurnsToHeavy"/>)・構え (<see cref="Escalation.TurnsToGuard"/>)・灰燼の王〈業火の予兆〉の予兆のターン。
        /// <list type="bullet">
        /// <item><see cref="DownCancelRule.ChargeOrHeavy"/> (既定): どれかの山場が 0 か 1 の時だけ取り消す。 それ以外は周期に触らない
        /// (小技のターンのダウンは、 その小技を潰すだけ ── 次のターンは予定どおりチャージ、 その次に大技)。</item>
        /// <item><see cref="DownCancelRule.Always"/>: 2026-11-13〜11-14 の旧い規則。 どのダウンでも起点を置き直す (測定の口)。</item>
        /// </list>
        /// 起点は 1 つ (<see cref="Escalation.HeavyCycleOffset"/>) なので、 取り消すと、 敵が持つ周期は全部そろって頭に戻る。
        /// 本体 (<c>CombatManager.BeginBossDownTurn</c>) と BOT の見積もり (<c>AutoRunner</c>) が同じ関数を呼ぶ。</summary>
        public static bool CancelsCycle(DownCancelRule rule, int turnsToHeavy, int turnsToGuard, int turnsToOmen)
            => rule == DownCancelRule.Always || IsChargeOrClimax(turnsToHeavy) || IsChargeOrClimax(turnsToGuard) || IsChargeOrClimax(turnsToOmen);

        /// <summary>「山場まで何ターンか」が、 山場のターン (0) か、 その 1 ターン前のチャージ (1) か。</summary>
        public static bool IsChargeOrClimax(int turnsTo) => turnsTo == 0 || turnsTo == 1;
    }

    /// <summary>ダウンが敵の周期を取り消す条件 (<see cref="BossDown.CancelsCycle"/>・測定の口 <c>GameHooks.DownCancel</c> / <c>AutoRunner.downCancelRule</c>)。</summary>
    public enum DownCancelRule
    {
        /// <summary>既定 (2026-11-14〜): ダウンがチャージのターンか山場 (大技・構え・予兆) のターンに重なった時だけ取り消し、 周期を頭に戻す。</summary>
        ChargeOrHeavy = 0,
        /// <summary>旧い規則 (2026-11-13〜11-14): どのダウンでも周期を頭に戻す。</summary>
        Always = 1,
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
        /// <summary>このターンのダウンが大技を取り消した (<see cref="BossDown.CancelsCycle"/> が真で、 敵が大技を持つ)。 CombatManager が立てる・画面が読む。</summary>
        public bool heavyCanceledNow;

        public bool Enabled => notches != null && notches.Length > 0;

        /// <summary>戦闘の始め・形態の交代: 目盛りを入れ直し、 割った数と待ちを 0 に。</summary>
        public void Reset(float[] n) { notches = n != null && n.Length > 0 ? n : null; broken = 0; pending = 0; downNow = false; heavyCanceledNow = false; }

        /// <summary>ターンの頭: 待ちがあればこのターンをダウンにして 1 つ使う。 戻り値 = このターンはダウンか。</summary>
        public bool BeginTurn()
        {
            heavyCanceledNow = false;
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

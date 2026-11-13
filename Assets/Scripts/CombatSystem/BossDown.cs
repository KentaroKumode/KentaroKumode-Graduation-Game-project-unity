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
    /// <para><b>割れ方 (2026-11-14・作者の決定・<see cref="DownBreakRule"/>)</b>: 目盛りを割れるのはプレイヤーの<b>通常攻撃 (主攻撃)</b> だけ
    /// (主攻撃が 1 以上のダメージで当たった直後の HP が、 まだ割っていない目盛り以下なら、 その目盛りが全部割れる)。
    /// 通常攻撃以外 (毒・出血・爆発・固定ダメージ・反射・品の直接・ターン終了時の削り・ヴェスカの〈裂け目〉) で下回った目盛りには、
    /// <b>ターンの終わりに HP が目盛り以下ならひびが入り</b> (そのターンのどこで下回ったかは問わない)、 ひびが入ったターンから数えて
    /// 猶予 (<see cref="CrackGraceTurns"/>・既定 1) ターン後のターンの終わりに、 まだ割れていなければ<b>ひとりでに割れる</b> (猶予 1 = 次のターンの終わり)。
    /// その間に通常攻撃が当たれば、 その時点で普通に割れる。 ターンの終わりに HP が目盛りの上へ戻っていれば (回復)、 ひびは消える。
    /// ひとりでに割れた目盛りのダウンは<b>弱い</b>: 敵は攻撃しないが、 受けるダメージ ×<see cref="TakenMul"/> は付かない (口 downCrackWeak)。
    /// 口 downBreakRule = AnyDamage で旧い判定 (ターンの終わりの HP で割る・ひびなし・全部 満額のダウン)。</para>
    ///
    /// <para>本体 (<c>CombatManager</c>) と BOT (<c>AutoRunner</c> の効用・先読み) が<b>同じ関数</b>を呼ぶ。 式を 2 か所に書かない。
    /// このファイルは UnityEngine に依存しない (Unity なしの検算 <c>Tools/rules_check</c> がそのままコンパイルする)。
    /// 小数の定数は const の float で持つ (実行時の割り算に置き換えない)。</para>
    /// </summary>
    public static class BossDown
    {
        /// <summary>ダウン中に受けるダメージの倍率 (満額のダウンだけ・ひびで割れた弱いダウンには付かない)。</summary>
        public const float TakenMul = 1.5f;

        /// <summary>ひびの猶予の既定 (ターン数・2026-11-14)。 ひびが入ったターンから数えてこのターン数の後のターンの終わりに割れる
        /// (0 = ひびが入ったターンの終わりにすぐ割れる・1 = 次のターンの終わり)。 負 = ひびでは割れない。 測定の口 downCrackGrace。</summary>
        public const int CrackGraceTurns = 1;

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

        /// <summary>[旧い判定 AnyDamage] このターンの終わりの HP が <paramref name="hpAfter"/> になった時、 次のターンがダウンか。 新しい判定は <see cref="ForecastTurn"/>。
        /// <paramref name="pendingNow"/> = このターンの頭でダウンを 1 つ使った後に残っているダウンの数 (<see cref="BossDownState.pending"/>)。</summary>
        public static bool NextTurnIsDown(float[] notches, int maxHp, int broken, int pendingNow, int hpAfter)
            => notches != null && hpAfter > 0 && pendingNow + (BrokenCount(notches, maxHp, hpAfter, broken) - broken) > 0;

        /// <summary>受けるダメージにダウンの倍率を掛ける (<paramref name="down"/> = このターンはダウン・切り捨て)。</summary>
        public static int ApplyTaken(int dmg, bool down) => down && dmg > 0 ? (int)(dmg * TakenMul) : dmg;

        /// <summary>いまの戦闘のこのターンがダウンか (種類を問わない・CombatManager がターンの頭に立て、 次のターンの頭・戦闘の始めと終わりに戻す)。
        /// 「ダウン中は発動しない」を名乗ったパッシブの止め (<c>PassiveSkillManager.FireEnemyTrigger</c>) が読む。</summary>
        public static bool ActiveNow;

        /// <summary>このターンのダウンが受けるダメージ ×<see cref="TakenMul"/> を持つか (満額のダウン。 ひびで割れた弱いダウンは false・2026-11-14)。
        /// 敵の HP を減らす口 (<c>HpLossDiag.Lose</c>・主攻撃の <c>ApplyWinDamageModifiers</c>・品の直接の削り) が <see cref="ApplyTakenNow"/> で読む。</summary>
        public static bool TakenMulNow;

        /// <summary>このターンが満額のダウンなら ×<see cref="TakenMul"/>。</summary>
        public static int ApplyTakenNow(int dmg) => ApplyTaken(dmg, TakenMulNow);

        // ---- 割れ方の見込み (本体の BossDownState と同じ規則を、 状態を書き換えずに 1 ターン分だけ進める・BOT が読む) ----

        /// <summary><see cref="ForecastTurn"/> の答え。</summary>
        public struct Forecast
        {
            /// <summary>通常攻撃で割れる数 (満額のダウン) / ひびの猶予が切れて割れる数 / ターンの後の割った目盛りの数。</summary>
            public int fullAdded, crackAdded, brokenAfter;
            /// <summary>次のターンはダウンか / そのダウンは弱いか (被ダメ ×1.5 なし)。</summary>
            public bool nextDown, nextWeak;
            /// <summary>ターンの後の、 目盛りごとのひびの入ったターン (−1 = ひびなし・割れた目盛りも −1・wantCracks の時だけ)。</summary>
            public int[] cracksAfter;
        }

        /// <summary>このターンに、 主攻撃 <paramref name="mainDmg"/> (HP に入る量) が当たり、 その後に通常攻撃以外 <paramref name="afterMainDmg"/> が入った時の、 目盛りの割れ方
        /// (新しい判定では <paramref name="afterMainDmg"/> が負 = 差し引きの回復も読む・ひびが消える)。
        /// <paramref name="hpStart"/> = 主攻撃の前の HP / <paramref name="pendingNow"/> = このターンの頭でダウンを 1 つ使った後に残っている数 /
        /// <paramref name="pendingHeadWeak"/> = 残っているダウンの先頭が弱いか / <paramref name="crackTurns"/> = 目盛りごとのひびの入ったターン (−1 = なし・null 可) /
        /// <paramref name="turn"/> = このターンの番号。 <see cref="DownBreakRule.AnyDamage"/> は旧い判定 (<see cref="NextTurnIsDown"/> と同じ・ひびなし・全部 満額)。
        /// 本体の <see cref="BossDownState.NoteMainHit"/> → <see cref="BossDownState.NoteTurnEndCracks"/> と同じ規則 (検算 Tools/rules_check)。</summary>
        public static Forecast ForecastTurn(DownBreakRule rule, int grace, bool crackWeak, float[] notches, int maxHp, int broken, int pendingNow, bool pendingHeadWeak,
                                            int[] crackTurns, int turn, int hpStart, int mainDmg, int afterMainDmg, bool wantCracks = false)
        {
            var f = new Forecast { brokenAfter = broken };
            if (notches == null) return f;
            mainDmg = Math.Max(0, mainDmg);
            if (rule == DownBreakRule.AnyDamage)
            {
                afterMainDmg = Math.Max(0, afterMainDmg);
                int hpAfter = hpStart - mainDmg - afterMainDmg;
                int nb = BrokenCount(notches, maxHp, hpAfter, broken);
                f.brokenAfter = nb; f.fullAdded = nb - broken;
                f.nextDown = hpAfter > 0 && pendingNow + f.fullAdded > 0;
                return f;
            }
            int hpMain = hpStart - mainDmg, hpEnd = Math.Min(maxHp, hpMain - afterMainDmg);   // 負の afterMainDmg = 差し引きの回復 (最大 HP まで)
            int b = broken;
            if (mainDmg > 0 && hpMain > 0) { int nb = BrokenCount(notches, maxHp, hpMain, b); f.fullAdded = nb - b; b = nb; }
            int[] after = wantCracks ? new int[notches.Length] : null;
            if (after != null) for (int i = 0; i < after.Length; i++) after[i] = i < b ? -1 : (crackTurns != null && i < crackTurns.Length ? crackTurns[i] : -1);
            if (hpEnd > 0)
            {
                bool expiring = grace >= 0;
                for (int i = b; i < notches.Length; i++)
                {
                    if (hpEnd <= NotchHp(maxHp, notches[i]))
                    {
                        int ct = crackTurns != null && i < crackTurns.Length && crackTurns[i] >= 0 ? crackTurns[i] : turn;
                        if (expiring && turn - ct >= grace) { f.crackAdded++; if (after != null) after[i] = -1; continue; }
                        expiring = false;   // 割れるのは上から順 (割れない目盛りの下は、 このターンには割れない)
                        if (after != null) after[i] = ct;
                    }
                    else { expiring = false; if (after != null) after[i] = -1; }   // 目盛りの上 (回復で戻った) = ひびは消える
                }
            }
            f.brokenAfter = b + f.crackAdded;
            f.nextDown = hpEnd > 0 && pendingNow + f.fullAdded + f.crackAdded > 0;
            f.nextWeak = f.nextDown && (pendingNow > 0 ? pendingHeadWeak : f.fullAdded == 0 && crackWeak);
            f.cracksAfter = after;
            return f;
        }

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

    /// <summary>目盛りの割れ方 (<see cref="BossDown.ForecastTurn"/>・測定の口 <c>GameHooks.DownBreak</c> / <c>AutoRunner.downBreakRule</c>)。</summary>
    public enum DownBreakRule
    {
        /// <summary>既定 (2026-11-14〜): 通常攻撃 (主攻撃) が当たった時だけ割れる。 通常攻撃以外で下回った目盛りにはひびが入り、 猶予の後にひとりでに割れる (弱いダウン)。</summary>
        MainAttackOnly = 0,
        /// <summary>旧い判定 (2026-11-13〜11-14): ターンの終わりの HP で割る (経路を問わない・ひびなし・全部 満額のダウン)。</summary>
        AnyDamage = 1,
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
        /// <summary>まだ来ていないダウンの種類 (割れた順・true = ひびで割れた弱いダウン)。 長さは <see cref="pending"/> と同じ。</summary>
        public readonly System.Collections.Generic.List<bool> pendingWeak = new System.Collections.Generic.List<bool>();
        /// <summary>目盛りごとの、 ひびが入ったターン (−1 = ひびなし・割った目盛りも −1)。 null = 目盛りなし。</summary>
        public int[] crackTurn;
        /// <summary>このターンはダウン。</summary>
        public bool downNow;
        /// <summary>このターンのダウンは弱い (ひびで割れた・受けるダメージ ×1.5 なし)。</summary>
        public bool downWeakNow;
        /// <summary>このターンのダウンが大技を取り消した (<see cref="BossDown.CancelsCycle"/> が真で、 敵が大技を持つ)。 CombatManager が立てる・画面が読む。</summary>
        public bool heavyCanceledNow;

        public bool Enabled => notches != null && notches.Length > 0;

        /// <summary>戦闘の始め・形態の交代: 目盛りを入れ直し、 割った数と待ちを 0 に。</summary>
        public void Reset(float[] n)
        {
            notches = n != null && n.Length > 0 ? n : null; broken = 0; pending = 0; downNow = false; downWeakNow = false; heavyCanceledNow = false;
            pendingWeak.Clear();
            crackTurn = notches != null ? new int[notches.Length] : null;
            if (crackTurn != null) for (int i = 0; i < crackTurn.Length; i++) crackTurn[i] = -1;
        }

        /// <summary>残っているダウンの先頭が弱いか。</summary>
        public bool PendingHeadWeak => pendingWeak.Count > 0 && pendingWeak[0];

        /// <summary>ターンの頭: 待ちがあればこのターンをダウンにして 1 つ使う。 戻り値 = このターンはダウンか。</summary>
        public bool BeginTurn()
        {
            heavyCanceledNow = false;
            downNow = pending > 0;
            downWeakNow = false;
            if (downNow)
            {
                pending--;
                if (pendingWeak.Count > 0) { downWeakNow = pendingWeak[0]; pendingWeak.RemoveAt(0); }
            }
            return downNow;
        }

        private void Push(int d, bool weak) { for (int i = 0; i < d; i++) pendingWeak.Add(weak); pending += d; }

        /// <summary>[旧い判定 AnyDamage] ターンの終わり: HP から新しく割った目盛りを数え、 その数だけダウンを待ちに積む (全部 満額)。 戻り値 = 新しく割った数。 倒れた (HP ≤ 0) なら数えない。</summary>
        public int NoteTurnEnd(int hp, int maxHp)
        {
            if (!Enabled || hp <= 0) return 0;
            int nb = BossDown.BrokenCount(notches, maxHp, hp, broken);
            int d = nb - broken;
            broken = nb; Push(d, false);
            return d;
        }

        /// <summary>[新しい判定 MainAttackOnly] 主攻撃が <paramref name="mainDmg"/> (1 以上) で当たった直後の HP <paramref name="hp"/> で、 まだ割っていない目盛り以下のものを全部割る (満額のダウン)。
        /// 戻り値 = 割った数。 倒れた (HP ≤ 0)・ダメージ 0 (回避・吸収) なら割らない。 <paramref name="crackedBroken"/> = 割った中で、 ひびが入っていた数 /
        /// <paramref name="crackWaitSum"/> = それらの、 ひびが入ってから割るまでのターン数の合計 (計装)。</summary>
        public int NoteMainHit(int hp, int maxHp, int mainDmg, int turn, out int crackedBroken, out int crackWaitSum)
        {
            crackedBroken = 0; crackWaitSum = 0;
            if (!Enabled || hp <= 0 || mainDmg <= 0) return 0;
            int nb = BossDown.BrokenCount(notches, maxHp, hp, broken);
            for (int i = broken; i < nb; i++)
                if (crackTurn[i] >= 0) { crackedBroken++; crackWaitSum += Math.Max(0, turn - crackTurn[i]); crackTurn[i] = -1; }
            int d = nb - broken;
            broken = nb; Push(d, false);
            return d;
        }

        /// <summary>[新しい判定 MainAttackOnly] ターンの終わり: まだ割っていない目盛りのうち、 HP が目盛り以下のものにひびを入れ (入っていなければこのターン)、
        /// 目盛りの上なら消す (回復で戻った)。 ひびが入ったターンから <paramref name="grace"/> ターン後のターンの終わりに達したものを、 上から順に割る
        /// (<paramref name="grace"/> &lt; 0 = ひびでは割れない)。 割ったダウンは <paramref name="crackWeak"/> なら弱い。 戻り値 = ひびで割った数。 倒れた (HP ≤ 0) なら何もしない。
        /// <paramref name="newCracks"/> = このターンに新しく入ったひび / <paramref name="healed"/> = 回復で消えたひび (計装)。 規則は <see cref="BossDown.ForecastTurn"/> と同じ。</summary>
        public int NoteTurnEndCracks(int hp, int maxHp, int turn, int grace, bool crackWeak, out int newCracks, out int healed)
        {
            newCracks = 0; healed = 0;
            if (!Enabled || hp <= 0) return 0;
            for (int i = broken; i < notches.Length; i++)
            {
                if (hp <= BossDown.NotchHp(maxHp, notches[i])) { if (crackTurn[i] < 0) { crackTurn[i] = turn; newCracks++; } }
                else if (crackTurn[i] >= 0) { crackTurn[i] = -1; healed++; }
            }
            int d = 0;
            if (grace >= 0)
                while (broken < notches.Length && crackTurn[broken] >= 0 && turn - crackTurn[broken] >= grace)
                { crackTurn[broken] = -1; broken++; d++; }
            Push(d, crackWeak);
            return d;
        }

        /// <summary>まだ割っていない一番上の目盛りのひびが割れるターン (そのターンの終わりに割れる・−1 = ひびなし / 猶予が負)。 画面・予告が読む。</summary>
        public int CrackBreakTurn(int grace)
            => grace >= 0 && crackTurn != null && broken < crackTurn.Length && crackTurn[broken] >= 0 ? crackTurn[broken] + grace : -1;

        /// <summary>まだ割っていない一番上の目盛りにひびが入っているか。</summary>
        public bool TopCracked => crackTurn != null && broken < crackTurn.Length && crackTurn[broken] >= 0;

        /// <summary>次に割る目盛りの HP (−1 = 無し)。</summary>
        public int NextNotchHp(int maxHp) => BossDown.NextNotchHp(notches, maxHp, broken);
    }
}

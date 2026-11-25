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
    /// その間に通常攻撃が当たれば、 その時点で普通に割れる。 一度入ったひびは、 回復で HP が目盛りの上へ戻っても消えない (2026-11-14・作者の決定・その間の通常攻撃では割れず、 猶予が切れた時に割れる)。
    /// ひとりでに割れた目盛りのダウンは<b>弱い</b>: 敵は攻撃しないが、 受けるダメージ ×<see cref="TakenMul"/> は付かない (口 downCrackWeak)。
    /// 口 downBreakRule = AnyDamage で旧い判定 (ターンの終わりの HP で割る・ひびなし・全部 ダウン)。</para>
    ///
    /// <para>本体 (<c>CombatManager</c>) と BOT (<c>AutoRunner</c> の効用・先読み) が<b>同じ関数</b>を呼ぶ。 式を 2 か所に書かない。
    /// このファイルは UnityEngine に依存しない (Unity なしの検算 <c>Tools/rules_check</c> がそのままコンパイルする)。
    /// 小数の定数は const の float で持つ (実行時の割り算に置き換えない)。</para>
    /// </summary>
    public static class BossDown
    {
        /// <summary>ダウン中に受けるダメージの倍率 (ダウンだけ・ひびで割れたよろめきには付かない)。</summary>
        public const float TakenMul = 1.5f;
        /// <summary>今の戦闘のダウンの倍率 (既定 <see cref="TakenMul"/>・挑戦カード〈受身〉で 1.35 / 1.20)。 戦闘の始めに <see cref="CardRules.Load"/> が入れる。</summary>
        public static float TakenMulCard = TakenMul;

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

        /// <summary>目盛りの間隔を <paramref name="pct"/>% 広げた新しい並び (挑戦カード〈受身〉III・2026-11-22)。 満タンから i 番目の目盛りまでの距離 (1 − 割合) を ×(1 + pct/100) し、
        /// 0 以下になった目盛りは消す。 <paramref name="pct"/> ≤ 0・null なら元のまま (元の配列は書き換えない)。</summary>
        public static float[] WidenNotches(float[] notches, int pct)
        {
            if (notches == null || notches.Length == 0 || pct <= 0) return notches;
            var l = new System.Collections.Generic.List<float>(notches.Length);
            foreach (float f in notches) { float g = 1f - (1f - f) * (100 + pct) / 100f; if (g > 0f) l.Add(g); }
            return l.Count > 0 ? l.ToArray() : null;
        }

        /// <summary>次に割る目盛りの HP (−1 = もう目盛りが無い・ダウンの無い敵)。</summary>
        public static int NextNotchHp(float[] notches, int maxHp, int broken)
            => notches != null && broken >= 0 && broken < notches.Length ? NotchHp(maxHp, notches[broken]) : -1;

        /// <summary>[旧い判定 AnyDamage] このターンの終わりの HP が <paramref name="hpAfter"/> になった時、 次のターンがダウンか。 新しい判定は <see cref="ForecastTurn"/>。
        /// <paramref name="pendingNow"/> = このターンの頭でダウンを 1 つ使った後に残っているダウンの数 (<see cref="BossDownState.pending"/>)。</summary>
        public static bool NextTurnIsDown(float[] notches, int maxHp, int broken, int pendingNow, int hpAfter)
            => notches != null && hpAfter > 0 && pendingNow + (BrokenCount(notches, maxHp, hpAfter, broken) - broken) > 0;

        /// <summary>受けるダメージにダウンの倍率を掛ける (<paramref name="down"/> = このターンはダウン・切り捨て)。
        /// 〈英雄の意志〉〈黒錫のライデン瓶〉で強めたダウン (2026-11-25) は倍率 +<see cref="TakenBonusPctNow"/>%。</summary>
        public static int ApplyTaken(int dmg, bool down)
            => !down || dmg <= 0 ? dmg : TakenBonusPctNow > 0 ? (int)(dmg * (TakenMulCard + TakenBonusPctNow * 0.01f)) : (int)(dmg * TakenMulCard);

        /// <summary>このダウンの倍率の上乗せ (% ・0 = なし・2026-11-25)。 次のダウンを強める品 (<see cref="BossDownState.bonusPct"/>) が、 そのダウンの間だけ立てる。 CombatManager がターンの頭に入れ、 戦闘の始めと終わりに 0 へ戻す。</summary>
        public static int TakenBonusPctNow;

        /// <summary>次のダウンの倍率の上乗せの上限 (% ・×1.5 → ×2.5 まで・2026-11-25 neon answer_gauge)。</summary>
        public const int TakenBonusMaxPct = 100;
        /// <summary>次のダウンを 1 ターン延ばせる回数 (1 戦闘につき・〈柱折りの発破筒〉・2026-11-25)。</summary>
        public const int ExtendMaxPerFight = 2;

        /// <summary>いまの戦闘のこのターンがダウンか (種類を問わない・CombatManager がターンの頭に立て、 次のターンの頭・戦闘の始めと終わりに戻す)。
        /// 「ダウン中は発動しない」を名乗ったパッシブの止め (<c>PassiveSkillManager.FireEnemyTrigger</c>) が読む。</summary>
        public static bool ActiveNow;

        /// <summary>このターンのダウンが受けるダメージ ×<see cref="TakenMul"/> を持つか (ダウン。 ひびで割れたよろめきは false・2026-11-14。 〈骨溶かしの黒油〉を持てばよろめきも true・2026-11-25)。
        /// 敵の HP を減らす口 (<c>HpLossDiag.Lose</c>・主攻撃の <c>ApplyWinDamageModifiers</c>・品の直接の削り) が <see cref="ApplyTakenNow"/> で読む。</summary>
        public static bool TakenMulNow;

        /// <summary>このターンが ×1.5 を持つダウンなら ×<see cref="TakenMul"/> (と上乗せ)。</summary>
        public static int ApplyTakenNow(int dmg) => ApplyTaken(dmg, TakenMulNow);

        // ---- 割れ方の見込み (本体の BossDownState と同じ規則を、 状態を書き換えずに 1 ターン分だけ進める・BOT が読む) ----

        /// <summary><see cref="ForecastTurn"/> の答え。</summary>
        public struct Forecast
        {
            /// <summary>通常攻撃で割れる数 (ダウン) / ひびの猶予が切れて割れる数 / ターンの後の割った目盛りの数。</summary>
            public int fullAdded, crackAdded, brokenAfter;
            /// <summary>次のターンはダウンか / そのダウンは弱いか (被ダメ ×1.5 なし)。</summary>
            public bool nextDown, nextWeak;
            /// <summary>ターンの後の、 目盛りごとのひびの入ったターン (−1 = ひびなし・割れた目盛りも −1・wantCracks の時だけ)。</summary>
            public int[] cracksAfter;
        }

        /// <summary>このターンに、 主攻撃 <paramref name="mainDmg"/> (HP に入る量) が当たり、 その後に通常攻撃以外 <paramref name="afterMainDmg"/> が入った時の、 目盛りの割れ方
        /// (新しい判定では <paramref name="afterMainDmg"/> が負 = 差し引きの回復も読む・ひびは回復では消えない)。
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
                    int had = crackTurns != null && i < crackTurns.Length ? crackTurns[i] : -1;
                    // 目盛り以下ならひび (無ければこのターン)。 目盛りの上でも、 一度入ったひびは消えない (2026-11-14・作者の決定)
                    int ct = had >= 0 ? had : hpEnd <= NotchHp(maxHp, notches[i]) ? turn : -1;
                    if (ct >= 0 && expiring && turn - ct >= grace) { f.crackAdded++; if (after != null) after[i] = -1; continue; }
                    expiring = false;   // 割れるのは上から順 (割れない目盛りの下は、 このターンには割れない)
                    if (after != null) after[i] = ct;
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
        /// <summary>既定 (2026-11-14〜): 通常攻撃 (主攻撃) が当たった時だけ割れる。 通常攻撃以外で下回った目盛りにはひびが入り、 猶予の後にひとりでに割れる (よろめき)。</summary>
        MainAttackOnly = 0,
        /// <summary>旧い判定 (2026-11-13〜11-14): ターンの終わりの HP で割る (経路を問わない・ひびなし・全部 ダウン)。</summary>
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
        /// <summary>まだ来ていないダウンの種類 (割れた順・true = ひびで割れたよろめき)。 長さは <see cref="pending"/> と同じ。</summary>
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

        /// <summary>[新しい判定 MainAttackOnly] 主攻撃が <paramref name="mainDmg"/> (1 以上) で当たった直後の HP <paramref name="hp"/> で、 まだ割っていない目盛り以下のものを全部割る (ダウン)。
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

        /// <summary>[新しい判定 MainAttackOnly] ターンの終わり: まだ割っていない目盛りのうち、 HP が目盛り以下でひびの無いものにひびを入れる (このターン)。
        /// 一度入ったひびは回復で目盛りの上へ戻っても消さない (2026-11-14・作者の決定)。 ひびが入ったターンから <paramref name="grace"/> ターン後のターンの終わりに達したものを、
        /// 上から順に割る (<paramref name="grace"/> &lt; 0 = ひびでは割れない)。 割ったダウンは <paramref name="crackWeak"/> なら弱い。 戻り値 = ひびで割った数。 倒れた (HP ≤ 0) なら何もしない。
        /// <paramref name="newCracks"/> = このターンに新しく入ったひび (計装)。 規則は <see cref="BossDown.ForecastTurn"/> と同じ。</summary>
        public int NoteTurnEndCracks(int hp, int maxHp, int turn, int grace, bool crackWeak, out int newCracks)
        {
            newCracks = 0;
            if (!Enabled || hp <= 0) return 0;
            // 一度入ったひびは、 回復で目盛りの上へ戻っても消さない (2026-11-14・作者の決定)
            for (int i = broken; i < notches.Length; i++)
                if (hp <= BossDown.NotchHp(maxHp, notches[i]) && crackTurn[i] < 0) { crackTurn[i] = turn; newCracks++; }
            int d = 0;
            if (grace >= 0)
                while (broken < notches.Length && crackTurn[broken] >= 0 && turn - crackTurn[broken] >= grace)
                {
                    Push(1, crackWeak);
                    crackTurn[broken] = -1; broken++; d++;
                }
            return d;
        }

        /// <summary>次に割る目盛りを <paramref name="add"/> (HP) だけ上げる (臨界の爆発・【劇症】・〈雷鯰の棘〉〈避雷の杭〉ほか)。 <paramref name="hpCap"/> より上へは行かない。
        /// 並びは書き換えず作り直す (<c>BossParams</c> の表を共有しているため)。 戻り値 = 上がった後の目盛りの HP (動かなければ −1)。</summary>
        public int RaiseNext(int maxHp, int hpCap, int add)
        {
            if (!Enabled || add <= 0 || maxHp <= 0 || broken >= notches.Length) return -1;
            int cur = BossDown.NotchHp(maxHp, notches[broken]);
            int to = Math.Min(hpCap, cur + add);
            if (to <= cur) return -1;
            var n = (float[])notches.Clone();
            n[broken] = (to + 0.5f) / maxHp;   // NotchHp の切り捨てで to より下がらないよう半分足す
            notches = n;
            return BossDown.NotchHp(maxHp, notches[broken]);
        }

        /// <summary>まだ割っていない一番上の目盛りのひびが割れるターン (そのターンの終わりに割れる・−1 = ひびなし / 猶予が負)。 画面・予告が読む。</summary>
        public int CrackBreakTurn(int grace)
            => grace >= 0 && crackTurn != null && broken < crackTurn.Length && crackTurn[broken] >= 0 ? crackTurn[broken] + grace : -1;

        // ---- 次のダウンを強める (2026-11-25・neon answer_gauge の方向 2)。 戦闘を通して持つ (形態の交代では消さない・ResetBuffs は戦闘の始めだけ) ----

        /// <summary>[persistent] 次のダウンの倍率の上乗せ (% ・上限 <see cref="BossDown.TakenBonusMaxPct"/>) / そのダウンの開始時に得るシールド / 充電。</summary>
        public int bonusPct, shieldOnDown, chargeOnDown;
        /// <summary>[persistent] 次のダウンを 1 ターン延ばす (〈柱折りの発破筒〉) / この戦闘で延ばした回数。</summary>
        public bool extendNext;
        public int extendsUsed;
        /// <summary>このダウンの倍率の上乗せ (% ・ダウンが続く間だけ)。 / このダウンの間に、 強めを使った (よろめきでないダウンの最初のターンで使う)。</summary>
        public int bonusNowPct;
        private bool _buffsTaken;

        /// <summary>戦闘の始め: 次のダウンを強める分を全部 0 へ。</summary>
        public void ResetBuffs() { bonusPct = 0; shieldOnDown = 0; chargeOnDown = 0; extendNext = false; extendsUsed = 0; bonusNowPct = 0; _buffsTaken = false; }

        /// <summary>次のダウンの倍率 +<paramref name="pct"/>% (上限まで)・開始時のシールド / 充電を足す。</summary>
        public void BuffNext(int pct, int shield, int charge)
        {
            bonusPct = Math.Min(BossDown.TakenBonusMaxPct, bonusPct + Math.Max(0, pct));
            shieldOnDown += Math.Max(0, shield); chargeOnDown += Math.Max(0, charge);
        }

        /// <summary>次のダウンを 1 ターン延ばす (1 回のダウンにつき 1 ターン・1 戦闘 <see cref="BossDown.ExtendMaxPerFight"/> 回まで)。 仕込めたら true。</summary>
        public bool ExtendNext()
        {
            if (!Enabled || extendNext || extendsUsed >= BossDown.ExtendMaxPerFight) return false;
            extendNext = true;
            return true;
        }

        /// <summary><see cref="BeginTurn"/> の後に呼ぶ: このターンがよろめきでないダウンの最初のターンなら、 溜めた強めを使う
        /// (倍率の上乗せはダウンが続く間・シールド / 充電は戻り値で渡す・延長はダウンを 1 ターン足す)。 ダウンでないターンは上乗せを消す。 使ったら true。</summary>
        public bool TakeBuffs(out int shield, out int charge, out bool extended)
        {
            shield = 0; charge = 0; extended = false;
            if (!downNow) { bonusNowPct = 0; _buffsTaken = false; return false; }
            if (downWeakNow || _buffsTaken) return false;
            _buffsTaken = true;
            bonusNowPct = bonusPct; shield = shieldOnDown; charge = chargeOnDown;
            bonusPct = 0; shieldOnDown = 0; chargeOnDown = 0;
            if (extendNext) { extendNext = false; extendsUsed++; pending++; pendingWeak.Insert(0, false); extended = true; }
            return bonusNowPct > 0 || shield > 0 || charge > 0 || extended;
        }

        /// <summary>次に割る目盛りの HP (−1 = 無し)。</summary>
        public int NextNotchHp(int maxHp) => BossDown.NextNotchHp(notches, maxHp, broken);
    }
}

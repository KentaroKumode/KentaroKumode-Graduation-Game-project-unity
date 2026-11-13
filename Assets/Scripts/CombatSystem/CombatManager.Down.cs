using ItemSystem.PassiveSkills;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>CombatManager: ボスのダウン (HP の目盛り・2026-11-13・GAME.md §6-15)。 規則の式は <see cref="BossDown"/> にだけある (ここは状態を進めるだけ)。</summary>
    public partial class CombatManager
    {
        /// <summary>この戦闘 (ヴェスカは段) のダウンの状態。 戦闘の始めと形態の交代で作り直す。</summary>
        private readonly BossDownState _down = new BossDownState();

        /// <summary>ダウンの状態 (画面・BOT が読む)。 書き換えないこと。</summary>
        public BossDownState Down => _down;

        /// <summary>この段で博士の「止まらない時計」が効いている (目盛りを 1 つ足した・<see cref="GameLoop.VescaClock"/>)。 画面のヴェスカの状態の欄が読む。</summary>
        public bool VescaClockActive { get; private set; }

        /// <summary>戦闘の始め・形態の交代: 敵の目盛りを入れ直す (目盛りの無い敵・測定の口で止めた時は無し)。
        /// **目盛りの並びを作るのはここ 1 か所** (時計の目盛りもここで足す)。 画面・BOT は <see cref="Down"/> の並びを読む。</summary>
        private void ResetBossDown(EnemyData enemy, bool newCombat)
        {
            bool clock = GameLoop.VescaClock.Held(GameLoop.GameManager.Instance?.Run);
            _down.Reset(BossParams.DownNotches(enemy?.id, clock));
            VescaClockActive = clock && _down.Enabled && GameLoop.BossIds.VescaStageOf(enemy?.id) > 0;   // ダウンを止めた測定の口では効かない
            // 戦闘の始めの地の文 (最初の段だけ 1 回)。 戦闘の画面に文の欄が無いので戦闘ログの行として出す (2026-11-13)。 ヴェスカの台詞は置かない (作者の決定)
            if (VescaClockActive && newCombat && enemy?.id == GameLoop.BossIds.VescaP1)
                Debug.Log("[戦闘] " + GameLoop.VescaClock.BattleLine);
            if (VescaClockActive) Debug.Log($"[ダウン] 〈{GameLoop.VescaClock.Name}〉: {enemy?.displayName} の目盛りに {BossParams.VescaClockNotch * 100f:0}% を足した");
            BossDown.ActiveNow = false; BossDown.TakenMulNow = false;
            if (newCombat) { Escalation.HeavyCycleOffset = 0; Escalation.HeavyHpRatio = 1f; }
            BossDownDiag.BeginFight(enemy?.id, _down.Enabled);
        }

        /// <summary>ターン <paramref name="turn"/> がダウンだったら、 敵の周期 (大技・構え・〈業火の予兆〉) を取り消して頭に戻すか (<see cref="BossDown.CancelsCycle"/>)。
        /// 今の起点 (<see cref="Escalation.HeavyCycleOffset"/>) で数える ── 本体はダウンのターンの頭に起点を動かす前に呼び、 BOT は先のターンの見込みに呼ぶ (同じ関数・写しを持たない)。
        /// <paramref name="bossHpRatio"/> = ボスの HP の割合 (〈業火の予兆〉の周期が HP 50% で変わる)。 規則は口 <see cref="GameLoop.GameHooks.DownCancel"/>。</summary>
        public static bool DownCancelsCycleAt(EnemyData e, int turn, float bossHpRatio)
        {
            if (e == null) return false;
            int omen = -1;
            if (e.passiveSkills != null)
                foreach (var ps in e.passiveSkills)
                    if (ps != null && ps.internalName == ItemSystem.PassiveSkills.Effects.EmberOmen.Id)
                    { omen = ItemSystem.PassiveSkills.Effects.EmberOmenTiming.TurnsToOmen(turn, bossHpRatio); break; }
            return BossDown.CancelsCycle(GameLoop.GameHooks.DownCancel, Escalation.TurnsToHeavy(e, turn), Escalation.TurnsToGuard(e, turn), omen);
        }

        /// <summary>ターンの頭 (ターン番号を進めた後・敵が振る前): 待っているダウンがあれば、 このターンをダウンにする。
        /// ダウンがチャージのターンか山場 (大技・構え・予兆) のターンに重なった時だけ、 その大技は取り消し、 周期は次のターンから頭に戻す
        /// (<see cref="Escalation.HeavyCycleOffset"/>・<see cref="DownCancelsCycleAt"/>・2026-11-14 の作者の決定)。 それ以外のダウンは周期に触らない。</summary>
        private void BeginBossDownTurn(CombatContext ctx)
        {
            // 灰燼の王の〈業火〉の周期は HP 50% で 4 → 3 ターン (2026-11-14)。 このターンの周期の数え方に使う HP の割合をターンの頭に決める (取り消しの判定より前)
            Escalation.HeavyHpRatio = currentEnemy != null ? enemyHP / (float)System.Math.Max(1, EnemyMaxHP) : 1f;
            BossDown.ActiveNow = false; BossDown.TakenMulNow = false;
            if (!_down.Enabled || ctx == null) { _down.downNow = false; _down.downWeakNow = false; return; }
            if (!_down.BeginTurn()) return;
            bool hadHeavy = currentEnemy != null && currentEnemy.heavyPeriod > 0;
            bool always = GameLoop.GameHooks.DownCancel == DownCancelRule.Always;
            // 起点を動かす前に見る。 計装の「取り消した」: 新しい規則 = 大技のチャージか大技のターンに重なった / 旧い規則 (Always) = 今までどおり大技のターンだけ数える
            int toHeavyAtDown = hadHeavy ? Escalation.TurnsToHeavy(currentEnemy, ctx.currentTurn) : -1;   // [診断] ダウンのターンの周期の位置 (起点を動かす前)
            bool canceled = hadHeavy && (always ? Escalation.IsHeavyTurn(currentEnemy, ctx.currentTurn)
                                                : BossDown.IsChargeOrClimax(toHeavyAtDown));
            if (DownCancelsCycleAt(currentEnemy, ctx.currentTurn, Escalation.HeavyHpRatio))
                Escalation.HeavyCycleOffset = ctx.currentTurn;
            _down.heavyCanceledNow = hadHeavy && (always || canceled);   // 画面の「大技は取り消し」(旧い規則はどのダウンでも大技が先へ延びる)
            BossDown.ActiveNow = true;
            BossDown.TakenMulNow = !_down.downWeakNow;   // ひびで割れた弱いダウン (2026-11-14) は受けるダメージ ×1.5 なし
            BossDownDiag.NoteDown(hadHeavy, canceled, toHeavyAtDown, _down.downWeakNow);
            Debug.Log($"[ダウン] T{ctx.currentTurn} {currentEnemy?.displayName}: 行動不能" + (_down.downWeakNow ? "・ひびのダウン (受けるダメージは普段どおり)" : $"・受けるダメージ ×{BossDown.TakenMul:0.0}")
                    + (canceled ? "・大技は取り消し (周期は頭から)" : hadHeavy ? (always ? "・(大技は溜まっていない)" : "・大技の周期はそのまま進む") : "")
                    + (_down.pending > 0 ? $" (あと {_down.pending} ターン続く)" : ""));
        }

        /// <summary>予告にダウンの欄を入れる (振り直しの判断の仮組み・配線の判断の予告の 2 か所が呼ぶ・写しを持たない)。
        /// ダウンのターンは敵の攻撃 (1 体目・2 体目 / 〈連撃〉の 2 回目) を 0 にする。 ダウンの無い敵では何も変えない
        /// (欄は既定のまま: enemyDown=false・downNotches=null・downNextNotchHp=−1)。</summary>
        private void FillDownTelegraph(ref MutualTurnTelegraph t)
        {
            t.enemyDown = _down.downNow;
            t.enemyDownWeak = _down.downNow && _down.downWeakNow;
            t.downPending = _down.pending;
            t.downPendingHeadWeak = _down.PendingHeadWeak;
            t.downCrackTurns = _down.crackTurn;
            t.downCrackBreakTurn = _down.CrackBreakTurn(GameLoop.GameHooks.DownCrackGrace);
            t.downNotches = _down.notches;
            t.downBroken = _down.broken;
            t.downNextNotchHp = currentEnemy != null ? _down.NextNotchHp(currentEnemy.maxHP) : -1;
            if (_down.downNow) { t.enemyAttackValue = 0; t.secondaryAttackValue = 0; }
            // 大技のチャージ (2026-11-14): 攻撃しない。 攻撃値は Escalation.HeavyFactor が 0 にしてある (予告も BOT の見込みも同じ関数)。 ここは画面の印だけ
            t.enemyCharging = !_down.downNow && Escalation.IsChargeNoAttack(currentEnemy, t.turn);
            // 灰燼の王の〈業火〉(2026-11-14): このターンが業火なら、 烈炎の分 (軽減無視・撃ったら烈炎 0)。 ダウンのターンは撃たない
            t.heavyBurst = _down.downNow ? 0 : BlazeBurstAt(currentEnemy, t.turn, BlazeStacks(ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context));
        }

        /// <summary>[旧い判定 AnyDamage の計装] 主攻撃の直後に、 主攻撃だけで割れた目盛りの数 (ターンの終わりの数と比べて、 主攻撃以外で割れた数を出す)。</summary>
        private int _downMainWouldBreak;

        /// <summary>主攻撃 (通常攻撃) が当たった直後 (主攻撃のダメージを HP から引いた所・固定ダメの枠より前・2026-11-14):
        /// 新しい判定 (MainAttackOnly) なら、 HP 以下にある未割りの目盛りを全部割る (満額のダウン・ひびの入った目盛りも)。 ダメージ 0 (回避・吸収) や撃破では割らない。
        /// 旧い判定 (AnyDamage) では何もしない (計装だけ)。 1 ターンに主攻撃が複数回当たる時は、 当たるたびに呼んでよい。</summary>
        private void NoteBossDownMainHit(int mainDmg, int turn)
        {
            _downMainWouldBreak = 0;
            if (!_down.Enabled || currentEnemy == null || enemyHP <= 0 || mainDmg <= 0) return;
            if (GameLoop.GameHooks.DownBreak == DownBreakRule.AnyDamage)
            {
                _downMainWouldBreak = BossDown.BrokenCount(_down.notches, currentEnemy.maxHP, enemyHP, _down.broken) - _down.broken;
                return;
            }
            int d = _down.NoteMainHit(enemyHP, currentEnemy.maxHP, mainDmg, turn, out int crackedBroken, out int waitSum);
            if (d <= 0) return;
            BossDownDiag.NoteMainBreak(d, crackedBroken, waitSum);
            Debug.Log($"[ダウン] 通常攻撃で目盛りを {d} つ割った (HP {enemyHP}/{currentEnemy.maxHP}"
                    + (crackedBroken > 0 ? $"・うちひびの入った目盛り {crackedBroken}" : "") + $") → 次のターンからダウン {_down.pending} ターン");
        }

        /// <summary>ターンの終わり (ターン終了時の削りを拾った後): 旧い判定 (AnyDamage) = 新しく割った目盛りを数え、 その数だけ次のターンからダウンを待たせる。
        /// 新しい判定 (MainAttackOnly) = HP が目盛り以下の未割りの目盛りにひびを入れ、 猶予の切れたひびを割る (弱いダウン)・目盛りの上へ戻っていればひびを消す。</summary>
        private void NoteBossDownTurnEnd()
        {
            if (!_down.Enabled || currentEnemy == null || enemyHP <= 0) { _downMainWouldBreak = 0; return; }
            int turn = PassiveSkillManager.Instance?.Context?.currentTurn ?? 0;
            if (GameLoop.GameHooks.DownBreak == DownBreakRule.AnyDamage)
            {
                int d = _down.NoteTurnEnd(enemyHP, currentEnemy.maxHP);
                if (d > 0)
                {
                    BossDownDiag.NoteAnyDamageBreak(d, System.Math.Min(d, _downMainWouldBreak));
                    Debug.Log($"[ダウン] 目盛りを {d} つ割った (HP {enemyHP}/{currentEnemy.maxHP}) → 次のターンからダウン {_down.pending} ターン");
                }
                _downMainWouldBreak = 0;
                return;
            }
            _downMainWouldBreak = 0;
            int c = _down.NoteTurnEndCracks(enemyHP, currentEnemy.maxHP, turn, GameLoop.GameHooks.DownCrackGrace, GameLoop.GameHooks.DownCrackWeak,
                                            out int newCracks, out int healed);
            BossDownDiag.NoteCracks(newCracks, healed, c);
            if (newCracks > 0) Debug.Log($"[ダウン] 通常攻撃以外で目盛りを下回った: ひび {newCracks} (HP {enemyHP}/{currentEnemy.maxHP})");
            if (c > 0) Debug.Log($"[ダウン] ひびの猶予が切れて目盛りが {c} つひとりでに割れた → 次のターンからダウン {_down.pending} ターン");
        }

        /// <summary>戦闘の終わり: ダウンの倍率を戻す。</summary>
        private void EndBossDown()
        {
            BossDown.ActiveNow = false; BossDown.TakenMulNow = false;
            _down.downNow = false; _down.downWeakNow = false;
            Escalation.HeavyCycleOffset = 0;
            Escalation.HeavyHpRatio = 1f;
        }

        // ===== 灰燼の王の大技〈業火〉の烈炎の分 (2026-11-14・作者の決定) =====

        /// <summary>この敵が〈烈炎〉を持ち、 大技を持つ (= 〈業火〉で烈炎を返す) か。 旧い形 (ashKingHeavy=False) では大技が無いので偽。</summary>
        private static bool HasBlazeBurst(EnemyData e)
        {
            if (e == null || e.heavyPeriod <= 0 || e.passiveSkills == null) return false;
            foreach (var ps in e.passiveSkills)
                if (ps != null && ps.internalName == ItemSystem.PassiveSkills.Effects.BlazeBrand.Id) return true;
            return false;
        }

        /// <summary>ターン <paramref name="turn"/> が〈業火〉なら、 烈炎の分 (軽減無視) = 烈炎の数 × BlazeBurstPerStack。 それ以外・烈炎の無い敵は 0。
        /// 予告 (<see cref="MutualTurnTelegraph.heavyBurst"/>)・解決 (<see cref="ApplyBlazeBurst"/>)・BOT が同じ関数を読む。</summary>
        public static int BlazeBurstAt(EnemyData e, int turn, int stacks)
            => HasBlazeBurst(e) && Escalation.IsHeavyTurn(e, turn)
             ? HeavyCycle.BlazeBurst(stacks, BossParams.ParamInt(e.id, BossParam.BlazeBurstPerStack)) : 0;

        /// <summary>今の烈炎の数 (戦闘を通して積もる・<see cref="ItemSystem.PassiveSkills.Effects.BlazeBrand.StackKey"/>)。</summary>
        private static int BlazeStacks(CombatContext ctx)
            => ctx != null && ctx.accumulatedValues.TryGetValue(ItemSystem.PassiveSkills.Effects.BlazeBrand.StackKey, out var v) ? (int)v : 0;

        /// <summary>〈業火〉を撃ったターン (攻撃の枝・主攻撃の後): 烈炎の分を軽減無視で入れ (シールドは肩代わりする規則に従う・〈蝕み〉と同じ扱い)、 烈炎を 0 に戻す。
        /// ダウン・チャージ・〈封じ・揃〉のターンは攻撃の枝を通らないので呼ばれない (烈炎は持ち越す)。 戻り値 = HP に入った量。</summary>
        private int ApplyBlazeBurst(CombatContext ctx)
        {
            if (ctx == null || currentEnemy == null || playerHP <= 0) return 0;
            int raw = BlazeBurstAt(currentEnemy, ctx.currentTurn, BlazeStacks(ctx));
            if (raw <= 0) return 0;
            int stacks = BlazeStacks(ctx);
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Effects.BlazeBrand.StackKey] = HeavyCycle.BlazeStacksAfter(stacks, true);
            int absorbed = CombatContext.ShieldAbsorbsUnmitigable && ctx.consShield > 0
                ? ItemSystem.PassiveSkills.Keywords.Kw.AbsorbShield(ctx, raw, isAttack: false) : 0;
            int dmg = raw - absorbed;
            if (dmg > 0)
            {
                playerHP = System.Math.Max(0, playerHP - dmg);
                ctx.playerCurrentHP = playerHP;
                ctx.playerDamageThisTurn += dmg;
                ctx.AddPlayerDamageSource(DeathCause.Judgment, dmg);
                if (playerHP <= 0) ctx.lastDamageCause = DeathCause.Judgment;
            }
            Debug.Log($"[業火] 烈炎 {stacks} × {BossParams.ParamInt(currentEnemy.id, BossParam.BlazeBurstPerStack)} = {raw} (軽減無視・シールド {absorbed} 肩代わり) → HP {playerHP} / 烈炎は 0 へ");
            return dmg;
        }
    }
}

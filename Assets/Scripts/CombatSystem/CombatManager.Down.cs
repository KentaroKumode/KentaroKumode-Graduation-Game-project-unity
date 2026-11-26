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
        /// **目盛りの並びを作るのはここ** (時計の目盛りもここで足す・2026-11-26 に混乱ゲージへ・戦闘中に動かすのは <see cref="AddBossGauge"/> だけ)。 画面・BOT は <see cref="Down"/> の並びを読む。</summary>
        private void ResetBossDown(EnemyData enemy, bool newCombat)
        {
            bool clock = GameLoop.VescaClock.Held(GameLoop.GameManager.Instance?.Run);
            // 混乱ゲージ (2026-11-26): 挑戦カード〈受身〉III は満タンの量 +N% (旧: 目盛りの間隔 +N%)・博士の時計はヴェスカの各段を半分から
            var (gMax, gGrow) = BossParams.DownGauge(enemy?.id);
            int spacing = (int)MetaProgression.MetaDebuffApplicator.GetDownNotchSpacingPct();
            if (gMax > 0 && spacing > 0) gMax = (int)System.Math.Round(gMax * (100 + spacing) / 100.0);
            bool vesca = GameLoop.BossIds.VescaStageOf(enemy?.id) > 0;
            _down.ResetGauge(gMax, gGrow, clock && vesca ? (int)(gMax * BossParams.VescaClockGaugeStart) : 0);
            VescaClockActive = clock && _down.Enabled && vesca;   // ダウンを止めた測定の口では効かない
            _gaugeFullTurn = _crushTurn = -99; _gaugeLate = false; _gaugeCarry = 0;
            if (newCombat) _gaugeFullsFight = 0;
            // 戦闘の始めの地の文 (最初の段だけ 1 回)。 戦闘の画面に文の欄が無いので戦闘ログの行として出す (2026-11-13)。 ヴェスカの台詞は置かない (作者の決定)
            if (VescaClockActive && newCombat && enemy?.id == GameLoop.BossIds.VescaP1)
                if (Debug.unityLogger.logEnabled) Debug.Log("[戦闘] " + GameLoop.VescaClock.BattleLine);
            if (VescaClockActive) Debug.Log($"[ダウン] 〈{GameLoop.VescaClock.Name}〉: {enemy?.displayName} の混乱は {_down.gauge}/{_down.gaugeMax} から");
            BossDown.ActiveNow = false; BossDown.TakenMulNow = false; BossDown.TakenBonusPctNow = 0; BossDown.ExtraNotchesNow = 0;
            if (newCombat) _down.ResetBuffs();   // 次のダウンを強める分は段の交代では消さない (2026-11-25)
            if (newCombat) { Escalation.HeavyCycleOffset = 0; Escalation.StageOffset = 0; Escalation.HeavyHpRatio = 1f; TenyoHeavy.Reset(); }
            BossDownDiag.BeginFight(enemy?.id, _down.Enabled);
            HeavyHitDiag.BeginFight(enemy?.id);   // [計装 2026-11-14] 大技の被ダメ
            FightEndDiag.Begin();   // [計装 2026-11-18] 戦闘の終わりの診断
        }

        /// <summary>ターン <paramref name="turn"/> がダウンだったら、 敵の周期 (大技・構え・〈業火の予兆〉) を取り消して頭に戻すか (<see cref="BossDown.CancelsCycle"/>)。
        /// 今の起点 (<see cref="Escalation.HeavyCycleOffset"/>) で数える ── 本体はダウンのターンの頭に起点を動かす前に呼び、 BOT は先のターンの見込みに呼ぶ (同じ関数・写しを持たない)。
        /// <paramref name="bossHpRatio"/> = ボスの HP の割合 (〈業火の予兆〉の周期が HP 50% で変わる)。 規則は口 <see cref="GameLoop.GameHooks.DownCancel"/>。</summary>
        public static bool DownCancelsCycleAt(EnemyData e, int turn, float bossHpRatio)
            => DownCancelsCycleAt(e, turn, bossHpRatio, GameLoop.GameHooks.DownCancel);

        /// <summary><see cref="DownCancelsCycleAt(EnemyData,int,float)"/> の規則を指定する版 (BOT が「規則を知らない」腕で旧い規則の見込みを作る・2026-11-26)。</summary>
        public static bool DownCancelsCycleAt(EnemyData e, int turn, float bossHpRatio, DownCancelRule rule)
        {
            if (e == null) return false;
            TurnsToClimaxAt(e, turn, bossHpRatio, out int h, out int g, out int o);
            return BossDown.CancelsCycle(rule, h, g, o);
        }

        /// <summary>ターン <paramref name="turn"/> の、 周期ごとの「山場まで何ターンか」(大技・構え・〈業火の予兆〉・−1 = その周期を持たない)。 今の起点 (<see cref="Escalation.HeavyCycleOffset"/>) で数える。</summary>
        public static void TurnsToClimaxAt(EnemyData e, int turn, float bossHpRatio, out int heavy, out int guard, out int omen)
        {
            heavy = guard = omen = -1;
            if (e == null) return;
            if (e.passiveSkills != null)
                foreach (var ps in e.passiveSkills)
                    if (ps != null && ps.internalName == ItemSystem.PassiveSkills.Effects.EmberOmen.Id)
                    { omen = ItemSystem.PassiveSkills.Effects.EmberOmenTiming.TurnsToOmen(turn, bossHpRatio); break; }
            heavy = Escalation.TurnsToHeavy(e, turn); guard = Escalation.TurnsToGuard(e, turn);
        }

        /// <summary>ターン <paramref name="turn"/> にダウンが来たら、 その種類 (<see cref="BossDown.Classify"/>・2026-11-26)。 本体のターンの頭・予告・BOT の見込みが同じ関数を呼ぶ。
        /// <paramref name="rule"/> を省くと本体の規則 (<see cref="GameLoop.GameHooks.DownCancel"/>)。</summary>
        public static DownKind DownKindAt(EnemyData e, int turn, float bossHpRatio, bool weak, bool extension = false)
            => DownKindAt(e, turn, bossHpRatio, weak, extension, GameLoop.GameHooks.DownCancel);

        public static DownKind DownKindAt(EnemyData e, int turn, float bossHpRatio, bool weak, bool extension, DownCancelRule rule)
        {
            TurnsToClimaxAt(e, turn, bossHpRatio, out int h, out int g, out int o);
            return BossDown.Classify(rule, weak, extension, h, g, o);
        }

        /// <summary>ターンの頭 (ターン番号を進めた後・敵が振る前): 待っているダウンがあれば、 このターンをダウンにする。
        /// ダウンがチャージのターンか山場 (大技・構え・予兆) のターンに重なった時だけ、 その大技は取り消し、 周期は次のターンから頭に戻す
        /// (<see cref="Escalation.HeavyCycleOffset"/>・<see cref="DownCancelsCycleAt"/>・2026-11-14 の作者の決定)。 それ以外のダウンは周期に触らない。</summary>
        /// <summary>[計装 2026-11-14] ターンの頭の自 HP / このターンはチャージだった (HeavyHitDiag のチャージのターンの HP の減り)。</summary>
        private int _diagHpTurnHead;
        private bool _diagChargeTurn;

        /// <summary>ターンの頭のダウンの処理 (<see cref="BeginBossDownTurnCore"/>) と、 始まり・解けた時の合図 (#319)。</summary>
        private void BeginBossDownTurn(CombatContext ctx)
        {
            bool was = BossDown.ActiveNow;
            _gaugeLate = false;
            BeginBossDownTurnCore(ctx);
            // 預かった混乱 (前のターンに敵が動いた後で足された分) を、 このターンの頭で最初に入れる。 大ダウンのターンは溜まらないので、 明けるまで預かったまま
            if (_gaugeCarry > 0 && !(_down.downNow && !_down.downWeakNow)) { int c = _gaugeCarry; _gaugeCarry = 0; AddBossGauge(c); }
            // 「ダウン中は発動しない」パッシブの後始末 (2026-11-20・BossDownPassives): 毒沼の主はダウンに入った時に毒 −2、 ヴェスカは前のターンの遺物を消す
            if (BossDown.ActiveNow)
            {
                if (!was && BossDownPassives.SkipsDuringDown(ItemSystem.PassiveSkills.Effects.MiasmaCorrosion.Id))
                    ItemSystem.PassiveSkills.Effects.MiasmaCorrosion.OnDownStart(ctx);
                if (BossDownPassives.SkipsDuringDown(ItemSystem.PassiveSkills.Effects.RelicScholar.Id))
                    ItemSystem.PassiveSkills.Effects.RelicScholar.ClearForDownTurn(ctx);
            }
            if (!was && BossDown.ActiveNow) RaiseCue(CombatCue.BossDownStart);
            else if (was && !BossDown.ActiveNow) RaiseCue(CombatCue.BossDownEnd);
        }

        /// <summary>このターン敵が大技の予告かチャージを出しているか (〈英雄の意志〉がターン開始時に読む・2026-11-20)。
        /// ターン開始のトリガーはダウンを立てる前に鳴るので、 待っているダウンがあれば (このターンはダウン = 予告を出さない) false。</summary>
        public bool EnemyHeavyOrChargeNow(int turn)
        {
            if (currentEnemy == null || enemyHP <= 0 || PendingDownLandsAt(turn)) return false;
            return Escalation.IsHeavyTurn(currentEnemy, turn) || Escalation.IsChargeTurn(currentEnemy, turn);
        }

        /// <summary>ターンの頭より前に読む所 (ターン開始のトリガー・ターン頭の毒): 待っているダウンがあり、 ターン <paramref name="turn"/> にそれが来るか
        /// (溜めで無効になる分は来ない = 敵は普段どおり・2026-11-26)。</summary>
        public bool PendingDownLandsAt(int turn)
            => currentEnemy != null && enemyHP > 0 && _down.Enabled && _down.pending > 0
               && DownKindAt(currentEnemy, turn, Escalation.HeavyHpRatio, _down.PendingHeadWeak, _down.PendingHeadExt) != DownKind.Resisted;

        /// <summary>このターン敵がダウン中か (〈抜き打ちの作法〉が撃の中で読む)。</summary>
        public bool EnemyDownNow => currentEnemy != null && enemyHP > 0 && _down.downNow;

        /// <summary>このターン敵がチャージ中 (大技の前で攻撃しない・天与の盾と指輪も) かダウン中か (〈隙突きの短槍〉が配線の後に読む・2026-11-21)。
        /// 予告の「チャージ」の印 (<see cref="FillDownTelegraph"/>)・敵の攻撃の解決の「攻撃しない」の枝と同じ判定。</summary>
        public bool EnemyChargingOrDownNow(int turn)
        {
            if (currentEnemy == null || enemyHP <= 0) return false;
            return _down.downNow || Escalation.IsChargeNoAttack(currentEnemy, turn) || TenyoHeavy.NoAttackAt(currentEnemy, turn);
        }

        /// <summary>次に割る目盛りを <paramref name="add"/> (HP) だけ上げる (臨界の爆発・【劇症】・〈雷鯰の棘〉〈避雷の杭〉ほか)。 今の HP より上へは行かない
        /// (今の HP の 1 下まで ── 上げるだけでは割れもひびも入らない・割るには通常攻撃が要る)。 品が預けてまだ入っていない敵へのダメージ (Kw.DamageEnemy) は今の HP から引いて数える。
        /// 目盛りの無い敵では何もしない。 ターンの解決中は CombatManager の手元の HP、 それ以外は ctx の HP の低い方を今の HP とみなす。</summary>
        public void AddBossGauge(int add)
        {
            if (!_down.Enabled || currentEnemy == null || enemyHP <= 0 || add <= 0) return;
            // 敵が行動した後に足された分 (〈黎明〉〈落城の礎石〉ほか) は預かり、 次のターンの頭で最初に入れる (2026-11-26・作者の決定: 満ちるのは常に敵が動く前)
            if (_gaugeLate) { _gaugeCarry += add; if (Debug.unityLogger.logEnabled) Debug.Log($"[混乱] 敵が動いた後の +{add} は次のターンの頭へ (預かり {_gaugeCarry})"); return; }
            if (_down.downNow && !_down.downWeakNow) return;   // 大ダウンのターンは溜まらない (連続でダウンさせない)
            int before = _down.gauge;
            bool full = _down.AddGauge(add);
            if (Debug.unityLogger.logEnabled) Debug.Log($"[{(string.IsNullOrEmpty(CombatContext.CurrentSkillId) ? "配線" : CombatContext.CurrentSkillId)}] 混乱 {before} +{add}" + (full ? " → 満ちた" : $" → {_down.gauge}/{_down.gaugeMax}"));
            if (full) OnBossGaugeFull(PassiveSkillManager.Instance?.Context?.currentTurn ?? 0);
        }

        /// <summary>混乱ゲージに入る量 = 攻撃の端子に挿して発動したダイスの、 止まった面の数字の和 (出目 +N・鋳造・怠惰・端子の倍率は入れない)。
        /// <paramref name="native"/> = ダイスごとの止まった面の数字 (<see cref="NativeDice"/>)。 本体と BOT の見積もりが同じ関数を呼ぶ。</summary>
        public static int GaugePips(TerminalOutcome o, int[] native)
        {
            if (o?.kindOfDie == null || o.slotOfDie == null || native == null) return 0;
            int s = 0;
            for (int i = 0; i < native.Length && i < o.kindOfDie.Length && i < o.slotOfDie.Length; i++)
                if (o.kindOfDie[i] == DiceTerminal.Attack && o.slotOfDie[i] >= 0 && o.fired.Contains(o.slotOfDie[i])) s += native[i];
            return s;
        }

        /// <summary>ダイスごとの止まった面の数字 (<see cref="CombatContext.equippedDiceFaces"/>[<see cref="CombatContext.playerDiceFaceIdx"/>])。
        /// <paramref name="rolls"/> が今の出目 (ctx.playerDice) と違う (BOT が仮の目で試す) か、 面配列を使わない素のロールなら、 その目をそのまま使う。</summary>
        public static int[] NativeDice(CombatContext ctx, int[] rolls)
        {
            if (rolls == null) return null;
            var r = (int[])rolls.Clone();
            var faces = ctx?.equippedDiceFaces; var idx = ctx?.playerDiceFaceIdx; var cur = ctx?.playerDice;
            if (faces == null || idx == null || cur == null || cur.Length != rolls.Length) return r;
            for (int i = 0; i < r.Length; i++) if (cur[i] != rolls[i]) return (int[])rolls.Clone();
            for (int i = 0; i < r.Length && i < idx.Length; i++) if (idx[i] >= 0 && idx[i] < faces.Length) r[i] = faces[idx[i]];
            return r;
        }

        /// <summary>配線を反映した直後 (撃と敵の攻撃より前): 攻撃に挿した止まった面の数字を混乱ゲージに足す。</summary>
        private void NoteBossGaugeWired(CombatContext ctx, TerminalOutcome o, int[] playerDice)
        {
            if (!_down.Enabled || ctx == null || o == null) return;
            int pips = GaugePips(o, NativeDice(ctx, playerDice));
            if (pips > 0) AddBossGauge(pips);
        }

        /// <summary>混乱ゲージが満ちた瞬間: Instant と同じ効き方 (山場のターン = 行動をキャンセルして次のターンは大ダウン / チャージ = 無効 / 小技 = よろめき)。</summary>
        /// <summary>混乱が満ちたターン (〈崩し斬り〉が読む) / 〈崩し斬り〉が効いたターン (1 ターン 1 回)。</summary>
        private int _gaugeFullTurn = -99, _crushTurn = -99, _gaugeFullsFight;
        /// <summary>このターンは敵が行動した後 (足された混乱は預かる) / 預かった混乱 (次のターンの頭で入れる)。</summary>
        private bool _gaugeLate;
        private int _gaugeCarry;

        /// <summary>敵の行動の直前に呼ぶ: これより後に足される混乱は次のターンの頭へ。</summary>
        private void MarkBossGaugeLate() => _gaugeLate = true;

        private void OnBossGaugeFull(int turn)
        {
            _gaugeFullTurn = turn;
            MetaProgression.Achievements.AchievementService.NoteBossDownBroken(++_gaugeFullsFight);   // 実績〈二枚抜き〉(1 戦闘で 2 回満たす)
            ApplyInstantBreak(1, turn);
            RaiseBossEvent(PassiveSkillManager.Instance?.Context, PassiveSkillTrigger.OnBossDown, 1, false);
            BossDownDiag.NoteMainBreak(1, 0, 0);
        }

        /// <summary>次のダウン (よろめきは数えない) の受けるダメージの倍率 +<paramref name="pct"/>% (上限 +100%)・そのダウンの開始時にシールド / 充電 (〈英雄の意志〉〈黒錫のライデン瓶〉・2026-11-25)。 目盛りの無い敵では何もしない。</summary>
        public void BuffNextBossDown(string label, int pct, int shield, int charge)
        {
            if (!_down.Enabled || currentEnemy == null || enemyHP <= 0) return;
            _down.BuffNext(pct, shield, charge);
            if (Debug.unityLogger.logEnabled) Debug.Log($"[{label}] 次のダウン: 受けるダメージ ×{BossDown.TakenMulCard + _down.bonusPct * 0.01f:0.0#}" + (_down.shieldOnDown > 0 ? $"・開始時シールド +{_down.shieldOnDown}" : "") + (_down.chargeOnDown > 0 ? $"・開始時充電 +{_down.chargeOnDown}" : ""));
        }

        /// <summary>次のダウンを 1 ターン延ばす (〈柱折りの発破筒〉・1 戦闘 2 回まで・2026-11-25)。 仕込めたら true。</summary>
        public bool ExtendNextBossDown(string label)
        {
            if (!_down.Enabled || currentEnemy == null || enemyHP <= 0 || !_down.ExtendNext()) return false;
            if (Debug.unityLogger.logEnabled) Debug.Log($"[{label}] 次のダウンを 1 ターン延ばす (この戦闘 {_down.extendsUsed + 1} 回目)");
            return true;
        }

        private void BeginBossDownTurnCore(CombatContext ctx)
        {
            _diagHpTurnHead = playerHP; _diagChargeTurn = false;
            // 灰燼の王の〈業火〉の周期は HP 50% で 4 → 3 ターン (2026-11-14)。 このターンの周期の数え方に使う HP の割合をターンの頭に決める (取り消しの判定より前)
            Escalation.HeavyHpRatio = currentEnemy != null ? enemyHP / (float)System.Math.Max(1, EnemyMaxHP) : 1f;
            FightEndDiag.NoteTurnHead(ctx?.currentTurn ?? 0, playerHP, Escalation.HeavyHpRatio);   // [計装 2026-11-18] 読むだけ
            BossDown.ActiveNow = false; BossDown.TakenMulNow = false; BossDown.TakenBonusPctNow = 0; BossDown.ExtraNotchesNow = 0;
            if (!_down.Enabled || ctx == null) { _down.downNow = false; _down.downWeakNow = false; return; }
            bool downTurn = _down.BeginTurn();
            // ダウンの種類 (2026-11-26・BossDown.Classify): 来たターンで決める。 溜めで無効なら、 このターンは普段どおり (目盛りは消える)。
            //   旧い規則 (ChargeOrHeavy・Always) では種類はひびかどうかだけ (今までと同じ)
            bool timed = BossDown.IsTimedFamily(GameLoop.GameHooks.DownCancel);
            bool instant = GameLoop.GameHooks.DownCancel == DownCancelRule.Instant;
            DownKind kind = DownKind.Big;
            if (downTurn)
            {
                kind = DownKindAt(currentEnemy, ctx.currentTurn, Escalation.HeavyHpRatio, _down.downWeakNow, _down.extNow);
                if (kind == DownKind.Resisted || (BossDown.DownStaggerNoStun && kind == DownKind.Stagger))
                {
                    _down.downNow = false; _down.downWeakNow = false; _down.resistedNow = true; downTurn = false;
                    BossDownDiag.NoteResisted();
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] T{ctx.currentTurn} {currentEnemy?.displayName}: 大ダウン以外では怯まない ── ダウンは無効 (目盛りは消えた)");
                }
                else _down.downWeakNow = kind == DownKind.Stagger;
            }
            // 次のダウンを強める分 (2026-11-25): よろめきでないダウンの最初のターンに使う (延長はこのダウンを 1 ターン足す)
            if (_down.TakeBuffs(out int buffShield, out int buffCharge, out bool extended))
            {
                if (buffShield > 0) ItemSystem.PassiveSkills.Effects.Rw.Shield(ctx, buffShield, "ダウン");
                if (buffCharge > 0) ctx.AddCharge(buffCharge);
                if (extended) BurstBreakFired++;   // [計装] 〈柱折りの発破筒〉の延長
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] 強めたダウン: 受けるダメージ +{_down.bonusNowPct}%" + (buffShield > 0 ? $"・シールド +{buffShield}" : "") + (buffCharge > 0 ? $"・充電 +{buffCharge}" : "") + (extended ? "・1 ターン延びる" : ""));
            }
            if (!downTurn) return;
            bool hadHeavy = currentEnemy != null && currentEnemy.heavyPeriod > 0;
            bool always = GameLoop.GameHooks.DownCancel == DownCancelRule.Always;
            // 起点を動かす前に見る。 計装の「取り消した」: 新しい規則 = 大技のチャージか大技のターンに重なった / 旧い規則 (Always) = 今までどおり大技のターンだけ数える
            int toHeavyAtDown = hadHeavy ? Escalation.TurnsToHeavy(currentEnemy, ctx.currentTurn) : -1;   // [診断] ダウンのターンの周期の位置 (起点を動かす前)
            // 周期を頭に戻すのは: 新しい規則 = 山場のターンの大ダウンだけ (よろめきは周期に触らない) / 旧い規則 = 今までどおり
            // 割った瞬間に効く規則 (Instant): 山場は割ったターンにもうキャンセルしてある。 大ダウンのターンの頭で周期を頭に戻すだけ (印は割った時に立てた)
            bool resetCycle = instant ? kind == DownKind.Big && _down.extNow && _down.resetCycleNext
                            : (!timed || kind == DownKind.Big) && DownCancelsCycleAt(currentEnemy, ctx.currentTurn, Escalation.HeavyHpRatio);
            if (instant && resetCycle) _down.resetCycleNext = false;
            bool canceled = hadHeavy && (always ? Escalation.IsHeavyTurn(currentEnemy, ctx.currentTurn)
                                       : instant ? false
                                       : timed ? resetCycle && toHeavyAtDown == 0
                                               : BossDown.IsChargeOrClimax(toHeavyAtDown));
            if (resetCycle)
                Escalation.HeavyCycleOffset = ctx.currentTurn;
            _down.heavyCanceledNow = hadHeavy && (always || canceled);
            if (canceled) RaiseBossEvent(ctx, PassiveSkillTrigger.OnHeavyCanceled, 1, false);   // 大技を取り消した (2026-11-21・〈構え崩しの鉄杭〉)
            if (canceled) MetaProgression.Achievements.AchievementService.NoteBossDownCanceledHeavy();   // 実績〈出鼻をくじく〉   // 画面の「大技は取り消し」(旧い規則はどのダウンでも大技が先へ延びる)
            BossDown.ActiveNow = true;
            BossDown.TakenMulNow = !_down.downWeakNow || ctx.poisonDownDouble;   // よろめき (ひび・2026-11-14 / 小技のターン・2026-11-26) は受けるダメージ ×1.5 なし (〈骨溶かしの黒油〉はよろめきにも ×1.5・2026-11-25)
            BossDown.TakenBonusPctNow = _down.bonusNowPct;
            BossDown.ExtraNotchesNow = !_down.downWeakNow ? System.Math.Max(0, _down.countNow - 1) : 0;   // まとめて割った分の倍率 (大ダウンだけ・2026-11-26)
            if (!instant || _down.downWeakNow) BossDownDiag.NoteDown(hadHeavy, canceled, toHeavyAtDown, _down.downWeakNow);   // Instant の大ダウンは割った時に数えた
            if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] T{ctx.currentTurn} {currentEnemy?.displayName}: 行動不能" + (!BossDown.TakenMulNow ? "・よろめき (受けるダメージは普段どおり)" : $"{(_down.downWeakNow ? "・よろめき" : "")}・受けるダメージ ×{BossDown.MulNow:0.00}")
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
            // 次のターンに来るダウンの種類 / 今割ったら次のターンのダウンの種類 (2026-11-26・完全情報)
            t.downNextKind = _down.Enabled && currentEnemy != null && _down.pending > 0
                ? (int)DownKindAt(currentEnemy, t.turn + 1, Escalation.HeavyHpRatio, _down.PendingHeadWeak, _down.PendingHeadExt) : -1;
            t.downGauge = _down.gauge; t.downGaugeMax = _down.gaugeMax; t.downGaugeGrowth = _down.gaugeGrowth;   // 混乱ゲージ (2026-11-26)
            t.downBreakNowKind = !_down.Enabled || currentEnemy == null ? -1
                : GameLoop.GameHooks.DownCancel == DownCancelRule.Instant
                    ? (_down.downNow && !_down.downWeakNow ? -1 : (int)InstantBreakKindAt(currentEnemy, t.turn, Escalation.HeavyHpRatio))   // 割った瞬間に効く (大ダウン中は倍率が上がるだけ)
                : _down.pending == 0 ? (int)DownKindAt(currentEnemy, t.turn + 1, Escalation.HeavyHpRatio, false) : -1;
            if (_down.downNow) { t.enemyAttackValue = 0; t.secondaryAttackValue = 0; }
            // 大技のチャージ (2026-11-14): 攻撃しない。 攻撃値は Escalation.HeavyFactor が 0 にしてある (予告も BOT の見込みも同じ関数)。 ここは画面の印だけ
            t.enemyCharging = !_down.downNow && Escalation.IsChargeNoAttack(currentEnemy, t.turn);
            t.tenyoGift = (int)TenyoHeavy.TelegraphAt(currentEnemy, t.turn, _down.downNow);      // ヴェスカの天与 (2026-11-15): チャージと大技のターンに、 どれが来るか
            // 灰燼の王の〈業火〉(2026-11-14): このターンが業火なら、 烈炎の分 (軽減無視・撃ったら烈炎 0)。 ダウンのターンは撃たない
            t.heavyBurst = _down.downNow ? 0 : BlazeBurstAt(currentEnemy, t.turn, BlazeStacks(ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context));
        }

        /// <summary>[旧い判定 AnyDamage の計装] 主攻撃の直後に、 主攻撃だけで割れた目盛りの数 (ターンの終わりの数と比べて、 主攻撃以外で割れた数を出す)。</summary>
        private int _downMainWouldBreak;

        /// <summary>主攻撃 (通常攻撃) が当たった直後 (主攻撃のダメージを HP から引いた所・固定ダメの枠より前・2026-11-14):
        /// 新しい判定 (MainAttackOnly) なら、 HP 以下にある未割りの目盛りを全部割る (ダウン・ひびの入った目盛りも)。 ダメージ 0 (回避・吸収) や撃破では割らない。
        /// 旧い判定 (AnyDamage) では何もしない (計装だけ)。 1 ターンに主攻撃が複数回当たる時は、 当たるたびに呼んでよい。
        /// 戻り値 = この撃で割った目盛りの数 (旧い判定・割らなかった時は 0・端子〈崩し斬り〉が読む)。</summary>
        private int NoteBossDownMainHit(int mainDmg, int turn)
        {
            _downMainWouldBreak = 0;
            if (!_down.Enabled || currentEnemy == null || enemyHP <= 0 || mainDmg <= 0) return 0;
            if (GameLoop.GameHooks.DownBreak == DownBreakRule.AnyDamage)
            {
                _downMainWouldBreak = BossDown.BrokenCount(_down.notches, currentEnemy.maxHP, enemyHP, _down.broken) - _down.broken;
                return 0;
            }
            bool instantRule = GameLoop.GameHooks.DownCancel == DownCancelRule.Instant;
            int crackedBroken, waitSum;
            int d = instantRule ? _down.BreakByMainHit(enemyHP, currentEnemy.maxHP, mainDmg, turn, out crackedBroken, out waitSum)
                                : _down.NoteMainHit(enemyHP, currentEnemy.maxHP, mainDmg, turn, out crackedBroken, out waitSum);
            if (d <= 0) return 0;
            if (instantRule) ApplyInstantBreak(d, turn);
            MetaProgression.Achievements.AchievementService.NoteBossDownBroken(d);   // 実績〈二枚抜き〉(1 回の攻撃で 2 つ)
            RaiseBossEvent(PassiveSkillManager.Instance?.Context, PassiveSkillTrigger.OnBossDown, d, false);   // 通常攻撃で割った (2026-11-21)
            BossDownDiag.NoteMainBreak(d, crackedBroken, waitSum);
            if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] 通常攻撃で目盛りを {d} つ割った (HP {enemyHP}/{currentEnemy.maxHP}"
                    + (crackedBroken > 0 ? $"・うちひびの入った目盛り {crackedBroken}" : "") + $") → 次のターンからダウン {_down.pending} ターン");
            return d;
        }

        /// <summary>[2026-11-26・Instant・GAME.md §23-36] 通常攻撃で目盛りを <paramref name="d"/> 個割った瞬間の処理 (主攻撃の直後・敵の攻撃より前)。
        /// 大ダウンのターン = そのターンの割った後のダメージの倍率を上げる (次のターンは普段どおり) /
        /// 山場のターン (と周期を持たない敵) = このターンの行動をキャンセルし、 次のターンは大ダウン (周期は大ダウンのターンの頭で頭へ) /
        /// チャージのターン = 溜め中は怯まない (無効・目盛りは消える) / 小技のターン = このターンの行動をキャンセル (よろめき・次のターンは普段どおり)。
        /// 1 ターンに 2 回の攻撃: 〈連撃〉〈多頭攻撃〉は両方止まる (同じ枝)・2 体戦は割った敵 (1 体目) だけ止まる。</summary>
        private void ApplyInstantBreak(int d, int turn)
        {
            var ctx = PassiveSkillManager.Instance?.Context;
            if (_down.downNow && !_down.downWeakNow)
            {
                BossDown.ExtraNotchesNow += d;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] 大ダウン中に目盛りを {d} つ割った: このターンの残りの受けるダメージ ×{BossDown.MulNow:0.00}");
                return;
            }
            TurnsToClimaxAt(currentEnemy, turn, Escalation.HeavyHpRatio, out int h, out int g, out int o);
            var kind = BossDown.ClassifyInstant(h, g, o);
            bool hadHeavy = currentEnemy.heavyPeriod > 0;
            if (kind == DownKind.Resisted)
            {
                BossDownDiag.NoteResisted();
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] T{turn} {currentEnemy.displayName}: 溜め中は怯まない ── 目盛り {d} つは無効 (消えた)");
                return;
            }
            _down.interruptNow = true;
            bool canceled = kind == DownKind.Big && hadHeavy && h == 0;
            BossDownDiag.NoteDown(hadHeavy, canceled, hadHeavy ? h : -1, kind == DownKind.Stagger);
            if (kind == DownKind.Big)
            {
                _down.heavyCanceledNow = canceled;
                _down.PushBig(d, BossDown.IsClimax(h, g, o));
                ApplyConfusionBossHooks(ctx, currentEnemy, turn, isBig: true);
                if (canceled) RaiseBossEvent(ctx, PassiveSkillTrigger.OnHeavyCanceled, 1, false);
                if (canceled) MetaProgression.Achievements.AchievementService.NoteBossDownCanceledHeavy();
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] T{turn} {currentEnemy.displayName}: 割った瞬間にこのターンの行動をキャンセル" + (canceled ? " (大技を止めた)" : "") + $" → 次のターンは大ダウン (目盛り {d} つ)");
            }
            else
            {
                ApplyConfusionBossHooks(ctx, currentEnemy, turn, isBig: false);
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] T{turn} {currentEnemy.displayName}: よろめき ── 割った瞬間にこのターンの行動をキャンセル (次のターンは普段どおり)");
            }
        }

        /// <summary>[2026-11-26] ボス固有の混乱効果:
        /// 1. ヴェスカ〈解析演算〉: 混乱（大ダウン・よろめき問わず）で回避率を 0% にリセット。
        /// 2. 灰燼の王〈烈炎〉: 大ダウンで烈炎スタックを 0 にリセット。
        /// 3. 灰燼の王〈灰の再生〉: 混乱（よろめき含む）で 2 ターン再生停止。</summary>
        private static void ApplyConfusionBossHooks(CombatContext ctx, EnemyData enemy, int turn, bool isBig)
        {
            if (ctx == null || enemy == null) return;
            if (ctx.enemyDodgeChance > 0f)
            {
                float prev = ctx.enemyDodgeChance;
                ctx.enemyDodgeChance = 0f;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] 混乱により〈解析演算〉の回避率 {prev:P0} → 0% にリセット");
            }
            if (isBig && HasBlazeBurst(enemy) && BlazeStacks(ctx) > 0)
            {
                int prev = BlazeStacks(ctx);
                ctx.accumulatedValues[ItemSystem.PassiveSkills.Effects.BlazeBrand.StackKey] = 0f;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] 大ダウンにより〈烈炎〉 {prev} → 0 にリセット");
            }
            if (enemy.passiveSkills != null)
            {
                foreach (var ps in enemy.passiveSkills)
                {
                    if (ps != null && ps.internalName == "AshRegrowth")
                    {
                        int until = turn + ItemSystem.PassiveSkills.Effects.AshRegrowth.StaggerSuppressTurns;
                        ctx.accumulatedValues.TryGetValue(ItemSystem.PassiveSkills.Effects.AshRegrowth.SuppressedUntilTurnKey, out var cur);
                        if (until > (int)cur) ctx.accumulatedValues[ItemSystem.PassiveSkills.Effects.AshRegrowth.SuppressedUntilTurnKey] = until;
                        if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] 混乱により〈灰の再生〉を T{until} まで停止");
                        break;
                    }
                }
            }
        }

        /// <summary>[2026-11-26・Instant] このターンに通常攻撃で割ったら何が起きるか (予告・BOT が読む)。</summary>
        public static DownKind InstantBreakKindAt(EnemyData e, int turn, float bossHpRatio)
        {
            TurnsToClimaxAt(e, turn, bossHpRatio, out int h, out int g, out int o);
            return BossDown.ClassifyInstant(h, g, o);
        }

        /// <summary>ターンの終わり (ターン終了時の削りを拾った後): 旧い判定 (AnyDamage) = 新しく割った目盛りを数え、 その数だけ次のターンからダウンを待たせる。
        /// 新しい判定 (MainAttackOnly) = HP が目盛り以下の未割りの目盛りにひびを入れ、 猶予の切れたひびを割る (よろめき)・目盛りの上へ戻っていればひびを消す。</summary>
        private void NoteBossDownTurnEnd()
        {
            if (_diagChargeTurn) { HeavyHitDiag.NoteCharge(_diagHpTurnHead - playerHP, playerMaxHP, _diagHpTurnHead, playerHP <= 0); _diagChargeTurn = false; }   // [計装 2026-11-14]
            if (!_down.Enabled || currentEnemy == null || enemyHP <= 0) { _downMainWouldBreak = 0; return; }
            int turn = PassiveSkillManager.Instance?.Context?.currentTurn ?? 0;
            if (GameLoop.GameHooks.DownBreak == DownBreakRule.AnyDamage)
            {
                int d = _down.NoteTurnEnd(enemyHP, currentEnemy.maxHP);
                if (d > 0)
                {
                    BossDownDiag.NoteAnyDamageBreak(d, System.Math.Min(d, _downMainWouldBreak));
                    RaiseBossEvent(PassiveSkillManager.Instance?.Context, PassiveSkillTrigger.OnBossDown, d, false);   // 旧い判定はひびが無い (通常攻撃の扱い)
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[ダウン] 目盛りを {d} つ割った (HP {enemyHP}/{currentEnemy.maxHP}) → 次のターンからダウン {_down.pending} ターン");
                }
                _downMainWouldBreak = 0;
                return;
            }
            _downMainWouldBreak = 0;
            int c = _down.NoteTurnEndCracks(enemyHP, currentEnemy.maxHP, turn, GameLoop.GameHooks.DownCrackGrace, GameLoop.GameHooks.DownCrackWeak,
                                            out int newCracks);
            BossDownDiag.NoteCracks(newCracks, c);
            if (newCracks > 0) Debug.Log($"[ダウン] 通常攻撃以外で目盛りを下回った: ひび {newCracks} (HP {enemyHP}/{currentEnemy.maxHP})");
            if (c > 0) Debug.Log($"[ダウン] ひびの猶予が切れて目盛りが {c} つひとりでに割れた (よろめき) → 次のターンからダウン {_down.pending} ターン");
            if (c > 0) RaiseBossEvent(PassiveSkillManager.Instance?.Context, PassiveSkillTrigger.OnBossDown, c, true);   // ひびで割れた (2026-11-21)
        }

        /// <summary>ボスの出来事 (OnBossDown / OnHeavyCanceled・2026-11-21) を配る。 <paramref name="crack"/> = ひびで割れた分 (<see cref="ItemSystem.PassiveSkills.Keywords.Kw.BossDownCrackKey"/>)。</summary>
        private static void RaiseBossEvent(CombatContext ctx, PassiveSkillTrigger t, int amount, bool crack)
        {
            if (ctx == null) return;
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Keywords.Kw.BossDownCrackKey] = crack ? 1 : 0;
            ItemSystem.PassiveSkills.Keywords.Kw.Raise(ctx, t, amount);
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Keywords.Kw.BossDownCrackKey] = 0;
        }

        /// <summary>戦闘の終わり: ダウンの倍率を戻す。</summary>
        private void EndBossDown()
        {
            BossDown.ActiveNow = false; BossDown.TakenMulNow = false; BossDown.TakenBonusPctNow = 0; BossDown.ExtraNotchesNow = 0;
            _down.downNow = false; _down.downWeakNow = false;
            Escalation.HeavyCycleOffset = 0;
            Escalation.StageOffset = 0;
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
            if (Debug.unityLogger.logEnabled) Debug.Log($"[業火] 烈炎 {stacks} × {BossParams.ParamInt(currentEnemy.id, BossParam.BlazeBurstPerStack)} = {raw} (軽減無視・シールド {absorbed} 肩代わり) → HP {playerHP} / 烈炎は 0 へ");
            return dmg;
        }
    }
}

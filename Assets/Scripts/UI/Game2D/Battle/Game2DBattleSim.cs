using System.Collections.Generic;
using CombatSystem;
using GameLoop;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// 戦闘の流れ (<b>仮置き</b>・2026-10-31 に実戦闘へ接続)。 <see cref="CombatManager"/> の「人が操作するターン」
    /// (<see cref="CombatManager.BeginInteractiveTurn"/>) を進め、 <see cref="Game2DBattleFx"/> の演出と <see cref="Game2DBattleHud"/> の GUI をつなぐ。
    /// 1 ターン: 敵の攻撃が予告される → 自分のダイスが出る → (充電を払って振り直す) → 配線 → [GO!] → 自分の攻撃 → 敵の攻撃 → 次のターン。
    /// HP・出目・配線・ダメージはすべて本物で、 この部品は値を作らない (見せる順番と間だけを持つ)。
    /// 操作: [GO! / NEXT] ボタンか Space / 出目のクリックか 1〜9 / [REROLL] ボタンか E / Q = おまかせ。
    /// 端子の山札 (ADR-0014): 出目ごとの挿し先 (<see cref="slot"/>) と、 〈写し取り〉〈焼き切り〉〈呼び戻し〉の対象 (<see cref="target"/>) を持ち、
    /// 決定で AnswerWiringSlots へ渡す。 出目の個数に上限は置かない (振った個数だけ並べる)。
    /// BOT がランを動かしている間 (<see cref="GameHooks.BotDriving"/>) は手を出さない。
    /// </summary>
    public class Game2DBattleSim : MonoBehaviour
    {
        public Game2DBattleFx fx;

        public enum P { ERoll, Await, PRoll, Wire, PAttack, Dot, EAttack, Won, Lost }
        public static bool Active { get; private set; }

        public P phase = P.ERoll;
        public float t;
        /// <summary>自分の出目 (振った個数だけ・添字 = ダイス番号)。 個数が変わると判断の写しで作り直す (<see cref="EnsureDice"/>)。</summary>
        public int[] pd = { 1, 1, 1 };
        /// <summary>敵の出目 (先頭 3 個)。 敵は 1 個しか引かない敵が多いので <see cref="enemyDiceCount"/> まで描く。</summary>
        public readonly int[] ed = { 1, 1, 1 };
        public int enemyDiceCount = 3;
        /// <summary>自分の出目の個数。</summary>
        public int DiceCount => pd.Length;
        public int turn = 1, playerHp, playerMax = 1, charge, eatk, lastTaken, stage, nextStageIn = -1;
        /// <summary>このターンの敵の防御と、 次の構えまでのターン (0 = このターンは構え / −1 = 構えない)。</summary>
        public int edef, guardIn = -1;
        /// <summary>次の大技までのターン (0 = このターンが大技 / −1 = 大技を持たない)。</summary>
        public int heavyIn = -1;
        /// <summary>ボスのダウン (2026-11-13・予告 MutualTurnTelegraph の写し): このターンはダウン / 後に続くダウンのターン数 /
        /// 次の目盛りの HP (−1 = 無し) / 目盛り (最大 HP の割合・null = ダウンしない敵) / 割った目盛りの数。</summary>
        public bool down; public int downPending, downNotchHp = -1, downBroken; public float[] downNotches;
        /// <summary>このターンのダウンは弱い (ひびで割れた・被ダメ ×1.5 なし) / 次のダウンは弱い / 目盛りごとのひびの入ったターン (−1 = なし) /
        /// 一番上のひびがひとりでに割れるターン (−1 = なし)・予告の写し (2026-11-14)。</summary>
        public bool downWeak, downNextWeak; public int[] downCracks; public int crackBreakTurn = -1;
        /// <summary>このターンは大技のチャージで、 敵は攻撃しない (予告 MutualTurnTelegraph.enemyCharging の写し・2026-11-14)。</summary>
        public bool charging;
        /// <summary>灰燼の王の〈業火〉のターンの烈炎の分 (軽減無視・予告 MutualTurnTelegraph.heavyBurst の写し・2026-11-14)。 0 = 無し。</summary>
        public int burst;
        public int version;
        public bool rolled;                  // 今のターンのダイスが出ているか (合計の表示に使う)
        /// <summary>振り直しの判断で止まっているか / 払う充電。</summary>
        public bool canReroll; public int rerollCost;
        /// <summary>武器の素の攻撃力 (本物)。</summary>
        public int baseAtk;

        private int[] _shownDice;            // 振り直しの判断の時に見せていた出目 (配線の判断で変わったかを見る)
        private int _hpBefore, _enemyBefore, _enemyMaxBefore, _combatSerial, _seenSerial = -1;
        private bool _eHit;
        private TurnResult _tr;
        // 毒・出血のダメージ (CombatManager.OnDotDamage)。 この画面が呼んだ処理の間だけ数える
        private bool _countDots; private int _poisonSeen, _bleedSeen;
        private int _poisonShown, _bleedPending, _mistPending;      // ターン頭に見せた毒 / 攻撃の後に見せる出血 / 敵の攻撃の前に見せる毒 (〈廃庭の霧吹き〉)

        public int Stage => stage;

        /// <summary>敵の周りに出す印 (2026-11-11): 頭上 = 攻撃の予告 (大技のターンは交差した剣)・右下 = デバフ (毒・出血)・左下 = バフ (構え)。
        /// 予告は敵の出目が見えてから出す (振る前は数字が無い)。</summary>
        private Game2DBattleFx.Marks Marks()
        {
            var m = new Game2DBattleFx.Marks();
            bool known = phase != P.ERoll && phase != P.Won && phase != P.Lost;
            if (known && !down && !charging) { m.intent = heavyIn == 0 ? 2 : 1; m.atk = eatk; m.guard = guardIn == 0; }   // ダウンとチャージのターンは攻撃の予告を出さない (印は絵が要る・仮置きは右の枠の文字)
            else if (known && charging) m.guard = guardIn == 0;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx != null) { m.poison = ctx.EnemyPoison; m.bleed = ctx.enemyBleedStacks; }
            return m;
        }
        public int NextStageIn => nextStageIn;
        public int EnemyDiceSum { get { int s = 0; for (int i = 0; i < enemyDiceCount && i < ed.Length; i++) s += ed[i]; return s; } }

        public struct Wired { public int atk, aDice, blk, chg; }
        /// <summary>端子の見込み (解決後は結果) の値。 攻撃の種類に 1 個も挿さないターンは攻撃しないので 0。</summary>
        public Wired GetWired()
        {
            var o = preview;
            if (o == null) return new Wired();
            return new Wired { atk = o.attackDice > 0 ? baseAtk + o.attackAdd : 0, aDice = o.attackAdd, blk = o.blockAdd, chg = o.chargeAdd };
        }

        private void Touch() { version++; }

        private void OnEnable()
        {
            var cm = CombatManager.Instance;
            if (cm != null) cm.OnCombatStart += OnCombatStart;
            CombatManager.OnDotDamage += OnDot;
        }

        private void OnDisable()
        {
            var cm = CombatManager.Instance;
            if (cm != null) cm.OnCombatStart -= OnCombatStart;
            CombatManager.OnDotDamage -= OnDot;
            Active = false; GameManager.CombatInputByScreen = false;
        }

        private void OnCombatStart(string _) { _combatSerial++; }

        private void OnDot(CombatManager.DotKind kind, int dmg)
        {
            if (!_countDots) return;
            if (kind == CombatManager.DotKind.Poison) _poisonSeen += dmg; else _bleedSeen += dmg;
        }

        /// <summary>この画面が戦闘を受け持つ場面か。</summary>
        private static bool Wanted(GameManager gm)
        {
            if (gm == null || GameHooks.BotDriving || Application.isBatchMode) return false;
            var ph = gm.CurrentPhase;
            return ph == GameManager.GamePhase.Combat || ph == GameManager.GamePhase.BattleResult;
        }

        private void Update()
        {
            var gm = GameManager.Instance; var cm = CombatManager.Instance;
            bool want = fx != null && cm != null && Wanted(gm);
            GameManager.CombatInputByScreen = want && gm.CurrentPhase == GameManager.GamePhase.Combat;
            // 決着の演出 (攻撃・撃破) を流している間は、 GameManager の Space で結果を確定させない (NEXT / END が出てから)
            GameManager.BattleResultHeldByScreen = want && Active && phase != P.Won && phase != P.Lost;
            if (!want)
            {
                if (Active) { Active = false; fx.SimDriven = false; if (Game2DBattleFx.Showing) fx.Show(false); }
                return;
            }
            if (!Active)
            {
                Active = true; fx.SimDriven = true; _seenSerial = _combatSerial;
                fx.Show(true); EnterFight(cm);
            }
            else if (_seenSerial != _combatSerial && gm.CurrentPhase == GameManager.GamePhase.Combat)
            {
                // 続けて次の戦闘が始まった (激戦の 2 戦目・イベントの戦闘など)
                _seenSerial = _combatSerial; fx.NextEnemy(); EnterFight(cm);
            }
            fx.marks = Marks();
            if (fx.IsIntro) { t = 0; return; }      // 切り替わり・出現の演出が終わるまで、 ターンを始めない
            // 画面の外で戦闘が閉じた (逃走・強制決着など): 判断待ちのまま残らないよう、 決着の表示へ
            if (!cm.IsCombatActive && !cm.IsAwaitingDecision && (phase == P.ERoll || phase == P.PRoll || phase == P.Wire))
            {
                if (cm.LastInteractiveResult.HasValue) _tr = cm.LastInteractiveResult.Value; else _tr.playerHPAfter = gm.Run != null ? gm.Run.playerHP : 0;
                EndFight(cm);
            }
            Keys();
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime); t += dt;
            switch (phase)
            {
                case P.ERoll:
                    if (cm.IsCombatActive && gm.CurrentPhase == GameManager.GamePhase.Combat) BeginTurn(cm);
                    break;
                case P.PRoll:
                {
                    float k = t / 0.5f;
                    var real = cm.PendingDecision?.playerDice;
                    if (real != null) EnsureDice(real.Length);
                    for (int i = 0; i < pd.Length; i++)
                    {
                        int fin = real != null && i < real.Length ? real[i] : 0;
                        if (k < 0.5f + i * 0.14f) { if (Random.value < 0.55f) pd[i] = Random.Range(1, 7); } else pd[i] = fin;
                    }
                    if (t >= 0.5f) { SyncDecision(cm, false); phase = P.Wire; rolled = true; t = 0; TutorialOnWire(); }
                    Touch(); break;
                }
                case P.PAttack:
                case P.Dot:
                    if (t > 0.05f && (fx.IsIdle || fx.IsWon)) AfterPlayerAttack(cm);
                    break;
                case P.EAttack:
                    if (!_eHit && fx.EnemyHitLanded) { _eHit = true; playerHp = _tr.playerHPAfter; Touch(); }      // 当たった拍子 (ふつうの攻撃は 0.54 秒・大技は絵の当たるコマ)
                    if (t > 0.1f && fx.IsIdle && !_flameShown && cm.IsCombatActive && BossFx.KindOf(cm.CurrentEnemy?.id) == BossFx.Kind.Judge)
                    {
                        // 審判官〈審判の炎〉(毎ターン末・#354): 敵の攻撃の後に流し、 終わるまで次のターンへ進まない (ダメージは攻撃の数字に含めて出している)
                        _flameShown = true; fx.PlayMove("move-judge-flame", 0f, true); t = 0; break;
                    }
                    if (t > 0.1f && fx.IsIdle)
                    {
                        playerHp = _tr.playerHPAfter;
                        if (!cm.IsCombatActive) EndFight(cm);
                        else { phase = P.ERoll; t = 0; }
                        Touch();
                    }
                    break;
            }
        }

        private void EnterFight(CombatManager cm)
        {
            phase = P.ERoll; t = 0; rolled = false;
            _poisonShown = _bleedPending = _mistPending = 0; _countDots = false;
            playerHp = cm.PlayerHP; playerMax = Mathf.Max(1, cm.PlayerMaxHP); baseAtk = cm.PlayerAttackPower;
            fx.SetEnemyHp(cm.EnemyHP, cm.EnemyMaxHP);
            fx.WeaponIndex = WeaponIndexOf(GameManager.Instance?.Run?.equippedWeaponId);
            fx.WeaponSheetFamily = SheetFamilyOf(GameManager.Instance?.Run?.equippedWeaponId);
            _chevPhase = -1; _blazeStacks = 0; _flameShown = false;
            for (int i = 0; i < slot.Length; i++) slot[i] = -1;
            board.Clear(); target = new int[0]; preview = null; lastOutcome = null; focusSlot = -1;
            Touch();
            // 手引き (issue #86): 「戦闘の形式」は初回戦闘の直前・「層とボス」は層の底のボスに着いた時
            Game2DTutorial.Request(TutorialTopics.CombatForm);
            var foe = GameManager.Instance?.CurrentEnemy;
            if (foe != null && BossIds.IsBoss(foe.id)) Game2DTutorial.Request(TutorialTopics.LayerBoss);
        }

        /// <summary>配線の判断に入った時 (ダイスを振り終えた時) の手引き: 「ダイスを振る」「敵の予告」「端子に挿す」(初回戦闘)・「逃げる」(「端子に挿す」を読んだ後の、 逃げられる戦闘)。</summary>
        private void TutorialOnWire()
        {
            Game2DTutorial.Request(TutorialTopics.RollDice);
            Game2DTutorial.Request(TutorialTopics.Telegraph);
            Game2DTutorial.Request(TutorialTopics.Plug);
            if (Game2DTutorial.Seen(TutorialTopics.Plug) && CanFlee) Game2DTutorial.Request(TutorialTopics.Flee);
        }

        /// <summary>出目の個数を n に合わせる (挿し先は持ち越せる分だけ持ち越す)。 個数の上限は置かない。</summary>
        private void EnsureDice(int n)
        {
            if (n < 0) n = 0;
            if (pd.Length == n && slot.Length == n) return;
            var np = new int[n]; var ns = new int[n];
            for (int i = 0; i < n; i++) { np[i] = i < pd.Length ? pd[i] : 1; ns[i] = i < slot.Length ? slot[i] : -1; }
            pd = np; slot = ns; Touch();
        }

        /// <summary>ターンを始める: 敵の攻撃が予告され、 自分のダイスが出たところ (最初の判断) で止まる。</summary>
        private void BeginTurn(CombatManager cm)
        {
            _hpBefore = cm.PlayerHP; _enemyBefore = cm.EnemyHP; _enemyMaxBefore = cm.EnemyMaxHP;
            rolled = false; _eHit = false; roles.Clear();
            _countDots = true; _poisonSeen = _bleedSeen = 0;
            bool paused = cm.BeginInteractiveTurn();
            int regen = cm.EnemyHP - _enemyBefore + _poisonSeen;      // ターン頭に敵が回復した量 (毒で減った分を足し戻す・〈灰の再生〉の絵を出すか)
            // 端子の山札: このターンの場 (常設 + 手札) を写す。 判断なしで解決したターンも、 発動の表示はこの並びの添字で読む
            TakeBoard(cm);
            // ターンの頭の毒: ここで見せて HP の表示を先に減らす (本当の HP は攻撃と一緒に減るので、 尽きる分は残す)
            _poisonShown = Mathf.Min(_poisonSeen, Mathf.Max(0, _enemyBefore - 1));
            _poisonSeen = _bleedSeen = 0;      // ここから先は判断への答えで入った分 (Resolve で読む)
            if (_poisonShown > 0) fx.DotTick(false, _poisonShown);
            if (!paused) { Resolve(cm); return; }      // 判断なしで解決したターン (逃走・刻限など)
            if (board.Count > 0) Game2DTutorial.Request(TutorialTopics.Deck);      // 手引き「端子の山札」: 最初の手札 (issue #86)
            SyncDecision(cm, true);
            TurnStartFx(cm, regen);
            Game2DSfx.DiceRoll(4, 0.45f, 0.55f);
            phase = P.PRoll; t = 0; Touch();
        }

        /// <summary>止まっている判断の中身を表示へ写す。 turnStart = ターンの頭 (挿し方を空から始める。 振り直しでは手元の挿し方を残す)。</summary>
        private void SyncDecision(CombatManager cm, bool turnStart)
        {
            var p = cm.PendingDecision; if (p == null) return;
            var tele = p.tele;
            turn = tele.turn; eatk = tele.enemyAttackValue; stage = tele.escalationStage; edef = tele.enemyDefense; guardIn = tele.turnsToGuard; heavyIn = tele.turnsToHeavy;
            down = tele.enemyDown; charging = tele.enemyCharging; burst = tele.heavyBurst; downPending = tele.downPending; downNotchHp = tele.downNextNotchHp; downNotches = tele.downNotches; downBroken = tele.downBroken;
            downWeak = tele.enemyDownWeak; downNextWeak = tele.downPendingHeadWeak; downCracks = tele.downCrackTurns; crackBreakTurn = tele.downCrackBreakTurn;
            if (downNotches != null) Game2DTutorial.Request(TutorialTopics.BossDown);      // 手引き「ボスのダウン」: 目盛りのあるボスとの初めての戦闘 (第 2 層のボスから)
            nextStageIn = tele.nextThresholdTurn > 0 ? tele.nextThresholdTurn - tele.turn : -1;
            if (stage >= 1) Game2DTutorial.Request(TutorialTopics.Long);      // 手引き「戦闘の長期化」: 敵の攻撃が初めて段を上げた (issue #86)
            var e = tele.enemyDice; enemyDiceCount = e != null ? Mathf.Min(3, e.Length) : 0;
            for (int i = 0; i < 3; i++) ed[i] = e != null && i < e.Length ? e[i] : 0;
            EnsureDice(p.playerDice != null ? p.playerDice.Length : 0);
            if (phase != P.PRoll) for (int i = 0; i < pd.Length; i++) pd[i] = p.playerDice[i];
            canReroll = p.kind == TurnPauseKind.Reroll; rerollCost = p.rerollCost;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            charge = ctx != null ? ctx.GetCharge() : 0;
            _shownDice = p.playerDice != null ? (int[])p.playerDice.Clone() : null;
            TakeRoles(p, ctx);
            // ターンの頭は挿し方を空から (または おまかせ) / 振り直しの後は、 挿せなくなった出目だけ外す
            if (turnStart)
            {
                for (int i = 0; i < slot.Length; i++) slot[i] = -1;
                for (int s = 0; s < target.Length; s++) target[s] = -1;
                for (int s = 0; s < keepMark.Length; s++) keepMark[s] = false;
                lastOutcome = null; focusSlot = -1;
                if (AutoWireOnTurnStart) AutoWire(false);
            }
            else ValidateSlots();
            TakeCatPaw(p);
            RecomputePreview();
        }

        private void Keys()
        {
            if (Game2DModal.Active) return;      // 窓 (敵の詳細・設定・道具) が出ている間は戦闘を進めない (2026-11-11)
            // Space は配線の決定だけ。 戦闘結果の確定は GameManager の Space が受ける (両方で受けると次のフェーズまで流れる)
            if (Input.GetKeyDown(KeyCode.Space) && phase == P.Wire) { GameManager.ScreenUsedSpaceFrame = Time.frameCount; Button(); }
            if (Input.GetKeyDown(KeyCode.E)) Reroll();
            for (int i = 0; i < 9 && i < pd.Length; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) CycleWire(i);      // 10 個目以降はクリックで
            if (Input.GetKeyDown(KeyCode.Q)) AutoWire();
        }

        public bool CanOperate => phase == P.Wire;
        public bool CanRerollNow => phase == P.Wire && canReroll && charge >= rerollCost;
        public bool ButtonEnabled => phase == P.Wire || phase == P.Won || phase == P.Lost;
        public string ButtonLabel => phase == P.Wire ? "決める" : phase == P.Won ? "次へ" : phase == P.Lost ? "終わる" : "…";

        /// <summary>決定ボタン (GO! → NEXT)。</summary>
        public void Button()
        {
            var cm = CombatManager.Instance; var gm = GameManager.Instance;
            if (fx == null || cm == null || gm == null) return;
            switch (phase)
            {
                case P.Wire:
                {
                    var p = cm.PendingDecision;
                    if (p == null) break;
                    if (p.kind == TurnPauseKind.Reroll)
                    {
                        // 振り直さない → 配線の判断へ。 出目が変わっていなければ (ふつうはそのまま) 続けて配線を渡す
                        int atkShown = eatk; bool downShown = down;
                        cm.AnswerReroll(false);
                        if (!cm.IsAwaitingDecision) { Resolve(cm); break; }
                        if (!SameDice(cm.PendingDecision.playerDice)) { SyncDecision(cm, false); Game2DSfx.Play("ui_tab"); Touch(); break; }
                        // 振り直しから配線へ進む時は必ず予告を写し直す (振り直しの判断の予告は仮組みで、 配線の予告と数字が違うことがある)。
                        //   攻撃の数字かダウンが変わったら、 人が見ないまま決めないよう、 写し直した所で止まる (もう一度押すと決まる)。
                        p = cm.PendingDecision;
                        // 〈猫の手〉が挿した: 人が見ないまま決めないよう、 猫の 1 組を見せて止まる (もう一度押すと決まる)。 写し直す前の挿し方で見る
                        bool catNew = p.kind == TurnPauseKind.Wiring && p.catDie >= 0 && (p.catDie >= slot.Length || slot[p.catDie] != p.catSlot);
                        SyncDecision(cm, false); Touch();
                        if (catNew) { Game2DSfx.Play("wire", 1f, 1.1f); break; }
                        if (eatk != atkShown || down != downShown) { Game2DSfx.Play("ui_tab"); break; }
                    }
                    if (p.kind == TurnPauseKind.Wiring)
                    {
                        Game2DSfx.Play("decide");
                        int n = p.playerDice != null ? p.playerDice.Length : 0;
                        cm.AnswerWiringSlots(BuildSlots(n), (int[])target.Clone(), 0, (bool[])keepMark.Clone());   // 残す印は本体が KeepFromMarks で直す (挿した端子には効かない)
                        if (!cm.IsAwaitingDecision) Resolve(cm);
                    }
                    break;
                }
                case P.Won:
                case P.Lost:
                    Game2DSfx.Play("ui_confirm");
                    if (gm.CurrentPhase == GameManager.GamePhase.BattleResult) gm.ConfirmBattleResult();
                    break;
            }
            Touch();
        }

        /// <summary>充電を払って全ダイスを振り直す。</summary>
        public void Reroll()
        {
            var cm = CombatManager.Instance;
            if (cm == null || !CanRerollNow) return;
            Game2DSfx.Play("decide"); Game2DSfx.DiceRoll();
            cm.AnswerReroll(true);
            if (!cm.IsAwaitingDecision) { Resolve(cm); return; }
            phase = P.PRoll; t = 0; rolled = false; SyncDecision(cm, false); Touch();
        }

        // ── 役 (ADR-0011) の表示 ──
        //   役は振り終えた出目と、 止まった面の段 (出目パーツの Tier) だけで決まる (端子の挿し方は関係しない)。 本体が役を適用するのは解決の時なので、
        //   判断の間は同じ判定 (YachtRoles.Evaluate・封じられた役は除く) で見込みを出し、 解決の後は本体の結果 (ctx.firedRolesThisTurn) に置き換える。

        /// <summary>このターンに成立する役 (判断の間は見込み・解決の後は適用された役)。 3 個の出目では高々 1 つ。 ターンの頭で空にする。</summary>
        public readonly List<RoleKind> roles = new List<RoleKind>(2);

        private void TakeRoles(TurnPause p, ItemSystem.PassiveSkills.CombatContext ctx)
        {
            roles.Clear();
            if (p == null || ctx == null || p.playerDice == null) return;
            YachtRoles.Evaluate(p.playerDice, p.playerFaceIdx, ctx.equippedFaceTiers, roles);
            for (int i = roles.Count - 1; i >= 0; i--)
                if ((ctx.sealedRoleMask & (1 << (int)roles[i])) != 0) roles.RemoveAt(i);      // 〈凶運〉で封じられた役は成立しても効かない (ResolveRoles と同じ)
        }

        /// <summary>出目 i が、 いま見せている役をつくる目か。</summary>
        public bool IsRoleDie(int i)
        {
            if (!rolled || i < 0 || i >= pd.Length) return false;
            foreach (var k in roles) if (YachtRoles.FaceOf(k) == pd[i]) return true;
            return false;
        }

        // ── 消耗品・逃げる (配線の判断の間だけ) ──

        /// <summary>この戦闘で逃げられるか (層のボス・イベント戦は不可)。</summary>
        public bool CanFlee
        {
            get
            {
                var cm = CombatManager.Instance; var e = GameManager.Instance?.CurrentEnemy;
                return CanOperate && cm != null && cm.FleeAllowed && !cm.FleeRequested && (e == null || !BossIds.IsBoss(e.id));
            }
        }

        /// <summary>逃げると宣言して、 このターンを解決する (こちらは攻撃せず、 敵の予告攻撃をブロックで受けて離脱)。</summary>
        public bool Flee()
        {
            var cm = CombatManager.Instance;
            if (!CanFlee || !cm.RequestFlee()) return false;
            Game2DSfx.Play("decide");
            Button();      // 配線を渡してターンを進める (逃げのターンとして解決される)
            return true;
        }

        /// <summary>消耗品を使える段か: ターンの判断 (振り直し・配線) で止まっている間だけ。 本体は CombatManager.Interactive のイテレーターで待っている。
        /// BOT が戦闘中に消耗品を使う所 (ターンの間・配線の方策の中) と同じく、 敵の攻撃が予告されてから、 そのターンが解決する前。</summary>
        public bool CanUseItems
        {
            get { var cm = CombatManager.Instance; return CanOperate && cm != null && cm.IsCombatActive && cm.IsAwaitingDecision; }
        }

        /// <summary>判断の間に消耗品を使う。 <b>本体の <see cref="Consumables.Use"/> をそのまま呼ぶ</b> (BOT と同じ関数・画面は値を作らない)。
        /// 使えたら HP・充電の表示を写し直す (シールド・与ダメは HUD が毎フレーム読み直す)。</summary>
        public bool UseItem(string id)
        {
            var gm = GameManager.Instance;
            if (!CanUseItems || gm?.Run == null) return false;
            int hp0 = CombatManager.Instance.PlayerHP;
            if (!Consumables.Use(gm.Run, id)) return false;
            AfterItem(hp0);
            return true;
        }

        /// <summary>〈シュヴァリエのレイピア〉のコントラタックを切り替える (消費しない)。 本体の <see cref="Consumables.TryUseRapier"/> を呼ぶ (BOT と同じ関数)。</summary>
        public bool UseRapier()
        {
            var gm = GameManager.Instance;
            if (!CanUseItems || gm?.Run == null) return false;
            int hp0 = CombatManager.Instance.PlayerHP;
            if (!Consumables.TryUseRapier(gm.Run)) return false;
            AfterItem(hp0);
            return true;
        }

        /// <summary>使った後の表示の写し。 回復した分はターンの頭の HP にも足す (敵の攻撃の演出で見せる被ダメ = 頭の HP − 解決後の HP を、 回復で小さく見せない)。</summary>
        private void AfterItem(int hpBefore)
        {
            var cm = CombatManager.Instance;
            if (cm != null) { _hpBefore += cm.PlayerHP - hpBefore; playerHp = cm.PlayerHP; playerMax = Mathf.Max(1, cm.PlayerMaxHP); }
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx != null) charge = ctx.GetCharge();
            Touch();
        }

        /// <summary>出目 i の行き先を順に変える (dir = 1 で次・−1 で前)。 場の端子 → 挿さない の順で、 挿せない端子は飛ばす。</summary>
        public void CycleWire(int i, int dir = 1)
        {
            if (!CanOperate || i < 0 || i >= pd.Length) return;
            CycleSlot(i, dir >= 0 ? 1 : -1);
        }

        // ── 端子の山札 (ADR-0014) ──
        //   出目を場の端子 (常設 + 手札) へ 1 個ずつ挿す。 挿し方を変えるたびに TerminalCards.Resolve を状態の写しで呼んで見込みを出す
        //   (式は本体の 1 か所だけ ── ここは呼ぶだけ)。 決定で CombatManager.AnswerWiringSlots へ渡す。
        //   (旧い配線の画面 ── 攻撃・防御・充電・特殊の 4 択を出目ごとに回す ── は 2026-11-11 に削除。 戦闘は常に山札で戦う)

        /// <summary>ターンの頭に おまかせの挿し方を入れておくか。 false = 空 (全部「挿さない」) から選ぶ。</summary>
        private static readonly bool AutoWireOnTurnStart = false;      // const にすると「到達しないコード」の警告が出る

        /// <summary>出目ごとの挿し先 (場の添字・−1 = 挿さない)。 長さ = 出目の個数。</summary>
        public int[] slot = { -1, -1, -1 };
        /// <summary>〈写し取り〉〈焼き切り〉〈呼び戻し〉の対象 (添字 = その端子の場の位置・値 = 対象の場の位置・−1 = 選んでいない = 左端の候補)。 長さ = 場の枚数。</summary>
        public int[] target = new int[0];
        /// <summary>手札の規則 Pick (2026-11-13): 次のターンも手札に残す印 (添字 = 場の位置)。 挿していない手札の端子を押すと付け外しする。 長さ = 場の枚数。</summary>
        public bool[] keepMark = new bool[0];
        /// <summary>このターンの場の写し (添字 = 挿し先)。</summary>
        public readonly List<string> board = new List<string>();
        /// <summary>今の挿し方の見込み。 解決した後は結果 (<see cref="lastOutcome"/> と同じ)。 null あり。</summary>
        public TerminalOutcome preview;
        /// <summary>解決したターンの結果 (発動した端子を光らせる)。 次のターンの手札が来るまで残す。 null = 判断の前。</summary>
        public TerminalOutcome lastOutcome;
        /// <summary>説明を出す端子 (画面で押して選んだ端子の場の添字。 −1 = なし)。 ターンの頭に外す。</summary>
        public int focusSlot = -1;

        public bool ShowingResult => lastOutcome != null;

        private void TakeBoard(CombatManager cm)
        {
            board.Clear(); if (cm.TerminalDeck != null) board.AddRange(cm.TerminalDeck.board);
            boardSerial++;
            lastOutcome = null; preview = null;      // 前のターンの結果は前の場の添字なので持ち越さない
            for (int i = 0; i < slot.Length; i++) if (slot[i] >= board.Count) slot[i] = -1;
            target = new int[board.Count]; for (int s = 0; s < target.Length; s++) target[s] = -1;
            keepMark = new bool[board.Count];
        }

        /// <summary>見積もりに要る入力。 本体が判断の写しに詰めた盤面 (振り直しの判断でも配線の判断でも入っている) を使う。</summary>
        private bool TermInputs(out int[] dice, out TerminalEnv env, out TerminalBattleState st)
        {
            var cm = CombatManager.Instance; var p = cm != null ? cm.PendingDecision : null;
            dice = p?.playerDice; env = default; st = null;
            if (cm == null || cm.TerminalDeck == null || dice == null || board.Count == 0) return false;
            if (p.board != null) { env = p.terminalEnv; st = p.terminalState; }
            else env = cm.TerminalEnvNow(ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context, p.tele.enemyAttackValue);
            if (st == null) st = cm.TerminalDeck.state;
            return true;
        }

        private int[] BuildSlots(int n)
        {
            var s = new int[n];
            for (int i = 0; i < n; i++) s[i] = i < slot.Length ? slot[i] : -1;
            return s;
        }

        private void RecomputePreview()
        {
            if (lastOutcome != null) { preview = lastOutcome; return; }
            if (!TermInputs(out var dice, out var env, out var st)) { preview = null; return; }
            preview = TerminalCards.Resolve(board, BuildSlots(dice.Length), dice, env, st.Clone(), target);
        }

        /// <summary>出目 i を端子 s に挿せるか (挿せる数・払う充電・出目の条件)。 他の出目との関係を見る条件は、 そろう見込みがあれば通す
        /// (〈双発〉の 1 個目を挿せるように)。 本当に発動するかは見込み (Resolve) が決める。</summary>
        public bool Accepts(int i, int s)
        {
            if (!TermInputs(out var dice, out var env, out var st) || i < 0 || i >= dice.Length || i >= slot.Length || s < 0 || s >= board.Count) return false;
            if (st.IsStopped(s)) return false;      // ヴェスカ〈停滞する時間〉で止まった端子は挿せない (Resolve も外す)
            var d = TerminalCards.Get(board[s]); if (d == null) return false;
            int used = 0;
            for (int j = 0; j < slot.Length; j++) if (j != i && slot[j] == s) used++;
            if (used >= d.capacity) return false;
            if (d.chargeCost > 0 && env.charge < d.chargeCost) return false;
            if (board[s] == TerminalIds.BigDischarge && env.charge <= 0) return false;
            if (st.ignoreCond) return true;
            // 〈増圧〉: 他の出目が増圧に挿さっていれば、 その数だけ +1
            int v = dice[i];
            for (int j = 0; j < slot.Length && j < dice.Length; j++)
                if (j != i && slot[j] >= 0 && slot[j] < board.Count && board[slot[j]] == TerminalIds.Grinder) v++;
            switch (d.cond)
            {
                case TerminalCond.SameElsewhere:
                case TerminalCond.PairSame:
                    for (int j = 0; j < dice.Length; j++) if (j != i && dice[j] == dice[i]) return true;
                    return false;
                default: return TerminalCards.CondOk(d.cond, v);
            }
        }

        /// <summary>場の s が、 このターン止まっているか (ヴェスカ〈停滞する時間〉)。 画面が暗くして印を付けるのに使う。</summary>
        public bool IsStopped(int s)
            => TermInputs(out _, out _, out var st) ? st.IsStopped(s) : CombatManager.Instance?.TerminalDeck?.state.IsStopped(s) ?? false;

        /// <summary>端子 s にあと何個挿せるか (挿せる数 − 今挿している出目の数)。</summary>
        public int Remaining(int s)
        {
            var d = s >= 0 && s < board.Count ? TerminalCards.Get(board[s]) : null; if (d == null) return 0;
            int n = 0; for (int i = 0; i < slot.Length; i++) if (slot[i] == s) n++;
            return Mathf.Max(0, d.capacity - n);
        }

        private bool IsDie(int i) => i >= 0 && i < slot.Length;

        // ── 挑戦カード〈猫の手〉(#5) ──
        //   本体が配線の判断の頭に 1 組 (出目 catDie → 場の catSlot) を先に挿したら、 その出目は動かせない (挿し替え・外す・おまかせでも残す)。

        /// <summary>このターン〈猫の手〉が先に挿した出目 (−1 = 無し)。 画面の印に使う。</summary>
        public int catDie = -1;
        /// <summary>〈猫の手〉が挿した端子 (場の添字・−1 = 無し)。</summary>
        public int catSlot = -1;

        /// <summary>出目 i が〈猫の手〉に挿されていて動かせないか。</summary>
        public bool IsCatLocked(int i) => catDie >= 0 && i == catDie;

        /// <summary>判断の写しから猫の 1 組を読み、 挿し方に入れる (配線の判断の時だけ入る)。</summary>
        private void TakeCatPaw(TurnPause p)
        {
            catDie = catSlot = -1;
            if (p == null || p.kind != TurnPauseKind.Wiring || p.catDie < 0 || p.catSlot < 0 || p.catSlot >= board.Count || !IsDie(p.catDie)) return;
            catDie = p.catDie; catSlot = p.catSlot;
            // 猫の端子が挿せる数を超えるなら、 他の出目を後ろから外す (本体の TerminalWiring.WithPair と同じ規則)
            var w = TerminalWiring.WithPair(slot, slot.Length, board, catDie, catSlot);
            for (int i = 0; i < slot.Length; i++) slot[i] = w[i];
        }

        /// <summary>出目 die へ端子 s を挿せるか (画面のドラッグ用)。 fromDie = つかんで動かしている元の出目 (手札からなら −1)。
        /// die に別の端子が挿さっていれば入れ替え (前の端子は手札へ戻る) とみなす。</summary>
        public bool CanPlug(int die, int s, int fromDie = -1)
        {
            if (!CanOperate || !IsDie(die) || s < 0 || s >= board.Count) return false;
            if (IsCatLocked(die) || IsCatLocked(fromDie)) return false;      // 猫の出目は動かせない
            int keepFrom = IsDie(fromDie) ? slot[fromDie] : -1, keepDie = slot[die];
            if (IsDie(fromDie)) slot[fromDie] = -1;
            slot[die] = -1;
            bool ok = Accepts(die, s);
            slot[die] = keepDie; if (IsDie(fromDie)) slot[fromDie] = keepFrom;
            return ok;
        }

        /// <summary>出目 die へ端子 s を挿す (fromDie からの移動なら元を外す)。 挿せなければ false (何も変えない)。</summary>
        public bool Plug(int die, int s, int fromDie = -1)
        {
            if (!CanPlug(die, s, fromDie)) return false;
            if (IsDie(fromDie) && fromDie != die) slot[fromDie] = -1;
            slot[die] = s;
            var d = TerminalCards.Get(board[s]);
            Game2DSfx.Play("wire", 1f, d != null ? 1f + (int)d.kind * 0.12f : 1f);
            RecomputePreview(); Touch();
            return true;
        }

        /// <summary>出目 die から端子を外す (手札へ戻す)。</summary>
        public void Unplug(int die)
        {
            if (!CanOperate || !IsDie(die) || slot[die] < 0 || IsCatLocked(die)) return;
            slot[die] = -1; Game2DSfx.Play("wire", 1f, 0.85f);
            RecomputePreview(); Touch();
        }

        /// <summary>まだ端子 s を挿していない出目のどれかに、 s を挿せるか (手札の USB の明暗)。</summary>
        public bool HandFits(int s)
        {
            for (int i = 0; i < slot.Length; i++) if (slot[i] != s && CanPlug(i, s)) return true;
            return false;
        }

        /// <summary>場が新しくなるたびに増える (画面が引いてくる動きを始める合図)。</summary>
        public int boardSerial;

        private void CycleSlot(int i, int dir)
        {
            int m = board.Count; if (m == 0 || !IsDie(i) || IsCatLocked(i)) return;
            int n = m + 1, cur = slot[i] < 0 || slot[i] >= m ? m : slot[i];      // 並び: 0..m-1 → 挿さない (= m)
            for (int k = 1; k <= n; k++)
            {
                int c = ((cur + dir * k) % n + n) % n;
                if (c == m || Accepts(i, c)) { slot[i] = c == m ? -1 : c; break; }
            }
            var d = slot[i] >= 0 ? TerminalCards.Get(board[slot[i]]) : null;
            Game2DSfx.Play("wire", 1f, d != null ? 1f + (int)d.kind * 0.12f : 0.85f);
            RecomputePreview(); Touch();
        }

        /// <summary>振り直しで出目が変わった: 挿せなくなった出目を外す。</summary>
        private void ValidateSlots()
        {
            for (int i = 0; i < slot.Length; i++)
                if (slot[i] >= 0 && (slot[i] >= board.Count || !Accepts(i, slot[i]))) slot[i] = -1;
        }

        /// <summary>おまかせ (<see cref="TerminalWiring.Auto"/>) の挿し方にする (対象は選び直さない ── 選んでいなければ左端の候補)。</summary>
        public void AutoWire(bool sound = true)
        {
            if (sound && !CanOperate) return;
            if (!TermInputs(out var dice, out var env, out var st)) return;
            var w = TerminalWiring.Auto(board, dice, env, st, catDie, catSlot);      // 猫の 1 組は外さない
            for (int i = 0; i < slot.Length; i++) slot[i] = w != null && i < w.Length ? w[i] : -1;
            focusSlot = -1;
            if (sound) { Game2DSfx.Play("wire", 1f, 1.1f); RecomputePreview(); Touch(); }
        }

        /// <summary>説明を出す端子を選ぶ (画面が端子の行を押した時)。</summary>
        public void Focus(int s)
        {
            focusSlot = s >= 0 && s < board.Count ? s : -1; Touch();
            if (focusSlot >= 0) Game2DTutorial.Request(TutorialTopics.Kinds);      // 手引き「端子の種類」: 初めて端子の説明を開いた (issue #86)
        }

        // ── 〈写し取り〉〈焼き切り〉〈呼び戻し〉の対象 ──
        //   これらに出目を挿すと (見込みで発動すると)、 手札の「挿していない端子」が候補になる。 候補の USB をその端子の上へ落とすと対象が決まる。
        //   選んでいない・選んだ端子が候補でなくなった時は左端の候補 (候補が 1 枚なら自動でそれ)。 判定は見込み (Resolve) と同じ関数で行う。

        /// <summary>場の s が、 見込みで発動する対象を取る端子か。</summary>
        public bool IsPicker(int s)
        {
            var o = preview;
            return o != null && s >= 0 && s < board.Count && TerminalCards.PicksTarget(board[s]) && o.fired.Contains(s);
        }

        /// <summary>対象を取る端子 picker の、 今の対象 (場の添字・−1 = 候補なし)。 選んだ端子が候補ならそれ、 でなければ左端の候補。</summary>
        public int TargetOf(int picker)
        {
            var o = preview; if (o == null) return -1;
            int want = picker >= 0 && picker < target.Length ? target[picker] : -1;
            if (TerminalCards.IsCandidate(board, o, picker, want)) return want;
            var c = TerminalCards.TargetCandidates(board, o, picker);
            return c.Count > 0 ? c[0] : -1;
        }

        /// <summary>s が、 発動する対象を取る端子のどれかの候補か (印を付ける)。 chosen = どれかの今の対象になっている。</summary>
        public bool IsTargetCandidate(int s, out bool chosen)
        {
            chosen = false; bool any = false; var o = preview;
            if (o == null || ShowingResult) return false;
            for (int p = 0; p < board.Count; p++)
            {
                if (!IsPicker(p) || !TerminalCards.IsCandidate(board, o, p, s)) continue;
                any = true;
                if (TargetOf(p) == s) chosen = true;
            }
            return any;
        }

        // ── 〈呼び戻し〉(手札の持ち越しの規則): 捨て札から 1 枚を選ぶ ──
        //   対象は場ではなく捨て札 (場に見えていない) なので、 画面は窓で選ぶ (Game2DBattleHud.OpenRecall)。 値は target[その端子の場の位置] = 捨て札の添字。
        //   選ばなければ本体の既定 (最後に捨てた 1 枚・TerminalDeckState.RecallPick)。 選んだ値も既定も、 本体と同じ関数で読む。

        // ── 手札の規則 Pick: 残す印 ──
        //   挿していない手札の端子を押す (つかんで動かさずに放す) と、 「次のターンも手札に残す」の印を付け外しする。
        //   挿すと印は効かない (挿した端子は捨て札へ)。 止まっている端子は印が無くても残るので付けられない。 常設は手札に無いので付けられない。

        /// <summary>手札の規則が Pick で、 場の s に残す印を付けられるか (手札の端子・止まっていない・判断の間)。</summary>
        public bool CanKeep(int s)
        {
            var dk = CombatManager.Instance?.TerminalDeck;
            if (!CanOperate || TerminalCards.CarryMode != HandCarryMode.Pick || dk == null || s < dk.permanent.Count || s >= board.Count || IsStopped(s)) return false;
            var d = TerminalCards.Get(board[s]);
            return d != null && !d.permanent;
        }

        /// <summary>場の s に残す印が付いていて、 いま挿していない (印が効く) か。</summary>
        public bool IsKept(int s)
        {
            if (s < 0 || s >= keepMark.Length || !keepMark[s]) return false;
            for (int i = 0; i < slot.Length; i++) if (slot[i] == s) return false;
            return true;
        }

        /// <summary>場の s の残す印を付け外しする。 できなければ false。</summary>
        public bool ToggleKeep(int s)
        {
            if (!CanKeep(s) || s >= keepMark.Length) return false;
            keepMark[s] = !keepMark[s];
            Game2DSfx.Play("ui_click", 1f, keepMark[s] ? 1.2f : 0.9f);
            Touch();
            return true;
        }

        /// <summary>今の捨て札 (読むだけ)。 null あり。</summary>
        public List<string> Discard => CombatManager.Instance?.TerminalDeck?.discard;

        /// <summary>場の s が、 見込みで発動する〈呼び戻し〉(捨て札から選ぶ) か。 捨て札が空なら false (選ぶ操作を出さない)。</summary>
        public bool IsRecaller(int s)
        {
            var o = preview; var disc = Discard;
            return o != null && !ShowingResult && s >= 0 && s < board.Count && TerminalCards.PicksDiscard(board[s]) && o.fired.Contains(s)
                && disc != null && disc.Count > 0;
        }

        /// <summary>〈呼び戻し〉s が戻す捨て札の添字 (選んだ添字が正しければそれ・でなければ最後に捨てた 1 枚・空なら −1)。 本体の RecallPick と同じ選び方。</summary>
        public int RecallOf(int s)
        {
            var disc = Discard; if (disc == null || disc.Count == 0) return -1;
            int want = s >= 0 && s < target.Length ? target[s] : -1;
            return want >= 0 && want < disc.Count ? want : disc.Count - 1;
        }

        /// <summary>〈呼び戻し〉s が戻す 1 枚を捨て札の添字 idx にする。 できなければ false。</summary>
        public bool SetRecall(int s, int idx)
        {
            var disc = Discard;
            if (!CanOperate || !IsRecaller(s) || disc == null || idx < 0 || idx >= disc.Count || s >= target.Length) return false;
            target[s] = idx;
            Game2DSfx.Play("wire", 1f, 1.25f);
            Touch();
            return true;
        }

        /// <summary>候補 s を picker の対象にできるか (画面のドラッグ用)。</summary>
        public bool CanSetTarget(int picker, int s)
            => CanOperate && IsPicker(picker) && preview != null && TerminalCards.IsCandidate(board, preview, picker, s);

        /// <summary>候補 s を picker の対象にする (落とし直せば替わる)。 できなければ false。</summary>
        public bool SetTarget(int picker, int s)
        {
            if (!CanSetTarget(picker, s)) return false;
            target[picker] = s;
            Game2DSfx.Play("wire", 1f, 1.25f);
            RecomputePreview(); Touch();
            return true;
        }

        private bool SameDice(int[] d)
        {
            if (d == null || _shownDice == null || d.Length != _shownDice.Length) return false;
            for (int i = 0; i < d.Length; i++) if (d[i] != _shownDice[i]) return false;
            return true;
        }

        /// <summary>ターンが解決した: 自分の攻撃の演出から見せる。</summary>
        private void Resolve(CombatManager cm)
        {
            _tr = cm.LastInteractiveResult ?? default;
            // 端子の山札: 解決した結果を残す (発動した端子を光らせる・見込みの数字を結果に置き換える)
            if (cm.LastTerminalOutcome != null) { lastOutcome = cm.LastTerminalOutcome; preview = lastOutcome; }
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            charge = ctx != null ? ctx.GetCharge() : charge;
            canReroll = false;
            // 役: 解決の後は、 本体が実際に適用した役 (CombatManager.ResolveRoles が積む) に置き換える。
            //   戦闘が閉じた (このターンで倒した・倒れた) 時は戦闘の情報が片付けられているので、 見込みのまま残す (役は解決の途中で適用済み)
            if (ctx != null && cm.IsCombatActive) { roles.Clear(); roles.AddRange(ctx.firedRolesThisTurn); }
            _countDots = false;
            // 手引き (issue #86): 「解決の順番」は最初の解決・「会心」「確定会心」は初めて出た時 (確定会心 = 会心確定の効果が立っていたターンの会心)
            Game2DTutorial.Request(TutorialTopics.Order);
            if (_tr.isCritical)
            {
                Game2DTutorial.Request(TutorialTopics.Crit);
                if (ctx != null && ctx.forceCritical) Game2DTutorial.Request(TutorialTopics.ForceCrit);
            }
            // 与ダメージは HP の差で見せる (固定ダメを含む)。 ターンの頭に見せた毒は差し引き、 出血・〈廃庭の霧吹き〉の毒は別の演出で見せる。
            // 形態が変わって HP が戻った時は結果の値で (出血・毒も分けない)
            int before = _enemyBefore - _poisonShown;
            int dealt = before - _tr.enemyHPAfter;
            _bleedPending = _bleedSeen; _mistPending = _poisonSeen;
            if (dealt < 0 || (cm.IsCombatActive && cm.EnemyMaxHP != _enemyMaxBefore)) { dealt = _tr.totalDamage + _tr.fixedDamage; _bleedPending = _mistPending = 0; }
            if (!cm.IsCombatActive && _tr.playerHPAfter <= 0 && _tr.totalDamage + _tr.fixedDamage == 0) { dealt = 0; _bleedPending = _mistPending = 0; }      // ターンの頭で力尽きた (刻限など): 攻撃は出ていない
            _bleedPending = Mathf.Clamp(_bleedPending, 0, dealt); dealt -= _bleedPending;
            _mistPending = Mathf.Clamp(_mistPending, 0, dealt); dealt -= _mistPending;
            fx.FlushDot(); fx.SetEnemyHp(before, _enemyMaxBefore);
            if (dealt > 0) { fx.HitMultPct = cm.LastHitMultPct; fx.PlayerAttack(fx.WeaponIndex, _tr.isCritical, dealt); phase = P.PAttack; t = 0; }
            else AfterPlayerAttack(cm);
            Touch();
        }

        /// <summary>自分の攻撃の後: 出血 → 〈廃庭の霧吹き〉の毒 (敵が攻撃に出る瞬間) を 1 つずつ見せてから、 敵の攻撃か決着へ。</summary>
        private void AfterPlayerAttack(CombatManager cm)
        {
            if (_bleedPending > 0) { fx.DotTick(true, _bleedPending); _bleedPending = 0; phase = P.Dot; t = 0; Touch(); return; }
            if (_mistPending > 0) { fx.DotTick(false, _mistPending); _mistPending = 0; phase = P.Dot; t = 0; Touch(); return; }
            fx.SetEnemyHp(_tr.enemyHPAfter, cm.EnemyMaxHP > 0 ? cm.EnemyMaxHP : _enemyMaxBefore);
            if (!cm.IsCombatActive) EndFight(cm);
            else StartEnemyAttack(cm);
        }

        private void StartEnemyAttack(CombatManager cm)
        {
            lastTaken = Mathf.Max(0, _hpBefore - _tr.playerHPAfter);
            _flameShown = false;
            // ボスの演出 (2026-11-14・#350〜#354): チャージのターン = チャージ (攻撃しない) / 大技のターン = 大技 / それ以外 = 通常攻撃 (王・シュヴァリエは技の絵)
            var kind = BossFx.KindOf(cm.CurrentEnemy?.id);
            if (kind != BossFx.Kind.None && charging) fx.BossCharge(kind, _blazeStacks, lastTaken);
            else if (kind != BossFx.Kind.None && heavyIn == 0 && !down) fx.BossHeavy(kind, _blazeStacks, lastTaken);
            else fx.EnemyAttack(lastTaken, down ? null : kind == BossFx.Kind.King ? "move-king-attack" : kind == BossFx.Kind.Chevalier ? "move-chev-thrust" : null);      // ダウンのターンは攻撃しないので技の絵は出さない
            phase = P.EAttack; t = 0; _eHit = false; Touch();
        }

        // ── ボスの演出の契機 (2026-11-14) ──
        private int _blazeStacks, _chevPhase = -1;
        private bool _flameShown;

        /// <summary>ターンの頭 (判断で止まった所): 大技の取り消し・〈灰の再生〉・ヴェスカの遺物・シュヴァリエの構え。 値は読むだけ。</summary>
        private void TurnStartFx(CombatManager cm, int regen)
        {
            var kind = BossFx.KindOf(cm.CurrentEnemy?.id);
            if (kind == BossFx.Kind.None) return;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            // 烈炎の数 (王の絵の ash)。 業火のターンは撃つ前の数 = ここで読んだ値
            _blazeStacks = ctx != null ? (int)ctx.GetAccumulated(ItemSystem.PassiveSkills.Effects.BlazeBrand.StackKey) : 0;
            // ダウン (または周期がずれた) で大技が来なくなった: 待機を止めて暗幕を戻す
            if (fx.BossIdling && (down || heavyIn != 0)) fx.BossCancel();
            // 絵を先に読む (全画面のコマが多い・敵の行動の瞬間に読むと一瞬止まる): チャージのターン = チャージと待機 / 大技のターン = 大技
            if (charging) BossFx.Preload(kind, _blazeStacks, false);
            else if (heavyIn == 0 && !down) BossFx.Preload(kind, _blazeStacks, true);
            if (kind == BossFx.Kind.King && regen > 0) fx.PlayMove("move-king-regen");
            if (kind == BossFx.Kind.Chevalier)
            {
                int ph = ItemSystem.PassiveSkills.Effects.SaintGeorgesPhases.PhaseOf(ctx);
                if (_chevPhase > 0 && ph > 0 && ph != _chevPhase) fx.PlayMove("move-chev-stance");
                _chevPhase = ph;
            }
            if ((kind == BossFx.Kind.Vesca || kind == BossFx.Kind.VescaFinal) && cm.PendingDecision?.tele.drawnRelics is string[] relics)
            {
                // 抽選された遺物: 種類 (攻撃 / 防御 / 妨害) で汎用の 3 本を出し分ける。 2 枚の時は続けて流す。
                // 種類は遺物の表 (VescaRelicPool.All の role) を名前で引く (名前を綴りで切らない)。 天与の 3 品の絵は、 遺物が 2026-08-16 に廃止されているので掛けない
                float at = 0f;
                foreach (var name in relics)
                {
                    var r = ItemSystem.PassiveSkills.Effects.VescaRelicPool.All.Find(x => x.name == name);
                    string sheet = r == null ? null : r.role == "攻撃" ? "move-relic-strike" : r.role == "防御" ? "move-relic-guard" : r.role == "妨害" ? "move-relic-hex" : null;
                    if (sheet == null) continue;
                    fx.PlayMove(sheet, at); at += Game2DBattleFx.MoveDuration(sheet);
                }
            }
        }

        /// <summary>武器の絵の家系: items.json の family が剣・斧・短剣・盾の時だけ (id を綴りで切らない)。 素手・竜閃・それ以外は null (手描きの演出のまま)。</summary>
        private static string SheetFamilyOf(string id)
        {
            if (string.IsNullOrEmpty(id) || id == ItemIds.Ryusen) return null;
            var f = ItemSystem.ItemDatabase.Instance?.GetItem(id)?.family;
            return f == "sword" || f == "axe" || f == "dagger" || f == "shield" ? f : null;
        }

        private void EndFight(CombatManager cm)
        {
            // 戦闘を閉じた後の CombatManager の HP は戦闘の外の値に戻っているので、 最後のターンの結果で見せる
            playerHp = _tr.playerHPAfter;
            bool won = playerHp > 0;
            phase = won ? P.Won : P.Lost; Game2DSfx.Play(won ? "victory" : "defeat"); Touch();
        }

        /// <summary>装備中の武器 → 演出の武器の絵。 品名ではなく items.json の family / tier で選ぶ (id から構造を読まない・#130)。
        /// 竜閃 (family を持たない) は <see cref="ItemIds.Ryusen"/> で判定する。 当たる行が無ければ先頭 (剣)。</summary>
        private static int WeaponIndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            int wi;
            if (id == ItemIds.Ryusen) wi = Game2DBattleFx.IndexOf(null, 0, true);
            else
            {
                var data = ItemSystem.ItemDatabase.Instance?.GetItem(id);
                wi = data != null ? Game2DBattleFx.IndexOf(data.family, data.tier, false) : -1;
            }
            return wi >= 0 ? wi : 0;
        }
    }
}

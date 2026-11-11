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
    /// 端子の山札 (ADR-0014): 出目ごとの挿し先 (<see cref="slot"/>) と、 〈写し取り〉〈焼き切り〉〈保持器〉の対象 (<see cref="target"/>) を持ち、
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
            if (known) { m.intent = heavyIn == 0 ? 2 : 1; m.atk = eatk; m.guard = guardIn == 0; }
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
                    if (!_eHit && t >= 0.54f) { _eHit = true; playerHp = _tr.playerHPAfter; Touch(); }
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
            for (int i = 0; i < slot.Length; i++) slot[i] = -1;
            board.Clear(); target = new int[0]; preview = null; lastOutcome = null; focusSlot = -1;
            Touch();
            // 手引き (issue #86): 「戦闘の形式」は初回戦闘の直前・「層とボス」は層の底のボスに着いた時
            Game2DTutorial.Request(TutorialTopics.CombatForm);
            var foe = GameManager.Instance?.CurrentEnemy;
            if (foe != null && BossIds.IsBoss(foe.id)) Game2DTutorial.Request(TutorialTopics.LayerBoss);
        }

        /// <summary>配線の判断に入った時の手引き: 「敵の予告」「端子に挿す」(初回戦闘)・「逃げる」(「端子に挿す」を読んだ後の、 逃げられる戦闘)。</summary>
        private void TutorialOnWire()
        {
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
            rolled = false; _eHit = false;
            _countDots = true; _poisonSeen = _bleedSeen = 0;
            bool paused = cm.BeginInteractiveTurn();
            // 端子の山札: このターンの場 (常設 + 手札) を写す。 判断なしで解決したターンも、 発動の表示はこの並びの添字で読む
            TakeBoard(cm);
            // ターンの頭の毒: ここで見せて HP の表示を先に減らす (本当の HP は攻撃と一緒に減るので、 尽きる分は残す)
            _poisonShown = Mathf.Min(_poisonSeen, Mathf.Max(0, _enemyBefore - 1));
            _poisonSeen = _bleedSeen = 0;      // ここから先は判断への答えで入った分 (Resolve で読む)
            if (_poisonShown > 0) fx.DotTick(false, _poisonShown);
            if (!paused) { Resolve(cm); return; }      // 判断なしで解決したターン (逃走・刻限など)
            if (board.Count > 0) Game2DTutorial.Request(TutorialTopics.Deck);      // 手引き「端子の山札」: 最初の手札 (issue #86)
            SyncDecision(cm, true);
            Game2DSfx.DiceRoll(4, 0.45f, 0.55f);
            phase = P.PRoll; t = 0; Touch();
        }

        /// <summary>止まっている判断の中身を表示へ写す。 turnStart = ターンの頭 (挿し方を空から始める。 振り直しでは手元の挿し方を残す)。</summary>
        private void SyncDecision(CombatManager cm, bool turnStart)
        {
            var p = cm.PendingDecision; if (p == null) return;
            var tele = p.tele;
            turn = tele.turn; eatk = tele.enemyAttackValue; stage = tele.escalationStage; edef = tele.enemyDefense; guardIn = tele.turnsToGuard; heavyIn = tele.turnsToHeavy;
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
            // ターンの頭は挿し方を空から (または おまかせ) / 振り直しの後は、 挿せなくなった出目だけ外す
            if (turnStart)
            {
                for (int i = 0; i < slot.Length; i++) slot[i] = -1;
                for (int s = 0; s < target.Length; s++) target[s] = -1;
                lastOutcome = null; focusSlot = -1;
                if (AutoWireOnTurnStart) AutoWire(false);
            }
            else ValidateSlots();
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
                        cm.AnswerReroll(false);
                        if (!cm.IsAwaitingDecision) { Resolve(cm); break; }
                        if (!SameDice(cm.PendingDecision.playerDice)) { SyncDecision(cm, false); Game2DSfx.Play("ui_tab"); Touch(); break; }
                        p = cm.PendingDecision;
                    }
                    if (p.kind == TurnPauseKind.Wiring)
                    {
                        Game2DSfx.Play("decide");
                        int n = p.playerDice != null ? p.playerDice.Length : 0;
                        cm.AnswerWiringSlots(BuildSlots(n), (int[])target.Clone());
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

        /// <summary>配線の判断の間に消耗品を使う。 使えたら HP・充電の表示を写し直す。</summary>
        public bool UseItem(string id)
        {
            var gm = GameManager.Instance; var cm = CombatManager.Instance;
            if (!CanOperate || gm?.Run == null || cm == null) return false;
            if (!Consumables.Use(gm.Run, id)) return false;
            playerHp = cm.PlayerHP; playerMax = Mathf.Max(1, cm.PlayerMaxHP);
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx != null) charge = ctx.GetCharge();
            Touch();
            return true;
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
        /// <summary>〈写し取り〉〈焼き切り〉〈保持器〉の対象 (添字 = その端子の場の位置・値 = 対象の場の位置・−1 = 選んでいない = 左端の候補)。 長さ = 場の枚数。</summary>
        public int[] target = new int[0];
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

        /// <summary>端子 s にあと何個挿せるか (挿せる数 − 今挿している出目の数)。</summary>
        public int Remaining(int s)
        {
            var d = s >= 0 && s < board.Count ? TerminalCards.Get(board[s]) : null; if (d == null) return 0;
            int n = 0; for (int i = 0; i < slot.Length; i++) if (slot[i] == s) n++;
            return Mathf.Max(0, d.capacity - n);
        }

        private bool IsDie(int i) => i >= 0 && i < slot.Length;

        /// <summary>出目 die へ端子 s を挿せるか (画面のドラッグ用)。 fromDie = つかんで動かしている元の出目 (手札からなら −1)。
        /// die に別の端子が挿さっていれば入れ替え (前の端子は手札へ戻る) とみなす。</summary>
        public bool CanPlug(int die, int s, int fromDie = -1)
        {
            if (!CanOperate || !IsDie(die) || s < 0 || s >= board.Count) return false;
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
            if (!CanOperate || !IsDie(die) || slot[die] < 0) return;
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

        /// <summary>今の出目のどれかを端子 s に挿せるか (もう挿している出目があれば true)。 画面が挿せない端子を暗くするのに使う。</summary>
        public bool AnyDieFits(int s)
        {
            for (int i = 0; i < slot.Length; i++) if (slot[i] == s || Accepts(i, s)) return true;
            return false;
        }

        private void CycleSlot(int i, int dir)
        {
            int m = board.Count; if (m == 0 || !IsDie(i)) return;
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
            var w = TerminalWiring.Auto(board, dice, env, st);
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

        // ── 〈写し取り〉〈焼き切り〉〈保持器〉の対象 ──
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
            if (TerminalCards.IsCandidate(board, o.slotOfDie, picker, want)) return want;
            var c = TerminalCards.TargetCandidates(board, o.slotOfDie, picker);
            return c.Count > 0 ? c[0] : -1;
        }

        /// <summary>s が、 発動する対象を取る端子のどれかの候補か (印を付ける)。 chosen = どれかの今の対象になっている。</summary>
        public bool IsTargetCandidate(int s, out bool chosen)
        {
            chosen = false; bool any = false; var o = preview;
            if (o == null || ShowingResult) return false;
            for (int p = 0; p < board.Count; p++)
            {
                if (!IsPicker(p) || !TerminalCards.IsCandidate(board, o.slotOfDie, p, s)) continue;
                any = true;
                if (TargetOf(p) == s) chosen = true;
            }
            return any;
        }

        /// <summary>候補 s を picker の対象にできるか (画面のドラッグ用)。</summary>
        public bool CanSetTarget(int picker, int s)
            => CanOperate && IsPicker(picker) && preview != null && TerminalCards.IsCandidate(board, preview.slotOfDie, picker, s);

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
            fx.EnemyAttack(lastTaken); phase = P.EAttack; t = 0; _eHit = false; Touch();
        }

        private void EndFight(CombatManager cm)
        {
            // 戦闘を閉じた後の CombatManager の HP は戦闘の外の値に戻っているので、 最後のターンの結果で見せる
            playerHp = _tr.playerHPAfter;
            bool won = playerHp > 0;
            phase = won ? P.Won : P.Lost; Game2DSfx.Play(won ? "victory" : "defeat"); Touch();
        }

        /// <summary>装備中の武器 → 演出の武器の絵 (名前が一致するもの。 無ければ先頭)。</summary>
        private static int WeaponIndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            for (int i = 0; i < Game2DBattleFx.Weapons.Length; i++) if (Game2DBattleFx.Weapons[i].name == id) return i;
            return 0;
        }
    }
}

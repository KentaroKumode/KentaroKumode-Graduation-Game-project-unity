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
    /// 操作: [GO! / NEXT] ボタンか Space / ダイス・端子のクリックか 1 2 3 / [REROLL] ボタンか E。
    /// BOT がランを動かしている間 (<see cref="GameHooks.BotDriving"/>) は手を出さない。
    /// </summary>
    public class Game2DBattleSim : MonoBehaviour
    {
        public Game2DBattleFx fx;

        public enum P { ERoll, Await, PRoll, Wire, PAttack, Dot, EAttack, Won, Lost }
        public static bool Active { get; private set; }

        public P phase = P.ERoll;
        public float t;
        /// <summary>表示する出目 (先頭 3 個)。 敵は 1 個しか引かない敵が多いので <see cref="enemyDiceCount"/> まで描く。</summary>
        public readonly int[] pd = { 1, 1, 1 }, ed = { 1, 1, 1 }, wire = { 0, 0, 0 };     // wire: 0 = 攻撃 / 1 = 防御 / 2 = 充電 / 3 = 特殊
        public int enemyDiceCount = 3;
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
        /// <summary>端子の種類の数 (特殊端子を装着していれば 4)。</summary>
        public int TerminalKinds => SpecialTerminals.EquippedLimit(GameManager.Instance?.Run) > 0 ? 4 : 3;

        public struct Wired { public int atk, aDice, blk, chg; }
        public Wired GetWired()
        {
            int a = 0, b = 0, c = 0;
            for (int i = 0; i < 3; i++) { if (wire[i] == 0) a += pd[i]; else if (wire[i] == 1) b += pd[i]; else if (wire[i] == 2) c += pd[i]; }
            return new Wired { atk = baseAtk + a, aDice = a, blk = b, chg = c };
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
                    for (int i = 0; i < 3; i++)
                    {
                        int fin = real != null && i < real.Length ? real[i] : 0;
                        if (k < 0.5f + i * 0.14f) { if (Random.value < 0.55f) pd[i] = Random.Range(1, 7); } else pd[i] = fin;
                    }
                    if (t >= 0.5f) { SyncDecision(cm, false); phase = P.Wire; rolled = true; t = 0; }
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
            for (int i = 0; i < 3; i++) wire[i] = 0;
            Touch();
        }

        /// <summary>ターンを始める: 敵の攻撃が予告され、 自分のダイスが出たところ (最初の判断) で止まる。</summary>
        private void BeginTurn(CombatManager cm)
        {
            _hpBefore = cm.PlayerHP; _enemyBefore = cm.EnemyHP; _enemyMaxBefore = cm.EnemyMaxHP;
            rolled = false; _eHit = false;
            _countDots = true; _poisonSeen = _bleedSeen = 0;
            bool paused = cm.BeginInteractiveTurn();
            // ターンの頭の毒: ここで見せて HP の表示を先に減らす (本当の HP は攻撃と一緒に減るので、 尽きる分は残す)
            _poisonShown = Mathf.Min(_poisonSeen, Mathf.Max(0, _enemyBefore - 1));
            _poisonSeen = _bleedSeen = 0;      // ここから先は判断への答えで入った分 (Resolve で読む)
            if (_poisonShown > 0) fx.DotTick(false, _poisonShown);
            if (!paused) { Resolve(cm); return; }      // 判断なしで解決したターン (逃走・刻限など)
            SyncDecision(cm, true);
            Game2DSfx.DiceRoll(4, 0.45f, 0.55f);
            phase = P.PRoll; t = 0; Touch();
        }

        /// <summary>止まっている判断の中身を表示へ写す。 takeWiring = 配線も前ターンのものへ戻す (ターンの頭だけ。 振り直しでは手元の配線を残す)。</summary>
        private void SyncDecision(CombatManager cm, bool takeWiring)
        {
            var p = cm.PendingDecision; if (p == null) return;
            var tele = p.tele;
            turn = tele.turn; eatk = tele.enemyAttackValue; stage = tele.escalationStage; edef = tele.enemyDefense; guardIn = tele.turnsToGuard; heavyIn = tele.turnsToHeavy;
            nextStageIn = tele.nextThresholdTurn > 0 ? tele.nextThresholdTurn - tele.turn : -1;
            var e = tele.enemyDice; enemyDiceCount = e != null ? Mathf.Min(3, e.Length) : 0;
            for (int i = 0; i < 3; i++) ed[i] = e != null && i < e.Length ? e[i] : 0;
            if (phase != P.PRoll) for (int i = 0; i < 3; i++) pd[i] = p.playerDice != null && i < p.playerDice.Length ? p.playerDice[i] : 0;
            if (takeWiring && p.currentWiring != null) for (int i = 0; i < 3 && i < p.currentWiring.Length; i++) wire[i] = (int)p.currentWiring[i];
            canReroll = p.kind == TurnPauseKind.Reroll; rerollCost = p.rerollCost;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            charge = ctx != null ? ctx.GetCharge() : 0;
            _shownDice = p.playerDice != null ? (int[])p.playerDice.Clone() : null;
        }

        private void Keys()
        {
            // Space は配線の決定だけ。 戦闘結果の確定は GameManager の Space が受ける (両方で受けると次のフェーズまで流れる)
            if (Input.GetKeyDown(KeyCode.Space) && phase == P.Wire) { GameManager.ScreenUsedSpaceFrame = Time.frameCount; Button(); }
            if (Input.GetKeyDown(KeyCode.E)) Reroll();
            if (Input.GetKeyDown(KeyCode.Alpha1)) CycleWire(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) CycleWire(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) CycleWire(2);
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
                        cm.AnswerWiring(BuildWiring(p.playerDice != null ? p.playerDice.Length : 3));
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

        public void CycleWire(int i)
        {
            if (!CanOperate || i < 0 || i > 2) return;
            wire[i] = (wire[i] + 1) % TerminalKinds; Game2DSfx.Play("wire", 1f, 1f + wire[i] * 0.12f); Touch();
        }

        private DiceTerminal[] BuildWiring(int n)
        {
            var w = new DiceTerminal[n];
            for (int i = 0; i < n; i++) w[i] = i < 3 ? (DiceTerminal)wire[i] : DiceTerminal.Attack;      // 4 個目以降は表示していないので攻撃へ
            return w;
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
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            charge = ctx != null ? ctx.GetCharge() : charge;
            canReroll = false;
            _countDots = false;
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
            if (dealt > 0) { fx.PlayerAttack(fx.WeaponIndex, _tr.isCritical, dealt); phase = P.PAttack; t = 0; }
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

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

        public enum P { ERoll, Await, PRoll, Wire, PAttack, EAttack, Won, Lost }
        public static bool Active { get; private set; }

        public P phase = P.ERoll;
        public float t;
        /// <summary>表示する出目 (先頭 3 個)。 敵は 1 個しか引かない敵が多いので <see cref="enemyDiceCount"/> まで描く。</summary>
        public readonly int[] pd = { 1, 1, 1 }, ed = { 1, 1, 1 }, wire = { 0, 0, 0 };     // wire: 0 = 攻撃 / 1 = 防御 / 2 = 充電 / 3 = 特殊
        public int enemyDiceCount = 3;
        public int turn = 1, playerHp, playerMax = 1, charge, eatk, lastTaken, stage, nextStageIn = -1;
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

        public int Stage => stage;
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
        }

        private void OnDisable()
        {
            var cm = CombatManager.Instance;
            if (cm != null) cm.OnCombatStart -= OnCombatStart;
            Active = false; GameManager.CombatInputByScreen = false;
        }

        private void OnCombatStart(string _) { _combatSerial++; }

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
                    if (t > 0.05f && (fx.IsIdle || fx.IsWon))
                    {
                        fx.SetEnemyHp(_tr.enemyHPAfter, cm.EnemyMaxHP > 0 ? cm.EnemyMaxHP : _enemyMaxBefore);
                        if (!cm.IsCombatActive) { EndFight(cm); break; }
                        StartEnemyAttack(cm);
                    }
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
            if (!cm.BeginInteractiveTurn()) { Resolve(cm); return; }      // 判断なしで解決したターン (逃走・刻限など)
            SyncDecision(cm, true);
            Game2DSfx.DiceRoll(4, 0.45f, 0.55f);
            phase = P.PRoll; t = 0; Touch();
        }

        /// <summary>止まっている判断の中身を表示へ写す。 takeWiring = 配線も前ターンのものへ戻す (ターンの頭だけ。 振り直しでは手元の配線を残す)。</summary>
        private void SyncDecision(CombatManager cm, bool takeWiring)
        {
            var p = cm.PendingDecision; if (p == null) return;
            var tele = p.tele;
            turn = tele.turn; eatk = tele.enemyAttackValue; stage = tele.escalationStage;
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
        public string ButtonLabel => phase == P.Wire ? "GO!" : phase == P.Won ? "NEXT" : phase == P.Lost ? "END" : "...";

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
            // 与ダメージは HP の差で見せる (出血・固定ダメを含む)。 形態が変わって HP が戻った時は結果の値で
            int dealt = _enemyBefore - _tr.enemyHPAfter;
            if (dealt < 0 || (cm.IsCombatActive && cm.EnemyMaxHP != _enemyMaxBefore)) dealt = _tr.totalDamage + _tr.fixedDamage;
            if (!cm.IsCombatActive && _tr.playerHPAfter <= 0 && _tr.totalDamage + _tr.fixedDamage == 0) dealt = 0;      // ターンの頭で力尽きた (刻限など): 攻撃は出ていない
            fx.SetEnemyHp(_enemyBefore, _enemyMaxBefore);
            if (dealt > 0) { fx.PlayerAttack(fx.WeaponIndex, _tr.isCritical, dealt); phase = P.PAttack; t = 0; }
            else if (cm.IsCombatActive) StartEnemyAttack(cm);
            else EndFight(cm);
            Touch();
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

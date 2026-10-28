using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// 戦闘の流れ (<b>試作・単独のシミュレーション</b>・2026-10-23)。 正本は docs/design/battle-prototype.src.html の <c>reset / beginTurn / advance / update / wired</c>。
    /// 敵ダイスのロール → [ROLL] → 自分のダイスロール → 配線 (ダイスか端子をクリックで 攻撃 → 防御 → 充電) → [GO!] で攻撃の演出 → 敵の攻撃 → 次のターン。
    /// 相互攻撃モデル (ダイスロールの比較は無い)。 <see cref="Game2DBattleFx"/> の演出と <see cref="Game2DBattleHud"/> の仮置きの GUI をつなぐ。
    /// <b>CombatManager にはつないでいない</b> (実戦闘の HP・出目・配線は動かさない)。
    /// 操作: [ROLL / GO! / NEXT] ボタンか Space / ダイス・端子のクリックか 1 2 3 / 武器の &lt; &gt; か ← →。
    /// </summary>
    public class Game2DBattleSim : MonoBehaviour
    {
        public Game2DBattleFx fx;

        public enum P { ERoll, Await, PRoll, Wire, PAttack, EAttack, Won, Lost }
        public static bool Active { get; private set; }

        public P phase = P.ERoll;
        public float t;
        public readonly int[] pd = { 1, 1, 1 }, ed = { 1, 1, 1 }, wire = { 0, 0, 0 };     // wire: 0 = 攻撃 / 1 = 防御 / 2 = 充電
        private readonly int[] _pFin = new int[3], _eFin = new int[3];
        public int turn = 1, playerHp = 75, playerMax = 75, charge, eatk, kills, lastTaken;
        public int version;
        public bool rolled;                  // 今のターンのダイスを振ったか (合計の表示に使う)

        public const int EnemyBase = 4;
        private static readonly int[] StageAt = { 5, 10, 15, 20 };
        private static readonly float[] StageMul = { 1, 1.25f, 1.5f, 1.75f, 2 };
        private bool _started, _eHit; private float _enemyWait;

        public int Stage { get { int s = 0; foreach (var a in StageAt) if (turn >= a) s++; return s; } }
        public int NextStageIn { get { int s = Stage; return s >= StageAt.Length ? -1 : StageAt[s] - turn; } }
        private static int Sum(int[] a) => a[0] + a[1] + a[2];
        public int EnemyAtk() => Mathf.RoundToInt(StageMul[Stage] * (EnemyBase + Sum(ed)));
        public int EnemyDiceSum => Sum(ed);

        public struct Wired { public int atk, aDice, blk, chg; }
        public Wired GetWired()
        {
            int a = 0, b = 0, c = 0;
            for (int i = 0; i < 3; i++) { if (wire[i] == 0) a += pd[i]; else if (wire[i] == 1) b += pd[i]; else c += pd[i]; }
            int baseAtk = Game2DBattleFx.Weapons[fx != null ? fx.WeaponIndex : 0].atk;
            return new Wired { atk = baseAtk + a, aDice = a, blk = b, chg = c };
        }

        private void Touch() { version++; }

        private void BeginTurn()
        {
            phase = P.ERoll; t = 0; rolled = false; _eHit = false;
            Game2DSfx.DiceRoll(4, 0.45f, 0.55f);
            for (int i = 0; i < 3; i++) { _eFin[i] = Random.Range(1, 7); _pFin[i] = Random.Range(1, 7); }
            Touch();
        }

        private void ResetAll(bool newEnemy)
        {
            playerHp = playerMax; charge = 0; turn = 1; kills = 0; if (newEnemy && fx != null) fx.NextEnemy(); BeginTurn();      // 最初の敵は Show の出現演出で出ている
        }

        private void Update()
        {
            bool on = Game2DBattleFx.Showing && fx != null;
            if (!on) { _started = false; Active = false; if (fx != null) fx.SimDriven = false; return; }
            if (!_started) { _started = true; Active = true; ResetAll(false); fx.SimDriven = true; }
            Active = true;
            if (fx.IsIntro) { t = 0; return; }      // 切り替わり・出現の演出が終わるまで、 敵のロールも始めない
            Keys();
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime); t += dt;
            switch (phase)
            {
                case P.ERoll:
                    if (t < 0.56f) { if (Random.value < 0.6f) for (int i = 0; i < 3; i++) ed[i] = Random.Range(1, 7); }
                    else for (int i = 0; i < 3; i++) ed[i] = _eFin[i];
                    if (t >= 0.7f) { for (int i = 0; i < 3; i++) ed[i] = _eFin[i]; eatk = EnemyAtk(); phase = P.Await; t = 0; }
                    Touch(); break;
                case P.PRoll:
                {
                    float k = t / 0.75f;
                    for (int i = 0; i < 3; i++) { if (k < 0.5f + i * 0.14f) { if (Random.value < 0.55f) pd[i] = Random.Range(1, 7); } else pd[i] = _pFin[i]; }
                    if (t >= 0.75f) { for (int i = 0; i < 3; i++) pd[i] = _pFin[i]; phase = P.Wire; rolled = true; t = 0; }
                    Touch(); break;
                }
                case P.PAttack:
                    if (t > 0.05f)
                    {
                        if (fx.IsWon) { phase = P.Won; Game2DSfx.Play("victory"); Touch(); }
                        else if (fx.IsIdle)
                        {
                            lastTaken = Mathf.Max(0, eatk - GetWired().blk); fx.EnemyAttack(lastTaken); phase = P.EAttack; t = 0; _eHit = false; Touch();
                        }
                    }
                    break;
                case P.EAttack:
                    if (!_eHit && t >= 0.54f) { _eHit = true; playerHp = Mathf.Max(0, playerHp - lastTaken); Touch(); }
                    if (t > 0.1f && fx.IsIdle)
                    {
                        if (playerHp <= 0) { phase = P.Lost; Game2DSfx.Play("defeat"); Touch(); }
                        else { turn++; BeginTurn(); }
                    }
                    break;
            }
        }

        private void Keys()
        {
            if (Input.GetKeyDown(KeyCode.Space)) Button();
            if (Input.GetKeyDown(KeyCode.Alpha1)) CycleWire(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) CycleWire(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) CycleWire(2);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) ChangeWeapon(-1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) ChangeWeapon(1);
        }

        public bool CanOperate => phase == P.Await || phase == P.Wire;
        public bool ButtonEnabled => phase == P.Await || phase == P.Wire || phase == P.Won || phase == P.Lost;
        public string ButtonLabel => phase == P.Await ? "ROLL" : phase == P.Wire ? "GO!" : phase == P.Won ? "NEXT" : phase == P.Lost ? "RETRY" : "...";

        /// <summary>決定ボタン (ROLL → GO! → NEXT)。</summary>
        public void Button()
        {
            if (fx == null) return;
            switch (phase)
            {
                case P.Await: phase = P.PRoll; t = 0; rolled = false; Game2DSfx.Play("decide"); Game2DSfx.DiceRoll(); break;
                case P.Wire:
                {
                    var w = GetWired(); bool crit = fx.RollCrit();
                    int dealt = Mathf.RoundToInt(w.atk * (crit ? 1.5f : 1f)); charge += w.chg;
                    Game2DSfx.Play("decide"); fx.PlayerAttack(fx.WeaponIndex, crit, dealt); phase = P.PAttack; t = 0; break;
                }
                case P.Won: Game2DSfx.Play("ui_confirm"); kills++; fx.NextEnemy(); turn++; BeginTurn(); break;
                case P.Lost: ResetAll(true); break;
            }
            Touch();
        }

        public void CycleWire(int i) { if (!CanOperate || i < 0 || i > 2) return; wire[i] = (wire[i] + 1) % 3; Game2DSfx.Play("wire", 1f, 1f + wire[i] * 0.12f); Touch(); }

        public void ChangeWeapon(int dir)
        {
            if (!CanOperate || fx == null) return;
            int n = Game2DBattleFx.Weapons.Length; fx.WeaponIndex = (fx.WeaponIndex + dir + n) % n; Game2DSfx.Play("ui_tab"); Touch();
        }
    }
}

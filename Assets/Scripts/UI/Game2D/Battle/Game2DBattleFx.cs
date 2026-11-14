using System;
using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// 戦闘演出 (2026-10-23)。 正本は docs/design/battle-prototype.src.html の <c>update / updatePlayerAttack / updateEnemyAttack / drawEffects / frame</c>。
    /// 480×270 の dot へ 1 dot ずつ打ち、 液晶の Quad (<see cref="plotQuad"/>) へ流す。 <b>演出 (エフェクトとアニメーション) だけ</b>を移した:
    /// 武器 4 家系 + 竜閃の攻撃モーション・斬撃痕・光の軌跡・会心・叩きつけの瓦礫・敵の飛び込み・ヒットストップ・画面のゆれ。
    /// ダイス・端子・HP バーなどの UI は移していない。 画面全体に掛かる質感 (ブルーム・格子・暗がり) は、 HTML のものではなく
    /// Unity の液晶 (Game2D/LcdScreen と Game2DLcdGhost) に任せる。
    ///
    /// <para>呼び出し: <see cref="PlayerAttack"/> / <see cref="EnemyAttack"/>。 試験用のキー (エディタ・開発ビルド): F8 = 表示の入り切り / Space = 攻撃 / ←→ = 武器 /
    /// C = 会心 (確率・常に・なし) / T = 攻撃の速さ (1・0.5・0.25・0.1) / R = 敵を戻す /
    /// K = 試験のボスを替える・J = そのボスのチャージ → 待機の間にもう一度で大技 / H = 武器の絵の入り切り (2026-11-14)。</para>
    /// <para>2026-11-14〜: ボスの大技 (<see cref="BossFx"/>)・ボスの通常の技・剣/斧/短剣/盾の攻撃は、 作者が合格を出した pixelfx の絵を流す
    /// (Resources/Game2DFx・Tools/pixelfx/dicebound/export_fx.py)。 武器の絵は最初にダメージが入る着弾から流し、 その攻撃の手描きの痕・輪・瓦礫・火花・閃光は出さない
    /// (武器の動き・数字・揺れ・止め・音は残す)。 素手・竜閃・4 家系以外は手描きのまま。</para>
    /// </summary>
    public class Game2DBattleFx : MonoBehaviour
    {
        public MeshRenderer plotQuad;
        public Texture2D backdrop;
        [Tooltip("武器の絵 (Weapons の並びと同じ 13 枚。 Game2DBuilder が sword_t2..shield_t4, dragon の順に入れる)")] public Texture2D[] weaponTex;

        /// <summary>演出を出している間 true (航行の地図は隠れる)。</summary>
        public static bool Showing { get; private set; }

        private const int W = BattleCanvas.W, H = BattleCanvas.H, FLOOR = BattleMotion.FloorY, EX = BattleMotion.EX, EY = BattleMotion.EY, WSC = BattleMotion.WSC;
        private static readonly Col White = new Col(255, 255, 255);

        // ── 武器 (HTML の WEAPONS) ──
        /// <summary>演出の武器の 1 行 = 絵 1 枚 (<see cref="weaponTex"/> と同じ並び)。 <b>品名を持たない</b> (2026-11-12・#130):
        /// 装備中の品から行を選ぶときは items.json の family / tier を引く (<see cref="IndexOf"/>)。 竜閃は family を持たないので
        /// <see cref="dragon"/> の行に当てる (判定は <c>ItemIds.Ryusen</c>)。 fam は絵の持ち方・モーション・音の家系で、 竜閃の行は剣の持ち方を借りる。
        /// atk は試験用のキー (Space) の見かけの与ダメにだけ使う。</summary>
        public struct Weapon { public string fam; public int tier, atk; public bool dragon; }
        public static readonly Weapon[] Weapons =
        {
            new Weapon { fam = "sword", tier = 2, atk = 3 }, new Weapon { fam = "sword", tier = 3, atk = 4 }, new Weapon { fam = "sword", tier = 4, atk = 5 },
            new Weapon { fam = "dagger", tier = 2, atk = 2 }, new Weapon { fam = "dagger", tier = 3, atk = 3 }, new Weapon { fam = "dagger", tier = 4, atk = 4 },
            new Weapon { fam = "axe", tier = 2, atk = 4 }, new Weapon { fam = "axe", tier = 3, atk = 6 }, new Weapon { fam = "axe", tier = 4, atk = 8 },
            new Weapon { fam = "shield", tier = 2, atk = 2 }, new Weapon { fam = "shield", tier = 3, atk = 3 }, new Weapon { fam = "shield", tier = 4, atk = 4 },
            new Weapon { fam = "sword", atk = 3, dragon = true },
        };
        private static bool IsDragon(int wi) => Weapons[wi].dragon;

        /// <summary>家系と段 (items.json の family / tier) → 演出の行。 dragon = true なら竜閃の行。 当たる行が無ければ -1。</summary>
        public static int IndexOf(string family, int tier, bool dragon)
        {
            for (int i = 0; i < Weapons.Length; i++)
            {
                var w = Weapons[i];
                if (dragon ? w.dragon : (!w.dragon && w.fam == family && w.tier == tier)) return i;
            }
            return -1;
        }
        private static readonly Dictionary<string, Vector2> Pivot = new Dictionary<string, Vector2> { { "sword", new Vector2(3, 29) }, { "dagger", new Vector2(3, 29) }, { "axe", new Vector2(29, 29) }, { "shield", new Vector2(16, 16) } };

        // ── 演出の部品 ──
        private sealed class Streak { public float a, t, life, w, sc, cx = float.NaN, cy = float.NaN, bow = float.NaN; public bool thin, hold, red, low; }
        private sealed class Ring { public float x, y, t, life, r0, r1; }
        private sealed class Spark { public float x, y, vx, vy, t, life; public Col c; }
        private sealed class Debris { public float x, y, vx, vy, t, life, spin; public int sz; }
        private sealed class Gather { public float x, y, tx, ty, t, life; public bool big; }
        /// <summary>cnt = 数え上げの長さ (秒・0 = 無し。 倍率の前の値から v まで)。 slam = 会心の「決め」(数え上げの終わり) を済ませたか。</summary>
        private sealed class Dmg { public float x, y, t, cnt, life = 1.2f; public int v, pct; public Col c; public bool crit, slam; }

        private enum Phase { Idle, PAttack, EAttack, Dying, Won, Intro, Dot }

        // ── 状態 (HTML の S) ──
        private Phase _phase = Phase.Idle; private float _t, _freeze, _shake, _flashE, _flashR, _flashW, _dissolve;
        private int _wi, _ehp = 80, _eMax = 80, _dealt, _dealtSoFar, _hitIdx, _taken;
        private bool _crit, _hitDone;
        /// <summary>CRITICAL! の字の時計 (秒)。 会心の着弾で −(数え上げの長さ) から始め、 0 で字が出る。 −999 = 出さない。
        /// 旧: 攻撃の始まりから出していた = 当たる前に会心が分かってしまい、 当たった瞬間の手応えが無かった (2026-11-11)。</summary>
        private float _critT = -999f;
        /// <summary>CRITICAL! の字を出す位置 (会心の数字のすぐ上・中央の x と上端の y)。</summary>
        private int _critX, _critY;
        private Plan _anim;
        private float? _hitAgo;
        private readonly List<Streak> _streaks = new List<Streak>(), _marks = new List<Streak>();
        private readonly List<Ring> _rings = new List<Ring>();
        private readonly List<Spark> _sparks = new List<Spark>();
        private readonly List<Debris> _debris = new List<Debris>();
        private readonly List<Gather> _cp = new List<Gather>();
        private readonly List<Dmg> _dmgs = new List<Dmg>();
        private readonly List<Pose> _ghosts = new List<Pose>();
        private float _cpAt = -1;
        private int _critMode;          // 0 = 確率 20% / 1 = 常に / 2 = なし
        private float _timeScale = 1f;
        private static readonly float[] Speeds = { 1f, 0.5f, 0.25f, 0.1f };
        private float _guiShake;      // 会心の衝撃で GUI を揺らす強さ (1 → 0)
        // スプライトシートのエフェクト (FxSheet・試作 2026-11-01): 会心の命中で pixelfx の crit-hit を 1 回出す
        private sealed class FxPlay { public FxSheet s; public float t; public int x, y; public bool add = true; }
        private readonly List<FxPlay> _fx = new List<FxPlay>();
        private FxSheet _critFx; private bool _critFxDone;
        private const int CritFxLift = 26;      // crit-hit の星は基準点 (足もと) から 26 dot 上 (書き出しの height)
        // 毒・出血が効いた瞬間 (2026-11-02): poison-tick / bleed-tick を敵の足もとに上から塗り、 粒がはじける拍子でダメージを数字に出して HP を減らす
        private FxSheet _poisonFx, _bleedFx;
        private int _dotDmg; private float _dotHitAt, _dotEnd; private bool _dotPending; private Col _dotCol;

        // ── ボスと武器の絵 (2026-11-14・#350〜#355) ──
        //   大技 (チャージ → 待機 → 大技) は BossFx が重ね方を持つ。 通常の技・武器の攻撃は 1 回きりの絵を _fx に足して、 画面の左上に合わせて上から塗る
        //   (書き出したコマは画面と同じ 480×270 ── 見本の paste(f, (0, 0), f) と同じ)。
        private readonly BossFx _boss = new BossFx();
        /// <summary>この攻撃で流す武器の絵 (null = 今の手描きの演出のまま: 素手・竜閃・4 家系以外)。 当たった時は手描きの斬撃痕・輪・瓦礫・火花・会心の絵・白い閃光を出さない。</summary>
        private FxSheet _weaponSheet; private bool _weaponSheetDone;
        /// <summary>この敵の攻撃で流す技の絵 (王・シュヴァリエの通常攻撃)。 当たった時は手描きの赤い斬撃と火花を出さない。</summary>
        private FxSheet _enemyMoveSheet;
        /// <summary>戦闘の流れを止める絵 (審判の炎) が終わる時刻 (<see cref="_clockU"/>)。</summary>
        private float _blockUntil, _clockU;
        private int _bossTaken; private string _enemyMoveName;

        /// <summary>[実機の確認用] 流れで流した技・武器・大技の絵の履歴 (ターンの番号つき・新しい物が後ろ・最大 40 件)。 開発ビルドとエディタだけ記録する。</summary>
        public static readonly List<string> QaMoveLog = new List<string>();
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void QaNote(string name)
        {
            QaMoveLog.Add($"T{CombatSystem.CombatManager.Instance?.CurrentCombatTurn ?? 0} {name}");
            if (QaMoveLog.Count > 40) QaMoveLog.RemoveAt(0);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>[実機の確認用] 大技の待機・暗幕と、 流れている 1 回きりの絵を全部消す (QaBattle.Restart が敵を替える時)。</summary>
        public void QaClear() { Init(); ResetEnemy(); }
#endif
        private BossFx.Kind _demoKind = BossFx.Kind.King; private bool _sheetsInTest = true;      // 試験用のキー (K・J・H)

        /// <summary>装備している武器の家系 (items.json の family)。 剣・斧・短剣・盾の時だけ絵を流す (それ以外・素手・竜閃は null)。 戦闘の流れ (Game2DBattleSim) が入れる。</summary>
        public string WeaponSheetFamily;
        /// <summary>敵の攻撃が当たった (ダメージの数字が出た) か。 戦闘の流れが HP の表示を減らす拍子に使う (大技は当たるまでが長い)。</summary>
        public bool EnemyHitLanded { get; private set; }
        /// <summary>チャージの後の待機を出しているか。</summary>
        public bool BossIdling => _boss.Idling;

        private BattleCanvas _c;
        private BSprite _bg; private BSprite[] _wspr;
        private uint[] _base;
        private Texture2D _tex; private Color32[] _out;
        private float[] _trb;
        private readonly Dictionary<int, List<Vector2Int>> _pix = new Dictionary<int, List<Vector2Int>>();

        // ── 公開 API ──

        /// <summary>敵の周りに出す印 (<see cref="Game2DBattleSim"/> が毎フレーム書く)。 intent: 0 = 無し / 1 = 攻撃 / 2 = 大技。</summary>
        public struct Marks { public int intent, atk, poison, bleed; public bool guard; }
        public Marks marks;
        /// <summary>次の自分の攻撃の倍率 (%)。 最初の着弾の数字の横に出す (0 なら出さない・2026-11-11)。</summary>
        public int HitMultPct;

        /// <summary>戦闘の流れ (Game2DBattleSim) が操作を握っている間 true。 試験用のキー (Space・←→) は効かなくなる。</summary>
        public bool SimDriven;
        public int WeaponIndex { get => _wi; set => _wi = Mathf.Clamp(value, 0, Weapons.Length - 1); }
        /// <summary>次へ進んでよいか。 ボスのチャージ・大技 (待機へ入るのを待つ間を含む)・審判の炎の間は false。</summary>
        public bool IsIdle => _phase == Phase.Idle && !_boss.Busy && _clockU >= _blockUntil;
        /// <summary>戦闘への切り替わり・敵の出現の演出中。</summary>
        public bool IsIntro => _phase == Phase.Intro;
        public bool IsWon => _phase == Phase.Won;
        public int EnemyHp => _ehp;
        public int EnemyMax => _eMax;
        /// <summary>敵の HP を本物の値に合わせる (実戦闘につないだ時)。 攻撃の演出はここから dealt ぶん減らす。</summary>
        public void SetEnemyHp(int cur, int max) { _eMax = Mathf.Max(1, max); _ehp = Mathf.Clamp(cur, 0, _eMax); }

        /// <summary>自分の攻撃の演出を始める。 dealt = 与ダメージ (数字に出す)。 竜閃は会心の時だけ専用の動き。</summary>
        public void PlayerAttack(int weaponIndex, bool crit, int dealt)
        {
            Init(); FlushDot(); _wi = Mathf.Clamp(weaponIndex, 0, Weapons.Length - 1); _crit = crit; _dealt = dealt; _dealtSoFar = 0; _hitIdx = 0; _hitAgo = null; _critFxDone = false; _critT = -999f;
            var w = Weapons[_wi];
            _weaponSheet = WeaponSheetFamily != null && !IsDragon(_wi) ? FxSheet.Load("weapon-" + WeaponSheetFamily + (crit ? "-crit" : "-normal")) : null; _weaponSheetDone = false;
            _anim = BattleMotion.MakePlan(IsDragon(_wi) && crit ? "dragon" : w.fam, crit, w.fam == "sword" && UnityEngine.Random.value < 0.5f);
            _phase = Phase.PAttack; _t = 0; _hitDone = false; _ghosts.Clear(); _cp.Clear(); Showing = true;
            if (w.fam == "shield") Game2DSfx.PlayDelayed(crit ? "charge_shield_crit" : "charge_shield", 0.18f);      // 盾 = チャージ (チャージの演出の始まりから、 命中の瞬間まで音が上がる) → 命中で解放音 (hit_shield / crit_shield)
            else Game2DSfx.Play("slash_swing", 1f, w.fam == "axe" ? 0.75f : crit ? 0.9f : 1f);
        }

        /// <summary>敵の攻撃の演出 (敵が手前へ飛び込み、 赤い斬撃と画面の縁の赤)。 taken = 受けたダメージ。</summary>
        public void EnemyAttack(int taken) => EnemyAttack(taken, null);

        /// <summary>敵の攻撃の演出。 moveSheet = 当たった瞬間に流す技の絵 (Resources/Game2DFx の名前・王とシュヴァリエの通常攻撃)。 無ければ手描きの赤い斬撃。</summary>
        public void EnemyAttack(int taken, string moveSheet)
        {
            Init(); FlushDot(); _taken = taken; _phase = Phase.EAttack; _t = 0; _hitDone = false; Showing = true; EnemyHitLanded = false;
            _enemyMoveSheet = moveSheet != null ? FxSheet.Load(moveSheet) : null; _enemyMoveName = moveSheet;
            Game2DSfx.PlayDelayed("slash_swing", EWind * 0.7f, 0.8f, 0.7f);
        }

        // ── ボスの技 (2026-11-14) ──

        /// <summary>ボスの技の絵を 1 回流す (画面全体の絵・左上に合わせて上から塗る)。 delay 秒後に始める。 block = 終わるまで戦闘の流れを止める (審判の炎)。</summary>
        public void PlayMove(string name, float delay = 0f, bool block = false)
        {
            Init(); Showing = true;
            var s = FxSheet.Load(name); if (s == null) return;
            _fx.Add(new FxPlay { s = s, t = -delay, x = s.PivotX, y = s.PivotY, add = false });
            QaNote(name + (delay > 0 ? $" (+{delay:0.00}s)" : ""));
            if (block) _blockUntil = Mathf.Max(_blockUntil, _clockU + delay + s.Duration);
        }

        /// <summary>絵の長さ (秒・無ければ 0)。 続けて流す時の間に使う。</summary>
        public static float MoveDuration(string name) { var s = FxSheet.Load(name); return s != null ? s.Duration : 0f; }

        /// <summary>大技のチャージのターン (敵はこのターン攻撃しない): チャージを流し、 終わったら待機のループ。 ash = 烈炎の数 (王だけ)。
        /// taken = このターンに受けたダメージ (審判の炎など・0 なら数字を出さない)。 絵の無いボスは、 ふつうの敵の攻撃の演出。</summary>
        public void BossCharge(BossFx.Kind kind, int ash, int taken)
        {
            Init(); FlushDot(); Showing = true;
            _boss.Charge(kind, ash); QaNote("charge:" + kind);
            if (_boss.Current == BossFx.Kind.None) { EnemyAttack(taken); return; }
            _phase = Phase.Idle; _t = 0; EnemyHitLanded = true;
            if (taken > 0) { _dmgs.Add(new Dmg { x = EX, y = 178, v = taken, c = Col.Hex("#ff6a5a") }); Game2DSfx.Play("hit_enemy"); }
        }

        /// <summary>大技のターンの敵の行動: 待機を入ってよいコマまで回してから大技を流し、 当たる拍子にダメージの数字を出す。 ash = 撃つ前の烈炎の数 (王だけ)。</summary>
        public void BossHeavy(BossFx.Kind kind, int ash, int taken)
        {
            Init(); FlushDot(); Showing = true;
            _bossTaken = taken; EnemyHitLanded = false;
            _boss.OnHit = () =>
            {
                EnemyHitLanded = true;
                _dmgs.Add(new Dmg { x = EX, y = 178, v = _bossTaken, c = Col.Hex("#ff6a5a"), life = 1.6f });
                Game2DSfx.Play(_bossTaken > 0 ? "hit_enemy" : "block");
            };
            _boss.Heavy(kind, ash); QaNote("heavy:" + kind);
            if (!_boss.Busy) { _boss.OnHit = null; EnemyAttack(taken); return; }      // 絵が無い
            _phase = Phase.Idle; _t = 0;
        }

        /// <summary>大技が取り消された (ダウン): 待機のループを止め、 暗幕を戻す。</summary>
        public void BossCancel() => _boss.Cancel();

        /// <summary>毒 (bleed = false) / 出血が効いた演出 (1 回きり)。 dmg = そのダメージ (粒がはじける拍子に数字に出し、 HP を減らす)。
        /// 終わると Idle へ戻る (HP が尽きていれば撃破の演出へ)。 絵の残りは次の演出と重なって流れきる。</summary>
        public void DotTick(bool bleed, int dmg)
        {
            Init(); FlushDot();
            FxSheet s;
            if (bleed) s = _bleedFx ?? (_bleedFx = FxSheet.Load("bleed-tick"));
            else s = _poisonFx ?? (_poisonFx = FxSheet.Load("poison-tick"));
            if (s != null) _fx.Add(new FxPlay { s = s, x = EX, y = FLOOR, add = false });
            _dotDmg = dmg; _dotPending = true; _dotCol = bleed ? Hx("#ff6a50") : Hx("#b4f064");
            _dotHitAt = bleed ? 0.1f : 0.38f;      // 出血 = 噴き出す瞬間 / 毒 = 水ぶくれがはじける瞬間 (書き出し元の拍子)
            _dotEnd = 1.0f;
            _phase = Phase.Dot; _t = 0; Showing = true;
            Game2DSfx.Play(bleed ? "hit_enemy" : "ui_tab", 1f, bleed ? 1.2f : 0.6f);
        }

        /// <summary>まだ出していない毒・出血のダメージを今すぐ HP へ入れる (演出の途中で次へ進んだ時に、 HP の表示がずれないように)。</summary>
        public void FlushDot()
        {
            if (!_dotPending) return;
            _dotPending = false;
            if (_dotDmg <= 0) return;
            _ehp = Mathf.Max(0, _ehp - _dotDmg);
            _dmgs.Add(new Dmg { x = EX + Mathf.Round(R(-12, 12)), y = EY - 40, v = _dotDmg, c = _dotCol });
        }

        private void UpdateDot()
        {
            if (_dotPending && _t >= _dotHitAt) FlushDot();
            if (_t >= _dotEnd) { _phase = _ehp <= 0 ? Phase.Dying : Phase.Idle; _t = 0; }
        }

        /// <summary>戦闘の表示を入り切りする。 入る時は「戦闘への切り替わり」と「敵の出現」の演出から始まる。</summary>
        public void Show(bool on) { if (on && !Showing) { Init(); ResetEnemy(); StartIntro(true); } Showing = on; }

        /// <summary>次の敵の出現 (切り替わりのブラインドは無し)。</summary>
        public void NextEnemy() { Init(); ResetEnemy(); StartIntro(false); }

        private bool _introBlinds, _landed;
        private const float RevealDur = 0.45f;      // 真っ黒な影のまま → 網点で徐々に姿が見える
        private float _revealT0 = 0.38f, _introEnd = 0.9f;      // 切り替え (ブラインド) が開ききってから姿が見え始める。 ブラインドなしの時 (次の敵) はすぐ
        private void StartIntro(bool blinds) { if (blinds) Game2DSfx.Play("battle_start"); Game2DSfx.PlayDelayed("enemy_appear", blinds ? 0.3f : 0f, 0.9f); _phase = Phase.Intro; _t = 0; _introBlinds = blinds; _landed = false; _revealT0 = blinds ? 0.38f : 0.08f; _introEnd = _revealT0 + RevealDur + 0.07f; if (blinds) _flashW = 1f; }

        public void ResetEnemy() { _ehp = _eMax; _phase = Phase.Idle; _t = 0; _dissolve = 0; _streaks.Clear(); _marks.Clear(); _rings.Clear(); _sparks.Clear(); _debris.Clear(); _dmgs.Clear(); _fx.Clear(); _dotPending = false; _anim = null; _hitAgo = null; _boss.Clear(); _boss.OnHit = null; _blockUntil = 0; EnemyHitLanded = false; }

        private void Init()
        {
            if (_c != null) return;
            _c = new BattleCanvas();
            _bg = backdrop != null ? new BSprite(backdrop) : null;
            _wspr = new BSprite[Weapons.Length];
            if (weaponTex != null) for (int i = 0; i < Weapons.Length && i < weaponTex.Length; i++) if (weaponTex[i] != null) { _wspr[i] = new BSprite(weaponTex[i]); if (Weapons[i].fam == "axe") _wspr[i] = _wspr[i].Mirrored(); }
            _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DBattleFx" };
            _out = new Color32[W * H]; _trb = new float[W * H];
            if (plotQuad != null)
            {
                var mpb = new MaterialPropertyBlock(); plotQuad.GetPropertyBlock(mpb); mpb.SetTexture("_MainTex", _tex); plotQuad.SetPropertyBlock(mpb);
            }
            BuildBase();
        }

        private void OnDestroy() { if (_tex != null) Destroy(_tex); Showing = false; }
        private void OnDisable() { Showing = false; Game2DHud.ShakeDots = Vector2.zero; if (plotQuad != null) plotQuad.enabled = false; }

        private void LateUpdate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugKeys();
#endif
            if (!Showing) { if (plotQuad != null) plotQuad.enabled = false; return; }
            Init();
            float rawDt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            float dt = rawDt * (_phase == Phase.PAttack ? _timeScale : 1f);
            _clockU += rawDt; _boss.Tick(rawDt);      // ボスの絵はヒットストップ・攻撃の速さ (T) に左右されない
            Tick(dt);
            Frame();
            Upload();
            if (plotQuad != null) plotQuad.enabled = true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void DebugKeys()
        {
            if (Input.GetKeyDown(KeyCode.F8)) Show(!Showing);
            if (!Showing) return;
            if (Input.GetKeyDown(KeyCode.Space) && !SimDriven)
            {
                if (_phase == Phase.Idle) { bool crit = _critMode == 1 || (_critMode == 0 && UnityEngine.Random.value < 0.2f); WeaponSheetFamily = _sheetsInTest && !IsDragon(_wi) ? Weapons[_wi].fam : null; PlayerAttack(_wi, crit, Mathf.RoundToInt(Weapons[_wi].atk + UnityEngine.Random.Range(3, 19)) * (crit ? 3 : 2) / 2); }
                else if (_phase == Phase.Won) ResetEnemy();
            }
            if (Input.GetKeyDown(KeyCode.R)) ResetEnemy();
            if (!SimDriven)
            {
                // ボスと武器の絵の試験 (2026-11-14): K = ボスを替える / J = チャージ → (待機) → もう一度で大技 / H = 武器の絵の入り切り (Space の攻撃)
                if (Input.GetKeyDown(KeyCode.K)) { _demoKind = _demoKind >= BossFx.Kind.VescaFinal ? BossFx.Kind.King : _demoKind + 1; _boss.Clear(); Debug.Log("[Game2DBattleFx] 試験のボス: " + _demoKind); }
                if (Input.GetKeyDown(KeyCode.J) && !_boss.Busy) { if (_boss.Idling) BossHeavy(_demoKind, 3, 12); else BossCharge(_demoKind, 3, 0); }
                if (Input.GetKeyDown(KeyCode.H)) { _sheetsInTest = !_sheetsInTest; Debug.Log("[Game2DBattleFx] 武器の絵: " + (_sheetsInTest ? "あり" : "なし (手描き)")); }
            }
            if (Input.GetKeyDown(KeyCode.C)) _critMode = (_critMode + 1) % 3;
            if (Input.GetKeyDown(KeyCode.T)) { int i = Array.IndexOf(Speeds, _timeScale); _timeScale = Speeds[(i + 1) % Speeds.Length]; }
            if (_phase == Phase.Idle && !SimDriven)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow)) _wi = (_wi + 1) % Weapons.Length;
                if (Input.GetKeyDown(KeyCode.LeftArrow)) _wi = (_wi + Weapons.Length - 1) % Weapons.Length;
            }
        }
#endif

        // =====================================================================
        //  更新 (HTML の update)
        // =====================================================================

        private float FadeA() => _hitAgo == null ? 1f : Mathf.Clamp01(1f - _hitAgo.Value / 0.2f);
        private Pose PoseNow(Plan A, float t) { var p = BattleMotion.PoseAt(A, t); if (p != null) p.a *= FadeA(); return p; }
        private static float R(float a, float b) => UnityEngine.Random.Range(a, b);

        private void Tick(float dt)
        {
            foreach (var d in _debris)
            {
                d.t += dt;
                if (d.t > 0) { d.x += d.vx * dt; d.y += d.vy * dt; d.vy += 760 * dt; if (d.y > FLOOR + 2) { d.y = FLOOR + 2; d.vy *= -0.28f; d.vx *= 0.6f; } }
            }
            _debris.RemoveAll(d => d.t >= d.life);
            if (_hitAgo != null) _hitAgo += dt;
            _guiShake = Mathf.Max(0, _guiShake - dt * 3.2f);
            Game2DHud.ShakeDots = _guiShake > 0.01f ? new Vector2(R(-1, 1), R(-1, 1)) * (_guiShake * _guiShake * 7f) : Vector2.zero;
            _flashW = Mathf.Max(0, _flashW - dt * 4); _shake = Mathf.Max(0, _shake - dt * 5); _flashE = Mathf.Max(0, _flashE - dt * 7); _flashR = Mathf.Max(0, _flashR - dt * 3.2f);
            if (_critT > -900f) _critT += dt;
            for (int i = 0; i < _dmgs.Count; i++)
            {
                var d = _dmgs[i]; d.t += dt;
                if (d.crit && d.cnt > 0 && !d.slam && d.t >= d.cnt)      // 会心の決め: 数え上げが終わった瞬間 (= 止めが解ける瞬間) にもう一度揺らして、 数字から火花
                { d.slam = true; _shake = Mathf.Max(_shake, 1.6f); _guiShake = Mathf.Max(_guiShake, 0.6f); Burst(d.x, d.y + 8, 14, Yel); }
            }
            _dmgs.RemoveAll(d => d.t >= d.life);
            foreach (var p in _sparks) { p.t += dt; p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 300 * dt; } _sparks.RemoveAll(p => p.t >= p.life);
            foreach (var r in _rings) r.t += dt; _rings.RemoveAll(r => r.t >= r.life);
            foreach (var s in _streaks) s.t += dt; _streaks.RemoveAll(s => s.t >= s.life);
            foreach (var f in _fx) f.t += dt;      // (終わったものは描く時に外す)
            if (_freeze > 0) { _freeze -= dt; return; }
            _t += dt;
            if (_phase == Phase.PAttack) UpdatePlayerAttack(dt);
            else if (_phase == Phase.EAttack) UpdateEnemyAttack();
            else if (_phase == Phase.Intro) UpdateIntro();
            else if (_phase == Phase.Dot) UpdateDot();
            else if (_phase == Phase.Dying) { _boss.Clear(); _dissolve = Mathf.Clamp01(_t / 1f); if (_t >= 1f) { _phase = Phase.Won; _t = 0; } }
        }

        private void Burst(float x, float y, int n, Col[] cols)
        {
            for (int i = 0; i < n; i++)
            {
                float a = R(0, 6.28f), v = 60 + R(0, 170);
                _sparks.Add(new Spark { x = x, y = y, vx = Mathf.Cos(a) * v, vy = Mathf.Sin(a) * v - 70, life = 0.3f + R(0, 0.45f), c = cols[UnityEngine.Random.Range(0, cols.Length)] });
            }
        }
        private static readonly Col[] Yel = { Col.Hex("#fff8e4"), Col.Hex("#ffeab8"), Col.Hex("#ffd8a0") }, RedC = { Col.Hex("#ffd8cc"), Col.Hex("#e88a7c"), Col.Hex("#b45a4c") };

        /// <summary>床から跳ねる光の瓦礫: 叩きつけの衝撃で床が砕け、 光る破片が上へ飛び散って落ちる (会心は数も勢いも大きい)。</summary>
        private void SpawnDebris(bool crit)
        {
            int n = crit ? 130 : 40; float spread = crit ? 150 : 74, up = crit ? 1.5f : 1f;
            for (int i = 0; i < n; i++)
            {
                float dx = R(-1, 1) * spread; bool big = UnityEngine.Random.value < (crit ? 0.4f : 0.2f);
                _debris.Add(new Debris
                {
                    x = EX + dx, y = FLOOR - 2 + R(0, 3), vx = dx * (1.6f + R(0, 1.8f)), vy = -(280 + R(0, 360)) * up * (0.5f + 0.5f * (1 - Mathf.Abs(dx) / spread)),
                    t = -(crit && i > n * 0.6f ? 0.12f : 0), life = 0.7f + R(0, 0.6f), sz = big ? (crit ? 7 : 5) : (UnityEngine.Random.value < 0.5f ? 4 : 3), spin = UnityEngine.Random.value
                });
            }
            if (crit) _rings.Add(new Ring { x = EX, y = FLOOR - 2, life = 0.5f, r0 = 6, r1 = 160 });
        }

        private void UpdatePlayerAttack(float dt)
        {
            var A = _anim; var p = BattleMotion.PoseAt(A, _t);
            if (p != null && p.seg != null && p.seg.trail && _t - _ghostAt >= 0.0125f) { var g = p.Copy(); _ghosts.Add(g); _ghostAt = _t; }
            if (p != null && p.charge && _t - _cpAt >= 0.0125f)
            {
                _cpAt = _t; var cc = BattleMotion.WeaponCenter(p, A.fam, A.flip);
                for (int i = 0; i < (p.big ? 3 : 1); i++) { float a = R(0, 6.28f), d = 50 + R(0, 50); _cp.Add(new Gather { x = cc.x + Mathf.Cos(a) * d, y = cc.y + Mathf.Sin(a) * d * 0.7f, tx = cc.x, ty = cc.y, life = 0.28f + R(0, 0.12f), big = p.big }); }
                if (UnityEngine.Random.value < (p.big ? 0.22f : 0.1f)) _rings.Add(new Ring { x = cc.x, y = cc.y, life = 0.34f, r0 = p.big ? 70 : 50, r1 = 6 });
            }
            foreach (var q in _cp) q.t += dt; _cp.RemoveAll(q => q.t >= q.life);
            while (_hitIdx < A.hits.Count && _t >= A.hits[_hitIdx].t)
            {
                var h = A.hits[_hitIdx++];
                bool isSword = A.fam == "sword";
                if (h.kind == "cut")
                {
                    if (h.fs && _hitAgo == null) _hitAgo = 0; _flashE = 0.3f;
                    var o = h.off ?? new float[] { 0, 0, 15 };
                    var m = new Streak { a = h.angFixed ? h.ang : isSword ? BattleMotion.TipAngle(A, h.t) : h.ang, t = 0, life = 9, w = 3, thin = true, hold = true, sc = isSword ? 2 : 1, cx = EX + o[0], cy = EY + o[1], bow = o[2] };
                    if (_weaponSheet == null) { _streaks.Add(m); _marks.Add(m); }      // 武器の絵を流す時は、 手描きの痕を出さない
                    Game2DSfx.Hit(A.fam, "slash", false, A.fam == "dagger" ? 1.1f : 1f); continue;      // 斬撃痕が出た瞬間に、 通常の命中音 (最後の会心の演出で hit_crit)。 薄い痕だけ (消さずに蓄積・ヒットストップ・数字・火花・閃光は無し)
                }
                if (_hitAgo == null) _hitAgo = 0;
                Game2DSfx.Hit(A.fam, h.kind, _crit, A.fam == "dagger" ? 1.1f : 1f);
                int part = _hitIdx == A.hits.Count ? _dealt - _dealtSoFar : Mathf.Max(1, Mathf.RoundToInt(_dealt * h.frac));
                bool first = _dealtSoFar == 0;      // この攻撃で最初にダメージが入る着弾 (薄い斬撃痕 cut は数えない)
                _dealtSoFar += part; _ehp = Mathf.Max(0, _ehp - part);
                bool big = h.kind == "chop" || h.kind == "slam" || h.kind == "finish" || h.kind == "dragon"; float c = _crit ? 1.5f : 1f;
                if (_crit) _guiShake = big ? 1f : 0.45f;      // 会心だけ、 GUI が衝撃で揺れる
                if (_weaponSheet != null && !_weaponSheetDone)
                {
                    // 武器の絵 (2026-11-14・#355): 最初にダメージが入る着弾から 1 回流す。 画面全体の絵なので左上に合わせて上から塗る
                    _weaponSheetDone = true; _critFxDone = true;
                    _fx.Add(new FxPlay { s = _weaponSheet, x = _weaponSheet.PivotX, y = _weaponSheet.PivotY, add = false });
                    QaNote("weapon-" + WeaponSheetFamily + (_crit ? "-crit" : "-normal"));
                }
                if (_crit && !_critFxDone)
                {
                    _critFxDone = true;      // 1 回の攻撃で 1 度だけ (連撃の 2 発目以降は出さない)
                    if (_critFx == null) _critFx = FxSheet.Load("crit-hit");
                    if (_critFx != null) _fx.Add(new FxPlay { s = _critFx, x = EX, y = EY + CritFxLift });
                }
                // 倍率は 1 回の攻撃の最初の着弾にだけ。 倍率の前の値から数え上げる (会心 = 止めの間に数え上げ、 止めが解ける瞬間に決め)
                int pct = first ? HitMultPct : 0;
                float cnt = pct > 100 && part > 1 ? (_crit ? (big ? 0.2f : 0.11f) : 0.14f) : 0f;
                _flashE = 1; _shake = (big ? 1.8f : 0.7f) * c; _freeze = (big ? 0.06f : 0.03f) * (_crit ? 1.4f : 1f);
                if (_crit && first) { _freeze = Mathf.Max(_freeze, cnt); _critT = -cnt; }
                float dmgX = EX + Mathf.Round(R(-15, 15)), dmgY = EY - 54 - (h.kind == "slash" ? Mathf.Round(R(0, 12)) : 0);
                if (_crit && first) { _critX = Mathf.RoundToInt(dmgX); _critY = Mathf.RoundToInt(dmgY) - 4 - 27; }
                _dmgs.Add(new Dmg { x = dmgX, y = dmgY, v = part, c = _crit ? Col.Hex("#ffb347") : Col.Hex("#ffe27a"),
                                    pct = pct, crit = _crit, cnt = cnt, life = _crit ? 1.5f : 1.2f });
                if (_weaponSheet != null) { _streaks.RemoveAll(x => x.hold); _marks.Clear(); continue; }      // 武器の絵が形を受け持つ: 手描きの火花・斬撃痕・輪・瓦礫・白い閃光は出さない (数字・揺れ・止め・音は残す)
                Burst(EX, EY, Mathf.RoundToInt((big ? 28 : 14) * c), Yel);
                if (h.kind == "dragon")
                {
                    _streaks.Add(new Streak { a = 0, life = 0.4f, w = 6, sc = 4, cx = EX, cy = EY }); _streaks.Add(new Streak { a = -22, t = 0.02f, life = 0.35f, w = 5, sc = 2, cx = EX, cy = EY }); _streaks.Add(new Streak { a = 24, t = 0.04f, life = 0.35f, w = 5, sc = 2, cx = EX, cy = EY });
                    _rings.Add(new Ring { x = EX, y = EY, life = 0.55f, r0 = 6, r1 = 170 }); _rings.Add(new Ring { x = EX, y = EY, life = 0.35f, r0 = 4, r1 = 100 }); _flashW = 1; _shake = 2.6f;
                }
                else if (h.kind == "finish")
                {
                    _streaks.RemoveAll(x => x.hold); foreach (var m in _marks) _streaks.Add(new Streak { a = m.a, life = 0.3f, w = 5, cx = m.cx, cy = m.cy, bow = m.bow }); _marks.Clear();
                    _rings.Add(new Ring { x = EX, y = EY, life = 0.45f, r0 = 6, r1 = 120 }); _rings.Add(new Ring { x = EX, y = EY, life = 0.3f, r0 = 4, r1 = 70 }); _flashW = 0.9f;
                }
                else if (h.kind == "slam")
                {
                    _rings.Add(new Ring { x = EX, y = EY + 14, life = 0.42f, r0 = 8, r1 = 100 }); _rings.Add(new Ring { x = EX, y = EY + 14, life = 0.3f, r0 = 8, r1 = 64 });
                    if (_crit) _rings.Add(new Ring { x = EX, y = EY + 14, life = 0.55f, r0 = 8, r1 = 150 });
                    _flashW = _crit ? 0.9f : 0.5f;
                }
                else if (h.kind == "chop")
                {
                    if (A.fam == "axe") SpawnDebris(_crit);
                    _streaks.RemoveAll(x => x.hold); _marks.Clear();
                    _streaks.Add(new Streak { a = h.angFixed ? h.ang : isSword ? BattleMotion.TipAngle(A, h.t) : 90, sc = isSword ? 2 : 1, life = 0.3f, w = _crit ? 7 : 5 });
                    _rings.Add(new Ring { x = EX, y = FLOOR - 4, life = 0.4f, r0 = 6, r1 = _crit ? 130 : 90 }); _flashW = _crit ? 0.9f : 0.45f;
                }
                else
                {
                    var o = h.off ?? new float[] { 0, 0, 15 };
                    _streaks.Add(new Streak { a = h.ang, life = 0.22f, w = 3, cx = EX + o[0], cy = EY + o[1], bow = o[2] });
                }
            }
            if (_t >= A.total) { _ghosts.Clear(); _cp.Clear(); if (_ehp <= 0) { _phase = Phase.Dying; _t = 0; } else { _phase = Phase.Idle; _t = 0; } }
        }
        private float _ghostAt = -1;

        /// <summary>敵の出現: 真っ黒な影が立っている → 網点で徐々に姿が見えてくる → 見えきった瞬間に、 ほんの少し光る。 落ちてはこない。</summary>
        private void UpdateIntro()
        {
            if (!_landed && _t >= _revealT0 + RevealDur) { _landed = true; _flashE = 0.45f; }
            if (_t >= _introEnd) { _phase = Phase.Idle; _t = 0; }
        }

        /// <summary>敵を真っ黒な影にする (k: 1 = 真っ黒 → 0 = 見える)。 豆腐の 3 つの面を、 濃さに応じた網点で黒く塗りつぶす。</summary>
        private void Silhouette(float k, int feet)
        {
            if (k <= 0.001f) return;
            int x = EX - 50, y = feet - 120, d = 10, w = 100, h = 120;
            var black = new Col(2, 2, 4);
            System.Action<int, int, int, int> fill = (rx, ry, rw, rh) =>
            {
                for (int j = ry; j < ry + rh; j++) for (int i = rx; i < rx + rw; i++)
                    if (k * 16f > BattleCanvas.Bayer[(j & 3) * 4 + (i & 3)] || k >= 0.999f) _c.Put(i, j, black, 1f);
            };
            fill(x, y + d, w, h - d); fill(x + 4, y, w + 3, d); fill(x + w, y + d, 7, h - d);
        }

        /// <summary>戦闘への切り替わり: 横縞のブラインドが、 帯ごとに時間差で網点を抜きながら開く (閉じた状態 = 真っ黒 → 開いた状態 = 戦闘の画面)。</summary>
        private void Blinds(float t)
        {
            const int band = 6; int bands = (H + band - 1) / band;
            for (int b = 0; b < bands; b++)
            {
                float delay = ((b * 7) % bands) / (float)bands * 0.14f, u = Cl((t - 0.02f - delay) / 0.14f, 0, 1), cover = 1f - u * u * (3 - 2 * u);
                if (cover <= 0.001f) continue;
                for (int y = b * band; y < Mathf.Min(H, (b + 1) * band); y++)
                    for (int x = 0; x < W; x++)
                        if (cover * 16f > BattleCanvas.Bayer[(y & 3) * 4 + (x & 3)]) _c.Put(x, y, new Col(2, 2, 4), 1f);
            }
        }

        // ── 敵の攻撃 (画面の手前へ飛び込んでくる) ──
        private const float EWind = 0.42f, ELunge = 0.12f, ERecover = 0.45f;
        private struct EPose { public int y, dx; public float glow; }
        private EPose EnemyPose()
        {
            if (_phase != Phase.EAttack) return new EPose();
            float t = _t;
            if (t < EWind) { float u = t / EWind; return new EPose { y = Mathf.RoundToInt(Mathf.Lerp(0, -6, u * u * (3 - 2 * u))), glow = u, dx = Mathf.RoundToInt(Mathf.Lerp(0, -4, u * u * (3 - 2 * u))) }; }
            if (t < EWind + ELunge) { float u = (t - EWind) / ELunge; return new EPose { y = Mathf.RoundToInt(Mathf.Lerp(-6, 30, u * u)), glow = 1 }; }
            float v = Mathf.Clamp01((t - EWind - ELunge) / ERecover); return new EPose { y = Mathf.RoundToInt(Mathf.Lerp(30, 0, 1 - (1 - v) * (1 - v))), glow = 1 - v };
        }

        private void UpdateEnemyAttack()
        {
            if (!_hitDone && _t >= EWind + ELunge)
            {
                _hitDone = true; _flashR = 1; _shake = 1.3f; _freeze = 0.06f; EnemyHitLanded = true;
                if (_taken > 0) { Game2DSfx.Play("hit_enemy"); } else Game2DSfx.Play("block");
                _dmgs.Add(new Dmg { x = EX, y = 178, v = _taken, c = Col.Hex("#ff6a5a") });
                if (_enemyMoveSheet != null)
                {
                    // ボスの通常攻撃の絵 (2026-11-14・#354): 当たった瞬間に 1 回流す。 手描きの赤い斬撃と火花は出さない
                    _fx.Add(new FxPlay { s = _enemyMoveSheet, x = _enemyMoveSheet.PivotX, y = _enemyMoveSheet.PivotY, add = false });
                    QaNote(_enemyMoveName);
                    _enemyMoveSheet = null;
                }
                else
                {
                    _streaks.Add(new Streak { a = 28, life = 0.3f, w = 3, red = true, low = true }); _streaks.Add(new Streak { a = 34, t = -0.04f, life = 0.3f, w = 3, red = true, low = true }); _streaks.Add(new Streak { a = 22, t = -0.08f, life = 0.3f, w = 3, red = true, low = true });
                    Burst(EX, 205, 14, RedC);
                }
            }
            if (_t >= EWind + ELunge + ERecover) { _phase = Phase.Idle; _t = 0; }
        }

        // =====================================================================
        //  描画 (すべて dot)
        // =====================================================================

        private static Col Hx(string s) => Col.Hex(s);
        private static float Cl(float v, float a, float b) => Mathf.Max(a, Mathf.Min(b, v));
        private static float EaseOutF(float t) => 1 - (1 - t) * (1 - t);

        /// <summary>動かない下地 (背景 + 暖色の暗がり + 白熱灯の光だまり + 四隅の暗がり) は 1 度だけ作る。</summary>
        private void BuildBase()
        {
            _base = new uint[W * H];
            if (_bg != null)
                for (int i = 0; i < W * H; i++)
                {
                    float r = _bg.d[i * 4], g = _bg.d[i * 4 + 1], b = _bg.d[i * 4 + 2];
                    _base[i] = BattleCanvas.Pack((int)(r * 0.66f + 17), (int)(g * 0.58f + 7), (int)(b * 0.5f + 2));
                }
            else for (int i = 0; i < W * H; i++) _base[i] = BattleCanvas.Pack(17, 7, 2);
            var keep = _c.FB; Array.Copy(_base, keep, W * H);
            _c.glowA = 0.16f;
            _c.Dglow(EX, 96, 250, 200, Hx("#ffd09c"), 0.55f, true); _c.Dglow(EX, 120, 120, 100, Hx("#ffc48c"), 0.5f, true);
            _c.Dglow(EX, FLOOR + 4, 190, 34, Hx("#ffc88c"), 0.8f, true); _c.glowA = -1f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = (x - W / 2f) / 340f, dy = (y - 120) / 250f, d = Mathf.Sqrt(dx * dx + dy * dy); if (d < 0.42f) continue;
                    float lv = Cl((d - 0.42f) / 0.6f, 0, 1) * 16 * 0.9f; if (lv > BattleCanvas.Bayer[(y & 3) * 4 + (x & 3)]) _c.Put(x, y, new Col(8, 3, 0), 0.4f);
                }
            Array.Copy(keep, _base, W * H);
        }

        /// <summary>豆腐 (敵・仮)。 白熱灯に上から照らされる: 上が白く灯り、 下と側面は暗く落ちる (網点で遷移)。 拡縮はしない。</summary>
        private void Tofu(float cx, float feet, int w, int h, Col tone, float flash, float glow)
        {
            int x = Mathf.RoundToInt(cx - w / 2f), y = Mathf.RoundToInt(feet - h), d = 10;
            Func<float, Col> sh = k => new Col(Cl(tone.r + k, 0, 255), Cl(tone.g + k, 0, 255), Cl(tone.b + k, 0, 255));
            int cxi = Mathf.RoundToInt(cx), fe = Mathf.RoundToInt(feet);
            for (int i = -Mathf.RoundToInt(w * 0.6f); i < Mathf.RoundToInt(w * 0.6f); i++) for (int j = -2; j <= 2; j++) if (((i + j) & 1) != 0) _c.Put(cxi + i, fe + 1 + j, new Col(0, 0, 0), 0.5f);   // 床の影 (網点)
            _c.Rect(x + w, y + d, 7, h - d, sh(-128));
            _c.Rect(x + 4, y, w + 3, d, sh(-20));
            _c.DitherRect(x, y + d, w, h - d, sh(-34), sh(-124));
            var warm = new Col(255, 226, 170); _c.Rect(x, y + d, w, 1, warm, 0.55f); _c.Rect(x, y + d, 1, h - d, warm, 0.35f);
            for (int i = 0; i < 26; i++) _c.Put(x + 6 + ((i * 37) % (w - 12)), y + d + 8 + ((i * 53) % (h - d - 16)), new Col(0, 0, 0), 0.18f);
            var amber = new Col(255, 160, 70); _c.Rect(x, y + d, w, h - d, amber, 0.1f); _c.Rect(x + 4, y, w + 3, d, amber, 0.1f);
            if (glow > 0) { var red = new Col(255, 60, 40); _c.Rect(x, y + d, w, h - d, red, 0.32f * glow); _c.Rect(x + 4, y, w + 3, d, red, 0.32f * glow); }
            if (flash > 0.05f) { var wh = new Col(255, 248, 226); float fa = 0.7f * flash; _c.Rect(x, y + d, w, h - d, wh, fa); _c.Rect(x + 4, y, w + 3, d, wh, fa); _c.Rect(x + w, y + d, 7, h - d, wh, fa); }
        }

        private void Frame()
        {
            int sx = _shake > 0 ? Mathf.RoundToInt(R(-1, 1) * _shake * 2) : 0, sy = _shake > 0 ? Mathf.RoundToInt(R(-1, 1) * _shake) : 0;
            Array.Copy(_base, _c.FB, W * H);
            var ep = EnemyPose(); int bob = Mathf.RoundToInt(Mathf.Sin(Time.unscaledTime * 1000f / 420f) * 1.5f);
            var tone = Hx("#a8c498");
            if (_phase != Phase.Won)
            {
                if (_phase == Phase.Dying)
                {
                    Tofu(EX, FLOOR + bob, 100, 120, tone, 0, 0); float d = _dissolve;
                    for (int r = 0; r < 124; r += 2)
                    {
                        float gate = r / 124f * 0.55f, a = Cl(1 - (d - gate) / 0.45f, 0, 1); int yy = FLOOR + bob - 122 + r; if (a >= 1) continue;
                        for (int j = 0; j < 2; j++) for (int x = EX - 52; x < EX + 62; x++)
                            if (BattleCanvas.Bayer[((yy + j) & 3) * 4 + (x & 3)] / 16f >= a) { int i = (yy + j) * W + x; if (i >= 0 && i < W * H) _c.FB[i] = _base[i]; }      // 帯ごとに時間差で、 網点で背景へ抜く
                    }
                }
                else if (_phase == Phase.Intro)
                {
                    Tofu(EX, FLOOR + bob, 100, 120, tone, _flashE, 0);
                    float u = Cl((_t - _revealT0) / RevealDur, 0, 1);
                    Silhouette(1f - u * u * (3f - 2f * u), FLOOR + bob);
                }
                else Tofu(EX + ep.dx, FLOOR + bob + ep.y, 100, 120, tone, _flashE, ep.glow);
            }
            _boss.PreDark(_c);      // 大技の暗幕 (見本で、 効果を貼る前の舞台に掛けている物)
            DrawEffects();
            _boss.DrawLayers(_c);   // 大技のコマ・王の赤いフィルター・ヴェスカの凍った画面と破片
            if (_flashR > 0)
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                    {
                        float dx = (x - W / 2f) / 240f, dy = (y - H / 2f) / 135f, d = Mathf.Sqrt(dx * dx + dy * dy), lv = Cl((d - 0.55f) / 0.7f, 0, 1) * _flashR * 14;
                        if (lv > BattleCanvas.Bayer[(y & 3) * 4 + (x & 3)]) _c.Put(x, y, new Col(170, 24, 12), 0.7f);      // 被弾の赤い縁 (網点)
                    }
            if (SimDriven && (_phase == Phase.Idle || _phase == Phase.Dot) && !_boss.Busy) DrawMarks(EX + ep.dx, FLOOR + bob + ep.y);
            foreach (var d in _dmgs)
            {
                if (d.t >= d.life || (d.life - d.t < 0.16f && ((int)(d.t * 30) & 1) == 0)) continue;      // 消え際は明滅
                float ts = d.t - d.cnt; bool counting = ts < 0;      // ts = 決めからの時間
                int v = d.v, step = 0;
                if (counting)      // 倍率の前の値から加速して数え上げる (6 段・最後の 1 段で大きく跳ねる)
                {
                    step = Mathf.Min(5, (int)(d.t / d.cnt * 6f)); float k = step / 6f;
                    int b = Mathf.Max(1, d.v * 100 / d.pct); v = b + Mathf.RoundToInt((d.v - b) * k * k * k);
                }
                // 位置: 数え上げ中は 1 ドット刻む。 会心の決めは下へ叩きつけて 1 度跳ね返り、 その場で脈打つ
                int dy = counting ? -(step & 1)
                       : d.crit && ts < 0.04f ? 3 : d.crit && ts < 0.08f ? -2
                       : d.crit ? 0 : -Mathf.RoundToInt(Mathf.Min(1, ts / 0.6f) * 18);      // 会心はその場に居座る (CRITICAL! の字の下)
                string num = "-" + v; int xi = Mathf.RoundToInt(d.x), y0 = Mathf.RoundToInt(d.y) - 4, yi = y0 + dy;
                int right = xi + Game2DJpFont.Width("-" + d.v), nx = right - Game2DJpFont.Width(num) * 2;      // 右端をそろえる (桁が増えても倍率の字が動かない)
                var col = d.c;
                if (d.crit && !counting)
                {
                    col = ts < 0.05f ? new Col(255, 255, 255) : ts < 0.1f ? Hx("#fff0c8") : ((int)(ts * 8) & 1) == 0 ? d.c : Hx("#ffc866");      // 白 → 橙の濃淡で脈打つ (8 コマ/秒)
                    if (d.cnt > 0 && ts < 0.09f) { int hh = ts < 0.03f ? 3 : ts < 0.06f ? 2 : 1; _c.Rect(0, yi + Game2DJpFont.Cell - hh / 2, W, hh, Hx("#fff0c8"), 0.8f - ts * 6f, true); }      // 横一閃
                    if (ts < 0.14f) for (int j = 1; j <= 3; j++) Text35(num, nx - j * Mathf.RoundToInt(5 - ts * 28), yi, 2, Hx("#ff7a1e"), 0, true, 0.45f / j * (1 - ts / 0.14f), true, false);      // 左へ流れる残像 (加算)
                    for (int g = 0; g < 4; g++) Text35(num, nx + (g == 0 ? -2 : g == 1 ? 2 : 0), yi + (g == 2 ? -2 : g == 3 ? 2 : 0), 2, Hx("#ff8a2a"), 0, true, 0.2f, true, false);      // 発光 (加算)
                }
                Text35(num, nx, yi, 2, col, 0, d.crit);      // 会心は斜体
                if (d.pct > 0 && ts < 0.75f)      // 倍率 (会心込み) を数字の右下に。 数え上げの間は明滅、 決めの後は暗くなって先に消える
                {
                    var mc = d.t < 0.04f || (counting && (step & 1) == 0) ? Hx("#fff6e0") : ts < 0.25f ? d.c : ts < 0.5f ? Hx("#c98c3a") : Hx("#7d5a2c");
                    Text35(d.pct + "%", right + 3, y0 + 12 - (d.t < 0.04f ? 2 : 0), 1, mc, 0, d.crit);
                }
            }
            if (_phase == Phase.PAttack && _anim != null && _anim.fam == "dragon") DragonLine(_t);
            if (_flashW > 0) for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (_flashW * 16 > BattleCanvas.Bayer[(y & 3) * 4 + (x & 3)]) _c.Put(x, y, new Col(255, 248, 232), 0.4f, true);
            if (_critT >= 0 && _critT < 1.1f && !(_critT > 0.95f && ((int)(_critT * 30) & 1) == 0))      // 会心の決めの瞬間に左から滑り込み、 白と橙で明滅してから落ち着く
            {
                int slide = _critT < 0.04f ? -20 : _critT < 0.08f ? 3 : 0;
                var cc = _critT < 0.32f && ((int)(_critT / 0.04f) & 1) == 0 ? new Col(255, 255, 255) : Hx("#ffb347");
                Text35("CRITICAL!", _critX + slide, _critY, 2, cc, 1, true);
            }
            if (_phase == Phase.Intro && _introBlinds) Blinds(_t);
            if (sx != 0 || sy != 0)
            {
                var tmp = (uint[])_c.FB.Clone();
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) _c.FB[y * W + x] = tmp[(int)Cl(y - sy, 0, H - 1) * W + (int)Cl(x - sx, 0, W - 1)];      // 描き終えた配列ごと 1 dot 単位でずらす
            }
            _boss.Post(_c);         // 大技の揺れ (見本の表どおり)・ヴェスカ 4 段目の帯のずれ (出来上がった画面全体に最後に掛ける)
        }

        /// <summary>竜閃: 横一線のライン。 端から端へ一瞬で走り、 着弾まで細く残り、 着弾の瞬間に太く閃いて消える。</summary>
        private void DragonLine(float t)
        {
            bool fl = _anim.flip; float lk = EaseOutF(Cl(t / 0.06f, 0, 1)); if (t >= 0.9f) return;
            float hold = t < 0.7f ? 0.8f + 0.2f * Mathf.Sin(t * 50) : Cl(1 - (t - 0.7f) / 0.2f, 0, 1), flare = t >= 0.7f ? Cl(1 - (t - 0.7f) / 0.12f, 0, 1) : 0;
            int len = Mathf.RoundToInt(W * lk), x0 = fl ? W - len : 0, Y = EY;
            _c.Rect(x0, Y - 1, len, 3, new Col(255, 236, 200), 0.3f * hold, true); _c.Rect(x0, Y, len, 1, White, hold, true);
            if (flare > 0) { _c.Rect(0, Y - 3, W, 7, new Col(255, 240, 210), 0.55f * flare, true); _c.Rect(0, Y - 1, W, 3, White, flare, true); }
            if (lk < 1) _c.Rect(fl ? x0 : x0 + len - 10, Y - 1, 10, 3, White, 0.9f, true);
        }

        private List<Vector2Int> PixList(int wi)
        {
            if (_pix.TryGetValue(wi, out var l)) return l;
            l = new List<Vector2Int>(); var sp = _wspr[wi];
            if (sp != null) for (int y = 0; y < sp.h; y++) for (int x = 0; x < sp.w; x++) if (sp.d[(y * sp.w + x) * 4 + 3] >= 128) l.Add(new Vector2Int(x, y));
            return _pix[wi] = l;
        }

        private void RasterMax(float x0, float y0, float x1, float y1, float a)
        {
            int n = Mathf.Max(Mathf.Max(Mathf.Abs((int)(x1 - x0)), Mathf.Abs((int)(y1 - y0))), 1);
            for (int i = 0; i <= n; i++)
            {
                int x = Mathf.RoundToInt(x0 + (x1 - x0) * i / n), y = Mathf.RoundToInt(y0 + (y1 - y0) * i / n);
                for (int j = 0; j < 2; j++) for (int k = 0; k < 2; k++) { int xx = x + k, yy = y + j; if (xx >= 0 && yy >= 0 && xx < W && yy < H) { int idx = yy * W + xx; if (_trb[idx] < a) _trb[idx] = a; } }
            }
        }

        private void DrawEffects()
        {
            if (_phase == Phase.PAttack && _anim != null)
            {
                var w = Weapons[_wi]; var pv = Pivot[w.fam]; var A = _anim; var pose = PoseNow(A, _t);
                float fl = A.flip ? -1 : 1;
                // 光の軌跡: 刃の光そのものを、 動いてきた向きへ引き伸ばす。 速いほど長く伸び、 根元側ほど短い
                var pn = PoseNow(A, _t); var pp = BattleMotion.PoseAt(A, Mathf.Max(0, _t - 0.012f));
                if (pn != null && pp != null && pn.vis != 0 && pn.a > 0.02f && w.fam != "dagger")
                {
                    Func<Pose, float, Vector2> pt = (q, f) => { float vx = 26 * WSC * f, vy = -26 * WSC * f, th = q.rot * Mathf.PI / 180f, c = Mathf.Cos(th), sn = Mathf.Sin(th); return new Vector2(q.x + fl * (vx * c - vy * sn), q.y + (vx * sn + vy * c) * (q.sy <= 0 ? 1 : q.sy)); };
                    float sp = (pt(pn, 1) - pt(pp, 1)).magnitude / 0.012f, TW = Cl((sp - 500) * 0.00007f, 0, 0.2f);
                    if (TW > 0.004f)
                    {
                        var all = PixList(_wi); const int N = 60; Array.Clear(_trb, 0, _trb.Length);
                        Func<Pose, Vector2Int, Vector2> pos = (q, p) =>
                        {
                            float u = p.x + 0.5f - pv.x, v = p.y + 0.5f - pv.y, su = u, sv = v;
                            if (q.bend != 0) { float al = (u - v) * 0.7071f, pe = (u + v) * 0.7071f, t = Mathf.Max(0, al) / 26f, pe2 = pe + q.bend * t * (0.45f + 0.55f * t); su = (al + pe2) * 0.7071f; sv = (pe2 - al) * 0.7071f; }
                            float vx = su * WSC, vy = sv * WSC, th = q.rot * Mathf.PI / 180f, c = Mathf.Cos(th), sn = Mathf.Sin(th);
                            return new Vector2(q.x + fl * (vx * c - vy * sn), q.y + (vx * sn + vy * c) * (q.sy <= 0 ? 1 : q.sy));
                        };
                        var qs = new List<Pose>(); for (int i = 0; i <= N; i++) { var q = BattleMotion.PoseAt(A, Mathf.Max(0, _t - TW * i / N)); if (q == null) break; qs.Add(q); }
                        float rmax = 1; for (int i = 0; i < all.Count; i += 2) rmax = Mathf.Max(rmax, Mathf.Sqrt((all[i].x + 0.5f - pv.x) * (all[i].x + 0.5f - pv.x) + (all[i].y + 0.5f - pv.y) * (all[i].y + 0.5f - pv.y)));
                        for (int pi = 0; pi < all.Count; pi += 2)
                        {
                            var p = all[pi]; float r = Mathf.Sqrt((p.x + 0.5f - pv.x) * (p.x + 0.5f - pv.x) + (p.y + 0.5f - pv.y) * (p.y + 0.5f - pv.y)) / rmax;
                            int steps = Mathf.Min(qs.Count - 1, Mathf.RoundToInt(N * Mathf.Pow(r, 4.5f)));      // 持ち手に近い dot は短く、 外周ほど長く
                            if (steps < 2) continue;
                            var prev = pos(qs[0], p);
                            for (int i = 1; i <= steps; i++) { var cur = pos(qs[i], p); RasterMax(prev.x, prev.y, cur.x, cur.y, 1 - i / (steps + 1f)); prev = cur; }      // 透明度は 剣側 = 最大 → 尾の先 = 0
                        }
                        for (int i = 0; i < W * H; i++) { float a = _trb[i]; if (a > 0.02f) _c.PutRgb(i % W, i / W, 255, 240, 210, a * pn.a, true); }
                    }
                }
                foreach (var q in _cp)
                {
                    float u = q.t / q.life, e = u * u; int x = Mathf.RoundToInt(Mathf.Lerp(q.x, q.tx, e)), y = Mathf.RoundToInt(Mathf.Lerp(q.y, q.ty, e));
                    _c.Rect(x, y, q.big ? 2 : 1, q.big ? 2 : 1, q.big ? new Col(255, 242, 214) : new Col(255, 230, 186), 0.9f * (1 - u * 0.4f), true);
                }
                if (pose != null && pose.vis != 0 && pose.a > 0.02f)
                {
                    var o = BattleCanvas.BlitOpt.Default; o.bend = pose.bend; o.sy = pose.sy; o.flip = A.flip; o.pvx = pv.x; o.pvy = pv.y; o.hasPv = true; o.sc = WSC; o.yel = pose.yel; o.a = pose.a; o.hot = pose.glow > 0.85f;
                    var cc = BattleMotion.WeaponCenter(pose, w.fam, A.flip);
                    if (pose.glow * pose.a > 0.05f) _c.Dglow(cc.x, cc.y, 70, 70, new Col(255, 214, 160), Cl(pose.glow, 0, 1) * pose.a * 0.9f, true);
                    _c.Blit(_wspr[_wi], pose.x, pose.y, pose.rot, o);
                }
            }
            foreach (var s in _streaks)
            {
                if (s.t < 0) continue;
                float u = s.t / s.life; bool hold = s.hold; float g = hold ? EaseOutF(Mathf.Min(1, s.t / 0.08f)) : EaseOutF(Mathf.Min(1, u * 2.2f)), sc = s.sc <= 0 ? 1 : s.sc, L = 120 * sc * g, a = hold ? 1 : Cl(1 - u, 0, 1);
                float r = s.a * Mathf.PI / 180f, cx = float.IsNaN(s.cx) ? EX : s.cx, cy = float.IsNaN(s.cy) ? (s.low ? 196 : EY) : s.cy, nx = -Mathf.Sin(r), ny = Mathf.Cos(r);
                float wmax = sc * (s.thin ? 4 : Mathf.Max(3, (s.w <= 0 ? 3 : s.w) * 1.7f * (1 - u * 0.45f))), ak = s.thin ? 0.5f : 1, half = L / 2;
                var col = s.red ? new Col(255, 222, 214) : new Col(255, 252, 238); var glow = s.red ? new Col(214, 120, 108) : new Col(255, 232, 176);
                for (float q = -half; q <= half; q += 1)
                {
                    float t = half > 0 ? q / half : 0, px = cx + Mathf.Cos(r) * q, py = cy + Mathf.Sin(r) * q;
                    float hw = Mathf.Max(0.6f, (wmax / 2) * Mathf.Pow(1 - Mathf.Abs(t), 0.75f));      // 直線のまま、 太さは中心が最大で両端へ細く尖る
                    for (int k = -Mathf.CeilToInt(hw + 1.5f); k <= Mathf.CeilToInt(hw + 1.5f); k++)
                    {
                        float d = Mathf.Abs(k); int x = Mathf.RoundToInt(px + nx * k), y = Mathf.RoundToInt(py + ny * k);
                        if (d <= hw) _c.PutRgb(x, y, col.r, col.g, col.b, a * ak, true);
                        else if (!s.thin && d <= hw + 1.5f) _c.PutRgb(x, y, glow.r, glow.g, glow.b, a * 0.5f, true);
                    }
                }
            }
            foreach (var r in _rings)
            {
                float u = r.t / r.life; int rad = Mathf.RoundToInt(Mathf.Lerp(r.r0, r.r1, r.r0 > r.r1 ? u : EaseOutF(u))); float a = Cl(1 - u, 0, 1);
                _c.Ellipse(r.x, r.y, rad, Mathf.RoundToInt(rad * 0.6f), new Col(255, 240, 208), a, true); if (rad > 2) _c.Ellipse(r.x, r.y, rad - 1, Mathf.RoundToInt(rad * 0.6f) - 1, new Col(255, 220, 170), a * 0.6f, true);
            }
            foreach (var d in _debris)
            {
                if (d.t < 0) continue; float u = d.t / d.life, a = Cl(1.4f - u * 1.4f, 0, 1) * (0.7f + 0.3f * Mathf.Sin(d.t * 60 + d.spin * 6));
                _c.Rect(Mathf.RoundToInt(d.x), Mathf.RoundToInt(d.y), d.sz, d.sz, u < 0.5f ? new Col(255, 240, 205) : new Col(255, 214, 150), a, true);
                if (d.sz >= 5) _c.Rect(Mathf.RoundToInt(d.x) + 1, Mathf.RoundToInt(d.y) + 1, d.sz - 2, d.sz - 2, new Col(255, 255, 245), a, true);      // 大きい破片は芯が白い
            }
            foreach (var p in _sparks) { float a = Cl(1 - p.t / p.life, 0, 1); _c.Rect(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), 2, 2, p.c, a, true); }
            _fx.RemoveAll(f => !f.s.Draw(_c, f.t, f.x, f.y, f.add));
        }

        // ── 敵の周りの印 (2026-11-11) ──
        // 絵は 12×12 の手打ち (IconWork/intent_icons12/make12.py)。 Resources/Game2DIcons/*.png.bytes を読む (取り込みの設定に左右されない)。
        private static readonly Dictionary<string, BSprite> _icons = new Dictionary<string, BSprite>();
        private static BSprite Icon(string name)
        {
            if (_icons.TryGetValue(name, out var s)) return s;
            var a = Resources.Load<TextAsset>("Game2DIcons/" + name + ".png");
            if (a != null) { var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(a.bytes); s = new BSprite(t); Destroy(t); }
            else Debug.LogWarning("[Game2DBattleFx] Resources/Game2DIcons/" + name + " が無い");
            return _icons[name] = s;
        }

        /// <summary>1 dot ずつ写す (拡縮・回転なし)。</summary>
        private void Stamp(BSprite s, int x, int y)
        {
            if (s == null) return;
            for (int j = 0; j < s.h; j++) for (int i = 0; i < s.w; i++)
                {
                    int k = (j * s.w + i) * 4; if (s.d[k + 3] < 128) continue;
                    _c.Put(x + i, y + j, new Col(s.d[k], s.d[k + 1], s.d[k + 2]));
                }
        }

        private static readonly Col MarkAtk = Col.Hex("#fff6e0"), MarkHeavy = Col.Hex("#ffb347"), MarkPoison = Col.Hex("#a6f06a"), MarkBleed = Col.Hex("#e04a50"), MarkBuff = Col.Hex("#8fc4ff");
        private const int MarkGap = 4;

        /// <summary>頭上 = 攻撃の予告 / 右下 = デバフ / 左下 = バフ。 cx・feet は敵の足もとの中心 (豆腐は幅 100・高さ 120)。</summary>
        private void DrawMarks(float cx, float feet)
        {
            var m = marks; int x0 = Mathf.RoundToInt(cx), fe = Mathf.RoundToInt(feet);
            if (m.intent > 0)
            {
                string v = m.atk.ToString(); int tw = Game2DJpFont.Width(v), w = 12 + 3 + tw, x = x0 - w / 2, y = fe - 120 - 18;
                Stamp(Icon(m.intent == 2 ? "heavy" : "attack"), x, y);
                Text35(v, x + 15, y, 1, m.intent == 2 ? MarkHeavy : MarkAtk, 0);
            }
            // 足もとの下と体の下の端は筐体の計器 (HP・物資・丸い窓) に隠れるので、 体の脇の計器より上に、 下から上へ積む (2026-11-11)
            int dy = fe - 50, dx = x0 + 60;      // デバフ: 体の右下
            if (m.poison > 0) { MarkItem("poison", m.poison, dx, dy, MarkPoison); dy -= 14; }
            if (m.bleed > 0) { MarkItem("bleed", m.bleed, dx, dy, MarkBleed); dy -= 14; }
            if (m.guard) { const string g = "構え"; int gw = Game2DJpFont.Width(g); Text35(g, x0 - 54 - gw, fe - 50, 1, MarkBuff, 0); }      // バフ: 体の左下 (構えは絵が無いので字)
        }

        private int MarkItem(string icon, int n, int x, int y, Col col)
        {
            Stamp(Icon(icon), x, y);
            string s = n.ToString(); Text35(s, x + 14, y, 1, col, 0);
            return x + 14 + Game2DJpFont.Width(s) + MarkGap;
        }

        // ── 3×5 のドット字 (ダメージの数字と CRITICAL!) ──
        private static readonly Dictionary<char, string> F35 = new Dictionary<char, string>
        {
            {'0',"111101101101111"},{'1',"010110010010111"},{'2',"110001010100111"},{'3',"110001010001110"},{'4',"101101111001001"},{'5',"111100110001110"},{'6',"011100111101111"},
            {'7',"111001010010010"},{'8',"111101111101111"},{'9',"111101111001110"},{'-',"000000111000000"},{'!',"010010010000010"},
            {'C',"011100100100011"},{'G',"011100101101011"},{'U',"101101101101111"},{'D',"110101101101110"},{'R',"110101110101101"},{'I',"111010010010111"},{'T',"111010010010010"},{'A',"010101111101101"},{'L',"100100100100111"},
        };

        /// <summary>字を scale 倍のドットで打つ (既定のフォント・GameFont・2026-11-11 に旧 3×5 の字から変更)。 align: 0 = 左 / 1 = 中央。 影つき。
        /// scale は整数倍 (1 dot を scale×scale の四角で打つ・補間しない)。</summary>
        /// <param name="italic">斜体 (会心のダメージの数字・2026-11-11)。 字の上ほど右へずらす (元の 3 行ごとに 1 ドット = 約 18°)。 ドットは拡大しないので崩れない。</param>
        /// <param name="a">濃さ。</param><param name="add">加算で重ねる (発光・残像)。</param><param name="shadow">影を打つか。</param>
        private void Text35(string s, int x, int y, int scale, Col col, int align, bool italic = false, float a = 1f, bool add = false, bool shadow = true)
        {
            scale = Mathf.Max(1, scale);
            int cell = Game2DJpFont.Cell;
            int w = Game2DJpFont.Width(s) * scale + (italic ? (cell / 3) * scale : 0); if (align == 1) x -= w / 2;
            if (a <= 0f) return;
            for (int pass = shadow ? 0 : 1; pass < 2; pass++)
            {
                int o = pass == 0 ? Mathf.Max(1, scale / 2) : 0; var cc = pass == 0 ? new Col(0, 0, 0) : col;
                Game2DJpFont.Draw((px, py) =>
                {
                    int sh = italic ? ((cell - 1 - (py - y)) / 3) * scale : 0;
                    _c.Rect(x + (px - x) * scale + o + sh, y + (py - y) * scale + o, scale, scale, cc, a, add);
                }, s, x, y);
            }
        }

        private void Upload()
        {
            var fb = _c.FB;
            for (int y = 0; y < H; y++)
            {
                int row = (H - 1 - y) * W;
                for (int x = 0; x < W; x++) { uint v = fb[y * W + x]; _out[row + x] = new Color32((byte)(v & 255), (byte)((v >> 8) & 255), (byte)((v >> 16) & 255), 255); }
            }
            _tex.SetPixels32(_out); _tex.Apply(false);
        }
    }
}

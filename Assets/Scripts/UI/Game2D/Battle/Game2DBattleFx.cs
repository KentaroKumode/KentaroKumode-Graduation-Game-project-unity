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
    /// C = 会心 (確率・常に・なし) / T = 攻撃の速さ (1・0.5・0.25・0.1) / R = 敵を戻す。</para>
    /// </summary>
    public class Game2DBattleFx : MonoBehaviour
    {
        public MeshRenderer plotQuad;
        public Texture2D backdrop;
        [Tooltip("武器の絵 (Weapons の並びと同じ 13 枚)")] public Texture2D[] weaponTex;

        /// <summary>演出を出している間 true (航行の地図は隠れる)。</summary>
        public static bool Showing { get; private set; }

        private const int W = BattleCanvas.W, H = BattleCanvas.H, FLOOR = BattleMotion.FloorY, EX = BattleMotion.EX, EY = BattleMotion.EY, WSC = BattleMotion.WSC;
        private static readonly Col White = new Col(255, 255, 255);

        // ── 武器 (HTML の WEAPONS) ──
        public struct Weapon { public string name, fam; public int atk; }
        public static readonly Weapon[] Weapons =
        {
            new Weapon { name = "鍛鉄の剣", fam = "sword", atk = 3 }, new Weapon { name = "銀の長剣", fam = "sword", atk = 4 }, new Weapon { name = "デュランダル", fam = "sword", atk = 5 },
            new Weapon { name = "盗賊の短刀", fam = "dagger", atk = 2 }, new Weapon { name = "処刑人の曲刀", fam = "dagger", atk = 3 }, new Weapon { name = "ノクタリア", fam = "dagger", atk = 4 },
            new Weapon { name = "猛斧", fam = "axe", atk = 4 }, new Weapon { name = "血塗りの戦斧", fam = "axe", atk = 6 }, new Weapon { name = "血帝廻天", fam = "axe", atk = 8 },
            new Weapon { name = "鉄盾", fam = "shield", atk = 2 }, new Weapon { name = "聖騎士の盾", fam = "shield", atk = 3 }, new Weapon { name = "ドーンブリンガー", fam = "shield", atk = 4 },
            new Weapon { name = "竜閃", fam = "sword", atk = 3 },
        };
        public static readonly string[] WeaponIds = { "sword_t2", "sword_t3", "sword_t4", "dagger_t2", "dagger_t3", "dagger_t4", "axe_t2", "axe_t3", "axe_t4", "shield_t2", "shield_t3", "shield_t4", "dragon" };
        private static bool IsDragon(int wi) => wi == Weapons.Length - 1;
        private static readonly Dictionary<string, Vector2> Pivot = new Dictionary<string, Vector2> { { "sword", new Vector2(3, 29) }, { "dagger", new Vector2(3, 29) }, { "axe", new Vector2(29, 29) }, { "shield", new Vector2(16, 16) } };

        // ── 演出の部品 ──
        private sealed class Streak { public float a, t, life, w, sc, cx = float.NaN, cy = float.NaN, bow = float.NaN; public bool thin, hold, red, low; }
        private sealed class Ring { public float x, y, t, life, r0, r1; }
        private sealed class Spark { public float x, y, vx, vy, t, life; public Col c; }
        private sealed class Debris { public float x, y, vx, vy, t, life, spin; public int sz; }
        private sealed class Gather { public float x, y, tx, ty, t, life; public bool big; }
        private sealed class Dmg { public float x, y, t; public int v; public Col c; }

        private enum Phase { Idle, PAttack, EAttack, Dying, Won, Intro }

        // ── 状態 (HTML の S) ──
        private Phase _phase = Phase.Idle; private float _t, _freeze, _shake, _flashE, _flashR, _flashW, _dissolve;
        private int _wi, _ehp = 80, _eMax = 80, _dealt, _dealtSoFar, _hitIdx, _taken;
        private bool _crit, _hitDone;
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

        private BattleCanvas _c;
        private BSprite _bg; private BSprite[] _wspr;
        private uint[] _base;
        private Texture2D _tex; private Color32[] _out;
        private float[] _trb;
        private readonly Dictionary<int, List<Vector2Int>> _pix = new Dictionary<int, List<Vector2Int>>();

        // ── 公開 API ──

        /// <summary>戦闘の流れ (Game2DBattleSim) が操作を握っている間 true。 試験用のキー (Space・←→) は効かなくなる。</summary>
        public bool SimDriven;
        public int WeaponIndex { get => _wi; set => _wi = Mathf.Clamp(value, 0, Weapons.Length - 1); }
        public bool IsIdle => _phase == Phase.Idle;
        /// <summary>戦闘への切り替わり・敵の出現の演出中。</summary>
        public bool IsIntro => _phase == Phase.Intro;
        public bool IsWon => _phase == Phase.Won;
        public int EnemyHp => _ehp;
        public int EnemyMax => _eMax;
        /// <summary>会心にするか (C キーの設定: 確率 20% / 常に / なし)。</summary>
        public bool RollCrit() => _critMode == 1 || (_critMode == 0 && UnityEngine.Random.value < 0.2f);

        /// <summary>自分の攻撃の演出を始める。 dealt = 与ダメージ (数字に出す)。 竜閃は会心の時だけ専用の動き。</summary>
        public void PlayerAttack(int weaponIndex, bool crit, int dealt)
        {
            Init(); _wi = Mathf.Clamp(weaponIndex, 0, Weapons.Length - 1); _crit = crit; _dealt = dealt; _dealtSoFar = 0; _hitIdx = 0; _hitAgo = null;
            var w = Weapons[_wi];
            _anim = BattleMotion.MakePlan(IsDragon(_wi) && crit ? "dragon" : w.fam, crit, w.fam == "sword" && UnityEngine.Random.value < 0.5f);
            _phase = Phase.PAttack; _t = 0; _hitDone = false; _ghosts.Clear(); _cp.Clear(); Showing = true;
            if (w.fam == "shield") Game2DSfx.PlayDelayed(crit ? "charge_shield_crit" : "charge_shield", 0.18f);      // 盾 = チャージ (チャージの演出の始まりから、 命中の瞬間まで音が上がる) → 命中で解放音 (hit_shield / crit_shield)
            else Game2DSfx.Play("slash_swing", 1f, w.fam == "axe" ? 0.75f : crit ? 0.9f : 1f);
        }

        /// <summary>敵の攻撃の演出 (敵が手前へ飛び込み、 赤い斬撃と画面の縁の赤)。 taken = 受けたダメージ。</summary>
        public void EnemyAttack(int taken)
        {
            Init(); _taken = taken; _phase = Phase.EAttack; _t = 0; _hitDone = false; Showing = true;
            Game2DSfx.PlayDelayed("slash_swing", EWind * 0.7f, 0.8f, 0.7f);
        }

        /// <summary>戦闘の表示を入り切りする。 入る時は「戦闘への切り替わり」と「敵の出現」の演出から始まる。</summary>
        public void Show(bool on) { if (on && !Showing) { Init(); ResetEnemy(); StartIntro(true); } Showing = on; }

        /// <summary>次の敵の出現 (切り替わりのブラインドは無し)。</summary>
        public void NextEnemy() { Init(); ResetEnemy(); StartIntro(false); }

        private bool _introBlinds, _landed;
        private const float RevealDur = 0.45f;      // 真っ黒な影のまま → 網点で徐々に姿が見える
        private float _revealT0 = 0.38f, _introEnd = 0.9f;      // 切り替え (ブラインド) が開ききってから姿が見え始める。 ブラインドなしの時 (次の敵) はすぐ
        private void StartIntro(bool blinds) { if (blinds) Game2DSfx.Play("battle_start"); Game2DSfx.PlayDelayed("enemy_appear", blinds ? 0.3f : 0f, 0.9f); _phase = Phase.Intro; _t = 0; _introBlinds = blinds; _landed = false; _revealT0 = blinds ? 0.38f : 0.08f; _introEnd = _revealT0 + RevealDur + 0.07f; if (blinds) _flashW = 1f; }

        public void ResetEnemy() { _ehp = _eMax; _phase = Phase.Idle; _t = 0; _dissolve = 0; _streaks.Clear(); _marks.Clear(); _rings.Clear(); _sparks.Clear(); _debris.Clear(); _dmgs.Clear(); _anim = null; _hitAgo = null; }

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
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime) * (_phase == Phase.PAttack ? _timeScale : 1f);
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
                if (_phase == Phase.Idle) { bool crit = _critMode == 1 || (_critMode == 0 && UnityEngine.Random.value < 0.2f); PlayerAttack(_wi, crit, Mathf.RoundToInt(Weapons[_wi].atk + UnityEngine.Random.Range(3, 19)) * (crit ? 3 : 2) / 2); }
                else if (_phase == Phase.Won) ResetEnemy();
            }
            if (Input.GetKeyDown(KeyCode.R)) ResetEnemy();
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
            foreach (var d in _dmgs) d.t += dt; _dmgs.RemoveAll(d => d.t >= 1.2f);
            foreach (var p in _sparks) { p.t += dt; p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 300 * dt; } _sparks.RemoveAll(p => p.t >= p.life);
            foreach (var r in _rings) r.t += dt; _rings.RemoveAll(r => r.t >= r.life);
            foreach (var s in _streaks) s.t += dt; _streaks.RemoveAll(s => s.t >= s.life);
            if (_freeze > 0) { _freeze -= dt; return; }
            _t += dt;
            if (_phase == Phase.PAttack) UpdatePlayerAttack(dt);
            else if (_phase == Phase.EAttack) UpdateEnemyAttack();
            else if (_phase == Phase.Intro) UpdateIntro();
            else if (_phase == Phase.Dying) { _dissolve = Mathf.Clamp01(_t / 1f); if (_t >= 1f) { _phase = Phase.Won; _t = 0; } }
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
                    _streaks.Add(m); _marks.Add(m); Game2DSfx.Hit(A.fam, "slash", false, A.fam == "dagger" ? 1.1f : 1f); continue;      // 斬撃痕が出た瞬間に、 通常の命中音 (最後の会心の演出で hit_crit)。 薄い痕だけ (消さずに蓄積・ヒットストップ・数字・火花・閃光は無し)
                }
                if (_hitAgo == null) _hitAgo = 0;
                Game2DSfx.Hit(A.fam, h.kind, _crit, A.fam == "dagger" ? 1.1f : 1f);
                int part = _hitIdx == A.hits.Count ? _dealt - _dealtSoFar : Mathf.Max(1, Mathf.RoundToInt(_dealt * h.frac));
                _dealtSoFar += part; _ehp = Mathf.Max(0, _ehp - part);
                bool big = h.kind == "chop" || h.kind == "slam" || h.kind == "finish" || h.kind == "dragon"; float c = _crit ? 1.5f : 1f;
                if (_crit) _guiShake = big ? 1f : 0.45f;      // 会心だけ、 GUI が衝撃で揺れる
                _flashE = 1; _shake = (big ? 1.8f : 0.7f) * c; _freeze = (big ? 0.06f : 0.03f) * (_crit ? 1.4f : 1f);
                _dmgs.Add(new Dmg { x = EX + Mathf.Round(R(-15, 15)), y = EY - 54 - (h.kind == "slash" ? Mathf.Round(R(0, 12)) : 0), v = part, c = _crit ? Col.Hex("#ffb347") : Col.Hex("#ffe27a") });
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
                _hitDone = true; _flashR = 1; _shake = 1.3f; _freeze = 0.06f;
                if (_taken > 0) { Game2DSfx.Play("hit_enemy"); } else Game2DSfx.Play("block");
                _dmgs.Add(new Dmg { x = EX, y = 178, v = _taken, c = Col.Hex("#ff6a5a") });
                _streaks.Add(new Streak { a = 28, life = 0.3f, w = 3, red = true, low = true }); _streaks.Add(new Streak { a = 34, t = -0.04f, life = 0.3f, w = 3, red = true, low = true }); _streaks.Add(new Streak { a = 22, t = -0.08f, life = 0.3f, w = 3, red = true, low = true });
                Burst(EX, 205, 14, RedC);
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
            DrawEffects();
            if (_flashR > 0)
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                    {
                        float dx = (x - W / 2f) / 240f, dy = (y - H / 2f) / 135f, d = Mathf.Sqrt(dx * dx + dy * dy), lv = Cl((d - 0.55f) / 0.7f, 0, 1) * _flashR * 14;
                        if (lv > BattleCanvas.Bayer[(y & 3) * 4 + (x & 3)]) _c.Put(x, y, new Col(170, 24, 12), 0.7f);      // 被弾の赤い縁 (網点)
                    }
            foreach (var d in _dmgs) { float u = d.t / 1.2f, y = d.y - Mathf.Round(Mathf.Min(1, u * 1.8f) * 20); if (u < 1f) Text35("-" + d.v, Mathf.RoundToInt(d.x), Mathf.RoundToInt(y), u < 0.12f ? 5 : 4, d.c, 1); }
            if (_phase == Phase.PAttack && _anim != null && _anim.fam == "dragon") DragonLine(_t);
            if (_flashW > 0) for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (_flashW * 16 > BattleCanvas.Bayer[(y & 3) * 4 + (x & 3)]) _c.Put(x, y, new Col(255, 248, 232), 0.4f, true);
            if (_crit && _phase == Phase.PAttack && _t < 1.1f) Text35("CRITICAL!", W / 2, 70, 4, Hx("#ffb347"), 1);
            if (_phase == Phase.Intro && _introBlinds) Blinds(_t);
            if (sx != 0 || sy != 0)
            {
                var tmp = (uint[])_c.FB.Clone();
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) _c.FB[y * W + x] = tmp[(int)Cl(y - sy, 0, H - 1) * W + (int)Cl(x - sx, 0, W - 1)];      // 描き終えた配列ごと 1 dot 単位でずらす
            }
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
        }

        // ── 3×5 のドット字 (ダメージの数字と CRITICAL!) ──
        private static readonly Dictionary<char, string> F35 = new Dictionary<char, string>
        {
            {'0',"111101101101111"},{'1',"010110010010111"},{'2',"110001010100111"},{'3',"110001010001110"},{'4',"101101111001001"},{'5',"111100110001110"},{'6',"011100111101111"},
            {'7',"111001010010010"},{'8',"111101111101111"},{'9',"111101111001110"},{'-',"000000111000000"},{'!',"010010010000010"},
            {'C',"011100100100011"},{'R',"110101110101101"},{'I',"111010010010111"},{'T',"111010010010010"},{'A',"010101111101101"},{'L',"100100100100111"},
        };

        /// <summary>3×5 の字を scale 倍で打つ (align: 0 = 左 / 1 = 中央)。 影つき。</summary>
        private void Text35(string s, int x, int y, int scale, Col col, int align)
        {
            int w = (s.Length * 4 - 1) * scale; if (align == 1) x -= w / 2;
            for (int pass = 0; pass < 2; pass++)
            {
                int ox = pass == 0 ? 1 : 0, oy = pass == 0 ? 1 : 0; var cc = pass == 0 ? new Col(0, 0, 0) : col;
                for (int k = 0; k < s.Length; k++)
                {
                    if (!F35.TryGetValue(s[k], out var g)) continue;
                    for (int i = 0; i < 15; i++) if (g[i] == '1') _c.Rect(x + k * 4 * scale + (i % 3) * scale + ox, y + (i / 3) * scale + oy, scale, scale, cc);
                }
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

using System;
using System.Collections.Generic;
using GameLoop;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// ボスの大技の演出 (2026-11-14・#350〜#353): チャージ → 待機 (ループ) → 大技。 絵は作者が合格を出した pixelfx のコードを
    /// 見本と同じ道具で焼いたコマ (<see cref="FxSheet"/>・Tools/pixelfx/dicebound/export_fx.py)。 ここは**重ね方だけ**を持つ。
    ///
    /// <para><b>正本は見本を作った Python のスクリプト</b> (Tools/pixelfx/lab/): 暗幕・赤いフィルター・揺れ・コマの拾い方・破片・帯のずれの式と数値と順番は、
    /// <c>gouka_flow_preview.py</c> (灰燼の王)・<c>judge_flow_preview.py</c> / <c>judge_phase_preview.py</c> (審判官)・<c>chevalier_flow_preview.py</c> (シュヴァリエ)・
    /// <c>vesca_flow_preview.py</c> (ヴェスカ) をそのまま C# へ移した。 変える時は、 先にスクリプトと見本を直す。</para>
    ///
    /// <para>時刻はすべて 1 秒 8 コマに刻む (見本と同じく、 コマの番号 i から t = i / 8 を出して式に入れる)。 60 fps で描いても、 見える絵は見本のコマと同じ。</para>
    ///
    /// <para>描く順 (<see cref="Game2DBattleFx"/> の Frame): 背景と敵 → <see cref="PreDark"/> (暗幕) → 手描きの演出 → <see cref="DrawLayers"/> (コマ・赤いフィルター・破片)
    /// → 数字など → <see cref="Post"/> (揺れ・帯のずれ)。 戦闘の結果・乱数には一切触らない (UnityEngine.Random も GameRng も使わない)。</para>
    /// </summary>
    public sealed class BossFx
    {
        public enum Kind { None, King, Judge, Chevalier, Vesca, VescaFinal }

        /// <summary>ボスの id → 大技の演出の種類 (BossIds で判定・綴りで切らない)。</summary>
        public static Kind KindOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return Kind.None;
            if (id == BossIds.Layer6Ash) return Kind.King;
            if (id == BossIds.Layer5Judgment) return Kind.Judge;
            if (id == BossIds.Layer5Hidden) return Kind.Chevalier;
            int vs = BossIds.VescaStageOf(id);
            return vs == 4 ? Kind.VescaFinal : vs >= 1 ? Kind.Vesca : Kind.None;
        }

        private const int W = BattleCanvas.W, H = BattleCanvas.H;
        /// <summary>見本の舞台の敵の足もと (EX, FLOOR) と、 ヴェスカの玉の中心。 全画面でない絵 (王の着弾・ヴェスカの時計) はここに基準点を合わせる。</summary>
        private const int EX = 240, FLOOR = 196, VX = 240, VY = 130;
        public const int ASH_MAX = 8;      // 烈炎の数ごとに焼いた絵の上限 (export_fx.py の ASH_MAX)

        // ── 見本の定数 ──
        private const double LEAD_K = 1.4, RUSH = 1.2, HOLD = 1.0, COOL = 1.6;                  // gouka_flow_preview.py
        private const int SLOW = 6;
        private const double LEAD_J = 1.4;                                                       // judge_flow_preview.py
        private const double DARK_C = 0.85, LEAD_C = 0.75;                                       // chevalier_flow_preview.py
        private const double CUT_C = LEAD_C - 0.06, SPLIT_C = LEAD_C - 0.06 + 0.24;
        private const double DARK_V = 0.55, LEAD_V = 1.0, GLITCH = 0.9;                          // vesca_flow_preview.py
        private const int HOLD_AT = 3, HOLD_N = 8;
        private static readonly int[,] Shake7 = { { 0, 0 }, { 0, 4 }, { -3, -2 }, { 2, 2 }, { -2, -1 }, { 1, 1 }, { 0, -1 } };   // 王・審判官
        private static readonly int[,] Shake6C = { { 4, 0 }, { -3, 2 }, { 2, -2 }, { -2, 1 }, { 1, -1 }, { 0, 1 } };            // シュヴァリエ
        private static readonly int[,] Shake6V = { { 0, 4 }, { -3, -2 }, { 3, 2 }, { -2, -1 }, { 1, 1 }, { 0, -1 } };           // ヴェスカ
        // 揺れで空いた所の色 (見本の舞台の地の色。 シュヴァリエは黒い 2 枚と同じ黒)
        private static readonly uint FillK = BattleCanvas.Pack(26, 20, 16), FillJ = BattleCanvas.Pack(26, 22, 20), FillC = BattleCanvas.Pack(2, 3, 8), FillV = BattleCanvas.Pack(5, 4, 10);

        private enum Seg { None, Charge, Idle, Heavy }
        private Kind _kind = Kind.None;
        private Seg _seg = Seg.None;
        private double _clock, _t0;       // 経過 (秒) と、 いまの区間の始まり
        private int _lastIdleI = -1;
        private bool _heavyPending, _hitDone;
        private int _ver;                 // 大技へ入った待機のコマの位相 (審判官 = ÷8・ヴェスカ = ÷6 の版)
        private FxSheet _charge, _idle, _rel, _scr, _imp, _ver2, _shatter, _glitch, _off;
        private int[] _impIdx;
        private uint[] _frozen, _tmp;
        private bool _frozenTinted;

        /// <summary>大技の当たる瞬間 (ダメージの数字を出す拍子)。 1 回の大技で 1 度。</summary>
        public Action OnHit;

        /// <summary>チャージか大技を流している間 (待機へ入るのを待っている間を含む) true。 戦闘の流れは、 これが終わるまで次へ進まない。</summary>
        public bool Busy => _seg == Seg.Charge || _seg == Seg.Heavy || _heavyPending;
        /// <summary>チャージの後の待機 (ループ) を出しているか。</summary>
        public bool Idling => _seg == Seg.Idle;
        public Kind Current => _kind;

        // ── GUI (外枠の板) へ渡す値 (2026-11-14・作者の指定: 揺れ・暗転・赤・白飛びを GUI にも掛ける。 乱れと割れは掛けない) ──
        /// <summary>このフレームの暗幕の倍率 (1 = 暗くしない・−1 = 真っ黒の板)。 <see cref="PreDark"/> が書く。</summary>
        public float GuiDark { get; private set; } = 1f;
        /// <summary>このフレームの赤いフィルターの強さ a (0 = 無し・王だけ)。</summary>
        public float GuiRed { get; private set; }
        /// <summary>このフレームの揺れ (dot)。</summary>
        public Vector2Int GuiShake { get; private set; }
        /// <summary>大技の区間 (チャージ・待機・大技) か。 false の時、 GUI へは何も掛けない。</summary>
        public bool Active => _seg != Seg.None;
        /// <summary>大技 (チャージ・待機でない) の区間か。 白飛びはこの間だけ測る。</summary>
        public bool InHeavy => _seg == Seg.Heavy;

        /// <summary>全部やめて、 暗幕も外す (戦闘の終わり・次の敵・ダウンで大技が取り消された時)。</summary>
        public void Clear()
        {
            _seg = Seg.None; _heavyPending = false; _kind = Kind.None; _frozen = null;
            _charge = _idle = _rel = _scr = _imp = _ver2 = _shatter = _glitch = _off = null; _p4 = null;      // 絵の参照を放す (戦闘が終わった後、 キャッシュから外した絵を GC が回収できるように)
        }

        /// <summary>大技が取り消された (ダウン): 待機のループを止めて暗幕を戻す (霧散の絵はまだ無い)。</summary>
        public void Cancel() { if (_seg == Seg.Idle || _heavyPending) { _seg = Seg.None; _heavyPending = false; } }

        private static string Ash(int ash) => "-a" + Mathf.Clamp(ash, 0, ASH_MAX);

        /// <summary>そのボスの大技の絵の名前 (heavy = false: チャージと待機 / true: 大技)。 先に読む一覧 (<see cref="Game2DBattleFx.QueuePreload"/>) に使う。</summary>
        public static string[] SheetNames(Kind k, int ash, bool heavy)
        {
            string[] names;
            switch (k)
            {
                case Kind.King: names = heavy ? new[] { "gouka-release", "gouka-screen" + Ash(ash), "gouka-impact" + Ash(ash) } : new[] { "gouka-charge" + Ash(ash), "gouka-charged" }; break;
                case Kind.Judge: names = heavy ? new[] { "judge-release-p0", "judge-release-p1", "judge-release-p2", "judge-release-p3", "judge-verdict" } : new[] { "judge-charge", "judge-charged" }; break;
                case Kind.Chevalier: names = heavy ? new[] { "chevalier-slash" } : new[] { "chevalier-charge", "chevalier-charged" }; break;
                case Kind.Vesca: names = heavy ? new[] { "vesca-stop-p0", "vesca-stop-p1", "vesca-stop-p2", "vesca-stop-p3", "vesca-shatter" } : new[] { "vesca-charge", "vesca-charged" }; break;
                case Kind.VescaFinal: names = heavy ? new[] { "vesca-lost-p0", "vesca-lost-p1", "vesca-lost-p2", "vesca-lost-p3", "vesca-glitch", "vesca-off" } : new[] { "vesca-charge-broken", "vesca-charged-broken" }; break;
                default: return new string[0];
            }
            return names;
        }

        /// <summary>そのボスの、 大技以外の技の絵の名前 (戦闘の入りに先に読む)。</summary>
        public static string[] MoveNames(Kind k)
        {
            switch (k)
            {
                case Kind.King: return new[] { "move-king-attack", "move-king-regen" };
                case Kind.Judge: return new[] { "move-judge-flame" };
                case Kind.Chevalier: return new[] { "move-chev-thrust", "move-chev-stance" };
                case Kind.Vesca: case Kind.VescaFinal: return new[] { "move-relic-strike", "move-relic-guard", "move-relic-hex" };
                default: return new string[0];
            }
        }

        /// <summary>チャージのターン: チャージを流し、 終わったら待機のループへ。 ash = 烈炎の数 (王だけ)。</summary>
        public void Charge(Kind k, int ash)
        {
            _kind = k; _heavyPending = false; _frozen = null;
            switch (k)
            {
                case Kind.King: _charge = FxSheet.Load("gouka-charge" + Ash(ash)); _idle = FxSheet.Load("gouka-charged"); break;
                case Kind.Judge: _charge = FxSheet.Load("judge-charge"); _idle = FxSheet.Load("judge-charged"); break;
                case Kind.Chevalier: _charge = FxSheet.Load("chevalier-charge"); _idle = FxSheet.Load("chevalier-charged"); break;
                case Kind.Vesca: _charge = FxSheet.Load("vesca-charge"); _idle = FxSheet.Load("vesca-charged"); break;
                case Kind.VescaFinal: _charge = FxSheet.Load("vesca-charge-broken"); _idle = FxSheet.Load("vesca-charged-broken"); if (_tear == null) LoadTear(); break;
                default: _seg = Seg.None; return;
            }
            if (_charge == null || _idle == null) { _seg = Seg.None; _kind = Kind.None; return; }
            _seg = Seg.Charge; _t0 = _clock;
        }

        /// <summary>大技のターン: 待機を、 入ってよいコマ (王・シュヴァリエ = 0 / 6・審判官 = 8 の倍数・ヴェスカ = 6 の倍数) まで回してから大技を流す。
        /// 待機が出ていなければ (チャージを見ていない) すぐ、 位相 0 の版で入る。 ash = 烈炎の数 (王だけ・撃つ前の数)。</summary>
        public void Heavy(Kind k, int ash)
        {
            if (k == Kind.None) return;
            if (_kind != k) { _kind = k; _seg = Seg.None; }
            switch (k)
            {
                case Kind.King:
                    _rel = FxSheet.Load("gouka-release"); _scr = FxSheet.Load("gouka-screen" + Ash(ash)); _imp = FxSheet.Load("gouka-impact" + Ash(ash));
                    _impIdx = null;
                    if (_imp != null)
                    {
                        var idx = new List<int>(); int q = 0;
                        while (q < _imp.FrameCount) { idx.Add(q); int n = idx.Count; q += n <= SLOW ? 1 : n <= SLOW + 2 ? 2 : 3; }
                        _impIdx = idx.ToArray();
                    }
                    break;
                case Kind.Judge: _ver2 = FxSheet.Load("judge-verdict"); break;
                case Kind.Chevalier: _rel = FxSheet.Load("chevalier-slash"); break;
                case Kind.Vesca: _shatter = FxSheet.Load("vesca-shatter"); if (_shards == null) LoadShards(); break;
                case Kind.VescaFinal: _glitch = FxSheet.Load("vesca-glitch"); _off = FxSheet.Load("vesca-off"); if (_tear == null) LoadTear(); break;
            }
            _heavyPending = true;
            if (_seg != Seg.Idle && _seg != Seg.Charge) StartHeavy(0, _clock);
        }

        private int EntryStep => _kind == Kind.Judge ? 8 : 6;
        private bool EntryOk(int idleFrame)
            => _kind == Kind.King || _kind == Kind.Chevalier ? idleFrame == 0 || idleFrame == 6 : idleFrame % EntryStep == 0;

        private void StartHeavy(int idleFrame, double t0)
        {
            _heavyPending = false; _hitDone = false; _frozen = null; _frozenTinted = false;
            int v = _kind == Kind.King || _kind == Kind.Chevalier ? 0 : idleFrame / EntryStep;
            _ver = v;
            if (_kind == Kind.Judge) _rel = FxSheet.Load("judge-release-p" + v);
            else if (_kind == Kind.Vesca) _rel = FxSheet.Load("vesca-stop-p" + v);
            else if (_kind == Kind.VescaFinal) _rel = FxSheet.Load("vesca-lost-p" + v);
            if (_rel == null) { _seg = Seg.None; return; }
            _seg = Seg.Heavy; _t0 = t0;
        }

        /// <summary>いまの区間のコマの番号 (1 秒 8 コマ)。</summary>
        private int FrameI => (int)Math.Floor((_clock - _t0) * 8.0 + 1e-6);

        public void Tick(float dt)
        {
            _clock += dt;
            if (_seg == Seg.Charge && FrameI >= _charge.FrameCount) { _t0 += _charge.FrameCount / 8.0; _seg = Seg.Idle; _lastIdleI = -1; }
            if (_seg == Seg.Idle)
            {
                int i = FrameI, k = i % _idle.FrameCount;
                if (_heavyPending && i != _lastIdleI && EntryOk(k)) StartHeavy(k, _t0 + i / 8.0);
                else _lastIdleI = i;
            }
            if (_seg == Seg.Heavy)
            {
                int i = FrameI;
                if (!_hitDone && i >= HitFrame) { _hitDone = true; OnHit?.Invoke(); }
                if (i >= HeavyLength) { _seg = Seg.None; if (!_hitDone) { _hitDone = true; OnHit?.Invoke(); } }
            }
        }

        // ── 大技の長さと当たる拍子 (見本の 1 秒 8 コマのコマ番号) ──
        private int OffK => (int)Math.Round(LEAD_K * 8);
        private int HeavyLength
        {
            get
            {
                switch (_kind)
                {
                    case Kind.King: return Math.Max(Math.Max((_impIdx?.Length ?? 0) + OffK, _scr?.FrameCount ?? 0), _rel.FrameCount);
                    case Kind.Judge: return Math.Max(_rel.FrameCount, (int)Math.Round(LEAD_J * 8) + (_ver2?.FrameCount ?? 0));
                    case Kind.Chevalier: return Math.Max(_rel.FrameCount, (int)(SPLIT_C * 8) + 1 + 6);      // 揺れは絵の後ろの空のコマ (捨てた) まで続く
                    case Kind.Vesca: return (int)(LEAD_V * 8) + (_shatter?.FrameCount ?? 0);
                    case Kind.VescaFinal: return (int)(LEAD_V * 8) + P4Parts.Count + 4;      // 見本: 戻った後も 4 コマ (帯の乱れが残る)
                    default: return 0;
                }
            }
        }
        /// <summary>王 = 白く飛ぶコマ (着弾の始まり)・審判官 = 光が降りて白く飛ぶコマ・シュヴァリエ = 割れ始め (揺れの始まり)・
        /// ヴェスカ 1〜3 段目 = 砕けるコマ (揺れの始まり)・4 段目 = 乱れの始まり。</summary>
        private int HitFrame
        {
            get
            {
                switch (_kind)
                {
                    case Kind.King: return OffK;
                    case Kind.Judge: return (int)Math.Round(LEAD_J * 8) + 1;
                    case Kind.Chevalier: return (int)(SPLIT_C * 8) + 1;
                    case Kind.Vesca: return (int)(LEAD_V * 8) + 4;
                    default: return (int)(LEAD_V * 8);
                }
            }
        }

        // 4 段目の全画面の効果の並び: 乱れの 0〜2 コマ目 → 3 コマ目を HOLD_N + 1 コマ止める → 4〜6 コマ目 → 消灯 (vesca_flow_preview.py の parts)
        private List<KeyValuePair<FxSheet, int>> _p4;
        private List<KeyValuePair<FxSheet, int>> P4Parts
        {
            get
            {
                if (_p4 != null && _p4.Count > 0 && _p4[_p4.Count - 1].Key == _off) return _p4;
                _p4 = new List<KeyValuePair<FxSheet, int>>();
                if (_glitch == null || _off == null) return _p4;
                for (int k = 0; k < HOLD_AT; k++) _p4.Add(new KeyValuePair<FxSheet, int>(_glitch, k));
                for (int k = 0; k < HOLD_N + 1; k++) _p4.Add(new KeyValuePair<FxSheet, int>(_glitch, HOLD_AT));
                for (int k = HOLD_AT + 1; k < (int)(GLITCH * 8); k++) _p4.Add(new KeyValuePair<FxSheet, int>(_glitch, k));
                for (int k = 0; k < _off.FrameCount; k++) _p4.Add(new KeyValuePair<FxSheet, int>(_off, k));
                return _p4;
            }
        }

        // =====================================================================
        //  描く
        // =====================================================================

        private static double Sm(double a, double b, double x) { x = Math.Max(0.0, Math.Min(1.0, (x - a) / (b - a))); return x * x * (3 - 2 * x); }

        /// <summary>見本の dark(im, a): 各色を v × m へ (切り捨て)。 王・審判官は m = 1 − 0.5a、 シュヴァリエ・ヴェスカは m = 1 − a。</summary>
        private static void Dark(BattleCanvas c, double m)
        {
            if (m >= 1.0) return;
            var lut = new byte[256];
            for (int v = 0; v < 256; v++) lut[v] = (byte)(int)(v * m);
            var fb = c.FB;
            for (int i = 0; i < fb.Length; i++) { uint p = fb[i]; fb[i] = BattleCanvas.Pack(lut[p & 255], lut[(p >> 8) & 255], lut[(p >> 16) & 255]); }
        }

        /// <summary>見本の red(im, a) (王だけ): 赤 +26a・緑 ×(1−0.45a)・青 ×(1−0.6a)。</summary>
        private static void Red(BattleCanvas c, double a)
        {
            if (a <= 0.01) return;
            byte[] lr = new byte[256], lg = new byte[256], lb = new byte[256];
            for (int v = 0; v < 256; v++) { lr[v] = (byte)Math.Min(255, (int)(v + 26 * a)); lg[v] = (byte)(int)(v * (1 - 0.45 * a)); lb[v] = (byte)(int)(v * (1 - 0.6 * a)); }
            var fb = c.FB;
            for (int i = 0; i < fb.Length; i++) { uint p = fb[i]; fb[i] = BattleCanvas.Pack(lr[p & 255], lg[(p >> 8) & 255], lb[(p >> 16) & 255]); }
        }

        /// <summary>暗幕の倍率 (見本の dark の a を m = 1 − 0.5a / 1 − a にした物)。 1 = 暗くしない。 −1 = 真っ黒の板 (シュヴァリエの一拍)。</summary>
        private double DarkMul()
        {
            if (_seg == Seg.None) return 1.0;
            int i = FrameI; double t = i / 8.0;
            bool half = _kind == Kind.King || _kind == Kind.Judge;
            double a;
            if (_seg == Seg.Charge)
            {
                if (_kind == Kind.Chevalier) a = 0.3 * Math.Min(1.0, i / 6.0) + (DARK_C - 0.3) * Math.Max(0.0, Math.Min(1.0, (i - 13) / 3.0));
                else if (half) a = Math.Min(1.0, i / 6.0);
                else a = DARK_V * Math.Min(1.0, i / 6.0);
            }
            else if (_seg == Seg.Idle) a = _kind == Kind.Chevalier ? DARK_C : half ? 1.0 : DARK_V;
            else
            {
                switch (_kind)
                {
                    case Kind.King: a = 1 - Sm(LEAD_K, LEAD_K + 0.4, t); break;
                    case Kind.Judge: a = 1 - Sm(LEAD_J, LEAD_J + 0.4, t); break;
                    case Kind.Chevalier: if (t >= CUT_C) return 1.0; if (t >= LEAD_C * 0.8) return -1.0; a = DARK_C; break;
                    default: a = i < (int)(LEAD_V * 8) ? DARK_V : 0.0; break;
                }
            }
            return half ? 1 - 0.5 * a : 1 - a;
        }

        /// <summary>背景と敵を描いた直後に呼ぶ: 暗幕 (見本で、 効果を貼る前の舞台に掛けている物)。</summary>
        public void PreDark(BattleCanvas c)
        {
            double m = DarkMul();
            GuiDark = (float)m; GuiRed = 0f; GuiShake = Vector2Int.zero;      // GUI へ渡す値 (このフレーム・Post まで書き足す)
            if (m < 0) { uint k = BattleCanvas.Pack(2, 3, 8); for (int i = 0; i < c.FB.Length; i++) c.FB[i] = k; return; }      // #020308 = 色の組 steel の一番暗い色・黒い 2 枚と同じ
            if (m < 1.0) Dark(c, m);
        }

        private static void Paste(BattleCanvas c, FxSheet s, int k, int ax, int ay)
        {
            if (s == null) return;
            if (s.W == W && s.H == H) s.DrawFrame(c, k, 0, 0);
            else s.DrawFrame(c, k, ax - s.PivotX, ay - s.PivotY);
        }

        /// <summary>手描きの演出の後に呼ぶ: チャージ・待機・大技のコマ、 王の赤いフィルター、 ヴェスカの凍った画面と破片。</summary>
        public void DrawLayers(BattleCanvas c)
        {
            if (_seg == Seg.None) return;
            int i = FrameI;
            if (_seg == Seg.Charge) { Paste(c, _charge, i, VX, VY); return; }
            if (_seg == Seg.Idle) { Paste(c, _idle, i % _idle.FrameCount, VX, VY); return; }
            double t = i / 8.0;
            switch (_kind)
            {
                case Kind.King:
                {
                    int j = i - OffK; double e1 = LEAD_K + RUSH + HOLD;
                    if (_impIdx != null && j >= 0 && j < _impIdx.Length) Paste(c, _imp, _impIdx[j], EX, FLOOR);
                    if (_scr != null) Paste(c, _scr, i, 0, 0);
                    double ra = (t < LEAD_K ? 0.45 * Sm(0, LEAD_K, t) : 0.55 + 0.45 * (1 - Sm(LEAD_K, LEAD_K + 0.5, t))) * (1 - Sm(e1, e1 + COOL, t));
                    Red(c, ra); GuiRed = ra > 0.01 ? (float)ra : 0f;
                    Paste(c, _rel, i, 0, 0);      // 発光は赤いフィルターの上 (白く飛ぶ)
                    break;
                }
                case Kind.Judge:
                {
                    int j = i - (int)Math.Round(LEAD_J * 8);
                    if (_ver2 != null && j >= 0) Paste(c, _ver2, j, 0, 0);
                    Paste(c, _rel, i, 0, 0);
                    break;
                }
                case Kind.Chevalier: Paste(c, _rel, i, 0, 0); break;
                case Kind.Vesca:
                {
                    int n1 = (int)(LEAD_V * 8);
                    if (i < n1)
                    {
                        Paste(c, _rel, Math.Min(i, _rel.FrameCount - 1), VX, VY);
                        // 止まった瞬間の画面の写し: 溜めの最後のコマ (見本の seq[-1])。 描くたびに写し直し、 最後に描いた物が残る (コマを飛ばした時も直前のコマが残る)
                        if (_frozen == null) _frozen = new uint[W * H];
                        Array.Copy(c.FB, _frozen, _frozen.Length); _frozenTinted = false;
                        break;
                    }
                    int jj = i - n1;
                    if (_frozen != null)
                    {
                        if (!_frozenTinted) { FrozenTint(_frozen); _frozenTinted = true; }
                        var geom = _shards != null && jj < _shards.Count ? _shards[jj] : null;
                        if (geom == null) Array.Copy(_frozen, c.FB, _frozen.Length);      // 割れる前は 1 枚
                        else Shattered(c, geom);                                            // 割れたら破片 (後ろは動いているふつうの画面)
                    }
                    if (_shatter != null) Paste(c, _shatter, jj, 0, 0);
                    break;
                }
                case Kind.VescaFinal:
                {
                    int n1 = (int)(LEAD_V * 8);
                    if (i < n1) { Paste(c, _rel, Math.Min(i, _rel.FrameCount - 1), VX, VY); break; }
                    var parts = P4Parts; int jj = i - n1;
                    if (jj < parts.Count) Paste(c, parts[jj].Key, parts[jj].Value, 0, 0);
                    break;
                }
            }
        }

        /// <summary>全部描いた後に呼ぶ: 揺れ (空いた所は見本の地の色) と、 ヴェスカ 4 段目の帯のずれ。</summary>
        public void Post(BattleCanvas c)
        {
            if (_seg == Seg.None) return;
            int i = FrameI;
            if (_kind == Kind.VescaFinal && _tear != null)
            {
                List<Band> bands = null;
                if (_seg == Seg.Charge) bands = i < _tearCharge.Count ? _tearCharge[i] : null;
                else if (_seg == Seg.Idle) bands = _tearIdle.Count > 0 ? _tearIdle[(i % _idle.FrameCount) % _tearIdle.Count] : null;
                else if (_seg == Seg.Heavy) bands = i < _tear.Count ? _tear[i] : null;
                if (bands != null && bands.Count > 0) Torn(c, bands);
                return;
            }
            if (_seg != Seg.Heavy) return;
            switch (_kind)
            {
                case Kind.King: { int j = i - OffK - 2; if (j >= 0 && j < 7) Shift(c, Shake7[j, 0], Shake7[j, 1], FillK); break; }
                case Kind.Judge: { int j = i - (int)Math.Round(LEAD_J * 8) - 2; if (j >= 0 && j < 7) Shift(c, Shake7[j, 0], Shake7[j, 1], FillJ); break; }
                case Kind.Chevalier: { int j = i - (int)(SPLIT_C * 8) - 1; if (j >= 0 && j < 6) Shift(c, Shake6C[j, 0], Shake6C[j, 1], FillC); break; }
                case Kind.Vesca: { int j = i - (int)(LEAD_V * 8) - 4; if (j >= 0 && j < 6) Shift(c, Shake6V[j, 0], Shake6V[j, 1], FillV); break; }
            }
        }

        /// <summary>見本の paste(b, (dx, dy)) を地の色の板へ: 絵を (dx, dy) ずらし、 空いた所は fill。</summary>
        private void Shift(BattleCanvas c, int dx, int dy, uint fill)
        {
            GuiShake = new Vector2Int(dx, dy);
            if (dx == 0 && dy == 0) return;
            if (_tmp == null) _tmp = new uint[W * H];
            Array.Copy(c.FB, _tmp, _tmp.Length);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int sx = x - dx, sy = y - dy;
                    c.FB[y * W + x] = sx >= 0 && sy >= 0 && sx < W && sy < H ? _tmp[sy * W + sx] : fill;
                }
        }

        // ── ヴェスカ 1〜3 段目: 凍った画面と破片 (vesca_flow_preview.py の frozen_tint / shattered) ──

        /// <summary>色を抜いて紫がかった灰色に: L = Pillow の RGB→L (×19595/38470/7471・丸め)・g = min(255, int(L × 1.5 + 14))・(int(g × 0.86), int(g × 0.8), g)。</summary>
        private static void FrozenTint(uint[] px)
        {
            for (int i = 0; i < px.Length; i++)
            {
                uint p = px[i]; int r = (int)(p & 255), gg = (int)((p >> 8) & 255), b = (int)((p >> 16) & 255);
                int l = (r * 19595 + gg * 38470 + b * 7471 + 0x8000) >> 16;
                int g = Math.Min(255, (int)(l * 1.5 + 14));
                px[i] = BattleCanvas.Pack((int)(g * 0.86), (int)(g * 0.8), g);
            }
        }

        private sealed class Shard { public double[] sx, sy, dx, dy; }
        private List<List<Shard>> _shards;

        private void LoadShards()
        {
            _shards = new List<List<Shard>>();
            var a = Resources.Load<TextAsset>("Game2DFx/vesca-shards");
            if (a == null) { Debug.LogWarning("[BossFx] Resources/Game2DFx/vesca-shards が無い"); return; }
            var root = FxJson.Parse(a.text) as Dictionary<string, object>;
            foreach (var f in (List<object>)root["frames"])
            {
                if (f == null) { _shards.Add(null); continue; }
                var list = new List<Shard>();
                foreach (Dictionary<string, object> sh in (List<object>)f)
                {
                    var src = (List<object>)sh["src"]; var dst = (List<object>)sh["dst"]; int n = dst.Count;
                    var s = new Shard { sx = new double[n], sy = new double[n], dx = new double[n], dy = new double[n] };
                    for (int k = 0; k < n; k++)
                    {
                        var p = (List<object>)src[k]; var q = (List<object>)dst[k];
                        s.sx[k] = FxJson.Num(p[0]); s.sy[k] = FxJson.Num(p[1]); s.dx[k] = FxJson.Num(q[0]); s.dy[k] = FxJson.Num(q[1]);
                    }
                    list.Add(s);
                }
                _shards.Add(list);
            }
            Resources.UnloadAsset(a);
        }

        /// <summary>凍った画面の写しを、 破片の形に貼る (奥から順・破片ごとに 1 つのアフィン変換 = 今の位置 → 元の位置を最小二乗で解く)。
        /// 写しの外を引いた所は、 写しの (2, 2) の色。 ほぼ真横を向いた破片 (面積の 2 倍 &lt; 6) は描かない。</summary>
        private void Shattered(BattleCanvas c, List<Shard> geom)
        {
            uint edge = _frozen[2 * W + 2];
            foreach (var s in geom)
            {
                int n = s.dx.Length;
                double area2 = 0; for (int k = 0; k < n; k++) { int p = (k + n - 1) % n; area2 += s.dx[k] * s.dy[p] - s.dy[k] * s.dx[p]; }
                if (Math.Abs(area2) < 6) continue;
                if (!SolveAffine(s.dx, s.dy, s.sx, out double a0, out double a1, out double a2) || !SolveAffine(s.dx, s.dy, s.sy, out double b0, out double b1, out double b2)) continue;
                FillPolygon(s.dx, s.dy, (x, y) =>
                {
                    // Pillow の AFFINE (最近傍): 出力の画素の中心 (x + 0.5, y + 0.5) を写して切り捨て
                    double u = a0 * (x + 0.5) + a1 * (y + 0.5) + a2, v = b0 * (x + 0.5) + b1 * (y + 0.5) + b2;
                    int ix = u < 0 ? -1 : (int)u, iy = v < 0 ? -1 : (int)v;
                    c.FB[y * W + x] = ix >= 0 && iy >= 0 && ix < W && iy < H ? _frozen[iy * W + ix] : edge;
                });
            }
        }

        /// <summary>z ≈ c0·x + c1·y + c2 を最小二乗で解く (numpy.linalg.lstsq と同じ答え・正規方程式)。</summary>
        private static bool SolveAffine(double[] x, double[] y, double[] z, out double c0, out double c1, out double c2)
        {
            double sxx = 0, sxy = 0, sx = 0, syy = 0, sy = 0, n = x.Length, sxz = 0, syz = 0, sz = 0;
            for (int k = 0; k < x.Length; k++) { sxx += x[k] * x[k]; sxy += x[k] * y[k]; sx += x[k]; syy += y[k] * y[k]; sy += y[k]; sxz += x[k] * z[k]; syz += y[k] * z[k]; sz += z[k]; }
            double[,] m = { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } }; double[] r = { sxz, syz, sz };
            double det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            c0 = c1 = c2 = 0;
            if (Math.Abs(det) < 1e-9) return false;
            Func<int, double> col = q =>
            {
                double[,] k = (double[,])m.Clone(); for (int row = 0; row < 3; row++) k[row, q] = r[row];
                return (k[0, 0] * (k[1, 1] * k[2, 2] - k[1, 2] * k[2, 1]) - k[0, 1] * (k[1, 0] * k[2, 2] - k[1, 2] * k[2, 0]) + k[0, 2] * (k[1, 0] * k[2, 1] - k[1, 1] * k[2, 0])) / det;
            };
            c0 = col(0); c1 = col(1); c2 = col(2);
            return true;
        }

        private static int RoundUp(double f) => f >= 0.0 ? (int)Math.Floor(f + 0.5) : -(int)Math.Floor(Math.Abs(f) + 0.5);
        private static int RoundDown(double f) => f >= 0.0 ? (int)Math.Ceiling(f - 0.5) : -(int)Math.Ceiling(Math.Abs(f) - 0.5);

        /// <summary>多角形の塗り (見本の ImageDraw.polygon(fill) = Pillow の polygon_generic に寄せた: 頂点を整数へ切り捨て、 整数の行ごとに辺との交点を出し、
        /// 辺の下端の行は交点を 2 度数え、 [切り上げ, 切り下げ] を塗る)。 Pillow の「角をつなぐ」補正は移していない (縁の数ドットが違うことがある)。</summary>
        private static void FillPolygon(double[] fx, double[] fy, Action<int, int> put)
        {
            int n = fx.Length; var x = new int[n]; var y = new int[n];
            for (int k = 0; k < n; k++) { x[k] = (int)fx[k]; y[k] = (int)fy[k]; }      // Pillow は頂点を (int) で切り捨てる (0 へ向けて)
            // 辺 (水平は除く)
            var ex0 = new List<int>(); var ey0 = new List<int>(); var eymin = new List<int>(); var eymax = new List<int>(); var edx = new List<double>();
            int ymin = int.MaxValue, ymax = int.MinValue;
            for (int k = 0; k < n; k++)
            {
                int p = (k + 1) % n; if (p == 0 && x[k] == x[0] && y[k] == y[0]) continue;
                int x0 = x[k], y0 = y[k], x1 = x[p], y1 = y[p];
                if (y0 == y1) continue;
                ex0.Add(x0); ey0.Add(y0); eymin.Add(Math.Min(y0, y1)); eymax.Add(Math.Max(y0, y1)); edx.Add((double)(x1 - x0) / (y1 - y0));
                ymin = Math.Min(ymin, Math.Min(y0, y1)); ymax = Math.Max(ymax, Math.Max(y0, y1));
            }
            if (ymin < 0) ymin = 0;
            if (ymax > H) ymax = H;
            var xx = new List<double>();
            for (int yy = ymin; yy <= ymax; yy++)
            {
                xx.Clear();
                for (int e = 0; e < ex0.Count; e++)
                {
                    if (yy < eymin[e] || yy > eymax[e]) continue;
                    double v = (yy - ey0[e]) * edx[e] + ex0[e];
                    xx.Add(v);
                    if (yy == eymax[e] && yy < ymax) xx.Add(v);
                }
                xx.Sort();
                if (yy >= H) continue;
                for (int q = 1; q < xx.Count; q += 2)
                {
                    int xs = RoundUp(xx[q - 1]), xe = RoundDown(xx[q]);
                    for (int px = Math.Max(0, xs); px <= Math.Min(W - 1, xe); px++) put(px, yy);
                }
            }
        }

        // ── ヴェスカ 4 段目: 画面全体を帯ごとにずらす (vesca_flow_preview.py の torn・帯の表 vesca-tear.json) ──

        private struct Band { public int y, h, dx, kind, ca; }      // kind: 0 = shift / 1 = smear / 2 = ghost
        private List<List<Band>> _tear, _tearCharge, _tearIdle;

        private void LoadTear()
        {
            _tear = new List<List<Band>>(); _tearCharge = new List<List<Band>>(); _tearIdle = new List<List<Band>>();
            var a = Resources.Load<TextAsset>("Game2DFx/vesca-tear");
            if (a == null) { Debug.LogWarning("[BossFx] Resources/Game2DFx/vesca-tear が無い"); return; }
            var root = FxJson.Parse(a.text) as Dictionary<string, object>;
            Fill(root, "frames", _tear); Fill(root, "charge", _tearCharge); Fill(root, "idle", _tearIdle);
            Resources.UnloadAsset(a);
        }

        private static void Fill(Dictionary<string, object> root, string key, List<List<Band>> into)
        {
            if (!root.TryGetValue(key, out var o) || !(o is List<object> frames)) return;
            foreach (List<object> f in frames)
            {
                var l = new List<Band>();
                foreach (Dictionary<string, object> b in f)
                {
                    string k = b["kind"] as string;
                    l.Add(new Band { y = FxJson.Int(b["y"]), h = FxJson.Int(b["h"]), dx = FxJson.Int(b["dx"]), ca = b.TryGetValue("ca", out var ca) ? FxJson.Int(ca) : 0, kind = k == "smear" ? 1 : k == "ghost" ? 2 : 0 });
                }
                into.Add(l);
            }
        }

        private static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        /// <summary>見本の torn(im, bands): 帯は元の画面から切り出す (前の帯の結果は読まない)。 shift = 横へずらす (はみ出た分は反対側から)・
        /// smear = 列 x0 = |dx|×3 mod W から右を、 その 1 列で引き伸ばす・ghost = ずらした帯と元の明るい方 (色ごとの最大)。
        /// どの帯にも色収差: 赤を左へ・青を右へ ca ドット (ImageChops.offset と同じく、 はみ出た分は反対側から)。</summary>
        private void Torn(BattleCanvas c, List<Band> bands)
        {
            if (_tmp == null) _tmp = new uint[W * H];
            Array.Copy(c.FB, _tmp, _tmp.Length);
            var strip = new uint[W];
            foreach (var b in bands)
            {
                for (int y = b.y; y < b.y + b.h && y < H; y++)
                {
                    if (y < 0) continue;
                    int row = y * W;
                    for (int x = 0; x < W; x++)
                    {
                        uint v;
                        if (b.kind == 0) v = _tmp[row + Mod(x - b.dx, W)];
                        else if (b.kind == 1) { int x0 = Math.Abs(b.dx) * 3 % W; v = _tmp[row + (x >= x0 ? x0 : x)]; }
                        else
                        {
                            uint p = _tmp[row + x], q = _tmp[row + Mod(x - b.dx, W)];
                            v = BattleCanvas.Pack((int)Math.Max(p & 255, q & 255), (int)Math.Max((p >> 8) & 255, (q >> 8) & 255), (int)Math.Max((p >> 16) & 255, (q >> 16) & 255));
                        }
                        strip[x] = v;
                    }
                    for (int x = 0; x < W; x++)
                    {
                        uint r = strip[Mod(x + b.ca, W)] & 255, g = (strip[x] >> 8) & 255, bl = (strip[Mod(x - b.ca, W)] >> 16) & 255;
                        c.FB[row + x] = BattleCanvas.Pack((int)r, (int)g, (int)bl);
                    }
                }
            }
        }
    }
}

using System.Collections.Generic;
using GameLoop;
using UnityEngine;
using UnityEngine.UI;
using UI.Game2D.Battle;

namespace UI.Game2D
{
    /// <summary>
    /// 戦闘の GUI (<b>仮置き</b>・2026-10-24)。 枠・ダイスの面・ボタン・ランプ・ゲージ・丸ボタンは PixelLab (参考画像の切り抜き + パレット抽出) で作った絵
    /// (Assets/gui2d/battle_ui/・<see cref="Game2DKitAssets"/>)。 枠は 1 枚の絵を 9 分割してパネルの大きさに合わせる。
    /// 右 = 敵の出目と攻撃値 (予告の枠) / 上の帯 = 充電・エスカレーション・敵 HP。 状態は <see cref="Game2DBattleSim"/> (実戦闘の流れ) から読む。
    /// 航行用の GUI が引っ込むのと入れ替わりに、 左・右・上からばねで入ってくる (<see cref="Drive"/>)。 座標は 480×270 の格子 (左上が原点)。
    /// <para>端子の山札 (ADR-0014): 左に 3 枚を積む: Ports (出目とそれぞれの差し込み口・出目が 4 個以上なら 3 個ずつの列を右へ足す) / ステータス / Ctl (操作)。
    /// 右下 (Hand) にこのターンに引いた端子 (USB メモリの形) を並べ、 つかんで出目の差し込み口へ落とすと装着する (<see cref="Game2DBattleSim.Plug"/>)。
    /// 〈写し取り〉〈焼き切り〉〈呼び戻し〉に出目を挿すと、 挿していない手札の端子に「候補」の印が付き、 候補をその端子の上へ落とすと対象が決まる
    /// (<see cref="Game2DBattleSim.SetTarget"/>)。 名前・条件・効果は USB にマウスを乗せた時だけ帯 (Tip) で出す。 発動の見込みは
    /// <see cref="CombatSystem.TerminalCards.Resolve"/> を状態の写しで呼んだ値 (<see cref="Game2DBattleSim.preview"/>)。</para>
    /// (旧い配線の画面 ── 左のパネルに出目 3 個と攻撃・防御・充電・特殊の板 ── は 2026-11-11 に削除。 戦闘は常に山札で戦う)
    /// </summary>
    public class Game2DBattleHud : MonoBehaviour
    {
        // 配置 (dot)
        // 2026-11-11: 字を既定のフォント (高さ 12) にしたので、 右の枠を広げ、 上の帯を高くし、 右は帯の下から始める
        //   場所の値は BattleHudLayout (Unity 非依存・検算 Tools/inventory_check が入口 [持ち物] とほかの部品の重なりを数える)
        private static RectInt R(InventoryLayout.Box b) => new RectInt(b.x, b.y, b.w, b.h);
        private static readonly RectInt Right = R(BattleHudLayout.Right), Top = R(BattleHudLayout.Top);
        private const int FrameB = 12;
        /// <summary>自分のステータス (会心率・攻撃・防御): 左下 (Ports と Ctl の間・左と同じばね)。</summary>
        private static readonly RectInt Stat = R(BattleHudLayout.Stat);
        /// <summary>成立した役 (ADR-0011): Ports の上の空き (上の帯の左・左と同じばね)。 役が無いターンは出さない。</summary>
        private static readonly RectInt RoleR = R(BattleHudLayout.Role);
        /// <summary>持ち物の画面の入口 [持ち物] (2026-11-13): 役の板の上の空き (上の帯の左・左と同じばね)。 キー I でも開く。 見るだけ (Game2DNavExtras.OpenInventoryInBattle)。</summary>
        private static readonly RectInt InvR = R(BattleHudLayout.Inv);

        // ── 端子の山札 (ADR-0014・2026-11-11) の配置 ──
        //   Ports (2,60,108,98)   = 出目と、 出目ごとの差し込み口 (挿した USB は横向きに挿さった絵で残る)。 3 個ずつ縦に並べ、 4 個目からは列を右へ足す。
        //   Stat (2,162,108,48)   = 会心・攻撃・防御/貫通。
        //   Ctl (2,214,108,52)    = 振り直す・おまかせ / 道具・逃げる・決める。 x 110 まで = 資源バー (x 110〜・y 215〜) にかからない。
        //   Hand (318,184,160,80) = このターンに引いた端子 (縦向きの USB・左から) と常設 (右の端・仕切りの右)・山/捨ての枚数。 敵 (x 190〜290) と予告の枠 (Right・y 〜180) の外。
        //   Tip (幅 200)          = USB にマウスを乗せた時だけ。 予告の枠 (Right) と敵の頭上の予告 (y 〜78) に重ねない:
        //                           差し込み口の USB は Ports の右・口の高さ (y は TipMinY から)、 手札の USB は Hand の上で、 右端を予告の枠の左に揃える。
        private static readonly RectInt Ports = R(BattleHudLayout.Ports), Ctl = R(BattleHudLayout.Ctl), Hand = R(BattleHudLayout.Hand);
        private const int TipW = 200, TipLinesMax = 6, TipH = 6 + TipLinesMax * 13 + 3, TipMinY = 82, TipMaxY = 214;
        // 差し込み口の行 (Ports の中): 出目 → 配線 → 口 (USB の差し込み部が口に入る) → 本体 → ランプ。 1 列 3 個・列の幅 76
        private const int PortRows = BattleHudLayout.PortRows, PortColW = BattleHudLayout.PortColW;
        private static int PortCols(int n) => Mathf.Max(1, (n + PortRows - 1) / PortRows);
        private static int PortsW(int n) => Ports.width + (PortCols(n) - 1) * PortColW;
        private static int PortX(int i) => (i / PortRows) * PortColW;
        private static int PortY(int i) => 6 + (i % PortRows) * 31;
        private static RectInt PDieR(int i) => new RectInt(PortX(i) + 8, PortY(i) + 3, 18, 18);
        private static RectInt PortR(int i, int n) => new RectInt(PortX(i) + 30, PortY(i), PortCols(n) > 1 ? PortColW - 30 : 68, UsbBody);      // 落とせる範囲 (口と挿さった USB)
        // USB の大きさ: 差し込み部 8×12 (縦向きは 12×8) + 本体 24×24
        private const int UsbConn = 8, UsbConnW = 12, UsbBody = 25, UsbLong = UsbConn + UsbBody;
        // 手札 (Hand の中)
        private const int HandY = 18, HandStep = 28, HandPad = 6;
        private static readonly RectInt CReroll = new RectInt(5, 5, 56, 20), CAuto = new RectInt(63, 5, 40, 20),
            CItem = new RectInt(5, 27, 24, 20), CFlee = new RectInt(31, 27, 36, 20), CGo = new RectInt(69, 27, 34, 20);
        private RectTransform _ports, _ctl, _hand, _tip, _drag, _role, _inv; private Texture2D _tp, _tc, _th, _tt2, _td, _trl, _tinv;
        private int _portsTexW = -1;

        /// <summary>端子のアイコン (16×16・行優先・左上から・0 = 透明 / 1 = 暗 / 2 = 中 / 3 = 明)。 無ければ null。</summary>
        private static byte[] IconOf(string id) => Game2DTerminalIcons.Get(id);

        public Game2DBattleSim sim;
        public Game2DBattleFx fx;
        public Game2DScreen screen;
        [Tooltip("決定ボタンの絵。 枠・ダイスなどの共通の絵は Game2DKitAssets")]
        public Texture2D btnGoTex;

        [SerializeField] private RectTransform _right, _top;
        private RectTransform _stat;
        private Texture2D _tr, _tt, _ts;
        private int _drawn = -1; private float _slide;
        private KitSrc _bGo; private bool _srcInit;

        private static readonly Color32 Ink = Game2DKit.Ink, Dim = Game2DKit.Dim, Dark = Game2DKit.Dark, Off = Game2DKit.Off, Well = Game2DKit.Well,
            Atk = Game2DKit.Red, Blk = Game2DKit.Blue, Chg = Game2DKit.Gold, Hp = new Color32(0xd1, 0x50, 0x3c, 255);
        /// <summary>対象を選ぶ印: 候補の縁 / 決まった対象の縁 (色と明度だけで出す・拡大しない)。</summary>
        private static readonly Color32 CandEdge = Game2DTerminalLook.Mix(Game2DKit.Ivory, Game2DKit.Off, 0.45f), ChosenEdge = Game2DKit.Ivory;
        /// <summary>ヴェスカ〈停滞する時間〉で止まった端子の印 (本体を横切る線と下の「停止」)。 暗くした本体の上で読める、 くすんだ青白。</summary>
        /// <summary>手札の規則 Pick: 残す印の色 (仮)。</summary>
        private static readonly Color32 KeepMark = Game2DTerminalLook.Mix(Game2DKit.Amber, Game2DKit.Ivory, 0.3f);
        private static readonly Color32 StopMark = Game2DTerminalLook.Mix(Game2DKit.Blue, Game2DKit.Ivory, 0.45f);

        /// <summary>手札・出目をつかんでいる間と、 離したフレーム (の次まで) は true。 敵の詳細の窓 (<see cref="Game2DEnemyPanel"/>) はこの間は開かない。</summary>
        public static bool PointerBusy => _busyDrag || Time.frameCount <= _releaseFrame + 1;
        private static bool _busyDrag; private static int _releaseFrame = -10;

        /// <summary>手引きの強調 (issue #109) が範囲を読む部品。</summary>
        public enum Part { Intent, Top, Ports, Stat, Ctl, Hand }

        /// <summary>部品の RectTransform (まだ作っていなければ null)。 位置はばねで動くので、 読む側が毎フレーム読み直す。</summary>
        public RectTransform PartRect(Part p)
        {
            switch (p)
            {
                case Part.Intent: return _right;
                case Part.Top: return _top;
                case Part.Ports: return _ports;
                case Part.Stat: return _stat;
                case Part.Ctl: return _ctl;
                default: return _hand;
            }
        }

        /// <summary>見た目を作る (builder から 1 度呼ぶ)。 親は 480×270 の格子の枠。</summary>
        public void Build()
        {
            _right = Make("right", Right, out _tr); _top = Make("top", Top, out _tt);
            _stat = Make("stat", Stat, out _ts);
        }

        private RectTransform Make(string name, RectInt r, out Texture2D tex)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rt = (RectTransform)go.transform; rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(r.x, -r.y); rt.sizeDelta = new Vector2(r.width, r.height);
            tex = null;      // 絵は実行時に作る (Ensure)。 シーンには保存しない
            var ri = go.AddComponent<RawImage>(); ri.raycastTarget = false; ri.enabled = false;
            return ri.rectTransform;
        }

        private void OnDestroy()
        {
            foreach (var t in new[] { _tr, _tt, _ts, _tp, _tc, _th, _tt2, _td, _trl, _tinv }) if (t != null) Destroy(t);
            _busyDrag = false;
        }

        /// <summary>組み上げ済みのシーンに無いパネルを作る (ステータス・山札の画面の部品。 実行時に作り、 シーンには保存しない)。
        /// 作る順 = 重なりの順 (後ほど手前): 説明の帯とつかんでいる USB をいちばん手前に。</summary>
        private void EnsurePanels()
        {
            if (_stat == null) _stat = Make("stat", Stat, out _ts);
            if (_ports == null) _ports = Make("ports", Ports, out _tp);
            if (_ctl == null) _ctl = Make("ctl", Ctl, out _tc);
            if (_hand == null) _hand = Make("hand", Hand, out _th);
            if (_role == null) _role = Make("role", RoleR, out _trl);
            if (_inv == null) _inv = Make("inv", InvR, out _tinv);
            if (_tip == null) _tip = Make("tip", new RectInt(0, 0, TipW, TipH), out _tt2);
            if (_drag == null) _drag = Make("drag", new RectInt(0, 0, UsbBody, UsbLong), out _td);
        }

        private Game2DSpring _spL, _spR, _spT; private bool _spInit;
        /// <summary>入り・出の途中を含めて、 どれかが見えている。</summary>
        public bool Visible => !_spInit || !(_spL.tgt < 0.5f && _spR.tgt < 0.5f && _spT.tgt < 0.5f && _spL.x < 0.005f && _spR.x < 0.005f && _spT.x < 0.005f && _spL.Settled && _spR.Settled && _spT.Settled);

        /// <summary>戦闘の GUI をばねで出し入れする (毎フレーム)。 入る順は 左 → 上 → 右 で、 少しずつずらす。 行き過ぎは 3 dot 以内。</summary>
        public void Drive(bool on, float dt)
        {
            if (_right == null) return;
            EnsurePanels();
            if (!_spInit) { _spL.Set(0); _spR.Set(0); _spT.Set(0); _spInit = true; }
            float t = on ? 1f : 0f;
            _spL.Go(t, on ? 0.03f : 0f); _spT.Go(t, on ? 0.07f : 0.03f); _spR.Go(t, on ? 0.12f : 0.06f);
            _spL.Step(dt); _spT.Step(dt); _spR.Step(dt);
            _slide = _spL.x;
            float inL = Mathf.Round((1f - _spL.Value(200f)) * 200f), inR = Mathf.Round((1f - _spR.Value(170f)) * 170f);
            _right.anchoredPosition = new Vector2(Right.x + Mathf.Round((1f - _spR.Value(120f)) * 120f), -Right.y);
            _top.anchoredPosition = new Vector2(Top.x, -Top.y + Mathf.Round((1f - _spT.Value(62f)) * 62f));
            _stat.anchoredPosition = new Vector2(Stat.x - inL, -Stat.y);
            _ports.anchoredPosition = new Vector2(Ports.x - inL, -Ports.y);
            _ctl.anchoredPosition = new Vector2(Ctl.x - inL, -Ctl.y);
            _hand.anchoredPosition = new Vector2(Hand.x + inR, -Hand.y);
            _role.anchoredPosition = new Vector2(RoleR.x - inL, -RoleR.y);
            _inv.anchoredPosition = new Vector2(InvR.x - inL, -InvR.y);
            if (_spL.x < 0.95f) { Show(_tip, false); CancelDrag(); }      // 説明の帯・つかみは、 パネルが収まっている間だけ
        }

        private void Update()
        {
            if (_right == null || sim == null || !Game2DBattleSim.Active) { CancelDrag(); return; }
            // 配線を決める間だけ読み直す (その後は本判定に取り込まれて二重に数えるので、 最後の値を出し続ける)
            if (sim.CanOperate && CombatSystem.CombatManager.Instance != null)
            { _hs = CombatSystem.CombatManager.Instance.PreviewHudStats(); _hsKnown = true; }
            bool ptr = _slide > 0.95f && !Game2DModal.Active && screen != null;
            // 持ち物の画面 (見るだけ・2026-11-13): キー I。 窓が出ている間・つかんでいる間・BOT が動かしている間は開かない
            if (_slide > 0.95f && !Game2DModal.Active && !PointerBusy && !Game2DTutorial.Blocking && Input.GetKeyDown(KeyCode.I)) OpenInventory();
            Vector2 d = default; bool hasDot = ptr && Game2DPointer.TryDot(screen, out d);
            DeckPointer(hasDot, d);
            // 引いてくる動きの間は毎フレーム描き直す
            if (sim.boardSerial != _dealSerial) { _dealSerial = sim.boardSerial; _dealT0 = Time.unscaledTime; }
            bool dealing = Time.unscaledTime - _dealT0 < DealTime(sim.board.Count);
            int ehp = fx != null ? fx.EnemyHp : 0, ver = sim.version * 31 + ehp * 7 + sim.charge
                + (_hsKnown ? Mathf.RoundToInt(_hs.critRate * 1000f) * 131 + _hs.attack * 17 + _hs.outgoingPct * 7 + _hs.defense * 3 + _hs.pen + _hs.consOutgoingPct * 7919 + _hs.toilCutPct * 104729 : 0)
                + 977 + DeckCountsHash() + sim.DiceCount * 7577 + (dealing ? Time.frameCount * 7919 : 0);
            if (ver != _drawn) { _drawn = ver; Redraw(); }
        }

        private static bool In(Vector2 d, RectInt panel, RectInt r) => d.x >= panel.x + r.x && d.x < panel.x + r.x + r.width && d.y >= panel.y + r.y && d.y < panel.y + r.y + r.height;

        /// <summary>逃げるは 2 度押しで確定 (本当に? → 押すとこのターンは攻撃せずに離脱)。</summary>
        private void PressFlee()
        {
            if (_fleeArmed) { _fleeArmed = false; if (!sim.Flee()) Game2DSfx.Play("ui_click"); }
            else if (sim.CanFlee) { _fleeArmed = true; _drawn = -1; }
        }

        private static int DeckCountsHash()
        {
            var dk = CombatSystem.CombatManager.Instance?.TerminalDeck;
            return dk == null ? 0 : dk.draw.Count + dk.discard.Count * 37 + dk.exiled.Count * 1009;
        }

        // ── 絵 ──

        private void InitSrc()
        {
            if (_srcInit) return; _srcInit = true;
            if (Game2DKit.Plain) return;
            _bGo = KitSrc.From(btnGoTex);
        }

        private KitPix Frame(int w, int h) { var p = new KitPix(w, h); p.NineSlice(Game2DKit.Frame, FrameB, Well); return p; }
        private KitPix Frame(RectInt r) => Frame(r.width, r.height);

        /// <summary>ダイス 1 個: 面の絵 (点の数が出目)。 絵が無い時は無地の板 + 数字。</summary>
        private void DrawDie(KitPix p, RectInt r, int v, bool enemy, bool question = false)
        {
            var face = Game2DKit.DieFace(v);
            if (question || face == null)
            {
                p.Box(r.x, r.y, r.width, r.height, enemy ? new Color32(0xd8, 0xbc, 0xb4, 255) : Game2DKit.Ivory, Dark);
                p.Text(question ? "?" : v.ToString(), r.x + (r.width - 6) / 2, r.y + (r.height - 10) / 2 + 1, Dark, 2);
            }
            else p.Sprite(face, r.x, r.y, r.width, r.height);
        }

        private void Redraw()
        {
            InitSrc(); EnsurePanels();
            Ensure(ref _tr, _right, Right); Ensure(ref _tt, _top, Top);
            // 出目の個数で Ports の幅が変わる: 絵を作り直す
            int pw = PortsW(sim.DiceCount);
            if (pw != _portsTexW) { if (_tp != null) Destroy(_tp); _tp = null; _portsTexW = pw; _ports.sizeDelta = new Vector2(pw, Ports.height); }
            Ensure(ref _tp, _ports, new RectInt(Ports.x, Ports.y, pw, Ports.height)); Ensure(ref _tc, _ctl, Ctl); Ensure(ref _th, _hand, Hand);
            DrawPorts(_tp, pw); DrawCtl(_tc); DrawHand(_th); bool tip = DrawTip();
            Show(_ports, true); Show(_ctl, true); Show(_hand, true); Show(_tip, tip && _slide > 0.95f);
            DrawRight(_tr); DrawTop(_tt);
            Ensure(ref _ts, _stat, Stat); DrawStat(_ts);
            Ensure(ref _trl, _role, RoleR); Show(_role, DrawRole(_trl));
            Ensure(ref _tinv, _inv, InvR); DrawInv(_tinv); Show(_inv, true);
        }

        /// <summary>成立した役の板: 1 行目 = 「役」と名前 (目のテーマ・対 / 揃) / 2 行目 = そろった目。 役が無ければ描かずに false。
        /// 役をつくる出目は Ports で金の縁を付ける (<see cref="DrawRoleMark"/>)。 状態は色だけで出す (大きさは変えない)。</summary>
        private bool DrawRole(Texture2D t)
        {
            if (sim == null || !sim.rolled || sim.roles.Count == 0) return false;
            var k = sim.roles[0];
            int face = CombatSystem.YachtRoles.FaceOf(k);
            var p = Frame(RoleR);
            p.Text("役", 8, 5, Dim);
            p.Text(CombatSystem.YachtRoles.NameOf(k), 8 + Game2DJpFont.Width("役 "), 5, Chg);
            string f = face + "・" + face + (CombatSystem.YachtRoles.IsTriple(k) ? "・" + face : "");
            p.Text("目 " + f, 8, 19, Ink);
            p.Apply(t);
            return true;
        }

        /// <summary>持ち物の画面の入口 [持ち物] (2026-11-13)。 BOT が動かしている間は暗く出す (押しても開かない)。</summary>
        private void DrawInv(Texture2D t)
        {
            var p = new KitPix(InvR.width, InvR.height);
            SmallBtn(p, new RectInt(0, 0, InvR.width, InvR.height), BattleHudLayout.InvLabel, !GameHooks.BotDriving, Ink);
            p.Apply(t);
        }

        /// <summary>持ち物の画面を開く (見るだけ・消耗品の [使う] は出さない ── 戦闘中に使うのは [道具] の窓)。 戦闘の進行・乱数・判断待ちには触らない。</summary>
        private void OpenInventory()
        {
            if (GameHooks.BotDriving || Game2DModal.Active) return;
            if (Game2DNavExtras.OpenInventoryInBattle(GameManager.Instance)) Game2DSfx.Play("ui_click");
        }

        /// <summary>役をつくる出目の印: 金の縁 (〈猫の手〉の琥珀の縁と同じ形・色で分ける)。</summary>
        private static void DrawRoleMark(KitPix p, int i)
        {
            var r = PDieR(i); var c = Game2DKit.Gold;
            p.Rect(r.x - 1, r.y - 1, r.width + 2, 1, c); p.Rect(r.x - 1, r.y + r.height, r.width + 2, 1, c);
            p.Rect(r.x - 1, r.y, 1, r.height, c); p.Rect(r.x + r.width, r.y, 1, r.height, c);
        }

        /// <summary>パネルを出す / 隠す。 絵がまだ無いパネルは出さない。</summary>
        private static void Show(RectTransform rt, bool on)
        {
            var ri = rt != null ? rt.GetComponent<RawImage>() : null;
            if (ri != null) ri.enabled = on && ri.texture != null;
        }

        private CombatSystem.CombatManager.HudStats _hs; private bool _hsKnown;

        /// <summary>自分のステータス: 会心率と倍率・攻撃・防御と貫通。 数字は配線を決める間に読んだ値。</summary>
        private void DrawStat(Texture2D t)
        {
            var p = Frame(Stat);
            bool k = _hsKnown && sim != null && Game2DBattleSim.Active;
            string crit = k ? "会心 " + Mathf.RoundToInt(_hs.critRate * 100f) + "% " + _hs.critMul.ToString("0.0") + "倍" : "会心 --";
            string atk = k ? "攻撃 " + _hs.attack : "攻撃 --";      // 与ダメの倍率は着弾の数字の横に出す (Game2DBattleFx・2026-11-11)
            // 消耗品 (〈油・膏薬〉・立てた〈瞬間研磨剤〉) で足した与ダメ% だけは、 使った時にすぐ見えるよう攻撃の横に出す (issue #11)
            // 〈徒労〉の戦闘のデバフ (2026-11-14): 持っている間は攻撃の横に「徒労−N%」(与えるダメージ −N%・隠し倍率にしない)。 口が 0 なら出さない
            if (k && (_hs.consOutgoingPct > 0 || _hs.toilCutPct > 0))
            {
                string add = (_hs.consOutgoingPct > 0 ? " 与ダメ+" + _hs.consOutgoingPct + "%" : "")
                           + (_hs.toilCutPct > 0 ? " " + EventSystem.TimedEffects.BattleBoonRule.ToilName + "−" + _hs.toilCutPct + "%" : "");
                atk = Game2DJpFont.Width(atk + add) <= Stat.width - 14 ? atk + add : "攻撃" + _hs.attack + add;
                atk = Game2DJpFont.Clip(atk, Stat.width - 14);
            }
            string def = k ? "防御 " + _hs.defense + " 貫通 " + _hs.pen : "防御 --";
            p.Text(crit, 8, 5, Chg); p.Text(atk, 8, 18, Atk); p.Text(def, 8, 31, Blk);
            p.Apply(t);
        }

        /// <summary>テクスチャは実行時に作る (シーンには保存されないので、 再生のたびに作り直す)。</summary>
        private static void Ensure(ref Texture2D tex, RectTransform rt, RectInt r)
        {
            if (tex != null || rt == null) return;
            tex = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DBattleHud" };
            var ri = rt.GetComponent<RawImage>(); if (ri != null) { ri.texture = tex; ri.enabled = true; }
        }

        /// <summary>決定ボタン (決める → 次へ / 終わる)。</summary>
        private void DrawGo(KitPix p, RectInt r, bool live)
        {
            bool en = live && sim.ButtonEnabled; string lab = live ? sim.ButtonLabel : "...";
            if (_bGo != null) p.Sprite(_bGo, r.x, r.y, r.width, r.height);
            else p.Box(r.x, r.y, r.width, r.height, en ? new Color32(0x6d, 0x3a, 0x16, 255) : Off, Dark);
            p.TextC(lab, r, en ? Ink : Dim, 2);
        }

        // ── 端子の山札 (ADR-0014): 操作 ──
        //   右下の手札の USB をつかんで (左ボタンを押したまま動かす)、 左の出目の差し込み口へ落とす = その出目をその端子に挿す。
        //   挿さった USB もつかめる: 別の出目の口へ落とす = 移す / 口の外で離す = 手札へ戻す。 口の上で右クリック = 外す。
        //   別の USB を挿さっている口へ落とす = 入れ替え (前の USB は手札へ)。 落とせない口で離したら手札へ戻る。
        //   対象を選ぶ: 〈写し取り〉〈焼き切り〉〈呼び戻し〉が挿さっている口 (か手札の抜け殻) へ、 候補の USB を落とす = 対象に決める
        //   (その口への「入れ替え」より先に効く。 入れ替えたい時は右クリックで外してから挿す)。
        //   出目を押す = 行き先を順に変える (右クリックで逆・1〜9 キーと同じ)。 Q = おまかせ・Space = 決める (Sim のキー)。

        /// <summary>つかんでいる端子 (場の添字・−1 = つかんでいない) / つかんだ元の出目 (手札からなら −1)。</summary>
        private int _dragS = -1, _dragFrom = -1, _dropHover = -1;
        private bool _moved; private Vector2 _pressDot;
        /// <summary>つかんでいる間: 出目ごとに 挿せる口か / 対象に決められる口か。 場の添字ごとに 対象に決められる手札の抜け殻か。</summary>
        private bool[] _dropOk = new bool[3], _targetOk = new bool[3], _targetHand = new bool[0];
        /// <summary>マウスが乗っている USB (場の添字) と、 それが挿さっている出目 (手札なら −1)。 説明の帯に使う。</summary>
        private int _hoverS = -1, _hoverPort = -1, _handHover = -1;
        private int _dealSerial = -1; private float _dealT0 = -10f;

        private static float DealTime(int n) => 0.22f + 0.07f * n;

        private void DeckPointer(bool hasDot, Vector2 d)
        {
            int n = sim.DiceCount;
            if (_dragS >= 0)
            {
                if (!sim.CanOperate || _dragS >= sim.board.Count) { CancelDrag(); return; }
                if (hasDot)
                {
                    if (_drag != null) _drag.anchoredPosition = new Vector2(Mathf.FloorToInt(d.x) - UsbBody / 2, -(Mathf.FloorToInt(d.y) - UsbLong / 2));
                    if (!_moved && (d - _pressDot).sqrMagnitude > 9f) { _moved = true; Show(_drag, true); _drawn = -1; }
                    int tgt = PortAt(d), hh = HandAt(d);
                    if (tgt != _dropHover || hh != _handHover) { _dropHover = tgt; _handHover = hh; _drawn = -1; }
                }
                if (Input.GetMouseButtonUp(0)) EndDrag(hasDot ? PortAt(d) : -1, hasDot ? HandAt(d) : -1);
                return;
            }
            // 乗っている USB (説明の帯)
            int hs = -1, hp = -1;
            if (hasDot)
            {
                int pt = PortAt(d);
                if (pt >= 0 && pt < sim.slot.Length && sim.slot[pt] >= 0 && !sim.ShowingResult) { hs = sim.slot[pt]; hp = pt; }
                else if (pt >= 0 && sim.ShowingResult && sim.lastOutcome != null && pt < sim.lastOutcome.slotOfDie.Length && sim.lastOutcome.slotOfDie[pt] >= 0) { hs = sim.lastOutcome.slotOfDie[pt]; hp = pt; }
                else hs = HandAt(d);
            }
            if (hs != _hoverS || hp != _hoverPort) { _hoverS = hs; _hoverPort = hp; _drawn = -1; }
            if (!hasDot) return;
            if (Input.GetMouseButtonDown(0))
            {
                for (int i = 0; i < n; i++) if (In(d, Ports, PDieR(i))) { sim.CycleWire(i, 1); return; }
                int pt = PortAt(d);
                if (pt >= 0) { if (sim.CanOperate && sim.slot[pt] >= 0) StartDrag(sim.slot[pt], pt, d); return; }
                int h = HandAt(d);
                if (h >= 0)
                {
                    if (sim.CanOperate && sim.IsStopped(h)) { Game2DSfx.Play("ui_click"); return; }      // 〈停滞する時間〉で止まった端子はつかめない
                    if (sim.CanOperate && sim.Remaining(h) > 0) StartDrag(h, -1, d);
                    return;
                }
                if (In(d, Ctl, CReroll)) { sim.Reroll(); return; }
                if (In(d, Ctl, CAuto)) { sim.AutoWire(); return; }
                if (In(d, Ctl, CItem)) { OpenItems(); return; }
                if (In(d, new RectInt(0, 0, Game2DLayout.Width, Game2DLayout.Height), InvR)) { OpenInventory(); return; }
                if (In(d, Ctl, CFlee)) { PressFlee(); return; }
                if (In(d, Ctl, CGo)) { if (sim.ButtonEnabled) sim.Button(); }
            }
            else if (Input.GetMouseButtonDown(1))
            {
                for (int i = 0; i < n; i++) if (In(d, Ports, PDieR(i))) { sim.CycleWire(i, -1); return; }
                int pt = PortAt(d); if (pt >= 0) sim.Unplug(pt);
            }
        }

        private void StartDrag(int s, int from, Vector2 d)
        {
            int n = sim.DiceCount, m = sim.board.Count;
            _dragS = s; _dragFrom = from; _moved = false; _pressDot = d; _dropHover = _handHover = -1; _hoverS = _hoverPort = -1;
            _busyDrag = true;
            if (_dropOk.Length != n) { _dropOk = new bool[n]; _targetOk = new bool[n]; }
            if (_targetHand.Length != m) _targetHand = new bool[m];
            for (int i = 0; i < n; i++)
            {
                _dropOk[i] = sim.CanPlug(i, s, from);
                // 手札の候補を、 対象を取る端子が挿さった口へ落とす = 対象に決める (手札から つかんだ時だけ)
                int ps = i < sim.slot.Length ? sim.slot[i] : -1;
                _targetOk[i] = from < 0 && ps >= 0 && sim.CanSetTarget(ps, s);
            }
            for (int q = 0; q < m; q++) _targetHand[q] = from < 0 && sim.CanSetTarget(q, s);
            // つかんでいる USB の絵 (縦向き)。 動かし始めたら出す
            Ensure(ref _td, _drag, new RectInt(0, 0, UsbBody, UsbLong));
            var p = new KitPix(UsbBody, UsbLong);
            var def = CombatSystem.TerminalCards.Get(sim.board[s]);
            var c = UsbColors(def, true, false, true, false);
            DrawUsb(p, 0, 0, false, def, c.body, Game2DKit.Amber, c.metal);
            p.Apply(_td);
            _drag.SetAsLastSibling();
            _drawn = -1;
        }

        private void EndDrag(int port, int hand)
        {
            int s = _dragS, from = _dragFrom; bool moved = _moved;
            bool[] tOk = _targetOk, tHand = _targetHand;
            CancelDrag();
            if (!moved || s < 0)
            {
                // 押しただけ: 挿してある〈呼び戻し〉なら、 戻す 1 枚を選ぶ窓を開く (それ以外は何もしない)
                if (s >= 0 && from >= 0 && sim.IsRecaller(s)) OpenRecall(s);
                // 手札の端子を押した: 手札の規則 Pick なら「次のターンも残す」の印を付け外し
                else if (s >= 0 && from < 0) sim.ToggleKeep(s);
                return;
            }
            // 対象を選ぶ (差し込み口の上 / 手札の抜け殻の上)
            if (port >= 0 && port < tOk.Length && tOk[port] && sim.SetTarget(sim.slot[port], s)) return;
            if (port < 0 && hand >= 0 && hand < tHand.Length && tHand[hand] && sim.SetTarget(hand, s)) return;
            if (port >= 0)
            {
                if (port == from) return;
                if (sim.Plug(port, s, from))
                {
                    if (from < 0 && sim.IsRecaller(s)) OpenRecall(s);      // 〈呼び戻し〉を挿した: 捨て札から戻す 1 枚を選ぶ窓 (選ばずに閉じれば最後に捨てた 1 枚)
                    return;
                }
                Game2DSfx.Play("ui_click");      // 落とせない口: 手札へ戻る
            }
            if (from >= 0) sim.Unplug(from);
        }

        private void CancelDrag()
        {
            if (_dragS < 0) return;
            _dragS = _dragFrom = _dropHover = _handHover = -1; _moved = false;
            _busyDrag = false; _releaseFrame = Time.frameCount;
            Show(_drag, false); _drawn = -1;
        }

        /// <summary>d の下の出目の行 (出目か差し込み口)。 −1 = なし。</summary>
        private int PortAt(Vector2 d)
        {
            int n = sim.DiceCount;
            for (int i = 0; i < n; i++) if (In(d, Ports, PortR(i, n)) || In(d, Ports, PDieR(i))) return i;
            return -1;
        }

        /// <summary>手札の並び: (場の添字, Hand の中の x)。 引いた端子は左から、 常設は右の端から (仕切りの右)。</summary>
        private List<KeyValuePair<int, int>> HandLayout(out int divider)
        {
            var l = new List<KeyValuePair<int, int>>(); divider = -1;
            int m = sim.board.Count, j = 0, q = 0;
            // 手札は 5 枚が基本 (2026-11-11)。 場に残る端子や〈配電盤〉で枠に収まらない時は、 間隔を詰めて並べる
            int nHand = 0, nPerm = 0;
            for (int s = 0; s < m; s++) { var d0 = CombatSystem.TerminalCards.Get(sim.board[s]); if (d0 != null && d0.permanent) nPerm++; else nHand++; }
            int avail = Hand.width - 2 * HandPad - nPerm * HandStep - (nPerm > 0 ? 4 : 0);
            int step = nHand > 1 ? Mathf.Min(HandStep, (avail - UsbBody) / (nHand - 1)) : HandStep;
            for (int s = 0; s < m; s++)
            {
                var def = CombatSystem.TerminalCards.Get(sim.board[s]);
                if (def != null && def.permanent) continue;
                l.Add(new KeyValuePair<int, int>(s, HandPad + j++ * step));
            }
            for (int s = 0; s < m; s++)
            {
                var def = CombatSystem.TerminalCards.Get(sim.board[s]);
                if (def == null || !def.permanent) continue;
                int x = Hand.width - HandPad - UsbBody - q++ * HandStep;
                l.Add(new KeyValuePair<int, int>(s, x)); divider = x - 3;
            }
            return l;
        }

        /// <summary>d の下の手札の USB (場の添字・使い切った抜け殻も含む)。 −1 = なし。</summary>
        private int HandAt(Vector2 d)
        {
            foreach (var e in HandLayout(out _))
                if (In(d, Hand, new RectInt(e.Value, HandY, UsbBody, UsbLong))) return e.Key;
            return -1;
        }

        // ── 端子の山札 (ADR-0014): 絵 ──

        private struct UsbCol { public Color32 body, edge, metal; }

        /// <summary>USB の色: 光る (解決後に発動) > 明るい (見込みで発動) > 普通 > 暗い (挿せる出目が無い・解決後に発動しなかった)。
        /// 縁: 選んでいる = 琥珀 / レア = 金 / 光る = 種類の色の明るい縁。 色と明度だけで状態を出す (拡大しない)。</summary>
        private static UsbCol UsbColors(CombatSystem.TerminalDef d, bool fits, bool fires, bool sel, bool lit)
        {
            var kc = d != null ? Game2DTerminalLook.KindColor(d.kind) : Dim;
            var c = new UsbCol();
            c.body = lit ? Game2DTerminalLook.Mix(kc, Game2DKit.Ivory, 0.2f)
                   : fires ? Game2DTerminalLook.Mix(kc, Off, 0.25f)
                   : !fits ? Game2DTerminalLook.Mix(kc, Off, 0.8f)
                   : Game2DTerminalLook.Mix(kc, Off, 0.55f);
            c.edge = sel ? Game2DKit.Amber : d != null && d.rare ? Game2DTerminalLook.Mix(Game2DKit.Gold, Game2DKit.Ivory, 0.3f)
                   : lit ? Game2DTerminalLook.Mix(kc, Game2DKit.Ivory, 0.6f) : Dark;
            c.metal = lit ? new Color32(0xc8, 0xc4, 0xb8, 255) : fits ? new Color32(0x8a, 0x88, 0x80, 255) : new Color32(0x4a, 0x48, 0x42, 255);
            return c;
        }

        /// <summary>USB メモリ 1 本 (差し込み部 + 本体 25×25・本体に 17×17 のアイコン (奇数どうしで中央に載る)、 無ければ種類の 1 字)。 horiz = 横向き (差し込み部が左・口に挿さった絵) / 縦向き (差し込み部が上・手札)。
        /// 印: 使い捨て = 本体の右下の切り欠き / 常設 = 本体の下の角の鋲 / レア = 金の縁。</summary>
        private static void DrawUsb(KitPix p, int x, int y, bool horiz, CombatSystem.TerminalDef d, Color32 body, Color32 edge, Color32 metal)
        {
            int bx, by;
            if (horiz) { p.Box(x, y + (UsbBody - UsbConnW) / 2, UsbConn, UsbConnW, Off, metal); bx = x + UsbConn; by = y; }
            else { p.Box(x + (UsbBody - UsbConnW) / 2, y, UsbConnW, UsbConn, Off, metal); bx = x; by = y + UsbConn; }
            p.Box(bx, by, UsbBody, UsbBody, body, edge);
            int b = UsbBody;
            if (d != null && d.disposable) { p.Rect(bx + b - 4, by + b - 4, 4, 4, Well); p.Rect(bx + b - 4, by + b - 4, 1, 3, edge); p.Rect(bx + b - 4, by + b - 4, 3, 1, edge); }
            if (d != null && d.permanent) { var rv = Game2DTerminalLook.Mix(body, Game2DKit.Ivory, 0.6f); p.Rect(bx + 2, by + b - 3, 1, 1, rv); p.Rect(bx + b - 3, by + b - 3, 1, 1, rv); }
            const int n = Game2DTerminalIcons.Size;
            int ix = bx + (b - n) / 2, iy = by + (b - n) / 2;
            var icon = d != null ? IconOf(d.id) : null;
            if (icon != null && icon.Length >= n * n)
            {
                // 2 色だけ (2026-11-11): 明 = 主役 / 中 = 主役の後ろの飾り。 主役の中の線は抜き (本体の色)。 暗い色は使わない
                Color32 back = Game2DTerminalLook.Mix(body, Game2DKit.Ivory, 0.35f), front = Game2DTerminalLook.Mix(body, Game2DKit.Ivory, 0.75f);
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        byte v = icon[j * n + i];
                        if (v != 0) p.Set(ix + i, iy + j, v == Game2DTerminalIcons.Back ? back : front);
                    }
            }
            else
            {
                string ch = d != null ? Game2DTerminalLook.KindShort(d.kind) : "?";
                p.Text(ch, bx + (b - Game2DJpFont.Width(ch)) / 2, by + (b - Game2DJpFont.Cell) / 2 + 1, Game2DTerminalLook.Mix(body, Game2DKit.Ivory, 0.7f));
            }
        }

        /// <summary>Ports: 出目ごとに 出目 → 配線 → 差し込み口。 挿した USB は横向きに挿さった絵で残る (2 個挿せる端子は両方の口に出る)。
        /// つかんでいる間は、 落とせる口を琥珀で明るく・対象に決められる口を白で・落とせない口を暗く。 発動の見込み (解決後は結果) を口の右のランプで出す。</summary>
        private void DrawPorts(Texture2D t, int w)
        {
            var p = Frame(w, Ports.height);
            bool res = sim.ShowingResult, dragging = _dragS >= 0 && _moved; var o = sim.preview; int m = sim.board.Count, n = sim.DiceCount;
            int[] dieSlot = res && o != null ? o.slotOfDie : sim.slot;
            for (int i = 0; i < n; i++)
            {
                int x0 = PortX(i), y0 = PortY(i);
                DrawDie(p, PDieR(i), sim.pd[i], false, !sim.rolled && sim.phase != Game2DBattleSim.P.PRoll);
                if (sim.IsRoleDie(i)) DrawRoleMark(p, i);
                if (sim.IsCatLocked(i)) DrawCatMark(p, i);
                int s = dieSlot != null && i < dieSlot.Length ? dieSlot[i] : -1;
                var d = s >= 0 && s < m ? CombatSystem.TerminalCards.Get(sim.board[s]) : null;
                bool used = d != null && o != null && i < o.slotOfDie.Length && o.slotOfDie[i] == s;      // 挿して発動に使われる出目
                bool fires = d != null && o != null && o.fired.Contains(s), lit = res && fires;
                var kc = d != null ? Game2DTerminalLook.KindColor(d.kind) : Game2DKit.Faint;
                p.Rect(x0 + 27, y0 + 11, 3, 2, used ? kc : Game2DKit.Faint);      // 配線
                bool ghost = dragging && _dragFrom == i;
                bool tOk = dragging && i < _targetOk.Length && _targetOk[i], dOk = dragging && i < _dropOk.Length && _dropOk[i];
                // 口の地: つかんでいる間は 対象に決められる = 白 / 落とせる = 琥珀 (真上なら明るく) / 落とせない = 暗い
                Color32 mouth = Game2DKit.Faint, fill = Well;
                if (dragging)
                {
                    var on = tOk ? ChosenEdge : Game2DKit.Amber;
                    mouth = tOk || dOk ? (_dropHover == i ? Game2DTerminalLook.Mix(on, Game2DKit.Ivory, 0.4f) : on) : Off;
                    fill = tOk || dOk ? Game2DTerminalLook.Mix(on, Well, 0.8f) : Dark;
                }
                if (d == null || ghost)
                {
                    p.Box(x0 + 30, y0, UsbLong + 2, UsbBody, fill, mouth);
                    p.Rect(x0 + 31, y0 + (UsbBody - UsbConnW) / 2, 2, UsbConnW, dragging && dOk ? mouth : Off);      // 差し込みの口
                }
                else
                {
                    var c = UsbColors(d, true, fires || (!res && used), _hoverPort == i && _hoverS == s, lit);
                    if (!used) c.body = Game2DTerminalLook.Mix(kc, Off, 0.8f);      // 挿したが発動に使われない (条件・充電・2 個そろわない)
                    if (dragging) c.edge = mouth;
                    DrawUsb(p, x0 + 31, y0, true, d, c.body, c.edge, c.metal);
                }
                // ランプ (見込みで発動 / 解決後に発動)
                int lx = x0 + 68, ly = y0 + 9;
                p.Box(lx, ly, 5, 5, fires && used ? kc : Off, fires && used ? Game2DTerminalLook.Mix(kc, Game2DKit.Ivory, 0.3f) : Game2DKit.Faint);
            }
            p.Apply(t);
        }

        /// <summary>挑戦カード〈猫の手〉(#5) の印: 猫が先に挿した出目 (このターン動かせない) を琥珀の枠で囲み、 左の余白に肉球を置く。
        /// 大きさは変えない (色と別の絵だけ)。</summary>
        private static readonly string[] CatPaw =
        {
            "#.#.#",
            ".....",
            ".###.",
            "#####",
            ".###.",
        };
        private static void DrawCatMark(KitPix p, int i)
        {
            var r = PDieR(i); var c = Game2DKit.Amber;
            p.Rect(r.x - 1, r.y - 1, r.width + 2, 1, c); p.Rect(r.x - 1, r.y + r.height, r.width + 2, 1, c);
            p.Rect(r.x - 1, r.y, 1, r.height, c); p.Rect(r.x + r.width, r.y, 1, r.height, c);
            int px = PortX(i) + 1, py = r.y + (r.height - CatPaw.Length) / 2;
            for (int j = 0; j < CatPaw.Length; j++)
                for (int k = 0; k < CatPaw[j].Length; k++)
                    if (CatPaw[j][k] == '#') p.Set(px + k, py + j, c);
        }

        /// <summary>Hand: 上 = 山/捨て(/外) の枚数 / 下 = 手札の USB (縦向き)。 ターンの頭に右から滑り込む (位置だけ・拡大しない)。
        /// 使い切った端子は抜け殻の枠だけ残す (並びが動かないように)。 2 個挿せる端子は残りの数を下に出す。 挿せる出目が無い端子は暗い。
        /// 対象を選ぶ印: 候補 = 灰白の縁と下に「候補」/ 決まった対象 = 白の縁と下に「対象」。
        /// 止まった端子 (ヴェスカ〈停滞する時間〉): いちばん暗い本体・縁と金具なし・本体を横切る青白の線と下に「停止」(つかめない)。</summary>
        private void DrawHand(Texture2D t)
        {
            var p = Frame(Hand);
            var dk = CombatSystem.CombatManager.Instance?.TerminalDeck;
            if (dk != null) p.Text("山" + dk.draw.Count + " 捨" + dk.discard.Count + (dk.exiled.Count > 0 ? " 外" + dk.exiled.Count : ""), HandPad, 3, Dim);
            var lay = HandLayout(out int divider);
            if (lay.Count == 0) p.Text("—", HandPad, HandY + 8, Game2DKit.Faint);
            if (divider >= 0) p.Rect(divider, HandY, 1, UsbLong, Off);
            bool res = sim.ShowingResult, op = sim.CanOperate, dragging = _dragS >= 0 && _moved;
            float now = Time.unscaledTime; int j = 0;
            foreach (var e in lay)
            {
                int s = e.Key, x = e.Value;
                var d = CombatSystem.TerminalCards.Get(sim.board[s]);
                bool perm = d != null && d.permanent;
                if (!perm)
                {
                    float k = Mathf.Clamp01((now - _dealT0 - 0.07f * j) / 0.22f); k = 1f - (1f - k) * (1f - k); j++;
                    x += Mathf.RoundToInt((1f - k) * Hand.width);
                }
                int rem = sim.Remaining(s) - (dragging && _dragFrom < 0 && _dragS == s ? 1 : 0);
                if (rem <= 0)
                {
                    // 抜け殻。 対象を取る端子の抜け殻は、 候補をつかんでいる間 落とせる所として白く
                    bool tgt = dragging && s < _targetHand.Length && _targetHand[s];
                    var edge = tgt ? (_handHover == s ? Game2DTerminalLook.Mix(ChosenEdge, Game2DKit.Ivory, 0.4f) : ChosenEdge) : Off;
                    p.Box(x, HandY + UsbConn, UsbBody, UsbBody, tgt ? Game2DTerminalLook.Mix(ChosenEdge, Well, 0.8f) : Well, edge);
                    continue;
                }
                bool stopped = sim.IsStopped(s);      // ヴェスカ〈停滞する時間〉: このターン挿せない
                bool fits = !stopped && (!op || res || sim.HandFits(s));
                var c = UsbColors(d, fits, false, _hoverPort < 0 && _hoverS == s, false);
                bool chosen = false, cand = op && sim.IsTargetCandidate(s, out chosen);
                if (cand && !(_hoverPort < 0 && _hoverS == s)) c.edge = chosen ? ChosenEdge : CandEdge;
                if (stopped)
                {
                    // 止まった端子: 挿せない端子よりさらに暗く、 縁と金具を消す (色と明度だけ・拡大縮小はしない)
                    var kc0 = d != null ? Game2DTerminalLook.KindColor(d.kind) : Dim;
                    c.body = Game2DTerminalLook.Mix(kc0, Off, 0.92f); c.metal = Dark;
                    if (!(_hoverPort < 0 && _hoverS == s)) c.edge = Off;
                }
                DrawUsb(p, x, HandY, false, d, c.body, c.edge, c.metal);
                if (stopped) p.Rect(x + 2, HandY + UsbConn + UsbBody / 2, UsbBody - 4, 1, StopMark);      // 本体を横切る 1 本の線 = 止まっている印
                bool kept = op && sim.IsKept(s);      // 手札の規則 Pick: 次のターンも残す印 (仮の印 = 本体の上の 2 ドットの帯・色だけ・拡大縮小はしない)
                if (kept) p.Rect(x + 1, HandY + UsbConn, UsbBody - 2, 2, KeepMark);
                string label = stopped ? "停止" : cand ? (chosen ? "対象" : "候補") : kept ? "残す" : d != null && d.capacity >= 2 ? "残" + rem : null;
                if (label != null) p.Text(label, x + (UsbBody - Game2DJpFont.Width(label)) / 2, HandY + UsbLong + 1, stopped ? StopMark : kept ? KeepMark : cand && chosen ? Ink : Dim);
            }
            p.Apply(t);
        }

        /// <summary>説明の帯 (USB にマウスを乗せた時だけ・つかんでいる間は出さない): 1 行目 = 名前 (種類の色) と印 / 続けて効果 /
        /// 対象 (〈写し取り〉など) / 誓約の封じ。 出すものが無ければ false。</summary>
        private bool DrawTip()
        {
            int m = sim.board.Count, f = _hoverS;
            if (_dragS >= 0 || f < 0 || f >= m || _tip == null) return false;
            var d = CombatSystem.TerminalCards.Get(sim.board[f]); if (d == null) return false;
            Ensure(ref _tt2, _tip, new RectInt(0, 0, TipW, TipH));
            int iw = TipW - 10;
            var run = GameManager.Instance?.Run;
            string head = d.id + "（" + Game2DTerminalLook.Marks(d, run) + "）";
            var headLines = Game2DJpFont.Lines(head, iw);
            var body = new List<string>(Game2DJpFont.Lines(d.description, iw));
            // 対象 (〈写し取り〉など) と誓約の封じの行
            var extra = new List<string>();
            if (sim.IsStopped(f))
                extra.AddRange(Game2DJpFont.Lines(d.permanent ? "止まっている (このターンは挿せない)"
                                                              : "止まっている (このターンは挿せない・次のターンまで手札に残る)", iw));
            // 手札の規則 Pick: 残す印の状態
            if (sim.IsKept(f)) extra.AddRange(Game2DJpFont.Lines("次のターンも手札に残す (押して外す)", iw));
            else if (sim.CanKeep(f) && !sim.ShowingResult && sim.Remaining(f) > 0) extra.AddRange(Game2DJpFont.Lines("押すと、 挿さなかった時に次のターンも手札に残す", iw));
            string tgt = TargetLine(f);
            if (tgt != null) extra.AddRange(Game2DJpFont.Lines(tgt, iw));
            for (int q = 0; q < m && !sim.ShowingResult; q++)
                if (q != f && sim.IsPicker(q) && sim.TargetOf(q) == f) extra.AddRange(Game2DJpFont.Lines("〈" + sim.board[q] + "〉の対象", iw));
            if (CombatSystem.TerminalCards.AxisSealedByRun(d, run)) extra.AddRange(Game2DJpFont.Lines("誓約で封じられている (軸の資源は増えない)", iw));
            // 収まらない時は効果の末尾を削って、 対象・封じの行を残す
            int over = headLines.Count + body.Count + extra.Count - TipLinesMax;
            if (over > 0) body.RemoveRange(Mathf.Max(0, body.Count - over), Mathf.Min(body.Count, over));
            var lines = new List<string>(headLines); lines.AddRange(body);
            int extraFrom = lines.Count; lines.AddRange(extra);
            int n = Mathf.Min(TipLinesMax, lines.Count), h = 6 + n * Game2DJpFont.LineH + 3;
            var p = new KitPix(TipW, TipH);      // 使わない下の部分は透明のまま
            var kc = Game2DTerminalLook.KindColor(d.kind);
            p.Box(0, 0, TipW, h, Well, kc);
            for (int i = 0; i < n; i++) Game2DJpFont.Draw(p, lines[i], 5, 4 + i * Game2DJpFont.LineH, i < headLines.Count ? Dim : i >= extraFrom ? Chg : Ink);
            Game2DJpFont.Draw(p, d.id, 5, 4, kc);      // 名前だけ種類の色で上書き
            p.Apply(_tt2);
            int x0, y0;
            int rightLimit = Right.x - 2 - TipW;      // 予告の枠 (Right) の左に収める
            if (_hoverPort >= 0) { x0 = Mathf.Min(Ports.x + PortsW(sim.DiceCount) + 2, rightLimit); y0 = Mathf.Clamp(Ports.y + PortY(_hoverPort), TipMinY, TipMaxY - h); }
            else { x0 = rightLimit; y0 = Mathf.Max(TipMinY, Hand.y - h - 2); }
            _tip.anchoredPosition = new Vector2(x0, -y0);
            _tip.SetAsLastSibling();
            return true;
        }

        /// <summary>対象を取る端子の「対象: 〈◯◯〉」の行 (対象を取る端子でなければ null)。 解決の後は結果の対象を出す。</summary>
        private string TargetLine(int f)
        {
            string id = sim.board[f];
            // 〈呼び戻し〉(手札の持ち越しの規則): 捨て札から戻す 1 枚
            if (CombatSystem.TerminalCards.PicksDiscard(id))
            {
                if (sim.ShowingResult) return null;
                var disc = sim.Discard;
                if (disc == null || disc.Count == 0) return "捨て札が空 (戻す端子が無い)";
                if (!sim.IsRecaller(f)) return "出目に挿すと、 捨て札から戻す 1 枚を選べる";
                int r = sim.RecallOf(f);
                return r >= 0 ? "戻す: 〈" + disc[r] + "〉 (押して選び直す)" : null;
            }
            if (!CombatSystem.TerminalCards.PicksTarget(id)) return null;
            int t = -1;
            if (sim.ShowingResult)
            {
                var o = sim.lastOutcome;
                if (o == null || !o.fired.Contains(f)) return null;
                t = id == CombatSystem.TerminalIds.Copier ? o.copyTarget : id == CombatSystem.TerminalIds.BurnOut ? o.burnTarget : o.retainTarget;
            }
            else
            {
                if (!sim.IsPicker(f))
                    return id == CombatSystem.TerminalIds.BurnOut      // 〈焼き切り〉(2026-11-14): 外す端子が無ければ挿しても不発
                        ? "出目に挿すと、 挿していない手札の端子を外す対象に選べる (外す端子が無いと発動しない)"
                        : "出目に挿すと、 挿していない手札の端子を対象に選べる";
                t = sim.TargetOf(f);
            }
            return t >= 0 && t < sim.board.Count ? "対象: 〈" + sim.board[t] + "〉" : "対象: なし (空けた端子が無い)";
        }

        /// <summary>Ctl: 上の段 = 振り直す・おまかせ / 下の段 = 道具・逃げる・決める。</summary>
        private void DrawCtl(Texture2D t)
        {
            var p = Frame(Ctl);
            string rl = sim.canReroll ? "振り直す -" + sim.rerollCost : "振り直す";
            if (Game2DJpFont.Width(rl) > CReroll.width - 2) rl = "振直 -" + sim.rerollCost;
            SmallBtn(p, CReroll, rl, sim.CanRerollNow, Chg);
            SmallBtn(p, CAuto, "おまかせ", sim.CanOperate, Ink);
            if (!sim.CanFlee) _fleeArmed = false;
            SmallBtn(p, CItem, "道具", AnyUsable(), Chg);
            SmallBtn(p, CFlee, _fleeArmed ? "本当に?" : "逃げる", sim.CanFlee, _fleeArmed ? Atk : Ink);
            DrawGo(p, CGo, true);
            p.Apply(t);
        }

        private bool _fleeArmed;
        private Game2DModal _modal;

        private void SmallBtn(KitPix p, RectInt r, string label, bool on, Color32 col)
        {
            if (Game2DKit.Tab != null) p.NineSlice(Game2DKit.Tab, 5, Well, r.x, r.y, r.width, r.height);
            else p.Box(r.x, r.y, r.width, r.height, Off, Dark);
            p.TextC(label, r, on ? col : Dim);
        }

        // ── 道具 (消耗品・issue #11・gui-needed.md #31) ──
        //   [道具] = 所持品の消耗品の窓。 使えるのはターンの判断 (振り直し・配線) で止まっている間だけ (Game2DBattleSim.CanUseItems)。
        //   使うのは本体の Consumables.Use (BOT と同じ関数)。 使えない品 (HP が満ちている・戦闘の外でだけ使える・死翔で封じられた 等) は
        //   Consumables.BlockReason で暗く出し、 押すと理由を足元に出す。 囮罠は航行の道具なので並べない。
        //   〈シュヴァリエのレイピア〉(パッシブ・消費しない) も同じ窓の先頭で切り替える (Consumables.TryUseRapier・BOT と同じ関数)。

        /// <summary>窓の 1 行: 消耗品 (id・同じ品は 1 行) か、 レイピアの切り替え。</summary>
        private struct ItemRow { public string id; public bool rapier; }

        /// <summary>戦闘の窓に並べる品 (レイピア → 消耗品の持っている順・同じ品は 1 行・囮罠は除く)。</summary>
        private static List<ItemRow> CombatItems()
        {
            var l = new List<ItemRow>(); var run = GameManager.Instance?.Run;
            if (run == null) return l;
            if (run.ownedPassiveItems != null && run.ownedPassiveItems.Contains(ItemIds.ChevalierRapier)) l.Add(new ItemRow { id = ItemIds.ChevalierRapier, rapier = true });
            if (run.ownedConsumables == null) return l;
            var seen = new HashSet<string>();
            foreach (var id in run.ownedConsumables)
                if (seen.Add(id) && ItemIds.ConsFamilyOf(id) != ItemIds.ConsLureFamily) l.Add(new ItemRow { id = id });
            return l;
        }

        /// <summary>今その行を使えるか (使えなければ理由)。 段 (判断の間か) は呼び手が見る。</summary>
        private static string RowBlock(RunState run, ItemRow r)
            => r.rapier ? (Consumables.CanUseRapier(run) ? null : "今は切り替えられない") : Consumables.BlockReason(run, r.id);

        /// <summary>[道具] を明るく出すか: 判断の間で、 使える品が 1 つでもある。</summary>
        private bool AnyUsable()
        {
            if (!sim.CanUseItems) return false;
            var run = GameManager.Instance?.Run; if (run == null) return false;
            foreach (var r in CombatItems()) if (RowBlock(run, r) == null) return true;
            return false;
        }

        // ── 〈呼び戻し〉の窓 (捨て札から 1 枚・仮の見た目 = 道具の窓と同じ部品) ──
        //   開く: 〈呼び戻し〉を出目に挿した時 / 挿してある〈呼び戻し〉を押した (動かさずに放した) 時。 捨て札が空なら開かない。
        //   最初から「今戻る 1 枚」(選んでいなければ最後に捨てた 1 枚) を選んでおき、 その行に「戻す」の印を付ける。
        //   [これを戻す] = 選んだ行に決める / [閉じる] = 今の 1 枚のまま。 決めた値は sim.target[〈呼び戻し〉の場の位置] = 捨て札の添字。

        private void OpenRecall(int s)
        {
            if (!sim.CanOperate || !sim.IsRecaller(s)) return;
            if (_modal == null) _modal = FindObjectOfType<Game2DModal>();
            if (_modal == null) return;
            _modal.Open(RecallDef(s));
        }

        private ScreenDef RecallDef(int s)
        {
            var disc = sim.Discard ?? new List<string>();
            int cur = sim.RecallOf(s);
            var d = new ScreenDef { id = "battle_recall", jp = true, title = "〈" + sim.board[s] + "〉: 戻す 1 枚", w = 460, cols = 2,
                sub = "捨て札 " + disc.Count + " 枚から選ぶ (次のターンの手札に入る)", sel0 = Mathf.Max(0, cur) };
            for (int i = 0; i < disc.Count; i++)
            {
                var td = CombatSystem.TerminalCards.Get(disc[i]);
                d.Items.Add(new ModalItem { name = disc[i], value = i == cur ? "戻す" : "", desc = td?.description });
            }
            d.buttons.Add(new ModalBtn { label = "これを戻す", action = "pick", primary = true });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.footer = "選ばずに閉じると、 最後に捨てた 1 枚が戻る";
            d.onAction = (a, sel) =>
            {
                if (a == "close") { _modal.Close(); return; }
                if (a != "pick") return;
                if (!sim.SetRecall(s, sel)) { Game2DSfx.Play("ui_click"); _modal.ShowToast("今は選べない"); return; }
                _drawn = -1;
                _modal.Close();
            };
            return d;
        }

        private int _itemPage;
        private const int ItemsPerPage = 10;

        /// <summary>消耗品を選んで使う窓 (判断の間だけ)。 使っても窓は閉じずに中身を写し直す (続けて使える・HP とシールドは題の下に出す)。</summary>
        private void OpenItems()
        {
            if (!sim.CanUseItems) { Game2DSfx.Play("ui_click"); return; }
            if (_modal == null) _modal = FindObjectOfType<Game2DModal>();
            var run = GameManager.Instance?.Run;
            if (_modal == null || run == null || CombatItems().Count == 0) { Game2DSfx.Play("ui_click"); return; }
            _itemPage = 0;
            _modal.Open(ItemsDef());
        }

        private ScreenDef ItemsDef()
        {
            var run = GameManager.Instance?.Run;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            var all = CombatItems();
            int pages = Mathf.Max(1, (all.Count + ItemsPerPage - 1) / ItemsPerPage); _itemPage = Mathf.Clamp(_itemPage, 0, pages - 1);
            int first = _itemPage * ItemsPerPage, cnt = Mathf.Min(ItemsPerPage, all.Count - first);
            int shield = ctx != null ? ctx.consShield : 0;
            var d = new ScreenDef { id = "battle_items", jp = true, title = "道具", w = 460, cols = 2,
                sub = "HP " + sim.playerHp + "/" + sim.playerMax + "  シールド " + shield };
            var rows = new List<ItemRow>();
            for (int i = 0; i < cnt; i++)
            {
                var r = all[first + i]; rows.Add(r);
                var data = ItemSystem.ItemDatabase.Instance?.GetItem(r.id);
                string name = ItemSystem.ItemDatabase.DisplayNameOf(data, r.id);
                string block = run != null ? RowBlock(run, r) : "使えない";
                string value;
                if (r.rapier) value = Consumables.RapierContreOn() ? "構え中" : "";
                else
                {
                    int n = 0; foreach (var c in run.ownedConsumables) if (c == r.id) n++;
                    value = "x" + n;
                }
                string desc = data?.description;
                if (!r.rapier && r.id == ItemIds.OldLetter)      // 1 枚で数回使える (使い切った時だけ所持から減る)
                    desc = (desc ?? "") + "\n(今の 1 枚はあと " + Mathf.Max(1, ItemIds.OldLetterUses - run.oldLetterUsesSpent) + " 回)";
                if (block != null) desc = string.IsNullOrEmpty(desc) ? "(" + block + ")" : desc + "\n(" + block + ")";
                d.Items.Add(new ModalItem { icon = r.id, rarity = data != null ? data.rarity : (ItemSystem.ItemRarity?)null, name = name, value = value, desc = desc, disabled = block != null });
            }
            if (cnt <= 0) d.body = "使える品を持っていない。";
            d.buttons.Add(new ModalBtn { label = "使う", action = "use", primary = true });
            if (pages > 1) d.buttons.Add(new ModalBtn { label = "次の頁 " + (_itemPage + 1) + "/" + pages, action = "page" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.footer = "このターンの判断の間に使う (敵の攻撃より先に効く)";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "close": _modal.Close(); return;
                    case "page": _itemPage = (_itemPage + 1) % pages; break;
                    case "use":
                    {
                        if (sel < 0 || sel >= rows.Count) return;
                        if (!sim.CanUseItems) { _modal.ShowToast("今は使えない"); return; }
                        var r = rows[sel]; var rs = GameManager.Instance?.Run;
                        string why = rs != null ? RowBlock(rs, r) : "使えない";
                        if (why != null) { Game2DSfx.Play("ui_click"); _modal.ShowToast(why); return; }
                        bool ok = r.rapier ? sim.UseRapier() : sim.UseItem(r.id);
                        if (!ok) { Game2DSfx.Play("ui_click"); _modal.ShowToast("今は使えない"); return; }
                        Game2DSfx.Play("ui_confirm"); _modal.ShowToast(r.rapier ? (Consumables.RapierContreOn() ? "構えた" : "構えを解いた") : "使った");
                        _drawn = -1;
                        if (CombatItems().Count == 0) { _modal.Close(); return; }
                        break;
                    }
                    default: return;
                }
                _modal.Replace(ItemsDef());
            };
            return d;
        }

        private void DrawRight(Texture2D t)
        {
            var p = Frame(Right);
            bool live = sim != null && Game2DBattleSim.Active; bool known = live && sim.phase != Game2DBattleSim.P.ERoll;
            int en = live ? sim.enemyDiceCount : 3;
            for (int i = 0; i < en; i++) DrawDie(p, new RectInt(14 + i * 20, 16, 17, 17), live ? sim.ed[i] : i + 3, true);
            if (known && sim.down) p.Text("ダウン:攻撃なし", 14, 36, MarkHeavyHud);      // ボスのダウン (2026-11-13): 止まるのは攻撃だけ (パッシブは動く)
            else p.Text(known ? "攻撃 " + sim.eatk : "攻撃 ??", 14, 36, known ? Atk : Dim);
            if (known) p.Text("出目 " + sim.EnemyDiceSum, 14, 49, Dim);
            if (known && sim.edef > 0) p.Text("防御 " + sim.edef, 14, 62, Ink);      // 敵の防御 (構え・割れる/積み上がる装甲を反映した今の値)
            // 予告 (攻撃・大技・構え) と毒・出血は敵の周りに出す (Game2DBattleFx.DrawMarks・2026-11-11)。 ここは次の構え・大技の前触れだけ
            string next = known ? (sim.guardIn == 1 && sim.heavyIn == 1 ? "次: 構え・大技" : sim.guardIn == 1 ? "次: 構え" : sim.heavyIn == 1 ? "次: 大技" : null) : null;
            // ボスのダウン (2026-11-13): 後に続くダウンが決まっていれば「次: ダウン」(大技・構えの前触れより先に出す)
            if (known && sim.downPending > 0) next = "次: ダウン";
            if (next != null) p.Text(next, 14, 75, sim.heavyIn == 1 || (known && sim.downPending > 0) ? MarkHeavyHud : Dim);
            if (live) p.Text("ターン " + sim.turn + "  段階 " + sim.Stage, 14, 90, Dim);
            // ボスのダウン: ダウン中は受けるダメージの倍率、 そうでなければ次の目盛りまでの残り (HP がこの点数だけ減れば割れる)
            if (known && sim.down) p.Text("被ダメ×" + CombatSystem.BossDown.TakenMul.ToString("0.0"), 14, 103, MarkHeavyHud);
            else if (known && sim.downNotchHp >= 0 && fx != null) p.Text("目盛りまで " + Mathf.Max(0, fx.EnemyHp - sim.downNotchHp), 14, 103, Ink);
            // 不安定化 (2026-11-13・GAME.md §5-12): 激化の段が上がるたびに不安定 +1 (ボス戦では溜まらない・この戦闘で溜まった分)。 段の表示 (上の行) のすぐ下
            else if (live && GameLoop.GameManager.Instance != null && GameLoop.GameManager.Instance.InstabilityFromEscalationThisCombat > 0)
                p.Text("不安定 +" + GameLoop.GameManager.Instance.InstabilityFromEscalationThisCombat, 14, 103, MarkHeavyHud);
            p.Apply(t);
        }

        private static readonly Color32 MarkHeavyHud = new Color32(0xff, 0xb3, 0x47, 255);

        private void DrawLamp(KitPix p, int x, int y, int size, bool lit, Color32 col)
        {
            var s = lit ? Game2DKit.LampOn : Game2DKit.LampOff;
            if (s != null) p.Sprite(s, x, y, size, size);
            else p.Box(x, y, size, size, lit ? col : Off, Dark);
        }

        private void DrawTop(Texture2D t)
        {
            var p = Frame(Top); bool live = sim != null && Game2DBattleSim.Active;
            int pips = live ? Mathf.Min(10, sim.charge / 3) : 4;
            p.Text("充電", 14, 13, Chg);
            for (int i = 0; i < 10; i++) DrawLamp(p, 38 + i * 10, 14, 10, i < pips, Chg);
            string esc = live ? "激化 " + (sim.NextStageIn >= 0 ? "あと" + sim.NextStageIn : "最大") : "激化";
            p.Text(esc, 146, 13, Dim);
            if (live) for (int i = 0; i < 5; i++) DrawLamp(p, 214 + i * 10, 14, 10, i < sim.Stage, Atk);
            int cur = fx != null ? fx.EnemyHp : 56, max = fx != null ? fx.EnemyMax : 80;
            p.Text("敵", 14, 29, Ink); p.GaugeBar(Game2DKit.Gauge, 30, 31, 192, 10, Mathf.Clamp01(cur / (float)Mathf.Max(1, max)), Hp);
            // ボスのダウンの目盛り (2026-11-13・仮の印): 棒に 1 ドットの刻み。 まだ割っていない目盛りは明るく、 割った目盛りは暗く (状態は色で表す)
            if (live && sim.downNotches != null)
                for (int i = 0; i < sim.downNotches.Length; i++)
                {
                    int nx = 30 + Mathf.Clamp(Mathf.RoundToInt(192 * sim.downNotches[i]), 1, 190);
                    p.Rect(nx, 30, 1, 12, i < sim.downBroken ? Dim : MarkHeavyHud);
                }
            p.Text(cur + "/" + max, 228, 29, Ink);
            p.Apply(t);
        }
    }
}

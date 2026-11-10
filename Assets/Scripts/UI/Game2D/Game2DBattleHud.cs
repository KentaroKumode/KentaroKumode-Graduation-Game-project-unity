using System.Collections.Generic;
using GameLoop;
using UnityEngine;
using UnityEngine.UI;
using UI.Game2D.Battle;

namespace UI.Game2D
{
    /// <summary>
    /// 戦闘の GUI (<b>仮置き</b>・2026-10-24)。 枠・ダイスの面・ボタン・ランプ・ゲージ・丸ボタンは PixelLab (参考画像の切り抜き + パレット抽出) で作った絵
    /// (Assets/gui2d/battle_ui/・<see cref="Game2DKitAssets"/>)。 枠は 1 枚の絵を 9 分割して 3 つのパネルの大きさに合わせる。 文字は 3×5 のドット字 (英数字だけ)。
    /// 左 = 自分のダイス 3 個と配線 (攻撃・防御・充電・特殊) と振り直しと決定ボタン / 右 = 敵の出目と攻撃値 / 上の帯 = 充電・エスカレーション・敵 HP。
    /// 状態は <see cref="Game2DBattleSim"/> (実戦闘の流れ) から読む。 クリック: ダイスか端子 = 配線を切り替え / REROLL = 充電を払って振り直す / 下のボタン = GO! → NEXT。
    /// 航行用の GUI が引っ込むのと入れ替わりに、 左・右・上からばねで入ってくる (<see cref="Drive"/>)。 座標は 480×270 の格子 (左上が原点)。
    /// <para>端子の山札 (ADR-0014) で戦っている時は、 左のパネルの代わりに Board (出目・山/捨て・USB メモリの形の端子) と Ctl (操作) を出し、
    /// 端子にマウスを乗せた時・押して選んだ時だけ Board の右に説明の帯 (Tip) を出す。 字は常時出さない。
    /// 出目を押す = 挿し先を順に変える (挿せない端子は飛ばす・右クリックで逆) / 端子を押す = 説明 / おまかせ・決める。 発動の見込みは
    /// <see cref="CombatSystem.TerminalCards.Resolve"/> を状態の写しで呼んだ値 (<see cref="Game2DBattleSim.preview"/>)。 攻撃・防御の数字は資源バーとステータスに任せる。</para>
    /// </summary>
    public class Game2DBattleHud : MonoBehaviour
    {
        // 配置 (dot)
        // 2026-11-11: 字を既定のフォント (高さ 12) にしたので、 左右の枠を広げ、 上の帯を高くし、 左右は帯の下から始める
        private static readonly RectInt Left = new RectInt(2, 60, 108, 208), Right = new RectInt(370, 60, 108, 120), Top = new RectInt(96, 2, 288, 56);
        private const int FrameB = 12;
        // 自分のステータス (会心率・攻撃・防御): 右のパネルの下。 右と同じばねで横から入る (2026-11-05)
        private static readonly RectInt Stat = new RectInt(370, 184, 108, 68);
        // 左パネルの中 (パネルの左上が原点): 枠の内側は x 14〜72
        private static readonly RectInt Btn = new RectInt(15, 170, 80, 26), RerollR = new RectInt(15, 134, 80, 16),
            ItemR = new RectInt(15, 152, 39, 15), FleeR = new RectInt(56, 152, 39, 15);      // 消耗品 / 逃げる (配線の判断の間だけ)
        private static RectInt DieR(int i) => new RectInt(15, 18 + i * 24, 18, 18);
        private static RectInt TermR(int i) => new RectInt(37, 18 + i * 24, 58, 18);

        // ── 端子の山札 (ADR-0014・2026-11-11) の配置 ──
        //   山札で戦っている時だけ、 左のパネルの代わりに 2 枚を出す (左のパネルは隠す)。
        //   Board = 出目 3 個・山/捨ての枚数・場の端子 (USB メモリの形・1 段 3 本、 7 本以上は 1 段 4 本・最大 2 段)。 幅は元の左パネルと同じ 108。
        //           高さは中身ぶん (1 段 = 72・2 段 = 112)。 字は常時出さない。
        //   Ctl   = 振り直す・おまかせ・道具・逃げる・決める。 Board が 2 段の時の真下に固定 (段数でボタンが動かないように)。
        //   Tip   = 端子の名前・印・効果。 マウスを乗せた時・押して選んだ時だけ、 Board の右 (x 112〜) に端子の高さに合わせて出す。
        private static readonly RectInt Board = new RectInt(2, 60, 108, 112), Ctl = new RectInt(2, 174, 108, 52), Tip = new RectInt(112, 60, 200, 61);
        private const int BPad = 8, BInW = 92, BStickY = 30, BStickH = 36, BStickStep = 40, BConnH = 12, TipMaxY = 214, TipLines = 4;
        private static RectInt BDieR(int i) => new RectInt(BPad + i * 20, 6, 18, 18);
        private static int PerRow(int m) => m <= 6 ? 3 : 4;
        private static int StickW(int m) => PerRow(m) == 3 ? 28 : 22;
        private static int BoardH(int m) => BStickY + (m > PerRow(m) ? 2 : 1) * BStickStep + 2;
        /// <summary>端子 k (場の添字) の USB メモリ 1 本の枠 (差し込み口 + 本体)。 2 段を超える分は描かない (−1 幅)。</summary>
        private static RectInt StickR(int k, int m)
        {
            int per = PerRow(m), w = StickW(m), gap = per > 1 ? (BInW - per * w) / (per - 1) : 0, row = k / per, col = k % per;
            if (row > 1) return new RectInt(0, 0, -1, -1);
            return new RectInt(BPad + col * (w + gap), BStickY + row * BStickStep, w, BStickH);
        }
        private static readonly RectInt CReroll = new RectInt(5, 5, 56, 20), CAuto = new RectInt(63, 5, 40, 20),
            CItem = new RectInt(5, 27, 24, 20), CFlee = new RectInt(31, 27, 36, 20), CGo = new RectInt(69, 27, 34, 20);
        private RectTransform _board, _ctl, _tip; private Texture2D _tb, _tc, _tt2;
        /// <summary>マウスが乗っている端子 (−1 = なし)。 乗っている間はその端子の説明を出す。</summary>
        private int _hover = -1;

        /// <summary>端子のアイコン (16×16・行優先・左上から・0 = 透明 / 1 = 暗 / 2 = 中 / 3 = 明)。 無ければ null。
        /// 仮: アイコンの担当 (Game2DTerminalIcons.Get) がそろうまで null を返す ── 差し替えはここ 1 か所。</summary>
        private static byte[] IconOf(string id) => null;

        public Game2DBattleSim sim;
        public Game2DBattleFx fx;
        public Game2DScreen screen;
        [Tooltip("端子のボタン (攻撃・防御・充電) と決定ボタン。 枠・ダイスなどの共通の絵は Game2DKitAssets")]
        public Texture2D btnAtkTex, btnBlkTex, btnChgTex, btnGoTex, btnPlainTex;

        [SerializeField] private RectTransform _left, _right, _top;
        private RectTransform _stat;
        private Texture2D _tl, _tr, _tt, _ts;
        private int _drawn = -1; private float _slide;
        private KitSrc _bAtk, _bBlk, _bChg, _bGo, _bPlain; private bool _srcInit;

        private static readonly Color32 Ink = Game2DKit.Ink, Dim = Game2DKit.Dim, Dark = Game2DKit.Dark, Off = Game2DKit.Off, Well = Game2DKit.Well,
            Atk = Game2DKit.Red, Blk = Game2DKit.Blue, Chg = Game2DKit.Gold, Spc = Game2DKit.Amber, Hp = new Color32(0xd1, 0x50, 0x3c, 255);
        private static readonly Color32[] TermCol = { Atk, Blk, Chg, Spc };
        private static readonly string[] TermName = { "攻撃", "防御", "充電", "特殊" };

        /// <summary>見た目を作る (builder から 1 度呼ぶ)。 親は 480×270 の格子の枠。</summary>
        public void Build()
        {
            _left = Make("left", Left, out _tl); _right = Make("right", Right, out _tr); _top = Make("top", Top, out _tt);
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
            if (_tl != null) Destroy(_tl); if (_tr != null) Destroy(_tr); if (_tt != null) Destroy(_tt); if (_ts != null) Destroy(_ts);
            if (_tb != null) Destroy(_tb); if (_tc != null) Destroy(_tc); if (_tt2 != null) Destroy(_tt2);
        }

        /// <summary>組み上げ済みのシーンに無いパネルを作る (ステータス・端子の山札の 2 枚。 実行時に作り、 シーンには保存しない)。</summary>
        private void EnsurePanels()
        {
            if (_stat == null) _stat = Make("stat", Stat, out _ts);
            if (_board == null) _board = Make("board", Board, out _tb);
            if (_ctl == null) _ctl = Make("ctl", Ctl, out _tc);
            if (_tip == null) _tip = Make("tip", Tip, out _tt2);
        }

        private Game2DSpring _spL, _spR, _spT; private bool _spInit;
        /// <summary>入り・出の途中を含めて、 どれかが見えている。</summary>
        public bool Visible => !_spInit || !(_spL.tgt < 0.5f && _spR.tgt < 0.5f && _spT.tgt < 0.5f && _spL.x < 0.005f && _spR.x < 0.005f && _spT.x < 0.005f && _spL.Settled && _spR.Settled && _spT.Settled);

        /// <summary>戦闘の GUI をばねで出し入れする (毎フレーム)。 入る順は 左 → 上 → 右 で、 少しずつずらす。 行き過ぎは 3 dot 以内。</summary>
        public void Drive(bool on, float dt)
        {
            if (_left == null) return;
            EnsurePanels();
            if (!_spInit) { _spL.Set(0); _spR.Set(0); _spT.Set(0); _spInit = true; }
            float t = on ? 1f : 0f;
            _spL.Go(t, on ? 0.03f : 0f); _spT.Go(t, on ? 0.07f : 0.03f); _spR.Go(t, on ? 0.12f : 0.06f);
            _spL.Step(dt); _spT.Step(dt); _spR.Step(dt);
            _slide = _spL.x;
            _left.anchoredPosition = new Vector2(Left.x - Mathf.Round((1f - _spL.Value(120f)) * 120f), -Left.y);
            _right.anchoredPosition = new Vector2(Right.x + Mathf.Round((1f - _spR.Value(120f)) * 120f), -Right.y);
            _top.anchoredPosition = new Vector2(Top.x, -Top.y + Mathf.Round((1f - _spT.Value(62f)) * 62f));
            _stat.anchoredPosition = new Vector2(Stat.x + Mathf.Round((1f - _spR.Value(120f)) * 120f), -Stat.y);
            _board.anchoredPosition = new Vector2(Board.x - Mathf.Round((1f - _spL.Value(120f)) * 120f), -Board.y);
            if (_spL.x < 0.95f) Show(_tip, false);      // 説明の帯は、 パネルが収まっている間だけ
            _ctl.anchoredPosition = new Vector2(Ctl.x - Mathf.Round((1f - _spL.Value(120f)) * 120f), -Ctl.y);
        }

        private void Update()
        {
            if (_left == null || sim == null || !Game2DBattleSim.Active) return;
            // 配線を決める間だけ読み直す (その後は本判定に取り込まれて二重に数えるので、 最後の値を出し続ける)
            if (sim.CanOperate && CombatSystem.CombatManager.Instance != null)
            { _hs = CombatSystem.CombatManager.Instance.PreviewHudStats(); _hsKnown = true; }
            bool deck = sim.DeckMode, ptr = _slide > 0.95f && !Game2DModal.Active && screen != null;
            Vector2 d = default; bool hasDot = ptr && Game2DPointer.TryDot(screen, out d);
            // 端子の行にマウスが乗ったら、 その端子の説明を出す
            int hv = deck && hasDot ? RowAt(d) : -1;
            if (hv != _hover) { _hover = hv; _drawn = -1; }
            int ehp = fx != null ? fx.EnemyHp : 0, ver = sim.version * 31 + ehp * 7 + sim.charge
                + (_hsKnown ? Mathf.RoundToInt(_hs.critRate * 1000f) * 131 + _hs.attack * 17 + _hs.outgoingPct * 7 + _hs.defense * 3 + _hs.pen : 0)
                + (deck ? 977 + DeckCountsHash() : 0);
            if (ver != _drawn) { _drawn = ver; Redraw(); }
            if (!hasDot) return;
            if (Input.GetMouseButtonDown(0)) Click(d, 1);
            else if (deck && Input.GetMouseButtonDown(1)) Click(d, -1);      // 右クリック = 出目の行き先を逆に回す
        }

        private bool In(Vector2 d, RectInt panel, RectInt r) => d.x >= panel.x + r.x && d.x < panel.x + r.x + r.width && d.y >= panel.y + r.y && d.y < panel.y + r.y + r.height;

        /// <summary>dir = 1 は左クリック・−1 は右クリック (山札の時の出目だけ)。</summary>
        private void Click(Vector2 d, int dir)
        {
            if (sim.DeckMode) { ClickDeck(d, dir); return; }
            if (dir < 0) return;
            for (int i = 0; i < 3; i++) if (In(d, Left, DieR(i)) || In(d, Left, TermR(i))) { sim.CycleWire(i); return; }
            if (In(d, Left, RerollR)) { sim.Reroll(); return; }
            if (In(d, Left, ItemR)) { OpenItems(); return; }
            if (In(d, Left, FleeR)) { PressFlee(); return; }
            if (In(d, Left, Btn)) { if (sim.ButtonEnabled) sim.Button(); }
        }

        /// <summary>逃げるは 2 度押しで確定 (本当に? → 押すとこのターンは攻撃せずに離脱)。</summary>
        private void PressFlee()
        {
            if (_fleeArmed) { _fleeArmed = false; if (!sim.Flee()) Game2DSfx.Play("ui_click"); }
            else if (sim.CanFlee) { _fleeArmed = true; _drawn = -1; }
        }

        // ── 端子の山札の操作 ──
        //   出目を押す = 行き先が 場の端子 (上から) → 挿さない の順に変わる (挿せない端子は飛ばす・右クリックで逆)。
        //   端子の行を押す = その端子の説明を出す (もう一度押すと外す)。

        private void ClickDeck(Vector2 d, int dir)
        {
            for (int i = 0; i < 3; i++) if (In(d, Board, BDieR(i))) { sim.CycleWire(i, dir); return; }
            if (dir < 0) return;
            int r = RowAt(d);
            if (r >= 0) { sim.Focus(r == sim.focusSlot ? -1 : r); Game2DSfx.Play("ui_click"); return; }
            if (In(d, Ctl, CReroll)) { sim.Reroll(); return; }
            if (In(d, Ctl, CAuto)) { sim.AutoWire(); return; }
            if (In(d, Ctl, CItem)) { OpenItems(); return; }
            if (In(d, Ctl, CFlee)) { PressFlee(); return; }
            if (In(d, Ctl, CGo)) { if (sim.ButtonEnabled) sim.Button(); }
        }

        private int RowAt(Vector2 d)
        {
            int m = sim.board.Count;
            for (int k = 0; k < m; k++) { var r = StickR(k, m); if (r.width > 0 && In(d, Board, r)) return k; }
            return -1;
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
            _bAtk = KitSrc.From(btnAtkTex); _bBlk = KitSrc.From(btnBlkTex); _bChg = KitSrc.From(btnChgTex); _bGo = KitSrc.From(btnGoTex);
            _bPlain = KitSrc.From(btnPlainTex);
        }

        private KitPix Frame(RectInt r) { var p = new KitPix(r.width, r.height); p.NineSlice(Game2DKit.Frame, FrameB, Well); return p; }

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
            bool deck = sim != null && Game2DBattleSim.Active && sim.DeckMode;
            Ensure(ref _tr, _right, Right); Ensure(ref _tt, _top, Top);
            bool tip = false;
            if (deck)
            {
                EnsureSized(ref _tb, _board, Board.width, BoardH(sim.board.Count)); Ensure(ref _tc, _ctl, Ctl);
                DrawBoard(_tb); DrawCtl(_tc); tip = DrawTip();
            }
            else { Ensure(ref _tl, _left, Left); DrawLeft(_tl); }
            Show(_left, !deck); Show(_board, deck); Show(_ctl, deck); Show(_tip, tip && _slide > 0.95f);
            DrawRight(_tr); DrawTop(_tt);
            Ensure(ref _ts, _stat, Stat); DrawStat(_ts);
        }

        /// <summary>中身で高さが変わるパネル (Board) のテクスチャ: 大きさが違えば作り直し、 RectTransform の大きさも合わせる。</summary>
        private static void EnsureSized(ref Texture2D tex, RectTransform rt, int w, int h)
        {
            if (rt == null) return;
            if (tex != null && tex.width == w && tex.height == h) return;
            if (tex != null) Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DBattleHud" };
            rt.sizeDelta = new Vector2(w, h);
            var ri = rt.GetComponent<RawImage>(); if (ri != null) ri.texture = tex;
        }

        /// <summary>パネルを出す / 隠す (山札の時は左のパネルの代わりに Board と Ctl)。 絵がまだ無いパネルは出さない。</summary>
        private static void Show(RectTransform rt, bool on)
        {
            var ri = rt != null ? rt.GetComponent<RawImage>() : null;
            if (ri != null) ri.enabled = on && ri.texture != null;
        }

        private CombatSystem.CombatManager.HudStats _hs; private bool _hsKnown;

        /// <summary>自分のステータス: 会心率と倍率・攻撃と与ダメ補正・防御と貫通。 数字は配線を決める間に読んだ値。</summary>
        private void DrawStat(Texture2D t)
        {
            var p = Frame(Stat);
            bool k = _hsKnown && sim != null && Game2DBattleSim.Active;
            string crit = k ? "会心 " + Mathf.RoundToInt(_hs.critRate * 100f) + "% " + _hs.critMul.ToString("0.0") + "倍" : "会心 --";
            string atk = k ? "攻撃 " + _hs.attack : "攻撃 --";      // 与ダメの倍率は着弾の数字の横に出す (Game2DBattleFx・2026-11-11)
            string def = k ? "防御 " + _hs.defense + " 貫通 " + _hs.pen : "防御 --";
            p.Text(crit, 14, 14, Chg); p.Text(atk, 14, 27, Atk); p.Text(def, 14, 40, Blk);
            p.Apply(t);
        }

        /// <summary>テクスチャは実行時に作る (シーンには保存されないので、 再生のたびに作り直す)。</summary>
        private static void Ensure(ref Texture2D tex, RectTransform rt, RectInt r)
        {
            if (tex != null || rt == null) return;
            tex = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DBattleHud" };
            var ri = rt.GetComponent<RawImage>(); if (ri != null) { ri.texture = tex; ri.enabled = true; }
        }

        private void DrawTerm(KitPix p, RectInt tr, int wv, bool on)
        {
            var s = wv == 0 ? _bAtk : wv == 1 ? _bBlk : wv == 2 ? _bChg : null;      // 特殊端子は専用の絵が無いので板で
            if (s != null) p.Sprite(s, tr.x, tr.y, tr.width, tr.height);
            else p.Box(tr.x, tr.y, tr.width, tr.height, on ? TermCol[wv] : Dim, Dark);
            p.TextC(wv == 3 ? CombatSystem.SpecialTerminals.ShortName(CombatSystem.SpecialTerminals.Equipped(GameLoop.GameManager.Instance?.Run)) : TermName[wv], tr, on ? Ink : Dim);      // 特殊端子は付けている物の名前
        }

        private void DrawLeft(Texture2D t)
        {
            var p = Frame(Left);
            bool live = sim != null && Game2DBattleSim.Active;
            for (int i = 0; i < 3; i++)
            {
                var dr = DieR(i); var tr = TermR(i); int wv = live ? sim.wire[i] : i; bool on = live && sim.CanOperate;
                DrawDie(p, dr, live ? sim.pd[i] : i + 1, false, live && !sim.rolled && sim.phase != Game2DBattleSim.P.PRoll);
                p.Rect(dr.x + dr.width + 1, dr.y + 8, tr.x - dr.x - dr.width - 2, 2, on ? Ink : Dim);                      // 配線 (ダイス → 端子)
                DrawTerm(p, tr, wv, on);
            }
            string a = "-", b = "-", c = "-";
            if (live && sim.rolled) { var w = sim.GetWired(); a = w.atk + " = " + (w.atk - w.aDice) + "+" + w.aDice; b = w.blk.ToString(); c = "+" + w.chg + " (" + sim.charge + ")"; }
            p.Text("攻撃 " + a, 15, 90, Atk); p.Text("防御 " + b, 15, 103, Blk); p.Text("充電 " + c, 15, 116, Chg);
            // 振り直し: 充電を払って全ダイスを振り直す (払えない・判断の外では暗く)
            bool rr = live && sim.CanRerollNow;
            if (Game2DKit.Tab != null) p.NineSlice(Game2DKit.Tab, 5, Well, RerollR.x, RerollR.y, RerollR.width, RerollR.height);
            else p.Box(RerollR.x, RerollR.y, RerollR.width, RerollR.height, Off, Dark);
            p.TextC(live && sim.canReroll ? "振り直す -" + sim.rerollCost : "振り直す", RerollR, rr ? Chg : Dim);
            // 消耗品 / 逃げる (逃げるは 2 度押しで確定: SURE? → 押すとこのターンは攻撃せずに離脱)
            if (!live || !sim.CanFlee) _fleeArmed = false;
            bool itemOn = live && sim.CanOperate && CombatItems().Count > 0, fleeOn = live && sim.CanFlee;
            SmallBtn(p, ItemR, "道具", itemOn, Chg);
            SmallBtn(p, FleeR, _fleeArmed ? "本当に?" : "逃げる", fleeOn, _fleeArmed ? Atk : Ink);
            DrawGo(p, Btn, live);
            p.Apply(t);
        }

        /// <summary>決定ボタン (決める → 次へ / 終わる)。</summary>
        private void DrawGo(KitPix p, RectInt r, bool live)
        {
            bool en = live && sim.ButtonEnabled; string lab = live ? sim.ButtonLabel : "...";
            if (_bGo != null) p.Sprite(_bGo, r.x, r.y, r.width, r.height);
            else p.Box(r.x, r.y, r.width, r.height, en ? new Color32(0x6d, 0x3a, 0x16, 255) : Off, Dark);
            p.TextC(lab, r, en ? Ink : Dim, 2);
        }

        // ── 端子の山札 (ADR-0014) の Board と説明の帯 ──

        /// <summary>Board: 上 = 出目 3 個 (下の帯が行き先の種類の色) と 山/捨て の枚数 / 下 = 場の端子 (USB メモリ 1 本ずつ)。 字は常時出さない。
        /// 発動する見込みの端子は明るく・今の出目を 1 個も挿せない端子は暗く・解決後に発動した端子は種類の色で光らせる (色と明度だけ・拡大しない)。</summary>
        private void DrawBoard(Texture2D t)
        {
            int m = sim.board.Count;
            var p = Frame(new RectInt(0, 0, Board.width, BoardH(m)));
            var cm = CombatSystem.CombatManager.Instance;
            bool res = sim.ShowingResult; var o = sim.preview;
            int[] dieSlot = res && o != null ? o.slotOfDie : sim.slot;
            // 出目 3 個
            for (int i = 0; i < 3; i++)
            {
                var dr = BDieR(i);
                DrawDie(p, dr, sim.pd[i], false, !sim.rolled && sim.phase != Game2DBattleSim.P.PRoll);
                int s = dieSlot != null && i < dieSlot.Length ? dieSlot[i] : -1;
                var def = s >= 0 && s < m ? CombatSystem.TerminalCards.Get(sim.board[s]) : null;
                bool on = def != null && o != null && i < o.slotOfDie.Length && o.slotOfDie[i] == s;      // 挿して発動に使われる出目
                p.Rect(dr.x, dr.y + dr.height + 1, dr.width, 2, def == null ? Game2DKit.Faint : on ? Game2DTerminalLook.KindColor(def.kind) : Dim);
            }
            // 山・捨て札 (この戦闘から外れた枚数は捨ての後ろに)
            var dk = cm != null ? cm.TerminalDeck : null;
            if (dk != null)
            {
                p.TextR("山" + dk.draw.Count, BPad + BInW, 5, Dim);
                p.TextR("捨" + dk.discard.Count + (dk.exiled.Count > 0 ? "外" + dk.exiled.Count : ""), BPad + BInW, 17, Game2DKit.Faint);
            }
            for (int k = 0; k < m; k++) DrawStick(p, k, m, o, res, dieSlot);
            p.Apply(t);
        }

        /// <summary>USB メモリ 1 本: 上 = 差し込み口 (金属・挿した出目の数字を出す。 2 個挿せる端子は口が広い) / 下 = 本体 (種類の色・16×16 のアイコン)。
        /// 印: 使い捨て = 本体の右下の切り欠き / 常設 = 本体の下の角の鋲 / レア = 本体の縁が金。 選んでいる・乗っている端子は琥珀の縁。</summary>
        private void DrawStick(KitPix p, int k, int m, CombatSystem.TerminalOutcome o, bool res, int[] dieSlot)
        {
            var r = StickR(k, m); if (r.width <= 0) return;
            var d = CombatSystem.TerminalCards.Get(sim.board[k]);
            var kc = d != null ? Game2DTerminalLook.KindColor(d.kind) : Dim;
            bool fires = o != null && o.fired.Contains(k), lit = res && fires;
            bool fits = res || !sim.CanOperate || sim.AnyDieFits(k);
            // 本体の色: 光る (解決後に発動) > 明るい (見込みで発動) > 普通 > 暗い (挿せる出目が無い・解決後に発動しなかった)
            Color32 body = lit ? Game2DTerminalLook.Mix(kc, Game2DKit.Ivory, 0.2f)
                         : fires ? Game2DTerminalLook.Mix(kc, Off, 0.25f)
                         : (!fits || res) ? Game2DTerminalLook.Mix(kc, Off, 0.8f)
                         : Game2DTerminalLook.Mix(kc, Off, 0.55f);
            bool sel = k == _hover || k == sim.focusSlot;
            // 差し込み口
            int cap = d != null ? Mathf.Clamp(d.capacity, 1, 2) : 1;
            int cw = cap >= 2 ? Mathf.Min(r.width - 4, 22) : 12, cx = r.x + (r.width - cw) / 2, cy = r.y;
            var metal = lit ? new Color32(0xc8, 0xc4, 0xb8, 255) : fits ? new Color32(0x8a, 0x88, 0x80, 255) : new Color32(0x4a, 0x48, 0x42, 255);
            p.Box(cx, cy, cw, BConnH, Off, metal);
            int shown = 0;
            for (int i = 0; i < 3 && dieSlot != null && i < dieSlot.Length && shown < cap; i++)
            {
                if (dieSlot[i] != k) continue;
                bool used = o != null && i < o.slotOfDie.Length && o.slotOfDie[i] == k;
                string v = sim.pd[i].ToString();
                int sx = cap >= 2 ? cx + 2 + shown * (cw - 4) / 2 : cx, sw = cap >= 2 ? (cw - 4) / 2 : cw;
                p.Text(v, sx + (sw - Game2DJpFont.Width(v)) / 2, cy + 1, used ? Game2DKit.Ivory : Dim);      // 挿したが発動に使われない出目 (条件・充電・2 個そろわない) は暗く
                shown++;
            }
            if (shown == 0)      // 空の口: 接点の 2 本の筋
            {
                p.Rect(cx + cw / 2 - 3, cy + 4, 2, 4, metal); p.Rect(cx + cw / 2 + 1, cy + 4, 2, 4, metal);
            }
            // 本体
            int by = r.y + BConnH, bh = r.height - BConnH;
            var edge = sel ? Game2DKit.Amber : d != null && d.rare ? Game2DTerminalLook.Mix(Game2DKit.Gold, Game2DKit.Ivory, 0.3f) : lit ? Game2DTerminalLook.Mix(kc, Game2DKit.Ivory, 0.6f) : Dark;
            p.Box(r.x, by, r.width, bh, body, edge);
            if (lit) p.Rect(r.x + 1, by + 1, r.width - 2, 1, Game2DTerminalLook.Mix(kc, Game2DKit.Ivory, 0.7f));      // 上の照り
            if (d != null && d.disposable) { p.Rect(r.x + r.width - 4, by + bh - 4, 4, 4, Well); p.Rect(r.x + r.width - 4, by + bh - 4, 1, 3, edge); p.Rect(r.x + r.width - 4, by + bh - 4, 3, 1, edge); }
            if (d != null && d.permanent) { var rv = Game2DTerminalLook.Mix(body, Game2DKit.Ivory, 0.6f); p.Rect(r.x + 2, by + bh - 3, 1, 1, rv); p.Rect(r.x + r.width - 3, by + bh - 3, 1, 1, rv); }
            // アイコン (無ければ種類の 1 字)
            int ix = r.x + (r.width - 16) / 2, iy = by + (bh - 16) / 2;
            var icon = d != null ? IconOf(d.id) : null;
            if (icon != null && icon.Length >= 256)
            {
                Color32 c1 = Game2DTerminalLook.Mix(body, Dark, 0.6f), c2 = Game2DTerminalLook.Mix(body, Game2DKit.Ivory, 0.35f), c3 = Game2DTerminalLook.Mix(body, Game2DKit.Ivory, 0.75f);
                for (int j = 0; j < 16; j++)
                    for (int i = 0; i < 16; i++)
                    {
                        byte v = icon[j * 16 + i];
                        if (v != 0) p.Set(ix + i, iy + j, v == 1 ? c1 : v == 2 ? c2 : c3);
                    }
            }
            else
            {
                string ch = d != null ? Game2DTerminalLook.KindShort(d.kind) : "?";
                p.Text(ch, r.x + (r.width - Game2DJpFont.Width(ch)) / 2, by + (bh - Game2DJpFont.Cell) / 2 + 1, Game2DTerminalLook.Mix(body, Game2DKit.Ivory, fits ? 0.75f : 0.4f));
            }
        }

        /// <summary>説明の帯 (マウスを乗せた / 押して選んだ端子だけ): 1 行目 = 名前 (種類の色) と印 / 続けて効果。 Board の右に、 端子の高さに合わせて出す。
        /// 出すものが無ければ false (帯を隠す)。</summary>
        private bool DrawTip()
        {
            int m = sim.board.Count, f = _hover >= 0 ? _hover : sim.focusSlot;
            if (f < 0 || f >= m || _tip == null) return false;
            var d = CombatSystem.TerminalCards.Get(sim.board[f]); if (d == null) return false;
            Ensure(ref _tt2, _tip, Tip);
            int iw = Tip.width - 10;
            var lines = new List<string>();
            string head = d.id + "（" + Game2DTerminalLook.Marks(d, GameManager.Instance?.Run) + "）";
            lines.AddRange(Game2DJpFont.Lines(head, iw));
            lines.AddRange(Game2DJpFont.Lines(d.description, iw));
            int n = Mathf.Min(TipLines, lines.Count), h = 6 + n * Game2DJpFont.LineH + 3;
            var p = new KitPix(Tip.width, Tip.height);      // 使わない下の部分は透明のまま
            var kc = Game2DTerminalLook.KindColor(d.kind);
            p.Box(0, 0, Tip.width, h, Well, kc);
            for (int i = 0; i < n; i++)
            {
                bool headLine = i < Game2DJpFont.Lines(head, iw).Count;
                Game2DJpFont.Draw(p, lines[i], 5, 4 + i * Game2DJpFont.LineH, headLine ? Dim : Ink);
            }
            if (lines.Count > 0) Game2DJpFont.Draw(p, d.id, 5, 4, kc);      // 名前だけ種類の色で上書き
            p.Apply(_tt2);
            // 位置: 端子の本体の上端にそろえ、 資源バー (y 214〜) にかからないよう上へ寄せる
            var sr = StickR(f, m);
            int y = Mathf.Clamp(Board.y + sr.y, 2, TipMaxY - h);
            _tip.anchoredPosition = new Vector2(Tip.x, -y);
            return true;
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
            SmallBtn(p, CItem, "道具", sim.CanOperate && CombatItems().Count > 0, Chg);
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

        /// <summary>戦闘中に使える消耗品 (囮罠は航行の道具なので除く)。 同じ品は 1 行にまとめる。</summary>
        private static List<string> CombatItems()
        {
            var l = new List<string>(); var run = GameManager.Instance?.Run;
            if (run?.ownedConsumables == null) return l;
            foreach (var id in run.ownedConsumables)
                if (!l.Contains(id) && ItemIds.ConsFamilyOf(id) != ItemIds.ConsLureFamily) l.Add(id);
            return l;
        }

        /// <summary>消耗品を選んで使う窓 (配線の判断の間だけ)。 使うと窓を閉じ、 HP などの表示を写し直す。</summary>
        private void OpenItems()
        {
            if (!sim.CanOperate) return;
            if (_modal == null) _modal = FindObjectOfType<Game2DModal>();
            var items = CombatItems(); var run = GameManager.Instance?.Run;
            if (_modal == null || items.Count == 0 || run == null) { Game2DSfx.Play("ui_click"); return; }
            var d = new ScreenDef { id = "battle_items", jp = true, title = "消耗品", sub = "HP " + sim.playerHp + "/" + sim.playerMax, w = 360 };
            foreach (var id in items)
            {
                int n = 0; foreach (var c in run.ownedConsumables) if (c == id) n++;
                var data = ItemSystem.ItemDatabase.Instance?.GetItem(id);
                d.Items.Add(new ModalItem { name = data != null && !string.IsNullOrEmpty(data.displayName) ? data.displayName : id, value = "x" + n, desc = data?.description });
            }
            d.buttons.Add(new ModalBtn { label = "使う", action = "use", primary = true });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.footer = "使う品を選ぶ";
            d.onAction = (a, sel) =>
            {
                if (a == "close") { _modal.Close(); return; }
                if (a != "use" || sel < 0 || sel >= items.Count) return;
                if (sim.UseItem(items[sel])) { Game2DSfx.Play("ui_confirm"); _modal.Close(); _drawn = -1; }
                else _modal.ShowToast("今は使えない");
            };
            _modal.Open(d);
        }

        private void DrawRight(Texture2D t)
        {
            var p = Frame(Right);
            bool live = sim != null && Game2DBattleSim.Active; bool known = live && sim.phase != Game2DBattleSim.P.ERoll;
            int en = live ? sim.enemyDiceCount : 3;
            for (int i = 0; i < en; i++) DrawDie(p, new RectInt(14 + i * 20, 16, 17, 17), live ? sim.ed[i] : i + 3, true);
            p.Text(known ? "攻撃 " + sim.eatk : "攻撃 ??", 14, 36, known ? Atk : Dim);
            if (known) p.Text("出目 " + sim.EnemyDiceSum, 14, 49, Dim);
            if (known && sim.edef > 0) p.Text("防御 " + sim.edef, 14, 62, Ink);      // 敵の防御 (構え・割れる/積み上がる装甲を反映した今の値)
            // 予告 (攻撃・大技・構え) と毒・出血は敵の周りに出す (Game2DBattleFx.DrawMarks・2026-11-11)。 ここは次の構え・大技の前触れだけ
            string next = known ? (sim.guardIn == 1 && sim.heavyIn == 1 ? "次: 構え・大技" : sim.guardIn == 1 ? "次: 構え" : sim.heavyIn == 1 ? "次: 大技" : null) : null;
            if (next != null) p.Text(next, 14, 75, sim.heavyIn == 1 ? MarkHeavyHud : Dim);
            if (live) p.Text("手番 " + sim.turn + "  段階 " + sim.Stage, 14, 90, Dim);
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
            p.Text(cur + "/" + max, 228, 29, Ink);
            p.Apply(t);
        }
    }
}

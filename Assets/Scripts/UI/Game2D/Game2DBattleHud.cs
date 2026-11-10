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
    /// <para>端子の山札 (ADR-0014) で戦っている時は、 左のパネルの代わりに Board (出目・場の端子・見込み・説明) と Ctl (操作) の 2 枚を出す。
    /// 出目を押す = 挿し先を順に変える (挿せない端子は飛ばす・右クリックで逆) / 端子を押す = 説明 / おまかせ・決める。 見込みは
    /// <see cref="CombatSystem.TerminalCards.Resolve"/> を状態の写しで呼んだ値 (<see cref="Game2DBattleSim.preview"/>)。</para>
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
        //   Board = 出目 3 個・場の端子 (3〜5 枚)・見込み・選んだ端子の説明。 敵 (x 190〜) にかからない幅。
        //   Ctl   = 振り直す・おまかせ・道具・逃げる・決める。 資源バー (x 110〜・y 215〜) にかからない幅。
        private static readonly RectInt Board = new RectInt(2, 60, 184, 154), Ctl = new RectInt(2, 216, 108, 52);
        private const int BPad = 9, BRowW = 166, BRowY = 27, BRowsH = 70, BPrevY = 99, BDescY = 113, BDescLines = 3;
        private static RectInt BDieR(int i) => new RectInt(BPad + i * 22, 6, 18, 18);
        /// <summary>場の端子の行の高さ (5 枚までは 14・それより多い時は詰める)。</summary>
        private static int BRowH(int count) => count <= 5 ? 14 : Mathf.Max(11, BRowsH / count);
        private static RectInt BRowR(int k, int count) { int h = BRowH(count); return new RectInt(BPad, BRowY + k * h, BRowW, h - 1); }
        private static readonly RectInt CReroll = new RectInt(5, 5, 56, 20), CAuto = new RectInt(63, 5, 40, 20),
            CItem = new RectInt(5, 27, 24, 20), CFlee = new RectInt(31, 27, 36, 20), CGo = new RectInt(69, 27, 34, 20);
        private RectTransform _board, _ctl; private Texture2D _tb, _tc;
        /// <summary>マウスが乗っている端子の行 (−1 = なし)。 乗っている間はその端子の説明を出す。</summary>
        private int _hover = -1;

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
            if (_tb != null) Destroy(_tb); if (_tc != null) Destroy(_tc);
        }

        /// <summary>組み上げ済みのシーンに無いパネルを作る (ステータス・端子の山札の 2 枚。 実行時に作り、 シーンには保存しない)。</summary>
        private void EnsurePanels()
        {
            if (_stat == null) _stat = Make("stat", Stat, out _ts);
            if (_board == null) _board = Make("board", Board, out _tb);
            if (_ctl == null) _ctl = Make("ctl", Ctl, out _tc);
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
            _board.anchoredPosition = new Vector2(Board.x - Mathf.Round((1f - _spL.Value(190f)) * 190f), -Board.y);
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
            for (int k = 0; k < m; k++) if (In(d, Board, BRowR(k, m))) return k;
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
            if (deck) { Ensure(ref _tb, _board, Board); Ensure(ref _tc, _ctl, Ctl); DrawBoard(_tb); DrawCtl(_tc); }
            else { Ensure(ref _tl, _left, Left); DrawLeft(_tl); }
            Show(_left, !deck); Show(_board, deck); Show(_ctl, deck);
            DrawRight(_tr); DrawTop(_tt);
            Ensure(ref _ts, _stat, Stat); DrawStat(_ts);
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

        // ── 端子の山札 (ADR-0014) の 2 枚 ──

        /// <summary>Board: 上 = 出目 3 個 (下の帯が行き先の種類の色) と山・捨て札の枚数 / 中 = 場の端子の行 / 見込みの行 / 下 = 説明 (3 行)。
        /// 端子の行: 種類の帯と 1 字・名前・条件・挿した出目の枠 (挿せる数だけ)・発動のランプ。 解決した後は、 発動した端子の行を種類の色で光らせる。</summary>
        private void DrawBoard(Texture2D t)
        {
            var p = Frame(Board);
            var cm = CombatSystem.CombatManager.Instance; var run = GameManager.Instance?.Run;
            bool res = sim.ShowingResult; var o = sim.preview; int m = sim.board.Count;
            int[] dieSlot = res && o != null ? o.slotOfDie : sim.slot;
            // 出目 3 個
            for (int i = 0; i < 3; i++)
            {
                var dr = BDieR(i);
                DrawDie(p, dr, sim.pd[i], false, !sim.rolled && sim.phase != Game2DBattleSim.P.PRoll);
                int s = dieSlot != null && i < dieSlot.Length ? dieSlot[i] : -1;
                var def = s >= 0 && s < m ? CombatSystem.TerminalCards.Get(sim.board[s]) : null;
                bool on = def != null && o != null && i < o.slotOfDie.Length && o.slotOfDie[i] == s;      // 見込みでも挿さったまま (= 発動に使われる)
                p.Rect(dr.x, dr.y + dr.height + 1, dr.width, 2, def == null ? Game2DKit.Faint : on ? Game2DTerminalLook.KindColor(def.kind) : Dim);
            }
            // 山・捨て札・この戦闘から外れた枚数
            var dk = cm != null ? cm.TerminalDeck : null;
            if (dk != null) p.TextR("山 " + dk.draw.Count + " 捨 " + dk.discard.Count + (dk.exiled.Count > 0 ? " 外 " + dk.exiled.Count : ""), BPad + BRowW, 9, Dim);
            // 場の端子
            if (m == 0) p.Text("端子を引く前", BPad + 4, BRowY + 2, Game2DKit.Faint);
            for (int k = 0; k < m; k++) DrawTermRow(p, k, m, o, res, dieSlot);
            // 見込み (解決後は結果)
            p.Rect(BPad, BPrevY - 2, BRowW, 1, Off);
            DrawPreviewLine(p, o, res);
            // 説明: マウスが乗っている / 選んだ端子の全文。 無ければ見込みの効果の一覧か操作の案内
            p.Rect(BPad, BDescY - 2, BRowW, 1, Off);
            int f = _hover >= 0 ? _hover : sim.focusSlot;
            string desc; Color32 col = Ink;
            if (f >= 0 && f < m) desc = Game2DTerminalLook.Describe(CombatSystem.TerminalCards.Get(sim.board[f]), run);
            else
            {
                var ex = Extras(o); col = Dim;
                desc = ex.Count > 0 ? (res ? "結果: " : "見込み: ") + string.Join("・", ex)
                     : sim.CanOperate ? "出目を押すと挿し先が変わる (右クリックで逆・1〜3 キー)。 端子を押すと説明。 Q でおまかせ。" : "";
                if (sim.CanOperate && dk != null && dk.state.ignoreCond) desc = "このターンは端子の条件を無視できる。 " + desc;
            }
            if (!string.IsNullOrEmpty(desc)) Game2DJpFont.Wrap(p, desc, BPad, BDescY, BRowW, col, BDescLines);
            p.Apply(t);
        }

        private void DrawTermRow(KitPix p, int k, int m, CombatSystem.TerminalOutcome o, bool res, int[] dieSlot)
        {
            var r = BRowR(k, m);
            var d = CombatSystem.TerminalCards.Get(sim.board[k]);
            var kc = d != null ? Game2DTerminalLook.KindColor(d.kind) : Dim;
            bool fires = o != null && o.fired.Contains(k), lit = res && fires;
            // 地: 解決して発動した行 = 種類の色で光らせる / 見込みで発動する行 = 明るい板 / 解決後に発動しなかった行 = 暗い板
            if (lit)
            {
                p.Rect(r.x, r.y, r.width, r.height, Game2DTerminalLook.Mix(Well, kc, 0.45f));
                p.Rect(r.x, r.y, r.width, 1, Game2DTerminalLook.Mix(kc, Game2DKit.Ivory, 0.4f));
            }
            else p.Plate(r.x, r.y, r.width, r.height, fires ? 1.35f : res ? 0.7f : 1f);
            p.Rect(r.x + 1, r.y + 1, 3, r.height - 2, fires ? kc : Game2DTerminalLook.Mix(kc, Off, 0.55f));      // 種類の帯
            if (k == _hover || k == sim.focusSlot)
            {
                var a = Game2DKit.Amber;
                p.Rect(r.x, r.y, r.width, 1, a); p.Rect(r.x, r.y + r.height - 1, r.width, 1, a); p.Rect(r.x + r.width - 1, r.y, 1, r.height, a);
            }
            int ty = r.y + (r.height - Game2DJpFont.Cell) / 2 + 1;
            if (d == null) { p.Text(Game2DJpFont.Clip(sim.board[k], 100), r.x + 19, ty, Dim); return; }
            p.Text(Game2DTerminalLook.KindShort(d.kind), r.x + 7, ty, lit ? Game2DKit.Ivory : kc);
            p.Text(Game2DJpFont.Clip(d.id, 52), r.x + 19, ty, lit ? Game2DKit.Ivory : Ink);
            string c = Game2DTerminalLook.CondShort(d);
            if (c != null) p.Text(Game2DJpFont.Clip(c, 54), r.x + 73, ty, lit ? Ink : Dim);
            // 挿した出目の枠 (挿せる数だけ・右寄せ)。 挿したが発動に使われない出目 (条件・充電・2 個そろわない) は暗く
            int cap = Mathf.Clamp(d.capacity, 1, 3), fill = 0, bh = r.height;
            for (int i = 0; i < 3 && dieSlot != null && i < dieSlot.Length && fill < cap; i++)
            {
                if (dieSlot[i] != k) continue;
                bool used = o != null && i < o.slotOfDie.Length && o.slotOfDie[i] == k;
                int bx = r.x + r.width - 10 - (cap - fill) * 14;
                p.Box(bx, r.y, 12, bh, Off, used ? kc : Dim);
                string v = sim.pd[i].ToString();
                p.Text(v, bx + (12 - Game2DJpFont.Width(v)) / 2, ty, used ? Ink : Dim);
                fill++;
            }
            for (; fill < cap; fill++)
            {
                int bx = r.x + r.width - 10 - (cap - fill) * 14;
                p.Box(bx, r.y + 1, 12, bh - 2, Well, Game2DKit.Faint);
            }
            // 発動のランプ (見込み・結果)
            int lx = r.x + r.width - 8, ly = r.y + (r.height - 5) / 2;
            p.Box(lx, ly, 5, 5, fires ? kc : Off, fires ? Game2DTerminalLook.Mix(kc, Game2DKit.Ivory, 0.3f) : Game2DKit.Faint);
        }

        /// <summary>見込みの行: 「見込 攻 31 防 8 充 +4 …」。 攻撃の種類に 1 個も挿さないターンは攻撃しないので「攻 なし」。 入りきらない分は説明の欄 (何も選んでいない時) に出す。</summary>
        private void DrawPreviewLine(KitPix p, CombatSystem.TerminalOutcome o, bool res)
        {
            int x = BPad, y = BPrevY, right = BPad + BRowW;
            if (o == null) { p.Text(sim.CanOperate ? "見込 —" : "—", x, y, Game2DKit.Faint); return; }
            var w = sim.GetWired();
            x = Tok(p, x, y, right, res ? "結果" : "見込", Game2DKit.Faint);
            x = Tok(p, x, y, right, o.attackDice > 0 ? "攻 " + w.atk : "攻 なし", o.attackDice > 0 ? Atk : Dim);
            x = Tok(p, x, y, right, "防 " + o.blockAdd, Blk);
            x = Tok(p, x, y, right, "充 +" + o.chargeAdd + (o.chargeSpend > 0 ? " −" + o.chargeSpend : ""), Chg);
            foreach (var s in Extras(o)) { int nx = Tok(p, x, y, right, s, Ink); if (nx == x) break; x = nx; }
        }

        /// <summary>1 語を描いて次の x を返す。 入りきらなければ描かずに x をそのまま返す。</summary>
        private static int Tok(KitPix p, int x, int y, int right, string s, Color32 c)
        {
            int w = Game2DJpFont.Width(s);
            if (x + w > right) return x;
            p.Text(s, x, y, c);
            return x + w + 5;
        }

        /// <summary>攻撃・防御・充電の合計以外の効果 (見込み・結果)。 文は画面用の短い言い方。</summary>
        private static List<string> Extras(CombatSystem.TerminalOutcome o)
        {
            var l = new List<string>();
            if (o == null) return l;
            if (Mathf.Abs(o.damageMul - 1f) > 0.001f) l.Add("与ダメ×" + o.damageMul.ToString("0.##"));
            if (o.outgoingPctAdd > 0f) l.Add("与ダメ+" + Mathf.RoundToInt(o.outgoingPctAdd * 100f) + "%");
            if (o.forceCrit) l.Add("必ず会心");
            if (o.critMultAdd > 0f) l.Add("会心倍率+" + o.critMultAdd.ToString("0.0#"));
            if (o.heal > 0) l.Add("HP+" + o.heal);
            if (o.selfDamage > 0) l.Add("HP−" + o.selfDamage);
            if (o.shield > 0) l.Add("シールド+" + o.shield);
            if (o.takenMul < 0.999f) l.Add("被ダメ−" + Mathf.RoundToInt((1f - o.takenMul) * 100f) + "%");
            if (o.enemyAttackMul < 0.999f) l.Add("敵の攻撃−" + Mathf.RoundToInt((1f - o.enemyAttackMul) * 100f) + "%");
            if (o.poison > 0) l.Add("毒+" + o.poison);
            if (o.bleedOnHit > 0) l.Add("通れば出血+" + o.bleedOnHit);
            if (o.heat > 0) l.Add("臨界+" + o.heat);
            if (o.ignoreDefense) l.Add("防御無視");
            if (o.chargeFill) l.Add("充電を満たす");
            if (o.enemyStunNext) l.Add("敵は次のターン行動しない");
            if (o.retreatStage) l.Add("激化の段階を戻す");
            if (o.escalationDelay > 0) l.Add("激化を " + o.escalationDelay + " ターン遅らせる");
            return l;
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

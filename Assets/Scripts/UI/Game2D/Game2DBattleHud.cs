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
    /// <para>端子の山札 (ADR-0014) で戦っている時は、 左のパネルの代わりに 3 枚を左に積む: Ports (出目 3 個とそれぞれの差し込み口) / ステータス / Ctl (操作)。
    /// 右下 (Hand) にこのターンに引いた端子 (USB メモリの形) を並べ、 つかんで出目の差し込み口へ落とすと装着する (<see cref="Game2DBattleSim.Plug"/>)。
    /// 名前・条件・効果は USB にマウスを乗せた時だけ帯 (Tip) で出す。 発動の見込みは <see cref="CombatSystem.TerminalCards.Resolve"/> を状態の写しで呼んだ値
    /// (<see cref="Game2DBattleSim.preview"/>)。 攻撃・防御の数字は資源バーとステータスに任せる。</para>
    /// </summary>
    public class Game2DBattleHud : MonoBehaviour
    {
        // 配置 (dot)
        // 2026-11-11: 字を既定のフォント (高さ 12) にしたので、 左右の枠を広げ、 上の帯を高くし、 左右は帯の下から始める
        private static readonly RectInt Left = new RectInt(2, 60, 108, 208), Right = new RectInt(370, 60, 108, 120), Top = new RectInt(96, 2, 288, 56);
        private const int FrameB = 12;
        // 自分のステータス (会心率・攻撃・防御): 旧い配線の画面では右のパネルの下 (右と同じばねで横から入る・2026-11-05)。
        // 山札の画面では左下 (Ports と Ctl の間・左と同じばね)。
        private static readonly RectInt Stat = new RectInt(370, 184, 108, 68), StatDeck = new RectInt(2, 162, 108, 48);
        // 左パネルの中 (パネルの左上が原点): 枠の内側は x 14〜72
        private static readonly RectInt Btn = new RectInt(15, 170, 80, 26), RerollR = new RectInt(15, 134, 80, 16),
            ItemR = new RectInt(15, 152, 39, 15), FleeR = new RectInt(56, 152, 39, 15);      // 消耗品 / 逃げる (配線の判断の間だけ)
        private static RectInt DieR(int i) => new RectInt(15, 18 + i * 24, 18, 18);
        private static RectInt TermR(int i) => new RectInt(37, 18 + i * 24, 58, 18);

        // ── 端子の山札 (ADR-0014・2026-11-11) の配置 ──
        //   Ports (2,60,108,98)   = 出目 3 個と、 出目ごとの差し込み口 (挿した USB は横向きに挿さった絵で残る)。
        //   StatDeck (2,162,108,48) = 会心・攻撃・防御/貫通。
        //   Ctl (2,214,108,52)    = 振り直す・おまかせ / 道具・逃げる・決める。 x 110 まで = 資源バー (x 110〜・y 215〜) にかからない。
        //   Hand (318,184,160,80) = このターンに引いた端子 (縦向きの USB・左から) と常設 (右の端・仕切りの右)・山/捨ての枚数。 敵 (x 190〜290) と予告の欄 (y 〜180) の外。
        //   Tip (幅 200)          = USB にマウスを乗せた時だけ。 差し込み口の USB は Ports の右 (x 112)、 手札の USB は Hand の上に出す。
        private static readonly RectInt Ports = new RectInt(2, 60, 108, 98), Ctl = new RectInt(2, 214, 108, 52), Hand = new RectInt(318, 184, 160, 80);
        private const int TipW = 200, TipH = 61, TipLines = 4, TipMaxY = 214;
        // 差し込み口の行 (Ports の中): 出目 → 配線 → 口 (USB の差し込み部が口に入る) → 本体
        private static int PortY(int i) => 6 + i * 31;
        private static RectInt PDieR(int i) => new RectInt(8, PortY(i) + 3, 18, 18);
        private static RectInt PortR(int i) => new RectInt(30, PortY(i), 68, 24);      // 落とせる範囲 (口と挿さった USB)
        // USB の大きさ: 差し込み部 8×12 (縦向きは 12×8) + 本体 24×24
        private const int UsbConn = 8, UsbConnW = 12, UsbBody = 24, UsbLong = UsbConn + UsbBody;
        // 手札 (Hand の中)
        private const int HandY = 18, HandStep = 28, HandPad = 6;
        private static readonly RectInt CReroll = new RectInt(5, 5, 56, 20), CAuto = new RectInt(63, 5, 40, 20),
            CItem = new RectInt(5, 27, 24, 20), CFlee = new RectInt(31, 27, 36, 20), CGo = new RectInt(69, 27, 34, 20);
        private RectTransform _ports, _ctl, _hand, _tip, _drag; private Texture2D _tp, _tc, _th, _tt2, _td;

        /// <summary>端子のアイコン (16×16・行優先・左上から・0 = 透明 / 1 = 暗 / 2 = 中 / 3 = 明)。 無ければ null。
        /// 仮: アイコンの担当 (Game2DTerminalIcons.Get) がそろうまで null を返す ── 差し替えはここ 1 か所。</summary>
        private static byte[] IconOf(string id) => Game2DTerminalIcons.Get(id);

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
        /// <summary>最後に描いた時、 山札の配置だったか (ステータスの置き場とばねを決める)。</summary>
        private bool _deckLayout;

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
            foreach (var t in new[] { _tl, _tr, _tt, _ts, _tp, _tc, _th, _tt2, _td }) if (t != null) Destroy(t);
        }

        /// <summary>組み上げ済みのシーンに無いパネルを作る (ステータス・山札の画面の部品。 実行時に作り、 シーンには保存しない)。
        /// 作る順 = 重なりの順 (後ほど手前): 説明の帯とつかんでいる USB をいちばん手前に。</summary>
        private void EnsurePanels()
        {
            if (_stat == null) _stat = Make("stat", Stat, out _ts);
            if (_ports == null) _ports = Make("ports", Ports, out _tp);
            if (_ctl == null) _ctl = Make("ctl", Ctl, out _tc);
            if (_hand == null) _hand = Make("hand", Hand, out _th);
            if (_tip == null) _tip = Make("tip", new RectInt(0, 0, TipW, TipH), out _tt2);
            if (_drag == null) _drag = Make("drag", new RectInt(0, 0, UsbBody, UsbLong), out _td);
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
            float inL = Mathf.Round((1f - _spL.Value(120f)) * 120f), inR = Mathf.Round((1f - _spR.Value(170f)) * 170f);
            _left.anchoredPosition = new Vector2(Left.x - inL, -Left.y);
            _right.anchoredPosition = new Vector2(Right.x + Mathf.Round((1f - _spR.Value(120f)) * 120f), -Right.y);
            _top.anchoredPosition = new Vector2(Top.x, -Top.y + Mathf.Round((1f - _spT.Value(62f)) * 62f));
            if (_deckLayout) _stat.anchoredPosition = new Vector2(StatDeck.x - inL, -StatDeck.y);
            else _stat.anchoredPosition = new Vector2(Stat.x + Mathf.Round((1f - _spR.Value(120f)) * 120f), -Stat.y);
            _ports.anchoredPosition = new Vector2(Ports.x - inL, -Ports.y);
            _ctl.anchoredPosition = new Vector2(Ctl.x - inL, -Ctl.y);
            _hand.anchoredPosition = new Vector2(Hand.x + inR, -Hand.y);
            if (_spL.x < 0.95f) { Show(_tip, false); CancelDrag(); }      // 説明の帯・つかみは、 パネルが収まっている間だけ
        }

        private void Update()
        {
            if (_left == null || sim == null || !Game2DBattleSim.Active) { CancelDrag(); return; }
            // 配線を決める間だけ読み直す (その後は本判定に取り込まれて二重に数えるので、 最後の値を出し続ける)
            if (sim.CanOperate && CombatSystem.CombatManager.Instance != null)
            { _hs = CombatSystem.CombatManager.Instance.PreviewHudStats(); _hsKnown = true; }
            bool deck = sim.DeckMode, ptr = _slide > 0.95f && !Game2DModal.Active && screen != null;
            Vector2 d = default; bool hasDot = ptr && Game2DPointer.TryDot(screen, out d);
            if (deck) DeckPointer(hasDot, d);
            else if (_dragS >= 0) CancelDrag();
            // 引いてくる動きの間は毎フレーム描き直す
            if (sim.boardSerial != _dealSerial) { _dealSerial = sim.boardSerial; _dealT0 = Time.unscaledTime; }
            bool dealing = deck && Time.unscaledTime - _dealT0 < DealTime(sim.board.Count);
            int ehp = fx != null ? fx.EnemyHp : 0, ver = sim.version * 31 + ehp * 7 + sim.charge
                + (_hsKnown ? Mathf.RoundToInt(_hs.critRate * 1000f) * 131 + _hs.attack * 17 + _hs.outgoingPct * 7 + _hs.defense * 3 + _hs.pen : 0)
                + (deck ? 977 + DeckCountsHash() : 0) + (dealing ? Time.frameCount * 7919 : 0);
            if (ver != _drawn) { _drawn = ver; Redraw(); }
            if (!hasDot || deck) return;
            if (Input.GetMouseButtonDown(0)) Click(d);
        }

        private bool In(Vector2 d, RectInt panel, RectInt r) => d.x >= panel.x + r.x && d.x < panel.x + r.x + r.width && d.y >= panel.y + r.y && d.y < panel.y + r.y + r.height;

        /// <summary>旧い配線の画面のクリック。</summary>
        private void Click(Vector2 d)
        {
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
            if (deck != _deckLayout) { _deckLayout = deck; if (_ts != null) { Destroy(_ts); _ts = null; } }      // ステータスの大きさが変わる
            Ensure(ref _tr, _right, Right); Ensure(ref _tt, _top, Top);
            bool tip = false;
            if (deck)
            {
                Ensure(ref _tp, _ports, Ports); Ensure(ref _tc, _ctl, Ctl); Ensure(ref _th, _hand, Hand);
                DrawPorts(_tp); DrawCtl(_tc); DrawHand(_th); tip = DrawTip();
            }
            else { Ensure(ref _tl, _left, Left); DrawLeft(_tl); }
            Show(_left, !deck); Show(_ports, deck); Show(_ctl, deck); Show(_hand, deck); Show(_tip, tip && _slide > 0.95f);
            DrawRight(_tr); DrawTop(_tt);
            var sr = deck ? StatDeck : Stat;
            if (_stat != null) _stat.sizeDelta = new Vector2(sr.width, sr.height);
            Ensure(ref _ts, _stat, sr); DrawStat(_ts, sr, deck);
        }

        /// <summary>パネルを出す / 隠す。 絵がまだ無いパネルは出さない。</summary>
        private static void Show(RectTransform rt, bool on)
        {
            var ri = rt != null ? rt.GetComponent<RawImage>() : null;
            if (ri != null) ri.enabled = on && ri.texture != null;
        }

        private CombatSystem.CombatManager.HudStats _hs; private bool _hsKnown;

        /// <summary>自分のステータス: 会心率と倍率・攻撃と与ダメ補正・防御と貫通。 数字は配線を決める間に読んだ値。 山札の画面では左下の低いパネル。</summary>
        private void DrawStat(Texture2D t, RectInt r, bool deck)
        {
            var p = Frame(r);
            bool k = _hsKnown && sim != null && Game2DBattleSim.Active;
            string crit = k ? "会心 " + Mathf.RoundToInt(_hs.critRate * 100f) + "% " + _hs.critMul.ToString("0.0") + "倍" : "会心 --";
            string atk = k ? "攻撃 " + _hs.attack : "攻撃 --";      // 与ダメの倍率は着弾の数字の横に出す (Game2DBattleFx・2026-11-11)
            string def = k ? "防御 " + _hs.defense + " 貫通 " + _hs.pen : "防御 --";
            int x = deck ? 8 : 14, y = deck ? 5 : 14;
            p.Text(crit, x, y, Chg); p.Text(atk, x, y + 13, Atk); p.Text(def, x, y + 26, Blk);
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
            var s = wv == 0 ? _bAtk : wv == 1 ? _bBlk : wv == 2 ? _bChg : null;      // 補助は専用の絵が無いので板で
            if (s != null) p.Sprite(s, tr.x, tr.y, tr.width, tr.height);
            else p.Box(tr.x, tr.y, tr.width, tr.height, on ? TermCol[wv] : Dim, Dark);
            p.TextC(TermName[wv], tr, on ? Ink : Dim);
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

        // ── 端子の山札 (ADR-0014): 操作 ──
        //   右下の手札の USB をつかんで (左ボタンを押したまま動かす)、 左の出目の差し込み口へ落とす = その出目をその端子に挿す。
        //   挿さった USB もつかめる: 別の出目の口へ落とす = 移す / 口の外で離す = 手札へ戻す。 口の上で右クリック = 外す。
        //   別の USB を挿さっている口へ落とす = 入れ替え (前の USB は手札へ)。 落とせない口で離したら手札へ戻る。
        //   出目を押す = 行き先を順に変える (右クリックで逆・1〜3 キーと同じ)。 Q = おまかせ・Space = 決める (Sim のキー)。

        /// <summary>つかんでいる端子 (場の添字・−1 = つかんでいない) / つかんだ元の出目 (手札からなら −1)。</summary>
        private int _dragS = -1, _dragFrom = -1, _dropHover = -1;
        private bool _moved; private Vector2 _pressDot;
        private readonly bool[] _dropOk = new bool[3];
        /// <summary>マウスが乗っている USB (場の添字) と、 それが挿さっている出目 (手札なら −1)。 説明の帯に使う。</summary>
        private int _hoverS = -1, _hoverPort = -1;
        private int _dealSerial = -1; private float _dealT0 = -10f;

        private static float DealTime(int n) => 0.22f + 0.07f * n;

        private void DeckPointer(bool hasDot, Vector2 d)
        {
            if (_dragS >= 0)
            {
                if (!sim.CanOperate || _dragS >= sim.board.Count) { CancelDrag(); return; }
                if (hasDot)
                {
                    if (_drag != null) _drag.anchoredPosition = new Vector2(Mathf.FloorToInt(d.x) - UsbBody / 2, -(Mathf.FloorToInt(d.y) - UsbLong / 2));
                    if (!_moved && (d - _pressDot).sqrMagnitude > 9f) { _moved = true; Show(_drag, true); _drawn = -1; }
                    int tgt = PortAt(d); if (tgt != _dropHover) { _dropHover = tgt; _drawn = -1; }
                }
                if (Input.GetMouseButtonUp(0)) EndDrag(hasDot ? PortAt(d) : -1);
                return;
            }
            // 乗っている USB (説明の帯)
            int hs = -1, hp = -1;
            if (hasDot)
            {
                int pt = PortAt(d);
                if (pt >= 0 && sim.slot[pt] >= 0 && !sim.ShowingResult) { hs = sim.slot[pt]; hp = pt; }
                else if (pt >= 0 && sim.ShowingResult && sim.lastOutcome != null && pt < sim.lastOutcome.slotOfDie.Length && sim.lastOutcome.slotOfDie[pt] >= 0) { hs = sim.lastOutcome.slotOfDie[pt]; hp = pt; }
                else hs = HandAt(d);
            }
            if (hs != _hoverS || hp != _hoverPort) { _hoverS = hs; _hoverPort = hp; _drawn = -1; }
            if (!hasDot) return;
            if (Input.GetMouseButtonDown(0))
            {
                for (int i = 0; i < 3; i++) if (In(d, Ports, PDieR(i))) { sim.CycleWire(i, 1); return; }
                int pt = PortAt(d);
                if (pt >= 0) { if (sim.CanOperate && sim.slot[pt] >= 0) StartDrag(sim.slot[pt], pt, d); return; }
                int h = HandAt(d);
                if (h >= 0) { if (sim.CanOperate && sim.Remaining(h) > 0) StartDrag(h, -1, d); return; }
                if (In(d, Ctl, CReroll)) { sim.Reroll(); return; }
                if (In(d, Ctl, CAuto)) { sim.AutoWire(); return; }
                if (In(d, Ctl, CItem)) { OpenItems(); return; }
                if (In(d, Ctl, CFlee)) { PressFlee(); return; }
                if (In(d, Ctl, CGo)) { if (sim.ButtonEnabled) sim.Button(); }
            }
            else if (Input.GetMouseButtonDown(1))
            {
                for (int i = 0; i < 3; i++) if (In(d, Ports, PDieR(i))) { sim.CycleWire(i, -1); return; }
                int pt = PortAt(d); if (pt >= 0) sim.Unplug(pt);
            }
        }

        private void StartDrag(int s, int from, Vector2 d)
        {
            _dragS = s; _dragFrom = from; _moved = false; _pressDot = d; _dropHover = -1; _hoverS = _hoverPort = -1;
            for (int i = 0; i < 3; i++) _dropOk[i] = sim.CanPlug(i, s, from);
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

        private void EndDrag(int target)
        {
            int s = _dragS, from = _dragFrom; bool moved = _moved;
            CancelDrag();
            if (!moved || s < 0) return;      // 押しただけ (動かしていない) なら何もしない
            if (target >= 0)
            {
                if (target == from) return;
                if (sim.Plug(target, s, from)) return;
                Game2DSfx.Play("ui_click");      // 落とせない口: 手札へ戻る
            }
            if (from >= 0) sim.Unplug(from);
        }

        private void CancelDrag()
        {
            if (_dragS < 0) return;
            _dragS = _dragFrom = _dropHover = -1; _moved = false;
            Show(_drag, false); _drawn = -1;
        }

        /// <summary>d の下の出目の行 (出目か差し込み口)。 −1 = なし。</summary>
        private int PortAt(Vector2 d)
        {
            for (int i = 0; i < 3; i++) if (In(d, Ports, PortR(i)) || In(d, Ports, PDieR(i))) return i;
            return -1;
        }

        /// <summary>手札の並び: (場の添字, Hand の中の x)。 引いた端子は左から、 常設は右の端から (仕切りの右)。</summary>
        private List<KeyValuePair<int, int>> HandLayout(out int divider)
        {
            var l = new List<KeyValuePair<int, int>>(); divider = -1;
            int m = sim.board.Count, j = 0, q = 0;
            for (int s = 0; s < m; s++)
            {
                var def = CombatSystem.TerminalCards.Get(sim.board[s]);
                if (def != null && def.permanent) continue;
                l.Add(new KeyValuePair<int, int>(s, HandPad + j++ * HandStep));
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

        /// <summary>d の下の手札の USB (場の添字)。 −1 = なし。</summary>
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

        /// <summary>USB メモリ 1 本 (差し込み部 + 本体 24×24・本体に 16×16 のアイコン、 無ければ種類の 1 字)。 horiz = 横向き (差し込み部が左・口に挿さった絵) / 縦向き (差し込み部が上・手札)。
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
            int ix = bx + (b - 16) / 2, iy = by + (b - 16) / 2;
            var icon = d != null ? IconOf(d.id) : null;
            if (icon != null && icon.Length >= 256)
            {
                // 2bit の値を、 本体の色に対して 暗 / 中 / 明 の 3 階調で打つ
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
                p.Text(ch, bx + (b - Game2DJpFont.Width(ch)) / 2, by + (b - Game2DJpFont.Cell) / 2 + 1, Game2DTerminalLook.Mix(body, Game2DKit.Ivory, 0.7f));
            }
        }

        /// <summary>Ports: 出目ごとに 出目 → 配線 → 差し込み口。 挿した USB は横向きに挿さった絵で残る (2 個挿せる端子は両方の口に出る)。
        /// つかんでいる間は、 落とせる口を琥珀で明るく・落とせない口を暗く。 発動の見込み (解決後は結果) を口の右のランプで出す。</summary>
        private void DrawPorts(Texture2D t)
        {
            var p = Frame(Ports);
            bool res = sim.ShowingResult, dragging = _dragS >= 0 && _moved; var o = sim.preview; int m = sim.board.Count;
            int[] dieSlot = res && o != null ? o.slotOfDie : sim.slot;
            for (int i = 0; i < 3; i++)
            {
                int y0 = PortY(i);
                DrawDie(p, PDieR(i), sim.pd[i], false, !sim.rolled && sim.phase != Game2DBattleSim.P.PRoll);
                int s = dieSlot != null && i < dieSlot.Length ? dieSlot[i] : -1;
                var d = s >= 0 && s < m ? CombatSystem.TerminalCards.Get(sim.board[s]) : null;
                bool used = d != null && o != null && i < o.slotOfDie.Length && o.slotOfDie[i] == s;      // 挿して発動に使われる出目
                bool fires = d != null && o != null && o.fired.Contains(s), lit = res && fires;
                var kc = d != null ? Game2DTerminalLook.KindColor(d.kind) : Game2DKit.Faint;
                p.Rect(27, y0 + 11, 3, 2, used ? kc : Game2DKit.Faint);      // 配線
                bool ghost = dragging && _dragFrom == i;
                // 口の地: つかんでいる間は 落とせる = 琥珀 (真上なら明るく) / 落とせない = 暗い
                Color32 mouth = Game2DKit.Faint, fill = Well;
                if (dragging) { mouth = _dropOk[i] ? (_dropHover == i ? Game2DTerminalLook.Mix(Game2DKit.Amber, Game2DKit.Ivory, 0.4f) : Game2DKit.Amber) : Off; fill = _dropOk[i] ? Game2DTerminalLook.Mix(Game2DKit.Amber, Well, 0.8f) : Dark; }
                if (d == null || ghost)
                {
                    p.Box(30, y0, UsbLong + 2, UsbBody, fill, mouth);
                    p.Rect(31, y0 + (UsbBody - UsbConnW) / 2, 2, UsbConnW, dragging && _dropOk[i] ? mouth : Off);      // 差し込みの口
                }
                else
                {
                    var c = UsbColors(d, true, fires || (!res && used), _hoverPort == i && _hoverS == s, lit);
                    if (!used) c.body = Game2DTerminalLook.Mix(kc, Off, 0.8f);      // 挿したが発動に使われない (条件・充電・2 個そろわない)
                    if (dragging) c.edge = mouth;
                    DrawUsb(p, 31, y0, true, d, c.body, c.edge, c.metal);
                }
                // ランプ (見込みで発動 / 解決後に発動)
                int lx = 68, ly = y0 + 9;
                p.Box(lx, ly, 5, 5, fires && used ? kc : Off, fires && used ? Game2DTerminalLook.Mix(kc, Game2DKit.Ivory, 0.3f) : Game2DKit.Faint);
            }
            p.Apply(t);
        }

        /// <summary>Hand: 上 = 山/捨て(/外) の枚数 / 下 = 手札の USB (縦向き)。 ターンの頭に右から滑り込む (位置だけ・拡大しない)。
        /// 使い切った端子は抜け殻の枠だけ残す (並びが動かないように)。 2 個挿せる端子は残りの数を下に出す。 挿せる出目が無い端子は暗い。</summary>
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
                if (rem <= 0) { p.Box(x, HandY + UsbConn, UsbBody, UsbBody, Well, Off); continue; }      // 抜け殻
                bool fits = !op || res || sim.HandFits(s);
                var c = UsbColors(d, fits, false, _hoverPort < 0 && _hoverS == s, false);
                DrawUsb(p, x, HandY, false, d, c.body, c.edge, c.metal);
                int cap = d != null ? d.capacity : 1;
                if (cap >= 2) { string r = "残" + rem; p.Text(r, x + (UsbBody - Game2DJpFont.Width(r)) / 2, HandY + UsbLong + 1, Dim); }
            }
            p.Apply(t);
        }

        /// <summary>説明の帯 (USB にマウスを乗せた時だけ・つかんでいる間は出さない): 1 行目 = 名前 (種類の色) と印 / 続けて効果。
        /// 差し込み口の USB は Ports の右 (x 112) に口の高さで、 手札の USB は Hand の上に右寄せで出す。 出すものが無ければ false。</summary>
        private bool DrawTip()
        {
            int m = sim.board.Count, f = _hoverS;
            if (_dragS >= 0 || f < 0 || f >= m || _tip == null) return false;
            var d = CombatSystem.TerminalCards.Get(sim.board[f]); if (d == null) return false;
            Ensure(ref _tt2, _tip, new RectInt(0, 0, TipW, TipH));
            int iw = TipW - 10;
            string head = d.id + "（" + Game2DTerminalLook.Marks(d, GameManager.Instance?.Run) + "）";
            var headLines = Game2DJpFont.Lines(head, iw);
            var lines = new List<string>(headLines); lines.AddRange(Game2DJpFont.Lines(d.description, iw));
            int n = Mathf.Min(TipLines, lines.Count), h = 6 + n * Game2DJpFont.LineH + 3;
            var p = new KitPix(TipW, TipH);      // 使わない下の部分は透明のまま
            var kc = Game2DTerminalLook.KindColor(d.kind);
            p.Box(0, 0, TipW, h, Well, kc);
            for (int i = 0; i < n; i++) Game2DJpFont.Draw(p, lines[i], 5, 4 + i * Game2DJpFont.LineH, i < headLines.Count ? Dim : Ink);
            Game2DJpFont.Draw(p, d.id, 5, 4, kc);      // 名前だけ種類の色で上書き
            p.Apply(_tt2);
            int x, y;
            if (_hoverPort >= 0) { x = Ports.x + Ports.width + 2; y = Mathf.Clamp(Ports.y + PortY(_hoverPort), 2, TipMaxY - h); }
            else { x = Hand.x + Hand.width - TipW; y = Hand.y - h - 2; }
            _tip.anchoredPosition = new Vector2(x, -y);
            _tip.SetAsLastSibling();
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
            if (live) p.Text("ターン " + sim.turn + "  段階 " + sim.Stage, 14, 90, Dim);
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

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
    /// </summary>
    public class Game2DBattleHud : MonoBehaviour
    {
        // 配置 (dot)
        private static readonly RectInt Left = new RectInt(2, 52, 86, 198), Right = new RectInt(392, 56, 86, 112), Top = new RectInt(96, 2, 288, 44);
        private const int FrameB = 12;
        // 左パネルの中 (パネルの左上が原点): 枠の内側は x 14〜72
        private static readonly RectInt Btn = new RectInt(15, 154, 56, 24), RerollR = new RectInt(15, 122, 56, 16),
            ItemR = new RectInt(15, 140, 27, 12), FleeR = new RectInt(44, 140, 27, 12);      // 消耗品 / 逃げる (配線の判断の間だけ)
        private static RectInt DieR(int i) => new RectInt(15, 20 + i * 24, 18, 18);
        private static RectInt TermR(int i) => new RectInt(36, 20 + i * 24, 36, 18);

        public Game2DBattleSim sim;
        public Game2DBattleFx fx;
        public Game2DScreen screen;
        [Tooltip("端子のボタン (攻撃・防御・充電) と決定ボタン。 枠・ダイスなどの共通の絵は Game2DKitAssets")]
        public Texture2D btnAtkTex, btnBlkTex, btnChgTex, btnGoTex, btnPlainTex;

        [SerializeField] private RectTransform _left, _right, _top;
        private Texture2D _tl, _tr, _tt;
        private int _drawn = -1; private float _slide;
        private KitSrc _bAtk, _bBlk, _bChg, _bGo, _bPlain; private bool _srcInit;

        private static readonly Color32 Ink = Game2DKit.Ink, Dim = Game2DKit.Dim, Dark = Game2DKit.Dark, Off = Game2DKit.Off, Well = Game2DKit.Well,
            Atk = Game2DKit.Red, Blk = Game2DKit.Blue, Chg = Game2DKit.Gold, Spc = Game2DKit.Amber, Hp = new Color32(0xd1, 0x50, 0x3c, 255);
        private static readonly Color32[] TermCol = { Atk, Blk, Chg, Spc };
        private static readonly string[] TermName = { "ATK", "BLK", "CHG", "SPC" };

        /// <summary>見た目を作る (builder から 1 度呼ぶ)。 親は 480×270 の格子の枠。</summary>
        public void Build()
        {
            _left = Make("left", Left, out _tl); _right = Make("right", Right, out _tr); _top = Make("top", Top, out _tt);
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

        private void OnDestroy() { if (_tl != null) Destroy(_tl); if (_tr != null) Destroy(_tr); if (_tt != null) Destroy(_tt); }

        private Game2DSpring _spL, _spR, _spT; private bool _spInit;
        /// <summary>入り・出の途中を含めて、 どれかが見えている。</summary>
        public bool Visible => !_spInit || !(_spL.tgt < 0.5f && _spR.tgt < 0.5f && _spT.tgt < 0.5f && _spL.x < 0.005f && _spR.x < 0.005f && _spT.x < 0.005f && _spL.Settled && _spR.Settled && _spT.Settled);

        /// <summary>戦闘の GUI をばねで出し入れする (毎フレーム)。 入る順は 左 → 上 → 右 で、 少しずつずらす。 行き過ぎは 3 dot 以内。</summary>
        public void Drive(bool on, float dt)
        {
            if (_left == null) return;
            if (!_spInit) { _spL.Set(0); _spR.Set(0); _spT.Set(0); _spInit = true; }
            float t = on ? 1f : 0f;
            _spL.Go(t, on ? 0.03f : 0f); _spT.Go(t, on ? 0.07f : 0.03f); _spR.Go(t, on ? 0.12f : 0.06f);
            _spL.Step(dt); _spT.Step(dt); _spR.Step(dt);
            _slide = _spL.x;
            _left.anchoredPosition = new Vector2(Left.x - Mathf.Round((1f - _spL.Value(100f)) * 100f), -Left.y);
            _right.anchoredPosition = new Vector2(Right.x + Mathf.Round((1f - _spR.Value(100f)) * 100f), -Right.y);
            _top.anchoredPosition = new Vector2(Top.x, -Top.y + Mathf.Round((1f - _spT.Value(50f)) * 50f));
        }

        private void Update()
        {
            if (_left == null || sim == null || !Game2DBattleSim.Active) return;
            int ehp = fx != null ? fx.EnemyHp : 0, ver = sim.version * 31 + ehp * 7 + sim.charge;
            if (ver != _drawn) { _drawn = ver; Redraw(); }
            if (_slide > 0.95f && !Game2DModal.Active && screen != null && Input.GetMouseButtonDown(0) && Game2DPointer.TryDot(screen, out var d)) Click(d);
        }

        private bool In(Vector2 d, RectInt panel, RectInt r) => d.x >= panel.x + r.x && d.x < panel.x + r.x + r.width && d.y >= panel.y + r.y && d.y < panel.y + r.y + r.height;

        private void Click(Vector2 d)
        {
            for (int i = 0; i < 3; i++) if (In(d, Left, DieR(i)) || In(d, Left, TermR(i))) { sim.CycleWire(i); return; }
            if (In(d, Left, RerollR)) { sim.Reroll(); return; }
            if (In(d, Left, ItemR)) { OpenItems(); return; }
            if (In(d, Left, FleeR)) { if (_fleeArmed) { _fleeArmed = false; if (!sim.Flee()) Game2DSfx.Play("ui_click"); } else if (sim.CanFlee) { _fleeArmed = true; _drawn = -1; } return; }
            if (In(d, Left, Btn)) { if (sim.ButtonEnabled) sim.Button(); }
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
            InitSrc();
            Ensure(ref _tl, _left, Left); Ensure(ref _tr, _right, Right); Ensure(ref _tt, _top, Top);
            DrawLeft(_tl); DrawRight(_tr); DrawTop(_tt);
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
            if (live && sim.rolled) { var w = sim.GetWired(); a = w.atk + "=" + (w.atk - w.aDice) + "+" + w.aDice; b = w.blk.ToString(); c = w.chg + " (T" + sim.charge + ")"; }
            p.Text("ATK " + a, 15, 93, Atk); p.Text("BLK " + b, 15, 102, Blk); p.Text("CHG " + c, 15, 111, Chg);
            // 振り直し: 充電を払って全ダイスを振り直す (払えない・判断の外では暗く)
            bool rr = live && sim.CanRerollNow;
            if (Game2DKit.Tab != null) p.NineSlice(Game2DKit.Tab, 5, Well, RerollR.x, RerollR.y, RerollR.width, RerollR.height);
            else p.Box(RerollR.x, RerollR.y, RerollR.width, RerollR.height, Off, Dark);
            p.TextC(live && sim.canReroll ? "REROLL -" + sim.rerollCost : "REROLL", RerollR, rr ? Chg : Dim);
            // 消耗品 / 逃げる (逃げるは 2 度押しで確定: SURE? → 押すとこのターンは攻撃せずに離脱)
            if (!live || !sim.CanFlee) _fleeArmed = false;
            bool itemOn = live && sim.CanOperate && CombatItems().Count > 0, fleeOn = live && sim.CanFlee;
            SmallBtn(p, ItemR, "ITEM", itemOn, Chg);
            SmallBtn(p, FleeR, _fleeArmed ? "SURE?" : "FLEE", fleeOn, _fleeArmed ? Atk : Ink);
            // 決定ボタン
            bool en = live && sim.ButtonEnabled; string lab = live ? sim.ButtonLabel : "...";
            if (_bGo != null) p.Sprite(_bGo, Btn.x, Btn.y, Btn.width, Btn.height);
            else p.Box(Btn.x, Btn.y, Btn.width, Btn.height, en ? new Color32(0x6d, 0x3a, 0x16, 255) : Off, Dark);
            p.TextC(lab, Btn, en ? Ink : Dim, 2);
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
            p.Text(known ? "ATK " + sim.eatk : "ATK ??", 14, 38, known ? Atk : Dim);
            if (known) p.Text("ROLL " + sim.EnemyDiceSum, 14, 48, Dim);
            p.Box(14, 60, 58, 14, Off, Dark); p.Text(known ? "INTENT: ATK" : "INTENT: ...", 17, 65, Ink);
            if (live) p.Text("T" + sim.turn + " STAGE " + sim.Stage, 14, 80, Dim);
            // 敵に乗っている毒・出血 (仮のアイコン + スタック数)。 乗っているものだけを左から詰めて並べる
            var ctx = live ? ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context : null;
            if (ctx != null)
            {
                int x = 14;
                if (ctx.EnemyPoison > 0) x = DrawStatus(p, x, 89, false, ctx.EnemyPoison);
                if (ctx.enemyBleedStacks > 0) DrawStatus(p, x, 89, true, ctx.enemyBleedStacks);
            }
            p.Apply(t);
        }

        // 仮のアイコン (7×7): 毒 = 泡 / 出血 = 滴。 # = 色・o = 光
        private static readonly string[] IconPoison = { "..###..", ".#o###.", "#o#####", "#######", "#######", ".#####.", "..###.." };
        private static readonly string[] IconBleed = { "...#...", "..###..", ".#o###.", ".#o###.", "#######", "#######", ".#####." };
        private static readonly Color32 PoisonCol = new Color32(0x8e, 0xea, 0x3f, 255), BleedCol = new Color32(0xe0, 0x4a, 0x38, 255), IconHi = new Color32(0xff, 0xf6, 0xe0, 255);

        /// <summary>状態 1 つ (アイコン + 数)。 戻り値 = 次を置く x。</summary>
        private static int DrawStatus(KitPix p, int x, int y, bool bleed, int stacks)
        {
            var icon = bleed ? IconBleed : IconPoison; var col = bleed ? BleedCol : PoisonCol;
            for (int j = 0; j < 7; j++) for (int i = 0; i < 7; i++)
                {
                    char ch = icon[j][i];
                    if (ch == '#') p.Set(x + i, y + j, col); else if (ch == 'o') p.Set(x + i, y + j, IconHi);
                }
            string n = stacks.ToString();
            p.Text(n, x + 9, y + 1, col);
            return x + 9 + Game2DKit.TextWidth(n, 1) + 6;
        }

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
            p.Text("CHARGE", 14, 15, Chg);
            for (int i = 0; i < 10; i++) DrawLamp(p, 50 + i * 10, 12, 10, i < pips, Chg);
            string esc = live ? "ESC T" + sim.turn + (sim.NextStageIn >= 0 ? " NEXT " + sim.NextStageIn : " MAX") : "ESC";
            p.Text(esc, 160, 15, Dim);
            if (live) for (int i = 0; i < 5; i++) DrawLamp(p, 222 + i * 10, 12, 10, i < sim.Stage, Atk);
            int cur = fx != null ? fx.EnemyHp : 56, max = fx != null ? fx.EnemyMax : 80;
            p.Text("ENEMY", 14, 28, Ink); p.GaugeBar(Game2DKit.Gauge, 48, 26, 190, 10, Mathf.Clamp01(cur / (float)Mathf.Max(1, max)), Hp);
            p.Text(cur + "/" + max, 244, 28, Ink);
            p.Apply(t);
        }
    }
}

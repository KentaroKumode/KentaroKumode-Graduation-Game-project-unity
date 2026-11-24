using System.Collections.Generic;
using GameLoop;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 航行の計器 (2026-11-24 に作り直し・neon の配置 §A-1)。 液晶の安全域 (<see cref="LcdSafeArea"/>) の内側に、 戦闘・イベントの画面と同じ平たい暗い板と日本語の字で並べる。
    /// <list type="bullet">
    /// <item>上の帯: 天気と嵐の時計 / 経過ターン・近道の残り・2 行目 (説明・捕捉中の警告・時限効果) / 所持金 (増減で点滅し、 増えた時は金の粒が上がって硬貨の音)</item>
    /// <item>右の列: [測量] [囮罠] [待つ] と、 航行アイテムの 3 枠 (V / B / N・〈前哨の旗〉を立てる)</item>
    /// <item>下の帯: HP (戦闘の画面の左下と同じ位置・同じ棒 = 戦闘に入っても HP だけが同じ所に残る) と物資 (行き先の見込みの分が点滅)</item>
    /// <item>左の列: 所持品 (2 列 × 5 段・押すと持ち物の画面)</item>
    /// </list>
    /// 旧い筐体の部品 (GOLD の入れ物・TRAP/FLARE/RECON のボタン盤・円いレーダー・資源バー・数字ドラム) は 2026-11-24 に削除 (GAME.md §24)。 戦闘中は 4 方向へ引っ込む (<see cref="Drive"/>)。
    /// <para>終点 (ボス・〈門〉) へ踏み出す前の確認の窓 (<see cref="ConfirmExit"/>)・持ち物の画面もここ。</para>
    /// </summary>
    public class Game2DNavExtras : MonoBehaviour
    {
        public Game2DScreen screen;
        public Game2DModal modal;
        [SerializeField] private RectTransform _top, _left, _right, _bottom;
        private Texture2D _tt, _tl, _tr, _tb;
        // 位置 (GUI の 480×270 の dot・左上が原点)。 全部 LcdSafeArea (x 16〜464・y 9〜259) の内側
        private static readonly RectInt TopR = new RectInt(16, 9, 448, 28);
        /// <summary>左の列: 所持品。 上の帯の下・下の帯の上。</summary>
        private static readonly RectInt LeftR = new RectInt(16, 42, 40, 122);
        /// <summary>右の列: 操作 3 つと航行アイテムの 3 枠。</summary>
        private static readonly RectInt RightR = new RectInt(416, 42, 48, 142);
        /// <summary>下の帯: HP と物資。 HP の数と棒は戦闘の画面の左下 (<see cref="BattleLcdLayout.MBarX0"/>・<see cref="BattleLcdLayout.MBarY"/>・<see cref="BattleLcdLayout.MNumY"/>) と同じ所 (棒の黒い縁の分だけ左へ 1 dot)。</summary>
        private static readonly RectInt BottomR = new RectInt(BattleLcdLayout.MBarX0 - 1, BattleLcdLayout.MNumY - 2, 184, BattleLcdLayout.MBarY + 7 - (BattleLcdLayout.MNumY - 2));
        // 上の帯の中の区切り (帯の左上が原点)
        private const int WeatherW = 86, StatusX = 90, StatusW = 252, GoldX = 346, GoldW = 102;
        // 右の列の中: 操作のボタン (幅いっぱい・高さ 18・送り 21) → 航行アイテムの枠 (20×20・送り 25)
        private const int BtnH = 18, BtnStep = 21, Btns = 3, SlotTop = 72;
        private const int SlotSize = 20, SlotPitch = 25;
        /// <summary>枠のキー。 W は視点の移動 (WASD) と重なるので、 設計 (Q / W / E) から V / B / N に変えた (2026-11-11: Q / E も視点の移動の隣で紛らわしいので下の段へ) (F は測量・L は囮罠・数字は移動)。</summary>
        private static readonly KeyCode[] SlotKeys = { KeyCode.V, KeyCode.B, KeyCode.N };
        private static readonly string[] SlotKeyNames = { "V", "B", "N" };
        private static readonly string[] BtnNames = { "測量", "囮罠", "待つ" };
        private int _hash = int.MinValue; private float _next; private float _e;
        private static bool _shown;
        private static Game2DNavExtras _instance;
        private int _invPage;
        /// <summary>持ち物の画面の頁: 0 = 升目 (2026-11-13) / 2 = ステータス (2026-11-09)。 ステータスの頁の _invPage は挑戦カードの頁。</summary>
        private int _invTab;
        private Game2DMapView _view;

        /// <summary>この dot (GUI の 480×270) がボタン・計器の上か (地図のクリック・ドラッグに渡さない)。</summary>
        public static bool Consumes(Vector2 d) => _shown && (In(d, TopR) || In(d, LeftR) || In(d, RightR) || In(d, BottomR));
        private static bool In(Vector2 d, RectInt r) => d.x >= r.x && d.x < r.x + r.width && d.y >= r.y && d.y < r.y + r.height;

        public void Build()
        {
            _top = Make("top", TopR); _left = Make("left", LeftR); _right = Make("right", RightR); _bottom = Make("bottom", BottomR);
        }

        private void Awake() { _instance = this; }

        /// <summary>上の帯 (天気・経過ターン・所持金) の RectTransform。 手引きの強調 (issue #109) が範囲を読む。 無ければ null。</summary>
        public static RectTransform TopBar => _instance != null ? _instance._top : null;

        /// <summary>シーンを builder で作り直していない時に、 新しく足した欄 (下の帯) を作り、 旧い欄 (状態の帯・2026-11-24 に上の帯へまとめた) を消す。</summary>
        private void EnsureParts()
        {
            if (_top == null) return;
            if (_right == null) _right = Make("right", RightR);
            if (_bottom == null) _bottom = Make("bottom", BottomR);
            var old = transform.Find("status"); if (old != null) Destroy(old.gameObject);
        }

        private RectTransform Make(string name, RectInt r)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rt = (RectTransform)go.transform; rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(r.x, -r.y); rt.sizeDelta = new Vector2(r.width, r.height);
            var ri = go.AddComponent<RawImage>(); ri.raycastTarget = false; ri.enabled = false;
            return rt;
        }

        private void OnDestroy()
        {
            foreach (var t in new[] { _tt, _tl, _tr, _tb }) if (t != null) Destroy(t);
            if (_instance == this) _instance = null;
        }

        /// <param name="e">引っ込み具合 0 = 出ている / 1 = 画面の外 (ばね。 行き過ぎはそのまま渡す)</param>
        public void Drive(float e)
        {
            _e = e;
            if (_top == null) return;
            EnsureParts();
            _top.anchoredPosition = new Vector2(TopR.x, -TopR.y + Mathf.Round(e * 40f));
            _bottom.anchoredPosition = new Vector2(BottomR.x, -BottomR.y - Mathf.Round(e * 40f));
            _left.anchoredPosition = new Vector2(LeftR.x - Mathf.Round(e * 60f), -LeftR.y);
            _right.anchoredPosition = new Vector2(RightR.x + Mathf.Round(e * 60f), -RightR.y);
            bool show = e < 0.995f; _shown = e < 0.05f;
            foreach (var rt in new[] { _top, _left, _right, _bottom }) rt.gameObject.SetActive(show);
        }

        // ── 所持金 (2026-11-24・旧 GOLD の入れ物の振り込み) ──
        //   増えた時: 数字が明るく点滅し、 金の粒が数字から上がる。 硬貨の音 (Game2DSfx.DiceCoin・増えた量で段階) はここだけが鳴らす (#322)。 減った時は赤く点滅するだけ。

        private RunState _goldRun; private int _goldShown = -1; private float _goldFlashUntil; private bool _goldUp;
        private struct Mote { public float x, y, vy, t0; }
        private readonly List<Mote> _motes = new List<Mote>();
        private const float GoldFlashSec = 0.6f, MoteLife = 0.7f;

        /// <summary>今の所持金を知らせる (Game2DHud が毎フレーム呼ぶ・戦闘中も)。 ランが替わった時は数え直す (点滅・音を出さない)。</summary>
        public void NoteGold(RunState run)
        {
            if (run == null) return;
            if (run != _goldRun || _goldShown < 0) { _goldRun = run; _goldShown = run.coins; return; }
            int d = run.coins - _goldShown;
            if (d == 0) return;
            _goldShown = run.coins; _goldUp = d > 0; _goldFlashUntil = Time.unscaledTime + GoldFlashSec;
            if (d <= 0) return;
            Game2DSfx.DiceCoin(d);
            int n = Mathf.Clamp(2 + d / 5, 2, 12), right = GoldX + GoldW - 6;
            for (int i = 0; i < n; i++)
                _motes.Add(new Mote { x = right - 4 - ((i * 7) % 28), y = 18, vy = 14f + (i * 5) % 9, t0 = Time.unscaledTime + i * 0.04f });
        }

        private bool Animating => Time.unscaledTime < _goldFlashUntil || _motes.Count > 0;

        private void Update()
        {
            SyncInventoryInput();
            if (_top == null || _e > 0.5f) return;
            var gm = GameManager.Instance; if (gm == null) return;
            var sim = gm.CurrentPhase == GameManager.GamePhase.MapNavigation ? gm.FreeMap : null;
            var run = gm.Run;
            bool free = sim != null && !Game2DModal.Active && !Game2DLoadingScreen.Blocking;
            _hoverSlot = -1; _hoverBtn = -1;
            if (free && screen != null && Game2DPointer.TryDot(screen, out var hd))
            {
                for (int i = 0; i < 3; i++) if (In(hd, SlotRect(i))) _hoverSlot = i;
                for (int i = 0; i < Btns; i++) if (In(hd, BtnRect(i))) _hoverBtn = i;
            }
            if (free && Input.GetMouseButtonDown(0) && screen != null && Game2DPointer.TryDot(screen, out var dot))
            {
                if (_hoverBtn >= 0) PressButton(gm, sim, run, _hoverBtn);
                else if (In(dot, LeftR)) OpenInventory(gm);
                else if (_hoverSlot >= 0) UseSlot(gm, _hoverSlot);
            }
            if (free) for (int i = 0; i < SlotKeys.Length; i++) if (Input.GetKeyDown(SlotKeys[i])) UseSlot(gm, i);
            int floor = run != null ? run.currentFloor : 1, passives = run?.ownedPassiveItems?.Count ?? 0, cons = run?.ownedConsumables?.Count ?? 0;
            int weather = sim != null ? (int)sim.Weather : 0, foe = sim != null && sim.HasFoe && sim.FoeAlive ? (int)sim.FoeMode : -1;
            // まとめて進んだターン (イベント・多めに休む・雪の前線): 「+N」を 2.5 秒だけ出す (#21 の待ち時間の代わり)
            if (sim != null)
            {
                if (_lastSim != sim) { _lastSim = sim; _lastTurn = sim.Turn; }
                if (sim.Turn - _lastTurn >= 2) { _jumpN = sim.Turn - _lastTurn; _jumpUntil = Time.unscaledTime + 2.5f; }
                _lastTurn = sim.Turn;
            }
            if (_view == null) _view = FindObjectOfType<Game2DMapView>();
            float pre = sim != null && _view != null ? _view.SupplyPreview : 0f;
            bool jump = Time.unscaledTime < _jumpUntil;
            bool chase = sim != null && sim.FoeAlive && sim.FoeMode == FoeMode.Chase;
            int blink = chase ? Mathf.FloorToInt(Time.unscaledTime * 2.5f) & 1 : 0;      // 捕捉中の警告は 2.5 回/秒で明滅
            int preBlink = pre > 0f ? 1 + (Mathf.FloorToInt(Time.unscaledTime / 0.18f) & 1) : 0;      // 物資の見込みの点滅 (180ms ごと)
            int h = floor * 7 + passives * 31 + cons * 131 + (run != null ? run.playerHP * 389 + run.playerMaxHP * 13 + run.provision * 977 + run.provisionCap * 53 + run.coins * 7333 : 0)
                    + weather * 17 + (foe + 2) * 1009 + (sim != null ? sim.Version * 7919 : 0) + blink * 104729 + (_hoverSlot + 2) * 15485863 + (_hoverBtn + 2) * 179424673
                    + (jump ? 31 : 0) + preBlink * 49979687 + (Time.unscaledTime < _pressUntil ? 3 + _pressBtn * 5 : 0);
            if (h == _hash && Time.unscaledTime < _next && !Animating) return;
            _hash = h; _next = Time.unscaledTime + 0.5f;
            RedrawTop(gm, sim, run, weather, blink == 0);
            RedrawLeft(run, passives, cons);
            RedrawRight(gm, sim, run);
            RedrawBottom(sim, run, pre, preBlink == 1);
        }

        // ── 上の帯: 天気・経過ターン・捕捉中の警告 (issue #69 / #72)・所持金 ──
        //   状態の 1 行目 = 経過ターン (「ターン」とは書かない) と近道の残り (イベント「近道+N」・ただで動ける回数)。
        //   2 行目 = 優先順に 「ボタン・枠の説明 (乗せている時)」→「捕捉中の ETA」→「時限効果」。
        //   まとめて進んだ分 (イベント・多めに休む) はターン数の横の「+N」(gui-needed.md #21)。

        private void RedrawTop(GameManager gm, FreeMapSim sim, RunState run, int weather, bool blinkOn)
        {
            Ensure(ref _tt, _top, TopR);
            var p = new KitPix(TopR.width, TopR.height);
            // ── 天気 ──
            Plate(p, 0, 0, WeatherW, TopR.height);
            string wn = weather >= 0 && weather < WeatherNames.Length ? WeatherNames[weather] : "W" + weather;
            p.AnyIcon(weather >= 0 && weather < WeatherIcons.Length ? WeatherIcons[weather] : "still", 3, 5, 18, Game2DKit.Ink);
            bool storm = sim != null && sim.StormMode;
            if (storm && weather == 0) wn = "嵐";                                                   // 嵐の層の種類なし (素の嵐・2026-11-13)
            p.Text("天気", 24, 2, Game2DKit.Dim); p.Text(wn, 48, 2, weather == 0 && !storm ? Game2DKit.Ink : Game2DKit.Amber);
            if (storm)
            {
                // 嵐 (2026-11-13): 2 行目に嵐の時計 (静かな間は「不安定の累計/崩れ始める境目」、 崩れ始めたら段と次の段までの不安定化ポイント)
                int ph = sim.StormPhase, n = sim.StormTurnsToNext;
                p.Text("嵐", 24, 15, Game2DKit.Dim);
                string v = ph == 0 ? (sim.InstabilityMode ? sim.Instability + "/" + FreeMapParams.InstabilityPhase1AtOf(sim.Floor) : "まで" + n) : ph >= 4 ? "段4" : "段" + ph + " " + n;
                p.Text(v, 40, 15, ph == 0 ? Game2DKit.Ink : Game2DKit.Amber);
            }
            // ── 経過ターンと 2 行目 ──
            if (sim != null)
            {
                int x0 = StatusX;
                Plate(p, x0, 0, StatusW, TopR.height);
                p.Text("ターン", x0 + 5, 2, Game2DKit.Dim);
                string tn = Mathf.Min(9999, sim.Turn).ToString(); int tx = x0 + 5 + Game2DJpFont.Width("ターン") + 4;
                p.Text(tn, tx, 2, Game2DKit.Ink);
                if (Time.unscaledTime < _jumpUntil) p.Text("+" + _jumpN, tx + Game2DJpFont.Width(tn) + 4, 2, Game2DKit.Amber);      // まとめて進んだ分
                string flare = sim.FlareOn ? "測量 残り" + sim.FlareTurnsLeft : null;
                string cut = sim.FreeMovesLeft > 0 ? "近道 " + sim.FreeMovesLeft + "回" : null;      // 時限バフ「近道」の残り (時限バフの「(N回)」と同じ数え方)
                string right = flare != null && cut != null ? cut + " " + flare : flare ?? cut;
                if (right != null) p.TextR(right, x0 + StatusW - 5, 2, Game2DKit.Amber);
                string line = null; var col = Game2DKit.Dim;
                if (_hoverBtn >= 0) { line = BtnHint(sim, run, _hoverBtn); col = Game2DKit.Ink; }
                else if (_hoverSlot >= 0) { line = SlotHint(gm, sim, run, _hoverSlot); col = Game2DKit.Ink; }
                else if (sim.FoeAlive && sim.FoeMode == FoeMode.Chase)
                {
                    // 捕捉中: こちらが動かなかった場合に最短で何ターンで捕まるか (§5-9・FreeMapSim.Eta)。 誰が来るかは出さない (性質は明かさない)
                    var eta = sim.Eta();
                    line = eta.Valid ? "捕捉中 あと" + eta.turns + "ターンで捕まる" : "捕捉中";
                    col = blinkOn ? Game2DKit.Red : Game2DKit.Off;
                }
                else if (NavItems.FineBearing(run))
                {
                    var st = sim.Stone();      // 〈羅盤の針〉: 魔石の方角を 8 方位の字で
                    if (st.level > 0 && st.bearing >= 0) { line = "魔石 " + StoneReading.BearingNames[st.bearing]; col = st.level >= 2 ? Game2DKit.Amber : Game2DKit.Dim; }
                }
                if (string.IsNullOrEmpty(line))
                {
                    // 時限バフ / デバフ (説明を持つもの・2026-11-13・〈徒労〉〈開眼〉): 名前・残り・説明。 ほかに出す行が無い時だけ (いちばん後ろの優先)
                    string tl = TimedEffectLine(run);
                    if (tl != null) { line = tl; col = Game2DKit.Amber; }
                }
                if (!string.IsNullOrEmpty(line)) p.Text(Game2DJpFont.Clip(line, StatusW - 10), x0 + 5, 15, col);
            }
            // ── 所持金 ──
            Plate(p, GoldX, 0, GoldW, TopR.height);
            p.Text("所持金", GoldX + 5, 2, Game2DKit.Dim);
            if (run != null)
            {
                float now = Time.unscaledTime;
                bool flash = now < _goldFlashUntil && (Mathf.FloorToInt((_goldFlashUntil - now) * 10f) & 1) == 0;      // 0.1 秒ごとに明滅
                var gc = flash ? (_goldUp ? Game2DKit.Ivory : Game2DKit.Red) : Game2DKit.Gold;
                p.TextR(run.coins + "G", GoldX + GoldW - 5, 15, gc);
                for (int i = _motes.Count - 1; i >= 0; i--)
                {
                    var m = _motes[i]; float t = now - m.t0;
                    if (t > MoteLife) { _motes.RemoveAt(i); continue; }
                    if (t < 0f) continue;
                    int mx = Mathf.RoundToInt(m.x), my = Mathf.RoundToInt(m.y - m.vy * t);
                    p.Rect(mx, my, 1, 1, t < MoteLife * 0.6f ? Game2DKit.Gold : Game2DKit.Dim);      // 粒は 1 dot (大きさは変えない・終わりは暗く)
                }
            }
            p.Apply(_tt);
        }

        /// <summary>持っている時限デバフ → 時限バフの順で、 説明を持つ最初の 1 つを「〈名〉残りN 説明」の 1 行にする (無ければ null)。</summary>
        private static string TimedEffectLine(RunState run)
        {
            if (run == null) return null;
            foreach (var d in new[] { run.timedDebuffs, run.timedBuffs })
            {
                if (d == null) continue;
                foreach (var kv in d)
                {
                    if (kv.Value <= 0) continue;
                    string desc = EventSystem.TimedEffects.TimedEffectRegistry.DescriptionOf(kv.Key);
                    if (desc != null)   // 〈徒労〉の別の形 (LayerLong・2026-11-14) は減らないので残りを出さない
                        return EventSystem.TimedEffects.BattleBoonRule.ShowsCharges(kv.Key, GameLoop.GameHooks.ToilMode)
                            ? "〈" + kv.Key + "〉残り" + kv.Value + " " + desc : "〈" + kv.Key + "〉 " + desc;
                }
            }
            return null;
        }

        // ── 右の列: 操作 [測量] [囮罠] [待つ] (2026-11-24・旧 TRAP / FLARE / RECON のボタン盤と上の帯の [WAIT]) ──
        //   押すのはキーと同じ操作の関数 (音はその関数の合図が鳴らす・#321)。 押せない時は失敗の音 (#324)。

        private int _hoverBtn = -1, _pressBtn = -1; private float _pressUntil;

        private static RectInt BtnRect(int i) => new RectInt(RightR.x, RightR.y + i * BtnStep, RightR.width, BtnH);

        private static int CountLures(RunState run)
        {
            int n = 0;
            if (run?.ownedConsumables != null) foreach (var c in run.ownedConsumables) if (c == ItemIds.Lure) n++;
            return n;
        }

        private static bool CanAct(GameManager gm, FreeMapSim sim) => gm.CurrentPhase == GameManager.GamePhase.MapNavigation && sim != null && !sim.InFight && !sim.Dead && !Game2DLoadingScreen.Blocking;

        private static bool BtnEnabled(GameManager gm, FreeMapSim sim, RunState run, int i)
        {
            if (run == null || !CanAct(gm, sim)) return false;
            switch (i)
            {
                case 0: return !sim.FlareOn && run.provision >= FreeMapParams.FlareSupply;
                case 1: return CountLures(run) > 0;
                default: return true;
            }
        }

        private static string BtnHint(FreeMapSim sim, RunState run, int i)
        {
            switch (i)
            {
                case 0: return "測量 物資" + FreeMapParams.FlareSupply + "・" + FreeMapParams.FlareTurns + "ターンのあいだ" + FreeMapParams.FlareRange + "歩先まで見る (F)";
                case 1: return "囮罠 今いる点に置く 残り" + CountLures(run) + "個 (L)";
                default: return "待つ その場で1ターン (Space)";
            }
        }

        private void PressButton(GameManager gm, FreeMapSim sim, RunState run, int i)
        {
            _pressBtn = i; _pressUntil = Time.unscaledTime + 0.11f;
            if (!BtnEnabled(gm, sim, run, i)) { Game2DSfx.Error(); return; }
            switch (i)
            {
                case 0: gm.UseFlare(); break;
                case 1: Consumables.Use(run, ItemIds.Lure); break;      // 音は置けた時だけ (GameManager.NavFx.Lure・#323)
                default: gm.WaitTurn(); break;                         // 音は WaitTurn の合図 (GameManager.NavFx.Wait・キーの Space と同じ・#321)
            }
            _hash = int.MinValue;
        }

        // ── 航行アイテムの 3 枠 (issue #10・gui-needed.md #20) ──
        //   枠に入るのは「使う」か「回数のある」航行アイテムだけ: 〈前哨の旗〉(押すと立てる)・〈煙玉〉(捕捉された時に自動で弾ける・押しても何もしない)。
        //   ほかの品は持っているだけで効くので、 持ち物の画面 (秘宝) で読む。 絵は無いので、 名前の最後の 1 字で出す。

        private int _hoverSlot = -1;
        private FreeMapSim _lastSim; private int _lastTurn, _jumpN; private float _jumpUntil;

        private static RectInt SlotRect(int i) => new RectInt(RightR.x + 16, RightR.y + SlotTop + i * SlotPitch, SlotSize, SlotSize);

        /// <summary>枠に入る品 (順番どおり・最大 3)。</summary>
        private static List<string> SlotItems(RunState run)
        {
            var l = new List<string>();
            if (NavItems.Has(run, ItemIds.NavOutpostFlag)) l.Add(ItemIds.NavOutpostFlag);
            if (NavItems.Has(run, ItemIds.NavSmokeBall)) l.Add(ItemIds.NavSmokeBall);
            return l;
        }

        /// <summary>その枠の品がまだこの層で使えるか。</summary>
        private static bool SlotReady(GameManager gm, FreeMapSim sim, string id)
        {
            if (sim == null) return false;
            if (id == ItemIds.NavOutpostFlag) return sim.Beacon < 0;
            if (id == ItemIds.NavSmokeBall) return sim.SmokeLeft > 0 || sim.Perks.smokeCharges == 0;      // 層の途中で得た直後は、 次の操作で回数が入る
            return false;
        }

        private static string SlotHint(GameManager gm, FreeMapSim sim, RunState run, int i)
        {
            var items = SlotItems(run);
            if (i >= items.Count) return "空き (" + SlotKeyNames[i] + ")";
            string id = items[i]; bool ready = SlotReady(gm, sim, id);
            if (id == ItemIds.NavOutpostFlag) return ready ? id + " 押すとここに立てる" : id + " この層は立て済み";
            if (id == ItemIds.NavSmokeBall) return ready ? id + " 見つかった時に自動" : id + " この層は使用済み";
            return id;
        }

        private void UseSlot(GameManager gm, int i)
        {
            var items = SlotItems(gm.Run);
            if (i < 0 || i >= items.Count) return;
            if (items[i] == ItemIds.NavOutpostFlag)
            {
                if (gm.UseOutpostFlag()) Game2DSfx.Play("ui_confirm"); else Game2DSfx.Play("ui_click", 0.4f);
                _hash = int.MinValue;
            }
            else Game2DSfx.Play("ui_click", 0.4f);      // 〈煙玉〉は自動 (押しても何もしない)
        }

        private void RedrawRight(GameManager gm, FreeMapSim sim, RunState run)
        {
            if (_right == null) return;
            Ensure(ref _tr, _right, RightR);
            var p = new KitPix(RightR.width, RightR.height);
            for (int i = 0; i < Btns; i++)
            {
                var r = BtnRect(i); int y = r.y - RightR.y;
                bool on = BtnEnabled(gm, sim, run, i), hv = i == _hoverBtn, pr = i == _pressBtn && Time.unscaledTime < _pressUntil;
                p.Box(0, y, RightR.width, BtnH, pr ? Game2DKit.Dark : Game2DKit.Off, hv && on ? Game2DKit.Amber : on ? Game2DKit.Dim : Game2DKit.Faint);
                int ty = y + (BtnH - Game2DJpFont.Cell) / 2 + 1;
                p.Text(BtnNames[i], 5, ty, on ? Game2DKit.Ink : Game2DKit.Faint);
                if (i == 1) p.TextR(Mathf.Min(99, CountLures(run)).ToString(), RightR.width - 4, ty, on ? Game2DKit.Amber : Game2DKit.Faint);      // 囮罠の残り
            }
            var items = SlotItems(run);
            for (int i = 0; i < 3; i++)
            {
                var r = SlotRect(i); int x = r.x - RightR.x, y = r.y - RightR.y;
                bool has = i < items.Count, ready = has && SlotReady(gm, sim, items[i]), hv = i == _hoverSlot;
                p.Text(SlotKeyNames[i], 4, y + (SlotSize - Game2DJpFont.Cell) / 2 + 1, hv ? Game2DKit.Ink : Game2DKit.Dim);
                p.Box(x, y, SlotSize, SlotSize, Game2DKit.Off, hv ? Game2DKit.Amber : has ? Game2DKit.Dim : Game2DKit.Faint);
                if (!has) continue;
                string g = items[i].Substring(items[i].Length - 1);      // 名前の最後の 1 字 (旗・玉)。 id の綴りから効果は読まない ── 字を出すだけ
                p.TextC(g, new RectInt(x, y, SlotSize, SlotSize), ready ? Game2DKit.Ink : Game2DKit.Faint);
                if (ready) p.Rect(x + SlotSize - 4, y + SlotSize - 4, 2, 2, Game2DKit.Amber);      // 使える印
            }
            p.Apply(_tr);
        }

        // ── 下の帯: HP と物資 ──
        //   HP は戦闘の画面の左下と同じ規則 (数 = 骨色・棒 = 黒い縁 + 暗い地 + 緑・上の 1 行は明るい緑)。 物資は同じ形の棒を右に並べ、 行き先の見込みの分を点滅させて数の横に −N。

        private static readonly Color32 BarEdge = new Color32(10, 8, 10, 255), BarEmpty = new Color32(38, 30, 30, 255), Bone = new Color32(242, 236, 216, 255),
            HpC = new Color32(104, 170, 108, 255), HpHi = new Color32(160, 214, 160, 255), SupC = new Color32(0xd8, 0xa8, 0x2c, 255), SupHi = new Color32(0xf0, 0xc9, 0x3c, 255);
        /// <summary>下の帯の地 (neon の配置 §A-1 の暗い平たい帯 #15171C)。</summary>
        private static readonly Color32 BandBg = new Color32(0x15, 0x17, 0x1c, 255);
        private const int BarW = BattleLcdLayout.MBarX1 - BattleLcdLayout.MBarX0, SupX = 104;

        private void RedrawBottom(FreeMapSim sim, RunState run, float preFrac, bool blinkOn)
        {
            if (_bottom == null) return;
            Ensure(ref _tb, _bottom, BottomR);
            var p = new KitPix(BottomR.width, BottomR.height);
            p.Rect(0, 0, BottomR.width, BottomR.height, BandBg);      // 地図の点や道の上でも字が読めるよう、 平たい暗い地を敷く
            if (run == null) { p.Apply(_tb); return; }
            int ny = BattleLcdLayout.MNumY - BottomR.y, by = BattleLcdLayout.MBarY - BottomR.y, hx = BattleLcdLayout.MBarX0 - BottomR.x;
            int max = Mathf.Max(1, run.playerMaxHP), hp = Mathf.Clamp(run.playerHP, 0, max);
            p.Text(hp.ToString(), hx, ny, Bone);
            Bar(p, hx, by, hp / (float)max, HpC, HpHi);
            int cap = Mathf.Max(1, run.provisionCap), sup = Mathf.Clamp(run.provision, 0, cap);
            p.Text("物資", SupX, ny, Game2DKit.Dim);
            string sn = sup + "/" + cap; int sx = SupX + Game2DJpFont.Width("物資") + 4;
            p.Text(sn, sx, ny, Bone);
            int e = Bar(p, SupX, by, sup / (float)cap, SupC, SupHi);
            string preText = sim != null && _view != null ? _view.SupplyPreviewText : null;
            if (preFrac > 0f && e > SupX)
            {
                int lose = Mathf.Min(e - SupX + 1, Mathf.Max(2, Mathf.RoundToInt(BarW * preFrac)));
                p.Rect(e - lose + 1, by, lose, 5, blinkOn ? Game2DKit.Ivory : BarEmpty);      // 使う予定ぶん (180ms ごとに明・暗)
            }
            if (!string.IsNullOrEmpty(preText)) p.Text(preText, sx + Game2DJpFont.Width(sn) + 6, ny, Game2DKit.Red);
            p.Apply(_tb);
        }

        /// <summary>戦闘の画面の左下と同じ棒 (黒い縁・暗い地・中身・上の 1 行は明るい色)。 中身の右端の x を返す (空なら x)。</summary>
        private static int Bar(KitPix p, int x, int y, float frac, Color32 fill, Color32 hi)
        {
            p.Rect(x - 1, y - 1, BarW + 3, 7, BarEdge); p.Rect(x, y, BarW + 1, 5, BarEmpty);
            int e = x + Mathf.RoundToInt(BarW * Mathf.Clamp01(frac));
            if (e > x) { p.Rect(x, y, e - x + 1, 5, fill); p.Rect(x, y, e - x + 1, 1, hi); }
            return e;
        }

        // ── 脱出の確認 (issue #59・gui-needed.md #19) ──
        //   終点 (ボス・〈門〉) は着いた瞬間に起動して戻れない (ボスからは逃げられない・層の残りの点は二度と踏めない)。
        //   なので、 終点へ踏み出す前に共通の板の窓で確かめる。 「進む」で実際の 1 歩 (GameManager.MoveStep) を叩く。

        /// <summary>終点への 1 歩を確かめる窓を出す。 出したら true (呼んだ側は動かない)。 窓が無い・BOT が動かしている時は false (そのまま動いてよい)。</summary>
        public static bool ConfirmExit(GameManager gm, string nodeId, TileType goalType)
        {
            var me = _instance;
            if (me == null || me.modal == null || gm == null || gm.Run == null || GameHooks.BotDriving || Game2DModal.Active) return false;
            var run = gm.Run;
            bool gate = goalType == TileType.Gate;
            var d = new ScreenDef { id = "exit_jp", jp = true, w = 300,
                title = gate ? "〈門〉へ進む" : "終点へ進む",
                sub = "HP " + run.playerHP + "/" + run.playerMaxHP + "  物資 " + run.provision + "/" + run.provisionCap };
            d.body = (gate ? "この先は〈門〉の前。 着いたら、 門へ何を捧げるかを決めることになる。"
                           : "この先にボスが待っている。 着いたら戦闘が始まり、 ボスからは逃げられない。")
                   + "\nこの層の残りの点には、 もう戻れない。";
            d.bodyLines = 4;
            d.buttons.Add(new ModalBtn { label = "進む", action = "go", primary = true });
            d.buttons.Add(new ModalBtn { label = "やめる", action = "close" });
            d.onAction = (a, _) =>
            {
                me.modal.Close();
                if (a == "go") gm.MoveStep(nodeId);
            };
            me.modal.Open(d);
            return true;
        }

        // ── 持ち物の画面 (航行中・2026-11-13 に升目の画面へ置き換え) ──
        //   持っている物を全部、 種類ごとの見出しの下にアイコンの升目で並べる (InventoryGridView)。 升目に乗せると吹き出し (品名 + 効果の説明文)、
        //   押すとフォーカスして右の欄に詳細 (フレーバー文まで)。 消耗品にフォーカスしている時だけ [使う] (回復はその場で・シールドや油は次の戦闘の開幕へ・囮罠はこの点に置く)。
        //   [ステータス] で今までのステータスの頁へ。 見るだけ ── 使う以外は状態を書かない。 BOT が動かしている間は開かない (開いていたら閉じる)。
        //   開いている間は GameManager のキー (Space で待つ・F・L・数字で動く) を止める (SyncInventoryInput)。

        private InventoryGridView _grid;
        private bool _invLocked; private int _invShownFrame = -1;
        /// <summary>戦闘の画面から開いた (2026-11-13): 見るだけ。 消耗品の [使う] は出さない (戦闘中に使うのは [道具] の窓・二重の入口にしない)。</summary>
        private bool _invBattle;

        /// <summary>戦闘の画面 (<see cref="Game2DBattleLcd"/> の [持ち物]・キー I) から持ち物の画面を開く。 開いたら true。
        /// 戦闘の進行・乱数・判断待ちには触らない ── 窓が出ている間は戦闘の画面と <see cref="Battle.Game2DBattleSim"/> が窓を見て止まり、
        /// GameManager のキーは <see cref="SyncInventoryInput"/> が止める (地図と同じ作り)。 BOT が動かしている間は開かない。</summary>
        public static bool OpenInventoryInBattle(GameManager gm)
        {
            var me = _instance;
            if (me == null || gm == null || gm.CurrentPhase != GameManager.GamePhase.Combat) return false;
            return me.OpenInventory(gm, battle: true);
        }

        private bool OpenInventory(GameManager gm, bool battle = false)
        {
            if (modal == null || gm.Run == null || GameHooks.BotDriving) return false;
            _invBattle = battle;
            _invTab = 0; _invPage = 0;
            if (_grid == null) _grid = new InventoryGridView { onClose = () => modal.Close(), onFocusChanged = () => { if (InventoryShown) modal.Replace(Inventory(GameManager.Instance)); } };
            _grid.ResetView();
            modal.Open(Inventory(gm));
            return true;
        }

        /// <summary>戦闘中の 1 行 (消耗品の詳細の欄の「今は使えない:」の後ろ)。</summary>
        public const string BattleUseNote = "戦闘中は [道具] から使う";

        /// <summary>持ち物の画面 (升目かステータスの頁) が出ているか。</summary>
        private bool InventoryShown => modal != null && modal.IsOpen && modal.Current != null && _grid != null
                                       && (modal.Current.canvas == _grid || modal.Current.id == "status_jp");

        /// <summary>出ている間は GameManager のキーを止める (毎フレーム立て直す ── 手引きの板が閉じる時に下ろしても戻す)。 BOT が動き出したら閉じる。</summary>
        private void SyncInventoryInput()
        {
            bool shown = InventoryShown;
            if (shown && GameHooks.BotDriving) { modal.Close(); shown = false; }
            if (shown)
            {
                GameManager.InputBlockedByScreen = true; _invLocked = true; _invShownFrame = Time.frameCount;
                // ステータスの頁の Esc は升目へ戻る (升目の Esc は InventoryGridView が閉じる)
                if (modal.Current.id == "status_jp" && !Game2DTutorial.Blocking && Input.GetKeyDown(KeyCode.Escape)) { _invTab = 0; _invPage = 0; modal.Replace(Inventory(GameManager.Instance)); }
            }
            else if (_invLocked && Time.frameCount > _invShownFrame + 1)      // 閉じた次のフレームまで止め続ける (閉じた時の Esc・Space が GameManager に届かない)
            {
                _invLocked = false;
                if (!Game2DLoadingScreen.Blocking && !Game2DTutorial.Blocking) GameManager.InputBlockedByScreen = false;      // ほかの画面が立てている時は触らない
            }
        }

        private ScreenDef Inventory(GameManager gm)
        {
            if (_invTab == 2) return Status(gm);
            var run = gm.Run;
            var cards = ItemCards.Collect(run);
            if (_invBattle)      // 戦闘中: [使う] を出さない。 消耗品の詳細の欄に「戦闘中は [道具] から使う」
                foreach (var sec in cards) foreach (var c in sec) if (c.usable) { c.usable = false; c.block = BattleUseNote; }
            _grid.SetItems(cards);
            var d = new ScreenDef { id = "inventory_grid", jp = true, w = InventoryLayout.W, h = InventoryLayout.H, canvas = _grid,
                title = "持ち物",
                sub = "HP " + run.playerHP + "/" + run.playerMaxHP + "  物資 " + run.provision + "/" + run.provisionCap + "  " + run.coins + "G" };
            var f = _grid.Focused;
            if (f != null && f.usable) d.buttons.Add(new ModalBtn { label = "使う", action = "use", primary = true });
            d.buttons.Add(new ModalBtn { label = "ステータス", action = "tab" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.footer = "矢印で選ぶ  Space で詳細を送る  Esc で閉じる";
            d.onAction = (a, _) =>
            {
                switch (a)
                {
                    case "close": modal.Close(); return;
                    case "tab": _invTab = 2; _invPage = 0; break;
                    case "use":
                    {
                        var c = _grid.Focused;
                        if (c == null || !c.usable) return;
                        string why = Consumables.BlockReason(run, c.key);
                        if (why != null) { modal.ShowError(why); return; }
                        if (Consumables.Use(run, c.key)) { Game2DSfx.Play("ui_confirm"); modal.ShowToast("使った"); }
                        else { modal.ShowError("今は使えない"); return; }
                        break;
                    }
                }
                modal.Replace(Inventory(gm));
            };
            return d;
        }

        // ── ステータスの頁: 会心率・攻撃 (武器)・会心倍率・与ダメ・防御・貫通と、 受けている挑戦カード (2026-11-09) ──
        //   HP と シールドは計器に出ているので載せない。 戦闘の外では、 直近の戦闘で読んだ値 (CombatManager.PreviewHudStats)。

        private ScreenDef Status(GameManager gm)
        {
            var run = gm.Run;
            var cm = CombatSystem.CombatManager.Instance;
            var hs = cm != null ? cm.PreviewHudStats() : default;
            bool inBattle = gm.CurrentPhase == GameManager.GamePhase.Combat;
            var weapon = ItemSystem.ItemDatabase.Instance?.GetItem(run.equippedWeaponId);
            var d = new ScreenDef { id = "status_jp", jp = true, w = 460, cols = 1, rowH = Game2DModal.ChallengeRowH,
                title = "ステータス", sub = inBattle ? "この戦闘" : "直近の戦闘の値" };
            string wname = weapon != null && !string.IsNullOrEmpty(weapon.displayName) ? weapon.displayName : "なし";
            // まだ一度も戦闘の文脈が無い時は数字を出さない (0% や ×0.0 は間違った値に見える)
            bool known = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context != null;
            d.body = known
                ? "武器 " + wname + "  攻撃 " + hs.attack + "  会心率 " + Mathf.RoundToInt(hs.critRate * 100f) + "%  会心倍率 ×" + hs.critMul.ToString("0.0")
                  + "\n与ダメ " + (hs.outgoingPct >= 0 ? "+" : "") + hs.outgoingPct + "%  防御 " + hs.defense + "  貫通 " + hs.pen
                  + (hs.toilCutPct > 0 ? "  〈" + EventSystem.TimedEffects.BattleBoonRule.ToilName + "〉与ダメ −" + hs.toilCutPct + "%" : "")   // 〈徒労〉の戦闘のデバフ (2026-11-14)
                : "武器 " + wname + "  攻撃 ―  会心率 ―  会心倍率 ―\n与ダメ ―  防御 ―  貫通 ―  (戦闘に入ると出る)";
            d.bodyLines = 2;
            int pages = 1;
            if (run.challengePicks.Count > 0)
            {
                const int perPage = 3;   // 2 段の行が窓に収まる数
                pages = (run.challengePicks.Count + perPage - 1) / perPage; _invPage = Mathf.Clamp(_invPage, 0, pages - 1);
                for (int i = _invPage * perPage; i < Mathf.Min(run.challengePicks.Count, (_invPage + 1) * perPage); i++)
                {
                    MetaProgression.ChallengeCards.Split(run.challengePicks[i], out var deb, out var buf);
                    int tier = deb?.tier ?? buf?.tier ?? 1;
                    if (deb == null && buf == null) continue;   // 廃止したカードの古いセーブ (知らない id) は出さない
                    d.Items.Add(Game2DPhaseScreens.SinOrCardItem(Mathf.Clamp(tier, 1, 4), deb, buf));
                }
                d.footer = "受けている挑戦カード " + run.challengePicks.Count + " 枚 (同じ種類はランで 1 度だけ)";
            }
            else d.footer = "挑戦カードはまだ受けていない";
            d.buttons.Add(new ModalBtn { label = "持ち物へ", action = "tab", primary = true });
            if (pages > 1) d.buttons.Add(new ModalBtn { label = "次の頁 " + (_invPage + 1) + "/" + pages, action = "page" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.onAction = (a, sel) =>
            {
                if (a == "close") { modal.Close(); return; }
                if (a == "tab") { _invTab = 0; _invPage = 0; }
                if (a == "page") _invPage = (_invPage + 1) % pages;
                modal.Replace(Inventory(gm));
            };
            return d;
        }

        private static void Ensure(ref Texture2D tex, RectTransform rt, RectInt r)
        {
            if (rt != null) rt.sizeDelta = new Vector2(r.width, r.height);      // シーンに保存された大きさが古くても、 絵の大きさに合わせる (引き延ばし防止)
            if (tex != null || rt == null) return;
            tex = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DNavExtras" };
            var ri = rt.GetComponent<RawImage>(); if (ri != null) { ri.texture = tex; ri.enabled = true; }
        }

        private static readonly string[] WeatherIcons = { "still", "fog", "still", "sand", "snow", "hail", "crumble", "sink" };      // WeatherKind: None, Fog, (2 = 旧 沈む静寂・空き), Sand, Snow, Hail, Crumble, Sink
        private static readonly string[] WeatherNames = { "凪", "霧", "静寂", "砂", "雪", "雹", "崩れ", "沈み" };

        /// <summary>計器の台 (PixelLab のチップ台を 9 分割)。 無ければコード描画の板。</summary>
        private static void Plate(KitPix p, int x, int y, int w, int h, float mul = 1f)
        {
            if (Game2DKit.Chip != null) p.NineSlice(Game2DKit.Chip, 6, Game2DKit.Well, x, y, w, h); else p.Plate(x, y, w, h, mul);
        }

        /// <summary>所持品の欄の 1 枠 (18×18)。 itemId = 品のアイコン (Resources/Icons/Items・16×16 をそのまま打つ・拡大縮小しない)。
        /// 絵の無い品は、 系統の代わりのアイコン (fallback) を塗る。 dim = 今は使えない消耗品 (明度を落として出す ── 大きさは変えない)。</summary>
        private static void Slot(KitPix p, int x, int y, string itemId, string fallback, bool dim)
        {
            const int size = SlotCell;
            if (Game2DKit.Slot != null) p.Sprite(Game2DKit.Slot, x, y, size, size); else p.Box(x, y, size, size, Game2DKit.Off, Game2DKit.Dark);
            if (itemId == null) return;
            var ic = Game2DKit.ItemIcon(itemId);
            if (ic != null) p.Sprite(ic, x + 1, y + 1, 16, 16);
            else p.AnyIcon(fallback, x + 2, y + 2, size - 4, Game2DKit.Ink);
            if (!dim) return;
            for (int j = 1; j < size - 1; j++)
                for (int i = 1; i < size - 1; i++)
                {
                    int px = x + i, py = y + j; if (px < 0 || py < 0 || px >= p.w || py >= p.h) continue;
                    int k = (p.h - 1 - py) * p.w + px; var c = p.px[k];
                    p.px[k] = new Color32((byte)(c.r * 2 / 5), (byte)(c.g * 2 / 5), (byte)(c.b * 2 / 5), c.a);
                }
        }

        /// <summary>所持品の欄の枠の大きさと間隔 (2 列 × 5 段)。</summary>
        private const int SlotCell = 18, SlotStep = 19, SlotCols = 2, SlotRows = 5;

        /// <summary>所持品の欄に並べる品: 消耗品 (同じ品は 1 枠・持っている順) → パッシブ。 枠に入りきらない分は最後の枠に「+」。</summary>
        private static List<string> ShelfItems(RunState run, out int consKinds)
        {
            var l = new List<string>(); consKinds = 0;
            if (run?.ownedConsumables != null) foreach (var id in run.ownedConsumables) if (!l.Contains(id)) l.Add(id);
            consKinds = l.Count;
            if (run?.ownedPassiveItems != null) l.AddRange(run.ownedPassiveItems);
            return l;
        }

        /// <summary>所持品の欄 (左の列): 品のアイコンを消耗品 → パッシブの順に 2 列 × 5 段。 今は使えない消耗品 (戦闘の中だけで使える品・HP が満ちている時の回復薬 等) は暗く。 押すと持ち物の画面。</summary>
        private void RedrawLeft(RunState run, int passives, int cons)
        {
            Ensure(ref _tl, _left, LeftR);
            var p = new KitPix(LeftR.width, LeftR.height);
            p.Text("所持品", 0, 0, Game2DKit.Dim);
            var shelf = ShelfItems(run, out int consKinds);
            int slots = SlotCols * SlotRows; bool more = shelf.Count > slots;
            for (int i = 0; i < slots; i++)
            {
                int x = (i % SlotCols) * SlotStep, y = 13 + (i / SlotCols) * SlotStep;
                bool last = i == slots - 1;
                if (more && last) { Slot(p, x, y, null, null, false); p.TextC("+" + (shelf.Count - slots + 1), new RectInt(x, y, SlotCell, SlotCell), Game2DKit.Ink); continue; }
                string id = i < shelf.Count ? shelf[i] : null; bool isCons = i < consKinds;
                Slot(p, x, y, id, isCons ? "supply" : "relic", id != null && isCons && !Consumables.CanUse(run, id));
            }
            p.Text((cons + passives) + "個", 0, 13 + SlotRows * SlotStep + 1, Game2DKit.Ink);
            p.Apply(_tl);
        }
    }
}

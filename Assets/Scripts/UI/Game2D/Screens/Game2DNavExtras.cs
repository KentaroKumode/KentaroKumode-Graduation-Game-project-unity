using System.Collections.Generic;
using GameLoop;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 航行の計器の足りない分 (<b>仮置き</b>・2026-10-24)。 docs/design/gui-needed.md の「未実装」の航行の項目を、 枠・ボタンの仮の絵と文字で並べたもの。
    /// 上の帯 = 天気 (今)・層 (今の層 / 最終層)・警告ランプ (追跡者が警戒・追跡中で点灯)・待つボタン / 左下 = 職業・状態異常・消耗品・パッシブ / 右 = 航行アイテムの枠 3 つ・脱出ボタン。
    /// 層・追跡者・天気・所持品 (左下の 10 枠は品のアイコン・2026-11-12) は実際の値、 予報は見本。 戦闘中は 3 方向へ引っ込む (<see cref="Drive"/>)。
    /// <para>操作 (2026-11-04): 上の帯の [WAIT] = 待つ (その場で 1 ターン・Space と同じ) / 左下の ITEMS の欄 = 持ち物の画面 (2026-11-13 に升目の画面へ・品の詳細を読む・消耗品を使う)。</para>
    /// <para>2026-11-11: 上の帯の右隣に「経過ターン・補足中の警告 (ETA)・照明の残り」の帯、 右の列に航行アイテムの 3 枠 (V / B / N・〈前哨の旗〉を立てる)、
    /// 終点 (ボス・〈門〉) へ踏み出す前の確認の窓 (<see cref="ConfirmExit"/>) を足した。</para>
    /// </summary>
    public class Game2DNavExtras : MonoBehaviour
    {
        public Game2DScreen screen;
        public Game2DModal modal;
        [SerializeField] private RectTransform _top, _left, _right;      // _right = 航行アイテムの 3 枠 (2026-11-11 に戻した。 旧版のシーンに残っていたものは大きさ・位置を直して使う)
        /// <summary>経過ターン・補足中の警告 (ETA)・照明の残り・羅針の方角の帯 (2026-11-11・issue #69 / #72)。 シーンに無ければ実行時に作る。</summary>
        [SerializeField] private RectTransform _status;
        private Texture2D _tt, _tl, _tr, _ts;
        private static readonly RectInt TopR = new RectInt(96, 0, 124, 30), LeftR = new RectInt(0, 219, 100, 51);      // 2026-11-11: 字を既定のフォント (高さ 12) にして広げた
        /// <summary>上の帯の右隣 (レーダーの手前まで)。 押せる物は無い。</summary>
        private static readonly RectInt StatusR = new RectInt(222, 0, 166, 30);
        /// <summary>右の列: 航行アイテムの 3 枠 (V / B / N)。 レーダーの下・HP のバーの上。</summary>
        private static readonly RectInt RightR = new RectInt(436, 94, 44, 76);
        private const int SlotSize = 20, SlotPitch = 25;
        /// <summary>枠のキー。 W は視点の移動 (WASD) と重なるので、 設計 (Q / W / E) から V / B / N に変えた (2026-11-11: Q / E も視点の移動の隣で紛らわしいので下の段へ) (R は偵察・F は照明・L は囮罠・数字は移動)。</summary>
        private static readonly KeyCode[] SlotKeys = { KeyCode.V, KeyCode.B, KeyCode.N };
        private static readonly string[] SlotKeyNames = { "V", "B", "N" };
        private int _hash = int.MinValue; private float _next; private float _e;
        private static readonly RectInt WaitR = new RectInt(84, 4, 38, 22);      // 上の帯の中 (帯の左上が原点)
        private static bool _shown;
        private static Game2DNavExtras _instance;
        private int _invPage;
        /// <summary>持ち物の画面の頁: 0 = 升目 (2026-11-13) / 2 = ステータス (2026-11-09)。 ステータスの頁の _invPage は挑戦カードの頁。</summary>
        private int _invTab;

        /// <summary>この dot (GUI の 480×270) がボタン・計器の上か (地図のクリック・ドラッグに渡さない)。</summary>
        public static bool Consumes(Vector2 d) => _shown && (In(d, TopR) || In(d, LeftR) || In(d, StatusR) || In(d, RightR));
        private static bool In(Vector2 d, RectInt r) => d.x >= r.x && d.x < r.x + r.width && d.y >= r.y && d.y < r.y + r.height;

        public void Build()
        {
            _top = Make("top", TopR); _left = Make("left", LeftR); _status = Make("status", StatusR); _right = Make("right", RightR);
        }

        private void Awake() { _instance = this; }

        /// <summary>上の帯 (経過ターン・層・待つ) の RectTransform。 手引きの強調 (issue #109) が範囲を読む。 無ければ null。</summary>
        public static RectTransform TopBar => _instance != null ? _instance._top : null;

        /// <summary>シーンを builder で作り直していない時に、 新しく足した欄 (状態の帯・右の列) を作る。</summary>
        private void EnsureParts()
        {
            if (_top == null) return;
            if (_status == null) _status = Make("status", StatusR);
            if (_right == null) _right = Make("right", RightR);
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
            foreach (var t in new[] { _tt, _tl, _tr, _ts }) if (t != null) Destroy(t);
            if (_instance == this) _instance = null;
        }

        /// <param name="e">引っ込み具合 0 = 出ている / 1 = 画面の外 (ばね。 行き過ぎはそのまま渡す)</param>
        public void Drive(float e)
        {
            _e = e;
            if (_top == null) return;
            EnsureParts();
            _top.anchoredPosition = new Vector2(TopR.x, -TopR.y + Mathf.Round(e * 30f));
            _status.anchoredPosition = new Vector2(StatusR.x, -StatusR.y + Mathf.Round(e * 30f));
            _left.anchoredPosition = new Vector2(LeftR.x - Mathf.Round(e * 100f), -LeftR.y);
            _right.anchoredPosition = new Vector2(RightR.x + Mathf.Round(e * 60f), -RightR.y);
            bool show = e < 0.995f; _shown = e < 0.05f;
            foreach (var rt in new[] { _top, _left, _status, _right }) rt.gameObject.SetActive(show);
        }

        private void Update()
        {
            SyncInventoryInput();
            if (_top == null || _e > 0.5f) return;
            var gm = GameManager.Instance; if (gm == null) return;
            var sim = gm.CurrentPhase == GameManager.GamePhase.MapNavigation ? gm.FreeMap : null;
            var run = gm.Run;
            bool free = sim != null && !Game2DModal.Active && !Game2DLoadingScreen.Blocking;
            _hoverSlot = -1;
            if (free && screen != null && Game2DPointer.TryDot(screen, out var hd))
                for (int i = 0; i < 3; i++) if (In(hd, SlotRect(i))) _hoverSlot = i;
            if (free && Input.GetMouseButtonDown(0) && screen != null && Game2DPointer.TryDot(screen, out var dot))
            {
                if (In(dot, new RectInt(TopR.x + WaitR.x, TopR.y + WaitR.y, WaitR.width, WaitR.height))) { if (gm.WaitTurn()) Game2DSfx.Play("ui_click"); }
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
            bool jump = Time.unscaledTime < _jumpUntil;
            bool chase = sim != null && sim.FoeAlive && sim.FoeMode == FoeMode.Chase;
            int blink = chase ? Mathf.FloorToInt(Time.unscaledTime * 2.5f) & 1 : 0;      // 補足中の警告は 2.5 回/秒で明滅
            int h = floor * 7 + passives * 31 + cons * 131 + (run != null ? run.playerHP * 389 : 0) + weather * 17 + (foe + 2) * 1009 + (sim != null ? sim.Version * 7919 : 0) + blink * 104729 + (_hoverSlot + 2) * 15485863 + (jump ? 31 : 0);
            if (h == _hash && Time.unscaledTime < _next) return;
            _hash = h; _next = Time.unscaledTime + 0.5f;
            Redraw(floor, passives, cons, weather, foe, sim);
            RedrawStatus(gm, sim, run, blink == 0);
            RedrawSlots(gm, sim, run);
        }

        // ── 経過ターン・補足中の警告 (issue #69 / #72) ──
        //   1 行目 = 経過ターン (「ターン」とは書かない) と照明の残り・近道の残り (イベント「近道+N」・ただで動ける回数)。 2 行目 = 優先順に 「枠の説明 (乗せている時)」→「補足中の ETA」→「羅針の方角」。
        //   補給 (店) で動けない待ち時間は、 今の規則 (店は 0 ターン) では起きない。 まとめて進んだ分 (イベント・多めに休む) はターン数の横の「+N」(gui-needed.md #21)。

        private void RedrawStatus(GameManager gm, FreeMapSim sim, RunState run, bool blinkOn)
        {
            if (_status == null) return;
            Ensure(ref _ts, _status, StatusR);
            var p = new KitPix(StatusR.width, StatusR.height);
            if (sim == null) { p.Apply(_ts); return; }
            Plate(p, 0, 2, StatusR.width, 28);
            p.Text("ターン", 5, 3, Game2DKit.Dim);
            string tn = Mathf.Min(9999, sim.Turn).ToString(); int tx = 5 + Game2DJpFont.Width("ターン") + 4;
            p.Text(tn, tx, 3, Game2DKit.Ink);
            if (Time.unscaledTime < _jumpUntil) p.Text("+" + _jumpN, tx + Game2DJpFont.Width(tn) + 4, 3, Game2DKit.Amber);      // まとめて進んだ分
            // 右: 照明の残り (ターン) と近道の残り (ただで動ける回数)。 両方ある時は、 ターン数の字と重なるなら短く書く
            string flare = sim.FlareOn ? "照明 残り" + sim.FlareTurnsLeft : null;
            string cut = sim.FreeMovesLeft > 0 ? "近道 " + sim.FreeMovesLeft + "回" : null;      // 時限バフ「近道」の残り (時限バフの「(N回)」と同じ数え方)
            string right = flare != null && cut != null ? cut + " " + flare : flare ?? cut;
            int leftEnd = tx + Game2DJpFont.Width(tn) + (Time.unscaledTime < _jumpUntil ? 4 + Game2DJpFont.Width("+" + _jumpN) : 0);
            if (flare != null && cut != null && StatusR.width - 5 - Game2DJpFont.Width(right) < leftEnd + 6)
                right = "近道" + sim.FreeMovesLeft + "回 照明" + sim.FlareTurnsLeft;
            if (right != null) p.TextR(right, StatusR.width - 5, 3, Game2DKit.Amber);
            string line = null; var col = Game2DKit.Dim;
            if (_hoverSlot >= 0) { line = SlotHint(gm, sim, run, _hoverSlot); col = Game2DKit.Ink; }
            else if (sim.FoeAlive && sim.FoeMode == FoeMode.Chase)
            {
                // 補足中: こちらが動かなかった場合に最短で何ターンで捕まるか (§5-9・FreeMapSim.Eta)。 誰が来るかは出さない (性質は明かさない)
                var eta = sim.Eta();
                line = eta.Valid ? "補足中 あと" + eta.turns + "ターンで捕まる" : "補足中";
                col = blinkOn ? Game2DKit.Red : Game2DKit.Off;
            }
            else if (NavItems.FineBearing(run))
            {
                var st = sim.Stone();      // 〈羅針の針〉: 魔石の方角を 8 方位の字で (レーダーは 4 方向の扇)
                if (st.level > 0 && st.bearing >= 0) { line = "魔石 " + StoneReading.BearingNames[st.bearing]; col = st.level >= 2 ? Game2DKit.Amber : Game2DKit.Dim; }
            }
            if (string.IsNullOrEmpty(line))
            {
                // 時限バフ / デバフ (説明を持つもの・2026-11-13・〈徒労〉〈覚悟〉): 名前・残り・説明。 ほかに出す行が無い時だけ (いちばん後ろの優先)
                string tl = TimedEffectLine(run);
                if (tl != null) { line = tl; col = Game2DKit.Amber; }
            }
            if (!string.IsNullOrEmpty(line)) p.Text(Game2DJpFont.Clip(line, StatusR.width - 10), 5, 16, col);
            p.Apply(_ts);
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
                    if (desc != null) return "〈" + kv.Key + "〉残り" + kv.Value + " " + desc;
                }
            }
            return null;
        }

        // ── 航行アイテムの 3 枠 (issue #10・gui-needed.md #20) ──
        //   枠に入るのは「使う」か「回数のある」航行アイテムだけ: 〈前哨の旗〉(押すと立てる)・〈煙玉〉(補足された時に自動で弾ける・押しても何もしない)。
        //   ほかの 11 品は持っているだけで効くので、 持ち物の画面 (秘宝) で読む。 絵は無いので、 名前の頭の 1 字で出す。

        private int _hoverSlot = -1;
        private FreeMapSim _lastSim; private int _lastTurn, _jumpN; private float _jumpUntil;

        private static RectInt SlotRect(int i) => new RectInt(RightR.x + 12, RightR.y + i * SlotPitch, SlotSize, SlotSize);

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
            if (id == ItemIds.NavSmokeBall) return ready ? id + " 補足された時に自動" : id + " この層は使用済み";
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

        private void RedrawSlots(GameManager gm, FreeMapSim sim, RunState run)
        {
            if (_right == null) return;
            Ensure(ref _tr, _right, RightR);
            var p = new KitPix(RightR.width, RightR.height);
            var items = SlotItems(run);
            for (int i = 0; i < 3; i++)
            {
                var r = SlotRect(i); int x = r.x - RightR.x, y = r.y - RightR.y;
                bool has = i < items.Count, ready = has && SlotReady(gm, sim, items[i]), hv = i == _hoverSlot;
                p.Text(SlotKeyNames[i], 1, y + (SlotSize - Game2DJpFont.Cell) / 2 + 1, hv ? Game2DKit.Ink : Game2DKit.Dim);
                p.Box(x, y, SlotSize, SlotSize, Game2DKit.Off, hv ? Game2DKit.Amber : has ? Game2DKit.Dim : Game2DKit.Faint);
                if (!has) continue;
                string g = items[i].Substring(items[i].Length - 1);      // 名前の最後の 1 字 (旗・玉)。 id の綴りから効果は読まない ── 字を出すだけ
                p.TextC(g, new RectInt(x, y, SlotSize, SlotSize), ready ? Game2DKit.Ink : Game2DKit.Faint);
                if (ready) p.Rect(x + SlotSize - 4, y + SlotSize - 4, 2, 2, Game2DKit.Amber);      // 使える印
            }
            p.Apply(_tr);
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
        //   開いている間は GameManager のキー (Space で待つ・R・F・L・数字で動く) を止める (SyncInventoryInput)。

        private InventoryGridView _grid;
        private bool _invLocked; private int _invShownFrame = -1;

        private void OpenInventory(GameManager gm)
        {
            if (modal == null || gm.Run == null || GameHooks.BotDriving) return;
            _invTab = 0; _invPage = 0;
            if (_grid == null) _grid = new InventoryGridView { onClose = () => modal.Close(), onFocusChanged = () => { if (InventoryShown) modal.Replace(Inventory(GameManager.Instance)); } };
            _grid.ResetView();
            modal.Open(Inventory(gm));
        }

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
            _grid.SetItems(ItemCards.Collect(run));
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
                        if (why != null) { Game2DSfx.Play("ui_click"); modal.ShowToast(why); return; }
                        if (Consumables.Use(run, c.key)) { Game2DSfx.Play("ui_confirm"); modal.ShowToast("使った"); }
                        else { modal.ShowToast("今は使えない"); return; }
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
                    d.Items.Add(new ModalItem { challengeTier = Mathf.Clamp(tier, 1, 4), deb = MetaProgression.ChallengeCards.Text(deb), buf = MetaProgression.ChallengeCards.Text(buf) });
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

        /// <summary>所持品の欄の枠の大きさと間隔 (5 枠 × 2 段)。</summary>
        private const int SlotCell = 18, SlotStep = 19, SlotCols = 5, SlotRows = 2;

        /// <summary>所持品の欄に並べる品: 消耗品 (同じ品は 1 枠・持っている順) → パッシブ。 枠に入りきらない分は最後の枠に「+」。</summary>
        private static List<string> ShelfItems(RunState run, out int consKinds)
        {
            var l = new List<string>(); consKinds = 0;
            if (run?.ownedConsumables != null) foreach (var id in run.ownedConsumables) if (!l.Contains(id)) l.Add(id);
            consKinds = l.Count;
            if (run?.ownedPassiveItems != null) l.AddRange(run.ownedPassiveItems);
            return l;
        }

        private void Redraw(int floor, int passives, int cons, int weather, int foe, FreeMapSim sim = null)
        {
            Ensure(ref _tt, _top, TopR); Ensure(ref _tl, _left, LeftR);
            // ── 上の帯 ──
            var p = new KitPix(TopR.width, TopR.height);
            Plate(p, 0, 2, 82, 28);                                                                    // 天気 (気圧計の代わり)
            string wn = weather >= 0 && weather < WeatherNames.Length ? WeatherNames[weather] : "W" + weather;
            p.AnyIcon(weather >= 0 && weather < WeatherIcons.Length ? WeatherIcons[weather] : "still", 3, 7, 18, Game2DKit.Ink);
            bool storm = sim != null && sim.StormMode;
            if (storm && weather == 0) wn = "嵐";                                                   // 嵐の層の種類なし (素の嵐・2026-11-13)
            p.Text("天気", 24, 3, Game2DKit.Dim); p.Text(wn, 48, 3, weather == 0 && !storm ? Game2DKit.Ink : Game2DKit.Amber);
            if (storm)
            {
                // 嵐 (2026-11-13): 2 行目に嵐の時計 (静かな時間は「まで N」・嵐の間は「k あとN」)
                int ph = sim.StormPhase, n = sim.StormTurnsToNext;
                p.Text("嵐", 24, 16, Game2DKit.Dim);
                p.Text(ph == 0 ? "まで" + n : ph >= 4 ? "段4" : "段" + ph + " " + n, 40, 16, ph == 0 ? Game2DKit.Ink : Game2DKit.Amber);
            }
            else { p.Text("予報", 24, 16, Game2DKit.Dim); p.Text("???", 48, 16, Game2DKit.Faint); }       // 予報は物資の段階で精度が変わる (未接続)
            // 待つ (その場で 1 ターン)。 旧: 見本の気圧計
            if (Game2DKit.Tab != null) p.NineSlice(Game2DKit.Tab, 5, Game2DKit.Well, WaitR.x, WaitR.y, WaitR.width, WaitR.height); else p.Box(WaitR.x, WaitR.y, WaitR.width, WaitR.height, Game2DKit.Off, Game2DKit.Dark);
            p.TextC("待つ", WaitR, Game2DKit.Ink);
            p.Apply(_tt);
            // ── 左下 (所持品: 消耗品とパッシブを 1 つの欄にまとめる。 状態異常は戦闘の表示) ──
            //   実際の所持品 (issue #11): 品のアイコンを消耗品 → パッシブの順に。 今は使えない消耗品 (戦闘の中だけで使える品・HP が満ちている時の回復薬 等) は暗く。
            p = new KitPix(LeftR.width, LeftR.height);
            int total = cons + passives;
            p.Text("所持品", 2, 0, Game2DKit.Dim); p.TextR(total + "個", 96, 0, Game2DKit.Ink);
            var run = GameManager.Instance?.Run;
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
            p.Apply(_tl);
        }
    }
}

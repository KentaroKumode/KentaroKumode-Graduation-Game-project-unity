using System.Collections.Generic;
using GameLoop;
using MapSystem.FreeMove;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 航行の計器の足りない分 (<b>仮置き</b>・2026-10-24)。 docs/design/gui-needed.md の「未実装」の航行の項目を、 枠・ボタンの仮の絵と文字で並べたもの。
    /// 上の帯 = 天気 (今)・層 (今の層 / 最終層)・警告ランプ (追跡者が警戒・追跡中で点灯)・待つボタン / 左下 = 職業・状態異常・消耗品・パッシブ / 右 = 航行アイテムの枠 3 つ・脱出ボタン。
    /// 層・追跡者・天気・所持数は実際の値、 そのほか (職業名・状態異常・アイテム枠) は見本。 戦闘中は 3 方向へ引っ込む (<see cref="Drive"/>)。
    /// <para>操作 (2026-11-04): 上の帯の [WAIT] = 待つ (その場で 1 手番・Space と同じ) / 左下の ITEMS の欄 = 所持品の窓 (消耗品を使う・パッシブを見る)。</para>
    /// </summary>
    public class Game2DNavExtras : MonoBehaviour
    {
        public Game2DScreen screen;
        public Game2DModal modal;
        [SerializeField] private RectTransform _top, _left, _right;      // _right は旧版の残り (航行アイテムの欄は廃止。 シーンに残っていれば隠す)
        private Texture2D _tt, _tl;
        private static readonly RectInt TopR = new RectInt(96, 0, 124, 30), LeftR = new RectInt(0, 219, 100, 51);      // 2026-11-11: 字を既定のフォント (高さ 12) にして広げた
        private int _hash = int.MinValue; private float _next; private float _e;
        private static readonly RectInt WaitR = new RectInt(84, 4, 38, 22);      // 上の帯の中 (帯の左上が原点)
        private static bool _shown;
        private bool _invPassives; private int _invPage;
        /// <summary>所持品の窓の頁: 0 = 消耗品 / 1 = パッシブ / 2 = ステータス (2026-11-09)。 _invPassives は 1 の時だけ立つ。</summary>
        private int _invTab;
        private const int InvPerPage = 10;

        /// <summary>この dot (GUI の 480×270) がボタン・計器の上か (地図のクリック・ドラッグに渡さない)。</summary>
        public static bool Consumes(Vector2 d) => _shown && (In(d, TopR) || In(d, LeftR));
        private static bool In(Vector2 d, RectInt r) => d.x >= r.x && d.x < r.x + r.width && d.y >= r.y && d.y < r.y + r.height;

        public void Build()
        {
            _top = Make("top", TopR); _left = Make("left", LeftR);
        }

        private RectTransform Make(string name, RectInt r)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rt = (RectTransform)go.transform; rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(r.x, -r.y); rt.sizeDelta = new Vector2(r.width, r.height);
            var ri = go.AddComponent<RawImage>(); ri.raycastTarget = false; ri.enabled = false;
            return rt;
        }

        private void OnDestroy() { if (_tt != null) Destroy(_tt); if (_tl != null) Destroy(_tl); }

        /// <param name="e">引っ込み具合 0 = 出ている / 1 = 画面の外 (ばね。 行き過ぎはそのまま渡す)</param>
        public void Drive(float e)
        {
            _e = e;
            if (_top == null) return;
            _top.anchoredPosition = new Vector2(TopR.x, -TopR.y + Mathf.Round(e * 30f));
            _left.anchoredPosition = new Vector2(LeftR.x - Mathf.Round(e * 100f), -LeftR.y);
            bool show = e < 0.995f; _shown = e < 0.05f;
            foreach (var rt in new[] { _top, _left }) rt.gameObject.SetActive(show);
        }

        private void Update()
        {
            if (_top == null || _e > 0.5f) return;
            var gm = GameManager.Instance; if (gm == null) return;
            var sim = gm.CurrentPhase == GameManager.GamePhase.MapNavigation ? gm.FreeMap : null;
            var run = gm.Run;
            if (sim != null && !Game2DModal.Active && !Game2DLoadingScreen.Blocking && Input.GetMouseButtonDown(0) && screen != null && Game2DPointer.TryDot(screen, out var dot))
            {
                if (In(dot, new RectInt(TopR.x + WaitR.x, TopR.y + WaitR.y, WaitR.width, WaitR.height))) { if (gm.WaitTurn()) Game2DSfx.Play("ui_click"); }
                else if (In(dot, LeftR)) OpenInventory(gm);
            }
            int floor = run != null ? run.currentFloor : 1, passives = run?.ownedPassiveItems?.Count ?? 0, cons = run?.ownedConsumables?.Count ?? 0;
            int weather = sim != null ? (int)sim.Weather : 0, foe = sim != null && sim.HasFoe && sim.FoeAlive ? (int)sim.FoeMode : -1;
            int h = floor * 7 + passives * 31 + cons * 131 + weather * 17 + (foe + 2) * 1009;
            if (h == _hash && Time.unscaledTime < _next) return;
            _hash = h; _next = Time.unscaledTime + 0.5f;
            Redraw(floor, passives, cons, weather, foe);
        }

        // ── 所持品の窓 (航行中) ──
        //   消耗品: 選んで使う (回復はその場で・シールドや油は次の戦闘の開幕へ・囮罠はこの点に置く)。 パッシブ: 説明を読む。 頁で送る。

        private void OpenInventory(GameManager gm)
        {
            if (modal == null || gm.Run == null) return;
            _invPassives = false; _invPage = 0; _invTab = 0;
            modal.Open(Inventory(gm));
        }

        private ScreenDef Inventory(GameManager gm)
        {
            if (_invTab == 2) return Status(gm);
            var run = gm.Run;
            var ids = new List<string>();
            var src = _invPassives ? run.ownedPassiveItems : run.ownedConsumables;
            if (src != null) foreach (var id in src) if (_invPassives || !ids.Contains(id)) ids.Add(id);
            int pages = Mathf.Max(1, (ids.Count + InvPerPage - 1) / InvPerPage); _invPage = Mathf.Clamp(_invPage, 0, pages - 1);
            int first = _invPage * InvPerPage, cnt = Mathf.Min(InvPerPage, ids.Count - first);
            var d = new ScreenDef { id = "inventory_jp", jp = true, w = 460, cols = 2,
                title = _invPassives ? "所持品 ― パッシブ" : "所持品 ― 消耗品",
                sub = "HP " + run.playerHP + "/" + run.playerMaxHP + "  物資 " + run.provision + "/" + run.provisionCap + "  " + run.coins + "G" };
            for (int i = 0; i < cnt; i++)
            {
                string id = ids[first + i]; var data = ItemSystem.ItemDatabase.Instance?.GetItem(id);
                int n = 0; if (!_invPassives) foreach (var c in run.ownedConsumables) if (c == id) n++;
                d.Items.Add(new ModalItem { name = data != null && !string.IsNullOrEmpty(data.displayName) ? data.displayName : id, value = _invPassives ? null : "x" + n, desc = data?.description });
            }
            if (ids.Count == 0) d.body = _invPassives ? "パッシブを持っていない。" : "消耗品を持っていない。";
            if (!_invPassives) d.buttons.Add(new ModalBtn { label = "使う", action = "use", primary = true });
            d.buttons.Add(new ModalBtn { label = _invPassives ? "ステータスへ" : "パッシブへ", action = "tab" });
            if (pages > 1) d.buttons.Add(new ModalBtn { label = "次の頁 " + (_invPage + 1) + "/" + pages, action = "page" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.footer = _invPassives ? "パッシブ " + ids.Count + " 個" : "使う品を選ぶ (シールド・油は次の戦闘の開幕に効く)";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "close": modal.Close(); return;
                    case "tab": _invTab = (_invTab + 1) % 3; _invPassives = _invTab == 1; _invPage = 0; break;
                    case "page": _invPage = (_invPage + 1) % pages; break;
                    case "use":
                        if (sel < 0 || sel >= cnt) return;
                        if (Consumables.Use(run, ids[first + sel])) { Game2DSfx.Play("ui_confirm"); modal.ShowToast("使った"); }
                        else { modal.ShowToast("今は使えない"); return; }
                        break;
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
            d.buttons.Add(new ModalBtn { label = "消耗品へ", action = "tab", primary = true });
            if (pages > 1) d.buttons.Add(new ModalBtn { label = "次の頁 " + (_invPage + 1) + "/" + pages, action = "page" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.onAction = (a, sel) =>
            {
                if (a == "close") { modal.Close(); return; }
                if (a == "tab") { _invTab = 0; _invPassives = false; _invPage = 0; }
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

        private static void Slot(KitPix p, int x, int y, int size, bool filled, string icon)
        {
            if (Game2DKit.Slot != null) p.Sprite(Game2DKit.Slot, x, y, size, size); else p.Box(x, y, size, size, Game2DKit.Off, Game2DKit.Dark);
            if (filled) p.AnyIcon(icon, x + 2, y + 2, size - 4, Game2DKit.Ink);
        }

        private void Redraw(int floor, int passives, int cons, int weather, int foe)
        {
            Ensure(ref _tt, _top, TopR); Ensure(ref _tl, _left, LeftR);
            if (_right != null) _right.gameObject.SetActive(false);
            // ── 上の帯 ──
            var p = new KitPix(TopR.width, TopR.height);
            Plate(p, 0, 2, 82, 28);                                                                    // 天気 (気圧計の代わり)
            string wn = weather >= 0 && weather < WeatherNames.Length ? WeatherNames[weather] : "W" + weather;
            p.AnyIcon(weather >= 0 && weather < WeatherIcons.Length ? WeatherIcons[weather] : "still", 3, 7, 18, Game2DKit.Ink);
            p.Text("天気", 24, 3, Game2DKit.Dim); p.Text(wn, 48, 3, weather == 0 ? Game2DKit.Ink : Game2DKit.Amber);
            p.Text("予報", 24, 16, Game2DKit.Dim); p.Text("???", 48, 16, Game2DKit.Faint);                // 予報は物資の段階で精度が変わる (未接続)
            // 待つ (その場で 1 手番)。 旧: 見本の気圧計
            if (Game2DKit.Tab != null) p.NineSlice(Game2DKit.Tab, 5, Game2DKit.Well, WaitR.x, WaitR.y, WaitR.width, WaitR.height); else p.Box(WaitR.x, WaitR.y, WaitR.width, WaitR.height, Game2DKit.Off, Game2DKit.Dark);
            p.TextC("待つ", WaitR, Game2DKit.Ink);
            p.Apply(_tt);
            // ── 左下 (所持品: 消耗品とパッシブを 1 つの欄にまとめる。 状態異常は戦闘の表示) ──
            p = new KitPix(LeftR.width, LeftR.height);
            int total = cons + passives;
            p.Text("所持品", 2, 0, Game2DKit.Dim); p.TextR(total + "個", 96, 0, Game2DKit.Ink);
            for (int i = 0; i < 10; i++) Slot(p, (i % 5) * 19, 13 + (i / 5) * 19, 17, i < total, i < cons ? "flare" : "relic");
            p.Apply(_tl);
        }
    }
}

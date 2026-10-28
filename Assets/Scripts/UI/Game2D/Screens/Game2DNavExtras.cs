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
    /// </summary>
    public class Game2DNavExtras : MonoBehaviour
    {
        public Game2DScreen screen;
        public Game2DModal modal;
        [SerializeField] private RectTransform _top, _left, _right;      // _right は旧版の残り (航行アイテムの欄は廃止。 シーンに残っていれば隠す)
        private Texture2D _tt, _tl;
        private static readonly RectInt TopR = new RectInt(96, 0, 104, 24), LeftR = new RectInt(0, 227, 100, 43);
        private int _hash = int.MinValue; private float _next; private float _e;

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
            bool show = e < 0.995f;
            foreach (var rt in new[] { _top, _left }) rt.gameObject.SetActive(show);
        }

        private void Update()
        {
            if (_top == null || _e > 0.5f) return;
            var gm = GameManager.Instance; if (gm == null) return;
            var sim = gm.CurrentPhase == GameManager.GamePhase.MapNavigation ? gm.FreeMap : null;
            var run = gm.Run;
            int floor = run != null ? run.currentFloor : 1, passives = run?.ownedPassiveItems?.Count ?? 0, cons = run?.ownedConsumables?.Count ?? 0;
            int weather = sim != null ? (int)sim.Weather : 0, foe = sim != null && sim.HasFoe && sim.FoeAlive ? (int)sim.FoeMode : -1;
            int h = floor * 7 + passives * 31 + cons * 131 + weather * 17 + (foe + 2) * 1009;
            if (h == _hash && Time.unscaledTime < _next) return;
            _hash = h; _next = Time.unscaledTime + 0.5f;
            Redraw(floor, passives, cons, weather, foe);
        }

        private static void Ensure(ref Texture2D tex, RectTransform rt, RectInt r)
        {
            if (rt != null) rt.sizeDelta = new Vector2(r.width, r.height);      // シーンに保存された大きさが古くても、 絵の大きさに合わせる (引き延ばし防止)
            if (tex != null || rt == null) return;
            tex = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DNavExtras" };
            var ri = rt.GetComponent<RawImage>(); if (ri != null) { ri.texture = tex; ri.enabled = true; }
        }

        private static readonly string[] WeatherIcons = { "still", "fog", "still", "sand", "snow", "hail", "crumble", "sink" };      // WeatherKind: None, Fog, Still, Sand, Snow, Hail, Crumble, Sink
        private static readonly string[] WeatherNames = { "CALM", "FOG", "STILL", "SAND", "SNOW", "HAIL", "CRUMBLE", "SINK" };

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
            Plate(p, 0, 2, 100, 20);                                                                   // 天気 (気圧計の代わり)
            string wn = weather >= 0 && weather < WeatherNames.Length ? WeatherNames[weather] : "W" + weather;
            p.AnyIcon(weather >= 0 && weather < WeatherIcons.Length ? WeatherIcons[weather] : "still", 3, 3, 18, Game2DKit.Ink);
            p.Text("WX", 24, 5, Game2DKit.Dim); p.Text(wn, 38, 5, weather == 0 ? Game2DKit.Ink : Game2DKit.Amber);
            p.Text("NEXT", 24, 13, Game2DKit.Dim); p.Text("???", 44, 13, Game2DKit.Faint);                // 予報は物資の段階で精度が変わる (未接続)
            for (int i = 0; i < 6; i++) p.Rect(68 + i * 5, 14, 4, 3, i < 3 ? Game2DKit.Blue : Game2DKit.Off);      // 気圧計 (見本)
            p.Apply(_tt);
            // ── 左下 (所持品: 消耗品とパッシブを 1 つの欄にまとめる。 状態異常は戦闘の表示) ──
            p = new KitPix(LeftR.width, LeftR.height);
            int total = cons + passives;
            p.Text("ITEMS", 2, 0, Game2DKit.Dim); p.Text("X" + total, 90 - Game2DKit.TextWidth("X" + total) + 6, 0, Game2DKit.Ink);
            for (int i = 0; i < 10; i++) Slot(p, (i % 5) * 19, 7 + (i / 5) * 19, 17, i < total, i < cons ? "flare" : "relic");
            p.Apply(_tl);
        }
    }
}

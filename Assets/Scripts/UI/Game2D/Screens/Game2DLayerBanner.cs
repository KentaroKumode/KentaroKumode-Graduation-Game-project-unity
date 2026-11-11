using GameLoop;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 層タイトルのバナー (issue #73・2026-11-11・<b>仮置き</b>)。 <see cref="LayerTitles.OnShow"/> を購読し、 層に入った時に
    /// 「タイトル ＋ サブタイトル」を液晶の GUI (480×270 の格子) の上寄りに出す。 字は既定のドット字 (<see cref="Game2DJpFont"/>)、 台はコード描画の板。
    /// 旧 <c>UI.Lcd.LayerTitleBanner</c> は旧液晶 UI (contentCamera の TMP) 用で、 Game2D の画面には載っていない。
    ///
    /// <para>動き: 網目 (4×4 Bayer) で入る 0.35 秒 → サブタイトルを 1 字ずつ打つ → 3 秒止まる → 網目で抜ける 0.5 秒。
    /// ロード画面・窓 (モーダル) が出ている間は待ち、 消えてから出す。 戦闘に入ったら消す。 BOT がランを動かしている間は出さない。
    /// 大きさ・位置は変えない (状態を localScale で表さない)。</para>
    /// </summary>
    public class Game2DLayerBanner : MonoBehaviour
    {
        private RawImage _img; private RectTransform _rt; private Texture2D _tex;
        private string _title, _sub;
        private bool _pending, _playing; private float _t; private int _lastFrame = -1;
        private int _w, _h;

        private const float InDur = 0.35f, TypeRate = 30f, Hold = 3.0f, OutDur = 0.5f;
        private const int PadX = 12, PadY = 5, TopY = 40;
        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        /// <summary>シーンに無ければ付ける (シーンは git 管理外なので、 builder を回していない環境でも動くように)。 窓 (Game2DModal) と同じ格子に、 窓より奥へ置く。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if (FindObjectOfType<Game2DLayerBanner>() != null) return;
            var m = FindObjectOfType<Game2DModal>(); if (m == null || m.transform.parent == null) return;
            var go = new GameObject("LayerBanner", typeof(RectTransform)); go.layer = 5;
            var rt = (RectTransform)go.transform; rt.SetParent(m.transform.parent, false);
            rt.SetSiblingIndex(m.transform.GetSiblingIndex());          // 窓の手前の兄弟 = 窓より奥に描く
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.sizeDelta = new Vector2(1, 1);
            go.AddComponent<Game2DLayerBanner>();
        }

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _img = gameObject.AddComponent<RawImage>(); _img.raycastTarget = false; _img.enabled = false;
        }

        private void OnEnable() => LayerTitles.OnShow += HandleShow;
        private void OnDisable() => LayerTitles.OnShow -= HandleShow;
        private void OnDestroy() { if (_tex != null) Destroy(_tex); }

        private void HandleShow(string title, string subtitle)
        {
            if (GameHooks.BotDriving || Application.isBatchMode) return;
            _title = title ?? ""; _sub = subtitle ?? "";
            _pending = true; _playing = false; _img.enabled = false;
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            bool battle = gm != null && gm.CurrentPhase == GameManager.GamePhase.Combat;
            bool gone = gm == null || gm.CurrentPhase == GameManager.GamePhase.Title || gm.CurrentPhase == GameManager.GamePhase.GameOver;
            if (gone || battle || GameHooks.BotDriving) { _pending = false; _playing = false; _img.enabled = false; return; }
            if (_pending)
            {
                if (Game2DLoadingScreen.Blocking || Game2DModal.Active) return;      // ロード画面・窓が消えてから出す
                _pending = false; _playing = true; _t = 0f; _lastFrame = -1; Layout();
                Game2DSfx.Play("ui_open", 0.6f);
            }
            if (!_playing) return;
            _t += Mathf.Min(0.05f, Time.unscaledDeltaTime);
            float typeDur = _sub.Length / TypeRate, total = InDur + typeDur + Hold + OutDur;
            if (_t >= total) { _playing = false; _img.enabled = false; return; }
            int frame = Mathf.FloorToInt(_t * 30f);                    // 30 コマ/秒で描き直す
            if (frame == _lastFrame) return;
            _lastFrame = frame;
            float cover = _t < InDur ? _t / InDur : _t > total - OutDur ? (total - _t) / OutDur : 1f;
            int chars = Mathf.Clamp(Mathf.FloorToInt((_t - InDur) * TypeRate), 0, _sub.Length);
            Redraw(cover, chars);
        }

        /// <summary>字の幅に合わせて板の大きさを決め、 格子の上寄り中央に置く。</summary>
        private void Layout()
        {
            int tw = Game2DJpFont.Width(_title), sw = Game2DJpFont.Width(_sub);
            _w = Mathf.Min(Game2DLayout.Width - 8, Mathf.Max(tw, sw) + PadX * 2);
            _h = PadY * 2 + Game2DJpFont.LineH * 2 + 4;
            if (_tex == null || _tex.width != _w || _tex.height != _h)
            {
                if (_tex != null) Destroy(_tex);
                _tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DLayerBanner" };
            }
            _img.texture = _tex; _img.enabled = true;
            _rt.sizeDelta = new Vector2(_w, _h);
            _rt.anchoredPosition = new Vector2((Game2DLayout.Width - _w) / 2, -TopY);
        }

        private void Redraw(float cover, int chars)
        {
            var p = new KitPix(_w, _h);
            p.Plate(0, 0, _w, _h);
            int LH = Game2DJpFont.LineH;
            int ty = PadY, sy = PadY + LH + 4;
            Game2DJpFont.Draw(p, _title, (_w - Game2DJpFont.Width(_title)) / 2, ty, Game2DKit.Ivory);
            p.Rect(PadX, ty + LH + 1, _w - PadX * 2, 1, Game2DKit.Faint);
            string s = _sub.Substring(0, chars);
            Game2DJpFont.Draw(p, s, (_w - Game2DJpFont.Width(_sub)) / 2, sy, Game2DKit.Dim);
            // 網目で入る / 抜ける: Bayer のしきい値より薄い所を透明にする
            if (cover < 1f)
            {
                float lv = Mathf.Clamp01(cover) * 16f;
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                        if (lv <= Bayer[(y & 3) * 4 + (x & 3)]) p.px[(_h - 1 - y) * _w + x] = default;
            }
            p.Apply(_tex);
        }
    }
}

using GameLoop;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 層に入った時の名前の帯 (issue #73 → #111 で作り直し・2026-11-12)。 <see cref="LayerTitles.OnShow"/> を購読し、 層に入った時に
    /// 液晶の GUI (480×270 の格子) の中央へ、 層の名前を大きく出す。 旧 <c>UI.Lcd.LayerTitleBanner</c> は旧液晶 UI (contentCamera の TMP) 用で、 Game2D の画面には載っていない。
    ///
    /// <para><b>形</b>: 窓も枠も持たない。 画面の横いっぱいに、 上下がぼける帯を薄く暗く敷き、 その中央に
    /// 「第N層」(小さい字) ─ 細い横線 ─ 名前 (大きい字) ─ 細い横線 ─ 添え書き (小さい字) を重ねる。 横線は端へ行くほど薄くなる。
    /// 題が「第N層　名前」の形 (全角の空白で区切る) なら前を小さい字・後を大きい字に分け、 区切りが無ければ全体を大きい字にする。</para>
    /// <para><b>字</b>: 液晶のドット字 (<see cref="Game2DJpFont"/>) のまま。 大きい字は 1 ドットを 2×2 の点で打った別の大きさの字
    /// (<see cref="Game2DJpFont.DrawLarge"/>)。 localScale では大きくしない。 色は落ち着いた象牙色と灰褐色だけ (原色は使わない)。</para>
    /// <para><b>動き</b>: 絵は出す時に 1 度だけ描き、 濃さ (RawImage の色の α) だけを動かす ── ゆっくり入る <see cref="InDur"/> 秒 → 留まる <see cref="Hold"/> 秒
    /// → ゆっくり抜ける <see cref="OutDur"/> 秒。 ロード画面・窓 (モーダル) が出ている間は待ち、 消えてから出す。 戦闘に入ったら消す。 BOT がランを動かしている間は出さない。</para>
    /// </summary>
    public class Game2DLayerBanner : MonoBehaviour
    {
        private RawImage _img; private RectTransform _rt; private Texture2D _tex;
        private string _title, _sub;
        private bool _pending, _playing; private float _t;

        private const float InDur = 1.4f, Hold = 2.6f, OutDur = 1.8f;
        /// <summary>帯の高さ (dot)。 中央の <see cref="CoreH"/> は一様に暗く、 その上下はなめらかに透明へ。</summary>
        private const int BandH = 112, CoreH = 64;
        /// <summary>大きい字の倍率 (1 ドット → Big×Big の点) と字の間。</summary>
        private const int Big = 2, Track = 3;
        /// <summary>帯のいちばん濃い所の暗さ (0〜1)。 少し暗くするだけ。</summary>
        private const float BandAlpha = 0.7f;

        // 色: 落ち着いた象牙色 (名前)・灰褐色 (横線・小さい字)。 原色・彩度の高い色は使わない
        private static readonly Color32 NameCol = new Color32(0xd8, 0xcd, 0xb2, 255), LineCol = new Color32(0x9a, 0x8c, 0x72, 255),
            SmallCol = new Color32(0xa8, 0x9c, 0x84, 255), SubCol = new Color32(0x84, 0x7a, 0x68, 255), Shade = new Color32(0x04, 0x03, 0x02, 255);

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
        private void OnDestroy() { if (_tex != null) Destroy(_tex); Busy = false; }

        /// <summary>帯を待っている・流している間 true (手引きの板は帯が済んでから出す・issue #86)。</summary>
        public static bool Busy { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Busy = false; }

        private void HandleShow(string title, string subtitle)
        {
            if (GameHooks.BotDriving || Application.isBatchMode) return;
            _title = title ?? ""; _sub = subtitle ?? "";
            _pending = true; _playing = false; _img.enabled = false;
        }

        private void Update() { Step(); Busy = _pending || _playing; }

        private void Step()
        {
            var gm = GameManager.Instance;
            bool battle = gm != null && gm.CurrentPhase == GameManager.GamePhase.Combat;
            bool gone = gm == null || gm.CurrentPhase == GameManager.GamePhase.Title || gm.CurrentPhase == GameManager.GamePhase.GameOver;
            if (gone || battle || GameHooks.BotDriving) { _pending = false; _playing = false; _img.enabled = false; return; }
            if (_pending)
            {
                if (Game2DLoadingScreen.Blocking || Game2DModal.Active) return;      // ロード画面・窓が消えてから出す
                _pending = false; _playing = true; _t = 0f;
                Build();
                _img.color = new Color(1f, 1f, 1f, 0f); _img.enabled = true;
                Game2DSfx.Play("ui_open", 0.5f);
            }
            if (!_playing) return;
            _t += Mathf.Min(0.05f, Time.unscaledDeltaTime);
            float total = InDur + Hold + OutDur;
            if (_t >= total) { _playing = false; _img.enabled = false; return; }
            float a = _t < InDur ? _t / InDur : _t > InDur + Hold ? (total - _t) / OutDur : 1f;
            _img.color = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(a)));
        }

        /// <summary>帯の絵を 1 度だけ描く (格子の横いっぱい・縦の中央)。</summary>
        private void Build()
        {
            int W = Game2DLayout.Width, H = BandH;
            if (_tex == null || _tex.width != W || _tex.height != H)
            {
                if (_tex != null) Destroy(_tex);
                _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DLayerBanner" };
            }
            _img.texture = _tex;
            _rt.sizeDelta = new Vector2(W, H);
            _rt.anchoredPosition = new Vector2(0, -(Game2DLayout.Height - H) / 2);

            // 題を「第N層」と名前に分ける (全角の空白で区切る)
            string head = "", name = _title.Trim();
            int sp = name.IndexOf('　');
            if (sp > 0) { head = name.Substring(0, sp).Trim(); name = name.Substring(sp + 1).Trim(); }
            bool big = Game2DJpFont.WidthLarge(name, Big, Track) <= W - 24;          // 横に入らない名前 (今は無い) だけ小さい字
            int k = big ? Big : 1, tr = big ? Track : 1;
            int bigW = Game2DJpFont.WidthLarge(name, k, tr);

            var p = new KitPix(W, H);
            // 帯: 中央は一様、 上下はなめらかに透明へ (smoothstep)。 色は黒に近い焦げ茶
            int edge = (H - CoreH) / 2;
            for (int y = 0; y < H; y++)
            {
                float d = y < edge ? y / (float)edge : y >= H - edge ? (H - 1 - y) / (float)edge : 1f;
                byte a = (byte)Mathf.RoundToInt(255f * BandAlpha * Mathf.SmoothStep(0f, 1f, d));
                var c = new Color32(Shade.r, Shade.g, Shade.b, a);
                for (int x = 0; x < W; x++) p.Set(x, y, c);
            }

            // 縦の並び: [第N層] 余白 [線] 余白 [名前 (大きい字)] 余白 [線] 余白 [添え書き]
            int cell = Game2DJpFont.Cell, nameH = cell * k;
            const int GapS = 5, GapL = 7;
            int blockH = (head.Length > 0 ? cell + GapS : 0) + 1 + GapL + nameH + GapL + 1 + (_sub.Length > 0 ? GapS + cell : 0);
            int y0 = (H - blockH) / 2;
            int lineW = Mathf.Min(W - 40, bigW + 120), lx = (W - lineW) / 2;
            if (head.Length > 0) { DrawSmall(p, head, y0, SmallCol); y0 += cell + GapS; }
            Line(p, lx, y0, lineW); y0 += 1 + GapL;
            Game2DJpFont.DrawLarge(p, name, (W - bigW) / 2, y0, k, tr, NameCol); y0 += nameH + GapL;
            Line(p, lx, y0, lineW); y0 += 1;
            if (_sub.Length > 0) DrawSmall(p, _sub, y0 + GapS, SubCol);
            p.Apply(_tex);
        }

        private static void DrawSmall(KitPix p, string s, int y, Color32 c) => Game2DJpFont.Draw(p, s, (p.w - Game2DJpFont.Width(s)) / 2, y, c);

        /// <summary>細い横線 (1 dot)。 中央は線の色、 両端へ向かって薄くなる (帯の上に α で重ねる)。</summary>
        private static void Line(KitPix p, int x, int y, int w)
        {
            for (int i = 0; i < w; i++)
            {
                float d = Mathf.Abs(i - (w - 1) * 0.5f) / ((w - 1) * 0.5f);      // 0 = 中央 / 1 = 端
                float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, d));
                var bg = p.px[(p.h - 1 - y) * p.w + x + i];
                // 帯の暗さの上に線を重ねる: 色は線の色へ寄せ、 α は濃い方
                var c = new Color32(LineCol.r, LineCol.g, LineCol.b, (byte)Mathf.Max(bg.a, Mathf.RoundToInt(255f * a)));
                if (a > 0.02f) p.Set(x + i, y, Color32.Lerp(new Color32(bg.r, bg.g, bg.b, c.a), c, a));
            }
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 資源バー (2026-10-23 作り直し)。 4 枚の絵 (<c>bar_layer1〜4.png</c>・どれも 470×270) を、 480×270 の GUI の左上 (0,0) へ等倍で重ねる。
    /// 絵の中の座標がそのまま GUI の座標になる (ずらさない)。
    /// <para>layer1 = 土台 (いつもある・板より手前) / layer2 = 物資の板 (航行・土台の後ろ) / layer4 = シールドの板 (戦闘・土台の後ろ) / layer3 = HP バーの本体 (左から割合ぶん見せる)。
    /// 物資とシールドは同じ位置の別の板で、 航行 / 戦闘で入れ替わる。 板の窓 (暗い帯) に割合ぶんの色を塗る。
    /// 行き先の見込みぶんは物資の窓の右端を点滅させ、 数字を板の上に出す。 ランプは絵のまま (点灯の切り替えは無い)。</para>
    /// </summary>
    public class Game2DResBar : MonoBehaviour
    {
        public Texture2D layer1, layer2, layer3, layer4;
        [Tooltip("アナログカウンターのシート (counter_strip.png・8×90・isReadable)")] public Texture2D counterStrip;

        // 絵の中の位置 (bar_layer*.png の画素・左上が原点)
        private static readonly RectInt Hp = new RectInt(154, 250, 144, 4);          // layer3 の見える範囲 = HP の窓
        private static readonly RectInt Win = new RectInt(193, 224, 105, 6);         // layer2 / 4 の窓 (暗い帯)
        private const int TexW = 470, TexH = 270, PanelTop = 219;
        // 数字の置き場 (絵の中の緑・赤のランプ = 8×8 の窓が 3 つ・間 1 画素)
        private static readonly Vector2Int HpNumPos = new Vector2Int(116, 248), MainNumPos = new Vector2Int(162, 227);

        [SerializeField] private RawImage _base, _hp, _supply, _shield;
        [SerializeField] private RawImage _supplyFill, _shieldFill, _previewRaw;
        [SerializeField] private Image _blink;
        [SerializeField] private RawImage _hpNum, _mainNum;
        private Game2DCounter _hpCnt, _mainCnt; private bool _cntSnap = true;
        private Texture2D _supplyTex, _shieldTex, _previewTex; private string _previewDrawn;

        private static readonly Color32[] SupplyRows = { C(0xff, 0xf0, 0xa0), C(0xf0, 0xc9, 0x3c), C(0xf0, 0xc9, 0x3c), C(0xd8, 0xa8, 0x2c), C(0xd8, 0xa8, 0x2c), C(0xb0, 0x84, 0x1c) };
        private static readonly Color32[] ShieldRows = { C(0xe4, 0xf6, 0xff), C(0x9f, 0xd0, 0xea), C(0x9f, 0xd0, 0xea), C(0x6d, 0xa6, 0xc8), C(0x6d, 0xa6, 0xc8), C(0x45, 0x78, 0x9c) };
        private static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        /// <summary>見た目を作る (builder から 1 度呼ぶ)。 親は 480×270 の格子の上の左上原点の枠。</summary>
        public void Build()
        {
            var rt = (RectTransform)transform;
            // 描く順 (下から): 物資の板 (layer2)・シールドの板 (layer4) → 土台 (layer1) → HP (layer3)。 板は土台より後ろ
            _supply = Full("layer2", rt, layer2);
            _shield = Full("layer4", rt, layer4);
            _base = Full("layer1", rt, layer1);
            _hp = Full("layer3", rt, layer3);
            _supplyFill = Fill("supplyFill", _supply.rectTransform);
            _shieldFill = Fill("shieldFill", _shield.rectTransform);
            var bgo = NewRect("blink", _supply.rectTransform, Win.x, Win.y, 2, Win.height);
            _blink = bgo.gameObject.AddComponent<Image>(); _blink.raycastTarget = false; _blink.enabled = false;
            _hpNum = Num("hpNum", rt, HpNumPos); _mainNum = Num("mainNum", rt, MainNumPos);
            var pr = NewRect("previewText", rt, 0, 0, 1, 1); _previewRaw = pr.gameObject.AddComponent<RawImage>(); _previewRaw.raycastTarget = false; _previewRaw.enabled = false;
        }

        private static RectTransform NewRect(string name, Transform parent, int x, int y, int w, int h)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var r = (RectTransform)go.transform; r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            return r;
        }

        private static RawImage Num(string name, Transform parent, Vector2Int pos)
        {
            var r = NewRect(name, parent, pos.x, pos.y, Game2DCounter.TotalW, Game2DCounter.GlyphH);
            var ri = r.gameObject.AddComponent<RawImage>(); ri.raycastTarget = false; ri.enabled = false;
            return ri;
        }

        private static RawImage Full(string name, Transform parent, Texture2D tex)
        {
            var r = NewRect(name, parent, 0, 0, TexW, TexH);
            var ri = r.gameObject.AddComponent<RawImage>(); ri.texture = tex; ri.raycastTarget = false;
            return ri;
        }

        /// <summary>板の窓に塗る帯。 板は (0,0) に等倍なので、 窓の座標をそのまま使う。</summary>
        private static RawImage Fill(string name, Transform panel)
        {
            var r = NewRect(name, panel, Win.x, Win.y, 1, Win.height);
            var ri = r.gameObject.AddComponent<RawImage>(); ri.raycastTarget = false; ri.enabled = false;
            return ri;
        }

        private static Texture2D RowsTex(Color32[] rows)
        {
            var t = new Texture2D(1, rows.Length, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DResBarFill" };
            var px = new Color32[rows.Length];
            for (int i = 0; i < rows.Length; i++) px[rows.Length - 1 - i] = rows[i];      // 画像は下から並ぶ
            t.SetPixels32(px); t.Apply(false);
            return t;
        }

        private void OnDestroy()
        {
            if (_supplyTex != null) Destroy(_supplyTex);
            if (_shieldTex != null) Destroy(_shieldTex);
            if (_previewTex != null) Destroy(_previewTex);
            _hpCnt?.Destroy(); _mainCnt?.Destroy();
        }

        /// <param name="hpFrac">HP の割合 0..1</param>
        /// <param name="frac">物資 (航行) / シールド (戦闘) の割合 0..1</param>
        /// <param name="battle">戦闘ならシールドの板、 航行なら物資の板</param>
        /// <param name="preFrac">行き先の見込みで減る割合 (点滅・航行のみ)。 無ければ 0</param>
        /// <param name="hpNum">HP の数 (赤→緑のランプの位置・3 桁)</param>
        /// <param name="mainNum">物資 (航行) / シールド (戦闘) の数 (3 桁)</param>
        public void Set(float hpFrac, float frac, bool battle, float preFrac, string previewText = null, int hpNum = 0, int mainNum = 0)
        {
            SetCounters(hpNum, mainNum);
            // HP: layer3 を左から割合ぶんだけ見せる (残りがあれば 1 画素は残す)
            hpFrac = Mathf.Clamp01(hpFrac);
            int hw = hpFrac <= 0f ? 0 : Mathf.Max(1, Mathf.RoundToInt(Hp.width * hpFrac));
            var hr = _hp.rectTransform; hr.anchoredPosition = new Vector2(Hp.x, -Hp.y); hr.sizeDelta = new Vector2(hw, Hp.height);
            _hp.uvRect = new Rect(Hp.x / (float)TexW, 1f - (Hp.y + Hp.height) / (float)TexH, hw / (float)TexW, Hp.height / (float)TexH);
            _hp.enabled = hw > 0;

            // 板: 物資 / シールドの入れ替え
            _supply.enabled = !battle; _shield.enabled = battle;
            if (_supplyTex == null) { _supplyTex = RowsTex(SupplyRows); _supplyFill.texture = _supplyTex; }
            if (_shieldTex == null) { _shieldTex = RowsTex(ShieldRows); _shieldFill.texture = _shieldTex; }
            var f = battle ? _shieldFill : _supplyFill;
            frac = Mathf.Clamp01(frac);
            int wpx = frac <= 0f ? 0 : Mathf.Max(1, Mathf.RoundToInt(Win.width * frac));
            f.rectTransform.sizeDelta = new Vector2(wpx, Win.height); f.enabled = wpx > 0;
            (battle ? _supplyFill : _shieldFill).enabled = false;

            // 使う予定ぶんが点滅 (180ms ごとに明・暗)・数字は板の上
            bool nav = !battle;
            if (nav && preFrac > 0 && wpx > 0)
            {
                int lose = Mathf.Min(wpx, Mathf.Max(2, Mathf.RoundToInt(Win.width * preFrac)));
                bool on = Mathf.FloorToInt(Time.unscaledTime / 0.18f) % 2 == 0;
                _blink.enabled = true; _blink.color = on ? new Color32(0xff, 0xf2, 0xc8, 255) : new Color32(0x1b, 0x15, 0x11, 255);
                var br = _blink.rectTransform; br.sizeDelta = new Vector2(lose, Win.height); br.anchoredPosition = new Vector2(Win.x + wpx - lose, -Win.y);
            }
            else _blink.enabled = false;
            SetPreviewText(nav ? previewText : null);
        }

        private void SetCounters(int hpNum, int mainNum)
        {
            if (counterStrip == null || _hpNum == null) return;
            if (_hpCnt == null)
            {
                var bg = new Color32(29, 29, 32, 255);
                _hpCnt = new Game2DCounter(counterStrip, bg); _mainCnt = new Game2DCounter(counterStrip, bg);
                _hpNum.texture = _hpCnt.Texture; _mainNum.texture = _mainCnt.Texture; _hpNum.enabled = _mainNum.enabled = true;
            }
            float dt = Mathf.Min(0.1f, Time.unscaledDeltaTime);
            _hpCnt.Tick(hpNum, dt, _cntSnap); _mainCnt.Tick(mainNum, dt, _cntSnap); _cntSnap = false;
        }

        /// <summary>見込みの数字: 板の上・窓の右ぞろえ。 3×5 の字を 2 倍 (色 #ffeebe)。</summary>
        private void SetPreviewText(string text)
        {
            if (_previewRaw == null) return;
            if (string.IsNullOrEmpty(text)) { _previewRaw.enabled = false; return; }
            if (text != _previewDrawn)
            {
                _previewDrawn = text;
                if (_previewTex != null) Destroy(_previewTex);
                _previewTex = Game2DPixelFont.RenderSmall(text, new Color32(0xff, 0xee, 0xbe, 255));
                _previewRaw.texture = _previewTex;
                var r = _previewRaw.rectTransform; r.sizeDelta = new Vector2(_previewTex.width * 2, _previewTex.height * 2);
                int w = (text.Length * 4 - 1) * 2;
                r.anchoredPosition = new Vector2(Win.x + Win.width - 2 - w, -(PanelTop - 12));
            }
            _previewRaw.enabled = true;
        }
    }
}

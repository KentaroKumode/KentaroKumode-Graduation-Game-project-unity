using GameLoop;
using MapSystem.FreeMove;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 航行の液晶 (地図) の本体 (2026-10-19)。 map-prototype.html の <c>drawLCD</c> / <c>setScreen</c> を、 手番制の <see cref="FreeMapSim"/> の上に移した。
    ///
    /// <para>480×270 (GUI の 1 dot) のバッファへ毎フレーム 1 dot ずつ作図し (<c>Plot</c>)、 天候・照明・偵察の演出 (<c>Fx</c>) を重ね、
    /// 航行を映すカメラの正面に置いた Quad のテクスチャへ流す。 それが液晶の RT に映り、 Quad のシェーダ (残像・湾曲・ノイズ・格子) が掛かる。
    /// 旧来の地図のスプライト (点・道・飾り) は、 この Quad の奥に隠れる。</para>
    ///
    /// <para>操作 (<c>Input</c>): 点へカーソルを乗せると行き先の見込み、 クリックで隣の点へ 1 歩。 ホイールで X1 / X2 / X3。</para>
    /// </summary>
    public partial class Game2DMapView : MonoBehaviour
    {
        [Tooltip("液晶の絵を映す Quad (航行を映すカメラの子)。 builder が作る")]
        public MeshRenderer plotQuad;
        [Tooltip("GUI の拡大率 (マウス位置 → dot の換算)")]
        public Game2DScreen screen;

        /// <summary>崩れる地平の揺れで 1 dot ずらす液晶の Quad (Game2D の LCD Quad)。</summary>
        public Transform lcdQuad;

        private Camera _cam;

        // ── 視点 (HTML の LS / LOX / LOY / ZOOM / CAM) ──
        private float _ls = 1f;                  // 1 配置単位 = 何 dot
        private float _lox, _loy;
        private static readonly float[] ZoomK = { 1.3f, 2f, 4f };     // X1 / X2 / X3 の倍率 (基準の縮尺に対して)
        private int _zoom = 2;                   // 1 = 全体 / 2 = 既定 / 3 = 寄り
        private Vector2 _camPos;                 // 視点 (配置の座標・板の中心に来る点)
        private Vector2 _meP;                    // 描く自分の位置 (配置の座標・点から点へなめらかに動く)
        private bool _meInit;
        private Vector2 _me;                     // 自分の位置 (dot)
        private float _now, _last;
        private float _hopUnit = 1f;             // 道 1 本の平均の長さ (配置の単位)。 「歩数」を距離に直す物差し
        private int _viewFloor = -1;
        private int _viewNodes = -1;

        private void OnEnable()
        {
            if (GameManager.Instance != null) GameManager.Instance.OnNavFx += OnNavFx;
            EnsureCanvas();
            BindTexture();
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null) GameManager.Instance.OnNavFx -= OnNavFx;
        }

        private void OnDestroy() { if (_tex != null) Destroy(_tex); }

        private void BindTexture()
        {
            if (plotQuad == null || _tex == null) return;
            var mpb = new MaterialPropertyBlock();
            plotQuad.GetPropertyBlock(mpb);
            mpb.SetTexture("_MainTex", _tex);
            plotQuad.SetPropertyBlock(mpb);
        }

        [Tooltip("航行だけを試す: 点に着いても戦闘・イベント・ショップなどへ入らない (GameManager.NavOnly)。 2026-10-31 に戦闘と各場面の画面がつながったので既定は切")] public bool navOnly = false;

        private void LateUpdate()
        {
            GameManager.NavOnly = navOnly;
            EnsureCanvas();
            var gm = GameManager.Instance;
            var sim = gm != null && gm.CurrentPhase == GameManager.GamePhase.MapNavigation ? gm.FreeMap : null;
            if (plotQuad != null) plotQuad.enabled = sim != null && !Battle.Game2DBattleFx.Showing;      // 戦闘演出を出している間は地図を隠す
            if (sim == null)
            {
                ShakeDots = Vector2.zero; ApplyShake();
                _flareActive = false; _reconActive = false; _meInit = false; _hover = -1; _camManual = false; _dragOn = false;
                return;
            }
            if (_cam == null)
            {
                var router = FindObjectOfType<PhaseSceneRouter>();
                _cam = router != null ? router.targetCamera : null;
            }
            if (_cam != null) { _cam.orthographic = true; _cam.orthographicSize = H / 2f / 16f; }     // 1 unit = 32 RT 画素 = 16 dot → 270 dot = 16.875 unit の縦視野

            _now = Time.unscaledTime;
            float dt = _last > 0 ? Mathf.Min(0.1f, _now - _last) : 0f; _last = _now;
            _lc = Pals[Mathf.Clamp(sim.Floor, 1, 7)];

            PrepareLayout(sim);
            SetScreen(sim, dt);
            HandleInput(sim, dt);

            Fill(_lc.bg);
            Plot(sim);
            WeatherFx(sim, dt);
            ReconFx();
            FlareFx(sim, dt);
            DrawSelf();                       // 自分は最前面 (天候・照明・偵察の演出より手前)
            Upload();
            ApplyShake();
        }

        /// <summary>崩れる地平の揺れ: 液晶を 1 dot (= RT の 2 画素 = 2/32 ユニット) ずらす。</summary>
        private void ApplyShake()
        {
            if (lcdQuad != null) lcdQuad.localPosition = new Vector3(ShakeDots.x, -ShakeDots.y, 0f) * (2f / 32f);
        }

        // =====================================================================
        //  視点 (HTML の setScreen)
        // =====================================================================

        private void PrepareLayout(FreeMapSim sim)
        {
            if (_viewFloor == sim.Floor && _viewNodes == sim.Layout.Count) return;
            _viewFloor = sim.Floor; _viewNodes = sim.Layout.Count;
            float sum = 0; int n = 0;
            foreach (var (a, b) in sim.Layout.roads) { sum += (float)Vec2.Dist(sim.Layout.pos[a], sim.Layout.pos[b]); n++; }
            _hopUnit = n > 0 ? sum / n : 1f;
            _meInit = false;
            _bg = null;
        }

        /// <summary>配置の座標 → dot。 HTML の <c>sp()</c> (整数 dot の中心 +0.5)。</summary>
        private Vector2 Sp(double x, double y) => new Vector2(Mathf.Floor(_lox + (float)x * _ls + 0.5f) + 0.5f, Mathf.Floor(_loy + (float)y * _ls + 0.5f) + 0.5f);
        private Vector2 Sp(Vec2 p) => Sp(p.x, p.y);

        private void SetScreen(FreeMapSim sim, float dt)
        {
            float mapW = (float)FreeMapParams.Width, mapH = (float)FreeMapParams.Height;
            var target = sim.Layout.pos[sim.Me];
            if (!_meInit) { _meP = new Vector2((float)target.x, (float)target.y); _meInit = true; }
            else _meP = Vector2.Lerp(_meP, new Vector2((float)target.x, (float)target.y), 1f - Mathf.Exp(-14f * dt));   // 1 歩ぶんをなめらかに動く
            if (!_camManual) _camPos = _meP;                    // 拡大中の自由な視点でなければ、 自分に追従する

            float iw = W, ih = H;
            float padU = 2f * _hopUnit, padV = 2f * _hopUnit;
            _ls = Mathf.Min(iw / mapW, ih / (mapH + 2 * padV)) * ZoomK[_zoom - 1];      // 基準 = 上下の余白ごと全体が収まる縮尺。 X1 はそれより少し寄る (全体は収まらないが、 視点を動かして見回せる)
            float hx = iw / (2 * _ls), hy = ih / (2 * _ls), spanX = (mapW + 2 * padU) * _ls, spanY = (mapH + 2 * padV) * _ls;      // 上下にも余白 (端の点が画面の縁に貼りつかない)
            // 全体が画面に収まる時 (X1) は、 中央に置く。 ただし視点を自分で動かしている間は、 画面の 1/5 まで ずらせる (HUD に隠れた所を覗くため)
            float slackX = _camManual ? 0.2f * iw / _ls : 0f, slackY = _camManual ? 0.2f * ih / _ls : 0f;
            float cx = spanX <= iw ? Mathf.Clamp(_camPos.x, mapW / 2f - slackX, mapW / 2f + slackX) : Mathf.Max(hx - padU, Mathf.Min(mapW - hx + padU, _camPos.x));
            float cy = spanY <= ih ? Mathf.Clamp(_camPos.y, mapH / 2f - slackY, mapH / 2f + slackY) : Mathf.Max(hy - padV, Mathf.Min(mapH - hy + padV, _camPos.y));
            if (!_camManual) { if (spanX <= iw) cx = mapW / 2f; if (spanY <= ih) cy = mapH / 2f; }
            _camPos = new Vector2(cx, cy);                      // 視点の中心を、 実際に映せる範囲へ丸めて保存する (端で押した分を溜めない)
            _lox = Mathf.Round(spanX <= iw ? (iw - spanX) / 2f + padU * _ls - (cx - mapW / 2f) * _ls : Fit(0, iw, spanX, cx + padU) + padU * _ls);
            _loy = Mathf.Round(spanY <= ih ? (ih - spanY) / 2f + padV * _ls - (cy - mapH / 2f) * _ls : Fit(0, ih, spanY, cy + padV) + padV * _ls);
            _me = Sp(_meP.x, _meP.y);
        }

        private float Fit(float lo, float len, float span, float c)
            => span <= len ? lo + (len - span) / 2f : Mathf.Min(lo, Mathf.Max(lo + len - span, lo + len / 2f - c * _ls));
    }
}

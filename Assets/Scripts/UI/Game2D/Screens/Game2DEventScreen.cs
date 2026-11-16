using GameLoop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// イベントの画面を<b>液晶の中身として</b>写す板 (2026-11-16・作者の承認)。 絵 (背景・本文の板・選択肢の列) を描くのは <see cref="EventScreenView"/>、
    /// 中身を詰めるのは <see cref="Game2DPhaseScreens"/> (<see cref="Show"/> / <see cref="Hide"/> を呼ぶ)。 ここは写す板・マウス・入りの網目だけを持つ。
    ///
    /// <para><b>液晶の中身として描く理由</b>: 背景の絵を見せる画面なので、 戦闘・地図・結末と同じ液晶の効果 (走査線・湾曲・にじみ・ノイズ) を掛けたい。
    /// 窓 (<see cref="Game2DModal"/>) は GUI の層で、 液晶の効果の外になる (2026-11-16 の実機の止め絵で、 イベントの画面だけ質感が違った)。
    /// 描き方は読み込みの画面 (<see cref="Game2DLoadingScreen"/>)・結末の画面 (<see cref="Game2DEndingScreen"/>) と同じ: 地図の板の複製を少し手前に置き
    /// (読み込み −0.1・結末 −0.12 より奥の −0.08)、 Game2D/LoadingBlend の網目で入る (閉じる時はすぐ消す)。</para>
    ///
    /// <para>出している間は <see cref="Game2DModal.Active"/> が true を返す (地図・戦闘・帯などの後ろの画面は今までどおり手を止める) ── 窓で出していた時と同じ働き。
    /// 計器盤 (GUI) は読み込み・結末と同じく隠す (<see cref="Game2DHud"/>・液晶の全面を使う)。 BOT がランを動かしている間は出さない。</para>
    ///
    /// <para><b>マウス</b>: 液晶は樽型に湾曲しているので、 湾曲を戻した液晶の dot (<see cref="Game2DPointer.TryLcdDot"/>) で当てる
    /// (地図・戦闘の液晶と同じ式)。</para>
    /// </summary>
    public class Game2DEventScreen : MonoBehaviour
    {
        public const int W = Game2DLayout.Width, H = Game2DLayout.Height;
        /// <summary>入りの網目の長さ (秒)。</summary>
        private const float FadeSec = 0.25f;

        private static Game2DEventScreen _inst;
        private static bool _shown;
        /// <summary>出している間 true。</summary>
        public static bool Showing => _shown && _inst != null;

        private EventScreenView _view;
        private MeshRenderer _quad; private Texture2D _tex; private KitPix _pix; private MaterialPropertyBlock _mpb;
        private Game2DMapView _mv;
        private bool _dirty; private float _fadeT;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _inst = null; _shown = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if (FindObjectOfType<Game2DEventScreen>() != null) return;
            var mv = FindObjectOfType<Game2DMapView>(); if (mv == null) return;
            mv.gameObject.AddComponent<Game2DEventScreen>();
        }

        private void Awake() { _inst = this; }

        /// <summary>液晶の中身のカメラを、 イベントの間も生かす (結末の画面と同じ・シーンの設定で既に入っていれば何もしない)。</summary>
        private void Start()
        {
            _mv = FindObjectOfType<Game2DMapView>();
            if (_mv != null && _mv.plotQuad != null) PhaseDirector.AlsoShowFor(_mv.plotQuad.transform, GameManager.GamePhase.EventEncounter);
        }

        private void OnDisable() { if (_inst == this && _shown) HideNow(); }

        private void OnDestroy()
        {
            if (_inst == this) { _inst = null; _shown = false; }
            if (_tex != null) Destroy(_tex);
            if (_quad != null) { if (_quad.sharedMaterial != null && _quad.sharedMaterial.name == "Game2DEventBlend") Destroy(_quad.sharedMaterial); Destroy(_quad.gameObject); }
        }

        /// <summary>中身 view を出す (出ていれば描き直すだけ・入りの網目はやり直さない)。 板が作れない (地図の板が無い) 時は false。</summary>
        public static bool Show(EventScreenView view)
        {
            var s = _inst; if (s == null || view == null || !s.EnsureQuad()) return false;
            s._view = view; s._dirty = true;
            if (!_shown)
            {
                _shown = true; s._fadeT = 0f;
                if (s._tex == null) { s._tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DEvent" }; s._pix = new KitPix(W, H); }
                s.SetBlend(); s.Redraw(); s.ApplyFade();
                s._quad.enabled = true;
                Game2DSfx.Play("ui_open");
            }
            return true;
        }

        /// <summary>閉じる (すぐ消す)。 出ていなければ何もしない。</summary>
        public static void Hide() { if (_shown && _inst != null) { Game2DSfx.Play("ui_close"); _inst.HideNow(); } else _shown = false; }

        private void HideNow() { _shown = false; if (_quad != null) _quad.enabled = false; }

        private bool EnsureQuad()
        {
            if (_quad != null) return true;
            if (_mv == null) _mv = FindObjectOfType<Game2DMapView>();
            if (_mv == null || _mv.plotQuad == null) return false;
            var src = _mv.plotQuad;
            var go = new GameObject("EventPlotQuad") { layer = src.gameObject.layer };
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localPosition = src.transform.localPosition + new Vector3(0, 0, -0.08f);      // 地図より手前・読み込み (−0.1)・結末 (−0.12) より奥
            go.transform.localRotation = src.transform.localRotation; go.transform.localScale = src.transform.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            _quad = go.AddComponent<MeshRenderer>(); _quad.enabled = false;
            var sh = Shader.Find("Game2D/LoadingBlend");
            _quad.sharedMaterial = sh != null ? new Material(sh) { name = "Game2DEventBlend" } : src.sharedMaterial;
            return true;
        }

        private void Update()
        {
            if (!_shown) return;
            if (GameHooks.BotDriving || Application.isBatchMode) { HideNow(); return; }
            if (_fadeT < FadeSec) { _fadeT += Mathf.Min(0.05f, Time.unscaledDeltaTime); ApplyFade(); }
            if (_view != null && !Game2DTutorial.Blocking && !Game2DModal.WindowActive)      // 手引きの板・窓が上にある間は押させない
            {
                var local = default(Vector2);
                bool inside = _mv != null && Game2DPointer.TryLcdDot(_mv.screen, out local);      // 湾曲を戻した液晶の dot (変換は TryLcdDot の中)
                bool click = inside && _fadeT >= FadeSec && Input.GetMouseButtonDown(0);
                if (_view.Pointer(local, inside, click)) _dirty = true;
            }
            if (_dirty && _shown) Redraw();
        }

        private void Redraw()
        {
            _dirty = false;
            if (_pix == null || _view == null) return;
            _view.Draw(_pix, new RectInt(0, 0, W, H), 0);
            _tex.SetPixels32(_pix.px); _tex.Apply(false);
        }

        private void SetBlend()
        {
            if (_quad == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_mpb);
            _mpb.SetTexture("_MainTex", _tex); _mpb.SetVector("_Dots", new Vector4(W, H, 0, 0)); _mpb.SetFloat("_UsePrev", 0f);
            _quad.SetPropertyBlock(_mpb);
        }

        private void ApplyFade()
        {
            if (_quad == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_mpb); _mpb.SetFloat("_Fade", Mathf.Clamp01(_fadeT / FadeSec)); _quad.SetPropertyBlock(_mpb);
        }
    }
}

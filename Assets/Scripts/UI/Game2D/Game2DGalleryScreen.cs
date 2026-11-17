using GameLoop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// ギャラリーの画面 (タイトルの「GALLERY」から入る・2026-11-17 作者の決定 Captures/gallery_layout/decisions.md)。 タイトルに窓を重ねず、
    /// <b>液晶の中身を画面ごと切り替える</b> (背景は書庫の絵・液晶の効果が掛かる)。 絵と操作は <see cref="GalleryView"/>、 ここは写す板・入力・開け閉めだけを持つ。
    ///
    /// <para>写し方はイベントの画面 (<see cref="Game2DEventScreen"/>) と同じ (Game2D/LoadingBlend の網目で入る・閉じる時はすぐ消す) だが、 タイトルの間は
    /// 地図の板のカメラ (GameplayContentCamera) が止まっているので、 板は<b>タイトルの中身のカメラ</b> (<see cref="UI.Codex.CodexView.contentCamera"/>・
    /// 層 LcdContent) の手前に置く。 板は深度を書くので、 後ろのタイトル (ロゴ・ボタン・背景) は隠れる。 シーンは触らない (実行時に足す)。</para>
    ///
    /// <para>出している間 (と閉じたフレーム) は <see cref="Blocking"/> が true: <see cref="Game2DModal.Active"/> が true を返し (Esc で設定の窓が開かない)、
    /// タイトルのボタン (<see cref="SpriteButton.InputLocked"/>) と GameManager のキー (<see cref="GameManager.InputBlockedByScreen"/>) を止める。
    /// タイトルでない時・BOT がランを動かしている間は出さない。</para>
    ///
    /// <para><b>マウス</b>: 液晶は樽型に湾曲しているので、 湾曲を戻した液晶の dot (<see cref="Game2DPointer.TryLcdDot"/>) で当てる。</para>
    /// </summary>
    public class Game2DGalleryScreen : MonoBehaviour
    {
        public const int W = Game2DLayout.Width, H = Game2DLayout.Height;
        private const float FadeSec = 0.25f;
        /// <summary>板をタイトルの中身のカメラから離す距離 (カメラの手前 0.3 より奥・タイトルの部品 (z 0〜5 = カメラから 10〜15) より手前)。</summary>
        private const float QuadDepth = 5f;

        private static Game2DGalleryScreen _inst;
        private static bool _shown;
        private static int _endFrame = -1;
        /// <summary>出している間 true。</summary>
        public static bool Showing => _shown && _inst != null;
        /// <summary>出している間と、 閉じたフレーム (同じ Esc で設定の窓が開かないように) は true。</summary>
        public static bool Blocking => Showing || _endFrame == Time.frameCount;

        private readonly GalleryView _view = new GalleryView();
        private MeshRenderer _quad; private Texture2D _tex; private KitPix _pix; private MaterialPropertyBlock _mpb;
        private Game2DMapView _mv;
        private bool _dirty; private float _fadeT;
        private bool _prevLock, _prevBlock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _inst = null; _shown = false; _endFrame = -1; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if (FindObjectOfType<Game2DGalleryScreen>() != null) return;
            var mv = FindObjectOfType<Game2DMapView>(); if (mv == null) return;
            mv.gameObject.AddComponent<Game2DGalleryScreen>();
        }

        private void Awake() { _inst = this; }

        private void OnDisable() { if (_inst == this && _shown) CloseNow(false); }

        private void OnDestroy()
        {
            if (_inst == this) { if (_shown) CloseNow(false); _inst = null; _shown = false; }
            if (_tex != null) Destroy(_tex);
            if (_quad != null) { if (_quad.sharedMaterial != null && _quad.sharedMaterial.name == "Game2DGalleryBlend") Destroy(_quad.sharedMaterial); Destroy(_quad.gameObject); }
        }

        private static bool CanOpen()
        {
            if (Application.isBatchMode || GameHooks.BotDriving) return false;
            var gm = GameManager.Instance;
            return gm == null || gm.CurrentPhase == GameManager.GamePhase.Title;
        }

        /// <summary>ギャラリーを開く (入口から)。 開けなければ false (Game2D が無い・タイトルでない・ほかの窓が出ている)。</summary>
        public static bool Open()
        {
            if (_inst == null) { Attach(); if (_inst == null) _inst = FindObjectOfType<Game2DGalleryScreen>(); }
            var s = _inst;
            if (s == null || _shown || Game2DModal.Active || !CanOpen() || !s.EnsureQuad()) return false;
            s._view.Reset();
            if (s._tex == null) { s._tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DGallery" }; s._pix = new KitPix(W, H); }
            _shown = true; s._fadeT = 0f;
            s._prevLock = SpriteButton.InputLocked; SpriteButton.InputLocked = true;
            s._prevBlock = GameManager.InputBlockedByScreen; GameManager.InputBlockedByScreen = true;
            s.SetBlend(); s.Redraw(); s.ApplyFade();
            s._quad.enabled = true;
            Game2DSfx.Play("ui_open");
            return true;
        }

        private void CloseNow(bool sound)
        {
            if (!_shown) return;
            _shown = false; _endFrame = Time.frameCount;
            if (_quad != null) _quad.enabled = false;
            SpriteButton.InputLocked = _prevLock;
            GameManager.InputBlockedByScreen = _prevBlock;
            if (sound) Game2DSfx.Play("ui_close");
        }

        /// <summary>板を作る: タイトルの中身のカメラの子に、 地図の板と同じ形 (網目・大きさ) で置く。</summary>
        private bool EnsureQuad()
        {
            if (_quad != null) return true;
            if (_mv == null) _mv = FindObjectOfType<Game2DMapView>();
            if (_mv == null || _mv.plotQuad == null) return false;
            Camera cam = null;
            var cv = FindObjectOfType<UI.Codex.CodexView>(true);
            if (cv != null) cam = cv.contentCamera;
            if (cam == null) { var lcd = FindObjectOfType<UI.Lcd.LcdScreen>(); if (lcd != null) cam = lcd.contentCamera; }
            if (cam == null) return false;
            var src = _mv.plotQuad;
            int layer = LayerMask.NameToLayer("LcdContent");
            if (layer < 0 || (cam.cullingMask & (1 << layer)) == 0)
                for (int i = 0; i < 32; i++) if ((cam.cullingMask & (1 << i)) != 0) { layer = i; break; }
            var go = new GameObject("GalleryPlotQuad") { layer = Mathf.Max(0, layer) };
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, QuadDepth);
            go.transform.localRotation = Quaternion.identity;
            var ls = src.transform.lossyScale; var ps = cam.transform.lossyScale;
            go.transform.localScale = new Vector3(ls.x / Mathf.Max(1e-4f, ps.x), ls.y / Mathf.Max(1e-4f, ps.y), 1f);      // 地図の板と同じ大きさ (状態の違いを表す拡大ではない)
            go.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            _quad = go.AddComponent<MeshRenderer>(); _quad.enabled = false;
            var sh = Shader.Find("Game2D/LoadingBlend");
            _quad.sharedMaterial = sh != null ? new Material(sh) { name = "Game2DGalleryBlend" } : src.sharedMaterial;
            return true;
        }

        private void Update()
        {
            if (!_shown) return;
            if (!CanOpen()) { CloseNow(false); return; }
            if (_fadeT < FadeSec) { _fadeT += Mathf.Min(0.05f, Time.unscaledDeltaTime); ApplyFade(); }
            if (!Game2DModal.WindowActive && !Game2DTutorial.Blocking)
            {
                if (_view.Keys()) _dirty = true;
                if (_view.closeRequested) { CloseNow(true); return; }
                var local = default(Vector2);
                bool inside = _mv != null && Game2DPointer.TryLcdDot(_mv.screen, out local);
                bool ready = _fadeT >= FadeSec;
                bool click = inside && ready && Input.GetMouseButtonDown(0);
                float wheel = ready ? Input.mouseScrollDelta.y : 0f;
                if (_view.Pointer(local, inside, click, wheel)) _dirty = true;
                if (_view.closeRequested) { CloseNow(true); return; }
            }
            if (_dirty) Redraw();
        }

        private void Redraw()
        {
            _dirty = false;
            if (_pix == null) return;
            _view.Draw(_pix);
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

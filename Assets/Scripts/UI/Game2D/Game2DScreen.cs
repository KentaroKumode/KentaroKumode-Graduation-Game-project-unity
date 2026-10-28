using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 2D 画面の拡大率を<b>整数倍</b>に保つ (2026-10-19・疑似 3D をやめた)。
    ///
    /// <para>画面は 2 層でできている。 ① 液晶の中身 (<see cref="UI.Lcd.LcdScreen"/> の RT・既定 960×540) を
    /// 正射影のカメラで <see cref="lcdQuad"/> に映す。 ② その上に UI の Canvas (480×270 の格子の GUI) を重ねる。
    /// GUI の 1 dot は RT の 2 画素で、 どちらも<b>同じ整数倍</b>で画面に出す ── ドット絵の密度を崩さない。</para>
    ///
    /// <para>画面が 960×540 に満たない時だけ小数倍率になる (開発中の小さいウィンドウ用)。</para>
    /// </summary>
    [ExecuteAlways]
    public class Game2DScreen : MonoBehaviour
    {
        [Tooltip("液晶の RT を映す正射影カメラ")]
        public Camera screenCamera;
        [Tooltip("RT を貼った Quad。 RT の画素数 / PPU の大きさ (既定 30×16.875) に自動で合わせる")]
        public Transform lcdQuad;
        [Tooltip("GUI の Canvas (Screen Space - Overlay)")]
        public Canvas canvas;
        [Tooltip("480×270 の格子の根 (Canvas の子・中央に置く)")]
        public RectTransform gridRoot;
        [Tooltip("画面の仕上げ (Game2D/LcdFx) のマテリアル。 RT の大きさを流す")]
        public Material fxMaterial;
        [Tooltip("エディタだけ、 整数倍にせず Game ビューいっぱいに合わせる (ビルドでは常に整数倍)")]
        public bool fitInEditor = true;

        public const int RtWidth = 960, RtHeight = 540, Ppu = 32;

        /// <summary>RT の 1 画素が画面の何画素か。</summary>
        public float PixelsPerRtPixel { get; private set; } = 1f;
        /// <summary>GUI の 1 dot (480×270 の格子) が画面の何画素か。</summary>
        public float PixelsPerGuiDot => PixelsPerRtPixel * 2f;

        private int _lastW, _lastH;

        private void OnEnable() => Apply();

        private void LateUpdate()
        {
            if (Screen.width != _lastW || Screen.height != _lastH) Apply();
        }

        public void Apply()
        {
            _lastW = Screen.width; _lastH = Screen.height;
            float fit = Mathf.Min(Screen.width / (float)RtWidth, Screen.height / (float)RtHeight);
            float s = fit >= 1f ? Mathf.Floor(fit) : Mathf.Max(0.25f, fit);
#if UNITY_EDITOR
            // エディタの Game ビューは画面より小さい (タブ・ツールバー分) ので、 確認用に小数倍率で画面へ合わせる (ビルドでは使わない)
            if (fitInEditor) s = Mathf.Max(0.25f, fit);
#endif
            PixelsPerRtPixel = s;

            if (lcdQuad != null)
            {
                lcdQuad.localScale = new Vector3(RtWidth / (float)Ppu, RtHeight / (float)Ppu, 1f);
                lcdQuad.localPosition = Vector3.zero;
                lcdQuad.localRotation = Quaternion.identity;
            }
            if (screenCamera != null)
            {
                screenCamera.orthographic = true;
                // 1 unit = 32 画素 × s。 画面の高さ (画素) の半分 ÷ (32 × s)
                screenCamera.orthographicSize = Screen.height / (2f * Ppu * s);
                screenCamera.rect = new Rect(0, 0, 1, 1);
            }
            if (canvas != null)
            {
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.scaleFactor = PixelsPerGuiDot;
                }
            }
            if (fxMaterial != null) fxMaterial.SetVector("_RtSize", new Vector4(RtWidth, RtHeight, 0, 0));
            if (gridRoot != null)
            {
                gridRoot.anchorMin = gridRoot.anchorMax = new Vector2(0.5f, 0.5f);
                gridRoot.pivot = new Vector2(0.5f, 0.5f);
                gridRoot.anchoredPosition = Vector2.zero;
                gridRoot.sizeDelta = new Vector2(Game2DLayout.Width, Game2DLayout.Height);
            }
        }
    }
}

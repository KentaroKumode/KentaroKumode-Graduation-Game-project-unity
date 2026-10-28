using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 外枠の GUI を 960×540 の RT (透明な地) へ描く専用のカメラと、 その RT を液晶の上に重ねる板 (<c>Game2D/GuiPresent</c>) をつなぐ (2026-10-19)。
    /// GUI を一度 RT にするのは、 液晶へ落ちる影 (GUI の輪郭から作る) と、 上が明るく下が暗い色調を付けるため (map-prototype.html の「GUI の影・にじみ・ビネット」)。
    /// RT は GUI の 1 dot = 2 画素 (液晶の RT と同じ細かさ) なので、 画面へは整数倍で最近傍に引き伸ばされ、 ドット絵の密度は変わらない。
    /// </summary>
    public class Game2DGuiRt : MonoBehaviour
    {
        public Camera guiCamera;
        public RawImage present;
        public Material presentMaterial;
        public const int Width = 960, Height = 540;

        private RenderTexture _rt;
        private Material _mat;

        private void OnEnable()
        {
            if (_rt == null)
            {
                _rt = new RenderTexture(Width, Height, 16, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DGuiRt", useMipMap = false };
                _rt.Create();
            }
            if (guiCamera != null)
            {
                guiCamera.targetTexture = _rt;
                guiCamera.clearFlags = CameraClearFlags.SolidColor;
                guiCamera.backgroundColor = new Color(0, 0, 0, 0);
            }
            // RT は実行時に作るものなので、 アセットのマテリアルへは書かず (保存されてしまう)、 複製に入れる
            if (presentMaterial != null && present != null)
            {
                if (_mat == null) _mat = new Material(presentMaterial) { name = "Game2DGuiPresent (runtime)" };
                _mat.SetTexture("_GuiTex", _rt); _mat.SetVector("_RtSize", new Vector4(Width, Height, 0, 0));
                present.material = _mat;
            }
            if (present != null) present.texture = _rt;
        }

        private void OnDisable()
        {
            if (guiCamera != null && guiCamera.targetTexture == _rt) guiCamera.targetTexture = null;
        }

        private void OnDestroy() { if (_rt != null) { _rt.Release(); Destroy(_rt); } if (_mat != null) Destroy(_mat); }
    }
}

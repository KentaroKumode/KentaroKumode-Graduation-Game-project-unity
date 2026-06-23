using UnityEngine;

namespace UI.Lcd
{
    /// <summary>
    /// <see cref="LcdScreen"/> の RT に対し「副カメラ」を後付けで取り付けるための薄いアダプタ。
    /// 同 GO の Camera を取り、 LcdScreen.Texture を targetTexture にバインドし、 アスペクトを LcdProfile に追従させる。
    ///
    /// 用途: タイトル(正射影) と ゲームプレイ(透視) のように、 同じ LCD 面に2系統のカメラを切り替えで描画したい場合、
    /// メイン側は LcdScreen.contentCamera、 副側はこのコンポーネントで配線する。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class LcdAuxCamera : MonoBehaviour
    {
        [Tooltip("RT をホストする LcdScreen。 未指定時はシーンから1個探す。")]
        public LcdScreen lcdScreen;

        private Camera _cam;
        private RenderTexture _bound;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (lcdScreen == null) lcdScreen = FindObjectOfType<LcdScreen>();
        }

        private void OnEnable() => TryBind();
        private void LateUpdate()
        {
            // LcdScreen.OnEnable が後から走るシーン構成にも追従するため、 毎フレーム差分があれば再配線する(安価)。
            if (lcdScreen != null && lcdScreen.Texture != null && _bound != lcdScreen.Texture)
                TryBind();
        }

        private void TryBind()
        {
            if (_cam == null || lcdScreen == null || lcdScreen.Texture == null) return;
            _cam.targetTexture = lcdScreen.Texture;
            if (lcdScreen.profile != null) _cam.aspect = lcdScreen.profile.Aspect;
            _bound = lcdScreen.Texture;
        }
    }
}

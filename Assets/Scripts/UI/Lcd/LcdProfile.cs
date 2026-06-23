using UnityEngine;

namespace UI.Lcd
{
    /// <summary>
    /// 「液晶（LCD）」規格を共有するためのプロファイル。 ネイティブ解像度・フィルタ等を1か所で定義し、
    /// タイトル/ゲーム本編/メニュー等、 液晶へ描く全画面で使い回す（RenderTexture パイプラインの基準）。
    ///
    /// 作り方: Project で右クリック → Create → Lcd → Profile。 各 <see cref="LcdScreen"/> に割り当てる。
    /// </summary>
    [CreateAssetMenu(menuName = "Lcd/Profile", fileName = "LcdProfile")]
    public class LcdProfile : ScriptableObject
    {
        [Header("ネイティブ解像度（液晶の物理ドット数）")]
        [Min(16)] public int nativeWidth = 320;
        [Min(16)] public int nativeHeight = 240;

        [Header("質感")]
        [Tooltip("ピクセルをくっきり出すなら Point。 にじませるなら Bilinear。")]
        public FilterMode filterMode = FilterMode.Point;
        [Tooltip("深度バッファのビット数（2D=16/24, 3Dコンテンツも描くなら24）")]
        public int depthBits = 24;
        [Tooltip("ミップマップ（ピクセルアート液晶では基本オフ）")]
        public bool useMipMap = false;
        public RenderTextureFormat format = RenderTextureFormat.Default;

        public float Aspect => nativeHeight > 0 ? (float)nativeWidth / nativeHeight : 1f;

        /// <summary>このプロファイルから RenderTexture を生成（呼び出し側が Release 管理）。</summary>
        public RenderTexture CreateRenderTexture()
        {
            int w = Mathf.Max(16, nativeWidth);
            int h = Mathf.Max(16, nativeHeight);
            var rt = new RenderTexture(w, h, Mathf.Max(0, depthBits), format)
            {
                name = $"LCD_{name}_{w}x{h}",
                filterMode = filterMode,
                useMipMap = useMipMap,
                autoGenerateMips = useMipMap,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1, // 液晶は等倍ドット想定＝MSAA不要
            };
            rt.Create();
            return rt;
        }
    }
}

using UnityEngine;

namespace UI.Lcd
{
    /// <summary>
    /// 液晶（LCD）パイプラインの中核。 「コンテンツ用カメラ → 専用 RenderTexture → ワールドに置いた画面サーフェス」を
    /// 自動で配線・管理する。 ゲーム世界の中の“携帯機の画面”として、 タイトル/本編/メニューを同じ仕組みで描くための再利用部品。
    ///
    /// 構成:
    ///   ・<see cref="contentCamera"/> … 液晶に映す中身（タイトルのスプライト群／本編など）を描く。 通常は正射影ピクセルアート。
    ///     このカメラの targetTexture に RT を割り当てる（＝画面には直接出ず、 液晶面にだけ映る）。
    ///   ・<see cref="surface"/> … ワールドに置いた画面メッシュ（Quad 等の Renderer）。 RT をそのテクスチャに流す。
    ///   ・別の透視カメラが「携帯機＋部屋＋液晶面」を映す（パララックス揺らしはそちらに付ける）。
    ///
    /// 使い方:
    ///   1) LcdProfile を作り（解像度=液晶ドット数）、 ここに割り当て。
    ///   2) contentCamera にコンテンツ描画カメラ、 surface に液晶面の Renderer を割り当て。
    ///   3) 実行時に RT が生成され、 カメラ出力が液晶面へ映る。
    ///
    /// 注意: 液晶面の見た目スケールは Transform（メッシュの大きさ）で決める。 コンテンツ側は localScale 倍率で状態表現しない（PPU=32 規約）。
    /// </summary>
    [DisallowMultipleComponent]
    public class LcdScreen : MonoBehaviour
    {
        [Header("規格")]
        public LcdProfile profile;

        [Header("配線")]
        [Tooltip("液晶に映す中身を描くカメラ（targetTexture に RT が入る）")]
        public Camera contentCamera;
        [Tooltip("ワールド上の画面メッシュ（Quad 等）。 RT をこの Renderer に流す。")]
        public Renderer surface;
        [Tooltip("サーフェス側マテリアルのテクスチャプロパティ名（Unlit/Texture・Sprites/Default は _MainTex）")]
        public string surfaceTextureProperty = "_MainTex";

        [Header("オプション")]
        [Tooltip("contentCamera の縦視野(orthographicSize) を液晶アスペクトに合わせて自動調整しない場合は false")]
        public bool driveContentCameraAspect = true;

        public RenderTexture Texture { get; private set; }

        private MaterialPropertyBlock _mpb;
        private int _texPropId;

        private void OnEnable()  => Build();
        private void OnDisable() => Teardown();

        /// <summary>RT を生成し、 コンテンツカメラ出力と液晶サーフェスへ配線する。 再設定時にも呼べる。</summary>
        public void Build()
        {
            if (profile == null)
            {
                Debug.LogWarning("[LcdScreen] LcdProfile 未設定。 配線をスキップ。");
                return;
            }
            Teardown();

            Texture = profile.CreateRenderTexture();
            _texPropId = Shader.PropertyToID(surfaceTextureProperty);

            if (contentCamera != null)
            {
                contentCamera.targetTexture = Texture;
                // targetTexture を持つカメラのアスペクトは RT 解像度から自動決定される（横は縦×アスペクト）。
                if (driveContentCameraAspect)
                    contentCamera.aspect = profile.Aspect;
            }

            ApplyToSurface();
        }

        private void ApplyToSurface()
        {
            if (surface == null || Texture == null) return;
            // マテリアルを実体化せずに済むよう MaterialPropertyBlock でテクスチャ差し込み。
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            surface.GetPropertyBlock(_mpb);
            _mpb.SetTexture(_texPropId, Texture);
            surface.SetPropertyBlock(_mpb);
        }

        private void Teardown()
        {
            if (contentCamera != null && contentCamera.targetTexture == Texture)
                contentCamera.targetTexture = null;

            if (surface != null && _mpb != null)
            {
                surface.GetPropertyBlock(_mpb);
                _mpb.SetTexture(_texPropId, Texture2D.blackTexture);
                surface.SetPropertyBlock(_mpb);
            }

            if (Texture != null)
            {
                Texture.Release();
                if (Application.isPlaying) Destroy(Texture); else DestroyImmediate(Texture);
                Texture = null;
            }
        }
    }
}

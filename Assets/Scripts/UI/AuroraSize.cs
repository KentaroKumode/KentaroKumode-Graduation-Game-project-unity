using UnityEngine;

namespace UI
{
    /// <summary>
    /// Aurora 用 Quad の横/縦幅をインスペクタから直接調整するための薄いコンポーネント。
    /// localScale を field に反映するだけ。 Edit モードでも即時反映される。
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class AuroraSize : MonoBehaviour
    {
        [Tooltip("横幅(world)。 LCD コンテンツ視野幅(≈28)以上にすると端の継ぎ目が見えなくなる。")]
        public float width = 28f;
        [Tooltip("縦幅(world)。 帯の縦の太さ。 上端固定で下に伸びる。")]
        public float height = 5f;
        [Tooltip("帯の上端の世界 Y 座標。 LCD ortho 上端=8.69 想定。")]
        public float topY = 8.69f;

        [Header("ピクセルパーフェクト")]
        [Tooltip("LCD コンテンツ解像度（ドット単位）。 背景=PPU32 と揃える。")]
        public int pixelsPerUnit = 32;
        [Tooltip("OFF にすると格子量子化を切る（滑らかな描画）。")]
        public bool pixelPerfect = true;

        private MaterialPropertyBlock _mpb;
        private Renderer _rend;
        private static readonly int IdGridX = Shader.PropertyToID("_PixelGridX");
        private static readonly int IdGridY = Shader.PropertyToID("_PixelGridY");

        private void OnEnable() { Apply(); }
        private void OnValidate() { Apply(); }

        private void Apply()
        {
            int ppu = Mathf.Max(1, pixelsPerUnit);
            float unit = 1f / ppu;

            // サイズと位置をドット格子へスナップ
            float w = SnapUp(Mathf.Max(0.001f, width), unit);
            float h = SnapUp(Mathf.Max(0.001f, height), unit);
            float tY = SnapDown(topY, unit); // 上端は格子の下側に寄せて誤差を内側に閉じる

            var s = transform.localScale;
            s.x = w; s.y = h; s.z = 1f;
            if (transform.localScale != s) transform.localScale = s;

            var p = transform.localPosition;
            float newY = tY - h * 0.5f;
            float snappedY = Mathf.Round(newY * ppu) / ppu;
            if (!Mathf.Approximately(p.y, snappedY))
            {
                p.y = snappedY;
                transform.localPosition = p;
            }
            // X もスナップ
            float snappedX = Mathf.Round(p.x * ppu) / ppu;
            if (!Mathf.Approximately(p.x, snappedX))
            {
                p.x = snappedX;
                transform.localPosition = p;
            }

            // マテリアル側にドット格子（quad全体を何ドットで覆うか）を渡す
            if (_rend == null) _rend = GetComponent<Renderer>();
            if (_rend != null)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                _rend.GetPropertyBlock(_mpb);
                if (pixelPerfect)
                {
                    _mpb.SetFloat(IdGridX, w * ppu);
                    _mpb.SetFloat(IdGridY, h * ppu);
                }
                else
                {
                    _mpb.SetFloat(IdGridX, 0f);
                    _mpb.SetFloat(IdGridY, 0f);
                }
                _rend.SetPropertyBlock(_mpb);
            }
        }

        // 切り上げ（指定単位の倍数に丸める）
        private static float SnapUp(float v, float unit) => Mathf.Ceil(v / unit) * unit;
        private static float SnapDown(float v, float unit) => Mathf.Floor(v / unit) * unit;
    }
}

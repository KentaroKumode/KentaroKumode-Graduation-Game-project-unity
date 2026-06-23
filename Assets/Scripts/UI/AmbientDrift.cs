using UnityEngine;

namespace UI
{
    /// <summary>
    /// マウスに依存しない「アンビエントな漂い」。 1枚絵の背景にゆっくりした位置オフセットを与えて
    /// “生きてる”感を出す（貼り付けただけの静止画チープさの緩和）。
    ///
    /// 設計:
    ///   ・localScale は使わず **localPosition のオフセットのみ**（PPU=32 ピクセル要素のスケール禁止に準拠）。
    ///   ・縦は端が露出しやすいので既定 0。 横は左右クロップの余白内で揺らす想定。
    ///   ・2つの周期で X/Y を独立にサイン駆動（無理なら片軸のみ）。
    ///
    /// 使い方: 背景スプライトに付け、 amplitude をクロップ余白未満に設定（例 X=0.8, Y=0）。
    /// </summary>
    [DisallowMultipleComponent]
    public class AmbientDrift : MonoBehaviour
    {
        [Tooltip("最大オフセット(world)。 横はクロップ余白未満に。 縦は端が出るので基本0。")]
        public Vector2 amplitude = new Vector2(0.8f, 0f);
        [Tooltip("X/Y それぞれの周期（秒）。 互いに素に近い値だと往復が目立たない。")]
        public Vector2 period = new Vector2(23f, 17f);
        [Tooltip("ポーズ中(timeScale=0)でも動かす")]
        public bool useUnscaledTime = true;

        private Vector3 _base;
        private bool _captured;

        private void OnEnable()
        {
            if (!_captured) { _base = transform.localPosition; _captured = true; }
        }

        private void OnDisable()
        {
            if (_captured) transform.localPosition = _base;
        }

        private void Update()
        {
            float t = useUnscaledTime ? Time.unscaledTime : Time.time;
            const float TAU = 6.28318530718f;
            float x = Mathf.Sin(t / Mathf.Max(0.01f, period.x) * TAU) * amplitude.x;
            float y = Mathf.Sin(t / Mathf.Max(0.01f, period.y) * TAU) * amplitude.y;
            transform.localPosition = _base + new Vector3(x, y, 0f);
        }
    }
}

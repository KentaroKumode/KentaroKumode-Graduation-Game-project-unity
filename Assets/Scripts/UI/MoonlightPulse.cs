using UnityEngine;

namespace UI
{
    /// <summary>
    /// 月光の脈動（背景 SpriteRenderer のコントラストをゆっくり脈動）。 静かな夜の呼吸感。
    /// 純粋な明度倍ではなく、 (rgb - 0.5) * contrast + 0.5 を適用＝暗部はより暗く、 明部はより明るく動く。
    ///
    /// 前提: 背景のマテリアルが <c>Sprites/Contrast</c> シェーダ（_Contrast プロパティ）を使っていること。
    ///       未対応マテリアルなら何もしない。
    ///
    /// 規約: localScale 不使用。 MaterialPropertyBlock で _Contrast を制御するため Material の実体化なし。
    /// </summary>
    [DisallowMultipleComponent]
    public class MoonlightPulse : MonoBehaviour
    {
        [Tooltip("脈動させる SpriteRenderer。 未指定なら同GO/子から自動取得。")]
        public SpriteRenderer target;

        [Header("脈動（コントラスト 1=変化なし）")]
        [Range(0.5f, 1f)] public float minContrast = 0.95f;
        [Range(1f, 2f)]   public float maxContrast = 1.20f;
        [Tooltip("脈動の周期(秒)。 5〜12 がそれっぽい。")]
        public float periodSeconds = 8f;
        public bool useUnscaledTime = true;

        private MaterialPropertyBlock _mpb;
        private int _contrastId;

        private void OnEnable()
        {
            if (target == null) target = GetComponentInChildren<SpriteRenderer>();
            _mpb = new MaterialPropertyBlock();
            _contrastId = Shader.PropertyToID("_Contrast");
        }

        private void OnDisable()
        {
            // コントラストを 1.0（変化なし）に戻す
            if (target == null) return;
            target.GetPropertyBlock(_mpb);
            _mpb.SetFloat(_contrastId, 1f);
            target.SetPropertyBlock(_mpb);
        }

        private void Update()
        {
            if (target == null) return;
            float t = useUnscaledTime ? Time.unscaledTime : Time.time;
            float omega = (periodSeconds > 0.001f) ? (2f * Mathf.PI / periodSeconds) : 0f;
            float k = 0.5f + 0.5f * Mathf.Sin(t * omega);
            float c = Mathf.Lerp(minContrast, maxContrast, k);

            target.GetPropertyBlock(_mpb);
            _mpb.SetFloat(_contrastId, c);
            target.SetPropertyBlock(_mpb);
        }
    }
}

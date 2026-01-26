using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// フィルター時の強調エフェクト
    /// </summary>
    public class FilterHighlightEffect : MonoBehaviour
    {
        [Header("エフェクト設定")]
        [SerializeField] private GameObject highlightEffectPrefab;
        [SerializeField] private Color highlightColor = new Color(1f, 1f, 0.5f, 0.5f);
        [SerializeField] private float pulseSpeed = 2f;
        
        private GameObject currentEffect;
        private bool isHighlighted = false;
        
        /// <summary>
        /// 強調エフェクトを適用
        /// </summary>
        public void ApplyHighlight(Transform target)
        {
            if (isHighlighted) return;
            
            if (highlightEffectPrefab != null)
            {
                currentEffect = Instantiate(highlightEffectPrefab, target);
                currentEffect.transform.localPosition = Vector3.zero;
            }
            
            isHighlighted = true;
        }
        
        /// <summary>
        /// 強調エフェクトを解除
        /// </summary>
        public void RemoveHighlight()
        {
            if (!isHighlighted) return;
            
            if (currentEffect != null)
            {
                Destroy(currentEffect);
                currentEffect = null;
            }
            
            isHighlighted = false;
        }
        
        void Update()
        {
            if (isHighlighted && currentEffect != null)
            {
                // パルスアニメーション
                float pulse = Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f;
                // TODO: エフェクトの明るさを調整
            }
        }
        
        /// <summary>
        /// メモリリーク防止のクリーンアップ
        /// </summary>
        void OnDestroy()
        {
            RemoveHighlight();
        }
    }
}

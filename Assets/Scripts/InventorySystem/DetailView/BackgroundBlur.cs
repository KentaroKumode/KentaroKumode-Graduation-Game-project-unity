using UnityEngine;
using UnityEngine.UI;

namespace InventorySystem
{
    /// <summary>
    /// 背景ぼかし効果
    /// </summary>
    public class BackgroundBlur : MonoBehaviour
    {
        [Header("UI要素")]
        [SerializeField] private Image blurOverlay;
        
        [Header("設定")]
        [SerializeField] private Color blurColor = new Color(0, 0, 0, 0.5f);
        [SerializeField] private float fadeDuration = 0.2f;
        
        private bool isBlurred = false;
        private float currentAlpha = 0f;
        
        void Start()
        {
            if (blurOverlay != null)
            {
                blurOverlay.gameObject.SetActive(false);
            }
        }
        
        /// <summary>
        /// ぼかしを有効化
        /// </summary>
        public void EnableBlur()
        {
            if (blurOverlay != null)
            {
                blurOverlay.gameObject.SetActive(true);
                isBlurred = true;
            }
        }
        
        /// <summary>
        /// ぼかしを無効化
        /// </summary>
        public void DisableBlur()
        {
            isBlurred = false;
            
            if (blurOverlay != null)
            {
                blurOverlay.gameObject.SetActive(false);
            }
        }
        
        void Update()
        {
            if (blurOverlay == null) return;

            // フェード処理
            float targetAlpha = isBlurred ? blurColor.a : 0f;
            currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.deltaTime / fadeDuration);

            Color color = blurColor;
            color.a = currentAlpha;
            blurOverlay.color = color;
        }
        
        /// <summary>
        /// メモリリーク防止のクリーンアップ
        /// </summary>
        void OnDestroy()
        {
            DisableBlur();
        }
    }
}

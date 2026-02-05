using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテムプレビュー用の背景プレーン
    /// </summary>
    public class BackGroundPlane : MonoBehaviour
    {
        [Header("背景設定")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        [SerializeField] private Vector3 defaultScale = new Vector3(2f, 2f, 1f);
        
        [Header("アニメーション設定")]
        [SerializeField] private bool enableFadeAnimation = true;
        [SerializeField] private float fadeInDuration = 0.3f;
        [SerializeField] private float fadeOutDuration = 0.2f;
        
        private void Awake()
        {
            InitializeBackgroundPlane();
        }
        
        /// <summary>
        /// 背景プレーンを初期化
        /// </summary>
        private void InitializeBackgroundPlane()
        {
            // SpriteRendererが未設定の場合、自動で取得または作成
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
                
                if (spriteRenderer == null)
                {
                    spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
                }
            }
            
            // デフォルト設定を適用
            SetupDefaultSprite();
            SetBackgroundColor(backgroundColor);
            transform.localScale = defaultScale;
            
            // 初期状態は透明
            if (enableFadeAnimation)
            {
                SetAlpha(0f);
            }
        }
        
        /// <summary>
        /// デフォルトスプライトを設定（白い四角形）
        /// </summary>
        private void SetupDefaultSprite()
        {
            if (spriteRenderer.sprite == null)
            {
                // 白い四角形のテクスチャを作成
                Texture2D texture = new Texture2D(1, 1);
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                
                // スプライトを作成
                Sprite defaultSprite = Sprite.Create(
                    texture, 
                    new Rect(0, 0, 1, 1), 
                    new Vector2(0.5f, 0.5f)
                );
                
                spriteRenderer.sprite = defaultSprite;
            }
        }
        
        /// <summary>
        /// 背景色を設定
        /// </summary>
        public void SetBackgroundColor(Color color)
        {
            backgroundColor = color;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = color;
            }
        }
        
        /// <summary>
        /// アルファ値を設定
        /// </summary>
        public void SetAlpha(float alpha)
        {
            if (spriteRenderer != null)
            {
                Color color = spriteRenderer.color;
                color.a = alpha;
                spriteRenderer.color = color;
            }
        }
        
        /// <summary>
        /// フェードイン表示
        /// </summary>
        public void FadeIn()
        {
            if (enableFadeAnimation)
            {
                StartCoroutine(FadeCoroutine(0f, backgroundColor.a, fadeInDuration));
            }
            else
            {
                SetAlpha(backgroundColor.a);
            }
        }
        
        /// <summary>
        /// フェードアウト
        /// </summary>
        public void FadeOut()
        {
            if (enableFadeAnimation)
            {
                StartCoroutine(FadeCoroutine(spriteRenderer.color.a, 0f, fadeOutDuration));
            }
            else
            {
                SetAlpha(0f);
            }
        }
        
        /// <summary>
        /// フェードアニメーションのコルーチン
        /// </summary>
        private System.Collections.IEnumerator FadeCoroutine(float fromAlpha, float toAlpha, float duration)
        {
            float elapsed = 0f;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = elapsed / duration;
                float currentAlpha = Mathf.Lerp(fromAlpha, toAlpha, normalizedTime);
                SetAlpha(currentAlpha);
                yield return null;
            }
            
            SetAlpha(toAlpha);
        }
        
        /// <summary>
        /// 背景プレーンを表示
        /// </summary>
        public void Show()
        {
            gameObject.SetActive(true);
            FadeIn();
        }
        
        /// <summary>
        /// 背景プレーンを非表示
        /// </summary>
        public void Hide()
        {
            if (enableFadeAnimation)
            {
                StartCoroutine(HideAfterFade());
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
        
        /// <summary>
        /// フェードアウト後に非表示にする
        /// </summary>
        private System.Collections.IEnumerator HideAfterFade()
        {
            FadeOut();
            yield return new WaitForSeconds(fadeOutDuration);
            gameObject.SetActive(false);
        }
        
        /// <summary>
        /// 背景プレーンのサイズを設定
        /// </summary>
        public void SetSize(Vector3 size)
        {
            transform.localScale = size;
        }
        
        /// <summary>
        /// 背景プレーンの位置を設定
        /// </summary>
        public void SetPosition(Vector3 position)
        {
            transform.position = position;
        }
        
        /// <summary>
        /// 背景プレーンの回転を設定
        /// </summary>
        public void SetRotation(Quaternion rotation)
        {
            transform.rotation = rotation;
        }
    }
}
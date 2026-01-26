using UnityEngine;

namespace CoinSystem
{
    /// <summary>
    /// アニメーション用のユーティリティ関数を提供するクラス
    /// </summary>
    public static class AnimationUtility
    {
        /// <summary>
        /// バウンスイージング関数（Ease Out Bounce）
        /// </summary>
        /// <param name="t">0.0～1.0の進行度</param>
        /// <returns>イージングされた値</returns>
        public static float EaseOutBounce(float t)
        {
            if (t < (1f / 2.75f))
            {
                return 7.5625f * t * t;
            }
            else if (t < (2f / 2.75f))
            {
                return 7.5625f * (t -= (1.5f / 2.75f)) * t + 0.75f;
            }
            else if (t < (2.5f / 2.75f))
            {
                return 7.5625f * (t -= (2.25f / 2.75f)) * t + 0.9375f;
            }
            else
            {
                return 7.5625f * (t -= (2.625f / 2.75f)) * t + 0.984375f;
            }
        }
        
        /// <summary>
        /// Linear イージング関数
        /// </summary>
        /// <param name="t">0.0～1.0の進行度</param>
        /// <returns>そのままの値</returns>
        public static float Linear(float t)
        {
            return t;
        }
        
        /// <summary>
        /// Ease In Quad イージング関数
        /// </summary>
        /// <param name="t">0.0～1.0の進行度</param>
        /// <returns>イージングされた値</returns>
        public static float EaseInQuad(float t)
        {
            return t * t;
        }
        
        /// <summary>
        /// Ease Out Quad イージング関数
        /// </summary>
        /// <param name="t">0.0～1.0の進行度</param>
        /// <returns>イージングされた値</returns>
        public static float EaseOutQuad(float t)
        {
            return t * (2f - t);
        }
        
        /// <summary>
        /// Ease In Out Quad イージング関数
        /// </summary>
        /// <param name="t">0.0～1.0の進行度</param>
        /// <returns>イージングされた値</returns>
        public static float EaseInOutQuad(float t)
        {
            return t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;
        }
    }
}
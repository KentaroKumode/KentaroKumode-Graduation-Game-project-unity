using UnityEngine;
using System.Collections;

namespace InventorySystem
{
    /// <summary>
    /// アンロック演出のエフェクト制御
    /// </summary>
    public class UnlockEffectController : MonoBehaviour
    {
        [Header("パーティクル")]
        [SerializeField] private ParticleSystem chainBreakParticle;
        [SerializeField] private ParticleSystem flashParticle;
        [SerializeField] private ParticleSystem glowParticle;
        
        /// <summary>
        /// 鎖破壊エフェクト再生
        /// </summary>
        public void PlayChainBreakEffect(Vector3 position)
        {
            if (chainBreakParticle != null)
            {
                ParticleSystem ps = Instantiate(chainBreakParticle, position, Quaternion.identity);
                Destroy(ps.gameObject, 3f);
            }
        }
        
        /// <summary>
        /// フラッシュエフェクト再生
        /// </summary>
        public void PlayFlashEffect(Vector3 position)
        {
            if (flashParticle != null)
            {
                ParticleSystem ps = Instantiate(flashParticle, position, Quaternion.identity);
                Destroy(ps.gameObject, 2f);
            }
        }
        
        /// <summary>
        /// 輝きエフェクト再生
        /// </summary>
        public void PlayGlowEffect(Transform target)
        {
            if (glowParticle != null)
            {
                ParticleSystem ps = Instantiate(glowParticle, target);
                ps.transform.localPosition = Vector3.zero;
                Destroy(ps.gameObject, 5f);
            }
        }
    }
}

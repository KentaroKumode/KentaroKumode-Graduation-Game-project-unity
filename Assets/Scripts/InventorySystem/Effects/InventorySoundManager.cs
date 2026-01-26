using UnityEngine;
using System.Collections.Generic;

namespace InventorySystem
{
    /// <summary>
    /// インベントリの効果音管理
    /// </summary>
    public class InventorySoundManager : MonoBehaviour
    {
        [Header("効果音")]
        [SerializeField] private AudioClip itemPickupSound;
        [SerializeField] private AudioClip itemPlaceSound;
        [SerializeField] private AudioClip itemEquipSound;
        [SerializeField] private AudioClip itemUseSound;
        [SerializeField] private AudioClip itemDiscardSound;
        [SerializeField] private AudioClip unlockSound;
        [SerializeField] private AudioClip invalidActionSound;
        [SerializeField] private AudioClip uiClickSound;
        
        private AudioSource audioSource;
        private static InventorySoundManager instance;
        
        public static InventorySoundManager Instance => instance;
        
        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        
        /// <summary>
        /// アイテム取得音
        /// </summary>
        public void PlayItemPickup()
        {
            PlaySound(itemPickupSound);
        }
        
        /// <summary>
        /// アイテム配置音
        /// </summary>
        public void PlayItemPlace()
        {
            PlaySound(itemPlaceSound);
        }
        
        /// <summary>
        /// アイテム装備音
        /// </summary>
        public void PlayItemEquip()
        {
            PlaySound(itemEquipSound);
        }
        
        /// <summary>
        /// アイテム使用音
        /// </summary>
        public void PlayItemUse()
        {
            PlaySound(itemUseSound);
        }
        
        /// <summary>
        /// アイテム破棄音
        /// </summary>
        public void PlayItemDiscard()
        {
            PlaySound(itemDiscardSound);
        }
        
        /// <summary>
        /// アンロック音
        /// </summary>
        public void PlayUnlock()
        {
            PlaySound(unlockSound);
        }
        
        /// <summary>
        /// 無効な操作音
        /// </summary>
        public void PlayInvalidAction()
        {
            PlaySound(invalidActionSound);
        }
        
        /// <summary>
        /// UIクリック音
        /// </summary>
        public void PlayUIClick()
        {
            PlaySound(uiClickSound);
        }
        
        /// <summary>
        /// サウンド再生
        /// </summary>
        private void PlaySound(AudioClip clip)
        {
            if (clip != null && audioSource != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }
    }
}

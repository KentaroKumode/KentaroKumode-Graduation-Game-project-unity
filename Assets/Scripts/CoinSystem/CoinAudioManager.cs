using UnityEngine;

namespace CoinSystem
{
    /// <summary>
    /// コインシステムの音声管理
    /// </summary>
    public class CoinAudioManager : MonoBehaviour
    {
        [Header("コイン音設定")]
        [SerializeField] private AudioClip[] coinSounds;
        [SerializeField] private bool enableCoinSounds = true;
        [SerializeField] private float coinSoundVolume = 0.7f;
        [SerializeField] [Range(0.5f, 2.0f)] private float coinSoundPitchMin = 0.8f;
        [SerializeField] [Range(0.5f, 2.0f)] private float coinSoundPitchMax = 1.2f;
        [SerializeField] private int maxCoinAudioSources = 4;
        
        [Header("払い出し音設定")]
        [SerializeField] private AudioClip dispensingSound;
        [SerializeField] private bool enableDispensingSound = true;
        [SerializeField] private float dispensingSoundVolume = 0.8f;
        [SerializeField] [Range(0.5f, 2.0f)] private float dispensingSoundPitch = 1.0f;
        
        [Header("積み上げ音設定")]
        [SerializeField] private AudioClip stackSound;
        [SerializeField] private bool enableStackSound = true;
        [SerializeField] private float stackSoundVolume = 0.6f;
        [SerializeField] private float stackPitchMin = 0.8f;
        [SerializeField] private float stackPitchMax = 1.4f;
        
        [Header("チケット音設定")]
        [SerializeField] private AudioClip ticketDispenseSound;
        [SerializeField] private bool enableTicketSound = true;
        [SerializeField] private float ticketSoundVolume = 0.7f;
        [SerializeField] [Range(0.5f, 2.0f)] private float ticketSoundPitch = 1.0f;
        
        private AudioSource mainAudioSource;
        private AudioSource ticketAudioSource;  // チケット専用AudioSource
        private AudioSource[] coinAudioSources;
        private int currentCoinAudioIndex = 0;
        private bool isInitialized = false;
        private int emergencyFixCount = 0;
        
        private void Start()
        {
            InitializeAudio();
        }
        
        private void InitializeAudio()
        {
            if (isInitialized) return;
            
            // メインAudioSource
            mainAudioSource = gameObject.AddComponent<AudioSource>();
            mainAudioSource.playOnAwake = false;
            
            // チケット専用AudioSource（優先度高）
            ticketAudioSource = gameObject.AddComponent<AudioSource>();
            ticketAudioSource.playOnAwake = false;
            ticketAudioSource.priority = 0; // 最高優先度
            
            // コイン音用の複数AudioSource
            coinAudioSources = new AudioSource[maxCoinAudioSources];
            for (int i = 0; i < maxCoinAudioSources; i++)
            {
                coinAudioSources[i] = gameObject.AddComponent<AudioSource>();
                coinAudioSources[i].playOnAwake = false;
                coinAudioSources[i].priority = 128; // 低優先度（チケットより低い）
            }
            
            isInitialized = true;
            
            // AudioListener重複問題を解決
            EmergencyAudioListenerFix();
            
            Debug.Log("CoinAudioManager initialized");
        }
        
        public void PlayRandomCoinSound(float overridePitch = -1f)
        {
            if (!enableCoinSounds || coinSounds == null || coinSounds.Length == 0)
                return;
            
            AudioSource source = coinAudioSources[currentCoinAudioIndex];
            currentCoinAudioIndex = (currentCoinAudioIndex + 1) % maxCoinAudioSources;
            
            AudioClip clip = coinSounds[Random.Range(0, coinSounds.Length)];
            source.clip = clip;
            source.volume = coinSoundVolume;
            
            if (overridePitch > 0f)
            {
                source.pitch = overridePitch;
            }
            else
            {
                source.pitch = Random.Range(coinSoundPitchMin, coinSoundPitchMax);
            }
            
            source.Play();
        }
        
        public void PlayDispensingSound()
        {
            if (!enableDispensingSound || dispensingSound == null)
                return;
            
            mainAudioSource.clip = dispensingSound;
            mainAudioSource.volume = dispensingSoundVolume;
            mainAudioSource.pitch = dispensingSoundPitch;
            mainAudioSource.Play();
        }
        
        public void PlayStackSound(int coinIndex, int maxCoins)
        {
            if (!enableStackSound || stackSound == null)
                return;
            
            float t = (float)coinIndex / maxCoins;
            float pitch = Mathf.Lerp(stackPitchMin, stackPitchMax, t);
            
            AudioSource source = coinAudioSources[currentCoinAudioIndex];
            currentCoinAudioIndex = (currentCoinAudioIndex + 1) % maxCoinAudioSources;
            
            source.clip = stackSound;
            source.volume = stackSoundVolume;
            source.pitch = pitch;
            source.Play();
        }
        
        public void PlayTicketSound()
        {
            Debug.Log($"PlayTicketSound called - enableTicketSound: {enableTicketSound}, ticketDispenseSound: {(ticketDispenseSound != null ? "assigned" : "null")}");
            
            if (!enableTicketSound)
            {
                Debug.LogWarning("Ticket sound is disabled!");
                return;
            }
            
            if (ticketDispenseSound == null)
            {
                Debug.LogError("ticketDispenseSound is not assigned!");
                return;
            }
            
            if (ticketAudioSource == null)
            {
                Debug.LogError("ticketAudioSource is null!");
                return;
            }
            
            // チケット専用AudioSourceで再生（優先度高）
            ticketAudioSource.clip = ticketDispenseSound;
            ticketAudioSource.volume = ticketSoundVolume;
            ticketAudioSource.pitch = ticketSoundPitch;
            ticketAudioSource.Play();
            
            Debug.Log($"Ticket sound played on dedicated AudioSource - Volume: {ticketSoundVolume}, Pitch: {ticketSoundPitch}");
        }
        
        public void SetCoinSoundPitch(float min, float max)
        {
            coinSoundPitchMin = min;
            coinSoundPitchMax = max;
        }
        
        public void SetTicketSoundVolume(float volume)
        {
            ticketSoundVolume = volume;
        }
        
        public void SetTicketSoundPitch(float pitch)
        {
            ticketSoundPitch = pitch;
        }
        
        /// <summary>
        /// AudioListener重複の安全な自己修復。
        /// 優先順位:
        /// 1) Main Camera の AudioListener を保持（なければ付与）
        /// 2) それ以外の重複を削除
        /// 3) どのカメラにも無い場合は最初のリスナーを保持
        /// </summary>
        private void EmergencyAudioListenerFix()
        {
            var listeners = FindObjectsOfType<AudioListener>();

            // 0個の場合: Main Camera があれば追加
            if (listeners.Length == 0)
            {
                var mainCam = Camera.main;
                if (mainCam != null)
                {
                    mainCam.gameObject.AddComponent<AudioListener>();
                    Debug.Log("[AudioListener Fix] Added AudioListener to Main Camera (none found in scene)");
                }
                else
                {
                    Debug.LogWarning("[AudioListener Fix] No AudioListener and no Main Camera found");
                }
                return;
            }

            // 1個のみ: 何もしない
            if (listeners.Length == 1)
            {
                return;
            }

            emergencyFixCount++;

            // Keeper を決定
            var mainCamera = Camera.main;
            AudioListener keeper = null;
            if (mainCamera != null)
            {
                keeper = mainCamera.GetComponent<AudioListener>();
                if (keeper == null)
                {
                    keeper = mainCamera.gameObject.AddComponent<AudioListener>();
                    Debug.Log("[AudioListener Fix] Added missing AudioListener to Main Camera");
                }
            }

            if (keeper == null)
            {
                // フォールバック: 最初のリスナーを保持
                keeper = listeners[0];
            }

            int removedCount = 0;
            foreach (var listener in listeners)
            {
                if (listener == null || listener == keeper) continue;
                var parent = listener.transform.parent?.gameObject;
                var parentName = parent != null ? parent.name : "None";
                Debug.LogWarning($"[AudioListener Fix] Removing duplicate from: {listener.gameObject.name} (Parent: {parentName})");
                Destroy(listener);
                removedCount++;
            }

            if (removedCount > 0)
            {
                Debug.Log($"[AudioListener Fix #{emergencyFixCount}] Removed {removedCount} duplicate listener(s)");
            }
        }
    }
}

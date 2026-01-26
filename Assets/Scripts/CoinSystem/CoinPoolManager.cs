using System.Collections.Generic;
using UnityEngine;

namespace CoinSystem
{
    /// <summary>
    /// コインのオブジェクトプール管理
    /// </summary>
    public class CoinPoolManager : MonoBehaviour
    {
        [Header("プール設定")]
        [SerializeField] private GameObject coinPrefab;
        [SerializeField] private int maxConcurrentCoins = 300;
        
        [Header("物理設定")]
        [SerializeField] private float coinMass = 0.1f;
        [SerializeField] private float coinDrag = 0.5f;
        [SerializeField] private float coinAngularDrag = 0.8f;
        [SerializeField] private PhysicMaterial coinPhysicsMaterial;
        
        private Queue<GameObject> coinPool = new Queue<GameObject>();
        private List<GameObject> activeCoins = new List<GameObject>();
        private bool isInitialized = false;
        
        public GameObject CoinPrefab => coinPrefab;
        public List<GameObject> ActiveCoins => activeCoins;
        public int ActiveCoinCount => activeCoins.Count;
        
        private void Start()
        {
            InitializePool();
        }
        
        private void InitializePool()
        {
            if (isInitialized) return;
            
            if (coinPrefab == null)
            {
                Debug.LogError("CoinPrefab is not assigned!");
                return;
            }
            
            isInitialized = true;
            Debug.Log("CoinPoolManager initialized");
        }
        
        public void SetCoinPrefab(GameObject prefab)
        {
            coinPrefab = prefab;
            InitializePool();
        }
        
        public GameObject GetCoinFromPool()
        {
            GameObject coin;
            
            if (coinPool.Count > 0)
            {
                coin = coinPool.Dequeue();
                coin.SetActive(true);
            }
            else
            {
                if (activeCoins.Count >= maxConcurrentCoins)
                {
                    Debug.LogWarning($"Max concurrent coins reached: {maxConcurrentCoins}");
                    return null;
                }
                
                coin = Instantiate(coinPrefab);
                ConfigureCoinPhysics(coin);
            }
            
            return coin;
        }
        
        public void ReturnCoinToPool(GameObject coin)
        {
            if (coin == null) return;
            
            coin.SetActive(false);
            coinPool.Enqueue(coin);
            activeCoins.Remove(coin);
        }
        
        public void ReturnAllCoinsToPool()
        {
            for (int i = activeCoins.Count - 1; i >= 0; i--)
            {
                if (activeCoins[i] != null)
                {
                    ReturnCoinToPool(activeCoins[i]);
                }
            }
            activeCoins.Clear();
        }
        
        private void ConfigureCoinPhysics(GameObject coin)
        {
            Rigidbody rb = coin.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = coinMass;
                rb.drag = coinDrag;
                rb.angularDrag = coinAngularDrag;
                
                Collider collider = coin.GetComponent<Collider>();
                if (collider != null && coinPhysicsMaterial != null)
                {
                    collider.material = coinPhysicsMaterial;
                }
            }
        }
        
        /// <summary>
        /// メモリリーク防止：プール内の全オブジェクトを完全クリア
        /// </summary>
        void OnDestroy()
        {
            Debug.LogWarning($"[CoinPoolManager] Destroying pool with {activeCoins.Count} active coins and {coinPool.Count} pooled coins");
            
            // アクティブコインを安全に破棄
            for (int i = activeCoins.Count - 1; i >= 0; i--)
            {
                if (activeCoins[i] != null)
                {
                    DestroyImmediate(activeCoins[i]);
                }
            }
            activeCoins.Clear();
            
            // プール内のコインを安全に破棄
            while (coinPool.Count > 0)
            {
                GameObject coin = coinPool.Dequeue();
                if (coin != null)
                {
                    DestroyImmediate(coin);
                }
            }
            coinPool.Clear();
            
            Debug.LogWarning("[CoinPoolManager] Pool destruction complete");
        }
    }
}

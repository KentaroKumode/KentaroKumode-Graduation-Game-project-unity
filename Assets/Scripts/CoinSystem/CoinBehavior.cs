using UnityEngine;

namespace CoinSystem
{
    /// <summary>
    /// 個々のコインの動作を管理するコンポーネント
    /// </summary>
    public class CoinBehavior : MonoBehaviour
    {
        [Header("コイン設定")]
        [SerializeField] private float settleThreshold = 0.1f; // 静止判定の閾値
        [SerializeField] private float settleTime = 1f; // 静止判定の時間
        
        private Rigidbody coinRigidbody;
        private bool isSettled = false;
        private float settleTimer = 0f;
        
        public bool IsSettled => isSettled;
        
        private void Awake()
        {
            coinRigidbody = GetComponent<Rigidbody>();
            if (coinRigidbody == null)
            {
                Debug.LogError("CoinBehavior requires a Rigidbody component!");
            }
        }
        
        private void Update()
        {
            CheckIfSettled();
        }
        
        private void CheckIfSettled()
        {
            if (coinRigidbody == null) return;
            
            // 速度が閾値以下かチェック
            bool isMovingSlowly = coinRigidbody.velocity.magnitude < settleThreshold &&
                                  coinRigidbody.angularVelocity.magnitude < settleThreshold;
            
            if (isMovingSlowly)
            {
                settleTimer += Time.deltaTime;
                if (settleTimer >= settleTime && !isSettled)
                {
                    isSettled = true;
                    OnCoinSettled();
                }
            }
            else
            {
                settleTimer = 0f;
                isSettled = false;
            }
        }
        
        private void OnCoinSettled()
        {
            // コインが静止したときの処理
            // 必要に応じて音効果やエフェクトを追加
        }
        
        /// <summary>
        /// コインの状態をリセット
        /// </summary>
        public void ResetCoin()
        {
            isSettled = false;
            settleTimer = 0f;
            
            if (coinRigidbody != null)
            {
                coinRigidbody.velocity = Vector3.zero;
                coinRigidbody.angularVelocity = Vector3.zero;
            }
        }
        
        private void OnEnable()
        {
            ResetCoin();
        }
    }
}
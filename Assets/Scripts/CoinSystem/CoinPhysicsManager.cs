using UnityEngine;
using System.Collections;

namespace CoinSystem
{
    /// <summary>
    /// コインの物理演算を管理するクラス
    /// </summary>
    public class CoinPhysicsManager : MonoBehaviour
    {
        [Header("物理設定")]
        [SerializeField] private float dispenseForce = 5f;
        [SerializeField] private float randomForceRange = 1f;
        
        // 一時計算用変数（ガベージコレクション最適化）
        private Vector3 randomOffset = Vector3.zero;
        private Vector3 tempTorque = Vector3.zero;
        
        /// <summary>
        /// コインに物理的な排出力を適用
        /// </summary>
        /// <param name="coin">対象コイン</param>
        /// <param name="dispenserPoint">排出位置</param>
        /// <param name="potTarget">ポット位置</param>
        public void ApplyDispensePhysics(GameObject coin, Transform dispenserPoint, Transform potTarget)
        {
            if (coin == null || dispenserPoint == null || potTarget == null) return;
            
            Rigidbody rb = coin.GetComponent<Rigidbody>();
            if (rb == null) return;
            
            // 初期状態をリセット
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            
            // ポット誘導方向の力＋ランダム誤差を作成
            Vector3 directionToPot = (potTarget.position - dispenserPoint.position).normalized;
            
            // パラメータ計算: ランダムオフセットの一時変数
            randomOffset.x = UnityEngine.Random.Range(-randomForceRange, randomForceRange);
            randomOffset.y = UnityEngine.Random.Range(0, randomForceRange * 0.5f);
            randomOffset.z = UnityEngine.Random.Range(-randomForceRange, randomForceRange);
            
            Vector3 finalDirection = (directionToPot + randomOffset).normalized;
            rb.AddForce(finalDirection * dispenseForce, ForceMode.Impulse);
            
            // 回転力を追加
            tempTorque.x = UnityEngine.Random.Range(-1f, 1f);
            tempTorque.y = UnityEngine.Random.Range(-1f, 1f);
            tempTorque.z = UnityEngine.Random.Range(-1f, 1f);
            tempTorque = tempTorque.normalized * dispenseForce * 0.5f;
            rb.AddTorque(tempTorque, ForceMode.Impulse);
        }
        
        /// <summary>
        /// 物理設定を更新
        /// </summary>
        public void UpdatePhysicsSettings(float newDispenseForce, float newRandomForceRange)
        {
            dispenseForce = newDispenseForce;
            randomForceRange = newRandomForceRange;
        }
        
        /// <summary>
        /// 現在の物理設定を取得
        /// </summary>
        public (float dispenseForce, float randomForceRange) GetPhysicsSettings()
        {
            return (dispenseForce, randomForceRange);
        }
    }
}
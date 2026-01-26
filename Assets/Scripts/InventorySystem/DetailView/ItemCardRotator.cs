using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// 3Dカードの自動回転
    /// </summary>
    public class ItemCardRotator : MonoBehaviour
    {
        [Header("回転設定")]
        [SerializeField] private float rotationSpeed = 30f;
        [SerializeField] private Vector3 rotationAxis = Vector3.up;
        
        private GameObject targetCard;
        private bool isRotating = false;
        
        /// <summary>
        /// 回転開始
        /// </summary>
        public void StartRotation(GameObject card)
        {
            targetCard = card;
            isRotating = true;
        }
        
        /// <summary>
        /// 回転停止
        /// </summary>
        public void StopRotation()
        {
            isRotating = false;
            targetCard = null;
        }
        
        void Update()
        {
            if (isRotating && targetCard != null)
            {
                targetCard.transform.Rotate(rotationAxis, rotationSpeed * Time.deltaTime, Space.World);
            }
        }
        
        /// <summary>
        /// メモリリーク防止のクリーンアップ
        /// </summary>
        void OnDestroy()
        {
            StopRotation();
        }
    }
}

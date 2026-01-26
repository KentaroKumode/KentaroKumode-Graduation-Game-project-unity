using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// インベントリ整理中のカメラ移動をブロック
    /// </summary>
    public class CameraLockController : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private CameraMouseFollow cameraController;
        
        [Header("警告UI")]
        [SerializeField] private GameObject warningPanel;
        
        private bool isLocked = false;
        private System.Action onForceClose;
        
        void Start()
        {
            if (cameraController == null)
            {
                cameraController = FindObjectOfType<CameraMouseFollow>();
            }
            
            if (warningPanel != null)
            {
                warningPanel.SetActive(false);
            }
        }
        
        /// <summary>
        /// カメラをロック
        /// </summary>
        public void LockCamera(System.Action onForceCloseCallback)
        {
            isLocked = true;
            onForceClose = onForceCloseCallback;
            
            // カメラ移動を無効化
            if (cameraController != null)
            {
                cameraController.enabled = false;
            }
            
            Debug.Log("[CameraLockController] Camera locked");
        }
        
        /// <summary>
        /// カメラをアンロック
        /// </summary>
        public void UnlockCamera()
        {
            isLocked = false;
            onForceClose = null;
            
            // カメラ移動を有効化
            if (cameraController != null)
            {
                cameraController.enabled = true;
            }
            
            if (warningPanel != null)
            {
                warningPanel.SetActive(false);
            }
            
            Debug.Log("[CameraLockController] Camera unlocked");
        }
        
        /// <summary>
        /// カメラ移動試行を検知
        /// </summary>
        public void OnCameraMovementAttempted()
        {
            if (!isLocked) return;
            
            // 警告表示
            ShowWarning();
        }
        
        /// <summary>
        /// 警告表示
        /// </summary>
        private void ShowWarning()
        {
            if (warningPanel != null)
            {
                warningPanel.SetActive(true);
            }
            
            Debug.LogWarning("[CameraLockController] Camera movement blocked. Inventory must be organized first.");
        }
        
        /// <summary>
        /// 強制終了確認
        /// </summary>
        public void ConfirmForceClose()
        {
            // アイテムを削除して閉じる
            onForceClose?.Invoke();
            UnlockCamera();
        }
        
        /// <summary>
        /// 強制終了キャンセル
        /// </summary>
        public void CancelForceClose()
        {
            if (warningPanel != null)
            {
                warningPanel.SetActive(false);
            }
        }
    }
}

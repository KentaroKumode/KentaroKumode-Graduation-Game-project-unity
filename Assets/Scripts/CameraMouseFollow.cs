using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// マウスカーソル位置に応じてカメラを微妙に移動させるコンポーネント
/// 画面端に近づくほどカメラがその方向に傾く
/// </summary>
public class CameraMouseFollow : MonoBehaviour
{
    [Header("位置設定（Transform参照）")]
    [SerializeField] private Transform leftPosition; // 左位置のTransform
    [SerializeField] private Transform centerPosition; // 中央位置のTransform（nullなら初期位置）
    [SerializeField] private Transform rightPosition; // 右位置のTransform
    
    [Header("移動設定（5エリア構造）")]
    [SerializeField, Range(0f, 0.2f)] private float farLeftThreshold = 0.1f;    // 一番左エリアの閾値
    [SerializeField, Range(0.1f, 0.3f)] private float centerLeftThreshold = 0.2f; // 中央よりの左エリアの閾値（中央の左境界）
    [SerializeField, Range(0.7f, 0.9f)] private float centerRightThreshold = 0.8f; // 中央よりの右エリアの閾値（中央の右境界）
    [SerializeField, Range(0.8f, 1f)] private float farRightThreshold = 0.9f;   // 一番右エリアの閾値
    [SerializeField, Range(0f, 1f)] private float smoothSpeed = 0.1f;          // 移動の滑らかさ
    [SerializeField] private bool applyRotation = true;                         // 回転も適用するか
    
    [Header("デバッグ表示")]
    [SerializeField] private bool showDebugInfo = true; // 判定情報を画面に表示
    
    [Header("参照")]
    [SerializeField] private Camera targetCamera; // 対象カメラ（nullなら自動取得）
    
    private Vector3 originalPosition; // カメラの初期位置
    private Quaternion originalRotation; // カメラの初期回転
    
    // 現在のカメラ状態
    private enum CameraState { Left, Center, Right }
    private CameraState currentState = CameraState.Center;
    
    // デバッグ用
    private float currentNormalizedX = 0f;
    private bool isOverUI = false;
    
    void Start()
    {
        // カメラ取得
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }
        
        if (targetCamera != null)
        {
            originalPosition = targetCamera.transform.localPosition;
            originalRotation = targetCamera.transform.localRotation;
            Debug.Log($"[CameraMouseFollow] Initialized. Original position: {originalPosition}, rotation: {originalRotation.eulerAngles}");
        }
        else
        {
            Debug.LogError("[CameraMouseFollow] Camera not found!");
            enabled = false;
        }
    }
    
    void Update()
    {
        if (targetCamera == null) return;
        
        // マウス位置を取得（スクリーン座標）
        Vector2 mousePosition = Input.mousePosition;
        
        // 正規化座標に変換（0 〜 1）
        float normalizedX = mousePosition.x / Screen.width;
        currentNormalizedX = normalizedX; // デバッグ用に保存
        
        // UI上にマウスがある場合はカメラ移動をスキップ
        isOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (isOverUI)
        {
            return;
        }
        
        // 5エリア構造でのラッチ式状態遷移
        switch (currentState)
        {
            case CameraState.Left:
                // 左から中央: Center-Rightエリアに入る必要がある
                if (normalizedX >= centerRightThreshold && normalizedX < farRightThreshold)
                {
                    currentState = CameraState.Center;
                }
                // 左から右: Far Rightエリアに入る
                else if (normalizedX >= farRightThreshold)
                {
                    currentState = CameraState.Right;
                }
                // それ以外は左のまま
                break;
                
            case CameraState.Center:
                // 中央から左: Far Leftエリアに入る
                if (normalizedX < farLeftThreshold)
                {
                    currentState = CameraState.Left;
                }
                // 中央から右: Far Rightエリアに入る
                else if (normalizedX >= farRightThreshold)
                {
                    currentState = CameraState.Right;
                }
                // それ以外は中央のまま
                break;
                
            case CameraState.Right:
                // 右から中央: Center-Leftエリアに入る必要がある
                if (normalizedX >= farLeftThreshold && normalizedX < centerLeftThreshold)
                {
                    currentState = CameraState.Center;
                }
                // 右から左: Far Leftエリアに入る
                else if (normalizedX < farLeftThreshold)
                {
                    currentState = CameraState.Left;
                }
                // それ以外は右のまま
                break;
        }
        
        // 現在の状態に応じたターゲット位置を決定
        Vector3 targetPos;
        Quaternion targetRot;
        
        switch (currentState)
        {
            case CameraState.Left:
                if (leftPosition != null)
                {
                    targetPos = leftPosition.position;
                    targetRot = leftPosition.rotation;
                }
                else
                {
                    targetPos = originalPosition;
                    targetRot = originalRotation;
                }
                break;
                
            case CameraState.Right:
                if (rightPosition != null)
                {
                    targetPos = rightPosition.position;
                    targetRot = rightPosition.rotation;
                }
                else
                {
                    targetPos = originalPosition;
                    targetRot = originalRotation;
                }
                break;
                
            case CameraState.Center:
            default:
                if (centerPosition != null)
                {
                    targetPos = centerPosition.position;
                    targetRot = centerPosition.rotation;
                }
                else
                {
                    targetPos = originalPosition;
                    targetRot = originalRotation;
                }
                break;
        }
        
        // 滑らかに補間（位置）
        targetCamera.transform.position = Vector3.Lerp(
            targetCamera.transform.position,
            targetPos,
            smoothSpeed
        );
        
        // 滑らかに補間（回転）
        if (applyRotation)
        {
            targetCamera.transform.rotation = Quaternion.Slerp(
                targetCamera.transform.rotation,
                targetRot,
                smoothSpeed
            );
        }
    }
    
    void OnGUI()
    {
        // 完全に無効化（TLS Allocatorエラー防止）
        return;
    }
    
    private string GetCurrentAreaName(float normalizedX)
    {
        if (normalizedX < farLeftThreshold)
            return "Far Left (→左移動)";
        else if (normalizedX < centerLeftThreshold)
            return "Center-Left (右→中央)";
        else if (normalizedX < centerRightThreshold)
            return "Center (維持)";
        else if (normalizedX < farRightThreshold)
            return "Center-Right (左→中央)";
        else
            return "Far Right (→右移動)";
    }
    
    /// <summary>
    /// カメラ位置をリセット
    /// </summary>
    [ContextMenu("Reset Camera Position")]
    public void ResetPosition()
    {
        if (targetCamera != null)
        {
            targetCamera.transform.localPosition = originalPosition;
            Debug.Log("[CameraMouseFollow] Camera position reset");
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (targetCamera == null) return;
        
        // 3つの位置を可視化
        
        // 左位置
        if (leftPosition != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(leftPosition.position, 0.2f);
            Gizmos.DrawLine(targetCamera.transform.position, leftPosition.position);
        }
        
        // 中央位置
        if (centerPosition != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(centerPosition.position, 0.2f);
            Gizmos.DrawLine(targetCamera.transform.position, centerPosition.position);
        }
        else
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(originalPosition, 0.2f);
        }
        
        // 右位置
        if (rightPosition != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(rightPosition.position, 0.2f);
            Gizmos.DrawLine(targetCamera.transform.position, rightPosition.position);
        }
    }
}

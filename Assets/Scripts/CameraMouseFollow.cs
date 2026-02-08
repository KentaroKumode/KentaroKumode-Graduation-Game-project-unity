using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// WASD キーでカメラビューポイントを切り替えるコンポーネント
/// 
/// <para><b>操作:</b></para>
/// <list type="bullet">
///   <item>A → viewpoint_inv（インベントリビュー = 左位置）</item>
///   <item>D → viewpoint_pot（ポットビュー = 右位置）</item>
///   <item>W → viewpoint_base（ベースビュー = 中央位置）</item>
/// </list>
/// 
/// <para><b>補間:</b></para>
/// Lerp/Slerpで滑らかに遷移
/// </summary>
public class CameraMouseFollow : MonoBehaviour
{
    [Header("ビューポイント設定（Transform参照）")]
    [SerializeField] private Transform leftPosition;   // viewpoint_inv（Aキー）
    [SerializeField] private Transform centerPosition; // viewpoint_base（Wキー）
    [SerializeField] private Transform rightPosition;  // viewpoint_pot（Dキー）
    
    [Header("移動設定")]
    [SerializeField, Range(1f, 20f)] private float moveSpeed = 8f;  // 移動速度（高いほど即応）
    [SerializeField] private bool applyRotation = true;              // 回転も適用するか
    
    [Header("デバッグ表示")]
    [SerializeField] private bool showDebugInfo = true;
    
    [Header("参照")]
    [SerializeField] private Camera targetCamera;
    
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    
    // 現在のカメラ状態
    public enum CameraState { Inventory, Base, Pot }
    private CameraState currentState = CameraState.Base;
    
    /// <summary>現在のカメラ状態</summary>
    public CameraState CurrentState => currentState;
    
    // カメラロック（D&D中にカメラ移動を無効化する用）
    private bool isLocked = false;
    public bool IsLocked => isLocked;
    
    void Start()
    {
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
            Debug.Log($"[CameraWASD] Initialized. Default: viewpoint_base");
        }
        else
        {
            Debug.LogError("[CameraWASD] Camera not found!");
            enabled = false;
        }
    }
    
    void Update()
    {
        if (targetCamera == null || isLocked) return;
        
        // ===== WASD入力でビューポイント切り替え =====
        if (Input.GetKeyDown(KeyCode.A))
        {
            SetViewpoint(CameraState.Inventory);
        }
        else if (Input.GetKeyDown(KeyCode.D))
        {
            SetViewpoint(CameraState.Pot);
        }
        else if (Input.GetKeyDown(KeyCode.W))
        {
            SetViewpoint(CameraState.Base);
        }
        
        // 現在の状態に応じたターゲット位置を決定
        Vector3 targetPos;
        Quaternion targetRot;
        GetTargetTransform(out targetPos, out targetRot);
        
        // 滑らかに補間（位置）— Time.deltaTime依存でフレームレート非依存
        float t = 1f - Mathf.Exp(-moveSpeed * Time.deltaTime);
        targetCamera.transform.position = Vector3.Lerp(
            targetCamera.transform.position,
            targetPos,
            t
        );
        
        // 滑らかに補間（回転）
        if (applyRotation)
        {
            targetCamera.transform.rotation = Quaternion.Slerp(
                targetCamera.transform.rotation,
                targetRot,
                t
            );
        }
    }
    
    // =================================================================
    //  公開 API
    // =================================================================
    
    /// <summary>ビューポイントを切り替え</summary>
    public void SetViewpoint(CameraState state)
    {
        if (currentState == state) return;
        
        currentState = state;
        string viewName = state switch
        {
            CameraState.Inventory => "viewpoint_inv (A)",
            CameraState.Pot => "viewpoint_pot (D)",
            CameraState.Base => "viewpoint_base (W)",
            _ => "unknown"
        };
        Debug.Log($"[CameraWASD] → {viewName}");
    }
    
    /// <summary>カメラ移動をロック</summary>
    public void LockCamera()
    {
        isLocked = true;
    }
    
    /// <summary>カメラ移動をアンロック</summary>
    public void UnlockCamera()
    {
        isLocked = false;
    }
    
    /// <summary>カメラ位置をリセット（viewpoint_baseへ）</summary>
    [ContextMenu("Reset Camera Position")]
    public void ResetPosition()
    {
        currentState = CameraState.Base;
        if (targetCamera != null)
        {
            Vector3 targetPos;
            Quaternion targetRot;
            GetTargetTransform(out targetPos, out targetRot);
            targetCamera.transform.position = targetPos;
            targetCamera.transform.rotation = targetRot;
            Debug.Log("[CameraWASD] Camera reset to viewpoint_base");
        }
    }
    
    // =================================================================
    //  内部メソッド
    // =================================================================
    
    private void GetTargetTransform(out Vector3 targetPos, out Quaternion targetRot)
    {
        switch (currentState)
        {
            case CameraState.Inventory:
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
                
            case CameraState.Pot:
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
                
            case CameraState.Base:
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
    }
    
    void OnGUI()
    {
        // 完全に無効化（TLS Allocatorエラー防止）
        return;
    }
    
    void OnDrawGizmosSelected()
    {
        if (targetCamera == null) return;
        
        // viewpoint_inv（左）
        if (leftPosition != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(leftPosition.position, 0.2f);
            Gizmos.DrawLine(targetCamera.transform.position, leftPosition.position);
        }
        
        // viewpoint_base（中央）
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
        
        // viewpoint_pot（右）
        if (rightPosition != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(rightPosition.position, 0.2f);
            Gizmos.DrawLine(targetCamera.transform.position, rightPosition.position);
        }
    }
}

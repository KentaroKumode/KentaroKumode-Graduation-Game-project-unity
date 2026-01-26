using UnityEngine;

/// <summary>
/// 完全ライト依存の描画状態を実現
/// ライトが存在しない場合は完全に黒、ライトが当たった部分のみ可視化
/// </summary>
public class CompleteDarknessMode : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private bool applyOnAwake = true;
    
    void Awake()
    {
        if (applyOnAwake)
        {
            ApplyCompleteDarknessMode();
        }
    }
    
    /// <summary>
    /// 完全ライト依存モードを適用
    /// </summary>
    [ContextMenu("Apply Complete Darkness Mode")]
    public void ApplyCompleteDarknessMode()
    {
        Debug.Log("=== Complete Darkness Mode Setup Started ===");
        
        // 1. 環境光（Ambient Light）を完全に無効化
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientIntensity = 0f;
        Debug.Log("Ambient Light disabled: Color=#000000, Intensity=0");
        
        // 2. Skybox を無効化
        RenderSettings.skybox = null;
        Debug.Log("Skybox disabled: Material=None");
        
        // 3. グローバルイルミネーション（GI）を無効化
        // Note: Realtime/Baked GI は Lighting Settings でのみ設定可能（ランタイム変更不可）
        // プロジェクト設定で手動無効化が必要
        
        // 4. 反射光・間接反射を完全に遮断
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.reflectionIntensity = 0f;
        Debug.Log("Environment Reflections disabled: Source=Custom, Cubemap=None, Intensity=0");
        
        // 5. メインカメラの背景を完全な黒に設定
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.black;
            Debug.Log("Main Camera: Clear Flags=SolidColor, Background=#000000");
        }
        else
        {
            Debug.LogWarning("Main Camera not found. Please set Camera manually: Clear Flags=SolidColor, Background=#000000");
        }
        
        // 6. Fog を無効化（霧による可視化防止）
        RenderSettings.fog = false;
        Debug.Log("Fog disabled");
        
        Debug.Log("=== Complete Darkness Mode Setup Complete ===");
        Debug.Log("VERIFICATION CHECKLIST:");
        Debug.Log("- Ambient Light: " + (RenderSettings.ambientIntensity == 0 ? "✓ OFF" : "✗ ON"));
        Debug.Log("- Skybox: " + (RenderSettings.skybox == null ? "✓ None" : "✗ Active"));
        Debug.Log("- Reflections: " + (RenderSettings.reflectionIntensity == 0 ? "✓ OFF" : "✗ ON"));
        Debug.Log("- Camera Background: " + (mainCamera != null && mainCamera.backgroundColor == Color.black ? "✓ Black" : "✗ Not Black"));
        Debug.Log("");
        Debug.Log("MANUAL CHECKS REQUIRED:");
        Debug.Log("1. Window > Rendering > Lighting > Realtime GI: OFF");
        Debug.Log("2. Window > Rendering > Lighting > Baked GI: OFF");
        Debug.Log("3. All Materials: Emission = #000000, Intensity = 0");
        Debug.Log("4. No Unlit Shaders in use");
        Debug.Log("5. URP/HDRP: Volume > Exposure > Mode = Fixed, Value = 0");
    }
}

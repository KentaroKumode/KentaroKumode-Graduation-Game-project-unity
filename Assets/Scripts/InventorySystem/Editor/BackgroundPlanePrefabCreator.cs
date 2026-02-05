#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace InventorySystem.Editor
{
    /// <summary>
    /// BackgroundPlaneプレハブの自動生成ツール
    /// </summary>
    public class BackgroundPlanePrefabCreator : EditorWindow
    {
        private string prefabPath = "Assets/Prefabs/InventorySystem/";
        private string materialPath = "Assets/Prefabs/InventorySystem/Materials/";
        private string prefabName = "BackgroundPlane";
        private string materialName = "BackgroundPlane_Material";
        
        private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        private Vector3 prefabScale = new Vector3(2f, 1f, 2f);
        
        [MenuItem("Tools/InventorySystem/Create BackgroundPlane Prefab")]
        public static void ShowWindow()
        {
            GetWindow<BackgroundPlanePrefabCreator>("BackgroundPlane Creator");
        }
        
        private void OnGUI()
        {
            GUILayout.Label("BackgroundPlane Prefab Creator", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // パス設定
            GUILayout.Label("Paths", EditorStyles.boldLabel);
            prefabPath = EditorGUILayout.TextField("Prefab Path", prefabPath);
            materialPath = EditorGUILayout.TextField("Material Path", materialPath);
            
            EditorGUILayout.Space();
            
            // 名前設定
            GUILayout.Label("Names", EditorStyles.boldLabel);
            prefabName = EditorGUILayout.TextField("Prefab Name", prefabName);
            materialName = EditorGUILayout.TextField("Material Name", materialName);
            
            EditorGUILayout.Space();
            
            // プレハブ設定
            GUILayout.Label("Prefab Settings", EditorStyles.boldLabel);
            backgroundColor = EditorGUILayout.ColorField("Background Color", backgroundColor);
            prefabScale = EditorGUILayout.Vector3Field("Scale", prefabScale);
            
            EditorGUILayout.Space();
            
            if (GUILayout.Button("Create BackgroundPlane Prefab", GUILayout.Height(30)))
            {
                CreateBackgroundPlanePrefab();
            }
            
            EditorGUILayout.Space();
            
            EditorGUILayout.HelpBox(
                "このツールは以下を作成します：\\n" +
                "1. BackgroundPlane用のマテリアル\\n" +
                "2. BackGroundPlaneコンポーネント付きのプレハブ\\n" +
                "3. 適切な設定でのPlaneオブジェクト", 
                MessageType.Info
            );
        }
        
        private void CreateBackgroundPlanePrefab()
        {
            try
            {
                // ディレクトリ作成
                CreateDirectories();
                
                // マテリアル作成
                Material material = CreateBackgroundMaterial();
                
                // プレハブ作成
                GameObject prefab = CreatePrefabGameObject(material);
                
                // プレハブ保存
                string fullPrefabPath = Path.Combine(prefabPath, prefabName + ".prefab");
                PrefabUtility.SaveAsPrefabAsset(prefab, fullPrefabPath);
                
                // 一時オブジェクト削除
                DestroyImmediate(prefab);
                
                // アセット更新
                AssetDatabase.Refresh();
                
                Debug.Log($"[BackgroundPlanePrefabCreator] ✅ プレハブ作成完了: {fullPrefabPath}");
                EditorUtility.DisplayDialog("Success", $"BackgroundPlane prefab created at:\\n{fullPrefabPath}", "OK");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BackgroundPlanePrefabCreator] ❌ プレハブ作成エラー: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to create prefab:\\n{e.Message}", "OK");
            }
        }
        
        private void CreateDirectories()
        {
            if (!AssetDatabase.IsValidFolder(prefabPath))
            {
                Directory.CreateDirectory(prefabPath);
            }
            
            if (!AssetDatabase.IsValidFolder(materialPath))
            {
                Directory.CreateDirectory(materialPath);
            }
        }
        
        private Material CreateBackgroundMaterial()
        {
            // 既存のマテリアルをチェック
            string fullMaterialPath = Path.Combine(materialPath, materialName + ".mat");
            Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(fullMaterialPath);
            
            if (existingMaterial != null)
            {
                Debug.Log($"[BackgroundPlanePrefabCreator] 既存のマテリアルを使用: {fullMaterialPath}");
                return existingMaterial;
            }
            
            // 新規マテリアル作成
            Material material = new Material(Shader.Find("Standard"));
            material.name = materialName;
            
            // マテリアル設定
            material.color = backgroundColor;
            
            // アルファブレンド設定
            material.SetFloat("_Mode", 3); // Transparent mode
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
            
            // アセットとして保存
            AssetDatabase.CreateAsset(material, fullMaterialPath);
            
            Debug.Log($"[BackgroundPlanePrefabCreator] ✅ マテリアル作成完了: {fullMaterialPath}");
            return material;
        }
        
        private GameObject CreatePrefabGameObject(Material material)
        {
            // Planeオブジェクト作成
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = prefabName;
            
            // 不要なCollider削除
            Collider collider = plane.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyImmediate(collider);
            }
            
            // マテリアル適用
            Renderer renderer = plane.GetComponent<Renderer>();
            renderer.material = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            
            // スケール設定
            plane.transform.localScale = prefabScale;
            
            // BackGroundPlaneコンポーネント追加
            BackGroundPlane backgroundPlane = plane.AddComponent<BackGroundPlane>();
            
            Debug.Log($"[BackgroundPlanePrefabCreator] ✅ プレハブオブジェクト作成完了: {plane.name}");
            return plane;
        }
    }
}
#endif
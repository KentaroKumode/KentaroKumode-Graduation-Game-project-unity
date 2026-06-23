using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Battle.Visual.EditorTools
{
    /// <summary>
    /// VFX 試打用テストシーンをワンクリックで生成する。
    /// Tools → VFX Test → Create Scene で発火。
    /// </summary>
    public static class VFXTestSceneCreator
    {
        private const string ScenePath = "Assets/Scenes/VFXTestScene.unity";

        [MenuItem("Tools/VFX Test/Create or Open Scene")]
        public static void CreateOrOpenScene()
        {
            if (System.IO.File.Exists(ScenePath))
            {
                if (EditorUtility.DisplayDialog(
                    "VFX Test Scene",
                    "既に VFXTestScene.unity が存在します。",
                    "開く", "新規作成して上書き"))
                {
                    EditorSceneManager.OpenScene(ScenePath);
                    return;
                }
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 1.2f, -6f);
            cam.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.05f, 0.08f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<Battle.Visual.PixelPostFilter>();

            // Light (簡易)
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.color = new Color(1f, 0.95f, 0.85f);
            lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            // Floor (グリッド感のある床)
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            var floorRenderer = floor.GetComponent<Renderer>();
            if (floorRenderer != null)
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0.15f, 0.15f, 0.18f);
                floorRenderer.sharedMaterial = mat;
            }

            // Player / Enemy のキューブ表示 (簡易プレースホルダ)
            var playerCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            playerCube.name = "PlayerStand";
            playerCube.transform.position = new Vector3(-2f, 0f, 0f);
            playerCube.transform.localScale = new Vector3(0.4f, 1f, 0.4f);
            var playerMat = new Material(Shader.Find("Standard"));
            playerMat.color = new Color(0.4f, 0.6f, 1f);
            playerCube.GetComponent<Renderer>().sharedMaterial = playerMat;

            var enemyCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            enemyCube.name = "EnemyStand";
            enemyCube.transform.position = new Vector3(2f, 0f, 0f);
            enemyCube.transform.localScale = new Vector3(0.4f, 1f, 0.4f);
            var enemyMat = new Material(Shader.Find("Standard"));
            enemyMat.color = new Color(1f, 0.4f, 0.4f);
            enemyCube.GetComponent<Renderer>().sharedMaterial = enemyMat;

            // Tester
            var testerGo = new GameObject("CombatVFXTester");
            var tester = testerGo.AddComponent<Battle.Visual.CombatVFXTester>();

            // Anchors を Player/Enemy の頭付近に
            var playerAnchorGo = new GameObject("PlayerAnchor");
            playerAnchorGo.transform.SetParent(playerCube.transform, false);
            playerAnchorGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            tester.playerAnchor = playerAnchorGo.transform;

            var enemyAnchorGo = new GameObject("EnemyAnchor");
            enemyAnchorGo.transform.SetParent(enemyCube.transform, false);
            enemyAnchorGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            tester.enemyAnchor = enemyAnchorGo.transform;

            // プレハブ自動収集
            tester.AutoPopulateBuiltIn();

            // 保存
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[VFXTestSceneCreator] 生成完了: {ScenePath}\n再生してQ/E/Space/Tab で動作確認。");

            // Inspector で確認しやすいよう選択
            Selection.activeGameObject = testerGo;
        }

        [MenuItem("Tools/VFX Test/Repopulate Prefabs in Current Scene")]
        public static void RepopulateCurrentScene()
        {
            var tester = Object.FindObjectOfType<Battle.Visual.CombatVFXTester>();
            if (tester == null)
            {
                Debug.LogWarning("[VFXTestSceneCreator] CombatVFXTester がシーンに見つかりません。");
                return;
            }
            tester.AutoPopulateBuiltIn();
        }

        [MenuItem("Tools/VFX Test/Add Pixel Filter to Main Camera (Full Screen)")]
        public static void AddPixelFilterToMainCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[VFXTestSceneCreator] MainCamera が見つかりません。 タグを確認してください。");
                return;
            }
            if (cam.GetComponent<Battle.Visual.PixelPostFilter>() != null)
            {
                Debug.Log("[VFXTestSceneCreator] PixelPostFilter は既にアタッチされています。");
                Selection.activeGameObject = cam.gameObject;
                return;
            }
            cam.gameObject.AddComponent<Battle.Visual.PixelPostFilter>();
            EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
            Selection.activeGameObject = cam.gameObject;
            Debug.Log("[VFXTestSceneCreator] PixelPostFilter (全画面) を MainCamera に追加しました。 [P] でトグル。");
        }

        [MenuItem("Tools/VFX Test/Add VFX-Only Pixel Compositor to Main Camera")]
        public static void AddVFXPixelCompositorToMainCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[VFXTestSceneCreator] MainCamera が見つかりません。 タグを確認してください。");
                return;
            }
            // 全画面フィルタが既にある場合は除去 (二重描画防止)
            var fullScreen = cam.GetComponent<Battle.Visual.PixelPostFilter>();
            if (fullScreen != null)
            {
                Object.DestroyImmediate(fullScreen);
                Debug.Log("[VFXTestSceneCreator] 既存の全画面 PixelPostFilter を除去しました。");
            }
            if (cam.GetComponent<Battle.Visual.VFXPixelCompositor>() != null)
            {
                Debug.Log("[VFXTestSceneCreator] VFXPixelCompositor は既にアタッチされています。");
                Selection.activeGameObject = cam.gameObject;
                return;
            }
            cam.gameObject.AddComponent<Battle.Visual.VFXPixelCompositor>();
            EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
            Selection.activeGameObject = cam.gameObject;
            Debug.Log("[VFXTestSceneCreator] VFXPixelCompositor を MainCamera に追加しました。 [P] enable [I] α閾値 [O] 量子化");
        }
    }
}

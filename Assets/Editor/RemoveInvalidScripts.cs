using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// ScriptableObjectが誤ってGameObjectに追加されている場合に自動除去するエディタツール
/// </summary>
[InitializeOnLoad]
public static class RemoveInvalidScripts
{
    static RemoveInvalidScripts()
    {
        EditorApplication.delayCall += CleanupOnce;
    }

    [MenuItem("Tools/Inventory System/Remove Invalid Scripts from Scene")]
    public static void CleanupFromMenu()
    {
        int removed = CleanupScene();
        EditorUtility.DisplayDialog("完了", $"無効なスクリプトを {removed} 件除去しました。", "OK");
    }

    private static void CleanupOnce()
    {
        int removed = CleanupScene();
        if (removed > 0)
        {
            Debug.Log($"[RemoveInvalidScripts] 無効なスクリプトを {removed} 件自動除去しました。");
        }
    }

    private static int CleanupScene()
    {
        int removedCount = 0;
        foreach (var go in Object.FindObjectsOfType<GameObject>())
        {
            int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (count > 0)
            {
                Undo.RegisterCompleteObjectUndo(go, "Remove Invalid Scripts");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                removedCount += count;
                Debug.Log($"[RemoveInvalidScripts] '{go.name}' から無効スクリプト {count} 件を除去");
            }
        }

        if (removedCount > 0)
        {
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        return removedCount;
    }
}

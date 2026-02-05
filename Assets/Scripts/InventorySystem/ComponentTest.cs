using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// ItemPreviewStatusUIコンポーネント認識テスト
    /// </summary>
    public class ComponentTest : MonoBehaviour
    {
        [Header("コンポーネント認識テスト")]
        [SerializeField] private ItemPreviewStatusUI testStatusUI;
        
        void Start()
        {
            Debug.Log("=== Component Recognition Test ===");
            
            // 型の存在確認
            System.Type statusUIType = typeof(ItemPreviewStatusUI);
            Debug.Log($"ItemPreviewStatusUI Type exists: {statusUIType != null}");
            Debug.Log($"Type name: {statusUIType?.FullName}");
            
            // FindObjectOfType テスト
            ItemPreviewStatusUI foundUI = FindObjectOfType<ItemPreviewStatusUI>();
            Debug.Log($"FindObjectOfType result: {foundUI != null}");
            
            // 手動作成テスト
            try
            {
                GameObject testObj = new GameObject("ComponentTestObject");
                ItemPreviewStatusUI addedComponent = testObj.AddComponent<ItemPreviewStatusUI>();
                Debug.Log($"AddComponent success: {addedComponent != null}");
                
                if (addedComponent != null)
                {
                    Debug.Log($"Component added successfully: {addedComponent.GetType().Name}");
                    DestroyImmediate(testObj); // テスト後に削除
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"AddComponent failed: {ex.Message}");
            }
        }
        
        void OnValidate()
        {
            // Inspector上での参照テスト
            if (testStatusUI != null)
            {
                Debug.Log($"Inspector reference works: {testStatusUI.GetType().Name}");
            }
        }
    }
}

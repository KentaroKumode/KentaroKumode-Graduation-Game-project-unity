using UnityEngine;
using UnityEditor;

namespace InventorySystem
{
#if UNITY_EDITOR
    [CustomEditor(typeof(GridCell))]
    public class GridCellEditor : Editor
    {
        private SerializedProperty lockVisualProp;
        private SerializedProperty cellRendererProp;
        private SerializedProperty showDebugInfoProp;
        private SerializedProperty indicatorYOffsetProp;
        
        private void OnEnable()
        {
            lockVisualProp = serializedObject.FindProperty("lockVisual");
            cellRendererProp = serializedObject.FindProperty("cellRenderer");
            showDebugInfoProp = serializedObject.FindProperty("showDebugInfo");
            indicatorYOffsetProp = serializedObject.FindProperty("indicatorYOffset");
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            GridCell gridCell = (GridCell)target;
            
            // ヘッダー
            EditorGUILayout.LabelField("Grid Cell Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // ビジュアル設定
            EditorGUILayout.LabelField("ビジュアル設定", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(lockVisualProp, new GUIContent("ロック表示オブジェクト"));
            EditorGUILayout.PropertyField(cellRendererProp, new GUIContent("セルレンダラー"));
            
            EditorGUILayout.Space(10);
            
            // インジケーター情報
            EditorGUILayout.LabelField("インジケーター設定", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("ℹ️ インジケーターはGridManagerで一元管理されています", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("設定場所: GridManager > デフォルトインジケーター設定", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // インジケーター調整
            EditorGUILayout.LabelField("🎛️ インジケーター調整", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(indicatorYOffsetProp, new GUIContent("Y座標オフセット (フォールバック用)"));
            if (EditorGUI.EndChangeCheck())
            {
                // リアルタイム更新（Play中のみ）
                if (Application.isPlaying && gridCell != null)
                {
                    gridCell.UpdateIndicatorPosition();
                }
            }
            
            EditorGUILayout.Space(10);
            
            // デバッグ情報
            EditorGUILayout.PropertyField(showDebugInfoProp, new GUIContent("デバッグ情報を表示"));
            
            if (showDebugInfoProp.boolValue)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("🔍 デバッグ情報", EditorStyles.miniLabel);
                
                // 座標情報
                EditorGUILayout.LabelField($"グリッド座標: ({gridCell.GridX}, {gridCell.GridY})");
                EditorGUILayout.LabelField($"ロック状態: {(gridCell.IsLocked ? "ロック中" : "アンロック")}");
                EditorGUILayout.LabelField($"占有状態: {(gridCell.IsOccupied ? "占有中" : "空き")}");
                
                if (gridCell.IsOccupied && gridCell.OccupiedItem != null)
                {
                    EditorGUILayout.LabelField($"占有アイテム: {gridCell.OccupiedItem.itemName}");
                }
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.Space(10);
            
            // ランタイムテストボタン
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("🎮 ランタイムテスト", EditorStyles.boldLabel);
                
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("配置可能表示"))
                {
                    gridCell.ShowValidIndicator();
                }
                if (GUILayout.Button("配置不可表示"))
                {
                    gridCell.ShowInvalidIndicator();
                }
                if (GUILayout.Button("非表示"))
                {
                    gridCell.HideIndicatorTexture();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("ランタイム中にインジケーターテストボタンが表示されます。", MessageType.Info);
            }
            
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
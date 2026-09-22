using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UI.ClassSelect;

/// <summary>
/// 職業選択リグのシーン手配置 (2026-07-07 改)。 実行時は TitleMenuBootstrap が
/// ClassSelectRigBuilder で自動構築するため通常は不要。 位置・ドットサイズ等を
/// Inspector で恒久調整したい場合のみ、 タイトルシーンを開いて実行する
/// (手配置があると実行時の自動構築はスキップされ、 こちらが優先される)。
///
/// メニュー: Tools → ClassSelect → Setup In Current Scene
/// </summary>
public static class ClassSelectAutoSetup
{
    /// <summary>スキン登録アセットを Assets/Resources/ClassSelectSkin.asset に作成して選択する。
    /// Inspector にスプライト登録欄 (タブ/票/肖像/詳細/スタンプ、 各要素は複数レイヤの重ね) が出る。
    /// Resources 配下なので実行時自動構築でも読み込まれる。</summary>
    [MenuItem("Tools/ClassSelect/Create Skin Asset")]
    public static void CreateSkinAsset()
    {
        const string dir = "Assets/Resources";
        const string path = dir + "/" + ClassSelectSkinAsset.ResourceName + ".asset";

        var existing = AssetDatabase.LoadAssetAtPath<ClassSelectSkinAsset>(path);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("[ClassSelectAutoSetup] 既存のスキンアセットを選択");
            return;
        }

        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets", "Resources");
        var asset = ScriptableObject.CreateInstance<ClassSelectSkinAsset>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
        Debug.Log($"[ClassSelectAutoSetup] スキンアセット作成: {path} ── スプライトは PPU=8 (1px=1dot) でインポート");
    }

    /// <summary>シーン配置済みのリグを、 現在の Inspector 設定で子オブジェクトごと再ビルドする。
    /// 数値だけ変えて子スプライトの位置を反映させたい時、 プレイを回さず即確認できる。</summary>
    [MenuItem("Tools/ClassSelect/Rebuild Children")]
    public static void RebuildChildren()
    {
        var view = Object.FindObjectOfType<ClassSelectView>(true);
        if (view == null)
        {
            EditorUtility.DisplayDialog("ClassSelect Rebuild",
                "シーンに ClassSelectView が見つかりません。 先に Setup In Current Scene を実行してください。", "OK");
            return;
        }
        Undo.RegisterFullObjectHierarchyUndo(view.gameObject, "Rebuild ClassSelect Children");
        // 既存の子オブジェクトを全消去
        for (int i = view.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(view.transform.GetChild(i).gameObject);
        // Awake は Edit モードだと再発火しないので、 private Build() をリフレクションで直接呼び出す
        var buildMethod = typeof(ClassSelectView).GetMethod("Build",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        buildMethod.Invoke(view, null);
        EditorUtility.SetDirty(view.gameObject);
        EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        Debug.Log($"[ClassSelectAutoSetup] 子オブジェクトを再ビルド (子={view.transform.childCount})");
    }

    [MenuItem("Tools/ClassSelect/Setup In Current Scene")]
    public static void Setup()
    {
        var lcd = Object.FindObjectOfType<UI.Lcd.LcdScreen>();
        if (lcd == null || lcd.contentCamera == null)
        {
            EditorUtility.DisplayDialog("ClassSelect Setup",
                "LcdScreen (contentCamera 付き) が見つかりません。\nタイトルシーン (SampleScene) を開いてから実行してください。", "OK");
            return;
        }

        // 冪等: 既存があれば選択して終了
        var existing = Object.FindObjectOfType<ClassSelectView>(true);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[ClassSelectAutoSetup] 既存の ClassSelectView を選択 (再生成しない)");
            return;
        }

        var view = ClassSelectRigBuilder.Build(lcd);
        if (view == null) return;

        // ルートをシーン保存対象として登録
        var rigRoot = view.transform.root.gameObject;
        Undo.RegisterCreatedObjectUndo(rigRoot, "Create ClassSelectRig");

        // 手配置 TitleMenuRouter があれば配線
        var router = Object.FindObjectOfType<UI.TitleMenuRouter>(true);
        if (router != null)
        {
            Undo.RecordObject(router, "Wire ClassSelect");
            router.classSelect = view;
            EditorUtility.SetDirty(router);
        }

        EditorSceneManager.MarkSceneDirty(rigRoot.scene);
        Selection.activeGameObject = view.gameObject;
        Debug.Log($"[ClassSelectAutoSetup] 完了: シーン={rigRoot.scene.name} " +
                  $"router={(router != null ? "手配線" : "実行時Bootstrap任せ")} ── 保存を忘れずに (Ctrl+S)");
    }
}

using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UI.Game2D;

/// <summary>Game2D シーンに GUI の Canvas を組み立てる (2026-10-19)。 配置は Game2DLayout (gui-full-layout.html)。 何度実行しても作り直す。</summary>
public static class Game2DBuilder
{
    private const string Dir = "Assets/gui2d/";


    private static RectTransform Node(string name, Transform parent, int x, int y, int w, int h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    [MenuItem("Tools/Game2D/Build Canvas")]
    public static void Build()
    {
        var old = GameObject.Find("Game2DCanvas");
        if (old != null) Object.DestroyImmediate(old);

        // ルート (部品を載せる)。 GUI は専用カメラで 960×540 の RT へ描き (GuiCanvas)、 その RT を液晶の上に重ねる (PresentCanvas)。
        var cgo = new GameObject("Game2DCanvas");
        cgo.layer = 5;

        var guiCamGo = new GameObject("Game2DGuiCamera"); guiCamGo.transform.SetParent(cgo.transform, false); guiCamGo.layer = 5;
        var guiCam = guiCamGo.AddComponent<Camera>();
        guiCam.orthographic = true; guiCam.orthographicSize = 5; guiCam.nearClipPlane = 0.1f; guiCam.farClipPlane = 20f;
        guiCam.cullingMask = 1 << 5; guiCam.clearFlags = CameraClearFlags.SolidColor; guiCam.backgroundColor = new Color(0, 0, 0, 0);
        guiCam.depth = 5; guiCam.allowHDR = false; guiCam.allowMSAA = false;
        guiCamGo.transform.position = new Vector3(0, -5000, -10);          // 他の絵のそばに置かない

        var guiCanvasGo = new GameObject("GuiCanvas", typeof(Canvas), typeof(CanvasScaler)); guiCanvasGo.layer = 5;
        guiCanvasGo.transform.SetParent(cgo.transform, false);
        var guiCanvas = guiCanvasGo.GetComponent<Canvas>();
        guiCanvas.renderMode = RenderMode.ScreenSpaceCamera; guiCanvas.worldCamera = guiCam; guiCanvas.planeDistance = 1f;
        var guiScaler = guiCanvasGo.GetComponent<CanvasScaler>();
        guiScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; guiScaler.scaleFactor = 2f;       // GUI の 1 dot = RT の 2 画素

        var presentCanvasGo = new GameObject("PresentCanvas", typeof(Canvas), typeof(CanvasScaler)); presentCanvasGo.layer = 5;
        presentCanvasGo.transform.SetParent(cgo.transform, false);
        var canvas = presentCanvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
        presentCanvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // GUI 側の格子: カメラの RT いっぱい (480×270 dot)
        var gridRt = Node("Grid", guiCanvasGo.transform, 0, 0, Game2DLayout.Width, Game2DLayout.Height);
        gridRt.anchorMin = Vector2.zero; gridRt.anchorMax = Vector2.one; gridRt.offsetMin = gridRt.offsetMax = Vector2.zero;
        // 提示側の格子: 画面の中央に 480×270 dot (整数倍・Game2DScreen が置く)
        var presentGrid = Node("PresentGrid", presentCanvasGo.transform, 0, 0, Game2DLayout.Width, Game2DLayout.Height);

        // --- 航行の液晶 (地図): 480×270 の作図を、 航行を映すカメラの正面の Quad へ貼る。 旧来の地図のスプライトはその奥に隠れる ---
        var quadGo = GameObject.Find("Quad");
        var router = Object.FindObjectOfType<GameLoop.PhaseSceneRouter>();
        var gameplayCam = router != null ? router.targetCamera : null;
        MeshRenderer plotRenderer = null;
        if (gameplayCam != null)
        {
            var old2 = gameplayCam.transform.Find("MapPlotQuad");
            if (old2 != null) Object.DestroyImmediate(old2.gameObject);
            var pq = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pq.name = "MapPlotQuad";
            Object.DestroyImmediate(pq.GetComponent<Collider>());
            pq.transform.SetParent(gameplayCam.transform, false);
            pq.transform.localPosition = new Vector3(0, 0, 12f);
            pq.transform.localScale = new Vector3(30f, 16.875f, 1f);
            pq.layer = gameplayCam.gameObject.layer;
            plotRenderer = pq.GetComponent<MeshRenderer>();
            const string pm = "Assets/Settings/Game2DMapPlot.mat";
            var plotMat = AssetDatabase.LoadAssetAtPath<Material>(pm);
            if (plotMat == null) { plotMat = new Material(Shader.Find("Unlit/Texture")); AssetDatabase.CreateAsset(plotMat, pm); }
            plotRenderer.sharedMaterial = plotMat;
            plotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            plotRenderer.receiveShadows = false;
            plotRenderer.enabled = false;
        }
        // --- 戦闘演出 (battle-prototype.html): 地図と同じ位置に 2 枚目の Quad を置く (どちらか一方だけ出る) ---
        MeshRenderer battleRenderer = null;
        if (gameplayCam != null)
        {
            var old3 = gameplayCam.transform.Find("BattlePlotQuad");
            if (old3 != null) Object.DestroyImmediate(old3.gameObject);
            var bq = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bq.name = "BattlePlotQuad";
            Object.DestroyImmediate(bq.GetComponent<Collider>());
            bq.transform.SetParent(gameplayCam.transform, false);
            bq.transform.localPosition = new Vector3(0, 0, 12f);
            bq.transform.localScale = new Vector3(30f, 16.875f, 1f);
            bq.layer = gameplayCam.gameObject.layer;
            battleRenderer = bq.GetComponent<MeshRenderer>();
            battleRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/Game2DMapPlot.mat");
            battleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            battleRenderer.receiveShadows = false;
            battleRenderer.enabled = false;
        }
        var battleFx = cgo.AddComponent<UI.Game2D.Battle.Game2DBattleFx>();
        battleFx.plotQuad = battleRenderer;
        battleFx.backdrop = BattleTex("backdrop");
        var wn = new[] { "sword_t2", "sword_t3", "sword_t4", "dagger_t2", "dagger_t3", "dagger_t4", "axe_t2", "axe_t3", "axe_t4", "shield_t2", "shield_t3", "shield_t4", "dragon" };
        battleFx.weaponTex = new Texture2D[wn.Length];
        for (int i = 0; i < wn.Length; i++) battleFx.weaponTex[i] = BattleTex(wn[i]);

        var mapView = cgo.AddComponent<Game2DMapView>();
        mapView.plotQuad = plotRenderer;
        mapView.lcdQuad = quadGo != null ? quadGo.transform : null;

        var hudRoot = Node("Hud", gridRt, 0, 0, Game2DLayout.Width, Game2DLayout.Height);
        hudRoot.anchorMin = Vector2.zero; hudRoot.anchorMax = Vector2.one; hudRoot.offsetMin = hudRoot.offsetMax = Vector2.zero;

        // 航行の計器 (所持金・HP・物資・測量 / 囮罠 / 待つ・天気・ターン・所持品) は下の NavExtras が液晶の中の暗い帯として描く (2026-11-24・旧い筐体の部品は削除)

        var battleSim = cgo.AddComponent<UI.Game2D.Battle.Game2DBattleSim>(); battleSim.fx = battleFx;
        // 戦闘の画面は液晶の中 (Game2DBattleLcd・戦闘の流れが実行時に同じ GameObject に付ける・2026-11-16〜)。 旧い戦闘の HUD (BattleOnly のパネル群) は削除

        // --- 起動 (TOUCH DISPLAY → クリックで点灯)。 文字と覆いは実行時に液晶の中身として出す (液晶の効果が掛かる) ---
        var boot = cgo.AddComponent<Game2DBoot>();
        boot.lcd = quadGo != null ? quadGo.GetComponent<UI.Lcd.LcdScreen>() : null;
        boot.lcdSurface = quadGo != null ? quadGo.GetComponent<MeshRenderer>() : null;
        // フォントは UI.GameFont (ゲーム全体で 1 か所の指定) が持つ。 ここでは割り当てない

        // --- 液晶の効果 (最前面・クリックは通す) ---
        const string matPath = "Assets/Settings/Game2DLcdFx.mat";
        var fxMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (fxMat == null)
        {
            fxMat = new Material(Shader.Find("Game2D/LcdFx"));
            AssetDatabase.CreateAsset(fxMat, matPath);
        }
        // GUI の板 (液晶の上・仕上げの下): 影と色調を付けて重ねる
        const string gpPath = "Assets/Settings/Game2DGuiPresent.mat";
        var gpMat = AssetDatabase.LoadAssetAtPath<Material>(gpPath);
        if (gpMat == null) { gpMat = new Material(Shader.Find("Game2D/GuiPresent")); AssetDatabase.CreateAsset(gpMat, gpPath); }
        var gpRt = Node("GuiPresent", presentGrid, 0, 0, Game2DLayout.Width, Game2DLayout.Height);
        gpRt.anchorMin = Vector2.zero; gpRt.anchorMax = Vector2.one; gpRt.offsetMin = gpRt.offsetMax = Vector2.zero;
        var gpImg = gpRt.gameObject.AddComponent<RawImage>(); gpImg.raycastTarget = false;
        var guiRt = cgo.AddComponent<Game2DGuiRt>(); guiRt.guiCamera = guiCam; guiRt.present = gpImg; guiRt.presentMaterial = gpMat;

        var fxRt = Node("LcdFx", presentGrid, 0, 0, Game2DLayout.Width, Game2DLayout.Height);
        fxRt.anchorMin = Vector2.zero; fxRt.anchorMax = Vector2.one; fxRt.offsetMin = fxRt.offsetMax = Vector2.zero;
        var fxImg = fxRt.gameObject.AddComponent<RawImage>();
        fxImg.material = fxMat; fxImg.raycastTarget = false;

        // --- 仮置きの画面 (ショップ・イベント・報酬・休憩・秘宝・整備パネル ほか) と、 航行の計器の足りない分 ---
        var kit = cgo.AddComponent<Game2DKitAssets>();
        kit.frameTex = BattleUiTex("frame_gray"); kit.btnTex = BattleUiTex("btn"); kit.goTex = BattleUiTex("btn_go"); kit.dieTex = BattleUiTex("die");
        kit.rowTex = BattleUiTex("row"); kit.tabTex = BattleUiTex("tab"); kit.chipTex = BattleUiTex("chip"); kit.bannerTex = BattleUiTex("banner"); kit.slotTex = BattleUiTex("slot");
        kit.lampOnTex = BattleUiTex("lamp_on"); kit.lampOffTex = BattleUiTex("lamp_off"); kit.roundTex = BattleUiTex("round_btn"); kit.dialTex = BattleUiTex("dial"); kit.gaugeTex = BattleUiTex("gauge");
        kit.diceTex = new Texture2D[6]; for (int i = 0; i < 6; i++) kit.diceTex[i] = BattleUiTex("die_" + (i + 1));
        kit.iconAtlas = BattleUiTex("icons"); kit.iconNames = System.IO.File.ReadAllText(Dir + "battle_ui/icons.txt");
        var modalRt = Node("Modal", gridRt, 0, 0, Game2DLayout.Width, Game2DLayout.Height);
        modalRt.anchorMin = Vector2.zero; modalRt.anchorMax = Vector2.one; modalRt.offsetMin = modalRt.offsetMax = Vector2.zero;
        var modal = modalRt.gameObject.AddComponent<Game2DModal>(); modal.Build();
        modalRt.gameObject.AddComponent<Game2DPhaseScreens>().modal = modal;      // 実データの画面 (ショップ・イベント・報酬 ほか)
        cgo.AddComponent<Game2DLoadingScreen>();      // ロード画面 (液晶の板を地図の板の手前に複製して描く)
        var exRt = Node("NavExtras", hudRoot, 0, 0, Game2DLayout.Width, Game2DLayout.Height);
        exRt.anchorMin = Vector2.zero; exRt.anchorMax = Vector2.one; exRt.offsetMin = exRt.offsetMax = Vector2.zero;
        var extras = exRt.gameObject.AddComponent<Game2DNavExtras>(); extras.modal = modal; extras.Build();

        // --- 配線 ---
        // Hud は常に生きている Grid に付ける (hudRoot は Hud 自身が出し入れするので、 そこに付けると一度消えたら戻れない)
        var hud = gridRt.gameObject.AddComponent<Game2DHud>();
        hud.battleSim = battleSim; hud.extras = extras; hud.root = hudRoot.gameObject;

        // --- 液晶の Quad: 走査線・湾曲・色収差・ブルーム・ノイズ + 点灯の瞬間の残像 (Game2D/LcdScreen)。 ビネットは外枠まで覆う Game2D/LcdFx ---
        if (quadGo != null)
        {
            const string qm = "Assets/Settings/Game2DLcdScreen.mat";
            var screenMat = AssetDatabase.LoadAssetAtPath<Material>(qm);
            if (screenMat == null) { screenMat = new Material(Shader.Find("Game2D/LcdScreen")); AssetDatabase.CreateAsset(screenMat, qm); }
            var mr = quadGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = screenMat;
            var ghost = quadGo.GetComponent<Game2DLcdGhost>() ?? quadGo.AddComponent<Game2DLcdGhost>();
            ghost.lcd = quadGo.GetComponent<UI.Lcd.LcdScreen>(); ghost.surface = mr; ghost.bloomShader = Shader.Find("Hidden/Game2DBloom");
            var noise = quadGo.GetComponent<Game2DLcdNoise>() ?? quadGo.AddComponent<Game2DLcdNoise>();
            noise.surface = mr; noise.gain = 0.55f; noise.attackMs = 16f; noise.decayPow = 1.3f;
        }

        var cam = GameObject.Find("Main Camera").GetComponent<Camera>();
        var quad = GameObject.Find("Quad").transform;
        var screen = cgo.AddComponent<Game2DScreen>();
        screen.screenCamera = cam; screen.lcdQuad = quad; screen.canvas = canvas; screen.gridRoot = presentGrid; mapView.screen = screen; modal.screen = screen; extras.screen = screen;
        screen.Apply();

        // 開発用のデバッグ表示 (左上の文字) は、 液晶の作図と重なるので切る (キー操作は GameManager 側)
        foreach (var dbg in Object.FindObjectsOfType<GameLoop.GameDebugHUD>(true))
        {
            var so = new SerializedObject(dbg); var pr = so.FindProperty("showHUD");
            if (pr != null) { pr.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        Selection.activeGameObject = cgo;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    /// <summary>戦闘演出の絵 (Assets/gui2d/battle/): 1 dot ずつ読むので、 読み出し可・補間なし・非圧縮にそろえる。</summary>
    private static Texture2D BattleTex(string n)
    {
        var path = Dir + "battle/" + n + ".png";
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null && (!imp.isReadable || imp.filterMode != FilterMode.Point || imp.textureCompression != TextureImporterCompression.Uncompressed || imp.mipmapEnabled))
        {
            imp.isReadable = true; imp.filterMode = FilterMode.Point; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.mipmapEnabled = false; imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>戦闘の GUI の絵 (Assets/gui2d/battle_ui/・PixelLab で作った仮の絵)。 1 dot ずつ読むので、 読み出し可・補間なし・非圧縮にそろえる。 無ければ null (長方形で代用)。</summary>
    private static Texture2D BattleUiTex(string n)
    {
        var path = Dir + "battle_ui/" + n + ".png";
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return null;
        if (!imp.isReadable || imp.filterMode != FilterMode.Point || imp.textureCompression != TextureImporterCompression.Uncompressed || imp.mipmapEnabled)
        {
            imp.isReadable = true; imp.filterMode = FilterMode.Point; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.mipmapEnabled = false; imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

}

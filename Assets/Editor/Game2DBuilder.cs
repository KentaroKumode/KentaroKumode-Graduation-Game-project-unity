using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UI.Game2D;

/// <summary>Game2D シーンに GUI の Canvas を組み立てる (2026-10-19)。 配置は Game2DLayout (gui-full-layout.html)。 何度実行しても作り直す。</summary>
public static class Game2DBuilder
{
    private const string Dir = "Assets/gui2d/";

    private static Sprite S(string n) => AssetDatabase.LoadAssetAtPath<Sprite>(Dir + n + ".png");
    private static Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + n + ".png");

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

    private static Image Pic(string name, Transform parent, string sprite, int x, int y, bool raycast = false)
    {
        var sp = S(sprite);
        var rt = Node(name, parent, x, y, (int)sp.rect.width, (int)sp.rect.height);
        var im = rt.gameObject.AddComponent<Image>();
        im.sprite = sp; im.raycastTarget = raycast;
        return im;
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

        // HTML の描画の順: 資源バー → レーダー → 操作パネル → GOLD の入れ物 (入れ物が上・パネルが下)。 HP のバーは HTML に無いので、 実用のため足す。
        EnsureReadable("gold_pot_layer2", "gold_pot_layer3", "gold_pot_layer4", "gold_pot_layer5", "gold_pot_layer6", "coin_gui",
                       "button_trap", "button_trap_push", "button_flare", "button_flare_push", "button_recon", "button_recon_push", "button_frame",
                       "bar_layer1", "bar_layer2", "bar_layer3", "bar_layer4", "counter_strip");

        // --- 資源バー (bar_layer1〜4): 土台・物資/シールドの板・HP ---
        var resRt = Node("ResBar", hudRoot, 0, 0, Game2DLayout.Width, Game2DLayout.Height);
        var resBar = resRt.gameObject.AddComponent<Game2DResBar>();
        resBar.layer1 = T("bar_layer1"); resBar.layer2 = T("bar_layer2"); resBar.layer3 = T("bar_layer3"); resBar.layer4 = T("bar_layer4"); resBar.counterStrip = T("counter_strip");
        resBar.Build();

        // --- レーダー (2026-10-27 刷新): 資源バー (bar_layer1) の右の液晶 (左上 313,213・51x51) の上へ、 気配の円を描く。 素材の絵は使わない ---
        var radarRt = Node("Radar", hudRoot, 313, 213, 51, 51);
        var radarRaw = radarRt.gameObject.AddComponent<RawImage>(); radarRaw.raycastTarget = false;
        var radar = radarRt.gameObject.AddComponent<Game2DRadar>();
        radar.image = radarRaw;


        var navOnly = Node("NavOnly", hudRoot, 0, 0, Game2DLayout.Width, Game2DLayout.Height);
        navOnly.anchorMin = Vector2.zero; navOnly.anchorMax = Vector2.one; navOnly.offsetMin = navOnly.offsetMax = Vector2.zero;
        var battleOnly = Node("BattleOnly", hudRoot, 0, 0, Game2DLayout.Width, Game2DLayout.Height);
        battleOnly.anchorMin = Vector2.zero; battleOnly.anchorMax = Vector2.one; battleOnly.offsetMin = battleOnly.offsetMax = Vector2.zero;

        var battleSim = cgo.AddComponent<UI.Game2D.Battle.Game2DBattleSim>(); battleSim.fx = battleFx;
        var battleHud = battleOnly.gameObject.AddComponent<Game2DBattleHud>(); battleHud.sim = battleSim; battleHud.fx = battleFx;
        battleHud.btnAtkTex = BattleUiTex("btn_atk"); battleHud.btnBlkTex = BattleUiTex("btn_blk"); battleHud.btnChgTex = BattleUiTex("btn_chg"); battleHud.btnGoTex = BattleUiTex("btn_go");
        battleHud.Build();      // 戦闘の GUI (仮置きの長方形 + 字)

        // --- 操作パネル (map-prototype.html の BTNP): 枠の上端が、 閉じた GOLD の入れ物の下端 (54) に接する (枠は PNG の 3 行目から) → y = 51。
        //     下 = 3 つのボタン (押した絵は同じ場所に重ねて出し入れ)、 上 = 枠、 その上に残数ランプ、 いちばん上に影 ---
        var panelRt = Node("ButtonPanel", navOnly, 0, 51, 89, 124);
        var names = new[] { "button_trap", "button_flare", "button_recon" };
        var normals = new RawImage[3]; var pushes = new RawImage[3];
        for (int k = 0; k < 3; k++) normals[k] = FullRaw(names[k], panelRt, T(names[k]), 89, 124);
        for (int k = 0; k < 3; k++) { pushes[k] = FullRaw(names[k] + "_push", panelRt, T(names[k] + "_push"), 89, 124); pushes[k].enabled = false; }
        FullRaw("frame", panelRt, T("button_frame"), 89, 124);
        var lampX = new[] { 20, 27, 34 };
        var trapLamps = new Image[3]; var flareLamps = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            trapLamps[i] = Pic("trapLamp" + i, panelRt, "lamp_off", lampX[i], 47);
            flareLamps[i] = Pic("flareLamp" + i, panelRt, "lamp_off", lampX[i], 75);
        }
        var shadeRaw = FullRaw("potShade", panelRt, null, 89, 124); shadeRaw.enabled = false;

        // --- GOLD の入れ物 (map-prototype.html の GOLDPOT) (0,0) 76x96: 描画順 1 → 3 → 5 → 2 → 4 → 粒。 初期は閉じている ---
        var pot = Node("GoldPot", hudRoot, 0, 0, 76, 96);
        var l1 = Pic("layer1", pot, "gold_pot_layer1", 0, 0).rectTransform;
        var clip = Node("layer3Clip", pot, 0, 0, 76, 96); clip.gameObject.AddComponent<RectMask2D>();
        var l3raw = FullRaw("layer3", clip, T("gold_pot_layer3"), 76, 96);
        var l5 = Pic("layer5", pot, "gold_pot_layer5", 0, 0).rectTransform;
        var l2 = Pic("layer2", pot, "gold_pot_layer2", 0, 0).rectTransform;
        var l4 = Pic("layer4", pot, "gold_pot_layer4", 0, 0).rectTransform;
        var dispRaw = FullRaw("Display", l4, null, 76, 96);
        var partRaw = FullRaw("Particles", pot, null, 156, 176); partRaw.enabled = false; dispRaw.enabled = false;   // テクスチャを入れるまで出さない (null の RawImage は白く出る)
        var goldPot = pot.gameObject.AddComponent<Game2DGoldPot>();
        goldPot.layer1 = l1; goldPot.layer2 = l2; goldPot.layer4 = l4; goldPot.layer5 = l5; goldPot.layer3Clip = clip; goldPot.layer3 = l3raw;
        goldPot.display = dispRaw; goldPot.particles = partRaw;
        goldPot.tex2 = T("gold_pot_layer2"); goldPot.tex3 = T("gold_pot_layer3"); goldPot.tex4 = T("gold_pot_layer4"); goldPot.tex5 = T("gold_pot_layer5");
        goldPot.marker = T("gold_pot_layer6"); goldPot.coinSprite = T("coin_gui");
        goldPot.ApplyEditorPose();      // 再生していなくても、 閉じた状態 (HTML の初期) で見えるように

        // 操作パネルの制御 (ボタンの当たり判定・押す・影・残数ランプ)。 戦闘でも GOLD の入れ物は触れるので、 NavOnly の外に置く
        var ctl = Node("NavPanelCtl", hudRoot, 0, 0, 1, 1);
        var navPanel = ctl.gameObject.AddComponent<Game2DNavPanel>();
        navPanel.normal = normals; navPanel.push = pushes; navPanel.trapLamps = trapLamps; navPanel.flareLamps = flareLamps; navPanel.shade = shadeRaw;
        navPanel.normalTex = new[] { T("button_trap"), T("button_flare"), T("button_recon") };
        navPanel.pushTex = new[] { T("button_trap_push"), T("button_flare_push"), T("button_recon_push") };
        navPanel.frameTex = T("button_frame"); navPanel.lampOn = S("lamp"); navPanel.lampOff = S("lamp_off");
        navPanel.pot = goldPot; navPanel.view = mapView;
        mapView.panel = navPanel;

        // --- 起動 (TOUCH DISPLAY → クリックで点灯)。 文字と覆いは実行時に液晶の中身として出す (液晶の効果が掛かる) ---
        var boot = cgo.AddComponent<Game2DBoot>();
        boot.lcd = quadGo != null ? quadGo.GetComponent<UI.Lcd.LcdScreen>() : null;
        boot.lcdSurface = quadGo != null ? quadGo.GetComponent<MeshRenderer>() : null;
        boot.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/TextMesh Pro/Fonts/mihiPixelmoji_v1.asset");

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
        var exRt = Node("NavExtras", hudRoot, 0, 0, Game2DLayout.Width, Game2DLayout.Height);
        exRt.anchorMin = Vector2.zero; exRt.anchorMax = Vector2.one; exRt.offsetMin = exRt.offsetMax = Vector2.zero;
        var extras = exRt.gameObject.AddComponent<Game2DNavExtras>(); extras.modal = modal; extras.Build();

        // --- 配線 ---
        // Hud は常に生きている Grid に付ける (hudRoot は Hud 自身が出し入れするので、 そこに付けると一度消えたら戻れない)
        var hud = gridRt.gameObject.AddComponent<Game2DHud>();
        hud.gold = goldPot; hud.radar = radar; hud.resBar = resBar; hud.panel = navPanel;
        hud.navOnly = navOnly.gameObject; hud.battleOnly = battleOnly.gameObject; hud.battleHud = battleHud; hud.battleSim = battleSim; hud.extras = extras; hud.root = hudRoot.gameObject;

        // --- 液晶の Quad: 残像・湾曲・横線ノイズ・画素の格子・にじみ (Game2D/LcdScreen)。 旧 Lcd/Scanline は使わない ---
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
        screen.screenCamera = cam; screen.lcdQuad = quad; screen.canvas = canvas; screen.gridRoot = presentGrid; screen.fxMaterial = fxMat; mapView.screen = screen; navPanel.screen = screen; battleHud.screen = screen; modal.screen = screen; extras.screen = screen;
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

    /// <summary>テクスチャを (0,0) に等倍で置く RawImage (当たり判定は使わず、 部品が画素で判定する)。</summary>
    private static RawImage FullRaw(string name, Transform parent, Texture2D tex, int w, int h)
    {
        var rt = Node(name, parent, 0, 0, w, h);
        var ri = rt.gameObject.AddComponent<RawImage>(); ri.texture = tex; ri.raycastTarget = false;
        return ri;
    }

    /// <summary>当たり判定 (alpha) に使うので、 スプライトの読み出しを許可する。</summary>
    private static void EnsureReadable(params string[] names)
    {
        foreach (var n in names)
        {
            var imp = AssetImporter.GetAtPath(Dir + n + ".png") as TextureImporter;
            if (imp != null && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
        }
    }
}

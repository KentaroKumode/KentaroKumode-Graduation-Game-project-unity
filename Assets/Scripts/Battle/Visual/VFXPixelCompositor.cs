using UnityEngine;
using UnityEngine.Rendering;

namespace Battle.Visual
{
    /// <summary>
    /// VFX レイヤーだけを低解像度 RT に焼き、 α閾値とカラー量子化を通して
    /// メインカメラ出力に合成する VFX 専用ドット化フィルタ。
    ///
    /// 全画面ピクセル化 ([PixelPostFilter]) との違い:
    ///   ・床/キャラ/HUD は素のまま (= モザイクしない)
    ///   ・α 閾値で柔らかいフェードを 1bit に潰す (= ドットの輪郭がカリッと立つ)
    ///   ・色を N 段階にスナップしてパレット風にできる
    ///
    /// 仕組み:
    ///   1. メインカメラから VFX レイヤーを除外
    ///   2. 子に同じ画角の VFX カメラを生成し、 VFX レイヤーだけ低解像度 RT へ描画
    ///   3. メインカメラの OnRenderImage で合成シェーダーを通して上乗せ
    ///   4. VFX 発火側 (CombatVFXTester) は Spawn 後に GameObject の layer を [vfxLayer] に設定する
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class VFXPixelCompositor : MonoBehaviour
    {
        public static VFXPixelCompositor Instance { get; private set; }

        [Header("ドット化")]
        [Tooltip("ON で有効化。 OFF でメインカメラ素通し。")]
        public bool enableCompositor = true;
        [Tooltip("VFX 用 RT の縦解像度。 横はアスペクト比で決まる。 値を下げるほどドット荒くなる。")]
        [Range(60, 720)] public int vfxPixelHeight = 180;

        [Header("合成モード")]
        [Tooltip("Additive = m + v.rgb (加算系 VFX 向け・α 蓄積回避だが Ink Slash は見えない)。 " +
                 "AlphaBlend = v.rgb + m * (1-v.a) (Ink Slash 等の黒系正しく出るが加算系で黒ハロ)。 " +
                 "Auto = 輝度で per-pixel 自動切替 (明暗両 VFX 共存時の推奨)。")]
        public CompositeMode compositeMode = CompositeMode.Auto;

        [Header("Auto モード パラメータ")]
        [Tooltip("Auto: この輝度を境に additive ↔ alpha-blend を per-pixel ブレンド")]
        [Range(0.0f, 1.0f)] public float autoSplit = 0.18f;
        [Tooltip("Auto: 切替境界の幅 (smoothstep)。 大きいとなだらかに混じる")]
        [Range(0.01f, 0.5f)] public float autoSplitWidth = 0.18f;

        [Header("グロー (加算層のみ・ Ink には適用されない)")]
        [Tooltip("グローの強度。 0 で完全 OFF。")]
        [Range(0f, 2f)] public float glowStrength = 0.6f;
        [Tooltip("ボケ半径 (RT テクセル単位)。 1.5 ≒ 軽い 9-tap、 3〜4 でしっかりハロ。")]
        [Range(0f, 6f)] public float glowRadius = 1.5f;
        [Tooltip("これ未満の輝度はグロー源から除外 (背景うっすら発光を防ぐ)。")]
        [Range(0f, 0.5f)] public float glowThreshold = 0.05f;

        public enum CompositeMode { Additive, AlphaBlend, Auto }

        [Header("エッジ処理")]
        [Tooltip("Soft = アルファをそのまま使う (柔らかい縁を保持)。 Hard = 閾値で 0/1 に潰す (1bit エッジ・カリッとドット絵)。 Smooth = 閾値付近を smoothstep でなだらかに。")]
        public EdgeMode edgeMode = EdgeMode.Soft;
        [Tooltip("Hard / Smooth 時の閾値。 α または輝度がこの値未満ならカット。")]
        [Range(0.01f, 0.9f)] public float alphaThreshold = 0.15f;
        [Tooltip("Smooth 時の遷移幅 (閾値±この値で smoothstep)。")]
        [Range(0.01f, 0.5f)] public float smoothWidth = 0.1f;

        public enum EdgeMode { Soft, Smooth, Hard }

        // ホットキー [I] は EdgeMode を Soft → Smooth → Hard → Soft で循環
        // 内部互換用に保持。 外部から enableAlphaThreshold を触る古いコードは Hard 相当。
        public bool enableAlphaThreshold
        {
            get => edgeMode == EdgeMode.Hard;
            set => edgeMode = value ? EdgeMode.Hard : EdgeMode.Soft;
        }

        [Header("カラー量子化")]
        public bool enableQuantize;
        [Range(2, 32)] public int colorLevels = 8;

        [Header("レイヤー")]
        [Tooltip("加算系 VFX 用のレイヤー番号 (0-31)。 デフォルト 31 (User Layer 31)。")]
        [Range(8, 31)] public int vfxLayer = 31;
        [Tooltip("Ink (アルファブレンド・黒系) VFX 用のレイヤー番号。 加算用と別にすることで黒い斬撃を別 RT に焼く。")]
        [Range(8, 31)] public int vfxInkLayer = 30;

        [Header("FPS ロック (ドット絵らしい コマ送り感)")]
        [Tooltip("ON でターゲット FPS を強制。 OFF だと素 (通常 60+)。")]
        public bool enableFpsLock;
        [Tooltip("ターゲット FPS。 8 = 紙芝居感、 15 = NES風、 24 = アニメ風、 30 = 標準ピクセルゲーム風。")]
        [Range(4, 120)] public int targetFps = 15;

        [Header("ホットキー")]
        public KeyCode toggleKey = KeyCode.P;
        public KeyCode quantizeToggleKey = KeyCode.O;
        public KeyCode thresholdToggleKey = KeyCode.I;
        [Tooltip("FPSロックをトグル + プリセット (8→15→24→30→60→OFF) を循環。")]
        public KeyCode fpsCycleKey = KeyCode.F;
        [Tooltip("VFX RT のリアルタイム内容を画面右上に表示するデバッグオーバーレイをトグル。")]
        public KeyCode debugOverlayKey = KeyCode.U;
        [Tooltip("カメラ設定の現在値を Console にダンプ。")]
        public KeyCode debugLogKey = KeyCode.Y;
        [Tooltip("Composite Mode を Additive ⟷ AlphaBlend で切替。")]
        public KeyCode compositeModeKey = KeyCode.M;
        [Tooltip("Alpha 正規化 (Replacement Shader) のトグル。 ON で additive 素材の黒い四角を解消。")]
        public KeyCode alphaFromLumKey = KeyCode.K;

        [Header("Debug")]
        [Tooltip("VFX RT の内容を画面右上に重ねて表示する。 マゼンタ背景の上に描画されるので、 透過部分はマゼンタが透ける。")]
        public bool showRTOverlay = false;

        private Camera _mainCam;
        private Camera _vfxCam;
        private Camera _inkCam;
        private RenderTexture _vfxRT;
        private RenderTexture _inkRT;
        private Material _compositeMat;
        private Shader _compositeShader;
        private Material _clearMat;
        private Shader _clearShader;
        private Shader _vfxReplacementShader;
        private int _appliedCullMask;

        [Header("Alpha 正規化 (Replacement Shader)")]
        [Tooltip("ON で VFX カメラに replacement shader を適用し、 RT の α を 輝度ベースに正規化。 " +
                 "additive 素材の空白部 (RGB=0,α=1) が背景を黒く塗りつぶす問題を解消する。 " +
                 "黒い alpha-blend 系 (Ink Slash 等) は見えなくなるトレードオフあり。")]
        public bool useAlphaFromLuminance = true;

        public int VfxLayer => vfxLayer;
        public int VfxInkLayer => vfxInkLayer;

        private void Awake()
        {
            Instance = this;
            _mainCam = GetComponent<Camera>();
            ApplyCullingToMain();
            CreateOrUpdateVfxCamera();
        }

        private void OnEnable()
        {
            Instance = this;
            ApplyCullingToMain();
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            // メインカメラの cullingMask を復元
            if (_mainCam != null) _mainCam.cullingMask |= (1 << vfxLayer) | (1 << vfxInkLayer);
        }

        private void ApplyCullingToMain()
        {
            if (_mainCam == null) return;
            _mainCam.cullingMask &= ~((1 << vfxLayer) | (1 << vfxInkLayer));
            _appliedCullMask = _mainCam.cullingMask;
        }

        private void CreateOrUpdateVfxCamera()
        {
            if (_mainCam == null) return;
            if (_vfxCam == null)
            {
                var go = new GameObject("VFXCamera_Pixelized");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                _vfxCam = go.AddComponent<Camera>();
                // AudioListener が二重にならないように削除
                var al = go.GetComponent<AudioListener>();
                if (al != null) Destroy(al);
                // α クリアは VFXPixelCompositor.Update の ClearVfxRT() (Graphics.Blit) で行うため、
                // 旧 TransparentRTClearer は不要 (むしろ干渉の原因になる)
            }
            // 既存シーンで残っている TransparentRTClearer を除去 (二重クリア防止)
            var stale = _vfxCam != null ? _vfxCam.GetComponent<TransparentRTClearer>() : null;
            if (stale != null)
            {
                if (Application.isPlaying) Destroy(stale); else DestroyImmediate(stale);
            }
            _vfxCam.CopyFrom(_mainCam);
            _vfxCam.cullingMask = 1 << vfxLayer;
            // clearFlags = Nothing。 内蔵クリアを無効化し、 TransparentRTClearer が
            // OnPreRender で RGBA すべてを 0 にクリアする。
            _vfxCam.clearFlags = CameraClearFlags.Nothing;
            _vfxCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _vfxCam.depth = _mainCam.depth - 1f;
            _vfxCam.allowHDR = false;
            _vfxCam.allowMSAA = false;
            ApplyReplacementShader();
            SyncTargetTexture();

            CreateOrUpdateInkCamera();
        }

        private void CreateOrUpdateInkCamera()
        {
            if (_mainCam == null) return;
            if (_inkCam == null)
            {
                var go = new GameObject("VFXCamera_Ink");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                _inkCam = go.AddComponent<Camera>();
                var al = go.GetComponent<AudioListener>();
                if (al != null) Destroy(al);
            }
            _inkCam.CopyFrom(_mainCam);
            _inkCam.cullingMask = 1 << vfxInkLayer;
            _inkCam.clearFlags = CameraClearFlags.Nothing;
            _inkCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _inkCam.depth = _mainCam.depth - 0.5f; // _vfxCam (-1) より後、 _mainCam より前
            _inkCam.allowHDR = false;
            _inkCam.allowMSAA = false;
            // ink カメラは replacement shader を使わない (素のアルファブレンドで描画)
            _inkCam.ResetReplacementShader();
            if (_inkRT != null && _inkCam.targetTexture != _inkRT)
                _inkCam.targetTexture = _inkRT;
        }

        private void ApplyReplacementShader()
        {
            if (_vfxCam == null) return;
            if (useAlphaFromLuminance)
            {
                if (_vfxReplacementShader == null)
                    _vfxReplacementShader = Shader.Find("Hidden/Battle/VFXAlphaFromLuminance");
                if (_vfxReplacementShader != null)
                    _vfxCam.SetReplacementShader(_vfxReplacementShader, "RenderType");
                else
                    Debug.LogWarning("[VFXPixelCompositor] VFXAlphaFromLuminance shader が見つかりません。");
            }
            else
            {
                _vfxCam.ResetReplacementShader();
            }
        }

        private void SyncTargetTexture()
        {
            if (_vfxCam != null && _vfxRT != null && _vfxCam.targetTexture != _vfxRT)
                _vfxCam.targetTexture = _vfxRT;
        }

        private void EnsureRenderTexture()
        {
            if (_mainCam == null) return;
            int srcH = Mathf.Max(16, _mainCam.pixelHeight);
            int srcW = Mathf.Max(16, _mainCam.pixelWidth);
            int targetH = Mathf.Clamp(vfxPixelHeight, 16, srcH);
            int targetW = Mathf.Max(16, Mathf.RoundToInt(targetH * (float)srcW / srcH));

            if (_vfxRT == null || _vfxRT.height != targetH || _vfxRT.width != targetW)
            {
                if (_vfxRT != null)
                {
                    // Camera の参照を先に外してから Release / Destroy
                    if (_vfxCam != null && _vfxCam.targetTexture == _vfxRT) _vfxCam.targetTexture = null;
                    _vfxRT.Release();
                    if (Application.isPlaying) Destroy(_vfxRT); else DestroyImmediate(_vfxRT);
                }
                _vfxRT = new RenderTexture(targetW, targetH, 0, RenderTextureFormat.ARGB32);
                _vfxRT.filterMode = FilterMode.Point;
                _vfxRT.wrapMode = TextureWrapMode.Clamp;
                _vfxRT.useMipMap = false;
                _vfxRT.autoGenerateMips = false;
                _vfxRT.Create();
                if (_vfxCam != null) _vfxCam.targetTexture = _vfxRT;
            }

            // Ink RT (同サイズ)
            if (_inkRT == null || _inkRT.height != targetH || _inkRT.width != targetW)
            {
                if (_inkRT != null)
                {
                    if (_inkCam != null && _inkCam.targetTexture == _inkRT) _inkCam.targetTexture = null;
                    _inkRT.Release();
                    if (Application.isPlaying) Destroy(_inkRT); else DestroyImmediate(_inkRT);
                }
                _inkRT = new RenderTexture(targetW, targetH, 0, RenderTextureFormat.ARGB32);
                _inkRT.filterMode = FilterMode.Point;
                _inkRT.wrapMode = TextureWrapMode.Clamp;
                _inkRT.useMipMap = false;
                _inkRT.autoGenerateMips = false;
                _inkRT.Create();
                if (_inkCam != null) _inkCam.targetTexture = _inkRT;
            }
        }

        private static readonly int[] FpsPresets = { 8, 15, 24, 30, 60 };
        private int _fpsPresetIndex;

        private void Update()
        {
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
                enableCompositor = !enableCompositor;
            if (quantizeToggleKey != KeyCode.None && Input.GetKeyDown(quantizeToggleKey))
                enableQuantize = !enableQuantize;
            if (thresholdToggleKey != KeyCode.None && Input.GetKeyDown(thresholdToggleKey))
                edgeMode = (EdgeMode)(((int)edgeMode + 1) % 3);
            if (fpsCycleKey != KeyCode.None && Input.GetKeyDown(fpsCycleKey))
                CycleFpsPreset();
            if (debugOverlayKey != KeyCode.None && Input.GetKeyDown(debugOverlayKey))
                showRTOverlay = !showRTOverlay;
            if (debugLogKey != KeyCode.None && Input.GetKeyDown(debugLogKey))
                LogCameraSetup();
            if (compositeModeKey != KeyCode.None && Input.GetKeyDown(compositeModeKey))
                compositeMode = (CompositeMode)(((int)compositeMode + 1) % 3);
            if (alphaFromLumKey != KeyCode.None && Input.GetKeyDown(alphaFromLumKey))
                useAlphaFromLuminance = !useAlphaFromLuminance;

            ApplyFpsLock();

            // enableCompositor の状態に応じてメインカメラの VFX レイヤー可視性を切替える。
            // ON: VFX レイヤーを除外 (VFX カメラ経由でピクセル化合成)
            // OFF: VFX レイヤーを再表示 (素のパーティクル描画 = 通常再生と同等。 P 比較用)
            if (_mainCam != null)
            {
                int bits = (1 << vfxLayer) | (1 << vfxInkLayer);
                if (enableCompositor)
                {
                    if ((_mainCam.cullingMask & bits) != 0) _mainCam.cullingMask &= ~bits;
                }
                else
                {
                    if ((_mainCam.cullingMask & bits) != bits) _mainCam.cullingMask |= bits;
                }
            }

            // 順序重要: RT を先に確保 → カメラ設定 (CopyFrom の後で targetTexture が再アサインされる)
            EnsureRenderTexture();
            CreateOrUpdateVfxCamera();
            SyncTargetTexture();

            // Update タイミングで RT を完全透明 (RGBA=0) で強制クリア。
            ClearRT(_vfxRT);
            ClearRT(_inkRT);

            if (_vfxCam != null) _vfxCam.enabled = enableCompositor;
            if (_inkCam != null) _inkCam.enabled = enableCompositor;
        }

        private bool EnsureMaterial()
        {
            if (_compositeMat != null) return true;
            if (_compositeShader == null) _compositeShader = Shader.Find("Hidden/Battle/VFXComposite");
            if (_compositeShader == null) return false;
            _compositeMat = new Material(_compositeShader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        private bool EnsureClearMaterial()
        {
            if (_clearMat != null) return true;
            if (_clearShader == null) _clearShader = Shader.Find("Hidden/Battle/ClearTransparent");
            if (_clearShader == null) return false;
            _clearMat = new Material(_clearShader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        private void ClearVfxRT() => ClearRT(_vfxRT);

        private void ClearRT(RenderTexture rt)
        {
            if (rt == null) return;
            if (!EnsureClearMaterial()) return;
            var prevActive = RenderTexture.active;
            Graphics.Blit(Texture2D.whiteTexture, rt, _clearMat);
            RenderTexture.active = prevActive;
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (!enableCompositor || _vfxRT == null || !EnsureMaterial())
            {
                Graphics.Blit(src, dst);
                return;
            }

            _compositeMat.SetTexture("_VfxTex", _vfxRT);
            // Ink RT (アルファブレンド系を素のまま焼いた第二 RT)
            if (_inkRT != null)
            {
                _compositeMat.SetTexture("_InkTex", _inkRT);
                _compositeMat.SetFloat("_HasInk", 1f);
            }
            else
            {
                _compositeMat.SetFloat("_HasInk", 0f);
            }
            // _CompositeMode: 0=Additive, 1=AlphaBlend, 2=Auto
            _compositeMat.SetFloat("_CompositeMode", (float)compositeMode);
            _compositeMat.SetFloat("_AutoSplit", autoSplit);
            _compositeMat.SetFloat("_AutoSplitWidth", autoSplitWidth);
            _compositeMat.SetFloat("_GlowStrength", glowStrength);
            _compositeMat.SetFloat("_GlowRadius", glowRadius);
            _compositeMat.SetFloat("_GlowThreshold", glowThreshold);
            // _EdgeMode: 0=Soft, 1=Smooth, 2=Hard
            _compositeMat.SetFloat("_EdgeMode", (float)edgeMode);
            _compositeMat.SetFloat("_Threshold", alphaThreshold);
            _compositeMat.SetFloat("_SmoothWidth", smoothWidth);
            _compositeMat.SetFloat("_DoQuantize", enableQuantize ? 1f : 0f);
            _compositeMat.SetFloat("_Levels", Mathf.Max(2, colorLevels));
            Graphics.Blit(src, dst, _compositeMat);
        }

        private void OnDestroy()
        {
            // 1. RT を カメラの targetTexture から外す
            if (_vfxCam != null) _vfxCam.targetTexture = null;
            if (_inkCam != null) _inkCam.targetTexture = null;

            // 2. RT を Release / Destroy
            if (_vfxRT != null)
            {
                _vfxRT.Release();
                if (Application.isPlaying) Destroy(_vfxRT); else DestroyImmediate(_vfxRT);
                _vfxRT = null;
            }
            if (_inkRT != null)
            {
                _inkRT.Release();
                if (Application.isPlaying) Destroy(_inkRT); else DestroyImmediate(_inkRT);
                _inkRT = null;
            }

            // 3. カメラ GameObject ごと破棄
            if (_vfxCam != null)
            {
                var go = _vfxCam.gameObject;
                _vfxCam = null;
                if (go != null) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
            }
            if (_inkCam != null)
            {
                var go = _inkCam.gameObject;
                _inkCam = null;
                if (go != null) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
            }

            // 4. メインカメラの cullingMask を元に戻す
            if (_mainCam != null) _mainCam.cullingMask |= (1 << vfxLayer) | (1 << vfxInkLayer);

            // 5. マテリアル破棄
            if (_compositeMat != null)
            {
                if (Application.isPlaying) Destroy(_compositeMat); else DestroyImmediate(_compositeMat);
                _compositeMat = null;
            }
            if (_clearMat != null)
            {
                if (Application.isPlaying) Destroy(_clearMat); else DestroyImmediate(_clearMat);
                _clearMat = null;
            }

            if (Instance == this) Instance = null;
        }

        private void CycleFpsPreset()
        {
            // OFF → 8 → 15 → 24 → 30 → 60 → OFF ...
            if (!enableFpsLock)
            {
                enableFpsLock = true;
                _fpsPresetIndex = 0;
            }
            else
            {
                _fpsPresetIndex++;
                if (_fpsPresetIndex >= FpsPresets.Length)
                {
                    enableFpsLock = false;
                    _fpsPresetIndex = 0;
                }
            }
            if (enableFpsLock) targetFps = FpsPresets[_fpsPresetIndex];
        }

        private void ApplyFpsLock()
        {
            if (enableFpsLock)
            {
                // targetFrameRate を効かせるには vSync を切る必要がある
                if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
                int desired = Mathf.Clamp(targetFps, 1, 240);
                if (Application.targetFrameRate != desired) Application.targetFrameRate = desired;
            }
            else
            {
                // OFF 時は制限解除 (-1 = プラットフォーム既定)
                if (Application.targetFrameRate != -1) Application.targetFrameRate = -1;
            }
        }

        private void DrawRTOverlay()
        {
            if (!showRTOverlay || _vfxRT == null) return;
            float w = 320f;
            float h = w * _vfxRT.height / Mathf.Max(1, _vfxRT.width);
            var rect = new Rect(Screen.width - w - 10, 30, w, h);

            // マゼンタ背景: 透過部分はこの色が透ける
            GUI.color = new Color(1f, 0f, 1f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, false);

            // 上に VFX RT を描画 (alphaBlend = true)
            GUI.color = Color.white;
            GUI.DrawTexture(rect, _vfxRT, ScaleMode.StretchToFill, true);

            // ラベル
            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            labelStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(rect.x, rect.y - 16, w, 16),
                $"VFX RT ({_vfxRT.width}x{_vfxRT.height}) — マゼンタ=透過、 不透明部分=VFX", labelStyle);
        }

        [ContextMenu("Log Camera Setup")]
        public void LogCameraSetup()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("===== VFXPixelCompositor Camera Setup =====");
            if (_mainCam != null)
            {
                sb.AppendLine($"[Main] name={_mainCam.name}, cullingMask=0x{_mainCam.cullingMask:X8}, " +
                              $"layer31_culled={((_mainCam.cullingMask & (1 << vfxLayer)) == 0)}, depth={_mainCam.depth}, " +
                              $"clearFlags={_mainCam.clearFlags}, targetTexture={_mainCam.targetTexture}");
            }
            else sb.AppendLine("[Main] null");

            if (_vfxCam != null)
            {
                bool layerOnly = _vfxCam.cullingMask == (1 << vfxLayer);
                sb.AppendLine($"[VFX]  name={_vfxCam.name}, cullingMask=0x{_vfxCam.cullingMask:X8}, " +
                              $"only_layer{vfxLayer}={layerOnly}, depth={_vfxCam.depth}, " +
                              $"clearFlags={_vfxCam.clearFlags}, targetTexture={(_vfxCam.targetTexture != null ? _vfxCam.targetTexture.name : "NULL")}, " +
                              $"bgColor={_vfxCam.backgroundColor}, enabled={_vfxCam.enabled}");
                sb.AppendLine($"       TransparentRTClearer attached: {_vfxCam.GetComponent<TransparentRTClearer>() != null}");
            }
            else sb.AppendLine("[VFX]  null");

            if (_vfxRT != null)
                sb.AppendLine($"[RT]   size={_vfxRT.width}x{_vfxRT.height}, format={_vfxRT.format}, filterMode={_vfxRT.filterMode}");
            else sb.AppendLine("[RT]   null");

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// VFX カメラに付け、 OnPreRender 直前で α 含め RGBA を 0 にクリアする。
        /// 明示的に RenderTexture.active を切替えることで GL.Clear が正しい RT に効くようにする。
        /// </summary>
        private class TransparentRTClearer : MonoBehaviour
        {
            private Camera _cam;
            private void Awake() { _cam = GetComponent<Camera>(); }
            private void OnPreRender()
            {
                if (_cam == null) _cam = GetComponent<Camera>();
                if (_cam == null) return;
                var rt = _cam.targetTexture;
                if (rt == null) return;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
                RenderTexture.active = prev;
            }
        }

        /// <summary>加算系 VFX レイヤーへ配置 (replacement shader 経由でドット化合成)。</summary>
        public static void AssignLayerRecursive(GameObject root)
        {
            if (Instance == null || root == null) return;
            SetLayerRecursive(root, Instance.vfxLayer);
        }

        /// <summary>Ink (アルファブレンド・黒系) VFX レイヤーへ配置 (素のまま第二 RT に焼く)。</summary>
        public static void AssignInkLayerRecursive(GameObject root)
        {
            if (Instance == null || root == null) return;
            SetLayerRecursive(root, Instance.vfxInkLayer);
        }

        /// <summary>
        /// プレハブ階層内の Renderer ごとにシェーダーを判定し、
        /// additive 系 → vfxLayer、 alpha-blend 系 → vfxInkLayer に振り分ける。
        /// Ink Slash 系のように 一つのプレハブに二種類のブレンドが混在する場合に使用。
        /// </summary>
        public static void AssignSmartLayerRecursive(GameObject root)
        {
            if (Instance == null || root == null) return;
            int additiveLayer = Instance.vfxLayer;
            int inkLayer = Instance.vfxInkLayer;
            // ベースは additive レイヤー (Renderer を持たない空 GO はこちらに)
            SetLayerRecursive(root, additiveLayer);
            // Renderer 単位で再振り分け
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                bool isAlphaBlend = MaterialIsAlphaBlend(r.sharedMaterial);
                r.gameObject.layer = isAlphaBlend ? inkLayer : additiveLayer;
            }
        }

        private static bool MaterialIsAlphaBlend(Material mat)
        {
            if (mat == null || mat.shader == null) return false;
            string n = mat.shader.name.ToLowerInvariant();
            // Eric の命名規則: "Eric/BuiltIn_AdditiveFlow" / "Eric/BuiltIn_AlphaBlendFlow"
            if (n.Contains("additive")) return false;
            if (n.Contains("alphablend")) return true;
            if (n.Contains("alpha blended")) return true;
            // 汎用 Unity 命名
            if (n.Contains("particles/standard unlit") || n.Contains("particles/standard surface"))
            {
                // _DstBlend (UnityEngine.Rendering.BlendMode): 1=One(additive), 10=OneMinusSrcAlpha
                if (mat.HasProperty("_DstBlend"))
                {
                    int dst = (int)mat.GetFloat("_DstBlend");
                    if (dst == (int)UnityEngine.Rendering.BlendMode.One) return false;
                    if (dst == (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha) return true;
                }
            }
            // 不明なら additive 扱い (RT 側は α=輝度に正規化されるので安全側)
            return false;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i).gameObject, layer);
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            var rect = new Rect(10, Screen.height - 24, 1200, 22);
            string msg = $"VFXPixel: {(enableCompositor ? $"ON ({vfxPixelHeight}px)" : "OFF")}  " +
                         $"Composite: {compositeMode}  " +
                         $"AlphaFromLum: {(useAlphaFromLuminance ? "ON" : "OFF")}  " +
                         $"Edge: {edgeMode}{(edgeMode != EdgeMode.Soft ? $" ({alphaThreshold:F2})" : "")}  " +
                         $"Quantize: {(enableQuantize ? $"ON (Lv {colorLevels})" : "OFF")}  " +
                         $"FPS: {(enableFpsLock ? $"{targetFps}" : "free")}  " +
                         $"[P] enable [M] mode [K] alpha-lum [I] edge [O] quant [F] fps [U] rt [Y] log";
            GUI.Label(rect, msg, style);

            DrawRTOverlay();
        }
    }
}

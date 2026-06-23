using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Codex;

namespace UI.Codex
{
    /// <summary>
    /// 読み物コーデックスの閲覧画面（2階層レイアウト）。LCD コンテンツ内に常時 Browse モードで表示。
    ///
    /// レイアウト:
    ///   ・上部: ENDING 0X / 章タイトル (カテゴリ見出し)
    ///   ・左: そのカテゴリ配下のヴィネット一覧（クリック選択）
    ///   ・右: 選択中ヴィネットの本文
    ///   ・左上: 戻るボタン
    ///
    /// カテゴリ分け（4種）:
    ///   ① 第五層 (end1: 5層本ボス・英雄の帰還)
    ///   ② 第五層裏 (end2: 5裏・無明の剣)
    ///   ③ 第六層 (end3: 6ボス・空の玉座)
    ///   ④ 終層 (end4 最後の見出し + end5 シグナル・リターン + capstone)
    ///
    /// ナビ: 数字キー1-4 でカテゴリ切替、 矢印UP/DOWN でヴィネット切替、 ホイールで本文スクロール、 ESC で閉じる
    /// </summary>
    [DisallowMultipleComponent]
    public class CodexView : MonoBehaviour
    {
        [Header("開閉")]
        public GameObject menuToHide;
        public SpriteButton backButton;
        public SpriteButton galleryButton;

        [Header("配線")]
        public TMP_FontAsset font;
        public TextMeshPro templateTmp;
        public Camera contentCamera;
        public string sortingLayer = "Default";
        public int sortingOrder = 200;

        [Header("レイアウト(world)")]
        public float vignetteTitleSize = 6f;
        public float bodyTitleSize = 10f;
        public float bodySize = 5f;
        public float lineHeightVignette = 1.4f;
        [Tooltip("本文タイトル/本文の X 開始位置 (左寄せ)")]
        public float bodyStartX = -5f;
        public float bodyWidth = 18f;
        public float bodyHeight = 14f;
        public float scrollStep = 1.5f;
        [Tooltip("ホバー判定: マウスX < この値 で左列扱い (左列スクロール)、 以上で本文扱い (本文スクロール)")]
        public float leftZoneMaxX = -6f;
        [Tooltip("左列の見える行数")]
        public int leftVisibleCount = 14;
        [Tooltip("左列の最上段 Y 位置 (LCDオーバーフロー回避のため低めに)")]
        public float leftListTopY = 6f;
        [Tooltip("左列の X 位置 (LCD左端から余白を取る)")]
        public float leftListX = -10f;

        [Header("BackButton自前配置")]
        public Vector2 backButtonLocalPos = new Vector2(-13f, 7.5f);
        public Vector2 backButtonHitMinSize = new Vector2(3f, 2.5f);
        public Vector2 backButtonHitScale = new Vector2(3f, 3f);
        public bool backButtonShowHitArea = false;
        public Color backButtonHitAreaColor = new Color(0f, 1f, 0f, 0.12f);

        [Header("文字間隔・行間隔 (TMP単位)")]
        [Tooltip("ヘッダー(ENDING番号/章タイトル)の文字間隔")]
        public float headerCharSpacing = 0f;
        [Tooltip("左列ヴィネットの文字間隔")]
        public float vignetteCharSpacing = 0f;
        [Tooltip("左列ヴィネットの行間隔")]
        public float vignetteLineSpacing = 0f;
        [Tooltip("右本文タイトルの文字間隔")]
        public float bodyTitleCharSpacing = 0f;
        [Tooltip("本文の文字間隔")]
        public float bodyCharSpacing = 0f;
        [Tooltip("本文の行間隔")]
        public float bodyLineSpacing = 0f;

        [Header("背景")]
        [Tooltip("背景スプライト（名前 Library 推奨。 未指定なら Resources から 'Library' を探索）")]
        public Sprite librarySprite;
        [Tooltip("グラデーション上端の色（通常は透明）")]
        public Color gradientTopColor = new Color(0f, 0f, 0f, 0f);
        [Tooltip("グラデーション下端の色（通常は黒）")]
        public Color gradientBottomColor = new Color(0f, 0f, 0f, 1f);
        [Tooltip("グラデーション解像度（縦ピクセル数）")]
        public int gradientResolution = 128;

        [Header("デバッグ")]
        public bool debugUnlockAll = true;

        // === 内部 ===
        private enum Mode { Closed, Browse }
        private Mode _mode = Mode.Closed;

        private int _selectedVignette;
        private int _listScroll;

        private IReadOnlyList<WorldVignettes.Vignette> Entries => WorldVignettes.All;

        private GameObject _root;
        private readonly List<TextMeshPro> _leftListTmps = new List<TextMeshPro>();
        private TextMeshPro _bodyTitleTmp;
        private TextMeshPro _bodyTmp;
        private float _bodyYOffset;

        private SpriteButton _ownBackButton;
        private bool _backPressArmed;
        private bool _wired;

        private void Awake()
        {
            if (contentCamera == null)
            {
                var lcd = FindObjectOfType<UI.Lcd.LcdScreen>();
                if (lcd != null) contentCamera = lcd.contentCamera;
            }
            WireButtons();
            _mode = Mode.Closed;
        }

        private void OnEnable() => WireButtons();
        private void OnDisable() => UnwireButtons();

        private void WireButtons()
        {
            if (_wired) return;
            if (galleryButton != null) galleryButton.Clicked += HandleGalleryClick;
            _wired = true;
        }

        private void UnwireButtons()
        {
            if (!_wired) return;
            if (galleryButton != null) galleryButton.Clicked -= HandleGalleryClick;
            _wired = false;
        }

        private void HandleGalleryClick() { if (_mode == Mode.Closed) Open(); }

        // ----------------------------------------------------------------
        //  Open / Close
        // ----------------------------------------------------------------

        public void Open()
        {
            if (contentCamera == null) return;
            if (menuToHide != null) menuToHide.SetActive(false);
            EnsureOwnBackButton();
            if (_ownBackButton != null) _ownBackButton.gameObject.SetActive(true);
            EnsureRoot();
            _root.SetActive(true);
            _selectedVignette = 0;
            _listScroll = 0;
            _bodyYOffset = 0f;
            RefreshAll();
            _mode = Mode.Browse;
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
            if (_ownBackButton != null) _ownBackButton.gameObject.SetActive(false);
            if (menuToHide != null) menuToHide.SetActive(true);
            _mode = Mode.Closed;
        }

        // ----------------------------------------------------------------
        //  Build
        // ----------------------------------------------------------------

        private void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("CodexBrowse");
            _root.transform.SetParent(transform, false);
            _root.transform.localPosition = Vector3.zero;
            _root.layer = ResolveContentLayer();

            BuildBackground();

            // 左列ヴィネット一覧 (leftVisibleCount 個プール、 scrollable)
            float topY = leftListTopY;
            int pool = Mathf.Max(1, leftVisibleCount);
            for (int i = 0; i < pool; i++)
            {
                var tmp = CreateTmp(_root, $"Vignette{i}", new Vector3(leftListX, topY - i * lineHeightVignette, 0f), vignetteTitleSize, TextAlignmentOptions.MidlineLeft);
                tmp.rectTransform.pivot = new Vector2(0f, 0.5f); // 座標 = 左端起点
                tmp.rectTransform.sizeDelta = new Vector2(12f, lineHeightVignette);
                _leftListTmps.Add(tmp);

                var btnGo = new GameObject($"VBtn{i}");
                btnGo.transform.SetParent(_root.transform, false);
                btnGo.transform.localPosition = new Vector3(leftListX + 4f, topY - i * lineHeightVignette, 0.01f);
                btnGo.layer = ResolveContentLayer();
                var col = btnGo.AddComponent<BoxCollider2D>();
                col.size = new Vector2(10f, lineHeightVignette * 0.9f);
                var btn = btnGo.AddComponent<SpriteButton>();
                btn.selfRaycast = false;
                btn.pressBrightness = 0f;
                int idx = i;
                btn.Clicked += () => OnVignetteClicked(idx);
            }

            // 右側: 本文タイトル + 本文 (bodyStartX = 左端起点)
            _bodyTitleTmp = CreateTmp(_root, "BodyTitle", new Vector3(bodyStartX, 6.5f, 0f), bodyTitleSize, TextAlignmentOptions.MidlineLeft);
            _bodyTitleTmp.rectTransform.pivot = new Vector2(0f, 0.5f);
            _bodyTitleTmp.rectTransform.sizeDelta = new Vector2(bodyWidth, 3f);

            // 本文: TopLeft alignment、 上端 y=5、 高さ bodyHeight、 pivot を (0,1) で左上起点
            float bodyTopY = 5f;
            _bodyTmp = CreateTmp(_root, "Body", new Vector3(bodyStartX, bodyTopY, 0f), bodySize, TextAlignmentOptions.TopLeft);
            _bodyTmp.rectTransform.pivot = new Vector2(0f, 1f);
            _bodyTmp.rectTransform.sizeDelta = new Vector2(bodyWidth, bodyHeight);
            _bodyTmp.enableWordWrapping = true;
            _bodyTmp.overflowMode = TextOverflowModes.Overflow;
        }

        // ----------------------------------------------------------------
        //  Refresh
        // ----------------------------------------------------------------

        private void ApplySpacing()
        {
            foreach (var t in _leftListTmps) if (t != null) { t.characterSpacing = vignetteCharSpacing; t.lineSpacing = vignetteLineSpacing; }
            if (_bodyTitleTmp != null) _bodyTitleTmp.characterSpacing = bodyTitleCharSpacing;
            if (_bodyTmp != null) { _bodyTmp.characterSpacing = bodyCharSpacing; _bodyTmp.lineSpacing = bodyLineSpacing; }
        }

        private void RefreshAll()
        {
            ApplySpacing();
            int total = Entries.Count;
            if (_selectedVignette >= total) _selectedVignette = 0;

            // 左列
            int maxScroll = Mathf.Max(0, total - _leftListTmps.Count);
            _listScroll = Mathf.Clamp(_listScroll, 0, maxScroll);
            for (int i = 0; i < _leftListTmps.Count; i++)
            {
                int srcIdx = _listScroll + i;
                if (srcIdx < 0 || srcIdx >= total)
                {
                    _leftListTmps[i].text = string.Empty;
                    continue;
                }
                var v = Entries[srcIdx];
                bool unlocked = debugUnlockAll || VignetteUnlockState.IsUnlocked(v);
                string title = unlocked ? v.title : "???";
                string prefix = (srcIdx == _selectedVignette) ? "■ " : "  ";
                _leftListTmps[i].text = prefix + title;
                _leftListTmps[i].ForceMeshUpdate();
            }

            // 右本文
            if (total == 0)
            {
                _bodyTitleTmp.text = string.Empty;
                _bodyTmp.text = string.Empty;
            }
            else
            {
                var v = Entries[_selectedVignette];
                bool unlocked = debugUnlockAll || VignetteUnlockState.IsUnlocked(v);
                _bodyTitleTmp.text = unlocked ? v.title : "???";
                _bodyTmp.text = unlocked ? v.body : "未解禁";
                ApplyBodyScroll();
            }
            _bodyTitleTmp.ForceMeshUpdate();
            _bodyTmp.ForceMeshUpdate();
        }

        private void ApplyBodyScroll()
        {
            if (_bodyTmp == null) return;
            // pivot=(0,1) なので localPosition.y = 本文上端の Y
            float bodyTopY = 5f;
            var p = _bodyTmp.transform.localPosition;
            p.y = bodyTopY + _bodyYOffset;
            _bodyTmp.transform.localPosition = p;
        }

        private void OnVignetteClicked(int rowIndex)
        {
            int srcIdx = _listScroll + rowIndex;
            if (srcIdx < 0 || srcIdx >= Entries.Count) return;
            _selectedVignette = srcIdx;
            _bodyYOffset = 0f;
            RefreshAll();
        }

        // ----------------------------------------------------------------
        //  入力
        // ----------------------------------------------------------------

        private void Update()
        {
            if (_mode == Mode.Closed) return;

            // Inspector 値の Play 中即時反映
            ApplySpacing();

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace))
            {
                Close();
                return;
            }

            // ヴィネット切替: UP/DOWN
            int total = Entries.Count;
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                _selectedVignette = Mathf.Max(0, _selectedVignette - 1);
                _bodyYOffset = 0f;
                RefreshAll();
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                _selectedVignette = Mathf.Min(total - 1, _selectedVignette + 1);
                _bodyYOffset = 0f;
                RefreshAll();
            }

            // マウスホイール: ホバー位置で切替
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.1f && TryMouseToContentWorld(out var mWorld))
            {
                bool overLeft = mWorld.x < leftZoneMaxX;
                if (overLeft)
                {
                    // 左列スクロール
                    int maxScroll = Mathf.Max(0, total - _leftListTmps.Count);
                    int newScroll = Mathf.Clamp(_listScroll + (wheel < 0 ? 1 : -1), 0, maxScroll);
                    if (newScroll != _listScroll) { _listScroll = newScroll; RefreshAll(); }
                }
                else
                {
                    // 本文スクロール
                    _bodyYOffset = Mathf.Max(0f, _bodyYOffset + (wheel < 0 ? scrollStep : -scrollStep));
                    ApplyBodyScroll();
                }
            }

            // BackButton 独自判定
            if (_ownBackButton != null && _ownBackButton.gameObject.activeInHierarchy && TryMouseToContentWorld(out var mp))
            {
                var bp = _ownBackButton.transform.position;
                var col = _ownBackButton.GetComponent<BoxCollider2D>();
                Vector2 half = col != null ? col.size * 0.5f : new Vector2(3f, 1.5f);
                bool over = Mathf.Abs(mp.x - bp.x) <= half.x && Mathf.Abs(mp.y - bp.y) <= half.y;
                _ownBackButton.SetPointerOver(over);
                if (over && Input.GetMouseButtonDown(0)) _backPressArmed = true;
                if (Input.GetMouseButtonUp(0))
                {
                    if (_backPressArmed && over) Close();
                    _backPressArmed = false;
                }
            }
        }

        // ----------------------------------------------------------------
        //  ヘルパ
        // ----------------------------------------------------------------

        private void BuildBackground()
        {
            if (contentCamera == null) return;
            float oh = contentCamera.orthographicSize * 2f;
            float ow = oh * contentCamera.aspect;

            // Library スプライト
            var spr = librarySprite;
            if (spr == null) spr = Resources.Load<Sprite>("Library");
            if (spr != null)
            {
                var bgGo = new GameObject("LibraryBg");
                bgGo.transform.SetParent(_root.transform, false);
                bgGo.transform.localPosition = new Vector3(0f, 0f, 0.8f);
                bgGo.layer = ResolveContentLayer();
                var bgSr = bgGo.AddComponent<SpriteRenderer>();
                bgSr.sprite = spr;
                bgSr.sortingOrder = sortingOrder - 20;
                if (!string.IsNullOrEmpty(sortingLayer)) bgSr.sortingLayerName = sortingLayer;
                // スプライト寸法 → ortho 視野いっぱいに拡大
                var sz = spr.bounds.size;
                if (sz.x > 0.0001f && sz.y > 0.0001f)
                {
                    float sx = ow / sz.x;
                    float sy = oh / sz.y;
                    bgGo.transform.localScale = new Vector3(sx, sy, 1f);
                }
            }

            // グラデーション overlay (上→下に黒へ)
            {
                int h = Mathf.Max(8, gradientResolution);
                var tex = new Texture2D(1, h, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[h];
                for (int y = 0; y < h; y++)
                {
                    float t = 1f - (float)y / (h - 1); // Texture2D の y=0 が下端。 下端=t1=bottom, 上端=t0=top
                    var c = Color.Lerp(gradientTopColor, gradientBottomColor, t);
                    px[y] = c;
                }
                tex.SetPixels32(px);
                tex.Apply(false, false);

                var gradGo = new GameObject("Gradient");
                gradGo.transform.SetParent(_root.transform, false);
                gradGo.transform.localPosition = new Vector3(0f, 0f, 0.6f);
                gradGo.layer = ResolveContentLayer();
                var gradSr = gradGo.AddComponent<SpriteRenderer>();
                gradSr.sprite = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), h);
                gradSr.sortingOrder = sortingOrder - 10;
                if (!string.IsNullOrEmpty(sortingLayer)) gradSr.sortingLayerName = sortingLayer;
                // 1x h(world) → ortho 視野いっぱいに拡大 (x方向)
                gradGo.transform.localScale = new Vector3(ow * h, oh, 1f);
            }
        }

        private int ResolveContentLayer()
        {
            int l = LayerMask.NameToLayer("LcdContent");
            return l >= 0 ? l : (contentCamera != null ? contentCamera.gameObject.layer : 0);
        }

        private UI.Lcd.LcdPointer _cachedLcdPointer;
        private bool TryMouseToContentWorld(out Vector3 worldPos)
        {
            worldPos = default;
            if (_cachedLcdPointer == null) _cachedLcdPointer = FindObjectOfType<UI.Lcd.LcdPointer>();
            var lp = _cachedLcdPointer;
            if (lp?.worldCamera == null || lp?.surfaceCollider == null || lp?.contentCamera == null) return false;
            Vector3 mouse = Input.mousePosition;
            if (mouse.x < 0 || mouse.y < 0 || mouse.x > Screen.width || mouse.y > Screen.height) return false;
            Ray wray = lp.worldCamera.ScreenPointToRay(mouse);
            if (!Physics.Raycast(wray, out RaycastHit hit, lp.maxDistance, lp.surfaceMask)) return false;
            if (hit.collider != lp.surfaceCollider) return false;
            Vector2 uv = hit.textureCoord;
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return false;
            var cc = lp.contentCamera;
            float ow = cc.orthographicSize * cc.aspect;
            float oh = cc.orthographicSize;
            float lx = (uv.x - 0.5f) * 2f * ow;
            float ly = (uv.y - 0.5f) * 2f * oh;
            worldPos = cc.transform.position + cc.transform.right * lx + cc.transform.up * ly;
            return true;
        }

        private void EnsureOwnBackButton()
        {
            if (_ownBackButton != null) return;
            Sprite spr = null;
            if (backButton != null)
            {
                var sr = backButton.GetComponent<SpriteRenderer>();
                if (sr != null) spr = sr.sprite;
            }
            var go = new GameObject("CodexBackButton");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(backButtonLocalPos.x, backButtonLocalPos.y, 0f);
            go.layer = ResolveContentLayer();
            var newSr = go.AddComponent<SpriteRenderer>();
            newSr.sprite = spr;
            newSr.sortingOrder = sortingOrder + 1;
            if (!string.IsNullOrEmpty(sortingLayer)) newSr.sortingLayerName = sortingLayer;

            var col = go.AddComponent<BoxCollider2D>();
            Vector2 baseSize = spr != null ? (Vector2)spr.bounds.size : new Vector2(2f, 1f);
            Vector2 scaled = new Vector2(baseSize.x * backButtonHitScale.x, baseSize.y * backButtonHitScale.y);
            col.size = new Vector2(Mathf.Max(scaled.x, backButtonHitMinSize.x), Mathf.Max(scaled.y, backButtonHitMinSize.y));

            _ownBackButton = go.AddComponent<SpriteButton>();
            _ownBackButton.selfRaycast = false;
            _ownBackButton.targetRenderer = newSr;
            _ownBackButton.pressBrightness = -0.2f;

            if (backButtonShowHitArea)
            {
                var hitGo = new GameObject("HitAreaViz");
                hitGo.transform.SetParent(go.transform, false);
                hitGo.transform.localPosition = new Vector3(0f, 0f, 0.02f);
                hitGo.layer = ResolveContentLayer();
                var hitSr = hitGo.AddComponent<SpriteRenderer>();
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white); tex.Apply();
                hitSr.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
                hitSr.color = backButtonHitAreaColor;
                hitSr.sortingOrder = sortingOrder;
                if (!string.IsNullOrEmpty(sortingLayer)) hitSr.sortingLayerName = sortingLayer;
                hitGo.transform.localScale = new Vector3(col.size.x, col.size.y, 1f);
            }
            go.SetActive(false);
        }

        private TextMeshPro CreateTmp(GameObject parent, string name, Vector3 localPos, float fontSize, TextAlignmentOptions align)
        {
            TextMeshPro tmp;
            GameObject go;
            if (templateTmp != null)
            {
                go = Instantiate(templateTmp.gameObject, parent.transform);
                go.name = name;
                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = templateTmp.transform.localScale;
                go.layer = templateTmp.gameObject.layer;
                go.SetActive(true);
                tmp = go.GetComponent<TextMeshPro>();
                // Inspector に font が指定されていれば複製後に上書き (Codex 用フォント差し替え)
                if (font != null)
                {
                    tmp.font = font;
                    if (font.material != null) tmp.fontMaterial = font.material;
                }
            }
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(parent.transform, false);
                go.transform.localPosition = localPos;
                go.layer = ResolveContentLayer();
                tmp = go.AddComponent<TextMeshPro>();
                if (font != null)
                {
                    tmp.font = font;
                    if (font.material != null) tmp.fontMaterial = font.material;
                }
                var r0 = tmp.GetComponent<Renderer>();
                r0.sortingOrder = sortingOrder;
                if (!string.IsNullOrEmpty(sortingLayer)) r0.sortingLayerName = sortingLayer;
            }
            tmp.enableAutoSizing = false;
            tmp.fontSize = fontSize;
            tmp.fontSizeMax = fontSize;
            tmp.alignment = align;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.richText = false;
            tmp.text = string.Empty;
            tmp.color = Color.white;
            return tmp;
        }
    }
}

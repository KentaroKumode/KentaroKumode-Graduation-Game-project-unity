using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace UI.ClassSelect
{
    /// <summary>
    /// Papers, Please 風 出自選択 UI (2026-07-07、 2026-07-13 職業選択から改称)。
    /// 職業は出自 (祖父母急逝後の空白の数年) の分岐 ── 正本: docs/GAME.md §11。 4 パネル構成:
    ///   ・キャラ切替タブ ×4 …… 左端から展開
    ///   ・キャラ表示票 (登録票) …… 上から落ちてくる
    ///   ・詳細説明タブ …… 右から展開
    ///   ・決定ボタン (受理スタンプ) …… 右から展開
    /// タブをクリックすると現在の票が上へ引っ込み、 新しい票が上から落ちてくる。
    /// スタンプで GameManager.SelectedClass を確定 (autoStartRun=true なら即ラン開始)。
    ///
    /// 【座標系】 すべてのレイアウト数値は「モニタードット」 単位 (物理 4×4px = 1dot、
    /// LCD 924×556 → 231×139 dot)。 dotWorldSize (既定 4/32=0.125) でワールドへ変換し、
    /// 位置は整数ドットへスナップする。 Inspector の数値をドットで直接調整できる。
    ///
    /// 【配線】 空 GameObject に本コンポーネントを付け、 LCD コンテンツカメラの写る位置に置く。
    /// スプライト/テキストはコードで自動生成 (見た目は無地パネル+文字のプレースホルダ)。
    /// ボタン入力は SpriteButton ── 画面直描きなら pointerCamera にそのカメラを、
    /// 液晶面越しなら LcdPointer 経由 (buttonsSelfRaycast=false) にする。
    /// 日本語表示のため font に日本語対応 TMP_FontAsset を割り当てること (未指定は TMP 既定)。
    /// </summary>
    public class ClassSelectView : MonoBehaviour
    {
        // ============================================================
        //  Inspector
        // ============================================================

        [Header("座標系 (モニタードット)")]
        [Tooltip("1 ドットのワールドサイズ。 既定 = 4px/dot ÷ PPU32 = 0.125")]
        public float dotWorldSize = 0.125f;

        [Header("参照")]
        [Tooltip("SpriteButton のレイキャストに使うカメラ (通常は LCD コンテンツカメラ)")]
        public Camera pointerCamera;
        [Tooltip("この UI を LCD へ転写する撮影カメラ (ClassSelectRigBuilder が設定)。 Show/Hide で有効化/無効化して画面を切り替える")]
        public Camera rigCamera;
        [Tooltip("スプライトスキン (Tools→ClassSelect→Create Skin Asset で作成、 Resources 配下なら自動ロード)。 未設定の要素は無地プレースホルダ")]
        public ClassSelectSkinAsset skin;
        [Tooltip("日本語対応 TMP フォント (未指定は TMP 既定 ── 日本語が豆腐になる場合はここを設定)")]
        public TMP_FontAsset font;
        [Tooltip("false = LcdPointer から SetPointerOver で駆動 (液晶内 UI)。 true = 自前レイキャスト")]
        public bool buttonsSelfRaycast = true;

        [Header("挙動")]
        public bool showOnStart = true;
        [Tooltip("スタンプ確定後に GameManager.StartNewRun() を呼ぶ")]
        public bool autoStartRun = false;
        [Tooltip("デバッグ: スタンプを押しても Decided イベントを発火せず、 リセットして再選択可能にする。 マップ移行を防ぐ")]
        public bool debugStayOnStamp = false;

        [Header("レイアウト: 切替タブ (左端から)")]
        public Vector2 tabSizeDots = new Vector2(34, 20);
        [Tooltip("タブ全体 (base+アイコン重ね) の中心位置。 X=-96 で LCD 左端接地、 Y=0 で LCD 縦中央揃え")]
        public Vector2 tabTopPosDots = new Vector2(-96, 0);
        [Tooltip("タブの縦間隔 (dot)")]
        public float tabStepDots = 24;
        [Tooltip("選択中タブの右ずらし量 (dot)")]
        public float tabSelectedShiftDots = 4;
        [Tooltip("折り畳み時の左オフセット (dot)。 タブ本体は画面外へ退避しつつ、 右端の FoldToggle だけ画面に残る値")]
        public float tabHiddenOffsetDots = 22;
        [Tooltip("FoldToggle (折り畳みボタン) の幅 (dot)。 base 右端に配置され、 hidden 時はこの幅ぶんだけ画面に残ってクリック可能領域になる")]
        public float foldWidthDots = 14;
        [Tooltip("タブの SlideIn/Out 両端でのバネ跳ね返り量 (dot)。 0 で無効。 4 程度でしっかり跳ねる")]
        public float tabBounceDots = 3;
        [Tooltip("タブの SlideIn/Out 所要秒 (X=展開, Y=畳み)")]
        public Vector2 tabSlideDurations = new Vector2(0.22f, 0.18f);
        [Tooltip("キャラ票 (職業切替時) の SlideIn/Out 所要秒 (X=展開, Y=退場)")]
        public Vector2 sheetSlideDurations = new Vector2(0.28f, 0.16f);
        [Tooltip("詳細タブの SlideIn/Out 所要秒 (X=展開, Y=退場)")]
        public Vector2 detailSlideDurations = new Vector2(0.25f, 0.15f);
        [Tooltip("受理スタンプの SlideIn/Out 所要秒 (X=展開, Y=畳み)")]
        public Vector2 stampSlideDurations = new Vector2(0.22f, 0.18f);

        [Header("レイアウト: キャラ票 (上から)")]
        public Vector2 sheetSizeDots = new Vector2(104, 108);
        [Tooltip("票の中心位置 (dot)。 X=-54 で本スプライト (幅123dot) の左端が LCD 左端 (-115.5dot) に接地")]
        public Vector2 sheetPosDots = new Vector2(-54, 0);
        [Tooltip("隠し位置への上オフセット (dot)")]
        public float sheetHiddenOffsetDots = 150;
        [Tooltip("職業名スプライト (skin.nameSprites) の位置オフセット (dot・票中心基準)。 既定はテキスト名と同じ高さ")]
        public Vector2 nameSpriteOffsetDots = new Vector2(0, -14);
        [Tooltip("肖像の位置オフセット (dot)。 既定 (0,10) = 2026-07-14 に 10dot 上へ移動")]
        public Vector2 portraitOffsetDots = new Vector2(0, 10);
        [Tooltip("出自テキスト (肖像下の来歴文) の位置オフセット (dot・票中心基準)")]
        public Vector2 originTextOffsetDots = new Vector2(0, -42);
        [Tooltip("出自テキストのボックスサイズ (dot)")]
        public Vector2 originBoxDots = new Vector2(88, 28);
        [Tooltip("出自テキストの文字サイズ (dot)")]
        public float originFontSizeDots = 4.5f;
        [Tooltip("出自テキストの文字色")]
        public Color originTextColor = new Color(0.30f, 0.25f, 0.20f);
        [Tooltip("落下着地のオーバーシュート (dot)")]
        public float sheetOvershootDots = 3;

        [Header("レイアウト: 詳細タブ (右から)")]
        public Vector2 detailSizeDots = new Vector2(84, 78);
        public Vector2 detailPosDots = new Vector2(66, 18);
        public float detailHiddenOffsetDots = 120;

        [Header("レイアウト: 決定スタンプ (右から)")]
        public Vector2 stampSizeDots = new Vector2(44, 26);
        public Vector2 stampPosDots = new Vector2(66, -44);
        [Tooltip("スタンプ hidden 時の右退避量 (dot)。 左端の StampFoldToggle が LCD 内に残る値にする")]
        public float stampHiddenOffsetDots = 100;
        [Tooltip("受理スタンプのクリック判定サイズ (dot)。 base 全体ではなく印章 (accept_ui_stamp) の絵の範囲だけに限定するため、 スプライト実寸より小さい値を指定。 デフォルト 40×20 = 印章絵の中央領域")]
        public Vector2 stampHitSizeDots = new Vector2(40, 20);
        [Tooltip("受理スタンプ土台の左端に配置する折り畳みトグルの幅 (dot)。 スタンプが hidden 状態でもこの幅ぶんだけ左端が画面内に残ってクリック可能")]
        public float stampFoldWidthDots = 12;
        [Tooltip("受理押下時に accept_ui_stamp スプライトが下に沈む深さ (dot)。 押し込み演出")]
        public float stampPressDepthDots = 3;

        [Header("ページめくり (職業切替演出)")]
        [Tooltip("1 コマの表示秒。 2 コマ構成なので めくり全体 ≒ この 2 倍")]
        public float pageFlipFrameDuration = 0.09f;
        [Tooltip("めくりコマの位置オフセット (dot・キャラ票中心基準)。 素材の描き位置ズレをここで補正")]
        public Vector2 pageFlipOffsetDots = Vector2.zero;

        [Header("描画順")]
        public string sortingLayerName = "Default";
        public int sortingOrderBase = 100;

        [Header("デバッグ")]
        [Tooltip("クリック判定領域を半透明カラーで可視化 (最前面描画)。 開発中は true、 リリース時 false")]
        public bool showHitboxes = true;

        [Header("イベント")]
        public UnityEvent onDecided = new UnityEvent();
        /// <summary>確定時 (選択された職業付き)。</summary>
        public event System.Action<GameLoop.ClassType> Decided;

        // ============================================================
        //  内部状態
        // ============================================================

        private ClassSelectPanel _tabsPanel, _sheetPanel, _detailPanel, _stampPanel;
        private bool _tabsFolded;   // タブの折り畳み状態
        private bool _stampFolded;  // 受理スタンプの折り畳み状態 (独立動作)
        private readonly List<Transform> _tabs = new List<Transform>();
        private readonly List<Vector3> _tabBasePos = new List<Vector3>();
        private SpriteRenderer _portrait, _sheetPaper;
        private SpriteRenderer _pageFlip; // ページめくりコマ表示 (skin.HasPageFlip 時のみ生成)
        private SpriteRenderer _nameArt;  // 職業名の描き文字 (skin.nameSprites 登録職業のみ表示)
        private Sprite _portraitRectSprite; // 無地フォールバック用の矩形スプライト (アート未登録職業で戻す)
        private TextMeshPro _sheetName, _sheetKana, _sheetRegNo, _sheetOrigin, _detailTitle, _detailBody;
        private GameObject _stampInk;
        private Transform _stampInnerT; // accept_ui_stamp レイヤ (押下時に下がる部分)
        private int _current;
        private bool _switching, _decided;

        // クラス識別色 (肖像プレースホルダ/タブ)
        private static readonly Color[] ClassColors =
        {
            new Color(0.43f, 0.50f, 0.58f), // 剣士: steel blue
            new Color(0.66f, 0.57f, 0.37f), // 騎士: pale gold
            new Color(0.62f, 0.35f, 0.29f), // 狂戦士: dull red
            new Color(0.43f, 0.37f, 0.49f), // 暗殺者: violet gray
        };
        private static readonly Color PaperColor = new Color(0.89f, 0.85f, 0.76f);
        private static readonly Color InkColor   = new Color(0.18f, 0.15f, 0.12f);
        private static readonly Color StampRed   = new Color(0.63f, 0.19f, 0.16f);

        private readonly Dictionary<long, Sprite> _spriteCache = new Dictionary<long, Sprite>();

        // ============================================================
        //  ライフサイクル
        // ============================================================

        private void Awake()
        {
            // カメラ未指定なら LCD コンテンツカメラへフォールバック (BattleVisualRig と同型)
            if (pointerCamera == null)
            {
                var lcd = FindObjectOfType<UI.Lcd.LcdScreen>();
                if (lcd != null) pointerCamera = lcd.contentCamera;
            }
            // 二重生成防止: Edit モードで残された子オブジェクトを全削除してから Build。
            // 「調整用 Inspector を持たない子」 (Collider/_Hitbox/スプライトの重ね/テキスト) は
            // 全て動的生成なので、 Play 開始時に毎回作り直す。 数値パラメータ (dot 座標/サイズ/
            // バウンド量など) は ClassSelectView の Inspector から反映される。
            // DestroyImmediate は Awake の直後に Build と衝突しないよう即座に消すために必要。
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
            Build();
            // 生成した子を自身のレイヤーに揃える (コンテンツカメラがレイヤーカリングしている場合の映らない事故防止)
            SetLayerRecursively(gameObject, gameObject.layer);
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
        }

        private void Start()
        {
            if (showOnStart) Show();
        }

        private float Dot(float d) => Mathf.Round(d) * dotWorldSize;
        private Vector3 DotPos(Vector2 d, float z = 0f) => new Vector3(Dot(d.x), Dot(d.y), z);

        // ============================================================
        //  構築
        // ============================================================

        private void Build()
        {
            // Rebuild 対応: 前回参照は破棄済みなのでクリアしてから作り直す
            _tabs.Clear();
            _tabBasePos.Clear();
            _sheetPanel = null; _detailPanel = null; _stampPanel = null; _tabsPanel = null;
            _portrait = null; _sheetPaper = null; _stampInk = null; _pageFlip = null; _nameArt = null;

            // スキン未割当なら Resources から自動ロード (実行時自動構築でも Inspector 登録が効くように)
            if (skin == null)
                skin = Resources.Load<ClassSelectSkinAsset>(ClassSelectSkinAsset.ResourceName);

            // ---- 背景 (最も奥) 2026-07-10: スキンにあれば LCD 中央に配置 ----
            if (skin != null && skin.backgroundSkin != null && skin.backgroundSkin.HasSprites)
            {
                MakeElement(transform, "Background", Vector2.zero, Vector2.zero,
                            Color.white, sortingOrderBase - 100, skin.backgroundSkin);
            }

            // ---- 切替タブ (左) 2026-07-09: 「base 1 枚 + 4 アイコン全部同座標重ね」 方式 ----
            // レイヤは全て同座標・同サイズで描画されるので、 素材内の Y 位置で 4 職業を描き分ける
            // ピクセルパーフェクト設計と噛み合う。 Collider は base 全体の縦を 4 等分した領域に配置。
            _tabsPanel = MakePanel("TabsPanel", Vector2.zero, new Vector2(-tabHiddenOffsetDots, 0));
            // 2026-07-10: SlideIn/Out の両端でバネ挙動 (Papers Please 風のバインダー引き出し感)
            _tabsPanel.overshoot = Dot(tabBounceDots);
            _tabsPanel.outOvershoot = Dot(tabBounceDots);
            _tabsPanel.slideInDuration = tabSlideDurations.x;
            _tabsPanel.slideOutDuration = tabSlideDurations.y;

            // 土台 (base) を配置。 サイズはスプライト実寸を採用
            Vector2 tabsBoundsDots = tabSizeDots;
            // 描画順 2026-07-13: 本 (キャラ票 +20〜+29・めくりコマ +34) が LCD 左端まで届くため、
            // タブは本の上に乗せる (+36/+40) ── バインダーのインデックスが本からはみ出ている見た目
            var baseGo = MakeElement(_tabsPanel.transform, "TabsBase", tabSizeDots, tabTopPosDots,
                                     PaperColor, sortingOrderBase + 36,
                                     skin != null ? skin.tabBaseSkin : null,
                                     out var baseActual);
            if (baseActual.x != tabSizeDots.x || baseActual.y != tabSizeDots.y)
                tabsBoundsDots = baseActual;

            // 4 職業アイコンを base と同座標・同サイズで重ねる (素材が描き分けを内包)
            for (int i = 0; i < ClassDossierData.Entries.Length; i++)
            {
                var e = ClassDossierData.Entries[i];
                var iconGo = MakeElement(_tabsPanel.transform, $"Tab_{e.classNameKana}", tabSizeDots,
                                         tabTopPosDots, ClassColors[i], sortingOrderBase + 40,
                                         skin != null ? skin.GetTabIconSkin(i) : null);
                _tabs.Add(iconGo.transform);
                _tabBasePos.Add(iconGo.transform.localPosition);
            }

            // Collider: base 全体の縦を 4 等分し、 各アイコンに対応する Y 位置に配置。
            // 右端は「畳みボタン」 領域として除外 (Papers, Please 風のトグル)。 hidden 時にこの幅ぶんだけ残る。
            float foldWidth = Mathf.Max(4f, foldWidthDots);
            float sliceH = tabsBoundsDots.y / 4f;
            float topY = tabTopPosDots.y + tabsBoundsDots.y / 2f - sliceH / 2f;
            float iconWidth = tabsBoundsDots.x - foldWidth;
            float iconCenterX = tabTopPosDots.x - foldWidth / 2f; // 右端の畳みボタンぶん左へオフセット
            for (int i = 0; i < ClassDossierData.Entries.Length; i++)
            {
                var e = ClassDossierData.Entries[i];
                var hitGo = new GameObject($"TabHit_{e.classNameKana}");
                hitGo.transform.SetParent(_tabsPanel.transform, false);
                hitGo.transform.localPosition = DotPos(new Vector2(iconCenterX, topY - sliceH * i));
                AddButton(hitGo, new Vector2(iconWidth, sliceH), CaptureTabClick(i));
            }
            // 畳みボタン: base の右端に縦バー状のクリック領域を 1 枚配置。
            var foldGo = new GameObject("FoldToggle");
            foldGo.transform.SetParent(_tabsPanel.transform, false);
            float foldCenterX = tabTopPosDots.x + tabsBoundsDots.x / 2f - foldWidth / 2f;
            foldGo.transform.localPosition = DotPos(new Vector2(foldCenterX, tabTopPosDots.y));
            AddButton(foldGo, new Vector2(foldWidth, tabsBoundsDots.y), OnFoldToggle);

            // ---- キャラ票 (上から) ----
            _sheetPanel = MakePanel("SheetPanel", Vector2.zero, new Vector2(0, sheetHiddenOffsetDots));
            _sheetPanel.overshoot = Dot(sheetOvershootDots);
            _sheetPanel.slideInDuration = sheetSlideDurations.x;
            _sheetPanel.slideOutDuration = sheetSlideDurations.y;
            var sheetGo = MakeElement(_sheetPanel.transform, "Paper", sheetSizeDots, sheetPosDots,
                                      PaperColor, sortingOrderBase + 20,
                                      skin != null ? skin.sheetSkin : null);
            _sheetPaper = sheetGo.GetComponent<SpriteRenderer>(); // フォールバック時のみ非null
            // 肖像 (票の上半分)。 スキンの portraits[i] があれば差し替え、 無ければ職業色の無地
            var portraitSize = new Vector2(sheetSizeDots.x - 16, 52);
            var portraitPos = new Vector2(sheetPosDots.x, sheetPosDots.y + sheetSizeDots.y / 2 - portraitSize.y / 2 - 10)
                              + portraitOffsetDots;
            _portrait = MakeRect(_sheetPanel.transform, "Portrait", portraitSize, portraitPos,
                                 ClassColors[0], sortingOrderBase + 28);
            _portraitRectSprite = _portrait.sprite;
            _sheetRegNo = MakeText(_sheetPanel.transform, "RegNo", "潜行登録票  No.----", 5f, InkColor,
                                   new Vector2(sheetSizeDots.x - 12, 8),
                                   new Vector2(sheetPosDots.x, sheetPosDots.y + sheetSizeDots.y / 2 - 5),
                                   TextAlignmentOptions.Center, sortingOrderBase + 22);
            _sheetName = MakeText(_sheetPanel.transform, "Name", "剣士", 11f, InkColor,
                                  new Vector2(sheetSizeDots.x - 12, 14),
                                  new Vector2(sheetPosDots.x, sheetPosDots.y - 14),
                                  TextAlignmentOptions.Center, sortingOrderBase + 29);
            _sheetKana = MakeText(_sheetPanel.transform, "Kana", "SWORDSMAN", 5f, InkColor,
                                  new Vector2(sheetSizeDots.x - 12, 8),
                                  new Vector2(sheetPosDots.x, sheetPosDots.y - 26),
                                  TextAlignmentOptions.Center, sortingOrderBase + 29);

            // 出自 (空白の数年・肖像下の来歴文)。 出自選択というフレーミングの本体。
            _sheetOrigin = MakeText(_sheetPanel.transform, "Origin", "", originFontSizeDots,
                                    originTextColor,
                                    originBoxDots,
                                    sheetPosDots + originTextOffsetDots,
                                    TextAlignmentOptions.TopLeft, sortingOrderBase + 29);

            // 職業名の描き文字 (skin.nameSprites)。 テキスト名 (+29) と同じ序列で、
            // ApplyClassContent が登録有無に応じてテキストと排他表示する。
            {
                var nameArtGo = new GameObject("NameArt");
                nameArtGo.transform.SetParent(_sheetPanel.transform, false);
                nameArtGo.transform.localPosition = DotPos(sheetPosDots + nameSpriteOffsetDots);
                _nameArt = nameArtGo.AddComponent<SpriteRenderer>();
                _nameArt.sortingLayerName = sortingLayerName;
                _nameArt.sortingOrder = sortingOrderBase + 29;
                nameArtGo.SetActive(false);
            }

            // ページめくりコマ (skin.pageFlipFrames が揃っている時のみ)。 票の全内容
            // (紙 +20 / 登録番号 +22 / 肖像 +28 / 名前 +29) を覆う +34 に配置し、
            // 内容差し替えの瞬間をページで隠す。 親は SheetPanel = 票のスライドにも追従。
            if (skin != null && skin.HasPageFlip)
            {
                var flipGo = new GameObject("PageFlip");
                flipGo.transform.SetParent(_sheetPanel.transform, false);
                flipGo.transform.localPosition = DotPos(sheetPosDots + pageFlipOffsetDots);
                _pageFlip = flipGo.AddComponent<SpriteRenderer>();
                _pageFlip.sprite = skin.pageFlipFrames[0];
                _pageFlip.sortingLayerName = sortingLayerName;
                _pageFlip.sortingOrder = sortingOrderBase + 34;
                flipGo.SetActive(false);
            }

            // ---- 詳細タブ (右から) ----
            _detailPanel = MakePanel("DetailPanel", Vector2.zero, new Vector2(detailHiddenOffsetDots, 0));
            _detailPanel.slideInDuration = detailSlideDurations.x;
            _detailPanel.slideOutDuration = detailSlideDurations.y;
            MakeElement(_detailPanel.transform, "Paper", detailSizeDots, detailPosDots,
                        PaperColor, sortingOrderBase + 30,
                        skin != null ? skin.detailSkin : null);
            // 2026-07-14: フレーバー欄を廃止し、 メイン武器 / 配給品 / 性質 の 3 項目構成に。
            // Title = メイン武器、 Body = 配給品 (名前+効果) と性質
            _detailTitle = MakeText(_detailPanel.transform, "Title", "", 6f, InkColor,
                                    new Vector2(detailSizeDots.x - 12, 10),
                                    new Vector2(detailPosDots.x, detailPosDots.y + detailSizeDots.y / 2 - 8),
                                    TextAlignmentOptions.TopLeft, sortingOrderBase + 38);
            _detailBody = MakeText(_detailPanel.transform, "Body", "", 5f, InkColor,
                                   new Vector2(detailSizeDots.x - 12, detailSizeDots.y - 22),
                                   new Vector2(detailPosDots.x, detailPosDots.y - 6),
                                   TextAlignmentOptions.TopLeft, sortingOrderBase + 38);

            // ---- 決定スタンプ (右から) ----
            _stampPanel = MakePanel("StampPanel", Vector2.zero, new Vector2(stampHiddenOffsetDots, 0));
            // 2026-07-10: タブと同じ 3 段バウンド (target 到達 → 外側へ跳ね返り → target 停止)
            _stampPanel.overshoot = Dot(tabBounceDots);
            _stampPanel.outOvershoot = Dot(tabBounceDots);
            _stampPanel.slideInDuration = stampSlideDurations.x;
            _stampPanel.slideOutDuration = stampSlideDurations.y;
            var stampBtn = MakeElement(_stampPanel.transform, "StampButton", stampSizeDots, stampPosDots,
                                       StampRed, sortingOrderBase + 40,
                                       skin != null ? skin.stampSkin : null,
                                       out var stampActual);
            // accept_ui_stamp レイヤの子 Transform を検索 (押下時に下がる部分)
            _stampInnerT = null;
            foreach (Transform c in stampBtn.transform)
                if (c.name.EndsWith("_accept_ui_stamp") || c.name.Contains("accept_ui_stamp"))
                { _stampInnerT = c; break; }
            MakeText(stampBtn.transform, "Label", "受 理", 8f, PaperColor,
                     stampActual, Vector2.zero, TextAlignmentOptions.Center, sortingOrderBase + 48);
            // 受理判定は accept_ui_stamp の中央 (印章絵の範囲) だけに限定 ── base 全体だとお盆や
            // 装飾部分をクリックしても発火してしまう。 判定用の子 GameObject を印章位置に配置。
            var stampHit = new GameObject("StampHit");
            stampHit.transform.SetParent(stampBtn.transform, false);
            stampHit.transform.localPosition = Vector3.zero;
            AddButton(stampHit, stampHitSizeDots, OnStampClicked);

            // スタンプ土台の左端に折り畳みトグル (タブの FoldToggle と対称配置)
            float stampFoldW = Mathf.Max(4f, stampFoldWidthDots);
            float stampFoldCenterX = stampPosDots.x - stampActual.x / 2f + stampFoldW / 2f;
            var stampFoldGo = new GameObject("StampFoldToggle");
            stampFoldGo.transform.SetParent(_stampPanel.transform, false);
            stampFoldGo.transform.localPosition = DotPos(new Vector2(stampFoldCenterX, stampPosDots.y));
            AddButton(stampFoldGo, new Vector2(stampFoldW, stampActual.y), OnStampFoldToggle);

            // スタンプ印影 (受理ボタン=スタンプマシーンと完全同座標に配置。
            // 親を StampPanel にすることで、 スタンプの SlideIn/Out に印影も追従する)
            // 描画順: stamp レイヤ (+41) と同層 → front レイヤ (+42) の下。 印影は装飾の下に隠れる
            var inkGo = MakeElement(_stampPanel.transform, "StampInk", new Vector2(30, 16),
                                    stampPosDots,
                                    StampRed, sortingOrderBase + 41,
                                    skin != null ? skin.stampInkSkin : null);
            MakeText(inkGo.transform, "InkLabel", "受理", 6f, PaperColor,
                     new Vector2(30, 16), Vector2.zero, TextAlignmentOptions.Center, sortingOrderBase + 41);
            _stampInk = inkGo;
            _stampInk.SetActive(false);

            // 初期状態: 全パネル隠し + 1 人目を反映
            _tabsPanel.SnapHidden();
            _sheetPanel.SnapHidden();
            _detailPanel.SnapHidden();
            _stampPanel.SnapHidden();
            ApplyClass(0);
        }

        private System.Action CaptureTabClick(int index) => () => OnTabClicked(index);

        // ============================================================
        //  公開 API
        // ============================================================

        /// <summary>確定状態を解除して再利用可能にする (タイトル復帰→2 回目のラン用)。</summary>
        public void ResetSelection()
        {
            _decided = false;
            _switching = false;
            if (_stampInk != null) _stampInk.SetActive(false);
        }

        /// <summary>全パネルを順に展開 (タブ→票→詳細→スタンプ)。 確定済みなら自動でリセット。
        /// rigCamera があれば点灯して LCD をこの画面に切り替え、 LcdPointer の解決先も差し替える。</summary>
        public void Show()
        {
            if (_decided) ResetSelection();
            SetRigActive(true);
            StopAllCoroutines();
            // StopAllCoroutines で CoSwitch が中断された場合の後始末
            // (_switching 固着でタブが無反応になる / めくりコマが出しっぱなしになる、 の防止)
            _switching = false;
            if (_pageFlip != null) _pageFlip.gameObject.SetActive(false);
            // CoShow が全パネルを展開するため、 折り畳みフラグも見た目に合わせてリセット
            _tabsFolded = false;
            _stampFolded = false;
            StartCoroutine(CoShow());
        }

        /// <summary>全パネルを退場し、 スライド完了後にリグカメラを消灯 (タイトル画面へ戻る)。</summary>
        public void Hide()
        {
            StopAllCoroutines();
            _stampPanel.SlideOut();
            _detailPanel.SlideOut();
            _sheetPanel.SlideOut();
            _tabsPanel.SlideOut();
            StartCoroutine(CoDeactivateRigAfter(0.35f));
        }

        private IEnumerator CoDeactivateRigAfter(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            SetRigActive(false);
        }

        private UI.Lcd.LcdPointer _pointer;
        private Camera _pointerPrevCamera;

        private void Update()
        {
            // Edit モード用の保険処理 (Play モード開始時にも 1 度は通す)
            if (!Application.isPlaying) return;
            // 保険: リグが有効なのに LcdPointer が別カメラを掴んでいたら強制的に rig に戻す。
            // Play セッションを跨いで Show/Hide の状態が壊れたケースでもクリックが継続動作する。
            if (rigCamera == null || !rigCamera.gameObject.activeInHierarchy) return;
            if (_pointer == null) _pointer = FindObjectOfType<UI.Lcd.LcdPointer>();
            if (_pointer != null && _pointer.contentCamera != rigCamera)
            {
                if (_pointerPrevCamera == null) _pointerPrevCamera = _pointer.contentCamera;
                _pointer.contentCamera = rigCamera;
            }
        }

        /// <summary>撮影カメラの点灯/消灯 + LcdPointer のコンテンツカメラ差し替え/復元。</summary>
        private void SetRigActive(bool active)
        {
            if (rigCamera == null)
            {
                Debug.LogWarning("[ClassSelectView] rigCamera が null ── LCD 切替不可 (旧構成の残骸か、 ビルダー未経由)");
            }
            else
            {
                if (rigCamera.gameObject.activeSelf != active)
                    rigCamera.gameObject.SetActive(active);
                if (active)
                    Debug.Log($"[ClassSelectView] リグカメラ点灯: enabled={rigCamera.enabled} depth={rigCamera.depth} " +
                              $"targetTex={(rigCamera.targetTexture != null ? rigCamera.targetTexture.name : "<未バインド>")} " +
                              $"pos={rigCamera.transform.position}");
            }

            if (_pointer == null) _pointer = FindObjectOfType<UI.Lcd.LcdPointer>();
            if (_pointer == null || rigCamera == null) return;

            if (active)
            {
                if (_pointer.contentCamera != rigCamera)
                {
                    _pointerPrevCamera = _pointer.contentCamera;
                    _pointer.contentCamera = rigCamera;
                }
            }
            else
            {
                if (_pointer.contentCamera == rigCamera)
                    _pointer.contentCamera = _pointerPrevCamera;
            }
        }

        private IEnumerator CoShow()
        {
            _tabsPanel.SlideIn();
            yield return new WaitForSecondsRealtime(0.10f);
            _sheetPanel.SlideIn();
            yield return new WaitForSecondsRealtime(0.12f);
            _detailPanel.SlideIn();
            yield return new WaitForSecondsRealtime(0.08f);
            _stampPanel.SlideIn();
        }

        // ============================================================
        //  切替 / 確定
        // ============================================================

        private void OnTabClicked(int index)
        {
            if (_decided || _switching) return;
            // 折り畳み中なら展開 + 該当職業を反映
            if (_tabsFolded)
            {
                ApplyClass(index);
                SetTabsFolded(false);
                return;
            }
            if (index == _current) return;
            StartCoroutine(CoSwitch(index));
        }

        /// <summary>タブ右端バーで畳み/展開トグル。 スタンプとは独立動作。</summary>
        private void OnFoldToggle()
        {
            if (_decided || _switching) return;
            SetTabsFolded(!_tabsFolded);
        }

        /// <summary>スタンプ左端バーで畳み/展開トグル。 タブとは独立動作。</summary>
        private void OnStampFoldToggle()
        {
            if (_decided || _switching) return;
            SetStampFolded(!_stampFolded);
        }

        /// <summary>タブ本体を左へ引っ込める (右端の FoldToggle だけ画面に残す)。</summary>
        private void SetTabsFolded(bool folded)
        {
            if (_tabsFolded == folded) return;
            _tabsFolded = folded;
            if (folded) _tabsPanel.SlideOut();
            else _tabsPanel.SlideIn();
            Debug.Log($"[ClassSelectView] タブ {(folded ? "折り畳み" : "展開")}");
        }

        /// <summary>受理スタンプを右へ引っ込める (左端の StampFoldToggle だけ画面に残す)。</summary>
        private void SetStampFolded(bool folded)
        {
            if (_stampFolded == folded) return;
            _stampFolded = folded;
            if (folded) _stampPanel.SlideOut();
            else _stampPanel.SlideIn();
            Debug.Log($"[ClassSelectView] スタンプ {(folded ? "折り畳み" : "展開")}");
        }

        private IEnumerator CoDeferredIn(ClassSelectPanel p, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            p.SlideIn();
        }

        private IEnumerator CoSwitch(int index)
        {
            _switching = true;

            // タブアイコン (SetActive) はクリック即時に反映 ── 押した瞬間に選択枠が動く
            ApplyTabHighlight(index);

            if (_pageFlip != null)
            {
                // ---- ページめくり演出 (2026-07-13) ----
                // 票は動かさず、 本のページをめくって内容を差し替える。 詳細タブは従来どおり
                // 右へ退場→復帰 (票と別の紙なので独立に動いてよい)。
                _detailPanel.SlideOut();

                // 1 コマ目: ページが持ち上がる (旧内容はまだ見えている)
                _pageFlip.sprite = skin.pageFlipFrames[0];
                _pageFlip.gameObject.SetActive(true);
                yield return new WaitForSecondsRealtime(pageFlipFrameDuration);

                // 2 コマ目: ページが票を覆う ── この裏で内容 (肖像/名前/説明/flavor) を差し替え
                _pageFlip.sprite = skin.pageFlipFrames[1];
                ApplyClassContent(index);
                yield return new WaitForSecondsRealtime(pageFlipFrameDuration);

                // めくり終わり: コマを消して新内容を見せ、 詳細タブを戻す
                _pageFlip.gameObject.SetActive(false);
                _detailPanel.SlideIn();
            }
            else
            {
                // ---- 従来のスライド切替 (めくりコマ未登録時のフォールバック) ----
                // 票は上へ引っ込み、 詳細は右へ引っ込む
                bool sheetDone = false, detailDone = false;
                _sheetPanel.SlideOut(() => sheetDone = true);
                _detailPanel.SlideOut(() => detailDone = true);
                while (!sheetDone || !detailDone) yield return null;

                // 内容 (肖像/名前/説明/flavor) は退場後に差し替え
                ApplyClassContent(index);

                // 新しい票が上から落ち、 詳細が右から戻る
                sheetDone = false;
                _sheetPanel.SlideIn(() => sheetDone = true);
                yield return new WaitForSecondsRealtime(0.10f);
                _detailPanel.SlideIn();
                while (!sheetDone) yield return null;
            }

            _switching = false;
        }

        /// <summary>タブアイコンのハイライト状態だけを即時反映 (クリック直後に呼ぶ)。</summary>
        private void ApplyTabHighlight(int index)
        {
            _current = index;
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool selected = (i == index);
                _tabs[i].gameObject.SetActive(!selected);
                if (!selected)
                    foreach (var sr in _tabs[i].GetComponentsInChildren<SpriteRenderer>())
                        sr.color = Color.white;
            }
        }

        /// <summary>職業内容 (肖像/名前/説明/flavor) を差し替える。 SlideOut → SlideIn 間で呼ぶ。</summary>
        private void ApplyClassContent(int index)
        {
            var e = ClassDossierData.Entries[index];
            ClassDossierData.Resolve(e, out string itemName, out string itemDesc, out string flavor);

            // 肖像: スキンにアートがあれば差し替え、 無ければ職業色の無地矩形に戻す
            if (_portrait != null)
            {
                var art = skin != null ? skin.GetPortrait(index) : null;
                if (art != null)
                {
                    _portrait.sprite = art;
                    _portrait.color = Color.white;
                }
                else
                {
                    _portrait.sprite = _portraitRectSprite;
                    _portrait.color = ClassColors[index];
                }
            }
            // 職業名: スキンに描き文字があればスプライト表示、 無ければ TMP テキストにフォールバック。
            // 描き文字にはカナも含めて描き込む想定なので、 スプライト時は名前+カナの両テキストを隠す。
            var nameArt = skin != null ? skin.GetNameSprite(index) : null;
            if (_nameArt != null)
            {
                _nameArt.sprite = nameArt;
                // 位置 = 票中心 + 共通オフセット (Inspector) + 職業別オフセット (skin)
                _nameArt.transform.localPosition = DotPos(
                    sheetPosDots + nameSpriteOffsetDots +
                    (skin != null ? skin.GetNameSpriteOffset(index) : Vector2.zero));
                _nameArt.gameObject.SetActive(nameArt != null);
            }
            if (_sheetName != null)
            {
                _sheetName.text = e.className;
                _sheetName.gameObject.SetActive(nameArt == null);
            }
            if (_sheetKana != null)
            {
                _sheetKana.text = e.classNameKana;
                _sheetKana.gameObject.SetActive(nameArt == null);
            }
            if (_sheetRegNo != null) _sheetRegNo.text = $"潜行登録票  No.19{24 + index:00}";
            if (_sheetOrigin != null) _sheetOrigin.text = e.origin;
            if (_detailTitle != null) _detailTitle.text = $"メイン武器: {ClassDossierData.ResolveWeaponName(e)}";
            if (_detailBody != null)
                _detailBody.text = $"配給品: {itemName}\n\n性質:\n{e.traits}";
        }

        /// <summary>初期化・折り畳み解除など、 タブと内容の両方を一気に反映するとき用。</summary>
        private void ApplyClass(int index)
        {
            ApplyTabHighlight(index);
            ApplyClassContent(index);
        }

        private void OnStampClicked()
        {
            if (_decided || _switching) return;
            _decided = true;
            StartCoroutine(CoStamp());
        }

        private IEnumerator CoStamp()
        {
            var cls = ClassDossierData.Entries[_current].type;
            GameLoop.GameManager.SelectedClass = cls;

            // 印影は押された時点で StampPanel から切り離す (2026-07-14) ──
            // 以後スタンプ機の格納/スライドに追従せず、 書類の上に残る
            var inkT = _stampInk.transform;
            if (inkT.parent != transform) inkT.SetParent(transform, true);

            // 印影を落とす (上 6dot から素早く着地) + 同時に accept_ui_stamp が押し込まれて下がる
            _stampInk.SetActive(true);
            Vector3 target = inkT.localPosition;
            inkT.localPosition = target + new Vector3(0, Dot(6), 0);
            // accept_ui_stamp スプライトの押し込み (即時)
            Vector3 innerBase = _stampInnerT != null ? _stampInnerT.localPosition : Vector3.zero;
            if (_stampInnerT != null)
                _stampInnerT.localPosition = innerBase + new Vector3(0, -Dot(stampPressDepthDots), 0);
            float t = 0f;
            const float dur = 0.08f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                inkT.localPosition = Vector3.Lerp(inkT.localPosition, target, Mathf.Clamp01(t / dur));
                yield return null;
            }
            inkT.localPosition = target;

            // スタンプマシーンの着弾ジッタ (減衰) ── 押される衝撃をマシーン自身が受ける
            var stampT = _stampPanel.transform;
            Vector3 basePos = stampT.localPosition;
            float jitter = Dot(2);
            for (int i = 0; i < 6; i++)
            {
                stampT.localPosition = basePos + (Vector3)(Random.insideUnitCircle * jitter);
                jitter *= 0.6f;
                yield return new WaitForSecondsRealtime(0.025f);
            }
            stampT.localPosition = basePos;

            // 押し込まれた accept_ui_stamp を元位置に戻す (ジッタ後にゆっくり戻る)
            if (_stampInnerT != null)
            {
                Vector3 pressed = _stampInnerT.localPosition;
                float rt = 0f;
                const float rdur = 0.10f;
                while (rt < rdur)
                {
                    rt += Time.unscaledDeltaTime;
                    _stampInnerT.localPosition = Vector3.Lerp(pressed, innerBase, Mathf.Clamp01(rt / rdur));
                    yield return null;
                }
                _stampInnerT.localPosition = innerBase;
            }

            // 押印後、 スタンプ機は自動で格納 (2026-07-14)。 印影は切り離し済みなので書類上に残る
            yield return new WaitForSecondsRealtime(0.15f);
            SetStampFolded(true);

            yield return new WaitForSecondsRealtime(0.35f);

            if (debugStayOnStamp)
            {
                // デバッグモード: マップ移行せず再選択可能に戻す。
                // 印影は残す (2026-07-14: 押印結果の見た目を確認し続けられるように。
                // 次の押印時に CoStamp が再度落下させ、 タブ切替では消えない)
                Debug.Log($"[ClassSelectView] [DEBUG] 職業 {cls} を選択 (マップ移行スキップ・印影は残置)");
                _decided = false;
                yield break;
            }

            Debug.Log($"[ClassSelectView] 職業確定: {cls}");
            onDecided?.Invoke();
            Decided?.Invoke(cls);

            if (autoStartRun && GameLoop.GameManager.Instance != null)
                GameLoop.GameManager.Instance.StartNewRun();
        }

        // ============================================================
        //  生成ヘルパ (無地スプライト / TMP テキスト / ボタン)
        // ============================================================

        private ClassSelectPanel MakePanel(string name, Vector2 shownDots, Vector2 hiddenOffsetDots)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var p = go.AddComponent<ClassSelectPanel>();
            p.shownLocalPos = DotPos(shownDots);
            p.hiddenLocalPos = DotPos(shownDots + hiddenOffsetDots);
            return p;
        }

        /// <summary>UI 要素を生成。 スキン (SpriteStack) があれば複数スプライトを orderOffset の
        /// 前後序列で重ねて 1 つの塊にし、 無ければ従来の無地矩形にフォールバックする。
        /// 戻り値のルート GO を動かせば重ねたスプライトごと一体で動く。
        /// 副作用: スキン適用時、 sizeDots をスプライト実寸 (最大バウンディング) で out 返却 ──
        /// Collider や位置計算をピクセルパーフェクトに追従させるため。</summary>
        private GameObject MakeElement(Transform parent, string name, Vector2 sizeDots,
                                       Vector2 posDots, Color fallbackColor, int baseOrder,
                                       ClassSelectSkinAsset.SpriteStack stack, out Vector2 actualSizeDots)
        {
            actualSizeDots = sizeDots;
            if (stack == null || !stack.HasSprites)
                return MakeRect(parent, name, sizeDots, posDots, fallbackColor, baseOrder).gameObject;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = DotPos(posDots);
            float maxW = 0, maxH = 0;
            for (int i = 0; i < stack.layers.Length; i++)
            {
                var layer = stack.layers[i];
                if (layer == null || layer.sprite == null) continue;
                var lgo = new GameObject($"Layer{i}_{layer.sprite.name}");
                lgo.transform.SetParent(go.transform, false);
                lgo.transform.localPosition = DotPos(layer.offsetDots);
                var sr = lgo.AddComponent<SpriteRenderer>();
                sr.sprite = layer.sprite;
                // アセット作成時に tint 未設定だと α=0 になる (ScriptableObject の default(Color))。
                // その場合は白として描画 = 「素材そのまま」 の直感的な既定値に。
                sr.color = (layer.tint.a < 0.001f) ? Color.white : layer.tint;
                sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder = baseOrder + layer.orderOffset;
                // 1 sprite px = 1 モニタードット (PPU=8, dotWorldSize=0.125) なのでバウンディングは
                // sprite.rect.size をそのまま dot 単位として扱える
                float w = layer.sprite.rect.width;
                float h = layer.sprite.rect.height;
                if (w > maxW) maxW = w;
                if (h > maxH) maxH = h;
            }
            if (maxW > 0f && maxH > 0f) actualSizeDots = new Vector2(maxW, maxH);
            return go;
        }

        /// <summary>out なしオーバーロード (呼び出し側でサイズ追従が不要な場合)。</summary>
        private GameObject MakeElement(Transform parent, string name, Vector2 sizeDots,
                                       Vector2 posDots, Color fallbackColor, int baseOrder,
                                       ClassSelectSkinAsset.SpriteStack stack)
            => MakeElement(parent, name, sizeDots, posDots, fallbackColor, baseOrder, stack, out _);

        private SpriteRenderer MakeRect(Transform parent, string name, Vector2 sizeDots,
                                        Vector2 posDots, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = DotPos(posDots);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetRectSprite(Mathf.Max(1, Mathf.RoundToInt(sizeDots.x)),
                                      Mathf.Max(1, Mathf.RoundToInt(sizeDots.y)));
            sr.color = color;
            sr.sortingLayerName = sortingLayerName;
            sr.sortingOrder = sortingOrder;
            return sr;
        }

        /// <summary>ドット等倍の白矩形スプライト (PPU = 1/dotWorldSize)。 スケール不使用で実寸生成。</summary>
        private Sprite GetRectSprite(int wDots, int hDots)
        {
            long key = ((long)wDots << 20) | (uint)hDots;
            if (_spriteCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(wDots, hDots, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[wDots * hDots];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, true);

            float ppu = 1f / Mathf.Max(0.0001f, dotWorldSize); // 1dot = 1px になる PPU
            var sp = Sprite.Create(tex, new Rect(0, 0, wDots, hDots), new Vector2(0.5f, 0.5f), ppu);
            sp.name = $"Rect_{wDots}x{hDots}";
            _spriteCache[key] = sp;
            return sp;
        }

        private TextMeshPro MakeText(Transform parent, string name, string text, float fontSizeDots,
                                     Color color, Vector2 boxDots, Vector2 posDots,
                                     TextAlignmentOptions align, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = DotPos(posDots, -0.01f);
            var tmp = go.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            // TMP のワールド fontSize ≒ 文字高(ワールド単位)×10 → dot 指定から換算
            tmp.fontSize = fontSizeDots * dotWorldSize * 10f;
            tmp.color = color;
            tmp.alignment = align;
            tmp.enableWordWrapping = true;
            tmp.rectTransform.sizeDelta = new Vector2(boxDots.x * dotWorldSize, boxDots.y * dotWorldSize);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sortingLayerName = sortingLayerName;
                mr.sortingOrder = sortingOrder;
            }
            return tmp;
        }

        private void AddButton(GameObject go, Vector2 sizeDots, System.Action onClick)
        {
            // 親と同じレイヤに揃える (RigBuilder が親を LcdPointer.contentMask 対応レイヤに設定するため、
            // AddComponent 前に確実に反映しておく。 Collider がレイヤ 0 のままだと Pointer に拾われない)
            go.layer = transform.gameObject.layer;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(sizeDots.x * dotWorldSize, sizeDots.y * dotWorldSize);
            var btn = go.AddComponent<SpriteButton>();
            btn.cam = pointerCamera;
            btn.selfRaycast = buttonsSelfRaycast;
            btn.pressBrightness = -0.25f; // 押下で少し沈む (暗く)
            btn.Clicked += onClick;

            // デバッグ: クリック判定領域を半透明矩形で可視化 (最前面)
            if (showHitboxes)
            {
                var vis = new GameObject("_Hitbox");
                vis.transform.SetParent(go.transform, false);
                var sr = vis.AddComponent<SpriteRenderer>();
                sr.sprite = GetRectSprite(Mathf.Max(1, Mathf.RoundToInt(sizeDots.x)),
                                          Mathf.Max(1, Mathf.RoundToInt(sizeDots.y)));
                // 名前で色分け: FoldToggle=黄、 Stamp=赤、 Tab=シアン
                Color c = new Color(0f, 1f, 1f, 0.35f);
                if (go.name.Contains("Fold")) c = new Color(1f, 0.9f, 0f, 0.4f);
                else if (go.name.Contains("Stamp")) c = new Color(1f, 0.2f, 0.2f, 0.4f);
                sr.color = c;
                sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder = 999; // 最前面
            }
        }
    }
}

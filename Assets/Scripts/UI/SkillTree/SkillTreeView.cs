using System.Collections.Generic;
using UnityEngine;
using TMPro;
using MetaProgression;
using UI;
using UI.Lcd;

namespace UI.SkillTree
{
    /// <summary>
    /// 液晶内のスキルツリー（メタ進行）解放画面。 線形 59 段の <see cref="MetaBuffTrack"/> を
    /// 「下から上へ昇る星座」として描く。 ノードは星、 連結線は星座線。 次の1段だけが購入可能（線形）。
    ///
    /// 構成（このオブジェクトの子に実行時生成）:
    ///   Lines   … 連続ノード間の星座線（スクロールする）
    ///   Nodes   … 各段の星ノード＋SpriteButton（スクロールする）
    ///   Header  … トークン残高/次コストの TextMesh（固定）
    /// 既存の夜空 BG/星/砂塵はそのまま背景に使う。 メニューは <see cref="Open"/>/<see cref="Close"/> で出し入れ。
    ///
    /// 規約: 状態（解放/次/未開放）は色・明度・別スプライトで表現し localScale は使わない（PPU=32）。
    /// 入力: 液晶内なので各ノードの SpriteButton は selfRaycast=false（LcdPointer 駆動）。
    /// </summary>
    [DisallowMultipleComponent]
    public class SkillTreeView : MonoBehaviour
    {
        [Header("開閉")]
        [Tooltip("ツリー表示中に隠すメニュー（タイトル/ボタン群）")]
        public GameObject menuToHide;
        [Tooltip("戻るボタン（selfRaycast=false 推奨）")]
        public SpriteButton backButton;
        [Tooltip("トークン残高/次コストの表示（任意）。TMP_Text なのでピクセルアート TMP フォント(mihiPixelmoji_v1) を割り当てる。")]
        public TMP_Text infoText;
        [Tooltip("infoText に強制適用するピクセルアート TMP フォント（item preview と同じ mihiPixelmoji_v1）。未指定なら infoText 側の設定を尊重。")]
        public TMP_FontAsset infoFont;
        [Tooltip("infoText 自動生成時のフォントサイズ(world)。 既存 TMP には適用しない。")]
        public float infoFontSize = 8f;
        [Tooltip("デバッグ: Info テキスト GO を強制非表示にして □ の出所を切り分ける。")]
        public bool debugHideInfo = false;

        [Header("ノード情報ウィンドウ (skilltree_window)")]
        [Tooltip("ノードクリック時に画面下に開くウィンドウ背景 sprite (BUFF モード用)。")]
        public Sprite skilltreeWindowSprite;
        [Tooltip("DEBUFF モードのノードクリック時に開くウィンドウ背景 sprite。 未指定なら BUFF と同じ sprite を流用。")]
        public Sprite skilltreeDebuffWindowSprite;
        [Tooltip("ウィンドウの sortingOrder (前景なのでノードより大きく)。")]
        public int windowOrder = 20;
        [Tooltip("上段 (タイトル) 用 TMP フォント (未指定なら infoFont を流用)。")]
        public TMP_FontAsset windowFont;
        [Tooltip("下段 (説明) 用 TMP フォント (未指定なら windowFont → infoFont の順でフォールバック)。")]
        public TMP_FontAsset windowDescFont;
        [Tooltip("上段 (ノード名) のフォントサイズ (world)。")]
        public float windowTitleFontSize = 2.4f;
        [Tooltip("上段 (ノード名) の色 ─ BUFF モード用。")]
        public Color windowTitleColor = new Color(1.00f, 0.95f, 0.70f, 1f);
        [Tooltip("上段 (ノード名) の色 ─ DEBUFF モード用 (デバフ側のタイトルにのみ適用)。")]
        public Color windowDebuffTitleColor = new Color(0.55f, 0.85f, 1.00f, 1f);
        [Tooltip("下段 (効果説明) のフォントサイズ (world)。")]
        public float windowDescFontSize = 1.6f;
        [Tooltip("下段 (効果説明) の色。")]
        public Color windowDescColor = Color.white;
        [Tooltip("タイトルと説明の縦間隔 (world)。 0 で隙間なし。")]
        public float windowTitleDescGap = 0.5f;
        [Tooltip("タイトル領域が内側高さに占める比率 (0=無、 1=全部)。 残りが説明領域になる。 既定 0.30。")]
        [Range(0.1f, 0.7f)] public float windowTitleAreaRatio = 0.30f;
        [Tooltip("ウィンドウ底辺と画面下端の間隔 (world)。 0 で密着。")]
        public float windowBottomMargin = 0.5f;
        [Tooltip("ウィンドウ枠内のテキスト余白 (world, x=左右 / y=上下)。")]
        public Vector2 windowTextPadding = new Vector2(2.0f, 1.5f);
        public enum WindowHorizontalAlign { Center, LeftEdge, RightEdge }
        [Tooltip("ウィンドウの横方向アンカー。 Center=画面中央 / LeftEdge=左端を可視左端に揃える / RightEdge=右端を可視右端に揃える。")]
        public WindowHorizontalAlign windowHorizontalAlign = WindowHorizontalAlign.Center;

        [Header("背景星（star.png 4分割: 暗→明）")]
        [Tooltip("star_1〜star_4 の順（暗→明）に割当。 各要素はランダム抽選で配置される。")]
        public Sprite[] backgroundStarSprites;
        [Tooltip("背景に散らす星の総数")]
        [Min(0)] public int backgroundStarCount = 60;
        [Tooltip("散布範囲(world local)。 背景縦長エリアにフィットさせる。")]
        public Vector2 backgroundStarArea = new Vector2(28f, 60f);
        [Tooltip("明度分布の重み（要素数は backgroundStarSprites と同じか短く）。 末尾の明るい星ほど少なく。")]
        public float[] backgroundStarWeights = new float[] { 6f, 3f, 1.5f, 0.5f };
        [Tooltip("背景星の sortingOrder。 背景画像(4)より上、 線(6)より下が目安。")]
        public int backgroundStarOrder = 5;
        [Tooltip("ピクセルスナップ用 PPU（背景=32 推奨）")]
        public int backgroundStarPPU = 32;
        [Tooltip("明滅の振幅(0..0.6)。 0 で点滅なし。")]
        [Range(0f, 0.6f)] public float backgroundStarTwinkle = 0.25f;
        [Tooltip("明滅速度(rad/sec)。 1〜3 程度がそれっぽい。")]
        public float backgroundStarTwinkleSpeed = 1.4f;
        [Tooltip("星同士の最低距離(world)。 これより近くは生成しない（密集回避）。 0 で無効。")]
        public float backgroundStarMinDistance = 1.5f;
        [Tooltip("最低距離を満たせない時の最大リトライ回数。 増やすと密度を保ちやすいが負荷上がる。")]
        public int backgroundStarMaxAttempts = 24;

        [Header("背景（縦長PNG・スクロール連動）")]
        [Tooltip("スキルツリーの夜空背景（下=水平線/上=深い夜）。 ノード/線の奥でスクロールする。")]
        public Sprite backgroundSprite;
        public int backgroundOrder = 4;
        [Tooltip("ノード配置を背景の高さに自動フィット（下→上）")]
        public bool fitToBackground = true;
        [Tooltip("フィット時の上/下の余白(world)")]
        public float topMargin = 5f;
        public float bottomMargin = 6f;
        [Tooltip("コンテンツカメラの縦半幅(world)。 ortho=8.6875 相当。 背景クランプ用。")]
        public float contentHalfHeight = 8.6875f;
        [Tooltip("背景のスクロール率（ツリー基準）。 1=完全連動 / <1=背景がゆっくり動く視差。 0.6推奨。")]
        [Range(0f, 1f)] public float backgroundScrollFactor = 0.6f;

        [Header("ノード見た目（PNG・未指定はコード生成の星）")]
        [Tooltip("通常ノードの汎用スプライト。 kind-別アイコン未割当のコモンに使われる。")]
        public Sprite nodeNormalSprite;
        [Tooltip("大スキルのフォールバック（プレースホルダー）。 kind-別アイコン未割当の大スキルに使われる。 placeholder_star.png を推奨。")]
        public Sprite nodeMajorSprite;

        [Header("ノードアイコン: コモン (MetaBuffKind 別)")]
        [Tooltip("MetaBuffKind=Hp 用。")]
        public Sprite nodeHpSprite;
        [Tooltip("MetaBuffKind=Gold 用。")]
        public Sprite nodeGoldSprite;
        [Tooltip("MetaBuffKind=OutgoingDamagePct（与ダメ+5%）用。")]
        public Sprite nodeDamagePctSprite;

        [Header("ノードアイコン: 大スキル序盤 (Lv 1-20)")]
        [Tooltip("Lv8 開幕武器強化素材 +3 (StartMaterial)")]                public Sprite iconStartMaterial;
        [Tooltip("Lv12 戦闘勝利金 +2 (CombatGoldBonus)")]                    public Sprite iconCombatGoldBonus;
        [Tooltip("Lv16 被ダメージ -2 (DamageReduce)")]                        public Sprite iconDamageReduce;
        [Tooltip("Lv20 ボス撃破時 ノーマルパッシブ獲得 (BossExtraNormal)")]    public Sprite iconBossExtraNormal;

        [Header("ノードアイコン: 大スキル ミッド (Lv 21-41)")]
        [Tooltip("Lv23 戦闘後の希望減少 -3 (HopeLossReduce)")]                public Sprite iconHopeLossReduce;
        [Tooltip("Lv26 開幕ノーマルパッシブ獲得 (StartingPassiveItem)")]        public Sprite iconStartingPassiveItem;
        [Tooltip("Lv27 ショップ強盗 解禁 (ShopRobberyUnlock)")]                public Sprite iconShopRobberyUnlock;
        [Tooltip("Lv30 会心ダイス補正 +2 (CritLevelUp)")]                      public Sprite iconCritLevelUp;

        [Header("ノードアイコン: 大スキル 終盤 (Lv 42-58)")]
        [Tooltip("Lv42 ダイス合計 +1 (DiceTotal)")]                            public Sprite iconDiceTotal;
        [Tooltip("Lv44 ラストスタンド 最大HP低下無効 (LastStandHpLossDisable)")] public Sprite iconLastStandHpLossDisable;
        [Tooltip("Lv45 ボス追加報酬を レア化 (BossExtraRare)")]                public Sprite iconBossExtraRare;
        [Tooltip("Lv51 ボス前休憩 回復+強化 同時可 (BossRestHealAndUpgrade)")]    public Sprite iconBossRestHealAndUpgrade;
        [Tooltip("Lv58 final 会心ダメージ +100% (CritDamageBonus)")]             public Sprite iconCritDamageBonus;

        [Header("ノード接続線・クリック判定")]
        [Tooltip("連結線のスプライト（1px白推奨・未指定は生成）")]
        public Sprite lineSprite;
        [Tooltip("ノードのクリック判定の一辺(world)。 見た目より大きめにすると押しやすい。 0以下でスプライト矩形に従う。")]
        public float nodeHitSize = 5f;

        [Header("ノード状態カラー（位置に関係なく状態で決まる）")]
        [Tooltip("未開放ノードの色（水色）。")]
        public Color lockedColor = new Color(133f / 255f, 167f / 255f, 192f / 255f, 1f);
        [Tooltip("解放済みノードの色（やや寒色寄りの白＝色温度低め）。")]
        public Color unlockedColor = new Color(0.90f, 0.95f, 1.00f, 1f);
        [Tooltip("次に買えるノードの色（明滅）。")]
        public Color nextColor = Color.white;
        public Color lineUnlockedColor = new Color(0.95f, 0.90f, 0.65f, 0.85f);
        public Color lineLockedColor = new Color(0.40f, 0.45f, 0.60f, 0.30f);
        [Tooltip("次に買えるノードの明滅の速さ")]
        public float nextPulseSpeed = 3f;

        [Header("当たり判定の可視化（デバッグ）")]
        [Tooltip("ノードの BoxCollider2D サイズを矩形で可視化する。")]
        public bool showHitAreaDebug = false;
        [Tooltip("有効化中(=次に押せる)の判定枠の色。半透明緑。")]
        public Color hitEnabledColor = new Color(0.20f, 1.00f, 0.30f, 0.35f);
        [Tooltip("無効化中の判定枠の色。半透明赤。")]
        public Color hitDisabledColor = new Color(1.00f, 0.25f, 0.25f, 0.20f);
        [Tooltip("判定枠の描画順（ノードより前に出すと見やすい）")]
        public int hitDebugOrder = 9;

        [Header("星座レイアウト（全体は下→上・個々は自由に散らばる）")]
        [Tooltip("1段あたりの平均縦上昇(world)。 大きいほどツリーが縦に長い。")]
        public float dyPerNode = 1.4f;
        [Tooltip("最下段の基準 Y(local)。 自動計算されるので通常触らない。")]
        public float baseY = 0f;
        [Tooltip("横方向の散らばり半幅(world)。 可視半幅±14.4内に収める。")]
        public float amplitude = 11f;
        [Tooltip("隣接ノード間の横移動の最大ステップ(world)。 大きいほど横に跳ねる。")]
        public float xStep = 6f;
        [Tooltip("横位置を中央へ引き戻す力(0-1)。 ランダムウォークの片寄り(ドリフト)を抑え、 全体の密度を均す。")]
        [Range(0f, 1f)] public float centerPull = 0.28f;
        [Tooltip("縦の揺らぎ(world)。 上昇トレンドからこの幅で上下にぶれる＝前の星より下に来てもよい。")]
        public float verticalJitter = 2.5f;
        [Tooltip("隣接ノードの最小間隔(world)。 星が重ならない下限。 星実寸より少し大きく。")]
        public float minNodeGap = 2.6f;

        [Header("均一間隔モード")]
        [Tooltip("ON: 隣接ノード間の距離を完全一定にする（横は揺れるが線の長さが全て同じ）。")]
        public bool uniformSpacing = false;
        [Tooltip("均一モードでの1セグメントの距離(world)。 これが全ノード間で一定になる。")]
        public float uniformGap = 9f;
        [Tooltip("均一モードの横移動の最大比率(0-0.98)。 大きいほど横に強く振れ、 縦の上昇は緩やかになる。")]
        [Range(0.1f, 0.98f)] public float uniformHorizontalRatio = 0.85f;
        [Tooltip("配置の乱数シード（固定で再現）")]
        public int seed = 12345;

        [Header("終端ノード調整")]
        [Tooltip("最後の2ノード (Lv57 / Lv58) を中央 (X=0) にロックする。 final の存在を視覚的に強調するため。")]
        public bool centerFinalTwo = true;
        [Tooltip("最終ノード (Lv58 final) を最後から2番目から余分に離す距離(world)。 通常セグメント長に加算される。")]
        public float finalNodeExtraGap = 6f;

        [Header("メタデバフ拡張 (深淵モード)")]
        [Tooltip("ON: BUFF 下端到達後の追加スクロールで DEBUFF モードへ切替できるようになる。")]
        public bool debuffSectionEnabled = true;
        [Tooltip("深淵モード専用の背景 (縦長 PNG)。 BUFF 背景とは別レンダで完全分離。")]
        public Sprite debuffBackgroundSprite;
        [Tooltip("深淵背景の sortingOrder。")]
        public int debuffBackgroundOrder = 4;
        [Header("メタデバフ ノードアイコン (Lv1〜Lv10)")]
        [Tooltip("Lv1 困窮した商隊 (ショップ価格 +25%)")]              public Sprite iconDebuffLv1;
        [Tooltip("Lv2 俊敏 (敵が最初の被弾を必ず回避)")]                 public Sprite iconDebuffLv2;
        [Tooltip("Lv3 向かい風 (敵への与ダメ -1)")]                      public Sprite iconDebuffLv3;
        [Tooltip("Lv4 前途多難 (マップ視界 -2)")]                         public Sprite iconDebuffLv4;
        [Tooltip("Lv5 偽の商人 (ショップ 30% で偽商人化)")]               public Sprite iconDebuffLv5;
        [Tooltip("Lv6 死神の影 (3層突入時に恒久デバフ +1)")]              public Sprite iconDebuffLv6;
        [Tooltip("Lv7 補給断絶 (前哨基地回復上限 50%)")]                  public Sprite iconDebuffLv7;
        [Tooltip("Lv8 絶望的な進軍 (移動毎に希望 -1)")]                   public Sprite iconDebuffLv8;
        [Tooltip("Lv9 鋼の皮膚 (敵が初回致命傷を HP1 で耐える)")]         public Sprite iconDebuffLv9;
        [Tooltip("Lv10 天変地異 (final / 敵ダメ+100% / 1層恒久 / ラスタン無効)")] public Sprite iconDebuffLv10;
        [Tooltip("DEBUFF ノード間の一定縦間隔 (world)。 全ノード直線等間隔で配置。")]
        public float debuffNodeSpacing = 4f;
        [Tooltip("DEBUFF 最終ノード (Lv10) より先に余分にスクロールできる距離 (world)。 背景の下端を画面内で見せるため。")]
        public float debuffEndExtraScroll = 8f;
        [Tooltip("BUFF 下端 / DEBUFF 上端を超えてスクロールを継続したとき、 この距離 (world) 蓄積で モード切替トランジションが発生する。")]
        public float overscrollThreshold = 10f;
        [Tooltip("ホイール停止後、 オーバースクロール蓄積を 0 に減衰させる速度 (world/秒)。")]
        public float overscrollDecay = 12f;
        [Tooltip("モード切替直後にホイール入力を抑制する時間 (秒)。 0 で抑制なし。")]
        public float postTransitionLockDuration = 1.0f;
        [Tooltip("抑制期間中のホイール速度倍率 (0=完全無視、 1=通常)。 0〜0.2 推奨。")]
        [Range(0f, 1f)] public float postTransitionScrollScale = 0.0f;
        [Tooltip("有効化中 (activeDebuffs に含まれる) ノードの色。 青背景に焼き付く氷シアン。")]
        public Color debuffActiveColor = new Color(0.45f, 0.85f, 1.00f, 1f);
        [Tooltip("未有効化ノードの色。 沈降した深青。 バフ未開放 (水色) よりも一段深く濁る方向。")]
        public Color debuffInactiveColor = new Color(0.25f, 0.35f, 0.50f, 1f);
        [Tooltip("デバフノード間の連結線 (active)。 アイスシアン。")]
        public Color debuffLineActiveColor = new Color(0.50f, 0.85f, 1.00f, 0.85f);
        [Tooltip("デバフノード間の連結線 (inactive)。 深青フェード。")]
        public Color debuffLineInactiveColor = new Color(0.20f, 0.30f, 0.45f, 0.35f);
        [Tooltip("デバフノードの当たり判定一辺 (world)。 0 以下でスプライト矩形に従う。")]
        public float debuffNodeHitSize = 5f;

        [Header("メタデバフ ノードの脈動 (水面波紋シェーダー)")]
        [Tooltip("ON: 中心から外向きに広がる波紋シェーダーを Lv に応じた振幅で適用する。 全 Lv 共通の周期。 明滅なし。")]
        public bool debuffPulseEnabled = true;
        [Tooltip("脈動の周期 (秒)。 1 周期で 1 波が中心から外周へ広がる。")]
        public float debuffRipplePeriod = 1.6f;
        [Tooltip("脈動終了後、 次の脈動が始まるまでの待機時間 (秒)。 0 で連続。 2.0 なら 「波が消えた後 2 秒静止 → 次の波」 のリズム。")]
        public float debuffRippleInterval = 1.0f;
        [Tooltip("Lv1 の波紋強度 (UV ディスプレースメント振幅)。 パーフェクトピクセル時は texelサイズ未満 (32px なら 1/32≒0.031) で 0 px 動作になる。")]
        [Range(0f, 0.30f)] public float debuffRippleStrengthLv1 = 0.031f;
        [Tooltip("Lv10 の波紋強度。 capstone デバフほど大きく歪む。 32px sprite なら 0.125 で約 ±4px の swing。")]
        [Range(0f, 0.30f)] public float debuffRippleStrengthLv10 = 0.031f;
        [Tooltip("波紋シェーダーを使った Material (Custom/MetaDebuffRipple)。 アサインしないと波紋エフェクトが無効。")]
        public Material debuffRippleMaterial;

        public enum TreeMode { Buff, Debuff }

        [Header("描画順")]
        public int lineOrder = 6;
        public int nodeOrder = 8;
        public string sortingLayer = "Default";

        [Header("スクロール")]
        [Tooltip("ホイール感度(world/notch)")]
        public float scrollSpeed = 2.2f;
        [Tooltip("マウスホイールの方向を反転する。 ON で「上回し=下スクロール (= ナチュラル)」 等の入れ替え。")]
        public bool invertScrollDirection = false;
        [Tooltip("画面内で見せる縦の可視半幅(world)。 上下クランプ用。")]
        public float viewHalfHeight = 7.5f;
        [Tooltip("スクロール上下端に追加するパディング(world)。 最上/最下ノードが大きい場合に見切れ防止。")]
        public float scrollEdgePadding = 3f;
        [Range(0.01f, 0.4f)] public float scrollSmooth = 0.10f;

        // --- 内部 ---
        private Transform _bgRoot, _linesRoot, _nodesRoot;
        private Transform _debuffBgRoot, _debuffLinesRoot, _debuffNodesRoot;
        private float _bgHalf;        // バフ背景の縦半幅(world)。 未使用時0。
        private float _bgLim;         // バフ BG スクロールクランプ幅。
        private float _debuffBgHalf;  // デバフ背景の縦半幅(world)。
        private float _debuffBgLim;   // デバフ BG スクロールクランプ幅。
        private readonly List<SpriteRenderer> _nodeSr = new List<SpriteRenderer>();
        private readonly List<SpriteButton> _nodeBtn = new List<SpriteButton>();
        private readonly List<Collider2D> _nodeCol = new List<Collider2D>();
        private readonly List<SpriteRenderer> _hitDebugSr = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _debuffNodeSr = new List<SpriteRenderer>();
        private readonly List<BoxCollider2D> _debuffNodeCol = new List<BoxCollider2D>();
        private readonly List<SpriteButton> _debuffNodeBtn = new List<SpriteButton>();
        private readonly List<SpriteRenderer> _debuffLineSr = new List<SpriteRenderer>();

        // モード状態
        private TreeMode _currentMode = TreeMode.Buff;
        private float _overscrollAccum;   // 現在モードの境界を超えた累積 (world)
        private bool _transitioning;
        private float _postTransitionLockUntil; // unscaledTime: この時刻まではホイール入力を抑制
        private MaterialPropertyBlock _debuffMpb; // 波紋シェーダー用

        // ノード情報ウィンドウ
        private GameObject _windowGo;
        private SpriteRenderer _windowSr;
        private TMP_Text _windowTitleText;
        private TMP_Text _windowDescText;
        private bool _windowVisible;
        private int _windowCurrentBuffIdx = -1;   // 現在ウィンドウに表示中のバフノード idx (-1=非表示)
        private int _windowCurrentDebuffLv = 0;   // 現在ウィンドウに表示中のデバフ Lv (0=非表示)
        // デバフモードの独立スクロール状態
        private float _debuffScrollY, _debuffScrollMin, _debuffScrollMax, _debuffScrollVel;
        private float _debuffNodeMinY, _debuffNodeMaxY;
        private readonly List<SpriteRenderer> _bgStarSr = new List<SpriteRenderer>();
        private readonly List<float> _bgStarPhase = new List<float>();
        private readonly List<float> _bgStarBaseA = new List<float>();
        private Sprite _genHitBox;
        private readonly List<SpriteRenderer> _lineSr = new List<SpriteRenderer>();
        private readonly List<Vector2> _pos = new List<Vector2>();
        private Sprite _genNode, _genLine;
        private float _scrollY, _scrollVel, _scrollMin, _scrollMax;
        private float _nodeMinY, _nodeMaxY; // 実測のノード上下端(local)
        private bool _built;

        private bool _initialized;

        private void Awake()
        {
            InitializeOnce();
            gameObject.SetActive(false); // 既定は閉じておく（メニューから開く）
        }

        /// <summary>
        /// 重い初期化（TMP 差し替え、 ノード生成、 各種購読）を一度だけ実行。 Awake と Open の双方から呼ばれて
        /// どちらが先でも安全。 トランジション前に Open が呼ぶと、 transition 中にこの重さが乗らず固まらない。
        /// </summary>
        private void InitializeOnce()
        {
            if (_initialized) return;
            _initialized = true;

            // TMP が未収録グリフを □ で補填するのを止める（mihiPixelmoji_v1 が一部の文字を欠くと表示が汚れる）
            try { TMP_Settings.instance.GetType()
                .GetField("m_MissingGlyphCharacter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(TMP_Settings.instance, 0); } catch { }
            EnsureInfoTextMP();
            BuildIfNeeded();
            if (backButton != null) backButton.Clicked += Close;
            if (MetaProgressManager.Instance != null)
                MetaProgressManager.Instance.OnStateChanged += RefreshStates;
        }

        /// <summary>
        /// シーン上の Info(legacy TextMesh) を TextMeshPro に自動差し替え。
        /// infoText が未割当なら子 "Info" を探す。 すでに TMP なら font だけ差し替え。
        /// </summary>
        private void EnsureInfoTextMP()
        {
            // 1) infoText 未割当なら子 "Info" を探す（TMP_Text コンポーネントを優先）
            if (infoText == null)
            {
                var infoTr = transform.Find("Info");
                if (infoTr != null) infoText = infoTr.GetComponent<TMP_Text>();
            }

            // 2) まだ無ければ TextMesh を捜索 → 同GOから TextMesh/MeshRenderer/Filter を剥がして TextMeshPro を付け直す。
            if (infoText == null)
            {
                var legacy = GetComponentInChildren<TextMesh>(includeInactive: true);
                if (legacy != null)
                {
                    var go = legacy.gameObject;
                    string prevText = legacy.text;
                    Color prevColor = legacy.color;
                    // TextMesh と付随する MR/MF を即時破棄しないと、 TextMeshPro の RequireComponent と競合して NRE になる。
                    DestroyImmediate(legacy);
                    var mr = go.GetComponent<MeshRenderer>(); if (mr != null) DestroyImmediate(mr);
                    var mf = go.GetComponent<MeshFilter>(); if (mf != null) DestroyImmediate(mf);
                    var tmp = go.AddComponent<TextMeshPro>();
                    tmp.text = prevText;
                    tmp.color = prevColor;
                    tmp.alignment = TextAlignmentOptions.MidlineLeft;
                    tmp.fontSize = infoFontSize;
                    tmp.enableWordWrapping = false;
                    infoText = tmp;
                }
            }

            // 3) ピクセルフォントとアライメント/オーバーフロー設定を強制適用（既存 TMP にも適用）
            if (infoText != null)
            {
                if (infoFont != null) infoText.font = infoFont;
                infoText.alignment = TextAlignmentOptions.MidlineLeft;
                infoText.enableWordWrapping = false;
                // overflowMode=Ellipsis だとフォントに無い「…」が □ になるので Overflow に固定
                infoText.overflowMode = TextOverflowModes.Overflow;
                // rich text タグの誤解釈で意図しないグリフが出るのも防ぐ
                infoText.richText = false;
            }

            // 4) 3D TextMeshPro の場合は SortingLayer/Order をスキルツリーの設定に合わせる。
            //    contentCamera がこのレイヤーを描画しないと不可視になる。
            if (infoText is TextMeshPro tmp3d)
            {
                var rend = tmp3d.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (!string.IsNullOrEmpty(sortingLayer)) rend.sortingLayerName = sortingLayer;
                    rend.sortingOrder = nodeOrder + 4; // ノード/線/判定枠より前面
                }
                if (infoText.gameObject.layer != gameObject.layer)
                    infoText.gameObject.layer = gameObject.layer;
            }

            if (debugHideInfo && infoText != null) infoText.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (backButton != null) backButton.Clicked -= Close;
            var mpm = MetaProgressManager.Instance;
            if (mpm != null) mpm.OnStateChanged -= RefreshStates;
        }

        /// <summary>メニューを隠してツリーを開く。 走査線トランジションを挟む。</summary>
        public void Open()
        {
            InitializeOnce(); // 重い初期化を transition より前に終わらせる（コルーチンの dt 大ジャンプ防止）
            BuildIfNeeded();
            var bgm = FindObjectOfType<TitleBgm>();
            if (bgm != null) bgm.SetMode(TitleBgm.BgmMode.SkillTree);
            // 砂塵はスキルツリー画面では消す（夜空風の星座が主役のため）
            ToggleDust(false);
            var dnForHide = FindObjectOfType<TitleDayNight>();
            var trans = FindObjectOfType<LcdTransition>();
            void Swap()
            {
                if (menuToHide != null) menuToHide.SetActive(false);
                HideTitleVisuals(dnForHide); // メニュー側の BG/Title/Buttons スプライトも明示的に消す
                gameObject.SetActive(true);
                RefreshStates();
                ScrollToNext(true);
            }
            if (trans != null) trans.Begin(Swap);
            else Swap();
        }

        /// <summary>ツリーを閉じてメニューへ戻る。 走査線トランジションを挟む。</summary>
        public void Close()
        {
            var bgm = FindObjectOfType<TitleBgm>();
            var dn = FindObjectOfType<TitleDayNight>();
            if (bgm != null)
            {
                var target = (dn != null && dn.mode == TitleDayNight.Mode.Night)
                    ? TitleBgm.BgmMode.Night : TitleBgm.BgmMode.Day;
                bgm.SetMode(target);
            }
            // 砂塵は昼に戻る時だけ復帰（夜なら TitleDayNight が引き続き OFF にする）
            ToggleDust(dn == null || dn.mode == TitleDayNight.Mode.Day);
            // 情報ウィンドウを閉じる
            CloseInfoWindow();
            var trans = FindObjectOfType<LcdTransition>();
            void Swap()
            {
                gameObject.SetActive(false);
                RestoreTitleVisuals(); // 隠していたメニュー側の BG/Title/Buttons を復帰
                if (menuToHide != null) menuToHide.SetActive(true);
            }
            if (trans != null) trans.Begin(Swap);
            else Swap();
        }

        // ----------------------------------------------------------------
        //  メニュー側の SpriteRenderer (TitleDayNight 経由) を一括 ON/OFF
        //  menuToHide の階層外にある背景・タイトル・ボタン群が透けて見える問題対策。
        // ----------------------------------------------------------------
        private static readonly List<GameObject> _hiddenTitleVisuals = new List<GameObject>();

        private static void HideTitleVisuals(TitleDayNight dn)
        {
            _hiddenTitleVisuals.Clear();
            if (dn == null) return;
            if (dn.bgRenderer != null && dn.bgRenderer.gameObject.activeSelf)
            {
                _hiddenTitleVisuals.Add(dn.bgRenderer.gameObject);
                dn.bgRenderer.gameObject.SetActive(false);
            }
            if (dn.slots != null)
            {
                foreach (var s in dn.slots)
                {
                    if (s == null || s.target == null) continue;
                    var go = s.target.gameObject;
                    if (!go.activeSelf) continue;
                    _hiddenTitleVisuals.Add(go);
                    go.SetActive(false);
                }
            }
        }

        private static void RestoreTitleVisuals()
        {
            foreach (var g in _hiddenTitleVisuals) if (g != null) g.SetActive(true);
            _hiddenTitleVisuals.Clear();
        }

        // タイトル空のアトモスフィア（Dust/Firefly/Stars/ShootingStars/Aurora 等）を一括 ON/OFF。
        // active=false: スキルツリー中は全部消す。 active=true: TitleDayNight.Apply で昼夜状態に応じて再評価。
        private static void ToggleDust(bool active)
        {
            if (!active)
            {
                var dusts = FindObjectsOfType<DustField>(includeInactive: true);
                foreach (var d in dusts) if (d != null) d.gameObject.SetActive(false);
                var fireflies = FindObjectsOfType<FireflyField>(includeInactive: true);
                foreach (var f in fireflies) if (f != null) f.gameObject.SetActive(false);
                // TitleDayNight の dayOnly / nightOnly に登録されている全 GameObject も非表示
                var dn = FindObjectOfType<TitleDayNight>();
                if (dn != null)
                {
                    if (dn.dayOnly != null) foreach (var g in dn.dayOnly) if (g != null) g.SetActive(false);
                    if (dn.nightOnly != null) foreach (var g in dn.nightOnly) if (g != null) g.SetActive(false);
                }
            }
            else
            {
                // 復帰は TitleDayNight に再評価させる（昼/夜に応じて Dust/Firefly/Stars/Aurora 等を正しい状態に）
                var dn = FindObjectOfType<TitleDayNight>();
                if (dn != null) dn.Apply();
                else
                {
                    var dusts = FindObjectsOfType<DustField>(includeInactive: true);
                    foreach (var d in dusts) if (d != null) d.gameObject.SetActive(true);
                }
            }
        }

        private void Update()
        {
            if (!_built) return;

            // ホイールスクロール (モード別)。 モード切替直後はスケール抑制 (or 完全無視)。
            float wheel = Input.mouseScrollDelta.y;
            if (invertScrollDirection) wheel = -wheel;
            if (Time.unscaledTime < _postTransitionLockUntil)
            {
                wheel *= Mathf.Clamp01(postTransitionScrollScale);
            }
            if (!_transitioning)
            {
                if (_currentMode == TreeMode.Buff) HandleWheelBuff(wheel);
                else HandleWheelDebuff(wheel);
            }

            // オーバースクロール蓄積の減衰 (ホイール停止時)
            if (Mathf.Abs(wheel) < 0.001f && _overscrollAccum > 0f)
            {
                _overscrollAccum = Mathf.Max(0f, _overscrollAccum - overscrollDecay * Time.unscaledDeltaTime);
            }

            // スクロール適用 (アクティブモードのみ)
            if (_currentMode == TreeMode.Buff)
            {
                float curY = _nodesRoot.localPosition.y;
                float ny = Mathf.SmoothDamp(curY, _scrollY, ref _scrollVel, scrollSmooth, Mathf.Infinity, Time.unscaledDeltaTime);
                _nodesRoot.localPosition = new Vector3(0f, ny, 0f);
                _linesRoot.localPosition = new Vector3(0f, ny, 0f);
                if (_bgRoot != null) _bgRoot.localPosition = new Vector3(0f, BgOffsetBuff(ny), 0f);
            }
            else
            {
                float curY = _debuffNodesRoot.localPosition.y;
                float ny = Mathf.SmoothDamp(curY, _debuffScrollY, ref _debuffScrollVel, scrollSmooth, Mathf.Infinity, Time.unscaledDeltaTime);
                _debuffNodesRoot.localPosition = new Vector3(0f, ny, 0f);
                _debuffLinesRoot.localPosition = new Vector3(0f, ny, 0f);
                if (_debuffBgRoot != null) _debuffBgRoot.localPosition = new Vector3(0f, BgOffsetDebuff(ny), 0f);
            }

            // 次ノードの明滅
            var mpm = MetaProgressManager.Instance;
            int nextIdx = (mpm != null ? mpm.NextLevel : 1) - 1;
            if (nextIdx >= 0 && nextIdx < _nodeSr.Count)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * nextPulseSpeed);
                var c = nextColor; c.a = Mathf.Lerp(0.55f, 1f, k);
                _nodeSr[nextIdx].color = c;
            }

            // デバフノードの波紋脈動 (DEBUFF モード時のみ・色は静止・UV ディスプレースメントのみ)
            if (debuffPulseEnabled && _currentMode == TreeMode.Debuff
                && _debuffNodeSr.Count > 0 && debuffRippleMaterial != null)
            {
                if (_debuffMpb == null) _debuffMpb = new MaterialPropertyBlock();
                int dn = _debuffNodeSr.Count;
                // 1 サイクル = period (波が中心→外周へ広がる) + interval (静止)
                float period = Mathf.Max(0.01f, debuffRipplePeriod);
                float interval = Mathf.Max(0f, debuffRippleInterval);
                float cycle = period + interval;
                float cycleT = Mathf.Repeat(Time.unscaledTime, cycle);
                float phase;
                float strengthMul; // インターバル中は強度 0 で完全静止
                if (cycleT < period)
                {
                    phase = cycleT / period;     // 0 → 1 (波が伝播)
                    strengthMul = 1f;
                }
                else
                {
                    phase = 1f;                   // 安全値 (どこでも band 外)
                    strengthMul = 0f;             // 静止 (歪み 0)
                }
                int phaseId = Shader.PropertyToID("_RipplePhase");
                int strengthId = Shader.PropertyToID("_RippleStrength");
                for (int i = 0; i < dn; i++)
                {
                    float t = (dn > 1) ? (i / (float)(dn - 1)) : 0f; // 0..1 (Lv1→Lv10)
                    float strength = Mathf.Lerp(debuffRippleStrengthLv1, debuffRippleStrengthLv10, t) * strengthMul;
                    var sr = _debuffNodeSr[i];
                    sr.GetPropertyBlock(_debuffMpb);
                    _debuffMpb.SetFloat(phaseId, phase);
                    _debuffMpb.SetFloat(strengthId, strength);
                    sr.SetPropertyBlock(_debuffMpb);
                    // スケールは必ず (1,1,1) — 旧 scale 脈動を撤廃
                    var tr = sr.transform;
                    if (tr.localScale.x != 1f) tr.localScale = Vector3.one;
                }
            }

            // 背景星の明滅（α サイン波）
            if (backgroundStarTwinkle > 0.001f && _bgStarSr.Count > 0)
            {
                float now = Time.unscaledTime;
                for (int i = 0; i < _bgStarSr.Count; i++)
                {
                    var sr = _bgStarSr[i]; if (sr == null) continue;
                    float kk = 0.5f + 0.5f * Mathf.Sin(now * backgroundStarTwinkleSpeed + _bgStarPhase[i]);
                    float a = Mathf.Clamp01(_bgStarBaseA[i] + (kk - 0.5f) * 2f * backgroundStarTwinkle * _bgStarBaseA[i]);
                    var c = sr.color; c.a = a; sr.color = c;
                }
            }
        }

        // ============================================================
        //  生成
        // ============================================================

        private void BuildIfNeeded()
        {
            if (_built) return;
            _built = true;

            _bgRoot = MakeChild("Bg");
            _linesRoot = MakeChild("Lines");
            _nodesRoot = MakeChild("Nodes");

            int n = MetaBuffTrack.TotalSteps;

            // 背景（縦長PNG）を中央に。 ツリーとは別レートでスクロール（視差）。
            if (backgroundSprite != null)
            {
                _bgHalf = backgroundSprite.bounds.size.y * 0.5f;
                _bgLim = Mathf.Max(0f, _bgHalf - contentHalfHeight); // 背景が画面を埋める範囲
                var bgGo = new GameObject("background");
                bgGo.transform.SetParent(_bgRoot, false);
                bgGo.layer = gameObject.layer;
                bgGo.transform.localPosition = Vector3.zero; // 中央。 _bgRoot ごとスクロール。
                var bsr = bgGo.AddComponent<SpriteRenderer>();
                bsr.sprite = backgroundSprite;
                bsr.sortingOrder = backgroundOrder;
                if (!string.IsNullOrEmpty(sortingLayer)) bsr.sortingLayerName = sortingLayer;

                if (fitToBackground && n > 1)
                {
                    baseY = -_bgHalf + bottomMargin;
                    float topY = _bgHalf - topMargin;
                    dyPerNode = (topY - baseY) / (n - 1);
                }
            }

            // 自動フィットしない場合は authored の dyPerNode を使い、 ツリー全体を中央に。
            if (!fitToBackground && n > 1)
                baseY = -((n - 1) * dyPerNode) * 0.5f;

            // 背景星（_bgRoot 配下に散布＝背景と一緒にゆっくりスクロール）
            BuildBackgroundStars();

            ComputePositions(n);

            // 線（連続ノード間）
            for (int i = 0; i < n - 1; i++)
            {
                var lsr = MakeLine(_pos[i], _pos[i + 1]);
                _lineSr.Add(lsr);
            }

            // ノード
            var rng = new System.Random(seed ^ 0x5bd1e995);
            for (int i = 0; i < n; i++)
            {
                var step = MetaBuffTrack.Get(i + 1);
                bool major = step != null && step.isMajor;
                var go = new GameObject("node_" + (i + 1) + (major ? "_major" : ""));
                go.transform.SetParent(_nodesRoot, false);
                go.layer = gameObject.layer;
                go.transform.localPosition = new Vector3(_pos[i].x, _pos[i].y, 0f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PickNodeSprite(step, major);
                sr.sortingOrder = nodeOrder + (major ? 1 : 0);
                if (!string.IsNullOrEmpty(sortingLayer)) sr.sortingLayerName = sortingLayer;
                _nodeSr.Add(sr);

                // クリック判定（液晶内＝LcdPointer 駆動）。 見た目より広めの判定にできる。
                var col = go.AddComponent<BoxCollider2D>();
                if (nodeHitSize > 0f) col.size = new Vector2(nodeHitSize, nodeHitSize);
                else if (sr.sprite != null) col.size = sr.sprite.bounds.size;
                _nodeCol.Add(col);

                // 当たり判定の可視化（デバッグオーバーレイ）。ノードの子として配置し、 size は collider と同じ。
                if (showHitAreaDebug)
                {
                    float hit = nodeHitSize > 0f ? nodeHitSize : (sr.sprite != null ? sr.sprite.bounds.size.x : 1f);
                    var dgo = new GameObject("hit_debug");
                    dgo.transform.SetParent(go.transform, false);
                    dgo.layer = gameObject.layer;
                    var dsr = dgo.AddComponent<SpriteRenderer>();
                    dsr.sprite = GenHitBox();
                    dsr.sortingOrder = hitDebugOrder;
                    if (!string.IsNullOrEmpty(sortingLayer)) dsr.sortingLayerName = sortingLayer;
                    // 1x1 単位スプライト(PPU=1)を nodeHitSize 倍にスケール＝矩形が実コライダー寸と一致。
                    dgo.transform.localScale = new Vector3(hit, hit, 1f);
                    _hitDebugSr.Add(dsr);
                }
                else _hitDebugSr.Add(null);

                var btn = go.AddComponent<SpriteButton>();
                btn.selfRaycast = false;
                btn.targetRenderer = null; // 色は SkillTreeView が状態で管理（SpriteButton に上書きさせない）
                int idx = i;
                btn.Clicked += () => OnNodeClicked(idx);
                _nodeBtn.Add(btn);
            }

            // 深淵セクション (メタデバフ 10 ノード) を連結
            BuildDebuffSection();

            // ノード情報ウィンドウ (画面下、 非スクロール)
            BuildInfoWindow();

            ComputeScrollClamp(n);
        }

        // ============================================================
        //  ノード情報ウィンドウ
        // ============================================================

        private void BuildInfoWindow()
        {
            if (skilltreeWindowSprite == null) return;
            var go = new GameObject("InfoWindow");
            go.transform.SetParent(transform, false); // this の直下 = スクロールしない
            go.layer = gameObject.layer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = windowOrder;
            if (!string.IsNullOrEmpty(sortingLayer)) sr.sortingLayerName = sortingLayer;
            _windowSr = sr;
            _windowGo = go;

            // タイトル (上段)
            var titleFont = windowFont != null ? windowFont : infoFont;
            _windowTitleText = CreateWindowText(go.transform, "Title", windowTitleFontSize, windowTitleColor, titleFont);
            // 説明 (下段) — 個別フォント。 未指定なら titleFont へフォールバック
            var descFont = windowDescFont != null ? windowDescFont : titleFont;
            _windowDescText  = CreateWindowText(go.transform, "Desc",  windowDescFontSize,  windowDescColor,  descFont);

            // 初期スプライト適用 (位置・サイズ計算込み)
            ApplyWindowSprite(skilltreeWindowSprite);

            go.SetActive(false);
            _windowVisible = false;
        }

        private TMP_Text CreateWindowText(Transform parent, string name, float fontSize, Color color, TMP_FontAsset font)
        {
            var txtGo = new GameObject(name);
            txtGo.transform.SetParent(parent, false);
            txtGo.layer = gameObject.layer;
            var tmp = txtGo.AddComponent<TextMeshPro>();
            tmp.fontSize = fontSize;
            tmp.font = font;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.richText = false;
            tmp.color = color;
            var renderer = tmp.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = windowOrder + 1;
                if (!string.IsNullOrEmpty(sortingLayer))
                    renderer.sortingLayerName = sortingLayer;
            }
            return tmp;
        }

        /// <summary>ウィンドウ sprite を切り替えてレイアウトを再計算する (BUFF / DEBUFF 用の sprite 差し替え時に呼ぶ)。</summary>
        private void ApplyWindowSprite(Sprite sprite)
        {
            if (_windowSr == null || _windowGo == null || sprite == null) return;
            if (_windowSr.sprite == sprite) return; // 既に適用済

            _windowSr.sprite = sprite;

            // ピボット補償 + 横方向アンカー + ピクセル整数スナップで配置を再計算
            Vector3 bc = sprite.bounds.center;
            float windowH = sprite.bounds.size.y;
            float windowW = sprite.bounds.size.x;
            float targetCenterY = -viewHalfHeight + windowH * 0.5f + windowBottomMargin;

            float viewportHalfW = MeasureViewportHalfWidth();
            float viewportFullW = viewportHalfW * 2f;

            float targetCenterX;
            switch (windowHorizontalAlign)
            {
                case WindowHorizontalAlign.LeftEdge:
                    targetCenterX = -viewportHalfW + windowW * 0.5f; break;
                case WindowHorizontalAlign.RightEdge:
                    targetCenterX = viewportHalfW - windowW * 0.5f; break;
                default:
                    targetCenterX = 0f; break;
            }
            const float windowPPU = 8f;
            targetCenterX = Mathf.Round(targetCenterX * windowPPU) / windowPPU;

            _windowGo.transform.localPosition = new Vector3(targetCenterX - bc.x, targetCenterY - bc.y, 0f);

            Debug.Log(
                $"[SkillTreeView] WindowSprite={sprite.name} 幅={windowW:0.00} / LCD={viewportFullW:0.00} "
                + $"(±{viewportHalfW:0.00}) / align={windowHorizontalAlign}");
            if (windowW > viewportFullW + 0.01f)
            {
                float total = windowW - viewportFullW;
                string detail;
                switch (windowHorizontalAlign)
                {
                    case WindowHorizontalAlign.LeftEdge:
                        detail = $"右端が {total:0.00} world 切れます。"; break;
                    case WindowHorizontalAlign.RightEdge:
                        detail = $"左端が {total:0.00} world 切れます。"; break;
                    default:
                        detail = $"両端 {total * 0.5f:0.00} world ずつ切れます。"; break;
                }
                Debug.LogWarning($"[SkillTreeView] {sprite.name} が LCD 可視幅を超えています。 {detail}");
            }

            // タイトル / 説明 を 2 段で配置: 内側高さを比率で分割。
            float innerW = Mathf.Max(0.1f, windowW - 2f * windowTextPadding.x);
            float innerH = Mathf.Max(0.1f, windowH - 2f * windowTextPadding.y);
            float gap = Mathf.Max(0f, windowTitleDescGap);
            float titleH = Mathf.Max(0.1f, (innerH - gap) * Mathf.Clamp(windowTitleAreaRatio, 0.05f, 0.95f));
            float descH = Mathf.Max(0.1f, innerH - titleH - gap);
            Debug.Log($"[SkillTreeView] レイアウト: windowW×H={windowW:0.0}×{windowH:0.0} inner={innerW:0.0}×{innerH:0.0} "
                + $"titleH={titleH:0.2f} descH={descH:0.2f} (titleFontSize={windowTitleFontSize}, descFontSize={windowDescFontSize})");
            // ローカル中心: 0,0 が sprite 中心。 内側上端 = +innerH/2、 内側下端 = -innerH/2。
            if (_windowTitleText != null)
            {
                _windowTitleText.fontSize = windowTitleFontSize;
                _windowTitleText.color = windowTitleColor;
                var titleFontNow = windowFont != null ? windowFont : infoFont;
                if (titleFontNow != null && _windowTitleText.font != titleFontNow) _windowTitleText.font = titleFontNow;
                var rt = _windowTitleText.rectTransform;
                rt.sizeDelta = new Vector2(innerW, titleH);
                // タイトル中心 = 内側上端 - titleH/2
                rt.localPosition = new Vector3(0f, innerH * 0.5f - titleH * 0.5f, 0f);
            }
            if (_windowDescText != null)
            {
                _windowDescText.fontSize = windowDescFontSize;
                _windowDescText.color = windowDescColor;
                var descFontNow = windowDescFont != null ? windowDescFont : (windowFont != null ? windowFont : infoFont);
                if (descFontNow != null && _windowDescText.font != descFontNow) _windowDescText.font = descFontNow;
                var rt = _windowDescText.rectTransform;
                rt.sizeDelta = new Vector2(innerW, descH);
                // 説明中心 = 内側上端 - titleH - gap - descH/2
                rt.localPosition = new Vector3(0f,
                    innerH * 0.5f - titleH - Mathf.Max(0f, windowTitleDescGap) - descH * 0.5f, 0f);
            }
        }

        private float MeasureViewportHalfWidth()
        {
            // 1) LcdScreen の contentCamera が見つかればそれを優先
            var lcd = FindObjectOfType<UI.Lcd.LcdScreen>();
            if (lcd != null && lcd.contentCamera != null)
            {
                var cam = lcd.contentCamera;
                if (cam.orthographic) return cam.orthographicSize * cam.aspect;
            }
            // 2) フォールバック: contentHalfHeight (BG クランプ用) × 想定アスペクト
            return contentHalfHeight * 1.333f;
        }

        private void ShowBuffInfo(int idx)
        {
            if (_windowGo == null || _windowTitleText == null || _windowDescText == null) return;
            ApplyWindowSprite(skilltreeWindowSprite);
            int level = idx + 1;
            var step = MetaBuffTrack.Get(level);
            if (step == null)
            {
                _windowTitleText.text = "-";
                _windowDescText.text = "-";
            }
            else
            {
                string nodeName = GetBuffNodeName(step.kind);
                string title;
                string desc;
                if (IsCumulativeKind(step.kind))
                {
                    var (nth, total) = ComputeBuffCumulative(step.kind, level);
                    title = $"{nodeName} {ToRoman(nth)}";
                    desc = BuildBuffEffectDesc(step, total);
                }
                else
                {
                    title = nodeName;
                    desc = step.DisplayLabel;
                }
                _windowTitleText.text = FilterToFont(title, _windowTitleText.font);
                _windowDescText.text = FilterToFont(desc, _windowDescText.font);
            }
            _windowGo.SetActive(true);
            _windowVisible = true;
            _windowCurrentBuffIdx = idx;
            _windowCurrentDebuffLv = 0;
        }

        private void ShowDebuffInfo(int lv)
        {
            if (_windowGo == null || _windowTitleText == null || _windowDescText == null) return;
            ApplyWindowSprite(skilltreeDebuffWindowSprite != null ? skilltreeDebuffWindowSprite : skilltreeWindowSprite);
            // デバフ側専用のタイトル色を上書き (ApplyWindowSprite では BUFF 用の windowTitleColor が入る)
            _windowTitleText.color = windowDebuffTitleColor;
            var mpm = MetaProgressManager.Instance;
            bool active = mpm != null && mpm.State.HasDebuff((MetaDebuffLevel)lv);
            string name = DebuffName(lv);
            string desc = DebuffDescription(lv);
            string status = active ? "[有効化中]" : "[無効]";
            _windowTitleText.text = FilterToFont(name, _windowTitleText.font);
            _windowDescText.text = FilterToFont($"{desc}  {status}", _windowDescText.font);
            _windowGo.SetActive(true);
            _windowVisible = true;
            _windowCurrentBuffIdx = -1;
            _windowCurrentDebuffLv = lv;
        }

        // ----------------------------------------------------------------
        //  バフノードの表示用ヘルパー
        // ----------------------------------------------------------------

        private static string GetBuffNodeName(MetaBuffKind kind)
        {
            switch (kind)
            {
                case MetaBuffKind.Hp:                       return "頑強";
                case MetaBuffKind.Gold:                     return "富";
                case MetaBuffKind.OutgoingDamagePct:        return "痛撃";
                case MetaBuffKind.StartMaterial:            return "余剰在庫";
                case MetaBuffKind.CombatGoldBonus:          return "追いはぎ";
                case MetaBuffKind.DamageReduce:             return "防御術";
                case MetaBuffKind.BossExtraNormal:          return "戦利品活用";
                case MetaBuffKind.HopeLossReduce:           return "折れぬ心";
                case MetaBuffKind.StartingPassiveItem:      return "事前の備え";
                case MetaBuffKind.ShopRobberyUnlock:        return "値下げ交渉";
                case MetaBuffKind.CritLevelUp:              return "手応えアリ";
                case MetaBuffKind.DiceTotal:                return "訓練の成果";
                case MetaBuffKind.LastStandHpLossDisable:   return "立ち上がる意志";
                case MetaBuffKind.BossExtraRare:            return "戦利品活用+";
                case MetaBuffKind.BossRestHealAndUpgrade:   return "準備万端";
                case MetaBuffKind.CritDamageBonus:          return "天与の剣";
                default: return kind.ToString();
            }
        }

        private static bool IsCumulativeKind(MetaBuffKind kind)
        {
            // 累積される (複数ノードに分散配置される) 種別
            return kind == MetaBuffKind.Hp
                || kind == MetaBuffKind.Gold
                || kind == MetaBuffKind.OutgoingDamagePct;
        }

        /// <summary>指定 Lv 時点でその種別が何番目か (nth) と累計量 (total) を計算する。</summary>
        private static (int nth, int total) ComputeBuffCumulative(MetaBuffKind kind, int upToLevel)
        {
            int nth = 0, total = 0;
            for (int i = 1; i <= upToLevel; i++)
            {
                var s = MetaBuffTrack.Get(i);
                if (s != null && s.kind == kind) { nth++; total += s.amount; }
            }
            return (nth, total);
        }

        /// <summary>下段: 「効果 [累計 X]」 形式。</summary>
        private static string BuildBuffEffectDesc(MetaBuffStep step, int cumulativeTotal)
        {
            switch (step.kind)
            {
                case MetaBuffKind.Hp:
                    return $"最大HP +{step.amount} [累計 +{cumulativeTotal}]";
                case MetaBuffKind.Gold:
                    return $"開幕ゴールド +{step.amount} [累計 +{cumulativeTotal}]";
                case MetaBuffKind.OutgoingDamagePct:
                    return $"与ダメージ +{step.amount}% [累計 +{cumulativeTotal}%]";
                default:
                    return step.DisplayLabel;
            }
        }

        private static string ToRoman(int n)
        {
            if (n <= 0) return "";
            string[] M = { "", "M", "MM", "MMM" };
            string[] C = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
            string[] X = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
            string[] I = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
            return M[n / 1000] + C[(n % 1000) / 100] + X[(n % 100) / 10] + I[n % 10];
        }

        private void CloseInfoWindow()
        {
            if (_windowGo != null) _windowGo.SetActive(false);
            _windowVisible = false;
            _windowCurrentBuffIdx = -1;
            _windowCurrentDebuffLv = 0;
        }

        private static string DebuffName(int lv)
        {
            switch (lv)
            {
                case 1:  return "困窮した商隊";
                case 2:  return "俊敏";
                case 3:  return "向かい風";
                case 4:  return "前途多難";
                case 5:  return "偽の商人";
                case 6:  return "死神の影";
                case 7:  return "補給断絶";
                case 8:  return "絶望的な進軍";
                case 9:  return "鋼の皮膚";
                case 10: return "天変地異";
                default: return "?";
            }
        }

        private static string DebuffDescription(int lv)
        {
            switch (lv)
            {
                case 1:  return "ショップ価格 +25%";
                case 2:  return "敵が各戦闘の最初の被弾を必ず回避";
                case 3:  return "敵への与ダメージ -1";
                case 4:  return "マップ視界が 2マス先で遮断";
                case 5:  return "ショップ 30% で偽商人化 (特殊エリート戦)";
                case 6:  return "3層突入時に恒久デバフ +1";
                case 7:  return "前哨基地の回復上限 = 最大HPの 50%";
                case 8:  return "移動するたびに希望 -1";
                case 9:  return "敵が初回致命傷を HP1 で耐える";
                case 10: return "全戦闘 敵ダメ+100% / 1層恒久デバフ +1 / ラスタン無効";
                default: return "";
            }
        }

        // ============================================================
        //  メタデバフ (深淵) セクション
        // ============================================================

        private void BuildDebuffSection()
        {
            if (!debuffSectionEnabled) return;

            _debuffBgRoot = MakeChild("DebuffBg");
            _debuffLinesRoot = MakeChild("DebuffLines");
            _debuffNodesRoot = MakeChild("DebuffNodes");
            int dn = 10;

            // 専用背景 (バフ BG とは完全分離)
            if (debuffBackgroundSprite != null)
            {
                _debuffBgHalf = debuffBackgroundSprite.bounds.size.y * 0.5f;
                _debuffBgLim = Mathf.Max(0f, _debuffBgHalf - contentHalfHeight);
                var bgGo = new GameObject("debuff_background");
                bgGo.transform.SetParent(_debuffBgRoot, false);
                bgGo.layer = gameObject.layer;
                bgGo.transform.localPosition = Vector3.zero;
                var bsr = bgGo.AddComponent<SpriteRenderer>();
                bsr.sprite = debuffBackgroundSprite;
                bsr.sortingOrder = debuffBackgroundOrder;
                if (!string.IsNullOrEmpty(sortingLayer)) bsr.sortingLayerName = sortingLayer;
            }

            // デバフノード配置: 自分の座標系で中央寄せ (Lv1 が上、 Lv10 が下)
            float totalH = (dn - 1) * Mathf.Max(0.01f, debuffNodeSpacing);
            _debuffNodeMaxY = totalH * 0.5f;
            _debuffNodeMinY = -totalH * 0.5f;

            // 線 (9 本、 _debuffLinesRoot 下に生成)
            for (int i = 0; i < dn - 1; i++)
            {
                float ya = _debuffNodeMaxY - i * debuffNodeSpacing;
                float yb = _debuffNodeMaxY - (i + 1) * debuffNodeSpacing;
                var lsr = MakeLineUnder(_debuffLinesRoot, new Vector2(0f, ya), new Vector2(0f, yb));
                _debuffLineSr.Add(lsr);
            }

            // 10 ノード (直線等間隔)
            for (int i = 0; i < dn; i++)
            {
                float y = _debuffNodeMaxY - i * debuffNodeSpacing;
                var go = new GameObject("debuff_node_" + (i + 1));
                go.transform.SetParent(_debuffNodesRoot, false);
                go.layer = gameObject.layer;
                go.transform.localPosition = new Vector3(0f, y, 0f);

                var sr = go.AddComponent<SpriteRenderer>();
                Sprite assigned = PickDebuffIcon(i + 1);
                sr.sprite = assigned != null ? assigned : (nodeMajorSprite != null ? nodeMajorSprite : GenNode());
                sr.sortingOrder = nodeOrder + 1;
                if (!string.IsNullOrEmpty(sortingLayer)) sr.sortingLayerName = sortingLayer;
                // 波紋シェーダー Material をアサイン (未指定なら既定 sprite material のまま)
                if (debuffRippleMaterial != null) sr.sharedMaterial = debuffRippleMaterial;
                _debuffNodeSr.Add(sr);

                var col = go.AddComponent<BoxCollider2D>();
                if (debuffNodeHitSize > 0f) col.size = new Vector2(debuffNodeHitSize, debuffNodeHitSize);
                else if (sr.sprite != null) col.size = sr.sprite.bounds.size;
                _debuffNodeCol.Add(col);

                var btn = go.AddComponent<SpriteButton>();
                btn.selfRaycast = false;
                btn.targetRenderer = null;
                int lv = i + 1;
                btn.Clicked += () => OnDebuffClicked(lv);
                _debuffNodeBtn.Add(btn);
            }

            // デバフモードのスクロール範囲
            // 上端 (Lv1) は通常 pad のみ、 下端 (Lv10) には追加で debuffEndExtraScroll を加算 → 背景下端を見せられる
            float pad = Mathf.Max(0f, scrollEdgePadding);
            float endExtra = Mathf.Max(0f, debuffEndExtraScroll);
            _debuffScrollMax = -viewHalfHeight - _debuffNodeMinY + pad + endExtra;
            _debuffScrollMin = viewHalfHeight - _debuffNodeMaxY - pad;
            _debuffScrollY = _debuffScrollMin; // 初期位置: 上端 (Lv1) 表示

            // 既定は BUFF モード = デバフ側を非表示
            SetDebuffSectionVisible(false);
        }

        private Sprite PickDebuffIcon(int lv)
        {
            switch (lv)
            {
                case 1:  return iconDebuffLv1;
                case 2:  return iconDebuffLv2;
                case 3:  return iconDebuffLv3;
                case 4:  return iconDebuffLv4;
                case 5:  return iconDebuffLv5;
                case 6:  return iconDebuffLv6;
                case 7:  return iconDebuffLv7;
                case 8:  return iconDebuffLv8;
                case 9:  return iconDebuffLv9;
                case 10: return iconDebuffLv10;
                default: return null;
            }
        }

        private SpriteRenderer MakeLineUnder(Transform parent, Vector2 a, Vector2 b)
        {
            // _linesRoot ではなく指定親に作る MakeLine 派生 (デバフ線用)
            var prev = _linesRoot;
            _linesRoot = parent;
            try { return MakeLine(a, b); }
            finally { _linesRoot = prev; }
        }

        private void SetDebuffSectionVisible(bool visible)
        {
            if (_debuffBgRoot != null) _debuffBgRoot.gameObject.SetActive(visible);
            if (_debuffLinesRoot != null) _debuffLinesRoot.gameObject.SetActive(visible);
            if (_debuffNodesRoot != null) _debuffNodesRoot.gameObject.SetActive(visible);
        }

        private void SetBuffSectionVisible(bool visible)
        {
            if (_bgRoot != null) _bgRoot.gameObject.SetActive(visible);
            if (_linesRoot != null) _linesRoot.gameObject.SetActive(visible);
            if (_nodesRoot != null) _nodesRoot.gameObject.SetActive(visible);
        }

        private void EnterMode(TreeMode mode)
        {
            _currentMode = mode;
            _overscrollAccum = 0f;
            // モード切替直後の暴発防止: 一定時間ホイール入力を抑制
            _postTransitionLockUntil = Time.unscaledTime + Mathf.Max(0f, postTransitionLockDuration);
            // モード切替時は情報ウィンドウを閉じる
            CloseInfoWindow();
            if (mode == TreeMode.Buff)
            {
                SetBuffSectionVisible(true);
                SetDebuffSectionVisible(false);
                _scrollY = _scrollMax; // BUFF に戻る時は下端 (Lv1) からスタート
            }
            else
            {
                SetBuffSectionVisible(false);
                SetDebuffSectionVisible(true);
                _debuffScrollY = _debuffScrollMin; // DEBUFF 入る時は上端 (Lv1) からスタート
            }
            RefreshStates();
        }

        private void OnDebuffClicked(int lv)
        {
            // 同じノードを再クリック → ウィンドウを閉じる (トグルは発生させない)
            if (_windowVisible && _windowCurrentDebuffLv == lv) { CloseInfoWindow(); return; }

            // トグル + ウィンドウ表示
            var mpm = MetaProgressManager.Instance;
            if (mpm != null)
            {
                bool currentlyActive = mpm.State.HasDebuff((MetaDebuffLevel)lv);
                mpm.ToggleDebuff((MetaDebuffLevel)lv, !currentlyActive);
                // OnStateChanged → RefreshStates 経由で色更新
            }
            ShowDebuffInfo(lv);
        }

        private void RefreshDebuffStates()
        {
            var mpm = MetaProgressManager.Instance;
            if (mpm == null) return;
            for (int i = 0; i < _debuffNodeSr.Count; i++)
            {
                bool active = mpm.State.HasDebuff((MetaDebuffLevel)(i + 1));
                _debuffNodeSr[i].color = active ? debuffActiveColor : debuffInactiveColor;
            }
            for (int i = 0; i < _debuffLineSr.Count; i++)
            {
                // 線 i は debuff Lv(i+1) → Lv(i+2)。 i+1 が active なら点灯。
                bool lit = mpm.State.HasDebuff((MetaDebuffLevel)(i + 1));
                _debuffLineSr[i].color = lit ? debuffLineActiveColor : debuffLineInactiveColor;
            }
        }

        // 背景星を _bgRoot 配下に散布。 4スプライト（暗→明）から重み付き抽選。 ピクセル格子へスナップ。
        private void BuildBackgroundStars()
        {
            if (_bgRoot == null) return;
            if (backgroundStarSprites == null || backgroundStarSprites.Length == 0) return;
            if (backgroundStarCount <= 0) return;

            int validCount = 0;
            float weightSum = 0f;
            for (int i = 0; i < backgroundStarSprites.Length; i++)
            {
                if (backgroundStarSprites[i] == null) continue;
                validCount++;
                float w = (backgroundStarWeights != null && i < backgroundStarWeights.Length) ? backgroundStarWeights[i] : 1f;
                weightSum += Mathf.Max(0f, w);
            }
            if (validCount == 0 || weightSum <= 0f) return;

            var rng = new System.Random(unchecked(seed ^ (int)0x9747b28c));
            int ppu = Mathf.Max(1, backgroundStarPPU);
            float halfX = backgroundStarArea.x * 0.5f;
            float halfY = backgroundStarArea.y * 0.5f;
            float minD = Mathf.Max(0f, backgroundStarMinDistance);
            float minDSq = minD * minD;
            int maxAttempts = Mathf.Max(1, backgroundStarMaxAttempts);
            var placed = new List<Vector2>(backgroundStarCount);

            for (int i = 0; i < backgroundStarCount; i++)
            {
                // 重み付き抽選
                float r = (float)rng.NextDouble() * weightSum;
                Sprite chosen = null;
                for (int k = 0; k < backgroundStarSprites.Length; k++)
                {
                    var sp = backgroundStarSprites[k]; if (sp == null) continue;
                    float w = (backgroundStarWeights != null && k < backgroundStarWeights.Length) ? backgroundStarWeights[k] : 1f;
                    w = Mathf.Max(0f, w);
                    if (r < w) { chosen = sp; break; }
                    r -= w;
                }
                if (chosen == null) chosen = backgroundStarSprites[0];

                // 最低距離を満たす位置を試行（poissonish）。 maxAttempts 超えたらスキップ＝総数が減ることがある。
                float px = 0f, py = 0f;
                bool ok = false;
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    px = (float)(rng.NextDouble() * 2.0 - 1.0) * halfX;
                    py = (float)(rng.NextDouble() * 2.0 - 1.0) * halfY;
                    px = Mathf.Round(px * ppu) / ppu;
                    py = Mathf.Round(py * ppu) / ppu;
                    if (minDSq <= 0f) { ok = true; break; }
                    bool tooClose = false;
                    for (int j = 0; j < placed.Count; j++)
                    {
                        float dx = placed[j].x - px;
                        float dy = placed[j].y - py;
                        if (dx * dx + dy * dy < minDSq) { tooClose = true; break; }
                    }
                    if (!tooClose) { ok = true; break; }
                }
                if (!ok) continue;
                placed.Add(new Vector2(px, py));

                var go = new GameObject("bgstar_" + i);
                go.transform.SetParent(_bgRoot, false);
                go.layer = gameObject.layer;
                go.transform.localPosition = new Vector3(px, py, 0f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = chosen;
                sr.sortingOrder = backgroundStarOrder;
                if (!string.IsNullOrEmpty(sortingLayer)) sr.sortingLayerName = sortingLayer;
                Color c = Color.white; c.a = 1f;
                sr.color = c;
                _bgStarSr.Add(sr);
                _bgStarPhase.Add((float)(rng.NextDouble() * Mathf.PI * 2.0));
                _bgStarBaseA.Add(1f);
            }
        }

        private void ComputePositions(int n)
        {
            _pos.Clear();
            if (uniformSpacing) ComputeUniform(n);
            else ComputeOrganic(n);

            // 終端2ノード調整: 最後の2ノードを中央寄せ + 最終ノードを少し離す
            if (centerFinalTwo && n >= 2)
            {
                int last = n - 1, prev = n - 2;
                var pPrev = _pos[prev]; pPrev.x = 0f;
                float baseGap = uniformSpacing ? Mathf.Max(0.01f, uniformGap)
                                                : Mathf.Max(0.01f, dyPerNode);
                var pLast = _pos[last];
                pLast.x = 0f;
                pLast.y = pPrev.y + baseGap + Mathf.Max(0f, finalNodeExtraGap);
                _pos[prev] = pPrev;
                _pos[last] = pLast;
            }

            // 実測の上下端を記録（スクロールのクランプ/センタリングに使用）。
            _nodeMinY = float.MaxValue; _nodeMaxY = float.MinValue;
            foreach (var p in _pos) { if (p.y < _nodeMinY) _nodeMinY = p.y; if (p.y > _nodeMaxY) _nodeMaxY = p.y; }
            if (_pos.Count == 0) { _nodeMinY = _nodeMaxY = 0f; }
        }

        // 有機モード: 上昇トレンド＋揺らぎ＋最小間隔。 距離はばらつく。
        private void ComputeOrganic(int n)
        {
            var rng = new System.Random(seed);
            float span = (n > 1) ? (n - 1) * dyPerNode : 0f;
            float halfX = amplitude;
            float x = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (n > 1) ? (float)i / (n - 1) : 1f;
                float y = baseY + t * span + (float)(rng.NextDouble() * 2.0 - 1.0) * verticalJitter;
                if (i == 0) y = baseY;
                else if (i == n - 1) y = baseY + span;
                else y = Mathf.Clamp(y, baseY, baseY + span);

                float step = (float)(rng.NextDouble() * 2.0 - 1.0) * xStep;
                x = x * (1f - centerPull) + step;
                x = Mathf.Clamp(x, -halfX, halfX);

                Vector2 cand = new Vector2(x, y);
                if (i > 0 && i < n - 1)
                {
                    Vector2 prev = _pos[i - 1];
                    Vector2 d = cand - prev;
                    float dist = d.magnitude;
                    if (dist < minNodeGap)
                    {
                        if (dist < 1e-4f) d = new Vector2(0f, 1f);
                        cand = prev + d.normalized * minNodeGap;
                        cand.x = Mathf.Clamp(cand.x, -halfX, halfX);
                        cand.y = Mathf.Clamp(cand.y, baseY, baseY + span);
                    }
                }
                x = cand.x;
                _pos.Add(cand);
            }
        }

        // 均一モード: 全セグメントの距離を uniformGap で一定に。 横は揺れ、 縦は √(L²-Δx²) で補う＝必ず上昇。
        private void ComputeUniform(int n)
        {
            var rng = new System.Random(seed);
            float L = Mathf.Max(0.01f, uniformGap);
            float halfX = amplitude;
            float maxDx = Mathf.Clamp(uniformHorizontalRatio, 0.1f, 0.98f) * L; // |Δx|<L を保証（Δy が実数に）

            float x = 0f, y = 0f;
            _pos.Add(new Vector2(0f, 0f));
            for (int i = 1; i < n; i++)
            {
                float step = (float)(rng.NextDouble() * 2.0 - 1.0) * xStep;
                float nx = x * (1f - centerPull) + step;
                nx = Mathf.Clamp(nx, -halfX, halfX);
                float dx = nx - x;
                if (Mathf.Abs(dx) > maxDx) { dx = Mathf.Sign(dx) * maxDx; nx = x + dx; }
                float dy = Mathf.Sqrt(Mathf.Max(0f, L * L - dx * dx)); // 距離が常に L
                x = nx; y += dy;
                _pos.Add(new Vector2(x, y));
            }

            // 全体を縦中央へ（最初=最下 / 最後=最上）。
            float half = y * 0.5f;
            for (int i = 0; i < _pos.Count; i++) _pos[i] = new Vector2(_pos[i].x, _pos[i].y - half);
        }

        private void ComputeScrollClamp(int n)
        {
            // ツリー（ノード）の実測上下端で、 画面端を行き過ぎないクランプ。 背景は別途 factor で追従。
            float pad = Mathf.Max(0f, scrollEdgePadding);
            _scrollMax = -viewHalfHeight - _nodeMinY + pad; // 最下段を画面下端まで(+pad で更に下にスクロール可)
            _scrollMin = viewHalfHeight - _nodeMaxY - pad;  // 最上段を画面上端まで(-pad で更に上にスクロール可)
            if (_scrollMin > _scrollMax) { float m = (_scrollMin + _scrollMax) * 0.5f; _scrollMin = _scrollMax = m; }
            _scrollY = Mathf.Clamp(_scrollY, _scrollMin, _scrollMax);
        }

        // BUFF モードの BG オフセット。 スクロール上下端で BG 上下端が画面端に一致するよう線形マップ。
        private float BgOffsetBuff(float treeY)
        {
            if (_bgLim <= 0f) return 0f;
            float range = _scrollMax - _scrollMin;
            if (range <= 0.0001f) return 0f;
            float t = Mathf.Clamp01((treeY - _scrollMin) / range);
            return Mathf.Lerp(-_bgLim, _bgLim, t);
        }

        // DEBUFF モードの BG オフセット (BUFF と同形・別ストリップ)。
        private float BgOffsetDebuff(float treeY)
        {
            if (_debuffBgLim <= 0f) return 0f;
            float range = _debuffScrollMax - _debuffScrollMin;
            if (range <= 0.0001f) return 0f;
            float t = Mathf.Clamp01((treeY - _debuffScrollMin) / range);
            return Mathf.Lerp(-_debuffBgLim, _debuffBgLim, t);
        }

        // ----------------------------------------------------------------
        //  ホイール処理 (モード別) + オーバースクロール検出
        // ----------------------------------------------------------------

        private void HandleWheelBuff(float wheel)
        {
            if (Mathf.Abs(wheel) < 0.001f) return;
            float delta = wheel * scrollSpeed;
            float next = _scrollY + delta;
            // 通常スクロール範囲内
            if (next >= _scrollMin && next <= _scrollMax)
            {
                _scrollY = next;
                _overscrollAccum = 0f;
                return;
            }
            // BUFF 下端 (scrollMax) を超える方向に押し続けたら DEBUFF へ遷移
            if (next > _scrollMax && debuffSectionEnabled && _debuffNodeSr.Count > 0)
            {
                _scrollY = _scrollMax;
                _overscrollAccum += (next - _scrollMax);
                if (_overscrollAccum >= overscrollThreshold) BeginTransitionTo(TreeMode.Debuff);
            }
            else
            {
                _scrollY = Mathf.Clamp(next, _scrollMin, _scrollMax);
                _overscrollAccum = 0f;
            }
        }

        private void HandleWheelDebuff(float wheel)
        {
            if (Mathf.Abs(wheel) < 0.001f) return;
            float delta = wheel * scrollSpeed;
            float next = _debuffScrollY + delta;
            if (next >= _debuffScrollMin && next <= _debuffScrollMax)
            {
                _debuffScrollY = next;
                _overscrollAccum = 0f;
                return;
            }
            // DEBUFF 上端 (scrollMin) を超える方向に押し続けたら BUFF へ復帰
            if (next < _debuffScrollMin)
            {
                _debuffScrollY = _debuffScrollMin;
                _overscrollAccum += (_debuffScrollMin - next);
                if (_overscrollAccum >= overscrollThreshold) BeginTransitionTo(TreeMode.Buff);
            }
            else
            {
                _debuffScrollY = Mathf.Clamp(next, _debuffScrollMin, _debuffScrollMax);
                _overscrollAccum = 0f;
            }
        }

        // ----------------------------------------------------------------
        //  モード切替トランジション (LcdTransition があれば使う)
        // ----------------------------------------------------------------

        private void BeginTransitionTo(TreeMode target)
        {
            if (_transitioning || _currentMode == target) return;
            _transitioning = true;
            var trans = FindObjectOfType<LcdTransition>();
            void Swap()
            {
                EnterMode(target);
                _transitioning = false;
            }
            if (trans != null) trans.Begin(Swap);
            else Swap();
        }

        // フォント未収録の文字を取り除く（TMP の □ プレースホルダ抑制）。
        // ホワイトスペース類はそのまま残す（HasCharacter は false を返すことがあるため）。
        // フォント未収録の文字を取り除く（TMP の □ プレースホルダ抑制）。
        private static string FilterToFont(string s, TMP_FontAsset f)
        {
            if (string.IsNullOrEmpty(s) || f == null) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || f.HasCharacter(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        private void ScrollToNext(bool instant)
        {
            var mpm = MetaProgressManager.Instance;
            int nextIdx = (mpm != null ? mpm.NextLevel : 1) - 1;
            nextIdx = Mathf.Clamp(nextIdx, 0, Mathf.Max(0, _pos.Count - 1));
            float targetNodeY = (_pos.Count > 0) ? _pos[nextIdx].y : 0f;
            _scrollY = Mathf.Clamp(-targetNodeY, _scrollMin, _scrollMax);
            if (instant)
            {
                _nodesRoot.localPosition = new Vector3(0f, _scrollY, 0f);
                _linesRoot.localPosition = new Vector3(0f, _scrollY, 0f);
                if (_bgRoot != null) _bgRoot.localPosition = new Vector3(0f, BgOffsetBuff(_scrollY), 0f);
                _scrollVel = 0f;
            }
        }

        // ============================================================
        //  状態
        // ============================================================

        private void OnNodeClicked(int idx)
        {
            // 同じノードを再クリック → ウィンドウを閉じる
            if (_windowVisible && _windowCurrentBuffIdx == idx) { CloseInfoWindow(); return; }

            // クリックは情報ウィンドウを開くのみ (購入は別 UI でトリガする想定)
            ShowBuffInfo(idx);
        }

        private void RefreshStates()
        {
            var mpm = MetaProgressManager.Instance;
            int cur = mpm != null ? mpm.State.currentLevel : 0;
            int nextLevel = cur + 1;

            for (int i = 0; i < _nodeSr.Count; i++)
            {
                int level = i + 1;
                bool unlocked = level <= cur;
                bool isNext = level == nextLevel;
                // 位置に関係なく状態で色を決める: 解放=白 / 次=明滅(Update) / 未開放=水色。
                _nodeSr[i].color = unlocked ? unlockedColor : (isNext ? nextColor : lockedColor);
                // 全ノードクリック可 (情報ウィンドウ表示用)。 購入は OnNodeClicked 内で「次の1個のみ」 判定。
                if (i < _nodeCol.Count && _nodeCol[i] != null) _nodeCol[i].enabled = true;
                if (i < _hitDebugSr.Count && _hitDebugSr[i] != null)
                    _hitDebugSr[i].color = isNext ? hitEnabledColor : hitDisabledColor;
            }
            for (int i = 0; i < _lineSr.Count; i++)
            {
                // 線 i は node i→i+1。 i+1 段(level=i+2)まで解放されたら点灯。
                bool lit = (i + 1) < cur + 1; // node i(level i+1) が解放済み
                _lineSr[i].color = lit ? lineUnlockedColor : lineLockedColor;
            }

            // 深淵セクション (メタデバフ) の状態も同時更新
            RefreshDebuffStates();

            if (infoText != null)
            {
                if (mpm != null && mpm.IsTrackComplete)
                    infoText.text = FilterToFont("星 " + mpm.State.tokens + "   全解放", infoText.font);
                else if (mpm != null)
                {
                    var step = MetaBuffTrack.Get(mpm.NextLevel);
                    string label = step != null ? step.DisplayLabel : "-";
                    infoText.text = FilterToFont("星 " + mpm.State.tokens + "   次 " + mpm.NextCost + " : " + label, infoText.font);
                }
            }
        }

        // ============================================================
        //  ヘルパー（生成物）
        // ============================================================

        private Transform MakeChild(string n)
        {
            var ex = transform.Find(n);
            if (ex != null) return ex;
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            return go.transform;
        }

        private SpriteRenderer MakeLine(Vector2 a, Vector2 b)
        {
            var go = new GameObject("line");
            go.transform.SetParent(_linesRoot, false);
            go.layer = gameObject.layer;

            // ピクセルパーフェクト: ドット格子(1dot=1/PPU world, PPU=8 → 0.125u)に endpoints をスナップし、
            // Bresenham でラスタライズして専用テクスチャを生成する。 回転/任意スケールは使わない。
            const float ppu = 8f;
            const float dot = 1f / ppu;
            int x0 = Mathf.RoundToInt(a.x * ppu);
            int y0 = Mathf.RoundToInt(a.y * ppu);
            int x1 = Mathf.RoundToInt(b.x * ppu);
            int y1 = Mathf.RoundToInt(b.y * ppu);

            // Bresenham（始点→終点を整数格子でつなぐ）
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int cx = x0, cy = y0;
            int minX = cx, maxX = cx, minY = cy, maxY = cy;
            var pts = new List<Vector2Int>();
            pts.Add(new Vector2Int(cx, cy));
            int guard = 0;
            while (!(cx == x1 && cy == y1) && guard++ < 8192)
            {
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; cx += sx; }
                if (e2 <= dx) { err += dx; cy += sy; }
                pts.Add(new Vector2Int(cx, cy));
                if (cx < minX) minX = cx; if (cx > maxX) maxX = cx;
                if (cy < minY) minY = cy; if (cy > maxY) maxY = cy;
            }

            int w = maxX - minX + 1;
            int h = maxY - minY + 1;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, name = "GenLinePixel", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            // pts のドットだけ白で埋める（残りは透明 = default Color32(0,0,0,0))
            for (int i = 0; i < pts.Count; i++)
            {
                int ix = pts[i].x - minX;
                int iy = pts[i].y - minY;
                px[iy * w + ix] = new Color32(255, 255, 255, 255);
            }
            tex.SetPixels32(px); tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu);

            // bbox の中心(ドット) → world に戻して GO 配置（drawMode=Simple、回転なし）
            float cxw = (minX + maxX) * 0.5f * dot;
            float cyw = (minY + maxY) * 0.5f * dot;
            go.transform.localPosition = new Vector3(cxw, cyw, 0.1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = lineLockedColor;
            sr.sortingOrder = lineOrder;
            if (!string.IsNullOrEmpty(sortingLayer)) sr.sortingLayerName = sortingLayer;
            return sr;
        }

        // ノード種別に応じてスプライトを選ぶ。 kind 別アイコンが割当済ならそれを、 未割当なら
        // (major→nodeMajorSprite=placeholder / 通常→nodeNormalSprite) にフォールバック。
        private Sprite PickNodeSprite(MetaBuffStep step, bool major)
        {
            Sprite s = null;
            if (step != null)
            {
                switch (step.kind)
                {
                    case MetaBuffKind.Hp:                       s = nodeHpSprite; break;
                    case MetaBuffKind.Gold:                     s = nodeGoldSprite; break;
                    case MetaBuffKind.OutgoingDamagePct:        s = nodeDamagePctSprite; break;
                    case MetaBuffKind.StartMaterial:            s = iconStartMaterial; break;
                    case MetaBuffKind.CombatGoldBonus:          s = iconCombatGoldBonus; break;
                    case MetaBuffKind.DamageReduce:             s = iconDamageReduce; break;
                    case MetaBuffKind.BossExtraNormal:          s = iconBossExtraNormal; break;
                    case MetaBuffKind.HopeLossReduce:           s = iconHopeLossReduce; break;
                    case MetaBuffKind.StartingPassiveItem:      s = iconStartingPassiveItem; break;
                    case MetaBuffKind.ShopRobberyUnlock:        s = iconShopRobberyUnlock; break;
                    case MetaBuffKind.CritLevelUp:              s = iconCritLevelUp; break;
                    case MetaBuffKind.DiceTotal:                s = iconDiceTotal; break;
                    case MetaBuffKind.LastStandHpLossDisable:   s = iconLastStandHpLossDisable; break;
                    case MetaBuffKind.BossExtraRare:            s = iconBossExtraRare; break;
                    case MetaBuffKind.BossRestHealAndUpgrade:   s = iconBossRestHealAndUpgrade; break;
                    case MetaBuffKind.CritDamageBonus:          s = iconCritDamageBonus; break;
                }
            }
            if (s != null) return s;
            if (major) return nodeMajorSprite != null ? nodeMajorSprite : GenNode();
            return nodeNormalSprite != null ? nodeNormalSprite : GenNode();
        }

        // コード生成の星ノード（小さな十字＋中心の点）。 PNG 未指定時のフォールバック。
        private Sprite GenNode()
        {
            if (_genNode != null) return _genNode;
            int s = 7; // 7x7 px
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "GenStar" };
            var px = new Color32[s * s];
            for (int k = 0; k < px.Length; k++) px[k] = new Color32(255, 255, 255, 0);
            void Set(int x, int y, byte a) { if (x >= 0 && x < s && y >= 0 && y < s) px[y * s + x] = new Color32(255, 255, 255, a); }
            int c = s / 2;
            Set(c, c, 255);
            Set(c - 1, c, 200); Set(c + 1, c, 200); Set(c, c - 1, 200); Set(c, c + 1, 200);
            Set(c - 2, c, 110); Set(c + 2, c, 110); Set(c, c - 2, 110); Set(c, c + 2, 110);
            Set(c - 3, c, 50); Set(c + 3, c, 50); Set(c, c - 3, 50); Set(c, c + 3, 50);
            tex.SetPixels32(px); tex.Apply();
            _genNode = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 32f);
            return _genNode;
        }

        // 当たり判定可視化用の 1x1 白スプライト(PPU=1)。 localScale で hitSize に合わせて拡大する。
        private Sprite GenHitBox()
        {
            if (_genHitBox != null) return _genHitBox;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "GenHitBox", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(new[] { new Color32(255, 255, 255, 255) }); tex.Apply();
            _genHitBox = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _genHitBox;
        }

        // コード生成の 1px 白線。 横長 drawMode=Sliced 用に左右1pxボーダー。
        private Sprite GenLine()
        {
            if (_genLine != null) return _genLine;
            int w = 4, h = 1;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "GenLine", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int k = 0; k < px.Length; k++) px[k] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            _genLine = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 32f, 0,
                SpriteMeshType.FullRect, new Vector4(1, 0, 1, 0)); // 左右1pxボーダー→Sliced で横に伸ばす
            return _genLine;
        }
    }
}

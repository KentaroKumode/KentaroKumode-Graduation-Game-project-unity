using UnityEngine;

namespace UI.ClassSelect
{
    /// <summary>
    /// 職業選択 UI のスキン (見た目素材) 登録アセット (2026-07-07)。
    ///
    /// 各要素 (切替タブ/キャラ票/詳細/スタンプ) を 「複数スプライトの重ね (SpriteStack)」 として
    /// 定義できる。 layers の orderOffset が前後の序列 ── 大きいほど手前。 要素はパネルごと
    /// 一体でスライドするため、 重ねたスプライトは常に 1 つの塊として動く。
    ///
    /// 【インポート規約】 スプライトは PPU=8 でインポートすると 1px = 1 モニタードット (4×4 物理px)。
    /// 【作り方】 Tools → ClassSelect → Create Skin Asset (Assets/Resources/ClassSelectSkin.asset)。
    ///            Resources 配下に置くと実行時自動構築 (TitleMenuBootstrap) でも読み込まれる。
    /// 未登録の要素は従来の無地プレースホルダで描画される。
    /// </summary>
    [CreateAssetMenu(menuName = "ClassSelect/Skin", fileName = "ClassSelectSkin")]
    public class ClassSelectSkinAsset : ScriptableObject
    {
        /// <summary>Resources.Load で使うアセット名。</summary>
        public const string ResourceName = "ClassSelectSkin";

        [System.Serializable]
        public class SpriteLayer
        {
            public Sprite sprite;
            [Tooltip("要素中心からのオフセット (モニタードット)")]
            public Vector2 offsetDots;
            [Tooltip("前後の序列。 大きいほど手前 (0〜7 推奨。 テキストは +8 に乗る)")]
            [Range(0, 7)] public int orderOffset;
            public Color tint = Color.white;
        }

        [System.Serializable]
        public class SpriteStack
        {
            public SpriteLayer[] layers;

            public bool HasSprites
            {
                get
                {
                    if (layers == null) return false;
                    foreach (var l in layers)
                        if (l != null && l.sprite != null) return true;
                    return false;
                }
            }
        }

        [Header("背景 (最も奥、 LCD 全面を覆う想定)")]
        [Tooltip("職業選択画面全体の背景スプライト。 231×139 dot 相当が理想。 未設定なら SolidColor 背景 (机色) のまま")]
        public SpriteStack backgroundSkin;

        [Header("切替タブ (2026-07-09 改: 土台 1 + アイコン 4 の重ね方式)")]
        [Tooltip("タブ土台 (4 職業共通、 最初に描画される背景)")]
        public SpriteStack tabBaseSkin;
        [Tooltip("職業別アイコン (剣士/騎士/狂戦士/暗殺者)。 tabBaseSkin と同じ位置に重ねて描画され、 素材内の Y 位置で 4 職業を描き分ける")]
        public SpriteStack[] tabIconSkins = new SpriteStack[4];
        [System.Obsolete("2026-07-09 廃止。 tabBaseSkin + tabIconSkins[] に移行。 未設定なら旧割当は無視される。")]
        [HideInInspector] public SpriteStack[] tabSkins = new SpriteStack[0];

        [Header("キャラ票 (登録票の台紙)")]
        public SpriteStack sheetSkin;

        [Header("肖像 (職業順。 票の肖像枠に表示)")]
        public Sprite[] portraits = new Sprite[4];

        [Header("職業名 (票の名前部分の描き文字。 職業順)")]
        [Tooltip("登録した職業は TMP テキスト (名前+カナ) の代わりにこのスプライトを表示。 未登録はテキストのまま")]
        public Sprite[] nameSprites = new Sprite[4];
        [Tooltip("職業別の位置オフセット (dot)。 ClassSelectView 側の共通オフセットに加算される。 素材ごとの描き位置ズレをここで個別補正")]
        public Vector2[] nameSpriteOffsetsDots = new Vector2[4];

        [Header("ページめくり (職業切替時のキャラ票演出)")]
        [Tooltip("めくりページのコマ。 [0]=1コマ目(めくり始め)、 [1]=2コマ目(めくり終わり)。 " +
                 "両方設定するとタブ切替時にスライド退場の代わりに本めくり演出になる。 未設定なら従来のスライド切替")]
        public Sprite[] pageFlipFrames = new Sprite[2];

        /// <summary>めくり演出が有効か (全コマが設定済み)。</summary>
        public bool HasPageFlip
        {
            get
            {
                if (pageFlipFrames == null || pageFlipFrames.Length < 2) return false;
                for (int i = 0; i < pageFlipFrames.Length; i++)
                    if (pageFlipFrames[i] == null) return false;
                return true;
            }
        }

        [Header("詳細説明タブ")]
        public SpriteStack detailSkin;

        [Header("決定ボタン (スタンプ)")]
        public SpriteStack stampSkin;

        [Header("受理印影 (確定時に票へ落ちる印)")]
        public SpriteStack stampInkSkin;

        public SpriteStack GetTabIconSkin(int i)
            => (tabIconSkins != null && i >= 0 && i < tabIconSkins.Length) ? tabIconSkins[i] : null;

        public Sprite GetPortrait(int i)
            => (portraits != null && i >= 0 && i < portraits.Length) ? portraits[i] : null;

        public Sprite GetNameSprite(int i)
            => (nameSprites != null && i >= 0 && i < nameSprites.Length) ? nameSprites[i] : null;

        public Vector2 GetNameSpriteOffset(int i)
            => (nameSpriteOffsetsDots != null && i >= 0 && i < nameSpriteOffsetsDots.Length)
               ? nameSpriteOffsetsDots[i] : Vector2.zero;
    }
}

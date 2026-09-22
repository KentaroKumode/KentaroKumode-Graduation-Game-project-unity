using UnityEditor;
using UnityEngine;

namespace AutoTest.EditorTools
{
    /// <summary><c>Assets/Resources/Icons/Items/</c> 配下の png へインポート設定を自動適用する。
    ///
    /// <para><b>なぜ自動か。</b> アイコンは 16x16 (武器のみ 32x32) のドット絵で、
    /// 既定のインポート設定 (Bilinear + 圧縮) だと<b>輪郭がぼやけて色も濁る</b>。
    /// 132 枚を手で設定するのは現実的でなく、 しかも 1 枚でも設定漏れがあると
    /// そこだけ見た目が違うという読みにくい不具合になる。 追加される画像にも自動でかかる。</para>
    ///
    /// <para><b>PPU=32 は規約</b> (CLAUDE.md / docs GAME.md §2-3)。 ここで勝手に変えないこと。
    /// 16x16 のアイコンは 0.5 ユニット、 32x32 の武器は 1 ユニットになる。</para>
    ///
    /// <para>対応表は持たない ── ファイル名 = items.json の <c>name</c> (表示名) が唯一の規約
    /// (<c>Tools/icon_audit.py</c> が 1:1 を検算する)。 <b>id ではない</b>:
    /// 絵を描く側は表示名で認識するため (理由は icon_audit.py の冒頭)。</para></summary>
    public sealed class ItemIconImporter : AssetPostprocessor
    {
        private const string TargetDir = "Assets/Resources/Icons/Items/";

        /// <summary>ピクセル規約 (PPU=32)。 <b>スケール倍率で見た目を作らない</b>ため、
        /// 解像度と PPU の対応をここで固定する。</summary>
        public const int PixelsPerUnit = 32;

        private void OnPreprocessTexture()
        {
            if (assetPath == null || !assetPath.StartsWith(TargetDir)) return;
            if (!assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) return;

            var ti = (TextureImporter)assetImporter;
            ti.textureType         = TextureImporterType.Sprite;
            ti.spriteImportMode    = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            ti.filterMode          = FilterMode.Point;   // ドット絵を補間しない
            ti.mipmapEnabled       = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode            = TextureWrapMode.Clamp;
            ti.npotScale           = TextureImporterNPOTScale.None;

            // 圧縮なし。 16x16 が 132 枚でも数十 KB にしかならないので、
            //   容量のために色を潰す理由が無い。
            var settings = ti.GetDefaultPlatformTextureSettings();
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            settings.maxTextureSize = 128;
            ti.SetPlatformTextureSettings(settings);
        }
    }
}

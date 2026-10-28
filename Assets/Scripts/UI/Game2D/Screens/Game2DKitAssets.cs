using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 仮置きの画面・計器に使う共通の素材と描画道具 (2026-10-24)。 枠・ボタン・ダイスの絵は PixelLab (参考画像の切り抜き + パレット抽出) で作った
    /// 仮の絵 (Assets/gui2d/battle_ui/)。 文字は 3×5 のドット字 (英数字だけ)、 アイコンは地図のノードの絵 (17×17) を使う。
    /// </summary>
    public class Game2DKitAssets : MonoBehaviour
    {
        [Tooltip("枠 (9 分割して使う)・押しボタン・大きいボタン・ダイスの面")]
        public Texture2D frameTex, btnTex, goTex, dieTex;
        [Tooltip("PixelLab で作った部品: 行・タブ・チップ・帯の台 / スロット / ランプ (点灯・消灯) / 丸ボタン / ダイアル / ゲージ。 無ければコード描画で代用")]
        public Texture2D rowTex, tabTex, chipTex, bannerTex, slotTex, lampOnTex, lampOffTex, roundTex, dialTex, gaugeTex;
        [Tooltip("ダイスの面 1〜6")] public Texture2D[] diceTex;
        [Tooltip("アイコンのアトラス (48×48 を 8 列)・名前の並び (カンマ区切り)")] public Texture2D iconAtlas; public string iconNames;
        public static Game2DKitAssets Instance { get; private set; }
        private void Awake() { Instance = this; Game2DKit.Reset(); }
        private void OnEnable() { Instance = this; }
    }
}

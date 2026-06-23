using UnityEngine;

namespace UI
{
    /// <summary>
    /// タイトル液晶の「昼/夜」スプライト一式を <see cref="mode"/> で一括差し替えする。
    /// 既存リグ（BG / Title / 各ボタン / 両端カーソル）はそのまま使い、 絵だけ昼夜で切り替える
    /// ＝「全く同じ処理の夜バージョン」。 切替はインスペクター手動（必要なら <see cref="SetMode"/> をコードから呼ぶ）。
    ///
    /// 規約: localScale は触らない（PPU=32）。 差し替えるのは SpriteRenderer.sprite と
    /// <see cref="MenuHoverCursor"/> の left/rightSprite のみ。
    ///
    /// 付け先: TitleLcdRig/Content（または任意の常駐 GameObject）。
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class TitleDayNight : MonoBehaviour
    {
        public enum Mode { Day, Night }

        [Tooltip("昼/夜。 編集中に変えると即時反映。 実行時は Awake で適用。")]
        public Mode mode = Mode.Day;

        [System.Serializable]
        public class Slot
        {
            [Tooltip("差し替え対象の SpriteRenderer（BG / Title / ボタン等）")]
            public SpriteRenderer target;
            public Sprite day;
            public Sprite night;
        }

        [Header("スプライト差し替え（BG・Title・各ボタン）")]
        public Slot[] slots;

        [Header("両端カーソル（MenuHoverCursor の left/right を差し替え）")]
        public MenuHoverCursor cursor;
        public Sprite cursorLeftDay, cursorLeftNight;
        public Sprite cursorRightDay, cursorRightNight;

        [Header("BG マテリアル差し替え（昼=陽炎 / 夜=陽炎なし）")]
        [Tooltip("背景の SpriteRenderer")]
        public SpriteRenderer bgRenderer;
        [Tooltip("昼のマテリアル（Sprites/HeatHaze）")]
        public Material dayMaterial;
        [Tooltip("夜のマテリアル（陽炎なし＝Sprites/Default 等）")]
        public Material nightMaterial;

        [Header("モード限定オブジェクト（SetActive で切替）")]
        [Tooltip("昼だけ有効にする GameObject（例: なし）")]
        public GameObject[] dayOnly;
        [Tooltip("夜だけ有効にする GameObject（例: 星・流れ星）")]
        public GameObject[] nightOnly;

        private void Awake() { Apply(); }
        private void OnEnable() { Apply(); }
#if UNITY_EDITOR
        private void OnValidate() { Apply(); }
#endif

        /// <summary>コードから昼/夜を切り替える。</summary>
        public void SetMode(Mode m) { mode = m; Apply(); }

        public void Apply()
        {
            bool night = mode == Mode.Night;

            if (slots != null)
                foreach (var s in slots)
                {
                    if (s == null || s.target == null) continue;
                    var sp = night ? s.night : s.day;
                    if (sp != null) s.target.sprite = sp;
                }

            if (cursor != null)
            {
                cursor.leftSprite = night ? cursorLeftNight : cursorLeftDay;
                cursor.rightSprite = night ? cursorRightNight : cursorRightDay;
                cursor.RefreshCursors(); // 実行時に生成済みカーソルがあれば反映
            }

            // BG マテリアル（昼=陽炎 / 夜=陽炎なし）
            if (bgRenderer != null)
            {
                var m = night ? nightMaterial : dayMaterial;
                if (m != null) bgRenderer.sharedMaterial = m;
            }

            // モード限定オブジェクト（星・流れ星など）
            if (dayOnly != null)
                foreach (var g in dayOnly) if (g != null) g.SetActive(!night);
            if (nightOnly != null)
                foreach (var g in nightOnly) if (g != null) g.SetActive(night);

            // 砂塵(DustField) は夜では消す。 ピクセル砂塵=昼の風景表現のため。
            var dusts = FindObjectsOfType<DustField>(includeInactive: true);
            foreach (var d in dusts) if (d != null) d.gameObject.SetActive(!night);

            // 蛍/精霊光と月光の脈動は夜のみ ON
            var fireflies = FindObjectsOfType<FireflyField>(includeInactive: true);
            foreach (var f in fireflies) if (f != null) f.gameObject.SetActive(night);
            var moons = FindObjectsOfType<MoonlightPulse>(includeInactive: true);
            foreach (var m in moons) if (m != null) m.enabled = night;

            // BGM を昼夜に追従（スキルツリー中は SkillTreeView が上書きするので、 ここで上書きしても Close 後の復帰先が正しくなる）
            if (Application.isPlaying)
            {
                var bgm = FindObjectOfType<TitleBgm>();
                if (bgm != null) bgm.SetMode(night ? TitleBgm.BgmMode.Night : TitleBgm.BgmMode.Day);
            }
        }
    }
}

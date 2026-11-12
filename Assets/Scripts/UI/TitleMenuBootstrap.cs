using UnityEngine;

namespace UI
{
    /// <summary>
    /// シーンに <see cref="TitleMenuRouter"/> が手配線されていない場合、 PlayButton / SkillTreeButton を名前で探して
    /// 自動配線する (手配置のルーターでも、 未配線の欄は名前で補う)。 LcdScreen が存在するシーンでのみ起動する(タイトル前は無視)。
    ///
    /// 2026-07-07: 職業選択 (ClassSelectView) は ClassSelectRigBuilder で実行時自動構築する。
    /// タイトルと同一方式 ── 専用ゾーンに UI を置き、 専用カメラで撮影して LcdAuxCamera で
    /// LCD の RenderTexture へ転写。 シーン配置は不要 (手配置があればそれを優先)。
    /// </summary>
    public static class TitleMenuBootstrap
    {
        private static bool _spawned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Spawn()
        {
            if (_spawned) return;
            var lcd = Object.FindObjectOfType<Lcd.LcdScreen>();
            if (lcd == null) return;

            // ---- 職業選択: 手配置があれば使い、 無ければタイトルと同方式のリグを自動構築 ----
            var classSelect = Object.FindObjectOfType<ClassSelect.ClassSelectView>(true);
            if (classSelect == null)
                classSelect = ClassSelect.ClassSelectRigBuilder.Build(lcd);
            if (classSelect != null)
                classSelect.showOnStart = false; // 提示タイミングはルーターが握る

            // ---- ルーター: 手配線があれば classSelect だけ補完、 無ければ生成 ----
            var existing = Object.FindObjectOfType<TitleMenuRouter>();
            if (existing != null)
            {
                bool changed = false;
                if (existing.classSelect == null && classSelect != null)
                {
                    existing.classSelect = classSelect;
                    changed = true;
                    Debug.Log("[TitleMenuBootstrap] 既存 TitleMenuRouter に classSelect を補完");
                }
                if (existing.metaPanelButton == null)
                {
                    existing.metaPanelButton = FindButton("SkillTreeButton");
                    if (existing.metaPanelButton != null)
                    {
                        changed = true;
                        Debug.Log("[TitleMenuBootstrap] 既存 TitleMenuRouter に SKILL TREE (整備パネル) ボタンを補完");
                    }
                }
                if (changed) existing.Rewire(); // OnEnable 後の代入なので購読を張り直す
                _spawned = true;
                return;
            }

            SpriteButton playButton = FindButton("PlayButton");
            if (playButton == null) return;

            var go = new GameObject("[TitleMenuRouter]");
            var router = go.AddComponent<TitleMenuRouter>();
            router.playButton = playButton;
            router.metaPanelButton = FindButton("SkillTreeButton");
            router.classSelect = classSelect;
            router.Rewire(); // AddComponent 時点の OnEnable は参照 null で走っているため張り直す
            Object.DontDestroyOnLoad(go);
            _spawned = true;
            Debug.Log($"[TitleMenuBootstrap] wired PlayButton={playButton.name} SkillTreeButton={(router.metaPanelButton != null)} classSelect={(classSelect != null)}");
        }

        /// <summary>シーン内の SpriteButton を名前で探す (非アクティブも含む)。 無ければ null。</summary>
        private static SpriteButton FindButton(string name)
        {
            foreach (var b in Object.FindObjectsOfType<SpriteButton>(true))
                if (b != null && b.name == name) return b;
            return null;
        }
    }
}

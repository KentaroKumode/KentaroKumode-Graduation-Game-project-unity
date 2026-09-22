using UnityEngine;

namespace UI
{
    /// <summary>
    /// シーンに <see cref="TitleMenuRouter"/> が手配線されていない場合、 PlayButton を名前で探して
    /// 自動配線する。 LcdScreen が存在するシーンでのみ起動する(タイトル前は無視)。
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
                if (existing.classSelect == null && classSelect != null)
                {
                    existing.classSelect = classSelect;
                    existing.Rewire(); // OnEnable 後の代入なので購読を張り直す
                    Debug.Log("[TitleMenuBootstrap] 既存 TitleMenuRouter に classSelect を補完");
                }
                _spawned = true;
                return;
            }

            SpriteButton playButton = null;
            foreach (var b in Object.FindObjectsOfType<SpriteButton>(true))
            {
                if (b == null) continue;
                if (b.name == "PlayButton") { playButton = b; break; }
            }
            if (playButton == null) return;

            var go = new GameObject("[TitleMenuRouter]");
            var router = go.AddComponent<TitleMenuRouter>();
            router.playButton = playButton;
            router.classSelect = classSelect;
            router.Rewire(); // AddComponent 時点の OnEnable は参照 null で走っているため張り直す
            Object.DontDestroyOnLoad(go);
            _spawned = true;
            Debug.Log($"[TitleMenuBootstrap] wired PlayButton={playButton.name} classSelect={(classSelect != null)}");
        }
    }
}

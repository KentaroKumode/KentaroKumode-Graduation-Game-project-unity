using UnityEngine;

namespace UI
{
    /// <summary>
    /// シーンに <see cref="TitleMenuRouter"/> が手配線されていない場合、 PlayButton を名前で探して
    /// 自動配線する。 LcdScreen が存在するシーンでのみ起動する(タイトル前は無視)。
    /// </summary>
    public static class TitleMenuBootstrap
    {
        private static bool _spawned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Spawn()
        {
            if (_spawned) return;
            if (Object.FindObjectOfType<TitleMenuRouter>() != null) { _spawned = true; return; }
            var lcd = Object.FindObjectOfType<Lcd.LcdScreen>();
            if (lcd == null) return;

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
            Object.DontDestroyOnLoad(go);
            _spawned = true;
            Debug.Log($"[TitleMenuBootstrap] wired PlayButton={playButton.name}");
        }
    }
}

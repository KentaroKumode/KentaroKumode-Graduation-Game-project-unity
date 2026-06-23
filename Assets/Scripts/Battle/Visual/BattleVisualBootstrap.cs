using UnityEngine;

namespace Battle.Visual
{
    /// <summary>
    /// ランタイム初期化フックで <see cref="BattleVisualRig"/> と <see cref="BattleVisualDirector"/> が
    /// シーンに無ければ自動生成する。 既存のシングルトンパターン(_shuttingDown + RuntimeInitializeOnLoadMethod)
    /// に倣う薄いブートストラッパ。
    ///
    /// LcdScreen.contentCamera が見つからない時は何もしない(タイトル前など)。
    /// </summary>
    public static class BattleVisualBootstrap
    {
        private static bool _spawned;
        private static GameObject _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Spawn()
        {
            if (_spawned && _root != null) return;
            if (Object.FindObjectOfType<BattleVisualDirector>() != null) { _spawned = true; return; }

            var lcd = Object.FindObjectOfType<UI.Lcd.LcdScreen>();
            if (lcd == null || lcd.contentCamera == null) return; // LCD 無しシーンでは起動しない

            _root = new GameObject("[BattleVisual]");
            var rig = _root.AddComponent<BattleVisualRig>();
            rig.contentCamera = lcd.contentCamera;
            var director = _root.AddComponent<BattleVisualDirector>();
            director.rig = rig;
            Object.DontDestroyOnLoad(_root);
            _spawned = true;
        }
    }
}

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UI.Game2D;

/// <summary>層の帯 (Game2DLayerBanner) をその場で流すデバッグのメニュー (2026-11-16)。 Play 中だけ押せる。
/// ゲームの中では Ctrl + 0〜6 でも同じ物が出る (0 = 序層 … 5 = 第五層・6 = 最終段)。</summary>
public static class LayerBannerMenu
{
    private const string Root = "Tools/DICE BOUND/層の帯/";
    private static void Show(int floor) { if (!Game2DLayerBanner.DebugShow(floor)) Debug.LogWarning("[層の帯] 帯の部品が見つからない (Game2D のシーンを Play しているか)"); }
    private static bool Playing() => Application.isPlaying;

    [MenuItem(Root + "序層", priority = 300)] private static void S0() => Show(0);
    [MenuItem(Root + "第一層", priority = 301)] private static void S1() => Show(1);
    [MenuItem(Root + "第二層", priority = 302)] private static void S3() => Show(3);
    [MenuItem(Root + "第三層", priority = 303)] private static void S5() => Show(5);
    [MenuItem(Root + "第四層", priority = 304)] private static void S6() => Show(6);
    [MenuItem(Root + "第五層", priority = 305)] private static void S7() => Show(7);
    [MenuItem(Root + "最終段 (Null Point)", priority = 306)] private static void S8() => Show(8);

    [MenuItem(Root + "序層", true)] [MenuItem(Root + "第一層", true)] [MenuItem(Root + "第二層", true)] [MenuItem(Root + "第三層", true)]
    [MenuItem(Root + "第四層", true)] [MenuItem(Root + "第五層", true)] [MenuItem(Root + "最終段 (Null Point)", true)]
    private static bool Valid() => Playing();
}
#endif

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>エンディングを本物と同じ流れ (踏破のフェーズ) で出すデバッグのメニュー (2026-11-16)。 Play 中だけ押せる。 セーブは止める。
/// 中身は <see cref="UI.Game2D.Battle.QaBattle.ShowEndingInRunClear"/>。 最後の頁の後に結びの絵 → 「踏破」の窓 → タイトルへ。</summary>
public static class EndingMenu
{
    private const string Root = "Tools/DICE BOUND/エンディング/";
    private static void Show(string id) { if (!UI.Game2D.Battle.QaBattle.ShowEndingInRunClear(id)) Debug.LogWarning("[エンディング] 出せなかった: " + id); }

    [MenuItem(Root + "英雄の帰還 (end1)", priority = 320)] private static void E1() => Show("end1");
    [MenuItem(Root + "無明の剣 (end2)", priority = 321)] private static void E2() => Show("end2");
    [MenuItem(Root + "空の玉座 (end3)", priority = 322)] private static void E3() => Show("end3");
    [MenuItem(Root + "GOOD END (end_good)", priority = 323)] private static void EG() => Show(GameLoop.Endings.EndGoodId);
    [MenuItem(Root + "TRUE END (end_true)", priority = 324)] private static void ET() => Show(GameLoop.Endings.EndTrueId);

    [MenuItem(Root + "英雄の帰還 (end1)", true)] [MenuItem(Root + "無明の剣 (end2)", true)] [MenuItem(Root + "空の玉座 (end3)", true)]
    [MenuItem(Root + "GOOD END (end_good)", true)] [MenuItem(Root + "TRUE END (end_true)", true)]
    private static bool Valid() => Application.isPlaying;
}
#endif

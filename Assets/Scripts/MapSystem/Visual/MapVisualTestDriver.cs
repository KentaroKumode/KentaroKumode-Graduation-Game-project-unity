using System.Collections.Generic;
using UnityEngine;
using MapSystem;
using MapSystem.Visual;

/// <summary>
/// マップビジュアルの単体テスト用コントローラー。
/// GameManager / CombatManager なしで動作する。
///
/// ── セットアップ ──
/// 1. 空の Scene を作成
/// 2. GameObject を1つ作り MapVisualTestDriver をアタッチ
/// 3. 同 GameObject (または子) に MapManager, MapVisualizer, MapClickHandler をアタッチ
/// 4. MapVisualizer の各フィールドを設定
/// 5. Play → キー操作でテスト
///
/// ── キー操作 ──
///   1-6  : フロア番号を選んでマップ再生成
///   R    : 同じフロアを再生成（シード変わる）
///   Space: Reachable ノードへ自動移動（ランダム1つ）
///   H    : 空腹度を強制的に0にする（飢餓テスト）
///   S    : 現在の状態をコンソールに出力
/// </summary>
public class MapVisualTestDriver : MonoBehaviour
{
    [Header("テスト設定")]
    [SerializeField] private int testFloor = 1;
    [SerializeField] private bool autoGenerateOnStart = true;

    // === 内部参照 (Awake でセルフ取得) ===
    private MapManager mapManager;

    void Awake()
    {
        // MapManager が DontDestroyOnLoad シングルトンなら Instance から取得
        // テストシーンでは同 GameObject または子にいる場合もあるため両対応
        mapManager = MapManager.Instance;
        if (mapManager == null)
            mapManager = GetComponentInChildren<MapManager>(true);
    }

    void Start()
    {
        if (mapManager == null)
        {
            Debug.LogError("[MapVisualTestDriver] MapManager が見つかりません。" +
                           "同一 GameObject か子に MapManager をアタッチしてください。");
            return;
        }

        if (autoGenerateOnStart)
            Generate(testFloor);
    }

    void Update()
    {
        if (mapManager == null) return;

        // GameManager がシーンにいる場合、本ドライバはテスト目的の独立シーン用なので入力を全て無効化する。
        // これがないと Space/数字キー/R が GameManager 側のフロー（戦闘・イベント結果送りなど）と競合する。
        if (GameLoop.GameManager.Instance != null) return;

        // 1-6: フロア選択
        for (int f = 1; f <= 6; f++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + f) || Input.GetKeyDown(KeyCode.Keypad0 + f))
            {
                testFloor = f;
                Generate(f);
                return;
            }
        }

        // R: 再生成
        if (Input.GetKeyDown(KeyCode.R)) { Generate(testFloor); return; }

        // Space: ランダム自動移動
        if (Input.GetKeyDown(KeyCode.Space)) { AutoMove(); return; }

        // H: 飢餓テスト（空腹度を0に）
        if (Input.GetKeyDown(KeyCode.H)) { ForceFamine(); return; }

        // S: ステータスダンプ
        if (Input.GetKeyDown(KeyCode.S)) { DumpStatus(); return; }
    }

    // ================================================================
    //  操作
    // ================================================================

    private void Generate(int floor)
    {
        mapManager.GenerateFloor(floor);
        Debug.Log($"[MapVisualTest] フロア {floor} 生成完了 " +
                  $"(ノード数: {mapManager.CurrentMap?.GetAllNodes()?.Count ?? 0})");
    }

    private void AutoMove()
    {
        if (mapManager.CurrentMap == null || mapManager.CurrentNode == null) return;

        var moves = mapManager.GetAvailableMoves();
        if (moves.Count == 0)
        {
            Debug.Log("[MapVisualTest] 移動先なし（ボスか行き止まり）");
            return;
        }

        // ランダムに1つ選んで移動（playerMaxHP=30 固定）
        var target = moves[Random.Range(0, moves.Count)];
        int starvDmg = mapManager.MoveTo(target.id, 30);

        string tileLabel = target.EffectiveType.ToString();
        string dmgText = starvDmg > 0 ? $" ⚠飢餓{starvDmg}dmg" : "";
        Debug.Log($"[MapVisualTest] → {target.id} ({tileLabel}){dmgText}" +
                  $" 空腹:{mapManager.Hunger.Current}/{mapManager.Hunger.Max}");
    }

    private void ForceFamine()
    {
        if (mapManager.Hunger == null) return;
        // Initialize(0) では Max も変わってしまうので、SetCurrentForTest を使う
        mapManager.Hunger.SetCurrentForTest(0);
        Debug.Log("[MapVisualTest] 空腹度を強制 0 にしました");
    }

    private void DumpStatus()
    {
        if (mapManager.CurrentMap == null) { Debug.Log("[MapVisualTest] マップ未生成"); return; }

        var node = mapManager.CurrentNode;
        var map  = mapManager.CurrentMap;
        var hungry = mapManager.Hunger;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== MapVisual Test Status ===");
        sb.AppendLine($"  Floor     : {map.floor}");
        sb.AppendLine($"  Node      : {node?.id} ({node?.EffectiveType})");
        sb.AppendLine($"  Hunger    : {hungry?.Current}/{hungry?.Max}");
        sb.AppendLine($"  Moves     : {string.Join(", ", mapManager.GetAvailableMoves().ConvertAll(n => n.id))}");

        // タイルタイプ内訳
        var counts = new Dictionary<TileType, int>();
        foreach (var n in map.GetAllNodes())
        {
            if (!counts.ContainsKey(n.type)) counts[n.type] = 0;
            counts[n.type]++;
        }
        sb.AppendLine("  TileCounts:");
        foreach (var kv in counts)
            sb.AppendLine($"    {kv.Key,-15}: {kv.Value}");

        Debug.Log(sb.ToString());
    }

    // ================================================================
    //  GUI (画面右下にキー一覧を表示)
    // ================================================================

    void OnGUI()
    {
        if (GameLoop.GameManager.Instance != null) return;

        var style = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.UpperLeft };
        style.normal.textColor = Color.white;

        float w = 310, h = 200;
        float x = Screen.width  - w - 8;
        float y = Screen.height - h - 8;

        string info = "[MapVisual Test]\n" +
                      "  1-6  : フロア再生成\n" +
                      "  R    : 同フロア再生成\n" +
                      "  Space: ランダム自動移動\n" +
                      "  H    : 飢餓テスト（空腹度0）\n" +
                      "  S    : ステータスダンプ\n";

        if (mapManager?.CurrentNode != null)
        {
            info += $"\n現在 : {mapManager.CurrentNode.id}\n";
            info += $"タイル: {mapManager.CurrentNode.EffectiveType}\n";
            info += $"空腹 : {mapManager.Hunger?.Current}/{mapManager.Hunger?.Max}";
        }

        GUI.Box(new Rect(x, y, w, h), info, style);
    }
}

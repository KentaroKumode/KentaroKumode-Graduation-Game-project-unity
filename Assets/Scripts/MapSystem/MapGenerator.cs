using System.Collections.Generic;
using UnityEngine;

namespace MapSystem
{
    /// <summary>
    /// フロアマップの手続き生成。
    /// 3レーン × 10行 + ボス収束ノード。
    /// 6層のみ固定構成（前哨基地→ショップ→デバフ→ラスボス）。
    /// </summary>
    public static class MapGenerator
    {
        // === タイル抽選ウェイト（ランダム行用）===
        private static readonly (TileType type, float weight)[] TileWeights =
        {
            (TileType.Battle,      30f),
            (TileType.Mystery,     25f),
            (TileType.Event,       15f),
            (TileType.Trap,        10f),
            (TileType.EliteBattle,  8f),
            (TileType.Shop,         7f),
            (TileType.Treasure,     3f),
            (TileType.Rest,         2f),
        };

        // ?マス解決ウェイト（Outpost/Boss/Mystery 除外）
        private static readonly (TileType type, float weight)[] MysteryResolveWeights =
        {
            (TileType.Battle,      25f),
            (TileType.Event,       20f),
            (TileType.Trap,        12f),
            (TileType.EliteBattle, 10f),
            (TileType.Shop,        12f),
            (TileType.Treasure,    10f),
            (TileType.Rest,         8f),
        };

        public const int LaneCount = 3;
        public const int RowCount = 10;   // Row 0(前哨) 〜 Row 9(休憩)
        public const float LateralChance = 0.3f;
        public const float DiagonalChance = 0.6f;

        // ================================================================
        //  公開 API
        // ================================================================

        /// <summary>指定フロアのマップを生成</summary>
        public static FloorMap Generate(int floor)
        {
            return floor == 6 ? GenerateLayer6() : GenerateStandard(floor);
        }

        /// <summary>?マスの実タイプを抽選</summary>
        public static TileType ResolveMystery()
        {
            return WeightedRandom(MysteryResolveWeights);
        }

        // ================================================================
        //  通常フロア (1-5)
        // ================================================================

        private static FloorMap GenerateStandard(int floor)
        {
            var map = new FloorMap
            {
                floor = floor,
                laneCount = LaneCount,
                rowCount = RowCount
            };

            // --- ノード ---

            // Row 0: 前哨基地（収束ノード）
            map.AddNode(new MapNode("outpost", 0, -1, TileType.Outpost));
            map.startNodeId = "outpost";

            // Row 1 〜 7: ランダムタイル
            int lastRandomRow = RowCount - 3; // 7
            for (int row = 1; row <= lastRandomRow; row++)
            {
                for (int lane = 0; lane < LaneCount; lane++)
                {
                    var type = WeightedRandom(TileWeights);
                    map.AddNode(new MapNode(NodeId(row, lane), row, lane, type));
                }
            }

            // Row 8: 秘宝（保証）
            int treasureRow = RowCount - 2;
            for (int lane = 0; lane < LaneCount; lane++)
                map.AddNode(new MapNode(NodeId(treasureRow, lane), treasureRow, lane, TileType.Treasure));

            // Row 9: 休憩（保証）
            int restRow = RowCount - 1;
            for (int lane = 0; lane < LaneCount; lane++)
                map.AddNode(new MapNode(NodeId(restRow, lane), restRow, lane, TileType.Rest));

            // 5層のみ: ボス前にカルマ清算用の罠ノードを確定配置（収束ノード）
            bool insertKarmaTrap = floor == 5;
            if (insertKarmaTrap)
            {
                map.AddNode(new MapNode("karma_trap", RowCount, -1, TileType.Trap));
            }

            // ボス（Row 10 相当、収束ノード）。5層では Row 11 にずらす
            int bossRow = insertKarmaTrap ? RowCount + 1 : RowCount;
            map.AddNode(new MapNode("boss", bossRow, -1, TileType.Boss));
            map.bossNodeId = "boss";

            // --- エッジ ---

            // 前哨基地 → Row 1 全レーン
            for (int lane = 0; lane < LaneCount; lane++)
                map.AddConnection("outpost", NodeId(1, lane));

            // Row 1〜9 → 次行: 前方 + 斜め前方
            // Treasure(行8)・Rest(行9) を含む行/接続では斜めを 0% にする
            for (int row = 1; row < RowCount; row++)
            {
                int targetRow = row + 1;
                bool sourceGuaranteed = (row == treasureRow || row == restRow);
                bool targetGuaranteed = (targetRow == treasureRow || targetRow == restRow);
                bool allowDiagonal = !sourceGuaranteed && !targetGuaranteed;

                for (int lane = 0; lane < LaneCount; lane++)
                {
                    string from = NodeId(row, lane);

                    // 前方（同レーン、次行）— 常時
                    string fwd = (row + 1 <= restRow) ? NodeId(row + 1, lane) : null;
                    if (fwd != null)
                        map.AddConnection(from, fwd);

                    if (!allowDiagonal) continue;

                    // 斜め左（60%）
                    if (lane > 0 && Random.value < DiagonalChance)
                    {
                        string dl = (row + 1 <= restRow) ? NodeId(row + 1, lane - 1) : null;
                        if (dl != null)
                            map.AddConnection(from, dl);
                    }

                    // 斜め右（60%）
                    if (lane < LaneCount - 1 && Random.value < DiagonalChance)
                    {
                        string dr = (row + 1 <= restRow) ? NodeId(row + 1, lane + 1) : null;
                        if (dr != null)
                            map.AddConnection(from, dr);
                    }
                }
            }

            // 休憩行 → ボス（5層は休憩→カルマ罠→ボス で必ず通過）
            if (insertKarmaTrap)
            {
                for (int lane = 0; lane < LaneCount; lane++)
                    map.AddConnection(NodeId(restRow, lane), "karma_trap");
                map.AddConnection("karma_trap", "boss");
            }
            else
            {
                for (int lane = 0; lane < LaneCount; lane++)
                    map.AddConnection(NodeId(restRow, lane), "boss");
            }

            // 横移動（確率、双方向）— Treasure/Rest 行は除外
            for (int row = 1; row <= restRow; row++)
            {
                if (row == treasureRow || row == restRow) continue;

                for (int lane = 0; lane < LaneCount - 1; lane++)
                {
                    if (Random.value < LateralChance)
                        map.AddConnection(NodeId(row, lane), NodeId(row, lane + 1), bidirectional: true);
                }
            }

            // ショップ確定出現: 上下3行を除いた中央行(4〜6)のランダムマスを
            // 1〜3個 Shop へ強制置換（接続トポロジは不変＝type だけ差し替え）
            ForceInjectShops(map);

            return map;
        }

        /// <summary>中央行(4〜lastRandomRow-1)からランダムに1〜3マスを Shop に強制置換。</summary>
        private static void ForceInjectShops(FloorMap map)
        {
            int firstCentral = 4;
            int lastCentral = (RowCount - 3) - 1; // RowCount=10 → 6
            if (lastCentral < firstCentral) lastCentral = firstCentral;

            var cells = new List<MapNode>();
            for (int row = firstCentral; row <= lastCentral; row++)
                for (int lane = 0; lane < LaneCount; lane++)
                {
                    var n = map.GetNode(NodeId(row, lane));
                    if (n != null) cells.Add(n);
                }
            if (cells.Count == 0) return;

            // Fisher-Yates シャッフル
            for (int i = cells.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (cells[i], cells[j]) = (cells[j], cells[i]);
            }

            int n2 = Mathf.Min(cells.Count, Random.Range(1, 4)); // 1〜3
            for (int k = 0; k < n2; k++)
            {
                cells[k].type = TileType.Shop;
                cells[k].resolvedType = null; // EffectiveType が Shop を返すように
            }
        }

        // ================================================================
        //  6層（裏ボス）— 固定リニア構成
        // ================================================================

        private static FloorMap GenerateLayer6()
        {
            var map = new FloorMap { floor = 6, laneCount = 1, rowCount = 4 };

            map.AddNode(new MapNode("outpost", 0, -1, TileType.Outpost));
            map.AddNode(new MapNode(NodeId(1, 0), 1, 0, TileType.Shop));
            map.AddNode(new MapNode(NodeId(2, 0), 2, 0, TileType.SinAltar));
            map.AddNode(new MapNode("boss", 3, -1, TileType.Boss));

            map.startNodeId = "outpost";
            map.bossNodeId = "boss";

            map.AddConnection("outpost", NodeId(1, 0));
            map.AddConnection(NodeId(1, 0), NodeId(2, 0));
            map.AddConnection(NodeId(2, 0), "boss");

            return map;
        }

        // ================================================================
        //  ユーティリティ
        // ================================================================

        private static string NodeId(int row, int lane) => $"r{row}_l{lane}";

        private static TileType WeightedRandom((TileType type, float weight)[] weights)
        {
            float total = 0f;
            foreach (var (_, w) in weights) total += w;

            float roll = Random.Range(0f, total);
            float cumulative = 0f;

            foreach (var (type, weight) in weights)
            {
                cumulative += weight;
                if (roll <= cumulative) return type;
            }

            return weights[0].type;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapSystem
{
    /// <summary>
    /// マップの生成・ナビゲーション・タイル起動を管理するシングルトン。
    /// GameManager から呼ばれ、移動・空腹度・?マス解決を処理する。
    /// </summary>
    public class MapManager : MonoBehaviour
    {
        public static MapManager Instance { get; private set; }

        // === 状態 ===
        public FloorMap CurrentMap { get; private set; }
        public MapNode CurrentNode { get; private set; }
        public HungerSystem Hunger { get; private set; }

        // === イベント ===
        public event Action<FloorMap> OnMapGenerated;
        public event Action<MapNode> OnNodeEntered;
        public event Action<MapNode, TileType> OnMysteryResolved;

        // === 設定 ===
        [Header("空腹度")]
        [SerializeField] private int hungerPerFloor = 10;
        [SerializeField] private float starvationDamageRatio = 0.1f;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // ================================================================
        //  公開 API
        // ================================================================

        /// <summary>指定フロアのマップを生成し、前哨基地に配置</summary>
        public void GenerateFloor(int floor)
        {
            CurrentMap = MapGenerator.Generate(floor);
            CurrentNode = CurrentMap.GetNode(CurrentMap.startNodeId);
            CurrentNode.visited = true;

            Hunger = new HungerSystem { starvationDamageRatio = starvationDamageRatio };
            int hunger = (floor == 6) ? 3 : hungerPerFloor;
            Hunger.Initialize(hunger);

            OnMapGenerated?.Invoke(CurrentMap);
        }

        /// <summary>現在位置から移動可能なノード一覧</summary>
        public List<MapNode> GetAvailableMoves()
        {
            if (CurrentMap == null || CurrentNode == null)
                return new List<MapNode>();
            return CurrentMap.GetReachableFrom(CurrentNode.id);
        }

        /// <summary>前進と横移動を分類して返す</summary>
        public (List<MapNode> forward, List<MapNode> lateral) GetCategorizedMoves()
        {
            if (CurrentMap == null || CurrentNode == null)
                return (new List<MapNode>(), new List<MapNode>());
            return CurrentMap.CategorizeMovesFrom(CurrentNode.id);
        }

        /// <summary>
        /// 指定ノードへ移動。空腹ダメージを返す。
        /// Mystery タイルは自動解決される。
        /// </summary>
        public int MoveTo(string nodeId, int playerMaxHP)
        {
            var target = CurrentMap?.GetNode(nodeId);
            if (target == null)
            {
                Debug.LogWarning($"[MapManager] ノード '{nodeId}' が存在しません");
                return 0;
            }

            if (!CurrentNode.connections.Contains(nodeId))
            {
                Debug.LogWarning($"[MapManager] '{CurrentNode.id}' → '{nodeId}' は接続なし");
                return 0;
            }

            // 空腹度処理
            int starvationDmg = Hunger.OnMove(playerMaxHP);

            // 位置更新
            CurrentNode = target;
            target.visited = true;

            // ?マス解決
            if (target.type == TileType.Mystery && target.resolvedType == null)
            {
                target.resolvedType = MapGenerator.ResolveMystery();
                OnMysteryResolved?.Invoke(target, target.resolvedType.Value);
            }

            OnNodeEntered?.Invoke(target);

            return starvationDmg;
        }

        /// <summary>前哨基地効果: 空腹度全回復</summary>
        public void ProcessOutpost()
        {
            Hunger?.RestoreFull();
        }

        /// <summary>現在位置がボスかどうか</summary>
        public bool IsAtBoss => CurrentNode?.type == TileType.Boss;
    }
}

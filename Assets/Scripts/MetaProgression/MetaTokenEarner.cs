using UnityEngine;

namespace MetaProgression
{
    /// <summary>
    /// トークン獲得式と各種獲得イベントの集約。
    /// 進行に比例した獲得（リセマラ無効化）:
    ///   floor   : フロア到達ごとに +10
    ///   node    : マップノード踏破ごとに +1
    ///   enemy   : 敵撃破ごとに +2
    ///   event   : イベント発見ごとに +3
    /// </summary>
    public static class MetaTokenEarner
    {
        public const int TokensPerFloor = 10;
        public const int TokensPerNode = 1;
        public const int TokensPerEnemy = 2;
        public const int TokensPerEvent = 3;

        public static void OnFloorReached(int floor)
        {
            // フロア到達は currentFloor を渡すので、毎フロア +10
            Award(TokensPerFloor, $"フロア{floor}到達");
        }

        public static void OnNodeVisited()
        {
            Award(TokensPerNode, "ノード踏破");
        }

        public static void OnEnemyDefeated()
        {
            Award(TokensPerEnemy, "敵撃破");
        }

        public static void OnEventEncountered()
        {
            Award(TokensPerEvent, "イベント発見");
        }

        private static void Award(int amount, string reason)
        {
            var mgr = MetaProgressManager.Instance;
            if (mgr == null) return;
            mgr.AddTokens(amount);
            Debug.Log($"[MetaToken] +{amount} ({reason})  累計={mgr.State.tokens}");
        }
    }
}

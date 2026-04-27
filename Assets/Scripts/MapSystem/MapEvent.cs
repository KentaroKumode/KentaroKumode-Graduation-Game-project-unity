using System.Collections.Generic;
using UnityEngine;

namespace MapSystem
{
    /// <summary>
    /// マップイベントのインターフェース。
    /// 個々のイベントはこれを実装し MapEventRegistry に登録する。
    /// </summary>
    public interface IMapEvent
    {
        string Id { get; }
        string DisplayName { get; }
        EventRarity Rarity { get; }

        /// <summary>イベントを実行する</summary>
        void Execute();
    }

    /// <summary>
    /// イベント抽選レジストリ。
    /// 1) レアリティを重み付き抽選 → 2) そのレアリティ内から均等抽選。
    /// イベントの中身は外部モジュールとして後から登録する。
    /// </summary>
    public static class MapEventRegistry
    {
        private static readonly List<IMapEvent> allEvents = new List<IMapEvent>();

        private static readonly (EventRarity rarity, float weight)[] RarityWeights =
        {
            (EventRarity.Bronze,    50f),
            (EventRarity.Silver,    30f),
            (EventRarity.Gold,      15f),
            (EventRarity.Legendary,  5f),
        };

        public static void Register(IMapEvent mapEvent) => allEvents.Add(mapEvent);
        public static void Unregister(string id) => allEvents.RemoveAll(e => e.Id == id);
        public static void Clear() => allEvents.Clear();
        public static int Count => allEvents.Count;

        /// <summary>レアリティ重み付き抽選でイベントを1つ選出</summary>
        public static IMapEvent Draw()
        {
            if (allEvents.Count == 0)
            {
                Debug.LogWarning("[MapEventRegistry] イベント未登録");
                return null;
            }

            var rarity = DrawRarity();
            var candidates = allEvents.FindAll(e => e.Rarity == rarity);

            // 該当レアリティが空ならフォールバック
            if (candidates.Count == 0)
                candidates = allEvents;

            return candidates[Random.Range(0, candidates.Count)];
        }

        private static EventRarity DrawRarity()
        {
            float total = 0f;
            foreach (var (_, w) in RarityWeights) total += w;

            float roll = Random.Range(0f, total);
            float cumulative = 0f;

            foreach (var (rarity, weight) in RarityWeights)
            {
                cumulative += weight;
                if (roll <= cumulative) return rarity;
            }

            return EventRarity.Bronze;
        }
    }
}

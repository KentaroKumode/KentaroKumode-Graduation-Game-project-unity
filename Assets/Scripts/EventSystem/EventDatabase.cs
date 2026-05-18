using System.Collections.Generic;
using UnityEngine;
using GameLoop;

namespace EventSystem
{
    /// <summary>
    /// 全イベント定義の静的コレクション。Resources/Events/event_list から読み込む。
    /// 出現条件・フラグ・優先度に応じて選出する。
    /// </summary>
    public static class EventDatabase
    {
        private static List<EventDefinition> all = new List<EventDefinition>();
        private static bool initialized;

        public const string ResourcePath = "Events/event_list";

        public static IReadOnlyList<EventDefinition> All
        {
            get { EnsureInitialized(); return all; }
        }

        public static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            Reload();
        }

        public static void Reload()
        {
            all.Clear();
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[EventDatabase] Resources/{ResourcePath}.txt が見つかりません");
                return;
            }
            all = EventParser.ParseAll(asset.text);
            Debug.Log($"[EventDatabase] {all.Count} イベント読み込み完了");
        }

        /// <summary>
        /// 現在のラン状況に応じて1個のイベントを選出する。
        /// 優先(priority)タグ持ちが条件を満たしていれば最優先で選ぶ。
        /// 残りからは均等抽選（rare は1/3の重み）。
        /// </summary>
        /// <param name="excludeRandomEvent">
        /// true の場合、RandomEvent 効果を含むイベントを抽選プールから除外する。
        /// RandomEvent による振り直しで使用し、連鎖を構造的に発生不能にする。
        /// </param>
        public static EventDefinition Pick(RunState run, bool excludeRandomEvent = false)
        {
            EnsureInitialized();
            if (all.Count == 0 || run == null) return null;

            int floor = run.currentFloor;
            var available = new List<EventDefinition>();
            foreach (var ev in all)
            {
                if (!IsAvailable(ev, run, floor)) continue;
                if (excludeRandomEvent && ContainsRandomEvent(ev)) continue;
                available.Add(ev);
            }
            if (available.Count == 0)
            {
                Debug.LogWarning("[EventDatabase] 該当フロア・条件のイベントが0件");
                return null;
            }

            // 優先タグ: 条件満たすものがあれば、それらの中から抽選
            var priority = available.FindAll(e => e.condition.priority);
            var pool = priority.Count > 0 ? priority : available;

            // 重み付き抽選
            float total = 0f;
            var weights = new float[pool.Count];
            for (int i = 0; i < pool.Count; i++)
            {
                float w = pool[i].condition.rare ? 0.33f : 1f;
                weights[i] = w;
                total += w;
            }
            float r = Random.value * total;
            for (int i = 0; i < pool.Count; i++)
            {
                if ((r -= weights[i]) <= 0f) return pool[i];
            }
            return pool[pool.Count - 1];
        }

        /// <summary>イベントが現在の状態で出現可能か判定。</summary>
        public static bool IsAvailable(EventDefinition ev, RunState run, int floor)
        {
            if (ev == null || ev.condition == null) return false;
            if (!ev.condition.MatchesFloor(floor)) return false;

            // 一度のみイベントが既出ならスキップ
            if (ev.condition.onceOnly && run.seenOnceEvents != null && run.seenOnceEvents.Contains(ev.id))
                return false;

            // フラグ要件
            foreach (var flag in ev.condition.requiredFlags)
            {
                if (run.ownedFlags == null || !run.ownedFlags.Contains(flag))
                    return false;
            }

            // パッシブアイテム所持要件
            foreach (var passId in ev.condition.requiredPassiveItems)
            {
                if (run.ownedPassiveItems == null || !run.ownedPassiveItems.Contains(passId))
                    return false;
            }
            return true;
        }

        /// <summary>このイベントの選択肢(確率分岐含む)に RandomEvent 効果が含まれるか。</summary>
        public static bool ContainsRandomEvent(EventDefinition ev)
        {
            if (ev?.choices == null) return false;
            foreach (var c in ev.choices)
                if (c?.effects != null && EffectsHaveRandomEvent(c.effects)) return true;
            return false;
        }

        private static bool EffectsHaveRandomEvent(List<EventEffect> effects)
        {
            foreach (var e in effects)
            {
                if (e == null) continue;
                if (e.type == EventEffectType.RandomEvent) return true;
                if (e.branches != null)
                    foreach (var br in e.branches)
                        if (br != null && EffectsHaveRandomEvent(br)) return true;
            }
            return false;
        }

        public static EventDefinition GetById(string id)
        {
            EnsureInitialized();
            foreach (var ev in all)
                if (ev.id == id) return ev;
            return null;
        }
    }
}

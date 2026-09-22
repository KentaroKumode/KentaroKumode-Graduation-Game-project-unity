using System;
using System.Collections.Generic;
using UnityEngine;
using GameLoop;
using MapSystem;

namespace EventSystem
{
    /// <summary>
    /// 1回のイベントエンカウンタを管理するシングルトン。
    /// GameManager がイベントマス到達時に Begin() を呼び、UI が ResolveChoice(i) を呼ぶ。
    /// </summary>
    public class EventEncounter : MonoBehaviour
    {
        private static EventEncounter _instance;
        private static bool _shuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _shuttingDown = false; _instance = null; }

        public static EventEncounter Instance
        {
            get
            {
                if (_shuttingDown) return null;
                if (_instance == null)
                {
                    var go = new GameObject("EventEncounter");
                    _instance = go.AddComponent<EventEncounter>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        void OnApplicationQuit() { _shuttingDown = true; }

        public EventDefinition Current { get; private set; }
        public string LastPostFlavor { get; private set; }
        public EventEffectExecutor.ExecutionResult LastResult { get; private set; }

        public event Action<EventDefinition> OnEventStarted;
        public event Action<EventChoice, EventEffectExecutor.ExecutionResult> OnEventResolved;

        /// <summary>戦闘勝利後に再適用するべき効果。GameManager が戦闘終了時に呼び出す。</summary>
        public List<EventEffect> PendingPostCombatEffects { get; private set; } = new List<EventEffect>();

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>イベントを抽選して開始する。失敗時は false を返す。</summary>
        /// <param name="excludeRandomEvent">
        /// RandomEvent による振り直し時 true。RandomEvent を含むイベントを
        /// 抽選プールから除外し、連鎖を構造的に防ぐ。
        /// </param>
        public bool Begin(RunState run, bool excludeRandomEvent = false)
        {
            if (run == null) return false;
            return BeginWith(run, EventDatabase.Pick(run, excludeRandomEvent));
        }

        /// <summary>**抽選せず、指定したイベントを開始する**。 召喚専用イベント用。
        /// 開始後の流れ (選択肢解決 / フレーバー / マップ復帰) は抽選経路と完全に同じ。</summary>
        public bool BeginWith(RunState run, EventDefinition ev)
        {
            if (run == null) return false;
            if (ev == null)
            {
                Debug.LogWarning("[EventEncounter] 該当イベントなし");
                Current = null;
                return false;
            }

            Current = ev;
            LastPostFlavor = null;
            LastResult = null;
            OnEventStarted?.Invoke(ev);
            Debug.Log($"[EventEncounter] 開始: {ev.name} (id={ev.id})");
            return true;
        }

        /// <summary>選択肢を確定して効果を適用。戻り値は実行結果。</summary>
        public EventEffectExecutor.ExecutionResult ResolveChoice(int index)
        {
            if (Current == null) return null;
            if (index < 0 || index >= Current.choices.Count) return null;

            var choice = Current.choices[index];
            var hunger = MapManager.Instance?.Hunger;
            var run = GameManager.Instance?.Run;

            // 行動台帳: 効果の**種別を解釈せず**、 実行前後の差分で「代償を払ったか」を決める。
            //   EventEffectType は増えるので、 型で分類すると新しい効果が追加されるたびに
            //   ここが取り残される。 GOLD と HP の減少という結果だけを見る。
            int chGold = run != null ? run.coins : 0;
            int chHp   = run != null ? run.playerHP : 0;

            var execResult = EventEffectExecutor.Execute(choice.effects, run, hunger);

            if (run != null)
            {
                bool paid = run.coins < chGold || run.playerHP < chHp;
                bool empty = choice.effects == null || choice.effects.Count == 0;
                string code = empty  ? RunChronicle.EventRefuse
                            : paid   ? RunChronicle.EventPrice
                                     : RunChronicle.EventTake;
                RunChronicle.Event(run, code, Current.id, index);
            }

            // 一度のみイベントの記録
            if (Current.condition.onceOnly && run != null)
                run.seenOnceEvents.Add(Current.id);

            LastPostFlavor = choice.postFlavor;
            LastResult = execResult;

            // 戦闘勝利後の効果を保存
            PendingPostCombatEffects = new List<EventEffect>(execResult.postCombatEffects);

            OnEventResolved?.Invoke(choice, execResult);
            Debug.Log($"[EventEncounter] 選択: {choice.text} → 効果{choice.effects.Count}個適用");
            return execResult;
        }

        /// <summary>戦闘勝利後の効果を適用してクリアする（GameManager から呼ぶ）。</summary>
        public void ApplyPostCombatEffects()
        {
            if (PendingPostCombatEffects == null || PendingPostCombatEffects.Count == 0) return;

            var hunger = MapManager.Instance?.Hunger;
            var run = GameManager.Instance?.Run;

            // postCombat フラグを外して通常実行
            var clones = new List<EventEffect>();
            foreach (var e in PendingPostCombatEffects)
                clones.Add(new EventEffect { type = e.type, param = e.param, amount = e.amount, branches = e.branches, branchWeights = e.branchWeights });

            EventEffectExecutor.Execute(clones, run, hunger);
            PendingPostCombatEffects.Clear();
            Debug.Log("[EventEncounter] 戦闘後効果適用");
        }

        public void Clear()
        {
            Current = null;
            LastPostFlavor = null;
            LastResult = null;
        }
    }
}

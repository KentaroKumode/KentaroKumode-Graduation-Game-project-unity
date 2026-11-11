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
            var run = GameManager.Instance?.Run;
            if (!IsChoiceSelectable(Current, index, run)) return null;   // 払えないゴールド

            // 行動台帳: 効果の**種別を解釈せず**、 実行前後の差分で「代償を払ったか」を決める。
            //   EventEffectType は増えるので、 型で分類すると新しい効果が追加されるたびに
            //   ここが取り残される。 GOLD と HP の減少という結果だけを見る。
            int chGold = run != null ? run.coins : 0;
            int chHp   = run != null ? run.playerHP : 0;
            int chProv = run != null ? run.provision : 0;
            int chMat  = run != null ? run.weaponMaterials : 0;

            var execResult = EventEffectExecutor.Execute(choice.effects, run);

            if (run != null)
            {
                // 物資と時間も代償に数える (2026-11-11・ターンを使う選択肢)。 素材も (2026-11-12・素材を払う選択肢)
                bool paid = run.coins < chGold || run.playerHP < chHp || run.provision < chProv || run.weaponMaterials < chMat
                         || execResult.turnsToPass > 0;
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
            PendingTurns += execResult.turnsToPass;   // ランダムなイベントへ振り直した時は両方の分を積む
            PendingNav.AddRange(execResult.navEffects);   // 航行の効果も同じく地図へ戻った時に (2026-11-12)

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

            var run = GameManager.Instance?.Run;

            // postCombat フラグを外して通常実行
            var clones = new List<EventEffect>();
            foreach (var e in PendingPostCombatEffects)
                clones.Add(new EventEffect { type = e.type, param = e.param, amount = e.amount, branches = e.branches, branchWeights = e.branchWeights });

            EventEffectExecutor.Execute(clones, run);
            PendingPostCombatEffects.Clear();
            Debug.Log("[EventEncounter] 戦闘後効果適用");
        }

        public void Clear()
        {
            Current = null;
            LastPostFlavor = null;
            LastResult = null;
        }

        // =====================================================================
        //  ターンを使う選択肢 (2026-11-11・issue #17)
        // =====================================================================

        /// <summary>選んだ選択肢が使うターンのうち、 まだ進めていない分。 GameManager がイベントを閉じて
        /// 地図へ戻った時に <see cref="TakePendingTurns"/> で取り出して進める。</summary>
        public int PendingTurns { get; private set; }

        /// <summary>まだ進めていないターンを取り出して 0 に戻す。</summary>
        public int TakePendingTurns() { int n = PendingTurns; PendingTurns = 0; return n; }

        /// <summary>選んだ選択肢の航行の効果 (地図を明かす・追跡者を撒く・近道・前線 …) のうち、 まだ適用していない分 (2026-11-12)。
        /// ターンと同じく、 GameManager がイベントを閉じて地図へ戻った時に <see cref="TakePendingNav"/> で取り出して適用する。</summary>
        public List<EventEffect> PendingNav { get; } = new List<EventEffect>();

        /// <summary>まだ適用していない航行の効果を取り出して空にする。</summary>
        public List<EventEffect> TakePendingNav() { var l = new List<EventEffect>(PendingNav); PendingNav.Clear(); return l; }

        /// <summary>選択肢 index を選べるか。 **全部が選べない時は全部を選べる扱いにする** (詰み防止)。</summary>
        public static bool IsChoiceSelectable(EventDefinition ev, int index, RunState run)
        {
            if (ev?.choices == null || index < 0 || index >= ev.choices.Count) return false;
            if (ev.choices[index] == null || ev.choices[index].IsAvailable(run)) return true;
            foreach (var c in ev.choices) if (c != null && c.IsAvailable(run)) return false;
            return true;
        }

        /// <summary>いま追跡者・猟犬・使い魔に補足されているなら「最短で何ターンで捕まるか」(こちらが留まった場合)。
        /// 補足されていない・航行マップの外なら <see cref="MapSystem.FreeMove.CatchEta.None"/>。
        /// 表示 (#72) と BOT が同じ値を読む。</summary>
        public static MapSystem.FreeMove.CatchEta CatchEtaNow()
        {
            var sim = GameManager.Instance != null ? GameManager.Instance.FreeMap : null;
            return sim != null ? sim.Eta() : MapSystem.FreeMove.CatchEta.None;
        }

        /// <summary>N ターンをその場で使うと捕まるか (今の補足の見込みで判定。 巡回中の追跡者が偶然通りかかる分は含まない)。</summary>
        public static bool WouldBeCaught(int turns)
        {
            if (turns <= 0) return false;
            var eta = CatchEtaNow();
            return eta.Valid && eta.turns <= turns;
        }

        /// <summary>選択肢の「代償」の短い説明 (イベントの画面の選択肢の下に出す)。 何も無ければ null。
        /// 得るものは書かない (見出しの文が語る) ── 書くのは時間と、 払えないゴールドだけ。</summary>
        public static string DescribeCost(EventChoice choice, RunState run)
        {
            if (choice == null) return null;
            var sb = new System.Text.StringBuilder();
            string why = choice.UnavailableReason(run);   // 払えないゴールド・素材・差し出せる品が無い・鍛えられない (2026-11-12)
            if (why != null) sb.Append(why);
            int t = choice.TurnCost;
            if (t > 0)
            {
                if (sb.Length > 0) sb.Append("・");
                sb.Append($"{t} ターンを使う");
                var eta = CatchEtaNow();
                if (eta.Valid)
                    sb.Append(eta.turns <= t ? $"（補足されている: {eta.turns} ターンで捕まる）"
                                             : $"（補足されている: 最短 {eta.turns} ターンで捕まる）");
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
    }
}

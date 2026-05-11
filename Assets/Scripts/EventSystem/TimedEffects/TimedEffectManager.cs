using System.Collections.Generic;
using UnityEngine;
using GameLoop;
using InventorySystem.PassiveSkills;

namespace EventSystem.TimedEffects
{
    /// <summary>
    /// 時限バフ・デバフのオーケストレータ。CombatManager / GameManager のフックから呼ばれる。
    /// 効果の適用とチャージ減算（消費）を担当。
    /// </summary>
    public static class TimedEffectManager
    {
        /// <summary>戦闘開始時に呼ぶ。CombatStart 系バフ・デバフを適用してチャージ-1。</summary>
        public static void OnCombatStart(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            ApplyAndConsume(TimedEffectTrigger.CombatStart, ctx, run, combat);
        }

        /// <summary>毎ロール時に呼ぶ（適用のみ、消費は戦闘終了時に集約）。</summary>
        public static void OnRoll(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            ApplyOnly(TimedEffectTrigger.OnRoll, ctx, run, combat);
        }

        /// <summary>毎ターン終了時に呼ぶ（適用のみ、消費は戦闘終了時に集約）。</summary>
        public static void OnTurnEnd(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            ApplyOnly(TimedEffectTrigger.OnTurnEnd, ctx, run, combat);
        }

        /// <summary>戦闘終了時に呼ぶ。CombatEnd 系適用 + 戦闘で消費すべきバフをまとめて減算。</summary>
        public static void OnCombatEnd(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            // 戦闘終了時バフを適用
            ApplyAndConsume(TimedEffectTrigger.CombatEnd, ctx, run, combat);

            // 戦闘で消費する OnRoll/OnTurnEnd 系バフをここで一括 -1
            DecrementByTrigger(TimedEffectTrigger.OnRoll, run);
            DecrementByTrigger(TimedEffectTrigger.OnTurnEnd, run);
        }

        /// <summary>マップ移動時に呼ぶ。OnMapMove 系を適用してチャージ-1。</summary>
        public static void OnMapMove(RunState run)
        {
            ApplyAndConsume(TimedEffectTrigger.OnMapMove, null, run, null);
        }

        // ============================================================

        private static void ApplyAndConsume(TimedEffectTrigger trigger, CombatContext ctx, RunState run,
            CombatSystem.CombatManager combat)
        {
            if (run == null) return;
            TimedEffectRegistry.EnsureInitialized();

            // バフ
            ApplyDictByTrigger(trigger, run.timedBuffs, ctx, run, combat, decrement: true);
            // デバフ
            ApplyDictByTrigger(trigger, run.timedDebuffs, ctx, run, combat, decrement: true);
        }

        private static void ApplyOnly(TimedEffectTrigger trigger, CombatContext ctx, RunState run,
            CombatSystem.CombatManager combat)
        {
            if (run == null) return;
            ApplyDictByTrigger(trigger, run.timedBuffs, ctx, run, combat, decrement: false);
            ApplyDictByTrigger(trigger, run.timedDebuffs, ctx, run, combat, decrement: false);
        }

        private static void ApplyDictByTrigger(TimedEffectTrigger trigger, Dictionary<string, int> store,
            CombatContext ctx, RunState run, CombatSystem.CombatManager combat, bool decrement)
        {
            if (store == null || store.Count == 0) return;

            var keys = new List<string>(store.Keys);
            foreach (var id in keys)
            {
                int charges = store[id];
                if (charges <= 0) continue;

                var effect = TimedEffectRegistry.Get(id);
                if (effect == null) continue;
                if (effect.Trigger != trigger) continue;

                effect.Apply(ctx, run, combat);

                if (decrement)
                {
                    store[id] = charges - 1;
                    if (store[id] <= 0) store.Remove(id);
                }
            }
        }

        private static void DecrementByTrigger(TimedEffectTrigger trigger, RunState run)
        {
            DecrementDict(trigger, run.timedBuffs);
            DecrementDict(trigger, run.timedDebuffs);
        }

        private static void DecrementDict(TimedEffectTrigger trigger, Dictionary<string, int> store)
        {
            if (store == null || store.Count == 0) return;
            var keys = new List<string>(store.Keys);
            foreach (var id in keys)
            {
                var effect = TimedEffectRegistry.Get(id);
                if (effect == null) continue;
                if (effect.Trigger != trigger) continue;
                store[id] = store[id] - 1;
                if (store[id] <= 0) store.Remove(id);
            }
        }
    }
}

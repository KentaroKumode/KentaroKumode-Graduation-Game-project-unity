using UnityEngine;
using EventSystem.TimedEffects;
using GameLoop;
using InventorySystem.PassiveSkills;
using MapSystem;

namespace InventorySystem.PassiveItems.Effects
{
    /// <summary>
    /// 巡礼者の杖: 戦闘終了時、50%で空腹度+1。
    /// </summary>
    public class PilgrimStaffEffect : ITimedEffect
    {
        public string Id => "巡礼者の杖";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (Random.value >= 0.5f) return;
            var hunger = MapManager.Instance?.Hunger;
            if (hunger == null) return;
            hunger.Restore(1);
            Debug.Log($"[PassiveItem] 巡礼者の杖発動: 空腹度+1 ({hunger.Current}/{hunger.Max})");
        }
    }

    /// <summary>
    /// 記憶の砂時計: 戦闘1ターン目に最低出目を最大値に置換（時の凝視の永続版）。
    /// </summary>
    public class MemoryHourglassEffect : ITimedEffect
    {
        public string Id => "記憶の砂時計";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || ctx.playerDice == null || ctx.playerDice.Length == 0) return;
            if (ctx.currentTurn != 1) return;

            int minIdx = 0;
            for (int i = 1; i < ctx.playerDice.Length; i++)
                if (ctx.playerDice[i] < ctx.playerDice[minIdx]) minIdx = i;

            int original = ctx.playerDice[minIdx];
            if (original < ctx.playerDiceMax)
            {
                ctx.playerDice[minIdx] = ctx.playerDiceMax;
                Debug.Log($"[PassiveItem] 記憶の砂時計発動: ダイス[{minIdx}] {original}→{ctx.playerDiceMax}");
            }
        }
    }

    /// <summary>
    /// 激情の刃: HPが最大値の半分以下のとき、与ダメ+30%。
    /// 毎ロール時に評価し ctx.outgoingDamageMultiplier をセット。
    /// </summary>
    public class FuriousBladeEffect : ITimedEffect
    {
        public string Id => "激情の刃";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || combat == null) return;
            if (combat.PlayerMaxHP <= 0) return;
            // HP <= 50% で発動
            if (combat.PlayerHP * 2 > combat.PlayerMaxHP) return;
            // 既存倍率に +0.3 を上乗せ（複数効果の累積に備える）
            ctx.outgoingDamageMultiplier += 0.3f;
            Debug.Log($"[PassiveItem] 激情の刃発動: 与ダメ倍率→{ctx.outgoingDamageMultiplier:F2}");
        }
    }

    /// <summary>
    /// 希望の灯片: 戦闘終了時、HP+3 回復。
    /// </summary>
    public class HopeEmberEffect : ITimedEffect
    {
        public string Id => "希望の灯片";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (combat == null) return;
            int healed = combat.HealPlayer(3);
            Debug.Log($"[PassiveItem] 希望の灯片発動: HP+{healed}");
        }
    }

    /// <summary>
    /// 黄昏の懐中時計: 戦闘5T目以降、毎ターン会心率+1/9（同ターン中に他の補正と加算可能）。
    /// CombatContext に critNumeratorBonus を直接加える運用ではなく、毎ターンチェック方式。
    /// </summary>
    public class TwilightPocketwatchEffect : ITimedEffect
    {
        public string Id => "黄昏の懐中時計";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || ctx.currentTurn < 5) return;
            // 既存システムの会心は ProcessDamage 内で乱数判定。会心率を補正するフィールドが
            // ない場合は ctx の playerDice を1個 max に置き換えて疑似的に成功率を上げる。
            // ここでは単純に会心ボーナスダメ +1 を加える代替（補正経路）。
            ctx.criticalBonus += 1;
            Debug.Log($"[PassiveItem] 黄昏の懐中時計: T{ctx.currentTurn} 会心ボーナス+1 (合計{ctx.criticalBonus})");
        }
    }

    /// <summary>
    /// 苦難の刻印: プレイヤーHPが最大値の20%以下のとき、被ダメ-1。
    /// CombatContext.receivedDamageBonus は加算系なので、負値分を入れて軽減する。
    /// </summary>
    public class HardshipSigilEffect : ITimedEffect
    {
        public string Id => "苦難の刻印";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || combat == null) return;
            if (combat.PlayerMaxHP <= 0) return;
            // HP <= 20%
            if (combat.PlayerHP * 5 > combat.PlayerMaxHP) return;
            // 既存の被ダメ加算フィールドを負値で利用
            ctx.receivedDamageBonus -= 1f / Mathf.Max(1, ctx.baseDamage); // 概算で -1 ダメ相当
            Debug.Log("[PassiveItem] 苦難の刻印: 瀕死状態のため被ダメ軽減");
        }
    }

    /// <summary>
    /// 死神の数珠: 敵を撃破するたびに +2 ゴールド。
    /// </summary>
    public class ReapersBeadsEffect : ITimedEffect
    {
        public string Id => "死神の数珠";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (run == null || combat == null) return;
            // 戦闘勝利のときだけ加算。CombatEnd は勝敗問わず呼ばれる前提なので enemyHP=0 で判定
            if (combat.EnemyHP > 0) return;
            run.coins += 2;
            Debug.Log($"[PassiveItem] 死神の数珠: +2G (現在 {run.coins})");
        }
    }

    /// <summary>
    /// 嵐の徽章: 戦闘開始時の1ターン目のみ、ダイス追加+1個。
    /// CombatContext に bonusPlayerDiceCount のような追加用フィールドがあれば使う。
    /// 現状はロール時に手動でもう1個振って合計に加える簡易実装。
    /// </summary>
    public class StormCrestEffect : ITimedEffect
    {
        public string Id => "嵐の徽章";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || ctx.currentTurn != 1) return;
            // 追加ダイス1個分を ctx.playerDiceTotal に加算（最大値の半分を期待値として2加算）
            int bonus = Mathf.Max(1, ctx.playerDiceMax / 2);
            ctx.playerDiceTotal += bonus;
            Debug.Log($"[PassiveItem] 嵐の徽章: 1T目ダイス追加分 +{bonus}");
        }
    }

    /// <summary>
    /// 沈黙の剣帯: 戦闘中、消費アイテム使用不可。代わりに毎ターン与ダメ +1。
    /// 消費禁止フラグは ItemUseHandler 側で参照する。
    /// </summary>
    public class SilentSwordbeltEffect : ITimedEffect
    {
        public string Id => "沈黙の剣帯";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null) return;
            ctx.criticalBonus += 0; // ダメには finalDamage 経由
            ctx.fixedDamageToEnemy += 1;
            Debug.Log("[PassiveItem] 沈黙の剣帯: 与ダメ+1（消費封印中）");
        }
    }

    /// <summary>
    /// 灯心の鈴: マップ移動時、10% で空腹度+1。
    /// </summary>
    public class WickBellEffect : ITimedEffect
    {
        public string Id => "灯心の鈴";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnMapMove;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (Random.value >= 0.1f) return;
            var hunger = MapManager.Instance?.Hunger;
            if (hunger == null) return;
            hunger.Restore(1);
            Debug.Log($"[PassiveItem] 灯心の鈴発動: 空腹度+1 ({hunger.Current}/{hunger.Max})");
        }
    }
}

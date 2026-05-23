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
            ctx.playerFlatDamageReduction += 1;
            Debug.Log("[PassiveItem] 苦難の刻印: 瀕死状態のため被ダメ-1");
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
            // 1/5 デノミ後: 50%で +1G (確定+1Gは強すぎたためナーフ)
            if (UnityEngine.Random.value < 0.5f)
            {
                run.coins += 1;
                Debug.Log($"[PassiveItem] 死神の数珠: +1G (50%抽選成功、現在 {run.coins})");
            }
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

    // ============================================================
    //  HP閾値発動系
    // ============================================================

    /// <summary>狂乱のメダリオン: HP≤25%で与ダメ+50%</summary>
    public class FrenzyMedallionEffect : ITimedEffect
    {
        public string Id => "狂乱のメダリオン";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || combat == null || combat.PlayerMaxHP <= 0) return;
            if (combat.PlayerHP * 4 > combat.PlayerMaxHP) return; // HP > 25%
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 0.5f;
            Debug.Log("[PassiveItem] 狂乱のメダリオン: HP≤25% → 与ダメ+50%");
        }
    }

    /// <summary>不屈の鎧: HP≤50%で被ダメ-2</summary>
    public class UnyieldingArmorEffect : ITimedEffect
    {
        public string Id => "不屈の鎧";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || combat == null || combat.PlayerMaxHP <= 0) return;
            if (combat.PlayerHP * 2 > combat.PlayerMaxHP) return; // HP > 50%
            ctx.playerFlatDamageReduction += 2;
            Debug.Log("[PassiveItem] 不屈の鎧: HP≤50% → 被ダメ-2");
        }
    }

    /// <summary>死神の予感: HP≤20%で会心率+3/9</summary>
    public class DeathOmenEffect : ITimedEffect
    {
        public string Id => "死神の予感";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || combat == null || combat.PlayerMaxHP <= 0) return;
            if (combat.PlayerHP * 5 > combat.PlayerMaxHP) return; // HP > 20%
            ctx.criticalBonus += 3;
            Debug.Log("[PassiveItem] 死神の予感: HP≤20% → 会心率+3/9");
        }
    }

    // ============================================================
    //  歩行HP回復系
    // ============================================================

    /// <summary>歩行HP回復系の共通処理。</summary>
    internal static class StepHealHelper
    {
        public static void StepHeal(int amount, string name)
        {
            var run = GameManager.Instance?.Run;
            if (run == null) return;
            int oldHP = run.playerHP;
            run.playerHP = Mathf.Min(run.playerMaxHP, run.playerHP + amount);
            if (run.playerHP != oldHP)
                Debug.Log($"[PassiveItem] {name}: HP+{run.playerHP - oldHP} ({run.playerHP}/{run.playerMaxHP})");
        }
    }

    public class CalmShoesEffect : ITimedEffect
    {
        public string Id => "安らぎの靴";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnMapMove;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat) => StepHealHelper.StepHeal(1, Id);
    }

    public class HealingShoesEffect : ITimedEffect
    {
        public string Id => "癒しの靴";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnMapMove;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat) => StepHealHelper.StepHeal(2, Id);
    }

    public class HolyShoesEffect : ITimedEffect
    {
        public string Id => "神聖の靴";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnMapMove;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat) => StepHealHelper.StepHeal(3, Id);
    }

    // ============================================================
    //  その他高レア
    // ============================================================

    /// <summary>黄金の天秤: 戦闘勝利時 +5G</summary>
    public class GoldenScaleEffect : ITimedEffect
    {
        public string Id => "黄金の天秤";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (run == null || combat == null) return;
            if (combat.EnemyHP > 0) return; // 勝利ではない
            int gain = LastStand.FilterGoldGain(run, 5);
            run.coins += gain;
            if (gain > 0) Debug.Log($"[PassiveItem] 黄金の天秤: +{gain}G");
        }
    }

    /// <summary>倍音のクロック: 戦闘ターン数が3の倍数のとき与ダメ×2</summary>
    public class HarmonicClockEffect : ITimedEffect
    {
        public string Id => "倍音のクロック";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || ctx.currentTurn <= 0 || ctx.currentTurn % 3 != 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 1f; // 1.0 → 2.0
            Debug.Log($"[PassiveItem] 倍音のクロック: T{ctx.currentTurn} 与ダメ×2");
        }
    }

    /// <summary>静寂のローブ: 戦闘1ターン目のみ敵パッシブ発動しない</summary>
    public class SilentRobeEffect : ITimedEffect
    {
        public string Id => "静寂のローブ";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatStart;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null) return;
            ctx.enemyPassivesDisabledTurns = 1;
            Debug.Log("[PassiveItem] 静寂のローブ: 1ターン目の敵パッシブを封印");
        }
    }

    /// <summary>黒煙の符: 戦闘開始時、敵に出血+2付与</summary>
    public class BlackSmokeTalismanEffect : ITimedEffect
    {
        public string Id => "黒煙の符";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatStart;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null) return;
            ctx.enemyBleedStacks += 2;
            Debug.Log("[PassiveItem] 黒煙の符: 敵に出血+2スタック付与");
        }
    }

    /// <summary>蒼穹の眼: 戦闘1ターン目のロール必ず会心ヒット</summary>
    public class AzureEyeEffect : ITimedEffect
    {
        public string Id => "蒼穹の眼";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || ctx.currentTurn != 1) return;
            ctx.criticalBonus = Mathf.Max(ctx.criticalBonus, 9);
            Debug.Log("[PassiveItem] 蒼穹の眼: 1ターン目会心確定");
        }
    }

    /// <summary>鋼の心臓: 戦闘終了時HP+5（最大HP+20は獲得時ボーナスで適用）</summary>
    public class IronHeartEffect : ITimedEffect
    {
        public string Id => "鋼の心臓";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (combat == null) return;
            int healed = combat.HealPlayer(5);
            Debug.Log($"[PassiveItem] 鋼の心臓: HP+{healed}");
        }
    }

    /// <summary>守護天使の鈴: 戦闘中1回限り、HP25以下になったターン末にHP+15</summary>
    public class GuardianAngelBellEffect : ITimedEffect
    {
        public string Id => "守護天使の鈴";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnTurnEnd;
        private const string Key = "guardian_angel_used";
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || combat == null) return;
            if (ctx.accumulatedValues.TryGetValue(Key, out var used) && used > 0f) return;
            if (combat.PlayerHP <= 0 || combat.PlayerHP > 25) return;
            int healed = combat.HealPlayer(15);
            ctx.accumulatedValues[Key] = 1f;
            Debug.Log($"[PassiveItem] 守護天使の鈴: HP+{healed}（戦闘中1回限り）");
        }
    }

    /// <summary>災厄の指輪: 被弾するたび次の与ダメ+2累積（上限+10、戦闘終了リセット）</summary>
    public class CalamityRingEffect : ITimedEffect
    {
        public string Id => "災厄の指輪";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        private const string Stack = "calamity_stack";
        private const string LastHP = "calamity_last_hp";

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || combat == null) return;

            // 前ターンとの HP 差分で被弾を検知してスタックを加算
            if (ctx.accumulatedValues.TryGetValue(LastHP, out var prevHP))
            {
                if (combat.PlayerHP < prevHP)
                {
                    float cur = 0f; ctx.accumulatedValues.TryGetValue(Stack, out cur);
                    ctx.accumulatedValues[Stack] = Mathf.Min(10f, cur + 2f);
                }
            }
            ctx.accumulatedValues[LastHP] = combat.PlayerHP;

            // 蓄積分を与ダメに加算（合計上限+10）
            if (ctx.accumulatedValues.TryGetValue(Stack, out var stack) && stack > 0f)
            {
                ctx.finalDamage += (int)stack;
                Debug.Log($"[PassiveItem] 災厄の指輪: 与ダメ+{(int)stack}（被弾蓄積）");
            }
        }
    }

    /// <summary>永遠の燈: 戦闘終了時HP10以下ならHP+20</summary>
    public class EternalLanternEffect : ITimedEffect
    {
        public string Id => "永遠の燈";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (combat == null || combat.PlayerHP <= 0 || combat.PlayerHP > 10) return;
            int healed = combat.HealPlayer(20);
            Debug.Log($"[PassiveItem] 永遠の燈: 瀕死回復 HP+{healed}");
        }
    }
}

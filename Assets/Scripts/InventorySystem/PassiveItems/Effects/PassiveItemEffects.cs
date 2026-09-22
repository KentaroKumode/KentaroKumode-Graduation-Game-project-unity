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
    // 2026-07-18 死コード掃除: MemoryHourglass/ReapersBeads/StormCrest/SilentSwordbelt/FrenzyMedallion/
    //  SilentRobe/BlackSmokeTalisman/AzureEye/GuardianAngelBell の 9 effect を class ごと削除 (対応アイテム削除済み)
    public class PilgrimStaffEffect : ITimedEffect
    {
        public string Id => "心軽めの巡礼杖";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            // 飢餓→希望統合(ADR-0002): 旧「空腹度+1」を希望+1へ
            // 2026-09-05: **50% 抽選を廃止して常時発動**。 期待値 +0.5/戦闘 では
            //   希望の減り (戦闘あたり -4〜-6) に対して桁が合わず、 準パワー -1.60 だった。
            if (run == null) return;
            GameLoop.HopeSystem.ApplyFood(run, 1);
            Debug.Log($"[PassiveItem] 巡礼者の杖発動: 希望+1 ({run.hope}/{run.hopeCap})");
        }
    }


    /// <summary>
    /// 希望の灯片: 戦闘中にロール敗北を一度もせずに勝利した時、最大HP+2（永続・無限スタック）。
    /// 実装: CombatEnd で勝利かつ ctx.rollLossOccurredThisCombat==false なら playerMaxHP+2。
    /// PassiveSkillManager の敗北処理で rollLossOccurredThisCombat=true がセットされる。
    /// </summary>
    public class HopeEmberEffect : ITimedEffect
    {
        public string Id => "希望の灯片";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || combat == null || run == null) return;
            // 勝利判定: 敵HP<=0
            if (combat.EnemyHP > 0) return;
            // 敗北ロールが一度でもあれば不発
            if (ctx.rollLossOccurredThisCombat) return;
            // 永続バフ: ランの最大HP+2 (戦闘外でも残る・無限スタック)
            run.playerMaxHP += 2;
            run.playerHP = Mathf.Min(run.playerMaxHP, run.playerHP + 2);
            Debug.Log($"[PassiveItem] 希望の灯片発動: 無敗勝利→ 最大HP+2 (現在 {run.playerMaxHP})");
        }
    }




    /// <summary>道銭の帯封: 層に入るたび ゴールド+6 (2026-09-15)。
    ///
    /// <para><b>層はランに 8 回しか来ない</b>ので 1 回の量を大きく取れる (+48G ≒ 獲得 316G の 15%)。
    /// マップ移動系 (1 ラン 30〜40 回) と同じ量にすると桁が変わってしまう。</para>
    ///
    /// <para><b>ゴールドは倍率チェーンを通らない</b>ので、 攻撃や被ダメ軽減と違って
    /// 値が暴れない ── atkBase に乗る量は実測で ×4.16 されるが、 金は金のまま。
    /// 「基礎的な小品」を置ける数少ない軸。</para></summary>
    public class RoadMoneyBandEffect : ITimedEffect
    {
        private const int Gain = 6;
        public string Id => "切り分けた帯封";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnFloorEnter;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (run == null) return;
            GameLoop.GoldIncome.Gain(run, Gain, "切り分けた帯封");
            Debug.Log($"[PassiveItem] 道銭の帯封: 層突入 ゴールド+{Gain} (所持 {run.coins})");
        }
    }

    // ============================================================
    //  HP閾値発動系
    // ============================================================


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
        public string Id => "継ぎ革の旅靴";
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
        public string Id => "聖路の白靴";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnMapMove;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat) => StepHealHelper.StepHeal(3, Id);
    }

    // ============================================================
    //  その他高レア
    // ============================================================

    /// <summary>黄金の天秤: 戦闘勝利時 +10G (2026-09-05 に +5G から増額)。
    /// 経済のインフレに追随できておらず、 準パワー -1.13 / regβ -0.138 (2σ有意) ──
    /// 枠を 1 つ潰して +5G は、 同じ枠に入る戦闘系パッシブに明確に負けていた。</summary>
    public class GoldenScaleEffect : ITimedEffect
    {
        public string Id => "戦果で傾く天秤";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (run == null || combat == null) return;
            if (combat.EnemyHP > 0) return; // 勝利ではない
            int gain = GameLoop.GoldIncome.Gain(run, 10, "戦果で傾く天秤");
            if (gain > 0) Debug.Log($"[PassiveItem] 黄金の天秤: +{gain}G");
        }
    }




    /// <summary>鋼の心臓: 戦闘終了時HP+5（最大HP+20は獲得時ボーナスで適用）</summary>
    public class IronHeartEffect : ITimedEffect
    {
        public string Id => "脈なしの鋼心臓";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (combat == null) return;
            int healed = combat.HealPlayer(5);
            Debug.Log($"[PassiveItem] 鋼の心臓: HP+{healed}");
        }
    }


    /// <summary>災厄の指輪 (リワーク 2026-05-30): 被弾するたび次の与ダメ+3累積 (上限+15、戦闘終了リセット)。
    /// 旧+2/+10 → +3/+15 で 1.5倍化。</summary>
    public class CalamityRingEffect : ITimedEffect
    {
        public string Id => "傷覚えの災環";
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
                    ctx.accumulatedValues[Stack] = Mathf.Min(15f, cur + 3f);
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

    /// <summary>永遠の燈 (リワーク 2026-05-30): 戦闘終了時 HPが最大の25%以下なら最大HPの50%まで回復。</summary>
    public class EternalLanternEffect : ITimedEffect
    {
        public string Id => "鑑定済みの無害灯";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (combat == null || combat.PlayerHP <= 0) return;
            int maxHP = combat.PlayerMaxHP;
            int curHP = combat.PlayerHP;
            // HPが maxHP×25% 以下なら発動
            if (curHP * 4 > maxHP) return;
            int targetHP = Mathf.CeilToInt(maxHP * 0.50f);
            int healAmount = System.Math.Max(0, targetHP - curHP);
            if (healAmount <= 0) return;
            int healed = combat.HealPlayer(healAmount);
            Debug.Log($"[PassiveItem] 永遠の燈: 瀕死回復 HP {curHP}→{curHP + healed} (max{maxHP} の50%まで)");
        }
    }

    // ============================================================
    //  佯狂者シリーズ（発狂連動・ADR-0002）。鈴は店フックのため非登録。
    // ============================================================

    /// <summary>佯狂者の杖: [発狂]中、他の[佯狂者]アイテムの数×1 をダイス合計に加える。</summary>
    public class YokyoStaffEffect : ITimedEffect
    {
        public string Id => YokyoSet.Staff;
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || run == null || !HopeSystem.IsMadness(run)) return;
            int n = YokyoSet.OtherCount(run, Id);
            if (n <= 0) return;
            ctx.playerDiceTotal += n;
            Debug.Log($"[佯狂者の杖] 発狂中: ダイス合計+{n}");
        }
    }

    /// <summary>佯狂者の衣: [発狂]中、他の[佯狂者]アイテムの数×30% の与ダメージ増加。</summary>
    public class YokyoGarbEffect : ITimedEffect
    {
        public string Id => YokyoSet.Garb;
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || run == null || !HopeSystem.IsMadness(run)) return;
            int n = YokyoSet.OtherCount(run, Id);
            if (n <= 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 0.30f * n;
            Debug.Log($"[佯狂者の衣] 発狂中: 与ダメ+{30 * n}%");
        }
    }

    /// <summary>佯狂者の冠（与ダメ部分）: フルセット時のみ、狂気スタック×4% の与ダメージ増加。
    /// 希望0固定・燃え尽き終了・スタック蓄積は HopeSystem 側で処理する。</summary>
    public class YokyoCrownEffect : ITimedEffect
    {
        public string Id => YokyoSet.Crown;
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || run == null || !HopeSystem.IsMadness(run)) return;
            if (!YokyoSet.IsFullSet(run)) return;     // 与ダメスケールはフルセット限定
            int stack = run.madnessStack;
            if (stack <= 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 0.04f * stack;
            Debug.Log($"[佯狂者の冠] 発狂スタック{stack}: 与ダメ+{stack * 4}%");
        }
    }

    // ============================================================
    //  2026-06-03 新規追加アイテム（ITimedEffect系）
    // ============================================================

    /// <summary>巡礼の杖飾り (BRONZE): マップ移動時、25%で希望+1。歩み続けるほど心が慰められる、
    /// 希望ビルドの序盤の足場。希望システム(ADR-0002)へ直接接続。</summary>
    public class PilgrimCharmEffect : ITimedEffect
    {
        public string Id => "四歩返しの杖飾り";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnMapMove;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (run == null || GameLoop.GameRng.Value("passiveItem.staffCharm") >= 0.25f) return;
            HopeSystem.ApplyFood(run, 1);
            Debug.Log($"[PassiveItem] 巡礼の杖飾り: 希望+1 ({run.hope}/{run.hopeCap})");
        }
    }

    /// <summary>幸運の硬貨: 戦闘勝利時 ゴールド+3 (2026-09-18)。
    /// 〈黄金の天秤〉(+10G/勝) の 3 割。 イベントで無料で渡されるので BRONZE 相当に抑える。</summary>
    public class LuckyCoinEffect : ITimedEffect
    {
        private const int Gain = 3;
        public string Id => "幸運の硬貨";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatEnd;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (run == null || combat == null || combat.EnemyHP > 0) return;
            GameLoop.GoldIncome.Gain(run, Gain, "幸運の硬貨");
        }
    }

    /// <summary>相棒の魂: 戦闘開始時 シールド+3 (2026-09-18)。
    /// 当初案は「ランに 1 度 HP1 で踏みとどまる」だったが、 救済は既に
    /// 灯火 → ラストスタンド → フルーレ の 3 段があり順序の規則 (LastStand.cs) が重いので見送った。</summary>
    public class CompanionSoulEffect : ITimedEffect
    {
        private const int Shield = 3;
        public string Id => "相棒の魂";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.CombatStart;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null) return;
            ctx.consShield += Shield;
            CombatSystem.ShieldDiag.Note("相棒の魂", Shield);
            ctx.shieldGainedTotal += Shield;
        }
    }

}

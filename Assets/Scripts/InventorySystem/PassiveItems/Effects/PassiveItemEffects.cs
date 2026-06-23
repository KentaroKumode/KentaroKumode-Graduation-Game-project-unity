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
            // 飢餓→希望統合(ADR-0002): 旧「空腹度+1」を希望+1へ
            if (run == null || Random.value >= 0.5f) return;
            GameLoop.HopeSystem.ApplyFood(run, 1);
            Debug.Log($"[PassiveItem] 巡礼者の杖発動: 希望+1 ({run.hope}/{run.hopeCap})");
        }
    }

    /// <summary>
    /// 記憶の砂時計: 3ターンごとに、その3T区間で敵に与えた累積ダメの30%を
    /// 軽減不可ダメージとして敵にもう一度叩き込む（蓄積はその度にリセット）。
    /// 実装:
    ///  ・OnTurnEnd で毎ターン、前回スナップショットからの敵HP減少量を hourglassPendingDamageWindow に加算
    ///  ・ターンが 3 の倍数(3/6/9...) で 30% を DealFixedDamageToEnemy 経由で叩き、ウィンドウをリセット
    ///  ・スナップショットは ctx.hourglassLastEnemyHPSnap で保持（ここでフィールド名を統一）
    /// </summary>
    public class MemoryHourglassEffect : ITimedEffect
    {
        public string Id => "記憶の砂時計";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnTurnEnd;

        // 直前ターン終了時の敵HPスナップショット (戦闘ごとに先頭で初期化)
        private static int _lastEnemyHPSnap;
        private static bool _initialized;
        private static int _ownerCombatToken;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || combat == null || !combat.IsCombatActive) return;

            // CombatStart で初期化されない設計のため、 token (currentTurn==1 で reset) で疑似初期化
            int token = combat.GetHashCode() ^ combat.EnemyMaxHP;
            if (!_initialized || _ownerCombatToken != token || ctx.currentTurn <= 1)
            {
                _lastEnemyHPSnap = combat.EnemyHP;
                _ownerCombatToken = token;
                _initialized = true;
                ctx.hourglassPendingDamageWindow = 0;
            }

            // このターンに与えたダメージ = 前回スナップ - 現在敵HP
            int dealt = System.Math.Max(0, _lastEnemyHPSnap - combat.EnemyHP);
            ctx.hourglassPendingDamageWindow += dealt;
            _lastEnemyHPSnap = combat.EnemyHP;

            // 3ターン区切りで30%を返却
            if (ctx.currentTurn > 0 && ctx.currentTurn % 3 == 0 && ctx.hourglassPendingDamageWindow > 0)
            {
                int rebound = Mathf.CeilToInt(ctx.hourglassPendingDamageWindow * 0.30f);
                Debug.Log($"[PassiveItem] 記憶の砂時計発動: T{ctx.currentTurn} 蓄積{ctx.hourglassPendingDamageWindow} → 軽減不可+{rebound}");
                combat.DealFixedDamageToEnemy(rebound);
                ctx.hourglassPendingDamageWindow = 0;
                _lastEnemyHPSnap = combat.EnemyHP; // 自前ダメ後の値で続行
            }
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
    /// さらに「最初のターンの相手のダイスロール結果から-99」＝1T目のみ敵ダイス合計を-99する
    /// (enemyDiceTotalPenalty・床なし)。実質1T目は確定大差勝ち＋基礎ダメ激増の先制必殺。
    /// 消費禁止フラグは ItemUseHandler 側で参照する。
    /// </summary>
    public class SilentSwordbeltEffect : ITimedEffect
    {
        public string Id => "沈黙の剣帯";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;

        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null) return;
            ctx.fixedDamageToEnemy += 1; // 消費封印の見返り（毎ターン）
            // 1ターン目: 敵ダイス合計-99（床なし）。ProcessPostRoll 前なので勝敗・基礎ダメに反映される。
            if (ctx.currentTurn == 1 && !ctx.rollPurity)
            {
                ctx.enemyDiceTotalPenalty += 99;
                Debug.Log("[PassiveItem] 沈黙の剣帯: 1T目 敵ダイス合計-99（先制必殺）");
            }
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
            // 飢餓→希望統合(ADR-0002): 旧「空腹度+1」を希望+1へ
            if (run == null || Random.value >= 0.1f) return;
            GameLoop.HopeSystem.ApplyFood(run, 1);
            Debug.Log($"[PassiveItem] 灯心の鈴発動: 希望+1 ({run.hope}/{run.hopeCap})");
        }
    }

    // ============================================================
    //  HP閾値発動系
    // ============================================================

    /// <summary>狂乱のメダリオン: HP≤25%で与ダメ+50%</summary>
    /// <summary>狂乱のメダリオン (リワーク 2026-05-30): HP≤50% で +30%、 HP≤25% で +60% (差分計算で重複適用しない)。</summary>
    public class FrenzyMedallionEffect : ITimedEffect
    {
        public string Id => "狂乱のメダリオン";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || combat == null || combat.PlayerMaxHP <= 0) return;
            int hp = combat.PlayerHP, max = combat.PlayerMaxHP;
            float bonus = 0f;
            if (hp * 4 <= max)      bonus = 0.60f; // ≤25%
            else if (hp * 2 <= max) bonus = 0.30f; // ≤50%
            else return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += bonus;
            Debug.Log($"[PassiveItem] 狂乱のメダリオン: HP{hp}/{max} → 与ダメ+{(int)(bonus*100)}%");
        }
    }

    /// <summary>末那識 (リワーク 2026-05-30): HP≤35% で会心確定 + 与ダメ×1.3 (旧 HP≤20% で会心のみ)。
    /// 閾値緩和+追加倍率で発動機会と威力を 2倍化。</summary>
    public class ManashikiEffect : ITimedEffect
    {
        public string Id => "末那識";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || combat == null || combat.PlayerMaxHP <= 0) return;
            // HP×100/Max ≤ 35 → 発動
            if (combat.PlayerHP * 100 > combat.PlayerMaxHP * 35) return;
            ctx.forceCritical = true;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 0.3f;
            Debug.Log($"[PassiveItem] 末那識: HP{combat.PlayerHP}/{combat.PlayerMaxHP} → 会心確定+30%");
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

    /// <summary>倍音のクロック (2026-06-03 リバフ): 3の倍数ターンで outgoing+=1.0 (×2.0)。
    /// 旧 +0.5 (×1.5) → +1.0 (×2.0)。 不発ターンが多くE帯だったため発火時の威力を倍化。</summary>
    public class HarmonicClockEffect : ITimedEffect
    {
        public string Id => "倍音のクロック";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || ctx.currentTurn <= 0 || ctx.currentTurn % 3 != 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 1.0f; // 1.0 → 2.0
            Debug.Log($"[PassiveItem] 倍音のクロック: T{ctx.currentTurn} 拍 与ダメ×2.0");
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

    /// <summary>災厄の指輪 (リワーク 2026-05-30): 被弾するたび次の与ダメ+3累積 (上限+15、戦闘終了リセット)。
    /// 旧+2/+10 → +3/+15 で 1.5倍化。</summary>
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
        public string Id => "永遠の燈";
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
        public string Id => "巡礼の杖飾り";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnMapMove;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (run == null || Random.value >= 0.25f) return;
            HopeSystem.ApplyFood(run, 1);
            Debug.Log($"[PassiveItem] 巡礼の杖飾り: 希望+1 ({run.hope}/{run.hopeCap})");
        }
    }

    /// <summary>狂宴の仮面 (SILVER): 希望が低いほど与ダメージ上昇（悲観以下+10% / 絶望以下+25%）。
    /// 発狂(佯狂者)ビルドや低希望ハイリスク戦法を火力で報いる。OnRoll で毎ターン再適用。</summary>
    public class RevelMaskEffect : ITimedEffect
    {
        public string Id => "狂宴の仮面";
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || run == null) return;
            var tier = HopeSystem.GetTier(run);
            float add;
            if (tier >= HopeTier.Despair) add = 0.25f;       // 絶望・発狂
            else if (tier >= HopeTier.Pessimism) add = 0.10f; // 悲観
            else return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += add;
        }
    }
}

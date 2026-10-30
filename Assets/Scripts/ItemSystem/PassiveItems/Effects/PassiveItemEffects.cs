using UnityEngine;
using EventSystem.TimedEffects;
using GameLoop;
using ItemSystem.PassiveSkills;
using MapSystem;

namespace ItemSystem.PassiveItems.Effects
{
    /// <summary>
    /// 巡礼者の杖: 戦闘終了時、50%で空腹度+1。
    /// </summary>
    // 2026-07-18 死コード掃除: MemoryHourglass/ReapersBeads/StormCrest/SilentSwordbelt/FrenzyMedallion/
    //  SilentRobe/BlackSmokeTalisman/AzureEye/GuardianAngelBell の 9 effect を class ごと削除 (対応アイテム削除済み)






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





    // ============================================================
    //  その他高レア
    // ============================================================









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
            if (ctx == null || run == null || !ProvisionSystem.IsExhausted(run)) return;
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
            if (ctx == null || run == null || !ProvisionSystem.IsExhausted(run)) return;
            int n = YokyoSet.OtherCount(run, Id);
            if (n <= 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 0.30f * n;
            Debug.Log($"[佯狂者の衣] 発狂中: 与ダメ+{30 * n}%");
        }
    }

    /// <summary>佯狂者の冠（与ダメ部分）: フルセット時のみ、狂気スタック×4% の与ダメージ増加。
    /// 物資0固定・燃え尽き終了・スタック蓄積は ProvisionSystem 側で処理する。</summary>
    public class YokyoCrownEffect : ITimedEffect
    {
        public string Id => YokyoSet.Crown;
        public TimedEffectTrigger Trigger => TimedEffectTrigger.OnRoll;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat)
        {
            if (ctx == null || run == null || !ProvisionSystem.IsExhausted(run)) return;
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



}

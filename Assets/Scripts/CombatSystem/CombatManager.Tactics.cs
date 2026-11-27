using System;
using ItemSystem.PassiveSkills;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>CombatManager: 戦術消耗品 (2026-11-20・item-overhaul-spec §7-1) のうち、 戦闘の中の状態 (敵の HP・ダウンの目盛り・撃ごとの値) に触る分。
    /// 使う判断と量の定数は <see cref="GameLoop.Consumables"/> (人の画面も BOT も <see cref="GameLoop.Consumables.Use"/> を通る)。 使えるのは判断の間 (振り直し・配線) だけ。</summary>
    public partial class CombatManager
    {
        /// <summary>〈急所突きの針〉を仕込んだ印 (ctx.accumulatedValues・1 = 次の 1 撃)。 攻撃する撃が来るまで (攻撃しないターンをまたいで) 残る。</summary>
        public const string TacticNeedleKey = "tactic.needle";
        /// <summary>〈鎧割りの酸瓶〉をこの戦闘で使った数 (ctx.accumulatedValues)。 ヴェスカの形態の交代 (防御を入れ直す) の後にも同じだけ引く。</summary>
        public const string TacticAcidKey = "tactic.acid";

        /// <summary>〈瞬光ランタン〉: 敵に <paramref name="dmg"/> のダメージ。 敵の HP を減らす口 (<see cref="HpLossDiag.Lose"/>) を通すので、 最終与ダメージ倍率・怠惰・ダウンの ×1.5 は
        /// 主攻撃以外と同じに掛かる (敵の防御は掛からない)。 目盛りは<b>通常攻撃として</b>割る (<see cref="NoteBossDownMainHit"/>・ひびの入った目盛りもダウン)。
        /// 戻り値 = 使えたか (敵が倒れていれば使えない)。</summary>
        /// <summary>〈瞬光ランタン〉の混乱 +N (2026-11-26・仮置き)。</summary>
        public const int ChiselGauge = 10;

        public bool TacticChisel(CombatContext ctx, int dmg)
        {
            if (!isCombatActive || ctx == null || enemyHP <= 0 || dmg <= 0) return false;
            int before = enemyHP;
            HpLossDiag.Lose(ref enemyHP, dmg, "瞬光ランタン");
            int dealt = before - enemyHP;
            ctx.enemyCurrentHP = enemyHP;
            EnemyHpLedger.CtxOverwritten();   // [計装]
            if (enemyHP > 0) AddBossGauge(ChiselGauge);   // 2026-11-26: 目盛り → 混乱ゲージ
            if (Debug.unityLogger.logEnabled) Debug.Log($"[瞬光ランタン] 敵に {dealt}・混乱 +{ChiselGauge} → 敵HP {enemyHP}");
            return true;
        }

        /// <summary>撃の頭 (ADR-0016 の撃のループ・<paramref name="h"/> = 撃の番号): 〈急所突きの針〉が仕込まれていて、 これが攻撃のある最初の撃なら印を消して true。</summary>
        private static bool TakeTacticNeedle(CombatContext ctx, int h, bool attacks)
        {
            if (ctx == null || h != 0 || !attacks || ctx.GetAccumulated(TacticNeedleKey) <= 0f) return false;
            ctx.accumulatedValues[TacticNeedleKey] = 0f;
            return true;
        }

        /// <summary>形態の交代で敵の防御を入れ直した後: 〈鎧割りの酸瓶〉の分を引き直す (「この戦闘中」なのでヴェスカの連戦の後の段にも効く・下限 0)。</summary>
        private static void ReapplyTacticAcid(CombatContext ctx)
        {
            int n = ctx != null ? (int)ctx.GetAccumulated(TacticAcidKey) : 0;
            if (n > 0) ctx.enemyDefense = Math.Max(0, ctx.enemyDefense - GameLoop.Consumables.AcidDefenseCut * n);
        }
    }
}

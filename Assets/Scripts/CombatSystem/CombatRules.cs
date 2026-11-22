using ItemSystem.PassiveSkills.Effects;

namespace CombatSystem
{
    /// <summary>1 ターンの<b>物理的な規則の式</b>の正本 (2026-11-12・2026-11-21 武器のリワーク)。
    ///
    /// <para>本体の効果クラス (武器のスキル WeaponFamilyEffects.cs・処刑人の烙印・攻撃に挿した出目の上乗せ) と、
    /// BOT の見積もり (<c>AutoTest.TurnRules</c> / <c>AutoTest.WeaponRungModel</c> 経由の Optimal / Super) が<b>同じ関数</b>を呼ぶ。</para>
    ///
    /// <para>率は整数の % で持ち、 整数で切り上げ/切り捨てる (float の率は 5 × 1.2f = 6.0000002 のように丸めがずれる)。</para></summary>
    public static class CombatRules
    {
        private static int CeilPct(int v, int pct) => v <= 0 ? 0 : (v * pct + 99) / 100;

        /// <summary>剣〈切り返し〉: ブロック合計 × <see cref="GuardCut.RatePct"/>% (切り上げ) を攻撃へ。</summary>
        public static int GuardCutBonus(int blockSum) => CeilPct(blockSum, GuardCut.RatePct);

        /// <summary>剣〈不滅の刃〉: 連続 <paramref name="streak"/> ターン目の最終与ダメージ +% (12% × 連続・最大 72%)。</summary>
        public static int UndyingPct(int streak) => streak <= 0 ? 0 : System.Math.Min(UndyingBlade.MaxPct, UndyingBlade.PerTurn * streak);

        /// <summary>盾〈シールドバッシュ〉で使うシールド (I = 半分・切り上げ / II = 全部)。</summary>
        public static int BashSpend(int shield, bool all) => shield <= 0 ? 0 : all ? shield : (shield + 1) / 2;

        /// <summary>盾〈シールドバッシュ〉: 使ったシールド × 率% (切り上げ) を攻撃へ。</summary>
        public static int BashBonus(int spent, int ratePct) => CeilPct(spent, ratePct);

        /// <summary>盾〈城壁〉: 【ジャスト防御】なら固定量。 そうでなく完全に受け止めたら (ブロック ≥ 敵の攻撃)、 余ったブロック × 率% (切り捨て)。 受け止められなければ 0。</summary>
        public static int BulwarkGain(int block, int enemyAtk, int ratePct, bool justGuard, int justShield)
        {
            if (justGuard) return justShield;
            return block > 0 && block >= enemyAtk ? (block - enemyAtk) * ratePct / 100 : 0;
        }

        /// <summary>盾〈黎明〉: 敵の最大 HP を削る量 (最低 1)。 <paramref name="units"/> = 軽減前の敵の攻撃 / <see cref="Daybreak.PerUnit"/> (0 以下で呼ばない)。 ジャスト防御なら 8%・それ以外 5%。</summary>
        public static int DaybreakCut(int enemyMaxHp, int units, bool justGuard)
            => System.Math.Max(1, enemyMaxHp * (justGuard ? Daybreak.JustPctPerUnit : Daybreak.PctPerUnit) * units / 100);

        /// <summary>短剣〈処刑〉の最終与ダメージ +% と、 それが掛かる敵の残り HP (%以下)・即座に倒す線 (通常 / ボス)。</summary>
        public const int ExecutionFinalPct = 50, ExecutionBoostHpPct = 40, ExecutionKillPct = 15, ExecutionKillPctBoss = 10;

        public static bool ExecutionBoostOn(int hp, int maxHp) => maxHp > 0 && hp > 0 && hp * 100 <= maxHp * ExecutionBoostHpPct;

        public static bool ExecutionKills(int hp, int maxHp, bool boss) => maxHp > 0 && hp * 100 <= maxHp * (boss ? ExecutionKillPctBoss : ExecutionKillPct);

        /// <summary>短剣〈連刃〉: 追撃の攻撃 = 武器の攻撃力 × 率% (切り上げ)。</summary>
        public static int ChainHitBase(int weaponAtk, int ratePct) => CeilPct(weaponAtk, ratePct);

        /// <summary>竜閃〈無我無心〉: 他の端子に挿した目の合計 × 1.5 (切り上げ)。</summary>
        public static int MugaBonus(int otherDiceSum) => CeilPct(otherDiceSum, MugaMushin.RatePct);

        /// <summary>竜閃〈無我無心〉: 攻撃の端子 1 枚のターン、 防御の端子に挿した出目 1 個につきブロック +3 (2026-11-22)。</summary>
        public static int MugaBlock(int blockDice) => blockDice <= 0 ? 0 : blockDice * MugaMushin.BlockPerDie;

        /// <summary>竜閃〈画竜点睛〉の最終与ダメージ +% (攻撃の端子 1 枚の撃・<paramref name="face"/> = その撃に挿した目・<paramref name="rolledMax"/> = このターン振った目の最大・
        /// <paramref name="dieMax"/> = 装備ダイスの最大の目)。 振った目の最大 (タイ含む) なら +100% (×2.0)、 それが装備ダイスの最大の目なら +200% (×3.0・目盛りを割る)。 どちらでもなければ 0 (2026-11-22・final5: 旧 +60% / +150%)。</summary>
        public static int GaryoPct(int face, int rolledMax, int dieMax)
            => face <= 0 || face < rolledMax ? 0 : dieMax > 0 && face >= dieMax ? GaryoTensei.FinalPct : GaryoTensei.RollMaxPct;

        /// <summary>処刑人の烙印のしきい値 (最大 HP 比)。</summary>
        public const float ExecuteHpRatio = 0.20f;

        /// <summary>処刑人の烙印: 攻撃終了時の HP が最大の <see cref="ExecuteHpRatio"/> 以下なら即死。 HP が 0 より大きいかは呼ぶ側が見る。</summary>
        public static bool ExecuteKills(int hp, int maxHp) => hp <= UnityEngine.Mathf.CeilToInt(maxHp * ExecuteHpRatio);

        /// <summary>攻撃に挿した出目への上乗せ (ctx.attackDicePct): 攻撃の出目の合計 × 率 (切り上げ)。</summary>
        public static int AttackDiceScaled(int attackSum, float pct) => UnityEngine.Mathf.CeilToInt(attackSum * pct);
    }
}

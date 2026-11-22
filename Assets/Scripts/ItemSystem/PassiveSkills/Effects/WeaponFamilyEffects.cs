using CombatSystem;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  武器 13 品のスキル (2026-11-21 フルリワーク・full-item-rework-ledger §⑫)
    //
    //  武器は「1 軸専用」ではなく戦闘スタイル。 どの軸を組んでも家系ごとに戦い方が変わる。
    //
    //  | 家系 | A (T2〜) | B (T3〜) | 固有1 (T3+〜) | 固有2 (T4〜) |
    //  |---|---|---|---|---|
    //  | 斧   | 猛り (攻撃 +1/ターン・目盛りへの削り) | 鎧砕き (撃ごとに防御 −N・0 で最終与ダメ +%) | 捨て身 | 血令 |
    //  | 剣   | 間合 (出目 +%・固定削り打ち消し)       | 一対一 (単体戦の最終与ダメ +%・防御)        | 切り返し | 不滅の刃 |
    //  | 短剣 | 疾手 (最初の 5 ターン 攻撃 +N・初手の手札) | 連刃 (攻撃の端子 2 枚以上で追撃 1 撃)     | 急所 | 処刑 |
    //  | 盾   | 城壁 (余りブロック → シールド・ジャスト防御で固定) | シールドバッシュ (防御なしのターンにシールドを攻撃へ) | 堅守の構え | 黎明 |
    //  竜閃 (家系なし): 画竜点睛・無我無心。 秘宝・端子・消耗品の強化はすべて乗る。
    //
    //  **口 (撃ごと・ADR-0016)**: 攻撃 +N は OnPostRoll / OnWired で AddPlayerAttackOrDiceBonus (全部の撃に乗る)。
    //  「最終与ダメージ +%」は撃ごとの ctx.hitFinalDamagePct (OnPreDealDamage・IsRealHit)。
    //  式の数字は CombatRules が正本 (本体と BOT が同じ関数)。
    // ============================================================

    /// <summary>相互攻撃モデルで CombatManager がターンごとに書く値のキー (accumulatedValues)。
    /// 毎ターン配線確定時に上書きされる [perTurn] 扱い。</summary>
    public static class MutualKeys
    {
        public const string AttackDiceCount  = "mutualAttackDiceCount";
        public const string BlockDiceCount   = "mutualBlockDiceCount";
        public const string ChargeDiceCount  = "mutualChargeDiceCount";
        public const string SpecialDiceCount = "mutualSpecialDiceCount";
        /// <summary>敵〈多頭攻撃〉が戦闘の始めに立てる印 (persistent)。 プレイヤーが攻撃よりブロックに多く挿したターン、 敵の攻撃が 2 回になる
        /// (CombatManager.Turn.cs の 2 回目のパケット)。</summary>
        public const string EnemyDoubleOnBlockHeavy = "enemyDoubleOnBlockHeavy";
        /// <summary>攻撃端子に挿した出目の合計と最大値 (上乗せ率をかける前・2026-11-06)。</summary>
        public const string AttackDiceSum    = "mutualAttackDiceSum";
        public const string AttackDiceMax    = "mutualAttackDiceMax";
        /// <summary>攻撃端子に挿した出目の最小値 (攻撃に挿していなければ 0)。</summary>
        public const string AttackDiceMin    = "mutualAttackDiceMin";
        /// <summary>予告の敵攻撃 (防御後)。 2 体戦は 2 体のうち大きい方。</summary>
        public const string EnemyAtk         = "mutualEnemyAtk";
        /// <summary>このターン実際に放たれた敵の直接攻撃の、 プレイヤーの防御を引く前の合計 (2 体戦は 2 体分)。
        /// 敵が行動しなかったら 0。 敵の攻撃の解決後に書く。</summary>
        public const string EnemyAtkPreDefense = "mutualEnemyAtkPreDefense";
        /// <summary>このターン敵の攻撃で減った HP (直接攻撃・固定ダメ・2 体目を含む)。 敵の攻撃の解決後に書く。</summary>
        public const string HpLossFromEnemy  = "mutualHpLossFromEnemy";
    }

    internal static class Fam
    {
        private static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);

        /// <summary>このターンに発動した攻撃の端子の枚数 (OnWired 以降・端子の解決の結果)。</summary>
        public static int AttackTerminals() => CombatManager.Instance?.LastTerminalOutcome?.attackHits.Count ?? 0;
        /// <summary>このターンに発動した防御の端子の枚数 (【再起動】の分も 1 回・OnWired 以降)。</summary>
        public static int BlockTerminals() => CombatManager.Instance?.LastTerminalOutcome?.blockTerminals ?? 0;

        /// <summary>攻撃端子とブロック端子の両方に挿した (剣の条件)。</summary>
        public static bool Split(CombatContext ctx)
            => Count(ctx, MutualKeys.AttackDiceCount) > 0 && Count(ctx, MutualKeys.BlockDiceCount) > 0;

        /// <summary>攻撃の端子に挿し、 防御の端子に 1 つも挿さなかった (全力攻撃・斧〈捨て身〉と盾〈シールドバッシュ〉の条件)。</summary>
        public static bool NoBlock(CombatContext ctx)
            => Count(ctx, MutualKeys.AttackDiceCount) > 0 && Count(ctx, MutualKeys.BlockDiceCount) == 0;

        public static int BlockDice(CombatContext ctx) => Count(ctx, MutualKeys.BlockDiceCount);

        /// <summary>この撃の最終与ダメージ +pct% (本物の撃だけ)。</summary>
        public static void Final(CombatContext ctx, int pct) { if (ctx.IsRealHit && pct > 0) ctx.hitFinalDamagePct += pct; }

        /// <summary>当たった撃のダメージの pct% だけボスの次の目盛りを上げる (目盛りへの削り・目盛りの無い敵では何もしない)。</summary>
        public static void Notch(CombatContext ctx, int pct)
        {
            if (!Psn.Hit(ctx) || pct <= 0) return;
            int add = ctx.finalDamage * pct / 100;
            if (add > 0) CombatManager.Instance?.RaiseBossNextNotch(add);
        }

        public static void GainShield(CombatContext ctx, int amount, string label) => Rw.Shield(ctx, amount, label);
    }

    // ============================================================
    //  斧 — 重撃・装甲粉砕・全力攻撃
    // ============================================================

    /// <summary>猛り I/II/III: 経過ターン 1 ごとに全部の撃の攻撃 +1 (上限 +5/+9/+14)・ボスの目盛りへの削り +15/25/40% (撃のダメージの割合だけ次の目盛りを上げる)。</summary>
    public class Fervor : IPassiveSkillEffect
    {
        public static readonly string[] Ids = { "猛りI", "猛りII", "猛りIII" };
        public static readonly int[] Caps = { 5, 9, 14 }, NotchPct = { 15, 25, 40 };
        private readonly int _lv;
        public Fervor(int lv) { _lv = lv; }
        public string SkillId => Ids[_lv];
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnPostDealDamage) { Fam.Notch(ctx, NotchPct[_lv]); return; }
            int n = System.Math.Min(System.Math.Max(0, ctx.currentTurn - 1), Caps[_lv]);
            if (n > 0) ctx.AddPlayerAttackOrDiceBonus(n);
        }
    }

    /// <summary>鎧砕き I/II: 撃が当たるたび敵の防御 −2/−3 (1 ターン 3 回まで・下限 0・CombatManager の撃の輪が <see cref="OnHit"/> を呼ぶ)。
    /// 自分で敵の防御を 0 にした後、 防御が 0 の間は全部の撃の最終与ダメージ +20/20% (2026-11-22・II 旧 35%)。</summary>
    public class ArmorBreak : IPassiveSkillEffect
    {
        public static readonly string[] Ids = { "鎧砕きI", "鎧砕きII" };
        public static readonly int[] Amounts = { 2, 3 }, ShatterPct = { 20, 20 };
        public const int PerTurn = 3;
        /// <summary>[persistent] 持っている段の削り量・割った後の % / 割った印 / [perTurn 相当] 今のターンの番号と削った回数。</summary>
        public const string AmountKey = "axe.breakAmt", PctKey = "axe.breakPct", ShatteredKey = "axe.shattered", TurnKey = "axe.breakTurn", CountKey = "axe.breakN";
        private readonly int _lv;
        public ArmorBreak(int lv) { _lv = lv; }
        public string SkillId => Ids[_lv];
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.accumulatedValues[AmountKey] = Amounts[_lv]; ctx.accumulatedValues[PctKey] = ShatterPct[_lv]; return; }
            if (Shattered(ctx)) Fam.Final(ctx, ShatterPct[_lv]);
        }

        /// <summary>割った後の上乗せが今効く (自分で 0 にした・今の防御が 0)。</summary>
        public static bool Shattered(CombatContext ctx) => ctx != null && ctx.GetAccumulated(ShatteredKey) > 0f && ctx.enemyDefense <= 0;

        /// <summary>撃が当たった直後 (CombatManager.Turn の撃の輪・防御の計算の後)。 〈鎧砕き〉を持たなければ何もしない。</summary>
        public static void OnHit(CombatContext ctx)
        {
            int amt = ctx != null ? (int)ctx.GetAccumulated(AmountKey) : 0;
            if (amt <= 0 || ctx.enemyDefense <= 0) return;
            if ((int)ctx.GetAccumulated(TurnKey) != ctx.currentTurn) { ctx.accumulatedValues[TurnKey] = ctx.currentTurn; ctx.accumulatedValues[CountKey] = 0; }
            int n = (int)ctx.GetAccumulated(CountKey);
            if (n >= PerTurn) return;
            ctx.accumulatedValues[CountKey] = n + 1;
            int before = ctx.enemyDefense;
            ctx.enemyDefense = System.Math.Max(0, before - amt);
            if (ctx.enemyDefense == 0) ctx.accumulatedValues[ShatteredKey] = 1f;
            Rw.Log($"[鎧砕き] 敵の防御 {before} → {ctx.enemyDefense} (このターン {n + 1}/{PerTurn})");
        }
    }

    /// <summary>捨て身: 防御の端子に 1 つも挿さなかったターン (全力攻撃)、 全部の撃の最終与ダメージ +35%。</summary>
    public class Recklessness : IPassiveSkillEffect
    {
        public const int Pct = 35;
        public string SkillId => "捨て身";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) { if (Fam.NoBlock(ctx)) Fam.Final(ctx, Pct); }
    }

    /// <summary>血令: 敵の攻撃で失った HP の 60% を「血」として溜める (ボスのダウンのたび +8)。 次に攻撃したターン、 血を全部使って全部の撃の攻撃 +血。
    /// 攻撃が通ったら、 使った血の 1/3 だけ HP を回復する。 4 連戦の段替えでも血は持ち越す。</summary>
    public class BloodEdict : IPassiveSkillEffect
    {
        /// <summary>[persistent・戦闘開始で 0] 溜まっている血 / [perTurn] このターンの攻撃に使った血。</summary>
        public const string BloodKey = "bloodEdictBlood", SpentKey = "bloodEdictSpent";
        public const int StorePct = 60, DownBlood = 8, HealDiv = 3;
        public string SkillId => "血令";
        public PassiveSkillTrigger[] Triggers => new[]
        { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnTurnEnd, PassiveSkillTrigger.OnBossDown };

        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            switch (t)
            {
                case PassiveSkillTrigger.OnBattleStart:
                    ctx.accumulatedValues[BloodKey] = 0f; ctx.accumulatedValues[SpentKey] = 0f;
                    break;
                case PassiveSkillTrigger.OnBossDown:
                    ctx.accumulatedValues[BloodKey] = ctx.GetAccumulated(BloodKey) + DownBlood;
                    break;
                case PassiveSkillTrigger.OnPostRoll:
                {
                    ctx.accumulatedValues[SpentKey] = 0f;
                    int blood = (int)ctx.GetAccumulated(BloodKey);
                    if (blood > 0) ctx.AddPlayerAttackOrDiceBonus(blood);   // 攻撃しないターンは攻撃値が使われないだけ (血は残る)
                    break;
                }
                case PassiveSkillTrigger.OnWired:
                    if (ctx.GetAccumulated(MutualKeys.AttackDiceCount) <= 0f) return;
                    ctx.accumulatedValues[SpentKey] = ctx.GetAccumulated(BloodKey);
                    ctx.accumulatedValues[BloodKey] = 0f;
                    break;
                case PassiveSkillTrigger.OnTurnEnd:
                {
                    int spent = (int)ctx.GetAccumulated(SpentKey);
                    if (spent >= HealDiv && ctx.lastPlayerDamage > 0 && ctx.playerCurrentHP > 0) Rw.Heal(ctx, spent / HealDiv);
                    ctx.accumulatedValues[SpentKey] = 0f;
                    int lost = (int)ctx.GetAccumulated(MutualKeys.HpLossFromEnemy);
                    if (lost > 0) ctx.accumulatedValues[BloodKey] = ctx.GetAccumulated(BloodKey) + lost * StorePct / 100;
                    break;
                }
            }
        }
    }

    // ============================================================
    //  剣 — 攻防一体・盤面熟練・対単体
    // ============================================================

    /// <summary>間合 I/II/III: 攻撃の端子に挿した出目 +25/40/60%・敵の固定削りを 3/5/8 打ち消す。 III は攻撃と防御の両方に挿したターン、 次のターンの手札 +1。</summary>
    public class SwordReach : IPassiveSkillEffect
    {
        public static readonly string[] Ids = { "間合I", "間合II", "間合III" };
        public static readonly float[] DicePct = { 0.25f, 0.40f, 0.60f };
        public static readonly int[] PenNegate = { 3, 5, 8 };
        public const int SplitDraw = 1;
        private readonly int _lv;
        public SwordReach(int lv) { _lv = lv; }
        public string SkillId => Ids[_lv];
        public PassiveSkillTrigger[] Triggers => _lv == 2
            ? new[] { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnWired }
            : new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired) { if (Fam.Split(ctx)) Rw.DrawNext(SplitDraw); return; }
            ctx.AddAttackDicePct(DicePct[_lv]);
            ctx.enemyArmorPenNegate += PenNegate[_lv];   // [perTurn]
        }
    }

    /// <summary>一対一 I/II: 単体戦 (ボス含む) のとき、 全部の撃の最終与ダメージ +10/18% (2026-11-22・旧 15/25)・自分の防御 +3/+5。 2 体戦では乗らない (戦闘名は 1 マス手前で開示)。</summary>
    public class Duelist : IPassiveSkillEffect
    {
        public static readonly string[] Ids = { "一対一I", "一対一II" };
        public static readonly int[] FinalPct = { 10, 18 }, Def = { 3, 5 };
        private readonly int _lv;
        public Duelist(int lv) { _lv = lv; }
        public string SkillId => Ids[_lv];
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.isPairEncounter) return;
            if (t == PassiveSkillTrigger.OnPostRoll) ctx.playerDefense += Def[_lv];   // [perTurn] 予告より前に乗せる
            else Fam.Final(ctx, FinalPct[_lv]);
        }
    }

    /// <summary>切り返し: 攻撃と防御の両方に挿したターン、 ブロック合計 × 0.6 (切り上げ・<see cref="CombatRules.GuardCutBonus"/>) を全部の撃の攻撃に加え、
    /// そのターンの最初の 1 撃は敵の防御を 10 無視する (ctx.hitArmorPen)。</summary>
    public class GuardCut : IPassiveSkillEffect
    {
        public const int RatePct = 60, FirstHitPen = 10;
        public string SkillId => "切り返し";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Fam.Split(ctx)) return;
            if (t == PassiveSkillTrigger.OnPreDealDamage) { if (ctx.IsRealHit && ctx.hitIndex == 0) ctx.hitArmorPen += FirstHitPen; return; }
            if (ctx.blockSumThisTurn <= 0) return;
            int add = CombatRules.GuardCutBonus(ctx.blockSumThisTurn);
            ctx.AddPlayerAttackOrDiceBonus(add);
            ctx.diceDerivedAttack += add;   // [計装] ブロックに挿した出目から来た攻撃
            Rw.Log($"[切り返し] ブロック{ctx.blockSumThisTurn} × {RatePct}% → 攻撃 +{add}・最初の 1 撃は防御 {FirstHitPen} 無視");
        }
    }

    /// <summary>不滅の刃: 攻撃と防御の両方に挿したターンが続くたび、 全部の撃の最終与ダメージ +12% × 連続ターン (最大 +72%・<see cref="CombatRules.UndyingPct"/>)。 途切れたら 0。</summary>
    public class UndyingBlade : IPassiveSkillEffect
    {
        public const string StreakKey = "undyingBladeStreak";
        public const int PerTurn = 12, MaxPct = 72;
        public string SkillId => "不滅の刃";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired)
            {
                ctx.accumulatedValues[StreakKey] = Fam.Split(ctx) ? ctx.GetAccumulated(StreakKey) + 1f : 0f;
                return;
            }
            Fam.Final(ctx, CombatRules.UndyingPct((int)ctx.GetAccumulated(StreakKey)));
        }
    }

    // ============================================================
    //  短剣 — 超手数・先制・装甲素通し・処刑
    // ============================================================

    /// <summary>疾手 I/II/III: 戦闘の最初の 5 ターン、 全部の撃の攻撃 +4/+6/+9・戦闘の 1 ターン目の手札 +1/+1/+2。</summary>
    public class SwiftHand : IPassiveSkillEffect
    {
        public static readonly string[] Ids = { "疾手I", "疾手II", "疾手III" };
        public static readonly int[] Atk = { 4, 6, 9 }, OpeningDraw = { 1, 1, 2 };
        public const int Window = 5;
        private readonly int _lv;
        public SwiftHand(int lv) { _lv = lv; }
        public string SkillId => Ids[_lv];
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Rw.DrawNext(OpeningDraw[_lv]); return; }   // 山札は OnBattleStart より前に作られる → 1 ターン目に引く
            if (ctx.currentTurn <= Window) ctx.AddPlayerAttackOrDiceBonus(Atk[_lv]);
        }
    }

    /// <summary>連刃 I/II: 同じターンに攻撃の端子を 2 枚以上発動したとき、 最後に追撃を 1 撃 (1 ターン 1 回)。
    /// 追撃の攻撃 = 武器の攻撃力 × 1.2/1.6 (切り上げ・<see cref="CombatRules.ChainHitBase"/>)・敵の防御を無視・会心と撃ごとの効果は通常の撃と同じ。 追撃するターン、 ブロック +4 (2026-11-22)。
    /// 撃を足すのは CombatManager の撃の輪 (<see cref="HitBase"/> を読む)。</summary>
    public class ChainBlade : IPassiveSkillEffect
    {
        public static readonly string[] Ids = { "連刃I", "連刃II" };
        public static readonly int[] RatePct = { 120, 160 };
        public const int NeedTerminals = 2, Block = 4;
        /// <summary>[perTurn・OnWired で毎ターン書く] このターンの追撃の率 (%・0 = 追撃なし)。</summary>
        public const string RateKey = "dagger.chainRate";
        private readonly int _lv;
        public ChainBlade(int lv) { _lv = lv; }
        public string SkillId => Ids[_lv];
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            bool on = Fam.AttackTerminals() >= NeedTerminals;
            ctx.accumulatedValues[RateKey] = on ? RatePct[_lv] : 0;
            if (on) ctx.itemBlockBonus += Block;      // 敵の攻撃の前に足す (配線の後の品のブロック)
        }

        /// <summary>このターンの追撃の攻撃 (0 = 追撃なし)。</summary>
        public static int HitBase(CombatContext ctx, int weaponAtk)
        {
            int rate = ctx != null ? (int)ctx.GetAccumulated(RateKey) : 0;
            return rate > 0 ? CombatRules.ChainHitBase(weaponAtk, rate) : 0;
        }
    }

    /// <summary>急所: 会心の撃と 2 撃目以降の撃は、 敵の防御を完全に無視する (会心は CombatManager.Damage が <see cref="IgnoresDefense"/> を読む)。</summary>
    public class VitalStrike : IPassiveSkillEffect
    {
        public const string Key = "vitalStrikeIgnoresDefense";
        public string SkillId => "急所";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) ctx.accumulatedValues[Key] = 1f;
            else if (ctx.IsRealHit && ctx.hitIndex >= 1) ctx.hitIgnoreDefense = true;
        }

        public static bool IgnoresDefense(CombatContext ctx) => ctx != null && ctx.GetAccumulated(Key) > 0f;
    }

    /// <summary>処刑: 敵の残り HP が 40% 以下のとき、 全部の撃の最終与ダメージ +50%。 撃が当たった後に敵の残り HP が 15% 以下 (ボス 10% 以下) なら即座に倒す
    /// (CombatManager の撃の輪が <see cref="Kills"/> を読む・CanExecuteEnemy で止まる)。 HP は配線の時点の値で見る (撃の輪の中では HP がまだ減っていない)。</summary>
    public class Execution : IPassiveSkillEffect
    {
        public const string Key = "dagger.execute";
        public string SkillId => "処刑";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.accumulatedValues[Key] = 1f; return; }
            if (CombatRules.ExecutionBoostOn(ctx.enemyCurrentHP, ctx.enemyMaxHP)) Fam.Final(ctx, CombatRules.ExecutionFinalPct);
        }

        public static bool Kills(CombatContext ctx, int hp, int maxHp, bool boss)
            => ctx != null && ctx.GetAccumulated(Key) > 0f && hp > 0 && CombatRules.ExecutionKills(hp, maxHp, boss) && ctx.CanExecuteEnemy();
    }

    // ============================================================
    //  盾 — 鉄壁要塞・ジャスト見切り・反撃解放
    // ============================================================

    /// <summary>城壁 I/II/III: 敵の攻撃を完全に受け止めたターン、 余ったブロック × 0.6/0.9/1.2 (切り捨て) をシールドへ。
    /// 【ジャスト防御】が成立したターンは、 余りに関係なくシールド +8/12/16 (<see cref="CombatRules.BulwarkGain"/>)。 ターンの終わりに入れる。</summary>
    public class Bulwark : IPassiveSkillEffect
    {
        public static readonly string[] Ids = { "城壁I", "城壁II", "城壁III" };
        public static readonly int[] RatePct = { 60, 90, 120 }, JustShield = { 8, 12, 16 };
        private readonly int _lv;
        public Bulwark(int lv) { _lv = lv; }
        public string SkillId => Ids[_lv];
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int gain = CombatRules.BulwarkGain(ctx.blockSumThisTurn, (int)ctx.GetAccumulated(MutualKeys.EnemyAtk), RatePct[_lv], Shd.JustGuardNow(ctx), JustShield[_lv]);
            Fam.GainShield(ctx, gain, SkillId);
        }
    }

    /// <summary>シールドバッシュ I/II: 防御の端子に 1 つも挿さずに攻撃したターン、 今のシールドの半分 (切り上げ・II は全部) を使い、
    /// 使った量 × 0.9/1.5 (切り上げ) を全部の撃の攻撃に加える。 そのターンの撃はボスの目盛りへの削り +30%。</summary>
    public class ShieldBash : IPassiveSkillEffect
    {
        public static readonly string[] Ids = { "シールドバッシュI", "シールドバッシュII" };
        public static readonly int[] RatePct = { 90, 150 };
        public static readonly bool[] SpendAll = { false, true };
        public const int NotchPct = 30;
        public const string TurnKey = "shield.bashTurn";
        private readonly int _lv;
        public ShieldBash(int lv) { _lv = lv; }
        public string SkillId => Ids[_lv];
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnPostDealDamage) { if ((int)ctx.GetAccumulated(TurnKey) == ctx.currentTurn) Fam.Notch(ctx, NotchPct); return; }
            if (!Fam.NoBlock(ctx) || ctx.consShield <= 0) return;
            int spent = Kw.SpendShield(ctx, CombatRules.BashSpend(ctx.consShield, SpendAll[_lv]));
            int add = CombatRules.BashBonus(spent, RatePct[_lv]);
            ctx.AddPlayerAttackOrDiceBonus(add);
            ctx.accumulatedValues[TurnKey] = ctx.currentTurn;
            CombatManager.WeaponBashFired++;
            Rw.Log($"[{SkillId}] シールド {spent} を使い × {RatePct[_lv]}% → 攻撃 +{add}・目盛りへの削り +{NotchPct}%");
        }
    }

    /// <summary>堅守の構え: 防御の端子に 2 個以上挿したターン、 配線の時にシールド +18。 使ったら次の 2 ターンは発動しない (3 ターンに 1 回・大技の周期と同じ)。</summary>
    public class SteadfastStance : IPassiveSkillEffect
    {
        public const int Shield = 18, NeedBlockDice = 2;
        public const int Cooldown = 3;   // 発動ターン + 3 から再び使える = 次の 2 ターンは不可
        /// <summary>次に使えるターン (accumulatedValues の鍵)。 BOT の配線評価も読む。</summary>
        public const string ReadyKey = "steadfastStanceReadyTurn";
        public string SkillId => "堅守の構え";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Fam.BlockDice(ctx) < NeedBlockDice) return;
            if (ctx.currentTurn < (int)ctx.GetAccumulated(ReadyKey)) return;
            Fam.GainShield(ctx, Shield, SkillId);
            ctx.accumulatedValues[ReadyKey] = ctx.currentTurn + Cooldown;
            Rw.Log($"[堅守の構え] シールド +{Shield} (次に使えるのは T{ctx.currentTurn + Cooldown})");
        }
    }

    /// <summary>黎明: 敵の攻撃を HP の減り 0 で受け切ったターン、 軽減前のダメージ 10 につき敵の最大 HP の 5% を削る。
    /// 【ジャスト防御】が成立したターンは 8% にして、 ボスの次の目盛りにひびを入れる。 2 体戦は 2 体目にも同じ量 (CombatManager が <see cref="DealtKey"/> を読む)。
    /// 削りは敵の HP を直接減らす (〈天与の才〉の踏みとどまりは通す)。</summary>
    public class Daybreak : IPassiveSkillEffect
    {
        public const int PerUnit = 10, PctPerUnit = 5, JustPctPerUnit = 8;
        /// <summary>このターンに 1 体目へ入れた削り。 2 体戦で 2 体目へ同じ量を写すために CombatManager が読む (読んだら消す)。</summary>
        public const string DealtKey = "daybreakDealt";
        public string SkillId => "黎明";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int pre = (int)ctx.GetAccumulated(MutualKeys.EnemyAtkPreDefense);
            int units = pre / PerUnit;
            if (units <= 0) return;
            if (ctx.GetAccumulated(MutualKeys.HpLossFromEnemy) > 0f) return;
            if (ctx.enemyCurrentHP <= 0 || ctx.enemyMaxHP <= 0) return;
            bool just = Shd.JustGuardNow(ctx);
            int dmg = CombatRules.DaybreakCut(ctx.enemyMaxHP, units, just);
            int before = ctx.enemyCurrentHP;
            int after = DivineTalent.ClampToThreshold(ctx, System.Math.Max(0, before - dmg), ctx.enemyMaxHP);
            ctx.enemyCurrentHP = after;
            if (before > after) ctx.accumulatedValues[DealtKey] = before - after;
            if (just) CombatManager.Instance?.CrackBossNotch(SkillId);
            Rw.Log($"[黎明] 無傷で受け切った{(just ? " (ジャスト防御)" : "")}: 軽減前 {pre} → {units}×{(just ? JustPctPerUnit : PctPerUnit)}% = {dmg} (敵HP {before} → {after})");
        }
    }

    // ============================================================
    //  竜閃 — 一刀両断・極大単撃
    // ============================================================

    /// <summary>画竜点睛: 攻撃の端子を 1 枚だけ発動し、 そこにこのターン振った目の最大 (タイ含む) を挿したとき、 その 1 撃は確定会心・最終与ダメージ ×1.6 (+60%)。
    /// その目が装備ダイスの最大の目なら ×2.5 (+150%) で、 ボスの次の目盛りをその場で割る (満額のダウン・1 ターン 1 回)。 攻撃の端子を 2 枚以上発動したターンは不発 (〈無我無心〉)。
    /// 式は <see cref="CombatRules.GaryoPct"/> (2026-11-22・旧: 装備ダイスの最大の目だけ)。</summary>
    public class GaryoTensei : IPassiveSkillEffect
    {
        public const int FinalPct = 150, RollMaxPct = 60;
        public const string TurnKey = "ryusen.garyoTurn", BrokeKey = "ryusen.garyoBroke";
        public string SkillId => "画竜点睛";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnPreDealDamage)
            {
                if (!ctx.IsRealHit || ctx.hitCount != 1 || ctx.hitIndex != 0) return;
                int pct = CombatRules.GaryoPct(ctx.hitFaceMax, RolledMax(ctx), MaxFace(ctx));
                if (pct <= 0) return;
                ctx.forceCritThisHit = true;
                ctx.hitFinalDamagePct += pct;
                if (pct >= FinalPct) ctx.accumulatedValues[TurnKey] = ctx.currentTurn;      // 目盛りを割るのは装備ダイスの最大の目の時だけ
                CombatManager.WeaponGaryoFired++;
                Rw.Log($"[画竜点睛] 目 {ctx.hitFaceMax} (振った最大 {RolledMax(ctx)}・ダイスの最大 {MaxFace(ctx)}) → 確定会心・最終与ダメージ +{pct}%");
                return;
            }
            if ((int)ctx.GetAccumulated(TurnKey) != ctx.currentTurn || !Psn.Hit(ctx) || !Rw.OncePerTurn(ctx, BrokeKey)) return;
            CombatManager.Instance?.BreakBossNotch(SkillId);
        }

        /// <summary>このターン振った目の最大 (振っていなければ 0)。</summary>
        public static int RolledMax(CombatContext ctx)
        {
            int m = 0;
            if (ctx.playerDice != null) foreach (int d in ctx.playerDice) if (d > m) m = d;
            return m;
        }

        /// <summary>装備ダイスの最大の目 (面が無ければ playerDiceMax)。</summary>
        public static int MaxFace(CombatContext ctx)
        {
            var f = ctx.equippedDiceFaces;
            if (f == null || f.Length == 0) return ctx.playerDiceMax > 0 ? ctx.playerDiceMax : 6;
            int m = f[0];
            for (int i = 1; i < f.Length; i++) if (f[i] > m) m = f[i];
            return m;
        }
    }

    /// <summary>無我無心: 攻撃の端子を 1 枚だけ発動したターン、 他の端子に挿した目の合計 × 1.5 (切り上げ) をその 1 撃の攻撃に加え (他の端子の働きはそのまま)、
    /// 防御の端子に挿した出目 1 個につきブロック +3 (2026-11-22・<see cref="CombatRules.MugaBlock"/>)。
    /// 攻撃の端子を 2 枚以上発動したターンは武器の攻撃力が 0 になる (〈画竜点睛〉は 1 枚の時だけ働く)。</summary>
    public class MugaMushin : IPassiveSkillEffect
    {
        public const int RatePct = 150, BlockPerDie = 3;
        public string SkillId => "無我無心";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var cm = CombatManager.Instance;
            var o = cm?.LastTerminalOutcome;
            int n = o?.attackHits.Count ?? 0;
            if (n <= 0) return;
            if (n >= 2) { ctx.AddPlayerAttackOrDiceBonus(-cm.PlayerAttackPower); Rw.Log($"[無我無心] 攻撃の端子 {n} 枚 → 武器の攻撃力 0"); return; }
            int block = CombatRules.MugaBlock(o.blockDice);
            if (block > 0) { ctx.itemBlockBonus += block; Rw.Log($"[無我無心] 攻撃の端子 1 枚: 防御の出目 {o.blockDice} 個 → ブロック +{block}"); }
            int other = 0;
            if (ctx.playerDice != null && o.kindOfDie != null)
                for (int i = 0; i < ctx.playerDice.Length && i < o.kindOfDie.Length; i++)
                    if (o.kindOfDie[i] != DiceTerminal.Attack && o.kindOfDie[i] != DiceTerminal.None) other += ctx.playerDice[i];
            int add = CombatRules.MugaBonus(other);
            if (add <= 0) return;
            ctx.AddPlayerAttackOrDiceBonus(add);
            Rw.Log($"[無我無心] 攻撃の端子 1 枚: 他の端子の目 {other} × {RatePct}% → 攻撃 +{add}");
        }
    }
}

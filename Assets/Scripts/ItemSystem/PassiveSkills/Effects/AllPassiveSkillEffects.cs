namespace ItemSystem.PassiveSkills.Effects
{
    // 追撃・剛力・堅忍・慧眼・不屈・治癒阻害の I〜IV、 天極・出血 (Sting)・腐蝕の一撃・遅効の呪・雷雲の効果クラス (英字の id) は
    //   2026-11-12 削除。 スキル id が品の名前になった (2026-09-22) あと、 どの品もその id を持っていなかった。

    // ============================================================
    //  汎用パッシブ — 反撃（Counter）
    // ============================================================

    // 反撃 I-IV: 被ダメ発生時に発火 (収支トリガーではなく被弾で発火)。
    // OnPreReceiveDamage は ProcessDamage 内で発火し、fixedDamageToEnemy に積むと
    // counterFixed として ExecuteTurnMutual が敵HPへ反射する。ctx.finalDamage > 0 の被ダメ発生時のみ発火
    // (完全防御・シールド吸収で被ダメ0の時は反撃なし)。
    internal static class CounterHelper
    {
        public static void Apply(PassiveSkillTrigger t, CombatContext ctx, int dmg)
        {
            if (t == PassiveSkillTrigger.OnPreReceiveDamage && ctx.finalDamage > 0)
                ctx.fixedDamageToEnemy += dmg;
        }
    }





    // ============================================================
    //  素のステータス品 (2026-09-15)
    //
    //  **ラダーではない。** Might/Insight のような I〜IV の段階品ではなく、
    //  1 品 1 効果の独立した品として置く。 汎用ラダーはアイテムとしては
    //  1 ラダー 1 品まで解体済み (GAME.md §9-2b) なので、 段を足すのは再生産になる。
    //
    //  **なぜ要ったか。** BRONZE 21 品のうち発動条件なしで常時乗るのは 3 品、
    //  SILVER 18 品では 1 品しかなかった。 残りは臨界メーター・充電・毒・出血の
    //  ギミック品で、 <b>そのビルドを既に組んでいないと数値がゼロ</b>。
    //  1〜3 層は BRONZE/SILVER が主体なので、 まだ何者でもない状態の棚に
    //  そのランで機能する品がほとんど並ばなかった。
    //
    //  **値は実測のレバレッジから決めている** (10,000 ラン・107 万攻撃):
    //      atkBase 36.10 → 最終与ダメ 150.12  ＝ <b>×4.16</b> (最大観測 ×51.67)
    //      敵の攻撃 89.9 回/ラン  平均ブロック 10.37  平均 lossBase 15.67
    //  この倍率のせいで「小さな素のステータス」が小さくならない ──
    //      攻撃 +1      → 最終与ダメ +2.8%
    //      被ダメ −1    → 被弾すべてに乗って −6.4%   (メタ防御 2 段ぶん)
    //      与ダメ +1%   → +0.6%                     ← 穏やか
    //  **攻撃加算と固定被ダメ軽減は「基礎的な小品」には置けない。**
    //  与ダメージ% と少量のシールドだけがこの帯で扱える量だった。
    // ============================================================





    // ============================================================
    //  汎用パッシブ — 活力（Vitality）
    // ============================================================





    // ============================================================
    //  ユニークパッシブ — 盾系
    // ============================================================

    // 〈パリィ〉〈衛士の慣い〉は 2026-10-28 削除。 盾家系の〈堅守の構え〉〈黎明〉へ置き換えた
    //   (WeaponFamilyEffects.cs)。 パリィは最初の被弾で自動発動して T1・T4・T7… と続き、
    //   5 層以降の大技 (T3・T6・T9) と一度も重ならなかった (GAME.md §24)。

    // ============================================================
    //  ユニークパッシブ — 斧系
    // ============================================================

    // 〈復讐〉は 2026-10-28 削除 (相互攻撃モデルではほぼ毎ターン被弾するので〈猛り〉と同じ意味だった)。

    // ============================================================
    //  ユニークパッシブ — 短剣系
    // ============================================================

    // 〈処刑〉は items.json の stats だけになった (2026-10-28 にダイス潰しの固有部分を削除)。
    // 〈蝕夜〉(過剰ダメージ ×4 を次の戦闘へ持ち越す) は 2026-10-28 削除 (GAME.md §24)。

    // (Ignite / CurseBind / Abyss は 2026-07-18 削除: dead_staff / curse_t1〜t4 全廃に伴う死コード掃除)

    // ============================================================
    //  ダイス固有パッシブ
    // ============================================================








    // ============================================================
    //  汎用パッシブ — 吸血（Lifesteal）: ロール勝利時、最終与ダメの2/4/6/8%回復
    //  ctx.lifestealPct に加算し、CombatManager 勝利分岐が totalDmg×pct を HealPlayer で回復
    //  （負傷/回復封印/天衣無縫を尊重）。毎ターン0リセットのため OnTurnStart で再適用。
    // ============================================================

    // ShieldBashI〜IV (IV = 旧〈内から落ちた城盾〉) は 2026-10-28 削除。 I〜III はどの品にも付いていなかった。
    //   「シールドを攻撃に変える」は盾家系の〈シールドバッシュ〉(WeaponFamilyEffects.cs) へ一本化した。






    // ============================
    //  ダイス固有パッシブ（新規）
    // ============================


    // ============================
    //  武器Tier段階補正（ダイス合計フラット加算。少ダイス低Tierを底上げし進行を線形化）
    // ============================




    // 〈溶金の領主剣〉(旧 GoldKingBlade・使ったゴールド 1 につき与ダメ +0.2%・2026-08-10 の経済リスケールで係数 0.01 → 0.002) は 2026-11-11 に items.json の stats へ移した (段 3・per=coinsSpent)。

    // ============================================================
    //  2026-06-03 新規追加アイテム
    // ============================================================



    // ============================================================
    //  2026-06-05 会心バリエーション（会心を「ただ×2」から質の違う一撃へ）
    //  発火: OnCriticalDamage（会心成立後・×criticalMultiplier 適用前）／一部 OnCriticalCheck（会心判定前）
    // ============================================================

    // 武器Tier段階補正 (旧: ダイス合計 / 新: 攻撃力・自動分岐)

    // ============================================================
    //  汎用パッシブ — 利刃（BladeEdge）リワーク 2026-07-15:
    //  攻撃時、敵シールドを 5/10/15/20 削る。Lv4 のみ会心ダメージ+20%も追加。
    //  「敵シールド」= 現状 SaintGeorges の sg_shield のみ (対シュヴァリエ特効)。
    //  将来的にシールド持ちボスが増えたら同キー統合 or enemy_shield 汎用化を検討。
    //  従来の armorPen/winMinDamage は廃止 (基礎防御% の意味論は保持されるが利刃系統は移行)。
    // ============================================================
    internal static class BladeEdgeHelper
    {
        public const string EnemyShieldKey = "sg_shield"; // 現状は SaintGeorges 唯一
    }

    // ============================================================
    //  処刑・対タンク・シナジー触媒（2026-05-29 追加）
    // ============================================================

    /// <summary>賞金首狩り (リワーク 2026-05-30) — ターン終了時、敵HPが閾値%以下なら即処刑。
    /// 通常敵: 閾値 10/20/30/40% / ボス: 閾値 5/10/15/20% (耐性)。
    /// 報酬: 処刑時 最大HP×(lv×3%) 回復 + GOLD 1/2/3/4 (全Tier獲得)。</summary>
    internal static class BountyHunterHelper
    {
        public static bool IsBoss(CombatContext ctx)
        {
            var enemy = CombatSystem.CombatManager.Instance?.CurrentEnemy;
            return enemy != null && GameLoop.BossIds.IsBoss(enemy.id);
        }
    }



    /// <summary>重畳 (2026-05-31 v5 %ベース) — ロール勝利毎に与ダメ倍率を加算。
    /// Lv1-4: 勝利毎 +2/4/5/10%、 上限 +20/40/60/80% (outgoing 加算)。
    /// stack 値は % 値 (整数) で保存し、 OnDeal で outgoing += stack/100 を反映。</summary>
    internal static class ConquerorHelper
    {
        public const string Key = "conqueror_stack";

        public static void OnDeal(CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            int pct = (int)ctx.GetAccumulated(Key);
            if (pct <= 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += pct / 100f;
        }
    }





    // ============================================================
    //  竜閃（ユニーク武器）— 安定性の対極の斬鉄剣
    // ============================================================

    /// <summary>無我無心 — カスタムダイス以外の補正を一切受けない（戦闘中持続）。</summary>
    public class MugaMushin : IPassiveSkillEffect
    {
        public string SkillId => "無我無心";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.rollPurity = true;
        }
    }

    /// <summary>画竜点睛 — 旧: 出目最大なら「ロール即勝利＋(出目+10)＋会心確定」。
    /// 新: ロール勝負なし → 出目最大なら「会心確定＋攻撃+(出目+10)」 (即勝利部分は削除、火力バーストは維持)。
    /// garyoProc/garyoDieValue は旧法の ApplyWinDamageModifiers が上書きで発火するため、
    /// 新法では OnPreDealDamage で mutualAttackBonus+(出目+10) + 会心確定にする。</summary>
    public class GaryoTensei : IPassiveSkillEffect
    {
        public string SkillId => "画竜点睛";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length == 0) return;

            // その時点で取りうる最大値: カスタムダイス装備中はその最大面、無ければ playerDiceMax
            int maxVal;
            if (ctx.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0)
            {
                maxVal = ctx.equippedDiceFaces[0];
                for (int i = 1; i < ctx.equippedDiceFaces.Length; i++)
                    if (ctx.equippedDiceFaces[i] > maxVal) maxVal = ctx.equippedDiceFaces[i];
            }
            else
            {
                maxVal = ctx.playerDiceMax > 0 ? ctx.playerDiceMax : 6;
            }

            // ダイス数1前提だが、いずれかが最大値なら発動
            int hit = 0;
            for (int i = 0; i < ctx.playerDice.Length; i++)
                if (ctx.playerDice[i] >= maxVal) { hit = ctx.playerDice[i]; break; }

            if (hit > 0)
            {
                // 攻撃+(出目+10) + 会心確定 (即勝利は消失。バースト火力のみ保持)
                //   **ヘルパーを通す (2026-09-14 修正)。** 直接 mutualAttackBonus を叩いていたため
                //   SkillId 別の計装 (CombatContext.AttackBonusBySkill) から漏れており、
                //   atkBase の「パッシブ加算」36.9 の内訳を読み違えた。
                //   加算先は同じなので挙動は変わらない。
                ctx.AddPlayerAttackOrDiceBonus(hit + 10);
                ctx.forceCritical = true;
                UnityEngine.Debug.Log($"[画竜点睛] 出目{hit}=最大 → 攻撃+{hit + 10} + 会心確定");
            }
        }
    }
}

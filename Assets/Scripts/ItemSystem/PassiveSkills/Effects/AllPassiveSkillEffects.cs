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

    // ============================================================
    //  [剣の舞] セット（2026-06-04）— Passive カテゴリのシナジー武器群。
    //  4枚インベントリ集約で〈無銘の剣舞譜〉(BladeDance) に変化（GameLoop.SwordDanceSet）。
    //  ダイス合計加算は OnPostRoll（RecomputeDiceTotals 後＝確実に効く）で行う。
    //  run 参照は GameLoop.GameManager.Instance.Run（StepHeal/Eternal と同じ手法）。
    // ============================================================

    /// <summary>サーベル・ワルツ (BRONZE): ダイス合計+1。
    /// 他の[剣の舞]がインベントリにも昇華にも存在しないとき、戦闘開始時にHPを半減（孤剣のリスク）。
    /// ショップでの[剣の舞]出現率上昇は ShopManager 側のフックで処理。</summary>
    public class SaberWaltz : IPassiveSkillEffect
    {
        public string SkillId => "サーベル・ワルツ";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnPostRoll,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // [撤去 2026-09-14] 孤剣ペナルティ (戦闘開始HP -10%)。
            //   4 枚とも LEGENDARY へ揃えた結果、 1 枚目が「10G のデメリット付き」になり
            //   BOT の購入率が 16.6% ── 棚に 0.61 枚/ラン 出ているのに取得は 0.10 枚/ラン で、
            //   <b>揃える前に見送られて集約が 0%</b> になっていた。
            //   「揃えば強い、 単体でも損しない」へ寄せる。 集めに行く動機はここで作る。
            //   (2026-09-05 に HP半減 → -10% へ緩和した経緯があり、 今回で全廃。)
            if (trigger == PassiveSkillTrigger.OnBattleStart) return;
            // OnPostRoll: 与ダメージ +10% (2026-11-10・旧 攻撃に挿した出目 +10%・その前は 攻撃+1)。 [剣の舞] 2 枚で 1.5 倍・3 枚で 2 倍 (2026-11-20)
            ctx.AddOutgoingPct(0.1f * GameLoop.SwordDanceSet.SoloScale(GameLoop.GameManager.Instance?.Run));
        }
    }

    /// <summary>エスパーダ・パソドブレ (SILVER): 自分と敵のダイス合計に+5。
    /// 与えるダメージ+20%（outgoing）／受けるダメージ+20%（OnPreReceiveで×1.2）。
    /// 自他+5でダイス差は不変だが、合計値を読む効果（血令/断罪の天秤/万華 等）と高め合う両刃。</summary>
    public class EspadaPasodoble : IPassiveSkillEffect
    {
        public string SkillId => "エスパーダ・パソドブレ";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnPostRoll,
            PassiveSkillTrigger.OnPreReceiveDamage,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPostRoll)
            {
                // 旧: 自他ダイス合計+5 (差分不変で「合計」参照系と相乗) / 新: 自攻撃+5 のみ
                // (敵ダイス合計+5 は新モデルではロール勝負がないため機能喪失 = 廃止)
                // 与ダメージ +40% (2026-11-10: 旧 出目 +50% と与ダメージ +20% を 1 本にまとめた・毎ターンリセット→再適用)。
                //   [剣の舞] 2 枚で 1.5 倍・3 枚で 2 倍 (2026-11-20・受けるダメージ +20% は伸ばさない)
                ctx.AddOutgoingPct(0.4f * GameLoop.SwordDanceSet.SoloScale(GameLoop.GameManager.Instance?.Run));
                return;
            }
            // OnPreReceiveDamage: 被ダメ+20%（毎被弾イベントで都度乗る＝累積しない）
            if (ctx.finalDamage > 0)
                ctx.finalDamage = UnityEngine.Mathf.CeilToInt(ctx.finalDamage * 1.2f);
        }
    }

    /// <summary>フルーレ・バレエ (BRONZE): ダイス合計+3。
    /// 「戦闘に敗北したとき、このアイテムを廃棄し最大HPを1にして生還」は救済チェーン
    /// (GameLoop.LastStand.TryConsumeRevival) 側で処理する。ここでは火力部分のみ。</summary>
    public class FleuretBallet : IPassiveSkillEffect
    {
        public string SkillId => "フルーレ・バレエ";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 与ダメージ +25% (2026-11-10・旧 攻撃に挿した出目 +30%・その前は 攻撃+3)。 [剣の舞] 2 枚で 1.5 倍・3 枚で 2 倍 (2026-11-20)
            ctx.AddOutgoingPct(0.25f * GameLoop.SwordDanceSet.SoloScale(GameLoop.GameManager.Instance?.Run));
        }
    }

    /// <summary>ファコン・タンゴ (LEGENDARY): 2026-06-20 効果変更。
    /// 攻撃+2。 <b>2026-09-14: 孤剣ペナルティ (戦闘開始時 最大HP-1) を撤去。</b></summary>
    public class FalconTango : IPassiveSkillEffect
    {
        public string SkillId => "ファコン・タンゴ";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        /// <summary>攻撃加算量。 <b>[撤去 2026-09-14] 孤剣ペナルティ (戦闘開始時 最大HP-1)。</b>
        ///
        /// <para>この札は<b>効果がペナルティだけ</b>だったので、 単に外すと効果ゼロの札になる。
        /// 10G の LEGENDARY が無効果では買われず、 ペナルティを外した意味が消えるため、
        /// セット共通の書式 (ワルツ +1 / バレエ +3 / パソドブレ +5) に合わせて最小限の上振れを持たせる。
        /// <b>この値だけが今回の追加分</b>なので、 不要ならここを 0 にすれば元の「無効果」に戻る。</para></summary>
        public const float OutgoingPct = 0.2f;   // 与ダメージ +20% (2026-11-10・旧 攻撃に挿した出目 +20%・その前は 攻撃+2)

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger != PassiveSkillTrigger.OnPostRoll) return;
            ctx.AddOutgoingPct(OutgoingPct * GameLoop.SwordDanceSet.SoloScale(GameLoop.GameManager.Instance?.Run));   // [剣の舞] 2 枚で 1.5 倍・3 枚で 2 倍 (2026-11-20)
        }
    }

    // ============================================================
    //  鈍器系 (Bludgeon・2026-07-15 新規・4-2-2-1 テンプレで 9 種)
    //  差別化: 会心を犠牲/放棄することで非会心火力を伸ばすトレードオフ型。
    //  ctx.nonCritOutgoingMultiplier に加算し、ProcessDamage 内で isCritical=false 時のみ適用。
    //  会心特化ビルドの下位互換になっていた通常火力ビルドを独立軸化する。
    // ============================================================

    // (炎上系 8 種 Kindling/FlamingWeapon/InfernoOrder/Stoke/Immortalflame/InfernoManifest/FlameEdge/EverBurning は
    //  2026-07-18 削除: Burn 全廃 → 臨界 (Rinkai) 軸に置換済み)

    // ============================================================
    //  臨界系 (Rinkai) 2026-07-16 追加: メーター蓄積型 (Burn 削除後の新軸)
    //  attackSum を毎T末に meter へ加算、閾値 (基準50) 到達で 次T の攻撃に +BurstDamage (基準50)。
    //  ヘルパー: ctx.rinkaiThreshold/rinkaiBurstDamage/rinkaiAfterglow/rinkaiRadiationBonus 経由。
    //  「基礎値」を確立する起点パッシブ = 発火 (Ignition) — 開始時 threshold=50, burstDamage=50 に initialise。
    //  他パッシブは基礎値を修飾 (LowerThreshold で 35 に等)。
    //  2026-10-31 アイテム再編で臨界の品は ReworkItemEffects.cs (出来事は Keywords.Kw) へ移った。 ここに残るのは
    //  基礎値 (RinkaiInit) と、 効果を据え置いた〈八日目の灰燼〉(UnyieldingHeat) だけ。
    // ============================================================

    /// <summary>臨界の基礎値を初期化 (どの Rinkai パッシブでも1度呼ばれれば有効化される)。</summary>
    internal static class RinkaiInit
    {
        public const int DefaultThreshold = 50;
        /// <summary>爆発 1 回の基礎。 2026-11-07: 50 → 30 (投資しないと威力が足りない形・炉温の品で伸ばす)。</summary>
        public const int DefaultBurstDamage = 30;
        /// <summary>閾値は臨界の軸の深さで決まるので Kw.EnableHeat に寄せる (2026-11-06・AxisRules)。</summary>
        public static void EnsureEnabled(CombatContext ctx) => Keywords.Kw.EnableHeat(ctx);
    }









    /// <summary>不朽の熱 (UnyieldingHeat) — 臨界メーター≥40 の間に致命ダメを受けた時、
    /// HP=1 で踏みとどまり meter を全消費 (Burn Immortalflame の遺伝子継承)。 1戦闘1回。</summary>
    public class UnyieldingHeat : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "八日目の灰燼";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Once(ctx.rinkaiUnyieldingUsed, ctx.rinkaiMeter >= 40);
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart)
            {
                RinkaiInit.EnsureEnabled(ctx);
                ctx.rinkaiUnyieldingUsed = false;
                return;
            }
            if (ctx.rinkaiUnyieldingUsed) return;
            if (ctx.finalDamage <= 0) return;
            if (ctx.rinkaiMeter < 40) return;
            if (ctx.finalDamage < ctx.playerCurrentHP) return; // 致命ダメでなければ発動しない
            int lethalDmg = ctx.finalDamage;
            ctx.finalDamage = System.Math.Max(0, ctx.playerCurrentHP - 1);
            int consumed = ctx.rinkaiMeter;
            ctx.rinkaiMeter = 0;
            ctx.rinkaiUnyieldingUsed = true;
            UnityEngine.Debug.Log($"[不朽の熱] 致命ダメ{lethalDmg} → HP=1 踏みとどまり + メーター{consumed}全消費");
        }
    }

    /// <summary>無銘の剣舞譜 (特殊・4枚集約の変化先): 戦闘突入ごとに[剣先]スタック+1（最大99・ラン中持続）。
    /// ダイス合計に剣先スタック分を加算。与ダメージ時に剣先スタック分HP回復、被ダメージ時に剣先スタック分の
    /// 軽減不可ダメージを相手へ。スタックはランを跨がず IRunResettable でリセット。</summary>
    public class BladeDance : IPassiveSkillEffect, IRelicDisplay, IRunResettable, IRunStatePersistent
    {
        public string SkillId => "無銘の剣舞譜";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(kensaki);
        private int kensaki = 0; // 剣先スタック（ラン中持続）

        public void ResetRunState() { kensaki = 0; }

        // 中断セーブ (issue #84): 剣先スタックはラン中ずっと持つので、 中断して再開しても続きから数える
        public string CaptureRunState() => kensaki.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public bool RestoreRunState(string state)
        {
            if (!int.TryParse(state, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v)) return false;
            kensaki = System.Math.Max(0, System.Math.Min(99, v));
            return true;
        }

        /// <summary>4枚集約による BD 取得時の初期スタック付与 (現在層 × 5・2026-11-15 にコードに合わせてコメントを直した)。
        /// 取得が後半層に偏る構造のため、 集約成功への報酬として「現時点の層数 × 5」 をプリロードする。
        /// 既に kensaki > seed の場合は上書きしない (後から再取得→上書きで減少を防ぐ)。</summary>
        public void SeedOnAcquire(int floor)
        {
            int seed = System.Math.Min(99, System.Math.Max(0, floor) * 5);
            if (seed > kensaki) kensaki = seed;
            UnityEngine.Debug.Log($"[無銘の剣舞譜] 取得時シード: floor={floor} → kensaki={kensaki}");
        }

        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnPostRoll,
            PassiveSkillTrigger.OnPostDealDamage,
            PassiveSkillTrigger.OnPostReceiveDamage,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnBattleStart:
                    kensaki = System.Math.Min(99, kensaki + 1);
                    UnityEngine.Debug.Log($"[無銘の剣舞譜] 戦闘突入: 剣先スタック → {kensaki}");
                    break;
                case PassiveSkillTrigger.OnPostRoll:
                    // 旧: ダイス合計+剣先 / 新: 攻撃+剣先
                    if (kensaki > 0) ctx.AddPlayerAttackOrDiceBonus(kensaki);
                    break;
                case PassiveSkillTrigger.OnPostDealDamage:
                    // 与ダメージ時: 剣先スタック分HP回復
                    if (kensaki > 0 && ctx.finalDamage > 0)
                        ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + kensaki);
                    break;
                case PassiveSkillTrigger.OnPostReceiveDamage:
                    // 被ダメージ時: 剣先スタック分の軽減不可ダメージを相手へ
                    if (kensaki > 0 && ctx.finalDamage > 0)
                        ctx.fixedDamageToEnemy += kensaki;
                    break;
            }
        }
    }

}

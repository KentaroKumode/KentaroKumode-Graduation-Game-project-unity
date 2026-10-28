namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  武器家系 専用パッシブ (2026-09-05 リワーク / 2026-10-28 整理)
    //
    //  **なぜ作ったか。** 武器 4 家系は共通ラダー (筋力/追撃/心眼/頑強/活力) を
    //  2 本ずつ借りて組まれていた。 これは<b>アイテム側の家系システムの名残</b>で、
    //  借り物である以上「剣と斧の違い」は数値の大小でしか出せなかった
    //  (剣=筋力+追撃 / 斧=筋力+心眼 ── 半分が同じ)。
    //  ここでは家系ごとに専用ラダーを 2 本ずつ持たせ、 個性を機構で出す。
    //
    //  **2026-10-28 整理 (ADR-0013 の続き)**: 1 段に仕組みは 1 つ。 家系ごとに
    //  防御と貫通の使い方を分けた (2×2: 敵の防御を越える側 = 短剣/斧、 自分の防御を使う側 = 剣/盾)。
    //
    //  | 家系 | 性格 | ラダーA | ラダーB | 固有1 | 固有2 |
    //  |---|---|---|---|---|---|
    //  | 短剣 | すり抜ける (開幕から装甲を抜ける) | 疾手 | 毒手 | 急所 | 処刑 |
    //  | 剣   | 攻防一体 (分け続けるほど伸びる)   | 間合 | 一対一 | 切り返し | 不滅の刃 |
    //  | 盾   | 受けて溜め、 まとめて放つ        | 城壁 | シールドバッシュ | 堅守の構え | 黎明 |
    //  | 斧   | 叩き割る (殴るほど敵の防御が下がる) | 猛り | 鎧砕き | 捨て身 | 血令 |
    //
    //  **発火順序の約束 (ADR-0009 §9.2)**:
    //    攻撃値への加算は <b>OnPostRoll</b> か <b>OnWired</b> で <c>AddPlayerAttackOrDiceBonus</c> を使う。
    //    OnPreDealDamage は <c>atkBase</c> が確定した**後**に走るので、 そこで攻撃を足しても乗らない。
    //    配線 (どの端子に何本挿したか) を条件にする効果は OnWired でしか判定できない ──
    //    OnPostRoll は予告より前、 つまり配線より前に走る。
    //    与ダメ % は OnPreDealDamage (<c>outgoingDamageMultiplier</c>)、 会心率は OnCriticalCheck。
    // ============================================================

    /// <summary>相互攻撃モデルで CombatManager がターンごとに書く値のキー (accumulatedValues)。
    /// 毎ターン配線確定時に上書きされる [perTurn] 扱い。</summary>
    public static class MutualKeys
    {
        public const string AttackDiceCount  = "mutualAttackDiceCount";
        public const string BlockDiceCount   = "mutualBlockDiceCount";
        public const string ChargeDiceCount  = "mutualChargeDiceCount";
        public const string SpecialDiceCount = "mutualSpecialDiceCount";
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
        /// <summary>与ダメ倍率へ加算 (未初期化の 0 を 1.0 に直してから足す)。</summary>
        public static void Outgoing(CombatContext ctx, float add)
        {
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += add;
        }

        private static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);

        /// <summary>攻撃端子とブロック端子の両方に挿した (剣の条件)。</summary>
        public static bool Split(CombatContext ctx)
            => Count(ctx, MutualKeys.AttackDiceCount) > 0 && Count(ctx, MutualKeys.BlockDiceCount) > 0;

        /// <summary>出目を全部攻撃端子に挿した (盾〈シールドバッシュ〉の条件)。</summary>
        public static bool AllAttack(CombatContext ctx)
            => Count(ctx, MutualKeys.AttackDiceCount) > 0 && Count(ctx, MutualKeys.BlockDiceCount) == 0
            && Count(ctx, MutualKeys.ChargeDiceCount) == 0 && Count(ctx, MutualKeys.SpecialDiceCount) == 0;

        /// <summary>出目を全部ブロック端子に挿した (盾〈堅守の構え〉の条件)。</summary>
        public static bool AllBlock(CombatContext ctx)
            => Count(ctx, MutualKeys.BlockDiceCount) > 0 && Count(ctx, MutualKeys.AttackDiceCount) == 0
            && Count(ctx, MutualKeys.ChargeDiceCount) == 0 && Count(ctx, MutualKeys.SpecialDiceCount) == 0;

        public static void GainShield(CombatContext ctx, int amount, string label)
        {
            if (amount <= 0) return;
            ctx.consShield += amount;
            ctx.shieldGainedTotal += amount;
            CombatSystem.ShieldDiag.Note(label, amount);
        }
    }

    // ============================================================
    //  短剣 — すり抜ける: 開幕から装甲を抜ける (貫通が家系で最大・防御 0)
    // ============================================================

    /// <summary>疾手 — 戦闘の最初の 3 ターンだけ攻撃 +N。 4T 目以降は何もしない。
    ///
    /// <para>短剣の「短期決戦」を数値でなく<b>期限</b>で表す。 常時 +N の筋力より瞬間値は高いが、
    /// エスカレーションが効き始める帯 (T5 で ×1.30) には既に切れている ──
    /// 「早く終わらせられないなら弱い武器」という性格をここで作る。</para></summary>
    internal static class SwiftHandHelper
    {
        public const int Window = 3;
        public static void Apply(CombatContext ctx, int atk)
        {
            if (ctx.currentTurn > Window) return;
            ctx.AddPlayerAttackOrDiceBonus(atk);
        }
    }

    public class SwiftHandI : IPassiveSkillEffect
    {
        public string SkillId => "疾手I";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => SwiftHandHelper.Apply(ctx, 3);
    }

    /// <summary>毒手 — 戦闘開始時に毒 N、 会心が出たターンにさらに毒 +1。
    ///
    /// <para><b>前倒しの DOT。</b> 毒 (StatusRegistry) は減衰なし・上限 10・stacks×1 を軽減無視で毎T。
    /// 積み上げ型にすると「長引くほど強い」になり短期決戦と噛み合わないので、
    /// <b>開幕にまとめて置いて</b>初手から効かせる。 伸びしろは会心に紐付けたので、
    /// 会心率の高い短剣ほど伸びる ── ラダー同士が同じ方向を向く。</para></summary>
    internal static class VenomHandHelper
    {
        public static void Open(CombatContext ctx, int stacks)
            => ctx.AddStatus(StatusTarget.Enemy, "poison", stacks);

        public static void OnCrit(CombatContext ctx)
        {
            if (ctx.isCritical) ctx.AddStatus(StatusTarget.Enemy, "poison", 1);
        }
    }

    public class VenomHandI : IPassiveSkillEffect
    {
        public string SkillId => "毒手I";
        public PassiveSkillTrigger[] Triggers => new[]
        { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) VenomHandHelper.Open(ctx, 2);
            else VenomHandHelper.OnCrit(ctx);
        }
    }

    public class VenomHandII : IPassiveSkillEffect
    {
        public string SkillId => "毒手II";
        public PassiveSkillTrigger[] Triggers => new[]
        { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) VenomHandHelper.Open(ctx, 3);
            else VenomHandHelper.OnCrit(ctx);
        }
    }

    public class VenomHandIII : IPassiveSkillEffect
    {
        public string SkillId => "毒手III";
        public PassiveSkillTrigger[] Triggers => new[]
        { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) VenomHandHelper.Open(ctx, 4);
            else VenomHandHelper.OnCrit(ctx);
        }
    }

    /// <summary>急所 (2026-10-28) — <b>会心の一撃は敵の防御を無視する</b>。 会心率 +10% は items.json の stats。
    ///
    /// <para>静的な貫通が届かない段 (T3 の貫通 8〜10 に対し 3 層の高防御の敵) でも、 会心なら素通しになる。
    /// 判定は <c>CombatManager.ApplyWinDamageModifiers</c> の敵の防御の段で <see cref="IgnoresDefense"/> を読む。
    /// 印は戦闘中ずっと立てたまま (ヴェスカの形態 swap でも消えない)。</para></summary>
    public class VitalStrike : IPassiveSkillEffect
    {
        public const string Key = "vitalStrikeIgnoresDefense";
        public string SkillId => "急所";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.accumulatedValues[Key] = 1f;

        public static bool IgnoresDefense(CombatContext ctx) => ctx != null && ctx.GetAccumulated(Key) > 0f;
    }

    // 〈処刑〉は items.json の stats だけになった (敵の残HP 25% 以下で与ダメ +80%)。
    //   旧: 勝利時に次ターン敵の最大ダイスを 1 に固定する固有部分を持っていた (2026-10-28 削除・GAME.md §24)。
    // 〈蝕夜〉(過剰ダメージを次の戦闘へ持ち越す) は 2026-10-28 削除。

    // ============================================================
    //  剣 — 攻防一体: 出目を攻めと守りに分け続けるほど伸びる (貫通・防御とも中)
    // ============================================================

    /// <summary>間合 — 攻撃 +N ／ 敵の固定削り −M。 常時・無条件。 II / III は items.json の stats。
    ///
    /// <para>剣の防御は盾ほど厚くないが、 <b>固定削りで剥がされない</b>。 固定削りの大きいボスの前で
    /// 防御が残るのは剣だけ、 という位置を作る。 打ち消しは <see cref="CombatContext.enemyArmorPenNegate"/>。</para></summary>
    public class SwordReachI : IPassiveSkillEffect
    {
        public string SkillId => "間合I";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            ctx.AddPlayerAttackOrDiceBonus(2);
            ctx.enemyArmorPenNegate += 2;   // [perTurn]
        }
    }

    /// <summary>一対一 — <b>単体戦のときだけ</b> 攻撃 +N / 防御 +M。 2 体戦では一切乗らない。
    /// 防御は 2026-10-28 に被ダメ −M から置き換えた (ADR-0013)。 予告より前に乗せるため OnPostRoll で足す。
    ///
    /// <para>「タイマン力」を条件で表す。 <see cref="CombatContext.isPairEncounter"/> は
    /// 戦闘開始時に確定する [persistent] なので、 戦闘中に切り替わることはない。
    /// 単体戦を条件にする段は家系でこれ 1 つだけ (2026-10-28・旧〈果たし合い〉と重なっていた)。</para>
    ///
    /// <para><b>死に札にはならない。</b> 戦闘名は 1 マス手前で開示される (EncounterPresets) ので、
    /// 「剣を担いでいる間はペアを避ける」が<b>プレイヤーの選択として成立する</b>。
    /// 条件付きを強く振れるのはマップ開示があるからで、 開示を外すならこの数値も見直すこと。</para></summary>
    internal static class DuelistHelper
    {
        public static void Apply(PassiveSkillTrigger t, CombatContext ctx, int atk, int def)
        {
            if (ctx.isPairEncounter) return;
            ctx.AddPlayerAttackOrDiceBonus(atk);
            ctx.playerDefense += def;   // [perTurn] BeginNewTurn で土台へ戻るので毎ターン足し直す
        }
    }

    public class DuelistIII : IPassiveSkillEffect
    {
        public string SkillId => "一対一III";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => DuelistHelper.Apply(t, ctx, 8, 4);
    }

    /// <summary>切り返し (2026-10-28 リワーク) — 攻撃端子とブロック端子の<b>両方に</b>挿したターン、
    /// ブロックした出目合計 × 0.5 を攻撃にも加える。
    ///
    /// <para><b>しきい値を持たない</b>ので、 敵の攻撃がいくら大きくても効く。 旧〈切り返し〉
    /// (被弾した次のターン 攻撃 +7) は相互攻撃モデルではほぼ毎ターン成立し、 その次の案
    /// (完全に受け止めた次のターン) はボスの攻撃をブロックで受け切れないので雑魚戦専用になった。</para>
    ///
    /// <para><b>×0.5 である理由。</b> ×1 以上だと「分ける」が「全部攻撃」より常に得になり、
    /// 配線の選択が消える。 ブロック量は〈貫きの錐〉等で削られた後の値 (blockSumThisTurn)。</para></summary>
    public class GuardCut : IPassiveSkillEffect
    {
        public const float Rate = 0.5f;
        public string SkillId => "切り返し";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Fam.Split(ctx) || ctx.blockSumThisTurn <= 0) return;
            int add = UnityEngine.Mathf.CeilToInt(ctx.blockSumThisTurn * Rate);
            ctx.AddPlayerAttackOrDiceBonus(add);
            UnityEngine.Debug.Log($"[切り返し] ブロック{ctx.blockSumThisTurn} × {Rate} → 攻撃 +{add}");
        }
    }

    /// <summary>不滅の刃 (2026-10-28・旧〈果たし合い〉の枠) — 攻撃とブロックの両方に挿したターンが<b>続くたび</b>
    /// 与ダメ +8% (連続 n ターン目は +8%×n)。 両方に挿さなかったターンで 0 に戻る。
    ///
    /// <para>長い戦闘ほど伸びる ＝ ボス戦 (10T+) 向きで、 雑魚戦 (2〜5T) では育ち切らない。
    /// とどめの全部攻撃・リロール用の全部充電で途切れるのが代償。 上限は置かない ──
    /// エスカレーションで敵の攻撃が伸び、 守りに本数を割き続けること自体が重くなる。</para></summary>
    public class UndyingBlade : IPassiveSkillEffect
    {
        public const string StreakKey = "undyingBladeStreak";
        public const float PerTurn = 0.08f;
        public string SkillId => "不滅の刃";
        public PassiveSkillTrigger[] Triggers => new[]
        { PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired)
            {
                ctx.accumulatedValues[StreakKey] = Fam.Split(ctx) ? ctx.GetAccumulated(StreakKey) + 1f : 0f;
                return;
            }
            int streak = (int)ctx.GetAccumulated(StreakKey);
            if (streak <= 0) return;
            Fam.Outgoing(ctx, PerTurn * streak);
        }
    }

    // ============================================================
    //  盾 — 受けて溜め、 まとめて放つ (防御が家系で最大・貫通 0)
    //    溜める: 城壁 (余ったブロック) / 堅守の構え (全部ブロック)
    //    放つ  : シールドバッシュ (全部攻撃) / 黎明 (無傷で受け切ったとき)
    //    溜めているだけのターンは火力ゼロ ── 防御を連打しても最強にはならない。
    // ============================================================

    /// <summary>城壁 (2026-10-28 リワーク) — <b>完全に受け止めたターン</b>
    /// (ブロック合計 ≥ 予告の敵攻撃・防御後)、 余ったブロック × 0.5 / 0.75 / 1.0 をシールドへ。
    ///
    /// <para>ターン終了時に入れる ── このターンの被弾は受け切っているので、 余りは翌ターン以降の弾。
    /// シールドは毎ターン頭に半減する (DAMAGE_CALC_REFERENCE ③-D) ので溜めっぱなしにはできない。
    /// 旧: ターン終了時 HP +1 / +1 / +2 (さらに前は 被ダメ −N・ADR-0013 で武器の防御へ移した)。</para></summary>
    internal static class BulwarkHelper
    {
        public static void Apply(CombatContext ctx, float rate, string label)
        {
            int block = ctx.blockSumThisTurn;
            if (block <= 0) return;
            int enemyAtk = (int)ctx.GetAccumulated(MutualKeys.EnemyAtk);
            if (block < enemyAtk) return;
            int gain = UnityEngine.Mathf.FloorToInt((block - enemyAtk) * rate);
            Fam.GainShield(ctx, gain, label);
        }
    }

    public class BulwarkI : IPassiveSkillEffect
    {
        public string SkillId => "城壁I";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => BulwarkHelper.Apply(ctx, 0.5f, SkillId);
    }

    public class BulwarkII : IPassiveSkillEffect
    {
        public string SkillId => "城壁II";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => BulwarkHelper.Apply(ctx, 0.75f, SkillId);
    }

    public class BulwarkIII : IPassiveSkillEffect
    {
        public string SkillId => "城壁III";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => BulwarkHelper.Apply(ctx, 1.0f, SkillId);
    }

    /// <summary>シールドバッシュ (2026-10-28・旧〈反攻〉の枠) — <b>出目を全部攻撃端子に挿したターン</b>、
    /// シールドを全部使い切り、 その量 × 0.5 / 1.0 / 1.5 を攻撃に加える。
    ///
    /// <para><b>攻撃値に入れる</b>ので、 倍率・会心・エスカレーションが全部乗る。 旧〈反攻〉は固定ダメの枠
    /// (fixedDamageToEnemy) で何も掛からず、 出目 3 個の合計 × 1.0 ではボス HP の 1% に届かなかった。
    /// 自攻撃の後に敵攻撃が来るので、 使い切ったシールドはそのターンの被弾を受けない ── それが代償。</para></summary>
    internal static class ShieldBashHelper
    {
        public static void Apply(CombatContext ctx, float rate, string label)
        {
            if (!Fam.AllAttack(ctx) || ctx.consShield <= 0) return;
            int spent = ctx.consShield;
            int add = UnityEngine.Mathf.CeilToInt(spent * rate);
            ctx.consShield = 0;
            ctx.AddPlayerAttackOrDiceBonus(add);
            UnityEngine.Debug.Log($"[{label}] シールド{spent} を使い切り × {rate} → 攻撃 +{add}");
        }
    }

    public class ShieldBashLadderI : IPassiveSkillEffect
    {
        public string SkillId => "シールドバッシュI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ShieldBashHelper.Apply(ctx, 0.5f, SkillId);
    }

    public class ShieldBashLadderII : IPassiveSkillEffect
    {
        public string SkillId => "シールドバッシュII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ShieldBashHelper.Apply(ctx, 1.0f, SkillId);
    }

    public class ShieldBashLadderIII : IPassiveSkillEffect
    {
        public string SkillId => "シールドバッシュIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ShieldBashHelper.Apply(ctx, 1.5f, SkillId);
    }

    /// <summary>堅守の構え (2026-10-28・旧〈パリィ〉の枠) — <b>出目を全部ブロック端子に挿したターン</b>、
    /// 相互攻撃フェーズの開始時にシールド +15。 次の 2 ターンは発動しない。
    ///
    /// <para>旧〈パリィ〉は最初の被弾で自動発動し T1・T4・T7… と続くので、 5 層以降の大技 (T3・T6・T9) と
    /// 一度も重ならなかった。 こちらは発動をプレイヤーが選ぶ。 使えるのは 3 ターンに 1 回で大技の周期と同じなので、
    /// 一度大技に合わせれば揃い続ける。 OnWired はターン頭の半減より後なので、 そのターンは 15 を丸ごと使える。</para></summary>
    public class SteadfastStance : IPassiveSkillEffect
    {
        public const int Shield = 15;
        public const int Cooldown = 3;   // 発動ターン + 3 から再び使える = 次の 2 ターンは不可
        private const string ReadyKey = "steadfastStanceReadyTurn";
        public string SkillId => "堅守の構え";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Fam.AllBlock(ctx)) return;
            if (ctx.currentTurn < (int)ctx.GetAccumulated(ReadyKey)) return;
            Fam.GainShield(ctx, Shield, SkillId);
            ctx.accumulatedValues[ReadyKey] = ctx.currentTurn + Cooldown;
            UnityEngine.Debug.Log($"[堅守の構え] シールド +{Shield} (次に使えるのは T{ctx.currentTurn + Cooldown})");
        }
    }

    /// <summary>黎明 (2026-10-28・旧〈衛士の慣い〉の枠) — 敵の攻撃の後、 <b>HP の減少が 0</b> だったら、
    /// このターン敵が放った軽減前ダメージ 10 につき 敵の最大 HP の 5% を削る。
    ///
    /// <para>数え方 (2026-10-28 決定):
    /// 軽減前 = 直接攻撃の予告値のうちプレイヤーの防御を引く前 (エスカレーション・大技・〈封じ〉は反映済み、
    /// パッシブ由来のダメージは含めない)。 2 体戦は 2 体の合計。 シールドで吸収して HP が減らなければ成立、
    /// 軽減無視のパッシブで HP が減ったら不成立。 10 未満は切り捨て。 ヴェスカは今の形態の最大 HP。</para>
    ///
    /// <para>素の装備では成立は〈堅守の構え〉のターン (3 ターンに 1 回) が上限。 敵の攻撃が伸びるほど払いは増えるが、
    /// 受け切る条件も厳しくなるので打ち消し合う。 〈天与の才〉の踏みとどまりは通す (役〈極〉と違い、 削りを謳う効果ではないため)。</para></summary>
    public class Daybreak : IPassiveSkillEffect
    {
        public const int PerUnit = 10;
        public const int PctPerUnit = 5;
        public string SkillId => "黎明";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int pre = (int)ctx.GetAccumulated(MutualKeys.EnemyAtkPreDefense);
            int units = pre / PerUnit;
            if (units <= 0) return;
            if (ctx.GetAccumulated(MutualKeys.HpLossFromEnemy) > 0f) return;
            if (ctx.enemyCurrentHP <= 0 || ctx.enemyMaxHP <= 0) return;
            int dmg = System.Math.Max(1, ctx.enemyMaxHP * PctPerUnit * units / 100);
            int before = ctx.enemyCurrentHP;
            int after = DivineTalent.ClampToThreshold(ctx, System.Math.Max(0, before - dmg), ctx.enemyMaxHP);
            ctx.enemyCurrentHP = after;
            UnityEngine.Debug.Log($"[黎明] 無傷で受け切った: 軽減前 {pre} → {units}×{PctPerUnit}% = {dmg} "
                                + $"(敵HP {before} → {after})");
        }
    }

    // ============================================================
    //  斧 — 叩き割る: 殴るほど敵の防御が下がり、 自分の守りは捨てる (貫通高・防御低)
    // ============================================================

    /// <summary>猛り — ターンが経つごとに攻撃 +1 (上限 N)。 自己バフの本体。
    ///
    /// <para>蓄積用の状態を持たず <see cref="CombatContext.currentTurn"/> から直に引く ──
    /// 累積カウンタは連戦 (7層ヴェスカ) で currentTurn がリセットされない事情と絡んで
    /// 挙動が読みにくくなるので、 素直に「経過ターン」を式に入れる。</para></summary>
    internal static class FervorHelper
    {
        public static void Apply(CombatContext ctx, int cap)
        {
            int n = System.Math.Min(System.Math.Max(0, ctx.currentTurn - 1), cap);
            if (n > 0) ctx.AddPlayerAttackOrDiceBonus(n);
        }
    }

    public class FervorI : IPassiveSkillEffect
    {
        public string SkillId => "猛りI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => FervorHelper.Apply(ctx, 5);
    }

    public class FervorII : IPassiveSkillEffect
    {
        public string SkillId => "猛りII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => FervorHelper.Apply(ctx, 9);
    }

    public class FervorIII : IPassiveSkillEffect
    {
        public string SkillId => "猛りIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => FervorHelper.Apply(ctx, 14);
    }

    /// <summary>鎧砕き (2026-10-28・旧〈大鉈〉の枠) — 与ダメが通ったターンの終わりに、 敵の防御 −2 / −3 / −4。
    ///
    /// <para>戦闘中ずっと残り、 下限 0。 ヴェスカの形態 swap では新しい形態の防御に戻る (SwapEnemy が設定し直す)。
    /// 2 体戦は敵の防御を共有しているので両方割れる。 短剣が「初手から抜ける」のに対し、 斧は「時間をかけて割る」。
    /// 旧〈大鉈〉(敵の失った HP 割合で与ダメ +%) は〈捨て身〉と同じ HP 割合条件で重なっていたので外した。</para></summary>
    internal static class ArmorBreakHelper
    {
        public static void Apply(CombatContext ctx, int amount, string label)
        {
            if (ctx.lastPlayerDamage <= 0 || ctx.enemyDefense <= 0) return;
            int before = ctx.enemyDefense;
            ctx.enemyDefense = System.Math.Max(0, before - amount);
            UnityEngine.Debug.Log($"[{label}] 敵の防御 {before} → {ctx.enemyDefense}");
        }
    }

    public class ArmorBreakI : IPassiveSkillEffect
    {
        public string SkillId => "鎧砕きI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ArmorBreakHelper.Apply(ctx, 2, SkillId);
    }

    public class ArmorBreakII : IPassiveSkillEffect
    {
        public string SkillId => "鎧砕きII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ArmorBreakHelper.Apply(ctx, 3, SkillId);
    }

    public class ArmorBreakIII : IPassiveSkillEffect
    {
        public string SkillId => "鎧砕きIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ArmorBreakHelper.Apply(ctx, 4, SkillId);
    }

    // 〈捨て身〉(HP 割合で敵に負けている間 与ダメ +50%) は items.json の stats だけ。
    //   旧〈血令〉の効果を改名して T3+ へ移し、 被ダメ −15% を外した (2026-10-28)。
    // 〈復讐〉(被弾ごとに攻撃 +2) は 2026-10-28 削除 ── 相互攻撃モデルではほぼ毎ターン被弾するので〈猛り〉と同じ意味だった。

    /// <summary>血令 (2026-10-28) — <b>自分の防御をすべて貫通に換える</b> (防御 1 → 貫通 2) ／
    /// 〈血の盟約〉: 失った HP 15% ごとに 1 (最大 6)。 1 つにつき 攻撃 +1、 攻撃したターンの終わりに HP +1。
    ///
    /// <para><b>盟約はその時点の HP から数える</b> (リアルタイム)。 回復すると減るので仕組みの中で釣り合う。
    /// 上限 6 は失った HP 90% にあたる ── 実質は HP が決める。</para>
    ///
    /// <para>換算は予告より前・全部の防御加算 (OnPostRoll) の後でなければならないので、
    /// CombatManager が OnPostRoll の直後に <see cref="ConvertDefenseToPen"/> を呼ぶ。
    /// リロール判断用の仮予告はそれより前に組まれるので、 <see cref="IsActive"/> を見て防御を掛けない。
    /// 余った貫通は何にもならないので、 防御を積んで換えても青天井にはならない。</para></summary>
    public class BloodEdict : IPassiveSkillEffect
    {
        public const string ActiveKey = "bloodEdictDefToPen";
        public const int PenPerDefense = 2;
        public const int PactStepPct = 15;
        public const int PactMax = 6;
        public string SkillId => "血令";
        public PassiveSkillTrigger[] Triggers => new[]
        { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnTurnEnd };

        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            switch (t)
            {
                case PassiveSkillTrigger.OnBattleStart:
                    ctx.accumulatedValues[ActiveKey] = 1f;
                    break;
                case PassiveSkillTrigger.OnPostRoll:
                {
                    int pact = Pact(ctx);
                    if (pact > 0) ctx.AddPlayerAttackOrDiceBonus(pact);
                    break;
                }
                case PassiveSkillTrigger.OnTurnEnd:
                {
                    if (ctx.GetAccumulated(MutualKeys.AttackDiceCount) <= 0f) return;
                    if (ctx.healBlocked || ctx.playerCurrentHP <= 0) return;
                    int pact = Pact(ctx);
                    if (pact <= 0) return;
                    ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + pact);
                    break;
                }
            }
        }

        /// <summary>〈血の盟約〉の数 = floor(失った HP 割合 / 15%)、 最大 6。</summary>
        public static int Pact(CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0) return 0;
            int lost = System.Math.Max(0, ctx.playerMaxHP - ctx.playerCurrentHP);
            return System.Math.Min(PactMax, lost * 100 / (ctx.playerMaxHP * PactStepPct));
        }

        public static bool IsActive(CombatContext ctx) => ctx != null && ctx.GetAccumulated(ActiveKey) > 0f;

        public static void ConvertDefenseToPen(CombatContext ctx)
        {
            if (!IsActive(ctx) || ctx.playerDefense <= 0) return;
            int def = ctx.playerDefense;
            ctx.playerArmorPen += def * PenPerDefense;
            ctx.playerDefense = 0;
            UnityEngine.Debug.Log($"[血令] 防御{def} → 貫通 +{def * PenPerDefense} (貫通 {ctx.playerArmorPen})");
        }
    }

    // 複合武器の固有 6 種 (2026-09-05 追加) は 2026-09-21 削除。
    //   複合武器そのものを廃止し、 純 4 家系の T2〜T4 ラダーへ一本化した (GAME.md §24)。
}

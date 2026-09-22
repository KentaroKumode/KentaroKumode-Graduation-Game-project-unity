namespace InventorySystem.PassiveSkills.Effects
{
    // ============================================================
    //  武器家系 専用パッシブ (2026-09-05 リワーク)
    //
    //  **なぜ作ったか。** 武器 4 家系は共通ラダー (筋力/追撃/心眼/頑強/活力) を
    //  2 本ずつ借りて組まれていた。 これは<b>アイテム側の家系システムの名残</b>で、
    //  借り物である以上「剣と斧の違い」は数値の大小でしか出せなかった
    //  (剣=筋力+追撃 / 斧=筋力+心眼 ── 半分が同じ)。
    //  ここでは家系ごとに専用ラダーを 2 本ずつ持たせ、 個性を機構で出す。
    //
    //  | 家系 | 性格 | ラダーA | ラダーB | 固有1 | 固有2 |
    //  |---|---|---|---|---|---|
    //  | 短剣 | 短期決戦・一撃必殺 + DOT削り | 疾手 | 毒手 | 処刑 | 蝕夜 |
    //  | 剣   | タイマン力 + バランス        | 間合 | 一対一 | 切り返し | 果たし合い |
    //  | 盾   | 生存 + カウンター            | 城壁 | 反攻 | パリィ | 衛士の慣い |
    //  | 斧   | 削り合いレース + 自己バフ    | 猛り | 大鉈 | 復讐 | 血令 |
    //
    //  **発火順序の約束 (ADR-0009 §9.2)**:
    //    攻撃値への加算は <b>OnPostRoll</b> で <c>AddPlayerAttackOrDiceBonus</c> を使う。
    //    OnPreDealDamage は <c>atkBase</c> が確定した**後**に走るので、 そこで攻撃を足しても乗らない。
    //    与ダメ % は OnPreDealDamage (<c>outgoingDamageMultiplier</c>)、
    //    被ダメ軽減は OnPreReceiveDamage (<c>finalDamage</c>)、 会心率は OnCriticalCheck。
    // ============================================================

    internal static class Fam
    {
        /// <summary>与ダメ倍率へ加算 (未初期化の 0 を 1.0 に直してから足す)。</summary>
        public static void Outgoing(CombatContext ctx, float add)
        {
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += add;
        }

        /// <summary>被ダメの定額軽減 (下限 0)。</summary>
        public static void Reduce(CombatContext ctx, int n)
        {
            if (ctx.finalDamage > 0)
                ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - n);
        }

        /// <summary>被ダメの割合軽減 (下限 0)。</summary>
        public static void ReducePct(CombatContext ctx, float pct)
        {
            if (ctx.finalDamage > 0)
                ctx.finalDamage = System.Math.Max(0, UnityEngine.Mathf.CeilToInt(ctx.finalDamage * (1f - pct)));
        }

        /// <summary>敵が失った HP の割合 (0.0〜1.0)。 最大HP が不明なら 0。</summary>
        public static float EnemyHpLostRatio(CombatContext ctx)
        {
            if (ctx.enemyMaxHP <= 0) return 0f;
            float cur = UnityEngine.Mathf.Clamp(ctx.enemyCurrentHP, 0, ctx.enemyMaxHP);
            return 1f - cur / ctx.enemyMaxHP;
        }

        /// <summary>自分の HP 割合が敵の HP 割合を下回っているか (＝削り合いで負けている)。</summary>
        public static bool Behind(CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0 || ctx.enemyMaxHP <= 0) return false;
            float me = ctx.playerCurrentHP / (float)ctx.playerMaxHP;
            float foe = ctx.enemyCurrentHP / (float)ctx.enemyMaxHP;
            return me < foe;
        }
    }

    // ============================================================
    //  短剣 — 短期決戦・一撃必殺 + デバフ/DOT での削り
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

    // ============================================================
    //  剣 — タイマン力 + バランス
    // ============================================================

    /// <summary>間合 — 攻撃 +N かつ 被ダメ −M。 常時・無条件。
    ///
    /// <para>剣の「バランス」は<b>穴が無いこと</b>なので、 1 本のラダーで攻防を両方持つ。
    /// 単体で見れば筋力にも頑強にも劣るが、 どちらの側にも落ち込みが無い。</para></summary>
    internal static class SwordReachHelper
    {
        public static void Apply(PassiveSkillTrigger t, CombatContext ctx, int atk, int def)
        {
            if (t == PassiveSkillTrigger.OnPostRoll) ctx.AddPlayerAttackOrDiceBonus(atk);
            else Fam.Reduce(ctx, def);
        }
    }

    public class SwordReachI : IPassiveSkillEffect
    {
        public string SkillId => "間合I";
        public PassiveSkillTrigger[] Triggers => new[]
        { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => SwordReachHelper.Apply(t, ctx, 2, 1);
    }



    /// <summary>一対一 — <b>単体戦のときだけ</b> 攻撃 +N / 被ダメ −M。 2 体戦では一切乗らない。
    ///
    /// <para>「タイマン力」を条件で表す。 <see cref="CombatContext.isPairEncounter"/> は
    /// 戦闘開始時に確定する [persistent] なので、 戦闘中に切り替わることはない。</para>
    ///
    /// <para><b>死に札にはならない。</b> 戦闘名は 1 マス手前で開示される (EncounterPresets) ので、
    /// 「剣を担いでいる間はペアを避ける」が<b>プレイヤーの選択として成立する</b>。
    /// 条件付きを強く振れるのはマップ開示があるからで、 開示を外すならこの数値も見直すこと。</para></summary>
    internal static class DuelistHelper
    {
        public static void Apply(PassiveSkillTrigger t, CombatContext ctx, int atk, int def)
        {
            if (ctx.isPairEncounter) return;
            if (t == PassiveSkillTrigger.OnPostRoll) ctx.AddPlayerAttackOrDiceBonus(atk);
            else Fam.Reduce(ctx, def);
        }
    }

    public class DuelistIII : IPassiveSkillEffect
    {
        public string SkillId => "一対一III";
        public PassiveSkillTrigger[] Triggers => new[]
        { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => DuelistHelper.Apply(t, ctx, 8, 4);
    }

    // ============================================================
    //  盾 — 生存 + カウンター
    // ============================================================

    /// <summary>城壁 — 被ダメ −N かつ ターン終了時に自HP +M。 生存の柱。</summary>
    internal static class BulwarkHelper
    {
        public static void Apply(PassiveSkillTrigger t, CombatContext ctx, int def, int heal)
        {
            if (t == PassiveSkillTrigger.OnPreReceiveDamage) { Fam.Reduce(ctx, def); return; }
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + heal);
        }
    }

    public class BulwarkI : IPassiveSkillEffect
    {
        public string SkillId => "城壁I";
        public PassiveSkillTrigger[] Triggers => new[]
        { PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => BulwarkHelper.Apply(t, ctx, 2, 1);
    }



    /// <summary>反攻 — <b>そのターン Block 端子へ配線した出目合計</b> × 係数 を軽減無視で敵へ返す。
    ///
    /// <para>盾の「カウンター」を、 被ダメ量ではなく<b>守った量</b>に紐付ける。
    /// 被ダメ反射 (切り返し) は「殴られないと返せない」＝守りが成功するほど弱い、という逆立ちがあった。
    /// こちらは<b>ブロックへ挿すこと自体が火力</b>になるので、 ADR-0009 の
    /// 「攻撃へ挿すか守りへ挿すか」の二択に盾だけの第三の答えが立つ。</para>
    ///
    /// <para><see cref="CombatContext.blockSumThisTurn"/> は配線集計 (§9.2 step5) で確定し、
    /// 解決 (step6) より前なので OnPreDealDamage から読める。 貫通で削られた後の値が入る。</para></summary>
    internal static class RetaliationHelper
    {
        public static void Apply(CombatContext ctx, float rate)
        {
            int blocked = ctx.blockSumThisTurn;
            if (blocked <= 0) return;
            ctx.fixedDamageToEnemy += UnityEngine.Mathf.CeilToInt(blocked * rate);
        }
    }

    public class RetaliationI : IPassiveSkillEffect
    {
        public string SkillId => "反攻I";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => RetaliationHelper.Apply(ctx, 0.4f);
    }

    public class RetaliationII : IPassiveSkillEffect
    {
        public string SkillId => "反攻II";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => RetaliationHelper.Apply(ctx, 0.7f);
    }

    public class RetaliationIII : IPassiveSkillEffect
    {
        public string SkillId => "反攻III";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => RetaliationHelper.Apply(ctx, 1.0f);
    }

    // ============================================================
    //  斧 — 削り合いレース最強 + 自己バフ
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

    /// <summary>大鉈 — 敵が失った HP の割合に比例して与ダメ +N%。
    ///
    /// <para>「削り合いレース」の直接表現。 <b>先に削った側が加速する</b>ので、
    /// 同じ土俵に立った時点で斧が勝つ。 逆に一撃も入っていないうちは 0 ＝
    /// 開幕の押し込みは短剣に譲る。</para></summary>
    internal static class CleaverHelper
    {
        public static void Apply(CombatContext ctx, float perFullHp)
        {
            float lost = Fam.EnemyHpLostRatio(ctx);
            if (lost <= 0f) return;
            Fam.Outgoing(ctx, lost * perFullHp);
        }
    }

    public class CleaverI : IPassiveSkillEffect
    {
        public string SkillId => "大鉈I";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => CleaverHelper.Apply(ctx, 0.30f);
    }

    public class CleaverII : IPassiveSkillEffect
    {
        public string SkillId => "大鉈II";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => CleaverHelper.Apply(ctx, 0.50f);
    }

    public class CleaverIII : IPassiveSkillEffect
    {
        public string SkillId => "大鉈III";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => CleaverHelper.Apply(ctx, 0.80f);
    }

    // 複合武器の固有 6 種 (2026-09-05 追加) は 2026-09-21 削除。
    //   複合武器そのものを廃止し、 純 4 家系の T2〜T4 ラダーへ一本化した (GAME.md §24)。
}

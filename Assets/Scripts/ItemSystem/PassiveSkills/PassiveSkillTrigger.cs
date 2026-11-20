namespace ItemSystem.PassiveSkills
{
    /// <summary>
    /// パッシブスキルが発動するタイミング（トリガー条件）
    /// 各スキルは1つ以上のトリガーに反応する
    /// </summary>
    public enum PassiveSkillTrigger
    {
        // === 戦闘フロー ===
        /// <summary>戦闘開始時</summary>
        OnBattleStart,
        /// <summary>ターン開始時</summary>
        OnTurnStart,
        /// <summary>ターン終了時</summary>
        OnTurnEnd,
        /// <summary>戦闘終了時</summary>
        OnBattleEnd,

        // === ダイスロール ===
        /// <summary>ダイスロール直前（ダイス補正用）</summary>
        OnPreRoll,
        /// <summary>ダイスロール直後（結果確定前）</summary>
        OnPostRoll,

        // === 勝敗判定 ===
        /// <summary>ロール勝利時</summary>
        OnRollWin,
        /// <summary>ロール敗北時</summary>
        OnRollLose,
        /// <summary>ロール引き分け時</summary>
        OnRollDraw,

        // === ダメージ計算 ===
        /// <summary>ダメージ計算前（自分が与える側）</summary>
        OnPreDealDamage,
        /// <summary>ダメージ計算前（自分が受ける側）</summary>
        OnPreReceiveDamage,
        /// <summary>ダメージ確定後（自分が与えた側）</summary>
        OnPostDealDamage,
        /// <summary>ダメージ確定後（自分が受けた側）</summary>
        OnPostReceiveDamage,

        // === 追撃/scratch ===
        /// <summary>追撃ダメージ計算前</summary>
        OnPrePursuitDamage,
        /// <summary>scratchダメージ計算前（勝利時のthreat削りダメージ）</summary>
        OnPreScratchDamage,

        // === 会心 ===
        /// <summary>会心判定時</summary>
        OnCriticalCheck,
        /// <summary>会心ダメージ計算時</summary>
        OnCriticalDamage,

        // === 状態異常 ===
        /// <summary>状態異常を付与する時</summary>
        OnApplyStatusEffect,
        /// <summary>状態異常を受ける時</summary>
        OnReceiveStatusEffect,

        // === 装備 ===
        /// <summary>装備した時（常駐効果の適用）</summary>
        OnEquip,
        /// <summary>装備を外した時</summary>
        OnUnequip,

        // === 相互攻撃モデル (ADR-0009) ===
        /// <summary>配線の確定直後・自攻撃の解決より前 (2026-10-28 追加)。
        /// 端子ごとの本数は accumulatedValues の "mutualAttackDiceCount" / "mutualBlockDiceCount" /
        /// "mutualChargeDiceCount" / "mutualSpecialDiceCount"、 ブロック量は blockSumThisTurn で読める。
        /// 攻撃値への加算 (AddPlayerAttackOrDiceBonus) はここでもまだ乗る。 **末尾に足すこと** ── 途中に挟むと序数がずれる。</summary>
        OnWired,

        // === キーワードの出来事 (2026-10-31 アイテム再編) ===
        //   Keywords (Kw) が量の変化を見て PassiveSkillManager.RaiseKeyword で配る。 量は Kw.EventAmount(ctx) で読む。
        //   1 つの出来事から続く連鎖の中では、 同じ品は 1 度しか働かない (GAME.md §11)。
        /// <summary>臨界が閾値に達して爆発が 1 回決まった (量 = その爆発で使った臨界)。</summary>
        OnRinkaiBurst,
        /// <summary>充電が実際に増えた (量 = 増えた分)。</summary>
        OnChargeGain,
        /// <summary>充電を使った・リロールを含む (量 = 使った分)。</summary>
        OnChargeSpend,
        /// <summary>過充電: 上限を超えて捨てられた (量 = 捨てられた分)。</summary>
        OnOvercharge,
        /// <summary>シールドが増えた (量 = 増えた分)。</summary>
        OnShieldGain,
        /// <summary>シールドが攻撃を吸収した (量 = 吸収した分)。</summary>
        OnShieldAbsorb,
        /// <summary>ターン開始の半減でシールドを失った (量 = 失った分)。</summary>
        OnShieldDecay,
        /// <summary>シールドが吸収で 0 になった (量 = 割られる前の量)。</summary>
        OnShieldBreak,
        /// <summary>敵の毒が増えた (量 = 増えた分)。</summary>
        OnPoisonGain,
        /// <summary>毒のダメージが入った (量 = そのダメージ)。</summary>
        OnPoisonTick,
        /// <summary>敵の毒が上限に達した。</summary>
        OnPoisonCap,
        /// <summary>出血が自然に減った (量 = 減った分)。</summary>
        OnBleedDecay,
        /// <summary>出血を品の効果で使った (量 = 使った分)。</summary>
        OnBleedConsume,
        /// <summary>敵の出血が 0 になった。</summary>
        OnBleedZero,
        /// <summary>超臨界爆発 (閾値を引く前の臨界が閾値の 2 倍以上・2026-11-20・§23-32)。 OnRinkaiBurst の直後に配る (量 = その爆発のダメージ)。</summary>
        OnSuperBurst,
        /// <summary>戦闘中に消耗品を使った (2026-11-20・item-overhaul-spec §2-2・量 = 消耗品の Tier・Tier の無い品 (戦術消耗品ほか) は 1)。 GameLoop.Consumables.Use が配る。</summary>
        OnConsumableUse,
        // ── 端子の山札の出来事 (2026-11-20・段 1-e・item-overhaul-spec §2-2)。 どれも Kw.Raise で配る (量 0 以下は鳴らない)。
        //   配る所は CombatManager.Terminals.cs の RaiseTerminalEvents (配線の解決 ApplyTerminalOutcome の直後・OnWired の前) と RaiseDeckShuffles。
        //   端子の出力を変える効果はこの出来事で書かない (Resolve の中で効かせる・BOT と自動で一致する)。 ここで書くのは端子の解決の外の効果だけ。
        /// <summary>使い捨ての端子を使った (発動した・この戦闘から外れる・量 = 1)。 1 枚につき 1 回。 戻る使い捨て (〈灰から組む機関〉〈緊急再装填〉) も使えば鳴る。</summary>
        OnTerminalDispose,
        /// <summary>端子の捨て札を切り直して山にした (量 = 切り直した枚数)。 ターンの頭に引く時のほか、 判断の間に引く消耗品・品で切り直した時も (OnWired の前にまとめて配る)。</summary>
        OnDeckShuffle,
        /// <summary>前のターンから残す印で残した端子を発動した (量 = 挿した出目の合計・最低 1)。 名前で数える (同じ名前の 2 枚目は区別しない)。</summary>
        OnKeptTerminalUse,
        /// <summary>補助の端子 (基本の〈時計〉を除く) を発動した (量 = 挿した出目の合計・最低 1)。 〈残響〉で 2 回働いても 1 回。</summary>
        OnSupportTerminalUse,
        // ── 役とボスの出来事 (2026-11-21・段 1-f・item-overhaul-spec §2-2)。 どれも Kw.Raise で配る。
        /// <summary>役が立った直後 (CombatManager.ResolveRoles・配線の解決の後・OnWired の前・量 = 強さ: 2 個そろい 1 / ゾロ目 2 / 連なり 2)。
        /// 立った役は <see cref="CombatContext.firedRolesThisTurn"/> の最後。 〈鏡合わせの盤〉の重ねた効果・〈宿命の刻み目〉のもう 1 回では鳴らない (役が立ったのではない)。
        /// ここで足す攻撃 +N (ctx.mutualAttackBonus) は撃ごとに乗る。</summary>
        OnRoleFired,
        /// <summary>ボスの目盛りを割ってダウンが決まった (量 = 割った目盛りの数)。 通常攻撃で割った (主攻撃・〈目盛り削りの鑿〉) か、 ひびの猶予が切れて割れたかは
        /// <see cref="Keywords.Kw.BossDownCrackKey"/> (1 = ひび) で分ける。 通常攻撃の割れは撃の輪の中、 ひびの割れはターンの終わりに鳴る。</summary>
        OnBossDown,
        /// <summary>敵の大技を取り消した (量 = 1): ダウンがチャージか大技のターンに重なった (ターンの頭・CombatManager.BeginBossDownTurnCore) か、
        /// 大技のターンに〈封じ・揃〉(端子の行動不能も) で敵が行動しなかった (敵の攻撃の解決)。</summary>
        OnHeavyCanceled,
    }
}

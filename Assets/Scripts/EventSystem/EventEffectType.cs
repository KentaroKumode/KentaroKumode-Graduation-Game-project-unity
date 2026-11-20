namespace EventSystem
{
    /// <summary>
    /// イベント結果の効果種別。テキストパーサが各エフェクト文字列をこのenumに分類する。
    /// </summary>
    public enum EventEffectType
    {
        None,                    // 「なし」
        HpDelta,                 // HP+N / HP-N
        HpFullHeal,              // HP全回復
        HpSetTo,                 // HPがNになる
        HpHalve,                 // 現在HPの半分を支払う (端数切上で残す・最低1)
        MaxHpDelta,              // 最大HP+N / 最大HP-N
        GoldDelta,               // ゴールド+N / ゴールド-N
        HungerDelta,             // 空腹度+N / 空腹度-N (旧表記、物資±N と等価)
        ProvisionDelta,          // 物資+N / 物資-N (今の単位: 1 歩 = 1。 支払いが足りなければ選べない・確率分岐の中はある分だけ失う・2026-11-13)
        MaterialDelta,           // 武器強化素材+N / 武器強化素材-N
        ArmorDurabilityLoss,     // 防具の耐久値減少
        TimedBuff,               // 時限バフ[xxx]を獲得
        TimedDebuff,             // 時限デバフ[xxx]を獲得
        PermanentDebuff,         // 永続デバフ[xxx]を獲得
        GainPassiveItem,         // パッシブアイテム獲得 / パッシブアイテム[xxx]を獲得
        GainConsumableItem,      // 消費アイテム獲得 / 消費アイテム[xxx]を獲得
        GainSpecificItem,        // アイテム[xxx]を獲得（カテゴリ未指定）
        GainFlag,                // フラグアイテム[xxx]を獲得
        DiscardFlag,             // フラグアイテム[xxx]を廃棄
        DiscardPassiveItem,      // パッシブアイテム[xxx]を廃棄
        EnterCombat,             // 戦闘に突入
        EnterEliteCombat,        // エリートとの戦闘に突入
        RandomEvent,             // ランダムなイベント発生
        Probability,             // 確率分岐の親（child のリストを持つ）
        CircusHandover,          // 「サーカス団を引き渡す」(契約解除 + Lv連動報酬)

        /// <summary>観測所の写しを持ち帰る。 **ラン跨ぎ**で残り、次の再訪 1 回で追加報酬になる。
        /// フラグアイテムでは表現できない (ownedFlags はラン内で消える)。</summary>
        ObservatoryTakeCopy,

        /// <summary>ターン+N: その場で N ターンを使う (2026-11-11・issue #17)。 追跡者・使い魔・天候の前線が N 回動く。
        /// **選んだ瞬間ではなく、 イベントを閉じて地図へ戻った時に進める** (<c>GameManager.ConfirmEventEncounter</c>)。
        /// 航行マップの無い層では何も起きない。 確率分岐の中・「勝利後」には置かない (読まれない)。</summary>
        TurnsPass,

        // ── 2026-11-12: 報酬を多様にする汎用の動詞 (docs/GAME.md §17「効果種別」)。 末尾に足す (既存の番号を動かさない) ──
        //   航行の効果 (Reveal* / *Pursuer / Shortcut / ShiftFront) は「ターン+N」と同じく、 イベントを閉じて地図へ戻った時に
        //   GameManager.ApplyEventNav が適用する。 航行マップの無い層では何も起きない。 確率分岐の中・「勝利後」には置かない (読まれない)。

        /// <summary>地図を明かす+N: 今いる点から道で N 歩以内の点の種類を明かす (戦闘名は出さない)。</summary>
        RevealMap,
        /// <summary>戦闘を見抜く+N: 道で N 歩以内の点を完全公開する (偵察と同じ・戦闘名まで)。</summary>
        RevealEncounters,
        /// <summary>補給の場所が分かる: この層の店と休憩の点を完全公開する。</summary>
        RevealSupplies,
        /// <summary>追跡者を撒く: 捕捉を外す (巡回へ戻り、 しばらく捕捉しない・使い魔は消える)。</summary>
        ShakeOffPursuer,
        /// <summary>追跡者を遠ざける+N: 追跡者の本体を道で N 歩遠ざける。</summary>
        PushPursuer,
        /// <summary>追跡者を呼ぶ+N (代償): 追跡者の本体が道で N 歩近づく (隣で止まる)。</summary>
        PullPursuer,
        /// <summary>近道+N: この後の移動 N 回ぶん、 物資を使わずターンも進まない (どこへ動くかは遊ぶ人が選ぶ・自動では動かない)。
        /// 待つ・偵察・〈補給線〉には効かない。 層を出たら残りは消える。 中身は <c>FreeMapSim.GrantFreeMoves</c>。</summary>
        Shortcut,
        /// <summary>前線を遠ざける+N / 前線を近づける+N: 天候の前線を N 段戻す / 進める。 amount は符号つき (近づける = 正)。</summary>
        ShiftFront,

        /// <summary>パッシブアイテムをNつから選ぶ: N 個を引いて 1 つ選ぶ (人は地図に戻った時の選択画面・BOT は GameHooks)。</summary>
        OfferPassives,
        /// <summary>パッシブを差し出して替える: 交換マスと同じ取り替え (いちばん段の低いパッシブ → 上の段のパッシブ)。 渡せる品が無ければ選べない。</summary>
        TradePassive,
        /// <summary>武器を鍛える: 休憩の「鍛える」と同じ強化を 1 段 (素材は通常どおり払う)。 鍛えられない・素材が足りないなら選べない。</summary>
        ForgeWeapon,
        /// <summary>次の店の値段-N%: 次に入る店の全価格に (1 − N/100) を掛ける (重ねたら掛け合わせ)。</summary>
        NextShopDiscount,
        /// <summary>次の休憩の回復+N%: 次に休憩で回復する時、 回復量に最大 HP の N% を足す (重ねたら足し合わせ)。</summary>
        NextRestBonus,

        /// <summary>止まらない時計を預かる (2026-11-13・〈さびれた観測所・再訪・写し〉の 5 つ目の選択肢だけ): 秘宝
        /// 〈止まらない時計〉(<see cref="GameLoop.ItemIds.VescaClock"/>・2026-11-13 にフラグから秘宝へ) を得る。 持ったまま七層のヴェスカの戦闘に入ると、
        /// 各段のダウンの目盛りが 1 つ増える (<see cref="GameLoop.VescaClock"/>)。 秘宝なので次の潜行には持ち越さない (中断セーブには残る)。
        /// 結果の行は「秘宝獲得: 止まらない時計」(GainPassiveItem と同じ書式)。</summary>
        VescaClockTake,

        /// <summary>真理を得る (2026-11-13・〈灰に埋もれた基地・再訪〉の「手記の最後の頁を開く」だけ): 〈決意〉を〈真理〉へ書き換え、 深さ 7 への資格
        /// (<c>layer7Unlocked</c>) を立てる (<see cref="GameLoop.ConvictionSystem.RevealTruthInLayer6"/>)。 選択肢の並びや名前でなく、 この効果で判定する。
        /// 〈真理〉はランをまたいで残らない。 結果の行は「〈決意〉が〈真理〉に変わった」。</summary>
        GainTruth,

        // ── 2026-11-20: 装備・偏重とつながる汎用の動詞 (docs/system-overhaul-spec.md §2-2・docs/GAME.md §17) ──

        /// <summary>賽ロール[N]:成功の効果|失敗の効果 ── 装備中の賽 (<c>GameLoop.EquippedDie</c>) を 1 回振り、 出目が N 以上なら成功。
        /// amount = 目標値・branches[0] = 成功・branches[1] = 失敗 (branchWeights は持たない)。 乱数は GameRng の専用のキー。
        /// 選んだ後の文・記録の文は確率分岐と同じく「||」で成功・失敗に分けられる (並びは 成功 → 失敗)。
        /// 確率分岐と同じく、 中に航行の効果・ターン・印〔見せる〕〔伏せる〕は置かない (読まれない・見せない)。</summary>
        DieRoll,
        /// <summary>条件[種別]:効果 ── 条件を満たす時だけ選べる選択肢の印 (それ自体は何もしない)。 結果の欄の頭にだけ置ける。
        /// param = 種別 (<see cref="EventEffectRoles.ReqAxis"/> / <see cref="EventEffectRoles.ReqWeapon"/> / <see cref="EventEffectRoles.ReqProvision"/>)・
        /// arg = 軸の名前 / 武器の家系 (短剣・剣・盾・斧)・amount = N。 満たさない時は選べない (理由を出す・全部が選べない時の安全弁でも開かない)。</summary>
        Requirement,
        /// <summary>賽を提示して交換 ── 賽を 2 種引いて面を見せ、 今の賽と入れ替えるか「やめる」を選ばせる (強制の没収・見えない交換は入れない)。
        /// 人は地図へ戻った時の選択画面、 BOT は GameHooks.ChooseOfferedDie (無ければ替えない)。</summary>
        OfferDieSwap,
        /// <summary>軸の秘宝をNつから選ぶ ── 今の偏重が一番高い軸 (同数なら AxisRules.Ranked の順) の秘宝から N 個を引いて 1 つ選ぶ。
        /// 選び方・不安定化 (+3) は「パッシブアイテムをNつから選ぶ」と同じ。</summary>
        OfferAxisPassives,
    }
}

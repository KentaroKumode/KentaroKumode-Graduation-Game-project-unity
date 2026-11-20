namespace MetaProgression
{
    /// <summary>
    /// 整備パネル (2026-11-20 刷新・docs/GAME.md §15-1・system-overhaul-spec §1) の<b>数値の正本</b>。
    /// 二階建て: <b>基礎整備</b> (トークンで買い切る 6 段・安全網) ＋ <b>作戦の枠</b> 3 つ (主方針 / 補助 / 種火・モジュールを 1 つずつ挿す・ラン前に無料で組み替え)。
    /// 状態は <see cref="MetaProgressState"/>、 購入の口は <see cref="MetaProgressManager"/>、 効果は <see cref="MetaBuffApplicator"/> が getter で配る。
    /// 画面の説明文は Game2DMetaPanel。 <b>数値はすべて仮</b> (最後の 3 段階の測定で合わせる)。
    /// <para>旧: 24 点の配点式 (n 個目の点 = 8n トークン・数値 8 本 ×10 段 + 宣言 7 本 ×3 段) は 2026-11-20 に撤去 (§24)。
    /// 旧い段の名前 (<see cref="MetaPanelKind"/>) はセーブの返金と測定の旧い指定の読み替えにだけ残す。</para>
    /// </summary>
    public static class MetaPanel
    {
        // ============================================================
        //  ラン本体の開始値のうち、 整備パネルの getter が配るもの (§1-2 A)
        // ============================================================

        /// <summary>店の特売枠の素の数 (整備パネルと無関係・ラン本体の値)。 2026-11-20 に 0 → 1。
        /// 特売の抽選は ShopManager にあるが ShopManager は数を <see cref="MetaBuffApplicator.GetSaleItemCount"/> からしか読まないので、 素の値もここに置く。</summary>
        public const int BaseSaleSlots = 1;

        // ============================================================
        //  基礎整備 (6 段・買い切り・§1-2 B)
        // ============================================================

        public const int BaseTierCount = 6;

        /// <summary>n 段目 (1 起点) を買うトークン (仮)。 1〜2 周で 1 段目に届く程度。</summary>
        public static readonly int[] BaseTierCost = { 40, 60, 120, 160, 240, 320 };

        /// <summary>1 段目と 4 段目の最大 HP。</summary>
        public const int BaseTier1Hp = 6, BaseTier4Hp = 4;
        /// <summary>2 段目の防御。</summary>
        public const int BaseTier2Defense = 2;
        /// <summary>3 段目の開幕ゴールド (特売枠 +1 と一緒)。</summary>
        public const int BaseTier3Gold = 10;
        /// <summary>5 段目の与ダメ % (与ダメ % の池に足す)。</summary>
        public const int BaseTier5DamagePct = 10;

        /// <summary>段 <paramref name="tier"/> まで買った時の最大 HP の上乗せ。</summary>
        public static int BaseHp(int tier) => (tier >= 1 ? BaseTier1Hp : 0) + (tier >= 4 ? BaseTier4Hp : 0);
        public static int BaseDefense(int tier) => tier >= 2 ? BaseTier2Defense : 0;
        public static int BaseGold(int tier) => tier >= 3 ? BaseTier3Gold : 0;
        /// <summary>特売枠の上乗せ (3 段目と 6 段目で +1 ずつ)。</summary>
        public static int BaseSaleSlotsBonus(int tier) => (tier >= 3 ? 1 : 0) + (tier >= 6 ? 1 : 0);
        public static int BaseDamagePct(int tier) => tier >= 5 ? BaseTier5DamagePct : 0;

        // ============================================================
        //  作戦の枠 (3 つ・§1-2 C)
        // ============================================================

        /// <summary>n 枠目 (1 起点) を開くトークン (仮)。 開く順は <see cref="MetaSlot"/> の並び (主方針 → 補助 → 種火)。</summary>
        public static readonly int[] SlotOpenCost = { 50, 150, 300 };

        /// <summary>モジュールを解禁するトークン (仮)。 解禁は買い切り、 挿すのは無料。</summary>
        public static int ModuleCost(MetaModule m) => SlotOf(m) == MetaSlot.Main ? 120 : 80;

        /// <summary>モジュールが挿さる枠。 <b>新しい種火は <see cref="SparkKeyword"/> と一緒にここへ足す。</b></summary>
        public static MetaSlot SlotOf(MetaModule m) => m switch
        {
            MetaModule.SparkCharge or MetaModule.SparkRinkai or MetaModule.SparkPoison or MetaModule.SparkBleed
                or MetaModule.SparkShield or MetaModule.SparkCrit or MetaModule.SparkDeck or MetaModule.SparkRole => MetaSlot.Spark,
            MetaModule.AttackTune or MetaModule.BlockTune or MetaModule.AxisTicket
                or MetaModule.TacticKnife or MetaModule.Compression => MetaSlot.Support,
            _ => MetaSlot.Main,
        };

        /// <summary>種火のモジュールが宣言する軸のキーワード (<see cref="SparkStarterPicker"/> の表と店の出現の重みが読む)。 種火でなければ null。
        /// 新しい軸 (山札・役) の種火を足す時: <see cref="MetaModule"/> の末尾 → ここ → <see cref="SlotOf"/> → SparkStarterPicker の表 → 固有効果の getter → 画面の説明文。</summary>
        public static string SparkKeyword(MetaModule m) => m switch
        {
            MetaModule.SparkCharge => "charge",
            MetaModule.SparkRinkai => "rinkai",
            MetaModule.SparkPoison => "poison",
            MetaModule.SparkBleed  => "bleed",
            MetaModule.SparkShield => "shield",
            MetaModule.SparkCrit   => "crit",
            MetaModule.SparkDeck   => "deck",
            MetaModule.SparkRole   => "role",
            _ => null,
        };

        // ---- 主方針 (旧 r10 の極点 8 つ・数値階段なしでそのまま) ----

        /// <summary>〈戦意〉: 戦闘に勝つたびの与ダメ % (端数は累計に対して切り捨て・<see cref="MetaBuffApplicator.GetBattleSpiritPct"/>)。</summary>
        public const float BattleSpiritPctPerWin = 1.5f;

        /// <summary>〈剛胆〉: 戦闘マスがエリートへ格上げされる確率 (%) / エリート戦の報酬ゴールド割増 (%) / エリートへの与ダメ (ボスには乗らない)。</summary>
        public const float ValorEliteUpgradePct = 25f, ValorEliteGoldPct = 50f;
        public const int ValorEliteSlayerPct = 10;
        /// <summary>[較正専用] 〈剛胆〉のエリートへの与ダメを止める。 既定 true = 有効 (AutoRunner.valorSlayerOff)。</summary>
        public static bool SlayerEnabled = true;
        /// <summary>〈剛胆〉: 勝った戦闘の残りシールドのうち、 次の戦闘へ持ち越す割合 (切り上げ・適用は CombatManager.Finish)。</summary>
        public const float GuardShieldCarryRatio = 0.30f;

        /// <summary>〈ショップ強盗〉: 通常の敵を倒した時の追加の秘宝ドロップ (%・ボス戦は対象外)。</summary>
        public const float RobberyPassiveDropPct = 10f;

        /// <summary>〈99% 特売〉: 特売枠の上乗せ。</summary>
        public const int Sale99SlotBonus = 2;

        /// <summary>特売の割引率の段と重み。 既定 15/30/50% (42:38:20) / 〈99% 特売〉で 99% が加わる (42:35:15:8)。</summary>
        public static int[] SaleTiers(bool sale99) => sale99 ? new[] { 15, 30, 50, 99 } : new[] { 15, 30, 50 };
        public static int[] SaleWeights(bool sale99) => sale99 ? new[] { 42, 35, 15, 8 } : new[] { 42, 38, 20 };

        /// <summary>〈開幕厳選〉: 開幕の秘宝の本数と、 <paramref name="itemIndex"/> 本目 (0 起点) の提示数 (1 本目 2 択・2 本目 3 択)。</summary>
        public const int StartPickCount = 2;
        public static int StartPickOffers(int itemIndex) => UnityEngine.Mathf.Clamp(itemIndex + 2, 2, 3);

        /// <summary>〈不動〉: 1 回の攻撃で受けるダメージは最大 HP のこの割合まで ＋ 被ダメージ −N (防御の後に引く・下限 1)。</summary>
        public const float ArmorHitCapPct = 0.35f;
        public const int ImmovableFlatReduction = 2;
        public static int ArmorHitCap(int maxHp) => maxHp > 0 ? UnityEngine.Mathf.Max(1, UnityEngine.Mathf.CeilToInt(maxHp * ArmorHitCapPct)) : 0;

        // ---- 種火 (軸の宣言・旧 r1 + r2 相当。 旧 r3 の開幕の品は外した) ----

        /// <summary>対応する軸の秘宝の、 店の秘宝の枠での出現の重み。</summary>
        public const float SparkAppearanceWeightMul = 2.0f;
        /// <summary>充電: 充電の上限と戦闘開始時の充電。</summary>
        public const int ChargeCapBonus = 10, ChargeOpening = 10;
        /// <summary>臨界: 戦闘の終わりのメーターを次の戦闘へ持ち越す割合。</summary>
        public const float RinkaiCarryoverRatio = 0.5f;
        /// <summary>毒: 1 ターンに最初の 1 回だけ、 毒の付与に足すスタック (CombatContext.AddStatus)。</summary>
        public const int PoisonApplyBonus = 5;
        /// <summary>出血: 出血がこれ以上の敵の攻撃値 −<see cref="BleedWeakenAmount"/>。</summary>
        public const int BleedWeakenThreshold = 3, BleedWeakenAmount = 4;
        /// <summary>シールド: ターン開始の減衰で残るシールド +N ポイント。</summary>
        public const int ShieldKeepBonusPct = 10;
        /// <summary>会心 (再設計): 会心倍率 +N。</summary>
        public const float CritMulBonus = 0.5f;
        /// <summary>山札 (采配・2026-11-21): 戦闘の 1 ターン目に多く引く端子の枚数 (TerminalBattleState.extraDrawNext に足す)。</summary>
        public const int DeckOpeningDraw = 1;
        /// <summary>役 (賽目・2026-11-21): 各戦闘で無料になる振り直しの回数 (戦闘の最初から数える・<c>CombatManager.FreeRerollsNow</c>)。</summary>
        public const int RoleFreeRerolls = 1;

        // ---- 補助 (戦術パーツ) ----

        /// <summary>攻撃 / 防御の端子調律: その端子に挿した出目 1 個につき +N。</summary>
        public const int TerminalTunePerDie = 1;
        /// <summary>〈軸引きの手形〉: 店の軸指定リロールの追加から引く額。</summary>
        public const int AxisTicketDiscount = 5;
    }

    /// <summary>作戦の枠。 <b>並びが開く順</b> (1 枠目 = 主方針・2 枠目 = 補助・3 枠目 = 種火)。 セーブの equippedModules の添え字なので並べ替えない。</summary>
    public enum MetaSlot { Main, Support, Spark }

    /// <summary>作戦のモジュール。 <b>セーブは int で持つので並べ替えも削除もしない</b> (足すのは末尾)。 枠は <see cref="MetaPanel.SlotOf"/>。</summary>
    public enum MetaModule
    {
        // === 主方針 (旧 r10 の極点) ===
        LastStand,     // 〈ラストスタンド〉 その場蘇生 (ボス戦でも 1 度だけ・最大 HP 半減)
        BattleSpirit,  // 〈戦意〉
        Valor,         // 〈剛胆〉 エリート格上げ・報酬・エリートへの与ダメ・シールド持ち越し
        VaultBank,     // 〈貸金庫〉 層突破で所持金 1.5 倍 (200G まで)・宝箱ゴールド
        Robbery,       // 〈ショップ強盗〉 値下げ交渉・通常の敵の追加ドロップ
        Sale99,        // 〈99% 特売〉
        StartPick,     // 〈開幕厳選〉
        Immovable,     // 〈不動〉

        // === 種火 ===
        SparkCharge, SparkRinkai, SparkPoison, SparkBleed, SparkShield, SparkCrit,

        // === 補助 ===
        AttackTune,    // 攻撃端子調律
        BlockTune,     // 防御端子調律
        AxisTicket,    // 〈軸引きの手形〉
        TacticKnife,   // 〈戦術の懐刀〉
        Compression,   // 〈圧縮の心得〉

        // === 種火 (2026-11-21 に足した 2 軸) ===
        SparkDeck,     // 種火・山札 (采配): 戦闘の 1 ターン目の手札 +1 枚
        SparkRole,     // 種火・役 (賽目): 各戦闘の最初の振り直しが無料
    }
}

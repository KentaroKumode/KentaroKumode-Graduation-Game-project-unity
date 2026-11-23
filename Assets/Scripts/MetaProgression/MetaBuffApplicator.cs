using UnityEngine;
using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// バフ効果を各システムに適用するための中央ヘルパー。
    ///
    /// 2026-11-20 (整備パネル刷新・docs/GAME.md §15-1): getter の形は旧い配点式の時のまま、 中身を
    /// 「効いている基礎整備の段 (<see cref="Tier"/>) ＋ 開いた枠に挿しているモジュール (<see cref="Has"/>)」へ差し替えた。
    /// 戦闘・店・地図の側は今までどおり getter を読むだけ。 数値の正本は <see cref="MetaPanel"/>。
    /// </summary>
    public static class MetaBuffApplicator
    {
        private static MetaProgressState S => MetaProgressManager.Instance?.State;

        /// <summary>整備盤の効いているパネルの数値の合計 (2026-11-23・<see cref="MetaBoard"/>)。 メタ進行が無ければ全部 0。</summary>
        private static BoardStats B => S?.BoardEffects ?? Empty;
        private static readonly BoardStats Empty = new BoardStats();

        /// <summary>そのモジュール (主方針・補助・種火の効果) を持つパネルが盤で効いているか。</summary>
        private static bool Has(MetaModule m) => S?.BoardHas(m) ?? false;

        /// <summary>今の整備盤の構成 (ログ・測定の実効状態)。</summary>
        public static string Describe() => S?.DescribeBoard() ?? "(メタ進行なし)";

        // ============================================================
        //  RunState 初期化時の補正
        // ============================================================

        /// <summary>RunState.Initialize 直後に呼んで開幕値を底上げする。</summary>
        public static void ApplyToRunStart(RunState run, int baseHP)
        {
            if (run == null) return;
            var s = S;
            if (s == null) return;

            int hpBonus  = B.hp;
            // 遺物 (§15-5) 最大HP+N。 ラン開始時に 1 回だけ。 刻印の毎ターン条件は
            // この時点でまだ判定材料が無いので成立扱い (RelicApplicator の規約)。
            int relicHp  = Relics.RelicApplicator.GetMaxHpBonus(run);
            int hp       = baseHP + hpBonus + relicHp;
            run.playerMaxHP = hp;
            run.playerHP    = hp;

            int startGold = B.gold;
            GoldIncome.GainExempt(run, startGold, "整備盤");

            // 開幕の秘宝 (〈開幕厳選〉)・戦術消耗品 (〈戦術の懐刀〉) の付与は GameManager 側 (アイテムDB と重複排除が要るため)。

            Debug.Log($"[MetaBuff] 開幕補正: HP {baseHP}→{hp} (遺物 +{relicHp}), "
                    + $"Gold +{startGold}, 開幕の秘宝 {GetStartingPassiveCount()} 本 / {Describe()}");
            if (Relics.RelicApplicator.DescribeEquipped() != "遺物なし")
                Debug.Log($"[遺物] 持ち込み: {Relics.RelicApplicator.DescribeEquipped()}");
        }

        /// <summary>遺物の自動獲得を止めるフラグ。 **AutoRunner のバッチ中は必ず true にする。**
        ///
        /// バッチは 1 万ラン規模で回るので、 毎ラン AddRelic + PlayerPrefs.Save() が走ると
        /// (1) セーブが数千個の遺物で膨れ、 (2) ディスク I/O でバッチが極端に遅くなる。
        /// 周回モード (AscensionLoop) は自前で RelicRoller を呼ぶので、 自動獲得は不要。</summary>
        public static bool SuppressRelicGrant;

        /// <summary>直近のランの終わりに入手した遺物 (結果画面が見せる)。 次のランの開始まで残る。</summary>
        public static readonly System.Collections.Generic.List<Relics.Relic> LastRunRelics = new System.Collections.Generic.List<Relics.Relic>();

        /// <summary>ラン終了時に遺物を獲得する (§15-5・2026-11-10 再編)。 クリア・死亡いずれでも呼ぶ。
        /// ordinal は見せる層 (1〜5)。 落ちる数は 3 層以下 1 / 4 層 2 / 5 層到達 3。 難易度は挑戦カードの目盛り (8 で頭打ち)。</summary>
        public static void GrantRelicOnRunEnd(int ordinal, bool cleared)
        {
            LastRunRelics.Clear();
            if (SuppressRelicGrant) return;
            var s = S;
            if (s == null) return;
            int diff = s.challengeDifficulty;
            var got = Relics.RelicRoller.RollRunEnd(GameLoop.GameRng.RunIndex, ordinal, diff, s.relicTargetSet);
            foreach (var relic in got)
            {
                if (relic == null || !relic.IsValid()) { Debug.LogWarning($"[遺物] 生成に失敗 (層{ordinal} 難易度{diff})"); continue; }
                s.AddRelic(relic);
                LastRunRelics.Add(relic);
                Achievements.AchievementService.NoteRelicAcquired(relic);
                Debug.Log($"[遺物] 獲得: {relic.Describe()}  (層{ordinal}{(cleared ? "クリア" : "")} / 難易度{diff} / 所持{s.ownedRelics.Count}個)");
            }
            MetaProgressManager.Instance?.Save();
        }

        // ============================================================
        //  戦闘関連
        // ============================================================

        // [削除 2026-09-13] GetDamageReduction ── 0 を返すだけの残骸。 防御の段効果は
        //   GetGuardDamageReductionPct が正本。

        /// <summary>ボス撃破時の追加ゴールド。 旧〈強奪〉の段効果で、 今の整備パネルには無い (常に 0・呼ぶ側の形を保つ)。</summary>
        public static int GetBossGoldBonus() => 0;

        /// <summary>〈ショップ強盗〉: 通常エネミー撃破時の追加の秘宝ドロップ確率 (%)。 <b>ボス戦は対象外</b>。</summary>
        public static float GetPlunderPassiveDropPct()
            => Has(MetaModule.Robbery) ? MetaPanel.RobberyPassiveDropPct : 0f;

        // [削除 2026-11-12] GetDiceTotalBonus ── v6 で廃止した出目の補正。 常に 0 を返し、 読む所は AutoRunner の実効状態の表示だけだった。

        // ============================================================
        //  物資ゲージ減少の軽減 (ADR-0002)
        // ============================================================

        /// <summary>兵站 r10〈補給線〉: この層で一度着いた前哨基地・店のあいだを物資 1 で跳べる。 兵站は 2026-11-10 に枠から外した (常に false・呼ぶ側の形を保つ)。</summary>
        public static bool IsSupplyLineUnlocked() => false;

        /// <summary>整備盤の防御 +N。 戦闘開始時に防御の土台へ足す。</summary>
        public static int GetArmorDefense() => B.defense;

        /// <summary>〈不動〉: 被ダメージ −N (防御の後に引く)。</summary>
        public static int GetArmorFlatReduction() => Has(MetaModule.Immovable) ? MetaPanel.ImmovableFlatReduction : 0;

        /// <summary>〈不動〉: 1 回の攻撃で受けるダメージの上限 (0 = 上限なし)。</summary>
        public static int GetArmorHitCap(int maxHp) => Has(MetaModule.Immovable) ? MetaPanel.ArmorHitCap(maxHp) : 0;

        /// <summary>物資上限の上乗せ。 兵站は 2026-11-10 に枠から外した (常に 0・呼ぶ側の形を保つ)。</summary>
        public static int GetProvisionCapBonus() => 0;

        // ============================================================
        //  ショップ
        // ============================================================

        /// <summary>店の特売枠の数 = 素の <see cref="MetaPanel.BaseSaleSlots"/> + 整備盤のパネル + 〈99% 特売〉。</summary>
        public static int GetSaleItemCount()
            => MetaPanel.BaseSaleSlots + B.saleSlots + (Has(MetaModule.Sale99) ? MetaPanel.Sale99SlotBonus : 0);

        /// <summary>特売の割引率を重み付きで 1 つ引く (2026-09-12)。
        /// 段は <see cref="MetaPanel.SaleTiers"/> (既定 {15,30,50} / 〈99% 特売〉で 99 が加わる)。
        /// <paramref name="key"/>/<paramref name="salt"/> は GameRng のキー (決定性のため必須)。</summary>
        public static int RollSaleDiscountPct(string key, int salt)
        {
            bool sale99 = Has(MetaModule.Sale99);
            var tiers = MetaPanel.SaleTiers(sale99);
            var w = MetaPanel.SaleWeights(sale99);
            int total = 0;
            for (int i = 0; i < w.Length; i++) total += w[i];
            if (total <= 0) return tiers[tiers.Length - 1];
            int r = GameLoop.GameRng.Range(0, total, key, salt);
            for (int i = 0; i < w.Length; i++)
            {
                r -= w[i];
                if (r < 0) return tiers[i];
            }
            return tiers[tiers.Length - 1];
        }

        // [廃止 2026-09-12] ボス追加報酬 (旧 強奪 r10)。
        //   単体で r10−r9 = +7.02pt と目標帯 (±3pt) の倍以上あり、
        //   強奪 r10 をショップ強盗へ差し替えるにあたって二階建てを避けるため撤去した。

        // ============================================================
        //  各種 unlock
        // ============================================================

        /// <summary>〈開幕厳選〉: 開幕で獲得する秘宝の本数。</summary>
        public static int GetStartingPassiveCount()
            => Has(MetaModule.StartPick) ? MetaPanel.StartPickCount : 0;

        // [削除 2026-11-12] GetFloorClearHeal ── v6 で廃止したフロアクリア時の追加回復。 常に 0 を返していたので呼び出し (GameManager.Floor の HandleFloorClear) ごと外した。

        /// <summary>出力 r10: オーバーロード (1戦闘1度・与ダメ2倍・反動あり) が使えるか。</summary>
        // [撤去 2026-09-13] IsOverloadUnlocked ── 出力 r10 は〈戦意〉へ差し替え。
        //   経緯は MetaPanel.BattleSpiritUnlocked。

        /// <summary>〈剛胆〉: 戦闘終了時の残りシールドを次戦へ持ち越す。</summary>
        public static bool IsShieldCarryUnlocked() => Has(MetaModule.Valor);

        /// <summary>開幕の秘宝のレア度を上位へ寄せる度合い (0..1)。 旧〈宝物庫〉の段効果で、 今の整備パネルには無い (常に 0・呼ぶ側の形を保つ)。</summary>
        public static float GetStartingPassiveRarityBias() => 0f;

        /// <summary>〈開幕厳選〉: 開幕の秘宝 <paramref name="itemIndex"/> 本目 (0 起点) の提示数 (1 本目 2 択 / 2 本目 3 択)。</summary>
        public static int GetStartingPassiveOffers(int itemIndex)
            => Has(MetaModule.StartPick) ? MetaPanel.StartPickOffers(itemIndex) : 1;

        /// <summary>〈貸金庫〉: 層を突破するたびに所持金が増える (<see cref="VaultBank"/>)。</summary>
        public static bool IsVaultBankUnlocked() => Has(MetaModule.VaultBank);

        /// <summary>〈貸金庫〉: 宝箱マスでゴールドも獲得。</summary>
        public static bool IsTreasureChestGoldUnlocked() => Has(MetaModule.VaultBank);

        /// <summary>〈ショップ強盗〉: 値下げ交渉 (強盗) を解禁。</summary>
        public static bool IsShopRobberyUnlocked() => Has(MetaModule.Robbery);

        /// <summary>会心倍率 (メタ由来は常に固定値)。
        /// 2026-07-26 に 2.0→3.0 へ上げたが、 **2026-08-03 に 2.0 へ戻した。**
        ///   理由: 与ダメ倍率の段別実測で、 会心段が単独 ×2.18 (会心率36% × 実測倍率3.33) と
        ///   全修飾チェーン中で最大の増幅源になっていた。 さらに遺物の会心倍率軸が
        ///   「基準値の N%」定義なので基準3.0だと段6が +2.7 と突出し、 12軸の実効差が 4.2 倍に開いていた。
        ///   基準を 2.0 に戻すと会心段は ×1.75 に落ち、 遺物軸の格差も同時に縮む。
        /// **2026-11-22 final5: 2.0 → 1.6** (超過会心率の変換 CritCore.ExcessPerRate 1 → 2 と組): 他の軸が会心率を少し摘まむ得を減らし、 100% を超えて積む会心の軸の得を増やす。
        ///   1.5 にしないのは〈回帰性真理〉(倍率 −0.5) を受けても ×1.10 を残すため。
        /// **2026-11-22 final6: 1.6 → 2.0** (会心の軸が死に軸になったので final4 までの値へ戻す・作者の決定・〈回帰性真理〉下で ×1.50)。 超過の変換 (CritCore.ExcessPerRate = 2) は据え置き。</summary>
        public static float GetCriticalMultiplier() => 2.0f;

        // ============================================================
        //  与ダメ% (出力トラック 0..50)
        // ============================================================

        /// <summary>与ダメ % (池に足す): 整備盤のパネル。 (挑戦カード〈与ダメ +N%〉は 2026-11-22 に撤去)</summary>
        public static int GetOutgoingDamagePct() => B.damagePct;

        /// <summary>〈戦意〉が挿さっているか。</summary>
        public static bool IsBattleSpiritUnlocked() => Has(MetaModule.BattleSpirit);

        /// <summary>〈戦意〉による与ダメ% (勝利数 × <see cref="MetaPanel.BattleSpiritPctPerWin"/>)。
        /// 未解禁なら 0。 <b>累計に対して切り捨てる</b> ── 勝利ごとに丸めると 1.5 が 2 に戻る。</summary>
        public static int GetBattleSpiritPct(GameLoop.RunState run)
            => (run != null && IsBattleSpiritUnlocked())
                ? Mathf.FloorToInt(run.battleSpiritWins * MetaPanel.BattleSpiritPctPerWin) : 0;

        // ============================================================
        //  【v6 廃止】互換 stub
        // ============================================================

        /// <summary>〈ラストスタンド〉が挿さっているか。</summary>
        public static bool IsLastStandUnlocked() => Has(MetaModule.LastStand);

        /// <summary>会心倍率への加算 (種火・会心)。</summary>
        public static float GetCritDamageBonus() => Has(MetaModule.SparkCrit) ? MetaPanel.CritMulBonus : 0f;

        /// <summary>種火・シールド: ターン開始の減衰で残るシールドへの上乗せ (% ポイント)。</summary>
        public static int GetShieldKeepBonusPct() => Has(MetaModule.SparkShield) ? MetaPanel.ShieldKeepBonusPct : 0;

        // ============================================================
        //  v6 新規: 防御-シールド / 精密-分母 / 端子調律 2 系 / 種火 4 系
        // ============================================================

        /// <summary>【2026-08-15 廃止】防御 r1〜r10 の開幕シールド (0..30)。 常に 0。
        ///
        /// <para><b>定額シールドが難易度曲線を反転させていた。</b> 戦闘ごとに無条件で満タン配給され、
        /// 道中の被ダメ (1戦 9.9% ≒ 9.2 HP) を丸ごと飲み込む一方、 ヴェスカ戦の 45.2 HP には
        /// 焼け石だった ── **危険でない戦闘だけを消し、 危険な戦闘には効かない**。</para>
        ///
        /// <para>実測 (2026-08-15 / 各 1000 ラン): メタ 0 とメタ全開の差は 1 ラン 920 シールド、
        /// 34.9 戦で割ると 26.4/戦 ＝ ほぼ r10 の 30。 **測定されたシールドの大半がこれ**で、
        /// 役や配線で稼いだものではなかった。 飛来ダメージの吸収率はメタ 0 の 41% に対し
        /// メタ全開で 77%、 HP 被ダメは 18.4%/戦 → 9.9%/戦 と半減していた。</para>
        ///
        /// <para>副作用として HP が資源でなくなり、 回復スポット到着時の HP が 90% 以上のケースが
        /// 71.9%、 うち満タンが 35.8% ＝ **回復地点が機能しない**状態を作っていた。
        /// 戦闘数に比例して総軽減が増える (踏むほど無償で硬くなる) 点も構造的に逆。</para></summary>
        // [削除 2026-09-13] GetOpeningShield ── 0 を返すだけの残骸。 呼び出し側の合算からも外した。

        // ---- 〈剛胆〉リスク軸 (enum キーは互換のため Guard のまま) ----

        /// <summary>戦闘マスがエリートへ格上げされる確率 (%)。</summary>
        /// <summary>[較正専用] エリート格上げ率の上書き。 負 = 触らない (メタ配点どおり)。
        ///
        /// <para><b>「エリートは踏むほど得なのか」を方策と切り離して測るための穴</b> (2026-09-14)。
        /// 剛胆トラックはエリート率 + 報酬 + ドロップ + 極点を同時に動かすので、
        /// トラックを振っても<b>エリートそのものの損得が分離できない</b>。
        /// ここだけを振れば、 メタ配点を Balanced に固定したまま
        /// 「マップ上のエリートが増えるとクリア率がどう動くか」が純粋に出る。</para>
        ///
        /// <para>書き換えるのは <c>AutoRunner.eliteUpgradePctOverride</c> だけ。
        /// 実効値は <c>[実効状態]</c> に印字される。</para></summary>
        public static float EliteUpgradePctOverride = -1f;

        public static float GetEliteUpgradePct()
            => EliteUpgradePctOverride >= 0f
             ? EliteUpgradePctOverride
             : Has(MetaModule.Valor) ? MetaPanel.ValorEliteUpgradePct : 0f;

        /// <summary>〈剛胆〉: エリート戦の報酬ゴールド割増 (%)。</summary>
        public static float GetEliteGoldPct() => Has(MetaModule.Valor) ? MetaPanel.ValorEliteGoldPct : 0f;

        /// <summary>エリート撃破時の追加の秘宝ドロップ率 (%)。 旧〈剛胆〉の段効果で、 今の〈剛胆〉には無い (常に 0・呼ぶ側の形を保つ)。</summary>
        public static float GetElitePassiveDropPct() => 0f;

        /// <summary>〈剛胆〉: エリートへの与ダメ割増 (%・ボスには乗らない)。</summary>
        public static int GetEliteSlayerPct()
            => Has(MetaModule.Valor) && MetaPanel.SlayerEnabled ? MetaPanel.ValorEliteSlayerPct : 0;

        /// <summary>会心率の加算 (%)。 挑戦カード〈急所読み〉の会心率 +N% だけ (小数で足す)。
        /// (旧: 精密トラックの段あたり会心率 MetaPanel.PrecisionCritRatePct を足していたが、 2026-09-10 の撤去で常に 0f だった。
        ///  0f + x は x と同じ値なので 2026-11-12 に外した)</summary>
        public static float GetCritRatePctBonus()
            => (ChallengeCards.Value(ChallengeCards.Crit) + B.critPct) / 100f;      // + 整備盤〈精密較正ピン〉(2026-11-23)

        /// <summary>攻撃端子調律: 攻撃の端子に挿した出目 1 個あたりの加算。 呼び出し側で個数を掛けること。 (挑戦カードの端子の札は 2026-11-22 に撤去)</summary>
        public static int GetAttackTerminalPerDice() => Has(MetaModule.AttackTune) ? MetaPanel.TerminalTunePerDie : 0;

        /// <summary>防御端子調律: 防御の端子に挿した出目 1 個あたりの加算。 呼び出し側で個数を掛けること。</summary>
        public static int GetBlockTerminalPerDice() => Has(MetaModule.BlockTune) ? MetaPanel.TerminalTunePerDie : 0;

        /// <summary>〈軸引きの手形〉: 店の軸指定リロールの追加から引く額。
        /// <b>未接続</b> ── 追加の額は ShopManager.AxisRerollSurchargeFor が決めるが、 ShopManager は並行作業 (8 軸化) の区域なので Stage D では触っていない。
        /// 接続は AxisRerollSurchargeFor の式から <c>MetaBuffApplicator.GetAxisRerollDiscount()</c> を引く 1 か所 (下限 0)。</summary>
        public static int GetAxisRerollDiscount()
            => (Has(MetaModule.AxisTicket) ? MetaPanel.AxisTicketDiscount : 0) + (int)ChallengeCards.Value(ChallengeCards.Regular);   // 挑戦カード〈顔なじみ〉(III は 999 = 無料・下限 0 は呼び手)

        /// <summary>〈戦術の懐刀〉: ラン開始時にランダムな戦術消耗品を 1 個持つ (付与は GameManager のラン開始)。</summary>
        public static bool HasTacticKnife() => Has(MetaModule.TacticKnife);

        /// <summary>〈圧縮の心得〉: 店での 1 回目の端子の削除が無料 (GameManager.TerminalRemovePrice)。</summary>
        public static bool IsFirstShopRemovalFree() => Has(MetaModule.Compression);

        // === 種火 (軸の宣言・枠に 1 つ) ===

        /// <summary>挿している種火の軸 (items.json の axis・"充電" など)。 ランの始めに出現の枠 (RunState.lockedAxes) の 1 つ目へ入る。 種火が無ければ null。</summary>
        public static string GetSparkAxis()
        {
            var m = S?.BoardSpark();
            return m.HasValue ? SparkStarterPicker.AxisOf(MetaPanel.SparkKeyword(m.Value)) : null;
        }

        /// <summary>種火・充電: 充電上限 +N。 CombatManager.ChargeMax + この値。</summary>
        public static int GetChargeCapBonus() => Has(MetaModule.SparkCharge) ? MetaPanel.ChargeCapBonus : 0;

        /// <summary>種火・充電 + 挑戦カード〈予備電源〉: 戦闘開始時に得る充電。</summary>
        public static int GetChargeOpening()
            => (Has(MetaModule.SparkCharge) ? MetaPanel.ChargeOpening : 0) + B.chargeOpening + (int)ChallengeCards.Value(ChallengeCards.Charge);

        /// <summary>種火・臨界: 戦闘終了時にメーターの何割を次戦闘へ持ち越すか (0 or 0.5)。</summary>
        public static float GetRinkaiCarryoverRatio() => Has(MetaModule.SparkRinkai) ? MetaPanel.RinkaiCarryoverRatio : 0f;

        /// <summary>種火・毒: 1 ターンに最初の 1 回だけ、 毒の付与に足すスタック。</summary>
        public static int GetPoisonApplyBonus() => Has(MetaModule.SparkPoison) ? MetaPanel.PoisonApplyBonus : 0;

        /// <summary>種火・山札 (2026-11-21): 戦闘の 1 ターン目に多く引く端子の枚数 (CombatManager.BeginTerminalDeck)。</summary>
        public static int GetSparkDeckOpeningDraw() => (Has(MetaModule.SparkDeck) ? MetaPanel.DeckOpeningDraw : 0) + B.handTurn1;   // + 整備盤のパネル (予備スロット端子など)

        /// <summary>種火・役 (2026-11-21): 各戦闘で、 最初から数えて無料になる振り直しの回数 (CombatManager.FreeRerollsLeft)。</summary>
        public static int GetSparkRoleFreeRerolls()
            => (Has(MetaModule.SparkRole) ? MetaPanel.RoleFreeRerolls : 0) + B.freeRerolls + (int)ChallengeCards.Value(ChallengeCards.Role);   // 挑戦カード〈手馴染み〉も同じ数え方 (各戦闘の最初から)

        // === 整備盤のパネルだけの効果 (2026-11-23・meta_board_panels.json の stats) ===

        /// <summary>〈緊急冷却ピン〉: 各戦闘の 1 ターン目のブロック +N (CombatManager.FinalBlockSum)。</summary>
        public static int GetOpeningBlock() => B.blockTurn1;

        /// <summary>〈端数回収回路〉〈商隊ネットワーク〉: 秘宝の見送り換金 +N% (MetaDebuffApplicator.ApplySkipGold が挑戦カードの後に掛ける)。</summary>
        public static int GetSkipCashPct() => B.skipCashPct;

        /// <summary>〈試運転ブースター〉: 0 層〈支給の秘宝〉の提示数の上乗せ。</summary>
        public static int GetFarmOfferBonus() => B.farmOffer;

        /// <summary>〈補給ユニット〉: 休憩の回復量 +N% (回復量 × (1 + N/100))。</summary>
        public static int GetRestHealPct() => B.restHealPct;

        /// <summary>〈補給ユニット〉〈探査レーダー〉: 前哨基地の物資の補給 +N% (切り上げ)。</summary>
        public static int GetOutpostSupplyPct() => B.outpostSupplyPct;

        /// <summary>〈探査レーダー〉: 地図の視界 +N 歩。</summary>
        public static int GetVisionBonus() => B.vision;

        /// <summary>〈熟練の整備書〉: 店の武器の強化素材の値段 −N% (切り上げ)。</summary>
        public static int GetMaterialCutPct() => B.materialCutPct;

        /// <summary>〈先制解析ユニット〉: 各層の最初の通常戦の数と、 その戦闘の敵の攻撃の削り (%)。</summary>
        public static int GetOpenerFights() => B.openerFights;
        public static int GetOpenerAtkCutPct() => B.openerAtkCutPct;

        /// <summary>〈均衡モジュール〉: 出現の枠が 4 つ埋まった瞬間 (AxisPool.NoteAcquired)、 最大 HP +N・HP 全回復。</summary>
        public static void OnAxisLockCompleted(RunState run)
        {
            int hp = MetaProgressManager.Existing?.State?.BoardEffects?.lockHp ?? 0;   // 管理を作らない (AxisPool のテストは EditMode で通る)
            if (run == null || hp <= 0) return;
            run.playerMaxHP += hp;
            run.playerHP = run.playerMaxHP;
            Debug.Log($"[MetaBuff] 均衡モジュール: 出現の枠が埋まった → 最大 HP +{hp}・全回復 ({run.playerHP}/{run.playerMaxHP})");
        }

        /// <summary>種火・出血: 敵の出血が閾値以上で敵攻撃値 −N。 CombatManager 予告後・敵攻撃前で減算。</summary>
        public static int GetBleedWeakenAttack(int enemyBleedStacks)
            => Has(MetaModule.SparkBleed) && enemyBleedStacks >= MetaPanel.BleedWeakenThreshold ? MetaPanel.BleedWeakenAmount : 0;

    }
}

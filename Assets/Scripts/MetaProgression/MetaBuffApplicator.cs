using UnityEngine;
using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// バフ効果を各システムに適用するための中央ヘルパー。
    ///
    /// 2026-07-25 v6 (整備パネル移行):
    ///   ・全 getter は MetaProgressState.panelRanks 経由で MetaPanel.*(rank) を返す。
    ///   ・旧 State フィールド (hpBonus/goldBonus 等) は撤廃。 現在値は都度計算。
    ///   ・削除された機能: FloorClearHeal / LastStandHpLossDisable / BossRestHealAndUpgrade /
    ///     CritDamageBonus / BossExtraRare (Rare 昇格) / DiceTotalBonus (ダイス合計概念自体が消滅)。
    ///   ・追加された getter: GuardShield / ProvisionCapBonus / CritDenominatorReduce / 種火 4 系 / 端子 2 系。
    /// </summary>
    public static class MetaBuffApplicator
    {
        private static MetaProgressState S => MetaProgressManager.Instance?.State;

        private static int Rank(MetaPanelKind k) => S?.GetRank(k) ?? 0;

        // ============================================================
        //  RunState 初期化時の補正
        // ============================================================

        /// <summary>RunState.Initialize 直後に呼んで開幕値を底上げする。</summary>
        public static void ApplyToRunStart(RunState run, int baseHP)
        {
            if (run == null) return;
            var s = S;
            if (s == null) return;

            int hpBonus  = MetaPanel.ShellHp(Rank(MetaPanelKind.Shell));
            // 遺物 (§15-5) 最大HP+N。 ラン開始時に 1 回だけ。 刻印の毎ターン条件は
            // この時点でまだ判定材料が無いので成立扱い (RelicApplicator の規約)。
            int relicHp  = Relics.RelicApplicator.GetMaxHpBonus(run);
            int hp       = baseHP + hpBonus + relicHp;
            run.playerMaxHP = hp;
            run.playerHP    = hp;

            int startGold = UnityEngine.Mathf.Max(0, MetaPanel.VaultGold(Rank(MetaPanelKind.Vault)));
            GoldIncome.GainExempt(run, startGold, "整備パネル(金庫)", applyLastStandFilter: false);

            // 2026-09-10: 宝物庫は素材ではなく開幕パッシブを配る (MetaPanel.SupplyStartingPassives)。
            //   実際の付与は GameManager 側 (アイテムDB と重複排除が要るため)。
            int material = MetaPanel.SupplyStartMaterial(Rank(MetaPanelKind.Supply));   // 常に 0
            run.weaponMaterials += material;

            Debug.Log($"[MetaBuff] 開幕補正: HP {baseHP}→{hp} (遺物 +{relicHp}), "
                    + $"Gold +{startGold}, 開幕パッシブ {GetStartingPassiveCount()} 本");
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

        /// <summary>強奪: ボス撃破時の追加ゴールド (0..10)。 <b>ボスマス限定</b>。</summary>
        public static int GetBossGoldBonus()
            => MetaPanel.PlunderBossGold(Rank(MetaPanelKind.Plunder));

        /// <summary>強奪: 通常エネミー撃破時の追加パッシブドロップ確率 (%)。 <b>ボス戦は対象外</b>。</summary>
        public static float GetPlunderPassiveDropPct()
            => MetaPanel.PlunderPassiveDropPct(Rank(MetaPanelKind.Plunder));

        /// <summary>【v6 廃止】ダイス合計値補正。 相互攻撃モデルで「ダイス合計」概念自体が消滅したため常に 0。
        /// 互換のため残置 (CombatManager 側の呼び出しはブランチガード込みで維持)。</summary>
        public static int GetDiceTotalBonus() => 0;

        // ============================================================
        //  物資ゲージ減少の軽減 (ADR-0002)
        // ============================================================

        /// <summary>戦闘後の物資ゲージ減少の軽減量。 <b>2026-09-12 以降 常に 0</b>
        /// (兵站の効果を物資上限へ移したため)。 呼び出し側の互換のため残置。</summary>
        public static int GetProvisionLossReduction()
            => MetaPanel.LanternProvisionLossReduce(Rank(MetaPanelKind.Lantern));

        /// <summary>兵站 r10〈補給線〉: この層で一度着いた前哨基地・店のあいだを物資 1 で跳べる (ターンを使わない・2026-11-09)。</summary>
        public static bool IsSupplyLineUnlocked() => Rank(MetaPanelKind.Lantern) >= 10;

        /// <summary>装甲 (2026-11-10): 防御 +1/段 (0..10)。 戦闘開始時に防御の土台へ足す。</summary>
        public static int GetArmorDefense() => MetaPanel.ArmorDefense(Rank(MetaPanelKind.Armor));

        /// <summary>装甲: 被ダメージ −N (防御の後に引く)。</summary>
        public static int GetArmorFlatReduction() => MetaPanel.ArmorFlatReduction(Rank(MetaPanelKind.Armor));

        /// <summary>装甲 r10〈不動〉: 1 回の攻撃で受けるダメージの上限 (0 = 上限なし)。</summary>
        public static int GetArmorHitCap(int maxHp) => MetaPanel.ArmorHitCap(Rank(MetaPanelKind.Armor), maxHp);

        /// <summary>装甲: 開幕シールド (段 × 刻み)。</summary>
        public static int GetArmorOpeningShield() => MetaPanel.ArmorOpeningShield(Rank(MetaPanelKind.Armor));

        /// <summary>装甲 r10〈不動〉: 失った HP 10% ごとに防御 +1 (最大 +9)。 r10 未満は 0。</summary>
        public static int GetArmorImmovableDefense(int hp, int maxHp) => MetaPanel.ArmorImmovable(Rank(MetaPanelKind.Armor), hp, maxHp);

        /// <summary>兵站: 物資上限 +1/段 (0..10)。 2026-11-10 に枠から外した (常に 0)。</summary>
        public static int GetProvisionCapBonus()
            => MetaPanel.LanternProvisionCapBonus(Rank(MetaPanelKind.Lantern));

        // ============================================================
        //  ショップ
        // ============================================================

        /// <summary>特売品出現数 (0..3, 商才 r3/6/9)。</summary>
        public static int GetSaleItemCount()
            => MetaPanel.TradeSaleSlots(Rank(MetaPanelKind.Trade));

        /// <summary>特売割引率の最小値 (%)。 表示用 (段テーブルの先頭)。</summary>
        public static int GetSaleDiscountMinPct()
            => MetaPanel.TradeSaleTiers(Rank(MetaPanelKind.Trade))[0];

        /// <summary>特売割引率の最大値 (%)。 表示用 (段テーブルの末尾。 商才 r10 で 99)。</summary>
        public static int GetSaleDiscountMaxPct()
        {
            var t = MetaPanel.TradeSaleTiers(Rank(MetaPanelKind.Trade));
            return t[t.Length - 1];
        }

        /// <summary>特売の割引率を重み付きで 1 つ引く (2026-09-12)。
        /// 段は <see cref="MetaPanel.TradeSaleTiers"/> (既定 {15,30,50} / r10 で 99 が加わる)。
        /// <paramref name="key"/>/<paramref name="salt"/> は GameRng のキー (決定性のため必須)。</summary>
        public static int RollSaleDiscountPct(string key, int salt)
        {
            int r10 = Rank(MetaPanelKind.Trade);
            var tiers = MetaPanel.TradeSaleTiers(r10);
            var w = MetaPanel.TradeSaleWeights(r10);
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

        /// <summary>互換用: 旧 RollRefund は no-op (特売機構へ移行済)。</summary>
        public static int RollRefund(int paidAmount, RunState run) => 0;

        // [廃止 2026-09-12] ボス追加報酬 (旧 強奪 r10)。
        //   単体で r10−r9 = +7.02pt と目標帯 (±3pt) の倍以上あり、
        //   強奪 r10 をショップ強盗へ差し替えるにあたって二階建てを避けるため撤去した。

        // ============================================================
        //  各種 unlock
        // ============================================================

        /// <summary>宝物庫: 開幕で獲得するパッシブスティックの本数 (r2/5/8/10 で +1 ずつ)。</summary>
        public static int GetStartingPassiveCount()
            => MetaPanel.SupplyStartingPassives(Rank(MetaPanelKind.Supply));

        /// <summary>【v6 廃止】フロアクリア時の追加回復。 常に 0。</summary>
        public static int GetFloorClearHeal() => 0;

        /// <summary>出力 r10: オーバーロード (1戦闘1度・与ダメ2倍・反動あり) が使えるか。</summary>
        // [撤去 2026-09-13] IsOverloadUnlocked ── 出力 r10 は〈戦意〉へ差し替え。
        //   経緯は MetaPanel.BattleSpiritUnlocked。

        /// <summary>防御 r10: 戦闘終了時の残りシールドを次戦へ持ち越す。</summary>
        public static bool IsShieldCarryUnlocked()
            => MetaPanel.GuardShieldCarryUnlocked(Rank(MetaPanelKind.Guard));

        /// <summary>宝物庫 r3/6/9: 開幕パッシブのレア度を上位へ寄せる度合い (0..1)。</summary>
        public static float GetStartingPassiveRarityBias()
            => MetaPanel.SupplyRarityBias(Rank(MetaPanelKind.Supply));

        /// <summary>宝物庫 r10: 開幕パッシブ <paramref name="itemIndex"/> 本目 (0 起点) の提示数。
        /// 1 本目 1 択 / 2 本目 2 択 / 3 本目 3 択。</summary>
        public static int GetStartingPassiveOffers(int itemIndex)
            => MetaPanel.SupplyStartingOffers(Rank(MetaPanelKind.Supply), itemIndex);

        /// <summary>金庫 r10: 貸金庫 (ランを跨ぐ預金) を使えるか。</summary>
        public static bool IsVaultBankUnlocked()
            => MetaPanel.VaultBankUnlocked(Rank(MetaPanelKind.Vault));

        /// <summary>金庫 r10: 宝箱マスでゴールドも獲得。</summary>
        public static bool IsTreasureChestGoldUnlocked()
            => MetaPanel.VaultTreasureUnlocked(Rank(MetaPanelKind.Vault));

        /// <summary>強奪 r10: ショップ強盗解禁 (2026-09-12 に商才から移設)。</summary>
        public static bool IsShopRobberyUnlocked()
            => MetaPanel.PlunderShopRobberyUnlocked(Rank(MetaPanelKind.Plunder));

        /// <summary>会心倍率 (メタ由来は常に固定値)。
        /// 2026-07-26 に 2.0→3.0 へ上げたが、 **2026-08-03 に 2.0 へ戻した。**
        ///   理由: 与ダメ倍率の段別実測で、 会心段が単独 ×2.18 (会心率36% × 実測倍率3.33) と
        ///   全修飾チェーン中で最大の増幅源になっていた。 さらに遺物の会心倍率軸が
        ///   「基準値の N%」定義なので基準3.0だと段6が +2.7 と突出し、 12軸の実効差が 4.2 倍に開いていた。
        ///   基準を 2.0 に戻すと会心段は ×1.75 に落ち、 遺物軸の格差も同時に縮む。</summary>
        public static float GetCriticalMultiplier() => 2.0f;

        // ============================================================
        //  与ダメ% (出力トラック 0..50)
        // ============================================================

        public static int GetOutgoingDamagePct()
            => MetaPanel.OutputPct(Rank(MetaPanelKind.Output)) + (int)ChallengeCards.Value(ChallengeCards.Output);   // 挑戦カード: 与ダメ +N%

        /// <summary>出力 r10 (極点) 〈戦意〉が解禁されているか。</summary>
        public static bool IsBattleSpiritUnlocked()
            => MetaPanel.BattleSpiritUnlocked(Rank(MetaPanelKind.Output));

        /// <summary>〈戦意〉による与ダメ% (勝利数 × <see cref="MetaPanel.BattleSpiritPctPerWin"/>)。
        /// 未解禁なら 0。 <b>累計に対して切り捨てる</b> ── 勝利ごとに丸めると 1.5 が 2 に戻る。</summary>
        public static int GetBattleSpiritPct(GameLoop.RunState run)
            => (run != null && IsBattleSpiritUnlocked())
                ? Mathf.FloorToInt(run.battleSpiritWins * MetaPanel.BattleSpiritPctPerWin) : 0;

        // ============================================================
        //  【v6 廃止】互換 stub
        // ============================================================

        /// <summary>ラストスタンドが解禁されているか (メタ〈外殻〉r10 の特典・2026-08-04)。</summary>
        public static bool IsLastStandUnlocked()
            => MetaPanel.ShellLastStandUnlocked(Rank(MetaPanelKind.Shell));

        /// <summary>【v6 廃止】会心ダメージ加算。 常に 0 (メタからは付与しない)。</summary>
        /// <summary>会心倍率への加算。 2026-11-07〜 種火・会心 r2 (+0.2)。</summary>
        public static float GetCritDamageBonus() => MetaPanel.CritMulBonus(Rank(MetaPanelKind.SparkCrit));

        /// <summary>種火・シールド r2: ターン開始の減衰で残るシールドへの上乗せ (% ポイント)。</summary>
        public static int GetShieldKeepBonusPct() => MetaPanel.ShieldKeepBonusPct(Rank(MetaPanelKind.SparkShield));

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
             : MetaPanel.ValorEliteUpgradePct(Rank(MetaPanelKind.Guard));

        /// <summary>エリート戦の報酬ゴールド割増 (%)。</summary>
        public static float GetEliteGoldPct()
            => MetaPanel.ValorEliteGoldPct(Rank(MetaPanelKind.Guard));

        /// <summary>エリート撃破時の追加パッシブドロップ率 (%)。</summary>
        public static float GetElitePassiveDropPct()
            => MetaPanel.ValorElitePassiveDropPct(Rank(MetaPanelKind.Guard));

        /// <summary>剛胆 r10: 精鋭・ボスへの与ダメ割増 (%)。 未解禁なら 0。</summary>
        public static int GetEliteSlayerPct()
            => MetaPanel.ValorEliteSlayerUnlocked(Rank(MetaPanelKind.Guard))
                ? MetaPanel.ValorEliteSlayerPct : 0;

        /// <summary>【2026-07-28 廃止】精密の段あたり会心率加算。 常に 0。
        /// 較正段規約 (r1/2/4/5/7/8 は効果なし) を破っていたため撤去。 詳細は MetaPanel 側。</summary>
        public static float GetCritRatePctBonus()
            => MetaPanel.PrecisionCritRatePct(Rank(MetaPanelKind.Precision)) + ChallengeCards.Value(ChallengeCards.Crit) / 100f;   // 挑戦カード: 会心率 +N% (小数で足す)

        /// <summary>攻撃端子 配線合計への加算 (0..3)。</summary>
        /// <summary>攻撃端子: 接続ダイス 1 本あたりの加算 (0..3)。 呼び出し側で本数を掛けること。</summary>
        public static int GetAttackTerminalPerDice()
            => MetaPanel.AttackTerminalPerDice(Rank(MetaPanelKind.AttackTerm)) + (int)ChallengeCards.Value(ChallengeCards.TermAtk);

        /// <summary>防御端子 配線合計への加算 (0..3)。</summary>
        /// <summary>防御端子: 接続ダイス 1 本あたりの加算 (0..3)。 呼び出し側で本数を掛けること。</summary>
        public static int GetBlockTerminalPerDice()
            => MetaPanel.BlockTerminalPerDice(Rank(MetaPanelKind.BlockTerm)) + (int)ChallengeCards.Value(ChallengeCards.TermBlk);

        /// <summary>挑戦カード〈猫の手〉: 毎ターン、 猫が 1 組を先に挿す確率 (0..1)。 札を受けていなければ 0。 読むのは CombatManager.ChooseCatPaw。</summary>
        public static float GetCatPawChance()
            => System.Math.Max(0f, System.Math.Min(1f, ChallengeCards.Value(ChallengeCards.Cat) / 100f));

        /// <summary>挑戦カード〈金で殴る〉(#6) を受けているか (払う金額を決める判断が要るか)。 効果の量は <see cref="ChallengeCards.GoldPunchAttack"/>。</summary>
        public static bool HasGoldPunch()
            => ChallengeCards.Value(ChallengeCards.GoldPunch) > 0f;

        // === 種火共通 (r1 出現重み・r3 開幕所持) ===

        /// <summary>キーワード → 対応する種火 MetaPanelKind。</summary>
        public static MetaPanelKind SparkKindFor(string keyword)
        {
            switch (keyword)
            {
                case "charge":  return MetaPanelKind.SparkCharge;
                case "rinkai":  return MetaPanelKind.SparkRinkai;
                case "poison":  return MetaPanelKind.SparkPoison;
                case "bleed":   return MetaPanelKind.SparkBleed;
                case "crit":    return MetaPanelKind.SparkCrit;
                case "shield":  return MetaPanelKind.SparkShield;
                default:        return MetaPanelKind.SparkCharge; // fallback
            }
        }

        /// <summary>種火 r1 到達で対応キーワードアイテムの抽選重み倍率 (1.0 or 1.6)。</summary>
        public static float GetSparkAppearanceWeightMul(string keyword)
            => MetaPanel.SparkAppearanceWeightMul(Rank(SparkKindFor(keyword)));

        /// <summary>種火 r3: 対応キーワードのアイテム (LEGENDARY 以上を除く) を開幕所持するか。</summary>
        public static bool IsSparkStartingItemUnlocked(string keyword)
            => MetaPanel.SparkStartingItemUnlocked(Rank(SparkKindFor(keyword)));

        // === 種火固有 (r2) ===

        /// <summary>充電 r2: 充電上限 +N (0 or 5)。 CombatManager.ChargeMax + この値。</summary>
        public static int GetChargeCapBonus()
            => MetaPanel.ChargeCapBonus(Rank(MetaPanelKind.SparkCharge)) + (int)ChallengeCards.Value(ChallengeCards.Charge);

        /// <summary>充電 r2: 戦闘開始時に得る充電 (0 or 10・2026-11-07)。</summary>
        public static int GetChargeOpening()
            => MetaPanel.ChargeOpening(Rank(MetaPanelKind.SparkCharge)) + ChallengeCards.ChargeOpening(ChallengeCards.Held(ChallengeCards.Charge)?.tier ?? 0);

        /// <summary>臨界 r2: 戦闘終了時にメーターの何割を次戦闘へ持ち越すか (0 or 0.5)。</summary>
        public static float GetRinkaiCarryoverRatio()
            => MetaPanel.RinkaiCarryoverRatio(Rank(MetaPanelKind.SparkRinkai));

        /// <summary>毒 r2: 毒付与時の追加スタック +N (0 or 1)。</summary>
        public static int GetPoisonApplyBonus()
            => MetaPanel.PoisonApplyBonus(Rank(MetaPanelKind.SparkPoison));

        /// <summary>出血 r2: 敵の出血 3 以上で敵攻撃値 -N (0 or 2)。 CombatManager 予告後・敵攻撃前で減算。</summary>
        public static int GetBleedWeakenAttack(int enemyBleedStacks)
            => MetaPanel.BleedWeakenAttack(Rank(MetaPanelKind.SparkBleed), enemyBleedStacks)
               + System.Math.Min((int)ChallengeCards.Value(ChallengeCards.Bleed), enemyBleedStacks / 2);   // 挑戦カード: 出血 2 につき −1 (最大はカードの値)

        /// <summary>挑戦カード: 毎ターン足す臨界のメーター。</summary>
        public static int GetHeatPerTurn() => (int)ChallengeCards.Value(ChallengeCards.Heat);
        /// <summary>挑戦カード: 戦闘に勝つたびの追加ゴールド。</summary>
        public static int GetWinGoldBonus() => (int)ChallengeCards.Value(ChallengeCards.WinGold);
        /// <summary>挑戦カード: 毒の半減で減る量を減らす割合 (0..1)。</summary>
        public static float GetPoisonDecayCut() => ChallengeCards.Value(ChallengeCards.Poison) / 100f;
        /// <summary>挑戦カード: シールドの減衰で減る量を減らす割合 (0..1)。</summary>
        public static float GetShieldDecayCut() => ChallengeCards.Value(ChallengeCards.Shield) / 100f;
    }
}

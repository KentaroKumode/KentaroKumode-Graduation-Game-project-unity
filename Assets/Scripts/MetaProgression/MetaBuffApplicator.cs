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
    ///   ・追加された getter: GuardShield / HopeCapBonus / CritDenominatorReduce / 種火 4 系 / 端子 2 系。
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
            // 挑戦デバフ〈脆弱な肉体〉×0.90 / ×0.85 / ×0.80 を **最後に**掛ける。
            //   **ここで掛けないと効かない** ── このメソッドは RunState.Initialize が入れた
            //   playerMaxHP を丸ごと上書きするので、 Initialize 側だけに書くと消える
            //   (v1 の定数減算 −5/−15/−30 はこれで死んでいた・2026-08-04 修正)。
            //   整備パネル・遺物のボーナスを足した後の値に掛けるので、
            //   メタを伸ばすほどデバフが薄まることもない。
            float fragile = MetaDebuffApplicator.GetMaxHpMultiplier();
            if (fragile < 1f) hp = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(hp * fragile));
            run.playerMaxHP = hp;
            run.playerHP    = hp;

            // 開幕所持金は〈搾取経済〉の対象外 (T4-D〈破産〉の管轄)。
            // T4-D〈破産〉: 開幕 GOLD から一定額を差し引く (下限 0)。
            //   2026-08-11: 「0 にする」から差し引き方式へ。
            int startGold = UnityEngine.Mathf.Max(0,
                MetaPanel.VaultGold(Rank(MetaPanelKind.Vault))
                - MetaDebuffApplicator.GetStartingGoldPenalty());
            GoldIncome.GainExempt(run, startGold, "整備パネル(金庫)", applyLastStandFilter: false);

            // 2026-09-10: 兵站は素材ではなく開幕パッシブを配る (MetaPanel.SupplyStartingPassives)。
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

        /// <summary>ラン終了時に遺物を 1 個獲得する (§15-5)。 クリア・死亡いずれでも呼ぶ。
        /// floor は到達した最深層、 cleared は「その層のボスを倒したか」。</summary>
        public static void GrantRelicOnRunEnd(int floor, bool cleared)
        {
            if (SuppressRelicGrant) return;
            var s = S;
            if (s == null) return;
            int score = s.ChallengeScore;
            // rngIndex は同一シード内で衝突しないようにラン番号を使う。
            int idx = GameLoop.GameRng.RunIndex;
            var relic = Relics.RelicRoller.Roll(floor, cleared, score, idx);
            if (relic == null || !relic.IsValid())
            {
                Debug.LogWarning($"[遺物] 生成に失敗 (層{floor} クリア{cleared} 挑戦{score})");
                return;
            }
            s.AddRelic(relic);
            Achievements.AchievementService.NoteRelicAcquired(relic);
            MetaProgressManager.Instance?.Save();
            Debug.Log($"[遺物] 獲得: {relic.Describe()}  "
                    + $"(層{floor}{(cleared ? "クリア" : "で死亡")} / 挑戦{score} / 所持{s.relics.Count}個)");
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

        /// <summary>会心ダイスへの追加補正値 (0/1/2/3, 精密トラック r3/6/9)。</summary>
        public static int GetCritBonus()
            => MetaPanel.PrecisionCritBonus(Rank(MetaPanelKind.Precision));

        /// <summary>【v6 廃止】ダイス合計値補正。 相互攻撃モデルで「ダイス合計」概念自体が消滅したため常に 0。
        /// 互換のため残置 (CombatManager 側の呼び出しはブランチガード込みで維持)。</summary>
        public static int GetDiceTotalBonus() => 0;

        // ============================================================
        //  希望ゲージ減少の軽減 (ADR-0002)
        // ============================================================

        /// <summary>戦闘後の希望ゲージ減少の軽減量。 <b>2026-09-12 以降 常に 0</b>
        /// (燈火の効果を希望上限へ移したため)。 呼び出し側の互換のため残置。</summary>
        public static int GetHopeLossReduction()
            => MetaPanel.LanternHopeLossReduce(Rank(MetaPanelKind.Lantern));

        /// <summary>燈火: 希望上限 +10/段 (0..100)。</summary>
        public static int GetHopeCapBonus()
            => MetaPanel.LanternHopeCapBonus(Rank(MetaPanelKind.Lantern));

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
        /// <summary>互換用: 旧 GetRefundChance は 0 を返す (特売機構へ移行済)。</summary>
        public static float GetRefundChance() => 0f;

        // [廃止 2026-09-12] ボス追加報酬 (旧 強奪 r10)。
        //   単体で r10−r9 = +7.02pt と目標帯 (±3pt) の倍以上あり、
        //   強奪 r10 をショップ強盗へ差し替えるにあたって二階建てを避けるため撤去した。

        // ============================================================
        //  各種 unlock
        // ============================================================

        /// <summary>兵站: 開幕で獲得するパッシブスティックの本数 (r2/5/8/10 で +1 ずつ)。</summary>
        public static int GetStartingPassiveCount()
            => MetaPanel.SupplyStartingPassives(Rank(MetaPanelKind.Supply));

        /// <summary>互換: 開幕パッシブが 1 本でも付くか。</summary>
        public static bool IsStartingPassiveItemUnlocked() => GetStartingPassiveCount() > 0;

        /// <summary>【v6 廃止】フロアクリア時の追加回復。 常に 0。</summary>
        public static int GetFloorClearHeal() => 0;

        /// <summary>出力 r10: オーバーロード (1戦闘1度・与ダメ2倍・反動あり) が使えるか。</summary>
        // [撤去 2026-09-13] IsOverloadUnlocked ── 出力 r10 は〈戦意〉へ差し替え。
        //   経緯は MetaPanel.BattleSpiritUnlocked。

        /// <summary>防御 r10: 戦闘終了時の残りシールドを次戦へ持ち越す。</summary>
        public static bool IsShieldCarryUnlocked()
            => MetaPanel.GuardShieldCarryUnlocked(Rank(MetaPanelKind.Guard));

        /// <summary>兵站 r3/6/9: 開幕パッシブのレア度を上位へ寄せる度合い (0..1)。</summary>
        public static float GetStartingPassiveRarityBias()
            => MetaPanel.SupplyRarityBias(Rank(MetaPanelKind.Supply));

        /// <summary>兵站 r10: 開幕パッシブ <paramref name="itemIndex"/> 本目 (0 起点) の提示数。
        /// 1 本目 1 択 / 2 本目 2 択 / 3 本目 3 択。</summary>
        public static int GetStartingPassiveOffers(int itemIndex)
            => MetaPanel.SupplyStartingOffers(Rank(MetaPanelKind.Supply), itemIndex);

        /// <summary>燈火 r10: ゴールド不足分を希望で 1:1 で払えるか。</summary>
        /// <summary>燈火: 希望で支払える下限 (段効果)。 未解禁なら int.MaxValue。</summary>
        public static int GetHopeSpendFloor()
            => MetaPanel.LanternHopeSpendFloor(Rank(MetaPanelKind.Lantern));

        public static bool IsHopePaymentUnlocked()
            => MetaPanel.LanternHopePaymentUnlocked(Rank(MetaPanelKind.Lantern));

        /// <summary>横移動 1 回あたりの希望消費。 現状は常に既定値
        /// (2026-09-13 に燈火 r10 の無税化を廃し、 希望払いへ差し替えた)。</summary>
        public static int GetLateralHopeCost() => GameLoop.HopeSystem.LateralCost;

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
            => MetaPanel.OutputPct(Rank(MetaPanelKind.Output));

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

        /// <summary>【廃止】ラストスタンド HP 半減無効化。 2026-08-04 に半減そのものを廃止したため常に true 相当。
        /// 呼び出し箇所を壊さないための残置。</summary>
        public static bool IsLastStandHpLossDisabled() => true;

        /// <summary>ラストスタンドが解禁されているか (メタ〈外殻〉r10 の特典・2026-08-04)。</summary>
        public static bool IsLastStandUnlocked()
            => MetaPanel.ShellLastStandUnlocked(Rank(MetaPanelKind.Shell));

        /// <summary>【v6 廃止】ボス前休憩の回復+強化同時。 常に false。</summary>
        public static bool IsBossRestHealAndUpgradeUnlocked() => false;

        /// <summary>【v6 廃止】会心ダメージ加算。 常に 0 (メタからは付与しない)。</summary>
        public static float GetCritDamageBonus() => 0f;

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

        /// <summary>防御 r1〜r10: 被ダメージ **割合軽減** (1 段につき <b>4%</b>、 r10 で 40%)。
        /// 開幕シールドの跡地（2026-08-15）。
        ///
        /// <para><b>定額ではなく割合にするのが要点。</b> 廃止した開幕シールドは定額 30 で、
        /// 道中の 9.2 HP を丸ごと消す一方ヴェスカの 45.2 HP には効かない ──
        /// **危険でない戦闘だけを無効化する**形だった。割合軽減は強敵にも弱敵にも同じ比率で効くので、
        /// 難易度曲線を反転させない。戦闘数に比例して総軽減が積み上がることも無い。</para>
        ///
        /// <para><b>遺物側と合算しない。</b> 適用は CombatManager の**独立した乗算段**
        /// （遺物の割合軽減を掛けた後に、もう一段 ×(1−N)）。加算にすると実効値が遺物の引き次第で変わり、
        /// 共有上限 0.85 に近いほどメタ分が食われて**整備パネルへ振ったポイントが捨て点になりうる**
        /// ── 破産の設計で決めた「割り振ったポイントがいかなる場合でも完全に捨て点にならない」に反する。
        /// 乗算なら逓減はするが 0 にはならない。</para>
        ///
        /// <para>なお <c>GetDamageReductionPct</c> の <c>0.85</c> は**到達する上限ではなく事故防止のガード**。
        /// 遺物の同軸は段10 でも 30%、理論値遺物のサブ 5 段なら 15% にしかならない。</para></summary>
        /// <para><b>【2026-09-13 廃止】常に 0。</b> 旧〈防御〉はリスク軸〈剛胆〉へ
        /// 完全リワークした (Valor 系)。</para>
        public static float GetGuardDamageReductionPct() => 0f;

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
            => MetaPanel.PrecisionCritRatePct(Rank(MetaPanelKind.Precision));

        /// <summary>精密 r10: 逓減で捨てられた会心率を会心倍率へ変換した値 (0 なら極点未到達)。
        /// **天井 (注意散漫) 適用後の実効率を渡すこと。**</summary>
        public static float GetPrecisionOverflowMultiplier(float critAddTotal, float effectiveRate)
            => MetaPanel.PrecisionOverflowMultiplier(
                   Rank(MetaPanelKind.Precision), critAddTotal, effectiveRate);

        /// <summary>【2026-07-26 廃止】会心分母縮小。 分子/分母モデル自体が消滅したため常に 0。</summary>
        public static int GetCritDenominatorReduce() => 0;

        /// <summary>攻撃端子 配線合計への加算 (0..3)。</summary>
        /// <summary>攻撃端子: 接続ダイス 1 本あたりの加算 (0..3)。 呼び出し側で本数を掛けること。</summary>
        public static int GetAttackTerminalPerDice()
            => MetaPanel.AttackTerminalPerDice(Rank(MetaPanelKind.AttackTerm));

        /// <summary>防御端子 配線合計への加算 (0..3)。</summary>
        /// <summary>防御端子: 接続ダイス 1 本あたりの加算 (0..3)。 呼び出し側で本数を掛けること。</summary>
        public static int GetBlockTerminalPerDice()
            => MetaPanel.BlockTerminalPerDice(Rank(MetaPanelKind.BlockTerm));

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
            => MetaPanel.ChargeCapBonus(Rank(MetaPanelKind.SparkCharge));

        /// <summary>臨界 r2: 戦闘終了時にメーターの何割を次戦闘へ持ち越すか (0 or 0.5)。</summary>
        public static float GetRinkaiCarryoverRatio()
            => MetaPanel.RinkaiCarryoverRatio(Rank(MetaPanelKind.SparkRinkai));

        /// <summary>毒 r2: 毒付与時の追加スタック +N (0 or 1)。</summary>
        public static int GetPoisonApplyBonus()
            => MetaPanel.PoisonApplyBonus(Rank(MetaPanelKind.SparkPoison));

        /// <summary>出血 r2: 敵の出血 3 以上で敵攻撃値 -N (0 or 2)。 CombatManager 予告後・敵攻撃前で減算。</summary>
        public static int GetBleedWeakenAttack(int enemyBleedStacks)
            => MetaPanel.BleedWeakenAttack(Rank(MetaPanelKind.SparkBleed), enemyBleedStacks);
    }
}

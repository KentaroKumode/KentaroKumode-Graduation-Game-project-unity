using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using GameLoop;
using CombatSystem;
using MapSystem;
using EventSystem;
using ItemSystem;
using ItemSystem.Shop;

namespace AutoTest
{
    /// <summary>AutoRunner: 1 ランの実行 (RunOne) と 1 手の進行 (Step)。</summary>
    public partial class AutoRunner
    {
        private IEnumerator RunOne(int index)
        {
            var gm = GameManager.Instance;
            // 全ラン戦闘貪欲ルーチン（戦闘回避ルーチンは廃止）
            _curCombatAverse = false;

            // ビルド軸ペルソナ抽選 (2026-07-15)。RawTier = 従来動作、非RawTier は選好パッシブに Score+2 加点。
            if (_ascensionPersonaLock)
            {
                // 周回モード: ペルソナは RunAscension が固定済み。 抽選し直さない。
            }
            else if (useBuildPersonas)
            {
                // **決定論シード時はラン毎に派生シードで作り直す。** TickCount 由来の種を使い回すと
                // 同じマスターシードでもランに配られるペルソナ列が毎回変わり、 再現性が壊れる
                // (2026-07-29: A/B バッチが一致しなかった主因)。
                if (GameLoop.GameRng.IsSeeded)
                    _personaRng = new System.Random(
                        GameLoop.GameRng.Range(0, int.MaxValue, "autorun.persona", index));
                else if (_personaRng == null)
                    _personaRng = new System.Random(personaSeed == 0 ? System.Environment.TickCount : personaSeed);
                _currentPersona = BuildPersonaProfiles.Roll(rawTierRatio, _personaRng);
            }
            else
            {
                _currentPersona = BuildPersona.RawTier;
            }
            // メタバフ軸の一斉走査: ラン単位でラウンドロビン (等サンプル)。
            // ランダムではなく順送りにすることで、 中断しても軸ごとの本数が偏らない。
            if (metaBuffMode == MetaBuffMode.BuildFocused && sweepAllMetaAxes)
                _currentAxis = SweepAxes[index % SweepAxes.Length];
            else
                _currentAxis = metaAllocation;

            // 停止則のラン内実績。 **ランを跨いで持ち越さない** ── 持ち越すと run i の判断が
            //   それ以前のランに依存し、 チャンク分割と逐次で結果が変わる (決定性違反)。
            _rerollYieldSum = 0f; _rerollYieldN = 0;
            // [V3 mainCorr] 主攻撃の実績の比もランをまたいで持ち越さない
            ResetMainCorrRun();
            // [2026-11-10 試験] 黎明の溜める形の率を本体へ (ラン終了時に 0 へ戻す)
            ItemSystem.PassiveSkills.Effects.Daybreak.NextAttackPctPerUnit = System.Math.Max(0f, daybreakNextAttackPct);
            _daybreakApplied = ItemSystem.PassiveSkills.Effects.Daybreak.NextAttackPctPerUnit; _daybreakAppliedOnce = true;
            // [測定用の物差し 2026-11-10] 主攻撃 / 毒・出血・爆発・止血帯の倍率を本体へ (ラン終了時に 1 へ戻す)
            GameLoop.GameHooks.WeaponTierCap = System.Math.Max(0, weaponTierCap);
            GameLoop.GameHooks.PursuerNoReward = pursuerNoReward;                 // [測定 2026-11-12・#35]
            GameLoop.GameHooks.PursuerAtkMulOverride = pursuerAtkMul;
            GameLoop.GameHooks.PursuerHpAdd = pursuerHpAdd;
            GameLoop.GameHooks.PursuerSupplyOverride = pursuerSupply;
            GameLoop.GameHooks.PursuerLeavesTerritory = pursuerLeavesTerritory;   // [測定 2026-11-12] 縄張りの作り
            GameLoop.GameHooks.TerritoryTreasure = territoryTreasure;
            GameLoop.GameHooks.TerminalHealMulOverride = System.Math.Max(0, terminalHealMul);   // [測定 2026-11-12・#102]
            GameLoop.GameHooks.TerminalArresterNeedOverride = System.Math.Max(0, terminalArresterNeed);
            GameLoop.GameHooks.TerminalFirstAidBattleOnly = terminalFirstAidBattleOnly;
            GameLoop.GameHooks.TerminalKillBonusNeedsAttack = terminalKillNeedsAttack;   // [測定 2026-11-13・#117]
            GameLoop.GameHooks.HandCarryDisabled = !handCarry;                           // [測定 2026-11-13] 手札の持ち越し
            GameLoop.GameHooks.VescaDiceForce = System.Math.Max(0, vescaDiceForce);   // [測定 2026-11-11]   // [測定 2026-11-11] 武器の段の上限 (ラン終了時に 0 へ)
            CombatSystem.EnemyHpLedger.ProbeMainMult = ProbeMultClamp(probeMainMult);
            CombatSystem.EnemyHpLedger.ProbePassiveMult = ProbeMultClamp(probePassiveMult);
            _probeMainApplied = CombatSystem.EnemyHpLedger.ProbeMainMult; _probePassiveApplied = CombatSystem.EnemyHpLedger.ProbePassiveMult; _probeAppliedOnce = true;

            GameLoop.GoldIncome.RunByDepthSource.Clear();
            _cur = new RunRec
            {
                index = index, profile = "貪欲",
                persona = _currentPersona.ToString(),
                metaAxis = metaAllocation.ToString(),
            };
            GameLoop.ProvisionSystem.Stats.Reset(); // 物資の発生源別収支を1ラン単位で集計
            _curLog.Clear();
            _exceptionFlag = false;
            _exceptionMsg = null;
            _lastResolvedEvent = null;
            _pendingEnemyName = null;
            _eventStuckCount = 0;
            _lastEventInfo = "";

            // タイトルへ戻す（前ランが RunClear/GameOver 停止のままなら）
            int guard = 0;
            while (gm.CurrentPhase != GameManager.GamePhase.Title && guard++ < 50)
            {
                // 前ランが一択待ちのまま止まっていたら答えてから閉じる (答えないと RunClear に進まない)
                if (gm.CurrentPhase == GameManager.GamePhase.RiftChoice)
                    gm.ChooseRiftFate(false);
                if (gm.CurrentPhase == GameManager.GamePhase.GameOver ||
                    gm.CurrentPhase == GameManager.GamePhase.RunClear)
                    gm.ReturnToTitle();
                // ReturnToTitle は同期遷移。完了済みなら不要な1フレーム待ちを挟まない。
                if (gm.CurrentPhase == GameManager.GamePhase.Title)
                    break;
                yield return null;
            }

            // メタ恒久進行の前処理 (パターン別)
            MetaAllocationPresets.AllowOverBudget = metaAllowOverBudget;
            try
            {
                switch (metaPattern)
                {
                    case MetaPattern.Cowardly:
                        MetaProgression.MetaProgressManager.Instance?.ResetAll();
                        break;
                    case MetaPattern.FullProgression:
                        // 2026-07-28: MaxAllForTesting() は予算 36pt に対し全 108 段を立てる
                        // (v6 の固定予算モデルへの追従漏れ)。 実在する 36pt 配分プリセットへ置換。
                        // 既定は Balanced ＝ 予算を初めて使い切ったプレイヤーの標準形。
                        // §13-3 のボス調整はこの配分に対して行う。
                        // **配分スペックが指定されていればそちらを優先** (2026-09-10)。
                        //   整備パネルはオッズ比 14〜21 で全軸中最大なのに、 どの軸が効いているかの
                        //   内訳が無い。 drop-one アブレーション用の注入口。
                        if (_metaAblationSpec != null)
                        {
                            MetaProgression.MetaProgressManager.Instance?.ResetAll();
                            MetaAllocationPresets.ApplyRanks(_metaAblationSpec, "ablation:" + curArmLabel);
                        }
                        else if (!string.IsNullOrWhiteSpace(metaRankSpec))
                        {
                            MetaProgression.MetaProgressManager.Instance?.ResetAll();
                            MetaAllocationPresets.ApplyRanks(
                                MetaAllocationPresets.ParseSpec(metaRankSpec), "spec:" + metaRankSpec);
                        }
                        else MetaAllocationPresets.Apply(metaAllocation);
                        break;
                    case MetaPattern.Untouched:
                        // 何もしない
                        break;
                }

                // 挑戦の難易度 (挑戦カード・§23-25)。 2026-11-10 に旧配点方式 (§15-2) を撤去 (#9)。
                //   優先: スイープの固定値 (_pendingChallengeDifficulty) → enableAllDebuffs (難易度 10) → challengeCardDifficulty。
                //   **周回モード中はここを飛ばす** ── AscensionLoop が難易度の唯一の所有者。
                if (!_ascensionPersonaLock)
                {
                    var stc = MetaProgression.MetaProgressManager.Instance?.State;
                    if (stc != null)
                        stc.challengeDifficulty = IntendedChallengeDifficulty();   // Begin も同じ規則で先に入れる (#119)
                }
                else
                {
                    // 周回モード: **ここが ResetAll の直後**。 上の MetaAllocationPresets.Apply は
                    // ResetAll() で MetaProgressState を作り直すので、 遺物と挑戦構成の流し込みは
                    // 必ずこの位置で行う。 これより前に置くと毎ラン消える。
                    _ascension?.BeforeRun();
                }

                // 測定の差し込み口
                {
                    GameHooks.ProvisionMaxOverride = provisionMaxOverride;
                    GameHooks.ArmorDefPerRankOverride = armorDefPerRank;
                    GameHooks.ArmorShieldPerRankOverride = armorShieldPerRank;
                    GameHooks.ArmorFlatEveryOverride = armorFlatEvery;
                    MetaProgression.Relics.RelicApplicator.Override = RelicPresets.Parse(relicLoadoutSpec);
                    GameHooks.RelicStatOffMask = ParseRelicStatOff(relicStatOff);           // [#95] 既定 "" = 0 (止めない)
                    GameHooks.RelicSetBonusOffMask = ParseRelicSetBonusOff(relicSetBonusOff);
                    GameHooks.EnemyArmorPenOverride = ParseEnemyArmorPen(enemyArmorPenSpec); // [#20/#41] 既定 "" = null (データの値)
                    GameHooks.ForcedChallengeCards = ParseChallengeForce(challengeForce);
                    GameHooks.ChooseChallengeCard = challengeCardPickByAxis ? BotHooks.ChooseChallengeByAxis : (System.Func<IReadOnlyList<string>, int>)null;
                }

                // 遺物プリセット / 挑戦構成をここで流し込む。
                //
                // **2026-09-05 修正: この流し込みは `_ascensionPersonaLock` の else 側に入っていた。**
                //   ＝ 周回/スイープモードでしか実行されず、 **通常バッチでは遺物が一度も乗らなかった**。
                //   `forceTheoreticalRelic` を立てて「遺物を理論値固定」とログに出しても、
                //   State には 1 個も入らない。 25 万ラン の学習を丸ごと「遺物なし」で回して発覚した
                //   (サマリーの `[実効状態]` 行が `遺物 0個 計0pt` を出し続けていた ── **設定ログではなく
                //   実効状態の行を読むこと**)。 if/else の外へ出して両方の経路で通す。
                //
                // **遺物の有無と挑戦構成は独立に扱う** (2026-08-09 修正)。 旧実装は
                //   丸ごと `if (_pendingPresetRelic != null)` の中にあったため、
                //   **遺物なしアームだけ挑戦スコアが 0 に落ちていた**。 実測: 挑戦30pt の
                //   単軸スイープで、 遺物なし行だけ 5層クリア 53.2% (他アームは 5% 前後) と
                //   0pt の値がそのまま出ていた。

                {
                    var st = MetaProgression.MetaProgressManager.Instance?.State;
                    if (st != null)
                    {
                        // **遺物は「明示的に管理する」と宣言したスイープでは必ず作り直す。**
                        //   2026-08-10: 旧実装は `_pendingPresetRelic != null` のときだけ
                        //   作り直していたため、 **null は「遺物なし」ではなく「触らない」**
                        //   を意味していた。 結果、 前セッションでランダムに転がった遺物が
                        //   そのまま残り、 「遺物なし」を名乗るスイープが遺物込みで走っていた。
                        //   実測: 同一シード・同一職業・同一武器なのに開始 HP が 113 と 93 に割れ、
                        //   0pt 基準値が 53.3% と 16.6% に分かれていた (合計 pt は両方 9pt で
                        //   一致していたため、 合計だけ見ても気づけない ── 配点先の軸が違う)。
                        if (_pendingRelicExplicit)
                        {
                            st.SetRelicLoadout(_pendingPresetRelic != null
                                ? _pendingPresetRelic.ConvertAll(r => r.Clone()) : null);
                        }
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[AutoRunner] Meta pattern={metaPattern}: {e.Message}"); }

            // **探索ランか否かをラン番号で決める (2026-08-17b)。** 乱数を使わないので再現する。
            //   探索ランは不偏な値付けだけに使い、 観測 lift の集計からは丸ごと外す。
            _isExploreRun = exploreHoldoutRate > 0f
                         && (index % 10) < Mathf.RoundToInt(exploreRunFraction * 10f);
            if (_cur != null) _cur.isExploreRun = _isExploreRun;

            // **アイテム評価学習を挑戦スコア帯へ切り替える (2026-08-17)。**
            //   挑戦構成を流し込んだ直後・ラン開始前がこの位置。 帯が変わったときだけ読み直す。
            //   スイープはアームごとに pt が変わるので、 ここを通さないと
            //   **前のアームの序列で買い続ける**。 Tier 較正中は隔離領域を使うので触らない。
            if (!IsTierCalibration)
            {
                // 帯の固定 (botLearningBandOverride・#118) があればその帯を読む。 無ければ今の挑戦の点数の帯。
                try { LearnedPriorityProvider.SwitchToChallengeScore(BotLearningBandScore); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] 学習帯の切替失敗: {e.Message}"); }
                // **手書きの序列で走ったランを数える** (#118)。 黙って落ちると、 帯のフォルダが無いだけでクリア率が 0% になる。
                _bandSwitchRuns++;
                if (LearnedPriorityProvider.UsingFallback) _fallbackRuns++;
                // **戦闘名の危険度も同じ帯へ読み直す** (2026-11-12・#119)。 これまではバッチ開始時の 1 回だけで、
                //   その時の帯 (前の測定が PlayerPrefs に残した難易度から決まる) のまま全ランを走っていた。
                //   encounter_danger.json は帯 0 にしか無いので、 残りが帯 16 以上だと危険度が空になり、 ワーカー単位で結果が割れた。
                //   帯が変わった時だけ読む (毎ランのファイル読みはしない)。
                try
                {
                    string dangerRoot = System.IO.Path.GetFullPath(TierLearningRoot);
                    if (!string.Equals(EncounterDangerStats.RequestedRoot, dangerRoot, StringComparison.OrdinalIgnoreCase))
                        EncounterDangerStats.Reload(dangerRoot);
                }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] 戦闘名の危険度の切替失敗: {e.Message}"); }
            }

            // 2026-06-28: BOT は職業をランダムに選択 (BOT 学習で全 4 職を均等にサンプル)
            GameLoop.GameManager.SelectedClass = (GameLoop.ClassType)GameLoop.GameRng.RangeAuto("AutoRunner.1", 0, 4);
            // 端子の山札 (ADR-0014): 戦闘後の端子の報酬を選ぶ (AutoRunner.Terminals)。
            GameHooks.ChooseTerminalReward = ChooseTerminalReward;
            gm.StartNewRun();
            // アイテム アブレーション: 開幕付与。 **StartNewRun の直後**でないと
            //   ownedPassiveItems がまだ存在しない。 AddPassiveItem は GameRng を
            //   1 つも消費しないので、 付与あり/なしで乱数列はずれない ＝ ペアが保たれる。
            if (!string.IsNullOrEmpty(_pendingGrantItemId) && gm.Run != null)
                ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(gm.Run, _pendingGrantItemId);

            // [計装 2026-11-10] 実際に入っていた整備パネルの段 (配点の指定が予算で切り詰められていないかを、 ランの記録で確かめる)
            if (_cur != null)
            {
                var mst = MetaProgression.MetaProgressManager.Instance?.State;
                if (mst != null)
                {
                    var parts = new List<string>();
                    foreach (MetaProgression.MetaPanelKind k in Enum.GetValues(typeof(MetaProgression.MetaPanelKind)))
                    {
                        int rk = mst.GetRank(k);
                        if (rk > 0) parts.Add(k + ":" + rk);
                    }
                    _cur.metaRanks = string.Join(",", parts);
                }
                _cur.startClass = gm.Run != null ? gm.Run.playerClass.ToString() : "";
            }

            // [診断] 軸の品をまとめて開幕に配る (2026-11-07)。 軸に深く投資した構成が実際に強いかを因果で測る。
            if (!string.IsNullOrEmpty(axisPackageAxis) && gm.Run != null) GrantAxisPackage(index, gm.Run);
            // [診断] 武器を 1 本渡して装備させる (2026-11-07)。 GrantOne は AddPassiveItem + TryAutoEquip で、 乱数を使わない。
            if (!string.IsNullOrEmpty(grantStartWeapon) && gm.Run != null) { GrantOne(gm.Run, grantStartWeapon); _cur?.acquiredItemsEver.Add(grantStartWeapon); }

            // ランダム付与試行: このランの割り当てを引く。 引くだけで、 配るのは各層に着いてから。
            if (randomGrantTrial && gm.Run != null) RollGrantPlan(index, gm.Run);
            // 乖離調査 (2026-08-10): **ラン開始時点の構成**。 職業はここまでの
            //   GameRng 消費で決まるので、 ここが割れていれば乱数、 揃っていれば乱数より後の分岐。
            {
                var r = gm.Run;
                string pd = (r?.permanentDebuffs != null && r.permanentDebuffs.Count > 0)
                          ? string.Join("+", r.permanentDebuffs) : "なし";
                _probeRunConfig = $"{GameLoop.GameManager.SelectedClass}"
                                + $"/max{r?.playerMaxHP}"
                                + $"/整備パネルHP+{MetaProgression.MetaPanel.ShellHp(MetaProgression.MetaProgressManager.Instance?.State?.GetRank(MetaProgression.MetaPanelKind.Shell) ?? 0)}"
                                + $"/遺物HP+{MetaProgression.Relics.RelicApplicator.GetMaxHpBonus(r)}"
                                + $"/メタ{metaBuffMode}"
                                + $"/恒久:{pd}";
            }
            _deckLaRunSeed = index;   // [#105] 山札の先読みの専用乱数もラン番号だけで決める
            ResetAblationRng(index);

            _prevCoins = gm.Run != null ? gm.Run.coins : 0;
            _prevMaterials = gm.Run != null ? gm.Run.weaponMaterials : 0;

            int iter = 0;
            int stall = 0;
            var lastPhase = gm.CurrentPhase;
            string lastNode = CurrentNodeId();
            int lastFloor = gm.Run?.Depth ?? 1;
            int lastHp = gm.Run?.playerHP ?? 0;
            // **番犬**: 1 ラン の実時間上限 (2026-08-10)。
            //   反復上限もストール検出も「状態が変わり続ける」形の固着には効かない
            //   ── 実際、 戦闘数が止まったまま 15 万フレーム回り続けるランが出た。
            //   バッチが 1 ラン で永久に止まると、 測定そのものが成立しない。
            float runStartRt = Time.realtimeSinceStartup;
            // (旧 Ultra の思考時間の控除と worker 待ちの空回りの除外は 2026-11-13 に削除・#135。 通常のランでは 0 / false だった)

            while (true)
            {
                if (_exceptionFlag)
                {
                    Finish(Outcome.Crash, $"例外: {_exceptionMsg}");
                    yield break;
                }
                // 到達した層の付与を配る。 計画は空のことがほとんどなので毎周でも安い。
                if (_grantPlan.Count > 0 || _grantGoldAmount > 0) ApplyDueGrants(gm);
                if (iter++ > maxIterationsPerRun)
                {
                    Finish(Outcome.Deadlock, $"反復上限超過 phase={gm.CurrentPhase} node={CurrentNodeId()}");
                    yield break;
                }
                if (runWatchdogSeconds > 0f
                    && Time.realtimeSinceStartup - runStartRt > runWatchdogSeconds)
                {
                    // **打ち切ったことを必ず残す。** 黙って捨てると、 集計だけ見て
                    //   「そういう分布なのだ」と読んでしまう。
                    Finish(Outcome.Deadlock,
                        $"番犬 {runWatchdogSeconds:F0}s 超過 phase={gm.CurrentPhase} node={CurrentNodeId()} "
                      + $"floor={gm.Run?.Depth ?? -1} 戦闘={_cur?.totalCombats ?? -1}");
                    yield break;
                }

                var phase = gm.CurrentPhase;
                heartbeat++;                 // 本体ループが生きている証拠 (外部観測用)
                lastPhaseSeen = phase.ToString();

                // 進展のないストール検出
                string node = CurrentNodeId();
                int fl = gm.Run?.Depth ?? 0;
                int hp = gm.Run?.playerHP ?? 0;
                if (phase == lastPhase && node == lastNode && fl == lastFloor && hp == lastHp)
                {
                    if (++stall > stallLimit)
                    {
                        string ev = phase == GameManager.GamePhase.EventEncounter && !string.IsNullOrEmpty(_lastEventInfo)
                            ? $" event=[{_lastEventInfo}]" : "";
                        Finish(Outcome.Deadlock, $"ストール phase={phase} node={node} floor={fl}{ev}");
                        yield break;
                    }
                }
                else stall = 0;

                // 5F突入時 (4→5) にチェーン進行状況をスナップショット
                if (fl == 5 && lastFloor != 5 && _cur != null && _cur.convictionStageAt5F < 0 && gm.Run != null)
                {
                    var rs = gm.Run;
                    _cur.convictionStageAt5F = rs.convictionStage;
                    var owned = rs.ownedPassiveItems;
                    _cur.hadConvictionItem5F = owned != null && owned.Contains(GameLoop.ConvictionSystem.IdConviction);
                    _cur.hadResolveAt5F     = owned != null && owned.Contains(GameLoop.ConvictionSystem.IdResolve);
                    _cur.hadTruthAt5F       = owned != null && owned.Contains(GameLoop.ConvictionSystem.IdTruth);
                    var flags = rs.ownedFlags;
                    _cur.hadFlagYogenAt5F    = flags != null && flags.Contains(GameLoop.ItemIds.HardshipProphecy);
                    _cur.hadFlagKakushinAt5F = flags != null && flags.Contains(GameLoop.ItemIds.HardshipConviction);
                }

                lastPhase = phase; lastNode = node; lastFloor = fl; lastHp = hp;

                // ラストスタンド発動検知
                TrackLastStand();
                TrackEconomy();

                bool finished = false;
                try
                {
                    finished = Step(phase);
                }
                catch (Exception e)
                {
                    Finish(Outcome.Crash, $"Step例外 phase={phase}: {e.GetType().Name} {e.Message}");
                    yield break;
                }

                if (finished) yield break;

                // 高速化: 1フレームあたり複数 Step を回す。
                // GameManager は同期遷移なので問題ないが、UI/演出が必要な場合は stepsPerYield=1 にする。
                _innerStepCount++;
                if (_innerStepCount >= Mathf.Max(1, stepsPerYield))
                {
                    _innerStepCount = 0;
                    yield return null;
                }
            }
        }

        private int _innerStepCount;

        /// <summary>現フェーズに対する1アクション。ラン終了時 true。</summary>
        private bool Step(GameManager.GamePhase phase)
        {
            var gm = GameManager.Instance;

            switch (phase)
            {
                case GameManager.GamePhase.Title:
                case GameManager.GamePhase.RunStart:
                case GameManager.GamePhase.FloorIntro:
                    return false; // GameManager が即座に MapNavigation へ遷移

                case GameManager.GamePhase.MapNavigation:
                    return DoNavigate();

                case GameManager.GamePhase.Combat:
                    // **フェーズが Combat なのに戦闘が動いていない状態を可視化する。**
                    //   ここが起きると RunCombatWithItems は即 return し、 ループが空回りして
                    //   「ストール phase=Combat」としか出ない ── 理由が残らないので追えない。
                    //   バッチ中は filterLogType=Error なので Error で出す (Log は落ちる)。
                    {
                        var cmProbe = CombatManager.Instance;
                        if (cmProbe == null || !cmProbe.IsCombatActive)
                        {
                            if (++_combatInactiveStreak == 20)
                                Debug.LogError("[AutoRunner] 戦闘フェーズだが戦闘が非アクティブ: "
                                    + $"cm={(cmProbe == null ? "null" : "有")} "
                                    + $"enemy={(cmProbe?.CurrentEnemy?.id ?? "null")} "
                                    + $"node={CurrentNodeId()} floor={gm.Run?.Depth} "
                                    + $"gmEnemy={(gm.CurrentEnemy?.id ?? "null")} "
                                    // 開始処理が「入った」通番と「完了した」通番。 食い違えば
                                    // StartCombatInternal が途中で抜けている。
                                    + $"startEntered={cmProbe?.StartCombatEntered} "
                                    + $"startCompleted={cmProbe?.StartCombatCompleted} "
                                    // 開始処理が完了しているのに非アクティブ ＝ 一度始まって
                                    // すぐ終わった。 どちらの HP が尽きていたかで原因が割れる。
                                    + $"pHP={cmProbe?.PlayerHP} eHP={cmProbe?.EnemyHP} "
                                    + $"runHP={gm.Run?.playerHP}/{gm.Run?.playerMaxHP} "
                                    + $"eMax={cmProbe?.CurrentEnemy?.maxHP}");
                        }
                        else _combatInactiveStreak = 0;
                    }
                    RunCombatWithItems(gm);
                    return false;

                case GameManager.GamePhase.BattleResult:
                    // 6Fクリア時のビルド情報を後でサルベージできるよう、撃破直後にスナップショット
                    bool floor6BossWinPending = gm.LastCombatResult.HasValue
                        && gm.LastCombatResult.Value.playerWon
                        && MapManager.Instance?.CurrentNode != null
                        && MapManager.Instance.CurrentNode.type == TileType.Boss
                        && gm.Run != null && gm.Run.Depth == 6;
                    // (旧 TryDropRapierOnFloor4Boss は撤去。レイピアは 4F イベント「亡霊との決闘」 で正規配布される)
                    gm.ConfirmBattleResult();
                    if (floor6BossWinPending) Capture6FClearSnapshot(gm);
                    return false;

                case GameManager.GamePhase.Reward:
                    // 戦闘後ドロップ2択: 未所持(ビルドの幅)を優先、同条件なら option a。
                    while (gm.HasPendingRewardChoice)
                    {
                        var (ra, rb) = gm.CurrentRewardChoice;
                        var owned = gm.Run?.ownedPassiveItems;
                        bool ownsA = owned != null && ra != null && owned.Contains(ra);
                        bool ownsB = owned != null && rb != null && owned.Contains(rb);
                        int pick = (ownsA && !ownsB) ? 1 : 0;
                        // [2026-11-10] 軸に寄せる方策: 片方だけが未所持の軸の品なら、 そちらを取る (両方/どちらも違うなら従来どおり)
                        if (!string.IsNullOrEmpty(buyAxisFocus))
                        {
                            bool fa = !ownsA && IsAxisFocusItem(ra), fb = !ownsB && IsAxisFocusItem(rb);
                            if (fa != fb) pick = fa ? 0 : 1;
                        }
                        gm.ResolveRewardChoice(pick);
                    }
                    gm.ConfirmReward();
                    return false;

                case GameManager.GamePhase.RestStop:
                {
                    // 休憩は3択(食事/回復/強化)から1つ。生存(燃料→HP)優先、余裕あれば成長。
                    var run = gm.Run;
                    float hpR = run.playerMaxHP > 0 ? (float)run.playerHP / run.playerMaxHP : 1f;
                    int cost = GameManager.WeaponUpgradeCost(run);
                    // 物資(ADR-0002): 飢餓→物資統合で「食事」休憩は廃止。休憩は HP回復 or 武器強化のみ。
                    bool greedyBossPrep = !_curCombatAverse && _curBossNear;

                    // T4 到達率改善 v2: 強化を更に優先 (T4 追跡型)
                    //   ・素材が足りる && HPギリ余裕 → 必ず強化
                    //   ・T4 寸前 (cost ≤ 4 で次が T4) なら HP 0.25 でも強化
                    //   ・素材余剰 (≥2cost) なら HP 0.30 で強化
                    bool richMaterials = run.weaponMaterials >= cost * 2;
                    bool canUpgrade    = cost != int.MaxValue && run.weaponMaterials >= cost;
                    // T4 到達直前判定: 次の強化で T4 になる (現 T3+ → T4 等)
                    //   段は items.json の tier で読む (#123)。 以前は id の綴り Contains("_t3") で読んでいて、
                    //   id = 表示名 (2026-09-22) になってから常に false だった (CLAUDE.md「id から構造を読み取らない」)。
                    bool nextStepReachesT4 =
                        canUpgrade &&
                        !string.IsNullOrEmpty(run.equippedWeaponId) &&
                        ItemSystem.ItemDatabase.Instance?.GetItem(run.equippedWeaponId)?.tier == 3 &&
                        run.weaponPlus > 0;
                    int restPick;
                    if (nextStepReachesT4 && hpR > 0.25f)
                        restPick = RestUpgradeIndex;         // T4 寸前は HP低めでも強化 (機会逃さない)
                    else if (richMaterials && hpR > 0.30f)
                        restPick = RestUpgradeIndex;         // 素材余剰: 危機未満なら強化を優先
                    else if (greedyBossPrep && hpR < 0.8f)
                        restPick = RestHealIndex;            // 貪欲: ボス前にHPを整える
                    else if (hpR <= 0.40f)
                        restPick = RestHealIndex;            // 低HPは回復
                    else if (canUpgrade)
                        restPick = RestUpgradeIndex;         // 中HP+素材有り → 強化優先
                    else
                        restPick = RestHealIndex;            // 満ちていれば回復で無駄なく（食事休憩は廃止）

                    // **`canUpgrade` で条件を足さないこと。** 元の分岐は
                    //   `richMaterials && hpR > 0.30f` で `RestUpgrade()` を呼んでおり、
                    //   `richMaterials` は `weaponMaterials >= cost * 2` ── 武器が最大強化だと
                    //   `cost == int.MaxValue` なので `cost * 2` が unchecked で −2 になり、
                    //   **常に true** になる。 つまり強化不能でも `RestUpgrade()` が呼ばれる
                    //   経路が既にあり、 ここに `&& canUpgrade` を足すと **その分が回復へ
                    //   移って既存の基準値が変わる**。 挙動は 1 ビットも変えない。
                    if (restPick == RestUpgradeIndex) { gm.RestUpgrade(); return false; }

                    // 2026-11-04: 回復の場面で使える休憩の残り 2 つ (多めに休む・面を外す)。
                    //   どちらも「回復を選んだ」後の枝で、 強化を選ぶ基準 (上の分岐) は変えない。
                    //   面を外す: HP が満ちていて回復が無駄になる場面だけ。 役を持たない低い目 (2 以下) を外して出目の平均を上げる
                    if (hpR >= 0.95f && TryRestRemoveLowFace(gm)) return false;
                    //   山札から抜く (ADR-0014): 同じく HP が満ちている時だけ。 基本の端子が多ければ 1 枚 (AutoRunner.Terminals)
                    if (hpR >= 0.95f && TryRestRemoveTerminal(gm)) return false;
                    //   多めに休む: HP が 6 割を切っていて、 追跡者に補足されていない時 (1 ターン進むので、 追われている時は使わない)
                    var sim = gm.FreeMap;
                    bool hunted = sim != null && sim.FoeAlive && sim.FoeMode == MapSystem.FreeMove.FoeMode.Chase;
                    if (hpR < 0.6f && sim != null && !hunted) { gm.RestHealMore(); return false; }
                    gm.RestHeal();
                    return false;
                }

                case GameManager.GamePhase.ShopVisit:
                    DoShop();
                    return false;

                case GameManager.GamePhase.EventEncounter:
                    DoEvent();
                    return false;

                case GameManager.GamePhase.TreasureOpen:
                case GameManager.GamePhase.TrapTriggered:
                    gm.ConfirmTileEvent();
                    return false;

                case GameManager.GamePhase.ExchangeTile:
                    // 交換は最低Tierパッシブ→上位ランダムの厳密アップグレード。所持があれば必ず交換。
                    if (gm.CanExchangeTile) gm.DoExchangeTile();
                    else gm.SkipExchangeTile();
                    return false;

                case GameManager.GamePhase.GateRitual:
                    // **方針: 3 工程とも払う (既定)。** 欠陥は避けられる限り避ける。
                    //   払えないとき (最大HP を割る / 遺物が 2 個未満 / 物資 40 未満) だけ
                    //   〈不完全な〜〉が付く。
                    //
                    //   **これは方策であって最適解ではない。** 旧儀式では「払う方が
                    //   −4.33pt で一方的に損」という測定結果が出た前例がある。
                    //   門は代償も罰も別物なので再測定が要る ── 工程ごとに払う/払わないを
                    //   切り替えるアームを組んで McNemar で比べること (§15-1 の配点スイープと同じ形)。
                    if (!gateBotSkipsAll)
                    {
                        gm.OfferGateBlood(gateBotPaysBlood);
                        gm.OfferGateRelics(gateBotPaysRelics);
                        gm.OfferGateTransfer(gateBotPaysTransfer);
                    }
                    // 素通り (gateBotSkipsAll) は**計測専用の対照群**。 工程を 1 つも解決しないので
                    //   代償も欠陥も無い ＝「門が無かったラン」。 製品では起こらない。
                    gm.CompleteGateRitual();
                    return false;

                case GameManager.GamePhase.FloorClear:
                    gm.ConfirmFloorClear();
                    return false;

                case GameManager.GamePhase.RiftChoice:
                    // ヴェスカ撃破後の一択 (2026-08-17)。 **必ず答えること** ── 放置すると
                    //   RunClear へ進まずバッチが止まる。
                    //   BOT は常に「閉じる」= TRUE END。 勝敗の計測値は分岐で変わらないので、
                    //   額縁 (Epilogue) が開く側に寄せておく。
                    gm.ChooseRiftFate(false);
                    return false;

                case GameManager.GamePhase.RunClear:
                    FinishClear();
                    return true;

                case GameManager.GamePhase.GameOver:
                    FinishGameOver();
                    return true;

                default:
                    return false;
            }
        }

        // ===== 行動方針: 前進貪欲 + 生存重視 =====

        /// <summary>休憩で面を 1 枚外す (2026-11-04)。 役を持たない (T1 以下の) いちばん低い目で、 その目が 2 以下の時だけ。
        /// 払えなければ外さない。 外したら true (休憩の行動を使った)。</summary>
        private static bool TryRestRemoveLowFace(GameManager gm)
        {
            var run = gm?.Run;
            if (run?.diceFaceParts == null) return false;
            if (!gm.TryGetFaceRemovalCost(out int gold, out int materials)) return false;
            if (run.weaponMaterials < materials || run.coins < gold) return false;
            int face = int.MaxValue;
            foreach (var p in run.diceFaceParts)
                if (p.tier <= DiceFaceParts.Tier.T1 && p.face < face) face = p.face;
            if (face > 2) return false;
            return gm.RestRemoveFace(face);
        }
    }
}

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
    /// <summary>AutoRunner: 計測フック・ラン終了処理・ログ購読・初期化補助。</summary>
    public partial class AutoRunner
    {
        /// <summary>[計装 2026-11-13] ボスの点の戦闘に入った時 (= 層を抜けた時) の不安定化を <see cref="RunRec.stormLayers"/> へ 1 層ぶん足す。
        /// 本体の欄を読むだけ (乱数・動きに触らない)。 不安定化の時計でない層 (嵐なし・stormClock=Turns・深さ 0/7/8) と、 同じ深さの 2 度目は足さない。</summary>
        private void RecordStormLayer(GameManager gm)
        {
            var sim = gm?.FreeMap;
            if (_cur == null || sim == null || !sim.InstabilityMode || gm.Run == null) return;
            int depth = gm.Run.Depth;
            for (int i = 0; i + StormLayerCols <= _cur.stormLayers.Count; i += StormLayerCols) if (_cur.stormLayers[i] == depth) return;
            var p = sim.InstabilityParts;
            _cur.stormLayers.AddRange(new[] { depth, sim.Instability, sim.StormPhase,
                p[MapSystem.FreeMove.Instability.PartTake], p[MapSystem.FreeMove.Instability.PartEscalation], p[MapSystem.FreeMove.Instability.PartWalk],
                p[MapSystem.FreeMove.Instability.PartEventTurns], p[MapSystem.FreeMove.Instability.PartShift],
                sim.CollapseTurn, sim.StormStepsTaken, sim.StormHpPaid, sim.Turn });
        }

        private void OnEnemyEncountered(EnemyData e)
        {
            var gm = GameManager.Instance;
            var cm = CombatSystem.CombatManager.Instance;

            // 5Fボス勝率スイープ: 採取モード中、対象フロアのボスに到達したらビルドを採取
            if (_simHarvestArmed && e != null && e.id != null
                && e.id.StartsWith($"boss_layer{simBossFloor}")
                && _simBases.Count < simSampleBuilds && gm?.Run != null)
            {
                var run = gm.Run;
                var passives = new List<string>();
                if (run.ownedPassiveItems != null)
                    foreach (var id in run.ownedPassiveItems)
                    {
                        // 武器・ダイスはスイープ側で差し替えるため土台ビルドからは除外
                        var it = ItemDatabase.Instance?.GetItem(id);
                        if (it != null && (it.category == ItemCategory.Weapon || it.category == ItemCategory.Dice)) continue;
                        passives.Add(id);
                    }
                _simBases.Add(new SimBuild
                {
                    hp = run.playerHP,
                    weaponPlus = run.weaponPlus,
                    limitBreakStage = run.limitBreakStage,
                    passives = passives
                });
            }

            // チェーン swap で再エンカウントしたケース: 前フォームの戦績を 1 件確定させる。
            // (戦闘自体は継続するため OnBattleEnded は鳴らない → ここで明示記録しないと
            //  途中フォームが永遠に summary に出てこない)
            SettleMainCorr();   // [V3 mainCorr] 前の段の最後のターンの実際を締める (段の記録に入れる)
            bool isChainSwap = cm != null && cm.IsCombatActive
                && !string.IsNullOrEmpty(_pendingEnemyId);
            if (isChainSwap && _cur != null)
            {
                // 戦闘中は Run.playerHP が更新されない（戦闘終了時のみ同期）。
                // チェーン中の正しい現在HPは CombatManager のライブ値を使う。
                int hpNow = (cm != null && cm.IsCombatActive) ? cm.PlayerHP : (gm?.Run?.playerHP ?? 0);
                var midRec = new CombatRec
                {
                    enemy = _pendingEnemyName ?? "?",
                    enemyId = _pendingEnemyId ?? "",
                    floor = gm?.Run?.Depth ?? 0,
                    isBoss = _pendingEnemyIsBoss,
                    won = true, // チェーン swap は前形態を倒したから起きる
                    turns = _cwWin + _cwDraw + _cwLoss,
                    hpBefore = _pendingEnemyHpBefore,
                    hpAfter = hpNow,
                    afterLastStand = _cur.lastStandUsed,
                    tWin = _cwWin, tDraw = _cwDraw, tLoss = _cwLoss, tLossAbs = _cwLossAbs,
                    weaponId = gm?.Run?.equippedWeaponId ?? "",
                    diceId = gm?.Run?.equippedDiceId ?? "",
                    // **被ダメ/与ダメ/敵HP を必ず埋める (2026-09-09)。** 未設定のままだと 0 が入り、
                    //   per-enemy 集計で 7層 p1〜p3 が「被ダメ 0・与ダメ 0・敵HP 2」という
                    //   **存在しない弱さ**に見える (敵HP 2 は「その段で死んだ数件だけが実HP を持つ」
                    //   平均の産物)。 実際に一度それを根拠に強化しかけた。
                    //   damageDealt は段の HP を削り切ったから遷移が起きるので maxHP と等しい。
                    damageTaken = Math.Max(0, _pendingEnemyHpBefore - hpNow),
                    dmgParts = DmgPartsSinceStart(),
                    hpLedger = LedgerSinceStart(), shownFloor = _pendingShownFloor, axisAtStart = _pendingAxis,
                    mainPred = (float)System.Math.Round(_mfPred, 1), mainPredCorr = (float)System.Math.Round(_mfPredCorr, 1),
                    mainActual = (float)System.Math.Round(_mfAct, 1), atkHist = (int[])_mfAtkHist.Clone(), dawnShots = _mfDawnShots.ToArray(),
                    enemyMaxHP = CombatSystem.EnemyDatabase.Get(_pendingEnemyId)?.maxHP ?? 0,
                    damageDealt = CombatSystem.EnemyDatabase.Get(_pendingEnemyId)?.maxHP ?? 0,
                    playerMaxHpEnd = (cm != null && cm.IsCombatActive)
                                   ? cm.PlayerMaxHP : (gm?.Run?.playerMaxHP ?? 0),
                };
                _cur.combats.Add(midRec);
                // L1学習: チェーン途中で倒した形態も「撃破記録」に入れる
                if (!string.IsNullOrEmpty(midRec.enemyId) && midRec.enemyId.StartsWith(GameLoop.BossIds.Layer7Prefix))
                    _cur.awakenedFormsKilled.Add(midRec.enemyId);
                // 注: totalCombats / totalTurns / totalWins には加算しない
                // (OnBattleEnded 側のチェーン最終形態分でラン全体の合計が記録されるため、
                //  ここで足すと二重計上になる。combats リストの per-enemy 集計だけ厚くする)
            }

            _pendingEnemyName = e != null ? e.displayName : "?";
            _pendingEnemyId = e != null && e.id != null ? e.id : "";
            // ボス判定は敵IDのみで厳密に行う（ノード種別フォールバックは誤検出の元）
            _pendingEnemyIsBoss = GameLoop.BossIds.IsBoss(_pendingEnemyId);
            if (_pendingEnemyIsBoss && !isChainSwap) RecordStormLayer(gm);   // [計装 2026-11-13] 層を抜けた時の不安定化 (読むだけ)
            // 戦闘名の鍵 (追跡者の戦闘は点に載っていないので鍵なし)
            _pendingEncounterKey = (gm != null && !gm.InPursuerCombat)
                ? (EncounterDangerStats.KeyOf(MapSystem.MapManager.Instance?.CurrentNode, gm.Run?.Depth ?? 1) ?? "") : "";
            // 次フォーム開始時点の現在HP。チェーン中は CombatManager のライブHPを使う
            // （Run.playerHP は戦闘終了まで更新されないため、これが無いと各フォームの被ダメが常に0と誤計測される）。
            _pendingEnemyHpBefore = (cm != null && cm.IsCombatActive) ? cm.PlayerHP : (gm?.Run?.playerHP ?? 0);
            System.Array.Copy(CombatSystem.CombatStats.DamageBreakdown, _bdAtStart, _bdAtStart.Length);
            System.Array.Copy(CombatSystem.EnemyHpLedger.Cum, _ledAtStart, _ledAtStart.Length);
            _pendingShownFloor = gm?.Run?.currentFloor ?? 0;
            _pendingAxis = new int[ItemSystem.PassiveSkills.AxisRules.Ranked.Length];
            for (int ai = 0; ai < _pendingAxis.Length; ai++)
                _pendingAxis[ai] = ItemSystem.PassiveSkills.AxisRules.Count(gm?.Run, ItemSystem.PassiveSkills.AxisRules.Ranked[ai]);
            FlushPredTrace();
            if (_pendingEnemyIsBoss) SnapState(1, _pendingEnemyHpBefore);
            _cwWin = _cwDraw = _cwLoss = _cwLossAbs = 0; // 新戦闘のターン内訳リセット
            ResetMainCorrForm();
        }

        // ── [計装 2026-11-06] 与ダメの内訳 (戦闘ごと) ──
        //   出目の比重 = 攻撃の出目 ÷ (武器 + 出目 + 攻撃+N)。 与ダメ増加・会心はこの合計に後から掛かるので、 出目の比重を薄めるのは
        //   攻撃+N と、 倍率の外のダメージ (固定・追撃・毒・出血) だけ。 CombatStats.DamageBreakdown (全体の累計) の、 戦闘の開始からの差分。
        private readonly double[] _bdAtStart = new double[CombatSystem.CombatStats.DamageBreakdown.Length];
        // [計装 2026-11-10] 経路別の入った量 (EnemyHpLedger) の段の開始時点・見せる層・偏重 6 軸の数
        private readonly double[] _ledAtStart = new double[CombatSystem.EnemyHpLedger.Length];
        private int _pendingShownFloor;
        private int[] _pendingAxis;

        private float[] LedgerSinceStart()
        {
            var c = CombatSystem.EnemyHpLedger.Cum;
            var a = new float[c.Length];
            for (int i = 0; i < a.Length; i++) a[i] = (float)System.Math.Round(c[i] - _ledAtStart[i], 3);
            return a;
        }

        /// <summary>[武器, 攻撃の出目, 攻撃+N, 主攻撃の与ダメ (倍率の後), 固定ダメ, 追撃, 攻撃+N のうち出目から来た分]。</summary>
        private int[] DmgPartsSinceStart()
        {
            var b = CombatSystem.CombatStats.DamageBreakdown;
            int D(int i) => (int)System.Math.Min(int.MaxValue, System.Math.Max(0, b[i] - _bdAtStart[i]));
            return new[] { D(1), D(2), D(3), D(7), D(9), D(10), D(19), D(20), D(21), D(22) };
        }

        /// <summary>今の状態を 1 行 (10 列) 残す: 種別 (0 層に入った / 1 ボスに入る) / 深さ / HP / 最大 HP / パッシブ数 / 所持金 /
        /// 回復薬の数 / 護符の数 / 出目パーツの数 / 物資。 「この状態ならクリアできる確率」(マクロの価値 V) を測るため。</summary>
        private void SnapState(int kind, int hp)
        {
            var run = GameManager.Instance?.Run;
            if (_cur == null || run == null) return;
            int heal = 0, def = 0;
            if (run.ownedConsumables != null)
                foreach (var id in run.ownedConsumables)
                {
                    string fam = GameLoop.ItemIds.ConsFamilyOf(id);
                    if (fam == GameLoop.ItemIds.ConsHealFamily) heal++;
                    else if (fam == GameLoop.ItemIds.ConsShieldFamily) def++;
                }
            _cur.stateSnaps.AddRange(new[] { kind, run.Depth, hp, run.playerMaxHP, run.ownedPassiveItems?.Count ?? 0, run.coins,
                                             heal, def, run.diceFaceParts?.Count ?? 0, run.provision });
        }

        private void OnBattleEnded(CombatResult r)
        {
            FlushPredTrace();
            SettleMainCorr();   // [V3 mainCorr] 最後のターンの実際を締める
            if (_cur == null) return;
            var gm = GameManager.Instance;
            var rec = new CombatRec
            {
                enemy = string.IsNullOrEmpty(r.enemyDisplayName) ? _pendingEnemyName : r.enemyDisplayName,
                enemyId = _pendingEnemyId ?? "",
                floor = gm.Run?.Depth ?? 0,
                isBoss = _pendingEnemyIsBoss,
                won = r.playerWon,
                turns = r.totalTurns,
                hpBefore = _pendingEnemyHpBefore,
                hpAfter = r.playerHPRemaining,
                afterLastStand = _cur.lastStandUsed,
                tWin = _cwWin, tDraw = _cwDraw, tLoss = _cwLoss, tLossAbs = _cwLossAbs,
                isFightEnd = true,
                healApplied = r.healApplied,
                shieldGained = r.shieldGained,
                damageDealt = r.damageDealt,
                damageTaken = r.damageTaken,
                dmgParts = DmgPartsSinceStart(),
                hpLedger = LedgerSinceStart(), shownFloor = _pendingShownFloor, axisAtStart = _pendingAxis,
                mainPred = (float)System.Math.Round(_mfPred, 1), mainPredCorr = (float)System.Math.Round(_mfPredCorr, 1),
                mainActual = (float)System.Math.Round(_mfAct, 1), atkHist = (int[])_mfAtkHist.Clone(), dawnShots = _mfDawnShots.ToArray(),
                fbDecided = _fbDecided, fbTurns = _fbTurns, fbKillTurns = _fbKillTurns,
                enemyMaxHP = r.enemyMaxHP,
                weaponId = gm?.Run?.equippedWeaponId ?? "",
                diceId = gm?.Run?.equippedDiceId ?? "",
                deathCause = r.deathCause,
                playerDamageBySource = r.playerDamageBySource,
                playerMaxHpEnd = gm?.Run?.playerMaxHP ?? 0,
                encounterKey = _pendingEncounterKey,
                escRises = gm?.EscalationRisesThisCombat ?? 0, instEsc = gm?.InstabilityFromEscalationThisCombat ?? 0,   // [計装 2026-11-13]
            };
            _cur.combats.Add(rec);
            _cur.totalCombats++;
            _cur.totalTurns += r.totalTurns;
            if (r.playerWon) _cur.totalWins++;
            // L1学習: ラン全体に加算
            _cur.totalDamageDealt += r.damageDealt;
            _cur.totalDamageTaken += r.damageTaken;
            _cur.totalHealed += r.healApplied;
            _cur.totalShieldGained += r.shieldGained;
            // ヴェスカ段撃破: 7層ボス chain で勝った（含 swap）段を記録
            if (r.playerWon && !string.IsNullOrEmpty(rec.enemyId) && rec.enemyId.StartsWith(GameLoop.BossIds.Layer7Prefix))
                _cur.awakenedFormsKilled.Add(rec.enemyId);
            if (_cur.lastStandUsed)
            {
                _cur.combatsAfterLastStand++;
                if (r.playerWon) _cur.winsAfterLastStand++;
            }
        }

        private void OnStarvation(int dmg)
        {
            if (_cur == null) return;
            _cur.starvationTotal += dmg;
            _cur.starvationHits++;
        }

        private void OnTileActivated(TileType t)
        {
            if (_cur == null) return;
            _cur.tileVisits.TryGetValue(t, out int c);
            _cur.tileVisits[t] = c + 1;

            // 前哨基地: 層別の戦力スナップショットを採る
            if (t == TileType.Outpost) HandleOutpostArrival();
        }

        /// <summary>前哨基地に着いたときの計測。 旅団契約システムは 2026-08-11 に削除されたので、
        /// 層別戦力推移のスナップショットだけが残っている。</summary>
        private void HandleOutpostArrival()
        {
            var run = GameManager.Instance?.Run;
            if (run == null || _cur == null) return;

            int floor = run.Depth;
            if (floor > 0 && !_cur.inventoryPowerByFloor.ContainsKey(floor))
                _cur.inventoryPowerByFloor[floor] = InventoryPower.Compute(run);
        }

        // 武器強化で新 Tier に到達した瞬間に L1学習へ記録 (中間Tierの集計漏れ修正)
        private void OnWeaponTierUpgraded(string prevId, string newId)
        {
            if (_cur == null || string.IsNullOrEmpty(newId)) return;
            _cur.acquiredItemsEver.Add(newId);
            _cur.tierUpgradeCount++;
        }

        private void TrackLastStand()
        {
            var run = GameManager.Instance.Run;
            if (run == null || _cur == null) return;
            if (run.lastStandActive && !_cur.lastStandUsed)
            {
                _cur.lastStandUsed = true;
                _cur.lastStandFloor = run.Depth;
                _curLog.Enqueue($"[AutoRunner] ラストスタンド発動 (Floor {run.Depth})");
            }
        }

        // 〈昇華〉(GameLoop.SublimationSystem) は現状 BOT からも UI からも呼ばれていない。
        //   再導入するなら発動条件から設計すること。

        private void TrackEconomy()
        {
            var run = GameManager.Instance.Run;
            if (run == null || _cur == null) return;
            if (run.coins > _cur.peakCoins) _cur.peakCoins = run.coins;
            if (run.coins > _prevCoins) _cur.totalGoldGained += (run.coins - _prevCoins);
            _prevCoins = run.coins;
            // 素材収入(差分): 増加分だけ累計（昇華コスト逓増カーブ較正用の pt 基準）
            if (run.weaponMaterials > _prevMaterials) _cur.materialsGainedTotal += (run.weaponMaterials - _prevMaterials);
            _prevMaterials = run.weaponMaterials;
            // 物資(ADR-0002): 最低物資と発狂到達を追跡
            if (run.provision < _cur.minProvision) _cur.minProvision = run.provision;
            if (run.provision <= 0) _cur.reachedMadness = true;
        }

        private string CurrentNodeId()
        {
            var mm = MapManager.Instance;
            // 航行マップの層では囮罠や照明のようにターンを使わない操作がある。 点が同じでも
            //   ターンが進んでいれば進展なので、 ターンを添えてストール検出に誤判定させない。
            if (mm != null && mm.IsFreeMap) return $"{mm.CurrentNode?.id ?? ""}@{mm.Sim.Turn}";
            return mm?.CurrentNode?.id ?? "";
        }

        // ===== ラン終了処理 =====

        private void FinishClear()
        {
            var run = GameManager.Instance.Run;
            bool full = run != null && run.Depth >= run.maxFloor;
            Finish(full ? Outcome.FullClear : Outcome.NormalClear, full ? "完全クリア(7F)" : "通常クリア(5F)");
        }

        private void FinishGameOver()
        {
            var run = GameManager.Instance.Run;
            var cause = DeathCause.Unknown;
            bool bossFight = MapManager.Instance?.CurrentNode?.EffectiveType == TileType.Boss;
            string fatal = "";

            // **_lastPhaseWasCombat は Debug.Log の文字列パースで立つ**が、 バッチ実行では
            // suppressLogsDuringBatch で logEnabled=false にしているためハンドラが呼ばれず、
            // 死因が常に Unknown になっていた (2026-08-04 発覚)。 ログに依存しない判定を先に置く。
            // 直近の戦闘結果が「このランの終端で敗北」なら戦闘死とみなす。
            var lcr = GameManager.Instance.LastCombatResult;
            if (lcr.HasValue && !_lastPhaseWasCombat)
                _lastPhaseWasCombat = !lcr.Value.playerWon || run == null || run.playerHP <= 0;

            if (lcr.HasValue && (_lastPhaseWasCombat))
            {
                if (!lcr.Value.playerWon) cause = DeathCause.CombatLoss;
                else cause = DeathCause.CombatPyrrhic;
                fatal = lcr.Value.enemyDisplayName;
            }
            else if (_recentStarvation)
            {
                cause = DeathCause.Starvation;
            }
            if (_cur != null)
            {
                _cur.deathInBossFight = bossFight;
                _cur.fatalEnemy = fatal;
            }
            Finish(Outcome.GameOver, $"敗北 cause={cause}", cause);
        }

        private bool _lastPhaseWasCombat;
        private bool _recentStarvation;

        private void Finish(Outcome o, string note, DeathCause cause = DeathCause.None)
        {
            if (_cur == null) return;
            var run = GameManager.Instance.Run;
            _cur.outcome = o;
            if (cause != DeathCause.None) _cur.cause = cause;
            _cur.note = note;
            if (run != null)
            {
                _cur.reachedFloor = run.Depth;
                _cur.reached6F = run.Depth >= 6;
                // 行動台帳の締め。 **ラン 1 本につき必ず 1 行** ── G 群の合計が
                //   ラン数と一致しない場合、 どこかの終了経路がここを通っていない。
                GameLoop.RunChronicle.End(run,
                    o == Outcome.GameOver ? GameLoop.RunChronicle.EndDeath
                                          : GameLoop.RunChronicle.EndClear, null);
                RecordMapVisit();   // 最後の層の踏破
                _cur.finalHP = run.playerHP;
                _cur.finalMaxHP = run.playerMaxHP;
                _cur.finalCoins = run.coins;
                _cur.goldKeys = new string[GameLoop.GoldIncome.RunByDepthSource.Count];
                _cur.goldVals = new int[_cur.goldKeys.Length];
                { int gi = 0; foreach (var kv in GameLoop.GoldIncome.RunByDepthSource) { _cur.goldKeys[gi] = kv.Key; _cur.goldVals[gi++] = kv.Value; } }
                _cur.finalProvision = run.provision;
                // [計装 2026-11-14] 〈徒労〉の記録 (RunStats はラン開始で 0 に戻している)
                _cur.toilNothingBattles = GameLoop.RunStats.ToilNothingBattles;
                _cur.toilBlockedDrops = GameLoop.RunStats.ToilBlockedDrops;
                _cur.chestsOpened = GameLoop.RunStats.ChestsOpened;
                _cur.toilEmptyChests = GameLoop.RunStats.ToilEmptyChests;
                _cur.finalProvisionCap = run.provisionCap;
                if (run.provision <= 0) _cur.reachedMadness = true;
                _cur.provisionLateralLoss  = GameLoop.ProvisionSystem.Stats.travelLoss;
                _cur.provisionEvilLoss     = GameLoop.ProvisionSystem.Stats.evilLoss;
                _cur.deathFloor = (o == Outcome.GameOver) ? run.Depth : 0;
                _cur.appliedChallengeScore = CurrentChallengeScore();   // 難易度 × 5 (2026-11-10)
                _cur.finalInventoryPower = InventoryPower.Compute(run);
                _cur.finalMaterials = run.weaponMaterials;
                _cur.finalUpgradeCost = GameManager.CanSpendMaterials(run)
                    ? GameManager.WeaponUpgradeCost(run) : -1;
                _cur.finalUpgradeReady = _cur.finalUpgradeCost > 0
                    && run.weaponMaterials >= _cur.finalUpgradeCost;
                _cur.finalConsumableIds.Clear();
                if (run.ownedConsumables != null)
                {
                    foreach (var id in run.ownedConsumables)
                    {
                        if (string.IsNullOrEmpty(id)) continue;
                        _cur.finalConsumableIds.Add(id);
                        _cur.finalConsumableCount++;
                        // **接頭辞では分類しない** (2026-09-22) ── items.json の consFamily を読む。
                        switch (GameLoop.ItemIds.ConsFamilyOf(id))
                        {
                            case "heal": _cur.finalHealCount++; break;
                            case "dmg":  _cur.finalDamageBuffCount++; break;
                            case "def":  _cur.finalShieldCount++; break;
                            case "provision": _cur.finalProvisionCount++; break;
                        }
                    }
                }
                // 2026-06-23: 最終所持アイテム ID 集合 (保持率計算用)
                _cur.finalOwnedItemIds.Clear();
                if (!string.IsNullOrEmpty(run.equippedWeaponId)) _cur.finalOwnedItemIds.Add(run.equippedWeaponId);
                if (!string.IsNullOrEmpty(run.equippedDiceId)) _cur.finalOwnedItemIds.Add(run.equippedDiceId);
                if (run.ownedPassiveItems != null)
                    foreach (var id in run.ownedPassiveItems) _cur.finalOwnedItemIds.Add(id);
                if (run.ascendedPassiveIds != null)
                    foreach (var id in run.ascendedPassiveIds) _cur.finalOwnedItemIds.Add(id);

                // L1学習: 最終所持アイテムを acquiredItemsEver に union
                //   ・売却/消費で消えた分は別途 ShopBuy/UseItem 経由で捕捉する設計だが、
                //     現状の最小実装ではラン終了時の最終所持の和をベースラインとする
                //     （AutoRunner は売却を行わず、消費は使用するため、ここでは「使用前/購入時のスナップ」を別途用意）
                if (run.ownedPassiveItems != null)
                    foreach (var id in run.ownedPassiveItems) if (!string.IsNullOrEmpty(id)) _cur.acquiredItemsEver.Add(id);
                if (run.ownedConsumables != null)
                    foreach (var id in run.ownedConsumables) if (!string.IsNullOrEmpty(id)) _cur.acquiredItemsEver.Add(id);
                if (!string.IsNullOrEmpty(run.equippedWeaponId)) _cur.acquiredItemsEver.Add(run.equippedWeaponId);
                // 装備ダイスは 2026-08-17 に廃止 (equippedDiceId はセーブ互換の残置)。
                // **所持した出目パーツを学習へ載せる。** 購入時にも Buy() が積むが、
                //   イベント等ショップ以外の入手経路が付いたときに取りこぼさないよう最終所持でも union する。
                if (run.diceFaceParts != null)
                    foreach (var p in run.diceFaceParts) _cur.acquiredItemsEver.Add(GameLoop.DiceFaceParts.Id(p));
                _cur.finalWeaponTier = run.equippedWeaponId ?? "";
                _cur.finalLimitBreak = run.limitBreakStage;
                _cur.totalCoinsSpent = run.coinsSpent;
                _cur.woundTriggers = run.lingeringWoundTriggers;
                _cur.woundHpLost = run.lingeringWoundLost;
            }
            // 指紋を確定する。 **Classify より前**に置かない ── 分類は指紋に含めない
            //   (帯ラベルの定義を変えただけで指紋が動くと、 決定性の検査にならない)。
            unchecked
            {
                int h = 17;
                h = h * 31 + _cur.reachedFloor;
                h = h * 31 + _cur.finalHP;
                h = h * 31 + _cur.finalMaxHP;
                h = h * 31 + _cur.finalCoins;
                h = h * 31 + _cur.totalCoinsSpent;
                h = h * 31 + _cur.totalCombats;
                h = h * 31 + _cur.totalWins;
                h = h * 31 + _cur.shopPurchases;
                h = h * 31 + _cur.finalInventoryPower;
                h = h * 31 + (int)_cur.outcome;
                _cur.fingerprint = h;
            }
            Classify(_cur);
            _cur.bandScore = ComputeBandScore(_cur);
            // [計装 2026-09-17] **ラン終了時の残金**。 クリア/死亡で分けて数える。
            //   店を出る基準は「リロール価格 + SurplusBuyReserve(30) を切ったら止める」で、
            //   リロール価格は 5→10→20→30 と逓増する ＝ **構造的に数十Gは残る**。
            //   支出を減らす軸 (商才) と現金を配る軸 (金庫) の価値差を読むのに要る ──
            //   終了時に余っているなら、 節約は現金より価値が低くて当然。
            NoteEndGold(run != null ? run.coins : 0, _cur.bandScore >= 11);
            if (_lastShopExitSeen && run != null)
            {
                ShopExitRuns++;
                ShopExitCoinsSum += _lastShopExitCoins;
                PostShopGainSum += run.coins - _lastShopExitCoins;
                // [計装 2026-09-18] **クリア/非クリアで分ける。** 全ラン平均と「クリア時残金」を
                //   並べると母集団が違うのに前後比較に見える (243G → 449G と読まれた)。
                int k = _cur.bandScore >= 11 ? 0 : 1;
                ExitByOutcomeN[k]++; ExitByOutcomeCoins[k] += _lastShopExitCoins;
                ExitByOutcomeGain[k] += run.coins - _lastShopExitCoins;
                ExitByOutcomeFloor[k] += _lastShopExitFloor;
                if (_floor7ShopEntryCoins >= 0) { Floor7EntryN[k]++; Floor7EntryCoins[k] += _floor7ShopEntryCoins; }
            }
            _lastShopExitSeen = false; _lastShopExitCoins = 0; _lastShopExitFloor = 0; _floor7ShopEntryCoins = -1;
            _cur.deterministicDigest = ComputeRunDeterministicDigest(_cur, run);
            // L2ペアテスト記録
            _cur.policyVariant = _currentRunVariant;
            _cur.pairedSeed = _currentRunSeed;
            _records.Add(_cur);
            RecordScreenRunCompletion(_cur);
            _curLog.Enqueue($"[AutoRunner] === RUN {_cur.index} 終了: {_cur.band} ({_cur.bandLabel}) — {note} ===");
            _curAttachLog(_cur);
            _cur = null;
            ItemSystem.PassiveSkills.Effects.Daybreak.NextAttackPctPerUnit = 0f;   // [2026-11-10 試験] ラン終了で既定へ戻す
            GameLoop.GameHooks.WeaponTierCap = 0;
            GameLoop.GameHooks.PursuerNoReward = false;                          // [測定 2026-11-12・#35]
            GameLoop.GameHooks.PursuerAtkMulOverride = 0f;
            GameLoop.GameHooks.PursuerHpAdd = 0f;
            GameLoop.GameHooks.PursuerSupplyOverride = -1;
            GameLoop.GameHooks.PursuerLeavesTerritory = false;                  // [測定 2026-11-12] 縄張りの作りを既定へ
            GameLoop.GameHooks.TerritoryTreasure = GameLoop.TerritoryTreasureKind.Forge;
            GameLoop.GameHooks.TerritoryKeepBypass = true;                       // [測定 2026-11-13] 縄張りを避ける道の保証を既定へ
            GameLoop.GameHooks.AshKingBeatenEver = null;                         // [測定 2026-11-13] 灰燼の王を倒した記録の扱いを既定 (ランの外の記録を読む) へ
            GameLoop.GameHooks.ToilMode = EventSystem.TimedEffects.ToilMode.Stacks;   // [測定 2026-11-14] 〈徒労〉の形を既定 (今の形) へ
            GameLoop.GameHooks.ToilChestEmptyPct = EventSystem.TimedEffects.BattleBoonRule.DefaultChestEmptyPct;
            GameLoop.GameHooks.StormEnabled = true;                              // [測定 2026-11-13] 天候の作りを既定 (嵐) へ
            GameLoop.GameHooks.StormClock = MapSystem.FreeMove.StormClock.Instability;   // [測定 2026-11-13 二版] 嵐の時計を既定 (不安定化) へ
            GameLoop.GameHooks.TerminalHealMulOverride = 0;                      // [測定 2026-11-12・#102]
            GameLoop.GameHooks.TerminalArresterNeedOverride = 0;
            GameLoop.GameHooks.TerminalFirstAidLeavesRunDeck = false;
            GameLoop.GameHooks.TerminalKillBonusNeedsAttack = false;            // [測定 2026-11-13・#117]
            GameLoop.GameHooks.HandCarry = CombatSystem.HandCarryMode.Pick;     // [測定 2026-11-13] 手札の規則を既定 (Pick) へ
            GameLoop.GameHooks.BossDownDisabled = false;                        // [測定 2026-11-13] ボスのダウンを既定 (あり) へ
            GameLoop.GameHooks.Depth3HeavyDisabled = false;                     // [測定 2026-11-13] 深さ 3 のボスの大技を既定 (あり) へ
            GameLoop.GameHooks.VescaClockHeldAtStart = false;                   // [測定 2026-11-13] 博士の時計を既定 (預からない) へ
            GameLoop.GameHooks.VescaDiceForce = 0;               // [測定 2026-11-11] 武器の段の上限を外す
            CombatSystem.EnemyHpLedger.ProbeMainMult = 1f;      // [測定用の物差し 2026-11-10] ラン終了で既定 (1) へ戻す
            CombatSystem.EnemyHpLedger.ProbePassiveMult = 1f;
        }

        /// <summary>ラン 1 本の決定的な指紋 (同じシードの突き合わせ・Tools/digest_cmp.py)。
        /// 先頭の印 <c>ultra-production-run-v1</c> は旧 Ultra の名残だが、 <b>変えると過去のラン・並列ワーカーの digest と
        /// 全件が食い違う</b>ので残す (2026-11-13・#135)。</summary>
        private static string ComputeRunDeterministicDigest(RunRec rec, GameLoop.RunState run)
        {
            var sb = new StringBuilder(2048);
            sb.Append("ultra-production-run-v1\n");
            sb.Append(rec.index).Append('|').Append((int)rec.outcome).Append('|')
              .Append(rec.reachedFloor).Append('|').Append(rec.finalHP).Append('|')
              .Append(rec.finalMaxHP).Append('|').Append(rec.finalCoins).Append('|')
              .Append(rec.totalCoinsSpent).Append('|').Append(rec.totalCombats).Append('|')
              .Append(rec.totalWins).Append('|').Append(rec.finalInventoryPower).Append('|')
              .Append(rec.appliedChallengeScore).Append('|').Append(rec.fingerprint).Append('\n');
            if (run?.chronicle != null)
            {
                for (int i = 0; i < run.chronicle.Count; i++)
                {
                    string entry = run.chronicle[i] ?? "";
                    sb.Append(entry.Length).Append(':').Append(entry).Append('\n');
                }
            }
            return Sha256Hex(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        private static string Sha256File(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return ToLowerHex(sha.ComputeHash(stream));
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return ToLowerHex(sha.ComputeHash(bytes ?? Array.Empty<byte>()));
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        /// <summary>L1学習用: 帯ラベルから数値スコアを返す。
        /// CRASH=-1, DEADLOCK=-2, R1a..R11 を 1..11 にマップ（先頭文字 R+数字部）。 段の表は <see cref="Classify"/> の上。</summary>
        private int ComputeBandScore(RunRec r)
        {
            if (string.IsNullOrEmpty(r.band)) return 0;
            if (r.band == "CRASH") return -1;
            if (r.band == "DEADLOCK") return -2;
            // "R11" → 11, "R10b" → 10 等。 小文字 a/b/c/d は細分しない (数字部だけ読む)。
            int v = 0; int i = 1;
            while (i < r.band.Length && char.IsDigit(r.band[i])) { v = v * 10 + (r.band[i] - '0'); i++; }
            return v;
        }

        private readonly Dictionary<int, List<string>> _detail = new Dictionary<int, List<string>>();
        private void _curAttachLog(RunRec r)
        {
            var list = new List<string>(_curLog);
            _detail[r.index] = list;
        }

        /// <summary>結果を帯 + CRASH/DEADLOCK に分類。 f は Run.Depth (深さ 0/1/3/5/6/7/8 = 画面 序層/1/2/3/4/5/5 の最終段)。
        /// <code>
        /// 帯    点  条件
        /// R1a    1  深さ 1 以下の道中で死亡 (深さ 0 の序層・不明の深さも)
        /// R1b    1  深さ 1 のボス戦で死亡
        /// R1c/d  1  深さ 2 (今は無い層)
        /// R2     2  深さ 3 (画面 第 2 層) の道中で死亡
        /// R3     3  深さ 3 のボス戦で死亡
        /// R4/R5  4/5 深さ 4 (今は無い層)
        /// R6     6  深さ 5 (画面 第 3 層) の道中で死亡
        /// R7     7  深さ 5 のボス戦で死亡
        /// R8     8  深さ 5 を倒して生きて終了 (〈決意〉が無い・第 3 層の結末)
        /// R9     9  深さ 6 (画面 第 4 層・灰燼の王) で死亡 (道中もボス戦も)
        /// R10b  10  深さ 6 を倒して生きて終了 (〈真理〉が無い・第 4 層の結末)  ← 2026-11-13 #137 で R8b (=8) から上げた
        /// R10   10  深さ 7 以上 (画面 第 5 層・Null Point のヴェスカ) で死亡
        /// R11   11  ヴェスカ撃破 (完全クリア)。 「クリア」の数え方 bandScore &gt;= 11 はこれだけ
        /// </code>
        /// <para><b>R10b (#137)。</b> 旧 R8b は 8 点で、 灰燼の王に負けた R9 (9 点) より低かった。 生きて第 4 層の結末へ着いたランは
        /// 灰燼の王を越えているので R9 より上、 ヴェスカは倒していないので R11 より下の 10 点 (R10 と同点) に置く。
        /// <b>既定の測定 (ashKingBeatenEver = True) ではこの帯に入るランは無い</b>: 深さ 6 は〈決意〉を持って入る層で、
        /// 最初に踏んだイベントマスで〈灰に埋もれた基地〉が起き (踏まずに終点へ着いても安全弁がボス戦の前に出す・GameManager.StartBossTileOrAshBase)、
        /// 倒したことがある記録では再訪の版になる。 BOT は必ず手記の最後の頁 (効果「真理を得る」) を開くので (PickSafeChoice・アブレーションでも潰さない)
        /// 〈真理〉が立ち、 HandleFloorClear の「深さ 6 で〈真理〉なし」の終了を通らない。
        /// イベントなしの測定 (SuppressEvents / SuppressConvictionChain) は深さ 6 に入る時に〈真理〉を配る。
        /// 入るのは「はじめて」の腕 (ashKingBeatenEver = False・基地は〈徒労〉の 1 択) で深さ 6 のボスを倒したランだけ。</para></summary>
        private void Classify(RunRec r)
        {
            if (r.outcome == Outcome.Crash) { r.band = "CRASH"; r.bandLabel = "クラッシュ(例外)"; return; }
            if (r.outcome == Outcome.Deadlock) { r.band = "DEADLOCK"; r.bandLabel = "デッドロック"; return; }
            if (r.outcome == Outcome.FullClear)
            {
                r.band = "R11"; r.bandLabel = "Null Pointクリア(完全クリア)";
                return;
            }
            if (r.outcome == Outcome.NormalClear)
            {
                // 深さ 6 クリア (〈真理〉未所持で深さ 7 へ進めない) と 深さ 5 クリア (〈決意〉未所持で深さ 6 へ進めない) を区別。
                //   深さ 6 クリアは灰燼の王を越えているので R9 (深さ 6 で死亡) より上の R10b (#137・旧 R8b = 8)。
                if (r.reachedFloor >= 6) { r.band = "R10b"; r.bandLabel = "第4層クリアで終了(真理未所持)"; }
                else                     { r.band = "R8"; r.bandLabel = "5Fクリア(決意未所持)"; }
                return;
            }

            // GameOver
            int f = r.deathFloor;
            bool boss = r.deathInBossFight;
            // **7 層では死なない (2026-09-14)。** 戦闘マスが無く、 門も HP を 0 にしない。
            //   死ぬなら 8 層 Null Point のヴェスカ戦。 7 層が出たら異常なので
            //   同じ帯へ畳まず、 到達したこと自体は R10 として残す。
            if (f >= 7) { r.band = "R10"; r.bandLabel = "Null Pointで死亡(門を通過)"; return; }
            if (f >= 6) { r.band = "R9"; r.bandLabel = "6Fボスで死亡"; return; }
            switch (f)
            {
                case 1:
                    if (boss) { r.band = "R1b"; r.bandLabel = "1Fボスで死亡"; }
                    else      { r.band = "R1a"; r.bandLabel = "1F道中で死亡"; }
                    break;
                case 2:
                    if (boss) { r.band = "R1d"; r.bandLabel = "2Fボスで死亡"; }
                    else      { r.band = "R1c"; r.bandLabel = "2F道中で死亡"; }
                    break;
                case 3:
                    if (boss) { r.band = "R3"; r.bandLabel = "3Fボスで死亡"; }
                    else      { r.band = "R2"; r.bandLabel = "3F道中で死亡"; }
                    break;
                case 4:
                    if (boss) { r.band = "R5"; r.bandLabel = "4Fボスで死亡"; }
                    else      { r.band = "R4"; r.bandLabel = "4F道中で死亡"; }
                    break;
                case 5:
                    if (boss) { r.band = "R7"; r.bandLabel = "5Fボスで死亡"; }
                    else      { r.band = "R6"; r.bandLabel = "5F道中で死亡"; }
                    break;
                default:
                    r.band = "R1a"; r.bandLabel = "1F道中で死亡"; break;
            }
        }

        // ===== ログ購読 =====

        /// <summary>バッチ中のログ抑止を**貫通して必ず出す**ログ。
        ///
        /// 2026-08-11: 各所で `logEnabled = true` に切り替えて出す書き方をしていたが、
        /// 抑止の実装を `filterLogType = Error` に変えた途端に**全部黙った**
        /// (logEnabled を戻しても filterLogType が Log を弾く)。 抑止の掛け方と
        /// 貫通の仕方が 2 箇所に分かれていると、 片方を直したときに必ずこうなる。
        /// **貫通はこの 1 関数だけが知っている**形にして、 二度と分岐させない。</summary>
        private static void LogAlways(string msg, LogType type = LogType.Log)
        {
            var lg = Debug.unityLogger;
            bool pe = lg.logEnabled;
            var pf = lg.filterLogType;
            lg.logEnabled = true;
            lg.filterLogType = LogType.Log;
            if (type == LogType.Warning) Debug.LogWarning(msg);
            else if (type == LogType.Error) Debug.LogError(msg);
            else Debug.Log(msg);
            lg.logEnabled = pe;
            lg.filterLogType = pf;
        }

        private void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception)
            {
                _exceptionFlag = true;
                _exceptionMsg = condition;
            }
            if (_cur == null) return;
            if (condition != null && condition.StartsWith("[GameManager]"))
            {
                AddLog(condition);

                // 死因推定の補助
                if (condition.Contains("Phase:"))
                    _lastPhaseWasCombat = condition.Contains("Combat") || condition.Contains("BattleResult");
                if (condition.Contains("空腹ダメージ"))
                    _recentStarvation = true;
                else if (condition.Contains("Phase:") && !condition.Contains("GameOver"))
                    _recentStarvation = false;
            }
            else if (condition != null && condition.StartsWith("[EventEncounter]"))
            {
                AddLog(condition);
                // 「[EventEncounter] 開始: <名> (id=<id>)」からイベント識別子を保持
                if (condition.Contains("開始:"))
                    _lastEventInfo = condition.Substring(condition.IndexOf("開始:"));
            }
            else if (condition != null && condition.StartsWith("[DBG]"))
            {
                AddLog(condition); // 一時トレース（原因特定後に削除）
            }
            else if (type == LogType.Exception || type == LogType.Error)
            {
                AddLog($"[{type}] {condition}");
            }
        }

        /// <summary>リングバッファ追記。上限超過時は先頭(古い行)を捨て、末尾の終端ログを必ず残す。
        /// Queue による O(1) Dequeue で 10000ラン規模でも線形時間を維持する。</summary>
        private void AddLog(string line)
        {
            if (_curLog.Count >= detailMaxLinesPerRun && _curLog.Count > 0)
                _curLog.Dequeue();
            _curLog.Enqueue(line);
        }

        // ===== 初期化補助 =====

        private void SafeInitDatabases()
        {
            try { var _ = ItemDatabase.Instance; } catch (Exception e) { Debug.LogWarning($"[AutoRunner] ItemDatabase: {e.Message}"); }
            try { EnemyDatabase.EnsureInitialized(); } catch (Exception e) { Debug.LogWarning($"[AutoRunner] EnemyDatabase: {e.Message}"); }
        }


        // ===== ログ出力 =====

        private string ResolveAutoRunOutputRoot()
        {
            string root = !string.IsNullOrWhiteSpace(productionOutputRootOverride)
                ? productionOutputRootOverride
                : Path.Combine(Application.dataPath, "..", "AutoRunLogs");
            root = Path.GetFullPath(root);
            Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>残すバッチ出力の件数。 <c>Tools/prune_outputs.py</c> の <c>KEEP_RUNS</c> と揃えること。</summary>
        private const int KeepBatchDirs = 20;

        /// <summary>古い <c>batch_*</c> を刈る (2026-09-22)。
        ///
        /// <para><b>バッチは実行のたびにディレクトリを 1 つ作り、 何も消さなかった。</b>
        /// 2026-09-22 時点で 537 個・862MB。 git 側は .gitignore で除外済みなので、
        /// ここはディスク側の上限を保つ役。 スイープ側は <c>Tools/prune_outputs.py</c> が同じことをする。</para>
        ///
        /// <para>新しさは更新時刻で判定する。 いま作ったディレクトリは必ず残す。
        /// 失敗してもバッチ本体は止めない (刈り込みは副業)。</para></summary>
        private static void PruneOldBatchDirs(string root, string justCreated)
        {
            try
            {
                var dirs = new List<DirectoryInfo>(new DirectoryInfo(root).GetDirectories("batch_*"));
                dirs.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                string keep = Path.GetFullPath(justCreated);
                for (int i = KeepBatchDirs; i < dirs.Count; i++)
                {
                    if (string.Equals(dirs[i].FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    dirs[i].Delete(true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoRunner] 古いバッチ出力の削除に失敗 (続行): {e.Message}");
            }
        }
    }
}

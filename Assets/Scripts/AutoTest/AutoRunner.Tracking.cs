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
using AutoTest.Ultra;

namespace AutoTest
{
    /// <summary>AutoRunner: 計測フック・ラン終了処理・ログ購読・初期化補助。</summary>
    public partial class AutoRunner
    {
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
            // 次フォーム開始時点の現在HP。チェーン中は CombatManager のライブHPを使う
            // （Run.playerHP は戦闘終了まで更新されないため、これが無いと各フォームの被ダメが常に0と誤計測される）。
            _pendingEnemyHpBefore = (cm != null && cm.IsCombatActive) ? cm.PlayerHP : (gm?.Run?.playerHP ?? 0);
            _cwWin = _cwDraw = _cwLoss = _cwLossAbs = 0; // 新戦闘のターン内訳リセット
        }

        private void OnBattleEnded(CombatResult r)
        {
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
                enemyMaxHP = r.enemyMaxHP,
                weaponId = gm?.Run?.equippedWeaponId ?? "",
                diceId = gm?.Run?.equippedDiceId ?? "",
                deathCause = r.deathCause,
                playerRollSum = r.playerRollSum,
                playerRollCount = r.playerRollCount,
                playerDamageBySource = r.playerDamageBySource,
                strongRollTurns = r.strongRollTurns,
                strongRollBossWins = r.strongRollBossWins,
                weakRollTurns = r.weakRollTurns,
                weakRollBossWins = r.weakRollBossWins,
                playerMaxHpEnd = gm?.Run?.playerMaxHP ?? 0,
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
            // 航行マップの層では囮罠や照明のように手番を使わない操作がある。 点が同じでも
            //   手番が進んでいれば進展なので、 手番を添えてストール検出に誤判定させない。
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
                // 刻限を越えたランだけの到達層分布。 深い側に寄っていれば勝ち筋を
                //   削れており、 浅い側なら「負けるランを罰しているだけ」。
                MetaProgression.MetaDebuffApplicator.NoteRunEnd(run, run.Depth, o == Outcome.GameOver);
                // 行動台帳の締め。 **ラン 1 本につき必ず 1 行** ── G 群の合計が
                //   ラン数と一致しない場合、 どこかの終了経路がここを通っていない。
                GameLoop.RunChronicle.End(run,
                    o == Outcome.GameOver ? GameLoop.RunChronicle.EndDeath
                                          : GameLoop.RunChronicle.EndClear, null);
                _cur.finalHP = run.playerHP;
                _cur.finalMaxHP = run.playerMaxHP;
                _cur.finalCoins = run.coins;
                _cur.finalProvision = run.provision;
                _cur.finalProvisionCap = run.provisionCap;
                if (run.provision <= 0) _cur.reachedMadness = true;
                _cur.provisionCombatLoss   = GameLoop.ProvisionSystem.Stats.combatLoss;
                _cur.provisionComposureGain = GameLoop.ProvisionSystem.Stats.composureGain;
                _cur.provisionLateralLoss  = GameLoop.ProvisionSystem.Stats.travelLoss;
                _cur.provisionMarchLoss    = GameLoop.ProvisionSystem.Stats.marchLoss;
                _cur.provisionEvilLoss     = GameLoop.ProvisionSystem.Stats.evilLoss;
                _cur.provisionFoodGain     = GameLoop.ProvisionSystem.Stats.foodGain;
                _cur.provisionRerollLoss   = GameLoop.ProvisionSystem.Stats.rerollLoss;
                _cur.deathFloor = (o == Outcome.GameOver) ? run.Depth : 0;
                _cur.appliedChallengeScore = MetaProgression.MetaDebuffApplicator.Score;
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
            var productionRecord = new UltraProductionRunRecord
            {
                runOrdinal = _ultraProductionRuns.Count,
                runIndex = _cur.index,
                scenarioSeed = _currentUltraProductionScenarioSeed,
                scenarioSeedHex = _currentUltraProductionScenarioSeed.ToString("x16"),
                valid = _cur.bandScore >= 0,
                fullClear = _cur.bandScore >= 11,
                crash = _cur.outcome == Outcome.Crash,
                deadlock = _cur.outcome == Outcome.Deadlock,
                fingerprint = _cur.fingerprint,
                deterministicDigest = _cur.deterministicDigest,
            };
            _ultraProductionRuns.Add(productionRecord);
            try { UltraProductionRunCompleted?.Invoke(productionRecord); }
            catch (Exception ex)
            {
                Debug.LogError("[AutoRunner][UltraWorker] run completion callback failed: " + ex.Message);
            }
            // L2ペアテスト記録
            _cur.policyVariant = _currentRunVariant;
            _cur.pairedSeed = _currentRunSeed;
            _records.Add(_cur);
            NoteFamilyTierRunEnd();
            AutoTest.Ultra.UltraDecisionCensus.NoteRunCompleted();
            RecordScreenRunCompletion(_cur);
            _curLog.Enqueue($"[AutoRunner] === RUN {_cur.index} 終了: {_cur.band} ({_cur.bandLabel}) — {note} ===");
            _curAttachLog(_cur);
            _cur = null;
        }

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
        /// CRASH=-1, DEADLOCK=-2, R1a..R10/R11 を 1..11 にマップ（先頭文字 R+数字部）。</summary>
        private int ComputeBandScore(RunRec r)
        {
            if (string.IsNullOrEmpty(r.band)) return 0;
            if (r.band == "CRASH") return -1;
            if (r.band == "DEADLOCK") return -2;
            // "R11" → 11, "R8b" → 8 等
            int v = 0; int i = 1;
            while (i < r.band.Length && char.IsDigit(r.band[i])) { v = v * 10 + (r.band[i] - '0'); i++; }
            // 小文字 a/b で 0.5 単位の細分はしないが、6Fクリアは R8b(=8) のまま、5Fクリアは R8(=8) で同点
            // 7層クリア(R11)が最高。死亡 R1a..R10。
            return v;
        }

        private readonly Dictionary<int, List<string>> _detail = new Dictionary<int, List<string>>();
        private void _curAttachLog(RunRec r)
        {
            var list = new List<string>(_curLog);
            _detail[r.index] = list;
        }

        /// <summary>結果を10段階バンド + CRASH/DEADLOCK に分類。
        /// R1:2F以前(道中/ボス問わず) R2:3F道中 R3:3Fボス R4:4F道中 R5:4Fボス
        /// R6:5F道中 R7:5Fボス R8:5Fクリア R9:6Fボスで死亡 R10:6層クリア。</summary>
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
                // 6F クリア (〈真理〉未所持で 7F 進入不可) と 5F クリア (〈決意〉未所持で 6F 進入不可) を区別
                if (r.reachedFloor >= 6) { r.band = "R8b"; r.bandLabel = "6Fクリア(真理未所持)"; }
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

        private void DisableMapTransition()
        {
            try
            {
                var t = MapSystem.Visual.MapTransitionController.Instance;
                if (t != null)
                {
                    Destroy(t.gameObject);
                    Debug.Log("[AutoRunner] MapTransitionController を無効化(同期ショップ遷移)");
                }
            }
            catch { /* 型が無い/未配置なら無視 */ }
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

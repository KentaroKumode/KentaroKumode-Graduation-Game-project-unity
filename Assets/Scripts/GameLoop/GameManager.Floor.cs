using System;
using System.Collections.Generic;
using UnityEngine;
using Codex;
using CombatSystem;
using EventSystem;
using ItemSystem;
using ItemSystem.PassiveSkills;
using ItemSystem.Shop;
using MapSystem;

namespace GameLoop
{
    /// <summary>GameManager: 層の出入り・マスの起動・戦闘終了の処理・交換マス・ボス・層クリア。</summary>
    public partial class GameManager
    {
        /// <summary>フロアクリア確認→次フロアへ</summary>
        public void ConfirmFloorClear()
        {
            if (CurrentPhase != GamePhase.FloorClear) return;

            // 行動台帳: 層の踏破。 **戦闘側の A 群とは別物** ── あちらは 1 戦闘 1 行で、
            //   こちらは「その層を抜けた」という節目。 ダイジェストの章の切れ目になる。
            RunChronicle.End(Run, RunChronicle.EndKill, null);

            // 挑戦デバフ〈通行料〉: **層を移動するたびに所持金の N% を失う**。
            //   定額ではなく割合なので、 貯め込むほど損が大きい ── 「いま買うか、
            //   次の店まで待つか」という判断を作るのが狙い (実測で BOT は平均 57.9G 使い残す)。
            //   **層を跨ぐ瞬間に取る。** クリア報酬の後・次層の入場前。
            {
                float toll = MetaProgression.MetaDebuffApplicator.GetFloorTollRatio();
                if (toll > 0f && Run.coins > 0)
                {
                    int paid = Mathf.CeilToInt(Run.coins * toll);
                    Run.coins = Mathf.Max(0, Run.coins - paid);
                    Log($"通行料: {Run.currentFloor}層の関所で -{paid}G (残 {Run.coins}G)");
                }
            }

            if (!Run.AdvanceFloor())
            {
                ReturnToTitle();
                return;
            }
            EnterFloor();
        }

        /// <summary>タイトルに戻る</summary>
        public void ReturnToTitle()
        {
            if (Run != null && Run.isRunActive)
                Run.EndRun();

            CurrentEnemy = null;
            LastCombatResult = null;
            SetPhase(GamePhase.Title);
        }

        // ============================================================
        //  内部処理
        // ============================================================

        /// <summary>フロアに入る（マップ生成、前哨基地処理、デバフ適用）</summary>
        private void EnterFloor()
        {
            MetaProgression.Achievements.AchievementService.NoteFloorEntered(Run?.Depth ?? 0);
            Run.tilesThisFloor = 0;   // 軸14〈焦燥〉の層別カウンタ
            var mm = MapManager.Instance;

            // 契約システム: 前層終了 → 新層開始 hook (この呼び順は重要)
            // 前層終了で 商業連合隊の層末収入支払い、 サーカスフラグクリア等が行われる
            // [廃止] 旅団契約の層フック (2026-08-11 削除)

            // メタ: トークン獲得 + 1層/3層で固有恒久デバフ抽選
            MetaProgression.MetaTokenEarner.OnFloorReached(Run.Depth);
            // T4-E〈最後の審判〉: 3層・5層で大罪を付与 (§15-2 v3.0)。 v1 の 1層付与は廃止。
            if (Run.Depth == 3) MetaProgression.MetaDebuffApplicator.TryGrantOnFloor3(Run);
            if (Run.Depth == 5) MetaProgression.MetaDebuffApplicator.TryGrantOnFloor5(Run);

            // T4-A〈破綻〉: 各層突入時、 HP を最大HPの 70% 以下へ切り詰める (§15-2 v3.0)。
            //   既にそれ以下なら何もしない ── 回復ではなく「上限の切り下げ」。
            float hpCap = MetaProgression.MetaDebuffApplicator.GetFloorEntryHpCapRatio();
            if (hpCap < 1f)
            {
                int cap = Mathf.Max(1, Mathf.FloorToInt(Run.playerMaxHP * hpCap));
                if (Run.playerHP > cap)
                {
                    Log($"【破綻】層突入: HP {Run.playerHP} → {cap} (最大の{hpCap:P0}へ切詰め)");
                    Run.playerHP = cap;
                }
            }

            // 恒久デバフ「ムシュファの強欲」: 5層突入時に所持ゴールド0
            if (Run.Depth == 5 && MetaProgression.PermanentDebuffEffects.HasGreed(Run))
            {
                Log($"恒久デバフ {MetaProgression.PermanentDebuffIds.Greed}: 所持ゴールド {Run.coins}→0");
                Run.coins = 0;
            }

            // 層突入のパッシブ (2026-09-15)。 **層デバフより前に鳴らす** ── 〈ムシュファの強欲〉が
            //   5 層で所持金を 0 にするので、 後ろに置くと配ったゴールドが同じ層で消える。
            ItemSystem.PassiveItems.PassiveItemManager.OnFloorEnter(Run);

            // 層デバフ取得
            ActiveModifier = FloorModifierDatabase.Get(Run.Depth);

            // マップ生成（空腹度上限はModifierで調整）
            mm.GenerateFloor(Run.Depth);

            // 前哨基地に立った時点で、 最初の分岐ぶんの戦闘名を開示する。
            RevealAdjacentEncounters();

            // 層タイトル表示（確定文言の正本: docs/GAME.md §5。ビジュアルは未実装、OnShow を UI が購読）
            var layerIntro = LayerTitles.ForFloor(Run.Depth);
            Log($"=== {layerIntro.title} ===　{layerIntro.subtitle}");
            LayerTitles.Raise(layerIntro);

            // 前哨基地は開始ノードのため OnTileActivated イベントが ActivateTile 経由で発火しない。
            // 旅団契約 AI (AutoRunner) 等のリスナーへ明示的に通知する。
            OnTileActivated?.Invoke(TileType.Outpost);

            // 2026-05-31 v3: 前哨基地で武器強化を1回まで自動実行 (素材があり、 強化可能なら)。
            // HPゲート無し (前哨基地は安全)。 BOT/プレイヤー共通の自動挙動 (UI 開発まで暫定)。
            // 各層 1 回制限は OutpostUpgrade 側で outpostUpgradeUsedThisFloor フラグ管理。
            OutpostUpgrade();

            // 挑戦デバフ〈補給断絶〉: 前哨基地の回復量 ×0.80 / ×0.60 / ×0.40 (§15-2 v3.0)。
            //   **v3.0 は前哨基地を消さない** ── 低効率でも経路を残す (challenge-debuff-plan §3)。
            //   T4-A〈破綻〉の全回復−25% もここに乗る。
            //   〈浅い眠り〉は倍率ではなく**回復後 HP の上限**として ApplyHealSpotCap で掛かる。
            float healMult = MetaProgression.MetaDebuffApplicator.GetHealMultiplier();

            // 挑戦デバフ〈宿屋連合〉(§15-2 v4.0): 前哨基地で休むたびに GOLD を払う。
            //   **回復を止めずに値段を付ける** ── 経路を消さないのが v3.0 以来の制約。
            {
                int toll = MetaProgression.MetaDebuffApplicator.GetRestGoldCost();
                if (toll > 0 && Run.coins > 0)
                {
                    int paid = Mathf.Min(toll, Run.coins);
                    Run.coins -= paid;
                    Log($"宿屋連合: 前哨基地の宿代 -{paid}G (残 {Run.coins}G)");
                }
            }

            if (ActiveModifier != null && ActiveModifier.maxHPBonusFlat != 0)
            {
                // 6層前哨基地: HP全回復 + MaxHP+5
                Run.playerMaxHP += ActiveModifier.maxHPBonusFlat;
                int beforeHP = Run.playerHP;
                int missing = Mathf.Max(0, Run.playerMaxHP - Run.playerHP);
                int healAmount = Mathf.CeilToInt(missing * healMult);
                healAmount = MetaProgression.MetaDebuffApplicator.ApplyJudgmentHealReduction(healAmount, Run);
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + healAmount);
                Run.playerHP = MetaProgression.MetaDebuffApplicator.ApplyHealSpotCap(Run.playerHP, Run.playerMaxHP, beforeHP);
                MetaProgression.MetaDebuffApplicator.NoteHeal(healAmount, Run.playerHP - beforeHP, Run);
                RunChronicle.Rest(Run, Run.playerHP - beforeHP, beforeHP, Run.playerMaxHP);
                Log($"前哨基地: MaxHP+{ActiveModifier.maxHPBonusFlat} → {Run.playerMaxHP}, HP+{Run.playerHP - beforeHP} → {Run.playerHP}");
            }
            else if (Run.Depth == 8)
            {
                // **8層 (Null Point) に前哨基地の回復は無い。** ここは門が転移させた先の一点で、
                //   野営できる場所ではない。 補給は 7 層 (店 → 休憩 → 門) で終わっている。
                //   回復を入れると、 門で払った最大HP −25% を回復側で埋め戻せてしまい、
                //   代償が「HP を削られた状態で入るかどうか」に化ける。
                Log("Null Point: 転移した先に、休める場所は無い");
            }
            else if (Run.Depth == 7)
            {
                // 7層前哨基地: 最終戦に挑む者への餞別。HP全回復（挑戦デバフの倍率は乗る）
                int beforeHP = Run.playerHP;
                int missing = Mathf.Max(0, Run.playerMaxHP - Run.playerHP);
                int healAmount = Mathf.CeilToInt(missing * healMult);
                healAmount = MetaProgression.MetaDebuffApplicator.ApplyJudgmentHealReduction(healAmount, Run);
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + healAmount);
                Run.playerHP = MetaProgression.MetaDebuffApplicator.ApplyHealSpotCap(Run.playerHP, Run.playerMaxHP, beforeHP);
                MetaProgression.MetaDebuffApplicator.NoteHeal(healAmount, Run.playerHP - beforeHP, Run);
                RunChronicle.Rest(Run, Run.playerHP - beforeHP, beforeHP, Run.playerMaxHP);
                Log($"前哨基地(7F): HP+{Run.playerHP - beforeHP} → {Run.playerHP}/{Run.playerMaxHP}");
            }
            else
            {
                // 通常前哨基地は「**最大HPの30%**」回復 (2026-08-11 変更・ceil)。
                //   旧「不足HPの30%」は計装で **1 回あたり 6.1 HP** しか戻していなかった
                //   (最大HP 93 に対し 6.5%)。 HP が高いほど回復量が小さくなる式なので、
                //   「削られる前に寄る」が無意味になり、 回復スポットという要素が死んでいた。
                //   1 ラン 6.4 回も寄って合計 39 HP ── 消耗品 9.96 個の前では誤差だった。
                //   最大HP 基準なら 28 HP 前後になり、 経路として意味を持つ。
                //   6F/7F の特殊前哨基地 (全回復+MaxHP+5 / 全回復) は別経路。
                int healAmount = Mathf.CeilToInt(Run.playerMaxHP * 0.30f * healMult);
                healAmount = MetaProgression.MetaDebuffApplicator.ApplyJudgmentHealReduction(healAmount, Run);
                int beforeHP = Run.playerHP;
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + healAmount);
                Run.playerHP = MetaProgression.MetaDebuffApplicator.ApplyHealSpotCap(Run.playerHP, Run.playerMaxHP, beforeHP);
                MetaProgression.MetaDebuffApplicator.NoteHeal(healAmount, Run.playerHP - beforeHP, Run);
                RunChronicle.Rest(Run, Run.playerHP - beforeHP, beforeHP, Run.playerMaxHP);
                Log($"前哨基地: 不足HPの30%回復 +{Run.playerHP - beforeHP} → {Run.playerHP}/{Run.playerMaxHP}");
            }

            // [撤回] 前哨基地の物資回復 (減少分の20%)。 2026-08-05 に試して即日撤回。
            //   各層で発動し、 減っているほど回復量が増えるため実質「毎層リセット」になり、
            //   最低物資 38.9 → 71.0 / 発狂到達率 15.6% → 0.4% と物資が資源でなくなった。
            //   物資はショップの「物資の包」で買う (2026-10-31 に物資回復の消費アイテムは廃止)。

            // MaxHPデバフ（崩れの共鳴等）
            if (ActiveModifier != null && ActiveModifier.maxHPBonus != 0)
            {
                Run.playerMaxHP = Mathf.Max(1, Run.playerMaxHP + ActiveModifier.maxHPBonus);
                Run.playerHP = Mathf.Min(Run.playerHP, Run.playerMaxHP);
                Log($"層デバフ: MaxHP{ActiveModifier.maxHPBonus:+0;-0} → {Run.playerMaxHP}");
            }

            // 物資の補給 (2026-09-28): 前哨基地 +200。 8 層は転移した先の一点で補給は無い。
            //   1 層は満タンで始まるので足さない ── 足すと遺物〈渇き〉の「開幕物資を下げる」が 1 手目で消える。
            if (Run.Depth != 8 && Run.Depth != MapSystem.FreeMove.FloorPlan.FirstFloor)
                ProvisionSystem.Supply(Run, MapSystem.FreeMove.FreeMapParams.RewardOutpost);

            SetPhase(GamePhase.FloorIntro);
            Log($"--- フロア {Run.currentFloor} [{ActiveModifier?.displayName ?? "なし"}] ---");
            OnFloorModifierApplied?.Invoke(ActiveModifier);

            SetPhase(GamePhase.MapNavigation);

            // 8 層 (Null Point) は内部でボス戦だけの層。 マップを挟まず、 そのままヴェスカ戦へ入る。
            if (Run.Depth == 8 && mm.CurrentMap != null && !string.IsNullOrEmpty(mm.CurrentMap.bossNodeId))
                MoveToNode(mm.CurrentMap.bossNodeId);
        }

        /// <summary>戦闘終了ハンドラ</summary>
        private void HandleCombatEnd(CombatResult result)
        {
            LastCombatResult = result;

            // 物資(ADR-0002): 戦闘中 Run.playerHP は不変なので、ここでの値が戦闘開始時HP。
            // 終了時HPと比較してHP収支(マイナス=物資減少)を判定する（ApplyBattleResult 後に適用）。
            if (Run != null) Run.combatStartHP = Run.playerHP;

            // 2026-06-04: 素材経済の潤沢化（武器フル強化＋昇華5-10レンジを狙う）。
            // 旧 25%×1 → 戦闘勝利で確定+1、エリート勝利はさらに+1。
            if (result.playerWon && Run != null)
            {
                // 出力 r10 (極点) 〈戦意〉: 勝つたびに与ダメが恒久的に伸びる (2026-09-13)。
                //   極点が未解禁でも数える ── 参照側 (GetBattleSpiritPct) が解禁を見る。
                Run.battleSpiritWins++;

                Run.weaponMaterials += 1;
                bool elite = MapSystem.MapManager.Instance?.CurrentNode != null
                             && MapSystem.MapManager.Instance.CurrentNode.EffectiveType == MapSystem.TileType.EliteBattle;
                if (elite) Run.weaponMaterials += 1;
                Log($"[戦闘報酬] 強化素材+{(elite ? 2 : 1)}");

                // 強奪トラック (2026-09-13): 通常エネミー撃破で確率パッシブドロップ。
                //   **ボス戦は対象外** ── ボスは同トラックの追加ゴールドが担当する。
                //   「道中で奪い、 ボスから奪う」で 1 本の軸として読める形にする。
                bool bossNodeForDrop = MapSystem.MapManager.Instance?.CurrentNode != null
                    && MapSystem.MapManager.Instance.CurrentNode.type == MapSystem.TileType.Boss;
                float dropPct = bossNodeForDrop
                    ? 0f : MetaProgression.MetaBuffApplicator.GetPlunderPassiveDropPct();
                if (dropPct > 0f
                    && GameLoop.GameRng.Value("plunder.drop") * 100f < dropPct)
                {
                    string pid = PickPassiveItemForBossExtra(false);
                    if (!string.IsNullOrEmpty(pid))
                    {
                        ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, pid);
                        RunStats.PlunderDrops++;
                        Log($"[強奪] 戦利品: {pid}");
                    }
                }

                // 整備パネル〈剛胆〉: **エリート撃破時だけ**追加のパッシブドロップ。
                //   強奪 (道中の通常戦) とは対象が排他なので二重取りにならない。
                bool eliteForValorDrop = MapSystem.MapManager.Instance?.CurrentNode != null
                    && MapSystem.MapManager.Instance.CurrentNode.EffectiveType.ToEnemyKind()
                       == CombatSystem.EnemyKind.Elite;
                float valorDropPct = eliteForValorDrop
                    ? MetaProgression.MetaBuffApplicator.GetElitePassiveDropPct() : 0f;
                if (valorDropPct > 0f
                    && GameLoop.GameRng.Value("valor.eliteDrop") * 100f < valorDropPct)
                {
                    string pid = PickPassiveItemForBossExtra(false);
                    if (!string.IsNullOrEmpty(pid))
                    {
                        ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, pid);
                        RunStats.ValorEliteDrops++;
                        Log($"[剛胆] 精鋭の戦利品: {pid}");
                    }
                }
            }

            // 値下げ交渉(=強盗) の戦闘: 勝敗で報酬/脱出 を独自処理
            if (Run.shopRobberyInProgress)
            {
                Run.shopRobberyInProgress = false;
                if (result.playerWon)
                {
                    // 勝利: スナップ済みアイテム全取得 + 大金 30-60G
                    int loot = 0;
                    if (Run.robberyPendingItems != null)
                    {
                        foreach (var id in Run.robberyPendingItems)
                        {
                            if (string.IsNullOrEmpty(id)) continue;
                            var data = ItemDatabase.Instance?.GetItem(id);
                            if (data == null) continue;
                            // **戻り値を見る。** AddPassiveItem は重複 (所持済 or このランで
                            //   見た品) を黙ってスキップして false を返す。 強盗は棚を無差別に
                            //   奪うので、 直前に自分で買った品が seen に入っていれば全部落ちる。
                            //   ここを数えないと「戦利品 N 件」が試行回数のままになり、
                            //   **実際には 1 つも増えていないのに報酬が出たように見える**。
                            bool got;
                            if (data.category == ItemCategory.Consumable)
                                got = Run.TryAddConsumable(id);
                            else
                                got = ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
                            if (got) Loadout.TryAutoEquip(Run, id);
                            loot++;
                            if (got) RunStats.RobberyLootApplied++;
                        }
                        Run.robberyPendingItems.Clear();
                    }
                    int gold = GameLoop.GameRng.RangeAuto("GameManager.4", 90, 181);   // 2026-08-10 収入 ×3
                    gold = GoldIncome.Gain(Run, gold, "値下げ交渉", applyLastStandFilter: false);
                    Run.playerHP = Mathf.Max(1, result.playerHPRemaining);
                    RunStats.RobberyWins++;
                    RunStats.RobberyLootTotal += loot;
                    Log($"値下げ交渉勝利: 戦利品{loot}件 +{gold}G");
                    OnBattleEnded?.Invoke(result);
                    SetPhase(GamePhase.MapNavigation);
                    return;
                }
                else
                {
                    // 敗北: ラン継続、 HP1 で外に放り出される
                    Run.robberyPendingItems?.Clear();
                    Run.playerHP = 1;
                    RunStats.RobberyLosses++;
                    Log("値下げ交渉敗北: HP1 で外に放り出された (ラン継続)");
                    OnBattleEnded?.Invoke(result);
                    SetPhase(GamePhase.MapNavigation);
                    return;
                }
            }

            // 1 戦終えた。 航行の危険度見積りが使う (RunState.AverageFightDamage)。
            //   **強盗・特殊戦は上の return で抜けているので、通常戦とボスだけが数えられる。**
            Run.fightCount++;

            Run.ApplyBattleResult(result.playerWon, result.playerHPRemaining, result.totalTurns);

            // 物資(ADR-0002): HP収支マイナス→床定量を減少／非マイナス→わずか回復。
            // 飢餓→物資統合の再マップ: 暴食(トゥルハドの暴食)で損×2、ProvisionLossReduce メタバフで損を軽減。
            float provisionLossMult = MetaProgression.PermanentDebuffEffects.HasGluttony(Run) ? 2f : 1f;
            int provisionLossReduce = MetaProgression.MetaBuffApplicator.GetProvisionLossReduction();
            ProvisionSystem.ApplyCombatHpBalance(Run, provisionLossMult, provisionLossReduce);

            SetPhase(GamePhase.BattleResult);
            OnBattleEnded?.Invoke(result);

            string resultText = result.playerWon ? "勝利" : "敗北";
            Log($"戦闘結果: {result.enemyDisplayName} — {resultText} ({result.totalTurns}T) 残HP:{result.playerHPRemaining}");

            // 層デバフ: 戦闘後固定ダメージ（瘀気侵蟀等）
            if (ActiveModifier != null && ActiveModifier.postCombatDamage > 0 && Run.playerHP > 0)
            {
                int postDamage = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                    ActiveModifier.postCombatDamage, Run, Run.playerHP);
                int hpBeforePostDamage = Run.playerHP;
                Run.playerHP = Mathf.Max(1, Run.playerHP - postDamage);
                MetaProgression.Achievements.AchievementService.NoteExternalHpDamage(hpBeforePostDamage - Run.playerHP);
                Log($"層デバフ: 戦闘後{postDamage}ダメージ (HP:{Run.playerHP})");
            }
        }

        /// <summary>タイルの種類に応じてフェーズを遷移</summary>
        /// <summary>現在地から 1 ホップで行けるノードの戦闘名を開示する。
        ///
        /// <para>**解決と開示を同じ場所でやる。** エンカウントはノード添字で決まる (順序非依存) ので、
        /// ここで先に解いても、 踏んだ時に解いても結果は同じ。 未訪問ノードを解いても
        /// GameRng の列は進まない (<see cref="FloorManager.NodeRngIndex"/> の説明を参照)。</para></summary>
        public void RevealAdjacentEncounters()
        {
            var mm = MapManager.Instance;
            var map = mm?.CurrentMap;
            var here = mm?.CurrentNode;
            if (map == null || here == null) return;

            int floor = map.floor > 0 ? map.floor : Run.Depth;

            // 現在地そのものも解いておく (踏んだ瞬間に StartBattleTile が読む)。
            FloorManager.EnsureEncounter(floor, here);
            here.encounterRevealed = true;

            // 自由移動の層 (2026-09-28): 道の隣ではなく「マスの種別が見えている点」を開く
            //   (何もしない時の視界 1・照明 5・偵察 3)。 道の隣は遠いことがあるので 1 ホップの規則は使えない。
            if (mm.IsFreeMap) mm.SyncFreeKnowledge();
            var reachable = mm.IsFreeMap ? null : map.GetReachableFrom(here.id);
            if (reachable != null)
            {
                for (int i = 0; i < reachable.Count; i++)
                {
                    var n = reachable[i];
                    if (n == null) continue;
                    FloorManager.EnsureEncounter(floor, n);
                    n.encounterRevealed = true;
                }
            }

            // ── ボスだけは層に入った時点で開示する (1 マス前の規則の唯一の例外) ──
            //   3 層はボスが 3 体プールからの抽選なので、 正体が本物の情報になる。 それを
            //   ボス直前で知っても手遅れで、 **ボス前の確定 Shop + Rest (2026-08-16) が
            //   「相手を見て買う」場にならない**。 層頭で開けば、 その買い物がボスへ応答できる。
            //   ボスマスはもともと盤の端に見えているので、 名前だけ先に出しても隠蔽は壊れない。
            //   (裏ボスは FloorManager 側で対象外 ── 到着時に差し替わるので事前には出せない)
            var boss = !string.IsNullOrEmpty(map.bossNodeId) ? map.GetNode(map.bossNodeId) : null;
            if (boss != null)
            {
                FloorManager.EnsureEncounter(floor, boss);
                boss.encounterRevealed = true;
            }
        }

        private void ActivateTile(MapNode node)
        {
            var effectiveType = node.EffectiveType;

            // 収束ノード(Boss/Outpost)以外の一度きりタイルは、再訪時に再発火させない。
            // （イベントの無限ファーム＝同行マス往復によるデッドロックを構造的に防ぐ）
            //
            // **除外判定は EffectiveType ではなく素の type で行う (2026-08-08 修正)。**
            //   resolvedType を立てたまま戻さない経路があると、 EffectiveType 基準ではノードの種類が
            //   すり替わって oneShot の判定を誤る (旧 Λ 環状線で全ノードが沈黙した原因・GAME.md §24)。
            var baseType = node.type;
            bool oneShot = baseType != TileType.Boss && baseType != TileType.Outpost;
            if (oneShot && node.activated)
            {
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            if (oneShot) node.activated = true;

            OnTileActivated?.Invoke(effectiveType);
            Log($"タイル起動: {TileToJapanese(effectiveType)} ({node.id})");

            // T4-A〈破綻〉: 有利マスが空白になっていることがある。
            //   **oneShot で activated を立てた後に判定する** ── 空白化したマスは
            //   「踏んだ」扱いで確定させ、 往復して引き直せないようにする。
            if (TryVoidAdvantageTile(effectiveType)) return;

            switch (effectiveType)
            {
                case TileType.Battle:
                    StartBattleTile();
                    break;
                case TileType.EliteBattle:
                    IsEliteSecondFight = false;
                    firstEliteEnemy = null;
                    StartBattleTile();
                    break;
                case TileType.Boss:
                    StartBossTile();
                    break;
                case TileType.Rest:
                    SetPhase(GamePhase.RestStop);
                    break;
                case TileType.Shop:
                    // [変更 2026-09-12] 強盗後の「出禁」(ショップマス素通り) は既定で撤去。
                    //   罰は ShopManager 側の価格割増へ移した ── 出禁だと強盗報酬の 90〜180G が
                    //   使い道ごと消えて、 報酬と罰が打ち消し合っていた。
                    //   下の分岐は比較スイープ用の退避経路 (ShopManager.RobberyBlocksShops)。
                    if (Run != null && Run.shopRobberyDone
                        && ItemSystem.Shop.ShopManager.RobberyBlocksShops)
                    {
                        Log("ショップ閉鎖中: 値下げ交渉により商人ギルドから出禁 (旧仕様・比較用)");
                        SetPhase(GamePhase.MapNavigation);
                        break;
                    }
                    if (node.isFalseMerchant) StartFalseMerchantCombat();
                    else EnterShop();
                    break;
                case TileType.Event:
                    BeginEventEncounter();
                    break;
                case TileType.Treasure:
                    OpenTreasure();
                    break;
                case TileType.Exchange:
                    SetPhase(GamePhase.ExchangeTile);
                    break;
                case TileType.Gate:
                    BeginGateRitual();
                    break;
                default:
                    SetPhase(GamePhase.MapNavigation);
                    break;
            }
        }

        /// <summary>2 体戦のときの 2 体目。 単体戦では null。 報酬計算と UI が読む。</summary>
        public CombatSystem.EnemyData CurrentEnemySecondary { get; private set; }

        /// <summary>いま戦っているエンカウントの報酬倍率 (2 体戦 = 2.0)。</summary>
        public float CurrentEncounterRewardMultiplier { get; private set; } = 1f;

        private void StartBattleTile()
        {
            var node = MapManager.Instance?.CurrentNode;
            int floor = Run.Depth;

            // 到着時に開示済みのはずだが、 保存ロード直後など未解決で入ることがあるので念のため解く。
            FloorManager.EnsureEncounter(floor, node);
            var preset = EncounterPresets.Get(node?.encounterId);
            bool isPair = node != null && !string.IsNullOrEmpty(node.encounterEnemyB);

            CurrentEnemySecondary = null;
            CurrentEncounterRewardMultiplier = 1f;

            CurrentEnemy = node != null && !string.IsNullOrEmpty(node.encounterEnemyA)
                ? EnemyDatabase.Get(node.encounterEnemyA)?.Clone()
                : FloorManager.PickEnemy(floor);
            if (CurrentEnemy == null)
            {
                Debug.LogError("[GameManager] 敵の選出に失敗");
                SetPhase(GamePhase.MapNavigation);
                return;
            }

            if (isPair)
            {
                var second = EnemyDatabase.Get(node.encounterEnemyB)?.Clone();
                if (second != null)
                {
                    // **HP の大きい方を 1 体目に置く。** 与ダメージは両方に同額入るので、
                    //   累積被ダメが同じ = HP の小さい方が必ず先に落ちる。 1 体目を大きい側に
                    //   固定しておくと「1 体目の撃破 = 戦闘終了」が常に正しくなり、
                    //   CombatManager 側で決着条件を分岐させずに済む
                    //   (2 体目は 1 体目の HP 減少ぶんを写して削る実装なので、
                    //    1 体目が先に 0 になると 2 体目へダメージが流れなくなる)。
                    if (second.maxHP > CurrentEnemy.maxHP)
                    {
                        var tmp = CurrentEnemy; CurrentEnemy = second; second = tmp;
                    }

                    CurrentEnemySecondary = second;
                    CurrentEncounterRewardMultiplier = preset?.RewardMultiplier ?? 2f;
                }
            }

            if (firstEliteEnemy == null && MapManager.Instance.CurrentNode.EffectiveType == TileType.EliteBattle)
                firstEliteEnemy = CurrentEnemy;

            OnEnemyEncountered?.Invoke(CurrentEnemy);
            // 単体もペアも必ず戦闘名が付く (名前の有無で構成を漏らさないため)。
            string title = FloorManager.EncounterTitle(node, floor);
            if (string.IsNullOrEmpty(title)) title = CurrentEnemy.displayName;
            Log(CurrentEnemySecondary != null
                ? $"エンカウント: 〈{title}〉 {CurrentEnemy.displayName} ＋ {CurrentEnemySecondary.displayName} (報酬 ×{CurrentEncounterRewardMultiplier:0.#})"
                : $"エンカウント: 〈{title}〉 {CurrentEnemy.displayName}");

            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df, str_, ft_,
                                               CurrentEnemySecondary);
            // 逃げる (2026-09-28): 自由移動の層の戦闘・エリートだけ。
            CombatManager.Instance.FleeAllowed = MapManager.Instance != null && MapManager.Instance.IsFreeMap;
        }

        /// <summary>所持リストのうちカテゴリが Passive のアイテム数を数える
        /// （ownedPassiveItems には武器/ダイス/消費品も混在するため category で絞る）。</summary>
        private int CountOwnedPassiveItems()
        {
            var owned = Run?.ownedPassiveItems;
            if (owned == null) return 0;
            var db = ItemDatabase.Instance;
            int n = 0;
            foreach (var id in owned)
            {
                var it = db?.GetItem(id);
                if (it != null && it.category == ItemCategory.Passive) n++;
            }
            return n;
        }

        /// <summary>ラン重複排除用: 所持済みで「重複させたくない」アイテムの internalName 集合。
        /// 消費アイテムは使い切る前提なので重複可（除外しない）。パッシブ/武器/ダイスは重複させない。</summary>
        private System.Collections.Generic.HashSet<string> BuildDedupExclude()
        {
            var set = new System.Collections.Generic.HashSet<string>();
            var owned = Run?.ownedPassiveItems;
            if (owned == null) return set;
            var db = ItemDatabase.Instance;
            foreach (var id in owned)
            {
                var it = db?.GetItem(id);
                if (it != null && it.category == ItemCategory.Consumable) continue;
                set.Add(id);
            }
            // このランで一度取得した(=売却/廃棄した物も含む)パッシブも再抽選しない（重複禁止・捨てた物も対象）。
            if (Run?.seenPassiveItemIds != null) set.UnionWith(Run.seenPassiveItemIds);

            ExcludeObsoleteFamilyTiers(set, db);
            return set;
        }

        /// <summary>Lv 制家系（追撃 / 剛力 / 反攻 … 14 家系）のうち、
        /// <b>既に到達した Lv 以下</b>を抽選から外す。
        ///
        /// <para><b>なぜ必要か（2026-09-01）。</b> 除外は所持 ID そのものしか見ていなかったため、
        /// <b>追撃IV を装備していても店頭に追撃I が並んでいた</b>。
        /// ゲーム側は下位 Lv が無意味だと既に知っている ──
        /// <c>PassiveSkillRegistry.IsHigherTierPresent</c> も <c>InventoryPower</c> も、
        /// 上位がある家系の下位を評価から飛ばしている。
        /// <b>内部で死んでいると分かっているものを店頭に出していた</b>のが実態で、
        /// パッシブ枠は 1 ショップ 3 枠しかない（§16-1）ので取りこぼしが重い。</para>
        ///
        /// <para><b>上位 Lv は絶対に外さないこと。</b> 下位を所持していると上位が 1G になる
        /// 「上位互換アップグレード割引」（§16-3）が成立しなくなる ──
        /// あれは<b>意図された進行経路</b>で、ここで塞ぐと家系が伸ばせなくなる。
        /// 外すのは<b>到達済み Lv 以下</b>だけ。</para>
        ///
        /// <para>売却・廃棄した下位も対象にするため、判定には所持だけでなく
        /// <c>seenPassiveItemIds</c> 由来の集合（＝引数の <paramref name="set"/>）も使う。
        /// 「IV を買うために I を売った」後に I が再び並ぶのを防ぐ。</para></summary>
        private void ExcludeObsoleteFamilyTiers(System.Collections.Generic.HashSet<string> set, ItemDatabase db)
        {
            if (db == null || set.Count == 0) return;

            // 到達済みの家系 → 最大 Lv
            var reached = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var id in set)
            {
                int lv = MaxFamilyLevelOf(db, id, out string fam);
                if (lv <= 0) continue;
                if (!reached.TryGetValue(fam, out int prev) || lv > prev) reached[fam] = lv;
            }
            if (reached.Count == 0) return;

            foreach (var data in db.GetAllItems())
            {
                if (data == null || data.category != ItemCategory.Passive) continue;
                int lv = MaxFamilyLevelOf(db, data.id, out string fam);
                if (lv <= 0) continue;
                if (reached.TryGetValue(fam, out int maxLv) && lv <= maxLv) set.Add(data.id);
            }
        }

        /// <summary>アイテムが持つパッシブの家系名と最大 Lv。 Lv 制でなければ 0。</summary>
        private static int MaxFamilyLevelOf(ItemDatabase db, string itemId, out string family)
        {
            family = null;
            var data = db?.GetItem(itemId);
            if (data?.passiveSkills == null) return 0;
            int maxLv = 0;
            foreach (var ps in data.passiveSkills)
            {
                if (string.IsNullOrEmpty(ps.internalName)) continue;
                var (fam, lv) = ItemSystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ps.internalName);
                if (lv > maxLv) { maxLv = lv; family = fam; }
            }
            return maxLv;
        }

        // ---- 交換マス（パッシブ1つ → 上位Tierパッシブをランダム入手・任意） ----

        /// <summary>交換可能か（渡せるパッシブを所持しているか）。</summary>
        public bool CanExchangeTile
            => CurrentPhase == GamePhase.ExchangeTile && FindLowestTierOwnedPassiveIndex() >= 0;

        /// <summary>所持パッシブのうち最も低Tierのものの index。無ければ -1。</summary>
        private int FindLowestTierOwnedPassiveIndex()
        {
            var owned = Run?.ownedPassiveItems;
            if (owned == null) return -1;
            var db = ItemDatabase.Instance;
            int bestIdx = -1;
            ItemRarity bestR = ItemRarity.MYTHIC;
            for (int i = 0; i < owned.Count; i++)
            {
                var it = db?.GetItem(owned[i]);
                if (it == null || it.category != ItemCategory.Passive) continue;
                if (bestIdx < 0 || it.rarity < bestR) { bestR = it.rarity; bestIdx = i; }
            }
            return bestIdx;
        }

        /// <summary>交換実行: 最低Tierの所持パッシブを渡し、上位Tierのパッシブをランダム入手。</summary>
        public void DoExchangeTile()
        {
            if (CurrentPhase != GamePhase.ExchangeTile) return;
            int idx = FindLowestTierOwnedPassiveIndex();
            if (idx < 0) { SetPhase(GamePhase.MapNavigation); return; }

            var db = ItemDatabase.Instance;
            string givenId = Run.ownedPassiveItems[idx];
            var given = db?.GetItem(givenId);
            if (given == null) { SetPhase(GamePhase.MapNavigation); return; }

            string resultId = PickHigherTierPassive(given.rarity);
            if (string.IsNullOrEmpty(resultId))
            {
                Log("交換: 上位Tierのパッシブが無く成立せず");
                SetPhase(GamePhase.MapNavigation);
                return;
            }

            ItemSystem.Helpers.PassiveAddHelper.RemoveAt(Run, idx);
            ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, resultId);
            Loadout.TryAutoEquip(Run, resultId);
            var rd = db?.GetItem(resultId);
            Log($"交換マス: 「{given.displayName}」を渡し「{(rd != null ? rd.displayName : resultId)}」を入手");
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>交換せずに通過。</summary>
        public void SkipExchangeTile()
        {
            if (CurrentPhase != GamePhase.ExchangeTile) return;
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>given より高Tier（上限LEGENDARY、given が LEGENDARY なら据え置き）のパッシブを
        /// 重複排除の上でレア度重み抽選。未所持が枯渇したら重複許可で再抽選（上位からを許可）。</summary>
        private string PickHigherTierPassive(ItemRarity givenRarity)
        {
            var db = ItemDatabase.Instance;
            var all = db?.GetAllItems();
            if (all == null || all.Count == 0) return null;

            bool atCap = givenRarity >= ItemRarity.LEGENDARY;
            System.Func<ItemRarity, bool> tierOk = atCap
                ? (System.Func<ItemRarity, bool>)(r => r == ItemRarity.LEGENDARY)
                : (r => r > givenRarity && r <= ItemRarity.LEGENDARY);

            var excl = BuildDedupExclude();
            var pool = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in all)
            {
                if (it == null || it.category != ItemCategory.Passive) continue;
                if (!ItemSystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (!tierOk(it.rarity)) continue;
                if (excl.Contains(it.internalName)) continue;
                pool.Add(it);
            }
            if (pool.Count == 0) // 枯渇 → 重複許可で再構築
            {
                foreach (var it in all)
                {
                    if (it == null || it.category != ItemCategory.Passive) continue;
                    if (!ItemSystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                    if (!tierOk(it.rarity)) continue;
                    pool.Add(it);
                }
            }
            if (pool.Count == 0) return null;
            return ItemSystem.RarityWeightedPicker.Pick(pool)?.internalName;
        }

        /// <summary>所持パッシブからランダムに1つ除去（刻印も同期）。除去した index を返す。無ければ -1。</summary>
        private int LoseRandomPassiveItem()
        {
            var owned = Run?.ownedPassiveItems;
            if (owned == null || owned.Count == 0) return -1;
            int idx = GameLoop.GameRng.RangeAuto("GameManager.5", 0, owned.Count);
            string lostId = owned[idx];
            ItemSystem.Helpers.PassiveAddHelper.RemoveAt(Run, idx);
            var dd = ItemDatabase.Instance?.GetItem(lostId);
            Log($"恒久アイテム喪失: {(dd != null ? dd.displayName : lostId)}");
            return idx;
        }

        /// <summary>メタデバフ Lv5 偽の商人: ショップを装ったマスを踏んだ時の特殊エリート戦。
        /// 3ターン以内に倒せば勝利報酬(レア恒久アイテム)、倒せず逃走されると恒久アイテム1喪失。</summary>
        private void StartFalseMerchantCombat()
        {
            var baseEnemy = EnemyDatabase.Get("false_merchant");
            if (baseEnemy == null)
            {
                Debug.LogError("[GameManager] false_merchant 敵データが無いため通常ショップへフォールバック");
                EnterShop();
                return;
            }

            // 貪欲: プレイヤーの所持パッシブ数に応じてスケール。
            //   HP          : +3/個（複製データへ）
            //   ダイス合計値 : +1/3個（毎ロール加算。enemyDiceTotalBonus 経由）
            int passiveCount = CountOwnedPassiveItems();
            int hpBonus = passiveCount * 2;
            int diceTotalBonus = passiveCount / 3;
            var enemy = baseEnemy.Clone();
            enemy.maxHP += hpBonus;
            Log($"偽の商人[貪欲]: 所持パッシブ{passiveCount}個 → HP {baseEnemy.maxHP}→{enemy.maxHP}, ダイス合計+{diceTotalBonus}/ロール");

            inFalseMerchantCombat = true;
            CurrentEnemy = enemy;
            OnEnemyEncountered?.Invoke(enemy);
            Log("ショップに見えたが――偽の商人だ！(3ターン以内に討て)");

            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(enemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
            CombatManager.Instance.SetFleeAfterTurns(3);
            CombatManager.Instance.AddEnemyDiceTotalBonus(diceTotalBonus);
        }

        /// <summary>ボスノードを持たない層 (7 層) で、 終点の〈門〉を処理し終えたか。
        /// その層のボス撃破に相当する「層クリア」条件として使う。</summary>
        private bool IsBosslessFloorCleared()
        {
            if (Run == null) return false;
            if (MapSystem.MapGenerator.HasBoss(Run.Depth)) return false;
            var mm = MapManager.Instance;
            var node = mm?.CurrentNode;
            if (node == null || mm.CurrentMap == null) return false;
            // 2026-09-28: ボスの居ない層は 7 層だけで、 終点は〈門〉。 門を処理し終えて
            //   マップへ戻った瞬間が層クリア (そのまま 8 層へ転移する)。
            return node.type == TileType.Gate && node.activated;
        }

        /// <summary>タイル解決後の共通後処理。 ボス撃破 または ボスなし層の終端到達で層クリア。</summary>
        private void ReturnToMapOrClearFloor()
        {
            // ボスなし層の終端判定は SetPhase(MapNavigation) 側で一元処理する。
            if (Run != null && Run.bossDefeatedThisFloor) { HandleFloorClear(); return; }
            SetPhase(GamePhase.MapNavigation);
        }

        private void StartBossTile()
        {
            if (Run != null)
            {
                int fi = Mathf.Clamp(Run.Depth, 1, 8);
                RunStats.BossEntryCount[fi]++;
                RunStats.BossEntryProvision[fi] += Run.provision;
                RunStats.BossEntryProvisionTierSum[fi] += (int)ProvisionSystem.GetTier(Run);
                RunStats.BossEntryConsumables[fi] += Run.ownedConsumables?.Count ?? 0;
                RunStats.BossEntryHpPct[fi] += Run.playerMaxHP > 0
                    ? Mathf.RoundToInt(Run.playerHP * 100f / Run.playerMaxHP) : 0;
                RunStats.BossEntryPassives[fi] += Run.ownedPassiveItems?.Count ?? 0;
                if (GameHooks.MeasureRunPower != null)
                    try { RunStats.BossEntryPower[fi] += GameHooks.MeasureRunPower(Run); } catch { }
                CombatSystem.BossCombatTrace.BeginFight(Run.Depth,
                    GameLoop.GameRng.RunIndex, "boss", Run.playerHP, Run.playerMaxHP,
                    0, Run.provision, Run.ownedPassiveItems?.Count ?? 0);
            }
            // ボスの正体は **マス側に焼き付けてある** (FloorManager.EnsureEncounter)。
            //   3 層は 3 体プールからの抽選 (2026-07-29: ボス配置を 1/3/5/6/7 に絞った際、
            //   2 層のゴブリン王と 4 層の鏡の双子を捨てず 3 層難度へ再調整して流用した) なので、
            //   **ここで引き直すと戦闘名の表示と実物が食い違う**。 焼き付けた値を読む。
            var bossNode = MapManager.Instance?.CurrentNode;
            FloorManager.EnsureEncounter(Run.Depth, bossNode);
            string bossId = bossNode != null && !string.IsNullOrEmpty(bossNode.encounterEnemyA)
                ? bossNode.encounterEnemyA
                : FloorManager.BossIdForFloor(Run.Depth,
                                              FloorManager.NodeRngIndex(Run.Depth, bossNode));

            // 5F裏ボス置換: シュヴァリエのレイピア所持 → シュヴァリエ・サン=ジョリオラ
            if (!SuppressLayer5HiddenBoss
                && Run.Depth == 5
                && Run.ownedPassiveItems != null
                && Run.ownedPassiveItems.Contains("シュヴァリエのレイピア"))
            {
                bossId = "boss_layer5_hidden";
                Log("挑戦資格を満たす者の前にのみ現れる剣聖の影が形を成す ―");
            }

            CurrentEnemy = CombatSystem.EnemyDatabase.Get(bossId);
            if (CurrentEnemy == null)
            {
                Debug.LogWarning($"[GameManager] ボス '{bossId}' 未定義 — フロア{Run.Depth}のプールから抽選");
                CurrentEnemy = FloorManager.PickEnemy(Run.Depth);
            }
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"ボス戦: {CurrentEnemy.displayName}");

            // 5層ボス戦の清算系デバフ (カルマ系統廃止済み)
            if (Run.Depth == Run.normalClearFloor)
            {
                // 血の負債: 最大HP-15（一度きり、戦闘前に適用）
                if (Run.permanentDebuffs.Contains("血の負債"))
                {
                    int reduction = 15;
                    Run.playerMaxHP = Mathf.Max(1, Run.playerMaxHP - reduction);
                    Run.playerHP = Mathf.Min(Run.playerHP, Run.playerMaxHP);
                    Run.permanentDebuffs.Remove("血の負債"); // 一度限り消費
                    Log($"血の負債清算: 最大HP-{reduction} → {Run.playerMaxHP}");
                }
            }

            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
        }

        /// <summary>[検証用] 現在の Run ビルドのまま、指定フロアのボスと1戦だけ即決着し結果を返す。
        /// AutoRunner の「5Fボス勝率スイープ」専用。通常のフロア遷移・報酬・OnBattleEnded は介さず、
        /// CombatManager を直接駆動する（GameManager の集計イベントを発火しない）。</summary>
        public CombatResult SimulateBossFight(int floor)
        {
            var enemy = CombatSystem.EnemyDatabase.Get($"boss_layer{floor}");
            if (enemy == null || Run == null) return default;
            CurrentEnemy = enemy;
            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            CombatManager.Instance.StartCombat(enemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
            return CombatManager.Instance.ExecuteFullCombat();
        }

        // ================================================================
        //  7層終端〈門〉 (2026-09-14 リワーク。 旧 SinAltar 儀式)
        // ================================================================
        //
        //  地獄の果ての壁に立てかけられた円盤。 横書きの文が、 不思議と読める。
        //  曰く、 これは門であり、 いくつかの代償を払えば起動する。
        //
        //  **工程が 3 つあり、 手を抜いた工程がそのまま向こう側での欠陥になる。**
        //    ① 外縁の再構成 … 生物の血  → 怠ると〈不完全な修復〉
        //    ② 起動          … 遺物      → 怠ると〈不完全な起動〉
        //    ③ 転移          … 意識      → 抗うと〈不完全な転移〉
        //
        //  旧設計 (血の儀 / 貪欲の儀 / 遺品の儀) からの変更点:
        //    ・名前が機能を説明する。 「何を選んでいるのか」が読める。
        //    ・代償が 3 種類とも別の資源 (最大HP / 遺物 / 物資)。 ゴールドは外した ──
        //      直前がショップなので、 ゴールドの要求は実質「買い物を我慢したか」にしかならず、
        //      選択ではなく所持金チェックだった。
        //    ・罰が 3 種類とも別のビルドを刺す (充電依存 / 一点集中 / 防御依存)。

        private void HandleFloorClear()
        {
            // 金庫 r10 (極点): 層を跨ぐたびに所持金が倍になる。
            MetaProgression.VaultBank.OnFloorCleared(Run);

            // メタバフ〈フロアクリア回復〉: 階突破時の HP 回復（次層への持ち越し前に適用）
            int fch = MetaProgression.MetaBuffApplicator.GetFloorClearHeal();
            if (fch > 0 && Run != null && Run.playerHP > 0 && Run.playerHP < Run.playerMaxHP)
            {
                int before = Run.playerHP;
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + fch);
                Log($"メタ恩恵: フロアクリア回復 +{Run.playerHP - before} ({Run.playerHP}/{Run.playerMaxHP})");
            }

            // 5層クリア時: 〈決意〉以上未所持なら 5層クリアエンディング(NormalClear) で終了
            if (Run.IsNormalClear && Run.Depth == Run.normalClearFloor)
            {
                // 災厄の予兆を受けた事実を、5層ボス撃破によって〈決意〉へ昇格させる。
                // エリート撃破数は6層ゲートに使わない。
                ConvictionSystem.PromoteForLayer6(Run);
                // **計測専用**: 5層撃破時点で〈ブレイドダンス〉を強制付与する (既定 false)。
                //   「剣の舞 4 枚を完成させれば高難易度を通せるのか」を切り分けるための実験用。
                //   通常は 4 枚集約でしか手に入らず、 高難易度ほど揃う前に死ぬので実測できない。
                //   製品挙動は変えない。 AutoRunner の固定難易度スイープだけが true にする。
                if (GrantBladeDanceOnFloor5Clear && Run.ownedPassiveItems != null
                    && !Run.ownedPassiveItems.Contains(SwordDanceSet.Finale))
                {
                    ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, SwordDanceSet.Finale);
                    Log("[計測] 5層撃破 → 〈ブレイドダンス〉を強制付与");
                }

                if (!ConvictionSystem.HasResolveOrBetter(Run))
                {
                    MetaProgression.Achievements.AchievementService.CompleteRun(Run);
                    Run.EndRun();
                    SetPhase(GamePhase.RunClear);
                    OnRunCleared?.Invoke(Run);
                    Log("=== 3層クリア === 〈決意〉が無いためここで運命に背を向けた");
                    var ending5f = Endings.Resolve(Run);
                    Log(ending5f.Display);
                    VignetteUnlockState.OnRunClear(Run, ending5f.id, VignetteUnlockState.IsMaxDifficultyRun(Run));
                    return;
                }
                // 〈決意〉以上所持 → 6 層へ降りる。
                //   ここが「記録の残らない深さ」へ踏み込む節目なので、 演出だけはフックで出せるようにしておく。
                OnDeepDescent?.Invoke(Run);
                SetPhase(GamePhase.FloorClear);
                OnFloorAdvanced?.Invoke(MapSystem.FreeMove.FloorPlan.Next(Run.Depth));
                Log($"フロア{Run.currentFloor}クリア → 深部へ");
                return;
            }
            else if (Run.IsFullClear)
            {
                // **ヴェスカを倒しても、まだランは終わらない。** エンディングは
                //   「裂け目を閉じるか否か」の一択で決まる (2026-08-17)。 ここで止め、
                //   ChooseRiftFate が呼ばれてから RunClear へ進む。
                //   止める理由: 撃破と決断を同じフレームで済ませると、 プレイヤーが
                //   選んだという事実が残らない ── 分岐の重さは待たされることで出る。
                SetPhase(GamePhase.RiftChoice);
                Log("=== ヴェスカ撃破 === 裂け目の前に立っている");
                Log("  [1] 裂け目を閉じる  ── 自分と遺物の山を対価に、こちら側と向こう側の帳尻を合わせる");
                Log("  [2] 裂け目を閉じない ── 大穴の広がる速さを見るに、亡びは孫の孫より遠い");
            }
            else
            {
                // 6層 → 7層: 〈真理〉未所持なら 6層クリアエンディングで終了
                if (Run.Depth == 6 && !ConvictionSystem.HasTruth(Run))
                {
                    MetaProgression.Achievements.AchievementService.CompleteRun(Run);
                    Run.EndRun();
                    SetPhase(GamePhase.RunClear);
                    OnRunCleared?.Invoke(Run);
                    Log("=== 4層クリア === 〈真理〉が無いためここで〈門〉への道は閉ざされた");
                    var ending6f = Endings.Resolve(Run);
                    Log(ending6f.Display);
                    VignetteUnlockState.OnRunClear(Run, ending6f.id, VignetteUnlockState.IsMaxDifficultyRun(Run));
                    return;
                }
                SetPhase(GamePhase.FloorClear);
                OnFloorAdvanced?.Invoke(MapSystem.FreeMove.FloorPlan.Next(Run.Depth));
                Log($"フロア{Run.currentFloor}クリア → 次へ");
            }
        }

        /// <summary>ADR-0011 〈富〉(目 9) の報酬加算を適用して使い切る。 戦闘開始時にも 0 へ戻す。</summary>
        private int ApplyRoleGoldBonus(int coins)
        {
            if (Run == null || Run.roleGoldBonusPct <= 0) return coins;
            int boosted = Mathf.RoundToInt(coins * (1f + Run.roleGoldBonusPct / 100f));
            Log($"役〈富〉: 報酬 {coins} → {boosted} (+{Run.roleGoldBonusPct}%)");
            Run.roleGoldBonusPct = 0;
            return boosted;
        }

        /// <summary>武器の会心率 + 層補正の上限 (%)。</summary>
        public const float MaxWeaponCritPct = 50f;

        /// <summary>装備中の武器・ダイスからステータスを取得</summary>
        private (int diceCount, int diceMax, float critRate, int[] diceFaces,
                 DiceFaceParts.Tier[] faceTiers, bool suppressTerminalRoles)
            GatherPlayerCombatStats()
        {
            int diceCount = 2;
            int diceMax = 6;
            // 会心率は % で持つ (2026-09-19 統一・5% 刻み)。 武器なしの素値は 5%。
            float critRatePct = 5f;
            int[] diceFaces = null;
            // 出目パーツ: 面添字ごとの Tier。 diceFaces と 1 対 1 で対応する (DiceFaceParts.Build)。
            DiceFaceParts.Tier[] faceTiers = null;
            // ADR-0010〈無銘の賽〉: 端子役を成立させないダイスか。 面と同じ経路で解決する。
            bool suppressTerminalRoles = false;

            // 武器はダイスの「個数」と会心のみ定義する（面/最大値は出目パーツが供給）。
            //   装備は RunState.equippedWeaponId だけが持つ (旧 ItemEquipHandler は 2026-10-28 削除・
            //   どのシーンにも載っておらず常に null だった)。
            if (Run != null && !string.IsNullOrEmpty(Run.equippedWeaponId))
            {
                var w = ItemDatabase.Instance?.GetItem(Run.equippedWeaponId);
                if (w != null && w.hasWeaponStats)
                {
                    diceCount = w.weaponDice.count;
                    critRatePct = w.critRatePct;
                }
            }
            // --- サイコロの面 (2026-08-17: ダイスというアイテム種別を廃止) ---
            //   素の 6 面は全員共通。 個性は出目パーツだけで付く。
            //   faces と tiers は **必ず Build で対に組む** ── 別々に作ると
            //   素の面とパーツ面で添字がずれ、 効果が別の面に付く。
            DiceFaceParts.Build(Run != null ? Run.diceFaceParts : null,
                                out diceFaces, out faceTiers);

            // 面/最大出目は装備ダイス由来。diceMax は面の最大値から導出（RollDice・メタ補正・運命等が参照）。
            if (diceFaces != null && diceFaces.Length > 0)
            {
                int mx = diceFaces[0];
                for (int k = 1; k < diceFaces.Length; k++) if (diceFaces[k] > mx) mx = diceFaces[k];
                diceMax = mx;
            }

            // 層デバフ適用
            if (ActiveModifier != null)
            {
                if (ActiveModifier.diceMaxBonus != 0)
                    diceMax = Mathf.Max(1, diceMax + ActiveModifier.diceMaxBonus);
                // 層の会心補正は **武器の会心率だけ**に掛けて 0% で止める (旧: 分子で 0〜9 にクランプ)。
                //   他の会心ソース (パッシブ・メタ) はこの後に足すので、 層のマイナスでは削れない。
                if (ActiveModifier.critRatePctBonus != 0f)
                    critRatePct = Mathf.Clamp(critRatePct + ActiveModifier.critRatePctBonus, 0f, MaxWeaponCritPct);
            }

            // 影の代償の出目-1 はロール時に50%確率で発動するため、ここでは何もしない。
            // 実適用は CombatManager.ExecuteTurn のロール直後で処理。

            return (diceCount, diceMax, critRatePct / 100f, diceFaces, faceTiers, suppressTerminalRoles);
        }

        /// <summary>フェーズ遷移</summary>
        /// <summary>ボスなし層 (2/4) の層クリア判定の再入ガード。</summary>
        private bool _resolvingBosslessClear;
    }
}

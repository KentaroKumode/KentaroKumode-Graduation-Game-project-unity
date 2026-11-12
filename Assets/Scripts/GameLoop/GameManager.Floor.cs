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
    /// <summary>GameManager: 層の出入り・層クリア・抽選の除外。 戦闘の開始と終わりは GameManager.Combat、 マスの起動と交換マスは GameManager.Tiles。</summary>
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

            // メタ: トークン獲得
            MetaProgression.MetaTokenEarner.OnFloorReached(Run.Depth);
            // 挑戦カード: この層の入口で引く (§23-25)。 BOT はここで選び、 人は航行の窓で選ぶ。 見せる層 (currentFloor) で引く ── 深さ (Depth) ではない
            DealChallengeForLayer(Run.currentFloor);

            // 層を移ったら HP を全回復する (2026-11-04)。 1 層は満タンで始まるので 2 層から。
            //   物資を絞って (戦闘で物資が出ない・移動 50) 寄り道が減った分、 層の中の HP の収支だけで死なないように。
            //   〈破綻〉の切り詰めより前に置く ── 挑戦デバフは全回復の後に効く。
            //   挑戦カードの入口の札を全部受けてから回復する (回復 −N% が人にも BOT にも同じに効くように)。
            //   人が航行の窓で選ぶ間は預け、 最後の札を受けた時に回復する (ResolveChallengePick)。
            if (Run.Depth > MapSystem.FreeMove.FloorPlan.FirstFloor && Run.playerHP < Run.playerMaxHP)
            {
                if (PendingChallengeTiers.Count == 0) ApplyEntryHeal();
                else _entryHealPending = true;
            }

            // 計測専用 (SuppressEvents): イベントの代わりに確信の資格を与える。 HP 半減と啓示のエリートは払わせない。
            if ((SuppressEvents || SuppressConvictionChain) && Run != null)
            {
                if (Run.convictionStage <= 0 && Run.ownedPassiveItems != null
                    && !Run.ownedPassiveItems.Contains(ConvictionSystem.IdConviction) && !ConvictionSystem.HasResolveOrBetter(Run))
                {
                    ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, ConvictionSystem.IdConviction);
                    ConvictionSystem.OnFirstAcquired(Run);
                }
                if (Run.Depth == 6) ConvictionSystem.RevealTruthInLayer6(Run);
            }

            // 層突入のパッシブ (2026-09-15)。
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

            // (前哨基地の武器の自動強化 (各層 1 回・2026-05-31〜) は 2026-11-12 に削除・GAME.md §24。 武器の強化は休憩の「鍛える」だけ)

            // 挑戦デバフ〈補給断絶〉: 前哨基地の回復量 ×0.80 / ×0.60 / ×0.40 (§15-2 v3.0)。
            //   **v3.0 は前哨基地を消さない** ── 低効率でも経路を残す (challenge-debuff-plan §3)。
            //   T4-A〈破綻〉の全回復−25% もここに乗る。
            float healMult = MetaProgression.MetaDebuffApplicator.GetHealMultiplier();

            if (ActiveModifier != null && ActiveModifier.maxHPBonusFlat != 0)
            {
                // 6層前哨基地: HP全回復 + MaxHP+5
                Run.playerMaxHP += ActiveModifier.maxHPBonusFlat;
                int beforeHP = Run.playerHP;
                int missing = Mathf.Max(0, Run.playerMaxHP - Run.playerHP);
                int healAmount = Mathf.CeilToInt(missing * healMult);
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + healAmount);
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
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + healAmount);
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
                int beforeHP = Run.playerHP;
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + healAmount);
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
                ProvisionSystem.Supply(Run, NavItems.OutpostSupply(Run));      // 〈補給商の割符〉で ×1.5

            SetPhase(GamePhase.FloorIntro);
            Log($"--- フロア {Run.currentFloor} [{ActiveModifier?.displayName ?? "なし"}] ---");
            OnFloorModifierApplied?.Invoke(ActiveModifier);

            SetPhase(GamePhase.MapNavigation);

            // 8 層 (Null Point) は内部でボス戦だけの層。 マップを挟まず、 そのままヴェスカ戦へ入る。
            if (Run.Depth == 8 && mm.CurrentMap != null && !string.IsNullOrEmpty(mm.CurrentMap.bossNodeId))
                MoveToNode(mm.CurrentMap.bossNodeId);
        }

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

        /// <summary>2 体戦のときの 2 体目。 単体戦では null。 報酬計算と UI が読む。</summary>
        public CombatSystem.EnemyData CurrentEnemySecondary { get; private set; }

        /// <summary>いま戦っているエンカウントの報酬倍率 (2 体戦 = 2.0)。</summary>
        public float CurrentEncounterRewardMultiplier { get; private set; } = 1f;

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

            return set;
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

                RecordChallengeClear();   // 5 層の踏破 = 挑戦の難易度の記録

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

        /// <summary>フェーズ遷移</summary>
        /// <summary>ボスなし層 (2/4) の層クリア判定の再入ガード。</summary>
        private bool _resolvingBosslessClear;
    }
}

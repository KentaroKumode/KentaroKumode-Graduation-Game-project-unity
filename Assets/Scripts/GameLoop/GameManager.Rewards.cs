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
    /// <summary>GameManager: 戦闘結果の確定・報酬とドロップ・宝箱・開幕パッシブの抽選。</summary>
    public partial class GameManager
    {
        /// <summary>戦闘結果を確認→次へ</summary>
        public void ConfirmBattleResult()
        {
            if (CurrentPhase != GamePhase.BattleResult) return;
            if (!LastCombatResult.HasValue) return;

            var result = LastCombatResult.Value;

            // メタデバフ Lv5 偽の商人戦の決着処理（通常の勝敗/報酬ロジックより前に解決）。
            //   勝利            → レア恒久アイテム(GOLD以上)を1つ獲得
            //   逃走(生存・未勝利) → 所持パッシブをランダム1つ喪失し、ランは継続
            //   死亡            → 下の通常敗北処理へフォールスルー（救済/ゲームオーバー）
            if (inFalseMerchantCombat)
            {
                if (result.playerWon)
                {
                    inFalseMerchantCombat = false;
                    string id = PickPassiveItemForBossExtra(wantRare: true);
                    if (!string.IsNullOrEmpty(id))
                    {
                        ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
                        Loadout.TryAutoEquip(Run, id);
                        var dd = ItemDatabase.Instance?.GetItem(id);
                        Log($"偽の商人を討伐 → レア恒久アイテム「{(dd != null ? dd.displayName : id)}」を奪い返した");
                    }
                    SetPhase(GamePhase.MapNavigation);
                    return;
                }
                if (Run.playerHP > 0)
                {
                    inFalseMerchantCombat = false;
                    int lost = LoseRandomPassiveItem();
                    Log(lost >= 0
                        ? "偽の商人に逃げられた ― 恒久アイテムを1つ奪われた"
                        : "偽の商人に逃げられた（奪われる恒久アイテムが無かった）");
                    SetPhase(GamePhase.MapNavigation);
                    return;
                }
                // HP0 で決着 = 通常の死亡として扱う
                inFalseMerchantCombat = false;
            }

            // 逃げた (2026-09-28): 物資 −100・ゴールド半減・報酬なし。 直前の点へ戻る。
            if (result.playerFled && Run.playerHP > 0)
            {
                HandleFledBattle();
                return;
            }

            // 敗北 or HP0 → 救済（灯火→ラストスタンド）/ なければゲームオーバー
            if (!result.playerWon || Run.playerHP <= 0)
            {
                // イベント由来戦闘は勝敗問わずここで終了扱い。フラグを必ず解除する。
                // 解除しないと「敗北→救済でマップ復帰」後にフラグが残存し、
                // 後続のボス勝利時に下のイベント復帰ブロックを誤通過して
                // フロアクリア遷移が発火せず、出口なしボスノードでソフトロックする。
                if (returnToMapAfterEventCombat)
                {
                    returnToMapAfterEventCombat = false;
                    EventEncounter.Instance?.Clear();
                }

                // ボスマス戦闘では灯火 / フルーレ・バレエ による救済を行わない（復活して戻ると
                // ボスノードは収束ノードで出力接続が無く進行不能になるため）。
                // **ラストスタンドはここには居ない** ── 2026-09-13 から戦闘内で
                // その場蘇生する (CombatManager.TryLastStandRevive)。 退却を伴わないので
                // ボスノードで詰まらない。
                bool isBossFight = !InMapEncounterCombat && MapManager.Instance?.CurrentNode != null
                    && MapManager.Instance.CurrentNode.type == TileType.Boss;

                if (LastStand.TryConsumeRevival(Run, isBossFight))
                {
                    Log("救済発動: マップへ戻る");
                    // 追跡者の戦闘の途中なら、 逃げた扱い (直前の点へ戻り、 追跡者は 2 手番足止め)。
                    //   代償は取らない ── 救済そのものが代償 (灯火を失う)。
                    bool wasPursuer = InPursuerCombat;
                    if (InFamiliarCombat) { EndFamiliarFight(fled: false); return; }
                    if (wasPursuer) EndPursuerFightAsFled(payCost: false);
                    SetPhase(GamePhase.MapNavigation);
                    return;
                }
                if (isBossFight)
                    Log("ボス戦敗北: 救済なし → ラン終了");

                // **敗北ではエンディングを出さない (2026-08-17)。**
                //   死ぬたびに同じ文章を読ませるのは物語ではなく作業になる、 というのが廃止の理由。
                //   フラグだけは残す ── 「7 層まで行って落ちた」は計測にも codex 条件にも使う。
                //   <b>ここで Endings.Resolve を呼んではいけない</b>: Resolve は lastBossId しか見ないので、
                //   7 層で敗北しても TRUE END を返してしまう (エンド分岐は撃破後の一択に一本化した)。
                if (isBossFight && CombatSystem.CombatManager.Instance?.CurrentEnemy != null
                    && CombatSystem.CombatManager.Instance.CurrentEnemy.id
                       .StartsWith(BossIds.Layer7Prefix))
                {
                    Run.lostAtLayer7 = true;
                    Log("7層で敗北 ── エンディング無し");
                }

                Run.EndRun();
                SetPhase(GamePhase.GameOver);
                OnGameOver?.Invoke(Run);
                return;
            }

            // イベント由来の戦闘 → 勝利後効果を適用してマップへ戻る
            if (returnToMapAfterEventCombat)
            {
                returnToMapAfterEventCombat = false;
                EventEncounter.Instance?.ApplyPostCombatEffects();
                EventEncounter.Instance?.Clear();
                SetPhase(GamePhase.MapNavigation);
                return;
            }

            // 追跡者の戦闘: 勝ち切ったら物資 +300 とアイテム 1 個。
            //   **マスに紐づく報酬 (下) は通さない** ── 捕まった点は直前に着いた点で、
            //   その点の起動は戦闘の後 (FlushPendingArrival) に行う。
            if (InPursuerCombat)
            {
                HandlePursuerWon();
                return;
            }
            if (InFamiliarCombat)
            {
                HandleFamiliarFightWon();
                return;
            }

            var mm = MapManager.Instance;
            var node = mm.CurrentNode;

            // メタ: 敵撃破トークン + 戦闘勝利金
            // metaWinGold は最大3、全戦闘に加算すると経済を歪める(1/5 デノミ済み環境で +90G/ラン に到達)。
            // ボス撃破時のみ適用し、通常戦闘では 0 とする。
            MetaProgression.MetaTokenEarner.OnEnemyDefeated();

            bool isBossNodeForMeta = node != null && node.type == TileType.Boss;
            int metaWinGold = isBossNodeForMeta
                ? MetaProgression.MetaBuffApplicator.GetBossGoldBonus()
                : 0;
            bool prideActive = MetaProgression.PermanentDebuffEffects.HasPride(Run);

            // ボス勝利 → フロアクリア
            if (node.type == TileType.Boss)
            {
                Run.bossDefeatedThisFloor = true;
                Run.lastBossId = CurrentEnemy?.id ?? Run.lastBossId; // エンディング分岐用（最後に撃破したボス）

                // 5層裏ボス撃破フラグ: 以降のレイピア解除に会心+9 が付くようになる
                if (CurrentEnemy != null && CurrentEnemy.id == "boss_layer5_hidden")
                {
                    Run.defeatedSaintGeorges = true;
                    Log("剣聖サン=ジョリオラを撃破 ― 真の決闘術が解放された。");
                    // 2026-06-28: 固有ドロップ「シュヴァリエのレイピア」 (従来は入手経路が未配線だった)
                    if (!Run.OwnsPassive(ItemIds.ChevalierRapier))
                    {
                        ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, ItemIds.ChevalierRapier);
                        Log("剣聖の得物「シュヴァリエのレイピア」を手に入れた。");
                    }
                }

                // 1層ボス（トレジャーゴブリン）: 良質な武器/パッシブを1個ドロップ
                if (Run.Depth == 1)
                {
                    string dropId = PickTreasureGoblinDrop();
                    if (!string.IsNullOrEmpty(dropId))
                    {
                        ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, dropId);
                        Loadout.TryAutoEquip(Run, dropId);
                        var dd = ItemDatabase.Instance?.GetItem(dropId);
                        Log($"トレジャーゴブリン討伐報酬: {(dd != null ? dd.displayName : dropId)} を獲得");
                    }
                }

                int rewardBase = FloorManager.CalculateRewardCoins(Run.Depth, true, result.totalTurns) * 2;
                // 傲慢: ボスはエリート以上扱いで報酬2倍（割合先）
                if (prideActive) rewardBase *= 2;
                // パッシブ刻印〈守銭〉: 発動分 × 1G/ボス
                // ADR-0011 〈富〉(目 9): 戦闘中に積んだ報酬加算 (%)。 割合先 (メタの定額 +N の前)。
                rewardBase = ApplyRoleGoldBonus(rewardBase);
                int reward = GoldIncome.Gain(Run, rewardBase + metaWinGold, "ボス報酬");
                // 物資の補給 (2026-09-28): ボス +150
                ProvisionSystem.Supply(Run, MapSystem.FreeMove.FreeMapParams.RewardBoss);

                // [廃止 2026-09-12] メタ: ボス撃破時の追加パッシブ報酬 (旧 強奪 r10)。

                SetPhase(GamePhase.Reward);
                OnRewardGranted?.Invoke(reward);
                return;
            }

            // エリートマスは1連戦（精鋭1体で完結）。2連戦は廃止。

            // 通常報酬（傲慢: 通常戦闘=0、エリート以上=×2、いずれも割合計算が先）
            // エンカウントプリセットの報酬倍率 (2 体戦 = ×2)。 2 体ぶんの攻撃を受け切った対価。
            int coins = FloorManager.CalculateRewardCoins(Run.Depth, true, result.totalTurns,
                                                          CurrentEncounterRewardMultiplier);
            // 精鋭戦の報酬割増 (2026-09-14)。 2 体戦の ×2 より少し下 ── 単体だが危険、 の対価。
            //   [撤去] IsEliteSecondFight による ×1.5 ── **どこからも true にならない死にフラグ**
            //   だった (docs §5-2 の「連続 2 戦」は実装されていなかった)。
            bool eliteNodeReward = MapManager.Instance?.CurrentNode != null
                && MapManager.Instance.CurrentNode.EffectiveType.ToEnemyKind()
                   == CombatSystem.EnemyKind.Elite;
            if (eliteNodeReward)
                coins = Mathf.RoundToInt(coins * CombatRewards.EliteRewardMul);
            if (prideActive)
            {
                if (eliteNodeReward) coins *= 2;
                else coins = 0;
            }
            // 整備パネル〈剛胆〉: **エリート戦だけ**報酬ゴールドを割増。
            //   通常戦に乗せると「強敵を選ぶ」という軸の性格が消えて単なる金庫になる。
            bool eliteNodeForValor = MapManager.Instance?.CurrentNode != null
                && MapManager.Instance.CurrentNode.EffectiveType.ToEnemyKind()
                   == CombatSystem.EnemyKind.Elite;
            if (eliteNodeForValor)
            {
                RunStats.ValorEliteFights++;   // [計装] 格上げが効いているかを見る
                float valorGoldPct = MetaProgression.MetaBuffApplicator.GetEliteGoldPct();
                if (valorGoldPct > 0f)
                    coins = Mathf.RoundToInt(coins * (1f + valorGoldPct / 100f));
            }

            // ADR-0011 〈富〉(目 9): 戦闘中に積んだ報酬加算 (%)。 割合先 (メタの定額 +N の前)。
            coins = ApplyRoleGoldBonus(coins);
            coins += metaWinGold;
            coins = GoldIncome.Gain(Run, coins, "戦闘報酬");
            IsEliteSecondFight = false;

            // 物資の補給 (2026-09-28): 通常 +30 / エリート +80。 払底したら戦って奪うしかない
            //   (Highfleet の「追ってきた敵から燃料を奪う」)。
            ProvisionSystem.Supply(Run, eliteNodeReward
                ? MapSystem.FreeMove.FreeMapParams.RewardElite : MapSystem.FreeMove.FreeMapParams.RewardBattle);
            // (手番制の航行マップでは戦闘は手番を使わず、 物音も立てない。)

            // 戦闘勝利報酬: **エリートは確定 1 個 / 通常は 15%** (2026-09-09 変更)。
            //
            // 旧: 通常 50% + エリートは更に 50% でもう 1 個 (期待 1.0)。
            //   「戦闘を経済の主軸へ (戦闘以外のゴールド源ナーフと対の措置)」という意図だったが、
            //   計装したところ **1 ラン のパッシブ取得 30.2 品のうち 8.91 品 (29.5%) が戦闘報酬**で
            //   最大の供給源になっていた (ショップ 8.16 / Λ 3.94 / 前哨基地 2.78 / イベント 2.05 /
            //   交換マス 1.84 / 宝箱 1.52 / 開幕 1.00)。 19 戦で 2 戦に 1 回 恒久パッシブが落ちる計算。
            //   供給の 2/3 が無料経路なので、 パッシブ価格を 4/6/8/10 → 7/8/9/10 にしても
            //   床のクリア率は 19.8% → 15.3% にしか動かなかった (目標は 1〜5%)。
            //
            // 新: **エリートは期待値そのまま・分散ゼロ** (0.5+0.5 = 期待1.0 → 確定1.0)。
            //   減らすのは通常戦だけ (0.5 → 0.15)。 代わりに戦闘報酬ゴールドを微増して
            //   「戦闘が経済の主軸」という筋は残す ── 現物ではなく金で払う形に寄せる。
            bool eliteWin = node != null && node.EffectiveType == TileType.EliteBattle;

            // 確信進化: エリート戦勝利時に〈根拠のない確信〉→〈決意〉→〈真理〉と進化
            if (eliteWin)
                ConvictionSystem.OnEliteDefeated(Run);
            // **抽選は毎戦引く** (エリートでも消費する) ── キー別ストリームの消費本数を
            //   分岐で変えると、 同一シードのペア比較で列がずれる。
            // **2026-09-15: エリートの確定ドロップを外した** (確定 1.0 → 40%)。 比率の正本は
            //   CombatRewards。 確定 vs 15% の 6.7 倍が「エリートは踏むほど得」の主因で、
            //   同時に供給の 45% が無料経路という状態を作っていた。 削った現物は
            //   CombatRewards.GoldScale で金にして返す ── 店での購入という選択へ寄せる。
            float rate = eliteWin ? CombatRewards.EliteDropRate : CombatRewards.NormalDropRate;
            bool dropped = GameLoop.GameRng.Value("GameManager.1") < rate;
            if (eliteWin) { CombatRewards.EliteRolls++; if (dropped) CombatRewards.EliteDrops++; }
            else          { CombatRewards.NormalRolls++; if (dropped) CombatRewards.NormalDrops++; }
            int drops = dropped ? 1 : 0;

            // 各ドロップを「同カテゴリ・同レア度の2択」として保留。
            // 相方候補が無い場合のみ即時付与（単体ドロップにフォールバック）。
            pendingRewardChoices.Clear();
            rewardChoiceIndex = 0;
            for (int d = 0; d < drops; d++)
            {
                var (a, b) = PickTreasureChoicePair();
                if (string.IsNullOrEmpty(a)) continue;
                if (string.IsNullOrEmpty(b))
                {
                    GrantRewardItem(a, eliteWin);
                    continue;
                }
                pendingRewardChoices.Add((a, b));
            }

            SetPhase(GamePhase.Reward);
            OnRewardGranted?.Invoke(coins);
        }

        /// <summary>ボス撃破ボーナス用のパッシブ抽選。wantRare=true で GOLD 以上のみ、レア度重み付き。</summary>
        /// <summary>開幕パッシブ専用の抽選 (2026-09-13)。 兵站の段に応じて
        /// レア度の重みを上位へ寄せる (<see cref="MetaProgression.MetaPanel.SupplyRarityBias"/>)。
        /// 本数だけ増やしても弱かったのは、 配られるのがカタログ平均の品で
        /// BOT が自分で買う「学習序列の上位」に質で負けていたため。</summary>
        private string PickStartingPassive()
        {
            var db = ItemDatabase.Instance;
            if (db == null) return null;
            var all = db.GetAllItems();
            if (all == null || all.Count == 0) return null;

            var dedup = BuildDedupExclude();
            var pool = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in all)
            {
                if (it == null) continue;
                if (it.category != ItemCategory.Passive) continue;
                if (!ItemSystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (dedup.Contains(it.internalName)) continue;
                pool.Add(it);
            }
            if (pool.Count == 0) return null;

            float bias = MetaProgression.MetaBuffApplicator.GetStartingPassiveRarityBias();
            return ItemSystem.RarityWeightedPicker.Pick(pool, null, bias)?.internalName;
        }

        private string PickPassiveItemForBossExtra(bool wantRare)
        {
            var db = ItemDatabase.Instance;
            if (db == null) return null;
            var all = db.GetAllItems();
            if (all == null || all.Count == 0) return null;

            var dedup = BuildDedupExclude();
            var pool = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in all)
            {
                if (it == null) continue;
                if (it.category != ItemCategory.Passive) continue;
                if (!ItemSystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (dedup.Contains(it.internalName)) continue;   // ラン重複排除
                pool.Add(it);
            }
            if (pool.Count == 0) return null;

            ItemRarity? minRarity = wantRare ? ItemRarity.GOLD : (ItemRarity?)null;
            var picked = ItemSystem.RarityWeightedPicker.Pick(pool, minRarity);
            if (picked == null && wantRare)
            {
                // フォールバック: レア該当無しなら全プールから
                picked = ItemSystem.RarityWeightedPicker.Pick(pool);
            }
            return picked?.internalName;
        }

        /// <summary>1層ボス(トレジャーゴブリン)のドロップ抽選。武器+パッシブから、SILVER以上を優先。</summary>
        private string PickTreasureGoblinDrop()
        {
            var db = ItemDatabase.Instance;
            if (db == null) return null;
            var all = db.GetAllItems();
            if (all == null || all.Count == 0) return null;

            var dedup = BuildDedupExclude();
            var pool = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in all)
            {
                if (it == null) continue;
                if (it.category != ItemCategory.Weapon && it.category != ItemCategory.Passive) continue;
                if (!ItemSystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (dedup.Contains(it.internalName)) continue;   // ラン重複排除
                pool.Add(it);
            }
            if (pool.Count == 0) return null;

            // 「いい感じ」: SILVER 以上を優先抽選、該当無しなら全プール
            var picked = ItemSystem.RarityWeightedPicker.Pick(pool, ItemRarity.SILVER)
                         ?? ItemSystem.RarityWeightedPicker.Pick(pool);
            return picked?.internalName;
        }

        /// <summary>報酬確認→マップに戻る or フロアクリア</summary>
        public void ConfirmReward()
        {
            if (CurrentPhase != GamePhase.Reward) return;

            // 未解決のドロップ2択が残っていれば確定を保留（UI/Botが先に選ぶ）。
            // 万一未選択のまま呼ばれたらソフトロック回避のため既定(option a)で消化。
            if (HasPendingRewardChoice)
            {
                while (HasPendingRewardChoice) ResolveRewardChoice(0);
                return;
            }

            ReturnToMapOrClearFloor();
        }

        /// <summary>タイルイベント完了→マップに戻る</summary>
        public void ConfirmTileEvent()
        {
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>宝箱マス到達時。ゴールドランダム + パッシブ/消費アイテム1個。</summary>
        private void OpenTreasure()
        {
            int floor = Run.Depth;

            // 挑戦デバフ 軸15〈空箱〉T3: 宝箱そのものが空 (1 層から)。
            if (MetaProgression.MetaDebuffApplicator.IsTreasureGone())
            {
                LastTreasureSummary = "空箱";
                Log("宝箱は空だった（空箱 T3）");
                return;
            }
            // 宝箱はゴールド0(価値はアイテムのみ)が基準。1/5デノミ後、僅少な変動が無意味になるため撤廃済み。
            // メタバフ〈宝箱の財宝〉解放時のみ、フロア依存の少額ゴールドが復活する。
            int gold = 0;
            if (MetaProgression.MetaBuffApplicator.IsTreasureChestGoldUnlocked())
            {
                // フロア依存の少額ゴールド。 F1=3, F3=6, F5=9, F7=12。
                //
                //   2026-09-12 に半額へ落としたが、 それは **予算 36pt 時代の +5.48pt** を
                //   見ての判断だった。 予算 24pt で測り直すと r10−r9 +0.21pt (CI ±1.1) で
                //   **ゼロと区別できない**ところまで落ちていたので、 元に戻す。
                gold = Mathf.Max(1, (1 + floor / 2) * 3);
            }
            gold = GoldIncome.Gain(Run, gold, "宝箱");

            // 挑戦デバフ 軸15〈空箱〉T2: 中身 −50%。
            //   宝箱の中身はゴールドではなく **アイテム 1 個 + 素材 1 個**が本体なので、
            //   「50% の確率でそれぞれ出ない」形で半減を表現する。 量を半分にできない離散報酬のため。
            float boxMul = MetaProgression.MetaDebuffApplicator.GetTreasureContentMultiplier();
            bool halfBox = boxMul < 0.999f;
            bool dropMaterial = !halfBox || GameLoop.GameRng.Chance(boxMul, "meta.emptyBoxMat", floor);
            bool dropItem     = !halfBox || GameLoop.GameRng.Chance(boxMul, "meta.emptyBoxItem", floor);

            // 2026-06-04: 宝箱でも強化素材+1（素材経済の潤沢化）
            if (dropMaterial) Run.weaponMaterials += 1;

            string itemId = dropItem ? PickRandomTreasureItem() : null;
            string itemLabel = "（なし）";
            if (!string.IsNullOrEmpty(itemId))
            {
                ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, itemId);
                Loadout.TryAutoEquip(Run, itemId);
                var data = ItemDatabase.Instance?.GetItem(itemId);
                itemLabel = data != null ? data.displayName : itemId;
            }

            LastTreasureSummary = $"宝箱: ゴールド+{gold}, {itemLabel}";
            Log(LastTreasureSummary);
            SetPhase(GamePhase.TreasureOpen);
        }

        /// <summary>宝箱から獲得するアイテムを ItemDatabase からレア度重み付きで1個選出。武器・クエスト・イベント限定は除外。</summary>
        private string PickRandomTreasureItem()
        {
            var db = ItemDatabase.Instance;
            if (db == null) return null;
            var all = db.GetAllItems();
            if (all == null || all.Count == 0) return null;

            var dedup = BuildDedupExclude();
            var pool = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in all)
            {
                if (it == null) continue;
                if (it.category == ItemCategory.Weapon) continue;
                if (it.category == ItemCategory.Quest) continue;
                if (!ItemSystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (dedup.Contains(it.internalName)) continue;   // ラン重複排除
                pool.Add(it);
            }
            if (pool.Count == 0) return null;

            // 鑑定の眼鏡: 次の宝箱の最低レア保証（消費）。ショップで既消費なら -1。
            CompleteItemData picked;
            if (Run != null && Run.nextLootMinRarity >= 0)
            {
                var minR = (ItemRarity)Run.nextLootMinRarity;
                Run.nextLootMinRarity = -1;
                picked = ItemSystem.RarityWeightedPicker.Pick(pool, minR)
                         ?? ItemSystem.RarityWeightedPicker.Pick(pool);
            }
            else
            {
                picked = ItemSystem.RarityWeightedPicker.Pick(pool);
            }
            return picked?.internalName;
        }

        /// <summary>戦闘後ドロップ用に「主候補 + 同カテゴリ・同レア度の相方」を抽選する。
        /// 相方が存在しなければ b = null（単体ドロップ扱い）。</summary>
        private (string a, string b) PickTreasureChoicePair()
        {
            var db = ItemDatabase.Instance;
            if (db == null) return (null, null);
            var all = db.GetAllItems();
            if (all == null || all.Count == 0) return (null, null);

            var dedup = BuildDedupExclude();
            var pool = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in all)
            {
                if (it == null) continue;
                if (it.category == ItemCategory.Weapon) continue;
                if (it.category == ItemCategory.Quest) continue;
                if (!ItemSystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (dedup.Contains(it.internalName)) continue;   // ラン重複排除
                pool.Add(it);
            }
            if (pool.Count == 0) return (null, null);

            CompleteItemData first;
            if (Run != null && Run.nextLootMinRarity >= 0)
            {
                var minR = (ItemRarity)Run.nextLootMinRarity;
                Run.nextLootMinRarity = -1;
                first = ItemSystem.RarityWeightedPicker.Pick(pool, minR)
                        ?? ItemSystem.RarityWeightedPicker.Pick(pool);
            }
            else
            {
                first = ItemSystem.RarityWeightedPicker.Pick(pool);
            }
            if (first == null) return (null, null);

            // 同カテゴリ・同レア度の相方候補（自身は除外）
            var partners = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in pool)
            {
                if (it == null || it.internalName == first.internalName) continue;
                if (it.category == first.category && it.rarity == first.rarity)
                    partners.Add(it);
            }
            string b = partners.Count > 0
                ? partners[GameLoop.GameRng.RangeAuto("GameManager.8", 0, partners.Count)].internalName
                : null;
            return (first.internalName, b);
        }

        /// <summary>報酬アイテムを実際に付与（所持追加＋刻印ロール＋自動装備）。</summary>
        private void GrantRewardItem(string itemId, bool eliteWin)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, itemId);
            Loadout.TryAutoEquip(Run, itemId);
            var dd = ItemDatabase.Instance?.GetItem(itemId);
            Log($"戦闘勝利報酬: {(dd != null ? dd.displayName : itemId)} を獲得{(eliteWin ? "（精鋭）" : "")}");
        }

        // ---- 戦闘後ドロップ2択: 公開API（UI / Bot から使用）----

        /// <summary>未解決のドロップ2択が残っているか。</summary>
        public bool HasPendingRewardChoice
            => CurrentPhase == GamePhase.Reward && rewardChoiceIndex < pendingRewardChoices.Count;

        /// <summary>現在提示中の2択のアイテムID。残っていなければ (null,null)。</summary>
        public (string a, string b) CurrentRewardChoice
            => HasPendingRewardChoice ? pendingRewardChoices[rewardChoiceIndex] : (null, null);

        /// <summary>現在の2択から which(0/1) を選んで付与し、次の選択へ進める。</summary>
        public void ResolveRewardChoice(int which)
        {
            if (!HasPendingRewardChoice) return;
            var pair = pendingRewardChoices[rewardChoiceIndex];
            string chosen = which == 1 ? pair.b : pair.a;
            GrantRewardItem(chosen, eliteWin: false);
            rewardChoiceIndex++;
        }
    }
}

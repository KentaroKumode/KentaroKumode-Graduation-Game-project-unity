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
    /// <summary>GameManager: 戦闘の開始 (通常・ボス・偽の商人)・戦闘の終わりの処理・ボスの単独の模擬戦・戦闘の前の能力値の集計。 GameManager.Floor.cs から切り出した (2026-11-12・中身は同じ)。</summary>
    public partial class GameManager
    {
        /// <summary>戦闘終了ハンドラ</summary>
        private void HandleCombatEnd(CombatResult result)
        {
            LastCombatResult = result;

            // 物資(ADR-0002): 戦闘中 Run.playerHP は不変なので、ここでの値が戦闘開始時HP。
            // 終了時HPと比較してHP収支(マイナス=物資減少)を判定する（ApplyBattleResult 後に適用）。
            if (Run != null) Run.combatStartHP = Run.playerHP;

            // 2026-06-04: 素材経済の潤沢化（武器フル強化＋昇華5-10レンジを狙う）。
            // 旧 25%×1 → 戦闘勝利で確定+1、エリート勝利はさらに+1。
            // 時限デバフ〈徒労〉/ 時限バフ〈開眼〉(2026-11-13): 通常の戦闘に勝った時だけ 1 つ減り、 この戦闘の報酬を 0 / 2 倍にする。
            //   判定はここで 1 回だけ (GameManager.Rewards.cs の TakeNormalBattleBoon)。 素材・強奪 (下) とゴールド・物資・ドロップ (ConfirmBattleResult) が結果を読む。
            TakeNormalBattleBoon(result);

            if (result.playerWon && Run != null)
            {
                // 出力 r10 (極点) 〈戦意〉: 勝つたびに与ダメが恒久的に伸びる (2026-09-13)。
                //   極点が未解禁でも数える ── 参照側 (GetBattleSpiritPct) が解禁を見る。
                Run.battleSpiritWins++;

                bool elite = MapSystem.MapManager.Instance?.CurrentNode != null
                             && MapSystem.MapManager.Instance.CurrentNode.EffectiveType == MapSystem.TileType.EliteBattle;
                int mat = ScaleByBattleBoon(elite ? 2 : 1);   // 〈徒労〉0 /〈開眼〉2 倍 (通常の戦闘だけ・エリートには掛からない)
                Run.weaponMaterials += mat;
                Log($"[戦闘報酬] 強化素材+{mat}{BattleBoonTag()}");

                // 強奪トラック (2026-09-13): 通常エネミー撃破で確率パッシブドロップ。
                //   **ボス戦は対象外** ── ボスは同トラックの追加ゴールドが担当する。
                //   「道中で奪い、 ボスから奪う」で 1 本の軸として読める形にする。
                bool bossNodeForDrop = MapSystem.MapManager.Instance?.CurrentNode != null
                    && MapSystem.MapManager.Instance.CurrentNode.type == MapSystem.TileType.Boss;
                float dropPct = bossNodeForDrop
                    ? 0f : MetaProgression.MetaBuffApplicator.GetPlunderPassiveDropPct();
                // 〈徒労〉: 抽選は引く (乱数の列を変えない) が配らない。 〈開眼〉: 抽選を 2 回引く (乱数を余分に引く)
                for (int k = 0, rolls = EventSystem.TimedEffects.BattleBoonRule.DropRolls(LastBattleBoon); k < rolls; k++)
                {
                    if (dropPct > 0f
                        && GameLoop.GameRng.Value("plunder.drop") * 100f < dropPct
                        && EventSystem.TimedEffects.BattleBoonRule.GrantsDrops(LastBattleBoon))
                    {
                        string pid = PickPassiveItemForBossExtra(false);
                        if (!string.IsNullOrEmpty(pid))
                        {
                            ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, pid);
                            RunStats.PlunderDrops++;
                            Log($"[強奪] 戦利品: {pid}{BattleBoonTag()}");
                            AddRelicInstabilityIfPassive(pid);   // 不安定化: 報酬で秘宝を得た (2026-11-13)
                        }
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
                        AddRelicInstabilityIfPassive(pid);   // 不安定化: 報酬で秘宝を得た (2026-11-13)
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
                            else if (Loadout.IsWeapon(id)) { Loadout.Gain(Run, id); got = true; }   // 武器は 1 本だけ (入れ替えるかを聞く)
                            else
                            {
                                got = ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
                                if (got) Loadout.TryAutoEquip(Run, id);
                            }
                            loot++;
                            if (got) RunStats.RobberyLootApplied++;
                        }
                        Run.robberyPendingItems.Clear();
                    }
                    int gold = GameLoop.GameRng.RangeAuto("GameManager.4", 90, 181);   // 2026-08-10 収入 ×3
                    gold = GoldIncome.Gain(Run, gold, "値下げ交渉");
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
            if (Run.Depth != MapSystem.FreeMove.FloorPlan.FarmFloor) Run.fightCount++;

            Run.ApplyBattleResult(result.playerWon, result.playerHPRemaining, result.totalTurns);

            // 端子の山札 (ADR-0014): 勝ったら端子の報酬 (戦闘後 2 択・エリート 3 択・層ボスはレア 3 択)
            if (result.playerWon && Run.playerHP > 0)
            {
                var tnode = MapSystem.MapManager.Instance?.CurrentNode;
                OfferTerminalReward(tnode != null && tnode.EffectiveType == MapSystem.TileType.EliteBattle,
                                    tnode != null && tnode.type == MapSystem.TileType.Boss);
            }

            // (戦闘の HP 収支マイナスによる物資の損は 2026-11-09 に撤去。 物資が減るのは移動だけ)

            SetPhase(GamePhase.BattleResult);
            OnBattleEnded?.Invoke(result);

            string resultText = result.playerWon ? "勝利" : "敗北";
            Log($"戦闘結果: {result.enemyDisplayName} — {resultText} ({result.totalTurns}T) 残HP:{result.playerHPRemaining}");

            // 層デバフ: 戦闘後固定ダメージ（瘀気侵蟀等）
            if (ActiveModifier != null && ActiveModifier.postCombatDamage > 0 && Run.playerHP > 0)
            {
                int postDamage = ActiveModifier.postCombatDamage;
                int hpBeforePostDamage = Run.playerHP;
                Run.playerHP = Mathf.Max(1, Run.playerHP - postDamage);
                MetaProgression.Achievements.AchievementService.NoteExternalHpDamage(hpBeforePostDamage - Run.playerHP);
                HpOutsideDiag.Add("層デバフ(戦闘後)", hpBeforePostDamage - Run.playerHP);   // [計装 2026-11-05]
                Log($"層デバフ: 戦闘後{postDamage}ダメージ (HP:{Run.playerHP})");
            }
        }

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
            Log($"偽の商人[貪欲]: 所持する秘宝 {passiveCount}個 → HP {baseEnemy.maxHP}→{enemy.maxHP}, ダイス合計+{diceTotalBonus}/ロール");

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
                && Run.ownedPassiveItems.Contains(ItemIds.ChevalierRapier))
            {
                bossId = BossIds.Layer5Hidden;
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

            // (旧: 5層ボス戦の前に清算デバフ〈血の負債〉(最大HP-15) を払う分岐があったが、 〈血の負債〉を
            //  permanentDebuffs に足す所がどこにも無かった (カルマ系統の廃止の残り)。 2026-11-12 削除)

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

            return (diceCount, diceMax, critRatePct / 100f, diceFaces, faceTiers, suppressTerminalRoles);
        }
    }
}

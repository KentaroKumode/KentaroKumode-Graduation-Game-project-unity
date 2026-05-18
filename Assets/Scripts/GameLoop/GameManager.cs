using System;
using System.Collections.Generic;
using UnityEngine;
using CombatSystem;
using EventSystem;
using InventorySystem;
using InventorySystem.PassiveSkills;
using InventorySystem.Shop;
using MapSystem;

namespace GameLoop
{
    /// <summary>
    /// ゲームのメインループを制御するシングルトン。
    /// マップベース進行: 3レーン×10行のマップを探索し、タイルイベントを処理する。
    /// ビジュアル/UIは別コンポーネントがイベントを購読して実装する想定。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // === シングルトン ===
        public static GameManager Instance { get; private set; }

        // === ゲーム状態 ===
        public enum GamePhase
        {
            Title,
            RunStart,
            FloorIntro,       // 前哨基地処理、マップ表示
            MapNavigation,    // 移動先選択
            Combat,           // 戦闘中
            BattleResult,     // 戦闘結果
            Reward,           // 報酬獲得
            RestStop,         // 休憩（回復 or 強化）
            ShopVisit,        // ショップ
            EventEncounter,   // イベント発生
            TreasureOpen,     // 秘宝
            TrapTriggered,    // 罠発動
            SinRitual,        // 6層祭壇マスでの3段階儀式
            FloorClear,       // ボス撃破→次フロア
            RunClear,         // ラン完了
            GameOver,         // 敗北
        }

        // === 公開プロパティ ===
        public GamePhase CurrentPhase { get; private set; } = GamePhase.Title;
        public RunState Run { get; private set; }
        public EnemyData CurrentEnemy { get; private set; }
        public CombatResult? LastCombatResult { get; private set; }

        /// <summary>激戦の2戦目かどうか</summary>
        public bool IsEliteSecondFight { get; private set; }

        /// <summary>現在フロアのバフ/デバフ</summary>
        public FloorModifier ActiveModifier { get; private set; }

        // === イベント ===
        public event Action<GamePhase> OnPhaseChanged;
        public event Action<RunState> OnRunStarted;
        public event Action<EnemyData> OnEnemyEncountered;
        public event Action<CombatResult> OnBattleEnded;
        public event Action<int> OnRewardGranted;
        public event Action<int> OnFloorAdvanced;
        public event Action<RunState> OnRunCleared;
        public event Action<RunState> OnGameOver;
        public event Action<int> OnStarvationDamage;
        public event Action<TileType> OnTileActivated;
        public event Action<FloorModifier> OnFloorModifierApplied;

        // === 設定 ===
        [Header("ゲーム設定")]
        [SerializeField] private int startingHP = 30;

        [Header("デバッグ")]
        [SerializeField] private bool autoStartRun = false;
        [SerializeField] private bool logPhaseChanges = true;

        // === 内部参照 ===
        private ItemEquipHandler equipHandler;
        private EnemyData firstEliteEnemy;

        // === SinRitual 状態 (3つの儀式それぞれが既に決まったか) ===
        private bool ritualHpResolved;
        private bool ritualGoldResolved;
        private bool ritualItemResolved;

        // === EventEncounter: 選択肢確定後・フレーバー表示中フラグ ===
        private bool eventChoiceResolved;
        // 戦闘トリガで保留している場合、戦闘終了後に MapNavigation へ戻すフラグ
        private bool returnToMapAfterEventCombat;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            // CombatManager のイベント購読
            CombatManager.Instance.OnCombatEnd += HandleCombatEnd;

            // ItemEquipHandler の参照取得
            equipHandler = FindObjectOfType<ItemEquipHandler>();

            if (autoStartRun)
                StartNewRun();
        }

        void OnDestroy()
        {
            if (CombatManager.Instance != null)
                CombatManager.Instance.OnCombatEnd -= HandleCombatEnd;
        }

        // ============================================================
        //  公開API — UI/外部から呼ばれる操作
        // ============================================================

        /// <summary>新しいランを開始</summary>
        public void StartNewRun()
        {
            Run = new RunState();
            Run.Initialize(startingHP);

            // メタ恒久バフを適用（HP/Gold/初期素材を底上げ）
            MetaProgression.MetaBuffApplicator.ApplyToRunStart(Run, startingHP);

            LastCombatResult = null;
            CurrentEnemy = null;
            IsEliteSecondFight = false;

            SetPhase(GamePhase.RunStart);
            OnRunStarted?.Invoke(Run);
            Log($"=== ラン開始 === HP:{Run.playerMaxHP}");

            EnterFloor();
        }

        /// <summary>マップ上のノードへ移動</summary>
        public void MoveToNode(string nodeId)
        {
            Debug.Log($"[GameManager] MoveToNode: {nodeId} (phase={CurrentPhase})");
            if (CurrentPhase != GamePhase.MapNavigation) return;

            var mm = MapManager.Instance;

            // マップ移動時系時限効果（翼の恩寵・警戒心・導きの光等）
            EventSystem.TimedEffects.TimedEffectManager.OnMapMove(Run);

            // 名前付き固有パッシブ（マップ移動時系。現状該当なし、フック確保）
            InventorySystem.PassiveItems.PassiveItemManager.OnMapMove(Run);

            // メタ: ノード踏破トークン
            MetaProgression.MetaTokenEarner.OnNodeVisited();

            int starvDmg = mm.MoveTo(nodeId, Run.playerMaxHP);

            // 恒久デバフ「トゥルハドの暴食」: 飢餓ダメージ ×2 (割合計算が先)
            if (starvDmg > 0 && MetaProgression.PermanentDebuffEffects.HasGluttony(Run))
                starvDmg *= 2;

            // メタバフ: 飢餓ダメ削減（実数計算が後・最低1は残す）
            if (starvDmg > 0)
            {
                int red = MetaProgression.MetaBuffApplicator.GetHungerDamageReduction();
                if (red > 0) starvDmg = Mathf.Max(1, starvDmg - red);
            }

            if (starvDmg > 0)
            {
                Run.playerHP = Mathf.Max(0, Run.playerHP - starvDmg);
                OnStarvationDamage?.Invoke(starvDmg);
                Log($"空腹ダメージ: {starvDmg} (HP: {Run.playerHP}/{Run.playerMaxHP})");

                if (Run.playerHP <= 0)
                {
                    // ちいさな灯火 → ラストスタンドの順で救済を試行
                    if (!LastStand.TryConsumeRevival(Run))
                    {
                        Run.EndRun();
                        SetPhase(GamePhase.GameOver);
                        OnGameOver?.Invoke(Run);
                        return;
                    }
                }
            }

            ActivateTile(mm.CurrentNode);
        }

        /// <summary>戦闘結果を確認→次へ</summary>
        public void ConfirmBattleResult()
        {
            if (CurrentPhase != GamePhase.BattleResult) return;
            if (!LastCombatResult.HasValue) return;

            var result = LastCombatResult.Value;

            // 敗北 or HP0 → 救済（灯火→ラストスタンド）/ なければゲームオーバー
            if (!result.playerWon || Run.playerHP <= 0)
            {
                // ボスマス戦闘では救済を一切行わない（ボス敗北＝ラン終了確定）。
                // ボスノードは収束ノードで出力接続が無く、復活して戻ると進行不能になるため。
                bool isBossFight = MapManager.Instance?.CurrentNode != null
                    && MapManager.Instance.CurrentNode.type == TileType.Boss;

                if (!isBossFight && LastStand.TryConsumeRevival(Run))
                {
                    Log("救済発動: マップへ戻る");
                    SetPhase(GamePhase.MapNavigation);
                    return;
                }
                if (isBossFight)
                    Log("ボス戦敗北: 救済(灯火/ラストスタンド)は無効 → ラン終了");
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

            var mm = MapManager.Instance;
            var node = mm.CurrentNode;

            // メタ: 敵撃破トークン + 戦闘勝利金
            MetaProgression.MetaTokenEarner.OnEnemyDefeated();
            int metaWinGold = MetaProgression.MetaBuffApplicator.GetCombatGoldBonus();
            bool prideActive = MetaProgression.PermanentDebuffEffects.HasPride(Run);

            // ボス勝利 → フロアクリア
            if (node.type == TileType.Boss)
            {
                Run.bossDefeatedThisFloor = true;

                // 1層ボス（トレジャーゴブリン）: 良質な武器/パッシブを1個ドロップ
                if (Run.currentFloor == 1)
                {
                    string dropId = PickTreasureGoblinDrop();
                    if (!string.IsNullOrEmpty(dropId))
                    {
                        Run.ownedPassiveItems.Add(dropId);
                        Loadout.TryAutoEquip(Run, dropId);
                        var dd = ItemDatabase.Instance?.GetItem(dropId);
                        Log($"トレジャーゴブリン討伐報酬: {(dd != null ? dd.displayName : dropId)} を獲得");
                    }
                }

                int rewardBase = FloorManager.CalculateRewardCoins(Run.currentFloor, true, result.totalTurns) * 2;
                // 傲慢: ボスはエリート以上扱いで報酬2倍（割合先）
                if (prideActive) rewardBase *= 2;
                int reward = LastStand.FilterGoldGain(Run, rewardBase + metaWinGold);
                Run.coins += reward;

                // メタ: ボス撃破時の追加パッシブ報酬
                var extra = MetaProgression.MetaBuffApplicator.GetBossExtraDrop();
                if (extra != MetaProgression.MetaBuffApplicator.BossExtraDrop.None)
                {
                    bool wantRare = extra == MetaProgression.MetaBuffApplicator.BossExtraDrop.Rare;
                    string id = PickPassiveItemForBossExtra(wantRare);
                    if (!string.IsNullOrEmpty(id))
                    {
                        Run.ownedPassiveItems.Add(id);
                        Log($"メタ報酬: ボス撃破ボーナス {(wantRare ? "レア" : "ノーマル")}パッシブ獲得: {id}");
                    }
                }

                SetPhase(GamePhase.Reward);
                OnRewardGranted?.Invoke(reward);
                return;
            }

            // 激戦1戦目勝利 → 2戦目
            if (node.EffectiveType == TileType.EliteBattle && !IsEliteSecondFight)
            {
                IsEliteSecondFight = true;
                StartEliteSecondCombat();
                return;
            }

            // 通常報酬（傲慢: 通常戦闘=0、エリート以上=×2、いずれも割合計算が先）
            int coins = FloorManager.CalculateRewardCoins(Run.currentFloor, true, result.totalTurns);
            if (IsEliteSecondFight) coins = (int)(coins * 1.5f);
            if (prideActive)
            {
                if (IsEliteSecondFight) coins *= 2;
                else coins = 0;
            }
            coins += metaWinGold;
            coins = LastStand.FilterGoldGain(Run, coins);
            Run.coins += coins;
            IsEliteSecondFight = false;

            SetPhase(GamePhase.Reward);
            OnRewardGranted?.Invoke(coins);
        }

        /// <summary>ボス撃破ボーナス用のパッシブ抽選。wantRare=true で GOLD 以上のみ、レア度重み付き。</summary>
        private string PickPassiveItemForBossExtra(bool wantRare)
        {
            var db = ItemDatabase.Instance;
            if (db == null) return null;
            var all = db.GetAllItems();
            if (all == null || all.Count == 0) return null;

            var pool = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in all)
            {
                if (it == null) continue;
                if (it.category != ItemCategory.Passive) continue;
                if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                pool.Add(it);
            }
            if (pool.Count == 0) return null;

            ItemRarity? minRarity = wantRare ? ItemRarity.GOLD : (ItemRarity?)null;
            var picked = InventorySystem.RarityWeightedPicker.Pick(pool, minRarity);
            if (picked == null && wantRare)
            {
                // フォールバック: レア該当無しなら全プールから
                picked = InventorySystem.RarityWeightedPicker.Pick(pool);
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

            var pool = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in all)
            {
                if (it == null) continue;
                if (it.category != ItemCategory.Weapon && it.category != ItemCategory.Passive) continue;
                if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                pool.Add(it);
            }
            if (pool.Count == 0) return null;

            // 「いい感じ」: SILVER 以上を優先抽選、該当無しなら全プール
            var picked = InventorySystem.RarityWeightedPicker.Pick(pool, ItemRarity.SILVER)
                         ?? InventorySystem.RarityWeightedPicker.Pick(pool);
            return picked?.internalName;
        }

        /// <summary>報酬確認→マップに戻る or フロアクリア</summary>
        public void ConfirmReward()
        {
            if (CurrentPhase != GamePhase.Reward) return;

            if (Run.bossDefeatedThisFloor)
            {
                HandleFloorClear();
                return;
            }

            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>タイルイベント完了→マップに戻る</summary>
        public void ConfirmTileEvent()
        {
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>休憩でHP回復を選択</summary>
        public void RestHeal()
        {
            if (CurrentPhase != GamePhase.RestStop) return;
            float ratio = ActiveModifier?.restHealMultiplier ?? 0.3f;
            int heal = Mathf.CeilToInt(Run.playerMaxHP * ratio);
            Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
            Log($"休憩回復: +{heal}HP ({(int)(ratio*100)}%) (現在: {Run.playerHP}/{Run.playerMaxHP})");
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>休憩で強化を選択（スタブ）</summary>
        /// <summary>1段階の武器強化に必要な素材数（レベルが上がるほど高くなる）。</summary>
        public static int WeaponUpgradeCost(int currentLevel) => 2 + currentLevel;

        public void RestUpgrade()
        {
            if (CurrentPhase != GamePhase.RestStop) return;

            int cost = WeaponUpgradeCost(Run.weaponUpgradeLevel);
            if (Run.weaponMaterials >= cost)
            {
                Run.weaponMaterials -= cost;
                Run.weaponUpgradeLevel++;
                Log($"武器強化: Lv{Run.weaponUpgradeLevel} (素材-{cost}, 残{Run.weaponMaterials})");
                SetPhase(GamePhase.MapNavigation);
            }
            else
            {
                // 素材不足なら休憩を無駄にせず回復にフォールバック
                Log($"武器強化 素材不足 (必要{cost}/所持{Run.weaponMaterials}) → 回復に切替");
                RestHeal();
            }
        }

        /// <summary>フロアクリア確認→次フロアへ</summary>
        public void ConfirmFloorClear()
        {
            if (CurrentPhase != GamePhase.FloorClear) return;

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
            var mm = MapManager.Instance;

            // メタ: トークン獲得 + 1層/3層で固有恒久デバフ抽選
            MetaProgression.MetaTokenEarner.OnFloorReached(Run.currentFloor);
            if (Run.currentFloor == 1) MetaProgression.MetaDebuffApplicator.TryGrantOnFloor1(Run);
            if (Run.currentFloor == 3) MetaProgression.MetaDebuffApplicator.TryGrantOnFloor3(Run);

            // 恒久デバフ「ムシュファの強欲」: 5層突入時に所持ゴールド0
            if (Run.currentFloor == 5 && MetaProgression.PermanentDebuffEffects.HasGreed(Run))
            {
                Log($"恒久デバフ {MetaProgression.PermanentDebuffIds.Greed}: 所持ゴールド {Run.coins}→0");
                Run.coins = 0;
            }

            // 層デバフ取得
            ActiveModifier = FloorModifierDatabase.Get(Run.currentFloor);

            // マップ生成（空腹度上限はModifierで調整）
            mm.GenerateFloor(Run.currentFloor);

            // 空腹度ボーナス適用
            if (ActiveModifier != null && ActiveModifier.hungerMaxBonus != 0)
            {
                int newMax = mm.Hunger.Max + ActiveModifier.hungerMaxBonus;
                mm.Hunger.Initialize(Mathf.Max(1, newMax));
            }

            // メタデバフ Lv8: ハンガー初期値 -2/フロア
            int hungerPenalty = MetaProgression.MetaDebuffApplicator.GetHungerInitialPenalty();
            if (hungerPenalty > 0 && mm.Hunger != null)
                mm.Hunger.SetCurrentForTest(Mathf.Max(0, mm.Hunger.Current - hungerPenalty));

            // 飢餓ダメージ倍率上書き
            if (ActiveModifier != null && ActiveModifier.starvationDamageOverride >= 0)
                mm.Hunger.starvationDamageRatio = ActiveModifier.starvationDamageOverride;

            mm.ProcessOutpost();

            // メタデバフ Lv9 で前哨基地効果を無効化
            bool forwardBaseDisabled = MetaProgression.MetaDebuffApplicator.IsForwardBaseDisabled();
            int healCap = MetaProgression.MetaDebuffApplicator.GetForwardBaseHealCap(Run.playerMaxHP); // Lv7

            if (forwardBaseDisabled)
            {
                Log("前哨基地は崩壊している（メタデバフ Lv9）");
                // MaxHPボーナス含めてスキップ
            }
            else if (ActiveModifier != null && ActiveModifier.maxHPBonusFlat != 0)
            {
                // 6層前哨基地: HP全回復 + MaxHP+5（Lv7 で上限制限される）
                Run.playerMaxHP += ActiveModifier.maxHPBonusFlat;
                int upper = healCap < 0 ? Run.playerMaxHP : Mathf.Min(healCap, Run.playerMaxHP);
                Run.playerHP = upper;
                Log($"前哨基地: MaxHP+{ActiveModifier.maxHPBonusFlat} → {Run.playerMaxHP}, HP={Run.playerHP}");
            }
            else
            {
                // 通常前哨基地: HP30%回復（Lv7 で上限制限される）
                int heal = Mathf.CeilToInt(Run.playerMaxHP * 0.3f);
                int upper = healCap < 0 ? Run.playerMaxHP : Mathf.Min(healCap, Run.playerMaxHP);
                Run.playerHP = Mathf.Min(upper, Run.playerHP + heal);
            }

            // MaxHPデバフ（崩れの共鳴等）
            if (ActiveModifier != null && ActiveModifier.maxHPBonus != 0)
            {
                Run.playerMaxHP = Mathf.Max(1, Run.playerMaxHP + ActiveModifier.maxHPBonus);
                Run.playerHP = Mathf.Min(Run.playerHP, Run.playerMaxHP);
                Log($"層デバフ: MaxHP{ActiveModifier.maxHPBonus:+0;-0} → {Run.playerMaxHP}");
            }

            SetPhase(GamePhase.FloorIntro);
            Log($"--- フロア {Run.currentFloor} [{ActiveModifier?.displayName ?? "なし"}] ---");
            OnFloorModifierApplied?.Invoke(ActiveModifier);

            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>戦闘終了ハンドラ</summary>
        private void HandleCombatEnd(CombatResult result)
        {
            LastCombatResult = result;
            Run.ApplyBattleResult(result.playerWon, result.playerHPRemaining, result.totalTurns);

            SetPhase(GamePhase.BattleResult);
            OnBattleEnded?.Invoke(result);

            string resultText = result.playerWon ? "勝利" : "敗北";
            Log($"戦闘結果: {result.enemyDisplayName} — {resultText} ({result.totalTurns}T) 残HP:{result.playerHPRemaining}");

            // 層デバフ: 戦闘後固定ダメージ（瘀気侵蟀等）
            if (ActiveModifier != null && ActiveModifier.postCombatDamage > 0 && Run.playerHP > 0)
            {
                Run.playerHP = Mathf.Max(1, Run.playerHP - ActiveModifier.postCombatDamage);
                Log($"層デバフ: 戦闘後{ActiveModifier.postCombatDamage}ダメージ (HP:{Run.playerHP})");
            }
        }

        /// <summary>タイルの種類に応じてフェーズを遷移</summary>
        private void ActivateTile(MapNode node)
        {
            var effectiveType = node.EffectiveType;
            OnTileActivated?.Invoke(effectiveType);
            Log($"タイル起動: {TileToJapanese(effectiveType)} ({node.id})");

            // 翼の恩寵: トラップマス1回無効
            if (effectiveType == TileType.Trap && Run.HasTimedBuff("翼の恩寵"))
            {
                Run.timedBuffs.Remove("翼の恩寵");
                Log("翼の恩寵発動: トラップ無効化");
                SetPhase(GamePhase.MapNavigation);
                return;
            }

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
                    EnterShop();
                    break;
                case TileType.Event:
                    BeginEventEncounter();
                    break;
                case TileType.Treasure:
                    OpenTreasure();
                    break;
                case TileType.Trap:
                    HandleTrapTile();
                    break;
                case TileType.SinAltar:
                    BeginSinRitual();
                    break;
                default:
                    SetPhase(GamePhase.MapNavigation);
                    break;
            }
        }

        /// <summary>罠マス到達時の処理。5層ボス前の専用罠 (id=karma_trap) ではカルマ清算、それ以外は無効。</summary>
        private void HandleTrapTile()
        {
            var node = MapManager.Instance?.CurrentNode;
            bool isKarmaTrap = node != null && node.id == "karma_trap";

            // ボス前専用罠でのカルマ清算（最大HP -= カルマ × 10、最低1。清算後 karma=0）
            if (isKarmaTrap)
            {
                if (Run.karma > 0)
                {
                    int desired = Run.karma * 10;
                    int newMax = Mathf.Max(1, Run.playerMaxHP - desired);
                    int actual = Run.playerMaxHP - newMax;
                    Run.playerMaxHP = newMax;
                    Run.playerHP = Mathf.Min(Run.playerHP, Run.playerMaxHP);
                    Log($"罪の罠: カルマ清算 最大HP-{actual} (カルマ{Run.karma}×10) → {Run.playerMaxHP}");
                    Run.karma = 0;
                }
                else
                {
                    Log("罪の罠: カルマ無し、何も起きず通過");
                }
            }
            // それ以外の通常罠マスは現状効果なし

            SetPhase(GamePhase.TrapTriggered);
        }

        private void StartBattleTile()
        {
            CurrentEnemy = FloorManager.PickEnemy(Run.currentFloor);
            if (CurrentEnemy == null)
            {
                Debug.LogError("[GameManager] 敵の選出に失敗");
                SetPhase(GamePhase.MapNavigation);
                return;
            }

            if (firstEliteEnemy == null && MapManager.Instance.CurrentNode.EffectiveType == TileType.EliteBattle)
                firstEliteEnemy = CurrentEnemy;

            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"エンカウント: {CurrentEnemy.displayName}");

            var (dc, dm, cr, df) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df);
        }

        private void StartEliteSecondCombat()
        {
            var candidates = new System.Collections.Generic.List<EnemyData>(EnemyDatabase.GetByFloor(Run.currentFloor));
            // 激戦2戦目もボス専用敵(boss_layer*)を除外（残存ボス漏れの修正）
            candidates.RemoveAll(e => e.id == firstEliteEnemy?.id
                || (e.id != null && e.id.StartsWith("boss_layer")));

            if (candidates.Count > 0)
                CurrentEnemy = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            else
                CurrentEnemy = FloorManager.PickEnemy(Run.currentFloor);

            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"激戦2戦目: {CurrentEnemy.displayName}");

            var (dc, dm, cr, df) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df);
        }

        private void StartBossTile()
        {
            // ボス専用ID で取得、なければフロアプール抽選にフォールバック
            string bossId = $"boss_layer{Run.currentFloor}";
            CurrentEnemy = CombatSystem.EnemyDatabase.Get(bossId);
            if (CurrentEnemy == null)
            {
                Debug.LogWarning($"[GameManager] ボス '{bossId}' 未定義 — フロア{Run.currentFloor}のプールから抽選");
                CurrentEnemy = FloorManager.PickEnemy(Run.currentFloor);
            }
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"ボス戦: {CurrentEnemy.displayName}");

            // 5層ボス戦の清算系デバフ（カルマ清算は罠マスへ移動済み）
            if (Run.currentFloor == Run.normalClearFloor)
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

            var (dc, dm, cr, df) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df);
        }

        // ================================================================
        //  6層 SinAltar 儀式
        // ================================================================

        /// <summary>祭壇マス到達時に呼ばれ、SinRitual フェーズへ遷移する。</summary>
        private void BeginSinRitual()
        {
            ritualHpResolved = false;
            ritualGoldResolved = false;
            ritualItemResolved = false;
            SetPhase(GamePhase.SinRitual);
            Log("祭壇に辿り着いた。3つの儀式を捧げよ。");
        }

        /// <summary>「血の儀」: 現在HPの 30% を支払う。失敗で HeartOfGolgotha 付与。</summary>
        public void OfferHpSacrifice(bool accept)
        {
            if (CurrentPhase != GamePhase.SinRitual) return;

            int demand = Mathf.Max(1, Mathf.CeilToInt(Run.playerHP * 0.3f));
            if (accept && Run.playerHP > demand)
            {
                Run.playerHP -= demand;
                Log($"血の儀: HP {demand} を捧げた (残 {Run.playerHP})");
            }
            else
            {
                Run.AddDebuff(SinDebuff.HeartOfGolgotha);
                Log("血の儀を拒んだ。〈ゴルゴダの心〉が刻まれる。");
            }
        }

        /// <summary>「貪欲の儀」: 所持金 50% を支払う。失敗で SeveredTime 付与。</summary>
        public void OfferGoldSacrifice(bool accept)
        {
            if (CurrentPhase != GamePhase.SinRitual) return;

            int demand = Mathf.Max(1, Run.coins / 2);
            if (accept && Run.coins >= demand)
            {
                Run.coins -= demand;
                Log($"貪欲の儀: コイン {demand} を捧げた (残 {Run.coins})");
            }
            else
            {
                Run.AddDebuff(SinDebuff.SeveredTime);
                Log("貪欲の儀を拒んだ。〈断絶した時間〉が刻まれる。");
            }
        }

        /// <summary>「遺品の儀」: イベントアイテム 1個 を消費。失敗で AshenBrand 付与。</summary>
        /// <param name="hasItemAndAccept">所持していて、かつ捧げる選択をしたか</param>
        public void OfferItemSacrifice(bool hasItemAndAccept)
        {
            if (CurrentPhase != GamePhase.SinRitual) return;

            if (hasItemAndAccept)
            {
                // TODO: イベントアイテム実装後、実際にインベントリから1個消費する
                Log("遺品の儀: 遺品を捧げた");
            }
            else
            {
                Run.AddDebuff(SinDebuff.AshenBrand);
                Log("遺品の儀を拒んだ。〈灰燼の烙印〉が刻まれる。");
            }
        }

        // ================================================================
        //  ショップマス
        // ================================================================

        /// <summary>ショップ売却モード（true=売却、false=購入）</summary>
        public bool ShopSellMode { get; private set; }

        /// <summary>ショップ売却モードでの売却対象種別</summary>
        public ShopManager.SellSource ShopSellSource { get; private set; } = ShopManager.SellSource.Passive;

        /// <summary>ショップマス到達時。マップ巻取り → 在庫生成 → ShopVisit フェーズへ。</summary>
        private void EnterShop()
        {
            Debug.Log("[GameManager] EnterShop called");
            ShopSellMode = false;
            ShopSellSource = ShopManager.SellSource.Passive;

            var transition = MapSystem.Visual.MapTransitionController.Instance;
            Debug.Log($"[GameManager] MapTransitionController.Instance = {(transition == null ? "null" : "exists")}");

            if (transition != null)
            {
                transition.RollUp(() =>
                {
                    Debug.Log("[GameManager] RollUp callback fired");
                    var sm = ShopManager.Instance;
                    if (sm == null) { Debug.LogError("[GameManager] ShopManager.Instance is null!"); return; }
                    sm.Generate(Run.currentFloor);
                    SetPhase(GamePhase.ShopVisit);
                });
            }
            else
            {
                var sm = ShopManager.Instance;
                if (sm == null) { Debug.LogError("[GameManager] ShopManager.Instance is null!"); return; }
                sm.Generate(Run.currentFloor);
                SetPhase(GamePhase.ShopVisit);
            }
        }

        public void ToggleShopSellMode()
        {
            if (CurrentPhase != GamePhase.ShopVisit) return;
            ShopSellMode = !ShopSellMode;
            Log($"ショップモード: {(ShopSellMode ? "売却" : "購入")}");
        }

        public void CycleSellSource()
        {
            if (CurrentPhase != GamePhase.ShopVisit || !ShopSellMode) return;
            ShopSellSource = (ShopManager.SellSource)(((int)ShopSellSource + 1) % 3);
            Log($"売却対象: {ShopSellSource}");
        }

        public void ShopBuy(int slotIndex)
        {
            if (CurrentPhase != GamePhase.ShopVisit) return;
            ShopManager.Instance.TryBuy(slotIndex, Run);
        }

        public void ShopSell(int listIndex)
        {
            if (CurrentPhase != GamePhase.ShopVisit) return;
            ShopManager.Instance.TrySell(ShopSellSource, listIndex, Run);
        }

        public void ExitShop()
        {
            if (CurrentPhase != GamePhase.ShopVisit) return;
            ShopManager.Instance.Close();

            var transition = MapSystem.Visual.MapTransitionController.Instance;
            if (transition != null)
            {
                transition.Unroll(() => SetPhase(GamePhase.MapNavigation));
            }
            else
            {
                SetPhase(GamePhase.MapNavigation);
            }
        }

        // ================================================================
        //  宝箱マス
        // ================================================================

        /// <summary>最後に開けた宝箱の中身（HUD表示用）</summary>
        public string LastTreasureSummary { get; private set; }

        /// <summary>宝箱マス到達時。ゴールドランダム + パッシブ/消費アイテム1個。</summary>
        private void OpenTreasure()
        {
            int floor = Run.currentFloor;
            int rawGold = floor * UnityEngine.Random.Range(2, 8); // 半減後: 1層:2-7 〜 6層:12-42
            int gold = LastStand.FilterGoldGain(Run, rawGold);
            Run.coins += gold;

            string itemId = PickRandomTreasureItem();
            string itemLabel = "（なし）";
            if (!string.IsNullOrEmpty(itemId))
            {
                Run.ownedPassiveItems.Add(itemId);
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

            var pool = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in all)
            {
                if (it == null) continue;
                if (it.category == ItemCategory.Weapon) continue;
                if (it.category == ItemCategory.Quest) continue;
                if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                pool.Add(it);
            }
            if (pool.Count == 0) return null;

            // 鑑定の眼鏡: 次の宝箱の最低レア保証（消費）。ショップで既消費なら -1。
            CompleteItemData picked;
            if (Run != null && Run.nextLootMinRarity >= 0)
            {
                var minR = (ItemRarity)Run.nextLootMinRarity;
                Run.nextLootMinRarity = -1;
                picked = InventorySystem.RarityWeightedPicker.Pick(pool, minR)
                         ?? InventorySystem.RarityWeightedPicker.Pick(pool);
            }
            else
            {
                picked = InventorySystem.RarityWeightedPicker.Pick(pool);
            }
            return picked?.internalName;
        }

        // ================================================================
        //  イベントエンカウンタ
        // ================================================================

        /// <summary>イベントマスに到達したときに呼ばれる。抽選 → EventEncounter へ。</summary>
        private void BeginEventEncounter()
        {
            eventChoiceResolved = false;
            returnToMapAfterEventCombat = false;

            var ee = EventEncounter.Instance;
            if (ee == null)
            {
                Debug.LogWarning("[GameManager] EventEncounter シングルトン未配置 — マップに戻る");
                SetPhase(GamePhase.MapNavigation);
                return;
            }

            bool ok = ee.Begin(Run);
            if (!ok || ee.Current == null)
            {
                // イベント0件/抽選失敗なら何もせずマップへ（ソフトロック防止）
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            // メタ: イベント発見トークン
            MetaProgression.MetaTokenEarner.OnEventEncountered();
            SetPhase(GamePhase.EventEncounter);
        }

        /// <summary>イベントの選択肢 i を選ぶ（UI / デバッグから呼ぶ）。</summary>
        public void ResolveEventChoice(int index)
        {
            if (CurrentPhase != GamePhase.EventEncounter) return;
            var ee = EventEncounter.Instance;
            if (ee?.Current == null) return;
            if (eventChoiceResolved) return;

            var result = ee.ResolveChoice(index);
            eventChoiceResolved = true;
            if (result == null) return;

            // 戦闘トリガがあれば即時遷移（フレーバー表示は後回し）
            if (result.triggerEliteCombat)
            {
                returnToMapAfterEventCombat = true;
                StartEventCombat(elite: true);
                return;
            }
            if (result.triggerCombat)
            {
                returnToMapAfterEventCombat = true;
                StartEventCombat(elite: false);
                return;
            }

            // ランダムイベント発生: RandomEvent を含まないプールから1回だけ
            // 別イベントへ振り直す。振り直し先は RandomEvent を持たないため
            // 連鎖は構造的に発生しない。抽選失敗時は通常終了。
            if (result.triggerRandomEvent)
            {
                if (ee.Begin(Run, excludeRandomEvent: true) && ee.Current != null)
                    eventChoiceResolved = false;  // 振り直し先を改めて解決させる
                else
                    eventChoiceResolved = true;   // 該当なし → 通常終了
                return;
            }

            // 通常はフレーバー表示 → Space で完了
        }

        /// <summary>イベント完了（フレーバー読了） → マップへ戻る。</summary>
        public void ConfirmEventEncounter()
        {
            if (CurrentPhase != GamePhase.EventEncounter) return;
            // 通常は選択確定後のみ。ただし Current==null（抽選失敗/消失）の場合は
            // ソフトロック回避のため未確定でも強制的にマップへ戻す。
            bool curNull = EventEncounter.Instance == null || EventEncounter.Instance.Current == null;
            if (!eventChoiceResolved && !curNull) return;
            EventEncounter.Instance?.Clear();
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>イベントから戦闘を開始する。</summary>
        private void StartEventCombat(bool elite)
        {
            CurrentEnemy = FloorManager.PickEnemy(Run.currentFloor);
            if (CurrentEnemy == null)
            {
                Debug.LogError("[GameManager] イベント戦闘の敵選出失敗");
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"イベント戦闘: {CurrentEnemy.displayName} (elite={elite})");

            var (dc, dm, cr, df) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df);
        }

        /// <summary>3つの儀式の選択をすべて完了して MapNavigation に戻る。</summary>
        public void CompleteSinRitual()
        {
            if (CurrentPhase != GamePhase.SinRitual) return;
            Log($"儀式完了。刻まれた呪い: {Run.sinDebuffs}");
            SetPhase(GamePhase.MapNavigation);
        }

        private void HandleFloorClear()
        {
            // 5層クリア時: 「決意」所持なら6層挑戦に進む、なければランクリア確定
            if (Run.IsNormalClear && Run.currentFloor == Run.normalClearFloor)
            {
                bool hasResolve = Run.ownedPassiveItems != null && Run.ownedPassiveItems.Contains("決意");
                if (hasResolve)
                {
                    SetPhase(GamePhase.FloorClear);
                    OnFloorAdvanced?.Invoke(Run.currentFloor + 1);
                    Log($"フロア{Run.currentFloor}クリア → 決意により裏ボス挑戦へ");
                    return;
                }
                Run.EndRun();
                SetPhase(GamePhase.RunClear);
                OnRunCleared?.Invoke(Run);
                Log($"=== ランクリア！=== (Floor {Run.currentFloor})");
            }
            else if (Run.IsFullClear)
            {
                Run.EndRun();
                SetPhase(GamePhase.RunClear);
                OnRunCleared?.Invoke(Run);
                Log($"=== 完全クリア！=== 裏ボス撃破");
            }
            else
            {
                SetPhase(GamePhase.FloorClear);
                OnFloorAdvanced?.Invoke(Run.currentFloor + 1);
                Log($"フロア{Run.currentFloor}クリア → 次へ");
            }
        }

        /// <summary>装備中の武器・ダイスからステータスを取得</summary>
        private (int diceCount, int diceMax, int critRate, int[] diceFaces) GatherPlayerCombatStats()
        {
            // ItemEquipHandler を探す（初回のみ）
            if (equipHandler == null)
                equipHandler = FindObjectOfType<ItemEquipHandler>();

            int diceCount = 2;
            int diceMax = 6;
            int critRate = 1;
            int[] diceFaces = null;

            bool weaponResolved = false;
            bool diceResolved = false;

            if (equipHandler != null)
            {
                var weapon = equipHandler.GetCurrentEquipment(ItemCategory.Weapon);
                if (weapon != null && weapon.hasWeaponStats)
                {
                    diceCount = weapon.weaponDice.count;
                    diceMax = weapon.weaponDice.maxValue;
                    critRate = weapon.criticalRate;
                    weaponResolved = true;
                }

                var dice = equipHandler.GetCurrentEquipment(ItemCategory.Dice);
                if (dice != null && dice.diceFaces != null)
                {
                    diceFaces = dice.diceFaces;
                    diceResolved = true;
                }
            }

            // ItemEquipHandler が無い/未装備なら RunState の自動装備IDから解決
            if (!weaponResolved && Run != null && !string.IsNullOrEmpty(Run.equippedWeaponId))
            {
                var w = ItemDatabase.Instance?.GetItem(Run.equippedWeaponId);
                if (w != null && w.hasWeaponStats)
                {
                    diceCount = w.weaponDice.count;
                    diceMax = w.weaponDice.maxValue;
                    critRate = w.criticalRate;
                }
            }
            if (!diceResolved && Run != null && !string.IsNullOrEmpty(Run.equippedDiceId))
            {
                var d = ItemDatabase.Instance?.GetItem(Run.equippedDiceId);
                if (d != null && d.diceFaces != null)
                    diceFaces = d.diceFaces;
            }

            // 武器強化レベル反映: Lvごとに diceMax+1、2Lvごとに diceCount+1。
            // （カスタムダイス装備時は faces が面を上書きするため diceMax 分は無効だが、
            //   diceCount 分は常に有効＝強化が無駄にならない）
            int upLv = Run?.weaponUpgradeLevel ?? 0;
            bool ryusen = Run != null && Run.equippedWeaponId == "ryusen";
            if (upLv > 0 && !ryusen) // 竜閃(無我無心)は強化補正も受けない
            {
                diceMax += upLv;
                diceCount += upLv / 2;
            }

            // 層デバフ適用
            if (ActiveModifier != null)
            {
                if (ActiveModifier.diceMaxBonus != 0)
                    diceMax = Mathf.Max(1, diceMax + ActiveModifier.diceMaxBonus);
                if (ActiveModifier.critRateBonus != 0)
                    critRate = Mathf.Clamp(critRate + ActiveModifier.critRateBonus, 0, 9);
            }

            // 影の代償の出目-1 はロール時に50%確率で発動するため、ここでは何もしない。
            // 実適用は CombatManager.ExecuteTurn のロール直後で処理。

            return (diceCount, diceMax, critRate, diceFaces);
        }

        /// <summary>フェーズ遷移</summary>
        private void SetPhase(GamePhase newPhase)
        {
            var prev = CurrentPhase;
            CurrentPhase = newPhase;
            OnPhaseChanged?.Invoke(newPhase);

            if (logPhaseChanges)
                Debug.Log($"[GameManager] Phase: {prev} → {newPhase}");
        }

        private void Log(string msg)
        {
            if (logPhaseChanges)
                Debug.Log($"[GameManager] {msg}");
        }

        /// <summary>タイルタイプの日本語名</summary>
        public static string TileToJapanese(TileType type)
        {
            switch (type)
            {
                case TileType.Outpost:     return "前哨基地";
                case TileType.Battle:      return "戦闘";
                case TileType.EliteBattle: return "激戦";
                case TileType.Rest:        return "休憩";
                case TileType.Treasure:    return "秘宝";
                case TileType.Shop:        return "ショップ";
                case TileType.Event:       return "イベント";
                case TileType.Mystery:     return "？";
                case TileType.Trap:        return "罠";
                case TileType.Boss:        return "ボス";
                case TileType.SinAltar:    return "祭壇";
                default:                   return type.ToString();
            }
        }

        // ============================================================
        //  デバッグ用キーバインド
        // ============================================================

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.G))
            {
                Debug.Log($"[GameManager] G pressed (phase={CurrentPhase})");
                if (CurrentPhase == GamePhase.Title)
                    StartNewRun();
                else
                    Debug.LogWarning($"[GameManager] G無視: フェーズが Title でない (現在={CurrentPhase})。/clear や autoStartRun=true で既に走っている可能性あり");
            }

            // マップナビゲーション: 数字キーで移動先選択
            if (CurrentPhase == GamePhase.MapNavigation)
            {
                var moves = MapManager.Instance?.GetAvailableMoves();
                if (moves != null)
                {
                    for (int i = 0; i < Mathf.Min(moves.Count, 9); i++)
                    {
                        if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                        {
                            MoveToNode(moves[i].id);
                            return;
                        }
                    }
                }
                return;
            }

            // SinRitual 中の儀式選択
            if (CurrentPhase == GamePhase.SinRitual)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) && !ritualHpResolved)
                {
                    OfferHpSacrifice(true);
                    ritualHpResolved = true;
                }
                if (Input.GetKeyDown(KeyCode.Alpha2) && !ritualGoldResolved)
                {
                    OfferGoldSacrifice(true);
                    ritualGoldResolved = true;
                }
                if (Input.GetKeyDown(KeyCode.Alpha3) && !ritualItemResolved)
                {
                    OfferItemSacrifice(true);
                    ritualItemResolved = true;
                }

                if (Input.GetKeyDown(KeyCode.Space))
                {
                    // 未解決の儀式は拒んだ扱いでデバフを確定
                    if (!ritualHpResolved)   OfferHpSacrifice(false);
                    if (!ritualGoldResolved) OfferGoldSacrifice(false);
                    if (!ritualItemResolved) OfferItemSacrifice(false);
                    CompleteSinRitual();
                }
                return;
            }

            // ShopVisit 中: Esc 退店、T 売買モード切替、S 売却対象切替、1-9 購入/売却
            if (CurrentPhase == GamePhase.ShopVisit)
            {
                // 購入ダイアログ表示中はキー入力をダイアログ側に渡さない
                var dialog = InventorySystem.Shop.Visual.ShopPurchaseDialog.Instance;
                if (dialog != null && dialog.IsOpen)
                {
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Y))
                        dialog.ConfirmPurchase();
                    else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.N))
                        dialog.Close();
                    return;
                }

                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    ExitShop();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.T))
                {
                    ToggleShopSellMode();
                    return;
                }
                if (ShopSellMode && Input.GetKeyDown(KeyCode.S))
                {
                    CycleSellSource();
                    return;
                }
                for (int i = 0; i < 9; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    {
                        if (ShopSellMode) ShopSell(i);
                        else ShopBuy(i);
                        return;
                    }
                }
                return;
            }

            // EventEncounter 中: 1～9 で選択肢、Space でフレーバー読了
            if (CurrentPhase == GamePhase.EventEncounter)
            {
                if (!eventChoiceResolved)
                {
                    var ev = EventEncounter.Instance?.Current;
                    if (ev != null)
                    {
                        for (int i = 0; i < Mathf.Min(ev.choices.Count, 9); i++)
                        {
                            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                            {
                                ResolveEventChoice(i);
                                return;
                            }
                        }
                    }
                }
                else
                {
                    if (Input.GetKeyDown(KeyCode.Space))
                    {
                        ConfirmEventEncounter();
                        return;
                    }
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                switch (CurrentPhase)
                {
                    case GamePhase.Combat:
                        if (CombatManager.Instance.IsCombatActive)
                            CombatManager.Instance.ExecuteTurn();
                        break;
                    case GamePhase.BattleResult:
                        ConfirmBattleResult();
                        break;
                    case GamePhase.Reward:
                        ConfirmReward();
                        break;
                    case GamePhase.RestStop:
                        RestHeal();
                        break;
                    case GamePhase.TreasureOpen:
                    case GamePhase.TrapTriggered:
                        ConfirmTileEvent();
                        break;
                    case GamePhase.FloorClear:
                        ConfirmFloorClear();
                        break;
                    case GamePhase.RunClear:
                    case GamePhase.GameOver:
                        ReturnToTitle();
                        break;
                }
            }

            if (Input.GetKeyDown(KeyCode.F) && CurrentPhase == GamePhase.Combat)
            {
                if (CombatManager.Instance.IsCombatActive)
                    CombatManager.Instance.ExecuteFullCombat();
            }
        }
    }
}

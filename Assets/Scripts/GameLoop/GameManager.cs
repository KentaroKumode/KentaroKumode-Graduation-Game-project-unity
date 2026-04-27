using System;
using System.Collections.Generic;
using UnityEngine;
using CombatSystem;
using InventorySystem;
using InventorySystem.PassiveSkills;
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
            if (CurrentPhase != GamePhase.MapNavigation) return;

            var mm = MapManager.Instance;
            int starvDmg = mm.MoveTo(nodeId, Run.playerMaxHP);

            if (starvDmg > 0)
            {
                Run.playerHP = Mathf.Max(0, Run.playerHP - starvDmg);
                OnStarvationDamage?.Invoke(starvDmg);
                Log($"空腹ダメージ: {starvDmg} (HP: {Run.playerHP}/{Run.playerMaxHP})");

                if (Run.playerHP <= 0)
                {
                    Run.EndRun();
                    SetPhase(GamePhase.GameOver);
                    OnGameOver?.Invoke(Run);
                    return;
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

            // 敗北 or HP0 → ゲームオーバー
            if (!result.playerWon || Run.playerHP <= 0)
            {
                Run.EndRun();
                SetPhase(GamePhase.GameOver);
                OnGameOver?.Invoke(Run);
                return;
            }

            var mm = MapManager.Instance;
            var node = mm.CurrentNode;

            // ボス勝利 → フロアクリア
            if (node.type == TileType.Boss)
            {
                Run.bossDefeatedThisFloor = true;
                int reward = FloorManager.CalculateRewardCoins(Run.currentFloor, true, result.totalTurns) * 2;
                Run.coins += reward;
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

            // 通常報酬
            int coins = FloorManager.CalculateRewardCoins(Run.currentFloor, true, result.totalTurns);
            if (IsEliteSecondFight) coins = (int)(coins * 1.5f);
            Run.coins += coins;
            IsEliteSecondFight = false;

            SetPhase(GamePhase.Reward);
            OnRewardGranted?.Invoke(coins);
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
        public void RestUpgrade()
        {
            if (CurrentPhase != GamePhase.RestStop) return;
            Log("休憩強化（未実装）");
            SetPhase(GamePhase.MapNavigation);
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

            // 飢餓ダメージ倍率上書き
            if (ActiveModifier != null && ActiveModifier.starvationDamageOverride >= 0)
                mm.Hunger.starvationDamageRatio = ActiveModifier.starvationDamageOverride;

            mm.ProcessOutpost();

            // 6層前哨基地: HP全回復 + MaxHP+5
            if (ActiveModifier != null && ActiveModifier.maxHPBonusFlat != 0)
            {
                Run.playerMaxHP += ActiveModifier.maxHPBonusFlat;
                Run.playerHP = Run.playerMaxHP;
                Log($"前哨基地: MaxHP+{ActiveModifier.maxHPBonusFlat} → {Run.playerMaxHP}, HP全回復");
            }
            else
            {
                // 通常前哨基地: HP30%回復
                int heal = Mathf.CeilToInt(Run.playerMaxHP * 0.3f);
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
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
                    SetPhase(GamePhase.ShopVisit);
                    break;
                case TileType.Event:
                    SetPhase(GamePhase.EventEncounter);
                    break;
                case TileType.Treasure:
                    SetPhase(GamePhase.TreasureOpen);
                    break;
                case TileType.Trap:
                    SetPhase(GamePhase.TrapTriggered);
                    break;
                default:
                    SetPhase(GamePhase.MapNavigation);
                    break;
            }
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
            candidates.RemoveAll(e => e.id == firstEliteEnemy?.id);

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
            // TODO: ボス専用のEnemyData選出
            CurrentEnemy = FloorManager.PickEnemy(Run.currentFloor);
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"ボス戦: {CurrentEnemy.displayName}");

            var (dc, dm, cr, df) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df);
        }

        private void HandleFloorClear()
        {
            if (Run.IsNormalClear && Run.currentFloor == Run.normalClearFloor)
            {
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

            if (equipHandler != null)
            {
                var weapon = equipHandler.GetCurrentEquipment(ItemCategory.Weapon);
                if (weapon != null && weapon.hasWeaponStats)
                {
                    diceCount = weapon.weaponDice.count;
                    diceMax = weapon.weaponDice.maxValue;
                    critRate = weapon.criticalRate;
                }

                var dice = equipHandler.GetCurrentEquipment(ItemCategory.Dice);
                if (dice != null && dice.diceFaces != null)
                {
                    diceFaces = dice.diceFaces;
                }
            }

            // 層デバフ適用
            if (ActiveModifier != null)
            {
                if (ActiveModifier.diceMaxBonus != 0)
                    diceMax = Mathf.Max(1, diceMax + ActiveModifier.diceMaxBonus);
                if (ActiveModifier.critRateBonus != 0)
                    critRate = Mathf.Clamp(critRate + ActiveModifier.critRateBonus, 0, 9);
            }

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
                default:                   return type.ToString();
            }
        }

        // ============================================================
        //  デバッグ用キーバインド
        // ============================================================

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.G) && CurrentPhase == GamePhase.Title)
                StartNewRun();

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
                    case GamePhase.ShopVisit:
                    case GamePhase.EventEncounter:
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

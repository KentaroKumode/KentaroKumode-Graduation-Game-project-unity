using System;
using System.Collections.Generic;
using UnityEngine;
using Codex;
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
            ExchangeTile,     // 交換マス（パッシブ1つ→上位Tierパッシブ）
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
        /// <summary>覚者連戦などのチェーン swap で次フォームへ切り替わったことを通知する。
        /// 戦闘自体は継続中だが、計測上は新エネミーとの戦闘扱いにしたい AutoRunner 等が拾う。</summary>
        public void RaiseEnemyEncountered(EnemyData e) => OnEnemyEncountered?.Invoke(e);
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

        // === 戦闘後ドロップの2択 ===
        // 各ドロップにつき、同カテゴリ・同レア度の2候補(a,b)。プレイヤー/Botが1つ選ぶ。
        private readonly System.Collections.Generic.List<(string a, string b)> pendingRewardChoices
            = new System.Collections.Generic.List<(string a, string b)>();
        private int rewardChoiceIndex;

        // === メタデバフ Lv5: 偽の商人戦の進行中フラグ ===
        private bool inFalseMerchantCombat;

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

            // ラン跨ぎで永続するスキル状態 (Nightfall の蓄積過剰ダメ等) をリセット
            InventorySystem.PassiveSkills.PassiveSkillRegistry.ResetAllRunState();

            // 初期ダイスは木のダイス（武器はダイス個数のみ定義し、面は装備ダイスが供給する）
            Run.equippedDiceId = "dice_wood";

            // 初期武器: BRONZE Tier1 から盾/斧/ダガー/剣をランダム配布
            // (disp_* 削除後の置き換え。 ラン開始時から最低限の装備を保証)
            string[] starterPool = { "sword_t1", "axe_t1", "dagger_t1", "shield_t1" };
            string starterWeapon = starterPool[UnityEngine.Random.Range(0, starterPool.Length)];
            if (Run.ownedPassiveItems == null)
                Run.ownedPassiveItems = new System.Collections.Generic.List<string>();
            InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, starterWeapon);
            Run.equippedWeaponId = starterWeapon;
            Log($"初期武器: {starterWeapon}");

            // メタ恒久バフを適用（HP/Gold/初期素材を底上げ）
            MetaProgression.MetaBuffApplicator.ApplyToRunStart(Run, startingHP);

            // メタバフ〈開幕パッシブ〉: ノーマル枠から1個獲得して所持
            if (MetaProgression.MetaBuffApplicator.IsStartingPassiveItemUnlocked())
            {
                string id = PickPassiveItemForBossExtra(false);
                if (!string.IsNullOrEmpty(id))
                {
                    if (Run.ownedPassiveItems == null)
                        Run.ownedPassiveItems = new System.Collections.Generic.List<string>();
                    InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
                    Log($"メタ報酬: 開幕パッシブ「{id}」を獲得");
                }
            }


            LastCombatResult = null;
            CurrentEnemy = null;
            IsEliteSecondFight = false;
            inFalseMerchantCombat = false;

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

            var hopeFromNode = mm.CurrentNode; // 希望(ADR-0002): 横移動判定用の移動前ノード
            // 飢餓→希望統合(ADR-0002): 旧・空腹HPダメージは廃止。移動の生存圧は希望システム
            // （横移動コスト＋絶望的な進軍）が担う。Hunger ゲージ更新は MoveTo 内で行われるが無害。
            mm.MoveTo(nodeId, Run.playerMaxHP);

            // 落石のような雹: ノード移動毎に HP-1
            int boulderDmg = MapSystem.AbyssPhenomena.AbyssPhenomenonCombatHooks.OnNodeMove(Run);
            if (boulderDmg > 0 && Run.playerHP > 0)
            {
                Run.playerHP = Mathf.Max(0, Run.playerHP - boulderDmg);
                Log($"異常現象「落石のような雹」 HP-{boulderDmg} (HP:{Run.playerHP})");
                if (Run.playerHP <= 0)
                {
                    Run.EndRun();
                    SetPhase(GamePhase.GameOver);
                    OnGameOver?.Invoke(Run);
                    return;
                }
            }

            // Λ層: 環状線マスを踏むたびに「次元の乱れ」を蓄積。3 毎にランダム恒久デバフを付与/段階上昇。
            // タイル起動(=その戦闘)より前に付与することで、踏んだマスの戦闘へ即座に反映される。
            if (Run.inLambda && mm.CurrentNode != null && mm.CurrentNode.type == TileType.LambdaRing)
            {
                Run.dimensionalDisturbance++;
                if (Run.dimensionalDisturbance % 3 == 0)
                {
                    string granted = GameLoop.Lambda.LambdaDebuffEffects.GrantRandom(Run);
                    if (granted != null)
                        Log($"次元の乱れ {Run.dimensionalDisturbance}: Λデバフ「{granted}」lv{Run.GetLambdaDebuffLevel(granted)} 付与");
                }
            }

            // 希望(ADR-0002): 横移動(同行=row不変)で減少。縦/斜め移動は進行のため無料。
            // 発狂(hope==0)中は秒読みを1消費し、尽きたらラン終了。絶望的な進軍(メタLv8)で全移動 -1。
            bool hopeLateral = hopeFromNode != null && mm.CurrentNode != null
                               && hopeFromNode.row == mm.CurrentNode.row && hopeFromNode != mm.CurrentNode;
            bool despairMarch = MetaProgression.MetaDebuffApplicator.IsDespairMarchActive();
            if (HopeSystem.ApplyMove(Run, hopeLateral, despairMarch))
            {
                Log("発狂: 秒読みが尽きてラン終了");
                Run.EndRun();
                SetPhase(GamePhase.GameOver);
                OnGameOver?.Invoke(Run);
                return;
            }

            ActivateTile(mm.CurrentNode);
        }

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
                        InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
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
            // metaWinGold は最大3、全戦闘に加算すると経済を歪める(1/5 デノミ済み環境で +90G/ラン に到達)。
            // ボス撃破時のみ適用し、通常戦闘では 0 とする。
            MetaProgression.MetaTokenEarner.OnEnemyDefeated();
            bool isBossNodeForMeta = node != null && node.type == TileType.Boss;
            int metaWinGold = isBossNodeForMeta
                ? MetaProgression.MetaBuffApplicator.GetCombatGoldBonus()
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
                }

                // 7層裏ボス 妙覚サドンデス勝利: 解脱エンディング
                if (result.gedatsu)
                {
                    Run.gedatsuVictory = true;
                    Log("★解脱★ ─ 覚者の試練を超え、真の悟達に至った。");
                }

                // 1層ボス（トレジャーゴブリン）: 良質な武器/パッシブを1個ドロップ
                if (Run.currentFloor == 1)
                {
                    string dropId = PickTreasureGoblinDrop();
                    if (!string.IsNullOrEmpty(dropId))
                    {
                        InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, dropId);
                        Loadout.TryAutoEquip(Run, dropId);
                        var dd = ItemDatabase.Instance?.GetItem(dropId);
                        Log($"トレジャーゴブリン討伐報酬: {(dd != null ? dd.displayName : dropId)} を獲得");
                    }
                }

                int rewardBase = FloorManager.CalculateRewardCoins(Run.currentFloor, true, result.totalTurns) * 2;
                // 傲慢: ボスはエリート以上扱いで報酬2倍（割合先）
                if (prideActive) rewardBase *= 2;
                // パッシブ刻印〈守銭〉: 発動分 × 1G/ボス
                int sigilGold = InventorySystem.Sigils.PassiveSigilActivator.CountGoldOnBossSigils(Run);
                int reward = LastStand.FilterGoldGain(Run, rewardBase + metaWinGold + sigilGold);
                Run.coins += reward;

                // メタ: ボス撃破時の追加パッシブ報酬
                var extra = MetaProgression.MetaBuffApplicator.GetBossExtraDrop();
                if (extra != MetaProgression.MetaBuffApplicator.BossExtraDrop.None)
                {
                    bool wantRare = extra == MetaProgression.MetaBuffApplicator.BossExtraDrop.Rare;
                    string id = PickPassiveItemForBossExtra(wantRare);
                    if (!string.IsNullOrEmpty(id))
                    {
                        InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
                        Log($"メタ報酬: ボス撃破ボーナス {(wantRare ? "レア" : "ノーマル")}パッシブ獲得: {id}");
                    }
                }

                SetPhase(GamePhase.Reward);
                OnRewardGranted?.Invoke(reward);
                return;
            }

            // エリートマスは1連戦（精鋭1体で完結）。2連戦は廃止。

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

            // 戦闘勝利報酬: 50%でパッシブ/消費を獲得。エリートマスは更に50%でもう1つ。
            // 戦闘を経済の主軸へ（戦闘以外のゴールド源ナーフと対の措置）。
            bool eliteWin = node != null && node.EffectiveType == TileType.EliteBattle;

            // 確信進化: エリート戦勝利時に〈根拠のない確信〉→〈決意〉→〈真理〉と進化
            if (eliteWin)
                ConvictionSystem.OnEliteDefeated(Run);
            int drops = 0;
            if (UnityEngine.Random.value < 0.5f) drops++;
            if (eliteWin && UnityEngine.Random.value < 0.5f) drops++;

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
                if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (dedup.Contains(it.internalName)) continue;   // ラン重複排除
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

            var dedup = BuildDedupExclude();
            var pool = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in all)
            {
                if (it == null) continue;
                if (it.category != ItemCategory.Weapon && it.category != ItemCategory.Passive) continue;
                if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (dedup.Contains(it.internalName)) continue;   // ラン重複排除
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

            // 未解決のドロップ2択が残っていれば確定を保留（UI/Botが先に選ぶ）。
            // 万一未選択のまま呼ばれたらソフトロック回避のため既定(option a)で消化。
            if (HasPendingRewardChoice)
            {
                while (HasPendingRewardChoice) ResolveRewardChoice(0);
                return;
            }

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

        /// <summary>休憩でHP回復を選択。 メタ〈ボス前休憩 回復+強化〉解放時は強化も同時に試みる。</summary>
        public void RestHeal()
        {
            if (CurrentPhase != GamePhase.RestStop) return;
            float ratio = ActiveModifier?.restHealMultiplier ?? 0.3f;
            int heal = Mathf.CeilToInt(Run.playerMaxHP * ratio);
            Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
            Log($"休憩回復: +{heal}HP ({(int)(ratio*100)}%) (現在: {Run.playerHP}/{Run.playerMaxHP})");
            // メタ Lv51: 同じ休憩マスで武器強化も同時に試みる（素材不足なら無視・回復は通る）
            if (MetaProgression.MetaBuffApplicator.IsBossRestHealAndUpgradeUnlocked())
                TryAutoUpgradeAtRest();
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>休憩で食事を選択（空腹を全回復）。HP回復・武器強化と並ぶ独立選択肢。</summary>
        public void RestEat()
        {
            if (CurrentPhase != GamePhase.RestStop) return;
            var h = MapManager.Instance?.Hunger;
            int before = h?.Current ?? 0;
            h?.RestoreFull();
            Log($"休憩食事: 空腹 {before} → {h?.Current ?? 0}/{h?.Max ?? 0}（全回復）");
            SetPhase(GamePhase.MapNavigation);
        }

        // ===== 武器Tier強化＆限界突破システム =====
        public const int MaxLimitBreak = 10;
        private static readonly System.Text.RegularExpressions.Regex TierIdRx
            = new System.Text.RegularExpressions.Regex(@"^(.+)_t(\d+)$");

        /// <summary>装備武器が家系のTier武器（{family}_t{N}）か。</summary>
        public static bool IsTierWeapon(string weaponId)
            => !string.IsNullOrEmpty(weaponId) && TierIdRx.IsMatch(weaponId);

        /// <summary>同家系の次に存在するTier武器ID。無ければ null（=最上位Tier）。</summary>
        public static string NextTierWeaponId(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return null;
            var m = TierIdRx.Match(weaponId);
            if (!m.Success) return null; // _tN 形式でない武器(竜閃等)は強化対象外
            string family = m.Groups[1].Value;
            int n = int.Parse(m.Groups[2].Value);
            var db = ItemDatabase.Instance;
            if (db == null) return null;
            for (int k = n + 1; k <= n + 5; k++)
            {
                string id = $"{family}_t{k}";
                if (db.GetItem(id) != null) return id;
            }
            return null;
        }

        /// <summary>現在の総合強化段階。進行武器: (tier-1)*2 + plus + 業物。非進行Tier武器: tier番号-1。</summary>
        private static int OverallStage(RunState run)
        {
            var m = TierIdRx.Match(run.equippedWeaponId ?? "");
            if (!m.Success) return 0;
            int n = int.Parse(m.Groups[2].Value);
            if (InventorySystem.PassiveSkills.WeaponProgression.IsProgressionWeapon(run.equippedWeaponId))
                return (n - 1) * 2 + (run.weaponPlus > 0 ? 1 : 0) + run.limitBreakStage;
            return n - 1;
        }

        /// <summary>T4 までの段階別コスト表 (stage 0..6)。</summary>
        private static readonly int[] WeaponUpgradeCostTable = { 1, 1, 2, 2, 3, 3, 4 };

        /// <summary>次の1段階強化に必要な素材数。 強化不可なら int.MaxValue。
        /// 2026-05-31: T4までは {1,1,2,2,3,3,4}、 業物 lv1〜 は 4,5,6,7,... (stage-3)。</summary>
        public static int WeaponUpgradeCost(RunState run)
        {
            if (run == null) return int.MaxValue;
            bool prog = InventorySystem.PassiveSkills.WeaponProgression.IsProgressionWeapon(run.equippedWeaponId);
            int stage = OverallStage(run);
            int cost = stage < WeaponUpgradeCostTable.Length
                       ? WeaponUpgradeCostTable[stage]
                       : stage - 3;
            if (prog)
            {
                bool atTopPlus = run.weaponPlus > 0 && NextTierWeaponId(run.equippedWeaponId) == null;
                if (atTopPlus && run.limitBreakStage >= MaxLimitBreak) return int.MaxValue;
                return cost;
            }
            if (NextTierWeaponId(run.equippedWeaponId) != null) return cost;
            return int.MaxValue;
        }

        /// <summary>武器強化で Tier が進んだ際に通知される (旧ID, 新ID)。
        /// L1 学習が中間 Tier を acquiredItemsEver に記録するためのフック。
        /// 同一 Tier 内の + 昇格は通知しない (Tier ID が変わらないため)。</summary>
        public static event System.Action<string, string> OnWeaponTierUpgraded;

        /// <summary>武器を1段階強化。進行武器: T_n→T_n+→次Tier→…→T4+→業物lv++。
        /// 非進行Tier武器: 従来のTier置換。free=true で素材消費なし（鍛冶の霊薬）。</summary>
        public static bool TryUpgradeWeapon(RunState run, bool free = false)
        {
            if (run == null) return false;
            int cost = WeaponUpgradeCost(run);
            if (cost == int.MaxValue) return false;
            if (!free && run.weaponMaterials < cost) return false;

            string prevWeaponId = run.equippedWeaponId;
            bool prog = InventorySystem.PassiveSkills.WeaponProgression.IsProgressionWeapon(run.equippedWeaponId);
            if (prog)
            {
                if (run.weaponPlus == 0)
                {
                    run.weaponPlus = 1;                 // T_n → T_n+
                }
                else
                {
                    string next = NextTierWeaponId(run.equippedWeaponId);
                    if (next != null) { run.equippedWeaponId = next; run.weaponPlus = 0; } // T_n+ → T_{n+1}
                    else run.limitBreakStage++;          // T4+ → 業物lv++
                }
            }
            else
            {
                string next = NextTierWeaponId(run.equippedWeaponId);
                if (next == null) return false;
                run.equippedWeaponId = next;
            }
            if (!free) run.weaponMaterials -= cost;
            if (prevWeaponId != run.equippedWeaponId)
                OnWeaponTierUpgraded?.Invoke(prevWeaponId, run.equippedWeaponId);
            // 天工開物: 武器を強化するたび、強化素材を1つ返還する
            if (run != null && run.OwnsPassive("天工開物"))
            {
                run.weaponMaterials += 1;
                Debug.Log("[天工開物] 強化素材を1つ返還");
            }
            return true;
        }

        public void RestUpgrade()
        {
            if (CurrentPhase != GamePhase.RestStop) return;

            // 2026-05-31 v3: 1 Rest = 1 強化のみ (前哨基地強化 (各層1回) と組み合わせて運用)
            int cost = WeaponUpgradeCost(Run);
            if (cost == int.MaxValue || Run.weaponMaterials < cost)
            {
                Log($"武器強化 不可 (必要{cost}/所持{Run.weaponMaterials}) → 回復に切替");
                RestHeal();
                return;
            }
            int before = Run.weaponMaterials;
            if (TryUpgradeWeapon(Run))
            {
                int used = before - Run.weaponMaterials;
                Log($"武器強化 ×1: {Run.equippedWeaponId}{(Run.weaponPlus > 0 ? "+" : "")} 業物Lv{Run.limitBreakStage} (素材-{used}, 残{Run.weaponMaterials})");
            }
            // メタ Lv51: 同じ休憩マスで HP 回復も同時に実行
            if (MetaProgression.MetaBuffApplicator.IsBossRestHealAndUpgradeUnlocked())
            {
                float ratio = ActiveModifier?.restHealMultiplier ?? 0.3f;
                int heal = Mathf.CeilToInt(Run.playerMaxHP * ratio);
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
                Log($"[メタLv51] 同時回復: +{heal}HP ({(int)(ratio*100)}%) (現在: {Run.playerHP}/{Run.playerMaxHP})");
            }
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>RestHeal 経由で呼ばれる、 素材があれば武器強化を1回試みるヘルパー（メタ Lv51 用）。</summary>
        private void TryAutoUpgradeAtRest()
        {
            int cost = WeaponUpgradeCost(Run);
            if (cost == int.MaxValue || Run.weaponMaterials < cost) return;
            int before = Run.weaponMaterials;
            if (TryUpgradeWeapon(Run))
            {
                int used = before - Run.weaponMaterials;
                Log($"[メタLv51] 同時強化 ×1: {Run.equippedWeaponId}{(Run.weaponPlus > 0 ? "+" : "")} 業物Lv{Run.limitBreakStage} (素材-{used}, 残{Run.weaponMaterials})");
            }
        }

        /// <summary>前哨基地強化 (2026-05-31 新規): 各層 (Outpost マス) で 1 回のみ実行可能な武器強化。
        /// 素材消費は通常の WeaponUpgradeCost に従う。 RestUpgrade とは独立してフラグ管理される。
        /// 戻り値: true=強化成功、 false=既に使用済 or 素材不足 or 強化不可。</summary>
        public bool OutpostUpgrade()
        {
            if (Run == null) return false;
            if (Run.outpostUpgradeUsedThisFloor) return false;
            int cost = WeaponUpgradeCost(Run);
            if (cost == int.MaxValue || Run.weaponMaterials < cost) return false;
            int before = Run.weaponMaterials;
            if (!TryUpgradeWeapon(Run)) return false;
            Run.outpostUpgradeUsedThisFloor = true;
            int used = before - Run.weaponMaterials;
            Log($"[前哨基地] 武器強化 ×1: {Run.equippedWeaponId}{(Run.weaponPlus > 0 ? "+" : "")} 業物Lv{Run.limitBreakStage} (素材-{used}, 残{Run.weaponMaterials})");
            return true;
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

        // ============================================================
        //  Λ層（時間の狭間）
        // ============================================================

        /// <summary>Λ層へ強制突入。currentFloor は 5 のまま、環状線マップを生成して周回させる。
        /// 移動毎に「次元の乱れ」を蓄積し、3 毎にランダム恒久デバフを付与する。</summary>
        private void EnterLambda()
        {
            Run.inLambda = true;
            Run.dimensionalDisturbance = 0;
            // 5Fボス撃破フラグを下ろす。これを残すと Λ内エリート報酬の ConfirmReward が
            // 再び HandleFloorClear→EnterLambda を呼び（IsNormalClear が currentFloor==5 で成立し続けるため）、
            // 毎報酬でΛへ再突入＝dimensionalDisturbance リセット＆マップ再生成のループになる。
            Run.bossDefeatedThisFloor = false;
            if (Run.lambdaDebuffs == null)
                Run.lambdaDebuffs = new System.Collections.Generic.Dictionary<string, int>();

            // Λ層に層デバフ(ActiveModifier)は適用しない
            ActiveModifier = null;

            MapManager.Instance.GenerateLambda();

            SetPhase(GamePhase.FloorIntro);
            // 層タイトル表示（Λ層。確定文言の正本: docs/specs/map-run.md）
            var lambdaIntro = LayerTitles.ForLambda();
            Log($"=== {lambdaIntro.title} ===　{lambdaIntro.subtitle}　中央マスを踏むまで周回する");
            LayerTitles.Raise(lambdaIntro);
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>Λ層の中央マス踏破 → 離脱。6F へ進む（通常の FloorClear→AdvanceFloor→EnterFloor 経路）。</summary>
        private void ExitLambda()
        {
            Run.inLambda = false;
            Log($"=== 時間の狭間を離脱 === 踏破マス {Run.dimensionalDisturbance} / Λデバフ {Run.lambdaDebuffs.Count}種 → 6層前哨基地へ");
            SetPhase(GamePhase.FloorClear);
            OnFloorAdvanced?.Invoke(Run.currentFloor + 1);
        }

        /// <summary>Λ層の環状線マス起動: エリート戦 or 固有イベント(回復/パッシブ)を抽選。</summary>
        private void ActivateLambdaRing(MapNode node)
        {
            // 50% エリート / 50% 固有イベント
            if (UnityEngine.Random.value < 0.5f)
            {
                // エリート戦: ノードを一時的に EliteBattle 扱いにして既存のエリート抽選/強化を流用
                node.resolvedType = TileType.EliteBattle;
                IsEliteSecondFight = false;
                firstEliteEnemy = null;
                Log("Λ環状線: エリート戦");
                StartBattleTile();
            }
            else
            {
                node.resolvedType = null; // 次回再訪時にまた抽選するため戻す
                ResolveLambdaEvent();
            }
        }

        /// <summary>Λ固有イベント: HPが半分未満なら回復、十分なら未所持寄りのパッシブ1つを獲得。
        /// (ロジック先行のためボット向け自動解決。UI実装時に2択提示へ差し替える)。</summary>
        private void ResolveLambdaEvent()
        {
            bool wantHeal = Run.playerHP * 2 < Run.playerMaxHP;
            if (wantHeal)
            {
                int heal = Mathf.CeilToInt(Run.playerMaxHP * 0.4f);
                int before = Run.playerHP;
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
                Log($"Λ固有イベント: 回復 +{Run.playerHP - before} ({Run.playerHP}/{Run.playerMaxHP})");
            }
            else
            {
                var (a, b) = PickTreasureChoicePair();
                string pick = !string.IsNullOrEmpty(a) ? a : b;
                if (!string.IsNullOrEmpty(pick))
                {
                    GrantRewardItem(pick, eliteWin: false);
                    Log("Λ固有イベント: パッシブアイテム獲得");
                }
                else
                {
                    // 付与候補が尽きた場合は回復にフォールバック
                    int heal = Mathf.CeilToInt(Run.playerMaxHP * 0.4f);
                    Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
                    Log("Λ固有イベント: 候補なし → 回復にフォールバック");
                }
            }
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>フロアに入る（マップ生成、前哨基地処理、デバフ適用）</summary>
        private void EnterFloor()
        {
            var mm = MapManager.Instance;

            // 契約システム: 前層終了 → 新層開始 hook (この呼び順は重要)
            // 前層終了で 商業連合隊の層末収入支払い、 サーカスフラグクリア等が行われる
            GameLoop.Contracts.ContractManager.Instance.FireOnLayerEnd(Run);
            GameLoop.Contracts.ContractManager.Instance.FireOnLayerStart(Run);

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

            // 大穴の異常現象を抽選 (希望連動・1〜2件)
            MapSystem.AbyssPhenomena.AbyssPhenomenonRoller.RollForFloor(Run);
            foreach (var phen in Run.activePhenomena)
            {
                var def = MapSystem.AbyssPhenomena.AbyssPhenomenonDatabase.Get(phen);
                if (def != null) Log($"異常現象「{def.displayName}」 — {def.description}");
            }

            // マップ生成（空腹度上限はModifierで調整）
            mm.GenerateFloor(Run.currentFloor);

            // 層タイトル表示（確定文言の正本: docs/specs/map-run.md。ビジュアルは未実装、OnShow を UI が購読）
            var layerIntro = LayerTitles.ForFloor(Run.currentFloor);
            Log($"=== {layerIntro.title} ===　{layerIntro.subtitle}");
            LayerTitles.Raise(layerIntro);

            // 空腹度ボーナス適用
            if (ActiveModifier != null && ActiveModifier.hungerMaxBonus != 0)
            {
                int newMax = mm.Hunger.Max + ActiveModifier.hungerMaxBonus;
                mm.Hunger.Initialize(Mathf.Max(1, newMax));
            }

            // 2026-05-31: メタデバフ Lv8 はハンガー初期値減算→飢餓ダメ×2 に変更 (前哨基地全回復で旧効果失効のため)。
            //             → 倍率適用は MoveTo 後の starvDmg 計算で行うので、 ここでの初期値操作は廃止。

            // 飢餓ダメージ倍率上書き
            if (ActiveModifier != null && ActiveModifier.starvationDamageOverride >= 0)
                mm.Hunger.starvationDamageRatio = ActiveModifier.starvationDamageOverride;

            mm.ProcessOutpost();

            // 前哨基地は開始ノードのため OnTileActivated イベントが ActivateTile 経由で発火しない。
            // 旅団契約 AI (AutoRunner) 等のリスナーへ明示的に通知する。
            OnTileActivated?.Invoke(TileType.Outpost);

            // 2026-05-31 v3: 前哨基地で武器強化を1回まで自動実行 (素材があり、 強化可能なら)。
            // HPゲート無し (前哨基地は安全)。 BOT/プレイヤー共通の自動挙動 (UI 開発まで暫定)。
            // 各層 1 回制限は OutpostUpgrade 側で outpostUpgradeUsedThisFloor フラグ管理。
            OutpostUpgrade();

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
            else if (Run.currentFloor == 7)
            {
                // 7層前哨基地: 覚者戦に挑む者への餞別。HP全回復（healCap は尊重）
                int upper = healCap < 0 ? Run.playerMaxHP : Mathf.Min(healCap, Run.playerMaxHP);
                Run.playerHP = upper;
                Log($"前哨基地(7F): HP全回復 → {Run.playerHP}/{Run.playerMaxHP}");
            }
            else
            {
                // 2026-06-21: 通常前哨基地は「不足HPの30%」 回復 (ceil)。 全回復は廃止 (道中で減った HP がボスへ持ち越される)。
                // 6F/7F の特殊前哨基地 (全回復+MaxHP+5 / 全回復) は別経路で処理されるためここは通常層のみ。
                int upper = healCap < 0 ? Run.playerMaxHP : Mathf.Min(healCap, Run.playerMaxHP);
                int missing = Mathf.Max(0, upper - Run.playerHP);
                int healAmount = Mathf.CeilToInt(missing * 0.30f);
                int beforeHP = Run.playerHP;
                Run.playerHP = Mathf.Min(upper, Run.playerHP + healAmount);
                Log($"前哨基地: 不足HPの30%回復 +{Run.playerHP - beforeHP} → {Run.playerHP}/{Run.playerMaxHP}");
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

            // 希望(ADR-0002): 戦闘中 Run.playerHP は不変なので、ここでの値が戦闘開始時HP。
            // 終了時HPと比較してHP収支(マイナス=希望減少)を判定する（ApplyBattleResult 後に適用）。
            if (Run != null) Run.combatStartHP = Run.playerHP;

            // 2026-06-04: 素材経済の潤沢化（武器フル強化＋昇華5-10レンジを狙う）。
            // 旧 25%×1 → 戦闘勝利で確定+1、エリート勝利はさらに+1。
            if (result.playerWon && Run != null)
            {
                Run.weaponMaterials += 1;
                bool elite = MapSystem.MapManager.Instance?.CurrentNode != null
                             && MapSystem.MapManager.Instance.CurrentNode.EffectiveType == MapSystem.TileType.EliteBattle;
                if (elite) Run.weaponMaterials += 1;
                Log($"[戦闘報酬] 強化素材+{(elite ? 2 : 1)}");
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
                            if (data.category == ItemCategory.Consumable)
                                Run.ownedConsumables.Add(id);
                            else
                                InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
                            Loadout.TryAutoEquip(Run, id);
                            loot++;
                        }
                        Run.robberyPendingItems.Clear();
                    }
                    int gold = UnityEngine.Random.Range(30, 61);
                    Run.coins += gold;
                    Run.playerHP = Mathf.Max(1, result.playerHPRemaining);
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
                    Log("値下げ交渉敗北: HP1 で外に放り出された (ラン継続)");
                    OnBattleEnded?.Invoke(result);
                    SetPhase(GamePhase.MapNavigation);
                    return;
                }
            }

            Run.ApplyBattleResult(result.playerWon, result.playerHPRemaining, result.totalTurns);

            // 希望(ADR-0002): HP収支マイナス→床定量を減少／非マイナス→わずか回復。
            // 飢餓→希望統合の再マップ: 暴食(トゥルハドの暴食)で損×2、HopeLossReduce メタバフで損を軽減。
            float hopeLossMult = MetaProgression.PermanentDebuffEffects.HasGluttony(Run) ? 2f : 1f;
            int hopeLossReduce = MetaProgression.MetaBuffApplicator.GetHopeLossReduction();
            HopeSystem.ApplyCombatHpBalance(Run, hopeLossMult, hopeLossReduce);

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

            // 収束ノード(Boss/Outpost)以外の一度きりタイルは、再訪時に再発火させない。
            // （イベントの無限ファーム＝同行マス往復によるデッドロックを構造的に防ぐ）
            // Λ環状線/中央は周回前提のため oneShot から除外（毎回再発火＝再抽選）。
            bool oneShot = effectiveType != TileType.Boss && effectiveType != TileType.Outpost
                        && effectiveType != TileType.LambdaRing && effectiveType != TileType.LambdaExit;
            if (oneShot && node.activated)
            {
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            if (oneShot) node.activated = true;

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
                    if (Run != null && Run.shopsBlocked)
                    {
                        // 値下げ交渉後は商人ギルドから締め出されている。 ショップマスは素通り
                        Log("ショップ閉鎖中: 値下げ交渉により商人ギルドから出禁");
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
                case TileType.Trap:
                    HandleTrapTile();
                    break;
                case TileType.SinAltar:
                    BeginSinRitual();
                    break;
                case TileType.LambdaRing:
                    ActivateLambdaRing(node);
                    break;
                case TileType.LambdaExit:
                    ExitLambda();
                    break;
                default:
                    SetPhase(GamePhase.MapNavigation);
                    break;
            }
        }

        /// <summary>罠マス到達時の処理。 カルマ系統廃止に伴い、 旧 karma_trap も含めて全て無効。</summary>
        private void HandleTrapTile()
        {
            // カルマ系統撤去済み。 旧 karma_trap タイルが残っていても何も起きない (放置)。

            SetPhase(GamePhase.TrapTriggered);
        }

        /// <summary>エリート化対象外の基敵ID（弱すぎてエリートに値しない）。</summary>
        private static readonly System.Collections.Generic.HashSet<string> EliteExcludedBaseIds =
            new System.Collections.Generic.HashSet<string> { "slime", "goblin" };

        /// <summary>4層以降のエリートマス戦の敵を「精鋭」化する。
        /// DB共有インスタンスは汚さずクローンに対して HP×2 / threat+2 /
        /// 精鋭パッシブ(3Tごとダイス+1) を付与。表示名に「精鋭」を冠し、
        /// 計測/デバッグ上は通常敵と別枠として集計される。
        /// 1～3層・通常戦は対象外（raw をそのまま返す）。</summary>
        private EnemyData MaybeMakeElite(EnemyData raw)
        {
            if (raw == null) return raw;
            var node = MapManager.Instance?.CurrentNode;
            if (node == null || node.EffectiveType != TileType.EliteBattle) return raw;
            if (Run.currentFloor < 4) return raw;
            // スライム/ゴブリン/ハーピィはエリート化対象外（通過しても通常敵のまま）
            if (EliteExcludedBaseIds.Contains(raw.id)) return raw;

            var e = raw.Clone();
            e.maxHP = Mathf.Max(1, e.maxHP * 2);
            e.threat += 2;
            e.displayName = "精鋭" + e.displayName;
            if (e.passiveSkills == null)
                e.passiveSkills = new System.Collections.Generic.List<EnemyPassiveEntry>();

            // 基敵ごとの固有エリートパッシブ（逆スケール: 弱敵ほど強力）。
            // 未マップ敵は汎用 EliteVigor をフォールバック付与。
            var (eliteId, eliteName, eliteDesc) = EliteTraitFor(raw.id);
            e.passiveSkills.Add(new EnemyPassiveEntry
            {
                internalName = eliteId, skillName = eliteName, description = eliteDesc,
            });
            return e;
        }

        /// <summary>基敵IDに対応するエリート固有パッシブ定義を返す。</summary>
        private (string id, string name, string desc) EliteTraitFor(string baseId)
        {
            switch (baseId)
            {
                case "kobold":     return ("EliteKobold",     "早業",           "ロールの度に5GOLD奪取、50%でダメージを回避");
                case "skeleton":   return ("EliteSkeleton",   "不死の軍勢",     "致命ダメージで2回までHP全回復で踏みとどまる。発動毎に自ダイス+3（永続・最大+6）");
                case "wolf":       return ("EliteWolf",        "血盟の疾走",     "自ダイス合計+2、勝利時さらに与ダメ+2");
                case "harpy":      return ("EliteHarpy",       "死翔",           "プレイヤー消費アイテム使用不可。毎ロール自ダイス+3＋経過ターン（毎T+1無限累積）");
                case "13th_death": return ("EliteDecree13",    "死の重圧",       "13番目の宣告が灯ったターン中、自ダイス合計+13");
                case "orc":        return ("EliteOrc",         "痛恨の一撃",     "ロール勝利時+8ダメージ、敗北で自ダイス数+1（勝利でリセット・最大5個）");
                case "lizardman":  return ("EliteLizard",      "重甲",           "被弾時さらに-2軽減（硬鱗と累積）、敗北時に固定1反射");
                case "wraith":     return ("EliteWraith",      "霊体",           "2ターンに1度 被ダメ全て1に減少、非霊体時はダイス数+1");
                case "golem":      return ("EliteGolem",       "巌の意志",       "毎ターン意志+1、撃破時にスタック分の確定ダメージ");
                case "minotaur":   return ("EliteMinotaur",    "際限なき暴走",   "毎ロール自ダイス合計+1（既に強い→控えめ）");
                case "dark_knight":return ("EliteDarkKnight",  "闇技",           "勝利時、与ダメ+2");
                default:           return ("EliteVigor",       "精鋭",           "HP2倍 / threat+2 / 3ターンごとにダイス出目合計+1");
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

            // エリートマス(4層以降)で除外敵を引いたら、エリートに値する敵へ引き直す
            var eliteNode = MapManager.Instance?.CurrentNode;
            if (eliteNode != null && eliteNode.EffectiveType == TileType.EliteBattle
                && Run.currentFloor >= 4 && EliteExcludedBaseIds.Contains(CurrentEnemy.id))
            {
                for (int tries = 0; tries < 12 && EliteExcludedBaseIds.Contains(CurrentEnemy.id); tries++)
                    CurrentEnemy = FloorManager.PickEnemy(Run.currentFloor) ?? CurrentEnemy;
            }

            CurrentEnemy = MaybeMakeElite(CurrentEnemy);

            if (firstEliteEnemy == null && MapManager.Instance.CurrentNode.EffectiveType == TileType.EliteBattle)
                firstEliteEnemy = CurrentEnemy;

            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"エンカウント: {CurrentEnemy.displayName}");

            var (dc, dm, cr, df) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df);
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
            return set;
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

            InventorySystem.Helpers.PassiveAddHelper.RemoveAt(Run, idx);
            InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, resultId);
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
                if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (!tierOk(it.rarity)) continue;
                if (excl.Contains(it.internalName)) continue;
                pool.Add(it);
            }
            if (pool.Count == 0) // 枯渇 → 重複許可で再構築
            {
                foreach (var it in all)
                {
                    if (it == null || it.category != ItemCategory.Passive) continue;
                    if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                    if (!tierOk(it.rarity)) continue;
                    pool.Add(it);
                }
            }
            if (pool.Count == 0) return null;
            return InventorySystem.RarityWeightedPicker.Pick(pool)?.internalName;
        }

        /// <summary>所持パッシブからランダムに1つ除去（刻印も同期）。除去した index を返す。無ければ -1。</summary>
        private int LoseRandomPassiveItem()
        {
            var owned = Run?.ownedPassiveItems;
            if (owned == null || owned.Count == 0) return -1;
            int idx = UnityEngine.Random.Range(0, owned.Count);
            string lostId = owned[idx];
            InventorySystem.Helpers.PassiveAddHelper.RemoveAt(Run, idx);
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

            var (dc, dm, cr, df) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(enemy, Run.playerHP, dc, dm, cr, df);
            CombatManager.Instance.SetFleeAfterTurns(3);
            CombatManager.Instance.AddEnemyDiceTotalBonus(diceTotalBonus);
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

            CurrentEnemy = MaybeMakeElite(CurrentEnemy);

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

            // 5F裏ボス置換: シュヴァリエのレイピア所持 → シュヴァリエ・サン=ジョリオラ
            if (Run.currentFloor == 5
                && Run.ownedPassiveItems != null
                && Run.ownedPassiveItems.Contains("chevalier_rapier"))
            {
                bossId = "boss_layer5_hidden";
                Log("挑戦資格を満たす者の前にのみ現れる剣聖の影が形を成す ―");
            }

            CurrentEnemy = CombatSystem.EnemyDatabase.Get(bossId);
            if (CurrentEnemy == null)
            {
                Debug.LogWarning($"[GameManager] ボス '{bossId}' 未定義 — フロア{Run.currentFloor}のプールから抽選");
                CurrentEnemy = FloorManager.PickEnemy(Run.currentFloor);
            }
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"ボス戦: {CurrentEnemy.displayName}");

            // 5層ボス戦の清算系デバフ (カルマ系統廃止済み)
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

        /// <summary>[検証用] 現在の Run ビルドのまま、指定フロアのボスと1戦だけ即決着し結果を返す。
        /// AutoRunner の「5Fボス勝率スイープ」専用。通常のフロア遷移・報酬・OnBattleEnded は介さず、
        /// CombatManager を直接駆動する（GameManager の集計イベントを発火しない）。</summary>
        public CombatResult SimulateBossFight(int floor)
        {
            var enemy = CombatSystem.EnemyDatabase.Get($"boss_layer{floor}");
            if (enemy == null || Run == null) return default;
            CurrentEnemy = enemy;
            var (dc, dm, cr, df) = GatherPlayerCombatStats();
            CombatManager.Instance.StartCombat(enemy, Run.playerHP, dc, dm, cr, df);
            return CombatManager.Instance.ExecuteFullCombat();
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

        /// <summary>「血の儀」: 最大HPの 30% を支払う。HP不足 or ラストスタンド中は
        /// 捧げられず HeartOfGolgotha 付与。</summary>
        public void OfferHpSacrifice(bool accept)
        {
            if (CurrentPhase != GamePhase.SinRitual) return;

            int demand = Mathf.Max(1, Mathf.CeilToInt(Run.playerMaxHP * 0.3f));
            bool canPay = accept && !Run.lastStandActive && Run.playerHP > demand;
            if (canPay)
            {
                Run.playerHP -= demand;
                Log($"血の儀: HP {demand}（最大の30%）を捧げた (残 {Run.playerHP})");
            }
            else
            {
                Run.AddDebuff(SinDebuff.HeartOfGolgotha);
                Log(Run.lastStandActive
                    ? "血の儀: ラストスタンド中は捧げられない。〈ゴルゴダの心〉が刻まれる。"
                    : "血の儀: HP不足／拒否。〈ゴルゴダの心〉が刻まれる。");
            }
        }

        /// <summary>「貪欲の儀」: 20G(旧100G・1/5デノミ) を支払う。所持不足 or ラストスタンド中は
        /// 捧げられず SeveredTime 付与。</summary>
        public void OfferGoldSacrifice(bool accept)
        {
            if (CurrentPhase != GamePhase.SinRitual) return;

            const int demand = 20; // 旧100G、1/5デノミ
            bool canPay = accept && !Run.lastStandActive && Run.coins >= demand;
            if (canPay)
            {
                Run.coins -= demand;
                Run.coinsSpent += demand;
                Log($"貪欲の儀: コイン {demand} を捧げた (残 {Run.coins})");
            }
            else
            {
                Run.AddDebuff(SinDebuff.SeveredTime);
                Log(Run.lastStandActive
                    ? "貪欲の儀: ラストスタンド中は捧げられない。〈断絶した時間〉が刻まれる。"
                    : "貪欲の儀: 100G不足／拒否。〈断絶した時間〉が刻まれる。");
            }
        }

        /// <summary>「遺品の儀」: グリッド所持の Passive アイテムを 2 個消費する。
        /// 要件: チェーン/初期装備を除く2個以上 ＋ 消費2個の basePrice 合計が 30G 以上。
        /// 達成不可 or ラストスタンド中は捧げられず AshenBrand 付与。
        /// 消費は basePrice 昇順で「合計 ≥30 を満たす最小ペア」を選ぶ（プレイヤー有利）。
        /// 取得済み(seenPassiveItemIds)からは抹消しない＝重複禁止は維持。</summary>
        /// <param name="accept">捧げる選択をしたか</param>
        public void OfferItemSacrifice(bool accept)
        {
            if (CurrentPhase != GamePhase.SinRitual) return;

            if (!accept || Run.lastStandActive)
            {
                Run.AddDebuff(SinDebuff.AshenBrand);
                Log(Run.lastStandActive
                    ? "遺品の儀: ラストスタンド中は捧げられない。〈灰燼の烙印〉が刻まれる。"
                    : "遺品の儀を拒んだ。〈灰燼の烙印〉が刻まれる。");
                return;
            }

            // 候補抽出: ownedPassiveItems から「チェーン保護」を除外し (index, id, basePrice) を集める
            var candidates = new List<(int index, string id, int price)>();
            var owned = Run.ownedPassiveItems;
            var db = ItemDatabase.Instance;
            if (owned != null && db != null)
            {
                for (int i = 0; i < owned.Count; i++)
                {
                    string id = owned[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (AutoTest.ItemLearningStats.ExcludedFromLift.Contains(id)) continue;
                    var def = db.GetItem(id);
                    if (def == null) continue;
                    candidates.Add((i, id, Mathf.Max(0, def.basePrice)));
                }
            }

            // 合計 ≥30 を満たす最小コストのペアを探す（basePrice 昇順 → 全ペア列挙）
            candidates.Sort((a, b) => a.price.CompareTo(b.price));
            int bestI = -1, bestJ = -1, bestSum = int.MaxValue;
            for (int a = 0; a < candidates.Count - 1; a++)
            {
                for (int b = a + 1; b < candidates.Count; b++)
                {
                    int sum = candidates[a].price + candidates[b].price;
                    if (sum < 30) continue;
                    if (sum < bestSum)
                    {
                        bestSum = sum;
                        bestI = a;
                        bestJ = b;
                    }
                }
            }

            if (bestI < 0)
            {
                Run.AddDebuff(SinDebuff.AshenBrand);
                Log(candidates.Count < 2
                    ? "遺品の儀: 捧げられる遺物が足りない。〈灰燼の烙印〉が刻まれる。"
                    : "遺品の儀: 価値が足りない (合計30G以上必要)。〈灰燼の烙印〉が刻まれる。");
                return;
            }

            // 高 index から除去（並列配列のインデックス崩れ回避）
            int idxA = candidates[bestI].index;
            int idxB = candidates[bestJ].index;
            string nameA = candidates[bestI].id;
            string nameB = candidates[bestJ].id;
            int hi = Mathf.Max(idxA, idxB);
            int lo = Mathf.Min(idxA, idxB);
            InventorySystem.Helpers.PassiveAddHelper.RemoveAt(Run, hi);
            InventorySystem.Helpers.PassiveAddHelper.RemoveAt(Run, lo);
            Log($"遺品の儀: 〈{nameA}〉と〈{nameB}〉を灰にした (basePrice 計{bestSum}G)");
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

        public void ShopReroll()
        {
            if (CurrentPhase != GamePhase.ShopVisit) return;
            ShopManager.Instance.TryReroll(Run);
        }

        /// <summary>
        /// 値下げ交渉 (=強盗) を実行。
        /// 1. ShopManager.TryRobbery: 在庫スナップ・希望コスト・shopsBlocked, ショップを閉じる
        /// 2. 「怪しい商人」(shady_merchant) との特殊エリート戦に突入
        /// 3. HandleCombatEnd 内で勝利時=報酬付与/敗北時=HP1脱出 を分岐
        /// </summary>
        public void ShopRobbery()
        {
            if (CurrentPhase != GamePhase.ShopVisit) return;
            if (!ShopManager.Instance.TryRobbery(Run)) return;

            // 戦闘準備: 怪しい商人を取得
            var data = CombatSystem.EnemyDatabase.Get("shady_merchant");
            if (data == null)
            {
                Debug.LogError("[GameManager] shady_merchant が enemies.json に見つかりません");
                Run.shopRobberyInProgress = false;
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            CurrentEnemy = data;
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            var (dc, dm, cr, df) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df);
            Log("値下げ交渉 → 怪しい商人が刃を抜いた");
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
            // 宝箱はゴールド0(価値はアイテムのみ)が基準。1/5デノミ後、僅少な変動が無意味になるため撤廃済み。
            // メタバフ〈宝箱の財宝〉解放時のみ、フロア依存の少額ゴールドが復活する。
            int gold = 0;
            if (MetaProgression.MetaBuffApplicator.IsTreasureChestGoldUnlocked())
            {
                gold = Mathf.Max(1, 1 + floor / 2); // F1=1, F3=2, F5=3, F7=4
                gold = LastStand.FilterGoldGain(Run, gold);
            }
            Run.coins += gold;
            // 2026-06-04: 宝箱でも強化素材+1（素材経済の潤沢化）
            Run.weaponMaterials += 1;

            string itemId = PickRandomTreasureItem();
            string itemLabel = "（なし）";
            if (!string.IsNullOrEmpty(itemId))
            {
                InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, itemId);
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
                if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
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
                picked = InventorySystem.RarityWeightedPicker.Pick(pool, minR)
                         ?? InventorySystem.RarityWeightedPicker.Pick(pool);
            }
            else
            {
                picked = InventorySystem.RarityWeightedPicker.Pick(pool);
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
                if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (dedup.Contains(it.internalName)) continue;   // ラン重複排除
                pool.Add(it);
            }
            if (pool.Count == 0) return (null, null);

            CompleteItemData first;
            if (Run != null && Run.nextLootMinRarity >= 0)
            {
                var minR = (ItemRarity)Run.nextLootMinRarity;
                Run.nextLootMinRarity = -1;
                first = InventorySystem.RarityWeightedPicker.Pick(pool, minR)
                        ?? InventorySystem.RarityWeightedPicker.Pick(pool);
            }
            else
            {
                first = InventorySystem.RarityWeightedPicker.Pick(pool);
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
                ? partners[UnityEngine.Random.Range(0, partners.Count)].internalName
                : null;
            return (first.internalName, b);
        }

        /// <summary>報酬アイテムを実際に付与（所持追加＋刻印ロール＋自動装備）。</summary>
        private void GrantRewardItem(string itemId, bool eliteWin)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            // 2026-06-22: Λ 滞在中の取得は保護対象 + gross カウンタ計上
            bool fromLambda = Run != null && Run.inLambda;
            InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, itemId);
            Loadout.TryAutoEquip(Run, itemId);
            if (fromLambda)
            {
                Run.lambdaProtectedItemIds.Add(itemId);
                Run.lambdaItemsAcquiredGross++;
            }
            var dd = ItemDatabase.Instance?.GetItem(itemId);
            Log($"戦闘勝利報酬: {(dd != null ? dd.displayName : itemId)} を獲得{(eliteWin ? "（精鋭）" : "")}{(fromLambda ? " [Λ保護]" : "")}");
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
            // メタバフ〈フロアクリア回復〉: 階突破時の HP 回復（次層への持ち越し前に適用）
            int fch = MetaProgression.MetaBuffApplicator.GetFloorClearHeal();
            if (fch > 0 && Run != null && Run.playerHP > 0 && Run.playerHP < Run.playerMaxHP)
            {
                int before = Run.playerHP;
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + fch);
                Log($"メタ恩恵: フロアクリア回復 +{Run.playerHP - before} ({Run.playerHP}/{Run.playerMaxHP})");
            }

            // 5層クリア時: 〈決意〉以上未所持なら 5層クリアエンディング(NormalClear) で終了
            if (Run.IsNormalClear && Run.currentFloor == Run.normalClearFloor)
            {
                if (!ConvictionSystem.HasResolveOrBetter(Run))
                {
                    Run.EndRun();
                    SetPhase(GamePhase.RunClear);
                    OnRunCleared?.Invoke(Run);
                    Log("=== 5層クリア === 〈決意〉が無いためここで運命に背を向けた");
                    var ending5f = Endings.Resolve(Run);
                    Log(ending5f.Display);
                    VignetteUnlockState.OnRunClear(Run, ending5f.id, VignetteUnlockState.IsMaxDifficultyRun(Run));
                    return;
                }
                // 〈決意〉以上所持 → 6層へ進む前に Λ層（時間の狭間）へ強制突入。
                // 中央マス踏破で 6F 前哨基地に着地する（= 通常の FloorClear→6F 遷移）。
                EnterLambda();
                return;
            }
            else if (Run.IsFullClear)
            {
                Run.EndRun();
                SetPhase(GamePhase.RunClear);
                OnRunCleared?.Invoke(Run);
                Log($"=== 完全クリア！=== 裏ボス撃破");
                var endingFull = Endings.Resolve(Run);
                Log(endingFull.Display);
                VignetteUnlockState.OnRunClear(Run, endingFull.id, VignetteUnlockState.IsMaxDifficultyRun(Run));
            }
            else
            {
                // 6層 → 7層: 〈真理〉未所持なら 6層クリアエンディングで終了
                if (Run.currentFloor == 6 && !ConvictionSystem.HasTruth(Run))
                {
                    Run.EndRun();
                    SetPhase(GamePhase.RunClear);
                    OnRunCleared?.Invoke(Run);
                    Log("=== 6層クリア === 〈真理〉が無いためここで覚者の門は閉ざされた");
                    var ending6f = Endings.Resolve(Run);
                    Log(ending6f.Display);
                    VignetteUnlockState.OnRunClear(Run, ending6f.id, VignetteUnlockState.IsMaxDifficultyRun(Run));
                    return;
                }
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

            // 武器はダイスの「個数」と会心のみ定義する（面/最大値は装備ダイスが供給）。
            if (equipHandler != null)
            {
                var weapon = equipHandler.GetCurrentEquipment(ItemCategory.Weapon);
                if (weapon != null && weapon.hasWeaponStats)
                {
                    diceCount = weapon.weaponDice.count;
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
                    critRate = w.criticalRate;
                }
            }
            if (!diceResolved && Run != null && !string.IsNullOrEmpty(Run.equippedDiceId))
            {
                var d = ItemDatabase.Instance?.GetItem(Run.equippedDiceId);
                if (d != null && d.diceFaces != null)
                    diceFaces = d.diceFaces;
            }

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
                case TileType.Exchange:    return "交換";
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

            // 報酬フェーズで2択が残っている間: [1]/[2] で選択（Spaceでの確定は選び切ってから）
            if (CurrentPhase == GamePhase.Reward && HasPendingRewardChoice)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) { ResolveRewardChoice(0); return; }
                if (Input.GetKeyDown(KeyCode.Alpha2)) { ResolveRewardChoice(1); return; }
                return;
            }

            // 交換マス: [1] 交換する / [Space] 交換せず通過
            if (CurrentPhase == GamePhase.ExchangeTile)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) && CanExchangeTile) { DoExchangeTile(); return; }
                if (Input.GetKeyDown(KeyCode.Space)) { SkipExchangeTile(); return; }
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

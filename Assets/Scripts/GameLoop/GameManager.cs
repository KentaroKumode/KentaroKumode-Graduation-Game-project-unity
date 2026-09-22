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
        private static GameManager _instance;
        private static bool _shuttingDown;

        /// <summary>ゲーム進行の単一窓口。 アプリ終了中は null を返す。
        ///
        /// **getter に FindObjectOfType の復帰口がある理由 (2026-08-05):**
        /// 再生中にスクリプトが再コンパイルされるとドメインリロードで static だけが初期化され、
        /// シーン上の GameObject は生き残る。 すると <see cref="Awake"/> は二度と走らないので
        /// Instance だけが永久に null のままになり、 AutoRunner のラン走行が進行不能に陥って
        /// バッチが無限に空回りした (同日 2 度発生・いずれも 1 ランも完了せず)。
        /// CLAUDE.md のシングルトン規約 (_shuttingDown + RuntimeInitializeOnLoadMethod) に従う。</summary>
        public static GameManager Instance
        {
            get
            {
                if (_shuttingDown) return null;
                if (_instance == null) _instance = FindObjectOfType<GameManager>();
                return _instance;
            }
            private set { _instance = value; }
        }

        /// <summary>ドメインリロード無効設定でも static を必ず初期状態へ戻す。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _shuttingDown = false;
        }

        /// <summary>2026-06-28: 次ラン開始時に適用する職業。 UI / BOT が StartNewRun 前に書き込む。 デフォルト = 剣士。</summary>
        public static ClassType SelectedClass = ClassType.Swordsman;

        /// <summary>ラン開始前に選ぶ特殊端子 (§6-5)。 空 = 未選択 (第4端子なし)。
        ///
        /// **ショップ販売ではなく開始前の宣言にした** (2026-08-10)。 ショップ品にすると
        /// 「端子を増やすか武器を買うか」というゴールドの取り合いになり、 選択肢を増やす仕組みが
        /// 逆に選択肢を減らす ── 実測でも 7層クリアが 25.8% → 20.8% と**下がった**。
        /// 職業・種火と同じ「ランの宣言」レイヤーへ置く。</summary>
        public static string SelectedSpecialTerminal = "";

        /// <summary>true の間、 5層裏ボス(シュヴァリエ)への置換を行わない。 **計測専用の一時遮断** で、
        /// 製品挙動を変えるものではない (既定 false)。 裏ボスはレイピア所持という
        /// BOT のアイテム運に依存して 5層の難度を二分するため、 バランス基準値の測定では
        /// 分散源=ノイズになる。 AutoRunner の基準値測定だけが true にする (2026-08-05)。</summary>
        public static bool SuppressLayer5HiddenBoss = false;

        /// <summary>**計測専用**: 5層撃破時に〈ブレイドダンス〉を強制付与する (既定 false・製品挙動は不変)。
        /// 「剣の舞 4 枚が完成すれば高難易度の 7 層を通せるのか」を切り分けるための実験用フラグ。
        /// 通常は 4 枚集約でしか入手できず、高難易度ほど揃う前に死ぬため自然発生を待って測れない。</summary>
        public static bool GrantBladeDanceOnFloor5Clear = false;

        /// <summary>Λ層: 何マスごとに恒久デバフを 1 つ付与するか。
        ///
        /// 2026-08-08 に 3 → 5 へ延ばしたが、 **同日 3 へ戻した**。 延ばした根拠は
        /// 「利得だけが逓減しコストは一定」という観測だったが、 その逓減自体が
        /// <see cref="ActivateTile"/> の oneShot 判定バグ (Λ環状線ノードが一度エリートを引くと
        /// 永久に沈黙する) による偽の現象だった。 修正後は利得が踏破に完全比例するため、
        /// コスト側を寝かせる理由が消えた。
        /// **中央離脱のスポーク間隔 (3マス) とは別物** ── 混同すると離脱可能地点がずれる。
        ///
        /// <para><b>2026-09-08: 3 → 2 へ短縮。</b> 訓練済み BOT (Optimal) の 7層クリアが
        /// <b>66.9%</b> まで上がり、 <b>band 11 に 66.9% が張り付いて</b>いた
        /// (設計目標は人間で「2割強」)。 Λ は 1 ラン の所持 67.9 品のうち <b>19.9 品 (29%)</b> と
        /// 105G を配っており、 難易度と「アイテム選択がゲームを決めない」問題の両方の源になっている。
        /// 撤退条件 (lv2 が 4 つ) は BOT 方策なので触らず、 <b>同じリスク許容度でより浅くしか
        /// 潜れないように</b>コスト側を詰める。 踏破 26.9 マス → 18 前後、 Λ 由来 19.9 品 → 13 前後の想定。
        /// エスカレーション曲線の巻き戻し ([[Escalation.CurveStd]]) と同時に入れている。</para></summary>
        public const int LambdaDebuffInterval = 2;

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
            GateRitual,       // 7層終端〈門〉での3つの代償 (旧 SinRitual)
            FloorClear,       // ボス撃破→次フロア
            RunClear,         // ラン完了
            GameOver,         // 敗北
            /// <summary>ヴェスカ撃破後の一択待ち。 **エンディング分岐はここだけ** (2026-08-17)。
            /// <see cref="ChooseRiftFate"/> を呼ぶまで進まない。 末尾に足すこと ── 既存値の番号を動かさない。</summary>
            RiftChoice,
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
        /// <summary>プレイヤーの基礎最大HP。 **SerializeField をやめてコード定数にした (2026-08-03)。**
        ///   経緯: 以前は `[SerializeField] int startingHP` で、 実効値はシーン側の 30 だった。
        ///   2026-07-28 に「30→50」としたつもりの変更は RunState.Initialize の**既定引数だけ**を
        ///   書き換えており、 StartNewRun がフィールドを渡す以上一度も効いていなかった。
        ///   さらにシーンの値を直接書き換えても、 エディタがメモリ上の旧値を保持していて反映されない。
        ///   正本ポリシー(実コードが唯一の正本)どおり、 ここを唯一の定義点にする。
        ///
        /// 経緯: 通常戦を 1T → 3〜4T にして敵が手番を得るようになり、 1 ランの総被ダメが
        /// 約 3 倍 (~101) に増えたため 30 → 100 とした。 その後 2026-08-04 の消費アイテム再編で
        /// 回復/シールドが常にショップに並ぶようになり、 **耐久の一部を消費アイテム側へ移した**ので
        /// 75 へ引き下げている ── 「回復を買うか強化を買うか」がショップの選択として立つようにするため。</summary>
        public const int BaseStartingHP = 75;

        [Header("デバッグ")]
        [SerializeField] private bool autoStartRun = false;
        [SerializeField] private bool logPhaseChanges = true;

        // === 内部参照 ===
        private ItemEquipHandler equipHandler;
        private EnemyData firstEliteEnemy;

        // === GateRitual 状態 (門の 3 工程それぞれが既に決まったか) ===
        private bool gateBloodResolved;
        private bool gateRelicResolved;
        private bool gateTransferResolved;

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
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _shuttingDown = false;
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnApplicationQuit() => _shuttingDown = true;

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
            // 自分が現役なら静的参照も畳む。 破棄済み参照を残すと Unity の擬似 null 経由で
            // 「非 null だが使えない Instance」になり、 原因の分かりにくい停止を生む。
            if (_instance == this) _instance = null;
        }

        // ============================================================
        //  公開API — UI/外部から呼ばれる操作
        // ============================================================

        /// <summary>新しいランを開始</summary>
        public void StartNewRun()
        {
            // **戦闘終了イベントの購読をここで確実にする。**
            //   購読は Start() で 1 回だけ張っていたが、 それだと
            //   「GameManager の Start より後に CombatManager の実体が入れ替わる」順序で
            //   購読が宙に浮く。 実際 Ultra の episode worker で発生し、 戦闘は 13T 走って
            //   勝っているのに**誰も聞いていない**のでフェーズが Combat のまま止まった。
            //   ラン開始ごとに張り直せば、 起動順に依存しなくなる。
            //   `-=` を先に呼ぶので二重購読にはならない (未購読への -= は無害)。
            var cmForEvents = CombatManager.Instance;
            if (cmForEvents != null)
            {
                cmForEvents.OnCombatEnd -= HandleCombatEnd;
                cmForEvents.OnCombatEnd += HandleCombatEnd;
            }

            Run = new RunState();
            Run.Initialize(BaseStartingHP);
            // 〈長引く負傷〉の閾値基準。 **ここで焼き付けて以降変えない** (§15-2 v4.0)
            Run.baseMaxHPAtRunStart = Run.playerMaxHP;

            // 2026-06-28: 職業セット + スターター消耗品 1 個配布
            Run.playerClass = SelectedClass;
            string starterConsumable = ClassStarter.GetConsumableId(Run.playerClass);
            Run.TryAddConsumable(starterConsumable);
            Log($"職業: {Run.playerClass} → スターター消耗品「{starterConsumable}」を配布");

            // 特殊端子 (§6-5): 開始前の宣言。 **第 4 端子が存在するかどうかを決める**ので、
            //   装備の強弱ではなく「配線の自由度」を選んでいることになる。
            Run.equippedSpecialTerminalId = SelectedSpecialTerminal ?? "";
            if (!string.IsNullOrEmpty(Run.equippedSpecialTerminalId))
            {
                var td = CombatSystem.SpecialTerminals.Get(Run.equippedSpecialTerminalId);
                Log($"特殊端子: {(td != null ? td.displayName : Run.equippedSpecialTerminalId)}"
                  + $" (接続制限 {(td != null ? td.connectLimit : 0)})");
            }

            // ラン跨ぎで永続するスキル状態 (Nightfall の蓄積過剰ダメ等) をリセット
            InventorySystem.PassiveSkills.PassiveSkillRegistry.ResetAllRunState();

            // **戦闘乱数の通し番号をラン単位に戻す (2026-08-10)。**
            //   CombatManager はシングルトンなので、 リセットしないとバッチ全体で
            //   増え続け、 同一シードでも「そのランがバッチの何番目か」で戦闘の乱数列が
            //   変わる。 決定性の前提が崩れ、 ペア比較と McNemar が成立しなくなる。
            CombatSystem.CombatManager.Instance?.ResetRunCombatSequence();

            // 2026-08-17: **初期ダイスの配布を廃止。** ダイスというアイテム種別自体が無くなり、
            //   面は DiceFaceParts.BaseFaces + 出目パーツ から組む (GatherPlayerCombatStats)。
            //   `dice_wood` を入れ続けると存在しない ID が acquiredItemsEver に混ざり、
            //   学習統計に幽霊エントリが残るだけだった。 equippedDiceId 自体はセーブ互換で残置。

            // 初期武器: 職業固定 (2026-07-14 ランダム配布から変更 ── 出自選択画面の
            // 「メイン武器」表示と実体を一致させる。正本: ClassStarter.GetWeaponId)
            string starterWeapon = ClassStarter.GetWeaponId(Run.playerClass);
            if (Run.ownedPassiveItems == null)
                Run.ownedPassiveItems = new System.Collections.Generic.List<string>();
            InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, starterWeapon);
            Run.equippedWeaponId = starterWeapon;
            Log($"初期武器: {starterWeapon}");

            // メタ恒久バフを適用（HP/Gold/初期素材を底上げ）
            MetaProgression.MetaBuffApplicator.ApplyToRunStart(Run, BaseStartingHP);

            // T4-C〈凶運〉: **ラン開始時に一度だけ**全役から 2 つ封印する。
            //   メタバフ適用の後に置く ── GameRng の消費順を固定するため。
            Run.sealedRoleMask = MetaProgression.MetaDebuffApplicator.RollSealedRoles(Run);
            if (Run.sealedRoleMask != 0)
                Log($"凶運: 役を封印 (mask={Run.sealedRoleMask})");
            // メタバフ〈開幕パッシブ〉(兵站): ノーマル枠から N 個獲得して所持。
            //   2026-09-10: 兵站が素材からパッシブへ変わり、 本数が r2/5/8/10 で増える。
            //   `PickPassiveItemForBossExtra` は `BuildDedupExclude` でラン内重複を除くので、
            //   付与済みの品は次の抽選から自動的に外れる。 抽選は `RangeAuto` (毎回進む) なので
            //   同じ品を引き続けることはない。
            {
                int startPassives = MetaProgression.MetaBuffApplicator.GetStartingPassiveCount();
                for (int sp = 0; sp < startPassives; sp++)
                {
                    // 兵站 r10 (極点): **何本目かで提示数が増える** (1 本目 1 択 / 2 本目 2 択 / 3 本目 3 択)。
                    //   offers=1 なら従来どおりランダム。
                    //   **提示は必ず offers 回引く** ── 引く回数を採否で変えると GameRng の
                    //   消費列が条件で変わり、 同一シードのペア比較が壊れる。
                    int offers = MetaProgression.MetaBuffApplicator.GetStartingPassiveOffers(sp);
                    string id = null; float best = float.NegativeInfinity;
                    for (int o = 0; o < offers; o++)
                    {
                        string cand = PickStartingPassive();
                        if (string.IsNullOrEmpty(cand)) continue;
                        float sc = AutoTest.LearnedPriorityProvider.BuyScore(cand);
                        if (sc > best) { best = sc; id = cand; }
                    }
                    if (string.IsNullOrEmpty(id)) break;   // プール枯渇
                    if (Run.ownedPassiveItems == null)
                        Run.ownedPassiveItems = new System.Collections.Generic.List<string>();
                    InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
                    Log($"メタ報酬: 開幕パッシブ「{id}」を獲得 ({sp + 1}/{startPassives})");
                }
            }

            // 2026-07-25 v6: 種火 r3 (充電/臨界/毒/出血) - キーワード BRONZE を 1 本ずつ開幕所持
            foreach (var kw in new[] { "charge", "rinkai", "poison", "bleed" })
            {
                if (!MetaProgression.MetaBuffApplicator.IsSparkStartingItemUnlocked(kw)) continue;
                string id = MetaProgression.SparkStarterPicker.PickStarterFor(kw, Run);
                if (string.IsNullOrEmpty(id)) continue;
                if (Run.ownedPassiveItems == null)
                    Run.ownedPassiveItems = new System.Collections.Generic.List<string>();
                InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
                Log($"メタ報酬: 種火 [{kw}] 開幕パッシブ「{id}」を獲得");
            }


            LastCombatResult = null;
            CurrentEnemy = null;
            IsEliteSecondFight = false;
            inFalseMerchantCombat = false;

            SetPhase(GamePhase.RunStart);
            MetaProgression.Achievements.AchievementService.BeginRun(Run);
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

            // 戦闘名の開示 (2026-08-28)。 **到着した時点で、隣接ノードぶんだけ**開く。
            //   遠くからは見えないので「危険なプリセットを何手も前から迂回する」ことはできず、
            //   判断は必ず「いまの手番で、この分岐をどうするか」に閉じる。
            RevealAdjacentEncounters();

            // 落石のような雹: ノード移動毎に HP-1
            int boulderDmg = MapSystem.AbyssPhenomena.AbyssPhenomenonCombatHooks.OnNodeMove(Run);
            if (boulderDmg > 0 && Run.playerHP > 0)
            {
                boulderDmg = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                    boulderDmg, Run, Run.playerHP);
                int hpBeforeBoulder = Run.playerHP;
                Run.playerHP = Mathf.Max(0, Run.playerHP - boulderDmg);
                MetaProgression.Achievements.AchievementService.NoteExternalHpDamage(hpBeforeBoulder - Run.playerHP);
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
                if (Run.dimensionalDisturbance % LambdaDebuffInterval == 0)
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

            // 挑戦デバフ 軸14〈焦燥〉: この層の踏破が閾値を超えたら、 1 マスごとに希望 −5。
            //   「長く彷徨うほど追い詰められる」形。 閾値以内なら一切効かないので、
            //   最短で降りるプレイには無害 ── 寄り道の対価としてだけ働く。
            Run.tilesThisFloor++;
            int impatienceMax = MetaProgression.MetaDebuffApplicator.GetImpatienceTileThreshold();
            if (impatienceMax > 0 && Run.tilesThisFloor > impatienceMax)
            {
                HopeSystem.Reduce(Run, MetaProgression.MetaDebuffApplicator.ImpatienceHopeLoss);
                Log($"焦燥: {Run.currentFloor}層 {Run.tilesThisFloor}マス目 (閾値{impatienceMax}) → 希望 -{MetaProgression.MetaDebuffApplicator.ImpatienceHopeLoss}");
            }

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

                // ボスマス戦闘では灯火 / フルーレ・バレエ による救済を行わない（復活して戻ると
                // ボスノードは収束ノードで出力接続が無く進行不能になるため）。
                // **ラストスタンドはここには居ない** ── 2026-09-13 から戦闘内で
                // その場蘇生する (CombatManager.TryLastStandRevive)。 退却を伴わないので
                // ボスノードで詰まらない。
                bool isBossFight = MapManager.Instance?.CurrentNode != null
                    && MapManager.Instance.CurrentNode.type == TileType.Boss;

                if (LastStand.TryConsumeRevival(Run, isBossFight))
                {
                    Log("救済発動: マップへ戻る");
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

            var mm = MapManager.Instance;
            var node = mm.CurrentNode;

            // メタ: 敵撃破トークン + 戦闘勝利金
            // metaWinGold は最大3、全戦闘に加算すると経済を歪める(1/5 デノミ済み環境で +90G/ラン に到達)。
            // ボス撃破時のみ適用し、通常戦闘では 0 とする。
            MetaProgression.MetaTokenEarner.OnEnemyDefeated();

            // 遺物軸〈Λ共鳴〉: Λ層で戦闘を終えるたびに会心倍率が積む。 **勝敗は問わない**
            // (負ければランが終わるので実質は勝利数だが、 条件を単純に保つ)。
            if (Run != null && Run.inLambda) Run.lambdaCombatsFinished++;

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
                        InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, ItemIds.ChevalierRapier);
                        Log("剣聖の得物「シュヴァリエのレイピア」を手に入れた。");
                    }
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
                int reward = GoldIncome.Gain(Run, rewardBase + metaWinGold, "ボス報酬");

                // [廃止 2026-09-12] メタ: ボス撃破時の追加パッシブ報酬 (旧 強奪 r10)。

                SetPhase(GamePhase.Reward);
                OnRewardGranted?.Invoke(reward);
                return;
            }

            // エリートマスは1連戦（精鋭1体で完結）。2連戦は廃止。

            // 通常報酬（傲慢: 通常戦闘=0、エリート以上=×2、いずれも割合計算が先）
            // エンカウントプリセットの報酬倍率 (2 体戦 = ×2)。 2 体ぶんの攻撃を受け切った対価。
            int coins = FloorManager.CalculateRewardCoins(Run.currentFloor, true, result.totalTurns,
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
                ValorEliteFights++;   // [計装] 格上げが効いているかを見る
                float valorGoldPct = MetaProgression.MetaBuffApplicator.GetEliteGoldPct();
                if (valorGoldPct > 0f)
                    coins = Mathf.RoundToInt(coins * (1f + valorGoldPct / 100f));

                // 〈精鋭首の請取証〉: エリート勝利だけ ゴールド+2 (2026-09-15)。
                //   **通常戦に乗せない** ── 上の〈剛胆〉と同じ理由で、 全戦闘に配ると
                //   「強敵を選ぶ」という性格が消えて単なる金庫になる。
                //   エリートは 1 ラン 6.3 戦なので +25G 前後。 割増 (%) ではなく定額なのは、
                //   層が進んでも同じ重みにしないため (報酬本体が層で伸びる)。
                if (Run.OwnsPassive(ItemIds.EliteBountyReceipt)) coins += 4;
            }

            coins += metaWinGold;
            coins = GoldIncome.Gain(Run, coins, "戦闘報酬");
            IsEliteSecondFight = false;

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
            //   Λ 環状線のマスは EliteBattle 扱いなので、 Λ の取り分も平均では変わらない。
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
                if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (dedup.Contains(it.internalName)) continue;
                pool.Add(it);
            }
            if (pool.Count == 0) return null;

            float bias = MetaProgression.MetaBuffApplicator.GetStartingPassiveRarityBias();
            return InventorySystem.RarityWeightedPicker.Pick(pool, null, bias)?.internalName;
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

            ReturnToMapOrClearFloor();
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
            // 〈遅い回復〉: **あらゆる回復に掛かる**ので休憩地点にも適用する。
                int heal = Mathf.CeilToInt(Run.playerMaxHP * ratio
                                           * MetaProgression.MetaDebuffApplicator.GetHealMultiplier());
            heal = MetaProgression.MetaDebuffApplicator.ApplyJudgmentHealReduction(heal, Run);
            int beforeRestHP = Run.playerHP;
            Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
            // 〈浅い眠り〉: 回復スポットの頭打ち。 **対象は前哨基地と休憩地点だけ**。
            Run.playerHP = MetaProgression.MetaDebuffApplicator.ApplyHealSpotCap(Run.playerHP, Run.playerMaxHP, beforeRestHP);
            MetaProgression.MetaDebuffApplicator.NoteHeal(heal, Run.playerHP - beforeRestHP, Run);
            RunChronicle.Rest(Run, Run.playerHP - beforeRestHP, beforeRestHP, Run.playerMaxHP);
            Log($"休憩回復: +{Run.playerHP - beforeRestHP}HP ({(int)(ratio*100)}%) (現在: {Run.playerHP}/{Run.playerMaxHP})");
            // メタ Lv51: 同じ休憩マスで武器強化も同時に試みる（素材不足なら無視・回復は通る）
            if (MetaProgression.MetaBuffApplicator.IsBossRestHealAndUpgradeUnlocked())
                TryAutoUpgradeAtRest();
            ReturnToMapOrClearFloor();
        }

        /// <summary>休憩で食事を選択（空腹を全回復）。HP回復・武器強化と並ぶ独立選択肢。</summary>
        public void RestEat()
        {
            if (CurrentPhase != GamePhase.RestStop) return;
            var h = MapManager.Instance?.Hunger;
            int before = h?.Current ?? 0;
            h?.RestoreFull();
            Log($"休憩食事: 空腹 {before} → {h?.Current ?? 0}/{h?.Max ?? 0}（全回復）");
            ReturnToMapOrClearFloor();
        }

        // ===== 武器Tier強化＆限界突破システム =====
        public const int MaxLimitBreak = 10;

        /// <summary>その武器の家系と段 (items.json の family / tier)。 進行武器でなければ (null, 0)。
        ///
        /// <para><b>id を綴りで切らない</b> (2026-09-22)。 id は表示名なので、
        /// 正規表現で <c>_tN</c> を取り出す旧実装は品名を変えた瞬間に黙って外れる。</para></summary>
        private static (string family, int tier) FamilyTierOf(string weaponId)
        {
            var d = string.IsNullOrEmpty(weaponId) ? null : ItemDatabase.Instance?.GetItem(weaponId);
            return d == null ? (null, 0) : (d.family, d.tier);
        }

        /// <summary>装備武器が家系の進行武器か。</summary>
        public static bool IsTierWeapon(string weaponId)
        {
            var (f, t) = FamilyTierOf(weaponId);
            return !string.IsNullOrEmpty(f) && t > 0;
        }

        /// <summary>同家系の次に存在するTier武器ID。無ければ null（=最上位Tier）。</summary>
        public static string NextTierWeaponId(string weaponId)
        {
            var (family, n) = FamilyTierOf(weaponId);
            if (string.IsNullOrEmpty(family) || n <= 0) return null;  // 竜閃等は強化対象外
            var db = ItemDatabase.Instance;
            if (db == null) return null;
            for (int k = n + 1; k <= n + 5; k++)
            {
                var next = db.GetWeapon(family, k);
                if (next != null) return next.id;
            }
            return null;
        }

        /// <summary>強化の行き先。 <b>純家系のラダーを 1 段上がるだけ</b>。
        ///
        /// <para>2026-09-21 に複合武器を廃止するまでは、 ここが「純 T4 と複合 T4 の分岐」で、
        /// 学習済み regβ による選択・同点の乱択・探索ホールドアウトを抱えていた。
        /// 分岐先が無くなったので、 判断そのものが消えている (GAME.md §24)。</para></summary>
        public static string ChooseUpgradeTarget(RunState run)
            => NextTierWeaponId(run?.equippedWeaponId);

        /// <summary>現在の総合強化段階。進行武器: (tier-1)*2 + plus + 業物。非進行Tier武器: tier番号-1。</summary>
        private static int OverallStage(RunState run)
        {
            var (fam, n) = FamilyTierOf(run.equippedWeaponId);
            if (string.IsNullOrEmpty(fam) || n <= 0) return 0;
            // **2026-09-05: T1 廃止で起点が T2 になった** ── WeaponProgression と同じ数え方に揃える。
            //   T2=0 / T2+=1 / T3=2 / T3+=3 / T4=4 / T4+=5
            if (InventorySystem.PassiveSkills.WeaponProgression.IsProgressionWeapon(run.equippedWeaponId))
                return System.Math.Max(0, (n - 2) * 2 + (run.weaponPlus > 0 ? 1 : 0) + run.limitBreakStage);
            return n - 1;
        }

        /// <summary>T4+ までの段階別コスト表 (stage 0..4 の 5 段)。
        ///
        /// <para><b>2026-09-05 再ベース。</b> T1 廃止で段が 8 → 6 になり、 必要な強化回数が
        /// 7 → 5 に減った。 旧表 <c>{1,1,2,2,3,3,4}</c> の合計 16 をそのまま維持すると
        /// 素材が余りすぎる (素材の使い道は武器強化ただ 1 つしかない) ので、
        /// <b>合計 16 を保ったまま 5 段へ配り直した</b> ── <c>{2,2,3,4,5}</c>。
        /// 後ろほど重くしてあるので、 T4 へ届かせるかどうかが判断になる。</para></summary>
        private static readonly int[] WeaponUpgradeCostTable = { 2, 2, 3, 4, 5 };

        /// <summary>次の1段階強化に必要な素材数。 強化不可なら int.MaxValue。
        /// コスト表は T4+ までの {1,1,2,2,3,3,4} だけ。
        ///
        /// **2026-08-10: 業物 (限界突破) を廃止した。** 進行武器の強化は **T4+ が終点**。
        /// 廃止理由は §24 ── 業物は「与ダメ +20%/lv」の素の倍率で、 素材の唯一の
        /// 後半シンクでもあったため、 ショップ価格 (搾取経済) の効きが素材価格の
        /// 2^N カーブを通って **log で潰れ**、 挑戦デバフに段差を作れなくなっていた。
        /// 代償は敵HPの後半層 −10% (<see cref="CombatSystem.CombatManager"/> 側)。</summary>
        public static int WeaponUpgradeCost(RunState run)
        {
            if (run == null) return int.MaxValue;
            bool prog = InventorySystem.PassiveSkills.WeaponProgression.IsProgressionWeapon(run.equippedWeaponId);
            int stage = OverallStage(run);
            if (stage >= WeaponUpgradeCostTable.Length) return int.MaxValue;
            int cost = WeaponUpgradeCostTable[stage];
            if (prog)
            {
                // T4+ (次 Tier 無し かつ + 済み) が終点。 これ以上は素材を受け付けない。
                bool atTopPlus = run.weaponPlus > 0 && NextTierWeaponId(run.equippedWeaponId) == null;
                if (atTopPlus) return int.MaxValue;
                return cost;
            }
            if (NextTierWeaponId(run.equippedWeaponId) != null) return cost;
            return int.MaxValue;
        }

        /// <summary>素材を消費できる先が残っているか (武器強化 or 装備ダイス強化)。
        ///
        /// **業物廃止 (2026-08-10) で必要になった判定。** T4+ 到達後の進行武器は素材を
        /// 受け付けなくなったので、 ダイス強化も上限なら素材は完全な死に資源になる。
        /// ショップは在庫無限で売り続けるため、 **買う側**がここを見て止まること。</summary>
        public static bool CanSpendMaterials(RunState run)
        {
            if (run == null) return false;
            if (WeaponUpgradeCost(run) != int.MaxValue) return true;
            var d = InventorySystem.ItemDatabase.Instance?.GetItem(run.equippedDiceId);
            if (d == null) return false;
            int cur = DiceEnhance.GetLevel(run, run.equippedDiceId);
            return cur < DiceEnhance.MaxLevelForItem(d)
                && DiceEnhance.CostForNextLevel(d, cur + 1) != int.MaxValue;
        }

        /// <summary>武器強化で Tier が進んだ際に通知される (旧ID, 新ID)。
        /// L1 学習が中間 Tier を acquiredItemsEver に記録するためのフック。
        /// 同一 Tier 内の + 昇格は通知しない (Tier ID が変わらないため)。</summary>
        public static event System.Action<string, string> OnWeaponTierUpgraded;

        /// <summary>武器を1段階強化。進行武器: T_n→T_n+→次Tier→…→**T4+ で終点**。
        /// 非進行Tier武器: 従来のTier置換。free=true で素材消費なし（鍛冶の霊薬）。
        /// 2026-08-10: 業物 (T4+ 以降の限界突破) を廃止。 T4+ 到達後は
        /// <see cref="WeaponUpgradeCost"/> が int.MaxValue を返すのでここへ来ない。</summary>
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
                    // **T3+ → T4 は分岐点** (2026-09-05)。 純家系へ深めるか、 別家系と混ぜるか。
                    //   純   T4 = A-III + B-II + 固有1 + 固有2   (一点特化)
                    //   複合 T4 = A-III(親1) + A-III(親2) + 複合固有  (2軸)
                    //   どちらへ進むかは**学習済みの準パワー**で決める ── 手置きの優先順位は入れない。
                    //   同点なら純を採る (既存挙動の維持)。
                    string next = ChooseUpgradeTarget(run);
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
            {
                OnWeaponTierUpgraded?.Invoke(prevWeaponId, run.equippedWeaponId);
                // 行動台帳: Tier が上がったときだけ積む。 素材を注いだだけで
                //   段が上がらない回は「山」ではないので載せない。
                bool apex = NextTierWeaponId(run.equippedWeaponId) == null;
                RunChronicle.Forge(run,
                    apex ? RunChronicle.ForgeApex : RunChronicle.ForgeUpgrade,
                    run.equippedWeaponId, cost);
            }
            // 天工開物: 武器を強化するたび、強化素材を1つ返還する
            if (run != null && run.OwnsPassive(ItemIds.HeavenlyCraft))
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
            // 2026-07-18: 武器が強化不可なら ダイス強化にフォールバック (共通素材)
            int wcost = WeaponUpgradeCost(Run);
            bool weaponPossible = wcost != int.MaxValue && Run.weaponMaterials >= wcost;
            if (!weaponPossible)
            {
                if (DiceEnhance.TryUpgradeEquipped(Run))
                {
                    Log($"武器強化不可 → ダイス強化: {Run.equippedDiceId} Lv{DiceEnhance.GetLevel(Run, Run.equippedDiceId)} (残素材 {Run.weaponMaterials})");
                }
                else
                {
                    Log($"武器強化 不可 (必要{wcost}/所持{Run.weaponMaterials}) → 回復に切替");
                    RestHeal();
                    return;
                }
            }
            else
            {
                int before = Run.weaponMaterials;
                if (TryUpgradeWeapon(Run))
                {
                    int used = before - Run.weaponMaterials;
                    Log($"武器強化 ×1: {Run.equippedWeaponId}{(Run.weaponPlus > 0 ? "+" : "")} 業物Lv{Run.limitBreakStage} (素材-{used}, 残{Run.weaponMaterials})");
                }
            }
            // メタ Lv51: 同じ休憩マスで HP 回復も同時に実行
            if (MetaProgression.MetaBuffApplicator.IsBossRestHealAndUpgradeUnlocked())
            {
                float ratio = ActiveModifier?.restHealMultiplier ?? 0.3f;
                // 〈遅い回復〉: **あらゆる回復に掛かる**ので休憩地点にも適用する。
            int heal = Mathf.CeilToInt(Run.playerMaxHP * ratio
                                       * MetaProgression.MetaDebuffApplicator.GetHealMultiplier());
                heal = MetaProgression.MetaDebuffApplicator.ApplyJudgmentHealReduction(heal, Run);
                int beforeHP = Run.playerHP;
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
                MetaProgression.MetaDebuffApplicator.NoteHeal(heal, Run.playerHP - beforeHP, Run);
                Log($"[メタLv51] 同時回復: +{heal}HP ({(int)(ratio*100)}%) (現在: {Run.playerHP}/{Run.playerMaxHP})");
            }
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>RestHeal 経由で呼ばれる、 素材があれば武器強化を1回試みるヘルパー（メタ Lv51 用）。
        /// 2026-07-18: 武器強化不可ならダイス強化にフォールバック。</summary>
        private void TryAutoUpgradeAtRest()
        {
            int cost = WeaponUpgradeCost(Run);
            if (cost != int.MaxValue && Run.weaponMaterials >= cost)
            {
                int before = Run.weaponMaterials;
                if (TryUpgradeWeapon(Run))
                {
                    int used = before - Run.weaponMaterials;
                    Log($"[メタLv51] 同時強化 ×1: {Run.equippedWeaponId}{(Run.weaponPlus > 0 ? "+" : "")} 業物Lv{Run.limitBreakStage} (素材-{used}, 残{Run.weaponMaterials})");
                }
            }
            else if (DiceEnhance.TryUpgradeEquipped(Run))
            {
                Log($"[メタLv51] 同時強化 (ダイス): {Run.equippedDiceId} Lv{DiceEnhance.GetLevel(Run, Run.equippedDiceId)}");
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
            if (cost != int.MaxValue && Run.weaponMaterials >= cost)
            {
                int before = Run.weaponMaterials;
                if (!TryUpgradeWeapon(Run)) return false;
                Run.outpostUpgradeUsedThisFloor = true;
                int used = before - Run.weaponMaterials;
                Log($"[前哨基地] 武器強化 ×1: {Run.equippedWeaponId}{(Run.weaponPlus > 0 ? "+" : "")} 業物Lv{Run.limitBreakStage} (素材-{used}, 残{Run.weaponMaterials})");
                return true;
            }
            // 2026-07-18: 武器強化不可ならダイス強化にフォールバック
            if (DiceEnhance.TryUpgradeEquipped(Run))
            {
                Run.outpostUpgradeUsedThisFloor = true;
                Log($"[前哨基地] ダイス強化: {Run.equippedDiceId} Lv{DiceEnhance.GetLevel(Run, Run.equippedDiceId)} (残素材 {Run.weaponMaterials})");
                return true;
            }
            return false;
        }

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
            RevealAdjacentEncounters();

            SetPhase(GamePhase.FloorIntro);
            // 層タイトル表示（Λ層。確定文言の正本: docs/GAME.md §5）
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
            Run.lambdaRingsEntered++;   // 計装: マス起動まで到達した回数
            // 50% エリート / 50% 固有イベント
            if (GameLoop.GameRng.Value("GameManager.3") < 0.5f)
            {
                Run.lambdaRingElite++;
                // エリート戦: ノードを一時的に EliteBattle 扱いにして既存のエリート抽選/強化を流用。
                // **StartBattleTile は resolvedType を読まない**ので、 立てた直後に戻して構わない。
                // 立てっぱなしにすると ActivateTile の oneShot 判定で LambdaRing と見なされなくなる
                // (上記 2026-08-08 の修正で type 基準にしたが、 ここでも残さないでおく)。
                node.resolvedType = TileType.EliteBattle;
                IsEliteSecondFight = false;
                firstEliteEnemy = null;
                // 固有イベントと同じ深度補正を戦闘報酬にも効かせる。 片方だけだと
                // 「エリートを引いた深いマスは無価値」という抜けが残る。
                ApplyLambdaDepthLootFloor();
                Log("Λ環状線: エリート戦");
                StartBattleTile();
                node.resolvedType = null;   // 次回再訪でまた抽選できるよう戻す
            }
            else
            {
                Run.lambdaRingEvent++;
                node.resolvedType = null; // 次回再訪時にまた抽選するため戻す
                ResolveLambdaEvent();
            }
        }

        /// <summary>**Λ は深く潜るほど品質が上がる** (2026-08-05)。 踏破マス数に応じて
        /// 次の戦利品の最低レア度を引き上げる (6マス SILVER / 12 GOLD / 18 LEGENDARY)。
        ///
        /// 実測で Λ は 1 マスあたりのアイテム取得が 0.96 → 0.32、 GOLD が 2.45 → 0.95 と
        /// 逓減する一方、 デバフ付与は 1 マス 0.33 で一定だった。 浅いマスで良品を取り尽くす
        /// ため深追いする理由が原理的に無く、 7層クリアは 安全策 34.0% > リスキー 29.0% >
        /// 現行適応 24.6% と「即撤退が最適」になっていた。 単価を深度で上げて逓減を打ち消す。
        ///
        /// **既に nextLootMinRarity が立っている場合は触らない** ── 鑑定の眼鏡など
        /// 他の保証と競合させず、 先に立っている方を尊重する。</summary>
        private void ApplyLambdaDepthLootFloor()
        {
            if (Run == null || !Run.inLambda) return;

            // [撤回] 深度連動のレア度下限 (6マス SILVER / 12 GOLD / 18 LEGENDARY)。
            //   2026-08-08 に導入し同日撤回。 導入根拠だった「深いマスほど 1 マスの利得が薄い」は、
            //   ActivateTile の oneShot 判定バグ (Λ環状線ノードが一度エリートを引くと永久沈黙) が
            //   生んだ偽の逓減だった。 修正後は利得が踏破に完全比例するため、 質での補正は不要。
            //   計装 (候補残数) だけは診断価値があるので残す。
            CountLambdaLootPool(-1, out Run.lambdaPoolRemainingAll, out Run.lambdaPoolRemainingFloored);
        }

        /// <summary>Λ 戦利品の抽選候補を数える。 **<see cref="PickTreasureChoicePair"/> と同じ絞り込み**を
        /// 使うこと ── 条件がずれると「枯れているか」の検証にならない。</summary>
        private void CountLambdaLootPool(int minRarity, out int all, out int floored)
        {
            all = 0; floored = 0;
            var db = ItemDatabase.Instance;
            if (db == null) return;
            var items = db.GetAllItems();
            if (items == null) return;
            var dedup = BuildDedupExclude();
            foreach (var it in items)
            {
                if (it == null) continue;
                if (it.category == ItemCategory.Weapon) continue;
                if (it.category == ItemCategory.Quest) continue;
                if (!InventorySystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (dedup.Contains(it.internalName)) continue;
                all++;
                if (minRarity < 0 || (int)it.rarity >= minRarity) floored++;
            }
        }

        /// <summary>Λ固有イベント: HPが半分未満なら回復、十分なら未所持寄りのパッシブ1つを獲得。
        /// (ロジック先行のためボット向け自動解決。UI実装時に2択提示へ差し替える)。</summary>
        private void ResolveLambdaEvent()
        {
            bool wantHeal = Run.playerHP * 2 < Run.playerMaxHP;
            if (wantHeal)
            {
                Run.lambdaRingEventHeal++;   // 計装: アイテム抽選に到達しなかった回
                int heal = Mathf.CeilToInt(Run.playerMaxHP * 0.4f);
                int before = Run.playerHP;
                Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
                Log($"Λ固有イベント: 回復 +{Run.playerHP - before} ({Run.playerHP}/{Run.playerMaxHP})");
            }
            else
            {
                Run.lambdaRingEventItem++;
                ApplyLambdaDepthLootFloor();
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
            MetaProgression.Achievements.AchievementService.NoteFloorEntered(Run?.currentFloor ?? 0);
            Run.tilesThisFloor = 0;   // 軸14〈焦燥〉の層別カウンタ
            var mm = MapManager.Instance;

            // 契約システム: 前層終了 → 新層開始 hook (この呼び順は重要)
            // 前層終了で 商業連合隊の層末収入支払い、 サーカスフラグクリア等が行われる
            // [廃止] 旅団契約の層フック (2026-08-11 削除)

            // メタ: トークン獲得 + 1層/3層で固有恒久デバフ抽選
            MetaProgression.MetaTokenEarner.OnFloorReached(Run.currentFloor);
            // T4-E〈最後の審判〉: 3層・5層で大罪を付与 (§15-2 v3.0)。 v1 の 1層付与は廃止。
            if (Run.currentFloor == 3) MetaProgression.MetaDebuffApplicator.TryGrantOnFloor3(Run);
            if (Run.currentFloor == 5) MetaProgression.MetaDebuffApplicator.TryGrantOnFloor5(Run);

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
            if (Run.currentFloor == 5 && MetaProgression.PermanentDebuffEffects.HasGreed(Run))
            {
                Log($"恒久デバフ {MetaProgression.PermanentDebuffIds.Greed}: 所持ゴールド {Run.coins}→0");
                Run.coins = 0;
            }

            // 層突入のパッシブ (2026-09-15)。 **層デバフより前に鳴らす** ── 〈ムシュファの強欲〉が
            //   5 層で所持金を 0 にするので、 後ろに置くと配ったゴールドが同じ層で消える。
            InventorySystem.PassiveItems.PassiveItemManager.OnFloorEnter(Run);

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

            // 前哨基地に立った時点で、 最初の分岐ぶんの戦闘名を開示する。
            RevealAdjacentEncounters();

            // 層タイトル表示（確定文言の正本: docs/GAME.md §5。ビジュアルは未実装、OnShow を UI が購読）
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
            else if (Run.currentFloor == 8)
            {
                // **8層 (Null Point) に前哨基地の回復は無い。** ここは門が転移させた先の一点で、
                //   野営できる場所ではない。 補給は 7 層 (店 → 休憩 → 門) で終わっている。
                //   回復を入れると、 門で払った最大HP −25% を回復側で埋め戻せてしまい、
                //   代償が「HP を削られた状態で入るかどうか」に化ける。
                Log("Null Point: 転移した先に、休める場所は無い");
            }
            else if (Run.currentFloor == 7)
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

            // [撤回] 前哨基地の希望回復 (減少分の20%)。 2026-08-05 に試して即日撤回。
            //   各層で発動し、 減っているほど回復量が増えるため実質「毎層リセット」になり、
            //   最低希望 38.9 → 71.0 / 発狂到達率 15.6% → 0.4% と希望が資源でなくなった。
            //   希望の回復は消費アイテム側 (cons_hope_*) で行う。

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
                        InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, pid);
                        PlunderDrops++;
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
                        InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, pid);
                        ValorEliteDrops++;
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
                                got = InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
                            if (got) Loadout.TryAutoEquip(Run, id);
                            loot++;
                            if (got) RobberyLootApplied++;
                        }
                        Run.robberyPendingItems.Clear();
                    }
                    int gold = GameLoop.GameRng.RangeAuto("GameManager.4", 90, 181);   // 2026-08-10 収入 ×3
                    gold = GoldIncome.Gain(Run, gold, "値下げ交渉", applyLastStandFilter: false);
                    Run.playerHP = Mathf.Max(1, result.playerHPRemaining);
                    RobberyWins++;
                    RobberyLootTotal += loot;
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
                    RobberyLosses++;
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

            int floor = map.floor > 0 ? map.floor : Run.currentFloor;

            // 現在地そのものも解いておく (踏んだ瞬間に StartBattleTile が読む)。
            FloorManager.EnsureEncounter(floor, here);
            here.encounterRevealed = true;

            var reachable = map.GetReachableFrom(here.id);
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
            // Λ環状線/中央は周回前提のため oneShot から除外（毎回再発火＝再抽選）。
            //
            // **除外判定は EffectiveType ではなく素の type で行う (2026-08-08 修正)。**
            //   ActivateLambdaRing のエリート枝が node.resolvedType = EliteBattle を立てたまま
            //   戻さないため、 EffectiveType 基準だとそのノードは以後 LambdaRing と見なされず
            //   oneShot 扱いになり activated が立って**二度と起動しなくなる**。 環状線はノード数が
            //   限られるので数周で全ノードが沈黙し、 踏破を 2 倍にしてもマス起動が 5.7 回で頭打ち、
            //   取得アイテムが 7.9 個で固定される一方、 デバフだけが踏破数に比例して積み続けていた。
            var baseType = node.type;
            bool oneShot = baseType != TileType.Boss && baseType != TileType.Outpost
                        && baseType != TileType.LambdaRing && baseType != TileType.LambdaExit;
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
                        && InventorySystem.Shop.ShopManager.RobberyBlocksShops)
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

        /// <summary>2 体戦のときの 2 体目。 単体戦では null。 報酬計算と UI が読む。</summary>
        public CombatSystem.EnemyData CurrentEnemySecondary { get; private set; }

        /// <summary>いま戦っているエンカウントの報酬倍率 (2 体戦 = 2.0)。</summary>
        public float CurrentEncounterRewardMultiplier { get; private set; } = 1f;

        private void StartBattleTile()
        {
            var node = MapManager.Instance?.CurrentNode;
            int floor = Run.currentFloor;

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
                var (fam, lv) = InventorySystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ps.internalName);
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
            int idx = GameLoop.GameRng.RangeAuto("GameManager.5", 0, owned.Count);
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

            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(enemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
            CombatManager.Instance.SetFleeAfterTurns(3);
            CombatManager.Instance.AddEnemyDiceTotalBonus(diceTotalBonus);
        }

        /// <summary>ボスノードを持たない層 (2/4) で、 終端の休憩行を踏み終えたか。
        /// その層のボス撃破に相当する「層クリア」条件として使う。
        /// 終端判定は **前方への接続が無いこと** で行う (行番号を直に見ない)。</summary>
        private bool IsBosslessFloorCleared()
        {
            if (Run == null) return false;
            if (MapSystem.MapGenerator.HasBoss(Run.currentFloor)) return false;
            var mm = MapManager.Instance;
            var node = mm?.CurrentNode;
            if (node == null || mm.CurrentMap == null) return false;
            // 横移動しか残っていない = 前へ進めない = 終端
            var (forward, _) = mm.CurrentMap.CategorizeMovesFrom(node.id);
            return forward == null || forward.Count == 0;
        }

        /// <summary>タイル解決後の共通後処理。 ボス撃破 または ボスなし層の終端到達で層クリア。</summary>
        private void ReturnToMapOrClearFloor()
        {
            // ボスなし層の終端判定は SetPhase(MapNavigation) 側で一元処理する。
            if (Run != null && Run.bossDefeatedThisFloor) { HandleFloorClear(); return; }
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>[計装] ボス突入時の状態を層別に積む (index 0 = 1層)。
        /// <b>「休憩で HP は戻るのに突破率が落ちる」の正体を掴むため。</b>
        /// 戻らない資源 (希望・消耗品) が消えているなら、 ここに差が出る。</summary>
        public static readonly long[] BossEntryCount = new long[9];
        public static readonly long[] BossEntryHope = new long[9];
        public static readonly long[] BossEntryHopeTierSum = new long[9];  // 0=平穏..3=絶望
        public static readonly long[] BossEntryConsumables = new long[9];
        public static readonly long[] BossEntryHpPct = new long[9];
        public static readonly long[] BossEntryPassives = new long[9];
        /// <summary>ボス突入時の装備力 (AutoTest.InventoryPower)。 **戦闘力そのもの。**</summary>
        public static readonly long[] BossEntryPower = new long[9];
        public static void ResetBossEntryStats()
        {
            Array.Clear(BossEntryCount, 0, BossEntryCount.Length);
            Array.Clear(BossEntryHope, 0, BossEntryHope.Length);
            Array.Clear(BossEntryHopeTierSum, 0, BossEntryHopeTierSum.Length);
            Array.Clear(BossEntryConsumables, 0, BossEntryConsumables.Length);
            Array.Clear(BossEntryHpPct, 0, BossEntryHpPct.Length);
            Array.Clear(BossEntryPassives, 0, BossEntryPassives.Length);
            Array.Clear(BossEntryPower, 0, BossEntryPower.Length);
        }

        /// <summary>[計装] 〈門〉の 3 工程の結果 (2026-09-14)。
        ///
        /// <b>「払った」と「払えなかった」を分ける。</b> BOT へ accept=true を渡しても、
        /// 資源が足りなければ拒否と同じ扱いで欠陥が付く。 これを分けないと
        /// 「払うアーム」の結果が<b>払えた率と代償の重さの混合</b>になり、
        /// どちらが効いているのか読めない。
        ///
        /// <c>GateHopeBefore/After</c> は取り置き方策 (<c>AutoRunner.gateHopeReserveFromFloor</c>)
        /// が効いているかの検算用。 払った後の希望が 45 (悲観・上限恒久ロック) を
        /// 割っていないかをここで見る。</summary>
        public static long GateReached;
        public static long GateBloodPaid, GateRelicsPaid, GateTransferPaid;
        public static long GateHopeBefore, GateHopeAfter;
        public static long GateMaxHpPaid, GateRelicsBurned;
        public static void ResetGateStats()
        {
            GateReached = 0;
            GateBloodPaid = GateRelicsPaid = GateTransferPaid = 0;
            GateHopeBefore = GateHopeAfter = 0;
            GateMaxHpPaid = GateRelicsBurned = 0;
        }

        private void StartBossTile()
        {
            if (Run != null)
            {
                int fi = Mathf.Clamp(Run.currentFloor, 1, 8);
                BossEntryCount[fi]++;
                BossEntryHope[fi] += Run.hope;
                BossEntryHopeTierSum[fi] += (int)HopeSystem.GetTier(Run);
                BossEntryConsumables[fi] += Run.ownedConsumables?.Count ?? 0;
                BossEntryHpPct[fi] += Run.playerMaxHP > 0
                    ? Mathf.RoundToInt(Run.playerHP * 100f / Run.playerMaxHP) : 0;
                BossEntryPassives[fi] += Run.ownedPassiveItems?.Count ?? 0;
                try { BossEntryPower[fi] += AutoTest.InventoryPower.Compute(Run); } catch { }
                CombatSystem.BossCombatTrace.BeginFight(Run.currentFloor,
                    GameLoop.GameRng.RunIndex, "boss", Run.playerHP, Run.playerMaxHP,
                    0, Run.hope, Run.ownedPassiveItems?.Count ?? 0);
            }
            // ボスの正体は **マス側に焼き付けてある** (FloorManager.EnsureEncounter)。
            //   3 層は 3 体プールからの抽選 (2026-07-29: ボス配置を 1/3/5/6/7 に絞った際、
            //   2 層のゴブリン王と 4 層の鏡の双子を捨てず 3 層難度へ再調整して流用した) なので、
            //   **ここで引き直すと戦闘名の表示と実物が食い違う**。 焼き付けた値を読む。
            var bossNode = MapManager.Instance?.CurrentNode;
            FloorManager.EnsureEncounter(Run.currentFloor, bossNode);
            string bossId = bossNode != null && !string.IsNullOrEmpty(bossNode.encounterEnemyA)
                ? bossNode.encounterEnemyA
                : FloorManager.BossIdForFloor(Run.currentFloor,
                                              FloorManager.NodeRngIndex(Run.currentFloor, bossNode));

            // 5F裏ボス置換: シュヴァリエのレイピア所持 → シュヴァリエ・サン=ジョリオラ
            if (!SuppressLayer5HiddenBoss
                && Run.currentFloor == 5
                && Run.ownedPassiveItems != null
                && Run.ownedPassiveItems.Contains("シュヴァリエのレイピア"))
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
        //    ・代償が 3 種類とも別の資源 (最大HP / 遺物 / 希望)。 ゴールドは外した ──
        //      直前がショップなので、 ゴールドの要求は実質「買い物を我慢したか」にしかならず、
        //      選択ではなく所持金チェックだった。
        //    ・罰が 3 種類とも別のビルドを刺す (充電依存 / 一点集中 / 防御依存)。

        /// <summary>門マス到達時に呼ばれ、GateRitual フェーズへ遷移する。</summary>
        private void BeginGateRitual()
        {
            gateBloodResolved = false;
            gateRelicResolved = false;
            gateTransferResolved = false;
            GateReached++;
            GateHopeBefore += Run.hope;   // [計装] 取り置き方策の検算用 (支払い前)
            SetPhase(GamePhase.GateRitual);
            Log("地獄の果てに、大きな円盤が壁へ立てかけられている。横の文が、不思議と読める ── これは門だ。");
        }

        /// <summary>①外縁の再構成で支払う最大HPの割合。 <b>現在HPではなく最大HPを削る。</b>
        /// 門の先に休憩も前哨基地の回復も無いので、 現在HPを払う形にすると
        /// 「削られた状態で入る」だけになり、 回復手段の有無で価値が乱高下する。
        /// 最大HPを削れば<b>どう回復しても取り返せない</b>ので、 代償として一定になる。</summary>
        public const float GateBloodMaxHpPct = 0.25f;

        /// <summary>①「門の外縁を再構成するために、生物の血が必要らしい」
        /// — 捧げる = 最大HP −25%。 拒否/不能なら〈不完全な修復〉。</summary>
        public void OfferGateBlood(bool accept)
        {
            if (CurrentPhase != GamePhase.GateRitual) return;

            int demand = Mathf.Max(1, Mathf.CeilToInt(Run.playerMaxHP * GateBloodMaxHpPct));
            // 最大HP を割り切ったら死ぬので、 払っても 1 以上残ることを条件にする。
            bool canPay = accept && !Run.lastStandActive && Run.playerMaxHP - demand >= 1;
            if (canPay)
            {
                int beforeMax = Run.playerMaxHP;
                Run.playerMaxHP -= demand;
                int lost = Mathf.Max(0, Run.playerHP - Run.playerMaxHP);
                Run.playerHP = Mathf.Min(Run.playerHP, Run.playerMaxHP);
                if (lost > 0) MetaProgression.Achievements.AchievementService.NoteExternalHpDamage(lost);
                GateBloodPaid++; GateMaxHpPaid += demand;
                Log($"血を捧げた。外縁は修復され、門が動き始めた (最大HP {beforeMax}→{Run.playerMaxHP})");
            }
            else
            {
                Run.AddFlaw(GateFlaw.IncompleteRepair);
                Log(Run.lastStandActive
                    ? "ラストスタンド中は血を流せない。〈不完全な修復〉のまま門が動き始めた。"
                    : "今後を加味して血を流すことを拒んだ。不完全ではあるが、門が動き始めた。〈不完全な修復〉");
            }
        }

        /// <summary>②で焚かれる遺物の数。</summary>
        public const int GateRelicDemand = 2;

        /// <summary>②「門を起動するために、いくつかの遺物が必要らしい」
        /// — 捧げる = <b>ランダムに</b>パッシブ 2 個を失う。 拒否/不足なら〈不完全な起動〉。
        ///
        /// <para><b>選べない。</b> 旧〈遺品の儀〉は「合計 30G 以上を満たす最小ペア」＝
        /// 一番安い 2 個を自動で出しており、 実質「端数を捨てるだけ」の無痛の支払いだった。
        /// ランダムにすると<b>主力が焼ける可能性</b>が入り、 代償として意味を持つ。
        /// 抽選は専用キーの <see cref="GameRng"/> ── 同一シードのペア比較を壊さないため、
        /// <b>払う/払わないに関わらず同じ回数だけ引く</b>ようにはしない
        /// (この判断はランの最後の 1 マスで、 以降に乱数を消費する分岐が無いので影響が閉じる)。</para></summary>
        public void OfferGateRelics(bool accept)
        {
            if (CurrentPhase != GamePhase.GateRitual) return;

            if (!accept || Run.lastStandActive)
            {
                Run.AddFlaw(GateFlaw.IncompleteIgnition);
                Log(Run.lastStandActive
                    ? "ラストスタンド中は遺物を手放せない。〈不完全な起動〉"
                    : "起動に見合う遺物など無いと判断した。間に合わせの材料で起動したが、明らかに不完全だ。〈不完全な起動〉");
                return;
            }

            // 候補: ownedPassiveItems のうち、チェーン/初期装備 (ExcludedFromLift) を除いた実体。
            var candidates = new List<int>();
            var owned = Run.ownedPassiveItems;
            if (owned != null)
            {
                for (int i = 0; i < owned.Count; i++)
                {
                    string id = owned[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (AutoTest.ItemLearningStats.ExcludedFromLift.Contains(id)) continue;
                    candidates.Add(i);
                }
            }

            if (candidates.Count < GateRelicDemand)
            {
                Run.AddFlaw(GateFlaw.IncompleteIgnition);
                Log($"捧げられる遺物が {candidates.Count} 個しかない。〈不完全な起動〉");
                return;
            }

            // ランダムに GateRelicDemand 個。 高 index から除去する (並列配列の添字崩れ回避)。
            var picked = new List<int>(GateRelicDemand);
            for (int n = 0; n < GateRelicDemand; n++)
            {
                int at = GameRng.Range(0, candidates.Count, "gate.relic", n);
                picked.Add(candidates[at]);
                candidates.RemoveAt(at);
            }
            picked.Sort((a, b) => b.CompareTo(a));

            var names = new List<string>(picked.Count);
            foreach (int idx in picked)
            {
                if (idx >= 0 && owned != null && idx < owned.Count) names.Add(owned[idx]);
                InventorySystem.Helpers.PassiveAddHelper.RemoveAt(Run, idx);
            }
            GateRelicsPaid++; GateRelicsBurned += names.Count;
            Log($"遺物を門に捧げた。〈{string.Join("〉と〈", names)}〉は光を放った後、門の中で塵になって消えた。");
        }

        /// <summary>③で支払う希望。</summary>
        public const int GateHopeDemand = 40;

        /// <summary>③「門が転移を開始した時、あなたの意識が薄れゆく…」
        /// — 光に身を任せる = 希望 −40。 抗う/不足なら〈不完全な転移〉。</summary>
        public void OfferGateTransfer(bool accept)
        {
            if (CurrentPhase != GamePhase.GateRitual) return;

            bool canPay = accept && !Run.lastStandActive && Run.hope >= GateHopeDemand;
            if (canPay)
            {
                int before = Run.hope;
                HopeSystem.Reduce(Run, GateHopeDemand);
                GateTransferPaid++;
                Log($"光に身を任せ、転移の行く末に思いを馳せた (希望 {before}→{Run.hope})");
            }
            else
            {
                Run.AddFlaw(GateFlaw.IncompleteTransfer);
                Log(Run.lastStandActive
                    ? "ラストスタンド中は身を任せられない。〈不完全な転移〉"
                    : "薄れゆく意識に頑強に抵抗した。転移は不完全に終わったが、意識を失うことは避けた。〈不完全な転移〉");
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

        /// <summary>[計装 2026-09-17] リロールが不発になった理由。
        /// 0=フェーズ不一致 / 1=Instance無し / 2=TryReroll が false / 3=成功。
        /// **余剰リロールの 21.6% が「失敗」で抜けており、 そのとき残金 224.5G もある。**</summary>
        public static readonly long[] RerollOutcome = new long[4];
        public static void ResetRerollOutcome() { System.Array.Clear(RerollOutcome, 0, 4); }

        /// <summary>ショップのリロール。 <b>成否を返す。</b>
        ///
        /// <para><b>呼び出し側はコインの増減で判定しないこと</b> (2026-09-17)。
        /// 〈合札〉のように価格が 0 になる経路があると、 成功してもコインが減らないので
        /// 「失敗」と誤認する。 実際 BOT の余剰リロールは<b>店の 21.6% で金を持ったまま
        /// 抜けており (そのとき平均 224.5G)</b>、 ラン終了時の残金 92G の主因だった。</para></summary>
        public bool ShopReroll()
        {
            if (CurrentPhase != GamePhase.ShopVisit) { RerollOutcome[0]++; return false; }
            var sm = ShopManager.Instance;
            if (sm == null) { RerollOutcome[1]++; return false; }
            bool ok = sm.TryReroll(Run);
            RerollOutcome[ok ? 3 : 2]++;
            return ok;
        }

        /// <summary>[計装 2026-09-12] 値下げ交渉の勝敗と戦利品総数。 試行回数は
        /// <c>ShopManager.RobberyAttempts</c>。 <b>強盗は 1 ラン 1 回まで</b>なので
        /// 試行数 = 実行したラン数。 static はドメインリロードで消えるのでバッチ開始時に 0 へ戻すこと。</summary>
        public static long RobberyWins, RobberyLosses, RobberyLootTotal;
        /// <summary>強盗の戦利品のうち **実際に所持へ入った数**。 RobberyLootTotal は試行回数。</summary>
        public static long RobberyLootApplied;

        /// <summary>[計装 2026-09-13] 強奪トラックの確率ドロップ発生数 (バッチ内累積)。</summary>
        public static long PlunderDrops;
        /// <summary>[計装] 〈剛胆〉のエリート追加ドロップ数。</summary>
        public static long ValorEliteDrops;
        /// <summary>[計装] エリート戦に入った回数 (格上げが効いているかを見る)。</summary>
        public static long ValorEliteFights;

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
            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
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
                ? partners[GameLoop.GameRng.RangeAuto("GameManager.8", 0, partners.Count)].internalName
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
                // 2026-06-23c: lambdaProtectedItemIds は撤廃 (Triage 公平判定で参照不要に)。 gross のみ計測継続。
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
        /// <summary>T4-A〈破綻〉: 有利マス (休憩/秘宝/交換) を確率で空白化する。
        /// 空白化したら固有イベント〈国破れて山河あり〉を起こし、 true を返す。
        ///
        /// **マップ生成ではなく踏んだ瞬間に判定する。** 盤上は普通の有利マスに見えるので、
        /// 「そこへ向かう」判断は必ず先に済んでおり、 空だと分かるのは着いてから。
        /// 迂回で避けられない代わりに、 有利マスの期待値そのものが目減りする。
        ///
        /// イベント側は効果も選択肢も持たない ── 罰は**この関数が一律で取る希望 −3** に
        /// 一本化する。 文面ごとに効果を散らすと、 どの文面を引いたかで罰が変わり、
        /// 空白化の確率だけを較正できなくなる。</summary>
        private bool TryVoidAdvantageTile(TileType type)
        {
            if (type != TileType.Rest && type != TileType.Treasure && type != TileType.Exchange)
                return false;
            float p = MetaProgression.MetaDebuffApplicator.GetVoidTileChance();
            if (p <= 0f) return false;
            MetaProgression.MetaDebuffApplicator.VoidTileChecks++;
            if (GameRng.Value("challenge.hatan") >= p) return false;
            MetaProgression.MetaDebuffApplicator.VoidTileTriggers++;

            Log($"[破綻] {TileToJapanese(type)} は空白だった");
            // **希望 −3 を必ず取る (2026-08-17)。**
            //   空白化だけでは 4pt を払って p=0.103 の死に段だった (実測 −2.0pt)。
            //   有利マスを 1 つ失う痛みは「そこへ向かった手番が無駄になった」だけで、
            //   盤面には何も残らない ── 損失が観測されないので判断も変わらない。
            //   希望を削れば以後の振り直し・進路選択に効き続けるので、罰が持続する。
            //   **空白化の 4 種すべてに一律で乗せる**。 文面ごとに差を付けると、
            //   イベント側に効果を持たせないという設計 (下の doc 参照) が崩れる。
            HopeSystem.ApplyEvilChoice(Run, VoidTileHopeCost);
            var ee = EventSystem.EventEncounter.Instance;
            // 文面の抽選は**発動判定と別の乱数列**から引く。 同じ列を続けて消費すると、
            //   文面を増減しただけで発動の並びまで変わり、 シード固定のペア比較が壊れる。
            string pickName = VoidTileEventNames[
                GameRng.RangeAuto("challenge.hatan.flavor", 0, VoidTileEventNames.Length)];
            var def = EventSystem.EventDatabase.GetByName(pickName);
            // **イベントを出せなくても空白化は成立させる。** ここで有利マスの処理へ
            //   落とすと、 シングルトン未配置やテキスト欠落が「デバフが効かない」という
            //   形で現れ、 数値だけ見ても原因に辿り着けない。
            if (ee == null || def == null || !ee.BeginWith(Run, def))
            {
                Debug.LogWarning($"[破綻] 固有イベント『{pickName}』を開始できず — 空白のみ適用");
                SetPhase(GamePhase.MapNavigation);
                return true;
            }
            eventChoiceResolved = false;
            returnToMapAfterEventCombat = false;
            SetPhase(GamePhase.EventEncounter);
            return true;
        }

        /// <summary>〈破綻〉の空白化で失う希望。 4 種の文面すべてに一律で掛かる。</summary>
        private const int VoidTileHopeCost = 3;

        /// <summary>〈破綻〉の空白化で呼ぶ召喚専用イベントの名前 (event_list.txt と一致させる)。
        ///
        /// **1 ラン に何度も出る**ので 1 種類では飽きる。 4 種を用意し、 空白の理由を
        /// 毎回変えてある (自然に還った / 先客に取られた / 地図の方が古い)。
        /// 同じ絵の描き直しにすると、 種類を増やしても繰り返し感は減らない。</summary>
        private static readonly string[] VoidTileEventNames =
        {
            "国破れて山河あり",
            "兵どもが夢の跡",
            "先客",
            "間違いのない地図",
        };

        /// <summary>〈さびれた観測所〉のイベント名。 EventDatabase の定義名と一致させる。
        /// 遭遇回数 (メタ・ラン跨ぎ) で初対面と再訪を切り替える。</summary>
        public const string ObservatoryEventName = "さびれた観測所";
        public const string ObservatoryRevisitEventName = "さびれた観測所・再訪";
        /// <summary>写しを持ち帰った次の再訪 **1 回だけ** 使われる版。</summary>
        public const string ObservatoryRevisitCopyEventName = "さびれた観測所・再訪・写し";
        /// <summary>一度出たランで再出現させないためのフラグ。</summary>
        public const string ObservatorySeenFlag = "観測所";
        /// <summary>イベントマス 1 回あたりの出現確率。 1 ラン 約13 マスで通算 1/38 になる値。
        /// **ここを触ると出現頻度が直に変わる。** 30〜50 ラン に 1 度が設計意図。</summary>
        public const float ObservatoryChance = 0.002f;

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

            // 6層で最初に踏んだイベントマスは〈真理〉へ至る専用イベントにする。
            // BeginWith は EventDatabase.Pick を通らないため乱数列を消費しない。
            EventSystem.EventDefinition forced = null;
            if (Run != null && Run.currentFloor == 6
                && ConvictionSystem.HasResolveOrBetter(Run)
                && !ConvictionSystem.HasTruth(Run))
            {
                forced = EventSystem.EventDatabase.GetByName(ConvictionSystem.Layer7RevelationEventName);
            }

            // 〈さびれた観測所〉: 全イベント中これだけ超低確率。 30〜50 ラン に 1 度。
            //
            //   **プールの重みではなく専用の事前ロールで出す。** 重み方式だと
            //   出現率がプールの総数に依存し、イベントを足すたびに静かにずれる
            //   (Tier のパーセンタイル枠が「相対配分は品が増えると定義上押し出される」
            //    という理由で §24 で廃止されたのと同じ穴)。 絶対確率で持つ。
            //
            //   1 ラン のイベントマスは約 13 回。 1-(1-p)^13 = 1/38 となる p を置く。
            //   一度出たランでは二度と出ない (ownedFlags で自己抑止)。
            if (forced == null && Run != null
                && !Run.ownedFlags.Contains(ObservatorySeenFlag)
                && GameRng.Chance(ObservatoryChance, "event.observatory"))
            {
                bool revisit = ObservatoryState.IsRevisit;
                bool withCopy = revisit && ObservatoryState.CopyHeld;
                string want = !revisit ? ObservatoryEventName
                            : withCopy ? ObservatoryRevisitCopyEventName
                                       : ObservatoryRevisitEventName;
                forced = EventSystem.EventDatabase.GetByName(want);
                // 定義欠けはソフトロックの元なので順に落とす
                if (forced == null && withCopy)
                    forced = EventSystem.EventDatabase.GetByName(ObservatoryRevisitEventName);
                if (forced == null && revisit)
                    forced = EventSystem.EventDatabase.GetByName(ObservatoryEventName);
                if (forced != null)
                {
                    Run.ownedFlags.Add(ObservatorySeenFlag);
                    ObservatoryState.NoteMet();
                    if (withCopy)
                    {
                        // **選択肢ではなく上乗せ。** ラン跨ぎで持ち越した見返りなので、
                        //   どの択を選んでも入る (4つ目の択にすると通常択と排他になり、
                        //   パッシブが逆に 1 個減るという逆転が起きていた)。
                        EventSystem.EventEffectExecutor.GrantObservatoryCopyBonus(Run);
                        ObservatoryState.ConsumeCopy();            // 次の再訪 1 回だけ
                    }
                }
            }

            bool ok = forced != null ? ee.BeginWith(Run, forced) : ee.Begin(Run);
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

            // 専用イベントは選択を確定した時点で7層資格を立てる。
            // 選択効果とは分離し、イベント本文を調整しても進行フラグが壊れないようにする。
            if (index == 0
                && ee.Current.name == ConvictionSystem.Layer7RevelationEventName
                && ConvictionSystem.RevealTruthInLayer6(Run))
            {
                MetaProgression.Achievements.AchievementService.NoteFinalPageOpened();
                Log("裂け目の記録を読み終えた。〈決意〉は〈真理〉へ変わった");
            }

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

            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
        }

        /// <summary>ヴェスカ撃破後の一択を確定してランを閉じる。
        ///
        /// <para><paramref name="leaveOpen"/> = false → <b>裂け目を閉じる</b> (TRUE END〈帳尻〉)。
        /// 自分と遺物の山とヴェスカを対価に帳尻を合わせる。 遺物経済は破綻し、
        /// 主人公は行方不明になる。 数年後に七層の調査で見つかる手記から、
        /// この盤上遊戯の原作『大穴』が書かれる ── <b>額縁が閉じるのはこちら側だけ</b>。</para>
        ///
        /// <para><paramref name="leaveOpen"/> = true → <b>閉じない</b> (GOOD END〈見送り〉)。
        /// 世界は残るが本は書かれない。 額縁に接続しないことがこのエンドの代償にあたる。</para>
        ///
        /// <para>正本: <see cref="Endings"/> / docs/GAME.md §20。</para></summary>
        public void ChooseRiftFate(bool leaveOpen)
        {
            if (CurrentPhase != GamePhase.RiftChoice) return;
            Run.riftLeftOpen = leaveOpen;
            MetaProgression.Achievements.AchievementService.CompleteRun(Run);
            Run.EndRun();
            SetPhase(GamePhase.RunClear);
            OnRunCleared?.Invoke(Run);
            Log(leaveOpen ? "=== 完全クリア === 裂け目を閉じなかった"
                          : "=== 完全クリア === 裂け目を閉じた");
            var endingFull = Endings.Resolve(Run);
            Log(endingFull.Display);
            VignetteUnlockState.OnRunClear(Run, endingFull.id, VignetteUnlockState.IsMaxDifficultyRun(Run));
        }

        /// <summary>門の 3 工程をすべて決めて MapNavigation へ戻る。
        /// <b>門マスは 7 層の終端</b>なので、 戻った先で
        /// <see cref="IsBosslessFloorCleared"/> が成立し、 そのまま 8 層へ転移する。</summary>
        public void CompleteGateRitual()
        {
            if (CurrentPhase != GamePhase.GateRitual) return;
            GateHopeAfter += Run.hope;    // [計装] 支払い後。 45 (悲観・上限恒久ロック) を割っていないか
            Log(Run.gateFlaws == GateFlaw.None
                ? "門は完全に起動した。"
                : $"門が起動した ── 不完全なまま: {Run.gateFlaws}");
            SetPhase(GamePhase.MapNavigation);
        }

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
            if (Run.IsNormalClear && Run.currentFloor == Run.normalClearFloor)
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
                    InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, SwordDanceSet.Finale);
                    Log("[計測] 5層撃破 → 〈ブレイドダンス〉を強制付与");
                }

                if (!ConvictionSystem.HasResolveOrBetter(Run))
                {
                    MetaProgression.Achievements.AchievementService.CompleteRun(Run);
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
                if (Run.currentFloor == 6 && !ConvictionSystem.HasTruth(Run))
                {
                    MetaProgression.Achievements.AchievementService.CompleteRun(Run);
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

        /// <summary>武器の会心率 + 層補正の上限 (%)。</summary>
        public const float MaxWeaponCritPct = 50f;

        /// <summary>装備中の武器・ダイスからステータスを取得</summary>
        private (int diceCount, int diceMax, float critRate, int[] diceFaces,
                 DiceFaceParts.Tier[] faceTiers, bool suppressTerminalRoles)
            GatherPlayerCombatStats()
        {
            // ItemEquipHandler を探す（初回のみ）
            if (equipHandler == null)
                equipHandler = FindObjectOfType<ItemEquipHandler>();

            int diceCount = 2;
            int diceMax = 6;
            // 会心率は % で持つ (2026-09-19 統一・5% 刻み)。 武器なしの素値は 5%。
            float critRatePct = 5f;
            int[] diceFaces = null;
            // 出目パーツ: 面添字ごとの Tier。 diceFaces と 1 対 1 で対応する (DiceFaceParts.Build)。
            DiceFaceParts.Tier[] faceTiers = null;
            // ADR-0010〈無銘の賽〉: 端子役を成立させないダイスか。 面と同じ経路で解決する。
            bool suppressTerminalRoles = false;

            bool weaponResolved = false;

            // 武器はダイスの「個数」と会心のみ定義する（面/最大値は装備ダイスが供給）。
            if (equipHandler != null)
            {
                var weapon = equipHandler.GetCurrentEquipment(ItemCategory.Weapon);
                if (weapon != null && weapon.hasWeaponStats)
                {
                    diceCount = weapon.weaponDice.count;
                    critRatePct = weapon.critRatePct;
                    weaponResolved = true;
                }

            }

            // ItemEquipHandler が無い/未装備なら RunState の自動装備IDから解決
            if (!weaponResolved && Run != null && !string.IsNullOrEmpty(Run.equippedWeaponId))
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
            DiceFaceParts.Build(DiceFaceParts.BaseFaces,
                                Run != null ? Run.diceFaceParts : null,
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

        /// <summary>Ultra worker 用: 復元済みの状態から指定フェーズへ入り直す。
        /// **通常のゲーム進行では呼ばれない。**
        ///
        /// <para><see cref="StartNewRun"/> で正規に初期化した直後に、
        /// <c>Run</c> の中身とマップを checkpoint で上書きしてから呼ぶ想定。
        /// <see cref="SetPhase"/> をそのまま通すので、 ボスなし層のクリア判定など
        /// フェーズ遷移に紐づく規則も**通常と同じように効く** ── ここだけ別経路にすると、
        /// worker の中でだけ規則が違うという最悪の形になる。</para></summary>
        public void ResumeAtPhase(GamePhase phase)
        {
            SetPhase(phase);
        }

        private void SetPhase(GamePhase newPhase)
        {
            var prev = CurrentPhase;
            CurrentPhase = newPhase;
            OnPhaseChanged?.Invoke(newPhase);

            if (logPhaseChanges)
                Debug.Log($"[GameManager] Phase: {prev} → {newPhase}");

            // ボスなし層 (2/4) は「前へ進める先が無くなった時点」が層クリア。
            // 2026-07-29: 抜け口ごとに差し込む方式は漏れがあり、 移動先なしで
            // デッドロックしていた (10000 ラン中 7078 件)。 MapNavigation へ入る瞬間に
            // 一元判定へ変更 ── 呼び出し元がどこであっても取りこぼさない。
            if (newPhase == GamePhase.MapNavigation && !_resolvingBosslessClear && IsBosslessFloorCleared())
            {
                _resolvingBosslessClear = true;
                try
                {
                    Log($"{Run.currentFloor}層 踏破 — この層にボスはいない");
                    HandleFloorClear();
                }
                finally { _resolvingBosslessClear = false; }
            }
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
                case TileType.Gate:        return "門";
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

            // 〈門〉の 3 工程 (1=血 / 2=遺物 / 3=転移、 Space=残りを拒んで確定)
            if (CurrentPhase == GamePhase.GateRitual)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) && !gateBloodResolved)
                {
                    OfferGateBlood(true);
                    gateBloodResolved = true;
                }
                if (Input.GetKeyDown(KeyCode.Alpha2) && !gateRelicResolved)
                {
                    OfferGateRelics(true);
                    gateRelicResolved = true;
                }
                if (Input.GetKeyDown(KeyCode.Alpha3) && !gateTransferResolved)
                {
                    OfferGateTransfer(true);
                    gateTransferResolved = true;
                }

                if (Input.GetKeyDown(KeyCode.Space))
                {
                    // 未解決の工程は拒んだ扱いで〈不完全な〜〉を確定
                    if (!gateBloodResolved)    OfferGateBlood(false);
                    if (!gateRelicResolved)    OfferGateRelics(false);
                    if (!gateTransferResolved) OfferGateTransfer(false);
                    CompleteGateRitual();
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

            // ヴェスカ撃破後の一択。 UI が付くまでのキー入力口
            //   (Space での「次へ」に流されないよう、 上の分岐には入れない)。
            if (CurrentPhase == GamePhase.RiftChoice)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) ChooseRiftFate(false); // 閉じる → TRUE END
                if (Input.GetKeyDown(KeyCode.Alpha2)) ChooseRiftFate(true);  // 閉じない → GOOD END
            }

            if (Input.GetKeyDown(KeyCode.F) && CurrentPhase == GamePhase.Combat)
            {
                if (CombatManager.Instance.IsCombatActive)
                    CombatManager.Instance.ExecuteFullCombat();
            }
        }
    }
}

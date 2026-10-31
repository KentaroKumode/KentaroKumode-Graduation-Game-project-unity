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
    /// <summary>
    /// ゲームのメインループを制御するシングルトン。
    /// マップベース進行: 3レーン×10行のマップを探索し、タイルイベントを処理する。
    /// ビジュアル/UIは別コンポーネントがイベントを購読して実装する想定。
    /// </summary>
    public partial class GameManager : MonoBehaviour
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
        /// <summary>ヴェスカ連戦のチェーン swap で次フォームへ切り替わったことを通知する。
        /// 戦闘自体は継続中だが、計測上は新エネミーとの戦闘扱いにしたい AutoRunner 等が拾う。</summary>
        public void RaiseEnemyEncountered(EnemyData e) => OnEnemyEncountered?.Invoke(e);
        public event Action<CombatResult> OnBattleEnded;
        public event Action<int> OnRewardGranted;
        public event Action<int> OnFloorAdvanced;
        /// <summary>5 層ボスを〈決意〉付きで倒し、 6 層へ降りる直前に 1 回だけ鳴る (2026-10-28)。
        /// 大穴の時空のずれが「記録が残らない」域に入る節目として、 GUI 1 枚を出す想定のフック。
        /// 発火の直後に FloorClear へ進む (待たない)。 UI 側で止めたいときは FloorClear の確定 (ConfirmFloorClear) を遅らせる。</summary>
        public event Action<RunState> OnDeepDescent;
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
        private EnemyData firstEliteEnemy;

        // === GateRitual 状態 (門の 3 工程それぞれが既に決まったか) ===
        private bool gateBloodResolved;
        private bool gateRelicResolved;
        private bool gateTransferResolved;

        // === EventEncounter: 選択肢確定後・フレーバー表示中フラグ ===
        private bool eventChoiceResolved;
        /// <summary>イベントの選択肢を選び終え、 結果の文を読んでいるところか (画面が「次へ」を出すのに読む)。</summary>
        public bool EventChoiceResolved => eventChoiceResolved;
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
            ItemSystem.PassiveSkills.PassiveSkillRegistry.ResetAllRunState();

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
            ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, starterWeapon);
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
                    var cands = new System.Collections.Generic.List<string>(offers);
                    for (int o = 0; o < offers; o++)
                    {
                        string cand = PickStartingPassive();
                        if (!string.IsNullOrEmpty(cand)) cands.Add(cand);
                    }
                    string id = null;
                    if (cands.Count > 0)
                    {
                        // 選ぶのはプレイヤー。 選択画面が付くまでは先頭 (BOT は GameHooks で差し込む)。
                        int pick = GameHooks.ChooseStartingPassive?.Invoke(cands) ?? 0;
                        id = cands[System.Math.Max(0, System.Math.Min(pick, cands.Count - 1))];
                    }
                    if (string.IsNullOrEmpty(id)) break;   // プール枯渇
                    if (Run.ownedPassiveItems == null)
                        Run.ownedPassiveItems = new System.Collections.Generic.List<string>();
                    ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
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
                ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
                Log($"メタ報酬: 種火 [{kw}] 開幕パッシブ「{id}」を獲得");
            }

            LastCombatResult = null;
            CurrentEnemy = null;
            IsEliteSecondFight = false;
            inFalseMerchantCombat = false;
            _inPursuerFight = false;
            _pursuerFightCount = 0;
            _inFamiliarFight = false;
            _familiarFightCount = 0;

            // 航行マップの消耗品: 囮罠 (2026-09-28・2026-10-18 に囮と罠を統合)
            GrantStartingMapTools();

            SetPhase(GamePhase.RunStart);
            MetaProgression.Achievements.AchievementService.BeginRun(Run);
            OnRunStarted?.Invoke(Run);
            Log($"=== ラン開始 === HP:{Run.playerMaxHP}");

            EnterFloor();
        }

        /// <summary>マップ上のノードへ移動。
        ///
        /// <para><b>航行マップの層</b>（手番制・2026-10-18）では道でつながった隣の点へ 1 歩動く
        /// （<see cref="MoveStep"/>）。 動くと 1 手番が進み、 追跡者が 1 度動く。</para>
        ///
        /// <para>8 層は離散のまま、 1 手で隣へ動く。</para></summary>
        public void MoveToNode(string nodeId)
        {
            Debug.Log($"[GameManager] MoveToNode: {nodeId} (phase={CurrentPhase})");
            if (CurrentPhase != GamePhase.MapNavigation) return;

            var mm = MapManager.Instance;
            if (mm != null && mm.IsFreeMap)
            {
                MoveStep(nodeId);
                return;
            }

            BeforeNodeEntered();

            mm.MoveTo(nodeId, Run.playerMaxHP);

            AfterNodeEntered();
        }

        /// <summary>点に入る前のフック (離散の 1 手 / 自由移動の到着で共通)。</summary>
        private void BeforeNodeEntered()
        {
            // マップ移動時系時限効果（翼の恩寵・警戒心・導きの光等）
            EventSystem.TimedEffects.TimedEffectManager.OnMapMove(Run);

            // 名前付き固有パッシブ（マップ移動時系。現状該当なし、フック確保）
            ItemSystem.PassiveItems.PassiveItemManager.OnMapMove(Run);

            // 移動で貯まる品が次の戦闘の開始時に読む歩数 (2026-10-31)
            if (Run != null) Run.stepsSinceBattle++;

            // メタ: ノード踏破トークン
            MetaProgression.MetaTokenEarner.OnNodeVisited();
        }

        /// <summary>点に入った後の処理 (離散の 1 手 / 自由移動の到着で共通): 戦闘名の開示・
        /// 〈焦燥〉・絶望的な進軍 → タイル起動。</summary>
        private void AfterNodeEntered()
        {
            var mm = MapManager.Instance;

            // 戦闘名の開示 (2026-08-28)。 **到着した時点で、隣接ノードぶんだけ**開く。
            //   遠くからは見えないので「危険なプリセットを何手も前から迂回する」ことはできず、
            //   判断は必ず「いまの手番で、この分岐をどうするか」に閉じる。
            RevealAdjacentEncounters();

            bool despairMarch = MetaProgression.MetaDebuffApplicator.IsDespairMarchActive();

            // 挑戦デバフ 軸14〈焦燥〉: この層の踏破が閾値を超えたら、 1 マスごとに物資 −5。
            //   「長く彷徨うほど追い詰められる」形。 閾値以内なら一切効かないので、
            //   最短で降りるプレイには無害 ── 寄り道の対価としてだけ働く。
            Run.tilesThisFloor++;
            int impatienceMax = MetaProgression.MetaDebuffApplicator.GetImpatienceTileThreshold();
            if (impatienceMax > 0 && Run.tilesThisFloor > impatienceMax)
            {
                ProvisionSystem.Reduce(Run, MetaProgression.MetaDebuffApplicator.ImpatienceProvisionLoss);
                Log($"焦燥: {Run.currentFloor}層 {Run.tilesThisFloor}マス目 (閾値{impatienceMax}) → 物資 -{MetaProgression.MetaDebuffApplicator.ImpatienceProvisionLoss}");
            }

            if (ProvisionSystem.ApplyArrival(Run, despairMarch))
            {
                Log("燃え尽き: 佯狂者の冠で最大HPが尽きてラン終了");
                Run.EndRun();
                SetPhase(GamePhase.GameOver);
                OnGameOver?.Invoke(Run);
                return;
            }

            ActivateTile(mm.CurrentNode);
        }

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

        /// <summary>戦闘の入力を画面 (2D の戦闘) が受けている間 true。 Space / F でターンを自動で進めるキーを止める
        /// ── 画面の「人が操作するターン」の途中で <c>ExecuteTurn</c> が割り込まないように。</summary>
        public static bool CombatInputByScreen;

        /// <summary>画面が Space を使ったフレーム。 同じ押下が、 画面の操作で切り替わった次のフェーズ (報酬の確定など) まで流れないように。</summary>
        public static int ScreenUsedSpaceFrame = -1;

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

            // マップナビゲーション: 数字キーで移動先選択 (航行マップは Space 待つ (W/A/S/D は視点の移動なので使わない) / R 偵察 / F 照明 / L 囮罠)
            if (CurrentPhase == GamePhase.MapNavigation)
            {
                if (FreeMap != null)
                {
                    if (Input.GetKeyDown(KeyCode.Space)) { WaitTurn(); return; }
                    if (Input.GetKeyDown(KeyCode.R)) { ReconTurn(); return; }
                    if (Input.GetKeyDown(KeyCode.F)) { UseFlare(); return; }
                    if (Input.GetKeyDown(KeyCode.L)) { Consumables.Use(Run, ItemIds.Lure); return; }
                }
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
                var dialog = ItemSystem.Shop.Visual.ShopPurchaseDialog.Instance;
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

            if (Input.GetKeyDown(KeyCode.Space) && ScreenUsedSpaceFrame != Time.frameCount)
            {
                switch (CurrentPhase)
                {
                    case GamePhase.Combat:
                        if (!CombatInputByScreen && CombatManager.Instance.IsCombatActive)
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

            if (Input.GetKeyDown(KeyCode.F) && CurrentPhase == GamePhase.Combat && !CombatInputByScreen)
            {
                if (CombatManager.Instance.IsCombatActive)
                    CombatManager.Instance.ExecuteFullCombat();
            }
        }
    }
}

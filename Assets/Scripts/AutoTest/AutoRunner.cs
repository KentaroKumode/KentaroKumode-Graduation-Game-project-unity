using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using GameLoop;
using CombatSystem;
using MapSystem;
using EventSystem;
using InventorySystem;
using InventorySystem.Shop;

namespace AutoTest
{
    /// <summary>
    /// GameManager の公開APIだけを使い、キー入力なしで1ランを最後まで自動進行させる
    /// ヘッドレス・ドライバ。N回連続実行し、阻害要因を集計したログファイルを生成する。
    ///
    /// 行動方針: 前進貪欲 + 生存重視
    ///   - マップは常にボス方向(forward)へ進む。HP低下時は休憩/宝箱/ショップを優先。
    ///   - 戦闘は ExecuteFullCombat で即決。儀式は資源温存のため全拒否。
    ///
    /// ゲーム側コードは一切改変せず、Debug.Log("[GameManager] ...") を購読して
    /// 進行ナラティブを取得する。
    /// </summary>
    public class AutoRunner : MonoBehaviour
    {
        [Header("バッチ設定")]
        public int runCount = 10;
        public bool autoStart = false;
        [Tooltip("バッチ中の Time.timeScale（演出を早送り）")]
        public float batchTimeScale = 20f;
        [Tooltip("1ランあたりの最大ループ反復数。超過でDEADLOCK判定")]
        public int maxIterationsPerRun = 4000;
        [Tooltip("同一フェーズが進展なく続いた反復数の上限。超過でDEADLOCK判定")]
        public int stallLimit = 400;
        [Tooltip("詳細ログ(全ランのナラティブ)を書き出す")]
        public bool writeDetailLog = true;
        [Tooltip("詳細ログの1ランあたり最大行数。超過時は古い行を捨て末尾(決定的な終端)を必ず保持")]
        public int detailMaxLinesPerRun = 5000;
        /// <summary>メタ恒久進行の扱い方。</summary>
        public enum MetaPattern
        {
            /// <summary>臆病パターン: メタ進行を全リセット。パッシブボーナス0でバランス計測。</summary>
            Cowardly,
            /// <summary>全有効化パターン: トラック全段解放。メタバフ込みの上限プレイ計測。</summary>
            FullProgression,
            /// <summary>保存済み状態のまま手を付けない（実プレイヤーの進行データを使う）。</summary>
            Untouched,
        }

        [Tooltip("メタ恒久進行モード: Cowardly=全リセット / FullProgression=全段解放 / Untouched=保存値そのまま")]
        public MetaPattern metaPattern = MetaPattern.Cowardly;

        /// <summary>メタデバフ(挑戦モード)を全ON にするか。 最高難易度プレイ計測用。</summary>
        [Tooltip("メタデバフ Lv1-10 を全ON にする (最高難易度モード)")]
        public bool enableAllDebuffs = false;

        // 旧フィールド (互換用、内部では metaPattern を見る)
        [System.Obsolete("metaPattern を使用してください")] public bool resetMetaForCleanBaseline = true;

        [Header("実行後")]
        [Tooltip("バッチ完了後にPlayModeを抜ける(Editorメニュー起動時)")]
        public bool exitPlayModeWhenDone = false;

        // ===== 集計分類 =====
        public enum Outcome { GameOver, NormalClear, FullClear, Deadlock, Crash }
        public enum DeathCause { None, CombatLoss, CombatPyrrhic, Starvation, KarmaSettlement, Unknown }

        [Serializable]
        public class CombatRec
        {
            public string enemy;
            public string enemyId;
            public int floor;
            public bool isBoss;
            public bool won;
            public int turns;
            public int hpBefore;
            public int hpAfter;
            public bool afterLastStand;
            // ターン内訳（非解決グラインドの原因特定用）
            public int tWin;       // ロール勝利ターン数
            public int tDraw;      // 引き分けターン数
            public int tLoss;      // ロール敗北ターン数
            public int tLossAbs;   // うちメインダメ0（シールド吸収/無効化で死を回避）
        }

        [Serializable]
        public class RunRec
        {
            public int index;
            public Outcome outcome;
            public DeathCause cause = DeathCause.None;
            public int deathFloor;
            public bool deathInBossFight;
            public string fatalEnemy = "";
            public int reachedFloor = 1;
            public bool reached6F;
            public int finalHP;
            public int finalMaxHP;
            public int finalCoins;
            public int peakCoins;
            public int totalGoldGained;
            public int starvationTotal;
            public int starvationHits;
            public int totalCombats;
            public int totalWins;
            public int shopPurchases;
            public int totalTurns;
            public bool lastStandUsed;
            public int lastStandFloor;
            public int combatsAfterLastStand;
            public int winsAfterLastStand;
            public string profile = "";    // 行動ルーチン: "貪欲"(戦闘貪欲) / "回避"(戦闘回避)
            public string band = "";       // R1..R10 / CRASH / DEADLOCK
            public string bandLabel = "";

            // 5F突入時点の確信チェーン状態スナップショット (-1=未到達)
            public int convictionStageAt5F = -1;
            public bool hadConvictionItem5F;       // 〈根拠のない確信〉所持
            public bool hadResolveAt5F;            // 〈決意〉所持
            public bool hadTruthAt5F;              // 〈真理〉所持
            public bool hadFlagYogenAt5F;          // 苦難の予言 所持
            public bool hadFlagKakushinAt5F;       // 苦難の確信 所持
            // 実際に起動したタイル種別ごとの回数（再訪・消化済みは含まない＝ActivateTile 初回のみ）
            public readonly Dictionary<TileType, int> tileVisits = new Dictionary<TileType, int>();
            public string note = "";
            public List<CombatRec> combats = new List<CombatRec>();
            /// <summary>6F (灰燼の王) 撃破時点のビルド情報スナップショット。
            /// 後から「どんな装備で6Fまで到達できたか」をサルベージするための行単位プレーンテキスト。
            /// null = 6F未到達(または到達前にラン終了)。</summary>
            public string clear6FSnapshot;

            /// <summary>解脱: 覚者・妙覚のサドンデス勝利で完全クリアした場合 true。</summary>
            public bool gedatsuVictory;
        }

        private readonly List<RunRec> _records = new List<RunRec>();
        private RunRec _cur;
        private readonly List<string> _curLog = new List<string>();
        private bool _exceptionFlag;
        private string _exceptionMsg;
        private int _prevCoins;
        private object _lastResolvedEvent;
        private string _pendingEnemyName;
        private string _pendingEnemyId;
        private bool _pendingEnemyIsBoss;
        private int _pendingEnemyHpBefore;
        private int _eventStuckCount;
        private string _lastEventInfo = "";
        // 行動ルーチン分割: 前半50%=戦闘貪欲 / 後半50%=戦闘回避（航行Rankのみ差し替え）
        private bool _curCombatAverse;
        private bool _curBossNear;   // 直近のDoNavigateで判定したボス接近フラグ（休憩判断で参照）
        // 現戦闘のターン内訳タリー（OnEnemyEncounteredでリセット、ExecuteTurnで加算）
        private int _cwWin, _cwDraw, _cwLoss, _cwLossAbs;

        private static readonly string[] LeaveKeywords =
            { "立ち去", "去る", "無視", "帰", "やめ", "見送", "通り過ぎ", "何もしない", "断る", "拒" };

        // 危険を示唆する選択肢（生存重視のため可能なら回避）
        private static readonly string[] DangerKeywords =
            { "戦", "挑", "賭", "呪", "捧", "食らう", "奪わ", "盗", "襲", "犠牲", "毒", "燃" };

        void Start()
        {
            if (autoStart) Begin();
        }

        public void Begin()
        {
            StartCoroutine(RunBatch());
        }

        private IEnumerator RunBatch()
        {
            Application.logMessageReceived += OnLog;
            float prevScale = Time.timeScale;
            Time.timeScale = Mathf.Max(1f, batchTimeScale);

            // DB / 演出の事前準備
            SafeInitDatabases();
            DisableMapTransition();

            // GameManager 出現待ち
            float waited = 0f;
            while (GameManager.Instance == null && waited < 10f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (GameManager.Instance == null)
            {
                Debug.LogError("[AutoRunner] GameManager が見つかりません。SampleScene で実行してください。");
                Application.logMessageReceived -= OnLog;
                Time.timeScale = prevScale;
                yield break;
            }

            var gm = GameManager.Instance;
            gm.OnEnemyEncountered += OnEnemyEncountered;
            gm.OnBattleEnded += OnBattleEnded;
            gm.OnStarvationDamage += OnStarvation;
            gm.OnTileActivated += OnTileActivated;

            Debug.Log($"[AutoRunner] バッチ開始: {runCount} ラン");

            for (int i = 0; i < runCount; i++)
            {
                yield return RunOne(i);
                yield return null; // フレーム譲り
            }

            gm.OnEnemyEncountered -= OnEnemyEncountered;
            gm.OnBattleEnded -= OnBattleEnded;
            gm.OnStarvationDamage -= OnStarvation;
            gm.OnTileActivated -= OnTileActivated;
            Application.logMessageReceived -= OnLog;
            Time.timeScale = prevScale;

            string dir = WriteLogs();
            Debug.Log($"[AutoRunner] バッチ完了。ログ出力先:\n{dir}");

            if (exitPlayModeWhenDone)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }

        private IEnumerator RunOne(int index)
        {
            var gm = GameManager.Instance;
            // 前半50%=戦闘貪欲 / 後半50%=戦闘回避
            _curCombatAverse = index >= runCount / 2;
            _cur = new RunRec { index = index, profile = _curCombatAverse ? "回避" : "貪欲" };
            _curLog.Clear();
            _exceptionFlag = false;
            _exceptionMsg = null;
            _lastResolvedEvent = null;
            _pendingEnemyName = null;
            _eventStuckCount = 0;
            _lastEventInfo = "";

            // タイトルへ戻す（前ランが RunClear/GameOver 停止のままなら）
            int guard = 0;
            while (gm.CurrentPhase != GameManager.GamePhase.Title && guard++ < 50)
            {
                if (gm.CurrentPhase == GameManager.GamePhase.GameOver ||
                    gm.CurrentPhase == GameManager.GamePhase.RunClear)
                    gm.ReturnToTitle();
                yield return null;
            }

            // メタ恒久進行の前処理 (パターン別)
            try
            {
                switch (metaPattern)
                {
                    case MetaPattern.Cowardly:
                        MetaProgression.MetaProgressManager.Instance?.ResetAll();
                        break;
                    case MetaPattern.FullProgression:
                        MetaProgression.MetaProgressManager.Instance?.MaxAllForTesting();
                        break;
                    case MetaPattern.Untouched:
                        // 何もしない
                        break;
                }

                // メタデバフトグル (最高難易度モード)
                var mgr = MetaProgression.MetaProgressManager.Instance;
                if (mgr?.State != null)
                {
                    for (int lv = 1; lv <= 10; lv++)
                        mgr.ToggleDebuff((MetaProgression.MetaDebuffLevel)lv, enableAllDebuffs);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[AutoRunner] Meta pattern={metaPattern}: {e.Message}"); }

            gm.StartNewRun();
            _prevCoins = gm.Run != null ? gm.Run.coins : 0;

            int iter = 0;
            int stall = 0;
            var lastPhase = gm.CurrentPhase;
            string lastNode = CurrentNodeId();
            int lastFloor = gm.Run?.currentFloor ?? 1;
            int lastHp = gm.Run?.playerHP ?? 0;

            while (true)
            {
                if (_exceptionFlag)
                {
                    Finish(Outcome.Crash, $"例外: {_exceptionMsg}");
                    yield break;
                }
                if (iter++ > maxIterationsPerRun)
                {
                    Finish(Outcome.Deadlock, $"反復上限超過 phase={gm.CurrentPhase} node={CurrentNodeId()}");
                    yield break;
                }

                var phase = gm.CurrentPhase;

                // 進展のないストール検出
                string node = CurrentNodeId();
                int fl = gm.Run?.currentFloor ?? 0;
                int hp = gm.Run?.playerHP ?? 0;
                if (phase == lastPhase && node == lastNode && fl == lastFloor && hp == lastHp)
                {
                    if (++stall > stallLimit)
                    {
                        string ev = phase == GameManager.GamePhase.EventEncounter && !string.IsNullOrEmpty(_lastEventInfo)
                            ? $" event=[{_lastEventInfo}]" : "";
                        Finish(Outcome.Deadlock, $"ストール phase={phase} node={node} floor={fl}{ev}");
                        yield break;
                    }
                }
                else stall = 0;

                // 5F突入時 (4→5) にチェーン進行状況をスナップショット
                if (fl == 5 && lastFloor != 5 && _cur != null && _cur.convictionStageAt5F < 0 && gm.Run != null)
                {
                    var rs = gm.Run;
                    _cur.convictionStageAt5F = rs.convictionStage;
                    var owned = rs.ownedPassiveItems;
                    _cur.hadConvictionItem5F = owned != null && owned.Contains(GameLoop.ConvictionSystem.IdConviction);
                    _cur.hadResolveAt5F     = owned != null && owned.Contains(GameLoop.ConvictionSystem.IdResolve);
                    _cur.hadTruthAt5F       = owned != null && owned.Contains(GameLoop.ConvictionSystem.IdTruth);
                    var flags = rs.ownedFlags;
                    _cur.hadFlagYogenAt5F    = flags != null && flags.Contains("苦難の予言");
                    _cur.hadFlagKakushinAt5F = flags != null && flags.Contains("苦難の確信");
                }

                lastPhase = phase; lastNode = node; lastFloor = fl; lastHp = hp;

                // ラストスタンド発動検知
                TrackLastStand();
                TrackEconomy();

                bool finished = false;
                try
                {
                    finished = Step(phase);
                }
                catch (Exception e)
                {
                    Finish(Outcome.Crash, $"Step例外 phase={phase}: {e.GetType().Name} {e.Message}");
                    yield break;
                }

                if (finished) yield break;
                yield return null;
            }
        }

        /// <summary>現フェーズに対する1アクション。ラン終了時 true。</summary>
        private bool Step(GameManager.GamePhase phase)
        {
            var gm = GameManager.Instance;
            switch (phase)
            {
                case GameManager.GamePhase.Title:
                case GameManager.GamePhase.RunStart:
                case GameManager.GamePhase.FloorIntro:
                    return false; // GameManager が即座に MapNavigation へ遷移

                case GameManager.GamePhase.MapNavigation:
                    return DoNavigate();

                case GameManager.GamePhase.Combat:
                    RunCombatWithItems(gm);
                    return false;

                case GameManager.GamePhase.BattleResult:
                    // 6Fクリア時のビルド情報を後でサルベージできるよう、撃破直後にスナップショット
                    bool floor6BossWinPending = gm.LastCombatResult.HasValue
                        && gm.LastCombatResult.Value.playerWon
                        && MapManager.Instance?.CurrentNode != null
                        && MapManager.Instance.CurrentNode.type == TileType.Boss
                        && gm.Run != null && gm.Run.currentFloor == 6;
                    // (旧 TryDropRapierOnFloor4Boss は撤去。レイピアは 4F イベント「亡霊との決闘」 で正規配布される)
                    gm.ConfirmBattleResult();
                    if (floor6BossWinPending) Capture6FClearSnapshot(gm);
                    return false;

                case GameManager.GamePhase.Reward:
                    // 戦闘後ドロップ2択: 未所持(ビルドの幅)を優先、同条件なら option a。
                    while (gm.HasPendingRewardChoice)
                    {
                        var (ra, rb) = gm.CurrentRewardChoice;
                        var owned = gm.Run?.ownedPassiveItems;
                        bool ownsA = owned != null && ra != null && owned.Contains(ra);
                        bool ownsB = owned != null && rb != null && owned.Contains(rb);
                        int pick = (ownsA && !ownsB) ? 1 : 0;
                        gm.ResolveRewardChoice(pick);
                    }
                    gm.ConfirmReward();
                    return false;

                case GameManager.GamePhase.RestStop:
                {
                    // 休憩は3択(食事/回復/強化)から1つ。生存(燃料→HP)優先、余裕あれば成長。
                    var run = gm.Run;
                    float hpR = run.playerMaxHP > 0 ? (float)run.playerHP / run.playerMaxHP : 1f;
                    int cost = GameManager.WeaponUpgradeCost(run.weaponUpgradeLevel);
                    var hg = MapManager.Instance?.Hunger;
                    int hCur = hg?.Current ?? 99;
                    int hMax = hg?.Max ?? 10;
                    bool starvingSoon = hCur <= 3;            // 次の道中で空腹切れ濃厚
                    bool greedyBossPrep = !_curCombatAverse && _curBossNear;

                    if (starvingSoon)
                        gm.RestEat();                        // 餓死回避が最優先
                    else if (greedyBossPrep && hpR < 0.8f)
                        gm.RestHeal();                       // 貪欲: ボス前にHPを整える
                    else if (hpR <= 0.55f)
                        gm.RestHeal();                       // 低HPは回復
                    else if (hpR > 0.6f && run.weaponMaterials >= cost)
                        gm.RestUpgrade();                    // 余裕あれば成長
                    else if (hCur < hMax)
                        gm.RestEat();                        // 他に用が無ければ燃料補給
                    else
                        gm.RestHeal();                       // 全部満ちていれば回復で無駄なく
                    return false;
                }

                case GameManager.GamePhase.ShopVisit:
                    DoShop();
                    return false;

                case GameManager.GamePhase.EventEncounter:
                    DoEvent();
                    return false;

                case GameManager.GamePhase.TreasureOpen:
                case GameManager.GamePhase.TrapTriggered:
                    gm.ConfirmTileEvent();
                    return false;

                case GameManager.GamePhase.ExchangeTile:
                    // 交換は最低Tierパッシブ→上位ランダムの厳密アップグレード。所持があれば必ず交換。
                    if (gm.CanExchangeTile) gm.DoExchangeTile();
                    else gm.SkipExchangeTile();
                    return false;

                case GameManager.GamePhase.SinRitual:
                    // 方針: 6層固有デバフ(ゴルゴダの心/断絶した時間/灰燼の烙印)は
                    // 回避できる限り必ず回避する。3儀式すべて支払いを選択（accept=true）。
                    // ゲーム側で支払い可能なときのみ実際に消費され、不能時のみデバフ付与。
                    gm.OfferHpSacrifice(true);
                    gm.OfferGoldSacrifice(true);
                    gm.OfferItemSacrifice(true);
                    gm.CompleteSinRitual();
                    return false;

                case GameManager.GamePhase.FloorClear:
                    gm.ConfirmFloorClear();
                    return false;

                case GameManager.GamePhase.RunClear:
                    FinishClear();
                    return true;

                case GameManager.GamePhase.GameOver:
                    FinishGameOver();
                    return true;

                default:
                    return false;
            }
        }

        // ===== 行動方針: 前進貪欲 + 生存重視 =====

        private bool DoNavigate()
        {
            var gm = GameManager.Instance;
            var mm = MapManager.Instance;
            if (mm == null) { Finish(Outcome.Deadlock, "MapManager null"); return true; }
            _eventStuckCount = 0; // マップに戻った＝イベント解決済み

            var (fwd, lat) = mm.GetCategorizedMoves();
            var pool = (fwd != null && fwd.Count > 0) ? fwd : lat;

            // 確信チェーン追跡中 (真理未到達) で、 横方向にイベントタイルがあるなら候補に加える。
            // (前進限定だと同行のイベントを拾えず、 チェーンが進まないため)
            int convStageNav = gm.Run?.convictionStage ?? 0;
            if (convStageNav < GameLoop.ConvictionSystem.StageTruth
                && fwd != null && fwd.Count > 0 && lat != null && lat.Count > 0)
            {
                foreach (var ln in lat)
                {
                    // 未訪問のイベントマスのみ追跡（訪問済み＝消化済みを追うと同行往復で無限ループ）
                    if (ln.EffectiveType == TileType.Event && !ln.visited && !pool.Contains(ln))
                        pool.Add(ln);
                }
            }

            if (pool == null || pool.Count == 0)
            {
                Finish(Outcome.Deadlock, "移動先なし(MapNavigation)");
                return true;
            }

            float hpRatio = gm.Run.playerMaxHP > 0
                ? (float)gm.Run.playerHP / gm.Run.playerMaxHP : 1f;

            // ボス接近判定: プールにBossがある or 残り行数が2以内
            bool bossNear = false;
            foreach (var n in pool) if (n.EffectiveType == TileType.Boss) { bossNear = true; break; }
            int rowCount = mm.CurrentMap?.rowCount ?? 10;
            if (!bossNear && mm.CurrentNode != null && mm.CurrentNode.row >= rowCount - 2)
                bossNear = true;
            _curBossNear = bossNear; // 休憩フェーズの選択判断で参照

            // 空腹残量
            int hungerCur = mm.Hunger?.Current ?? 99;
            bool hungerLow = hungerCur <= 2;

            // 回復目標: 貪欲はボス接近時 HP8割を目指す。それ以外は半分。
            float healTarget = (!_curCombatAverse && bossNear) ? 0.8f : 0.5f;
            while (Consumables.TryUseBestHeal(gm.Run, healTarget)) { }

            // 休憩を強く優先すべき状況:
            //  - 空腹が尽きかけ（道中赤字回避）
            //  - 貪欲がボス接近かつHPが8割未満（スケールしたbuildをボスへ生存させる）
            bool preferRest = hungerLow
                || (!_curCombatAverse && bossNear && hpRatio < 0.8f);

            MapNode best = pool[0];
            int bestRank = int.MaxValue;
            foreach (var n in pool)
            {
                int r = Rank(n.EffectiveType, hpRatio, _curCombatAverse, preferRest, gm.Run);
                if (r < bestRank) { bestRank = r; best = n; }
            }
            gm.MoveToNode(best.id);
            return false;
        }

        /// <summary>低いほど優先。HP帯(危機/低/健康)で重み分け。
        /// 戦闘タイル(Battle/EliteBattle)のみ profile で差し替え、他は共通固定。
        /// averse=false(戦闘貪欲): 健康なら戦闘を最優先級で選ぶ。
        /// averse=true(戦闘回避): 戦闘を最下位級にし、戦闘以外があれば必ず回避。</summary>
        private int Rank(TileType t, float hpRatio, bool averse, bool preferRest, GameLoop.RunState run)
        {
            bool crit = hpRatio < 0.3f;   // 危機
            bool low  = hpRatio < 0.55f;  // 低HP

            // 空腹尽きかけ／貪欲のボス前整え: 休憩を最優先（食事＝空腹全回復も兼ねる）
            if (preferRest && t == TileType.Rest) return -1;

            // 確信チェーン進路の強制優先 (両プロファイル共通)
            //  - 災厄の予兆 未完了 (= convictionStage 0) → イベント/ミステリ を常に最優先
            //    (Event/Mystery は戦闘でないので HP に関わらず追跡可能)
            //  - 災厄の予兆 完了 + 真理未到達 (stage 1〜4) → HP≥50% のみエリート最優先
            //    (エリートは戦闘発生 = HP リスクなので安全時のみ)
            int convStage = run?.convictionStage ?? 0;
            if (convStage == 0)
            {
                // Mystery は 20% でしか Event 化しないため Bot の確信進路としては当てにせず、
                // Event タイルのみ強優先する。
                if (t == TileType.Event) return -10;
            }
            else if (convStage > 0 && convStage <= GameLoop.ConvictionSystem.StageTruth)
            {
                if (t == TileType.EliteBattle && hpRatio >= 0.5f) return -5;
            }

            // --- 戦闘タイル: ここだけが比較軸（1変数） ---
            // HP低下時 (50%未満) は profile に関係なく Battle/Elite を強く忌避する。
            //  (回復を最優先にしたいというユーザー要望)
            if (t == TileType.Battle)
            {
                if (low) return crit ? 96 : 93; // 低HP/危機: averse 同等の忌避
                return averse ? 90 : 1;
            }
            if (t == TileType.EliteBattle)
            {
                if (low) return crit ? 98 : 95; // 低HP/危機: averse 同等の忌避
                return averse ? 93 : 2;
            }

            // --- 非戦闘タイル: 両プロファイル共通固定 ---
            switch (t)
            {
                case TileType.Rest:        return crit ? 0 : low ? 0 : 6;
                case TileType.Shop:        return crit ? 1 : 1;
                case TileType.Treasure:    return crit ? 2 : low ? 2 : 0;  // 装備強化源
                case TileType.Event:       return crit ? 5 : 3;
                case TileType.Mystery:     return crit ? 5 : 3;
                case TileType.Exchange:    return crit ? 5 : 2;  // ビルド強化源（厳密アップグレード）
                case TileType.Trap:        return crit ? 7 : 6;
                case TileType.SinAltar:    return 4;
                case TileType.Boss:        return 9;  // 最後に残れば踏む(フロアクリア必須)
                case TileType.Outpost:     return 0;
                default:                   return 4;
            }
        }

        private void DoShop()
        {
            var gm = GameManager.Instance;
            var sm = ShopManager.Instance;
            var inv = sm != null ? sm.Current : null;
            if (inv != null && inv.slots != null)
            {
                var run = gm.Run;
                bool Buy(int i)
                {
                    var s = inv.slots[i];
                    if (s == null || s.sold || s.price > run.coins) return false;
                    int before = run.coins;
                    gm.ShopBuy(i);
                    if (run.coins < before) { _cur.shopPurchases++; return true; }
                    return false;
                }
                bool BuyKind(ShopSlotKind k)
                {
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold
                            && inv.slots[i].kind == k && Buy(i)) return true;
                    return false;
                }

                // 「見える範囲で最後のショップ」(=5層以降) は買える物が尽きるまで使い切る
                bool lastShop = run.currentFloor >= run.normalClearFloor;
                if (lastShop)
                {
                    int guard = 0;
                    bool bought = true;
                    while (bought && guard++ < 40)
                    {
                        bought = false;
                        // 価値順: 武器/ダイス → パッシブ → 強化素材 → 消費
                        if (BuyKind(ShopSlotKind.Weapon)) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.Dice)) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.Passive)) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.WeaponMaterial)) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.Consumable)) { bought = true; continue; }
                    }
                }
                else
                {
                    // 通常ショップ: 余剰金を戦力へ。1/5デノミ後の閾値: 40→8, 25→5, 20→4
                    // 武器・ダイス（自動装備で強化見込み）
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > 8
                            && (inv.slots[i].kind == ShopSlotKind.Weapon
                                || inv.slots[i].kind == ShopSlotKind.Dice)) Buy(i);
                    // パッシブ（金が余るので貪欲に）
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > 5
                            && inv.slots[i].kind == ShopSlotKind.Passive) Buy(i);
                    // 武器強化素材: 次の強化に届くまで補充（価格倍々なので2個まで）
                    int needMat = GameManager.WeaponUpgradeCost(run.weaponUpgradeLevel);
                    int matBuys = 0;
                    while (run.weaponMaterials < needMat && run.coins > 4 && matBuys++ < 2
                           && BuyKind(ShopSlotKind.WeaponMaterial)) { }
                    // 回復消費を3個までストック
                    int stock = run.ownedConsumables != null ? run.ownedConsumables.Count : 0;
                    for (int i = 0; i < inv.slots.Count && stock < 3; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > 4
                            && inv.slots[i].kind == ShopSlotKind.Consumable && Buy(i)) stock++;
                }
            }
            gm.ExitShop();
        }

        /// <summary>戦闘をターン逐次で進行。開始直後にバフ消費、各ターン前に緊急回復判定。</summary>
        private void RunCombatWithItems(GameManager gm)
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsCombatActive) return;
            var run = gm.Run;

            // バフ/シールドは「重要戦闘」のみ。雑魚相手の初手オールインは非合理なので回避。
            // 重要 = ボス / 高脅威(threat>=5) / 一撃が現HPの半分以上を奪い得る危険戦闘。
            var e0 = cm.CurrentEnemy;
            bool important = false;
            if (e0 != null)
            {
                bool boss = e0.id != null && e0.id.StartsWith("boss_layer");
                int maxHit = e0.diceCount * e0.diceMaxValue * (e0.criticalNumerator > 0 ? 2 : 1);
                important = boss || e0.threat >= 5 || maxHit * 2 >= Math.Max(1, cm.PlayerHP);
            }

            if (important)
            {
                // 攻撃/ダイス/会心/鬼火 → シールド/土塊 → 継続 → ユニーク
                UseFirst(run, "uniq_oni_oil", "cons_atk_4", "cons_atk_3", "cons_dice_4", "cons_dice_3",
                              "cons_crit_4", "cons_crit_3", "cons_atk_2", "cons_dice_2", "cons_crit_2");
                UseFirst(run, "cons_shield_4", "cons_shield_3", "cons_shield_2", "uniq_earth_guard",
                              "cons_reduce_4", "cons_reduce_3", "cons_shield_1");
                UseFirst(run, "cons_regen_4", "cons_regen_3", "cons_regen_2", "cons_regen_1");
                UseFirst(run, "uniq_mirror");
                UseFirst(run, "uniq_ambush");
            }

            int guard = 0;
            while (cm.IsCombatActive && guard++ < 250)
            {
                // シュヴァリエ戦専用: ボス形態に同期してレイピアをトグル
                AutoToggleRapierVsSaintGeorges(cm, run);

                // 緊急回復: 敵の最大ダイスダメージ(会心なら×2)で落ちそうなら回復
                var e = cm.CurrentEnemy;
                if (e != null)
                {
                    int maxHit = e.diceCount * e.diceMaxValue;
                    if (e.criticalNumerator > 0) maxHit *= 2;
                    if (cm.PlayerHP <= maxHit)
                        UseFirst(run, "cons_heal_4", "cons_heal_3", "cons_heal_2", "cons_heal_1");
                }
                var tr = cm.ExecuteTurn();
                if (tr.isDraw) _cwDraw++;
                else if (tr.playerWon) _cwWin++;
                else { _cwLoss++; if (tr.totalDamage <= 0) _cwLossAbs++; }
            }
        }

/// <summary>6F (灰燼の王) 撃破直後のビルド情報を _cur に記録。
        /// 装備/アイテム/HP/カルマ/各種デバフを 1 行プレーンテキストに圧縮。
        /// 後でサマリーから「どんな装備で 6F まで来たか」をサルベージする用途。</summary>
        private void Capture6FClearSnapshot(GameManager gm)
        {
            if (_cur == null || gm?.Run == null) return;
            var run = gm.Run;
            var sb = new System.Text.StringBuilder();
            sb.Append($"HP {run.playerHP}/{run.playerMaxHP} | coins {run.coins} | mat {run.weaponMaterials} | upgLv {run.weaponUpgradeLevel} | karma {run.karma}");
            sb.Append($"\n      武器: {(string.IsNullOrEmpty(run.equippedWeaponId) ? "(無)" : run.equippedWeaponId)} | ダイス: {(string.IsNullOrEmpty(run.equippedDiceId) ? "(武器ダイス)" : run.equippedDiceId)}");
            int pCnt = run.ownedPassiveItems?.Count ?? 0;
            string pList = pCnt > 0 ? string.Join(", ", run.ownedPassiveItems) : "(無)";
            sb.Append($"\n      パッシブ({pCnt}): {pList}");
            int cCnt = run.ownedConsumables?.Count ?? 0;
            string cList = cCnt > 0 ? string.Join(", ", run.ownedConsumables) : "(無)";
            sb.Append($"\n      消費品({cCnt}): {cList}");
            int fCnt = run.ownedFlags?.Count ?? 0;
            if (fCnt > 0) sb.Append($"\n      フラグ({fCnt}): {string.Join(", ", run.ownedFlags)}");
            if (run.timedBuffs != null && run.timedBuffs.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in run.timedBuffs) parts.Add($"{kv.Key}×{kv.Value}");
                sb.Append($"\n      時限バフ: {string.Join(", ", parts)}");
            }
            if (run.timedDebuffs != null && run.timedDebuffs.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in run.timedDebuffs) parts.Add($"{kv.Key}×{kv.Value}");
                sb.Append($"\n      時限デバフ: {string.Join(", ", parts)}");
            }
            if (run.permanentDebuffs != null && run.permanentDebuffs.Count > 0)
                sb.Append($"\n      恒久デバフ: {string.Join(", ", run.permanentDebuffs)}");
            if (run.sinDebuffs != GameLoop.SinDebuff.None)
                sb.Append($"\n      6層儀式の罪: {run.sinDebuffs}");
            sb.Append($"\n      LS使用済: {run.lastStandActive} | サン=ジョリオラ撃破: {run.defeatedSaintGeorges}");
            _cur.clear6FSnapshot = sb.ToString();
        }

        /// <summary>Bot 専用: 戦闘中、シュヴァリエ・サン=ジョリオラ戦でのレイピア起動同期。
        /// ボスが形態1(コントラタック)中はレイピアON、形態2(オポジション)中はOFF。
        /// 切替時の解除効果(次T ダイス+1, 撃破済みなら会心+9)も自動で享受する。</summary>
        private void AutoToggleRapierVsSaintGeorges(CombatManager cm, GameLoop.RunState run)
        {
            if (cm == null || run == null) return;
            if (run.ownedPassiveItems == null || !run.ownedPassiveItems.Contains("chevalier_rapier")) return;
            var enemy = cm.CurrentEnemy;
            if (enemy == null || enemy.id != "boss_layer5_hidden") return;
            var ctx = InventorySystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx == null) return;
            int bossPhase = (int)ctx.GetAccumulated("sg_phase");
            bool contreActive = ctx.GetAccumulated("player_contre") > 0f;
            if (bossPhase == 1 && !contreActive)
                GameLoop.Consumables.TryUseRapier(run);
            else if (bossPhase == 2 && contreActive)
                GameLoop.Consumables.TryUseRapier(run);
        }

        /// <summary>所持consumableから優先順に最初の1個を使用（戦闘中＝ctx即時適用）。</summary>
        private bool UseFirst(GameLoop.RunState run, params string[] ids)
        {
            if (run?.ownedConsumables == null) return false;
            foreach (var id in ids)
                if (run.ownedConsumables.Contains(id))
                    return GameLoop.Consumables.Use(run, id);
            return false;
        }

        private void DoEvent()
        {
            var gm = GameManager.Instance;
            var ee = EventEncounter.Instance;
            var cur = ee != null ? ee.Current : null;

            // ランダムイベント無限ループ等の保険: 一定回数で強制読了して脱出を試みる
            // （回復不能なら RunOne のストール検出が DEADLOCK として確定させる）
            _eventStuckCount++;
            if (_eventStuckCount > 40)
            {
                // Current を null 化 → GameManager 側(D1)が未確定でも MapNavigation へ脱出させる
                EventEncounter.Instance?.Clear();
                gm.ConfirmEventEncounter();
                _lastResolvedEvent = cur;
                return;
            }

            if (cur == null) { gm.ConfirmEventEncounter(); return; }

            if (!ReferenceEquals(cur, _lastResolvedEvent))
            {
                int idx = PickSafeChoice(cur);
                _lastResolvedEvent = cur;
                gm.ResolveEventChoice(idx);
            }
            else
            {
                // フレーバー読了 or 戦闘トリガ後の復帰待ち
                gm.ConfirmEventEncounter();
            }
        }

        private int PickSafeChoice(EventDefinition def)
        {
            if (def == null || def.choices == null || def.choices.Count == 0) return 0;

            // ① フラグ進路スコアリング: フラグ成立 / 名前付きパッシブ獲得を強く優先、
            //    フラグ放棄のみの選択肢を強く忌避する (チェーン進路を切らないため)
            int progressIdx = PickFlagProgressChoice(def);
            if (progressIdx >= 0) return progressIdx;

            // ② 「立ち去る/無視」系があれば最優先（リスク0で確実に抜ける）
            for (int i = 0; i < def.choices.Count; i++)
            {
                var txt = def.choices[i]?.text ?? "";
                foreach (var kw in LeaveKeywords)
                    if (txt.Contains(kw)) return i;
            }
            // ③ 危険語を含まない選択肢を選ぶ
            for (int i = 0; i < def.choices.Count; i++)
            {
                var txt = def.choices[i]?.text ?? "";
                bool danger = false;
                foreach (var kw in DangerKeywords)
                    if (txt.Contains(kw)) { danger = true; break; }
                if (!danger) return i;
            }
            // ④ 全て危険語入り → 先頭
            return 0;
        }

        /// <summary>選択肢の効果を見て「フラグを進める」スコアを算出。
        /// いずれかの選択肢がプラスならその index を返し、なければ -1。
        /// 既存所持フラグを廃棄するだけの選択肢はマイナス点で忌避される。</summary>
        private int PickFlagProgressChoice(EventDefinition def)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            int bestIdx = -1;
            int bestScore = 0;
            for (int i = 0; i < def.choices.Count; i++)
            {
                int s = ScoreFlagChoice(def.choices[i], run);
                if (s > bestScore) { bestScore = s; bestIdx = i; }
            }
            return bestIdx;
        }

        /// <summary>選択肢1つに対するフラグ進路スコア。</summary>
        private int ScoreFlagChoice(EventSystem.EventChoice choice, GameLoop.RunState run)
        {
            if (choice?.effects == null || choice.effects.Count == 0) return 0;
            int score = 0;
            bool hasGain = false;
            int discardCount = 0;
            foreach (var eff in choice.effects)
            {
                switch (eff.type)
                {
                    case EventSystem.EventEffectType.GainFlag:
                        score += 100; hasGain = true;
                        // 未所持なら追加加点 (フラグ未成立時=新規進路を優先)
                        if (run?.ownedFlags == null || !run.ownedFlags.Contains(eff.param ?? "")) score += 50;
                        break;
                    case EventSystem.EventEffectType.GainPassiveItem:
                    case EventSystem.EventEffectType.GainSpecificItem:
                        // 名前付き / パッシブ獲得は基本的に進路系
                        score += 60; hasGain = true;
                        break;
                    case EventSystem.EventEffectType.DiscardFlag:
                        discardCount++;
                        break;
                }
            }
            // フラグ廃棄のみで何も獲得しない選択肢は強い忌避
            if (discardCount > 0 && !hasGain) score -= 80;
            // 廃棄しつつ獲得もある (チェーン継続: 旧フラグ → 新フラグ/パッシブ) は中立 (gain加点で十分)
            return score;
        }

        // ===== 計測フック =====

        private void OnEnemyEncountered(EnemyData e)
        {
            var gm = GameManager.Instance;
            var cm = CombatSystem.CombatManager.Instance;

            // チェーン swap で再エンカウントしたケース: 前フォームの戦績を 1 件確定させる。
            // (戦闘自体は継続するため OnBattleEnded は鳴らない → ここで明示記録しないと
            //  途中フォームが永遠に summary に出てこない)
            bool isChainSwap = cm != null && cm.IsCombatActive
                && !string.IsNullOrEmpty(_pendingEnemyId);
            if (isChainSwap && _cur != null)
            {
                int hpNow = gm?.Run?.playerHP ?? 0;
                var midRec = new CombatRec
                {
                    enemy = _pendingEnemyName ?? "?",
                    enemyId = _pendingEnemyId ?? "",
                    floor = gm?.Run?.currentFloor ?? 0,
                    isBoss = _pendingEnemyIsBoss,
                    won = true, // チェーン swap は前形態を倒したから起きる
                    turns = _cwWin + _cwDraw + _cwLoss,
                    hpBefore = _pendingEnemyHpBefore,
                    hpAfter = hpNow,
                    afterLastStand = _cur.lastStandUsed,
                    tWin = _cwWin, tDraw = _cwDraw, tLoss = _cwLoss, tLossAbs = _cwLossAbs
                };
                _cur.combats.Add(midRec);
                // 注: totalCombats / totalTurns / totalWins には加算しない
                // (OnBattleEnded 側のチェーン最終形態分でラン全体の合計が記録されるため、
                //  ここで足すと二重計上になる。combats リストの per-enemy 集計だけ厚くする)
            }

            _pendingEnemyName = e != null ? e.displayName : "?";
            _pendingEnemyId = e != null && e.id != null ? e.id : "";
            // ボス判定は敵IDのみで厳密に行う（ノード種別フォールバックは誤検出の元）
            _pendingEnemyIsBoss = _pendingEnemyId.StartsWith("boss_layer");
            _pendingEnemyHpBefore = gm.Run?.playerHP ?? 0;
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
                floor = gm.Run?.currentFloor ?? 0,
                isBoss = _pendingEnemyIsBoss,
                won = r.playerWon,
                turns = r.totalTurns,
                hpBefore = _pendingEnemyHpBefore,
                hpAfter = r.playerHPRemaining,
                afterLastStand = _cur.lastStandUsed,
                tWin = _cwWin, tDraw = _cwDraw, tLoss = _cwLoss, tLossAbs = _cwLossAbs
            };
            _cur.combats.Add(rec);
            _cur.totalCombats++;
            _cur.totalTurns += r.totalTurns;
            if (r.playerWon) _cur.totalWins++;
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
        }

        private void TrackLastStand()
        {
            var run = GameManager.Instance.Run;
            if (run == null || _cur == null) return;
            if (run.lastStandActive && !_cur.lastStandUsed)
            {
                _cur.lastStandUsed = true;
                _cur.lastStandFloor = run.currentFloor;
                _curLog.Add($"[AutoRunner] ラストスタンド発動 (Floor {run.currentFloor})");
            }
        }

        private void TrackEconomy()
        {
            var run = GameManager.Instance.Run;
            if (run == null || _cur == null) return;
            if (run.coins > _cur.peakCoins) _cur.peakCoins = run.coins;
            if (run.coins > _prevCoins) _cur.totalGoldGained += (run.coins - _prevCoins);
            _prevCoins = run.coins;
        }

        private string CurrentNodeId()
        {
            return MapManager.Instance?.CurrentNode?.id ?? "";
        }

        // ===== ラン終了処理 =====

        private void FinishClear()
        {
            var run = GameManager.Instance.Run;
            bool full = run != null && run.currentFloor >= run.maxFloor;
            Finish(full ? Outcome.FullClear : Outcome.NormalClear, full ? "完全クリア(7F)" : "通常クリア(5F)");
        }

        private void FinishGameOver()
        {
            var run = GameManager.Instance.Run;
            var cause = DeathCause.Unknown;
            bool bossFight = MapManager.Instance?.CurrentNode?.EffectiveType == TileType.Boss;
            string fatal = "";

            var lcr = GameManager.Instance.LastCombatResult;
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
            else if (run != null && run.playerMaxHP <= 1 && CurrentNodeId() == "karma_trap")
            {
                cause = DeathCause.KarmaSettlement;
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
                _cur.reachedFloor = run.currentFloor;
                _cur.reached6F = run.currentFloor >= 6;
                _cur.finalHP = run.playerHP;
                _cur.finalMaxHP = run.playerMaxHP;
                _cur.finalCoins = run.coins;
                _cur.deathFloor = (o == Outcome.GameOver) ? run.currentFloor : 0;
                _cur.gedatsuVictory = run.gedatsuVictory;
            }
            Classify(_cur);
            _records.Add(_cur);
            _curLog.Add($"[AutoRunner] === RUN {_cur.index} 終了: {_cur.band} ({_cur.bandLabel}) — {note} ===");
            _curAttachLog(_cur);
            _cur = null;
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
                if (r.gedatsuVictory) { r.band = "R12"; r.bandLabel = "解脱(妙覚サドンデス勝利)"; }
                else                  { r.band = "R11"; r.bandLabel = "7層クリア(完全クリア)"; }
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
            if (f >= 7) { r.band = "R10"; r.bandLabel = "7Fで死亡(覚者到達)"; return; }
            if (f >= 6) { r.band = "R9"; r.bandLabel = "6Fボスで死亡"; return; }
            switch (f)
            {
                case 1:
                case 2:
                    r.band = "R1"; r.bandLabel = "2F以前で死亡"; break;
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
                    r.band = "R1"; r.bandLabel = "2F以前で死亡"; break;
            }
        }

        // ===== ログ購読 =====

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
            else if (type == LogType.Exception || type == LogType.Error)
            {
                AddLog($"[{type}] {condition}");
            }
        }

        /// <summary>リングバッファ追記。上限超過時は先頭(古い行)を捨て、末尾の終端ログを必ず残す。</summary>
        private void AddLog(string line)
        {
            if (_curLog.Count >= detailMaxLinesPerRun && _curLog.Count > 0)
                _curLog.RemoveAt(0);
            _curLog.Add(line);
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

        private string WriteLogs()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string root = Path.Combine(Application.dataPath, "..", "AutoRunLogs");
            root = Path.GetFullPath(root);
            string metaTag = metaPattern switch
            {
                MetaPattern.Cowardly        => "cowardly",
                MetaPattern.FullProgression => "fullmeta",
                MetaPattern.Untouched       => "saved",
                _                           => "meta",
            };
            string debuffTag = enableAllDebuffs ? "_debuffON" : "";
            string dir = Path.Combine(root, $"batch_{stamp}_n{_records.Count}_{metaTag}{debuffTag}");
            Directory.CreateDirectory(dir);

            File.WriteAllText(Path.Combine(dir, "summary.txt"), BuildSummary(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "runs.jsonl"), BuildJsonl(), new UTF8Encoding(false));
            if (writeDetailLog)
                File.WriteAllText(Path.Combine(dir, "detail.log"), BuildDetail(), new UTF8Encoding(false));

            return dir;
        }

        private string Pct(int n, int total) =>
            total == 0 ? "0.0%" : (100.0 * n / total).ToString("F1", CultureInfo.InvariantCulture) + "%";

        /// <summary>同一ファイル内で、行動ルーチン別（戦闘貪欲/戦闘回避）に
        /// 集計ブロックを分離して出力する。各ブロックは自己完結の全集計。</summary>
        private string BuildSummary()
        {
            var greedy = _records.FindAll(r => r.profile == "貪欲");
            var averse = _records.FindAll(r => r.profile == "回避");
            var other  = _records.FindAll(r => r.profile != "貪欲" && r.profile != "回避");

            var sb = new StringBuilder();
            string metaLabel = metaPattern switch
            {
                MetaPattern.Cowardly        => "臆病(メタ全リセット)",
                MetaPattern.FullProgression => $"全有効化(メタLv{MetaProgression.MetaBuffTrack.TotalSteps})",
                MetaPattern.Untouched       => "保存値そのまま",
                _                           => metaPattern.ToString(),
            };
            sb.AppendLine("################ AutoRun サマリ ################");
            sb.AppendLine($"日時      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"メタ進行  : {metaLabel}");
            sb.AppendLine($"メタデバフ: {(enableAllDebuffs ? "全ON (Lv1-10, 最高難易度)" : "全OFF")}");
            sb.AppendLine($"総ラン数  : {_records.Count}  (戦闘貪欲={greedy.Count} / 戦闘回避={averse.Count})");
            sb.AppendLine("比較軸    : 航行Rankのみ差し替え（消費/ショップ/戦闘実行/イベントは共通固定）");
            sb.AppendLine("################################################");
            sb.AppendLine();
            sb.Append(BuildSummaryBlock("【前半 50% ─ 戦闘貪欲（戦闘マスを最優先で選ぶ）】", greedy));
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(BuildSummaryBlock("【後半 50% ─ 戦闘回避（戦闘以外があれば必ず回避）】", averse));
            if (other.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append(BuildSummaryBlock("【プロファイル未設定（保険）】", other));
            }
            return sb.ToString();
        }

        /// <summary>1コホート分の自己完結サマリブロック。</summary>
        private string BuildSummaryBlock(string title, List<RunRec> recs)
        {
            int n = recs.Count;
            var sb = new StringBuilder();
            sb.AppendLine("================ " + title + " ================");
            sb.AppendLine($"ラン数    : {n}");
            sb.AppendLine($"行動方針  : 前進貪欲 + 生存重視");
            sb.AppendLine();
            if (n == 0) { sb.AppendLine("（該当ランなし）"); return sb.ToString(); }

            // --- バンド分布 (R1-R12, R8b 含む) ---
            // R8b = 6Fクリアして 7F進入不可で終了 (真理未所持) を R9 と R10 の間に挿入。
            sb.AppendLine("---- 結果バンド分布（CRASH/DEADLOCK除く母数で割合算出） ----");
            string[] bands = { "R1","R2","R3","R4","R5","R6","R7","R8","R9","R8b","R10","R11","R12" };
            string[] labels = {
                "2F以前で死亡","3F道中で死亡","3Fボスで死亡","4F道中で死亡","4Fボスで死亡",
                "5F道中で死亡","5Fボスで死亡","5Fクリア(決意未所持)","6Fボスで死亡",
                "6Fクリア(真理未所持)","7Fで死亡(覚者到達)","7層クリア(完全クリア)","解脱(妙覚サドンデス勝利)" };
            int crash = recs.FindAll(r => r.band == "CRASH").Count;
            int dead  = recs.FindAll(r => r.band == "DEADLOCK").Count;
            int valid = n - crash - dead;
            for (int i = 0; i < bands.Length; i++)
            {
                int c = recs.FindAll(r => r.band == bands[i]).Count;
                sb.AppendLine($"  {PadR(bands[i],4)}{PadR(labels[i],22)}: {PadL(c.ToString(),4)}  ({Pct(c, valid)})");
            }
            sb.AppendLine();
            sb.AppendLine($"  {PadR("CRASH",4)}{PadR("クラッシュ(例外)",22)}: {PadL(crash.ToString(),4)}  ({Pct(crash, n)} of all)");
            sb.AppendLine($"  {PadR("DEAD",4)}{PadR("デッドロック",22)}: {PadL(dead.ToString(),4)}  ({Pct(dead, n)} of all)");
            sb.AppendLine();

            // --- 死亡階層・死因 ---
            sb.AppendLine("---- 死亡階層・死因 ----");
            var deaths = recs.FindAll(r => r.outcome == Outcome.GameOver);
            sb.AppendLine($"  総死亡数: {deaths.Count} / {n}");
            for (int f = 1; f <= 6; f++)
            {
                int c = deaths.FindAll(r => r.deathFloor == f).Count;
                if (c > 0) sb.AppendLine($"   {PadR("Floor " + f, 16)}: {PadL(c.ToString(),4)}  ({Pct(c, deaths.Count)})");
            }
            foreach (DeathCause dc in Enum.GetValues(typeof(DeathCause)))
            {
                if (dc == DeathCause.None) continue;
                int c = deaths.FindAll(r => r.cause == dc).Count;
                if (c > 0) sb.AppendLine($"   {PadR(dc.ToString(), 16)}: {PadL(c.ToString(),4)}  ({Pct(c, deaths.Count)})");
            }
            sb.AppendLine();

            // --- ラストスタンド ---
            sb.AppendLine("---- ラストスタンド ----");
            var ls = recs.FindAll(r => r.lastStandUsed);
            sb.AppendLine($"  発動ラン数: {ls.Count} / {n}  ({Pct(ls.Count, n)})");
            for (int f = 1; f <= 6; f++)
            {
                int c = ls.FindAll(r => r.lastStandFloor == f).Count;
                if (c > 0) sb.AppendLine($"   {PadR("発動Floor " + f, 16)}: {PadL(c.ToString(),4)}  ({Pct(c, ls.Count)})");
            }
            if (ls.Count > 0)
            {
                double avgAfter = 0, avgWin = 0;
                foreach (var r in ls) { avgAfter += r.combatsAfterLastStand; avgWin += r.winsAfterLastStand; }
                int maxAfter = 0; foreach (var r in ls) maxAfter = Math.Max(maxAfter, r.combatsAfterLastStand);
                sb.AppendLine($"   {PadR("発動後の平均戦闘数", 20)}: {(avgAfter/ls.Count):F2}");
                sb.AppendLine($"   {PadR("発動後の平均勝利数", 20)}: {(avgWin/ls.Count):F2}");
                sb.AppendLine($"   {PadR("発動後の最大潜り抜け", 20)}: {maxAfter} 戦");
            }
            sb.AppendLine();

            // --- ラストスタンド直接死因 ---
            sb.AppendLine("---- ラストスタンド直接死因 ----");
            var lsDead = ls.FindAll(r => r.outcome == Outcome.GameOver);
            sb.AppendLine($"  発動後に死亡したラン: {lsDead.Count} / {ls.Count}");
            if (lsDead.Count > 0)
            {
                // 発動後 何戦目で死亡したか（combatsAfterLastStand のバケット）
                int b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0;
                foreach (var r in lsDead)
                {
                    int c = r.combatsAfterLastStand;
                    if (c <= 0) b0++; else if (c == 1) b1++; else if (c == 2) b2++;
                    else if (c == 3) b3++; else b4++;
                }
                sb.AppendLine($"   {PadR("発動と同戦闘で即死(0戦)", 24)}: {PadL(b0.ToString(),4)}  ({Pct(b0, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後1戦で死亡", 24)}: {PadL(b1.ToString(),4)}  ({Pct(b1, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後2戦で死亡", 24)}: {PadL(b2.ToString(),4)}  ({Pct(b2, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後3戦で死亡", 24)}: {PadL(b3.ToString(),4)}  ({Pct(b3, lsDead.Count)})");
                sb.AppendLine($"   {PadR("発動後4戦以上で死亡", 24)}: {PadL(b4.ToString(),4)}  ({Pct(b4, lsDead.Count)})");

                // 死亡階層
                for (int f = 1; f <= 6; f++)
                {
                    int c = lsDead.FindAll(r => r.deathFloor == f).Count;
                    if (c > 0) sb.AppendLine($"   {PadR("Floor " + f + " で死亡", 24)}: {PadL(c.ToString(),4)}  ({Pct(c, lsDead.Count)})");
                }

                // 致命を与えた敵（各ランの最後の敗北戦から）
                var fatalAgg = new Dictionary<string, int>();
                foreach (var r in lsDead)
                {
                    if (r.combats == null) continue;
                    CombatRec fc = null;
                    for (int i = r.combats.Count - 1; i >= 0; i--)
                        if (!r.combats[i].won) { fc = r.combats[i]; break; }
                    if (fc == null) continue;
                    bool boss = fc.isBoss || (!string.IsNullOrEmpty(fc.enemyId) && fc.enemyId.StartsWith("boss_layer"));
                    string nm = (string.IsNullOrEmpty(fc.enemy) ? "?" : fc.enemy) + (boss ? "(BOSS)" : "");
                    fatalAgg.TryGetValue(nm, out var v);
                    fatalAgg[nm] = v + 1;
                }
                var fk = new List<string>(fatalAgg.Keys);
                fk.Sort((x, y) => fatalAgg[y].CompareTo(fatalAgg[x]));
                sb.AppendLine("   ── 致命を与えた敵 ──");
                foreach (var k in fk)
                    sb.AppendLine($"   {PadR(TruncDisp(k,18), 22)}: {PadL(fatalAgg[k].ToString(),4)}  ({Pct(fatalAgg[k], lsDead.Count)})");
            }
            sb.AppendLine("（発動後は基本ロール敗北のメインダメのみ＝致命敵＝その止めを刺した敵）");
            sb.AppendLine();

            // --- 経済・燃費 ---
            sb.AppendLine("---- 経済・燃費バランス ----");
            double sCoins=0, sPeak=0, sGain=0, sStarv=0, sStarvHit=0, sShop=0;
            foreach (var r in recs)
            { sCoins+=r.finalCoins; sPeak+=r.peakCoins; sGain+=r.totalGoldGained;
              sStarv+=r.starvationTotal; sStarvHit+=r.starvationHits; sShop+=r.shopPurchases; }
            int dn = Math.Max(1, n);
            sb.AppendLine($"  {PadR("平均最終ゴールド", 20)}: {(sCoins/dn):F1}");
            sb.AppendLine($"  {PadR("平均ピークゴールド", 20)}: {(sPeak/dn):F1}");
            sb.AppendLine($"  {PadR("平均総獲得ゴールド", 20)}: {(sGain/dn):F1}");
            sb.AppendLine($"  {PadR("平均空腹ダメージ計", 20)}: {(sStarv/dn):F1}  (平均被弾 {(sStarvHit/dn):F1}回)");
            sb.AppendLine($"  {PadR("平均ショップ購入数", 20)}: {(sShop/dn):F2}");
            sb.AppendLine();

            // --- 5F突入時 確信チェーン進行状況 ---
            // 5F到達ランのみ対象。 各種フラグ/パッシブ所持割合を出して、 どこで詰まったか分析する。
            var arrived5F = recs.FindAll(r => r.convictionStageAt5F >= 0);
            if (arrived5F.Count > 0)
            {
                sb.AppendLine("---- 5F突入時 確信チェーン進行 ----");
                int total5F = arrived5F.Count;
                int yogen   = arrived5F.FindAll(r => r.hadFlagYogenAt5F).Count;
                int kakushin= arrived5F.FindAll(r => r.hadFlagKakushinAt5F).Count;
                int gen     = arrived5F.FindAll(r => r.hadConvictionItem5F).Count;
                int ketsui  = arrived5F.FindAll(r => r.hadResolveAt5F).Count;
                int shinri  = arrived5F.FindAll(r => r.hadTruthAt5F).Count;
                int s0 = arrived5F.FindAll(r => r.convictionStageAt5F == 0).Count;
                int s1 = arrived5F.FindAll(r => r.convictionStageAt5F == 1).Count;
                int s2 = arrived5F.FindAll(r => r.convictionStageAt5F == 2).Count;
                int s3 = arrived5F.FindAll(r => r.convictionStageAt5F == 3).Count;
                int s4plus = arrived5F.FindAll(r => r.convictionStageAt5F >= 4).Count;
                sb.AppendLine($"  5F到達ラン数      : {total5F}");
                sb.AppendLine($"  フラグ[苦難の予言]: {PadL(yogen.ToString(),4)}  ({Pct(yogen, total5F)})  ※チェーン1段目完了");
                sb.AppendLine($"  フラグ[苦難の確信]: {PadL(kakushin.ToString(),4)}  ({Pct(kakushin, total5F)})  ※チェーン2段目完了");
                sb.AppendLine($"  〈根拠のない確信〉: {PadL(gen.ToString(),4)}  ({Pct(gen, total5F)})  ※チェーン3段目完了 (stage 1)");
                sb.AppendLine($"  〈決意〉所持      : {PadL(ketsui.ToString(),4)}  ({Pct(ketsui, total5F)})  ※stage 2-3 = 6F進入可");
                sb.AppendLine($"  〈真理〉所持      : {PadL(shinri.ToString(),4)}  ({Pct(shinri, total5F)})  ※stage 4+ = 7F進入可");
                sb.AppendLine($"  stage 0           : {PadL(s0.ToString(),4)}  ({Pct(s0, total5F)})");
                sb.AppendLine($"  stage 1           : {PadL(s1.ToString(),4)}  ({Pct(s1, total5F)})");
                sb.AppendLine($"  stage 2           : {PadL(s2.ToString(),4)}  ({Pct(s2, total5F)})");
                sb.AppendLine($"  stage 3           : {PadL(s3.ToString(),4)}  ({Pct(s3, total5F)})");
                sb.AppendLine($"  stage 4+          : {PadL(s4plus.ToString(),4)}  ({Pct(s4plus, total5F)})");
                sb.AppendLine();
            }

            // --- タイル踏破分布（イベント実遭遇数の検証用） ---
            // 1ランあたり、各タイル種別を実際に何回起動したか（再訪・消化済みは含まない）。
            // 「イベントを順に3回踏むのが難しい」仮説の真偽を実数で確認する。
            if (recs.Count > 0)
            {
                int runN = recs.Count;
                sb.AppendLine("---- タイル踏破分布（1ランあたり平均・実起動回数） ----");
                var order = new[]
                {
                    TileType.Battle, TileType.EliteBattle, TileType.Event, TileType.Mystery,
                    TileType.Shop, TileType.Treasure, TileType.Rest, TileType.Trap,
                };
                foreach (var tt in order)
                {
                    long sum = 0; int runsWithAny = 0;
                    foreach (var r in recs)
                    {
                        r.tileVisits.TryGetValue(tt, out int c);
                        sum += c;
                        if (c > 0) runsWithAny++;
                    }
                    double avg = (double)sum / runN;
                    sb.AppendLine($"  {PadR(tt.ToString(), 12)}: 平均 {avg:F2}/ラン  (総{sum}, 1回以上踏破 {Pct(runsWithAny, runN)})");
                }
                sb.AppendLine();
            }

            // --- 敵・ボス脅威度 ---
            // key = displayName。boss(enemyId が boss_layer*)は別エントリとして分離集計。
            sb.AppendLine("---- 敵・ボス脅威度 ----");
            var agg = new Dictionary<string, int[]>();    // key -> [0enc,1wins,2losses,3turns,4hpLost,5fatal, 6tWin,7tDraw,8tLoss,9tLossAbs]
            var isBossKey = new Dictionary<string, bool>(); // key -> boss か
            var dispName = new Dictionary<string, string>(); // key -> 表示名

            string KeyOf(CombatRec c)
            {
                bool boss = c.isBoss || (!string.IsNullOrEmpty(c.enemyId) && c.enemyId.StartsWith("boss_layer"));
                string nm = string.IsNullOrEmpty(c.enemy) ? (c.enemyId ?? "?") : c.enemy;
                string key = boss ? nm + "BOSS" : nm;
                isBossKey[key] = boss;
                dispName[key] = nm;
                return key;
            }

            foreach (var r in recs)
            {
                foreach (var c in r.combats)
                {
                    string key = KeyOf(c);
                    if (!agg.TryGetValue(key, out var a)) { a = new int[10]; agg[key] = a; }
                    a[0]++;
                    if (c.won) a[1]++; else a[2]++;
                    a[3] += c.turns;
                    a[4] += Math.Max(0, c.hpBefore - c.hpAfter);
                    a[6] += c.tWin; a[7] += c.tDraw; a[8] += c.tLoss; a[9] += c.tLossAbs;
                }
                // 致命: そのランの最後の「敗北」戦闘をゲームオーバー要因と見なす
                if (r.outcome == Outcome.GameOver && r.combats != null && r.combats.Count > 0)
                {
                    CombatRec fatal = null;
                    for (int i = r.combats.Count - 1; i >= 0; i--)
                        if (!r.combats[i].won) { fatal = r.combats[i]; break; }
                    if (fatal != null)
                    {
                        string key = KeyOf(fatal);
                        if (!agg.TryGetValue(key, out var a)) { a = new int[10]; agg[key] = a; }
                        a[5]++;
                    }
                }
            }

            var keys = new List<string>(agg.Keys);
            keys.Sort((x, y) =>
            {
                // 勝率の低い順（危険＝主指標）。同率は致命数の多い順→遭遇数の多い順。
                double wx = agg[x][0] == 0 ? 1.0 : (double)agg[x][1] / agg[x][0];
                double wy = agg[y][0] == 0 ? 1.0 : (double)agg[y][1] / agg[y][0];
                int c = wx.CompareTo(wy);
                if (c != 0) return c;
                int f = agg[y][5].CompareTo(agg[x][5]);
                return f != 0 ? f : agg[y][0].CompareTo(agg[x][0]);
            });
            sb.AppendLine($"  {PadR("敵名",18)}{PadR("種別",6)}{PadL("遭遇",5)} {PadL("勝率",6)} {PadL("平均T",7)} {PadL("平均被ダメ",11)} {PadL("致命",5)}");
            foreach (var k in keys)
            {
                var a = agg[k];
                string wr = a[0] == 0 ? "-" : (100.0*a[1]/a[0]).ToString("F0")+"%";
                string at = a[0] == 0 ? "-" : ((double)a[3]/a[0]).ToString("F1");
                string ad = a[0] == 0 ? "-" : ((double)a[4]/a[0]).ToString("F1");
                string nm = dispName.TryGetValue(k, out var dn2) ? dn2 : k;
                string kind = isBossKey.TryGetValue(k, out var b) && b ? "BOSS"
                            : (!string.IsNullOrEmpty(nm) && nm.StartsWith("精鋭")) ? "精鋭"
                            : "雑魚";
                sb.AppendLine($"  {PadR(TruncDisp(nm,16),18)}{PadR(kind,6)}{PadL(a[0].ToString(),5)} {PadL(wr,6)} {PadL(at,7)} {PadL(ad,11)} {PadL(a[5].ToString(),5)}");
            }
            sb.AppendLine();
            sb.AppendLine("（勝率の低い順にソート。致命=そのランをゲームオーバーに導いた回数）");
            sb.AppendLine();

            // --- ボス戦ターン内訳（非解決グラインドの原因特定） ---
            // 全ターンの勝/分/敗の比率＋「吸収敗(敗北だがメインダメ0で死回避)」を表示。
            // 99T級の長期戦＝勝or分or吸収敗ばかりで致命敗が稀、を定量化する。
            sb.AppendLine("---- ボス戦ターン内訳（勝/分/敗 ％・吸収敗=敗北だが被ダメ0） ----");
            sb.AppendLine($"  {PadR("ボス名",16)}{PadL("総T",6)} {PadL("勝%",6)} {PadL("分%",6)} {PadL("敗%",6)} {PadL("吸収敗%",8)}");
            foreach (var k in keys)
            {
                if (!(isBossKey.TryGetValue(k, out var b) && b)) continue;
                var a = agg[k];
                int tot = a[6] + a[7] + a[8];
                if (tot == 0) continue;
                string nm = dispName.TryGetValue(k, out var dn3) ? dn3 : k;
                string pw = (100.0*a[6]/tot).ToString("F0")+"%";
                string pd = (100.0*a[7]/tot).ToString("F0")+"%";
                string pl = (100.0*a[8]/tot).ToString("F0")+"%";
                string pa = a[8]==0 ? "-" : (100.0*a[9]/a[8]).ToString("F0")+"%";
                sb.AppendLine($"  {PadR(TruncDisp(nm,16),16)}{PadL(tot.ToString(),6)} {PadL(pw,6)} {PadL(pd,6)} {PadL(pl,6)} {PadL(pa,8)}");
            }
            sb.AppendLine("（吸収敗% = 敗北ターンのうちシールド吸収/無効化でメインダメ0だった割合。LS中はこれ＋勝＋分が生存ターン）");
            sb.AppendLine();

            // --- 6Fクリア時ビルドスナップショット（サルベージ用） ---
            var snaps = recs.FindAll(r => !string.IsNullOrEmpty(r.clear6FSnapshot));
            if (snaps.Count > 0)
            {
                sb.AppendLine($"---- 6Fクリア時ビルドスナップショット ({snaps.Count}件・サルベージ用) ----");
                foreach (var r in snaps)
                {
                    sb.AppendLine($"  ● RUN {r.index} [{r.bandLabel}]");
                    sb.AppendLine($"      {r.clear6FSnapshot}");
                }
                sb.AppendLine();
            }

            // --- デッドロック発生時の状況（原因特定用・最下段） ---
            var dls = recs.FindAll(r => r.band == "DEADLOCK");
            if (dls.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"---- デッドロック発生時の状況（{dls.Count}件・原因特定用） ----");
                foreach (var r in dls)
                {
                    sb.AppendLine($"  ● RUN {r.index} : {r.note}");
                    sb.AppendLine($"    reached F{r.reachedFloor}  HP {r.finalHP}/{r.finalMaxHP}  " +
                                  $"coins {r.finalCoins}  combats {r.totalWins}/{r.totalCombats}  " +
                                  $"LS={r.lastStandUsed}");
                    if (_detail.TryGetValue(r.index, out var lines) && lines.Count > 0)
                    {
                        const int tail = 40;
                        int start = Math.Max(0, lines.Count - tail);
                        sb.AppendLine($"    ── 発生時点周辺ログ（末尾 {lines.Count - start} 行 / 総 {lines.Count} 行） ──");
                        for (int i = start; i < lines.Count; i++)
                            sb.AppendLine($"    | {lines[i]}");
                    }
                    else
                    {
                        sb.AppendLine("    （ログ未取得）");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("=============================================");
            return sb.ToString();
        }

        // ===== 表示幅(全角=2)対応の整列ヘルパー =====

        private static int CharW(char ch)
        {
            if (ch < 0x1100) return 1;
            return
                (ch >= 0x1100 && ch <= 0x115F) ||                       // Hangul Jamo
                (ch >= 0x2E80 && ch <= 0xA4CF && ch != 0x303F) ||       // CJK..Yi
                (ch >= 0xAC00 && ch <= 0xD7A3) ||                       // Hangul Syllables
                (ch >= 0xF900 && ch <= 0xFAFF) ||                       // CJK Compat Ideographs
                (ch >= 0xFE30 && ch <= 0xFE4F) ||                       // CJK Compat Forms
                (ch >= 0xFF00 && ch <= 0xFF60) ||                       // Fullwidth Forms
                (ch >= 0xFFE0 && ch <= 0xFFE6)
                ? 2 : 1;
        }

        private static int DispWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            foreach (var c in s) w += CharW(c);
            return w;
        }

        /// <summary>表示幅で左詰めパディング。</summary>
        private static string PadR(string s, int w)
        {
            s = s ?? "";
            int d = DispWidth(s);
            return d >= w ? s : s + new string(' ', w - d);
        }

        /// <summary>表示幅で右詰めパディング。</summary>
        private static string PadL(string s, int w)
        {
            s = s ?? "";
            int d = DispWidth(s);
            return d >= w ? s : new string(' ', w - d) + s;
        }

        /// <summary>表示幅で切り詰め。</summary>
        private static string TruncDisp(string s, int w)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            int acc = 0;
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                int cw = CharW(c);
                if (acc + cw > w) break;
                sb.Append(c);
                acc += cw;
            }
            return sb.ToString();
        }

        private string BuildJsonl()
        {
            var sb = new StringBuilder();
            foreach (var r in _records)
                sb.AppendLine(JsonUtility.ToJson(r));
            return sb.ToString();
        }

        private string BuildDetail()
        {
            var sb = new StringBuilder();
            foreach (var r in _records)
            {
                sb.AppendLine($"########## RUN {r.index}  [{r.band} {r.bandLabel}]  {r.note}");
                sb.AppendLine($"# reached F{r.reachedFloor} HP {r.finalHP}/{r.finalMaxHP} coins {r.finalCoins} " +
                              $"combats {r.totalWins}/{r.totalCombats} LS={r.lastStandUsed}");
                if (_detail.TryGetValue(r.index, out var lines))
                    foreach (var l in lines) sb.AppendLine(l);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}

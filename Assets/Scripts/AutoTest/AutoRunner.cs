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
        [Tooltip("各ラン開始前にメタ恒久進行をリセット（ラン開始前パッシブボーナス0でバランス計測）")]
        public bool resetMetaForCleanBaseline = true;

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
            public string band = "";       // R1..R10 / CRASH / DEADLOCK
            public string bandLabel = "";
            public string note = "";
            public List<CombatRec> combats = new List<CombatRec>();
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

            Debug.Log($"[AutoRunner] バッチ開始: {runCount} ラン");

            for (int i = 0; i < runCount; i++)
            {
                yield return RunOne(i);
                yield return null; // フレーム譲り
            }

            gm.OnEnemyEncountered -= OnEnemyEncountered;
            gm.OnBattleEnded -= OnBattleEnded;
            gm.OnStarvationDamage -= OnStarvation;
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
            _cur = new RunRec { index = index };
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

            // ラン開始前パッシブボーナス0でのバランス計測: メタ恒久進行をリセット
            if (resetMetaForCleanBaseline)
            {
                try { MetaProgression.MetaProgressManager.Instance?.ResetAll(); }
                catch (Exception e) { Debug.LogWarning($"[AutoRunner] Meta reset: {e.Message}"); }
            }

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
                    gm.ConfirmBattleResult();
                    return false;

                case GameManager.GamePhase.Reward:
                    gm.ConfirmReward();
                    return false;

                case GameManager.GamePhase.RestStop:
                {
                    // 余裕がある(HP>60%)かつ素材が足りるなら武器強化、それ以外は回復
                    var run = gm.Run;
                    float hpR = run.playerMaxHP > 0 ? (float)run.playerHP / run.playerMaxHP : 1f;
                    int cost = GameManager.WeaponUpgradeCost(run.weaponUpgradeLevel);
                    if (hpR > 0.6f && run.weaponMaterials >= cost)
                        gm.RestUpgrade();
                    else
                        gm.RestHeal();
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

                case GameManager.GamePhase.SinRitual:
                    gm.OfferHpSacrifice(false);
                    gm.OfferGoldSacrifice(false);
                    gm.OfferItemSacrifice(false);
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

            // HPが半分以下なら所持消費アイテムで回復（過剰回復は避ける）
            while (Consumables.TryUseBestHeal(gm.Run, 0.5f)) { }

            var (fwd, lat) = mm.GetCategorizedMoves();
            var pool = (fwd != null && fwd.Count > 0) ? fwd : lat;
            if (pool == null || pool.Count == 0)
            {
                Finish(Outcome.Deadlock, "移動先なし(MapNavigation)");
                return true;
            }

            float hpRatio = gm.Run.playerMaxHP > 0
                ? (float)gm.Run.playerHP / gm.Run.playerMaxHP : 1f;

            MapNode best = pool[0];
            int bestRank = int.MaxValue;
            foreach (var n in pool)
            {
                int r = Rank(n.EffectiveType, hpRatio);
                if (r < bestRank) { bestRank = r; best = n; }
            }
            gm.MoveToNode(best.id);
            return false;
        }

        /// <summary>低いほど優先。HP帯(危機/低/健康)で重み分け。生存重視＋成長効率。</summary>
        private int Rank(TileType t, float hpRatio)
        {
            bool crit = hpRatio < 0.3f;   // 危機
            bool low  = hpRatio < 0.55f;  // 低HP
            switch (t)
            {
                case TileType.Rest:        return crit ? 0 : low ? 0 : 6;
                case TileType.Shop:        return crit ? 1 : 1;
                case TileType.Treasure:    return crit ? 2 : low ? 2 : 0;  // 装備強化源
                case TileType.Event:       return crit ? 5 : 3;
                case TileType.Mystery:     return crit ? 5 : 3;
                case TileType.Battle:      return crit ? 8 : low ? 5 : 2;
                case TileType.EliteBattle: return crit ? 9 : low ? 7 : 4;
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
                    // 通常ショップ: 余剰金を戦力へ。戦闘を避けないので装備/素材/回復を厚めに。
                    // 武器・ダイス（自動装備で強化見込み）
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > 40
                            && (inv.slots[i].kind == ShopSlotKind.Weapon
                                || inv.slots[i].kind == ShopSlotKind.Dice)) Buy(i);
                    // パッシブ（金が余るので貪欲に）
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > 25
                            && inv.slots[i].kind == ShopSlotKind.Passive) Buy(i);
                    // 武器強化素材: 次の強化に届くまで補充（価格倍々なので2個まで）
                    int needMat = GameManager.WeaponUpgradeCost(run.weaponUpgradeLevel);
                    int matBuys = 0;
                    while (run.weaponMaterials < needMat && run.coins > 20 && matBuys++ < 2
                           && BuyKind(ShopSlotKind.WeaponMaterial)) { }
                    // 回復消費を3個までストック
                    int stock = run.ownedConsumables != null ? run.ownedConsumables.Count : 0;
                    for (int i = 0; i < inv.slots.Count && stock < 3; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > 20
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
                // 緊急回復: 敵の最大ダイスダメージ(会心なら×2)で落ちそうなら回復
                var e = cm.CurrentEnemy;
                if (e != null)
                {
                    int maxHit = e.diceCount * e.diceMaxValue;
                    if (e.criticalNumerator > 0) maxHit *= 2;
                    if (cm.PlayerHP <= maxHit)
                        UseFirst(run, "cons_heal_4", "cons_heal_3", "cons_heal_2", "cons_heal_1");
                }
                cm.ExecuteTurn();
            }
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

            // ① 「立ち去る/無視」系があれば最優先（リスク0で確実に抜ける）
            for (int i = 0; i < def.choices.Count; i++)
            {
                var txt = def.choices[i]?.text ?? "";
                foreach (var kw in LeaveKeywords)
                    if (txt.Contains(kw)) return i;
            }
            // ② 危険語を含まない選択肢を選ぶ
            for (int i = 0; i < def.choices.Count; i++)
            {
                var txt = def.choices[i]?.text ?? "";
                bool danger = false;
                foreach (var kw in DangerKeywords)
                    if (txt.Contains(kw)) { danger = true; break; }
                if (!danger) return i;
            }
            // ③ 全て危険語入り → 先頭
            return 0;
        }

        // ===== 計測フック =====

        private void OnEnemyEncountered(EnemyData e)
        {
            var gm = GameManager.Instance;
            _pendingEnemyName = e != null ? e.displayName : "?";
            _pendingEnemyId = e != null && e.id != null ? e.id : "";
            // ボス判定は敵IDのみで厳密に行う（ノード種別フォールバックは誤検出の元）
            _pendingEnemyIsBoss = _pendingEnemyId.StartsWith("boss_layer");
            _pendingEnemyHpBefore = gm.Run?.playerHP ?? 0;
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
                afterLastStand = _cur.lastStandUsed
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
            Finish(full ? Outcome.FullClear : Outcome.NormalClear, full ? "完全クリア(6F)" : "通常クリア(5F)");
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

        /// <summary>結果を10段階バンド + CRASH/DEADLOCK に分類。</summary>
        private void Classify(RunRec r)
        {
            if (r.outcome == Outcome.Crash) { r.band = "CRASH"; r.bandLabel = "クラッシュ(例外)"; return; }
            if (r.outcome == Outcome.Deadlock) { r.band = "DEADLOCK"; r.bandLabel = "デッドロック"; return; }
            if (r.outcome == Outcome.FullClear) { r.band = "R10"; r.bandLabel = "6F完全クリア"; return; }
            if (r.outcome == Outcome.NormalClear) { r.band = "R8"; r.bandLabel = "5F通常クリア"; return; }

            // GameOver
            int f = r.deathFloor;
            if (r.reached6F) { r.band = "R9"; r.bandLabel = "6F挑戦するも死亡"; return; }
            switch (f)
            {
                case 1: r.band = "R1"; r.bandLabel = "1F道中で死亡"; break;
                case 2:
                    if (r.deathInBossFight) { r.band = "R3"; r.bandLabel = "2Fボスで死亡"; }
                    else { r.band = "R2"; r.bandLabel = "2F道中で死亡"; }
                    break;
                case 3: r.band = "R4"; r.bandLabel = "3Fで死亡"; break;
                case 4: r.band = "R5"; r.bandLabel = "4Fで死亡"; break;
                case 5:
                    if (r.deathInBossFight) { r.band = "R7"; r.bandLabel = "5Fボスで死亡"; }
                    else { r.band = "R6"; r.bandLabel = "5F道中/カルマで死亡"; }
                    break;
                default: r.band = "R1"; r.bandLabel = $"{f}F道中で死亡"; break;
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
            string dir = Path.Combine(root, $"batch_{stamp}_n{_records.Count}");
            Directory.CreateDirectory(dir);

            File.WriteAllText(Path.Combine(dir, "summary.txt"), BuildSummary(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "runs.jsonl"), BuildJsonl(), new UTF8Encoding(false));
            if (writeDetailLog)
                File.WriteAllText(Path.Combine(dir, "detail.log"), BuildDetail(), new UTF8Encoding(false));

            return dir;
        }

        private string Pct(int n, int total) =>
            total == 0 ? "0.0%" : (100.0 * n / total).ToString("F1", CultureInfo.InvariantCulture) + "%";

        private string BuildSummary()
        {
            int n = _records.Count;
            var sb = new StringBuilder();
            sb.AppendLine("================ AutoRun サマリ ================");
            sb.AppendLine($"日時      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ラン数    : {n}");
            sb.AppendLine($"行動方針  : 前進貪欲 + 生存重視");
            sb.AppendLine();

            // --- 10段階バンド分布 ---
            sb.AppendLine("---- 結果10段階分布（CRASH/DEADLOCK除く母数で割合算出） ----");
            string[] bands = { "R1","R2","R3","R4","R5","R6","R7","R8","R9","R10" };
            string[] labels = {
                "1F道中で死亡","2F道中で死亡","2Fボスで死亡","3Fで死亡","4Fで死亡",
                "5F道中/カルマで死亡","5Fボスで死亡","5F通常クリア","6F挑戦するも死亡","6F完全クリア" };
            int crash = _records.FindAll(r => r.band == "CRASH").Count;
            int dead  = _records.FindAll(r => r.band == "DEADLOCK").Count;
            int valid = n - crash - dead;
            for (int i = 0; i < bands.Length; i++)
            {
                int c = _records.FindAll(r => r.band == bands[i]).Count;
                sb.AppendLine($"  {PadR(bands[i],4)}{PadR(labels[i],22)}: {PadL(c.ToString(),4)}  ({Pct(c, valid)})");
            }
            sb.AppendLine();
            sb.AppendLine($"  {PadR("CRASH",4)}{PadR("クラッシュ(例外)",22)}: {PadL(crash.ToString(),4)}  ({Pct(crash, n)} of all)");
            sb.AppendLine($"  {PadR("DEAD",4)}{PadR("デッドロック",22)}: {PadL(dead.ToString(),4)}  ({Pct(dead, n)} of all)");
            sb.AppendLine();

            // --- 死亡階層・死因 ---
            sb.AppendLine("---- 死亡階層・死因 ----");
            var deaths = _records.FindAll(r => r.outcome == Outcome.GameOver);
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
            var ls = _records.FindAll(r => r.lastStandUsed);
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
            foreach (var r in _records)
            { sCoins+=r.finalCoins; sPeak+=r.peakCoins; sGain+=r.totalGoldGained;
              sStarv+=r.starvationTotal; sStarvHit+=r.starvationHits; sShop+=r.shopPurchases; }
            int dn = Math.Max(1, n);
            sb.AppendLine($"  {PadR("平均最終ゴールド", 20)}: {(sCoins/dn):F1}");
            sb.AppendLine($"  {PadR("平均ピークゴールド", 20)}: {(sPeak/dn):F1}");
            sb.AppendLine($"  {PadR("平均総獲得ゴールド", 20)}: {(sGain/dn):F1}");
            sb.AppendLine($"  {PadR("平均空腹ダメージ計", 20)}: {(sStarv/dn):F1}  (平均被弾 {(sStarvHit/dn):F1}回)");
            sb.AppendLine($"  {PadR("平均ショップ購入数", 20)}: {(sShop/dn):F2}");
            sb.AppendLine();

            // --- 敵・ボス脅威度 ---
            // key = displayName。boss(enemyId が boss_layer*)は別エントリとして分離集計。
            sb.AppendLine("---- 敵・ボス脅威度 ----");
            var agg = new Dictionary<string, int[]>();    // key -> [enc, wins, losses, turns, hpLost, fatal]
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

            foreach (var r in _records)
            {
                foreach (var c in r.combats)
                {
                    string key = KeyOf(c);
                    if (!agg.TryGetValue(key, out var a)) { a = new int[6]; agg[key] = a; }
                    a[0]++;
                    if (c.won) a[1]++; else a[2]++;
                    a[3] += c.turns;
                    a[4] += Math.Max(0, c.hpBefore - c.hpAfter);
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
                        if (!agg.TryGetValue(key, out var a)) { a = new int[6]; agg[key] = a; }
                        a[5]++;
                    }
                }
            }

            var keys = new List<string>(agg.Keys);
            keys.Sort((x, y) =>
            {
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
                string kind = isBossKey.TryGetValue(k, out var b) && b ? "BOSS" : "雑魚";
                string nm = dispName.TryGetValue(k, out var dn2) ? dn2 : k;
                sb.AppendLine($"  {PadR(TruncDisp(nm,16),18)}{PadR(kind,6)}{PadL(a[0].ToString(),5)} {PadL(wr,6)} {PadL(at,7)} {PadL(ad,11)} {PadL(a[5].ToString(),5)}");
            }
            sb.AppendLine();
            sb.AppendLine("（致命=そのランをゲームオーバーに導いた回数。脅威度の主指標）");
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

using CombatSystem;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// 自由移動の層 (2026-09-28・docs/GAME.md §5) の進行。 動く中身は素の C# の <see cref="FreeMapSim"/>
    /// (<see cref="MapManager.Sim"/>) で、 ここはゲームの規則 (到着したマスの起動・徘徊エネミーの連戦・
    /// 逃げる・補給庫・囮/罠) へ橋渡しする。
    ///
    /// <para><b>時間を進める口は <see cref="AdvanceFreeMap"/> だけ。</b> 人は <see cref="FreeMapClock"/>
    /// (停止 / 順速 / 早送り) が実時間に合わせて 1 刻みずつ呼び、 BOT は <see cref="TravelSync"/> で
    /// 同期的に進める。 どちらも同じ 0.1 手番刻みを踏むので、 同じ操作なら同じ結果になる。</para>
    /// </summary>
    public partial class GameManager
    {
        /// <summary>1 回の同期移動で進める刻みの上限。 層の対角 (約 13) を忍び足 (0.05/刻み) で歩いても 260 刻み。</summary>
        public const int MaxTravelTicks = 2000;

        /// <summary>自由移動の層の中身。 Λ・8 層・マップ外では null。</summary>
        public FreeMapSim FreeMap => MapManager.Instance != null && MapManager.Instance.IsFreeMap ? MapManager.Instance.Sim : null;

        /// <summary>刻みごとに起きたこと (None は流さない)。 表示・効果音・計器が購読する。</summary>
        public event System.Action<FreeMapEvent> OnFreeMapEvent;

        // ── 徘徊エネミーの連戦 ──
        private bool _inGauntlet;
        private int _gauntletFightIndex;
        private int _gauntletCount;
        /// <summary>徘徊エネミーの連戦の途中か。</summary>
        public bool InGauntletCombat => _inGauntlet;
        /// <summary>連戦の何戦目か (0 起点) と、 全部で何戦か。</summary>
        public int GauntletFightIndex => _gauntletFightIndex;
        public int GauntletFightTotal => FreeMap?.GauntletFights ?? 0;

        // =====================================================================
        //  移動
        // =====================================================================

        /// <summary>行き先を決める (移動中なら変える)。 時間は進めない。
        /// <paramref name="runSyncIfNoClock"/> が true で人の時計が無ければ、 着くまで同期的に進める
        /// (旧来の UI・デバッグキー向け)。</summary>
        public bool TravelTo(string nodeId, bool runSyncIfNoClock = false)
        {
            if (CurrentPhase != GamePhase.MapNavigation) return false;
            var mm = MapManager.Instance;
            if (mm == null || !mm.IsFreeMap) return false;
            if (!mm.BeginTravel(nodeId))
            {
                Log("そこへは行けない (山岳を横切る・偵察中・連戦中)");
                return false;
            }
            if (runSyncIfNoClock && FreeMapClock.Active == null)
                AdvanceFreeMap(MaxTravelTicks, FreeMapEvent.Arrived | FreeMapEvent.Caught | FreeMapEvent.Died);
            return true;
        }

        /// <summary>BOT 用: 行き先を決めて、 着く・捕まる・倒れる・<paramref name="stopMask"/> のどれかまで同期的に進める。
        /// 行けなければ None。</summary>
        public FreeMapEvent TravelSync(string nodeId, FreeMapEvent stopMask = FreeMapEvent.None, int maxTicks = MaxTravelTicks)
        {
            if (!TravelTo(nodeId)) return FreeMapEvent.None;
            return AdvanceFreeMap(maxTicks, stopMask | FreeMapEvent.Arrived | FreeMapEvent.Caught | FreeMapEvent.Died);
        }

        /// <summary>時間を進める唯一の口。 最大 <paramref name="maxTicks"/> 刻み、 <paramref name="stopMask"/> のどれかが
        /// 起きるか、 フェーズがマップから外れる (マスの起動・連戦・死亡) まで進める。 起きたことの和を返す。</summary>
        public FreeMapEvent AdvanceFreeMap(int maxTicks, FreeMapEvent stopMask = FreeMapEvent.None)
        {
            var acc = FreeMapEvent.None;
            var sim = FreeMap;
            if (sim == null) return acc;
            for (int i = 0; i < maxTicks; i++)
            {
                if (CurrentPhase != GamePhase.MapNavigation || sim.Dead || sim.InGauntlet) break;
                var ev = sim.Tick();
                acc |= ev;
                if (ev != FreeMapEvent.None) OnFreeMapEvent?.Invoke(ev);
                HandleFreeMapEvent(sim, ev);
                if ((ev & stopMask) != 0) break;
            }
            MapManager.Instance?.SyncFreeKnowledge();
            return acc;
        }

        private void HandleFreeMapEvent(FreeMapSim sim, FreeMapEvent ev)
        {
            if (ev == FreeMapEvent.None) return;
            if ((ev & FreeMapEvent.Detected) != 0) Log("（どこかで何かがこちらを嗅ぎつけた）");
            if ((ev & FreeMapEvent.FoeSighted) != 0) Log("徘徊エネミーが目の前に見えた");
            if ((ev & FreeMapEvent.TrapTriggered) != 0) Log($"（罠が作動した）徘徊エネミーの位置が分かった。{FreeMapParams.TrapFreezeTurns} 手番足止め");
            if ((ev & FreeMapEvent.DecoyBroken) != 0) Log("（囮が壊された）");

            if ((ev & FreeMapEvent.Died) != 0)
            {
                Log("物資が尽き、歩き続けた末に倒れた");
                Run.EndRun();
                SetPhase(GamePhase.GameOver);
                OnGameOver?.Invoke(Run);
                return;
            }
            if ((ev & FreeMapEvent.Caught) != 0)
            {
                BeginGauntlet();
                return;
            }
            if ((ev & FreeMapEvent.Arrived) != 0)
            {
                var node = MapManager.Instance.NoteFreeArrival(sim.LastArrived);
                if (node == null) return;
                BeforeNodeEntered();
                AfterNodeEntered();
            }
        }

        // =====================================================================
        //  速さ・索敵・囮/罠
        // =====================================================================

        /// <summary>移動の速さ (忍び足 / 通常 / 急ぐ)。 移動中も切り替えられる。</summary>
        public void SetTravelSpeed(MoveSpeed speed)
        {
            var sim = FreeMap;
            if (sim != null) sim.Speed = speed;
        }

        /// <summary>照明の点灯・消灯。 点灯中は物資を消費し続け、 察知距離は 5 で固定。</summary>
        public void SetFlare(bool on) => FreeMap?.SetFlare(on);

        /// <summary>偵察 (止まっている時だけ)。 2 手番動けない。</summary>
        public bool StartScout() => FreeMap != null && CurrentPhase == GamePhase.MapNavigation && FreeMap.StartScout();

        /// <summary>囮を今いる場所に置く (消耗品の消費は <see cref="Consumables.Use"/> が行う)。</summary>
        public bool TryPlaceDecoy()
        {
            var sim = FreeMap;
            if (sim == null || CurrentPhase != GamePhase.MapNavigation || sim.InGauntlet) return false;
            sim.PlaceDecoy();
            Log($"囮を置いた（{FreeMapParams.DecoyDelay} 手番後から {FreeMapParams.DecoyTurns} 手番鳴る）");
            return true;
        }

        /// <summary>罠を今いる場所に仕掛ける (消耗品の消費は <see cref="Consumables.Use"/> が行う)。</summary>
        public bool TryPlaceTrap()
        {
            var sim = FreeMap;
            if (sim == null || CurrentPhase != GamePhase.MapNavigation || sim.InGauntlet) return false;
            sim.PlaceTrap();
            Log(sim.AtNode >= 0 || sim.Segment.a >= 0 ? "罠を仕掛けた" : "罠を仕掛けた（道の外なので敵が踏むことはまず無い）");
            return true;
        }

        /// <summary>開始時の囮・罠 (1 個ずつ)。 StartNewRun から呼ぶ。</summary>
        private void GrantStartingMapTools()
        {
            for (int i = 0; i < FreeMapParams.StartDecoys; i++) Run.TryAddConsumable(ItemIds.Decoy);
            for (int i = 0; i < FreeMapParams.StartTraps; i++) Run.TryAddConsumable(ItemIds.Trap);
        }

        // =====================================================================
        //  補給庫
        // =====================================================================

        /// <summary>補給庫 (拠) を漁る: 物資 +200。 物音 (半径 3) で近くの徘徊エネミーを呼ぶ。 気配は上げない。</summary>
        private void LootSupplyCache()
        {
            int before = Run.provision;
            ProvisionSystem.Supply(Run, FreeMapParams.RewardCache);
            FreeMap?.NoteCacheLooted();
            Log($"補給庫を漁った。物資 +{Run.provision - before}（物音を立てた）");
            SetPhase(GamePhase.MapNavigation);
        }

        // =====================================================================
        //  徘徊エネミーの連戦
        // =====================================================================

        /// <summary>捕まった: 次の層のエリート (7 層は 7 層のエリート) との連戦。 3 層 2 戦・5 層以降 3 戦。</summary>
        private void BeginGauntlet()
        {
            _inGauntlet = true;
            _gauntletFightIndex = 0;
            _gauntletCount++;
            Log($"徘徊エネミーに捕まった ── {GauntletFightTotal} 連戦");
            StartGauntletFight();
        }

        private void StartGauntletFight()
        {
            int enemyFloor = FloorPlan.GauntletEnemyFloor(Run.currentFloor);
            // 添字: 層ごとの連戦の通し番号 × 8 ＋ 何戦目。 ノードの添字 (NodeRngIndex) と重ならない帯へ置く。
            int idx = Run.currentFloor * 4096 + 2048 + _gauntletCount * 8 + _gauntletFightIndex;
            var enemy = FloorManager.PickEnemyForNode(enemyFloor, idx, elite: true)?.Clone();
            if (enemy == null)
            {
                Debug.LogError("[GameManager] 連戦の敵の選出に失敗 ── 連戦を打ち切る");
                _inGauntlet = false;
                FreeMap?.ResolveGauntletWon();
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            CurrentEnemy = enemy;
            CurrentEnemySecondary = null;
            CurrentEncounterRewardMultiplier = 1f;
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"連戦 {_gauntletFightIndex + 1}/{GauntletFightTotal}: {CurrentEnemy.displayName}");

            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
            CombatManager.Instance.FleeAllowed = true;
        }

        /// <summary>連戦の 1 戦に勝った: 次の 1 戦へ / 勝ち切ったら物資 +300 とアイテム 1 個。</summary>
        private void HandleGauntletFightWon()
        {
            _gauntletFightIndex++;
            if (_gauntletFightIndex < GauntletFightTotal)
            {
                Log($"連戦 {_gauntletFightIndex}/{GauntletFightTotal} を切り抜けた (HP {Run.playerHP}/{Run.playerMaxHP})");
                StartGauntletFight();
                return;
            }
            _inGauntlet = false;
            FreeMap?.ResolveGauntletWon();
            int before = Run.provision;
            ProvisionSystem.Supply(Run, FreeMapParams.RewardGauntlet);
            string pid = PickPassiveItemForBossExtra(false);
            if (!string.IsNullOrEmpty(pid)) GrantRewardItem(pid, eliteWin: true);
            Log($"徘徊エネミーを退けた。物資 +{Run.provision - before}" + (string.IsNullOrEmpty(pid) ? "" : $"、{pid}"));
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>連戦から抜けた (逃げた・救済で生き延びた): 2 手番後から追跡が始まる。</summary>
        private void EndGauntletAsFled(bool payCost)
        {
            _inGauntlet = false;
            if (payCost) PayFleeCost();
            FreeMap?.ResolveGauntletFled();
            MapManager.Instance?.SyncCurrentNodeFromSim();
            Log($"連戦から逃げた。{FreeMapParams.FleeFreezeTurns} 手番後に追跡が始まる");
        }

        // =====================================================================
        //  逃げる
        // =====================================================================

        /// <summary>逃げるの代償: 物資 −100 ＋ ゴールド半減。 報酬は無い。</summary>
        private void PayFleeCost()
        {
            var (p, g) = ProvisionRules.ApplyFlee(Run.provision, Run.coins);
            Log($"逃げた代償: 物資 {Run.provision}→{p}、ゴールド {Run.coins}→{g}");
            Run.provision = p;
            Run.coins = g;
        }

        /// <summary>戦闘から逃げた。 通常の戦闘なら直前の点へ戻り、 そのマスは使い切りにならない (敵が残る)。</summary>
        private void HandleFledBattle()
        {
            if (_inGauntlet)
            {
                EndGauntletAsFled(payCost: true);
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            PayFleeCost();
            var mm = MapManager.Instance;
            var node = mm?.CurrentNode;
            if (node != null) node.activated = false;   // 使い切りにならない
            var sim = FreeMap;
            if (sim != null)
            {
                sim.NoteBattleEnded();                    // 戦闘の物音は立った
                sim.FleeToPrevious();
                mm.SyncCurrentNodeFromSim();
            }
            Log($"{CurrentEnemy?.displayName ?? "敵"}から逃げた。直前の点へ戻った（敵は残っている）");
            SetPhase(GamePhase.MapNavigation);
        }
    }
}

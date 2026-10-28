using CombatSystem;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// 航行マップ (手番制・2026-10-18・docs/GAME.md §5) の進行。 動く中身は素の C# の <see cref="FreeMapSim"/>
    /// (<see cref="MapManager.Sim"/>) で、 ここはゲームの規則 (到着した点の起動・追跡者との戦闘・
    /// 逃げる・囮罠・休憩) へ橋渡しする。
    ///
    /// <para><b>時間を進める口は「1 歩動く」「待つ」「偵察」「休憩で多めに回復」だけ。</b> どれも
    /// <see cref="FreeMapSim"/> の操作 1 回が 1 手番ぶん (雪の前線の内側は 2 手番) で、
    /// 戦闘・イベント・照明・囮罠は手番を使わない。 人も BOT も同じ口を同期的に叩く。</para>
    /// </summary>
    public partial class GameManager
    {
        /// <summary>true の間は、 点に着いても戦闘・イベント・ショップなどの旧画面へ入らない (航行だけを試すため)。 2D 画面 (Game2DMapView) が立てる。</summary>
        public static bool NavOnly;

        /// <summary>航行マップの中身。 8 層・マップ外では null。</summary>
        public FreeMapSim FreeMap => MapManager.Instance != null && MapManager.Instance.IsFreeMap ? MapManager.Instance.Sim : null;

        /// <summary>操作ごとに起きたこと (None は流さない)。 表示・効果音・計器が購読する。</summary>
        public event System.Action<FreeMapEvent> OnFreeMapEvent;

        /// <summary>航行の画面演出の合図 (偵察・照明)。 手番を進めない照明や、 イベントを返さない偵察でも鳴る。 Game2DMapFx が購読する。</summary>
        public enum NavFx { Recon, Flare }
        public event System.Action<NavFx> OnNavFx;

        // ── 追跡者との戦闘 ──
        private bool _inPursuerFight;
        private int _pursuerFightCount;
        /// <summary>追跡者 (本体) に捕まって戦闘中か。</summary>
        public bool InPursuerCombat => _inPursuerFight;

        // ── 使い魔との戦闘 ──
        private bool _inFamiliarFight;
        private int _familiarFightCount;
        /// <summary>使い魔との戦闘中か。</summary>
        public bool InFamiliarCombat => _inFamiliarFight;
        /// <summary>マスに紐づかない戦闘 (追跡者・使い魔) の途中か。 マスの報酬・ボス判定を通さない。</summary>
        public bool InMapEncounterCombat => _inPursuerFight || _inFamiliarFight;

        // =====================================================================
        //  操作 (手番を進める)
        // =====================================================================

        /// <summary>隣の点へ 1 歩動く。 行けない (道が無い・沈んだ・戦闘中) なら false。</summary>
        public bool MoveStep(string nodeId)
        {
            var sim = FreeMapForAction();
            if (sim == null) return false;
            var node = MapManager.Instance.CurrentMap.GetNode(nodeId);
            if (node == null || node.index < 0 || !sim.CanMoveTo(node.index))
            {
                Log("そこへは行けない（道がつながっていない）");
                return false;
            }
            RunFreeMapOp(s => s.MoveTo(node.index));
            return true;
        }

        /// <summary>待つ (1 手番)。</summary>
        public bool WaitTurn()
        {
            var sim = FreeMapForAction();
            if (sim == null) return false;
            Log("待つ（1手番）");
            RunFreeMapOp(s => s.Wait());
            return true;
        }

        /// <summary>偵察 (1 手番): 道で 2 歩先まで見える。</summary>
        public bool ReconTurn()
        {
            var sim = FreeMapForAction();
            if (sim == null) return false;
            Log("偵察（1手番）");
            RunFreeMapOp(s => s.Recon());
            OnNavFx?.Invoke(NavFx.Recon);
            return true;
        }

        /// <summary>照明 (手番は使わない): 物資 −50 で 3 手番のあいだ道で 3 歩先まで見える。</summary>
        public bool UseFlare()
        {
            var sim = FreeMapForAction();
            if (sim == null || sim.FlareOn) return false;
            Log($"照明（物資 −{FreeMapParams.FlareSupply}・手番は使わない・{FreeMapParams.FlareTurns}手番のあいだ見える）");
            RunFreeMapOp(s => s.UseFlare());
            OnNavFx?.Invoke(NavFx.Flare);
            return true;
        }

        /// <summary>囮罠を今いる点に仕掛ける (手番は使わない・消耗品の消費は <see cref="Consumables.Use"/> が行う)。</summary>
        public bool TryPlaceLure()
        {
            var sim = FreeMapForAction();
            if (sim == null) return false;
            Log($"囮罠を仕掛けた（{FreeMapParams.LureLife}手番のあいだ有効・踏んだ追跡者は{FreeMapParams.LureHoldTurns}手番足止めされる）");
            RunFreeMapOp(s => s.PlaceLure());
            return true;
        }

        /// <summary>手番を進められる状態の中身 (マップ移動中・戦闘中でない)。 そうでなければ null。</summary>
        private FreeMapSim FreeMapForAction()
        {
            if (CurrentPhase != GamePhase.MapNavigation) return null;
            var sim = FreeMap;
            return sim == null || sim.Dead || sim.InFight ? null : sim;
        }

        /// <summary>操作を 1 回実行し、 起きたことを処理する。</summary>
        private FreeMapEvent RunFreeMapOp(System.Func<FreeMapSim, FreeMapEvent> op)
        {
            var sim = FreeMap;
            if (sim == null) return FreeMapEvent.None;
            var ev = op(sim);
            MapManager.Instance?.SyncFreeKnowledge();
            if (ev != FreeMapEvent.None) OnFreeMapEvent?.Invoke(ev);
            HandleFreeMapEvent(sim, ev);
            return ev;
        }

        private void HandleFreeMapEvent(FreeMapSim sim, FreeMapEvent ev)
        {
            if (ev == FreeMapEvent.None) return;
            var pf = sim.Profile;
            if ((ev & FreeMapEvent.Detected) != 0) Log($"〈{pf.displayName}〉に補足された");
            if ((ev & FreeMapEvent.FoeLost) != 0) Log($"〈{pf.displayName}〉は見失った");
            if ((ev & FreeMapEvent.FoeAlerted) != 0) Log("（追跡者が何かに気づいた）");
            if ((ev & FreeMapEvent.FamiliarLaunched) != 0) Log("使い魔が放たれた");
            if ((ev & FreeMapEvent.FamiliarVanished) != 0) Log("（使い魔は見失って消えた）");
            if ((ev & FreeMapEvent.LureTriggered) != 0) Log($"（囮罠が作動した）追跡者が{FreeMapParams.LureHoldTurns}手番足止めされた");
            if ((ev & FreeMapEvent.FrontAdvanced) != 0 && sim.Weather != WeatherKind.None)
                Log($"前線が左から {System.Math.Min(sim.Front, FreeMapParams.Width):0.0} まで届いた（{sim.Weather.DisplayName()}）");
            if ((ev & FreeMapEvent.MapChanged) != 0) Log(sim.Weather == WeatherKind.Crumble ? "（道が1本崩れた）" : "（点が1つ沈んだ）");

            if ((ev & FreeMapEvent.Died) != 0)
            {
                Log("物資が尽き、歩き続けた末に倒れた");
                Run.EndRun();
                SetPhase(GamePhase.GameOver);
                OnGameOver?.Invoke(Run);
                return;
            }
            if (NavOnly)
            {
                // 航行だけを試す (2D 化の途中: 戦闘・イベント・ショップなど旧画面へは入らない)。 接触は無かったことにし、 着いた点は「到着」の記録だけ残す。
                if ((ev & FreeMapEvent.FamiliarContact) != 0)
                {
                    sim.ResolveFamiliarFight(false); MapManager.Instance?.SyncCurrentNodeFromSim(); Log("（航行のみ）使い魔に接触 ── 戦闘は省略した");
                    // 着いた手番に接触していても、 着いた点はそのまま (終点なら次の層へ進めるように、 到着の処理へ続ける)
                    if ((ev & FreeMapEvent.Arrived) == 0) return;
                    if ((ev & FreeMapEvent.Caught) == 0) sim.TakePendingArrival();
                }
                if ((ev & FreeMapEvent.Caught) != 0)
                {
                    if ((ev & FreeMapEvent.Arrived) != 0 && sim.LastArrived == sim.Layout.goal)
                    {
                        // 終点に着いた手番に捕まった: 戦闘は省いて勝ったことにし、 そのまま到着の処理へ (追い返され続けて終点へ入れなくなるのを防ぐ)
                        sim.ResolveBodyWon(); sim.TakePendingArrival(); Log("（航行のみ）終点で追跡者に捕まった ── 戦闘は省略し、 退けたことにした");
                    }
                    else { sim.ResolveBodyFled(); MapManager.Instance?.SyncCurrentNodeFromSim(); Log("（航行のみ）追跡者に捕まった ── 戦闘は省略し、 直前の点へ戻った"); return; }
                }
                if ((ev & FreeMapEvent.Arrived) != 0)
                {
                    var arrived = MapManager.Instance.NoteFreeArrival(sim.LastArrived);
                    if (arrived != null) Log($"（航行のみ）到着: {arrived.type}");
                    // 終点 (ボス / 〈門〉) に着いたら、 戦闘・エンディングを省いて次の層へ進む
                    if (arrived != null && (arrived.type == TileType.Boss || arrived.type == TileType.Gate)) AdvanceFloorNavOnly();
                }
                return;
            }
            if ((ev & FreeMapEvent.FamiliarContact) != 0) { StartFamiliarFight(); return; }
            if ((ev & FreeMapEvent.Caught) != 0) { BeginPursuerFight(); return; }
            if ((ev & FreeMapEvent.Arrived) != 0) ActivateArrival(sim.LastArrived);
        }

        /// <summary>航行だけを試す時 (NavOnly) の層移動: 終点に着いたら次の航行の層へ。 ボス戦・エンディングは省く。
        /// 次が航行マップを持たない層 (8 層 = ボス戦だけ) なら、 そこで止める。</summary>
        private void AdvanceFloorNavOnly()
        {
            int next = MapSystem.FreeMove.FloorPlan.Next(Run.Depth);
            if (next == Run.Depth || !MapSystem.FreeMove.FloorPlan.IsFreeMap(next))
            {
                Log($"（航行のみ）{Run.currentFloor}層の終点に着いた ── 最終層なのでここまで (ボス戦は未接続)");
                return;
            }
            Log($"（航行のみ）{Run.currentFloor}層の終点 → {MapSystem.FreeMove.FloorPlan.OrdinalOf(next)}層へ");
            Run.bossDefeatedThisFloor = true;
            SetPhase(GamePhase.FloorClear);
            ConfirmFloorClear();
        }

        /// <summary>着いた点を起動する。</summary>
        private void ActivateArrival(int index)
        {
            var node = MapManager.Instance.NoteFreeArrival(index);
            if (node == null) return;
            BeforeNodeEntered();
            AfterNodeEntered();
        }

        /// <summary>点に着いた手番に捕まっていたら、 戦闘が終わった今その点を発動させる。
        /// 放っておくと、 着いたのに起動しない点ができる。</summary>
        private void FlushPendingArrival()
        {
            var sim = FreeMap;
            if (CurrentPhase != GamePhase.MapNavigation || sim == null) return;
            int p = sim.TakePendingArrival();
            if (p >= 0) ActivateArrival(p);
        }

        /// <summary>開始時の囮罠 (<see cref="FreeMapParams.StartLures"/> 個)。 StartNewRun から呼ぶ。</summary>
        private void GrantStartingMapTools()
        {
            for (int i = 0; i < FreeMapParams.StartLures; i++) Run.TryAddConsumable(ItemIds.Lure);
        }

        // =====================================================================
        //  休憩 (多めに回復 = +1 手番)
        // =====================================================================

        /// <summary>休憩で多めに回復する: 最大 HP の <see cref="FreeMapParams.RestMoreRatio"/>、 代わりに
        /// <see cref="FreeMapParams.RestMoreTurns"/> 手番が進む (追跡者が動く)。 通常の回復 (<see cref="RestHeal"/>) は 0 手番。</summary>
        public void RestHealMore()
        {
            if (CurrentPhase != GamePhase.RestStop) return;
            int heal = Mathf.CeilToInt(Run.playerMaxHP * FreeMapParams.RestMoreRatio
                                       * MetaProgression.MetaDebuffApplicator.GetHealMultiplier());
            heal = MetaProgression.MetaDebuffApplicator.ApplyJudgmentHealReduction(heal, Run);
            int before = Run.playerHP;
            Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
            Run.playerHP = MetaProgression.MetaDebuffApplicator.ApplyHealSpotCap(Run.playerHP, Run.playerMaxHP, before);
            MetaProgression.MetaDebuffApplicator.NoteHeal(heal, Run.playerHP - before, Run);
            RunChronicle.Rest(Run, Run.playerHP - before, before, Run.playerMaxHP);
            Log($"休憩（多めに回復）: +{Run.playerHP - before}HP（{FreeMapParams.RestMoreTurns}手番）");

            // 休憩の画面を閉じてから手番を進める。 捕まれば戦闘へ、 そうでなければ地図 (ボス撃破後は層クリア) へ戻る。
            ReturnToMapOrClearFloor();
            if (CurrentPhase == GamePhase.MapNavigation && FreeMap != null)
                RunFreeMapOp(s => s.PassTurnsExternal(FreeMapParams.RestMoreTurns));
        }

        // =====================================================================
        //  追跡者との戦闘
        // =====================================================================

        /// <summary>追跡者の専用エネミー (単独の 1 戦)。 その層の次の層のエリート (7 層は 7 層自身) を土台に、
        /// 追跡者の性質で名前と強さを変える。 「重い」性質は最大 HP が大きい。</summary>
        private EnemyData BuildPursuerEnemy(FreeMapSim sim)
        {
            int enemyFloor = Run.Depth >= 7 ? 7 : FloorPlan.Next(Run.Depth);
            // 添字: 層ごとの捕まった回数 × 8。 ノードの添字 (NodeRngIndex) と重ならない帯へ置く。
            int idx = Run.Depth * 4096 + 2048 + _pursuerFightCount * 8;
            var template = FloorManager.PickEnemyForNode(enemyFloor, idx, elite: true);
            if (template == null) return null;
            var e = template.Clone();
            e.displayName = $"〈{sim.Profile.displayName}〉{template.displayName}";
            if (sim.Profile.heavyFight) e.maxHP = Mathf.Max(1, Mathf.RoundToInt(e.maxHP * PursuerHeavyHpMul));
            return e;
        }

        /// <summary>「重い」追跡者の最大 HP 倍率 (仮置き)。</summary>
        public const float PursuerHeavyHpMul = 1.4f;

        private void BeginPursuerFight()
        {
            var sim = FreeMap;
            _inPursuerFight = true;
            _pursuerFightCount++;
            var enemy = BuildPursuerEnemy(sim);
            if (enemy == null)
            {
                Debug.LogError("[GameManager] 追跡者の敵の選出に失敗 ── 戦闘を打ち切る");
                _inPursuerFight = false;
                sim?.ResolveBodyWon();
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            CurrentEnemy = enemy;
            CurrentEnemySecondary = null;
            CurrentEncounterRewardMultiplier = 1f;
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"追跡者に捕まった ── {CurrentEnemy.displayName}");

            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
            CombatManager.Instance.FleeAllowed = true;
        }

        /// <summary>追跡者に勝った: 追跡者は倒れ、 物資 +300 とアイテム 1 個。</summary>
        private void HandlePursuerWon()
        {
            _inPursuerFight = false;
            FreeMap?.ResolveBodyWon();
            int before = Run.provision;
            ProvisionSystem.Supply(Run, FreeMapParams.RewardPursuer);
            string pid = PickPassiveItemForBossExtra(false);
            if (!string.IsNullOrEmpty(pid)) GrantRewardItem(pid, eliteWin: true);
            Log($"追跡者を退けた。物資 +{Run.provision - before}" + (string.IsNullOrEmpty(pid) ? "" : $"、{pid}"));
            SetPhase(GamePhase.MapNavigation);
            FlushPendingArrival();
        }

        /// <summary>追跡者の戦闘から抜けた (逃げた・救済で生き延びた): 直前の点へ戻り、 追跡者は 2 手番足止めされる。</summary>
        private void EndPursuerFightAsFled(bool payCost)
        {
            _inPursuerFight = false;
            if (payCost) PayFleeCost();
            FreeMap?.ResolveBodyFled();
            MapManager.Instance?.SyncCurrentNodeFromSim();
            Log($"追跡者から逃げた。直前の点へ戻った（{FreeMapParams.FleeFreezeTurns}手番は追ってこない）");
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

        // =====================================================================
        //  使い魔との戦闘
        // =====================================================================

        /// <summary>使い魔に接触した: その層の通常の戦闘相手 1 体との戦闘 (仮置き)。 逃げられる。</summary>
        private void StartFamiliarFight()
        {
            _inFamiliarFight = true;
            _familiarFightCount++;
            int idx = Run.Depth * 4096 + 3072 + _familiarFightCount;
            var enemy = FloorManager.PickEnemyForNode(Run.Depth, idx, elite: false)?.Clone();
            if (enemy == null)
            {
                Debug.LogError("[GameManager] 使い魔の敵の選出に失敗 ── 戦闘を打ち切る");
                EndFamiliarFight(fled: false);
                return;
            }
            enemy.displayName = $"使い魔（{enemy.displayName}）";
            CurrentEnemy = enemy;
            CurrentEnemySecondary = null;
            CurrentEncounterRewardMultiplier = 1f;
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"使い魔に捕捉された: {CurrentEnemy.displayName}");
            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
            CombatManager.Instance.FleeAllowed = true;
        }

        /// <summary>使い魔との戦闘が終わった (勝っても逃げても使い魔は消える。 猟犬は本体の元へ戻る)。</summary>
        private void EndFamiliarFight(bool fled)
        {
            _inFamiliarFight = false;
            FreeMap?.ResolveFamiliarFight(fled);
            MapManager.Instance?.SyncCurrentNodeFromSim();
            SetPhase(GamePhase.MapNavigation);
            FlushPendingArrival();
        }

        /// <summary>使い魔に勝った: 通常の戦闘と同じ物資 (+30)。</summary>
        private void HandleFamiliarFightWon()
        {
            ProvisionSystem.Supply(Run, FreeMapParams.RewardBattle);
            Log("使い魔を退けた");
            EndFamiliarFight(fled: false);
        }

        /// <summary>戦闘から逃げた。 通常の戦闘なら直前の点へ戻り、 そのマスは使い切りにならない (敵が残る)。</summary>
        private void HandleFledBattle()
        {
            if (_inFamiliarFight)
            {
                PayFleeCost();
                Log("使い魔から逃げた");
                EndFamiliarFight(fled: true);
                return;
            }
            if (_inPursuerFight)
            {
                EndPursuerFightAsFled(payCost: true);
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
                sim.FleeToPrevious();
                mm.SyncCurrentNodeFromSim();
            }
            Log($"{CurrentEnemy?.displayName ?? "敵"}から逃げた。直前の点へ戻った（敵は残っている）");
            SetPhase(GamePhase.MapNavigation);
        }
    }
}

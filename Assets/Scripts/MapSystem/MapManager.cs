using System;
using System.Collections.Generic;
using MapSystem.FreeMove;
using UnityEngine;

namespace MapSystem
{
    /// <summary>
    /// マップの生成・ナビゲーション・タイル起動を管理するシングルトン。
    /// GameManager から呼ばれ、移動・空腹度・?マス解決を処理する。
    ///
    /// <para><b>2026-09-28: 自由移動の層</b>（1/3/5/6/7）では動く中身を素の C# の <see cref="FreeMapSim"/> が持ち、
    /// ここはその窓口になる（物資と HP の支払いを RunState へ橋渡しする・着いた点を <see cref="CurrentNode"/> に写す）。
    /// 時間を進めるのは GameManager（<c>AdvanceFreeMap</c>）で、 人の時計は <c>FreeMapClock</c>。
    /// Λ 環状線と 8 層（ボス戦だけ）は離散のまま <see cref="MoveTo"/> で動く。</para>
    /// </summary>
    public class MapManager : MonoBehaviour
    {
        private static MapManager _instance;
        private static bool _shuttingDown;

        /// <summary>マップ進行の単一窓口。 アプリ終了中は null を返す。
        ///
        /// **getter に FindObjectOfType の復帰口がある理由 (2026-08-05):**
        /// 再生中のスクリプト再コンパイルはドメインリロードで static だけを初期化し、
        /// シーン上の GameObject は残す。 すると Awake が二度と走らず Instance だけが
        /// 永久に null になり、 AutoRunner のマップ航行が進行不能のまま空回りする
        /// (同日 Λ スイープが 200 秒間 1 ランも進まず停止した)。
        /// GameManager と同じ CLAUDE.md のシングルトン規約に揃える。</summary>
        public static MapManager Instance
        {
            get
            {
                if (_shuttingDown) return null;
                if (_instance == null) _instance = FindObjectOfType<MapManager>();
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

        // === 状態 ===
        public FloorMap CurrentMap { get; private set; }
        public MapNode CurrentNode { get; private set; }
        public HungerSystem Hunger { get; private set; }
        /// <summary>自由移動の層の中身。 離散マップ（Λ・8 層）では null。</summary>
        public FreeMapSim Sim { get; private set; }
        public bool IsFreeMap => Sim != null && CurrentMap != null && CurrentMap.IsFreeMap;

        // === イベント ===
        public event Action<FloorMap> OnMapGenerated;
        public event Action<MapNode> OnNodeEntered;
        public event Action<MapNode, TileType> OnMysteryResolved;

        // === 設定 ===
        [Header("空腹度")]
        [SerializeField] private int hungerPerFloor = 10;
        [SerializeField] private float starvationDamageRatio = 0.08f;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _shuttingDown = false;
            _instance = this;
        }

        void OnDestroy()
        {
            // 破棄済み参照を残すと Unity の擬似 null 経由で「非 null だが使えない Instance」になる。
            if (_instance == this) _instance = null;
        }

        void OnApplicationQuit() => _shuttingDown = true;

        // ================================================================
        //  公開 API
        // ================================================================

        /// <summary>指定フロアのマップを生成し、前哨基地に配置</summary>
        public void GenerateFloor(int floor)
        {
            CurrentMap = MapGenerator.Generate(floor);
            CurrentNode = CurrentMap.GetNode(CurrentMap.startNodeId);
            CurrentNode.visited = true;

            Hunger = new HungerSystem { starvationDamageRatio = starvationDamageRatio };
            int hunger = (floor == 6) ? 3 : hungerPerFloor;
            Hunger.Initialize(hunger);

            if (CurrentMap.IsFreeMap)
            {
                Sim = new FreeMapSim(CurrentMap.layout, new RunWallet(), FloorPlan.HasWanderer(floor));
                SyncFreeKnowledge();
            }
            else
            {
                Sim = null;
                // メタデバフ Lv4: 初期位置で視界を反映 (離散マップのみ。 自由移動の層は視界そのものが距離 1)
                ApplyMetaSightLimit();
            }

            OnMapGenerated?.Invoke(CurrentMap);
        }

        /// <summary>Λ層（時間の狭間）の環状線マップを生成し、スポーク(S)に配置。
        /// 空腹/飢餓は発生しないため Hunger は十分大きい値で初期化する。</summary>
        public void GenerateLambda()
        {
            CurrentMap = MapGenerator.GenerateLambda();
            Sim = null;
            CurrentNode = CurrentMap.GetNode(CurrentMap.startNodeId);
            CurrentNode.visited = true;

            // Λ層では移動による空腹/飢餓は無いため、枯渇しない大きな値で初期化
            Hunger = new HungerSystem { starvationDamageRatio = 0f };
            Hunger.Initialize(99999);

            OnMapGenerated?.Invoke(CurrentMap);
        }

        /// <summary>盤面を復元する。 **Ultra の worker が checkpoint から再開するためだけの口**で、
        /// 通常のゲーム進行では呼ばれない。
        ///
        /// <para><see cref="GenerateFloor"/> との違いは 2 つ。 生成せずに与えられた盤面を据えること、
        /// そして <b>視界を再計算しないこと</b> ── snapshot の <c>revealed</c> は
        /// 「その時点でプレイヤーが何を見えていたか」の記録なので、 復元後に
        /// <see cref="ApplyMetaSightLimit"/> を掛け直すと、 記録した可視状態を
        /// 現在のデバフ状態で塗り替えてしまう。</para>
        ///
        /// <para>Hunger は <see cref="HungerSystem.SetCurrentForTest"/> で据える。 名前は test 向けだが、
        /// 「現在値を直接置く」以外の用途がここには無い。</para></summary>
        public void RestoreState(FloorMap map, string currentNodeId, int hungerCurrent, int hungerMax)
        {
            if (map == null) throw new System.ArgumentNullException(nameof(map));
            // 自由移動の層は位置・時間・徘徊エネミーの状態を持つが、 snapshot はノードしか保存しない。
            //   座標まで保存する口は作っていない ── Ultra は自由移動の層では無効
            //   (AutoRunner.DoNavigateFree は Ultra の決定点を通らない・UltraMapSnapshot.Capture も拒否する)。
            //   ここへ来たら取り違え。
            if (map.IsFreeMap)
                throw new System.NotSupportedException("自由移動の層は Ultra の盤面復元に対応しない");
            Sim = null;

            CurrentMap = map;
            CurrentNode = map.GetNode(currentNodeId) ?? map.GetNode(map.startNodeId);
            if (CurrentNode == null)
                throw new System.InvalidOperationException(
                    "restored map has neither the requested node nor a start node");

            Hunger = new HungerSystem { starvationDamageRatio = starvationDamageRatio };
            Hunger.Initialize(System.Math.Max(1, hungerMax));
            Hunger.SetCurrentForTest(hungerCurrent);

            OnMapGenerated?.Invoke(CurrentMap);
        }

        /// <summary>現在位置から移動可能なノード一覧</summary>
        public List<MapNode> GetAvailableMoves()
        {
            if (CurrentMap == null || CurrentNode == null)
                return new List<MapNode>();
            return CurrentMap.GetReachableFrom(CurrentNode.id);
        }

        /// <summary>
        /// 指定ノードへ移動。空腹ダメージを返す。
        /// Mystery タイルは自動解決される。
        /// </summary>
        public int MoveTo(string nodeId, int playerMaxHP)
        {
            var target = CurrentMap?.GetNode(nodeId);
            if (target == null)
            {
                Debug.LogWarning($"[MapManager] ノード '{nodeId}' が存在しません");
                return 0;
            }
            if (IsFreeMap)
            {
                Debug.LogWarning($"[MapManager] 自由移動の層では MoveTo は使えない (BeginTravel / GameManager.AdvanceFreeMap)");
                return 0;
            }

            if (!CurrentNode.connections.Contains(nodeId))
            {
                Debug.LogWarning($"[MapManager] '{CurrentNode.id}' → '{nodeId}' は接続なし");
                return 0;
            }

            // 空腹度処理
            int starvationDmg = Hunger.OnMove(playerMaxHP);

            // 位置更新
            CurrentNode = target;
            target.visited = true;

            // ?マス解決
            if (target.type == TileType.Mystery && target.resolvedType == null)
            {
                target.resolvedType = MapGenerator.ResolveMystery();
                OnMysteryResolved?.Invoke(target, target.resolvedType.Value);
            }

            // メタデバフ Lv4: 視界制限を再計算
            ApplyMetaSightLimit();

            OnNodeEntered?.Invoke(target);

            return starvationDmg;
        }

        /// <summary>挑戦デバフ 軸13〈戦場の霧〉/ T4-C〈暗夜〉: 現在地から N ホップ先まで以外を unrevealed に。
        /// **limit の意味: -1 = 制限なし / 0 = 暗夜 (現在地以外すべて不可視) / N&gt;0 = N ホップ先まで。**</summary>
        private void ApplyMetaSightLimit()
        {
            int limit = MetaProgression.MetaDebuffApplicator.GetMapSightLimit();
            if (limit < 0 || CurrentMap == null || CurrentNode == null) return;

            var nodes = CurrentMap.GetAllNodes();
            // BFS で各ノードの距離を計算
            var dist = new Dictionary<string, int>();
            var queue = new Queue<string>();
            dist[CurrentNode.id] = 0;
            queue.Enqueue(CurrentNode.id);
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                int d = dist[id];
                if (d >= limit) continue;
                var n = CurrentMap.GetNode(id);
                if (n == null) continue;
                foreach (var conn in n.connections)
                {
                    if (dist.ContainsKey(conn)) continue;
                    dist[conn] = d + 1;
                    queue.Enqueue(conn);
                }
            }

            foreach (var n in nodes)
            {
                if (n == null) continue;
                bool inSight = dist.ContainsKey(n.id) || n.visited; // 訪問済みは見える
                n.revealed = inSight;
            }
        }

        // ================================================================
        //  自由移動の層 (2026-09-28)
        // ================================================================

        /// <summary>行き先を決める (移動中なら行き先を変える)。 山岳を横切る・偵察中・連戦中は false。
        /// 時間は進めない ── 進めるのは GameManager.AdvanceFreeMap。</summary>
        public bool BeginTravel(string nodeId)
        {
            if (!IsFreeMap) return false;
            var node = CurrentMap.GetNode(nodeId);
            if (node == null || node.index < 0) return false;
            return Sim.SetDestination(node.index);
        }

        /// <summary>点に着いた: <see cref="CurrentNode"/> を写して OnNodeEntered を鳴らす。
        /// タイルの起動は呼び出し側 (GameManager) が行う。</summary>
        public MapNode NoteFreeArrival(int index)
        {
            var node = CurrentMap?.GetNodeByIndex(index);
            if (node == null) return null;
            CurrentNode = node;
            node.visited = true;
            SyncFreeKnowledge();
            OnNodeEntered?.Invoke(node);
            return node;
        }

        /// <summary>逃げた後など、 中身の位置が点に戻った時に <see cref="CurrentNode"/> を合わせる。
        /// 移動中 (点の上に居ない) は直前に居た点のまま。</summary>
        public void SyncCurrentNodeFromSim()
        {
            if (!IsFreeMap || Sim.AtNode < 0) return;
            var node = CurrentMap.GetNodeByIndex(Sim.AtNode);
            if (node != null) CurrentNode = node;
        }

        /// <summary>マスの種別が判明した点を <see cref="MapNode.revealed"/> へ写す (一度見えたら隠さない)。
        /// 戦闘名も同時に開示する ── 照明・偵察で「マス・敵の種別」が分かる (§23-21 の索敵の表)。</summary>
        public void SyncFreeKnowledge()
        {
            if (!IsFreeMap) return;
            foreach (var n in CurrentMap.GetAllNodes())
            {
                if (n.index < 0 || n.index >= Sim.Known.Length || !Sim.Known[n.index] || n.revealed) continue;
                n.revealed = true;
                GameLoop.FloorManager.EnsureEncounter(CurrentMap.floor, n);
                n.encounterRevealed = true;
            }
        }

        /// <summary>移動の支払いを RunState へ橋渡しする。 物資は ProvisionSystem.SpendTravel、
        /// 払底中の HP は直接減らす (死ぬ移動も許す ── §23-21)。</summary>
        private sealed class RunWallet : IFreeMapWallet
        {
            private static GameLoop.RunState Run => GameLoop.GameManager.Instance?.Run;
            public int Provision => Run?.provision ?? 0;
            public void SpendProvision(int amount) => GameLoop.ProvisionSystem.SpendTravel(Run, amount);
            public bool PayHp(int amount)
            {
                var run = Run;
                if (run == null) return false;
                int before = run.playerHP;
                run.playerHP = Mathf.Max(0, run.playerHP - amount);
                MetaProgression.Achievements.AchievementService.NoteExternalHpDamage(before - run.playerHP);
                return run.playerHP <= 0;
            }
        }

        /// <summary>前哨基地効果: 空腹度全回復</summary>
        public void ProcessOutpost()
        {
            Hunger?.RestoreFull();
        }

        /// <summary>現在位置がボスかどうか</summary>
        public bool IsAtBoss => CurrentNode?.type == TileType.Boss;
    }
}

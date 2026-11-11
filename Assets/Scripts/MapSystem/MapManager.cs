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
    /// <para><b>航行マップの層</b>（1/3/5/6/7・ターン制 2026-10-18）では動く中身を素の C# の <see cref="FreeMapSim"/> が持ち、
    /// ここはその窓口になる（物資と HP の支払いを RunState へ橋渡しする・着いた点を <see cref="CurrentNode"/> に写す）。
    /// 時間を進めるのは GameManager（<c>MoveStep</c> ほか）。
    /// 8 層（ボス戦だけ）は離散のまま <see cref="MoveTo"/> で動く。</para>
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
        /// <summary>自由移動の層の中身。 離散マップ（8 層）では null。</summary>
        public FreeMapSim Sim { get; private set; }
        public bool IsFreeMap => Sim != null && CurrentMap != null && CurrentMap.IsFreeMap;

        // === イベント ===
        public event Action<FloorMap> OnMapGenerated;
        public event Action<MapNode> OnNodeEntered;
        public event Action<MapNode, TileType> OnMysteryResolved;

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

            if (CurrentMap.IsFreeMap)
            {
                bool withFoe = FloorPlan.HasWanderer(floor);
                // 航行アイテムの係数 (2026-11-11): 層に入る時の持ち物で作る (〈地図売りの走り書き〉はこの時の 1 回だけ効く)
                Sim = new FreeMapSim(CurrentMap.layout, new RunWallet(), withFoe,
                                     withFoe ? FoeProfile.ForFloor(floor) : null, null,
                                     GameLoop.NavItems.PerksFor(GameLoop.GameManager.Instance?.Run));
                // 〈補給線〉: 層の開始点 (前哨基地) は「着いた」処理を通らないので、 ここで中継点にする
                if (IsRelayType(CurrentMap.layout.types[CurrentMap.layout.start])) Sim.AddRelayPoint(CurrentMap.layout.start);
                SyncFreeKnowledge();
            }
            else
            {
                Sim = null;
                // メタデバフ Lv4: 初期位置で視界を反映 (離散マップのみ。 自由移動の層は視界そのものが距離 1)
            }

            OnMapGenerated?.Invoke(CurrentMap);
        }

        /// <summary>盤面を復元する。 **Ultra の worker が checkpoint から再開するためだけの口**で、
        /// 通常のゲーム進行では呼ばれない。
        ///
        /// <para><see cref="GenerateFloor"/> との違いは 2 つ。 生成せずに与えられた盤面を据えること、
        /// そして <b>視界を再計算しないこと</b> ── snapshot の <c>revealed</c> は
        /// 「その時点でプレイヤーが何を見えていたか」の記録なので、 復元後に
        /// 旧〈戦場の霧〉の視界制限 (2026-11-10 に撤去) を掛け直すと、 記録した可視状態を
        /// 現在のデバフ状態で塗り替えてしまう。</para></summary>
        public void RestoreState(FloorMap map, string currentNodeId)
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

            OnMapGenerated?.Invoke(CurrentMap);
        }

        /// <summary>中断セーブ (issue #84) から盤面を戻す。 <see cref="RestoreState"/> と違い<b>航行マップの層を扱う</b>:
        /// 形 (<see cref="FloorMap.layout"/>) はそのまま据え、 動く中身は <see cref="GenerateFloor"/> と同じ引数で作ってから写しを書き戻す
        /// (追跡者の性質・財布は作り直す。 性質は層と乱数の塩だけで決まるので、 塩を戻した後なら同じものが出る)。
        /// 視界は計算し直さない (写しの <c>Known</c> / <c>revealed</c> をそのまま使う)。</summary>
        public void RestoreFromSave(FloorMap map, int depth, string currentNodeId, FreeMapSim.SavedState simState)
        {
            if (map == null) throw new System.ArgumentNullException(nameof(map));
            if (map.IsFreeMap)
            {
                if (simState == null) throw new System.InvalidOperationException("航行マップの層なのに、 動く中身の写しが無い");
                bool withFoe = FloorPlan.HasWanderer(depth);
                var perks = GameLoop.NavItems.PerksFor(GameLoop.GameManager.Instance?.Run);
                Sim = new FreeMapSim(map.layout, new RunWallet(), withFoe, withFoe ? FoeProfile.ForFloor(depth) : null, null, perks);
                Sim.LoadState(simState, perks);
            }
            else Sim = null;

            CurrentMap = map;
            CurrentNode = map.GetNode(currentNodeId) ?? map.GetNode(map.startNodeId);
            if (CurrentNode == null)
                throw new System.InvalidOperationException("restored map has neither the requested node nor a start node");

            OnMapGenerated?.Invoke(CurrentMap);
        }

        /// <summary>現在位置から移動可能なノード一覧。 航行マップでは<b>いま道がつながっている隣</b>
        /// (前線の天候で崩れた道・沈んだ点は含まない)。</summary>
        public List<MapNode> GetAvailableMoves()
        {
            if (CurrentMap == null || CurrentNode == null)
                return new List<MapNode>();
            if (IsFreeMap)
            {
                var list = new List<MapNode>();
                foreach (int i in Sim.Neighbors(Sim.Me))
                {
                    var n = CurrentMap.GetNodeByIndex(i);
                    if (n != null && !Sim.IsSunk(i)) list.Add(n);
                }
                return list;
            }
            return CurrentMap.GetReachableFrom(CurrentNode.id);
        }

        /// <summary>
        /// 指定ノードへ移動。 戻り値は常に 0（旧: 空腹ダメージ。 空腹は 2026-10-17 に削除。 呼び出し側の互換のため int のまま）。
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
                Debug.LogWarning($"[MapManager] 航行マップの層では MoveTo は使えない (GameManager.MoveStep)");
                return 0;
            }

            if (!CurrentNode.connections.Contains(nodeId))
            {
                Debug.LogWarning($"[MapManager] '{CurrentNode.id}' → '{nodeId}' は接続なし");
                return 0;
            }

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

            OnNodeEntered?.Invoke(target);

            return 0;
        }

        // ================================================================
        //  自由移動の層 (2026-09-28)
        // ================================================================

        /// <summary>点に着いた: <see cref="CurrentNode"/> を写して OnNodeEntered を鳴らす。
        /// タイルの起動は呼び出し側 (GameManager) が行う。</summary>
        /// <summary>〈補給線〉(〈兵站〉r10) の中継点になる点の種別。</summary>
        public static bool IsRelayType(TileType t) => t == TileType.Outpost || t == TileType.SupplyDepot || t == TileType.Shop;

        public MapNode NoteFreeArrival(int index)
        {
            var node = CurrentMap?.GetNodeByIndex(index);
            if (node == null) return null;
            CurrentNode = node;
            node.visited = true;
            Sim?.MarkDone(index);
            // 〈補給線〉の中継点: この層で一度着いた前哨基地・補給庫・店
            if (IsRelayType(node.type)) Sim?.AddRelayPoint(index);
            SyncFreeKnowledge();
            OnNodeEntered?.Invoke(node);
            return node;
        }

        /// <summary>逃げた後など、 中身の位置が変わった時に <see cref="CurrentNode"/> を合わせる。</summary>
        public void SyncCurrentNodeFromSim()
        {
            if (!IsFreeMap) return;
            var node = CurrentMap.GetNodeByIndex(Sim.Me);
            if (node != null) CurrentNode = node;
        }

        /// <summary>マスの種別が判明した点を <see cref="MapNode.revealed"/> へ写す (一度見えたら隠さない)。
        /// 戦闘名は**完全公開** (偵察・照明で見つけた点 = <see cref="FreeMapSim.Scouted"/>) の時だけ開示する (2026-11-05)。
        /// 何もしない時の視界 (1 歩) で見えただけの点は種別だけで、 戦闘名は出さない ── 近いことと調べたことは別。</summary>
        public void SyncFreeKnowledge()
        {
            if (!IsFreeMap) return;
            foreach (var n in CurrentMap.GetAllNodes())
            {
                if (n.index < 0 || n.index >= Sim.Known.Length) continue;
                if (Sim.Known[n.index] && !n.revealed) n.revealed = true;
                if (Sim.Scouted[n.index] && !n.encounterRevealed)
                {
                    GameLoop.FloorManager.EnsureEncounter(CurrentMap.floor, n);
                    n.encounterRevealed = true;
                }
            }
        }

        /// <summary>移動の支払いを RunState へ橋渡しする。 物資は ProvisionSystem.SpendTravel、
        /// 物資が足りない分の HP は直接減らす (死ぬ移動も許す ── §23-21)。</summary>
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
                GameLoop.HpOutsideDiag.Add(Instance?.Sim?.HpPayReason ?? "移動", before - run.playerHP);   // [計装 2026-11-05]
                return run.playerHP <= 0;
            }
        }

        /// <summary>現在位置がボスかどうか</summary>
        public bool IsAtBoss => CurrentNode?.type == TileType.Boss;
    }
}

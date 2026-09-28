using System.Collections.Generic;
using AutoTest.FreeNav;
using GameLoop;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// 自由移動の層 (2026-09-28・docs/GAME.md §5) の BOT の航行。 <b>技量で方策が変わる</b> (§21):
    /// <list type="bullet">
    /// <item><b>Naive</b>: 下の簡素な方策 (最初の実装のまま)。</item>
    /// <item><b>Optimal</b>: 近視眼 ── 1 手先だけを最善にする (<see cref="NavMode.Myopic"/>)。</item>
    /// <item><b>Super / Exact / Ultra</b>: マクロ ── 出口までの道順を読み、 反応の履歴から縄張りを推定する
    ///       (<see cref="NavMode.Macro"/>)。 中身は素の C# の <see cref="FreeNavPlanner"/>。</item>
    /// </list>
    /// <see cref="freeNavModeOverride"/> で戦闘の技量と切り離して測れる。 どの方策も乱数を使わない。
    ///
    /// <para>Naive の方針:</para>
    /// <list type="number">
    /// <item>物資が「出口までの費用 ＋ 余裕」を割ったら出口 (ボス / 門) へ。 1 層の行動回数の上限に達しても出口へ。</item>
    /// <item>それ以外は、 まだ使っていない点のうち <b>既存のマス評価 (Rank) を移動の費用で割った価値</b>が最大の点へ。
    ///       種別の見えていない点は平均的な価値として扱う (見えていない種別を覗かない)。</item>
    /// <item>魔石が「かなり近い」「察知されている」なら忍び足にして、 道の外を直線で行く (気配 0・痕跡が薄い)。
    ///       それ以外は通常の速さで、 道と直線の安い方を選ぶ。</item>
    /// <item>連戦は HP が少なければ各戦の頭で逃げる (<see cref="ShouldFleeGauntlet"/>)。</item>
    /// </list>
    /// <para>照明・偵察・囮・罠は使わない。 乱数は使わない (GameRng を消費しない)。</para>
    /// </summary>
    public partial class AutoRunner
    {
        /// <summary>1 層で航行の判断 (移動の指示) を出せる回数の上限。 これを超えたら出口へ向かい、
        /// その倍を超えたら詰みとして打ち切る (バッチを無限に回さない)。</summary>
        public const int MaxFreeMapActionsPerFloor = 80;
        /// <summary>出口までの費用に足す物資の余裕。</summary>
        public const int FreeMapExitMargin = 150;
        /// <summary>種別の見えていない点の価値 (Rank の天井からの距離)。 通常の戦闘と同じくらい。</summary>
        private const float UnknownNodeDesire = 5f;
        /// <summary>Rank の天井。 これより悪い (大きい) 点は行く価値が無いとして候補から外す。</summary>
        private const float RankCeil = 10f;
        /// <summary>連戦で逃げる HP 割合。</summary>
        private const float GauntletFleeHpRatio = 0.45f;

        private int _freeMapActions;
        private FreeMapLayout _freeMapLayout;
        private FreeNavBelief _freeNavBelief;

        /// <summary>航行の目盛り (Optimal / Super)。 全部仮置き ── スイープで差し替える。</summary>
        [System.NonSerialized] public FreeNavTuning freeNavTuning = new FreeNavTuning();
        /// <summary>航行の方策を戦闘の技量から切り離す。 -1 = 技量に従う (既定) / 0 = Naive / 1 = Optimal (近視眼) / 2 = Super (マクロ)。
        /// 「航行の読みの深さ」と「戦闘の読みの深さ」を別々に測るためのつまみ。</summary>
        [System.NonSerialized] public int freeNavModeOverride = -1;
        /// <summary>[計装] 方策ごとの航行判断の回数・逃げた回数・出口へ向かった回数 (バッチ累計)。 添字は 0 Naive / 1 Optimal / 2 Super。</summary>
        public static readonly long[] FreeNavDecisions = new long[3], FreeNavFlees = new long[3], FreeNavExits = new long[3];

        /// <summary>この判断で使う航行の方策 (0 Naive / 1 Myopic / 2 Macro)。</summary>
        private int FreeNavModeNow()
        {
            if (freeNavModeOverride >= 0 && freeNavModeOverride <= 2) return freeNavModeOverride;
            switch (EffectiveWiringSkill)
            {
                case WiringSkill.Naive: return 0;
                case WiringSkill.Optimal: return 1;
                default: return 2;   // Super / Exact / (Ultra の基準手は Super)
            }
        }

        /// <summary>[計装] 自由移動の層で行動の上限に達した回数 (バッチ累計)。</summary>
        public static long FreeMapActionCapHits;
        /// <summary>[計装] 連戦から逃げた回数 (バッチ累計)。</summary>
        public static long FreeMapGauntletFlees;

        private bool DoNavigateFree(GameManager gm, MapManager mm)
        {
            var run = gm.Run;
            var sim = mm.Sim;
            var map = mm.CurrentMap;
            if (run == null || sim == null || map == null) { Finish(Outcome.Deadlock, "自由移動の層の状態が無い"); return true; }

            if (!ReferenceEquals(map.layout, _freeMapLayout))
            {
                _freeMapLayout = map.layout; _freeMapActions = 0;
                _freeNavBelief = new FreeNavBelief(map.layout);   // 層ごとに作り直す (縄張りの推定は層で別物)
            }
            if (++_freeMapActions > 2 * MaxFreeMapActionsPerFloor)
            {
                Finish(Outcome.Deadlock, $"自由移動の層で行動の上限 ({2 * MaxFreeMapActionsPerFloor}) を超えた");
                return true;
            }
            if (_freeMapActions == MaxFreeMapActionsPerFloor + 1) FreeMapActionCapHits++;

            // ── 回復と物資の補充 (旧航行と同じ規則) ──
            float hpRatio = run.playerMaxHP > 0 ? (float)run.playerHP / run.playerMaxHP : 1f;
            int floorHit = EstimateFloorMaxHit(run);
            float dangerTarget = FightBudgetDanger(run, floorHit);
            while (Consumables.TryUseBestHeal(run, dangerTarget)) { }
            if (run.provision <= ProvisionRefillFloorEffective())
            {
                int guardProvision = 0;
                while (run.provision < run.provisionCap && guardProvision++ < 8
                       && UseFirstOfFamily(run, ItemIds.ConsProvisionFamily)) { }
            }
            hpRatio = run.playerMaxHP > 0 ? (float)run.playerHP / run.playerMaxHP : 1f;
            _curBossNear = false;

            var L = map.layout;
            int at = sim.AtNode >= 0 ? sim.AtNode : L.NearestNode(sim.Me);

            int navMode = FreeNavModeNow();
            FreeNavDecisions[navMode]++;
            if (navMode > 0) return DoNavigateFreePlanned(gm, mm, navMode, hpRatio, floorHit, dangerTarget);

            // ── 行き先 (Naive) ──
            int target = -1;
            bool toExit = _freeMapActions > MaxFreeMapActionsPerFloor
                          || run.provision < CostToNode(sim, L, L.goal) + FreeMapExitMargin;
            if (!toExit)
            {
                float best = 0f;
                foreach (var n in map.GetAllNodes())
                {
                    if (n.index < 0 || n.index == at || n.index == L.goal || n.index == L.start) continue;
                    if (n.activated) continue;                     // 使い切った点
                    float desire;
                    if (!n.revealed) desire = UnknownNodeDesire;
                    else if (n.EffectiveType == TileType.SupplyCache)
                        desire = 6f + 10f * (1f - Mathf.Clamp01(run.provision / (float)Mathf.Max(1, run.provisionCap)));
                    else desire = RankCeil - Rank(n, hpRatio, _curCombatAverse, false, run, dangerTarget);
                    if (desire <= 0f) continue;
                    float cost = CostToNode(sim, L, n.index);
                    float value = desire / (1f + cost / 100f);
                    // 同点は添字の小さい方 (決定的)
                    if (value > best + 1e-6f) { best = value; target = n.index; }
                }
                // 近くの未使用の点が 1 つも無ければ出口へ
                if (target < 0) toExit = true;
            }
            if (toExit) { target = L.goal; _curBossNear = true; }

            // ── 速さと経路 ──
            bool alert = sim.StoneLevel >= 2;   // かなり近い / 察知されている
            gm.SetTravelSpeed(alert ? MoveSpeed.Sneak : MoveSpeed.Normal);
            int hop = NextHop(sim, map, at, target, preferOffRoad: alert);
            if (hop < 0 || hop == sim.AtNode)
            {
                // ここに来るのは「山岳で直線も道も無い」場合だけ (道は必ずつながっているので実際には起きない)
                Finish(Outcome.Deadlock, $"自由移動の層で行き先 {target} へ動けない");
                return true;
            }

            string hopId = map.NodeIdAt(hop);
            var ev = gm.TravelSync(hopId, FreeMapEvent.StoneRaised | FreeMapEvent.Detected | FreeMapEvent.FoeSighted);
            if (ev == FreeMapEvent.None && gm.CurrentPhase == GameManager.GamePhase.MapNavigation && sim.AtNode == at)
            {
                Finish(Outcome.Deadlock, $"自由移動の層で {hopId} へ出発できない");
                return true;
            }
            return false;
        }

        /// <summary>Optimal (近視眼) / Super (マクロ) の航行。 判断は <see cref="FreeNavPlanner"/>、 ここは材料を揃えて指すだけ。</summary>
        private bool DoNavigateFreePlanned(GameManager gm, MapManager mm, int navMode, float hpRatio, int floorHit, float dangerTarget)
        {
            var run = gm.Run; var sim = mm.Sim; var map = mm.CurrentMap; var L = map.layout;
            _freeNavBelief.Observe(sim);

            int n = L.Count;
            var used = new bool[n];
            var known = new bool[n];
            foreach (var node in map.GetAllNodes())
            {
                if (node.index < 0 || node.index >= n) continue;
                used[node.index] = node.activated || node.index == L.start;
                known[node.index] = node.revealed;
            }

            // ボス前に欲しい HP: 危険度駆動の目標 ＋ ボスの 1 発ぶん (旧航行のボス接近と同じ考え方)
            float bossNeed = dangerTarget;
            if (floorHit > 0 && run.playerMaxHP > 0)
                bossNeed = Mathf.Clamp(dangerTarget + (float)floorHit / run.playerMaxHP, 0.5f, 0.95f);

            var memo = new Dictionary<long, NavOutcome>();
            var ctx = new FreeNavContext
            {
                sim = sim, L = L, used = used, known = known,
                maxHp = run.playerMaxHP, hp = hpRatio, provision = run.provision, provisionCap = run.provisionCap,
                bossHpNeed = bossNeed, belief = _freeNavBelief, tune = freeNavTuning,
                forceExit = _freeMapActions > MaxFreeMapActionsPerFloor,
                outcome = (j, hp, prov) => FreeNavOutcome(map, j, hp, prov, run, floorHit, dangerTarget, memo),
            };
            var d = FreeNavPlanner.Decide(ctx, navMode == 2 ? NavMode.Macro : NavMode.Myopic);
            if (d.fleeing) FreeNavFlees[navMode]++;
            if (d.toExit) FreeNavExits[navMode]++;
            _curBossNear = d.toExit;

            int at = sim.AtNode >= 0 ? sim.AtNode : L.NearestNode(sim.Me);
            int hop = d.hop;
            if (hop < 0 || hop == sim.AtNode) hop = NextHop(sim, map, at, d.target >= 0 ? d.target : L.goal, preferOffRoad: false);
            if (hop < 0 || hop == sim.AtNode)
            {
                Finish(Outcome.Deadlock, $"自由移動の層で行き先 {d.target} へ動けない (方策 {navMode})");
                return true;
            }
            gm.SetTravelSpeed(d.speed);
            string hopId = map.NodeIdAt(hop);
            var ev = gm.TravelSync(hopId, FreeMapEvent.StoneRaised | FreeMapEvent.Detected | FreeMapEvent.FoeSighted);
            if (ev == FreeMapEvent.None && gm.CurrentPhase == GameManager.GamePhase.MapNavigation && sim.AtNode == at)
            {
                Finish(Outcome.Deadlock, $"自由移動の層で {hopId} へ出発できない (方策 {navMode})");
                return true;
            }
            return false;
        }

        /// <summary>点 j を (HP 比, 物資) の状態で踏んだ時の見込み。 マスの価値は既存の <see cref="Rank"/> (天井 10 からの距離)、
        /// HP の増減は <see cref="PredictHpRatioAfter"/>、 物資は戦闘の報酬と被弾の損・補給庫。
        /// <b>種別が見えていない点は抽選重みで平均する</b> (見えていない種別を覗かない)。 同じ判断の中ではメモする。</summary>
        private NavOutcome FreeNavOutcome(FloorMap map, int j, double hp, double prov, RunState run,
                                          int floorHit, float dangerTarget, Dictionary<long, NavOutcome> memo)
        {
            var node = map.GetNodeByIndex(j);
            if (node == null) return default;
            int hpKey = (int)System.Math.Round(hp * 50), provKey = (int)System.Math.Round(prov / 25);
            long key = ((long)j << 32) | ((long)(hpKey & 0xFFFF) << 16) | (long)(provKey & 0xFFFF);
            if (memo.TryGetValue(key, out var hit)) return hit;

            NavOutcome o;
            if (node.revealed) o = KnownNavOutcome(node, hp, prov, run, floorHit, dangerTarget);
            else
            {
                o = default;
                float tot = 0f;
                foreach (var (type, w) in FreeMapGenerator.TileWeights) tot += w;
                foreach (var (type, w) in FreeMapGenerator.TileWeights)
                {
                    var k = KnownNavOutcome(SyntheticNavNode(type), hp, prov, run, floorHit, dangerTarget);
                    double f = w / tot;
                    o.value += f * k.value; o.hpDelta += f * k.hpDelta; o.provDelta += f * k.provDelta;
                }
            }
            memo[key] = o;
            return o;
        }

        private NavOutcome KnownNavOutcome(MapNode node, double hp, double prov, RunState run, int floorHit, float dangerTarget)
        {
            var o = new NavOutcome();
            var t = node.EffectiveType;
            if (t == TileType.SupplyCache) { o.provDelta = FreeMapParams.RewardCache; return o; }
            float h = Mathf.Clamp01((float)hp);
            o.value = RankCeil - Rank(node, h, _curCombatAverse, false, run, dangerTarget);
            o.hpDelta = PredictHpRatioAfter(node, h, floorHit, run) - h;
            int loss = ProvisionRules.CombatLoss(ProvisionRules.TierOf((int)prov));
            if (t == TileType.Battle) o.provDelta = FreeMapParams.RewardBattle - loss;
            else if (t == TileType.EliteBattle) o.provDelta = FreeMapParams.RewardElite - loss;
            return o;
        }

        private readonly Dictionary<TileType, MapNode> _syntheticNavNodes = new Dictionary<TileType, MapNode>();
        /// <summary>種別だけを持つ仮のノード (戦闘名は未開示)。 見えていない点の期待値を Rank で出すためだけに使う。</summary>
        private MapNode SyntheticNavNode(TileType t)
        {
            if (!_syntheticNavNodes.TryGetValue(t, out var n))
                _syntheticNavNodes[t] = n = new MapNode("nav?" + (int)t, 0, 0, t) { freeMap = true };
            return n;
        }

        /// <summary>今いる場所から点 j までの物資の見積もり: 直線 (山岳を横切るなら不可) と道の安い方。</summary>
        private static float CostToNode(FreeMapSim sim, FreeMapLayout L, int j)
        {
            var pv = sim.Preview(j);
            if (!pv.valid) return 0f;
            float direct = pv.blocked ? float.MaxValue : pv.provisionCost;
            int at = sim.AtNode >= 0 ? sim.AtNode : L.NearestNode(sim.Me);
            double road = L.RoadDistance(at, j);
            float roadCost = double.IsInfinity(road) ? float.MaxValue
                           : (float)(road + Vec2.Dist(sim.Me, L.pos[at])) * FreeMapParams.CostRoad;
            return Mathf.Min(direct, roadCost);
        }

        /// <summary>次に向かう点。 道を辿るか (安い)、 直線で行くか (道の外は 2 倍だが気配 0・痕跡が薄い)。
        /// <b>着いた点は発動する</b>ので、 道の途中に未使用の点があるなら (山岳で塞がれていない限り) 直線で飛び越す
        /// ── 行き先に選んでいないマスを踏んで戦闘に入らないように。</summary>
        private static int NextHop(FreeMapSim sim, FloorMap map, int at, int target, bool preferOffRoad)
        {
            var L = map.layout;
            var pv = sim.Preview(target);
            if (preferOffRoad && pv.valid && !pv.blocked) return target;
            var path = L.ShortestRoadPath(at, target);
            double road = double.PositiveInfinity;
            if (path.Count > 0)
            {
                road = 0; int a = at;
                foreach (int v in path) { road += L.Dist(a, v); a = v; }
            }
            double directCost = pv.valid && !pv.blocked
                ? pv.distance * (pv.road ? FreeMapParams.CostRoad : FreeMapParams.CostOffRoad) : double.PositiveInfinity;
            double roadCost = road * FreeMapParams.CostRoad;
            if (directCost < roadCost) return target;
            int hop = -1;
            if (path.Count > 0)
            {
                var hopNode = map.GetNodeByIndex(path[0]);
                bool hopUnused = path[0] != target && hopNode != null && !hopNode.activated
                                 && hopNode.type != TileType.Outpost;
                hop = hopUnused && pv.valid && !pv.blocked ? target : path[0];
            }
            else if (pv.valid && !pv.blocked) hop = target;

            // 道の途中・道の外で止まっていると、 「最寄りの点」から引いた経路の最初の点が山岳の向こうのことがある。
            //   その時は行ける点 (道の両端・山岳に塞がれない点) のうち、 そこから行き先までが一番近い点へ向かう。
            if (hop < 0 || sim.Preview(hop).blocked || !sim.Preview(hop).valid)
            {
                double bestD = double.PositiveInfinity; hop = -1;
                for (int i = 0; i < L.Count; i++)
                {
                    if (i == sim.AtNode) continue;
                    var p = sim.Preview(i);
                    if (!p.valid || p.blocked) continue;
                    double d = p.distance + L.RoadDistance(i, target);
                    if (d < bestD) { bestD = d; hop = i; }
                }
            }
            return hop;
        }

        /// <summary>連戦の各戦の頭で逃げるか: HP が少なく、 まだ 2 戦以上残っているか最後の 1 戦でも瀕死なら逃げる。</summary>
        private static bool ShouldFleeGauntlet(RunState run, GameManager gm)
        {
            if (run == null || gm == null || !gm.InGauntletCombat || run.playerMaxHP <= 0) return false;
            float hp = (float)run.playerHP / run.playerMaxHP;
            int remaining = gm.GauntletFightTotal - gm.GauntletFightIndex;
            return remaining >= 2 ? hp < GauntletFleeHpRatio : hp < GauntletFleeHpRatio * 0.5f;
        }

        /// <summary>系統で最初に見つかった消耗品を使う (小さい Tier から)。 id を綴りで選ばない。</summary>
        private static bool UseFirstOfFamily(RunState run, string family)
        {
            if (run?.ownedConsumables == null) return false;
            string pick = null; int pickTier = int.MaxValue;
            foreach (var id in run.ownedConsumables)
            {
                if (ItemIds.ConsFamilyOf(id) != family) continue;
                int t = ItemIds.ConsTierOf(id);
                if (t < pickTier) { pickTier = t; pick = id; }
            }
            return pick != null && Consumables.Use(run, pick);
        }
    }
}

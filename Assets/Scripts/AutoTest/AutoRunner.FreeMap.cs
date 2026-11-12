using System.Collections.Generic;
using GameLoop;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// 航行マップ (ターン制・2026-10-18・docs/GAME.md §5) の BOT の航行。 <b>技量で方策が変わる</b> (§21):
    /// <list type="bullet">
    /// <item><b>Naive</b>: 行き先だけを決めて最短の道を歩く。 追跡者を避けない。</item>
    /// <item><b>Optimal / Super / Exact / Ultra</b>: 同じ行き先に加えて、 <b>見えている / 魔石に出ている追跡者を避ける</b>
    ///       ── 次の 1 歩を、 追跡者に近づかない隣へ振る・補足されたら囮罠を使う。</item>
    /// </list>
    /// どの方策も乱数を使わない。 追跡者の性質 (<see cref="FoeProfile"/>) は読まない (プレイヤーには明かされない)。
    ///
    /// <para>方針:</para>
    /// <list type="number">
    /// <item>物資が「出口までの費用 ＋ 余裕」を割ったら出口 (ボス / 門) へ。 1 層の行動回数の上限に達しても出口へ。</item>
    /// <item>それ以外は、 まだ使っていない点のうち <b>既存のマス評価 (Rank) を歩数で割った価値</b>が最大の点へ。
    ///       種別の見えていない点は平均的な価値として扱う (見えていない種別を覗かない)。</item>
    /// <item>追跡者に捕まったら HP が少なければ逃げる (<see cref="ShouldFleePursuer"/>)。</item>
    /// <item>縄張りの宝 (2026-11-12): 補給が要らない時、 HP の割合から追跡者の戦闘の損の見積もりを引いて
    ///       <see cref="territoryTreasureHpMargin"/> 以上なら宝を行き先にする (<see cref="WantsTerritoryTreasure"/>・どの技量も同じ規則)。
    ///       既定のしきい値は 9 = 取りに行かない (測定で行かない方が良かった・2026-11-12)。
    ///       縄張りを出ない追跡者なら、 縄張りの外の点は避けない。</item>
    /// <item>縄張りを横切らない (2026-11-12・回避ありの方策だけ): 縄張りを出ない追跡者の層では、 払える遠回り
    ///       (<see cref="navTerritoryDetour"/> 歩まで・行った先から出口か店まで歩ける) なら縄張りの点を踏まない道を選ぶ (<see cref="TerritoryAwareStep"/>)。</item>
    /// </list>
    /// <para>照明・偵察・待つは使わない。 乱数は使わない (GameRng を消費しない)。</para>
    /// </summary>
    public partial class AutoRunner
    {
        /// <summary>1 層で航行の判断 (移動の指示) を出せる回数の上限。 これを超えたら出口へ向かい、
        /// その倍を超えたら詰みとして打ち切る (バッチを無限に回さない)。</summary>
        public const int MaxFreeMapActionsPerFloor = 120;
        /// <summary>出口までの費用に足す物資の余裕。</summary>
        public const int FreeMapExitMargin = 1;   // 2026-11-09: 上限 5 の 2 割
        /// <summary>種別の見えていない点の価値 (Rank の天井からの距離)。 通常の戦闘と同じくらい。</summary>
        private const float UnknownNodeDesire = 5f;
        /// <summary>Rank の天井。 これより悪い (大きい) 点は行く価値が無いとして候補から外す。</summary>
        private const float RankCeil = 10f;
        /// <summary>追跡者の戦闘で逃げる HP 割合。</summary>
        private const float PursuerFleeHpRatio = 0.45f;
        /// <summary>この歩数以内に追跡者が居ると分かっている点へは、 避けられる限り踏まない。</summary>
        private const int FoeAvoidHops = 2;
        /// <summary>囮罠を使う「最短で何手で捕まるか」の上限。</summary>
        private const int LureEtaThreshold = 3;

        private int _freeMapActions;
        private readonly Queue<int> _freeMapTrail = new Queue<int>();
        /// <summary>この層で踏んだ点。 新しい点へ着いたら「進んでいる」とみなす。</summary>
        private readonly HashSet<int> _freeMapVisited = new HashSet<int>();
        /// <summary>新しい点へ着かないまま過ぎた行動の数。 <see cref="FreeMapStallActions"/> で迂回へ切り替える。</summary>
        private int _freeMapSinceProgress;
        /// <summary>迂回している行き先 (-1 = 迂回していない)。 着くか行き先が変わるまで迂回を続ける。</summary>
        private int _freeMapDetourTarget = -1;
        /// <summary>見えている時に最後に見た追跡者の位置と、 その位置を続けて見た回数・一度でも動いたのを見たか。
        /// <b>性質 (FoeProfile) は読まない</b> ── 動かないことは、 人と同じく見え方から知る。</summary>
        private int _foeSeenAt = -1, _foeSeenStill, _foeSeenTurn = -1;
        private bool _foeSeenMove;

        /// <summary>偵察と照明を使うか (2026-11-05)。 戦闘名は偵察・照明で見つけた点 (完全公開) にしか出ないので、
        /// 使わないと危険な戦闘を避ける情報が無い。 **既定は false (使わない)** ── 2026-11-05 に同じ 5,000 シードで比べると
        /// 使う方がクリア 19.4% → 17.9% (z=−2.42) と悪化した (照明 5 回 = 物資 250 と、 偵察 6 回分のターンを払うのに、
        /// 戦闘名を活かす判断が TileDanger の足切りしか無い)。 使う場面を絞ってから戻す。</summary>
        [System.NonSerialized] public bool freeNavScout = false;
        /// <summary>この層で偵察した点 (同じ点では 1 回まで)。</summary>
        private readonly HashSet<int> _freeMapReconAt = new HashSet<int>();
        /// <summary>偵察: 2 歩以内に、 まだ調べていない未使用の点がこの数以上あれば使う。</summary>
        private const int ReconMinUnscouted = 2;
        /// <summary>照明: 3 歩以内に、 まだ調べていない未使用の点がこの数以上あれば使う。</summary>
        private const int FlareMinUnscouted = 4;
        /// <summary>[計装] 偵察・照明を使った回数 (バッチ累計)。</summary>
        public static long FreeNavRecons, FreeNavFlares;

        /// <summary>新しい点へ着かないまま、 この行動数が過ぎたら迂回を考える。</summary>
        private const int FreeMapStallActions = 10;
        /// <summary>同じ位置に居るのを続けて何回見たら「動かない追跡者」とみなすか。</summary>
        private const int FoeStillToStatic = 2;
        /// <summary>[計装] 迂回に切り替えた回数 (バッチ累計)。 添字は 0 迂回路あり / 1 迂回路なし (真っ直ぐ歩いて戦う)。</summary>
        public static readonly long[] FreeNavDetours = new long[2];
        private FreeMapLayout _freeMapLayout;
        /// <summary>[計装] 踏破を記録する対象の層 (層を出た時・ランの終わりに記録して null に戻す)。</summary>
        private FloorMap _visitMap;
        private int _visitDepth;

        /// <summary>[計装 2026-11-09] 層の踏破 (マスの種別ごとの数と踏んだ数) を記録する。</summary>
        private void RecordMapVisit()
        {
            if (_visitMap == null || _cur == null) { _visitMap = null; return; }
            var tot = new int[32]; var vis = new int[32];
            foreach (var n in _visitMap.GetAllNodes())
            {
                int t = (int)n.type;
                if (t < 0 || t >= 32) continue;
                tot[t]++;
                if (n.visited || n.activated) vis[t]++;
            }
            for (int t = 0; t < 32; t++)
                if (tot[t] > 0) _cur.mapVisit.AddRange(new[] { _visitDepth, t, tot[t], vis[t] });
            _visitMap = null;
        }

        /// <summary>航行の方策を戦闘の技量から切り離す。 -1 = 技量に従う (既定) / 0 = Naive / 1 以上 = 追跡者を避ける。
        /// 「航行の読みの深さ」と「戦闘の読みの深さ」を別々に測るためのつまみ。</summary>
        [System.NonSerialized] public int freeNavModeOverride = -1;
        /// <summary>[計装] 方策ごとの航行判断の回数・逃げた回数・出口へ向かった回数・囮罠を使った回数 (バッチ累計)。添字は 0 Naive / 1 回避あり。</summary>
        public static readonly long[] FreeNavDecisions = new long[2], FreeNavFlees = new long[2], FreeNavExits = new long[2], FreeNavLures = new long[2];

        /// <summary>この判断で追跡者を避ける方策か。</summary>
        private bool FreeNavAvoidsFoe()
        {
            if (freeNavModeOverride >= 0) return freeNavModeOverride > 0;
            return EffectiveWiringSkill != WiringSkill.Naive;
        }

        /// <summary>[計装] 航行マップの層で行動の上限に達した回数 (バッチ累計)。</summary>
        public static long FreeMapActionCapHits;
        /// <summary>[計装] 追跡者の戦闘から逃げた回数 (バッチ累計)。</summary>
        public static long FreeMapPursuerFlees;
        /// <summary>[計装] 〈補給線〉(〈兵站〉r10) で跳んだ回数 (バッチ累計)。</summary>
        public static long FreeMapRelays;

        /// <summary>[計装] 店を探すために照明を使った回数 (バッチ累計)。</summary>
        public static long FreeMapShopFlares;

        /// <summary>補給できる店 (種別が見えていて・まだ入っていない・物資 1 を買える金がある) の添字。 <paramref name="hops"/> で最も近いもの。 無ければ -1。
        /// <para>2026-11-09: 物資の上限 5 に合わせて、 寄り道の計画を「今の物資で出口まで」から「出口か、 まだ入っていない店まで」へ。
        /// 店は一度入ると使い切りなので、 補給の当てになるのは未使用の店だけ。</para></summary>
        private static int NearestRefuel(MapManager mm, FreeMapSim sim, int[] hops, RunState run, int exclude = -1, int[] toGoal = null, int maxToGoal = int.MaxValue)
        {
            if (run == null || run.coins < FreeMapParams.ProvisionPrice) return -1;
            int best = -1, bestH = int.MaxValue;
            foreach (var n in mm.CurrentMap.GetAllNodes())
            {
                int i = n.index;
                if (i < 0 || i >= hops.Length || n.activated || !n.revealed || sim.IsSunk(i)) continue;
                if (n.EffectiveType != TileType.Shop || n.isFalseMerchant) continue;
                if (i == exclude) continue;
                if (toGoal != null && (toGoal[i] == int.MaxValue || toGoal[i] >= maxToGoal)) continue;   // 出口へ近づく店だけ
                if (hops[i] < bestH) { bestH = hops[i]; best = i; }
            }
            return best;
        }

        /// <summary>歩く計画に使う物資 = 手持ち + 近道の残り (ただで動ける回数 × 1 歩の物資)。
        /// h 歩歩いた後の残りは「手持ち − max(0, h − 残り) + max(0, 残り − h)」= この値 − h で、 ただの歩を先に使うことと同じになる。</summary>
        private static int NavSupply(RunState run, FreeMapSim sim)
            => run.provision + (sim != null ? sim.FreeMovesLeft * FreeMapParams.MoveSupply : 0);

        /// <summary>[2026-11-10] HP で払って歩ける歩数 (物資が尽きた後・1 歩 HP <see cref="FreeMapParams.HpPerSupply"/>)。
        /// HP は層を移るたびに全回復するので、 ボスのいない層では HP を燃料として使い切ってよい (最大 HP の 25% を残す)。
        /// ボスのいる層ではボス戦の分を残す (最大 HP の 75%)。</summary>
        private static int HpWalkSteps(RunState run, MapManager mm, float hpFrac = -1f)
        {
            if (run == null || run.playerMaxHP <= 0) return 0;
            float hp = hpFrac >= 0f ? hpFrac * run.playerMaxHP : run.playerHP;
            float reserve = run.playerMaxHP * (LayerHasBoss(mm) ? 0.75f : 0.25f);
            return Mathf.Max(0, Mathf.FloorToInt((hp - reserve) / FreeMapParams.HpPerSupply));
        }

        /// <summary>[嵐 2026-11-13・BOT の最小の対応 (b)] from から to への最短の道 (<see cref="FreeMapSim.StepToward"/> をたどる) で払う嵐の HP の合計。
        /// 輪は今の手番のまま数える (先の縮みは見込まない)。 嵐の層でなければ 0。</summary>
        private static int StormHpOnPath(FreeMapSim sim, int from, int to)
        {
            if (sim == null || !sim.StormMode || from < 0 || to < 0) return 0;
            int hp = 0, cur = from;
            for (int guard = 0; guard < 200 && cur != to; guard++)
            {
                int nx = sim.StepToward(cur, to);
                if (nx == cur) break;
                hp += sim.StormHpFor(nx);
                cur = nx;
            }
            return hp;
        }

        /// <summary>[嵐 2026-11-13・(b)] HP で払って歩ける歩数 (<see cref="HpWalkSteps"/>) から、 道の上で払う嵐の HP を先に引いたもの。
        /// 嵐の HP が 0 なら <see cref="HpWalkSteps"/> そのもの (嵐の無い層・測定の口 stormEnabled = False の動きを変えない)。</summary>
        private static int HpWalkStepsAfterStorm(RunState run, MapManager mm, int stormHp)
        {
            if (stormHp <= 0 || run == null || run.playerMaxHP <= 0) return HpWalkSteps(run, mm);
            return HpWalkSteps(run, mm, Mathf.Max(0, run.playerHP - stormHp) / (float)run.playerMaxHP);
        }

        /// <summary>今の層の終点がボスか。</summary>
        private static bool LayerHasBoss(MapManager mm)
        {
            var map = mm?.CurrentMap; if (map == null || map.layout == null) return true;
            var g = map.GetNode(map.NodeIdAt(map.layout.goal));
            return g == null || g.type == TileType.Boss || g.type == TileType.Gate;
        }

        /// <summary>点 (<paramref name="hopsFromThere"/> の起点) に物資 <paramref name="supply"/> で立った時、 出口か補給できる店まで歩けるか。
        /// 2026-11-10: 出口までは HP で払って歩く分も数える (<see cref="HpWalkSteps"/>)。</summary>
        private static bool Sustainable(MapManager mm, FreeMapSim sim, int[] hopsFromThere, int goal, int supply, RunState run)
        {
            // 嵐 (2026-11-13・(b)): そこから出口までの道で払う嵐の HP を、 HP で歩ける分から先に引く
            int stormHp = sim != null && sim.StormMode ? StormHpOnPath(sim, System.Array.IndexOf(hopsFromThere, 0), goal) : 0;
            if (hopsFromThere[goal] != int.MaxValue && supply + HpWalkStepsAfterStorm(run, mm, stormHp) >= hopsFromThere[goal] * FreeMapParams.MoveSupply + FreeMapExitMargin) return true;
            if (hopsFromThere[goal] != int.MaxValue && supply >= hopsFromThere[goal] * FreeMapParams.MoveSupply + FreeMapExitMargin) return true;
            int shop = NearestRefuel(mm, sim, hopsFromThere, run);
            return shop >= 0 && hopsFromThere[shop] != int.MaxValue && supply >= hopsFromThere[shop] * FreeMapParams.MoveSupply;
        }

        private bool DoNavigateFree(GameManager gm, MapManager mm)
        {
            var run = gm.Run;
            var sim = mm.Sim;
            var map = mm.CurrentMap;
            if (run == null || sim == null || map == null) { Finish(Outcome.Deadlock, "航行マップの層の状態が無い"); return true; }

            if (!ReferenceEquals(map.layout, _freeMapLayout))
            {
                RecordMapVisit();
                _visitMap = map; _visitDepth = run.Depth;
                _freeMapLayout = map.layout; _freeMapActions = 0; _freeMapTrail.Clear();
                _freeMapVisited.Clear(); _freeMapSinceProgress = 0; _freeMapDetourTarget = -1;
                _foeSeenAt = -1; _foeSeenStill = 0; _foeSeenMove = false; _foeSeenTurn = -1;
                _freeMapReconAt.Clear();
                SnapState(0, run.playerHP);
            }
            if (_freeMapVisited.Add(sim.Me)) _freeMapSinceProgress = 0; else _freeMapSinceProgress++;
            ObserveFoe(sim);
            var L0 = map.layout;
            _freeMapTrail.Enqueue(sim.Me); while (_freeMapTrail.Count > 12) _freeMapTrail.Dequeue();
            if (++_freeMapActions > 2 * MaxFreeMapActionsPerFloor)
            {
                // [計装 2026-11-04] どこで何に詰まったか。 直近の位置は往復しているかを見るため。
                var hops = sim.HopsFrom(sim.Me);
                Finish(Outcome.Deadlock, $"航行マップの層で行動の上限 ({2 * MaxFreeMapActionsPerFloor}) を超えた"
                    + $" / 深さ{run.Depth} Me={sim.Me} goal={L0.goal} 出口まで{(hops[L0.goal] == int.MaxValue ? -1 : hops[L0.goal])}歩"
                    + $" / 物資{run.provision} / 追跡者{(sim.FoeAlive ? $"{sim.FoeMode}@{sim.FoeNode}({(hops[sim.FoeNode] == int.MaxValue ? -1 : hops[sim.FoeNode])}歩)" : "なし")}"
                    + $" / 囮{CountConsumable(run, ItemIds.Lure)} / 直近 {string.Join(",", _freeMapTrail)}");
                return true;
            }
            if (_freeMapActions == MaxFreeMapActionsPerFloor + 1) FreeMapActionCapHits++;

            // ── 回復 (旧航行と同じ規則)。 物資の補充は店の「物資の包」だけ (DoShop) ──
            float hpRatio = run.playerMaxHP > 0 ? (float)run.playerHP / run.playerMaxHP : 1f;
            int floorHit = EstimateFloorMaxHit(run);
            float dangerTarget = FightBudgetDanger(run, floorHit);
            while (Consumables.TryUseBestHeal(run, dangerTarget)) { }
            hpRatio = run.playerMaxHP > 0 ? (float)run.playerHP / run.playerMaxHP : 1f;
            _curBossNear = false;

            var L = map.layout;
            int at = sim.Me;
            bool avoid = FreeNavAvoidsFoe();
            int mode = avoid ? 1 : 0;
            FreeNavDecisions[mode]++;
            var hopsFromMe = sim.HopsFrom(at);

            // [計装 2026-11-04] 戦えない HP か (Rank の低 HP ゲートと同じ閾値)。 下の踏み込みの計数だけが読む。
            //   (同日に試した「重み付きの道」「出口までの HP 取り置き」は、 同一シード 5,000 ラン でクリア率を
            //    13.5% → 10.8% → 6.8% と下げたので撤回した。 早い層の死は判断ではなく HP の収支の問題だった)
            bool cantFightBlind = hpRatio < dangerTarget;
            int nCount = L.Count;

            // ── 補足されている: 囮罠を使う (回避あり) ──
            if (avoid)
            {
                var eta = sim.Eta();
                if (eta.Valid && eta.turns <= LureEtaThreshold && !sim.HasLureAt(at)
                    && HasConsumable(run, ItemIds.Lure) && Consumables.Use(run, ItemIds.Lure))
                {
                    FreeNavLures[mode]++;
                    return false;   // ターンは使わないので、 同じ判断をもう一度する (囮罠は仕掛けた点に残る)
                }
            }

            // ── 〈補給線〉(〈兵站〉r10・2026-11-09): 追われていたら、 追跡者から今より 3 歩以上遠い中継点へ跳ぶ ──
            //   補足は解けない (追跡者は跳んだ先へ向かう) ので、 稼げるのは距離だけ。 ターンを使わないので同じ判断をもう一度する。
            //   「今より遠い」でしか跳ばないので、 跳び返して往復することはない。
            if (sim.FoeAlive && sim.FoeMode == MapSystem.FreeMove.FoeMode.Chase && GameManager.SupplyLineUnlocked
                && run.provision >= FreeMapParams.RelaySupply)
            {
                var fromFoe = sim.HopsFrom(sim.FoeNode);
                int here = fromFoe[at], best = -1, bestD = here == int.MaxValue ? int.MaxValue : here + 2;
                if (here != int.MaxValue)
                    foreach (int r in sim.RelayPoints)
                        if (sim.CanRelayTo(r) && fromFoe[r] != int.MaxValue && fromFoe[r] > bestD) { bestD = fromFoe[r]; best = r; }
                if (best >= 0 && gm.RelayStep(map.NodeIdAt(best))) { FreeMapRelays++; return false; }
            }

            // ── 調べる (2026-11-05): 照明 → 偵察の順。 どちらも行き先を決める前に使い、 開示された戦闘名は Rank (TileDanger) が読む ──
            //   照明: 物資 50 (1 歩分)・ターンは使わない・3 ターンのあいだ 3 歩先まで。 追跡者の察知も 3 歩に伸びるので、 近くに居る時は使わない。
            //   偵察: 1 ターン (追跡者が動き天候の前線も進む)・2 歩先まで。 追われている時は使わない。 同じ点では 1 回まで。
            if (freeNavScout && _freeMapActions <= MaxFreeMapActionsPerFloor)
            {
                int Unscouted(int range)
                {
                    int c = 0;
                    foreach (var n in map.GetAllNodes())
                    {
                        int i = n.index;
                        if (i < 0 || i >= L.Count || i == at || n.activated || sim.IsSunk(i) || sim.Scouted[i]) continue;
                        if (hopsFromMe[i] != int.MaxValue && hopsFromMe[i] <= range) c++;
                    }
                    return c;
                }
                bool chased = sim.FoeAlive && sim.FoeMode == FoeMode.Chase;
                bool foeNear = sim.FoeAlive && (sim.FoeVisible || sim.Stone().level >= 2);
                int exitCost = hopsFromMe[L.goal] == int.MaxValue ? 0 : hopsFromMe[L.goal] * FreeMapParams.MoveSupply;
                if (!sim.FlareOn && !chased && !foeNear
                    && NavSupply(run, sim) >= exitCost + FreeMapExitMargin + FreeMapParams.FlareSupply
                    && Unscouted(FreeMapParams.FlareRange) >= FlareMinUnscouted
                    && gm.UseFlare())
                {
                    FreeNavFlares++;
                    return false;   // ターンは使わない。 見えた上で同じ判断をもう一度する
                }
                if (!sim.FlareOn && !chased && !_freeMapReconAt.Contains(at)
                    && Unscouted(FreeMapParams.ReconRange) >= ReconMinUnscouted
                    && gm.ReconTurn())
                {
                    _freeMapReconAt.Add(at);
                    FreeNavRecons++;
                    return false;
                }
            }

            // ── 行き先 ──
            //   2026-11-09: 物資が出口まで足りなくても、 まだ入っていない店まで届くなら先に店へ寄って買い足す。
            //   店が見つかっていなければ、 照明で周りを照らして探す (物資 1・ターンは使わない)。
            int target = -1;
            //   店は何度でも入れる (2026-11-09) ので、 往復で詰まらないように:
            //   ・補給に行くのは満タンでない時だけ・今いる点は除く
            //   ・満タンでも出口に届かない時は、 今より出口に近い店へ (店を伝って出口へ進む)。 無ければ出口へ (足りない分は HP)
            bool full = run.provision >= run.provisionCap;
            // 嵐 (2026-11-13・(b)): 出口までの道で払う嵐の HP を、 HP で歩ける分から先に引く (嵐の無い層では 0 = 今までどおり)
            bool exitOk = hopsFromMe[L.goal] != int.MaxValue && NavSupply(run, sim) + HpWalkStepsAfterStorm(run, mm, StormHpOnPath(sim, at, L.goal)) >= hopsFromMe[L.goal] * FreeMapParams.MoveSupply + FreeMapExitMargin;
            int refuel = full ? NearestRefuel(mm, sim, hopsFromMe, run, at, sim.HopsFrom(L.goal), sim.HopsFrom(L.goal)[at])
                              : NearestRefuel(mm, sim, hopsFromMe, run, at);
            bool refuelOk = refuel >= 0 && NavSupply(run, sim) >= hopsFromMe[refuel] * FreeMapParams.MoveSupply;
            if (refuel < 0 && !sim.FlareOn && !(sim.FoeAlive && sim.FoeMode == FoeMode.Chase)
                && run.coins >= FreeMapParams.ProvisionPrice && run.provision > FreeMapParams.FlareSupply
                && _freeMapActions <= MaxFreeMapActionsPerFloor)
            {
                int unknown = 0;
                foreach (var n in map.GetAllNodes())
                {
                    int i = n.index;
                    if (i >= 0 && i < L.Count && !n.revealed && !sim.IsSunk(i) && hopsFromMe[i] != int.MaxValue && hopsFromMe[i] <= FreeMapParams.FlareRange) unknown++;
                }
                if (unknown >= 2 && gm.UseFlare()) { FreeMapShopFlares++; FreeNavFlares++; return false; }
            }
            bool toExit = _freeMapActions > MaxFreeMapActionsPerFloor
                          || hopsFromMe[L.goal] == int.MaxValue
                          || (!exitOk && !refuelOk)
                          || (sim.StormMode && sim.StormPhase >= 3);   // 嵐 (2026-11-13・(a)): フェーズ 3 に入ったら出口へ (輪がボスの点へ縮み、 フェーズ 4 はボスの点のほか全部が嵐の中)
            if (!toExit && !exitOk && refuelOk) target = refuel;   // 先に補給
            else if (!toExit && WantsTerritoryTreasure(mm, sim, hopsFromMe, run, hpRatio))
            {
                target = sim.TreasureNode;                          // 縄張りの宝を取りに入る (取ったら下の行き先選びへ戻る = 出る)
                if (_freeMapTreasureLayout != L) { _freeMapTreasureLayout = L; FreeNavTreasureTrips++; }
            }
            else if (!toExit && v3.macroPlan)
            {
                target = MacroTarget(gm, mm, sim, L, at, hopsFromMe, run);
                if (target < 0) toExit = true;
            }
            else if (!toExit)
            {
                int fearR = navFear > 0f ? FearRange() : 0;
                int[] fearHops = fearR > 0 ? FearFoeHops(sim, at) : null;   // [測定 2026-11-12・#35] 恐れ
                float best = 0f;
                foreach (var n in map.GetAllNodes())
                {
                    if (n.index < 0 || n.index == at || n.index == L.goal || n.index == L.start) continue;
                    if (n.activated || sim.IsSunk(n.index)) continue;             // 使い切った点・沈んだ点
                    if (hopsFromMe[n.index] == int.MaxValue) continue;
                    // 行った先から出口か補給できる店まで歩けない点は寄らない
                    if (!Sustainable(mm, sim, sim.HopsFrom(n.index), L.goal, NavSupply(run, sim) - hopsFromMe[n.index] * FreeMapParams.MoveSupply, run)) continue;
                    float desire = !n.revealed ? UnknownNodeDesire
                                 : RankCeil - Rank(n, hpRatio, _curCombatAverse, false, run, dangerTarget);
                    if (desire <= 0f) continue;
                    float cost = hopsFromMe[n.index] * FreeMapParams.MoveSupply;
                    float value = desire / (1f + cost / 100f);
                    if (fearHops != null) value -= navFear * FearCloseness(fearHops, n.index, fearR);
                    // 同点は添字の小さい方 (決定的)
                    if (value > best + 1e-6f) { best = value; target = n.index; }
                }
                if (target < 0) toExit = true;   // 近くの未使用の点が 1 つも無ければ出口へ
            }
            if (toExit) { target = L.goal; _curBossNear = true; FreeNavExits[mode]++; }

            // S4: 次に向かう点の戦闘名が分からず、 近ければ偵察してから決める (同じ点では 1 回まで・追われている時はしない)
            if (v3.superNav && !toExit && target >= 0 && _freeMapActions <= MaxFreeMapActionsPerFloor
                && hopsFromMe[target] <= FreeMapParams.ReconRange
                && !(sim.FoeAlive && sim.FoeMode == FoeMode.Chase) && !_freeMapReconAt.Contains(at))
            {
                var tn = map.GetNode(map.NodeIdAt(target));
                bool unknownFight = tn != null && !tn.encounterRevealed && (!tn.revealed
                    || tn.EffectiveType == TileType.Battle || tn.EffectiveType == TileType.EliteBattle);
                if (unknownFight && gm.ReconTurn())
                {
                    _freeMapReconAt.Add(at);
                    FreeNavRecons++;
                    return false;
                }
            }

            // ── 次の 1 歩 ──
            int hop = sim.StepToward(at, target);
            // 行動の上限 (の手前) を過ぎたら避けるのをやめて出口へ真っ直ぐ歩く ── 避け道の往復で層に閉じ込められない
            if (avoid && _freeMapActions <= MaxFreeMapActionsPerFloor)
            {
                // 縄張りを出ない追跡者 (2026-11-12): 払える遠回りなら、 縄張りを横切らない道を選ぶ (行き先が縄張りの中なら、 踏む縄張りの点が一番少ない道)
                hop = TerritoryAwareStep(mm, sim, run, at, target, hopsFromMe, hop);
                // 新しい点へ着けないまま時間が過ぎたら、 その場の 1 歩の避け方ではなく行き先までの迂回路を引く (2026-11-04)
                if (_freeMapDetourTarget != target) _freeMapDetourTarget = -1;
                if (_freeMapDetourTarget < 0 && _freeMapSinceProgress >= FreeMapStallActions && sim.FoeAlive)
                {
                    _freeMapDetourTarget = target;
                    _freeMapSinceProgress = 0;
                }
                if (_freeMapDetourTarget >= 0)
                {
                    int d = DetourStep(sim, at, target);
                    FreeNavDetours[d >= 0 ? 0 : 1]++;
                    if (d >= 0) hop = d;      // 迂回路が無ければ真っ直ぐ歩く (捕まれば戦う)
                }
                else hop = AvoidFoeStep(sim, at, target, hop);
            }
            // [計装 2026-11-04] 戦えない HP で、 戦闘マス/見えていない点へ踏み込む 1 歩を理由別に数える
            if (cantFightBlind && hop >= 0 && hop < nCount && _cur?.lowHpRiskSteps != null)
            {
                var hn = map.GetNode(map.NodeIdAt(hop));
                bool battleish = hn != null && !hn.activated
                    && (!hn.revealed || hn.EffectiveType == TileType.Battle || hn.EffectiveType == TileType.EliteBattle || hop == L.goal);
                if (battleish)
                    _cur.lowHpRiskSteps[hop == L.goal ? 3 : toExit ? 2 : hop == target ? 0 : 1]++;
            }
            if (hop < 0 || hop == at || !sim.CanMoveTo(hop))
            {
                Finish(Outcome.Deadlock, $"航行マップの層で行き先 {target} へ動けない (Me={at})");
                return true;
            }
            if (!gm.MoveStep(map.NodeIdAt(hop)))
            {
                Finish(Outcome.Deadlock, $"航行マップの層で {hop} へ動けない");
                return true;
            }
            return false;
        }

        // ── 縄張りの宝 (2026-11-12) ──
        /// <summary>[計装] 縄張りの宝を取りに向かうと決めた層の数 (バッチ累計)。</summary>
        public static long FreeNavTreasureTrips;
        private FreeMapLayout _freeMapTreasureLayout;
        /// <summary>追跡者の戦闘で失う HP の見積もり = その層のエリート戦の平均の損 × この倍率 (追跡者はその層のエリートを土台に強くした専用の敵・§5-9)。 仮置き。</summary>
        private const float PursuerVsEliteHpLoss = 1.6f;
        /// <summary>エリート戦の平均の損が学習から読めない時の値 (最大 HP の割合)。 仮置き。</summary>
        private const double EliteHpLossFallback = 0.25;
        /// <summary>通常の戦闘の平均の損が学習から読めない時の値 (最大 HP の割合)。 仮置き。</summary>
        private const double NormalHpLossFallback = 0.10;

        /// <summary>縄張りの宝を取りに行くか (最小の規則): 宝が残っていて・行った先から出口か店まで歩けて、
        /// 「いまの HP の割合 − 追跡者の戦闘で失う HP の見積もり」が <see cref="territoryTreasureHpMargin"/> 以上の時だけ。
        /// 追跡者を倒した後は見積もり 0。 追跡者の性質は読まない (見積もりは層のエリートから)。</summary>
        private bool WantsTerritoryTreasure(MapManager mm, FreeMapSim sim, int[] hopsFromMe, RunState run, float hpRatio)
        {
            if (!sim.TreasureAvailable || territoryTreasureHpMargin >= 1f || _freeMapActions > MaxFreeMapActionsPerFloor) return false;
            int t = sim.TreasureNode;
            if (t == sim.Me || sim.IsSunk(t) || hopsFromMe[t] == int.MaxValue) return false;
            if (!Sustainable(mm, sim, sim.HopsFrom(t), sim.Layout.goal, run.provision - hopsFromMe[t] * FreeMapParams.MoveSupply, run)) return false;
            int layer = Mathf.Clamp(FloorPlan.OrdinalOf(run.Depth), 0, FloorPlan.LastOrdinal);
            float loss = 0f;
            if (sim.FoeAlive)
            {
                if (!EncounterDangerStats.TryGetKind(layer, 'E', out double mean, out _)) mean = EliteHpLossFallback;
                loss = (float)mean * PursuerVsEliteHpLoss;
            }
            // 宝の点そのものの戦闘 (2026-11-12): 宝は点の起動の前に渡るが、 点が戦闘ならその戦闘も払う。 以前は数えていなかった
            loss += TreasureNodeFightLoss(mm, t, layer, run);
            return hpRatio - loss >= territoryTreasureHpMargin;
        }

        /// <summary>宝の点に着いた時の、 点そのものの戦闘で失う HP の見積もり (最大 HP の割合)。 見えている種別が戦闘・エリートならその見積もり
        /// (戦闘名が開示されていればその名前の学習値)、 種別が見えていなければ通常の戦闘 × 0.7 (Super の計画の見えていない点と同じ扱い)、 それ以外は 0。
        /// 使い切った点は起動しないので 0。</summary>
        private static float TreasureNodeFightLoss(MapManager mm, int t, int layer, RunState run)
        {
            var map = mm?.CurrentMap;
            var n = map?.GetNode(map.NodeIdAt(t));
            if (n == null || n.activated) return 0f;
            char kind; float w = 1f;
            if (!n.revealed) { kind = 'N'; w = 0.7f; }
            else if (n.EffectiveType == TileType.Battle) kind = 'N';
            else if (n.EffectiveType == TileType.EliteBattle) kind = 'E';
            else return 0f;
            string key = n.revealed && n.encounterRevealed ? EncounterDangerStats.KeyOf(n, run.Depth) : null;
            if (!(key != null && EncounterDangerStats.TryGet(key, out _, out double mean, out _, out _))
                && !EncounterDangerStats.TryGetKind(layer, kind, out mean, out _))
                mean = kind == 'E' ? EliteHpLossFallback : NormalHpLossFallback;
            return (float)mean * w;
        }

        /// <summary>[計装] 縄張りを横切らない遠回りで、 最短の道の 1 歩と違う 1 歩を選んだ回数 / 遠回りが長すぎるか払えずに最短の道のまま縄張りを踏んだ回数 (バッチ累計)。</summary>
        public static long FreeNavTerritoryDetours, FreeNavTerritoryDetourRefused;

        /// <summary>縄張りを横切らない次の 1 歩 (2026-11-12)。 縄張りを出ない追跡者が生きている層で、
        /// 踏む縄張りの点が一番少なく・その中で一番短い道 (<see cref="FreeMapSim.StepAvoidingTerritory"/>) を、
        /// 最短の道より <see cref="navTerritoryDetour"/> 歩長いところまで・行った先から出口か店まで歩ける時 (<see cref="Sustainable"/>) だけ選ぶ。
        /// 選べなければ <paramref name="direct"/> (最短の道の 1 歩) のまま。 旧い作り (<c>Leashed = false</c>) と −1 では何もしない。</summary>
        private int TerritoryAwareStep(MapManager mm, FreeMapSim sim, RunState run, int at, int target, int[] hopsFromMe, int direct)
        {
            if (navTerritoryDetour < 0 || !sim.Leashed || !sim.FoeAlive || target < 0 || target == at) return direct;
            if (hopsFromMe[target] == int.MaxValue) return direct;
            int step = sim.StepAvoidingTerritory(at, target, out int hops, out int terr);
            if (step == at || hops == int.MaxValue) return direct;
            // 同じ 1 歩か、 最短の道より踏む縄張りの点が減らないなら最短のまま
            if (step == direct || terr >= TerritoryStepsOnShortest(sim, at, target)) return direct;
            int extra = hops - hopsFromMe[target];
            bool afford = extra <= navTerritoryDetour
                && Sustainable(mm, sim, sim.HopsFrom(target), sim.Layout.goal, run.provision - hops * FreeMapParams.MoveSupply, run);
            if (!afford) { FreeNavTerritoryDetourRefused++; return direct; }
            FreeNavTerritoryDetours++;
            return step;
        }

        /// <summary>最短の道 (<see cref="FreeMapSim.StepToward"/> をたどった道) で踏む縄張りの点の数 (at を含めず target を含む)。</summary>
        private static int TerritoryStepsOnShortest(FreeMapSim sim, int at, int target)
        {
            int c = 0, cur = at;
            for (int guard = 0; guard < 200 && cur != target; guard++)
            {
                int nx = sim.StepToward(cur, target);
                if (nx == cur) break;
                cur = nx;
                if (sim.InTerritory(cur)) c++;
            }
            return c;
        }

        /// <summary>[測定 2026-11-12・#35] 恐れの範囲 (歩)。 動かない追跡者はその点だけを怖がる。</summary>
        private int FearRange() => FoeLooksStatic() ? 1 : System.Math.Max(1, navFearRange);

        /// <summary>[測定 2026-11-12・#35] 恐れの重み: 推定した追跡者の位置から <paramref name="r"/> 歩より近いほど大きい (r − 歩数・0 以上)。</summary>
        private static float FearCloseness(int[] foeHops, int node, int r)
        {
            int h = node >= 0 && node < foeHops.Length ? foeHops[node] : int.MaxValue;
            return h == int.MaxValue ? 0f : System.Math.Max(0, r - h);
        }

        /// <summary>[測定 2026-11-12・#35] 推定した追跡者の位置から各点への歩数。 分からない (魔石に反応なし) なら null。
        /// 見えていればその位置。 見えていなければ魔石の段と方角から推す: 方角が ±45° に収まる点のうち、
        /// 自分からの歩数が段の目安 (遠い = <see cref="FreeMapParams.StoneFar"/>・かなり近い/察知 = <see cref="FreeMapParams.StoneNear"/>) に近い点。
        /// 見えていない位置そのものは読まない (魔石と同じ情報だけ)。</summary>
        private int[] FearFoeHops(FreeMapSim sim, int at)
        {
            if (!sim.FoeAlive) return null;
            int foe = -1;
            if (sim.FoeVisible) foe = sim.FoeNode;
            else
            {
                var st = sim.Stone();
                if (st.level <= 0) return null;
                int want = st.level == 1 ? FreeMapParams.StoneFar : FreeMapParams.StoneNear;
                var d = sim.HopsFrom(at);
                int bestScore = int.MaxValue;
                for (int i = 0; i < d.Length; i++)
                {
                    if (i == at || d[i] == int.MaxValue) continue;
                    int bd = System.Math.Abs(sim.Bearing(at, i) - st.bearing); if (bd > 4) bd = 8 - bd;
                    if (bd > 1) continue;
                    int score = System.Math.Abs(d[i] - want) * 2 + bd;
                    if (score < bestScore) { bestScore = score; foe = i; }   // 同点は添字の小さい方
                }
            }
            return foe < 0 ? null : sim.HopsFrom(foe);
        }

        /// <summary>見えている追跡者 / 魔石に出ている方角を避けて、 次の 1 歩を選び直す。
        /// 既定の一歩 <paramref name="preferred"/> が安全ならそのまま。 危ないなら、 行き先までの歩数が増えすぎない範囲で
        /// 追跡者から遠い隣へ。 追跡者の位置が分からない時は、 魔石の方角に向かう隣を避ける。</summary>
        private int AvoidFoeStep(FreeMapSim sim, int at, int target, int preferred)
        {
            if (!sim.FoeAlive) return preferred;
            // 縄張りを出ない追跡者 (2026-11-12): 縄張りの外の点は追跡者も使い魔も来ないので、 そのまま踏む
            if (sim.Leashed && !sim.InTerritory(preferred)) return preferred;
            var stone = sim.Stone();
            bool foeKnown = sim.FoeVisible;
            if (!foeKnown && stone.level < 2) return preferred;   // 遠い / 反応なしなら避けない

            // 動かない追跡者: 近づいても捕まらない。 その点を踏まないことだけを避ける
            if (foeKnown && FoeLooksStatic())
                return preferred != sim.FoeNode ? preferred : FirstSafeNeighbor(sim, at, target, sim.FoeNode, preferred);

            var toTarget = sim.HopsFrom(target);
            var nbrs = sim.Neighbors(at);
            int best = preferred; double bestScore = double.NegativeInfinity;
            double baseline = toTarget[at];
            // 追われている時は、 行き先へ近づく隣の中でだけ避ける (2026-11-04)。
            //   後ずさりを許すと、 出口との間に居座る追跡者の前で 2〜3 点を往復し続け、 学習の 1 ラウンドで
            //   13% のランが行動の上限で打ち切られていた。 近づける避け道が無ければ、 出口へ歩いて戦う。
            bool chased = sim.FoeMode == FoeMode.Chase;
            foreach (int c in nbrs)
            {
                if (!sim.CanMoveTo(c)) continue;
                if (chased && c != preferred && !(toTarget[c] < baseline)) continue;
                double danger;
                if (sim.Leashed && !sim.InTerritory(c)) danger = 0.0;   // 縄張りの外は来ない
                else if (foeKnown)
                {
                    var dFoe = sim.HopsFrom(sim.FoeNode);
                    int step = dFoe[c] == int.MaxValue ? 99 : dFoe[c];
                    danger = step <= FoeAvoidHops ? (FoeAvoidHops + 1 - step) * 4.0 : 0.0;
                }
                else
                {
                    // 魔石の方角 (45° 刻み) との差が小さいほど、 追跡者に近づく隣
                    int b = sim.Bearing(at, c);
                    int diff = System.Math.Abs(b - stone.bearing); if (diff > 4) diff = 8 - diff;
                    danger = diff <= 1 ? (2 - diff) * 3.0 : 0.0;
                }
                double progress = toTarget[c] == int.MaxValue ? -50 : baseline - toTarget[c];   // 近づけば +1
                double score = progress * 2.0 - danger + (c == preferred ? 0.01 : 0.0);
                if (score > bestScore + 1e-9) { bestScore = score; best = c; }
            }
            return best;
        }

        /// <summary>見えている時だけ追跡者の位置を記録する (見えていない位置は読まない)。 足止め中 (囮罠) の静止は数えない。</summary>
        private void ObserveFoe(FreeMapSim sim)
        {
            if (!sim.FoeAlive || !sim.FoeVisible || sim.FoeFreezeTurns > 0) return;
            if (sim.Turn == _foeSeenTurn) return;   // ターンが進んでいない (囮罠を置いただけ) なら数えない
            _foeSeenTurn = sim.Turn;
            if (_foeSeenAt == sim.FoeNode) _foeSeenStill++;
            else { if (_foeSeenAt >= 0) _foeSeenMove = true; _foeSeenAt = sim.FoeNode; _foeSeenStill = 0; }
        }

        /// <summary>この層の追跡者は動かないと見てよいか (一度も動くのを見ておらず、 同じ位置を続けて見た)。</summary>
        private bool FoeLooksStatic() => !_foeSeenMove && _foeSeenStill >= FoeStillToStatic;

        /// <summary>行き先までの迂回路の最初の 1 歩。 追跡者の近く (動かないならその点だけ) を通らない最短の道。
        /// 追跡者の位置は、 見えていればその位置・見えていなければ最後に見た位置。 迂回路が無ければ -1。</summary>
        private int DetourStep(FreeMapSim sim, int at, int target)
        {
            int foe = sim.FoeVisible ? sim.FoeNode : _foeSeenAt;
            if (foe < 0) return -1;
            int keep = FoeLooksStatic() ? 0 : FoeAvoidHops;
            var dFoe = sim.HopsFrom(foe);
            int n = dFoe.Length;
            bool Blocked(int v) => v != target && dFoe[v] != int.MaxValue && dFoe[v] <= keep
                                   && (!sim.Leashed || sim.InTerritory(v));   // 縄張りを出ない追跡者: 縄張りの外は塞がない
            // 行き先から逆に広げて、 自分の隣のうち一番近いものを選ぶ (決定的: 同点は添字の小さい方)
            var dist = new int[n];
            for (int i = 0; i < n; i++) dist[i] = int.MaxValue;
            var q = new Queue<int>();
            dist[target] = 0; q.Enqueue(target);
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                foreach (int v in sim.Neighbors(u))
                {
                    if (dist[v] != int.MaxValue || sim.IsSunk(v)) continue;
                    if (v != at && Blocked(v)) continue;
                    dist[v] = dist[u] + 1; q.Enqueue(v);
                }
            }
            int best = -1, bd = int.MaxValue;
            foreach (int c in sim.Neighbors(at))
            {
                if (!sim.CanMoveTo(c) || Blocked(c) || dist[c] == int.MaxValue) continue;
                if (dist[c] < bd || (dist[c] == bd && c < best)) { bd = dist[c]; best = c; }
            }
            return best;
        }

        /// <summary>avoid を踏まずに行き先へ近づく隣 (無ければ <paramref name="fallback"/>)。</summary>
        private static int FirstSafeNeighbor(FreeMapSim sim, int at, int target, int avoid, int fallback)
        {
            var toTarget = sim.HopsFrom(target);
            int best = -1, bd = int.MaxValue;
            foreach (int c in sim.Neighbors(at))
            {
                if (c == avoid || !sim.CanMoveTo(c) || toTarget[c] == int.MaxValue) continue;
                if (toTarget[c] < bd || (toTarget[c] == bd && c < best)) { bd = toTarget[c]; best = c; }
            }
            return best >= 0 ? best : fallback;
        }

        private static int CountConsumable(RunState run, string id)
        {
            int n = 0;
            if (run?.ownedConsumables != null) foreach (var c in run.ownedConsumables) if (c == id) n++;
            return n;
        }

        private static bool HasConsumable(RunState run, string id)
        {
            if (run?.ownedConsumables == null) return false;
            foreach (var c in run.ownedConsumables) if (c == id) return true;
            return false;
        }

        /// <summary>追跡者の戦闘の頭で逃げるか: HP が少なければ逃げる。</summary>
        private static bool ShouldFleePursuer(RunState run, GameManager gm)
        {
            if (run == null || gm == null || !gm.InPursuerCombat || run.playerMaxHP <= 0) return false;
            return (float)run.playerHP / run.playerMaxHP < PursuerFleeHpRatio;
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

using System;
using System.Collections.Generic;
using MapSystem;
using MapSystem.FreeMove;

namespace AutoTest.FreeNav
{
    /// <summary>そのマスを踏んだ時の見込み。 種別の見えていない点は期待値で渡すこと。</summary>
    public struct NavOutcome
    {
        /// <summary>マスそのものの価値 (Rank の天井からの距離と同じ目盛り。 負 = 行きたくない)。</summary>
        public double value;
        /// <summary>HP 比の増減 (戦闘で −・休憩で +)。</summary>
        public double hpDelta;
        /// <summary>物資の増減 (戦闘の報酬 +・被弾の損 −・補給庫 +200)。</summary>
        public double provDelta;
    }

    /// <summary>航行の方策の種類。 戦闘の技量 (<c>AutoRunner.WiringSkill</c>) と対にする。</summary>
    public enum NavMode
    {
        /// <summary>1 手先だけを最善にする (Optimal)。 今の脅威だけを見て、 次の行き先を 1 つ決める。</summary>
        Myopic = 1,
        /// <summary>層の出口までの道順を読む (Super)。 反応の履歴から縄張りを推定し、 ボス前の HP と持ち越す物資まで勘定する。</summary>
        Macro = 2,
    }

    /// <summary>航行の目盛り。 <b>全部仮置き</b> ── 測定で掃引する前提で、 AutoRunner の公開フィールドから差し替えられる。</summary>
    [Serializable]
    public sealed class FreeNavTuning
    {
        /// <summary>物資 1 あたりの価値 (Rank の目盛り)。 100 物資 ≒ 2 ＝ 通常の戦闘 1 つの 4 分の 1 くらい。</summary>
        public double provValue = 0.02;
        /// <summary>物資が乏しい時の価値の上がり方: λ = provValue × (1 + boost × (1 − 物資/scale))。</summary>
        public double provScarceBoost = 3.0;
        public double provScarceScale = 500.0;
        /// <summary>層を抜けた時に残っている物資の価値の割合 (次の層へ持ち越す燃料)。 Super の終端だけが使う。</summary>
        public double provCarryWeight = 0.5;
        /// <summary>HP 比 1.0 の価値。 Optimal は 1 手ごとの増減に、 Super は出口 (ボス前) の HP に掛ける。</summary>
        public double hpValue = 40.0;
        /// <summary>ボス前に欲しい HP を割った分を何倍に数えるか (Super の終端)。</summary>
        public double bossShortfallMul = 1.5;

        /// <summary>捕まった時の損 (次の層のエリートとの連戦)。 報酬 (物資 +300・アイテム) を差し引いた値のつもり。</summary>
        public double caughtCost = 30.0;
        /// <summary>危険度 1 の場所に道の上で 1 手番いた時の、 捕まる見込みの強さ (1 − e^{−hazard})。</summary>
        public double caughtHazard = 0.35;
        /// <summary>道の外 (気配 0 → 察知距離 2) と、 道の上の忍び足の、 道の上 (察知距離 4) に対する見つかりやすさ。</summary>
        public double offRoadExposure = 0.3;
        public double sneakExposure = 0.3;
        /// <summary>戦闘の物音 (半径 4) と補給庫の物音 (半径 3) で敵を呼ぶ見込み。 着いた点の危険度に掛ける。</summary>
        public double battleNoiseMul = 2.0;
        public double cacheNoiseMul = 1.5;
        /// <summary>縄張りの推定 (熱) の重み。 Super だけ。</summary>
        public double heatWeight = 0.5;

        /// <summary>出口までの物資にこれだけの余裕が無くなったら出口へ (Optimal)。 Super は計画で決めるが、 これの 3 分の 1 を割ったら強制。</summary>
        public double exitMargin = 150.0;
        /// <summary>Optimal: HP の増減を価値に入れる割合。</summary>
        public double myopicHpWeight = 1.0;

        /// <summary>Super: 先の手の「得」を割り引く率 (未知のマス・計画の立て直し)。 損は割り引かない。</summary>
        public double discount = 0.85;
        public int beamWidth = 10;
        public int depth = 6;
        public int branch = 8;
    }

    /// <summary>判断の材料。 BOT が毎回組み立てて渡す。</summary>
    public sealed class FreeNavContext
    {
        public FreeMapSim sim;
        public FreeMapLayout L;
        /// <summary>使い切った点 (MapNode.activated)。 始点は使用済みとして渡す。</summary>
        public bool[] used;
        /// <summary>種別が見えている点 (MapNode.revealed)。</summary>
        public bool[] known;
        public int maxHp;
        public double hp;
        public double provision;
        public double provisionCap;
        /// <summary>ボス前に欲しい HP 比 (Super の終端)。</summary>
        public double bossHpNeed;
        /// <summary>(点, HP 比, 物資) → 見込み。</summary>
        public Func<int, double, double, NavOutcome> outcome;
        public FreeNavBelief belief;
        public FreeNavTuning tune;
        /// <summary>行動の上限に達した ── 出口へ向かう。</summary>
        public bool forceExit;
    }

    /// <summary>判断の結果。</summary>
    public struct NavDecision
    {
        /// <summary>目指す点 (計画の先頭)。</summary>
        public int target;
        /// <summary>いま出す移動の指示先 (道を辿るなら次の点)。 -1 = 動けない。</summary>
        public int hop;
        public MoveSpeed speed;
        public bool toExit;
        public bool fleeing;
        /// <summary>計画の評価値 (計装用)。</summary>
        public double score;
    }

    /// <summary>
    /// 自由移動の層の航行の方策 (2026-09-28)。 素の C# ── UnityEngine にも RunState にも依存しない。
    ///
    /// <para><b>Optimal = 近視眼</b>: 1 手先だけを最善にする。 次の行き先ごとに「マスの価値 ＋ HP と物資の増減 −
    /// 移動の物資 − 捕まる見込み」を足し、 最大の点へ向かう。 敵は<b>いまの</b>脅威 (最後に分かった位置・魔石の反応) しか見ない。</para>
    ///
    /// <para><b>Super = マクロ</b>: 出口までの道順をビーム探索で読み (幅 10・深さ 6・各段 8 手)、 最良の計画の 1 手目だけを指す
    /// (毎回立て直す)。 HP と物資を計画の中で動かし、 終端で<b>ボス前の HP</b> と<b>持ち越す物資</b>を値にする。
    /// 敵は脅威に加えて<b>反応の履歴から推定した縄張り</b>を避ける (補給庫は縄張りの中なので、 物資が乏しい時だけ取りに行く計算になる)。</para>
    ///
    /// <para>共通: 1 本の移動は「直線 (道の外は 2 倍)」「道を辿る」「道の上を忍び足」の安い方。 道の途中に未使用の点がある経路は
    /// 選ばない (着いた点は発動する)。 <b>察知されたら急いで道の外へ</b>逃げる (敵は道しか動けず速さ 1・急ぐは 2・
    /// 道の外の急ぐは察知距離 3)。 乱数は使わず、 同点は添字の小さい方。</para>
    /// </summary>
    public static class FreeNavPlanner
    {
        private const double Inf = double.PositiveInfinity;
        /// <summary>道を辿る経路の途中で、 行き先に選んでいない未使用の点を踏む (発動する) 1 つあたりの罰。</summary>
        private const double DetourPenalty = 1.5;

        private struct Leg
        {
            public bool valid;
            public int hop;
            public MoveSpeed speed;
            public double per;        // 距離 1 あたりの物資
            public double provNeed;   // 物資の見積もり (per × 距離)
            public double pCaught;    // 捕まる見込み
            public double dist;
        }

        // =====================================================================
        //  入口
        // =====================================================================

        public static NavDecision Decide(FreeNavContext c, NavMode mode)
        {
            var sim = c.sim;
            double now = sim.TimeTurns;
            bool macro = mode == NavMode.Macro && c.L.Count <= 64;
            Func<Vec2, double> risk = macro
                ? (Func<Vec2, double>)(p => c.belief.Threat(p, now) + c.tune.heatWeight * c.belief.Heat(p))
                : (p => c.belief.Threat(p, now));
            double lam = Lambda(c, c.provision);

            // 出口までの 1 本 (今の位置から)
            var goalLeg = LegFromHere(c, c.L.goal, risk, lam);

            var d = macro ? DecideMacro(c, risk, lam, goalLeg) : DecideMyopic(c, risk, lam, goalLeg);

            // 察知された・目の前に見えた → 急いで道の外へ
            if (ShouldFlee(c, now))
            {
                var f = Flee(c, d.target, macro, lam, now);
                if (f.hop >= 0) return f;
                d.speed = MoveSpeed.Hurry;
            }
            return d;
        }

        // =====================================================================
        //  Optimal: 近視眼
        // =====================================================================

        private static NavDecision DecideMyopic(FreeNavContext c, Func<Vec2, double> risk, double lam, Leg goalLeg)
        {
            var L = c.L; var t = c.tune;
            bool exit = c.forceExit || !goalLeg.valid || c.provision < goalLeg.provNeed + t.exitMargin;
            int best = -1; double bestNet = 0; Leg bestLeg = default;
            if (!exit)
            {
                for (int j = 0; j < L.Count; j++)
                {
                    if (!IsCandidate(c, j)) continue;
                    var leg = LegFromHere(c, j, risk, lam);
                    if (!leg.valid) continue;
                    double net = StepNet(c, j, leg, c.hp, c.provision, lam, risk, 1.0, useHp: true,
                                         out _, out _);
                    if (net > bestNet + 1e-9) { bestNet = net; best = j; bestLeg = leg; }
                }
            }
            if (best < 0) return Make(L.goal, goalLeg, true, 0);
            return Make(best, bestLeg, false, bestNet);
        }

        // =====================================================================
        //  Super: マクロ (ビーム探索・毎回立て直す)
        // =====================================================================

        private struct Plan
        {
            public int at;        // -1 = 今の位置
            public double hp, prov, acc, est;
            public ulong mask;
            public int first;
            public int depth;
        }

        private static NavDecision DecideMacro(FreeNavContext c, Func<Vec2, double> risk, double lam, Leg goalLeg)
        {
            var L = c.L; var t = c.tune;
            int n = L.Count, goal = L.goal;

            // 物資が本当に尽きかけていたら計画を読まずに出口へ (計画の見込み違いで払底して HP で歩くのを避ける)
            if (c.forceExit || !goalLeg.valid || c.provision < goalLeg.provNeed + t.exitMargin / 3)
                return Make(goal, goalLeg, true, 0);

            // 1 本目 (今の位置から) と、 点どうしの表
            var first = new Leg[n];
            for (int j = 0; j < n; j++) first[j] = IsCandidate(c, j) || j == goal ? LegFromHere(c, j, risk, lam) : default;
            var table = new Leg[n, n];
            var have = new bool[n, n];
            Leg Table(int a, int b)
            {
                if (!have[a, b]) { table[a, b] = LegBetween(c, a, b, risk, lam); have[a, b] = true; }
                return table[a, b];
            }

            // 出口へ今すぐ向かった場合
            double bestScore = CloseToGoal(c, lam, goalLeg, c.hp, c.provision);
            int bestFirst = goal;
            var beam = new List<Plan> { new Plan { at = -1, hp = c.hp, prov = c.provision, acc = 0, mask = 0, first = -1, depth = 0 } };
            var cands = new List<(double net, int j)>();

            for (int depth = 0; depth < t.depth && beam.Count > 0; depth++)
            {
                double gam = Math.Pow(t.discount, depth);
                var children = new List<Plan>();
                foreach (var p in beam)
                {
                    // 枝刈り: その状態から 1 手の見込みが良い順に branch 本
                    cands.Clear();
                    for (int j = 0; j < n; j++)
                    {
                        if (!IsCandidate(c, j) || (p.mask & (1UL << j)) != 0) continue;
                        var leg = p.at < 0 ? first[j] : Table(p.at, j);
                        if (!leg.valid) continue;
                        double net = StepNet(c, j, leg, p.hp, p.prov, lam, risk, 1.0, useHp: true, out _, out _);
                        cands.Add((net, j));
                    }
                    cands.Sort((x, y) => y.net != x.net ? y.net.CompareTo(x.net) : x.j.CompareTo(y.j));
                    int take = Math.Min(t.branch, cands.Count);
                    for (int k = 0; k < take; k++)
                    {
                        int j = cands[k].j;
                        var leg = p.at < 0 ? first[j] : Table(p.at, j);
                        double net = StepNet(c, j, leg, p.hp, p.prov, lam, risk, gam, useHp: false,
                                             out double hp2, out double prov2);
                        if (hp2 <= 0.02) continue;    // 倒れる計画
                        var child = new Plan
                        {
                            at = j, hp = hp2, prov = prov2, acc = p.acc + net,
                            mask = p.mask | (1UL << j), first = p.first < 0 ? j : p.first, depth = depth + 1,
                        };
                        var g = Table(j, goal);
                        child.est = g.valid ? child.acc + CloseToGoal(c, lam, g, hp2, prov2) : double.NegativeInfinity;
                        if (child.est > bestScore + 1e-9
                            || (Math.Abs(child.est - bestScore) <= 1e-9 && child.first < bestFirst))
                        { bestScore = child.est; bestFirst = child.first; }
                        children.Add(child);
                    }
                }
                children.Sort((x, y) =>
                {
                    int cmp = y.est.CompareTo(x.est);
                    if (cmp != 0) return cmp;
                    cmp = x.first.CompareTo(y.first);
                    return cmp != 0 ? cmp : x.at.CompareTo(y.at);
                });
                if (children.Count > t.beamWidth) children.RemoveRange(t.beamWidth, children.Count - t.beamWidth);
                beam = children;
            }

            if (bestFirst == goal) return Make(goal, goalLeg, true, bestScore);
            return Make(bestFirst, first[bestFirst], false, bestScore);
        }

        /// <summary>出口へ向かって層を抜けた時の値: 移動の損を引き、 ボス前の HP と持ち越す物資を値にする。</summary>
        private static double CloseToGoal(FreeNavContext c, double lam, Leg g, double hp, double prov)
        {
            var t = c.tune;
            if (!g.valid) return double.NegativeInfinity;
            double cost = Pay(c, g, prov, lam, out double spent, out double hpPay);
            double hpEnd = hp - hpPay;
            if (hpEnd <= 0) return double.NegativeInfinity;
            double shortfall = Math.Max(0, c.bossHpNeed - hpEnd);
            double provEnd = prov - spent;
            return -cost + t.hpValue * (hpEnd - t.bossShortfallMul * shortfall)
                   + t.provValue * t.provCarryWeight * provEnd;
        }

        // =====================================================================
        //  逃げる
        // =====================================================================

        private static bool ShouldFlee(FreeNavContext c, double now)
        {
            if (c.sim.StoneLevel >= 3) return true;   // 察知されている
            var ls = c.sim.LastSeenFoe;
            return ls.HasValue && now - c.sim.LastSeenFoeTime < 1.0 && Vec2.Dist(ls.Value, c.sim.Me) < 2.0;
        }

        /// <summary>察知された: 距離 1〜4.5 の点のうち、 敵から離れ・計画の行き先に近く・道で結ばれていない点へ、 急いで直線で。</summary>
        private static NavDecision Flee(FreeNavContext c, int planTarget, bool macro, double lam, double now)
        {
            var L = c.L; var sim = c.sim; var me = sim.Me;
            var src = c.belief.ThreatSource(me, now, macro);
            double hurryPer = FreeMapParams.CostOffRoad * MoveSpeed.Hurry.CostMul();
            int best = -1; double bestS = double.NegativeInfinity;
            for (int j = 0; j < L.Count; j++)
            {
                if (j == sim.AtNode) continue;
                var pv = sim.Preview(j);
                if (!pv.valid || pv.blocked) continue;
                double d = pv.distance;
                if (d < 0.8 || d > 4.5) continue;
                double s = 0;
                if (src.HasValue) s += 2.0 * (Vec2.Dist(L.pos[j], src.Value) - Vec2.Dist(me, src.Value));
                if (planTarget >= 0 && planTarget != j) s -= 0.6 * Vec2.Dist(L.pos[j], L.pos[planTarget]);
                s -= lam * hurryPer * d;
                if (pv.road) s -= 2.0;                                  // 道の上を急ぐと察知距離 4
                if (!c.used[j])
                {
                    bool fight = c.known[j]
                        ? (L.types[j] == TileType.Battle || L.types[j] == TileType.EliteBattle)
                        : true;
                    if (fight && j != L.goal) s -= 3.0;                // 追われながら戦闘の物音を立てない
                    if (macro && j != L.goal) s += 0.1 * Math.Max(0, c.outcome(j, c.hp, c.provision).value);
                }
                if (s > bestS + 1e-9) { bestS = s; best = j; }
            }
            if (best < 0) return new NavDecision { target = planTarget, hop = -1 };
            return new NavDecision { target = best, hop = best, speed = MoveSpeed.Hurry, fleeing = true, score = bestS,
                                     toExit = best == L.goal };
        }

        // =====================================================================
        //  1 手の値
        // =====================================================================

        /// <summary>点 j へ行って踏んだ時の正味の値。 得 (マスの価値・物資の収入) に <paramref name="gain"/> の割引を掛け、
        /// 損 (移動の物資・払底の HP・捕まる見込み・物音) は割り引かない。 <paramref name="useHp"/> なら HP の増減も値にする
        /// (Optimal。 Super は HP を状態で運んで終端で値にする)。</summary>
        private static double StepNet(FreeNavContext c, int j, Leg leg, double hp, double prov, double lam,
                                      Func<Vec2, double> risk, double gain, bool useHp, out double hpOut, out double provOut)
        {
            var t = c.tune;
            double cost = Pay(c, leg, prov, lam, out double spent, out double hpPay);
            double hp1 = hp - hpPay, prov1 = prov - spent;
            var o = c.outcome(j, Math.Max(0, hp1), prov1);
            hpOut = Clamp01(hp1 + o.hpDelta);
            provOut = Math.Max(0, Math.Min(c.provisionCap, prov1 + o.provDelta));
            double noise = NoiseCost(c, j, risk);
            double net = gain * (o.value + lam * Math.Max(0, o.provDelta))
                         - lam * Math.Max(0, -o.provDelta) - cost - noise;
            if (useHp) net += t.hpValue * t.myopicHpWeight * (hpOut - hp);
            return net;
        }

        /// <summary>移動の支払い (物資 → 足りない分は HP) と捕まる見込みを値にする。</summary>
        private static double Pay(FreeNavContext c, Leg leg, double prov, double lam, out double spent, out double hpPay)
        {
            var t = c.tune;
            spent = Math.Min(Math.Max(0, prov), leg.provNeed);
            double shortfall = leg.provNeed - spent;
            hpPay = 0;
            if (shortfall > 0 && leg.per > 0 && c.maxHp > 0)
            {
                double hpRate = leg.per <= FreeMapParams.CostRoad ? FreeMapParams.HpPayRoad : FreeMapParams.HpPayOffRoad;
                hpPay = shortfall / leg.per * hpRate / c.maxHp;
            }
            return lam * spent + t.hpValue * hpPay + t.caughtCost * leg.pCaught;
        }

        /// <summary>戦闘・補給庫の物音で近くの敵を呼ぶ見込み。 種別が見えていない点は戦闘の割合 (約半分) で見る。</summary>
        private static double NoiseCost(FreeNavContext c, int j, Func<Vec2, double> risk)
        {
            if (c.used[j] || j == c.L.goal) return 0;
            var t = c.tune;
            double mul;
            if (c.known[j])
            {
                var ty = c.L.types[j];
                mul = ty == TileType.Battle || ty == TileType.EliteBattle ? t.battleNoiseMul
                    : ty == TileType.SupplyCache ? t.cacheNoiseMul : 0;
            }
            else mul = 0.5 * t.battleNoiseMul;
            if (mul <= 0) return 0;
            double r = risk(c.L.pos[j]);
            return t.caughtCost * (1 - Math.Exp(-t.caughtHazard * mul * r));
        }

        private static double Lambda(FreeNavContext c, double prov)
        {
            var t = c.tune;
            double scarce = Clamp01(1 - prov / Math.Max(1, t.provScarceScale));
            return t.provValue * (1 + t.provScarceBoost * scarce);
        }

        private static bool IsCandidate(FreeNavContext c, int j)
            => j != c.L.start && j != c.L.goal && !c.used[j] && j != c.sim.AtNode;

        private static NavDecision Make(int target, Leg leg, bool exit, double score)
            => new NavDecision { target = target, hop = leg.valid ? leg.hop : -1, speed = leg.valid ? leg.speed : MoveSpeed.Normal,
                                 toExit = exit, score = score };

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        // =====================================================================
        //  移動 1 本の選び方
        // =====================================================================

        /// <summary>今の位置 (点の上・道の途中・道の外) から点 j へ。</summary>
        private static Leg LegFromHere(FreeNavContext c, int j, Func<Vec2, double> risk, double lam)
        {
            var sim = c.sim;
            return BestLeg(c, sim.Me, sim.AtNode, sim.Segment, j, risk, lam);
        }

        /// <summary>点 a から点 b へ (計画の中)。</summary>
        private static Leg LegBetween(FreeNavContext c, int a, int b, Func<Vec2, double> risk, double lam)
            => BestLeg(c, c.L.pos[a], a, (-1, -1), b, risk, lam);

        private static Leg BestLeg(FreeNavContext c, Vec2 from, int fromNode, (int a, int b) seg, int to,
                                   Func<Vec2, double> risk, double lam)
        {
            var L = c.L; var t = c.tune;
            var best = default(Leg); double bestCost = Inf;
            if (to == fromNode) return best;

            void Consider(double per, double dist, double timeMul, double exposureMul, double rawExposure, MoveSpeed sp, int hop,
                          double penalty = 0)
            {
                double p = 1 - Math.Exp(-t.caughtHazard * rawExposure * timeMul * exposureMul);
                var leg = new Leg { valid = true, hop = hop, speed = sp, per = per, provNeed = per * dist, pCaught = p, dist = dist };
                double cost = lam * leg.provNeed + t.caughtCost * p + penalty;
                if (cost < bestCost - 1e-9) { bestCost = cost; best = leg; }
            }

            // ① 直線 (道で結ばれていれば道の値段)
            bool roadAdj = fromNode >= 0 ? L.HasRoad(fromNode, to) : (seg.a == to || seg.b == to);
            if (roadAdj || !L.SegmentHitsMountain(from, L.pos[to]))
            {
                double d = Vec2.Dist(from, L.pos[to]);
                double ex = Integrate(from, L.pos[to], risk);
                if (roadAdj)
                {
                    Consider(FreeMapParams.CostRoad, d, 1, 1, ex, MoveSpeed.Normal, to);
                    Consider(FreeMapParams.CostRoad, d, MoveSpeed.Sneak.TimeMul(), t.sneakExposure, ex, MoveSpeed.Sneak, to);
                }
                else Consider(FreeMapParams.CostOffRoad, d, 1, t.offRoadExposure, ex, MoveSpeed.Normal, to);
            }

            // ② 道を辿る。 途中に未使用の点がある経路は<b>罰を足して</b>残す (着いた点は発動する ── 寄り道になる)。
            //   層の最初は全部の点が未使用なので、 これを捨てると山岳の向こうへ行く手段が無くなる。
            //   道の途中なら近い方の端から、 道の外 (逃げた後など) なら直線で行ける点のうち経路が一番短い点から。
            int s = fromNode; double pre = 0; double prePer = FreeMapParams.CostRoad, preExposure = 1;
            if (s < 0 && seg.a >= 0)
            {
                double da = Vec2.Dist(from, L.pos[seg.a]) + c.belief.RoadDist(seg.a, to);
                double db = Vec2.Dist(from, L.pos[seg.b]) + c.belief.RoadDist(seg.b, to);
                s = da <= db ? seg.a : seg.b;
                pre = Vec2.Dist(from, L.pos[s]);
            }
            else if (s < 0)
            {
                double bestPre = Inf;
                for (int i = 0; i < L.Count; i++)
                {
                    if (i == to || double.IsInfinity(c.belief.RoadDist(i, to))) continue;
                    if (L.SegmentHitsMountain(from, L.pos[i])) continue;
                    double v = Vec2.Dist(from, L.pos[i]) + c.belief.RoadDist(i, to);
                    if (v < bestPre - 1e-9) { bestPre = v; s = i; }
                }
                if (s >= 0) { pre = Vec2.Dist(from, L.pos[s]); prePer = FreeMapParams.CostOffRoad; preExposure = t.offRoadExposure; }
            }
            if (s >= 0 && s != to && !double.IsInfinity(c.belief.RoadDist(s, to)))
            {
                int detours = fromNode < 0 && !c.used[s] ? 1 : 0;
                double len = 0, ex = 0;
                int a = s, hops = 0;
                while (a != to && hops++ < 64)
                {
                    int nx = c.belief.RoadNext(a, to);
                    if (nx < 0) break;
                    if (nx != to && !c.used[nx] && nx != L.start) detours++;
                    len += L.Dist(a, nx);
                    ex += Integrate(L.pos[a], L.pos[nx], risk);
                    a = nx;
                }
                // 1 区間だけの経路は ① と同じなので数えない (道の途中・道の外から端へ向かう時だけ意味がある)
                bool single = fromNode >= 0 && c.belief.RoadNext(s, to) == to;
                if (a == to && !single)
                {
                    int hop = fromNode >= 0 ? c.belief.RoadNext(s, to) : s;
                    double preEx = pre > 0 ? Integrate(from, L.pos[s], risk) : 0;
                    // 最初の区間 (道の外から点へ) の物資は道の値段と差があるので、 距離を道の値段へ換算して足す
                    double lenEq = len + pre * prePer / FreeMapParams.CostRoad;
                    double penalty = detours * DetourPenalty;
                    bool preOffRoad = prePer > FreeMapParams.CostRoad;
                    Consider(FreeMapParams.CostRoad, lenEq, 1, 1, ex + preEx * (preOffRoad ? preExposure : 1), MoveSpeed.Normal, hop, penalty);
                    if (!preOffRoad)
                        Consider(FreeMapParams.CostRoad, lenEq, MoveSpeed.Sneak.TimeMul(), t.sneakExposure, ex + preEx, MoveSpeed.Sneak, hop, penalty);
                }
            }
            return best;
        }

        /// <summary>線分に沿った危険度の積分 (距離 0.25 刻み・距離あたり)。</summary>
        private static double Integrate(Vec2 a, Vec2 b, Func<Vec2, double> risk)
        {
            double d = Vec2.Dist(a, b);
            if (d <= 1e-9) return 0;
            int n = Math.Max(1, (int)Math.Ceiling(d / 0.25));
            double s = 0;
            for (int k = 0; k < n; k++) s += risk(Vec2.Lerp(a, b, (k + 0.5) / n));
            return s * d / n;
        }
    }
}

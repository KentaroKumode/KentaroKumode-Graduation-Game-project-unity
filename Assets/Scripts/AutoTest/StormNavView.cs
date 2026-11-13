using System;
using System.Collections.Generic;
using MapSystem;
using MapSystem.FreeMove;

namespace AutoTest
{
    /// <summary>
    /// 嵐の層 (不安定化の時計) の BOT の航行の「見方」(2026-11-14・口 <c>AutoRunner.navStormAware</c>・GAME.md §21-3)。 UnityEngine に依存しない
    /// (検算 Tools/freemap_check が本物をコンパイルして確かめる)。 判断 1 回ぶんの値を作って使い捨てる。
    ///
    /// <para><b>遊び手が画面で見られる情報だけを読む</b>: 地図の点と道・種別が見えている点 (<c>revealed</c>)・今の累計と輪 (<see cref="FreeMapSim.StormRing"/> = 画面の輪)・
    /// 天候・縄張り。 <b>隠れたボスの点 (<see cref="FreeMapLayout.goal"/>) とフェーズ 2 のずらし (<see cref="FreeMapLayout.stormOffset"/>) は読まない</b>。
    /// <see cref="FreeMapSim.InStorm"/> も読まない (ボスの点を常に「嵐の外」と答えるので、 隠れたボスの位置が漏れる)。 乱数は引かない。</para>
    ///
    /// <para><b>数値は本体から読む</b>: 段の境目と長さ (<see cref="Instability.Schedule"/>・<see cref="Instability.TimeOf"/>)・輪の式 (<see cref="Storm.RingAt(FreeMapLayout, int, Storm.Schedule, Vec2, Vec2, double, out Vec2, out double)"/>)・
    /// 嵐の HP (<see cref="FreeMapParams.StormHp"/>・雹 <see cref="FreeMapParams.StormHailHp"/>)・歩く分 (<see cref="Instability.PointsOf"/>)・朱の雪の手番・削る砂の物資・
    /// ボスの点の選び方 (外周 2 層の外 <see cref="Storm.OuterRings"/>・始点からの下限 <see cref="FreeMapParams.StormBossMinHops"/>)・着いた点で溜まる分 (<see cref="Instability.ArrivalPoints"/>)。
    /// 嵐の数値を変えても、 ここは書き直さなくてよい。</para>
    ///
    /// <para><b>見えていないものの見込み</b>:
    /// ボスの点 = 候補 (<see cref="Candidates"/>: 種別が見えていない・外周 2 層に入らない・始点でない・道が 2 本以上・縄張りの外・始点から下限の歩数以上) のどれか (一様)。
    /// フェーズ 2 の終わりの中心 = ボスの点 (ずらしの向きは一様なので平均は 0)。 ただしフェーズ 2・3 の間は、 画面の輪の中心から逆算する (輪は線形に動くので、 今の中心と段の進み具合から決まる)。
    /// フェーズ 3 の終わりの半径 = ボスの点 (見えていなければ候補の平均) から一番遠い隣まで。</para>
    /// </summary>
    public sealed class StormNavView
    {
        public readonly FreeMapSim Sim;
        public readonly FreeMapLayout L;
        /// <summary>見えているボスの点 (-1 = まだ見えていない)。</summary>
        public readonly int Boss;
        /// <summary>ボスの点の候補 (ボスが見えていれば [<see cref="Boss"/>])。 本当のボスの点は必ず入る (検算)。</summary>
        public readonly List<int> Candidates;
        /// <summary>判断した時の不安定化の累計。</summary>
        public readonly int Points;
        /// <summary>各点から出口 (ボスの点) までの歩数の見込み: 見えていればその歩数、 見えていなければ候補までの歩数の平均 (切り上げ)。 着けなければ int.MaxValue。</summary>
        public readonly int[] ExitHops;

        private readonly Storm.Schedule _k;
        private readonly Vec2 _c2, _b;
        private readonly double _r3;
        private readonly int _stepHp;
        private readonly bool _snow, _sand;
        private readonly bool[] _isCand;
        private readonly Dictionary<int, int[]> _distTo = new Dictionary<int, int[]>();
        private readonly Dictionary<int, (Vec2 c, double r)> _ring = new Dictionary<int, (Vec2, double)>();
        private readonly Dictionary<long, Est> _exitMemo = new Dictionary<long, Est>();

        /// <summary>出口までの見込み (嵐で払う HP・歩数。 ボスが見えていなければ候補の平均)。</summary>
        public struct Est { public double hp; public double hops; public bool reachable; public double hpVar; }

        /// <summary>標準正規分布の累積分布 Φ(x) (Abramowitz–Stegun 7.1.26・誤差 1.5e-7)。</summary>
        public static double NormalCdf(double x)
        {
            double z = Math.Abs(x) / Math.Sqrt(2.0);
            double t = 1.0 / (1.0 + 0.3275911 * z);
            double erf = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-z * z);
            return x >= 0 ? 0.5 * (1.0 + erf) : 0.5 * (1.0 - erf);
        }

        /// <summary>HP の割合 <paramref name="hp"/> (見込みの平均) で、 これから <paramref name="need"/> (割合) を払った時に 0 以下になる確率。
        /// HP の見込みの散らばり (分散 <paramref name="var"/>・割合²) を正規で見る: Φ((need − hp) ÷ √var)。 散らばりが無ければ hp ≦ need の時 1・ほかは 0。
        /// HP が減るほど (need に近づくほど) 確率は増え、 HP が need より上の間は hp について凸 (1 の HP の重みが増える)。</summary>
        public static double DeathProb(double hp, double need, double var)
        {
            if (need <= 0) return hp <= 0 ? 1.0 : 0.0;
            if (var <= 1e-12) return hp <= need ? 1.0 : 0.0;
            return NormalCdf((need - hp) / Math.Sqrt(var));
        }

        // 外周の層は層の形だけで決まるので、 層ごとに 1 回だけ求める
        [ThreadStatic] private static FreeMapLayout _ringsFor;
        [ThreadStatic] private static int[] _rings;

        /// <param name="revealed">点ごとに種別が見えているか (地図の MapNode.revealed)。</param>
        /// <param name="knownBoss">種別が見えていて、 ボスと分かっている点 (-1 = 無い)。</param>
        public StormNavView(FreeMapSim sim, bool[] revealed, int knownBoss)
        {
            Sim = sim; L = sim.Layout;
            Points = sim.Instability;
            _k = Instability.Schedule;
            _stepHp = FreeMapParams.StormHp + (sim.Weather == WeatherKind.Hail ? FreeMapParams.StormHailHp : 0);
            _snow = sim.Weather == WeatherKind.Snow;
            _sand = sim.Weather == WeatherKind.Sand;
            Boss = knownBoss;
            Candidates = Boss >= 0 ? new List<int> { Boss } : BossCandidates(sim, revealed);
            _isCand = new bool[L.Count];
            foreach (int c in Candidates) _isCand[c] = true;

            // ボスの点の見込みの位置とフェーズ 3 の終わりの半径
            Vec2 b; double r3;
            if (Boss >= 0) { b = L.pos[Boss]; r3 = Storm.FinalRadiusAt(L, Boss); }
            else
            {
                double x = 0, y = 0, rr = 0;
                foreach (int c in Candidates) { x += L.pos[c].x; y += L.pos[c].y; rr += Storm.FinalRadiusAt(L, c); }
                int n = Math.Max(1, Candidates.Count);
                b = new Vec2(x / n, y / n); r3 = rr / n;
            }
            // フェーズ 2 の終わりの中心: 平均はボスの点。 画面の輪が動いていれば、 その中心から逆算する
            Vec2 c2 = b;
            int t = Instability.TimeOf(Points), ph = Storm.PhaseOf(t, _k);
            if (sim.StormRing(out var cNow, out _))
            {
                var c0 = Storm.MapCenter;
                if (ph == 2)
                {
                    double u = (double)(t - _k.Phase2Start) / _k.len2;
                    if (u > 1e-9) { c2 = c0 + (cNow - c0) * (1.0 / u); if (Boss < 0) b = c2; }
                }
                else if (ph == 3 && Boss >= 0)
                {
                    double u = (double)(t - _k.Phase3Start) / _k.len3;
                    c2 = u < 1 - 1e-9 ? (cNow - b * u) * (1.0 / (1 - u)) : b;
                }
            }
            _c2 = c2; _b = b; _r3 = r3;

            ExitHops = new int[L.Count];
            if (Boss >= 0) Array.Copy(DistTo(Boss), ExitHops, L.Count);
            else
                for (int i = 0; i < L.Count; i++)
                {
                    double s = 0; int n = 0;
                    foreach (int c in Candidates) { int d = DistTo(c)[i]; if (d != int.MaxValue) { s += d; n++; } }
                    ExitHops[i] = n == 0 ? int.MaxValue : (int)Math.Ceiling(s / n - 1e-9);
                }
        }

        /// <summary>隠れたボスの点の候補: 種別が見えていない・外周の 1 層目と 2 層目に入らない・始点でない・道が 2 本以上・縄張り (画面の) の外・
        /// 始点から道で <see cref="FreeMapParams.StormBossMinHops"/> 歩以上 (本体 <see cref="Storm.Apply"/> の選び方を、 見えている情報だけでなぞる)。
        /// 満たす点が無ければ歩数の下限を外し、 それでも無ければ種別が見えていない全部の点 (始点のほか)。</summary>
        public static List<int> BossCandidates(FreeMapSim sim, bool[] revealed)
        {
            var L = sim.Layout;
            if (!ReferenceEquals(_ringsFor, L)) { _rings = Storm.OuterRings(L); _ringsFor = L; }
            var hs = sim.HopsFrom(L.start);
            var res = new List<int>(); var loose = new List<int>(); var any = new List<int>();
            for (int i = 0; i < L.Count; i++)
            {
                if (revealed[i] || i == L.start) continue;
                any.Add(i);
                if (_rings[i] != 0 || L.adj[i].Count < 2 || sim.InTerritory(i)) continue;
                loose.Add(i);
                if (hs[i] != int.MaxValue && hs[i] >= FreeMapParams.StormBossMinHops) res.Add(i);
            }
            return res.Count > 0 ? res : loose.Count > 0 ? loose : any;
        }

        /// <summary>点 t までの歩数の表 (判断の中で使い回す)。</summary>
        public int[] DistTo(int t)
        {
            if (!_distTo.TryGetValue(t, out var d)) { d = Sim.HopsFrom(t); _distTo[t] = d; }
            return d;
        }

        /// <summary>u から t への最短の道の次の 1 歩 (<see cref="FreeMapSim.StepToward"/> と同じ選び方)。 着けなければ u。</summary>
        public int Next(int u, int t)
        {
            if (u == t) return u;
            var d = DistTo(t);
            int best = u;
            foreach (int v in Sim.Neighbors(u)) if (d[v] < d[best]) best = v;
            return best;
        }

        /// <summary>累計 <paramref name="pts"/> の時の輪 (見込み)。</summary>
        public void RingAt(int pts, out Vec2 center, out double radius)
        {
            int t = Instability.TimeOf(pts);
            if (!_ring.TryGetValue(t, out var r))
            {
                Storm.RingAt(L, t, _k, _c2, _b, _r3, out var c, out double rad);
                r = (c, rad); _ring[t] = r;
            }
            center = r.c; radius = r.r;
        }

        /// <summary>累計 <paramref name="pts"/> の時に点 i が嵐の中か (見込み)。 見えているボスの点は外。 静かな間は全部外。 フェーズ 4 はほかの全部。</summary>
        public bool InStormAt(int i, int pts)
        {
            if (i == Boss) return false;
            int t = Instability.TimeOf(pts);
            if (t < 0) return false;
            if (t >= _k.Phase4Start) return true;
            RingAt(pts, out var c, out double r);
            return Vec2.Dist(L.pos[i], c) > r;
        }

        /// <summary>累計 <paramref name="pts"/> の時に点 i へ 1 歩動いて払う嵐の HP (<see cref="FreeMapSim.StormHpFor"/> と同じ値)。</summary>
        public int StepHp(int i, int pts) => InStormAt(i, pts) ? _stepHp : 0;

        /// <summary>累計 <paramref name="pts"/> の時に点 i へ 1 歩動いて溜まる「歩く」の分 (<see cref="FreeMapSim.InstabilityForStep"/> と同じ・近道は数えない)。</summary>
        public int StepPoints(int i, int pts)
            => Instability.Collapsing(pts) ? Instability.PointsOf(InstabilitySource.Walk, _snow && InStormAt(i, pts) ? FreeMapParams.StormSnowTurns : 1) : 0;

        /// <summary>累計 <paramref name="pts"/> の時に点 i へ 1 歩動いて払う物資 (削る砂の嵐の中は上乗せ)。</summary>
        public int StepSupply(int i, int pts) => FreeMapParams.MoveSupply + (_sand && InStormAt(i, pts) ? FreeMapParams.StormSandSupply : 0);

        /// <summary>from から to へ最短の道 (<see cref="Next"/>) を歩いた時に払う嵐の HP。 歩く間に溜まる分で輪が縮むのも数える
        /// (本体の 1 歩と同じく、 HP と「歩く」の分は歩き出す前の累計で決まる)。 <paramref name="exemptLast"/> = 最後の点 (ボスの点の見込み) の HP は払わない。
        /// 着けなければ hops = -1。</summary>
        public int WalkCost(int from, int to, int pts, bool exemptLast, out int ptsEnd, out int hops)
        {
            int cur = from, p = pts, hp = 0; hops = 0;
            for (int guard = 0; guard < 200 && cur != to; guard++)
            {
                int nx = Next(cur, to);
                if (nx == cur) { hops = -1; break; }
                if (!(exemptLast && nx == to)) hp += StepHp(nx, p);
                p += StepPoints(nx, p);
                cur = nx; hops++;
            }
            ptsEnd = p;
            return hp;
        }

        /// <summary>点 from に累計 <paramref name="pts"/> で立って、 そこから出口 (ボスの点) へ歩いた時の見込み。
        /// ボスが見えていなければ候補ごとに歩いた値の平均 (着けない候補は除く)。</summary>
        public Est Exit(int from, int pts)
        {
            long key = (long)from * 100003 + pts;
            if (_exitMemo.TryGetValue(key, out var e)) return e;
            double hp = 0, hp2 = 0, hops = 0; int n = 0;
            foreach (int c in Candidates)
            {
                int h = WalkCost(from, c, pts, true, out _, out int k);
                if (k < 0) continue;
                hp += h; hp2 += (double)h * h; hops += k; n++;
            }
            e = n == 0 ? new Est { reachable = false }
              : new Est { hp = hp / n, hops = hops / n, reachable = true, hpVar = Math.Max(0.0, hp2 / n - (hp / n) * (hp / n)) };
            _exitMemo[key] = e;
            return e;
        }

        /// <summary>ボスを探す行き先 (ボスが見えていない時)。 立つと見える候補 (その点と隣の点・視界 1 歩) の数 ÷ (1 + 歩数) が最大の点。
        /// 同点は添字の小さい方。 見えていればボスの点。 候補が無ければ -1。</summary>
        public int SearchTarget(int at)
        {
            if (Boss >= 0) return Boss;
            var h = Sim.HopsFrom(at);
            int best = -1; double bestV = 0;
            for (int v = 0; v < L.Count; v++)
            {
                if (v == at || h[v] == int.MaxValue || Sim.IsSunk(v)) continue;
                int mass = _isCand[v] ? 1 : 0;
                foreach (int w in Sim.Neighbors(v)) if (_isCand[w]) mass++;
                if (mass == 0) continue;
                double val = mass / (1.0 + h[v]);
                if (val > bestV + 1e-12) { bestV = val; best = v; }
            }
            return best;
        }

        /// <summary>嵐を見込んだ次の 1 歩。 1 歩の費用 = 物資 (<see cref="StepSupply"/>) + 嵐の HP ÷ <see cref="FreeMapParams.HpPerSupply"/>
        /// (物資が尽きた後の 1 歩の値段で HP を物資に直す・本体の交換の比)。 歩くほど溜まって輪が縮むのを、 道の上の歩数ごとに数える。
        /// 見えているボスの点は行き先でなければ通らない (踏むとボス戦に入るので)。 最小の費用の道 (同点は歩数の少ない方・添字の小さい方) の最初の 1 歩。
        /// 着けなければ <see cref="FreeMapSim.StepToward"/>。</summary>
        public int StormStep(int at, int target)
        {
            if (at == target) return at;
            int n = L.Count;
            var cost = new double[n]; var hop = new int[n]; var pts = new int[n]; var first = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++) { cost[i] = double.PositiveInfinity; first[i] = -1; }
            cost[at] = 0; hop[at] = 0; pts[at] = Points;
            while (true)
            {
                int u = -1;
                for (int i = 0; i < n; i++)
                    if (!done[i] && !double.IsPositiveInfinity(cost[i])
                        && (u < 0 || cost[i] < cost[u] - 1e-9 || (Math.Abs(cost[i] - cost[u]) <= 1e-9 && (hop[i] < hop[u] || (hop[i] == hop[u] && i < u)))))
                        u = i;
                if (u < 0 || u == target) break;
                done[u] = true;
                if (u != at && u == Boss) continue;   // 見えているボスの点の先へは広げない
                foreach (int v in Sim.Neighbors(u))
                {
                    if (done[v] || Sim.IsSunk(v)) continue;
                    if (v == Boss && v != target) continue;
                    int p = pts[u];
                    double c = cost[u] + StepSupply(v, p) + (double)StepHp(v, p) / FreeMapParams.HpPerSupply;
                    int hv = hop[u] + 1;
                    if (c < cost[v] - 1e-9 || (Math.Abs(c - cost[v]) <= 1e-9 && hv < hop[v]))
                    {
                        cost[v] = c; hop[v] = hv; pts[v] = p + StepPoints(v, p);
                        first[v] = u == at ? v : first[u];
                    }
                }
            }
            return first[target] >= 0 ? first[target] : Sim.StepToward(at, target);
        }

        /// <summary>点の種別で、 着いた時に溜まる不安定化ポイントの見込み。 表は本体の <see cref="Instability.ArrivalPoints"/> (宝 3・エリート 2・戦闘 1・イベント 0 = 下限) に、
        /// 戦闘の中のエスカレーション (戦闘 1 回あたりの段の見込み <paramref name="escPerFight"/> × <see cref="InstabilitySource.Escalation"/>) と、
        /// 報酬の秘宝 (落ちる率 × <see cref="InstabilitySource.Relic"/>) を足したもの。 種別が見えていない点は通常の戦闘 × <paramref name="unknownWeight"/>
        /// (Super の計画が見えていない点を値付けするのと同じ重み)。</summary>
        public static double ExpectedArrivalPoints(TileType t, bool revealed, double escPerFight, double eliteRelicRate, double normalRelicRate, double unknownWeight)
        {
            double esc = escPerFight * Instability.PointsOf(InstabilitySource.Escalation);
            double relic = Instability.PointsOf(InstabilitySource.Relic);
            if (!revealed) return unknownWeight * (Instability.ArrivalPoints(TileType.Battle, out _) + esc + normalRelicRate * relic);
            double basePts = Instability.ArrivalPoints(t, out _);
            if (t == TileType.EliteBattle) return basePts + esc + eliteRelicRate * relic;
            if (t == TileType.Battle) return basePts + esc + normalRelicRate * relic;
            return basePts;
        }
    }
}

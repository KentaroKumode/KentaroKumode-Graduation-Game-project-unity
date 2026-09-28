using System;
using MapSystem.FreeMove;

namespace AutoTest.FreeNav
{
    /// <summary>
    /// BOT が層ごとに持つ記憶 (2026-09-28): 徘徊エネミーの見込みと、 道の全点対の表。 <b>プレイヤーに見えるものだけから作る</b> ──
    /// 縄張り (<see cref="FreeMapLayout.territory"/>) や敵の実位置は読まない。
    ///
    /// <para>材料は 3 つ: ① 最後に分かった位置 (<see cref="FreeMapSim.LastSeenFoe"/> ── 目視・照明・偵察・罠で更新される
    /// 公開の印) ② 魔石の反応 (敵が自分から距離 4 / 2 以内に居るか・察知されているか) ③ その履歴。</para>
    ///
    /// <para><b>熱 (heat)</b> は「敵がこの辺りを通る」という証拠を点ごとに貯めたもの。 巡回は毎回同じ輪を回るので、
    /// 反応が出た場所は縄張りの推定になる ── 減衰は遅くしてある (層が変われば作り直す)。
    /// <b>脅威 (threat)</b> は「いま敵が近くに居る」という短命の見込みで、 時間とともに広がって薄れる。</para>
    ///
    /// <para>Optimal (近視眼) は脅威だけ、 Super (マクロ) は脅威＋熱を使う。</para>
    /// </summary>
    public sealed class FreeNavBelief
    {
        private readonly FreeMapLayout _L;
        private readonly double[] _heat;
        private double _lastSeenTime = -1;
        private Vec2? _lastSeen;
        private int _stone;
        private Vec2 _stonePos;
        private double _stoneTime = -1;
        private double _lastObsTime = -1;

        // 道の全点対の最短距離と次の点 (Floyd–Warshall)。 層の形は変わらないので 1 回だけ作る。
        private readonly double[,] _roadDist;
        private readonly int[,] _roadNext;

        public FreeNavBelief(FreeMapLayout layout)
        {
            _L = layout ?? throw new ArgumentNullException(nameof(layout));
            _heat = new double[layout.Count];
            int n = layout.Count;
            _roadDist = new double[n, n];
            _roadNext = new int[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) { _roadDist[i, j] = i == j ? 0 : double.PositiveInfinity; _roadNext[i, j] = i == j ? i : -1; }
            for (int i = 0; i < n; i++)
                foreach (int j in layout.adj[i]) { _roadDist[i, j] = layout.Dist(i, j); _roadNext[i, j] = j; }
            for (int k = 0; k < n; k++)
                for (int i = 0; i < n; i++)
                {
                    if (double.IsInfinity(_roadDist[i, k])) continue;
                    for (int j = 0; j < n; j++)
                    {
                        double v = _roadDist[i, k] + _roadDist[k, j];
                        if (v < _roadDist[i, j] - 1e-12) { _roadDist[i, j] = v; _roadNext[i, j] = _roadNext[i, k]; }
                    }
                }
        }

        /// <summary>道だけを通る最短距離 (道で行けなければ +∞)。</summary>
        public double RoadDist(int a, int b) => _roadDist[a, b];
        /// <summary>道だけを通る最短経路の次の点 (a == b なら a、 行けなければ -1)。</summary>
        public int RoadNext(int a, int b) => _roadNext[a, b];

        public FreeMapLayout Layout => _L;
        public int StoneLevel => _stone;
        public double HeatOf(int node) => _heat[node];

        /// <summary>観測を取り込む (判断のたびに呼ぶ)。 同じ時刻に何度呼んでも二重に数えない。</summary>
        public void Observe(FreeMapSim sim)
        {
            double now = sim.TimeTurns;
            if (sim.LastSeenFoe.HasValue && sim.LastSeenFoeTime > _lastSeenTime + 1e-9)
            {
                _lastSeen = sim.LastSeenFoe.Value;
                _lastSeenTime = sim.LastSeenFoeTime;
                AddKernel(_lastSeen.Value, 1.0, 1.5);
            }
            _stone = sim.StoneLevel;
            _stonePos = sim.Me;
            _stoneTime = now;
            if (now - _lastObsTime < 0.5) return;   // 同じ所で続けて呼ばれた分は証拠を足さない
            _lastObsTime = now;
            if (_stone >= 2) AddKernel(sim.Me, 0.5, 2.0);
            else if (_stone == 1) AddRing(sim.Me, 0.12, 2.0, FreeMapParams.VisionStone);
            else
            {
                // 反応なし ＝ いまは距離 4 の内に居ない。 巡回は戻ってくるので消しはせず、 近くだけ少し冷ます
                for (int i = 0; i < _heat.Length; i++)
                    if (Vec2.Dist(_L.pos[i], sim.Me) <= 2.0) _heat[i] *= 0.9;
            }
        }

        /// <summary>いま敵が近くに居る見込み (0〜約 2)。 最後に分かった位置は 6 手番で消え、 その間は半径が広がる。
        /// 魔石の反応は自分の位置を中心に置く (方向は分からない)。</summary>
        public double Threat(Vec2 p, double now)
        {
            double t = 0;
            if (_lastSeen.HasValue)
            {
                double age = now - _lastSeenTime;
                if (age >= 0 && age < 6)
                {
                    double r = 1.0 + age * FreeMapParams.FoeChaseSpeed;
                    double d = Vec2.Dist(p, _lastSeen.Value);
                    t += (1 - age / 6.0) * Math.Max(0, 1 - d / (r + 2));
                }
            }
            if (_stone >= 1 && now - _stoneTime < 1.0)
            {
                double reach = _stone >= 2 ? 3.0 : 5.0;
                double w = _stone >= 3 ? 1.0 : _stone == 2 ? 0.8 : 0.3;
                t += w * Math.Max(0, 1 - Vec2.Dist(p, _stonePos) / reach);
            }
            return t;
        }

        /// <summary>縄張りの推定 (0〜1)。 反応の履歴から貯めた熱を距離で薄めて足す。</summary>
        public double Heat(Vec2 p)
        {
            double s = 0;
            for (int i = 0; i < _heat.Length; i++)
            {
                if (_heat[i] <= 0) continue;
                double d = Vec2.Dist(p, _L.pos[i]) / 1.2;
                s += _heat[i] * Math.Exp(-d * d);
            }
            return 1 - Math.Exp(-s);
        }

        /// <summary>逃げる向きの手がかり: 最後に分かった位置 (新しければ)、 無ければ熱の重心 (自分から距離 4 以内)。
        /// どちらも無ければ null。 <paramref name="useHeat"/> は Super だけ。</summary>
        public Vec2? ThreatSource(Vec2 me, double now, bool useHeat)
        {
            if (_lastSeen.HasValue && now - _lastSeenTime <= 4) return _lastSeen.Value;
            if (!useHeat) return null;
            double sx = 0, sy = 0, sw = 0;
            for (int i = 0; i < _heat.Length; i++)
            {
                if (_heat[i] <= 0) continue;
                if (Vec2.Dist(me, _L.pos[i]) > FreeMapParams.VisionStone) continue;
                sx += _heat[i] * _L.pos[i].x; sy += _heat[i] * _L.pos[i].y; sw += _heat[i];
            }
            if (sw < 0.3) return null;
            return new Vec2(sx / sw, sy / sw);
        }

        private void AddKernel(Vec2 c, double w, double radius)
        {
            for (int i = 0; i < _heat.Length; i++)
            {
                double d = Vec2.Dist(_L.pos[i], c);
                if (d <= radius) _heat[i] += w * (1 - d / (radius + 0.5));
            }
        }

        private void AddRing(Vec2 c, double w, double inner, double outer)
        {
            for (int i = 0; i < _heat.Length; i++)
            {
                double d = Vec2.Dist(_L.pos[i], c);
                if (d > inner && d <= outer) _heat[i] += w;
            }
        }
    }
}

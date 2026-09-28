using System;
using System.Collections.Generic;

namespace MapSystem.FreeMove
{
    /// <summary>層の上の点 (座標は距離の単位。 点どうしの標準の間隔 ≒ 1)。</summary>
    public struct Vec2
    {
        public double x, y;
        public Vec2(double x, double y) { this.x = x; this.y = y; }
        public static double Dist(Vec2 a, Vec2 b)
        {
            double dx = a.x - b.x, dy = a.y - b.y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
        public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new Vec2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        public override string ToString() => $"({x:0.00},{y:0.00})";
    }

    /// <summary>山岳: 通れない楕円の地帯。 点も道も置かない・直線の移動も横切れない。
    ///
    /// <para>向きは角度ではなく<b>単位ベクトル (cos, sin)</b> で持つ。 三角関数は実装ごとに
    /// 最下位ビットが揺れ得るので、 生成を跨いで同じ層を出すために平方根だけで作る。</para></summary>
    public struct Mountain
    {
        public double x, y, a, b, cos, sin;

        /// <summary>点が (余白 margin を足した) 楕円の内側か。</summary>
        public bool Contains(Vec2 p, double margin = 0)
        {
            double dx = p.x - x, dy = p.y - y;
            double u = (dx * cos + dy * sin) / (a + margin);
            double v = (-dx * sin + dy * cos) / (b + margin);
            return u * u + v * v <= 1;
        }
    }

    /// <summary>
    /// 自由移動の層 1 枚ぶんの形 (点・道・山岳・徘徊エネミーの縄張り)。 生成後は変わらない。
    /// 動く状態 (自分・敵・時間・痕跡) は <see cref="FreeMapSim"/> が持つ。
    /// </summary>
    public sealed class FreeMapLayout
    {
        public int floor;
        public Vec2[] pos;
        public TileType[] types;
        /// <summary>道 (無向)。 adj[i] は i と道で結ばれた点。</summary>
        public List<int>[] adj;
        public List<(int a, int b)> roads = new List<(int, int)>();
        public List<Mountain> mountains = new List<Mountain>();
        public int start;
        public int goal;
        /// <summary>6 層の「裂け目の記録」の点。 無い層は -1。</summary>
        public int fixedEvent = -1;

        /// <summary>徘徊エネミーの縄張り (道でつながった 6〜9 点)。 徘徊エネミーが居ない層でも作る (乱数の消費を揃える)。</summary>
        public HashSet<int> territory = new HashSet<int>();
        /// <summary>巡回点 (毎回同じ順で回る輪)。</summary>
        public List<int> waypoints = new List<int>();
        /// <summary>補給庫 (拠) の点。 縄張りの中に 2 か所。</summary>
        public List<int> caches = new List<int>();

        public int Count => pos.Length;

        public double Dist(int i, int j) => Vec2.Dist(pos[i], pos[j]);

        public bool HasRoad(int i, int j) => i >= 0 && j >= 0 && adj[i].Contains(j);

        public bool InMountain(Vec2 p, double margin = 0)
        {
            foreach (var m in mountains) if (m.Contains(p, margin)) return true;
            return false;
        }

        /// <summary>直線 p→q が山岳を横切るか (0.03 刻みで標本化・余白 0.04)。 試作の segHitsMount と同じ。</summary>
        public bool SegmentHitsMountain(Vec2 p, Vec2 q)
        {
            if (mountains.Count == 0) return false;
            for (int k = 0; k <= 34; k++)
            {
                double t = Math.Min(1.0, k * 0.03);
                if (InMountain(Vec2.Lerp(p, q, t), 0.04)) return true;
            }
            return false;
        }

        /// <summary>p に一番近い点。</summary>
        public int NearestNode(Vec2 p)
        {
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < pos.Length; i++)
            {
                double d = Vec2.Dist(p, pos[i]);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>道だけを通る最短経路 (from を含まず to を含む)。 allowed を渡すとその点だけを通る
        /// (巡回は縄張りの外へ出ない)。 allowed で通れなければ制限なしで引き直す。 通れなければ空。</summary>
        public List<int> ShortestRoadPath(int from, int to, HashSet<int> allowed = null)
        {
            var path = new List<int>();
            if (from == to) return path;
            int n = pos.Length;
            var d = new double[n];
            var pr = new int[n];
            var used = new bool[n];
            for (int i = 0; i < n; i++) { d[i] = double.MaxValue; pr[i] = -1; }
            d[from] = 0;
            while (true)
            {
                int u = -1;
                for (int i = 0; i < n; i++) if (!used[i] && (u < 0 || d[i] < d[u])) u = i;
                if (u < 0 || d[u] == double.MaxValue) break;
                used[u] = true;
                if (u == to) break;
                foreach (int v in adj[u])
                {
                    if (allowed != null && !allowed.Contains(v) && v != to) continue;
                    double nd = d[u] + Dist(u, v);
                    if (nd < d[v]) { d[v] = nd; pr[v] = u; }
                }
            }
            if (pr[to] < 0) return allowed != null ? ShortestRoadPath(from, to) : path;
            for (int v = to; v != from && v >= 0; v = pr[v]) path.Insert(0, v);
            return path;
        }

        /// <summary>道だけを通る最短距離 (道で行けなければ +∞)。</summary>
        public double RoadDistance(int from, int to)
        {
            if (from == to) return 0;
            var p = ShortestRoadPath(from, to);
            if (p.Count == 0) return double.PositiveInfinity;
            double s = 0; int a = from;
            foreach (int v in p) { s += Dist(a, v); a = v; }
            return s;
        }

        /// <summary>道だけで全点がつながっているか。</summary>
        public bool IsConnected()
        {
            var seen = new bool[pos.Length];
            var q = new Queue<int>();
            q.Enqueue(start); seen[start] = true; int c = 1;
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                foreach (int v in adj[u]) if (!seen[v]) { seen[v] = true; c++; q.Enqueue(v); }
            }
            return c == pos.Length;
        }
    }
}

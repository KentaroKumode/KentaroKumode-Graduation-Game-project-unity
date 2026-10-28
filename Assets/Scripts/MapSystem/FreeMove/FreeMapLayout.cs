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
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.x - b.x, a.y - b.y);
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.x + b.x, a.y + b.y);
        public static Vec2 operator *(Vec2 a, double k) => new Vec2(a.x * k, a.y * k);
        public override string ToString() => $"({x:0.00},{y:0.00})";
    }

    /// <summary>山岳: 通れない楕円の地帯。 点も道も置かない・道も横切れない。
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
    /// 航行マップ 1 枚ぶんの形 (点・道・山岳・追跡者の縄張り)。 生成後は変わらない。
    /// 動く状態 (自分・追跡者・手番・痕跡・前線で崩れた道) は <see cref="FreeMapSim"/> が持つ。
    /// </summary>
    public sealed class FreeMapLayout
    {
        public int floor;
        public Vec2[] pos;
        public TileType[] types;
        /// <summary>道 (無向)。 adj[i] は i と道で結ばれた点。 並びは道を足した順 (経路の同点はこの順で決まる)。</summary>
        public List<int>[] adj;
        public List<(int a, int b)> roads = new List<(int, int)>();
        public List<Mountain> mountains = new List<Mountain>();
        public int start;
        public int goal;
        /// <summary>6 層の「裂け目の記録」の点。 無い層は -1。</summary>
        public int fixedEvent = -1;
        /// <summary>5 層 (直線) の〈門〉のイベント点。 無い層は -1。 (効果は未実装 ── 旧い〈門〉の 3 つの代償を、 ここへ移す予定)</summary>
        public int gate = -1;

        /// <summary>追跡者の縄張り (道でつながった 7〜11 点)。 追跡者が居ない層でも作る (乱数の消費を揃える)。</summary>
        public HashSet<int> territory = new HashSet<int>();
        /// <summary>巡回の輪: 縄張りの点を中心の周りの角度順に並べたもの (毎回同じ順で回る)。</summary>
        public List<int> waypoints = new List<int>();
        /// <summary>縄張りの中心 (追跡者の最初の居場所)。</summary>
        public int home;

        public int Count => pos.Length;

        public double Dist(int i, int j) => Vec2.Dist(pos[i], pos[j]);

        public bool HasRoad(int i, int j) => i >= 0 && j >= 0 && adj[i].Contains(j);

        public bool InMountain(Vec2 p, double margin = 0)
        {
            foreach (var m in mountains) if (m.Contains(p, margin)) return true;
            return false;
        }

        /// <summary>直線 p→q が山岳を横切るか (0.03 刻みで標本化・余白 0.04)。 道を引けるかの判定に使う。</summary>
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

        /// <summary>道だけを通る歩数 (from から全点)。 つながっていなければ int.MaxValue。</summary>
        public int[] Hops(int from) => Hops(adj, from);

        /// <summary>隣接表を指定した歩数 (崩れた道・沈んだ点を反映した表で数える時に使う)。</summary>
        public static int[] Hops(List<int>[] graph, int from)
        {
            var d = new int[graph.Length];
            for (int i = 0; i < d.Length; i++) d[i] = int.MaxValue;
            var q = new Queue<int>();
            d[from] = 0; q.Enqueue(from);
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                foreach (int v in graph[u])
                    if (d[v] == int.MaxValue) { d[v] = d[u] + 1; q.Enqueue(v); }
            }
            return d;
        }

        /// <summary>from から to へ向かう次の 1 歩 (最短の道・同点は隣接表の並びが先の点)。 着いている / 行けないなら from。</summary>
        public static int StepToward(List<int>[] graph, int from, int to)
        {
            if (from == to) return from;
            var d = Hops(graph, to);
            int best = from;
            foreach (int v in graph[from]) if (d[v] < d[best]) best = v;
            return best;
        }

        /// <summary>道だけで全点がつながっているか。</summary>
        public bool IsConnected()
        {
            foreach (int h in Hops(start)) if (h == int.MaxValue) return false;
            return true;
        }
    }
}

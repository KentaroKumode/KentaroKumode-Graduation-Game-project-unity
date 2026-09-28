using System;
using System.Collections.Generic;
using System.Linq;

namespace MapSystem.FreeMove
{
    /// <summary>
    /// 自由移動の層の生成。 参照実装は docs/design/map-prototype.html の <c>genMap / genMapOnce</c>。
    ///
    /// <para>手順: 山岳 2〜3 か所 → 始点・終点 → 点を撒く (最小間隔 1.15・山岳の外) →
    /// 道 = 最小全域木 (つながりを保証) ＋ 近い組を交差しない範囲で足す → 山岳で分断されたら引き直す →
    /// 徘徊エネミーの縄張り・巡回点・補給庫 → マスの種別。</para>
    ///
    /// <para><b>乱数は <see cref="GameLoop.GameRng"/> のキー "map.free" だけ</b>。 連番は
    /// <c>層 × 1,000,000 + 生成内の通し番号</c> ── 生成の中では順序に依存するが、 生成は層の突入時に
    /// 1 回だけ走るので、 他の抽選点が増減しても層の形は変わらない。</para>
    ///
    /// <para>座標は生成時に <c>float</c> へ丸めて確定する (CLAUDE.md の小数規約)。 三角関数は使わない。</para>
    /// </summary>
    public static class FreeMapGenerator
    {
        public const string RngKey = "map.free";

        /// <summary>マスの抽選重み。 通常層の重みを写し、 休憩だけ試作どおり 3 (旧 2)。
        /// 補給庫・始点・終点・裂け目の記録は固定配置で、 この表からは引かない。</summary>
        public static readonly (TileType type, float weight)[] TileWeights =
        {
            (TileType.Battle,      29f),
            (TileType.Event,       25f),
            (TileType.EliteBattle, 14f),
            (TileType.Exchange,     7f),
            (TileType.Shop,         7f),
            (TileType.Treasure,     3f),
            (TileType.Rest,         3f),
        };

        private sealed class Rnd
        {
            private readonly int _base;
            private int _n;
            public Rnd(int floor) { _base = floor * 1000000; }
            public double Next() => GameLoop.GameRng.Value(RngKey, _base + (_n++ % 1000000));
        }

        private static double F(double v) => (float)v;   // float で確定

        /// <summary>層を生成する。 山岳で分断され続けたら山岳なしで作る (試作と同じ)。</summary>
        public static FreeMapLayout Generate(int floor)
        {
            var rnd = new Rnd(floor);
            for (int attempt = 0; attempt < FreeMapParams.GenerateAttempts; attempt++)
            {
                var r = GenerateOnce(floor, rnd, withMountains: true);
                if (r != null) return r;
            }
            for (int attempt = 0; attempt < FreeMapParams.GenerateAttempts; attempt++)
            {
                var r = GenerateOnce(floor, rnd, withMountains: false);
                if (r != null) return r;
            }
            throw new InvalidOperationException($"[FreeMapGenerator] {floor}層の生成に失敗した");
        }

        private static FreeMapLayout GenerateOnce(int floor, Rnd rnd, bool withMountains)
        {
            const double W = FreeMapParams.Width, H = FreeMapParams.Height;
            var L = new FreeMapLayout { floor = floor };
            var start = new Vec2(FreeMapParams.StartX, FreeMapParams.StartY);
            var goalRef = new Vec2(W - 0.5, H / 2);

            // ── 山岳 ──
            if (withMountains)
            {
                int nm = 2 + (rnd.Next() < 0.5 ? 1 : 0);
                for (int k = 0, guard = 0; k < nm && guard < 200; guard++)
                {
                    double mx = F(2.4 + rnd.Next() * (W - 4.8));
                    double my = F(0.9 + rnd.Next() * (H - 1.8));
                    double ma = F(0.7 + rnd.Next() * 0.7);
                    double mb = F(0.45 + rnd.Next() * 0.5);
                    // 向き: t ∈ [-1,1) の有理パラメータで単位ベクトルを作る (楕円は θ と θ+π が同じなので半周で足りる)
                    double t = 2 * rnd.Next() - 1;
                    double c = F((1 - t * t) / (1 + t * t)), s = F(2 * t / (1 + t * t));
                    var m = new Mountain { x = mx, y = my, a = ma, b = mb, cos = c, sin = s };
                    var mc = new Vec2(mx, my);
                    if (Vec2.Dist(mc, start) < 2 || Vec2.Dist(mc, goalRef) < 1.8) continue;
                    L.mountains.Add(m); k++;
                }
            }

            // ── 点 ──
            var pts = new List<Vec2> { start, new Vec2(W - 0.5, F(1 + rnd.Next() * (H - 2))) };
            if (L.InMountain(pts[1], 0.4)) return null;
            for (int tries = 0; pts.Count < FreeMapParams.NodeCount && tries < 5000; tries++)
            {
                var p = new Vec2(F(0.3 + rnd.Next() * (W - 0.6)), F(0.3 + rnd.Next() * (H - 0.6)));
                if (L.InMountain(p, 0.35)) continue;
                bool ok = true;
                foreach (var q in pts) if (Vec2.Dist(p, q) < FreeMapParams.MinNodeSpacing) { ok = false; break; }
                if (ok) pts.Add(p);
            }
            int n = pts.Count;
            L.pos = pts.ToArray();
            L.start = 0; L.goal = 1;
            L.adj = new List<int>[n];
            for (int i = 0; i < n; i++) L.adj[i] = new List<int>();

            // ── 道: 最小全域木 + 近い組 ──
            var pairs = new List<(double d, int i, int j)>();
            for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) pairs.Add((L.Dist(i, j), i, j));
            // 安定ソート (距離が同じなら添字順) ── List.Sort は不安定なので OrderBy を使う
            pairs = pairs.OrderBy(p => p.d).ThenBy(p => p.i).ThenBy(p => p.j).ToList();
            var par = new int[n];
            for (int i = 0; i < n; i++) par[i] = i;
            int Find(int i) { while (par[i] != i) { par[i] = par[par[i]]; i = par[i]; } return i; }
            bool RoadOk(int i, int j)
            {
                if (L.SegmentHitsMountain(L.pos[i], L.pos[j])) return false;
                foreach (var (a, b) in L.roads) if (SegCross(L.pos, i, j, a, b)) return false;
                return true;
            }
            void AddRoad(int i, int j) { L.roads.Add((i, j)); L.adj[i].Add(j); L.adj[j].Add(i); }
            foreach (var (_, i, j) in pairs)
                if (Find(i) != Find(j) && RoadOk(i, j)) { par[Find(i)] = Find(j); AddRoad(i, j); }
            foreach (var (d, i, j) in pairs)
                if (d < FreeMapParams.ExtraRoadMaxLen && rnd.Next() < FreeMapParams.ExtraRoadChance
                    && RoadOk(i, j) && !L.adj[i].Contains(j))
                    AddRoad(i, j);
            if (!L.IsConnected()) return null;   // 山岳で分断されたら引き直す

            // ── 縄張り: 層の中ほどの一角。 中心から道でつながった 6〜9 点 ──
            var mids = new List<int>();
            for (int i = 2; i < n; i++)
                if (L.pos[i].x > FreeMapParams.TerritoryCenterMinX && L.pos[i].x < W - FreeMapParams.TerritoryCenterMarginRight
                    && L.Dist(i, L.start) >= FreeMapParams.TerritoryStartClearance)
                    mids.Add(i);
            if (mids.Count == 0) return null;
            int center = mids[Math.Min(mids.Count - 1, (int)Math.Floor(rnd.Next() * mids.Count))];
            var terr = new List<int> { center };
            for (int k = 0; k <= 6; k++)
            {
                double radius = 2.2 + 0.4 * k;
                terr = new List<int> { center };
                var q = new Queue<int>(); q.Enqueue(center);
                while (q.Count > 0)
                {
                    int u = q.Dequeue();
                    foreach (int v in L.adj[u])
                        if (v > 1 && !terr.Contains(v) && L.Dist(v, center) <= radius
                            && L.Dist(v, L.start) >= FreeMapParams.TerritoryStartClearance)
                        { terr.Add(v); q.Enqueue(v); }
                }
                if (terr.Count >= FreeMapParams.TerritoryMin) break;
            }
            if (terr.Count > FreeMapParams.TerritoryMax) terr = terr.GetRange(0, FreeMapParams.TerritoryMax);
            if (terr.Count < 3) return null;

            // ── 巡回点: 縄張りの外周寄りの 3〜4 点を中心の周りの角度順に並べて輪にする ──
            int wn = Math.Min(terr.Count >= 7 ? 4 : 3, terr.Count);
            var way = new List<int>();
            while (way.Count < wn)
            {
                int best = -1; double bd = -1;
                foreach (int i in terr)
                {
                    if (way.Contains(i)) continue;
                    double dd = L.Dist(i, center) + 0.01;
                    foreach (int w in way) dd = Math.Min(dd, L.Dist(i, w));
                    if (dd > bd) { bd = dd; best = i; }
                }
                way.Add(best);
            }
            var cpos = L.pos[center];
            way = way.OrderBy(i => PseudoAngle(L.pos[i].x - cpos.x, L.pos[i].y - cpos.y)).ThenBy(i => i).ToList();

            // ── 補給庫 (拠) は縄張りの中に 2 か所 ──
            var caches = terr.Where(i => !way.Contains(i)).Take(2).ToList();
            for (int k = 0; caches.Count < 2 && k < way.Count; k++) if (!caches.Contains(way[k])) caches.Add(way[k]);

            // ── マスの種別 ──
            L.types = new TileType[n];
            L.types[0] = TileType.Outpost;
            L.types[1] = FloorPlan.GoalType(floor);
            float tot = 0; foreach (var (_, w) in TileWeights) tot += w;
            for (int i = 2; i < n; i++)
            {
                if (caches.Contains(i)) { L.types[i] = TileType.SupplyCache; continue; }
                double r = rnd.Next() * tot;
                L.types[i] = TileWeights[TileWeights.Length - 1].type;
                foreach (var (t, w) in TileWeights) { if ((r -= w) < 0) { L.types[i] = t; break; } }
            }
            var others = new List<int>();
            for (int i = 2; i < n; i++) if (!caches.Contains(i)) others.Add(i);
            // 店と休憩は必ず 1 つずつ。 置き換える先は店・休憩以外から選ぶ (試作は店を休憩で上書きし得た)
            foreach (var need in new[] { TileType.Shop, TileType.Rest })
            {
                if (others.Any(i => L.types[i] == need)) continue;
                var pool = others.Where(i => L.types[i] != TileType.Shop && L.types[i] != TileType.Rest).ToList();
                if (pool.Count == 0) pool = others;
                L.types[pool[Math.Min(pool.Count - 1, (int)Math.Floor(rnd.Next() * pool.Count))]] = need;
            }

            // ── 6 層の「裂け目の記録」: 縄張りの外で、 層の 3 分の 1 あたりに一番近い点 ──
            if (floor == 6)
            {
                var anchor = new Vec2(W * 0.35, H / 2);
                int best = -1; double bd = double.MaxValue;
                foreach (int i in others)
                {
                    if (terr.Contains(i) || L.types[i] == TileType.Shop || L.types[i] == TileType.Rest) continue;
                    double d = Vec2.Dist(L.pos[i], anchor);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best < 0) best = others[0];
                L.types[best] = TileType.Event;
                L.fixedEvent = best;
            }

            L.territory = new HashSet<int>(terr);
            L.waypoints = way;
            L.caches = caches;
            return L;
        }

        /// <summary>角度と同じ順序を持つ単調な値 ([-2, 2))。 atan2 の代わり ── 実装差の出ない四則だけで並べる。</summary>
        public static double PseudoAngle(double dx, double dy)
        {
            double s = Math.Abs(dx) + Math.Abs(dy);
            if (s == 0) return 0;
            double p = dx / s;                 // [-1, 1]
            return dy < 0 ? p - 1 : 1 - p;     // 下半分 [-2, 0) ／ 上半分 [0, 2]
        }

        /// <summary>線分 ab と cd が (端点を共有せずに) 交差するか。</summary>
        private static bool SegCross(Vec2[] P, int a, int b, int c, int d)
        {
            if (a == c || a == d || b == c || b == d) return false;
            int O(Vec2 p, Vec2 q, Vec2 r) => Math.Sign((q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x));
            return O(P[a], P[b], P[c]) != O(P[a], P[b], P[d]) && O(P[c], P[d], P[a]) != O(P[c], P[d], P[b]);
        }
    }
}

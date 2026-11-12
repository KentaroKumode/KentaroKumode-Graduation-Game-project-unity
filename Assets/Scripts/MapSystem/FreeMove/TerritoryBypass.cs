using System;
using System.Collections.Generic;
using System.Linq;

namespace MapSystem.FreeMove
{
    /// <summary>
    /// 縄張りを避ける道の保証 (2026-11-13・GAME.md §5-9)。 作者の決定:「不利な遠回りだとしても、 避けられるべき」。
    /// どの層でも、 縄張りの点を 1 つも踏まずに始点から終点へ行ける道があるように、 <b>縄張りの側を削る</b>。
    ///
    /// <para><b>地図の形 (点・道・種類) は変えず、 乱数も引かない</b>。 生成 (<see cref="FreeMapGenerator"/>) の後に、
    /// 形の縄張り (<see cref="FreeMapLayout.territory"/>) から計算し直すだけで、 形は書き換えない
    /// (中断セーブは形の縄張りを写し、 戻す時に同じ計算をするので、 戻した後も同じ縄張りになる)。</para>
    ///
    /// <para>規則 (<see cref="Compute"/>):
    /// <list type="number">
    /// <item>縄張りを踏まない道が既にあれば、 何もしない (形の縄張りそのまま)。</item>
    /// <item>無ければ、 始点から終点への道のうち<b>踏む縄張りの点が一番少ない道</b> (同数なら歩数の短い道・それも同じなら添字の小さい点を先に広げた道)
    ///       を選び、 その道の上の縄張りの点を縄張りから外す。 そのあと、 中心から縄張りの中の道でつながる点だけを残す (道でつながったまとまりのまま)。
    ///       道を選ぶ時、 中心と宝の点は通れないものとして扱う (①)。 それで道が無いか、 宝の点が中心から離れたら、 中心だけを通れないものとして選び直し、
    ///       宝の点は残った縄張りで決め直す (②)。 中心が始点と終点を分ける点 (どの道も中心を通る) なら、 道は何も避けずに選ぶ (③へ)。</item>
    /// <item>残った縄張りが <see cref="FreeMapParams.TerritoryTrimMin"/> 点に満たなければ、 縄張りに道で隣り合う点を、 中心に直線で近い順
    ///       (同点は添字の小さい点) に 1 つずつ足す (<see cref="Grow"/>)。 足せるのは、 選んだ道の上に無い点・始点と終点でない点・始点から直線距離
    ///       <see cref="FreeMapParams.TerritoryStartClearance"/> 以上の点 (生成と同じ条件)。 選んだ道は踏まないので、 避ける道は残る。</item>
    /// <item>それでも下限に満たない (中心の側が道に囲まれて小さい) か、 中心が道の上にある時は、 削った縄張りの残りのまとまりごとに同じように足してみて、
    ///       一番大きくなるまとまりへ中心を移す (③・新しい中心はまとまりの中で元の中心に直線で一番近い点。 大きさが同じなら元の中心に近い方。
    ///       中心の側より大きくなる時だけ移す)。 宝の点は新しい縄張りで決め直す。</item>
    /// <item>最後に <see cref="FreeMapParams.TerritoryHardMin"/> 点 (生成が受け入れる最小と同じ 3 点) に満たなければ、 その層には追跡者を置かない (④・縄張りは空)。</item>
    /// <item>巡回の輪は、 生成と同じ規則 (中心の周りの角度順・同点は添字順) で並べ直す。</item>
    /// </list></para>
    ///
    /// <para>検算 (Tools/freemap_check・追跡者の居る深さ 3/5/6 × 3000 種): 削る前に避ける道がある層 70.5% → 削った後 100%。
    /// 削る層 29.5% (中心と宝を残す 22.8%・宝を決め直す 2.6%・中心を移す 3.7%・追跡者を置かない 0.39%)。数字は GAME.md §5-9。</para>
    /// </summary>
    public static class TerritoryBypass
    {
        public struct Result
        {
            /// <summary>削った後の縄張り。 削らなければ形の縄張りそのもの (同じ参照)。</summary>
            public HashSet<int> territory;
            /// <summary>削った後の巡回の輪。 削らなければ形の輪そのもの (同じ参照)。</summary>
            public List<int> waypoints;
            /// <summary>中心 (追跡者の最初の居場所)。 ③ の時だけ形の中心と違う。</summary>
            public int home;
            /// <summary>宝の点 (削る前の縄張りで決めた点か、 ②③ で決め直した点)。</summary>
            public int treasure;
            /// <summary>0 = 削らない / 1 = 中心と宝を残して削った / 2 = 中心だけ残した (宝の点を決め直した) / 3 = 中心を移した /
            /// 4 = 残る縄張りが <see cref="FreeMapParams.TerritoryHardMin"/> 点に満たない ── この層には追跡者を置かない (縄張りは空)。</summary>
            public int stage;
            /// <summary>形の縄張りから外した点の数。</summary>
            public int removed;
            /// <summary>下限に満たずに足した点の数 (形の縄張りの外の点)。</summary>
            public int added;
        }

        /// <summary>縄張りを踏まずに始点から終点へ行ける道があるか (形の道で数える・<paramref name="terr"/> の点を通らない)。</summary>
        public static bool HasBypass(FreeMapLayout L, ICollection<int> terr)
        {
            var seen = new bool[L.Count];
            var q = new Queue<int>(); seen[L.start] = true; q.Enqueue(L.start);
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                if (u == L.goal) return true;
                foreach (int v in L.adj[u]) if (!seen[v] && !terr.Contains(v)) { seen[v] = true; q.Enqueue(v); }
            }
            return false;
        }

        /// <summary>縄張りを踏まない道が残るように縄張りを削る (上の規則)。 <paramref name="treasure"/> = 削る前の縄張りでの宝の点
        /// (<see cref="FreeMapSim.TreasurePointOf(FreeMapLayout)"/>・-1 = 無い)。 乱数は使わない。</summary>
        public static Result Compute(FreeMapLayout L, int treasure)
        {
            var orig = L.territory;
            var res = new Result { territory = orig, waypoints = L.waypoints, home = L.home, treasure = treasure };
            if (orig.Count == 0 || HasBypass(L, orig)) return res;

            int home = L.home;
            var path = Path(L, new[] { home, treasure });
            HashSet<int> keep = path == null ? null : Grow(L, Component(L, home, path), home, path);
            if (keep != null && (treasure < 0 || keep.Contains(treasure))) res.stage = 1;
            else
            {
                path = Path(L, new[] { home });
                if (path != null) { res.stage = 2; keep = Grow(L, Component(L, home, path), home, path); }
                else { res.stage = 3; path = Path(L, new int[0]); keep = null; }
            }
            // 中心の側が下限に満たない (③ では中心そのものが道の上) なら、 残りのうち一番大きいまとまりへ中心を移す
            if (keep == null || keep.Count < FreeMapParams.TerritoryTrimMin)
            {
                int bestHome = -1; HashSet<int> best = null; double bestD = double.MaxValue;
                var seen = new HashSet<int>();
                foreach (int c0 in orig.OrderBy(i => i))
                {
                    if (path.Contains(c0) || seen.Contains(c0)) continue;
                    var comp = Component(L, c0, path);
                    seen.UnionWith(comp);
                    int h = -1; double hd = double.MaxValue;
                    foreach (int i in comp) { double d = L.Dist(i, L.home); if (d < hd || (d == hd && i < h)) { hd = d; h = i; } }
                    var g = Grow(L, comp, h, path);
                    if (best == null || g.Count > best.Count || (g.Count == best.Count && hd < bestD)) { best = g; bestHome = h; bestD = hd; }
                }
                if (best == null)
                {
                    // 形の縄張りが全部道の上: 道の上に無い点のうち、 元の中心に直線で一番近い点を中心にして、 足して作る
                    for (int i = 2; i < L.Count; i++)
                    {
                        if (i == L.start || path.Contains(i) || L.Dist(i, L.start) < FreeMapParams.TerritoryStartClearance) continue;   // 嵐の層は始点が添字 0 とは限らない (2026-11-13)
                        double d = L.Dist(i, L.home);
                        if (d < bestD || (d == bestD && i < bestHome)) { bestD = d; bestHome = i; }
                    }
                    if (bestHome >= 0) best = Grow(L, new HashSet<int> { bestHome }, bestHome, path);
                }
                if (best != null && (keep == null || best.Count > keep.Count))
                {
                    if (bestHome != home) res.stage = 3;
                    keep = best; home = bestHome;
                }
            }
            if (keep == null || keep.Count < FreeMapParams.TerritoryHardMin)
            {
                // それでも小さすぎる (避ける道に囲まれた小さな一角しか残らない): この層には追跡者を置かない
                res.stage = 4; res.territory = new HashSet<int>(); res.waypoints = new List<int>(); res.treasure = -1;
                res.removed = orig.Count;
                return res;
            }
            int before = keep.Count(i => orig.Contains(i));
            res.territory = keep;
            res.home = home;
            res.removed = orig.Count - before;
            res.added = keep.Count(i => !orig.Contains(i));
            var hp = L.pos[home];
            res.waypoints = keep.OrderBy(i => FreeMapGenerator.PseudoAngle(L.pos[i].x - hp.x, L.pos[i].y - hp.y)).ThenBy(i => i).ToList();
            if (res.stage != 1) res.treasure = treasure < 0 ? -1 : FreeMapSim.TreasurePointOf(L, keep, home);
            return res;
        }

        /// <summary>縄張り <paramref name="keep"/> が下限に満たなければ、 道で隣り合う点を中心 <paramref name="home"/> に直線で近い順 (同点は添字の小さい点) に 1 つずつ足す。
        /// 足せるのは、 道 <paramref name="path"/> の上に無い点・始点と終点でない点・始点から直線距離 <see cref="FreeMapParams.TerritoryStartClearance"/> 以上の点。</summary>
        private static HashSet<int> Grow(FreeMapLayout L, HashSet<int> keep, int home, HashSet<int> path)
        {
            keep = new HashSet<int>(keep);
            while (keep.Count < FreeMapParams.TerritoryTrimMin)
            {
                int pick = -1; double pd = double.MaxValue;
                foreach (int u in keep)
                    foreach (int v in L.adj[u])
                    {
                        if (v <= 1 || v == L.start || keep.Contains(v) || path.Contains(v) || L.Dist(v, L.start) < FreeMapParams.TerritoryStartClearance) continue;
                        double d = L.Dist(v, home);
                        if (d < pd || (d == pd && v < pick)) { pd = d; pick = v; }
                    }
                if (pick < 0) break;
                keep.Add(pick);
            }
            return keep;
        }

        /// <summary>始点から終点への、 踏む縄張りの点が一番少ない道 (同数なら歩数の短い道・添字の小さい点を先に広げる) の点の集合。
        /// <paramref name="blocked"/> の点は通らない。 道が無ければ null。</summary>
        private static HashSet<int> Path(FreeMapLayout L, int[] blocked)
        {
            var orig = L.territory;
            int n = L.Count;
            var cost = new int[n]; var prev = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++) { cost[i] = int.MaxValue; prev[i] = -1; }
            cost[L.start] = 0;
            while (true)
            {
                int u = -1;
                for (int i = 0; i < n; i++) if (!done[i] && cost[i] != int.MaxValue && (u < 0 || cost[i] < cost[u])) u = i;   // 同点は添字の小さい点
                if (u < 0 || u == L.goal) break;
                done[u] = true;
                foreach (int v in L.adj[u])
                {
                    if (done[v] || Array.IndexOf(blocked, v) >= 0) continue;
                    int c = cost[u] + 1 + (orig.Contains(v) ? 1000 : 0);   // 費用 = 踏む縄張りの点の数 × 1000 + 歩数 (点は 50 程度)
                    if (c < cost[v]) { cost[v] = c; prev[v] = u; }
                }
            }
            if (cost[L.goal] == int.MaxValue) return null;
            var p = new HashSet<int>();
            for (int v = L.goal; v >= 0; v = prev[v]) p.Add(v);
            return p;
        }

        /// <summary>形の縄張りから道の上の点を除き、 <paramref name="home"/> から縄張りの中の道でつながる点だけを残した集合。</summary>
        private static HashSet<int> Component(FreeMapLayout L, int home, HashSet<int> path)
        {
            var orig = L.territory;
            var keep = new HashSet<int> { home };
            var q = new Queue<int>(); q.Enqueue(home);
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                foreach (int v in L.adj[u])
                    if (orig.Contains(v) && !path.Contains(v) && keep.Add(v)) q.Enqueue(v);
            }
            return keep;
        }
    }
}

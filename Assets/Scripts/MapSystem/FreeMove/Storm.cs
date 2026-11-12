using System;
using System.Collections.Generic;
using System.Linq;

namespace MapSystem.FreeMove
{
    /// <summary>
    /// 嵐 (2026-11-13・天候の作り直し・docs/GAME.md §5-12)。 旧い「前線」(左から右へ進む壁) の置き換え。
    ///
    /// <para>層に入ると、 静かな時間 (<see cref="FreeMapParams.StormQuietTurns"/> 手番・深さ 6 は 0) のあとに嵐が来る。
    /// <b>輪 (安全な円)</b> の外が「嵐の中」で、 輪は 4 つの段で縮む:
    /// フェーズ 1 = 地図の中心のまま、 全部の点を含む大きさ → その 60% /
    /// フェーズ 2 = 中心がボスの点の近く (少しずらした位置) へ寄りながら 60% → 30% /
    /// フェーズ 3 = 中心がボスの点へ移り、 30% → ボスの点とその隣の点が入る大きさ (この段に入るとボスの点の種別が見える) /
    /// フェーズ 4 = ボスの点のほかは全部が嵐の中。</para>
    ///
    /// <para><b>点が嵐の中か</b>は、 点の座標と、 その時の嵐の時計 (= 手番 + イベントのずらし) だけで決まる純粋な関数 (<see cref="InStorm"/>)。
    /// 点・道・種別には触らない (作者の決定:「地図のノードに干渉するのは禁止」)。 <b>ボスの点 (終点) は常に嵐の外</b>。</para>
    ///
    /// <para>層の形への手当て (<see cref="Apply"/>): 始点を地図の最外周 (点の凸包の頂点) から選び直す。 点と道は作り直さない。
    /// 乱数は専用のキー <see cref="RngKey"/> (連番は層 × 100 + 用途) だけで、 既存のキーの引く回数を変えない。</para>
    ///
    /// <para>測定の口 <c>GameHooks.StormEnabled = False</c> では、 この型は何もしない (層の形も <see cref="FreeMapSim"/> の動きも今までの前線のまま)。</para>
    /// </summary>
    public static class Storm
    {
        public const string RngKey = "map.free.storm";

        /// <summary>嵐のある深さ: 1・3・5・6 (画面の第 1〜4 層)。 深さ 0 (序層)・7 (画面の第 5 層の 1 本道)・8 (最終段・地図なし) は無い。</summary>
        public static bool HasStorm(int depth) => depth == 1 || depth == 3 || depth == 5 || depth == 6;

        /// <summary>静かな時間 (手番)。 深さ 6 (画面の第 4 層) は 0 ── 層に入った時からフェーズ 1。</summary>
        public static int QuietTurns(int depth) => depth == 6 ? 0 : FreeMapParams.StormQuietTurns;

        /// <summary>引いた天候を嵐の種類へ読み替える。 崩れる地平・沈み続ける湖面は点や道を消すので廃止 ── 引いた層は種類なし (素の嵐)。
        /// 引く処理 (<see cref="WeatherInfo.ForFloor"/>) は変えない (乱数の対応をずらさない)。</summary>
        public static WeatherKind KindOf(WeatherKind drawn)
            => drawn == WeatherKind.Crumble || drawn == WeatherKind.Sink ? WeatherKind.None : drawn;

        /// <summary>嵐の種類の名前 (種類なしは「嵐」)。</summary>
        public static string KindName(WeatherKind k) => k == WeatherKind.None ? "嵐" : k.DisplayName();

        // =====================================================================
        //  段
        // =====================================================================

        /// <summary>段の境目 (嵐が来てからの手番)。 フェーズ 1 の始まり = 0。</summary>
        public static int Phase2Start => FreeMapParams.StormPhase1Turns;
        public static int Phase3Start => FreeMapParams.StormPhase1Turns + FreeMapParams.StormPhase2Turns;
        public static int Phase4Start => Phase3Start + FreeMapParams.StormPhase3Turns;

        /// <summary>嵐が来てからの手番 <paramref name="s"/> (負 = 静かな時間) → 段 (0 = 静かな時間 / 1〜4)。</summary>
        public static int PhaseOf(int s)
            => s < 0 ? 0 : s < Phase2Start ? 1 : s < Phase3Start ? 2 : s < Phase4Start ? 3 : 4;

        /// <summary>次の段までの手番 (静かな時間なら嵐まで)。 フェーズ 4 は 0。</summary>
        public static int TurnsToNext(int s)
            => s < 0 ? -s : s < Phase2Start ? Phase2Start - s : s < Phase3Start ? Phase3Start - s : s < Phase4Start ? Phase4Start - s : 0;

        // =====================================================================
        //  輪 (安全な円)
        // =====================================================================

        /// <summary>地図の中心 (層の広さの真ん中)。</summary>
        public static Vec2 MapCenter => new Vec2(FreeMapParams.Width / 2.0, FreeMapParams.Height / 2.0);

        /// <summary>フェーズ 1 の始まりの半径: 地図の中心から一番遠い点まで (全部の点が輪の中)。</summary>
        public static double FullRadius(FreeMapLayout L)
        {
            var c = MapCenter; double r = 0;
            for (int i = 0; i < L.Count; i++) r = Math.Max(r, Vec2.Dist(c, L.pos[i]));
            return r + 1e-6;
        }

        /// <summary>フェーズ 3 の終わりの半径: ボスの点から一番遠い隣の点まで (ボスの点とその隣の点が全部入る)。</summary>
        public static double FinalRadius(FreeMapLayout L)
        {
            double r = 0;
            foreach (int v in L.adj[L.goal]) r = Math.Max(r, L.Dist(L.goal, v));
            return r + 1e-6;
        }

        /// <summary>フェーズ 2 の終わりの輪の中心 (ボスの点を、 層の生成時に決めた量だけずらした位置・<see cref="FreeMapLayout.stormOffset"/>)。</summary>
        public static Vec2 Phase2Center(FreeMapLayout L) => L.pos[L.goal] + L.stormOffset;

        /// <summary>嵐が来てから <paramref name="s"/> 手番の輪。 静かな時間 (s &lt; 0) はフェーズ 1 の始まりと同じ (全部が輪の中)。
        /// フェーズ 4 は半径 0 (ボスの点の外は全部が嵐の中・<see cref="InStorm"/> はボスの点を別に扱う)。 段の途中は線形に補間する。</summary>
        public static void RingAt(FreeMapLayout L, int s, out Vec2 center, out double radius)
        {
            double r0 = FullRadius(L);
            var c0 = MapCenter; var c2 = Phase2Center(L); var b = L.pos[L.goal];
            double r1 = r0 * FreeMapParams.StormPhase1EndRadius, r2 = r0 * FreeMapParams.StormPhase2EndRadius, r3 = FinalRadius(L);
            if (s <= 0) { center = c0; radius = r0; return; }
            if (s < Phase2Start)
            {
                double u = (double)s / FreeMapParams.StormPhase1Turns;
                center = c0; radius = r0 + (r1 - r0) * u; return;
            }
            if (s < Phase3Start)
            {
                double u = (double)(s - Phase2Start) / FreeMapParams.StormPhase2Turns;
                center = Vec2.Lerp(c0, c2, u); radius = r1 + (r2 - r1) * u; return;
            }
            if (s < Phase4Start)
            {
                double u = (double)(s - Phase3Start) / FreeMapParams.StormPhase3Turns;
                center = Vec2.Lerp(c2, b, u); radius = r2 + (r3 - r2) * u; return;
            }
            center = b; radius = 0;
        }

        /// <summary>点 i が、 嵐が来てから <paramref name="s"/> 手番の時に嵐の中か。 ボスの点 (終点) は常に外。 静かな時間は全部外。
        /// フェーズ 4 はボスの点のほかの全部。 それ以外は輪 (<see cref="RingAt"/>) の外 (中心からの距離が半径より大きい) の点。</summary>
        public static bool InStorm(FreeMapLayout L, int i, int s)
        {
            if (i == L.goal || s < 0) return false;
            if (s >= Phase4Start) return true;
            RingAt(L, s, out var c, out double r);
            return Vec2.Dist(L.pos[i], c) > r;
        }

        // =====================================================================
        //  層の形への手当て (生成の後に 1 回)
        // =====================================================================

        /// <summary>
        /// 嵐の層の形を整える (生成の後・<see cref="FreeMapGenerator.Generate"/> の storm = true の時だけ)。 点と道は作り直さない。
        /// <list type="number">
        /// <item><b>始点を選び直す</b>: 点の凸包の頂点 (外周の 1 層目) のうち、 生成の終点 (右端) でない・道が 2 本以上・
        /// 追跡者の居る深さでは縄張りの全部の点から直線で <see cref="FreeMapParams.TerritoryStartClearance"/> 以上の点から、 専用の乱数 (<see cref="RngKey"/>) で 1 つ。
        /// 候補が無ければ生成の始点のまま。 選んだ点と生成の始点 (添字 0) の種別を入れ替える。</item>
        /// <item><b>ボスの点 (終点) を選び直す</b> (2026-11-13・作者「外周 2 ノードではないノードで、 ランダムに」): 外周の 1 層目・2 層目 (<see cref="OuterRings"/>) に入らない「内側」の点のうち、
        /// 始点でない・縄張りの中でない・道が 2 本以上・(深さ 6 の旧い作りの) 固定の点でない・始点から道で <see cref="FreeMapParams.StormBossMinHops"/> 歩以上の点から、
        /// 専用の乱数 (<see cref="BossRngKey"/>) で 1 つ。 満たす点が無ければ下限を 1 歩ずつ下げる (<see cref="FreeMapLayout.stormBossHopsUsed"/>)。
        /// 内側の点が無ければ生成の終点のまま。 選んだ点と生成の終点 (添字 1) の種別を入れ替える。 始点を先に決め、 距離の下限はボスの側だけで確かめる (順序を循環させない)。</item>
        /// <item><b>危険の帯を始点と終点からの距離で読み直す</b> (<see cref="FreeMapLayout.BandOf"/>)。 新しい安全な帯のエリートは通常の戦闘へ替える。</item>
        /// <item><b>フェーズ 2 の終わりの輪の中心のずらし</b> (<see cref="FreeMapLayout.stormOffset"/>)。</item>
        /// </list>
        /// 深さ 6 の「裂け目の記録」(ボスに隣り合う点を全部イベントに) は、 この後に生成器が新しい終点で置く (<see cref="FreeMapGenerator.Generate"/>)。
        /// 縄張りを避ける道 (<see cref="TerritoryBypass"/>) は始点・終点を <see cref="FreeMapLayout.start"/> / <see cref="FreeMapLayout.goal"/> で読むので、 新しい点でそのまま働く。
        /// </summary>
        public static void Apply(FreeMapLayout L)
        {
            if (L == null || !HasStorm(L.floor)) return;
            L.storm = true;
            int floor = L.floor, oldStart = L.start, oldGoal = L.goal;
            var rings = OuterRings(L);
            var cand = StartCandidates(L);
            L.stormStartFallback = cand.Count == 0;
            if (cand.Count > 0)
            {
                int pick = cand[GameLoop.GameRng.Range(0, cand.Count, RngKey, floor * 100 + 0)];
                if (pick != oldStart)
                {
                    var t = L.types[pick]; L.types[pick] = L.types[oldStart]; L.types[oldStart] = t;
                    L.start = pick;
                }
            }
            // ボスの点: 内側の点から (始点からの下限を満たす点が無ければ 1 歩ずつ下げる)
            var inner = BossCandidates(L, rings);
            L.stormBossInner = inner.Count;
            L.stormBossHopsUsed = -1;
            if (inner.Count > 0)
            {
                var hs = L.Hops(L.start);
                for (int th = FreeMapParams.StormBossMinHops; th >= 1; th--)
                {
                    var ok = inner.Where(i => hs[i] != int.MaxValue && hs[i] >= th).ToList();
                    if (ok.Count == 0) continue;
                    int pick = ok[GameLoop.GameRng.Range(0, ok.Count, BossRngKey, floor * 100 + 0)];
                    var t = L.types[pick]; L.types[pick] = L.types[oldGoal]; L.types[oldGoal] = t;
                    L.goal = pick;
                    L.stormBossHopsUsed = th;
                    break;
                }
            }
            // 新しい安全な帯 (始点の側) のエリートは通常の戦闘へ (生成と同じ規則・乱数なし)
            for (int i = 0; i < L.Count; i++)
                if (i != L.start && i != L.goal && L.types[i] == TileType.EliteBattle && L.BandOf(i) == FreeMapParams.BandSafe)
                    L.types[i] = TileType.Battle;
            // フェーズ 2 の終わりの輪の中心のずらし (float で確定)
            double r2 = FullRadius(L) * FreeMapParams.StormPhase2EndRadius;
            double f = FreeMapParams.StormOffsetMin + (FreeMapParams.StormOffsetMax - FreeMapParams.StormOffsetMin) * GameLoop.GameRng.Value(RngKey, floor * 100 + 1);
            double t2 = 2 * GameLoop.GameRng.Value(RngKey, floor * 100 + 2) - 1;          // 半周の向き (山岳の向きと同じ有理の作り)
            double cx = (1 - t2 * t2) / (1 + t2 * t2), cy = 2 * t2 / (1 + t2 * t2);
            if (GameLoop.GameRng.Value(RngKey, floor * 100 + 3) < 0.5f) { cx = -cx; cy = -cy; }   // 残りの半周
            L.stormOffset = new Vec2((float)(cx * f * r2), (float)(cy * f * r2));
        }

        /// <summary>ボスの点を選ぶ乱数のキー (2026-11-13・新しいキー。 既存のキーの引く回数を変えない)。</summary>
        public const string BossRngKey = "map.free.storm.boss";

        /// <summary>外周の 1 層目と 2 層目 (2026-11-13)。 k 層目 = 残りの点の凸包の頂点と、 その凸包の辺から直線で
        /// <see cref="FreeMapParams.StormOuterEdgeSlack"/> 以内の残りの点 (辺にほぼ乗っていて、 見た目で外周に見える点)。 1 層目を除いた残りで 2 層目を作る。
        /// 戻り値は点ごとの層 (1 / 2 / 0 = 内側)。</summary>
        public static int[] OuterRings(FreeMapLayout L)
        {
            var ring = new int[L.Count];
            var rest = Enumerable.Range(0, L.Count).ToList();
            for (int k = 1; k <= 2 && rest.Count > 0; k++)
            {
                var hull = Hull(L, rest);
                var layer = new HashSet<int>(hull);
                if (hull.Count >= 2)
                    foreach (int i in rest)
                    {
                        if (layer.Contains(i)) continue;
                        for (int e = 0; e < hull.Count; e++)
                            if (SegDist(L.pos[i], L.pos[hull[e]], L.pos[hull[(e + 1) % hull.Count]]) <= FreeMapParams.StormOuterEdgeSlack) { layer.Add(i); break; }
                    }
                foreach (int i in layer) ring[i] = k;
                rest = rest.Where(i => !layer.Contains(i)).ToList();
            }
            return ring;
        }

        /// <summary>点 p から線分 ab までの直線距離 (平方根だけ)。</summary>
        private static double SegDist(Vec2 p, Vec2 a, Vec2 b)
        {
            double dx = b.x - a.x, dy = b.y - a.y, l2 = dx * dx + dy * dy;
            double t = l2 <= 0 ? 0 : Math.Max(0, Math.Min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / l2));
            return Vec2.Dist(p, new Vec2(a.x + dx * t, a.y + dy * t));
        }

        /// <summary>始点の候補 (添字の昇順): 凸包の頂点 (外周の 1 層目) のうち、 生成の終点でない・道が 2 本以上・
        /// 追跡者の居る深さでは縄張りの点から直線で <see cref="FreeMapParams.TerritoryStartClearance"/> 以上。 終点からの距離は、 ボスを選ぶ側で確かめる。</summary>
        public static List<int> StartCandidates(FreeMapLayout L)
        {
            var hull = Hull(L);
            bool foe = FloorPlan.HasWanderer(L.floor);
            var res = new List<int>();
            foreach (int i in hull.OrderBy(i => i))
            {
                if (i == L.goal || L.adj[i].Count < 2) continue;
                if (foe && L.territory.Any(t => L.Dist(t, i) < FreeMapParams.TerritoryStartClearance)) continue;
                res.Add(i);
            }
            return res;
        }

        /// <summary>ボスの点の候補 (添字の昇順・始点からの距離の下限は呼ぶ側): 外周の 1 層目・2 層目に入らない点のうち、 始点でない・縄張りの中でない・
        /// 道が 2 本以上・深さ 6 の旧い作りの固定の点でない。</summary>
        public static List<int> BossCandidates(FreeMapLayout L, int[] rings)
        {
            var res = new List<int>();
            for (int i = 0; i < L.Count; i++)
            {
                if (rings[i] != 0 || i == L.start || L.adj[i].Count < 2 || L.territory.Contains(i) || i == L.fixedEvent) continue;
                res.Add(i);
            }
            return res;
        }

        /// <summary>点の凸包の頂点 (全部の点・地図の最外周)。</summary>
        public static List<int> Hull(FreeMapLayout L) => Hull(L, Enumerable.Range(0, L.Count).ToList());

        /// <summary>点 <paramref name="pts"/> の凸包の頂点。 アンドリューの単調鎖 (座標の大小と外積だけ・三角関数なし)。 一直線上の点は頂点に入れない。</summary>
        public static List<int> Hull(FreeMapLayout L, List<int> pts)
        {
            var idx = pts.OrderBy(i => L.pos[i].x).ThenBy(i => L.pos[i].y).ThenBy(i => i).ToList();
            if (idx.Count < 3) return idx;
            double Cross(int o, int a, int b)
                => (L.pos[a].x - L.pos[o].x) * (L.pos[b].y - L.pos[o].y) - (L.pos[a].y - L.pos[o].y) * (L.pos[b].x - L.pos[o].x);
            var h = new List<int>();
            foreach (int p in idx)
            {
                while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(p);
            }
            int lower = h.Count + 1;
            for (int k = idx.Count - 2; k >= 0; k--)
            {
                int p = idx[k];
                while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(p);
            }
            h.RemoveAt(h.Count - 1);
            return h;
        }
    }
}

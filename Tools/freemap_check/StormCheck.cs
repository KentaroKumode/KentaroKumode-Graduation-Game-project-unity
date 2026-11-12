using System;
using System.Collections.Generic;
using System.Linq;
using MapSystem;
using MapSystem.FreeMove;
using M = MainRef.FreeMove;

/// <summary>main の FreeMove (名前空間 MainRef.FreeMove・run.py が git から取って一緒にコンパイルする) 用の財布。</summary>
class MainWallet : M.IFreeMapWallet
{
    public int Provision { get; set; } = 100000;
    public int Hp = 1000;
    public void SpendProvision(int a) { Provision -= a; }
    public bool PayHp(int a) { Hp -= a; return Hp <= 0; }
}

/// <summary>嵐 (2026-11-13・GAME.md §5-12) の検算。 口を閉じた時 (storm = false) に main と 1 点も変わらないこと、 口を開けた時の不変条件と分布。</summary>
static partial class T
{
    static readonly int[] StormDepths = { 1, 3, 5, 6 };

    static Dictionary<string, int> SnapDraws() => new Dictionary<string, int>(GameLoop.GameRng.Draws);

    static bool SameDraws(Dictionary<string, int> a, Dictionary<string, int> b)
        => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out int v) && v == kv.Value);

    /// <summary>今の生成器の形と main の生成器の形が 1 点も違わないか (違えば最初の違いの名前)。</summary>
    static string LayoutDiff(FreeMapLayout a, M.FreeMapLayout b)
    {
        if (a.floor != b.floor) return "floor";
        if (a.start != b.start || a.goal != b.goal) return "start/goal";
        if (a.fixedEvent != b.fixedEvent || a.gate != b.gate || a.home != b.home) return "fixed/gate/home";
        if (a.Count != b.Count) return "count";
        for (int i = 0; i < a.Count; i++)
        {
            if (a.pos[i].x != b.pos[i].x || a.pos[i].y != b.pos[i].y) return "pos";
            if (a.types[i] != b.types[i]) return "types";
            if (!a.adj[i].SequenceEqual(b.adj[i])) return "adj";
        }
        if (!a.roads.SequenceEqual(b.roads)) return "roads";
        if (a.mountains.Count != b.mountains.Count) return "mountains";
        for (int k = 0; k < a.mountains.Count; k++)
        {
            var p = a.mountains[k]; var q = b.mountains[k];
            if (p.x != q.x || p.y != q.y || p.a != q.a || p.b != q.b || p.cos != q.cos || p.sin != q.sin) return "mountain";
        }
        if (!a.territory.SetEquals(b.territory) || !a.waypoints.SequenceEqual(b.waypoints)) return "territory";
        if (!a.riftGate.SequenceEqual(b.riftGate)) return "riftGate";
        if (a.storm) return "storm";
        return null;
    }

    /// <summary>中身の写し (SavedState) をフィールドの名前で比べる。 main の写しに無い欄 (stormShift) は今の側を 0 として比べる。</summary>
    static string StateDiff(FreeMapSim.SavedState a, M.FreeMapSim.SavedState b)
    {
        if (a.stormShift != 0) return "stormShift";
        foreach (var fb in b.GetType().GetFields())
        {
            var fa = a.GetType().GetField(fb.Name);
            if (fa == null) return "missing " + fb.Name;
            object va = fa.GetValue(a), vb = fb.GetValue(b);
            if (va is Array xa)
            {
                var xb = (Array)vb;
                if (xa.Length != xb.Length) return fb.Name;
                for (int i = 0; i < xa.Length; i++) if (!Equals(xa.GetValue(i), xb.GetValue(i))) return fb.Name;
            }
            else if (!Equals(va, vb)) return fb.Name;
        }
        return null;
    }

    static int H(int a, int b, int c) { unchecked { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (int)(h & 0x7fffffff); } }

    /// <summary>同じ操作列を今の中身 (口を閉じた形) と main の中身に流し、 1 手ごとに起きたこと・写し・財布を比べる。 違った手の数を返す。</summary>
    static int ReplayDiff(int salt, int floor, int steps, out string first)
    {
        first = null;
        GameLoop.GameRng.Salt = (ulong)salt * 7919;
        var La = FreeMapGenerator.Generate(floor, true, false);
        var Lb = M.FreeMapGenerator.Generate(floor, true);
        bool foe = FloorPlan.HasWanderer(floor);
        var wa = new Wallet { Provision = 8, Hp = 60 }; var wb = new MainWallet { Provision = 8, Hp = 60 };
        var a = new FreeMapSim(La, wa, foe, null, null, null);
        var b = new M.FreeMapSim(Lb, wb, foe, null, null, null);
        a.ConfigureTerritory(true, true, true); b.ConfigureTerritory(true, true, true);
        int bad = 0;
        for (int k = 0; k < steps; k++)
        {
            if (a.Dead || b.Dead) { if (a.Dead != b.Dead) { bad++; first ??= $"dead@{k}"; } break; }
            int h = H(salt, floor, k), op = h % 100;
            int ea, eb;
            if (a.InFight || b.InFight)
            {
                bool body = a.Contact == FreeMapSim.ContactKind.Body;
                if (body) { if ((h & 1) == 0) { a.ResolveBodyWon(); b.ResolveBodyWon(); } else { a.ResolveBodyFled(); b.ResolveBodyFled(); } }
                else { bool fled = (h & 2) != 0; a.ResolveFamiliarFight(fled); b.ResolveFamiliarFight(fled); }
                ea = a.TakePendingArrival(); eb = b.TakePendingArrival();
            }
            else if (op < 55)
            {
                var na = a.Neighbors(a.Me); if (na.Count == 0) { ea = eb = 0; }
                else { int j = na[(h / 100) % na.Count]; ea = (int)a.MoveTo(j); eb = (int)b.MoveTo(j); a.MarkDone(j); b.MarkDone(j); }
            }
            else if (op < 65) { ea = (int)a.Wait(); eb = (int)b.Wait(); }
            else if (op < 70) { ea = (int)a.Recon(); eb = (int)b.Recon(); }
            else if (op < 73) { ea = (int)a.UseFlare(); eb = (int)b.UseFlare(); }
            else if (op < 76) { ea = (int)a.PlaceLure(); eb = (int)b.PlaceLure(); }
            else if (op < 80) { int n = ((h / 100) % 5) - 2; ea = (int)a.ShiftFront(n); eb = (int)b.ShiftFront(n); }
            else if (op < 83) { a.GrantFreeMoves(1); b.GrantFreeMoves(1); ea = eb = 0; }
            else if (op < 86) { ea = (int)a.PushFoe(2); eb = (int)b.PushFoe(2); }
            else if (op < 89) { ea = (int)a.PullFoe(2); eb = (int)b.PullFoe(2); }
            else if (op < 91) { ea = (int)a.ShakeOff(); eb = (int)b.ShakeOff(); }
            else if (op < 93) { ea = (int)a.PassTurnsExternal(2); eb = (int)b.PassTurnsExternal(2); }
            else if (op < 95) { ea = a.TakeTreasure() ? 1 : 0; eb = b.TakeTreasure() ? 1 : 0; }
            else if (op < 97) { ea = a.SupplyFor(a.Neighbors(a.Me)[0]) * 10 + a.TurnsFor(a.Neighbors(a.Me)[0]) + 100 * a.HpLossFor(a.Neighbors(a.Me)[0]);
                                eb = b.SupplyFor(b.Neighbors(b.Me)[0]) * 10 + b.TurnsFor(b.Neighbors(b.Me)[0]) + 100 * b.HpLossFor(b.Neighbors(b.Me)[0]); }
            else { ea = a.RevealAround(2, true); eb = b.RevealAround(2, true); }
            string d = ea != eb ? "event" : wa.Provision != wb.Provision || wa.Hp != wb.Hp ? "wallet"
                     : a.FogHidden(a.Me == 0 ? 1 : 0) != b.FogHidden(b.Me == 0 ? 1 : 0) ? "fog" : StateDiff(a.CaptureState(), b.CaptureState());
            if (d != null) { bad++; first ??= $"{d}@{k} op={op}"; break; }
        }
        return bad;
    }

    static FreeMapSim MakeStorm(int salt, int floor, Wallet w = null, WeatherKind? wx = null)
    {
        GameLoop.GameRng.Salt = (ulong)salt * 7919;
        var L = FreeMapGenerator.Generate(floor, true, true);
        var s = new FreeMapSim(L, w ?? new Wallet(), FloorPlan.HasWanderer(floor), null, wx, null);
        s.ConfigureTerritory(true, true, true);
        return s;
    }

    /// <summary>適当な方策で層を歩く: 静かな時間は近い未踏の点を踏み、 嵐が来たら終点へ向かう。 捕まったら勝ったことにする。</summary>
    static (bool reached, int turns, int visited, int stormSteps, int stormHp) StormWalk(FreeMapSim s, Wallet w)
    {
        var L = s.Layout; var seen = new HashSet<int> { s.Me };
        int stormSteps = 0, stormHp = 0;
        for (int guard = 0; guard < 600 && !s.Dead; guard++)
        {
            if (s.InFight)
            {
                if (s.Contact == FreeMapSim.ContactKind.Body) s.ResolveBodyWon(); else s.ResolveFamiliarFight(false);
                s.TakePendingArrival();
                continue;
            }
            if (s.Me == L.goal) break;
            int target = L.goal;
            if (s.StormPhase == 0)
            {
                var d = s.HopsFrom(s.Me); int bd = int.MaxValue;
                for (int i = 0; i < L.Count; i++)
                    if (i != L.goal && !seen.Contains(i) && d[i] < bd) { bd = d[i]; target = i; }
            }
            int nx = s.StepToward(s.Me, target);
            if (nx == s.Me) break;
            bool inStorm = s.InStorm(nx); int hp0 = w.Hp;
            s.MoveTo(nx); s.MarkDone(nx); seen.Add(nx);
            if (inStorm) { stormSteps++; stormHp += hp0 - w.Hp; }
        }
        return (s.Me == L.goal, s.Turn, seen.Count, stormSteps, stormHp);
    }

    static void StormReport()
    {
        // ── 1. 口を閉じた形 (storm = false) は main の生成器と 1 点も変わらない・乱数の引く回数も同じ ──
        {
            int layers = 0, diff = 0, drawDiff = 0; string firstDiff = null;
            foreach (int floor in new[] { 0, 1, 3, 5, 6, 7 })
                for (int salt = 1; salt <= 5000; salt++)
                {
                    foreach (bool rift in floor == 6 && salt <= 500 ? new[] { true, false } : new[] { true })
                    {
                        GameLoop.GameRng.Salt = (ulong)salt * 7919; GameLoop.GameRng.Draws.Clear();
                        var La = FreeMapGenerator.Generate(floor, rift, false); var da = SnapDraws();
                        GameLoop.GameRng.Draws.Clear();
                        var Lb = M.FreeMapGenerator.Generate(floor, rift); var db = SnapDraws();
                        layers++;
                        string d = LayoutDiff(La, Lb);
                        if (d != null) { diff++; firstDiff ??= $"深さ{floor} 種{salt} {d}"; }
                        if (!SameDraws(da, db)) drawDiff++;
                    }
                }
            Check(diff == 0 && drawDiff == 0, $"嵐の口 False: 生成は main と 1 点も変わらない ({layers} 層・形の違い {diff}{(firstDiff != null ? " 最初 " + firstDiff : "")}・乱数の引く回数の違い {drawDiff})");
        }
        // ── 2. 口を閉じた中身は main と同じ動き (同じ操作列・1 手ごとに起きたこと・写し・財布・引く回数) ──
        {
            int runs = 0, bad = 0, drawBad = 0; string first = null;
            foreach (int floor in StormDepths)
                for (int salt = 1; salt <= 1000; salt++)
                {
                    GameLoop.GameRng.Draws.Clear();
                    bad += ReplayDiff(salt, floor, 160, out string f);
                    if (f != null) first ??= $"深さ{floor} 種{salt} {f}";
                    runs++;
                }
            // 引く回数: 同じ操作を片側ずつ流して数える
            for (int salt = 1; salt <= 200; salt++)
                foreach (int floor in StormDepths)
                {
                    GameLoop.GameRng.Salt = (ulong)salt * 7919; GameLoop.GameRng.Draws.Clear();
                    var a = new FreeMapSim(FreeMapGenerator.Generate(floor, true, false), new Wallet(), FloorPlan.HasWanderer(floor), null, null, null);
                    for (int k = 0; k < 60 && !a.InFight; k++) { var nb = a.Neighbors(a.Me); a.MoveTo(nb[H(salt, floor, k) % nb.Count]); }
                    var da = SnapDraws();
                    GameLoop.GameRng.Draws.Clear();
                    var b = new M.FreeMapSim(M.FreeMapGenerator.Generate(floor, true), new MainWallet(), FloorPlan.HasWanderer(floor), null, null, null);
                    for (int k = 0; k < 60 && !b.InFight; k++) { var nb = b.Neighbors(b.Me); b.MoveTo(nb[H(salt, floor, k) % nb.Count]); }
                    if (!SameDraws(da, SnapDraws())) drawBad++;
                }
            Check(bad == 0 && drawBad == 0, $"嵐の口 False: 航行の中身は main と同じ動き ({runs} 層 × 160 手・違い {bad}{(first != null ? " 最初 " + first : "")}・引く回数の違い {drawBad}/800)");
        }

        // ── 3. 口を開けた形: 始点・距離・方角・保証 ──
        {
            int layers = 0, startBad = 0, shapeBad = 0, fallback = 0, shopRestBad = 0, terrBad = 0, bypassBad = 0, foeRemoved = 0, riftBad = 0, nearSupply = 0, nearSupplyOld = 0;
            var hopsStorm = new Dictionary<int, List<int>>(); var hopsOld = new Dictionary<int, List<int>>();
            var bearStorm = new int[8]; var bearOld = new int[8]; int bypassBefore = 0, wandererLayers = 0, trimmed = 0;
            var startsDistinct = new HashSet<int>();
            foreach (int floor in StormDepths) { hopsStorm[floor] = new List<int>(); hopsOld[floor] = new List<int>(); }
            foreach (int floor in StormDepths)
                for (int salt = 1; salt <= 3000; salt++)
                {
                    GameLoop.GameRng.Salt = (ulong)salt * 7919;
                    var Lo = FreeMapGenerator.Generate(floor, true, false);
                    var s = MakeStorm(salt, floor); var L = s.Layout;
                    layers++;
                    if (L.stormStartFallback) fallback++;
                    var hull = Storm.Hull(L);
                    int hg = L.Hops(L.goal)[L.start];
                    if (!L.storm || (!L.stormStartFallback && (!hull.Contains(L.start) || L.adj[L.start].Count < 2 || hg < FreeMapParams.StormStartMinHops))) startBad++;
                    // 点・道は 1 つも変わらない。 種別は始点と添字 0 の入れ替えと、 新しい安全な帯のエリート → 戦闘だけ
                    bool same = Lo.Count == L.Count && Lo.roads.SequenceEqual(L.roads) && Enumerable.Range(0, L.Count).All(i => Lo.pos[i].x == L.pos[i].x && Lo.pos[i].y == L.pos[i].y && Lo.adj[i].SequenceEqual(L.adj[i]))
                                && Lo.territory.SetEquals(L.territory) && Lo.riftGate.SequenceEqual(L.riftGate) && Lo.goal == L.goal;
                    for (int i = 0; i < L.Count && same; i++)
                    {
                        var to = i == L.start ? Lo.types[Lo.start] : i == Lo.start ? Lo.types[L.start] : Lo.types[i];
                        if (L.types[i] != to && !(to == TileType.EliteBattle && L.types[i] == TileType.Battle)) same = false;
                    }
                    if (!same) shapeBad++;
                    if (L.types[L.start] != TileType.Outpost || L.types[L.goal] != TileType.Boss) shapeBad++;
                    int shops = 0, rests = 0; var ds = L.Hops(L.start); bool near = false;
                    for (int i = 0; i < L.Count; i++)
                    {
                        if (L.types[i] == TileType.Shop) { shops++; if (ds[i] < 2) near = true; }
                        if (L.types[i] == TileType.Rest) { rests++; if (ds[i] < 2) near = true; }
                    }
                    if (shops < 1 || rests < 1) shopRestBad++;
                    if (near) nearSupply++;
                    { var dso = Lo.Hops(Lo.start); if (Enumerable.Range(0, Lo.Count).Any(i => (Lo.types[i] == TileType.Shop || Lo.types[i] == TileType.Rest) && dso[i] < 2)) nearSupplyOld++; }
                    if (floor == 6 && (L.riftGate.Contains(L.start) || L.riftGate.Any(r => L.types[r] != TileType.Event))) riftBad++;
                    if (FloorPlan.HasWanderer(floor))
                    {
                        wandererLayers++;
                        if (L.territory.Contains(L.start) || (!L.stormStartFallback && L.territory.Any(t => L.Dist(t, L.start) < FreeMapParams.TerritoryStartClearance))) terrBad++;
                        if (TerritoryBypass.HasBypass(L, L.territory)) bypassBefore++;
                        if (s.TerritoryTrimmed > 0) trimmed++;
                        if (s.TerritoryFoeRemoved) foeRemoved++;
                        else if (!TerritoryBypass.HasBypass(L, s.Territory.ToList()) || s.InTerritory(L.start)) bypassBad++;
                    }
                    hopsStorm[floor].Add(hg); hopsOld[floor].Add(Lo.Hops(Lo.goal)[Lo.start]);
                    bearStorm[s.Bearing(L.start, L.goal)]++;
                    var so = new FreeMapSim(Lo, new Wallet(), false, null, WeatherKind.None, null); bearOld[so.Bearing(Lo.start, Lo.goal)]++;
                    if (salt <= 50) startsDistinct.Add(floor * 1000 + L.start);
                }
            Check(startBad == 0 && shapeBad == 0, $"嵐: 始点は最外周 (凸包の頂点)・道 2 本以上・終点から {FreeMapParams.StormStartMinHops} 歩以上 / 点・道・縄張りは変わらず種別は入れ替えと安全な帯のエリートだけ ({layers} 層・始点の違反 {startBad}・形の違反 {shapeBad})");
            Check(fallback * 100 <= layers, $"嵐: 始点の候補が無く生成の始点のままの層 {fallback}/{layers} ({100.0 * fallback / layers:F2}%)");
            Check(shopRestBad == 0, $"嵐: 店と休憩は層に 1 つずつ以上 (違反 {shopRestBad}/{layers})・店か休憩が始点の隣にある層 嵐 {100.0 * nearSupply / layers:F1}% / 旧い始点 {100.0 * nearSupplyOld / layers:F1}%");
            Check(riftBad == 0, $"嵐: 深さ 6 のボスに隣り合う点は全部イベントのまま・始点にならない (違反 {riftBad})");
            Check(terrBad == 0 && bypassBad == 0, $"嵐: 始点は縄張りの外で縄張りの点から {FreeMapParams.TerritoryStartClearance} 以上・縄張りを避ける道が全層にある (追跡者の居る層 {wandererLayers}・違反 {terrBad}/{bypassBad}・削る前にある {100.0 * bypassBefore / Math.Max(1, wandererLayers):F1}%・削った {100.0 * trimmed / Math.Max(1, wandererLayers):F1}%・追跡者を置かない {foeRemoved})");
            foreach (int floor in StormDepths)
                Console.WriteLine($"[嵐] 深さ{floor} 始点→終点の歩数: 嵐 {Dist(hopsStorm[floor])} / 旧い始点 {Dist(hopsOld[floor])}");
            var all = hopsStorm.Values.SelectMany(x => x).ToList();
            var hist = all.GroupBy(x => x).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
            Console.WriteLine($"[嵐] 始点→終点の歩数の分布 (全深さ {all.Count} 層): {string.Join(" ", hist)}");
            Console.WriteLine($"[嵐] 始点から見た終点の方角 (東 北東 北 北西 西 南西 南 南東): 嵐 {string.Join(" ", bearStorm)} / 旧い始点 {string.Join(" ", bearOld)}");
            Console.WriteLine($"[嵐] 50 種 × 4 深さで選ばれた始点の添字の種類 {startsDistinct.Count}");
        }

        // ── 4. 輪の段の境目の半径・フェーズ 4・ボスの点 ──
        {
            int layers = 0, bad = 0; string first = null; int phase3Extra = 0, grew = 0;
            foreach (int floor in StormDepths)
                for (int salt = 1; salt <= 1000; salt++)
                {
                    GameLoop.GameRng.Salt = (ulong)salt * 7919;
                    var L = FreeMapGenerator.Generate(floor, true, true); layers++;
                    double r0 = Storm.FullRadius(L);
                    Storm.RingAt(L, 0, out var c0, out double ra);
                    Storm.RingAt(L, Storm.Phase2Start, out var c1, out double rb);
                    Storm.RingAt(L, Storm.Phase3Start, out var c2, out double rc);
                    var B = L.pos[L.goal]; var want2 = Storm.Phase2Center(L);
                    string e = null;
                    if (Math.Abs(ra - r0) > 1e-9 || Vec2.Dist(c0, Storm.MapCenter) > 1e-9) e = "s=0";
                    else if (Math.Abs(rb - r0 * FreeMapParams.StormPhase1EndRadius) > 1e-9 || Vec2.Dist(c1, Storm.MapCenter) > 1e-9) e = "s=10";
                    else if (Math.Abs(rc - r0 * FreeMapParams.StormPhase2EndRadius) > 1e-9 || Vec2.Dist(c2, want2) > 1e-9) e = "s=20";
                    else if (Vec2.Dist(B, c2) > rc) e = "boss outside at s=20";
                    else if (Vec2.Dist(B, c2) < 1e-9) e = "center == boss at s=20";
                    else if (Enumerable.Range(0, L.Count).Any(i => Storm.InStorm(L, i, 0) || Storm.InStorm(L, i, -5))) e = "storm at s<=0";
                    else if (Enumerable.Range(0, L.Count).Any(i => Storm.InStorm(L, i, Storm.Phase4Start) != (i != L.goal))) e = "phase4";
                    else if (Enumerable.Range(0, 60).Any(t => Storm.InStorm(L, L.goal, t - 10))) e = "goal in storm";
                    if (e != null) { bad++; first ??= $"深さ{floor} 種{salt} {e}"; }
                    // フェーズ 3 の終わり (u→1) に輪に入る点: ボスとその隣のほかに入る点の数
                    double r3 = Storm.FinalRadius(L);
                    if (r3 > r0 * FreeMapParams.StormPhase2EndRadius) grew++;
                    var nb = new HashSet<int>(L.adj[L.goal]) { L.goal };
                    phase3Extra += Enumerable.Range(0, L.Count).Count(i => !nb.Contains(i) && L.Dist(i, L.goal) <= r3);
                }
            Check(bad == 0, $"嵐: 段の境目の輪 (s=0 全点を含む・s=10 60%・s=20 30% で中心はボスをずらした点・ボスは輪の中)・静かな時間は嵐なし・フェーズ 4 はボスの点だけが外・ボスの点はいつも外 ({layers} 層・違反 {bad}{(first != null ? " 最初 " + first : "")})");
            Console.WriteLine($"[嵐] フェーズ 3 の終わりの輪に、 ボスの隣でない点が入る数の合計 {phase3Extra} ({layers} 層)・終わりの半径がフェーズ 2 の終わりより大きい層 {grew}");
        }

        // ── 5. 層の出方: 深さ 0/7 に嵐は無い・深さ 6 は手番 0 からフェーズ 1・他は 20 手番の静かな時間 ──
        {
            bool ok = true;
            foreach (int floor in new[] { 0, 7 })
            {
                GameLoop.GameRng.Salt = 77;
                var L = FreeMapGenerator.Generate(floor, true, true);
                var s = new FreeMapSim(L, new Wallet(), false, null, null, null);
                for (int k = 0; k < 40; k++) s.Wait();
                ok &= !L.storm && !s.StormMode && s.StormPhase == 0 && Enumerable.Range(0, L.Count).All(i => !s.InStorm(i)) && s.Weather == WeatherKind.None && L.start == 0 && s.Known[L.goal];
            }
            var s6 = MakeStorm(3, 6);
            ok &= s6.StormPhase == 1 && s6.StormTurnsToNext == FreeMapParams.StormPhase1Turns;
            foreach (int floor in new[] { 1, 3, 5 })
            {
                var s = MakeStorm(3, floor);
                bool q = s.StormPhase == 0 && s.StormTurnsToNext == FreeMapParams.StormQuietTurns;
                for (int k = 0; k < FreeMapParams.StormQuietTurns - 1; k++) s.PassTurnsExternal(1);
                q &= s.StormPhase == 0 && s.StormTurnsToNext == 1;
                var ev = s.PassTurnsExternal(1);
                q &= s.StormPhase == 1 && (ev & FreeMapEvent.StormAdvanced) != 0;
                ok &= q;
            }
            Check(ok, "嵐: 深さ 0/7 に嵐も天候も無い (始点は左端・ボスは見える)・深さ 6 は手番 0 からフェーズ 1・深さ 1/3/5 は 20 手番の静かな時間のあとフェーズ 1 (段の変わり目を知らせる)");
        }

        // ── 6. 嵐の HP・種類 (雹・砂・雪)・近道・待つ ──
        {
            bool ok = true; var notes = new List<string>();
            foreach (var wx in new[] { WeatherKind.None, WeatherKind.Hail, WeatherKind.Sand, WeatherKind.Snow, WeatherKind.Fog })
            {
                var w = new Wallet { Provision = 100, Hp = 1000 };
                var s = MakeStorm(11, 5, w, wx);
                s.ShiftFront(FreeMapParams.StormQuietTurns + Storm.Phase4Start);   // フェーズ 4 (ボスの点のほかは全部が嵐の中)
                ok &= s.StormPhase == 4 && s.Turn == 0;
                int j = s.Neighbors(s.Me).First(v => v != s.Layout.goal);
                int hp0 = w.Hp, p0 = w.Provision, t0 = s.Turn, st0 = s.StormTime;
                bool storm = s.InStorm(j);
                s.MoveTo(j);
                int wantHp = FreeMapParams.StormHp + (wx == WeatherKind.Hail ? FreeMapParams.StormHailHp : 0);
                int wantSup = FreeMapParams.MoveSupply + (wx == WeatherKind.Sand ? FreeMapParams.StormSandSupply : 0);
                int wantT = wx == WeatherKind.Snow ? FreeMapParams.StormSnowTurns : 1;
                bool one = storm && (s.InFight || (hp0 - w.Hp == wantHp && p0 - w.Provision == wantSup && s.Turn - t0 == wantT && s.StormTime - st0 == wantT));
                notes.Add($"{wx}: HP-{hp0 - w.Hp} 物資-{p0 - w.Provision} 手番+{s.Turn - t0}");
                ok &= one;
                if (s.InFight) continue;
                // 待つ: 嵐の中で −3 (種類によらない)
                int hp1 = w.Hp; s.Wait();
                ok &= s.InFight || hp1 - w.Hp == FreeMapParams.StormHp;
                if (s.InFight) continue;
                // 近道: 手番と物資は使わず、 嵐の HP は払う
                s.GrantFreeMoves(1);
                int k = s.Neighbors(s.Me).First(v => v != s.Layout.goal);
                int hp2 = w.Hp, p2 = w.Provision, t2 = s.Turn;
                s.MoveTo(k);
                ok &= s.InFight || (hp2 - w.Hp == wantHp && p2 == w.Provision && s.Turn == t2);
            }
            Check(ok, $"嵐: 嵐の中の点への 1 歩は HP −{FreeMapParams.StormHp} (雹 −{FreeMapParams.StormHp + FreeMapParams.StormHailHp}・砂は物資 +{FreeMapParams.StormSandSupply}・雪は 2 手番で時計も 2)・待つも −{FreeMapParams.StormHp}・近道でも HP は払う ({string.Join(" / ", notes)})");
            // 嵐の外の点では払わない・終点 (ボス) へは払わない
            var w2 = new Wallet { Provision = 100, Hp = 1000 };
            var s2 = MakeStorm(12, 3, w2);
            int hpq = w2.Hp; for (int k = 0; k < 5; k++) { if (s2.InFight) break; s2.MoveTo(s2.Neighbors(s2.Me)[0]); }
            Check(s2.InFight || w2.Hp == hpq, $"嵐: 静かな時間は嵐の HP を払わない (HP {hpq}->{w2.Hp})");
            // 死ぬ: HP が尽きたら Died
            var w3 = new Wallet { Provision = 100, Hp = 5 };
            var s3 = MakeStorm(13, 3, w3);
            s3.ShiftFront(FreeMapParams.StormQuietTurns + Storm.Phase4Start);
            FreeMapEvent acc = 0; for (int k = 0; k < 4 && !s3.Dead && !s3.InFight; k++) acc |= s3.Wait();
            Check(s3.InFight || (s3.Dead && (acc & FreeMapEvent.Died) != 0 && s3.HpPayReason == FreeMapSim.HpReasonStorm), $"嵐: 嵐の中で HP が尽きたら倒れる (Dead={s3.Dead} 理由={s3.HpPayReason})");
        }

        // ── 7. 「遠ざける」「近づける」で時計が動く ──
        {
            var s = MakeStorm(21, 3);
            int t0 = s.StormTurnsToNext;
            s.ShiftFront(-3); bool a = s.StormTurnsToNext == t0 + 3 && s.StormPhase == 0 && s.Turn == 0;
            s.ShiftFront(+3 + FreeMapParams.StormQuietTurns + 12); bool b = s.StormPhase == 2 && s.StormTime == 12;
            s.StormRing(out _, out double r12);
            s.ShiftFront(-5); s.StormRing(out _, out double r7);
            bool c = s.StormPhase == 1 && s.StormTime == 7 && r7 > r12;
            var ev = s.ShiftFront(+20); bool d = s.StormPhase == 4 && (ev & FreeMapEvent.StormAdvanced) != 0 && s.Known[s.Layout.goal];
            Check(a && b && c && d, $"嵐: 「前線を遠ざける」は静かな時間を延ばし・嵐の輪を戻す / 「近づける」は進める (手番は進まない・段が変わると知らせる) ({a}/{b}/{c}/{d})");
        }

        // ── 8. 霧・ボスの点の見え方 ──
        {
            bool ok = true; int hidden = 0;
            for (int salt = 1; salt <= 40; salt++)
            {
                var s = MakeStorm(salt, 5, null, WeatherKind.Fog);
                ok &= !s.Known[s.Layout.goal];
                for (int i = 0; i < s.Layout.Count; i++) ok &= !s.FogHidden(i);   // 静かな時間は霧も無い
                s.ShiftFront(FreeMapParams.StormQuietTurns + Storm.Phase4Start);
                var d = s.HopsFrom(s.Me);
                for (int i = 0; i < s.Layout.Count; i++)
                {
                    bool want = i != s.Layout.goal && i != s.Layout.start && d[i] >= 2 && s.InStorm(i);
                    if (s.FogHidden(i) != want) ok = false;
                    if (want) hidden++;
                }
                ok &= s.Known[s.Layout.goal] && !s.FogHidden(s.Layout.goal);
            }
            // 深さ 6: ボスに隣り合うイベントマスは霧でも隠れない
            for (int salt = 1; salt <= 40; salt++)
            {
                var s = MakeStorm(salt, 6, null, WeatherKind.Fog);
                s.ShiftFront(Storm.Phase4Start);
                foreach (int r in s.Layout.riftGate) ok &= !s.FogHidden(r);
            }
            // 照明・隣で見える
            int lit = 0, adj = 0;
            for (int salt = 1; salt <= 200; salt++)
            {
                var s = MakeStorm(salt, 3);
                for (int k = 0; k < 80 && !s.Dead; k++)
                {
                    if (s.InFight) { if (s.Contact == FreeMapSim.ContactKind.Body) s.ResolveBodyWon(); else s.ResolveFamiliarFight(false); continue; }
                    if (s.Known[s.Layout.goal]) break;
                    int h = s.HopsFrom(s.Me)[s.Layout.goal];
                    if (h <= FreeMapParams.FlareRange && (salt & 1) == 0) { s.UseFlare(); if (s.Known[s.Layout.goal]) lit++; break; }
                    s.MoveTo(s.StepToward(s.Me, s.Layout.goal));
                    if (s.Known[s.Layout.goal]) { if (s.HopsFrom(s.Me)[s.Layout.goal] == 1) adj++; break; }
                }
            }
            Check(ok && lit > 0 && adj > 0, $"嵐: ボスの点は最初は伏せられ (照明で {lit}・隣で {adj} 層が見えた・フェーズ 3 で必ず見える)・霧は嵐の中の 2 歩以上離れた点だけを「?」に (隠れた点 {hidden})・ボスの点と深さ 6 の隣り合う点は隠れない");
        }

        // ── 9. 点も道も 1 つも消えない・終点へ必ず着ける・中断セーブの往復 ──
        {
            int layers = 0, lost = 0, notReached = 0, saveBad = 0;
            foreach (int floor in StormDepths)
                for (int salt = 1; salt <= 300; salt++)
                {
                    var w = new Wallet { Provision = 3, Hp = 100000 };
                    var s = MakeStorm(salt, floor, w); layers++;
                    var roads0 = s.Roads().ToList();
                    for (int k = 0; k < 60; k++) { if (s.InFight) { if (s.Contact == FreeMapSim.ContactKind.Body) s.ResolveBodyWon(); else s.ResolveFamiliarFight(false); } s.Wait(); }
                    if (!s.Roads().SequenceEqual(roads0) || Enumerable.Range(0, s.Layout.Count).Any(i => s.IsSunk(i))) lost++;
                    // 中断セーブの往復 (ずらしも写す)
                    s.ShiftFront(-4);
                    var saved = s.CaptureState();
                    var r = new FreeMapSim(s.Layout, new Wallet(), FloorPlan.HasWanderer(floor), null, null, null);
                    r.ConfigureTerritory(true, true, true); r.LoadState(saved, null);
                    if (r.StormTime != s.StormTime || r.StormShift != s.StormShift || r.StormPhase != s.StormPhase || r.Weather != s.Weather
                        || Enumerable.Range(0, s.Layout.Count).Any(i => r.InStorm(i) != s.InStorm(i) || r.Known[i] != s.Known[i])) saveBad++;
                    // 終点へ歩く (嵐の HP を払って)
                    for (int k = 0; k < 200 && !s.Dead && s.Me != s.Layout.goal; k++)
                    {
                        if (s.InFight) { if (s.Contact == FreeMapSim.ContactKind.Body) s.ResolveBodyWon(); else s.ResolveFamiliarFight(false); continue; }
                        s.MoveTo(s.StepToward(s.Me, s.Layout.goal));
                    }
                    if (s.Me != s.Layout.goal) notReached++;
                }
            Check(lost == 0 && notReached == 0 && saveBad == 0, $"嵐: 点も道も消えない・終点へ必ず着ける・中断セーブの往復で嵐の状態が同じ ({layers} 層・消えた {lost}・着けない {notReached}・往復の違い {saveBad})");
        }

        // ── 10. 適当な方策で歩いた分布 (最初の置き値が極端でないかを見る・数値は変えない) ──
        foreach (int floor in StormDepths)
        {
            var turns = new List<int>(); var visited = new List<int>(); var ss = new List<int>(); var hp = new List<int>(); int reached = 0;
            for (int salt = 1; salt <= 1000; salt++)
            {
                var w = new Wallet { Provision = 100000, Hp = 100000 };
                var s = MakeStorm(salt, floor, w);
                var r = StormWalk(s, w);
                if (r.reached) reached++;
                turns.Add(r.turns); visited.Add(r.visited); ss.Add(r.stormSteps); hp.Add(r.stormHp);
            }
            Console.WriteLine($"[嵐の歩き] 深さ{floor} (静かな時間は近い未踏の点・嵐が来たら終点へ・1000 層・着いた {reached}): 手番 {Dist(turns)}");
            Console.WriteLine($"[嵐の歩き]   踏んだ点 {Dist(visited)} / 嵐の中の歩数 {Dist(ss)} / 嵐で払った HP {Dist(hp)}");
            Check(reached == 1000, $"嵐の歩き: 深さ{floor} は全層で終点に着く ({reached}/1000)");
        }
    }
}

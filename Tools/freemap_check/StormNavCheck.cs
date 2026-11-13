using System;
using System.Collections.Generic;
using System.Linq;
using MapSystem;
using MapSystem.FreeMove;
using AutoTest;

/// <summary>BOT の嵐の航行の見方 (AutoTest.StormNavView・2026-11-14・口 navStormAware) の検算。 本物の StormNavView.cs を一緒にコンパイルする。
/// (1) 隠れたボスの点は、 見えるまでいつも候補に入る (位置を読まずに探して必ず見つかる)。
/// (2) 輪の見込みが本体の輪 (FreeMapSim.InStorm) と一致する (ボスの点のほか・ボスが見えている時と、 フェーズ 1・2 の輪の逆算)。
/// (3) 道の嵐の HP と歩いて溜まる分の見込みが、 本体を歩かせた値と一致する。
/// (4) 嵐を見込んだ 1 歩は見えているボスの点を通り抜けず、 行き先へ着く。 着いた点のポイントの見込みは本体の表から。
/// (5) 静かな間 → フェーズ 1 の輪は同心で縮むだけなので、 出口の嵐の HP の見込みはポイントが増えて減らない (取る代償は負にならない)。</summary>
static partial class T
{
    static FreeMapSim MakeNavStorm(int salt, int floor, Wallet w = null, WeatherKind? wx = null)
    {
        GameLoop.GameRng.Salt = (ulong)salt * 7919;
        var L = FreeMapGenerator.Generate(floor, false, true);
        var s = new FreeMapSim(L, w ?? new Wallet(), false, null, wx, null);   // 追跡者なし (歩かせる検算が戦闘で止まらないように)
        s.ConfigureTerritory(true, true, true);
        s.ConfigureStorm(StormClock.Instability);
        return s;
    }

    static int KnownBoss(FreeMapSim s) => s.Known[s.Layout.goal] ? s.Layout.goal : -1;   // 検算だけが終点を読む (BOT は種別が見えた点から知る)

    static StormNavView View(FreeMapSim s) => new StormNavView(s, (bool[])s.Known.Clone(), KnownBoss(s));

    static void StormNavReport()
    {
        int[] floors = { 1, 3, 5, 6 };
        int layers = N(1000, 200);

        // ── (1) 探す: 隠れたボスの点はいつも候補に入る・探す歩き方で必ず見つかる ──
        {
            int notIn = 0, notFound = 0, total = 0; var stepsToFind = new List<int>(); var candCount = new List<int>();
            foreach (int floor in floors)
                for (int salt = 1; salt <= layers; salt++)
                {
                    var s = MakeNavStorm(salt, floor);
                    total++;
                    int steps = 0;
                    for (int g = 0; g < 200 && !s.Known[s.Layout.goal]; g++)
                    {
                        var v = View(s);
                        if (g == 0) candCount.Add(v.Candidates.Count);
                        if (!v.Candidates.Contains(s.Layout.goal)) { notIn++; break; }
                        int t = v.SearchTarget(s.Me);
                        if (t < 0) break;
                        int nx = v.StormStep(s.Me, t);
                        if (nx == s.Me || !s.CanMoveTo(nx)) break;
                        s.MoveTo(nx); steps++;
                    }
                    if (s.Known[s.Layout.goal]) stepsToFind.Add(steps); else notFound++;
                }
            Check(notIn == 0 && notFound == 0,
                $"嵐の航行 (1): 隠れたボスの点はいつも候補に入る・探す歩き方で全層で見つかる (候補の外 {notIn} / 見つからない {notFound} / {total} 層・" +
                $"始めの候補 平均 {candCount.Average():F1} 点・見つけるまで 平均 {stepsToFind.Average():F1} 歩 (最大 {stepsToFind.Max()}))");
        }

        // ── (2) 輪の見込み = 本体の輪 (ボスの点のほか) ──
        {
            long cmp = 0, diff = 0; string first = null;
            foreach (int floor in floors)
                for (int salt = 1; salt <= layers / 4; salt++)
                {
                    var s = MakeNavStorm(salt, floor);
                    for (int pts = s.Instability; pts <= FreeMapParams.InstabilityPhase4At + 5; pts++)
                    {
                        if (s.Instability < pts) s.AddInstability(InstabilitySource.Shift, pts - s.Instability);
                        var v = View(s);
                        for (int i = 0; i < s.Layout.Count; i++)
                        {
                            if (i == s.Layout.goal) continue;
                            cmp++;
                            if (v.InStormAt(i, s.Instability) != s.InStorm(i)) { diff++; if (first == null) first = $"深さ{floor} 種{salt} 累計{s.Instability} 点{i} ボス{(KnownBoss(s) >= 0 ? "見え" : "隠れ")}"; }
                        }
                    }
                }
            Check(diff == 0, $"嵐の航行 (2): 輪の見込みが本体の輪と一致 (ボスの点のほか・静か〜フェーズ 4・ボスが隠れたフェーズ 2 は画面の輪から逆算) ({diff}/{cmp} {first})");
        }

        // ── (3) 道の嵐の HP と歩く分の見込み = 本体を歩かせた値 (ボスが見えている時) ──
        {
            int walks = 0, hpBad = 0, ptsBad = 0; string first = null; long hpSum = 0;
            foreach (int floor in floors)
                foreach (var wx in new WeatherKind?[] { WeatherKind.None, WeatherKind.Hail, WeatherKind.Snow })
                    for (int salt = 1; salt <= layers / 4; salt++)
                    {
                        var w = new Wallet();
                        var s = MakeNavStorm(salt, floor, w, wx);
                        int want = (salt * 7) % (FreeMapParams.InstabilityPhase4At + 3);   // 静か〜フェーズ 4 の色々な累計から
                        if (want > s.Instability) s.AddInstability(InstabilitySource.Shift, want - s.Instability);
                        var v = View(s);
                        // 始点から一番遠い点へ (ボスの点は通らない道があれば)
                        var h = s.HopsFrom(s.Me);
                        int to = Enumerable.Range(0, s.Layout.Count).Where(i => i != s.Layout.goal && h[i] != int.MaxValue).OrderByDescending(i => h[i]).ThenBy(i => i).First();
                        int predHp = v.WalkCost(s.Me, to, s.Instability, false, out int predPts, out int k);
                        // ボスが隠れている間は、 先の輪 (フェーズ 2 の中心の行き先・フェーズ 3) が見込みになる。 一致を見るのは、 見込みの要らない範囲
                        //   (ボスが見えている / フェーズ 1 の中で終わる / フェーズ 2 の途中から始まりフェーズ 2 の中で終わる = 画面の輪から逆算できる) だけ
                        bool exact = s.Known[s.Layout.goal] || predPts < FreeMapParams.InstabilityPhase2At
                                     || (s.Instability > FreeMapParams.InstabilityPhase2At && predPts < FreeMapParams.InstabilityPhase3At);
                        if (!exact) continue;
                        int hp0 = s.StormHpPaid; int cur = s.Me;
                        bool viaBoss = false;
                        for (int g = 0; g < 200 && cur != to; g++) { int nx = v.Next(cur, to); if (nx == s.Layout.goal) viaBoss = true; s.MoveTo(nx); cur = nx; }
                        if (viaBoss) continue;
                        walks++; hpSum += predHp;
                        if (s.StormHpPaid - hp0 != predHp) { hpBad++; if (first == null) first = $"深さ{floor} {wx} 種{salt} HP 見込み{predHp} 実{s.StormHpPaid - hp0}"; }
                        if (s.Instability != predPts) { ptsBad++; if (first == null) first = $"深さ{floor} {wx} 種{salt} 累計 見込み{predPts} 実{s.Instability}"; }
                    }
            Check(walks > 0 && hpBad == 0 && ptsBad == 0, $"嵐の航行 (3): 道の嵐の HP・歩いて溜まる分の見込み = 本体を歩かせた値 (素・雹・朱の雪) (HP 違い {hpBad} / 累計違い {ptsBad} / {walks} 本・平均 HP {(walks > 0 ? hpSum / (double)walks : 0):F1}) {first}");
        }

        // ── (4) 嵐を見込んだ 1 歩・着いた点のポイント・出口の歩数 ──
        {
            int bad = 0, reach = 0, n = 0; string first = null;
            foreach (int floor in floors)
                for (int salt = 1; salt <= layers / 4; salt++)
                {
                    var s = MakeNavStorm(salt, floor);
                    s.AddInstability(InstabilitySource.Shift, FreeMapParams.InstabilityPhase3At + (salt % 6) - s.Instability);   // フェーズ 3〜4 (ボスが見える)
                    var v = View(s);
                    if (v.Boss != s.Layout.goal) { bad++; first ??= $"ボスが見えない 深さ{floor} 種{salt}"; continue; }
                    var hb = s.HopsFrom(s.Layout.goal);
                    for (int i = 0; i < s.Layout.Count; i++) if (v.ExitHops[i] != hb[i]) { bad++; first ??= $"出口の歩数 深さ{floor} 種{salt} 点{i}"; break; }
                    // 遠い点へ嵐を見込んで歩く: ボスの点を通らずに着く (ボスの点を通らないと着けない点は行き先にしない)
                    var h = HopsAvoiding(s.Layout, s.Me, new[] { s.Layout.goal });
                    int to = Enumerable.Range(0, s.Layout.Count).Where(i => i != s.Layout.goal && h[i] != int.MaxValue).OrderByDescending(i => h[i]).ThenBy(i => i).First();
                    int cur = s.Me; bool ok = true;
                    for (int g = 0; g < 100 && cur != to; g++)
                    {
                        int nx = new StormNavView(s, (bool[])s.Known.Clone(), s.Layout.goal).StormStep(cur, to);
                        if (nx == s.Layout.goal || nx == cur) { ok = false; break; }
                        s.MoveTo(nx); cur = nx;
                    }
                    n++; if (ok && cur == to) reach++;
                }
            double esc = 0.5, er = CombatRewardsEliteRate, nr = 0.0, uw = 0.7;
            bool pts = StormNavView.ExpectedArrivalPoints(TileType.Treasure, true, esc, er, nr, uw) == Instability.PointsOf(InstabilitySource.Treasure)
                    && StormNavView.ExpectedArrivalPoints(TileType.Shop, true, esc, er, nr, uw) == 0
                    && StormNavView.ExpectedArrivalPoints(TileType.Event, true, esc, er, nr, uw) == 0
                    && Math.Abs(StormNavView.ExpectedArrivalPoints(TileType.Battle, true, esc, er, nr, uw) - (Instability.PointsOf(InstabilitySource.Battle) + esc * Instability.PointsOf(InstabilitySource.Escalation))) < 1e-9
                    && Math.Abs(StormNavView.ExpectedArrivalPoints(TileType.EliteBattle, true, esc, er, nr, uw)
                                - (Instability.PointsOf(InstabilitySource.Elite) + esc * Instability.PointsOf(InstabilitySource.Escalation) + er * Instability.PointsOf(InstabilitySource.Relic))) < 1e-9
                    && Math.Abs(StormNavView.ExpectedArrivalPoints(TileType.Treasure, false, esc, er, nr, uw) - uw * (Instability.PointsOf(InstabilitySource.Battle) + esc * Instability.PointsOf(InstabilitySource.Escalation))) < 1e-9;
            Check(bad == 0 && reach == n && pts, $"嵐の航行 (4): 見えているボスの点の出口の歩数 = 道の歩数・嵐を見込んだ 1 歩はボスの点を通らずに着く ({reach}/{n})・着いた点のポイントは本体の表から {first}");
        }

        // ── (5) 静か〜フェーズ 1: 出口の嵐の HP の見込みはポイントが増えて減らない ──
        {
            long cmp = 0, bad = 0; string first = null;
            foreach (int floor in new[] { 1, 3, 5 })
                for (int salt = 1; salt <= layers / 4; salt++)
                {
                    var s = MakeNavStorm(salt, floor);
                    var v = View(s);
                    for (int i = 0; i < s.Layout.Count; i += 3)
                    {
                        double prev = -1;
                        // 出口まで歩く間にフェーズ 2 に入ると、 輪の中心がボスの側へ動いて道の点が輪の中へ戻ることがある (本当の動き) ので、 そこまでは見ない
                        int maxH = v.Candidates.Select(c => v.DistTo(c)[i]).Where(d => d != int.MaxValue).DefaultIfEmpty(0).Max();
                        int walkMax = Instability.PointsOf(InstabilitySource.Walk, FreeMapParams.StormSnowTurns);
                        for (int pts = 0; pts + maxH * walkMax < FreeMapParams.InstabilityPhase2At; pts++)
                        {
                            var e = v.Exit(i, pts);
                            if (!e.reachable) break;
                            cmp++;
                            if (e.hp < prev - 1e-9) { bad++; first ??= $"深さ{floor} 種{salt} 点{i} 累計{pts} {prev}→{e.hp}"; }
                            prev = e.hp;
                        }
                    }
                }
            Check(bad == 0, $"嵐の航行 (5): 静か〜フェーズ 1 では、 出口の嵐の HP の見込みはポイントが増えて減らない ({bad}/{cmp}) {first}");
        }

        // ── (6) 第 2 巡: 歩いて / 戦って HP が尽きる確率 (正規の裾) ──
        {
            bool cdf = Math.Abs(StormNavView.NormalCdf(0) - 0.5) < 1e-7 && Math.Abs(StormNavView.NormalCdf(1.959964) - 0.975) < 1e-6
                    && Math.Abs(StormNavView.NormalCdf(-1) - 0.158655) < 1e-6;
            // 散らばりなし = 段: HP が払う分以下なら 1・ほかは 0
            bool step = StormNavView.DeathProb(0.10, 0.10, 0) == 1 && StormNavView.DeathProb(0.11, 0.10, 0) == 0 && StormNavView.DeathProb(0.5, 0, 0) == 0;
            // HP が減るほど増え、 HP が払う分より上の間は凸 (2 階差分が正): 1 の HP の重みは HP が減るほど増える
            bool mono = true, convex = true; double var = 0.08 * 0.08;
            for (double h = 0.11; h < 0.9; h += 0.01)
            {
                double a = StormNavView.DeathProb(h - 0.01, 0.10, var), b = StormNavView.DeathProb(h, 0.10, var), c = StormNavView.DeathProb(h + 0.01, 0.10, var);
                if (!(a >= b && b >= c)) mono = false;
                if (a + c - 2 * b < -1e-12) convex = false;
            }
            // 出口の散らばり: ボスが見えていれば 0
            int varBad = 0;
            foreach (int floor in floors)
                for (int salt = 1; salt <= 20; salt++)
                {
                    var s = MakeNavStorm(salt, floor);
                    s.AddInstability(InstabilitySource.Shift, FreeMapParams.InstabilityPhase3At - s.Instability);
                    var v = View(s);
                    if (v.Boss >= 0 && v.Exit(s.Me, s.Instability).hpVar != 0) varBad++;
                }
            Check(cdf && step && mono && convex && varBad == 0,
                $"嵐の航行 (6): HP が尽きる確率 Φ((払う分 − HP) ÷ 散らばり) (正規の累積・段・HP について単調で払う分より上は凸・ボスが見えていれば出口の散らばり 0) (cdf {cdf} 段 {step} 単調 {mono} 凸 {convex} 散らばり {varBad})");
        }
    }

    /// <summary>エリートの報酬の秘宝の率の代わり (検算は本体の CombatRewards をコンパイルしない・値は何でもよい)。</summary>
    const double CombatRewardsEliteRate = 0.10;
}

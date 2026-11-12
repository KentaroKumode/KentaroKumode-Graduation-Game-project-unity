using System;
using System.Collections.Generic;
using System.Linq;
using MapSystem;
using MapSystem.FreeMove;

class Wallet : IFreeMapWallet
{
    public int Provision { get; set; } = 100000;
    public int Hp = 1000;
    public void SpendProvision(int a) { Provision -= a; }
    public bool PayHp(int a) { Hp -= a; return Hp <= 0; }
}

static class T
{
    static int fails = 0;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "OK   " : "FAIL ") + what); if (!ok) fails++; }

    /// <summary>既定は旧い作り (追跡者は縄張りの外まで追う・宝なし)。 下の「縄張り」の検算だけが leashed = true で作る。</summary>
    static FreeMapSim Make(int salt, FoeProfile pr, WeatherKind wx, Wallet w = null, int floor = 5, bool leashed = false, bool treasure = false)
    {
        GameLoop.GameRng.Salt = (ulong)salt * 104729;
        var L = FreeMapGenerator.Generate(floor);
        var s = new FreeMapSim(L, w ?? new Wallet(), true, pr, wx);
        s.ConfigureTerritory(leashed, treasure);
        return s;
    }

    /// <summary>縄張りを通らずに from から to へ向かう次の 1 歩 (通れなければ from)。</summary>
    static int StepAvoidingTerritory(FreeMapSim s, int from, int to)
    {
        int n = s.Layout.Count; var d = new int[n];
        for (int i = 0; i < n; i++) d[i] = int.MaxValue;
        var q = new Queue<int>(); d[to] = 0; q.Enqueue(to);
        while (q.Count > 0) { int u = q.Dequeue(); foreach (int v in s.Neighbors(u)) if (d[v] == int.MaxValue && !s.InTerritory(v)) { d[v] = d[u] + 1; q.Enqueue(v); } }
        if (from == to) return from;
        int best = from, bd = int.MaxValue;
        foreach (int v in s.Neighbors(from)) if (!s.InTerritory(v) && d[v] < bd) { bd = d[v]; best = v; }
        return best;
    }

    /// <summary>縄張りの外で、 縄張りの点と道で隣り合う点 (始点から縄張りを通らずに行けるもの)。 無ければ -1。</summary>
    static int BorderOutside(FreeMapSim s)
    {
        foreach (int t in s.Layout.territory.OrderBy(i => i))
            foreach (int v in s.Neighbors(t))
                if (!s.InTerritory(v) && v != s.Layout.goal && (v == s.Me || StepAvoidingTerritory(s, s.Me, v) != s.Me)) return v;
        return -1;
    }

    static bool FoeInsideTerritory(FreeMapSim s)
        => !s.FoeAlive || (s.InTerritory(s.FoeNode) && s.Familiars.All(f => s.InTerritory(f.pos)));

    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        // --- 生成の不変条件 ---
        {
            int bad = 0; var shortest = new List<int>(); int edgesSum = 0;
            for (int salt = 1; salt <= 120; salt++)
            {
                GameLoop.GameRng.Salt = (ulong)salt * 7919;
                foreach (int floor in new[] { 1, 3, 5, 6 })   // 深さ 7 は直線の 1 本道 (FloorPlan.LinearFloor) なので点の数・行き止まりの条件に当てない
                {
                    var L = FreeMapGenerator.Generate(floor);
                    if (!L.IsConnected()) bad++;
                    if (L.adj.Any(a => a.Count < 2)) bad++;
                    if (L.Count != FreeMapParams.NodeCount) bad++;
                    if (L.waypoints.Count < 3) bad++;
                    shortest.Add(L.Hops(L.start)[L.goal]); edgesSum += L.roads.Count;
                }
            }
            Check(bad == 0, $"生成: 異常 {bad} / 平均道 {edgesSum / 480.0:F1} / 終点まで平均 {shortest.Average():F1}手 ({shortest.Min()}-{shortest.Max()})");
        }
        // --- 決定性 ---
        {
            var a = Make(7, FoeProfile.Brute, WeatherKind.Crumble); var b = Make(7, FoeProfile.Brute, WeatherKind.Crumble);
            bool same = a.Layout.roads.SequenceEqual(b.Layout.roads) && a.Layout.home == b.Layout.home;
            for (int i = 0; i < 40; i++) { a.Wait(); b.Wait(); }
            same &= a.FoeNode == b.FoeNode && a.Front == b.Front && a.Roads().SequenceEqual(b.Roads());
            Check(same, "同じ種・同じ操作で同じ結果");
        }
        // --- 巡回 ---
        {
            var s = Make(11, FoeProfile.Brute, WeatherKind.None);
            var seen = new HashSet<int>();
            for (int i = 0; i < 120 && !s.InFight; i++) { s.Wait(); seen.Add(s.FoeNode); }
            Check(seen.Count >= 3, $"巡回: 追跡者は {seen.Count} 点を回った（縄張り {s.Layout.territory.Count}）");
        }
        // --- 補足→追跡→捕まる / ETA ---
        {
            var s = Make(5, FoeProfile.Brute, WeatherKind.None);
            int target = s.Layout.home; int guard = 0; FreeMapEvent acc = 0; bool detectedSeen = false; bool etaSeen = false;
            while (!s.InFight && guard++ < 60)
            {
                int nx = s.StepToward(s.Me, target); if (nx == s.Me) break;
                var ev = s.MoveTo(nx); acc |= ev;
                if ((ev & FreeMapEvent.Detected) != 0) detectedSeen = true;
                if (s.Eta().Valid) etaSeen = true;
            }
            Check(detectedSeen, "補足イベントが出る");
            Check(etaSeen, "補足中は ETA が出る");
            Check((acc & FreeMapEvent.Caught) != 0 && s.Contact == FreeMapSim.ContactKind.Body, "捕まる (Caught)");
            Check(s.PendingArrival == s.Me, "着いた点が戦闘後の発動待ちになる");
            s.ResolveBodyWon();
            Check(!s.FoeAlive && !s.InFight, "追跡者に勝つと倒れる");
            Check(s.TakePendingArrival() == s.Me, "待ちの点を取り出せる");
        }
        // --- 逃げる ---
        {
            var s = Make(5, FoeProfile.Brute, WeatherKind.None);
            int target = s.Layout.home; int guard = 0;
            while (!s.InFight && guard++ < 60) { int nx = s.StepToward(s.Me, target); if (nx == s.Me) break; s.MoveTo(nx); }
            int prev = s.PrevNode; s.ResolveBodyFled();
            Check(s.Me == prev && s.FoeFreezeTurns >= FreeMapParams.FleeFreezeTurns && !s.InFight, "逃げると直前の点へ戻り、追跡者は足止め");
        }
        // --- 囮罠 ---
        {
            var s = Make(5, FoeProfile.Brute, WeatherKind.None);
            int target = s.Layout.home; int guard = 0;
            while (guard++ < 60)
            {
                int d = s.HopsFrom(s.Me)[s.FoeNode];
                if (d <= FreeMapParams.LureNotice && d >= 4) break;
                int nx = s.StepToward(s.Me, target); if (nx == s.Me) break; s.MoveTo(nx);
                if (s.InFight) break;
            }
            var ev = s.PlaceLure();
            bool alerted = (ev & FreeMapEvent.FoeAlerted) != 0 && s.FoeMode == FoeMode.Alert;
            Check(alerted, $"囮罠で気を引かれる (mode={s.FoeMode})");
            bool triggered = false;
            for (int i = 0; i < 15 && !s.InFight; i++)
            {
                var e = s.Wait();
                if ((e & FreeMapEvent.LureTriggered) != 0) { triggered = true; break; }
            }
            Check(triggered || s.InFight, $"追跡者が囮罠を踏んで足止めされる (foeFreeze={s.FoeFreezeTurns}, caught={s.InFight})");
        }
        // --- 照明・偵察 ---
        {
            var s = Make(3, FoeProfile.Brute, WeatherKind.None);
            int turn = s.Turn; int knownBefore = s.Known.Count(k => k);
            s.UseFlare();
            Check(s.Turn == turn && s.FlareOn && s.Known.Count(k => k) > knownBefore, "照明は手番を使わず視界を広げる");
            s.Wait(); s.Wait(); s.Wait();
            Check(!s.FlareOn, "照明は3手番で切れる");
            var s2 = Make(3, FoeProfile.Brute, WeatherKind.None);
            int t2 = s2.Turn; s2.Recon();
            Check(s2.Turn == t2 + 1, "偵察は1手番");
        }
        // --- 天候: 道が崩れる / 点が沈む ---
        foreach (var wx in new[] { WeatherKind.Crumble, WeatherKind.Sink })
        {
            int broken = 0, changed = 0;
            for (int salt = 1; salt <= 60; salt++)
            {
                var s = Make(salt, FoeProfile.Summoner, wx);
                for (int i = 0; i < 80 && !s.InFight; i++)
                {
                    var ev = s.Wait();
                    if ((ev & FreeMapEvent.MapChanged) != 0) changed++;
                    if (s.HopsFrom(s.Me)[s.Layout.goal] == int.MaxValue) { broken++; break; }
                }
            }
            Check(broken == 0 && changed > 0, $"{wx}: 終点へは必ずつながったまま（崩れ/沈み {changed} 回, 切断 {broken}）");
        }
        // --- 天候: 雹・雪・砂 ---
        {
            var w = new Wallet();
            var s = Make(9, FoeProfile.Summoner, WeatherKind.Hail, w);
            for (int i = 0; i < 12; i++) s.Wait();
            int hp0 = w.Hp; int moved = 0;
            foreach (int c in s.Neighbors(s.Me)) if (s.InFront(c)) { s.MoveTo(c); moved++; break; }
            Check(moved == 0 || w.Hp == hp0 - 1, $"雹: 前線の中へ入ると HP-1 (moved={moved}, hp {hp0}->{w.Hp})");
            var w2 = new Wallet();
            var s2 = Make(9, FoeProfile.Summoner, WeatherKind.Snow, w2);
            for (int i = 0; i < 12; i++) s2.Wait();
            int t0 = s2.Turn; int m2 = 0;
            foreach (int c in s2.Neighbors(s2.Me)) if (s2.InFront(c) || s2.InFront(s2.Me)) { s2.MoveTo(c); m2++; break; }
            Check(m2 == 0 || s2.Turn == t0 + 2 || s2.InFight, $"雪: 前線の中の移動は2手番 (moved={m2}, turn {t0}->{s2.Turn})");
            var w3 = new Wallet();
            var s3 = Make(9, FoeProfile.Summoner, WeatherKind.Sand, w3);
            for (int i = 0; i < 12; i++) s3.Wait();
            int p0 = w3.Provision; int m3 = 0;
            foreach (int c in s3.Neighbors(s3.Me)) if (s3.InFront(c) || s3.InFront(s3.Me)) { s3.MoveTo(c); m3++; break; }
            Check(m3 == 0 || w3.Provision == p0 - FreeMapParams.MoveSupply || w3.Provision == p0 - FreeMapParams.MoveSupply - 1 || s3.InFight, $"砂: 前線の中の1歩は物資 +1 のことがある (moved={m3}, {p0}->{w3.Provision})");
        }
        // --- 物資不足は HP で払う ---
        {
            var w = new Wallet { Provision = 0, Hp = 30 };
            var s = Make(2, FoeProfile.Summoner, WeatherKind.None, w);
            s.MoveTo(s.Neighbors(s.Me)[0]);
            int hpCost = FreeMapParams.MoveSupply * FreeMapParams.HpPerSupply;   // 足りない物資 1 につき HP HpPerSupply (2026-11-09 のデノミ後)
            Check(w.Provision == 0 && w.Hp == 30 - hpCost, $"物資不足は HP で払う (prov {w.Provision}, hp {w.Hp})");
        }
        // --- 猟犬・使い魔 ---
        {
            var s = Make(4, FoeProfile.Houndmaster, WeatherKind.None);
            Check(s.Familiars.Count == 1 && s.Familiars[0].hound, "猟犬使いは最初から猟犬を連れている");
            var s2 = Make(5, FoeProfile.Summoner, WeatherKind.None);
            int target = s2.Layout.home; int guard = 0; bool launched = false;
            while (!s2.InFight && guard++ < 60) { int nx = s2.StepToward(s2.Me, target); if (nx == s2.Me) break; var ev = s2.MoveTo(nx); if ((ev & FreeMapEvent.FamiliarLaunched) != 0) launched = true; }
            for (int i = 0; i < 8 && !s2.InFight; i++) { var ev = s2.Wait(); if ((ev & FreeMapEvent.FamiliarLaunched) != 0) launched = true; }
            Check(launched || s2.InFight, "召喚者は補足中に使い魔を放つ");
        }
        // --- 魔石 ---
        {
            var s = Make(5, FoeProfile.Brute, WeatherKind.None);
            var st = s.Stone();
            Check(st.level >= 0 && st.level <= 3, $"魔石 level={st.level} {st.Text}");
        }
        // --- 縄張り (2026-11-12): 追跡者 (本体・使い魔) は縄張りの外へ出ない ---
        {
            var profiles = new[] { FoeProfile.Summoner, FoeProfile.Houndmaster, FoeProfile.Sprinter, FoeProfile.Brute };
            int outside = 0, runs = 0, caughtOutside = 0;
            foreach (var pr in profiles)
                foreach (var wx in new[] { WeatherKind.None, WeatherKind.Crumble, WeatherKind.Sink })
                    for (int salt = 1; salt <= 40; salt++)
                    {
                        var s = Make(salt, pr, wx, leashed: true); runs++;
                        // 中心へ歩いて (捕まれば逃げて) から、 終点へ直行する
                        for (int step = 0; step < 120 && !s.Dead; step++)
                        {
                            int goal = step < 40 ? s.Layout.home : s.Layout.goal;
                            int nx = s.StepToward(s.Me, goal);
                            var ev = nx == s.Me ? s.Wait() : s.MoveTo(nx);
                            if (!FoeInsideTerritory(s)) outside++;
                            if ((ev & FreeMapEvent.Caught) != 0) { if (!s.InTerritory(s.Me)) caughtOutside++; s.ResolveBodyFled(); }
                            if ((ev & FreeMapEvent.FamiliarContact) != 0) { if (!s.InTerritory(s.Me)) caughtOutside++; s.ResolveFamiliarFight(true); }
                            if (s.Me == s.Layout.goal) break;
                        }
                    }
            Check(outside == 0 && caughtOutside == 0, $"縄張り: 追跡者と使い魔は縄張りの外へ出ない・外で捕まらない ({runs} 層・外 {outside} 手・外で接触 {caughtOutside})");
        }
        // --- 縄張りの外に居れば補足されない・外の囮罠には縁まで引かれて踏まない ---
        foreach (var pr in new[] { FoeProfile.Summoner, FoeProfile.Houndmaster, FoeProfile.Sprinter, FoeProfile.Brute })
        {
            int tested = 0, detected = 0, leftT = 0, lureTrig = 0, alerted = 0, backToPatrol = 0;
            for (int salt = 1; salt <= 60; salt++)
            {
                var s = Make(salt, pr, WeatherKind.None, leashed: true);
                int border = BorderOutside(s); if (border < 0) continue;
                for (int g = 0; g < 60 && s.Me != border && !s.InFight; g++) { int nx = StepAvoidingTerritory(s, s.Me, border); if (nx == s.Me) break; s.MoveTo(nx); }
                if (s.Me != border || s.InFight) continue;
                tested++;
                var ev0 = s.PlaceLure();
                if ((ev0 & FreeMapEvent.FoeAlerted) != 0) alerted++;
                for (int i = 0; i < 30; i++)
                {
                    var ev = s.Wait();
                    if ((ev & (FreeMapEvent.Detected | FreeMapEvent.Caught | FreeMapEvent.FamiliarContact)) != 0) detected++;
                    if ((ev & FreeMapEvent.LureTriggered) != 0) lureTrig++;
                    if (!FoeInsideTerritory(s)) leftT++;
                }
                if (s.FoeMode == FoeMode.Patrol) backToPatrol++;
            }
            Check(tested > 0 && detected == 0 && leftT == 0 && lureTrig == 0 && (pr.foeSteps == 0 || backToPatrol == tested),   // 動かない召喚者は警戒のまま (旧い作りと同じ)
                $"縄張りの外 ({pr.id}): 補足されない・外の囮罠は踏まない・囮罠が切れたら巡回へ戻る (試 {tested} / 補足 {detected} / 外へ {leftT} / 踏む {lureTrig} / 気を引かれた {alerted} / 巡回へ {backToPatrol})");
        }
        // --- 縄張りの宝 ---
        {
            int bad = 0, taken = 0, layers = 0;
            for (int salt = 1; salt <= 120; salt++)
            {
                var s = Make(salt, FoeProfile.Brute, WeatherKind.None, leashed: true, treasure: true); layers++;
                int t = s.TreasureNode;
                if (t < 0 || !s.InTerritory(t) || t == s.Layout.home || t != FreeMapSim.TreasurePointOf(s.Layout)) { bad++; continue; }
                var none = Make(salt, FoeProfile.Brute, WeatherKind.None, leashed: true, treasure: false);
                if (none.TreasureNode != -1 || !none.Layout.roads.SequenceEqual(s.Layout.roads) || !none.Layout.types.SequenceEqual(s.Layout.types)) bad++;   // 宝の有無で地図は変わらない
                if (s.TakeTreasure()) bad++;                                   // 宝の点に居なければ取れない
                for (int g = 0; g < 80 && s.Me != t && !s.Dead; g++)
                {
                    var ev = s.MoveTo(s.StepToward(s.Me, t));
                    if ((ev & FreeMapEvent.Caught) != 0) s.ResolveBodyWon();
                    if ((ev & FreeMapEvent.FamiliarContact) != 0) s.ResolveFamiliarFight(false);
                }
                if (s.Me != t) continue;
                if (!s.TakeTreasure() || s.TakeTreasure() || s.TreasureAvailable) bad++; else taken++;   // 1 層 1 回
                var saved = s.CaptureState();
                var r = Make(salt, FoeProfile.Brute, WeatherKind.None, leashed: true, treasure: true); r.LoadState(saved, null);
                if (!r.TreasureTaken) bad++;                                    // 中断セーブを跨ぐ
            }
            Check(bad == 0 && taken > 0, $"縄張りの宝: 縄張りの中 (中心の隣) に置かれ、 1 層 1 回だけ取れる (層 {layers} / 取った {taken} / 異常 {bad})");
        }
        // --- 素朴な BOT の捕まり率 (直行・旧い作り / 縄張りを出ない) ---
        foreach (bool leash in new[] { false, true })
        foreach (var pr in new[] { FoeProfile.Summoner, FoeProfile.Houndmaster, FoeProfile.Sprinter, FoeProfile.Brute })
        {
            int caught = 0, reached = 0, N = 300;
            for (int salt = 1; salt <= N; salt++)
            {
                var s = Make(salt, pr, WeatherKind.None, leashed: leash);
                for (int step = 0; step < 80 && !s.Dead; step++)
                {
                    int next = s.StepToward(s.Me, s.Layout.goal);
                    if (next == s.Me) break;
                    var ev = s.MoveTo(next);
                    if ((ev & (FreeMapEvent.Caught | FreeMapEvent.FamiliarContact)) != 0) { caught++; break; }
                    if (s.Me == s.Layout.goal) { reached++; break; }
                }
            }
            Console.WriteLine($"直行BOT {(leash ? "縄張り" : "旧い作り")} {pr.id,-10}: 捕まる {100.0 * caught / N:F0}% / 到達 {100.0 * reached / N:F0}%");
        }
        Console.WriteLine(fails == 0 ? "ALL OK" : $"FAILED {fails}");
    }
}

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
        foreach (int t in s.Territory.OrderBy(i => i))
            foreach (int v in s.Neighbors(t))
                if (!s.InTerritory(v) && v != s.Layout.goal && (v == s.Me || StepAvoidingTerritory(s, s.Me, v) != s.Me)) return v;
        return -1;
    }

    /// <summary>最短の道 (StepToward をたどる) で踏む縄張りの点の数 (from を含めず to を含む)。 AutoRunner.TerritoryStepsOnShortest と同じ。</summary>
    static int TerritoryStepsOnShortest(FreeMapSim s, int from, int to)
    {
        int c = 0, cur = from;
        for (int g = 0; g < 200 && cur != to; g++) { int nx = s.StepToward(cur, to); if (nx == cur) break; cur = nx; if (s.InTerritory(cur)) c++; }
        return c;
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
            Check(seen.Count >= 3, $"巡回: 追跡者は {seen.Count} 点を回った（縄張り {s.Territory.Count}）");
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
        // --- 近道 (イベント「近道+N」= ただで動ける回数・2026-11-12) ---
        {
            var w = new Wallet { Provision = 5 };
            var s = Make(5, FoeProfile.Brute, WeatherKind.None, w);
            s.UseFlare();
            int p0 = w.Provision, t0 = s.Turn, foe0 = s.FoeNode, flare0 = s.FlareTurnsLeft;
            s.GrantFreeMoves(2);
            Check(s.FreeMovesLeft == 2 && s.Me == s.Layout.start, "近道: 受けただけでは動かない (残り 2)");
            int a = s.Neighbors(s.Me)[0];
            var ev1 = s.MoveTo(a);
            Check(w.Provision == p0 && s.Turn == t0 && s.FoeNode == foe0 && s.FlareTurnsLeft == flare0 && s.FreeMovesLeft == 1
                  && (ev1 & FreeMapEvent.TurnPassed) == 0 && (ev1 & FreeMapEvent.Arrived) != 0,
                  $"近道: ただの 1 歩は物資もターンも減らず、追跡者も照明も動かない (物資 {p0}->{w.Provision}, ターン {t0}->{s.Turn}, 残り {s.FreeMovesLeft})");
            Check(s.HasTrace(a), "近道: ただの 1 歩も痕跡は残す");
            s.Wait();
            Check(s.FreeMovesLeft == 1 && s.Turn == t0 + 1, "近道: 待つでは減らない");
            s.Recon();
            Check(s.FreeMovesLeft == 1 && s.Turn == t0 + 2, "近道: 偵察でも減らない");
            int t1 = s.Turn, p1 = w.Provision;
            s.MoveTo(s.Neighbors(s.Me)[0]);
            Check(s.FreeMovesLeft == 0 && s.Turn == t1 && w.Provision == p1, "近道: 2 回目もただで、残りは 0");
            int t2 = s.Turn, p2 = w.Provision;
            if (!s.InFight) s.MoveTo(s.Neighbors(s.Me)[0]);
            Check(s.InFight || (s.Turn == t2 + 1 && w.Provision == p2 - FreeMapParams.MoveSupply), "近道: 使い切った後の 1 歩は通常どおり払う");
            GameLoop.GameRng.Salt = 5 * 104729;
            var next = new FreeMapSim(FreeMapGenerator.Generate(6), new Wallet(), true, FoeProfile.Brute, WeatherKind.None);
            Check(next.FreeMovesLeft == 0, "近道: 層を出ると (中身を作り直すと) 残りは消える");

            // 中断セーブを跨ぐ (2026-11-13): 残りの回数を写して戻し、 戻した後の 1 歩もただ
            var sv = Make(5, FoeProfile.Brute, WeatherKind.None, new Wallet { Provision = 5 });
            sv.GrantFreeMoves(3);
            sv.MoveTo(sv.Neighbors(sv.Me)[0]);
            var saved = sv.CaptureState();
            var w4 = new Wallet { Provision = 5 };
            var rs = Make(5, FoeProfile.Brute, WeatherKind.None, w4); rs.LoadState(saved, null);
            int t4 = rs.Turn;
            Check(rs.FreeMovesLeft == 2, $"近道: 中断セーブを跨いで残りが戻る (残り {rs.FreeMovesLeft})");
            if (!rs.InFight) rs.MoveTo(rs.Neighbors(rs.Me)[0]);
            Check(rs.InFight || (rs.FreeMovesLeft == 1 && rs.Turn == t4 && w4.Provision == 5), $"近道: 戻した後の 1 歩もただ (残り {rs.FreeMovesLeft}, ターン {t4}->{rs.Turn}, 物資 {w4.Provision})");

            // 雹の前線の内側: ただの 1 歩は HP −1 を払わない (2026-11-13・作者の決定)。 通常の 1 歩は払う
            var wh = new Wallet { Hp = 100 };
            var sh = Make(5, FoeProfile.Brute, WeatherKind.Hail, wh);
            sh.ShiftFront(999);                                     // 前線を層の端まで進める (全部の点が内側)
            int hp0 = wh.Hp;
            sh.GrantFreeMoves(1);
            int hj = sh.Neighbors(sh.Me)[0];
            bool hailHere = sh.WeatherAt(hj, WeatherKind.Hail);
            sh.MoveTo(hj);
            Check(hailHere && wh.Hp == hp0, $"近道: 雹の前線の内側でも、 ただの 1 歩は HP を払わない (雹={hailHere}, HP {hp0}->{wh.Hp})");
            int hp1 = wh.Hp;
            if (!sh.InFight) sh.MoveTo(sh.Neighbors(sh.Me)[0]);
            Check(sh.InFight || wh.Hp == hp1 - 1, $"近道: 使い切った後の雹の 1 歩は HP −1 (HP {hp1}->{wh.Hp})");

            // 追跡者の居る点へ自分から踏み込むと、ターンを進めずにその場で捕まる
            var s2 = Make(5, FoeProfile.Brute, WeatherKind.None);
            s2.PullFoe(99);
            bool adj = s2.HopsFrom(s2.Me)[s2.FoeNode] == 1;
            int t3 = s2.Turn;
            s2.GrantFreeMoves(1);
            var ev2 = adj ? s2.MoveTo(s2.FoeNode) : FreeMapEvent.None;
            Check(adj && (ev2 & FreeMapEvent.Caught) != 0 && s2.Contact == FreeMapSim.ContactKind.Body && s2.Turn == t3 && s2.PendingArrival == s2.Me,
                  $"近道: 追跡者の点へ踏み込むと、ターンを進めずに捕まる (隣={adj}, ターン {t3}->{s2.Turn})");
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
        // --- イベントの「追跡者を呼ぶ+N」「追跡者を遠ざける+N」(2026-11-13): 縄張りを出ない追跡者は、 縄張りの中の道だけで動く (縁で止まる) ---
        //   自分の居場所を、 縄張りの外 (縁の隣・離れた所) と中で変えて、 呼ぶ+3・遠ざける+4 の後に本体と猟犬が縄張りの外に居る層を数える。
        //   旧い作り (縄張りの外まで追う) は、 2026-11-12 の動き (縄張りを見ない) と同じ点へ動くことも確かめる。
        {
            var profiles = new[] { FoeProfile.Summoner, FoeProfile.Houndmaster, FoeProfile.Sprinter, FoeProfile.Brute };
            int layers = 0, pullOut = 0, pushOut = 0, pullMoved = 0, pushMoved = 0, oldDiff = 0, oldLayers = 0, pullOutsideMe = 0;
            foreach (var pr in profiles)
                for (int salt = 1; salt <= 50; salt++)
                    for (int where = 0; where < 3; where++)
                    {
                        // where: 0 = 始点のまま (縄張りの外・離れた所) / 1 = 縄張りの縁の外 / 2 = 縄張りの中 (中心の隣へ歩く)
                        var s = Make(salt, pr, WeatherKind.None, leashed: true, treasure: true);
                        if (!s.FoeAlive) continue;
                        if (where == 1)
                        {
                            int b = BorderOutside(s); if (b < 0) continue;
                            for (int g = 0; g < 60 && s.Me != b && !s.InFight; g++) { int nx = StepAvoidingTerritory(s, s.Me, b); if (nx == s.Me) break; s.GrantFreeMoves(1); s.MoveTo(nx); }
                            if (s.Me != b || s.InFight) continue;
                        }
                        else if (where == 2)
                        {
                            for (int g = 0; g < 60 && !s.InTerritory(s.Me) && !s.InFight; g++) { int nx = s.StepToward(s.Me, s.Layout.home); if (nx == s.Me) break; s.GrantFreeMoves(1); s.MoveTo(nx); }
                            if (!s.InTerritory(s.Me) || s.InFight) continue;
                        }
                        layers++;
                        if (!s.InTerritory(s.Me)) pullOutsideMe++;
                        var save = s.CaptureState();
                        int f0 = s.FoeNode;
                        s.PullFoe(3);
                        if (s.FoeNode != f0) pullMoved++;
                        if (!FoeInsideTerritory(s)) pullOut++;
                        s.LoadState(save, s.Perks);
                        s.PushFoe(4);
                        if (s.FoeNode != f0) pushMoved++;
                        if (!FoeInsideTerritory(s)) pushOut++;
                    }
            Check(layers > 0 && pullOut == 0 && pushOut == 0,
                  $"イベントの呼ぶ+3・遠ざける+4 (縄張りを出ない追跡者): 後に番人が縄張りの外に居る層 0 (試 {layers}・うち自分が縄張りの外 {pullOutsideMe} / 外へ出た 呼ぶ {pullOut}・遠ざける {pushOut} / 動いた 呼ぶ {pullMoved}・遠ざける {pushMoved})");

            // 旧い作り: 縄張りを見ない 2026-11-12 の動きと同じ点へ (呼ぶ = 自分への最短の道を隣の手前まで / 遠ざける = 自分からの歩数がいちばん増える隣)
            foreach (var pr in profiles)
                for (int salt = 1; salt <= 50; salt++)
                {
                    var s = Make(salt, pr, WeatherKind.None);
                    if (!s.FoeAlive) continue;
                    for (int g = 0; g < salt % 7 && !s.InFight; g++) { s.GrantFreeMoves(1); s.MoveTo(s.Neighbors(s.Me)[0]); }
                    if (s.InFight) continue;
                    oldLayers++;
                    var save = s.CaptureState();
                    int exp = s.FoeNode;
                    for (int k = 0; k < 3; k++) { int nx = s.StepToward(exp, s.Me); if (nx == exp || nx == s.Me) break; exp = nx; }
                    s.PullFoe(3);
                    if (s.FoeNode != exp) oldDiff++;
                    s.LoadState(save, s.Perks);
                    var d = s.HopsFrom(s.Me); exp = s.FoeNode;
                    for (int k = 0; k < 4; k++) { int best = exp; foreach (int v in s.Neighbors(exp)) if (d[v] != int.MaxValue && d[v] > d[best]) best = v; if (best == exp) break; exp = best; }
                    s.PushFoe(4);
                    if (s.FoeNode != exp) oldDiff++;
                }
            Check(oldLayers > 0 && oldDiff == 0, $"イベントの呼ぶ・遠ざける (旧い作り): 縄張りを見ない動きのまま ({oldLayers} 層・食い違い {oldDiff})");
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
                if (!s.HasFoe) continue;   // 避ける道のために追跡者を置かなかった層 (TerritoryBypass の ④)
                if (t < 0 || !s.InTerritory(t) || t == s.Home) { bad++; continue; }
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
        // --- 縄張りを横切らない道 (FreeMapSim.StepAvoidingTerritory) が、 縄張りを通らない道があればそれを最短で返す ---
        {
            int layers = 0, bad = 0, hasAvoid = 0;
            for (int salt = 1; salt <= 200; salt++)
            {
                var s = Make(salt, FoeProfile.Brute, WeatherKind.None, leashed: true); layers++;
                int to = s.Layout.goal;
                int nx = s.StepAvoidingTerritory(s.Me, to, out int hops, out int terr);
                // 縄張りの点を通らない幅優先の歩数 (検算用の素朴な計算)
                var d = new int[s.Layout.Count]; for (int i = 0; i < d.Length; i++) d[i] = int.MaxValue;
                var q = new Queue<int>(); d[to] = 0; q.Enqueue(to);
                while (q.Count > 0) { int u = q.Dequeue(); foreach (int v in s.Neighbors(u)) if (d[v] == int.MaxValue && !s.InTerritory(v)) { d[v] = d[u] + 1; q.Enqueue(v); } }
                int clean = int.MaxValue; foreach (int v in s.Neighbors(s.Me)) if (!s.InTerritory(v) && d[v] != int.MaxValue) clean = Math.Min(clean, d[v] + 1);
                if (clean != int.MaxValue) { hasAvoid++; if (terr != 0 || hops != clean || s.InTerritory(nx)) bad++; }
                else if (terr <= 0) bad++;
                // たどると、 言った歩数で着き、 言った数だけ縄張りを踏む
                int cur = s.Me, walked = 0, stepped = 0;
                for (int g = 0; g < 200 && cur != to; g++) { int n2 = s.StepAvoidingTerritory(cur, to, out _, out _); if (n2 == cur) break; cur = n2; walked++; if (s.InTerritory(cur)) stepped++; }
                if (cur != to || walked != hops || stepped != terr) bad++;
            }
            Check(bad == 0, $"縄張りを横切らない道: 通らない道があれば最短・たどると言った歩数と踏む数のとおり (層 {layers} / 通らない道あり {hasAvoid} / 異常 {bad})");
        }
        // --- 縄張りを避ける BOT の捕まり率 (AutoRunner.TerritoryAwareStep と同じ決め方・物資は数えない) ---
        //   遠回りの上限 0 = 直行と同じ。 縄張りの外では捕まらないので、 捕まる率は「縄張りを踏まずに済まなかった層」の分だけ残る
        foreach (int detour in new[] { 0, 3, 5, 8, 99 })
        foreach (var pr in new[] { FoeProfile.Summoner, FoeProfile.Houndmaster, FoeProfile.Sprinter, FoeProfile.Brute })
        {
            int caught = 0, reached = 0, entered = 0, N = 300, caughtOut = 0;
            for (int salt = 1; salt <= N; salt++)
            {
                var s = Make(salt, pr, WeatherKind.None, leashed: true);
                bool inT = false;
                for (int step = 0; step < 80 && !s.Dead; step++)
                {
                    int to = s.Layout.goal;
                    int next = s.StepToward(s.Me, to);
                    if (detour > 0)
                    {
                        int alt = s.StepAvoidingTerritory(s.Me, to, out int hops, out int terr);
                        int direct = s.HopsFrom(s.Me)[to];
                        if (alt != next && alt != s.Me && terr < TerritoryStepsOnShortest(s, s.Me, to) && hops - direct <= detour) next = alt;
                    }
                    if (next == s.Me) break;
                    var ev = s.MoveTo(next);
                    if (s.InTerritory(s.Me)) inT = true;
                    if ((ev & (FreeMapEvent.Caught | FreeMapEvent.FamiliarContact)) != 0) { caught++; if (!s.InTerritory(s.Me)) caughtOut++; break; }
                    if (s.Me == s.Layout.goal) { reached++; break; }
                }
                if (inT) entered++;
            }
            Console.WriteLine($"避けるBOT 遠回り+{detour,-2} {pr.id,-10}: 捕まる {100.0 * caught / N:F0}% / 縄張りに入った {100.0 * entered / N:F0}% / 到達 {100.0 * reached / N:F0}%");
            if (caughtOut > 0) Check(false, $"避けるBOT: 縄張りの外で捕まった ({pr.id} {caughtOut})");
        }
        // --- 縄張りを避ける道の保証 (2026-11-13): 旧い作り・削らない口は今までと同じ動き / 避ける道を歩けば気づかれない ---
        {
            int diff = 0, refBad = 0, layers = 0;
            foreach (int floor in new[] { 3, 5, 6 })
                for (int salt = 1; salt <= 100; salt++)
                {
                    GameLoop.GameRng.Salt = (ulong)salt * 104729;
                    var L = FreeMapGenerator.Generate(floor);
                    var a = new FreeMapSim(L, new Wallet(), true, FoeProfile.Houndmaster, WeatherKind.Crumble);
                    var b = new FreeMapSim(L, new Wallet(), true, FoeProfile.Houndmaster, WeatherKind.Crumble);
                    a.ConfigureTerritory(false, true, keepBypass: true); b.ConfigureTerritory(false, true, keepBypass: false);   // 旧い作り: 口によらず同じ
                    var c = new FreeMapSim(L, new Wallet(), true, FoeProfile.Houndmaster, WeatherKind.Crumble);
                    c.ConfigureTerritory(true, true, keepBypass: false);                                                       // 削らない口: 形の縄張りそのもの
                    if (!ReferenceEquals(c.Territory, L.territory) || !ReferenceEquals(c.Waypoints, L.waypoints) || c.Home != L.home
                        || !ReferenceEquals(a.Territory, L.territory) || a.Home != L.home || a.TreasureNode != FreeMapSim.TreasurePointOf(L)) refBad++;
                    layers++;
                    for (int k = 0; k < 60 && !a.InFight && !b.InFight; k++)
                    {
                        int nx = a.StepToward(a.Me, k < 30 ? L.home : L.goal);
                        if (nx == a.Me) { a.Wait(); b.Wait(); } else { a.MoveTo(nx); b.MoveTo(nx); }
                        if (a.FoeNode != b.FoeNode || a.FoeMode != b.FoeMode || a.Me != b.Me || a.Familiars.Count != b.Familiars.Count) { diff++; break; }
                    }
                }
            Check(diff == 0 && refBad == 0, $"避ける道の保証: 旧い作りは口によらず同じ動き・削らない口は形の縄張りそのもの ({layers} 層・食い違い {diff} / 参照 {refBad})");
        }
        foreach (var pr in new[] { FoeProfile.Summoner, FoeProfile.Houndmaster, FoeProfile.Sprinter, FoeProfile.Brute })
        {
            int noticed = 0, reached = 0, layers = 0, stepped = 0;
            foreach (int floor in new[] { 3, 5, 6 })
                for (int salt = 1; salt <= 150; salt++)
                {
                    GameLoop.GameRng.Salt = (ulong)salt * 31337 + (ulong)floor;
                    var L = FreeMapGenerator.Generate(floor);
                    var s = new FreeMapSim(L, new Wallet(), true, pr, WeatherKind.None);
                    s.ConfigureTerritory(true, true); layers++;
                    s.UseFlare();   // 照明で補足の歩数を伸ばしても (3 歩・召喚者は 5 歩)、 縄張りの外なら気づかれない
                    for (int g = 0; g < 80 && s.Me != L.goal; g++)
                    {
                        int nx = s.StepAvoidingTerritory(s.Me, L.goal, out _, out int terr);
                        if (nx == s.Me || terr > 0) break;
                        if (g % 6 == 2) s.UseFlare();
                        var ev = s.MoveTo(nx);
                        if (s.InTerritory(s.Me)) stepped++;
                        if ((ev & (FreeMapEvent.Detected | FreeMapEvent.Caught | FreeMapEvent.FamiliarContact | FreeMapEvent.SmokeUsed)) != 0 || s.FoeMode == FoeMode.Chase) { noticed++; break; }
                    }
                    if (s.Me == L.goal) reached++;
                }
            Check(reached == layers && noticed == 0 && stepped == 0, $"避ける道を歩く ({pr.id}・照明つき): 全層で縄張りを踏まずに終点へ・気づかれない ({reached}/{layers} 到達 / 気づかれた {noticed} / 踏んだ {stepped})");
        }
        BypassReport();
        Console.WriteLine(fails == 0 ? "ALL OK" : $"FAILED {fails}");
    }

    /// <summary>形の道で、 avoid の点を通らない from からの歩数 (avoid = null なら全部通れる)。</summary>
    static int[] HopsAvoiding(FreeMapLayout L, int from, ICollection<int> avoid)
    {
        var d = new int[L.Count]; for (int i = 0; i < d.Length; i++) d[i] = int.MaxValue;
        var q = new Queue<int>(); d[from] = 0; q.Enqueue(from);
        while (q.Count > 0) { int u = q.Dequeue(); foreach (int v in L.adj[u]) if (d[v] == int.MaxValue && (avoid == null || !avoid.Contains(v))) { d[v] = d[u] + 1; q.Enqueue(v); } }
        return d;
    }

    /// <summary>物資だけで (HP で払わずに) 始点から終点へ着けるか。 層に入る時 startProv・店 (avoid の外) に着いたら上限 cap まで買い足せる (金は数えない)。
    /// 区間 (始点・店・終点の間) の歩数は avoid の点を通らない道で数える。</summary>
    static bool Refuelable(FreeMapLayout L, ICollection<int> avoid, int startProv, int cap)
    {
        var pts = new List<int> { L.start };
        for (int i = 2; i < L.Count; i++) if (L.types[i] == MapSystem.TileType.Shop && (avoid == null || !avoid.Contains(i))) pts.Add(i);
        var reached = new HashSet<int> { L.start }; var q = new Queue<int>(); q.Enqueue(L.start);
        while (q.Count > 0)
        {
            int u = q.Dequeue(); int fuel = u == L.start ? startProv : cap;
            var d = HopsAvoiding(L, u, avoid);
            if (d[L.goal] <= fuel) return true;
            foreach (int p in pts) if (!reached.Contains(p) && d[p] <= fuel) { reached.Add(p); q.Enqueue(p); }
        }
        return false;
    }

    static string Dist(List<int> xs)
    {
        if (xs.Count == 0) return "(なし)";
        var a = xs.OrderBy(x => x).ToList();
        int P(double p) => a[Math.Min(a.Count - 1, (int)Math.Floor(p * (a.Count - 1) + 0.5))];
        return $"中央値 {P(0.5)} / 90% 点 {P(0.9)} / 最大 {a[a.Count - 1]} / 平均 {a.Average():F2} (n={a.Count})";
    }

    /// <summary>縄張りを避ける道の保証 (2026-11-13・TerritoryBypass) の検算: 追跡者の居る層 (深さ 3/5/6) の全部で、 多数の種。</summary>
    static void BypassReport()
    {
        const int Seeds = 3000;
        const int Cap = 10;   // 物資の素の上限 (ProvisionRules.Max・GAME.md §10-1)。 層に入る時は前の層のボス +3 と前哨基地 +4 を足して上限で切る
        int layers = 0, before = 0, after = 0, blocked = 0, trimmed = 0, treasureMoved = 0, treasureLost = 0, homeLost = 0, disconnected = 0;
        var sizeBefore = new List<int>(); var sizeAfter = new List<int>(); var sizeAfterTrimmed = new List<int>(); var sizeBeforeTrimmed = new List<int>();
        var hist = new int[12]; var removedN = new List<int>(); var stages = new int[5]; int addedLayers = 0, homeMoved = 0; var addedN = new List<int>();
        var extraAll = new List<int>(); var extraTrimmed = new List<int>(); var bypassHops = new List<int>(); var directHops = new List<int>();
        int overCap = 0, overCapDirect = 0, overCapTrimmed = 0; var feasDirect = new int[2]; var feasBypass = new int[2]; var lostByBypass = new int[2]; var lostByBypassTrimmed = new int[2];
        var perFloor = new Dictionary<int, (int n, int before, int trimmed)>();
        foreach (int floor in new[] { 3, 5, 6 })
            for (int salt = 1; salt <= Seeds; salt++)
            {
                GameLoop.GameRng.Salt = (ulong)salt * 7919 + (ulong)floor;
                var L = FreeMapGenerator.Generate(floor);
                var s = new FreeMapSim(L, new Wallet(), true, FoeProfile.Brute, WeatherKind.None);
                int t0 = FreeMapSim.TreasurePointOf(L);
                s.ConfigureTerritory(true, true);
                layers++;
                var terr = new HashSet<int>(s.Territory);
                bool had = TerritoryBypass.HasBypass(L, L.territory), has = TerritoryBypass.HasBypass(L, terr);
                if (had) before++;
                if (has) after++;
                if (s.TerritoryFoeRemoved) blocked++;
                stages[s.TerritoryBypassStage]++;
                if (s.TerritoryAdded > 0) { addedLayers++; addedN.Add(s.TerritoryAdded); }
                if (s.Home != L.home) homeMoved++;
                bool cut = s.TerritoryTrimmed > 0;
                if (cut) { trimmed++; removedN.Add(s.TerritoryTrimmed); sizeBeforeTrimmed.Add(L.territory.Count); sizeAfterTrimmed.Add(terr.Count); }
                var pf = perFloor.TryGetValue(floor, out var v0) ? v0 : (n: 0, before: 0, trimmed: 0);
                perFloor[floor] = (pf.n + 1, pf.before + (had ? 1 : 0), pf.trimmed + (cut ? 1 : 0));
                sizeBefore.Add(L.territory.Count); sizeAfter.Add(terr.Count);
                if (cut) hist[Math.Min(terr.Count, 11)]++;
                if (s.TreasureNode != t0) treasureMoved++;
                if (s.HasFoe && (s.TreasureNode < 0 || !terr.Contains(s.TreasureNode) || s.TreasureNode == s.Home)) treasureLost++;
                if (s.HasFoe && (!terr.Contains(s.Home) || s.FoeNode != s.Home)) homeLost++;
                // 縄張りが道でつながったまとまりのままか
                var seen = new HashSet<int>(); if (terr.Count > 0) seen.Add(s.Home); var q = new Queue<int>(); q.Enqueue(s.Home);
                while (q.Count > 0) { int u = q.Dequeue(); foreach (int w in L.adj[u]) if (terr.Contains(w) && seen.Add(w)) q.Enqueue(w); }
                if (seen.Count != terr.Count) disconnected++;
                if (!has) continue;
                int direct = L.Hops(L.start)[L.goal];
                var dA = HopsAvoiding(L, L.start, terr); int by = dA[L.goal];
                extraAll.Add(by - direct); if (cut) extraTrimmed.Add(by - direct);
                bypassHops.Add(by); directHops.Add(direct);
                if (direct > Cap) overCapDirect++;
                if (by > Cap) { overCap++; if (cut) overCapTrimmed++; }
                foreach (int startProv in new[] { Cap, 4 })
                {
                    bool fd = Refuelable(L, null, startProv, Cap), fb = Refuelable(L, terr, startProv, Cap);
                    int k = startProv == Cap ? 0 : 1;
                    if (fd) feasDirect[k]++;
                    if (fb) feasBypass[k]++;
                    if (fd && !fb) { lostByBypass[k]++; if (cut) lostByBypassTrimmed[k]++; }
                }
            }
        Check(after == layers, $"縄張りを避ける道: 削った後は全部の層にある ({after}/{layers} = {100.0 * after / layers:F1}%・削る前 {100.0 * before / layers:F1}%・追跡者を置かなかった {blocked} = {100.0 * blocked / layers:F2}%)");
        Check(homeLost == 0 && treasureLost == 0 && disconnected == 0,
            $"縄張りを削っても: 追跡者は中心に居て・宝の点は縄張りの中 (中心以外)・道でつながったまとまり (中心の外れ {homeLost} / 宝の外れ {treasureLost} / 割れる {disconnected})");
        Console.WriteLine($"[避ける道] 宝の位置が変わった層 {treasureMoved}/{layers} ({100.0 * treasureMoved / layers:F2}%)・中心を移した層 {homeMoved} ({100.0 * homeMoved / layers:F2}%)・下限 {FreeMapParams.TerritoryTrimMin} 点まで足した層 {addedLayers} ({100.0 * addedLayers / layers:F2}%・足した点 {Dist(addedN)})");
        Console.WriteLine($"[避ける道] 層ごと: " + string.Join(" / ", perFloor.OrderBy(k => k.Key).Select(k => $"深さ{k.Key} 削る前にある {100.0 * k.Value.before / k.Value.n:F1}%・削った {100.0 * k.Value.trimmed / k.Value.n:F1}%")));
        Console.WriteLine($"[避ける道] 削り方: 削らない {stages[0]} / 中心と宝を残す {stages[1]} / 中心だけ残す (宝を決め直す) {stages[2]} / 中心を移す {stages[3]} / 追跡者を置かない {stages[4]}");
        Console.WriteLine($"[避ける道] 削った層 {trimmed}/{layers} ({100.0 * trimmed / layers:F1}%)・外した点の数 {Dist(removedN)}");
        Console.WriteLine($"[避ける道] 縄張りの大きさ 削る前 {Dist(sizeBefore)}");
        Console.WriteLine($"[避ける道] 縄張りの大きさ 削った後 {Dist(sizeAfter)}");
        Console.WriteLine($"[避ける道]   削った層だけ: 前 {Dist(sizeBeforeTrimmed)} → 後 {Dist(sizeAfterTrimmed)}");
        Console.WriteLine($"[避ける道]   削った層の大きさの分布: " + string.Join(" ", Enumerable.Range(0, 12).Select(k => $"{k}点{(k == 0 ? " (追跡者なし)" : "")} {hist[k]}")));
        Console.WriteLine($"[避ける道] 最短の道より何歩長いか (全部の層) {Dist(extraAll)}");
        Console.WriteLine($"[避ける道]   削った層だけ {Dist(extraTrimmed)}");
        Console.WriteLine($"[避ける道] 歩数 最短の道 {Dist(directHops)} / 避ける道 {Dist(bypassHops)}");
        Console.WriteLine($"[避ける道] 物資 (上限 {Cap}): 最短の道が上限を超える層 {100.0 * overCapDirect / layers:F1}% / 避ける道が上限を超える層 {100.0 * overCap / layers:F1}% (うち削った層 {overCapTrimmed})"
                          );
        for (int k = 0; k < 2; k++)
            Console.WriteLine($"[避ける道] 物資で賄えるか (層に入る時 {(k == 0 ? Cap : 4)}・店で上限まで買い足す・HP で払わない): 縄張りを通ってよい道 {100.0 * feasDirect[k] / layers:F1}% / 避ける道 {100.0 * feasBypass[k] / layers:F1}%"
                              + $" / 通ってよいなら賄えるのに避けると賄えない層 {100.0 * lostByBypass[k] / layers:F2}% ({lostByBypass[k]}・うち削った層 {lostByBypassTrimmed[k]})");
    }
}

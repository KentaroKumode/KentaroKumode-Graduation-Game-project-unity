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

    static FreeMapSim Make(int salt, FoeProfile pr, WeatherKind wx, Wallet w = null, int floor = 5)
    {
        GameLoop.GameRng.Salt = (ulong)salt * 104729;
        var L = FreeMapGenerator.Generate(floor);
        return new FreeMapSim(L, w ?? new Wallet(), true, pr, wx);
    }

    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        // --- 生成の不変条件 ---
        {
            int bad = 0; var shortest = new List<int>(); int edgesSum = 0;
            for (int salt = 1; salt <= 120; salt++)
            {
                GameLoop.GameRng.Salt = (ulong)salt * 7919;
                foreach (int floor in new[] { 1, 3, 5, 6, 7 })
                {
                    var L = FreeMapGenerator.Generate(floor);
                    if (!L.IsConnected()) bad++;
                    if (L.adj.Any(a => a.Count < 2)) bad++;
                    if (L.Count != FreeMapParams.NodeCount) bad++;
                    if (L.waypoints.Count < 3) bad++;
                    shortest.Add(L.Hops(L.start)[L.goal]); edgesSum += L.roads.Count;
                }
            }
            Check(bad == 0, $"生成: 異常 {bad} / 平均道 {edgesSum / 600.0:F1} / 終点まで平均 {shortest.Average():F1}手 ({shortest.Min()}-{shortest.Max()})");
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
            Check(m3 == 0 || w3.Provision == p0 - 15, $"砂: 前線の中の1歩は物資1.5倍 (moved={m3}, {p0}->{w3.Provision})");
        }
        // --- 物資不足は HP で払う ---
        {
            var w = new Wallet { Provision = 5, Hp = 30 };
            var s = Make(2, FoeProfile.Summoner, WeatherKind.None, w);
            s.MoveTo(s.Neighbors(s.Me)[0]);
            Check(w.Provision == 0 && w.Hp == 29, $"物資不足は HP で払う (prov {w.Provision}, hp {w.Hp})");
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
        // --- 素朴な BOT の捕まり率 (直行) ---
        foreach (var pr in new[] { FoeProfile.Summoner, FoeProfile.Houndmaster, FoeProfile.Sprinter, FoeProfile.Brute })
        {
            int caught = 0, reached = 0, N = 300;
            for (int salt = 1; salt <= N; salt++)
            {
                var s = Make(salt, pr, WeatherKind.None);
                for (int step = 0; step < 80 && !s.Dead; step++)
                {
                    int next = s.StepToward(s.Me, s.Layout.goal);
                    if (next == s.Me) break;
                    var ev = s.MoveTo(next);
                    if ((ev & (FreeMapEvent.Caught | FreeMapEvent.FamiliarContact)) != 0) { caught++; break; }
                    if (s.Me == s.Layout.goal) { reached++; break; }
                }
            }
            Console.WriteLine($"直行BOT {pr.id,-10}: 捕まる {100.0 * caught / N:F0}% / 到達 {100.0 * reached / N:F0}%");
        }
        Console.WriteLine(fails == 0 ? "ALL OK" : $"FAILED {fails}");
    }
}

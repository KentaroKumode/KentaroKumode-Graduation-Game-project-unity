using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MapSystem;
using MapSystem.FreeMove;
using TR = TurnRef.FreeMove;

/// <summary>storm-boss (手番で進む嵐・名前空間 TurnRef.FreeMove・run.py が git から取って一緒にコンパイルする) 用の財布。</summary>
class TurnWallet : TR.IFreeMapWallet
{
    public int Provision { get; set; } = 100000;
    public int Hp = 1000;
    public void SpendProvision(int a) { Provision -= a; }
    public bool PayHp(int a) { Hp -= a; return Hp <= 0; }
}

/// <summary>不安定化の時計 (2026-11-13 二版・GAME.md §5-12) の検算。
/// (1) 測定の口 stormClock = Turns が storm-boss と 1 手も変わらない (生成・操作列・写し・財布・乱数の引く回数)。
/// (2) ポイントの溜まり方の表・境目の輪・崩れ始める前は歩いても溜まらない・深さ 6 は崩れ始める境目から・遠ざけるで減る・ボス戦と深さ 0/7 は溜まらない・中断セーブの往復・点も道も消えない・終点へ着ける。
/// (3) 歩き方 A/B/C を手番の時計 (前の版) と不安定化の時計 (戦闘 1 回のエスカレーション 0/1/2 段) で比べる。</summary>
static partial class T
{
    // =====================================================================
    //  名前で突き合わせる (型は名前空間ごとに別なので、 b の欄の名前で a を読む)
    // =====================================================================

    static string Deep(object a, object b, string path, int depth = 0)
    {
        if (a == null || b == null) return (a == null) == (b == null) ? null : path;
        if (depth > 8) return null;
        var ta = a.GetType();
        if (ta.IsEnum || b.GetType().IsEnum) return Convert.ToInt64(a) == Convert.ToInt64(b) ? null : path;
        if (a is string || ta.IsPrimitive || a is decimal) return Equals(a, b) ? null : path;
        if (a is System.Collections.IEnumerable ea && b is System.Collections.IEnumerable eb)
        {
            var la = ea.Cast<object>().ToList(); var lb = eb.Cast<object>().ToList();
            if (la.Count != lb.Count) return path + "#count";
            for (int i = 0; i < la.Count; i++) { var d = Deep(la[i], lb[i], path + "[" + i + "]", depth + 1); if (d != null) return d; }
            return null;
        }
        foreach (var fb in b.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var fa = ta.GetField(fb.Name, BindingFlags.Public | BindingFlags.Instance);
            if (fa == null)
            {
                // 〈灰に埋もれた基地〉の前の版にだけある欄 (riftGate = ボスの隣の点) は、 空なら同じとみなす (深さ 6 はイベントなしの測定の形で比べる・TurnGen)
                if (fb.Name == "riftGate" && fb.GetValue(b) is System.Collections.ICollection rc && rc.Count == 0) continue;
                return path + "." + fb.Name + "(無い)";
            }
            var d = Deep(fa.GetValue(a), fb.GetValue(b), path + "." + fb.Name, depth + 1);
            if (d != null) return d;
        }
        return null;
    }

    /// <summary>手番の時計の中身 (今の FreeMove) と storm-boss の中身へ同じ操作列を流し、 1 手ごとに起きたこと・写し・財布を比べる。
    /// 今の側には不安定化の口 (AddInstability / PassEventTurns) も混ぜて叩く ── 手番の時計では何もしない (PassEventTurns は PassTurnsExternal と同じ) はず。</summary>
    /// <summary>storm-boss の生成器で、 今の生成器の <paramref name="fixedPt"/> (深さ 6 の固定のイベントの点 = イベントなしの測定) と同じ形を作る。
    /// storm-boss は「ボスの隣を全部イベントに」の作り (TURNREF_RING) なので、 口 (riftAtBoss) を裏返して渡す。 深さ 6 はイベントなしの測定の形で比べる
    /// (ふだんの形はボスの隣の点の種別だけが違う ── StormCheck の LayoutDiffAsh6 が main と比べて見る)。</summary>
    static TR.FreeMapLayout TurnGen(int floor, bool fixedPt, bool storm)
    {
#if TURNREF_RING
        return TR.FreeMapGenerator.Generate(floor, !fixedPt, storm);
#else
        return TR.FreeMapGenerator.Generate(floor, fixedPt, storm);
#endif
    }

    static int ReplayTurns(int salt, int floor, int steps, out string first)
    {
        first = null;
        bool fp = floor == AshBase.Depth;
        GameLoop.GameRng.Salt = (ulong)salt * 7919;
        var La = FreeMapGenerator.Generate(floor, fp, true);
        GameLoop.GameRng.Salt = (ulong)salt * 7919;
        var Lb = TurnGen(floor, fp, true);
        bool foe = FloorPlan.HasWanderer(floor);
        var wa = new Wallet { Provision = 8, Hp = 60 }; var wb = new TurnWallet { Provision = 8, Hp = 60 };
        var a = new FreeMapSim(La, wa, foe, null, null, null);
        var b = new TR.FreeMapSim(Lb, wb, foe, null, null, null);
        a.ConfigureTerritory(true, true, true); b.ConfigureTerritory(true, true, true);
        a.ConfigureStorm(StormClock.Turns);
        for (int k = 0; k < steps; k++)
        {
            if (a.Dead || b.Dead) { if (a.Dead != b.Dead) { first = $"dead@{k}"; return 1; } break; }
            int h = H(salt, floor, k), op = h % 100;
            int ea, eb;
            if ((h & 7) == 3) a.AddInstability((InstabilitySource)((h >> 3) % 10), 1 + (h >> 8) % 4);   // 手番の時計では何も起きない
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
            else if (op < 80) { int n = ((h / 100) % 9) - 4; ea = (int)a.ShiftFront(n); eb = (int)b.ShiftFront(n); }
            else if (op < 83) { a.GrantFreeMoves(1); b.GrantFreeMoves(1); ea = eb = 0; }
            else if (op < 86) { ea = (int)a.PushFoe(2); eb = (int)b.PushFoe(2); }
            else if (op < 89) { ea = (int)a.PullFoe(2); eb = (int)b.PullFoe(2); }
            else if (op < 91) { ea = (int)a.ShakeOff(); eb = (int)b.ShakeOff(); }
            else if (op < 93) { ea = (int)(a.InstabilityMode ? a.PassEventTurns(2) : a.PassTurnsExternal(2)); eb = (int)b.PassTurnsExternal(2); }
            else if (op < 95) { ea = a.TakeTreasure() ? 1 : 0; eb = b.TakeTreasure() ? 1 : 0; }
            else if (op < 97)
            {
                int ja = a.Neighbors(a.Me)[0], jb = b.Neighbors(b.Me)[0];
                ea = a.SupplyFor(ja) * 10 + a.TurnsFor(ja) + 100 * a.HpLossFor(ja) + 1000 * a.StormHpFor(ja) + 10000 * (a.InStorm(ja) ? 1 : 0);
                eb = b.SupplyFor(jb) * 10 + b.TurnsFor(jb) + 100 * b.HpLossFor(jb) + 1000 * b.StormHpFor(jb) + 10000 * (b.InStorm(jb) ? 1 : 0);
            }
            else { ea = a.RevealAround(2, true); eb = b.RevealAround(2, true); }
            string d = ea != eb ? "event" : wa.Provision != wb.Provision || wa.Hp != wb.Hp ? "wallet"
                     : a.StormTime != b.StormTime || a.StormPhase != b.StormPhase || a.StormTurnsToNext != b.StormTurnsToNext ? "stormclock"
                     : a.Instability != 0 ? "instability"
                     : Deep(a.CaptureState(), b.CaptureState(), "state");
            if (d == null)
            {
                bool ra = a.StormRing(out var ca, out double rra), rb = b.StormRing(out var cb, out double rrb);
                if (ra != rb || (ra && (ca.x != cb.x || ca.y != cb.y || rra != rrb))) d = "ring";
            }
            if (d != null) { first = $"{d}@{k} op={op}"; return 1; }
        }
        return 0;
    }

    /// <summary>不安定化の時計の中身。</summary>
    static FreeMapSim MakeInst(int salt, int floor, Wallet w = null, WeatherKind? wx = null) => MakeStorm(salt, floor, w, wx, StormClock.Instability);

    static void InstabilityReport()
    {
        // ── 1. 手番の時計は storm-boss と 1 点も変わらない: 嵐の層の生成・乱数の引く回数 ──
        {
            int layers = 0, diff = 0, drawDiff = 0; string firstDiff = null;
            foreach (int floor in new[] { 0, 1, 3, 5, 6, 7 })
                for (int salt = 1; salt <= N(3000, 1000); salt++)
                {
                    GameLoop.GameRng.Salt = (ulong)salt * 7919; GameLoop.GameRng.Draws.Clear();
                    bool fp = floor == AshBase.Depth;   // 深さ 6 はイベントなしの測定の形 (TurnGen)
                    var La = FreeMapGenerator.Generate(floor, fp, true); var da = SnapDraws();
                    GameLoop.GameRng.Draws.Clear();
                    var Lb = TurnGen(floor, fp, true); var db = SnapDraws();
                    layers++;
                    string d = Deep(La, Lb, "layout");
                    if (d != null) { diff++; firstDiff ??= $"深さ{floor} 種{salt} {d}"; }
                    if (!SameDraws(da, db)) drawDiff++;
                }
            Check(diff == 0 && drawDiff == 0, $"嵐の時計 Turns: 嵐の層の生成は storm-boss と 1 点も変わらない ({layers} 層・形の違い {diff}{(firstDiff != null ? " 最初 " + firstDiff : "")}・乱数の引く回数の違い {drawDiff})");
        }
        // ── 2. 手番の時計の中身は storm-boss と同じ動き (同じ操作列・1 手ごと) ──
        {
            int runs = 0, bad = 0, drawBad = 0; string first = null;
            foreach (int floor in StormDepths)
                for (int salt = 1; salt <= N(1000, 400); salt++)
                {
                    bad += ReplayTurns(salt, floor, 200, out string f);
                    if (f != null) first ??= $"深さ{floor} 種{salt} {f}";
                    runs++;
                }
            for (int salt = 1; salt <= 200; salt++)
                foreach (int floor in StormDepths)
                {
                    GameLoop.GameRng.Salt = (ulong)salt * 7919; GameLoop.GameRng.Draws.Clear();
                    var a = new FreeMapSim(FreeMapGenerator.Generate(floor, floor == AshBase.Depth, true), new Wallet(), FloorPlan.HasWanderer(floor), null, null, null);
                    a.ConfigureStorm(StormClock.Turns);
                    for (int k = 0; k < 60 && !a.InFight; k++) { var nb = a.Neighbors(a.Me); a.MoveTo(nb[H(salt, floor, k) % nb.Count]); }
                    var da = SnapDraws();
                    GameLoop.GameRng.Salt = (ulong)salt * 7919; GameLoop.GameRng.Draws.Clear();
                    var b = new TR.FreeMapSim(TurnGen(floor, floor == AshBase.Depth, true), new TurnWallet(), FloorPlan.HasWanderer(floor), null, null, null);
                    for (int k = 0; k < 60 && !b.InFight; k++) { var nb = b.Neighbors(b.Me); b.MoveTo(nb[H(salt, floor, k) % nb.Count]); }
                    if (!SameDraws(da, SnapDraws())) drawBad++;
                }
            Check(bad == 0 && drawBad == 0, $"嵐の時計 Turns: 航行の中身は storm-boss と同じ動き ({runs} 層 × 200 手・違い {bad}{(first != null ? " 最初 " + first : "")}・引く回数の違い {drawBad}/800)");
        }

        // ── 3. 溜まり方の表 (1 か所の関数) ──
        {
            var want = new Dictionary<InstabilitySource, int>
            {
                { InstabilitySource.Treasure, 3 }, { InstabilitySource.Relic, 3 }, { InstabilitySource.Elite, 2 }, { InstabilitySource.Pursuer, 2 },
                { InstabilitySource.Battle, 1 }, { InstabilitySource.EventGain, 1 }, { InstabilitySource.Escalation, 1 },
                { InstabilitySource.EventTurns, 1 }, { InstabilitySource.Walk, 1 }, { InstabilitySource.Shift, 1 },
            };
            bool ok = want.All(kv => Instability.PointsOf(kv.Key) == kv.Value)
                   && Instability.PointsOf(InstabilitySource.EventTurns, 4) == 4 && Instability.PointsOf(InstabilitySource.Shift, -3) == -3 && Instability.PointsOf(InstabilitySource.Walk, 2) == 2
                   && Instability.PointsOf(InstabilitySource.Relic, 2) == 6
                   && Instability.EventPoints(1, true, false) == 3 && Instability.EventPoints(2, false, false) == 6
                   && Instability.EventPoints(0, true, false) == 1 && Instability.EventPoints(0, true, true) == 0 && Instability.EventPoints(0, false, false) == 0
                   && Instability.EscalationPoints(2, false) == 2 && Instability.EscalationPoints(3, true) == 0 && Instability.EscalationPoints(0, false) == 0
                   && Instability.ArrivalPoints(TileType.Treasure, out bool m0) == 3 && !m0
                   && Instability.ArrivalPoints(TileType.EliteBattle, out bool m1) == 2 && m1
                   && Instability.ArrivalPoints(TileType.Battle, out bool m2) == 1 && m2
                   && Instability.ArrivalPoints(TileType.Event, out bool m3) == 0 && m3
                   && Instability.ArrivalPoints(TileType.Shop, out bool m4) == 0 && !m4 && Instability.ArrivalPoints(TileType.Rest, out _) == 0
                   && Instability.ArrivalPoints(TileType.Exchange, out _) == 0 && Instability.ArrivalPoints(TileType.Boss, out _) == 0;
            // イベントの「何かを得た +1」は持ち物が増えた時だけ (2026-11-13 三版)
            var counted = new[] { EventGainKind.Gold, EventGainKind.Consumable, EventGainKind.Material, EventGainKind.Provision, EventGainKind.Item, EventGainKind.Flag };
            bool gainOk = ((EventGainKind[])Enum.GetValues(typeof(EventGainKind))).All(g => Instability.CountsAsEventGain(g) == counted.Contains(g));
            Check(ok && gainOk, "不安定化: 表のとおり (宝・秘宝 +3 / エリート・番人 +2 / 通常の戦闘・イベントの得 +1 (持ち物が増えた時だけ = ゴールド・消耗品・素材・物資・品・フラグ。 HP・最大 HP・時限バフ・航行・値引き・休憩の上乗せ・観測所の写しは 0 / 秘宝を得たイベントは +3 だけ・+1 は重ねない) / 激化 +1 段ごと・ボス戦 0 / イベントのターン +N / 歩く +手番 / 嵐を動かす ±N / 店・休憩・交換・ボスの点 0)");
        }

        // ── 4. 境目と輪 (累計 → 段 → 輪・段の途中は線形) ──
        {
            const int P1 = FreeMapParams.InstabilityPhase1At, P2 = FreeMapParams.InstabilityPhase2At, P3 = FreeMapParams.InstabilityPhase3At, P4 = FreeMapParams.InstabilityPhase4At;
            bool ph = P1 == 15 && P2 == 30 && P3 == 45 && P4 == 50   // 2026-11-13 の 2 度目の置き値 (変えたらここも直す)
                   && new[] { (0, 0), (P1 - 1, 0), (P1, 1), (P2 - 1, 1), (P2, 2), (P3 - 1, 2), (P3, 3), (P4 - 1, 3), (P4, 4), (P4 + 25, 4) }.All(p => Instability.PhaseOf(p.Item1) == p.Item2);
            int layers = 0, bad = 0; string first = null;
            foreach (int floor in StormDepths)
                for (int salt = 1; salt <= 1000; salt++)
                {
                    var s = MakeInst(salt, floor); var L = s.Layout; layers++;
                    double r0 = Storm.FullRadius(L);
                    var K = Instability.Schedule;
                    // 段の長さは偶数でなくてよいので、 「途中」は実数の補間で見る (段の真ん中は (始まり + 終わり) / 2 の累計で、 長さが奇数なら半分の位置の値)
                    Storm.RingAt(L, Instability.TimeOf(P1), K, out var c10, out double r10);
                    Storm.RingAt(L, Instability.TimeOf(P1 + (P2 - P1) / 2), K, out var c15, out double r15);
                    Storm.RingAt(L, Instability.TimeOf(P2), K, out var c20, out double r20);
                    Storm.RingAt(L, Instability.TimeOf(P2 + (P3 - P2) / 2), K, out var c25, out double r25);
                    Storm.RingAt(L, Instability.TimeOf(P3), K, out var c30, out double r30);
                    double f1 = (double)((P2 - P1) / 2) / (P2 - P1), f2 = (double)((P3 - P2) / 2) / (P3 - P2);
                    string e = null;
                    if (Math.Abs(r10 - r0) > 1e-9 || Vec2.Dist(c10, Storm.MapCenter) > 1e-9) e = P1 + "";
                    else if (Math.Abs(r15 - (r0 + (r0 * FreeMapParams.StormPhase1EndRadius - r0) * f1)) > 1e-9) e = "フェーズ 1 の途中 (線形)";
                    else if (Math.Abs(r20 - r0 * FreeMapParams.StormPhase1EndRadius) > 1e-9 || Vec2.Dist(c20, Storm.MapCenter) > 1e-9) e = P2 + "";
                    else if (Vec2.Dist(c25, Vec2.Lerp(Storm.MapCenter, Storm.Phase2Center(L), f2)) > 1e-9) e = "フェーズ 2 の途中 (中心が寄る)";
                    else if (Math.Abs(r30 - r0 * FreeMapParams.StormPhase2EndRadius) > 1e-9 || Vec2.Dist(c30, Storm.Phase2Center(L)) > 1e-9) e = P3 + "";
                    else if (Enumerable.Range(0, L.Count).Any(i => Storm.InStorm(L, i, Instability.TimeOf(P1 - 1), K))) e = (P1 - 1) + " で嵐";
                    else if (Enumerable.Range(0, L.Count).Any(i => Storm.InStorm(L, i, Instability.TimeOf(P4), K) != (i != L.goal))) e = P4 + " (フェーズ 4)";
                    else if (Enumerable.Range(0, P4 + 25).Any(p => Storm.InStorm(L, L.goal, Instability.TimeOf(p), K))) e = "ボスの点が嵐の中";
                    // 中身の読み取りが同じ関数を通る (累計を足して段・輪・嵐の中が揃う)
                    if (e == null)
                    {
                        s.AddInstability(InstabilitySource.Treasure, 9);   // 27 (深さ 6 は 42)
                        int p = s.Instability, t = Instability.TimeOf(p);
                        if (s.StormPhase != Instability.PhaseOf(p) || s.StormTime != t || s.StormTurnsToNext != Storm.TurnsToNext(t, K)
                            || Enumerable.Range(0, L.Count).Any(i => s.InStorm(i) != Storm.InStorm(L, i, t, K))) e = "中身";
                    }
                    if (e != null) { bad++; first ??= $"深さ{floor} 種{salt} {e}"; }
                }
            Check(ph && bad == 0, $"不安定化: 境目 0〜{P1 - 1} 静か / {P1} フェーズ 1 (全部を含む輪・地図の中心) / {P2} フェーズ 2 (60%) / {P3} フェーズ 3 (30%・中心はボスをずらした点) / {P4} フェーズ 4 (ボスの点のほか全部)・段の途中は線形・ボスの点はいつも外 ({layers} 層・違反 {bad}{(first != null ? " 最初 " + first : "")})");
        }

        // ── 5. 崩れ始める前は歩いても溜まらない / 後は 1 歩・待つ・偵察で溜まる (雪の嵐の中へは +2・近道と〈補給線〉は 0) / 深さ 6 は崩れ始める境目から / 深さ 0・7 は溜まらない ──
        {
            bool quiet = true, after = true, snow = true, free = true, d6 = true, d07 = true, ext = true; int layers = 0;
            foreach (int floor in new[] { 1, 3, 5 })
                for (int salt = 1; salt <= 300; salt++)
                {
                    var w = new Wallet { Provision = 100000, Hp = 100000 };
                    var s = MakeInst(salt, floor, w); layers++;
                    quiet &= s.Instability == 0 && s.StormPhase == 0 && !s.Collapsing;
                    for (int k = 0; k < 40 && !s.Dead; k++)
                    {
                        if (s.InFight) { if (s.Contact == FreeMapSim.ContactKind.Body) s.ResolveBodyWon(); else s.ResolveFamiliarFight(false); continue; }
                        int op = H(salt, floor, k) % 4;
                        if (op == 0) s.Wait(); else if (op == 1) s.Recon(); else { var nb = s.Neighbors(s.Me); s.MoveTo(nb[H(salt, k, floor) % nb.Count]); }
                    }
                    quiet &= s.Instability == 0 && s.InstabilityParts.All(x => x == 0);
                    ext &= s.PassTurnsExternal(2) >= 0 && s.Instability == 0;                                // 休憩の「多めに回復」は 0
                    if (s.InFight) continue;
                    s.AddInstability(InstabilitySource.Treasure, 5);                                          // 15 (崩れ始めた)
                    int p0 = s.Instability;
                    var cand = s.Neighbors(s.Me).Where(v => v != s.Layout.goal).ToList();
                    if (cand.Count == 0) continue;
                    int wantStep = s.InstabilityForStep(cand[0]);                                            // 1 (朱の雪の嵐の中へは 2)
                    after &= wantStep == s.TurnsFor(cand[0]) && wantStep >= 1;
                    s.MoveTo(cand[0]); if (s.InFight) continue;
                    after &= s.Instability == p0 + wantStep; p0 += wantStep - 1;
                    s.Wait(); if (s.InFight) continue; after &= s.Instability == p0 + 2;
                    s.Recon(); if (s.InFight) continue; after &= s.Instability == p0 + 3;
                    s.GrantFreeMoves(1); int p1 = s.Instability;
                    s.MoveTo(s.Neighbors(s.Me).First(v => v != s.Layout.goal)); if (s.InFight) continue;
                    free &= s.Instability == p1;                                                               // 近道の 1 歩は 0
                    s.AddRelayPoint(s.Layout.start); int p2 = s.Instability;
                    if (s.CanRelayTo(s.Layout.start)) { s.RelayTo(s.Layout.start); free &= s.Instability == p2; }   // 〈補給線〉は 0
                    int t0 = s.Turn; int p3 = s.Instability;
                    s.PassEventTurns(3); ext &= s.Instability == p3 + 3 && s.Turn == t0 + 3 || s.InFight;         // イベントの「ターン+N」は +N
                }
            // 雪の嵐の中への 1 歩は +2 (手番の数だけ)
            for (int salt = 1; salt <= 60; salt++)
            {
                var w = new Wallet { Provision = 100000, Hp = 100000 };
                var s = MakeInst(salt, 5, w, WeatherKind.Snow);
                s.AddInstability(InstabilitySource.Treasure, 17);                                             // 51 (フェーズ 4)
                int j = s.Neighbors(s.Me).First(v => v != s.Layout.goal);
                int p = s.Instability; bool inS = s.InStorm(j);
                s.MoveTo(j);
                if (!s.InFight) snow &= inS && s.Instability == p + FreeMapParams.StormSnowTurns;
            }
            // 深さ 6 は崩れ始める境目 (15) から (入った時からフェーズ 1)
            for (int salt = 1; salt <= 100; salt++)
            {
                var s = MakeInst(salt, 6);
                d6 &= FreeMapParams.InstabilityDepth6Start == FreeMapParams.InstabilityPhase1At && s.Instability == FreeMapParams.InstabilityDepth6Start && s.StormPhase == 1 && s.Collapsing && s.InstabilityParts.All(x => x == 0);
                var nb = s.Neighbors(s.Me); s.MoveTo(nb[0]);
                if (!s.InFight) d6 &= s.Instability == FreeMapParams.InstabilityDepth6Start + 1;
            }
            // 深さ 0・7 は嵐が無い: 何を足しても 0
            foreach (int floor in new[] { 0, 7 })
            {
                GameLoop.GameRng.Salt = 77;
                var s = new FreeMapSim(FreeMapGenerator.Generate(floor, false, true), new Wallet(), false, null, null, null);
                s.ConfigureStorm(StormClock.Instability);
                foreach (InstabilitySource src in Enum.GetValues(typeof(InstabilitySource))) s.AddInstability(src, 5);
                for (int k = 0; k < 20; k++) s.Wait();
                s.PassEventTurns(3); s.ShiftFront(4);
                d07 &= !s.StormMode && !s.InstabilityMode && s.Instability == 0 && s.StormPhase == 0;
            }
            Check(quiet && after && snow && free && d6 && d07 && ext,
                $"不安定化: 崩れ始める前は歩く・待つ・偵察で溜まらない ({layers} 層)・後は 1 歩・待つ・偵察で +1 (雪の嵐の中へは +2)・近道と〈補給線〉と休憩の手番は 0・イベントのターン+3 は +3・深さ 6 は {FreeMapParams.InstabilityDepth6Start} から・深さ 0/7 は溜まらない ({quiet}/{after}/{snow}/{free}/{ext}/{d6}/{d07})");
        }

        // ── 6. 遠ざける / 近づける・段の知らせ・フェーズ 3 でボスが見える ──
        {
            var s = MakeInst(21, 3);
            s.AddInstability(InstabilitySource.Treasure, 5);              // 15
            var e1 = s.ShiftFront(-3); bool a = s.Instability == 12 && s.StormPhase == 0 && (e1 & FreeMapEvent.StormAdvanced) != 0 && s.Turn == 0;
            s.ShiftFront(-20); bool b = s.Instability == 0;              // 0 未満にしない
            var e2 = s.ShiftFront(+45); bool c = s.Instability == 45 && s.StormPhase == 3 && (e2 & FreeMapEvent.StormAdvanced) != 0 && s.Known[s.Layout.goal];
            bool d = s.InstabilityParts[Instability.PartShift] == -15 + 45 && s.InstabilityParts[Instability.PartTake] == 15;
            var s2 = MakeInst(22, 5); bool hidden = !s2.Known[s2.Layout.goal];
            s2.AddInstability(InstabilitySource.Treasure, 11); bool hid2 = s2.StormPhase == 2 && (!s2.Known[s2.Layout.goal] || s2.HopsFrom(s2.Me)[s2.Layout.goal] <= 1);
            s2.AddInstability(InstabilitySource.Battle, 12); bool seen = s2.StormPhase == 3 && s2.Known[s2.Layout.goal];
            Check(a && b && c && d && hidden && hid2 && seen, $"不安定化: 遠ざける+N は −N (0 未満にしない・境目を下回れば静かへ戻る)・近づける+N は +N・段が変わると知らせる・手番は進まない・フェーズ 3 でボスの点が見える ({a}/{b}/{c}/{d}/{hidden}/{hid2}/{seen})");
        }

        // ── 7. 中断セーブの往復 (累計・内訳・輪) と古いセーブ / 点も道も消えない / 終点へ着ける ──
        {
            int layers = 0, saveBad = 0, oldBad = 0, lost = 0, notReached = 0;
            foreach (int floor in StormDepths)
                for (int salt = 1; salt <= 300; salt++)
                {
                    var w = new Wallet { Provision = 3, Hp = 100000 };
                    var s = MakeInst(salt, floor, w); layers++;
                    var roads0 = s.Roads().ToList();
                    for (int k = 0; k < 50; k++)
                    {
                        if (s.InFight) { if (s.Contact == FreeMapSim.ContactKind.Body) s.ResolveBodyWon(); else s.ResolveFamiliarFight(false); continue; }
                        int h = H(salt, floor, k);
                        if (h % 5 == 0) s.AddInstability((InstabilitySource)(h / 5 % 10), 1 + h / 50 % 3);
                        else if (h % 5 == 1) s.Wait();
                        else { var nb = s.Neighbors(s.Me); s.MoveTo(nb[h / 7 % nb.Count]); }
                    }
                    if (!s.Roads().SequenceEqual(roads0) || Enumerable.Range(0, s.Layout.Count).Any(i => s.IsSunk(i))) lost++;
                    var saved = s.CaptureState();
                    var r = new FreeMapSim(s.Layout, new Wallet(), FloorPlan.HasWanderer(floor), null, null, null);
                    r.ConfigureTerritory(true, true, true); r.ConfigureStorm(StormClock.Instability); r.LoadState(saved, null);
                    if (r.Instability != s.Instability || !r.InstabilityParts.SequenceEqual(s.InstabilityParts) || r.StormPhase != s.StormPhase || r.StormTime != s.StormTime
                        || Enumerable.Range(0, s.Layout.Count).Any(i => r.InStorm(i) != s.InStorm(i) || r.Known[i] != s.Known[i])) saveBad++;
                    { bool ra = r.StormRing(out var ca, out double rra), rb = s.StormRing(out var cb, out double rrb); if (ra != rb || (ra && (ca.x != cb.x || ca.y != cb.y || rra != rrb))) saveBad++; }
                    // 古いセーブ (累計の欄が無い): 層の始まりの累計から数え直す
                    saved.instability = 0; saved.instabilityParts = new int[0];
                    var o = new FreeMapSim(s.Layout, new Wallet(), FloorPlan.HasWanderer(floor), null, null, null);
                    o.ConfigureTerritory(true, true, true); o.ConfigureStorm(StormClock.Instability); o.LoadState(saved, null);
                    if (o.Instability != Instability.StartOf(floor)) oldBad++;
                    // 終点へ歩く (フェーズ 4 で嵐の HP を払って)
                    s.AddInstability(InstabilitySource.Treasure, 20);
                    for (int k = 0; k < 200 && !s.Dead && s.Me != s.Layout.goal; k++)
                    {
                        if (s.InFight) { if (s.Contact == FreeMapSim.ContactKind.Body) s.ResolveBodyWon(); else s.ResolveFamiliarFight(false); continue; }
                        s.MoveTo(s.StepToward(s.Me, s.Layout.goal));
                    }
                    if (s.Me != s.Layout.goal) notReached++;
                }
            Check(saveBad == 0 && oldBad == 0 && lost == 0 && notReached == 0, $"不安定化: 中断セーブの往復で累計・内訳・輪が同じ・古いセーブは層の始まりから・点も道も消えない・終点へ必ず着ける ({layers} 層・往復の違い {saveBad}・古いセーブ {oldBad}・消えた {lost}・着けない {notReached})");
        }

        // 歩き方の比べ (WalkCompareInstability) は run.py --walk に分けた (2026-11-13・10 分前後かかる)
    }

    // =====================================================================
    //  歩き方の比べ (2026-11-13 二版): 手番の時計 (前の版) と、 不安定化の時計 (戦闘 1 回のエスカレーション k = 0/1/2 段)
    //    A = 崩れ始めるまでは近い未踏の点を踏み、 崩れ始めたら (手番の時計なら嵐が来たら) 終点へ
    //    B = 輪の中で粘る: 嵐の外の未踏の点を近い順に。 行って、 そこから終点まで嵐の中を歩かずに着ける点だけ (歩く間と途中で踏む点で溜まる分を見込む)
    //    C = 嵐を踏んででも全部踏む
    //  着いた点で溜まる分 (歩く人が知っている種別で数える・仮定): 宝 +3 / エリート +2+k / 戦闘 +1+k / イベント +1 (何かを得たと仮定・秘宝は数えない) /
    //  縄張りの宝 +3 / 番人 +2+k / 使い魔 +1+k / 店・休憩・交換 0。 戦闘の報酬の秘宝 (通常 15%・エリート 40%) は数えない (下限)。 戦闘は全部勝ち・物資は尽きない。
    // =====================================================================

    sealed class IWalk
    {
        public bool reached; public int visited, visitedBeforeCollapse = -1, stormSteps, stormHp, turns;
        public int[] kinds = new int[WalkKinds.Length];
        public int[] parts = new int[Instability.Parts];
    }

    /// <summary>着いた点で溜まる分の見込み (歩く人の仮定・上の表)。</summary>
    static int ArrivalGuess(FreeMapSim s, int i, int k)
    {
        if (i == s.Layout.goal) return 0;
        int p = 0;
        switch (s.Layout.types[i])
        {
            case TileType.Treasure: p += 3; break;
            case TileType.EliteBattle: p += 2 + k; break;
            case TileType.Battle: p += 1 + k; break;
            case TileType.Event: p += 1; break;
        }
        if (s.TreasureAvailable && i == s.TreasureNode) p += 3;
        return p;
    }

    /// <summary>着いた点を起動した時の不安定化 (手番の時計では何も起きない)。</summary>
    static void Arrive(FreeMapSim s, int i, int k)
    {
        if (i == s.Layout.goal) return;
        if (s.Me == i && s.TakeTreasure()) s.AddInstability(InstabilitySource.Treasure);
        switch (s.Layout.types[i])
        {
            case TileType.Treasure: s.AddInstability(InstabilitySource.Treasure); break;
            case TileType.EliteBattle: s.AddInstability(InstabilitySource.Elite); s.AddInstability(InstabilitySource.Escalation, k); break;
            case TileType.Battle: s.AddInstability(InstabilitySource.Battle); s.AddInstability(InstabilitySource.Escalation, k); break;
            case TileType.Event: s.AddInstability(InstabilitySource.EventGain); break;
        }
    }

    /// <summary>不安定化の時計で、 from から d に沿って歩いた時に嵐の中の点へ 1 度も入らないか。 歩く間 (崩れ始めた後の +1) と途中で初めて踏む点の分を見込む。 着いた時の累計を返す。</summary>
    static bool SafeRouteInst(FreeMapSim s, int from, int[] d, int pts, HashSet<int> seen, int k, out int ptsEnd)
    {
        var L = s.Layout; int cur = from; ptsEnd = pts; var K = Instability.Schedule;
        var passed = new HashSet<int>();
        for (int g = 0; g < 200 && d[cur] > 0; g++)
        {
            int nx = NextStep(s, cur, d);
            if (nx == cur) return false;
            if (Storm.InStorm(L, nx, Instability.TimeOf(ptsEnd), K)) return false;
            if (Instability.Collapsing(ptsEnd)) ptsEnd += Instability.PointsOf(InstabilitySource.Walk);
            if (!seen.Contains(nx) && passed.Add(nx)) ptsEnd += ArrivalGuess(s, nx, k);
            cur = nx;
        }
        return d[cur] == 0;
    }

    static IWalk WalkI(FreeMapSim s, Wallet w, char mode, int k)
    {
        var L = s.Layout; var r = new IWalk(); var seen = new HashSet<int> { s.Me };
        var dGoal = HopsTo(s, L.goal);
        bool inst = s.InstabilityMode;
        for (int guard = 0; guard < 900 && !s.Dead; guard++)
        {
            if (r.visitedBeforeCollapse < 0 && s.StormPhase >= 1) r.visitedBeforeCollapse = r.visited;
            if (s.InFight)
            {
                bool body = s.Contact == FreeMapSim.ContactKind.Body;
                if (body) s.ResolveBodyWon(); else s.ResolveFamiliarFight(false);
                s.AddInstability(body ? InstabilitySource.Pursuer : InstabilitySource.Battle); s.AddInstability(InstabilitySource.Escalation, k);
                int pa = s.TakePendingArrival();
                if (pa >= 0 && pa != L.goal) { s.MarkDone(pa); if (seen.Add(pa)) { CountVisit(r, L, pa); Arrive(s, pa, k); } }
                continue;
            }
            if (s.Me == L.goal) break;
            int target = L.goal; int[] dT = dGoal;
            bool explore = mode == 'C' || (mode == 'A' && s.StormPhase == 0) || mode == 'B';
            if (explore)
            {
                var dMe = HopsTo(s, s.Me);
                foreach (int i in Enumerable.Range(0, L.Count).Where(i => i != L.goal && !seen.Contains(i) && dMe[i] != int.MaxValue).OrderBy(i => dMe[i]).ThenBy(i => i))
                {
                    var di = HopsTo(s, i);
                    if (mode == 'B' && s.StormMode)
                    {
                        if (s.InStorm(i)) continue;
                        if (inst)
                        {
                            if (!SafeRouteInst(s, s.Me, di, s.Instability, seen, k, out int pA)) continue;
                            var seen2 = new HashSet<int>(seen) { i };
                            if (!SafeRouteInst(s, i, HopsTo(s, L.goal), pA, seen2, k, out _)) continue;
                        }
                        else
                        {
                            if (!SafeRoute(s, s.Me, di, s.StormTime, out int tArr)) continue;
                            if (!SafeRoute(s, i, HopsTo(s, L.goal), tArr, out _)) continue;
                        }
                    }
                    target = i; dT = di; break;
                }
            }
            if (target == L.goal) dT = HopsTo(s, L.goal);
            int nx = NextStep(s, s.Me, dT);
            if (nx == s.Me) break;
            bool inStorm = s.InStorm(nx); int before = w.Hp;
            var ev = s.MoveTo(nx);
            if (inStorm) { r.stormSteps++; r.stormHp += before - w.Hp; }
            if ((ev & (FreeMapEvent.Caught | FreeMapEvent.FamiliarContact)) != 0) continue;   // 着いた点の起動は戦闘の後
            if (nx != L.goal) { s.MarkDone(nx); if (seen.Add(nx)) { CountVisit(r, L, nx); Arrive(s, nx, k); } }
        }
        if (r.visitedBeforeCollapse < 0) r.visitedBeforeCollapse = r.visited;   // 崩れ始めずに抜けた層は、 抜けるまでに踏んだ点の数
        r.reached = s.Me == L.goal; r.turns = s.Turn;
        Array.Copy(s.InstabilityParts, r.parts, r.parts.Length);
        return r;
    }

    static void CountVisit(IWalk r, FreeMapLayout L, int i)
    {
        r.visited++;
        int kk = Array.IndexOf(WalkKinds, L.types[i]);
        if (kk >= 0) r.kinds[kk]++;
    }

    static string Avg(List<int> xs) => $"平均 {xs.Average():F1}・中央値 {Q(xs, 0.5)}・90% 点 {Q(xs, 0.9)}";

    static void WalkCompareInstability()
    {
        int[] maxHps = { 30, 85 };
        foreach (int floor in StormDepths)
        {
            foreach (var (clock, k) in new[] { (StormClock.Turns, 0), (StormClock.Instability, 0), (StormClock.Instability, 1), (StormClock.Instability, 2) })
                foreach (char mode in new[] { 'A', 'B', 'C' })
                {
                    var before = new List<int>(); var vis = new List<int>(); var hp = new List<int>(); var ss = new List<int>(); var turns = new List<int>();
                    var kinds = new long[WalkKinds.Length]; var parts = new long[Instability.Parts]; var dead = new int[maxHps.Length]; int reached = 0, collapsed = 0;
                    for (int salt = 1; salt <= 1000; salt++)
                    {
                        var w = new Wallet { Provision = 100000000, Hp = 100000000 };
                        var s = MakeStorm(salt, floor, w, null, clock);
                        var r = WalkI(s, w, mode, k);
                        if (r.reached) reached++;
                        if (s.StormPhase >= 1 || (clock == StormClock.Instability && Instability.Collapsing(s.Instability))) collapsed++;
                        before.Add(r.visitedBeforeCollapse); vis.Add(r.visited); hp.Add(r.stormHp); ss.Add(r.stormSteps); turns.Add(r.turns);
                        for (int q = 0; q < kinds.Length; q++) kinds[q] += r.kinds[q];
                        for (int q = 0; q < parts.Length; q++) parts[q] += r.parts[q];
                        int paid = 100000000 - w.Hp;
                        for (int m = 0; m < maxHps.Length; m++) if (paid >= maxHps[m]) dead[m]++;
                    }
                    string head = clock == StormClock.Turns ? "手番 (前の版)" : $"不安定化 k={k}";
                    string partText = clock == StormClock.Turns ? "" :
                        $" / 溜まった内訳 (1 層の平均) 取る {parts[Instability.PartTake] / 1000.0:F1}・激化 {parts[Instability.PartEscalation] / 1000.0:F1}・歩く {parts[Instability.PartWalk] / 1000.0:F1}";
                    string deadText = mode == 'C' ? $" / 死んだ層 (最大 HP 30 で {dead[0] / 10.0:F1}%・85 で {dead[1] / 10.0:F1}%)" : "";
                    Console.WriteLine($"[歩き {head} {mode}] 深さ{floor}: 着いた {reached}/1000・崩れた層 {collapsed / 10.0:F1}% / 崩れ始めるまでに踏んだ点 {Avg(before)} / 抜けるまでに踏んだ点 {Avg(vis)}"
                        + $" / 種別 (1 層の平均) {string.Join(" ", WalkKinds.Select((t, i) => $"{t}:{kinds[i] / 1000.0:F1}"))}{partText}"
                        + $" / 嵐の HP {Avg(hp)} (嵐の中の歩数 平均 {ss.Average():F1}) / 手番 {Avg(turns)}{deadText}");
                    if (clock == StormClock.Instability) Check(reached == 1000, $"不安定化の歩き {mode} k={k}: 深さ{floor} は全層で終点に着く ({reached}/1000)");
                }
        }
    }
}

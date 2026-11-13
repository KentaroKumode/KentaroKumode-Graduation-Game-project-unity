using System;
using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>
    /// [計装 2026-11-13] ボスのダウンと手札の持ち越しの数え (バッチ累計・結果を変えない・乱数も引かない)。
    /// ボス戦ごとに: ダウンの回数・うち大技を取り消した回数 (ダウンが大技のチャージのターンか大技のターンに重なった・2026-11-14〜)・
    /// 空振り (重ならなかった = 大技の周期はそのまま進む)・大技を受けた回数。 口 downCancelRule=Always (旧い規則) の時は、 今までどおり
    /// 「取り消した」= 大技のターンのダウンだけを数える (旧い測定と同じ数え方)。
    /// 手札の持ち越し: ターンあたりの持ち越した枚数。 AutoRunner のサマリーが <see cref="Line"/> を出す。
    /// </summary>
    public static class BossDownDiag
    {
        /// <summary>ボス id → [戦闘数, 目盛りのある戦闘, ダウン, 大技を取り消した (チャージか大技のターンに重なった), 空振り (大技を持つ敵で重ならなかった), 大技を持たない敵のダウン, 大技を受けた,
        /// (2026-11-14 診断) ダウンのターンの周期の位置 小技 / チャージ / 大技, 敵が大技で攻撃したターン数 / その被ダメ (防御・シールドの後の HP の減り) の合計,
        /// 小技で攻撃したターン数 / その被ダメの合計, チャージで攻撃しなかったターン数]。 添字は <see cref="Slot"/>。</summary>
        public static readonly Dictionary<string, long[]> ByBoss = new Dictionary<string, long[]>();
        /// <summary>手札の持ち越し: ターン数 / 持ち越した枚数の合計。</summary>
        public static long CarryTurns, CarryCards;

        private static long[] _cur;

        public static void Reset() { ByBoss.Clear(); CarryTurns = 0; CarryCards = 0; KeptCards = 0; KeptUsed = 0; _cur = null; }

        /// <summary>戦闘の始め (ヴェスカは段ごと)。 ボスでなければ数えない。</summary>
        public static void BeginFight(string id, bool hasNotches)
        {
            _cur = null;
            if (!GameLoop.BossIds.IsBoss(id)) return;
            if (!ByBoss.TryGetValue(id, out _cur)) { _cur = new long[Slot.Length]; ByBoss[id] = _cur; }
            _cur[0]++;
            if (hasNotches) _cur[1]++;
        }

        public static void EndFight() { _cur = null; }

        /// <summary>[計装 2026-11-14] ダウンしたターンの通算 (プロセスを通して増えるだけ・Reset で戻さない)。 戦闘ごとの数は、 読む側が戦闘の始めとの差で取る。</summary>
        public static long TotalDowns;

        /// <summary>添字 (2026-11-14 の診断で 7 以降を足した)。</summary>
        public static class Slot
        {
            public const int DownAtLight = 7, DownAtCharge = 8, DownAtHeavy = 9;
            public const int HeavyTurns = 10, HeavyTaken = 11, LightTurns = 12, LightTaken = 13, ChargeTurns = 14;
            // 割れ方 (2026-11-14・目盛りを割れるのは通常攻撃だけ・ひび)
            public const int MainBroken = 15, CrackBroken = 16, MainDownCanceled = 17, CrackDownCanceled = 18;
            public const int CrackThenMain = 19, CrackWaitSum = 20, CracksMade = 21, CracksHealedRetired = 22;   // 22 = 回復で消えたひび (廃止・2026-11-14 にひびは回復で消えなくなった)
            // 旧い判定 (AnyDamage) の時: ターンの終わりに割れた数 / そのうち主攻撃だけでは割れていなかった数 (主攻撃以外で割れた)
            public const int AnyBreaks = 23, AnyNonMainBreaks = 24;
            public const int Length = 25;
        }

        /// <summary>ダウンのターン。 <paramref name="turnsToHeavy"/> = 起点を動かす前の周期で数えた「大技まで何ターンか」(0 = 大技・1 = チャージ・2 以上 = 小技・−1 = 大技なし)。</summary>
        public static void NoteDown(bool hasHeavy, bool canceled, int turnsToHeavy = -1, bool weak = false)
        {
            TotalDowns++;
            if (_cur == null) return;
            if (hasHeavy && canceled) _cur[weak ? Slot.CrackDownCanceled : Slot.MainDownCanceled]++;
            _cur[2]++;
            if (!hasHeavy) _cur[5]++;
            else if (canceled) _cur[3]++;
            else _cur[4]++;
            if (hasHeavy && turnsToHeavy >= 0) _cur[turnsToHeavy == 0 ? Slot.DownAtHeavy : turnsToHeavy == 1 ? Slot.DownAtCharge : Slot.DownAtLight]++;
        }

        /// <summary>[診断 2026-11-14] 大技を持つボスの、 ダウンでないターンの敵の行動と、 そのターンに受けた HP の減り (防御・シールドの後・業火の烈炎の分も含む)。
        /// <paramref name="kind"/> = 0 大技 / 1 チャージ (攻撃しない) / 2 小技 / それ以外は数えない。</summary>
        public static void NoteEnemyTurn(int kind, int taken)
        {
            if (_cur == null) return;
            if (kind == 0) { _cur[Slot.HeavyTurns]++; _cur[Slot.HeavyTaken] += System.Math.Max(0, taken); }
            else if (kind == 2) { _cur[Slot.LightTurns]++; _cur[Slot.LightTaken] += System.Math.Max(0, taken); }
            else if (kind == 1) _cur[Slot.ChargeTurns]++;
        }

        /// <summary>通常攻撃で <paramref name="d"/> 個割った (うち <paramref name="crackedBroken"/> 個はひびが入っていた・ひびから割るまでのターン数の合計 <paramref name="waitSum"/>)。</summary>
        public static void NoteMainBreak(int d, int crackedBroken, int waitSum)
        {
            if (_cur == null) return;
            _cur[Slot.MainBroken] += d; _cur[Slot.CrackThenMain] += crackedBroken; _cur[Slot.CrackWaitSum] += waitSum;
        }

        /// <summary>ターンの終わり: 新しく入ったひび / ひびの猶予が切れて割れた数。</summary>
        public static void NoteCracks(int made, int crackBroken)
        {
            if (_cur == null) return;
            _cur[Slot.CracksMade] += made; _cur[Slot.CrackBroken] += crackBroken;
        }

        /// <summary>旧い判定 (AnyDamage): ターンの終わりに <paramref name="d"/> 個割れ、 そのうち主攻撃の直後の HP で割れていたのは <paramref name="byMain"/> 個。</summary>
        public static void NoteAnyDamageBreak(int d, int byMain)
        {
            if (_cur == null) return;
            _cur[Slot.AnyBreaks] += d; _cur[Slot.AnyNonMainBreaks] += System.Math.Max(0, d - byMain);
        }

        public static void NoteHeavyTaken() { if (_cur != null) _cur[6]++; }

        public static void NoteCarry(int carried) { CarryTurns++; CarryCards += carried; }

        /// <summary>手札の規則 Pick: 残す印で残した端子の数 / そのうち、 残した次のターンに挿した (使った) 数。</summary>
        public static long KeptCards, KeptUsed;
        public static void NoteKeptUse(int kept, int used) { KeptCards += kept; KeptUsed += used; }

        /// <summary>サマリーの行 (何も数えていなければ空)。</summary>
        public static string Line()
        {
            if (ByBoss.Count == 0 && CarryTurns == 0) return "";
            var sb = new System.Text.StringBuilder("[ダウンと手札] ");
            sb.Append(CarryTurns > 0 ? $"手札の持ち越し ({GameLoop.GameHooks.HandCarry}) 1 ターン平均 {(double)CarryCards / CarryTurns:0.00} 枚 ({CarryTurns} ターン)" : "手札の持ち越し なし");
            if (KeptCards > 0) sb.Append($" / 印で残した端子 {KeptCards} 枚のうち、 次のターンに使った {KeptUsed} 枚 ({100.0 * KeptUsed / KeptCards:0.0}%)");
            var ids = new List<string>(ByBoss.Keys); ids.Sort(System.StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var a = ByBoss[id];
                if (a[0] == 0) continue;
                sb.Append(System.Environment.NewLine).Append("  ").Append(id)
                  .Append($": 戦闘 {a[0]} (目盛りあり {a[1]}) / ダウン {a[2]} (1 戦 {(double)a[2] / a[0]:0.00})")
                  .Append($" うち大技を取り消した (チャージか大技に重なった) {a[3]}・空振り (重ならず周期はそのまま) {a[4]}・大技なしの敵 {a[5]} / 大技を受けた {a[6]} (1 戦 {(double)a[6] / a[0]:0.00})");
                // [診断 2026-11-14] ダウンの位置と、 大技 / 小技のターンの被ダメ (防御の後)。 浮いた被ダメの見込み = 大技のダウン × 大技の平均 + チャージのダウン × (大技の平均 − 1 周期の平均) + 小技のダウン × 小技の平均
                long ht = a[Slot.HeavyTurns], lt = a[Slot.LightTurns], ct = a[Slot.ChargeTurns];
                if (ht + lt + ct > 0)
                {
                    double hAvg = ht > 0 ? (double)a[Slot.HeavyTaken] / ht : 0, lAvg = lt > 0 ? (double)a[Slot.LightTaken] / lt : 0;
                    double cyc = (double)(a[Slot.HeavyTaken] + a[Slot.LightTaken]) / (ht + lt + ct);
                    double saved = a[Slot.DownAtHeavy] * hAvg + a[Slot.DownAtCharge] * (hAvg - cyc) + a[Slot.DownAtLight] * lAvg;
                    sb.Append(System.Environment.NewLine).Append("    ")
                      .Append($"ダウンの位置 小技 {a[Slot.DownAtLight]}・チャージ {a[Slot.DownAtCharge]}・大技 {a[Slot.DownAtHeavy]}")
                      .Append($" / 被ダメ (防御の後) 大技のターン 平均 {hAvg:0.0} ({ht} T)・小技のターン 平均 {lAvg:0.0} ({lt} T)・チャージ {ct} T・1 周期の平均 {cyc:0.0}")
                      .Append($" / ダウンで浮いた被ダメの見込み 1 戦 {saved / a[0]:0.0}");
                }
                // 割れ方 (2026-11-14)
                if (a[Slot.MainBroken] + a[Slot.CrackBroken] + a[Slot.CracksMade] > 0)
                    sb.Append(System.Environment.NewLine).Append("    ")
                      .Append($"割れ方 通常攻撃 {a[Slot.MainBroken]} (そのダウンで大技を消した {a[Slot.MainDownCanceled]})・ひび {a[Slot.CrackBroken]} (大技を消した {a[Slot.CrackDownCanceled]})")
                      .Append($" / 通常攻撃以外で下回った (ひび) {a[Slot.CracksMade]}")
                      .Append($"・ひびの後に通常攻撃で割った {a[Slot.CrackThenMain]} (平均 {(a[Slot.CrackThenMain] > 0 ? (double)a[Slot.CrackWaitSum] / a[Slot.CrackThenMain] : 0):0.00} ターン後)");
                if (a[Slot.AnyBreaks] > 0)
                    sb.Append(System.Environment.NewLine).Append("    ")
                      .Append($"割れ方 (旧い判定 AnyDamage) 割れた {a[Slot.AnyBreaks]}・うち主攻撃の直後には割れておらず通常攻撃以外で割れた {a[Slot.AnyNonMainBreaks]}")
                      .Append($" ({100.0 * a[Slot.AnyNonMainBreaks] / a[Slot.AnyBreaks]:0.0}%)");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// [計装 2026-11-14・作者の依頼「大技をモロに受けた時にどれくらい受けているか」] 大技を持つボスの、 実際に撃たれた大技 / 小技 / チャージのターンの被ダメ。
    /// 結果を変えない・乱数を引かない。 サマリーの <c>[大技の被ダメ]</c> 行 (<see cref="Line"/>)。 並列ワーカーのチャンクを足せるよう、 合計・回数・ヒストグラムも出す。
    /// 大技 = 取り消されず・ダウンでも〈封じ・揃〉でもなく・敵が倒れる前に撃った大技のターン (灰燼の王の〈業火〉も)。 被ダメ = 防御・シールドの後の HP の減り
    /// (敵の攻撃の枝の中: 主攻撃 + プレイヤーの状態の固定ダメ + 〈業火〉の烈炎の分。 ターン終了時の削りは入れない)。
    /// </summary>
    public static class HeavyHitDiag
    {
        /// <summary>5 HP 刻みのヒストグラムの数 (最後は 200 以上)。 割合 (最大 HP 比) は 1% 刻み・最後は 100% 以上。</summary>
        public const int HpBin = 5, HpBins = 41, PctBins = 101;

        public sealed class Acc
        {
            public long n, atkPre, taken, zero, half, deaths, blockDice, max;
            public double pctMax, pctHead;
            public readonly long[] hist = new long[HpBins], pctHist = new long[PctBins];
            // 〈業火〉: 攻撃の分 (主攻撃の防御・シールドの後) / 烈炎の分 (軽減無視・シールドの後) / 撃った時の烈炎の数
            public long atkPart, burstPart, blazeStacks, burstTurns;
            public void Add(int atkPreDef, int takenHp, int maxHp, int hpHead, bool dead, int blockDiceN, int atkPartHp, int burstHp, int stacks)
            {
                n++; atkPre += Math.Max(0, atkPreDef); taken += Math.Max(0, takenHp); if (takenHp > max) max = takenHp;
                if (takenHp <= 0) zero++;
                if (atkPreDef > 0 && takenHp * 2 <= atkPreDef) half++;
                if (dead) deaths++;
                blockDice += Math.Max(0, blockDiceN);
                double pm = maxHp > 0 ? (double)Math.Max(0, takenHp) / maxHp : 0, ph = hpHead > 0 ? (double)Math.Max(0, takenHp) / hpHead : 0;
                pctMax += pm; pctHead += ph;
                hist[Math.Min(HpBins - 1, Math.Max(0, takenHp) / HpBin)]++;
                pctHist[Math.Min(PctBins - 1, (int)(pm * 100))]++;
                atkPart += Math.Max(0, atkPartHp); burstPart += Math.Max(0, burstHp); blazeStacks += Math.Max(0, stacks); if (burstHp > 0 || stacks > 0) burstTurns++;
            }
        }

        /// <summary>ボス id → [0 大技・1 チャージ・2 小技]。 チャージは taken = そのターンの HP の減り (ターン終了時の削りも含む・ターンの頭からターンの終わり)。</summary>
        public static readonly Dictionary<string, Acc[]> ByBoss = new Dictionary<string, Acc[]>();
        /// <summary>ボス id → [戦闘数, プレイヤーが負けた戦闘数] (目盛りや大技の有無を問わない・大技を持つボスだけ表に出る)。</summary>
        public static readonly Dictionary<string, long[]> Fights = new Dictionary<string, long[]>();
        /// <summary>プロセスを通した通算 (Reset で戻さない・戦闘ごとの数は読む側が始めとの差で取る): 撃たれた大技の回数 / 大技で受けた HP の合計。</summary>
        public static long TotalHeavyHits, TotalHeavyTaken;
        /// <summary>今の戦闘 (段) の最大の 1 発 (読む側が戦闘の始めに 0 へ戻す)。</summary>
        public static int SegmentHeavyMax;

        private static string _id;
        public static void Reset() { ByBoss.Clear(); Fights.Clear(); _id = null; }

        /// <summary>戦闘の始め・形態の交代 (ヴェスカの段)。 前の段が開いたままなら、 倒して移った段として負けなしで閉じる。</summary>
        public static void BeginFight(string id) { if (_id != null) EndFight(false); _id = GameLoop.BossIds.IsBoss(id) ? id : null; }

        public static void EndFight(bool playerDied)
        {
            if (_id == null) return;
            if (!Fights.TryGetValue(_id, out var f)) { f = new long[2]; Fights[_id] = f; }
            f[0]++; if (playerDied) f[1]++;
            _id = null;
        }

        private static Acc Get(int kind)
        {
            if (_id == null || kind < 0 || kind > 2) return null;
            if (!ByBoss.TryGetValue(_id, out var a)) { a = new[] { new Acc(), new Acc(), new Acc() }; ByBoss[_id] = a; }
            return a[kind];
        }

        /// <summary>大技 (0) / 小技 (2) のターン: 敵の攻撃の枝を通った後。 <paramref name="atkPreDef"/> = 敵の攻撃値 (プレイヤーの防御の前) / <paramref name="taken"/> = 受けた HP の減り。</summary>
        public static void NoteHit(int kind, int atkPreDef, int taken, int maxHp, int hpHead, bool dead, int blockDiceN, int atkPartHp, int burstHp, int stacks)
        {
            var a = Get(kind);
            if (a == null) return;
            a.Add(atkPreDef, taken, maxHp, hpHead, dead, blockDiceN, atkPartHp, burstHp, stacks);
            if (kind == 0)
            {
                TotalHeavyHits++; TotalHeavyTaken += Math.Max(0, taken);
                if (taken > SegmentHeavyMax) SegmentHeavyMax = taken;
            }
        }

        /// <summary>チャージのターン: ターンの頭からターンの終わりまでの HP の減り (攻撃は無い・ターン終了時の削りなど)。</summary>
        public static void NoteCharge(int lost, int maxHp, int hpHead, bool dead)
        {
            var a = Get(1);
            if (a == null) return;
            a.Add(0, lost, maxHp, hpHead, dead, 0, 0, 0, 0);
        }

        private static double Q(long[] hist, long n, double q, double binW)
        {
            if (n <= 0) return 0;
            long target = (long)Math.Ceiling(q * n), c = 0;
            for (int i = 0; i < hist.Length; i++) { c += hist[i]; if (c >= target) return (i + 0.5) * binW; }
            return (hist.Length - 0.5) * binW;
        }

        /// <summary>サマリーの行 (大技を持つボスが居なければ空)。</summary>
        public static string Line()
        {
            if (ByBoss.Count == 0) return "";
            var sb = new System.Text.StringBuilder("[大技の被ダメ] (被ダメ = 防御・シールドの後の HP の減り・中央値と P90 は 5 HP / 1% 刻みのヒストグラムから・hist は 5 HP 刻み 0..200+・合計と回数はチャンクを足せる)");
            var ids = new List<string>(ByBoss.Keys); ids.Sort(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var a = ByBoss[id];
                Fights.TryGetValue(id, out var f);
                long fights = f != null ? f[0] : 0, lost = f != null ? f[1] : 0;
                var h = a[0]; var l = a[2]; var c = a[1];
                double D(long x, long n) => n > 0 ? (double)x / n : 0;
                sb.Append(Environment.NewLine).Append("  ").Append(id).Append($": 戦闘 {fights} (負け {lost})");
                sb.Append(Environment.NewLine).Append("    大技 ")
                  .Append($"{h.n} 回 (1 戦 {D(h.n, fights):0.00}) / 攻撃 (防御の前) 平均 {D(h.atkPre, h.n):0.0}")
                  .Append($" / 被ダメ 平均 {D(h.taken, h.n):0.0}・中央値 {Q(h.hist, h.n, 0.5, HpBin):0}・P90 {Q(h.hist, h.n, 0.9, HpBin):0}・最大 {h.max}")
                  .Append($" / ÷最大 HP 平均 {100 * (h.n > 0 ? h.pctMax / h.n : 0):0.0}%・中央値 {Q(h.pctHist, h.n, 0.5, 1):0}% / ÷敵の攻撃の前の HP 平均 {100 * (h.n > 0 ? h.pctHead / h.n : 0):0.0}%")
                  .Append($" / 完全に防いだ {100 * D(h.zero, h.n):0.0}%・半分以上防いだ {100 * D(h.half, h.n):0.0}%")
                  .Append($" / そのターンに死んだ {h.deaths} ({100 * D(h.deaths, h.n):0.0}%・このボスの負けの {100 * D(h.deaths, lost):0.0}%)")
                  .Append($" / 防御へ挿した出目 平均 {D(h.blockDice, h.n):0.00}");
                if (h.burstTurns > 0)
                    sb.Append($" / 〈業火〉 攻撃の分 平均 {D(h.atkPart, h.n):0.0}・烈炎の分 (軽減無視) 平均 {D(h.burstPart, h.n):0.0}・烈炎の数 平均 {D(h.blazeStacks, h.n):0.00}");
                sb.Append($" / 合計 被ダメ {h.taken}・攻撃 {h.atkPre}・死 {h.deaths}・完全 {h.zero}・半分 {h.half}・出目 {h.blockDice}・攻撃の分 {h.atkPart}・烈炎の分 {h.burstPart}・烈炎 {h.blazeStacks}")
                  .Append(" / hist5=").Append(string.Join(",", h.hist));
                sb.Append(Environment.NewLine).Append("    小技 ")
                  .Append($"{l.n} 回 (1 戦 {D(l.n, fights):0.00}) / 攻撃 (防御の前) 平均 {D(l.atkPre, l.n):0.0}")
                  .Append($" / 被ダメ 平均 {D(l.taken, l.n):0.0}・中央値 {Q(l.hist, l.n, 0.5, HpBin):0}・最大 {l.max} / ÷最大 HP 平均 {100 * (l.n > 0 ? l.pctMax / l.n : 0):0.0}%")
                  .Append($" / 完全に防いだ {100 * D(l.zero, l.n):0.0}% / そのターンに死んだ {l.deaths} / 防御へ挿した出目 平均 {D(l.blockDice, l.n):0.00}")
                  .Append($" / 合計 被ダメ {l.taken}・攻撃 {l.atkPre}・完全 {l.zero} / hist5=").Append(string.Join(",", l.hist));
                sb.Append(Environment.NewLine).Append("    チャージ ")
                  .Append($"{c.n} 回 / そのターンの HP の減り (ターン終了時の削りなど) 平均 {D(c.taken, c.n):0.0} / 合計 {c.taken} / そのターンに死んだ {c.deaths}");
            }
            return sb.ToString();
        }
    }
}

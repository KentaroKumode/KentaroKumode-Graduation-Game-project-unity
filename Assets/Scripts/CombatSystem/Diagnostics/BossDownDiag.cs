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
            public const int CrackThenMain = 19, CrackWaitSum = 20, CracksMade = 21, CracksHealed = 22;
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

        /// <summary>ターンの終わり: 新しく入ったひび / 回復で消えたひび / ひびの猶予が切れて割れた数。</summary>
        public static void NoteCracks(int made, int healed, int crackBroken)
        {
            if (_cur == null) return;
            _cur[Slot.CracksMade] += made; _cur[Slot.CracksHealed] += healed; _cur[Slot.CrackBroken] += crackBroken;
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
                      .Append($" / 通常攻撃以外で下回った (ひび) {a[Slot.CracksMade]}・回復で消えた {a[Slot.CracksHealed]}")
                      .Append($"・ひびの後に通常攻撃で割った {a[Slot.CrackThenMain]} (平均 {(a[Slot.CrackThenMain] > 0 ? (double)a[Slot.CrackWaitSum] / a[Slot.CrackThenMain] : 0):0.00} ターン後)");
                if (a[Slot.AnyBreaks] > 0)
                    sb.Append(System.Environment.NewLine).Append("    ")
                      .Append($"割れ方 (旧い判定 AnyDamage) 割れた {a[Slot.AnyBreaks]}・うち主攻撃の直後には割れておらず通常攻撃以外で割れた {a[Slot.AnyNonMainBreaks]}")
                      .Append($" ({100.0 * a[Slot.AnyNonMainBreaks] / a[Slot.AnyBreaks]:0.0}%)");
            }
            return sb.ToString();
        }
    }
}

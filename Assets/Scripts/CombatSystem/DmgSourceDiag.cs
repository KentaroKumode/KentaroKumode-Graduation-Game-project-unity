using System.Collections.Generic;
using InventorySystem.PassiveSkills;

namespace CombatSystem
{
    /// <summary>
    /// [計装 2026-09-20] 与ダメ増加の出どころ別に「所持中の攻撃のうち何割で効いたか」と「効いたときの量」を数える。
    ///
    /// <para><b>額面ではなく実効で比べるためのもの。</b> +100% でも 2% の攻撃でしか効かないものと、
    /// +50% で 99% の攻撃に効くものを並べる。 実効 = 発動率 × 発動時の平均量 = 合計量 ÷ 所持攻撃数。</para>
    ///
    /// <para>パッシブは <see cref="PassiveSkillManager.FireTrigger"/> で 1 効果ずつ前後の差分を取るので、
    /// 書き込み地点を個別に拾わなくても漏れない。 パッシブ以外 (整備パネル・消費・役・端子・遺物) は
    /// 発生地点で <see cref="Note"/> する。 どこにも帰属しなかった分は総計との差で出る。</para>
    ///
    /// <para>1 ターンの差分は <see cref="BeginTurn"/> で捨て、 プレイヤーの主攻撃で <see cref="Commit"/> する。
    /// 敵の攻撃処理で同じ値が動いても、 コミット後なので混ざらない。</para>
    /// </summary>
    public static class DmgSourceDiag
    {
        public const int Out = 0, CritMul = 1, NonCrit = 2, CritRate = 3, ForceCrit = 4, Pen = 5, Vul = 6, Fields = 7;

        /// <summary>出どころ 1 件の累計。
        /// [0]所持攻撃数 [1]うち会心 [2]うち非会心,
        /// 以降 Fields 個ぶん [発動数, 合計量] の組 (3 + 2×k)。
        /// 会心倍率は会心した攻撃だけ、 非会心% は非会心の攻撃だけを発動として数える (乗らなかった分は効いていない)。</summary>
        public const int Width = 3 + 2 * Fields;

        /// <summary>[0]全戦闘 [1]ボス戦のみ。</summary>
        public static readonly Dictionary<string, double[]>[] Stats =
            { new Dictionary<string, double[]>(), new Dictionary<string, double[]>() };

        /// <summary>全攻撃に対して「所持」扱いにする出どころ (役・端子・武器の素の会心率)。</summary>
        public static readonly HashSet<string> Universal = new HashSet<string>();

        public const string TotalKey = "__total__";
        public const string WeaponCritKey = "武器の素の会心率";
        public const string AimKey = "特殊端子〈照準〉";

        static readonly Dictionary<string, float[]> _pending = new Dictionary<string, float[]>();
        static readonly HashSet<string> _heldNow = new HashSet<string>();

        public static void Reset()
        {
            Stats[0].Clear(); Stats[1].Clear(); Universal.Clear();
            foreach (var k in YachtRoles.All) Universal.Add("役〈" + YachtRoles.NameOf(k) + "〉");
            Universal.Add(AimKey);
            _pending.Clear(); _heldNow.Clear();
        }

        public static void BeginTurn() { _pending.Clear(); _heldNow.Clear(); }

        public struct Snap { public float o, cm, nc, cr, pen; public bool fc; }

        public static Snap Take(CombatContext c) => c == null ? default : new Snap
        {
            o = c.outgoingDamageMultiplier <= 0f ? 1f : c.outgoingDamageMultiplier,
            cm = c.criticalMultiplier, nc = c.nonCritOutgoingMultiplier,
            cr = c.critRatePctAdd, pen = c.armorPenPct, fc = c.forceCritical,
        };

        /// <summary>前後の差分を出どころへ積む。 変化が無ければ何もしない。</summary>
        public static void Attribute(string src, Snap before, CombatContext c)
        {
            if (c == null || string.IsNullOrEmpty(src)) return;
            var a = Take(c);
            Add(src, Out, a.o - before.o);
            Add(src, CritMul, a.cm - before.cm);
            Add(src, NonCrit, a.nc - before.nc);
            Add(src, CritRate, a.cr - before.cr);
            Add(src, Pen, a.pen - before.pen);
            if (a.fc && !before.fc) Add(src, ForceCrit, 1f);
        }

        /// <summary>パッシブ以外の出どころを直接記録する。 所持扱いにもする。</summary>
        public static void Note(string src, int field, float v) { _heldNow.Add(src); Add(src, field, v); }

        /// <summary>このターン所持していたことだけを記録する (効いたかどうかは別)。</summary>
        public static void Held(string src) { if (!string.IsNullOrEmpty(src)) _heldNow.Add(src); }

        static void Add(string src, int field, float v)
        {
            if (v > -1e-6f && v < 1e-6f) return;
            if (!_pending.TryGetValue(src, out var p)) { p = new float[Fields]; _pending[src] = p; }
            p[field] += v;
        }

        /// <summary>プレイヤーの主攻撃 1 回ぶんを確定する。</summary>
        static readonly HashSet<string> _seen = new HashSet<string>();

        public static void Commit(CombatContext c, bool isCrit, bool boss, float critRateBase,
                                  IEnumerable<string> heldSkills, IEnumerable<string> heldItems,
                                  string extraFlag = null)
        {
            if (c == null) return;
            _heldNow.Add(WeaponCritKey);
            Add(WeaponCritKey, CritRate, critRateBase);
            if (heldSkills != null)
            {
                // 同じパッシブが 2 回以上登録されている攻撃を「__dup__:ID」として数える (二重発火の検出)。
                _seen.Clear();
                foreach (var s in heldSkills)
                {
                    _heldNow.Add(s);
                    if (!_seen.Add(s)) _heldNow.Add("__dup__:" + s);
                }
            }
            if (heldItems != null && extraFlag != null) _heldNow.Add(extraFlag);
            if (heldItems != null) foreach (var s in heldItems) _heldNow.Add(s);
            foreach (var u in Universal) _heldNow.Add(u);
            foreach (var k in _pending.Keys) _heldNow.Add(k);

            for (int d = 0; d < (boss ? 2 : 1); d++)
            {
                var st = Stats[d];
                foreach (var src in _heldNow)
                {
                    var a = Row(st, src);
                    a[0]++; if (isCrit) a[1]++; else a[2]++;
                    if (!_pending.TryGetValue(src, out var p)) continue;
                    for (int f = 0; f < Fields; f++)
                    {
                        float v = p[f];
                        if (v == 0f) continue;
                        if (f == CritMul && !isCrit) continue;
                        if (f == NonCrit && isCrit) continue;
                        if (f == ForceCrit && !isCrit) continue;
                        a[3 + 2 * f]++; a[4 + 2 * f] += v;
                    }
                }
                // 総計: 最終値。 帰属の合計との差が「取りこぼし」。
                var t = Row(st, TotalKey);
                t[0]++; if (isCrit) t[1]++; else t[2]++;
                var s2 = Take(c);
                t[4 + 2 * Out] += s2.o - 1f;
                if (isCrit) t[4 + 2 * CritMul] += s2.cm; else t[4 + 2 * NonCrit] += s2.nc;
                t[4 + 2 * CritRate] += s2.cr + critRateBase;
                if (s2.fc && isCrit) t[3 + 2 * ForceCrit]++;
                t[4 + 2 * Pen] += s2.pen;
            }
            _pending.Clear(); _heldNow.Clear();
        }

        static double[] Row(Dictionary<string, double[]> st, string k)
        {
            if (!st.TryGetValue(k, out var a)) { a = new double[Width]; st[k] = a; }
            return a;
        }
    }
}

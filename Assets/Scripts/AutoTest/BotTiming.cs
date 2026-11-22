using System.Diagnostics;
using System.Text;

namespace AutoTest
{
    /// <summary>[計装 2026-11-21] ランの所要を区分ごとに数える (排他時間・入れ子は内側の区分へ付け替え)。 乱数を引かない・判断に読まない。
    /// サマリーの [所要の内訳] 行が読み、 書いたら 0 に戻す。 主スレッド専用。</summary>
    internal static class BotTiming
    {
        public enum Cat { Other, Frame, Nav, Shop, Event, Rest, CombatBot, Wiring, Tactics, Reroll, Resolve,
            NavStorm, NavPlan, NavStep, NavBody, WirePrep, WireChoose, WireKeep, EvalResolve, EvalSums, EvalUtil, Count }

        private static readonly string[] Labels =
        {
            "その他 (ランの準備・記録)", "フレーム待ち", "航行: その他", "店", "イベント", "休憩・報酬ほか",
            "戦闘の BOT (回復判断ほか)", "配線: その他", "戦術消耗品", "振り直し", "本体の戦闘の解決",
            "航行: 嵐の見方", "航行: 行き先選び", "航行: 1 歩の道", "航行: 本体の移動", "配線: 頭の判断", "配線: 挿し方の選択", "配線: 手札",
            "挿し方 1 つ: 端子の解決", "挿し方 1 つ: 集計", "挿し方 1 つ: 効用",
        };

        private static readonly long[] _ticks = new long[(int)Cat.Count];
        private static readonly Cat[] _stack = new Cat[64];
        private static int _depth;
        private static Cat _cur = Cat.Other;
        private static long _last = Stopwatch.GetTimestamp();

        private static void Charge()
        {
            long now = Stopwatch.GetTimestamp();
            _ticks[(int)_cur] += now - _last;
            _last = now;
        }

        public static Scope Enter(Cat c)
        {
            Charge();
            if (_depth < _stack.Length) _stack[_depth] = _cur;
            _depth++;
            _cur = c;
            return default;
        }

        public static void Exit()
        {
            Charge();
            if (_depth > 0) { _depth--; _cur = _depth < _stack.Length ? _stack[_depth] : Cat.Other; }
        }

        public readonly struct Scope : System.IDisposable { public void Dispose() => Exit(); }

        public static void Reset()
        {
            System.Array.Clear(_ticks, 0, _ticks.Length);
            _last = Stopwatch.GetTimestamp();
        }

        /// <summary>1 ランあたりのミリ秒と割合。 runs はこの期間のラン数。</summary>
        public static string Describe(int runs)
        {
            Charge();
            long total = 0; foreach (var t in _ticks) total += t;
            double perMs = 1000.0 / Stopwatch.Frequency / System.Math.Max(1, runs);
            var sb = new StringBuilder($"[所要の内訳] ラン 1 本 {total * perMs:F1} ms ({runs} ラン)");
            for (int i = 0; i < (int)Cat.Count; i++)
                sb.Append($" / {Labels[i]} {_ticks[i] * perMs:F1} ms ({100.0 * _ticks[i] / System.Math.Max(1, total):F1}%)");
            return sb.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>[計測] ボスごとの **プレイヤーの実攻撃ダメージ**。
    /// HP減少総量 (damageDealt) は自壊・DOT・固定ダメを全部含むため、
    /// 「1 発いくら出ているか」を推し量る根拠にはならない。 ここは主攻撃と固定ダメを
    /// 分けて実測する。 バッチ後に Dump() で読み出す
    /// (AutoRunner がバッチ中 Debug.unityLogger.logEnabled=false にするためログは使えない)。</summary>
    public static class BossDmgDiag
    {
        private class Row { public long turns, mainSum, fixedSum, allTurns, fights; public int mainMax; }
        private static readonly System.Collections.Generic.Dictionary<string, Row> _rows
            = new System.Collections.Generic.Dictionary<string, Row>();
        private static readonly System.Collections.Generic.Dictionary<string, string> _names
            = new System.Collections.Generic.Dictionary<string, string>();

        public static void Reset() { _rows.Clear(); _names.Clear(); _spikes.Clear();
            p4Turns = p4Blocked = p4Execute = p4MaxHpBite = p4Fights = 0;
            p4EntryHpPct = 0; _relic.Clear();
            _relicSeen.Clear(); _relicFatal.Clear(); v7Turns = v7Deaths = 0;
            _phaseN.Clear(); _phaseHp.Clear(); _phaseHpMin.Clear(); }

        // ---- 7層 p4 専用の内訳 ----
        public static long p4Turns, p4Blocked, p4Execute, p4MaxHpBite, p4Fights;
        public static double p4EntryHpPct;
        private static readonly System.Collections.Generic.Dictionary<string,int> _relic
            = new System.Collections.Generic.Dictionary<string,int>();

        public static void P4Entry(double hpPct) { p4Fights++; p4EntryHpPct += hpPct; }

        // ---- 7層 各段の突入時 残HP ----
        private static readonly System.Collections.Generic.Dictionary<string,int> _phaseN
            = new System.Collections.Generic.Dictionary<string,int>();
        private static readonly System.Collections.Generic.Dictionary<string,double> _phaseHp
            = new System.Collections.Generic.Dictionary<string,double>();
        private static readonly System.Collections.Generic.Dictionary<string,double> _phaseHpMin
            = new System.Collections.Generic.Dictionary<string,double>();

        public static void PhaseEntry(string id, double hpPct)
        {
            if (string.IsNullOrEmpty(id)) return;
            _phaseN.TryGetValue(id, out var n); _phaseN[id] = n + 1;
            _phaseHp.TryGetValue(id, out var h); _phaseHp[id] = h + hpPct;
            if (!_phaseHpMin.TryGetValue(id, out var mn) || hpPct < mn) _phaseHpMin[id] = hpPct;
        }

        /// <summary>[計装] 段 index(0=p1..3=p4) の突入数と 残HP% 合計を取り出す。
        /// <c>DumpPhaseEntry</c> は文字列しか返さず、 しかもどこからも呼ばれていなかった。</summary>
        public static void ReadPhaseEntry(long[] n, double[] hpSum)
        {
            var ids = new[]{ "boss_layer7", "boss_layer7_p2", "boss_layer7_p3", "boss_layer7_p4" };
            for (int i = 0; i < ids.Length && i < n.Length && i < hpSum.Length; i++)
            {
                _phaseN.TryGetValue(ids[i], out var c); n[i] = c;
                _phaseHp.TryGetValue(ids[i], out var h); hpSum[i] = h;
            }
        }
        public static void ResetPhaseEntry() { _phaseN.Clear(); _phaseHp.Clear(); _phaseHpMin.Clear(); }

        // ---- 7層 全段: 遺物の「出現数」と「その遺物が出ていたターンでプレイヤーが死んだ数」 ----
        private static readonly System.Collections.Generic.Dictionary<string,int> _relicSeen
            = new System.Collections.Generic.Dictionary<string,int>();
        private static readonly System.Collections.Generic.Dictionary<string,int> _relicFatal
            = new System.Collections.Generic.Dictionary<string,int>();
        public static long v7Turns, v7Deaths;

        /// <summary>7層のターンごとに、 そのターン引かれていた遺物を数える。</summary>
        public static void V7Turn(System.Collections.Generic.List<string> relics)
        {
            v7Turns++;
            if (relics == null) return;
            foreach (var r in relics) { _relicSeen.TryGetValue(r, out var c); _relicSeen[r] = c + 1; }
        }

        /// <summary>そのターンにプレイヤーが死んだ場合、 出ていた遺物を致死側にも数える。</summary>
        public static void V7Death(System.Collections.Generic.List<string> relics)
        {
            v7Deaths++;
            if (relics == null) return;
            foreach (var r in relics) { _relicFatal.TryGetValue(r, out var c); _relicFatal[r] = c + 1; }
        }

        public static void P4Turn(bool blocked, bool execArmed, int bite,
                                  System.Collections.Generic.List<string> relics)
        {
            p4Turns++;
            if (blocked) p4Blocked++;
            if (execArmed) p4Execute++;
            p4MaxHpBite += bite;
            if (relics != null)
                foreach (var r in relics)
                { _relic.TryGetValue(r, out var c); _relic[r] = c + 1; }
        }

        /// <summary>そのボス戦で経過した「全ターン」を数える (攻撃が発生しなかったターンも含む)。
        /// Record() は攻撃解決の中でしか呼ばれないため、 turns だけでは
        /// 「1 攻撃あたりの威力」と「1 ターンあたりの威力」を取り違える。 分母を分けて持つ。</summary>
        public static void RecordTurn(string bossId, string displayName)
        {
            if (string.IsNullOrEmpty(bossId)) return;
            if (!_rows.TryGetValue(bossId, out var r)) { r = new Row(); _rows[bossId] = r; _names[bossId] = displayName; }
            r.allTurns++;
        }

        public static void Record(string bossId, string displayName, int mainDmg, int fixedDmg)
        {
            if (string.IsNullOrEmpty(bossId)) return;
            if (!_rows.TryGetValue(bossId, out var r)) { r = new Row(); _rows[bossId] = r; _names[bossId] = displayName; }
            r.turns++;
            r.mainSum  += mainDmg;
            r.fixedSum += fixedDmg;
            if (mainDmg > r.mainMax) r.mainMax = mainDmg;
        }

        // ---- 上振れヒットの実ログ (閾値超えを先着 N 件だけ全文保存) ----
        public const int SpikeThreshold = 400;
        private const int MaxSpikes = 12;
        private static readonly System.Collections.Generic.List<string> _spikes
            = new System.Collections.Generic.List<string>();

        public static bool WantSpike(int dmg) => dmg >= SpikeThreshold && _spikes.Count < MaxSpikes;
        public static void AddSpike(string line) { if (_spikes.Count < MaxSpikes) _spikes.Add(line); }

        public static string Dump()
        {
            if (_rows.Count == 0) return "BOSSDMG: 記録なし";
            string nl = System.Environment.NewLine;
            var sb = new System.Text.StringBuilder();
            sb.Append($"{"ボス",-16}{"全T",7}{"攻撃T",7}{"攻撃率",8}{"主/攻撃",9}{"主/全T",9}{"最大",7}").Append(nl);
            sb.Append(new string('-', 64)).Append(nl);
            var keys = new System.Collections.Generic.List<string>(_rows.Keys);
            keys.Sort();
            foreach (var k in keys)
            {
                var r = _rows[k];
                double t = System.Math.Max(1, r.turns);
                string nm = _names.TryGetValue(k, out var v) ? v : k;
                if (nm.Length > 14) nm = nm.Substring(0, 14);
                double at = System.Math.Max(1, r.allTurns);
                sb.Append($"{nm,-16}{r.allTurns,7}{r.turns,7}{r.turns / at,8:P0}"
                        + $"{(r.mainSum + r.fixedSum) / t,9:F1}{(r.mainSum + r.fixedSum) / at,9:F1}{r.mainMax,7}").Append(nl);
            }
            return sb.ToString();
        }
    }
}

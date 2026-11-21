using System.Collections.Generic;
using MetaProgression;

namespace AutoTest
{
    /// <summary>
    /// 測定で当てる整備パネルの構成 (2026-11-20 刷新・docs/GAME.md §15-1)。 基礎整備の段・開いた枠の数・枠ごとのモジュール。
    /// <para>枠のモジュール: -2 = 既定で埋める (<see cref="MetaAllocationPresets.DefaultModule"/>)・-1 = 空・0 以上 = <see cref="MetaModule"/>。
    /// 枠は開いた数の分だけ効く (開く順は <see cref="MetaSlot"/> の並び = 主方針 → 補助 → 種火)。</para>
    /// </summary>
    public sealed class MetaLoadout
    {
        public const int Default = -2, Empty = -1;
        public int baseTier;
        public bool baseDisabled;
        public int slots;
        public int[] modules = { Default, Default, Default };

        /// <summary>3 段階 (system-overhaul-spec §1-3 の検証ゲート): none = 未解禁 (基礎 0・枠 0) / mid = 途中 (基礎 3・枠 2) / full = 完成 (基礎 6・枠 3)。</summary>
        public static MetaLoadout Stage(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "none": return new MetaLoadout();
                case "mid":  return new MetaLoadout { baseTier = 3, slots = 2 };
                case "full": return new MetaLoadout { baseTier = MetaPanel.BaseTierCount, slots = 3 };
                default: return null;
            }
        }

        /// <summary>旧い 24 点の配点 (トラック → 段) を今の構造へ読み替える。 表は docs/GAME.md §15-1「旧い測定の指定の読み替え」。
        /// 基礎整備 = 合計の点 ÷ 4 (24 点 → 6 段・12 点 → 3 段)・枠 = 24 点以上 3 / 12 点以上 2 / 1 点以上 1 / 0 点 0・
        /// 主方針 = r10 のトラックの極点 (無ければ既定)・種火 = 段のある種火 (無ければ既定)・補助 = 段のある端子 (無ければ既定)。</summary>
        public static MetaLoadout FromLegacy(Dictionary<MetaPanelKind, int> ranks)
        {
            var lo = new MetaLoadout();
            if (ranks == null) return lo;
            int total = 0;
            foreach (var kv in ranks) total += MetaAllocationPresets.LegacyCostOf(kv.Key, kv.Value);
            lo.baseTier = System.Math.Min(MetaPanel.BaseTierCount, total / 4);
            lo.slots = total >= MetaAllocationPresets.LegacyMaxPoints ? 3 : total >= 12 ? 2 : total > 0 ? 1 : 0;
            foreach (var kv in ranks)
            {
                MetaModule? m = null;
                if (kv.Value >= 10) m = LegacyKeystone(kv.Key);
                else if (kv.Value >= 1) m = kv.Key switch
                {
                    MetaPanelKind.SparkCharge => MetaModule.SparkCharge, MetaPanelKind.SparkRinkai => MetaModule.SparkRinkai,
                    MetaPanelKind.SparkPoison => MetaModule.SparkPoison, MetaPanelKind.SparkBleed => MetaModule.SparkBleed,
                    MetaPanelKind.SparkShield => MetaModule.SparkShield, MetaPanelKind.SparkCrit => MetaModule.SparkCrit,
                    MetaPanelKind.AttackTerm => MetaModule.AttackTune, MetaPanelKind.BlockTerm => MetaModule.BlockTune,
                    _ => (MetaModule?)null,
                };
                if (m == null) continue;
                int slot = (int)MetaPanel.SlotOf(m.Value);
                if (lo.modules[slot] == Default) lo.modules[slot] = (int)m.Value;   // 同じ枠に 2 つ来たら enum の並びで先の方
            }
            return lo;
        }

        /// <summary>旧いトラックの r10 の極点 → 主方針のモジュール。</summary>
        public static MetaModule? LegacyKeystone(MetaPanelKind k) => k switch
        {
            MetaPanelKind.Shell => MetaModule.LastStand, MetaPanelKind.Output => MetaModule.BattleSpirit,
            MetaPanelKind.Guard => MetaModule.Valor, MetaPanelKind.Vault => MetaModule.VaultBank,
            MetaPanelKind.Plunder => MetaModule.Robbery, MetaPanelKind.Trade => MetaModule.Sale99,
            MetaPanelKind.Supply => MetaModule.StartPick, MetaPanelKind.Armor => MetaModule.Immovable,
            _ => (MetaModule?)null,
        };

        public string Describe()
        {
            var parts = new List<string> { "base:" + baseTier + (baseDisabled ? "(切)" : ""), "slots:" + slots };
            foreach (MetaSlot sl in System.Enum.GetValues(typeof(MetaSlot)))
            {
                int m = modules[(int)sl];
                parts.Add(sl.ToString().ToLowerInvariant() + ":" + (m == Default ? "既定" : m == Empty ? "none" : ((MetaModule)m).ToString()));
            }
            return string.Join(",", parts);
        }
    }

    /// <summary>
    /// 測定で整備パネルの構成を当てる口 (2026-11-20 刷新)。 正本: docs/GAME.md §15-1。
    /// <para>旧い 24 点の配分のプリセット (<see cref="Preset"/>・<see cref="Ranks"/>) と旧い指定 (<c>"Shell:10,Output:3"</c>) は残し、
    /// 当てる時に <see cref="MetaLoadout.FromLegacy"/> で今の構造へ読み替える ── 旧いスイープ・メニュー・並列ランナーの指定がそのまま走る。</para>
    /// </summary>
    public static class MetaAllocationPresets
    {
        public enum Preset
        {
            /// <summary>0pt。 → 未解禁 (基礎 0・枠 0)。</summary>
            None,
            /// <summary>旧: 予算を初めて使い切った標準形 (数値 8 本 × 3pt)。 → 完成 (基礎 6・枠 3・モジュールは既定)。 <b>Standard の既定。</b></summary>
            Balanced,
            /// <summary>旧: 出力 r10 ほか。 → 完成・主方針〈戦意〉。</summary>
            Offense,
            /// <summary>旧: 外殻 r10 ほか。 → 完成・主方針〈ラストスタンド〉。</summary>
            Defense,
            /// <summary>旧: 金庫 r10 ほか。 → 完成・主方針〈貸金庫〉。</summary>
            Economy,
            /// <summary>旧: 種火・出血 r3 ほか。 → 完成・種火 出血。</summary>
            SparkBuild,
        }

        /// <summary>旧い配点式の予算 (点)。 読み替えの「完成」のしきい値と旧いスイープの表示にだけ使う。</summary>
        public const int LegacyMaxPoints = 24;

        /// <summary>旧い配点式の 1 段の点 (種火 2・端子 3・数値 1)。</summary>
        public static int LegacyRankCost(MetaPanelKind kind) => kind switch
        {
            MetaPanelKind.SparkCharge or MetaPanelKind.SparkRinkai or MetaPanelKind.SparkPoison or
            MetaPanelKind.SparkBleed or MetaPanelKind.SparkCrit or MetaPanelKind.SparkShield => 2,
            _ => kind.MaxRank() <= 3 ? 3 : 1,
        };

        public static int LegacyCostOf(MetaPanelKind kind, int rank) => System.Math.Max(0, rank) * LegacyRankCost(kind);

        /// <summary>枠を既定で埋める時のモジュール = 測定の標準 (旧 Standard と同じく軸に寄せない)。 主方針〈ラストスタンド〉(安全網)・補助 攻撃端子調律 (どのビルドにも効く)・
        /// 種火は空 (<see cref="MetaLoadout.Empty"/>)。 2026-11-21: 種火 出血を既定にしていたら、 どの腕でも店が出血へ傾き、 軸どうしの比較が出血に有利に偏った。
        /// 種火を測る時は spark:&lt;キーワード&gt; を明示する。</summary>
        public static int DefaultModule(MetaSlot slot) => slot switch
        {
            MetaSlot.Main => (int)MetaModule.LastStand,
            MetaSlot.Support => (int)MetaModule.AttackTune,
            _ => MetaLoadout.Empty,
        };

        /// <summary>旧いプリセットの配分表 (合計は <see cref="LegacyMaxPoints"/>)。 当てる時は <see cref="MetaLoadout.FromLegacy"/> で読み替わる。</summary>
        public static Dictionary<MetaPanelKind, int> Ranks(Preset p)
        {
            switch (p)
            {
                case Preset.Balanced:
                    return new Dictionary<MetaPanelKind, int> {
                    { MetaPanelKind.Shell, 3 }, { MetaPanelKind.Output, 3 }, { MetaPanelKind.Guard, 3 },
                    { MetaPanelKind.Vault, 3 }, { MetaPanelKind.Plunder, 3 }, { MetaPanelKind.Trade, 3 },
                    { MetaPanelKind.Supply, 3 }, { MetaPanelKind.Armor, 3 },
                };
                case Preset.Offense: return new Dictionary<MetaPanelKind, int> {
                    { MetaPanelKind.Output, 10 }, { MetaPanelKind.Shell, 5 }, { MetaPanelKind.Guard, 4 },
                    { MetaPanelKind.Vault, 3 }, { MetaPanelKind.Supply, 2 },
                };
                case Preset.Defense: return new Dictionary<MetaPanelKind, int> {
                    { MetaPanelKind.Shell, 10 }, { MetaPanelKind.Guard, 8 },
                    { MetaPanelKind.Output, 3 }, { MetaPanelKind.Supply, 3 },
                };
                case Preset.Economy: return new Dictionary<MetaPanelKind, int> {
                    { MetaPanelKind.Vault, 10 }, { MetaPanelKind.Plunder, 7 }, { MetaPanelKind.Trade, 7 },
                };
                case Preset.SparkBuild: return new Dictionary<MetaPanelKind, int> {
                    { MetaPanelKind.SparkBleed, 3 }, { MetaPanelKind.Shell, 5 },
                    { MetaPanelKind.Output, 5 }, { MetaPanelKind.Guard, 5 },
                };
                default: return new Dictionary<MetaPanelKind, int>();
            }
        }

        /// <summary>プリセットをメタ状態へ当てる (ResetAll の後に読み替えて当てる)。</summary>
        public static void Apply(Preset p)
        {
            var mgr = MetaProgressManager.Instance;
            if (mgr == null) return;
            mgr.ResetAll();
            if (p == Preset.None) return;
            ApplyLoadout(MetaLoadout.FromLegacy(Ranks(p)), p.ToString());
        }

        /// <summary>構成をメタ状態へ当てる (トークンは払わない)。 <b>ResetAll は呼ばない</b> ── 呼び出し側が先に呼ぶ。</summary>
        public static void ApplyLoadout(MetaLoadout lo, string label)
        {
            var st = MetaProgressManager.Instance?.State;
            if (st == null || lo == null) return;
            st.baseTier = System.Math.Max(0, System.Math.Min(MetaPanel.BaseTierCount, lo.baseTier));
            st.baseDisabled = lo.baseDisabled;
            st.slotsOpen = System.Math.Max(0, System.Math.Min(MetaPanel.SlotOpenCost.Length, lo.slots));
            foreach (MetaSlot sl in System.Enum.GetValues(typeof(MetaSlot)))
            {
                int m = lo.modules[(int)sl];
                if (m == MetaLoadout.Default) m = st.IsSlotOpen(sl) ? DefaultModule(sl) : MetaLoadout.Empty;
                if (m < 0) { st.Unequip(sl); continue; }
                if (!st.IsSlotOpen(sl))
                    UnityEngine.Debug.LogWarning($"[MetaAllocationPresets] {label}: {(MetaModule)m} の枠 ({sl}) が閉じている (枠 {st.slotsOpen}) ので効かない");
                st.ForceEquip((MetaModule)m);
            }
            st.EnsurePanelInitialized();
            UnityEngine.Debug.Log($"[MetaAllocationPresets] {label} を適用: {st.DescribePanel()}");
        }

        private static readonly HashSet<string> LoadoutKeys = new HashSet<string> { "stage", "base", "baseoff", "slots", "main", "support", "spark" };

        /// <summary>測定の指定 (AutoRunner.metaRankSpec・並列ランナーの rankSpec) を構成へ。 解釈できなければ null (呼び手が止める)。
        /// <para><b>今の構造の書き方</b>: <c>"stage:mid,main:Valor"</c>・<c>"base:6,slots:3,main:LastStand,support:Compression,spark:bleed"</c>・<c>"stage:full,baseoff:1"</c>。
        /// stage (none/mid/full) を先に当て、 残りの鍵で上書きする。 枠の値は MetaModule の名前・種火はキーワード (bleed など) も可・none = 空。 書かなかった枠は既定で埋める。</para>
        /// <para><b>旧い書き方</b> (<c>"Shell:10,Output:3"</c>) は <see cref="MetaLoadout.FromLegacy"/> で読み替える。 2 つを混ぜたら旧い方の鍵は捨てて警告する。</para></summary>
        public static MetaLoadout ParseSpec(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            var toks = new List<(string k, string v)>();
            bool newStyle = false;
            foreach (var tok in spec.Split(','))
            {
                var kv = tok.Split(':');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim(), v = kv[1].Trim();
                toks.Add((k, v));
                if (LoadoutKeys.Contains(k.ToLowerInvariant())) newStyle = true;
            }
            if (!newStyle)
            {
                var d = new Dictionary<MetaPanelKind, int>();
                foreach (var (k, v) in toks)
                {
                    if (!System.Enum.TryParse(k, true, out MetaPanelKind kind))
                    { UnityEngine.Debug.LogWarning($"[MetaAllocationPresets] 未知の軸名: {k}"); continue; }
                    if (int.TryParse(v, out int r) && r > 0) d[kind] = r;
                }
                return d.Count > 0 ? MetaLoadout.FromLegacy(d) : null;
            }

            var lo = new MetaLoadout();
            foreach (var (k, v) in toks)
                if (k.ToLowerInvariant() == "stage")
                {
                    lo = MetaLoadout.Stage(v);
                    if (lo == null) { UnityEngine.Debug.LogWarning($"[MetaAllocationPresets] 未知の stage: {v} (none/mid/full)"); return null; }
                }
            foreach (var (k, v) in toks)
            {
                switch (k.ToLowerInvariant())
                {
                    case "stage": break;
                    case "base": if (!int.TryParse(v, out lo.baseTier)) return Bad(k, v); break;
                    case "slots": if (!int.TryParse(v, out lo.slots)) return Bad(k, v); break;
                    case "baseoff": lo.baseDisabled = v == "1" || v.ToLowerInvariant() == "true"; break;
                    case "main": if (!SetModule(lo, MetaSlot.Main, v)) return Bad(k, v); break;
                    case "support": if (!SetModule(lo, MetaSlot.Support, v)) return Bad(k, v); break;
                    case "spark": if (!SetModule(lo, MetaSlot.Spark, v)) return Bad(k, v); break;
                    default: UnityEngine.Debug.LogWarning($"[MetaAllocationPresets] 今の構造の指定に旧い鍵が混ざっている (捨てる): {k}"); break;
                }
            }
            return lo;
        }

        private static MetaLoadout Bad(string k, string v)
        {
            UnityEngine.Debug.LogWarning($"[MetaAllocationPresets] 解釈できない指定: {k}:{v}");
            return null;
        }

        /// <summary>枠の値 (none / MetaModule の名前 / 種火のキーワード) を入れる。 枠違いのモジュールは false。</summary>
        private static bool SetModule(MetaLoadout lo, MetaSlot slot, string v)
        {
            if (v.ToLowerInvariant() == "none") { lo.modules[(int)slot] = MetaLoadout.Empty; return true; }
            if (!System.Enum.TryParse(v, true, out MetaModule m))
            {
                bool found = false;
                foreach (MetaModule x in System.Enum.GetValues(typeof(MetaModule)))
                    if (MetaPanel.SparkKeyword(x) == v.ToLowerInvariant()) { m = x; found = true; break; }
                if (!found) return false;
            }
            if (MetaPanel.SlotOf(m) != slot) return false;
            lo.modules[(int)slot] = (int)m;
            return true;
        }
    }
}

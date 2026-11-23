using System.Collections.Generic;
using MetaProgression;

namespace AutoTest
{
    /// <summary>
    /// 測定で当てる整備盤の構成 (2026-11-23・docs/GAME.md §15-1): 開いたマスと置き方。 パネルは持たせてから置く (トークンは払わない)。
    /// <para>代表の盤 (<see cref="Board"/>・neon answer24 §3): empty (0/9) / s3 (3/9・攻撃端子調律 I2 + 外装プレート 1×1) / s5 (5/9 十字・ラストスタンド T + 外装プレート) /
    /// s6 (6/9 角 1・ラストスタンド T + 攻撃端子調律) / standard (9/9 標準の完成盤・ラストスタンド T + 重装フレーム L3 + 攻撃端子調律 I2)。</para>
    /// </summary>
    public sealed class MetaLoadout
    {
        public const int AllOpen = (1 << MetaBoard.Cells) - 1;
        public int openMask;
        public List<BoardPlacement> placements = new List<BoardPlacement>();

        private static int Mask(params int[] cells) { int m = 0; foreach (int c in cells) m |= 1 << c; return m; }

        // 置き方 (row, col, rot は MetaBoard.Rotated の時計回りの回数)
        private static BoardPlacement P(string id, int row, int col, int rot) => new BoardPlacement(id, rot, row, col);
        private const string CoreLastStand = "core_last_stand", HeavyFrame = "mod_heavy_frame", AtkTune = "unit_atk_tune", ChipHp = "chip_hp", Caravan = "mod_caravan";

        /// <summary>代表の盤。 知らない名前なら null。 full は standard と同じ。</summary>
        public static MetaLoadout Board(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "empty": case "none": return new MetaLoadout();
                case "s3":       // 中心 + 上 + 下: 攻撃端子調律 (縦・上と中心) + 外装プレート (下)
                    return new MetaLoadout { openMask = Mask(4, 1, 7), placements = { P(AtkTune, 0, 1, 1), P(ChipHp, 2, 1, 0) } };
                case "s5":       // 十字: ラストスタンド (横の 3 マス + 上) + 外装プレート (下)
                    return new MetaLoadout { openMask = Mask(4, 1, 3, 5, 7), placements = { P(CoreLastStand, 0, 0, 2), P(ChipHp, 2, 1, 0) } };
                case "s6":       // 十字 + 左下: ラストスタンド + 攻撃端子調律 (下と左下)
                    return new MetaLoadout { openMask = Mask(4, 1, 3, 5, 7, 6), placements = { P(CoreLastStand, 0, 0, 2), P(AtkTune, 2, 0, 0) } };
                case "standard": case "full":   // 9/9: T (上の行 + 中心) + L3 (左下) + I2 (右の縦 2 マス)
                    return new MetaLoadout { openMask = AllOpen, placements = { P(CoreLastStand, 0, 0, 0), P(HeavyFrame, 1, 0, 0), P(AtkTune, 1, 2, 1) } };
                default: return null;
            }
        }

        /// <summary>旧い指定 stage:none / mid / full の読み替え: none = 空の盤・mid = 5/9 (十字)・full = 標準の完成盤。</summary>
        public static MetaLoadout Stage(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "none": return Board("empty");
                case "mid": return Board("s5");
                case "full": return Board("standard");
                default: return null;
            }
        }

        /// <summary>旧い 24 点の配点 (トラック → 段) の読み替え (旧いスイープの指定がそのまま走るように)。
        /// 合計 24 点以上 = 標準の完成盤 (r10 の極点があればその T のコアに差し替え)・12 点以上 = 5/9・1 点以上 = 3/9・0 点 = 空。 種火は読まない。</summary>
        public static MetaLoadout FromLegacy(Dictionary<MetaPanelKind, int> ranks)
        {
            int total = 0;
            if (ranks != null) foreach (var kv in ranks) total += MetaAllocationPresets.LegacyCostOf(kv.Key, kv.Value);
            var lo = Board(total >= MetaAllocationPresets.LegacyMaxPoints ? "standard" : total >= 12 ? "s5" : total > 0 ? "s3" : "empty");
            if (ranks == null) return lo;
            foreach (var kv in ranks)
            {
                if (kv.Value < 10) continue;
                var m = LegacyKeystone(kv.Key);
                if (m.HasValue) { lo.SwapPanel("T", m.Value); break; }
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

        /// <summary>そのモジュールを持つ、 形 <paramref name="shape"/> のパネルの id (無ければ null)。</summary>
        public static string PanelOf(MetaModule m, string shape)
        {
            foreach (var d in MetaBoard.All) if (d.Module == m && (shape == null || d.shape == shape)) return d.id;
            return null;
        }

        /// <summary>置いている形 <paramref name="shape"/> のパネルを、 同じ位置のまま <paramref name="m"/> のパネルへ差し替える。 無ければ false。</summary>
        public bool SwapPanel(string shape, MetaModule m)
        {
            string id = PanelOf(m, shape);
            if (id == null) return false;
            for (int i = 0; i < placements.Count; i++)
                if (MetaBoard.Get(placements[i].id)?.shape == shape) { var p = placements[i]; placements[i] = new BoardPlacement(id, p.rot, p.row, p.col); return true; }
            return false;
        }

        public string Describe() => MetaBoard.Describe(openMask, placements);
    }

    /// <summary>
    /// 測定で整備盤の構成を当てる口 (2026-11-23・docs/GAME.md §15-1)。
    /// <para>旧い 24 点の配分のプリセット (<see cref="Preset"/>・<see cref="Ranks"/>) と旧い指定 (<c>"Shell:10,Output:3"</c>・<c>"stage:full"</c>) は残し、
    /// 当てる時に盤へ読み替える ── 旧いスイープ・メニュー・並列ランナーの指定がそのまま走る。</para>
    /// </summary>
    public static class MetaAllocationPresets
    {
        public enum Preset
        {
            /// <summary>0pt。 → 空の盤。</summary>
            None,
            /// <summary>旧: 予算を初めて使い切った標準形。 → 標準の完成盤。 <b>Standard の既定。</b></summary>
            Balanced,
            /// <summary>旧: 出力 r10 ほか。 → 完成・〈戦意〉の T。</summary>
            Offense,
            /// <summary>旧: 外殻 r10 ほか。 → 完成・〈ラストスタンド〉の T。</summary>
            Defense,
            /// <summary>旧: 金庫 r10 ほか。 → 完成・〈貸金庫〉の T。</summary>
            Economy,
            /// <summary>旧: 種火・出血 r3 ほか。 → 完成 (種火は読まない)。</summary>
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

        /// <summary>構成をメタ状態へ当てる (マスを開き・パネルを持たせて置く・トークンは払わない)。 <b>ResetAll は呼ばない</b> ── 呼び出し側が先に呼ぶ。
        /// 置けなかったパネル (閉じたマス・重なり) は警告を出す (実効状態の盤の文で確かめる)。</summary>
        public static void ApplyLoadout(MetaLoadout lo, string label)
        {
            var st = MetaProgressManager.Instance?.State;
            if (st == null || lo == null) return;
            st.ForceBoard(lo.openMask, lo.placements);
            if (st.ValidPlacements.Count != lo.placements.Count)
                UnityEngine.Debug.LogWarning($"[MetaAllocationPresets] {label}: 置けないパネルがある (指定 {lo.Describe()} / 効いている {st.DescribeBoard()})");
            UnityEngine.Debug.Log($"[MetaAllocationPresets] {label} を適用: {st.DescribeBoard()}");
        }

        private static readonly HashSet<string> BoardKeys = new HashSet<string> { "stage", "board", "open", "place", "main", "support", "spark" };

        /// <summary>測定の指定 (AutoRunner.metaRankSpec・並列ランナーの rankSpec) を盤へ。 解釈できなければ null (呼び手が止める)。
        /// <para><b>書き方</b> (, で並べる): <c>board:standard</c> (代表の盤 empty / s3 / s5 / s6 / standard)・<c>stage:full</c> (旧い書き方・none = 空 / mid = s5 / full = standard)・
        /// <c>open:all</c> か <c>open:511</c> (開いたマスの bit・中心 = 16)・<c>place:core_valor@0.0r0</c> (パネルの id @ 行.列 r 回転・1 つ目の place で代表の盤の置き方を捨てる)・
        /// <c>main:Valor</c> / <c>support:BlockTune</c> (置いている T / I2 を同じ位置のままそのモジュールのパネルへ差し替える)・
        /// <c>spark:poison</c> (種火の盤へ: 2×2 の種火 (左上) + 攻撃端子調律 (右の縦 2 マス) + 商隊ネットワーク (下の行)・answer23 の P6)。</para>
        /// <para><b>旧い書き方</b> (<c>"Shell:10,Output:3"</c>) は <see cref="MetaLoadout.FromLegacy"/> で読み替える。</para></summary>
        public static MetaLoadout ParseSpec(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            var toks = new List<(string k, string v)>();
            bool newStyle = false;
            foreach (var tok in spec.Split(','))
            {
                int colon = tok.IndexOf(':');
                if (colon <= 0) continue;
                string k = tok.Substring(0, colon).Trim(), v = tok.Substring(colon + 1).Trim();
                toks.Add((k, v));
                if (BoardKeys.Contains(k.ToLowerInvariant())) newStyle = true;
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

            var lo = MetaLoadout.Board("standard");
            foreach (var (k, v) in toks)
            {
                string key = k.ToLowerInvariant();
                if (key != "stage" && key != "board") continue;
                lo = key == "stage" ? MetaLoadout.Stage(v) : MetaLoadout.Board(v);
                if (lo == null) { UnityEngine.Debug.LogWarning($"[MetaAllocationPresets] 未知の盤: {k}:{v}"); return null; }
            }
            bool placedExplicit = false;
            foreach (var (k, v) in toks)
            {
                switch (k.ToLowerInvariant())
                {
                    case "stage": case "board": break;
                    case "open":
                        if (v.ToLowerInvariant() == "all") lo.openMask = MetaLoadout.AllOpen;
                        else if (int.TryParse(v, out int om)) lo.openMask = om & MetaLoadout.AllOpen;
                        else return Bad(k, v);
                        break;
                    case "place":
                    {
                        var p = ParsePlacement(v);
                        if (p == null) return Bad(k, v);
                        if (!placedExplicit) { lo.placements.Clear(); placedExplicit = true; }
                        lo.placements.Add(p);
                        break;
                    }
                    case "main":
                        if (v.ToLowerInvariant() == "none") { lo.placements.RemoveAll(q => MetaBoard.Get(q.id)?.shape == "T"); break; }
                        if (!System.Enum.TryParse(v, true, out MetaModule mm) || !lo.SwapPanel("T", mm)) return Bad(k, v);
                        break;
                    case "support":
                        if (v.ToLowerInvariant() == "none") { lo.placements.RemoveAll(q => MetaBoard.Get(q.id)?.shape == "I2"); break; }
                        if (!System.Enum.TryParse(v, true, out MetaModule sm) || !lo.SwapPanel("I2", sm)) return Bad(k, v);
                        break;
                    case "spark":
                    {
                        if (v.ToLowerInvariant() == "none") break;
                        var m = SparkOf(v);
                        string id = m.HasValue ? MetaLoadout.PanelOf(m.Value, "O") : null;
                        if (id == null) return Bad(k, v);
                        lo.openMask = MetaLoadout.AllOpen;
                        lo.placements = new List<BoardPlacement>
                        {
                            new BoardPlacement(id, 0, 0, 0),                 // 2×2: 左上 (中心を含む)
                            new BoardPlacement("unit_atk_tune", 1, 0, 2),    // I2: 右の縦 2 マス (上)
                            new BoardPlacement("mod_caravan", 0, 2, 0),      // I3: 下の行
                        };
                        break;
                    }
                    default: UnityEngine.Debug.LogWarning($"[MetaAllocationPresets] 盤の指定に旧い鍵が混ざっている (捨てる): {k}"); break;
                }
            }
            return lo;
        }

        /// <summary>"id@行.列r回転" (例 "core_valor@0.0r0")。 回転は省くと 0。 知らない id なら null。</summary>
        public static BoardPlacement ParsePlacement(string v)
        {
            int at = v.IndexOf('@');
            if (at <= 0) return null;
            string id = v.Substring(0, at).Trim(), pos = v.Substring(at + 1).Trim();
            if (MetaBoard.Get(id) == null) return null;
            int rot = 0, r = pos.IndexOf('r');
            if (r >= 0) { if (!int.TryParse(pos.Substring(r + 1), out rot)) return null; pos = pos.Substring(0, r); }
            var rc = pos.Split('.');
            if (rc.Length != 2 || !int.TryParse(rc[0], out int row) || !int.TryParse(rc[1], out int col)) return null;
            return new BoardPlacement(id, rot, row, col);
        }

        /// <summary>種火のキーワード (bleed など) か MetaModule の名前 → 種火のモジュール。</summary>
        private static MetaModule? SparkOf(string v)
        {
            if (System.Enum.TryParse(v, true, out MetaModule m) && MetaPanel.SlotOf(m) == MetaSlot.Spark) return m;
            foreach (MetaModule x in System.Enum.GetValues(typeof(MetaModule)))
                if (MetaPanel.SparkKeyword(x) == v.ToLowerInvariant()) return x;
            return null;
        }

        private static MetaLoadout Bad(string k, string v)
        {
            UnityEngine.Debug.LogWarning($"[MetaAllocationPresets] 解釈できない指定: {k}:{v}");
            return null;
        }
    }
}

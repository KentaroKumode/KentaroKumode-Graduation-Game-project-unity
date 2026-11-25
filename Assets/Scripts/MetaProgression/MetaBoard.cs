using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaProgression
{
    /// <summary>パネル 1 枚が足す数値 (0 = 何もしない)。 正本は Resources/meta_board_panels.json の stats。 JsonUtility で読むので欄は public のまま。</summary>
    [Serializable]
    public class BoardStats
    {
        public int hp, defense, damagePct, saleSlots, gold;
        /// <summary>各戦闘の開始時の充電。</summary>
        public int chargeOpening;
        /// <summary>各戦闘の 1 ターン目のブロック。</summary>
        public int blockTurn1;
        /// <summary>会心率 (%)。</summary>
        public int critPct;
        /// <summary>秘宝の見送り換金 (%・切り上げ)。</summary>
        public int skipCashPct;
        /// <summary>各戦闘の 1 ターン目に多く引く端子の枚数。</summary>
        public int handTurn1;
        /// <summary>各戦闘で、 最初から数えて無料になる振り直しの回数。</summary>
        public int freeRerolls;
        /// <summary>0 層〈支給の秘宝〉の提示数の上乗せ。</summary>
        public int farmOffer;
        /// <summary>休憩の回復量 (%・回復量 × (1 + N/100))。</summary>
        public int restHealPct;
        /// <summary>前哨基地の物資の補給 (%・補給量 × (1 + N/100)・切り上げ)。</summary>
        public int outpostSupplyPct;
        /// <summary>地図の視界 (歩)。</summary>
        public int vision;
        /// <summary>店の武器の強化素材の値段 (−%・切り上げ)。</summary>
        public int materialCutPct;
        /// <summary>各層の最初の通常戦 N 回、 敵の攻撃 −<see cref="openerAtkCutPct"/>%。</summary>
        public int openerFights, openerAtkCutPct;
        /// <summary>出現の枠が 4 つ埋まった (ロック成立) 瞬間、 最大 HP +N・HP 全回復。</summary>
        public int lockHp;

        public void Add(BoardStats o)
        {
            if (o == null) return;
            hp += o.hp; defense += o.defense; damagePct += o.damagePct; saleSlots += o.saleSlots; gold += o.gold;
            chargeOpening += o.chargeOpening; blockTurn1 += o.blockTurn1; critPct += o.critPct; skipCashPct += o.skipCashPct;
            handTurn1 += o.handTurn1; freeRerolls += o.freeRerolls; farmOffer += o.farmOffer; restHealPct += o.restHealPct;
            outpostSupplyPct += o.outpostSupplyPct; vision += o.vision; materialCutPct += o.materialCutPct;
            openerFights = Math.Max(openerFights, o.openerFights); openerAtkCutPct = Math.Max(openerAtkCutPct, o.openerAtkCutPct);
            lockHp += o.lockHp;
        }
    }

    /// <summary>パネル 1 種の定義 (Resources/meta_board_panels.json の 1 行)。</summary>
    [Serializable]
    public class BoardPanelDef
    {
        public string id = "", name = "", shape = "1", source = "shop", achievement = "", module = "";
        /// <summary>系統 (survive / defense / attack / economy / wiring / explore / spark・2026-11-23)。 棚と盤のアイコンと盤の地色はここから引く (id から推測しない)。</summary>
        public string family = "";
        public int cost;
        public BoardStats stats = new BoardStats();

        public bool FromAchievement => source == "achievement";
        /// <summary>今の主方針・補助・種火の効果 (無ければ null)。</summary>
        public MetaModule? Module => !string.IsNullOrEmpty(module) && Enum.TryParse(module, out MetaModule m) ? m : (MetaModule?)null;
        public int CellCount => MetaBoard.ShapeCells(shape).Length;
    }

    /// <summary>盤に置いたパネル 1 枚 (セーブ)。 rot = 90 度回転の回数 (0〜3・時計回り)・row/col = 回した形の左上の位置。</summary>
    [Serializable]
    public class BoardPlacement
    {
        public string id = "";
        public int rot, row, col;
        public BoardPlacement() { }
        public BoardPlacement(string id, int rot, int row, int col) { this.id = id; this.rot = rot; this.row = row; this.col = col; }
    }

    [Serializable]
    internal class BoardCatalogFile { public List<BoardPanelDef> panels = new List<BoardPanelDef>(); }

    /// <summary>
    /// 整備盤 (2026-11-23・docs/GAME.md §15-1・neon answer23 / answer24): 3×3 の盤に 1〜4 マスのパネルを置く。
    /// 90 度回転可・反転なし・各パネル 1 枚・ラン前に無料で置き直し・場所の効果なし。 マスはトークンで開く (中心 → 辺 → 角・開けた個数で値段が上がる)。
    /// 状態は <see cref="MetaProgressState"/> (開いたマス・持っているパネル・置き方)、 効果は <see cref="MetaBuffApplicator"/> の getter が配る。
    /// パネルの正本は Resources/meta_board_panels.json (BOT・測定も同じ物を読む)。
    /// </summary>
    public static class MetaBoard
    {
        public const int Size = 3, Cells = Size * Size, Center = 4;

        /// <summary>辺 (上下左右) のマスの index。</summary>
        public static readonly int[] EdgeCells = { 1, 3, 5, 7 };
        /// <summary>角のマスの index。</summary>
        public static readonly int[] CornerCells = { 0, 2, 6, 8 };

        /// <summary>中心を開くトークン。</summary>
        public const int CenterCost = 1;
        /// <summary>辺を n 個目 (1 起点) に開くトークン (どの辺から開けても)。</summary>
        public static readonly int[] EdgeCost = { 40, 80, 140, 220 };
        /// <summary>角を n 個目に開くトークン (どの角から開けても)。</summary>
        public static readonly int[] CornerCost = { 320, 460, 640, 880 };

        public static bool IsEdge(int cell) => Array.IndexOf(EdgeCells, cell) >= 0;
        public static bool IsCorner(int cell) => Array.IndexOf(CornerCells, cell) >= 0;
        public static int CountOpen(int mask, int[] cells) { int n = 0; foreach (int c in cells) if ((mask & 1 << c) != 0) n++; return n; }
        public static int OpenCount(int mask) { int n = 0; for (int c = 0; c < Cells; c++) if ((mask & 1 << c) != 0) n++; return n; }

        /// <summary>マス <paramref name="cell"/> を開くトークン。 開けられない (開いている・順番が来ていない・範囲外) なら −1。
        /// 順番: 中心 → 辺 (中心が開いていれば) → 角 (辺が 4 つとも開いていれば)。</summary>
        public static int OpenCost(int mask, int cell)
        {
            if (cell < 0 || cell >= Cells || (mask & 1 << cell) != 0) return -1;
            if (cell == Center) return CenterCost;
            if ((mask & 1 << Center) == 0) return -1;
            if (IsEdge(cell)) return EdgeCost[CountOpen(mask, EdgeCells)];
            if (CountOpen(mask, EdgeCells) < EdgeCells.Length) return -1;
            return CornerCost[CountOpen(mask, CornerCells)];
        }

        // ============================================================
        //  形
        // ============================================================

        /// <summary>形の基本のマス (row, col)。 回す前・左上を (0,0) に寄せた形。</summary>
        public static (int r, int c)[] ShapeCells(string shape) => shape switch
        {
            "1"  => new[] { (0, 0) },
            "I2" => new[] { (0, 0), (0, 1) },
            "I3" => new[] { (0, 0), (0, 1), (0, 2) },
            "L3" => new[] { (0, 0), (1, 0), (1, 1) },
            "T"  => new[] { (0, 0), (0, 1), (0, 2), (1, 1) },
            "O"  => new[] { (0, 0), (0, 1), (1, 0), (1, 1) },
            "S"  => new[] { (0, 1), (0, 2), (1, 0), (1, 1) },
            "Z"  => new[] { (0, 0), (0, 1), (1, 1), (1, 2) },
            _    => Array.Empty<(int, int)>(),
        };

        /// <summary>形を時計回りに <paramref name="rot"/> 回 90 度回し、 左上を (0,0) に寄せたマス。</summary>
        public static (int r, int c)[] Rotated(string shape, int rot)
        {
            var cells = (((int r, int c)[])ShapeCells(shape).Clone());
            int k = ((rot % 4) + 4) % 4;
            for (int t = 0; t < k; t++)
                for (int i = 0; i < cells.Length; i++) cells[i] = (cells[i].c, -cells[i].r);   // (r,c) → (c,−r) = 時計回り
            int mr = int.MaxValue, mc = int.MaxValue;
            foreach (var p in cells) { mr = Math.Min(mr, p.r); mc = Math.Min(mc, p.c); }
            for (int i = 0; i < cells.Length; i++) cells[i] = (cells[i].r - mr, cells[i].c - mc);
            return cells;
        }

        /// <summary>置いたパネルのマス (index)。 盤からはみ出す・形を知らないなら null。</summary>
        public static int[] CellsOf(BoardPlacement p)
        {
            var def = p != null ? Get(p.id) : null;
            if (def == null) return null;
            var cells = Rotated(def.shape, p.rot);
            if (cells.Length == 0) return null;
            var idx = new int[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                int r = p.row + cells[i].r, c = p.col + cells[i].c;
                if (r < 0 || r >= Size || c < 0 || c >= Size) return null;
                idx[i] = r * Size + c;
            }
            return idx;
        }

        /// <summary>置き方の全部 (BOT・測定が「置ける所」を探す時に使う)。 回転で同じ形になる物は 1 つにまとめる。</summary>
        public static List<BoardPlacement> AllPlacements(string id)
        {
            var list = new List<BoardPlacement>();
            var def = Get(id);
            if (def == null) return list;
            var seen = new HashSet<int>();
            for (int rot = 0; rot < 4; rot++)
                for (int r = 0; r < Size; r++)
                    for (int c = 0; c < Size; c++)
                    {
                        var p = new BoardPlacement(id, rot, r, c);
                        var cells = CellsOf(p);
                        if (cells == null) continue;
                        int m = 0; foreach (int x in cells) m |= 1 << x;
                        if (seen.Add(m)) list.Add(p);
                    }
            return list;
        }

        public static int MaskOf(int[] cells) { int m = 0; if (cells != null) foreach (int x in cells) m |= 1 << x; return m; }

        // ============================================================
        //  パネルの定義 (Resources/meta_board_panels.json)
        // ============================================================

        private static Dictionary<string, BoardPanelDef> _byId;
        private static List<BoardPanelDef> _all;

        /// <summary>全パネル (ファイルの並び)。</summary>
        public static IReadOnlyList<BoardPanelDef> All { get { EnsureLoaded(); return _all; } }

        public static BoardPanelDef Get(string id)
        {
            EnsureLoaded();
            return id != null && _byId.TryGetValue(id, out var d) ? d : null;
        }

        /// <summary>実績 <paramref name="achievementId"/> で手に入るパネル (無ければ空)。</summary>
        public static IEnumerable<BoardPanelDef> ForAchievement(string achievementId)
        {
            foreach (var d in All) if (d.FromAchievement && d.achievement == achievementId) yield return d;
        }

        private static void EnsureLoaded()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, BoardPanelDef>();
            _all = new List<BoardPanelDef>();
            var ta = Resources.Load<TextAsset>("meta_board_panels");
            if (ta == null) { Debug.LogError("[MetaBoard] Resources/meta_board_panels.json が無い"); return; }
            var file = JsonUtility.FromJson<BoardCatalogFile>(ta.text);
            foreach (var d in file?.panels ?? new List<BoardPanelDef>())
            {
                if (d == null || string.IsNullOrEmpty(d.id)) continue;
                if (d.stats == null) d.stats = new BoardStats();
                if (ShapeCells(d.shape).Length == 0) { Debug.LogError($"[MetaBoard] 知らない形: {d.id} {d.shape}"); continue; }
                if (!string.IsNullOrEmpty(d.module) && d.Module == null) Debug.LogError($"[MetaBoard] 知らない module: {d.id} {d.module}");
                if (_byId.ContainsKey(d.id)) { Debug.LogError($"[MetaBoard] id が重複: {d.id}"); continue; }
                _byId[d.id] = d; _all.Add(d);
            }
        }

        // ============================================================
        //  置き方の検査と効果の合計
        // ============================================================

        /// <summary>置き方の一覧を検査して、 効く物だけを返す: 持っている・形が盤に収まる・開いたマスだけ・重ならない・同じパネルは 1 枚 (先の物を残す)。</summary>
        public static List<BoardPlacement> Valid(int openMask, ICollection<string> owned, List<BoardPlacement> placements)
        {
            var ok = new List<BoardPlacement>();
            if (placements == null) return ok;
            int used = 0; var ids = new HashSet<string>();
            foreach (var p in placements)
            {
                if (p == null || owned == null || !owned.Contains(p.id) || ids.Contains(p.id)) continue;
                int m = MaskOf(CellsOf(p));
                if (m == 0 || (m & ~openMask) != 0 || (m & used) != 0) continue;
                used |= m; ids.Add(p.id); ok.Add(p);
            }
            return ok;
        }

        /// <summary>効く置き方の数値の合計と、 効いているモジュール。</summary>
        public static BoardStats Sum(List<BoardPlacement> valid, HashSet<MetaModule> modulesOut)
        {
            var s = new BoardStats();
            modulesOut?.Clear();
            if (valid == null) return s;
            foreach (var p in valid)
            {
                var d = Get(p.id);
                if (d == null) continue;
                s.Add(d.stats);
                var m = d.Module;
                if (m.HasValue) modulesOut?.Add(m.Value);
            }
            return s;
        }

        /// <summary>置き方の短い文 (ログ・実効状態)。 例 "盤9/9 core_last_stand@0,0r0 mod_heavy_frame@1,0r0 unit_atk_tune@1,2r1"。</summary>
        public static string Describe(int openMask, List<BoardPlacement> valid)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("盤").Append(OpenCount(openMask)).Append('/').Append(Cells);
            if (valid == null || valid.Count == 0) { sb.Append(" (空)"); return sb.ToString(); }
            foreach (var p in valid) sb.Append(' ').Append(p.id).Append('@').Append(p.row).Append(',').Append(p.col).Append('r').Append(p.rot);
            return sb.ToString();
        }
    }
}

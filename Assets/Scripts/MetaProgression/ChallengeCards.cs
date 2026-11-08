using System.Collections.Generic;
using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// 挑戦カード (docs/GAME.md §23-25・第 1 段 2026-11-08)。 難易度 = ラン中にカードを引く回数。
    /// 層の移り目 (開始・1→2・2→3・3→4・4→5) で 2 枚から 1 枚を選び、 選んだカードのデバフとバフを両方受ける。
    ///
    /// <para><b>効果は既存の仕組みに載せる</b>。 デバフ = 挑戦の軸の Tier / Tier4 (<see cref="MetaDebuffApplicator.C"/> がカードから組み直した構成を返す)、
    /// バフ = 整備パネルの段の上乗せ (<see cref="MetaBuffApplicator"/> の段数に足す)。 数字はどちらも元の関数が持ち、 ここは写しを持たない。
    /// 同じ軸のデバフを重ねたら重い Tier 1 つが効く。 バフの段は足し算で、 トラックの上限で止まる。</para>
    ///
    /// <para><b>第 1 段で外したもの</b>: 〈遅い回復〉と Tier4〈破綻〉は作り直す予定なので配らない。 バフは<b>ラン中に毎回読み直している効果だけ</b>
    /// (ラン開始時にだけ効く 外殻・金庫・兵站・燈火 と、 種火 r3 の開幕の品は、 途中で段を足しても効かないので外す)。</para>
    /// </summary>
    public static class ChallengeCards
    {
        public const int MaxDifficulty = 10;
        /// <summary>引く場所の数 (開始・1→2・2→3・3→4・4→5)。 層 d (1..5) に入る時が場所 d-1。</summary>
        public const int Slots = 5;
        public static readonly string[] SlotNames = { "開始", "1→2", "2→3", "3→4", "4→5" };

        public sealed class Card
        {
            public string id;
            public int tier;
            /// <summary>重なりの判定に使う種類 (デバフ = 軸 / Tier4 の名前・バフ = 整備のトラック)。</summary>
            public string kind;
            public bool isBuff;
            public ChallengeAxis axis; public int axisTier; public bool isT4; public ChallengeT4 t4;
            public MetaPanelKind panel; public int ranks;
        }

        private static readonly List<Card> _all = new List<Card>();
        private static readonly Dictionary<string, Card> _byId = new Dictionary<string, Card>();

        private static void D(int tier, ChallengeAxis ax, int axTier)
            => Add(new Card { id = "D:" + ax + ":" + axTier, tier = tier, kind = ax.ToString(), axis = ax, axisTier = axTier });
        private static void T4(ChallengeT4 v)
            => Add(new Card { id = "D:T4:" + v, tier = 4, kind = "T4:" + v, isT4 = true, t4 = v });
        private static void B(int tier, MetaPanelKind k, int ranks)
            => Add(new Card { id = "B:" + k + ":" + ranks, tier = tier, kind = k.ToString(), isBuff = true, panel = k, ranks = ranks });
        private static void Add(Card c) { _all.Add(c); _byId[c.id] = c; }

        static ChallengeCards()
        {
            // ── デバフ: 挑戦の軸 (ChallengeCatalog) の Tier をそのまま使う。 カードの Tier ≒ 軸の Tier ──
            D(1, ChallengeAxis.長引く負傷, 1); D(1, ChallengeAxis.穴の空いた鞄, 1); D(1, ChallengeAxis.狂暴化, 1); D(1, ChallengeAxis.綻び, 1);
            D(1, ChallengeAxis.見放された運, 1); D(1, ChallengeAxis.通行料, 1); D(1, ChallengeAxis.絶望的な戦闘, 1);
            D(2, ChallengeAxis.長引く負傷, 2); D(2, ChallengeAxis.穴の空いた鞄, 2); D(2, ChallengeAxis.厚い皮膚, 2); D(2, ChallengeAxis.狂暴化, 2);
            D(2, ChallengeAxis.綻び, 2); D(2, ChallengeAxis.搾取経済, 2); D(2, ChallengeAxis.宿屋連合, 2); D(2, ChallengeAxis.絶望的な戦闘, 2);
            D(3, ChallengeAxis.長引く負傷, 3); D(3, ChallengeAxis.厚い皮膚, 3); D(3, ChallengeAxis.狂暴化, 3); D(3, ChallengeAxis.綻び, 3);
            D(3, ChallengeAxis.見放された運, 3); D(3, ChallengeAxis.搾取経済, 3); D(3, ChallengeAxis.絶望的な戦闘, 3); D(3, ChallengeAxis.天変地異, 3);
            T4(ChallengeT4.鋼の皮膚); T4(ChallengeT4.凶運); T4(ChallengeT4.破産); T4(ChallengeT4.最後の審判);

            // ── バフ: 整備パネルの段の上乗せ。 種火 (軸の向き) を主に、 汎用の地力は少なめ ──
            foreach (var k in Sparks) B(1, k, 1);
            B(1, MetaPanelKind.Output, 1); B(1, MetaPanelKind.Plunder, 3); B(1, MetaPanelKind.Trade, 2);
            foreach (var k in Sparks) B(2, k, 2);
            B(2, MetaPanelKind.AttackTerm, 1); B(2, MetaPanelKind.BlockTerm, 1); B(2, MetaPanelKind.Output, 2);
            B(3, MetaPanelKind.AttackTerm, 2); B(3, MetaPanelKind.BlockTerm, 2); B(3, MetaPanelKind.Output, 4);
            B(3, MetaPanelKind.Plunder, 6); B(3, MetaPanelKind.Trade, 5);
            B(4, MetaPanelKind.AttackTerm, 3); B(4, MetaPanelKind.BlockTerm, 3); B(4, MetaPanelKind.Output, 8);
        }

        private static readonly MetaPanelKind[] Sparks =
            { MetaPanelKind.SparkPoison, MetaPanelKind.SparkBleed, MetaPanelKind.SparkRinkai, MetaPanelKind.SparkCharge, MetaPanelKind.SparkShield };

        public static Card Get(string id) => id != null && _byId.TryGetValue(id, out var c) ? c : null;
        public static IReadOnlyList<Card> All => _all;

        // ============================================================
        //  難易度 → 引く場所と Tier
        // ============================================================

        /// <summary>難易度 d の引き方: (場所, Tier) の並び。 Tier は I×3 → II×3 → III×3 → IV×1 の順に、 前の場所から配る。
        /// 場所は 1 回ずつ前から埋め、 6 回目からは後ろの場所に 2 回目を足す (序盤ほど軽く、 深いほど重い)。</summary>
        public static List<(int slot, int tier)> Schedule(int difficulty)
        {
            int d = System.Math.Max(0, System.Math.Min(MaxDifficulty, difficulty));
            var tiers = new List<int>(); for (int i = 0; i < d; i++) tiers.Add(i < 3 ? 1 : i < 6 ? 2 : i < 9 ? 3 : 4);
            var slots = new int[Slots]; for (int i = 0; i < Slots; i++) slots[i] = i < d ? 1 : 0;
            for (int i = 0; i < d - Slots; i++) slots[Slots - 1 - i] = 2;
            var outp = new List<(int, int)>(); int idx = 0;
            for (int s = 0; s < Slots; s++) for (int c = 0; c < slots[s]; c++) outp.Add((s, tiers[idx++]));
            return outp;
        }

        /// <summary>層 depth (1..5) に入る時の場所。 それ以外は -1。</summary>
        public static int SlotOfDepth(int depth) => depth >= 1 && depth <= Slots ? depth - 1 : -1;

        // ============================================================
        //  配る (2 枚・表と裏の組)
        // ============================================================

        /// <summary>Tier の 2 組を配る。 返すのは "デバフの id|バフの id" の 2 つ。 同じ回の 2 組は表どうし・裏どうしの種類が別。
        /// すでに受けた種類は出やすさ 1/4 (同じ軸を何枚も重ねない)。 乱数は専用の鍵 (他の抽選の消費列を動かさない)。</summary>
        public static List<string> Deal(int tier, IReadOnlyList<string> picks)
        {
            var takenD = new HashSet<string>(); var takenB = new HashSet<string>();
            if (picks != null) foreach (var p in picks) { Split(p, out var dc, out var bc); if (dc != null) takenD.Add(dc.kind); if (bc != null) takenB.Add(bc.kind); }
            Card d1 = Draw(tier, false, takenD, null), d2 = Draw(tier, false, takenD, d1?.kind);
            Card b1 = Draw(tier, true, takenB, null), b2 = Draw(tier, true, takenB, b1?.kind);
            var outp = new List<string>(2);
            if (d1 != null && b1 != null) outp.Add(d1.id + "|" + b1.id);
            if (d2 != null && b2 != null) outp.Add(d2.id + "|" + b2.id);
            return outp;
        }

        private static Card Draw(int tier, bool buff, HashSet<string> taken, string exclude)
        {
            var pool = new List<Card>(); var w = new List<int>(); int tot = 0;
            foreach (var c in _all)
            {
                if (c.tier != tier || c.isBuff != buff || c.kind == exclude) continue;
                int wt = taken.Contains(c.kind) ? 1 : 4;
                pool.Add(c); w.Add(wt); tot += wt;
            }
            if (tot <= 0) return null;
            int r = GameRng.RangeAuto("challenge_card", 0, tot);
            for (int i = 0; i < pool.Count; i++) { r -= w[i]; if (r < 0) return pool[i]; }
            return pool[pool.Count - 1];
        }

        public static void Split(string pick, out Card deb, out Card buf)
        {
            deb = buf = null; if (string.IsNullOrEmpty(pick)) return;
            int bar = pick.IndexOf('|');
            if (bar < 0) { deb = Get(pick); return; }
            deb = Get(pick.Substring(0, bar)); buf = Get(pick.Substring(bar + 1));
        }

        // ============================================================
        //  効果: 受けたカードから挑戦の構成と整備の段を組み直す
        // ============================================================

        /// <summary>受けたカードのデバフから挑戦の構成を作る。 同じ軸は重い Tier 1 つ。 点数 = Tier の合計 + Tier4 × 4。</summary>
        public static ResolvedChallenge Resolve(IReadOnlyList<string> picks)
        {
            int axisCount = 0;
            foreach (ChallengeAxis a in System.Enum.GetValues(typeof(ChallengeAxis))) if ((int)a > axisCount) axisCount = (int)a;
            var tiers = new int[axisCount + 1];
            var t4 = new bool[System.Enum.GetValues(typeof(ChallengeT4)).Length + 1];
            int score = 0;
            if (picks != null)
                foreach (var p in picks)
                {
                    Split(p, out var d, out _);
                    if (d == null) continue;
                    if (d.isT4) { if (!t4[(int)d.t4]) { t4[(int)d.t4] = true; score += ChallengeCatalog.T4Points; } }
                    else if (d.axisTier > tiers[(int)d.axis]) tiers[(int)d.axis] = d.axisTier;
                }
            for (int i = 0; i < tiers.Length; i++) score += tiers[i];
            return new ResolvedChallenge(tiers, t4, score);
        }

        /// <summary>受けたカードのバフで、 その整備トラックに足す段数。</summary>
        public static int BonusRanks(IReadOnlyList<string> picks, MetaPanelKind k)
        {
            int n = 0;
            if (picks != null) foreach (var p in picks) { Split(p, out _, out var b); if (b != null && b.panel == k) n += b.ranks; }
            return n;
        }

        // ============================================================
        //  文面 (数字は元の関数から取る・写しを持たない)
        // ============================================================

        public static string Text(Card c)
        {
            if (c == null) return "";
            return c.isBuff ? BuffText(c.panel, c.ranks) : DebuffText(c);
        }

        private static string DebuffText(Card c)
        {
            // その 1 枚だけを受けた構成で、 元の関数に効果の値を聞く
            var prev = MetaDebuffApplicator.PreviewOverride;
            MetaDebuffApplicator.PreviewOverride = Resolve(new[] { c.id });
            try { return c.isT4 ? T4Effect(c.t4) : AxisEffect(c.axis); }
            finally { MetaDebuffApplicator.PreviewOverride = prev; }
        }

        /// <summary>軸の効果 (短く)。 数字は <see cref="MetaDebuffApplicator"/> が今の構成から返す値。</summary>
        public static string AxisEffect(ChallengeAxis a)
        {
            switch (a)
            {
                case ChallengeAxis.長引く負傷:   return "受けたダメージの " + (MetaDebuffApplicator.GetLingeringWoundRatio() * 100f).ToString("0") + "% だけ、 最大 HP が減っていく";
                case ChallengeAxis.穴の空いた鞄: return "消耗品は " + MetaDebuffApplicator.GetConsumableCarryCap() + " 個までしか持てない";
                case ChallengeAxis.遅い回復:     return "回復 ×" + MetaDebuffApplicator.GetHealMultiplier().ToString("0.00");
                case ChallengeAxis.厚い皮膚:     return "敵の最大 HP ×" + MetaDebuffApplicator.GetEnemyHpMultiplier().ToString("0.###");
                case ChallengeAxis.狂暴化:       return "敵の攻撃 ×" + MetaDebuffApplicator.GetEnemyDamageMultiplier().ToString("0.00");
                case ChallengeAxis.綻び:         return MetaDebuffApplicator.GetTerminalSealPeriod() + " ターンごとに、 端子が 1 つ封印される";
                case ChallengeAxis.見放された運:
                {
                    int n = 0; while (n < 50 && MetaDebuffApplicator.GetForsakenLuckDiceCount(n + 1) > 0) n++;
                    return "戦闘の最初の " + n + " ターン、 ダイス 1 本の出目が 1";
                }
                case ChallengeAxis.搾取経済:     return "店の値段 ×" + MetaDebuffApplicator.GetShopPriceMultiplier().ToString("0.00");
                case ChallengeAxis.通行料:       return "層を移るたび、 所持金の " + (MetaDebuffApplicator.GetFloorTollRatio() * 100f).ToString("0") + "% を失う";
                case ChallengeAxis.宿屋連合:     return "休むたび " + MetaDebuffApplicator.GetRestGoldCost() + "G を払う";
                case ChallengeAxis.絶望的な戦闘: return "物資の上限 −" + (-MetaDebuffApplicator.GetProvisionCapDelta());
                case ChallengeAxis.天変地異:     return "ボスの攻撃が、 ターンごとに +" + ((MetaDebuffApplicator.GetBossAttackMultiplier(2) - 1f) * 100f).ToString("0.#") + "% 強くなる";
            }
            return a.ToString();
        }

        public static string T4Effect(ChallengeT4 t)
        {
            switch (t)
            {
                case ChallengeT4.破綻:       return "有利なマスが " + (MetaDebuffApplicator.GetVoidTileChance() * 100f).ToString("0") + "% で空振り";
                case ChallengeT4.鋼の皮膚:   return "各戦闘 1 度、 敵が致命傷を耐える・ボスの HP ×" + MetaDebuffApplicator.GetBossHpMultiplier().ToString("0.##") + "（鋼の皮膚）";
                case ChallengeT4.凶運:       return "役を " + MetaDebuffApplicator.SealedRoleCount + " つ、 ラン中封じる（凶運）";
                case ChallengeT4.破産:       return "売値 ×" + MetaDebuffApplicator.GetSellPriceMultiplier().ToString("0.##") + "（破産）";
                case ChallengeT4.最後の審判: return "戦闘の累計 " + MetaDebuffApplicator.GetJudgmentTurnThreshold() + " ターン以降、 受けるダメージが増える（最後の審判）";
            }
            return t.ToString();
        }

        /// <summary>整備トラックを n 段足したときの文面。</summary>
        public static string BuffText(MetaPanelKind k, int n)
        {
            switch (k)
            {
                case MetaPanelKind.Output:     return "与ダメ +" + MetaPanel.OutputPct(n) + "%";
                case MetaPanelKind.AttackTerm: return "攻撃に挿したダイス 1 個ごとに、 配線合計 +" + MetaPanel.AttackTerminalPerDice(n);
                case MetaPanelKind.BlockTerm:  return "ブロックに挿したダイス 1 個ごとに、 配線合計 +" + MetaPanel.BlockTerminalPerDice(n);
                case MetaPanelKind.Plunder:    return "ボスを倒すとゴールド +" + MetaPanel.PlunderBossGold(n) + "・敵がパッシブを落とす率 +" + MetaPanel.PlunderPassiveDropPct(n).ToString("0") + "%";
                case MetaPanelKind.Trade:      return "店の特売の枠 +" + MetaPanel.TradeSaleSlots(n);
                case MetaPanelKind.SparkPoison: return SparkText("毒", n, "毒を付けるたび +" + MetaPanel.PoisonApplyBonus(2));
                case MetaPanelKind.SparkBleed:  return SparkText("出血", n, "出血 3 以上の敵の攻撃 −" + MetaPanel.BleedWeakenAttack(2, 3));
                case MetaPanelKind.SparkRinkai: return SparkText("臨界", n, "臨界のメーターの " + (MetaPanel.RinkaiCarryoverRatio(2) * 100f).ToString("0") + "% を次の戦闘へ持ち越す");
                case MetaPanelKind.SparkCharge: return SparkText("充電", n, "充電の上限 +" + MetaPanel.ChargeCapBonus(2) + "・開幕の充電 +" + MetaPanel.ChargeOpening(2));
                case MetaPanelKind.SparkShield: return SparkText("シールド", n, "減衰で残るシールド +" + MetaPanel.ShieldKeepBonusPct(2) + "%");
            }
            return k + " +" + n;
        }

        private static string SparkText(string axis, int n, string r2)
            => n >= 2 ? axis + "の品が店に出やすい・" + r2 : axis + "の品が店に出やすい";

        /// <summary>難易度 d の引き方を 1 行で (整備の窓に出す)。 例: "開始 I ／ 1→2 I ／ 4→5 II II"。</summary>
        public static string ScheduleText(int difficulty)
        {
            var sch = Schedule(difficulty);
            if (sch.Count == 0) return "カードを引かない";
            var parts = new List<string>();
            for (int s = 0; s < Slots; s++)
            {
                var ts = new List<string>();
                foreach (var e in sch) if (e.slot == s) ts.Add(Roman(e.tier));
                if (ts.Count > 0) parts.Add(SlotNames[s] + " " + string.Join(" ", ts));
            }
            return string.Join(" ／ ", parts);
        }

        public static string Roman(int t) => t == 1 ? "I" : t == 2 ? "II" : t == 3 ? "III" : t == 4 ? "IV" : "";
    }
}

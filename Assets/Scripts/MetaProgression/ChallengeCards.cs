using System.Collections.Generic;
using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// 挑戦カード (docs/GAME.md §23-25・第 2 段 2026-11-09)。 難易度 = ラン中にカードを引く回数。
    /// 見せる層の入口 (開始・1→2・2→3・3→4・4→5) で 2 枚から 1 枚を選び、 選んだカードのデバフとバフを両方受ける。
    ///
    /// <para><b>カード専用の効果</b>: 1 つの種類を Tier I〜IV で強さ (値) だけ変える。 旧の挑戦の軸や整備パネルの段は借りない。
    /// 効果の値は <see cref="Value"/> で種類ごとに引き、 各システムの計算の場所 (<see cref="MetaDebuffApplicator"/> / <see cref="MetaBuffApplicator"/> の getter) が読む。</para>
    ///
    /// <para><b>同じ種類はランで 1 度だけ</b> (デバフもバフも)。 重ねの定義も上限も要らない。 軽い段で先に取って重い段を消すのは戦術として許す。
    /// デバフは 4 つの系統 (攻撃・耐久・手・経済) に分け、 1 回の 2 枚は別の系統から配る (作れなければ系統の条件を外す)。
    /// バフは軸ごとに 1 種類 (毒・出血・臨界・充電・シールド・会心) + 汎用。</para>
    /// </summary>
    public static class ChallengeCards
    {
        public const int MaxDifficulty = 10;
        /// <summary>引く場所の数 (開始・1→2・2→3・3→4・4→5)。 見せる層 n (1..5 = RunState.currentFloor) に入る時が場所 n-1。</summary>
        public const int Slots = 5;
        public static readonly string[] SlotNames = { "開始", "1→2", "2→3", "3→4", "4→5" };

        public enum Family { None, Attack, Durability, Hand, Economy }

        public sealed class Card
        {
            public string id, kind;
            public int tier;
            public bool isBuff;
            public Family family;
            /// <summary>効果の量 (種類ごとに意味が違う。 % の種類は % の数)。</summary>
            public float value;
            /// <summary>2 つ目の量 (〈最後の審判〉の刻限ターン・〈先手を取られる〉のターン数)。 他の種類は 0。</summary>
            public float value2;
            /// <summary>効果がまだ実装されていない (配らない)。</summary>
            public bool pending;
        }

        private static readonly List<Card> _all = new List<Card>();
        private static readonly Dictionary<string, Card> _byId = new Dictionary<string, Card>();

        // ── 種類 (kind) の名前。 効果を読む側はこの名前で Value を引く ──
        // デバフ
        public const string EnemyAtk = "eatk", EnemyHp = "ehp", EnemyDef = "edef", Die1 = "die1", Bag = "bag", UseCap = "use",
            Price = "price", Money = "money", Esc = "esc", BossEsc = "bossesc", Steel = "steel",
            Judge = "judge", Heavy = "heavy", Heal = "heal", Ambush = "ambush",
            Twin = "twin", Regen = "regen", Erode = "erode", EnemyShield = "eshield", Opener = "opener", Rage = "rage";
        // バフ
        public const string Poison = "poison", Bleed = "bleed", Heat = "heat", Charge = "charge", Shield = "shield", Crit = "crit",
            Output = "out", TermAtk = "tatk", TermBlk = "tblk", WinGold = "gold", Cat = "cat", GoldPunch = "punch", MaxHp = "maxhp", HealUp = "healup";

        /// <summary>1 つの種類を Tier ごとの値で登録する。 null の Tier には出さない。 <paramref name="v2"/> は 2 つ目の量を持つ種類だけ (審判の刻限・先手のターン数)。</summary>
        private static void K(string kind, bool buff, Family fam, float?[] v, bool pending = false, float?[] v2 = null)
        {
            for (int t = 1; t <= 4; t++)
            {
                if (!v[t - 1].HasValue) continue;
                var c = new Card { id = (buff ? "B:" : "D:") + kind + ":" + t, kind = kind, tier = t, isBuff = buff, family = fam, value = v[t - 1].Value,
                    value2 = v2 != null && v2[t - 1].HasValue ? v2[t - 1].Value : 0f, pending = pending };
                _all.Add(c); _byId[c.id] = c;
            }
        }
        private static float?[] T(float? a, float? b, float? c, float? d) => new[] { a, b, c, d };

        static ChallengeCards()
        {
            // ── デバフ (汎用の圧)。 2026-11-09 の 1 枚ずつの測定から、 目標の重さ (I −3.6 / II −5.3 / III −6.6 / IV −9.7) に届く Tier にだけ置く。
            //    I は 1 層・II は 4 層・III/IV は 5 層の入口で受けるので、 ラン全体で効く札 (経済・持ち込み・HP) は I に、 戦闘を直接重くする札は II〜IV に寄る。
            //    値は比例で逆算した見込み (要測定)。
            K(EnemyAtk, false, Family.Attack, T(7, 14, 26, 37));          // 敵の攻撃 +N% (予告の前・Escalation.GetMultiplier)
            K(Esc, false, Family.Attack, T(20, 40, 70, 100));             // エスカレーションの上がり幅 +N% (段の倍率の 1 を超えた分に掛ける)
            K(BossEsc, false, Family.Attack, T(null, 1, 1, null));        // ボス戦ではエスカレーションが毎ターン進む
            K(Judge, false, Family.Attack, T(null, 30, 35, 50), v2: T(null, 300, 300, 260));   // 〈最後の審判〉累計 v2 ターンの後、 敵の攻撃 +N%
            K(Heavy, false, Family.Attack, T(null, 0.5f, 0.7f, 1.0f));    // 大技の倍率 +N
            K(Ambush, false, Family.Attack, T(null, 60, 70, 100), v2: T(null, 2, 3, 3));   // 〈先手を取られる〉形態の最初の v2 ターン、 敵の攻撃 +N% (予告に出るのでブロックで受けられる)
            K(Twin, false, Family.Attack, T(null, null, 40, 60));         // 〈連撃〉敵の攻撃が 2 回。 2 回目は N% の威力 (ブロックは 2 回とも効く)
            K(Opener, false, Family.Attack, T(null, null, 100, 150));     // 〈開幕の大技〉ボスの各形態の最初のターン、 敵の攻撃 +N%
            K(Rage, false, Family.Attack, T(null, 2, 3, 5));              // 〈怒り〉形態の中で 1 ターン進むごとに敵の攻撃 +N% (加算)
            K(EnemyHp, false, Family.Durability, T(30, null, null, null));   // 敵の最大 HP +N% (連戦の全段)
            K(EnemyDef, false, Family.Durability, T(2, 3, null, null), pending: true);   // 敵の防御 +N ── 受けると易しくなる (I +1.5 有意)。 原因を調べるまで配らない
            K(Steel, false, Family.Durability, T(1, null, null, null));   // 〈鋼の皮膚〉
            K(Regen, false, Family.Durability, T(null, null, 2, 3));      // 〈再生〉敵が毎ターン最大 HP の N% を回復する
            K(EnemyShield, false, Family.Durability, T(null, null, 3, 5));   // 〈敵のシールド〉敵は毎ターン、 最大 HP の N% までシールドを張り直す
            K(Die1, false, Family.Hand, T(null, 1, 2, 3));                // 戦闘中ずっと、 ダイス N 本の出目が 1 (大きい目から潰す)
            K(Erode, false, Family.Hand, T(null, null, 1, 2));            // 〈蝕み〉ターンの始めに、 最大 HP の N% の軽減できないダメージ (シールドは肩代わりする)
            K(Bag, false, Family.Hand, T(4, null, null, null));           // 消耗品は N 個までしか持てない
            K(UseCap, false, Family.Hand, T(2, null, null, null));        // 1 戦闘で使える消耗品は N 個まで
            K(Heal, false, Family.Hand, T(40, null, null, null));         // 回復 −N% (層の入口の全回復にも)
            K(Price, false, Family.Economy, T(6, null, null, null));      // 店の品物の値段 +N%
            K(Money, false, Family.Economy, T(35, null, null, null));     // 層を移るたび、 所持金の N% を失う

            // ── バフ (構成の向き: 軸ごとに 1 種類 + 汎用)。 2026-11-09: 壊れていない限り据え置き (勝利の金は I +11.4 で壊れていたので外した) ──
            K(Poison, true, Family.None, T(10, 20, 30, 40));              // 毒: 半減で減る量を N% 減らす (残る割合 50 → 55/60/65/70%)
            K(Bleed, true, Family.None, T(2, 4, 6, 8));                   // 出血 2 につき敵の攻撃 −1 (最大 −N)
            K(Heat, true, Family.None, T(2, 3, 5, 8));                    // 臨界: 毎ターン、 メーター +N
            K(Charge, true, Family.None, T(6, 10, 15, 25));               // 充電: 上限 +N・開幕の充電 +ChargeOpening(Tier)
            K(Shield, true, Family.None, T(10, 20, 30, 50));              // シールド: 減衰で減る量を N% 減らす
            K(Crit, true, Family.None, T(5, 8, 12, 18));                  // 会心率 +N%
            K(Output, true, Family.None, T(5, 8, 12, 18));                // 与ダメ +N%
            K(TermAtk, true, Family.None, T(null, 1, 2, 3));              // 攻撃の端子: 挿したダイス 1 個ごとに配線合計 +N
            K(TermBlk, true, Family.None, T(null, 1, 2, 3));              // ブロックの端子: 同上
            K(MaxHp, true, Family.None, T(5, 8, 12, 18));                 // 最大 HP +N% (受けた時に上げ、 上がった分だけ HP も増える)
            K(HealUp, true, Family.None, T(10, 15, 20, 30));              // 回復 +N% (回復の倍率に掛ける。 入口の全回復は満タンまでなので、 回復 −N% を打ち消す分だけ効く)
            K(Cat, true, Family.None, T(2, null, null, null), pending: true);          // 猫の手 (予告と BOT の配線に先に見せる仕組みができてから)
            K(GoldPunch, true, Family.None, T(null, null, 1, 1), pending: true);     // 金で殴る (BOT の方策ができてから)
        }

        /// <summary>充電の札の、 開幕の充電 (Tier ごと)。</summary>
        public static int ChargeOpening(int tier) => tier == 1 ? 3 : tier == 2 ? 5 : tier == 3 ? 8 : tier == 4 ? 12 : 0;
        /// <summary>〈鋼の皮膚〉のボスの HP の上乗せ (%)。 敵の最大 HP の札と同じ溜まりに足す (掛け算にしない)。</summary>
        public const float SteelBossHpPct = 20f;

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

        /// <summary>見せる層 (1..5 = <see cref="RunState.currentFloor"/>) に入る時の場所。 それ以外は -1。
        /// <b>深さ (<see cref="RunState.Depth"/> = 0/1/3/5/6/7) で引かないこと</b> ── 2026-11-08 に深さで引いて、 1→2 と 3→4 の場所が一度も配られていなかった。</summary>
        public static int SlotOfLayer(int layer) => layer >= 1 && layer <= Slots ? layer - 1 : -1;

        // ============================================================
        //  配る (2 枚・表と裏の組)
        // ============================================================

        /// <summary>Tier の 2 組を配る。 返すのは "デバフの id|バフの id" の並び (最大 2)。
        /// 持っている種類は配らない (表も裏も)。 2 枚の表は別の系統から (作れなければ系統の条件を外す)、 2 枚の裏は別の種類。
        /// 乱数は専用の鍵 (他の抽選の消費列を動かさない)。</summary>
        public static List<string> Deal(int tier, IReadOnlyList<string> picks)
        {
            var owned = new HashSet<string>();
            if (picks != null) foreach (var p in picks) { Split(p, out var dc, out var bc); if (dc != null) owned.Add(dc.kind); if (bc != null) owned.Add(bc.kind); }
            var d1 = Draw(tier, false, owned, null, Family.None);
            var d2 = d1 != null ? (Draw(tier, false, owned, d1.kind, d1.family) ?? Draw(tier, false, owned, d1.kind, Family.None)) : null;
            var b1 = Draw(tier, true, owned, null, Family.None);
            var b2 = b1 != null ? Draw(tier, true, owned, b1.kind, Family.None) : null;
            var outp = new List<string>(2);
            if (d1 != null && b1 != null) outp.Add(d1.id + "|" + b1.id);
            if (d2 != null && b2 != null) outp.Add(d2.id + "|" + b2.id);
            return outp;
        }

        /// <summary>1 枚引く。 <paramref name="avoidFamily"/> が None でなければ、 その系統を除く。</summary>
        private static Card Draw(int tier, bool buff, HashSet<string> owned, string excludeKind, Family avoidFamily)
        {
            var pool = new List<Card>();
            foreach (var c in _all)
            {
                if (c.tier != tier || c.isBuff != buff || c.pending) continue;
                if (owned.Contains(c.kind) || c.kind == excludeKind) continue;
                if (avoidFamily != Family.None && c.family == avoidFamily) continue;
                pool.Add(c);
            }
            if (pool.Count == 0) return null;
            return pool[GameRng.RangeAuto("challenge_card", 0, pool.Count)];
        }

        public static void Split(string pick, out Card deb, out Card buf)
        {
            deb = buf = null; if (string.IsNullOrEmpty(pick)) return;
            int bar = pick.IndexOf('|');
            if (bar < 0) { var one = Get(pick); if (one != null && one.isBuff) buf = one; else deb = one; return; }   // 片面だけ (測定)
            deb = Get(pick.Substring(0, bar)); buf = Get(pick.Substring(bar + 1));
        }

        /// <summary>id が全部知っているカードか (測定の口の検査)。</summary>
        public static bool IsKnown(string pick)
        {
            if (string.IsNullOrEmpty(pick)) return false;
            foreach (var part in pick.Split('|')) if (Get(part) == null) return false;
            return true;
        }

        // ============================================================
        //  効果: 受けたカードの値を種類ごとに引く
        // ============================================================

        /// <summary>受けたカードの (種類 → カード)。 同じ種類は 1 度しか受けないので 1 枚。</summary>
        public static Dictionary<string, Card> Collect(IReadOnlyList<string> picks)
        {
            var d = new Dictionary<string, Card>();
            if (picks != null)
                foreach (var p in picks)
                {
                    Split(p, out var dc, out var bc);
                    if (dc != null) d[dc.kind] = dc;
                    if (bc != null) d[bc.kind] = bc;
                }
            return d;
        }

        /// <summary>今のラン (カードのランなら) で受けている、 その種類の値。 受けていなければ 0。</summary>
        public static float Value(string kind) => Held(kind)?.value ?? 0f;

        /// <summary>今のラン (カードのランなら) で受けている、 その種類のカード。 無ければ null。</summary>
        public static Card Held(string kind)
        {
            var map = RunState.CardMap(GameManager.Instance?.Run);
            return map != null && map.TryGetValue(kind, out var c) ? c : null;
        }

        // ============================================================
        //  文面 (人が読む)
        // ============================================================

        public static string Text(Card c)
        {
            if (c == null) return "";
            float v = c.value; string n = v.ToString("0.##");
            switch (c.kind)
            {
                case EnemyAtk: return "敵の攻撃 +" + n + "%";
                case Esc: return "エスカレーションの上がり幅 +" + n + "%";
                case BossEsc: return "ボス戦ではエスカレーションが毎ターン進む";
                case Judge: return "ランの累計 " + c.value2.ToString("0") + " ターンを過ぎると、 敵の攻撃 +" + n + "%（最後の審判）";
                case Heavy: return "敵の大技の倍率 +" + n;
                case Ambush: return "戦闘 (形態ごと) の最初の " + c.value2.ToString("0") + " ターン、 敵の攻撃 +" + n + "%（先手を取られる）";
                case Twin: return "敵の攻撃が 2 回になる。 2 回目は " + n + "% の威力（連撃）";
                case Opener: return "ボスの各形態の最初のターン、 敵の攻撃 +" + n + "%（開幕の大技）";
                case Rage: return "形態の中で 1 ターン進むごとに、 敵の攻撃 +" + n + "%（怒り）";
                case Regen: return "敵が毎ターン、 最大 HP の " + n + "% を回復する（再生）";
                case EnemyShield: return "敵は毎ターン、 最大 HP の " + n + "% までシールドを張り直す";
                case Erode: return "ターンの始めに、 最大 HP の " + n + "% の軽減できないダメージ（蝕み）";
                case EnemyHp: return "敵の最大 HP +" + n + "%";
                case EnemyDef: return "敵の防御 +" + n;
                case Steel: return "各戦闘 1 度、 敵が致命傷を HP1 で耐える・ボスの HP +" + SteelBossHpPct.ToString("0") + "%（鋼の皮膚）";
                case Die1: return "戦闘中ずっと、 ダイス " + n + " 本の出目が 1";
                case Bag: return "消耗品は " + n + " 個までしか持てない";
                case UseCap: return "1 戦闘で使える消耗品は " + n + " 個まで";
                case Heal: return "回復 −" + n + "%（層の入口の全回復にも）";
                case Price: return "店の品物の値段 +" + n + "%";
                case Money: return "層を移るたび、 所持金の " + n + "% を失う";
                case Poison: return "毒: 半減で減る量 −" + n + "%（残る割合 " + (50 + v / 2f).ToString("0") + "%）";
                case Bleed: return "出血 2 につき敵の攻撃 −1（最大 −" + n + "）";
                case Heat: return "毎ターン、 臨界のメーター +" + n;
                case Charge: return "充電の上限 +" + n + "・開幕の充電 +" + ChargeOpening(c.tier);
                case Shield: return "シールド: 減衰で減る量 −" + n + "%";
                case Crit: return "会心率 +" + n + "%";
                case Output: return "与ダメ +" + n + "%";
                case TermAtk: return "攻撃の端子: 挿したダイス 1 個ごとに配線合計 +" + n;
                case TermBlk: return "ブロックの端子: 挿したダイス 1 個ごとに配線合計 +" + n;
                case WinGold: return "戦闘に勝つたびゴールド +" + n;
                case MaxHp: return "最大 HP +" + n + "%";
                case HealUp: return "回復 +" + n + "%";
                case Cat: return "猫の手: 毎ターン、 ダイス 1 個を猫が配線する。 猫が挿したダイスは出目 +" + n;
                case GoldPunch: return "金で殴る: 攻撃に金を挿せる（1G につき攻撃 +" + n + "）";
            }
            return c.kind;
        }

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

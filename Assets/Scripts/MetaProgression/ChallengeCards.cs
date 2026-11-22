using System.Collections.Generic;
using GameLoop;
using ItemSystem.PassiveSkills;

namespace MetaProgression
{
    /// <summary>
    /// 挑戦カード (docs/GAME.md §23-25・2026-11-22 フルリワーク・docs/challenge-cards-rework-spec.md)。 難易度 = ラン中にカードを引く回数。
    /// 見せる層の入口 (開始・1→2・2→3・3→4・4→5) で <b>各所 1 回まで</b> 2 枚から 1 枚を選び、 選んだカードの表 (純デバフ) と裏 (純バフ) を両方受ける。
    ///
    /// <para><b>表と裏は独立</b>: 表 16 種 (4 系統 × 4) は 100% デバフ、 裏 14 種 (8 軸 × 1 + 汎用 6) は 100% バフ。 1 枚の中で混ぜない。
    /// 配る時に表の 2 枚 (別の系統) と裏の 2 枚を別々に引いて組む。 <b>同じ種類はランで 1 度だけ</b> (軽い Tier で先に取って重い Tier を消すのは戦術として許す)。</para>
    /// <para><b>裏は出現の枠 (<see cref="RunState.lockedAxes"/>) に合わせる</b>: 枠に軸が 1 つでもあれば、 枠の外の軸のバフは配らない (死に札を出さない)。</para>
    /// <para><b>七つの大罪 (Tier IV)</b> は難易度 10 の 4→5 だけ。 7 柱から 2 柱を配り 1 柱を選ぶ。 裏は付かない (id だけ・"|" なし)。 名前とフレーバーの正本は <see cref="PermanentDebuffIds"/>。</para>
    /// <para>丸め: 敵を強くする・自分を弱くする % は四捨五入 (<c>Mathf.RoundToInt</c>)、 自分を強くする % は切り上げ (<c>Mathf.CeilToInt</c>)。</para>
    /// </summary>
    public static class ChallengeCards
    {
        public const int MaxDifficulty = 10;
        /// <summary>引く場所の数 (開始・1→2・2→3・3→4・4→5)。 見せる層 n (1..5 = RunState.currentFloor) に入る時が場所 n-1。</summary>
        public const int Slots = 5;
        public static readonly string[] SlotNames = { "開始", "1→2", "2→3", "3→4", "4→5" };
        /// <summary>大罪の Tier。</summary>
        public const int SinTier = 4;

        public enum Family { None, Attack, Durability, Hand, Economy }

        public sealed class Card
        {
            public string id, kind;
            public int tier;
            public bool isBuff;
            public Family family;
            /// <summary>裏 (バフ) の軸 (<see cref="AxisRules"/> の 8 軸の名前)。 汎用のバフと表は null。</summary>
            public string axis;
            /// <summary>効果の量 (種類ごとに意味が違う。 % の種類は % の数)。</summary>
            public float value;
            /// <summary>2 つ目の量 (種類ごとの注記を見る)。 無ければ 0。</summary>
            public float value2;
        }

        private static readonly List<Card> _all = new List<Card>();
        private static readonly Dictionary<string, Card> _byId = new Dictionary<string, Card>();

        // ── 種類 (kind) の名前。 効果を読む側はこの名前で Value / Value2 を引く ──
        // 表: 攻撃
        public const string EnemyAtk = "eatk", Heavy = "heavy", Twin = "twin", Opener = "opener";
        // 表: 耐久
        public const string EnemyHp = "ehp", Steel = "steel", Regen = "regen", Ukemi = "ukemi";
        // 表: 手札・探索
        public const string Deadline = "deadline", HeavyDice = "hdice", Clog = "clog", Heal = "heal";
        // 表: 経済
        public const string Price = "price", Money = "money", Lowball = "lowball", Bag = "bag";
        // 七つの大罪 (IV・難易度 10 の 4→5 だけ・5 層の間だけ効く)
        public const string SinPride = "sin_pride", SinEnvy = "sin_envy", SinGreed = "sin_greed", SinWrath = "sin_wrath",
            SinSloth = "sin_sloth", SinGluttony = "sin_gluttony", SinLust = "sin_lust";
        public static bool IsSin(string kind) => kind != null && kind.StartsWith("sin_");
        // 裏: 8 軸
        public const string Charge = "charge", Poison = "poison", Shield = "shield", Bleed = "bleed", Heat = "heat", Crit = "crit", Deck = "deck", Role = "role";
        // 裏: 汎用
        public const string Blade = "blade", MaxHp = "maxhp", HealUp = "healup", Regular = "regular", Pursuit = "pursuit", Appraise = "appraise";

        /// <summary>1 つの種類を Tier I〜III (大罪は IV) ごとの値で登録する。 null の Tier には出さない。</summary>
        private static void K(string kind, bool buff, Family fam, string axis, float?[] v, float?[] v2 = null)
        {
            for (int t = 1; t <= 4; t++)
            {
                if (t - 1 >= v.Length || !v[t - 1].HasValue) continue;
                var c = new Card { id = (buff ? "B:" : "D:") + kind + ":" + t, kind = kind, tier = t, isBuff = buff, family = fam, axis = axis, value = v[t - 1].Value,
                    value2 = v2 != null && t - 1 < v2.Length && v2[t - 1].HasValue ? v2[t - 1].Value : 0f };
                _all.Add(c); _byId[c.id] = c;
            }
        }
        private static float?[] T(float? a, float? b, float? c) => new[] { a, b, c };
        private static float?[] IV(float d) => new float?[] { null, null, null, d };

        static ChallengeCards()
        {
            // ── 表 (純デバフ) 16 種。 値は仕様書 §2 のまま (未測定・難易度の測定は neon のアイテムリワーク後) ──
            K(EnemyAtk, false, Family.Attack, null, T(null, null, 35));                     // 猛攻: 敵の攻撃 +N%
            K(Heavy, false, Family.Attack, null, T(0.4f, 0.8f, 1.2f));                      // 致命的な一撃: 大技の倍率 +N
            K(Twin, false, Family.Attack, null, T(null, 60, 100));                          // 連撃: 敵の攻撃が 2 回・2 回目は N%
            K(Opener, false, Family.Attack, null, T(null, 100, 180));                       // 先手必殺: ボスの各形態の 1 ターン目 敵の攻撃 +N%
            K(EnemyHp, false, Family.Durability, null, T(20, 35, 50));                      // 厚い皮: 敵の最大 HP +N%
            K(Steel, false, Family.Durability, null, T(1, 1, null), T(0, 20, null));        // 鋼の皮膚: 致命傷を HP1 で耐える / v2 = ボスの最大 HP +N%
            K(Regen, false, Family.Durability, null, T(null, 3, 5));                        // 再生: 敵が毎ターン最大 HP の N% を回復
            K(Ukemi, false, Family.Durability, null, T(null, 1.35f, 1.20f), T(null, 0, 15)); // 受身: ダウン中の被ダメ倍率 / v2 = 目盛りの間隔 +N%
            K(Deadline, false, Family.Hand, null, T(1, 2, 2), T(0, 0, 1));                  // 刻限: 段の到達を N ターン早める / v2 = 1 ならボス戦は毎ターン進む
            K(HeavyDice, false, Family.Hand, null, T(1, 2, 3));                             // 重い賽: 振り直しの充電の費用 +N (無料は除く)
            K(Clog, false, Family.Hand, null, T(1, 1, 2), T(1, 2, 1));                      // 目詰まり: 各戦闘の最初の v2 ターン、 手札の端子 N 枚が止まる
            K(Heal, false, Family.Hand, null, T(25, 45, 65));                               // 深い傷: 回復 −N% (層の入口の全回復も)
            K(Price, false, Family.Economy, null, T(15, 30, 45));                           // 暴騰: 店の品物の値段 +N%
            K(Money, false, Family.Economy, null, T(20, 40, null));                         // 通行料: 層を移るたび所持金の N% を失う
            K(Lowball, false, Family.Economy, null, T(30, 50, null));                       // 買い叩き: 秘宝の見送り換金の額 −N%
            K(Bag, false, Family.Economy, null, T(2, 2, 1), T(0, 4, 0));                    // 浅い鞄: 1 戦闘で使える消耗品 N 個まで / v2 = 持てる数 (0 = 制限なし)

            // ── 七つの大罪 (IV・仕様書 §4 の値のまま・未較正) ──
            K(SinPride, false, Family.Hand, null, IV(60), IV(25));       // 傲慢: 防御の端子に挿した出目 −N% / v2 = シールドの獲得 −N%
            K(SinEnvy, false, Family.Attack, null, IV(80));              // 嫉妬: 残り HP の割合が敵より高い間、 敵の攻撃 +N%
            K(SinGreed, false, Family.Economy, null, IV(1));             // 強欲: 受けた時、 所持金と消耗品をすべて失う + 5 層の店を使えない
            K(SinWrath, false, Family.Attack, null, IV(40), IV(120));    // 憤怒: ボスの各形態の 1 ターン目、 現在 HP −N% / v2 = そのターンの敵の攻撃 +N%
            K(SinSloth, false, Family.Hand, null, IV(2), IV(2));         // 怠惰: 全部の出目 −N (下限 1) / v2 = 各戦闘の 1 ターン目、 手札の端子が止まる枚数
            K(SinGluttony, false, Family.Durability, null, IV(100), IV(35));   // 暴食: 回復 −N% / v2 = 受けた時に最大 HP −N%
            K(SinLust, false, Family.Hand, null, IV(2));                 // 色欲: 1 ターンに挿せる出目は N 個まで + 秘宝の見送り換金ができない

            // ── 裏 (純バフ) 14 種。 8 軸は 1 種ずつ ──
            K(Charge, true, Family.None, AxisRules.Charge, T(4, 7, 10), T(0, 0, 1));    // 予備電源: 開幕の充電 +N / v2 = 再起動・オーバークロックの充電 −N (最低 1)
            K(Poison, true, Family.None, AxisRules.Poison, T(0.5f, 1.0f, 1.5f));        // 毒仕込み: 起爆の倍率 +N
            K(Shield, true, Family.None, AxisRules.Shield, T(6, 12, 18), T(0, 0, 1));   // 見切り: ジャスト防御の時、 敵へ防御無視 N の反撃 / v2 = 許容幅 +N
            K(Bleed, true, Family.None, AxisRules.Bleed, T(2, 3, 5));                   // 初太刀: 各戦闘の 1 ターン目、 撃ごとに出血 +N
            K(Heat, true, Family.None, AxisRules.Heat, T(15, 30, 50), T(0, 0, 10));     // 予熱: 爆発が無視する敵の防御 +N% / v2 = 爆発の基礎 +N
            K(Crit, true, Family.None, AxisRules.Crit, T(6, 10, 15), T(0, 0, 25));      // 急所読み: 会心率 +N% / v2 = 超過会心率の変換 +N%
            K(Deck, true, Family.None, AxisRules.Deck, T(1, 1, 2), T(0, 3, 5));         // 手捌き: 戦闘の 1 ターン目の手札 +N / v2 = 使い捨てを使うたびブロック +N
            K(Role, true, Family.None, AxisRules.Role, T(1, 1, 2), T(0, 3, 5));         // 手馴染み: 各戦闘の最初の振り直し N 回が無料 / v2 = 役が立ったターン ブロック +N
            // 汎用 6 種
            K(Blade, true, Family.None, null, T(1, 2, 4));                              // 刃研ぎ: 撃ごとの攻撃 +N
            K(MaxHp, true, Family.None, null, T(10, 20, 30));                           // 地力: 最大 HP +N% (受けた時・上がった分だけ HP も)
            K(HealUp, true, Family.None, null, T(20, 40, 60));                          // 手当て: 回復 +N%
            K(Regular, true, Family.None, null, T(2, 4, 999));                          // 顔なじみ: 店の軸指定リロールの追加 −NG (III は無料)
            K(Pursuit, true, Family.None, null, T(1, 1, 2), T(0, 4, 8));                // 追い打ち: ボスのダウン中のターン、 手札 +N / v2 = 撃ごとの攻撃 +N
            K(Appraise, true, Family.None, null, T(25, 50, 75));                        // 目利き: 秘宝の見送り換金の額 +N%
        }

        public static Card Get(string id) => id != null && _byId.TryGetValue(id, out var c) ? c : null;
        public static IReadOnlyList<Card> All => _all;

        // ============================================================
        //  難易度 → 引く場所と Tier (仕様書 §1・1 層 1 枚・最大 5 枚・左 ≤ 右)
        // ============================================================

        /// <summary>難易度 1..10 の、 場所 (開始・1→2・2→3・3→4・4→5) ごとの Tier (0 = 引かない・4 = 大罪)。</summary>
        private static readonly int[][] Table =
        {
            new[] { 0, 0, 0, 0, 0 },
            new[] { 1, 0, 0, 0, 0 },
            new[] { 1, 1, 0, 0, 0 },
            new[] { 1, 1, 1, 0, 0 },
            new[] { 1, 1, 1, 2, 0 },
            new[] { 1, 1, 2, 2, 2 },
            new[] { 1, 1, 2, 2, 3 },
            new[] { 1, 2, 2, 3, 3 },
            new[] { 2, 2, 2, 3, 3 },
            new[] { 2, 2, 3, 3, 3 },
            new[] { 2, 2, 3, 3, SinTier },
        };

        /// <summary>難易度 d の引き方: (場所, Tier) の並び。 各場所 1 回まで。</summary>
        public static List<(int slot, int tier)> Schedule(int difficulty)
        {
            int d = System.Math.Max(0, System.Math.Min(MaxDifficulty, difficulty));
            var outp = new List<(int, int)>();
            for (int s = 0; s < Slots; s++) if (Table[d][s] > 0) outp.Add((s, Table[d][s]));
            return outp;
        }

        /// <summary>見せる層 (1..5 = <see cref="RunState.currentFloor"/>) に入る時の場所。 それ以外は -1。
        /// <b>深さ (<see cref="RunState.Depth"/> = 0/1/3/5/6/7) で引かないこと</b> ── 2026-11-08 に深さで引いて、 1→2 と 3→4 の場所が一度も配られていなかった。</summary>
        public static int SlotOfLayer(int layer) => layer >= 1 && layer <= Slots ? layer - 1 : -1;

        // ============================================================
        //  配る (2 枚)
        // ============================================================

        /// <summary>[計装] 配った裏のうち、 軸のバフの数 / 出現の枠に軸があった時に配った軸のバフの数 / そのうち枠の外の軸の数 (常に 0 のはず)。</summary>
        public static long DealtAxisBuffs, DealtAxisBuffsWithLock, DealtOutsideLock, DealtGenericBuffs;
        public static void ResetStats() { DealtAxisBuffs = DealtAxisBuffsWithLock = DealtOutsideLock = DealtGenericBuffs = 0; }
        /// <summary>計装を経路の集計へ混ぜる (並列ワーカーの返し・通常バッチのサマリー)。</summary>
        public static void WriteStats(Dictionary<string, int> into)
        {
            into["挑戦の裏:軸"] = (int)DealtAxisBuffs; into["挑戦の裏:枠ありで軸"] = (int)DealtAxisBuffsWithLock;
            into["挑戦の裏:枠の外"] = (int)DealtOutsideLock; into["挑戦の裏:汎用"] = (int)DealtGenericBuffs;
        }

        /// <summary>Tier の 2 組を配る。 返すのは "表の id|裏の id" の並び (最大 2)。 大罪 (Tier IV) は "大罪の id" だけ (裏なし)。
        /// 持っている種類は配らない。 2 枚の表は別の系統から (作れなければ系統の条件を外す)。 裏は出現の枠に合わせて引く (仕様書 §3-1)。
        /// 乱数は専用の鍵 (他の抽選の消費列を動かさない)。</summary>
        public static List<string> Deal(int tier, IReadOnlyList<string> picks, RunState run = null)
        {
            var owned = new HashSet<string>();
            if (picks != null) foreach (var p in picks) { Split(p, out var dc, out var bc); if (dc != null) owned.Add(dc.kind); if (bc != null) owned.Add(bc.kind); }
            var outp = new List<string>(2);
            if (tier >= SinTier)
            {
                var s1 = Draw(SinTier, false, owned, null, Family.None);
                var s2 = s1 != null ? Draw(SinTier, false, owned, s1.kind, Family.None) : null;
                if (s1 != null) outp.Add(s1.id);
                if (s2 != null) outp.Add(s2.id);
                return outp;
            }
            var d1 = Draw(tier, false, owned, null, Family.None);
            var d2 = d1 != null ? (Draw(tier, false, owned, d1.kind, d1.family) ?? Draw(tier, false, owned, d1.kind, Family.None)) : null;
            var b1 = DrawBuff(tier, owned, null, run, true);
            var b2 = b1 != null ? DrawBuff(tier, owned, b1.kind, run, false) : null;
            if (d1 != null && b1 != null) { outp.Add(d1.id + "|" + b1.id); NoteBuff(b1, run); }
            if (d2 != null && b2 != null) { outp.Add(d2.id + "|" + b2.id); NoteBuff(b2, run); }
            return outp;
        }

        private static void NoteBuff(Card b, RunState run)
        {
            if (b.axis == null) { DealtGenericBuffs++; return; }
            DealtAxisBuffs++;
            if (run?.lockedAxes != null && run.lockedAxes.Count > 0)
            {
                DealtAxisBuffsWithLock++;
                if (!run.lockedAxes.Contains(b.axis)) DealtOutsideLock++;
            }
        }

        /// <summary>表を 1 枚引く。 <paramref name="avoidFamily"/> が None でなければ、 その系統を除く。</summary>
        private static Card Draw(int tier, bool buff, HashSet<string> owned, string excludeKind, Family avoidFamily)
        {
            var pool = new List<Card>();
            foreach (var c in _all)
            {
                if (c.tier != tier || c.isBuff != buff) continue;
                if (owned.Contains(c.kind) || c.kind == excludeKind) continue;
                if (avoidFamily != Family.None && c.family == avoidFamily) continue;
                pool.Add(c);
            }
            if (pool.Count == 0) return null;
            return pool[GameRng.RangeAuto("challenge_card", 0, pool.Count)];
        }

        /// <summary>裏を 1 枚引く (仕様書 §3-1)。 出現の枠 (<see cref="RunState.lockedAxes"/>) に軸があれば、 枠の外の軸のバフは出さない:
        /// 1 枚目 (<paramref name="first"/>) = 枠の軸のバフ (所持数が単独 1 位の主軸は重み ×2)・無ければ汎用 / 2 枚目 = 残りの枠の軸のバフ + 汎用。
        /// 枠が空なら 8 軸 + 汎用の全部から。</summary>
        private static Card DrawBuff(int tier, HashSet<string> owned, string excludeKind, RunState run, bool first)
        {
            var locked = run?.lockedAxes;
            bool hasLock = locked != null && locked.Count > 0;
            string main = hasLock && first ? MainLockedAxis(run) : null;
            var pool = new List<Card>(); var w = new List<int>();
            void Collect(bool axisOnly)
            {
                pool.Clear(); w.Clear();
                foreach (var c in _all)
                {
                    if (c.tier != tier || !c.isBuff || owned.Contains(c.kind) || c.kind == excludeKind) continue;
                    if (c.axis != null && hasLock && !locked.Contains(c.axis)) continue;   // 枠の外の軸 (死に札) は出さない
                    if (axisOnly && c.axis == null) continue;
                    pool.Add(c); w.Add(c.axis != null && c.axis == main ? 2 : 1);
                }
            }
            Collect(hasLock && first);
            if (pool.Count == 0 && hasLock && first) Collect(false);   // 枠の軸のバフが尽きたら汎用へ
            if (pool.Count == 0) return null;
            int total = 0; foreach (int x in w) total += x;
            int r = GameRng.RangeAuto("challenge_card", 0, total);
            for (int i = 0; i < pool.Count; i++) { r -= w[i]; if (r < 0) return pool[i]; }
            return pool[pool.Count - 1];
        }

        /// <summary>枠の軸のうち、 所持数 (<see cref="AxisRules.Count"/>) が単独 1 位の軸 (同率・所持 0 なら null)。 ロックの成立を待たない (<see cref="ItemSystem.AxisPool.MainAxis"/> との違い)。</summary>
        private static string MainLockedAxis(RunState run)
        {
            string best = null; int bestN = 0; bool tie = false;
            foreach (var a in run.lockedAxes)
            {
                int n = AxisRules.Count(run, a);
                if (n > bestN) { bestN = n; best = a; tie = false; }
                else if (n == bestN && n > 0) tie = true;
            }
            return tie ? null : best;
        }

        /// <summary>"表|裏" (または片面だけの id) を分ける。 知らない id (廃止したカードの古いセーブ・2026-11-22) は null にする (無視する)。</summary>
        public static void Split(string pick, out Card deb, out Card buf)
        {
            deb = buf = null; if (string.IsNullOrEmpty(pick)) return;
            int bar = pick.IndexOf('|');
            if (bar < 0) { var one = Get(pick); if (one != null && one.isBuff) buf = one; else deb = one; return; }   // 片面だけ (大罪・測定)
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

        /// <summary>受けたカードの (種類 → カード)。 同じ種類は 1 度しか受けないので 1 枚。 知らない id は飛ばす。</summary>
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

        /// <summary>今のランで受けている、 その種類の値。 受けていなければ 0。</summary>
        public static float Value(string kind) => Held(kind)?.value ?? 0f;
        /// <summary>今のランで受けている、 その種類の 2 つ目の量。 受けていなければ 0。</summary>
        public static float Value2(string kind) => Held(kind)?.value2 ?? 0f;

        /// <summary>今のランで受けている、 その種類のカード。 無ければ null。</summary>
        public static Card Held(string kind)
        {
            var map = RunState.CardMap(GameManager.Instance?.Run);
            return map != null && map.TryGetValue(kind, out var c) ? c : null;
        }

        // ============================================================
        //  文面 (人が読む)
        // ============================================================

        /// <summary>カードの名前 (大罪は正式名 = <see cref="PermanentDebuffIds"/>)。</summary>
        public static string Name(Card c)
        {
            if (c == null) return "";
            switch (c.kind)
            {
                case EnemyAtk: return "猛攻"; case Heavy: return "致命的な一撃"; case Twin: return "連撃"; case Opener: return "先手必殺";
                case EnemyHp: return "厚い皮"; case Steel: return "鋼の皮膚"; case Regen: return "再生"; case Ukemi: return "受身";
                case Deadline: return "刻限"; case HeavyDice: return "重い賽"; case Clog: return "目詰まり"; case Heal: return "深い傷";
                case Price: return "暴騰"; case Money: return "通行料"; case Lowball: return "買い叩き"; case Bag: return "浅い鞄";
                case SinPride: return PermanentDebuffIds.Pride; case SinEnvy: return PermanentDebuffIds.Envy; case SinGreed: return PermanentDebuffIds.Greed;
                case SinWrath: return PermanentDebuffIds.Wrath; case SinSloth: return PermanentDebuffIds.Sloth; case SinGluttony: return PermanentDebuffIds.Gluttony;
                case SinLust: return PermanentDebuffIds.Lust;
                case Charge: return "予備電源"; case Poison: return "毒仕込み"; case Shield: return "見切り"; case Bleed: return "初太刀";
                case Heat: return "予熱"; case Crit: return "急所読み"; case Deck: return "手捌き"; case Role: return "手馴染み";
                case Blade: return "刃研ぎ"; case MaxHp: return "地力"; case HealUp: return "手当て"; case Regular: return "顔なじみ";
                case Pursuit: return "追い打ち"; case Appraise: return "目利き";
            }
            return c.kind;
        }

        /// <summary>大罪のフレーバー本文 (正本 <see cref="PermanentDebuffIds.GetFlavorText"/>)。 大罪でなければ ""。</summary>
        public static string Flavor(Card c) => c != null && IsSin(c.kind) ? PermanentDebuffIds.GetFlavorText(Name(c)) : "";

        /// <summary>「名前 ─ 効果」の 1 行。</summary>
        public static string Text(Card c) => c == null ? "" : Name(c) + " ─ " + Effect(c);

        /// <summary>効果の文 (新しい規則は全部ここに書く・画面の選択と持ち物のステータスに出る)。</summary>
        public static string Effect(Card c)
        {
            if (c == null) return "";
            string n = c.value.ToString("0.##"), n2 = c.value2.ToString("0.##");
            bool v2 = c.value2 > 0f;
            switch (c.kind)
            {
                case EnemyAtk: return "敵の攻撃 +" + n + "%";
                case Heavy: return "敵の大技の倍率 +" + n;
                case Twin: return "敵の攻撃が 2 回になる。 2 回目は " + n + "% の威力";
                case Opener: return "ボスの各形態の 1 ターン目、 敵の攻撃 +" + n + "%";
                case EnemyHp: return "敵の最大 HP +" + n + "%";
                case Steel: return "各戦闘 1 度、 敵が致命傷を HP1 で耐える（そのターンは攻撃しない）" + (v2 ? "・ボスの最大 HP +" + n2 + "%" : "");
                case Regen: return "敵が毎ターン、 最大 HP の " + n + "% を回復する";
                case Ukemi: return "ボスのダウン中に受けるダメージ ×" + c.value.ToString("0.00") + "（通常 ×" + CombatSystem.BossDown.TakenMul.ToString("0.0") + "）" + (v2 ? "・目盛りの間隔 +" + n2 + "%" : "");
                case Deadline: return "エスカレーションの段が " + n + " ターン早く上がる" + (v2 ? "・ボス戦は毎ターン上がる" : "");
                case HeavyDice: return "振り直しの充電の費用 +" + n + "（無料の振り直しは除く）";
                case Clog: return (c.value2 > 1 ? "各戦闘の 1〜" + n2 + " ターン目" : "各戦闘の 1 ターン目") + "、 手札の端子 " + n + " 枚が止まる";
                case Heal: return "回復 −" + n + "%（層の入口の全回復にも）";
                case Price: return "店の品物の値段 +" + n + "%";
                case Money: return "層を移るたび、 所持金の " + n + "% を失う";
                case Lowball: return "秘宝の見送り換金の額 −" + n + "%";
                case Bag: return (v2 ? "消耗品は " + n2 + " 個までしか持てない・" : "") + "1 戦闘で使える消耗品は " + n + " 個まで";
                case SinPride: return "防御の端子に挿した出目 −" + n + "%・シールドの獲得 −" + n2 + "%";
                case SinEnvy: return "残り HP の割合が敵より高い間、 敵の攻撃 +" + n + "%";
                case SinGreed: return "受けた時、 所持金と消耗品をすべて失う・5 層の店を使えない";
                case SinWrath: return "ボスの各形態の 1 ターン目、 現在 HP −" + n + "%（1 は残る）・そのターンの敵の攻撃 +" + n2 + "%";
                case SinSloth: return "全部の出目 −" + n + "（1 より小さくならない）・各戦闘の 1 ターン目、 手札の端子 " + n2 + " 枚が止まる";
                case SinGluttony: return "回復 −" + n + "%（吸血・回復薬・層の入口の全回復も）・受けた時に最大 HP −" + n2 + "%";
                case SinLust: return "1 ターンに挿せる出目は " + n + " 個まで・秘宝の見送り換金ができない";
                case Charge: return "戦闘の始めの充電 +" + n + (v2 ? "・【再起動】【オーバークロック】の充電 −" + n2 + "（最低 1）" : "");
                case Poison: return "【起爆】の倍率 +" + n;
                case Shield: return "【ジャスト防御】の時、 敵へ防御無視の " + n + " ダメージ" + (v2 ? "・許容幅 +" + n2 : "");
                case Bleed: return "各戦闘の 1 ターン目、 撃が当たるたび敵に出血 +" + n;
                case Heat: return "爆発が無視する敵の防御 +" + n + "%（計 " + System.Math.Min(100, CombatSystem.HeatCore.DefensePenPct + (int)c.value) + "%）" + (v2 ? "・爆発の基礎 +" + n2 : "");
                case Crit: return "会心率 +" + n + "%" + (v2 ? "・超過会心率の変換 +" + n2 + "%" : "");
                case Deck: return "各戦闘の 1 ターン目の手札 +" + n + (v2 ? "・使い捨ての端子を使うたびブロック +" + n2 : "");
                case Role: return "各戦闘の最初の振り直し " + n + " 回が無料" + (v2 ? "・役が立ったターン ブロック +" + n2 : "");
                case Blade: return "撃ごとの攻撃 +" + n;
                case MaxHp: return "最大 HP +" + n + "%（受けた時・上がった分だけ HP も）";
                case HealUp: return "回復 +" + n + "%";
                case Regular: return c.value >= 999f ? "店の軸指定リロールの追加が無料" : "店の軸指定リロールの追加 −" + n + "G";
                case Pursuit: return "ボスのダウン中のターン、 手札 +" + n + (v2 ? "・撃ごとの攻撃 +" + n2 : "");
                case Appraise: return "秘宝の見送り換金の額 +" + n + "%";
            }
            return c.kind;
        }

        /// <summary>難易度 d の引き方を 1 行で (整備の窓に出す)。 例: "開始 I ／ 1→2 I ／ 4→5 大罪"。</summary>
        public static string ScheduleText(int difficulty)
        {
            var sch = Schedule(difficulty);
            if (sch.Count == 0) return "カードを引かない";
            var parts = new List<string>();
            foreach (var e in sch) parts.Add(SlotNames[e.slot] + " " + Roman(e.tier));
            return string.Join(" ／ ", parts);
        }

        public static string Roman(int t) => t == 1 ? "I" : t == 2 ? "II" : t == 3 ? "III" : t == SinTier ? "大罪" : "";
    }
}

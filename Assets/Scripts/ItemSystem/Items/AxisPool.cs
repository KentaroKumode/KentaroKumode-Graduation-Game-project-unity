using System.Collections.Generic;
using GameLoop;
using ItemSystem.PassiveSkills;

namespace ItemSystem
{
    /// <summary><b>動的 4 枠ロック と 2 段階の軸均等の抽選 (2026-11-21・docs/full-item-rework-spec.md §1)。</b>
    ///
    /// <para>ロック: 8 軸 (<see cref="AxisRules.Ranked"/>) の秘宝を取得するたび、 その軸を <see cref="RunState.lockedAxes"/> の末尾へ足す (4 枠まで)。
    /// 4 枠が埋まったら、 秘宝と軸の端子 (TerminalDef.axis) はロックした 4 軸と枠外 (金・軸なし) からだけ出る。 手放しても外れない。</para>
    ///
    /// <para>抽選: 秘宝を引く経路 (店・イベント・宝箱・戦闘報酬・縄張りの宝・交換マス・支給・開幕) は、 レア度で引く前に
    /// <see cref="Narrow"/> で候補を 1 つの軸 (か枠外) に絞る。 軸あり <see cref="AxisShare"/> / 枠外 残り → 軸は出せる軸から等確率。
    /// 絞った後は今までのレア度の引き方 (店の tierWeights・RarityWeightedPicker) がそのまま働き、 無いレア度は引かない (= 別のレアへ落ちる)。
    /// 品の残っていない軸は選ばない (= 別の有効軸へ落ちる)。</para>
    ///
    /// <para>見送り換金 (§1-3): 店以外の秘宝の提示は「見送る」で <see cref="SkipGold"/> を受け取れる。 複数の択の提示を見送ると、 提示の中で一番高いレア度の額。</para></summary>
    public static class AxisPool
    {
        /// <summary>ロックの枠の数。</summary>
        public const int LockSlots = 4;
        /// <summary>抽選の第 1 段: 軸ありの秘宝を引く割合 (残りは枠外 = 金・軸なし・仮)。</summary>
        public const float AxisShare = 0.8f;
        /// <summary>軸指定リロール (店) で、 指定の軸を選ぶ重み (他の軸は 1)。</summary>
        public const float FocusWeight = 4f;
        /// <summary>主軸共鳴 (2026-11-22・neon 最終仕上げ §1-1): ロック後、 4 枠の中で所持数が単独 1 位の軸 (主軸) の重みに掛ける数 (4 軸なら 40:20:20:20)。</summary>
        public const float MainAxisWeight = 2f;
        /// <summary>枠外の呼び名 (計装・画面)。</summary>
        public const string Outside = "枠外";

        /// <summary>8 軸 (ロックの対象) か。 金・軸なしは false。</summary>
        public static bool IsRanked(string axis) => !string.IsNullOrEmpty(axis) && System.Array.IndexOf(AxisRules.Ranked, axis) >= 0;

        /// <summary>4 枠が埋まった (ロック成立)。</summary>
        public static bool IsLocked(RunState run) => run?.lockedAxes != null && run.lockedAxes.Count >= LockSlots;

        /// <summary>その軸の秘宝・端子が出てよいか。 軸なし・金はいつも true。 ロック前は 8 軸とも true。</summary>
        public static bool AxisOpen(RunState run, string axis) => !IsRanked(axis) || !IsLocked(run) || run.lockedAxes.Contains(axis);

        /// <summary>秘宝を取得した時 (PassiveAddHelper.AddPassiveItem): 8 軸の品で、 その軸がまだ無く、 枠が空いていれば末尾へ足す。</summary>
        public static void NoteAcquired(RunState run, string axis)
        {
            if (run == null || !IsRanked(axis)) return;
            if (run.lockedAxes == null) run.lockedAxes = new List<string>();
            if (run.lockedAxes.Contains(axis) || run.lockedAxes.Count >= LockSlots) return;
            run.lockedAxes.Add(axis);
            if (run.lockedAxes.Count == LockSlots) { LocksCompleted++; LockFloorSum += run.currentFloor; }
            if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[出現の枠] {axis} を登録 ({run.lockedAxes.Count}/{LockSlots}{(run.lockedAxes.Count == LockSlots ? "・ロック成立" : "")})");
        }

        /// <summary>主軸: ロック成立後、 4 枠の軸のうち所持数 (<see cref="AxisRules.Count"/>) が単独 1 位の軸。 ロック前・同率 1 位・所持 0 なら null。</summary>
        public static string MainAxis(RunState run)
        {
            if (!IsLocked(run)) return null;
            string best = null; int bestN = 0; bool tie = false;
            foreach (var a in run.lockedAxes)
            {
                int n = AxisRules.Count(run, a);
                if (n > bestN) { bestN = n; best = a; tie = false; }
                else if (n == bestN && n > 0) tie = true;
            }
            return tie ? null : best;
        }

        /// <summary>画面に出す 4 枠 (「[毒][充電][？][？]」)。 地図と店の上に出す。 主軸は「[毒×2]」(主軸共鳴)。</summary>
        public static string SlotsLabel(RunState run)
        {
            var sb = new System.Text.StringBuilder();
            string main = MainAxis(run);
            for (int i = 0; i < LockSlots; i++)
            {
                string a = run?.lockedAxes != null && i < run.lockedAxes.Count ? run.lockedAxes[i] : "？";
                sb.Append('[').Append(a).Append(a == main ? "×2" : "").Append(']');
            }
            return sb.ToString();
        }

        /// <summary>主軸共鳴のルールの 1 文 (店の軸指定リロールの画面に出す・地図と店の 4 枠は「×2」の印)。</summary>
        public const string MainAxisRule = "4 枠が埋まった後、 所持が単独で一番多い軸 (主軸・×2 の印) は出やすさ 2 倍。";

        /// <summary>見送り換金の額 (BRONZE 15 / SILVER 22 / GOLD 35 / LEGENDARY 50・仮)。</summary>
        public static int SkipGold(ItemRarity r) => r switch
        {
            ItemRarity.BRONZE => 15, ItemRarity.SILVER => 22, ItemRarity.GOLD => 35, _ => r >= ItemRarity.LEGENDARY ? 50 : 15,
        };

        /// <summary>提示 (id の並び) を見送った時の額: 提示の中で一番高いレア度の額 (〈銭皿〉を持っていれば +50%・切り上げ・<see cref="PassiveSkills.Effects.CoinDish"/>)。
        /// 秘宝が 1 つも無ければ 0。 <paramref name="run"/> を省くと今のラン。</summary>
        public static int SkipGoldFor(IEnumerable<string> ids, RunState run = null)
        {
            int best = 0;
            if (ids == null) return 0;
            foreach (var id in ids)
            {
                var d = ItemDatabase.Instance?.GetItem(id);
                if (d != null && d.IsPassive) best = System.Math.Max(best, SkipGold(d.rarity));
            }
            return PassiveSkills.Effects.CoinDish.Apply(run ?? GameManager.Instance?.Run, best);
        }

        /// <summary>見送りを受け取る (ゴールドを足して数える)。 実際に入った額を返す。</summary>
        public static int TakeSkip(RunState run, IEnumerable<string> ids, string source)
        {
            int gold = GoldIncome.Gain(run, SkipGoldFor(ids, run), "見送り(" + source + ")");
            Skips++; SkipGoldTotal += gold; RunSkips++; RunSkipGold += gold;
            if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[見送り] {source}: +{gold}G");
            return gold;
        }

        /// <summary><b>2 段階の軸均等の抽選の第 1 段。</b> 秘宝の候補 <paramref name="pool"/> から、 出てよい軸 (<see cref="AxisOpen"/>) の品だけを残し、
        /// 枠外か 1 つの軸に絞った並びを返す (第 2 段のレア度 → 品は呼び手の今までの引き方)。 乱数は <paramref name="key"/>.cat と .axis を毎回 1 回ずつ引く (専用のキー)。
        /// <paramref name="focus"/> = 指定の軸 (重み ×<see cref="FocusWeight"/>・<paramref name="forceFocus"/> ならその軸の品があれば必ずその軸)。 出せる品が無ければ空。</summary>
        public static List<CompleteItemData> Narrow(RunState run, List<CompleteItemData> pool, string key, string focus = null, bool forceFocus = false)
        {
            var outside = new List<CompleteItemData>();
            var byAxis = new Dictionary<string, List<CompleteItemData>>();
            if (pool != null)
                foreach (var it in pool)
                {
                    if (it == null) continue;
                    if (!IsRanked(it.axis)) { outside.Add(it); continue; }
                    if (!AxisOpen(run, it.axis)) continue;
                    if (!byAxis.TryGetValue(it.axis, out var l)) byAxis[it.axis] = l = new List<CompleteItemData>();
                    l.Add(it);
                }
            float cat = GameRng.Value(key + ".cat"), ax = GameRng.Value(key + ".axis");   // 引く本数は分岐で変えない
            // 枠外の指定 (金の軸指定リロール): 枠外の中の金の品
            var outFocus = focus != null && !IsRanked(focus) ? outside.FindAll(it => it.axis == focus) : null;
            if (forceFocus && focus != null)
            {
                if (byAxis.TryGetValue(focus, out var f)) return Note(focus, f);
                if (outFocus != null && outFocus.Count > 0) return Note(Outside, outFocus);
            }
            bool wantAxis = byAxis.Count > 0 && (cat < AxisShare || outside.Count == 0);
            if (!wantAxis)
            {
                //   金の指定の時は、 金の品 1 つの重みを ×FocusWeight にして枠外を引く (金だけに絞るか、 枠外のままか)
                if (outFocus != null && outFocus.Count > 0
                    && ax * (outside.Count + (FocusWeight - 1f) * outFocus.Count) < FocusWeight * outFocus.Count) return Note(Outside, outFocus);
                return Note(Outside, outside);
            }
            // 軸は Ranked の順に並べて重みで引く (辞書の列挙順に頼らない)。 主軸共鳴: 主軸は ×MainAxisWeight (指定の軸の ×FocusWeight と掛け合わせる)
            string main = MainAxis(run);
            float W(string a) => (a == focus ? FocusWeight : 1f) * (a == main ? MainAxisWeight : 1f);
            float total = 0f;
            foreach (var a in AxisRules.Ranked) if (byAxis.ContainsKey(a)) total += W(a);
            float r = ax * total; string pick = null;
            foreach (var a in AxisRules.Ranked)
            {
                if (!byAxis.ContainsKey(a)) continue;
                pick = a;
                if ((r -= W(a)) < 0f) break;
            }
            if (pick == main && main != null) { MainAxisPicks++; RunMainAxisPicks++; }
            return Note(pick, byAxis[pick]);
        }

        /// <summary>レア度と種類の混ざった候補から引いた品 <paramref name="picked"/> が秘宝なら、 同じレア度の秘宝から <see cref="Narrow"/> で引き直す
        /// (経路のレア度の出方と秘宝 / 消耗品の割合はそのまま・軸だけを均す)。 秘宝でなければそのまま。 出せる秘宝が無ければ null。</summary>
        public static CompleteItemData Repick(RunState run, CompleteItemData picked, List<CompleteItemData> pool, string key)
        {
            if (picked == null || !picked.IsPassive) return picked;
            var same = pool.FindAll(it => it != null && it.IsPassive && it.rarity == picked.rarity);
            var n = Narrow(run, same, key);
            return n.Count == 0 ? null : n[GameRng.Range(0, n.Count, key + ".pick")];
        }

        // ---- 計装 (バッチ累計・AutoRunner のサマリーが読む) ----

        /// <summary>第 1 段で選んだ軸 (と <see cref="Outside"/>) の回数。</summary>
        public static readonly Dictionary<string, long> Drawn = new Dictionary<string, long>();
        public static long Skips, SkipGoldTotal, LocksCompleted, LockFloorSum;
        /// <summary>第 1 段で主軸 (主軸共鳴の ×2) を引いた回数 (バッチ累計・「主軸共鳴:回」)。</summary>
        public static long MainAxisPicks;
        /// <summary>今のランの見送り換金の回数と額 (ランの頭で BOT が 0 に戻す・ランの記録の skipFired / skipGold)。</summary>
        public static int RunSkips, RunSkipGold;
        /// <summary>今のランの店の軸指定リロールの回数 (GameManager.ShopReroll・ランの頭で BOT が 0 に戻す・ランの記録の axisRerollFired)。</summary>
        public static int RunAxisRerolls;
        /// <summary>今のランの第 1 段で主軸を引いた回数 (ランの頭で BOT が 0 に戻す・ランの記録の mainAxisPicks)。</summary>
        public static int RunMainAxisPicks;
        public static void ResetStats() { Drawn.Clear(); Skips = SkipGoldTotal = LocksCompleted = LockFloorSum = MainAxisPicks = 0; }

        /// <summary>計装を経路の集計 (PassiveSourceAudit.Counts) へ「軸抽選:」「見送り:」「ロック:」付きで混ぜる (並列ワーカーの返し・通常バッチのサマリー)。</summary>
        public static void WriteStats(Dictionary<string, int> into)
        {
            foreach (var kv in Drawn) into["軸抽選:" + kv.Key] = (int)System.Math.Min(int.MaxValue, kv.Value);
            into["見送り:回"] = (int)Skips; into["見送り:G"] = (int)SkipGoldTotal;
            into["主軸共鳴:回"] = (int)System.Math.Min(int.MaxValue, MainAxisPicks);
            into["ロック:成立"] = (int)LocksCompleted; into["ロック:成立の層の和"] = (int)LockFloorSum;
        }

        private static List<CompleteItemData> Note(string axis, List<CompleteItemData> l)
        {
            if (l.Count > 0) { Drawn.TryGetValue(axis, out long c); Drawn[axis] = c + 1; }
            return l;
        }
    }
}

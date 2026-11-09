using System;
using System.Collections.Generic;

namespace MetaProgression.Relics
{
    /// <summary>
    /// 遺物の抽選・強化・分解 (§15-5・2026-11-10 再編)。 乱数は呼び出し側が渡す (<see cref="System.Random"/>)。
    /// ラン終わりの抽選は <see cref="RngForRun"/> でシードから作るので、 同じシード・同じラン番号なら同じ遺物になる。
    /// </summary>
    public static class RelicRoller
    {
        /// <summary>指名したセットが出る割合 (残りは 6 セット均等)。</summary>
        public const float TargetSetRate = 0.5f;
        /// <summary>★が上の段になる割合 (難易度 8 で★3/★4 が半々)。</summary>
        public const float UpperStarRate = 0.5f;

        /// <summary>ランの終わりに落ちる数。 見せる層 (1〜5) で決める: 3 層以下 1 / 4 層 2 / 5 層到達 3 (5 層で終わったランはクリアと同じ扱い)。</summary>
        public static int DropCount(int ordinalReached) => ordinalReached >= 5 ? 3 : ordinalReached >= 4 ? 2 : 1;

        /// <summary>難易度 (挑戦カード 0..10) → ★の下の段とサブの初期数。 難易度 8 で頭打ち (#7)。
        /// 0〜3: ★1〜2・サブ 0 / 4〜7: ★2〜3・サブ 1 / 8 以上: ★3〜4・サブ 2。</summary>
        public static int LowerStars(int difficulty) => 1 + Math.Min(Math.Max(0, difficulty), RelicCatalog.DifficultyBonusCap) / 4;
        public static int InitialSubs(int difficulty) => Math.Min(Math.Max(0, difficulty), RelicCatalog.DifficultyBonusCap) / 4;

        public static Random RngForRun(int runIndex)
        {
            int seed = (int)(GameLoop.GameRng.Value("relic.drop", runIndex) * int.MaxValue) ^ (runIndex * 7919);
            return new Random(seed);
        }

        /// <summary>1 個引く。 targetSet &lt; 0 なら指名なし (6 セット均等)。</summary>
        public static Relic Roll(Random rng, int difficulty, int targetSet)
        {
            var r = new Relic
            {
                slot = rng.Next(RelicCatalog.SlotCount),
                set = targetSet >= 0 && targetSet < RelicCatalog.SetCount && rng.NextDouble() < TargetSetRate
                    ? targetSet : rng.Next(RelicCatalog.SetCount),
            };
            r.stars = Math.Min(RelicCatalog.MaxStars, LowerStars(difficulty) + (rng.NextDouble() < UpperStarRate ? 1 : 0));
            int subs = InitialSubs(difficulty);
            for (int i = 0; i < subs; i++) AddSub(r, rng);
            return r;
        }

        /// <summary>ランの終わりの抽選 (落ちる数ぶん)。</summary>
        public static List<Relic> RollRunEnd(int runIndex, int ordinalReached, int difficulty, int targetSet)
        {
            var rng = RngForRun(runIndex);
            var list = new List<Relic>();
            int n = DropCount(ordinalReached);
            for (int i = 0; i < n; i++) list.Add(Roll(rng, difficulty, targetSet));
            return list;
        }

        /// <summary>サブを 1 個足す (段 1)。 主ステと同じ・既に持っている接辞は出ない。 重みはセット寄せ。</summary>
        private static bool AddSub(Relic r, Random rng)
        {
            if (r.SubCount >= RelicCatalog.MaxSubs) return false;
            float total = 0f;
            var pool = RelicCatalog.SubPool;
            var w = new float[pool.Length];
            for (int i = 0; i < pool.Length; i++)
            {
                var s = pool[i];
                if (s == r.MainStat || r.subStats.Contains((int)s)) continue;
                w[i] = RelicCatalog.SubWeight(r.Set, s); total += w[i];
            }
            if (total <= 0f) return false;
            double x = rng.NextDouble() * total;
            for (int i = 0; i < pool.Length; i++)
            {
                if (w[i] <= 0f) continue;
                x -= w[i];
                if (x <= 0 || i == pool.Length - 1) { r.subStats.Add((int)pool[i]); r.subSteps.Add(1); return true; }
            }
            return false;
        }

        /// <summary>強化 1 段。 サブが 3 個未満なら 1 個増やし、 3 個なら 2 段未満のサブのどれかを +1 する。 できなければ false。</summary>
        public static bool Enhance(Relic r, Random rng)
        {
            if (r == null || !r.CanEnhance) return false;
            if (r.SubCount < RelicCatalog.MaxSubs && AddSub(r, rng)) { r.enhance++; return true; }
            var open = new List<int>();
            for (int i = 0; i < r.SubCount; i++) if (r.subSteps[i] < RelicCatalog.SubStepMax) open.Add(i);
            if (open.Count == 0) { r.enhance++; return true; }   // 伸ばす先が無い (主ステの重複で枯れた) ── 段だけ進める
            r.subSteps[open[rng.Next(open.Count)]]++;
            r.enhance++;
            return true;
        }
    }
}

using System.Collections.Generic;
using GameLoop;
using UnityEngine;

namespace MetaProgression.Relics
{
    /// <summary>
    /// 遺物の生成。 正本: docs/GAME.md §15-5。
    /// 検証ツール docs/tools/relic-roller.html が同じ手順を再実装しているので、
    /// **数値を変えたら 3 箇所（本ファイル / GAME.md / html）すべてを直す**。
    ///
    /// 抽選の順序:
    ///   ① 到達層 → 層点
    ///   ② 挑戦スコア → サブ(3)・サブ(4) の出現判定（サブ4 はサブ3 が出た時のみ）
    ///   ③ ロール +1/+2/+3 を 60/30/10%
    ///   ④ 充填率 f = (層点 + 難度点 + ロール) / 22 を枠数ごとの点数レンジへ写す
    ///   ⑤ その総点になる段の組を全列挙し、 充填率の散らばりで重み付けして抽選
    ///   ⑥ 通常軸 (高難易度限定と引退済みを除く 11 本) から重複なしで割り当て
    ///   ⑦ 挑戦スコア 25 以上なら メインを高難易度限定軸へ差し替え判定 → 刻印を判定
    ///
    /// **クランプは行わない。** 総点は構造上つねに枠数ごとのレンジ内に収まるので、
    /// 上限にも下限にも張り付かない。 旧仕様（層点 + 2×追加サブ + ロール を切り捨て）では
    /// 難度0・7層クリア・3枠で必ず 12 に張り付き、組が (6,3,3) の 1 通りに固定されていた（§24）。
    ///
    /// 乱数は全て GameRng（キー指定）を通す。 シード固定で再現できること＝
    /// paircmp によるペア比較が成立する条件。
    /// </summary>
    public static class RelicRoller
    {
        public const int TotalCap = 29;          // メイン9 + サブ5×4 (2026-08-08 に 18 から引き上げ)
        /// <summary>充填率の分母 = 層点max(11) + 難度点max(8) + ロールmax(3)。</summary>
        public const int FillDenominator = 22;
        private const float Lambda = 2.0f;       // 平均化バイアスの強さ
        public const int ChallengeScoreMax = 50; // §15-2β を採用
        public const int CurseMinScore = 25;

        // ── 層点。 4層死(4) → 5層クリア(7) の +3 が「死んだ/クリアした」の壁 ──
        /// <summary>到達層と決着種別から層点を引く。 floor は 1〜7、 cleared は
        /// 「その層のボスを倒したか」。 5 層以降のクリアだけが大きく跳ねる。
        ///
        /// **死亡時も、到達した層は「その 1 つ下をクリアした証拠」として扱う** (2026-08-04)。
        /// 6 層に居るなら 5 層ボスは倒しているので 5 層クリアと同点、 7 層なら 6 層クリアと同点。
        ///
        /// 旧実装は死亡を一律 `clamp(f,1,4)` にしていたため、 **7 層で死んでも 2 層で死んでも
        /// ほぼ同じ点**という負のフィードバックを作っていた ── 難易度を上げると浅く死ぬ →
        /// 層点が落ちる → 遺物が弱くなる、で挑戦スコアが 13 前後で固着する。 §24 参照。
        /// なお AutoRunner の周回モードは呼び出し側で同じ読み替えを既に行っていたので、
        /// **この修正で実ゲームが周回モードの測定条件に一致する**（測定値は動かない）。</summary>
        public static int LayerPoint(int floor, bool cleared)
        {
            int f = Mathf.Clamp(floor, 1, 7);

            // **5 層で頭打ち** (2026-08-08)。 5 層に到達した時点で層点は最大。
            //   6/7 層まで潜っても層点は増えない ── 深層は挑戦スコアで報われるべきで、
            //   層点でも二重に報いると「深く行けるビルド」だけが遺物でも有利になり格差が開く。
            //   代わりに **4 層以下との落差を大きく取る** (4 → 11 の +7)。
            //   「5 層まで行けたか」が周回成長の主要な分岐点になる。
            if (f >= 5) return LayerPointCap;

            // 4 層以下は小さいまま。 クリア/死亡は同格 (どちらも 5 層に届いていない)。
            return Mathf.Clamp(f, 1, 4);
        }

        /// <summary>層点の最大値 (= 5 層以上に到達したときの値)。</summary>
        public const int LayerPointCap = 11;

        /// <summary>**遺物への難易度ボーナスの頭打ち** (2026-08-04)。
        ///
        /// 挑戦スコア 31 以降は遺物の見返りが一切増えない ＝ **純粋なチャレンジ難易度**と規定する。
        /// 旧実装は `ChallengeScoreMax`(50) を分母にしていたため、 実測のラチェット上限 4〜13 では
        /// `DifficultyPoint` が常に 0 で、 難易度を上げる動機が構造的に存在しなかった。
        /// 頭打ちを 30 に寄せることで、 到達しうる帯で見返りが実際に動く。
        /// **上限値そのものは変えていない** ── スコア 30 で旧スコア 50 と同じ値に達する。</summary>
        public const int RelicBonusCap = 30;
        private static int CappedScore(int s) => Mathf.Clamp(s, 0, RelicBonusCap);

        // ── 難易度は枠数と充填率を動かす ──
        public static float Sub3Rate(int challengeScore)
            => Mathf.Clamp01(0.30f + CappedScore(challengeScore) * 0.0217f);
        public static float Sub4Rate(int challengeScore)
            => Mathf.Clamp01(0.05f + CappedScore(challengeScore) * 0.0267f);

        /// <summary>難易度による充填点。 **0〜8** (2026-08-08 に 0〜2 から大幅増)。
        ///
        /// 旧値は分子 16 のうち最大 2 点しか動かず、 0pt と 30pt で遺物の質が 12.5% しか
        /// 変わらなかった。 高難易度に見返りが無く、 挑戦する動機が構造的に存在しない状態。
        /// 8 点にすると分母 22 のうち 36% を難易度が占め、 層点 (最大 11) に次ぐ主要因になる。
        /// **30pt で頭打ちは維持** ── それ以降は勝率が低すぎて周回が成立しないため、
        /// 見返りを伸ばしても「苦痛な周回」を強いるだけになる。</summary>
        public static int DifficultyPoint(int challengeScore)
            => Mathf.RoundToInt(CappedScore(challengeScore) * 8f / RelicBonusCap);
        /// <summary>刻印の出現率。 挑戦スコア 25 未満では出ない。 30 で頭打ち。</summary>
        public static float CurseRate(int challengeScore)
            => challengeScore < CurseMinScore ? 0f
             : Mathf.Clamp01(0.20f + (CappedScore(challengeScore) - CurseMinScore) * 0.02f);

        public static int StructLow(int slots)  => RelicAxisCatalog.MainStepMin
                                                 + (slots - 1) * RelicAxisCatalog.SubStepMin;
        public static int StructHigh(int slots) => Mathf.Min(TotalCap, RelicAxisCatalog.MainStepMax
                                                 + (slots - 1) * RelicAxisCatalog.SubStepMax);

        /// <summary>ラン終了時に 1 個生成する。 rngIndex は同一シード内で衝突しないための連番
        /// （通常はラン番号を渡す）。</summary>
        public static RolledRelic Roll(int floor, bool cleared, int challengeScore, int rngIndex = -1)
        {
            int layerPt = LayerPoint(floor, cleared);

            bool has3 = GameRng.Chance(Sub3Rate(challengeScore), "relic.sub3", rngIndex);
            bool has4 = has3 && GameRng.Chance(Sub4Rate(challengeScore), "relic.sub4", rngIndex);
            int slots = 3 + (has3 ? 1 : 0) + (has4 ? 1 : 0);

            // ロール +1(60%) / +2(30%) / +3(10%)
            float r = GameRng.Value("relic.roll", rngIndex);
            int bonus = r < 0.60f ? 1 : (r < 0.90f ? 2 : 3);

            int total = TotalFor(layerPt, challengeScore, slots, bonus);

            var steps = PickComposition(slots, total, rngIndex);
            var axes  = PickAxes(slots, rngIndex, challengeScore);

            var relic = new RolledRelic
            {
                sourceLayerPoint = layerPt,
                sourceChallengeScore = challengeScore,
                curse = (int)RollCurse(challengeScore, rngIndex),
            };
            for (int i = 0; i < slots; i++) { relic.axes.Add((int)axes[i]); relic.steps.Add(steps[i]); }
            return relic;
        }

        /// <summary>充填率を枠数ごとのレンジへ写して総点を出す。 **クランプ不要**。</summary>
        public static int TotalFor(int layerPoint, int challengeScore, int slots, int rollBonus)
        {
            float f = (layerPoint + DifficultyPoint(challengeScore) + rollBonus) / (float)FillDenominator;
            int lo = StructLow(slots), hi = StructHigh(slots);
            return Mathf.RoundToInt(lo + f * (hi - lo));
        }

        public static RelicCurse RollCurse(int challengeScore, int rngIndex)
        {
            if (!GameRng.Chance(CurseRate(challengeScore), "relic.curse", rngIndex))
                return RelicCurse.None;
            // None を除いた 4 種から一様に引く
            int n = (int)RelicCurse.NoRoleFired;                     // = 4
            return (RelicCurse)(1 + GameRng.Range(0, n, "relic.curseKind", rngIndex));
        }

        // ============================================================
        //  段の配分（平均化バイアス）
        // ============================================================

        /// <summary>合計 total になる段の組を全列挙する。 index 0 がメイン枠。</summary>
        public static List<int[]> Compositions(int slots, int total)
        {
            var outList = new List<int[]>();
            Recurse(0, total, slots, new int[slots], outList);
            return outList;
        }

        private static void Recurse(int idx, int rest, int slots, int[] acc, List<int[]> outList)
        {
            if (idx == slots) { if (rest == 0) outList.Add((int[])acc.Clone()); return; }
            int lo = idx == 0 ? RelicAxisCatalog.MainStepMin : RelicAxisCatalog.SubStepMin;
            int hi = idx == 0 ? RelicAxisCatalog.MainStepMax : RelicAxisCatalog.SubStepMax;
            int left = slots - idx - 1;
            for (int v = lo; v <= hi; v++)
            {
                int rem = rest - v;
                // 残り枠で埋められない値は試さない（枝刈り）
                if (rem < left * RelicAxisCatalog.SubStepMin) continue;
                if (rem > left * RelicAxisCatalog.SubStepMax) continue;
                acc[idx] = v;
                Recurse(idx + 1, rem, slots, acc, outList);
            }
        }

        /// <summary>充填率の散らばり。 メインとサブでレンジが違うので、
        /// 素の分散ではなく 0〜1 に正規化してから測る。</summary>
        public static float Spread(int[] comp)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < comp.Length; i++)
            {
                float f = i == 0
                    ? (comp[i] - RelicAxisCatalog.MainStepMin)
                      / (float)(RelicAxisCatalog.MainStepMax - RelicAxisCatalog.MainStepMin)
                    : (comp[i] - RelicAxisCatalog.SubStepMin)
                      / (float)(RelicAxisCatalog.SubStepMax - RelicAxisCatalog.SubStepMin);
                if (f < mn) mn = f;
                if (f > mx) mx = f;
            }
            return mx - mn;
        }

        /// <summary>重み exp(−λ·散らばり) で 1 組選ぶ。 λ=2.0 で
        /// 3枠合計9 なら最平坦 37.3% / 最尖鋭 7.0%（§15-5 の表）。</summary>
        public static int[] PickComposition(int slots, int total, int rngIndex)
        {
            var cs = Compositions(slots, total);
            if (cs.Count == 0)
            {
                // 充填率方式では起きないはず。 起きたら計算が壊れているので落とさず記録する。
                Debug.LogWarning($"[RelicRoller] 合計{total}/{slots}枠 の組が存在しない。 下限で代用する。");
                var fallback = new int[slots];
                fallback[0] = RelicAxisCatalog.MainStepMin;
                for (int i = 1; i < slots; i++) fallback[i] = RelicAxisCatalog.SubStepMin;
                return fallback;
            }
            float sum = 0f;
            var w = new float[cs.Count];
            for (int i = 0; i < cs.Count; i++) { w[i] = Mathf.Exp(-Lambda * Spread(cs[i])); sum += w[i]; }
            float r = GameRng.Value("relic.comp", rngIndex) * sum;
            for (int i = 0; i < cs.Count; i++) { r -= w[i]; if (r <= 0f) return cs[i]; }
            return cs[cs.Count - 1];
        }

        // ============================================================
        //  軸の割り当て
        // ============================================================

        /// <summary>挑戦スコアがこの値以上のときだけ <see cref="RelicAxisCatalog.HighDifficultyOnly"/>
        /// の軸が出る。 刻印の出現条件 (<see cref="CurseMinScore"/>) と同じ 25 に揃えてある ──
        /// 「25 で遺物の性質が変わる」という一本の線にして、 覚える境目を増やさないため。</summary>
        public const int HighDifficultyAxisMinScore = 25;

        /// <summary>25pt 以上のとき、 メイン枠が高難易度限定軸になる確率。
        /// 高すぎると通常 12 軸がメインから消えてビルドの幅が減り、 低すぎると
        /// 「高難易度でしか出ない」という手触りが立たない。 40% で 2 回に 1 回弱。</summary>
        public const float HighDifficultyAxisRate = 0.40f;

        /// <summary>軸を重複なしで slots 本引く。 Fisher-Yates の前半だけ回す。
        ///
        /// **高難易度限定軸は挑戦 25pt 以上でのみ、 しかもメイン枠 (index 0) にだけ出る。**
        /// サブ段 (1〜5) に落ちると効果が小さすぎて、 その軸が要求する賭け
        /// （Λ へ潜る・希望を切らす・長期戦を選ぶ・低HP で走る）に見合わず死に枠になるため。</summary>
        public static RelicAxis[] PickAxes(int slots, int rngIndex, int challengeScore = 0)
        {
            bool highDiffAllowed = challengeScore >= HighDifficultyAxisMinScore;

            var list = new List<RelicAxis>();
            foreach (var a in RelicAxisCatalog.All)
                if (!RelicAxisCatalog.IsHighDifficultyOnly(a) && !RelicAxisCatalog.IsRetired(a))
                    list.Add(a);

            var pool = list.ToArray();
            int n = Mathf.Min(slots, pool.Length);
            for (int i = 0; i < n; i++)
            {
                int j = i + GameRng.Range(0, pool.Length - i, "relic.axis" + i, rngIndex);
                var t = pool[i]; pool[i] = pool[j]; pool[j] = t;
            }
            var result = new RelicAxis[n];
            System.Array.Copy(pool, result, n);

            // メイン枠を一定確率で高難易度限定軸へ差し替える。 4 本から一様に 1 本。
            if (highDiffAllowed && n > 0
                && GameRng.Chance(HighDifficultyAxisRate, "relic.highDiffAxis", rngIndex))
            {
                var hd = RelicAxisCatalog.HighDifficultyOnly;
                result[0] = hd[GameRng.Range(0, hd.Length, "relic.highDiffKind", rngIndex)];
            }

            return result;
        }
    }
}

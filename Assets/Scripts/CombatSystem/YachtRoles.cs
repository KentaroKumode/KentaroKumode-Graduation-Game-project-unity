using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>
    /// ADR-0010 柱2・柱4: 役の種別。 **並び順を変えない** ──
    /// 使用済み管理を将来セーブへ載せる可能性があるため、 追加は末尾へ。
    ///
    /// 正本は docs/adr/0010-yacht-roles.md 柱4 の表。 検討記録は docs/design-yacht.md。
    /// </summary>
    public enum RoleKind
    {
        // ── 端子役 (同一端子に配線した組で判定) ──
        Pair = 0,        // 対   2個が同値
        Triple,          // 束   3個が同値
        SmallRun,        // 小階 3個が連番
        SkipRun,         // 飛階 2つ飛ばしの連番 (1,3,5)
        AllEven,         // 偶   全て偶数 (3個以上)
        AllOdd,          // 奇   全て奇数 (3個以上)
        AllDifferent,    // 散   3個以上が全て異なる

        // ── 手札役 (5個を振り終えた時点・配線に無関係) ──
        TwoPair,         // 二対 同値ペアが2組
        Quad,            // 大束 4個が同値
        Yacht,           // 極   5個が同値
        FullHouse,       // 満   3個同値 + 2個同値
        MediumRun,       // 中階 4個が連番
        LargeRun,        // 大階 5個が連番

        // ── 配線役 (盤面全体・敵の数値を読む) ──
        Balance,         // 均         攻撃合計 = ブロック合計
        Offset,          // 相殺       ブロック合計 = 敵攻撃値ちょうど
        // [リワーク 2026-08-22] 旧〈過不足なし〉= 攻撃合計 = 敵残HP ちょうど。
        //   **構造的に死んでいた** ── 攻撃合計 (10〜35) と敵残HP (201〜4,418) はスケールが違い、
        //   成立するのは敵が瀕死のときだけ。 その時は既に確殺なので発動する意味がなく、
        //   実測 1000ラン で 成立 143 / **発動 0**。 兄弟 2 役が「ダイス目同士」を比べているのに
        //   ここだけ「ダイス目 vs HP」を比べていた。 **列挙値は変えない** (並び順の規約)。
        MatchedStrike,   // 拮抗       攻撃合計 = 敵攻撃値ちょうど
    }

    /// <summary>役の判定範囲。 **必要ダイス数で分ける** (ADR-0010 柱2)。
    ///
    /// 4〜5 個を要する役を「同一端子」縛りにすると、 満(3+2)・極(5個)・大階(5連) は
    /// 攻撃かブロックのどちらかに全部入れるしかなくなり、 **配線の判断が消滅する**
    /// (充電もゼロになる)。 だからスコープを分ける。</summary>
    public enum RoleScope
    {
        /// <summary>手札役: 5個を振り終えた時点で判定。 配線に無関係。</summary>
        Hand,
        /// <summary>端子役: その端子へ配線した組で判定。 **同一端子に置く必要がある**。</summary>
        Terminal,
        /// <summary>配線役: 端子合計と敵の数値から判定。 盤面全体。</summary>
        Wiring,
    }

    /// <summary>
    /// ADR-0010 役システムの判定。 **副作用を持たない純関数の集まり**にしてある ──
    /// 戦闘パイプラインから独立させ、 成立率を単体で検算できるようにするため
    /// (docs/design-yacht.md のダイス表は本クラスで再現できる)。
    ///
    /// 効果の適用は行わない。 ここは「どの役が成立しているか」だけを答える。
    /// </summary>
    public static class YachtRoles
    {
        /// <summary>全役。 順序は enum 定義順。</summary>
        public static readonly RoleKind[] All =
            (RoleKind[])System.Enum.GetValues(typeof(RoleKind));

        /// <summary>分布系 (偶/奇/散) が成立するのに要る最小本数。
        /// これが無いと「1 本だけ置けば全部偶数」で自明に成立してしまう。</summary>
        public const int DistributionMinCount = 3;

        public static RoleScope ScopeOf(RoleKind k)
        {
            switch (k)
            {
                case RoleKind.TwoPair:
                case RoleKind.Quad:
                case RoleKind.Yacht:
                case RoleKind.FullHouse:
                case RoleKind.MediumRun:
                case RoleKind.LargeRun:
                    return RoleScope.Hand;
                case RoleKind.Balance:
                case RoleKind.Offset:
                case RoleKind.MatchedStrike:
                    return RoleScope.Wiring;
                default:
                    return RoleScope.Terminal;
            }
        }

        /// <summary>表示名 (日本語 1〜4 文字)。 ログと BOT レポートで使う。</summary>
        public static string NameOf(RoleKind k)
        {
            switch (k)
            {
                case RoleKind.Pair:         return "対";
                case RoleKind.Triple:       return "束";
                case RoleKind.SmallRun:     return "小階";
                case RoleKind.SkipRun:      return "飛階";
                case RoleKind.AllEven:      return "偶";
                case RoleKind.AllOdd:       return "奇";
                case RoleKind.AllDifferent: return "散";
                case RoleKind.TwoPair:      return "二対";
                case RoleKind.Quad:         return "大束";
                case RoleKind.Yacht:        return "極";
                case RoleKind.FullHouse:    return "満";
                case RoleKind.MediumRun:    return "中階";
                case RoleKind.LargeRun:     return "大階";
                case RoleKind.Balance:      return "均";
                case RoleKind.Offset:       return "相殺";
                case RoleKind.MatchedStrike: return "拮抗";
                default:                    return "?";
            }
        }

        // ============================================================
        //  判定
        // ============================================================

        /// <summary>手札役。 5 個全体から成立するものを返す。 配線には一切依存しない。</summary>
        public static void EvaluateHand(int[] dice, List<RoleKind> into)
        {
            if (into == null) return;
            if (dice == null || dice.Length == 0) return;

            int maxSame = MaxSameCount(dice, out int distinctCount, out int pairGroups);
            // 連番長と同値数は 1 回で足りる。 先読み方策 (SuperCombatAI) が本関数を
            // 1 ターンに数万回叩くので、 同じ走査を二度しない (結果は同一)。
            int run = LongestRun(dice, 1);

            if (pairGroups >= 2)             into.Add(RoleKind.TwoPair);
            if (maxSame >= 4)                into.Add(RoleKind.Quad);
            if (maxSame >= 5)                into.Add(RoleKind.Yacht);
            // 満 = ちょうど 3 個同値 + 2 個同値。 大束 (4個) は含めない。
            if (dice.Length == 5 && maxSame == 3 && distinctCount == 2) into.Add(RoleKind.FullHouse);
            if (run >= 4)                    into.Add(RoleKind.MediumRun);
            if (run >= 5)                    into.Add(RoleKind.LargeRun);
        }

        /// <summary>端子役。 **その端子へ配線した組**から成立するものを返す。
        ///
        /// 2-2 が出ても片方を攻撃・片方をブロックへ割ったらどちらも素の 2 のまま ──
        /// これがあるから分割が悩ましくなる (役と配線が同じダイスを取り合う)。</summary>
        public static void EvaluateTerminal(int[] group, List<RoleKind> into)
        {
            if (into == null) return;
            if (group == null || group.Length == 0) return;

            int maxSame = MaxSameCount(group, out int distinctCount, out _);

            if (maxSame >= 2)             into.Add(RoleKind.Pair);
            if (maxSame >= 3)             into.Add(RoleKind.Triple);
            if (LongestRun(group, 1) >= 3) into.Add(RoleKind.SmallRun);
            if (LongestRun(group, 2) >= 3) into.Add(RoleKind.SkipRun);

            if (group.Length >= DistributionMinCount)
            {
                if (AllParity(group, even: true))  into.Add(RoleKind.AllEven);
                if (AllParity(group, even: false)) into.Add(RoleKind.AllOdd);
                if (distinctCount == group.Length) into.Add(RoleKind.AllDifferent);
            }
        }

        /// <summary>配線役。 端子合計と敵の数値から成立するものを返す。
        ///
        /// **成立率は確率ではなくプレイヤーの計算能力で決まる** ＝ 技量帯の本体。
        /// ADR-0009 柱3 の完全情報テレグラフが、 ここで初めて「読むための道具」になる。</summary>
        public static void EvaluateWiring(int attackSum, int blockSum, int enemyAttackValue,
                                          int enemyCurrentHp, List<RoleKind> into)
        {
            if (into == null) return;

            // **0 同士の一致は役にしない。** 何も配線しないターンが〈均〉で報われるのは筋が通らない。
            if (attackSum > 0 && attackSum == blockSum) into.Add(RoleKind.Balance);
            if (blockSum  > 0 && blockSum  == enemyAttackValue) into.Add(RoleKind.Offset);
            // 〈拮抗〉: **攻撃を相手の攻撃値ちょうどに合わせて殴り返す**。
            //   旧〈過不足なし〉は敵残HP と比べていてスケールが合わず死んでいた (enum のコメント参照)。
            //   ここは兄弟 2 役と同じく**ダイス目同士**の比較なので成立しうる。
            if (attackSum > 0 && attackSum == enemyAttackValue) into.Add(RoleKind.MatchedStrike);
        }

        // ============================================================
        //  判定の部品
        // ============================================================

        /// <summary>最も多い同値の個数。 併せて相異なる値の個数と、
        /// 2 個以上重複している値の種類数 (二対の判定用) を返す。</summary>
        public static int MaxSameCount(int[] v, out int distinctCount, out int pairGroups)
        {
            distinctCount = 0; pairGroups = 0;
            if (v == null || v.Length == 0) return 0;

            // 本数が最大 5 なので、 辞書を作らず O(n^2) で数える方が速くゴミも出ない。
            int best = 0;
            for (int i = 0; i < v.Length; i++)
            {
                bool firstOccurrence = true;
                int count = 0;
                for (int j = 0; j < v.Length; j++)
                {
                    if (v[j] != v[i]) continue;
                    count++;
                    if (j < i) firstOccurrence = false;
                }
                if (!firstOccurrence) continue;   // 同じ値を二度数えない
                distinctCount++;
                if (count >= 2) pairGroups++;
                if (count > best) best = count;
            }
            return best;
        }

        /// <summary>ちょうど 3 個同値 + 2 個同値 か (フルハウス)。
        /// **大束 (4個同値) は満に含めない** ── 別の役として立っているため。</summary>
        public static bool IsFullHouse(int[] v)
        {
            if (v == null || v.Length != 5) return false;
            int max = MaxSameCount(v, out int distinct, out _);
            return max == 3 && distinct == 2;
        }

        /// <summary>最長の等差列の長さ。 step=1 で階系、 step=2 で飛階 (1,3,5)。
        ///
        /// **重複は 1 個として扱う** ── 3,3,4,5 は「3,4,5」で長さ 3 と数える。
        /// step を引数にしたのは、 階系と飛階で同じロジックを二重に持たないため。
        ///
        /// **ソート列の隣接判定にしてはいけない** (2026-08-09 修正)。 step≥2 では
        /// 間に別の値が挟まると切れてしまう ── {1,2,3,5} は {1,3,5} という飛階を含むのに、
        /// ソート列 [1,2,3,5] の隣接判定では 1→2 で切れて成立しない。
        /// step=1 なら連番は必ずソート列で隣接するので問題は出ないが、 飛階では誤る。
        /// 各値を起点に v, v+step, v+2step… の存在を数える形にしてある。</summary>
        public static int LongestRun(int[] v, int step)
        {
            if (v == null || v.Length == 0 || step <= 0) return 0;

            int best = 1;
            for (int i = 0; i < v.Length; i++)
            {
                // v[i] を起点にする。 起点の重複走査を避けるため、 v[i]-step が存在するなら飛ばす
                bool hasPrev = false;
                for (int j = 0; j < v.Length; j++) if (v[j] == v[i] - step) { hasPrev = true; break; }
                if (hasPrev) continue;

                int len = 1, next = v[i] + step;
                while (true)
                {
                    bool found = false;
                    for (int j = 0; j < v.Length; j++) if (v[j] == next) { found = true; break; }
                    if (!found) break;
                    len++; next += step;
                }
                if (len > best) best = len;
            }
            return best;
        }

        /// <summary>全て偶数か (even=true) / 全て奇数か (even=false)。
        ///
        /// **連番は必ず偶奇が交互になる**ので、 全偶／全奇のダイスでは階系が原理的に成立しない。
        /// これが ADR-0010 柱5 で「特化と引き換えの不能」を作る唯一のきれいな軸になっている。</summary>
        public static bool AllParity(int[] v, bool even)
        {
            if (v == null || v.Length == 0) return false;
            for (int i = 0; i < v.Length; i++)
            {
                bool isEven = (v[i] % 2) == 0;
                if (isEven != even) return false;
            }
            return true;
        }
    }
}

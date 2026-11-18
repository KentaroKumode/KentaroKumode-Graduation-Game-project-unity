using System.Collections.Generic;
using GameLoop;

namespace CombatSystem
{
    /// <summary>
    /// ADR-0011: 役の種別。 **目 (1〜9) × {2 個そろい, ゾロ目}** の 18 種。
    ///
    /// <para>並びは <c>(目 − 1) × 2 + (ゾロ目なら 1)</c>。 <see cref="YachtRoles.Of"/> /
    /// <see cref="YachtRoles.FaceOf"/> / <see cref="YachtRoles.IsTriple"/> で出入りする。
    /// 旧 16 役 (ADR-0010) は 2026-10-18 に全廃した。</para>
    /// </summary>
    public enum RoleKind
    {
        Pair1 = 0, Triple1,
        Pair2, Triple2,
        Pair3, Triple3,
        Pair4, Triple4,
        Pair5, Triple5,
        Pair6, Triple6,
        Pair7, Triple7,
        Pair8, Triple8,
        Pair9, Triple9,
        /// <summary>連なり (ADR-0015・2026-11-18): 3 つの目が連番 (例 2・3・4)。 連なりの賽を持つ時だけ成立する。
        /// 目の役ではないので <see cref="YachtRoles.FaceOf"/> は 10 (どの目でもない) を返す。</summary>
        Straight,
    }

    /// <summary>
    /// ADR-0011 役システムの判定。 **副作用を持たない純関数**。 効果の適用は
    /// <see cref="YachtRoleEffects"/> が行い、 ここは「どの役が成立しているか」だけを答える。
    ///
    /// <para>役は<b>振った時点の 3 個</b>で判定し、 端子への置き方は条件にしない。
    /// 成立するかどうかは「その目の面を持っているか」(パーツの Tier) で決まる ──
    /// T2 以上で 2 個そろい、 T3 以上でゾロ目。 T1 (素の面) は何も起きない。</para>
    /// </summary>
    public static class YachtRoles
    {
        /// <summary>役の総数 (配列の長さ)。 目 9 × {2 個そろい, ゾロ目} の 18 と、 連なり。</summary>
        public const int Count = 19;

        /// <summary>全役。 順序は enum 定義順。</summary>
        public static readonly RoleKind[] All = (RoleKind[])System.Enum.GetValues(typeof(RoleKind));

        public static RoleKind Of(int face, bool triple)
            => (RoleKind)((face - 1) * 2 + (triple ? 1 : 0));

        public static int FaceOf(RoleKind k) => (int)k / 2 + 1;

        public static bool IsTriple(RoleKind k) => ((int)k & 1) == 1;

        /// <summary>目ごとのテーマ名。</summary>
        public static string ThemeOf(int face)
        {
            switch (face)
            {
                case 1: return "特別";
                case 2: return "封じ";
                case 3: return "追撃";
                case 4: return "攻め";
                case 5: return "守り";
                case 6: return "時間";
                case 7: return "充電";
                case 8: return "癒し";
                case 9: return "富";
                default: return "?";
            }
        }

        /// <summary>表示名。 ログと BOT レポートで使う。 例: 3 の 2 個そろい → 「追撃・対」。</summary>
        public static string NameOf(RoleKind k)
            => k == RoleKind.Straight ? "連なり" : ThemeOf(FaceOf(k)) + (IsTriple(k) ? "・揃" : "・対");

        /// <summary>連なり (3 つの連番) が役になるか: 連なりの賽を持っている時だけ。</summary>
        public static bool StraightsEnabled()
            => EquippedDie.IdOf(GameManager.Instance?.Run) == DieIds.Straight;

        /// <summary>出た 3 つの目 (<paramref name="dice"/>) から成立する役を <paramref name="into"/> へ足す。
        ///
        /// <para><b>Tier は見ない</b> (ADR-0015・作者 2026-11-18: 賽がその目の面を持っていれば、 2 個そろいもゾロ目も効く)。
        /// 2 個そろい = ちょうど 2 つ、 ゾロ目 = 3 つ。 連なりの賽を持つ時だけ、 3 つの連番 (<see cref="RoleKind.Straight"/>) も役になる。</para></summary>
        /// <param name="faceIdx">使わない (呼び手の互換のため残す)。</param>
        /// <param name="tiers">使わない (呼び手の互換のため残す)。</param>
        public static void Evaluate(int[] dice, int[] faceIdx, DiceFaceParts.Tier[] tiers, List<RoleKind> into)
        {
            if (into == null || dice == null || dice.Length < 2) return;

            if (dice.Length == 3 && StraightsEnabled())
            {
                int a = dice[0], b = dice[1], c = dice[2];
                int lo = System.Math.Min(a, System.Math.Min(b, c)), hi = System.Math.Max(a, System.Math.Max(b, c));
                if (a != b && b != c && a != c && hi - lo == 2) { into.Add(RoleKind.Straight); return; }
            }

            for (int i = 0; i < dice.Length; i++)
            {
                int v = dice[i];
                if (v < DiceFaceParts.MinFace || v > DiceFaceParts.MaxFace) continue;

                bool firstOccurrence = true;
                int count = 0;
                for (int j = 0; j < dice.Length; j++)
                {
                    if (dice[j] != v) continue;
                    count++;
                    if (j < i) firstOccurrence = false;
                }
                if (!firstOccurrence || count < 2) continue;

                into.Add(Of(v, count >= 3));
            }
        }

        /// <summary>ダイス <paramref name="die"/> が止まっている面の Tier。 分からなければ None。</summary>
        public static DiceFaceParts.Tier TierAt(DiceFaceParts.Tier[] tiers, int[] faceIdx, int die)
        {
            if (tiers == null || faceIdx == null || die < 0 || die >= faceIdx.Length) return DiceFaceParts.Tier.None;
            int fi = faceIdx[die];
            if (fi < 0 || fi >= tiers.Length) return DiceFaceParts.Tier.None;
            return tiers[fi];
        }

        /// <summary>出目の中で最も多い同値の個数。 先読み方策と計装が使う。</summary>
        public static int MaxSameCount(int[] v)
        {
            if (v == null || v.Length == 0) return 0;
            int best = 0;
            for (int i = 0; i < v.Length; i++)
            {
                int c = 0;
                for (int j = 0; j < v.Length; j++) if (v[j] == v[i]) c++;
                if (c > best) best = c;
            }
            return best;
        }
    }
}

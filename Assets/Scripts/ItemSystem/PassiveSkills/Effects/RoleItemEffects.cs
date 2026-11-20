using GameLoop;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  賽目 (役の軸) の品のうち、 出来事で書く品 (2026-11-21・段 1-f・item-overhaul-spec §5 / §12-2・名前は仮・数値は仮)。
    //  ほかの 6 品は効果クラスを持たない:
    //    見切りの木札・符丁の刻み板 = 端子の解決の中 (CombatSystem.TerminalCards の RoleItems の旗・BOT の配線の評価と一致)
    //    擦り切れた賽袋 = 振り直しの無料の回数 (CombatManager.FreeRerollsLeft)
    //    鏡合わせの盤・宿命の刻み目・万象のゾロ目 = 役の適用 (CombatManager.ResolveRoles / DecideOmniPair)
    //  品の文に無い上限・回数制限は足さない (作者の方針)。
    // ============================================================

    internal static class RoleFx
    {
        /// <summary>出来事 OnRoleFired で、 いま立った役 (firedRolesThisTurn の最後)。</summary>
        public static CombatSystem.RoleKind? Last(CombatContext ctx)
            => ctx.firedRolesThisTurn.Count > 0 ? ctx.firedRolesThisTurn[ctx.firedRolesThisTurn.Count - 1] : (CombatSystem.RoleKind?)null;
    }

    /// <summary>欠け目の見立て: 1・2・3 の目を含む役 (1〜3 の 2 個そろい・ゾロ目、 1〜3 の目を含む連なり) が立ったターン、 敵の防御を 賽目 × 2 無視する (貫通に足す・このターン)。</summary>
    public class ChippedEyeReading : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.RoleChipped;
        public const int PerDepth = 2;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRoleFired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var k = RoleFx.Last(ctx);
            if (k == null || !HasLowFace(k.Value, ctx.playerDice)) return;
            int pen = PerDepth * ctx.AxisDepth(AxisRules.Role);
            if (pen <= 0) return;
            ctx.playerArmorPen += pen;
            Rw.Log($"[欠け目の見立て] 〈{CombatSystem.YachtRoles.NameOf(k.Value)}〉 → 敵の防御を {pen} 無視");
        }

        static bool HasLowFace(CombatSystem.RoleKind k, int[] dice)
        {
            if (k != CombatSystem.RoleKind.Straight) return CombatSystem.YachtRoles.FaceOf(k) <= 3;
            if (dice != null) foreach (int d in dice) if (d >= 1 && d <= 3) return true;
            return false;
        }
    }

    /// <summary>揃いの拍子木: 2 ターン続けて役が立ったとき (続く限り毎ターン)、 次のターンの手札 +1 枚・次のターンの最初の振り直しが無料
    /// (無料の振り直し +1・〈特別・対〉と同じ口)。</summary>
    public class MatchedClappers : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.RoleClapper;
        /// <summary>[persistent] 最後に役が立ったターンの番号。</summary>
        private const string LastKey = "role.clapper.last";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRoleFired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int last = Rw.Count(ctx, LastKey);
            ctx.accumulatedValues[LastKey] = ctx.currentTurn;
            if (last <= 0 || last != ctx.currentTurn - 1) return;
            Rw.DrawNext(1);
            CombatSystem.YachtRoleEffects.AddNextTurn(ctx, CombatSystem.YachtRoleEffects.FreeRerollKey, 1);
            Rw.Log("[揃いの拍子木] 2 ターン続けて役 → 次のターン 手札 +1・最初の振り直しが無料");
        }
    }
}

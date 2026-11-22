using CombatSystem;
using GameLoop;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  役の軸 13 品 (2026-11-21・full-item-rework-ledger §⑧・docs/GAME.md §7-8)。
    //  分岐 A〈低目ロール連打〉/ 分岐 B〈リロール＆格上げ爆発〉。
    //  そのターンの攻撃・ブロック・与ダメージ・防御の無視は CombatSystem.TerminalCards の RoleItems の旗 (Resolve の 6d ──
    //  本体・画面の見込み・BOT の写しが同じ関数を通る)。 役の回数・続いたターンは TerminalBattleState.roleCount / roleStreak (CombatManager.ResolveRoles が書く)。
    //  ここは次のターンへ持ち越す分と、 撃のダメージを読む分だけ。
    // ============================================================

    internal static class RoleFx
    {
        /// <summary>出来事 OnRoleFired で、 いま立った役 (firedRolesThisTurn の最後)。</summary>
        public static RoleKind? Last(CombatContext ctx)
            => ctx.firedRolesThisTurn.Count > 0 ? ctx.firedRolesThisTurn[ctx.firedRolesThisTurn.Count - 1] : (RoleKind?)null;

        /// <summary>このターン、 1・2・3 の目を含む役が立ったか。</summary>
        public static bool LowRoleThisTurn(CombatContext ctx)
        {
            foreach (var k in ctx.firedRolesThisTurn) if (TerminalCards.HasLowFace(k, ctx.playerDice)) return true;
            return false;
        }

        /// <summary>次のターンのブロック +n (<see cref="YachtRoleEffects.BlockNextKey"/>)。</summary>
        public static void BlockNext(CombatContext ctx, int n) => YachtRoleEffects.AddNextTurn(ctx, YachtRoleEffects.BlockNextKey, n);
    }

    /// <summary>欠け目の見立て: 1・2・3 の目を含む役が立ったターン、 攻撃が当たるたびボスの次の目盛りがその撃のダメージの 25% だけ上がる
    /// (目盛りへの削り +25%・〈刃毀れの見立て〉と同じ口 CombatManager.RaiseBossNextNotch)。 防御 12 の無視は旗 (<see cref="RoleItems.Chipped"/>)。</summary>
    public class ChippedEyeReading : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.RoleChipped;
        public const int NotchPct = 25;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Psn.Hit(ctx) || !RoleFx.LowRoleThisTurn(ctx)) return;
            int add = ctx.finalDamage * NotchPct / 100;
            if (add > 0) CombatManager.Instance?.RaiseBossNextNotch(add);
        }
    }

    /// <summary>揃いの拍子木: 2 ターン以上続けて役が立ったターン (役が途切れるまで毎ターン)、 次のターンの手札 +1 枚・次のターンの最初の振り直しが無料
    /// (無料の振り直し +1・〈特別・対〉と同じ口)。 各撃の与ダメージ +35% は旗 (<see cref="RoleItems.Clapper"/>)。</summary>
    public class MatchedClappers : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.RoleClapper;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRoleFired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if ((CombatManager.Instance?.TerminalDeck?.state.roleStreak ?? 0) < 1) return;   // 前のターンにも役が立った (このターンの分はまだ入っていない)
            Rw.DrawNext(1);
            YachtRoleEffects.AddNextTurn(ctx, YachtRoleEffects.FreeRerollKey, 1);
            Rw.Log("[揃いの拍子木] 役が続いた → 次のターン 手札 +1・最初の振り直しが無料");
        }
    }

    /// <summary>負け越しの賽: 役が立たなかったターンの終わり、 次のターンの最初の振り直しが無料 (無料の振り直し +1)・次のターンのブロック +6。</summary>
    public class LosingStreakDie : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.RoleLosing;
        public const int Block = 6;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.firedRolesThisTurn.Count > 0) return;
            YachtRoleEffects.AddNextTurn(ctx, YachtRoleEffects.FreeRerollKey, 1);
            RoleFx.BlockNext(ctx, Block);
            Rw.Log($"[負け越しの賽] 役なし → 次のターン 最初の振り直しが無料・ブロック +{Block}");
        }
    }

    /// <summary>雷毒の導管: 1・2・3 の目を含む役が立ったターン、 次のターンの手札 +1 枚・次のターンのブロック +4。</summary>
    public class LowFaceConduit : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.RoleConduit;
        public const int Block = 4;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRoleFired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var k = RoleFx.Last(ctx);
            if (k == null || !TerminalCards.HasLowFace(k.Value, ctx.playerDice)) return;
            Rw.DrawNext(1);
            RoleFx.BlockNext(ctx, Block);
            Rw.Log($"[雷毒の導管] 〈{YachtRoles.NameOf(k.Value)}〉 → 次のターン 手札 +1・ブロック +{Block}");
        }
    }
}

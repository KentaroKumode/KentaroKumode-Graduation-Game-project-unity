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

        /// <summary>このターン、 目の集合 <paramref name="faces"/> (<see cref="TerminalCards.FaceIn"/>) のどれかの役が立ったか。</summary>
        public static bool FaceRoleThisTurn(CombatContext ctx, int faces)
        {
            foreach (var k in ctx.firedRolesThisTurn) if (TerminalCards.FaceIn(faces, TerminalCards.FaceOfRole(k))) return true;
            return false;
        }

        /// <summary>次のターンのブロック +n (<see cref="YachtRoleEffects.BlockNextKey"/>)。</summary>
        public static void BlockNext(CombatContext ctx, int n) => YachtRoleEffects.AddNextTurn(ctx, YachtRoleEffects.BlockNextKey, n);
    }

    /// <summary>欠け目の見立て: 1 の役〈特別〉か 2 の役〈封じ〉が立ったターン (2026-11-22 neon 収束〜・その前は 1 だけ・さらに前は 1・2・3 の目を含む役)、 攻撃が当たるたびボスの次の目盛りがその撃のダメージの 25% だけ上がる
    /// (目盛りへの削り +25%・〈刃こぼれの見立て〉と同じ口 CombatManager.RaiseBossNextNotch)。 防御 12 の無視は旗 (<see cref="RoleItems.Chipped"/>)。</summary>
    public class ChippedEyeReading : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.RoleChipped;
        public const int Gauge = 1;   // 2026-11-26: 目盛りへの削り 25% → 混乱 +1 (撃ごと)
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Psn.Hit(ctx) || !RoleFx.FaceRoleThisTurn(ctx, TerminalCards.ChippedFaces)) return;
            CombatManager.Instance?.AddBossGauge(Gauge);
        }
    }

    /// <summary>揃いのメトロノーム: 同じ目の役が 2 ターン以上続けて立ったターン (2026-11-22〜・途切れるか目が変わるまで毎ターン・連なりは数えない)、 次のターンの手札 +1 枚・次のターンの最初の振り直しが無料
    /// (無料の振り直し +1・〈特別・対〉と同じ口)。 各撃の与ダメージ +35% は旗 (<see cref="RoleItems.Clapper"/>)。</summary>
    public class MatchedClappers : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.RoleClapper;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRoleFired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var k = RoleFx.Last(ctx);
            int face = k == null ? 0 : TerminalCards.FaceOfRole(k.Value);
            if (face == 0 || face != (CombatManager.Instance?.TerminalDeck?.state.roleLastFace ?? 0)) return;   // 前のターンにも同じ目の役 (このターンの分はまだ入っていない)
            Rw.DrawNext(1);
            YachtRoleEffects.AddNextTurn(ctx, YachtRoleEffects.FreeRerollKey, 1);
            Rw.Log($"[揃いのメトロノーム] {face} の役が続いた → 次のターン 手札 +1・最初の振り直しが無料");
        }
    }

    /// <summary>負け越しの賽: 役が立たなかったターンの終わり、 次のターンの最初の振り直しが無料 (無料の振り直し +1)・次のターンのブロック +6。 そのターンに振り直していたなら、 次のターンの手札 +1 枚 (answer13)。</summary>
    public class LosingStreakDie : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.RoleLosing;
        public const int Block = 6, Draw = 1;   // answer12: ブロック +6 → +8・手札 +1 → answer13: +6・手札は振り直したターンだけ
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.firedRolesThisTurn.Count > 0) return;
            bool rerolled = ctx.rerollsThisTurn > 0;
            if (rerolled) Rw.DrawNext(Draw);
            YachtRoleEffects.AddNextTurn(ctx, YachtRoleEffects.FreeRerollKey, 1);
            RoleFx.BlockNext(ctx, Block);
            Rw.Log($"[負け越しの賽] 役なし → 次のターン 最初の振り直しが無料・ブロック +{Block}" + (rerolled ? $"・手札 +{Draw}" : ""));
        }
    }

    /// <summary>小口の錠前: 1・2・3 の役 (〈特別〉〈封じ〉〈追撃〉) が立ったターン (2026-11-22 neon 収束〜・その前は 2 だけ)、 次のターンの手札 +1 枚 (1 ターン 1 回: 役は 1 ターンに高々 1 つ)・次のターンのブロック +4。</summary>
    public class LowFaceConduit : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.RoleConduit;
        public const int Block = 4;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRoleFired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var k = RoleFx.Last(ctx);
            if (k == null || !TerminalCards.FaceIn(TerminalCards.ConduitFaces, TerminalCards.FaceOfRole(k.Value))) return;
            Rw.DrawNext(1);
            RoleFx.BlockNext(ctx, Block);
            Rw.Log($"[小口の錠前] 〈{YachtRoles.NameOf(k.Value)}〉 → 次のターン 手札 +1・ブロック +{Block}");
        }
    }
}

using CombatSystem;
using GameLoop;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  山札の軸 19 品 (2026-11-21・full-item-rework-ledger §⑦・docs/GAME.md §7-7)。
    //  分岐 A〈廃棄・回転ループ〉/ 分岐 B〈永続・Pick 盤面構築〉。
    //  端子の出力・手札・山札を変える分は CombatSystem.TerminalCards の DeckItems の旗 (Resolve の 6c・BeforeShuffle・DisposeFired ──
    //  本体・画面の見込み・BOT の写しが同じ関数を通る)。 ここは端子の解決の外で働く 2 品だけ。
    // ============================================================

    /// <summary>焼き入れの挟み: 使い捨ての端子を使うたび (OnTerminalDispose)、 敵に 6 の防御無視ダメージ (Kw.DamageEnemy)。
    /// このターンの各撃の攻撃 +2 は旗 (<see cref="DeckItems.Tongs"/>)。 鳴るのは配線の解決の直後なので、 このターンの攻撃の固定ダメの枠で通る。</summary>
    public class TemperTongs : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.DeckTongs;
        public const int Damage = 6;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTerminalDispose };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.DamageEnemy(ctx, Damage);
            Rw.Log($"[焼き入れの挟み] 使い捨てを使った → 防御無視 {Damage}");
        }
    }

    /// <summary>仕込みの配電盤: 基本以外の端子を 3 枚発動したターン、 攻撃が当たるたびボスの次の目盛りがその撃のダメージの 20% だけ上がる
    /// (目盛りへの削り +20%・〈鎧縫いの針〉と同じ口 CombatManager.RaiseBossNextNotch)。 次のターンの手札 +2 は旗 (<see cref="DeckItems.Switchboard"/>)。</summary>
    public class PrimedSwitchboard : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.DeckSwitchboard;
        public const int NotchPct = 20;
        public static bool Active() => (CombatManager.Instance?.LastTerminalOutcome?.firedNonBasic ?? 0) >= TerminalCards.SwitchboardNeed;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Active());
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Psn.Hit(ctx) || !Active()) return;
            int add = ctx.finalDamage * NotchPct / 100;
            if (add > 0) CombatManager.Instance?.RaiseBossNextNotch(add);
        }
    }
}

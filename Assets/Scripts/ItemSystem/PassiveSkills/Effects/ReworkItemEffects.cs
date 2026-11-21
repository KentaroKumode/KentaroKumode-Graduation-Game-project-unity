using UnityEngine;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  アイテム再編 (2026-10-31)。 設計は docs/GAME.md §8-6、 計算の位置は DAMAGE_CALC_REFERENCE.md §3-H。
    //
    //  品どうしが 1+1>2 になるよう、 どの品も「何かを増やす」か「何かを条件にする」形にした。
    //  増やすもの・条件にするものは 臨界・充電・シールド・毒・出血・会心・金 の 7 つに寄せてある。
    //  キーワードの出来事 (爆発・充電を得る/使う・過充電・シールドの吸収/半減/割れ・毒の増加/ダメージ/上限・
    //  出血の減衰/消費/0) は Keywords.Kw が配る。 1 つの連鎖の中で同じ品は 1 度しか働かない。
    //
    //  効果を据え置いた品 (八日目の灰燼・蛇血瓶・鋸刃の山刀・先血の腕輪・
    //  医家の反り刃・逆綴じの止血帯・血桜・医書裏の開き針・溶金の領主剣・黄金色の封蝋) は元のクラスのまま。
    // ============================================================

    /// <summary>この品群で使う小さな道具。</summary>
    internal static class Rw
    {
        public static GameLoop.RunState Run => GameLoop.GameManager.Instance?.Run;
        public static int Steps => Run?.stepsSinceBattle ?? 0;
        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);

        /// <summary>刻みを「与ダメージ +10%」に換える品の共通部 (2026-11-06・2026-11-10 に出目 +10% から変更)。 OnPostRoll で刻みの分の率を積み、
        /// OnWired で攻撃に 1 本でも挿していたら刻みを 0 に戻す (使い切る)。 攻撃しないターンは持ち越す。
        /// この 2 つのトリガーを処理したら true (呼び手は刻みを数える処理へ進まない)。</summary>
        public const float DiceStackPct = 0.1f;
        public static bool SpendDiceStacks(PassiveSkillTrigger t, CombatContext ctx, string key)
        {
            if (t == PassiveSkillTrigger.OnPostRoll)
            {
                int n = Count(ctx, key);
                if (n > 0) ctx.AddOutgoingPct(n * DiceStackPct);
                return true;
            }
            if (t == PassiveSkillTrigger.OnWired)
            {
                if (Count(ctx, MutualKeys.AttackDiceCount) > 0) ctx.accumulatedValues[key] = 0f;
                return true;
            }
            return false;
        }
        public static bool Won(CombatContext ctx) => ctx.enemyCurrentHP <= 0;

        /// <summary>プレイヤーの攻撃が通った (ダメージ後トリガーの中で使う)。</summary>
        public static bool PlayerHit(CombatContext ctx) => ctx.playerWonRoll && ctx.finalDamage > 0;

        public static void Gold(int n, string label)
        {
            var run = Run;
            if (run != null && n > 0) GameLoop.GoldIncome.Gain(run, n, label, GameLoop.GoldOrigin.Item);
        }

        /// <summary>このターン 1 回だけ (key に今のターン番号を書く)。</summary>
        public static bool OncePerTurn(CombatContext ctx, string key)
        {
            if (Count(ctx, key) == ctx.currentTurn) return false;
            ctx.accumulatedValues[key] = ctx.currentTurn;
            return true;
        }

        /// <summary>この戦闘で 1 回だけ。</summary>
        public static bool OncePerBattle(CombatContext ctx, string key)
        {
            if (Count(ctx, key) > 0) return false;
            ctx.accumulatedValues[key] = 1;
            return true;
        }

        public static void Shield(CombatContext ctx, int n, string label)
        {
            if (n <= 0) return;
            ctx.consShield += n;
            CombatSystem.ShieldDiag.Note(label, n);
            ctx.shieldGainedTotal += n;
        }

        public static void Poison(CombatContext ctx, int n)
        {
            if (n > 0) ctx.AddStatus(StatusTarget.Enemy, "poison", n);
        }

        public static void Log(string s) => Debug.Log(s);

        /// <summary>プレイヤーの HP を n 回復する (戦闘中は CombatManager.HealPlayer ── 手元の HP と ctx の両方・回復を削るデバフも通す)。</summary>
        public static void Heal(CombatContext ctx, int n)
        {
            if (n <= 0) return;
            var cm = CombatSystem.CombatManager.Instance;
            if (cm != null && cm.IsCombatActive) cm.HealPlayer(n);
            else if (ctx != null) ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + n);
        }

        /// <summary>次のターンに端子を n 枚多く引く (端子〈先読み〉と同じ口・TerminalBattleState.extraDrawNext)。</summary>
        public static void DrawNext(int n)
        {
            var deck = CombatSystem.CombatManager.Instance?.TerminalDeck;
            if (deck != null && n > 0) deck.state.extraDrawNext += n;
        }

        /// <summary>いまの戦闘がエリート戦かボス戦か (StatModifierEffect の strongFoe と同じ判定)。</summary>
        public static bool StrongFoe()
        {
            var type = MapSystem.MapManager.Instance?.CurrentNode?.type;
            return type == MapSystem.TileType.EliteBattle || type == MapSystem.TileType.Boss;
        }
    }

    /// <summary>加重の拳套 (2026-11-20 作り直し): 同じターンに 2 撃目以降の攻撃が通るたび、 その撃の最終与ダメージ +15% (2 撃目 +15%・3 撃目 +30%・撃の番号 − 1 につき +15%)。
    /// この撃だけの最終与ダメージ倍率 (ctx.hitFinalDamagePct) を OnPreDealDamage で書く (CombatManager が撃ごとに 0 に戻す)。 軸なし。
    /// 旧: 攻撃が通るたび (1 ターンに 1 回) 刻み +1・刻み 1 つにつき与ダメージ +2% (戦闘の間)。
    /// 2026-11-11 (#100): 毒の品から作り直した (名前・フレーバー「留め紐は一撃ごとに長くなる。 外すと元へ戻る」と効果を合わせた)。</summary>
    public class WeightingGauntlet : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "加重の拳套";
        public string SkillId => Id;
        public const int PctPerHit = 15;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.hitCount >= 2);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.IsRealHit && ctx.hitIndex >= 1) ctx.hitFinalDamagePct += PctPerHit * ctx.hitIndex;
        }
    }

    // ============================================================
    //  金
    // ============================================================

    /// <summary>銭皿: シールドが残った状態で勝った戦闘ごとに 2G。
    /// 2026-11-06: 旧「残りシールド 3 につき 1G」は、 護符をとどめのターンに使うだけで買値より多く戻り (〈惜別の護符〉+13G)、
    /// 金を増やし続けられた。 通常のプレイでも収入の 23% を占めた。 額を残量に比例させない ── 最安の護符 (4G) でも元が取れない。</summary>
    public class CoinDish : IPassiveSkillEffect
    {
        public string SkillId => "銭皿";
        public const int Gold = 2;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Won(ctx) && ctx.consShield > 0) Rw.Gold(Gold, SkillId);
        }
    }

    /// <summary>幸運の硬貨: 同じ目が 2 個以上出たターン、 2G。</summary>
    public class LuckyCoin : IPassiveSkillEffect
    {
        public string SkillId => "幸運の硬貨";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var d = ctx.playerDice;
            if (d == null) return;
            for (int i = 0; i < d.Length; i++)
                for (int j = i + 1; j < d.Length; j++)
                    if (d[i] == d[j]) { Rw.Gold(2, SkillId); return; }
        }
    }

    /// <summary>戦果の天秤: 戦闘に勝ったとき、 その戦闘で出した会心の回数だけゴールド。</summary>
    public class SpoilsScale : IPassiveSkillEffect
    {
        public string SkillId => "戦果の天秤";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Won(ctx)) Rw.Gold(Rw.Count(ctx, Kw.CritCountKey), SkillId);
        }
    }

    /// <summary>爆破祭の小切手: 臨界の爆発でとどめを刺すたび +4G。 2026-11-20 (ADR-0016): 爆発のダメージそのもので敵が倒れた戦闘 (Kw.BurstKilledKey)。</summary>
    public class BlastPrize : IPassiveSkillEffect
    {
        public string SkillId => "爆破祭の小切手";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            if (Rw.Won(ctx) && Rw.Count(ctx, Kw.BurstKilledKey) > 0) Rw.Gold(4, SkillId);
        }
    }

    // ============================================================
    //  会心
    // ============================================================

    /// <summary>測量師の片眼鏡 (2026-11-20 作り直し): マップを 1 歩進むごとに、 次の戦闘の最初の 1 撃の会心率 +5% (その戦闘で最初に当てる撃・会心率は撃ごとに戻る)。
    /// 旧: 次の戦闘の会心率 +1% (その戦闘の間)。</summary>
    public class SurveyorMonocle : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "測量師の片眼鏡";
        public const float PctPerStep = 0.05f;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Once(Rw.Count(ctx, UsedKey) > 0, Rw.Count(ctx, Key) > 0);
        /// <summary>[persistent] 戦闘の始めの歩数 / 最初の 1 撃を撃った (1/0)。</summary>
        private const string Key = "rw.surveyor", UsedKey = "rw.surveyor.used";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { ctx.accumulatedValues[Key] = Rw.Steps; ctx.accumulatedValues[UsedKey] = 0; return; }
            if (!ctx.IsRealHit || Rw.Count(ctx, UsedKey) > 0) return;
            ctx.accumulatedValues[UsedKey] = 1;
            ctx.critRatePctAdd += Rw.Count(ctx, Key) * PctPerStep;
        }
    }

    /// <summary>瑠璃砥石: 役が成立したターン、 会心率 +15%。</summary>
    public class LapisWhetstone : IPassiveSkillEffect
    {
        public string SkillId => "瑠璃砥石";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.firedRolesThisTurn.Count > 0) ctx.critRatePctAdd += 0.25f;
        }
    }

    /// <summary>寸鉄【規則】(2026-11-20 作り直し): 会心しなかった撃にも、 会心倍率の上乗せ分 × (会心率 ÷ 100) が乗る (会心率は 100% を上限に数える)。
    /// 掛けるのは PassiveSkillManager.ProcessDamage (ctx.critShareOnNonCrit)。 旧: 会心率が 100% を超えた分が会心ダメージの倍率になる (超えた 10% につき +0.1)。</summary>
    public class Suntetsu : IPassiveSkillEffect
    {
        public string SkillId => "寸鉄";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critShareOnNonCrit = true;
    }

    /// <summary>銑鉄棍【規則】(2026-11-20 作り直し): 会心が出なくなる。 代わりに、 会心率 1% につき与ダメージ +1% (会心率は 100% を上限に数える・与ダメ % の池・撃ごと)。
    /// 掛けるのは PassiveSkillManager.ProcessDamage (ctx.critToDamage)。 旧: 会心になるはずだった攻撃は敵の防御を無視し、 この戦闘中の敵の防御を 1 下げる。</summary>
    public class UnwhettedClub : IPassiveSkillEffect
    {
        public string SkillId => "銑鉄棍";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critToDamage = true;
    }
}

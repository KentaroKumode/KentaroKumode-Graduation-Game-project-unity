using System.Collections.Generic;
using UnityEngine;
using ItemSystem.PassiveSkills;

namespace CombatSystem
{
    /// <summary>
    /// 戦場修飾子（Battle Modifier）マネージャー
    /// 
    /// 各戦闘開始時にランダムで1～2個の修飾子を選出し、
    /// CombatContextに適用する。修飾子は戦闘ルールを変化させる。
    /// 
    /// 修飾子一覧:
    /// - 豪雨(Downpour): 全ダイス最大値-2
    /// - 月蝕(LunarEclipse): 会心率+15%
    /// - 呪霧(CursedFog): scratchダメージ2倍
    /// - 血潮(BloodTide): 出血ダメージ2倍
    /// - 鉄壁(IronCurtain): 被ダメ上限5/ターン
    /// - 死闘(Deathmatch): 引き分けなし（同値はプレイヤー敗北）
    /// - 幸運(Fortune): 報酬2倍
    /// - 逆境(Adversity): 敵threat+3
    /// </summary>
    public class BattleModifierManager : MonoBehaviour
    {
        private static BattleModifierManager instance;
        public static BattleModifierManager Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("[BattleModifierManager]");
                    instance = go.AddComponent<BattleModifierManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>修飾子プール（有効な修飾子の定義）</summary>
        private static readonly BattleModifier[] modifierPool = new BattleModifier[]
        {
            new BattleModifier(BattleModifierId.Downpour,    "豪雨",   "全ダイス最大値-2"),
            new BattleModifier(BattleModifierId.LunarEclipse,"月蝕",   "会心率+15%"),
            new BattleModifier(BattleModifierId.CursedFog,   "呪霧",   "威圧の削りダメージ2倍"),
            new BattleModifier(BattleModifierId.BloodTide,   "血潮",   "出血ダメージ2倍"),
            new BattleModifier(BattleModifierId.IronCurtain, "鉄壁",   "被ダメージ上限5/ターン"),
            new BattleModifier(BattleModifierId.Deathmatch,  "死闘",   "引き分けなし（同値=プレイヤー敗北）"),
            new BattleModifier(BattleModifierId.Fortune,     "幸運",   "戦闘報酬2倍"),
            new BattleModifier(BattleModifierId.Adversity,   "逆境",   "敵の威圧値+3"),
        };

        /// <summary>
        /// 修飾子の即時効果を適用
        /// </summary>
        private void ApplyImmediateEffects(CombatContext ctx)
        {
            foreach (var mod in ctx.activeModifiers)
            {
                switch (mod.id)
                {
                    case BattleModifierId.Downpour:
                        // 豪雨: ダイス最大値-2（最低2を保証）
                        ctx.playerDiceMax = System.Math.Max(2, ctx.playerDiceMax - 2);
                        ctx.enemyDiceMax = System.Math.Max(2, ctx.enemyDiceMax - 2);
                        break;

                    case BattleModifierId.LunarEclipse:
                        // 月蝕: 会心率 +15%（バフとして永続）
                        ctx.currentBuffs[CombatContext.CritRateBuffKey] = ctx.GetBuff(CombatContext.CritRateBuffKey) + 0.15f;
                        break;

                    case BattleModifierId.Adversity:
                        // 逆境: 敵threat+3
                        ctx.enemyThreat += 3;
                        break;
                }
            }
        }

        /// <summary>
        /// 出血ダメージに対する修飾子効果を適用
        /// </summary>
        public static int ApplyBleedModifiers(CombatContext ctx, int baseBleed)
        {
            if (ctx == null) return baseBleed;

            // 血潮: 出血2倍
            if (ctx.HasModifier(BattleModifierId.BloodTide))
                baseBleed *= 2;

            return baseBleed;
        }

        void OnDestroy() { if (instance == this) instance = null; }
    }
}

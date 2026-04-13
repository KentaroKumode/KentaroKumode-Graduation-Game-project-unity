using System.Collections.Generic;
using UnityEngine;
using InventorySystem.PassiveSkills;

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
    /// - 月蝕(LunarEclipse): 会心率+3
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
            new BattleModifier(BattleModifierId.LunarEclipse,"月蝕",   "会心率+3"),
            new BattleModifier(BattleModifierId.CursedFog,   "呪霧",   "威圧の削りダメージ2倍"),
            new BattleModifier(BattleModifierId.BloodTide,   "血潮",   "出血ダメージ2倍"),
            new BattleModifier(BattleModifierId.IronCurtain, "鉄壁",   "被ダメージ上限5/ターン"),
            new BattleModifier(BattleModifierId.Deathmatch,  "死闘",   "引き分けなし（同値=プレイヤー敗北）"),
            new BattleModifier(BattleModifierId.Fortune,     "幸運",   "戦闘報酬2倍"),
            new BattleModifier(BattleModifierId.Adversity,   "逆境",   "敵の威圧値+3"),
        };

        /// <summary>
        /// 戦闘開始時にランダムで修飾子を選出し、CombatContextに適用
        /// </summary>
        /// <param name="ctx">戦闘コンテキスト</param>
        /// <param name="count">選出する修飾子数（デフォルト1）</param>
        public void RollModifiers(CombatContext ctx, int count = 1)
        {
            if (ctx == null) return;
            ctx.activeModifiers.Clear();

            // Fisher-Yatesでシャッフルしてcount個選出
            var pool = new List<BattleModifier>(modifierPool);
            int selectCount = Mathf.Min(count, pool.Count);

            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }

            for (int i = 0; i < selectCount; i++)
            {
                ctx.activeModifiers.Add(pool[i]);
                Debug.Log($"[BattleModifier] 戦場修飾子: {pool[i].displayName} — {pool[i].description}");
            }

            // 即時適用が必要な修飾子を処理
            ApplyImmediateEffects(ctx);
        }

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
                        // 月蝕: 会心率+3（バフとして永続）
                        ctx.currentBuffs["criticalBonus"] = ctx.GetBuff("criticalBonus") + 3;
                        break;

                    case BattleModifierId.Adversity:
                        // 逆境: 敵threat+3
                        ctx.enemyThreat += 3;
                        break;
                }
            }
        }

        /// <summary>
        /// scratchダメージに対する修飾子効果を適用
        /// CombatManagerのscratch計算後に呼ぶ
        /// </summary>
        public static int ApplyScratchModifiers(CombatContext ctx, int baseScratch)
        {
            if (ctx == null) return baseScratch;

            // 呪霧: scratch2倍
            if (ctx.HasModifier(BattleModifierId.CursedFog))
                baseScratch *= 2;

            return baseScratch;
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

        /// <summary>
        /// 受けるダメージに上限を適用（鉄壁）
        /// </summary>
        public static int ApplyDamageCap(CombatContext ctx, int damage)
        {
            if (ctx == null) return damage;

            // 鉄壁: 被ダメ上限5
            if (ctx.HasModifier(BattleModifierId.IronCurtain))
                damage = System.Math.Min(5, damage);

            return damage;
        }

        void OnDestroy() { if (instance == this) instance = null; }
    }
}

using System.Collections.Generic;
using UnityEngine;
using CombatSystem;

namespace GameLoop
{
    /// <summary>
    /// フロアごとの敵エンカウント管理
    /// enemies.jsonのfloorデータを使い、現在フロアに出現する敵を選出する
    /// </summary>
    public static class FloorManager
    {
        /// <summary>
        /// 指定フロアの敵候補からランダムに1体選出
        /// </summary>
        public static EnemyData PickEnemy(int floor)
        {
            // そのフロア以下で出現する全敵を候補にする（ボス専用敵 boss_layer* は通常戦闘から除外）
            var candidates = EnemyDatabase.GetByFloor(floor);
            candidates?.RemoveAll(IsBossOnly);
            if (candidates == null || candidates.Count == 0)
            {
                Debug.LogWarning($"[FloorManager] フロア{floor}の敵が見つかりません。全敵からランダム選出");
                return EnemyDatabase.GetRandom();
            }

            // 現在フロアに初登場する敵を優先（50%で選出。ボス専用敵は除外）
            var newEnemies = EnemyDatabase.GetNewOnFloor(floor);
            newEnemies?.RemoveAll(IsBossOnly);
            if (newEnemies != null && newEnemies.Count > 0 && Random.value < 0.5f)
            {
                return newEnemies[Random.Range(0, newEnemies.Count)];
            }

            return candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>ボス専用敵（id が boss_layer* ）か。通常戦闘抽選から除外する。</summary>
        private static bool IsBossOnly(EnemyData e)
            => e != null && e.id != null && e.id.StartsWith("boss_layer");

        /// <summary>
        /// フロアに応じた報酬コインを計算
        /// </summary>
        public static int CalculateRewardCoins(int floor, bool playerWon, int turnsUsed)
        {
            if (!playerWon) return 0;

            // 基本報酬: フロア + 3 (1層=4, 6層=9。約2.25倍カーブ、速度ボーナス無し)
            int baseReward = floor + 3;

            // フロアデバフの報酬倍率（Fortune層・shopPriceMultiplier等）
            var mod = MapSystem.FloorModifierDatabase.Get(floor);
            if (mod != null && Mathf.Abs(mod.coinRewardMultiplier - 1f) > 0.001f)
                baseReward = Mathf.CeilToInt(baseReward * mod.coinRewardMultiplier);

            return baseReward;
        }

        /// <summary>
        /// フロアに応じたHP回復量を計算（戦闘間）
        /// </summary>
        public static int CalculateHealAmount(int floor)
        {
            // 基本: 5HP回復、後半フロアは少なめ
            if (floor <= 3) return 5;
            if (floor <= 5) return 3;
            return 2;
        }
    }
}

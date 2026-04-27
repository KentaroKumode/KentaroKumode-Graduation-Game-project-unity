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
            // そのフロア以下で出現する全敵を候補にする
            var candidates = EnemyDatabase.GetByFloor(floor);
            if (candidates == null || candidates.Count == 0)
            {
                Debug.LogWarning($"[FloorManager] フロア{floor}の敵が見つかりません。全敵からランダム選出");
                return EnemyDatabase.GetRandom();
            }

            // 現在フロアに初登場する敵を優先（50%で選出）
            var newEnemies = EnemyDatabase.GetNewOnFloor(floor);
            if (newEnemies != null && newEnemies.Count > 0 && Random.value < 0.5f)
            {
                return newEnemies[Random.Range(0, newEnemies.Count)];
            }

            return candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>
        /// フロアに応じた報酬コインを計算
        /// </summary>
        public static int CalculateRewardCoins(int floor, bool playerWon, int turnsUsed)
        {
            if (!playerWon) return 0;

            // 基本報酬: フロア × 10
            int baseReward = floor * 10;

            // 速度ボーナス: 5ターン以内ならx1.5
            if (turnsUsed <= 5)
                baseReward = Mathf.CeilToInt(baseReward * 1.5f);

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

using System.Collections.Generic;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>
    /// 全敵データを管理する静的データベース
    /// Resources/enemies.json から自動ロード
    /// </summary>
    public static class EnemyDatabase
    {
        private static Dictionary<string, EnemyData> database;
        private static bool initialized = false;

        /// <summary>
        /// データベースを初期化（必要時に自動呼び出し）
        /// </summary>
        public static void EnsureInitialized()
        {
            if (initialized) return;
            Load();
            initialized = true;
        }

        /// <summary>
        /// JSONからロード
        /// </summary>
        private static void Load()
        {
            database = new Dictionary<string, EnemyData>();

            var jsonAsset = Resources.Load<TextAsset>("enemies");
            if (jsonAsset == null)
            {
                Debug.LogError("[EnemyDatabase] Resources/enemies.json not found!");
                return;
            }

            var dataList = JsonUtility.FromJson<EnemyDataList>(jsonAsset.text);
            if (dataList?.enemies == null)
            {
                Debug.LogError("[EnemyDatabase] Failed to parse enemies.json!");
                return;
            }

            foreach (var enemy in dataList.enemies)
            {
                if (string.IsNullOrEmpty(enemy.id)) continue;
                database[enemy.id] = enemy;
            }

            Debug.Log($"[EnemyDatabase] Loaded {database.Count} enemies.");
        }

        /// <summary>
        /// IDから敵データを取得
        /// </summary>
        public static EnemyData Get(string enemyId)
        {
            EnsureInitialized();
            database.TryGetValue(enemyId, out var data);
            return data;
        }

        /// <summary>
        /// 指定階層に出現する敵一覧を取得
        /// </summary>
        public static List<EnemyData> GetByFloor(int floor)
        {
            EnsureInitialized();
            var result = new List<EnemyData>();
            foreach (var kvp in database)
            {
                if (kvp.Value.floor <= floor)
                    result.Add(kvp.Value);
            }
            return result;
        }

        /// <summary>
        /// 指定フロア範囲 [minFloor, maxFloor] に出現する敵一覧。
        /// 低層雑魚が後半まで居座って道中を薄める問題を防ぐため、
        /// 抽選候補を直近フロアに限定する用途で使う。
        /// </summary>
        public static List<EnemyData> GetByFloorRange(int minFloor, int maxFloor)
        {
            EnsureInitialized();
            var result = new List<EnemyData>();
            foreach (var kvp in database)
            {
                int f = kvp.Value.floor;
                if (f >= minFloor && f <= maxFloor)
                    result.Add(kvp.Value);
            }
            return result;
        }

        /// <summary>
        /// 指定階層にちょうど初登場する敵一覧
        /// </summary>
        public static List<EnemyData> GetNewOnFloor(int floor)
        {
            EnsureInitialized();
            var result = new List<EnemyData>();
            foreach (var kvp in database)
            {
                if (kvp.Value.floor == floor)
                    result.Add(kvp.Value);
            }
            return result;
        }

        /// <summary>
        /// 全敵データ
        /// </summary>
        public static IEnumerable<EnemyData> GetAll()
        {
            EnsureInitialized();
            return database.Values;
        }

        /// <summary>
        /// ランダムに敵を1体取得
        /// </summary>
        public static EnemyData GetRandom()
        {
            EnsureInitialized();
            if (database == null || database.Count == 0) return null;
            var list = new List<EnemyData>(database.Values);
            return list[Random.Range(0, list.Count)];
        }

        /// <summary>
        /// データベースをリロード
        /// </summary>
        public static void Reload()
        {
            initialized = false;
            EnsureInitialized();
        }
    }
}

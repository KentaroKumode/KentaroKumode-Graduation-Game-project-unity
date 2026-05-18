using System;
using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>
    /// 敵1体の定義データ（JSONからデシリアライズ）
    /// </summary>
    [Serializable]
    public class EnemyData
    {
        public string id;              // 内部識別名 (例: "goblin")
        public string displayName;     // 表示名 (例: "ゴブリン")
        public string description;     // フレーバーテキスト
        public int floor;              // 初登場階層 (1～7)
        public int maxHP;              // 最大HP
        public int diceCount;          // ダイス数
        public int diceMaxValue;       // ダイス最大出目 (1～この値)
        public int criticalNumerator;  // クリティカル確率の分子 (0～9)
        public int threat;              // 脅威値（勝利時でもプレイヤーが受ける削りダメ基準）
        public List<EnemyPassiveEntry> passiveSkills; // パッシブスキル一覧

        /// <summary>ダイスを振り、各出目の配列を返す</summary>
        public int[] RollDice()
        {
            var results = new int[diceCount];
            for (int i = 0; i < diceCount; i++)
            {
                results[i] = UnityEngine.Random.Range(1, diceMaxValue + 1);
            }
            return results;
        }

        /// <summary>ダイス表記文字列 (例: "2d6")</summary>
        public string DiceNotation => $"{diceCount}d{diceMaxValue}";

        /// <summary>DB共有インスタンスを汚さずに改変するための複製。
        /// passiveSkills もリスト/要素ごと深くコピーする。</summary>
        public EnemyData Clone()
        {
            var c = (EnemyData)MemberwiseClone();
            if (passiveSkills != null)
            {
                c.passiveSkills = new List<EnemyPassiveEntry>(passiveSkills.Count);
                foreach (var p in passiveSkills)
                    c.passiveSkills.Add(p == null ? null : new EnemyPassiveEntry
                    {
                        internalName = p.internalName,
                        skillName = p.skillName,
                        description = p.description,
                    });
            }
            return c;
        }
    }

    /// <summary>
    /// 敵のパッシブスキルエントリ（JSONデシリアライズ用）
    /// </summary>
    [Serializable]
    public class EnemyPassiveEntry
    {
        public string internalName;
        public string skillName;
        public string description;
    }

    /// <summary>
    /// enemies.json のルートラッパー
    /// </summary>
    [Serializable]
    public class EnemyDataList
    {
        public List<EnemyData> enemies;
    }
}

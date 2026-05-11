using System.Collections.Generic;

namespace EventSystem
{
    /// <summary>
    /// イベント出現条件。テキストの第2フィールドからパースされる。
    /// </summary>
    public class EventCondition
    {
        public int floorMin = 1;
        public int floorMax = 99;
        public List<string> requiredFlags = new List<string>();
        public List<string> requiredPassiveItems = new List<string>();   // パッシブ所持要件
        public bool onceOnly;       // 「一度のみ」: 一度出現したら再出現しない
        public bool priority;       // 「優先」: 出現可能な場合、高確率で優先出現
        public bool rare;           // 「稀」: 出現重み低下

        /// <summary>floor が範囲内か</summary>
        public bool MatchesFloor(int floor)
        {
            return floor >= floorMin && floor <= floorMax;
        }
    }
}

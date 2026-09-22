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

        /// <summary>「召喚専用」: **通常のイベントマス抽選には一切出ない**。
        /// 特定の条件から名指しで呼び出されるときだけ発生する。
        ///
        /// 2026-08-11 追加。 T4-A〈破綻〉が有利マスを空白化するときに専用イベントを
        /// 起こすため。 出現階層を空にする等の小細工で抽選から外すと、 条件式を
        /// あとから触った誰かが**普通のイベントとして復活させてしまう**。
        /// 「呼ばれたときだけ出る」という意図を条件そのものに書く。</summary>
        public bool summonOnly;

        /// <summary>floor が範囲内か</summary>
        public bool MatchesFloor(int floor)
        {
            return floor >= floorMin && floor <= floorMax;
        }
    }
}

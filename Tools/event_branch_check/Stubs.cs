// Unity なしで EventParser / EventChoice をコンパイルするための最小の代役 (Tools/event_branch_check だけで使う)。
//   本体の型の、 イベントの選べるかの判定が読む所だけを持つ。 中身は検算の Program が場面ごとに書き換える。
namespace UnityEngine
{
    public static class Debug
    {
        public static int Warnings;
        public static void LogWarning(object m) { Warnings++; System.Console.WriteLine("WARN " + m); }
    }
}

namespace EventSystem
{
    /// <summary>出やすさの札の名前だけ (倍率は検算に要らない)。 札の正本は Assets/Scripts/EventSystem/EventWeights.cs。</summary>
    public static class EventWeights
    {
        public static bool IsKnownTag(string tag) => tag == "追跡" || tag == "地図" || tag == "素材";
    }
}

namespace MapSystem.FreeMove
{
    public enum WeatherKind { None, Crumble, Sink, Hail, Sand, Snow }

    public class FreeMapSim
    {
        public bool FoeAlive;
        public WeatherKind Weather;
        public bool FrontArrived;
    }
}

namespace GameLoop
{
    public class RunState
    {
        public int coins, provision, weaponMaterials;
    }

    public class GameManager
    {
        public static GameManager Instance;
        public RunState Run;
        public MapSystem.FreeMove.FreeMapSim FreeMap;
        /// <summary>検算が場面ごとに決める: 差し出せる秘宝があるか・鍛える素材 (int.MaxValue = 終点)。</summary>
        public static bool TradeOk = true;
        public static int ForgeCost = 3;
        public static bool CanTradePassive(RunState run) => TradeOk;
        public static int WeaponUpgradeCost(RunState run) => ForgeCost;
    }
}

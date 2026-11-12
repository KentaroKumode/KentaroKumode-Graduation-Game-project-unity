// Unity なしで EventParser / EventChoice をコンパイルするための最小の代役 (Tools/event_branch_check だけで使う)。
//   本体の型の、 イベントの選べるかの判定が読む所だけを持つ。 中身は検算の Program が場面ごとに書き換える。
namespace UnityEngine
{
    public static class Debug
    {
        public static int Warnings;
        public static void LogWarning(object m) { Warnings++; System.Console.WriteLine("WARN " + m); }
        public static void Log(object m) { }
    }
    public static class Mathf
    {
        public static int Max(int a, int b) => a > b ? a : b;
    }
}

// ItemIds (Ids.cs) と ConvictionSystem.cs をコンパイルするための代役 (2026-11-13・手放せない品の検算)。
namespace ItemSystem
{
    public class CompleteItemData { public string id, consFamily; public int tier; }
    public class ItemDatabase
    {
        public static ItemDatabase Instance;
        public CompleteItemData GetItem(string id) => null;
        public CompleteItemData GetConsumable(string family, int tier) => null;
    }
}
namespace ItemSystem.PassiveItems
{
    public static class TorchRevival { public const string TorchId = "ちいさな灯火"; }
}
namespace ItemSystem.Helpers
{
    /// <summary>本体の PassiveAddHelper.AddPassiveItem の、 秘宝の重複の判定 (今持っている・このランで持ったことがある) だけを写した代役。</summary>
    public static class PassiveAddHelper
    {
        public static int Adds;
        public static bool AddPassiveItem(GameLoop.RunState run, string itemId)
        {
            if (run.ownedPassiveItems.Contains(itemId) || run.seenPassiveItemIds.Contains(itemId)) return false;
            run.ownedPassiveItems.Add(itemId); run.seenPassiveItemIds.Add(itemId); Adds++;
            return true;
        }
    }
}

namespace EventSystem
{
    /// <summary>出やすさの札の名前だけ (倍率は検算に要らない)。 札の正本は Assets/Scripts/EventSystem/EventWeights.cs。</summary>
    public static class EventWeights
    {
        public static bool IsKnownTag(string tag) => tag == "追跡" || tag == "地図" || tag == "素材";
    }

    /// <summary>ConvictionSystem.RiftRecordSeen が読む所だけ (検算は「裂け目の記録」を引かない)。</summary>
    public static class EventDatabase
    {
        public static EventDefinition GetByName(string name) => null;
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
        // 確信の資格 (ConvictionSystem.cs が読む所)
        public System.Collections.Generic.List<string> ownedPassiveItems = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.HashSet<string> seenPassiveItemIds = new System.Collections.Generic.HashSet<string>();
        public int convictionStage;
        public bool layer6Unlocked, layer7Unlocked;
        public System.Collections.Generic.HashSet<string> seenOnceEvents = new System.Collections.Generic.HashSet<string>();
    }

    /// <summary>ConvictionSystem.RiftGateAtBoss が読む測定の口だけ。</summary>
    public static class GameHooks
    {
        public static bool RiftRecordAtBoss = true;
    }

    public class GameManager
    {
        public static GameManager Instance;
        public RunState Run;
        public MapSystem.FreeMove.FreeMapSim FreeMap;
        /// <summary>検算が場面ごとに決める: 差し出せる秘宝があるか・鍛える素材 (int.MaxValue = 終点)。</summary>
        public static bool TradeOk = true;
        public static int ForgeCost = 3;
        public static bool SuppressEvents;
        public static bool CanTradePassive(RunState run) => TradeOk;
        public static int WeaponUpgradeCost(RunState run) => ForgeCost;
    }
}

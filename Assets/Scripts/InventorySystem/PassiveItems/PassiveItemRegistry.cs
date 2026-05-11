using System.Collections.Generic;
using EventSystem.TimedEffects;

namespace InventorySystem.PassiveItems
{
    /// <summary>
    /// 名前付き固有パッシブアイテムの効果レジストリ。
    /// ITimedEffect を流用するが、こちらは「永続」（チャージ消費なし）。
    /// PassiveItemManager がプレイヤー所持リストから対象を引き、トリガごとに Apply する。
    /// </summary>
    public static class PassiveItemRegistry
    {
        private static readonly Dictionary<string, ITimedEffect> registry
            = new Dictionary<string, ITimedEffect>();

        private static bool initialized;

        public static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            RegisterAll();
        }

        public static ITimedEffect Get(string id)
        {
            EnsureInitialized();
            return registry.TryGetValue(id, out var v) ? v : null;
        }

        public static IEnumerable<KeyValuePair<string, ITimedEffect>> All
        {
            get { EnsureInitialized(); return registry; }
        }

        private static void Register(ITimedEffect effect) => registry[effect.Id] = effect;

        private static void RegisterAll()
        {
            // 名前付き固有パッシブの効果実装
            Register(new Effects.PilgrimStaffEffect());
            Register(new Effects.MemoryHourglassEffect());
            Register(new Effects.FuriousBladeEffect());
            Register(new Effects.HopeEmberEffect());
            Register(new Effects.TwilightPocketwatchEffect());
            Register(new Effects.HardshipSigilEffect());
            Register(new Effects.ReapersBeadsEffect());
            Register(new Effects.StormCrestEffect());
            Register(new Effects.SilentSwordbeltEffect());
            Register(new Effects.WickBellEffect());

            // 6種の旧名前付きパッシブ（ちいさな灯火 等）は効果未定義につき未登録。
            // 効果を決める時にここに追加する。
        }
    }
}

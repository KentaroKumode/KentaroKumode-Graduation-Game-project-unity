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
            Register(new Effects.HopeEmberEffect());
            Register(new Effects.ReapersBeadsEffect());
            Register(new Effects.StormCrestEffect());
            Register(new Effects.SilentSwordbeltEffect());
            Register(new Effects.WickBellEffect());

            // HP閾値発動系
            Register(new Effects.FrenzyMedallionEffect());
            Register(new Effects.ManashikiEffect());     // 末那識（旧 死神の予感）: HP≤20%で会心確定

            // 歩行HP回復
            Register(new Effects.CalmShoesEffect());
            Register(new Effects.HealingShoesEffect());
            Register(new Effects.HolyShoesEffect());

            // その他高レア
            Register(new Effects.GoldenScaleEffect());
            Register(new Effects.HarmonicClockEffect());
            Register(new Effects.SilentRobeEffect());
            Register(new Effects.BlackSmokeTalismanEffect());
            Register(new Effects.AzureEyeEffect());
            Register(new Effects.IronHeartEffect());
            Register(new Effects.GuardianAngelBellEffect());
            Register(new Effects.CalamityRingEffect());
            Register(new Effects.EternalLanternEffect());

            // 佯狂者シリーズ（発狂連動）。鈴は店フックのため非登録（商人の符牒と同型）。
            Register(new Effects.YokyoStaffEffect());
            Register(new Effects.YokyoGarbEffect());
            Register(new Effects.YokyoCrownEffect());

            // 2026-06-03 新規追加アイテム
            Register(new Effects.PilgrimCharmEffect()); // 巡礼の杖飾り（移動時25%で希望+1）
            Register(new Effects.RevelMaskEffect());    // 狂宴の仮面（低希望スケール与ダメ）
            // 商人の符牒・食通の懐刀 は他システム連携でフックされる（PassiveItemRegistry には登録しない）

            // 6種の旧名前付きパッシブ（ちいさな灯火 等）は効果未定義につき未登録。
            // 効果を決める時にここに追加する。
        }
    }
}

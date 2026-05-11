using System.Collections.Generic;

namespace EventSystem.TimedEffects
{
    /// <summary>
    /// 全 ITimedEffect 実装の静的レジストリ。
    /// 新バフ追加時は RegisterAll() に1行追加。
    /// </summary>
    public static class TimedEffectRegistry
    {
        private static readonly Dictionary<string, ITimedEffect> registry
            = new Dictionary<string, ITimedEffect>();

        /// <summary>
        /// バフID毎のデフォルト初期チャージ数。
        /// イベントで獲得時にこの値が timedBuffs に積まれる。
        /// 既定値1（次戦闘で消費）、複数戦闘持続するものはここで設定。
        /// </summary>
        private static readonly Dictionary<string, int> defaultCharges
            = new Dictionary<string, int>
            {
                { "啓示", 3 },   // 次の3戦闘
                { "中毒", 2 },   // 次の2戦闘
            };

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

        public static IEnumerable<ITimedEffect> GetByTrigger(TimedEffectTrigger trigger)
        {
            EnsureInitialized();
            foreach (var kvp in registry)
                if (kvp.Value.Trigger == trigger)
                    yield return kvp.Value;
        }

        /// <summary>このバフを獲得した際の初期チャージ数。未定義なら1。</summary>
        public static int GetDefaultCharges(string id)
        {
            EnsureInitialized();
            return defaultCharges.TryGetValue(id, out int n) ? n : 1;
        }

        private static void Register(ITimedEffect effect)
        {
            registry[effect.Id] = effect;
        }

        private static void RegisterAll()
        {
            // ===== 戦闘開始時系 =====
            Register(new Effects.LiberatorEffect());
            Register(new Effects.MutualAidEffect());
            Register(new Effects.BeastBondEffect());
            Register(new Effects.BeastFavorEffect());
            Register(new Effects.MissionEffect());
            Register(new Effects.CursedThirstEffect());
            Register(new Effects.DeadInvitationEffect());

            // ===== ロール時系 =====
            Register(new Effects.StarBlessingEffect());
            Register(new Effects.RevelationEffect());
            Register(new Effects.SentimentEffect());
            Register(new Effects.TimeGazeEffect());

            // ===== ターン終了時系 =====
            Register(new Effects.PoisonEffect());

            // ===== 戦闘終了時系 =====
            Register(new Effects.SpringBlessingEffect());
            Register(new Effects.SproutPrayerEffect());
        }
    }
}

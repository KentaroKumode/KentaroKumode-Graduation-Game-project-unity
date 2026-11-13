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
                { Effects.ToilDebuffEffect.Name,    Effects.ToilDebuffEffect.Charges },      // 通常の戦闘の勝ち 3 回 (2026-11-13)
                { Effects.ReadinessBuffEffect.Name, Effects.ReadinessBuffEffect.Charges },   // 同上
            };

        /// <summary>画面に出す説明 (名前・残りと並べる)。 説明を持つ時限効果だけ (2026-11-13・〈徒労〉〈覚悟〉)。 無ければ null。</summary>
        public static string DescriptionOf(string id)
        {
            if (id == Effects.ToilDebuffEffect.Name) return Effects.ToilDebuffEffect.Description;
            if (id == Effects.ReadinessBuffEffect.Name) return Effects.ReadinessBuffEffect.Description;
            return null;
        }

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

            // ===== 通常の戦闘に勝った時 (2026-11-13) =====
            Register(new Effects.ToilDebuffEffect());
            Register(new Effects.ReadinessBuffEffect());
        }
    }
}

using System;

namespace AutoTest.Ultra
{
    /// <summary>
    /// Ultra の探索専用シナリオ。候補 ID を入力に含めないため、同じ decision の
    /// 全候補へ同じ synthetic seed を割り当てる (common random numbers)。
    /// 本ランの GameRng / master seed には一切依存しない。
    /// </summary>
    public sealed class UltraScenarioBank
    {
        public const int Version = 1;

        private const ulong SearchDomain = 0x5345415243485F31UL;       // "SEARCH_1"
        private const ulong ConfirmationDomain = 0x434F4E4649524D31UL; // "CONFIRM1"
        private const ulong ShadowDomain = 0x534841444F575F31UL;       // "SHADOW_1"

        private readonly ulong _plannerSalt;

        /// <param name="plannerSalt">
        /// Ultra 専用の固定 salt。本ランの GameRng.MasterSeed を渡してはならない。
        /// </param>
        public UltraScenarioBank(ulong plannerSalt)
        {
            _plannerSalt = plannerSalt;
        }

        public ulong GetScenarioSeed(
            ulong publicStateHash,
            int runOrdinal,
            int decisionOrdinal,
            UltraScenarioPhase phase,
            int scenarioIndex)
        {
            if (runOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(runOrdinal));
            if (decisionOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(decisionOrdinal));
            if (scenarioIndex < 0) throw new ArgumentOutOfRangeException(nameof(scenarioIndex));

            ulong domain;
            switch (phase)
            {
                case UltraScenarioPhase.Search:
                    domain = SearchDomain;
                    break;
                case UltraScenarioPhase.Confirmation:
                    domain = ConfirmationDomain;
                    break;
                case UltraScenarioPhase.Shadow:
                    domain = ShadowDomain;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(phase));
            }

            ulong h = Mix(_plannerSalt, domain);
            h = Mix(h, publicStateHash);
            h = Mix(h, unchecked((ulong)(uint)runOrdinal));
            h = Mix(h, unchecked((ulong)(uint)decisionOrdinal));
            h = Mix(h, unchecked((ulong)(uint)scenarioIndex));
            return h;
        }

        internal static ulong Mix(ulong a, ulong b)
        {
            unchecked
            {
                ulong z = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

    }

    public enum UltraScenarioPhase
    {
        Search = 0,
        Confirmation = 1,
        Shadow = 2,
    }
}

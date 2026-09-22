using System;

namespace AutoTest.Ultra
{
    [Serializable]
    public sealed class UltraProductionRunRecord
    {
        public int runOrdinal;
        public int runIndex;
        public ulong scenarioSeed;
        public string scenarioSeedHex;
        public bool valid;
        public bool fullClear;
        public bool crash;
        public bool deadlock;
        public int fingerprint;
        public string deterministicDigest;
    }

    public readonly struct UltraProductionScenarioBinding
    {
        public UltraProductionScenarioBinding(int ordinal, int runIndex, ulong seed)
        {
            RunOrdinal = ordinal;
            RunIndex = runIndex;
            ScenarioSeed = seed;
        }

        public int RunOrdinal { get; }
        public int RunIndex { get; }
        public ulong ScenarioSeed { get; }
        public string ScenarioSeedHex => ScenarioSeed.ToString("x16");
    }

    [Serializable]
    public sealed class UltraProductionBatchRecord
    {
        public string policyId;
        public string scenarioSeedVectorHash;
        public UltraProductionRunRecord[] runs;
        public string outputPath;
        public string artifactPath;
        public string artifactSha256;
        public bool completedNormally;
        public string failureCode;
    }
}

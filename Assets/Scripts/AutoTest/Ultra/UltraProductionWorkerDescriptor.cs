using System;

namespace AutoTest.Ultra
{
    [Serializable]
    public sealed class UltraProductionWorkerDescriptor
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public string executableFile;
        public string executableSha256;
        public string assemblySha256;
        public UltraFingerprintSet fingerprints;
        public string builtUtc;
    }
}

namespace GameLoop
{
    public static class GameRng
    {
        static ulong Mix(ulong z) { z += 0x9E3779B97F4A7C15UL; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }
        static ulong H(string key, int index) { ulong h = 1469598103934665603UL; foreach (char c in key) { h ^= c; h *= 1099511628211UL; } return Mix(h ^ Mix((ulong)(long)index)); }
        public static ulong Salt = 12345;
        public static int Range(int minInclusive, int maxExclusive, string key, int index = -1)
        { if (maxExclusive <= minInclusive) return minInclusive; return minInclusive + (int)(Mix(H(key, index) ^ Salt) % (ulong)(maxExclusive - minInclusive)); }
        public static float Value(string key, int index = -1) => (Mix(H(key, index) ^ Salt) >> 40) / 16777216f;
    }
}

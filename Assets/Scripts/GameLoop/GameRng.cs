using System.Collections.Generic;

namespace GameLoop
{
    /// <summary>
    /// 決定論的乱数 (ハッシュ派生方式)。 正本: docs/GAME.md §22。
    ///
    /// **状態を持つ乱数器を回すのではなく、 その場で `hash(マスターシード, ラン番号, キー, 連番)`
    /// を計算して値を出す。** Minecraft のチャンク生成と同じ考え方。
    ///
    /// なぜこの方式か:
    ///   ストリーム分離方式 (Slay the Spire 型) は「同じ乱数器を何回呼んだか」に結果が依存する。
    ///   抽選点を 1 つ足しただけで、そこから先の全ての結果がズレる。 本プロジェクトは
    ///   バランス調整のたびに抽選点が増減するため、 順序依存は現実的でない。
    ///   ハッシュ派生ならキーさえ同じなら、 途中で何回振ろうが結果が変わらない。
    ///
    /// 使い方:
    ///   GameRng.BeginRun(masterSeed, runIndex);           // ラン開始時に 1 回
    ///   int v = GameRng.Range(0, 6, "combat.playerDie", turn * 10 + i);
    ///   float f = GameRng.Value("shop.discount", floor);
    ///
    /// **キーの一意性が全て。** 別の抽選が同じ (キー, 連番) を使うと常に同じ結果を返す。
    /// 命名規約は `領域.用途` (例: "map.tile", "shop.offer", "vesca.relic")。
    /// 連番には「その抽選を一意にする文脈」(層番号・ターン・スロット番号) を渡すこと。
    /// 省略すると内部カウンタを使うが、 **そのキー内でのみ順序依存**になる。
    ///
    /// 演出/UI の乱数はここを通さない (フレームレートで消費が変わり再現性を壊すため)。
    /// </summary>
    public static class GameRng
    {
        /// <summary>シード未設定 (通常プレイ) か。 true の間は毎回ランダムなマスターシードを使う。</summary>
        public static bool IsSeeded { get; private set; }
        /// <summary>現在のマスターシード。 表示用は <see cref="FormatSeed"/>。</summary>
        public static ulong MasterSeed { get; private set; }
        /// <summary>現在のラン番号 (バッチ内の通し番号)。 単発プレイは 0。</summary>
        public static int RunIndex { get; private set; }

        private static ulong _runSalt;
        private static readonly Dictionary<string, int> _counters = new Dictionary<string, int>();

        // ============================================================
        //  セットアップ
        // ============================================================

        /// <summary>マスターシードを設定する。 以降の BeginRun がこの値から派生する。</summary>
        public static void SetMasterSeed(ulong seed)
        {
            MasterSeed = seed;
            IsSeeded = true;
        }

        /// <summary>シードを解除し、 通常のランダム挙動に戻す。</summary>
        public static void ClearSeed()
        {
            IsSeeded = false;
            MasterSeed = 0;
        }

        /// <summary>16 桁の 10 進文字列をシードとして解釈する。 空/不正なら false。</summary>
        public static bool TryParseSeed(string text, out ulong seed)
        {
            seed = 0;
            if (string.IsNullOrEmpty(text)) return false;
            ulong acc = 0;
            int digits = 0;
            foreach (char c in text)
            {
                if (c == ' ' || c == '-' || c == '_') continue;   // 見やすさのための区切りは許容
                if (c < '0' || c > '9') return false;
                acc = acc * 10 + (ulong)(c - '0');
                if (++digits > 20) return false;
            }
            if (digits == 0) return false;
            seed = acc;
            return true;
        }

        /// <summary>表示用の 16 桁 10 進表記 (4 桁ごとに空白)。</summary>
        public static string FormatSeed(ulong seed)
        {
            string s = (seed % 10000000000000000UL).ToString("D16");
            return s.Substring(0, 4) + " " + s.Substring(4, 4) + " "
                 + s.Substring(8, 4) + " " + s.Substring(12, 4);
        }

        /// <summary>ラン開始時に呼ぶ。 マスターシードとラン番号からそのランの塩を作る。
        /// シード未設定なら毎回異なる塩を引く (＝通常プレイ)。</summary>
        public static void BeginRun(int runIndex)
        {
            RunIndex = runIndex;
            _counters.Clear();
            if (IsSeeded)
            {
                _runSalt = Mix(MasterSeed, (ulong)(long)runIndex + 0x9E3779B97F4A7C15UL);
            }
            else
            {
                // 未シード時: 時刻とフレームから適当な塩を作る (再現性なし = 従来挙動)
                unchecked
                {
                    ulong t = (ulong)System.DateTime.UtcNow.Ticks;
                    ulong f = (ulong)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                    _runSalt = Mix(t, f);
                }
            }
        }

        // ============================================================
        //  抽選
        // ============================================================

        /// <summary>[minInclusive, maxExclusive) の整数。 index を省くとキー内カウンタを使う。</summary>
        public static int Range(int minInclusive, int maxExclusive, string key, int index = -1)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong h = Draw(key, index);
            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(h % span);
        }

        /// <summary>キーを先に取る Range。 機械変換で引数順を保つための糖衣。
        /// float 版と int 版の両方に対応する (Unity の Random.Range は引数型で挙動が変わるため)。</summary>
        public static int RangeAuto(string key, int minInclusive, int maxExclusive)
            => Range(minInclusive, maxExclusive, key);

        /// <summary>float 版 Range: [min, max) の実数。</summary>
        public static float RangeAuto(string key, float min, float max)
            => min + Value(key) * (max - min);

        /// <summary>[0,1) の float。</summary>
        public static float Value(string key, int index = -1)
        {
            // 上位 24bit を使う (float の仮数に収まる範囲)
            return (Draw(key, index) >> 40) / 16777216f;
        }

        /// <summary>確率 p で true。</summary>
        public static bool Chance(float p, string key, int index = -1)
            => Value(key, index) < p;

        /// <summary>リストから 1 つ選ぶ。 空なら default。</summary>
        public static T Pick<T>(IList<T> list, string key, int index = -1)
            => (list == null || list.Count == 0) ? default(T) : list[Range(0, list.Count, key, index)];

        // ============================================================
        //  内部
        // ============================================================

        /// <summary>この生成器から値を引いた累計回数。 **診断専用で抽選には一切影響しない。**
        ///
        /// <para>Ultra の RNG 契約「思考の前後で本ランの <see cref="GameRng"/> を呼ばない・
        /// 進めない」を**検査可能にする**ために置いてある。 <c>_counters</c> はキー別なので、
        /// 外から「何かを消費したか」を見る手段が他に無い ── 別キーを消費されても
        /// 気付けないと、 契約は「守っているつもり」で終わる。</para></summary>
        public static long TotalDraws { get; private set; }

        private static ulong Draw(string key, int index)
        {
            TotalDraws++;
            int idx = index;
            if (idx < 0)
            {
                _counters.TryGetValue(key, out idx);
                _counters[key] = idx + 1;
            }
            return Mix(Mix(_runSalt, Fnv1a64(key)), (ulong)(long)idx + 0xD6E8FEB86659FD93UL);
        }

        /// <summary>SplitMix64 の finalizer。 2 値を混ぜて 64bit を返す。</summary>
        private static ulong Mix(ulong a, ulong b)
        {
            unchecked
            {
                ulong z = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>FNV-1a 64bit。 **文字列ハッシュは .NET 標準を使わないこと** ──
        /// string.GetHashCode はプロセス/バージョンごとに変わり、 再現性を壊す。</summary>
        private static ulong Fnv1a64(string s)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                if (s != null)
                    for (int i = 0; i < s.Length; i++)
                    {
                        h ^= s[i];
                        h *= 1099511628211UL;
                    }
                return h;
            }
        }
    }
}

using System.IO;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// メタバフ/メタデバフ ON/OFF の組み合わせを表すプロファイル。
    /// 学習データを各プロファイル別のサブディレクトリで分離し、
    /// バッチ実行時の Meta 系初期化も自動で切り替える。
    /// </summary>
    public enum MetaProfile
    {
        BuffOff_DebuffOff = 0, // 素プレイ (バフ/デバフ全停止)
        BuffOn_DebuffOff  = 1, // バフのみ有効 (既存学習データの引継ぎ先)
        BuffOn_DebuffOn   = 2, // フル設定 (バフ&デバフ両ON)
    }

    /// <summary>
    /// MetaProfile のグローバル状態管理 + パスヘルパ。
    /// バッチ起動時に SetCurrent し、 stats / regression / policy 系の
    /// 各クラスは LearningRoot() を介して該当サブディレクトリを使う。
    /// </summary>
    public static class MetaProfileHelper
    {
        private static MetaProfile _current = MetaProfile.BuffOn_DebuffOff;

        public static MetaProfile Current => _current;
        public static void SetCurrent(MetaProfile p) { _current = p; }

        public static string Suffix(MetaProfile p)
        {
            switch (p)
            {
                case MetaProfile.BuffOff_DebuffOff: return "buffOff_debuffOff";
                case MetaProfile.BuffOn_DebuffOff:  return "buffOn_debuffOff";
                case MetaProfile.BuffOn_DebuffOn:   return "buffOn_debuffOn";
                default:                            return "buffOn_debuffOff";
            }
        }

        public static string CurrentSuffix => Suffix(_current);

        public static bool BuffOn(MetaProfile p)   => p != MetaProfile.BuffOff_DebuffOff;
        public static bool DebuffOn(MetaProfile p) => p == MetaProfile.BuffOn_DebuffOn;
        public static bool CurrentBuffOn   => BuffOn(_current);
        public static bool CurrentDebuffOn => DebuffOn(_current);

        /// <summary>現プロファイルの学習データ用ルートディレクトリ
        /// (AutoRunLogs/learning/&lt;suffix&gt;)。 ディレクトリは未作成でも返す。</summary>
        public static string LearningRoot()
        {
            string baseRoot = Path.Combine(Application.dataPath, "..", "AutoRunLogs", "learning", CurrentSuffix);
            return Path.GetFullPath(baseRoot);
        }

        public static string LearningRootFor(MetaProfile p)
        {
            string baseRoot = Path.Combine(Application.dataPath, "..", "AutoRunLogs", "learning", Suffix(p));
            return Path.GetFullPath(baseRoot);
        }

        /// <summary>
        /// 難易度別 Item Tier 学習の隔離領域。
        /// 通常の policy / event / boss 学習とは混ぜず、同じメタ進行プロファイルの下へ
        /// tier_score_0 / tier_score_30 / tier_score_50 として保存する。
        /// </summary>
        public static string TierLearningRoot(int challengeScore)
        {
            string baseRoot = Path.Combine(LearningRoot(), $"tier_score_{Mathf.Max(0, challengeScore)}");
            return Path.GetFullPath(baseRoot);
        }

        // ============================================================
        //  BOT のアイテム評価学習: **挑戦スコア帯ごとに分離** (2026-08-17)
        // ============================================================
        // 0pt で無双できるアイテムと、 高難易度で要求されるアイテムは違う。
        // 全部を 1 つの item_stats.json へ混ぜると、 **ラン数の多い 0pt が序列を支配**し、
        // 高難易度で本当に要る品 (回復・シールド・希望維持) が下位に沈む。
        //
        // 帯は 0 / 1-15 / 16-30 / 31+ の 4 つ。 実際のランは任意の pt を取りうるので
        // 丸める必要があるが、 細かく割ると 1 帯あたりのサンプルが貯まらない。
        //
        // **Tier 表生成用の `tier_score_*` とは別系統。** あちらは人間が読む md を作るための
        // 隔離領域で、 BOT の購入判断には一切入らない (2026-08-17 に取り違えた)。

        /// <summary>挑戦スコアの帯名。 ディレクトリ名にそのまま使う。</summary>
        public static string ChallengeBand(int challengeScore)
        {
            if (challengeScore <= 0)  return "band_0";
            if (challengeScore <= 15) return "band_1_15";
            if (challengeScore <= 30) return "band_16_30";
            return "band_31up";
        }

        /// <summary>BOT のアイテム評価学習ルート。 帯 0 は従来どおりプロファイル直下を使う
        /// (既存の累積 1 万ラン超はほぼ全部 0pt のバッチなので、 そのまま 0pt 帯として引き継ぐ)。</summary>
        public static string BotLearningRoot(int challengeScore)
        {
            string band = ChallengeBand(challengeScore);
            if (band == "band_0") return LearningRoot();
            return Path.GetFullPath(Path.Combine(LearningRoot(), "bot_" + band));
        }

        /// <summary>プロファイル非依存の共有ルート (AutoRunLogs/learning)。
        /// ボス難易度係数など「全プロファイル共通で1つだけ持つべきデータ」用。</summary>
        public static string SharedLearningRoot()
        {
            string baseRoot = Path.Combine(Application.dataPath, "..", "AutoRunLogs", "learning");
            return Path.GetFullPath(baseRoot);
        }

        /// <summary>ボス難易度の調整基準プロファイル (デバフ無し=難易度0)。
        /// このプロファイルのバッチでのみ共有ボス係数を自動調整し、 他は読み取り専用で継承する。</summary>
        public const MetaProfile BaselineProfile = MetaProfile.BuffOn_DebuffOff;
        public static bool CurrentIsBaseline => _current == BaselineProfile;

        public static string DisplayName(MetaProfile p)
        {
            switch (p)
            {
                case MetaProfile.BuffOff_DebuffOff: return "素プレイ (バフOFF/デバフOFF)";
                case MetaProfile.BuffOn_DebuffOff:  return "バフのみ (バフON/デバフOFF)";
                case MetaProfile.BuffOn_DebuffOn:   return "フル設定 (バフON/デバフON)";
                default:                            return p.ToString();
            }
        }
    }
}

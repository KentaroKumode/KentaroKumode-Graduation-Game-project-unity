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

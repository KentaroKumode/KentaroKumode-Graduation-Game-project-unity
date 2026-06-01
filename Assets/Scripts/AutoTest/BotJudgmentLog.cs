using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// BOT の判断基準が変化したときに BALANCE_CHANGELOG_&lt;profile&gt;.md に追記する。
    /// プロファイル (buffOn_debuffOff 等) ごとに別ファイルへ分離 (2026-05-31)。
    /// 挿入ポイント `<!-- BOT_LOG_INSERT_BELOW -->` の直下に新エントリを追記 (降順)。
    /// ファイルが無ければヘッダ+マーカーを自動生成する。
    /// </summary>
    public static class BotJudgmentLog
    {
        private const string Marker = "<!-- BOT_LOG_INSERT_BELOW -->";

        /// <summary>現プロファイル専用の changelog パス。 例: BALANCE_CHANGELOG_buffOn_debuffOff.md</summary>
        private static string LogPath
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                $"BALANCE_CHANGELOG_{MetaProfileHelper.CurrentSuffix}.md"));

        /// <summary>ファイルが存在しなければヘッダ + 挿入マーカーで新規作成する。</summary>
        private static void EnsureFile(string path)
        {
            if (File.Exists(path)) return;
            var sb = new StringBuilder();
            sb.AppendLine($"# バランス変更ログ ({MetaProfileHelper.CurrentSuffix})");
            sb.AppendLine();
            sb.AppendLine("> 自動生成。 BOT の判断基準 (L1 Tier / L2 policy) が変化したバッチで追記される。");
            sb.AppendLine("> 新しいエントリほど上 (マーカー直下) に挿入される。");
            sb.AppendLine();
            sb.AppendLine(Marker);
            sb.AppendLine();
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>マーカー直下に1ブロックを差し込む (新しいエントリほど上に来る)。</summary>
        public static void Append(string entryMarkdown)
        {
            try
            {
                string path = LogPath;
                EnsureFile(path);
                string txt = File.ReadAllText(path, Encoding.UTF8);
                int idx = txt.IndexOf(Marker, StringComparison.Ordinal);
                if (idx < 0)
                {
                    Debug.LogWarning("[BotJudgmentLog] 挿入マーカー未検出。 末尾に追記");
                    File.AppendAllText(path, "\n" + entryMarkdown + "\n", new UTF8Encoding(false));
                    return;
                }
                int insertAt = idx + Marker.Length;
                var sb = new StringBuilder(txt.Length + entryMarkdown.Length + 16);
                sb.Append(txt, 0, insertAt);
                sb.Append("\n\n");
                sb.Append(entryMarkdown);
                sb.Append(txt, insertAt, txt.Length - insertAt);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning($"[BotJudgmentLog] 追記失敗: {e.Message}"); }
        }

        public static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}

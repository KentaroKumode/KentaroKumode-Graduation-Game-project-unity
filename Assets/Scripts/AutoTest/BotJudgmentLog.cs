using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// BOT の判断基準が変化したときに BALANCE_CHANGELOG.md に追記する。
    /// 挿入ポイント `<!-- BOT_LOG_INSERT_BELOW -->` の直下に新エントリを追記 (降順)。
    /// </summary>
    public static class BotJudgmentLog
    {
        private const string Marker = "<!-- BOT_LOG_INSERT_BELOW -->";

        private static string LogPath
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BALANCE_CHANGELOG.md"));

        /// <summary>マーカー直下に1ブロックを差し込む (新しいエントリほど上に来る)。</summary>
        public static void Append(string entryMarkdown)
        {
            try
            {
                string path = LogPath;
                if (!File.Exists(path))
                {
                    Debug.LogWarning("[BotJudgmentLog] BALANCE_CHANGELOG.md が存在しません。 追記スキップ");
                    return;
                }
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

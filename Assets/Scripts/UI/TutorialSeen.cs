using System.Collections.Generic;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// 手引き (issue #86) の「見た」印。 プレイヤー単位でランをまたいで覚える。 保存は <see cref="GameSettings"/> と同じ PlayerPrefs
    /// (メタ進行はエディタの起動ごとに初期化されるので、 そちらには載せない)。 鍵は 1 つ ("Tutorial.seen")、 値は見た節の見出しを改行で並べたもの。
    /// 見出しは handbook の「## 見出し」(<see cref="Game2D.TutorialTopics"/>)。 消すのは設定の画面の「手引きをもう一度」(<see cref="Clear"/>)。
    /// </summary>
    public static class TutorialSeen
    {
        private const string Pref = "Tutorial.seen";
        private static HashSet<string> _set;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _set = null; }

        private static HashSet<string> Set
        {
            get
            {
                if (_set != null) return _set;
                _set = new HashSet<string>();
                foreach (var s in PlayerPrefs.GetString(Pref, "").Split('\n')) if (s.Length > 0) _set.Add(s);
                return _set;
            }
        }

        public static bool Has(string topic) => !string.IsNullOrEmpty(topic) && Set.Contains(topic);

        /// <summary>見た印を付けて、 すぐディスクへ書く (落ちても同じ手引きを二度出さない)。</summary>
        public static void Mark(string topic)
        {
            if (string.IsNullOrEmpty(topic) || !Set.Add(topic)) return;
            PlayerPrefs.SetString(Pref, string.Join("\n", Set));
            PlayerPrefs.Save();
        }

        /// <summary>全部の見た印を消す (次にその場面へ来た時、 また出る)。</summary>
        public static void Clear()
        {
            Set.Clear();
            PlayerPrefs.DeleteKey(Pref);
            PlayerPrefs.Save();
        }

        public static int Count => Set.Count;
    }
}

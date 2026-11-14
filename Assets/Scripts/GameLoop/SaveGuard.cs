using System.Collections.Generic;
using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// セーブを一切書かない切り替え (2026-11-14・実機で演出を確かめるため・<c>UI.Game2D.Battle.QaBattle</c> が立てる)。
    /// 立っている間は、 ディスク / レジストリへ書く経路 (ランの中断セーブ・メタ進行・設定・手引きの見た印・観測所・BGM のミュート) を全部止める。
    /// 読むのは普段どおり。 <b>既定は切</b>で、 切の時は今までと同じ呼び出しをそのまま通す (振る舞いは 1 ビットも変わらない)。
    ///
    /// <para>PlayerPrefs を書く所は、 ここの <see cref="SetInt"/> / <see cref="SetString"/> / <see cref="DeleteKey"/> / <see cref="Save"/> を通す。
    /// 立っている間に書いた値は、 このプロセスの間だけ手元に持つ (<see cref="GetInt"/> で読める) ── 画面の動きは普段と同じで、 ディスクへは出ない。</para>
    ///
    /// <para>BOT の側の仕組み (<see cref="GameHooks.MetaPersistenceDisabled"/>・<c>RunSave.Disabled</c>) は読み込みも止めるので、 これとは別。
    /// 新しく永続化の書き込みを足す時は、 ここを通すこと (docs/qa/battle-fx.md)。</para>
    /// </summary>
    public static class SaveGuard
    {
        /// <summary>立っている間、 セーブを書かない。</summary>
        public static bool Suppressed;

        private static readonly Dictionary<string, object> Shadow = new Dictionary<string, object>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Suppressed = false; Shadow.Clear(); }

        public static void SetInt(string key, int v) { if (Suppressed) Shadow[key] = v; else PlayerPrefs.SetInt(key, v); }
        public static void SetString(string key, string v) { if (Suppressed) Shadow[key] = v; else PlayerPrefs.SetString(key, v); }
        public static void DeleteKey(string key) { if (Suppressed) Shadow[key] = null; else PlayerPrefs.DeleteKey(key); }
        public static void Save() { if (!Suppressed) PlayerPrefs.Save(); }

        /// <summary>止めている間に書いた値があればそれを、 無ければ PlayerPrefs を読む (切の時は PlayerPrefs そのもの)。</summary>
        public static int GetInt(string key, int def)
        {
            if (Suppressed && Shadow.TryGetValue(key, out var o)) return o is int i ? i : def;
            return PlayerPrefs.GetInt(key, def);
        }
    }
}

using System;

namespace MetaProgression
{
    /// <summary>PlayerPrefs の代わりに差し込める鍵と値の置き場 (本体は <c>MetaProgressManager</c> の PlayerPrefs の口・検算 Tools/save_check は辞書)。</summary>
    public interface IPrefsStore
    {
        bool HasKey(string key);
        string GetString(string key);
        void SetString(string key, string value);
        void DeleteKey(string key);
    }

    /// <summary>
    /// 1 つの鍵に JSON を置く記録を、 壊れても黙って失わない読み書き (2026-11-13・docs/qa/spec-gaps.md G-25)。 UnityEngine に依存しない。
    ///
    /// <para><b>保存</b> (<see cref="Save"/>): 今の値が読める値なら、 それを控えの鍵 (<c>鍵.bak</c>) へ写してから新しい値を書く。
    /// 読めない値は控えへ写さない (控えまで壊さない)。</para>
    ///
    /// <para><b>読み込み</b> (<see cref="Load"/>):
    /// 鍵が無い = 初回 (記録なし) → <see cref="LoadSource.Empty"/>。 <b>控えは見ない</b> (初回は空が正しい・消した後に古い値を戻さない)。
    /// 鍵があって読める → <see cref="LoadSource.Main"/>。
    /// 鍵があるのに読めない (空文字・JSON として読めない) → 壊れた値を <c>鍵.corrupt</c> へ退避し、 控えが読めれば <see cref="LoadSource.Backup"/>、
    /// 控えも駄目なら <see cref="LoadSource.CorruptReset"/> (空で始める・壊れた値は退避済みなので次の保存で上書きしても失わない)。</para>
    /// </summary>
    public static class BackedPrefs
    {
        public const string BakSuffix = ".bak";
        public const string CorruptSuffix = ".corrupt";

        public enum LoadSource
        {
            /// <summary>鍵が無い (初回・記録なし)。 空で始める。</summary>
            Empty,
            /// <summary>本番の鍵から読めた。</summary>
            Main,
            /// <summary>本番が壊れていて、 控えから読めた (壊れた値は .corrupt へ退避)。</summary>
            Backup,
            /// <summary>本番も控えも駄目。 空で始める (壊れた値は .corrupt へ退避)。</summary>
            CorruptReset,
        }

        /// <summary>読む。 <paramref name="json"/> は使う値 (Empty / CorruptReset は null)。 <paramref name="note"/> はログに出す一文 (Main / Empty は null)。
        /// <paramref name="isValid"/> は「この文字列を記録として読めるか」 (本体は JsonUtility で確かめる)。</summary>
        public static LoadSource Load(IPrefsStore store, string key, Func<string, bool> isValid, out string json, out string note)
        {
            json = null; note = null;
            if (!store.HasKey(key)) return LoadSource.Empty;
            string main = store.GetString(key);
            if (IsUsable(main, isValid)) { json = main; return LoadSource.Main; }

            // 本番が壊れている。 次の保存で上書きされる前に退避する。
            store.SetString(key + CorruptSuffix, main ?? "");
            string bakKey = key + BakSuffix;
            string bak = store.HasKey(bakKey) ? store.GetString(bakKey) : null;
            string what = string.IsNullOrEmpty(main) ? "空" : $"読めない ({(main.Length)} 文字)";
            if (IsUsable(bak, isValid))
            {
                json = bak;
                note = $"「{key}」が{what}ので、 控え「{bakKey}」から読んだ (1 つ前の保存の値)。 壊れた値は「{key}{CorruptSuffix}」に退避した";
                return LoadSource.Backup;
            }
            note = $"「{key}」が{what}で、 控え「{bakKey}」も{(bak == null ? "無い" : "読めない")}ので、 空の状態で始める。 壊れた値は「{key}{CorruptSuffix}」に退避した";
            return LoadSource.CorruptReset;
        }

        /// <summary>書く。 今の値が読める値で、 新しい値と違えば、 控えへ写してから書く。</summary>
        public static void Save(IPrefsStore store, string key, string json, Func<string, bool> isValid)
        {
            if (store.HasKey(key))
            {
                string old = store.GetString(key);
                if (old != json && IsUsable(old, isValid)) store.SetString(key + BakSuffix, old);
            }
            store.SetString(key, json);
        }

        /// <summary>消す (やり直し)。 本番と控えを消す。 控えが残ると、 消した後の初回でも控えが読まれる道ができる (読み込みは鍵が無ければ控えを見ないが、 念のため)。
        /// 退避した壊れた値 (.corrupt) は調べるための物なので残す。</summary>
        public static void DeleteAll(IPrefsStore store, string key)
        {
            store.DeleteKey(key);
            store.DeleteKey(key + BakSuffix);
        }

        private static bool IsUsable(string s, Func<string, bool> isValid)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string t = s.Trim();
            // 途中で切れた JSON を弾く安い確かめ (JsonUtility は途中で切れた文を例外にするが、 それに頼り切らない)
            if (t.Length < 2 || t[0] != '{' || t[t.Length - 1] != '}') return false;
            try { return isValid == null || isValid(s); }
            catch { return false; }
        }
    }
}

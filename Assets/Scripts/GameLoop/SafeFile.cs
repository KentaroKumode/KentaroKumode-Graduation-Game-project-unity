using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GameLoop
{
    /// <summary>
    /// 落ちても記録を失わないファイルの書き方 (2026-11-13・docs/qa/spec-gaps.md G-24)。 UnityEngine に依存しない (検算 Tools/save_check が同じ物を動かす)。
    ///
    /// <para><b>書き方</b> (<see cref="WriteAtomic"/>): ① 書きかけ (<c>.tmp</c>) に書いて<b>ディスクまで落とす</b> (<c>Flush(true)</c>) →
    /// ② 本番があれば控え (<c>.bak</c>) へ<b>名前を変えて</b>退ける → ③ 書きかけを本番の名前へ変える → ④ 控えを消す。
    /// どこで落ちても、 本番・控え・書きかけのどれかに完全な記録が残る:
    /// ① の途中 = 本番が無傷 / ②〜③ の間 = 本番が無く、 控え (前の記録) と書きかけ (新しい記録・ディスクに落とし済み) が残る / ③〜④ の間 = 本番が新しい記録。</para>
    ///
    /// <para><b>使わなかった物。</b> <c>File.Replace</c> (控え付きの置き換え) は Windows では ReplaceFile を呼ぶが、 Unity 2022.3 の Mono の実装の
    /// 細部 (失敗した時にどの名前に何が残るか) を資料で確かめられなかったので使わない。 <c>File.Move(src, dst, overwrite)</c> は
    /// .NET Core 3.0 からの物で、 Unity 2022.3 の API (.NET Standard 2.1 / .NET Framework 4.x) には無い。 2 引数の <c>File.Move</c> は
    /// 行き先があると IOException を投げる (.NET Framework・Mono・.NET Core で同じ) ので、 行き先を先に空けてから名前を変える。
    /// 同じフォルダの中の名前の変更は、 Windows (NTFS) では MoveFileEx 1 回で、 途中の状態を作らない。</para>
    ///
    /// <para><b>読み方</b>: 本番 → 控え → 書きかけの順に試し、 中身を確かめて最初に使える物を取る (<see cref="Candidates"/>)。
    /// 控え・書きかけから読めたら <see cref="Promote"/> で本番の名前へ戻す。 <b>消す時は 3 つとも消す</b> (<see cref="DeleteAll"/>) ──
    /// 控えが残ると、 次の起動で終わった記録が生き返る。</para>
    /// </summary>
    public static class SafeFile
    {
        public const string BakSuffix = ".bak";
        public const string TmpSuffix = ".tmp";

        /// <summary>BOM なしの UTF-8 (旧い <c>File.WriteAllText</c> と同じ)。</summary>
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static string BakOf(string path) => path + BakSuffix;
        public static string TmpOf(string path) => path + TmpSuffix;

        /// <summary>落ちても記録を失わない書き方で <paramref name="text"/> を <paramref name="path"/> へ書く (手順は型の説明)。 失敗は例外。
        /// <paramref name="afterStep"/> は検算用 (各手順の後に "tmp" / "bak" / "move" / "done" で呼ぶ・例外を投げると「そこで落ちた」の再現になる)。</summary>
        public static void WriteAtomic(string path, string text, Action<string> afterStep = null)
        {
            string tmp = TmpOf(path), bak = BakOf(path);
            byte[] bytes = Utf8.GetBytes(text ?? "");
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);   // OS の書き込み待ちに置かず、 ディスクまで落とす (落ちた後に長さ 0 の書きかけを残さない)
            }
            afterStep?.Invoke("tmp");
            if (File.Exists(path))
            {
                if (File.Exists(bak)) File.Delete(bak);   // 前に落ちた時の控え (本番がある = もう要らない)
                File.Move(path, bak);
            }
            afterStep?.Invoke("bak");
            File.Move(tmp, path);
            afterStep?.Invoke("move");
            if (File.Exists(bak)) File.Delete(bak);
            afterStep?.Invoke("done");
        }

        /// <summary>読む順 (本番 → 控え → 書きかけ) と、 ログに出す呼び名。</summary>
        public static IEnumerable<(string path, string label)> Candidates(string path)
        {
            yield return (path, "本番");
            yield return (BakOf(path), "控え");
            yield return (TmpOf(path), "書きかけ");
        }

        /// <summary>読み込みの結果。</summary>
        public enum ReadResult
        {
            /// <summary>使える記録があった (<c>usedPath</c> が本番以外なら控え・書きかけから)。</summary>
            Ok,
            /// <summary>3 つとも無い。</summary>
            Missing,
            /// <summary>あったが、 どれも中身が使えない (捨ててよい)。</summary>
            Unusable,
            /// <summary>開けないファイルがあった (他のプロセスが掴んでいる等・次は読めるかもしれないので消さない)。</summary>
            IoError,
        }

        /// <summary>本番 → 控え → 書きかけの順に読み、 <paramref name="validate"/> (使えれば null・使えなければ理由) が通った最初の物を返す。
        /// <paramref name="firstError"/> は最初に出会った失敗の理由。 <paramref name="warn"/> には候補ごとの失敗を渡す (ログ用)。
        /// 控え・書きかけから読めても名前は戻さない (戻すのは呼ぶ側が中身を使うと決めてから <see cref="Promote"/>)。</summary>
        public static ReadResult TryReadFirst(string path, Func<string, string> validate, out string text, out string usedPath, out string usedLabel,
                                              out string firstError, Action<string> warn = null)
        {
            text = null; usedPath = null; usedLabel = null; firstError = null;
            bool any = false, io = false;
            foreach (var (p, label) in Candidates(path))
            {
                string s;
                try
                {
                    if (!File.Exists(p)) continue;
                    any = true;
                    s = File.ReadAllText(p);
                }
                catch (Exception e) { io = true; firstError ??= "読めない: " + e.Message; warn?.Invoke($"{label}が読めない: {e.Message}"); continue; }
                string bad;
                try { bad = validate(s); }
                catch (Exception e) { bad = "形式が壊れている: " + e.Message; }
                if (bad != null) { firstError ??= bad; warn?.Invoke($"{label}を使えない: {bad}"); continue; }
                text = s; usedPath = p; usedLabel = label;
                return ReadResult.Ok;
            }
            if (!any) return ReadResult.Missing;
            return io ? ReadResult.IoError : ReadResult.Unusable;
        }

        /// <summary>本番・控え・書きかけのどれかがあるか。</summary>
        public static bool AnyExists(string path)
            => File.Exists(path) || File.Exists(BakOf(path)) || File.Exists(TmpOf(path));

        /// <summary>本番・控え・書きかけを全部消す。 消せない物があれば false (理由を <paramref name="error"/> に・ほかは消す)。</summary>
        public static bool DeleteAll(string path, out string error)
        {
            error = null;
            foreach (var (p, _) in Candidates(path))
            {
                try { if (File.Exists(p)) File.Delete(p); }
                catch (Exception e) { error = (error == null ? "" : error + " / ") + Path.GetFileName(p) + ": " + e.Message; }
            }
            return error == null;
        }

        /// <summary>控え・書きかけ (<paramref name="from"/>) から読めた時、 それを本番の名前へ戻し、 残りを消す。 失敗しても読めた中身は使えるので false を返すだけ。</summary>
        public static bool Promote(string path, string from, out string error)
        {
            error = null;
            try
            {
                if (from != path)
                {
                    if (File.Exists(path)) File.Delete(path);   // 壊れた本番
                    File.Move(from, path);
                }
                foreach (var (p, _) in Candidates(path))
                    if (p != path && File.Exists(p)) File.Delete(p);
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }
    }
}

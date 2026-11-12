using System;
using System.Collections.Generic;

namespace EventSystem
{
    /// <summary>
    /// 確率分岐ごとの文 (選んだ後の文・記録の文) の切り分け (2026-11-12)。
    ///
    /// <para>書式: 文の中で分岐を <c>||</c> で区切る (<c>文A||文B@@記録A||記録B</c>)。 並びは結果の欄の
    /// 「N%で」の順。 区切りが無ければ全分岐で同じ文 (これまでどおり)。</para>
    ///
    /// <para><b>UnityEngine に依存させない</b> ── <c>Tools/event_branch_check</c> が Unity なしでこのファイルだけを
    /// コンパイルして検算する。 警告の出力は呼び出し側 (EventParser) の仕事。</para>
    /// </summary>
    public static class EventBranchText
    {
        public const string Separator = "||";

        /// <summary><paramref name="text"/> を分岐ごとに切る。
        /// <list type="bullet">
        /// <item>区切りが無い → null を返す (全分岐で同じ文)。 <paramref name="first"/> は text そのもの。</item>
        /// <item>区切りの数 + 1 が <paramref name="branchCount"/> と一致 → 分岐ごとの文のリスト (各要素は Trim 済み)。</item>
        /// <item>一致しない (確率分岐が無いのに区切った場合を含む) → null を返し、 <paramref name="problem"/> に理由。
        ///   <paramref name="first"/> は先頭の文 (全分岐でこれを使う)。</item>
        /// </list></summary>
        public static List<string> Split(string text, int branchCount, out string first, out string problem)
        {
            problem = null;
            first = text;
            if (string.IsNullOrEmpty(text) || text.IndexOf(Separator, StringComparison.Ordinal) < 0) return null;

            var segs = text.Split(new[] { Separator }, StringSplitOptions.None);
            var list = new List<string>(segs.Length);
            foreach (var s in segs) list.Add(s.Trim());
            first = list[0];

            if (branchCount < 2)
            {
                problem = $"確率分岐が無いのに文を {list.Count} つに分けている。先頭の文を使う";
                return null;
            }
            if (list.Count != branchCount)
            {
                problem = $"文が {list.Count} つ・分岐が {branchCount} つで合わない。先頭の文を全分岐に使う";
                return null;
            }
            return list;
        }

        /// <summary>分岐 <paramref name="branch"/> の文。 分けていない (<paramref name="byBranch"/> が null)・
        /// 分岐を引いていない (負) なら <paramref name="fallback"/>。</summary>
        public static string For(List<string> byBranch, int branch, string fallback)
        {
            if (byBranch == null || branch < 0 || branch >= byBranch.Count) return fallback;
            return byBranch[branch];
        }
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// 演出のデータ (ヴェスカの破片の形 <c>vesca-shards.json</c>・帯の表 <c>vesca-tear.json</c>) を読むための小さな JSON の読み手 (2026-11-14)。
    /// JsonUtility は入れ子の配列と null の要素を読めないので、 ここで読む。 戻り値: オブジェクト = Dictionary&lt;string, object&gt; /
    /// 配列 = List&lt;object&gt; / 数 = double / 文字列 = string / true・false = bool / null = null。
    /// </summary>
    public static class FxJson
    {
        public static object Parse(string s) { int i = 0; return Value(s, ref i); }

        private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i); i++;      // ':'
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (s[i++] == '}') return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (s[i++] == ']') return l;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            int b = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(b, i - b), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;      // '"'
            while (s[i] != '"')
            {
                if (s[i] == '\\') { i++; char e = s[i]; sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'u' ? (char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber) : e); if (e == 'u') i += 4; i++; }
                else sb.Append(s[i++]);
            }
            i++;
            return sb.ToString();
        }

        public static double Num(object o) => o is double d ? d : 0;
        public static int Int(object o) => (int)Num(o);
    }
}

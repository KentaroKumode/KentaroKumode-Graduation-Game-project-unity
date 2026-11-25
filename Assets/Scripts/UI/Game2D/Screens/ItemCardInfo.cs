using System;
using System.Collections.Generic;

namespace UI.Game2D
{
    /// <summary>
    /// 品 1 つぶんの、 画面に出す中身 (2026-11-13・持ち物の画面)。 升目・ホバーの吹き出し (<see cref="ItemTooltip"/>)・
    /// フォーカスの詳細の欄 (<see cref="ItemDetailPane"/>) が同じ物を読む ── 店や報酬の画面でも、 これを作れば同じ部品で出せる。
    /// <b>UnityEngine に依存しない</b> (検算 Tools/inventory_check が items.json の全件からこれを作って、 欄の送りで切り捨てなく読めるかを見る)。
    /// ゲームの状態からの作り方は <see cref="ItemCards"/> (Unity 側)。
    /// </summary>
    /// <summary>
    /// 説明文の【】の言葉の説明 (2026-11-25・作者: 定義はキーワードの説明に入れ、 品の説明文は縮める)。 詳細の欄が説明文の後に出す。
    /// 品 1 つの中だけで使う数 (力み・反骨) は【】を付けずに品の説明文で書く (2026-11-25)。 ほかの品やカードから呼ばれる言葉はここに定義を置く。
    /// 数値の正本は PoisonCore / CritCore / ShieldCore / HeatCore と docs/GAME.md §7。 数値を変えたらここも直す。
    /// </summary>
    public static class KeywordGlossary
    {
        public static readonly Dictionary<string, string> Text = new Dictionary<string, string>
        {
            { "【劇症】", "【劇症】敵の毒を 3 分の 1 を消費し、「消費量 × 4」と「消費量 × 敵の最大 HP の 0.25%」の大きい方の軽減不可ダメージ。" },
            { "【練気】", "【練気】会心の秘宝を持つ戦闘で、攻撃が会心するたび +1（戦闘の間は減らない）。会心した攻撃は【練気】1 につき会心倍率 +0.06。" },
            { "【集中】", "【集中】集中の秘宝を 3 つ以上持ち、攻撃のカードを 1 枚だけ発動したターンの攻撃。会心率が低下するが、会心すれば【練気】の半分を使って集中会心の効果が働く。" },
            { "【ジャスト防御】", "【ジャスト防御】敵の攻撃を HP を 1 も失わずに受け、ブロックとシールドの合計と攻撃の差が 2 以下のときに発生する。" },
            { "【過熱】", "【過熱】爆発が起きたターンに獲得。ターン終了時に失われる。" },
            { "【再起動】", "【再起動】このターンに発動したカード 1 枚を、ダイスなしでもう 1 回発動する。" },
            { "【オーバークロック】", "【オーバークロック】充電を使い、そのターンの最初の攻撃の最終与ダメージを増加させ、敵の防御をすべて無視する。" },
        };

        /// <summary>説明文に出てくる順に、 載っている言葉の説明を返す (同じ言葉は 1 度)。</summary>
        public static List<string> In(string description)
        {
            var r = new List<string>();
            if (string.IsNullOrEmpty(description)) return r;
            var seen = new HashSet<string>();
            int i = 0;
            while ((i = description.IndexOf('【', i)) >= 0)
            {
                int j = description.IndexOf('】', i);
                if (j < 0) break;
                string k = description.Substring(i, j - i + 1);
                if (seen.Add(k) && Text.TryGetValue(k, out var t)) r.Add(t);
                i = j + 1;
            }
            return r;
        }
    }

    public sealed class ItemCardInfo
    {
        /// <summary>種類 (持ち物の画面の区切りの順)。</summary>
        public enum Kind { Relic = 0, Weapon = 1, Consumable = 2, Dice = 3, Keepsake = 4 }
        /// <summary>区切りの見出し (<see cref="Kind"/> の順)。 ランの中の持ち物は「秘宝」(CLAUDE.md の用語)。
        /// 「大事な物」は ownedFlags のフラグの品 (イベントの〈苦難の予言〉〈迷い犬の首輪〉など)。</summary>
        public static readonly string[] KindNames = { "秘宝", "武器", "消耗品", "賽", "大事な物" };
        public const int KindCount = 5;

        public Kind kind;
        /// <summary>アイコンの名前 (Resources/Icons/Items/&lt;iconId&gt;.png)。 null は絵を探さない (賽・強化素材・大事な物)。</summary>
        public string iconId;
        /// <summary>同じ品を見分ける鍵 (使った後にフォーカスを同じ品へ戻す)。</summary>
        public string key;
        public string name, description, flavor;
        /// <summary>種類の字 (「秘宝」「武器」「強化素材」など)。 区切りの見出しと違う時がある (強化素材は武器の区切りに入る)。</summary>
        public string kindLabel;
        /// <summary>レア度 (ItemSystem.ItemRarity の値)。 -1 は無し。 升目の枠の色はこれで決まる。</summary>
        public int rarity = -1;
        public string axis, family;
        /// <summary>段 (武器 2〜4・消耗品 1〜4)。 0 は無し。</summary>
        public int tier;
        /// <summary>同じ品の数 (消耗品・強化素材)。 2 以上で升目の右下に数を出す。</summary>
        public int count = 1;
        /// <summary>売値の字 (「4〜6G」)。 null は出さない (売れない品・武器)。</summary>
        public string sell;
        /// <summary>手放せない品・預かり物の印 (「手放せない」「預かり物」)。 null は無し。</summary>
        public string mark;
        /// <summary>今は使えない理由 (消耗品)。 null は無し。</summary>
        public string block;
        /// <summary>アイコンの無い時の仮の字 (<see cref="Glyph"/>)。 賽は「賽」。</summary>
        public string glyph;
        /// <summary>升目の下の辺に打つ点の数。 0 は点を打たない (2026-11-18 から使う所は無い・出目パーツの段だった)。</summary>
        public int pips;
        /// <summary>消耗品か (「使う」のボタンを出す)。</summary>
        public bool usable;

        public static string RarityName(int r)
            => r == 0 ? "銅" : r == 1 ? "銀" : r == 2 ? "金" : r == 3 ? "伝説" : null;

        /// <summary>武器の家系の字 (items.json の family を読む・id の綴りは読まない)。</summary>
        public static string FamilyName(string f)
            => f == "sword" ? "剣" : f == "axe" ? "斧" : f == "dagger" ? "短剣" : f == "shield" ? "盾" : string.IsNullOrEmpty(f) ? null : f;

        /// <summary>
        /// アイコンの無い品の仮の字: 品名の頭から、 幅 <paramref name="maxW"/> に入るだけの字 (漢字なら 1 字・半角なら 2 字)。 最低 1 字。
        /// 升目の内側 (16 dot) に収まる幅を渡す。
        /// </summary>
        public static string Glyph(string name, int maxW, Func<char, int> advance)
        {
            string s = (name ?? "").TrimStart('〈', '［', '[', ' ', '　');
            if (s.Length == 0) return "?";
            int w = advance(s[0]), n = 1;
            while (n < 2 && n < s.Length && s[n] != ' ' && s[n] != '　' && w + advance(s[n]) <= maxW) { w += advance(s[n]); n++; }
            return s.Substring(0, n);
        }

        /// <summary>升目の右下に出す数 (2 以上の時だけ)。 幅が升目の内側に収まらない数 (今のドット字で 3 桁以上) は null ── 詳細の欄に「個数」で出る。</summary>
        public static string CountLabel(int count, int cellW, Func<char, int> advance)
        {
            if (count < 2) return null;
            string s = count.ToString();
            int w = 0; foreach (var ch in s) w += advance(ch);
            return w <= cellW ? s : null;
        }

        /// <summary>items.json の品から作る (画面は <see cref="ItemCards.Of"/>・検算は items.json を読んで同じ関数を呼ぶ)。
        /// category = items.json の category (Passive / Weapon / Consumable)。</summary>
        public static ItemCardInfo ForItem(string id, string name, string category, int rarity, string description, string flavor,
                                           string axis, string family, int tier, string sell, string mark, int count, Func<char, int> advance)
        {
            var c = new ItemCardInfo
            {
                key = id, iconId = id, name = string.IsNullOrEmpty(name) ? id : name, rarity = rarity,
                description = description, flavor = flavor, axis = string.IsNullOrEmpty(axis) ? null : axis,
                family = FamilyName(family), tier = tier, sell = sell, mark = mark, count = Math.Max(1, count),
            };
            switch (category)
            {
                case "Weapon": c.kind = Kind.Weapon; c.kindLabel = "武器"; break;
                case "Consumable": c.kind = Kind.Consumable; c.kindLabel = "消耗品"; c.usable = true; break;
                default: c.kind = Kind.Relic; c.kindLabel = "秘宝"; break;
            }
            c.glyph = Glyph(c.name, InventoryLayout.CellInner - 2, advance);
            return c;
        }

        // ── 詳細の欄の文 ──

        /// <summary>詳細の欄の 1 行 (折り返し済み)。 role = 0 品名 / 1 種類・レア度などの付記 / 2 効果の説明文 / 3 フレーバー文 / 4 印 (手放せない・使えない理由)。 空の行は区切り。</summary>
        public struct DetailLine { public string text; public int role; }

        /// <summary>付記の行 (種類・レア度・軸・家系・段・個数・売値)。 字の並びはここだけで決める。</summary>
        public List<string> MetaParts()
        {
            var l = new List<string>();
            string head = kindLabel ?? KindNames[(int)kind];
            string rn = RarityName(rarity);
            l.Add(rn != null ? head + "・" + rn : head);
            if (axis != null) l.Add("軸 " + axis);
            if (family != null) l.Add("家系 " + family);
            if (tier > 0) l.Add("段 " + tier);
            if (count >= 2) l.Add("個数 " + count);
            if (sell != null) l.Add("売値 " + sell);
            return l;
        }

        /// <summary>
        /// 詳細の欄の行: 品名 → 付記 → (印) → 空行 → 効果の説明文 → 空行 → フレーバー文。 幅 <paramref name="width"/> で折り返す
        /// (<see cref="JpTextLayout.Lines"/>・イベントの窓と同じ折り返し)。 <b>どの字も捨てない</b>。
        /// 「／」は区切りの頁送り (<see cref="JpTextLayout.PageBreak"/>) ではなく字として出す ── 説明文が「会心率+10%／防御+1」のように使っている。
        /// </summary>
        public List<DetailLine> DetailLines(int width, Func<char, int> advance)
        {
            var l = new List<DetailLine>();
            void Add(string s, int role) { foreach (var x in JpTextLayout.Lines(s, width, advance)) l.Add(new DetailLine { text = x, role = role }); }
            void Blank() { if (l.Count > 0 && l[l.Count - 1].text.Length > 0) l.Add(new DetailLine { text = "", role = 1 }); }
            Add(name, 0);
            // 付記は項目 (「売値 2〜3G」など) の区切りで折り返す ── 字の途中で切ると単位だけが次の行へこぼれる (2026-11-13)。 1 項目で幅を超える時だけ字で折る
            string cur = "";
            foreach (var part in MetaParts())
            {
                string cand = cur.Length == 0 ? part : cur + "  " + part;
                if (Width(cand, advance) <= width) { cur = cand; continue; }
                if (cur.Length > 0) l.Add(new DetailLine { text = cur, role = 1 });
                if (Width(part, advance) <= width) cur = part; else { Add(part, 1); cur = ""; }
            }
            if (cur.Length > 0) l.Add(new DetailLine { text = cur, role = 1 });
            if (mark != null) Add(mark, 4);
            if (block != null) Add("今は使えない: " + block, 4);
            if (!string.IsNullOrEmpty(description)) { Blank(); Add(description, 2); }
            var kw = KeywordGlossary.In(description);
            foreach (var k in kw) { Blank(); Add(k, 1); }   // 言葉ごとに空行で区切る (頁は空行で割る・1 塊が頁を超えないように)
            if (!string.IsNullOrEmpty(flavor)) { Blank(); Add(flavor, 3); }
            return l;
        }

        private static int Width(string s, Func<char, int> advance) { int w = 0; foreach (var ch in s) w += advance(ch); return w; }

        /// <summary>ホバーの吹き出しの行: 品名 → 効果の説明文 (フレーバー文は出さない)。 幅 <paramref name="width"/> で折り返す。</summary>
        public List<DetailLine> TooltipLines(int width, Func<char, int> advance)
        {
            var l = new List<DetailLine>();
            foreach (var x in JpTextLayout.Lines(count >= 2 ? name + " ×" + count : name, width, advance)) l.Add(new DetailLine { text = x, role = 0 });
            if (!string.IsNullOrEmpty(description))
                foreach (var x in JpTextLayout.Lines(description, width, advance)) l.Add(new DetailLine { text = x, role = 2 });
            return l;
        }
    }
}

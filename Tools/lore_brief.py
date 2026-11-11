"""外部の書き手 (Gemini など) に渡す「世界観の資料」と「フレーバー文の入力」を書き出す。

使い方: python Tools/lore_brief.py [出力フォルダ (既定: Captures/lore_brief)]
  - lore_brief.md        依頼の決まり・文体の指針・画中画の原則・軸の用語・世界観 (GAME.md §19 をそのまま)・七つの大罪のフレーバー
  - items_flavor_input.json  items.json の全品 (id・種類・レア度・家系・段・軸・効果の説明・今のフレーバー)

**正本は GAME.md §19 と PermanentDebuffIds.cs と items.json のまま。** ここで書き出す物は写し (生成物) なので、
直したい所は正本を直して書き出し直す (この写しを手で直さない・コミットしない)。
"""
import io, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Captures", "lore_brief")


def read(p):
    return io.open(os.path.join(ROOT, p), encoding="utf-8-sig").read()


def lore_section():
    s = read("docs/GAME.md")
    a = s.index("\n## 19. 世界観")
    b = s.index("\n## 20. ", a)
    return s[a:b].strip()


def sins():
    src = read("Assets/Scripts/MetaProgression/PermanentDebuffIds.cs")
    names = dict(re.findall(r'public const string (\w+)\s*=\s*"([^"]+)"', src))
    body = src[src.index("flavorTexts"):]
    out = []
    for key, text in re.findall(r'\{\s*(\w+),\s*@"(.*?)"\s*\}', body, re.S):
        out.append((names.get(key, key), text.replace('""', '"').strip()))
    return out


def axes():
    src = read("Assets/Scripts/ItemSystem/PassiveSkills/AxisRules.cs")
    m = re.search(r"public const string (.+?);", src)
    consts = re.findall(r'(\w+)\s*=\s*"([^"]+)"', m.group(1))
    depth = dict(re.findall(r'(\w+)\s*=>\s*"([^"]+)"', src))
    return [(k, v, depth.get(k, "")) for k, v in consts]


BRIEF_HEAD = """# 世界観の資料 (外部の書き手向け)

このゲームのアイテムのフレーバーテキストを、**世界観に合う範囲で校正・書き換え**てほしい。

## 依頼の決まり

- 書き換えるのは **flavorText だけ**。description (効果の説明) は変えない。品の名前 (id / name) も変えない。
- 入力は `items_flavor_input.json`。出力は **`[{"id": "...", "flavorText": "..."}]` の JSON の配列**で、書き換えた品だけを返す。
  変えない品は返さなくてよい。直した理由を一言添えたい時は `"note"` を足してよい。
- 長さは今のフレーバーと同じくらい (1〜3 文・多くても 120 字前後)。画面は小さな液晶なので、長すぎる文は読まれない。
- 表記: 和文の中にスペースを入れない・リーダーは「……」・ダッシュは「——」・数字は漢数字。
- 使える字に制限がある (ドット字のフォント)。常用漢字の範囲なら概ね大丈夫。珍しい字は避ける。

## 文体の指針 (作者と合意した物)

- **いちばん大事なのは、話法を散らすこと。** 「皮肉な観察者が最後に切り返す」一本槍にしない。引用だけ (書付・祈祷・報告書・品書き)、
  感情の無い報告、温かい文、ただ怖い文などに声を分ける。カタログとして続けて読んだ時の単調さが最大の敵。
- **定型句を使い回さない。** 「それで十分だ」「と古参は言う」「深層を知らない」「ギルド鑑定所は〜と書いて終わりにした」などは、全体で 1〜3 回まで。
- **ゲームの仕組みの言葉を世界に持ち込まない。** ターン・ラン・パッシブ・ダンジョン・HP などは使わない。世界の中の言い方に換える
  (ターン →「拍」、ラン →「大穴を出る日」、ダンジョン →「大穴」「深層」)。
- **家系** (同じ family の段 1〜4 / 等級 I〜IV) は、4 幕の物語として繋がると強い。上の段が「同じ話を大声で言い直す」だけなのは失格。
- 固有名詞 (地名・組織・人物) は下の世界観にある物だけを使う。新しい固有名詞を足す時は控えめに、note に書く。

## 画中画の原則 (必ず守る)

このゲームは「卓上の筐体の中のチップが動かしている物語」という額縁を持つ。**物語の世界は液晶の中だけ**で、
筐体・液晶の外の物 (筐体・端子・充電の機構・計器盤・USB・電源・ダイスの機械) は、世界の中には存在しない。
フレーバーにそれらを出さない。逆に、世界の固有名詞を筐体の側に使うこともない。

## 軸の用語

品の多くは「軸」に属する。軸の品を持っている数には、世界の中の呼び名がある。

| 内部名 (items.json の axis) | 深さの呼び名 |
|---|---|
"""


def main():
    os.makedirs(OUT, exist_ok=True)
    parts = [BRIEF_HEAD]
    for k, v, d in axes():
        parts.append("| %s | %s |\n" % (v, d or "(呼び名なし)"))
    parts.append("\n---\n\n# 世界観 (正本: docs/GAME.md §19 の写し)\n\n")
    parts.append(lore_section().replace("\n## 19.", "\n## 19.", 1))
    parts.append("\n\n---\n\n# 七つの大罪のフレーバー (正本: PermanentDebuffIds.cs の写し)\n\n")
    for name, text in sins():
        parts.append("## %s\n\n%s\n\n" % (name, text))
    io.open(os.path.join(OUT, "lore_brief.md"), "w", encoding="utf-8").write("".join(parts))

    d = json.loads(read("Assets/Data/InventorySystem/items.json"))
    items = d["items"] if isinstance(d, dict) and "items" in d else d
    keep = ["id", "category", "rarity", "family", "tier", "axis", "description", "flavorText"]
    rows = [{k: it.get(k) for k in keep if it.get(k) not in (None, "")} for it in items]
    io.open(os.path.join(OUT, "items_flavor_input.json"), "w", encoding="utf-8").write(json.dumps(rows, ensure_ascii=False, indent=1))
    print("書いた:", os.path.join(OUT, "lore_brief.md"), "/", os.path.join(OUT, "items_flavor_input.json"), "(%d 品・大罪 %d)" % (len(rows), len(sins())))


if __name__ == "__main__":
    main()

"""外部の書き手 (Gemini など) に渡す「世界観の資料」と「フレーバー文の入力」を書き出す。

使い方: python Tools/lore_brief.py [出力フォルダ (既定: Captures/lore_brief)]
  - lore_brief.md        依頼の決まり・文体の指針・画中画の原則・軸ごとの題材の向き・世界観 (GAME.md §19 をそのまま)
  - items_flavor_input.json  items.json の全品 (id・種類・レア度・家系・段・軸・効果の説明・今のフレーバー)

**正本は GAME.md §19 と items.json のまま。** ここで書き出す物は写し (生成物) なので、
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


BRIEF_HEAD = """# 世界観の資料 (外部の書き手向け)

このゲームのアイテムのフレーバーテキストを、**世界観に合う範囲で校正・書き換え**てほしい。

## 依頼の決まり

- 書き換えるのは **flavorText だけ**。description (効果の説明) は変えない。
- **品の名前 (id) も判断の材料に入れる。** 名前・効果・軸・家系 (family と tier)・レア度を合わせて読み、品として一つの像になるように書く
  (名前が示す物と、効果と、文が別々の物を語っていないか)。名前は内部の ID を兼ねているので直接は変えない。
  名前そのものが世界観や題材の向きと合わない時は、`"rename"` に新しい名前の案と理由を添えて提案する。
- 入力は `items_flavor_input.json`。出力は **`[{"id": "...", "flavorText": "..."}]` の JSON の配列**で、書き換えた品だけを返す。
  変えない品は返さなくてよい。直した理由を一言添えたい時は `"note"` を、名前の変更を提案する時は `"rename"` を足す。
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

## 軸ごとの題材の向き

品の多くは「軸」に属する (items.json の `axis`)。下は作者が決めた**方向性の目安の 1 つ**で、強く縛る物ではない
(軸に固執しすぎて全部が同じ話になるのは避ける)。

| 軸 (items.json の axis) | 題材の向き |
|---|---|
| 会心 | 剣豪や暗殺者など、その道を究めた人間。目・見切りにまつわる品 |
| 出血 | 戦いや血に呑まれる、人ならざる力に頼る・与える |
| 毒 | 瘴気・毒殺などの後ろ暗い手段。伝聞や噂の語りが強い |
| 臨界 | 炉に関連するもの |
| 充電 | 工廠。小さな工廠での会話のようなフレーバー |
| シールド | 国を守る兵隊、大切なものを守ろうとする人間や出来事 |
| 金 | 金、富や利益 |
| (軸なし) | 自由 (大穴・遺物・探検家の暮らし全般) |
"""


def main():
    os.makedirs(OUT, exist_ok=True)
    parts = [BRIEF_HEAD]
    parts.append("\n---\n\n# 世界観 (正本: docs/GAME.md §19 の写し)\n\n")
    parts.append(lore_section())
    io.open(os.path.join(OUT, "lore_brief.md"), "w", encoding="utf-8").write("".join(parts))

    d = json.loads(read("Assets/Data/InventorySystem/items.json"))
    items = d["items"] if isinstance(d, dict) and "items" in d else d
    keep = ["id", "category", "rarity", "family", "tier", "axis", "description", "flavorText"]
    rows = [{k: it.get(k) for k in keep if it.get(k) not in (None, "")} for it in items]
    io.open(os.path.join(OUT, "items_flavor_input.json"), "w", encoding="utf-8").write(json.dumps(rows, ensure_ascii=False, indent=1))
    print("書いた:", os.path.join(OUT, "lore_brief.md"), "/", os.path.join(OUT, "items_flavor_input.json"), "(%d 品)" % len(rows))


if __name__ == "__main__":
    main()

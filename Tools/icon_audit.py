#!/usr/bin/env python3
"""アイテムアイコンと items.json を 1:1 で突き合わせる。 Unity 不要。

規約 (2026-09-14 制定):
    Assets/Resources/Icons/Items/<表示名>.png
    ファイル名 = items.json の name。 それ以外の対応表は持たない。
    解像度は カテゴリ Weapon = 32x32 / それ以外 = 16x16 (PPU=32)。

**id = 表示名 (2026-09-22〜)。** ファイル名・id・表示名・パッシブ名がすべて同じ文字列。

    2026-09-14 の時点では「id を日本語へ寄せる案は採らなかった」── id に構造が乗っていた
    (`sword_t2` を家系+Tier として切る / `cons_heal_1` を prefix+tier で組む / `uniq_*` で昇華可否)。
    2026-09-22 に**構造を items.json の明示フィールド (family / tier / consFamily / unique) へ出し**、
    id を綴りで切る箇所をゼロにしてから統一した。 旧 id は ItemDatabase.LegacyIdMap が解決する。

    **表示名を変えたら、 png のファイル名も変える。** このスクリプトが「欠落」「余分」として
    必ず検出するので、 黙って壊れることはない。

使い方:
    python Tools/icon_audit.py          # 一覧
    python Tools/icon_audit.py --todo   # 未着手だけ (作業指示に貼れる形)
"""
import io
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ITEMS = os.path.join(ROOT, "Assets", "Data", "InventorySystem", "items.json")
ICONS = os.path.join(ROOT, "Assets", "Resources", "Icons", "Items")

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass


def expected_px(category):
    """武器だけ 32x32。 他は 16x16。"""
    return 32 if category == "Weapon" else 16


def load_items():
    d = json.load(io.open(ITEMS, encoding="utf-8"))
    return d["items"] if isinstance(d, dict) and "items" in d else d


def is_blank(path):
    """完全透明か (= プレースホルダのまま = 未着手)。

    Pillow が無ければ None を返す ── **「分からない」を「着手済み」に
    寄せない**。 数えられないものを数えたことにすると進捗が嘘になる。
    """
    try:
        from PIL import Image
    except ImportError:
        return None
    try:
        with Image.open(path) as im:
            im = im.convert("RGBA")
            return im.getextrema()[3][1] == 0   # アルファの最大値が 0
    except Exception:
        return None


def main():
    todo_only = "--todo" in sys.argv
    items = load_items()
    # **キーは表示名**。 ファイル名 = items.json の name という規約そのもの。
    by_name = {it.get("name", ""): it for it in items if it.get("name")}

    have = set()
    if os.path.isdir(ICONS):
        have = {f[:-4] for f in os.listdir(ICONS) if f.lower().endswith(".png")}

    missing = sorted(set(by_name) - have)        # json にあるが png が無い
    orphan = sorted(have - set(by_name))         # png はあるが json に無い
    wrong_size, blank, done, unknown = [], [], [], []

    for nm in sorted(set(by_name) & have):
        p = os.path.join(ICONS, nm + ".png")
        want = expected_px(by_name[nm].get("category", ""))
        try:
            from PIL import Image
            with Image.open(p) as im:
                if im.size != (want, want):
                    wrong_size.append((nm, im.size, want))
        except ImportError:
            pass
        except Exception:
            wrong_size.append((nm, "読めない", want))
        b = is_blank(p)
        (blank if b is True else done if b is False else unknown).append(nm)

    if todo_only:
        for nm in sorted(set(missing) | set(blank)):
            it = by_name.get(nm, {})
            print(f"{nm}\t{it.get('category','')}\t{expected_px(it.get('category',''))}px"
                  f"\t{it.get('rarity','')}\tid={it.get('id','')}")
        return

    n = len(by_name)
    print(f"=== アイコン突き合わせ  items.json {n} 件 ===")
    print(f"  完成       {len(done):>4}  ({100*len(done)/max(1,n):.1f}%)")
    print(f"  未着手     {len(blank):>4}  (完全透明のまま)")
    print(f"  png 欠落   {len(missing):>4}")
    print(f"  余分な png {len(orphan):>4}  (json に表示名が無い)")
    print(f"  解像度違い {len(wrong_size):>4}")
    if unknown:
        print(f"  判定不能   {len(unknown):>4}  (Pillow 未導入 or 読めない)")

    if missing:
        print("\n[png 欠落] items.json にあるのにファイルが無い")
        for nm in missing:
            it = by_name[nm]
            print(f"   {nm:<22} id={it.get('id','')}  ({expected_px(it.get('category',''))}px)")
    if orphan:
        print("\n[余分な png] items.json に id が無い ── 改名漏れか、 削除されたアイテム")
        for nm in orphan:
            print(f"   {nm}")
    if wrong_size:
        print("\n[解像度違い] Weapon=32x32 / その他=16x16")
        for nm, got, want in wrong_size:
            print(f"   {nm:<26} {got} → {want}x{want} であるべき")

    if not (missing or orphan or wrong_size):
        print("\n  1:1 対応に破れなし。")
    print("\n※ 未着手の一覧は --todo で。 そのまま作業指示に貼れる形 (表示名/カテゴリ/px/レア/id) で出る。")


if __name__ == "__main__":
    main()

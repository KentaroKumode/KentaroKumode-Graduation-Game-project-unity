"""イベント定義テキストを読める Markdown に起こす。

`Assets/Resources/Events/*.txt` は 1 イベント 1 行で、フレーバーも選択肢も
効果も全部コロンとハイフンで詰め込んであるため、そのままでは校正できない。
文面を読み返したいときにこれで起こす。

    python Tools/render_events.py                        # NPC 分を docs/ へ
    python Tools/render_events.py --src event_list       # 本体側
    python Tools/render_events.py --filter さびれた観測所  # 名前で絞る

**生成物には必ず再生成コマンドを書き込む。** docs/items_catalog.md が
「自動生成」と書いてあるのに生成元がリポジトリに無い状態になっており
(docs/GAME.md §23-6)、同じことを繰り返さないため。

フォーマット (event_list.txt の冒頭コメントが正本):
    イベント名:出現条件:フレーバー:選択肢-結果-選択後フレ@@年代記/...
"""
import argparse
import io
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def parse(path, name_filter=None):
    events = []
    for raw in io.open(path, encoding='utf-8'):
        line = raw.rstrip('\n')
        if not line or line.startswith('#') or line.startswith('==='):
            continue
        parts = line.split(':', 3)
        if len(parts) < 4:
            continue
        name, cond, flavor, rest = parts
        if name_filter and name_filter not in name:
            continue
        choices = []
        for ch in rest.split('/'):
            f = ch.split('-')
            if len(f) < 3:
                continue
            after = '-'.join(f[2:]).split('@@')
            choices.append({
                'label': f[0],
                'effects': f[1],
                'after': after[0],
                'chronicle': after[1] if len(after) > 1 else '',
            })
        events.append({'name': name, 'cond': cond, 'flavor': flavor, 'choices': choices})
    return events


def render(events, src, out):
    w = out.write
    w('# イベント文面（読み上げ用・自動生成）\n\n')
    w('**このファイルは生成物。直接編集しても次の生成で消える。**\n')
    w('正本は `Assets/Resources/Events/%s.txt`。\n\n' % src)
    w('再生成:\n\n```\npython Tools/render_events.py --src %s\n```\n\n' % src)
    w('---\n\n')
    w('## 目次\n\n')
    for e in events:
        w('- %s（選択肢 %d）\n' % (e['name'], len(e['choices'])))
    w('\n---\n')
    for e in events:
        w('\n\n## %s\n\n' % e['name'])
        w('`%s`\n\n' % e['cond'])
        for seg in e['flavor'].split('／'):
            seg = seg.strip()
            if seg:
                w('　%s\n\n' % seg)
        for c in e['choices']:
            w('**▸ %s**\n\n' % c['label'])
            w('　　`%s`\n\n' % c['effects'])
            w('　　%s\n\n' % c['after'])
            if c['chronicle']:
                w('　　*年代記: %s*\n\n' % c['chronicle'])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', default='event_list_npc',
                    help='Assets/Resources/Events/ 以下のファイル名（拡張子なし）')
    ap.add_argument('--filter', default=None, help='イベント名にこの文字列を含むものだけ')
    ap.add_argument('--out', default=None, help='出力先（既定 docs/events-<src>.md）')
    a = ap.parse_args()

    src = os.path.join(ROOT, 'Assets', 'Resources', 'Events', a.src + '.txt')
    if not os.path.exists(src):
        sys.exit('見つかりません: ' + src)

    events = parse(src, a.filter)
    out_path = a.out or os.path.join(ROOT, 'docs', 'events-%s.md' % a.src.replace('_', '-'))
    with io.open(out_path, 'w', encoding='utf-8', newline='\n') as f:
        render(events, a.src, f)
    print('%d イベント → %s' % (len(events), os.path.relpath(out_path, ROOT)))


if __name__ == '__main__':
    main()

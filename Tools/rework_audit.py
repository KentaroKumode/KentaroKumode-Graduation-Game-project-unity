#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""フルリワークの削除の検証ゲート (spec §7-2 の 6・2026-11-21)。 Unity を起動せずに静的に数える。

    python -X utf8 Tools/rework_audit.py [--warn-baseline N] [--list]

出す項目 (どれも 0 が合格・警告は基準以下が合格):
  1. 旧世代ファイルの残存       Effects/ の下の旧ファイル名
  2. 孤立スキル                 items.json の skills 表のうち、 どの品の skills にも無い物
  3. 品が指す未実装のスキル     品の skill / skills が、 効果クラスの SkillId にも stats にも無い
  4. 未参照の効果クラス         Effects/ の SkillId を持つクラスで `new X(` がどこにも無い物
  5. 実装欠落                   説明文に効果がある秘宝・賽・武器で、 スキル・stats・本体のコードの参照 (id の文字列か定数) が全部無い品
  6. 未使用の ItemIds / DieIds  定数の名前が宣言の外で 1 回も出ない物・値が items.json に無い物
  7. コンパイル警告              Tools/typecheck.py の rsp を -nowarn なしで回した警告の数 (Unity と同じ既定の警告)

SkillId の静的な読み取りは `SkillId => "..."` と `SkillId => ItemIds.X` と段の配列 `Ids = { "...", ... }` だけ。
読めない形の SkillId を足したら、 ここも足すこと。
"""
import io, json, os, re, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EFFECTS = 'Assets/Scripts/ItemSystem/PassiveSkills/Effects'
OLD_FILES = ['CrossItemEffects.cs', 'AxisItemEffects.cs', 'AxisItemEffects2.cs', 'AxisItemEffects3.cs',
             'ReworkItemEffects.cs', 'DeckItemEffects.cs', 'RoleItemEffects.cs', 'TacticItemEffects.cs',
             'ConsumableLinkEffects.cs', 'MacroItemEffects.cs', 'AllPassiveSkillEffects.cs']
# 敵・ヴェスカのスキルは敵のデータが指す (品ではない)。 未参照の効果クラスの判定だけ見る
ENEMY_FILES = {'EnemyPassiveSkillEffects.cs', 'VescaEffects.cs'}
NO_EFFECT = re.compile(r'^\s*効果なし')
# ItemIds にあるが品ではない物 (イベントのフラグ・event_list.txt の [] の表記)
FLAG_IDS = {'苦難の予言', '苦難の確信'}


def read(p):
    return io.open(os.path.join(ROOT, p), encoding='utf-8-sig').read()


def cs_files(sub='Assets'):
    out = []
    for r, _d, fs in os.walk(os.path.join(ROOT, sub)):
        for f in fs:
            if f.endswith('.cs'):
                out.append(os.path.relpath(os.path.join(r, f), ROOT).replace('\\', '/'))
    return sorted(out)


def strip_comments(s):
    s = re.sub(r'/\*.*?\*/', '', s, flags=re.S)
    return re.sub(r'//[^\n]*', '', s)


def is_body(p):
    """本体のコード (BOT・エディタ・テストは実装に数えない)。"""
    return '/AutoTest/' not in p and '/Editor/' not in p


def main():
    args = sys.argv[1:]
    listing = '--list' in args
    warn_base = None
    if '--warn-baseline' in args:
        warn_base = int(args[args.index('--warn-baseline') + 1])
    db = json.loads(read('Assets/Data/InventorySystem/items.json'))
    items = db['items']
    ids = {i['id'] for i in items}
    skill_table = {s['id'] for s in db.get('skills', [])}

    files = cs_files()
    src = {p: strip_comments(read(p)) for p in files}

    # 定数 (const string A = "x", B = "y") を全部集める
    const_val = {}
    decl_re = re.compile(r'const\s+string\s+([^;]+);')
    pair_re = re.compile(r'(\w+)\s*=\s*"([^"]*)"')
    for p, s in src.items():
        for m in decl_re.finditer(s):
            for n, v in pair_re.findall(m.group(1)):
                const_val.setdefault(n, set()).add(v)
    # 定数の参照 = 宣言の外で名前が出る
    all_body = '\n'.join(s for p, s in src.items() if is_body(p))
    all_code = '\n'.join(src.values())
    decl_free_body = decl_re.sub('', all_body)
    decl_free_all = decl_re.sub('', all_code)

    def used(name, text):
        return re.search(r'(?<![\w])' + re.escape(name) + r'(?![\w])', text) is not None

    body_lits = set(re.findall(r'"([^"\n]*)"', decl_free_body))
    body_const_vals = {v for n, vs in const_val.items() if used(n, decl_free_body) for v in vs}

    # 効果クラスの SkillId
    eff_files = [p for p in files if p.startswith(EFFECTS + '/')]
    class_re = re.compile(r'(?:public|internal|sealed|\s)*class\s+(\w+)[^{]*\{')
    skill_ids = set()
    classes = []  # (file, class, skillid or None)
    for p in eff_files:
        s = src[p]
        starts = [(m.start(), m.group(1)) for m in class_re.finditer(s)]
        for k, (pos, name) in enumerate(starts):
            body = s[pos:starts[k + 1][0] if k + 1 < len(starts) else len(s)]
            if 'SkillId' not in body or re.search(r'\babstract\s+class\s+' + name, s[max(0, pos - 40):pos + 40]):
                continue
            sid = []
            m = re.search(r'SkillId\s*=>\s*"([^"]+)"', body)
            if m: sid.append(m.group(1))
            m = re.search(r'SkillId\s*=>\s*(?:GameLoop\.)?(?:ItemIds|DieIds)\.(\w+)', body)
            if m and m.group(1) in const_val: sid += sorted(const_val[m.group(1)])
            m = re.search(r'SkillId\s*=>\s*(\w+)\s*;', body)
            if m:
                local = re.search(r'const\s+string\s+' + m.group(1) + r'\s*=\s*"([^"]+)"', body)
                if local: sid.append(local.group(1))
            m = re.search(r'Ids\s*=\s*(?:new\s*\w*\[\]\s*)?\{([^}]*)\}', body)
            if m: sid += re.findall(r'"([^"]+)"', m.group(1))
            m = re.search(r'string\s+SkillId\s*\{\s*get[^"]*"([^"]+)"', body)
            if m: sid.append(m.group(1))
            if not sid and re.search(r'SkillId\s*=>\s*(_\w+|id|Id)\b', body): sid = None  # 引数で受け取る (StatModifierEffect・敵の汎用)
            classes.append((p, name, sid))
            for x in sid or []: skill_ids.add(x)
    stat_skills = {i['id'] for i in items if i.get('stats')}

    report = {}
    # 1
    report['旧世代ファイルの残存'] = [f for f in OLD_FILES if os.path.exists(os.path.join(ROOT, EFFECTS, f))
                                    or os.path.exists(os.path.join(ROOT, EFFECTS, f + '.meta'))]
    # 2
    ref_skills = set()
    for i in items:
        for s in i.get('skills') or []: ref_skills.add(s)
    report['孤立スキル (skills 表)'] = sorted(skill_table - ref_skills)
    # 3
    missing = []
    for i in items:
        names = ([i['skill']] if isinstance(i.get('skill'), str) else []) + list(i.get('skills') or [])
        for n in names:
            if n not in skill_ids and n not in stat_skills:
                missing.append(f"{i['id']} → {n}")
    report['品が指す未実装のスキル'] = missing
    # 4
    alltext_new = decl_free_all
    unref = []
    for p, name, sid in classes:
        if not re.search(r'new\s+(?:[\w.]+\.)?' + name + r'\s*\(', alltext_new):
            unref.append(f'{os.path.basename(p)}:{name}')
        elif sid and os.path.basename(p) not in ENEMY_FILES and not any(
                x in ref_skills or x in ids for x in sid):
            unref.append(f'{os.path.basename(p)}:{name} (SkillId {sid} を持つ品が無い)')
    report['未参照の効果クラス'] = unref
    # 5
    gap = []
    for i in items:
        if i['category'] not in ('Passive', 'Dice', 'Weapon'): continue
        if NO_EFFECT.match(i.get('description') or ''): continue
        names = ([i['skill']] if isinstance(i.get('skill'), str) else []) + list(i.get('skills') or [])
        has_skill = any(n in skill_ids or n in stat_skills for n in names)
        if i['category'] == 'Weapon':
            ok = has_skill
        else:
            ok = has_skill or i.get('stats') or i['id'] in body_lits or i['id'] in body_const_vals
        if not ok:
            gap.append(f"{i['category']}:{i['id']}")
    report['実装欠落 (説明文あり・効果なし)'] = gap
    # 6
    ids_src = src['Assets/Scripts/GameLoop/Ids.cs']
    unused = []
    for cls in ('ItemIds', 'DieIds'):
        m = re.search(r'class\s+' + cls + r'\b', ids_src)
        if not m: continue
        # クラスの中括弧の範囲
        i0 = ids_src.index('{', m.end()); depth = 0; j = i0
        while True:
            if ids_src[j] == '{': depth += 1
            elif ids_src[j] == '}':
                depth -= 1
                if depth == 0: break
            j += 1
        block = ids_src[i0:j]
        for dm in decl_re.finditer(block):
            for n, v in pair_re.findall(dm.group(1)):
                if not used(n, decl_free_all):
                    unused.append(f'{cls}.{n} = "{v}" (参照なし)')
                elif re.search(r'[\u3040-\u30ff\u4e00-\u9fff]', v) and v not in ids and v not in FLAG_IDS and 'Family' not in n:
                    unused.append(f'{cls}.{n} = "{v}" (items.json に無い・参照 {len(re.findall(chr(92)+"b"+n+chr(92)+"b", decl_free_all))})')
    report['未使用・不一致の ItemIds / DieIds'] = unused

    # 7 警告
    warns = None
    if '--no-warn' not in args:
        warns = count_warnings()

    sys.stdout.reconfigure(encoding='utf-8')
    total = 0
    for k, v in report.items():
        print(f'{k}: {len(v)}')
        total += len(v)
        if v and (listing or len(v) <= 40):
            for x in v: print('    ' + x)
    counts = {}
    for i in items:
        counts[i['category']] = counts.get(i['category'], 0) + 1
    print(f"品の数: 秘宝 {counts.get('Passive', 0)} / 賽 {counts.get('Dice', 0)} / 武器 {counts.get('Weapon', 0)}")
    if warns is not None:
        print(f'コンパイル警告: {warns}' + (f' (基準 {warn_base})' if warn_base is not None else ''))
        if warn_base is not None and warns > warn_base: total += 1
    print('合格' if total == 0 else f'不合格 ({total})')
    sys.exit(0 if total == 0 else 1)


def count_warnings():
    """typecheck の rsp を -nowarn なしで回す (先に Tools/typecheck.py を 1 回回して rsp を作っておく)。"""
    unity = r'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Data'
    dotnet, csc = unity + '/NetCoreRuntime/dotnet.exe', unity + '/DotNetSdkRoslyn/csc.dll'
    n = 0
    for name in ('Assembly-CSharp', 'Assembly-CSharp-Editor'):
        rsp = os.path.join(ROOT, 'obj', name + '.rsp')
        if not os.path.exists(rsp) or not os.path.exists(csc): return None
        lines = [l for l in io.open(rsp, encoding='utf-8').read().splitlines()
                 if not l.startswith('-nowarn') and not l.startswith('-out:')]
        tmp = os.path.join(ROOT, 'obj', name + '.audit.rsp')
        io.open(tmp, 'w', encoding='utf-8').write('\n'.join(lines + ['-out:obj/audit_%s.dll' % name]) + '\n')
        p = subprocess.run([dotnet, csc, '@' + tmp], capture_output=True, text=True, errors='replace', cwd=ROOT)
        n += len({l for l in (p.stdout + p.stderr).splitlines() if 'warning CS' in l})
    return n


if __name__ == '__main__':
    main()

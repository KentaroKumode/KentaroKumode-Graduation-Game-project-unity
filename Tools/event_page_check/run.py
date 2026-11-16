"""イベントの画面の頁割りと配置 (2026-11-13・2026-11-16 に全画面の配置へ・docs/GAME.md §17「イベントの画面」) を Unity なしで検算する。

画面と同じ関数 ── UnityEngine 非依存の JpTextLayout.cs (折り返し・頁割り)・EventLayout.cs (画面の寸法の式)・
EventPaging.cs (イベントの頁割り) ── と、 イベントの解析 (EventParser.cs ほか・Unity の型は
Tools/event_branch_check/Stubs.cs の代役) を Unity 同梱の Roslyn (csc) でコンパイルし、 システムの .NET Core 3.1
ランタイムで実行する (作りは Tools/event_branch_check/run.py と同じ)。 字の送り幅は焼いたドット字
(GameFont.cs の DotPath の .bytes) を読む。 検算の中身は Program.cs:
  「切り捨て 0・本文が板に収まる・選択肢の列 (最多の箱 + 説明の板) が画面に収まる・説明が 2 行に収まる」と、
  名前が札と当たって「…」で切れる選択肢の一覧と、 背景の絵の対応表 (全イベントに対応がある・表の名前が実在する・絵が全部ある)。
最後の行が `ALL OK` なら全部通っている。

使い方 (リポジトリのルートで): python Tools/event_page_check/run.py [導入の頁数の一覧の書き出し先]
"""
import glob
import os
import subprocess
import sys

BS = chr(92)
UNITY = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Data'
DOTNET_UNITY = UNITY + '/NetCoreRuntime/dotnet.exe'
CSC = UNITY + '/DotNetSdkRoslyn/csc.dll'
RUNTIME = 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App'
EV = 'Assets/Scripts/EventSystem/'
SOURCES = [EV + n + '.cs' for n in ('EventBranchText', 'EventEffectType', 'EventEffect', 'EventEffectRoles', 'EventChoice',
                                     'EventDefinition', 'EventCondition', 'EventParser')] + [
    'Assets/Scripts/UI/Game2D/JpTextLayout.cs', 'Assets/Scripts/UI/Game2D/LcdSafeArea.cs', 'Assets/Scripts/UI/Game2D/Screens/EventLayout.cs', 'Assets/Scripts/UI/Game2D/Screens/EventBackdropTable.cs', 'Assets/Scripts/UI/Game2D/Screens/EventPaging.cs']


def fwd(p):
    return p.replace(BS, '/')


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, 'obj')
    os.makedirs(out, exist_ok=True)
    versions = sorted(glob.glob(RUNTIME + '/3.1.*'))
    if not versions:
        sys.exit('.NET Core 3.1 のランタイムが見つかりません: ' + RUNTIME)
    rt = fwd(versions[-1])
    rsp = os.path.join(out, 'check.rsp')
    with open(rsp, 'w', encoding='utf-8') as f:
        f.write('-target:exe\n-nostdlib+\n-noconfig\n-langversion:9\n-nowarn:0169,0649,0414,0067,0162,0219,1701,1702\n')
        f.write('-out:"%s"\n' % fwd(os.path.join(out, 'check.dll')))
        for pat in ('System.*.dll', 'Microsoft.*.dll', 'mscorlib.dll', 'netstandard.dll'):
            for d in glob.glob(rt + '/' + pat):
                if 'Native' not in d:
                    f.write('-r:"%s"\n' % fwd(d))
        f.write('"%s"\n' % fwd(os.path.join(here, 'Program.cs')))
        f.write('"%s"\n' % fwd(os.path.join(here, '..', 'event_branch_check', 'Stubs.cs')))
        for s in SOURCES:
            f.write('"%s"\n' % s)
    p = subprocess.run([DOTNET_UNITY, CSC, '@' + rsp], capture_output=True, text=True, errors='replace')
    errs = [l for l in (p.stdout + p.stderr).splitlines() if 'error CS' in l]
    if errs:
        print('\n'.join(errs[:40]))
        sys.exit(1)
    with open(os.path.join(out, 'check.runtimeconfig.json'), 'w', encoding='utf-8') as f:
        f.write('{ "runtimeOptions": { "tfm": "netcoreapp3.1", "framework": { "name": "Microsoft.NETCore.App", "version": "3.1.0" } } }\n')
    # 引数 (導入の頁数の一覧の書き出し先) は標準の設定の回だけに渡す。
    # 設定の行間・字間 (2026-11-16): 既定 (行間 1・字間 0・2026-11-17〜 行間の既定は 1) と最大 (GameSettings.MaxLineGap, MaxCharGap = 3,2) の 2 回回す。 どちらも ALL OK で通る
    code = 0
    for gaps, extra in (('1,0', sys.argv[1:2]), ('3,2', [])):
        env = dict(os.environ, EVPC_GAPS=gaps)
        r = subprocess.run(['dotnet', os.path.join(out, 'check.dll'), '.'] + extra, capture_output=True, env=env)
        sys.stdout.buffer.write(r.stdout)
        sys.stdout.buffer.flush()
        code = code or r.returncode
    sys.exit(code)


if __name__ == '__main__':
    main()

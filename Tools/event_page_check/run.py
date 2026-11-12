"""イベントの窓の頁割り (2026-11-13・docs/GAME.md §17「イベントの窓」) を Unity なしで検算する。

画面と同じ関数 ── UnityEngine 非依存の JpTextLayout.cs (折り返し・頁割り)・ModalJpMetrics.cs (窓の寸法の式)・
EventPaging.cs (イベントの頁割り) ── と、 イベントの解析 (EventParser.cs ほか・Unity の型は
Tools/event_branch_check/Stubs.cs の代役) を Unity 同梱の Roslyn (csc) でコンパイルし、 システムの .NET Core 3.1
ランタイムで実行する (作りは Tools/event_branch_check/run.py と同じ)。 字の送り幅は焼いたドット字
(GameFont.cs の DotPath の .bytes) を読む。 検算の中身は Program.cs:
  直す前の窓で切り捨てられていた文・描かれていなかった選択肢の一覧 (上位 10 件) と、
  直した後の窓で「切り捨て 0・窓の高さ 266 以下・選択肢が全部描ける・名前と説明が切れない」。
最後の行が `ALL OK` なら全部通っている。

使い方 (リポジトリのルートで): python Tools/event_page_check/run.py
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
    'Assets/Scripts/UI/Game2D/JpTextLayout.cs', 'Assets/Scripts/UI/Game2D/Screens/ModalJpMetrics.cs', 'Assets/Scripts/UI/Game2D/Screens/EventPaging.cs']


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
    r = subprocess.run(['dotnet', os.path.join(out, 'check.dll'), '.'], capture_output=True)
    sys.stdout.buffer.write(r.stdout)
    sys.stdout.buffer.flush()
    sys.exit(r.returncode)


if __name__ == '__main__':
    main()

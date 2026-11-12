"""イベントの確率分岐ごとの文 (Assets/Scripts/EventSystem/EventBranchText.cs) を Unity なしで検算する。

UnityEngine 非依存の EventBranchText.cs と、 選べるかの判定 (EventEffectRoles.cs・EventChoice.cs・EventParser.cs ほか。
Unity の型は Stubs.cs の代役) を Unity 同梱の Roslyn (csc) でコンパイルし、 システムの
.NET Core 3.1 ランタイムで実行する (作りは Tools/freemap_check/run.py と同じ)。 検算の中身は Program.cs
(切り分けの規則と、 Assets/Resources/Events/*.txt の全選択肢で「区切った文の数 = 分岐の数」か・
選べない選択肢の規則と、 暗くなり得る選択肢の一覧・全部が暗くなり得るイベントが 0 件か)。
最後の行が `ALL OK` なら全部通っている。

使い方 (リポジトリのルートで): python Tools/event_branch_check/run.py
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
                                     'EventDefinition', 'EventCondition', 'EventParser')]


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
        f.write('"%s"\n' % fwd(os.path.join(here, 'Stubs.cs')))
        for s in SOURCES:
            f.write('"%s"\n' % s)
    p = subprocess.run([DOTNET_UNITY, CSC, '@' + rsp], capture_output=True, text=True, errors='replace')
    errs = [l for l in (p.stdout + p.stderr).splitlines() if 'error CS' in l]
    if errs:
        print('\n'.join(errs[:40]))
        sys.exit(1)
    with open(os.path.join(out, 'check.runtimeconfig.json'), 'w', encoding='utf-8') as f:
        f.write('{ "runtimeOptions": { "tfm": "netcoreapp3.1", "framework": { "name": "Microsoft.NETCore.App", "version": "3.1.0" } } }\n')
    r = subprocess.run(['dotnet', os.path.join(out, 'check.dll'), 'Assets/Resources/Events'], capture_output=True)
    sys.stdout.buffer.write(r.stdout)
    sys.stdout.buffer.flush()
    sys.exit(r.returncode)


if __name__ == '__main__':
    main()

"""戦闘の規則 (手札の持ち越し・ボスのダウン) を Unity なしで検算する。

UnityEngine 非依存の CombatSystem/TerminalCards.cs・BossDown.cs・MutualAttack.cs を Unity 同梱の Roslyn (csc) でコンパイルし、
システムの .NET Core 3.1 ランタイムで実行する。 本体の他の型 (GameHooks・GameRng など) は Stubs.cs の代用品。
最後の行が `ALL OK` なら全部通っている。

使い方 (リポジトリのルートで): python Tools/rules_check/run.py
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
COMBAT = 'Assets/Scripts/CombatSystem'
SOURCES = ['TerminalCards', 'BossDown', 'MutualAttack', 'HeavyCycle']
# BOT の判断のうち純粋な部分 (#141・ダウンの狙いと残す印)。 UnityEngine に依存しない
EXTRA = ['Assets/Scripts/AutoTest/DownKeepRules.cs']


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
        f.write('-target:exe\n-nostdlib+\n-noconfig\n-langversion:9\n-nowarn:0169,0649,0414,0067,0162,0219,1701,1702,0108,0114,0618,0672\n')
        f.write('-out:"%s"\n' % fwd(os.path.join(out, 'check.dll')))
        for pat in ('System.*.dll', 'Microsoft.*.dll', 'mscorlib.dll', 'netstandard.dll'):
            for d in glob.glob(rt + '/' + pat):
                if 'Native' not in d:
                    f.write('-r:"%s"\n' % fwd(d))
        for name in ('Stubs.cs', 'Program.cs'):
            f.write('"%s"\n' % fwd(os.path.join(here, name)))
        for n in SOURCES:
            f.write('"%s/%s.cs"\n' % (COMBAT, n))
        for p in EXTRA:
            f.write('"%s"\n' % p)
    p = subprocess.run([DOTNET_UNITY, CSC, '@' + rsp], capture_output=True, text=True, errors='replace')
    errs = [l for l in (p.stdout + p.stderr).splitlines() if 'error CS' in l]
    if errs:
        print('\n'.join(errs[:40]))
        sys.exit(1)
    with open(os.path.join(out, 'check.runtimeconfig.json'), 'w', encoding='utf-8') as f:
        f.write('{ "runtimeOptions": { "tfm": "netcoreapp3.1", "framework": { "name": "Microsoft.NETCore.App", "version": "3.1.0" } } }\n')
    r = subprocess.run(['dotnet', os.path.join(out, 'check.dll')], capture_output=True)
    sys.stdout.buffer.write(r.stdout)
    sys.stdout.buffer.flush()
    sys.exit(r.returncode)


if __name__ == '__main__':
    main()

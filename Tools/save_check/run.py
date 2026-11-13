"""本体の保存が落ちた時・壊れた時に記録を失わないかを Unity なしで検算する (docs/qa/spec-gaps.md G-24 / G-25)。

UnityEngine 非依存の GameLoop/SafeFile.cs (中断セーブのファイルの書き方・読み方) と
MetaProgression/BackedPrefs.cs (メタ進行の PlayerPrefs の控え・退避) を Unity 同梱の Roslyn (csc) で
コンパイルし、 システムの .NET Core 3.1 ランタイムで実行する。 PlayerPrefs は辞書が代役。
検算の中身は Program.cs (書き込みの各手順の後で落ちる・切れた書きかけ・壊れた本番と控え・消す時は全部・
初回は控えを見ない・壊れた値は退避して上書きで失わない)。 最後の行が `ALL OK` なら全部通っている。

本体側の残り (RunSave の版と指紋の確かめ・JsonUtility・MetaProgressManager の配線) は Unity でしか確かめられない
── 手順は docs/qa/spec-gaps.md の G-24 / G-25。

使い方 (リポジトリのルートで): python Tools/save_check/run.py
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
SOURCES = ['Assets/Scripts/GameLoop/SafeFile.cs', 'Assets/Scripts/MetaProgression/BackedPrefs.cs']


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
        f.write('-target:exe\n-nostdlib+\n-noconfig\n-langversion:9\n-nowarn:1701,1702\n')
        f.write('-out:"%s"\n' % fwd(os.path.join(out, 'check.dll')))
        for pat in ('System.*.dll', 'Microsoft.*.dll', 'mscorlib.dll', 'netstandard.dll'):
            for d in glob.glob(rt + '/' + pat):
                if 'Native' not in d:
                    f.write('-r:"%s"\n' % fwd(d))
        f.write('"%s"\n' % fwd(os.path.join(here, 'Program.cs')))
        for s in SOURCES:
            f.write('"%s"\n' % s)
    p = subprocess.run([DOTNET_UNITY, CSC, '@' + rsp], capture_output=True, text=True, errors='replace')
    errs = [l for l in (p.stdout + p.stderr).splitlines() if 'error CS' in l]
    if errs:
        print('\n'.join(errs[:40]))
        sys.exit(1)
    with open(os.path.join(out, 'check.runtimeconfig.json'), 'w', encoding='utf-8') as f:
        f.write('{ "runtimeOptions": { "tfm": "netcoreapp3.1", "framework": { "name": "Microsoft.NETCore.App", "version": "3.1.0" } } }\n')
    r = subprocess.run(['dotnet', os.path.join(out, 'check.dll')], capture_output=True)
    sys.stdout.buffer.write(r.stdout)
    sys.stdout.buffer.write(r.stderr)
    sys.stdout.buffer.flush()
    sys.exit(r.returncode)


if __name__ == '__main__':
    main()

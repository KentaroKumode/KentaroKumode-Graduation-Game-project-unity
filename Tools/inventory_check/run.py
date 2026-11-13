"""持ち物の画面 (2026-11-13・升目・吹き出し・詳細の欄) の寸法を Unity なしで検算する。

画面と同じ関数 ── UnityEngine 非依存の InventoryLayout.cs (窓の中の場所・升目の並べ方・送り・矢印・吹き出しの置き場・詳細の頁割り)・
ItemCardInfo.cs (品 1 つの中身・詳細と吹き出しの文)・JpTextLayout.cs (折り返し)・ModalJpMetrics.cs (日本語の窓の寸法) ── を
Unity 同梱の Roslyn (csc) でコンパイルし、 システムの .NET Core 3.1 ランタイムで実行する (作りは Tools/event_page_check/run.py と同じ)。
字の送り幅は焼いたドット字 (GameFont.cs の DotPath の .bytes) を読む。 検算の中身は Program.cs:
  窓・欄が 480×270 に収まる / 品の数 0・1・12・60・200 で升目・見出し・吹き出しが収まり、 全部の品に届く /
  items.json の全件の詳細が切り捨てなく読める / アイコンの無い品の仮の字が升目に入り、 互いに重ならない。
最後の行が `ALL OK` なら全部通っている。

使い方 (リポジトリのルートで): python Tools/inventory_check/run.py
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
SOURCES = ['Assets/Scripts/UI/Game2D/JpTextLayout.cs', 'Assets/Scripts/UI/Game2D/Screens/ModalJpMetrics.cs',
           'Assets/Scripts/UI/Game2D/Screens/InventoryLayout.cs', 'Assets/Scripts/UI/Game2D/Screens/ItemCardInfo.cs']


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

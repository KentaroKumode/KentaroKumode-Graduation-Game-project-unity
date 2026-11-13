"""航行マップの規則 (Assets/Scripts/MapSystem/FreeMove) を Unity なしで検算する。

UnityEngine 非依存の FreeMove/*.cs だけを Unity 同梱の Roslyn (csc) でコンパイルし、 システムの
.NET Core 3.1 ランタイムで実行する。 乱数 (GameRng) は Stubs.cs の代用品。 検算の中身は Program.cs
(生成の不変条件・決定性・巡回・補足・捕まる・逃げる・囮罠・照明・偵察・天候・物資不足・猟犬と使い魔・
直行する素朴な BOT の捕まり率)。 最後の行が `ALL OK` なら全部通っている。

使い方 (リポジトリのルートで):
  python Tools/freemap_check/run.py          規則の検算と口の一致 (層の数を絞る・3 分以内)
  python Tools/freemap_check/run.py --full   口の一致を全数で (嵐の前の main・storm-boss と突き合わせる層を全部)
  python Tools/freemap_check/run.py --walk   歩かせる検算だけ (歩き方 A/B/C × 時計 × 深さ 1/3/5/6 × 1000 層・10 分前後)
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
FREEMOVE = 'Assets/Scripts/MapSystem'
SOURCES = ['FreeMapParams', 'FreeMapLayout', 'FreeMapGenerator', 'FoeProfile', 'Weather', 'FreeMapSim', 'FreeMapPerks', 'FloorPlan', 'TerritoryBypass', 'AshBase', 'Storm', 'Instability']
# 比べる版から取るファイル: 今の一覧と、 〈灰に埋もれた基地〉の前の版にある RiftGate.cs (2026-11-13 に AshBase.cs へ置き換えた)。 無いファイルは飛ばす
REF_SOURCES = SOURCES + ['RiftGate']


def fwd(p):
    return p.replace(BS, '/')


# 比べる版 (2026-11-13 二版):
#   MAIN_REF = 嵐の前の main (b7bbc01・storm をマージする前)。 測定の口 stormEnabled = False が 1 点も変わらないかを見る。
#   TURN_REF = 手番で進む嵐 (storm-boss・6b055e8)。 測定の口 stormClock = Turns が 1 手も変わらないかを見る。
MAIN_REF = os.environ.get('FREEMAP_MAIN_REF', 'b7bbc01')
TURN_REF = os.environ.get('FREEMAP_TURN_REF', '6b055e8')


def reference(out, ref, ns, dirname):
    """版 ref の FreeMove を名前空間 ns へ移して一緒にコンパイルする (StormCheck.cs / InstabilityCheck.cs が今の FreeMove と突き合わせる)。"""
    # 戻り値の ring = その版が「ボスの隣を全部イベントに」の作り (〈灰に埋もれた基地〉の前・生成の口の意味が裏返し → StormCheck.MainGen / InstabilityCheck.TurnGen)
    d = os.path.join(out, dirname)
    os.makedirs(d, exist_ok=True)
    for old in glob.glob(os.path.join(d, '*.cs')):
        os.remove(old)   # 前の回に取ったファイルを残さない
    paths = []
    ring = False
    for n in REF_SOURCES:
        r = subprocess.run(['git', 'show', '%s:%s/FreeMove/%s.cs' % (ref, FREEMOVE, n)], capture_output=True)
        if r.returncode != 0:
            continue   # その版に無いファイル (Storm.cs / Instability.cs など)
        src = r.stdout.decode('utf-8-sig')
        if 'namespace MapSystem.FreeMove' not in src:
            sys.exit('%s の %s.cs に namespace MapSystem.FreeMove が無い' % (ref, n))
        if n == 'FreeMapGenerator' and 'PlaceRiftGate' in src:
            ring = True
        src = 'using MapSystem;\n' + src.replace('namespace MapSystem.FreeMove', 'namespace %s' % ns)
        path = os.path.join(d, n + '.cs')
        with open(path, 'w', encoding='utf-8') as f:
            f.write(src)
        paths.append(path)
    if not paths:
        sys.exit('%s の FreeMove を git から取れない' % ref)
    return paths, ring


def main_reference(out):
    """戻り値: (ファイルの一覧, 立てる記号の一覧)。"""
    mp, mring = reference(out, MAIN_REF, 'MainRef.FreeMove', 'mainref')
    tp, tring = reference(out, TURN_REF, 'TurnRef.FreeMove', 'turnref')
    return mp + tp, [d for d, on in (('MAINREF_RING', mring), ('TURNREF_RING', tring)) if on]


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
        for name in ('Stubs.cs', 'Program.cs', 'StormCheck.cs', 'InstabilityCheck.cs'):
            f.write('"%s"\n' % fwd(os.path.join(here, name)))
        refs, defines = main_reference(out)
        for dname in defines:
            f.write('-define:%s\n' % dname)
        for path in refs:
            f.write('"%s"\n' % fwd(path))
        f.write('"%s/TileType.cs"\n' % FREEMOVE)
        for n in SOURCES:
            f.write('"%s/FreeMove/%s.cs"\n' % (FREEMOVE, n))
    p = subprocess.run([DOTNET_UNITY, CSC, '@' + rsp], capture_output=True, text=True, errors='replace')
    errs = [l for l in (p.stdout + p.stderr).splitlines() if 'error CS' in l]
    if errs:
        print('\n'.join(errs[:40]))
        sys.exit(1)
    with open(os.path.join(out, 'check.runtimeconfig.json'), 'w', encoding='utf-8') as f:
        f.write('{ "runtimeOptions": { "tfm": "netcoreapp3.1", "framework": { "name": "Microsoft.NETCore.App", "version": "3.1.0" } } }\n')
    r = subprocess.run(['dotnet', os.path.join(out, 'check.dll')] + [a for a in sys.argv[1:] if a in ('--full', '--walk')], capture_output=True)
    sys.stdout.buffer.write(r.stdout)
    sys.stdout.buffer.flush()
    sys.exit(r.returncode)


if __name__ == '__main__':
    main()

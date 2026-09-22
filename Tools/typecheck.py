"""Unity を起動せずに C# の型チェックだけを回す。

Unity Editor を立ち上げずに「コンパイルが通るか」を確かめたいときに使う。
Unity 同梱の Roslyn (csc) を直接叩き、Assembly-CSharp と Assembly-CSharp-Editor を
それぞれビルドして診断だけ見る。生成した dll は obj/ に捨てるだけで使わない。

    python Tools/typecheck.py

前提: Unity 2022.3.22f1 がインストール済みで、一度でも Unity が
Assembly-CSharp*.csproj を生成していること (参照アセンブリ一覧をそこから取る)。

注意:
  - csproj の <Compile> は Unity が起動していないと古いままなので **信用しない**。
    ソースは Assets 以下を実走査して集め、Unity の分割規則
    (パスに Editor フォルダを含む = Editor アセンブリ) に合わせて振り分ける。
  - これはあくまで型チェック。 シリアライズ・meta・シーン参照は検証されないので、
    最終確認は Unity の refresh_unity + read_console で行うこと。
"""
import io
import os
import re
import subprocess
import sys
from xml.sax.saxutils import unescape

BS = chr(92)
UNITY = r'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Data'
DOTNET = UNITY + '/NetCoreRuntime/dotnet.exe'
CSC = UNITY + '/DotNetSdkRoslyn/csc.dll'

NOWARN = '0169,0649,0414,0067,0162,0219,1701,1702,0108,0114,0618,0672'


def fix(p):
    return unescape(p).replace(BS, '/')


def collect_sources():
    """Assets 以下の .cs を (通常, Editor) に振り分ける。"""
    game, editor = [], []
    for root, _dirs, files in os.walk('Assets'):
        r = root.replace(BS, '/')
        is_editor = 'Editor' in r.split('/')[1:]
        for f in files:
            if f.endswith('.cs'):
                (editor if is_editor else game).append(r + '/' + f)
    return sorted(game), sorted(editor)


def refs_of(proj):
    s = io.open(proj, encoding='utf-8-sig').read()
    refs = [fix(x) for x in re.findall(r'<HintPath>([^<]+)</HintPath>', s)]
    defs = re.findall(r'<DefineConstants>([^<]*)</DefineConstants>', s)
    return refs, (defs[0] if defs else '')


def write_rsp(name, sources, refs, defines, extra_refs=()):
    path = 'obj/%s.rsp' % name
    with io.open(path, 'w', encoding='utf-8') as f:
        f.write('-target:library\n-nostdlib+\n-noconfig\n-langversion:9\n')
        f.write('-nowarn:%s\n' % NOWARN)
        f.write('-out:obj/typecheck_%s.dll\n' % name)
        if defines:
            f.write('-define:%s\n' % defines)
        for r in list(refs) + list(extra_refs):
            f.write('-r:"%s"\n' % r)
        for c in sources:
            f.write('"%s"\n' % c)
    return path


def run(name, rsp):
    p = subprocess.run([DOTNET, CSC, '@' + rsp],
                       capture_output=True, text=True, errors='replace')
    lines = [l for l in (p.stdout + p.stderr).splitlines() if 'error CS' in l]
    if lines:
        print('%s: %d error(s)' % (name, len(lines)))
        for l in lines[:40]:
            print('  ' + l)
    else:
        print('%s: OK' % name)
    return len(lines)


def main():
    if not os.path.exists(CSC):
        sys.exit('Unity 同梱の csc が見つかりません: ' + CSC)
    os.makedirs('obj', exist_ok=True)
    game, editor = collect_sources()
    gref, gdef = refs_of('Assembly-CSharp.csproj')
    eref, edef = refs_of('Assembly-CSharp-Editor.csproj')
    print('Assembly-CSharp        : %d sources' % len(game))
    print('Assembly-CSharp-Editor : %d sources' % len(editor))
    n = run('Assembly-CSharp', write_rsp('Assembly-CSharp', game, gref, gdef))
    n += run('Assembly-CSharp-Editor',
             write_rsp('Assembly-CSharp-Editor', editor, eref, edef,
                       extra_refs=['obj/typecheck_Assembly-CSharp.dll']))
    sys.exit(1 if n else 0)


if __name__ == '__main__':
    main()

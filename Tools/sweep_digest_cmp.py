#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""2 つのスイープのアームを、 同じ runIdx の deterministicDigest で突き合わせる。

挙動を変えないはずのリファクタが本当に 1 ビットも変えていないかの合格判定に使う
(2026-09-16 の敵 ×0.93 畳み込みで 10,000/10,000 一致を出した方法)。

使い方:
    python Tools/sweep_digest_cmp.py <sessionA> <armA> <sessionB> <armB>
"""
import glob, io, json, os, sys

def load(session, arm):
    out = {}
    for d in glob.glob(os.path.join(session, arm + "_c*")):
        j = json.load(io.open(os.path.join(d, "response.json"), encoding="utf-8"))
        s = int(j["seedStart"])
        for i, (dg, b) in enumerate(zip(j["digests"], j["bandScores"])):
            out[s + i] = (dg, b)
    return out

def main():
    sys.stdout.reconfigure(encoding="utf-8")
    a = load(sys.argv[1], sys.argv[2]); b = load(sys.argv[3], sys.argv[4])
    ks = sorted(set(a) & set(b))
    same = sum(1 for k in ks if a[k][0] == b[k][0])
    diff = [k for k in ks if a[k][0] != b[k][0]]
    band = sum(1 for k in diff if a[k][1] != b[k][1])
    clr = sum(1 for k in diff if (a[k][1] >= 11) != (b[k][1] >= 11))
    print("共通 %d ラン: digest 一致 %d / 不一致 %d (うち band が変わった %d / クリア判定が変わった %d)"
          % (len(ks), same, len(diff), band, clr))
    if diff: print("不一致の先頭:", diff[:10])

if __name__ == "__main__":
    main()

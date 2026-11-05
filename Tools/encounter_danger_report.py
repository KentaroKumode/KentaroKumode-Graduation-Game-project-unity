"""戦闘名ごとの危険度の学習値 (AutoRunLogs/learning/<suffix>/encounter_danger.json) を表にする。
使い方: python Tools/encounter_danger_report.py [learning のディレクトリ] [最小戦闘数]"""
import json
import math
import sys

root = sys.argv[1] if len(sys.argv) > 1 else "AutoRunLogs/learning/buffOn_debuffOff"
min_n = int(sys.argv[2]) if len(sys.argv) > 2 else 40
d = json.load(open(root + "/encounter_danger.json", encoding="utf-8"))
KIND = {"N": "通常", "E": "エリート", "B": "ボス"}
rows = []
for e in d["entries"]:
    n = e["n"]
    if n < min_n:
        continue
    mean = e["sum"] / n
    sd = math.sqrt(max(0.0, e["sumSq"] / n - mean * mean))
    layer, kind, title = e["key"].split("|", 2)
    rows.append((int(layer), kind, mean, sd, e["lost"] / n, n, title))
rows.sort(key=lambda r: (r[0], r[1], -r[2]))
print(f"層 種類      戦闘数   平均損耗  ばらつき  敗北率  学習値(平均+1.28sd+0.5×敗北率)  戦闘名")
for layer, kind, mean, sd, lose, n, title in rows:
    bar = mean + 1.28 * sd + 0.5 * lose
    print(f"{layer}  {KIND[kind]:<6} {n:>7}   {100 * mean:5.1f}%   {100 * sd:5.1f}%  {100 * lose:5.1f}%   {100 * bar:5.1f}%   {title}")

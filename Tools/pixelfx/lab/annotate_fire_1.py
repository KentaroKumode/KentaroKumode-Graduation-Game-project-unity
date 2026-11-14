# 炎の効果に札を付けた写しを作る (lab/annotated/fire/)。 元の src/fx/fire/ は触らない。
# 置き換えは「元の行 → 札を足した行」の組で、 当たらなければ止まる。 札の意味は library/fire/ の各資料に書く。
import io, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "src", "fx")
DST = os.path.join(HERE, "annotated")


def ann(rel, edits):
    s = io.open(os.path.join(SRC, rel), encoding="utf-8").read().replace("\r\n", "\n")
    for a, b in edits:
        if s.count(a) != 1: sys.exit("%s: %d か所: %s" % (rel, s.count(a), a[:70]))
        s = s.replace(a, b)
    p = os.path.join(DST, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("ok", rel, len(edits))


# ---- 共通の道具 (_lib.js): 粒の炎は出どころの札で描く。 ループの炎 burn は部品ごとに札
ann("fire/_lib.js", [
    ("    for (const q of fire) if (q.delay <= 0 && !!(q.flag & PF.BACK) === back) firePuff(p, q.x, q.y, q.life / q.max, q.size, q.seed % 1, H);",
     "    const _t = p.tag;\n    for (const q of fire) if (q.delay <= 0 && !!(q.flag & PF.BACK) === back) (p.tag = (q.src || \"ps\") + \"/FIRE\"), firePuff(p, q.x, q.y, q.life / q.max, q.size, q.seed % 1, H);\n    p.tag = _t;"),
    ("  // the plume: each puff keeps the sway the plume had when it left, so the wiggle climbs it\n",
     "  const _T = p.tag, B = (_T ? _T + \":\" : \"burn:\");\n  p.tag = B + \"plume-smoke\";\n  // the plume: each puff keeps the sway the plume had when it left, so the wiggle climbs it\n"),
    ("  // flame puffs shed from the tips\n  const puffs = [];", "  p.tag = B + \"puff-smoke\";\n  // flame puffs shed from the tips\n  const puffs = [];"),
    ("  for (let i = 0; i < TT.length; i++) if (TT[i].back) tongueAt(i);\n  for (let i = 0; i < TT.length; i++) if (!TT[i].back) tongueAt(i);\n  o.mid?.(fl);",
     "  p.tag = B + \"tongues-back\";\n  for (let i = 0; i < TT.length; i++) if (TT[i].back) tongueAt(i);\n  p.tag = B + \"tongues\";\n  for (let i = 0; i < TT.length; i++) if (!TT[i].back) tongueAt(i);\n  p.tag = _T;\n  o.mid?.(fl);\n  p.tag = B + \"licks\";"),
    ("  for (const [x, y, k, s, f] of puffs) firePuff(p, x, y, k, s, f, H);", "  p.tag = B + \"puffs\";\n  for (const [x, y, k, s, f] of puffs) firePuff(p, x, y, k, s, f, H);\n  p.tag = B + \"embers\";"),
    ("  // light: a broad warm wash, lights on the flames, a hot heart at the base\n  if (G > 0) {", "  p.tag = B + \"light\";\n  // light: a broad warm wash, lights on the flames, a hot heart at the base\n  if (G > 0) {"),
    ("  return { tip, fl };\n}", "  p.tag = _T;\n  return { tip, fl };\n}"),
])

# ---- 炎の柱
ann("fire/flame-pillar.js", [
    # 描く側
    ("    const h = height(t), w0 = width(t);\n    if (h < 1 || w0 < 0.3) return;\n", "    const h = height(t), w0 = width(t);\n    if (h < 1 || w0 < 0.3) return;\n    p.tag = \"crown-tongues\";\n"),
    ("    const lvlC = [H[0], H[1], H[2], H[3], H[4], H[5], H[6], H[7]];\n", "    p.tag = \"column\";\n    const lvlC = [H[0], H[1], H[2], H[3], H[4], H[5], H[6], H[7]];\n"),
    ("      if (y % 12 === 0) p.glow(cx, y, w0 * 2.4 + 8, GLOW, 0.28 * clamp(w0 / 12));", "      if (y % 12 === 0) (p.tag = \"column-light\"), p.glow(cx, y, w0 * 2.4 + 8, GLOW, 0.28 * clamp(w0 / 12)), (p.tag = \"column\");"),
    ("    p.glow(0, -h * 0.45, 92, GLOW, 0.3 * clamp(w0 / 12));\n  }", "    p.tag = \"column-light\";\n    p.glow(0, -h * 0.45, 92, GLOW, 0.3 * clamp(w0 / 12));\n  }"),
    ("    const k = heat(t);\n    if (k <= -0.05) return;\n", "    const k = heat(t);\n    if (k <= -0.05) return;\n    p.tag = \"cracks\";\n"),
    ("        if (i % 2 === 0) p.glow(x1, y1, 5 + 7 * hot, GLOW, 0.3 * hot);", "        if (i % 2 === 0) (p.tag = \"cracks-light\"), p.glow(x1, y1, 5 + 7 * hot, GLOW, 0.3 * hot), (p.tag = \"cracks\");"),
    ("    if (k > 0) floorLight(p, 0, 0.5, 44, 8, GLOW, 0.35 * k);", "    p.tag = \"floor-light\";\n    if (k > 0) floorLight(p, 0, 0.5, 44, 8, GLOW, 0.35 * k);"),
    ("      cracksDraw(p);\n      simDraw(p, ps, H, \"back\");\n      const k = (t - ERUPT) / 0.45;\n", "      cracksDraw(p);\n      simDraw(p, ps, H, \"back\");\n      p.tag = \"shock-ring\";\n      const k = (t - ERUPT) / 0.45;\n"),
    ("      column(p);\n      for (const b of bits.list) rock(p, b, st);\n      simDraw(p, ps, H, \"front\");\n      const k = (t - ERUPT) / 0.45;\n",
     "      column(p);\n      p.tag = \"rocks\";\n      for (const b of bits.list) rock(p, b, st);\n      simDraw(p, ps, H, \"front\");\n      p.tag = \"shock-ring\";\n      const k = (t - ERUPT) / 0.45;\n"),
    ("      const f = (t - ERUPT) / 0.08;\n      if (f > 0 && f < 1) {", "      p.tag = \"foot-flash\";\n      const f = (t - ERUPT) / 0.08;\n      if (f > 0 && f < 1) {"),
    # 粒の出どころ
    ("      if (t < ERUPT && R.chance(0.6 * dt * 60 * smooth(0.15, ERUPT, t))) ps.spawn(P.FIRE,", "      ps.src = \"charge\";\n      if (t < ERUPT && R.chance(0.6 * dt * 60 * smooth(0.15, ERUPT, t))) ps.spawn(P.FIRE,"),
    ("          if (R.chance(0.5)) ps.spawn(P.FIRE, x, y - 1,", "          ps.src = \"crack-leak\";\n          if (R.chance(0.5)) ps.spawn(P.FIRE, x, y - 1,"),
    ("        erupted = true;\n        lights.add(0, -40, 90, WARM, 0.7, 0.6);\n        lights.add(0, -8, 44, WHITE, 0.7, 0.1);",
     "        erupted = true;\n        lights.src = \"burst-light\";\n        lights.add(0, -40, 90, WARM, 0.7, 0.6);\n        lights.add(0, -8, 44, WHITE, 0.7, 0.1);\n        ps.src = \"burst\";"),
    ("        if (R.chance(0.9 * dt * 60)) {\n          const s = R.sign(), y = -R.range(4, h * 0.9);", "        ps.src = \"side-shed\";\n        if (R.chance(0.9 * dt * 60)) {\n          const s = R.sign(), y = -R.range(4, h * 0.9);"),
    ("        if (R.chance(0.7 * dt * 60)) ps.spawn(P.FIRE, R.range(-w, w), -h + R.range(0, 6),", "        ps.src = \"cap\";\n        if (R.chance(0.7 * dt * 60)) ps.spawn(P.FIRE, R.range(-w, w), -h + R.range(0, 6),"),
    ("        if (t < FALL && R.chance(0.5 * dt * 60)) ps.spawn(P.EMBER,", "        ps.src = \"spew\";\n        if (t < FALL && R.chance(0.5 * dt * 60)) ps.spawn(P.EMBER,"),
    ("      if (fell && t < GONE - 0.1 && R.chance(1.6 * dt * 60)) ps.spawn(P.FIRE,", "      ps.src = \"collapse\";\n      if (fell && t < GONE - 0.1 && R.chance(1.6 * dt * 60)) ps.spawn(P.FIRE,"),
    ("          b.dusted = true;\n", "          b.dusted = true;\n          ps.src = \"rock-land\";\n"),
])

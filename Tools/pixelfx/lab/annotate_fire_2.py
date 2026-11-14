# 炎の効果に札を付けた写しを作る (その 2: 焚き火)。 使い方と考え方は annotate_fire_1.py と同じ。
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


ann("fire/campfire.js", [
    ("  // smoke curling off the top, behind everything:", "  p.tag = \"plume-smoke\";\n  // smoke curling off the top, behind everything:"),
    ("  // flame puffs shed from the tips: they burn out above the fire, some into smoke\n", "  p.tag = \"puff-smoke\";\n  // flame puffs shed from the tips: they burn out above the fire, some into smoke\n"),
    ("  coals(p, W, H, t, S);\n  for (const L of LOGS) log(p, L, W, H, CROSS, 1, t, S + 11);\n  for (let i = 0; i < T.length; i++) if (T[i].back) tongueAt(i);\n",
     "  p.tag = \"coals\";\n  coals(p, W, H, t, S);\n  p.tag = \"logs-far\";\n  for (const L of LOGS) log(p, L, W, H, CROSS, 1, t, S + 11);\n  p.tag = \"tongues-back\";\n  for (let i = 0; i < T.length; i++) if (T[i].back) tongueAt(i);\n  p.tag = \"wood-flames\";\n"),
    ("  for (let i = 0; i < T.length; i++) if (!T[i].back) tongueAt(i);\n  // licks torn off the tips, rising and shrinking\n",
     "  p.tag = \"tongues\";\n  for (let i = 0; i < T.length; i++) if (!T[i].back) tongueAt(i);\n  p.tag = \"licks\";\n  // licks torn off the tips, rising and shrinking\n"),
    ("  for (const [x, y, k, s, f] of puffs) firePuff(p, x, y, k, s, f, H);\n  for (const L of LOGS) log(p, L, W, H, 0, CROSS, t, S + 11);\n",
     "  p.tag = \"puffs\";\n  for (const [x, y, k, s, f] of puffs) firePuff(p, x, y, k, s, f, H);\n  p.tag = \"logs-near\";\n  for (const L of LOGS) log(p, L, W, H, 0, CROSS, t, S + 11);\n  p.tag = \"pops\";\n"),
    ("  // embers carried up by the heat, wandering and cooling\n", "  p.tag = \"embers\";\n  // embers carried up by the heat, wandering and cooling\n"),
    ("  // light: a big warm room light, a hot heart, a pool on the floor, all breathing with the flames\n", "  p.tag = \"light\";\n  // light: a big warm room light, a hot heart, a pool on the floor, all breathing with the flames\n"),
    ("  floorLight(p, 0, 0.5, 48, 8, GLOW, 0.3 * fl);\n}", "  p.tag = \"floor-light\";\n  floorLight(p, 0, 0.5, 48, 8, GLOW, 0.3 * fl);\n}"),
])

# ---- 炎の輪
ann("fire/fire-ring.js", [
    ("      scorch(p);\n      ring(p, true);\n      flames(p, true);\n      simDraw(p, ps, H, \"back\");\n      // light on the floor running with the front\n",
     "      p.tag = \"scorch\";\n      scorch(p);\n      p.tag = \"front-ring\";\n      ring(p, true);\n      p.tag = \"flames-far\";\n      flames(p, true);\n      simDraw(p, ps, H, \"back\");\n      p.tag = \"front-light\";\n      // light on the floor running with the front\n"),
    ("      ring(p, false);\n      flames(p, false);\n      simDraw(p, ps, H, \"front\");\n      // the charge glowing at the feet, then the white flash as it goes\n",
     "      p.tag = \"front-ring\";\n      ring(p, false);\n      p.tag = \"flames-near\";\n      flames(p, false);\n      simDraw(p, ps, H, \"front\");\n      p.tag = \"charge-glow\";\n      // the charge glowing at the feet, then the white flash as it goes\n"),
    ("      if (t >= CHARGE && t < CHARGE + 0.1) {\n        const k = 1 - (t - CHARGE) / 0.1;", "      p.tag = \"foot-flash\";\n      if (t >= CHARGE && t < CHARGE + 0.1) {\n        const k = 1 - (t - CHARGE) / 0.1;"),
    ("      const k = smooth(CHARGE - 0.05, CHARGE, t) * (1 - smooth(CHARGE + SPREAD * 0.5, CHARGE + SPREAD + 1.2, t));", "      p.tag = \"wash-light\";\n      const k = smooth(CHARGE - 0.05, CHARGE, t) * (1 - smooth(CHARGE + SPREAD * 0.5, CHARGE + SPREAD + 1.2, t));"),
    ("      if (t < CHARGE - 0.05 && R.chance(0.9 * dt * 60)) {", "      ps.src = \"charge-in\";\n      if (t < CHARGE - 0.05 && R.chance(0.9 * dt * 60)) {"),
    ("      if (t < CHARGE && R.chance(0.5 * dt * 60 * (t / CHARGE))) ps.spawn(P.FIRE,", "      ps.src = \"charge\";\n      if (t < CHARGE && R.chance(0.5 * dt * 60 * (t / CHARGE))) ps.spawn(P.FIRE,"),
    ("        burst = true;\n        lights.add(0, -18, 64, WARM, 0.85, 0.5);", "        burst = true;\n        lights.src = \"burst-light\";\n        ps.src = \"burst\";\n        lights.add(0, -18, 64, WARM, 0.85, 0.5);"),
    ("      if (t > CHARGE && t < CHARGE + SPREAD + 1.2)\n        for (let i = 0; i < 3; i++) {", "      ps.src = \"shed\";\n      if (t > CHARGE && t < CHARGE + SPREAD + 1.2)\n        for (let i = 0; i < 3; i++) {"),
])

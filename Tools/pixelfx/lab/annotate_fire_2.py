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

# ---- 隕石
ann("fire/meteor.js", [
    ("        const [x, y] = pos(t), vx = (x - last[0]) / dt, vy = (y - last[1]) / dt;\n        last = [x, y];\n", "        const [x, y] = pos(t), vx = (x - last[0]) / dt, vy = (y - last[1]) / dt;\n        last = [x, y];\n        ps.src = \"tail\";\n"),
    ("        lights.add(0, -40, 100, WARM, 0.95, 0.9);\n        lights.add(0, -20, 46, WHITE, 0.9, 0.12);\n", "        lights.src = \"burst-light\";\n        lights.add(0, -40, 100, WARM, 0.95, 0.9);\n        lights.add(0, -20, 46, WHITE, 0.9, 0.12);\n        ps.src = \"fireball\";\n"),
    ("        for (let i = 0; i < 9; i++) ps.spawn(P.FIRE, R.range(-6, 6), -6, R.range(-14, 14), -R.range(130, 220),", "        ps.src = \"rise\";\n        for (let i = 0; i < 9; i++) ps.spawn(P.FIRE, R.range(-6, 6), -6, R.range(-14, 14), -R.range(130, 220),"),
    ("        for (let i = 0; i < 20; i++) {\n          const a = R.range(-Math.PI, 0), d = R.range(4, 18);", "        ps.src = \"smoke-cap\";\n        for (let i = 0; i < 20; i++) {\n          const a = R.range(-Math.PI, 0), d = R.range(4, 18);"),
    ("        for (let i = 0; i < 16; i++) {\n          const s = R.sign();\n          ps.spawn(P.SMOKE, s * R.range(4, 18),", "        ps.src = \"ground-dust\";\n        for (let i = 0; i < 16; i++) {\n          const s = R.sign();\n          ps.spawn(P.SMOKE, s * R.range(4, 18),"),
    ("        for (let i = 0; i < 90; i++) {", "        ps.src = \"burst\";\n        for (let i = 0; i < 90; i++) {"),
    ("        for (let i = 0; i < 10; i++) {\n          const a = -Math.PI / 2 + R.range(-1, 1.05), sp = R.range(130, 240);", "        ps.src = \"brand\";\n        for (let i = 0; i < 10; i++) {\n          const a = -Math.PI / 2 + R.range(-1, 1.05), sp = R.range(130, 240);"),
    ("        for (let i = 0; i < 80; i++) {\n          const u = R.range(-1, 1),", "        ps.src = \"burst\";\n        for (let i = 0; i < 80; i++) {\n          const u = R.range(-1, 1),"),
    ("          b.dusted = true;\n", "          b.dusted = true;\n          ps.src = \"rock-land\";\n"),
    ("      crater(p);\n      simDraw(p, ps, H, \"back\");\n      blazes.drawBack(p);\n      const k = (t - HIT) / 0.5;\n", "      p.tag = \"crater\";\n      crater(p);\n      simDraw(p, ps, H, \"back\");\n      blazes.drawBack(p);\n      p.tag = \"shock-ring\";\n      const k = (t - HIT) / 0.5;\n"),
    ("    draw(p) {\n      const k = (t - HIT) / 0.5;\n", "    draw(p) {\n      p.tag = \"shock-ring\";\n      const k = (t - HIT) / 0.5;\n"),
    ("      for (const b of bits.list) rock(p, b, st);\n      if (t < HIT) {", "      p.tag = \"rocks\";\n      for (const b of bits.list) rock(p, b, st);\n      if (t < HIT) {"),
    ("        floorLight(p, 0, 0.5, 30 + 40 * (t / HIT), 7, GLOW, 0.5 * (t / HIT) ** 2);\n", "        p.tag = \"approach-floor-light\";\n        floorLight(p, 0, 0.5, 30 + 40 * (t / HIT), 7, GLOW, 0.5 * (t / HIT) ** 2);\n        p.tag = \"comet-tail\";\n"),
    ("        meteor(p, x, y, 6.5, 0);\n        p.glow(x, y, 46, GLOW, 0.55);", "        p.tag = \"meteor-rock\";\n        meteor(p, x, y, 6.5, 0);\n        p.tag = \"meteor-light\";\n        p.glow(x, y, 46, GLOW, 0.55);"),
    ("        if (end > 0) {\n          meteor(p, 0, 0, 5.5, cool, end);\n          p.glow(0, -2, 22, GLOW, 0.4 * (1 - cool) * end);", "        if (end > 0) {\n          p.tag = \"buried-core\";\n          meteor(p, 0, 0, 5.5, cool, end);\n          p.tag = \"buried-core-light\";\n          p.glow(0, -2, 22, GLOW, 0.4 * (1 - cool) * end);"),
    ("      const f = (t - HIT) / 0.08;\n      if (f > 0 && f < 1) {", "      p.tag = \"hero-flash\";\n      const f = (t - HIT) / 0.08;\n      if (f > 0 && f < 1) {"),
    ("      const fr = (t - HIT) / 0.24;", "      p.tag = \"hero-flare\";\n      const fr = (t - HIT) / 0.24;"),
])

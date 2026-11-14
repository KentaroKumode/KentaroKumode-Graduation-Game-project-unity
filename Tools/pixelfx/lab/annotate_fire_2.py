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

# ---- 吐く炎 (円錐)
ann("fire/fire-breath.js", [
    ("  for (const [sa, x, y, q] of smoke) smokePuff(p, x, y, 1 - sa / q.sl, q.s * 1.1, SMOKE, true, 0.7);\n  fire.sort((a, b) => b[0] - a[0]);\n",
     "  p.tag = \"cone-smoke\";\n  for (const [sa, x, y, q] of smoke) smokePuff(p, x, y, 1 - sa / q.sl, q.s * 1.1, SMOKE, true, 0.7);\n  fire.sort((a, b) => b[0] - a[0]);\n  p.tag = \"cone-billows\";\n"),
    ("    if (tear > 0) {\n      const n = Math.floor(q.f * 1e5),", "    if (tear > 0) {\n      p.tag = \"cone-tear-tongues\";\n      const n = Math.floor(q.f * 1e5),"),
    ("      flameTongue(p, x, y + size * 0.3, h, size * 0.5 * (1.1 - heat * 0.5), -h * 0.4 + N(2.5, n + 1) * 2, N, n, H, heat < 0.45 ? LAYERS : heat < 0.72 ? BACK_LAYERS : COOL_LAYERS);\n    }",
     "      flameTongue(p, x, y + size * 0.3, h, size * 0.5 * (1.1 - heat * 0.5), -h * 0.4 + N(2.5, n + 1) * 2, N, n, H, heat < 0.45 ? LAYERS : heat < 0.72 ? BACK_LAYERS : COOL_LAYERS);\n      p.tag = \"cone-billows\";\n    }"),
    ("  // licks curling off the edges of the cone\n", "  p.tag = \"edge-licks\";\n  // licks curling off the edges of the cone\n"),
    ("  // the roar: bright streaks racing out through the heart of the cone\n", "  p.tag = \"roar-streaks\";\n  // the roar: bright streaks racing out through the heart of the cone\n"),
    ("  // the jet at the lips: white-hot, overlapping tongues that flutter as the breath roars\n", "  p.tag = \"lip-jet\";\n  // the jet at the lips: white-hot, overlapping tongues that flutter as the breath roars\n"),
    ("  // sparks flung out ahead and falling; embers lifting off the far end\n", "  p.tag = \"sparks\";\n  // sparks flung out ahead and falling; embers lifting off the far end\n"),
    ("  looped(t, D, 18, S + 400,", "  p.tag = \"embers\";\n  looped(t, D, 18, S + 400,"),
    ("  // light: a flare at the lips, light all along the cone widening with it, a broad halo\n", "  p.tag = \"light\";\n  // light: a flare at the lips, light all along the cone widening with it, a broad halo\n"),
])

# ---- 火球 (飛んでいる玉)
ann("fire/fireball.js", [
    ("  for (const [sa, x, y, q] of smoke) smokePuff(p, x, y, 1 - sa / q.sl, q.s * 0.9, SMOKE);\n  fire.sort((a, b) => b[0] - a[0]);\n",
     "  p.tag = \"tail-smoke\";\n  for (const [sa, x, y, q] of smoke) smokePuff(p, x, y, 1 - sa / q.sl, q.s * 0.9, SMOKE);\n  fire.sort((a, b) => b[0] - a[0]);\n  p.tag = \"tail-puffs\";\n"),
    ("  // the body: tongues of flame rooted in the head streaming back, and a corona licking round it\n", "  p.tag = \"body-tongues\";\n  // the body: tongues of flame rooted in the head streaming back, and a corona licking round it\n"),
    ("  CORONA.forEach(([a, len, w], i) => {", "  p.tag = \"corona\";\n  CORONA.forEach(([a, len, w], i) => {"),
    ("  head(p, H, t, R0, S, hx, hy);\n", "  p.tag = \"head\";\n  head(p, H, t, R0, S, hx, hy);\n  p.tag = \"motes\";\n"),
    ("  // embers left behind: streaks that the head outruns, falling as they cool\n", "  p.tag = \"trail-embers\";\n  // embers left behind: streaks that the head outruns, falling as they cool\n"),
    ("  // light: a hot heart, a warm halo, light running down the tail\n", "  p.tag = \"light\";\n  // light: a hot heart, a warm halo, light running down the tail\n"),
])

# ---- 炎の竜巻
ann("fire/fire-tornado.js", [
    ("  // smoke spiralling off the top\n", "  p.tag = \"top-smoke\";\n  // smoke spiralling off the top\n"),
    ("  floorLight(p, 0, 0.5, 40, 8, GLOW, 0.4);\n  embers(false);\n  ring(false);\n", "  p.tag = \"floor-light\";\n  floorLight(p, 0, 0.5, 40, 8, GLOW, 0.4);\n  p.tag = \"orbit-embers-far\";\n  embers(false);\n  p.tag = \"foot-ring-far\";\n  ring(false);\n  p.tag = \"funnel\";\n"),
    ("    if (y % 10 === 0 && v <= 1) p.glow(c, y, r * 2 + 8, GLOW, 0.22 * fl);", "    if (y % 10 === 0 && v <= 1) (p.tag = \"funnel-light\"), p.glow(c, y, r * 2 + 8, GLOW, 0.22 * fl), (p.tag = \"funnel\");"),
    ("  // flame flung off the spinning sides, cooling as it flies, some into smoke\n", "  p.tag = \"flung-puffs\";\n  // flame flung off the spinning sides, cooling as it flies, some into smoke\n"),
    ("  ring(true);\n  embers(true);\n", "  p.tag = \"foot-ring-near\";\n  ring(true);\n  p.tag = \"orbit-embers-near\";\n  embers(true);\n  p.tag = \"wash-light\";\n"),
])

# ---- 火炎放射
ann("fire/flamethrower.js", [
    ("  for (const [u, x, y, q] of smoke) smokePuff(p, x, y, 1 - u, q.s * 1.2, SMOKE, true, 0.85);\n  fire.sort((a, b) => b[0] - a[0]);\n",
     "  p.tag = \"curl-smoke\";\n  for (const [u, x, y, q] of smoke) smokePuff(p, x, y, 1 - u, q.s * 1.2, SMOKE, true, 0.85);\n  fire.sort((a, b) => b[0] - a[0]);\n  p.tag = \"curl-puffs\";\n"),
    ("  // flames licking up off the burning fuel, riding along the stream, swept back by the wind of it\n", "  p.tag = \"stream-licks\";\n  // flames licking up off the burning fuel, riding along the stream, swept back by the wind of it\n"),
    ("  // the stream: a white core in yellow, orange and a red skin, surges racing down it\n", "  p.tag = \"stream\";\n  // the stream: a white core in yellow, orange and a red skin, surges racing down it\n"),
    ("  // the pilot at the nozzle: a blue cone of gas where it lights\n", "  p.tag = \"pilot\";\n  // the pilot at the nozzle: a blue cone of gas where it lights\n"),
    ("  // burning drops shaken off the stream: they arc down trailing flame and burn out\n", "  p.tag = \"drops\";\n  // burning drops shaken off the stream: they arc down trailing flame and burn out\n"),
    ("  // the cough: when the jet sputters the nozzle spits sparks and a puff of smoke\n", "  p.tag = \"cough\";\n  // the cough: when the jet sputters the nozzle spits sparks and a puff of smoke\n"),
    ("  // light: the pilot, the stream, the burning tip\n", "  p.tag = \"light\";\n  // light: the pilot, the stream, the burning tip\n"),
])

# ---- 霊の炎 (冷たい色・層を逆にした舌)
ann("fire/spirit-flame.js", [
    ("  // the glow it casts, cold and slow\n", "  p.tag = \"halo-light\";\n  // the glow it casts, cold and slow\n"),
    ("  // curls breaking off the tips and drifting up, thinning out\n", "  p.tag = \"curls\";\n  // curls breaking off the tips and drifting up, thinning out\n"),
    ("  // two thin wisps trailing off the sides, then the body\n", "  p.tag = \"side-wisps\";\n  // two thin wisps trailing off the sides, then the body\n"),
    ("  ghostTongue(p, 0, y0, H0, 8.6, (v, r) => wave(v, r), N, S * 11, H, GHOST, 0.6);\n", "  p.tag = \"body\";\n  ghostTongue(p, 0, y0, H0, 8.6, (v, r) => wave(v, r), N, S * 11, H, GHOST, 0.6);\n  p.tag = \"heart-strands\";\n"),
    ("  // light along the body\n", "  p.tag = \"body-light\";\n  // light along the body\n"),
    ("  // cold sparks rising off it, twinkling\n", "  p.tag = \"cold-sparks\";\n  // cold sparks rising off it, twinkling\n"),
])

# ---- ろうそく
ann("fire/candle.js", [
    ("  // a pool of warm light on the floor and the halo behind the flame\n", "  p.tag = \"halo-light\";\n  // a pool of warm light on the floor and the halo behind the flame\n"),
    ("  dish(p, B, H, fl);\n  candle(p, X, H, fl);\n", "  p.tag = \"prop\";\n  dish(p, B, H, fl);\n  candle(p, X, H, fl);\n"),
    ("  // the flame and its blue root\n", "  p.tag = \"flame\";\n  // the flame and its blue root\n"),
    ("  for (let dx = -2; dx <= 2; dx++) {\n    const a = 1 - Math.abs(dx) * 0.2;", "  p.tag = \"blue-root\";\n  for (let dx = -2; dx <= 2; dx++) {\n    const a = 1 - Math.abs(dx) * 0.2;"),
    ("  // a thread of smoke now and then off the tip\n", "  p.tag = \"smoke-thread\";\n  // a thread of smoke now and then off the tip\n"),
    ("  // motes drifting in the candlelight, catching it as they pass close\n", "  p.tag = \"dust-motes\";\n  // motes drifting in the candlelight, catching it as they pass close\n"),
    ("  // the hot core of the light\n", "  p.tag = \"core-light\";\n  // the hot core of the light\n"),
])

# ---- 火鉢・松明 (燃える所は共通の burn。 ここは道具の側だけ)
ann("fire/brazier.js", [
    ("  floorLight(p, 0, 0.5, 40, 7, GLOW, 0.32 * fl);\n  stand(p, I, H, fl);\n", "  p.tag = \"floor-light\";\n  floorLight(p, 0, 0.5, 40, 7, GLOW, 0.32 * fl);\n  p.tag = \"prop\";\n  stand(p, I, H, fl);\n"),
    ("  burn(p, t, TONGUES, {", "  p.tag = \"\";\n  burn(p, t, TONGUES, {"),
    ("    mid: () => {\n      coals(p, I, H, t, S);\n      bowl(p, I, H, fl);\n    },", "    mid: () => {\n      p.tag = \"coals\";\n      coals(p, I, H, t, S);\n      p.tag = \"prop\";\n      bowl(p, I, H, fl);\n    },"),
    ("  // sparks streaming up in the draught: fast streaks that slow, wander and cool to embers high above\n", "  p.tag = \"draught-sparks\";\n  // sparks streaming up in the draught: fast streaks that slow, wander and cool to embers high above\n"),
])
ann("fire/torch.js", [
    ("  plate(p, I, H, fl0);\n", "  p.tag = \"prop\";\n  plate(p, I, H, fl0);\n"),
    ("  burn(p, t, TONGUES, {", "  p.tag = \"\";\n  burn(p, t, TONGUES, {"),
    ("    mid: () => wrap(p, W, I, H, t),", "    mid: () => { p.tag = \"prop\"; wrap(p, W, I, H, t); },"),
])

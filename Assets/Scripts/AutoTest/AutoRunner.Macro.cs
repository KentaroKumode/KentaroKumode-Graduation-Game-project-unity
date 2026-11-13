using System.Collections.Generic;
using GameLoop;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// <b>Super の航行 = 層の回り方の計画 (2026-11-06)</b>。 旧来は「価値 ÷ 歩数 が最大の 1 点」へ向かう貪欲で、 どの層でも約 17 戦していた。
    ///
    /// <para><b>物差し</b>: 「このランのクリアの対数オッズ」の増減。 重みは実測 (Tools/macro_value.py・Optimal 10,000 ラン) から作る:
    /// HP の価値は層で大きく違う (ボス前の HP 75% 以上 vs 50〜75% で 深さ 5 は約 3 倍・深さ 3 はわずか)。
    /// 品の価値は深さが進むほど下がる。 金の価値はその深さの物価で品へ換算した値 (物価が 4 倍の終盤の金は安い)。</para>
    ///
    /// <para><b>計画</b>: 近い未使用の点から <see cref="MacroDepth"/> 手先までの回り方を全部試し、 価値の合計が最大の列の最初の点へ向かう。
    /// 手ごとに HP と金を動かすので「休んでから戦う」「稼いでから店へ」が比べられる。 どの回り方も得にならなければ層を出る。
    /// 歩くたびに立て直す。 種別の見えていない点は平均として扱い、 開示されていない情報は読まない。</para>
    /// </summary>
    public partial class AutoRunner
    {
        /// <summary>2026-11-10: 3 → 5 (上限が小さい時に「店 → 周辺 → 次の店」の鎖を組めるように)。</summary>
        private const int MacroDepth = 5;
        private const int MacroCandidates = 10;

        // 序数 (0 ファーム / 1〜5 = 深さ 1,3,5,6,7) ごとの重み。 対数オッズ。
        //   実測の係数そのままでは HP を重く見すぎ、 品が足りずヴェスカで負けた (観測の係数は「今の方策の下での」値で、 最終層の必要量を映さない)。
        //   掃引 (同じシード): HP ×1・品 ×1 = 10.7% → HP ×0.25・品 ×3 = 17.9% (基準 12.2%)。 この比で焼いた。
        private static readonly float[] MacroHpPer10 = { 0.000f, 0.025f, 0.015f, 0.058f, 0.030f, 0.025f };
        private static readonly float[] MacroItem    = { 0.90f, 0.90f, 0.78f, 0.66f, 0.48f, 0.30f };
        private static readonly float[] MacroPrice   = { 12f, 14f, 20f, 30f, 42f, 60f };
        /// <summary>金の価値の割引 (すぐ品に換えられるとは限らない)。</summary>
        private const float MacroGoldDiscount = 0.6f;
        /// <summary>死ぬことの損失 (対数オッズ)。 HP が少ないほど同じ敗北率でも危険なので、 敗北率に HP の余裕の逆数を掛ける。</summary>
        private const float MacroDeathCost = 3f;
        private const float MacroStepCost = 0.004f;

        /// <summary>種別が見えていない点を通常の戦闘の何倍として見積もるか (<see cref="MacroGain"/>・嵐の航行の着いた点のポイントの見込みも同じ重み)。</summary>
        private const float MacroUnknownWeight = 0.7f;

        /// <summary>toil = 〈徒労〉で通常の戦闘の報酬が止まっている残り (<see cref="NavToilRewardless"/>・0 = 止まっていない)。 計画の中で通常の戦闘に勝つと形に従って減る。
        /// pts / stormMul = 嵐の航行 (口 navStormAware) だけが使う: 計画の中の不安定化の累計の見込みと、 その点の戦闘が嵐の中で始まる時の被ダメの倍率 (0 = 嵐の外・口が切れている)。</summary>
        private struct MacroSim { public float hp; public float coins; public float supply; public float cap; public int toil; public float pts; public float stormMul; }

        /// <summary>次に向かう点。 どの回り方も得にならなければ -1 (層を出る)。
        /// <paramref name="sv"/> = 嵐の見方 (口 navStormAware・嵐の層だけ・null = 今までと同じ計算)。 渡された時は、 隠れたボスの点を読まず (出口の歩数は <see cref="StormNavView.ExitHops"/>)、
        /// 計画の値を「出口へ今すぐ向かう」との差で比べる: 回り方の値 = Σ(点の値 − 嵐の HP の損) − (回った後の出口の嵐の代償 − 今の出口の嵐の代償) (<see cref="StormExitCost"/>)。</summary>
        private int MacroTarget(GameManager gm, MapManager mm, FreeMapSim sim, FreeMapLayout L, int at, int[] hopsFromMe, RunState run, StormNavView sv = null)
        {
            var map = mm.CurrentMap;
            int layer = Mathf.Clamp(FloorPlan.OrdinalOf(run.Depth), 0, MacroHpPer10.Length - 1);
            var cands = new List<int>(MacroCandidates);
            foreach (var n in map.GetAllNodes())
            {
                if (n.index < 0 || n.index == at || n.index == (sv == null ? L.goal : sv.Boss) || n.index == L.start) continue;
                if (n.activated || sim.IsSunk(n.index) || hopsFromMe[n.index] == int.MaxValue) continue;
                cands.Add(n.index);
            }
            if (cands.Count == 0) return -1;
            cands.Sort((a, b) => hopsFromMe[a] != hopsFromMe[b] ? hopsFromMe[a].CompareTo(hopsFromMe[b]) : a.CompareTo(b));
            if (cands.Count > MacroCandidates) cands.RemoveRange(MacroCandidates, cands.Count - MacroCandidates);

            // 点どうしの歩数
            var dist = new Dictionary<int, int[]>(cands.Count + 1) { [at] = hopsFromMe };
            foreach (int c in cands) dist[c] = sim.HopsFrom(c);
            int[] toGoal = sv == null ? sim.HopsFrom(L.goal) : sv.ExitHops;

            var start = new MacroSim
            {
                hp = run.playerMaxHP > 0 ? (float)run.playerHP / run.playerMaxHP : 1f,
                coins = run.coins,
                supply = NavSupply(run, sim),      // 近道の残り (ただで動ける回数) を含める
                cap = run.provisionCap,
                toil = NavToilRewardless(run),     // 〈徒労〉(2026-11-14・口 navToilAware): 通常の戦闘の報酬を 0 と見る残り
                pts = sv != null ? sv.Points : 0,
            };
            // ── 嵐の航行 (2026-11-14・口 navStormAware): 判断ごとに 1 回だけ求める ──
            var storm = sv != null ? new StormCostCtx(this, sv, run, layer) : null;
            float exit0 = storm != null ? storm.ExitCost(at, start) : 0f;
            // 補給できる店 (まだ入っていない・種別が見えている)。 計画の中で寄った店は used で外れる
            var shops = new List<int>();
            foreach (var n in map.GetAllNodes())
                if (n.index >= 0 && !n.activated && n.revealed && !sim.IsSunk(n.index) && n.EffectiveType == TileType.Shop && !n.isFalseMerchant)
                    shops.Add(n.index);
            int fearR = navFearMacro > 0f ? FearRange() : 0;
            int[] fearHops = fearR > 0 ? FearFoeHops(sim, at) : null;   // [測定 2026-11-12・#35] 恐れ (計画の中の各点に掛ける)
            float best = 0f; int bestFirst = -1;
            var used = new bool[cands.Count];

            // ── 速度 (2026-11-10・結果は変えない): 判断ごとに 1 回だけ求めて使い回す ──
            //   ・終点がボスか (残す HP) ── 以前は並びを評価するたびに文字列の ID で終点を引き直していた
            //   ・候補の点 (MapNode) と、 そこからの歩数の表 ── 以前は毎回 文字列の ID / 辞書で引いていた
            //   ・店が候補の何番目か ── 以前は毎回リストを先頭から探していた
            float reserve = run.playerMaxHP * (LayerHasBoss(mm) ? 0.75f : 0.25f);
            int WalkSteps(float hpFrac) => run == null || run.playerMaxHP <= 0 ? 0
                : Mathf.Max(0, Mathf.FloorToInt(((hpFrac >= 0f ? hpFrac * run.playerMaxHP : run.playerHP) - reserve) / FreeMapParams.HpPerSupply));   // HpWalkSteps と同じ式
            var candNode = new MapNode[cands.Count];
            var candDist = new int[cands.Count][];
            for (int i = 0; i < cands.Count; i++) { candNode[i] = map.GetNode(map.NodeIdAt(cands[i])); candDist[i] = dist[cands[i]]; }
            var shopCi = new int[shops.Count];
            for (int k = 0; k < shops.Count; k++) shopCi[k] = cands.IndexOf(shops[k]);

            void Search(int from, int[] dFrom, MacroSim s, float acc, int first, int depth)
            {
                for (int i = 0; i < cands.Count; i++)
                {
                    if (used[i]) continue;
                    int c = cands[i];
                    int steps = dFrom[c];
                    if (steps == int.MaxValue) continue;
                    var ns = s;
                    ns.supply -= steps * FreeMapParams.MoveSupply;
                    if (toGoal[c] == int.MaxValue) continue;
                    // 2026-11-10: 物資が尽きた分は HP で払って歩く (1 歩 HP HpPerSupply)。 残す HP を割るなら行かない
                    if (ns.supply < 0)
                    {
                        ns.hp -= -ns.supply * FreeMapParams.HpPerSupply / Mathf.Max(1f, run.playerMaxHP);
                        ns.supply = 0;
                        if (WalkSteps(ns.hp) <= 0 && ns.hp * run.playerMaxHP < reserve) continue;
                    }
                    // 嵐の航行: 歩く間の嵐の HP (輪の縮みを歩数ごとに)・着いた点の戦闘が嵐の中か
                    float stormWalk = 0f;
                    if (storm != null && !storm.Walk(from, c, ref ns, out stormWalk)) continue;
                    float gain = MacroGain(candNode[i], layer, run, ref ns) - steps * MacroStepCost - stormWalk;
                    if (storm != null) storm.Arrive(candNode[i], ref ns);
                    if (fearHops != null) gain -= navFearMacro * FearCloseness(fearHops, c, fearR);
                    // 2026-11-09: 踏んだ後に「出口」か「まだ寄っていない補給できる店」まで歩けるか (店で買い足す前提の計画)
                    //   嵐の航行: 出口までの道で払う嵐の HP (見込み) を、 HP で歩ける分から先に引く
                    bool ok = ns.supply + WalkSteps(storm != null ? ns.hp - storm.ExitHpFrac(c, ns) : ns.hp) >= toGoal[c] * FreeMapParams.MoveSupply + FreeMapExitMargin;
                    if (!ok && ns.coins >= FreeMapParams.ProvisionPrice)
                    {
                        int[] dc = candDist[i];
                        for (int k = 0; k < shops.Count; k++)
                        {
                            int sh = shops[k];
                            if (sh == c) continue;
                            int ci = shopCi[k];
                            if (ci >= 0 && used[ci]) continue;
                            if (dc[sh] != int.MaxValue && ns.supply >= dc[sh] * FreeMapParams.MoveSupply) { ok = true; break; }
                        }
                    }
                    if (!ok) continue;
                    float total = acc + gain;
                    // 嵐の航行: 「ここで切り上げて出口へ」との差。 回った後の出口の代償が、 今すぐ出口へ向かう代償より増えた分を引く
                    float score = storm != null ? total - (storm.ExitCost(c, ns) - exit0) : total;
                    int f = first < 0 ? c : first;
                    if (score > best + 1e-6f) { best = score; bestFirst = f; }
                    if (depth + 1 < MacroDepth && ns.hp > 0f)
                    {
                        used[i] = true;
                        Search(c, candDist[i], ns, total, f, depth + 1);
                        used[i] = false;
                    }
                }
            }
            Search(at, hopsFromMe, start, 0f, -1, 0);
            if (storm != null) StormLastBest = best;
            return bestFirst;
        }

        /// <summary>[計装] 嵐の航行で、 直近の判断の一番良い回り方の値 (「出口へ今すぐ」との差・対数オッズ)。 0 以下なら出口へ切り上げた。</summary>
        private float StormLastBest;

        /// <summary>死の見込み (対数オッズ): 敗北率 × <see cref="MacroDeathCost"/> × HP の余裕の逆数 (<see cref="MacroFight"/> の死の項と同じ式)。</summary>
        private static float MacroDeathRisk(double lose, float hp)
        {
            float hp0 = Mathf.Max(0.05f, hp);
            return (float)lose * MacroDeathCost * Mathf.Clamp(0.8f / hp0, 0.5f, 4f);
        }

        /// <summary>
        /// 嵐の航行の代償 (2026-11-14・口 navStormAware)。 単位は Super の計画と同じ「クリアの対数オッズ」(<see cref="MacroFight"/> の「報酬 × 重み − 被ダメ − 死の見込み」)。
        /// <list type="bullet">
        /// <item><b>歩く間の嵐の HP</b>: 道の上で払う嵐の HP (<see cref="StormNavView.WalkCost"/>) × その層の HP の重み (<see cref="MacroHpPer10"/>)。 HP も減らす (後の戦闘の死の見込みに効く)。</item>
        /// <item><b>敵の〈嵐〉</b>: 着いた時に嵐の中の点の戦闘は、 被ダメと敗北率を 1 + 敵の攻撃の上乗せ (<see cref="FreeMapParams.StormEnemyAttack"/>) ÷ その層の 1 発の被ダメの見積もり
        ///       (<see cref="AutoRunner.EstimateFloorMaxHit"/>・ブロックを引いた最大の 1 発) 倍に見る (上乗せは 1 発ごとにほぼそのまま通るので、 被ダメ全体を 1 発の比で増やす見立て。
        ///       最大の 1 発で割るので小さめに出る)。</item>
        /// <item><b>着いた点で溜まるポイント</b>: <see cref="StormNavView.ExpectedArrivalPoints"/> (本体の表 + このランで見た戦闘 1 回あたりの激化 + 報酬の秘宝の率)。</item>
        /// <item><b>出口の代償</b> (<see cref="ExitCost"/>): その点・その累計から出口へ歩いた時の嵐の HP (見込み) × HP の重み + その HP の損でボス戦の死の見込みが増える分
        ///       (<see cref="MacroDeathRisk"/>・ボス戦の敗北率は学習 (EncounterDangerStats の 'B')・無ければ <see cref="MacroFight"/> と同じ 0.01)。
        ///       ポイントが溜まるほど輪が縮んで出口までの道が嵐に入るので、 取る代償はこの項の増え方に出る。</item>
        /// </list>
        /// 新しい手置きの重みは無い (重みは計画の既存の表と本体の定数だけ)。
        /// </summary>
        private sealed class StormCostCtx
        {
            private readonly StormNavView _sv;
            private readonly AutoRunner _r;
            private readonly RunState _run;
            private readonly float _maxHp, _wHp, _stormMul;
            private readonly double _bossLose, _esc;

            public StormCostCtx(AutoRunner r, StormNavView sv, RunState run, int layer)
            {
                _r = r; _sv = sv; _run = run;
                _maxHp = Mathf.Max(1f, run.playerMaxHP);
                _wHp = MacroHpPer10[layer] * 10f;
                float hit = Mathf.Max(FreeMapParams.StormEnemyAttack, EstimateFloorMaxHit(run));
                _stormMul = 1f + FreeMapParams.StormEnemyAttack / hit;
                if (!EncounterDangerStats.TryGetKind(layer, 'B', out _, out _bossLose)) _bossLose = 0.01;
                _esc = r.StormEscalationPerFight();
            }

            private static int P(float pts) => Mathf.RoundToInt(pts);

            /// <summary>from から c へ歩く。 嵐の HP を HP から引き、 累計を進め、 その損 (対数オッズ) を返す。 HP が尽きるなら false。</summary>
            public bool Walk(int from, int c, ref MacroSim s, out float loss)
            {
                int hp = _sv.WalkCost(from, c, P(s.pts), c == _sv.Boss, out int pEnd, out _);
                s.pts += pEnd - P(s.pts);
                MarkFight(c, ref s);
                loss = 0f;
                if (hp <= 0) return true;
                float frac = hp / _maxHp;
                s.hp -= frac;
                loss = _wHp * frac;
                return s.hp > 0f;
            }

            /// <summary>着いた点の戦闘が嵐の中で始まるなら被ダメの倍率を立てる (<see cref="MacroGain"/> の前)。</summary>
            public void MarkFight(int c, ref MacroSim s) => s.stormMul = _sv.InStormAt(c, P(s.pts)) ? _stormMul : 0f;

            /// <summary>着いた点で溜まるポイントを足す (<see cref="MacroGain"/> の後)。</summary>
            public void Arrive(MapNode n, ref MacroSim s)
            {
                s.stormMul = 0f;
                if (n == null) return;
                s.pts += (float)StormNavView.ExpectedArrivalPoints(n.EffectiveType, n.revealed, _esc,
                    CombatRewards.EliteDropRate, CombatRewards.NormalDropRate, MacroUnknownWeight);
            }

            /// <summary>点 c・状態 s から出口へ歩いた時の嵐の HP (最大 HP の割合・見込み)。</summary>
            public float ExitHpFrac(int c, MacroSim s)
            {
                var e = _sv.Exit(c, P(s.pts));
                return e.reachable ? (float)e.hp / _maxHp : 0f;
            }

            /// <summary>出口の代償 (対数オッズ): 出口までの嵐の HP × HP の重み + その分ボス戦の死の見込みが増える分。</summary>
            public float ExitCost(int c, MacroSim s)
            {
                float frac = ExitHpFrac(c, s);
                if (frac <= 0f) return 0f;
                return _wHp * frac + MacroDeathRisk(_bossLose, s.hp - frac) - MacroDeathRisk(_bossLose, s.hp);
            }
        }

        /// <summary>点を 1 つ踏んだ時の対数オッズの増減。 <paramref name="s"/> を踏んだ後の状態へ動かす。</summary>
        private float MacroGain(MapNode n, int layer, RunState run, ref MacroSim s)
        {
            if (n == null) return 0f;
            float wHp = MacroHpPer10[layer] * 10f;
            float wItem = MacroItem[layer];
            float wGold = wItem / MacroPrice[layer] * MacroGoldDiscount;

            if (!n.revealed)
            {
                // 種別が見えていない点: 通常の戦闘相当として見積もる
                return MacroFight(layer, 'N', null, wHp, wGold, 0f, ref s) * MacroUnknownWeight;
            }
            switch (n.EffectiveType)
            {
                case TileType.Battle:
                {
                    string key = n.encounterRevealed ? EncounterDangerStats.KeyOf(n, run.Depth) : null;
                    // 〈徒労〉(2026-11-14): 報酬が止まっていれば報酬のゴールドの見込みを 0 にする (被ダメ・死の見込みは残す = 正味で損)
                    bool noReward = s.toil > 0;
                    if (noReward) s.toil = ToilNavRule.AfterNormalWin(s.toil, GameHooks.ToilMode);
                    return MacroFight(layer, 'N', key, wHp, wGold, 0f, ref s, noReward);
                }
                case TileType.EliteBattle:
                {
                    string key = n.encounterRevealed ? EncounterDangerStats.KeyOf(n, run.Depth) : null;
                    return MacroFight(layer, 'E', key, wHp, wGold, CombatRewards.EliteDropRate * wItem, ref s);
                }
                case TileType.Rest:
                {
                    float healed = Mathf.Min(0.3f, 1f - s.hp);
                    s.hp += healed;
                    return wHp * healed + 0.02f;   // 満タンなら強化に使う
                }
                case TileType.Shop:
                {
                    // 物資を満タンまで買い足す (2026-11-09)。 残りの金で品を買う
                    float need = Mathf.Max(0f, s.cap - s.supply);
                    float afford = Mathf.Min(need, Mathf.Floor(s.coins / FreeMapParams.ProvisionPrice));
                    s.supply += afford; s.coins -= afford * FreeMapParams.ProvisionPrice;
                    float price = Mathf.Max(1f, MacroPrice[layer]);
                    float items = s.coins / price;
                    float g = items * wItem - s.coins * wGold;
                    s.coins = 0f;
                    return g;
                }
                case TileType.Treasure:
                    return wItem * (CombatRewards.TreasurePassiveRate + 0.5f * (1f - CombatRewards.TreasurePassiveRate));
                case TileType.Trap: return -0.05f;
                case TileType.Exchange: return 0.05f;
                case TileType.Event: return 0.02f;
                case TileType.SupplyDepot: return 0.02f;
                default: return 0f;
            }
        }

        /// <summary><paramref name="noReward"/> = 報酬が出ない戦闘 (〈徒労〉の通常の戦闘): 報酬のゴールドを 0 と見る (被ダメ・死の項はそのまま)。</summary>
        private static float MacroFight(int layer, char kind, string key, float wHp, float wGold, float bonus, ref MacroSim s, bool noReward = false)
        {
            double mean, lose;
            if (!(key != null && EncounterDangerStats.TryGet(key, out _, out mean, out _, out lose))
                && !EncounterDangerStats.TryGetKind(layer, kind, out mean, out lose))
            { mean = 0.05; lose = 0.01; }
            // 嵐の航行 (2026-11-14・口 navStormAware): 嵐の中で始まる戦闘は敵に〈嵐〉(攻撃の上乗せ) ── 被ダメと敗北率を倍率で見る (口が切れていれば 0 = 何もしない)
            if (s.stormMul > 0f) { mean *= s.stormMul; lose = System.Math.Min(1.0, lose * s.stormMul); }
            float gold = noReward ? 0f : kind == 'E' ? 20f : 10f;
            float hp0 = Mathf.Max(0.05f, s.hp);
            float death = (float)lose * MacroDeathCost * Mathf.Clamp(0.8f / hp0, 0.5f, 4f);
            s.hp = Mathf.Max(0f, s.hp - (float)mean);
            s.coins += gold;
            return gold * wGold + bonus - wHp * (float)mean - death;
        }
    }
}

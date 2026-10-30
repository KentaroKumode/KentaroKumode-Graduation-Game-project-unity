using System.Collections.Generic;
using GameLoop;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// 航行マップ (手番制・2026-10-18・docs/GAME.md §5) の BOT の航行。 <b>技量で方策が変わる</b> (§21):
    /// <list type="bullet">
    /// <item><b>Naive</b>: 行き先だけを決めて最短の道を歩く。 追跡者を避けない。</item>
    /// <item><b>Optimal / Super / Exact / Ultra</b>: 同じ行き先に加えて、 <b>見えている / 魔石に出ている追跡者を避ける</b>
    ///       ── 次の 1 歩を、 追跡者に近づかない隣へ振る・補足されたら囮罠を使う。</item>
    /// </list>
    /// どの方策も乱数を使わない。 追跡者の性質 (<see cref="FoeProfile"/>) は読まない (プレイヤーには明かされない)。
    ///
    /// <para>方針:</para>
    /// <list type="number">
    /// <item>物資が「出口までの費用 ＋ 余裕」を割ったら出口 (ボス / 門) へ。 1 層の行動回数の上限に達しても出口へ。</item>
    /// <item>それ以外は、 まだ使っていない点のうち <b>既存のマス評価 (Rank) を歩数で割った価値</b>が最大の点へ。
    ///       種別の見えていない点は平均的な価値として扱う (見えていない種別を覗かない)。</item>
    /// <item>追跡者に捕まったら HP が少なければ逃げる (<see cref="ShouldFleePursuer"/>)。</item>
    /// </list>
    /// <para>照明・偵察・待つは使わない。 乱数は使わない (GameRng を消費しない)。</para>
    /// </summary>
    public partial class AutoRunner
    {
        /// <summary>1 層で航行の判断 (移動の指示) を出せる回数の上限。 これを超えたら出口へ向かい、
        /// その倍を超えたら詰みとして打ち切る (バッチを無限に回さない)。</summary>
        public const int MaxFreeMapActionsPerFloor = 120;
        /// <summary>出口までの費用に足す物資の余裕。</summary>
        public const int FreeMapExitMargin = 150;
        /// <summary>種別の見えていない点の価値 (Rank の天井からの距離)。 通常の戦闘と同じくらい。</summary>
        private const float UnknownNodeDesire = 5f;
        /// <summary>Rank の天井。 これより悪い (大きい) 点は行く価値が無いとして候補から外す。</summary>
        private const float RankCeil = 10f;
        /// <summary>追跡者の戦闘で逃げる HP 割合。</summary>
        private const float PursuerFleeHpRatio = 0.45f;
        /// <summary>この歩数以内に追跡者が居ると分かっている点へは、 避けられる限り踏まない。</summary>
        private const int FoeAvoidHops = 2;
        /// <summary>囮罠を使う「最短で何手で捕まるか」の上限。</summary>
        private const int LureEtaThreshold = 3;

        private int _freeMapActions;
        private FreeMapLayout _freeMapLayout;

        /// <summary>航行の方策を戦闘の技量から切り離す。 -1 = 技量に従う (既定) / 0 = Naive / 1 以上 = 追跡者を避ける。
        /// 「航行の読みの深さ」と「戦闘の読みの深さ」を別々に測るためのつまみ。</summary>
        [System.NonSerialized] public int freeNavModeOverride = -1;
        /// <summary>[計装] 方策ごとの航行判断の回数・逃げた回数・出口へ向かった回数・囮罠を使った回数 (バッチ累計)。添字は 0 Naive / 1 回避あり。</summary>
        public static readonly long[] FreeNavDecisions = new long[2], FreeNavFlees = new long[2], FreeNavExits = new long[2], FreeNavLures = new long[2];

        /// <summary>この判断で追跡者を避ける方策か。</summary>
        private bool FreeNavAvoidsFoe()
        {
            if (freeNavModeOverride >= 0) return freeNavModeOverride > 0;
            return EffectiveWiringSkill != WiringSkill.Naive;
        }

        /// <summary>[計装] 航行マップの層で行動の上限に達した回数 (バッチ累計)。</summary>
        public static long FreeMapActionCapHits;
        /// <summary>[計装] 追跡者の戦闘から逃げた回数 (バッチ累計)。</summary>
        public static long FreeMapPursuerFlees;

        private bool DoNavigateFree(GameManager gm, MapManager mm)
        {
            var run = gm.Run;
            var sim = mm.Sim;
            var map = mm.CurrentMap;
            if (run == null || sim == null || map == null) { Finish(Outcome.Deadlock, "航行マップの層の状態が無い"); return true; }

            if (!ReferenceEquals(map.layout, _freeMapLayout)) { _freeMapLayout = map.layout; _freeMapActions = 0; }
            if (++_freeMapActions > 2 * MaxFreeMapActionsPerFloor)
            {
                Finish(Outcome.Deadlock, $"航行マップの層で行動の上限 ({2 * MaxFreeMapActionsPerFloor}) を超えた");
                return true;
            }
            if (_freeMapActions == MaxFreeMapActionsPerFloor + 1) FreeMapActionCapHits++;

            // ── 回復 (旧航行と同じ規則)。 物資の補充は店の「物資の包」だけ (DoShop) ──
            float hpRatio = run.playerMaxHP > 0 ? (float)run.playerHP / run.playerMaxHP : 1f;
            int floorHit = EstimateFloorMaxHit(run);
            float dangerTarget = FightBudgetDanger(run, floorHit);
            while (Consumables.TryUseBestHeal(run, dangerTarget)) { }
            hpRatio = run.playerMaxHP > 0 ? (float)run.playerHP / run.playerMaxHP : 1f;
            _curBossNear = false;

            var L = map.layout;
            int at = sim.Me;
            bool avoid = FreeNavAvoidsFoe();
            int mode = avoid ? 1 : 0;
            FreeNavDecisions[mode]++;
            var hopsFromMe = sim.HopsFrom(at);

            // ── 補足されている: 囮罠を使う (回避あり) ──
            if (avoid)
            {
                var eta = sim.Eta();
                if (eta.Valid && eta.turns <= LureEtaThreshold && !sim.HasLureAt(at)
                    && HasConsumable(run, ItemIds.Lure) && Consumables.Use(run, ItemIds.Lure))
                {
                    FreeNavLures[mode]++;
                    return false;   // 手番は使わないので、 同じ判断をもう一度する (囮罠は仕掛けた点に残る)
                }
            }

            // ── 行き先 ──
            int target = -1;
            bool toExit = _freeMapActions > MaxFreeMapActionsPerFloor
                          || run.provision < hopsFromMe[L.goal] * FreeMapParams.MoveSupply + FreeMapExitMargin
                          || hopsFromMe[L.goal] == int.MaxValue;
            if (!toExit)
            {
                float best = 0f;
                foreach (var n in map.GetAllNodes())
                {
                    if (n.index < 0 || n.index == at || n.index == L.goal || n.index == L.start) continue;
                    if (n.activated || sim.IsSunk(n.index)) continue;             // 使い切った点・沈んだ点
                    if (hopsFromMe[n.index] == int.MaxValue) continue;
                    float desire = !n.revealed ? UnknownNodeDesire
                                 : RankCeil - Rank(n, hpRatio, _curCombatAverse, false, run, dangerTarget);
                    if (desire <= 0f) continue;
                    float cost = hopsFromMe[n.index] * FreeMapParams.MoveSupply;
                    float value = desire / (1f + cost / 100f);
                    // 同点は添字の小さい方 (決定的)
                    if (value > best + 1e-6f) { best = value; target = n.index; }
                }
                if (target < 0) toExit = true;   // 近くの未使用の点が 1 つも無ければ出口へ
            }
            if (toExit) { target = L.goal; _curBossNear = true; FreeNavExits[mode]++; }

            // ── 次の 1 歩 ──
            int hop = sim.StepToward(at, target);
            if (avoid) hop = AvoidFoeStep(sim, at, target, hop);
            if (hop < 0 || hop == at || !sim.CanMoveTo(hop))
            {
                Finish(Outcome.Deadlock, $"航行マップの層で行き先 {target} へ動けない (Me={at})");
                return true;
            }
            if (!gm.MoveStep(map.NodeIdAt(hop)))
            {
                Finish(Outcome.Deadlock, $"航行マップの層で {hop} へ動けない");
                return true;
            }
            return false;
        }

        /// <summary>見えている追跡者 / 魔石に出ている方角を避けて、 次の 1 歩を選び直す。
        /// 既定の一歩 <paramref name="preferred"/> が安全ならそのまま。 危ないなら、 行き先までの歩数が増えすぎない範囲で
        /// 追跡者から遠い隣へ。 追跡者の位置が分からない時は、 魔石の方角に向かう隣を避ける。</summary>
        private int AvoidFoeStep(FreeMapSim sim, int at, int target, int preferred)
        {
            if (!sim.FoeAlive) return preferred;
            var stone = sim.Stone();
            bool foeKnown = sim.FoeVisible;
            if (!foeKnown && stone.level < 2) return preferred;   // 遠い / 反応なしなら避けない

            var toTarget = sim.HopsFrom(target);
            var nbrs = sim.Neighbors(at);
            int best = preferred; double bestScore = double.NegativeInfinity;
            double baseline = toTarget[at];
            foreach (int c in nbrs)
            {
                if (!sim.CanMoveTo(c)) continue;
                double danger;
                if (foeKnown)
                {
                    var dFoe = sim.HopsFrom(sim.FoeNode);
                    int step = dFoe[c] == int.MaxValue ? 99 : dFoe[c];
                    danger = step <= FoeAvoidHops ? (FoeAvoidHops + 1 - step) * 4.0 : 0.0;
                }
                else
                {
                    // 魔石の方角 (45° 刻み) との差が小さいほど、 追跡者に近づく隣
                    int b = sim.Bearing(at, c);
                    int diff = System.Math.Abs(b - stone.bearing); if (diff > 4) diff = 8 - diff;
                    danger = diff <= 1 ? (2 - diff) * 3.0 : 0.0;
                }
                double progress = toTarget[c] == int.MaxValue ? -50 : baseline - toTarget[c];   // 近づけば +1
                double score = progress * 2.0 - danger + (c == preferred ? 0.01 : 0.0);
                if (score > bestScore + 1e-9) { bestScore = score; best = c; }
            }
            return best;
        }

        private static bool HasConsumable(RunState run, string id)
        {
            if (run?.ownedConsumables == null) return false;
            foreach (var c in run.ownedConsumables) if (c == id) return true;
            return false;
        }

        /// <summary>追跡者の戦闘の頭で逃げるか: HP が少なければ逃げる。</summary>
        private static bool ShouldFleePursuer(RunState run, GameManager gm)
        {
            if (run == null || gm == null || !gm.InPursuerCombat || run.playerMaxHP <= 0) return false;
            return (float)run.playerHP / run.playerMaxHP < PursuerFleeHpRatio;
        }

        /// <summary>系統で最初に見つかった消耗品を使う (小さい Tier から)。 id を綴りで選ばない。</summary>
        private static bool UseFirstOfFamily(RunState run, string family)
        {
            if (run?.ownedConsumables == null) return false;
            string pick = null; int pickTier = int.MaxValue;
            foreach (var id in run.ownedConsumables)
            {
                if (ItemIds.ConsFamilyOf(id) != family) continue;
                int t = ItemIds.ConsTierOf(id);
                if (t < pickTier) { pickTier = t; pick = id; }
            }
            return pick != null && Consumables.Use(run, pick);
        }
    }
}

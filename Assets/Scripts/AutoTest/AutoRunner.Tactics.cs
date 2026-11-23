using System;
using System.Collections.Generic;
using GameLoop;
using CombatSystem;
using MapSystem;

namespace AutoTest
{
    /// <summary>
    /// AutoRunner: 戦術消耗品 (2026-11-20・item-overhaul-spec §7-1 / §12-6) の使い方。 配線の判断の頭 (<see cref="DeckWiringPlanPolicy"/>・護符と同じ口) で呼ぶ。
    ///
    /// <para><b>比べて使う 4 つ</b> (入れ替えの符・無銘の潤滑油・急所突きの針・倍化の共鳴玉): 使った場合の盤面を本体と同じ旗
    /// (TerminalBattleState の ignoreCond / doubleAll・針の印・手札の引き直し) で組み、 今ターンの最良の効用 (<see cref="BestDeckUtilityOver"/>・
    /// 配線と同じ列挙と HP の帳簿) を使わない場合と比べる。 差が「取っておく値」(<see cref="TacticKeepValue"/>) より大きい物のうち、 差が一番大きい 1 つだけ使う (1 ターン 1 つ)。</para>
    /// <para><b>規則の 4 つ</b>: 鎧割りの酸瓶 (戦闘の頭・エリートかボス・敵の防御 6 以上)・反転の砂 (ボス戦・エスカレーションの段 2 以上)・
    /// 複製の水銀 (ボス戦の頭・手札で素の点が一番高い端子)・目盛り削りの鑿 (ボス戦・鑿の削りだけで目盛りが割れ、 そのダウンが大技のチャージか大技のターンに重なり、
    /// 今ターンの最良の挿し方では割れない時)。</para>
    /// <para>乱数: 入れ替えの符の引き直しの見込みは専用の乱数 (盤面から決まる種) で引く。 GameRng は引かない。</para>
    /// </summary>
    public partial class AutoRunner
    {
        /// <summary>取っておく値 (HP 相当・仮・未測定): 次の店までの戦闘 1 つにつき / 次の店が見えていない時の戦闘の数 / 今の層にボスが控えている時の上乗せ。 ボス戦の中は 0。</summary>
        private const float TacticKeepPerFight = 2f, TacticKeepBossAhead = 6f;
        private const int TacticFightsNoShop = 3;
        /// <summary>鎧割りの酸瓶を使う敵の防御の下限 (§12-6)。</summary>
        private const int TacticAcidMinDefense = 6;
        /// <summary>反転の砂を使うエスカレーションの段の下限 (§12-6)。</summary>
        private const int TacticSandMinStage = 2;
        /// <summary>入れ替えの符の引き直しの標本の数。</summary>
        private const int TacticSwapSamples = 8;

        /// <summary>[計装] 戦術消耗品を使った数 (品ごと・バッチ累計)。</summary>
        public static readonly Dictionary<string, long> TacticUses = new Dictionary<string, long>();

        /// <summary>[計装] サマリーに出す行 (使っていなければ空)。 数はプロセスの累計 (ワーカーはバッチをまたいで足す)。</summary>
        public static string TacticUsesLine()
        {
            if (TacticUses.Count == 0) return "";
            var sb = new System.Text.StringBuilder("[戦術消耗品] 使った数:");
            foreach (var kv in TacticUses) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value);
            return sb.ToString();
        }

        private static bool HasTactic(RunState run)
        {
            if (run?.ownedConsumables == null) return false;
            foreach (var id in run.ownedConsumables) if (ItemIds.ConsFamilyOf(id) == ItemIds.ConsTacticFamily) return true;
            return false;
        }

        private static bool UseTactic(RunState run, string id, int target = -1)
        {
            if (!Consumables.Use(run, id, target)) return false;
            TacticUses.TryGetValue(id, out long n); TacticUses[id] = n + 1;
            return true;
        }

        /// <summary>取っておく値 (HP 相当)。 ボス戦の中は 0。 それ以外は 次の店までの戦闘の数 × <see cref="TacticKeepPerFight"/> (+ 今の層にボスが控えていれば <see cref="TacticKeepBossAhead"/>)。
        /// shortcut: 次の店までの戦闘の数は「今の層にまだ入っていない店が見えていれば 1・無ければ <see cref="TacticFightsNoShop"/>」の粗い近似。 道の上の戦闘を数えるなら航行の計画 (Macro) から読む。</summary>
        private static float TacticKeepValue(bool bossFight)
        {
            if (bossFight) return 0f;
            var mm = MapSystem.MapManager.Instance;
            bool shopAhead = false;
            var map = mm?.CurrentMap;
            if (map != null)
                foreach (var n in map.GetAllNodes())
                    if (!n.activated && n.revealed && n.EffectiveType == MapSystem.TileType.Shop && !n.isFalseMerchant) { shopAhead = true; break; }
            float v = (shopAhead ? 1 : TacticFightsNoShop) * TacticKeepPerFight;
            if (LayerHasBoss(mm)) v += TacticKeepBossAhead;
            return v;
        }

        /// <summary>配線の判断の頭で戦術消耗品を使う (素朴版は使わない)。 敵の防御を変えたら <paramref name="tele"/> の防御も写す (この後の配線の見積もりが読む)。</summary>
        private void UseTacticsThisTurn(int[] rolls, ref MutualTurnTelegraph tele)
        {
            if (CombatWiringSkill == WiringSkill.Naive || rolls == null || rolls.Length == 0) return;
            var run = GameManager.Instance?.Run;
            var cm = CombatManager.Instance;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            var enemy = cm?.CurrentEnemy;
            if (!HasTactic(run) || cm?.TerminalDeck == null || ctx == null || enemy == null) return;
            bool boss = BossIds.IsBoss(enemy.id);
            bool head = cm.CurrentCombatTurn <= 1;
            var own = run.ownedConsumables;

            // ---- 規則 ----
            if (head && own.Contains(ItemIds.TacticAcid) && ctx.enemyDefense >= TacticAcidMinDefense
                && (boss || MapSystem.MapManager.Instance?.CurrentNode?.EffectiveType.ToEnemyKind() == EnemyKind.Elite)
                && UseTactic(run, ItemIds.TacticAcid))
                tele.enemyDefense = ctx.enemyDefense;
            if (boss && own.Contains(ItemIds.TacticSand)
                && Escalation.EffectiveStage(Math.Max(1, ctx.currentTurn - (int)ctx.GetAccumulated(CombatManager.CoolantDelayKey))) >= TacticSandMinStage)
                UseTactic(run, ItemIds.TacticSand);
            if (boss && head && own.Contains(ItemIds.TacticMercury))
            {
                var dk = cm.TerminalDeck; int best = -1; float bw = float.NegativeInfinity;
                for (int s = dk.permanent.Count; s < dk.board.Count; s++)
                {
                    if (dk.state.IsStopped(s)) continue;
                    float w = TargetWorth(TerminalCards.Get(dk.board[s]), run);
                    if (w > bw) { bw = w; best = s; }
                }
                if (best >= 0) UseTactic(run, ItemIds.TacticMercury, best);
            }
            if (boss && own.Contains(ItemIds.TacticChisel) && Consumables.CanUse(run, ItemIds.TacticChisel) && ChiselBreaksIntoHeavy(rolls, tele, enemy))
                UseTactic(run, ItemIds.TacticChisel);

            // ---- 比べて使う (1 ターン 1 つ) ----
            bool oil = own.Contains(ItemIds.TacticOil) && Consumables.CanUse(run, ItemIds.TacticOil);
            bool orb = own.Contains(ItemIds.TacticOrb) && Consumables.CanUse(run, ItemIds.TacticOrb);
            bool needle = own.Contains(ItemIds.TacticNeedle) && Consumables.CanUse(run, ItemIds.TacticNeedle);
            bool swap = own.Contains(ItemIds.TacticSwap) && Consumables.CanUse(run, ItemIds.TacticSwap);
            if (!oil && !orb && !needle && !swap) return;
            var ev = PrepareDeckEval(rolls, tele);
            var dice = (int[])rolls.Clone(); Array.Sort(dice);      // BestDeckUtilityOver は昇順の出目を取る
            float baseU = BestDeckUtilityOver(in ev.st, ev.board, dice, in ev.env, ev.deck.state, in ev);
            float keep = TacticKeepValue(boss) * (ev.st.ledger ? 1f : ev.st.wDef);
            string pick = null; float bestGain = keep;
            void Try(string id, float u) { if (u - baseU > bestGain) { bestGain = u - baseU; pick = id; } }
            if (oil) { var s1 = ev.deck.state.Clone(); s1.ignoreCond = true; Try(ItemIds.TacticOil, BestDeckUtilityOver(in ev.st, ev.board, dice, in ev.env, s1, in ev)); }
            if (orb) { var s2 = ev.deck.state.Clone(); s2.doubleAll = true; Try(ItemIds.TacticOrb, BestDeckUtilityOver(in ev.st, ev.board, dice, in ev.env, s2, in ev)); }
            if (needle) { var ns = ev.st; ns.needleFirst = true; ns.critP1 = 1f; Try(ItemIds.TacticNeedle, BestDeckUtilityOver(in ns, ev.board, dice, in ev.env, ev.deck.state, in ev)); }
            if (swap) Try(ItemIds.TacticSwap, SwapExpectedUtility(in ev, dice, tele));
            if (pick != null) UseTactic(run, pick);
        }

        /// <summary>入れ替えの符を使った後の最良の効用の期待値 (手札を捨て札へ送り、 同じ枚数を引き直す・本体 <see cref="TerminalDeckState.SwapHand"/> と同じ順・写しで引く)。
        /// shortcut: 切り直しの時の山札の規則 (BeforeShuffle の引く枚数の上乗せ・采配の品) は見ない。 采配の品で差が出るなら SampleNextBoard と同じく BeforeShuffle を写しに掛ける。</summary>
        private float SwapExpectedUtility(in DeckEval ev, int[] dice, MutualTurnTelegraph tele)
        {
            var dk = ev.deck;
            int seed;
            unchecked
            {
                int h = _deckLaRunSeed * 1103515245 + 0x5A9;
                h = h * 31 + tele.turn; h = h * 31 + ev.st.enemyHp; h = h * 31 + ev.st.myHp;
                foreach (int r in dice) h = h * 31 + r;
                seed = h & 0x7FFFFFFF;
            }
            var rng = new System.Random(seed);
            double acc = 0;
            for (int j = 0; j < TacticSwapSamples; j++)
            {
                var draw = new List<string>(dk.draw);
                var discard = new List<string>(dk.discard); discard.AddRange(dk.hand);
                var perm = new List<string>(dk.permanent); var hand = new List<string>();
                int guard = 0;
                while (hand.Count < dk.hand.Count && guard++ < 64)
                {
                    if (draw.Count == 0) { if (discard.Count == 0) break; draw.AddRange(discard); discard.Clear(); }
                    int i = rng.Next(draw.Count);
                    var id = draw[i]; draw[i] = draw[draw.Count - 1]; draw.RemoveAt(draw.Count - 1);
                    var d = TerminalCards.Get(id);
                    if (d != null && d.permanent) perm.Add(id); else hand.Add(id);
                }
                var nb = new List<string>(perm.Count + hand.Count); nb.AddRange(perm); nb.AddRange(hand);
                var st = dk.state.Clone();
                if (st.stopped != null)      // 本体の RebuildBoard(perm0, 0): 止まった印は前からある常設の分だけ残る
                {
                    var sp = new bool[nb.Count];
                    for (int p = 0; p < dk.permanent.Count && p < st.stopped.Length; p++) sp[p] = st.stopped[p];
                    st.stopped = sp;
                }
                acc += BestDeckUtilityOver(in ev.st, nb, dice, in ev.env, st, in ev);
            }
            return (float)(acc / TacticSwapSamples);
        }

        /// <summary>目盛り削りの鑿の規則: 鑿の削り (通常攻撃として目盛りを見る) だけで目盛りが割れ、 次のターン (ダウンのターン) が大技のチャージか大技のターンで、
        /// 何もしなくても次がダウンではなく、 今ターンの最良の挿し方の主攻撃では割れない時。 割れ方は本体と同じ規則 (<see cref="DownForecast"/> → BossDown.ForecastTurn)。</summary>
        private bool ChiselBreaksIntoHeavy(int[] rolls, MutualTurnTelegraph tele, EnemyData enemy)
        {
            if (enemy.heavyPeriod <= 0 || !BossDown.IsChargeOrClimax(Escalation.TurnsToHeavy(enemy, tele.turn + 1))) return false;
            var ev = PrepareDeckEval(rolls, tele);
            var st = ev.st;
            if (st.downNotches == null || DownForecast(in st, st.enemyHp, 0, 0).nextDown) return false;
            int chisel = BossDown.ApplyTaken(Consumables.ChiselDamage, st.enemyDown && !st.enemyDownWeak);
            if (chisel >= st.enemyHp) return true;      // 鑿で倒せる
            if (DownForecast(in st, st.enemyHp, chisel, chisel).fullAdded <= 0) return false;
            var w = ChooseDeckWiring(rolls, tele, out _, out _);
            if (w == null) return true;
            EvalDeckWiring(in ev, rolls, w, out var outc, out _, out _);
            return !DownForecast(in st, st.enemyHp, outc.mainDmg, Math.Min(outc.effDmg, st.enemyHp)).nextDown;
        }
    }
}

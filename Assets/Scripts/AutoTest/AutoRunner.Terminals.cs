using System;
using System.Collections.Generic;
using UnityEngine;
using GameLoop;
using CombatSystem;

namespace AutoTest
{
    /// <summary>
    /// AutoRunner: 端子の山札 (ADR-0014) の BOT 方策 ── 挿し方と、 戦闘後の端子の報酬。
    ///
    /// <para><b>挿し方</b>: 場の端子 (常設 + 手札) への挿し方を <see cref="TerminalWiring.Enumerate"/> で全部出し、 それぞれ
    /// <see cref="TerminalCards.Resolve"/> (状態は写しに掛ける) で結果を出して、 <b>旧い配線と同じ Optimal の効用</b>
    /// (<see cref="OptimalUtilityCore"/>: 今ターンの被ダメ + 撃破までの被ダメの見込み / 旧の効用) に通して比べる。
    /// 端子の式は Resolve、 効用の式は OptimalUtilityCore の 1 か所ずつ ── ここは結果を「合計と個数」へ詰め直すのと、
    /// 先のターンに効く分の見積もり (下の定数) だけを持つ。</para>
    ///
    /// <para>旧い配線との違い: 2 ターン先読み (v3.lookahead2) とターンの記録 (turnAudit) は山札では通らない
    /// (旧い 3 種/4 種の端子の列挙が前提)。 山札の 2 ターン先読みは別の口 <see cref="deckLookahead"/> (#105・既定 OFF)。
    /// 配線のミス (wiringMistakeRate) は 2026-11-12 (#134) に「次点の挿し方を選ぶ」形で山札へつなぎ直した (Optimal だけ)。</para>
    /// </summary>
    public partial class AutoRunner
    {
        // =====================================================================
        //  見積もりの定数 (すべて仮・未測定)。 単位は 2 つ:
        //   「与ダメ相当」 … DeckDamageWorth を掛けて効用へ (帳簿なら 1 ダメージで減る先の被ダメ・旧の効用なら wAtk)
        //   「HP 相当」   … そのまま効用へ (帳簿の単位)。 先のターンの HP は DeckFutureHpWorth を掛ける (帳簿の ledgerFutureShare と同じ割引)
        // =====================================================================
        /// <summary>充電の価値の単位: 充電 3.5 (出目の平均) = 旧い配線の充電 1 本。 旧い効用の充電の項 (wChg × 2 × 本数) をそのまま使うため。</summary>
        private const float DeckChargePipsPerDie = 3.5f;
        /// <summary>次の攻撃に乗る攻撃 (〈充填〉〈受け太刀〉の溜め) を、 今の与ダメの 7 割と見る (次に攻撃するまでに戦闘が終わる・倒し過ぎる分)。</summary>
        private const float DeckFutureAttackShare = 0.7f;
        /// <summary>溜める端子の溜まり 1 あたりの価値 = 発動 1 回の価値 ÷ 必要量 × 0.5 (溜め切る前に戦闘が終わることが多い)。
        /// 発動した回の価値は別に数えるので、 発動で溜まりが減る分は負の溜まりとして相殺される (溜めるほど得・発動すれば満額)。</summary>
        private const float DeckGaugeShare = 0.5f;
        /// <summary>「残りターン」の見積もりの上限 (撃破までのターンを 1 ターンの与ダメの見込みで割った値を、 ここで頭打ち)。</summary>
        private const int DeckTurnsCap = 6;
        /// <summary>エスカレーションを 1 ターン遅らせる価値 = 敵の攻撃の 1 割 × 残りターン (1 段の伸びを 1 割と見る・先の HP なので割り引く)。
        /// 〈冷却弾〉(段階を 1 つ戻す) はその 2 倍と見る。</summary>
        private const float DeckEscStepShare = 0.1f, DeckRetreatTurns = 2f;
        /// <summary>次のターンに効く補助 (与ダメ相当): 条件を無視 (〈予熱器〉) 3 / 多く引く 1 枚につき 4 (〈配電盤〉は SwitchboardDraw 枚) / 手札に残す (〈保持器〉) 2。</summary>
        private const float DeckPreheatDmg = 3f, DeckDrawDmg = 4f, DeckRetainDmg = 2f;
        /// <summary>〈写し取り〉: 基本以外を複製 6 / 基本を複製 1。 〈焼き切り〉: 基本を抜く 6 (山札が締まる) / 基本以外を抜く −4 (与ダメ相当)。</summary>
        private const float DeckCopyGoodDmg = 6f, DeckCopyBasicDmg = 1f, DeckBurnBasicDmg = 6f, DeckBurnGoodDmg = -4f;
        /// <summary>別枠のダメージ (毒・臨界・出血) 1 あたり 0.5 与ダメ相当。 式は見ていない控えめな定数 (値付けの規則 4: 別枠は量を控えめに)。</summary>
        private const float DeckPoisonDmg = 0.5f, DeckHeatDmg = 0.5f, DeckBleedDmg = 0.5f;
        /// <summary>使い捨ての端子を使う費用 (この戦闘ではもう使えない): 攻撃の種類は与ダメ相当 8 (出目 2 個ぶん)・それ以外は HP 相当 3。
        /// 弱い目で起爆筒を切らず、 良い目・危ないターンに回すための足切り。</summary>
        private const float DeckDisposableDmgCost = 8f, DeckDisposableHpCost = 3f;
        /// <summary>使うとランの山札から抜ける端子 (〈応急箱〉) を使う費用の上乗せ (HP 相当・今の HP の単位・2026-11-12・仮・未測定)。
        /// 先の戦闘で使える 1 回の回復の見込み (出目の平均 3.5 × <see cref="TerminalCards.FirstAidMul"/>) の半分 ── 引けない・満タンで余る分を半分と見る。
        /// これを超える回復 (最大 HP で切った後の量) が出るターンにだけ使う。</summary>
        private const float DeckRunLeaveHpCost = 3.5f * TerminalCards.FirstAidMul * 0.5f;
        /// <summary>〈停止信号〉: 次のターンの敵の攻撃 × 0.5 を今の HP で見る (2026-11-12)。 次のターンはほぼ確実に来るので先の割引 (帳簿の 0.2) は掛けない。
        /// 半分にするのは、 止めなくてもブロックで一部は受けられるから。 旧: × 0.9 × 先の割引 で、 使い捨ての費用を超えず一度も使われなかった。</summary>
        private const float DeckStunShare = 0.5f;
        /// <summary>端子の「素の点」1 点 = 1 回使うたびの与ダメ相当 3.5 (出目 1 個の平均)。 写し取り・焼き切り・保持器の値付けに使う (2026-11-12)。
        /// 素の点は 基本 1 / コモン 2 / レア 3 なので、 コモン 1 枚は基本より 1 回あたり 3.5 ほど得、 という見方。</summary>
        private const float DeckCardDmgPerPoint = 3.5f;
        /// <summary>撃破のボーナス (HP 相当・恒久なので割り引かない): 最大 HP +1 = 1.5 / 研ぎ澄まし +1 = 3 / ゴールド 1 = 0.15 / 報酬の選択肢 +1 = 1.5。</summary>
        private const float DeckKillMaxHpWorth = 1.5f, DeckKillWhetWorth = 3f, DeckKillGoldWorth = 0.15f, DeckKillExtraChoiceWorth = 1.5f;

        /// <summary>未来の与ダメ 1 の価値。 帳簿: 1 ダメージで撃破が早まり減る先の被ダメ (ledgerFutureShare × 敵の攻撃 ÷ 1 ターンの与ダメの見込み)。 旧の効用: wAtk。</summary>
        private float DeckDamageWorth(in OptimalTurnState s)
            => s.ledger ? v3.ledgerFutureShare * Math.Max(0, FutureEnemyAtk(in s)) / Math.Max(1f, s.dFull) : s.wAtk;   // ダウンのターンは明けた後の攻撃で見る

        /// <summary>先のターンの HP 1 の価値。 帳簿: ledgerFutureShare (帳簿が先の被ダメに掛ける割引と同じ)。 旧の効用: wDef。</summary>
        private float DeckFutureHpWorth(in OptimalTurnState s) => s.ledger ? v3.ledgerFutureShare : s.wDef;

        /// <summary>1 ターンの与ダメの見込み (帳簿の dFull・無ければ 素の攻撃力 + 個数 × 3.5)。</summary>
        private static float DeckPerTurnDamage(in OptimalTurnState s, int k)
            => s.dFull > 0f ? s.dFull : Math.Max(1f, s.atkBase + k * 3.5f);

        private static int DeckEstTurns(in OptimalTurnState s, int k)
            => Mathf.Clamp(Mathf.CeilToInt(s.enemyHp / DeckPerTurnDamage(in s, k)), 1, DeckTurnsCap);

        /// <summary>端子の結果を、 効用の入力 (端子ごとの合計と個数 + 追加の効果 + 先の得) へ詰め直す。 盤面は読まない。</summary>
        private WiredSums DeckSums(in OptimalTurnState s, int[] rolls, List<string> board, TerminalOutcome o,
                                   TerminalBattleState before, TerminalBattleState after, in TerminalEnv env, bool deckHasCounter)
        {
            int k = rolls.Length;
            var ws = WiredSums.None();
            // ---- 今ターンの合計と個数 (本体 CombatManager.Turn の「5. 配線」と同じく、 端子の結果から) ----
            ws.aCount = o.attackDice; ws.bCount = o.blockDice;
            ws.aSum = o.attackAdd; ws.blockRaw = o.blockAdd; ws.chargeRaw = o.chargeAdd;
            for (int i = 0; i < k && i < o.kindOfDie.Length; i++)
                if (o.kindOfDie[i] == DiceTerminal.Block && rolls[i] > ws.maxBlockDie) ws.maxBlockDie = rolls[i];
            // 充電: 本体の順 (払う → 満たす → 足す・上限で止まる) で、 実際に増える量を出目の平均で割って「本数」に換える
            int cap = env.chargeCap > 0 ? env.chargeCap : 9999;
            int ch = Math.Max(0, env.charge - o.chargeSpend);
            if (o.chargeFill) ch = Math.Max(ch, cap);
            ch = Math.Min(cap, ch + o.chargeAdd);
            ws.chargeUnits = (ch - env.charge) / DeckChargePipsPerDie;
            // ---- 今ターンの追加の効果 (効用の Core が本体の位置で掛ける) ----
            ws.damageMul = o.damageMul; ws.outPctAdd = o.outgoingPctAdd; ws.critMultAdd = o.critMultAdd; ws.forceCrit = o.forceCrit;
            ws.ignoreDefense = o.ignoreDefense; ws.takenMul = o.takenMul; ws.enemyAtkMul = o.enemyAttackMul;
            ws.shield = o.shield - o.shieldSpend; ws.heal = o.heal; ws.selfDamage = o.selfDamage;

            // ---- 先のターンに効く分の見積もり (上の定数) ----
            float dw = DeckDamageWorth(in s), fw = DeckFutureHpWorth(in s);
            int turns = DeckEstTurns(in s, k);
            float escDelayWorth = DeckEscStepShare * Math.Max(0, s.enemyAtk) * turns * fw;
            float flat = 0f;
            // 次の攻撃に乗る攻撃 (〈充填〉が発動した分・〈受け太刀〉)。 今ターン攻撃して使った分は attackAdd に入っているので、 減った分は負で相殺
            int stored0 = before.storedAttack + before.storedAttackNext, stored1 = after.storedAttack + after.storedAttackNext;
            flat += (stored1 - stored0) * DeckFutureAttackShare * dw;
            // 溜まり (発動前の分)
            foreach (var kv in after.gauge)
            {
                before.gauge.TryGetValue(kv.Key, out int g0);
                int d = kv.Value - g0;
                if (d == 0) continue;
                float perPip = 0f;
                switch (kv.Key)
                {
                    case TerminalIds.Capacitor: perPip = TerminalCards.CapacitorAttack / (float)TerminalCards.CapacitorNeed * DeckFutureAttackShare * dw; break;
                    case TerminalIds.Arrester:  perPip = 0.5f * Math.Max(0, s.enemyAtk) / TerminalCards.ArresterNeedNow * fw; break;
                    case TerminalIds.Foundry:   perPip = k * turns / (float)TerminalCards.FoundryNeed * DeckFutureAttackShare * dw; break;
                    case TerminalIds.Coolant:   perPip = DeckRetreatTurns * escDelayWorth / TerminalCards.CoolantNeed; break;
                }
                flat += d * perPip * DeckGaugeShare;
            }
            // 〈鋳造口〉が発動した: この戦闘中の出目 +1 (個数 × 残りターン・攻撃に挿す割合を 7 割と見る)
            flat += (after.foundryBonus - before.foundryBonus) * k * turns * DeckFutureAttackShare * dw;
            // エスカレーションの段階を戻す (〈冷却弾〉・溜まり切った〈冷却材〉)
            if (o.retreatStage) flat += DeckRetreatTurns * escDelayWorth;
            // 次のターンの補助
            if (after.ignoreCondNext && !before.ignoreCondNext) flat += DeckPreheatDmg * dw;
            flat += Math.Max(0, after.extraDrawNext - before.extraDrawNext) * DeckDrawDmg * dw;
            // 〈主幹〉: 使うたび以後の攻撃 +N (残りターンの半分で使えると見る)
            flat += (after.mainsUses - before.mainsUses) * TerminalCards.MainsPerUse * turns * 0.5f * DeckFutureAttackShare * dw;
            if (o.enemyStunNext) flat += DeckStunShare * Math.Max(0, s.enemyAtk) * (s.ledger ? 1f : s.wDef);
            // 別枠のダメージ
            flat += (o.poison * DeckPoisonDmg + o.heat * DeckHeatDmg) * dw;
            if (o.attackDice > 0) flat += o.bleedOnHit * DeckBleedDmg * dw;
            // 〈計数器〉: 基本以外の発動 1 回 = 与ダメ +(10% ÷ 10 回) を残りターンずっと (山札に入っている時だけ効く・本体 DrawTerminalHand)
            if (deckHasCounter)
                flat += o.firedNonBasic * (TerminalCards.CounterPct / TerminalCards.CounterEvery) * DeckPerTurnDamage(in s, k) * turns * dw;
            // 手札の操作 (対象は ChooseDeckTargets が選んだ端子・選ばなければ左端の候補)。 2026-11-12: この戦闘で山札を引く回数から値付けする。
            //   旧: 定数 (写し取り 6 / 焼き切り 6 × 未来の与ダメの価値) で、 使い捨ての費用 (HP 3) をほぼ超えず、 発動率が 0.1〜0.2% だった。
            //   引く回数 = 残りターン × 手札 ÷ 山札の残り (この戦闘で同じ 1 枚をあと何回引くか・残りターンで頭打ち)。
            var runNow = GameManager.Instance?.Run;
            var dkNow = CombatManager.Instance?.TerminalDeck;
            int deckN = dkNow != null ? Math.Max(1, dkNow.Remaining) : 10;
            float draws = Math.Min(turns, turns * (float)TerminalCards.HandSize / deckN);
            float PerUse(int slot) => slot >= 0 && slot < board.Count
                ? DeckCardDmgPerPoint * Math.Max(0f, TargetWorth(TerminalCards.Get(board[slot]), runNow) - 1f) : 0f;
            // 次のターンにもう 1 回使える。 手札の持ち越し (TerminalCards.HandCarry) では挿さなかった端子は全部残るので、 残す効果に値打ちは無い
            if (o.retainTarget >= 0 && !TerminalCards.HandCarry) flat += (DeckRetainDmg + PerUse(o.retainTarget)) * dw;
            // 〈保持器〉(持ち越しの規則): 捨て札から戻す 1 枚 (方策と同じ選び方) を、 次のターンにもう 1 回使える分として見る
            if (o.recall)
            {
                var disc = dkNow?.discard;
                int rp = RecallBest(disc, runNow);
                if (rp >= 0) flat += (DeckRetainDmg + DeckCardDmgPerPoint * Math.Max(0f, TargetWorth(TerminalCards.Get(disc[rp]), runNow) - 1f)) * dw;
            }
            if (o.copyTarget >= 0 && o.copyTarget < board.Count) flat += draws * PerUse(o.copyTarget) * dw;
            if (o.burnTarget >= 0 && o.burnTarget < board.Count && runNow?.terminalDeck != null && runNow.terminalDeck.Count > 0)
            {
                // 抜いた分だけ、 山札の平均の端子を引く回数が増える (対象が平均より悪いほど得・良い端子を抜けば損)
                float avg = 0f; foreach (var id in runNow.terminalDeck) avg += TargetWorth(TerminalCards.Get(id), runNow);
                avg /= runNow.terminalDeck.Count;
                flat += draws * DeckCardDmgPerPoint * (avg - TargetWorth(TerminalCards.Get(board[o.burnTarget]), runNow)) * dw;
            }
            // 使い捨ての費用
            float hpUnit = s.ledger ? 1f : s.wDef;
            foreach (int slot in o.fired)
            {
                var d = TerminalCards.Get(board[slot]);
                if (d == null || !d.disposable) continue;
                // 使い捨てを失うのはこの戦闘の残りだけ (次の戦闘では山札に戻る)。 攻撃以外の費用は先の HP の価値で見る (2026-11-12・旧: 今の HP で見て高すぎた)
                flat -= d.kind == TerminalKind.Attack ? DeckDisposableDmgCost * dw : DeckDisposableHpCost * fw;
                // 使うとランの山札から抜ける端子 (〈応急箱〉・2026-11-12): 先の戦闘で使えた 1 回ぶんも失う。 今の HP の単位で引く
                //   (戦闘をまたいで HP は残るので、 先の戦闘の回復も今の HP と同じ重さ)。 小さな傷に切って捨てないための足切り。
                if (TerminalCards.LeavesRunDeck(d) && (runNow?.terminalDeck?.Count ?? 0) > TerminalCards.MinDeckSize)
                    flat -= DeckRunLeaveHpCost * hpUnit;
            }
            ws.specFlat = flat;

            // ---- 撃破のボーナス (今ターン撃破した時だけ Core が足す) ----
            float kill = 0f;
            if (o.Fired(board, TerminalIds.Harvester)) kill += TerminalCards.HarvesterMaxHp * DeckKillMaxHpWorth;
            if (o.Fired(board, TerminalIds.Whet))      kill += TerminalCards.WhetGain * DeckKillWhetWorth;
            if (o.Fired(board, TerminalIds.LootBox))   kill += TerminalCards.LootGold * DeckKillGoldWorth;
            if (o.Fired(board, TerminalIds.Stripper))  kill += DeckKillExtraChoiceWorth;
            ws.killBonus = kill * hpUnit;
            return ws;
        }

        /// <summary>山札の挿し方を選ぶ (読むだけ・山札の状態は写しに掛ける)。 戻り値 = 出目ごとの挿し先 (場の添字・−1 = 挿さない)。
        /// <paramref name="bestU"/> = 選んだ挿し方の効用 (リロールの値付けが読む)。 山札で戦っていなければ null。
        /// <paramref name="lookahead"/> = 実際の配線の判断の時だけ true (#105 の先読みを掛けてよい)。 振り直しの値付けと〈猫の手〉は false (重いので掛けない)。</summary>
        private int[] ChooseDeckWiring(int[] rolls, MutualTurnTelegraph tele, out float bestU, out int[] targets, bool lookahead = false, bool mayMistake = false)
        {
            bestU = float.NegativeInfinity; targets = null;
            var cm = CombatManager.Instance;
            var deck = cm?.TerminalDeck;
            int k = rolls?.Length ?? 0;
            if (deck == null || k == 0) return null;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            var board = deck.board;
            // 挑戦カード〈猫の手〉が先に挿した 1 組は固定 (外す挿し方を候補に入れない)。 振り直しの値付けの時は −1 (猫は配線の判断の頭で挿す)
            int catDie = cm.CatPawDie, catSlot = cm.CatPawSlot;

            // 素朴版: 本体の既定 (素朴な点) と同じ。 旧い配線の素朴版 (出目降順の本数分割) に当たる「深く考えない」打ち方。
            if (EffectiveWiringSkill == WiringSkill.Naive)
            {
                bestU = 0f;
                return TerminalWiring.Auto(board, rolls, cm.TerminalEnvNow(ctx, tele.enemyAttackValue), deck.state, catDie, catSlot);
            }

            var ev = PrepareDeckEval(rolls, tele);
            // 止まった端子 (ヴェスカ〈停滞する時間〉) へ挿す手は列挙しない (Resolve が外すので結果は「挿さない」と同じ)
            var cands = TerminalWiring.Enumerate(board, k, deck.state, catDie, catSlot);
            int n = cands.Count;
            var us = new float[n];
            var outs = new OptimalTurnOutcome[n];
            var tgts = new int[n][];
            var touts = new TerminalOutcome[n];
            var afters = new TerminalBattleState[n];
            for (int i = 0; i < n; i++)
            {
                us[i] = EvalDeckWiring(ev, rolls, cands[i], out outs[i], out touts[i], out tgts[i], out afters[i]);
            }
            var st = ev.st;

            int best = -1; float bu = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
                if (best < 0 || us[i] > bu) { best = i; bu = us[i]; }
            if (best < 0) return null;

            // [#105] 山札の 2 ターン先読み (deckLookahead・既定 OFF): 今の効用の上位 K 本だけに、 次のターンの最良効用の期待値を足して選び直す
            var la = lookahead ? BuildDeckLookahead(in ev, rolls, tele) : null;
            if (la != null && n > 1)
            {
                var order = new int[n];
                for (int i = 0; i < n; i++) order[i] = i;
                System.Array.Sort(order, (a, b) => { int c = us[b].CompareTo(us[a]); return c != 0 ? c : a.CompareTo(b); });   // 同点は列挙の順 (貪欲と同じ)
                int kk = Math.Min(n, Math.Max(1, deckLookaheadTopK));
                int laBest = -1; float laV = float.NegativeInfinity;
                for (int r = 0; r < kk; r++)
                {
                    int i = order[r];
                    float v = us[i] + DeckNextValue(in ev, la, rolls, touts[i], afters[i], in outs[i]);
                    if (laBest < 0 || v > laV) { laBest = i; laV = v; }
                }
                DeckLookaheadDecisions++;
                if (laBest != best && us[laBest] < us[best]) DeckLookaheadChanged++;   // 同点の入れ替えは数えない (上書き率 ≠ 強さ)
                // [#105 診断] 「挿し方に読みの深さがあるか」を数える。 貪欲の手 (best) と先読みの手 (laBest) の、 今の損と先の得。
                {
                    bool boss = BossIds.IsBoss(cm.CurrentEnemy.id);
                    var dg = boss ? _laDiagBoss : _laDiagOther;
                    dg.decisions++;
                    dg.cands += n;
                    // 貪欲の 1 位と 2 位 (効用が違う最初の手) の差 = 今のターンだけで手がどれだけ決まっているか
                    float second = float.NaN;
                    for (int r = 1; r < n; r++) if (us[order[r]] < us[order[0]] - 1e-4f) { second = us[order[r]]; break; }
                    if (float.IsNaN(second)) dg.allTied++;
                    else { float gap = us[order[0]] - second; if (gap >= 500f) dg.forced++; else dg.gapSum += gap; }
                    int tiedTop = 0;
                    for (int r = 0; r < n; r++) if (us[order[r]] >= us[order[0]] - 1e-4f) tiedTop++;
                    dg.tiedTopSum += tiedTop;
                    if (laBest != best && us[laBest] < us[best] - 1e-4f)
                    {
                        float nowLoss = us[best] - us[laBest];
                        float futGain = DeckNextValue(in ev, la, rolls, touts[laBest], afters[laBest], in outs[laBest])
                                      - DeckNextValue(in ev, la, rolls, touts[best], afters[best], in outs[best]);   // memo に載っているので軽い
                        if (nowLoss >= 500f || futGain >= 500f) dg.changedBig++;
                        else { dg.changed++; dg.nowLossSum += nowLoss; dg.futGainSum += futGain; }
                    }
                }
                if (!deckLookaheadProbe) best = laBest;   // 診断だけの時は手を変えない (基準のランと全件一致のまま数える)
            }

            bestU = us[best];
            // [計装] 旧い配線と同じ値を残す (防ぎ切りの規則の数え・主攻撃の見積もりの照合)
            var fbo = outs[best];
            _lastBestU = bestU;
            _lastFreeBlock = fbo.freeBlock && !fbo.killsNow; _lastKillsNow = fbo.killsNow; _lastExpTaken = fbo.taken;
            _lastPredOld = fbo.dmgOld; _lastPredNew = fbo.dmgNew; _lastPredA = fbo.aCount; _lastPredUsed = fbo.dmgUsed;
            _lastPredRaw = fbo.rawNew; _lastPredCritP = st.critP; _lastPredCritMul = st.critMulEff; _lastPredOutMul = st.outMul;
            // [#134] Optimal の配線のミス (wiringMistakeRate): 次点の挿し方を選ぶ。 計装 (上の _last*) は旧と同じく意図した最良の手のまま。
            //   Super・素朴版・Exact はここを通らない (判定の乱数も引かない)。
            if (mayMistake && wiringMistakeRate > 0f && EffectiveWiringSkill == WiringSkill.Optimal && n > 1
                && RollWiringMistake(rolls, tele))
            {
                int alt = PickMistakeIndex(us, outs, best, cm.PlayerHP);
                if (alt >= 0) { WiringMistakes++; best = alt; }
            }
            // [計装 2026-11-13] 選んだ手が攻撃の端子に 1 つも挿さないか / 攻撃の端子に挿せる手が候補にあったか (判断は変えない)
            _lastChosenA = outs[best].aCount;
            _lastAttackCandExists = false;
            for (int i = 0; i < n && !_lastAttackCandExists; i++) if (outs[i].aCount > 0) _lastAttackCandExists = true;
            targets = tgts[best];
            return cands[best];
        }

        /// <summary>山札の挿し方の効用の入力 (盤面を 1 回だけ読んだもの)。 <see cref="ChooseDeckWiring"/> と〈猫の手〉の評価 (<see cref="CatPawPick"/>) が使う。</summary>
        private struct DeckEval
        {
            public OptimalTurnState st;
            public TerminalEnv env;
            public List<string> board;
            public TerminalDeckState deck;
            public RunState run;
            public bool deckHasCounter;
        }

        /// <summary>今の盤面から <see cref="DeckEval"/> を組む (読むだけ)。 山札で戦っていない時は呼ばないこと。</summary>
        private DeckEval PrepareDeckEval(int[] rolls, MutualTurnTelegraph tele)
        {
            var cm = CombatManager.Instance;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            var run = GameManager.Instance?.Run;
            var ev = new DeckEval
            {
                deck = cm.TerminalDeck, board = cm.TerminalDeck.board, run = run,
                env = cm.TerminalEnvNow(ctx, tele.enemyAttackValue),
                st = BuildOptimalTurnState(rolls, tele),
                deckHasCounter = run?.terminalDeck != null && run.terminalDeck.Contains(TerminalIds.Counter),
            };
            // 会心の効果 (〈真空管〉〈照星〉〈急所読み〉) を旧の式でも見積もれるよう、 会心率と倍率は常に読む (readOut の切り替えは変えない)
            if (ctx != null && !ev.st.readOut && !ev.st.traceBoth) FillReadOut(ref ev.st, cm, ctx, run, tele.playerAttackPenalty);
            return ev;
        }

        /// <summary>挿し方 1 つの効用 (山札の状態は写しに掛ける・読むだけ)。 <paramref name="o"/> = 端子の結果・<paramref name="tgts"/> = 方策が選んだ対象 (null あり)。</summary>
        private float EvalDeckWiring(in DeckEval ev, int[] rolls, int[] cand, out OptimalTurnOutcome outc, out TerminalOutcome o, out int[] tgts)
            => EvalDeckWiring(in ev, rolls, cand, out outc, out o, out tgts, out _);

        /// <summary>同上。 <paramref name="after"/> = この挿し方で解いた後の端子の状態の写し (先読み #105 が次のターンを組むのに使う)。</summary>
        private float EvalDeckWiring(in DeckEval ev, int[] rolls, int[] cand, out OptimalTurnOutcome outc, out TerminalOutcome o, out int[] tgts,
                                     out TerminalBattleState after)
        {
            after = ev.deck.state.Clone();
            o = TerminalCards.Resolve(ev.board, cand, rolls, ev.env, after);
            // 〈写し取り〉〈焼き切り〉〈保持器〉が発動する挿し方なら、 対象を方策で選び直して解き直す (対象は今ターンの攻防を変えない)
            tgts = ChooseDeckTargets(ev.board, o, ev.run);
            if (tgts != null)
            {
                after = ev.deck.state.Clone();
                o = TerminalCards.Resolve(ev.board, cand, rolls, ev.env, after, tgts);
            }
            var ws = DeckSums(in ev.st, rolls, ev.board, o, ev.deck.state, after, in ev.env, ev.deckHasCounter);
            return OptimalUtilityCore(in ev.st, rolls.Length, in ws, out outc);
        }

        /// <summary>挑戦カード〈猫の手〉(#5) の評価 (<see cref="GameHooks.CatPawPick"/> の中身・<see cref="CatPawAdvisor"/> が Super の設定の写しで呼ぶ)。
        /// この評価で最良の挿し方を選び (<see cref="ChooseDeckWiring"/> と同じ列挙と効用)、 その中で「外すと効用が一番落ちる 1 組」を返す
        /// (選び方は本体の <see cref="TerminalWiring.MostValuablePair"/>)。 戻り値 <c>{ 出目, 場の添字 }</c>・挿す組が無ければ <c>{ -1, -1 }</c>・評価できなければ null。</summary>
        internal int[] CatPawPick(int[] rolls, MutualTurnTelegraph tele)
        {
            var cm = CombatManager.Instance;
            if (cm?.TerminalDeck == null || rolls == null || rolls.Length == 0) return null;
            var best = ChooseDeckWiring(rolls, tele, out _, out var targets);
            if (best == null) return null;
            var ev = PrepareDeckEval(rolls, tele);
            // 挿し方を直した後の値で数える (条件に合わず外れる組を猫が挿さないように)
            var fixedUp = TerminalCards.Resolve(ev.board, best, rolls, ev.env, ev.deck.state.Clone(), targets).slotOfDie;
            return TerminalWiring.MostValuablePair(fixedUp, w => EvalDeckWiring(ev, rolls, w, out _, out _, out _));
        }

        /// <summary>対象を取る端子の対象の方策 (読むだけ)。 写し取り = 山札に増やして一番得な端子 (素の点の最大) / 焼き切り = 一番要らない端子 (最小) /
        /// 保持器 = 次のターンに一番使いたい端子 (最大)。 点は報酬の素の点 (<see cref="RewardBaseScore"/>)・基本の端子は 1。 対象を取る端子が発動しなければ null。</summary>
        private static int[] ChooseDeckTargets(List<string> board, TerminalOutcome o, RunState run)
        {
            int[] res = null;
            // 〈保持器〉(手札の持ち越しの規則): 捨て札から次のターンに一番使いたい 1 枚 (素の点の最大・同点は後に捨てた方) を選ぶ (値は捨て札の添字)
            if (o.recall)
            {
                var disc = CombatManager.Instance?.TerminalDeck?.discard;
                int pick = RecallBest(disc, run);
                if (pick >= 0)
                    for (int p = 0; p < board.Count; p++)
                        if (board[p] == TerminalIds.Retainer && o.fired.Contains(p))
                        {
                            if (res == null) { res = new int[board.Count]; for (int q = 0; q < res.Length; q++) res[q] = -1; }
                            res[p] = pick;
                        }
            }
            for (int p = 0; p < board.Count; p++)
            {
                if (!TerminalCards.PicksTarget(board[p]) || !o.fired.Contains(p)) continue;
                var cand = TerminalCards.TargetCandidates(board, o, p);
                if (cand.Count == 0) continue;
                bool wantLow = board[p] == TerminalIds.BurnOut;
                int pick = -1; float bestSc = 0f;
                foreach (int s in cand)
                {
                    float sc = TargetWorth(TerminalCards.Get(board[s]), run);
                    if (pick < 0 || (wantLow ? sc < bestSc : sc > bestSc)) { pick = s; bestSc = sc; }
                }
                if (res == null) { res = new int[board.Count]; for (int q = 0; q < res.Length; q++) res[q] = -1; }
                res[p] = pick;
            }
            return res;
        }

        /// <summary>[〈保持器〉] 捨て札で素の点が最大の 1 枚の添字 (同点は後に捨てた方・空なら −1)。 先読みでも同じ選び方 (次のターンの効用での選び直しはしない・仮)。</summary>
        private static int RecallBest(List<string> disc, RunState run)
        {
            if (disc == null) return -1;
            int pick = -1; float best = 0f;
            for (int i = 0; i < disc.Count; i++)
            {
                float sc = TargetWorth(TerminalCards.Get(disc[i]), run);
                if (pick < 0 || sc >= best) { pick = i; best = sc; }
            }
            return pick;
        }

        /// <summary>対象の点: 基本の端子 1 (報酬の足切りと同じ見方)・それ以外は報酬の素の点。</summary>
        private static float TargetWorth(TerminalDef d, RunState run)
            => d == null ? 0f : d.basic ? 1f : RewardBaseScore(d, run);

        /// <summary>山札で戦う戦闘の配線方策 (<see cref="CombatManager.WiringPolicy"/> へ差す)。 前のターンの観測・計装の包みもここで持つ (旧い配線の包み MutualWiringPlanPolicy は 2026-11-12 に削除)。</summary>
        private WiringPlan DeckWiringPlanPolicy(int[] rolls, MutualTurnTelegraph tele)
        {
            RefreshMainCorr();   // [V3 mainCorr]
            PrepareSuperTurn(rolls, tele);
            int hpNow = CombatManager.Instance != null ? CombatManager.Instance.PlayerHP : -1;
            if (_fbPrevHp >= 0 && hpNow >= 0 && _fbPrevHp - hpNow > _fbPrevExpTaken) _fbUnavoidSeen = true;
            _lastFreeBlock = _lastKillsNow = false;
            _lastChosenA = -1; _lastAttackCandExists = false;
            var slots = ChooseDeckWiring(rolls, tele, out _, out var targets, lookahead: true, mayMistake: true);
            // 挑戦カード〈金で殴る〉(#6): 払う金額 (札が無ければ 0・とどめに合わせて挿し方を替えることがある)。 計装の見積もり (_lastPred*) も金の分で上書きする
            int gold = slots != null ? ChooseGoldPunch(rolls, tele, ref slots, ref targets) : 0;
            // [計装 2026-11-13] 攻撃の端子に挿さない手を選んだ割合 (素朴版は効用で選ばないので数えない)
            if (_lastChosenA >= 0)
            {
                NoAttackDecisions++;
                if (_lastChosenA == 0) { NoAttackChosen++; if (!_lastAttackCandExists) NoAttackNoChoice++; }
            }
            if (predTrace) BeginPredTrace(tele.turn);
            BeginMainCorrTurn(_lastPredA);
            _fbDecided++; if (_lastFreeBlock) _fbTurns++; if (_lastKillsNow) _fbKillTurns++;
            _fbPrevHp = hpNow; _fbPrevExpTaken = _lastExpTaken;
            return slots != null ? WiringPlan.OfSlots(slots, targets, gold) : default;      // null なら本体が既定 (TerminalWiring.Auto) で挿す
        }

        // =====================================================================
        //  挑戦カード〈金で殴る〉(#6・2026-11-12) の払い方 (効果の定義は ChallengeCards.GoldPunchAttack)
        // =====================================================================
        //  金の値段 = goldPunchHpPerGold (HP 相当 / 1G)。 既定 0.15 は〈戦利品箱〉の撃破ボーナスの値付け (DeckKillGoldWorth) と同じ ──
        //  この BOT が金を HP に換える率は 1 つにそろえる。 効用 (HP の帳簿) で「金で上げた攻撃が減らす被ダメ」と「払った金 × 値段」を比べ、 得な方を取る。
        //   A. とどめ: 攻撃する挿し方のうち、 所持金を全部払えば今ターン撃破できるものについて、 撃破に要る最小の金額 (二分探索) を払う案。
        //      撃破のターンは敵が攻撃しないので、 今ターンの被ダメと先の被ダメの見込みが全部消える ── 金の主な使い道はここ。
        //      とどめでは所持金を全部使ってよい (取り置き goldPunchReserve を割ってよい)。
        //   B. 上乗せ: 貪欲で選んだ挿し方のまま、 (所持金 − 取り置き) の 1/4・1/2・全部を払う案。 撃破しない攻撃は、 1 ダメージが減らす
        //      先の被ダメ (帳簿の ledgerFutureShare × 敵の攻撃 ÷ 1 ターンの与ダメ) が値段を超える時だけ得になる。
        //   C. 払わない。
        //  3 つの中で 効用 − 値段 × 払う金額 が最大の案。 計算量: 札を受けたランだけ、 1 判断で 挿し方 W 回の解き直し + 撃破できる挿し方ごとに二分探索 (≈ log2 所持金 回)。
        //  未決 (作者の判断): 金の値段を店の残り (最後の店の後は 0 に近い) で変えるか・最後の戦闘で使い切るか。 今はどちらも見ない。

        /// <summary>[#6] 〈金で殴る〉の金の値段 (HP 相当 / 1G・既定 0.15 = <see cref="DeckKillGoldWorth"/>)。 A/B で掃く口。</summary>
        public float goldPunchHpPerGold = DeckKillGoldWorth;
        /// <summary>[#6] 〈金で殴る〉の上乗せ (案 B) で残す所持金 (G・既定 40 = 店で端子を買う時の取り置き <see cref="TerminalShopReserve"/>)。 とどめ (案 A) には掛けない。</summary>
        public int goldPunchReserve = TerminalShopReserve;

        /// <summary>[#6 計装] 金を払った判断の数 / そのうち とどめ (案 A) / 払った金の累計。 ランをまたいで累計。</summary>
        public int GoldPunchDecisions { get; private set; }
        public int GoldPunchFinishers { get; private set; }
        public long GoldPunchPaid { get; private set; }

        /// <summary>払う金額を決める (読むだけ)。 とどめの案を選んだら <paramref name="slots"/> / <paramref name="targets"/> をその挿し方へ替える。 札が無い・素朴版・金が無いなら 0。</summary>
        private int ChooseGoldPunch(int[] rolls, MutualTurnTelegraph tele, ref int[] slots, ref int[] targets)
        {
            if (!MetaProgression.MetaBuffApplicator.HasGoldPunch() || EffectiveWiringSkill == WiringSkill.Naive || rolls == null) return 0;
            var run = GameManager.Instance?.Run;
            int coins = run?.coins ?? 0;
            var cm = CombatManager.Instance;
            if (coins <= 0 || cm?.TerminalDeck == null) return 0;
            var ev = PrepareDeckEval(rolls, tele);
            float price = goldPunchHpPerGold * (ev.st.ledger ? 1f : ev.st.wDef);   // 帳簿は HP の単位・旧の効用は守りの重みで HP を測る (killBonus と同じ)

            // C. 払わない (貪欲で選んだ挿し方のまま)
            ResolveDeckCand(in ev, rolls, slots, out var o0, out var a0, out var t0);
            int bestGold = 0; int[] bestSlots = slots, bestTgts = targets ?? t0;
            float bestV = EvalWithGold(in ev, rolls, o0, a0, 0, out var bestOut);
            bool finisher = false;

            // B. 上乗せ (攻撃する挿し方の時だけ・本体は攻撃しないターンに払わない)
            int spendable = Math.Max(0, coins - Math.Max(0, goldPunchReserve));
            if (o0.attackDice > 0 && spendable > 0)
                foreach (int g in new[] { spendable / 4, spendable / 2, spendable })
                {
                    if (g <= 0 || g == bestGold) continue;
                    float v = EvalWithGold(in ev, rolls, o0, a0, g, out var oc) - price * g;
                    if (v > bestV) { bestV = v; bestGold = g; bestOut = oc; }
                }

            // A. とどめ (全部払えば今ターン撃破できる挿し方だけ・撃破に要る最小の金額)
            var cands = TerminalWiring.Enumerate(ev.board, rolls.Length, ev.deck.state, cm.CatPawDie, cm.CatPawSlot);
            foreach (var c in cands)
            {
                ResolveDeckCand(in ev, rolls, c, out var o, out var a, out var tg);
                if (o.attackDice <= 0) continue;
                EvalWithGold(in ev, rolls, o, a, coins, out var full);
                if (full.effDmg < ev.st.enemyHp) continue;
                int lo = 0, hi = coins;      // 撃破できる最小の金額 (与ダメは金額に対して単調)
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    EvalWithGold(in ev, rolls, o, a, mid, out var om);
                    if (om.effDmg >= ev.st.enemyHp) hi = mid; else lo = mid + 1;
                }
                float v = EvalWithGold(in ev, rolls, o, a, lo, out var ol) - price * lo;
                if (v > bestV) { bestV = v; bestGold = lo; bestSlots = c; bestTgts = tg; bestOut = ol; finisher = lo > 0; }
            }

            if (bestGold <= 0) return 0;
            if (!ReferenceEquals(bestSlots, slots)) { slots = bestSlots; targets = bestTgts; }
            // [計装] 選んだ案の見積もり (主攻撃の照合・防ぎ切りの数え) を金の分で上書きする
            _lastKillsNow = bestOut.killsNow; _lastFreeBlock = bestOut.freeBlock && !bestOut.killsNow; _lastExpTaken = bestOut.taken;
            _lastPredOld = bestOut.dmgOld; _lastPredNew = bestOut.dmgNew; _lastPredA = bestOut.aCount; _lastPredUsed = bestOut.dmgUsed; _lastPredRaw = bestOut.rawNew;
            _lastChosenA = bestOut.aCount;
            GoldPunchDecisions++; if (finisher) GoldPunchFinishers++; GoldPunchPaid += bestGold;
            return bestGold;
        }

        /// <summary>挿し方 1 つを解く (端子の状態は写し・対象は方策で選び直す)。 <see cref="EvalDeckWiring"/> の前半と同じ。</summary>
        private void ResolveDeckCand(in DeckEval ev, int[] rolls, int[] cand, out TerminalOutcome o, out TerminalBattleState after, out int[] tgts)
        {
            after = ev.deck.state.Clone();
            o = TerminalCards.Resolve(ev.board, cand, rolls, ev.env, after);
            tgts = ChooseDeckTargets(ev.board, o, ev.run);
            if (tgts != null)
            {
                after = ev.deck.state.Clone();
                o = TerminalCards.Resolve(ev.board, cand, rolls, ev.env, after, tgts);
            }
        }

        /// <summary>解いた挿し方に <paramref name="gold"/> G を払った時の効用 (値段は引かない)。 金の攻撃は攻撃+N の枠 (本体の atkBase) へ入れる。</summary>
        private float EvalWithGold(in DeckEval ev, int[] rolls, TerminalOutcome o, TerminalBattleState after, int gold, out OptimalTurnOutcome outc)
        {
            var st = GoldPunchState(in ev.st, gold);
            var ws = DeckSums(in st, rolls, ev.board, o, ev.deck.state, after, in ev.env, ev.deckHasCounter);
            return OptimalUtilityCore(in st, rolls.Length, in ws, out outc);
        }

        /// <summary>金の攻撃を足した盤面 (旧の式の素の攻撃力と、 倍率を読む式の攻撃+N の両方へ・本体の atkBase と同じ位置)。
        /// 効果の定義を替えたらここも直す (<see cref="MetaProgression.ChallengeCards.GoldPunchAttack"/>)。</summary>
        private static OptimalTurnState GoldPunchState(in OptimalTurnState s, int gold)
        {
            var t = s;
            int a = MetaProgression.ChallengeCards.GoldPunchAttack(gold);
            if (a > 0) { t.atkBase += a; t.atkFlat += a; }
            return t;
        }

        // =====================================================================
        //  #105 山札の 2 ターン先読み (2026-11-12・既定 OFF)
        // =====================================================================
        //  旧い配線の E8 (v3.lookahead2・AutoRunner.Combat の LookaheadValue) を山札へ移したもの。 E8 と同じ形:
        //    挿し方 w の値 = 今ターンの効用 U_now(w) + 次のターンの最良効用の期待値 E[max U_next]。 割引なし・新しい重みなし。
        //    今ターンで撃破・死亡する挿し方は先読みせず U_now のまま。 振り直しは次のターンの見積もりに入れない。
        //  次のターンの盤面は w で今ターンを解いた後 (E8 と同じ NextTurnStateFrom) に、 山札の状態を足したもの:
        //    端子の状態 (溜まり・〈予熱器〉・〈配電盤〉の多く引く・〈受け太刀〉〈充填〉の溜め・〈鋳造口〉…) = Resolve の写し (after) に BeginTurn を掛ける
        //    山札 = 本体の FinishTurn (使い捨て・写し取り・焼き切り・保持) と DrawTurn (止まっていた手札を残す・捨てる・引く・常設は場へ) を写しで回す
        //    盤面の数 (充電・シールド・HP・毒・出血・敵の攻撃) = 今ターンの結果から (敵の攻撃は E8 と同じ公開規則の期待値)
        //  **先の情報は知らない**: 山の並び (GameRng で切った順) は読まず、 山の中身 (枚数と種類 = 人も数えられる) から一様に引く。
        //    次の出目も面から引く。 どちらも専用の System.Random (ランの番号・ターン・HP・出目から作る種) で、 GameRng を消費しない。
        //    同じ判断の中では、 候補ごとに同じ種の標本を使う (共通乱数 ── 候補の差だけが値の差に出る)。
        //  計算量の見積もり (1 判断):
        //    今: 挿し方 W ≈ 136 本 (場 5 枚・出目 3 個) を 1 回ずつ解く。
        //    先読み: 上位 K 本 × 標本 S × 次のターンの挿し方 W' (同じ目の入れ替えを除くと ≈ 100〜136) を解く = 既定 K 3 × S 6 × ≈ 130 ≈ 2,300 回。
        //    同じ次の盤面 (山札に効かない候補どうし) は memo で 1 回。 既定はボス戦だけ (E8 の lookaheadBossOnly と同じ) なので、
        //    1 ランの増分は ボス戦のターン数 (≈ 50) × 2,300 ≈ 12 万回の解決。 E8 (4 × 56 面 × 27 本 ≈ 6,000 回 / 判断) より軽い。

        /// <summary>[#105] 山札の 2 ターン先読みを使う (既定 OFF)。 技量が素朴版のときは使わない。 A/B の腕では <c>deckLookahead=True</c> と <c>_skill=Super</c>。</summary>
        public bool deckLookahead = false;
        /// <summary>[#105] 先読みをボス戦 (<see cref="BossIds.IsBoss"/>) だけに掛ける (既定 true・E8 の lookaheadBossOnly と同じ)。 false なら全戦闘。</summary>
        public bool deckLookaheadBossOnly = true;
        /// <summary>[#105] 先読みにかける候補の数 = 今の効用の上位 K 本 (既定 3)。</summary>
        public int deckLookaheadTopK = 3;
        /// <summary>[#105] 次のターンの (手札, 出目) の標本の数 (既定 6)。</summary>
        public int deckLookaheadSamples = 6;
        /// <summary>[#105 診断] true = 先読みを計算して数えるだけで、 手は変えない (貪欲の手のまま = 基準のランと同じ結果)。</summary>
        public bool deckLookaheadProbe = false;

        /// <summary>[#105 診断] 先読みを掛けた判断の内訳 (ランをまたいで累計)。</summary>
        private sealed class LaDiag
        {
            public long decisions, cands, tiedTopSum, allTied, forced, changed, changedBig;
            public double gapSum, nowLossSum, futGainSum;
            public string Line(string tag)
            {
                if (decisions == 0) return tag + " 判断 0";
                long free = decisions - allTied - forced;
                return $"{tag} 判断 {decisions} / 候補の平均 {(double)cands / decisions:0.0} / 1 位と同点の手の平均 {(double)tiedTopSum / decisions:0.0} "
                     + $"/ 全部同点 {allTied} ({100.0 * allTied / decisions:0.0}%) / 2 位との差が 500 以上 (生死) {forced} ({100.0 * forced / decisions:0.0}%) "
                     + $"/ 残りの 1 位と 2 位の差の平均 {(free > 0 ? gapSum / free : 0):0.00} "
                     + $"/ 先読みが手を変えた {changed} ({100.0 * changed / decisions:0.00}%) 今の損の平均 {(changed > 0 ? nowLossSum / changed : 0):0.00} 先の得の平均 {(changed > 0 ? futGainSum / changed : 0):0.00} "
                     + $"/ 生死がらみで変えた {changedBig} ({100.0 * changedBig / decisions:0.00}%)";
            }
        }
        private readonly LaDiag _laDiagBoss = new LaDiag(), _laDiagOther = new LaDiag();
        /// <summary>[#105 診断] サマリーに出す行 (先読みを掛けていなければ空)。</summary>
        public string DeckLookaheadDiagLine()
            => _laDiagBoss.decisions + _laDiagOther.decisions == 0 ? ""
             : "[先読みの診断] " + (deckLookaheadProbe ? "数えるだけ (手は貪欲のまま)" : "先読みの手を使う")
               + System.Environment.NewLine + "  " + _laDiagBoss.Line("ボス戦:")
               + System.Environment.NewLine + "  " + _laDiagOther.Line("それ以外:");

        /// <summary>[#105 計装] 先読みを掛けた判断の数 / そのうち今の効用が最良でない挿し方に変えた数 (同点の入れ替えは数えない)。 ランをまたいで累計。</summary>
        public int DeckLookaheadDecisions { get; private set; }

        /// <summary>[計装 2026-11-13] 直近の配線の判断で選んだ手の攻撃の端子の個数 (−1 = 効用で選んでいない) / 攻撃の端子に挿せる手が候補にあったか。</summary>
        private int _lastChosenA = -1;
        private bool _lastAttackCandExists;
        /// <summary>[計装 2026-11-13] 効用で選んだ配線の判断の数 / そのうち攻撃の端子に 1 つも挿さない手 / そのうち攻撃に挿せる手が候補に無かった数。 ランをまたいで累計。</summary>
        public int NoAttackDecisions { get; private set; }
        public int NoAttackChosen { get; private set; }
        public int NoAttackNoChoice { get; private set; }
        /// <summary>[計装 2026-11-13] サマリーに出す行 (判断が無ければ空)。</summary>
        public string NoAttackChoiceLine()
            => NoAttackDecisions == 0 ? ""
             : $"[攻撃なしの手] 配線の判断 {NoAttackDecisions} 回のうち、 攻撃の端子に挿さない手 {NoAttackChosen} 回 "
             + $"({100.0 * NoAttackChosen / NoAttackDecisions:0.00}%) / うち攻撃に挿せる手が無かった {NoAttackNoChoice} 回 "
             + $"/ 選べたのに選んだ {NoAttackChosen - NoAttackNoChoice} 回 ({100.0 * (NoAttackChosen - NoAttackNoChoice) / NoAttackDecisions:0.00}%)";
        public int DeckLookaheadChanged { get; private set; }

        /// <summary>[#105] 先読みの乱数の種のもと (ラン番号・ラン開始時に入れる)。</summary>
        private int _deckLaRunSeed;

        /// <summary>1 回の判断のあいだ変わらない、 先読みの前提と memo。</summary>
        private sealed class DeckLookahead
        {
            public NextTurnPlan plan;          // 次のターンの盤面の雛形 (E8 と同じ BuildNextTurnPlan)
            public int seed;                   // 標本 j の種 = seed + j × 7919
            public float stopChance;           // ヴェスカ〈停滞する時間〉の止まる確率 (持たない敵は 0)
            public readonly Dictionary<string, float> memo = new Dictionary<string, float>(8);
        }

        /// <summary>先読みの前提を組む。 掛けない (OFF・ボス戦でない・盤面が読めない) なら null。</summary>
        private DeckLookahead BuildDeckLookahead(in DeckEval ev, int[] rolls, MutualTurnTelegraph tele)
        {
            if (!deckLookahead || EffectiveWiringSkill == WiringSkill.Naive || deckLookaheadSamples <= 0) return null;
            var cm = CombatManager.Instance;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (cm?.CurrentEnemy == null || ctx == null || ev.deck == null) return null;
            if (deckLookaheadBossOnly && !BossIds.IsBoss(cm.CurrentEnemy.id)) return null;
            var la = new DeckLookahead
            {
                plan = BuildNextTurnPlan(in ev.st, cm, ctx, ev.run, rolls.Length),
                stopChance = ItemSystem.PassiveSkills.Effects.StagnantTime.StopChance(cm.CurrentEnemy),
            };
            unchecked
            {
                int h = _deckLaRunSeed * 1103515245 + 12345;
                h = h * 31 + tele.turn; h = h * 31 + ev.st.enemyHp; h = h * 31 + ev.st.myHp;
                foreach (int r in rolls) h = h * 31 + r;
                la.seed = h & 0x7FFFFFFF;
            }
            return la;
        }

        /// <summary>挿し方 1 つの、 次のターンの最良効用の期待値 (今ターンで撃破・死亡するなら 0)。 読むだけ。</summary>
        private float DeckNextValue(in DeckEval ev, DeckLookahead la, int[] rolls, TerminalOutcome o, TerminalBattleState after, in OptimalTurnOutcome outc)
        {
            var st = ev.st;
            int k = rolls.Length;
            // 敵の攻撃の前の HP とシールド (効用の Core と同じ: 払った HP は 1 未満にならない・回復は最大まで / 〈盾押し口〉で払う分を先に引く)
            int hpStart = Math.Min(st.myMaxHp, Math.Max(1, st.myHp - o.selfDamage) + o.heal);
            if (outc.effDmg >= st.enemyHp || outc.taken >= hpStart) return 0f;
            int pool = Math.Max(0, st.myShield - o.shieldSpend) + o.shield;
            var ns = NextTurnStateFrom(in st, la.plan, in outc, k, hpStart, pool, out int plusNext);
            TerminalWeights(in ns.play, la.plan.turn + 1, ns.enemyHp / la.plan.enemyMaxHp, out float wa, out float wd, out float wc);
            ns.wAtk = wa; ns.wDef = wd; ns.wChg = wc;

            // 次のターンの端子の盤面 (充電は DeckSums と同じ順: 払う → 満たす → 足す・上限で止まる)
            var env = ev.env;
            env.hp = ns.myHp; env.shield = ns.myShield; env.enemyAttack = ns.enemyAtk;
            int cap = env.chargeCap > 0 ? env.chargeCap : 9999;
            int ch = Math.Max(0, env.charge - o.chargeSpend);
            if (o.chargeFill) ch = Math.Max(ch, cap);
            env.charge = Math.Min(cap, ch + o.chargeAdd);
            env.enemyPoison += o.poison;                       // 毒の減衰は見ない (控えめな近似)
            if (o.attackDice > 0) env.enemyBleed += o.bleedOnHit;

            string key = DeckNextKey(in ns, plusNext, ev.board, o, after, in env);
            if (la.memo.TryGetValue(key, out float memoV)) return memoV;

            int samples = deckLookaheadSamples;
            var faces = la.plan.faces;
            double acc = 0;
            for (int j = 0; j < samples; j++)
            {
                var rng = new System.Random(unchecked(la.seed + j * 7919));
                var nst = after.Clone();
                int extra = nst.BeginTurn();
                // ダウンのターンは〈停滞する時間〉も止まる (本体 DrawTerminalHand と同じ)
                var nb = SampleNextBoard(ev.deck, ev.board, o, TerminalCards.HandSize + extra, ns.enemyDown ? 0f : la.stopChance, rng, out var stopped);
                nst.stopped = stopped;
                // 〈鋳造口〉の出目 +N は振った後に足される (本体 DrawTerminalHand → FoundryBonusKey)
                var dice = new int[k];
                for (int i = 0; i < k; i++) dice[i] = faces[rng.Next(faces.Length)] + nst.foundryBonus;
                System.Array.Sort(dice);
                acc += BestDeckUtilityOver(in ns, nb, dice, in env, nst, in ev);
            }
            float v = (float)(acc / samples);
            la.memo[key] = v;
            return v;
        }

        /// <summary>次のターンの盤面の memo の鍵 (同じ鍵なら次のターンの期待値は同じ)。 盤面の数・山札に効いた結果・端子の状態。</summary>
        private static string DeckNextKey(in OptimalTurnState ns, int plusNext, List<string> board, TerminalOutcome o, TerminalBattleState after, in TerminalEnv env)
        {
            var sb = new System.Text.StringBuilder(96);
            sb.Append(ns.enemyHp).Append(',').Append(ns.myHp).Append(',').Append(ns.myShield).Append(',').Append(ns.undyingStreak).Append(',')
              .Append(ns.steadfastReady ? 1 : 0).Append(',').Append(ns.eDefNow).Append(',').Append(ns.tele.blazeStacks * 2 + plusNext).Append(',')
              .Append(ns.enemyAtk).Append(',').Append(ns.enemyHitBonus).Append(',').Append(ns.enemyDown ? 1 : 0).Append(',').Append(ns.downBroken).Append('|')
              .Append(env.charge).Append(',').Append(env.enemyPoison).Append(',').Append(env.enemyBleed).Append('|');
            string At(int s) => s >= 0 && s < board.Count ? board[s] : "-";
            sb.Append(At(o.retainTarget)).Append(',').Append(At(o.copyTarget)).Append(',').Append(At(o.burnTarget)).Append(o.recall ? ",R" : "").Append('|');
            foreach (int s in o.fired) { var d = TerminalCards.Get(board[s]); if (d != null && d.disposable) sb.Append(board[s]).Append(','); }
            sb.Append('|').Append(after.oscLast).Append(',').Append(after.oscStreak).Append(',').Append(after.firedTotal).Append(',')
              .Append(after.ignoreCondNext ? 1 : 0).Append(',').Append(after.storedAttack).Append(',').Append(after.storedAttackNext).Append(',')
              .Append(after.extraDrawNext).Append(',').Append(after.foundryBonus).Append(',').Append(after.mainsUses).Append('|');
            if (after.gauge.Count > 0)
            {
                var keys = new List<string>(after.gauge.Keys); keys.Sort(StringComparer.Ordinal);
                foreach (var g in keys) sb.Append(g).Append('=').Append(after.gauge[g]).Append(',');
            }
            return sb.ToString();
        }

        /// <summary>次のターンの場 (常設 + 手札) を 1 つ引く (写しで回す・本体の山札は書き換えない・専用の乱数)。
        /// 本体の <see cref="TerminalDeckState.FinishTurn"/> → <see cref="TerminalDeckState.DrawTurn"/> と同じ順。 違いは、 山の並びを読まずに中身から一様に引くことだけ。
        /// <paramref name="stopped"/> = ヴェスカ〈停滞する時間〉で止まった場の端子 (null = なし)。</summary>
        private static List<string> SampleNextBoard(TerminalDeckState dk, List<string> board, TerminalOutcome o, int size, float stopChance,
                                                    System.Random rng, out bool[] stopped)
        {
            var draw = new List<string>(dk.draw);
            var discard = new List<string>(dk.discard);
            var hand = new List<string>(dk.hand);
            var perm = new List<string>(dk.permanent);
            // ---- FinishTurn: 残す 1 枚・写し取り (捨て札へ複製)・使い捨てと焼き切り (この戦闘から外す) ----
            string retain = o.retainTarget >= 0 && o.retainTarget < board.Count ? board[o.retainTarget] : null;
            if (o.copyTarget >= 0 && o.copyTarget < board.Count) discard.Add(board[o.copyTarget]);
            foreach (int s in o.fired)
            {
                var d = TerminalCards.Get(board[s]);
                if (d != null && d.disposable) hand.Remove(board[s]);
            }
            if (o.burnTarget >= 0 && o.burnTarget < board.Count) hand.Remove(board[o.burnTarget]);
            // ---- DrawTurn: 本体と同じ関数 (TerminalDeckState.SplitHand) で、 残す端子と捨て札へ送る端子に分ける ----
            //   手札の持ち越し (TerminalCards.HandCarry): 今ターンに挿した端子だけを捨て札へ・挿さなかった端子は残る。
            //   旧い規則: 今ターンに止まっていた手札は残す・残す 1 枚 (〈保持器〉)・それ以外は捨て札へ。
            var keep = new List<string>();
            TerminalDeckState.SplitHand(hand, board, perm.Count, dk.state.stopped, retain,
                                        TerminalDeckState.UsedFromHand(board, perm.Count, o), TerminalCards.HandCarry, keep, discard);
            // 〈保持器〉(持ち越しの規則): 本体と同じ選び方・同じ関数で、 捨て札から 1 枚を手札へ戻す
            if (o.recall && TerminalCards.HandCarry)
                TerminalDeckState.ApplyRecall(TerminalDeckState.RecallPick(dk.discard, RecallBest(dk.discard, GameManager.Instance?.Run)), keep, discard, size);
            hand.Clear(); hand.AddRange(keep);
            // ---- 引く (山が尽きたら捨て札を混ぜて続ける・常設は場へ出して引き直す・引いた物だけが止まる対象) ----
            var stopPerm = new List<int>(); var stopHand = new List<int>();
            int guard = 0;
            while (hand.Count < size && guard++ < 64)
            {
                if (draw.Count == 0)
                {
                    if (discard.Count == 0) break;
                    draw.AddRange(discard); discard.Clear();
                }
                int j = rng.Next(draw.Count);
                var id = draw[j]; draw[j] = draw[draw.Count - 1]; draw.RemoveAt(draw.Count - 1);
                bool stop = stopChance > 0f && rng.NextDouble() < stopChance;
                var d = TerminalCards.Get(id);
                if (d != null && d.permanent) { if (stop) stopPerm.Add(perm.Count); perm.Add(id); continue; }
                if (stop) stopHand.Add(hand.Count);
                hand.Add(id);
            }
            var nb = new List<string>(perm.Count + hand.Count);
            nb.AddRange(perm); nb.AddRange(hand);
            stopped = null;
            if (stopPerm.Count + stopHand.Count > 0)
            {
                stopped = new bool[nb.Count];
                foreach (int p in stopPerm) stopped[p] = true;
                foreach (int h in stopHand) stopped[perm.Count + h] = true;
            }
            return nb;
        }

        /// <summary>次のターンの場 <paramref name="board"/> と出目 <paramref name="dice"/> (昇順) での最良効用 (今ターンと同じ列挙・同じ効用)。
        /// 同じ目どうしを入れ替えただけの挿し方は 1 回だけ解く。 攻撃の端子を挿せる手が 1 つも無い場では「攻撃 0 本」の抑止を外して比べる
        /// (<see cref="OptimalTurnState.noAttackForced"/>)。</summary>
        private float BestDeckUtilityOver(in OptimalTurnState ns, List<string> board, int[] dice, in TerminalEnv env, TerminalBattleState state, in DeckEval ev)
        {
            int k = dice.Length;
            var cands = TerminalWiring.Enumerate(board, k, state);
            var sums = new List<WiredSums>(cands.Count);
            float best = float.NegativeInfinity;
            bool anyAttack = false;
            foreach (var w in cands)
            {
                bool dup = false;
                for (int i = 1; i < k && !dup; i++) if (dice[i] == dice[i - 1] && w[i] < w[i - 1]) dup = true;
                if (dup) continue;
                var after = state.Clone();
                var o = TerminalCards.Resolve(board, w, dice, env, after);
                var tg = ChooseDeckTargets(board, o, ev.run);
                if (tg != null) { after = state.Clone(); o = TerminalCards.Resolve(board, w, dice, env, after, tg); }
                var ws = DeckSums(in ns, dice, board, o, state, after, in env, ev.deckHasCounter);
                if (ws.aCount > 0) anyAttack = true;
                sums.Add(ws);
                float u = OptimalUtilityCore(in ns, k, in ws, out _);
                if (u > best) best = u;
            }
            if (sums.Count == 0) return 0f;
            if (!anyAttack)
            {
                var nf = ns; nf.noAttackForced = true;
                best = float.NegativeInfinity;
                foreach (var ws in sums)
                {
                    var w2 = ws;
                    float u = OptimalUtilityCore(in nf, k, in w2, out _);
                    if (u > best) best = u;
                }
            }
            return best;
        }

        // =====================================================================
        //  戦闘後の端子の報酬 (GameHooks.ChooseTerminalReward)
        // =====================================================================
        //  手書きの規則 (未測定):
        //   1. 端子ごとの素の点 (RewardBaseScore): レア 3・コモン 2・使い捨て 1.5、 弱い/状況次第の物は下げる。
        //      軸の端子は偏重 (軸の品の数) で決まる: 0 なら 0.3 (効果が 0 に近い)・1 以上は 1.5 + 0.75 × 偏重 (上限 5)。
        //   2. ビルドの型 (_currentPersona) に合う端子に +2 (PersonaTerminals)。
        //   3. 攻撃の種類の割合: 今 3 割未満なら攻撃の種類に +1.25。 攻撃以外を足して 3 割を割るなら −0.75。
        //      防御の種類の割合: 今 2 割未満なら防御の種類に +1.25。 防御以外を足して 2 割を割るなら −0.75。
        //      2026-11-11: 旧は「攻撃 4 割・+2.5 / −1.5」だけで、 防御の下限が無かった。 その方策で山札を組むとブロックに挿す出目が
        //      1 ターン 0.45 個 (main 0.82 個) まで減り、 6 層到達が 80% → 54% に落ちた (各 100 ラン・山札を変えない腕は 80%)。
        //      手札 5 枚では攻撃の種類を引けないターンは少ないので、 攻撃の下限を緩め、 防御にも下限を置く。
        //   4. 同じ端子をもう持っていれば 1 枚ごとに −1 (手札がそろって条件や連鎖が噛み合わなくなる)。
        //   5. 最良でも足切り (1.2 + 山札 10 枚を超えた 1 枚ごとに +0.1) に届かなければ受け取らない ── 弱い端子は、
        //      引くたびに強い端子を引く機会を削る (山札を弱くする)。 基本の端子 (攻撃・防御・充電) の価値を 1 前後と見た足切り。
        private const float RewardRejectBelow = 1.2f, RewardThresholdPerCard = 0.1f;
        private const int RewardThresholdFreeSize = 10;
        private const float RewardPersonaBonus = 2f, RewardMinAttackShare = 0.3f, RewardMinBlockShare = 0.2f, RewardAttackNeedBonus = 1.25f, RewardDilutePenalty = 0.75f, RewardDupPenalty = 1f;

        /// <summary>診断: 山札を初めの形のまま変えない (報酬を受け取らない・店で買わない・抜かない)。
        /// 「端子を取ると得か」の対照の腕。 並列の A/B (v3_arm.txt) で <c>terminalsLeaveDeck=True</c>。</summary>
        public bool terminalsLeaveDeck = false;

        /// <summary>報酬の候補 (端子の名前の並び) から 1 つ選ぶ。 戻り値 = 添字・負 = 受け取らない。</summary>
        private int ChooseTerminalReward(IReadOnlyList<string> offer)
        {
            if (offer == null || offer.Count == 0 || terminalsLeaveDeck) return -1;
            var run = GameManager.Instance?.Run;
            var deck = run?.terminalDeck;
            int total = deck?.Count ?? 0, atk = 0, blk = 0;
            if (deck != null) foreach (var id in deck) { var k = TerminalCards.Get(id)?.kind; if (k == TerminalKind.Attack) atk++; else if (k == TerminalKind.Block) blk++; }
            float shareNow = total > 0 ? atk / (float)total : 0f, blkShareNow = total > 0 ? blk / (float)total : 0f;
            var want = PersonaTerminals(_currentPersona);
            float threshold = RewardRejectBelow + RewardThresholdPerCard * Math.Max(0, total - RewardThresholdFreeSize);

            int best = -1; float bestScore = float.NegativeInfinity;
            for (int i = 0; i < offer.Count; i++)
            {
                var d = TerminalCards.Get(offer[i]);
                if (d == null) continue;
                float sc = RewardBaseScore(d, run);
                if (Array.IndexOf(want, d.id) >= 0) sc += RewardPersonaBonus;
                bool isAtk = d.kind == TerminalKind.Attack;
                if (shareNow < RewardMinAttackShare && isAtk) sc += RewardAttackNeedBonus;
                if (!isAtk && (atk / (float)(total + 1)) < RewardMinAttackShare) sc -= RewardDilutePenalty;
                bool isBlk = d.kind == TerminalKind.Block;
                if (blkShareNow < RewardMinBlockShare && isBlk) sc += RewardAttackNeedBonus;
                if (!isBlk && (blk / (float)(total + 1)) < RewardMinBlockShare) sc -= RewardDilutePenalty;
                int copies = 0;
                if (deck != null) foreach (var id in deck) if (id == d.id) copies++;
                sc -= RewardDupPenalty * copies;
                if (sc > bestScore) { bestScore = sc; best = i; }
            }
            return bestScore >= threshold ? best : -1;
        }

        /// <summary>端子の素の点 (規則 1)。</summary>
        private static float RewardBaseScore(TerminalDef d, RunState run)
        {
            if (d.axis != null)
            {
                int depth = ItemSystem.PassiveSkills.AxisRules.Count(run, d.axis);
                return depth <= 0 ? 0.3f : Math.Min(5f, 1.5f + 0.75f * depth);
            }
            switch (d.id)
            {
                case TerminalIds.Stripper:    return 1.5f;      // 倒したターンにしか効かない (2026-11-12: 1.0 では一度も取られなかった)
                case TerminalIds.Retainer:    return 2.0f;      // 出目をブロックに入れたうえで 1 枚残す (2026-11-12 の作り替えの後・旧 1.0)
                case TerminalIds.Heal:        return 1.2f;      // 3 以下だけ・攻撃にならない
                case TerminalIds.DischargeTube:
                case TerminalIds.Breaker:     return 1.3f;      // 充電を払う (充電の型なら下の +2 で上がる)
                case TerminalIds.Switchboard: return 1.5f;
            }
            if (d.rare) return d.disposable ? 2.5f : 3f;
            return d.disposable ? 1.5f : 2f;
        }

        // =====================================================================
        //  店と休憩で山札に足す・抜く (人と同じ GameManager の関数を呼ぶ・手書きの規則・未測定)
        // =====================================================================
        //   買う: 棚の候補を報酬と同じ点 (ChooseTerminalReward・足切り込み) で選び、 払った後に TerminalShopReserve G 残るなら買う。 1 店で棚の枚数まで。
        //   抜く: 基本の端子 (攻撃・防御・充電) が TerminalPruneBasicMin 枚以上で、 基本以外が TerminalPruneNonBasicMin 枚以上ある時だけ、 基本を 1 枚。
        //         どれを抜くか: 基本のうち枚数の多い物から。 ただし攻撃の基本は攻撃の種類が TerminalPruneAttackKeep 枚より多い時だけ、
        //         防御の基本は防御の種類が TerminalPruneBlockKeep 枚より多い時だけ (2026-11-11: 旧は防御・充電を先に抜いて守りが薄くなった)。
        //         店: 抜いた後に TerminalShopReserve G 残るなら 1 店 1 回。 休憩: HP が満ちていて回復が無駄になる時だけ (回復と択一)。
        private const int TerminalShopReserve = 40, TerminalPruneBasicMin = 3, TerminalPruneNonBasicMin = 2, TerminalPruneAttackKeep = 3, TerminalPruneBlockKeep = 2;

        /// <summary>店に入った時の端子の買い物と抜くサービス。 棚の品の買い物より先に呼ぶ。</summary>
        private void DoShopTerminals(GameManager gm)
        {
            var run = gm?.Run; if (run == null || terminalsLeaveDeck) return;
            // 買う
            for (int guard = 0; guard < GameManager.TerminalShopSlots; guard++)
            {
                var idx = new List<int>(); var ids = new List<string>();
                for (int i = 0; i < gm.TerminalShop.Count; i++)
                {
                    var s = gm.TerminalShop[i];
                    if (!s.sold && run.coins - s.price >= TerminalShopReserve) { idx.Add(i); ids.Add(s.id); }
                }
                if (ids.Count == 0) break;
                int pick = ChooseTerminalReward(ids);
                if (pick < 0 || !gm.ShopBuyTerminal(idx[pick])) break;
            }
            // 抜く
            int at = PruneTarget(run);
            if (at >= 0 && gm.CanRemoveTerminal && run.coins - gm.TerminalRemovePrice >= TerminalShopReserve) gm.ShopRemoveTerminal(at);
        }

        /// <summary>休憩: 回復が無駄になる時に、 基本が多ければ 1 枚抜く (休憩の行動を使う)。 抜いたら true。</summary>
        private bool TryRestRemoveTerminal(GameManager gm)
        {
            var run = gm?.Run; if (run == null || !gm.CanRemoveTerminal || terminalsLeaveDeck) return false;
            int at = PruneTarget(run);
            return at >= 0 && gm.RestRemoveTerminal(at);
        }

        /// <summary>抜く端子 (山札の添字・−1 = 抜かない)。 規則は上の注記。</summary>
        private static int PruneTarget(RunState run)
        {
            var deck = run?.terminalDeck;
            if (deck == null || deck.Count <= TerminalCards.MinDeckSize) return -1;
            int basic = 0, nonBasic = 0, atkKind = 0, blkKind = 0;
            var cnt = new Dictionary<string, int>();
            foreach (var id in deck)
            {
                var d = TerminalCards.Get(id); if (d == null) continue;
                if (d.kind == TerminalKind.Attack) atkKind++; else if (d.kind == TerminalKind.Block) blkKind++;
                if (d.basic) { basic++; cnt.TryGetValue(id, out int c); cnt[id] = c + 1; } else nonBasic++;
            }
            if (basic < TerminalPruneBasicMin || nonBasic < TerminalPruneNonBasicMin) return -1;
            string pick = null; int most = 0;
            foreach (var id in new[] { TerminalIds.Charge, TerminalIds.Attack, TerminalIds.Block })      // 同数なら充電 → 攻撃 → 防御 の順
            {
                if (id == TerminalIds.Attack && atkKind <= TerminalPruneAttackKeep) continue;
                if (id == TerminalIds.Block && blkKind <= TerminalPruneBlockKeep) continue;
                if (cnt.TryGetValue(id, out int c) && c > most) { most = c; pick = id; }
            }
            return pick != null ? deck.IndexOf(pick) : -1;
        }

        /// <summary>ビルドの型に合う端子 (規則 2)。 軸の端子は偏重の点で別に上がるので、 ここは軸の外の相性。</summary>
        private static string[] PersonaTerminals(BuildPersona p)
        {
            switch (p)
            {
                case BuildPersona.Charge:   return new[] { TerminalIds.Leak, TerminalIds.BigDischarge, TerminalIds.Battery, TerminalIds.Rectifier, TerminalIds.DischargeTube, TerminalIds.Breaker, TerminalIds.FullCharge };
                case BuildPersona.Shield:   return new[] { TerminalIds.Patch, TerminalIds.ShieldPush, TerminalIds.Insulator, TerminalIds.Parry, TerminalIds.Fork, TerminalIds.Preheater };
                case BuildPersona.Berserk:  return new[] { TerminalIds.Deathwish, TerminalIds.Detonator, TerminalIds.Relay, TerminalIds.Heal };
                case BuildPersona.Bludgeon: return new[] { TerminalIds.Transformer, TerminalIds.Amplifier, TerminalIds.TwinShot, TerminalIds.Twin, TerminalIds.Resonator };
                case BuildPersona.Standard: return new[] { TerminalIds.Twin, TerminalIds.Detector, TerminalIds.Igniter, TerminalIds.Mains, TerminalIds.Grinder };
                case BuildPersona.Crit:     return new[] { TerminalIds.Sight, TerminalIds.VitalRead, TerminalIds.VacuumTube };
                case BuildPersona.Bleed:    return new[] { TerminalIds.Barb, TerminalIds.BloodTap, TerminalIds.Relay };
                case BuildPersona.Poison:   return new[] { TerminalIds.Mister, TerminalIds.PoisonScale };
                case BuildPersona.Rinkai:   return new[] { TerminalIds.Stoker, TerminalIds.AfterHeat, TerminalIds.Foundry };
                default:                    return Array.Empty<string>();
            }
        }
    }
}

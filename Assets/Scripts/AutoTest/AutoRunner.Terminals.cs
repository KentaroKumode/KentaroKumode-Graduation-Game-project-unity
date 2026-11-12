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
    /// <para>旧い配線との違い: 2 ターン先読み (v3.lookahead2) と配線のミス (wiringMistakeRate)・ターンの記録 (turnAudit) は山札では通らない
    /// (どちらも旧い 3 種/4 種の端子の列挙が前提)。</para>
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
            => s.ledger ? v3.ledgerFutureShare * Math.Max(0, s.enemyAtk) / Math.Max(1f, s.dFull) : s.wAtk;

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
                    case TerminalIds.Arrester:  perPip = 0.5f * Math.Max(0, s.enemyAtk) / TerminalCards.ArresterNeed * fw; break;
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
            if (o.retainTarget >= 0) flat += (DeckRetainDmg + PerUse(o.retainTarget)) * dw;          // 次のターンにもう 1 回使える
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
        /// <paramref name="bestU"/> = 選んだ挿し方の効用 (リロールの値付けが読む)。 山札で戦っていなければ null。</summary>
        private int[] ChooseDeckWiring(int[] rolls, MutualTurnTelegraph tele, out float bestU, out int[] targets)
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
            for (int i = 0; i < n; i++)
            {
                us[i] = EvalDeckWiring(ev, rolls, cands[i], out outs[i], out _, out tgts[i]);
            }
            var st = ev.st;

            int best = -1; float bu = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
                if (best < 0 || us[i] > bu) { best = i; bu = us[i]; }
            if (best < 0) return null;

            bestU = us[best];
            // [計装] 旧い配線と同じ値を残す (防ぎ切りの規則の数え・主攻撃の見積もりの照合)
            var fbo = outs[best];
            _lastBestU = bestU;
            _lastFreeBlock = fbo.freeBlock && !fbo.killsNow; _lastKillsNow = fbo.killsNow; _lastExpTaken = fbo.taken;
            _lastPredOld = fbo.dmgOld; _lastPredNew = fbo.dmgNew; _lastPredA = fbo.aCount; _lastPredUsed = fbo.dmgUsed;
            _lastPredRaw = fbo.rawNew; _lastPredCritP = st.critP; _lastPredCritMul = st.critMulEff; _lastPredOutMul = st.outMul;
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
        {
            var after = ev.deck.state.Clone();
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

        /// <summary>対象の点: 基本の端子 1 (報酬の足切りと同じ見方)・それ以外は報酬の素の点。</summary>
        private static float TargetWorth(TerminalDef d, RunState run)
            => d == null ? 0f : d.basic ? 1f : RewardBaseScore(d, run);

        /// <summary>山札で戦う戦闘の配線方策 (<see cref="CombatManager.WiringPolicy"/> へ差す)。 包みの処理は MutualWiringPlanPolicy と同じ。</summary>
        private WiringPlan DeckWiringPlanPolicy(int[] rolls, MutualTurnTelegraph tele)
        {
            RefreshMainCorr();   // [V3 mainCorr]
            PrepareSuperTurn(rolls, tele);
            int hpNow = CombatManager.Instance != null ? CombatManager.Instance.PlayerHP : -1;
            if (_fbPrevHp >= 0 && hpNow >= 0 && _fbPrevHp - hpNow > _fbPrevExpTaken) _fbUnavoidSeen = true;
            _lastFreeBlock = _lastKillsNow = false;
            var slots = ChooseDeckWiring(rolls, tele, out _, out var targets);
            // 挑戦カード〈金で殴る〉(#6): 払う金額 (札が無ければ 0・とどめに合わせて挿し方を替えることがある)。 計装の見積もり (_lastPred*) も金の分で上書きする
            int gold = slots != null ? ChooseGoldPunch(rolls, tele, ref slots, ref targets) : 0;
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

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
        /// <summary>次の攻撃に乗る攻撃 (〈蓄電器〉〈受け太刀〉の溜め) を、 今の与ダメの 7 割と見る (次に攻撃するまでに戦闘が終わる・倒し過ぎる分)。</summary>
        private const float DeckFutureAttackShare = 0.7f;
        /// <summary>溜める端子の溜まり 1 あたりの価値 = 発動 1 回の価値 ÷ 必要量 × 0.5 (溜め切る前に戦闘が終わることが多い)。
        /// 発動した回の価値は別に数えるので、 発動で溜まりが減る分は負の溜まりとして相殺される (溜めるほど得・発動すれば満額)。</summary>
        private const float DeckGaugeShare = 0.5f;
        /// <summary>「残りターン」の見積もりの上限 (撃破までのターンを 1 ターンの与ダメの見込みで割った値を、 ここで頭打ち)。</summary>
        private const int DeckTurnsCap = 6;
        /// <summary>エスカレーションを 1 ターン遅らせる価値 = 敵の攻撃の 1 割 × 残りターン (1 段の伸びを 1 割と見る・先の HP なので割り引く)。
        /// 〈冷却弾〉(段階を 1 つ戻す) はその 2 倍と見る。</summary>
        private const float DeckEscStepShare = 0.1f, DeckRetreatTurns = 2f;
        /// <summary>次のターンに効く補助 (与ダメ相当): 条件を無視 (〈予熱器〉) 3 / 1 枚多く引く (〈配電盤〉) 4 / 手札に残す (〈保持器〉) 2。</summary>
        private const float DeckPreheatDmg = 3f, DeckDrawDmg = 4f, DeckRetainDmg = 2f;
        /// <summary>〈写し取り〉: 基本以外を複製 6 / 基本を複製 1。 〈焼き切り〉: 基本を抜く 6 (山札が締まる) / 基本以外を抜く −4 (与ダメ相当)。</summary>
        private const float DeckCopyGoodDmg = 6f, DeckCopyBasicDmg = 1f, DeckBurnBasicDmg = 6f, DeckBurnGoodDmg = -4f;
        /// <summary>別枠のダメージ (毒・臨界・出血) 1 あたり 0.5 与ダメ相当。 式は見ていない控えめな定数 (値付けの規則 4: 別枠は量を控えめに)。</summary>
        private const float DeckPoisonDmg = 0.5f, DeckHeatDmg = 0.5f, DeckBleedDmg = 0.5f;
        /// <summary>使い捨ての端子を使う費用 (この戦闘ではもう使えない): 攻撃の種類は与ダメ相当 8 (出目 2 個ぶん)・それ以外は HP 相当 3。
        /// 弱い目で起爆筒を切らず、 良い目・危ないターンに回すための足切り。</summary>
        private const float DeckDisposableDmgCost = 8f, DeckDisposableHpCost = 3f;
        /// <summary>〈停止信号〉: 次のターンの敵の攻撃 × 0.9 (先の HP なので割り引く)。</summary>
        private const float DeckStunShare = 0.9f;
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
            ws.shield = o.shield; ws.heal = o.heal; ws.selfDamage = o.selfDamage;

            // ---- 先のターンに効く分の見積もり (上の定数) ----
            float dw = DeckDamageWorth(in s), fw = DeckFutureHpWorth(in s);
            int turns = DeckEstTurns(in s, k);
            float escDelayWorth = DeckEscStepShare * Math.Max(0, s.enemyAtk) * turns * fw;
            float flat = 0f;
            // 次の攻撃に乗る攻撃 (〈蓄電器〉が発動した分・〈受け太刀〉)。 今ターン攻撃して使った分は attackAdd に入っているので、 減った分は負で相殺
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
                    case TerminalIds.Coolant:   perPip = escDelayWorth / TerminalCards.CoolantNeed; break;
                }
                flat += d * perPip * DeckGaugeShare;
            }
            // 〈鋳造口〉が発動した: この戦闘中の出目 +1 (個数 × 残りターン・攻撃に挿す割合を 7 割と見る)
            flat += (after.foundryBonus - before.foundryBonus) * k * turns * DeckFutureAttackShare * dw;
            // エスカレーションの遅れ (〈冷却材〉) / 段階を戻す (〈冷却弾〉)
            flat += o.escalationDelay * escDelayWorth;
            if (o.retreatStage) flat += DeckRetreatTurns * escDelayWorth;
            // 次のターンの補助
            if (after.ignoreCondNext && !before.ignoreCondNext) flat += DeckPreheatDmg * dw;
            flat += Math.Max(0, after.extraDrawNext - before.extraDrawNext) * DeckDrawDmg * dw;
            if (o.enemyStunNext) flat += DeckStunShare * Math.Max(0, s.enemyAtk) * fw;
            // 別枠のダメージ
            flat += (o.poison * DeckPoisonDmg + o.heat * DeckHeatDmg) * dw;
            if (o.attackDice > 0) flat += o.bleedOnHit * DeckBleedDmg * dw;
            // 〈計数器〉: 基本以外の発動 1 回 = 与ダメ +(10% ÷ 10 回) を残りターンずっと (山札に入っている時だけ効く・本体 DrawTerminalHand)
            if (deckHasCounter)
                flat += o.firedNonBasic * (TerminalCards.CounterPct / TerminalCards.CounterEvery) * DeckPerTurnDamage(in s, k) * turns * dw;
            // 手札の操作 (対象は本体が選ぶ = 挿さずに空けた最初の端子)
            if (o.retainTarget >= 0) flat += DeckRetainDmg * dw;
            if (o.copyTarget >= 0 && o.copyTarget < board.Count)
                flat += ((TerminalCards.Get(board[o.copyTarget])?.basic ?? true) ? DeckCopyBasicDmg : DeckCopyGoodDmg) * dw;
            if (o.burnTarget >= 0 && o.burnTarget < board.Count)
                flat += ((TerminalCards.Get(board[o.burnTarget])?.basic ?? true) ? DeckBurnBasicDmg : DeckBurnGoodDmg) * dw;
            // 使い捨ての費用
            float hpUnit = s.ledger ? 1f : s.wDef;
            foreach (int slot in o.fired)
            {
                var d = TerminalCards.Get(board[slot]);
                if (d == null || !d.disposable) continue;
                flat -= d.kind == TerminalKind.Attack ? DeckDisposableDmgCost * dw : DeckDisposableHpCost * hpUnit;
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
        private int[] ChooseDeckWiring(int[] rolls, MutualTurnTelegraph tele, out float bestU)
        {
            bestU = float.NegativeInfinity;
            var cm = CombatManager.Instance;
            var deck = cm?.TerminalDeck;
            int k = rolls?.Length ?? 0;
            if (deck == null || k == 0) return null;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            var run = GameManager.Instance?.Run;
            var board = deck.board;
            var env = cm.TerminalEnvNow(ctx, tele.enemyAttackValue);

            // 素朴版: 本体の既定 (素朴な点) と同じ。 旧い配線の素朴版 (出目降順の本数分割) に当たる「深く考えない」打ち方。
            if (EffectiveWiringSkill == WiringSkill.Naive)
            {
                bestU = 0f;
                return TerminalWiring.Auto(board, rolls, env, deck.state);
            }

            var st = BuildOptimalTurnState(rolls, tele);
            st.specDef = null;      // 旧い特殊端子 (§6-5) は山札では無い
            // 会心の効果 (〈真空管〉〈照星〉〈急所読み〉) を旧の式でも見積もれるよう、 会心率と倍率は常に読む (readOut の切り替えは変えない)
            if (ctx != null && !st.readOut && !st.traceBoth) FillReadOut(ref st, cm, ctx, run, tele.playerAttackPenalty);

            bool deckHasCounter = run?.terminalDeck != null && run.terminalDeck.Contains(TerminalIds.Counter);
            var cands = TerminalWiring.Enumerate(board, k);
            int n = cands.Count;
            var us = new float[n];
            var outs = new OptimalTurnOutcome[n];
            var addsStag = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var after = deck.state.Clone();
                var o = TerminalCards.Resolve(board, cands[i], rolls, env, after);
                var ws = DeckSums(in st, rolls, board, o, deck.state, after, in env, deckHasCounter);
                us[i] = OptimalUtilityCore(in st, k, in ws, out outs[i]);
                // ヴェスカ〈停滞する時間〉: 本体と同じく、 挿し方を直した後の攻撃の個数 < ブロックの個数で積む
                addsStag[i] = o.attackDice < o.blockDice;
            }

            int Pick(bool stagFilter)
            {
                int b = -1; float bu = float.NegativeInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (stagFilter && addsStag[i]) continue;
                    if (b < 0 || us[i] > bu) { b = i; bu = us[i]; }
                }
                return b;
            }
            // ヴェスカ〈停滞する時間〉は旧い配線と同じ規則 (SelectOptimalWiring の注記)。 手札の都合で避けられないターン
            // (停滞を積まない候補が残らない) と、 避けた最良の手でも今ターン死ぬターンは、 制限を外して選び直す。
            bool stag = StagFilterActive(cm, ctx);
            int best = Pick(stag);
            if (stag && (best < 0 || outs[best].taken >= st.myHp)) best = Pick(false);
            if (best < 0) return null;

            bestU = us[best];
            // [計装] 旧い配線と同じ値を残す (防ぎ切りの規則の数え・主攻撃の見積もりの照合)
            var fbo = outs[best];
            _lastBestU = bestU;
            _lastFreeBlock = fbo.freeBlock && !fbo.killsNow; _lastKillsNow = fbo.killsNow; _lastExpTaken = fbo.taken;
            _lastPredOld = fbo.dmgOld; _lastPredNew = fbo.dmgNew; _lastPredA = fbo.aCount; _lastPredUsed = fbo.dmgUsed;
            _lastPredRaw = fbo.rawNew; _lastPredCritP = st.critP; _lastPredCritMul = st.critMulEff; _lastPredOutMul = st.outMul;
            return cands[best];
        }

        /// <summary>山札で戦う戦闘の配線方策 (<see cref="CombatManager.WiringPolicy"/> へ差す)。 包みの処理は MutualWiringPlanPolicy と同じ。</summary>
        private WiringPlan DeckWiringPlanPolicy(int[] rolls, MutualTurnTelegraph tele)
        {
            RefreshMainCorr();   // [V3 mainCorr]
            PrepareSuperTurn(rolls, tele);
            int hpNow = CombatManager.Instance != null ? CombatManager.Instance.PlayerHP : -1;
            if (_fbPrevHp >= 0 && hpNow >= 0 && _fbPrevHp - hpNow > _fbPrevExpTaken) _fbUnavoidSeen = true;
            _lastFreeBlock = _lastKillsNow = false;
            var slots = ChooseDeckWiring(rolls, tele, out _);
            if (predTrace) BeginPredTrace(tele.turn);
            BeginMainCorrTurn(_lastPredA);
            _fbDecided++; if (_lastFreeBlock) _fbTurns++; if (_lastKillsNow) _fbKillTurns++;
            _fbPrevHp = hpNow; _fbPrevExpTaken = _lastExpTaken;
            return slots != null ? WiringPlan.OfSlots(slots) : default;      // null なら本体が既定 (TerminalWiring.Auto) で挿す
        }

        // =====================================================================
        //  戦闘後の端子の報酬 (GameHooks.ChooseTerminalReward)
        // =====================================================================
        //  手書きの規則 (未測定):
        //   1. 端子ごとの素の点 (RewardBaseScore): レア 3・コモン 2・使い捨て 1.5、 弱い/状況次第の物は下げる。
        //      軸の端子は偏重 (軸の品の数) で決まる: 0 なら 0.3 (効果が 0 に近い)・1 以上は 1.5 + 0.75 × 偏重 (上限 5)。
        //   2. ビルドの型 (_currentPersona) に合う端子に +2 (PersonaTerminals)。
        //   3. 攻撃の種類の割合: 今 4 割未満なら攻撃の種類に +2.5。 攻撃以外を足して 4 割を割るなら −1.5。
        //      攻撃の種類を引けないターンは攻撃できない (ADR-0014 規則 5) ので、 山札が守りと補助で薄まると殴れないターンが増える。
        //   4. 同じ端子をもう持っていれば 1 枚ごとに −1 (手札がそろって条件や連鎖が噛み合わなくなる)。
        //   5. 最良でも足切り (1.2 + 山札 10 枚を超えた 1 枚ごとに +0.1) に届かなければ受け取らない ── 弱い端子は、
        //      引くたびに強い端子を引く機会を削る (山札を弱くする)。 基本の端子 (攻撃・防御・充電) の価値を 1 前後と見た足切り。
        private const float RewardRejectBelow = 1.2f, RewardThresholdPerCard = 0.1f;
        private const int RewardThresholdFreeSize = 10;
        private const float RewardPersonaBonus = 2f, RewardMinAttackShare = 0.4f, RewardAttackNeedBonus = 2.5f, RewardDilutePenalty = 1.5f, RewardDupPenalty = 1f;

        /// <summary>報酬の候補 (端子の名前の並び) から 1 つ選ぶ。 戻り値 = 添字・負 = 受け取らない。</summary>
        private int ChooseTerminalReward(IReadOnlyList<string> offer)
        {
            if (offer == null || offer.Count == 0) return -1;
            var run = GameManager.Instance?.Run;
            var deck = run?.terminalDeck;
            int total = deck?.Count ?? 0, atk = 0;
            if (deck != null) foreach (var id in deck) if (TerminalCards.Get(id)?.kind == TerminalKind.Attack) atk++;
            float shareNow = total > 0 ? atk / (float)total : 0f;
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
                case TerminalIds.Copier:
                case TerminalIds.BurnOut:     return 0.8f;      // 対象を選べない (空けた最初の端子)
                case TerminalIds.Retainer:
                case TerminalIds.Stripper:    return 1.0f;      // 効く場面が狭い
                case TerminalIds.Heal:        return 1.2f;      // 3 以下だけ・攻撃にならない
                case TerminalIds.DischargeTube:
                case TerminalIds.Breaker:     return 1.3f;      // 充電を払う (充電の型なら下の +2 で上がる)
                case TerminalIds.Switchboard: return 1.5f;
            }
            if (d.rare) return d.disposable ? 2.5f : 3f;
            return d.disposable ? 1.5f : 2f;
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

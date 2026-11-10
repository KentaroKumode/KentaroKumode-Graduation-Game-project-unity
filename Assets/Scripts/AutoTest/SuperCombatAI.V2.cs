using System;
using System.Collections.Generic;
using CombatSystem;
using ItemSystem.PassiveSkills;

namespace AutoTest
{
    /// <summary>
    /// 先読み BOT (Super) の新方式 V2 (2026-11-05)。 旧方式の評価は、 ダイス 5 個の時代に手で決めた重み
    /// (<c>rolloutTtkWeight</c> / <c>rolloutChargeWeight</c> / <c>winHpWeight</c> / <c>StaticValue</c> の 0.6・0.3 など) の上に立っていて、
    /// 重みを動かしても強さが動かなかった (HP の重み・配線候補数の実験)。 構造そのものを置き換える。
    ///
    /// <para><b>原則</b>: ① 目的は「この戦闘に勝つ確率」ただ 1 つ。 ② 静的な評価式・足切り・部分点を持たない
    /// (全配線を決着までロールアウトする)。 ③ ロールアウト内の方策は重みを持たない
    /// ── 「あと何ターンで死ぬか − あと何ターンで倒せるか」の差 (競争の余裕) が最大の配線を選ぶ。
    /// ④ リロールは「いまの手の価値」と「振り直した手の期待価値」を同じ物差しで比べるだけ。</para>
    ///
    /// <para>規則の写しは旧方式と共通の <see cref="ApplyTurn"/> (<see cref="TurnRules"/> を使う) をそのまま使う。
    /// 与ダメ・被ダメの倍率は旧方式と同じく<b>観測から合わせる</b> (手置きではない)。</para>
    /// </summary>
    public sealed partial class SuperCombatAI
    {
        /// <summary>true のとき、 配線とリロールを V2 で決める。</summary>
        public bool useV2;
        /// <summary>配線 1 本あたりのロールアウト本数 (精度と時間の兼ね合いで、 強さの調整値ではない)。</summary>
        public int v2Rollouts = 16;
        /// <summary>リロール判断の内側で使うロールアウト本数と、 振り直した手の標本数。</summary>
        public int v2RerollRollouts = 6, v2RerollSamples = 6;
        /// <summary>戦闘の長さの安全弁 (通常の戦闘は 2〜15 ターン・ボスの連戦で 45 ターン級)。</summary>
        private const int V2Horizon = 60;

        private DiceTerminal[] _v2W;
        /// <summary>診断: 最初の N 件の配線判断を worker.log に書く (0 = 書かない)。</summary>
        public int v2TraceDecisions;
        private int _v2Traced, _v2TracedR;

        /// <summary>診断の書き出し。 ワーカーは戦闘中の Debug.Log を出さないので、 一時フォルダのファイルへ書く。</summary>
        private static void V2Trace(string line)
        {
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "v2_trace_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt"), line + "\n");
            }
            catch { }
        }

        private SimState MakeState(CombatManager cm, CombatContext ctx, int turn, int charge) => new SimState
        {
            pHp = cm.PlayerHP,
            eHp = cm.EnemyHP,
            turn = turn,
            charge = charge,
            eDef = _eDef0,
            shield = Math.Max(0, ctx.consShield),
            streak = (int)ctx.GetAccumulated(ItemSystem.PassiveSkills.Effects.UndyingBlade.StreakKey),
            steadfastReady = (int)ctx.GetAccumulated(ItemSystem.PassiveSkills.Effects.SteadfastStance.ReadyKey),
        };

        // ============================================================
        //  配線
        // ============================================================

        private DiceTerminal[] ChooseWiringV2(int[] rolls, MutualTurnTelegraph tele)
        {
            wiringDecisions++;
            var cm = CombatManager.Instance;
            if (rolls == null || rolls.Length == 0 || cm == null) return null;
            Observe(cm);
            if (!Snapshot(cm, rolls.Length)) return null;
            ObserveTelegraph(tele, cm.CurrentEnemy != null ? cm.CurrentEnemy.id : null);
            _nowMods = ModsFromTelegraph(tele);
            _sealedNow = tele.sealedTerminal;

            var ctx = PassiveSkillManager.Instance.Context;
            var s0 = MakeState(cm, ctx, tele.turn, ctx.GetCharge());
            int enemyAtk = Math.Max(0, tele.enemyAttackValue);
            _secondAtk = Math.Max(0, tele.secondaryAttackValue);
            int atkBase = Math.Max(0, _pAtkPower - Math.Max(0, tele.playerAttackPenalty));

            BuildCodes();
            PrepareHand(rolls);
            int seed = NextSeed();
            float bestV = float.NegativeInfinity; int bestCi = -1;
            for (int ci = 0; ci < _codes.Length; ci++)
            {
                var w = _codes[ci];
                if (!Canonical(w, rolls) || UsesSealed(w)) continue;
                float v = WiringValueV2(s0, rolls, enemyAtk, atkBase, w, v2Rollouts, seed);
                if (v > bestV) { bestV = v; bestCi = ci; }
            }
            if (bestCi < 0) return null;
            Array.Copy(_codes[bestCi], _best, _k);
            if (v2TraceDecisions > 0 && _v2Traced++ < v2TraceDecisions)
                V2Trace($"[V2] 敵={cm.CurrentEnemy?.id} T{tele.turn} 出目=[{string.Join(",", rolls)}] 敵攻撃={enemyAtk} 敵HP={s0.eHp}/{_eMaxHp} 自HP={s0.pHp}/{_pMaxHp}"
                                    + $" 素火力={atkBase} dmgMul={_dmgMul:F2} 候補={_codes.Length} 最良v={bestV:F4} 配線=[{string.Join(",", _best)}]");
            Remember(cm, s0, rolls, _best, enemyAtk, atkBase);
            return _best;
        }

        /// <summary>配線 <paramref name="w"/> を選んだときの勝率 (決着までのロールアウトの平均)。
        /// 今ターンの結果は決まっているので 1 回だけ解き、 その先を <paramref name="n"/> 本ロールアウトする。
        /// 候補ごとに同じ種から振り直す (共通乱数) ので、 候補どうしの差が引きの差ではなく配線の差になる。</summary>
        private float WiringValueV2(SimState s0, int[] dice, int enemyAtk, int atkBase, DiceTerminal[] w, int n, int seed)
        {
            int savedSeal = _sealedNow;
            _v2StartTurn = s0.turn;
            var s = s0;
            int outcome = ApplyTurn(ref s, dice, enemyAtk, w, atkBase, _nowMods);
            float v;
            if (outcome == Win) v = WinValueV2(s);
            else if (outcome == Loss) v = LossValueV2(s);
            else
            {
                _rng = new System.Random(seed);
                float acc = 0f;
                for (int r = 0; r < n; r++) acc += RolloutV2(s);
                v = acc / n;
            }
            _sealedNow = savedSeal;
            return v;
        }

        /// <summary>この判断が始まったターン。 「何ターンかけて決着したか」の順序づけに使う。</summary>
        private int _v2StartTurn;

        /// <summary>勝ちの値。 勝率が同じ手の中での<b>順序づけ</b>だけが目的で、 勝率の差 (最小でも 1/本数) より桁違いに小さい。
        /// 優先は ① 残り HP が多い ② 早く決着する。 これが無いと、 確実に勝てる局面で「どれを選んでも同じ」になり、
        /// 決着を先送りする配線 (HP 1 のクレートに 300 ターン) を選んでしまった。</summary>
        private float WinValueV2(SimState s)
            => 1f + 1e-3f * Math.Max(0, s.pHp) / _pMaxHp - 1e-5f * Math.Max(0, s.turn - _v2StartTurn);

        /// <summary>負けの値。 全部の手が負けでも、 敵を削る・長く生き延びる手を上に置く (順序づけだけ)。</summary>
        private float LossValueV2(SimState s)
            => 1e-4f * (1f - Math.Max(0, s.eHp) / (float)_eMaxHp) + 1e-6f * Math.Max(0, s.turn - _v2StartTurn);

        /// <summary>決着まで回す。 先のターンの敵の抽選は<b>観測した頻度から引き直す</b> (旧方式と同じ)。</summary>
        private float RolloutV2(SimState s)
        {
            if (_v2W == null || _v2W.Length != _k) _v2W = new DiceTerminal[_k];
            for (int d = 0; d < V2Horizon; d++)
            {
                var fm = SampleMods();
                _sealedNow = -1;
                int eTotal = 0;
                for (int i = 0; i < _eDiceCount; i++) eTotal += _rng.Next(1, _eRollMax + 1);
                int enemyAtk = Math.Max(0, (int)Math.Round(
                    Escalation.GetMultiplier(_escProfile, s.turn) * (_eBaseAtk + eTotal) * _armor.AttackFactor(s.turn)));

                for (int i = 0; i < _k; i++) _simDice[i] = RollFace();
                // 振り直し: 充電が払えて、 「新しい手の期待の余裕」が「いまの手の余裕」を上回るときだけ (全部振り直し・1 回目の費用)
                if (s.charge >= _k)
                {
                    float cur = BestMarginV2(_simDice, enemyAtk, s, fm, null);
                    float fresh = 0f;
                    const int Samples = 3;
                    for (int r = 0; r < Samples; r++)
                    {
                        for (int i = 0; i < _k; i++) _trialBuf[i] = RollFace();
                        fresh += BestMarginV2(_trialBuf, enemyAtk, s, fm, null);
                    }
                    if (fresh / Samples > cur)
                    {
                        s.charge -= _k;
                        for (int i = 0; i < _k; i++) _simDice[i] = RollFace();
                    }
                }

                BestMarginV2(_simDice, enemyAtk, s, fm, _rolloutW);
                int outcome = ApplyTurn(ref s, _simDice, enemyAtk, _rolloutW, _pAtkPower, fm);
                if (outcome == Win) return WinValueV2(s);
                if (outcome == Loss) return LossValueV2(s);
            }
            // 安全弁で決着しなかった: 相対 HP で先行している側を勝ちとみなす
            return (s.eHp / (float)_eMaxHp) < (s.pHp / (float)_pMaxHp) ? WinValueV2(s) : LossValueV2(s);
        }

        /// <summary>ロールアウト内の方策。 出目降順で「攻撃 a 本 / ブロック b 本 / 残り充電」の本数分割を見て、
        /// <b>競争の余裕</b> (あと何ターンで死ぬか − あと何ターンで倒せるか) が最大のものを選ぶ。
        /// 余裕が同じなら与ダメが大きい方、 それも同じなら被ダメが小さい方 (順序づけだけで、 重みではない)。
        /// 戻り値 = その余裕 (リロールの比較に使う)。 <paramref name="outW"/> が null なら選んだ形は書き出さない。</summary>
        private float BestMarginV2(int[] dice, int enemyAtk, SimState s, in TurnMods m, DiceTerminal[] outW)
        {
            for (int i = 0; i < _k; i++) _sortIdx[i] = i;
            for (int i = 1; i < _k; i++)
            {
                int key = _sortIdx[i], j = i - 1;
                while (j >= 0 && dice[_sortIdx[j]] < dice[key]) { _sortIdx[j + 1] = _sortIdx[j]; j--; }
                _sortIdx[j + 1] = key;
            }
            if (_v2W == null || _v2W.Length != _k) _v2W = new DiceTerminal[_k];

            int bestMargin = int.MinValue, bestDmg = -1, bestTaken = int.MaxValue;
            int bestA = -1, bestB = -1;
            for (int a = 0; a <= _k; a++)
                for (int b = 0; b <= _k - a; b++)
                {
                    for (int i = 0; i < _k; i++)
                        _v2W[_sortIdx[i]] = i < a ? DiceTerminal.Attack : i < a + b ? DiceTerminal.Block : DiceTerminal.Charge;
                    var t = s;
                    int outcome = ApplyTurn(ref t, dice, enemyAtk, _v2W, _pAtkPower, m);
                    int dmg, taken, margin;
                    if (outcome == Win) { dmg = s.eHp; taken = 0; margin = 1000; }
                    else if (outcome == Loss) { dmg = 0; taken = s.pHp; margin = -1000; }
                    else
                    {
                        dmg = Math.Max(0, s.eHp - t.eHp);
                        taken = Math.Max(0, s.pHp - t.pHp);
                        int tKill = dmg > 0 ? (s.eHp + dmg - 1) / dmg : 99;
                        int tDie = taken > 0 ? (s.pHp + taken - 1) / taken : 99;
                        margin = tDie - tKill;
                    }
                    if (margin > bestMargin || (margin == bestMargin && (dmg > bestDmg || (dmg == bestDmg && taken < bestTaken))))
                    { bestMargin = margin; bestDmg = dmg; bestTaken = taken; bestA = a; bestB = b; }
                }
            if (outW != null)
                for (int i = 0; i < _k; i++)
                    outW[_sortIdx[i]] = i < bestA ? DiceTerminal.Attack : i < bestA + bestB ? DiceTerminal.Block : DiceTerminal.Charge;
            return bestMargin;
        }

        // ============================================================
        //  リロール
        // ============================================================

        /// <summary>手 <paramref name="dice"/> の価値 = 最良の配線を選んだときの勝率。</summary>
        private float ValueOfDiceV2(SimState s0, int[] dice, int enemyAtk, int atkBase, int n, int seed)
        {
            PrepareHand(dice);
            float best = float.NegativeInfinity;
            for (int ci = 0; ci < _codes.Length; ci++)
            {
                var w = _codes[ci];
                if (!Canonical(w, dice) || UsesSealed(w)) continue;
                float v = WiringValueV2(s0, dice, enemyAtk, atkBase, w, n, seed);
                if (v > best) best = v;
            }
            return best;
        }

        private int[] ChooseRerollV2(int[] dice, MutualTurnTelegraph tele, int attempt)
        {
            rerollDecisions++;
            if (dice == null || dice.Length == 0) return null;
            var cm = CombatManager.Instance;
            if (cm == null || !Snapshot(cm, dice.Length)) return null;
            _nowMods = ModsFromTelegraph(tele);
            _sealedNow = tele.sealedTerminal;
            var ctx = PassiveSkillManager.Instance.Context;
            if (v2TraceDecisions > 0 && _v2TracedR++ < v2TraceDecisions)
                V2Trace($"[V2リロール] 敵={cm.CurrentEnemy?.id} T{tele.turn} 出目=[{string.Join(",", dice)}] 充電={ctx.GetCharge()} attempt={attempt} 今ターンのリロール={ctx.rerollsThisTurn}");
            if (ctx.rerollsThisTurn >= Math.Max(1, rerollMaxAttempts)) return null;

            int charge = ctx.GetCharge();
            bool free = ctx.currentBuffs.TryGetValue(YachtRoleEffects.FreeRerollKey, out float f) && f > 0f;
            int cost = _k * attempt;
            if (!free && charge < cost) return null;

            var s0 = MakeState(cm, ctx, tele.turn, charge);
            int enemyAtk = Math.Max(0, tele.enemyAttackValue);
            _secondAtk = Math.Max(0, tele.secondaryAttackValue);
            int atkBase = Math.Max(0, _pAtkPower - Math.Max(0, tele.playerAttackPenalty));
            BuildCodes();
            int seed = NextSeed();

            int samples = Math.Max(1, v2RerollSamples);
            // 振り直した手を先に引く (評価が内側で乱数を使い直すため)
            var fresh = new int[samples][];
            for (int r = 0; r < samples; r++)
            {
                fresh[r] = new int[_k];
                for (int i = 0; i < _k; i++) fresh[r][i] = RollFace();
            }

            float baseV = ValueOfDiceV2(s0, dice, enemyAtk, atkBase, v2RerollRollouts, seed);
            if (baseV >= 1f) return null;        // 今ターンで勝てる手は、 振り直しで良くならない
            var sAfter = s0; sAfter.charge = free ? charge : charge - cost;
            float acc = 0f;
            for (int r = 0; r < samples; r++)
                acc += ValueOfDiceV2(sAfter, fresh[r], enemyAtk, atkBase, v2RerollRollouts, seed);
            if (acc / samples <= baseV) return null;

            var all = new int[_k];
            for (int i = 0; i < _k; i++) all[i] = i;
            return all;
        }
    }
}

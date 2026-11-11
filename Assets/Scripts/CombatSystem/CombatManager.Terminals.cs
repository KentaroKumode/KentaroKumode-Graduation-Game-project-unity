using System;
using System.Collections.Generic;
using ItemSystem.PassiveSkills;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>CombatManager: 端子の山札 (ADR-0014)。 山札の出し入れと、 端子の結果を戦闘へ反映する所。
    /// 端子の効果の式は <see cref="TerminalCards.Resolve"/> にだけある (ここは結果を配るだけ)。</summary>
    public partial class CombatManager
    {
        /// <summary>この戦闘の山札。 戦闘の始め (<see cref="BeginTerminalDeck"/>) に必ず作る (最初の戦闘の前だけ null)。</summary>
        private TerminalDeckState _deck;
        /// <summary>このターンの端子の結果 (配線の前は null)。</summary>
        private TerminalOutcome _termOutcome;
        /// <summary>次のターンも手札に残す端子 (〈保持器〉)。</summary>
        private string _termRetain;
        /// <summary>このターンの主攻撃に掛ける別枠の倍率 (〈増幅器〉〈共振器〉)。 毎ターン 1 に戻す。</summary>
        private float _termDamageMul = 1f;

        /// <summary>この戦闘の山札 (画面・BOT が読む)。 最初の戦闘の前だけ null。 書き換えないこと。</summary>
        public TerminalDeckState TerminalDeck => _deck;
        /// <summary>このターンの端子の結果 (画面が発動した端子を光らせるのに使う)。</summary>
        public TerminalOutcome LastTerminalOutcome => _termOutcome;

        private void BeginTerminalDeck()
        {
            _termOutcome = null; _termRetain = null; _termDamageMul = 1f;
            // 山札は常に有る (旧い配線の経路は 2026-11-11 に削除)。 ランが無い戦闘 (単体の試験など) は開始の山札で戦う。
            var run = GameLoop.GameManager.Instance?.Run;
            List<string> runDeck;
            if (run != null)
            {
                if (run.terminalDeck == null || run.terminalDeck.Count == 0) run.terminalDeck = TerminalCards.StartingDeck();
                runDeck = run.terminalDeck;
            }
            else runDeck = TerminalCards.StartingDeck();
            _deck = new TerminalDeckState();
            _deck.BeginBattle(runDeck);
        }

        /// <summary>ターンの始め: 手札を引く。 持ち越し (条件無視・多く引く・溜めた攻撃・鋳造の出目) を今のターンへ移す。</summary>
        private void DrawTerminalHand(CombatContext ctx)
        {
            if (_deck == null) return;
            int extra = _deck.state.BeginTurn();
            _deck.DrawTurn(TerminalCards.HandSize + extra, _termRetain);
            _termRetain = null; _termOutcome = null; _termDamageMul = 1f;
            if (ctx == null) return;
            // 〈鋳造口〉: この戦闘中の出目 +N (自ロールの後に足す既存の口へ渡す)
            ctx.accumulatedValues[FoundryBonusKey] = _deck.state.foundryBonus;
            // 〈計数器〉: 基本以外の発動が累計 N 回ごとに与ダメージ +10% (山札に入っていれば効く)
            var run = GameLoop.GameManager.Instance?.Run;
            if (run?.terminalDeck != null && run.terminalDeck.Contains(TerminalIds.Counter))
            {
                int stacks = _deck.state.firedTotal / TerminalCards.CounterEvery;
                if (stacks > 0) ctx.AddOutgoingPct(TerminalCards.CounterPct * stacks);
            }
        }

        /// <summary>端子の見積もりに要る盤面を、 今の戦闘から作る。</summary>
        public TerminalEnv TerminalEnvNow(CombatContext ctx, int enemyAttack)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            var e = new TerminalEnv
            {
                hp = playerHP, maxHp = playerMaxHP, enemyAttack = enemyAttack,
                whetBonus = run != null ? run.terminalWhetBonus : 0,
            };
            if (ctx == null) return e;
            e.charge = ctx.GetCharge(); e.chargeCap = ctx.ChargeCap; e.shield = ctx.consShield;
            e.enemyPoison = ctx.EnemyPoison; e.enemyBleed = ctx.enemyBleedStacks;
            e.bursts = (int)ctx.GetAccumulated(ItemSystem.PassiveSkills.Keywords.Kw.BurstTotalKey);
            e.axCrit = ctx.AxisDepth(AxisRules.Crit); e.axBleed = ctx.AxisDepth(AxisRules.Bleed); e.axPoison = ctx.AxisDepth(AxisRules.Poison);
            e.axHeat = ctx.AxisDepth(AxisRules.Heat); e.axCharge = ctx.AxisDepth(AxisRules.Charge); e.axShield = ctx.AxisDepth(AxisRules.Shield);
            // 誓約の封じ (OathSeals.Apply が戦闘の始めに立てる)。 Resolve が同じ物を結果から落とす
            e.sealPoison = ctx.poisonSealed; e.sealBleed = ctx.bleedSealed; e.sealHeat = ctx.heatSealed;
            e.sealCharge = ctx.chargeSealed; e.sealShield = ctx.shieldSealed; e.sealCrit = ctx.critSealed;
            return e;
        }

        /// <summary>端子の結果のうち、 攻撃・防御・充電の合計以外を戦闘へ反映する (合計は呼び手が受け取る)。</summary>
        private void ApplyTerminalOutcome(CombatContext ctx, TerminalOutcome o, ref int enemyAtkValue)
        {
            _termDamageMul = o.damageMul;
            if (o.chargeSpend > 0) ctx.ConsumeCharge(Math.Min(o.chargeSpend, ctx.GetCharge()));
            if (o.chargeFill) ctx.AddCharge(ctx.ChargeCap);
            if (o.heal > 0) HealPlayer(o.heal);
            if (o.selfDamage > 0)
            {
                playerHP = Math.Max(1, playerHP - o.selfDamage);
                ctx.playerCurrentHP = playerHP;
            }
            if (o.shield > 0)
            {
                ctx.consShield += o.shield; ctx.shieldGainedTotal += o.shield;
                ShieldDiag.Note("端子", o.shield);
            }
            if (o.poison > 0) ItemSystem.PassiveSkills.Effects.Rw.Poison(ctx, o.poison);
            if (o.heat > 0) { ItemSystem.PassiveSkills.Keywords.Kw.EnableHeat(ctx); ItemSystem.PassiveSkills.Keywords.Kw.AddHeat(ctx, o.heat); }
            if (o.escalationDelay > 0) YachtRoleEffects.AddEscalationDelay(ctx, o.escalationDelay);
            if (o.retreatStage) YachtRoleEffects.AddEscalationDelay(ctx, YachtRoleEffects.TurnsToRetreatOneStage(ctx));
            if (o.enemyStunNext) ctx.nextTurnBuffs[YachtRoleEffects.EnemyStunKey] = 1f;
            if (o.ignoreDefense) ctx.playerArmorPen += DefenseFormula.IgnoreAll;
            if (o.forceCrit) ctx.forceCritical = true;
            if (o.critMultAdd > 0f) ctx.criticalMultiplier += o.critMultAdd;      // 会心倍率は毎ターン組み直されるので、 このターンだけ効く
            if (o.outgoingPctAdd > 0f) ctx.AddOutgoingPct(o.outgoingPctAdd);
            if (o.enemyAttackMul < 1f)
            {
                enemyAtkValue = Math.Max(0, Mathf.RoundToInt(enemyAtkValue * o.enemyAttackMul));
                // 2 体目の攻撃・〈連撃〉の 2 回目にも同じだけ掛ける (「このターンの敵の攻撃」なので)
                _secondaryAtkThisTurn = Math.Max(0, Mathf.RoundToInt(_secondaryAtkThisTurn * o.enemyAttackMul));
            }

            if (_deck != null)
            {
                var sb = new System.Text.StringBuilder("[端子] ");
                for (int i = 0; i < _deck.board.Count; i++)
                    sb.Append(i > 0 ? " / " : "").Append(_deck.board[i]).Append(o.fired.Contains(i) ? "●" : "○");
                sb.Append($" → 攻撃{o.attackAdd} 防御{o.blockAdd} 充電{o.chargeAdd}");
                if (o.damageMul != 1f) sb.Append($" ×{o.damageMul:F2}");
                Debug.Log(sb.ToString());
            }
        }

        /// <summary>ターンの終わり: 撃破のボーナスと、 山札への反映 (使い捨て・写し取り・焼き切り・保持)。</summary>
        private void FinishTerminalTurn(TerminalOutcome o)
        {
            if (_deck == null) return;
            if (enemyHP <= 0 && o.attackDice > 0)
            {
                var run = GameLoop.GameManager.Instance?.Run;
                var b = _deck.board;
                if (run != null)
                {
                    if (o.Fired(b, TerminalIds.Harvester))
                    {
                        run.playerMaxHP += TerminalCards.HarvesterMaxHp; playerMaxHP += TerminalCards.HarvesterMaxHp;
                        Debug.Log($"[端子/収穫機] 最大 HP +{TerminalCards.HarvesterMaxHp}");
                    }
                    if (o.Fired(b, TerminalIds.Whet)) { run.terminalWhetBonus += TerminalCards.WhetGain; Debug.Log($"[端子/研ぎ澄まし] 攻撃 +{TerminalCards.WhetGain} (計 {run.terminalWhetBonus})"); }
                    if (o.Fired(b, TerminalIds.LootBox)) { run.coins += TerminalCards.LootGold; Debug.Log($"[端子/戦利品箱] ゴールド +{TerminalCards.LootGold}"); }
                    if (o.Fired(b, TerminalIds.Stripper)) run.terminalExtraChoices++;
                }
            }
            _termRetain = _deck.FinishTurn(o);
        }
    }

    /// <summary>挿し方の列挙と、 既定の挿し方 (方策も画面も答えを出さなかった時・人の「おまかせ」)。 ADR-0014。</summary>
    public static class TerminalWiring
    {
        /// <summary>規則上ありうる挿し方をすべて返す (出目ごとの挿し先・−1 = 挿さない)。 挿せる数だけ見る (条件・充電は <see cref="TerminalCards.Resolve"/> が直す)。</summary>
        public static List<int[]> Enumerate(List<string> board, int diceCount)
        {
            var res = new List<int[]>();
            int m = board.Count;
            var cap = new int[m];
            for (int s = 0; s < m; s++) cap[s] = TerminalCards.Get(board[s])?.capacity ?? 0;
            var cur = new int[diceCount];
            void Rec(int i)
            {
                if (i == diceCount) { res.Add((int[])cur.Clone()); return; }
                cur[i] = -1; Rec(i + 1);
                for (int s = 0; s < m; s++)
                {
                    if (cap[s] <= 0) continue;
                    cap[s]--; cur[i] = s; Rec(i + 1); cap[s]++;
                }
            }
            Rec(0);
            return res;
        }

        /// <summary>既定の挿し方: 全部の挿し方を試して、 素朴な点 (攻撃 + 防げる分 + 充電と回復の一部) が最も高いものを選ぶ。
        /// 本気の方策ではない ── BOT は自分の評価で選ぶ (AutoRunner)。</summary>
        public static int[] Auto(List<string> board, int[] dice, TerminalEnv env, TerminalBattleState state)
        {
            int[] best = null; float bestScore = float.NegativeInfinity;
            foreach (var w in Enumerate(board, dice.Length))
            {
                var o = TerminalCards.Resolve(board, w, dice, env, state.Clone());
                float score = NaiveScore(o, env);
                if (score > bestScore) { bestScore = score; best = w; }
            }
            return best ?? new int[dice.Length];
        }

        public static float NaiveScore(TerminalOutcome o, TerminalEnv env)
        {
            float atk = o.attackDice > 0 ? o.attackAdd * o.damageMul * (1f + o.outgoingPctAdd) + o.critMultAdd * 4f : 0f;
            float enemy = env.enemyAttack * o.enemyAttackMul;
            float through = Math.Max(0f, enemy - o.blockAdd - o.shield) * o.takenMul;
            float saved = env.enemyAttack - through;
            return atk + saved + o.chargeAdd * 0.3f + o.heal * 0.5f - o.selfDamage * 0.8f - o.chargeSpend * 0.3f
                 + o.firedNonBasic * 0.25f + (o.enemyStunNext ? env.enemyAttack : 0f) + o.poison * 0.3f + o.heat * 0.3f;
        }
    }
}

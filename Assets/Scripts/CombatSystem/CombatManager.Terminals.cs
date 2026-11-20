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
        /// <summary>[計装 2026-11-19] ランの中で最大の攻撃の端子の合計と、 その内訳。 BOT がランの始めに空にする。</summary>
        public static int AttackSumMax;
        public static string AttackSumMaxInfo = "";
        /// <summary>このターンの敵の出目 (夜番の賽が読む・ロールの直後に入れる)。</summary>
        private int[] _enemyDiceNow;
        /// <summary>このターンの端子の結果 (配線の前は null)。</summary>
        private TerminalOutcome _termOutcome;
        /// <summary>次のターンも手札に残す端子 (〈有効活用〉)。</summary>
        private string _termRetain;
        /// <summary>このターンの主攻撃に掛ける別枠の倍率 (〈激昂〉〈共鳴〉)。 毎ターン 1 に戻す。</summary>
        private float _termDamageMul = 1f;
        /// <summary>[計装 #106] このターンに方策 / 画面が返した挿し方 (Resolve が直す前)。 ターンの始めに null へ戻す。 判断には使わない。</summary>
        private int[] _termWanted;

        /// <summary>挑戦カード〈猫の手〉: このターン猫が先に挿した 1 組 (出目の添字 → 場の添字)。 −1 = 挿していない。
        /// ターンの始め (<see cref="DrawTerminalHand"/>) に外し、 配線の判断の頭 (<see cref="ChooseCatPaw"/>) で決める。</summary>
        private int _catDie = -1, _catSlot = -1;

        /// <summary>[計装 #6] 挑戦カード〈金で殴る〉で払った金の累計 (G) / 払ったターンの累計。 プロセスの中で累計し続ける (読む側が差分を取る)。</summary>
        public static long GoldPunchPaidTotal, GoldPunchTurnsTotal;

        /// <summary>〈猫の手〉がこのターン先に挿した出目 (−1 = 無し)。 BOT の挿し方の方策はこの 1 組を固定として扱う (外さない)。</summary>
        public int CatPawDie => _catDie;
        /// <summary>〈猫の手〉がこのターン先に挿した端子 (場の添字・−1 = 無し)。</summary>
        public int CatPawSlot => _catSlot;

        /// <summary>この戦闘の山札 (画面・BOT が読む)。 最初の戦闘の前だけ null。 書き換えないこと。</summary>
        public TerminalDeckState TerminalDeck => _deck;
        /// <summary>このターンの端子の結果 (画面が発動した端子を光らせるのに使う)。</summary>
        public TerminalOutcome LastTerminalOutcome => _termOutcome;

        private void BeginTerminalDeck()
        {
            _termOutcome = null; _termRetain = null; _termDamageMul = 1f; _catDie = _catSlot = -1; _termKillPending = null;
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
            // 時計の賽 (ADR-0015): 〈時計〉の端子を常設として場に置く (山札には入れない・戦闘の間ずっと場にある)
            if (GameLoop.EquippedDie.IdOf(run) == GameLoop.DieIds.Clock) _deck.permanent.Add(TerminalIds.Clock);
            TerminalDiag.NoteBattle(runDeck);   // [計装 2026-11-12]
        }

        /// <summary>ターンの始め: 手札を引く。 持ち越し (条件無視・多く引く・溜めた攻撃・鋳造の出目) を今のターンへ移す。</summary>
        private void DrawTerminalHand(CombatContext ctx)
        {
            _catDie = _catSlot = -1;      // 〈猫の手〉は配線の判断の頭で挿し直す (振り直しの判断の間は無い)
            _termKillPending = null;
            _termWanted = null;
            if (_deck == null) return;
            int extra = _deck.state.BeginTurn();
            // ヴェスカ〈停滞する時間〉: 引いた端子 1 枚ごとに確率で止める (持たない敵は 0)
            float stop = ItemSystem.PassiveSkills.Effects.StagnantTime.StopChance(currentEnemy);
            if (ctx != null && TenyoHeavy.RingStopsAt(ctx.currentTurn)) stop = 1f;      // 天与の指輪 (2026-11-15): 大技の次のターンは、 引いた端子を全部止める
            _deck.DrawTurn(TerminalCards.HandSize + extra, _termRetain, stop);
            TerminalDiag.NoteDrawn(_deck.LastDrawnIds);   // [計装 #106] 新しく引いた端子
            if (TerminalCards.HandCarry && ctx != null && ctx.currentTurn > 1) BossDownDiag.NoteCarry(_deck.LastCarriedCount);   // [計装 2026-11-13] 持ち越した枚数 (戦闘の 1 ターン目は除く)
            if (stop > 0f)
            {
                GuardDiag.NoteStag7F(currentEnemy?.id, _deck.LastDrawnCount, _deck.LastStoppedCount, _deck.LastStoppedMains, _deck.LastHeldCount);   // [計装]
                if (_deck.LastStoppedCount > 0)
                    Debug.Log($"[停滞する時間] 引いた {_deck.LastDrawnCount} 枚のうち {_deck.LastStoppedCount} 枚が止まった (確率 {stop:P0})");
            }
            _termRetain = null; _termOutcome = null; _termDamageMul = 1f;
            if (ctx == null) return;
            // 〈打ち直し〉: この戦闘中の出目 +N (自ロールの後に足す既存の口へ渡す)
            ctx.accumulatedValues[FoundryBonusKey] = _deck.state.foundryBonus;
            // 〈柔軟な戦略〉: 基本以外の発動が累計 N 回ごとに与ダメージ +10%。 このターンに引いて場にある時だけ効く
            //   (2026-11-20〜 説明文どおり「端子の効果」として。 旧: ランの山札に入っていれば、 引かなくても毎ターン効いた)。 累計は引いていない間も数える
            //   素と強化した〈柔軟な戦略＋〉は別の端子 (両方が場にあれば両方効く・同じ名前の 2 枚目は足さない)
            foreach (var cid in new[] { TerminalIds.Counter, TerminalIds.Upgraded(TerminalIds.Counter) })
            {
                if (!_deck.board.Contains(cid)) continue;
                int stacks = _deck.state.firedTotal / (TerminalCards.Get(cid).upgraded ? TerminalCards.CounterEveryUp : TerminalCards.CounterEvery);
                if (stacks > 0) ctx.AddOutgoingPct(TerminalCards.CounterPct * stacks);
            }
        }

        /// <summary>挑戦カード〈猫の手〉(#5): 配線の判断の頭 (振り直しが済み、 予告が確定した後) に、 札の確率で 1 組を先に挿す。
        /// 選ぶのは Super の評価 (<see cref="GameLoop.GameHooks.CatPawPick"/>)。 口が無い・評価できない時は既定の挿し方 (<see cref="TerminalWiring.CatPawFallback"/>)。
        /// 乱数は専用の鍵 "challenge.cat" (他の抽選の消費列を動かさない)。 札を受けていなければ乱数も引かない。</summary>
        private void ChooseCatPaw(int[] dice, MutualTurnTelegraph tele, TerminalEnv env)
        {
            _catDie = _catSlot = -1;
            float p = MetaProgression.MetaBuffApplicator.GetCatPawChance();
            if (p <= 0f || _deck == null || dice == null || dice.Length == 0 || _deck.board.Count == 0) return;
            if (!GameLoop.GameRng.Chance(p, "challenge.cat", RngIdx(0))) return;
            int[] pick = null;
            var hook = GameLoop.GameHooks.CatPawPick;
            if (hook != null)
            {
                try { pick = hook(dice, tele); }
                catch (Exception e) { Debug.LogWarning("[猫の手] 評価に失敗したので既定の挿し方で選ぶ: " + e.Message); pick = null; }
            }
            if (pick == null) pick = TerminalWiring.CatPawFallback(_deck.board, dice, env, _deck.state);
            if (pick == null || pick.Length < 2 || pick[0] < 0 || pick[0] >= dice.Length || pick[1] < 0 || pick[1] >= _deck.board.Count) return;
            _catDie = pick[0]; _catSlot = pick[1];
            Debug.Log($"[猫の手] 出目 {dice[_catDie]} → 〈{_deck.board[_catSlot]}〉");
        }

        /// <summary>端子の見積もりに要る盤面を、 今の戦闘から作る。</summary>
        public TerminalEnv TerminalEnvNow(CombatContext ctx, int enemyAttack)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            var e = new TerminalEnv
            {
                hp = playerHP, maxHp = playerMaxHP, enemyAttack = enemyAttack,
                whetBonus = run != null ? run.terminalWhetBonus : 0,
                enemyHp = enemyHP, enemyMaxHp = EnemyMaxHP,      // 〈処刑の刃＋〉
                // 時限デバフ〈罪悪感〉(2026-11-16): 防御に挿した出目 −2 (下限 0)。 Resolve が引く (本体・画面・BOT の同じ口)
                blockDieCut = EventSystem.TimedEffects.TimedDebuffRule.BlockDieCut(run?.timedDebuffs),
            };
            if (ctx == null) return e;
            e.charge = ctx.GetCharge(); e.chargeCap = ctx.ChargeCap; e.shield = ctx.consShield;
            e.enemyPoison = ctx.EnemyPoison; e.enemyBleed = ctx.enemyBleedStacks;
            e.bursts = (int)ctx.GetAccumulated(ItemSystem.PassiveSkills.Keywords.Kw.BurstTotalKey);
            e.axCrit = ctx.AxisDepth(AxisRules.Crit); e.axBleed = ctx.AxisDepth(AxisRules.Bleed); e.axPoison = ctx.AxisDepth(AxisRules.Poison);
            e.axHeat = ctx.AxisDepth(AxisRules.Heat); e.axCharge = ctx.AxisDepth(AxisRules.Charge); e.axShield = ctx.AxisDepth(AxisRules.Shield);
            e.axDeck = ctx.AxisDepth(AxisRules.Deck); e.axRole = ctx.AxisDepth(AxisRules.Role);
            // 賽のパッシブ (ADR-0015・DiePassives)
            e.dieId = GameLoop.EquippedDie.IdOf(run);
            e.diceRerolled = ctx.rerollsThisTurn > 0;
            int emax = 0;
            if (_enemyDiceNow != null) foreach (int d in _enemyDiceNow) if (d > emax) emax = d;
            e.enemyMaxDie = emax;
            e.heavyTurn = Escalation.IsHeavyTurn(currentEnemy, ctx.currentTurn);
            e.chargeCostCut = ctx.ChargeCostNow(2) < 2 ? 1 : 0;   // 〈過負荷チューナー〉: 過充電の間 1 減る (最低 1)
            e.noAttack = GameLoop.Consumables.RapierContreOn(ctx);   // 〈シュヴァリエのレイピア〉のコントラタック中は攻撃の端子に挿せない
            return e;
        }

        /// <summary>端子の結果のうち、 攻撃・防御・充電の合計以外を戦闘へ反映する (合計は呼び手が受け取る)。</summary>
        private void ApplyTerminalOutcome(CombatContext ctx, TerminalOutcome o, ref int enemyAtkValue)
        {
            _termDamageMul = o.damageMul;
            if (o.chargeSpend > 0) ctx.ConsumeCharge(Math.Min(o.chargeSpend, ctx.GetCharge()));
            if (o.chargeCapAdd > 0) ctx.chargeCapBonus += o.chargeCapAdd;      // 〈大導電＋〉: この戦闘の充電の上限 (満たす前に)
            if (o.chargeFill) ctx.AddCharge(ctx.ChargeCap);
            //   〈応急箱＋〉: 回復の量のうち、 最大 HP を超えた分 (回復する前の空きを超えた分) はシールドになる
            int overheal = o.overhealToShield > 0 ? Math.Max(0, o.overhealToShield - Math.Max(0, playerMaxHP - playerHP)) : 0;
            if (o.heal > 0) HealPlayer(o.heal);
            if (overheal > 0) { ctx.consShield += overheal; ctx.shieldGainedTotal += overheal; ShieldDiag.Note("端子", overheal); }
            if (o.selfDamage > 0)
            {
                playerHP = Math.Max(1, playerHP - o.selfDamage);
                ctx.playerCurrentHP = playerHP;
            }
            if (o.shieldSpend > 0) ctx.consShield = Math.Max(0, ctx.consShield - o.shieldSpend);
            if (o.shield > 0)
            {
                ctx.consShield += o.shield; ctx.shieldGainedTotal += o.shield;
                ShieldDiag.Note("端子", o.shield);
            }
            if (o.poison > 0) ItemSystem.PassiveSkills.Effects.Rw.Poison(ctx, o.poison);
            if (o.heat > 0) { ItemSystem.PassiveSkills.Keywords.Kw.EnableHeat(ctx); ItemSystem.PassiveSkills.Keywords.Kw.AddHeat(ctx, o.heat, "端子"); }
            for (int r = 0; r < o.retreatStage; r++) YachtRoleEffects.AddEscalationDelay(ctx, YachtRoleEffects.TurnsToRetreatOneStage(ctx));
            if (o.escalationDelay > 0)      // 時計の賽 (ADR-0015)
            {
                YachtRoleEffects.AddEscalationDelay(ctx, o.escalationDelay);
                Debug.Log($"[賽] 時計の賽: エスカレーションの次の段階を {o.escalationDelay} ターン先へ");
            }
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

        /// <summary>撃破のボーナスを配る端子のうち、 このターンに発動したもの。 場の添字は <see cref="TerminalDeckState.FinishTurn"/> で書き換わるので、
        /// ターン終了時の撃破を待つ間は名前で控えておく (<see cref="_termKillPending"/>)。</summary>
        private sealed class TermKillBonus
        {
            /// <summary>配る量 (<see cref="TerminalOutcome.killMaxHp"/> などの写し・〈残響〉の 2 回目も入っている)。</summary>
            public int maxHp, whet, gold, choices;
            /// <summary>撃破した時に戻る HP (〈諸刃の剣＋〉)。</summary>
            public int refundHp;
            public bool harvester => maxHp > 0;
            public bool loot => gold > 0;
            public bool stripper => choices > 0;
            public bool whetOn => whet > 0;
            public bool Any => harvester || whetOn || loot || stripper || refundHp > 0;
        }
        /// <summary>このターンに発動した撃破のボーナスの端子のうち、 まだ配っていない (敵がまだ倒れていない) もの。 ターン終了時の撃破 (〈絞り出し〉〈百断〉など)
        /// を <see cref="SettleTerminalKillBonus"/> で拾う。 null = 待っていない。 ターンの終わり (FinishTurnCommon) までに必ず null へ戻る。</summary>
        private TermKillBonus _termKillPending;

        /// <summary>ターンの終わり: 撃破のボーナスと、 山札への反映 (使い捨て・影写し・火葬の剣・保持)。
        /// 撃破のボーナスは「このターンに敵を倒すと」(説明文) のとおり、 撃破の経路を問わない (#117・2026-11-13)。
        /// 主攻撃までに倒れていればここで配り、 まだ倒れていなければターン終了時の削りを待つ (<see cref="SettleTerminalKillBonus"/>)。
        /// 旧: 攻撃の端子に出目を挿したターンの、 ここまでの撃破だけ (<see cref="GameLoop.GameHooks.TerminalKillBonusNeedsAttack"/> で戻せる)。
        /// 4 種とも攻撃の種類 (〈剥ぎ取り〉は 2026-11-13 に補助から攻撃へ) なので、 発動すれば攻撃に挿している。 旧い条件で落ちるのは 4 種すべてのターン終了時の撃破
        /// (2026-11-13 より前は〈剥ぎ取り〉の攻撃なしの撃破も落ちていた)。</summary>
        private void FinishTerminalTurn(TerminalOutcome o)
        {
            if (_deck == null) return;
            _termKillPending = null;
            var b = _deck.board;
            var kb = new TermKillBonus { maxHp = o.killMaxHp, whet = o.killWhet, gold = o.killGold, choices = o.killChoices, refundHp = o.killRefundHp };
            if (kb.Any)
            {
                if (enemyHP <= 0 && o.attackDice > 0) GrantTerminalKillBonus(kb);   // 旧い条件でも入る撃破
                else if (enemyHP <= 0)
                {
                    // 攻撃の端子なしで倒れた (毒・出血・爆発・反撃・品の直接ダメージなど)。 4 種とも攻撃の種類なので、 発動していればここへは来ない
                    // (〈剥ぎ取り〉が補助だった 2026-11-13 まではここへ来た)。 守りとして残す
                    TerminalDiag.NoteKillNewOnly(kb.harvester, kb.whetOn, kb.loot, kb.stripper);   // [計装]
                    if (!GameLoop.GameHooks.TerminalKillBonusNeedsAttack) GrantTerminalKillBonus(kb);
                }
                else _termKillPending = kb;
            }
            TerminalDiag.NoteTurn(_deck.board, o, _termWanted);   // [計装 2026-11-12・#106 内訳] (FinishTurn が手札を書き換える前に)
            if (_deck.LastKept.Count > 0)            // [計装 2026-11-13] 残す印で残した端子を、 このターンに使ったか
            {
                var usedNames = TerminalDeckState.UsedFromHand(_deck.board, _deck.permanent.Count, o);
                int usedKept = 0;
                foreach (var id in _deck.LastKept) if (usedNames.Remove(id)) usedKept++;
                BossDownDiag.NoteKeptUse(_deck.LastKept.Count, usedKept);
            }
            CueTerminals(b, o);                      // 合図 (#319・場の添字で読む ── FinishTurn が手札を書き換える前に)
            RemoveUsedFromRunDeck(o);                // (FinishTurn が手札を書き換える前に ── 場の添字で読む)
            _termRetain = _deck.FinishTurn(o);
        }

        /// <summary>〈後の先〉(2026-11-20〜): 「防いだ量」= ブロックで実際に防いだ量。 Resolve が入れた見込み (<see cref="TerminalOutcome.parryStore"/>) を
        /// 実際の量 (<paramref name="blocked"/> = 攻撃 1 回ごとの min(後の先のブロック, ブロックで止めた量) の和) へ直す。 BOT の見積もりは見込みのまま。</summary>
        private void SettleParry(TerminalOutcome o, int blocked)
        {
            if (_deck == null || o == null || (o.parryBlock <= 0 && o.parryStore <= 0)) return;
            _deck.state.storedAttackNext = Math.Max(0, _deck.state.storedAttackNext + blocked - o.parryStore);
        }

        /// <summary>このターンに発動した端子の合図 (#319): 発動・使い捨て・〈火葬の剣〉が外した端子。 知らせるだけ。</summary>
        private static void CueTerminals(List<string> b, TerminalOutcome o)
        {
            foreach (int i in o.fired)
            {
                if (i < 0 || i >= b.Count) continue;
                string id = b[i];
                RaiseCue(CombatCue.TerminalFired, id);
                var def = TerminalCards.Get(id);
                if (def != null && def.disposable) RaiseCue(CombatCue.DisposableUsed, id);
                if (TerminalCards.BaseOf(id) == TerminalIds.BurnOut && o.burnTarget >= 0 && o.burnTarget < b.Count) RaiseCue(CombatCue.BurnOut, b[o.burnTarget]);
            }
        }

        /// <summary>ターン終了時の撃破を拾う (FinishTurnCommon が 2 か所で呼ぶ)。 <paramref name="enemyHpNow"/> ≤ 0 なら、 控えておいた撃破のボーナスを配る。
        /// <paramref name="final"/> = このターン最後の確かめ (ここで倒れていなければ、 このターンの撃破は無い)。
        /// 旧い条件 (<see cref="GameLoop.GameHooks.TerminalKillBonusNeedsAttack"/>) ではターン終了時の撃破に配らない (計装だけ数える)。</summary>
        private void SettleTerminalKillBonus(int enemyHpNow, bool final)
        {
            var kb = _termKillPending;
            if (kb == null) return;
            if (enemyHpNow <= 0)
            {
                _termKillPending = null;
                TerminalDiag.NoteKillNewOnly(kb.harvester, kb.whetOn, kb.loot, kb.stripper);   // [計装]
                if (!GameLoop.GameHooks.TerminalKillBonusNeedsAttack) GrantTerminalKillBonus(kb);
            }
            else if (final) _termKillPending = null;
        }

        /// <summary>撃破のボーナスを配る (ランが無い戦闘では何もしない)。</summary>
        private void GrantTerminalKillBonus(TermKillBonus kb)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null) return;
            if (kb.harvester)
            {
                TerminalDiag.Note(TerminalIds.Harvester, TerminalDiag.KillBonus);   // [計装]
                run.playerMaxHP += kb.maxHp; playerMaxHP += kb.maxHp;
                // ターン終了時の撃破では FinishTurnCommon の SyncHPFromContext が ctx から最大 HP を拾い直すので、 ctx にも入れる
                var hctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
                if (hctx != null) hctx.playerMaxHP = playerMaxHP;
                Debug.Log($"[端子/魂狩り] 最大 HP +{kb.maxHp}");
            }
            if (kb.whetOn) { TerminalDiag.Note(TerminalIds.Whet, TerminalDiag.KillBonus); run.terminalWhetBonus += kb.whet; Debug.Log($"[端子/処刑の刃] 攻撃 +{kb.whet} (計 {run.terminalWhetBonus})"); }
            if (kb.loot) { TerminalDiag.Note(TerminalIds.LootBox, TerminalDiag.KillBonus); run.coins += kb.gold; Debug.Log($"[端子/賞金狩り] ゴールド +{kb.gold}"); }
            if (kb.refundHp > 0)
            {
                // 〈諸刃の剣＋〉: 払った HP が戻る (回復ではないので、 回復を減らす効果は掛からない・最大 HP は超えない)
                playerHP = Math.Min(playerMaxHP, playerHP + kb.refundHp);
                var rctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
                if (rctx != null) rctx.playerCurrentHP = playerHP;
                Debug.Log($"[端子/諸刃の剣＋] 払った HP が戻る +{kb.refundHp}");
            }
            if (kb.stripper) { TerminalDiag.Note(TerminalIds.Stripper, TerminalDiag.KillBonus); run.terminalExtraChoices += kb.choices; Debug.Log($"[端子/剥ぎ取り] 戦闘後の端子の選択肢 +{kb.choices} (計 +{run.terminalExtraChoices})"); }
        }

        /// <summary>[測定の口の時だけ] 使うとランの山札から抜ける端子 (〈応急箱〉・<see cref="TerminalCards.LeavesRunDeck"/>・既定では抜けない = 2026-11-14〜) を、
        /// 使ったターンの終わりにランの山札から 1 枚ずつ抜く。
        /// この戦闘の中は使い捨てと同じく戻らない (<see cref="TerminalDeckState.FinishTurn"/>)。 山札が <see cref="TerminalCards.MinDeckSize"/> 枚以下なら抜かない
        /// (店・休憩の抜き方と同じ下限 ── 出目 3 個を挿す先が足りなくならない枚数)。</summary>
        private void RemoveUsedFromRunDeck(TerminalOutcome o)
        {
            var deck = GameLoop.GameManager.Instance?.Run?.terminalDeck;
            if (deck == null) return;
            foreach (int s in o.fired)
            {
                if (s < 0 || s >= _deck.board.Count) continue;
                string id = _deck.board[s];
                if (!TerminalCards.LeavesRunDeck(TerminalCards.Get(id))) continue;
                if (deck.Count <= TerminalCards.MinDeckSize) { Debug.Log($"[端子/{id}] 山札が {deck.Count} 枚なので、ランの山札からは抜かない"); continue; }
                if (deck.Remove(id)) TerminalDiag.NoteRemoved(id, TerminalDiag.LeftRunDeck);   // [計装 #106]
                else continue;
                Debug.Log($"[端子/{id}] 使ったので、ランの山札から抜いた (山札 {deck.Count} 枚)");
            }
        }
    }

    /// <summary>挿し方の列挙と、 既定の挿し方 (方策も画面も答えを出さなかった時・人の「おまかせ」)。 ADR-0014。</summary>
    public static class TerminalWiring
    {
        /// <summary>〈有効活用〉(手札の持ち越しの規則) の対象として選ばれた捨て札の添字 (発動した〈有効活用〉の場の位置の targets の値・選ばれていなければ −1)。</summary>
        public static int RecallWant(List<string> board, TerminalOutcome o, int[] targets)
        {
            if (board == null || o == null || targets == null) return -1;
            foreach (int s in o.fired)
                if (s >= 0 && s < board.Count && s < targets.Length && TerminalCards.BaseOf(board[s]) == TerminalIds.Retainer) return targets[s];
            return -1;
        }

        /// <summary>規則上ありうる挿し方をすべて返す (出目ごとの挿し先・−1 = 挿さない)。 挿せる数だけ見る (条件・充電は <see cref="TerminalCards.Resolve"/> が直す)。
        /// state を渡すと、 止まった端子 (ヴェスカ〈停滞する時間〉) へ挿す手は出さない (Resolve が外すのと同じ結果になる手を数え直さないため)。
        /// <paramref name="fixedDie"/> ≥ 0 なら、 その出目は <paramref name="fixedSlot"/> に挿した挿し方だけ (〈猫の手〉が先に挿した 1 組)。</summary>
        public static List<int[]> Enumerate(List<string> board, int diceCount, TerminalBattleState state = null, int fixedDie = -1, int fixedSlot = -1)
        {
            var res = new List<int[]>();
            int m = board.Count;
            var cap = new int[m];
            for (int s = 0; s < m; s++) cap[s] = state != null && state.IsStopped(s) ? 0 : TerminalCards.Get(board[s])?.capacity ?? 0;
            bool hasFixed = fixedDie >= 0 && fixedDie < diceCount && fixedSlot >= 0 && fixedSlot < m && cap[fixedSlot] > 0;
            if (hasFixed) cap[fixedSlot]--;      // 先に挿した 1 組の分を、 先に場から取っておく
            var cur = new int[diceCount];
            void Rec(int i)
            {
                if (i == diceCount) { res.Add((int[])cur.Clone()); return; }
                if (hasFixed && i == fixedDie) { cur[i] = fixedSlot; Rec(i + 1); return; }
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
        /// 本気の方策ではない ── BOT は自分の評価で選ぶ (AutoRunner)。 <paramref name="fixedDie"/> は <see cref="Enumerate"/> と同じ (〈猫の手〉の 1 組を外さない)。</summary>
        public static int[] Auto(List<string> board, int[] dice, TerminalEnv env, TerminalBattleState state, int fixedDie = -1, int fixedSlot = -1)
        {
            int[] best = null; float bestScore = float.NegativeInfinity;
            foreach (var w in Enumerate(board, dice.Length, state, fixedDie, fixedSlot))
            {
                var o = TerminalCards.Resolve(board, w, dice, env, state.Clone());
                float score = NaiveScore(o, env);
                if (score > bestScore) { bestScore = score; best = w; }
            }
            return best ?? new int[dice.Length];
        }

        // ── 挑戦カード〈猫の手〉(#5) ──
        //   ターンの配線の判断の頭に、 最良の挿し方の中から「外すと効用が一番落ちる 1 組」を先に挿す。 効用は Super の評価 (GameHooks.CatPawPick・AutoTest が差す)、
        //   評価が使えなければ既定の挿し方の素朴な点。 1 組の選び方 (MostValuablePair) はここの 1 か所だけで、 どちらの評価も同じ規則で選ぶ。

        /// <summary>挿し方 <paramref name="wiring"/> (挿し方を直した後の値 = <see cref="TerminalOutcome.slotOfDie"/> を渡す) の中で、
        /// その 1 組だけを外した時に効用が一番落ちる組 <c>{ 出目, 場の添字 }</c>。 同点は若い出目。 挿した組が無ければ <c>{ -1, -1 }</c>。</summary>
        public static int[] MostValuablePair(int[] wiring, Func<int[], float> utility)
        {
            var none = new[] { -1, -1 };
            if (wiring == null || utility == null) return none;
            float full = utility(wiring);
            int bestDie = -1; float bestLoss = float.NegativeInfinity;
            for (int i = 0; i < wiring.Length; i++)
            {
                if (wiring[i] < 0) continue;
                var w = (int[])wiring.Clone(); w[i] = -1;
                float loss = full - utility(w);
                if (bestDie < 0 || loss > bestLoss) { bestDie = i; bestLoss = loss; }
            }
            return bestDie >= 0 ? new[] { bestDie, wiring[bestDie] } : none;
        }

        /// <summary>〈猫の手〉の既定 (Super の評価が使えない時): 既定の挿し方 (<see cref="Auto"/>) の中から、 素朴な点で 1 組を選ぶ。</summary>
        public static int[] CatPawFallback(List<string> board, int[] dice, TerminalEnv env, TerminalBattleState state)
        {
            if (board == null || board.Count == 0 || dice == null || dice.Length == 0 || state == null) return new[] { -1, -1 };
            var best = Auto(board, dice, env, state);
            var fixedUp = TerminalCards.Resolve(board, best, dice, env, state.Clone()).slotOfDie;
            return MostValuablePair(fixedUp, w => NaiveScore(TerminalCards.Resolve(board, w, dice, env, state.Clone()), env));
        }

        /// <summary>挿し方に〈猫の手〉の 1 組を入れた写しを返す (元の配列は書き換えない)。 その端子が挿せる数を超える分は、 他の出目を後ろから外す。</summary>
        public static int[] WithPair(int[] slots, int diceCount, List<string> board, int die, int slot)
        {
            var w = new int[diceCount];
            for (int i = 0; i < diceCount; i++) w[i] = slots != null && i < slots.Length ? slots[i] : -1;
            if (die < 0 || die >= diceCount || board == null || slot < 0 || slot >= board.Count) return w;
            w[die] = slot;
            int cap = Math.Max(1, TerminalCards.Get(board[slot])?.capacity ?? 1), used = 1;
            for (int i = 0; i < diceCount; i++) if (i != die && w[i] == slot) used++;
            for (int i = diceCount - 1; i >= 0 && used > cap; i--)
                if (i != die && w[i] == slot) { w[i] = -1; used--; }
            return w;
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

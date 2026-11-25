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

        /// <summary>[計装] 【再起動】した回数 / 【オーバークロック】した回数 (ランの始めに BOT が 0 へ戻す・ランの記録の rebootFired / overclockFired)。</summary>
        public static long RebootFired, OverclockFired;
        /// <summary>[計装] 【起爆】した回数 (ランの始めに BOT が 0 へ戻す・ランの記録の detonateFired)。</summary>
        public static long DetonateFired;
        /// <summary>[計装] 【ジャスト防御】が成立した回数 (ランの始めに BOT が 0 へ戻す・ランの記録の justGuardFired)。</summary>
        public static long JustGuardFired;
        /// <summary>[計装] 〈防人の軍標〉で撃がもう 1 回発火した回数 (2026-11-22・ランの記録の guardEchoFired)。</summary>
        public static long GuardEchoFired;
        /// <summary>[計装] 秘宝が敵の出血を使った回数 (Kw.ConsumeBleed・〈血桜〉〈紅の搾り具〉) / 〈百人切りの鉈〉で処刑した回数 (ランの始めに BOT が 0 へ戻す・ランの記録の bleedSpendFired / bleedExecuteFired)。</summary>
        public static long BleedSpendFired, BleedExecuteFired;
        /// <summary>[計装] 臨界の爆発の回数 / 〈柱折りの発破筒〉でダウンを 1 ターン延ばした回数 (2026-11-25〜・旧: 爆発のひびを割った回数) (ランの始めに BOT が 0 へ戻す・ランの記録の burstFired / burstBreakFired)。</summary>
        public static long BurstFired, BurstBreakFired;
        /// <summary>[計装] 超過会心率 (100% 超え) が会心倍率になった会心の回数 / 〈天眼の鍔〉で処刑した回数 / 〈必殺の照準器〉がもう 1 回発火した回数
        /// (ランの始めに BOT が 0 へ戻す・ランの記録の critOverflowFired / critExecuteFired / critRefireFired)。</summary>
        public static long CritOverflowFired, CritExecuteFired, CritRefireFired;
        /// <summary>[計装 2026-11-23] 【集中】の撃の回数 / 集中会心の回数 / 集中会心で使った【練気】の合計 (ランの始めに BOT が 0 へ戻す・ランの記録の focusAttempted / focusCritted / renkiSpentTotal)。</summary>
        public static long FocusAttempted, FocusCritted, RenkiSpentTotal;
        /// <summary>[計装] 使い捨ての端子を使った回数 / 山札を切り直した回数 (ランの始めに BOT が 0 へ戻す・ランの記録の deckDisposeFired / deckShuffleFired)。</summary>
        public static long DeckDisposeFired, DeckShuffleFired;
        /// <summary>[計装] 役が成立した回数 / 〈万象のゾロ目〉が 2 個そろいをゾロ目として扱った回数 (ランの始めに BOT が 0 へ戻す・ランの記録の roleFormedFired / roleOmniFired)。</summary>
        public static long RoleFormedFired, RoleOmniFired;
        /// <summary>[計装] 武器: 〈連刃〉の追撃 / 〈処刑〉で倒した / 〈画竜点睛〉が働いた撃 / 〈シールドバッシュ〉でシールドを攻撃にした
        /// (ランの始めに BOT が 0 へ戻す・ランの記録の weaponChainFired / weaponExecuteFired / weaponGaryoFired / weaponBashFired)。</summary>
        public static long WeaponChainFired, WeaponExecuteFired, WeaponGaryoFired, WeaponBashFired;

        /// <summary>この戦闘の山札 (画面・BOT が読む)。 最初の戦闘の前だけ null。 書き換えないこと。</summary>
        public TerminalDeckState TerminalDeck => _deck;
        /// <summary>このターンの端子の結果 (画面が発動した端子を光らせるのに使う)。</summary>
        public TerminalOutcome LastTerminalOutcome => _termOutcome;

        private void BeginTerminalDeck()
        {
            _termOutcome = null; _termRetain = null; _termDamageMul = 1f; _termKillPending = null;
            CardRules.Load();   // 挑戦カードの値 (端子の解決・軸の式・撃の輪が読む) を写す。 札が無ければ全部 0
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
            // 山札の品: 端子の解決と山札の出し入れを変える品の旗 (本体も BOT の写しも state から読む)
            _deck.state.deckItems = TerminalCards.DeckItemsOf(run);
            _deck.state.roleItems = TerminalCards.RoleItemsOf(run);      // 賽目の品 (2026-11-21・段 1-f)
            _deck.state.chargeItems = ChargeCore.ItemsOf(run);           // 充電の品 (再起動・オーバークロック・2026-11-21)
            _deck.state.poisonItems = PoisonCore.ItemsOf(run);           // 毒の品 (端子の解決の 5d・起爆の式・2026-11-21)
            _deck.state.shieldItems = ShieldCore.ItemsOf(run);           // シールドの品 (端子の解決の 5e・ジャスト防御の許容幅・2026-11-21)
            _deck.state.bleedItems = BleedCore.ItemsOf(run);             // 出血の品 (端子の解決の 5f・撃ごとの出血・2026-11-21)
            _deck.state.heatItems = HeatCore.ItemsOf(run);               // 臨界の品 (爆発の式・端子の解決の 5g・2026-11-21)
            if (AxisRules.Owns(run, GameLoop.ItemIds.DeckSparePlug)) _deck.state.extraDrawNext += TerminalCards.SparePlugDraw;      // 〈予備の差込口〉: 1 ターン目に多く引く
            _deck.state.extraDrawNext += MetaProgression.MetaBuffApplicator.GetSparkDeckOpeningDraw();      // 種火・山札 (2026-11-21): 1 ターン目の手札 +1
            _deck.state.extraDrawNext += CardRules.OpeningDraw;      // 挑戦カード〈手捌き〉: 1 ターン目の手札 +N
            if (AxisRules.Owns(run, GameLoop.ItemIds.DeckToolbox) && runDeck.Count <= TerminalCards.ToolboxMaxDeck)
                _deck.state.handBonus = TerminalCards.ToolboxHand;      // 〈整理された工具箱〉: 戦闘開始時の山札で決める (戦闘中に増えても変えない)
            if ((_deck.state.deckItems & DeckItems.Workbench) != 0) _deck.MovePermanentsToBoard();      // 〈常設の作業台〉: 山の常設を全部場へ
            // 時計の賽 (ADR-0015): 〈時計〉の端子を常設として場に置く (山札には入れない・戦闘の間ずっと場にある)
            if (GameLoop.EquippedDie.IdOf(run) == GameLoop.DieIds.Clock) _deck.permanent.Add(TerminalIds.Clock);
            TerminalDiag.NoteBattle(runDeck);   // [計装 2026-11-12]
        }

        /// <summary>ターンの始め: 手札を引く。 持ち越し (条件無視・多く引く・溜めた攻撃・鋳造の出目) を今のターンへ移す。</summary>
        private void DrawTerminalHand(CombatContext ctx)
        {
            _termKillPending = null;
            _termWanted = null;
            if (_deck == null) return;
            int extra = _deck.state.BeginTurn();
            // ヴェスカ〈停滞する時間〉: 引いた端子 1 枚ごとに確率で止める (持たない敵は 0)
            float stop = ItemSystem.PassiveSkills.Effects.StagnantTime.StopChance(currentEnemy);
            if (ctx != null && TenyoHeavy.RingStopsAt(ctx.currentTurn)) stop = 1f;      // 天与の指輪 (2026-11-15): 大技の次のターンは、 引いた端子を全部止める
            int pursuit = BossDown.ActiveNow ? CardRules.PursuitHand : 0;      // 挑戦カード〈追い打ち〉: ボスのダウン中のターン、 手札 +N
            int clog = ctx != null ? MetaProgression.MetaDebuffApplicator.GetClogStops(ctx.currentTurn) : 0;      // 挑戦カード〈目詰まり〉・クェシナの怠惰: 引いた端子を N 枚止める
            _deck.DrawTurn(TerminalCards.HandSize + extra + _deck.state.handBonus + pursuit, _termRetain, stop, clog);      // handBonus = 〈整理された工具箱〉
            if (clog > 0 && Debug.unityLogger.logEnabled) Debug.Log($"[目詰まり] T{ctx.currentTurn}: 引いた端子のうち {_deck.LastStoppedCount} 枚が止まった");
            TerminalDiag.NoteDrawn(_deck.LastDrawnIds);   // [計装 #106] 新しく引いた端子
            RaiseDeckShuffles(ctx);   // 引く時に切り直した (2026-11-20)
            if (TerminalCards.HandCarry && ctx != null && ctx.currentTurn > 1) BossDownDiag.NoteCarry(_deck.LastCarriedCount);   // [計装 2026-11-13] 持ち越した枚数 (戦闘の 1 ターン目は除く)
            if (stop > 0f)
            {
                GuardDiag.NoteStag7F(currentEnemy?.id, _deck.LastDrawnCount, _deck.LastStoppedCount, _deck.LastStoppedMains, _deck.LastHeldCount);   // [計装]
                if (_deck.LastStoppedCount > 0)
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[停滞する時間] 引いた {_deck.LastDrawnCount} 枚のうち {_deck.LastStoppedCount} 枚が止まった (確率 {stop:P0})");
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
            // 軸の端子が読む戦闘の状態 (2026-11-22・旧: 偏重 = 軸の品の数)
            e.critsBattle = ItemSystem.PassiveSkills.Effects.Crit.Count(ctx, ItemSystem.PassiveSkills.Effects.Crit.TotalKey);
            e.justGuardLast = ItemSystem.PassiveSkills.Effects.Shd.LastTurn(ctx, ItemSystem.PassiveSkills.Effects.Shd.JustGuardTurnKey);
            int burstTurn = (int)ctx.GetAccumulated(ItemSystem.PassiveSkills.Keywords.Kw.BurstTurnKey);
            e.heatHot = burstTurn > 0 && (burstTurn == ctx.currentTurn || burstTurn == ctx.currentTurn - 1);
            e.hpLostBattle = _hpAtCombatStart - playerHP + ctx.healAppliedTotal > 0;      // CombatResult.HpLostThisCombat と同じ数え方 (〈応急箱〉)
            // 賽のパッシブ (ADR-0015・DiePassives)
            e.dieId = GameLoop.EquippedDie.IdOf(run);
            e.diceRerolled = ctx.rerollsThisTurn > 0;
            int emax = 0;
            if (_enemyDiceNow != null) foreach (int d in _enemyDiceNow) if (d > emax) emax = d;
            e.enemyMaxDie = emax;
            e.heavyTurn = Escalation.IsHeavyTurn(currentEnemy, ctx.currentTurn);
            e.chargeCostCut = ctx.ChargeCostCut;   // 〈過負荷チューナー〉: 過充電の間 2 減る (最低 1)
            e.chargeSpendsTurn = (int)ctx.GetAccumulated(CombatContext.ChargeSpendsThisTurnKey);   // 〈灼線鞭〉: 配線より前に充電を使った回数
            e.noAttack = GameLoop.Consumables.RapierContreOn(ctx);   // 〈シュヴァリエのレイピア〉のコントラタック中は攻撃の端子に挿せない
            e.sealedRoleMask = ctx.sealedRoleMask;                    // 立つ役 (TerminalCards.RoleOf) は ResolveRoles と同じ判定 (2026-11-21)
            e.roleDiceCut = (int[])_slothCut.Clone(); e.roleDiceAt = (int[])_slothAt.Clone();   // クェシナの怠惰: 立つ役は引く前の目で決める (RoleDice と同じ)
            e.pairAsTriple = OmniPairNow(ctx);                        // 〈万象のゾロ目〉: このターン 2 個そろいをゾロ目として扱う (振り直しの後に決める)
            return e;
        }

        /// <summary>ブロックの最終値 (端子の防御の合計 <paramref name="raw"/>・防御の端子に挿した個数 <paramref name="blockDice"/>・このターンの敵の攻撃 <paramref name="enemyAtk"/>)。
        /// 本体の配線の集計 (CombatManager.Turn の「5. 配線」) と、 ジャスト防御の見込み (<see cref="JustGuardForecast"/>・画面と BOT) が同じこの関数を通す。
        /// 順: 遺物〈堅牢の環〉+% → カイロスの傲慢 −% → 端子調律 (1 本あたり) → 品のブロック +N → 〈貫きの錐〉→〈狂暴化〉(0) →〈不完全な転移〉。</summary>
        public int FinalBlockSum(CombatContext ctx, int raw, int blockDice, int enemyAtk, bool log)
        {
            int b = raw;
            float cut = MetaProgression.MetaDebuffApplicator.GetSinBlockCutPct();     // カイロスの傲慢 (挑戦カード IV): −N% (四捨五入・デバフの丸め)
            if (cut > 0f && b > 0) b = Mathf.RoundToInt(b * (1f - cut / 100f));
            b += blockDice * MetaProgression.MetaBuffApplicator.GetBlockTerminalPerDice();   // 端子調律メタバフ: 接続したダイス 1 本あたり +N
            if (ctx != null) b += ctx.itemBlockBonus;                                  // 品のブロック +N (〈帯電の胸甲〉〈絶縁の外套〉〈避雷の杭〉ほか)
            if (ctx != null && ctx.currentTurn == 1) b += MetaProgression.MetaBuffApplicator.GetOpeningBlock();   // 整備盤〈緊急冷却ピン〉: 各戦闘の 1 ターン目 +N (2026-11-23)
            //   〈貫きの錐〉(7層ヴェスカ): 敵の攻撃値 1 につきブロックを 2 削る (端子調律の本数ボーナスも削る対象なので加算の後)
            if (ctx != null && ctx.playerBlockIgnored && b > 0)
            {
                int before = b;
                b = Math.Max(0, b - Math.Max(0, enemyAtk) * PierceAwlPerAttack);
                if (log) Debug.Log($"[貫きの錐] 敵攻撃{enemyAtk} × {PierceAwlPerAttack} → ブロック {before} → {b}");
            }
            if (ctx != null && ctx.enemyAttackIgnoresGuard) b = 0;      // 〈狂暴化〉(51T〜): ブロックは何も受けない
            if (b > 0)      // 〈不完全な転移〉(門を不完全に起動した罰): ブロック量に比例して削る (錐の後)
            {
                int beforeGate = b;
                b = GameLoop.GateFlaws.ApplyTransferPierce(GameLoop.GameManager.Instance?.Run, b);
                if (log && b != beforeGate) Debug.Log($"[不完全な転移] ブロック貫通 {beforeGate} → {b}");
            }
            return b;
        }

        /// <summary>【ジャスト防御】の見込み (配線の見込みの画面・BOT が読む): 挿し方の結果 <paramref name="o"/> で、 敵の攻撃を受ける時の防御 (ブロック + シールド) が
        /// <see cref="ShieldCore.IsJustGuard"/> を満たすか。 本体の判定と同じ式・同じブロックの関数 (<see cref="FinalBlockSum"/>)。
        /// 見ないもの: 敵の攻撃までに品が足す / 使うシールド (〈破城斧〉の消費・OnWired の品のシールド)。 〈狂暴化〉の攻撃は対象外。</summary>
        public bool JustGuardForecast(CombatContext ctx, TerminalOutcome o, in TerminalEnv env, out int enemyAttack, out int guard)
        {
            enemyAttack = ShieldCore.EnemyAttackAfter(o, env.enemyAttack); guard = 0;
            if (o == null || ctx == null || ctx.enemyAttackIgnoresGuard || enemyAttack <= 0) return false;
            guard = FinalBlockSum(ctx, o.blockAdd, o.blockDice, enemyAttack, log: false) + ShieldCore.ShieldAfterWiring(o, in env);
            return ShieldCore.IsJustGuard(enemyAttack, guard, ShieldCore.Width(_deck?.state.shieldItems ?? ShieldItems.None));
        }

        /// <summary>端子の結果のうち、 攻撃・防御・充電の合計以外を戦闘へ反映する (合計は呼び手が受け取る)。</summary>
        private void ApplyTerminalOutcome(CombatContext ctx, TerminalOutcome o, ref int enemyAtkValue)
        {
            _termDamageMul = o.damageMul;
            DeckDisposeFired += o.disposedNow;      // [計装]
            // 充電の支払いは 1 回ずつ (「充電を使うたび」の品が回数と量を数える・端子 → 再起動 → 〈逆さ避雷針〉 → オーバークロックの順)
            foreach (int pay in o.chargePays) ctx.ConsumeCharge(Math.Min(pay, ctx.GetCharge()));
            if (o.takenFlatCut > 0) ctx.playerFlatDamageReduction += o.takenFlatCut;      // 〈逆さ避雷針〉
            if (o.rebooted.Count > 0 || o.overclock)
            {
                RebootFired += o.rebooted.Count; if (o.overclock) OverclockFired++;      // [計装] BOT のサマリー
                if (Debug.unityLogger.logEnabled) Debug.Log($"[充電] 再起動 {string.Join(" / ", o.rebooted.ConvertAll(i => _deck != null && i < _deck.board.Count ? _deck.board[i] : i.ToString()))}"
                        + (o.overclock ? "・オーバークロック" : ""));
            }
            if (o.chargeCapAdd > 0) ctx.chargeCapBonus += o.chargeCapAdd;      // 〈大導電＋〉: この戦闘の充電の上限 (満たす前に)
            if (o.chargeFill) ctx.AddCharge(ctx.ChargeCap);
            if (o.rerollDiscountNext > 0) YachtRoleEffects.AddNextTurn(ctx, RerollDiscountKey, o.rerollDiscountNext);      // 〈賽振り台〉(2026-11-21)
            //   〈応急箱＋〉: 回復の量のうち、 最大 HP を超えた分 (回復する前の空きを超えた分) はシールドになる
            int overheal = o.overhealToShield > 0 ? Math.Max(0, o.overhealToShield - Math.Max(0, playerMaxHP - playerHP)) : 0;
            if (o.heal > 0) HealPlayer(o.heal);
            if (overheal > 0) { ctx.consShield += overheal; ctx.shieldGainedTotal += overheal; ShieldDiag.Note("端子", overheal); }
            if (o.selfDamage > 0)
            {
                playerHP = Math.Max(1, playerHP - o.selfDamage);
                ctx.playerCurrentHP = playerHP;
            }
            if (o.shieldSpend > 0) ItemSystem.PassiveSkills.Keywords.Kw.SpendShield(ctx, o.shieldSpend);
            if (o.shield > 0)
            {
                ctx.consShield += o.shield; ctx.shieldGainedTotal += o.shield;
                ShieldDiag.Note("端子", o.shield);
            }
            if (o.poison > 0) ItemSystem.PassiveSkills.Effects.Rw.Poison(ctx, o.poison);
            //   臨界: 〈燧石の指輪〉の倍を立ててから、 端子の分 → 品の分 (5g) を 1 つずつ (HeatCore.Forecast と同じ順)
            if (o.heatDouble) ctx.accumulatedValues[HeatCore.DoubleTurnKey] = ctx.currentTurn;
            if (o.heat > 0 || o.heatGains.Count > 0) ItemSystem.PassiveSkills.Keywords.Kw.EnableHeat(ctx);
            if (o.heat > 0) ItemSystem.PassiveSkills.Keywords.Kw.AddHeat(ctx, o.heat, "端子");
            foreach (int g in o.heatGains) ItemSystem.PassiveSkills.Keywords.Kw.AddHeat(ctx, g, "臨界の品(配線)");
            for (int r = 0; r < o.retreatStage; r++) YachtRoleEffects.AddEscalationDelay(ctx, YachtRoleEffects.TurnsToRetreatOneStage(ctx));
            if (o.escalationDelay > 0)      // 時計の賽 (ADR-0015)
            {
                YachtRoleEffects.AddEscalationDelay(ctx, o.escalationDelay);
                if (Debug.unityLogger.logEnabled) Debug.Log($"[賽] 時計の賽: エスカレーションの次の段階を {o.escalationDelay} ターン先へ");
            }
            if (o.enemyStunNext) ctx.nextTurnBuffs[YachtRoleEffects.EnemyStunKey] = 1f;
            if (o.ignoreDefense) ctx.playerArmorPen += DefenseFormula.IgnoreAll;
            if (o.armorPen > 0) ctx.playerArmorPen += o.armorPen;      // 〈継ぎ目のない導線〉
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

        /// <summary>配線の解決の直後 (<see cref="ApplyTerminalOutcome"/> の後・OnWired の前): 端子の出来事を配る (2026-11-20・段 1-e・item-overhaul-spec §2-2)。
        /// 判断の間に消耗品・品で引いて切り直した分の OnDeckShuffle もここで配る。 使い捨て = 1 枚につき OnTerminalDispose (量 1)・
        /// 補助 (基本を除く) = OnSupportTerminalUse・残す印で残した端子 = OnKeptTerminalUse (量 = 挿した出目の合計・最低 1・名前で数える)。
        /// ここはまだターンの解決中なので、 品が入れる敵へのダメージ (Kw.DamageEnemy) はこのターンの攻撃の固定ダメの枠で通る。</summary>
        private void RaiseTerminalEvents(CombatContext ctx, TerminalOutcome o)
        {
            if (_deck == null || ctx == null || o == null) return;
            RaiseDeckShuffles(ctx);
            var kept = _deck.state.kept != null ? new List<string>(_deck.state.kept) : null;
            for (int i = 0; i < o.fired.Count; i++)
            {
                int s = o.fired[i];
                if (s < 0 || s >= _deck.board.Count) continue;
                string id = _deck.board[s];
                var d = TerminalCards.Get(id);
                if (d == null) continue;
                int amt = Math.Max(1, i < o.firedSums.Count ? o.firedSums[i] : 0);
                if (d.disposable) ItemSystem.PassiveSkills.Keywords.Kw.Raise(ctx, PassiveSkillTrigger.OnTerminalDispose, 1);
                if (d.kind == TerminalKind.Support && !d.basic) ItemSystem.PassiveSkills.Keywords.Kw.Raise(ctx, PassiveSkillTrigger.OnSupportTerminalUse, amt);
                if (kept != null && s >= _deck.permanent.Count && kept.Remove(id)) ItemSystem.PassiveSkills.Keywords.Kw.Raise(ctx, PassiveSkillTrigger.OnKeptTerminalUse, amt);
            }
        }

        /// <summary>まだ配っていない切り直しの出来事 (OnDeckShuffle・量 = 切り直した枚数) を配る。 ctx が null なら捨てずに次へ持ち越す。</summary>
        private void RaiseDeckShuffles(CombatContext ctx)
        {
            if (_deck == null || ctx == null) return;
            foreach (int n in _deck.TakeShuffled()) { DeckShuffleFired++; ItemSystem.PassiveSkills.Keywords.Kw.Raise(ctx, PassiveSkillTrigger.OnDeckShuffle, n); }
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
                if (Debug.unityLogger.logEnabled) Debug.Log($"[端子/魂狩り] 最大 HP +{kb.maxHp}");
            }
            if (kb.whetOn) { TerminalDiag.Note(TerminalIds.Whet, TerminalDiag.KillBonus); run.terminalWhetBonus += kb.whet; Debug.Log($"[端子/処刑の刃] 攻撃 +{kb.whet} (計 {run.terminalWhetBonus})"); }
            if (kb.loot) { TerminalDiag.Note(TerminalIds.LootBox, TerminalDiag.KillBonus); run.coins += kb.gold; Debug.Log($"[端子/賞金狩り] ゴールド +{kb.gold}"); }
            if (kb.refundHp > 0)
            {
                // 〈諸刃の剣＋〉: 払った HP が戻る (回復ではないので、 回復を減らす効果は掛からない・最大 HP は超えない)
                playerHP = Math.Min(playerMaxHP, playerHP + kb.refundHp);
                var rctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
                if (rctx != null) rctx.playerCurrentHP = playerHP;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[端子/諸刃の剣＋] 払った HP が戻る +{kb.refundHp}");
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
                int at = deck.IndexOf(id);
                if (!GameLoop.GameManager.CanRemoveTerminalAt(GameLoop.GameManager.Instance.Run, at)) continue;      // 最後の攻撃の端子は抜かない (at < 0 = 山札に無い)
                deck.RemoveAt(at); TerminalDiag.NoteRemoved(id, TerminalDiag.LeftRunDeck);   // [計装 #106]
                if (Debug.unityLogger.logEnabled) Debug.Log($"[端子/{id}] 使ったので、ランの山札から抜いた (山札 {deck.Count} 枚)");
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
        /// state を渡すと、 止まった端子 (ヴェスカ〈停滞する時間〉・挑戦カード〈目詰まり〉) へ挿す手は出さない (Resolve が外すのと同じ結果になる手を数え直さないため)。
        /// クァディルの色欲 (<see cref="CardRules.MaxWiredDice"/>) の時は、 挿す出目がその数を超える手も出さない (Resolve が外す)。</summary>
        public static List<int[]> Enumerate(List<string> board, int diceCount, TerminalBattleState state = null)
        {
            var res = new List<int[]>();
            int m = board.Count;
            var cap = new int[m];
            for (int s = 0; s < m; s++) cap[s] = state != null && state.IsStopped(s) ? 0 : TerminalCards.Get(board[s])?.capacity ?? 0;
            int maxWired = CardRules.MaxWiredDice > 0 ? CardRules.MaxWiredDice : diceCount;
            var cur = new int[diceCount];
            void Rec(int i, int wired)
            {
                if (i == diceCount) { res.Add((int[])cur.Clone()); return; }
                cur[i] = -1; Rec(i + 1, wired);
                if (wired >= maxWired) return;
                for (int s = 0; s < m; s++)
                {
                    if (cap[s] <= 0) continue;
                    cap[s]--; cur[i] = s; Rec(i + 1, wired + 1); cap[s]++;
                }
            }
            Rec(0, 0);
            return res;
        }

        /// <summary>既定の挿し方: 全部の挿し方を試して、 素朴な点 (攻撃 + 防げる分 + 充電と回復の一部) が最も高いものを選ぶ。
        /// 本気の方策ではない ── BOT は自分の評価で選ぶ (AutoRunner)。</summary>
        public static int[] Auto(List<string> board, int[] dice, TerminalEnv env, TerminalBattleState state)
        {
            int[] best = null; float bestScore = float.NegativeInfinity;
            foreach (var w in Enumerate(board, dice.Length, state))
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

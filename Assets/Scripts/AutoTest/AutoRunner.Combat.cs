using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using GameLoop;
using CombatSystem;
using MapSystem;
using EventSystem;
using ItemSystem;
using ItemSystem.Shop;

namespace AutoTest
{
    /// <summary>AutoRunner: 戦闘の実行と配線・リロール方策・回復の使い方。</summary>
    public partial class AutoRunner
    {
        /// <summary>戦闘をターン逐次で進行。開始直後にバフ消費、各ターン前に緊急回復判定。</summary>
        private void RunCombatWithItems(GameManager gm)
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsCombatActive) return;
            var run = gm.Run;

            // バフ/シールドは「重要戦闘」のみ。雑魚相手の初手オールインは非合理なので回避。
            // 重要 = ボス / 高脅威(threat>=5) / 一撃が現HPの半分以上を奪い得る危険戦闘。
            var e0 = cm.CurrentEnemy;
            bool important = false;
            var polC = AutoTest.PolicyParameters.Current;
            if (e0 != null)
            {
                bool boss = GameLoop.BossIds.IsBoss(e0.id);
                // 敵の最大単発 = 基礎攻撃 + ロール上限 (攻撃ロール方式・旧ダイス方式の両方に対応)
                int maxHit = (e0.EffectiveBaseAttack + e0.EffectiveRollCount * e0.EffectiveRollMax)
                           * (e0.criticalNumerator > 0 ? 2 : 1);
                important = boss || e0.threat >= polC.importantThreatThreshold
                                 || maxHit * 2 >= Math.Max(1, cm.PlayerHP);
            }

            if (important)
            {
                // 2026-05-31: LEG (Lv4) は本当の窮地のみ使用 (BOT 過剰消費抑制 = LEG 評価低下対策 C案)。
                //   desperate = ボス OR HP残り33%以下 → LEG 解禁
                //   通常 important → GOLD(Lv3)以下のみ
                bool desperate = GameLoop.BossIds.IsBoss(e0?.id)
                              || (run.playerMaxHP > 0 && cm.PlayerHP * 3 <= run.playerMaxHP);
                // 2026-08-04 消費アイテム再編: 攻撃強化 (cons_dmg_*) / シールド (cons_def_*) の
                //   2 カテゴリを Tier 順に使う。 cons_atk_* は廃止 (与ダメ% へ統合)。
                //   desperate: T4 から / 通常: T2 以下 (上位を温存)。
                if (desperate)
                {
                    UseFirst(run, ItemIds.PowerOil4, ItemIds.PowerOil3, ItemIds.PowerOil2, ItemIds.PowerOil1);
                    if (!v3.superConsumables) UseFirst(run, ItemIds.Talisman4, ItemIds.Talisman3, ItemIds.Talisman2, ItemIds.Talisman1);
                }
                else
                {
                    UseFirst(run, ItemIds.PowerOil2, ItemIds.PowerOil1);
                    if (!v3.superConsumables) UseFirst(run, ItemIds.Talisman2, ItemIds.Talisman1);
                }

                // 2026-06-28: 職業スターター消耗品の使い時判定
                // 不抜の聖紋 (騎士): HP 90% 以上で発動 (80% gate を最大活用)
                if (run.playerMaxHP > 0 && cm.PlayerHP * 10 >= run.playerMaxHP * 9)
                    UseFirst(run, GameLoop.ClassStarter.OathId);
                // 仕込み刃 (暗殺者): このターン限り。 強敵戦の冒頭で切る (予告攻撃を見た判断はまだ入れていない)
                UseFirst(run, GameLoop.ClassStarter.DaggerId);
                // 瞬間研磨剤 (剣士): 1 ロール burst なので強敵戦冒頭で切る
                UseFirst(run, GameLoop.ClassStarter.PolishId);
                // 痛覚遮断剤 (狂戦士): desperate (ボス + HP 33% 以下) のときのみ
                if (desperate) UseFirst(run, GameLoop.ClassStarter.PainkillerId);
            }

            // ADR-0009/0010: 配線・リロール・役の 3 方策を注入 (パイプライン OFF 時は未使用)。
            //   素朴版は **リロールと役を一切使わない** ＝ ADR-0010 以前と同じ打ち方。
            //   最適版との差が「役システムが開けた技量帯の幅」そのものになる (Verification ①)。
            WiringSkill effectiveSkill = CombatWiringSkill;   // [アブレーション 2026-11-17] combatNaive なら配線・リロールだけ素朴版
            // 6 層・7 層のボス戦で Super を Optimal の方策へ切り替える分岐 (superLayer6/7OptimalCombatRoutine) と、
            //   旧い先読み方策 (ExactCombatAI・SuperCombatAI) の BeginCombat は 2026-11-12 に削除した。
            //   切り替えの先も元も、 下で必ず同じ山札の方策に上書きされていたので、 分岐の結果を読む所が無かった。
            //   BeginCombat は自分の中のメモと観測を消すだけで、 共有の状態にも乱数にも触れていなかった。
            // 端子の山札 (ADR-0014): どの技量でも、 配線は山札用の方策 (AutoRunner.Terminals)、 リロールは Optimal の方策
            //   (山札の効用で値付けする) を差す。 素朴版はリロールしない。
            cm.WiringPolicy = DeckWiringPlanPolicy;
            cm.RerollPolicy = effectiveSkill == WiringSkill.Naive ? null
                            : (System.Func<int[], CombatSystem.MutualTurnTelegraph, int, int[]>)MutualRerollPolicy;

            _fbDecided = _fbTurns = _fbKillTurns = 0;   // [計装] 防ぎ切りの規則の数え直し
            EndCombatMainCorr();   // [V3 mainCorr] 前の戦闘の残りを締めてから、 この戦闘の累計を 0 から
            _fbUnavoidSeen = false; _fbPrevHp = -1; _fbPrevExpTaken = 0;
            int guard = 0;
            while (cm.IsCombatActive && guard++ < 250)
            {
                // シュヴァリエ戦専用: ボス形態に同期してレイピアをトグル
                AutoToggleRapierVsSaintGeorges(cm, run);

                // 緊急回復: 敵の最大ダイスダメージ(会心なら×2)で落ちそうなら回復
                var e = cm.CurrentEnemy;
                if (e != null)
                {
                    // 挑戦デバフとエスカレーションを含む共通推定を使う (2026-08-05)。
                    // 素ステータスだけだと長期戦で倍率が乗った実被弾を過小評価する。
                    // ctx を渡す ── 停滞/遺物/溜め打ちの実行時加算を含めないと 4 割過小になり、
                    //   回復を持ったまま一撃で落ちる死角ができる (EstimateMaxHit の doc 参照)。
                    int maxHit = EstimateMaxHit(e, cm.CurrentCombatTurn,
                                                ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context);
                    // 致死見積り: 主攻撃の最大 + そのターンに確実に入る継続ダメ。
                    //   大出血は軽減不可・毎ターン確定なので必ず足す (7層 p4 で被ダメの 25〜40%)。
                    var hctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
                    int chip = hctx != null
                             ? hctx.massiveBleedStacks
                               * ItemSystem.PassiveSkills.Effects.VescaRelicPool.MassiveBleedDamagePerStack
                             : 0;
                    int lethal = maxHit + chip;
                    int healTrigger = Mathf.RoundToInt(maxHit * polC.emergencyHealRatio);

                    if (cm.PlayerHP <= lethal)
                    {
                        // **死ぬ場面では温存しない。** 人間なら 100% しない判断なので BOT にもさせない。
                        //   2026-08-16 まで「T3/T4 は HP 1/4 以下まで温存」だったが、 7層後半は
                        //   1 撃が最大HP の 3 割入るため **HP 25〜40% の帯を 1 ターンで飛び越え**、
                        //   解除条件に一度も触れずに死んでいた。 実測: p4 敗死 278 件のうち 43% が
                        //   回復を所持したまま死亡し、 その内訳は T3 が 157 件・T4 が 29 件 ──
                        //   **温存ルールが大きい回復を死蔵させていた**。
                        UseBestHeal(run, cm.PlayerHP, lethal);
                    }
                    else if (cm.PlayerHP <= healTrigger)
                    {
                        // 死なないが削れている場面。 ここは従来どおり小さいものだけ使う
                        //   (大きい回復を上限で溢れさせない、 という温存の本来の意図)。
                        UseFirst(run, ItemIds.HealPotion2, ItemIds.HealPotion1);
                    }
                }
                // 逃げる (2026-09-28): 追跡者の戦闘は、 頭で HP が少なければ逃げる。
                if (cm.CurrentCombatTurn <= 1 && ShouldFleePursuer(run, GameManager.Instance) && cm.RequestFlee())
                    FreeMapPursuerFlees++;
                BeginTurnDealt();   // [計装 2026-11-17] 1 ターンの与ダメ
                CombatSystem.TurnResult tr;
                using (BotTiming.Enter(BotTiming.Cat.Resolve)) tr = cm.ExecuteTurn();
                NoteTurnDealt();
                if (tr.isDraw) _cwDraw++;
                else if (tr.playerWon) _cwWin++;
                else { _cwLoss++; if (tr.totalDamage <= 0) _cwLossAbs++; }
            }
            EndCombatMainCorr();   // [V3 mainCorr] この戦闘の累計をランの累計へ
        }


/// <summary>6F (灰燼の王) 撃破直後のビルド情報を _cur に記録。
        /// 装備/アイテム/HP/物資/各種デバフを 1 行プレーンテキストに圧縮。
        /// 後でサマリーから「どんな装備で 6F まで来たか」をサルベージする用途。</summary>
        private void Capture6FClearSnapshot(GameManager gm)
        {
            if (_cur == null || gm?.Run == null) return;
            var run = gm.Run;
            var sb = new System.Text.StringBuilder();
            sb.Append($"HP {run.playerHP}/{run.playerMaxHP} | coins {run.coins} | mat {run.weaponMaterials} | 武器 {run.equippedWeaponId} 限界突破{run.limitBreakStage} | 物資 {run.provision}/{run.provisionCap}[{GameLoop.ProvisionSystem.GetTier(run)}]");
            sb.Append($"\n      武器: {(string.IsNullOrEmpty(run.equippedWeaponId) ? "(無)" : run.equippedWeaponId)} | ダイス: {(string.IsNullOrEmpty(run.equippedDiceId) ? "(武器ダイス)" : run.equippedDiceId)}");
            int pCnt = run.ownedPassiveItems?.Count ?? 0;
            string pList = pCnt > 0 ? string.Join(", ", run.ownedPassiveItems) : "(無)";
            sb.Append($"\n      パッシブ({pCnt}): {pList}");
            int cCnt = run.ownedConsumables?.Count ?? 0;
            string cList = cCnt > 0 ? string.Join(", ", run.ownedConsumables) : "(無)";
            sb.Append($"\n      消費品({cCnt}): {cList}");
            int fCnt = run.ownedFlags?.Count ?? 0;
            if (fCnt > 0) sb.Append($"\n      フラグ({fCnt}): {string.Join(", ", run.ownedFlags)}");
            if (run.timedBuffs != null && run.timedBuffs.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in run.timedBuffs) parts.Add($"{kv.Key}×{kv.Value}");
                sb.Append($"\n      時限バフ: {string.Join(", ", parts)}");
            }
            if (run.timedDebuffs != null && run.timedDebuffs.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in run.timedDebuffs) parts.Add($"{kv.Key}×{kv.Value}");
                sb.Append($"\n      時限デバフ: {string.Join(", ", parts)}");
            }
            if (run.permanentDebuffs != null && run.permanentDebuffs.Count > 0)
                sb.Append($"\n      恒久デバフ: {string.Join(", ", run.permanentDebuffs)}");
            if (run.gateFlaws != GameLoop.GateFlaw.None)
                sb.Append($"\n      門の欠陥: {run.gateFlaws}");
            sb.Append($"\n      LS使用済: {run.lastStandActive} | サン=ジョリオラ撃破: {run.defeatedSaintGeorges}");
            _cur.clear6FSnapshot = sb.ToString();
        }

        /// <summary>このターンの被ダメの罰の倍率 (S2: 大きい攻撃の予告で 3 倍)。</summary>
        private float _takenMulNow = 1f;

        /// <summary>大きい攻撃の予告か: 3 個とも守っても止まらない、 または最大 HP の 20% 以上。</summary>
        private static bool IsHeavyAttack(int[] rolls, int atk, int maxHp)
        {
            int sum = 0;
            if (rolls != null) foreach (var d in rolls) sum += d;
            return atk > sum || atk * 5 >= maxHp;
        }

        /// <summary>Super の配線の前の判断: 大きい攻撃なら罰を上げる / 足りない分を護符で埋める / 致死なら回復薬 (S2・S3)。</summary>
        private void PrepareSuperTurn(int[] rolls, CombatSystem.MutualTurnTelegraph tele)
        {
            _takenMulNow = 1f;
            if (!v3.superDefense && !v3.superConsumables) return;
            var cm = CombatManager.Instance;
            var run = GameManager.Instance?.Run;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (cm == null || run == null || ctx == null || rolls == null) return;
            int atk = tele.enemyAttackValue + tele.secondaryAttackValue;
            bool heavy = IsHeavyAttack(rolls, atk, System.Math.Max(1, cm.PlayerMaxHP));
            if (v3.superDefense && heavy) _takenMulNow = 3f;
            if (!v3.superConsumables) return;
            int sum = 0; foreach (var d in rolls) sum += d;
            int shortfall = atk - sum - ctx.consShield;
            if (shortfall >= 10)
            {
                string pick = null, largest = null;
                foreach (var t in SuperTalismans)
                {
                    if (!run.ownedConsumables.Contains(t.id)) continue;
                    largest = t.id;
                    if (pick == null && t.amount >= shortfall) pick = t.id;
                }
                string use = pick ?? largest;
                if (use != null && GameLoop.Consumables.Use(run, use)) { shortfall = atk - sum - ctx.consShield; SuperTalismansUsed++; }
            }
            if (shortfall >= cm.PlayerHP) UseBestHeal(run, cm.PlayerHP, shortfall);
        }
        /// <summary>護符の段 (昇順)。 量は本体の <see cref="GameLoop.Consumables.ShieldAmount"/> を引く (写しを持たない・#129)。</summary>
        private static readonly (string id, int amount)[] SuperTalismans = TiersOf(GameLoop.Consumables.ShieldAmount,
            ItemIds.Talisman1, ItemIds.Talisman2, ItemIds.Talisman3, ItemIds.Talisman4);
        private static (string id, T value)[] TiersOf<T>(Func<string, T> amountOf, params string[] ids)
        {
            var a = new (string id, T value)[ids.Length];
            for (int i = 0; i < ids.Length; i++) a[i] = (ids[i], amountOf(ids[i]));
            return a;
        }
        /// <summary>[計装] Super が予告を読んで使った護符の数 (バッチ累計)。</summary>
        public static long SuperTalismansUsed;

        /// <summary>配線のミスの率 (2026-11-06・0 = しない)。 Optimal を 10〜100 戦の経験者とみなす: 人は毎ターン同じ規則で最善を選べない。
        /// <b>効くのは実効の技量が Optimal の時だけ</b> (Super・素朴版・Exact はミスの経路を通らず、 ミスの乱数も引かない)。
        /// 値は Begin が <see cref="optimalMistakeRate"/> から入れる (Super は 0)。
        ///
        /// <para><b>2026-11-12 (#134) に山札の方策 (<see cref="DeckWiringPlanPolicy"/>) へつなぎ直した。</b>
        /// 旧い配線 (攻撃・ブロック・充電の 3 端子) では「1 個を攻撃とブロックの反対へ挿す」形だったが、 山札では端子が手札ごとに違い、
        /// 「攻撃の反対」が決まらないので、 <b>ミス = 効用が最良より真に低い挿し方のうち最良のもの (次点) を選ぶ</b> と定義し直した
        /// (<see cref="PickMistakeIndex"/>)。 旧と同じく、 致死を見落とすミスはしない (次点が今ターンの被ダメの見込みで HP を失い切るなら、
        /// その次を見る・全部致死ならミスしない)。 最良と同点の手しか無い時もミスしない (同点の入れ替えは損をしない = ミスではない)。</para>
        ///
        /// <para>判定の乱数は旧と同じく局面 (敵 HP・自 HP・ターン・ラン番号・出目) のハッシュから作る専用の値で、 <b>GameRng を消費しない</b>
        /// (同じシードの対比較を壊さない)。 判定は 2 ターン先読み (<see cref="deckLookahead"/>) で選び直した後。</para></summary>
        public float wiringMistakeRate = 0f;

        /// <summary>このターンにミスするか (局面のハッシュ・GameRng を引かない)。 旧 MaybeMisWire と同じハッシュ。</summary>
        private bool RollWiringMistake(int[] rolls, CombatSystem.MutualTurnTelegraph tele)
        {
            var cm = CombatManager.Instance;
            if (rolls == null || cm == null) return false;
            uint h = (uint)(cm.EnemyHP * 73856093) ^ (uint)(cm.PlayerHP * 19349663) ^ (uint)(tele.turn * 83492791) ^ (uint)((_cur?.index ?? 0) * 2654435761u);
            foreach (var d in rolls) h = h * 31u + (uint)d;
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 65536f < wiringMistakeRate;
        }

        /// <summary>ミスで選ぶ挿し方の添字 (次点)。 効用が <paramref name="best"/> より真に低い手のうち最良で、 今ターンの被ダメの見込みで
        /// HP を失い切らないもの。 同点は列挙の順 (先の方)。 無ければ −1 (ミスしない)。</summary>
        private static int PickMistakeIndex(float[] us, OptimalTurnOutcome[] outs, int best, int hp)
        {
            int alt = -1; float au = float.NegativeInfinity;
            for (int i = 0; i < us.Length; i++)
            {
                if (i == best || !(us[i] < us[best])) continue;
                if (hp > 0 && outs[i].taken >= hp) continue;      // 死ぬミスはしない
                if (alt < 0 || us[i] > au) { alt = i; au = us[i]; }
            }
            return alt;
        }
        /// <summary>[計装] 配線のミスの回数 (バッチ累計)。</summary>
        public static long WiringMistakes;

        // ── [計装 2026-11-06] 配線の判断 (1 ターン 1 行) ──
        //   後半の被弾がブロックの幅の外にあると配線の判断が消える、 という見立てを層ごとに確かめる。
        //   列: 層 (ordinal) / 種別 (0 雑魚 1 精鋭 2 ボス) / ターン / 大技のターンか / 予告の敵攻撃 (プレイヤーの防御の後) /
        //       HP / 最大 HP / シールド / 出目の合計 / 攻撃・ブロック・充電に挿した出目の合計 / ブロックに挿した本数
        /// <summary>true のとき、 Optimal の配線ごとに 1 行を <c>turn_audit.tsv</c> (ワーカーの出力先) に書く。 既定 OFF。</summary>
        public bool turnAudit;
        private System.IO.StreamWriter _turnAuditWriter;

        private void WriteTurnAudit(int[] rolls, CombatSystem.MutualTurnTelegraph tele, CombatSystem.DiceTerminal[] w)
        {
            if (rolls == null || string.IsNullOrWhiteSpace(productionOutputRootOverride)) return;
            if (_turnAuditWriter == null)
                _turnAuditWriter = new System.IO.StreamWriter(System.IO.Path.Combine(productionOutputRootOverride, "turn_audit.tsv"), true, new System.Text.UTF8Encoding(false));
            var cm = CombatManager.Instance;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            var e = cm?.CurrentEnemy;
            int kind = GameLoop.BossIds.IsBoss(e?.id) ? 2
                     : MapSystem.MapManager.Instance?.CurrentNode?.EffectiveType.ToEnemyKind() == EnemyKind.Elite ? 1 : 0;
            int sum = 0, a = 0, b = 0, c = 0, nb = 0;
            for (int i = 0; i < rolls.Length; i++)
            {
                sum += rolls[i];
                var t = w != null && i < w.Length ? w[i] : CombatSystem.DiceTerminal.Attack;
                if (t == CombatSystem.DiceTerminal.Block) { b += rolls[i]; nb++; }
                else if (t == CombatSystem.DiceTerminal.Charge) c += rolls[i];
                else a += rolls[i];
            }
            _turnAuditWriter.WriteLine(string.Join("\t", new object[] {
                GameManager.Instance?.Run?.currentFloor ?? -1, kind, tele.turn, tele.turnsToHeavy == 0 && tele.heavyMul > 1f ? 1 : 0,
                tele.enemyAttackValue, cm?.PlayerHP ?? 0, cm?.PlayerMaxHP ?? 0, ctx?.consShield ?? 0, sum, a, b, c, nb }));
        }

        // ============================================================
        //  [計装 2026-11-10] 主攻撃の見積もり (配線時点) と実際の主攻撃の照合 (readOutgoingMult)
        // ============================================================
        /// <summary>true のとき、 配線ごとに主攻撃の見積もり (旧の式・倍率を読む式) と、 そのターンに実際に出た主攻撃
        /// (DamageBreakdown[7] の増分 = ヴェスカの防御の前・役の追撃込み) を pred_trace.tsv に書く。 判断には使わない。</summary>
        public bool predTrace;
        /// <summary>predTrace の行を書くラン数の上限 (index がこれ未満のランだけ)。</summary>
        public int predTraceRuns = 50;
        private System.IO.StreamWriter _predWriter;
        private int _lastPredOld, _lastPredNew, _lastPredA;
        // 見積もりに使った倍率の入力 (照合で「どこが外れたか」を段別の実測 CombatStats.DamageStages と並べる)
        private float _lastPredCritP, _lastPredCritMul, _lastPredOutMul; private int _lastPredRaw;
        private float _predCritP, _predCritMul, _predOutMul; private int _predRaw;
        private readonly double[] _predStg = new double[CombatSystem.CombatStats.DamageStages.Length];
        private bool _predPending;
        private int _predTurn, _predOld, _predNew, _predA;
        private string _predEnemy = "";
        private double _predBd7;

        private void BeginPredTrace(int turn)
        {
            FlushPredTrace();
            if ((_cur?.index ?? int.MaxValue) >= predTraceRuns) return;
            _predPending = true;
            _predTurn = turn; _predOld = _lastPredOld; _predNew = _lastPredNew; _predA = _lastPredA;
            _predEnemy = CombatManager.Instance?.CurrentEnemy?.id ?? "";
            _predBd7 = CombatSystem.CombatStats.DamageBreakdown[7];
            _predCritP = _lastPredCritP; _predCritMul = _lastPredCritMul; _predOutMul = _lastPredOutMul; _predRaw = _lastPredRaw;
            System.Array.Copy(CombatSystem.CombatStats.DamageStages, _predStg, _predStg.Length);
        }

        /// <summary>保留中の見積もりに、 その後に出た主攻撃を並べて 1 行書く (次の配線判断・段の切り替え・戦闘の終わりで呼ぶ)。</summary>
        private void FlushPredTrace()
        {
            if (!_predPending) return;
            _predPending = false;
            if (string.IsNullOrWhiteSpace(productionOutputRootOverride)) return;
            try
            {
                if (_predWriter == null)
                {
                    _predWriter = new System.IO.StreamWriter(System.IO.Path.Combine(productionOutputRootOverride, "pred_trace.tsv"), true,
                                                             new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                }
                int actual = (int)System.Math.Round(CombatSystem.CombatStats.DamageBreakdown[7] - _predBd7);
                var sg = CombatSystem.CombatStats.DamageStages;
                string S(int i) => (sg[i] - _predStg[i]).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                // 列: run 層 敵 T 攻撃本数 旧予測 新予測 実際 フラグ | 見積もり: atkBase 会心率 会心倍率 与ダメ倍率 |
                //     実測 (段の増分): A atkBase / B OnPreDealDamage 後 / D 会心後 / E 与ダメ倍率後 / I 防御後 / O 最終 / 会心回数 / 会心倍率の和 / 掛けた与ダメ倍率
                _predWriter.WriteLine(string.Join("\t", new object[] {
                    _cur?.index ?? -1, GameManager.Instance?.Run?.currentFloor ?? -1, _predEnemy, _predTurn, _predA,
                    _predOld, _predNew, actual, ReadOutgoingMultEffective ? 1 : 0,
                    _predRaw, F(_predCritP), F(_predCritMul), F(_predOutMul),
                    S(1), S(2), S(4), S(5), S(9), S(15), S(17), S(16), S(22) }));
            }
            catch (Exception ex) { Debug.LogWarning("[predTrace] " + ex.Message); }
        }

        // ============================================================
        //  [V3 mainCorr 2026-11-10] 主攻撃の実績の比 (実際に入った主攻撃 ÷ 配線時点の見積もり)
        // ============================================================
        //   実際 = CombatStats.DamageBreakdown[7] の増分 (predTrace と同じ: 敵の防御の後・ヴェスカの回避/半減/シールド/はみ出しの前・役の追撃込み)。
        //   見積もり = 効用に使った主攻撃 (比を掛ける前・OptimalTurnOutcome.dmgUsed)。 攻撃に 1 本以上挿したターンだけ積む。
        //   次の判断 (リロール・配線)・段の切り替え・戦闘の終わりで、 保留中のターンの実際を締める。 GameRng を使わない (読むだけ)。
        //   計装 (段ごとの見積もり・比を掛けた見積もり・実際・攻撃の本数の分布・黎明の溜めを撃った値) は切り替えに関わらず取る。
        private int _lastPredUsed;
        private double _mcRunAct, _mcRunPred;     // このランの、 終わった戦闘の累計
        private double _mcCmbAct, _mcCmbPred;     // 今の戦闘 (連戦は通算) の累計
        private bool _mcPending; private int _mcPendPred, _mcPendA; private double _mcPendBd7; private float _mcPendRatio = 1f;
        private float _mcRatioNow = 1f;
        // 段 (形態) ごとの計装
        private double _mfPred, _mfPredCorr, _mfAct;
        private readonly int[] _mfAtkHist = new int[4];

        private void SettleMainCorr()
        {
            if (!_mcPending) return;
            _mcPending = false;
            if (_mcPendA <= 0) return;
            double act = System.Math.Max(0.0, CombatSystem.CombatStats.DamageBreakdown[7] - _mcPendBd7);
            _mcCmbAct += act; _mcCmbPred += _mcPendPred;
            _mfAct += act; _mfPred += _mcPendPred; _mfPredCorr += _mcPendPred * (double)_mcPendRatio;
        }

        /// <summary>今の比: 今の戦闘の累計 → 無ければこのランの直前までの戦闘の累計 → 無ければ 1。</summary>
        private float MainCorrRatio()
        {
            if (_mcCmbPred >= 1.0) return (float)(_mcCmbAct / _mcCmbPred);
            if (_mcRunPred >= 1.0) return (float)(_mcRunAct / _mcRunPred);
            return 1f;
        }

        /// <summary>判断 (リロール・配線) の前: 前のターンの実際を締めて比を出し直す。</summary>
        private void RefreshMainCorr() { SettleMainCorr(); _mcRatioNow = MainCorrRatio(); }

        /// <summary>配線を決めた直後: このターンの見積もりを保留する (実際は次の判断か段の切り替え・戦闘の終わりで締める)。</summary>
        private void BeginMainCorrTurn(int aCount)
        {
            _mfAtkHist[System.Math.Min(3, System.Math.Max(0, aCount))]++;
            _mcPending = true; _mcPendPred = System.Math.Max(0, _lastPredUsed); _mcPendA = aCount;
            _mcPendBd7 = CombatSystem.CombatStats.DamageBreakdown[7]; _mcPendRatio = _mcRatioNow;
        }

        /// <summary>戦闘の開始と終わり: 保留を締め、 今の戦闘の累計をランの累計へ移す。</summary>
        private void EndCombatMainCorr()
        {
            SettleMainCorr();
            _mcRunAct += _mcCmbAct; _mcRunPred += _mcCmbPred;
            _mcCmbAct = _mcCmbPred = 0;
            _mcRatioNow = MainCorrRatio();
        }

        /// <summary>ランの開始: ランをまたいで持ち越さない (決定性・チャンク分割で結果を変えない)。</summary>
        private void ResetMainCorrRun()
        {
            _mcRunAct = _mcRunPred = _mcCmbAct = _mcCmbPred = 0;
            _mcPending = false; _mcRatioNow = 1f; _mcPendRatio = 1f;
            ResetMainCorrForm();
        }

        private void ResetMainCorrForm()
        {
            _mfPred = _mfPredCorr = _mfAct = 0;
            System.Array.Clear(_mfAtkHist, 0, _mfAtkHist.Length);
        }

        /// <summary>バッチ終了時に閉じる (worker が呼ぶ)。</summary>
        public void CloseTurnAudit()
        {
            // [計装 2026-11-10] ctx を通した削りの品別 (EnemyHpLedger.CtxBySource) を書き出す (predTrace のときだけ)
            if (predTrace && !string.IsNullOrWhiteSpace(productionOutputRootOverride))
                try
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var kv in CombatSystem.EnemyHpLedger.CtxBySource)
                        sb.Append(kv.Key).Append('\t').Append(kv.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                    System.IO.File.WriteAllText(System.IO.Path.Combine(productionOutputRootOverride, "ctx_direct_sources.tsv"), sb.ToString(),
                                                new System.Text.UTF8Encoding(false));
                }
                catch (Exception ex) { Debug.LogWarning("[ctxSources] " + ex.Message); }
            try { _turnAuditWriter?.Flush(); _turnAuditWriter?.Dispose(); } catch { }
            _turnAuditWriter = null;
        }

        // ============================================================
        //  [計装 2026-09-19] 先読み (Super) と 1 ターン最善 (Optimal) の配線の食い違い
        // ============================================================
        /// <summary>防ぎ切りの規則 (2026-11-05)。 このターンのブロックで、 エスカレーションで強くなった後の敵の攻撃まで
        /// 撃破までずっと防ぎ切れる配線 (または今ターンで倒せる配線) を、 それ以外より優先する。
        ///
        /// <para><b>なぜ要るか。</b> 効用は「与ダメ + その 1 ターンで防げた量 − 4 × 撃破までのターン数」で、
        /// HP を失うこと自体のコストを持たない。 攻撃が小さい敵 (層 1 の攻撃 2〜5) だと、 ダイス 1 個で全部防げるのに
        /// 「延びる罰」に負けて全部攻撃に挿し、 毎ターン被弾していた (層 1 通常戦の損耗 18.3%・ブロックを使ったターン 15%)。
        /// 先読みする Super は同じ戦闘で損耗 6.0%・ブロック 75% (手動プレイの「1 戦 10% 未満」と一致)。</para>
        ///
        /// <para><b>亀にならない理由。</b> 時間のコストを被弾量で測った旧案 (taken × ttk・2026-08-04 に棄却) は、 防ぎ切れる相手で
        /// 罰が 0 に張り付いた。 ここでは時間のコストをエスカレーションで測る ── 撃破までに敵の攻撃が伸びてブロックを
        /// 上回るなら規則は効かず、 従来の効用に戻る。 攻撃端子 0 本の配線は従来どおり禁止。</para></summary>
        // インスタンスのフィールド (2026-11-05): 並列ワーカーへ設定として運ぶため。 static だと切り替えられなかった。
        public bool FreeBlockRule = true;
        /// <summary>防ぎ切れる配線への加点。 ほかの項より十分大きく、 致死回避 (−10000) より小さい。</summary>
        private const float FreeBlockBonus = 1000f;

        private const int HorizonCapTurns = 12;
        private const float TimeCostPerTurn = 4.0f;

        /// <summary>配線の効用の入力 (2026-11-05・SuperV3)。 <see cref="OptimalUtility"/> が読む盤面の値を全部ここに集める。
        /// 生きている盤面 (CombatManager / PassiveSkillManager) の読み出しは MutualWiringPolicy が 1 回だけ行い、
        /// 効用の側は盤面に触らない ── 先読みでは『このターンを解いた後の盤面』を同じ型で作って渡すため。
        /// 配線の判断中は読むだけ (書き換えない)。 並列評価からも呼ばれる。</summary>
        private struct OptimalTurnState
        {
            public CombatSystem.MutualTurnTelegraph tele;   // 敵の予告 (攻撃値・防御・シールド・回避・烈炎 など)
            public int enemyHp, myHp, myMaxHp;
            public int atkBase;                // 麻痺毒の減算後の素の攻撃力
            public int enemyAtk;               // tele.enemyAttackValue と同値 (先読みでは次ターンの期待値)
            public float wAtk, wDef, wChg;     // 端子の重み (ペルソナ・ボス別プレイブック・予兆ターン込み)
            public float critMul;
            public int eDefNow, myPen;
            /// <summary>武器のスキル (本体の WeaponFamilyEffects の定数を読む写し) / 〈堅守の構え〉が今使える / 〈不滅の刃〉の連続数 / 今のシールド / 敵の最大 HP。</summary>
            public WeaponRungModel rung;
            public bool steadfastReady;
            public int undyingStreak, myShield, enemyMaxHpNow;
            /// <summary>武器の最終与ダメ +% の条件: 単体戦 (〈一対一〉) / 敵の残り HP が処刑の線以下 (〈処刑〉) / 〈鎧砕き〉で防御 0 にした後 / 装備ダイスの最大の目 (〈画竜点睛〉)。</summary>
            public bool solo, execBoost, shattered;
            public int dieMaxFace;
            public float diceAtkPct;           // 攻撃に挿した出目への上乗せ率 (ctx.attackDicePct・2026-11-06)
            public BossPlaybook play;
            public float[] atkGrowth;          // 防ぎ切りの規則: 撃破まで n ターンの敵の攻撃の伸び (null = 規則なし)
            public bool ledger;                // [V3 hpLedger] 効用を HP の帳簿で測る
            public float dFull;                // [V3 hpLedger] 先のターンに全部攻撃に挿した時の 1 ターンの与ダメの見込み
            public bool enemyDoubleOnBlockHeavy; // [2026-11-12] 敵〈多頭攻撃〉: 攻撃の個数 < ブロックの個数で挿したターン、 敵の攻撃が 2 回 (CombatManager.Turn.cs)
            public int enemyHitBonus;            // [2026-11-12] 敵の攻撃が通った時に足される量 (〈剛力〉〈闇技〉+2・〈研ぎ澄まし〉+ターン÷3・前のターンに積まれた〈反撃態勢〉)。 本体は敵の攻撃の計算の中 (PSM.ProcessDamage) で足す
            // ---- [#107 2026-11-12] 敵のパッシブ: 収支 (与えたダメージ − 受けたダメージ) で決まる効果と〈吸血〉 ----
            public bool enemyBalanceRead;        // 下のどれかを持つ (false なら効用は従来どおり)
            public int enemyChipOnPlus;          // 収支プラスのターンにその場で入る軽減無視 (〈尾撃〉1・〈重甲〉の反射 1)
            public int enemyChipOnMinus;         // 収支マイナスのターンにその場で入る軽減無視 (〈地獄の業火〉2)
            public bool shieldAbsorbsChip;       // 軽減無視をシールドが肩代わりする (CombatContext.ShieldAbsorbsUnmitigable)
            public int enemyLifesteal;           // 〈吸血〉の数 (1 つにつき、 通った主攻撃のダメージ ÷2 を回復)
            public float enemyHealCut;           // 治癒阻害 (ctx.enemyHealReductionPct)
            public int nextHitOnPlus;            // 収支プラスの次のターン、 敵の攻撃が通った時 +N (〈反撃態勢〉3)
            public float nextAtkOnPlus;          // 収支プラスの次のターン、 敵の攻撃 +N (〈暴走〉3) を次のターンのエスカレーションとプレイヤーの防御に通した被ダメの増分
            public int counterPendingNow;        // 前のターンに積まれ、 このターンの enemyHitBonus に入れた〈反撃態勢〉の分 (先読みの次のターンでは外す)
            public float mainCorr;             // [V3 mainCorr] 主攻撃の見積もりに掛ける実績の比 (0 = 掛けない)
            // ---- [V3 readOutgoingMult 2026-11-10] 与ダメの倍率を読む (本体の主攻撃の計算 CombatManager.Turn / Damage / PassiveSkillManager.ProcessDamage に合わせる) ----
            public bool readOut;               // 下の値で主攻撃を見積もる (false = 旧: 素の攻撃力 + 攻撃の出目 − 防御)
            public bool traceBoth;             // [計装] 旧と新の両方を出す (予測の照合用)
            public int atkPower, atkPenalty;   // 武器の素火力 / 麻痺毒の減算 (本体は atkBase 全体から引く)
            public int atkFlat;                // 攻撃+N: ctx.mutualAttackBonus (配線前に積まれた分) + 消費の攻撃 + 遺物の攻撃
            public int atkPerDie;              // 端子調律: 攻撃に挿したダイス 1 本あたり
            public float igCut;                // 〈不完全な起動〉の攻撃カット率
            public int dmgBonus;               // バフ damageBonus (ProcessDamage の 1.)
            public float critP, critMulEff;    // 会心率 (配線前に分かる分) / 実効の会心倍率 (敵の会心耐性込み)
            /// <summary>[2026-11-20] このターンの最初の 1 撃の会心率 (〈抜き打ちの作法〉・〈急所突きの針〉の確定。 無ければ critP と同じ)。</summary>
            public float critP1;
            /// <summary>[2026-11-21] 〈急所突きの針〉が仕込んである: 攻撃のある最初の 1 撃は必ず会心・敵の防御を無視 (本体 CombatManager.TakeTacticNeedle)。</summary>
            public bool needleFirst;
            /// <summary>[会心の軸 2026-11-21・CritCore] 配線の前に分かる合算の会心率 (100% 超えもそのまま) / 会心倍率 (敵の会心耐性の前) / 敵の会心耐性 /
            /// 〈寸鉄〉(超過の変換 2 倍) /〈白濁の義眼〉(確定会心 ×1.25) /〈銑鉄棍〉(会心しなかった撃に会心率 1% につき +1%) / 会心が出ない (封印) / 必ず会心 (forceCritical)。</summary>
            public float critAdd, critMulBase, critRed;
            public bool critDouble, ironClub, critOff, critOn;
            /// <summary>[会心の軸] 配線で決まる品: 〈鷹匠の革手袋〉〈狙い目の刻み〉/ 攻撃の端子 1 枚の撃の〈一の矢〉〈渾身の鉄兜〉〈必殺の照準器〉/ 最初の 1 撃が〈抜き打ちの作法〉で確定会心。</summary>
            public bool falconer, aim, firstArrow, pommel, sniper;
            /// <summary>[会心の軸 2026-11-23] 【集中】と【練気】: 今の練気 / 持っている集中の品 / その数 / 会心の品の数 / 〈抜き打ちの作法〉の集中の会心率の上乗せ /
            /// 集中会心の品 (〈抜き打ちの作法〉〈細目の覗き筒〉〈白濁の義眼〉) / 分岐 A の撃ごとの品 (〈見切りの目印〉〈脈打ちの測り針〉とその持ち越し・〈初太刀の黒鞘〉の次の撃・〈白刃の鏡砥〉) /
            /// 〈測量師の片眼鏡〉の会心率の乗算 / 〈死相の面頬〉を持っている。</summary>
            public int renki, focusCount, critRelics;
            public CombatSystem.CritCore.FocusItems focusItems;
            public float focusBonus, critRateMul;
            public bool quickDraw, narrow, nick, heaven, readMark, pulse, pulsePrimed, firstStrokePrimed, whiteBlade, mask;
            /// <summary>〈余韻の鈴〉: 前のターンの会心の条件は満たした (どの挿し方でも会心率が乗る・2026-11-22 final4)。</summary>
            public float bellRate;   // 〈余韻の鈴〉: 前のターンの会心の数 × RatePer (final6)
            /// <summary>〈加重の拳套〉: 2 撃目以降の撃の最終与ダメージ % (0 = 持っていない・本体 ctx.hitFinalDamagePct・answer15)。</summary>
            public int lateHitFinalPct;
            public float nonCritMul;           // 鈍器: 非会心だけ ×(1+x)
            public bool pinpointCut;           // 〈一点読みの片眼〉の代価: 非会心 × PinpointEye.NonCritKeepPct%
            public float outMul;               // 与ダメ倍率の池 (ctx.outgoing + 整備パネル + 戦意 + 剛胆 + 遺物 + 研磨剤 + 油・膏薬)
            public int critPen;                // 会心した攻撃だけに足す貫通
            public bool vitalIgnore;           // 短剣〈急所〉: 会心は防御を無視
            public int minDmg;                 // 勝利時の与ダメ最低保証
            public float berserkMul;           // 狂暴化 (敵の被ダメ倍率)
            public int finalPct;               // 最終与ダメージ倍率の上乗せ % (ctx.finalDamagePct・〈防人の軍標〉・2026-11-20)
            public bool guardEcho;             // 〈防人の軍標〉: このターンは全部の撃がもう 1 回ずつ発火する (2026-11-22・本体 CombatManager.Turn の撃の輪)
            /// <summary>[#105 山札の先読み] 攻撃の端子を挿せる手が 1 つも無い (手札に攻撃の端子が無い・条件に合わない) 次のターン。
            /// 「攻撃 0 本」の抑止 (−10000) は選べる時の手の形を決める装置なので、 選べないターンには掛けない
            /// (掛けると、 次のターンの手札の引きの差が −10000 ÷ 標本数の桁で先読みの値を支配する)。 既定 false = 今までどおり。</summary>
            public bool noAttackForced;
            // ---- ボスのダウン (HP の目盛り・2026-11-13・規則は CombatSystem.BossDown) ----
            /// <summary>このターン敵はダウン (攻撃しない・受けるダメージ ×BossDown.TakenMul)。 予告の enemyAttackValue は 0。</summary>
            public bool enemyDown;
            /// <summary>目盛り (null = ダウンしない敵) / 割った数 / 目盛りの HP を測る最大 HP / このターンの後に続くダウンの数。</summary>
            public float[] downNotches;
            public int downBroken, downMaxHp, downPendingNow;
            /// <summary>[2026-11-14] このターンのダウンは弱い (ひびで割れた・受けるダメージ ×1.5 なし) / 後に続くダウンの先頭が弱い。</summary>
            public bool enemyDownWeak, downPendingHeadWeak;
            /// <summary>[2026-11-14] 目盛りごとのひびの入ったターン (−1 = なし・null 可・書き換えない) / このターンの番号 /
            /// このターンに主攻撃の後で入る通常攻撃以外の削りの見込み (固定ダメの枠に溜まった分 (毒など) + 出血・ひびの見込みに使う・旧い判定では 0)。</summary>
            public int[] downCrackTurns;
            public int downTurn, downChip;
            /// <summary>次のターン (ダウンのターンなら、 ダウンが明けた後) の敵の攻撃の見込み (目盛りを割る価値・ダウン中の先の被ダメの見積もりに使う)。</summary>
            public int downNextAtk;
            /// <summary>[2026-11-14] 次のターンをダウンにした時に受けずに済む攻撃 (目盛りを割る価値の元)。 旧い規則 (口 downCancelRule=Always) は <see cref="downNextAtk"/> と同じ。
            /// 新しい規則: 次のターンの攻撃 + (次のターンがチャージで大技を取り消すなら、 抜ける大技 − 周期の平均 = DownKeepRules の式と同じ考え)。</summary>
            public int downEraseAtk;
            /// <summary>[2026-11-14] 次のターンがダウンになった時、 ダウンが明けたターン (2 ターン後) の敵の攻撃の見込み (本体と同じ取り消しの規則で周期を数える)。
            /// 旧い規則は <see cref="downNextAtk"/> と同じ (今までの見込みのまま)。</summary>
            public int downAfterAtk;
            /// <summary>[2026-11-26] 今割ると次のターンのダウンは溜めで無効 (目盛りが消えるだけ)。 このとき <see cref="downEraseAtk"/> は負 = 大技を止める機会を捨てる損。</summary>
            public bool downNextResisted;
            /// <summary>[2026-11-26・Instant] 割った瞬間に効く規則で見積もる / このターンに割ったら何が起きるか (DownKind の値) /
            /// チャージのターンに割ると捨てる、 次の大技の見込み / 小技のターンに割ると捨てる、 近い大技を止める得 (大技 − 今の小技)。</summary>
            public bool downInstant;
            public int downBreakKindNow, downHeavyNextAtk, downHoldCost;
            /// <summary>[2026-11-26] 混乱ゲージ (目盛りの代わり): 今の値 / 満タン (0 = ゲージなし) / 満ちた後の増え方 / 大技までのターン (このターン・−1 = 大技なし) /
            /// 大技のターンに満たして止めた時の大技の見込み (支度の価値の元) / 1 ターンに攻撃へ挿せる止まった面の和の見込み (全部攻撃)。</summary>
            public int downGauge, downGaugeMax, downToHeavy, downHeavyAtk;
            public float downGaugeGrowth, downGaugePerTurn;
            /// <summary>[2026-11-26] 今割ると次のターンは小技 (よろめき) だが、 1〜2 ターン後にチャージ→大技が控えている時の大技攻撃力の見込み (温存罰の元)。</summary>
            public int downStaggerOppAtk;
            // ---- 大技のチャージ (2026-11-14・敵は攻撃しない) ----
            /// <summary>このターンは大技のチャージ (予告の攻撃は 0)。 口 heavyChargeNoAttack=False (旧い作り) では立たない。</summary>
            public bool charging;
            /// <summary>チャージのターンの、 次のターン (= 大技) の敵の攻撃の見込み。 帳簿の「撃破までの被ダメの見込み」が、 予告 0 の代わりに読む
            /// (ダウンのターンが明けた後の見込みを読むのと同じ扱い。 先のターンを「見えている最大の攻撃」で伸ばす今の帳簿の流儀に合わせて、 大技の値を使う)。</summary>
            public int chargeNextAtk;
            // ---- 灰燼の王の大技〈業火〉の烈炎 (2026-11-14) ----
            /// <summary>烈炎は〈業火〉でまとめて返る (王が大技を持つ・烈炎を持つ)。 旧い形 (ashKingHeavy=False) では false。</summary>
            public bool blazeBurst;
            /// <summary>烈炎 1 つあたりの業火の軽減無視ダメージ (BossParam.BlazeBurstPerStack・本体と同じ値) / いまの業火の周期 (Escalation.HeavyPeriodNow)。</summary>
            public int blazeBurstPer, heavyPeriodNow;
            /// <summary>[2026-11-14] 〈烈炎〉が積もる量: 主攻撃をシールドで肩代わりした量 この値 ごとに +1 (BossParam.BlazePerShield・本体と同じ値)。
            /// 積もり方の規則は本体と同じ口 GameHooks.BlazeStack (旧い条件 Guard2 では読まない)。</summary>
            public int blazePerShield;
        }

        /// <summary>先のターンの被ダメの見積もりに使う敵の攻撃: 普段はこのターンの予告値。 ダウンのターンは予告が 0 なので、 ダウンが明けた後の見込み。</summary>
        private static int FutureEnemyAtk(in OptimalTurnState s) => s.enemyDown ? s.downNextAtk : s.charging ? s.chargeNextAtk : s.enemyAtk;

        /// <summary>[ボスのダウン] 目盛りを割って次のターンをダウンにする価値の割合 (次のターンの敵の攻撃 × これ を今の HP で見る・仮・未測定)。
        /// 〈石化の瞳〉(DeckStunShare) と同じ形: 半分にするのは、 割らなくてもブロックで一部は受けられるから。</summary>
        private const float DownNextTurnShare = 0.5f;
        /// <summary>[2026-11-26] 混乱ゲージの支度 (大技のターンに満たせる位置に残す) の得の割合 (大ダウンの得の見込みに掛ける・確実ではないので半分)。 測定の口 downGaugeReadyShare。</summary>
        [UnityEngine.Tooltip("混乱ゲージの支度の得の割合 (2026-11-26)")] public float downGaugeReadyShare = 0.5f;
        private float DownGaugeReadyShare => downGaugeReadyShare;

        /// <summary>[測定 2026-11-26] BOT が「割る時でダウンの種類が決まる」規則 (<see cref="CombatSystem.DownCancelRule.Timed"/>) を知っているか。 既定 true。
        /// false = 知らない (旧い規則 ChargeOrHeavy のつもりで目盛りの価値を見積もる・どのダウンも大ダウンだと思う)。 本体の規則は変えない。
        /// 知る腕と知らない腕の差 = 割る時を読む価値 (戦闘の読みの深さ)。</summary>
        public bool downTimingAware = true;

        /// <summary>[2026-11-26] 割った瞬間に効く規則 (Instant) で、 振り直しを大事なターンに集める (既定 true・downTimingAware が false なら効かない)。
        /// 大ダウンのターンと「今割れば大技が止まる」大技のターンは、 振り直した後の最良の手の期待値で振るかを決め (充電の予備も使う・そのターン 2 回まで)、
        /// その 1〜2 ターン前は振り直し 1 回分の充電を残す。 false = 今までの振り直し (平均を下回る手を 1 回)。</summary>
        public bool downRerollPlan = true;

        /// <summary>[測定 2026-11-26] Timed で次がチャージ (溜めで無効) の時の罰の係数 (-1 = 既定 DownNextTurnShare=0.5)。</summary>
        public float downResistShare = -1f;

        /// <summary>[測定 2026-11-26] Timed で次が小技 (よろめき) かつ 1〜2 ターン後にチャージ→大技が控える時の温存罰の係数 (0 = 既定・罰なし)。</summary>
        public float downStaggerPenaltyShare = 0f;

        /// <summary>[測定 2026-11-26] 目盛り温存対象ターンに限り、 攻撃 0 本 (aCount==0) の -10000 禁止を解除して待機を選べるようにするか (既定 false)。</summary>
        public bool downAllowNoAttackHold = false;
        /// <summary>[測定 2026-11-26] 混乱ゲージを大技のターンまで温存するためなら、 攻撃 0 本で待つ手を許す (大技の 1〜2 ターン前・満タンまで 1 ターンに入れられる量以内の時だけ)。
        /// 既定 true (2026-11-26・作者の決定・⑯ で 3 層 +1.54 pp)。 false = 今まで (攻撃 0 本は死ぬ時だけ)。 規則を知る腕 (downTimingAware) でだけ効く。</summary>
        [UnityEngine.Tooltip("混乱ゲージの温存のための攻撃 0 本の待ちを許す (2026-11-26)")] public bool downGaugeNoAttackHold = true;

        /// <summary>BOT が見積もりに使うダウンの規則 (<see cref="downTimingAware"/> が false なら、 本体が Timed でも ChargeOrHeavy のつもりで読む)。</summary>
        private CombatSystem.DownCancelRule BotDownRule
            => !downTimingAware && CombatSystem.BossDown.IsTimedFamily(GameLoop.GameHooks.DownCancel) ? CombatSystem.DownCancelRule.ChargeOrHeavy : GameLoop.GameHooks.DownCancel;

        /// <summary>[V3 readOutgoingMult] 配線時点で分かる範囲で、 本体と同じ順に主攻撃 (ヴェスカの防御の前) の期待値を出す。
        /// 会心は確率で 2 通りを混ぜる。 配線の後で積まれる分 (OnWired・OnPreDealDamage・OnCriticalCheck・追撃・役の追撃・敵の軽減パッシブ・脆弱) は入らない。</summary>
        /// <summary>最終与ダメージ倍率 (本体の Kw.ApplyFinalDamage と同じ式・切り捨て)。</summary>
        private static int ApplyFinalPct(int d, int pct) => d > 0 && pct != 0 ? (int)((long)d * (100 + pct) / 100) : d;

        private static int ReadOutMainDamage(in OptimalTurnState s, int aCount, int bCount, int aSum, in WiredSums ws)
        {
            if (aCount <= 0) return 0;   // 攻撃端子 0 本 = 攻撃しない (本体の skipAttack)
            // ADR-0016: 攻撃の端子 1 枚につき 1 撃 (本体 CombatManager.Turn の hitBases)。 端子の外の攻撃 (武器・攻撃 +N・消耗品・遺物)・
            //   麻痺毒の減算・〈不完全な起動〉・バフ・会心の期待値は 1 撃ごと。
            //   shortcut: 端子ごとの出力は持たないので攻撃の合計を撃の数で等分する (端数は 1 撃目)。 端子ごとの差で丸めが変わる時は TerminalOutcome.attackHits を運ぶ。
            int hits = System.Math.Max(1, ws.aHits);
            if (s.rung.muga && hits == 1) aSum += CombatSystem.CombatRules.MugaBonus(ws.otherDice);   // 竜閃〈無我無心〉: 攻撃の端子 1 枚なら他の端子の目 × 1.5
            float total = 0f;
            for (int h = 0; h < hits; h++)
                total += ReadOutOneHit(in s, bCount, h == 0 ? aSum - (hits - 1) * (aSum / hits) : aSum / hits, in ws, h);
            //   短剣〈連刃〉: 攻撃の端子 2 枚以上なら最後に追撃 (武器の攻撃力 × 率・防御を無視)
            if (s.rung.chainRatePct > 0 && ws.aHits >= ItemSystem.PassiveSkills.Effects.ChainBlade.NeedTerminals)
                total += ReadOutOneHit(in s, bCount, 0, in ws, hits, CombatSystem.CombatRules.ChainHitBase(s.atkPower, s.rung.chainRatePct));
            if (s.guardEcho) total *= 2f;      // shortcut: もう 1 回の撃は同じ見込み (撃ごとの積み上げ・敵の防御の変化は見ない)
            return Mathf.RoundToInt(total);
        }

        /// <summary>主攻撃 1 撃の与ダメの期待値 (<paramref name="termAtk"/> = その撃の端子の出力)。 <see cref="ReadOutMainDamage"/> が撃の数だけ呼ぶ。</summary>
        /// <summary>[2026-11-23 answer9] 【練気】1 つ (会心 1 回) の将来の価値 (与ダメ相当・会心の秘宝を持つ時だけ)。</summary>
        private const float RenkiFutureDmg = 1.5f;

        private static float ReadOutOneHit(in OptimalTurnState s, int bCount, int termAtk, in WiredSums ws, int hitNo = 0, int rawOverride = -1)
        {
            //   竜閃〈無我無心〉: 攻撃の端子 2 枚以上のターンは武器の攻撃力 0 / 〈連刃〉の追撃は武器の攻撃力 × 率だけ (rawOverride)
            int raw = rawOverride >= 0 ? rawOverride : (s.rung.muga && ws.aHits >= 2 ? 0 : s.atkPower) + s.atkFlat + termAtk;
            raw = System.Math.Max(0, raw - s.atkPenalty);
            if (s.igCut > 0f && raw > 0) raw -= Mathf.FloorToInt(raw * s.igCut);
            raw += s.dmgBonus;
            float outMul = s.outMul;
            // 端子の山札 (旧い配線では全部「なし」): 与ダメ% は池へ足す (ctx.AddOutgoingPct)・会心倍率 +N・必ず会心・別枠の掛け算・防御の無視
            outMul += ws.outPctAdd;
            float termMul = ws.damageMul;
            //   武器の最終与ダメ +% (一対一・捨て身・処刑・鎧砕き・不滅の刃) / 竜閃〈画竜点睛〉(攻撃の端子 1 枚・最大の目 → 確定会心・+100% / +200%)
            int fpct = s.finalPct + s.rung.FinalPct(s.solo, 1, bCount, s.execBoost, s.shattered, s.undyingStreak + 1);   // ローカル関数は in の引数を捕まえられないので写す
            int garyoPct = s.rung.garyo && ws.aHits == 1 && hitNo == 0 && rawOverride < 0 ? CombatSystem.CombatRules.GaryoPct(ws.atkFaceMax, ws.rolledMax, s.dieMaxFace) : 0;
            bool garyo = garyoPct > 0;
            fpct += garyoPct;
            if (hitNo >= 1) fpct += s.lateHitFinalPct;   // 〈加重の拳套〉(answer15: 別枠の最終与ダメ)
            bool noDef = ws.ignoreDefense || (hitNo == 0 && s.needleFirst) || rawOverride >= 0 || (hitNo >= 1 && s.rung.vital2nd);   // 〈急所〉: 2 撃目以降は防御無視
            int minDmg = s.minDmg, eDef = noDef ? 0 : s.eDefNow, pen = s.myPen + ws.armorPen, critPen = s.critPen;
            float nonCritMul = s.nonCritMul, berserk = s.berserkMul;
            // 会心 (CritCore・本体の ProcessDamage と同じ式): 撃ごとの会心率と会心倍率。 配線で決まる品はこの撃の目・撃の数で足す
            float add = s.critAdd, mulAdd = ws.critMultAdd;
            bool force = ws.forceCrit || (hitNo == 0 && s.critP1 >= 1f);   // 〈画竜点睛〉の確定会心は 2026-11-23 に撤去
            if (s.falconer && ws.atkFaceMax >= ItemSystem.PassiveSkills.Effects.FalconerGlove.MinFace) { add += ItemSystem.PassiveSkills.Effects.FalconerGlove.Rate; mulAdd += ItemSystem.PassiveSkills.Effects.FalconerGlove.Mul; }
            if (s.aim && ws.hitFace != null && hitNo < ws.hitFace.Length) add += CombatSystem.CritCore.AimRate(ws.hitFace[hitNo]);
            add += s.bellRate;
            bool solo = ws.aHits == 1;
            //   分岐 A の撃ごとの品 (2026-11-23): 〈見切りの目印〉2 撃目以降 / 〈脈打ちの測り針〉前の撃が会心 (2 撃目以降は前の撃の会心率で重み付け) / 〈初太刀の黒鞘〉の次のターンの最初の撃
            if (s.readMark && ws.aHits >= 2 && hitNo >= 1) add += ItemSystem.PassiveSkills.Effects.ReadMark.RateFor(s.renki);
            if (s.pulse) { float w = hitNo == 0 ? (s.pulsePrimed ? 1f : 0f) : s.critP; add += w * ItemSystem.PassiveSkills.Effects.PulseNeedle.Rate; mulAdd += w * ItemSystem.PassiveSkills.Effects.PulseNeedle.Mul; }
            if (s.firstStrokePrimed && hitNo == 0) add += ItemSystem.PassiveSkills.Effects.FirstStrokePin.NextRate;
            add *= 1f + s.critRateMul;   // 〈測量師の片眼鏡〉: 会心率の乗算 (本体 ctx.hitCritRateMulAdd)
            float addPlain = add, mulPlain = mulAdd;   // 集中にならない撃 (〈必殺の照準器〉のもう 1 回) の会心率・倍率
            //   【集中】(本体 PassiveSkillManager.ProcessDamage と同じ CritCore.FocusedRate): 攻撃の端子 1 枚の撃で集中の品 3 つ以上・集中の会心率 1% 以上
            bool focus = false; int spent = 0, focusPen = 0, critFpct = 0;
            if (solo && hitNo == 0 && rawOverride < 0 && s.focusCount >= CombatSystem.CritCore.FocusNeedCount)
            {
                float fr = CombatSystem.CritCore.FocusedRate(add, true, false, s.focusItems, s.critRelics, s.focusBonus);
                if (fr > 0f)
                {
                    focus = true; add = fr; spent = CombatSystem.CritCore.RenkiSpend(s.renki + 1);
                    if (s.firstArrow) mulAdd += ItemSystem.PassiveSkills.Effects.FirstArrow.MulFor(spent, ws.atkFaceMax);
                    if (s.quickDraw) mulAdd += ItemSystem.PassiveSkills.Effects.QuickDrawArt.Mul;
                    if (s.pommel) critFpct += ItemSystem.PassiveSkills.Effects.HeftyPommel.FinalPctFor(spent);
                    if (s.narrow) { focusPen += ItemSystem.PassiveSkills.Effects.NarrowSpyglass.Pen + ItemSystem.PassiveSkills.Effects.NarrowSpyglass.PenPerRenki * spent; mulAdd += ItemSystem.PassiveSkills.Effects.NarrowSpyglass.Mul; }
                    if (s.nick) focusPen += ItemSystem.PassiveSkills.Effects.NickAppraisal.Pen;   // answer9: 集中会心の撃は防御 12 無視
                }
            }
            //   【練気】の基礎の効果 (全ビルド共通 +0.06 × 練気)
            int capR = CombatSystem.CritCore.RenkiCap + (s.critDouble ? ItemSystem.PassiveSkills.Effects.Suntetsu.CapAdd : 0);
            int nextR = System.Math.Min(capR, s.renki + 1);
            if (s.critRelics > 0) { mulAdd += CombatSystem.CritCore.RenkiMulPer * nextR; mulPlain += CombatSystem.CritCore.RenkiMulPer * nextR; }
            //   〈白刃の鏡砥〉: 会心した撃は今の【練気】(この会心の +1・集中なら使った後) × 0.04
            if (s.whiteBlade) { mulAdd += ItemSystem.PassiveSkills.Effects.WhiteBladeMirror.MulPerRenki * (nextR - spent); mulPlain += ItemSystem.PassiveSkills.Effects.WhiteBladeMirror.MulPerRenki * nextR; }
            bool focusNoDef = focus && s.heaven;                                                                   // 〈白濁の義眼〉: 集中会心は防御を全部無視
            bool suntetsuNoDef = s.critDouble && nextR >= ItemSystem.PassiveSkills.Effects.Suntetsu.IgnoreRenki;   // 〈寸鉄〉: 練気 4 以上の会心は防御を全部無視
            float critMulEff = CombatSystem.CritCore.EffectiveMul(s.critMulBase + mulAdd, s.critRed, add, s.critDouble, focus);
            float sniperMul = CombatSystem.CritCore.EffectiveMul(s.critMulBase + mulPlain + CombatSystem.CritCore.SniperMulAdd(spent), s.critRed, addPlain, s.critDouble);
            float ironOut = s.ironClub ? System.Math.Min(1f, add) : 0f;
            bool pinpoint = s.pinpointCut, vital = s.vitalIgnore, down = DownTakesMul(s.enemyDown, s.enemyDownWeak);   // よろめき (2026-11-14) は ×1.5 なし (〈骨溶かしの黒油〉は有り)
            float D(bool crit, float cmul, bool fz = true)   // fz = 集中会心の上乗せを入れる (照準器のもう 1 回は false)
            {
                int d = raw;
                if (d <= 0) return System.Math.Max(0, minDmg);
                if (crit) d = (int)(d * cmul);
                else
                {
                    if (nonCritMul > 0f) d = (int)(d * (1f + nonCritMul));
                    if (pinpoint) d = d * ItemSystem.PassiveSkills.Effects.PinpointEye.NonCritKeepPct / 100;
                    if (focus && fz) d = d * CombatSystem.CritCore.FocusMissKeepPct / 100;
                }
                float om = crit ? outMul : outMul + ironOut;   // 〈銑鉄棍〉: 会心しなかった撃だけ池へ足す
                if (om > 0f && System.Math.Abs(om - 1f) > 0.001f) d = Mathf.CeilToInt(d * om);
                if (termMul != 1f && d > 0) d = System.Math.Max(1, Mathf.RoundToInt(d * termMul));   // 〈激昂〉〈共鳴〉: 本体と同じ位置 (与ダメ倍率の池の後・物差しの前)
                d = CombatSystem.EnemyHpLedger.ProbeMain(d);   // [測定用の物差し 2026-11-10] 本体と同じ位置 (与ダメ倍率の池の後・防御の前)
                d = ApplyFinalPct(d, fpct + (crit && fz ? critFpct : 0));     // 最終与ダメージ倍率: 本体と同じ位置 (物差しの直後・徒労の前・〈渾身の鉄兜〉は集中会心の撃だけ)
                d = MetaProgression.MetaDebuffApplicator.ApplyOutgoingCut(d);   // 〈徒労〉: 本体と同じ位置 (物差しの直後・防御の前・CombatManager.Damage.cs)
                d = CombatSystem.BossDown.ApplyTaken(d, down);               // ボスのダウン ×1.5: 本体と同じ位置 (徒労の直後)
                if (eDef > 0 && d > 0 && !(crit && (vital || suntetsuNoDef || (fz && focusNoDef))))
                {
                    int effDef = CombatSystem.DefenseFormula.Effective(eDef, pen + (crit ? critPen + (fz ? focusPen : 0) : 0));
                    if (effDef > 0) d = CombatSystem.DefenseFormula.Apply(d, effDef);
                }
                if (d < minDmg) d = minDmg;
                if (berserk > 1f && d > 0) d = Mathf.CeilToInt(d * berserk);
                return d;
            }
            float p = s.critOff ? 0f : force || s.critOn ? 1f : ItemSystem.PassiveSkills.CombatContext.ResolveCritRate(add);
            if (focus && !force && !s.critOn && p > CombatSystem.CritCore.FocusMaxRate) p = CombatSystem.CritCore.FocusMaxRate;
            float v = p >= 1f ? D(true, critMulEff) : p <= 0f ? D(false, critMulEff) : p * D(true, critMulEff) + (1f - p) * D(false, critMulEff);
            //   【練気】の将来の価値 (answer9): 会心の秘宝を持つ時、 会心 1 回 (= 練気 +1) を与ダメ RenkiFutureDmg 相当に数える
            if (s.critRelics > 0) v += p * RenkiFutureDmg;
            //   〈必殺の照準器〉(2026-11-23): 集中会心の撃をもう 1 回 (集中にならない・実際の会心率で振る)
            if (focus && s.sniper && p > 0f)
            {
                float pr = s.critOff ? 0f : s.critOn ? 1f : ItemSystem.PassiveSkills.CombatContext.ResolveCritRate(addPlain);
                v += p * (pr * D(true, sniperMul, false) + (1f - pr) * D(false, sniperMul, false));
            }
            return v;
        }

        /// <summary>与ダメ倍率の池: ApplyWinDamageModifiers と同じ足し方 (加算の池)。 読むだけ。
        /// <see cref="FillReadOut"/> と、 旧の式の〈不滅の刃〉(#122) が使う。</summary>
        private static float OutgoingPoolNow(ItemSystem.PassiveSkills.CombatContext ctx, GameLoop.RunState run)
        {
            float outMul = ctx.outgoingDamageMultiplier <= 0f ? 1f : ctx.outgoingDamageMultiplier;
            int metaPct = MetaProgression.MetaBuffApplicator.GetOutgoingDamagePct()
                        + MetaProgression.MetaBuffApplicator.GetBattleSpiritPct(run);
            {
                int slayer = MetaProgression.MetaBuffApplicator.GetEliteSlayerPct();
                var node = MapSystem.MapManager.Instance?.CurrentNode;
                if (slayer > 0 && node != null && node.EffectiveType.ToEnemyKind() == EnemyKind.Elite) metaPct += slayer;
            }
            outMul += metaPct * 0.01f;
            if (ctx.polishArmed) outMul += 1.5f;
            if (ctx.consDmgMultPct > 0) outMul *= 1f + ctx.consDmgMultPct / 100f;   // 〈油・膏薬〉は最終与ダメージ (2026-11-25・池の外で掛ける)
            return outMul;
        }

        /// <summary>[V3 readOutgoingMult] 配線時点の盤面から、 主攻撃の倍率の入力を読む (読むだけ・書き換えない)。</summary>
        private void FillReadOut(ref OptimalTurnState st, CombatManager cm, ItemSystem.PassiveSkills.CombatContext ctx,
                                 GameLoop.RunState run, int atkPenalty)
        {
            st.atkPower = System.Math.Max(0, cm?.PlayerAttackPower ?? 2);
            st.atkPenalty = System.Math.Max(0, atkPenalty);
            int consAtk = ctx.consDiceRoll;
            st.atkFlat = ctx.mutualAttackBonus + consAtk;
            st.atkPerDie = MetaProgression.MetaBuffApplicator.GetAttackTerminalPerDice();
            st.igCut = (run != null && run.HasFlaw(GameLoop.GateFlaw.IncompleteIgnition)) ? System.Math.Max(0f, GameLoop.GateFlaws.IgnitionAttackCutPct) : 0f;
            st.dmgBonus = (int)ctx.GetBuff("damageBonus");
            // 会心率: 本体の CritRateOf(pendingBuff: true) と同じ (武器 + このターンの加算 + 整備パネル + 戦闘中バフ)。 OnCriticalCheck の条件付きは入らない。
            float add = (cm?.PlayerCritBaseRate ?? 0f) + ctx.critRatePctAdd + MetaProgression.MetaBuffApplicator.GetCritRatePctBonus()
                      + ctx.GetBuff(ItemSystem.PassiveSkills.CombatContext.CritRateBuffKey);
            //   配線の前に分かる会心の品 (〈瑠璃砥石〉〈一点読みの片眼〉ほか・Crit.ForecastTurn) を足す
            ItemSystem.PassiveSkills.Effects.Crit.ForecastTurn(ctx, run, out float itemRate, out float itemMul, out float itemRateMul);
            st.critRateMul = itemRateMul;
            add = System.Math.Max(0f, add + itemRate);
            st.critAdd = add;
            st.critMulBase = ctx.criticalMultiplier + itemMul;
            st.critRed = ctx.EffectiveEnemyCritMultReduction;
            st.critDouble = ctx.critExcessDouble || (run != null && run.OwnsPassive(ItemIds.CritSuntetsu));
            st.ironClub = ctx.critToDamage || (run != null && run.OwnsPassive(ItemIds.CritIronClub));
            bool bladeDance = ItemSystem.PassiveSkills.Effects.BladeDance.Held(run);   // 〈無銘の剣舞譜〉: 全部の撃が会心確定・防御無視・もう 1 回 (本体 CombatManager.Turn の撃の輪)
            st.critOff = ctx.critSuppressed && !bladeDance;
            st.critOn = bladeDance || (!st.critOff && ctx.forceCritical);
            st.falconer = run != null && run.OwnsPassive(ItemSystem.PassiveSkills.Effects.FalconerGlove.Id);
            // 【練気】・【集中】(2026-11-23・本体と同じ CritCore の関数)
            st.renki = ItemSystem.PassiveSkills.Effects.Crit.Renki(ctx);
            st.focusItems = CombatSystem.CritCore.FocusOf(run);
            st.focusCount = CombatSystem.CritCore.FocusCount(st.focusItems);
            st.critRelics = CombatSystem.CritCore.CritRelicCount(run);
            st.focusBonus = ItemSystem.PassiveSkills.Effects.QuickDrawArt.FocusRateBonus(ctx, run, cm != null && cm.EnemyDownNow);
            st.quickDraw = run != null && run.OwnsPassive(ItemIds.CritQuickDraw);
            st.narrow = run != null && run.OwnsPassive(ItemIds.CritNarrow);
            st.nick = run != null && run.OwnsPassive(ItemIds.CritNick);
            st.heaven = run != null && run.OwnsPassive(ItemIds.CritHeaven);
            st.readMark = run != null && run.OwnsPassive(ItemIds.CritReadMark);
            st.pulse = run != null && run.OwnsPassive(ItemIds.CritPulse);
            st.pulsePrimed = ItemSystem.PassiveSkills.Effects.PulseNeedle.Primed(ctx);
            st.firstStrokePrimed = run != null && run.OwnsPassive(ItemIds.CritFirstStroke) && ItemSystem.PassiveSkills.Effects.FirstStrokePin.Primed(ctx);
            st.whiteBlade = run != null && run.OwnsPassive(ItemIds.CritWhiteBlade);
            st.mask = run != null && run.OwnsPassive(ItemIds.CritMask);
            st.bellRate = run != null && run.OwnsPassive(ItemSystem.PassiveSkills.Effects.LingeringBell.Id) ? ItemSystem.PassiveSkills.Effects.LingeringBell.RateNow(ctx) : 0f;
            st.aim = run != null && run.OwnsPassive(ItemIds.CritAim);
            st.firstArrow = run != null && run.OwnsPassive(ItemIds.CritFirstArrow);
            st.pommel = run != null && run.OwnsPassive(ItemIds.CritPommel);
            st.sniper = run != null && run.OwnsPassive(ItemIds.CritSniper);
            float p = st.critOff ? 0f : st.critOn ? 1f : ItemSystem.PassiveSkills.CombatContext.ResolveCritRate(add);
            st.critP = p;
            // 最初の 1 撃: 〈急所突きの針〉の確定 (配線の時点では ctx.hitIndex = 0)
            {
                float p1 = p;
                st.needleFirst = ctx.GetAccumulated(CombatSystem.CombatManager.TacticNeedleKey) > 0f;
                if (st.needleFirst) p1 = 1f;      // 〈急所突きの針〉: 本体は forceCritThisHit (撃の会心の判定を確定にする)
                st.critP1 = p1;
            }
            st.critMulEff = CombatSystem.CritCore.EffectiveMul(st.critMulBase, st.critRed, add, st.critDouble);
            st.lateHitFinalPct = run != null && run.OwnsPassive(ItemSystem.PassiveSkills.Effects.WeightingGauntlet.Id) ? ItemSystem.PassiveSkills.Effects.WeightingGauntlet.Pct : 0;
            st.nonCritMul = ctx.nonCritOutgoingMultiplier;
            st.pinpointCut = ctx.GetAccumulated(ItemSystem.PassiveSkills.Effects.PinpointEye.NonCritCutKey) > 0f;
            st.outMul = OutgoingPoolNow(ctx, run);
            st.finalPct = ctx.finalDamagePct + (ctx.painkillerProcced ? GameLoop.Consumables.PainkillerFinalPct : 0)
                        + ItemSystem.PassiveSkills.Effects.BloodEdict.BurstForecast(ctx);   // 〈痛覚遮断剤〉・〈血令〉の発動ターン
            st.guardEcho = bladeDance;   // 〈防人の軍標〉の「もう 1 回」は 2026-11-25 に撤去
            st.critPen = ctx.critArmorPen;
            st.vitalIgnore = bladeDance || ItemSystem.PassiveSkills.Effects.VitalStrike.IgnoresDefense(ctx);   // 剣舞譜は会心確定なので「会心は防御を無視」と同じ見込み
            st.minDmg = ctx.winMinDamage;
            st.berserkMul = ctx.enemyDamageTakenMultiplier;
        }

        /// <summary>[V3 hpLedger] この戦闘で失う HP の見込み = 今ターンの被ダメ + 撃破までの残りターンで受ける被ダメ (全部攻撃する前提)。</summary>
        /// <param name="enemyHeal">[#107] 敵〈吸血〉で今ターンの終わりに戻る敵 HP (撃破しないときだけ・<see cref="OptimalTurnOutcome.enemyHeal"/>)。</param>
        /// <param name="nextTurnExtra">[#107] 今ターンの挿し方で次のターンの被ダメに乗る分 (収支プラス → 〈反撃態勢〉〈暴走〉)。 次のターンの 1 回だけ。</param>
        private float LedgerLoss(in OptimalTurnState s, int effDmg, int taken, int enemyHeal = 0, float nextTurnExtra = 0f)
        {
            int rem = s.enemyHp - System.Math.Min(effDmg, s.enemyHp);
            if (rem <= 0) return taken;
            rem = System.Math.Min(System.Math.Max(rem, s.enemyMaxHpNow), rem + System.Math.Max(0, enemyHeal));
            float dF = System.Math.Max(1f, s.dFull);
            if (v3.ledgerContinuous && s.enemyLifesteal <= 0)
            {
                float remTurns = rem / dF;
                int full = (int)remTurns;
                float frac = remTurns - full;
                float baseAtk = FutureEnemyAtk(in s);
                float futureC = 0f;
                int capFull = System.Math.Min(full, HorizonCapTurns);
                for (int j = 1; j <= capFull; j++)
                {
                    float g = s.atkGrowth != null ? s.atkGrowth[System.Math.Min(j + 1, HorizonCapTurns)] : 1f;
                    futureC += baseAtk * g;
                }
                if (full >= HorizonCapTurns)
                {
                    float gCap = s.atkGrowth != null ? s.atkGrowth[HorizonCapTurns] : 1f;
                    futureC += ((full - HorizonCapTurns) + frac) * baseAtk * gCap;
                }
                else if (frac > 0f)
                {
                    float gNext = s.atkGrowth != null ? s.atkGrowth[System.Math.Min(full + 2, HorizonCapTurns)] : 1f;
                    futureC += frac * baseAtk * gNext;
                }
                futureC += nextTurnExtra;
                return taken + v3.ledgerFutureShare * futureC;
            }
            int n;
            if (s.enemyLifesteal <= 0)
                n = System.Math.Min(HorizonCapTurns, UnityEngine.Mathf.CeilToInt(rem / dF));
            else
            {
                // 〈吸血〉(#107): 先のターンも、 敵の攻撃が通るたび (全部攻撃 = ブロック 0 の前提で敵の攻撃そのまま) その ÷2 だけ戻る。
                //   撃破するターンは敵が攻撃しない (CombatManager.Turn.cs の `else if (enemyHP > 0)`) ので戻らない。
                float r = rem; n = HorizonCapTurns;
                for (int j = 1; j <= HorizonCapTurns; j++)
                {
                    r -= dF;
                    if (r <= 0f) { n = j; break; }
                    float g = s.atkGrowth != null ? s.atkGrowth[System.Math.Min(j + 1, HorizonCapTurns)] : 1f;
                    int hit = Mathf.RoundToInt(FutureEnemyAtk(in s) * g);
                    r = System.Math.Min(System.Math.Max(r, s.enemyMaxHpNow), r + LifestealHeal(in s, hit));
                }
            }
            float future = 0f;
            for (int j = 1; j <= n; j++)
            {
                float g = s.atkGrowth != null ? s.atkGrowth[System.Math.Min(j + 1, HorizonCapTurns)] : 1f;
                future += FutureEnemyAtk(in s) * g;
            }
            if (n >= 1) future += nextTurnExtra;   // 〈反撃態勢〉〈暴走〉: 次のターンも敵が生きていれば乗る
            return taken + v3.ledgerFutureShare * future;
        }

        /// <summary>[#107] 敵〈吸血〉の回復量 (本体: EnemyPassiveSkillEffects.Lifesteal = ReduceEnemyHeal(通った主攻撃 ÷2)・1 つずつ)。</summary>
        private static int LifestealHeal(in OptimalTurnState s, int hpDamage)
        {
            if (s.enemyLifesteal <= 0 || hpDamage <= 0) return 0;
            int heal = hpDamage / 2;
            if (s.enemyHealCut > 0f && heal > 0) heal = (int)(heal * (1f - s.enemyHealCut));
            return heal * s.enemyLifesteal;
        }

        /// <summary>[#107] 1 ターンに <paramref name="dmg"/> 削り、 撃破しなかったターンの終わりに <paramref name="heal"/> 戻る (最大 HP まで) 時の、 撃破までのターン数 (上限 <see cref="HorizonCapTurns"/>)。</summary>
        private static int TurnsToKillWithHeal(int hp, int dmg, int heal, int maxHp)
        {
            if (dmg <= 0) return HorizonCapTurns;
            int r = hp;
            for (int t = 1; t <= HorizonCapTurns; t++)
            {
                r -= dmg;
                if (r <= 0) return t;
                r = System.Math.Min(System.Math.Max(r, maxHp), r + heal);
            }
            return HorizonCapTurns;
        }

        /// <summary>配線 <paramref name="w"/> の効用 (state + 出目 + 配線 → 効用)。 盤面を読まない純粋な関数 (v3 の係数だけ読む)。</summary>
        private float OptimalUtility(in OptimalTurnState s, int[] rolls, CombatSystem.DiceTerminal[] w)
            => OptimalUtility(in s, rolls, w, out _);

        /// <summary>このターンを配線 w で解いた結果 (先読みが次ターンの盤面を作るのに使う)。 効用の計算で出た値をそのまま写す。</summary>
        private struct OptimalTurnOutcome
        {
            public int effDmg;      // 敵 HP を減らす量の見積もり (撃破判定に使う値と同じ)
            public int mainDmg;     // [2026-11-14] そのうち主攻撃 (目盛りを割れる分・黎明の削りを除く)
            public int gaugePips;   // [2026-11-26] 混乱ゲージに入る量
            public int taken;       // 自 HP の減少の見積もり
            public int block;
            public int aCount, bCount;
            public int bashUsed;    // 盾〈シールドバッシュ〉で使ったシールド
            public bool justGuard;  // 【ジャスト防御】の見込み (〈城壁〉の次のシールド)
            public bool freeBlock;  // [計装 2026-11-10] 防ぎ切りの規則の対象 (撃破まで HP を失わない) だった
            public bool killsNow;   // [計装] 今ターン撃破の見込み
            public int rawNew;           // [計装] 倍率の前の攻撃 (素火力 + 攻撃+N + 出目)
            public int dmgOld, dmgNew;   // [計装 readOutgoingMult] 主攻撃の見積もり (ヴェスカの防御の前): 旧の式 / 倍率を読む式 (traceBoth か readOut のときだけ)
            public int dmgUsed;          // [V3 mainCorr] 効用に使った主攻撃の見積もり (実績の比を掛ける前・ヴェスカの防御の前)
            public int enemyHeal;        // [#107] 敵〈吸血〉で、 このターンの終わりに敵 HP が戻る量 (撃破しないときだけ)
            public bool balancePlus;     // [#107] このターンの収支プラスの見込み (〈反撃態勢〉〈暴走〉が次のターンに乗る)
            public int blazeGain;        // [2026-11-14] このターンの終わりに積もる烈炎の見込み (本体と同じ式 HeavyCycle.BlazeGain)
        }

        /// <summary>配線から出た「端子ごとの合計と個数」と、 端子の山札 (ADR-0014) の結果の追加の効果。 <see cref="OptimalUtilityCore"/> の入力。
        /// 旧い配線は <see cref="LegacySums"/> が出目と配線から組み、 山札は AutoRunner.Terminals の DeckSums が
        /// <see cref="CombatSystem.TerminalCards.Resolve"/> の結果から組む ── 効用の式は Core の 1 か所だけ。
        /// 旧い配線では追加の効果は全部「なし」(倍率 1・量 0) で、 Core の結果は分割の前と同じ。</summary>
        private struct WiredSums
        {
            public int aCount, bCount;
            /// <summary>攻撃の端子の枚数 = 主攻撃の撃の数 (ADR-0016・TerminalOutcome.attackHits)。 0 = 旧い配線 (1 撃とみなす)。</summary>
            public int aHits;
            /// <summary>充電の価値の単位 (旧い配線 = 充電に挿したダイスの本数。 山札 = 実際に増える充電 ÷ <c>DeckChargePipsPerDie</c>)。</summary>
            public float chargeUnits;
            public int aSum, blockRaw, chargeRaw, maxBlockDie;
            /// <summary>[2026-11-26] 混乱ゲージに入る量 (攻撃に挿した止まった面の和・本体と同じ CombatManager.GaugePips)。</summary>
            public int gaugePips;
            /// <summary>効用へそのまま足す分 (山札の先の得の見積もり)。</summary>
            public float specFlat;
            // ---- 端子の山札の結果 (旧い配線では「なし」) ----
            public float damageMul;      // 別枠の与ダメの掛け算 (1 = なし)
            public float outPctAdd;      // 与ダメ% の池へ足す分
            public float critMultAdd;    // このターンの会心倍率 +N
            public bool forceCrit;       // このターンは必ず会心
            /// <summary>攻撃に挿した目の一番大きい値 (〈鷹匠の革手袋〉) / 撃ごとの挿した目の一番大きい値 (〈狙い目の刻み〉を持つ時だけ・CritCore.HitFaceMax)。</summary>
            public int atkFaceMax;
            /// <summary>攻撃以外の端子に挿した目の合計 (竜閃〈無我無心〉) / このターン振った目の最大 (竜閃〈画竜点睛〉・2026-11-22)。</summary>
            public int otherDice, rolledMax;
            public int[] hitFace;
            public bool ignoreDefense;   // 敵の防御を無視
            public int armorPen;         // 敵の防御を N 無視 (〈継ぎ目のない導線〉)
            public float takenMul;       // このターンの被ダメに掛ける (1 = なし)
            public float enemyAtkMul;    // このターンの敵の攻撃に掛ける (1 = なし)
            public int shield, heal, selfDamage;
            /// <summary>このターンに主攻撃とは別に入る軽減不可ダメージ (端子の解決の外の品・〈焼き入れの挟み〉など・2026-11-21)。 撃破の見込みに入れ、 目盛りは割らない (ひび)。</summary>
            public int extraDmg;
            /// <summary>今ターン撃破したときだけ足す分 (〈魂狩り〉〈処刑の刃〉〈賞金狩り〉〈剥ぎ取り〉)。</summary>
            public float killBonus;

            public static WiredSums None() => new WiredSums { damageMul = 1f, takenMul = 1f, enemyAtkMul = 1f };
        }

        private float OptimalUtility(in OptimalTurnState s, int[] rolls, CombatSystem.DiceTerminal[] w, out OptimalTurnOutcome outcome)
        {
            var ws = LegacySums(in s, rolls, w);
            return OptimalUtilityCore(in s, rolls.Length, in ws, out outcome);
        }

        /// <summary>旧い配線 (攻撃/ブロック/充電) の出目の合計と個数 (山札では通らない)。</summary>
        private static WiredSums LegacySums(in OptimalTurnState s, int[] rolls, CombatSystem.DiceTerminal[] w)
        {
            int k = rolls.Length;
            int aCount = 0, bCount = 0, c = 0, aSum = 0, blockRaw = 0, maxBlockDie = 0;
            int chargeRaw = 0;
            for (int i = 0; i < k; i++)
            {
                int v = rolls[i];
                if (w[i] == CombatSystem.DiceTerminal.Attack)
                { aCount++; aSum += v; }
                else if (w[i] == CombatSystem.DiceTerminal.Block)
                {
                    bCount++; blockRaw += v;
                    if (v > maxBlockDie) maxBlockDie = v;
                }
                else if (w[i] == CombatSystem.DiceTerminal.Charge) { c++; chargeRaw += v; }
            }
            var ws = WiredSums.None();
            ws.aCount = aCount; ws.bCount = bCount; ws.chargeUnits = c;
            ws.aSum = aSum; ws.blockRaw = blockRaw; ws.chargeRaw = chargeRaw; ws.maxBlockDie = maxBlockDie;
            return ws;
        }

        /// <summary>効用の本体: 端子ごとの合計と個数 (<paramref name="ws"/>) → 効用。 盤面を読まない純粋な関数 (v3 の係数だけ読む)。
        /// <paramref name="k"/> = 振ったダイスの個数 (「全部攻撃」「全部ブロック」の判定に使う)。</summary>
        private float OptimalUtilityCore(in OptimalTurnState s, int k, in WiredSums ws, out OptimalTurnOutcome outcome)
        {
            var tele = s.tele; int enemyHp = s.enemyHp, myHp = s.myHp, myMaxHp = s.myMaxHp, atkBase = s.atkBase, enemyAtk = s.enemyAtk;
            float wAtk = s.wAtk, wDef = s.wDef, wChg = s.wChg;
            var play = s.play; var atkGrowth = s.atkGrowth;
            int eDefNow = s.eDefNow, myPen = s.myPen, myShield = s.myShield;
            int enemyMaxHpNow = s.enemyMaxHpNow;
            bool steadfastReady = s.steadfastReady;
            var rung = s.rung;
            int aCount = ws.aCount, bCount = ws.bCount, aSum = ws.aSum, blockRaw = ws.blockRaw, chargeRaw = ws.chargeRaw, maxBlockDie = ws.maxBlockDie;
            float c = ws.chargeUnits, specFlat = ws.specFlat;
            // 端子の山札: このターンの敵の攻撃 ×N (〈大結界〉)。 本体は防御の後の予告の値に掛ける (CombatManager.ApplyTerminalOutcome)。
            if (ws.enemyAtkMul < 1f) enemyAtk = System.Math.Max(0, Mathf.RoundToInt(enemyAtk * ws.enemyAtkMul));
            // 端子の山札: HP を払う (〈諸刃の剣〉・HP は 1 未満にならない) / 回復 (〈包帯〉〈応急箱〉)。 どちらも敵の攻撃の前に入る。
            int myHpEff = myHp;
            if (ws.selfDamage > 0 || ws.heal > 0)
                myHpEff = System.Math.Min(myMaxHp, System.Math.Max(1, myHp - ws.selfDamage) + ws.heal);
            int hpDelta = myHpEff - myHp;
            if (ws.ignoreDefense) eDefNow = 0;   // 〈貫通弾〉

            // [V3 readOutgoingMult] 本体は 攻撃の出目 + 端子調律 (本数) + 〈電流〉の② の合計に上乗せ率を掛ける。 旧の式はどちらも入れていない。
            bool wantNew = s.readOut || s.traceBoth;
            int aSumNew = aSum;
            if (wantNew)
            {
                aSumNew += aCount * s.atkPerDie;
                if (s.diceAtkPct > 0f && aSumNew > 0) aSumNew += CombatSystem.CombatRules.AttackDiceScaled(aSumNew, s.diceAtkPct);
            }
            // 攻撃に挿した出目への上乗せ率 (2026-11-06): 本体と同じく 攻撃の出目の合計 × 率 (切り上げ)
            if (s.diceAtkPct > 0f && aSum > 0)
                aSum += CombatSystem.CombatRules.AttackDiceScaled(aSum, s.diceAtkPct);
            // 剣〈切り返し〉: 攻撃と防御の端子を両方挿したターン、 ブロック × 0.6 を攻撃にも
            if (rung.guardCut && aCount > 0 && bCount > 0)
            { int gc = TurnRules.GuardCutBonus(blockRaw); aSum += gc; aSumNew += gc; }
            // 盾〈シールドバッシュ〉: 防御の端子なしで攻撃したターン、 シールド (半分 / 全部) を使って × 率 を攻撃へ
            bool bashSpent = rung.bashRatePct > 0 && aCount > 0 && bCount == 0 && myShield > 0;
            int bashUsed = bashSpent ? CombatSystem.CombatRules.BashSpend(myShield, rung.bashAll) : 0;
            if (bashSpent) { int bb = TurnRules.BashBonus(bashUsed, rung.bashRatePct); aSum += bb; aSumNew += bb; }
            int shieldKept = myShield - bashUsed;
            // 盾〈堅守の構え〉: 防御の端子に 2 個以上のターン、 シールド +18 (このターンの被弾から使える)
            if (steadfastReady && bCount >= ItemSystem.PassiveSkills.Effects.SteadfastStance.NeedBlockDice)
                blockRaw += ItemSystem.PassiveSkills.Effects.SteadfastStance.Shield;

            // 攻撃の端子に 1 つも挿さない手は主攻撃をしない (本体 CombatManager.Turn の skipAttack = attackDiceCount <= 0)。
            //   2026-11-13 まで旧の式はこの手でも武器の素火力 (atkBase) を与ダメに数えていた (新しい式 ReadOutMainDamage は 0)。
            //   攻撃なしでも通る削り (〈黎明〉など) は下で effDmg / noAtkDmg へ別に足すので、 ここで 0 にしても消えない。
            //   v3.oldNoAttackBaseDmg = true で旧い挙動 (素火力を数える) に戻す (測定の口)。
            //   ADR-0016: 素の攻撃力は撃の数だけ乗る (攻撃の端子 1 枚につき 1 撃・端子の外の攻撃は毎回)。
            int dmg = (aCount > 0 || v3.oldNoAttackBaseDmg) ? atkBase * System.Math.Max(1, ws.aHits) + aSum : 0;
            //   短剣〈連刃〉: 攻撃の端子 2 枚以上なら追撃 1 撃 (旧の式は防御を引く前の量として足す)
            if (rung.chainRatePct > 0 && ws.aHits >= ItemSystem.PassiveSkills.Effects.ChainBlade.NeedTerminals)
                dmg += CombatSystem.CombatRules.ChainHitBase(s.atkPower > 0 ? s.atkPower : atkBase, rung.chainRatePct);
            // 端子の山札の与ダメの効果 (旧の式 = 会心を見ない式なので、 基準を非会心とみなして差分だけ足す)。 旧い配線では全部「なし」で素通し。
            if (aCount > 0 && dmg > 0)
            {
                if (ws.outPctAdd > 0f) dmg = Mathf.CeilToInt(dmg * (1f + ws.outPctAdd));
                float cmEff = s.critMulEff > 0f ? s.critMulEff : s.critMul;
                if (ws.forceCrit) dmg = (int)(dmg * (cmEff + ws.critMultAdd));
                else if (ws.critMultAdd > 0f && s.critP > 0f) dmg += (int)(dmg * s.critP * ws.critMultAdd);
                if (ws.damageMul != 1f) dmg = System.Math.Max(1, Mathf.RoundToInt(dmg * ws.damageMul));
            }
            if (aCount > 0) dmg = CombatSystem.EnemyHpLedger.ProbeMain(dmg);   // [測定用の物差し 2026-11-10] 旧の式にも同じ位置で (防御の前)
            if (aCount > 0) dmg = ApplyFinalPct(dmg, s.finalPct + rung.FinalPct(s.solo, aCount, bCount, s.execBoost, s.shattered, s.undyingStreak + 1));   // 最終与ダメージ倍率 (武器の撃ごとの +% 込み): 本体と同じ位置 (物差しの直後・徒労の前)
            if (aCount > 0) dmg = MetaProgression.MetaDebuffApplicator.ApplyOutgoingCut(dmg);   // 〈徒労〉: 本体と同じ位置 (物差しの直後・防御の前)
            if (aCount > 0) dmg = CombatSystem.BossDown.ApplyTaken(dmg, DownTakesMul(s.enemyDown, s.enemyDownWeak));   // ボスのダウン ×1.5: 本体と同じ位置 (徒労の直後・よろめきは無し・強めたダウンの上乗せは ApplyTaken が読む)
            // 敵の防御 (このターンの値) で軽減
            if (aCount > 0 && eDefNow > 0)
                dmg = CombatSystem.DefenseFormula.Apply(dmg, CombatSystem.DefenseFormula.Effective(eDefNow, myPen + ws.armorPen));
            int dmgOld = dmg, dmgNew = 0, rawNew = wantNew ? s.atkPower + s.atkFlat + aSumNew : 0;
            // [V3 readOutgoingMult] 攻撃+N・与ダメ倍率・会心の期待値・端子調律・〈電流〉の② を本体の順で読む
            if (wantNew) dmgNew = ReadOutMainDamage(in s, aCount, bCount, aSumNew, in ws);
            if (s.readOut) dmg = dmgNew;
            int dmgUsed = dmg;
            // [V3 mainCorr] 配線の後で積まれる分を実績の比で埋める (主攻撃の項だけ・ここから effDmg → ttk / HP の帳簿の撃破までの見込みへ流れる)
            if (s.mainCorr > 0f && dmg > 0) dmg = Mathf.RoundToInt(dmg * s.mainCorr);
            // damageCapped ボス: 与ダメが閾値超えたら超過分を半減した見積もりで探索 (実挙動と一致)
            int effDmg = dmg;
            // 攻撃の端子なしでも通る削りの見積もり (〈黎明〉・品の軽減不可ダメージ)。 端子の撃破のボーナスを攻撃なしのターンに見る時に使う (#117)。
            //   旧い挙動 (v3.oldNoAttackBaseDmg) では effDmg が攻撃なしでも武器の素火力 (atkBase) を含むので、 攻撃なしの撃破にはこちらを使う。
            int noAtkDmg = 0;
            if (play.damageCapped && play.damageCapThreshold > 0 && dmg > play.damageCapThreshold)
                effDmg = play.damageCapThreshold + (dmg - play.damageCapThreshold) / 2;
            // 反射ボス: 与ダメの reflectRatio 分が自HPに返る想定でペナルティ
            float reflectPenalty = play.reflectsAttack ? effDmg * play.reflectRatio : 0f;

            // === 7層ヴェスカ〈遺物学者〉の抽選結果を織り込む (§13-4) ===
            // 予告で開示されている情報なので、 配線判断に使うのは完全情報の範囲内。
            if (aCount > 0)
            {
                // 天与の盾: このターンの与ダメは半分。 予告済みなので「効率が落ちる
                // ターン」として読める ── 無効ではないので押し切る選択も残る。
                if (tele.enemyHalvesDamageThisTurn) effDmg = (effDmg + 1) / 2;
                // 旧経路 (倍率) は互換のため残置。 現行の盾はここを通らない。
                else if (tele.enemyDamageTakenMul > 0f && tele.enemyDamageTakenMul < 1f)
                    effDmg = (int)(effDmg * tele.enemyDamageTakenMul);
                // 解析演算: 全ダメージを確率で回避 → 期待値で割る
                if (tele.enemyDodgeChance > 0f)
                    effDmg = (int)(effDmg * (1f - tele.enemyDodgeChance));
                // シールド: 与ダメを先に吸う。 吸われた分 × 反射率 が自 HP へ返る。
                if (tele.enemyShield > 0)
                {
                    int absorbed = System.Math.Min(tele.enemyShield, effDmg);
                    effDmg -= absorbed;
                    if (tele.enemyShieldReflectRate > 0f)
                        reflectPenalty += absorbed * tele.enemyShieldReflectRate;
                }
            }

            int mainEff = aCount > 0 ? System.Math.Max(0, effDmg) : 0;   // [2026-11-14] 目盛りを割れる主攻撃の見込み (黎明の削りは入れない)
            // [2026-11-26・Instant] この主攻撃で目盛りが割れたら、 その場でこのターンの敵の行動が止まる (溜め中は怯まない)。 本体と同じ規則 (BossDown.ClassifyInstant)
            //   [2026-11-26] 混乱ゲージ: 配線の時点で満ちる (撃の前・与ダメに依らない)。 撃破する手は数えない
            bool instBreak = s.downInstant && !s.enemyDown && s.downGaugeMax > 0 ? effDmg < enemyHp && s.downGauge + ws.gaugePips >= s.downGaugeMax
                           : s.downInstant && s.downNotches != null && !s.enemyDown && mainEff > 0 && mainEff < enemyHp
                             && DownForecast(in s, enemyHp, mainEff, mainEff).fullAdded > 0;
            if (instBreak && s.downBreakKindNow != (int)CombatSystem.DownKind.Resisted) enemyAtk = 0;
            if (ws.extraDmg > 0) { effDmg = System.Math.Max(0, effDmg) + ws.extraDmg; noAtkDmg += ws.extraDmg; }   // 品の軽減不可ダメージ (主攻撃ではない)
            int block = blockRaw;
            // 〈貫きの錐〉: 敵攻撃値 1 につきブロックを 2 削る (予告済み)。
            if (tele.playerBlockIgnored)
                block = System.Math.Max(0, block - enemyAtk * CombatSystem.CombatManager.PierceAwlPerAttack);

            // 端子の山札: このターンに得たシールド (〈二重障壁〉〈装甲板〉) はこのターンの被弾から吸う / 被ダメ −N% (〈受け流し〉〈防御陣〉〈身代わり札〉)
            if (tele.enemyAttackIgnoresGuard) block = 0;      // 〈狂暴化〉: ブロックもシールドも受けない (本体と同じ)
            int taken = System.Math.Max(0, enemyAtk - block - (tele.enemyAttackIgnoresGuard ? 0 : ws.shield));
            // 敵の攻撃が通った時の上乗せ (〈剛力〉〈闇技〉〈研ぎ澄まし〉・2026-11-12)。 本体はブロックの後に 1 以上残る時だけ、 プレイヤーの軽減の前に足す
            if (s.enemyHitBonus > 0 && enemyAtk - block > 0) taken += s.enemyHitBonus;
            // [#107] 収支の見積もりに使う、 シールドの前の 1 回目 / 2 回目の攻撃 (本体の takenDmg / secTaken のシールドの前)
            int hit1Raw = System.Math.Max(0, enemyAtk - block), hit2Raw = 0;
            if (hit1Raw > 0) hit1Raw += s.enemyHitBonus;
            // 敵〈多頭攻撃〉(2026-11-12): 攻撃の個数 < ブロックの個数なら同じ攻撃値でもう 1 回 (ブロックは 2 回とも全額・シールドは 1 回目で使った分を引く)
            if (s.enemyDoubleOnBlockHeavy && ws.bCount > ws.aCount && enemyAtk > 0)
            {
                int shieldLeft = System.Math.Max(0, ws.shield - System.Math.Max(0, enemyAtk - block));
                taken += System.Math.Max(0, enemyAtk - block - shieldLeft);
                hit2Raw = System.Math.Max(0, enemyAtk - block);   // 2 回目には通った時の上乗せが乗らない (本体: 2 回目の ProcessDamage は OnPreDealDamage を鳴らさない)
            }
            if (ws.takenMul < 1f && !tele.enemyAttackIgnoresGuard) taken = Mathf.RoundToInt(taken * ws.takenMul);
            //   〈死相の面頬〉(2026-11-23): 会心したターンの敵の攻撃を減らす (上限 45% / 大技 55%)
            if (s.mask && taken > 0 && ws.aHits > 0 && !tele.enemyAttackIgnoresGuard)
            {
                float pAny = 1f - (float)System.Math.Pow(1f - s.critP, ws.aHits);
                taken = Mathf.RoundToInt(taken * (1f - pAny * ItemSystem.PassiveSkills.Effects.DeathMaskVisor.Cut(s.renki + 1, tele.turnsToHeavy == 0)));
            }
            if (ws.aHits == 1 && s.focusCount >= CombatSystem.CritCore.FocusNeedCount && taken > 0 && !tele.enemyAttackIgnoresGuard)
            {
                float pFoc = System.Math.Min(CombatSystem.CritCore.FocusMaxRate, s.critP);
                taken += Mathf.RoundToInt(taken * (1f - pFoc) * CombatSystem.CritCore.FocusMissTakenBonus);
            }
            taken += (int)reflectPenalty;
            // 【ジャスト防御】の見込み (本体 ShieldCore.IsJustGuard・許容幅は既定・HP の減り 0)。 盾〈黎明〉〈城壁〉が読む
            bool jgNow = taken == 0 && CombatSystem.ShieldCore.IsJustGuard(enemyAtk, block + ws.shield + shieldKept, CombatSystem.ShieldCore.JustWidth + CombatSystem.CardRules.JustWidthAdd);
            // 挑戦カード〈見切り〉: ジャスト防御の時の防御無視の反撃 (本体と同じく固定の量)
            if (jgNow && CombatSystem.CardRules.JustCounter > 0) { effDmg += CombatSystem.CardRules.JustCounter; noAtkDmg += CombatSystem.CardRules.JustCounter; }
            // 盾〈黎明〉: 無傷で受け切ったら、 敵の攻撃 10 につき敵の最大 HP の 5% (ジャスト防御なら 8%) を削る
            if (rung.daybreak && taken == 0)
            {
                int dbNow = TurnRules.DaybreakDamage(enemyMaxHpNow, enemyAtk, jgNow);
                effDmg += dbNow;
                noAtkDmg += dbNow;
            }

            // 処刑人の烙印: 攻撃終了時に HP が最大の 20% 以下なら 9999。
            if (tele.executeArmed && TurnRules.ExecuteKills(myHpEff - taken, myMaxHp))
                taken = myHpEff;

            // ===== 敵のパッシブ: 収支で決まる効果と〈吸血〉(#107・2026-11-12) =====
            //   本体: 収支 = (与ダメ + 固定) − 敵の攻撃で減った HP (CombatManager.Turn.cs の `int balance = (dealtDmg + dealtFixed) - takenApplied`)。
            //   収支プラス → 敵の OnRollLose (〈尾撃〉〈重甲〉の反射・〈反撃態勢〉〈暴走〉を次のターンへ) / マイナス → OnRollWin (〈地獄の業火〉)。
            //   ここでは 与えた = effDmg、 受けた = 敵の攻撃 (シールドの前) からシールド (持ち越し + このターンの端子) を引いた HP の減り。
            //   撃破するターンは敵が攻撃しない (`else if (enemyHP > 0)`) ので受けた = 0。 収支トリガーは撃破のターンにも鳴る。
            int enemyHeal = 0; bool balancePlus = false;
            if (s.enemyBalanceRead)
            {
                bool enemyAttacks = effDmg < enemyHp;
                float tm = ws.takenMul < 1f ? ws.takenMul : 1f;
                int h1 = 0, hAll = 0;
                if (enemyAttacks)
                {
                    h1 = Mathf.RoundToInt(hit1Raw * tm);
                    hAll = Mathf.RoundToInt((hit1Raw + hit2Raw) * tm);
                }
                int pool = shieldKept + ws.shield;   // シールドは敵の攻撃で先に減り、 残りが軽減無視を肩代わりする (③-C)
                int hp1 = System.Math.Max(0, h1 - pool);
                int hpAll = System.Math.Max(0, hAll - pool);
                int shieldAfter = System.Math.Max(0, pool - hAll);
                int balance = effDmg - hpAll;
                balancePlus = balance > 0;
                // 〈尾撃〉〈重甲〉(収支プラス) / 〈地獄の業火〉(収支マイナス): その場で EnemyDealUnmitigable (CombatContext.cs)
                int chip = balance > 0 ? s.enemyChipOnPlus : balance < 0 ? s.enemyChipOnMinus : 0;
                if (chip > 0)
                {
                    if (s.shieldAbsorbsChip) chip = System.Math.Max(0, chip - shieldAfter);
                    taken += chip;
                }
                // 〈吸血〉: 敵の攻撃の末尾で、 通った主攻撃 (1 回目・シールドの後) ÷2 を回復 (最大 HP まで)
                if (enemyAttacks && s.enemyLifesteal > 0)
                    enemyHeal = System.Math.Min(LifestealHeal(in s, hp1),
                                                System.Math.Max(0, enemyMaxHpNow - (enemyHp - effDmg)));
            }
            float nextTurnExtra = balancePlus ? s.nextHitOnPlus + s.nextAtkOnPlus : 0f;

            float u = s.ledger
                    ? specFlat - LedgerLoss(in s, effDmg, taken, enemyHeal, nextTurnExtra)
                    : wAtk * System.Math.Min(effDmg, enemyHp)
                    + wDef * System.Math.Min(block, enemyAtk)
                    + specFlat;
            // 端子の山札: 敵の攻撃の前に動いた HP (回復 − 払った HP)。 帳簿は HP の単位なのでそのまま、 旧の効用は守りの重みで。 旧い配線では 0。
            if (hpDelta != 0) u += s.ledger ? hpDelta : wDef * hpDelta;
            // 端子の山札: 今ターン撃破で得る恒久のボーナス (本体 FinishTerminalTurn と同じ条件・#117)。
            //   今の作り = 撃破の経路を問わない。 攻撃の端子に挿していなければ、 攻撃なしでも通る削り (noAtkDmg) で倒れるかを見る
            //   (撃破のボーナスの端子は 4 種とも攻撃の種類 ── 〈剥ぎ取り〉は 2026-11-13 に補助から攻撃へ ── なので、 発動すれば aCount > 0。 下の攻撃なしの枝は守り)。
            //   毒・出血・ターン終了時の削りは、 ここでは見ていない (主攻撃の見積もりと同じく)。
            //   旧い条件 (terminalKillNeedsAttack) = 攻撃したターンだけ。
            if (ws.killBonus != 0f && (aCount > 0 ? effDmg >= enemyHp : !terminalKillNeedsAttack && noAtkDmg >= enemyHp)) u += ws.killBonus;
            // 盾〈城壁〉: 完全に受け止めたら、 余ったブロック × 率 (ジャスト防御なら固定) がシールドへ (次のターン以降の弾・毎ターン半減するので半値で見る)
            if (rung.bulwarkRatePct > 0)
                u += 0.5f * wDef * TurnRules.BulwarkGain(block, enemyAtk, rung.bulwarkRatePct, jgNow, rung.bulwarkJust);
            // 〈シールドバッシュ〉で使ったシールドは、 このターンの被弾を吸えない
            if (bashSpent) u -= wDef * System.Math.Min(bashUsed, System.Math.Max(0, enemyAtk - block));
            if (effDmg >= enemyHp && !s.ledger) u += 50f + enemyAtk; // 今ターン撃破 = 敵攻撃が消える (帳簿では先の被ダメ 0 として既に入っている)
            if (taken >= myHpEff) u -= 10000f;           // 致死回避を最優先 (山札の回復・払った HP の後の HP で見る)
            // ボスのダウン (2026-11-13): この手で目盛りを割って次のターンをダウンにするなら、 次のターンの敵の攻撃の一部を今の HP で足す。
            //   判定は本体と同じ関数 (BossDown.NextTurnIsDown)。 何もしなくても次がダウン (前に割った分が残っている) なら手の差にならないので足さない。
            //   段取り (溜めのターンに合わせて割る) は 2 ターン先読み (deckLookahead) の役目 ── ここでは次のターンの 1 回分だけ。
            //   [2026-11-14] 割れるかは本体と同じ規則 (BossDown.ForecastTurn・口 downBreakRule)。 新しい判定 = 主攻撃 (mainEff) だけで割れる・通常攻撃以外の削り (downChip) はひび。
            //   旧い判定 (AnyDamage) は今までどおり 与ダメの見込み (effDmg) の後の HP で数える (NextTurnIsDown と同じ)。
            if (instBreak)
            {
                // 割った瞬間に効く規則: このターンの攻撃は上で 0 にした。 大技を止めたら次のターンは大ダウン (次の攻撃も来ない)・
                //   チャージや小技で割ると、 近い大技を止める機会を捨てる
                float w = s.ledger ? 1f : wDef;
                var kNow = (CombatSystem.DownKind)s.downBreakKindNow;
                if (kNow == CombatSystem.DownKind.Big)
                {
                    u += DownNextTurnShare * System.Math.Max(0, s.downNextAtk) * w;
                    // 大ダウンのターンは与ダメ ×倍率 (2026-11-26・×2.0): 増える与ダメ ≒ (倍率 − 1) ターン分 → その分だけ戦闘が縮み、 先で受ける攻撃が減る (帳簿と同じ物差し・先の割合を掛ける)
                    if (s.ledger) u += (CombatSystem.BossDown.TakenMulCard - 1f) * FutureEnemyAtk(in s) * v3.ledgerFutureShare;
                }
                else if (kNow == CombatSystem.DownKind.Resisted) u -= DownNextTurnShare * s.downHeavyNextAtk * w;
                else if (s.downHoldCost > 0) u -= DownNextTurnShare * s.downHoldCost * w;
            }
            else if (s.downInstant && s.downGaugeMax > 0 && !s.enemyDown && s.downToHeavy >= 1 && s.downHeavyAtk > 0 && effDmg < enemyHp)
            {
                // [2026-11-26] 混乱ゲージの支度: 満たさずに、 大技のターンに満たせる位置へ寄せた手に、 大技を止める得の見込みを足す (残りが 0 より大きく、 大技までのターン × 1 ターンに入れられる量以内)
                int rest = s.downGaugeMax - (s.downGauge + ws.gaugePips);
                if (rest > 0 && rest <= s.downToHeavy * s.downGaugePerTurn)
                    u += DownNextTurnShare * DownGaugeReadyShare * s.downHeavyAtk * (s.ledger ? 1f : wDef);
            }
            else if (!s.downInstant && s.downNotches != null && effDmg < enemyHp
                && DownForecast(in s, enemyHp, mainEff, effDmg).nextDown
                && !DownForecast(in s, enemyHp, 0, 0).nextDown)
            {
                float w = s.ledger ? 1f : wDef;
                if (s.downNextResisted)
                    u += (downResistShare >= 0f ? downResistShare : DownNextTurnShare) * s.downEraseAtk * w;
                else
                {
                    u += DownNextTurnShare * System.Math.Max(0, s.downEraseAtk) * w;
                    if (downStaggerPenaltyShare > 0f && s.downStaggerOppAtk > 0)
                        u -= downStaggerPenaltyShare * s.downStaggerOppAtk * w;
                }
            }

            // ===== 短期決戦の概念 (2026-08-04) =====
            //   従来の効用は **1 ターンぶんの収支しか見ていなかった**。 撃破ボーナスも
            //   「**今ターン**倒せるなら」だけで、 2〜3 ターンで倒せる配分を評価できない。
            //   その結果 wDef * min(block, enemyAtk) が敵攻撃値に比例して伸び、
            //   強敵ほど防御が常に勝つ。 ブロックは敵 HP を減らさないので戦闘が伸び、
            //   総被ダメは増えるが BOT はそれを見られない
            //   （実測: 6F ボス戦の防御配線率 95.6% ＝ 亀に固定）。
            //
            //   **撃破までの残りターン数そのものを罰する。**
            //
            //   試して外した定式化 (2026-08-04・再提案しないこと):
            //     ① `taken * ttk` … 完全に防ぎ切ると taken=0 で罰が 0 になる。
            //        BOT は全層で亀に固定 (1F 防御配線率 18.6%→95.0%)、 完全クリア 9.1%→2.8%。
            //     ② ①をエスカレーション曲線で射影 … 短い戦闘では曲線が立ち上がらず罰が 0 のまま。
            //   どちらも「被弾量」経由で時間を測ろうとしたのが誤り。 防ぎ切れる相手には
            //   被弾量が 0 に張り付くので、 時間の情報が消える。 ここでは ttk を直接引く。
            int ttk = effDmg > 0
                    ? UnityEngine.Mathf.CeilToInt((float)enemyHp / effDmg)
                    : HorizonCapTurns;
            if (ttk > HorizonCapTurns) ttk = HorizonCapTurns;
            // [#107] 敵〈吸血〉: 撃破しないターンの終わりに戻る分を、 同じ挿し方を続けた前提で毎ターン差し引く
            if (enemyHeal > 0 && effDmg > 0 && effDmg < enemyHp)
                ttk = TurnsToKillWithHeal(enemyHp, effDmg, enemyHeal, enemyMaxHpNow);
            if (!s.ledger) u -= TimeCostPerTurn * v3.timeCostMul * ttk;

            // 防ぎ切りの規則 (2026-11-05・上の FreeBlockRule)。 撃破まで HP を失わない配線を優先する。
            //   その中での順位は従来の効用のまま (速く倒せる方・今ターン撃破が上)。
            bool fbCovers = false, fbKills = false;
            if (atkGrowth != null && aCount > 0)
            {
                bool killsNow = effDmg >= enemyHp;
                bool coversFight = block >= UnityEngine.Mathf.CeilToInt(enemyAtk * atkGrowth[System.Math.Min(ttk, HorizonCapTurns)]);
                if (!s.ledger && (killsNow || (coversFight && !(v3.fbCheckUnavoid && _fbUnavoidSeen)))) u += FreeBlockBonus;
                fbCovers = coversFight; fbKills = killsNow;
            }

            // 烈炎 (6 層ボス): ターンの終わりにスタックが積もり、 **配線側がこれを見られないと
            //   ただの一方的な税になる**ので、 撃破までの累積を効用へ入れる。
            //   2026-11-14〜 (作者の決定): 積もる数 = このターンに敵の主攻撃をシールドで肩代わりする見込み ÷ BlazePerShield (切り捨て)。
            //   式は本体と同じ HeavyCycle.BlazeGain (口 blazeStackRule=Guard2 = 旧い「ブロック端子へ 2 本以上で +1」・その時は今までと同じ値付け)。
            //   シールドの見込み = min(敵の攻撃の前のシールド (持ち越し + このターンの端子・バッシュで使い切った分は除く), ブロックの後の主攻撃 (1 回目 + 〈多頭攻撃〉の 2 回目))
            //   ── 収支の見積もり (上の pool) と同じ近似。 撃破するターン・チャージ・ダウンは敵が攻撃しないので 0。
            //   2026-11-14〜: 毎ターンの削りは既定で無し (口 blazeTickEachTurn=True で残す)。 代わりに積もった烈炎は王の大技〈業火〉で
            //   × BlazeBurstPerStack にまとめて返り、 撃ったら 0 (本体 CombatManager.ApplyBlazeBurst)。 値付けは「撃破より前に業火が来るなら、 その時の烈炎 × 1 つあたり」。
            int blazeGain = 0;
            if (tele.blazePenalizesBlock)
            {
                int s0 = tele.blazeStacks;
                int shieldAbs = 0;
                if (effDmg < enemyHp)
                {
                    float tmB = ws.takenMul < 1f ? ws.takenMul : 1f;
                    int hitsB = Mathf.RoundToInt((hit1Raw + hit2Raw) * tmB);
                    shieldAbs = System.Math.Min(shieldKept + ws.shield, hitsB);
                }
                blazeGain = CombatSystem.HeavyCycle.BlazeGain(GameLoop.GameHooks.BlazeStack, shieldAbs, s.blazePerShield,
                                                              bCount, ItemSystem.PassiveSkills.Effects.BlazeBrand.BlockDiceThreshold);
                if (GameLoop.GameHooks.BlazeTickEachTurn)
                {
                    float blaze = s0 * ttk + blazeGain * ttk * (ttk + 1) * 0.5f;   // 積み続ける (積もらないなら据え置き)
                    u -= blaze;
                }
                if (s.blazeBurst && tele.turnsToHeavy >= 0 && !s.enemyDown)
                {
                    int th = tele.turnsToHeavy;
                    // このターンが業火: 撃つ前に倒せなければ、 今の烈炎がこのターンに返る (軽減無視)。 撃った後の烈炎は 0 から
                    if (th == 0 && effDmg < enemyHp) u -= s0 * s.blazeBurstPer;
                    int nextIn = th == 0 ? System.Math.Max(1, s.heavyPeriodNow) : th;
                    int stacksThen = (th == 0 ? 0 : s0) + blazeGain;   // このターンの分はターンの終わり (業火を撃った後) に積もる
                    if (nextIn <= ttk) u -= stacksThen * s.blazeBurstPer;   // 次の業火まで生きている見込みなら、 そこで返る
                }
            }
            u += wChg * v3.chargeMul * c * 2f;           // 充電価値 (リロールの原資)
            if (v3.takenCost != 0f && !s.ledger) u -= v3.takenCost * _takenMulNow * taken;     // E5 (V3)
            u += 0.01f * aCount;                         // タイブレーク: 同効用なら攻撃を選ぶ

            // 2026-07-28: 攻撃端子 0 本 = **攻撃を行わない** (素火力も追撃も出ない)。
            //   よって「攻撃 0 本」は、 その 1 本を防御へ回さないと死ぬ場合にのみ選ぶ。
            if (aCount == 0 && !s.noAttackForced)
            {
                // 「ブロックの最大目を 1 本だけ攻撃へ回す」場合の被弾と比較する。
                int takenIfAttackOne = System.Math.Max(0, enemyAtk - (block - maxBlockDie));
                bool onlySurvivesByNotAttacking = (takenIfAttackOne >= myHpEff) && (taken < myHpEff);
                bool holdNotchAllowed = downAllowNoAttackHold && s.downNotches != null
                    && (s.downNextResisted || (downStaggerPenaltyShare > 0f && s.downStaggerOppAtk > 0))
                    && !DownForecast(in s, enemyHp, 0, 0).nextDown;
                bool holdGaugeAllowed = downGaugeNoAttackHold && EffectiveWiringSkill == WiringSkill.Super && s.downInstant && s.downGaugeMax > 0 && !s.enemyDown
                    && (s.downToHeavy == 1 || s.downToHeavy == 2) && s.downGaugeMax - s.downGauge <= s.downGaugePerTurn;
                if (!onlySurvivesByNotAttacking && !holdNotchAllowed && !holdGaugeAllowed) u -= 10000f;
            }
            outcome = new OptimalTurnOutcome { effDmg = effDmg, mainDmg = mainEff, gaugePips = ws.gaugePips, taken = taken, block = block, aCount = aCount, bCount = bCount, bashUsed = bashUsed, justGuard = jgNow,
                                               freeBlock = fbCovers, killsNow = fbKills, dmgOld = dmgOld, dmgNew = dmgNew, rawNew = rawNew,
                                               dmgUsed = aCount > 0 ? dmgUsed : 0, enemyHeal = enemyHeal, balancePlus = balancePlus, blazeGain = blazeGain };
            return u;
        }


        // ============================================================
        //  E8: 2 ターン先読み (SuperV3・2026-11-05)
        // ============================================================
        /// <summary>1 回の配線判断のあいだ変わらない、 次ターンの前提と memo。</summary>
        private sealed class NextTurnPlan
        {
            public OptimalTurnState next;      // 次ターンの盤面の雛形 (敵 HP・自 HP・シールド・連続数・堅守・防御は候補ごとに上書き)
            public EnemyArmorModel armor;
            public int baseDef;                // 敵の防御の持続分 (構えの増減を除く)
            public int turn;                   // このターン
            public bool steadfastOn;
            public int steadfastReadyTurn;
            public int[] faces;                // 次の出目の面 (全ダイス共通)
            public int termKinds;
            public int nextBlazeBase;
            public float enemyMaxHp;           // 敵の最大 HP (Phase 判定の比に使う)
            public readonly Dictionary<(int, int, int, int, int, int, int), float> memo
                = new Dictionary<(int, int, int, int, int, int, int), float>(32);
        }

        private NextTurnPlan BuildNextTurnPlan(in OptimalTurnState st, CombatManager cm,
                                               ItemSystem.PassiveSkills.CombatContext ctx, GameLoop.RunState run, int k)
        {
            var enemy = cm.CurrentEnemy;
            var plan = new NextTurnPlan { turn = ctx.currentTurn };
            plan.armor = EnemyArmorModel.From(enemy, ctx, out plan.baseDef);
            plan.steadfastOn = WeaponRungModel.From(ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.ActivePlayerSkillIds).steadfast;
            plan.steadfastReadyTurn = (int)ctx.GetAccumulated(ItemSystem.PassiveSkills.Effects.SteadfastStance.ReadyKey);
            plan.faces = (ctx.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0) ? ctx.equippedDiceFaces : new[] { 1, 2, 3, 4, 5, 6 };
            plan.termKinds = 3;      // 攻撃・ブロック・充電
            plan.enemyMaxHp = System.Math.Max(1, enemy.maxHP);

            int nt = plan.turn + 1;
            int nextAtk = ExpectedEnemyAttackAt(enemy, ctx, in st.tele, nt, 0);

            // 次ターンの予告: 抽選で決まるもの (ヴェスカの盾・回避・遺物・封印・麻痺毒) は未知なので「無し」とみなす
            var tele = st.tele;
            tele.turn = nt;
            tele.enemyAttackValue = nextAtk;
            tele.secondaryAttackValue = 0;
            tele.enemyHalvesDamageThisTurn = false;
            tele.enemyDamageTakenMul = 0f;
            tele.enemyDodgeChance = 0f;
            tele.enemyShield = 0;
            tele.enemyShieldReflectRate = 0f;
            tele.playerAttackPenalty = 0;
            tele.playerBlockIgnored = false;
            tele.enemyAttackIgnoresGuard = nt > ItemSystem.PassiveSkills.Effects.Berserk.EnrageTurn;   // 〈狂暴化〉は全部の敵が持つ (2026-11-21)・一度立てば続く
            tele.sealedTerminal = -1;
            plan.nextBlazeBase = tele.blazeStacks;
            // [2026-11-14] 王の〈業火〉: このターンが業火 (ダウンでない) なら撃った後の烈炎は 0。 次のターンの大技までも数え直す (旧い形では通らない)
            if (st.blazeBurst)
            {
                if (st.tele.turnsToHeavy == 0 && !st.enemyDown) plan.nextBlazeBase = 0;
                tele.turnsToHeavy = CombatSystem.Escalation.TurnsToHeavy(enemy, nt);
            }

            var ns = st;
            ns.tele = tele;
            ns.enemyAtk = nextAtk;
            ns.atkBase = System.Math.Max(0, cm.PlayerAttackPower);
            ns.atkPower = System.Math.Max(0, cm.PlayerAttackPower); ns.atkPenalty = 0;   // [readOutgoingMult] 次ターンの麻痺毒は未知
            ns.atkGrowth = (FreeBlockRule && nextAtk > 0) ? BuildAtkGrowth(enemy, ctx, nt) : null;
            // [2026-11-14] 次のターンが大技のチャージなら (予告 0)、 その先の被ダメは次の次 (大技) の見込みで伸ばす
            ns.charging = CombatSystem.Escalation.IsChargeNoAttack(enemy, nt);
            ns.tele.enemyCharging = ns.charging;
            ns.chargeNextAtk = ns.charging ? ExpectedEnemyAttackAt(enemy, ctx, in st.tele, nt + 1, 0) : 0;
            // [#107] 前のターンに積まれた〈反撃態勢〉はこのターンだけ。 次のターンへ乗るかは今ターンの収支で決まる (LookaheadValue)
            ns.enemyHitBonus = System.Math.Max(0, st.enemyHitBonus - st.counterPendingNow);
            ns.counterPendingNow = 0;
            plan.next = ns;
            return plan;
        }

        /// <summary>[#107] 前のターンに積まれた〈暴走〉のこのターンの分 (本体: EnemyPassiveSkillEffects.Rampage が次のターンの敵の OnPostRoll で
        /// 敵の出目の合計へ足す → 予告の enemyDiceTotal に入っている)。 nextTurnBuffs の専用キーは本体の定数を読む (写しを持たない)。</summary>
        private const string RampageNextTurnKey = ItemSystem.PassiveSkills.Effects.Rampage.NextTurnKey;
        /// <summary>[#107] 同じく〈反撃態勢〉(本体: EnemyPassiveSkillEffects.CounterStance・次のターンの敵の OnPreDealDamage で finalDamage += )。</summary>
        private const string CounterStanceNextTurnKey = ItemSystem.PassiveSkills.Effects.CounterStance.NextTurnKey;

        /// <summary>このダウンが受けるダメージ ×1.5 を持つか (本体の BossDown.TakenMulNow と同じ条件: よろめきは無し・〈骨溶かしの黒油〉はよろめきにも有り・2026-11-25)。</summary>
        internal static bool DownTakesMul(bool down, bool weak)
            => down && (!weak || (ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context?.poisonDownDouble ?? false));

        /// <summary>[2026-11-14] 目盛りの割れ方の見込み (本体と同じ規則 BossDown.ForecastTurn を、 口 GameHooks.DownBreak / DownCrackGrace / DownCrackWeak で呼ぶ)。
        /// <paramref name="mainDmg"/> = 主攻撃の見込み / <paramref name="totalDmg"/> = このターンの与ダメの見込み (旧い判定はこの後の HP で数える = NextTurnIsDown と同じ)。
        /// 新しい判定では、 主攻撃の後に入る通常攻撃以外の削りを <see cref="OptimalTurnState.downChip"/> (固定ダメの枠の毒など + 出血) と、 与ダメの見込みのうち主攻撃以外の分の大きい方で見る。</summary>
        private static CombatSystem.BossDown.Forecast DownForecast(in OptimalTurnState s, int hpStart, int mainDmg, int totalDmg, bool wantCracks = false)
        {
            var rule = GameLoop.GameHooks.DownBreak;
            int afterMain = rule == CombatSystem.DownBreakRule.AnyDamage ? System.Math.Max(0, totalDmg - mainDmg)
                          : System.Math.Max(s.downChip, System.Math.Max(0, totalDmg - mainDmg));
            return CombatSystem.BossDown.ForecastTurn(rule, GameLoop.GameHooks.DownCrackGrace, GameLoop.GameHooks.DownCrackWeak,
                                                      s.downNotches, s.downMaxHp, s.downBroken, s.downPendingNow, s.downPendingHeadWeak,
                                                      s.downCrackTurns, s.downTurn, hpStart, mainDmg, afterMain, wantCracks);
        }

        /// <summary>[2026-11-14] このターンに主攻撃の後で敵の HP に入る、 通常攻撃以外の削りの見込み: 固定ダメの枠に溜まった分 (ターン頭の毒・品が預けた分) + 出血 (本体の式)。
        /// 読むだけ (乱数を引かない)。 ターン終了時の削り・反射・品の攻撃後の直接は入れない (見立て)。</summary>
        private static int DownChipEstimate(ItemSystem.PassiveSkills.CombatContext ctx)
        {
            if (ctx == null) return 0;
            int bleed = ctx.enemyBleedStacks > 0 ? CombatSystem.BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks) : 0;
            return System.Math.Max(0, ctx.fixedDamageToEnemy) + System.Math.Max(0, bleed);
        }

        /// <summary>ターン <paramref name="turn"/> の敵の攻撃の見込み (プレイヤーの防御の後)。 公開規則 (Escalation.EnemyAttackValue) に、 敵ダイスは面の平均を入れる。
        /// ダイス合計のパッシブ補正は今ターンの予告との差で見る (前のターンの〈暴走〉はこのターン限りなので除く・#107)。
        /// <paramref name="extraDice"/> = 敵の出目の合計へさらに足す分 (〈暴走〉の +3 の見積もり)。 プレイヤーの防御は本体と同じ関数を通す。</summary>
        private static int ExpectedEnemyAttackAt(EnemyData enemy, ItemSystem.PassiveSkills.CombatContext ctx,
                                                 in CombatSystem.MutualTurnTelegraph teleNow, int turn, int extraDice)
        {
            int diceNow = 0;
            if (teleNow.enemyDice != null) for (int i = 0; i < teleNow.enemyDice.Length; i++) diceNow += teleNow.enemyDice[i];
            int bonus = teleNow.enemyDice != null ? teleNow.enemyDiceTotal - diceNow : 0;
            bonus -= (int)ctx.GetBuff(RampageNextTurnKey);
            int expTotal = (int)System.Math.Round(enemy.EffectiveRollCount * (1 + enemy.EffectiveRollMax) / 2.0 + bonus) + extraDice;
            int delay = (int)ctx.GetAccumulated(CombatSystem.CombatManager.CoolantDelayKey);
            return CombatSystem.CombatManager.ApplyPlayerDefense(ctx,
                CombatSystem.Escalation.EnemyAttackValue(enemy, System.Math.Max(1, turn - delay), expTotal, turn), ctx.enemyArmorPen);
        }


        /// <summary>[2026-11-14] 新しい取り消しの規則 (チャージか大技のターンに重なったダウンだけが周期を頭に戻す) で、 次のターン (T+1) をダウンにした時の
        /// 受けずに済む攻撃 <paramref name="erase"/> と、 明けたターン (T+2) の攻撃の見込み <paramref name="after"/>。 取り消すかは本体と同じ関数
        /// (<see cref="CombatSystem.CombatManager.DownCancelsCycleAt"/>)、 攻撃は本体の式 (<see cref="ExpectedEnemyAttackAt"/>)。
        /// 取り消す時の T+2 は、 周期の起点を T+1 に置いた時の値 (起点を一時的に置き換えて本体の式を通し、 戻す・乱数は引かない)。</summary>
        private static void DownSequenceAfterNextTurn(EnemyData enemy, ItemSystem.PassiveSkills.CombatContext ctx, in CombatSystem.MutualTurnTelegraph tele,
                                                      int s1, float bossHpRatio, CombatSystem.DownCancelRule rule, out int erase, out int after)
        {
            int t1 = tele.turn + 1;
            erase = s1;
            after = ExpectedEnemyAttackAt(enemy, ctx, in tele, t1 + 1, 0);
            if (enemy == null || !CombatSystem.CombatManager.DownCancelsCycleAt(enemy, t1, bossHpRatio, rule)) return;
            // チャージのターンのダウン: 次の大技 s(T+2) が並びから抜け、 後ろが 1 ターン遅れる (DownKeepRules.HoldGainChargeOrHeavy と同じ考え・遅れた分は周期の平均で置く)
            if (enemy.heavyPeriod > 0 && CombatSystem.Escalation.IsChargeTurn(enemy, t1))
            {
                float avg = 0f;
                int per = System.Math.Max(1, CombatSystem.Escalation.HeavyPeriodNow(enemy));   // 王の〈業火〉は HP で 4 / 3
                for (int j = 0; j < per; j++) avg += ExpectedEnemyAttackAt(enemy, ctx, in tele, t1 + j, 0);
                avg /= per;
                erase = s1 + Mathf.RoundToInt(after - avg);
            }
            int saved = CombatSystem.Escalation.HeavyCycleOffset;
            try
            {
                CombatSystem.Escalation.HeavyCycleOffset = t1;
                after = ExpectedEnemyAttackAt(enemy, ctx, in tele, t1 + 1, 0);
            }
            finally { CombatSystem.Escalation.HeavyCycleOffset = saved; }
        }

        /// <summary>このターンを解いた結果 <paramref name="o"/> から、 次のターンの盤面を組む (E8 と山札の先読み #105 が共通で使う)。
        /// <paramref name="hpStart"/> = 敵の攻撃の前の自 HP (旧い配線は今の HP・山札は回復と払った HP の後)、
        /// <paramref name="shieldPool"/> = 敵の攻撃の前のシールド (旧い配線は持ち越し分・山札はこのターンの端子の分も)。
        /// <paramref name="plusNext"/> = [#107] 次のターンに〈反撃態勢〉〈暴走〉が乗るか (memo の鍵の一部)。</summary>
        private OptimalTurnState NextTurnStateFrom(in OptimalTurnState st, NextTurnPlan plan, in OptimalTurnOutcome o, int k,
                                                   int hpStart, int shieldPool, out int plusNext)
        {
            var ns = plan.next;
            ns.enemyHp = st.enemyHp - System.Math.Min(o.effDmg, st.enemyHp) + o.enemyHeal;   // [#107] 〈吸血〉の戻り (撃破しないときだけ・最大 HP まで)
            // [#107] 収支プラスなら次のターン〈反撃態勢〉(通った時 +N)・〈暴走〉(攻撃 +N)
            if (o.balancePlus)
            {
                ns.enemyHitBonus += st.nextHitOnPlus;
                if (st.nextAtkOnPlus > 0f)
                {
                    ns.enemyAtk += Mathf.RoundToInt(st.nextAtkOnPlus);
                    ns.tele.enemyAttackValue = ns.enemyAtk;
                }
            }
            ns.myHp = hpStart - o.taken;
            // ボスのダウン (2026-11-13): この手で次のターンがダウンになるか (本体と同じ関数)。 なれば次のターンの敵の攻撃は 0・受けるダメージ ×1.5。
            //   ダウンが明けた後の攻撃は downAfterAtk (新しい規則: チャージか大技に重なれば周期を頭に戻して数える・旧い規則: 今の見込み downNextAtk のまま)。
            //   ダウンのターンの次 (このターンがダウン) は plan.next の攻撃 (本体がこのターンの頭に起点を動かした後の値) になる。
            if (st.downNotches != null && GameLoop.GameHooks.DownBreak != CombatSystem.DownBreakRule.AnyDamage)
            {
                // 新しい判定 (2026-11-14): 主攻撃だけで割れる・通常攻撃以外の削りはひび (猶予の後によろめき)。 本体と同じ規則 (BossDown.ForecastTurn)
                var f = DownForecast(in st, st.enemyHp, o.mainDmg, System.Math.Min(o.effDmg, st.enemyHp) - o.enemyHeal, wantCracks: true);
                bool landed = f.nextDown;   // 次のターンの頭で待ちのダウンが 1 つ使われる
                bool nd = landed, weakNext = f.nextWeak;
                int added = f.fullAdded + f.crackAdded;
                ns.downBroken = f.brokenAfter;
                ns.downCrackTurns = f.cracksAfter;
                ns.downTurn = st.downTurn + 1;
                var botRule = BotDownRule;
                if (botRule == CombatSystem.DownCancelRule.Timed)
                {
                    // [2026-11-26] まとめて割っても 1 つのダウン。 種類は来るターンで決まる (本体と同じ関数)・溜めで無効なら敵は普段どおり
                    if (added > 0) added = 1;
                    if (landed)
                    {
                        var kind = CombatSystem.CombatManager.DownKindAt(CombatSystem.CombatManager.Instance?.CurrentEnemy, ns.downTurn,
                                                                         (float)System.Math.Max(0, ns.enemyHp) / System.Math.Max(1, st.downMaxHp), f.nextWeak, false, botRule);
                        if (kind == CombatSystem.DownKind.Resisted) nd = false;
                        else weakNext = kind == CombatSystem.DownKind.Stagger;
                    }
                }
                else if (botRule == CombatSystem.DownCancelRule.Instant)
                {
                    // [2026-11-26] 割った瞬間に効く規則: 通常攻撃で割った分は、 大技を止めた時だけ次のターンが大ダウン (それ以外はこのターンで済んでいる)。 ひびは次のターンによろめき (山場・チャージなら無効)
                    var enemyI = CombatSystem.CombatManager.Instance?.CurrentEnemy;
                    float ratioI = (float)System.Math.Max(0, ns.enemyHp) / System.Math.Max(1, st.downMaxHp);
                    bool bigNow = st.downInstant && !st.enemyDown && f.fullAdded > 0 && st.downBreakKindNow == (int)CombatSystem.DownKind.Big;
                    bool crack = f.crackAdded > 0;
                    added = bigNow || crack ? 1 : 0;
                    landed = st.downPendingNow > 0 || added > 0;
                    nd = landed; weakNext = !bigNow && st.downPendingNow == 0 && crack;
                    if (nd && weakNext
                        && CombatSystem.CombatManager.DownKindAt(enemyI, ns.downTurn, ratioI, true, false, botRule) == CombatSystem.DownKind.Resisted) nd = false;
                    if (enemyI != null) ns.downBreakKindNow = (int)CombatSystem.CombatManager.InstantBreakKindAt(enemyI, ns.downTurn, ratioI);
                }
                ns.enemyDown = nd; ns.enemyDownWeak = nd && weakNext;
                ns.downPendingNow = System.Math.Max(0, st.downPendingNow + added - (landed ? 1 : 0));
                // 次のターンの後に残るダウンの先頭の種類 (近似: 今残っている物が先・今のターンに割れた物は 通常攻撃 → ひび の順)
                ns.downPendingHeadWeak = ns.downPendingNow > 0
                    && (st.downPendingNow > 1 ? st.downPendingHeadWeak
                        : (st.downPendingNow == 1 ? f.fullAdded == 0 : f.fullAdded <= 1) && f.crackAdded > 0 && GameLoop.GameHooks.DownCrackWeak);
                ns.downNextAtk = nd ? st.downAfterAtk : st.downNextAtk;
                if (nd) { ns.enemyAtk = 0; ns.tele.enemyAttackValue = 0; ns.atkGrowth = null; }
            }
            else if (st.downGaugeMax > 0)
            {
                // [2026-11-26] 混乱ゲージ: 配線の時点で満ちる。 大技のターンに満たした時だけ次のターンが大ダウン (よろめき・無効はこのターンで済む)。 大ダウンのターンは溜まらない
                var enemyG = CombatSystem.CombatManager.Instance?.CurrentEnemy;
                bool full = !st.enemyDown && o.effDmg < st.enemyHp && st.downGauge + o.gaugePips >= st.downGaugeMax;
                bool bigNow = full && st.downInstant && st.downBreakKindNow == (int)CombatSystem.DownKind.Big;
                ns.downGauge = st.enemyDown ? st.downGauge : full ? 0 : st.downGauge + o.gaugePips;
                ns.downGaugeMax = full ? CombatSystem.BossDown.GrownMax(st.downGaugeMax, st.downGaugeGrowth) : st.downGaugeMax;
                ns.downTurn = st.downTurn + 1;
                bool nd = bigNow || st.downPendingNow > 0;
                ns.downPendingNow = 0;
                ns.enemyDown = nd; ns.enemyDownWeak = false;
                if (enemyG != null)
                {
                    float ratioG = (float)System.Math.Max(0, ns.enemyHp) / System.Math.Max(1, st.downMaxHp);
                    ns.downBreakKindNow = (int)CombatSystem.CombatManager.InstantBreakKindAt(enemyG, ns.downTurn, ratioG);
                    ns.downToHeavy = CombatSystem.Escalation.TurnsToHeavy(enemyG, ns.downTurn);
                }
                ns.downNextAtk = nd ? st.downAfterAtk : st.downNextAtk;
                if (nd) { ns.enemyAtk = 0; ns.tele.enemyAttackValue = 0; ns.atkGrowth = null; }
            }
            else if (st.downNotches != null)
            {
                // 旧い判定 (AnyDamage・2e76c46 の式のまま)
                int hpAfter = System.Math.Max(0, ns.enemyHp);
                int nb = CombatSystem.BossDown.BrokenCount(st.downNotches, st.downMaxHp, hpAfter, st.downBroken);
                bool nd = CombatSystem.BossDown.NextTurnIsDown(st.downNotches, st.downMaxHp, st.downBroken, st.downPendingNow, hpAfter);
                ns.downBroken = nb;
                ns.downPendingNow = System.Math.Max(0, st.downPendingNow + (nb - st.downBroken) - (nd ? 1 : 0));
                ns.enemyDown = nd;
                ns.downNextAtk = nd ? st.downAfterAtk : st.downNextAtk;   // [2026-11-14] 明けた後の見込み (旧い規則では downAfterAtk = downNextAtk で今までと同じ)
                if (nd) { ns.enemyAtk = 0; ns.tele.enemyAttackValue = 0; ns.atkGrowth = null; }
            }
            // シールド: 今ターンの被弾で吸われ、 〈城壁〉で増え、 次のターン頭に半分 (切り捨て・DAMAGE_CALC_REFERENCE ③-D)
            int shield = System.Math.Max(0, shieldPool - o.bashUsed - o.taken);
            if (st.rung.bulwarkRatePct > 0) shield += TurnRules.BulwarkGain(o.block, st.enemyAtk, st.rung.bulwarkRatePct, o.justGuard, st.rung.bulwarkJust);
            ns.myShield = shield / 2;
            // 剣〈不滅の刃〉: 攻撃とブロックの両方に挿したターンが続く
            ns.undyingStreak = (st.rung.undying && o.aCount > 0 && o.bCount > 0) ? st.undyingStreak + 1 : 0;
            // 盾〈堅守の構え〉: 防御の端子に 2 個以上で使うと次の 2 ターンは使えない
            int readyTurn = (plan.steadfastOn && st.steadfastReady && o.bCount >= ItemSystem.PassiveSkills.Effects.SteadfastStance.NeedBlockDice)
                ? plan.turn + ItemSystem.PassiveSkills.Effects.SteadfastStance.Cooldown : plan.steadfastReadyTurn;
            ns.steadfastReady = plan.steadfastOn && plan.turn + 1 >= readyTurn;
            // 敵の防御: 持続分はダメージを入れたターンの終わりに割れる/積み上がる・構えの増減は次のターン番号で決まる
            ns.eDefNow = System.Math.Max(0, plan.armor.DefAt(plan.armor.EndOfTurn(plan.baseDef, o.effDmg > 0), plan.turn + 1));
            // 烈炎: このターンの終わりに積もる分 (効用と同じ見込み・2026-11-14〜 シールドで受けた量 ÷ N / 旧い条件 Guard2 はブロック端子 2 本以上で +1)
            ns.tele.blazeStacks = plan.nextBlazeBase + o.blazeGain;

            // memo の鍵の最後の要素: 烈炎のスタック × 2 + [#107] 次のターンに〈反撃態勢〉〈暴走〉が乗るか
            plusNext = (o.balancePlus && (st.nextHitOnPlus > 0 || st.nextAtkOnPlus > 0f)) ? 1 : 0;
            return ns;
        }


        /// <summary>出目 <paramref name="rolls"/> の全配線 (今ターンの列挙と同じ制約) の最大効用。 封印は次ターン分が未知なので考えない。</summary>
        private float BestUtilityOver(in OptimalTurnState ns, int[] rolls, CombatSystem.DiceTerminal[] assign, NextTurnPlan plan)
        {
            int k = rolls.Length;
            int termKinds = plan.termKinds;
            int total = 1; for (int i = 0; i < k; i++) total *= termKinds;
            float best = float.NegativeInfinity;
            for (int code = 0; code < total; code++)
            {
                int t = code;
                for (int i = 0; i < k; i++)
                {
                    assign[i] = (CombatSystem.DiceTerminal)(t % termKinds); t /= termKinds;
                }
                bool dup = false;
                for (int i = 1; i < k && !dup; i++)
                    if (rolls[i] == rolls[i - 1] && (int)assign[i] < (int)assign[i - 1]) dup = true;
                if (dup) continue;
                float u = OptimalUtility(in ns, rolls, assign);
                if (u > best) best = u;
            }
            return best;
        }

        /// <summary>端子の重み (ペルソナ・ボス別プレイブック・予兆ターン込み)。 MutualWiringPolicy の本体と先読み (次ターンの重み) が同じ式を呼ぶ。
        /// <paramref name="bossHpRatio"/> = 敵の現在 HP / 最大 HP (Phase 判定に必要)。</summary>
        private void TerminalWeights(in BossPlaybook play, int turn, float bossHpRatio, out float wAtk, out float wDef, out float wChg)
        {
            // ペルソナで端子重みを調整 (2026-07-15): 該当ビルドが配線を意識して立ち回る
            wAtk = 1.0f; wDef = 1.0f; wChg = 0.6f;
            switch (_currentPersona)
            {
                case BuildPersona.Charge:    wChg = 2.5f; break;                      // 充電優先
                case BuildPersona.Shield:    wDef = 1.6f; wChg = 0.9f; break;         // 守り厚く
                case BuildPersona.Berserk:   wAtk = 1.4f; wDef = 0.7f; break;         // 攻めっ気
                case BuildPersona.Bludgeon:  wAtk = 1.3f; break;                      // 火力寄せ
                case BuildPersona.Standard:  wAtk = 1.2f; break;                      // 若干攻め
                case BuildPersona.Bleed:     wAtk = 1.15f; break;                     // 攻撃回数で積む
                case BuildPersona.Poison:    wAtk = 1.1f; break;                      // DoT積みつつ攻める
                case BuildPersona.Rinkai:    wAtk = 1.35f; break;                     // メーター爆発型 = 攻撃全振り
                case BuildPersona.Crit:      wAtk = 1.15f; break;
            }
            // ボス別プレイブック (2026-07-16): 該当ボスなら端子重みを追加調整
            //   予兆Tは defBias×omenDefBoost, atkBias×omenAtkDamp を上乗せ
            //   damageCapped ボス (6F 灰塵の外殻/5F ChipCap) は攻撃玉あたりの実効ダメを wiring 探索側でクランプ
            //   2026-07-16 追加: ペルソナと Playbook の合成で二重ブレーキ/二重ゲタを緩和
            //     耐久ペルソナ (Shield/Charge) は既に defBias 高 → Playbook の omenDefBoost を弱める
            //     火力ペルソナ (Standard/Bludgeon/Crit/Berserk) は Playbook の omenAtkDamp を弱める (削り速度維持)
            wAtk *= play.atkBias;
            wDef *= play.defBias;
            wChg *= play.chgBias;
            if (play.IsOmenTurn(turn, bossHpRatio))
            {
                float atkDamp = play.omenAtkDamp;
                float defBoost = play.omenDefBoost;
                switch (_currentPersona)
                {
                    case BuildPersona.Shield:
                    case BuildPersona.Charge:
                        // 既に守り厚い → 予兆Tの追加防御ゲタを緩和 (1.8 → ~1.3)
                        defBoost = 1f + (defBoost - 1f) * 0.4f;
                        break;
                    case BuildPersona.Standard:
                    case BuildPersona.Bludgeon:
                    case BuildPersona.Crit:
                    case BuildPersona.Berserk:
                        // 火力軸は削り速度が生命線 → 予兆Tの攻撃減衰を緩和 (0.7 → ~0.85)
                        atkDamp = 1f - (1f - atkDamp) * 0.5f;
                        break;
                }
                wAtk *= atkDamp;
                wDef *= defBoost;
            }
        }

        /// <summary>防ぎ切りの規則 (2026-11-05): 撃破まで n ターン戦う間の、 敵の攻撃の伸び (<paramref name="growT0"/> ターン比の最大)。
        /// エスカレーション・大技・構え・先手の周期だけを見る (どれも予告/説明に出る公開規則)。 先の出目は見ない。</summary>
        private float[] BuildAtkGrowth(EnemyData growEnemy, ItemSystem.PassiveSkills.CombatContext ctxRoles, int growT0)
        {
            var growArmor = EnemyArmorModel.From(growEnemy, ctxRoles, out _);
            string growProf = growEnemy.EffectiveEscalationProfile;
            int growDelay = (int)(ctxRoles?.GetAccumulated(CombatSystem.CombatManager.CoolantDelayKey) ?? 0f);
            float GrowMul(int t) => CombatSystem.Escalation.GetMultiplier(growProf, System.Math.Max(1, t - growDelay))
                                    * growArmor.AttackFactor(t);
            float growM0 = System.Math.Max(1e-3f, GrowMul(growT0));
            var atkGrowth = new float[HorizonCapTurns + 1];
            float growMax = 1f;
            atkGrowth[0] = 1f;
            for (int j = 1; j <= HorizonCapTurns; j++)
            {
                growMax = System.Math.Max(growMax, GrowMul(growT0 + j - 1) / growM0);
                atkGrowth[j] = growMax;
            }
            return atkGrowth;
        }

        /// <summary>配線の効用の入力 (<see cref="OptimalTurnState"/>) を、 今の盤面から 1 回だけ読んで組む (読むだけ・書き換えない)。
        /// 端子の山札 (AutoRunner.Terminals) が使う。</summary>
        private OptimalTurnState BuildOptimalTurnState(int[] rolls, CombatSystem.MutualTurnTelegraph tele)
        {
            int k = rolls.Length;
            var cm = CombatManager.Instance;
            int enemyHp = cm?.EnemyHP ?? 1;
            int myHp = cm?.PlayerHP ?? 1;
            int myMaxHp = cm?.PlayerMaxHP ?? System.Math.Max(1, myHp);
            // 麻痺毒の小瓶: このターンの攻撃力減算 (0 で床)。 予告済みなので見積もりに含める。
            int atkBase = System.Math.Max(0, (cm?.PlayerAttackPower ?? 2) - tele.playerAttackPenalty);
            int enemyAtk = tele.enemyAttackValue;

            // 短期決戦の視野 (2026-08-04)。 「この配分を撃破まで続けたら累積でいくら食らうか」を
            // 効用に入れるための係数。 HorizonCapTurns は effDmg=0 のときの発散止めと、
            // 「遠すぎる先は見ない」の兼用。
            // 1 ターン引き延ばすことのコスト。 既存項の桁 (wAtk×effDmg が ~25〜100、
            // wDef×min(block,enemyAtk) が ~3〜40) に対し、 ttk が 1 増える不利がブロック 1 本の
            // 利得を上回るように置いた初期値。

            var runNow = GameLoop.GameManager.Instance?.Run;

            var play = BossPlaybook.From(cm?.CurrentEnemy);
            // ボス側 HP 比 (Phase 判定に必要): CombatManager から取得 (敵ID・敵HP・敵MaxHP)
            float bossHpRatio = 1f;
            if (cm != null && cm.CurrentEnemy != null && cm.CurrentEnemy.maxHP > 0)
                bossHpRatio = (float)cm.EnemyHP / cm.CurrentEnemy.maxHP;
            // 端子の重み (ペルソナ・ボス別プレイブック・予兆ターン): TerminalWeights に切り出し済み (先読みも同じ式を呼ぶ)
            TerminalWeights(in play, tele.turn, bossHpRatio, out float wAtk, out float wDef, out float wChg);
            // ===== 割り当ての列挙 =====
            //   端子は 攻撃/ブロック/充電 の全列挙 (3^k)。 ADR-0011 でダイスは 3 個になったので 27 通り。
            //   役は配線に依存せず振った出目だけで決まるので、 配線の効用には役を入れない。
            var ctxRoles = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;

            // 会心倍率は「束(攻)＝会心確定」「奇(攻)＝会心率+25%」の価値見積もりに要る。
            float critMul = ctxRoles?.criticalMultiplier ?? 3f;

            // ===== 敵の防御と、 配線で条件が決まる武器の段 (2026-11-04) =====
            //   敵の防御: 予告に出ているこのターンの値 (構えの上乗せ・隙の 0・割れる/積み上がる装甲を反映済み)。
            //   以前は与ダメから防御を引いておらず、 装甲持ちと構えのターンで火力を大きく見誤っていた。
            //   武器の段 (§9-4b): 剣は攻撃とブロックの両方に挿すと伸び、 盾は全部守る・全部攻めるへ振り切ると伸びる。
            //   評価に無いと BOT はこの段を一度も狙わず、 家系の強さではなく方策の盲点を測ることになる。
            int eDefNow = System.Math.Max(0, tele.enemyDefense);
            int myPen = System.Math.Max(0, ctxRoles?.playerArmorPen ?? 0);
            var activeIds = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.ActivePlayerSkillIds;
            var rung = WeaponRungModel.From(activeIds);      // 段の検出と率は WeaponRungModel (Super と共通・2026-11-05)
            int undyingStreak = (int)(ctxRoles?.GetAccumulated(ItemSystem.PassiveSkills.Effects.UndyingBlade.StreakKey) ?? 0f);
            bool steadfastReady = rung.steadfast && ctxRoles != null
                && ctxRoles.currentTurn >= (int)ctxRoles.GetAccumulated(ItemSystem.PassiveSkills.Effects.SteadfastStance.ReadyKey);
            int myShield = System.Math.Max(0, ctxRoles?.consShield ?? 0);
            int enemyMaxHpNow = System.Math.Max(1, cm?.CurrentEnemy?.maxHP ?? 1);

            // 防ぎ切りの規則 (2026-11-05): 撃破まで n ターン戦う間の、 敵の攻撃の伸び (このターン比の最大)。
            //   エスカレーション・大技・構え・先手の周期だけを見る (どれも予告/説明に出る公開規則)。 先の出目は見ない。
            float[] atkGrowth = null;
            if (FreeBlockRule && EffectiveWiringSkill != WiringSkill.Naive && enemyAtk > 0 && cm?.CurrentEnemy != null)
                atkGrowth = BuildAtkGrowth(cm.CurrentEnemy, ctxRoles, tele.turn);

            // 効用の入力 (盤面の値) を 1 つにまとめる (SuperV3・2026-11-05)。 効用そのものは OptimalUtility (state + 出目 + 配線 → 効用)。
            var st = new OptimalTurnState
            {
                tele = tele, enemyHp = enemyHp, myHp = myHp, myMaxHp = myMaxHp, atkBase = atkBase, enemyAtk = enemyAtk,
                wAtk = wAtk, wDef = wDef, wChg = wChg, critMul = critMul,
                eDefNow = eDefNow, myPen = myPen, rung = rung,
                undyingStreak = undyingStreak,
                steadfastReady = steadfastReady, myShield = myShield,
                solo = ctxRoles != null && !ctxRoles.isPairEncounter,
                execBoost = CombatSystem.CombatRules.ExecutionBoostOn(enemyHp, enemyMaxHpNow),
                shattered = ItemSystem.PassiveSkills.Effects.ArmorBreak.Shattered(ctxRoles),
                dieMaxFace = ctxRoles != null ? ItemSystem.PassiveSkills.Effects.GaryoTensei.MaxFace(ctxRoles) : 0,
                enemyMaxHpNow = enemyMaxHpNow,
                play = play, atkGrowth = atkGrowth,
                diceAtkPct = ctxRoles?.attackDicePct ?? 0f,
            };
            // ボスのダウン (2026-11-13): 予告で開示された目盛りとダウン。 次のターンの攻撃は公開規則の見込み (大技の周期は本体と同じ起点 Escalation.HeavyCycleOffset)
            st.enemyDown = tele.enemyDown;
            if ((tele.downNotches != null || tele.downGaugeMax > 0) && cm?.CurrentEnemy != null && ctxRoles != null)
            {
                // [2026-11-26] 混乱ゲージ: 今の値と満タン・大技までのターン・大技の見込み・1 ターンに入れられる量 (止まった面の平均 × 個数)
                st.downGauge = tele.downGauge; st.downGaugeMax = tele.downGaugeMax; st.downGaugeGrowth = tele.downGaugeGrowth;
                st.downToHeavy = CombatSystem.Escalation.TurnsToHeavy(cm.CurrentEnemy, tele.turn);
                st.downHeavyAtk = st.downToHeavy > 0 ? ExpectedEnemyAttackAt(cm.CurrentEnemy, ctxRoles, in tele, tele.turn + st.downToHeavy, 0) : 0;
                {
                    var fcs = ctxRoles.equippedDiceFaces; float mf = 3.5f;
                    if (fcs != null && fcs.Length > 0) { float sf = 0f; foreach (int f in fcs) sf += f; mf = sf / fcs.Length; }
                    st.downGaugePerTurn = mf * k;
                }
                st.downNotches = tele.downNotches; st.downBroken = tele.downBroken; st.downPendingNow = tele.downPending;
                st.enemyDownWeak = tele.enemyDownWeak; st.downPendingHeadWeak = tele.downPendingHeadWeak;
                st.downCrackTurns = tele.downCrackTurns; st.downTurn = tele.turn;
                st.downChip = GameLoop.GameHooks.DownBreak == CombatSystem.DownBreakRule.AnyDamage ? 0 : DownChipEstimate(ctxRoles);
                st.downMaxHp = System.Math.Max(1, cm.CurrentEnemy.maxHP);
                st.downNextAtk = ExpectedEnemyAttackAt(cm.CurrentEnemy, ctxRoles, in tele, tele.turn + 1, 0);
                st.downEraseAtk = st.downNextAtk; st.downAfterAtk = st.downNextAtk;
                var botRule = BotDownRule;
                float hpRatio = (float)cm.EnemyHP / st.downMaxHp;
                if (botRule == CombatSystem.DownCancelRule.Instant)
                {
                    // [2026-11-26] 割った瞬間に効く規則: このターンに割ったら何が起きるか (本体と同じ関数) と、 割って捨てる機会の見込み
                    st.downInstant = true;
                    var kNow = CombatSystem.CombatManager.InstantBreakKindAt(cm.CurrentEnemy, tele.turn, hpRatio);
                    st.downBreakKindNow = (int)kNow;
                    if (kNow == CombatSystem.DownKind.Resisted)
                        st.downHeavyNextAtk = ExpectedEnemyAttackAt(cm.CurrentEnemy, ctxRoles, in tele, tele.turn + 1, 0);
                    else if (kNow == CombatSystem.DownKind.Stagger)
                    {
                        int toH = CombatSystem.Escalation.TurnsToHeavy(cm.CurrentEnemy, tele.turn);
                        if (toH >= 2 && toH <= 3)
                            st.downHoldCost = System.Math.Max(0, ExpectedEnemyAttackAt(cm.CurrentEnemy, ctxRoles, in tele, tele.turn + toH, 0) - st.enemyAtk);
                    }
                }
                var nextKind = botRule == CombatSystem.DownCancelRule.Timed
                    ? CombatSystem.CombatManager.DownKindAt(cm.CurrentEnemy, tele.turn + 1, hpRatio, false, false, botRule)
                    : CombatSystem.DownKind.Big;
                if (botRule == CombatSystem.DownCancelRule.Timed
                    && (nextKind == CombatSystem.DownKind.Resisted || (CombatSystem.BossDown.DownStaggerNoStun && nextKind == CombatSystem.DownKind.Stagger)))
                {
                    // [2026-11-26] 今割ると次のターンで無効・目盛りが消える: 次の山場を止める機会を捨てる損
                    st.downNextResisted = true;
                    int oppTurn = tele.turn + 2;
                    for (int dt = 2; dt <= 5; dt++)
                    {
                        if (CombatSystem.CombatManager.DownKindAt(cm.CurrentEnemy, tele.turn + dt, hpRatio, false, false, botRule) == CombatSystem.DownKind.Big)
                        { oppTurn = tele.turn + dt; break; }
                    }
                    st.downEraseAtk = -ExpectedEnemyAttackAt(cm.CurrentEnemy, ctxRoles, in tele, oppTurn, 0);
                }
                else
                {
                    if (botRule != CombatSystem.DownCancelRule.Always)
                        DownSequenceAfterNextTurn(cm.CurrentEnemy, ctxRoles, in tele, st.downNextAtk, hpRatio, botRule,
                                                  out st.downEraseAtk, out st.downAfterAtk);
                    if (botRule == CombatSystem.DownCancelRule.Timed && downStaggerPenaltyShare > 0f
                        && CombatSystem.CombatManager.DownKindAt(cm.CurrentEnemy, tele.turn + 1, hpRatio, false, false, botRule) == CombatSystem.DownKind.Stagger)
                    {
                        for (int dt = 3; dt <= 4; dt++)
                        {
                            if (CombatSystem.CombatManager.DownKindAt(cm.CurrentEnemy, tele.turn + dt, hpRatio, false, false, botRule) == CombatSystem.DownKind.Big)
                            {
                                st.downStaggerOppAtk = ExpectedEnemyAttackAt(cm.CurrentEnemy, ctxRoles, in tele, tele.turn + dt, 0);
                                break;
                            }
                        }
                    }
                }
            }
            // 〈烈炎〉の積もる量 (2026-11-14): 主攻撃をシールドで N 受けるごとに +1。 N は本体と同じ値
            if (cm?.CurrentEnemy != null && tele.blazePenalizesBlock)
                st.blazePerShield = CombatSystem.BossParams.ParamInt(cm.CurrentEnemy.id, CombatSystem.BossParam.BlazePerShield);
            // 灰燼の王の大技〈業火〉(2026-11-14): 烈炎は業火で返る。 1 つあたりと周期は本体と同じ値
            if (cm?.CurrentEnemy != null && tele.blazePenalizesBlock && !GameLoop.GameHooks.AshKingHeavyDisabled && cm.CurrentEnemy.heavyPeriod > 0)
            {
                st.blazeBurst = true;
                st.blazeBurstPer = CombatSystem.BossParams.ParamInt(cm.CurrentEnemy.id, CombatSystem.BossParam.BlazeBurstPerStack);
                st.heavyPeriodNow = CombatSystem.Escalation.HeavyPeriodNow(cm.CurrentEnemy);
            }
            // 大技のチャージ (2026-11-14): 予告は 0。 帳簿の先の被ダメは次のターン (大技) の見込みで伸ばす (本体の式)
            st.charging = tele.enemyCharging;
            if (st.charging && cm?.CurrentEnemy != null && ctxRoles != null)
                st.chargeNextAtk = ExpectedEnemyAttackAt(cm.CurrentEnemy, ctxRoles, in tele, tele.turn + 1, 0);
            // 敵のパッシブ (2026-11-12・#104 で効くようにした物のうち、 被ダメを読み違える物)
            if (cm?.CurrentEnemy?.passiveSkills != null)
                foreach (var ps in cm.CurrentEnemy.passiveSkills)
                {
                    if (ps == null) continue;
                    if (ps.internalName == "BruteForce" || ps.internalName == "EliteDarkKnight") st.enemyHitBonus += 2;
                    else if (ps.internalName == "HoningDuel") st.enemyHitBonus += System.Math.Max(0, tele.turn) / 3;
                }
            if (ctxRoles != null && ctxRoles.GetAccumulated(ItemSystem.PassiveSkills.Effects.MutualKeys.EnemyDoubleOnBlockHeavy) > 0f)
                st.enemyDoubleOnBlockHeavy = true;
            // [#107 2026-11-12] 収支で決まる敵のパッシブと〈吸血〉。 2 体戦は 1 体目 (CurrentEnemy) だけを見る (上の見本と同じ)。
            //   敵のパッシブが封じられているターン (静寂のローブ等・PSM.FireEnemyTrigger が鳴らさない) は読まない。
            //   読むのは Super だけ (V3Flags.readEnemyPassives・Optimal で読ませると悪化した)。 内訳は enemyPassiveMask。
            if (ReadEnemyPassivesEffective && cm?.CurrentEnemy?.passiveSkills != null && ctxRoles != null && ctxRoles.enemyPassivesDisabledTurns <= 0)
            {
                int mask = v3.enemyPassiveMask;
                bool mLife = (mask & 1) != 0, mNext = (mask & 2) != 0, mChip = (mask & 4) != 0;
                int rampage = 0;
                foreach (var ps in cm.CurrentEnemy.passiveSkills)
                {
                    if (ps == null) continue;
                    switch (ps.internalName)
                    {
                        case "TailStrike":    if (mChip) st.enemyChipOnPlus  += ItemSystem.PassiveSkills.Effects.TailStrike.Damage; break;
                        case "EliteLizard":   if (mChip) st.enemyChipOnPlus  += ItemSystem.PassiveSkills.Effects.EliteLizard.Reflect; break;
                        case "Hellfire":      if (mChip) st.enemyChipOnMinus += ItemSystem.PassiveSkills.Effects.Hellfire.Damage; break;
                        case "Lifesteal":     if (mLife) st.enemyLifesteal++; break;
                        case "CounterStance": if (mNext) st.nextHitOnPlus    += ItemSystem.PassiveSkills.Effects.CounterStance.Bonus; break;
                        case "Rampage":       if (mNext) rampage           += ItemSystem.PassiveSkills.Effects.Rampage.Bonus; break;
                    }
                }
                // 前のターンに積まれた〈反撃態勢〉: このターン、 敵の攻撃が通った時に足される (〈剛力〉と同じ位置)
                if (mNext) st.counterPendingNow = System.Math.Max(0, (int)ctxRoles.GetBuff(CounterStanceNextTurnKey));
                st.enemyHitBonus += st.counterPendingNow;
                // 〈暴走〉: 次のターンの敵の出目の合計 +N → 次のターンのエスカレーション・大技・プレイヤーの防御を通した増分
                if (rampage > 0)
                {
                    int nt = tele.turn + 1;
                    st.nextAtkOnPlus = System.Math.Max(0,
                        ExpectedEnemyAttackAt(cm.CurrentEnemy, ctxRoles, in tele, nt, rampage)
                      - ExpectedEnemyAttackAt(cm.CurrentEnemy, ctxRoles, in tele, nt, 0));
                }
                st.enemyHealCut = ctxRoles.enemyHealReductionPct;
                st.shieldAbsorbsChip = ItemSystem.PassiveSkills.CombatContext.ShieldAbsorbsUnmitigable;
                st.enemyBalanceRead = st.enemyChipOnPlus > 0 || st.enemyChipOnMinus > 0 || st.enemyLifesteal > 0
                                   || st.nextHitOnPlus > 0 || st.nextAtkOnPlus > 0f;
            }
            // [V3 readOutgoingMult] 主攻撃の見積もりに与ダメの倍率を読む (Super で既定 ON)。 予測の照合 (predTrace) では両方を出す。
            bool readOutNow = ReadOutgoingMultEffective && EffectiveWiringSkill != WiringSkill.Naive;
            if ((readOutNow || predTrace) && ctxRoles != null)
            {
                st.readOut = readOutNow; st.traceBoth = predTrace;
                FillReadOut(ref st, cm, ctxRoles, runNow, tele.playerAttackPenalty);
            }
            // [V3 mainCorr] 主攻撃の見積もりに実績の比を掛ける。 下の dFull (HP の帳簿の撃破までの見込み) の計算より前に入れる
            if (v3.mainCorr && EffectiveWiringSkill != WiringSkill.Naive) st.mainCorr = _mcRatioNow;
            // [V3 hpLedger] 先のターンの 1 ターンの与ダメの見込み: 今の手を全部攻撃に挿した時の値を、 面の平均で均す
            if (v3.hpLedger && EffectiveWiringSkill != WiringSkill.Naive)
            {
                var allAtk = new CombatSystem.DiceTerminal[k];
                for (int i = 0; i < k; i++) allAtk[i] = CombatSystem.DiceTerminal.Attack;
                OptimalUtility(in st, rolls, allAtk, out var oa);
                var fc = ctxRoles?.equippedDiceFaces;
                float avgFace = 3.5f;
                if (fc != null && fc.Length > 0) { float sf = 0f; foreach (int f in fc) sf += f; avgFace = sf / fc.Length; }
                int sumR = 0; for (int i = 0; i < k; i++) sumR += rolls[i];
                float expected = System.Math.Max(0, atkBase) + k * avgFace;
                st.dFull = System.Math.Max(1f, oa.effDmg * expected / System.Math.Max(1f, System.Math.Max(0, atkBase) + sumR));
                st.ledger = true;
                if (st.atkGrowth == null && enemyAtk > 0 && cm?.CurrentEnemy != null) st.atkGrowth = BuildAtkGrowth(cm.CurrentEnemy, ctxRoles, tele.turn);
                // (ヴェスカ〈停滞する時間〉の帳簿の項 stagCost は 2026-11-12 #103 で撤去 ── 配線では何も積まなくなった)
            }
            return st;
        }

        /// <summary>直近に選んだ配線の効用 (山札の方策が書く)。 読むだけ・判断には使わない (SuperV3 の実験用)。</summary>
        private float _lastBestU;
        /// <summary>[計装 2026-11-10] 直近に選んだ配線が防ぎ切りの規則の対象か (今ターン撃破は除く) / 今ターン撃破か。</summary>
        private bool _lastFreeBlock, _lastKillsNow;
        /// <summary>[計装] この戦闘で実際に配線を決めたターン数 / うち防ぎ切りの規則の対象 / うち今ターン撃破。</summary>
        private int _fbDecided, _fbTurns, _fbKillTurns;
        /// <summary>[V3 fbCheckUnavoid] この戦闘で予告の外のダメージを見たか / 前のターンの HP / 前のターンに見込んだ被ダメ / 直近に選んだ配線の見込み被ダメ。</summary>
        private bool _fbUnavoidSeen;
        private int _fbPrevHp = -1, _fbPrevExpTaken, _lastExpTaken;

        /// <summary>手 <paramref name="rolls"/> を最良に配線したときの効用 (Optimal の配線評価そのもの)。</summary>
        private float BestUtilityOf(int[] rolls, CombatSystem.MutualTurnTelegraph tele)
        {
            // 端子の山札: 手札の端子への挿し方の最良の効用 (リロールの値付けも山札の効用で見る)
            ChooseDeckWiring(rolls, tele, out float du, out _);
            return du;
        }

        /// <summary>全部振り直したあとの手の、 最良効用の期待値。 面の出方を<b>全部数える</b> (3 個 × 6 面 = 216 通り・同じ多重集合は 1 回だけ評価)。</summary>
        private float ExpectedBestUtilityAfterReroll(int k, int[] faces, CombatSystem.MutualTurnTelegraph tele)
        {
            int fc = faces.Length;
            long total = 1; for (int i = 0; i < k; i++) total *= fc;
            var memo = new Dictionary<long, float>(64);
            var buf = new int[k]; var sorted = new int[k];
            double acc = 0;
            for (long code = 0; code < total; code++)
            {
                long t = code;
                for (int j = 0; j < k; j++) { buf[j] = faces[(int)(t % fc)]; t /= fc; sorted[j] = buf[j]; }
                System.Array.Sort(sorted);
                long key = 0; for (int j = 0; j < k; j++) key = key * 32 + sorted[j];
                if (!memo.TryGetValue(key, out float u)) { u = BestUtilityOf(buf, tele); memo[key] = u; }
                acc += u;
            }
            return (float)(acc / total);
        }

        /// <summary>リロール方策（ADR-0011）。 全部振り直し。
        ///
        /// 振り直しは手札 3 個をまとめて引き直す賭け。 今の手を捨てるので、 **役が成立した手は持ったまま戦う**。
        /// 判断の順:
        ///   ① 役 (2 個そろい / ゾロ目) が成立している → 振らない
        ///   ② 振る合計が、 面平均 × 個数 を下回る → 振る
        ///   ③ それ以外 → 振らない
        ///
        /// 費用は `振り直す個数 × この戦闘で何回目か`（attempt が戦闘の通し番号）+ 時限デバフ〈無力感〉の +1。 本体と同じ
        /// <see cref="CombatSystem.CombatManager.RerollCostNow"/> を読む (2026-11-16)。 **充電を使い切らない** ──
        /// 充電は端子へ挿さなかったダイスの対価なので、 全部リロールへ流すと配線の判断そのものが消える。</summary>
        private int[] MutualRerollPolicy(int[] dice, CombatSystem.MutualTurnTelegraph tele, int attempt)
        {
            using (BotTiming.Enter(BotTiming.Cat.Reroll)) return MutualRerollPolicyCore(dice, tele, attempt);
        }

        private int[] MutualRerollPolicyCore(int[] dice, CombatSystem.MutualTurnTelegraph tele, int attempt)
        {
            if (dice == null || dice.Length == 0) return null;
            RefreshMainCorr();   // [V3 mainCorr] リロールの効用 (配線の評価) も同じ比で見る
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx == null) return null;
            bool heavyNow = v3.superDefense && IsHeavyAttack(dice, tele.enemyAttackValue + tele.secondaryAttackValue,
                                                             System.Math.Max(1, CombatManager.Instance?.PlayerMaxHP ?? 1));
            // [2026-11-26] 大事なターン (大ダウン・今割れば大技が止まる大技のターン) と、 その手前 (振り直しを温存する)
            bool planDown = downRerollPlan && downTimingAware && GameLoop.GameHooks.DownCancel == CombatSystem.DownCancelRule.Instant && (tele.downNotches != null || tele.downGaugeMax > 0);
            bool keyTurn = planDown && ((tele.enemyDown && !tele.enemyDownWeak)
                                        || (tele.turnsToHeavy == 0 && tele.downBreakNowKind == (int)CombatSystem.DownKind.Big));
            bool saveTurn = planDown && !keyTurn && (tele.downNextNotchHp >= 0 || tele.downGaugeMax > 0) && (tele.turnsToHeavy == 1 || tele.turnsToHeavy == 2);
            if (ctx.rerollsThisTurn >= (heavyNow || keyTurn ? RerollMaxPerTurn + 1 : RerollMaxPerTurn)) return null;

            int k = dice.Length;
            var faces = ctx.equippedDiceFaces;
            float meanFace = 3.5f;
            if (faces != null && faces.Length > 0)
            {
                int sum = 0;
                for (int i = 0; i < faces.Length; i++) sum += faces[i];
                meanFace = sum / (float)faces.Length;
            }

            // ① 役が成立した手は捨てない
            var formed = new List<CombatSystem.RoleKind>(2);
            CombatSystem.YachtRoles.Evaluate(CombatManager.Instance != null ? CombatManager.Instance.RoleDice(dice) : dice, ctx.playerDiceFaceIdx, ctx.equippedFaceTiers, formed);   // 怠惰で引く前の目 (本体の ResolveRoles と同じ)
            if (formed.Count > 0) return null;

            var all = new List<int>(k);
            int rerollSum = 0;
            for (int i = 0; i < k; i++) { all.Add(i); rerollSum += dice[i]; }

            // E1 (SuperV3 rerollEv): 平均との比較ではなく、 配線の効用の期待値で決める
            if (v3.rerollEv)
            {
                int costE = CombatSystem.CombatManager.RerollCostNow(ctx, all.Count, attempt);   // 無料の回数 (〈擦り切れた賽袋〉ほか) も本体と同じ関数
                if (ctx.GetCharge() - RerollChargeReserve < costE) return null;
                var faceVals = (faces != null && faces.Length > 0) ? faces : new[] { 1, 2, 3, 4, 5, 6 };
                float cur = BestUtilityOf(dice, tele);
                float ev = ExpectedBestUtilityAfterReroll(k, faceVals, tele);
                // 充電 1 の効用は、 配線評価の充電項 (wChg 0.6 × 2) と同じ 1.2。 ただし**上限 (ChargeMax) に近いほど安い**:
                //   上限に張り付いた充電は使わなければ捨てるだけなので、 満タンでは費用 0。 (第 1 版は一律 1.2 で、
                //   リロールが 3 分の 1 に減り 5 層のボス戦が 36.7% → 65.1% の死亡に悪化した。 長い戦闘では充電が溜まる。)
                const float ChargeUtilityPerUnit = 1.2f;
                float scarcity = 1f - UnityEngine.Mathf.Clamp01(ctx.GetCharge() / (float)CombatSystem.CombatManager.ChargeMax);
                return ev - ChargeUtilityPerUnit * scarcity * costE > cur ? RerollPickForDie(dice, all) : null;
            }

            // [2026-11-26] 大事なターン: 振り直した後の最良の手の期待値で決める (効用に「割れば大技が止まる」「大ダウンで ×2」が入っているので、 振り直して割り切る得も数える)
            if (keyTurn)
            {
                int costK = CombatSystem.CombatManager.RerollCostNow(ctx, all.Count, attempt);
                if (ctx.GetCharge() < costK) return null;
                var faceValsK = (faces != null && faces.Length > 0) ? faces : new[] { 1, 2, 3, 4, 5, 6 };
                float curK = BestUtilityOf(dice, tele);
                float evK = ExpectedBestUtilityAfterReroll(k, faceValsK, tele);
                const float ChargeUtilityK = 1.2f;   // E1 と同じ充電 1 の効用 (上限に近いほど安い)
                float scarcityK = 1f - UnityEngine.Mathf.Clamp01(ctx.GetCharge() / (float)CombatSystem.CombatManager.ChargeMax);
                return evK - ChargeUtilityK * scarcityK * costK > curK ? RerollPickForDie(dice, all) : null;
            }

            // S2: 大きい攻撃の予告なら、 止めきれない手は平均近くでも振り直し、 充電の予備も使う
            if (heavyNow && rerollSum < tele.enemyAttackValue && rerollSum < meanFace * all.Count * 1.15f)
            {
                int costH = CombatSystem.CombatManager.RerollCostNow(ctx, all.Count, attempt);
                return ctx.GetCharge() >= costH ? RerollPickForDie(dice, all) : null;
            }

            // ② 平均を下回る手だけ振り直す。 役の品を持つ時は、 振り直して役が立つ見込みの分 (攻撃相当の量) だけ線を上げる (2026-11-21・段 3)
            if (rerollSum >= meanFace * all.Count + RoleRerollPips(ctx, faces, k, tele)) return null;

            int cost = CombatSystem.CombatManager.RerollCostNow(ctx, all.Count, attempt);
            // [2026-11-26] 大事なターンの手前は、 振り直し 1 回分 (次の回目の費用) の充電を残す
            int reserve = RerollChargeReserve + (saveTurn ? CombatSystem.CombatManager.RerollCostNow(ctx, all.Count, attempt + 1) : 0);
            if (ctx.GetCharge() - reserve < cost) return null;
            return RerollPickForDie(dice, all);
        }

        /// <summary>全部振り直した後に立つ役が、 役の軸の品で生む得の期待値 (攻撃相当の量・出目の和と同じ単位)。 持っていなければ 0。
        /// 面の出方を全部数える (<see cref="ExpectedBestUtilityAfterReroll"/> と同じ)。 役は本体と同じ関数 (<see cref="CombatSystem.TerminalCards.RoleOf"/>・封じ・格上げ込み)、
        /// 量は TerminalCards の定数 (Resolve の 6d と同じ値)。 役の回数・前のターンの役の目は本体の TerminalBattleState.roleCount / roleLastFace。
        /// 撃 1 つぶん (撃の数は見ない)。 与ダメージの % は振った目の和に掛けた量。 振り直した後の手なので〈追撃の弾帯〉は 3 の役なら乗る。 品の条件の目は本体の TerminalCards の定数 (2026-11-22)。
        /// shortcut: 次のターンへ持ち越す分 (小口の錠前・揃いのメトロノームの手札・負け越しの賽) と役そのものの効果は数えない。</summary>
        private static float RoleRerollPips(ItemSystem.PassiveSkills.CombatContext ctx, int[] faces, int k, CombatSystem.MutualTurnTelegraph tele)
        {
            var cm = CombatManager.Instance;
            var st = cm?.TerminalDeck?.state;
            if (st == null || k <= 0 || st.roleItems == CombatSystem.RoleItems.None) return 0f;
            var f = st.roleItems;
            var env = cm.TerminalEnvNow(ctx, tele.enemyAttackValue);
            var fv = (faces != null && faces.Length > 0) ? faces : new[] { 1, 2, 3, 4, 5, 6 };
            int fc = fv.Length, count = st.roleCount + 1;
            long total = 1; for (int i = 0; i < k; i++) total *= fc;
            var buf = new int[k];
            double acc = 0;
            for (long code0 = 0; code0 < total; code0++)
            {
                long t = code0;
                int sum = 0;
                for (int j = 0; j < k; j++) { buf[j] = fv[(int)(t % fc)]; t /= fc; sum += buf[j]; }
                var rk = CombatSystem.TerminalCards.RoleOf(buf, in env);
                if (rk == null) continue;
                var r = rk.Value;
                bool big = CombatSystem.YachtRoles.IsBig(r);
                int face = CombatSystem.TerminalCards.FaceOfRole(r);
                float v = 0f, mul = 1f;
                if ((f & CombatSystem.RoleItems.WoodTag) != 0 && CombatSystem.TerminalCards.FaceIn(CombatSystem.TerminalCards.WoodTagFaces, face)) v += CombatSystem.TerminalCards.WoodTagAttack + CombatSystem.TerminalCards.WoodTagBlock;
                if ((f & CombatSystem.RoleItems.Gear) != 0) v += face == CombatSystem.TerminalCards.GearFace || (big && r != CombatSystem.RoleKind.Straight) ? CombatSystem.TerminalCards.GearAttackHigh : CombatSystem.TerminalCards.GearAttack;   // answer9: どの役も (3 の役かゾロ目は倍・振り直しのターンの見込み)
                if ((f & CombatSystem.RoleItems.NotchBoard) != 0 && CombatSystem.TerminalCards.FaceIn(CombatSystem.TerminalCards.NotchBoardFaces, face))
                    v += System.Math.Min(CombatSystem.TerminalCards.NotchBoardMax, CombatSystem.TerminalCards.NotchBoardPer * count) * (big ? 2 : 1);
                if ((f & CombatSystem.RoleItems.Chipped) != 0 && CombatSystem.TerminalCards.FaceIn(CombatSystem.TerminalCards.ChippedFaces, face))
                    v += System.Math.Min(CombatSystem.TerminalCards.ChippedPen, System.Math.Max(0, ctx.enemyDefense));
                if ((f & CombatSystem.RoleItems.Clapper) != 0 && face != 0 && face == st.roleLastFace) mul += CombatSystem.TerminalCards.ClapperPct;
                if ((f & CombatSystem.RoleItems.Thunder) != 0 && big) { mul *= 1f + CombatSystem.TerminalCards.ThunderFinalPct; v += CombatSystem.TerminalCards.ThunderBlock; }
                else if ((f & CombatSystem.RoleItems.Thunder) != 0) { mul *= 1f + CombatSystem.TerminalCards.ThunderRerollPct; v += CombatSystem.TerminalCards.ThunderRerollBlock; }   // answer12: 振り直したターンの他の役
                if ((f & CombatSystem.RoleItems.Bloodmark) != 0 && CombatSystem.TerminalCards.BloodmarkFires(r))
                    mul *= 1f + CombatSystem.TerminalCards.BloodmarkPer * count;
                acc += v + (mul - 1f) * sum;
            }
            // 〈擦り切れた賽袋〉振り直したターンは役の有無を問わずブロック (answer12)
            return (float)(acc / total) + ((f & CombatSystem.RoleItems.Pouch) != 0 ? CombatSystem.TerminalCards.PouchBlock : 0f);
        }

        /// <summary>振り直す目の添字。 目利きの賽 (ADR-0015) を持つ時は、 いちばん大きい目を残して残りを振る。</summary>
        private static int[] RerollPickForDie(int[] dice, List<int> all)
        {
            if (GameLoop.EquippedDie.IdOf(GameLoop.GameManager.Instance?.Run) != GameLoop.DieIds.Appraiser || dice.Length < 2) return all.ToArray();
            int best = 0;
            for (int i = 1; i < dice.Length; i++) if (dice[i] > dice[best]) best = i;
            return CombatSystem.CombatManager.AllDiceIndexExcept(dice.Length, best);
        }

        /// <summary>1 ターンあたりのリロール上限。 全部振り直しは賭けなので、 同じターンに何度も重ねない。
        /// (ゲーム規則は 1 ターン 2 回まで・GAME.md §6-3。 方策は 1 回に留める。)</summary>
        private const int RerollMaxPerTurn = 1;
        /// <summary>リロールに回さず残す充電。 パッシブ (火花・雷撃) の原資を枯らさないため。</summary>
        private const int RerollChargeReserve = 6;

        private static bool Contains(int[] v, int x)
        {
            for (int i = 0; i < v.Length; i++) if (v[i] == x) return true;
            return false;
        }

        /// <summary>端子上限の判定で使う「その端子を使ったか」のフラグ。 毎回 new しないため使い回す。</summary>
        private readonly bool[] _termSeen = new bool[4];

        /// <summary>Optimal 配線評価のワーカー専用スクラッチ。共有書き込みを完全に避ける。</summary>
        private sealed class OptimalWiringBuffers
        {
            public readonly CombatSystem.DiceTerminal[] assign;
            public readonly bool[] termSeen = new bool[4];

            public OptimalWiringBuffers(int diceCount)
            {
                assign = new CombatSystem.DiceTerminal[diceCount];
            }
        }

        /// <summary>Bot 専用: 戦闘中、シュヴァリエ・サン=ジョリオラ戦でのレイピア起動同期。
        /// ボスが形態1(コントラタック)中はレイピアON、形態2(オポジション)中はOFF。
        /// 切替時の解除効果 (次のターン 手札 +2・攻撃は会心確定・撃破済みなら会心倍率 +1.0・2026-11-20) も自動で享受する。
        /// 2026-11-20 の作り直しで、 発動中は攻撃の端子に挿せない (ブロックとシールドで防いだ分 ×2 を返す) ── 切り替えの方策は旧い効果のまま (未調整)。</summary>
        private void AutoToggleRapierVsSaintGeorges(CombatManager cm, GameLoop.RunState run)
        {
            if (cm == null || run == null) return;
            if (run.ownedPassiveItems == null || !run.ownedPassiveItems.Contains("シュヴァリエのレイピア")) return;
            var enemy = cm.CurrentEnemy;
            if (enemy == null || enemy.id != "boss_layer5_hidden") return;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx == null) return;
            int bossPhase = (int)ctx.GetAccumulated("sg_phase");
            bool contreActive = ctx.GetAccumulated("player_contre") > 0f;
            if (bossPhase == 1 && !contreActive)
                GameLoop.Consumables.TryUseRapier(run);
            else if (bossPhase == 2 && contreActive)
                GameLoop.Consumables.TryUseRapier(run);
        }

        /// <summary>所持consumableから優先順に最初の1個を使用（戦闘中＝ctx即時適用）。</summary>
        /// <summary>回復量 (最大HP 比)。 <c>ItemIds.ConsHealFamily</c> の 4 段。 昇順で持つこと。
        /// 割合は本体の <see cref="GameLoop.Consumables.HealRatio"/> を引く (写しを持たない・#129)。 float の定数がそのまま入る。</summary>
        private static readonly (string id, float pct)[] HealTiers = TiersOf(GameLoop.Consumables.HealRatio,
            ItemIds.HealPotion1, ItemIds.HealPotion2, ItemIds.HealPotion3, ItemIds.HealPotion4);

        /// <summary>**死ぬ場面**で飲む 1 本を選ぶ。 温存しない。
        ///
        /// <para>小さい順に見て「飲めば致死量を超えて生き残れる」最初のものを使う ──
        /// 過剰回復で上限に溢れさせないため。 どれでも足りないときは**最大のものを飲む**
        /// (足りなくても生存確率は上がる。 持ったまま死ぬよりは必ず良い)。</para>
        ///
        /// <para>「大きい回復は窮地まで温存」という旧ルールは、 1 撃が最大HP の 3 割入る
        /// 7層後半では解除条件 (HP 1/4 以下) に触れる前に死ぬため機能しなかった。</para></summary>
        private bool UseBestHeal(GameLoop.RunState run, int hp, int lethal)
        {
            if (run?.ownedConsumables == null || run.playerMaxHP <= 0) return false;
            string pick = null;
            foreach (var t in HealTiers)
            {
                if (!run.ownedConsumables.Contains(t.id)) continue;
                int after = Mathf.Min(run.playerMaxHP, hp + Mathf.RoundToInt(run.playerMaxHP * t.pct));
                if (after > lethal) { pick = t.id; break; }
            }
            if (pick == null)
                foreach (var t in HealTiers)          // 昇順なので最後に残るのが最大
                    if (run.ownedConsumables.Contains(t.id)) pick = t.id;
            return pick != null && GameLoop.Consumables.Use(run, pick);
        }

        private bool UseFirst(GameLoop.RunState run, params string[] ids)
        {
            if (run?.ownedConsumables == null) return false;
            foreach (var id in ids)
                if (run.ownedConsumables.Contains(id))
                    return GameLoop.Consumables.Use(run, id);
            return false;
        }
    }
}

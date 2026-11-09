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
using AutoTest.Ultra;

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
                    UseFirst(run, "天火の膏薬", "業火の膏薬", "燐の油", "鬼火の油");
                    if (!v3.superConsumables) UseFirst(run, "惜別の護符", "銀の護符", "鉄の護符", "木の護符");
                }
                else
                {
                    UseFirst(run, "燐の油", "鬼火の油");
                    if (!v3.superConsumables) UseFirst(run, "鉄の護符", "木の護符");
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
            WiringSkill effectiveSkill = EffectiveWiringSkill;
            bool useLayer6Routine = effectiveSkill == WiringSkill.Super
                                 && IsSuperTailMode
                                 && superLayer6OptimalCombatRoutine
                                 && e0 != null
                                 && e0.id == "boss_layer6";
            // [2026-08-22] **7層でも Optimal 方策へ切り替える** (既定 OFF)。
            //   下のコメントは「7層は Super が勝つので切り替えない」と書いているが、
            //   **それは古い測定**。 役の出目依存化・役札の連戦持ち越し・パーツ有りの
            //   現行条件で測り直すと符号が反転している:
            //     両AIが7層へ到達した 379 ラン に限定して Optimalだけクリア 71 /
            //     Superだけクリア 41 (p=0.0059)。 到達時点の状態 (HP率・パッシブ数・
            //     武器Tier・物資) は Super が僅かに上なので、 選択バイアスでは説明できない。
            //   IsSuperTailMode で絞らない ── 0pt で観測した差なので難度条件を付けない。
            bool useLayer7Routine = effectiveSkill == WiringSkill.Super
                                 && superLayer7OptimalCombatRoutine
                                 && e0 != null
                                 && !string.IsNullOrEmpty(e0.id)
                                 && e0.id.StartsWith(GameLoop.BossIds.Layer7Prefix);
            bool useOptimalRoutine = useLayer6Routine || useLayer7Routine;
            if (effectiveSkill == WiringSkill.Exact)
            {
                // 厳密版。 (ADR-0011: 役は自動で効くので、 発動方策の注入は無い。 役の効果は先読みしていない。)
                exactAI.BeginCombat(e0 != null ? e0.id : "");
                cm.WiringPolicy = exactAI.ChooseWiringPlan;
                cm.RerollPolicy = exactAI.ChooseReroll;
            }
            else if (useOldSuperAI && effectiveSkill == WiringSkill.Super && !useOptimalRoutine)
            {
                // 天井版は配線とリロールを一つの価値関数で決める。 **両方差し替える** ──
                // 配線だけ先読みにしてリロールを貪欲のままにすると、 探索の前提と食い違う。
                cm.WiringPolicy = logWiringDiff
                    ? (System.Func<int[], CombatSystem.MutualTurnTelegraph, CombatSystem.WiringPlan>)SuperWiringWithDiff
                    : superAI.ChooseWiringPlan;
                // [計測 2026-09-19] Super のリロール判断を軽いものへ差し替えられるようにする。
                //   Super の所要時間の 98% がリロールの全列挙なので、 近似で強さがどれだけ落ちるかを測る。
                //   0 = 全列挙 (既定) / 1 = Optimal のリロール方策 / 2 = 全列挙を軽く (標本・厳密範囲を絞る)
                if (superRerollMode == 2)
                {
                    superAI.rerollSamples = superLightRerollSamples;
                    superAI.exactRerollMaxDice = superLightExactMaxDice;
                }
                cm.RerollPolicy = superRerollMode == 1
                    ? (System.Func<int[], CombatSystem.MutualTurnTelegraph, int, int[]>)MutualRerollPolicy
                    : superAI.ChooseReroll;
            }
            else
            {
                // 50pt・同一10000シードで、両AIが6層へ遭遇した2322件に限定すると
                // Optimalだけ勝利425 / Superだけ勝利267。Superの未来近似は灰の再生・
                // 周期予兆・烈炎の複合戦で局所的に劣るため、ここだけ実測上位方策へ切替える。
                // 7層はSuperだけ完全クリア300 / Optimalだけ173なので切り替えない。
                //   ↑ **この 7層の判断は 2026-08-22 に覆った** (上の useLayer7Routine 参照)。
                if (useOptimalRoutine) superAI.BeginCombat(e0.id);
                cm.WiringPolicy = MutualWiringPlanPolicy;
                cm.RerollPolicy = effectiveSkill == WiringSkill.Naive ? null
                                : (System.Func<int[], CombatSystem.MutualTurnTelegraph, int, int[]>)MutualRerollPolicy;
            }

            _fbDecided = _fbTurns = _fbKillTurns = 0;   // [計装] 防ぎ切りの規則の数え直し
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
                        UseFirst(run, "回復薬", "小回復薬");
                    }
                }
                // 逃げる (2026-09-28): 追跡者の戦闘は、 頭で HP が少なければ逃げる。
                if (cm.CurrentCombatTurn <= 1 && ShouldFleePursuer(run, GameManager.Instance) && cm.RequestFlee())
                    FreeMapPursuerFlees++;
                var tr = cm.ExecuteTurn();
                if (tr.isDraw) _cwDraw++;
                else if (tr.playerWon) _cwWin++;
                else { _cwLoss++; if (tr.totalDamage <= 0) _cwLossAbs++; }
            }
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

        /// <summary>
        /// ADR-0009 配線ポリシー (完全情報・交換レート最大化)。
        /// tools/adr0009_sim.py の choose_wiring と同じロジックを C# 移植:
        ///   出目降順で「攻撃 a 本 / ブロック b 本 / 充電 c 本」を全列挙 (O(k^2)) し、
        ///   効用 U = w_atk × min(dmg, enemyHP) + w_def × min(block, enemyAtk)
        ///          + 撃破ボーナス − 致死ペナルティ + 充電価値 で最大化。
        /// ビルド重みはシミュレータのバランス型 (1.0/1.0/0.6) を既定にする。
        /// </summary>
        /// <summary>配線方策を <see cref="CombatSystem.WiringPlan"/> として返す薄い包み。</summary>
        private CombatSystem.WiringPlan MutualWiringPlanPolicy(int[] rolls, CombatSystem.MutualTurnTelegraph tele)
        {
            PrepareSuperTurn(rolls, tele);
            // 予告の外のダメージの観測: 前のターンに見込んだ被ダメより HP が多く減っていたら、 この戦闘にはそれがある
            int hpNow = CombatSystem.CombatManager.Instance != null ? CombatSystem.CombatManager.Instance.PlayerHP : -1;
            if (_fbPrevHp >= 0 && hpNow >= 0 && _fbPrevHp - hpNow > _fbPrevExpTaken) _fbUnavoidSeen = true;
            _lastFreeBlock = _lastKillsNow = false;
            var plan = MutualWiringPolicy(rolls, tele);
            _fbDecided++; if (_lastFreeBlock) _fbTurns++; if (_lastKillsNow) _fbKillTurns++;
            _fbPrevHp = hpNow; _fbPrevExpTaken = _lastExpTaken;
            if (wiringMistakeRate > 0f) plan = MaybeMisWire(rolls, tele, plan);
            if (turnAudit) WriteTurnAudit(rolls, tele, plan.main);
            return plan;
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
        private static readonly (string id, int amount)[] SuperTalismans = { ("木の護符", 15), ("鉄の護符", 30), ("銀の護符", 50), ("惜別の護符", 80) };
        /// <summary>[計装] Super が予告を読んで使った護符の数 (バッチ累計)。</summary>
        public static long SuperTalismansUsed;

        /// <summary>配線のミスの率 (2026-11-06・0 = しない)。 Optimal を 10〜100 戦の経験者とみなす: 人は毎ターン同じ規則で最善を選べない。
        /// ミスは「1 個を攻撃とブロックの反対へ挿す」形。 致死を見落とすミスはしない (予告を読めば分かるので経験者はしない)。
        /// 乱数は局面から作る専用の値で、 GameRng を消費しない (同じシードの対比較を壊さない)。</summary>
        public float wiringMistakeRate = 0f;

        private CombatSystem.WiringPlan MaybeMisWire(int[] rolls, CombatSystem.MutualTurnTelegraph tele, CombatSystem.WiringPlan plan)
        {
            var w = plan.main;
            var cm = CombatManager.Instance;
            if (w == null || rolls == null || cm == null || rolls.Length < 2) return plan;
            uint h = (uint)(cm.EnemyHP * 73856093) ^ (uint)(cm.PlayerHP * 19349663) ^ (uint)(tele.turn * 83492791) ^ (uint)((_cur?.index ?? 0) * 2654435761u);
            foreach (var d in rolls) h = h * 31u + (uint)d;
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            if ((h & 0xFFFF) / 65536f >= wiringMistakeRate) return plan;
            int k = rolls.Length;
            var m = (CombatSystem.DiceTerminal[])w.Clone();
            // 攻撃かブロックの 1 個を反対へ挿し直す (見立てを誤った時の形。 2 個の入れ違いは目の差しか動かず、 ほぼ無害だった)
            bool moved = false;
            for (int off = 0; off < k && !moved; off++)
            {
                int i = (int)((h >> 16) % (uint)k + (uint)off) % k;
                if (m[i] == CombatSystem.DiceTerminal.Attack) { m[i] = CombatSystem.DiceTerminal.Block; moved = true; }
                else if (m[i] == CombatSystem.DiceTerminal.Block) { m[i] = CombatSystem.DiceTerminal.Attack; moved = true; }
            }
            if (!moved) return plan;
            int block = 0, aCnt = 0;
            for (int i = 0; i < k; i++)
            {
                if (m[i] == CombatSystem.DiceTerminal.Block) block += rolls[i];
                if (m[i] == CombatSystem.DiceTerminal.Attack) aCnt++;
            }
            if (aCnt == 0) return plan;
            int shield = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context?.consShield ?? 0;
            if (System.Math.Max(0, tele.enemyAttackValue - block) >= cm.PlayerHP + shield) return plan;   // 死ぬミスはしない
            WiringMistakes++;
            plan.main = m;
            return plan;
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

        /// <summary>バッチ終了時に閉じる (worker が呼ぶ)。</summary>
        public void CloseTurnAudit()
        {
            try { _turnAuditWriter?.Flush(); _turnAuditWriter?.Dispose(); } catch { }
            _turnAuditWriter = null;
        }

        // ============================================================
        //  [計装 2026-09-19] 先読み (Super) と 1 ターン最善 (Optimal) の配線の食い違い
        // ============================================================
        /// <summary>true のとき、 Super の配線判断ごとに<b>同じ局面で Optimal ならどう配線したか</b>も計算し、
        /// 局面の特徴と一緒に CSV へ書く。 Optimal の方策は乱数を使わないので、 呼んでもゲームは変わらない。
        /// 目的は「先読みで何が得られているか」を局面で特定すること (エリート戦の死亡率 5.2% → 3.9%)。</summary>
        public bool logWiringDiff;

        /// <summary>[計測] Super のリロール判断。 0 = 全列挙 (既定) / 1 = Optimal の方策 / 2 = 軽い全列挙。</summary>
        public int superRerollMode = 0;
        /// <summary>superRerollMode=2 のときの標本数と、 厳密に全列挙する振り直し個数の上限。</summary>
        public int superLightRerollSamples = 4;
        public int superLightExactMaxDice = 0;
        [NonSerialized] public string wiringDiffPath;
        private System.IO.StreamWriter _wiringDiffWriter;

        private CombatSystem.WiringPlan SuperWiringWithDiff(int[] rolls, CombatSystem.MutualTurnTelegraph tele)
        {
            var sp = superAI.ChooseWiringPlan(rolls, tele);
            try
            {
                var op = MutualWiringPolicy(rolls, tele);
                WriteWiringDiff(rolls, tele, sp.main, op.main);
            }
            catch (Exception ex) { Debug.LogWarning("[WiringDiff] " + ex.Message); }
            return sp;
        }

        private void WriteWiringDiff(int[] rolls, CombatSystem.MutualTurnTelegraph tele,
                                     CombatSystem.DiceTerminal[] sw, CombatSystem.DiceTerminal[] ow)
        {
            if (string.IsNullOrEmpty(wiringDiffPath) || rolls == null) return;
            if (_wiringDiffWriter == null)
            {
                _wiringDiffWriter = new System.IO.StreamWriter(wiringDiffPath, false, new System.Text.UTF8Encoding(false));
                _wiringDiffWriter.WriteLine("run,kind,enemy,turn,stage,nextThr,enemyAtk,pHp,pMax,eHp,eMax,shield,charge,diceSum,"
                    + "sAtk,sBlk,sChg,sOth,oAtk,oBlk,oChg,oOth");
            }
            var cm = CombatManager.Instance;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            var e = cm?.CurrentEnemy;
            int kind = GameLoop.BossIds.IsBoss(e?.id) ? 2
                     : MapSystem.MapManager.Instance?.CurrentNode?.EffectiveType.ToEnemyKind() == EnemyKind.Elite ? 1 : 0;
            int dsum = 0; foreach (var d in rolls) dsum += d;
            void Sums(CombatSystem.DiceTerminal[] w, out int a, out int b, out int c, out int o)
            {
                a = b = c = o = 0;
                if (w == null) return;
                for (int i = 0; i < rolls.Length && i < w.Length; i++)
                    switch (w[i])
                    {
                        case CombatSystem.DiceTerminal.Attack: a += rolls[i]; break;
                        case CombatSystem.DiceTerminal.Block:  b += rolls[i]; break;
                        case CombatSystem.DiceTerminal.Charge: c += rolls[i]; break;
                        default: o += rolls[i]; break;
                    }
            }
            Sums(sw, out int sa, out int sb, out int sc, out int so);
            Sums(ow, out int oa, out int ob, out int oc, out int oo);
            _wiringDiffWriter.WriteLine(string.Join(",", new object[] {
                _cur?.index ?? -1, kind, e?.id ?? "", tele.turn, tele.escalationStage, tele.nextThresholdTurn, tele.enemyAttackValue,
                cm?.PlayerHP ?? 0, cm?.PlayerMaxHP ?? 0, cm?.EnemyHP ?? 0, cm?.EnemyMaxHP ?? 0,
                ctx?.consShield ?? 0, ctx?.GetCharge() ?? 0, dsum, sa, sb, sc, so, oa, ob, oc, oo }));
        }

        /// <summary>バッチ終了時に閉じる (worker が呼ぶ)。</summary>
        public void CloseWiringDiff()
        {
            try { _wiringDiffWriter?.Flush(); _wiringDiffWriter?.Dispose(); } catch { }
            _wiringDiffWriter = null;
        }

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
            public CombatSystem.SpecialTerminalDef specDef;
            public float critMul;
            public int eDefNow, myPen;
            public bool stepGuardCut, stepUndying, steadfastReady, stepDaybreak, hasVoidStance;
            public int undyingStreak, myShield, enemyMaxHpNow, voidStacks;
            public float bashRate, bulwarkRate;
            public float diceAtkPct;           // 攻撃に挿した出目への上乗せ率 (ctx.attackDicePct・2026-11-06)
            public BossPlaybook play;
            public float[] atkGrowth;          // 防ぎ切りの規則: 撃破まで n ターンの敵の攻撃の伸び (null = 規則なし)
            public bool ledger;                // [V3 hpLedger] 効用を HP の帳簿で測る
            public float dFull;                // [V3 hpLedger] 先のターンに全部攻撃に挿した時の 1 ターンの与ダメの見込み
        }

        /// <summary>[V3 hpLedger] この戦闘で失う HP の見込み = 今ターンの被ダメ + 撃破までの残りターンで受ける被ダメ (全部攻撃する前提)。</summary>
        private float LedgerLoss(in OptimalTurnState s, int effDmg, int taken)
        {
            int rem = s.enemyHp - System.Math.Min(effDmg, s.enemyHp);
            if (rem <= 0) return taken;
            int n = System.Math.Min(HorizonCapTurns, UnityEngine.Mathf.CeilToInt(rem / System.Math.Max(1f, s.dFull)));
            float future = 0f;
            for (int j = 1; j <= n; j++)
            {
                float g = s.atkGrowth != null ? s.atkGrowth[System.Math.Min(j + 1, HorizonCapTurns)] : 1f;
                future += s.enemyAtk * g;
            }
            return taken + v3.ledgerFutureShare * future;
        }

        /// <summary>配線 <paramref name="w"/> の効用 (state + 出目 + 配線 → 効用)。 盤面を読まない純粋な関数 (v3 の係数だけ読む)。</summary>
        private float OptimalUtility(in OptimalTurnState s, int[] rolls, CombatSystem.DiceTerminal[] w)
            => OptimalUtility(in s, rolls, w, out _);

        /// <summary>このターンを配線 w で解いた結果 (先読みが次ターンの盤面を作るのに使う)。 効用の計算で出た値をそのまま写す。</summary>
        private struct OptimalTurnOutcome
        {
            public int effDmg;      // 敵 HP を減らす量の見積もり (撃破判定に使う値と同じ)
            public int taken;       // 自 HP の減少の見積もり
            public int block;
            public int aCount, bCount;
            public bool bashSpent;  // 盾〈シールドバッシュ〉でシールドを使い切った
            public bool freeBlock;  // [計装 2026-11-10] 防ぎ切りの規則の対象 (撃破まで HP を失わない) だった
            public bool killsNow;   // [計装] 今ターン撃破の見込み
        }

        private float OptimalUtility(in OptimalTurnState s, int[] rolls, CombatSystem.DiceTerminal[] w, out OptimalTurnOutcome outcome)
        {
            int k = rolls.Length;
            var tele = s.tele; int enemyHp = s.enemyHp, myHp = s.myHp, myMaxHp = s.myMaxHp, atkBase = s.atkBase, enemyAtk = s.enemyAtk;
            float wAtk = s.wAtk, wDef = s.wDef, wChg = s.wChg, critMul = s.critMul;
            var specDef = s.specDef; var play = s.play; var atkGrowth = s.atkGrowth;
            int eDefNow = s.eDefNow, myPen = s.myPen, undyingStreak = s.undyingStreak, myShield = s.myShield;
            int enemyMaxHpNow = s.enemyMaxHpNow, voidStacks = s.voidStacks;
            bool stepGuardCut = s.stepGuardCut, stepUndying = s.stepUndying, steadfastReady = s.steadfastReady;
            bool stepDaybreak = s.stepDaybreak, hasVoidStance = s.hasVoidStance;
            float bashRate = s.bashRate, bulwarkRate = s.bulwarkRate;
            int aCount = 0, bCount = 0, c = 0, aSum = 0, blockRaw = 0, maxBlockDie = 0;
            int sCount = 0, sSum = 0, chargeRaw = 0;
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
                else if (w[i] == CombatSystem.DiceTerminal.Special)
                { sCount++; sSum += v; }
                else { c++; chargeRaw += v; }
            }

            // --- 特殊端子 (§6-5) の寄与を見積もる ---
            //   **ここを書かないと BOT は特殊端子へ 1 本も挿さない** ── 挿した瞬間に
            //   攻撃にもブロックにも充電にもならないので、 効用が単調に下がるため。
            float specFlat = 0f;
            if (specDef != null && sCount > 0)
            {
                switch (specDef.kind)
                {
                    case CombatSystem.SpecialTerminalKind.HeavyStrike:
                        aSum += Mathf.CeilToInt(sSum * CombatSystem.SpecialTerminals.HeavyStrikeMultiplier); break;
                    case CombatSystem.SpecialTerminalKind.FullGuard:
                        blockRaw += sSum * CombatSystem.SpecialTerminals.FullGuardMultiplier; break;
                    case CombatSystem.SpecialTerminalKind.Bleed:
                        specFlat += sSum * 0.8f + sSum * sCount * 0.5f; break;
                    case CombatSystem.SpecialTerminalKind.Heal:
                        specFlat += sSum * wDef; break;
                    case CombatSystem.SpecialTerminalKind.Deathwish:
                    {
                        int missing = Mathf.Max(0, myMaxHp - myHp);
                        aSum += sSum + Mathf.FloorToInt(missing * CombatSystem.SpecialTerminals.DeathwishMissingHpPct);
                        specFlat -= sSum * wDef;   // 自傷ぶんの不利
                        break;
                    }
                    case CombatSystem.SpecialTerminalKind.VitalPoint:
                    {
                        int mn = int.MaxValue;
                        for (int i = 0; i < k; i++) if (rolls[i] < mn) mn = rolls[i];
                        bool allMin = true;
                        for (int i = 0; i < k && allMin; i++)
                            if (w[i] == CombatSystem.DiceTerminal.Special && rolls[i] != mn) allMin = false;
                        if (allMin) aSum += sSum * CombatSystem.SpecialTerminals.VitalPointMultiplier;
                        break;
                    }
                    case CombatSystem.SpecialTerminalKind.Aim:
                        specFlat += Mathf.CeilToInt(sSum / 2f) / 100f * aSum * (critMul - 1f); break;
                    case CombatSystem.SpecialTerminalKind.Battery:
                        chargeRaw += sSum + sCount * CombatSystem.SpecialTerminals.BatteryPerDice;
                        c += sCount;
                        break;
                    case CombatSystem.SpecialTerminalKind.Coolant:
                    case CombatSystem.SpecialTerminalKind.Foundry:
                        // 蓄積型。 1 ターンでは返らないので「蓄積 1 あたりの期待値」で薄く評価する
                        specFlat += sSum * 0.6f; break;
                    case CombatSystem.SpecialTerminalKind.Riposte:
                        specFlat += Mathf.Min(sSum, Mathf.Max(0, enemyAtk - blockRaw)) * wAtk; break;
                }
            }

            // 攻撃に挿した出目への上乗せ率 (2026-11-06): 本体と同じく 攻撃の出目の合計 × 率 (切り上げ)
            if (s.diceAtkPct > 0f && aSum > 0)
                aSum += Mathf.CeilToInt(aSum * s.diceAtkPct);
            // 剣〈切り返し〉: 攻撃とブロックの両方に挿したターン、 ブロック × 0.5 を攻撃にも
            if (stepGuardCut && aCount > 0 && bCount > 0)
                aSum += TurnRules.GuardCutBonus(blockRaw);
            // 盾〈シールドバッシュ〉: 全部攻撃のターン、 シールドを使い切って × 率 を攻撃へ
            bool bashSpent = bashRate > 0f && aCount == k && myShield > 0;
            if (bashSpent) aSum += TurnRules.BashBonus(myShield, bashRate);
            // 盾〈堅守の構え〉: 全部ブロックのターン、 シールド +15 (このターンの被弾から使える)
            if (steadfastReady && bCount == k)
                blockRaw += ItemSystem.PassiveSkills.Effects.SteadfastStance.Shield;

            int dmg = atkBase + aSum;
            // 剣〈不滅の刃〉: 両方に挿したターンが続くたび与ダメ +8% (このターンで続けば streak+1)
            if (stepUndying && aCount > 0 && bCount > 0)
                dmg = (int)(dmg * TurnRules.UndyingMul(undyingStreak + 1));
            // 敵の防御 (このターンの値) で軽減
            if (aCount > 0 && eDefNow > 0)
                dmg = CombatSystem.DefenseFormula.Apply(dmg, CombatSystem.DefenseFormula.Effective(eDefNow, myPen));
            // damageCapped ボス: 与ダメが閾値超えたら超過分を半減した見積もりで探索 (実挙動と一致)
            int effDmg = dmg;
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

            int block = blockRaw;
            // 〈貫きの錐〉: 敵攻撃値 1 につきブロックを 2 削る (予告済み)。
            if (tele.playerBlockIgnored)
                block = System.Math.Max(0, block - enemyAtk * CombatSystem.CombatManager.PierceAwlPerAttack);

            int taken = System.Math.Max(0, enemyAtk - block) + (int)reflectPenalty;
            // 盾〈黎明〉: 無傷で受け切ったら、 敵の攻撃 10 につき敵の最大 HP の 5% を削る
            if (stepDaybreak && taken == 0)
                effDmg += TurnRules.DaybreakDamage(enemyMaxHpNow, enemyAtk);

            // 処刑人の烙印: 攻撃終了時に HP が最大の 20% 以下なら 9999。
            if (tele.executeArmed && TurnRules.ExecuteKills(myHp - taken, myMaxHp))
                taken = myHp;

            // 〈虚空〉: 攻撃 0 本のターンだけ発動。 殻で被ダメを削り、 現在 HP 割合を刻む。
            if (aCount == 0 && hasVoidStance)
            {
                int n = System.Math.Min(
                    ItemSystem.PassiveSkills.Effects.VoidStance.StackCap, voidStacks);
                int chip = UnityEngine.Mathf.CeilToInt(
                    enemyHp * (ItemSystem.PassiveSkills.Effects.VoidStance.ChipBase
                             + ItemSystem.PassiveSkills.Effects.VoidStance.ChipPerStack * n) / 100f);
                effDmg += chip;   // 殴らない代わりに通る軽減不可ダメージ
                float shell = System.Math.Max(0,
                    90 - ItemSystem.PassiveSkills.Effects.VoidStance.ReducePerStack * n) / 100f;
                if (shell > 0f) taken = System.Math.Max(0, (int)(taken * (1f - shell)));
            }

            float u = s.ledger
                    ? specFlat - LedgerLoss(in s, effDmg, taken)
                    : wAtk * System.Math.Min(effDmg, enemyHp)
                    + wDef * System.Math.Min(block, enemyAtk)
                    + specFlat;
            // 盾〈城壁〉: 完全に受け止めたら、 余ったブロック × 率 がシールドへ (次のターン以降の弾・毎ターン半減するので半値で見る)
            if (bulwarkRate > 0f)
                u += 0.5f * wDef * TurnRules.BulwarkGain(block, enemyAtk, bulwarkRate);
            // 〈シールドバッシュ〉で使い切ったシールドは、 このターンの被弾を吸えない
            if (bashSpent) u -= wDef * System.Math.Min(myShield, System.Math.Max(0, enemyAtk - block));
            if (effDmg >= enemyHp && !s.ledger) u += 50f + enemyAtk; // 今ターン撃破 = 敵攻撃が消える (帳簿では先の被ダメ 0 として既に入っている)
            if (taken >= myHp) u -= 10000f;              // 致死回避を最優先

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

            // 烈炎 (6 層ボス): ブロック端子へ閾値以上配線すると毎ターン スタック+1、
            //   ターン開始時にスタック分の軽減無視ダメ。 **配線側がこれを見られないと
            //   ただの一方的な税になる**ので、 撃破までの累積を効用へ入れる。
            if (tele.blazePenalizesBlock)
            {
                int s0 = tele.blazeStacks;
                float blaze = (bCount >= ItemSystem.PassiveSkills.Effects.BlazeBrand.BlockDiceThreshold)
                            ? s0 * ttk + ttk * (ttk + 1) * 0.5f   // 積み続ける
                            : s0 * ttk;                            // 据え置き
                u -= blaze;
            }
            u += wChg * v3.chargeMul * c * 2f;           // 充電価値 (リロールの原資)
            if (v3.takenCost != 0f && !s.ledger) u -= v3.takenCost * _takenMulNow * taken;     // E5 (V3)
            u += 0.01f * aCount;                         // タイブレーク: 同効用なら攻撃を選ぶ

            // 2026-07-28: 攻撃端子 0 本 = **攻撃を行わない** (素火力も追撃も出ない)。
            //   よって「攻撃 0 本」は、 その 1 本を防御へ回さないと死ぬ場合にのみ選ぶ。
            //   ※〈虚空〉持ちもこの抑止の対象 (2026-07-29)。 刻みの価値は effDmg に足してあるので、
            //     本当に得なら抑止を越えて選ばれる。
            if (aCount == 0)
            {
                // 「ブロックの最大目を 1 本だけ攻撃へ回す」場合の被弾と比較する。
                int takenIfAttackOne = System.Math.Max(0, enemyAtk - (block - maxBlockDie));
                bool onlySurvivesByNotAttacking = (takenIfAttackOne >= myHp) && (taken < myHp);
                if (!onlySurvivesByNotAttacking) u -= 10000f;
            }
            outcome = new OptimalTurnOutcome { effDmg = effDmg, taken = taken, block = block, aCount = aCount, bCount = bCount, bashSpent = bashSpent,
                                               freeBlock = fbCovers, killsNow = fbKills };
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
            public int specialLimit, termKinds;
            public int nextBlazeBase;
            public float enemyMaxHp;           // 敵の最大 HP (Phase 判定の比に使う)
            public readonly Dictionary<(int, int, int, int, int, int, int), float> memo
                = new Dictionary<(int, int, int, int, int, int, int), float>(32);
            /// <summary>先読みにかける候補の足切り = 今ターンの効用の上位 <see cref="LookaheadTopK"/> 本目。 これ未満の配線は選ばない。</summary>
            public float cutoff = float.NegativeInfinity;
        }

        /// <summary>先読みにかける候補の数 (2026-11-05)。 全候補 (最大 27) を先読みすると 1 判断で約 4 万回の効用評価になり、
        /// ボス戦のみでも 5,000 ランに約 28 分かかった。 今の効用の上位だけを読む (同点はすべて含める)。</summary>
        private const int LookaheadTopK = 4;

        /// <summary>今ターンの効用の上位 K 本目の値。 列挙の制約 (特殊端子の上限・封印) は本体の列挙と同じ。</summary>
        private float LookaheadCutoff(in OptimalTurnState st, int[] rolls, int termKinds, int specialLimit, int sealedTerm)
        {
            int k = rolls.Length;
            int total = 1; for (int i = 0; i < k; i++) total *= termKinds;
            var w = new CombatSystem.DiceTerminal[k];
            var top = new List<float>(total);
            for (int code = 0; code < total; code++)
            {
                int t = code, spec = 0; bool sealedUsed = false;
                for (int i = 0; i < k; i++)
                {
                    w[i] = (CombatSystem.DiceTerminal)(t % termKinds); t /= termKinds;
                    if (w[i] == CombatSystem.DiceTerminal.Special) spec++;
                    if (sealedTerm >= 0 && (int)w[i] == sealedTerm) sealedUsed = true;
                }
                if (spec > specialLimit || sealedUsed) continue;
                top.Add(OptimalUtility(in st, rolls, w));
            }
            if (top.Count <= LookaheadTopK) return float.NegativeInfinity;
            top.Sort((a, b) => b.CompareTo(a));
            return top[LookaheadTopK - 1];
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
            plan.specialLimit = CombatSystem.SpecialTerminals.EquippedLimit(run);
            plan.termKinds = plan.specialLimit > 0 ? 4 : 3;
            plan.enemyMaxHp = System.Math.Max(1, enemy.maxHP);

            // 次ターンの敵の攻撃: 公開規則 (Escalation.EnemyAttackValue) に、 敵ダイスは面の平均を入れる。
            //   ダイス合計のパッシブ補正は今ターンの予告との差で見る。 プレイヤーの防御は本体と同じ関数を通す。
            int diceNow = 0;
            if (st.tele.enemyDice != null) for (int i = 0; i < st.tele.enemyDice.Length; i++) diceNow += st.tele.enemyDice[i];
            int bonus = st.tele.enemyDice != null ? st.tele.enemyDiceTotal - diceNow : 0;
            int expTotal = (int)System.Math.Round(enemy.EffectiveRollCount * (1 + enemy.EffectiveRollMax) / 2.0 + bonus);
            int nt = plan.turn + 1;
            int delay = (int)ctx.GetAccumulated(CombatSystem.CombatManager.CoolantDelayKey);
            int nextAtk = CombatSystem.CombatManager.ApplyPlayerDefense(ctx,
                CombatSystem.Escalation.EnemyAttackValue(enemy, System.Math.Max(1, nt - delay), expTotal, nt), ctx.enemyArmorPen);

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
            tele.sealedTerminal = -1;
            plan.nextBlazeBase = tele.blazeStacks;

            var ns = st;
            ns.tele = tele;
            ns.enemyAtk = nextAtk;
            ns.atkBase = System.Math.Max(0, cm.PlayerAttackPower);
            ns.atkGrowth = (FreeBlockRule && nextAtk > 0) ? BuildAtkGrowth(enemy, ctx, nt) : null;
            plan.next = ns;
            return plan;
        }

        /// <summary>配線 w の値 = 今ターンの効用 + 次ターンの最良効用の期待値。 今ターンで撃破・死亡する配線は今の効用のまま。</summary>
        private float LookaheadValue(in OptimalTurnState st, NextTurnPlan plan, int[] rolls, CombatSystem.DiceTerminal[] w)
        {
            float uNow = OptimalUtility(in st, rolls, w, out var o);
            if (uNow < plan.cutoff) return float.NegativeInfinity;      // 上位 K 本に入らない配線は読まない
            if (o.effDmg >= st.enemyHp || o.taken >= st.myHp) return uNow;

            var ns = plan.next;
            ns.enemyHp = st.enemyHp - System.Math.Min(o.effDmg, st.enemyHp);
            ns.myHp = st.myHp - o.taken;
            // シールド: 今ターンの被弾で吸われ、 〈城壁〉で増え、 次のターン頭に半分 (切り捨て・DAMAGE_CALC_REFERENCE ③-D)
            int shield = o.bashSpent ? 0 : System.Math.Max(0, st.myShield - o.taken);
            if (st.bulwarkRate > 0f) shield += TurnRules.BulwarkGain(o.block, st.enemyAtk, st.bulwarkRate);
            ns.myShield = shield / 2;
            // 剣〈不滅の刃〉: 攻撃とブロックの両方に挿したターンが続く
            ns.undyingStreak = (st.stepUndying && o.aCount > 0 && o.bCount > 0) ? st.undyingStreak + 1 : 0;
            // 盾〈堅守の構え〉: 全部ブロックで使うと次の 2 ターンは使えない
            int k = rolls.Length;
            int readyTurn = (plan.steadfastOn && st.steadfastReady && o.bCount == k)
                ? plan.turn + ItemSystem.PassiveSkills.Effects.SteadfastStance.Cooldown : plan.steadfastReadyTurn;
            ns.steadfastReady = plan.steadfastOn && plan.turn + 1 >= readyTurn;
            // 敵の防御: 持続分はダメージを入れたターンの終わりに割れる/積み上がる・構えの増減は次のターン番号で決まる
            ns.eDefNow = System.Math.Max(0, plan.armor.DefAt(plan.armor.EndOfTurn(plan.baseDef, o.effDmg > 0), plan.turn + 1));
            // 烈炎: ブロック端子を閾値以上に挿すとスタックが増える
            ns.tele.blazeStacks = plan.nextBlazeBase
                + ((st.tele.blazePenalizesBlock && o.bCount >= ItemSystem.PassiveSkills.Effects.BlazeBrand.BlockDiceThreshold) ? 1 : 0);

            var key = (ns.enemyHp, ns.myHp, ns.myShield, ns.undyingStreak, ns.steadfastReady ? 1 : 0, ns.eDefNow, ns.tele.blazeStacks);
            if (!plan.memo.TryGetValue(key, out float e))
            {
                // 端子の重みは次ターンの敵 HP 比で決まる (予兆ターンの Phase)
                TerminalWeights(in ns.play, plan.turn + 1, ns.enemyHp / plan.enemyMaxHp, out float wa, out float wd, out float wc);
                ns.wAtk = wa; ns.wDef = wd; ns.wChg = wc;
                e = ExpectedBestNextUtility(in ns, plan, k);
                plan.memo[key] = e;
            }
            return uNow + e;
        }

        /// <summary>次ターンの盤面 <paramref name="ns"/> で、 出目の全列挙 (面^個数・同じ多重集合は 1 回だけ評価) の最良効用の期待値。</summary>
        private float ExpectedBestNextUtility(in OptimalTurnState ns, NextTurnPlan plan, int k)
        {
            var faces = plan.faces;
            int fc = faces.Length;
            long total = 1; for (int i = 0; i < k; i++) total *= fc;
            var memo = new Dictionary<long, float>(64);
            var sorted = new int[k];
            var assign = new CombatSystem.DiceTerminal[k];
            double acc = 0;
            for (long code = 0; code < total; code++)
            {
                long tcode = code;
                for (int j = 0; j < k; j++) { sorted[j] = faces[(int)(tcode % fc)]; tcode /= fc; }
                System.Array.Sort(sorted);
                long key = 0; for (int j = 0; j < k; j++) key = key * 32 + sorted[j];
                if (!memo.TryGetValue(key, out float u))
                {
                    u = BestUtilityOver(in ns, sorted, assign, plan);
                    memo[key] = u;
                }
                acc += u;
            }
            return (float)(acc / total);
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
                int spec = 0;
                for (int i = 0; i < k; i++)
                {
                    assign[i] = (CombatSystem.DiceTerminal)(t % termKinds); t /= termKinds;
                    if (assign[i] == CombatSystem.DiceTerminal.Special) spec++;
                }
                if (spec > plan.specialLimit) continue;
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

        private CombatSystem.WiringPlan MutualWiringPolicy(int[] rolls, CombatSystem.MutualTurnTelegraph tele)
        {
            int k = rolls?.Length ?? 0;
            if (k == 0) return default;
            var cm = CombatManager.Instance;
            int enemyHp = cm?.EnemyHP ?? 1;
            int myHp = cm?.PlayerHP ?? 1;
            int myMaxHp = cm?.PlayerMaxHP ?? System.Math.Max(1, myHp);
            // 麻痺毒の小瓶: このターンの攻撃力減算 (0 で床)。 予告済みなので見積もりに含める。
            int atkBase = System.Math.Max(0, (cm?.PlayerAttackPower ?? 2) - tele.playerAttackPenalty);
            int enemyAtk = tele.enemyAttackValue;

            // 出目降順ソートのインデックス (攻撃には高い目・ブロックは次・充電は残り)
            var idx = new int[k];
            for (int i = 0; i < k; i++) idx[i] = i;
            System.Array.Sort(idx, (a, b) => rolls[b].CompareTo(rolls[a]));

            // 短期決戦の視野 (2026-08-04)。 「この配分を撃破まで続けたら累積でいくら食らうか」を
            // 効用に入れるための係数。 HorizonCapTurns は effDmg=0 のときの発散止めと、
            // 「遠すぎる先は見ない」の兼用。
            // 1 ターン引き延ばすことのコスト。 既存項の桁 (wAtk×effDmg が ~25〜100、
            // wDef×min(block,enemyAtk) が ~3〜40) に対し、 ttk が 1 増える不利がブロック 1 本の
            // 利得を上回るように置いた初期値。

            // 〈虚空〉所持判定 (2026-07-29 改訂)。
            //   旧: 虚空は「攻撃 0 本で双方 0 ダメ」だったため、 攻撃 0 本の抑止から **除外**していた。
            //   新: リワークで双方 0 ダメは撤去され、「殻が剥がれる / 現在 HP 割合を刻む」に変わった。
            //       除外条項だけが残った結果、 剣ビルドは攻撃 0 本を無条件で選び放題になり、
            //       7 層で 15.3 ターン中 3.2 回しか殴らない状態を作っていた。
            //   → 除外をやめ、 **刻みの価値と殻の軽減を効用へ入れて天秤にかける**。
            var runForVoid = GameLoop.GameManager.Instance?.Run;
            bool hasVoidStance = runForVoid?.equippedWeaponId != null
                                 && (runForVoid.equippedWeaponId == "銀の長剣"
                                     || runForVoid.equippedWeaponId == "デュランダル");
            int voidStacks = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context?.voidStanceStacks ?? 0;

            var play = BossPlaybook.From(cm?.CurrentEnemy);
            // ボス側 HP 比 (Phase 判定に必要): CombatManager から取得 (敵ID・敵HP・敵MaxHP)
            float bossHpRatio = 1f;
            if (cm != null && cm.CurrentEnemy != null && cm.CurrentEnemy.maxHP > 0)
                bossHpRatio = (float)cm.EnemyHP / cm.CurrentEnemy.maxHP;
            // 端子の重み (ペルソナ・ボス別プレイブック・予兆ターン): TerminalWeights に切り出し済み (先読みも同じ式を呼ぶ)
            TerminalWeights(in play, tele.turn, bossHpRatio, out float wAtk, out float wDef, out float wChg);
            // ===== 割り当ての列挙 =====
            //   端子は 攻撃/ブロック/充電 (+特殊) の全列挙 (3^k)。 ADR-0011 でダイスは 3 個になったので 27 通り。
            //   役は配線に依存せず振った出目だけで決まるので、 配線の効用には役を入れない。
            var ctxRoles = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;

            // 会心倍率は「束(攻)＝会心確定」「奇(攻)＝会心率+25%」の価値見積もりに要る。
            float critMul = ctxRoles?.criticalMultiplier ?? 3f;
            var specDef = CombatSystem.SpecialTerminals.Equipped(runForVoid);

            // ===== 敵の防御と、 配線で条件が決まる武器の段 (2026-11-04) =====
            //   敵の防御: 予告に出ているこのターンの値 (構えの上乗せ・隙の 0・割れる/積み上がる装甲を反映済み)。
            //   以前は与ダメから防御を引いておらず、 装甲持ちと構えのターンで火力を大きく見誤っていた。
            //   武器の段 (§9-4b): 剣は攻撃とブロックの両方に挿すと伸び、 盾は全部守る・全部攻めるへ振り切ると伸びる。
            //   評価に無いと BOT はこの段を一度も狙わず、 家系の強さではなく方策の盲点を測ることになる。
            int eDefNow = System.Math.Max(0, tele.enemyDefense);
            int myPen = System.Math.Max(0, ctxRoles?.playerArmorPen ?? 0);
            var activeIds = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.ActivePlayerSkillIds;
            var rung = WeaponRungModel.From(activeIds);      // 段の検出と率は WeaponRungModel (Super と共通・2026-11-05)
            bool stepGuardCut = rung.guardCut;
            bool stepUndying = rung.undying;
            int undyingStreak = (int)(ctxRoles?.GetAccumulated(ItemSystem.PassiveSkills.Effects.UndyingBlade.StreakKey) ?? 0f);
            float bashRate = rung.bashRate;
            float bulwarkRate = rung.bulwarkRate;
            bool steadfastReady = rung.steadfast && ctxRoles != null
                && ctxRoles.currentTurn >= (int)ctxRoles.GetAccumulated(ItemSystem.PassiveSkills.Effects.SteadfastStance.ReadyKey);
            bool stepDaybreak = rung.daybreak;
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
                wAtk = wAtk, wDef = wDef, wChg = wChg, specDef = specDef, critMul = critMul,
                eDefNow = eDefNow, myPen = myPen, stepGuardCut = stepGuardCut, stepUndying = stepUndying,
                undyingStreak = undyingStreak, bashRate = bashRate, bulwarkRate = bulwarkRate,
                steadfastReady = steadfastReady, stepDaybreak = stepDaybreak, myShield = myShield,
                enemyMaxHpNow = enemyMaxHpNow, hasVoidStance = hasVoidStance, voidStacks = voidStacks,
                play = play, atkGrowth = atkGrowth,
                diceAtkPct = ctxRoles?.attackDicePct ?? 0f,
            };
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
            }
            // E8 (SuperV3 lookahead2): 次ターンの前提を 1 回だけ組む。 null なら従来どおり (OFF のときは何も変わらない)。
            NextTurnPlan plan = null;
            if (v3.lookahead2 && EffectiveWiringSkill != WiringSkill.Naive && ctxRoles != null && cm?.CurrentEnemy != null
                && (!v3.lookaheadBossOnly || GameLoop.BossIds.IsBoss(cm.CurrentEnemy.id)))
            {
                plan = BuildNextTurnPlan(in st, cm, ctxRoles, runForVoid, k);
                plan.cutoff = LookaheadCutoff(in st, rolls, plan.termKinds, plan.specialLimit, tele.sealedTerminal);
            }
            float ScoreWithBuffers(CombatSystem.DiceTerminal[] w)
                => plan != null ? LookaheadValue(in st, plan, rolls, w) : OptimalUtility(in st, rolls, w);
            float Score(CombatSystem.DiceTerminal[] w) => ScoreWithBuffers(w);

            var assign = new CombatSystem.DiceTerminal[k];
            var bestW = new CombatSystem.DiceTerminal[k];
            float bestU = float.NegativeInfinity;
            if (EffectiveWiringSkill == WiringSkill.Naive)
            {
                // 素朴版: 出目降順の本数分割だけを見る (ADR-0010 以前と同じ探索)。
                for (int a = 0; a <= k; a++)
                    for (int b = 0; b <= k - a; b++)
                    {
                        for (int i = 0; i < k; i++)
                            assign[idx[i]] = (i < a) ? CombatSystem.DiceTerminal.Attack
                                           : (i < a + b) ? CombatSystem.DiceTerminal.Block
                                           : CombatSystem.DiceTerminal.Charge;
                        if (tele.sealedTerminal >= 0)
                        {
                            bool used = false;
                            for (int i = 0; i < k && !used; i++)
                                if ((int)assign[i] == tele.sealedTerminal) used = true;
                            if (used) continue;
                        }
                        float u = Score(assign);
                        if (u > bestU) { bestU = u; System.Array.Copy(assign, bestW, k); }
                    }
            }
            else
            {
                // 端子は 攻撃/ブロック/充電 の 3 種、 特殊端子 (§6-5) を装着していれば 4 種。
                //   **未装着なら第 4 端子は存在しない**ので列挙もしない (3^5=243 / 4^5=1024)。
                int specialLimit = CombatSystem.SpecialTerminals.EquippedLimit(runForVoid);
                int termKinds = specialLimit > 0 ? 4 : 3;
                // 〈綻び〉: 封印された端子は使えない。 **予告に載っているので配線前に読める**。
                //   本体側 (SanitizeWiring) が追い出すので、 候補に残すと
                //   「追い出された後の形」で効用を出せず判断がずれる。
                int sealedTerm = tele.sealedTerminal;
                // 挑戦デバフ〈不器用〉(2026-08-10 リワーク) は端子の種類を縛らない。
                //   N 個以上挿すと**次のターン**その端子が半減するので、 合法性ではなく
                //   効用側で扱う (下の overload ペナルティ)。
                int termCap = 4;

                int total = 1;
                for (int i = 0; i < k; i++) total *= termKinds;
                optimalWiringDecisions++;
                optimalWiringCandidatesEvaluated += total;

                bool runParallel = parallelizeOptimalWiring && plan == null   // 先読みの memo は共有するので直列
                    && total >= System.Math.Max(1, optimalWiringParallelMinCandidates)
                    && System.Environment.ProcessorCount > 1;
                if (runParallel)
                {
                    var scores = new float[total];
                    int maxWorkers = optimalWiringMaxParallelism > 0
                        ? optimalWiringMaxParallelism
                        : System.Math.Min(8, System.Math.Max(1, System.Environment.ProcessorCount - 1));
                    var options = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };
                    Parallel.For(
                        0, total, options,
                        () => new OptimalWiringBuffers(k),
                        (code, _, buffers) =>
                        {
                            scores[code] = float.NegativeInfinity;
                            int t = code;
                            for (int i = 0; i < k; i++)
                            {
                                buffers.assign[i] = (CombatSystem.DiceTerminal)(t % termKinds);
                                t /= termKinds;
                            }

                            int spec = 0, distinct = 0;
                            System.Array.Clear(buffers.termSeen, 0, buffers.termSeen.Length);
                            for (int i = 0; i < k; i++)
                            {
                                int ti = (int)buffers.assign[i];
                                if (ti == (int)CombatSystem.DiceTerminal.Special) spec++;
                                if (!buffers.termSeen[ti]) { buffers.termSeen[ti] = true; distinct++; }
                            }
                            if (spec > specialLimit || distinct > termCap) return buffers;
                            if (sealedTerm >= 0)
                            {
                                for (int i = 0; i < k; i++)
                                    if ((int)buffers.assign[i] == sealedTerm) return buffers;
                            }

                            scores[code] = ScoreWithBuffers(buffers.assign);
                            return buffers;
                        },
                        _ => { });

                    // 評価順に集約し、従来の「最初の最大値」を厳密に維持する。
                    for (int code = 0; code < total; code++)
                    {
                        float u = scores[code];
                        if (u <= bestU) continue;
                        bestU = u;
                        int t = code;
                        for (int i = 0; i < k; i++)
                        {
                            bestW[i] = (CombatSystem.DiceTerminal)(t % termKinds);
                            t /= termKinds;
                        }
                    }
                }
                else
                {
                    for (int code = 0; code < total; code++)
                    {
                        int t = code;
                        for (int i = 0; i < k; i++) { assign[i] = (CombatSystem.DiceTerminal)(t % termKinds); t /= termKinds; }

                        // 実行側 (SanitizeWiring) が畳む前に、 **制約を破る割り当てをそもそも評価しない**。
                        int spec = 0, distinct = 0;
                        _termSeen[0] = _termSeen[1] = _termSeen[2] = _termSeen[3] = false;
                        for (int i = 0; i < k; i++)
                        {
                            int ti = (int)assign[i];
                            if (ti == (int)CombatSystem.DiceTerminal.Special) spec++;
                            if (!_termSeen[ti]) { _termSeen[ti] = true; distinct++; }
                        }
                        if (spec > specialLimit || distinct > termCap) continue;
                        if (sealedTerm >= 0)
                        {
                            bool usesSealed = false;
                            for (int i = 0; i < k && !usesSealed; i++)
                                if ((int)assign[i] == sealedTerm) usesSealed = true;
                            if (usesSealed) continue;
                        }
                        float u = Score(assign);
                        if (u > bestU) { bestU = u; System.Array.Copy(assign, bestW, k); }
                    }
                }
            }
            // E1 (V3 rerollEv) が「この手の最良効用」として読む。 先読みの値は次ターン分を含むので、 今ターンの効用だけに戻す。
            _lastBestU = plan != null ? OptimalUtility(in st, rolls, bestW) : bestU;
            // [計装 2026-11-10] 選んだ配線が防ぎ切りの規則の対象だったか (実際の配線の時だけ MutualWiringPlanPolicy が数える)
            OptimalUtility(in st, rolls, bestW, out var fbo);
            _lastFreeBlock = fbo.freeBlock && !fbo.killsNow; _lastKillsNow = fbo.killsNow; _lastExpTaken = fbo.taken;
            return CombatSystem.WiringPlan.Of(bestW);
        }

        /// <summary>直近の <see cref="MutualWiringPolicy"/> が選んだ配線の効用。 読むだけ・判断には使わない (SuperV3 の実験用)。</summary>
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
            MutualWiringPolicy(rolls, tele);
            return _lastBestU;
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
        /// 費用は `振り直す個数 × この戦闘で何回目か`（attempt が戦闘の通し番号）。 **充電を使い切らない** ──
        /// 充電は端子へ挿さなかったダイスの対価なので、 全部リロールへ流すと配線の判断そのものが消える。</summary>
        private int[] MutualRerollPolicy(int[] dice, CombatSystem.MutualTurnTelegraph tele, int attempt)
        {
            if (dice == null || dice.Length == 0) return null;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx == null) return null;
            bool heavyNow = v3.superDefense && IsHeavyAttack(dice, tele.enemyAttackValue + tele.secondaryAttackValue,
                                                             System.Math.Max(1, CombatManager.Instance?.PlayerMaxHP ?? 1));
            if (ctx.rerollsThisTurn >= (heavyNow ? RerollMaxPerTurn + 1 : RerollMaxPerTurn)) return null;

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
            CombatSystem.YachtRoles.Evaluate(dice, ctx.playerDiceFaceIdx, ctx.equippedFaceTiers, formed);
            if (formed.Count > 0) return null;

            var all = new List<int>(k);
            int rerollSum = 0;
            for (int i = 0; i < k; i++) { all.Add(i); rerollSum += dice[i]; }

            // E1 (SuperV3 rerollEv): 平均との比較ではなく、 配線の効用の期待値で決める
            if (v3.rerollEv)
            {
                int freeLeftE = ctx.currentBuffs.TryGetValue(CombatSystem.YachtRoleEffects.FreeRerollKey, out float ffE) ? (int)ffE : 0;
                int costE = freeLeftE > 0 ? 0 : all.Count * System.Math.Max(1, attempt);
                if (ctx.GetCharge() - RerollChargeReserve < costE) return null;
                var faceVals = (faces != null && faces.Length > 0) ? faces : new[] { 1, 2, 3, 4, 5, 6 };
                float cur = BestUtilityOf(dice, tele);
                float ev = ExpectedBestUtilityAfterReroll(k, faceVals, tele);
                // 充電 1 の効用は、 配線評価の充電項 (wChg 0.6 × 2) と同じ 1.2。 ただし**上限 (ChargeMax) に近いほど安い**:
                //   上限に張り付いた充電は使わなければ捨てるだけなので、 満タンでは費用 0。 (第 1 版は一律 1.2 で、
                //   リロールが 3 分の 1 に減り 5 層のボス戦が 36.7% → 65.1% の死亡に悪化した。 長い戦闘では充電が溜まる。)
                const float ChargeUtilityPerUnit = 1.2f;
                float scarcity = 1f - UnityEngine.Mathf.Clamp01(ctx.GetCharge() / (float)CombatSystem.CombatManager.ChargeMax);
                return ev - ChargeUtilityPerUnit * scarcity * costE > cur ? all.ToArray() : null;
            }

            // S2: 大きい攻撃の予告なら、 止めきれない手は平均近くでも振り直し、 充電の予備も使う
            if (heavyNow && rerollSum < tele.enemyAttackValue && rerollSum < meanFace * all.Count * 1.15f)
            {
                int freeH = ctx.currentBuffs.TryGetValue(CombatSystem.YachtRoleEffects.FreeRerollKey, out float ffH) ? (int)ffH : 0;
                int costH = freeH > 0 ? 0 : all.Count * System.Math.Max(1, attempt);
                return ctx.GetCharge() >= costH ? all.ToArray() : null;
            }

            // ② 平均を下回る手だけ振り直す
            if (rerollSum >= meanFace * all.Count) return null;

            int freeLeft = ctx.currentBuffs.TryGetValue(CombatSystem.YachtRoleEffects.FreeRerollKey, out float ff)
                ? (int)ff : 0;
            int cost = freeLeft > 0 ? 0 : all.Count * System.Math.Max(1, attempt);
            if (ctx.GetCharge() - RerollChargeReserve < cost) return null;
            return all.ToArray();
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

        /// <summary>ラン開始前に宣言する特殊端子 (§6-5) をペルソナから決める。
        ///
        /// **一様ランダムにしない。** 端子は「そのビルドで何をしたいか」の宣言なので、
        /// ビルドと無関係に配ると全ペルソナの平均しか出ず、 どの端子が効くのか分からなくなる。
        /// ペルソナが立っていないラン (RawTier) だけ一様抽選にして、 11 種の網羅を担保する。</summary>
        private string PickSpecialTerminal()
        {
            var K = CombatSystem.SpecialTerminalKind.None;
            switch (_currentPersona)
            {
                case BuildPersona.Charge:   K = CombatSystem.SpecialTerminalKind.Battery;     break;
                case BuildPersona.Shield:   K = CombatSystem.SpecialTerminalKind.FullGuard;   break;
                case BuildPersona.Berserk:  K = CombatSystem.SpecialTerminalKind.Deathwish;   break;
                case BuildPersona.Bludgeon: K = CombatSystem.SpecialTerminalKind.HeavyStrike; break;
                case BuildPersona.Crit:     K = CombatSystem.SpecialTerminalKind.Aim;         break;
                case BuildPersona.Bleed:    K = CombatSystem.SpecialTerminalKind.Bleed;       break;
                case BuildPersona.Poison:   K = CombatSystem.SpecialTerminalKind.VitalPoint;  break;
                case BuildPersona.Rinkai:   K = CombatSystem.SpecialTerminalKind.Foundry;     break;
                case BuildPersona.Standard: K = CombatSystem.SpecialTerminalKind.HeavyStrike; break;
            }
            if (K != CombatSystem.SpecialTerminalKind.None)
                return CombatSystem.SpecialTerminals.Get(K)?.id ?? "";

            var pool = CombatSystem.SpecialTerminals.All;
            return pool[GameLoop.GameRng.RangeAuto("AutoRunner.specialTerm", 0, pool.Count)].id;
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
        /// 切替時の解除効果(次T ダイス+1, 撃破済みなら会心+9)も自動で享受する。</summary>
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
        /// <summary>回復量 (最大HP 比)。 <c>ItemIds.ConsHealFamily</c> の 4 段。 昇順で持つこと。</summary>
        private static readonly (string id, float pct)[] HealTiers =
        {
            ("小回復薬", 0.25f), ("回復薬", 0.40f),
            ("上回復薬", 0.60f), ("完全回復薬", 1.00f),
        };

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

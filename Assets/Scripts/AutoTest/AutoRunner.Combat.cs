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
                    UseFirst(run, "惜別の護符", "銀の護符", "鉄の護符", "木の護符");
                }
                else
                {
                    UseFirst(run, "燐の油", "鬼火の油");
                    UseFirst(run, "鉄の護符", "木の護符");
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
            else if (effectiveSkill == WiringSkill.Super && !useOptimalRoutine)
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
            => MutualWiringPolicy(rolls, tele);

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
            const int HorizonCapTurns = 12;
            // 1 ターン引き延ばすことのコスト。 既存項の桁 (wAtk×effDmg が ~25〜100、
            // wDef×min(block,enemyAtk) が ~3〜40) に対し、 ttk が 1 増える不利がブロック 1 本の
            // 利得を上回るように置いた初期値。
            const float TimeCostPerTurn = 4.0f;

            // ペルソナで端子重みを調整 (2026-07-15): 該当ビルドが配線を意識して立ち回る
            float wAtk = 1.0f, wDef = 1.0f, wChg = 0.6f;
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

            var play = BossPlaybook.Get(cm?.CurrentEnemy?.id);
            wAtk *= play.atkBias;
            wDef *= play.defBias;
            wChg *= play.chgBias;
            // ボス側 HP 比 (Phase 判定に必要): CombatManager から取得 (敵ID・敵HP・敵MaxHP)
            float bossHpRatio = 1f;
            if (cm != null && cm.CurrentEnemy != null && cm.CurrentEnemy.maxHP > 0)
                bossHpRatio = (float)cm.EnemyHP / cm.CurrentEnemy.maxHP;
            if (play.IsOmenTurn(tele.turn, bossHpRatio))
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
            // ===== 割り当ての列挙 =====
            //   端子は 攻撃/ブロック/充電 (+特殊) の全列挙 (3^k)。 ADR-0011 でダイスは 3 個になったので 27 通り。
            //   役は配線に依存せず振った出目だけで決まるので、 配線の効用には役を入れない。
            var ctxRoles = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;

            // 会心倍率は「束(攻)＝会心確定」「奇(攻)＝会心率+25%」の価値見積もりに要る。
            float critMul = ctxRoles?.criticalMultiplier ?? 3f;
            var specDef = CombatSystem.SpecialTerminals.Equipped(runForVoid);

            float ScoreWithBuffers(CombatSystem.DiceTerminal[] w)
            {
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

                int dmg = atkBase + aSum;
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

                // 処刑人の烙印: 攻撃終了時に HP が最大の 20% 以下なら 9999。
                if (tele.executeArmed && (myHp - taken) <= UnityEngine.Mathf.CeilToInt(myMaxHp * 0.20f))
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

                float u = wAtk * System.Math.Min(effDmg, enemyHp)
                        + wDef * System.Math.Min(block, enemyAtk)
                        + specFlat;
                if (effDmg >= enemyHp) u += 50f + enemyAtk; // 今ターン撃破 = 敵攻撃が消える
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
                u -= TimeCostPerTurn * ttk;

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
                u += wChg * c * 2f;                          // 充電価値 (リロールの原資)
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
                return u;
            }

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

                bool runParallel = parallelizeOptimalWiring
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
            return CombatSystem.WiringPlan.Of(bestW);
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
            if (ctx.rerollsThisTurn >= RerollMaxPerTurn) return null;

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

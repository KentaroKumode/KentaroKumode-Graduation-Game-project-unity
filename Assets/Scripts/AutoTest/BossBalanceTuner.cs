using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using CombatSystem;
using DeathCause = InventorySystem.PassiveSkills.DeathCause;

namespace AutoTest
{
    /// <summary>
    /// L3: ボス難易度オートチューナー (苦戦診断 + 平均出目アンカー)。
    ///
    /// 対象は **6層以前のボスのみ** (7層=覚者連戦は調整しない)。
    /// バッチ毎に各ボスの戦闘勝率を目標へ寄せるが、 苦戦の種類を診断して原因に対応する数値だけ動かす。
    ///
    /// **調整は隠し倍率(×1.xx)ではなく、 各パッシブの具体的な数値そのもの** を増減する (BossParam)。
    /// 「断罪ダイス上乗せ 10→8」のようにゲーム内の実数値が変化し、 最終的に目標勝率へ落ち着く。
    ///
    /// 苦戦診断:
    ///   ・断罪(Judgment)で即死 →
    ///       見切りロールに負けている(ロール勝率低) → 断罪ダイス上乗せ(JudgmentDice)を下げる
    ///       ロールには勝てているが負けた一撃が致命 → 断罪係数(JudgmentCoefBase)を下げる
    ///   ・反射/爆ぜ火/審判の炎 → それぞれ ReflectPct / BurstDamage / ChipCap
    ///   ・ジリ貧(長期戦) → 灰塵の鎧の軽減量(RegenReduction)
    ///   ・ロール連敗 / その他 / 簡単すぎ → Dice (ボス素ダイスの期待値 = プレイヤー平均出目 + offset)
    ///       期待値が [1,25] を超える要求は HP(絶対値) に回す。
    ///
    /// ボス難易度は全プロファイル共通。 調整は基準プロファイル(デバフ無し)でのみ行う。
    /// </summary>
    public static class BossBalanceTuner
    {
        public const int MinSampleBoss = 100;  // ボスの遭遇がこの数未満なら調整スキップ

        public const float KStepE = 5.0f;          // Dice期待値の比例ゲイン
        public const float MaxEStepPerBatch = 1f; // Dice期待値の1バッチ最大変化
        public const float OffsetMax = 20f;        // diceOffset のクランプ
        public const float DeadZone = 0.03f;       // |err| がこれ未満は無調整

        public const float KStepHp = 0.5f;          // HP(絶対値)の比例ゲイン (基準HPに対する割合)
        public const float MaxHpStepFrac = 0.06f;   // HPの1バッチ最大変化 (基準HPの割合)

        public const float CauseDominanceFrac = 0.40f; // 総被ダメの40%以上を占めるソース = 支配的
        public const float RollLossThreshold  = 0.50f; // 断罪Tで見切りロールに勝てているかの境
        public const float AttritionTurns     = 15.0f;  // 平均ターンがこれ以上 = ジリ貧

        // 7層の目標ロール勝率は BossTuning.TargetRollWinRate(id) でボスごとに設定 (これを超え張り付くと真我↑)
        public const float RollPinDeadZone    = 0.02f;  // ロール勝率が目標±この幅内なら動かさない
        // スタンス別 ロール勝率レンジは BossTuning.Strong/WeakRollWinRange(id) で per-boss 設定。
        // 強ロール時=ダイス/固有面、 弱ロール時=弱ロール比 で各レンジに収める。
        public const int RollSampleMin = 30; // スタンス別roll-win制御に必要な そのスタンスの最小ターン数

        /// <summary>ボス戦闘勝率の目標 (フロア 1-6)。 7層は調整対象外。</summary>
        private static readonly float[] FloorTarget = { 0f, 0.99f, 0.96f, 0.95f, 0.91f, 0.88f, 0.5f };

        private class BossAgg
        {
            public int enc, wins;
            public long tWin, tLoss, tDraw, turns;
            public long rollSum; public int rollCount;
            public Dictionary<DeathCause, int> causes = new Dictionary<DeathCause, int>(); // 死亡時の一撃(参考/ログ用)
            public Dictionary<DeathCause, long> dmg = new Dictionary<DeathCause, long>();   // 戦闘総被ダメのソース別累計(診断の主軸)
            public long dmgTotal;
            // スタンス別 ボスroll-win (強ロール時/弱ロール時で別レンジ制御)。
            public long strongTurns, strongBossWins, weakTurns, weakBossWins;
            public float WinRate => enc > 0 ? (float)wins / enc : 0f;
            public float RollWinRate { get { long t = tWin + tLoss + tDraw; return t > 0 ? (float)tWin / t : 1f; } }
            public float AvgTurns => enc > 0 ? (float)turns / enc : 0f;
            public float AvgPlayerRoll => rollCount > 0 ? (float)rollSum / rollCount : 0f;
            public int Losses => enc - wins;
            /// <summary>強ロールスタンス時に ボスがロール勝ちした割合 (-1=サンプル無し)。</summary>
            public float StrongBossRollWin => strongTurns > 0 ? (float)strongBossWins / strongTurns : -1f;
            /// <summary>弱ロールスタンス時に ボスがロール勝ちした割合 (-1=サンプル無し)。</summary>
            public float WeakBossRollWin => weakTurns > 0 ? (float)weakBossWins / weakTurns : -1f;

            public void AddDamage(DeathCause c, int amount)
            {
                if (amount <= 0) return;
                dmg.TryGetValue(c, out long v); dmg[c] = v + amount; dmgTotal += amount;
            }
            /// <summary>総被ダメに占める割合が最大のソースと、 その割合。</summary>
            public (DeathCause cause, float share) DominantDamage()
            {
                DeathCause best = DeathCause.Normal; long bestV = -1;
                foreach (var kv in dmg) if (kv.Value > bestV) { bestV = kv.Value; best = kv.Key; }
                float share = dmgTotal > 0 ? (float)bestV / dmgTotal : 0f;
                return (best, share);
            }
        }

        public static void AssessAndAdjust(IList<AutoRunner.RunRec> recs, bool debuffOn, string learningRoot, int batchIndex = 0)
        {
            try
            {
                if (recs == null || recs.Count == 0) return;

                // ボス難易度は共通。 基準プロファイル(デバフ無し)のみ調整、 他は継承。
                if (!MetaProfileHelper.CurrentIsBaseline)
                {
                    BossTuning.Reload();
                    Debug.Log($"[BossBalanceTuner] 非基準 ({MetaProfileHelper.CurrentSuffix}) → 共有ボス係数を継承 (調整なし)");
                    return;
                }
                BossTuning.Reload(learningRoot);

                // ボス別に戦闘集計 (7層も含む。 7層は真我のみ調整するためロールデータが必要)
                var byBoss = new Dictionary<string, BossAgg>();
                foreach (var r in recs)
                {
                    if (r?.combats == null) continue;
                    foreach (var c in r.combats)
                    {
                        if (c == null || string.IsNullOrEmpty(c.enemyId) || !BossTuning.IsBoss(c.enemyId)) continue;
                        if (!byBoss.TryGetValue(c.enemyId, out var a)) { a = new BossAgg(); byBoss[c.enemyId] = a; }
                        a.enc++;
                        if (c.won) a.wins++;
                        a.tWin += c.tWin; a.tLoss += c.tLoss; a.tDraw += c.tDraw; a.turns += c.turns;
                        a.rollSum += c.playerRollSum; a.rollCount += c.playerRollCount;
                        if (!c.won)
                        {
                            a.causes.TryGetValue(c.deathCause, out int n);
                            a.causes[c.deathCause] = n + 1;
                        }
                        // 支配率診断: 勝敗問わず全戦闘の被ダメをソース別に積む (どの機構が総ダメの何割か)。
                        if (c.playerDamageBySource != null)
                            foreach (var kv in c.playerDamageBySource) a.AddDamage(kv.Key, kv.Value);
                        // スタンス別 ボスroll-win 集計。
                        a.strongTurns += c.strongRollTurns; a.strongBossWins += c.strongRollBossWins;
                        a.weakTurns += c.weakRollTurns;   a.weakBossWins += c.weakRollBossWins;
                    }
                }

                var changes = new List<string>();
                foreach (var kv in byBoss)
                {
                    string id = kv.Key;
                    var a = kv.Value;
                    if (a.enc < MinSampleBoss) continue;
                    if (!BossTuning.BossEnabled(id)) continue; // ボス別トグルで無効化されていれば一切調整しない

                    // 7層は通常の難易度調整(ダイス/HP/機構)を行わない。 唯一「真我」のみ、
                    // プレイヤーのロール勝率が90%張り付き時にダイス上限を超えて引き締める。
                    // ただし妙覚はサドンデス専用のため真我も無効・学習停止 (一切調整しない)。
                    if (BossTuning.FloorOf(id) >= 7)
                    {
                        if (!BossTuning.IsMyokaku(id)) AdjustTrueSelf(id, a, changes);
                        continue;
                    }

                    // 難度(クリア率)を目標へ。 **勝率が目標帯(DeadZone)内なら一切調整しない**
                    //   （ロール勝率調整はクリア率調整の手段であって目的ではない＝勝率OKなのにロール勝率を
                    //     しきい値へ寄せにいくことはしない）。
                    float target = TargetFor(id);
                    float err = target - a.WinRate; // >0 = 難しすぎ
                    if (Mathf.Abs(err) < DeadZone) continue;

                    // レバー優先順: ① スキル(各ボスの特色) → ② ダイス/固有面(強ロールroll-winレンジを割らない範囲のみ) →
                    //   ③ 弱ロール比(弱ロールroll-winレンジを割らない範囲のみ) → ④ 攻撃力(高火力倍率) → ⑤ HP。
                    //   ②③はクリア率errで動かすが、 ロール勝率レンジを割る方向なら不可(巻き戻し)→次レバーへ。
                    var candidates = (err > 0f) ? DiagnoseStruggle(id, a) : DiagnoseEasy(id); // スキル系paramのみ
                    bool applied = false;
                    foreach (var cand in candidates)
                    {
                        if (applied) break;
                        if (cand.HasValue && BossTuning.Enabled(cand.Value))
                            applied = AdjustParam(id, cand.Value, err, a, changes, false); // 飽和なら false=次へ
                    }
                    if (!applied) applied = AdjustStrongDiceForClear(id, err, a, changes); // ダイス(強ロールレンジで gate)
                    if (!applied) applied = AdjustWeakRatioForClear(id, err, a, changes);  // 弱ロール比(弱ロールレンジで gate)
                    if (!applied && BossTuning.Enabled(BossParam.StanceAtkMult))
                        applied = AdjustParam(id, BossParam.StanceAtkMult, err, a, changes, true); // 攻撃力(高火力倍率)
                    if (!applied) AdjustHpOnly(id, err, a, changes, true);                 // 最終受け皿: HP
                }

                BossTuning.Save(learningRoot, batchIndex);
                Debug.Log($"[BossBalanceTuner] 調整 {changes.Count}件 / {BossTuning.Summary()}");

                // 変更が無いバッチも「変更なし」として必ず追記する (調整が走った記録を残す)。
                var sb = new StringBuilder();
                sb.AppendLine($"## {BotJudgmentLog.Now()} — L3 ボス難易度 自動調整 (6層以前・苦戦診断・実数値)");
                sb.AppendLine();
                if (changes.Count > 0)
                    foreach (var c in changes) sb.AppendLine(c);
                else
                    sb.AppendLine("*(変更なし — 全ボスが目標帯内)*");
                sb.AppendLine();
                sb.AppendLine("---");
                BotJudgmentLog.Append(sb.ToString());
            }
            catch (Exception e) { Debug.LogWarning($"[BossBalanceTuner] 例外: {e.Message}\n{e.StackTrace}"); }
        }

        private static float TargetFor(string id)
        {
            int f = BossTuning.FloorOf(id);
            return (f >= 1 && f <= 6) ? FloorTarget[f] : 0.5f;
        }

        /// <summary>苦戦診断。 動かすレバーを **優先順位つき** で返す。 先頭=本命、 以降=次点。
        /// null は Dice(ボス素ダイス)/HP を意味する万能フォールバック (常に末尾に置く)。
        /// チューナーは有効(手動トグルon)な最初の候補を採用する。</summary>
        private static List<BossParam?> DiagnoseStruggle(string id, BossAgg a)
        {
            var list = new List<BossParam?>();

            // ① **戦闘総被ダメに占める割合** が支配的なメカニズムを特定 (キル時の一撃ではなく被ダメ支配率)。
            //    Normal(通常ロール被ダメ)は機構レバーが無いので除外し、 特殊機構の中で最大シェアを見る。
            DeathCause dom = DeathCause.Normal; long domV = 0;
            foreach (var kv in a.dmg)
                if (kv.Key != DeathCause.Normal && kv.Key != DeathCause.Other && kv.Value > domV)
                { dom = kv.Key; domV = kv.Value; }
            float domShare = a.dmgTotal > 0 ? (float)domV / a.dmgTotal : 0f;

            if (domV > 0 && domShare >= CauseDominanceFrac)
            {
                switch (dom)
                {
                    case DeathCause.Judgment:
                        // 多角診断: 断罪Tに「見切りロール」に負けて即死 → ダイス上乗せが本命 (ロール勝率が低い)。
                        //           ロールには勝てているのに稀な敗北が致命 → 係数が本命。 もう一方を次点に。
                        if (a.RollWinRate < RollLossThreshold)
                        { list.Add(BossParam.JudgmentDice); list.Add(BossParam.JudgmentCoefBase); }
                        else
                        { list.Add(BossParam.JudgmentCoefBase); list.Add(BossParam.JudgmentDice); }
                        break;
                    case DeathCause.Reflect:
                        // ボスが実際に持つ反射スキルに対応する実数値を選ぶ。
                        if (HasSkill(id, "MirrorTwinsResponse")) list.Add(BossParam.MirrorReflectCap); // 4層 鏡の双子
                        if (HasSkill(id, "AwakenedP3Mirror"))    list.Add(BossParam.ReflectPct);       // 覚者・鏡映(%)
                        break;
                    case DeathCause.Burst:   list.Add(BossParam.BurstDamage); break;
                    case DeathCause.Chip:
                        // チップ/DOTの本命はボスが持つ該当スキルの上限。 底に達したら威圧の脅威(勝利時の削り)を次点で下げる。
                        if (HasSkill(id, "JudgmentFlames"))   list.Add(BossParam.ChipCap);         // 5層 審判の炎
                        if (HasSkill(id, "MiasmaCorrosion"))  list.Add(BossParam.PoisonStackCap);  // 3層 毒の侵蝕
                        if (HasSkill(id, "IntimidatePlus"))   list.Add(BossParam.IntimidateThreat);
                        break;
                    // SuddenDeath は7層専用 → ここには来ない
                }
            }
            // ② 長期戦 = ジリ貧 (灰塵の鎧/サステイン)
            else if (a.AvgTurns >= AttritionTurns) list.Add(BossParam.RegenReduction);

            // ③ ボス固有のスキルレバー (死因に紐づかない分も難度レバーとして列挙)。 ダイスは難度に使わない(ロール勝率帯担当)。
            AppendBossLevers(id, list);
            return list; // この後 呼び出し側で 攻撃力倍率 → HP の順にフォールバック
        }

        /// <summary>簡単すぎ(err&lt;0)時の難化候補。 **スキルレバーを優先**。 この後 攻撃力倍率→HP へフォールバック。
        /// ダイスは難度に使わない(ロール勝率帯の維持専用)＝「全ボスがダイス5d9に張り付く」収束を避け、 特色を機構で出す。</summary>
        private static List<BossParam?> DiagnoseEasy(string id)
        {
            var list = new List<BossParam?>();
            AppendBossLevers(id, list); // スキルを上げて難化 (AdjustParam が err<0 で増加方向)
            return list;
        }

        /// <summary>そのボスが実際に持つスキルに対応する調整レバーを (有効かつ未追加のものだけ) 末尾に積む。</summary>
        private static void AppendBossLevers(string id, List<BossParam?> list)
        {
            void Add(string skill, BossParam p)
            {
                if (HasSkill(id, skill) && BossTuning.Enabled(p) && !list.Contains((BossParam?)p))
                    list.Add(p);
            }
            Add("GoblinKingsCall",     BossParam.GoblinCall);        // 2層
            Add("MiasmaCorrosion",     BossParam.PoisonStackCap);    // 3層
            Add("MirrorTwinsResponse", BossParam.MirrorReflectCap);  // 4層
            Add("JudgmentFlames",      BossParam.ChipCap);           // 5層 審判の炎
            Add("JudgmentBlaze",       BossParam.JudgmentDice);      // 6層 業火の断罪 (見切りダイス)
            Add("JudgmentBlaze",       BossParam.JudgmentCoefBase);  // 6層 業火の断罪 (係数)
            Add("AshArmor",            BossParam.RegenReduction);    // 6層 灰塵の鎧
            Add("IntimidatePlus",      BossParam.IntimidateThreat);  // 威圧+ (共通・最後=脅威は副次)
        }

        /// <summary>そのボスが指定 internalName のパッシブを持つか (enemies.json ベース)。</summary>
        private static bool HasSkill(string id, string internalName)
        {
            var e = CombatSystem.EnemyDatabase.Get(id);
            if (e?.passiveSkills == null) return false;
            foreach (var p in e.passiveSkills)
                if (p != null && p.internalName == internalName) return true;
            return false;
        }

        /// <summary>具体パラメータ (機構の実数値) を1ステップ調整。 err>0(難)→値を下げて易化。
        /// 戻り値=実際に値が変化したか。 変化なし(範囲飽和)なら呼び出し側は次の候補レバーへフォールスルーする。</summary>
        private static bool AdjustParam(string id, BossParam param, float err, BossAgg a, List<string> changes, bool fallback = false)
        {
            var meta = BossTuning.Meta(param);
            var knob = BossTuning.GetOrCreate(BossTuning.KeyFor(id));
            float before = BossTuning.GetParam(knob, param);
            float step = Mathf.Clamp(meta.gain * err, -meta.maxStep, meta.maxStep);
            BossTuning.SetParam(knob, param, before - step); // err>0(難)→下げる
            float after = BossTuning.GetParam(knob, param);
            if (Mathf.Abs(after - before) > 0.001f)
            {
                changes.Add($"- **{id}** 勝率{a.WinRate:P0}→目標{TargetFor(id):P0} [{DiagText(a)}]: "
                    + $"{ParamLabel(param)}{(fallback ? "(次点)" : "")} {Fmt(before)}→**{Fmt(after)}**");
                return true;
            }
            return false; // 範囲飽和 = 動かせなかった → 次候補へ
        }

        /// <summary>Dice を期待値で調整。 ボス期待値 = プレイヤー平均出目 + diceOffset。 範囲外は HP(絶対値) に回す。
        /// headOverride: ログ先頭の文言を差し替え (ロール勝率帯制御から呼ぶ時など)。 allowHp=false で HP受け皿を抑止。</summary>
        private static bool AdjustDice(string id, float err, BossAgg a, List<string> changes, bool fallback = false,
                                       string headOverride = null, bool allowHp = true)
        {
            if (!BossTuning.TuneDice && !(BossTuning.TuneHp && allowHp)) return false; // どちらも無効なら何もしない
            float avgRoll = a.AvgPlayerRoll;
            if (avgRoll <= 0f) return false; // ロールデータなし

            var knob = BossTuning.GetOrCreate(BossTuning.KeyFor(id));
            string head = headOverride ?? $"勝率{a.WinRate:P0}→目標{TargetFor(id):P0}";
            string line = $"- **{id}** {head} [{DiagText(a)} 平均出目{avgRoll:F1}]:{(fallback ? " (次点)" : "")}";
            bool changed = false;
            float rawE = avgRoll + knob.diceOffset;

            if (BossTuning.TuneDice)
            {
                float stepE = Mathf.Clamp(KStepE * err, -MaxEStepPerBatch, MaxEStepPerBatch);
                knob.diceOffset = Mathf.Clamp(knob.diceOffset - stepE, -OffsetMax, OffsetMax); // err>0(難)→ボス弱体
                knob.diceTuned = true;
                rawE = avgRoll + knob.diceOffset;
                float newE = Mathf.Clamp(rawE, BossTuning.DiceEMin, BossTuning.DiceEMax);
                float eBefore = BossTuning.CurrentDiceExpected(id);
                BossTuning.SetDiceExpected(knob, newE);
                var (cnt, faces) = BossTuning.BestDiceConfig(newE);
                line += $" Dice E{eBefore:F1}→**{newE:F1}** ({cnt}d{faces}, offset{knob.diceOffset:+0.0;-0.0})";
                changed = true;
            }

            // HP(絶対値): Dice有効時は期待値が範囲外に飽和したときの受け皿。
            //             Dice無効時は HP 単独で勝率誤差を吸収する。
            bool saturated = rawE > BossTuning.DiceEMax + 0.01f || rawE < BossTuning.DiceEMin - 0.01f;
            if (allowHp && BossTuning.TuneHp && (!BossTuning.TuneDice || saturated))
            {
                int baseHp = BossTuning.BaseMaxHp(id);
                if (baseHp > 0)
                {
                    int hpBefore = BossTuning.MaxHpFor(id);
                    int hpStep = Mathf.RoundToInt(baseHp * Mathf.Clamp(KStepHp * err, -MaxHpStepFrac, MaxHpStepFrac));
                    int hpAfter = Mathf.Clamp(hpBefore - hpStep, // err>0(難)→HP減 / err<0(易)→HP増
                        Mathf.RoundToInt(baseHp * BossTuning.MinHpFrac),
                        Mathf.RoundToInt(baseHp * BossTuning.MaxHpFrac));
                    if (hpAfter != hpBefore)
                    {
                        knob.maxHP = hpAfter;
                        line += changed ? $" + Dice飽和→HP {hpBefore}→**{hpAfter}**" : $" HP {hpBefore}→**{hpAfter}**";
                        changed = true;
                    }
                }
            }
            if (changed) changes.Add(line);
            return changed;
        }

        /// <summary>固有面ダイス(署名ダイス)ボスの調整。 期待値スカラではなく **面の各値そのもの** を学習する。
        /// err&gt;0(難しすぎ)→ボス弱体=高い面を下げて天井を削る / err&lt;0(易しすぎ)→ボス強化=低い面を上げて床を持ち上げる。
        /// 各面は [SignatureFaceMin, SignatureFaceMax] にクランプ。 全面が飽和(全9/全1)したら HP(絶対値) に回す。
        /// 隠し倍率は使わず、 面の実数値を直接動かす(正本ポリシー準拠)。</summary>
        private static bool AdjustSignatureDice(string id, float err, BossAgg a, List<string> changes, bool fallback = false,
                                                string headOverride = null, bool allowHp = true)
        {
            var knob = BossTuning.GetOrCreate(BossTuning.KeyFor(id));
            int[] faces = BossTuning.SignatureFaces(id);
            if (!BossTuning.IsValidSignature(faces)) return false;

            float eBefore = BossTuning.SignatureExpected(faces);
            int[] before = (int[])faces.Clone();

            // このバッチで動かす面の数: 1面±1 = EV 変化 SignatureDiceCount/SignatureFaceCount (≈0.83)。
            float stepE = Mathf.Clamp(KStepE * Mathf.Abs(err), 0f, MaxEStepPerBatch);
            float evPerBump = (float)BossTuning.SignatureDiceCount / BossTuning.SignatureFaceCount;
            int nBumps = Mathf.Clamp(Mathf.RoundToInt(stepE / Mathf.Max(0.01f, evPerBump)), 1, BossTuning.SignatureFaceCount);

            bool weaken = err > 0f; // 難しすぎ→弱体
            int applied = 0;
            for (int b = 0; b < nBumps; b++)
            {
                int idx = -1;
                if (weaken)
                {
                    // 最も高い、 まだ下げられる面(>min)を1下げる(天井を削る)。
                    for (int i = faces.Length - 1; i >= 0; i--)
                        if (faces[i] > BossTuning.SignatureFaceMin) { idx = i; break; }
                    if (idx < 0) break;
                    faces[idx] -= 1;
                }
                else
                {
                    // 最も低い、 まだ上げられる面(<max)を1上げる(床を持ち上げる)。
                    for (int i = 0; i < faces.Length; i++)
                        if (faces[i] < BossTuning.SignatureFaceMax) { idx = i; break; }
                    if (idx < 0) break;
                    faces[idx] += 1;
                }
                applied++;
                BossTuning.ClampSignature(faces); // 都度ソートして次の最小/最大を正しく拾う
            }

            string head = headOverride ?? $"勝率{a.WinRate:P0}→目標{TargetFor(id):P0}";
            string line = $"- **{id}** {head} [{DiagText(a)}]:{(fallback ? " (次点)" : "")}";

            if (applied > 0)
            {
                knob.diceFaces = faces;
                knob.diceTuned = true;
                float eAfter = BossTuning.SignatureExpected(faces);
                line += $" 固有面 [{string.Join(",", before)}]→**[{string.Join(",", faces)}]** (E{eBefore:F1}→{eAfter:F1})";
                changes.Add(line);
                return true;
            }

            // 面が飽和(全9 or 全1) → HP(絶対値)で勝率誤差を吸収。 ※ロール帯制御からの呼び出し(allowHp=false)では抑止。
            if (allowHp && BossTuning.TuneHp)
            {
                int baseHp = BossTuning.BaseMaxHp(id);
                if (baseHp > 0)
                {
                    int hpBefore = BossTuning.MaxHpFor(id);
                    int hpStep = Mathf.RoundToInt(baseHp * Mathf.Clamp(KStepHp * err, -MaxHpStepFrac, MaxHpStepFrac));
                    int hpAfter = Mathf.Clamp(hpBefore - hpStep, // err>0(難)→HP減 / err<0(易)→HP増
                        Mathf.RoundToInt(baseHp * BossTuning.MinHpFrac),
                        Mathf.RoundToInt(baseHp * BossTuning.MaxHpFrac));
                    if (hpAfter != hpBefore)
                    {
                        knob.maxHP = hpAfter;
                        line += $" 固有面飽和→HP {hpBefore}→**{hpAfter}**";
                        changes.Add(line);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>クリア率errでダイス/固有面を動かす。 ただし**強ロールroll-winレンジを割る方向なら不可**(巻き戻し→false)。
        /// err&gt;0(難)→ダイス弱体(強ロールroll-win↓)＝floor割れなら不可 / err&lt;0(易)→強化(↑)＝ceil超なら不可。 HP受け皿は使わない。</summary>
        private static bool AdjustStrongDiceForClear(string id, float err, BossAgg a, List<string> changes)
        {
            if (!BossTuning.TuneDice) return false;
            bool harden = err < 0f; // 易しすぎ→ボス強化(ダイス↑)
            // ゲート: そのスタンスのサンプルが十分あるときだけ、 レンジを割る方向を禁止する。
            if (a.strongTurns >= RollSampleMin)
            {
                float rwr = a.StrongBossRollWin;
                var range = BossTuning.StrongRollWinRange(id);
                if (harden && rwr >= range.ceil) return false;   // これ以上強めると勝ちすぎ → 別レバーへ
                if (!harden && rwr <= range.floor) return false; // これ以上弱めると負けすぎ → 別レバーへ
            }
            // err をそのまま渡す (AdjustSignatureDice/AdjustDice: err>0=弱体, err<0=強化)。 allowHp=false。
            if (BossTuning.IsSignatureDiceBoss(id)) return AdjustSignatureDice(id, err, a, changes, false, null, false);
            return AdjustDice(id, err, a, changes, false, null, false);
        }

        /// <summary>クリア率errで弱ロール比(WeakRollRatio)を動かす。 ただし**弱ロールroll-winレンジを割る方向なら不可**。
        /// 比が高い=弱ロールが強い。 harden(err&lt;0)→比↑(ceil超なら不可) / ease(err&gt;0)→比↓(floor割れなら不可)。</summary>
        private static bool AdjustWeakRatioForClear(string id, float err, BossAgg a, List<string> changes)
        {
            if (!BossTuning.Enabled(BossParam.WeakRollRatio)) return false;
            bool harden = err < 0f;
            if (a.weakTurns >= RollSampleMin)
            {
                float rwr = a.WeakBossRollWin;
                var range = BossTuning.WeakRollWinRange(id);
                if (harden && rwr >= range.ceil) return false;   // これ以上強めると勝ちすぎ
                if (!harden && rwr <= range.floor) return false; // これ以上弱めると負けすぎ
            }
            // AdjustParam(before - step, step=gain*err): err<0(harden)→step<0→比↑ / err>0(ease)→比↓。 正しい。
            return AdjustParam(id, BossParam.WeakRollRatio, err, a, changes, true);
        }

        /// <summary>HP(絶対値)のみを1ステップ調整 (難度の最終受け皿)。 戻り値=変化したか。</summary>
        private static bool AdjustHpOnly(string id, float err, BossAgg a, List<string> changes, bool fallback)
        {
            if (!BossTuning.TuneHp) return false;
            int baseHp = BossTuning.BaseMaxHp(id);
            if (baseHp <= 0) return false;
            int hpBefore = BossTuning.MaxHpFor(id);
            var knob = BossTuning.GetOrCreate(BossTuning.KeyFor(id));
            int hpStep = Mathf.RoundToInt(baseHp * Mathf.Clamp(KStepHp * err, -MaxHpStepFrac, MaxHpStepFrac));
            int hpAfter = Mathf.Clamp(hpBefore - hpStep, // err>0(難)→HP減 / err<0(易)→HP増
                Mathf.RoundToInt(baseHp * BossTuning.MinHpFrac),
                Mathf.RoundToInt(baseHp * BossTuning.MaxHpFrac));
            if (hpAfter == hpBefore) return false;
            knob.maxHP = hpAfter;
            changes.Add($"- **{id}** 勝率{a.WinRate:P0}→目標{TargetFor(id):P0} [{DiagText(a)}]: HP{(fallback ? "(次点)" : "")} {hpBefore}→**{hpAfter}**");
            return true;
        }

        /// <summary>7層専用: プレイヤーのロール勝率を ~90% へ寄せる「真我」(素ロール固定加算) の調整。
        /// 通常のダイス上限(5d9)を超えてボスのロールを引き締められる唯一のレバー。
        /// 勝率(HP差)ではなく **ロール勝率** を制御点にする。</summary>
        private static void AdjustTrueSelf(string id, BossAgg a, List<string> changes)
        {
            if (!BossTuning.TuneTrueSelf) return;
            float rwr = a.RollWinRate; // プレイヤーのロール勝率
            float target = BossTuning.TargetRollWinRate(id); // ボスごとの目標ロール勝率
            float err = rwr - target; // >0 = プレイヤーがロールに勝ちすぎ → 真我を上げる
            if (Mathf.Abs(err) < RollPinDeadZone) return;

            var meta = BossTuning.Meta(BossParam.TrueSelf);
            var knob = BossTuning.GetOrCreate(BossTuning.KeyFor(id));
            float before = BossTuning.GetParam(knob, BossParam.TrueSelf);
            float step = Mathf.Clamp(meta.gain * err, -meta.maxStep, meta.maxStep);
            BossTuning.SetParam(knob, BossParam.TrueSelf, before + step); // err>0→上げる / err<0→基準1まで下げる
            float after = BossTuning.GetParam(knob, BossParam.TrueSelf);
            if (Mathf.Abs(after - before) > 0.001f)
                changes.Add($"- **{id}** ロール勝率{rwr:P0}→目標{target:P0} [勝率{a.WinRate:P0} 平均{a.AvgTurns:F1}T]: "
                    + $"真我 {Fmt(before)}→**{Fmt(after)}** (ダイス上限超の素ロール加算)");
        }

        private static string ParamLabel(BossParam p)
        {
            switch (p)
            {
                case BossParam.JudgmentDice:      return "断罪ダイス上乗せ";
                case BossParam.JudgmentCoefBase:  return "断罪係数base";
                case BossParam.RobeStacks:        return "天衣無縫上限";
                case BossParam.ReflectPct:        return "反射率%";
                case BossParam.ChipCap:           return "審判の炎上限";
                case BossParam.BurstDamage:       return "爆ぜ火固定ダメ";
                case BossParam.RegenReduction:    return "灰塵の鎧軽減";
                case BossParam.SuddenDeathDamage: return "サドンデスダメ";
                case BossParam.TrueSelf:          return "真我";
                case BossParam.IntimidateThreat:  return "威圧の脅威";
                case BossParam.PoisonStackCap:    return "毒スタック上限";
                case BossParam.MirrorReflectCap:  return "反射上限";
                case BossParam.GoblinCall:        return "号令ダイス加算";
                case BossParam.StanceAtkMult:     return "高火力倍率";
                case BossParam.WeakRollRatio:     return "弱ロール比";
                default: return p.ToString();
            }
        }

        private static string Fmt(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        private static string DiagText(BossAgg a)
        {
            // 被ダメ支配率(全戦闘の総被ダメに占める最大ソース)を主表示。
            var (domCause, domShare) = a.DominantDamage();
            string dmgStr = a.dmgTotal > 0 ? $"被ダメ支配{domCause} {domShare:P0}" : "被ダメなし";
            return $"ロール勝率{a.RollWinRate:P0} 平均{a.AvgTurns:F1}T {dmgStr}";
        }
    }
}

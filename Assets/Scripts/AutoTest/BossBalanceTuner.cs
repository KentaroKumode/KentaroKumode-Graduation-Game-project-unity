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

        public const float CauseDominanceFrac = 0.40f; // 死因の40%以上 = 支配的
        public const float RollLossThreshold  = 0.50f; // 断罪Tで見切りロールに勝てているかの境
        public const float AttritionTurns     = 15.0f;  // 平均ターンがこれ以上 = ジリ貧

        // 7層の目標ロール勝率は BossTuning.TargetRollWinRate(id) でボスごとに設定 (これを超え張り付くと真我↑)
        public const float RollPinDeadZone    = 0.02f;  // ロール勝率が目標±この幅内なら真我を動かさない

        /// <summary>ボス戦闘勝率の目標 (フロア 1-6)。 7層は調整対象外。</summary>
        private static readonly float[] FloorTarget = { 0f, 0.98f, 0.96f, 0.95f, 0.91f, 0.88f, 0.25f };

        private class BossAgg
        {
            public int enc, wins;
            public long tWin, tLoss, tDraw, turns;
            public long rollSum; public int rollCount;
            public Dictionary<DeathCause, int> causes = new Dictionary<DeathCause, int>();
            public float WinRate => enc > 0 ? (float)wins / enc : 0f;
            public float RollWinRate { get { long t = tWin + tLoss + tDraw; return t > 0 ? (float)tWin / t : 1f; } }
            public float AvgTurns => enc > 0 ? (float)turns / enc : 0f;
            public float AvgPlayerRoll => rollCount > 0 ? (float)rollSum / rollCount : 0f;
            public int Losses => enc - wins;
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

                    float target = TargetFor(id);
                    float err = target - a.WinRate; // >0 = 難しすぎ
                    if (Mathf.Abs(err) < DeadZone) continue;

                    // 難しすぎ → 苦戦原因を診断し優先順位つき候補から有効な最初のレバーを動かす。 簡単すぎ → Dice で難化。
                    // 原因パラメータが無効(手動トグルoff)なら次点へフォールバック。 最終候補は Dice/HP。
                    var candidates = (err > 0f) ? DiagnoseStruggle(id, a) : DiceOnly;
                    bool applied = false;
                    for (int ci = 0; ci < candidates.Count && !applied; ci++)
                    {
                        var cand = candidates[ci];
                        if (cand.HasValue)
                        {
                            if (BossTuning.Enabled(cand.Value)) { AdjustParam(id, cand.Value, err, a, changes, ci > 0); applied = true; }
                        }
                        else if (BossTuning.TuneDice || BossTuning.TuneHp)
                        {
                            AdjustDice(id, err, a, changes, ci > 0); applied = true;
                        }
                    }
                }

                BossTuning.Save(learningRoot, batchIndex);
                Debug.Log($"[BossBalanceTuner] 調整 {changes.Count}件 / {BossTuning.Summary()}");

                if (changes.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"## {BotJudgmentLog.Now()} — L3 ボス難易度 自動調整 (6層以前・苦戦診断・実数値)");
                    sb.AppendLine();
                    foreach (var c in changes) sb.AppendLine(c);
                    sb.AppendLine();
                    sb.AppendLine("---");
                    BotJudgmentLog.Append(sb.ToString());
                }
            }
            catch (Exception e) { Debug.LogWarning($"[BossBalanceTuner] 例外: {e.Message}\n{e.StackTrace}"); }
        }

        private static float TargetFor(string id)
        {
            int f = BossTuning.FloorOf(id);
            return (f >= 1 && f <= 6) ? FloorTarget[f] : 0.5f;
        }

        /// <summary>Dice(=null)のみの候補リスト (簡単すぎ→難化 / 死因不明時の既定)。</summary>
        private static readonly List<BossParam?> DiceOnly = new List<BossParam?> { null };

        /// <summary>苦戦診断。 動かすレバーを **優先順位つき** で返す。 先頭=本命、 以降=次点。
        /// null は Dice(ボス素ダイス)/HP を意味する万能フォールバック (常に末尾に置く)。
        /// チューナーは有効(手動トグルon)な最初の候補を採用する。</summary>
        private static List<BossParam?> DiagnoseStruggle(string id, BossAgg a)
        {
            var list = new List<BossParam?>();

            // ① 特定の致死メカニズムが死因の支配的割合か
            int losses = Math.Max(1, a.Losses);
            DeathCause dom = DeathCause.Normal; int domN = 0;
            foreach (var kv in a.causes)
                if (kv.Key != DeathCause.Normal && kv.Key != DeathCause.Other && kv.Value > domN)
                { dom = kv.Key; domN = kv.Value; }

            if (domN > 0 && (float)domN / losses >= CauseDominanceFrac)
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
                    case DeathCause.Reflect: list.Add(BossParam.ReflectPct); break;
                    case DeathCause.Burst:   list.Add(BossParam.BurstDamage); break;
                    case DeathCause.Chip:    list.Add(BossParam.ChipCap); break;
                    // SuddenDeath は7層専用 → ここには来ない
                }
            }
            // ② 長期戦 = ジリ貧 (灰塵の鎧/サステイン)
            else if (a.AvgTurns >= AttritionTurns) list.Add(BossParam.RegenReduction);

            // ③ 万能フォールバック: ボス素ダイス(期待値)→HP。 本命/次点が全て無効でもここで吸収。
            list.Add(null);
            return list;
        }

        /// <summary>具体パラメータ (機構の実数値) を1ステップ調整。 err>0(難)→値を下げて易化。</summary>
        private static void AdjustParam(string id, BossParam param, float err, BossAgg a, List<string> changes, bool fallback = false)
        {
            var meta = BossTuning.Meta(param);
            var knob = BossTuning.GetOrCreate(BossTuning.KeyFor(id));
            float before = BossTuning.GetParam(knob, param);
            float step = Mathf.Clamp(meta.gain * err, -meta.maxStep, meta.maxStep);
            BossTuning.SetParam(knob, param, before - step); // err>0(難)→下げる
            float after = BossTuning.GetParam(knob, param);
            if (Mathf.Abs(after - before) > 0.001f)
                changes.Add($"- **{id}** 勝率{a.WinRate:P0}→目標{TargetFor(id):P0} [{DiagText(a)}]: "
                    + $"{ParamLabel(param)}{(fallback ? "(次点)" : "")} {Fmt(before)}→**{Fmt(after)}**");
        }

        /// <summary>Dice を期待値で調整。 ボス期待値 = プレイヤー平均出目 + diceOffset。 範囲外は HP(絶対値) に回す。</summary>
        private static void AdjustDice(string id, float err, BossAgg a, List<string> changes, bool fallback = false)
        {
            if (!BossTuning.TuneDice && !BossTuning.TuneHp) return; // どちらも無効なら何もしない
            float avgRoll = a.AvgPlayerRoll;
            if (avgRoll <= 0f) return; // ロールデータなし

            var knob = BossTuning.GetOrCreate(BossTuning.KeyFor(id));
            string line = $"- **{id}** 勝率{a.WinRate:P0}→目標{TargetFor(id):P0} [{DiagText(a)} 平均出目{avgRoll:F1}]:{(fallback ? " (次点)" : "")}";
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
            if (BossTuning.TuneHp && (!BossTuning.TuneDice || saturated))
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
                default: return p.ToString();
            }
        }

        private static string Fmt(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        private static string DiagText(BossAgg a)
        {
            DeathCause dom = DeathCause.Normal; int domN = 0;
            foreach (var kv in a.causes) if (kv.Value > domN) { dom = kv.Key; domN = kv.Value; }
            string causeStr = a.Losses > 0 ? $"死因{dom}{(domN > 0 ? $" {(float)domN / Math.Max(1, a.Losses):P0}" : "")}" : "敗北なし";
            return $"ロール勝率{a.RollWinRate:P0} 平均{a.AvgTurns:F1}T {causeStr}";
        }
    }
}

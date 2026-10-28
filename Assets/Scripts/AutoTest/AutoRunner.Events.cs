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
    /// <summary>AutoRunner: イベントの選択。</summary>
    public partial class AutoRunner
    {
        private void DoEvent()
        {
            var gm = GameManager.Instance;
            var ee = EventEncounter.Instance;
            var cur = ee != null ? ee.Current : null;

            // ランダムイベント無限ループ等の保険: 一定回数で強制読了して脱出を試みる
            // （回復不能なら RunOne のストール検出が DEADLOCK として確定させる）
            _eventStuckCount++;
            if (_eventStuckCount > 40)
            {
                // Current を null 化 → GameManager 側(D1)が未確定でも MapNavigation へ脱出させる
                EventEncounter.Instance?.Clear();
                gm.ConfirmEventEncounter();
                _lastResolvedEvent = cur;
                return;
            }

            if (cur == null) { gm.ConfirmEventEncounter(); return; }

            if (!ReferenceEquals(cur, _lastResolvedEvent))
            {
                int idx = PickSafeChoice(cur);
                // [アブレーション] イベントの選択判断を潰す (提示された選択肢から一様抽選)。
                if (ablateEvent && cur.choices != null && cur.choices.Count > 1)
                    idx = AblationRng.Next(0, cur.choices.Count);
                // Ultra: 選択肢に条件は無く (EventChoice は text/effects のみ)、
                //   提示された分がそのまま合法手になる。
                idx = UltraOverrideChoice(gm,
                    AutoTest.Ultra.UltraDecisionPoint.EventChoice,
                    AutoTest.Ultra.UltraActionKind.ChooseEventOption,
                    BuildEventChoiceView(cur), idx);
                _lastResolvedEvent = cur;
                // L1.5: イベント学習用に「id|choiceIndex」を記録
                if (_cur != null && cur != null && !string.IsNullOrEmpty(cur.id))
                    _cur.eventChoicesMade.Add(cur.id + "|" + idx);
                gm.ResolveEventChoice(idx);
            }
            else
            {
                // フレーバー読了 or 戦闘トリガ後の復帰待ち
                gm.ConfirmEventEncounter();
            }
        }

        /// <summary>イベントの選択肢を観測用の形へ写す。
        ///
        /// <para>選択肢の**文面をそのまま**ラベルに載せる ── プレイヤーが読んでいるのは
        /// これであって、 効果の内部表現ではない。 効果の要約 (<c>EventChoiceScorer</c> が
        /// 使うような数値) を渡すと、 盤面に出ていない情報を渡すことになる。</para></summary>
        private static AutoTest.Ultra.UltraChoiceView BuildEventChoiceView(EventDefinition def)
        {
            var view = new AutoTest.Ultra.UltraChoiceView
            {
                kind = "event",
                prompt = def != null ? (def.name ?? def.id ?? "") : "",
            };
            if (def?.choices == null || def.choices.Count == 0) return view;

            var options = new AutoTest.Ultra.UltraChoiceOption[def.choices.Count];
            for (int i = 0; i < def.choices.Count; i++)
                options[i] = new AutoTest.Ultra.UltraChoiceOption
                {
                    index = i,
                    id = "",                       // 安定 ID は無いので添字で識別する
                    label = def.choices[i]?.text ?? "",
                };
            view.options = options;
            return view;
        }

        private int PickSafeChoice(EventDefinition def)
        {
            if (def == null || def.choices == null || def.choices.Count == 0) return 0;

            var run = GameLoop.GameManager.Instance?.Run;

            // 7層を目指す方策では、6層の専用イベントで〈真理〉を得る選択を明示的に選ぶ。
            // 効果欄は「なし」なので、通常の数値スコアだけでは離脱択と同点になってしまう。
            if (def.name == GameLoop.ConvictionSystem.Layer7RevelationEventName)
                return 0;

            // ① フラグ進路は依然として「ほぼ無条件で進める」（チェーン進路は数値スコア以上に価値が高い）
            int progressIdx = PickFlagProgressChoice(def);
            if (progressIdx >= 0) return progressIdx;

            // ② 数値スコアラで選定。スコア差が小さければ次点も取り得る（両分岐の探索性）。
            //    HPが低い時は HpDelta/EnterCombat 系が強烈にマイナス → 自動的に「立ち去り」を選ぶ。
            //    現状HP余裕で 100G+物資損 vs なし なら 100G を取る（ゴールド価値 > 物資コスト）。
            int byScore = EventChoiceScorer.PickBestIndex(def, run, _rng,
                explorationRate: AutoTest.PolicyParameters.Current.eventExplorationRate);

            // ③ 効果が全くない選択肢が複数ある場合は、最低限のフォールバックとして「立ち去り」系を選ぶ
            //    （スコアラは効果ゼロを 0 と評価するので、明示的な離脱選択肢を優先）
            if (byScore >= 0 && byScore < def.choices.Count)
            {
                var pickedText = def.choices[byScore]?.text ?? "";
                bool pickedIsLeave = false;
                foreach (var kw in LeaveKeywords) if (pickedText.Contains(kw)) { pickedIsLeave = true; break; }
                // 危険語入り選択肢のスコアが負ならOK、もしスコア同点で危険語のみの選択肢を引いてしまった場合の救済
                if (!pickedIsLeave)
                {
                    // スコア 0 以下なら離脱選択肢を探す
                    float pickedScore = EventChoiceScorer.Score(def.choices[byScore], run);
                    if (pickedScore <= 0f)
                    {
                        for (int i = 0; i < def.choices.Count; i++)
                        {
                            var txt = def.choices[i]?.text ?? "";
                            foreach (var kw in LeaveKeywords)
                                if (txt.Contains(kw)) return i;
                        }
                    }
                }
                return byScore;
            }
            return 0;
        }

        /// <summary>選択肢の効果を見て「フラグを進める」スコアを算出。
        /// いずれかの選択肢がプラスならその index を返し、なければ -1。
        /// 既存所持フラグを廃棄するだけの選択肢はマイナス点で忌避される。</summary>
        private int PickFlagProgressChoice(EventDefinition def)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            int bestIdx = -1;
            int bestScore = 0;
            for (int i = 0; i < def.choices.Count; i++)
            {
                int s = ScoreFlagChoice(def.choices[i], run);
                if (s > bestScore) { bestScore = s; bestIdx = i; }
            }
            return bestIdx;
        }

        /// <summary>選択肢1つに対するフラグ進路スコア。</summary>
        private int ScoreFlagChoice(EventSystem.EventChoice choice, GameLoop.RunState run)
        {
            if (choice?.effects == null || choice.effects.Count == 0) return 0;
            int score = 0;
            bool hasGain = false;
            int discardCount = 0;
            foreach (var eff in choice.effects)
            {
                switch (eff.type)
                {
                    case EventSystem.EventEffectType.GainFlag:
                        score += 100; hasGain = true;
                        // 未所持なら追加加点 (フラグ未成立時=新規進路を優先)
                        if (run?.ownedFlags == null || !run.ownedFlags.Contains(eff.param ?? "")) score += 50;
                        break;
                    case EventSystem.EventEffectType.GainPassiveItem:
                    case EventSystem.EventEffectType.GainSpecificItem:
                        // 名前付き / パッシブ獲得は基本的に進路系
                        score += 60; hasGain = true;
                        break;
                    case EventSystem.EventEffectType.DiscardFlag:
                        discardCount++;
                        break;
                }
            }
            // フラグ廃棄のみで何も獲得しない選択肢は強い忌避
            if (discardCount > 0 && !hasGain) score -= 80;
            // 廃棄しつつ獲得もある (チェーン継続: 旧フラグ → 新フラグ/パッシブ) は中立 (gain加点で十分)
            return score;
        }

        // ===== 5Fボス勝率スイープ =====
    }
}

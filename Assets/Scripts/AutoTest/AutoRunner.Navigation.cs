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
    /// <summary>AutoRunner: 航行判断 (DoNavigate)・Ultra の上書き・危険度・アブレーション・階層 DP。</summary>
    public partial class AutoRunner
    {
        private bool DoNavigate()
        {
            var gm = GameManager.Instance;
            var mm = MapManager.Instance;
            if (mm == null) { Finish(Outcome.Deadlock, "MapManager null"); return true; }

            // **Ultra の評価中は、 盤面にも RNG にも触れずに戻る。**
            //   ここより下の探索は GameRng を消費し得るので、 待っている間に通すと
            //   毎フレーム引き直すことになり再現性が壊れる。 生産方策の手は
            //   評価を始めた時点のものを取ってある。
            if (_ultraPending != null) return PollUltraNavigation(gm, mm);

            _eventStuckCount = 0; // マップに戻った＝イベント解決済み

            // 自由移動の層 (2026-09-28): 距離と物資で点を選ぶ航行へ (AutoRunner.FreeMap.cs)。
            if (mm.IsFreeMap) return DoNavigateFree(gm, mm);

            // 離散マップ (8 層: 前哨基地 → ボス。 通常は EnterFloor が自動でボスへ入る) は道の先へ進むだけ。
            var pool = mm.GetAvailableMoves();

            if (pool == null || pool.Count == 0)
            {
                Finish(Outcome.Deadlock, "移動先なし(MapNavigation)");
                return true;
            }

            float hpRatio = gm.Run.playerMaxHP > 0
                ? (float)gm.Run.playerHP / gm.Run.playerMaxHP : 1f;

            // ボス接近判定: プールにBossがある or 残り行数が2以内
            bool bossNear = false;
            foreach (var n in pool) if (n.EffectiveType == TileType.Boss) { bossNear = true; break; }
            int rowCount = mm.CurrentMap?.rowCount ?? 10;
            if (!bossNear && mm.CurrentNode != null && mm.CurrentNode.row >= rowCount - 2)
                bossNear = true;
            _curBossNear = bossNear; // 休憩フェーズの選択判断で参照

            // 回復目標: **想定被弾 safetyHits 発分の HP を確保して次の戦闘へ入る** (危険度駆動)。
            //   旧実装は「ボス接近 0.8 / それ以外 0.5」の固定割合で、 高難易度でも HP 半分で
            //   戦闘に入り続けていた (30pt の敗因の 56% が削り負け・死亡戦の開始HP中央値 58%)。
            //   被弾量で駆動すればデバフ・層深度・エリートのどれが原因でも自動追従する。
            //   0.5 の床を残すので低難易度の挙動は従来どおり。
            int floorHit = EstimateFloorMaxHit(gm.Run);
            float dangerTarget = FightBudgetDanger(gm.Run, floorHit);
            // ボス接近時は 1 発ぶん上乗せ (旧 0.8 固定の役割を相対量で引き継ぐ)。
            if (!_curCombatAverse && bossNear && floorHit > 0 && gm.Run.playerMaxHP > 0)
            {
                float bossMargin = (float)floorHit / gm.Run.playerMaxHP;
                dangerTarget = Mathf.Clamp(dangerTarget + bossMargin, 0.5f, 0.95f);
            }
            float healTarget = dangerTarget;
            while (Consumables.TryUseBestHeal(gm.Run, healTarget)) { }

            // 物資の補充は店の「物資の包」だけ (DoShop)。 物資を回復する消耗品は 2026-10-31 に廃止した。

            // 休憩を強く優先すべき状況:
            //  - 貪欲がボス接近かつHPが8割未満（スケールしたbuildをボスへ生存させる）
            //  ※旧・空腹切れ条件は飢餓→物資統合で廃止（休憩は物資を回復しない＝物資は食料で対応）。
            bool preferRest = (!_curCombatAverse && bossNear && hpRatio < dangerTarget);

            // 戦闘忌避の境界 ＝ 危険度そのもの (2026-09-15)。
            //   旧実装は `max(hpLowThreshold=0.55, dangerTarget)` だったが、 **床が 2 つ競合して
            //   必ずどちらかが飽和していた** ── 旧式では dangerTarget が 0.95 に張り付いて
            //   hpLowThreshold が死に、 新式に替えたら今度は 0.55 が全部を潰した
            //   (SafetyFights を 1.0〜2.5 で振っても結果が動かなくなった)。
            //   新式は DangerFloor (0.25) と tailGuard (最悪の 1 発) で同じ役割を内蔵している。
            float lowBar = dangerTarget;

            // ── 2 手先読み ──
            //   1 手決めだと「回復してからエリート」のような組み立てが作れず、 目先の
            //   タイル種別だけで決めてしまう。 各候補について、 そこを踏んだ後の HP を
            //   予測し、 その状態で次に選べる最良手を足して評価する。
            //   **先の手は割り引く** (係数 0.6) ── 予測 HP は概算で、 分岐も再抽選されるため、
            //   同点なら手前の確実さを優先させる。
            const float LookaheadDiscount = 0.6f;
            MapNode best = pool[0];
            float bestScore = float.MaxValue;
            var fmap = mm.CurrentMap;
            var here = mm.CurrentNode;
            if (useFloorDpNavigation) FloorDpPrepare(fmap);
            _navScores.Clear();
            foreach (var n in pool)
            {
                float score = Rank(n, hpRatio, _curCombatAverse, preferRest, gm.Run, lowBar);

                float nextHpRatio = PredictHpRatioAfter(n, hpRatio, floorHit, gm.Run);
                var succ = fmap?.GetReachableFrom(n.id);
                if (succ != null && succ.Count > 0)
                {
                    float bestNext = float.MaxValue;
                    foreach (var s in succ)
                    {
                        // **既定は 1 手先までの評価** (`Rank` 1 回)。 `useFloorDpNavigation` を
                        //   立てると、 そこから階層の出口まで後ろ向きに解いた値に差し替わる。
                        //   既定 OFF なので、 立てない限り従来とビット単位で同一 ──
                        //   Super は全測定の基準線なので、 黙って変えると既存の基準値が
                        //   まとめて無効になる。
                        float r2 = useFloorDpNavigation
                            ? FloorDpValue(fmap, s, HpBandOf(nextHpRatio), floorHit, gm.Run, lowBar)
                            : Rank(s, nextHpRatio, _curCombatAverse, false, gm.Run, lowBar);
                        if (r2 < bestNext) bestNext = r2;
                    }
                    if (bestNext != float.MaxValue) score += LookaheadDiscount * bestNext;
                }
                _navScores.Add(score);
                if (score < bestScore) { bestScore = score; best = n; }
            }

            // --- [計装] 航行スコアの同点率 ---
            //   `Rank()` は **int** を返す (旧 `LateralPenalty` は 2026-09-28 に削除)。
            //   合成は int + {0,L} + 0.6×(int + {0,L}) の粗い格子なので、 同点が多発するはず
            //   ── だが**戦闘で同じ推測を 6 倍外した**ので数える。 最良と厳密同値の本数を採る。
            if (pool.Count > 1)
            {
                int tied = 0;
                for (int i = 0; i < _navScores.Count; i++)
                    if (_navScores[i] == bestScore) tied++;
                NavDecisions++;
                NavCandSum += pool.Count;
                NavTiedSum += tied;
                if (tied > 1) NavTiedDecisions++;
            }

            // --- [アブレーション] 航行判断を潰す ---
            //   **判断の価値は「無くしたときの落ち幅」で測る。** 触る前に測る (2026-08-22)。
            //   専用乱数を使うので GameRng を 1 つも消費しない ── マップ・敵・アイテムの
            //   抽選が対照アームと完全に一致し、 同一シードのペア比較が成立する。
            if (ablateNavigation && pool.Count > 1)
                best = pool[AblationRng.Next(0, pool.Count)];

            TryCaptureUltraCheckpoint(gm, mm);

            // --- Ultra: 公開観測だけで決める controller に 1 手だけ委ねる (§10 Phase D) ---
            //   **sink が null なら 1 命令も実行されない。** ここより上の探索は一切変えていない
            //   ので、 controller 未接続時の挙動は従来とビット単位で同一 ── 既存の基準値
            //   (Super/Optimal の全測定) が生き続けることがこの分岐の前提条件。
            MapNode overridden = UltraOverrideMove(gm, mm, best);
            // 評価が始まった (＝この決定はまだ終わっていない)。 盤面を動かさずに戻り、
            // 次以降の Step で回収する。 移動はそのときに 1 回だけ実行される。
            if (_ultraPending != null) { _ultraWaiting = true; return false; }

            NoteEliteChoice(overridden ?? best, pool, hpRatio, preferRest, gm.Run, lowBar);
            gm.MoveToNode((overridden ?? best).id);
            return false;
        }

        /// <summary>[計装 2026-09-20] エリートマスを選んだ理由と、 その時点の被ダメ見積りを SkillDiag へ置く。
        /// 戦闘の終わりに実際の被ダメ・死亡と突き合わされる (SkillDiag.EliteReason)。</summary>
        private void NoteEliteChoice(MapNode chosen, List<MapNode> pool, float hpRatio, bool preferRest,
                                     GameLoop.RunState run, float lowBar)
        {
            if (chosen == null || chosen.EffectiveType != TileType.EliteBattle) return;
            bool allElite = true;
            foreach (var n in pool) if (n.EffectiveType != TileType.EliteBattle) { allElite = false; break; }
            float r = Rank(chosen, hpRatio, _curCombatAverse, preferRest, run, lowBar);
            int reason = !UseEliteCostNav ? 4 : allElite ? 3 : r <= -5f ? 2 : r >= 90f ? 1 : 0;
            float loss = EliteFightLoss(chosen, run);
            CombatSystem.SkillDiag.PendingEliteReason = reason;
            CombatSystem.SkillDiag.PendingEliteEst = (loss >= 0f && run != null && run.playerMaxHP > 0)
                ? loss / run.playerMaxHP : -1f;
        }

        /// <summary>進行中の決定を捨て、 走っている worker を殺す。
        ///
        /// <para>ラン終了やバッチ停止で呼ぶ。 呼び忘れると、 誰も答えを待っていない
        /// worker プロセスが取り残される。</para></summary>
        private void ClearUltraPending()
        {
            if (_ultraPending != null)
                AutoTest.Ultra.UltraRolloutSink.CancelDecision(_ultraPending);
            _ultraPending = null;
            _ultraPendingCheckpoint = null;
            _ultraPendingProductionChoice = null;
            _ultraWaiting = false;
        }

        /// <summary>進行中の Ultra 決定を 1 掃引ぶん進める。 まだなら盤面に触れずに戻る。</summary>
        private bool PollUltraNavigation(GameManager gm, MapManager mm)
        {
            var sink = ultraSink as AutoTest.Ultra.UltraRolloutSink;
            if (sink == null)
            {
                // ありえないが、 保留を抱えたまま sink が入れ替わったら手を離す。
                ClearUltraPending();
                return false;
            }

            string actionId = null;
            string reason = null;
            bool faulted = false;
            try
            {
                if (!sink.PollDecide(_ultraPending, out actionId, out reason))
                {
                    _ultraWaiting = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                // controller の故障はゲーム上の敗北ではない。 数えて生産方策へ落とす。
                AutoTest.Ultra.UltraDispatch.NoteFault(ex, ref reason);
                faulted = true;
            }

            AutoTest.Ultra.UltraCheckpoint checkpoint = _ultraPendingCheckpoint;
            MapNode production = _ultraPendingProductionChoice;
            _ultraPending = null;
            _ultraPendingCheckpoint = null;
            _ultraPendingProductionChoice = null;
            _ultraWaiting = false;

            float spent = Time.realtimeSinceStartup - _ultraWaitBeganRt;
            _ultraThinkingSeconds += spent;
            _ultraThinkingSecondsTotal += spent;

            // **同期経路と同じ受け入れ規律を通す。** フレームを跨いだからといって
            //   commit 検証を素通しにはしない。
            if (!faulted)
                actionId = AutoTest.Ultra.UltraDispatch.CompleteResolve(
                    checkpoint, _ultraEpoch, AutoTest.Ultra.UltraDecisionPoint.MapNavigation,
                    actionId, ref reason);
            else
                actionId = null;
            _ultraEpoch++;   // **必ず進める。** 採否に関わらず 1 決定機会を消費した
            if (actionId == null && !string.IsNullOrEmpty(reason)) _ultraLastFallbackReason = reason;

            MapNode target = ResolveUltraMove(mm, actionId, reason);
            if (target != null)
                AutoTest.Ultra.UltraDispatch.NoteOutcome(
                    production == null
                    || !string.Equals(target.id, production.id, StringComparison.Ordinal));

            MapNode chosen = target ?? production;
            if (chosen == null)
            {
                // 生産方策の手すら残っていない ── 次の Step で普通に選び直させる。
                // **黙って進めない。**
                Debug.LogError("[AutoRunner][Ultra] 保留していた移動先が消えた。 選び直す");
                return false;
            }
            gm.MoveToNode(chosen.id);
            return false;
        }

        /// <summary>実ランの途中で checkpoint を 1 つだけ書き出す。 **path が空なら即 return**。
        ///
        /// <para>採るのは航行フェーズの判断直前 ── 合法手が確定していて、
        /// かつ盤面がまだ動いていない一点。 veil 種は 0 固定で書き出すが、
        /// 利用側は種を変えて読み直す（<see cref="AutoTest.Ultra.UltraResumePayload.Create"/> が
        /// 毎回 veil を掛け直す）。</para></summary>
        private void TryCaptureUltraCheckpoint(GameManager gm, MapManager mm)
        {
            if (_ultraCaptured || string.IsNullOrEmpty(ultraCaptureCheckpointPath)) return;
            if (gm?.Run == null || mm?.CurrentMap == null) return;
            if (++_ultraNavSeen < Math.Max(1, ultraCaptureAtNavigation)) return;

            try
            {
                var payload = AutoTest.Ultra.UltraResumePayload.Create(
                    gm.Run, mm, _ultraEpoch,
                    AutoTest.Ultra.UltraDecisionPoint.MapNavigation, 0,
                    out AutoTest.Ultra.UltraVeilReport veil);

                string full = System.IO.Path.GetFullPath(ultraCaptureCheckpointPath);
                string dir = System.IO.Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(full, JsonUtility.ToJson(payload, false),
                    new UTF8Encoding(false));

                _ultraCaptured = true;
                Debug.Log($"[AutoRunner][Ultra] checkpoint を書き出した: {full}"
                        + $"  航行{_ultraNavSeen}回目 / {gm.Run.Depth}層"
                        + $" / HP {gm.Run.playerHP}/{gm.Run.playerMaxHP}"
                        + $" / 所持金 {gm.Run.coins} / {veil.Describe()}");
            }
            catch (Exception ex)
            {
                _ultraCaptured = true;   // 失敗を毎ターン繰り返さない
                Debug.LogError("[AutoRunner][Ultra] checkpoint 書き出し失敗: " + ex.Message);
            }
        }

        /// <summary>Ultra へ問い合わせ、 掛かった実時間を控除枠へ積む。
        ///
        /// <para>rollout は worker プロセスの完了を同期で待つため、 1 決定で数秒ブロックする。
        /// その時間を番犬に数えさせると、 ラン は「固着」と判定されて全部 DEADLOCK になる。
        /// 番犬そのものは無効化しない ── 無効化すると、 本物の固着が出たときに
        /// バッチが永久に止まる。</para></summary>
        private string ResolveUltraTimed(
            AutoTest.Ultra.UltraCheckpoint checkpoint,
            AutoTest.Ultra.UltraDecisionPoint point,
            out string reason)
        {
            float began = Time.realtimeSinceStartup;
            try
            {
                return AutoTest.Ultra.UltraDispatch.TryResolve(
                    ultraSink, checkpoint, _ultraEpoch, point, out reason);
            }
            finally
            {
                float spent = Time.realtimeSinceStartup - began;
                _ultraThinkingSeconds += spent;
                _ultraThinkingSecondsTotal += spent;
            }
        }

        /// <summary>離散選択を Ultra controller に委ねる。 採用できなければ
        /// <paramref name="productionChoice"/> をそのまま返す。
        ///
        /// <para>移動と同じ規律: 提示した合法手だけを受け付け、 epoch と決定点を検証し、
        /// 例外・辞退・不正はすべて生産方策へ落ちる。 呼び出し側は戻り値の添字を
        /// **そのまま**使えばよく、 Ultra が居るかどうかを気にしなくてよい。</para></summary>
        private int UltraOverrideChoice(
            GameManager gm,
            AutoTest.Ultra.UltraDecisionPoint point,
            AutoTest.Ultra.UltraActionKind kind,
            AutoTest.Ultra.UltraChoiceView choice,
            int productionChoice)
        {
            if (gm?.Run == null || choice == null) return productionChoice;

            // 合法手の数え方は ChoiceActions と揃える ── 買えない選択肢は「見えているが
            // 選べない」ので手ではない。 ここで数がずれると、 国勢調査だけが別のゲームを
            // 数えることになる。
            int legalCount = 0;
            if (choice.options != null)
                for (int i = 0; i < choice.options.Length; i++)
                    if (choice.options[i] != null && choice.options[i].available) legalCount++;
            AutoTest.Ultra.UltraDecisionCensus.Record(point, gm.Run.Depth, legalCount);

            if (ultraSink == null) return productionChoice;

            var observation = AutoTest.Ultra.UltraObservationBuilder.Capture(
                gm.Run, MapManager.Instance, point.ToString(), _ultraEpoch);
            observation.choice = choice;

            var checkpoint = AutoTest.Ultra.UltraCheckpointProtocol.Create(
                _ultraEpoch, point, observation,
                AutoTest.Ultra.UltraCheckpointProtocol.ChoiceActions(observation, kind));

            string actionId = ResolveUltraTimed(checkpoint, point, out string reason);
            _ultraEpoch++;

            if (actionId == null)
            {
                if (!string.IsNullOrEmpty(reason)) _ultraLastFallbackReason = reason;
                return productionChoice;
            }

            int picked = AutoTest.Ultra.UltraCheckpointProtocol.ResolveChoiceIndex(
                observation, kind, actionId);
            if (picked < 0)
            {
                AutoTest.Ultra.UltraDispatchStats.Rejected++;
                _ultraLastFallbackReason = "choice action did not resolve to an option";
                return productionChoice;
            }

            AutoTest.Ultra.UltraDispatch.NoteOutcome(picked != productionChoice);
            return picked;
        }

        /// <summary>Ultra controller に移動先を選ばせる。 採用できなければ null を返す。
        ///
        /// <para><b>合法手は observation から作る。</b> 直上の <c>Rank</c> ループは
        /// <c>EffectiveType</c> を pool と**その後続**の両方について読むので、
        /// 未公開マスがあれば、 そこから手集合を作ると「プレイヤーが持たない情報で絞られた
        /// 候補」を Ultra に渡すことになる。
        /// <see cref="UltraCheckpointProtocol.MapNavigationActions"/> は observation の
        /// <c>reachableNow</c> だけを見るので、 その経路が構造的に存在しない。</para>
        ///
        /// <para><b>ただし現時点で未公開マスは存在しない (2026-08-21 検証)。</b>
        /// 視界制限を持っていたのは 〈戦場の霧〉/〈暗夜〉 の 2 軸だけで、 どちらも廃止され
        /// <c>MetaDebuffApplicator.GetMapSightLimit()</c> は <c>-1</c> (制限なし) を返すだけ。
        /// よって <c>MapNode.revealed</c> は常に true、 observation も
        /// <c>Classify(EffectiveType)</c> をそのまま渡す ── **Super と Ultra は同じ盤面を見る。**
        /// この分岐は今の非対称を消すためではなく、 視界制限が復活したときに自動的に
        /// 正しくあり続けるための構造。
        /// かつてここには「Super は未公開マスの正体まで見ている」と書いてあり、 軸の廃止後も
        /// 残っていたため、 2026-08-21 の Phase H 測定で敗因の解釈を誤らせた。
        /// **正本は実コード。 コメントを根拠に測定結果を説明しないこと。**</para>
        ///
        /// <para><b>候補は生産方策の pool ではなく合法手全体。</b> pool は物資残量で横移動を
        /// 足すかどうかを決める発見的な絞り込みで、 それを継承すると Ultra の選択肢が
        /// Super の選択肢に頭打ちされる。 横移動の物資コストは
        /// <c>GameManager.MoveToNode</c> が row の比較から徴収するので、
        /// **誰が選んでも同じように支払われる** ── 方策側で払い忘れる経路は無い。</para></summary>
        private MapNode UltraOverrideMove(GameManager gm, MapManager mm, MapNode productionChoice)
        {
            if (gm?.Run == null || mm == null) return null;

            // 国勢調査は sink が居なくても数える。 「1ラン あたりマクロ決定 30 回」は
            // 実測ではなくコスト計測メニューに直書きされた定数で、 rollout をどこで撃つかの
            // 判断はこの数に乗っている。 **絞る前に数える。**
            if (ultraSink == null)
            {
                if (AutoTest.Ultra.UltraDecisionCensus.Enabled)
                {
                    var censusObs = AutoTest.Ultra.UltraObservationBuilder.Capture(
                        gm.Run, mm, "MapNavigation", _ultraEpoch);
                    AutoTest.Ultra.UltraDecisionCensus.Record(
                        AutoTest.Ultra.UltraDecisionPoint.MapNavigation, gm.Run.Depth,
                        AutoTest.Ultra.UltraCheckpointProtocol.MapNavigationActions(censusObs).Count);
                }
                return null;
            }

            var observation = AutoTest.Ultra.UltraObservationBuilder.Capture(
                gm.Run, mm, "MapNavigation", _ultraEpoch);
            AutoTest.Ultra.UltraDecisionCensus.Record(
                AutoTest.Ultra.UltraDecisionPoint.MapNavigation, gm.Run.Depth,
                AutoTest.Ultra.UltraCheckpointProtocol.MapNavigationActions(observation).Count);
            var checkpoint = AutoTest.Ultra.UltraCheckpointProtocol.Create(
                _ultraEpoch,
                AutoTest.Ultra.UltraDecisionPoint.MapNavigation,
                observation,
                AutoTest.Ultra.UltraCheckpointProtocol.MapNavigationActions(observation));

            // rollout sink は worker を待つ。 **同期で待つとメインスレッドが数秒停車する**ので、
            // 開始だけして戻り、 回収は後続の Step に任せる。 それ以外の sink (疎通用の
            // 先頭手/末尾手など) は即答するので従来どおり同期で通す。
            if (ultraSink is AutoTest.Ultra.UltraRolloutSink rolloutSink)
            {
                AutoTest.Ultra.UltraDispatch.NoteOffered();
                _ultraPending = rolloutSink.BeginDecide(checkpoint);
                _ultraPendingCheckpoint = checkpoint;
                _ultraPendingProductionChoice = productionChoice;
                _ultraWaitBeganRt = Time.realtimeSinceStartup;
                // **epoch は回収時に進める。** ここで進めてしまうと、 待っている間に
                //   checkpoint の epoch と runner の epoch がずれ、 回収時の commit 検証
                //   (TryValidateCommit) が必ず落ちる ── 検証を素通しにするか、 全ての
                //   決定を却下するかの二択になる。 どちらも避ける。
                return null;
            }

            string actionId = ResolveUltraTimed(
                checkpoint, AutoTest.Ultra.UltraDecisionPoint.MapNavigation, out string reason);
            _ultraEpoch++;   // **必ず進める。** 採否に関わらず 1 決定機会を消費した

            if (actionId == null)
            {
                if (!string.IsNullOrEmpty(reason)) _ultraLastFallbackReason = reason;
                return null;
            }

            MapNode target = ResolveUltraMove(mm, actionId, reason);
            if (target == null) return null;

            AutoTest.Ultra.UltraDispatch.NoteOutcome(
                productionChoice == null || !string.Equals(target.id, productionChoice.id,
                    StringComparison.Ordinal));
            return target;
        }

        /// <summary>受け取った action id を、 いまの盤面で実際に踏める接続へ解決する。
        ///
        /// <para>checkpoint が通っても、 その id が現在の接続かどうかは別問題 ──
        /// 解決できなければ生産方策へ落とす。</para></summary>
        private MapNode ResolveUltraMove(MapManager mm, string actionId, string reason)
        {
            if (actionId == null)
            {
                if (!string.IsNullOrEmpty(reason)) _ultraLastFallbackReason = reason;
                return null;
            }
            var moves = mm != null ? mm.GetAvailableMoves() : null;
            if (moves != null)
                for (int i = 0; i < moves.Count; i++)
                {
                    if (moves[i] == null) continue;
                    if (!string.Equals(
                            AutoTest.Ultra.UltraLegalAction.Of(
                                AutoTest.Ultra.UltraActionKind.MoveTo, moves[i].id).ActionId,
                            actionId, StringComparison.Ordinal)) continue;
                    return moves[i];
                }
            AutoTest.Ultra.UltraDispatchStats.Rejected++;
            _ultraLastFallbackReason = "action did not resolve to a legal move";
            return null;
        }

        /// <summary>この敵から 1 ターンに飛んでくる最大被弾の見積り。
        ///
        /// **挑戦デバフとエスカレーションを必ず掛ける。** 素の <see cref="EnemyData.EffectiveBaseAttack"/>
        /// だけを見ると、 狂暴化 (敵ダメ +10〜20%) も長期戦の倍率 (最大 ×2.30) も推定に入らず、
        /// 「高難易度ほど危険」を BOT が検知できない (2026-08-05)。 危険度駆動の方針は
        /// この見積りを唯一の入力にするので、 ここが鈍いと方針全体が鈍る。
        ///
        /// **敵の会心は 2026-08-05 に全廃した** (雑魚15体・ボス5体・偽の商人)。 被弾が
        /// 「基礎 + ロール × エスカレーション」だけになり、 プレイヤーが読める量になる。
        /// そのためこの推定に会心項は無い。 <c>criticalNumerator</c> はプレイヤー側と
        /// 旧データ互換のために残っているだけで、 敵側は全て 0。</summary>
        private static int EstimateMaxHit(CombatSystem.EnemyData e, int turn)
            => EstimateMaxHit(e, turn, null);

        /// <summary>敵の 1 発分の最大被弾見積り。 緊急回復のトリガに使う。
        ///
        /// <para><b>ctx を渡すこと。</b> 素ステータス (EnemyData) だけだと、
        /// <c>enemyDiceTotalBonus</c> / <c>bossDiceBonus</c> に積まれた**実行時の加算**が丸ごと抜ける。
        /// 実測 (2026-08-16・7層p4): 見積り 35 に対し実際の攻撃値は 57.5 で 39% の過小評価だった
        /// ── 停滞スタック 4.9 + 遺物の出目加算 7.6 + 溜め打ち 5 が計上されていなかったため。</para>
        ///
        /// <para>結果として <c>HP ≤ 見積り</c> で回復する方策に**死角**ができていた:
        /// 見積り 35 / 実際の貫通 44 なら、 HP 36〜44 の帯は「まだ安全」と判断されたまま
        /// 次の一撃で落ちる。 実測で p4 敗死 277 件のうち <b>52% が回復アイテムを持ったまま</b>
        /// 死んでおり (平均 1.08 個)、 その大半がこの死角によるもの。</para></summary>
        private static int EstimateMaxHit(CombatSystem.EnemyData e, int turn,
                                          ItemSystem.PassiveSkills.CombatContext ctx)
        {
            if (e == null) return 0;
            float hit = e.EffectiveBaseAttack + e.EffectiveRollCount * e.EffectiveRollMax;
            if (ctx != null) hit += Mathf.Max(0, ctx.enemyDiceTotalBonus) + Mathf.Max(0, ctx.bossDiceBonus);
            hit *= MetaProgression.MetaDebuffApplicator.GetEnemyDamageMultiplier();
            hit *= CombatSystem.Escalation.GetMultiplier(null, Mathf.Max(1, turn));
            return Mathf.RoundToInt(hit);
        }

        /// <summary>次に踏む戦闘で想定される 1 発分の被弾。 マップ上ではまだ敵が確定していないので、
        /// **現在フロアの敵から最大値**を取る。 エスカレーションは序盤ターン (=1.00) で評価する。
        /// 進路選択・回復目標・イベント選択の 3 箇所が共通の危険度尺度として使うので public。</summary>
        public static int EstimateFloorMaxHit(GameLoop.RunState run)
        {
            if (run == null) return 0;
            int worst = 0;
            foreach (var e in CombatSystem.EnemyDatabase.GetByFloor(run.Depth))
            {
                if (e == null || e.elite || GameLoop.BossIds.IsBoss(e.id)) continue;   // 通常進行の見積りなのでボスとエリート敵は除く
                int h = EstimateMaxHit(e, 1);
                if (h > worst) worst = h;
            }
            // **ブロックを差し引く** (2026-09-15)。 上の見積りは「敵の最大上振れ × 防御ゼロ」で、
            //   ADR-0009 の被ダメは `lossBase = max(0, 敵攻撃値 − blockSum)` なのに blockSum が
            //   入っていなかった。 実測 (10,000 ラン): 平均ブロック 10.37 / 平均 lossBase 15.67 /
            //   完全防御 17.8%。 つまり `safetyHits = 3.0` は実質「5〜6 発ぶん」を確保していた。
            //
            //   **控えめに引く** (BlockCreditRatio < 1)。 過小評価は過大評価より危険で、
            //   2026-08-16 に 7層p4 で見積り 35 / 実際 57.5 の死角を作り、 p4 敗死 277 件の
            //   52% が回復アイテムを持ったまま死んでいた。 上振れたターンはブロックが足りない。
            //
            //   **ラン内の実績平均だけを使う** (run.AverageBlockSeen)。 GuardDiag はバッチ累計なので
            //   使うと「ラン i は runIdx だけの関数」が壊れる。 1 戦もしていなければ 0 ＝ 従来どおり。
            if (BlockCreditRatio > 0f && worst > 0)
                worst = Mathf.Max(1, Mathf.RoundToInt(worst - run.AverageBlockSeen * BlockCreditRatio));
            return worst;
        }


        // ================================================================
        //  危険度 (dangerTarget) の組み直し — 2026-09-15
        //
        //  **旧式は 4 箇所で過大評価していた。**
        //      dangerTarget = clamp(floorHit × safetyHits / maxHP, 0.5, 0.95)
        //      floorHit = max over 出現敵( baseAttack + 1 × attackRollMax )
        //    ① attackRollMax ＝ ロールの**上限**。 期待値ではない
        //    ② ブロックを引いていない (ADR-0009: lossBase = max(0, 敵攻撃値 − blockSum))。
        //       実測 平均ブロック 10.37 / 平均 lossBase 15.67 / 完全防御 17.8%
        //    ③ 「その層までに出る敵の最悪値」1 体ぶん。 実際の 1 戦は約 7.4 回の被弾
        //    ④ **結果として 4〜7 層は常にクランプ天井 0.95 に張り付く**
        //       (floorHit 41 × 3.0 / maxHP 86 = 1.43)。 1〜2 層は床 0.5。
        //       実際に効いていたのは 3 層だけで、 ゲートは事実上
        //       「序盤 50% / 中盤以降 95%」の二値だった。
        //
        //  **置き換えの考え方。** 「最悪の 1 発を N 回」ではなく
        //  **「次の 1 戦で失う体力」を実測から取る**。 ラン内の実績なので
        //  ブロック・軽減パッシブ・層の深さ・精鋭化・デバフが全部すでに入っている ──
        //  式を足し込む必要がない (RunState.AverageFightDamage)。
        //
        //  ただし平均だけでは**上振れた 1 発で落ちる**のを防げない。
        //  2026-08-16 に 7層p4 で見積り 35 / 実際 57.5 の死角を作り、
        //  p4 敗死 277 件の 52% が回復アイテムを持ったまま死んでいた前科がある。
        //  そこで **max(平均 1 戦ぶん × 係数, 最悪の 1 発)** を取る。
        // ================================================================

        /// <summary>「次の何戦ぶんの体力を確保して動くか」。 旧 safetyHits の後継。
        /// <b>2.0 は実測で採った値</b> ── 1.0/1.5/2.0/2.5 の差は 0.5pt 未満で、
        /// この軸はもう律速ではない (効いたのは値ではなく式を直したこと自体)。</summary>
        public static float SafetyFights = 2.0f;
        /// <summary>クランプ。 旧 [0.50, 0.95] は両端で飽和して判別力が無かった。</summary>
        public static float DangerFloor = 0.25f;
        public static float DangerCeil  = 0.90f;


        /// <summary>開示済みマスの敵から、そのマス専用の忌避しきい値を出す。
        /// <see cref="FightBudgetDanger"/> と同じ形で、 tailGuard だけ「層の最悪値」から
        /// 「このマスの敵」へ差し替える。 引けなければ 0 (＝呼び出し側が従来の lowBar を使う)。</summary>
        private static float TileDanger(MapNode node, bool elite, GameLoop.RunState run)
        {
            if (run == null || run.playerMaxHP <= 0) return 0f;
            var ea = CombatSystem.EnemyDatabase.Get(node.encounterEnemyA);
            if (ea == null) return 0f;

            // エリートマスの敵はエリート敵そのもの (2026-09-20) なので倍率は要らない。
            float tail = EstimateMaxHit(ea, 1);
            var eb = CombatSystem.EnemyDatabase.Get(node.encounterEnemyB);
            if (eb != null) tail += EstimateMaxHit(eb, 1);   // 2 体戦は攻撃が 2 パケット来る

            tail = Mathf.Max(1f, tail - run.AverageBlockSeen * BlockCreditRatio);
            float budget = run.AverageFightDamage * SafetyFights;
            return Mathf.Clamp(Mathf.Max(budget, tail) / run.playerMaxHP, DangerFloor, DangerCeil);
        }

        // [計装 2026-09-16] max(budget, tailGuard) の**どちらが勝っているか**を数える。
        //   片方が常に勝っていれば、 もう片方 (と、 それを動かすノブ) は死んでいる。
        //   今日 3 回踏んだ形なので、 値を振る前にここを読む。
        public static long DangerSamples, DangerBudgetWins, DangerClampLo, DangerClampHi;
        public static double DangerBudgetSum, DangerTailSum, DangerOutSum;
        public static void ResetDangerStats()
        { DangerSamples = DangerBudgetWins = DangerClampLo = DangerClampHi = 0;
          DangerBudgetSum = DangerTailSum = DangerOutSum = 0; }
        private static void NoteDanger(float budget, float tail, float maxHp)
        {
            DangerSamples++;
            if (budget >= tail) DangerBudgetWins++;
            DangerBudgetSum += budget; DangerTailSum += tail;
            float raw = Mathf.Max(budget, tail) / maxHp;
            if (raw <= DangerFloor) DangerClampLo++;
            else if (raw >= DangerCeil) DangerClampHi++;
            DangerOutSum += Mathf.Clamp(raw, DangerFloor, DangerCeil);
        }

        /// <summary>開示済みマスで忌避しきい値を引き直すか。 <b>既定 OFF</b> ── A/B で測ってから畳む。</summary>
        public static bool UseTileDanger = false;

        /// <summary>1 戦ぶんの体力予算から危険度を出す。
        /// <paramref name="floorHit"/> は <see cref="EstimateFloorMaxHit"/> (ブロック控除済み) の値。</summary>
        private static float FightBudgetDanger(GameLoop.RunState run, int floorHit)
        {
            if (run == null || run.playerMaxHP <= 0) return DangerFloor;
            float maxHp = run.playerMaxHP;

            // ① 平均 1 戦ぶん × 係数。 **実績が無い間 (1 戦目) は 0** なので ② が効く。
            float budget = run.AverageFightDamage * SafetyFights;

            // ② 最悪の 1 発。 上振れで即死しないための床。
            float tailGuard = floorHit;

            NoteDanger(budget, tailGuard, maxHp);
            return Mathf.Clamp(Mathf.Max(budget, tailGuard) / maxHp, DangerFloor, DangerCeil);
        }

        /// <summary>被弾見積りから差し引くブロックの割合 (0 = 引かない ＝ 2026-09-15 以前の挙動)。
        /// **既定は 0。** 機構だけ入れて値は較正で決める ── 測らずに既定を動かすと、
        /// 進路選択・回復目標・イベント選択の 3 箇所が一度に変わる。
        /// 較正ノブは <c>AutoRunner.navBlockCredit</c>。</summary>
        public static float BlockCreditRatio = 0.7f;

        /// <summary>特売の購入意欲補正のしきい値 (%)。 <b>50/30 へ下げると悪化する</b>
        /// (2026-09-17 実測・全条件で −0.5〜−1.4pt)。 段 {15,30,50} のうち 50% だけを
        /// 拾う線引きが正しい ── 下げると「安いだけの品」が足切りを通る。</summary>
        public static int SaleBonusHiPct = 60, SaleBonusLoPct = 40;

        // ================================================================
        //  階層まるごとの後ろ向き解 (④)
        //
        //  既存の航行評価は **1 手先まで**しか見ず、 しかもその先を 0.6 の手調整係数で
        //  割り引いていた。 だが 1 階層のマップは高々 10 行程度の小さな DAG で、
        //  ボス行から後ろ向きに解けば出口までの最良経路が**厳密に**求まる。
        //  rollout は 1 本も要らず、 コストはマイクロ秒。
        //
        //  **既定 OFF。** Super は全測定の基準線なので、 黙って強くすると
        //  既存の基準値がまとめて無効になる。 A/B で測ってから既定を動かすこと。
        // ================================================================

        // ================================================================
        //  アブレーション (マクロ判断の価値を「無くして測る」)
        // ================================================================
        //  技量帯スイープは `wiringSkill` しか振っておらず、 航行・ショップ・報酬・イベントは
        //  **両アームで同一**だった ── マクロ判断の技量帯は一度も測られていない。
        //  丸ごと弱いマクロ AI を作るより、 **1 軸ずつランダム化して落ち幅を測る**方が安く、
        //  しかも軸ごとに順位が付く。 落ちない軸は「そこに技量が無い」という設計上の答えになる。
        //
        //  **専用乱数を使う。** GameRng を消費するとマップ・敵・アイテムの抽選がずれて
        //  同一シードのペア比較が壊れる。 ここは判断だけを潰し、 世界は動かさない。

        /// <summary>航行の選択を一様抽選へ潰す (既定 OFF)。 実測 −13.4pt (p&lt;0.0001)。</summary>
        [NonSerialized] public bool ablateNavigation;

        // ---- [計装] 航行スコアの同点率 ----
        //   航行は 13.4pt の軸。 `Rank()` が int を返すので同点がリスト順で割れている疑い。
        //   **直す前に頻度を数える** (戦闘では eps 同点と厳密同値を取り違えて 6 倍過大に見た)。
        private readonly List<float> _navScores = new List<float>(16);
        public static long NavDecisions, NavTiedDecisions, NavTiedSum, NavCandSum;
        public static void ResetNavStats()
            => NavDecisions = NavTiedDecisions = NavTiedSum = NavCandSum = 0;
        /// <summary>7層だけの与ダメ倍率内訳。 **1 攻撃あたりに正規化して出す** ──
        /// アーム間でラン数も到達率も違うので、 合計値では比べられない。</summary>
        private static string DescribeDamageBreakdown7F()
        {
            var b = CombatStats.DamageBreakdown7F;
            double n = b[0];
            if (n < 1) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"【7層だけの与ダメ内訳 (1攻撃あたり)】攻撃 {n:N0} 回");
            sb.AppendLine($"  素火力 {b[1] / n,6:F2} + 攻撃端子出目 {b[2] / n,6:F2}"
                        + $" + パッシブ加算 {b[3] / n,5:F2} + 消費 {b[4] / n,4:F2} + 遺物 {b[5] / n,4:F2}"
                        + $"  = atkBase {b[6] / n,6:F2}");
            sb.AppendLine($"  **最終与ダメ {b[7] / n,7:F2}** / 固定ダメ {b[9] / n,5:F2}"
                        + $" / 追撃 {b[10] / n,5:F2} / 会心率 {100.0 * b[8] / n,5:F1}%");
            sb.AppendLine($"  与ダメ倍率: パッシブ {b[11] / n,5:F3} → メタ +{b[13] / n,5:F3}"
                        + $" → 遺物 +{b[15] / n,5:F3}"
                        + $"  = **最終 {b[16] / n,5:F3}**");
            sb.AppendLine($"  ※atkBase から最終与ダメへの実効倍率 {(b[6] > 0 ? b[7] / b[6] : 0),5:F3}"
                        + "   ── ここがアーム間で違えば倍率、 同じなら配分の問題");
            return sb.ToString();
        }

        public static string DescribeNavTies()
        {
            if (NavDecisions <= 0) return "";
            double d = NavDecisions;
            return "【航行スコアの同点率】判断 " + NavDecisions.ToString("N0")
                 + $" / 平均候補 {NavCandSum / d:F2} 本\n"
                 + $"  最良が同点だった判断 {NavTiedDecisions:N0} ({100.0 * NavTiedDecisions / d:F1}%)"
                 + $" / 同点 平均 {NavTiedSum / d:F2} 本"
                 + "   ※同点はリスト順で決まる ＝ そこは判断していない\n";
        }
        /// <summary>ショップの購入判断を潰す (買えるものから一様に買えるだけ買う)。</summary>
        [NonSerialized] public bool ablateShop;
        /// <summary>イベントの選択判断を潰す (提示された選択肢から一様抽選)。</summary>
        [NonSerialized] public bool ablateEvent;
        /// <summary>ショップの**順位付けだけ**を潰す (足切りは残し、通った候補から一様抽選)。
        /// <see cref="ablateShop"/> が「買い物の作法ごと潰す」のに対し、 こちらは
        /// 「どれが最良かを知っていること」だけの価値を切り出す。</summary>
        [NonSerialized] public bool ablateShopScore;

        [NonSerialized] private System.Random _ablationRng;
        private System.Random AblationRng => _ablationRng ?? (_ablationRng = new System.Random(0x_AB1A));
        /// <summary>ラン開始時に呼ぶ。 同一シードのラン比較でアブレーションまで再現させる。</summary>
        private void ResetAblationRng(int runSeed)
        {
            unchecked { _ablationRng = new System.Random((runSeed * 1103515245 + 12345) & 0x7FFFFFFF); }
        }

        /// <summary>階層 DP を使う (既定 OFF)。 立てると 1 手先読みが階層全体の解に変わる。</summary>
        [NonSerialized] public bool useFloorDpNavigation;
        /// <summary>DP の 1 段あたりの割引。 既定は既存の先読みと同じ 0.6。
        /// **0.6 のままだと 10 行先は 0.6^10 ≒ 0.006 で実質 1 手先読みと変わらない** ──
        /// 構造を入れただけで満足せず、 ここを掃引して初めて DP の意味が出る。</summary>
        [NonSerialized] public float floorDpDiscount = 0.6f;

        /// <summary>HP 比の量子化段数。 状態は (ノード, HP帯)。
        ///
        /// <para><b>これは精度のつまみではなく、実験の軸。</b> 1 手先読みは HP 比を float の
        /// まま <see cref="Rank"/> へ渡すが、DP は帯へ丸める ── 丸めが判定を反転させうるので、
        /// 深さを測るつもりで粗さも一緒に動かしてしまう交絡になる (2026-08-21 に実際に踏んだ)。
        /// 大きくすれば量子化はほぼ消え、DP は 1 手先読みと一致するはずで、
        /// **それが実装の正当性チェックになる**。</para></summary>
        [NonSerialized] public int floorDpHpBands = 11;   // 既定 = 0.1 刻み

        private int FloorDpHpBands { get { return Mathf.Clamp(floorDpHpBands, 2, 201); } }

        private float[] _dpValue;
        private bool[] _dpDone;
        private readonly Dictionary<string, int> _dpIndex =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private int HpBandOf(float ratio)
        {
            int bands = FloorDpHpBands;
            return Mathf.Clamp(Mathf.RoundToInt(ratio * (bands - 1)), 0, bands - 1);
        }

        /// <summary>1 決定ぶんのメモ表を用意する。 盤面は決定ごとに変わりうるので毎回作り直す
        /// (使い回して古い値を引くのは、 一番見つけにくい壊れ方)。</summary>
        private void FloorDpPrepare(FloorMap map)
        {
            _dpIndex.Clear();
            if (map == null) return;
            var nodes = map.GetAllNodes();
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] != null) _dpIndex[nodes[i].id] = i;

            int need = nodes.Count * FloorDpHpBands;
            if (_dpValue == null || _dpValue.Length < need)
            {
                _dpValue = new float[need];
                _dpDone = new bool[need];
            }
            Array.Clear(_dpDone, 0, need);
        }

        /// <summary>ノード <paramref name="node"/> を踏んでから階層の出口までの評価値
        /// (小さいほど良い)。 自身の <see cref="Rank"/> を含む。
        ///
        /// <para><b>再帰は前進辺だけを辿る。</b> 横移動辺 (row が増えない) を DP の中で辿ると
        /// 同一行を往復して循環しうる。 横移動は呼び出し側の pool が既に候補として持って
        /// いるので、 ここで扱わなくても選択肢は失われない ── かつ DAG であることが
        /// 構造的に保証され、 停止性を別途証明しなくてよくなる。</para>
        ///
        /// <para>HP は帯に量子化して状態に持つ。 「回復してからエリート」のような組み立ては
        /// HP が状態に入っていないと表現できない。</para></summary>
        private float FloorDpValue(FloorMap map, MapNode node, int band, int floorHit,
                                   GameLoop.RunState run, float lowBar)
        {
            if (map == null || node == null) return 0f;
            if (!_dpIndex.TryGetValue(node.id, out int idx)) return 0f;

            int bands = FloorDpHpBands;
            int key = idx * bands + band;
            if (_dpDone[key]) return _dpValue[key];

            float hp = band / (float)(bands - 1);
            // preferRest は「いま休むべきか」の判断なので手前の 1 手にしか掛けない。
            // 先の行にまで掛けると、 遠くの休憩マスが実際より魅力的に見える。
            float total = Rank(node, hp, _curCombatAverse, false, run, lowBar);

            var succ = map.GetReachableFrom(node.id);
            if (succ != null && succ.Count > 0)
            {
                int nextBand = HpBandOf(
                    PredictHpRatioAfter(node, hp, floorHit, run));
                float bestNext = float.MaxValue;
                for (int i = 0; i < succ.Count; i++)
                {
                    MapNode s = succ[i];
                    if (s == null || s.row <= node.row) continue;   // 前進辺のみ = 循環しない
                    float v = FloorDpValue(map, s, nextBand, floorHit, run, lowBar);
                    if (v < bestNext) bestNext = v;
                }
                if (bestNext != float.MaxValue) total += floorDpDiscount * bestNext;
            }

            _dpValue[key] = total;
            _dpDone[key] = true;
            return total;
        }

        // 2026-09-28: LateralPenalty (横移動の物資ペナルティ) は横移動の廃止に伴い削除 (docs/GAME.md §24)。

        /// <summary>そのタイルを踏んだ後の HP 割合の概算。 2 手先読みで「次の状態」を作るために使う。
        /// 精度は要らない ── 必要なのは「回復系なら上がる / 戦闘系なら下がる」の向きだけ。
        /// 戦闘は想定被弾 1 発ぶん、 エリートは 2 発ぶん減るものとして見る。</summary>
        private static float PredictHpRatioAfter(MapNode node, float hpRatio, int floorHit, GameLoop.RunState run)
        {
            if (node == null || run == null || run.playerMaxHP <= 0) return hpRatio;
            TileType t = node.EffectiveType;
            float unit = floorHit > 0 ? (float)floorHit / run.playerMaxHP : 0.12f;

            // 2 体戦は攻撃が 2 パケット来る。 **開示済みのときだけ**見込みに織り込む
            //   (未開示のプリセットを読むと 1 マス前開示という規則を BOT だけが破ることになる)。
            //   戦闘長は伸びない (与ダメは両方に入るので撃破ターンは max) ので、
            //   増えるのは 1 ターンあたりの被ダメ ＝ 素直に倍で見る。
            if (node.encounterRevealed && !string.IsNullOrEmpty(node.encounterEnemyB)) unit *= 2f;

            switch (t)
            {
                case TileType.Rest:        return Mathf.Min(1f, hpRatio + 0.30f);
                case TileType.Battle:      return Mathf.Max(0f, hpRatio - unit);
                case TileType.EliteBattle: return Mathf.Max(0f, hpRatio - unit * 2f);
                case TileType.Boss:        return Mathf.Max(0f, hpRatio - unit * 3f);
                default:                   return hpRatio;   // ショップ/イベント/宝箱は増減なしとみなす
            }
        }

        /// <summary>低いほど優先。HP帯(危機/低/健康)で重み分け。
        /// 戦闘タイル(Battle/EliteBattle)のみ profile で差し替え、他は共通固定。
        /// averse=false(戦闘貪欲): 健康なら戦闘を最優先級で選ぶ。
        /// averse=true(戦闘回避): 戦闘を最下位級にし、戦闘以外があれば必ず回避。</summary>
        /// <summary>金の逼迫度を測る基準額。 店頭価格 4〜20G・1 ラン 19 個購入が目標なので、
        /// 「いま 3〜4 個買える」= 逼迫していない、 とみなす額に置く。</summary>
        /// <summary>「これだけ持っていれば金に困っていない」水準。 <c>gp = clamp01(1 − 所持/この値)</c>。
        ///
        /// <para><b>収入と一緒に動かすこと。</b> 60 は収入が 100G/ラン 前後だった頃の目盛りで、
        /// 2026-09-15 の経済作り直し後は 456G/ラン。 実測 (2,000 ラン) で
        /// <b>判断の 36.6% が gp=0 に飽和</b>していた ── そこでは「稼ぐ必要性」の項も、
        /// エリートの報酬倍率 (goldPull = 0.8 × RewardMul × gp) も、
        /// ショップの優先度 (1 + 2.0gp − 0.8(1−gp)) も<b>まとめて定数に潰れる</b>。</para></summary>
        public static float GoldComfort = 60f;

        /// <summary>0 = 潤沢 / 1 = 無一文。</summary>
        // [計装 2026-09-15] gp の分布。 **飽和しているかを数える。**
        //   gp = clamp01(1 - coins/GoldComfort) は所持 GoldComfort 以上で 0 に張り付く。
        //   GoldComfort=60 は収入が 100G/ラン の頃の値で、 現在は 449G/ラン。
        //   gp が 0 のとき「稼ぐ必要性」の項が丸ごと消え、 エリートの報酬倍率
        //   (goldPull = 0.8 × RewardMul × gp) も同時に無効化される。
        public static long GpSamples, GpZero, GpOne; public static double GpSum;
    }
}

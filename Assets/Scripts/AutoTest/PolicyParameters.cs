using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// BOT の挙動パラメータ。 ゲーム全体の6サブシステムを横断する11軸を JSON 化。
    ///
    /// サブシステム分布 (偏らないよう各系から1-2軸ずつ取る):
    ///   ・ショップ系 (2軸): rerollCostRatio / consumableStockMax
    ///   ・強盗系 (1軸):     robberyMinHpRatio  (フロア閾値は固定)
    ///   ・イベント系 (1軸): eventExplorationRate
    ///   ・戦闘系 (2軸):     importantThreatThreshold / emergencyHealRatio
    ///   ・航行系 (3軸):     hpCritThreshold / lateralHopeFloor(損耗点) / hopeRefillFloor(補充点)
    ///   (昇華系 1軸 sublimationReserve は 2026-09-11 撤去 ── 昇華そのものが未使用のため)
    ///
    /// PolicyExplorer が毎バッチ 1軸を摂動させて bandScore 平均で評価。
    /// </summary>
    [Serializable]
    public class PolicyParameters
    {
        // === ショップ系 ===
        /// <summary>リロール起動可能なコスト比 (所持G に対する割合)。</summary>
        public float rerollCostRatio = 0.30f;
        /// <summary>回復消費アイテムのストック上限 (通常ショップで何個まで買い溜めるか)。</summary>
        public int consumableStockMax = 3;

        // === 強盗系 ===
        /// <summary>強盗試行に必要な (playerHP / playerMaxHP) の比率。</summary>
        public float robberyMinHpRatio = 0.60f;

        // === イベント系 ===
        /// <summary>イベント選択肢の次点を選ぶ確率 (探索性)。</summary>
        public float eventExplorationRate = 0.10f;

        // === 戦闘系 ===
        /// <summary>「重要戦闘」と見なす敵threat 下限。 これ以上でバフ消費を解禁。</summary>
        public int importantThreatThreshold = 5;
        /// <summary>緊急回復トリガ: HP &lt;= 敵最大ヒット × emergencyHealRatio で消費。 1.0=同等で発動。</summary>
        public float emergencyHealRatio = 1.0f;

        // === 航行系 ===
        // [撤去 2026-09-15] safetyHits / hpLowThreshold
        //   危険度 (dangerTarget) の式を組み直したのに伴い、 2 軸とも役目を終えた。
        //   旧式 `clamp(floorHit × safetyHits / maxHP, 0.5, 0.95)` は 4〜7 層で常に天井 0.95、
        //   1〜2 層で床 0.5 に張り付き、 実際に効いていたのは 3 層だけだった
        //   (floorHit が「敵の最大上振れ × 防御ゼロ」の二重の最悪値だったため)。
        //   新式は **ラン内の実測「1 戦で失う体力」** から出す (AutoRunner.FightBudgetDanger)。
        //   hpLowThreshold の床も DangerFloor と tailGuard が二重に担っており、
        //   **2 つの床が競合してどちらかが必ず飽和する**形だったので一本化した。
        //   実測 (10,000 ラン/アーム): クリア率 15.86% → 20.45% (z=+11.62)、
        //   抱え落ち 20.5% → 17.9% (＝ 見積りが甘くなったのではなく正確になった)。
        //   経緯は docs/GAME.md §24。

        /// <summary>HP危機境界 (危機判定)。 これ未満で Rest を最優先タイルへ。</summary>
        public float hpCritThreshold = 0.30f;
        /// <summary>横移動(寄り道)を許可する希望の下限。 現在希望がこの値を超えるときだけ希望-LateralCost を払って寄り道する。
        /// 低いほど「希望を資源として深く損耗してでも利得を取りにいく」、 高いほど温存重視。 L2が勝率(composite)で最適点を学習。
        /// 既定20 = 旧ハードコード(絶望帯≤20で見送り)と同等。</summary>
        public float lateralHopeFloor = 20f;
        /// <summary>希望回復(食料消費)を始める希望の上限。 現在希望がこの値以下になったら食料で補充する。
        /// 高いほど早めに補充(温存・安全)、 低いほど枯渇近くまで引っ張る(食料を出し惜しみ他用途へ)。 L2が勝率で最適化。
        /// 既定45 = 旧ハードコード(悲観帯≤45で補充)と同等。 ※佯狂者の冠所持時は発狂狙いのため別途補充しない。</summary>
        public float hopeRefillFloor = 45f;
        /// <summary>希望の補充を **hopeCap のこの割合で打ち切る**。 既定 1.0 = 上限まで満たす（従来動作）。
        ///
        /// 1 未満にすると「補充はするが上へ戻し切らない」＝ **希望を帯の中に留める**。
        /// 〈渇き〉のように *低希望でだけ発動する* 効果を維持するために要る ──
        /// 補充を止めるだけ (hopeRefillFloor=0) だと希望が 0 まで落ちて発狂でランが終わり、
        /// 上限まで満たすと発動条件から外れる。 実測ではどちらも軸が死んだ (2026-08-08)。
        ///
        /// 使い方: 発動閾値の少し下を天井にし、 床は発狂を避けられる高さに置く。
        /// 例) 〈渇き〉は 希望 ≤ hopeCap×40% で発動 → 天井 0.38 / 床 18 で帯 [18, 38] を維持。</summary>
        public float hopeBandCeilRatio = 1f;

        // === 昇華系 ===
        // [削除 2026-09-11] sublimationReserve ── 〈昇華〉の積極度。
        //   昇華 BOT ヒューリスティク (AutoRunner.TryBotSublimate) ごと削除。 詳細は同所のコメント。

        // === スタンス系（ADR-0006・BOT学習） ===
        /// <summary>防御スタンスに入る勝率閾値。ロール前推定 P(勝) がこれ未満なら防御。低いほど攻めっ気。L2が勝率で最適化。</summary>
        public float stanceDefendWinProb = 0.35f;
        /// <summary>HPが低いほど防御閾値を引き上げる量。実効閾値 = stanceDefendWinProb + これ×(1−HP割合)。高いほど瀕死で慎重。</summary>
        public float stanceDefendHpBias = 0.30f;

        // === メタ情報 ===
        /// <summary>このパラメータセットで観測した bandScore 平均 (探索の評価軸)。</summary>
        public float lastBandScoreAvg = 0f;
        /// <summary>このパラメータセットの試行回数 (バッチ単位)。</summary>
        public int trialBatches = 0;
        /// <summary>最終更新時刻。</summary>
        public string updatedAt = "";

        // ============================================================
        //  シングルトンアクセス
        // ============================================================
        private static PolicyParameters _current;
        public static PolicyParameters Current
        {
            get
            {
                if (_current == null) _current = LoadOrDefault();
                return _current;
            }
        }

        public static void ReloadFromDisk(string learningRoot = null)
        {
            _current = LoadFromPath(GetPath(learningRoot));
        }

        /// <summary>L2ペアテスト用: 実行時に Current を差し替える。</summary>
        public static void SetCurrent(PolicyParameters p)
        {
            if (p == null) return;
            _current = p;
        }

        public static void SaveToDisk(string learningRoot = null)
        {
            try
            {
                if (_current == null) return;
                string path = GetPath(learningRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                _current.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.WriteAllText(path, JsonUtility.ToJson(_current, true), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning($"[PolicyParameters] save fail: {e.Message}"); }
        }

        private static string GetPath(string learningRoot)
        {
            if (string.IsNullOrEmpty(learningRoot))
                learningRoot = MetaProfileHelper.LearningRoot();
            return Path.GetFullPath(Path.Combine(learningRoot, "policy.json"));
        }

        private static PolicyParameters LoadOrDefault() => LoadFromPath(GetPath(null));

        private static PolicyParameters LoadFromPath(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var txt = File.ReadAllText(path, Encoding.UTF8);
                    var p = JsonUtility.FromJson<PolicyParameters>(txt);
                    if (p != null) return p;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[PolicyParameters] load fail: {e.Message}"); }
            return new PolicyParameters();
        }

        // ============================================================
        //  クランプ
        // ============================================================
        public void Clamp()
        {
            rerollCostRatio          = Mathf.Clamp(rerollCostRatio, 0.10f, 0.60f);
            consumableStockMax       = Mathf.Clamp(consumableStockMax, 0, 8);
            robberyMinHpRatio        = Mathf.Clamp(robberyMinHpRatio, 0.30f, 1.00f);
            eventExplorationRate     = Mathf.Clamp(eventExplorationRate, 0.00f, 0.40f);
            importantThreatThreshold = Mathf.Clamp(importantThreatThreshold, 2, 9);
            emergencyHealRatio       = Mathf.Clamp(emergencyHealRatio, 0.50f, 2.00f);
            hpCritThreshold          = Mathf.Clamp(hpCritThreshold, 0.10f, 0.50f);
            lateralHopeFloor         = Mathf.Clamp(lateralHopeFloor, 0f, 60f);
            hopeRefillFloor          = Mathf.Clamp(hopeRefillFloor, 0f, 75f);
            hopeBandCeilRatio        = Mathf.Clamp(hopeBandCeilRatio, 0.10f, 1.00f);
            stanceDefendWinProb      = Mathf.Clamp(stanceDefendWinProb, 0f, 0.90f);
            stanceDefendHpBias       = Mathf.Clamp(stanceDefendHpBias, 0f, 0.60f);
            // 整合性: crit < low を強制
        }

        public PolicyParameters Clone()
        {
            return new PolicyParameters
            {
                rerollCostRatio          = rerollCostRatio,
                consumableStockMax       = consumableStockMax,
                robberyMinHpRatio        = robberyMinHpRatio,
                eventExplorationRate     = eventExplorationRate,
                importantThreatThreshold = importantThreatThreshold,
                emergencyHealRatio       = emergencyHealRatio,
                hpCritThreshold          = hpCritThreshold,
                lateralHopeFloor         = lateralHopeFloor,
                hopeRefillFloor          = hopeRefillFloor,
                hopeBandCeilRatio        = hopeBandCeilRatio,
                stanceDefendWinProb      = stanceDefendWinProb,
                stanceDefendHpBias       = stanceDefendHpBias,
                lastBandScoreAvg         = lastBandScoreAvg,
                trialBatches             = trialBatches,
                updatedAt                = updatedAt,
            };
        }

        public string Summary()
        {
            return $"shop[reroll={rerollCostRatio:F2} cons={consumableStockMax}] "
                 + $"rob[hp%={robberyMinHpRatio:F2}] event[exp={eventExplorationRate:F2}] "
                 + $"combat[thr={importantThreatThreshold} heal={emergencyHealRatio:F2}] "
                 + $"nav[crit={hpCritThreshold:F2} latHope={lateralHopeFloor:F0} refill={hopeRefillFloor:F0}→{hopeBandCeilRatio:P0}] "
                 + $"stance[def<{stanceDefendWinProb:F2} hpBias={stanceDefendHpBias:F2}] "
                 + $"| last avg={lastBandScoreAvg:F2} batches={trialBatches}";
        }
    }
}

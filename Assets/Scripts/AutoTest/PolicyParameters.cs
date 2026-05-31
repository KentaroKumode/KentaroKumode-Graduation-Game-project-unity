using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// BOT の挙動パラメータ。 ゲーム全体の5サブシステムを横断する8軸を JSON 化。
    ///
    /// サブシステム分布 (偏らないよう各系から1-2軸ずつ取る):
    ///   ・ショップ系 (2軸): rerollCostRatio / consumableStockMax
    ///   ・強盗系 (1軸):     robberyMinHpRatio  (フロア閾値は固定)
    ///   ・イベント系 (1軸): eventExplorationRate
    ///   ・戦闘系 (2軸):     importantThreatThreshold / emergencyHealRatio
    ///   ・航行系 (2軸):     hpLowThreshold / hpCritThreshold
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
        /// <summary>HP低下境界 (低HP判定)。 これ未満で戦闘タイルを忌避し始める。</summary>
        public float hpLowThreshold = 0.55f;
        /// <summary>HP危機境界 (危機判定)。 これ未満で Rest を最優先タイルへ。</summary>
        public float hpCritThreshold = 0.30f;

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
            hpLowThreshold           = Mathf.Clamp(hpLowThreshold, 0.30f, 0.80f);
            hpCritThreshold          = Mathf.Clamp(hpCritThreshold, 0.10f, 0.50f);
            // 整合性: crit < low を強制
            if (hpCritThreshold >= hpLowThreshold) hpCritThreshold = hpLowThreshold - 0.10f;
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
                hpLowThreshold           = hpLowThreshold,
                hpCritThreshold          = hpCritThreshold,
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
                 + $"nav[low={hpLowThreshold:F2} crit={hpCritThreshold:F2}] "
                 + $"| last avg={lastBandScoreAvg:F2} batches={trialBatches}";
        }
    }
}

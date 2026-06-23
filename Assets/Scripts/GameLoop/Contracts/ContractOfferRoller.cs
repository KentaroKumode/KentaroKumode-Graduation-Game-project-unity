using System.Collections.Generic;
using UnityEngine;

namespace GameLoop.Contracts
{
    /// <summary>
    /// 前哨基地での契約提示抽選。 既契約者以外から各 30% 独立抽選で 0 〜 N 個提示。
    /// 既契約者については 「延長」 ボタンを別途出す (Roll は新規のみ)。
    ///
    /// 「同層で解除された旅団は再提示しない」 ルールは、 RunState に "同層失効プール" を持って制御。
    /// (現状は GameManager 側で OnLayerStart 時にプールクリアする実装方針)
    /// </summary>
    public static class ContractOfferRoller
    {
        /// <summary>1 旅団あたりの提示確率 (仕様凍結値)。</summary>
        public const float DefaultPerContractChance = 0.30f;

        /// <summary>新規契約抽選。 既契約者・同層失効プールに該当する旅団は除外。</summary>
        public static List<ContractKind> RollOffers(RunState run, float chance = DefaultPerContractChance)
        {
            var result = new List<ContractKind>();
            if (run == null) return result;

            var active = new HashSet<ContractKind>();
            if (run.activeContracts != null)
                foreach (var c in run.activeContracts)
                    active.Add(c.kind);

            var expiredThisLayer = run.contractsExpiredThisLayer != null
                ? new HashSet<ContractKind>(run.contractsExpiredThisLayer)
                : new HashSet<ContractKind>();

            foreach (ContractKind k in System.Enum.GetValues(typeof(ContractKind)))
            {
                if (active.Contains(k)) continue;
                if (expiredThisLayer.Contains(k)) continue;
                if (Random.value < chance) result.Add(k);
            }
            return result;
        }

        /// <summary>延長候補: 既契約者かつ L3 未満のもの。</summary>
        public static List<ContractInstance> ListExtendable(RunState run)
        {
            var r = new List<ContractInstance>();
            if (run?.activeContracts == null) return r;
            foreach (var c in run.activeContracts)
            {
                if (c.level < ContractCost.MaxLevel) r.Add(c);
            }
            return r;
        }

        /// <summary>新規契約を結ぶ (UI から呼ぶ)。 敵対契約は強制解除される。</summary>
        public static List<ContractInstance> Sign(RunState run, ContractKind kind, int level = 1)
        {
            int cost = ContractCost.For(level);
            if (run == null || run.coins < cost) return null; // 失敗
            run.coins -= cost;
            return ContractManager.Instance.SignNew(run, kind, level);
        }

        /// <summary>延長 (UI から呼ぶ)。 次レベルの差額を支払う。</summary>
        public static bool Extend(RunState run, ContractKind kind)
        {
            var c = ContractManager.Instance.Find(run, kind);
            if (c == null || c.level >= ContractCost.MaxLevel) return false;
            int nextCost = ContractCost.For(c.level + 1);
            if (run.coins < nextCost) return false;
            run.coins -= nextCost;
            c.LevelUp();
            return true;
        }
    }
}

using System;

namespace GameLoop.Contracts
{
    /// <summary>
    /// 現在発効中の契約 1 件分の状態。 RunState.activeContracts に保持される。
    /// </summary>
    [Serializable]
    public class ContractInstance
    {
        public ContractKind kind;
        public int level;                     // 1〜3
        public int layerEntered;              // 契約締結時の層 (デバッグ・統計用)

        // 旅団固有の永続カウンタ
        public int bodyDoublesRemainingRevives;  // 影武者一座: ラン全体での残復活回数
        public int merchantsLeagueLayerLossFlag; // 商業連合隊: HP50%↓ で 1 になる (層内フラグ)

        // 補給キャラバン: 層に1回ずつ使えるシステム解放の消費フラグ
        public bool supplyShopUsedThisLayer;
        public bool supplyEnhanceUsedThisLayer;
        public bool supplyRestUsedThisLayer;

        // 戦術家: 戦闘ごとに振り直しチャージ ── 戦闘内で消費、 戦闘開始時にリセット
        public int tacticiansRerollsRemainingThisCombat;

        public ContractInstance() { }

        public ContractInstance(ContractKind k, int lv, int layer)
        {
            kind = k;
            level = lv;
            layerEntered = layer;
            ResetCounters();
        }

        /// <summary>契約締結時の初期化。 影武者は契約レベルで復活回数を設定。</summary>
        public void ResetCounters()
        {
            if (kind == ContractKind.BodyDoubles)
                bodyDoublesRemainingRevives = level;
            merchantsLeagueLayerLossFlag = 0;
            supplyShopUsedThisLayer = false;
            supplyEnhanceUsedThisLayer = false;
            supplyRestUsedThisLayer = false;
            tacticiansRerollsRemainingThisCombat = 0;
        }

        /// <summary>層終了時の状態クリア (層をまたぐカウンタのリセット)。</summary>
        public void OnLayerEnd()
        {
            merchantsLeagueLayerLossFlag = 0;
            supplyShopUsedThisLayer = false;
            supplyEnhanceUsedThisLayer = false;
            supplyRestUsedThisLayer = false;
        }

        /// <summary>レベルアップ時に呼ぶ。 影武者の残回数は L3 までで段階的に増加。</summary>
        public void LevelUp()
        {
            if (level >= ContractCost.MaxLevel) return;
            level++;
            // 影武者: 復活回数はラン全体で 1/2/3 回 (リチャージ無し)。 レベルアップで残数を補充
            if (kind == ContractKind.BodyDoubles)
                bodyDoublesRemainingRevives = level;
        }

        public int CurrentMaintenanceCost => ContractCost.For(level);

        /// <summary>HP20% 解除の免除フラグ (L3 は免除)。</summary>
        public bool IsImmuneToHpReleaseRule => level >= 3;
    }
}

using InventorySystem.PassiveSkills;

namespace GameLoop.Contracts
{
    /// <summary>
    /// 旅団契約の効果を実装するためのインターフェース。
    /// ContractManager が active 契約の各 hook を順に呼ぶ。
    ///
    /// 旅団によって関心のあるイベントが異なるので、 基底クラス ContractEffectBase で
    /// no-op デフォルト実装を提供し、 各契約は必要なフックだけオーバーライドする。
    /// </summary>
    public interface IContractEffect
    {
        ContractKind Kind { get; }

        /// <summary>戦闘開始時 (CombatManager.StartCombatInternal の OnBattleStart フェーズ相当)。</summary>
        void OnBattleStart(ContractInstance state, CombatContext ctx, RunState run);

        /// <summary>ターン終了時 (CombatManager のターン進行で呼ばれる)。</summary>
        void OnTurnEnd(ContractInstance state, CombatContext ctx, RunState run);

        /// <summary>戦闘終了時 (勝利/敗北/引分含む)。 result は勝利時のダメ累計など。</summary>
        void OnBattleEnd(ContractInstance state, RunState run, ContractBattleResult result);

        /// <summary>層突入時 (前哨基地経由)。 維持費徴収はここで行わず、 ContractManager 側で一括処理。</summary>
        void OnLayerStart(ContractInstance state, RunState run);

        /// <summary>層終了時 (次の前哨基地への遷移直前)。</summary>
        void OnLayerEnd(ContractInstance state, RunState run);

        /// <summary>ロール勝利時 (会心フラグ別で呼び分けは呼出元責任)。</summary>
        void OnRollWin(ContractInstance state, CombatContext ctx, RunState run, bool wasCritical);
    }

    /// <summary>戦闘終了時に IContractEffect.OnBattleEnd へ渡される簡易リザルト。</summary>
    public class ContractBattleResult
    {
        public bool playerWon;
        public int finalPlayerHp;
        public int playerMaxHp;
        public int totalDamageDealt;     // 与ダメ累計 (狩猟旅団+医術官協力で参照)
        public CombatSystem.EnemyKind enemyKind;
    }

    /// <summary>no-op デフォルトを提供する基底クラス。 各契約効果はこれを継承して必要な hook だけ書き換える。</summary>
    public abstract class ContractEffectBase : IContractEffect
    {
        public abstract ContractKind Kind { get; }
        public virtual void OnBattleStart(ContractInstance state, CombatContext ctx, RunState run) { }
        public virtual void OnTurnEnd(ContractInstance state, CombatContext ctx, RunState run) { }
        public virtual void OnBattleEnd(ContractInstance state, RunState run, ContractBattleResult result) { }
        public virtual void OnLayerStart(ContractInstance state, RunState run) { }
        public virtual void OnLayerEnd(ContractInstance state, RunState run) { }
        public virtual void OnRollWin(ContractInstance state, CombatContext ctx, RunState run, bool wasCritical) { }
    }
}

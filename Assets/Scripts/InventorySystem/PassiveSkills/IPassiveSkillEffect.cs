namespace InventorySystem.PassiveSkills
{
    /// <summary>
    /// パッシブスキルの実行インターフェース
    /// 
    /// 設計原則:
    /// - 1スキル = 1クラス（単一責任）
    /// - ステートレス（状態はCombatContextに保持）
    /// - 並列実行安全（他スキルへの依存なし）
    /// </summary>
    public interface IPassiveSkillEffect
    {
        /// <summary>スキルの一意な識別名（JSONの skillName と一致）</summary>
        string SkillId { get; }

        /// <summary>このスキルが反応するトリガー群</summary>
        PassiveSkillTrigger[] Triggers { get; }

        /// <summary>
        /// スキル効果を実行
        /// </summary>
        /// <param name="trigger">発火したトリガー</param>
        /// <param name="ctx">戦闘コンテキスト（読み書き可能）</param>
        void Execute(PassiveSkillTrigger trigger, CombatContext ctx);
    }

    /// <summary>ラン跨ぎで永続するインスタンス状態を持つスキル向け。
    /// GameManager.StartNewRun で各スキルの ResetRunState() が呼ばれる。</summary>
    public interface IRunResettable
    {
        void ResetRunState();
    }
}

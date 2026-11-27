namespace ItemSystem.PassiveSkills
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

    /// <summary>ラン中の状態を中断セーブへ書き出せるスキル (issue #84)。 <see cref="IRunResettable"/> を実装するスキルは
    /// <b>必ずこれも実装する</b> ── 実装しないと、 中断して再開した時にその状態だけ 0 に戻る
    /// (<see cref="PassiveSkillRegistry.CaptureAllRunState"/> が実装漏れを警告する)。</summary>
    public interface IRunStatePersistent
    {
        /// <summary>状態を文字列にする (JSON でなくてよい・同じ状態は同じ文字列)。</summary>
        string CaptureRunState();
        /// <summary><see cref="CaptureRunState"/> の文字列から戻す。 読めなければ false (状態は変えない)。</summary>
        bool RestoreRunState(string state);
    }
}

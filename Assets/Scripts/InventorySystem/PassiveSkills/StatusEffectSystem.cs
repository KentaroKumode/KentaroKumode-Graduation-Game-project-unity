using System.Collections.Generic;

namespace InventorySystem.PassiveSkills
{
    /// <summary>ステータスの対象（絶対視点。敵パッシブの視点スワップとは独立に Player=実プレイヤー / Enemy=実敵）。</summary>
    public enum StatusTarget { Player, Enemy }

    /// <summary>ステータスの処理タイミング（DOT適用＋減衰を行うタイミング）。</summary>
    public enum StatusTick { TurnStart, TurnEnd }

    /// <summary>
    /// 汎用ステータス効果の定義（#3 統一フレーム）。
    ///
    /// スコープ（2026-06-05・加算的導入）:
    /// - 既存の出血(enemyBleedStacks)/威圧(enemyThreat)/負傷(healShieldReduction)/敵ダイス減 等は
    ///   バランス済みのため現状フィールドのまま据え置く。
    /// - 本フレームは新ステータス用＋パイロットとして「炎上(burn)」のみを移行した。
    /// - スタック保持は <see cref="CombatContext.playerStatusStacks"/> / <see cref="CombatContext.enemyStatusStacks"/>。
    ///   DOT/減衰の一括処理は <see cref="CombatContext.TickStatuses"/>（ターン開始時に1回）。
    ///
    /// 既知の制限（パイロット段階）:
    /// - DOT値は def 単位（同 id の全発生源で共通）。発生源ごとに異なるダメ値は未対応。
    /// - DOT は軽減無視（fixedDamageToEnemy/Player 経由）。軽減ありの DOT が要るなら別途拡張。
    /// </summary>
    public class StatusDef
    {
        public string id;
        public StatusTarget target;
        public StatusTick tickTiming;
        public int dotPerStack;          // 1tickのダメージ。dotScalesWithStacks=false なら固定値
        public bool dotScalesWithStacks; // true: dmg=stacks×dotPerStack（出血型） / false: dmg=dotPerStack（持続型・炎上）
        public int decayPerTurn;         // tick後に減らすスタック量
        public int maxStacks;
    }

    /// <summary>ステータス定義のレジストリ。新ステータスはここに追加する。</summary>
    public static class StatusRegistry
    {
        public static readonly Dictionary<string, StatusDef> Defs = new Dictionary<string, StatusDef>
        {
            // 炎上（パイロット移行）: 敵へ毎ターン開始時 固定3ダメ・3ターン持続（stacks3／毎T-1／固定3）。
            // 旧 Ignite の enemyBurnTurns=3 / enemyBurnDamage=3 と等価。
            ["burn"] = new StatusDef
            {
                id = "burn",
                target = StatusTarget.Enemy,
                tickTiming = StatusTick.TurnStart,
                dotPerStack = 3,
                dotScalesWithStacks = false,
                decayPerTurn = 1,
                maxStacks = 99,
            },
        };
    }
}

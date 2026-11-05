using System.Collections.Generic;

namespace ItemSystem.PassiveSkills
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
        /// <summary>0 より大きいと、 1 スタックあたりのダメージを対象の最大 HP の N‰ (千分率) にする (dotPerStack の代わり)。
        /// dmg = floor(stacks × 最大HP × N / 1000)、 下限 1。 整数だけで計算する (Mono の float の揺れでダイジェストが割れないように
        /// ── 0.5% を小数で持たず 5‰ で持つ理由)。</summary>
        public int dotMaxHpPermillePerStack;
        public int decayPerTurn;         // tick後に減らすスタック量
        /// <summary>true: ダメージを出した直後に、 残っているスタックを半分にする (切り捨て)。 decayPerTurn の代わり。</summary>
        public bool halveAfterTick;
        public int maxStacks;
    }

    /// <summary>ステータス定義のレジストリ。新ステータスはここに追加する。</summary>
    public static class StatusRegistry
    {
        public static readonly Dictionary<string, StatusDef> Defs = new Dictionary<string, StatusDef>
        {
            // burn (炎上) は 2026-07-18 削除: Burn キーワード全廃 → 臨界 (Rinkai) 軸に置換。
            // 毒 (2026-07-15 追加): 拘束・妨害中心 (差別化)。麻痺毒で敵攻撃-N が主効果。
            // 上限10・ダメージを出すたびに半減・遅効型 = 長期戦のキル手段。 上限到達が毒殺 (キル判定) のトリガー。
            // 2026-11-05: 1 スタック 1% → 0.5%、 永続蓄積 → ダメージを出すたびに半減 (切り捨て)。
            //   毒は敵の最大 HP に比例するうえ、 減衰が無く、 毒を足す品が 5〜6 種同時に流れ込むので (ボスで 1 戦 9 スタック付与)、
            //   固定ダメの半分近くを毒が占め、 敵を硬くしても戦闘が長引かなかった。
            //   半減にしたので、 毎ターン足し続けないと貯まらない ── 足す品の数ではなく足す頻度が効く形。
            ["poison"] = new StatusDef
            {
                id = "poison",
                target = StatusTarget.Enemy,
                tickTiming = StatusTick.TurnStart,
                dotMaxHpPermillePerStack = 5,   // 0.5%
                dotScalesWithStacks = true,
                decayPerTurn = 0,
                halveAfterTick = true,
                maxStacks = 10,
            },
        };
    }
}

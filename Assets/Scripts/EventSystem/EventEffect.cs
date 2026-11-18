using System.Collections.Generic;

namespace EventSystem
{
    /// <summary>
    /// 1個の効果命令。Probability の場合のみ children/weights が埋まる。
    /// </summary>
    public class EventEffect
    {
        public EventEffectType type;
        public string param;        // バフ名、アイテム名、フラグ名等
        public int amount;          // 数値量（HPの増減など）
        public bool postCombat;     // 「勝利後...」プレフィックスがあった場合 true（戦闘勝利後に適用）
        /// <summary>代償を見せるかの印 (2026-11-16): +1 = 〔見せる〕・−1 = 〔伏せる〕・0 = 印なし (種類で決まる既定)。
        /// 読むのは <see cref="EventEffectRoles.IsShownCost"/> だけ (画面の説明と、 足りない時に選べなくするか)。 BOT の値付けは読まない。</summary>
        public int costMark;

        // Probability 用
        public List<float> branchWeights;   // 0.0~1.0 の重み（合計 1.0 想定）
        public List<List<EventEffect>> branches;

        public override string ToString()
        {
            if (type == EventEffectType.Probability)
                return $"確率分岐({branches?.Count ?? 0})";
            return $"{type}({param},{amount})";
        }
    }
}

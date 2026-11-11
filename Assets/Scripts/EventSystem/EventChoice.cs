using System.Collections.Generic;

namespace EventSystem
{
    /// <summary>
    /// プレイヤーが選ぶ選択肢。テキスト・効果リスト・選択後フレーバーを持つ。
    /// </summary>
    public class EventChoice
    {
        public string text;
        public List<EventEffect> effects = new List<EventEffect>();
        public string postFlavor;

        /// <summary>エンディング後のダイジェスト小説で使う**事後記録の文**。 未記入なら null。
        ///
        /// <para><b><see cref="postFlavor"/> で代用してはならない。</b> あちらは選択直後に読む
        /// その場のフィードバックで、独白調・現在の心情に寄った文が多い
        /// （「気にはなれなかった」「鼻を突いた」）。 回想の文脈へ置くと語りの位置がずれる。
        /// **未記入の選択肢はダイジェストに載せない** ── 沈黙の方が、違う声で喋るより害が小さい。</para>
        ///
        /// <para>event_list.txt では選択肢の末尾に <c>@@</c> で続ける。
        /// <c>-</c> 区切りは結果セクションの <c>HP-5</c> と衝突するので使えない。</para></summary>
        public string digest;

        /// <summary>この選択肢が使うターン (効果「ターン+N」の合計・確率分岐の中は数えない)。 0 なら時間を使わない。</summary>
        public int TurnCost
        {
            get
            {
                int n = 0;
                if (effects != null)
                    foreach (var e in effects)
                        if (e != null && !e.postCombat && e.type == EventEffectType.TurnsPass) n += e.amount;
                return n;
            }
        }

        /// <summary>この選択肢で払うゴールド (効果「ゴールド-N」の合計・確率分岐の中は数えない)。</summary>
        public int GoldCost
        {
            get
            {
                int n = 0;
                if (effects != null)
                    foreach (var e in effects)
                        if (e != null && !e.postCombat && e.type == EventEffectType.GoldDelta && e.amount < 0) n -= e.amount;
                return n;
            }
        }

        /// <summary>選べるか。 **払えないゴールドの支払いは選べない** (2026-11-11 まで、 所持金 0 でも
        /// 「20 ゴールドで品を買う」が通り、 所持金が 0 で止まるだけだった)。 物資は足りなくても選べる
        /// (足りない分は HP で払う ── 航行と同じ規則)。 run が null なら常に true。</summary>
        public bool IsAvailable(GameLoop.RunState run) => run == null || run.coins >= GoldCost;
    }
}

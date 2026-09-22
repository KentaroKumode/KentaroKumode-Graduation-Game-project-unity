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
    }
}

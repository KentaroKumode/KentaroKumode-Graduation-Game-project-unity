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

        /// <summary>確率分岐ごとの選んだ後の文 (2026-11-12)。 並びは結果の欄の「N%で」の順。
        /// event_list.txt で <c>||</c> 区切りに分けて書いた時だけ埋まる ── 分けていなければ null で、
        /// 全分岐で <see cref="postFlavor"/> を出す。 分けた時の <see cref="postFlavor"/> は先頭の分岐の文。
        /// 読む側は <see cref="PostFlavorFor"/> を通す。</summary>
        public List<string> postFlavorByBranch;

        /// <summary>確率分岐ごとの記録の文 (2026-11-12)。 <see cref="postFlavorByBranch"/> と同じ扱い。
        /// 空の分岐は「その分岐では記録に載せない」 (未記入と同じく沈黙する)。</summary>
        public List<string> digestByBranch;

        /// <summary>引いた分岐 <paramref name="branch"/> (<c>ExecutionResult.pickedBranch</c>・引いていなければ -1) の選んだ後の文。</summary>
        public string PostFlavorFor(int branch) => EventBranchText.For(postFlavorByBranch, branch, postFlavor);

        /// <summary>引いた分岐の記録の文。 未記入なら null。</summary>
        public string DigestFor(int branch)
        {
            string d = EventBranchText.For(digestByBranch, branch, digest);
            return string.IsNullOrEmpty(d) ? null : d;
        }

        /// <summary>記録の文を分岐ごとに分けて書いてあるか (分けてある時だけ、 引いた分岐を戦記に残す)。</summary>
        public bool DigestSplitByBranch => digestByBranch != null;

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

        /// <summary>この選択肢で払う強化素材 (効果「武器強化素材-N」の合計・確率分岐の中は数えない)。 2026-11-12。</summary>
        public int MaterialCost
        {
            get
            {
                int n = 0;
                if (effects != null)
                    foreach (var e in effects)
                        if (e != null && !e.postCombat && e.type == EventEffectType.MaterialDelta && e.amount < 0) n -= e.amount;
                return n;
            }
        }

        /// <summary>この選択肢が効果 type を (確率分岐の外に) 持つか。</summary>
        public bool Has(EventEffectType type)
        {
            if (effects != null)
                foreach (var e in effects)
                    if (e != null && !e.postCombat && e.type == type) return true;
            return false;
        }

        /// <summary>選べるか。 **払えないゴールドの支払いは選べない** (2026-11-11 まで、 所持金 0 でも
        /// 「20 ゴールドで品を買う」が通り、 所持金が 0 で止まるだけだった)。 物資は足りなくても選べる
        /// (足りない分は HP で払う ── 航行と同じ規則)。 run が null なら常に true。
        /// <para>2026-11-12: 払えない強化素材・差し出せるパッシブが無い取り替え・鍛えられない武器 (素材が足りない / 終点) も選べない。
        /// 理由の文は <see cref="UnavailableReason"/>。</para></summary>
        public bool IsAvailable(GameLoop.RunState run) => UnavailableReason(run) == null;

        /// <summary>選べない理由 (画面に出す短い文)。 選べるなら null。 判定の順は ゴールド → 素材 → 取り替え → 鍛える。</summary>
        public string UnavailableReason(GameLoop.RunState run)
        {
            if (run == null) return null;
            int g = GoldCost;
            if (run.coins < g) return $"ゴールドが足りない（{g}G）";
            int m = MaterialCost;
            if (Has(EventEffectType.ForgeWeapon))
            {
                int cost = GameLoop.GameManager.WeaponUpgradeCost(run);
                if (cost == int.MaxValue) return "武器はもう鍛えられない";
                m += cost;
            }
            if (run.weaponMaterials < m) return $"素材が足りない（{m}）";
            if (Has(EventEffectType.TradePassive) && !GameLoop.GameManager.CanTradePassive(run)) return "差し出せる秘宝が無い";
            return null;
        }
    }
}

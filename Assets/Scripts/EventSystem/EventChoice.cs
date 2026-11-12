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

        /// <summary>この選択肢で払う物資 (効果「物資-N」の合計・確率分岐の中と「勝利後」は数えない)。 2026-11-13。
        /// 足りなければ選べない (<see cref="UnavailableReason"/>)。 確率分岐の中の物資-N は支払いでなく結果として失う物なので、 ここには入れない。</summary>
        public int ProvisionCost
        {
            get
            {
                int n = 0;
                if (effects != null)
                    foreach (var e in effects)
                        if (e != null && !e.postCombat && (e.type == EventEffectType.ProvisionDelta || e.type == EventEffectType.HungerDelta) && e.amount < 0) n -= e.amount;
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
        /// 「20 ゴールドで品を買う」が通り、 所持金が 0 で止まるだけだった)。 run が null なら常に true。
        /// <para>2026-11-12: 払えない強化素材・差し出せる秘宝が無い取り替え・鍛えられない武器 (素材が足りない / 終点) も選べない。</para>
        /// <para>2026-11-13 (作者の決定「選択肢をブラックアウトさせて、 選択不可にする」): 状況で効果が起きない選択肢も選べない ──
        /// 払えない物資 (旧: 足りない分を HP で払っていた)・追跡者の居ない層の追跡者の効果・前線の無い層の前線の効果・航行マップの無い層の地図と近道。
        /// 起きない効果を「起きたように」書いた文を出さないため。 理由の文は <see cref="UnavailableReason"/>。</para></summary>
        public bool IsAvailable(GameLoop.RunState run) => UnavailableReason(run) == null;

        /// <summary>選べない理由 (画面に出す短い文)。 選べるなら null。
        /// 判定の順は ゴールド → 物資 → 素材 → 取り替え → 鍛える → 航行 (地図・近道 → 追跡者 → 前線)。</summary>
        public string UnavailableReason(GameLoop.RunState run)
        {
            if (run == null) return null;
            int g = GoldCost;
            if (run.coins < g) return $"ゴールドが足りない（{g}G）";
            int p = ProvisionCost;
            if (run.provision < p) return $"物資が足りない（{p}）";
            int m = MaterialCost;
            if (Has(EventEffectType.ForgeWeapon))
            {
                int cost = GameLoop.GameManager.WeaponUpgradeCost(run);
                if (cost == int.MaxValue) return "武器はもう鍛えられない";
                m += cost;
            }
            if (run.weaponMaterials < m) return $"素材が足りない（{m}）";
            if (Has(EventEffectType.TradePassive) && !GameLoop.GameManager.CanTradePassive(run)) return "差し出せる秘宝が無い";
            return NavUnavailableReason(run);
        }

        /// <summary>航行の効果が起きない理由 (2026-11-13)。 起きるなら null。 航行の効果は地図へ戻った時に適用するが
        /// (<c>GameManager.PassEventTurns</c>)、 イベントの間に追跡者・前線・地図の有無は変わらないので、 選ぶ時の状態で決める。
        /// 地図は本体の今の航行マップ (<c>GameManager.FreeMap</c>) を読む ── run が本体のランでなければ判定しない。</summary>
        private string NavUnavailableReason(GameLoop.RunState run)
        {
            bool map = Has(EventEffectType.RevealMap) || Has(EventEffectType.RevealEncounters) || Has(EventEffectType.RevealSupplies) || Has(EventEffectType.Shortcut);
            bool foe = Has(EventEffectType.ShakeOffPursuer) || Has(EventEffectType.PushPursuer) || Has(EventEffectType.PullPursuer);
            bool front = Has(EventEffectType.ShiftFront);
            if (!map && !foe && !front) return null;
            var gm = GameLoop.GameManager.Instance;
            if (gm == null || !ReferenceEquals(gm.Run, run)) return null;
            var sim = gm.FreeMap;
            if (map && sim == null) return "この層に地図は無い";
            if (foe && (sim == null || !sim.FoeAlive)) return "この層に追跡者は居ない";
            if (front)
            {
                if (sim == null || sim.Weather == MapSystem.FreeMove.WeatherKind.None) return "この層に前線は無い";
                // 遠ざける (amount < 0) は、 前線がまだ来ていなければ何も起きない (FreeMapSim.ShiftFront)
                if (!sim.FrontArrived && PushesFrontAway()) return "前線はまだ来ていない";
            }
            return null;
        }

        /// <summary>前線を遠ざける効果 (前線を遠ざける+N) を持つか。</summary>
        private bool PushesFrontAway()
        {
            if (effects != null)
                foreach (var e in effects)
                    if (e != null && !e.postCombat && e.type == EventEffectType.ShiftFront && e.amount < 0) return true;
            return false;
        }
    }
}

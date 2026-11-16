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

        /// <summary>選択肢の下に出す説明の文 (2026-11-16 に作り直し・作者の決定「自分から差し出す物は見せる・ハプニングは見せない」)。 何も無ければ null。
        /// 並びは 選べない理由 → 見せる代償 (効果の並びの順・<see cref="EventEffectRoles.IsShownCost"/>・文は <see cref="EventEffectRoles.ShownCostText"/>)。
        /// ターンの後ろには捕捉中の捕まるまでを付ける (etaTurns = 捕捉されている時の捕まるまでのターン・-1 = 捕捉されていない)。
        /// 選べない時 (why あり) は、 理由とターンだけを出す ── 足りない支払いは理由の文が言うので、 ほかの支払いを並べて 2 行を越えないようにする。
        /// 組み立ては <c>EventEncounter.DescribeCost</c> から呼ぶ (検算 Tools/event_page_check が最も長い形で窓の 2 行に収まるかを見る)。 得る物は書かない。</summary>
        public string CostText(string why, int etaTurns)
        {
            var sb = new System.Text.StringBuilder();
            if (why != null) sb.Append(why);
            bool etaDone = false;
            if (effects != null)
                foreach (var e in effects)
                {
                    if (!EventEffectRoles.IsShownCost(e)) continue;
                    if (why != null && e.type != EventEffectType.TurnsPass) continue;
                    string t = EventEffectRoles.ShownCostText(e);
                    if (t == null) continue;
                    if (sb.Length > 0) sb.Append("・");
                    sb.Append(t);
                    if (e.type == EventEffectType.TurnsPass && etaTurns >= 0 && !etaDone)
                    {
                        etaDone = true;
                        int turns = TurnCost;
                        sb.Append(etaTurns <= turns ? $"（捕捉されている: {etaTurns} ターンで捕まる）"
                                                    : $"（捕捉されている: 最短 {etaTurns} ターンで捕まる）");
                    }
                }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>選択肢の箱の右端の札 1 枚 (2026-11-16・イベントの画面)。 turns = ターンの札 (画面が砂時計の絵を前に付け、 text は数だけ)。</summary>
        public struct CostChip { public bool turns; public string text; }

        /// <summary>選択肢の箱の右端に並べる「見せる代償」の札 (2026-11-16・効果の並びの順)。 どれを見せるかは <see cref="EventEffectRoles.IsShownCost"/>・
        /// 札の文は <see cref="EventEffectRoles.ShownCostChip"/>。 得る物は出さない。 選べない時も全部並べる (画面が赤くする・理由は箱の下の説明が言う)。</summary>
        public List<CostChip> CostChips()
        {
            var list = new List<CostChip>();
            if (effects != null)
                foreach (var e in effects)
                {
                    if (!EventEffectRoles.IsShownCost(e)) continue;
                    string t = EventEffectRoles.ShownCostChip(e);
                    if (t != null) list.Add(new CostChip { turns = e.type == EventEffectType.TurnsPass, text = t });
                }
            return list;
        }

        /// <summary>選んでいる箱の下に出す説明 (2026-11-16・イベントの画面・行は改行で分ける)。 何も無ければ null。
        /// why = 選べない理由 (選べる箱は null を渡す)・etaTurns = 捕捉されている時の捕まるまでのターン (-1 = 捕捉されていない)。
        /// 捕捉中の見込みはターンを使う選択肢だけに付ける。 札が言う代償 (ターン・ゴールド …) はここに繰り返さない。
        /// 箱の下の 2 行に収まるかは検算 Tools/event_page_check が見る。</summary>
        public string NoteText(string why, int etaTurns)
        {
            string eta = null;
            int turns = TurnCost;
            if (turns > 0 && etaTurns >= 0)
                eta = etaTurns <= turns ? $"捕捉されている。{etaTurns} ターンで捕まる" : $"捕捉中。最短 {etaTurns} ターンで捕まる";      // 「捕捉されている。最短 99 ターン…」は箱の下の 1 行 (156 dot) に入らない
            if (why == null) return eta;
            return eta == null ? why : why + "\n" + eta;
        }

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
        /// 確率分岐の中の物資-N は支払いでなく結果として失う物なので、 ここには入れない。 選べるかの判定は見せる支払いだけを数える (<see cref="BlockingPayment"/>・2026-11-16)。</summary>
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

        /// <summary>足りなければ選べなくする支払いの合計 (2026-11-16)。 確率分岐の外・「勝利後」でない -N のうち、 <see cref="EventEffectRoles.BlocksWhenShort"/> の物だけ
        /// (見せる支払い。 伏せた支払いは足りなくても選べて、 ある分だけ失う)。 <paramref name="provision"/> = 物資 (旧表記の空腹度も) を数える。</summary>
        private int BlockingPayment(EventEffectType type, bool provision = false)
        {
            int n = 0;
            if (effects != null)
                foreach (var e in effects)
                    if (e != null && !e.postCombat && e.amount < 0
                        && (e.type == type || (provision && e.type == EventEffectType.HungerDelta))
                        && EventEffectRoles.BlocksWhenShort(e)) n -= e.amount;
            return n;
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
        /// <para>2026-11-13 (作者の決定): 暗くするのは (a) 払えない時 (ゴールド・物資・素材) と、 (b) <b>得になる効果が何ひとつ起きない時</b>だけ
        /// (<see cref="EventEffectRoles.NothingGainedReason"/>)。 起きない効果が代償 (追跡者を呼ぶ・前線を近づける) だけなら選べる
        /// (作者「いないならいないで OK」)。 得の一部だけが起きない時も選べる。 起きなかった効果は結果の行に出す (<c>EventEffectExecutor</c>)。
        /// HP が満ちている時の HP+N は選べる。 理由の文は <see cref="UnavailableReason"/>。</para></summary>
        public bool IsAvailable(GameLoop.RunState run) => UnavailableReason(run) == null;

        /// <summary>選べない理由 (画面に出す短い文)。 選べるなら null。
        /// 判定の順は (a) ゴールド → 物資 → 素材 (鍛える素材を含む)、 (b) 得になる効果が何ひとつ起きない (最初に書かれた得の効果の理由)。
        /// (a) に数えるのは見せる支払いだけ (2026-11-16・<see cref="EventEffectRoles.BlocksWhenShort"/>・切り替えは <see cref="EventEffectRoles.HiddenPaymentMayFallShort"/>)。</summary>
        public string UnavailableReason(GameLoop.RunState run)
        {
            if (run == null) return null;
            // 支払いは見せる物だけ (2026-11-16・EventEffectRoles.BlocksWhenShort)。 伏せた支払いは足りなくても選べて、 ある分だけ失う
            int g = BlockingPayment(EventEffectType.GoldDelta);
            if (run.coins < g) return $"ゴールドが足りない（{g}G）";
            int p = BlockingPayment(EventEffectType.ProvisionDelta, provision: true);
            if (run.provision < p) return $"物資が足りない（{p}）";
            int m = BlockingPayment(EventEffectType.MaterialDelta);
            if (Has(EventEffectType.ForgeWeapon))
            {
                int cost = GameLoop.GameManager.WeaponUpgradeCost(run);
                if (cost != int.MaxValue) m += cost;   // 終点 (鍛えられない) は (b) の側で見る
            }
            if (run.weaponMaterials < m) return $"素材が足りない（{m}）";
            return EventEffectRoles.NothingGainedReason(effects, Situation(run, Has(EventEffectType.TradePassive), Has(EventEffectType.ForgeWeapon)));
        }

        /// <summary>効果が起きるかを決める今の状況 (2026-11-13)。 航行の効果は地図へ戻った時に適用するが (<c>GameManager.PassEventTurns</c>)、
        /// イベントの間に追跡者・前線・地図の有無は変わらないので、 選ぶ時の状態で決める。
        /// 航行は本体の今の航行マップ (<c>GameManager.FreeMap</c>) を読む ── run が本体のランでなければ航行は判定しない (起きるとみなす)。
        /// 結果の行 (<c>EventEffectExecutor</c>) も同じ状況で「起きなかった」を書く。 <paramref name="trade"/> / <paramref name="forge"/> = 取り替え・鍛えるを読むか (持たない選択肢では手間を省く)。</summary>
        public static EventEffectSituation Situation(GameLoop.RunState run, bool trade = true, bool forge = true)
        {
            var s = new EventEffectSituation
            {
                canTrade = trade && run != null && GameLoop.GameManager.CanTradePassive(run),
                canForge = forge && run != null && GameLoop.GameManager.WeaponUpgradeCost(run) != int.MaxValue,
            };
            var gm = GameLoop.GameManager.Instance;
            if (run == null || gm == null || !ReferenceEquals(gm.Run, run)) return s;
            var sim = gm.FreeMap;
            s.navKnown = true;
            s.map = sim != null;
            s.foe = sim != null && sim.FoeAlive;
            s.weather = sim != null && sim.Weather != MapSystem.FreeMove.WeatherKind.None;
            s.frontArrived = sim != null && sim.FrontArrived;
            s.stormRules = GameLoop.GameHooks.StormEnabled;
            s.storm = sim != null && sim.StormMode;
            return s;
        }
    }
}

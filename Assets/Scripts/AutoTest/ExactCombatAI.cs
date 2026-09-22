using System;
using System.Collections.Generic;
using CombatSystem;
using GameLoop;
using InventorySystem.PassiveSkills;

namespace AutoTest
{
    /// <summary>
    /// **厳密戦闘AI（技量帯の天井を測るための参照実装）。**
    /// 設計と既知の限界は [docs/design-exact-ai.md](../../../docs/design-exact-ai.md) が正本。
    ///
    /// <para><b>目的は勝つことではなく「天井の位置を知ること」。</b> Super は同一シードのペア比較で
    /// Optimal に +2.9〜4.6pt 勝つが、 それが理論上の上限に近いのか遠いのか分からなかった。</para>
    ///
    /// <para><b>禁止事項（絶対）。</b> ① 未来予知の禁止 ② 乱数を読むことの禁止。
    /// 偶然ノードを**確率つきで列挙して平均する**ことで守る ── 覗くのではなく平均する。
    /// <see cref="GameRng"/> も専用乱数も参照しない。 結果として**同じ盤面には同じ手を返す**
    /// （分散ゼロ・同一シードでビット一致するはず。 しなければ実装に非決定性がある）。</para>
    ///
    /// <para><b>構造。</b>
    /// <list type="bullet">
    ///   <item>価値関数を <c>V(状態)</c> と <c>Q(状態, 手札)</c> に分け、 <c>V</c> を**状態だけ**で
    ///         メモ化する。 木が DAG に畳まれ、 指数から多項式になる</item>
    ///   <item>決定は「割り当て 3^k」ではなく**相異なるクリップ済み合計組の Pareto フロンティア**を
    ///         列挙する。 <c>ApplyTurn</c> が端子ごとの合計しか見ないので、 これで現行モデル内では厳密</item>
    ///   <item>ゴーストは**今ターン内の閉じた式**で置く。 端子役の判定に参加しないので
    ///         主配線と組み合わせ爆発を起こさない</item>
    ///   <item>止めるのは深さではなく <see cref="NodeBudget"/>（展開した状態数）</item>
    /// </list></para>
    ///
    /// <para><b>まだモデル化していないもの（design-exact-ai.md §8）。</b>
    /// 敵/自分のパッシブ・役の効果・会心・出血・臨界・特殊端子・ラン単位の価値。
    /// **モデルが現実と違うので、厳密に解いても弱くなりうる。**
    /// 「Exact が Super に負けた」はバグの証拠にならない。</para>
    /// </summary>
    public class ExactCombatAI
    {
        // ============================================================
        //  刈り幅と較正定数（**すべて未実測の仮値**）
        // ============================================================

        /// <summary>1 決定あたりに展開してよい状態ノード数。 予算切れの枝だけ静的評価へ落ちる。
        /// 上げるほど理論値へ近づくが速度は落ちる。</summary>
        public int NodeBudget = 60;

        /// <summary>リロールの**部分集合 1 つあたり**の予算。
        ///
        /// <para><b>集合ごとにリセットしないと不公平になる。</b> リロールは最大 2^k−1 = 31 個の
        /// 部分集合を評価するが、 予算を共有すると先に列挙された集合だけが深く読まれ、
        /// 残りは静的評価で決まる ── **列挙順が選択を決めてしまう**。
        /// 集合ごとに小さい予算を与えて揃える。</para></summary>
        public int RerollNodeBudget = 12;
        /// <summary>手札の偶然ノードで展開する多重集合の数（確率上位）。</summary>
        public int HandBranches = 6;
        /// <summary>敵ダイス合計の偶然ノードで展開する値の数（確率上位）。</summary>
        public int EnemyBranches = 2;
        /// <summary>手札分布を組むときに使う面クラス数（重みの大きい順）。</summary>
        public int TopClasses = 5;
        /// <summary>ゴースト配置で充電を割り引く重み（今ターンには効かないため）。</summary>
        public float ChargeWeight = 0.5f;
        /// <summary>多段ボスの 1 段撃破に与える基礎値。 残り HP 比で 1.0 まで伸びる。
        /// **戦闘は終わっていない**ので 1.0 を返してはいけない（design §7-2）。</summary>
        public float PhaseWinBase = 0.5f;
        /// <summary>膠着と判定するターン数。 超えたら負け扱い。</summary>
        public int StalemateTurn = 60;

        // ============================================================
        //  計装（design §9-1: 予算が効いているかを観測する）
        // ============================================================

        public static long StatDecisions, StatNodes, StatBudgetHits, StatTuplesEvaluated;
        public static void ResetStats() { StatDecisions = StatNodes = StatBudgetHits = StatTuplesEvaluated = 0; }
        public static string Diag()
            => $"決定 {StatDecisions} / 展開ノード {StatNodes} / 予算切れ {StatBudgetHits}"
             + $" ({(StatDecisions > 0 ? StatBudgetHits * 100.0 / StatDecisions : 0):F1}%)"
             + $" / 評価した合計組 {StatTuplesEvaluated}";

        // ============================================================
        //  盤面スナップショット
        // ============================================================

        private int _k;
        private int _pMaxHp, _pAtkPower;
        private int[] _faces;
        private DiceFaceParts.Tier[] _tiers;
        private int _eBaseAtk, _eDiceCount, _eRollMax, _eMaxHp;
        /// <summary>敵の被ダメ軽減 (0〜1)。 **抜けていると自分の火力を 1/(1−r) 倍に過大評価する。**
        /// 7層ボスは全段 0.35 なので 1.54 倍。 <see cref="EnemyData.baseDefenseRate"/> を
        /// そのまま読むので値の二重管理は無い。</summary>
        private float _eDefRate;

        /// <summary>2 体戦 (2026-08-28) の 2 体目の攻撃値。 0 = 単体戦 or 撃破済み。</summary>
        private int _secondAtk;
        private string _escProfile;
        private int _chargeMax;
        /// <summary>この敵が多段連戦の一部か。 1 段撃破を「勝ち」と誤認しないための判定。</summary>
        private bool _chained;

        /// <summary><b>端子は常に 3 種として扱う（design §4-2b）。</b>
        /// 特殊端子は 11 種あり効果もスケールも異なるので、 スカラの飽和関数では表せない。
        /// 初版は <c>_termKinds = 4</c> にしつつ端子 3 を <c>default</c> で充電と同一視しており、
        /// **1024 通りを列挙してそのうち特殊端子の枝を全部誤って評価していた**。
        /// 誤った評価より、 枝を作らない方が正直。 特殊端子の装備上限も検証していないので、
        /// 提案しなければ違法配線も起きない。</summary>
        private const int TermKinds = 3;

        private struct FaceClass { public int value; public int weight; }
        private readonly List<FaceClass> _classes = new List<FaceClass>();

        /// <summary>面構成の署名。 これが変わらない限り手札分岐を作り直さない（design §7-6）。</summary>
        private long _facesSig = long.MinValue;
        /// <summary>スナップショットの世代。 敵の形態が入れ替わったら増える。
        /// **メモ化のキーに混ぜる** ── 混ぜないと前の形態の価値が残る（design §7-3）。</summary>
        private long _snapSig;

        /// <summary>m 個ぶんの手札分岐のキャッシュ。 m=k は通常のロール、 m&lt;k はリロール用。
        /// <b>リロールは k 個の多重集合の先頭 m 要素ではない</b>（design §7-1）ので、
        /// m ごとに正しい周辺分布を作る。</summary>
        private readonly Dictionary<int, List<(int[] vals, double w)>> _branchCache
            = new Dictionary<int, List<(int[], double)>>();
        private readonly List<(int total, double w)> _enemyDist = new List<(int, double)>();

        private struct S { public int pHp, eHp, turn, charge; }

        public void BeginCombat(string enemyId)
        {
            _vMemo.Clear();
            _branchCache.Clear();
            _candCache.Clear();
            _facesSig = long.MinValue;
        }

        private bool Snapshot(CombatManager cm, int k)
        {
            var ctx = PassiveSkillManager.Instance?.Context;
            var enemy = cm?.CurrentEnemy;
            if (ctx == null || enemy == null || k <= 0) return false;

            _k = k;
            _pMaxHp = Math.Max(1, cm.PlayerMaxHP);
            _pAtkPower = Math.Max(0, cm.PlayerAttackPower);
            _faces = (ctx.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0)
                   ? ctx.equippedDiceFaces : null;
            _tiers = ctx.equippedFaceTiers;
            if (_faces == null || _faces.Length == 0) return false;

            _eBaseAtk = enemy.EffectiveBaseAttack;
            _eDiceCount = Math.Max(0, enemy.EffectiveRollCount);
            _eRollMax = Math.Max(1, enemy.EffectiveRollMax);
            _eMaxHp = Math.Max(1, enemy.maxHP);
            _eDefRate = Math.Min(0.95f, Math.Max(0f, enemy.baseDefenseRate));
            _escProfile = enemy.id;
            _chargeMax = CombatManager.ChargeMax;
            // 多段連戦（ヴェスカ 4 段）。 prefix 判定は Ids.cs の定数を経由する。
            _chained = !string.IsNullOrEmpty(enemy.id)
                    && enemy.id.StartsWith(BossIds.Layer7Prefix, StringComparison.Ordinal);

            // 形態が入れ替わったらメモを捨てる（キーに混ぜるだけでは古い値が残り続ける）
            long sig = Sig(enemy.id) * 31 + _eMaxHp * 7 + _eBaseAtk;
            if (sig != _snapSig) { _snapSig = sig; _vMemo.Clear(); _candCache.Clear(); }

            long fsig = FacesSignature();
            if (fsig != _facesSig)
            {
                _facesSig = fsig;
                BuildFaceClasses();
                _branchCache.Clear();
                _candCache.Clear();
            }
            BuildEnemyDist();
            return true;
        }

        private long FacesSignature()
        {
            long h = 1469598103934665603L;
            for (int i = 0; i < _faces.Length; i++) { h ^= _faces[i]; h *= 1099511628211L; }
            h ^= _faces.Length; h *= 1099511628211L;
            h ^= _k;
            return h;
        }

        private static long Sig(string s)
        {
            long h = 1469598103934665603L;
            if (s != null) for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 1099511628211L; }
            return h;
        }

        /// <summary>面を値でまとめる。 <b>Tier は束ねない</b> ── T1/T3/T4 の効果は
        /// 「今ターンどの面に止まっているか」で決まり、 それは <c>playerDiceFaceIdx</c> から読める。
        /// 先のターンの手札は値だけで足りる。</summary>
        private void BuildFaceClasses()
        {
            _classes.Clear();
            for (int i = 0; i < _faces.Length; i++)
            {
                int at = -1;
                for (int c = 0; c < _classes.Count; c++)
                    if (_classes[c].value == _faces[i]) { at = c; break; }
                if (at >= 0) { var fc = _classes[at]; fc.weight++; _classes[at] = fc; }
                else _classes.Add(new FaceClass { value = _faces[i], weight = 1 });
            }
            _classes.Sort((a, b) => b.weight.CompareTo(a.weight));
        }

        /// <summary>m 個のダイスを振ったときの分布。 **多重集合ごとに多項係数を掛ける**
        /// ── これを掛けないと <c>{1,2,3,4,5}</c>（120 通りの並び）と <c>{1,1,1,1,1}</c>（1 通り）が
        /// 同じ重みになり、 確率が 2 桁ずれる。 初版はここが抜けていた。</summary>
        private List<(int[] vals, double w)> Branches(int m)
        {
            if (_branchCache.TryGetValue(m, out var cached)) return cached;

            int C = Math.Min(Math.Max(1, TopClasses), _classes.Count);
            var acc = new List<(int[], double)>();
            var cur = new int[m];
            var cnt = new int[C];

            void Rec(int pos, int start)
            {
                if (acc.Count > 8192) return;                 // 安全弁
                if (pos == m)
                {
                    double w = Multinomial(cnt, m);
                    for (int c = 0; c < C; c++)
                        if (cnt[c] > 0) w *= Math.Pow(_classes[c].weight, cnt[c]);
                    acc.Add(((int[])cur.Clone(), w));
                    return;
                }
                for (int c = start; c < C; c++)
                {
                    cur[pos] = _classes[c].value; cnt[c]++;
                    Rec(pos + 1, c);                          // 非減少列 = 多重集合
                    cnt[c]--;
                }
            }
            Rec(0, 0);
            acc.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            int take = HandBranches > 0 ? Math.Min(HandBranches, acc.Count) : acc.Count;
            var list = new List<(int[], double)>(take);
            for (int i = 0; i < take; i++) list.Add(acc[i]);
            _branchCache[m] = list;
            return list;
        }

        private static double Multinomial(int[] counts, int m)
        {
            double r = Fact(m);
            for (int i = 0; i < counts.Length; i++) if (counts[i] > 1) r /= Fact(counts[i]);
            return r;
        }
        private static double Fact(int n) { double r = 1; for (int i = 2; i <= n; i++) r *= i; return r; }

        private void BuildEnemyDist()
        {
            _enemyDist.Clear();
            var dist = new Dictionary<int, double> { { 0, 1.0 } };
            for (int d = 0; d < _eDiceCount && d < 8; d++)
            {
                var next = new Dictionary<int, double>();
                foreach (var kv in dist)
                    for (int f = 1; f <= _eRollMax; f++)
                    {
                        int t = kv.Key + f;
                        next.TryGetValue(t, out double p);
                        next[t] = p + kv.Value;
                    }
                dist = next;
            }
            var list = new List<(int, double)>();
            foreach (var kv in dist) list.Add((kv.Key, kv.Value));
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            int take = EnemyBranches > 0 ? Math.Min(EnemyBranches, list.Count) : list.Count;
            for (int i = 0; i < take; i++) _enemyDist.Add(list[i]);
        }

        // ============================================================
        //  公開 API
        // ============================================================

        /// <summary>今ターンの配線（主 + ゴースト）を選ぶ。</summary>
        public WiringPlan ChooseWiringPlan(int[] rolls, MutualTurnTelegraph tele)
        {
            var cm = CombatManager.Instance;
            if (!Snapshot(cm, rolls?.Length ?? 0)) return WiringPlan.Of(FallbackMain(rolls));

            var ctx = PassiveSkillManager.Instance?.Context;
            var fidx = ctx?.playerDiceFaceIdx;
            var s0 = new S
            {
                pHp = cm.PlayerHP, eHp = cm.EnemyHP, turn = tele.turn,
                charge = ctx != null ? ctx.GetCharge() : 0,
            };
            int enemyAtk = Math.Max(0, tele.enemyAttackValue);
            // 2 体戦 (2026-08-28): 2 パケット目。 Optimal も同じ情報を見なければ、
            //   2 体戦でだけ技量帯の比較が交絡する。
            _secondAtk = Math.Max(0, tele.secondaryAttackValue);
            int atkBase = Math.Max(0, _pAtkPower - Math.Max(0, tele.playerAttackPenalty));

            CollectDual(fidx);
            _nodesUsed = 0; _budget = NodeBudget;
            StatDecisions++;

            var cands = Candidates(s0, rolls, enemyAtk, atkBase);
            var bestMain = FallbackMain(rolls);
            int[] bestGhost = NoGhostArray();
            float bestV = float.NegativeInfinity;
            var ghost = new int[_k];

            foreach (var c in cands)
            {
                if (_nodesUsed >= _budget) { StatBudgetHits++; break; }
                PlaceGhosts(c.main, ghost, rolls, s0, enemyAtk, atkBase);
                float v = TurnThenFuture(s0, rolls, enemyAtk,
                                         new WiringPlan { main = c.main, ghost = ghost }, atkBase);
                if (v > bestV)
                {
                    bestV = v;
                    bestMain = (DiceTerminal[])c.main.Clone();
                    bestGhost = (int[])ghost.Clone();
                }
            }
            return new WiringPlan { main = bestMain, ghost = bestGhost };
        }

        /// <summary>リロールする添字集合を選ぶ。 部分集合を決定ノードとして開く。
        /// T1（振り直せない）面のダイスは候補から除く。</summary>
        public int[] ChooseReroll(int[] dice, MutualTurnTelegraph tele, int attempt)
        {
            var cm = CombatManager.Instance;
            if (!Snapshot(cm, dice?.Length ?? 0)) return Array.Empty<int>();

            var ctx = PassiveSkillManager.Instance?.Context;
            var fidx = ctx?.playerDiceFaceIdx;
            int charge = ctx != null ? ctx.GetCharge() : 0;
            CollectDual(fidx);

            var free = new List<int>();
            for (int i = 0; i < _k; i++)
            {
                if (_tiers != null && fidx != null && i < fidx.Length
                    && DiceFaceParts.IsLocked(_tiers, fidx[i])) continue;
                free.Add(i);
            }
            if (free.Count == 0) return Array.Empty<int>();

            var s0 = new S
            {
                pHp = cm.PlayerHP, eHp = cm.EnemyHP, turn = tele.turn, charge = charge,
            };
            int enemyAtk = Math.Max(0, tele.enemyAttackValue);
            // 2 体戦 (2026-08-28): 2 パケット目。 Optimal も同じ情報を見なければ、
            //   2 体戦でだけ技量帯の比較が交絡する。
            _secondAtk = Math.Max(0, tele.secondaryAttackValue);
            int atkBase = Math.Max(0, _pAtkPower - Math.Max(0, tele.playerAttackPenalty));

            StatDecisions++;
            // 「振らない」基準。 部分集合と同じ予算で評価して条件を揃える。
            _nodesUsed = 0; _budget = RerollNodeBudget;
            float bestV = Q(s0, dice, enemyAtk, atkBase);
            int[] best = Array.Empty<int>();

            int subsets = 1 << free.Count;
            var buf = new int[_k];
            for (int mask = 1; mask < subsets; mask++)
            {
                var pick = new List<int>();
                for (int b = 0; b < free.Count; b++) if ((mask & (1 << b)) != 0) pick.Add(free[b]);
                int cost = pick.Count * Math.Max(1, attempt);
                if (cost > charge) continue;

                var s = s0; s.charge -= cost;
                // **部分集合ごとに予算をリセット。** 共有すると列挙順が選択を決めてしまう。
                _nodesUsed = 0; _budget = RerollNodeBudget;
                // **m 個ぶんの正しい周辺分布**を使う（design §7-1 の修正点）
                var br = Branches(pick.Count);
                double acc = 0, wsum = 0;
                foreach (var (vals, w) in br)
                {
                    Array.Copy(dice, buf, _k);
                    for (int j = 0; j < pick.Count; j++) buf[pick[j]] = vals[j];
                    acc += Q(s, buf, enemyAtk, atkBase) * w;
                    wsum += w;
                }
                float v = wsum > 0 ? (float)(acc / wsum) : 0f;
                if (v > bestV) { bestV = v; best = pick.ToArray(); }
            }
            return best;
        }

        // ============================================================
        //  決定空間: 相異なるクリップ済み合計組の Pareto フロンティア
        // ============================================================

        private struct Cand { public DiceTerminal[] main; public int a, b, c; }
        private readonly List<Cand> _candBuf = new List<Cand>();

        /// <summary>候補配線のキャッシュ。 **候補は (手札, 飽和上限) だけで決まる**ので、
        /// 同じ組に対して作り直す必要がない。
        ///
        /// <para>これが無いと <c>V</c> の 1 ノードあたり (手札 × 敵) 回だけ 3^k の列挙と
        /// Pareto 枝刈り O(n²) が走り、 予算ぶん掛け算されて破綻する。
        /// 手札は <see cref="Branches"/> の固定集合（既定 8 個）から来るので、
        /// 実際の相異なるキーは少なく、 ヒット率は高い。</para></summary>
        private readonly Dictionary<long, List<Cand>> _candCache = new Dictionary<long, List<Cand>>();
        private const int CandCacheCap = 40000;


        /// <summary>候補配線を列挙する（design §5-1）。
        ///
        /// <para><c>ApplyTurn</c> は端子ごとの**合計**しか見ない（役を実装していないため）。
        /// したがって 3^k 個の「割り当て」は <c>(aSum, bSum, cSum)</c> の「合計組」へ潰せる。
        /// さらに各成分は単調かつ飽和するので、 飽和上限でクリップしてから
        /// Pareto 劣位（クリップ後の全成分が他以下）を落とせる。
        /// <b>現行モデル内では厳密</b>だが**閉じた式ではない** ── 防御 vs 充電の配分は
        /// 価値関数に依存する本物の選択なので、 フロンティアは 1 点に潰れない。</para>
        ///
        /// <para><b>役を実装したら割り当てに戻す必要がある</b>（合計が同じでも役が違う）。</para></summary>
        private List<Cand> Candidates(S s, int[] dice, int enemyAtk, int atkBase)
        {
            float defMul = 1f - _eDefRate;
            // 攻撃の飽和上限: これ以上足しても敵は既に死んでいる
            int aCap = Math.Max(0, (int)Math.Ceiling(s.eHp / Math.Max(0.05f, defMul)) - atkBase);
            int bCap = enemyAtk;
            int cCap = Math.Max(0, _chargeMax - s.charge);

            long ck = 1469598103934665603L;
            for (int i = 0; i < _k; i++) { ck ^= dice[i]; ck *= 1099511628211L; }
            ck ^= aCap; ck *= 1099511628211L;
            ck ^= bCap; ck *= 1099511628211L;
            ck ^= cCap;
            if (_candCache.TryGetValue(ck, out var hit)) return hit;

            _candBuf.Clear();
            int total = Pow(TermKinds, _k);
            var seen = new HashSet<int>();          // クリップ済みキーの重複排除
            var main = new DiceTerminal[_k];
            for (int code = 0; code < total; code++)
            {
                int t = code, a = 0, b = 0, c = 0;
                for (int i = 0; i < _k; i++)
                {
                    int term = t % TermKinds; t /= TermKinds;
                    main[i] = (DiceTerminal)term;
                    if (term == (int)DiceTerminal.Attack) a += dice[i];
                    else if (term == (int)DiceTerminal.Block) b += dice[i];
                    else c += dice[i];
                }
                int ca = Math.Min(a, aCap), cb = Math.Min(b, bCap), cc = Math.Min(c, cCap);
                int key = (ca * 1000 + cb) * 1000 + cc;
                if (!seen.Add(key)) continue;
                _candBuf.Add(new Cand { main = (DiceTerminal[])main.Clone(), a = ca, b = cb, c = cc });
            }

            // Pareto 枝刈り: 全成分で他以下なら落とす（クリップ後は 3 成分すべて「多い方が良い」）
            var keep = new List<Cand>(_candBuf.Count);
            for (int i = 0; i < _candBuf.Count; i++)
            {
                bool dominated = false;
                for (int j = 0; j < _candBuf.Count && !dominated; j++)
                {
                    if (i == j) continue;
                    var x = _candBuf[i]; var y = _candBuf[j];
                    if (y.a >= x.a && y.b >= x.b && y.c >= x.c
                        && (y.a > x.a || y.b > x.b || y.c > x.c)) dominated = true;
                }
                if (!dominated) keep.Add(_candBuf[i]);
            }
            StatTuplesEvaluated += keep.Count;
            if (_candCache.Count < CandCacheCap) _candCache[ck] = keep;
            return keep;
        }

        // ============================================================
        //  ゴースト: 今ターン内の閉じた式（design R9/R10）
        // ============================================================

        private readonly List<int> _dual = new List<int>();
        private readonly HashSet<int> _stackable = new HashSet<int>();

        private void CollectDual(int[] fidx)
        {
            _dual.Clear(); _stackable.Clear();
            if (_tiers == null || fidx == null) return;
            for (int i = 0; i < _k && i < fidx.Length; i++)
            {
                if (DiceFaceParts.AllowsDualLink(_tiers, fidx[i])) _dual.Add(i);
                if (DiceFaceParts.AllowsSameTerminalStack(_tiers, fidx[i])) _stackable.Add(i);
            }
        }

        /// <summary>ゴーストを**今ターン内の閉じた式**で置く。 探索しない。
        ///
        /// <para>ゴーストは端子の合計値へスカラを足すだけで**端子役の判定に参加しない**
        /// （<see cref="DiceFaceParts"/> の doc: 「参加するのは実体だけ」）。 役の成立構造は
        /// 主配線だけで決まるので、 主配線と組み合わせ爆発を起こさない。
        /// さらに完全情報テレグラフで今ターンの敵攻撃値が既知なので、 +X の利得は
        /// 飽和関数として確定する。 出目の大きい順に確定させ、 都度合計を更新する。</para></summary>
        private void PlaceGhosts(DiceTerminal[] main, int[] ghost, int[] dice,
                                 S s, int enemyAtk, int atkBase)
        {
            for (int i = 0; i < _k; i++) ghost[i] = WiringPlan.NoGhost;
            if (_dual.Count == 0) return;

            int aSum = 0, bSum = 0, cSum = 0;
            for (int i = 0; i < _k; i++)
                switch (main[i])
                {
                    case DiceTerminal.Attack: aSum += dice[i]; break;
                    case DiceTerminal.Block:  bSum += dice[i]; break;
                    default:                  cSum += dice[i]; break;
                }

            var order = new List<int>(_dual);
            order.Sort((x, y) => dice[y].CompareTo(dice[x]));
            float defMul = 1f - _eDefRate;

            foreach (int die in order)
            {
                int bestT = WiringPlan.NoGhost; float bestGain = 0f;
                for (int t = 0; t < TermKinds; t++)
                {
                    bool same = (int)main[die] == t;
                    if (same && !_stackable.Contains(die)) continue;   // T3 は重ねられない
                    int add = dice[die] + (same ? DiceFaceParts.T4StackBonus : 0);
                    float gain;
                    switch ((DiceTerminal)t)
                    {
                        case DiceTerminal.Attack:
                            gain = Math.Min(add * defMul,
                                   Math.Max(0f, s.eHp - (atkBase + aSum) * defMul));
                            break;
                        case DiceTerminal.Block:
                            gain = Math.Min(add, Math.Max(0, enemyAtk - bSum))
                                 + (_secondAtk > 0 ? Math.Min(add, Math.Max(0, _secondAtk - bSum)) : 0);
                            break;
                        default:
                            gain = Math.Min(add, Math.Max(0, _chargeMax - (s.charge + cSum))) * ChargeWeight;
                            break;
                    }
                    if (gain > bestGain) { bestGain = gain; bestT = t; }
                }
                if (bestT == WiringPlan.NoGhost) continue;
                ghost[die] = bestT;
                int applied = dice[die] + ((int)main[die] == bestT ? DiceFaceParts.T4StackBonus : 0);
                switch ((DiceTerminal)bestT)
                {
                    case DiceTerminal.Attack: aSum += applied; break;
                    case DiceTerminal.Block:  bSum += applied; break;
                    default:                  cSum += applied; break;
                }
            }
        }

        // ============================================================
        //  価値関数（V/Q の 2 段分割 + 状態のみのメモ化）
        // ============================================================

        private readonly Dictionary<long, float> _vMemo = new Dictionary<long, float>();
        private int _nodesUsed;
        /// <summary>いま有効な予算。 配線決定は <see cref="NodeBudget"/>、
        /// リロールは部分集合ごとに <see cref="RerollNodeBudget"/>。</summary>
        private int _budget;

        /// <summary>状態の価値（手札を振る前）。 **メモ化はここだけ**。
        /// ターンは必ず増えるので再帰は必ず終わる。</summary>
        private float V(S s, int atkBase)
        {
            if (_nodesUsed >= _budget) return StaticValue(s);
            long key = StateKey(s);
            if (_vMemo.TryGetValue(key, out float cached)) return cached;
            _nodesUsed++; StatNodes++;

            double acc = 0, wsum = 0;
            float esc = Escalation.GetMultiplier(_escProfile, s.turn);
            var hands = Branches(_k);
            foreach (var (hand, hw) in hands)
                foreach (var (etot, ew) in _enemyDist)
                {
                    int atk = Math.Max(0, (int)Math.Round(esc * (_eBaseAtk + etot)));
                    double w = hw * ew;
                    acc += Q(s, hand, atk, atkBase) * w;
                    wsum += w;
                }
            float v = wsum > 0 ? (float)(acc / wsum) : StaticValue(s);
            _vMemo[key] = v;
            return v;
        }

        /// <summary>手札が確定した状態の価値。 候補配線の max。</summary>
        private float Q(S s, int[] dice, int enemyAtk, int atkBase)
        {
            var cands = Candidates(s, dice, enemyAtk, atkBase);
            float best = float.NegativeInfinity;
            foreach (var c in cands)
            {
                float v = TurnThenFuture(s, dice, enemyAtk, WiringPlan.Of(c.main), atkBase);
                if (v > best) best = v;
            }
            return best == float.NegativeInfinity ? 0f : best;
        }

        private float TurnThenFuture(S s, int[] dice, int enemyAtk, WiringPlan plan, int atkBase)
        {
            var ns = s;
            int outcome = ApplyTurn(ref ns, dice, enemyAtk, plan, atkBase);
            if (outcome > 0) return WinValue(ns);
            if (outcome < 0) return 0f;
            return V(ns, atkBase);
        }

        // ============================================================
        //  ターン適用と評価
        // ============================================================

        /// <summary>0 = 継続 / +1 = 敵HP 0 / −1 = 負け。</summary>
        private int ApplyTurn(ref S s, int[] dice, int enemyAtk, WiringPlan plan, int atkBase)
        {
            int aSum = 0, bSum = 0, cSum = 0;
            for (int i = 0; i < _k; i++)
            {
                int v = dice[i];
                switch (plan.main[i])
                {
                    case DiceTerminal.Attack: aSum += v; break;
                    case DiceTerminal.Block:  bSum += v; break;
                    default:                  cSum += v; break;
                }
                int g = plan.ghost != null ? plan.ghost[i] : WiringPlan.NoGhost;
                if (g != WiringPlan.NoGhost)
                {
                    int add = v + ((int)plan.main[i] == g ? DiceFaceParts.T4StackBonus : 0);
                    switch ((DiceTerminal)g)
                    {
                        case DiceTerminal.Attack: aSum += add; break;
                        case DiceTerminal.Block:  bSum += add; break;
                        default:                  cSum += add; break;
                    }
                }
            }

            // **敵の被ダメ軽減を必ず引く。** 抜けると火力を 1/(1−r) 倍に過大評価する。
            int raw = Math.Max(0, atkBase + aSum);
            s.eHp -= (int)Math.Round(raw * (1f - _eDefRate));
            if (s.eHp <= 0) return +1;
            s.pHp -= Math.Max(0, enemyAtk - bSum);
            // 2 体目は独立パケット。 ブロックは各パケットから全額引かれる (倍率なし)。
            if (_secondAtk > 0) s.pHp -= Math.Max(0, _secondAtk - bSum);
            if (s.pHp <= 0) return -1;
            s.charge = Math.Min(_chargeMax, s.charge + cSum);
            s.turn++;
            if (s.turn > StalemateTurn) return -1;
            return 0;
        }

        /// <summary>敵 HP を 0 にしたときの価値。
        ///
        /// <para><b>多段連戦では 1.0 を返してはいけない（design §7-2）。</b>
        /// ヴェスカは 4 段で、 p1 を倒しても戦闘は続く。 1.0 を返すと
        /// 「p1 を倒すために全資源を吐く」判断が正当化されてしまう。
        /// 次の段には HP が持ち越されるので、 残り HP 比で評価する。</para>
        ///
        /// <para>単段の敵は 1.0。 ラン単位の価値（終了時の充電や消耗品）は
        /// **まだ評価していない**（design §8-1）。</para></summary>
        private float WinValue(S s)
        {
            if (!_chained) return 1f;
            float hp = Math.Max(0f, Math.Min(1f, s.pHp / (float)_pMaxHp));
            return PhaseWinBase + (1f - PhaseWinBase) * hp;
        }

        /// <summary>予算切れの枝で使う**推定勝率**。
        ///
        /// <para><b>終端値と単位を揃えている（design §7-5）。</b> 初版は
        /// <c>0.10 + 0.80×進捗×HP</c> というヒューリスティック得点を、 勝ち 1.0 / 負け 0.0 と
        /// 同じ期待値の中で平均していた。 単位が違うので、 静的評価に落ちる枝の比率が
        /// 変わると評価がバイアスした。 いまは「この盤面から勝つ確率の粗い推定」として
        /// 0〜1 に収まる形にしている ── <b>較正はしていない</b>ので粗い推定でしかない。</para>
        ///
        /// <para>削り進捗と残 HP の比から、 相対優位をロジスティックで 0〜1 へ写す。</para></summary>
        private float StaticValue(S s)
        {
            float ep = Math.Max(0f, Math.Min(1f, s.eHp / (float)_eMaxHp));   // 敵の残り比
            float pp = Math.Max(0f, Math.Min(1f, s.pHp / (float)_pMaxHp));   // 自分の残り比
            // 「相手の方が削れている」ほど勝率が高い。 pp − ep を優位の指標にする。
            float adv = pp - ep;
            return 1f / (1f + (float)Math.Exp(-4.0 * adv));
        }

        // ============================================================
        //  ユーティリティ
        // ============================================================

        /// <summary>状態キー。 <b>スナップショットの世代を混ぜる</b> ── 混ぜないと
        /// 形態切替後に前の形態の価値が残る（design §7-3）。
        /// ここに含まれない値は「同じ状態」として畳まれる ── 停滞スタック・decay 系・
        /// 役の使用履歴・烈炎/焦土契約のスタックは持っていない（design §4-3）。</summary>
        private long StateKey(S s)
        {
            long h = _snapSig;
            void Mix(long x) { h ^= x; h *= 1099511628211L; }
            Mix(s.pHp); Mix(s.eHp); Mix(s.turn); Mix(s.charge);
            return h;
        }

        private static int Pow(int b, int e) { int r = 1; for (int i = 0; i < e; i++) r *= b; return r; }

        private DiceTerminal[] FallbackMain(int[] rolls)
        {
            int n = rolls?.Length ?? 0;
            var w = new DiceTerminal[n];
            for (int i = 0; i < n; i++) w[i] = DiceTerminal.Attack;
            return w;
        }

        private int[] NoGhostArray()
        {
            var g = new int[_k];
            for (int i = 0; i < _k; i++) g[i] = WiringPlan.NoGhost;
            return g;
        }
    }
}

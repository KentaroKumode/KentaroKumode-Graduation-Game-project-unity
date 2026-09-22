using System.Collections.Generic;
using MetaProgression;
using MetaProgression.Relics;

namespace AutoTest
{
    /// <summary>
    /// 周回モード（1 ペルソナぶんの 1 ライン）。 正本の設計: docs/GAME.md §15-5。
    ///
    /// 目的は「遺物と難易度のフィードバックループが収束するか / 停滞するか / 発散するか」を、
    /// **実ゲームの到達層とクリア判定**から測ること。 抽象モデルで成否を決めると
    /// モデルの仮定がそのまま結論になるので、 判定は必ず AutoRunner の実ランへ委ねる。
    ///
    /// 1 ラン の流れ:
    ///   BeforeRun()  現在の遺物を装備し、 現在の挑戦ロードアウトを適用する
    ///   （AutoRunner が実際にランを回す）
    ///   AfterRun()   遺物を 1 個引き、 効用が上なら乗り換える。 成績を見て難易度を上げる
    ///
    /// **ペルソナは 1 ライン中で固定する。** 遺物はそのペルソナの重みで蓄積していくので、
    /// 途中で入れ替わると効用の比較が意味を失う。
    /// </summary>
    public class AscensionLoop
    {
        /// <summary>難易度の上げ方。 **閾値の置き方が結論を左右する**ので、
        /// 単一のポリシーで結論を出さず、 対照群 (Fixed) を含めて比べること。</summary>
        public enum Policy
        {
            /// <summary>難易度を上げない対照群。 遺物だけが伸びる。</summary>
            Fixed,
            /// <summary>直近窓の 5 層クリア率 70% 以上で +1 段。</summary>
            Conservative,
            /// <summary>**押し上げ型・双方向** (2026-08-04)。 同 20% 以上で +1 段、 10% 未満で −1 段。
            /// 「かろうじてクリアできる最大難易度」を探る。 旧 50%・片道から変更。</summary>
            Standard,
            /// <summary>同 30% 以上で +1 段。</summary>
            Aggressive,
            /// <summary>Standard に加えて、 クリア率 20% 未満なら **1 段下げる**（双方向）。</summary>
            Adaptive,
        }

        public readonly BuildPersona persona;
        public readonly Policy policy;
        /// <summary>成績を見る窓幅（ラン数）。 短すぎると分散で誤判定する。</summary>
        public readonly int window;

        private readonly Queue<bool> _recent = new Queue<bool>();
        private RolledRelic _relic;
        private float _relicUtility;
        private int _runIndex;

        /// <summary>挑戦構成の**正本はここ**。 State 側には毎ラン入れ直す。
        ///
        /// AutoRunner はラン開始時に <c>MetaAllocationPresets.Apply</c> → <c>ResetAll()</c> を呼び、
        /// MetaProgressState を丸ごと作り直す。 State に置いたままだと遺物も挑戦構成も
        /// 毎ラン消えるので、 ライン側で保持して BeforeRun で流し込む。</summary>
        private ChallengeLoadout _loadout = new ChallengeLoadout();

        /// <summary>現在の挑戦スコア。 State ではなくライン側の構成から算出する。</summary>
        public int ChallengeScore => ChallengeResolver.Score(_loadout);
        /// <summary>難易度を上げた直後は成績が落ちるので、 窓が埋まるまで再判定しない。</summary>
        private int _cooldown;

        public RolledRelic CurrentRelic => _relic;
        public float CurrentUtility => _relicUtility;
        public int SwapCount { get; private set; }
        public int DifficultyUpCount { get; private set; }
        public int DifficultyDownCount { get; private set; }
        /// <summary>直近に上げたカテゴリ。 次はこの「次」から探して巡回させる。</summary>
        private ChallengeCategory _lastRaisedCategory = ChallengeCategory.E崩壊;

        public AscensionLoop(BuildPersona persona, Policy policy, int window = 20)
        {
            this.persona = persona;
            this.policy  = policy;
            this.window  = UnityEngine.Mathf.Max(5, window);
        }

        private static MetaProgressState State => MetaProgressManager.Instance?.State;

        /// <summary>ライン開始時。 遺物なし・挑戦スコア 0 から始める。</summary>
        public void Reset()
        {
            _recent.Clear(); _relic = null; _relicUtility = 0f;
            _runIndex = 0; _cooldown = 0;
            SwapCount = DifficultyUpCount = DifficultyDownCount = 0;
            _lastRaisedCategory = ChallengeCategory.E崩壊;   // 次は A から始まる
            _loadout = new ChallengeLoadout();
            ApplyToState();
        }

        /// <summary>ラン開始前。 現在の遺物と挑戦構成を State へ反映する。
        ///
        /// **AutoRunner のメタ前処理 (ResetAll) より後に呼ぶこと。** 先に呼ぶと
        /// State ごと作り直されて全部消える。</summary>
        public void BeforeRun() => ApplyToState();

        private void ApplyToState()
        {
            var st = State;
            if (st == null) return;
            st.relics = new List<RolledRelic>();
            st.equippedRelicIndex = -1;
            if (_relic != null) st.AddRelic(_relic);
            st.challenge = _loadout;
            st.InvalidateChallenge();
        }

        /// <summary>ラン終了後。 遺物を引いて乗り換え判定、 続いて難易度判定。</summary>
        public void AfterRun(int reachedFloor, bool cleared)
        {
            _runIndex++;

            // ---- ① 遺物を 1 個引く ----
            int score = ChallengeScore;
            var got = RelicRoller.Roll(reachedFloor, cleared, score, _runIndex);
            if (got != null && got.IsValid())
            {
                float u = RelicUtility.Score(got, persona);
                if (u > _relicUtility)
                {
                    _relic = got; _relicUtility = u; SwapCount++;
                }
            }

            // ---- ② 成績を窓へ積む ----
            // 「5 層クリア以上」を成功の基準にする ── §15-5 で層点が +3 跳ねる節目であり、
            // ここを安定して超えられるかが難易度を上げてよいかの判断材料になる。
            // 呼び出し側 (AutoRunner) が cleared に「5 層以上をクリアしたか」を入れてくる。
            bool success = cleared;
            _recent.Enqueue(success);
            while (_recent.Count > window) _recent.Dequeue();

            // ---- ③ 難易度判定 ----
            if (_cooldown > 0) { _cooldown--; return; }
            if (policy == Policy.Fixed || _recent.Count < window) return;

            int wins = 0;
            foreach (var b in _recent) if (b) wins++;
            float rate = (float)wins / _recent.Count;

            float upAt   = UpThreshold(policy);
            float downAt = DownThreshold(policy);
            if (rate >= upAt)
            {
                if (RaiseDifficulty()) { DifficultyUpCount++; _cooldown = window; }
            }
            else if (downAt >= 0f && rate < downAt)
            {
                if (LowerDifficulty()) { DifficultyDownCount++; _cooldown = window; }
            }
        }

        /// <summary>**Standard は「高難易度到達」を目標にした押し上げ型** (2026-08-04)。
        ///
        /// 旧実装は 5 層クリア率 50% を昇格閾値にし、 かつ下げ判定が Adaptive 限定だったため、
        /// **50% を割った瞬間に片道で固着**していた（全ペルソナ 難度↓0・挑戦13 前後が上限）。
        /// これは「遺物効率の最適解」ではあっても、 **「高難易度をクリアする」という目標には合わない** ──
        /// 勝率 50% を切ったから限界、 という基準では 15 前後で止まってしまう。
        ///
        /// 勝率が 5 割を割っても押し上げ続け、 **10% を切って初めて 1 段下げる**。
        /// 20% / 10% の band に落ち着くので、 「かろうじてクリアできる最大難易度」を探る挙動になる。</summary>
        private const float StandardUpAt   = 0.20f;
        private const float StandardDownAt = 0.10f;

        /// <summary>目標スコアに達するまで `RaiseDifficulty` と同じ規則で積んだ挑戦構成を返す。
        /// **固定難易度スイープ用** ── 「難易度 N で 7 層をクリアできるか」を測るのに使う。
        /// ラチェットの探索挙動を挟まないので、 難易度そのものの壁が見える。</summary>
        public static ChallengeLoadout BuildLoadoutAtScore(int targetScore)
        {
            var lo = new ChallengeLoadout();
            var lastCat = ChallengeCategory.E崩壊;
            int catCount = System.Enum.GetValues(typeof(ChallengeCategory)).Length;
            for (int guard = 0; guard < 200 && ChallengeResolver.Score(lo) < targetScore; guard++)
            {
                int minCost = int.MaxValue;
                foreach (var def in ChallengeCatalog.Axes)
                {
                    int cur = lo.GetTier(def.axis);
                    for (int i = 0; i < def.availableTiers.Length; i++)
                    {
                        int t = def.availableTiers[i];
                        if (t <= cur) continue;
                        // v4.1: 段番号 = 点数。 PointsOf は存在しない Tier を 0 にするので
                        //   「その軸に無い Tier」を経由したコスト計算にならない。
                        int cost = def.PointsOf(t) - def.PointsOf(cur);
                        if (cost < minCost) minCost = cost;
                        break;
                    }
                }
                bool moved = false;
                if (minCost != int.MaxValue)
                {
                    for (int off = 1; off <= catCount && !moved; off++)
                    {
                        var cat = (ChallengeCategory)(((int)lastCat + off) % catCount);
                        foreach (var def in ChallengeCatalog.Axes)
                        {
                            if (def.category != cat) continue;
                            int cur = lo.GetTier(def.axis);
                            for (int i = 0; i < def.availableTiers.Length; i++)
                            {
                                int t = def.availableTiers[i];
                                if (t <= cur) continue;
                                if (def.PointsOf(t) - def.PointsOf(cur) != minCost) break;
                                lo.SetTier(def.axis, t); lastCat = cat; moved = true;
                                break;
                            }
                            if (moved) break;
                        }
                    }
                }
                // **軸が尽きたら Tier4 を解禁する。** 軸だけでは 30pt までしか行かないので、
                // 満点 50pt を作るには解禁済みカテゴリの Tier4 を積む必要がある。
                if (!moved)
                {
                    for (int i = 0; i < ChallengeCatalog.T4s.Count && !moved; i++)
                    {
                        var t4 = ChallengeCatalog.T4s[i];
                        if (lo.HasT4(t4.t4)) continue;
                        if (!ChallengeResolver.CanSelectT4(lo, t4.t4)) continue;
                        lo.SetT4(t4.t4, true); moved = true;
                    }
                }
                if (!moved) break;
            }
            return lo;
        }

        private static float UpThreshold(Policy p)
            => p == Policy.Conservative ? 0.70f
             : p == Policy.Aggressive   ? 0.30f
             : p == Policy.Adaptive     ? 0.50f
             :                            StandardUpAt;     // Standard

        /// <summary>負値なら片道（下げない）。</summary>
        private static float DownThreshold(Policy p)
            => p == Policy.Adaptive ? 0.20f
             : p == Policy.Standard ? StandardDownAt        // **双方向化** (2026-08-04)
             :                        -1f;

        // ============================================================
        //  難易度の上げ下げ
        // ============================================================

        /// <summary>上げ幅を 1 つ適用する。 **決定論** ── 同じシードなら同じ難易度履歴になる。
        ///
        /// 選定は 2 段: **① コスト昇順が最優先 → ② 同コスト内でカテゴリを巡回**。
        ///
        /// **なぜカテゴリ巡回が要るか** (2026-08-04): 旧実装は「最も安い上げ幅」だけを見て、
        /// 同点は `cost &lt; bestCost` の厳密不等号で **List の先頭が必ず勝っていた**。
        /// 結果、脆弱な肉体 T1→T3 → 練度不足 T1→T3 → 偽の硬貨 → 不足する物資 → 絶望的な戦闘 と
        /// **カテゴリA(数値悪化) だけを埋め続け**、B(戦闘機構) に入るのは挑戦pt 15 から。
        /// 実測のラチェット上限は 4〜13 なので、**B〜E の 39pt が一度も発火していなかった**。
        /// 「挑戦上限」も難易度一般ではなく A への耐性しか測れていなかった (§24)。
        ///
        /// **コスト昇順を優先キーに残す**のは、カテゴリE が T3 (cost 3) しか持たないため。
        /// 巡回だけにすると挑戦pt 3 で天変地異が入って刻みが粗くなる。
        /// cost 1〜2 が他に残っている限り E は選ばれない。</summary>
        private bool RaiseDifficulty()
        {
            var lo = _loadout;

            // ① 各軸の「最小の上げ幅」を集め、 全体の最小コストを求める。
            int minCost = int.MaxValue;
            foreach (var def in ChallengeCatalog.Axes)
            {
                int cur = lo.GetTier(def.axis);
                for (int i = 0; i < def.availableTiers.Length; i++)
                {
                    int t = def.availableTiers[i];
                    if (t <= cur) continue;
                    if (t - cur < minCost) minCost = t - cur;   // Tier = 点数 なので差分がコスト
                    break;
                }
            }
            if (minCost == int.MaxValue) return false;          // 全軸が最高 Tier

            // ② 最小コストの候補の中で、 **前回上げたカテゴリの次**から巡回して最初に見つかる軸。
            int catCount = System.Enum.GetValues(typeof(ChallengeCategory)).Length;
            for (int off = 1; off <= catCount; off++)
            {
                var cat = (ChallengeCategory)(((int)_lastRaisedCategory + off) % catCount);
                foreach (var def in ChallengeCatalog.Axes)
                {
                    if (def.category != cat) continue;
                    int cur = lo.GetTier(def.axis);
                    for (int i = 0; i < def.availableTiers.Length; i++)
                    {
                        int t = def.availableTiers[i];
                        if (t <= cur) continue;
                        if (t - cur != minCost) break;          // この軸の最小上げ幅が最小コストでない
                        if (!lo.SetTier(def.axis, t)) return false;
                        _lastRaisedCategory = cat;
                        ApplyToState();
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>最後に上げた分を戻すのではなく、 **最も高い Tier を 1 段下げる**。
        /// 履歴を持たずに済み、 かつ「一番きついものから緩める」ので挙動が読みやすい。</summary>
        private bool LowerDifficulty()
        {
            var lo = _loadout;
            int bestTier = 0; ChallengeAxis bestAxis = default;
            foreach (var def in ChallengeCatalog.Axes)
            {
                int cur = lo.GetTier(def.axis);
                if (cur > bestTier) { bestTier = cur; bestAxis = def.axis; }
            }
            if (bestTier <= 0) return false;
            // 1 つ下の「存在する Tier」へ落とす (無ければ解除)
            var d = ChallengeCatalog.Get(bestAxis);
            int next = 0;
            for (int i = 0; i < d.availableTiers.Length; i++)
                if (d.availableTiers[i] < bestTier) next = d.availableTiers[i];
            if (!lo.SetTier(bestAxis, next)) return false;
            ApplyToState();
            return true;
        }

        /// <summary>レポート 1 行。</summary>
        public string Summary()
        {
            return $"{persona,-9} {policy,-12} 挑戦{ChallengeScore,3}pt  効用{_relicUtility,5:F1}  "
                 + $"乗換{SwapCount,3}回  難度↑{DifficultyUpCount,2} ↓{DifficultyDownCount,2}  "
                 + (_relic != null ? _relic.Describe() : "遺物なし");
        }
    }
}

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
        /// <summary>このラインの所持品 (2026-11-10 再編: 3 部位・強化・分解)。 持ち込みは毎ラン RelicUtility.ChooseLoadout で選び直す。</summary>
        private readonly List<Relic> _owned = new List<Relic>();
        private Relic[] _equipped = new Relic[3];
        private int _material;
        private float _relicUtility;
        private int _runIndex;

        /// <summary>挑戦の難易度 (挑戦カードの目盛り 0..10) の**正本はここ**。 State 側には毎ラン入れ直す
        /// (AutoRunner はラン開始時に State を作り直すので、 ライン側で保持して BeforeRun で流し込む)。
        /// 2026-11-10: 旧の配点 (軸ごとの Tier) から置き換えた (#9)。</summary>
        private int _difficulty;

        /// <summary>現在の挑戦の難易度 (0..10)。</summary>
        public int Difficulty => _difficulty;
        /// <summary>難易度を上げた直後は成績が落ちるので、 窓が埋まるまで再判定しない。</summary>
        private int _cooldown;

        /// <summary>今の持ち込み (表示用の 1 行)。</summary>
        public string CurrentRelicText => RelicUtility.Explain(_equipped, persona);
        public float CurrentUtility => _relicUtility;
        public int SwapCount { get; private set; }
        public int DifficultyUpCount { get; private set; }
        public int DifficultyDownCount { get; private set; }

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
            _recent.Clear(); _owned.Clear(); _equipped = new Relic[3]; _material = 0; _relicUtility = 0f;
            _runIndex = 0; _cooldown = 0;
            SwapCount = DifficultyUpCount = DifficultyDownCount = 0;
            _difficulty = 0;
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
            var eq = new List<Relic>();
            foreach (var r in _equipped) if (r != null) eq.Add(r);
            st.SetRelicLoadout(eq);
            st.challengeDifficulty = _difficulty;
        }

        /// <summary>ラン終了後。 遺物を引いて持ち込みを選び直し、 要らないものを分解して強化、 続いて難易度判定。
        /// ordinalReached は見せる層 (1〜5)、 cleared は 5 層を踏破したか。</summary>
        public void AfterRun(int ordinalReached, bool cleared)
        {
            _runIndex++;

            // ---- ① 遺物を引く (落ちる数は到達した層で決まる) ----
            int diff = _difficulty;
            var got = RelicRoller.RollRunEnd(_runIndex, ordinalReached, diff, (int)RelicUtility.PreferredSet(persona));
            _owned.AddRange(got);
            var prev = _equipped;
            _equipped = RelicUtility.ChooseLoadout(_owned, persona);
            for (int k = 0; k < 3; k++) if (_equipped[k] != prev[k]) { SwapCount++; break; }
            // 持ち込みに使っていない遺物は分解して素材にし、 持ち込みを強化する
            for (int i = _owned.Count - 1; i >= 0; i--)
                if (System.Array.IndexOf(_equipped, _owned[i]) < 0) { _owned.RemoveAt(i); _material++; }
            var rng = new System.Random(_runIndex * 7919 + 17);
            foreach (var r in _equipped)
                while (r != null && r.CanEnhance && _material > 0) { RelicRoller.Enhance(r, rng); _material--; }
            var eqList = new List<Relic>(); foreach (var r in _equipped) if (r != null) eqList.Add(r);
            _relicUtility = RelicUtility.ScoreLoadout(eqList, persona);

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

        /// <summary>難易度を 1 上げる (最大 10)。</summary>
        private bool RaiseDifficulty()
        {
            if (_difficulty >= MetaProgression.ChallengeCards.MaxDifficulty) return false;
            _difficulty++; ApplyToState(); return true;
        }

        /// <summary>難易度を 1 下げる (最小 0)。</summary>
        private bool LowerDifficulty()
        {
            if (_difficulty <= 0) return false;
            _difficulty--; ApplyToState(); return true;
        }

        /// <summary>レポート 1 行。</summary>
        public string Summary()
        {
            return $"{persona,-9} {policy,-12} 難易度{_difficulty,3}  効用{_relicUtility,5:F1}  "
                 + $"乗換{SwapCount,3}回  難度↑{DifficultyUpCount,2} ↓{DifficultyDownCount,2}  "
                 + CurrentRelicText;
        }
    }
}

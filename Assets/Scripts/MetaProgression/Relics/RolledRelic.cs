using System.Collections.Generic;

namespace MetaProgression.Relics
{
    /// <summary>
    /// 生成済みの遺物 1 個。 正本: docs/GAME.md §15-5。
    ///
    /// **JsonUtility でシリアライズされる**（MetaProgressState 経由で PlayerPrefs へ）。
    /// そのため Dictionary は使えず、 軸と段を並列 List で持つ
    /// （endingClearKeys / endingClearValues と同じ回避）。
    ///
    /// index 0 がメイン枠、 1 以降がサブ枠。 順序に意味があるので並べ替えない。
    /// </summary>
    [System.Serializable]
    public class RolledRelic
    {
        /// <summary>軸 id のリスト。 (int)RelicAxis。 steps と並列・同じ長さ。</summary>
        public List<int> axes = new List<int>();
        /// <summary>段のリスト。 メイン枠は 3〜6、 サブ枠は 1〜3。
        /// **刻印はここに反映しない** ── 総点は段 6 のまま計算し、効果だけ 7 段扱いにする。</summary>
        public List<int> steps = new List<int>();

        /// <summary>刻印（呪い）。 None なら通常の遺物。</summary>
        public int curse;

        /// <summary>生成時の到達層と挑戦スコア（表示・デバッグ用。 効果には使わない）。</summary>
        public int sourceLayerPoint;
        public int sourceChallengeScore;

        public int SlotCount => axes != null ? axes.Count : 0;
        public RelicCurse Curse => (RelicCurse)curse;
        public bool IsCursed => Curse != RelicCurse.None;

        /// <summary>段の合計。 §15-5 の「総点」。 構造上限は 18。
        /// **刻印による +1 は含めない**（刻印は総点を増やさない）。</summary>
        public int TotalPoints
        {
            get
            {
                int sum = 0;
                if (steps != null) for (int i = 0; i < steps.Count; i++) sum += steps[i];
                return sum;
            }
        }

        public RelicAxis AxisAt(int i)
            => (axes != null && i >= 0 && i < axes.Count) ? (RelicAxis)axes[i] : RelicAxis.Attack;

        public int StepAt(int i)
            => (steps != null && i >= 0 && i < steps.Count) ? steps[i] : 0;

        /// <summary>効果として使う段。 **メイン枠かつ刻印付きなら 7**。
        /// 刻印の条件を満たしていない間はメインが発動しないので、 呼び出し側は
        /// RelicApplicator 側で条件を見てから使うこと。</summary>
        public int EffectiveStepAt(int i)
            => (i == 0 && IsCursed) ? RelicAxisCatalog.CursedStep : StepAt(i);

        /// <summary>この遺物の名前。 メイン軸と総点で決まり、 刻印付きは「刻印の」が付く。</summary>
        public string DisplayName
            => SlotCount == 0 ? "（空）"
             : RelicAxisCatalog.RelicNameOf(AxisAt(0), TotalPoints, IsCursed);

        /// <summary>指定軸が何枠目に載っているか。 無ければ -1。
        /// **同一軸の重複は生成側で禁止**しているので、 最初に見つかった 1 件でよい。</summary>
        public int IndexOf(RelicAxis axis)
        {
            if (axes == null) return -1;
            for (int i = 0; i < axes.Count; i++) if (axes[i] == (int)axis) return i;
            return -1;
        }

        /// <summary>健全性チェック。 セーブ破損や enum 並べ替え事故を検出する。</summary>
        public bool IsValid()
        {
            if (axes == null || steps == null) return false;
            int n = axes.Count;
            if (n < 3 || n > 5 || steps.Count != n) return false;
            int axisMax = RelicAxisCatalog.All.Length;
            var seen = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                if (axes[i] < 0 || axes[i] >= axisMax) return false;
                if (!seen.Add(axes[i])) return false;             // 同一軸の重複は禁止
                int lo = i == 0 ? RelicAxisCatalog.MainStepMin : RelicAxisCatalog.SubStepMin;
                int hi = i == 0 ? RelicAxisCatalog.MainStepMax : RelicAxisCatalog.SubStepMax;
                if (steps[i] < lo || steps[i] > hi) return false;
            }
            return curse >= 0 && curse <= (int)RelicCurse.NoRoleFired;
        }

        public string Describe()
        {
            if (SlotCount == 0) return "（空）";
            var sb = new System.Text.StringBuilder();
            sb.Append($"{DisplayName} [{TotalPoints}pt/{SlotCount}枠]");
            if (IsCursed) sb.Append($"  刻印: {RelicAxisCatalog.DescribeCurse(Curse)}");
            int n = System.Math.Min(axes.Count, steps.Count);
            for (int i = 0; i < n; i++)
            {
                sb.Append(i == 0 ? "  ★" : " / ");
                sb.Append(RelicAxisCatalog.Describe(AxisAt(i), EffectiveStepAt(i)));
            }
            return sb.ToString();
        }
    }
}

using System.Collections.Generic;
using System.Text;

namespace MetaProgression.Relics
{
    /// <summary>
    /// 遺物 1 個 (§15-5・2026-11-10 再編)。 JsonUtility でセーブするので、 フィールドは int / List&lt;int&gt; だけにする。
    /// 主ステはセットと部位で決まり、 段は★で決まる (<see cref="RelicCatalog.MainStepOf"/>)。 強化はサブだけを伸ばす。
    /// </summary>
    [System.Serializable]
    public class Relic
    {
        public int slot;        // RelicSlot
        public int set;         // RelicSet
        public int stars;       // 1..4
        public int enhance;     // 0..4
        public List<int> subStats = new List<int>();   // RelicStat
        public List<int> subSteps = new List<int>();   // 1..2

        public RelicSlot Slot => (RelicSlot)slot;
        public RelicSet Set => (RelicSet)set;
        public RelicStat MainStat => RelicCatalog.MainOf(Set, Slot);
        public int MainStep => RelicCatalog.MainStepOf(stars);
        public int SubCount => subStats != null ? subStats.Count : 0;

        public bool IsValid()
        {
            if (slot < 0 || slot >= RelicCatalog.SlotCount) return false;
            if (set < 0 || set >= RelicCatalog.SetCount) return false;
            if (stars < 1 || stars > RelicCatalog.MaxStars) return false;
            if (enhance < 0 || enhance > RelicCatalog.MaxEnhance) return false;
            if (subStats == null || subSteps == null || subStats.Count != subSteps.Count || subStats.Count > RelicCatalog.MaxSubs) return false;
            for (int i = 0; i < subStats.Count; i++)
                if (subSteps[i] < 1 || subSteps[i] > RelicCatalog.SubStepMax) return false;
            return true;
        }

        /// <summary>この遺物が持つ、 指定ステータスの段の合計 (主 ＋ サブ)。</summary>
        public int StepOf(RelicStat s)
        {
            int n = MainStat == s ? MainStep : 0;
            if (subStats != null)
                for (int i = 0; i < subStats.Count; i++) if (subStats[i] == (int)s) n += subSteps[i];
            return n;
        }

        /// <summary>段の合計 (主 ＋ サブ)。 最大 10。</summary>
        public int TotalSteps
        {
            get { int n = MainStep; if (subSteps != null) foreach (var x in subSteps) n += x; return n; }
        }

        public bool CanEnhance => enhance < RelicCatalog.MaxEnhance;

        public string Name => RelicCatalog.NameOf(Set, Slot);

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(Name).Append(" ★").Append(stars);
            if (enhance > 0) sb.Append(" +").Append(enhance);
            sb.Append(" [").Append(RelicCatalog.Describe(MainStat, MainStep)).Append(']');
            for (int i = 0; i < SubCount; i++)
                sb.Append(i == 0 ? " / " : "・").Append(RelicCatalog.Describe((RelicStat)subStats[i], subSteps[i]));
            return sb.ToString();
        }

        public Relic Clone()
            => new Relic { slot = slot, set = set, stars = stars, enhance = enhance,
                           subStats = new List<int>(subStats ?? new List<int>()), subSteps = new List<int>(subSteps ?? new List<int>()) };
    }
}

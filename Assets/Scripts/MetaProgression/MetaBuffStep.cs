namespace MetaProgression
{
    /// <summary>
    /// バフトラック上の1段階。115段で1ラインを構成する。
    /// </summary>
    [System.Serializable]
    public class MetaBuffStep
    {
        public MetaBuffKind kind;
        public int amount = 1;
        public bool isMajor;     // 大スキル表示用
        public string label;     // UI 用ラベル（自動生成可）

        public string DisplayLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(label)) return label;
                switch (kind)
                {
                    case MetaBuffKind.Hp:               return $"開幕HP +{amount}";
                    case MetaBuffKind.Gold:             return $"開幕ゴールド +{amount}";
                    case MetaBuffKind.DiceTotal:        return $"ダイス合計 +{amount}";
                    case MetaBuffKind.DamageReduce:     return $"被ダメージ -{amount}";
                    case MetaBuffKind.HungerReduce:     return $"飢餓ダメージ -{amount}";
                    case MetaBuffKind.StartMaterial:    return $"開幕武器強化素材 +{amount}";
                    case MetaBuffKind.CombatGoldBonus:  return $"戦闘勝利金 +{amount}";
                    case MetaBuffKind.BossExtraNormal:  return "ボス撃破時 ノーマルパッシブ獲得";
                    case MetaBuffKind.BossExtraRare:    return "ボス追加報酬を レア化";
                    case MetaBuffKind.RefundLevelUp:    return "購入返金 レベル+1";
                    case MetaBuffKind.CritLevelUp:      return "会心ダイス補正 レベル+1";
                    default: return kind.ToString();
                }
            }
        }
    }
}

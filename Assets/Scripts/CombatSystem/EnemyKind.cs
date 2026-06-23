using MapSystem;

namespace CombatSystem
{
    /// <summary>
    /// 戦闘相手の種別。 マップタイル種別から導出される。
    /// 用途: 暗殺教団契約の対象判定 (通常エネミーのみ発動) 等、
    /// 「相手が雑魚かエリートかボスか」 で挙動を分けたい契約/効果。
    /// </summary>
    public enum EnemyKind
    {
        Normal,  // Battle / Mystery 等の通常戦闘
        Elite,   // EliteBattle / LambdaRing / 偽商人戦
        Boss,    // Boss
    }

    public static class EnemyKindExtensions
    {
        /// <summary>マップタイル種別から戦闘相手種別を導出する。
        /// 不明なタイル種別は Normal にフォールバック (契約効果が安全側で発動するため)。</summary>
        public static EnemyKind ToEnemyKind(this TileType t)
        {
            switch (t)
            {
                case TileType.Battle:       return EnemyKind.Normal;
                case TileType.EliteBattle:  return EnemyKind.Elite;
                case TileType.LambdaRing:   return EnemyKind.Elite;
                case TileType.Boss:         return EnemyKind.Boss;
                default:                    return EnemyKind.Normal;
            }
        }
    }
}

using System.Collections.Generic;

namespace GameLoop.Contracts
{
    /// <summary>
    /// 旅団間の敵対 (6 ペア) と協力 (6 ペア) 関係の正本テーブル。
    /// 設計詳細は docs/specs/contracts.md「敵対関係」「協力関係」セクション。
    /// </summary>
    public static class ContractRelations
    {
        /// <summary>敵対関係 (双方向)。</summary>
        public static readonly (ContractKind a, ContractKind b)[] Rivalries =
        {
            (ContractKind.Knights, ContractKind.BodyDoubles),
            (ContractKind.Missionaries, ContractKind.Assassins),
            (ContractKind.Hunters, ContractKind.OrphanCircus),
            (ContractKind.Mercenaries, ContractKind.Tacticians),
            (ContractKind.MerchantsLeague, ContractKind.Alchemist),
            (ContractKind.SupplyCaravan, ContractKind.WanderingDoctor),
        };

        /// <summary>協力関係 (双方向)。</summary>
        public static readonly (ContractKind a, ContractKind b)[] Alliances =
        {
            (ContractKind.Knights, ContractKind.Missionaries),
            (ContractKind.Assassins, ContractKind.Tacticians),
            (ContractKind.OrphanCircus, ContractKind.MerchantsLeague),
            (ContractKind.Alchemist, ContractKind.SupplyCaravan),
            (ContractKind.BodyDoubles, ContractKind.Mercenaries),
            (ContractKind.Hunters, ContractKind.WanderingDoctor),
        };

        private static readonly Dictionary<ContractKind, ContractKind> _rivalMap = BuildPairMap(Rivalries);
        private static readonly Dictionary<ContractKind, ContractKind> _allyMap = BuildPairMap(Alliances);

        public static bool TryGetRival(ContractKind k, out ContractKind rival)
            => _rivalMap.TryGetValue(k, out rival);

        public static bool TryGetAlly(ContractKind k, out ContractKind ally)
            => _allyMap.TryGetValue(k, out ally);

        public static bool AreRivals(ContractKind a, ContractKind b)
            => _rivalMap.TryGetValue(a, out var r) && r == b;

        public static bool AreAllies(ContractKind a, ContractKind b)
            => _allyMap.TryGetValue(a, out var ally) && ally == b;

        private static Dictionary<ContractKind, ContractKind> BuildPairMap((ContractKind a, ContractKind b)[] pairs)
        {
            var d = new Dictionary<ContractKind, ContractKind>(pairs.Length * 2);
            foreach (var (a, b) in pairs)
            {
                d[a] = b;
                d[b] = a;
            }
            return d;
        }
    }
}

using System;
using System.Collections.Generic;
using MapSystem;

namespace AutoTest.Ultra
{
    /// <summary>What a tile looks like to the player.
    ///
    /// <para><see cref="Unknown"/> is a real, distinct answer ── not a missing value. An
    /// unrevealed tile is a tile whose type the player has not learned yet, and the search
    /// must reason about it as such rather than being handed the answer.</para></summary>
    public enum UltraTileView
    {
        Unknown = 0,
        Outpost, Battle, EliteBattle, Rest, Treasure, Shop, Event, Mystery,
        Exchange, Trap, Boss, Gate, LambdaRing, LambdaExit, Other,
    }

    /// <summary>One tile, reduced to what is on screen.
    ///
    /// <para><b>Deliberately absent: <c>isFalseMerchant</c>.</b> <see cref="MapGenerator"/>
    /// documents it as 「type は Shop のまま（プレイヤーには判別不可）」 ── it is a hidden
    /// property of a *revealed* tile, so "the tile is revealed" is not sufficient grounds to
    /// copy every field off it. Each field is judged on its own.</para></summary>
    [Serializable]
    public sealed class UltraMapNodeView
    {
        public string id;
        public int row;
        public int lane;
        /// <summary><see cref="UltraTileView.Unknown"/> when the node is not revealed.</summary>
        public UltraTileView view;
        public bool revealed;
        public bool visited;
        /// <summary>Whether the player may move here from the current node right now.</summary>
        public bool reachableNow;
        public string[] connections = new string[0];
    }

    /// <summary>The map as the player sees it.</summary>
    [Serializable]
    public sealed class UltraMapView
    {
        public int floor;
        public int rowCount;
        public string currentNodeId;
        public string bossNodeId;
        public UltraMapNodeView[] nodes = new UltraMapNodeView[0];
    }

    /// <summary>Run-level public state.
    ///
    /// <para>This is a whitelist, not a projection of <see cref="GameLoop.RunState"/>. New
    /// <c>RunState</c> fields do not appear here automatically ── <c>UltraObservationTests</c>
    /// fails until someone classifies the new field as public or hidden. Growing silently is
    /// the failure mode this exists to prevent.</para></summary>
    [Serializable]
    public sealed class UltraRunView
    {
        public int currentFloor;
        public int maxFloor;
        public int normalClearFloor;
        public bool bossDefeatedThisFloor;
        public int playerHP;
        public int playerMaxHP;
        public int coins;
        public int weaponMaterials;
        public int tilesThisFloor;
        public int totalBattles;
        public int totalWins;
        public int totalTurns;
        public int totalCombatTurns;
        public bool inLambda;
        public int dimensionalDisturbance;
        public int convictionStage;
        public bool layer6Unlocked;
        public bool layer7Unlocked;
        public bool shopRobberyDone;
        public bool lastStandActive;
        public int sublimationCount;
        public int sealedRoleMask;
        public int lingeringWoundLost;
        public string equippedWeaponId = "";
        public string equippedDiceId = "";
        public string[] ownedPassiveItems = new string[0];
        public string[] ownedConsumables = new string[0];
        public string[] permanentDebuffs = new string[0];
        public string[] activePhenomena = new string[0];
        public string[] timedBuffKeys = new string[0];
        public int[] timedBuffTurns = new int[0];
        public string[] timedDebuffKeys = new string[0];
        public int[] timedDebuffTurns = new int[0];
        public string[] lambdaDebuffKeys = new string[0];
        public int[] lambdaDebuffLevels = new int[0];
        public string[] diceFacePartIds = new string[0];
    }

    /// <summary>One option in a discrete choice the game is currently presenting.</summary>
    [Serializable]
    public sealed class UltraChoiceOption
    {
        public int index;
        /// <summary>Stable identity where one exists (item id, "heal", "upgrade"). Empty when
        /// the option is identified only by position.</summary>
        public string id = "";
        public string label = "";
        /// <summary>Whether the player could actually take it right now (gold, materials,
        /// capacity). An unaffordable option is still shown — it just cannot be chosen — so
        /// hiding it would misrepresent the board.</summary>
        public bool available = true;
        /// <summary>Cost in the option's own currency, or 0. Public: the game shows it.</summary>
        public int cost;
    }

    /// <summary>A discrete choice currently on screen. Empty <see cref="options"/> means the
    /// game is not asking anything right now.</summary>
    [Serializable]
    public sealed class UltraChoiceView
    {
        /// <summary>"reward" / "rest" / "event" / "shop". Free-form label for diagnostics;
        /// the decision point in the checkpoint is what carries meaning.</summary>
        public string kind = "";
        public string prompt = "";
        public UltraChoiceOption[] options = new UltraChoiceOption[0];
    }

    /// <summary>A complete public observation.
    ///
    /// <para><b>The point of this type is what it cannot express.</b> There is no field for
    /// the live seed, the RNG counter, an unrevealed tile's type, the shop's future stock, or
    /// the enemy's next roll. A controller handed one of these cannot cheat even if it tries,
    /// because the information never crossed the boundary.</para></summary>
    [Serializable]
    public sealed class UltraObservation
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        /// <summary>Monotonic within a run. Lets a stale observation be rejected on commit.</summary>
        public int epoch;
        public string phase = "";
        public UltraRunView run = new UltraRunView();
        public UltraMapView map = new UltraMapView();
        /// <summary>The choice on screen, if any. Part of the observation rather than of the
        /// action set because the controller has to <b>see</b> what it is choosing between,
        /// not merely be told how many buttons there are.</summary>
        public UltraChoiceView choice = new UltraChoiceView();

        /// <summary>Names of fields deliberately excluded, with the reason. Carried in the
        /// type itself so the rule is visible where the data is, not only in a doc.</summary>
        public static readonly KeyValuePair<string, string>[] DeliberateExclusions =
        {
            new KeyValuePair<string, string>("MapNode.isFalseMerchant",
                "hidden property of a revealed Shop tile; MapGenerator marks it 'プレイヤーには判別不可'"),
            new KeyValuePair<string, string>("MapNode.resolvedType(unrevealed)",
                "an unrevealed tile's type is exactly what the player has not learned yet"),
            new KeyValuePair<string, string>("GameRng.state",
                "live RNG state; reading it is future knowledge"),
            new KeyValuePair<string, string>("RunState.chronicle",
                "diagnostic log, not a game-visible quantity"),
            new KeyValuePair<string, string>("RunState.lambdaPoolRemainingAll",
                "undrawn pool size; the player cannot count the remaining deck"),
            new KeyValuePair<string, string>("RunState.lambdaPoolRemainingFloored",
                "same undrawn pool, floored variant"),
        };
    }

    /// <summary>Builds an <see cref="UltraObservation"/> from live game state.
    ///
    /// <para><b>Read-only by construction.</b> It calls no mutator and consumes no RNG, so
    /// capturing an observation cannot perturb the run it observes ── which is what lets the
    /// same live run be observed and then continued deterministically.</para></summary>
    public static class UltraObservationBuilder
    {
        public static UltraObservation Capture(
            GameLoop.RunState run, MapManager map, string phase, int epoch)
        {
            var observation = new UltraObservation { epoch = epoch, phase = phase ?? "" };
            if (run != null) FillRun(observation.run, run);
            if (map != null) FillMap(observation.map, map);
            return observation;
        }

        private static void FillRun(UltraRunView view, GameLoop.RunState run)
        {
            view.currentFloor = run.currentFloor;
            view.maxFloor = run.maxFloor;
            view.normalClearFloor = run.normalClearFloor;
            view.bossDefeatedThisFloor = run.bossDefeatedThisFloor;
            view.playerHP = run.playerHP;
            view.playerMaxHP = run.playerMaxHP;
            view.coins = run.coins;
            view.weaponMaterials = run.weaponMaterials;
            view.tilesThisFloor = run.tilesThisFloor;
            view.totalBattles = run.totalBattles;
            view.totalWins = run.totalWins;
            view.totalTurns = run.totalTurns;
            view.totalCombatTurns = run.totalCombatTurns;
            view.inLambda = run.inLambda;
            view.dimensionalDisturbance = run.dimensionalDisturbance;
            view.convictionStage = run.convictionStage;
            view.layer6Unlocked = run.layer6Unlocked;
            view.layer7Unlocked = run.layer7Unlocked;
            view.shopRobberyDone = run.shopRobberyDone;
            view.lastStandActive = run.lastStandActive;
            view.sublimationCount = run.sublimationCount;
            view.sealedRoleMask = run.sealedRoleMask;
            view.lingeringWoundLost = run.lingeringWoundLost;
            view.equippedWeaponId = run.equippedWeaponId ?? "";
            view.equippedDiceId = run.equippedDiceId ?? "";
            view.ownedPassiveItems = ToArray(run.ownedPassiveItems);
            view.ownedConsumables = ToArray(run.ownedConsumables);
            view.permanentDebuffs = ToArray(run.permanentDebuffs);
            SplitCounts(run.timedBuffs, out view.timedBuffKeys, out view.timedBuffTurns);
            SplitCounts(run.timedDebuffs, out view.timedDebuffKeys, out view.timedDebuffTurns);
            SplitCounts(run.lambdaDebuffs, out view.lambdaDebuffKeys, out view.lambdaDebuffLevels);

            var phenomena = new List<string>();
            if (run.activePhenomena != null)
                for (int i = 0; i < run.activePhenomena.Count; i++)
                    phenomena.Add(run.activePhenomena[i].ToString());
            view.activePhenomena = phenomena.ToArray();

            var parts = new List<string>();
            if (run.diceFaceParts != null)
                for (int i = 0; i < run.diceFaceParts.Count; i++)
                    parts.Add(GameLoop.DiceFaceParts.Id(run.diceFaceParts[i]));
            view.diceFacePartIds = parts.ToArray();
        }

        private static void FillMap(UltraMapView view, MapManager map)
        {
            FloorMap floor = map.CurrentMap;
            if (floor == null) return;

            var reachable = new HashSet<string>();
            List<MapNode> moves = map.GetAvailableMoves();
            if (moves != null)
                for (int i = 0; i < moves.Count; i++)
                    if (moves[i] != null) reachable.Add(moves[i].id);

            FillMapFrom(view, floor, map.CurrentNode?.id, reachable);
        }

        /// <summary>The reveal rule, separated from the <see cref="MapManager"/> singleton so
        /// it can be exercised directly. Keeping the rule reachable without Unity's component
        /// lifecycle is what makes it practical to test, and an untested reveal rule is how
        /// hidden state escapes.</summary>
        private static void FillMapFrom(
            UltraMapView view, FloorMap floor, string currentNodeId, HashSet<string> reachable)
        {
            if (view == null || floor == null) return;
            reachable = reachable ?? new HashSet<string>();

            view.floor = floor.floor;
            view.rowCount = floor.rowCount;
            view.bossNodeId = floor.bossNodeId ?? "";
            view.currentNodeId = currentNodeId ?? "";

            List<MapNode> all = floor.GetAllNodes();
            var views = new List<UltraMapNodeView>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                MapNode node = all[i];
                if (node == null) continue;
                views.Add(new UltraMapNodeView
                {
                    id = node.id,
                    row = node.row,
                    lane = node.lane,
                    // **The only place the reveal rule is applied.** Everything downstream
                    // reads `view`, so there is no second path that could forget the check.
                    view = node.revealed ? Classify(node.EffectiveType) : UltraTileView.Unknown,
                    revealed = node.revealed,
                    visited = node.visited,
                    reachableNow = reachable.Contains(node.id),
                    connections = node.connections != null
                        ? node.connections.ToArray() : new string[0],
                });
            }
            view.nodes = views.ToArray();
        }

        private static UltraTileView Classify(TileType type)
        {
            switch (type)
            {
                case TileType.Outpost: return UltraTileView.Outpost;
                case TileType.Battle: return UltraTileView.Battle;
                case TileType.EliteBattle: return UltraTileView.EliteBattle;
                case TileType.Rest: return UltraTileView.Rest;
                case TileType.Treasure: return UltraTileView.Treasure;
                case TileType.Shop: return UltraTileView.Shop;
                case TileType.Event: return UltraTileView.Event;
                case TileType.Mystery: return UltraTileView.Mystery;
                case TileType.Exchange: return UltraTileView.Exchange;
                case TileType.Trap: return UltraTileView.Trap;
                case TileType.Boss: return UltraTileView.Boss;
                case TileType.Gate: return UltraTileView.Gate;
                case TileType.LambdaRing: return UltraTileView.LambdaRing;
                case TileType.LambdaExit: return UltraTileView.LambdaExit;
                default: return UltraTileView.Other;
            }
        }

        private static string[] ToArray(ICollection<string> source)
        {
            if (source == null || source.Count == 0) return new string[0];
            var result = new string[source.Count];
            source.CopyTo(result, 0);
            return result;
        }

        private static void SplitCounts(
            Dictionary<string, int> source, out string[] keys, out int[] values)
        {
            if (source == null || source.Count == 0)
            {
                keys = new string[0];
                values = new int[0];
                return;
            }
            // Sorted so the same state always produces the same observation ── an unordered
            // dictionary walk would make otherwise-identical observations hash differently.
            var names = new List<string>(source.Keys);
            names.Sort(StringComparer.Ordinal);
            keys = names.ToArray();
            values = new int[names.Count];
            for (int i = 0; i < names.Count; i++) values[i] = source[names[i]];
        }
    }
}

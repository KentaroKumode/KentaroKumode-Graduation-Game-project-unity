using System;
using System.Collections.Generic;
using System.Text;
using CombatSystem;

namespace AutoTest.Ultra
{
    /// <summary>The combat board as the player sees it before wiring.
    ///
    /// <para><b>The telegraph is copied verbatim, and that is safe by design.</b> ADR-0009 柱3
    /// makes <see cref="MutualTurnTelegraph"/> a complete-information disclosure: every field in
    /// it is shown to the player before they wire, including the enemy's rolled dice for this
    /// turn and the Vesca relic draw. Nothing in it is future knowledge — the roll has already
    /// happened and is on screen.</para>
    ///
    /// <para>What is <b>not</b> here: the enemy's rolls for later turns, the RNG, enemy passive
    /// internals that are not telegraphed, and the contents of the item/event decks.</para></summary>
    [Serializable]
    public sealed class UltraCombatView
    {
        public int turn;
        public int playerHP;
        public int playerMaxHP;
        public int enemyHP;
        public int enemyMaxHP;
        public int charge;
        public int chargeMax;
        public string enemyId = "";
        public bool isBoss;

        /// <summary>This turn's dice, in board order.</summary>
        public int[] dice = new int[0];
        /// <summary>Face index each die landed on, for part tier rules. −1 when unknown.</summary>
        public int[] faceIndex = new int[0];
        /// <summary>Roles already spent this combat (one use each), as a RoleKind bitmask.</summary>
        public int usedRoleMask;
        /// <summary>Roles sealed for the whole run by T4-C〈凶運〉.</summary>
        public int sealedRoleMask;

        // ---- telegraph (public by ADR-0009 柱3) ----
        public int enemyAttackValue;
        public int escalationStage;
        public int nextThresholdTurn;
        public int[] enemyDice = new int[0];
        public int enemyDiceTotal;
        public int enemyShield;
        public float enemyShieldReflectRate;
        public float enemyDodgeChance;
        public bool enemyHalvesDamageThisTurn;
        public int playerAttackPenalty;
        public bool playerHighDiceCrushed;
        public bool playerBlockIgnored;
        public bool executeArmed;
        public int sealedTerminal;
        public int blazeStacks;
        public bool blazePenalizesBlock;
        public string[] drawnRelics = new string[0];

        public static UltraCombatView FromTelegraph(in MutualTurnTelegraph tele)
        {
            return new UltraCombatView
            {
                turn = tele.turn,
                enemyAttackValue = tele.enemyAttackValue,
                escalationStage = tele.escalationStage,
                nextThresholdTurn = tele.nextThresholdTurn,
                enemyDice = tele.enemyDice != null ? (int[])tele.enemyDice.Clone() : new int[0],
                enemyDiceTotal = tele.enemyDiceTotal,
                enemyShield = tele.enemyShield,
                enemyShieldReflectRate = tele.enemyShieldReflectRate,
                enemyDodgeChance = tele.enemyDodgeChance,
                enemyHalvesDamageThisTurn = tele.enemyHalvesDamageThisTurn,
                playerAttackPenalty = tele.playerAttackPenalty,
                playerHighDiceCrushed = tele.playerHighDiceCrushed,
                playerBlockIgnored = tele.playerBlockIgnored,
                executeArmed = tele.executeArmed,
                sealedTerminal = tele.sealedTerminal,
                blazeStacks = tele.blazeStacks,
                blazePenalizesBlock = tele.blazePenalizesBlock,
                drawnRelics = tele.drawnRelics != null
                    ? (string[])tele.drawnRelics.Clone() : new string[0],
            };
        }
    }

    /// <summary>Encodes the three combat decisions as action ids.
    ///
    /// <para>Ids are compact and order-independent so they survive the process boundary and
    /// compare by value. Wiring uses one digit per die (terminal index), which is both the
    /// natural encoding and short enough that a full legal set fits inside
    /// <see cref="UltraWorkerProtocol.MaxLegalActionCount"/>.</para></summary>
    public static class UltraCombatActions
    {
        /// <summary>"0123" style: one terminal digit per die, board order.</summary>
        public static string EncodeWiring(DiceTerminal[] wiring)
        {
            if (wiring == null || wiring.Length == 0) return "";
            var sb = new StringBuilder(wiring.Length);
            for (int i = 0; i < wiring.Length; i++) sb.Append((char)('0' + (int)wiring[i]));
            return sb.ToString();
        }

        public static bool TryDecodeWiring(string encoded, int diceCount, out DiceTerminal[] wiring)
        {
            wiring = null;
            if (string.IsNullOrEmpty(encoded) || encoded.Length != diceCount) return false;
            var result = new DiceTerminal[diceCount];
            for (int i = 0; i < diceCount; i++)
            {
                int value = encoded[i] - '0';
                if (value < 0 || value > (int)DiceTerminal.Special) return false;
                result[i] = (DiceTerminal)value;
            }
            wiring = result;
            return true;
        }

        /// <summary>Reroll choice as a die-index bitmask. 0 = decline to reroll, which is a
        /// real decision and must be offered explicitly rather than implied by silence.</summary>
        public static string EncodeReroll(int mask)
        {
            return mask.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static bool TryDecodeReroll(string encoded, int diceCount, out int[] indices)
        {
            indices = null;
            if (!int.TryParse(encoded, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int mask)) return false;
            if (mask < 0 || mask >= (1 << Math.Max(0, diceCount))) return false;

            var picked = new List<int>(diceCount);
            for (int i = 0; i < diceCount; i++) if ((mask & (1 << i)) != 0) picked.Add(i);
            indices = picked.ToArray();
            return true;
        }

        /// <summary>Role decision as a RoleKind bitmask of the roles to fire now. 0 = defer
        /// everything, which is a legitimate play: roles are once per combat, so holding one
        /// back for a better turn is a choice the search must be able to express.</summary>
        public static string EncodeRoles(int fireMask)
        {
            return fireMask.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static bool TryDecodeRoles(string encoded, IList<RoleKind> candidates,
                                          out List<RoleKind> fire)
        {
            fire = null;
            if (!int.TryParse(encoded, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int mask)) return false;
            if (mask < 0) return false;

            var result = new List<RoleKind>();
            if (candidates != null)
                for (int i = 0; i < candidates.Count; i++)
                {
                    int bit = 1 << (int)candidates[i];
                    if ((mask & bit) != 0) result.Add(candidates[i]);
                }
            // A mask naming a role that is not on offer is a rules disagreement, not a
            // harmless extra — refuse it rather than silently dropping the bit.
            int offered = 0;
            if (candidates != null)
                for (int i = 0; i < candidates.Count; i++) offered |= 1 << (int)candidates[i];
            if ((mask & ~offered) != 0) return false;

            fire = result;
            return true;
        }

        public static UltraLegalAction Wiring(DiceTerminal[] plan)
        {
            return UltraLegalAction.Of(UltraActionKind.CombatWiring, EncodeWiring(plan));
        }

        public static UltraLegalAction Reroll(int mask)
        {
            return UltraLegalAction.Of(UltraActionKind.CombatReroll, EncodeReroll(mask), -1,
                mask == 0 ? "振り直さない" : "");
        }

        public static UltraLegalAction Roles(int fireMask)
        {
            return UltraLegalAction.Of(UltraActionKind.CombatRole, EncodeRoles(fireMask), -1,
                fireMask == 0 ? "全て温存" : "");
        }
    }
}

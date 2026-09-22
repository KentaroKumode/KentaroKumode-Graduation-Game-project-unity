using System;
using System.Collections.Generic;
using CombatSystem;
using GameLoop;
using InventorySystem.PassiveSkills;
using MetaProgression.Relics;
using UnityEngine;

namespace MetaProgression.Achievements
{
    /// <summary>
    /// GUI-independent achievement tracker. Public Note* methods are the only gameplay hooks.
    /// AutoRunner already raises SuppressRelicGrant, which also suppresses achievements.
    /// </summary>
    public static class AchievementService
    {
        private sealed class RunProgress
        {
            public bool active, usedConsumable, purchased, rerolled, playerFled, relicEquipped;
            public int startMaxHp, damageTaken, combatRequestedHeal, hpOneStreak;
            public int shopPurchases, shopSales, shopRerolls;
            public ResolvedChallenge challenge;
            public bool cursedRelicEquipped;
            public readonly HashSet<string> escapedEnemyIds = new HashSet<string>();
        }

        private static RunProgress run;
        public static event Action<AchievementDefinition> OnUnlocked;
        private static bool Suppressed => MetaBuffApplicator.SuppressRelicGrant;

        public static bool IsUnlocked(string id)
        {
            var s = MetaProgressManager.Instance?.State;
            return s?.unlockedAchievementIds != null && s.unlockedAchievementIds.Contains(id);
        }

        public static bool Unlock(string id)
        {
            if (Suppressed) return false;
            var def = AchievementCatalog.Get(id);
            var mgr = MetaProgressManager.Instance;
            var s = mgr?.State;
            if (def == null || s == null) return false;
            if (s.unlockedAchievementIds == null) s.unlockedAchievementIds = new List<string>();
            if (s.achievementUnlockUtcTicks == null) s.achievementUnlockUtcTicks = new List<long>();
            if (s.unlockedAchievementIds.Contains(id)) return false;
            s.unlockedAchievementIds.Add(id);
            s.achievementUnlockUtcTicks.Add(DateTime.UtcNow.Ticks);
            mgr.Save();
            mgr.OnStateChanged?.Invoke();
            Debug.Log($"[実績解除] {def.displayName} ({def.id})");
            OnUnlocked?.Invoke(def);
            return true;
        }

        public static void BeginRun(RunState state)
        {
            if (Suppressed || state == null) { run = null; return; }
            var meta = MetaProgressManager.Instance?.State;
            var relic = meta?.EquippedRelic;
            run = new RunProgress
            {
                active = true,
                startMaxHp = Math.Max(1, state.playerMaxHP),
                relicEquipped = relic != null,
                cursedRelicEquipped = relic != null && relic.Curse != RelicCurse.None,
                challenge = meta?.challenge != null ? ChallengeResolver.Build(meta.challenge.Clone()) : ResolvedChallenge.None,
            };
            Unlock(AchievementCatalog.FirstRun);
        }

        public static void NoteFloorEntered(int floor)
        {
            if (!Ready) return;
            if (floor >= 3) Unlock(AchievementCatalog.Reach3);
            if (floor >= 6) Unlock(AchievementCatalog.Reach6);
            if (floor >= 7) Unlock(AchievementCatalog.Reach7);
        }

        public static void NoteCombatStarted()
        {
            if (!Ready) return;
            run.combatRequestedHeal = 0;
            run.hpOneStreak = 0;
        }

        public static void NoteTurnEnded(TurnResult turn)
        {
            if (!Ready) return;
            run.hpOneStreak = turn.playerHPAfter == 1 ? run.hpOneStreak + 1 : 0;
        }

        public static void NoteHealRequested(int amount)
        {
            if (!Ready || amount <= 0) return;
            run.combatRequestedHeal += amount;
            run.hpOneStreak = 0;
        }

        public static void NoteReroll()
        {
            if (Ready) run.rerolled = true;
        }

        public static void NoteRoleFired(RoleKind role)
        {
            if (!Ready) return;
            var mgr = MetaProgressManager.Instance;
            if (mgr?.State == null) return;
            int bit = 1 << (int)role;
            if ((mgr.State.achievementRoleMask & bit) == 0)
            {
                mgr.State.achievementRoleMask |= bit;
                mgr.Save();
            }
            Unlock(AchievementCatalog.FirstRole);
            int allMask = 0;
            foreach (RoleKind k in Enum.GetValues(typeof(RoleKind))) allMask |= 1 << (int)k;
            if ((mgr.State.achievementRoleMask & allMask) == allMask) Unlock(AchievementCatalog.AllRoles);
        }

        public static void NoteConsumableUsed() { if (Ready) run.usedConsumable = true; }
        public static void NoteExternalHpDamage(int amount) { if (Ready && amount > 0) run.damageTaken += amount; }
        public static void NoteRelicEquipped() { if (Ready) run.relicEquipped = true; }
        public static void NotePlayerFled() { if (Ready) run.playerFled = true; }
        public static void NoteEnemyEscaped(string enemyId) { if (Ready && !string.IsNullOrEmpty(enemyId)) run.escapedEnemyIds.Add(enemyId); }

        public static void BeginShop()
        {
            if (!Ready) return;
            run.shopPurchases = run.shopSales = run.shopRerolls = 0;
        }

        public static void NoteShopPurchase(int coinsAfter)
        {
            if (!Ready) return;
            run.purchased = true;
            run.shopPurchases++;
            if (coinsAfter == 0) Unlock(AchievementCatalog.HiddenExactZeroPurchase);
            CheckStubbornShop();
        }

        public static void NoteShopSale()
        {
            if (!Ready) return;
            run.shopSales++;
            CheckStubbornShop();
        }

        public static void NoteShopReroll()
        {
            if (!Ready) return;
            run.rerolled = true;
            run.shopRerolls++;
            CheckStubbornShop();
        }

        private static void CheckStubbornShop()
        {
            if (run.shopPurchases >= 3 && run.shopSales >= 3 && run.shopRerolls >= 3)
                Unlock(AchievementCatalog.HiddenShopStubborn);
        }

        public static void NoteCombatEnded(RunState state, CombatResult result, CombatContext context)
        {
            if (!Ready || state == null) return;
            run.damageTaken += Math.Max(0, result.damageTaken);
            if (!result.playerWon) return;

            var mgr = MetaProgressManager.Instance;
            if (mgr?.State != null)
            {
                mgr.State.achievementCombatWins++;
                if (mgr.State.achievementCombatWins >= 100) Unlock(AchievementCatalog.CombatWins100);
                else mgr.Save();
            }

            if (result.playerHPRemaining == 1) Unlock(AchievementCatalog.WinAt1Hp);
            if (run.hpOneStreak >= 3) Unlock(AchievementCatalog.HiddenCalmDown);
            if (run.combatRequestedHeal >= Math.Max(1, state.playerMaxHP) * 3)
                Unlock(AchievementCatalog.HiddenPerpetualMotion);
            if (run.escapedEnemyIds.Contains(result.enemyId)) Unlock(AchievementCatalog.HiddenReturningEnemy);
            CheckOverkill(result);
            CheckThirteenthEnd(state, result);

            bool boss = !string.IsNullOrEmpty(result.enemyId) && result.enemyId.StartsWith("boss_layer", StringComparison.Ordinal);
            if (!boss) return;
            int floor = state.currentFloor;
            if (floor == 1) Unlock(AchievementCatalog.Boss1);
            if (floor == 5)
            {
                Unlock(AchievementCatalog.Boss5);
                if (!run.usedConsumable) Unlock(AchievementCatalog.NoConsumableBoss5);
                if (!run.playerFled) Unlock(AchievementCatalog.NoFleeBoss5);
            }
            if (result.damageTaken == 0) Unlock(AchievementCatalog.BossNoHpDamage);
            if (floor >= 3 && result.totalTurns <= 1) Unlock(AchievementCatalog.BossOneTurn);
            if (floor == 5 && result.enemyId == "boss_layer5_hidden") Unlock(AchievementCatalog.HiddenSaintGeorges);
            if (floor == 6)
            {
                if (!run.purchased) Unlock(AchievementCatalog.NoPurchaseBoss6);
                if (!run.rerolled) Unlock(AchievementCatalog.NoRerollBoss6);
                if (state.coins == 0) Unlock(AchievementCatalog.ZeroGoldBoss6);
                float blaze = context != null ? context.GetAccumulated(InventorySystem.PassiveSkills.Effects.BlazeBrand.StackKey) : 0f;
                if (blaze <= 0f) Unlock(AchievementCatalog.HiddenNoBlazeBoss6);
            }
            if (state.totalCombatTurns == 124) Unlock(AchievementCatalog.HiddenTurn124);
        }

        private static void CheckOverkill(CombatResult result)
        {
            if (result.turnLog == null || result.turnLog.Count == 0) return;
            var t = result.turnLog[result.turnLog.Count - 1];
            int dealt = Math.Max(0, t.totalDamage) + Math.Max(0, t.fixedDamage);
            int hpBefore = t.enemyHPAfter + dealt;
            if (t.enemyHPAfter <= 0 && hpBefore == 1 && dealt >= 100) Unlock(AchievementCatalog.HiddenOverkill);
        }

        private static void CheckThirteenthEnd(RunState state, CombatResult result)
        {
            if (state.totalCombatTurns != 13 || result.turnLog == null || result.turnLog.Count == 0) return;
            var t = result.turnLog[result.turnLog.Count - 1];
            int dealt = Math.Max(0, t.totalDamage) + Math.Max(0, t.fixedDamage);
            if (t.enemyHPAfter <= 0 && dealt == 13) Unlock(AchievementCatalog.HiddenThirteenthEnd);
        }

        public static void NoteFinalPageOpened() { if (Ready) Unlock(AchievementCatalog.HiddenFinalPage); }

        public static void NoteRelicAcquired(RolledRelic relic)
        {
            if (Suppressed || relic == null || !relic.IsValid()) return;
            Unlock(AchievementCatalog.FirstRelic);
            bool mainMax = relic.steps != null && relic.steps.Count > 0 && relic.steps[0] >= RelicAxisCatalog.MainStepMax;
            bool subMax = false, allMax = relic.steps != null && relic.steps.Count >= 3 && relic.steps[0] == RelicAxisCatalog.MainStepMax;
            if (relic.steps != null)
            {
                for (int i = 1; i < relic.steps.Count; i++)
                {
                    if (relic.steps[i] >= RelicAxisCatalog.SubStepMax) subMax = true;
                    if (relic.steps[i] != RelicAxisCatalog.SubStepMax) allMax = false;
                }
            }
            if (mainMax) Unlock(AchievementCatalog.RelicMainMax);
            if (subMax) Unlock(AchievementCatalog.RelicSubMax);
            if (allMax) Unlock(AchievementCatalog.RelicAllMax);
        }

        public static void CompleteRun(RunState state)
        {
            if (!Ready || state == null || state.playerHP <= 0) return;
            if (run.damageTaken > run.startMaxHp * 10) Unlock(AchievementCatalog.HiddenTheseus);
            if (state.currentFloor >= 7 && state.bossDefeatedThisFloor)
            {
                Unlock(AchievementCatalog.Clear7);
                if (!run.relicEquipped && !run.usedConsumable) Unlock(AchievementCatalog.NoRelicConsumableClear7);
                if (run.cursedRelicEquipped) Unlock(AchievementCatalog.CursedRelicClear7);
                UnlockChallengeAchievements();
                UnlockCategoryAchievements();
            }
            run.active = false;
        }

        private static void UnlockChallengeAchievements()
        {
            int score = run.challenge?.Score ?? 0;
            if (score >= 10) Unlock(AchievementCatalog.Challenge10);
            if (score >= 20) Unlock(AchievementCatalog.Challenge20);
            if (score >= 30) Unlock(AchievementCatalog.Challenge30);
            if (score >= 40) Unlock(AchievementCatalog.Challenge40);
            if (score == ChallengeCatalog.TotalMaxScore) Unlock(AchievementCatalog.Challenge50);
        }

        private static void UnlockCategoryAchievements()
        {
            string[] ids = { AchievementCatalog.CategoryAMax, AchievementCatalog.CategoryBMax, AchievementCatalog.CategoryCMax,
                             AchievementCatalog.CategoryDMax, AchievementCatalog.CategoryEMax };
            for (int c = 0; c < ids.Length; c++)
            {
                var cat = (ChallengeCategory)c;
                bool ok = true;
                for (int i = 0; i < ChallengeCatalog.Axes.Count; i++)
                {
                    var a = ChallengeCatalog.Axes[i];
                    int expected = a.category == cat ? a.MaxTier : 0;
                    if (run.challenge.Tier(a.axis) != expected) { ok = false; break; }
                }
                for (int i = 0; ok && i < ChallengeCatalog.T4s.Count; i++)
                {
                    var t4 = ChallengeCatalog.T4s[i];
                    if (run.challenge.Has(t4.t4) != (t4.category == cat)) ok = false;
                }
                if (ok) Unlock(ids[c]);
            }
        }

        private static bool Ready => !Suppressed && run != null && run.active;
    }
}

using System;
using System.Collections.Generic;
using CombatSystem;
using GameLoop;
using ItemSystem.PassiveSkills;
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
            public int difficulty;
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
            var relics = meta != null ? meta.EquippedRelics() : null;
            bool fullSet = false;
            for (int s = 0; s < RelicCatalog.SetCount; s++) if (RelicApplicator.Has3((RelicSet)s)) fullSet = true;
            run = new RunProgress
            {
                active = true,
                startMaxHp = Math.Max(1, state.playerMaxHP),
                relicEquipped = relics != null && relics.Count > 0,
                cursedRelicEquipped = fullSet,   // 2026-11-10: 刻印の廃止に伴い「同じセットを 3 個持ち込んだ」に読み替え (id は据え置き)
                difficulty = state.challengeDifficulty,
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
            int floor = state.Depth;
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
                float blaze = context != null ? context.GetAccumulated(ItemSystem.PassiveSkills.Effects.BlazeBrand.StackKey) : 0f;
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

        /// <summary>遺物を入手した・強化した時に呼ぶ (§15-5・2026-11-10)。 主ステ最大 = ★4 / サブ最大 = どれかのサブが 2 段 / 全部最大 = ★4・サブ 3 個がすべて 2 段。</summary>
        public static void NoteRelicAcquired(Relic relic)
        {
            if (Suppressed || relic == null || !relic.IsValid()) return;
            Unlock(AchievementCatalog.FirstRelic);
            bool mainMax = relic.stars >= RelicCatalog.MaxStars;
            bool subMax = false, allMax = mainMax && relic.SubCount >= RelicCatalog.MaxSubs;
            for (int i = 0; i < relic.SubCount; i++)
            {
                if (relic.subSteps[i] >= RelicCatalog.SubStepMax) subMax = true;
                else allMax = false;
            }
            if (mainMax) Unlock(AchievementCatalog.RelicMainMax);
            if (subMax) Unlock(AchievementCatalog.RelicSubMax);
            if (allMax) Unlock(AchievementCatalog.RelicAllMax);
        }

        public static void CompleteRun(RunState state)
        {
            if (!Ready || state == null || state.playerHP <= 0) return;
            if (run.damageTaken > run.startMaxHp * 10) Unlock(AchievementCatalog.HiddenTheseus);
            if (state.Depth >= 7 && state.bossDefeatedThisFloor)
            {
                Unlock(AchievementCatalog.Clear7);
                if (!run.relicEquipped && !run.usedConsumable) Unlock(AchievementCatalog.NoRelicConsumableClear7);
                if (run.cursedRelicEquipped) Unlock(AchievementCatalog.CursedRelicClear7);
                UnlockChallengeAchievements();
                UnlockCategoryAchievements(state);
            }
            run.active = false;
        }

        /// <summary>挑戦の実績 (2026-11-10: 旧配点の点数 10〜50 から、 難易度 2・4・6・8・10 へ読み替え・id は据え置き)。</summary>
        private static void UnlockChallengeAchievements()
        {
            int d = run.difficulty;
            if (d >= 2) Unlock(AchievementCatalog.Challenge10);
            if (d >= 4) Unlock(AchievementCatalog.Challenge20);
            if (d >= 6) Unlock(AchievementCatalog.Challenge30);
            if (d >= 8) Unlock(AchievementCatalog.Challenge40);
            if (d >= ChallengeCards.MaxDifficulty) Unlock(AchievementCatalog.Challenge50);
        }

        /// <summary>系統の実績 (2026-11-10: 旧「カテゴリを最高 Tier で揃える」から、 受けたデバフの系統へ読み替え・id と名前は据え置き)。
        /// A 耐久・B 攻撃・C 手を 3 枚以上 ／ D 経済を 2 枚 (経済の札は 2 種しかない) ／ E 七つの大罪を受けてクリア。</summary>
        private static void UnlockCategoryAchievements(RunState state)
        {
            int dur = 0, atk = 0, hand = 0, eco = 0; bool sin = false;
            if (state.challengePicks != null)
                foreach (var p in state.challengePicks)
                {
                    ChallengeCards.Split(p, out var deb, out _);
                    if (deb == null) continue;
                    if (ChallengeCards.IsSin(deb.kind)) sin = true;
                    switch (deb.family)
                    {
                        case ChallengeCards.Family.Durability: dur++; break;
                        case ChallengeCards.Family.Attack: atk++; break;
                        case ChallengeCards.Family.Hand: hand++; break;
                        case ChallengeCards.Family.Economy: eco++; break;
                    }
                }
            if (dur >= 3) Unlock(AchievementCatalog.CategoryAMax);
            if (atk >= 3) Unlock(AchievementCatalog.CategoryBMax);
            if (hand >= 3) Unlock(AchievementCatalog.CategoryCMax);
            if (eco >= 2) Unlock(AchievementCatalog.CategoryDMax);
            if (sin) Unlock(AchievementCatalog.CategoryEMax);
        }

        private static bool Ready => !Suppressed && run != null && run.active;

        // ============================================================
        //  中断セーブ (issue #84)
        // ============================================================

        /// <summary>ラン中の実績の進み具合の写し (JsonUtility で書ける形)。 「消耗品を使わずに」などの条件が、 中断を挟んでも続くように。</summary>
        [Serializable]
        public sealed class RunProgressState
        {
            public bool present;
            public bool active, usedConsumable, purchased, rerolled, playerFled, relicEquipped, cursedRelicEquipped;
            public int startMaxHp, damageTaken, combatRequestedHeal, hpOneStreak, shopPurchases, shopSales, shopRerolls, difficulty;
            public List<string> escapedEnemyIds = new List<string>();
        }

        public static RunProgressState CaptureRun()
        {
            var s = new RunProgressState();
            if (run == null) return s;
            s.present = true;
            s.active = run.active; s.usedConsumable = run.usedConsumable; s.purchased = run.purchased; s.rerolled = run.rerolled;
            s.playerFled = run.playerFled; s.relicEquipped = run.relicEquipped; s.cursedRelicEquipped = run.cursedRelicEquipped;
            s.startMaxHp = run.startMaxHp; s.damageTaken = run.damageTaken; s.combatRequestedHeal = run.combatRequestedHeal;
            s.hpOneStreak = run.hpOneStreak; s.shopPurchases = run.shopPurchases; s.shopSales = run.shopSales;
            s.shopRerolls = run.shopRerolls; s.difficulty = run.difficulty;
            s.escapedEnemyIds.AddRange(run.escapedEnemyIds);
            s.escapedEnemyIds.Sort(StringComparer.Ordinal);
            return s;
        }

        /// <summary>写しを戻す。 <see cref="BeginRun"/> の代わりに呼ぶ (再開では「初めてのラン」の実績を鳴らし直さない)。</summary>
        public static void RestoreRun(RunProgressState s)
        {
            if (Suppressed || s == null || !s.present) { run = null; return; }
            run = new RunProgress
            {
                active = s.active, usedConsumable = s.usedConsumable, purchased = s.purchased, rerolled = s.rerolled,
                playerFled = s.playerFled, relicEquipped = s.relicEquipped, cursedRelicEquipped = s.cursedRelicEquipped,
                startMaxHp = s.startMaxHp, damageTaken = s.damageTaken, combatRequestedHeal = s.combatRequestedHeal,
                hpOneStreak = s.hpOneStreak, shopPurchases = s.shopPurchases, shopSales = s.shopSales,
                shopRerolls = s.shopRerolls, difficulty = s.difficulty,
            };
            if (s.escapedEnemyIds != null) foreach (var id in s.escapedEnemyIds) run.escapedEnemyIds.Add(id);
        }
    }
}

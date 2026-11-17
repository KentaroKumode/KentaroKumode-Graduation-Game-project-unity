using System;
using System.Collections.Generic;
using CombatSystem;
using GameLoop;
using ItemSystem.PassiveSkills;
using MetaProgression.Relics;
using UnityEngine;
using Cat = MetaProgression.Achievements.AchievementCatalog;

namespace MetaProgression.Achievements
{
    /// <summary>
    /// 実績の判定と解除 (画面に依存しない)。 遊びの処理から呼ぶ口は Note* / BeginRun / CompleteRun だけで、 どれもゲームの値を<b>読むだけ</b> (戦闘・経済・乱数を変えない)。
    /// 一覧の正本は <see cref="AchievementCatalog"/>・仕様は docs/GAME.md §15-6。
    ///
    /// <para><b>層</b>: 文は画面の層の番号、 判定は <see cref="RunState.Depth"/> (<see cref="Depth"/> = <see cref="MapSystem.FreeMove.FloorPlan.DepthOf"/>)。</para>
    /// <para><b>保存</b>: ラン単位の数は <see cref="RunProgress"/> (中断セーブの写しは <see cref="RunProgressState"/>)。 <see cref="RunState"/> には足さない ── 中断セーブの指紋が変わる。
    /// ランを跨ぐ数は <see cref="MetaProgressState"/> の achievement*。</para>
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
        /// <summary>解除した時に鳴る (画面のお知らせ用)。 BOT が動かしている間は解除しないので鳴らない。</summary>
        public static event Action<AchievementDefinition> OnUnlocked;
        /// <summary>解除しない間: 遺物の付与を止めた測定 (<see cref="MetaBuffApplicator.SuppressRelicGrant"/>) と、 BOT がランを動かしている間 (<see cref="GameHooks.BotDriving"/>・2026-11-17)。
        /// 記録 (codex・<c>CodexRecorder</c>) と同じく、 実績は人が遊んだ時だけ解除する。 2026-11-17 まではふつうのバッチでも解除し、 <see cref="OnUnlocked"/> も鳴っていた。
        /// <b>Note* を足す時は必ず <see cref="Ready"/> か Suppressed を通す。</b></summary>
        private static bool Suppressed => MetaBuffApplicator.SuppressRelicGrant || GameHooks.BotDriving;

        private static bool Ready => !Suppressed && run != null && run.active;

        /// <summary>画面の層 n (0〜5) の深さ (判定は深さで行う)。</summary>
        private static int Depth(int layer) => MapSystem.FreeMove.FloorPlan.DepthOf(layer);

        private static MetaProgressState Meta => MetaProgressManager.Instance?.State;

        // ============================================================
        //  画面 (ギャラリー) が読む口
        // ============================================================

        /// <summary>解除済みか。</summary>
        public static bool IsUnlocked(string id)
        {
            var s = Meta;
            return s?.unlockedAchievementIds != null && s.unlockedAchievementIds.Contains(id);
        }

        /// <summary>解除した時刻 (UTC)。 解除していなければ null。</summary>
        public static DateTime? UnlockedAtUtc(string id)
        {
            var s = Meta;
            if (s?.unlockedAchievementIds == null || s.achievementUnlockUtcTicks == null) return null;
            int i = s.unlockedAchievementIds.IndexOf(id);
            if (i < 0 || i >= s.achievementUnlockUtcTicks.Count) return null;
            return new DateTime(s.achievementUnlockUtcTicks[i], DateTimeKind.Utc);
        }

        /// <summary>画面で伏せる (「？？？」にする) か: 隠しで、 まだ取っていない。</summary>
        public static bool IsMasked(AchievementDefinition def) => def != null && def.hidden && !IsUnlocked(def.id);

        /// <summary>伏せ字。</summary>
        public const string MaskText = "？？？";

        /// <summary>画面に出す名前 (伏せる物は「？？？」)。</summary>
        public static string ShownName(AchievementDefinition def) => def == null ? "" : IsMasked(def) ? MaskText : def.displayName;

        /// <summary>画面に出す条件の文 (伏せる物は「？？？」)。</summary>
        public static string ShownDescription(AchievementDefinition def) => def == null ? "" : IsMasked(def) ? MaskText : def.description;

        /// <summary>解除済みの数 (一覧にある物だけ数える)。</summary>
        public static int UnlockedCount()
        {
            int n = 0;
            foreach (var d in AchievementCatalog.All) if (IsUnlocked(d.id)) n++;
            return n;
        }

        // ============================================================
        //  解除
        // ============================================================

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

        /// <summary>ランを跨ぐ一覧に足す (足したら保存)。 足したら true。</summary>
        private static bool AddMeta(List<string> list, string v)
        {
            if (list == null || string.IsNullOrEmpty(v) || list.Contains(v)) return false;
            list.Add(v);
            MetaProgressManager.Instance?.Save();
            return true;
        }

        // ============================================================
        //  ラン
        // ============================================================

        public static void BeginRun(RunState state)
        {
            if (Suppressed || state == null) { run = null; return; }
            run = new RunProgress
            {
                active = true,
                startMaxHp = Math.Max(1, state.playerMaxHP),
                difficulty = state.challengeDifficulty,
            };
            ReadRelicLoadout(out run.relicEquipped, out run.cursedRelicEquipped);
            Unlock(Cat.FirstRun);
        }

        /// <summary>今の持ち込みの遺物: 1 個でも持ち込んでいるか / 同じセットを 3 個持ち込んでいるか。</summary>
        private static void ReadRelicLoadout(out bool any, out bool fullSet)
        {
            var meta = Meta;
            var relics = meta != null ? meta.EquippedRelics() : null;
            any = relics != null && relics.Count > 0;
            fullSet = false;
            for (int s = 0; s < RelicCatalog.SetCount; s++) if (RelicApplicator.Has3((RelicSet)s)) fullSet = true;
        }

        public static void NoteFloorEntered(int depth)
        {
            if (!Ready) return;
            if (depth >= Depth(4)) Unlock(Cat.Reach6);
            if (depth >= Depth(5)) Unlock(Cat.Reach7);
        }

        // ============================================================
        //  戦闘
        // ============================================================

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
            if (turn.isCritical && turn.totalDamage >= Cat.CritGoal) Unlock(Cat.Crit100);
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
            Unlock(Cat.FirstRole);
            if (YachtRoles.IsTriple(role)) Unlock(Cat.RoleZoro);
            int allMask = 0;
            foreach (RoleKind k in YachtRoles.All) allMask |= 1 << (int)k;
            if ((mgr.State.achievementRoleMask & allMask) == allMask) Unlock(Cat.AllRoles);
        }

        public static void NoteConsumableUsed() { if (Ready) run.usedConsumable = true; }
        public static void NoteExternalHpDamage(int amount) { if (Ready && amount > 0) run.damageTaken += amount; }
        /// <summary>自分が戦闘から逃げた (GameManager.HandleFledBattle)。 <paramref name="enemyId"/> = 逃げた相手 (昨日の敵は今日も敵)。
        /// 2026-11-17 まで「敵が逃げた」(偽の商人・旧挑戦デバフと一緒に廃止) を数えていて、 取れなかった。</summary>
        public static void NotePlayerFled(string enemyId)
        {
            if (!Ready) return;
            run.playerFled = true;
            if (!string.IsNullOrEmpty(enemyId)) run.escapedEnemyIds.Add(enemyId);
        }

        public static void NoteCombatEnded(RunState state, CombatResult result, CombatContext context)
        {
            if (!Ready || state == null) return;
            // 受けたダメージは戦闘の開始時の HP を基準に数える (damageTaken は最大 HP 基準で、 減った状態で始めた戦闘を水増しする)
            run.damageTaken += result.HpLostThisCombat;
            if (!result.playerWon) return;

            var mgr = MetaProgressManager.Instance;
            if (mgr?.State != null)
            {
                mgr.State.achievementCombatWins++;
                if (mgr.State.achievementCombatWins >= Cat.CombatWinsGoal) Unlock(Cat.CombatWins100);
                else mgr.Save();
            }

            if (result.playerHPRemaining == 1) Unlock(Cat.WinAt1Hp);
            if (run.hpOneStreak >= Cat.CalmDownTurns) Unlock(Cat.HiddenCalmDown);
            if (run.combatRequestedHeal >= Math.Max(1, state.playerMaxHP) * Cat.PerpetualHealTimes)
                Unlock(Cat.HiddenPerpetualMotion);
            if (run.escapedEnemyIds.Contains(result.enemyId)) Unlock(Cat.HiddenReturningEnemy);
            if (state.terminalDeck != null && state.terminalDeck.Count <= TerminalCards.MinDeckSize) Unlock(Cat.DeckMin);
            CheckOverkill(result);
            CheckThirteenthEnd(state, result);

            if (!BossIds.IsBoss(result.enemyId)) return;
            int depth = state.Depth;
            if (result.enemyId == BossIds.FarmBoss)
            {
                if (result.totalTurns >= Cat.CrateSlowTurns) Unlock(Cat.CrateSlow);
                return;   // 0 層の古い宝箱はほかのボスの実績に数えない
            }
            if (depth == Depth(1)) Unlock(Cat.Boss1);
            if (depth == Depth(2))
            {
                Unlock(Cat.Boss3);
                if (mgr?.State != null)
                {
                    if (mgr.State.achievementLayer2Bosses == null) mgr.State.achievementLayer2Bosses = new List<string>();
                    AddMeta(mgr.State.achievementLayer2Bosses, result.enemyId);
                    if (mgr.State.achievementLayer2Bosses.Contains(BossIds.Layer3Marsh)
                        && mgr.State.achievementLayer2Bosses.Contains(BossIds.Layer3Goblin)
                        && mgr.State.achievementLayer2Bosses.Contains(BossIds.Layer3Mirror))
                        Unlock(Cat.Boss3All);
                }
            }
            if (depth == Depth(3)) CheckLayer3Boss(state);
            // 傷一つなく: 開始時の HP を基準にする (0 層の宝箱は上で外した・2026-11-17 までは攻撃 0 の宝箱で初回ランに勝手に取れていた)
            if (depth >= Depth(2) && result.HpLostThisCombat == 0) Unlock(Cat.BossNoHpDamage);
            if (depth >= Depth(2) && result.totalTurns <= 1) Unlock(Cat.BossOneTurn);
            if (result.enemyId == BossIds.Layer6Ash)
            {
                // 灰は灰へ: 戦闘を通して積もった烈炎の累計 (GainedKey) を見る。 今の数 (StackKey) は〈業火〉で 0 に戻るので、 積ませても取れていた (2026-11-17 まで)
                float blaze = context != null ? context.GetAccumulated(ItemSystem.PassiveSkills.Effects.BlazeBrand.GainedKey) : 1f;
                if (blaze <= 0f) Unlock(Cat.HiddenNoBlazeBoss6);
                if (run.difficulty >= ChallengeCards.MaxDifficulty) Unlock(Cat.ChallengeKingD10);
            }
            if (BossIds.VescaStageOf(result.enemyId) > 0)
            {
                Unlock(Cat.Clear7);
                if (VescaClock.Held(state)) Unlock(Cat.VescaClock);
                if (run.difficulty >= ChallengeCards.MaxDifficulty) Unlock(Cat.ChallengeVescaD10);
            }
            // あと一ターンだけ: 〈最後の審判〉の刻限の 1 ターン前 (ラン通算の戦闘ターン・0 層は数えない) にボスを倒す
            int deadline = MetaDebuffApplicator.GetJudgmentTurnThreshold();
            if (deadline > 0 && state.totalCombatTurns == deadline - 1) Unlock(Cat.HiddenTurn124);
        }

        /// <summary>3 層 (深さ 5) のボスを倒した: 挑戦の難易度・職業・縛り・揃いの至宝・七つの重荷。</summary>
        private static void CheckLayer3Boss(RunState state)
        {
            int d = run.difficulty;
            if (d >= Cat.DifficultyLow) Unlock(Cat.ChallengeD3);
            if (d >= Cat.DifficultyMid) Unlock(Cat.ChallengeD6);
            if (d >= ChallengeCards.MaxDifficulty) Unlock(Cat.ChallengeD10);

            switch (state.playerClass)
            {
                case ClassType.Swordsman: Unlock(Cat.ClassSword); break;
                case ClassType.Knight: Unlock(Cat.ClassKnight); break;
                case ClassType.Berserker: Unlock(Cat.ClassBerserker); break;
                case ClassType.Assassin: Unlock(Cat.ClassAssassin); break;
            }

            if (!run.usedConsumable) Unlock(Cat.NoConsumableBoss5);
            if (!run.playerFled) Unlock(Cat.NoFleeBoss5);
            if (!run.purchased) Unlock(Cat.NoPurchaseBoss6);
            if (!run.rerolled) Unlock(Cat.NoRerollBoss6);
            if (state.coins == 0) Unlock(Cat.ZeroGoldBoss6);
            if (!run.relicEquipped && !run.usedConsumable) Unlock(Cat.NoRelicConsumableClear7);
            if (run.cursedRelicEquipped) Unlock(Cat.CursedRelicClear7);

            // 七つの重荷: 背負っている「大罪:○○」の種類を通算で数える。
            //   注意: 大罪は IV の札で 5 層の入口でしか配られないので、 今の配り方ではここで背負っていない (docs/GAME.md §23-31・作者の判断待ち)
            var meta = Meta;
            if (meta != null && state.challengePicks != null)
            {
                if (meta.achievementSinsCarried == null) meta.achievementSinsCarried = new List<string>();
                foreach (var p in state.challengePicks)
                {
                    ChallengeCards.Split(p, out var deb, out _);
                    if (deb != null && ChallengeCards.IsSin(deb.kind)) AddMeta(meta.achievementSinsCarried, deb.kind);
                }
                bool all = true;
                foreach (var kind in SinKinds()) if (!meta.achievementSinsCarried.Contains(kind)) { all = false; break; }
                if (all) Unlock(Cat.SinsAll);
            }
        }

        /// <summary>大罪の種類 (挑戦カードの一覧から引く・7 種)。</summary>
        private static List<string> SinKinds()
        {
            var l = new List<string>();
            foreach (var c in ChallengeCards.All)
                if (!c.isBuff && !c.pending && ChallengeCards.IsSin(c.kind) && !l.Contains(c.kind)) l.Add(c.kind);
            return l;
        }

        /// <summary>やりすぎ: 攻撃の前の敵の HP (<see cref="TurnResult.enemyHPBeforeAttack"/>) が 1 で、 そのターンの主攻撃 + 固定ダメージが 100 以上で倒した。
        /// 2026-11-17 までは「倒した後の HP + 与ダメ」で攻撃前を戻していたが、 敵の HP は 0 で止まるので「残り 1」と「100 以上」が同時に立たなかった。</summary>
        private static void CheckOverkill(CombatResult result)
        {
            if (result.turnLog == null || result.turnLog.Count == 0) return;
            var t = result.turnLog[result.turnLog.Count - 1];
            int dealt = Math.Max(0, t.totalDamage) + Math.Max(0, t.fixedDamage);
            if (t.enemyHPAfter <= 0 && t.enemyHPBeforeAttack == 1 && dealt >= Cat.OverkillGoal) Unlock(Cat.HiddenOverkill);
        }

        private static void CheckThirteenthEnd(RunState state, CombatResult result)
        {
            if (state.totalCombatTurns != Cat.ThirteenTurn || result.turnLog == null || result.turnLog.Count == 0) return;
            var t = result.turnLog[result.turnLog.Count - 1];
            int dealt = Math.Max(0, t.totalDamage) + Math.Max(0, t.fixedDamage);
            if (t.enemyHPAfter <= 0 && dealt == Cat.ThirteenDamage) Unlock(Cat.HiddenThirteenthEnd);
        }

        // ============================================================
        //  店
        // ============================================================

        public static void BeginShop()
        {
            if (!Ready) return;
            run.shopPurchases = run.shopSales = run.shopRerolls = 0;
        }

        /// <summary>店で買った (棚の品・強化素材・物資の包・囮罠・端子)。
        /// <paramref name="countsAsItem"/> = 品 (秘宝・武器・消耗品・囮罠) ── 清貧はこれだけ数える (物資の包・強化素材・端子・出目パーツは数えない・作者 2026-11-17)。
        /// <paramref name="surcharged"/> = 値下げ交渉の後の割増が掛かった店で買った (面の皮)。 しつこい交渉・買い物上手は全部の買い物を数える。</summary>
        public static void NoteShopPurchase(int coinsAfter, bool countsAsItem, bool surcharged)
        {
            if (!Ready) return;
            if (countsAsItem) run.purchased = true;
            run.shopPurchases++;
            if (coinsAfter == 0) Unlock(Cat.HiddenExactZeroPurchase);
            if (surcharged) Unlock(Cat.RobberyRebuy);
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
            int n = Cat.StubbornCount;
            if (run.shopPurchases >= n && run.shopSales >= n && run.shopRerolls >= n)
                Unlock(Cat.HiddenShopStubborn);
        }

        // ============================================================
        //  遺物 (ランの外)
        // ============================================================

        /// <summary>遺物を入手した・強化した時に呼ぶ (§15-5・2026-11-10)。 主ステ最大 = ★4 / サブ最大 = どれかのサブが 2 段 / 全部最大 = ★4・サブ 3 個がすべて 2 段。</summary>
        public static void NoteRelicAcquired(Relic relic)
        {
            if (Suppressed || relic == null || !relic.IsValid()) return;
            Unlock(Cat.FirstRelic);
            bool mainMax = relic.stars >= RelicCatalog.MaxStars;
            bool subMax = false, allMax = mainMax && relic.SubCount >= RelicCatalog.MaxSubs;
            for (int i = 0; i < relic.SubCount; i++)
            {
                if (relic.subSteps[i] >= RelicCatalog.SubStepMax) subMax = true;
                else allMax = false;
            }
            if (mainMax) Unlock(Cat.RelicMainMax);
            if (subMax) Unlock(Cat.RelicSubMax);
            if (allMax) Unlock(Cat.RelicAllMax);

            // 六つの棚: 持っている遺物のセットが全部そろった
            var owned = Meta?.ownedRelics;
            if (owned != null)
            {
                int mask = 0;
                foreach (var r in owned) if (r != null && r.IsValid() && r.set >= 0 && r.set < RelicCatalog.SetCount) mask |= 1 << r.set;
                if (mask == (1 << RelicCatalog.SetCount) - 1) Unlock(Cat.RelicSetsAll);
            }
        }

        // ============================================================
        //  ランの終わり
        // ============================================================

        /// <summary>ランをクリアした (結末を見る 3 か所・GameManager.Floor / Gate)。 結末の実績は結末の画面と同じ <see cref="Endings.Resolve"/> で決める。</summary>
        public static void CompleteRun(RunState state)
        {
            if (!Ready || state == null || state.playerHP <= 0) return;
            if (run.damageTaken > run.startMaxHp * Cat.TheseusTimes) Unlock(Cat.HiddenTheseus);
            var def = AchievementCatalog.ForEnding(Endings.NumberOf(Endings.Resolve(state).id));
            if (def != null) Unlock(def.id);
            run.active = false;
        }


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
            // 持ち込みの遺物は再開の時に読み直す (中断の間に整備パネルで持ち込むと、 持たざる者を見逃していた・2026-11-17)。
            //   持たざる者: どちらかの時点で 1 個でも持ち込んだら外れる。 揃いの至宝: 両方の時点で 3 個揃っている時だけ。
            ReadRelicLoadout(out bool anyNow, out bool fullNow);
            run.relicEquipped |= anyNow;
            run.cursedRelicEquipped &= fullNow;
        }
    }
}

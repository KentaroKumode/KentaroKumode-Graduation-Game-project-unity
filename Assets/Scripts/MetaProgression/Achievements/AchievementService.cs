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
            // ── 層ごと (層に入るたびに 0・素通り / 忍び足) ──
            /// <summary>この層で、 ボスのほかに始めた戦闘の数 (追跡者・使い魔・イベント・値下げ交渉の戦闘も数える)。</summary>
            public int floorFights;
            /// <summary>この層で追跡者に見つかった (捕捉・捕まる・使い魔と接触)。</summary>
            public bool floorFound;
            // ── 戦闘ごと (戦闘を始めるたびに 0) ──
            /// <summary>この戦闘で見た敵の攻撃の激化の一番高い段 (0〜4・長丁場)。</summary>
            public int combatMaxEscStage;
            /// <summary>この戦闘で〈狂暴化〉が発動した (根比べ)。</summary>
            public bool combatBerserk;
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
            run.floorFights = 0;
            run.floorFound = false;
            if (depth >= Depth(4)) Unlock(Cat.Reach6);
            if (depth >= Depth(5)) Unlock(Cat.Reach7);
            var gm = GameManager.Instance;
            if (gm?.Run != null) { NoteCoins(gm.Run.coins); NoteTerminalDeck(gm.Run); }
        }

        // ============================================================
        //  戦闘
        // ============================================================

        public static void NoteCombatStarted(string enemyId)
        {
            if (!Ready) return;
            run.combatRequestedHeal = 0;
            run.hpOneStreak = 0;
            run.combatMaxEscStage = 0;
            run.combatBerserk = false;
            if (!BossIds.IsBoss(enemyId)) run.floorFights++;
        }

        /// <summary>ターンの終わり (CombatManager.FinishTurnCommon・HP を同期した後)。 <paramref name="ctx"/> は読むだけ。</summary>
        public static void NoteTurnEnded(TurnResult turn, CombatContext ctx)
        {
            if (!Ready) return;
            run.hpOneStreak = turn.playerHPAfter == 1 ? run.hpOneStreak + 1 : 0;
            if (turn.escalationStage > run.combatMaxEscStage) run.combatMaxEscStage = turn.escalationStage;
            if (turn.isCritical && turn.totalDamage >= Cat.CritGoal) Unlock(Cat.Crit100);
            if (turn.defensePiercedToZero) Unlock(Cat.PierceZero);
            if ((turn.wiredKinds & TurnResult.WiredAll) == TurnResult.WiredAll) Unlock(Cat.Terminal5Types);
            if (ctx == null) return;
            if (ctx.healBlocked) run.combatBerserk = true;   // 回復を封じるのは〈狂暴化〉だけ (Berserk)
            if (ctx.EnemyPoison >= Cat.PoisonGoal) Unlock(Cat.Poison99);
            if (ctx.GetAccumulated(ItemSystem.PassiveSkills.Keywords.Kw.BleedMaxKey) >= Cat.BleedGoal) Unlock(Cat.BleedBig);
            if (ctx.GetCharge() >= Cat.ChargeGoal) Unlock(Cat.Charge30);
            if (ctx.GetAccumulated(ItemSystem.PassiveSkills.Keywords.Kw.BurstTotalKey) >= Cat.BurstGoal) Unlock(Cat.Burst5);
        }

        /// <summary>ボスのダウンが大技 (チャージか大技のターン) に重なって取り消した (CombatManager.Down)。</summary>
        public static void NoteBossDownCanceledHeavy() { if (Ready) Unlock(Cat.DownCancel); }

        /// <summary>主攻撃 1 回で目盛りを <paramref name="broken"/> 個割った (CombatManager.Down)。</summary>
        public static void NoteBossDownBroken(int broken) { if (Ready && broken >= 2) Unlock(Cat.DownDouble); }

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

        /// <summary>役が発動した。 <paramref name="tier"/> は使わない (出目パーツの段・2026-11-18 から全部の面が同じ扱い)。
        /// 「奥の手」は LEGENDARY の賽で役を出すこと、 「型を極める」は<b>どれかの賽が持つ目</b>の役を全部出すこと (9 の面を持つ賽が無いので 18 種のままだと取れない)。</summary>
        public static void NoteRoleFired(RoleKind role, DiceFaceParts.Tier tier)
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
            var die = ItemSystem.ItemDatabase.Instance?.GetItem(EquippedDie.IdOf(GameManager.Instance?.Run));
            if (die != null && die.rarity == ItemSystem.ItemRarity.LEGENDARY) Unlock(Cat.RoleTop);
            int allMask = AttainableRoleMask();
            if ((mgr.State.achievementRoleMask & allMask) == allMask) Unlock(Cat.AllRoles);
        }

        /// <summary>どれかの賽が持つ目の役 (2 個そろいとゾロ目) の印。 賽の面は items.json の diceFaces。</summary>
        private static int AttainableRoleMask()
        {
            int mask = 0;
            foreach (var id in EquippedDie.AllIds())
                foreach (int f in EquippedDie.FacesOf(id))
                    if (f >= DiceFaceParts.MinFace && f <= DiceFaceParts.MaxFace)
                        mask |= (1 << (int)YachtRoles.Of(f, false)) | (1 << (int)YachtRoles.Of(f, true));
            if (mask == 0) foreach (RoleKind k in YachtRoles.All) mask |= 1 << (int)k;
            return mask;
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

            if (result.enemyElite && mgr?.State != null)
            {
                mgr.State.achievementEliteKills++;
                if (mgr.State.achievementEliteKills >= Cat.EliteGoal) Unlock(Cat.Elite20);
                else mgr.Save();
            }
            if (run.combatMaxEscStage >= Escalation.Thresholds.Length) Unlock(Cat.Escalation4);   // 段は 0〜4 (閾値の数が最大)
            NoteCoins(state.coins);
            if (result.turnLog != null && result.turnLog.Count > 0 && result.turnLog[result.turnLog.Count - 1].secondaryHPAtStart > 0)
                Unlock(Cat.PairOneTurn);   // 最後のターンの頭に 2 体目が生きていた = 2 体とも最後のターンに倒した

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
            if (run.combatBerserk) Unlock(Cat.BerserkWin);
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
                CheckSinsAtVesca(state);
            }
            // あと一ターンだけ: 〈最後の審判〉の刻限の 1 ターン前 (ラン通算の戦闘ターン・0 層は数えない) にボスを倒す
            int deadline = MetaDebuffApplicator.GetJudgmentTurnThreshold();
            if (deadline > 0 && state.totalCombatTurns == deadline - 1) Unlock(Cat.HiddenTurn124);
        }

        /// <summary>3 層 (深さ 5) のボスを倒した: 挑戦の難易度・職業・縛り・揃いの至宝。</summary>
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
        }

        /// <summary>七つの重荷 (隠し): ヴェスカを倒した時に背負っている「大罪:○○」の種類を通算で数え、 7 種そろったら解除。
        /// 大罪は難易度 10 の IV の札で 5 層の入口でしか配られないので、 倒す相手はヴェスカ (2026-11-17 作者の決定 a。 それまでは 3 層のボスで、 取れなかった)。</summary>
        private static void CheckSinsAtVesca(RunState state)
        {
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
        //  航行 (GameManager.FreeMap / Rewards)
        // ============================================================

        /// <summary>追跡者に見つかった (捕捉・捕まる・使い魔と接触)。</summary>
        public static void NoteFoundByPursuer() { if (Ready) run.floorFound = true; }
        public static void NotePursuerWon() { if (Ready) Unlock(Cat.PursuerWin); }
        public static void NoteFamiliarWon() { if (Ready) Unlock(Cat.FamiliarWin); }
        public static void NoteLureHit() { if (Ready) Unlock(Cat.LureHit); }

        /// <summary>嵐の段の最後 (<see cref="MapSystem.FreeMove.Storm.PhaseOf(int)"/> の 4)。</summary>
        private const int StormLastPhase = 4;

        /// <summary>層の終点のボスを倒した (GameManager.Rewards のボスの枝・地図がまだ今の層の時)。 0 層の宝箱は数えない。
        /// <paramref name="sim"/> = 今の層の航行の地図 (地図の無い層は null)。</summary>
        public static void NoteBossNodeWon(RunState state, MapSystem.FreeMove.FreeMapSim sim)
        {
            if (!Ready || state == null) return;
            int depth = state.Depth;
            if (depth == Depth(0)) return;
            if (sim != null && sim.StormMode && sim.StormPhase >= StormLastPhase) Unlock(Cat.StormBoss);
            if (state.provision <= 0) Unlock(Cat.ProvisionZeroBoss);
            // 忍び足: 追跡者のいる層で、 ボスを倒した時に追跡者がまだいて、 一度も見つかっていない (追跡者を倒した = 捕まった = 見つかった)
            if (sim != null && sim.HasFoe && MapSystem.FreeMove.FloorPlan.HasWanderer(depth) && !run.floorFound) Unlock(Cat.PursuerUnseen);
            if (depth >= Depth(2) && run.floorFights == 0) Unlock(Cat.BossOnlyFloor);
        }

        // ============================================================
        //  お金・山札・武器・挑戦カード・〈門〉・確信の連鎖
        // ============================================================

        /// <summary>所持金が変わった (GoldIncome ほか)。</summary>
        public static void NoteCoins(int coins) { if (Ready && coins >= Cat.GoldGoal) Unlock(Cat.Gold1000); }

        /// <summary>値下げ交渉 (強盗) の戦闘に勝った (GameManager.Combat)。</summary>
        public static void NoteRobberyWon() { if (Ready) Unlock(Cat.RobberyWin); }

        /// <summary>端子の山札が増えた (報酬・店)。</summary>
        public static void NoteTerminalDeck(RunState state)
        {
            if (Ready && state?.terminalDeck != null && state.terminalDeck.Count >= Cat.DeckBigGoal) Unlock(Cat.Deck30);
        }

        /// <summary>武器を最後の段 (T4+) まで鍛えた (GameManager.TryUpgradeWeapon)。 <paramref name="family"/> = items.json の family。</summary>
        public static void NoteWeaponMaxed(string family)
        {
            if (!Ready) return;
            Unlock(Cat.WeaponTop);
            var meta = Meta;
            if (meta == null || string.IsNullOrEmpty(family)) return;
            if (meta.achievementWeaponFamiliesMaxed == null) meta.achievementWeaponFamiliesMaxed = new List<string>();
            AddMeta(meta.achievementWeaponFamiliesMaxed, family);
            foreach (var f in ItemIds.WeaponFamilies) if (!meta.achievementWeaponFamiliesMaxed.Contains(f)) return;
            Unlock(Cat.WeaponAllTop);
        }

        /// <summary>挑戦カードを 1 枚受けた (GameManager.TakeChallengeCard)。 <paramref name="buff"/> = 裏のバフ (無ければ null)。</summary>
        public static void NoteChallengeCardTaken(ChallengeCards.Card buff)
        {
            if (!Ready || buff == null) return;
            var meta = Meta;
            if (meta == null) return;
            if (meta.achievementBuffsTaken == null) meta.achievementBuffsTaken = new List<string>();
            AddMeta(meta.achievementBuffsTaken, buff.kind);
            foreach (var c in ChallengeCards.All)
                if (c.isBuff && !c.pending && !meta.achievementBuffsTaken.Contains(c.kind)) return;   // 配られない (pending) バフは数えない
            Unlock(Cat.BuffsAll);
        }

        /// <summary>〈門〉の 3 工程を決めた (GameManager.CompleteGateRitual)。 <paramref name="flawless"/> = 欠陥 (GateFlaw) が 1 つも無い。</summary>
        public static void NoteGateCompleted(bool flawless) { if (Ready && flawless) Unlock(Cat.GateFlawless); }

        /// <summary>確信の連鎖 (預言者の石板 → 啓示 → 災厄の予兆) をたどって〈根拠のない確信〉を得た (EventEffectExecutor)。</summary>
        public static void NoteConvictionChainDone() { if (Ready) Unlock(Cat.Conviction); }

        // ============================================================
        //  イベント・記録
        // ============================================================

        /// <summary>イベントに出会った (GameManager.Events)。 <paramref name="eventName"/> = EventDefinition.name。</summary>
        public static void NoteEventSeen(string eventName)
        {
            if (!Ready || string.IsNullOrEmpty(eventName)) return;
            var meta = Meta;
            if (meta == null) return;
            if (meta.achievementEventsSeen == null) meta.achievementEventsSeen = new List<string>();
            if (!AddMeta(meta.achievementEventsSeen, eventName)) return;
            // 旅の目録: 召喚専用 (名指しでしか出ない) を除いた全部
            var all = EventSystem.EventDatabase.All;
            if (all == null || all.Count == 0) return;
            foreach (var ev in all)
                if (ev != null && !(ev.condition != null && ev.condition.summonOnly) && !meta.achievementEventsSeen.Contains(ev.name)) return;
            Unlock(Cat.EventsAll);
        }

        /// <summary>イベントの選択肢を選んで終えた (別のイベントへの振り直しは数えない)。</summary>
        public static void NoteEventDone()
        {
            if (!Ready) return;
            var mgr = MetaProgressManager.Instance;
            if (mgr?.State == null) return;
            mgr.State.achievementEventsDone++;
            if (mgr.State.achievementEventsDone >= Cat.EventsGoal) Unlock(Cat.Events50);
            else mgr.Save();
        }

        /// <summary>〈さびれた観測所〉が出た (博士に会った)。</summary>
        public static void NoteObservatoryMet() { if (Ready) Unlock(Cat.Observatory); }

        /// <summary>記録 (codex) の品と本の埋まり具合を見る (CodexRecorder が記録を足した時・結末の後)。 ランの外でも呼んでよい。
        /// 品 = 記録の品の升目 (武器・秘宝・消耗品。 手に入れるまで影も出さない結末に関わる品 (ItemIds.EndingKeyItems) は分母に入れない)、 本 = 読み物 (<see cref="Codex.WorldVignettes.All"/>)。</summary>
        public static void CheckCodex()
        {
            if (Suppressed) return;
            var db = ItemSystem.ItemDatabase.Instance;
            if (db == null) return;
            int itemTotal = 0, itemOwned = 0;
            foreach (var it in db.GetAllItems())
            {
                if (it == null) continue;
                if (it.category != ItemSystem.ItemCategory.Weapon && it.category != ItemSystem.ItemCategory.Passive
                    && it.category != ItemSystem.ItemCategory.Consumable) continue;
                if (ItemIds.HiddenInCodexUntilOwned(it.id)) continue;
                itemTotal++;
                if (Codex.CodexRecord.ItemOwned(it.id)) itemOwned++;
            }
            int bookTotal = 0, bookOpen = 0;
            foreach (var v in Codex.WorldVignettes.All)
            {
                if (v == null) continue;
                bookTotal++;
                if (Codex.VignetteUnlockState.IsUnlocked(v)) bookOpen++;
            }
            if (itemTotal == 0 || bookTotal == 0) return;
            if (itemOwned * 2 >= itemTotal && bookOpen * 2 >= bookTotal) Unlock(Cat.CodexHalf);
            if (itemOwned >= itemTotal && bookOpen >= bookTotal) Unlock(Cat.CodexFull);
        }

        // ============================================================
        //  整備パネル (ランの外)
        // ============================================================

        /// <summary>整備パネルに点を振った (MetaProgressManager.TryAssignRank)。</summary>
        public static void NotePanelAssigned(MetaProgressState s)
        {
            if (Suppressed || s == null) return;
            if (s.TotalAssignedPoints() >= MetaPanel.MaxPoints) Unlock(Cat.PanelFull);
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
            public int floorFights, combatMaxEscStage;
            public bool floorFound, combatBerserk;
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
            s.floorFights = run.floorFights; s.floorFound = run.floorFound; s.combatMaxEscStage = run.combatMaxEscStage; s.combatBerserk = run.combatBerserk;
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
                floorFights = s.floorFights, floorFound = s.floorFound, combatMaxEscStage = s.combatMaxEscStage, combatBerserk = s.combatBerserk,
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

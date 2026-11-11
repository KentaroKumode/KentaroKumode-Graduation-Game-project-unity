using System;
using CombatSystem;
using MapSystem;
using UnityEngine;

namespace GameLoop
{
    /// <summary>GameManager: ランの中断セーブと再開 (issue #84・形式と書き方は <see cref="RunSave"/>)。</summary>
    public partial class GameManager
    {
        /// <summary>いま中断セーブできるか。 できなければ理由 (画面に出す日本語) を返す。
        ///
        /// <para>できるのは<b>航行のマップで、 何も待っていない時</b>だけ: 窓 (挑戦カード・開幕パッシブ・武器の入れ替え・端子の報酬・
        /// 報酬の 2 択) が残っておらず、 追跡者・使い魔と接触しておらず、 戦闘中でない。 戦闘・店・イベント・休憩の途中は、
        /// その場の状態 (戦闘の文脈・店の棚・選択肢) を写す口が無いので保存しない。 8 層 (マップの無いヴェスカ戦) も同じ。</para></summary>
        public bool CanSuspendNow(out string reason)
        {
            reason = null;
            if (RunSave.Disabled) { reason = "BOT の実行中は保存しない"; return false; }
            if (Run == null || !Run.isRunActive || Run.playerHP <= 0) { reason = "ランの途中ではない"; return false; }
            if (CurrentPhase != GamePhase.MapNavigation) { reason = "航行のマップにいる時だけ保存できる"; return false; }
            var sim = FreeMap;
            if (sim == null) { reason = "この層では保存できない"; return false; }
            if (sim.Dead || sim.InFight || sim.PendingArrival >= 0 || InMapEncounterCombat) { reason = "追跡者と接触している間は保存できない"; return false; }
            if (CombatManager.Instance != null && CombatManager.Instance.IsCombatActive) { reason = "戦闘中は保存できない"; return false; }
            if (PendingStartingPassiveChoices.Count > 0 || PendingChallengeTiers.Count > 0 || _entryHealPending
                || HasPendingRewardChoice || Run.pendingWeaponIds.Count > 0 || Run.pendingTerminalOffer.Count > 0)
            { reason = "選ぶものが残っている間は保存できない"; return false; }
            return true;
        }

        /// <summary>中断セーブを書く (ランはそのまま続く)。 書けたら true。</summary>
        public bool SuspendSave(out string why)
        {
            if (!CanSuspendNow(out why)) return false;
            RunSave.Data data;
            try
            {
                data = new RunSave.Data
                {
                    schema = RunSave.Schema(),
                    savedAtUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                    sumFloor = Run.currentFloor, sumHp = Run.playerHP, sumMaxHp = Run.playerMaxHP, sumCoins = Run.coins,
                    sumClass = (int)Run.playerClass, sumDifficulty = Run.challengeDifficulty,
                    run = RunSave.CaptureRun(Run),
                    map = RunSave.CaptureMap(MapManager.Instance),
                    rng = GameRng.CaptureState(),
                    combatSeq = CombatManager.Instance != null ? CombatManager.Instance.RunCombatSequence : 0,
                    pursuerFightCount = _pursuerFightCount,
                    familiarFightCount = _familiarFightCount,
                    achievements = MetaProgression.Achievements.AchievementService.CaptureRun(),
                    metaFingerprint = RunSave.MetaFingerprint(),
                };
                ItemSystem.PassiveSkills.PassiveSkillRegistry.CaptureAllRunState(data.skillIds, data.skillStates);
            }
            catch (Exception e)
            {
                why = "保存の写しを作れない";
                Debug.LogError($"[RunSave] {why}: {e}");
                return false;
            }
            if (!RunSave.Write(data, out why)) return false;
            Log($"中断セーブ: {Run.currentFloor}層 HP {Run.playerHP}/{Run.playerMaxHP} → {RunSave.FilePath}");
            return true;
        }

        /// <summary>中断セーブを書いてタイトルへ戻る (ランは終えない ── 遺物の獲得もしない)。</summary>
        public bool SuspendAndReturnToTitle(out string why)
        {
            if (!SuspendSave(out why)) return false;
            // ReturnToTitle は EndRun (遺物の獲得) を通るので使わない。 ランは「止めた」だけで終わっていない。
            //   isRunActive を落とすのは、 タイトルの間にこの Run を「進行中のラン」として扱わせないため
            //   (終了時の自動セーブもこれを見て書かない)。 中身は再開時にファイルから作り直す。
            Run.isRunActive = false;
            CurrentEnemy = null;
            LastCombatResult = null;
            SetPhase(GamePhase.Title);
            return true;
        }

        /// <summary>中断セーブから再開する。 <b>読み込んだらファイルを消す</b> (再開は 1 回きり)。 タイトルからだけ呼べる。
        /// 読めない版・壊れたセーブは捨てて false (理由を why に)。</summary>
        public bool ResumeFromSave(out string why)
        {
            why = null;
            if (CurrentPhase != GamePhase.Title) { why = "タイトルからだけ再開できる"; return false; }
            if (!RunSave.TryRead(out var data, out why)) return false;

            var cm = CombatManager.Instance;
            if (cm != null)
            {
                cm.OnCombatEnd -= HandleCombatEnd;
                cm.OnCombatEnd += HandleCombatEnd;
            }

            RunState run;
            FloorMap map;
            try
            {
                // 乱数の塩を先に戻す (盤面を作り直す時に追跡者の性質・天候を同じに引くため)
                if (!GameRng.RestoreState(data.rng)) throw new InvalidOperationException("乱数の写しが読めない");
                run = new RunState();
                run.Initialize(BaseStartingHP);
                RunSave.RestoreRun(run, data.run);
                run.InvalidateCardChallenge();
                run.isRunActive = true;
                map = RunSave.RestoreMap(data.map);
            }
            catch (Exception e)
            {
                why = "中断セーブの中身が読めない";
                Debug.LogError($"[RunSave] {why}: {e}");
                RunSave.Delete();
                return false;
            }

            // ここから先は戻すだけ (盤面が戻せなかった時だけタイトルに残る)
            var prevRun = Run;
            Run = run;
            SelectedClass = Run.playerClass;
            ItemSystem.PassiveSkills.PassiveSkillRegistry.ResetAllRunState();
            ItemSystem.PassiveSkills.PassiveSkillRegistry.RestoreAllRunState(data.skillIds, data.skillStates);
            cm?.RestoreRunCombatSequence(data.combatSeq);

            LastCombatResult = null;
            CurrentEnemy = null;
            CurrentEnemySecondary = null;
            IsEliteSecondFight = false;
            firstEliteEnemy = null;
            inFalseMerchantCombat = false;
            returnToMapAfterEventCombat = false;
            eventChoiceResolved = false;
            gateBloodResolved = gateRelicResolved = gateTransferResolved = false;
            pendingRewardChoices.Clear(); rewardChoiceIndex = 0;
            PendingStartingPassiveChoices.Clear();
            PendingChallengeTiers.Clear(); _challengeOffer = null; _entryHealPending = false;
            _inPursuerFight = false; _inFamiliarFight = false;
            _pursuerFightCount = data.pursuerFightCount;
            _familiarFightCount = data.familiarFightCount;
            ActiveModifier = FloorModifierDatabase.Get(Run.Depth);

            try { MapManager.Instance.RestoreFromSave(map, Run.Depth, data.map.currentNodeId, data.map.hasLayout ? data.map.sim : null); }
            catch (Exception e)
            {
                why = "中断セーブの盤面が戻せない";
                Debug.LogError($"[RunSave] {why}: {e}");
                Run = prevRun;
                RunSave.Delete();
                return false;
            }

            MetaProgression.Achievements.AchievementService.RestoreRun(data.achievements);
            GameRng.RestoreState(data.rng);   // 盤面を作り直す時に引いた分 (診断用の累計) も中断した時の値へ戻す
            RunSave.Delete();                 // 再開は 1 回きり

            if (data.metaFingerprint != RunSave.MetaFingerprint())
                Debug.LogWarning("[RunSave] 中断した時と整備パネル・遺物・難易度が違う。 再開したランには今の設定が効く");
            Log($"=== 中断したランを再開 === {Run.currentFloor}層 HP {Run.playerHP}/{Run.playerMaxHP} (保存 {data.savedAtUtc})");

            SetPhase(GamePhase.RunStart);
            OnRunStarted?.Invoke(Run);
            OnFloorModifierApplied?.Invoke(ActiveModifier);
            LayerTitles.Raise(LayerTitles.ForFloor(Run.Depth));
            SetPhase(GamePhase.MapNavigation);
            return true;
        }

        /// <summary>ゲームを閉じる時、 セーブできる時点にいれば中断セーブする (閉じてもランが消えないように)。</summary>
        private void AutoSuspendOnQuit()
        {
            if (Run == null || !CanSuspendNow(out _)) return;
            if (SuspendSave(out var why)) Log("終了に合わせて中断セーブした");
            else if (why != null) Debug.LogWarning($"[RunSave] 終了時の中断セーブに失敗: {why}");
        }
    }
}

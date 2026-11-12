using System;
using System.Collections.Generic;
using UnityEngine;
using Codex;
using CombatSystem;
using EventSystem;
using ItemSystem;
using ItemSystem.PassiveSkills;
using ItemSystem.Shop;
using MapSystem;

namespace GameLoop
{
    /// <summary>GameManager: イベントマス・空虚の利・イベント戦闘。</summary>
    public partial class GameManager
    {
        /// <summary>〈破綻〉の空白化で失う物資。 4 種の文面すべてに一律で掛かる。</summary>
        private const int VoidTileProvisionCost = 1;   // 2026-11-09 デノミ (旧 30)

        /// <summary>〈破綻〉の空白化で呼ぶ召喚専用イベントの名前 (event_list.txt と一致させる)。
        ///
        /// **1 ラン に何度も出る**ので 1 種類では飽きる。 4 種を用意し、 空白の理由を
        /// 毎回変えてある (自然に還った / 先客に取られた / 地図の方が古い)。
        /// 同じ絵の描き直しにすると、 種類を増やしても繰り返し感は減らない。</summary>
        private static readonly string[] VoidTileEventNames =
        {
            "国破れて山河あり",
            "兵どもが夢の跡",
            "先客",
            "間違いのない地図",
        };

        /// <summary>〈さびれた観測所〉のイベント名。 EventDatabase の定義名と一致させる。
        /// 遭遇回数 (メタ・ラン跨ぎ) で初対面と再訪を切り替える。</summary>
        public const string ObservatoryEventName = "さびれた観測所";
        public const string ObservatoryRevisitEventName = "さびれた観測所・再訪";
        /// <summary>写しを持ち帰った次の再訪 **1 回だけ** 使われる版。</summary>
        public const string ObservatoryRevisitCopyEventName = "さびれた観測所・再訪・写し";
        /// <summary>一度出たランで再出現させないためのフラグ。</summary>
        public const string ObservatorySeenFlag = "観測所";
        /// <summary>イベントマス 1 回あたりの出現確率。 1 ラン 約13 マスで通算 1/38 になる値。
        /// **ここを触ると出現頻度が直に変わる。** 30〜50 ラン に 1 度が設計意図。</summary>
        public const float ObservatoryChance = 0.002f;

        private void BeginEventEncounter()
        {
            eventChoiceResolved = false;
            returnToMapAfterEventCombat = false;

            var ee = EventEncounter.Instance;
            if (ee == null)
            {
                Debug.LogWarning("[GameManager] EventEncounter シングルトン未配置 — マップに戻る");
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            ee.TakePendingTurns();   // 前のイベントで進め損ねた分は持ち越さない
            ee.TakePendingNav();     // 航行の効果も同じ (戦闘になった選択肢など・2026-11-12)

            // 6層の固定の点「裂け目の記録」は〈真理〉へ至る専用イベントにする (2026-09-28: 自由移動の層では
            //   「最初に踏んだイベントマス」ではなく生成時に決めた固定の点 ── MapNode.isFixedEvent)。
            // BeginWith は EventDatabase.Pick を通らないため乱数列を消費しない。
            EventSystem.EventDefinition forced = null;
            var eventNode = MapManager.Instance?.CurrentNode;
            if (Run != null && Run.Depth == 6
                && (eventNode == null || !eventNode.freeMap || eventNode.isFixedEvent)
                && ConvictionSystem.HasResolveOrBetter(Run)
                && !ConvictionSystem.HasTruth(Run))
            {
                forced = EventSystem.EventDatabase.GetByName(ConvictionSystem.Layer7RevelationEventName);
            }

            // 〈さびれた観測所〉: 全イベント中これだけ超低確率。 30〜50 ラン に 1 度。
            //
            //   **プールの重みではなく専用の事前ロールで出す。** 重み方式だと
            //   出現率がプールの総数に依存し、イベントを足すたびに静かにずれる
            //   (Tier のパーセンタイル枠が「相対配分は品が増えると定義上押し出される」
            //    という理由で §24 で廃止されたのと同じ穴)。 絶対確率で持つ。
            //
            //   1 ラン のイベントマスは約 13 回。 1-(1-p)^13 = 1/38 となる p を置く。
            //   一度出たランでは二度と出ない (ownedFlags で自己抑止)。
            if (forced == null && Run != null
                && !Run.ownedFlags.Contains(ObservatorySeenFlag)
                && GameRng.Chance(ObservatoryChance, "event.observatory"))
            {
                bool revisit = ObservatoryState.IsRevisit;
                bool withCopy = revisit && ObservatoryState.CopyHeld;
                string want = !revisit ? ObservatoryEventName
                            : withCopy ? ObservatoryRevisitCopyEventName
                                       : ObservatoryRevisitEventName;
                forced = EventSystem.EventDatabase.GetByName(want);
                // 定義欠けはソフトロックの元なので順に落とす
                if (forced == null && withCopy)
                    forced = EventSystem.EventDatabase.GetByName(ObservatoryRevisitEventName);
                if (forced == null && revisit)
                    forced = EventSystem.EventDatabase.GetByName(ObservatoryEventName);
                if (forced != null)
                {
                    Run.ownedFlags.Add(ObservatorySeenFlag);
                    ObservatoryState.NoteMet();
                    if (withCopy)
                    {
                        // **選択肢ではなく上乗せ。** ラン跨ぎで持ち越した見返りなので、
                        //   どの択を選んでも入る (4つ目の択にすると通常択と排他になり、
                        //   パッシブが逆に 1 個減るという逆転が起きていた)。
                        EventSystem.EventEffectExecutor.GrantObservatoryCopyBonus(Run);
                        ObservatoryState.ConsumeCopy();            // 次の再訪 1 回だけ
                    }
                }
            }

            bool ok = forced != null ? ee.BeginWith(Run, forced) : ee.Begin(Run);
            if (!ok || ee.Current == null)
            {
                // イベント0件/抽選失敗なら何もせずマップへ（ソフトロック防止）
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            // メタ: イベント発見トークン
            MetaProgression.MetaTokenEarner.OnEventEncountered();
            SetPhase(GamePhase.EventEncounter);
        }

        /// <summary>イベントの選択肢 i を選ぶ（UI / デバッグから呼ぶ）。</summary>
        public void ResolveEventChoice(int index)
        {
            if (CurrentPhase != GamePhase.EventEncounter) return;
            var ee = EventEncounter.Instance;
            if (ee?.Current == null) return;
            if (eventChoiceResolved) return;
            // 払えないゴールドの選択肢は選べない (押されても何もしない・画面は暗く出す)
            if (!EventEncounter.IsChoiceSelectable(ee.Current, index, Run)) return;

            var result = ee.ResolveChoice(index);
            eventChoiceResolved = true;
            if (result == null) return;

            // 専用イベントは選択を確定した時点で7層資格を立てる。
            // 選択効果とは分離し、イベント本文を調整しても進行フラグが壊れないようにする。
            if (index == 0
                && ee.Current.name == ConvictionSystem.Layer7RevelationEventName
                && ConvictionSystem.RevealTruthInLayer6(Run))
            {
                MetaProgression.Achievements.AchievementService.NoteFinalPageOpened();
                Log("裂け目の記録を読み終えた。〈決意〉は〈真理〉へ変わった");
            }

            // 戦闘トリガがあれば即時遷移（フレーバー表示は後回し）
            if (result.triggerEliteCombat)
            {
                returnToMapAfterEventCombat = true;
                StartEventCombat(elite: true);
                return;
            }
            if (result.triggerCombat)
            {
                returnToMapAfterEventCombat = true;
                StartEventCombat(elite: false);
                return;
            }

            // ランダムイベント発生: RandomEvent を含まないプールから1回だけ
            // 別イベントへ振り直す。振り直し先は RandomEvent を持たないため
            // 連鎖は構造的に発生しない。抽選失敗時は通常終了。
            if (result.triggerRandomEvent)
            {
                if (ee.Begin(Run, excludeRandomEvent: true) && ee.Current != null)
                    eventChoiceResolved = false;  // 振り直し先を改めて解決させる
                else
                    eventChoiceResolved = true;   // 該当なし → 通常終了
                return;
            }

            // 通常はフレーバー表示 → Space で完了
        }

        /// <summary>イベント完了（フレーバー読了） → マップへ戻る。</summary>
        public void ConfirmEventEncounter()
        {
            if (CurrentPhase != GamePhase.EventEncounter) return;
            // 通常は選択確定後のみ。ただし Current==null（抽選失敗/消失）の場合は
            // ソフトロック回避のため未確定でも強制的にマップへ戻す。
            bool curNull = EventEncounter.Instance == null || EventEncounter.Instance.Current == null;
            if (!eventChoiceResolved && !curNull) return;
            EventEncounter.Instance?.Clear();
            SetPhase(GamePhase.MapNavigation);
            PassEventTurns();
        }

        /// <summary>イベントの選択肢が使ったターン (効果「ターン+N」) を、 地図へ戻ってから進める (2026-11-11・issue #17)。
        ///
        /// <para>口は休憩の「多めに回復」と同じ <see cref="MapSystem.FreeMove.FreeMapSim.PassTurnsExternal"/>。 その間に追跡者・使い魔・
        /// 天候の前線が動き、 捕まれば追跡者の戦闘へ入る (<see cref="RunFreeMapOp"/> が処理する)。
        /// 航行マップの無い層・戦闘の途中では何もしない (使わなかった分は捨てる)。</para></summary>
        private void PassEventTurns()
        {
            var ee = EventEncounter.Instance;
            int n = ee != null ? ee.TakePendingTurns() : 0;
            var nav = ee != null ? ee.TakePendingNav() : null;
            // 航行の効果 → ターンの順 (2026-11-12)。 「ターン+1,追跡者を撒く」は撒いてから時間が過ぎる。
            //   航行の効果はどれも自分を動かさない (近道も「この後の移動をただにする」だけ) ので、 ここで点が起動することは無い。
            //   下の段の確認は地図の外へ出た時の守りだけ。
            if (nav != null)
                foreach (var e in nav)
                {
                    if (CurrentPhase != GamePhase.MapNavigation || FreeMap == null) break;
                    ApplyEventNav(e);
                }
            if (n <= 0 || CurrentPhase != GamePhase.MapNavigation || FreeMap == null) return;
            Log($"イベントで {n} ターンを使った");
            RunFreeMapOp(s => s.PassTurnsExternal(n));
        }

        /// <summary>イベントの航行の効果を 1 つ適用する (2026-11-12・docs/GAME.md §17)。 中身は <see cref="MapSystem.FreeMove.FreeMapSim"/> の汎用の動詞で、
        /// 着いた点の起動・捕まった時の戦闘は他の操作と同じ <see cref="RunFreeMapOp"/> が処理する。 航行マップの無い層では呼ばれない。</summary>
        private void ApplyEventNav(EventSystem.EventEffect e)
        {
            switch (e.type)
            {
                case EventSystem.EventEffectType.RevealMap:
                    RunFreeMapOp(s => { int k = s.RevealAround(e.amount, false); Log($"イベント: 周り {e.amount} 歩の点が分かった（新しく {k} 点）"); return MapSystem.FreeMove.FreeMapEvent.None; });
                    break;
                case EventSystem.EventEffectType.RevealEncounters:
                    RunFreeMapOp(s => { s.RevealAround(e.amount, true); Log($"イベント: 周り {e.amount} 歩の戦闘の相手が分かった"); return MapSystem.FreeMove.FreeMapEvent.None; });
                    break;
                case EventSystem.EventEffectType.RevealSupplies:
                {
                    var pts = new List<int>();
                    var map = MapManager.Instance?.CurrentMap;
                    if (map != null)
                        foreach (var node in map.GetAllNodes())
                            if (node != null && node.index >= 0 && (node.type == TileType.Shop || node.type == TileType.Rest)) pts.Add(node.index);
                    RunFreeMapOp(s => { s.RevealPoints(pts); Log($"イベント: この層の店と休憩の場所が分かった（{pts.Count} か所）"); return MapSystem.FreeMove.FreeMapEvent.None; });
                    break;
                }
                case EventSystem.EventEffectType.ShakeOffPursuer:
                    RunFreeMapOp(s => s.ShakeOff());
                    break;
                case EventSystem.EventEffectType.PushPursuer:
                    RunFreeMapOp(s => s.PushFoe(e.amount));
                    break;
                case EventSystem.EventEffectType.PullPursuer:
                    RunFreeMapOp(s => s.PullFoe(e.amount));
                    break;
                case EventSystem.EventEffectType.Shortcut:
                    RunFreeMapOp(s =>
                    {
                        s.GrantFreeMoves(e.amount);
                        Log($"イベント: 時限バフ「近道」+{e.amount}（残り {s.FreeMovesLeft} 回・移動の物資とターンを使わない・待つ・偵察には効かない・層を出ると消える）");
                        return MapSystem.FreeMove.FreeMapEvent.None;
                    });
                    break;
                case EventSystem.EventEffectType.ShiftFront:
                    RunFreeMapOp(s => s.ShiftFront(e.amount));
                    break;
            }
        }

        // =====================================================================
        //  選べる品 (イベント「パッシブアイテムをNつから選ぶ」・2026-11-12)
        // =====================================================================

        /// <summary>選ぶのを預かっているイベントの品の提示 (人が遊んでいる時だけ)。 地図へ戻ると選択画面が出る (Game2DPhaseScreens)。 先頭から順に選ぶ。</summary>
        public readonly List<List<string>> PendingEventPassiveOffers = new List<List<string>>();

        /// <summary>品の提示を受ける。 BOT (GameHooks.ChooseOfferedPassive・無ければ開幕パッシブの口) はこの場で選び、 選んだ品の id を返す。
        /// 人が遊んでいる時は選択画面へ預けて null を返す (提示の抽選は呼ぶ側で済んでいるので、 乱数の消費は BOT と同じ)。</summary>
        public string OfferPassiveChoice(List<string> cands)
        {
            if (Run == null || cands == null || cands.Count == 0) return null;
            var chooser = GameHooks.ChooseOfferedPassive ?? GameHooks.ChooseStartingPassive;
            if (cands.Count > 1 && chooser == null && !GameHooks.BotDriving && !Application.isBatchMode)
            {
                PendingEventPassiveOffers.Add(new List<string>(cands));
                return null;
            }
            int pick = chooser?.Invoke(cands) ?? 0;
            string id = cands[Math.Max(0, Math.Min(pick, cands.Count - 1))];
            ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
            Log($"イベント: 秘宝「{id}」を {cands.Count} つから選んで獲得");
            return id;
        }

        /// <summary>預かっているイベントの品の提示の先頭から 1 つ選んで所持する (選択画面から呼ぶ)。</summary>
        public void ResolveEventPassiveOffer(int pick)
        {
            if (Run == null || PendingEventPassiveOffers.Count == 0) return;
            var cands = PendingEventPassiveOffers[0];
            PendingEventPassiveOffers.RemoveAt(0);
            if (cands == null || cands.Count == 0) return;
            string id = cands[Math.Max(0, Math.Min(pick, cands.Count - 1))];
            ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(Run, id);
            Log($"イベント: 秘宝「{id}」を選んで獲得");
        }

        /// <summary>イベントから戦闘を開始する。</summary>
        private void StartEventCombat(bool elite)
        {
            CurrentEnemy = FloorManager.PickEnemy(Run.Depth);
            if (CurrentEnemy == null)
            {
                Debug.LogError("[GameManager] イベント戦闘の敵選出失敗");
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"イベント戦闘: {CurrentEnemy.displayName} (elite={elite})");

            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
        }
    }
}

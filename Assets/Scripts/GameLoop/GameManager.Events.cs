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
        /// <summary>イベントマスに到達したときに呼ばれる。抽選 → EventEncounter へ。</summary>
        /// <summary>T4-A〈破綻〉: 有利マス (休憩/秘宝/交換) を確率で空白化する。
        /// 空白化したら固有イベント〈国破れて山河あり〉を起こし、 true を返す。
        ///
        /// **マップ生成ではなく踏んだ瞬間に判定する。** 盤上は普通の有利マスに見えるので、
        /// 「そこへ向かう」判断は必ず先に済んでおり、 空だと分かるのは着いてから。
        /// 迂回で避けられない代わりに、 有利マスの期待値そのものが目減りする。
        ///
        /// イベント側は効果も選択肢も持たない ── 罰は**この関数が一律で取る物資 −3** に
        /// 一本化する。 文面ごとに効果を散らすと、 どの文面を引いたかで罰が変わり、
        /// 空白化の確率だけを較正できなくなる。</summary>
        private bool TryVoidAdvantageTile(TileType type)
        {
            if (type != TileType.Rest && type != TileType.Treasure && type != TileType.Exchange)
                return false;
            float p = MetaProgression.MetaDebuffApplicator.GetVoidTileChance();
            if (p <= 0f) return false;
            MetaProgression.MetaDebuffApplicator.VoidTileChecks++;
            if (GameRng.Value("challenge.hatan") >= p) return false;
            MetaProgression.MetaDebuffApplicator.VoidTileTriggers++;

            Log($"[破綻] {TileToJapanese(type)} は空白だった");
            // **物資 −3 を必ず取る (2026-08-17)。**
            //   空白化だけでは 4pt を払って p=0.103 の死に段だった (実測 −2.0pt)。
            //   有利マスを 1 つ失う痛みは「そこへ向かった手番が無駄になった」だけで、
            //   盤面には何も残らない ── 損失が観測されないので判断も変わらない。
            //   物資を削れば以後の振り直し・進路選択に効き続けるので、罰が持続する。
            //   **空白化の 4 種すべてに一律で乗せる**。 文面ごとに差を付けると、
            //   イベント側に効果を持たせないという設計 (下の doc 参照) が崩れる。
            ProvisionSystem.ApplyEvilChoice(Run, VoidTileProvisionCost);
            var ee = EventSystem.EventEncounter.Instance;
            // 文面の抽選は**発動判定と別の乱数列**から引く。 同じ列を続けて消費すると、
            //   文面を増減しただけで発動の並びまで変わり、 シード固定のペア比較が壊れる。
            string pickName = VoidTileEventNames[
                GameRng.RangeAuto("challenge.hatan.flavor", 0, VoidTileEventNames.Length)];
            var def = EventSystem.EventDatabase.GetByName(pickName);
            // **イベントを出せなくても空白化は成立させる。** ここで有利マスの処理へ
            //   落とすと、 シングルトン未配置やテキスト欠落が「デバフが効かない」という
            //   形で現れ、 数値だけ見ても原因に辿り着けない。
            if (ee == null || def == null || !ee.BeginWith(Run, def))
            {
                Debug.LogWarning($"[破綻] 固有イベント『{pickName}』を開始できず — 空白のみ適用");
                SetPhase(GamePhase.MapNavigation);
                return true;
            }
            eventChoiceResolved = false;
            returnToMapAfterEventCombat = false;
            SetPhase(GamePhase.EventEncounter);
            return true;
        }

        /// <summary>〈破綻〉の空白化で失う物資。 4 種の文面すべてに一律で掛かる。</summary>
        private const int VoidTileProvisionCost = 30;   // 2026-09-28: 物資 ×10

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

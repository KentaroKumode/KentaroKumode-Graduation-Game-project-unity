using UnityEngine;

namespace GameLoop
{
    /// <summary>希望ゲージの床(tier)。下に行くほどデバフが累積する。</summary>
    public enum HopeTier
    {
        Calm = 0,       // 76-100 : 暇（デバフ無し）
        Fretful = 1,    // 46-75  : 焦燥（疲労）
        Pessimism = 2,  // 21-45  : 悲観（＋苦悩）/ ここから上限ロック
        Despair = 3,    // 1-20   : 絶望（＋迷妄）
        Madness = 4,    // 0      : 発狂（秒読み）
    }

    /// <summary>
    /// 希望ゲージ（カルマ＋飢餓を統合・ADR-0002）のロジック。
    /// 状態は <see cref="RunState"/> 側（hope / hopeCap / combatStartHP / madnessMoveCounter）に持ち、
    /// このクラスは「読む」クエリと「動かす」ミューテータを提供する純ヘルパ（MonoBehaviour非依存）。
    /// 設計の正本: docs/adr/0002-hope-system.md。数値は暫定（phase-3 タスク3-8でオートラン検証）。
    /// </summary>
    public static class HopeSystem
    {
        // === 定数（暫定・後ろ重心。3-8で要チューニング） ===
        public const int HopeMax = 100;

        // 床: この値「以下」で各 tier に入る
        public const int FloorFretful   = 75; // 焦燥
        public const int FloorPessimism = 45; // 悲観（ここで上限ロック開始）
        public const int FloorDespair   = 20; // 絶望
        public const int MadnessRecover = 10; // 発狂はここまで回復でリセット
        public const int MadnessMoves   = 5;  // 発狂の秒読み移動回数

        // 被弾（HP収支マイナス）1戦あたりの希望損（希望tierごと定量）
        public const int LossCalm      = 4;  // 暇でも被弾すれば確実に削れる（満タン張り付き防止）
        public const int LossFretful   = 5;  // 小増
        public const int LossPessimism = 7;  // 中
        public const int LossDespair   = 10; // 大

        public const int EvilChoiceCost = 10; // 悪選択(旧カルマ+1相当)の希望損
        public const int LateralCost    = 5; // 横移動1回の希望損（縦/斜めは0）2026-06-04: 2→5
        public const int RerollCost      = 3; // ダイス振り直し1回(毎ターン最大1回)の希望損（2026-06-05・#1）
        public const int DespairMarch    = 1; // 絶望的な進軍(旧Lv8): 移動毎の追加損
        public const int ComposureRecover = 0; // 2026-06-04: 被弾0勝利の希望回復を撤廃（回復源なし）

        // 絶望帯デバフの効果量
        public const float FatigueZeroDamageChance = 0.15f; // 疲労: 攻撃が0ダメになる確率
        public const float AnguishCritMultDelta    = -0.5f; // 苦悩: 会心倍率への加算補正
        public const int   DelusionDisableMin = 1;          // 迷妄: 戦闘開始時パッシブ無効の最小個数
        public const int   DelusionDisableMax = 3;          // 同・最大個数

        // === クエリ ===

        public static HopeTier GetTier(RunState run)
        {
            if (run == null) return HopeTier.Calm;
            int h = run.hope;
            if (h <= 0) return HopeTier.Madness;
            if (h <= FloorDespair) return HopeTier.Despair;
            if (h <= FloorPessimism) return HopeTier.Pessimism;
            if (h <= FloorFretful) return HopeTier.Fretful;
            return HopeTier.Calm;
        }

        /// <summary>HP収支マイナス1戦あたりの希望損（現在の tier 依存・定量）。</summary>
        public static int CombatHopeLoss(RunState run)
        {
            switch (GetTier(run))
            {
                case HopeTier.Calm:      return LossCalm;
                case HopeTier.Fretful:   return LossFretful;
                case HopeTier.Pessimism: return LossPessimism;
                case HopeTier.Despair:   return LossDespair;
                default:                 return 0; // 発狂(0)では既に底
            }
        }

        /// <summary>疲労: 攻撃が0ダメになる確率（焦燥以下で発生）。</summary>
        public static float GetZeroDamageChance(RunState run)
            => GetTier(run) >= HopeTier.Fretful ? FatigueZeroDamageChance : 0f;

        /// <summary>苦悩: 会心倍率への加算補正（悲観以下で -0.5）。</summary>
        public static float GetCritMultiplierDelta(RunState run)
            => GetTier(run) >= HopeTier.Pessimism ? AnguishCritMultDelta : 0f;

        /// <summary>迷妄: 戦闘開始時に無効化するパッシブ個数（絶望以下で 1-3、それ以外 0）。</summary>
        public static int RollPassiveDisableCount(RunState run, System.Random rng = null)
        {
            if (GetTier(run) < HopeTier.Despair) return 0;
            return rng != null
                ? rng.Next(DelusionDisableMin, DelusionDisableMax + 1)
                : Random.Range(DelusionDisableMin, DelusionDisableMax + 1);
        }

        public static bool IsMadness(RunState run) => run != null && run.hope <= 0;

        // === 計測（オートラン集計専用・通常プレイでは無害な静的カウンタ） ===
        /// <summary>希望の増減を「発生源別」に積算する。実際に適用された(クランプ後の)量を計上する。
        /// AutoRunner が1ランごとに <see cref="Reset"/> し、終了時に読み取る。</summary>
        public static class Stats
        {
            public static int combatLoss;     // 戦闘HP収支マイナスによる損
            public static int composureGain;   // 被弾0勝利のわずか回復
            public static int lateralLoss;     // 横移動コスト
            public static int marchLoss;       // 絶望的な進軍(移動毎)
            public static int evilLoss;        // 悪選択コスト
            public static int foodGain;        // 食料回復
            public static int rerollLoss;      // ダイス振り直しコスト（#1）

            public static void Reset()
            {
                combatLoss = composureGain = lateralLoss = marchLoss = evilLoss = foodGain = rerollLoss = 0;
            }
        }

        // === ミューテータ ===

        /// <summary>希望を減少（0でクランプ）。上限ロックと発狂状態を更新。
        /// 異常現象「薄れる人」 発動中は追加で -1 (ただし amount==0 ではトリガしない)。</summary>
        public static void Reduce(RunState run, int amount)
        {
            if (run == null || amount <= 0) return;
            int extra = MapSystem.AbyssPhenomena.AbyssPhenomenonCombatHooks.OnHopeReduceExtraLoss(run);
            // 契約「宣教師」: 戦闘外希望減少を -1/-2/-3 (協力中の騎士で +1)。 0 までしか減らない。
            // 戦闘中の reroll cost 等は対象外なので IsCombatActive で除外。
            int contractOffset = 0;
            var cm = CombatSystem.CombatManager.Instance;
            if (cm == null || !cm.IsCombatActive)
            {
                contractOffset = GameLoop.Contracts.ContractManager.Instance.GetHopeLossReduction(run);
            }
            int effective = Mathf.Max(0, amount - contractOffset);
            run.hope = Mathf.Max(0, run.hope - effective - extra);
            UpdateCapLock(run);
            UpdateMadnessState(run);
        }

        /// <summary>希望を回復（上限 hopeCap でクランプ）。発狂からの離脱を更新。
        /// 佯狂者の冠で希望0固定中(crownHopeLocked)は回復しない。</summary>
        public static void Recover(RunState run, int amount)
        {
            if (run == null || amount <= 0) return;
            if (run.crownHopeLocked) return; // 冠: このラン希望0固定
            run.hope = Mathf.Min(run.hopeCap, run.hope + amount);
            UpdateMadnessState(run);
        }

        /// <summary>戦闘終了時に呼ぶ。開始時HP(combatStartHP)と現在HPを比較し、
        /// 収支マイナス→床定量を減少／非マイナス→わずか回復。
        /// lossMult: 暴食(トゥルハドの暴食)で 2。lossReduce: HopeLossReduce メタバフで損を減算。</summary>
        public static void ApplyCombatHpBalance(RunState run, float lossMult = 1f, int lossReduce = 0)
        {
            if (run == null) return;
            if (run.playerHP < run.combatStartHP)
            {
                int loss = Mathf.Max(0, Mathf.CeilToInt(CombatHopeLoss(run) * lossMult) - lossReduce);
                int before = run.hope;
                Reduce(run, loss);
                Stats.combatLoss += before - run.hope;
            }
            else
            {
                int before = run.hope;
                Recover(run, ComposureRecover);
                Stats.composureGain += run.hope - before;
            }
        }

        /// <summary>移動1回ぶんの希望処理。横移動(isLateral)で LateralCost、絶望的な進軍 有効で追加 -1。
        /// 発狂中は秒読みを1消費し、0到達で true(ラン終了)を返す。</summary>
        public static bool ApplyMove(RunState run, bool isLateral, bool despairMarchActive)
        {
            if (run == null) return false;
            if (isLateral)
            {
                int b = run.hope; Reduce(run, LateralCost); Stats.lateralLoss += b - run.hope;
            }
            if (despairMarchActive)
            {
                int b = run.hope; Reduce(run, DespairMarch); Stats.marchLoss += b - run.hope;
            }

            if (run.hope <= 0)
            {
                // 佯狂者の冠フルセット: 5移動死を撤廃。狂気スタック+1 → 最大HP-1（燃え尽き）。
                // 与ダメ+スタック×4% は YokyoCrownEffect 側で適用。最大HPが尽きたらラン終了。
                if (run.crownHopeLocked && YokyoSet.IsFullSet(run))
                {
                    run.madnessStack++;
                    run.playerMaxHP -= 1;
                    if (run.playerHP > run.playerMaxHP) run.playerHP = run.playerMaxHP;
                    return run.playerMaxHP <= 0; // 燃え尽きてラン終了
                }

                // それ以外（冠なし／冠単体＝賭け）: 5移動秒読みを1消費（0でラン終了）。
                if (run.madnessMoveCounter > 0)
                {
                    run.madnessMoveCounter--;
                    if (run.madnessMoveCounter <= 0) return true;
                }
            }
            return false;
        }

        /// <summary>悪選択（旧カルマ蓄積）の希望コストを適用。</summary>
        public static void ApplyEvilChoice(RunState run, int amount)
        {
            if (run == null) return;
            int b = run.hope; Reduce(run, amount); Stats.evilLoss += b - run.hope;
        }

        /// <summary>食料アイテム等による希望回復。</summary>
        public static void ApplyFood(RunState run, int amount)
        {
            if (run == null) return;
            int b = run.hope; Recover(run, amount); Stats.foodGain += run.hope - b;
        }

        /// <summary>ダイス振り直しの希望コスト(RerollCost)を支払えるか試みる。
        /// 払えたら希望を減算して true。払えなければ何もせず false（＝振り直し不可）。
        /// 終盤(低希望)ほど振り直せない＝「二度目が無い」緊張を生む（#1・ADR-0002連動）。</summary>
        public static bool TryPayReroll(RunState run)
        {
            if (run == null || run.hope < RerollCost) return false;
            int b = run.hope; Reduce(run, RerollCost); Stats.rerollLoss += b - run.hope;
            return true;
        }

        // === 内部 ===

        /// <summary>上限ロック: 45以下で上限45、20以下で上限20（一方向）。</summary>
        private static void UpdateCapLock(RunState run)
        {
            if (run.hope <= FloorDespair) run.hopeCap = Mathf.Min(run.hopeCap, FloorDespair);
            else if (run.hope <= FloorPessimism) run.hopeCap = Mathf.Min(run.hopeCap, FloorPessimism);
        }

        /// <summary>発狂状態: hope==0 で秒読み開始(初回のみ)、hope>=MadnessRecover で解除。
        /// 佯狂者の冠所持時は発狂で希望0固定(crownHopeLocked)。フルセット時は5移動死を使わない。</summary>
        private static void UpdateMadnessState(RunState run)
        {
            if (run.hope <= 0)
            {
                // 冠: 発狂したらこのラン希望0固定
                if (YokyoSet.HasCrown(run)) run.crownHopeLocked = true;

                if (YokyoSet.IsFullSet(run))
                {
                    run.madnessMoveCounter = -1; // フルセットは秒読み死なし（燃え尽きへ移行）
                }
                else if (run.madnessMoveCounter < 0)
                {
                    run.madnessMoveCounter = MadnessMoves; // 5移動秒読み（冠なし／冠単体＝賭け）
                }
            }
            else if (run.hope >= MadnessRecover)
            {
                run.madnessMoveCounter = -1; // 秒読みリセット（冠ロック中は hope>=10 にならない）
            }
        }
    }
}

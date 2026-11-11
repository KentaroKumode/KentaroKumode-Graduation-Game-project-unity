using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// 物資ゲージ（旧・希望。カルマ＋飢餓を統合した ADR-0002 の後継）のロジック。
    /// 状態は <see cref="RunState"/> 側（provision / provisionCap / combatStartHP）に持ち、
    /// このクラスは「読む」クエリと「動かす」ミューテータを提供する純ヘルパ（MonoBehaviour非依存）。
    /// 正本: docs/GAME.md §10。 規則の定数は <see cref="ProvisionRules"/>（素の C#）。 2026-11-09 にデノミ (1 歩 = 1) と段の撤去。
    ///
    /// <para><b>2026-09-28: 希望 → 物資。</b> 固定値はすべて ×10。 <b>ラチェット（45 / 20 以下で上限を固定）と
    /// 発狂（0 で 5 移動後にラン終了）は撤廃</b> ── 物資は燃料のように補給できる資源で、 0（払底）の間は
    /// 移動に HP を払う（<c>MapSystem.FreeMove.FreeMapSim</c>）。 横移動の概念も無くなった（自由移動）。</para>
    /// </summary>
    public static class ProvisionSystem
    {
        public const int ProvisionMax = ProvisionRules.Max;

        public const int EvilChoiceCost   = ProvisionRules.EvilChoiceCost; // 悪選択(旧カルマ+1相当)
        public const int ComposureRecover = 0; // 2026-06-04: 被弾0勝利の回復を撤廃

        // === クエリ ===

        public static ProvisionTier GetTier(RunState run)
            => run == null ? ProvisionTier.Ample : ProvisionRules.TierOf(run.provision);

        // 2026-11-09: 段のデバフ (疲労・苦悩・迷妄) と戦闘の物資の損を撤去 (GAME.md §10・§24)。

        /// <summary>払底（物資 0）か。 佯狂者シリーズの「発狂中」はこれを読む。</summary>
        public static bool IsExhausted(RunState run) => run != null && run.provision <= 0;

        /// <summary>状態の表示名。</summary>
        public static string TierName(ProvisionTier t) => t == ProvisionTier.Exhausted ? "払底" : "";

        // === 計測（オートラン集計専用・通常プレイでは無害な静的カウンタ） ===
        /// <summary>物資の増減を「発生源別」に積算する。実際に適用された(クランプ後の)量を計上する。
        /// AutoRunner が1ランごとに <see cref="Reset"/> し、終了時に読み取る。</summary>
        public static class Stats
        {
            public static int combatLoss;     // 戦闘HP収支マイナスによる損
            public static int composureGain;  // 被弾0勝利のわずか回復
            public static int travelLoss;     // 移動の消費（自由移動マップ・照明）
            public static int marchLoss;      // 絶望的な進軍(点に着くたび)
            public static int evilLoss;       // 悪選択コスト
            public static int foodGain;       // 食料回復
            public static int rerollLoss;     // ダイス振り直しコスト（#1）
            public static int supplyGain;     // 補給（戦闘の報酬・補給庫・前哨基地・ショップ）

            public static void Reset()
            {
                combatLoss = composureGain = travelLoss = marchLoss = evilLoss = foodGain = rerollLoss = supplyGain = 0;
            }
        }

        // === ミューテータ ===

        /// <summary>物資を減少（0でクランプ）。</summary>
        public static void Reduce(RunState run, int amount)
        {
            if (run == null || amount <= 0) return;
            run.provision = Mathf.Max(0, run.provision - amount);
            UpdateCrownLock(run);
        }

        /// <summary>物資を回復（上限 provisionCap でクランプ）。
        /// 佯狂者の冠で物資0固定中(crownProvisionLocked)は回復しない。</summary>
        public static void Recover(RunState run, int amount)
        {
            if (run == null || amount <= 0) return;
            if (run.crownProvisionLocked) return; // 冠: このラン物資0固定
            run.provision = Mathf.Min(run.provisionCap, run.provision + amount);
        }

        /// <summary>補給（戦闘の報酬・補給庫・前哨基地・ショップ）。 計測は supplyGain。</summary>
        public static void Supply(RunState run, int amount)
        {
            if (run == null) return;
            int b = run.provision; Recover(run, amount); Stats.supplyGain += run.provision - b;
        }

        /// <summary>補給拠点で物資 1 を買う値段 (G)。 ショップ (<c>FreeMapParams.ProvisionPrice</c>) より安い・在庫の数は無い (2026-11-11・仮置き)。</summary>
        public const int DepotPrice = 1;

        /// <summary>補給拠点で物資を 1 買う。 金が足りない・物資が満ちている時は false。</summary>
        public static bool TryBuyAtDepot(RunState run)
        {
            if (run == null || run.coins < DepotPrice || run.provision >= run.provisionCap || run.crownProvisionLocked) return false;
            run.coins -= DepotPrice; run.coinsSpent += DepotPrice;
            Supply(run, 1);
            return true;
        }

        /// <summary>移動の消費（自由移動マップが距離に応じて払う）。 計測は travelLoss。</summary>
        public static void SpendTravel(RunState run, int amount)
        {
            if (run == null || amount <= 0) return;
            int b = run.provision;
            run.provision = Mathf.Max(0, run.provision - amount);
            UpdateCrownLock(run);
            Stats.travelLoss += b - run.provision;
        }

        /// <summary>点に着いた時の物資処理。 佯狂者の冠フルセットで払底中なら狂気スタック+1 → 最大HP-1（燃え尽き）。
        /// 最大HPが尽きたら true(ラン終了)。 (旧〈絶望的な進軍〉の追加損は 2026-11-09 に撤去)</summary>
        public static bool ApplyArrival(RunState run)
        {
            if (run == null) return false;

            if (run.provision <= 0 && run.crownProvisionLocked && YokyoSet.IsFullSet(run))
            {
                // 与ダメ+スタック×4% は YokyoCrownEffect 側で適用。
                run.madnessStack++;
                run.playerMaxHP -= 1;
                if (run.playerHP > run.playerMaxHP) run.playerHP = run.playerMaxHP;
                return run.playerMaxHP <= 0; // 燃え尽きてラン終了
            }
            return false;
        }

        /// <summary>悪選択（旧カルマ蓄積）の物資コストを適用。</summary>
        public static void ApplyEvilChoice(RunState run, int amount)
        {
            if (run == null) return;
            int b = run.provision; Reduce(run, amount); Stats.evilLoss += b - run.provision;
        }

        /// <summary>食料アイテム等による物資回復。</summary>
        public static void ApplyFood(RunState run, int amount)
        {
            if (run == null) return;
            int b = run.provision; Recover(run, amount); Stats.foodGain += run.provision - b;
        }

        // === 内部 ===

        /// <summary>佯狂者の冠: 払底したらこのラン物資0固定。</summary>
        private static void UpdateCrownLock(RunState run)
        {
            if (run.provision <= 0 && YokyoSet.HasCrown(run)) run.crownProvisionLocked = true;
        }
    }
}

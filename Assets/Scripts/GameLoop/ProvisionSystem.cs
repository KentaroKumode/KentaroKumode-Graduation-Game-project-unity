using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// 物資ゲージ（旧・希望。カルマ＋飢餓を統合した ADR-0002 の後継）のロジック。
    /// 状態は <see cref="RunState"/> 側（provision / provisionCap / combatStartHP）に持ち、
    /// このクラスは「読む」クエリと「動かす」ミューテータを提供する純ヘルパ（MonoBehaviour非依存）。
    /// 正本: docs/GAME.md §10。 段階の閾値と損の表は <see cref="ProvisionRules"/>（素の C#）。
    ///
    /// <para><b>2026-09-28: 希望 → 物資。</b> 固定値はすべて ×10。 <b>ラチェット（45 / 20 以下で上限を固定）と
    /// 発狂（0 で 5 移動後にラン終了）は撤廃</b> ── 物資は燃料のように補給できる資源で、 0（払底）の間は
    /// 移動に HP を払う（<c>MapSystem.FreeMove.FreeMapSim</c>）。 横移動の概念も無くなった（自由移動）。</para>
    /// </summary>
    public static class ProvisionSystem
    {
        public const int ProvisionMax = ProvisionRules.Max;

        // 床: この値「以下」で各段に入る
        public const int FloorDwindling = ProvisionRules.FloorDwindling; // 目減り
        public const int FloorDepleting = ProvisionRules.FloorDepleting; // 枯渇し始める
        public const int FloorScarce    = ProvisionRules.FloorScarce;    // 枯渇寸前

        public const int EvilChoiceCost   = ProvisionRules.EvilChoiceCost; // 悪選択(旧カルマ+1相当)
        public const int RerollCost       = ProvisionRules.RerollCost;     // ダイス振り直し1回(毎ターン最大1回)
        public const int DespairMarch     = ProvisionRules.DespairMarch;   // 絶望的な進軍(旧Lv8): 点に着くたびの追加損
        public const int ComposureRecover = 0; // 2026-06-04: 被弾0勝利の回復を撤廃

        // 段のデバフの効果量
        // 疲労 (目減り以下): 15% で最終ダメージが半減する。
        //   2026-08-09 に **0 ダメージ → 半減** へ緩和。 0 ダメは「そのターンの攻撃が丸ごと
        //   消える」ため、 与ダメを積むビルドほど損失が大きく、 低い段を機械的に忌避させていた。
        public const float FatigueChance           = 0.15f; // 疲労が起きる確率
        public const float FatigueDamageMultiplier = 0.5f;  // 疲労時の最終ダメージ倍率
        public const float AnguishCritMultDelta    = -0.3f; // 苦悩: 会心倍率への加算補正 (2026-08-09 に -0.5 から緩和)
        public const int   DelusionDisableMin = 1;          // 迷妄: 戦闘開始時パッシブ無効の最小個数
        public const int   DelusionDisableMax = 3;          // 同・最大個数

        // === クエリ ===

        public static ProvisionTier GetTier(RunState run)
            => run == null ? ProvisionTier.Ample : ProvisionRules.TierOf(run.provision);

        /// <summary>HP収支マイナス1戦あたりの損（現在の段依存・定量）。</summary>
        public static int CombatProvisionLoss(RunState run) => ProvisionRules.CombatLoss(GetTier(run));

        /// <summary>疲労が起きる確率（目減り以下で 15%、それ以外 0）。
        /// 起きたときは最終ダメージに <see cref="FatigueDamageMultiplier"/> を掛ける。</summary>
        public static float GetFatigueChance(RunState run)
            => GetTier(run) >= ProvisionTier.Dwindling ? FatigueChance : 0f;

        /// <summary>苦悩: 会心倍率への加算補正（枯渇し始める以下で -0.3）。</summary>
        public static float GetCritMultiplierDelta(RunState run)
            => GetTier(run) >= ProvisionTier.Depleting ? AnguishCritMultDelta : 0f;

        /// <summary>迷妄: 戦闘開始時に無効化するパッシブ個数（枯渇寸前以下で 1-3、それ以外 0）。</summary>
        public static int RollPassiveDisableCount(RunState run, System.Random rng = null)
        {
            if (GetTier(run) < ProvisionTier.Scarce) return 0;
            return rng != null
                ? rng.Next(DelusionDisableMin, DelusionDisableMax + 1)
                : GameLoop.GameRng.RangeAuto("HopeSystem.1", DelusionDisableMin, DelusionDisableMax + 1);
        }

        /// <summary>払底（物資 0）か。 佯狂者シリーズの「発狂中」はこれを読む。</summary>
        public static bool IsExhausted(RunState run) => run != null && run.provision <= 0;

        /// <summary>段の表示名。</summary>
        public static string TierName(ProvisionTier t)
        {
            switch (t)
            {
                case ProvisionTier.Ample:     return "充足";
                case ProvisionTier.Dwindling: return "目減り";
                case ProvisionTier.Depleting: return "枯渇し始める";
                case ProvisionTier.Scarce:    return "枯渇寸前";
                default:                      return "払底";
            }
        }

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

        /// <summary>物資を減少（0でクランプ）。
        /// 異常現象「薄れる人」 発動中は追加で損 (ただし amount==0 ではトリガしない)。</summary>
        public static void Reduce(RunState run, int amount)
        {
            if (run == null || amount <= 0) return;
            int extra = MapSystem.AbyssPhenomena.AbyssPhenomenonCombatHooks.OnProvisionReduceExtraLoss(run);
            run.provision = Mathf.Max(0, run.provision - amount - extra);
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

        /// <summary>移動の消費（自由移動マップが距離に応じて払う）。 計測は travelLoss。
        /// **異常現象「薄れる人」の追加損は乗せない** ── 刻みごとに払うので、 1 回ごとの追加損が
        /// 移動距離に比例して膨れ上がる。</summary>
        public static void SpendTravel(RunState run, int amount)
        {
            if (run == null || amount <= 0) return;
            int b = run.provision;
            run.provision = Mathf.Max(0, run.provision - amount);
            UpdateCrownLock(run);
            Stats.travelLoss += b - run.provision;
        }

        /// <summary>戦闘終了時に呼ぶ。開始時HP(combatStartHP)と現在HPを比較し、
        /// 収支マイナス→段の定量を減少／非マイナス→わずか回復。
        /// lossMult: 暴食(トゥルハドの暴食)で 2。lossReduce: ProvisionLossReduce メタバフで損を減算。</summary>
        public static void ApplyCombatHpBalance(RunState run, float lossMult = 1f, int lossReduce = 0)
        {
            if (run == null) return;
            if (run.playerHP < run.combatStartHP)
            {
                // 挑戦デバフ 軸5〈絶望的な戦闘〉T1: 戦闘後の物資減少を追加。
                // **軽減 (lossReduce) より後に足す** ── 軽減で打ち消せてしまうと T1 が無効化される。
                int loss = Mathf.Max(0, Mathf.CeilToInt(CombatProvisionLoss(run) * lossMult) - lossReduce)
                         + MetaProgression.MetaDebuffApplicator.GetPostCombatProvisionLoss(run);
                int before = run.provision;
                Reduce(run, loss);
                Stats.combatLoss += before - run.provision;
            }
            else
            {
                int before = run.provision;
                Recover(run, ComposureRecover);
                Stats.composureGain += run.provision - before;
            }
        }

        /// <summary>点に着いた時の物資処理（旧・移動 1 回ぶん）。 絶望的な進軍 有効で追加損。
        /// 佯狂者の冠フルセットで払底中なら狂気スタック+1 → 最大HP-1（燃え尽き）。
        /// 最大HPが尽きたら true(ラン終了)。 **発狂の 5 移動秒読みは撤廃**（2026-09-28）。</summary>
        public static bool ApplyArrival(RunState run, bool despairMarchActive)
        {
            if (run == null) return false;
            if (despairMarchActive)
            {
                int b = run.provision; Reduce(run, DespairMarch); Stats.marchLoss += b - run.provision;
            }

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

        /// <summary>ダイス振り直しの物資コスト(RerollCost)を支払えるか試みる。
        /// 払えたら物資を減算して true。払えなければ何もせず false（＝振り直し不可）。</summary>
        public static bool TryPayReroll(RunState run)
        {
            if (run == null || run.provision < RerollCost) return false;
            int b = run.provision; Reduce(run, RerollCost); Stats.rerollLoss += b - run.provision;
            return true;
        }

        // === 内部 ===

        /// <summary>佯狂者の冠: 払底したらこのラン物資0固定。</summary>
        private static void UpdateCrownLock(RunState run)
        {
            if (run.provision <= 0 && YokyoSet.HasCrown(run)) run.crownProvisionLocked = true;
        }
    }
}

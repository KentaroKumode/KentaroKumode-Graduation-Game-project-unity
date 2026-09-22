using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AutoTest;
using AutoTest.Ultra;

namespace AutoTest.EditorTools
{
    /// <summary>
    /// Tools > AutoRun メニュー。SampleScene を開いて PlayMode に入り、
    /// AutoRunner を自動生成して N ラン連続実行 → ログ出力 → PlayMode を抜ける。
    /// </summary>
    [InitializeOnLoad]
    public static class AutoRunMenu
    {
        private const string ScenePath = "Assets/Scenes/SampleScene2.unity";
        private const string PendingKey = "AutoRun.PendingCount";
        private const string SweepKey   = "AutoRun.Boss5Sweep";      // SessionState
        private const string LambdaKey  = "AutoRun.LambdaFarmSweep"; // SessionState
        private const string LoopKey    = "AutoRun.AutoLoopBatches"; // SessionState
        private const string MutualKey  = "AutoRun.UseMutualAttack"; // EditorPrefs
        private const string ChalSweepKey  = "AutoRun.ChalSweep";  // SessionState (固定難易度スイープ)
        private const string SkillCmpKey     = "AutoRun.SkillCmp";     // 技量帯スイープ
        private const string SkillCmp3Key    = "AutoRun.SkillCmp3";    // 素朴を含める
        private const string SkillCmpRunsKey = "AutoRun.SkillCmpRuns";
        private const string SkillCmpScoreKey = "AutoRun.SkillCmpScore";
        private const string SkillCmpBudgetKey = "AutoRun.SkillCmpBudget"; // 天井 新旧 (要求戦力の有無)
        private const string ChalOneScoreKey = "AutoRun.ChalOneScore"; // 単点測定: 挑戦スコア
        private const string ChalOneRunsKey  = "AutoRun.ChalOneRuns";  // 単点測定: ラン数
        // 複数点の固定難易度スイープを、 既定の {0,10,20,30,40,50} から差し替えるための上書き。
        //   ChalOneScoreKey が「1 点だけ」なのに対し、 こちらは点の集合ごと置き換える。
        private const string ChalSweepScoresKey = "AutoRun.ChalSweepScores"; // "0,30,50" 形式
        private const string ChalSweepRunsKey   = "AutoRun.ChalSweepRuns";
        private const string RelicSweepKey = "AutoRun.RelicSweep"; // SessionState (遺物プリセットスイープ)
        private const string RelicBaseKey  = "AutoRun.RelicBaseline"; // SessionState (遺物なし基準値のみ)
        private const string AxisSweepKey  = "AutoRun.RelicAxisSweep"; // SessionState (遺物 単軸スイープ)
        private const string GrantTrialKey     = "AutoRun.RandomGrantTrial";
        private const string GrantTrialRunsKey = "AutoRun.RandomGrantRuns";
        private const string GrantAllKindsKey  = "AutoRun.RandomGrantAllKinds";  // 消耗品/武器/パーツも配る
        private const string GrantItemsKey     = "AutoRun.RandomGrantItems";     // 期待付与数 (×100 で保持)
        private const string ItemAblKey    = "AutoRun.ItemAblation";     // SessionState (アイテム アブレーション)
        private const string IttKey        = "AutoRun.UseIttBeta";       // EditorPrefs (購入優先度を ITT にする)
        private const string ChalTargetKey = "AutoRun.ChallengeTarget";  // EditorPrefs (通常バッチの挑戦pt固定)
        private const string RobBlockKey = "AutoRun.RobberyBlocksShops"; // EditorPrefs (強盗の罰を旧「出禁」に戻す)
        private const string RankSpecKey   = "AutoRun.MetaRankSpec";    // EditorPrefs (パネル配分の直接指定)
        private const string MetaAblKey    = "AutoRun.MetaAxisAblation"; // SessionState (パネル drop-one)
        private const string KeystoneKey   = "AutoRun.KeystoneSweep";    // SessionState (極点 r10 比較)
        private const string RankMarginKey = "AutoRun.RankMarginSweep";  // SessionState (r9 vs r10 切り分け)
        private const string YieldBenchKey = "AutoRun.YieldBenchmark";   // SessionState (スループット計測)
        private const string ItemAblIdsKey = "AutoRun.ItemAblationIds";
        private const string ItemAblRunsKey = "AutoRun.ItemAblationRuns";
        private const string ChalAxisKey   = "AutoRun.ChalAxisSweep";  // SessionState (挑戦 単軸スイープ)
        private const string ChalAxisNoRelicKey = "AutoRun.ChalAxisNoRelic";
        private const string ChalAxisChangedKey = "AutoRun.ChalAxisChanged";
        private const string ChalCatKey = "AutoRun.ChalCategorySweep";
        private const string ChalCatT4Key = "AutoRun.ChalCategoryT4Only";
        private const string ChalCatOnlyKey = "AutoRun.ChalCategoryOnlyCat"; // SessionState (固着再現: 1カテゴリのみ)
        private const string ChalCatAloneKey = "AutoRun.ChalCategoryT4Alone"; // SessionState (診断: T4 単独)
        private const string ChalAxisSeedBaseKey = "AutoRun.ChalAxisSeedBase"; // SessionState (別シード帯での再測定)
        private const string ChalAxisMaxKey = "AutoRun.ChalAxisMaxOnly"; // SessionState (最上位段のみ)
        private const string ChalAxisSmokeKey = "AutoRun.ChalAxisSmoke"; // SessionState (動作確認: 少ラン・ログ有)
        private const string ChalAxisOnlyKey  = "AutoRun.ChalAxisOnly";  // SessionState (1軸だけ: Axes の添字)
        private const string ChalAxisDetKey   = "AutoRun.ChalAxisDeterminism"; // SessionState (決定性検査: 0pt アーム数)
        private const string ChalAxisDiffKey  = "AutoRun.ChalAxisDiff";        // SessionState (1ラン差分: ログ有・1ラン)
        private const string ChalAxisPriceKey = "AutoRun.ChalAxisPriceCurve";  // SessionState (価格弾性曲線)
        private const string OptimalParallelBenchKey = "AutoRun.OptimalParallelBench"; // SessionState
        private const string AxisDiagKey   = "AutoRun.RelicAxisDiag";  // SessionState (単軸の診断: 2アーム + jsonl)
        private const string PersonaKey    = "AutoRun.PersonaSweep";   // SessionState (ビルド別勝率スイープ)
        private const string AscendKey  = "AutoRun.Ascension";        // SessionState (周回モード)
        private const string AscendRunsKey = "AutoRun.AscensionRuns"; // SessionState (1ペルソナのラン数)
        private const string TierCalScoreKey = "AutoRun.TierCalibrationScore";
        private const string TierCalRunsKey  = "AutoRun.TierCalibrationRuns";
        private const string TierCalBatchesKey = "AutoRun.TierCalibrationBatches";
        private const string TierCalResumeKey = "AutoRun.TierCalibrationResumeAfterReload";
        private const string UltraStandard50WithRelic10KKey = "AutoRun.UltraStandard50WithRelic10K";
        /// <summary>診断: Ultra の dispatch 経路に smoke sink を差す (0=なし/1=先頭/2=末尾)。</summary>
        private const string UltraSmokeSinkKey = "AutoRun.UltraSmokeSink";
        /// <summary>診断: 実ランの途中で checkpoint を書き出す先。</summary>
        private const string UltraCaptureKey = "AutoRun.UltraCaptureCheckpoint";
        /// <summary>診断: checkpoint から resume する (Editor 内・コンソールが見える)。</summary>
        private const string UltraResumeKey = "AutoRun.UltraResumeCheckpoint";
        /// <summary>診断: マクロ決定を数えるだけ (判断は一切変えない)。</summary>
        private const string UltraCensusKey = "AutoRun.UltraDecisionCensus";
        /// <summary>本番: rollout で決める実バッチのラン数 (0=使わない)。</summary>
        private const string UltraRolloutRunsKey = "AutoRun.UltraRolloutRuns";
        /// <summary>本番: 1 手あたりの rollout 本数。</summary>
        private const string UltraRolloutPerActionKey = "AutoRun.UltraRolloutPerAction";
        /// <summary>階層DP航行の割引率 ×100 (0 = 使わない)。</summary>
        private const string FloorDpKey = "AutoRun.FloorDpDiscountPct";
        /// <summary>階層DP航行の HP 量子化帯数 (0 = 既定 11)。</summary>
        private const string FloorDpBandsKey = "AutoRun.FloorDpHpBands";
        /// <summary>浅い評価が同点だらけのときに広げる配線候補枠 (0 = 既定 OFF)。</summary>
        private const string TieExpandKey = "AutoRun.TieExpandCandidates";
        /// <summary>同点の中身を数える診断 (既定 OFF)。 判断は変えない。</summary>
        private const string SatCensusKey = "AutoRun.SaturationCensus";
        /// <summary>chargeRerollGain ×100 (0 = 既定 0.30 のまま触らない)。</summary>
        private const string RerollGainKey = "AutoRun.ChargeRerollGainPct";
        /// <summary>7層の役札持ち越しを**切る** (既定は持ち越し ON なので、これは旧仕様の対照用)。</summary>
        private const string VescaKeepRolesKey = "AutoRun.VescaKeepRolesOff";
        /// <summary>リロールを全列挙する最大個数 (0 = 既定の標本抽出のまま)。</summary>
        private const string ExactRerollKey = "AutoRun.ExactRerollMaxDice";
        /// <summary>winHpWeight ×100 (0 = 既定 0.5 のまま触らない)。</summary>
        private const string WinHpWeightKey = "AutoRun.WinHpWeightPct";
        /// <summary>ロールアウト内方策の ttk 価格 ×10 (0 = 既定 4.0 のまま触らない)。</summary>
        private const string TtkWeightKey = "AutoRun.RolloutTtkWeightX10";
        /// <summary>マクロ判断のアブレーション対象 (0 = 潰さない / 1 = 航行)。</summary>
        private const string AblateKey = "AutoRun.AblateAxis";
        /// <summary>不偏 lift 混入の縮小係数 K (0 = 混ぜない)。</summary>
        private const string ExploreBlendKey = "AutoRun.ExploreBlendK";
        /// <summary>7層の戦闘方策を Optimal へ切り替える (既定 OFF＝Super のまま)。</summary>
        private const string Layer7OptimalKey = "AutoRun.Layer7OptimalOn";
        /// <summary>ttd の頭打ちターン数 (0 = 無効・従来どおり)。</summary>
        private const string TtdCapKey = "AutoRun.TtdCapTurns";
        /// <summary>決定seed の一時上書き (再現確認用・1 回で消費)。</summary>
        private const string SeedOverrideKey = "AutoRun.SeedOverride";

        // 2026-07-28 整理: 散らばっていた選択欄を 3 グループへ統合。
        //   ① メタバフ     … 3 択・排他
        //   ② アイテム選択 … 2 択・排他
        //   ③ 学習         … 独立トグル (同時選択可・単独可)
        private const string MetaModeKey  = "AutoRun.MetaBuffMode";     // EditorPrefs (①)
        private const string MetaAxisKey  = "AutoRun.MetaBuildAxis";    // EditorPrefs (①の軸)
        private const string SweepAxisKey = "AutoRun.SweepAllMetaAxes"; // EditorPrefs (①の軸を一斉走査)
        private const string DebuffKey    = "AutoRun.EnableAllDebuffs"; // EditorPrefs
        private const string ItemModeKey  = "AutoRun.ItemPickMode";     // EditorPrefs (②)
        private const string RatioKey     = "AutoRun.PersonaRawTierRatio";
        private const string TuneBossKey  = "AutoRun.LearnBoss";        // EditorPrefs (③)
        private const string LearnTierKey = "AutoRun.LearnTier";        // EditorPrefs (③)
        private const string LearnAiKey   = "AutoRun.LearnBotAi";       // EditorPrefs (③)

        // ---- ① メタバフ (排他) ----
        private const string M1A = "Tools/AutoRun/① メタバフ/ビルド軸を使用";
        private const string M1B = "Tools/AutoRun/① メタバフ/標準を使用 (Balanced・ボス調整の基準)";
        private const string M1C = "Tools/AutoRun/① メタバフ/オフ (0pt)";
        private const string M1D = "Tools/AutoRun/① メタバフ/メタデバフ全ON (最高難易度・独立)";

        // ---- ①の軸 (ビルド軸使用時のみ意味を持つ) ----
        private const string AXALL = "Tools/AutoRun/① メタバフ軸/【一斉走査】全軸を比較 (ラン単位ラウンドロビン)";
        private const string AX1 = "Tools/AutoRun/① メタバフ軸/火力 (Offense)";
        private const string AX2 = "Tools/AutoRun/① メタバフ軸/耐久 (Defense)";
        private const string AX3 = "Tools/AutoRun/① メタバフ軸/経済 (Economy)";
        private const string AX4 = "Tools/AutoRun/① メタバフ軸/種火 (SparkBuild)";

        // ---- ② アイテム選択 (排他) ----
        private const string M2A = "Tools/AutoRun/② アイテム選択/ビルド軸に";
        private const string M2B = "Tools/AutoRun/② アイテム選択/Tier軸に";

        // ---- ③ 学習 (同時選択可・単独可) ----
        private const string M3A = "Tools/AutoRun/③ 学習/ボスチューナー ON";
        private const string M3B = "Tools/AutoRun/③ 学習/Tier学習 ON";
        private const string M3C = "Tools/AutoRun/③ 学習/BOT学習 ON";

        // ---- ④ 配線技量 (排他・ADR-0010 Verification ①) ----
        private const string M4N = "Tools/AutoRun/④ 配線技量/素朴 (役もリロールも使わない・対照群)";
        private const string M4O = "Tools/AutoRun/④ 配線技量/最適 (3^5全列挙・役とリロールを使う)";
        private const string M4S = "Tools/AutoRun/④ 配線技量/天井 (戦闘決着まで先読み・未来の出目は読まない)";
        private const string M4U = "Tools/AutoRun/④ 配線技量/Ultra (production worker・ラン全体最適化)";
        private const string WiringSkillKey = "AutoRun.WiringSkill";

        // ---- 対照群スイッチ: 軽減無視をシールドで肩代わりするか (2026-08-15) ----
        //   既定 ON = 製品挙動。 OFF は **変更前の値を採るためだけ**の退避経路。
        private const string ShieldChipKey = "AutoRun.ShieldAbsorbsUnmitigable";
        // ---- ⑥ 遺物なし強制 (2026-08-17) ----
        //   既定 OFF ＝ 保存された遺物を引き継ぐ (「無し」ではなく「触らない」)。
        //   バランスの正式な基準は遺物なしなので、 基準値を採るときは ON にする。
        //   **サマリー冒頭の `[実効状態]` 行で必ず実効値を確認すること。**
        private const string NoRelicKey = "AutoRun.ForceNoRelic";
        private const string MNR = "Tools/AutoRun/⑥ 遺物/遺物なしを強制 (基準条件)";
        [MenuItem(MNR, priority = 140)]
        private static void ToggleNoRelic()
            => EditorPrefs.SetBool(NoRelicKey, !EditorPrefs.GetBool(NoRelicKey, false));
        [MenuItem(MNR, validate = true)]
        private static bool ToggleNoRelicV()
        { Menu.SetChecked(MNR, EditorPrefs.GetBool(NoRelicKey, false)); return true; }

        // ---- ⑥-b 遺物 理論値固定 (2026-09-04) ----
        //   アイテム学習用。 遺物なしだと 6F/7F 到達が薄く、 終盤の情報が溜まらない。
        //   遺物なし強制と同時に立てた場合は**遺物なしが勝つ** (AutoRunner 側で分岐)。
        private const string TheoRelicKey = "AutoRun.ForceTheoreticalRelic";

        // ---- 計測: 武器家系の固定 (2026-09-05) ----
        //   職業4種均等配分 → 各家系 約25% のランで担がれる = 4家系を同じ厚みで測れる。
        private const string LockFamKey = "AutoRun.LockWeaponFamily";
        private const string MLF = "Tools/AutoRun/⑥ 遺物/武器家系を固定 (計測条件)";
        [MenuItem(MLF, priority = 142)]
        private static void ToggleLockFam()
            => EditorPrefs.SetBool(LockFamKey, !EditorPrefs.GetBool(LockFamKey, false));
        [MenuItem(MLF, validate = true)]
        private static bool ToggleLockFamV()
        { Menu.SetChecked(MLF, EditorPrefs.GetBool(LockFamKey, false)); return true; }
        private const string MTR = "Tools/AutoRun/⑥ 遺物/遺物を理論値で固定 (学習条件)";
        [MenuItem(MTR, priority = 141)]
        private static void ToggleTheoRelic()
            => EditorPrefs.SetBool(TheoRelicKey, !EditorPrefs.GetBool(TheoRelicKey, false));
        [MenuItem(MTR, validate = true)]
        private static bool ToggleTheoRelicV()
        { Menu.SetChecked(MTR, EditorPrefs.GetBool(TheoRelicKey, false)); return true; }

        private const string MSC = "Tools/AutoRun/⑤ 対照群/軽減無視をシールドで肩代わりしない (変更前の挙動)";
        [MenuItem(MSC, priority = 130)]
        private static void ToggleShieldChip()
            => EditorPrefs.SetBool(ShieldChipKey, !EditorPrefs.GetBool(ShieldChipKey, false));
        [MenuItem(MSC, validate = true)]
        private static bool ToggleShieldChipV()
        { Menu.SetChecked(MSC, EditorPrefs.GetBool(ShieldChipKey, false)); return true; }


        static AutoRunMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        // ===== ① メタバフ =====

        private static AutoRunner.MetaBuffMode MetaMode
        {
            get => (AutoRunner.MetaBuffMode)EditorPrefs.GetInt(MetaModeKey, (int)AutoRunner.MetaBuffMode.Standard);
            set => EditorPrefs.SetInt(MetaModeKey, (int)value);
        }
        private static MetaAllocationPresets.Preset MetaAxis
        {
            get => (MetaAllocationPresets.Preset)EditorPrefs.GetInt(MetaAxisKey, (int)MetaAllocationPresets.Preset.Offense);
            set => EditorPrefs.SetInt(MetaAxisKey, (int)value);
        }

        [MenuItem(M1A, priority = 100)] private static void SetM1A() { MetaMode = AutoRunner.MetaBuffMode.BuildFocused; }
        [MenuItem(M1A, validate = true)] private static bool VM1A()
        { Menu.SetChecked(M1A, MetaMode == AutoRunner.MetaBuffMode.BuildFocused); return true; }

        [MenuItem(M1B, priority = 101)] private static void SetM1B() { MetaMode = AutoRunner.MetaBuffMode.Standard; }
        [MenuItem(M1B, validate = true)] private static bool VM1B()
        { Menu.SetChecked(M1B, MetaMode == AutoRunner.MetaBuffMode.Standard); return true; }

        [MenuItem(M1C, priority = 102)] private static void SetM1C() { MetaMode = AutoRunner.MetaBuffMode.Off; }
        [MenuItem(M1C, validate = true)] private static bool VM1C()
        { Menu.SetChecked(M1C, MetaMode == AutoRunner.MetaBuffMode.Off); return true; }

        [MenuItem(M1D, priority = 110)] private static void SetM1D()
        { EditorPrefs.SetBool(DebuffKey, !EditorPrefs.GetBool(DebuffKey, false)); }
        [MenuItem(M1D, validate = true)] private static bool VM1D()
        { Menu.SetChecked(M1D, EditorPrefs.GetBool(DebuffKey, false)); return true; }

        // 一斉走査は「軸を1つ選ぶ」の代わりに立てるトグル。 ON の間、個別軸の選択は無視される。
        [MenuItem(AXALL, priority = 119)] private static void SetAXALL()
        { EditorPrefs.SetBool(SweepAxisKey, !EditorPrefs.GetBool(SweepAxisKey, false)); }
        [MenuItem(AXALL, validate = true)] private static bool VAXALL()
        { Menu.SetChecked(AXALL, EditorPrefs.GetBool(SweepAxisKey, false)); return true; }

        [MenuItem(AX1, priority = 120)] private static void SetAX1() { MetaAxis = MetaAllocationPresets.Preset.Offense; }
        [MenuItem(AX1, validate = true)] private static bool VAX1()
        { Menu.SetChecked(AX1, MetaAxis == MetaAllocationPresets.Preset.Offense); return true; }
        [MenuItem(AX2, priority = 121)] private static void SetAX2() { MetaAxis = MetaAllocationPresets.Preset.Defense; }
        [MenuItem(AX2, validate = true)] private static bool VAX2()
        { Menu.SetChecked(AX2, MetaAxis == MetaAllocationPresets.Preset.Defense); return true; }
        [MenuItem(AX3, priority = 122)] private static void SetAX3() { MetaAxis = MetaAllocationPresets.Preset.Economy; }
        [MenuItem(AX3, validate = true)] private static bool VAX3()
        { Menu.SetChecked(AX3, MetaAxis == MetaAllocationPresets.Preset.Economy); return true; }
        [MenuItem(AX4, priority = 123)] private static void SetAX4() { MetaAxis = MetaAllocationPresets.Preset.SparkBuild; }
        [MenuItem(AX4, validate = true)] private static bool VAX4()
        { Menu.SetChecked(AX4, MetaAxis == MetaAllocationPresets.Preset.SparkBuild); return true; }
        // AX5 (PrecisionApex) は 2026-09-10 の精密トラック撤去に伴い廃止。

        // ===== ② アイテム選択 =====

        private static AutoRunner.ItemPickMode ItemMode
        {
            get => (AutoRunner.ItemPickMode)EditorPrefs.GetInt(ItemModeKey, (int)AutoRunner.ItemPickMode.TierBased);
            set => EditorPrefs.SetInt(ItemModeKey, (int)value);
        }

        [MenuItem(M2A, priority = 200)] private static void SetM2A() { ItemMode = AutoRunner.ItemPickMode.BuildFocused; }
        [MenuItem(M2A, validate = true)] private static bool VM2A()
        { Menu.SetChecked(M2A, ItemMode == AutoRunner.ItemPickMode.BuildFocused); return true; }

        [MenuItem(M2B, priority = 201)] private static void SetM2B() { ItemMode = AutoRunner.ItemPickMode.TierBased; }
        [MenuItem(M2B, validate = true)] private static bool VM2B()
        { Menu.SetChecked(M2B, ItemMode == AutoRunner.ItemPickMode.TierBased); return true; }

        private const string MenuRatio25 = "Tools/AutoRun/② ビルド軸 Tier信奉率/25%";
        private const string MenuRatio50 = "Tools/AutoRun/② ビルド軸 Tier信奉率/50% (推奨)";
        private const string MenuRatio75 = "Tools/AutoRun/② ビルド軸 Tier信奉率/75%";
        [MenuItem(MenuRatio25, priority = 210)] private static void SetRatio25() { EditorPrefs.SetInt(RatioKey, 25); }
        [MenuItem(MenuRatio25, validate = true)] private static bool SetRatio25V()
        { Menu.SetChecked(MenuRatio25, EditorPrefs.GetInt(RatioKey, 50) == 25); return true; }
        [MenuItem(MenuRatio50, priority = 211)] private static void SetRatio50() { EditorPrefs.SetInt(RatioKey, 50); }
        [MenuItem(MenuRatio50, validate = true)] private static bool SetRatio50V()
        { Menu.SetChecked(MenuRatio50, EditorPrefs.GetInt(RatioKey, 50) == 50); return true; }
        [MenuItem(MenuRatio75, priority = 212)] private static void SetRatio75() { EditorPrefs.SetInt(RatioKey, 75); }
        [MenuItem(MenuRatio75, validate = true)] private static bool SetRatio75V()
        { Menu.SetChecked(MenuRatio75, EditorPrefs.GetInt(RatioKey, 50) == 75); return true; }

        // ===== ③ 学習 (同時選択可・単独可) =====

        [MenuItem(M3A, priority = 300)] private static void SetM3A()
        { EditorPrefs.SetBool(TuneBossKey, !EditorPrefs.GetBool(TuneBossKey, false)); }
        [MenuItem(M3A, validate = true)] private static bool VM3A()
        { Menu.SetChecked(M3A, EditorPrefs.GetBool(TuneBossKey, false)); return true; }

        [MenuItem(M3B, priority = 301)] private static void SetM3B()
        { EditorPrefs.SetBool(LearnTierKey, !EditorPrefs.GetBool(LearnTierKey, false)); }
        [MenuItem(M3B, validate = true)] private static bool VM3B()
        { Menu.SetChecked(M3B, EditorPrefs.GetBool(LearnTierKey, false)); return true; }

        [MenuItem(M3C, priority = 302)] private static void SetM3C()
        { EditorPrefs.SetBool(LearnAiKey, !EditorPrefs.GetBool(LearnAiKey, false)); }
        [MenuItem(M3C, validate = true)] private static bool VM3C()
        { Menu.SetChecked(M3C, EditorPrefs.GetBool(LearnAiKey, false)); return true; }


        // ===== ④ 配線技量 =====
        //   同一シードで素朴/最適を回し、 クリア率の差を McNemar で見る。
        //   その差が「役システムがプレイヤーに開けた伸びしろ」の実測値になる。

        private static AutoRunner.WiringSkill Skill
        {
            get => (AutoRunner.WiringSkill)EditorPrefs.GetInt(
                WiringSkillKey, (int)AutoRunner.WiringSkill.Optimal);
            set => EditorPrefs.SetInt(WiringSkillKey, (int)value);
        }

        [MenuItem(M4N, priority = 400)] private static void SetM4N() { Skill = AutoRunner.WiringSkill.Naive; }
        [MenuItem(M4N, validate = true)] private static bool VM4N()
        { Menu.SetChecked(M4N, Skill == AutoRunner.WiringSkill.Naive); return true; }
        [MenuItem(M4O, priority = 401)] private static void SetM4O() { Skill = AutoRunner.WiringSkill.Optimal; }
        [MenuItem(M4O, validate = true)] private static bool VM4O()
        { Menu.SetChecked(M4O, Skill == AutoRunner.WiringSkill.Optimal); return true; }
        // 天井: 1 ターンあたりの計算量が 2 桁上がる。 300 ラン級のスイープは覚悟して回すこと。
        [MenuItem(M4S, priority = 402)] private static void SetM4S() { Skill = AutoRunner.WiringSkill.Super; }
        [MenuItem(M4S, validate = true)] private static bool VM4S()
        { Menu.SetChecked(M4S, Skill == AutoRunner.WiringSkill.Super); return true; }
        // 厳密: 偶然ノードを全列挙する参照実装。 **バランス測定には使わない** ──
        //   天井の位置を測るためだけのもので、 速度は桁で落ちる。 数ラン単位で回す。
        private const string M4E = "Tools/AutoRun/④ 配線技量/厳密 (expectimax・天井の位置を測る専用)";
        [MenuItem(M4E, priority = 403)] private static void SetM4E() { Skill = AutoRunner.WiringSkill.Exact; }
        [MenuItem(M4E, validate = true)] private static bool VM4E()
        { Menu.SetChecked(M4E, Skill == AutoRunner.WiringSkill.Exact); return true; }
        [MenuItem(M4U, priority = 404)] private static void SetM4U() { Skill = AutoRunner.WiringSkill.Ultra; }
        [MenuItem(M4U, validate = true)] private static bool VM4U()
        { Menu.SetChecked(M4U, Skill == AutoRunner.WiringSkill.Ultra); return true; }

        [MenuItem("Tools/AutoRun/診断: 厳密AI 速度計測 (3ラン・0pt・遺物なし)", priority = 10)]
        public static void RunExactBenchmark()
        {
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Exact;
            Launch(3);
        }


        // ===== ADR-0009 相互攻撃パイプライン (トグル) =====

        private const string MenuMutual = "Tools/AutoRun/ADR-0009 相互攻撃パイプライン (トグル)";
        [MenuItem(MenuMutual, priority = 40)]
        private static void ToggleMutual() { EditorPrefs.SetBool(MutualKey, !EditorPrefs.GetBool(MutualKey, false)); }
        [MenuItem(MenuMutual, validate = true)]
        private static bool ToggleMutualValidate()
        { Menu.SetChecked(MenuMutual, EditorPrefs.GetBool(MutualKey, false)); return true; }

        // ===== ラン起動 =====

        [MenuItem("Tools/AutoRun/Run 10 runs", priority = 0)]
        public static void Run10() => Launch(10);

        [MenuItem("Tools/AutoRun/Run 100 runs", priority = 1)]
        public static void Run100() => Launch(100);

        [MenuItem("Tools/AutoRun/Run 1000 runs", priority = 2)]
        public static void Run1000() => Launch(1000);

        [MenuItem("Tools/AutoRun/Run 3000 runs", priority = 3)]
        public static void Run3000() => Launch(3000);

        [MenuItem("Tools/AutoRun/Run 10000 runs", priority = 4)]
        public static void Run10000() => Launch(10000);

        [MenuItem("Tools/AutoRun/Run custom...", priority = 5)]
        public static void RunCustom()
        {
            int n = AutoRunCountWindow.Ask(50);
            if (n > 0) Launch(n);
        }

        // ===== 自動周回モード (1000ラン × N回、 各バッチ間で L1/L2 自動学習) =====

        [MenuItem("Tools/AutoRun/自動周回: 1000ラン × 5回 (約5-10分)", priority = 5)]
        public static void RunAutoLoop5() => Launch(1000, loopBatches: 5);

        [MenuItem("Tools/AutoRun/自動周回: 1000ラン × 10回 (約10-20分)", priority = 6)]
        public static void RunAutoLoop10() => Launch(1000, loopBatches: 10);

        [MenuItem("Tools/AutoRun/自動周回: 1000ラン × 30回 (約30-60分)", priority = 7)]
        public static void RunAutoLoop30() => Launch(1000, loopBatches: 30);

        /// <summary>**アイテムパワー計測用の長時間周回 (2026-09-04)。**
        /// 5000 ラン × 50 回 = 25 万ラン。 `MaxBatchesRetained = 50` にちょうど収まる刻みで、
        /// 層化ホールドアウト下で LEGENDARY 1 品あたり n≒160 (Δ=0.80 の検出力) に届く。
        ///
        /// <para><b>runs.jsonl は書かない</b> (count>=3000 の分岐で抑止)。
        /// 1000 ラン で 12.1MB なので、 書くと 3GB になる。</para></summary>
        [MenuItem("Tools/AutoRun/計測周回: 5000ラン × 50回 (パワー測定・ログ最小)", priority = 8)]
        public static void RunMeasureLoop() => Launch(5000, loopBatches: 50);

        /// <summary>**アイテム学習の本走 (2026-09-04 再設計後)。**
        /// 条件を 1 箇所で全部立てる ── トグルを個別に押す運用は、 過去に
        /// 「BOT学習を ON にしたが item_stats.json を書くのは Tier学習だった」で
        /// 1 万ランを無駄にしている。 **条件は名前ではなくここで決める。**
        ///
        /// <para>条件: Tier学習 ON / 家系Lvボーナス OFF (既定) / **遺物なしを強制** /
        /// <b>カタログの家系構造 (19家系×約4段=74品) はそのまま</b> ── OFF なのは BOT スコアに乗る Lv 補正だけ。 /
        /// メタバフ Standard / アイテム選択 Tier軸 / 配線 Optimal / 挑戦 0pt。</para>
        ///
        /// <para><b>遺物を載せない理由 (2026-09-05 実測)。</b> 遺物 理論値 29pt を乗せると
        /// 7層クリアが 39.5% → <b>81.0%</b>、 6F 到達が 60.3% → 90.0% になる。
        /// 回帰の被説明変数 (bandScore) が天井に張り付き、 <b>アイテム間の差が band に出なくなる</b> ──
        /// 二値に近い成果指標の情報量は成功率 50% 付近で最大なので、 遺物なしの 39.5% の方が
        /// 測定条件として良い。 「6F 到達が薄いから遺物で嵩上げする」は誤った前提だった
        /// (遺物なしでも 60.3% 到達している)。 加えて遺物なしは §13-5 の正式なバランス基準でもある。</para>
        ///
        /// <para><b>`NoRelicKey` を明示的に立てること。</b> 立てないと「無し」ではなく「触らない」＝
        /// 前セッションで転がった遺物を引き継ぐ。 実効値はサマリー冒頭の `[実効状態]` 行で確認する。</para></summary>
        [MenuItem("Tools/AutoRun/■ アイテム学習 本走: 5000ラン × 50回 (家系Lvボーナス OFF・遺物なし)", priority = 8)]
        public static void RunItemLearning()
        {
            // **後段スイープのフラグを落とす (2026-09-09)。** `GrantTrialKey` / `ItemAblKey` は
            //   SessionState に残るので、 これが立ったまま本走に入ると 25 万ラン の裏で
            //   毎バッチ 6,000 ラン の別アームが回り、 しかも `randomGrantTrial` は
            //   `learnTier = false` を強制する ── **学習が 1 行も積まれないまま 3 日走る**。
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(LearnTierKey, true);      // item_stats.json を書くのはこちら
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(TheoRelicKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);        // **明示的に遺物なし** (null = 触らない ではない)
            EditorPrefs.SetBool(SweepAxisKey, false);
            EditorPrefs.SetBool(DebuffKey, false);
            EditorPrefs.SetBool(NoPartsKey, false);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            ItemMode = AutoRunner.ItemPickMode.TierBased;
            Skill = AutoRunner.WiringSkill.Optimal;       // Super は 1 ラン 5-10 秒で 25 万ランに乗らない
            AutoTest.LearnedPriorityProvider.DisableFamilyLvBonus = true;
            // **武器家系をクラス開始武器に固定する** (2026-09-05)。 職業は 4 種均等配分なので
            //   各家系が約 25% のランで最後まで担がれ、 4 家系を同じ厚みで測れる。
            //   固定しないと BOT の乗り換えが偏り (実測 盾 1.0%〜4.7%)、
            //   「弱いのか選ばれていないだけなのか」が分離できない ── 循環して永久に測れない。
            //   **static へ直接書かない** ── PlayMode 開始のドメインリロードで消える。
            EditorPrefs.SetBool(LockFamKey, true);
            Launch(5000, loopBatches: 50);
        }

        [MenuItem("Tools/AutoRun/自動周回: カスタム...", priority = 9)]
        public static void RunAutoLoopCustom()
        {
            var (runs, batches) = AutoLoopConfigWindow.Ask(1000, 10);
            if (runs > 0 && batches > 0) Launch(runs, loopBatches: batches);
        }

        // ===== 周回モード (遺物 §15-5): ペルソナごとに独立したラインを走らせる =====

        [MenuItem("Tools/AutoRun/周回モード: 10ペルソナ × 300ラン", priority = 9)]
        public static void RunAscension300() => LaunchAscension(300);

        [MenuItem("Tools/AutoRun/周回モード: 10ペルソナ × 1000ラン", priority = 9)]
        public static void RunAscension1000() => LaunchAscension(1000);

        [MenuItem("Tools/AutoRun/周回モード: カスタム...", priority = 9)]
        public static void RunAscensionCustom()
        {
            int n = AutoRunCountWindow.Ask(300);
            if (n > 0) LaunchAscension(n);
        }

        /// <summary>周回モードで起動する。 通常バッチは 1 ラン だけ回して、
        /// 本体は RunAscension() が担う (AutoRunner 側でバッチ後段に走る)。</summary>
        private static void LaunchAscension(int runsPerPersona)
        {
            SessionState.SetBool(AscendKey, true);
            SessionState.SetInt(AscendRunsKey, runsPerPersona);
            Launch(1);
        }

        /// <summary>天井 AI の動作確認。 **バランスを測るためのものではない** ──
        /// 例外を吐かないか・戦闘が固まらないか・1 ラン何秒かかるかだけを見る。
        /// 先読みは 1 ターンあたりの計算量が 2 桁上がるので、 本番のラン数を決める前にここで測る。</summary>
        [MenuItem("Tools/AutoRun/診断: 天井AI 動作確認 (3ラン)", priority = 10)]
        public static void RunSuperSmoke()
        {
            Skill = AutoRunner.WiringSkill.Super;
            Launch(3);
        }

        /// <summary>天井 AI を 1 点だけ測る。 遺物 TheoreticalBestCursed・挑戦 50pt・100 ラン。
        /// 条件は固定難易度スイープと同一 (器を借りている) ので、 過去の 50pt 行と直接比べられる。</summary>
        [MenuItem("Tools/AutoRun/診断: 天井AI 50pt × 100ラン (理論値遺物)", priority = 10)]
        public static void RunSuperAt50()
        {
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetInt(ChalOneScoreKey, 50);
            SessionState.SetInt(ChalOneRunsKey, 100);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>ADR-0010 Verification ①: 技量帯の実測。 配線方策だけを振り、
        /// **同一シードのラン単位ペア比較 (McNemar)** で差を判定する。</summary>
        [MenuItem("Tools/AutoRun/技量帯スイープ: 最適 vs 天井 (各300ラン・0pt)", priority = 12)]
        public static void RunSkillCompare()
        {
            SessionState.SetBool(SkillCmpKey, true);
            SessionState.SetInt(SkillCmpRunsKey, 300);
            Launch(1);
        }

        /// <summary>7層クリア率 (基準 16% 前後) の差を読むための厚めの本数。
        /// 300 ラン では不一致ペアが足りず、 数ポイントの差が McNemar で判定できない。</summary>
        /// <summary>ADR-0010 Verification ③: 難易度 30pt が 7層クリア 10% 前後に届くか。
        /// 従来 3.7% だった地点を、 天井 AI で測り直す。 条件は固定難易度スイープと同じ。</summary>
        [MenuItem("Tools/AutoRun/技量帯スイープ: 30pt 最適 vs 天井 (各500ラン)", priority = 12)]
        public static void RunSkillCompare30pt()
        {
            SessionState.SetBool(SkillCmpKey, true);
            SessionState.SetInt(SkillCmpRunsKey, 500);
            SessionState.SetInt(SkillCmpScoreKey, 30);
            Launch(1);
        }

        /// <summary>要求戦力 (RunPowerBudget) の寄与を単独で測る。 **両アームとも天井**で、
        /// 違いは「次のボスの要求戦力を読むか」だけ。 技量では区別できないので専用の並びにする。</summary>
        [MenuItem("Tools/AutoRun/技量帯スイープ: 天井 現行 vs +要求戦力 (各500ラン・0pt)", priority = 12)]
        public static void RunSkillCompareBudget()
        {
            SessionState.SetBool(SkillCmpKey, true);
            SessionState.SetBool(SkillCmpBudgetKey, true);
            SessionState.SetInt(SkillCmpRunsKey, 500);
            Launch(1);
        }

        /// <summary>乖離調査用: 20 ラン だけ回して**実効状態の行**を採る。 勝率は見ない。</summary>
        [MenuItem("Tools/AutoRun/診断: 技量帯スイープの実効状態 (各20ラン)", priority = 12)]
        public static void RunSkillCompareProbe()
        {
            SessionState.SetBool(SkillCmpKey, true);
            SessionState.SetInt(SkillCmpRunsKey, 20);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/技量帯スイープ: 最適 vs 天井 (各500ラン・0pt)", priority = 12)]
        public static void RunSkillCompare500()
        {
            SessionState.SetBool(SkillCmpKey, true);
            SessionState.SetInt(SkillCmpRunsKey, 500);
            Launch(1);
        }

        // ---- 出目パーツ遮断つき技量帯スイープ (2026-08-17) ----
        //   天井AI のロールアウトは面の**値**しか持たず添字を持たないので、 未来のターンで
        //   T1(振り直し禁止)/T3/T4(ゴースト配線) を評価できない。 パーツ 0 個なら誤差が消えるので、
        //   **技量差が回復するか**でこの仮説を切り分ける。 同一シードのペア比較 + McNemar。

        private const string NoPartsKey = "AutoRun.SuppressFaceParts";
        private const string MNP = "Tools/AutoRun/⑦ 計測用/出目パーツを陳列しない";
        [MenuItem(MNP, priority = 150)]
        private static void ToggleNoParts()
            => EditorPrefs.SetBool(NoPartsKey, !EditorPrefs.GetBool(NoPartsKey, false));
        [MenuItem(MNP, validate = true)]
        private static bool ToggleNoPartsV()
        { Menu.SetChecked(MNP, EditorPrefs.GetBool(NoPartsKey, false)); return true; }

        [MenuItem("Tools/AutoRun/技量帯スイープ: 最適 vs 天井 (各1000ラン・パーツ無し)", priority = 12)]
        public static void RunSkillCompareNoParts()
        {
            EditorPrefs.SetBool(NoPartsKey, true);
            SessionState.SetBool(SkillCmpKey, true);
            SessionState.SetInt(SkillCmpRunsKey, 1000);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/技量帯スイープ: 最適 vs 天井 (各1000ラン・パーツ有り)", priority = 12)]
        public static void RunSkillCompareWithParts()
        {
            EditorPrefs.SetBool(NoPartsKey, false);
            SessionState.SetBool(SkillCmpKey, true);
            SessionState.SetInt(SkillCmpRunsKey, 1000);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/技量帯スイープ: 素朴+最適+天井 (各300ラン・0pt)", priority = 12)]
        public static void RunSkillCompare3()
        {
            SessionState.SetBool(SkillCmpKey, true);
            SessionState.SetBool(SkillCmp3Key, true);
            SessionState.SetInt(SkillCmpRunsKey, 300);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/5Fボス勝率スイープ", priority = 10)]
        public static void RunBoss5Sweep() => Launch(300, sweep: true);

        // Λ突入には〈決意〉が要るため、 分母になるのは全ランの 6 割程度。 さらに 7層クリアは
        // 3 割前後の低頻度事象なので、 300 ランでは標準誤差 ±3.4pt もあり数ポイントの差が読めない。
        // 1000 ランなら突入 ~600 で ±1.9pt まで縮む (2026-08-08)。
        [MenuItem("Tools/AutoRun/Λファーム量スイープ (各1000ラン)", priority = 11)]
        public static void RunLambdaFarmSweep() => Launch(1000, lambdaSweep: true);

        [MenuItem("Tools/AutoRun/固定難易度スイープ (各300ラン)", priority = 13)]
        public static void RunChallengeFixedSweep()
        {
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>階段の 3 点 (0 / 30 / 50pt) を天井 AI で厚く測る。
        ///
        /// <para>既定の 6 点スイープ (0/10/20/30/40/50) は形を見るためのもので、
        /// 1 点 300 ラン ＝ クリア率 30% 付近の標準誤差が 2.6pt ある。
        /// **値を確定させる**用に、 端と中央の 3 点だけに絞ってラン数を積む。
        /// 遺物は他の固定難易度スイープと同じ TheoreticalBestCursed で固定
        /// (＝ 遺物なしの基準値測定とは別物。 0pt の数値を直接つないではいけない)。</para></summary>
        [MenuItem("Tools/AutoRun/固定難易度スイープ: 0/30/50pt × 各5000ラン (天井AI)", priority = 13)]
        public static void RunChallengeSweep3PointSuper()
        {
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetString(ChalSweepScoresKey, "0,30,50");
            SessionState.SetInt(ChalSweepRunsKey, 5000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>挑戦デバフを 1 段ずつ単独で載せ、 段ごとの重さを 0pt 基準のペア比較で測る。
        /// 固定難易度スイープが「合計 Npt」を測るのに対し、 こちらは **値付けが正しいか**を測る。</summary>
        /// <summary>段ごとの重さを**基準条件 (遺物なし・ノーマル AI)** で測る。
        /// 値付けの正本はこちら ── 遺物は周回引継ぎなので初回プレイヤーは 0 個。</summary>
        /// <summary>直近に効果を差し替えた軸だけを測り直す。 全 24 段は 1000 ラン で 40〜60 分。
        /// 現在の対象: 綻び (周期 3/2/1 → 4/3/2) / 遅い回復 (旧〈浅い眠り〉・全回復 ×0.70)。</summary>
        [MenuItem("Tools/AutoRun/挑戦 単軸スイープ: 変更した軸のみ (遺物なし・1000ラン)", priority = 13)]
        public static void RunChangedAxesSweep()
        {
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisNoRelicKey, true);
            SessionState.SetBool(ChalAxisChangedKey, true);
            Launch(1);
        }

        /// <summary>T4 の値付け用。 **カテゴリ 6pt** と **6pt+T4** を並べて、 T4 自身のコストを出す。
        /// T4 はカテゴリ 6pt が解禁条件なので、 単独では測れない。</summary>
        [MenuItem("Tools/AutoRun/T4 スイープ: 5種のT4のみ (遺物なし・1000ラン)", priority = 13)]
        public static void RunT4Sweep()
        {
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisNoRelicKey, true);
            SessionState.SetBool(ChalCatKey, true);
            // **6pt アームを省かない** (2026-08-11)。 「基礎軸は不変だから前回の
            //   参照値を使い回せる」として T4 アームだけを回していたが、 その前提が
            //   崩れても更新されず、 T4 コストを**別バッチの参照値との引き算**で
            //   出し続けていた。 基準アームの再現は基準の地続きしか保証しない。
            //   12 アーム 13 分は、 数字が信用できないことに比べれば安い。
            SessionState.SetBool(ChalCatT4Key, false);
            Launch(1);
        }

        /// <summary>**診断: T4 を単独で測る**。 基準 + 各 T4 のみ + ノイズ床 の 7 アーム。
        ///
        /// 通常の T4 スイープは「カテゴリ6pt」と「6pt+T4」の差で測るので、
        /// **T4 と基礎軸の相互作用が差に混ざる**。 〈最後の審判〉が E の上でだけ
        /// +1.4pt (プレイヤーが強くなる) を出したが、 それが審判固有なのか
        /// E の〈天変地異〉(ボス攻撃がターン比例) との噛み合わせなのかを分ける。
        ///
        /// **配点としては不正な組み合わせ**なので値付けには使わない。 診断専用。</summary>
        [MenuItem("Tools/AutoRun/診断: T4 単独 (基準 vs 各T4のみ・遺物なし・1000ラン)", priority = 13)]
        public static void RunT4AloneDiag()
        {
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisNoRelicKey, true);
            SessionState.SetBool(ChalCatKey, true);
            SessionState.SetBool(ChalCatAloneKey, true);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/Item Tier更新/難易度 0pt (Standard・通常AI・1000ラン)", priority = 14)]
        public static void UpdateItemTierAt0() => LaunchItemTierCalibration(0, 1000);

        /// <summary>単一難易度 (0pt) で **1 万ラン** の Tier 再評価 (2026-08-17)。
        ///
        /// <para>出目パーツの導入でアイテムの相対価値が全面的に変わったため、
        /// 233,500 ラン分の旧学習 (パーツ不在) を作り直す。 **0pt 単一で厚く取る** ──
        /// 難易度を混ぜると 1 難易度あたりのサンプルが薄くなり、
        /// 「パーツがある土俵での序列」という肝心の測定精度が落ちる。</para></summary>
        [MenuItem("Tools/AutoRun/Item Tier更新/難易度 0pt × 10000ラン (単一難易度・厚取り)", priority = 14)]
        public static void UpdateItemTierAt0TenThousand() => LaunchItemTierCalibration(0, 10000);

        [MenuItem("Tools/AutoRun/Item Tier更新/難易度 30pt (Standard・通常AI・1000ラン)", priority = 14)]
        public static void UpdateItemTierAt30() => LaunchItemTierCalibration(30, 1000);

        [MenuItem("Tools/AutoRun/Item Tier更新/難易度 50pt (Standard・通常AI・1000ラン)", priority = 14)]
        public static void UpdateItemTierAt50() => LaunchItemTierCalibration(50, 1000);

        [MenuItem("Tools/AutoRun/Item Tier更新/0・30・50pt 一括 (各1000ラン)", priority = 14)]
        public static void UpdateItemTierAllDifficulties() => LaunchItemTierCalibration(-2, 1000);

        [MenuItem("Tools/AutoRun/Item Tier更新/0・30・50pt × 1000ラン × 10セット", priority = 14)]
        public static void UpdateItemTierAllDifficultiesTenSets()
            => LaunchItemTierCalibration(-2, 1000, 10);

        [MenuItem("Tools/AutoRun/Item Tier更新/中断再開: 0pt残4・30pt残10・50pt残10", priority = 14)]
        public static void ResumeItemTierAfterReload()
        {
            SessionState.SetBool(TierCalResumeKey, true);
            LaunchItemTierCalibration(-2, 1000, 10);
        }

        [MenuItem("Tools/AutoRun/Item Tier更新/動作確認 0・30・50pt (各10ラン)", priority = 14)]
        public static void SmokeItemTierCalibration()
        {
            // 3条件の一括動作確認は AutoRunner 内で順次処理する。
            LaunchItemTierCalibration(-2, 10);
        }

        private static void LaunchItemTierCalibration(int score, int runs, int batches = 1)
        {
            Skill = AutoRunner.WiringSkill.Optimal;
            ItemMode = AutoRunner.ItemPickMode.BuildFocused;
            SessionState.SetInt(TierCalScoreKey, score);
            SessionState.SetInt(TierCalRunsKey, Mathf.Max(1, runs));
            SessionState.SetInt(TierCalBatchesKey, Mathf.Max(1, batches));
            Launch(1);
        }

        /// <summary>天井 AI・理論値遺物・挑戦50ptを1000ラン測る本計測。</summary>
        [MenuItem("Tools/AutoRun/天井AI 50pt × 1000ラン (理論値遺物)", priority = 10)]
        public static void RunSuperAt50Full()
        {
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetInt(ChalOneScoreKey, 50);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/診断: 天井AI 50pt × 10ラン (速度計測)", priority = 10)]
        public static void RunSuperAt50Benchmark()
        {
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetInt(ChalOneScoreKey, 50);
            SessionState.SetInt(ChalOneRunsKey, 10);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        // ---- 50pt × 天井AI × 1万ラン の対アーム (2026-08-17) ----
        //   遺物の寄与を挑戦ptから分離して読むための 2 本。 **同じ挑戦pt・同じAI・同じラン数**で
        //   遺物の有無だけを差し替える。 ⑥トグルはここで明示的に上書きするので、
        //   直前に何を触っていても条件が固定される (トグル頼みだと取り違える)。
        [MenuItem("Tools/AutoRun/50pt対アーム/① 遺物アリ 50pt × 10000ラン (天井AI)", priority = 11)]
        public static void RunSuper50WithRelic()
        {
            EditorPrefs.SetBool(NoRelicKey, false);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetInt(ChalOneScoreKey, 50);
            SessionState.SetInt(ChalOneRunsKey, 10000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>
        /// Ultra production benchmark. Runtime applies and re-validates the complete
        /// profile instead of trusting the current menu toggles. If the production
        /// worker/checkpoint gate is not ready, AutoRunner fails closed before run 1.
        /// </summary>
        [MenuItem("Tools/AutoRun/Ultra AI/遺物アリ Standard 50pt × 10000ラン", priority = 10)]
        public static void RunUltra50WithRelic()
        {
            if (!UltraProductionWorkerBuild.EnsureBuilt(out string workerBuildError))
            {
                Debug.LogError("[AutoRunMenu][Ultra] worker build failed; run not started: "
                    + workerBuildError);
                return;
            }
            // **Clear every batch-mode key first.** Launch() reads SessionState before
            // ApplyTo runs, so a leftover key from a previous menu action would configure a
            // competing sweep. ApplyTo forces those fields off again, but erasing here also
            // stops the keys from surviving into the *next* run — which is how a stale
            // toggle silently changes a measurement.
            //
            // EditorPrefs are deliberately **not** rewritten here. Launch() reads them into
            // the runner first, then ApplyTo overwrites every field the profile owns and
            // Matches re-verifies it, so correctness does not depend on them. Silently
            // flipping the user's saved menu toggles would change unrelated later batches.
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, false);
            Skill = AutoRunner.WiringSkill.Ultra;
            SessionState.SetBool(UltraStandard50WithRelic10KKey, true);
            Launch(1);
        }

        /// <summary>Ultra の dispatch 経路を実際に通す。 **強さの測定ではなく配管の確認。**
        ///
        /// 先頭手の sink と末尾手の sink を同一シードで走らせ、 結果が**違う**ことを見る。
        /// 同じなら「hook は呼ばれているが選択がゲームへ届いていない」。
        /// 全く動かないなら hook 自体が呼ばれていない。 どちらも
        /// 「controller が毎回 baseline に同意した」場合と結果が区別できないので、
        /// 意図的に**弱くて偏った** sink を使う。</summary>
        [MenuItem("Tools/AutoRun/Ultra AI/診断: dispatch 疎通 (先頭手・30ラン)", priority = 30)]
        public static void RunUltraDispatchSmokeFirst() => LaunchUltraSmoke(1);

        [MenuItem("Tools/AutoRun/Ultra AI/診断: dispatch 疎通 (末尾手・30ラン)", priority = 31)]
        public static void RunUltraDispatchSmokeLast() => LaunchUltraSmoke(2);

        /// <summary>**マップ航行にそもそも影響力があるかを測る。** 天井を探す前に、
        /// 探す価値がある層かを確かめる。
        ///
        /// <para>わざと下手なマップ方策（先頭手 / 末尾手）を Super と同一シード・同一ラン数で
        /// 走らせ、クリア率がどれだけ落ちるかを見る。<b>下手に打っても落ちないなら、
        /// 完璧に打っても上がらない</b> ── その層に天井は存在せず、rollout の予算を何倍に
        /// しても無駄だと事前に分かる。</para>
        ///
        /// <para>rollout を撃たないので通常バッチ速度。1手32本 に増やして 20 時間かけた末に
        /// 「本数が足りないのか天井が無いのか区別できない」に着地するのを避けるための、
        /// 先に払う 10 分。</para></summary>
        [MenuItem("Tools/AutoRun/Ultra AI/影響力測定: マップ方策 先頭手 (0pt・100ラン)", priority = 40)]
        public static void RunMapLeverageFirst() => LaunchUltraSmoke(1, 100);

        [MenuItem("Tools/AutoRun/Ultra AI/影響力測定: マップ方策 末尾手 (0pt・100ラン)", priority = 41)]
        public static void RunMapLeverageLast() => LaunchUltraSmoke(2, 100);

        /// <summary>実ランの途中から checkpoint を 1 つ採る。 rollout のコスト実測に要る。
        ///
        /// <para>合成盤面では測れない ── 手組みの `RunState` は装備もダイスも既定で、
        /// 戦闘に入った瞬間に固着する。 `RunState.Initialize` も `MapGenerator.Generate` も
        /// メタ進行のシングルトンに触るので play mode 外では呼べない。</para></summary>
        [MenuItem("Tools/AutoRun/Ultra AI/診断: 実ランから checkpoint を採取 (1ラン)", priority = 33)]
        public static void CaptureUltraCheckpoint()
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetString(UltraCaptureKey, UltraCheckpointFile);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>採取した checkpoint を **Editor の中で** resume する。
        ///
        /// <para>worker で追えないため。 <see cref="AutoRunner"/> はラン中の <c>Debug.Log</c> を
        /// 内部バッファへ回すので、 ヘッドレス worker の log にはラン中の行が 1 つも出ない。
        /// 同じ resume を Editor で走らせればコンソールに全部出る。</para></summary>
        [MenuItem("Tools/AutoRun/Ultra AI/診断: 採取した checkpoint から resume (1ラン)", priority = 34)]
        public static void ResumeFromUltraCheckpoint()
        {
            if (!System.IO.File.Exists(UltraCheckpointFile))
            {
                Debug.LogError("[AutoRunMenu][Ultra] checkpoint が無い。 先に採取メニューを実行: "
                    + UltraCheckpointFile);
                return;
            }
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetString(UltraResumeKey, UltraCheckpointFile);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>checkpoint の現在地から出ている最初の移動手の action id。
        /// 評価器が候補として並べるものと同じ形。</summary>
        private static string FirstMoveActionId(AutoTest.Ultra.UltraResumePayload payload)
        {
            var nodes = payload?.map?.nodes;
            if (nodes == null) return "";
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] == null
                    || !string.Equals(nodes[i].id, payload.map.currentNodeId,
                                      System.StringComparison.Ordinal)) continue;
                string[] links = nodes[i].connections;
                if (links == null || links.Length == 0) return "";
                var sorted = new System.Collections.Generic.List<string>(links);
                sorted.Sort(System.StringComparer.Ordinal);
                return AutoTest.Ultra.UltraLegalAction.Of(
                    AutoTest.Ultra.UltraActionKind.MoveTo, sorted[0]).ActionId;
            }
            return "";
        }

        /// <summary>採取した checkpoint の置き場。 コスト実測メニューが読む。</summary>
        public static string UltraCheckpointFile =>
            System.IO.Path.Combine(
                System.IO.Path.GetFullPath("AutoRunLogs"), "ultra_checkpoint.json");

        /// <summary>マクロ決定が 1 ラン に何回あるのかを数える。 **判断は一切変えない。**
        ///
        /// <para>コスト外挿がずっと使ってきた「マクロ決定 30 回/ラン」は実測ではなく、
        /// コスト計測メニューに直書きされた定数。 rollout をどの決定点で撃つか絞る話は
        /// 全部この数に乗っているので、 絞る前に数える。</para>
        ///
        /// <para>特に見たいのは **手が 1 つしかない決定点の割合**。 そこは何本 rollout を
        /// 撃っても選ぶ手が変わらないので、 発見的な間引きではなく定義上ただ働きになる。</para></summary>
        [MenuItem("Tools/AutoRun/Ultra AI/診断: マクロ決定の国勢調査 (0pt・30ラン)", priority = 35)]
        public static void RunUltraDecisionCensus()
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;   // 生産方策のまま。 数えるだけ
            SessionState.SetBool(UltraCensusKey, true);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 30);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>**rollout で実際に手を決めるバッチ。** ここまでの Ultra 実装が
        /// 初めて方策として走る唯一の経路。
        ///
        /// <para>撃つのは <c>MapNavigation</c> だけ ── 国勢調査の実測で、決定の 62% が
        /// ここに集まり、6-7層 にはそもそも決めるべき事がほとんど無い (手=1 が 8 割) と
        /// 分かったため。絞りは**方策の一部**であって最適化ではないので、
        /// どこで撃ったかはサマリに残る。</para>
        ///
        /// <para><b>Editor は決定のたび数秒固まる。</b> worker プロセスの完了を同期で待つ。
        /// 番犬 (20秒/ラン) はこの待ち時間を控除するので DEADLOCK にはならないが、
        /// 見た目は「応答なし」に近い。止めたければ Play を切ること。</para></summary>
        private static bool TryInstallRolloutSink(AutoRunner runner, int rolloutsPerAction)
        {
            // **ここでビルドはしない。** この関数は Play Mode に入った後の
            // OnPlayModeChanged から呼ばれるが、 BuildPipeline は Play Mode 中に走らない
            // (EnsureBuilt は "must finish before Play Mode" で断る)。
            // ビルドは LaunchUltraRollout が Play Mode に入る前に済ませてある。
            // ここは「本当に出来ているか」の確認だけ。
            if (!UltraProductionWorkerBuild.TryLoadDescriptor(out _, out string buildError))
            {
                Debug.LogError("[AutoRunMenu][Ultra] worker が使えない; rollout 経路は起動しない: "
                    + buildError + " ── 先に Play Mode を抜けてメニューから起動し直すこと");
                return false;
            }

            string workRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ultra_rollout_" + DateTime.Now.ToString("HHmmss"));

            // **profile は実バッチ側と揃える。** ここがずれると、本ランは 0pt・遺物なしで
            // 走っているのに rollout だけ 50pt・理論値遺物の未来を評価する、という
            // 2026-08-17 の portfolio 障害と同型の事故になる。
            UltraPortfolioProfileSpec profile = UltraPortfolioProtocol.CanonicalProfile();
            profile.challengeScore = 0;
            profile.theoreticalBestCursedRelic = false;

            var oracle = new UltraProcessEpisodeOracle(
                UltraProductionWorkerBuild.ExecutablePath,
                UltraProductionWorkerBuild.BuildRoot,
                workRoot, profile, UltraPortfolioProtocol.CanonicalCandidates()[0],
                AutoTest.PolicyParameters.Current?.Clone(),
                new string('a', 64))
            {
                EpisodeTimeoutSeconds = 300,
            };
            if (!oracle.IsHealthy)
            {
                Debug.LogError("[AutoRunMenu][Ultra] oracle unhealthy: worker が無い "
                    + UltraProductionWorkerBuild.ExecutablePath);
                return false;
            }

            var evaluator = new AutoTest.Ultra.UltraRolloutEvaluator(oracle)
            {
                RolloutsPerAction = Math.Max(1, rolloutsPerAction),
            };
            AutoTest.Ultra.UltraRolloutEvaluator.ResetCounters();

            // payload は **決定のたびに現在の盤面から** 作る。sink 生成時に固定すると、
            // ラン中の全決定を同じ古い盤面から評価することになる。
            var sink = new AutoTest.Ultra.UltraRolloutSink(
                evaluator,
                (checkpoint, veilSeed) =>
                {
                    var gm = GameLoop.GameManager.Instance;
                    var mm = MapSystem.MapManager.Instance;
                    if (gm?.Run == null || mm == null) return null;
                    return AutoTest.Ultra.UltraResumePayload.Create(
                        gm.Run, mm, checkpoint.epoch, checkpoint.point, veilSeed, out _);
                },
                0xD1CEB0A5D1CEB0A5UL);
            sink.AllowedPoints.Add(AutoTest.Ultra.UltraDecisionPoint.MapNavigation);
            runner.ultraSink = sink;

            Debug.LogWarning("[AutoRunMenu][Ultra] **rollout 方策で走る**: 1手 "
                + evaluator.RolloutsPerAction + " 本 / 撃つ決定点 = MapNavigation のみ"
                + " / worker 一時領域 " + workRoot
                + "  ── 決定ごとに Editor が数秒固まる。番犬は思考時間を控除する");
            return true;
        }

        /// <summary>疎通: rollout 方策が Super と**違う手を打つか**を見るだけ。
        /// 委譲率がほぼ 0% なら、どの規模で回しても何も測れないことが先に分かる。</summary>
        [MenuItem("Tools/AutoRun/Ultra AI/rollout 方策: 疎通 (0pt・10ラン・1手4本)", priority = 36)]
        public static void RunUltraRolloutSmoke() => LaunchUltraRollout(10, 4);

        /// <summary>本番と同じ本数で、同点率だけを先に見る。
        ///
        /// <para>1手4本 の疎通では評価 335 件のうち **52.8% が同点** で、そこは辞退に回る。
        /// 本数を倍にして同点がどれだけ減るかは事前に読めない ── クリア率 13% の 0/1 報酬
        /// では 8 本でも期待クリア数が 1 程度しかない。5.3 時間を投じる前に 30 分で確かめる。</para></summary>
        [MenuItem("Tools/AutoRun/Ultra AI/rollout 方策: 疎通 (0pt・10ラン・1手8本)", priority = 37)]
        public static void RunUltraRolloutSmoke8() => LaunchUltraRollout(10, 8);

        [MenuItem("Tools/AutoRun/Ultra AI/rollout 方策: 本番 (0pt・100ラン・1手8本)", priority = 38)]
        public static void RunUltraRolloutMain() => LaunchUltraRollout(100, 8);

        /// <summary>ペア比較の対照群。 **rollout 以外は 1 ビットも変えない。**
        ///
        /// <para>同じ起動経路を通すことが要点で、 別メニューで似た条件を組み直すのは避ける ──
        /// 遺物・挑戦pt・技量・ラン数のどれか 1 つがずれただけで比較は無意味になり、
        /// しかもサマリを後から読んでも気付けない。2026-08-10 に 0pt 基準値が
        /// 53.3% / 16.6% に割れたのがこの形。</para></summary>
        [MenuItem("Tools/AutoRun/Ultra AI/rollout 方策: 対照 Super (0pt・100ラン・rollout なし)",
            priority = 39)]
        public static void RunUltraRolloutBaseline() => LaunchUltraRollout(100, 0);

        /// <summary>階層DP航行 (④) の掃引。 対照は
        /// 「rollout 方策: 対照 Super」 と**同一シード・同一条件**（同じ起動経路を通す）。
        ///
        /// <para><b>割引率が本体。</b> 既定の 0.6 のままだと 10 行先は 0.6^10 ≒ 0.006 で、
        /// 実質 1 手先読みと変わらない ── 構造を入れただけでは何も変わらないので、
        /// 深く読むことに価値があるかをここで先に確かめる。</para></summary>
        /// <summary>**交絡の切り分け用。** γ をほぼ 0 にすると末尾項が消え、DP は
        /// 1 手先読みと同じ「次のノードの <see cref="AutoRunner"/>.Rank を 1 回」に縮む。
        /// 残る違いは <b>HP 比を 0.1 幅の帯へ量子化していること</b>だけ。
        ///
        /// <para>ここで対照と大きく食い違うなら、深さではなく量子化が挙動を変えている ──
        /// γ を上げた測定の解釈がまるごと変わる。</para></summary>
        [MenuItem("Tools/AutoRun/階層DP航行: γ=0.01 (量子化の影響だけ・100ラン)", priority = 49)]
        public static void RunFloorDp01() => LaunchFloorDp(1);

        [MenuItem("Tools/AutoRun/階層DP航行: γ=0.60 (0pt・100ラン)", priority = 50)]
        public static void RunFloorDp60() => LaunchFloorDp(60);

        [MenuItem("Tools/AutoRun/階層DP航行: γ=0.80 (0pt・100ラン)", priority = 51)]
        public static void RunFloorDp80() => LaunchFloorDp(80);

        [MenuItem("Tools/AutoRun/階層DP航行: γ=0.90 (0pt・100ラン)", priority = 52)]
        public static void RunFloorDp90() => LaunchFloorDp(90);

        [MenuItem("Tools/AutoRun/階層DP航行: γ=0.95 (0pt・100ラン)", priority = 53)]
        public static void RunFloorDp95() => LaunchFloorDp(95);

        // ---- 1000ラン 版 ----
        //
        //  **n=100 では数ポイントの差が原理的に解像できない。** 2026-08-21 の測定は
        //  クリア 13%〜25% に散らばったのに、 不一致 14〜26 件では McNemar が
        //  どれも p>0.05 ── 「負けた」も「少し悪い」も主張できていなかった。
        //  DP アームは rollout を撃たないので通常バッチ速度 (1000ラン ≒ 20分)。
        //  100 に留める理由が無い。
        [MenuItem("Tools/AutoRun/階層DP航行: 対照 Super (0pt・1000ラン)", priority = 55)]
        public static void RunFloorDpBaseline1000() => LaunchFloorDp(0, 1000);

        [MenuItem("Tools/AutoRun/階層DP航行: γ=0.01 (0pt・1000ラン)", priority = 56)]
        public static void RunFloorDp01_1000() => LaunchFloorDp(1, 1000);

        [MenuItem("Tools/AutoRun/階層DP航行: γ=0.60 (0pt・1000ラン)", priority = 57)]
        public static void RunFloorDp60_1000() => LaunchFloorDp(60, 1000);

        [MenuItem("Tools/AutoRun/階層DP航行: γ=0.90 (0pt・1000ラン)", priority = 58)]
        public static void RunFloorDp90_1000() => LaunchFloorDp(90, 1000);

        // ---- HP 量子化の掃引 (γ=0.01 固定 ＝ 量子化だけが変数) ----
        //
        //  γ=0.60 の n=1000 で「深さは決定を倍に変えるが、 その変更は 101 対 105 の
        //  コイン投げ」と出た ＝ Rank() には 1 手先を超える予測力が無い。
        //  一方 γ=0.01 (量子化のみ) は有利側へ傾いた。 **粗くすることが効いている**
        //  という仮説を、 帯数の単調性で直接確かめる。
        //
        //  **101 帯は実装の正当性チェック。** 量子化がほぼ消えるので DP は
        //  1 手先読みと一致するはずで、 対照とほぼ同じ結果にならなければ配線が間違っている。
        [MenuItem("Tools/AutoRun/階層DP航行: 帯5 (γ=0.01・1000ラン)", priority = 60)]
        public static void RunFloorDpBands5() => LaunchFloorDp(1, 1000, 5);

        [MenuItem("Tools/AutoRun/階層DP航行: 帯21 (γ=0.01・1000ラン)", priority = 61)]
        public static void RunFloorDpBands21() => LaunchFloorDp(1, 1000, 21);

        [MenuItem("Tools/AutoRun/階層DP航行: 帯101 (配線チェック・1000ラン)", priority = 62)]
        public static void RunFloorDpBands101() => LaunchFloorDp(1, 1000, 101);

        // ---- 別シードでの再現確認 ----
        //
        //  帯101・γ=0.01 が seed 0 で p=0.0396 と出た。 だが**同じシードで 8 本のアームを
        //  回した後の p=0.0396** で、 多重比較だけでも説明がつく (8 回検定すれば
        //  どれかが偶然 5% を切る確率は約 34%)。 **別のシードで再現するかどうかが本番。**
        //  対照と変種を必ずペアで回すこと ── 変種だけ別シードで走らせても比較にならない。
        [MenuItem("Tools/AutoRun/階層DP航行: 再現確認 対照 (seed2・1000ラン)", priority = 65)]
        public static void RunFloorDpConfirmBase() => LaunchFloorDp(0, 1000, 0, "2");

        [MenuItem("Tools/AutoRun/階層DP航行: 再現確認 帯101 (seed2・1000ラン)", priority = 66)]
        public static void RunFloorDpConfirmVariant() => LaunchFloorDp(1, 1000, 101, "2");

        /// <summary><paramref name="discountPct"/> が 0 なら DP を差さない対照群。
        /// **対照も同じ起動経路を通す** ── 別メニューで似た条件を組み直すと、
        /// 遺物・挑戦pt・ラン数のどれか 1 つがずれても後から気付けない。</summary>
        // ---- 他の難易度での確認 ----
        //
        //  0pt で 2 シード再現 (+2.3pt, 併合 p=0.0017) したが、測ったのは 0pt だけ。
        //  道中の選択肢が変わる高難易度でも同じ向きに効くかを見る。
        //  **遺物は有効にする** ── 遺物なしの 30pt はクリア率がほぼ 0 で、
        //  n=1000 でも不一致がひと桁になり検出力が消える。
        [MenuItem("Tools/AutoRun/階層DP航行: 30pt 対照 (遺物あり・1000ラン)", priority = 70)]
        public static void RunFloorDp30Base() => LaunchFloorDp(0, 1000, 0, null, 30, false);

        [MenuItem("Tools/AutoRun/階層DP航行: 30pt 帯101 (遺物あり・1000ラン)", priority = 71)]
        public static void RunFloorDp30Variant() => LaunchFloorDp(1, 1000, 101, null, 30, false);

        // 30pt はクリア率 3.8% で不一致が 21 件しか出ず、**効果が住んでいる指標を測れない**
        // （6F は 0pt でも動いていないので、そこが無差でも矛盾しない）。
        // 挑戦pt を上げる代わりに **遺物だけを足して**クリア率を上げ、検出力を確保する。
        // 変える軸が 1 本なので「0pt 遺物なし固有か」に答えられる。
        [MenuItem("Tools/AutoRun/階層DP航行: 0pt遺物あり 対照 (1000ラン)", priority = 68)]
        public static void RunFloorDpRelicBase() => LaunchFloorDp(0, 1000, 0, null, 0, false);

        [MenuItem("Tools/AutoRun/階層DP航行: 0pt遺物あり 帯101 (1000ラン)", priority = 69)]
        public static void RunFloorDpRelicVariant() => LaunchFloorDp(1, 1000, 101, null, 0, false);

        [MenuItem("Tools/AutoRun/階層DP航行: 50pt 対照 (遺物あり・1000ラン)", priority = 72)]
        public static void RunFloorDp50Base() => LaunchFloorDp(0, 1000, 0, null, 50, false);

        [MenuItem("Tools/AutoRun/階層DP航行: 50pt 帯101 (遺物あり・1000ラン)", priority = 73)]
        public static void RunFloorDp50Variant() => LaunchFloorDp(1, 1000, 101, null, 50, false);

        // ---- 配線の同点拡張 (2026-08-22) ----
        //  実測で **56.8% の判断が「上位候補すべて同点」** だった。 同点なら浅い評価は
        //  順位を決められないので、 どの 6 本がロールアウトへ進むかは列挙順が決めている。
        //  枠を広げても主判断の向きは変えない ── **入口の恣意性だけを減らす**変更。
        //  対照アームは階層DP の対照と同一条件なので、 決定性が保たれていれば再現する。
        [MenuItem("Tools/AutoRun/配線同点拡張: 対照 Super (0pt・1000ラン)", priority = 75)]
        public static void RunTieExpandBaseline() => LaunchTieExpand(0, 1000);

        [MenuItem("Tools/AutoRun/配線同点拡張: 枠24 (0pt・1000ラン)", priority = 76)]
        public static void RunTieExpand24() => LaunchTieExpand(24, 1000);

        [MenuItem("Tools/AutoRun/配線同点拡張: 枠48 (0pt・1000ラン)", priority = 77)]
        public static void RunTieExpand48() => LaunchTieExpand(48, 1000);

        // ---- 充電の値付け掃引 (2026-08-22) ----
        //  実測: リロール判断の **34.6% を「利得なし」で辞退、 基準との差は平均 0.0008**。
        //  15 万件が紙一重で落ちている。 `chargeRerollGain` を下げると充電の価値が下がり、
        //  払うのが安くなってリロールが増える。 コード自身の但し書きでは素の推定値が 0.40 で、
        //  0.30 は「役を狙って期待値を捨てる振り直しもあるぶん控えめに」置いた値。
        //  **対照は既存の 0pt 対照アーム (17.8%) と同一条件。**
        [MenuItem("Tools/AutoRun/充電値付け: gain 0.20 (0pt・1000ラン)", priority = 80)]
        public static void RunRerollGain20() => LaunchRerollGain(20);

        [MenuItem("Tools/AutoRun/充電値付け: gain 0.10 (0pt・1000ラン)", priority = 81)]
        public static void RunRerollGain10() => LaunchRerollGain(10);

        [MenuItem("Tools/AutoRun/充電値付け: gain 0.40 (0pt・1000ラン)", priority = 82)]
        public static void RunRerollGain40() => LaunchRerollGain(40);

        /// <summary>旧既定 0.30 の対照アーム。 **0.20 の採用根拠をパーツ有りで検証する**ため
        /// (採用時の +3.7pt はパーツ無し条件の測定だった)。</summary>
        [MenuItem("Tools/AutoRun/充電値付け: gain 0.30 (旧既定・0pt・1000ラン)", priority = 79)]
        public static void RunRerollGain30() => LaunchRerollGain(30);

        // ---- マクロ判断のアブレーション (2026-08-22) ----
        //  技量帯スイープは `wiringSkill` しか振っておらず、 航行・ショップ・報酬・イベントは
        //  両アームで同一だった ＝ **マクロの技量帯は未測定**。
        //  1 軸ずつランダム化して落ち幅を測る。 落ちない軸は「そこに技量が無い」という答え。
        //  専用乱数なので GameRng を消費せず、 同一シードのペア比較が成立する。
        /// <summary>Super 基準と**同一シード・同一経路**の Optimal アーム。
        ///
        /// <para>技量帯スイープの「7層クリア +2.5pt (p=0.145)」は
        /// 「7層に着けるか」と「着いてから勝てるか」が混ざっている。 runs.jsonl があれば
        /// <c>P(到達7F)</c> と <c>P(クリア | 到達7F)</c> に分解できる ──
        /// **後者に差が無ければ、 7層の戦闘そのものが技量を報いていない**という診断になる。</para></summary>
        [MenuItem("Tools/AutoRun/技量分解: Optimal 対照 (0pt・1000ラン)", priority = 93)]
        public static void RunOptimalArm()
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>7層だけ Optimal の戦闘方策へ切り替えたアーム。
        /// 条件付き実測 (両AIが7層到達した379ラン) で Optimal 71 対 Super 41 / p=0.0059。</summary>
        [MenuItem("Tools/AutoRun/7層Optimal戦闘: 旧挙動(Super)対照 (0pt・1000ラン)", priority = 96)]
        public static void RunLayer7Optimal() => LaunchLayer7Optimal(true);

        /// <summary>遺物ありでも同じ向きに効くか。 **階層DP はここで符号が反転した**
        /// (0pt遺物なし +2.3pt / 0pt遺物あり −3.3pt、 どちらも有意)。
        /// 対照は「充電値付け: 対照 遺物あり」(現行既定のまま遺物ON) を使う。</summary>
        [MenuItem("Tools/AutoRun/7層Optimal戦闘: 旧挙動(Super)対照 遺物あり (0pt・1000ラン)", priority = 96)]
        public static void RunLayer7OptimalRelic() => LaunchLayer7Optimal(false);

        private static void LaunchLayer7Optimal(bool noRelic)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, noRelic);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetBool(Layer7OptimalKey, true);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        // ---- StaticValue の飽和修正 (2026-08-22) ----
        //  「防げている」局面で ttd=999 → 勝率 p が 1 に飽和 → **敵HPが評価から消える**。
        //  7層 (40ターン級・エスカレーション×2.3) でだけ致命的になり、 Super は Optimal に対し
        //  実効倍率は同一 (5.414 対 5.460) なのに atkBase が 12.7% 低く、 ターン数が 8.6% 多かった。
        //  **7層で Super が戦っていないと効果が見えない**ので、 7層 Optimal 切替を OFF にして測る。
        [MenuItem("Tools/AutoRun/ttd頭打ち: 12ターン (7層Super・0pt遺物あり・1000ラン)", priority = 104)]
        public static void RunTtdCap12() => LaunchTtdCap(12);

        [MenuItem("Tools/AutoRun/ttd頭打ち: 6ターン (7層Super・0pt遺物あり・1000ラン)", priority = 105)]
        public static void RunTtdCap6() => LaunchTtdCap(6);

        /// <summary>修正 + 7層Optimal切替 **併用**。 修正だけ (74.9%) を上回らなければ
        /// 切替は冗長 ＝ 撤回してよい、 という判定に使う。</summary>
        [MenuItem("Tools/AutoRun/ttd頭打ち: 12ターン + 7層Optimal併用 (0pt遺物あり・1000ラン)", priority = 106)]
        public static void RunTtdCap12WithSwitch() => LaunchTtdCap(12, keepSwitch: true);

        [MenuItem("Tools/AutoRun/ttd頭打ち: 12ターン (7層Super・0pt遺物なし・1000ラン)", priority = 107)]
        public static void RunTtdCap12NoRelic() => LaunchTtdCap(12, noRelic: true);

        private static void LaunchTtdCap(int turns, bool keepSwitch = false, bool noRelic = false)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, noRelic);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetInt(TtdCapKey, turns);
            // 既定で 7層も Super が戦う。 keepSwitch なら撤回済みの Optimal 切替を足して測る。
            if (keepSwitch) SessionState.SetBool(Layer7OptimalKey, true);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        // ---- 不偏 lift の混入 (2026-08-22) ----
        //  ランダム化ホールドアウトの `ExploreLift` は実装済みなのに **参照ゼロ**だった。
        //  監査で BuyScore と不偏 lift の相関は r≈0.25〜0.33 ＝ 序列は「ランダムよりまし」程度。
        //  その序列は 11.5pt の価値がある (順位付けだけ潰した実測) ので、 精度が伸びしろになる。
        //  K は縮小係数 w(N)=N/(N+K)。 小さいほどホールドアウトを信じる。
        [MenuItem("Tools/AutoRun/不偏lift混入: K=100 (0pt・1000ラン)", priority = 101)]
        public static void RunExploreBlend100() => LaunchExploreBlend(100);

        [MenuItem("Tools/AutoRun/不偏lift混入: K=50 (0pt・1000ラン)", priority = 102)]
        public static void RunExploreBlend50() => LaunchExploreBlend(50);

        [MenuItem("Tools/AutoRun/不偏lift混入: K=200 (0pt・1000ラン)", priority = 103)]
        public static void RunExploreBlend200() => LaunchExploreBlend(200);

        private static void LaunchExploreBlend(int k)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetInt(ExploreBlendKey, k);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/アブレーション: 航行を潰す (0pt・1000ラン)", priority = 97)]
        public static void RunAblateNavigation() => LaunchAblation(1);

        [MenuItem("Tools/AutoRun/アブレーション: ショップ購入を潰す (0pt・1000ラン)", priority = 98)]
        public static void RunAblateShop() => LaunchAblation(2);

        [MenuItem("Tools/AutoRun/アブレーション: イベント選択を潰す (0pt・1000ラン)", priority = 99)]
        public static void RunAblateEvent() => LaunchAblation(3);

        /// <summary>ショップ 20.3pt の内訳。 **足切りは残して順位付けだけ潰す** ──
        /// 「買ってよいものを知っている」と「その中の最良を知っている」を分ける。</summary>
        [MenuItem("Tools/AutoRun/アブレーション: ショップの順位付けのみ潰す (0pt・1000ラン)", priority = 100)]
        public static void RunAblateShopScore() => LaunchAblation(4);

        private static void LaunchAblation(int axis)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetInt(AblateKey, axis);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        // ---- ロールアウト内方策の ttk 価格 (2026-08-22) ----
        //  `u = -ttk×A − 被ダメ×1.0 + 充電×0.3`。 **全ロールアウトがここを通る。**
        //  A=4 は「1 ターン短縮 = 4 HP」の値付けだが、 1 ターン伸びれば敵の攻撃を
        //  丸ごと 1 回余計に受ける ── 実測の敵攻撃値 11〜34 からすると実勢は 15〜30 HP 相当。
        //  **現行は 1/4〜1/8 に安く見積もっている疑いがある。**
        [MenuItem("Tools/AutoRun/ttk価格: 10.0 (0pt・1000ラン)", priority = 94)]
        public static void RunTtk10() => LaunchTtkWeight(100);

        [MenuItem("Tools/AutoRun/ttk価格: 20.0 (0pt・1000ラン)", priority = 95)]
        public static void RunTtk20() => LaunchTtkWeight(200);

        [MenuItem("Tools/AutoRun/ttk価格: 2.0 (逆側・0pt・1000ラン)", priority = 96)]
        public static void RunTtk2() => LaunchTtkWeight(20);

        private static void LaunchTtkWeight(int x10)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetInt(TtkWeightKey, x10);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        // ---- 残HP の値付け掃引 (2026-08-22) ----
        //  効用は p × (1 + winHpWeight × HP率)。 既定 0.5 では満タン勝利 1.5 対 瀕死勝利 1.0 で
        //  **1.5 倍の開きしかない**。 7層は 4 連戦、 しかも役札の連戦持ち越しを採用したので
        //  「HP は跨いで効くが役札は跨いで減る」── 残HP の価値は非線形寄りのはず。
        //  **下側 (0.25) も測る** ── 上げて効いた時に「HPを重く見るのが正しい」のか
        //  「動かせば何か起きる」のかを区別するため。 単調でなければ採らない。
        [MenuItem("Tools/AutoRun/残HP値付け: winHpWeight 0.25 (0pt・1000ラン)", priority = 90)]
        public static void RunWinHp025() => LaunchWinHpWeight(25);

        [MenuItem("Tools/AutoRun/残HP値付け: winHpWeight 1.00 (0pt・1000ラン)", priority = 91)]
        public static void RunWinHp100() => LaunchWinHpWeight(100);

        [MenuItem("Tools/AutoRun/残HP値付け: winHpWeight 2.00 (0pt・1000ラン)", priority = 92)]
        public static void RunWinHp200() => LaunchWinHpWeight(200);

        private static void LaunchWinHpWeight(int pct)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetInt(WinHpWeightKey, pct);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        // ---- リロールの全列挙 (2026-08-22) ----
        //  8 標本では確率 1/36 の引き (1,1,1 から 1 を 2 つ = 〈極〉) が構造的に見えない。
        //  6 面なら 1 個で 6 通り・2 個で 36 通りなので、 標本より安いか同程度で厳密になる。
        //  実測で採用リロールの平均は 1.60 個 ＝ **いちばん頻繁な判断がいちばん安く直せる**。
        [MenuItem("Tools/AutoRun/リロール全列挙: 2個まで (0pt・1000ラン)", priority = 88)]
        public static void RunExactReroll2() => LaunchExactReroll(2);

        [MenuItem("Tools/AutoRun/リロール全列挙: 3個まで (0pt・1000ラン)", priority = 89)]
        public static void RunExactReroll3() => LaunchExactReroll(3);

        // ---- 別シードでの再現確認 (2026-08-22) ----
        //  seed1 の結果は 37.7% → 40.3% / **p=0.0976** で、 有意に 2 件足りない。
        //  同日に階層DP は同程度の数字から条件を変えて符号が反転し、
        //  chargeRerollGain は条件を変えたら効果が消えている。 **際どい 1 本では採らない。**
        //  対照と変種を必ずペアで別シードへ振る (変種だけ振っても比較にならない)。
        [MenuItem("Tools/AutoRun/リロール全列挙: 再現確認 対照 (seed2・1000ラン)", priority = 90)]
        public static void RunExactRerollSeed2Base() => LaunchExactReroll(0, "2");

        [MenuItem("Tools/AutoRun/リロール全列挙: 再現確認 2個まで (seed2・1000ラン)", priority = 91)]
        public static void RunExactRerollSeed2() => LaunchExactReroll(2, "2");

        /// <summary>遺物ありでも同じ向きか。 対照は ttd 修正後の 74.9% (batch_161424)。
        /// 2シード併合で p=0.0237 だが、 **条件を変えたら消えた例が同日に 2 件ある**ので必ず測る。</summary>
        [MenuItem("Tools/AutoRun/リロール全列挙: 2個まで 遺物あり (0pt・1000ラン)", priority = 92)]
        public static void RunExactRerollRelic() => LaunchExactReroll(2, null, noRelic: false);

        /// <summary><paramref name="maxDice"/> が 0 なら全列挙を差さない対照群。</summary>
        private static void LaunchExactReroll(int maxDice, string seedOverride = null, bool noRelic = true)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, noRelic);
            Skill = AutoRunner.WiringSkill.Super;
            if (maxDice > 0) SessionState.SetInt(ExactRerollKey, maxDice);
            if (!string.IsNullOrEmpty(seedOverride))
                SessionState.SetString(SeedOverrideKey, seedOverride);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        // ---- 7層 役札の連戦持ち越し (2026-08-22) ----
        //  現行は形態ごとに usedRoles をクリアする。 1 形態 ≒ 10 ターンでは
        //  「見送ると 8〜9 割の確率で二度と成立しない」ので温存が成立しない。
        //  持ち越すと 16 枚を 40 ターン級へ配分する問題になり、 温存が技量軸として立つ。
        //  **7層でしかクリア判定に届かないので、 遺物ありで測る** (遺物なしの 7層到達は薄い)。
        [MenuItem("Tools/AutoRun/7層役札持ち越し: 現行(持ち越しON) 遺物あり (0pt・1000ラン)", priority = 85)]
        public static void RunVescaKeepRoles() => LaunchVescaKeepRoles(false);

        [MenuItem("Tools/AutoRun/7層役札持ち越し: 旧仕様(OFF) 遺物あり (0pt・1000ラン)", priority = 86)]
        public static void RunVescaKeepRolesBase() => LaunchVescaKeepRoles(true);

        /// <summary><paramref name="forceOff"/> で旧仕様 (形態ごとリセット) の対照アームになる。</summary>
        private static void LaunchVescaKeepRoles(bool forceOff)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, false);
            Skill = AutoRunner.WiringSkill.Super;
            if (forceOff) SessionState.SetBool(VescaKeepRolesKey, true);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        // 遺物ありでも同じ向きに効くか。 **階層DP はここで符号が反転した** (0pt遺物なし +2.3pt /
        //   0pt遺物あり −3.3pt、 どちらも有意)。 1 条件の有意は一般化ではない。
        //   対照は既存の 0pt+遺物 アーム (batch_20260821_235558 / 57.5%)。
        [MenuItem("Tools/AutoRun/充電値付け: gain 0.20 遺物あり (0pt・1000ラン)", priority = 83)]
        public static void RunRerollGain20Relic() => LaunchRerollGain(20, false);

        [MenuItem("Tools/AutoRun/充電値付け: 対照 遺物あり (0pt・1000ラン)", priority = 84)]
        public static void RunRerollGainRelicBase() => LaunchRerollGain(0, false);

        private static void LaunchRerollGain(int gainPct, bool noRelic = true)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, noRelic);
            Skill = AutoRunner.WiringSkill.Super;
            if (gainPct > 0) SessionState.SetInt(RerollGainKey, gainPct);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>同点の中身を数える (0pt・遺物なし・Super)。 **判断を変えない診断**なので、
        /// 7層クリアが対照 (17.8%) と一致するはず ── 一致しなければ計装が盤面へ漏れている。</summary>
        [MenuItem("Tools/AutoRun/飽和センサス: 同点の中身 (0pt・1000ラン)", priority = 78)]
        public static void RunSaturationCensus()
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetBool(SatCensusKey, true);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary><paramref name="cap"/> が 0 以下なら拡張を差さない対照群。</summary>
        private static void LaunchTieExpand(int cap, int runs)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;
            if (cap > 0) SessionState.SetInt(TieExpandKey, cap);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, runs);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        private static void LaunchFloorDp(int discountPct, int runs = 100, int hpBands = 0,
                                          string seedOverride = null,
                                          int challengeScore = 0, bool noRelic = true)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, noRelic);
            Skill = AutoRunner.WiringSkill.Super;
            if (discountPct > 0) SessionState.SetInt(FloorDpKey, discountPct);
            if (hpBands > 0) SessionState.SetInt(FloorDpBandsKey, hpBands);
            if (!string.IsNullOrEmpty(seedOverride))
                SessionState.SetString(SeedOverrideKey, seedOverride);
            SessionState.SetInt(ChalOneScoreKey, challengeScore);
            SessionState.SetInt(ChalOneRunsKey, runs);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary><paramref name="rolloutsPerAction"/> が 0 以下なら sink を差さない対照群。</summary>
        private static void LaunchUltraRollout(int runs, int rolloutsPerAction)
        {
            bool useRollout = rolloutsPerAction > 0;
            // **worker のビルドは Play Mode に入る前に済ませる。** BuildPipeline は
            // Play Mode 中には走らないので、 sink を差す OnPlayModeChanged の中では手遅れ。
            if (useRollout && !UltraProductionWorkerBuild.EnsureBuilt(out string buildError))
            {
                Debug.LogError("[AutoRunMenu][Ultra] worker build failed; rollout バッチを起動しない: "
                    + buildError);
                return;
            }
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;   // 戦闘は据え置き。 差し替えるのはマクロ側
            if (useRollout)
            {
                SessionState.SetInt(UltraRolloutRunsKey, runs);
                SessionState.SetInt(UltraRolloutPerActionKey, rolloutsPerAction);
            }
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, runs);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        private static void LaunchUltraSmoke(int sinkKind, int runs = 30)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;   // 戦闘は据え置き。 見たいのはマクロ側の疎通
            SessionState.SetInt(UltraSmokeSinkKey, sinkKind);
            SessionState.SetInt(ChalOneScoreKey, 0);
            SessionState.SetInt(ChalOneRunsKey, runs);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>Erase the SessionState keys that select an alternative batch mode or
        /// override the loadout. Used by the Ultra entry point so the profile is the only
        /// thing that decides the conditions.</summary>
        private static void ClearBatchModeSessionState()
        {
            // **出目パーツは EditorPrefs なのでセッションを跨いで残る。**
            //   2026-08-22: 計測用ランチャーが誰もこれを設定していなかったため、
            //   08-17 の「パーツ無し」測定の設定を丸ごと引き継いだまま 10 本以上の A/B を回した
            //   (7層クリアの絶対値が実勢 33% に対し 16% になっていた)。
            //   A/B 自体は両アームが同条件なので無傷だったが、 基準値としては使えない。
            //   **既定は製品挙動 = パーツ有り。** パーツ無しで測りたいメニューは
            //   ここを通さず自分で立てる (技量帯スイープの「パーツ無し」版がそれ)。
            EditorPrefs.SetBool(NoPartsKey, false);
            // ITT 優先度も EditorPrefs。 **既定は従来の観測 regβ**。 立てっぱなしだと
            //   「どちらの推定量で並んだ BOT を測ったのか」が分からなくなる。
            EditorPrefs.SetBool(IttKey, false);
            // **武器家系固定も同じ穴 (2026-09-09)。** アイテム学習 本走 が true を立てるので、
            //   その後に測定メニューを回すと「乗り換えない BOT」を測ってしまう。
            //   実測で対照アームの最終武器が 24.7/24.7/24.3/23.1% とほぼ均等になり、
            //   ITT アーム (42.3/19.7/16.8/16.3%) と別条件になっていた ──
            //   +21.5pt のうち序列の取り分が分離できなくなる。 **既定は製品挙動 = 乗り換えあり**。
            EditorPrefs.SetBool(LockFamKey, false);
            // 挑戦pt固定も EditorPrefs。 **既定は 0 = 触らない**。 立てっぱなしだと
            //   0pt のつもりの測定が高難易度帯で走る (今日 5 回踏んだ残留の穴と同型)。
            EditorPrefs.SetInt(ChalTargetKey, 0);
            EditorPrefs.SetString(RankSpecKey, "");   // 配分スペックも残留させない
            // 強盗の罰も EditorPrefs (2026-09-12)。 **既定は製品挙動 = 価格割増**。
            //   切り分け用の「旧出禁」を立てっぱなしにすると、 以降の全バッチが旧仕様で走る。
            EditorPrefs.SetBool(RobBlockKey, false);
            SessionState.EraseBool(UltraCensusKey);
            SessionState.EraseInt(FloorDpKey);
            SessionState.EraseInt(FloorDpBandsKey);
            SessionState.EraseInt(TieExpandKey);
            SessionState.EraseBool(SatCensusKey);
            SessionState.EraseInt(RerollGainKey);
            SessionState.EraseBool(VescaKeepRolesKey);
            SessionState.EraseInt(ExactRerollKey);
            SessionState.EraseInt(WinHpWeightKey);
            SessionState.EraseInt(TtkWeightKey);
            SessionState.EraseInt(AblateKey);
            SessionState.EraseInt(ExploreBlendKey);
            SessionState.EraseBool(Layer7OptimalKey);
            SessionState.EraseInt(TtdCapKey);
            SessionState.EraseString(SeedOverrideKey);
            // **static なので前バッチから残る。** 毎回既定 (混ぜない) へ戻す。
            AutoTest.LearnedPriorityProvider.ExploreBlendK = 0f;
            SessionState.EraseInt(UltraRolloutRunsKey);
            SessionState.EraseInt(UltraRolloutPerActionKey);
            SessionState.EraseBool(ChalSweepKey);
            SessionState.EraseBool(SkillCmpKey);
            SessionState.EraseBool(SkillCmp3Key);
            SessionState.EraseInt(SkillCmpRunsKey);
            SessionState.EraseInt(SkillCmpScoreKey);
            SessionState.EraseBool(SkillCmpBudgetKey);
            SessionState.EraseInt(ChalOneScoreKey);
            SessionState.EraseInt(ChalOneRunsKey);
            SessionState.EraseString(ChalSweepScoresKey);
            SessionState.EraseInt(ChalSweepRunsKey);
            SessionState.EraseBool(RelicSweepKey);
            SessionState.EraseBool(RelicBaseKey);
            SessionState.EraseBool(AxisSweepKey);
            SessionState.EraseBool(GrantTrialKey);
            SessionState.EraseInt(GrantTrialRunsKey);
            SessionState.EraseBool(GrantAllKindsKey);
            SessionState.EraseInt(GrantItemsKey);
            SessionState.EraseBool(ItemAblKey);
            SessionState.EraseString(ItemAblIdsKey);
            SessionState.EraseInt(ItemAblRunsKey);
            SessionState.EraseBool(AxisDiagKey);
            SessionState.EraseBool(ChalAxisKey);
            SessionState.EraseBool(ChalAxisNoRelicKey);
            SessionState.EraseBool(ChalAxisChangedKey);
            SessionState.EraseBool(ChalCatKey);
            SessionState.EraseBool(ChalCatT4Key);
            SessionState.EraseInt(ChalCatOnlyKey);
            SessionState.EraseBool(ChalCatAloneKey);
            SessionState.EraseInt(ChalAxisSeedBaseKey);
            SessionState.EraseBool(ChalAxisMaxKey);
            SessionState.EraseBool(ChalAxisSmokeKey);
            SessionState.EraseInt(ChalAxisOnlyKey);
            SessionState.EraseInt(ChalAxisDetKey);
            SessionState.EraseBool(ChalAxisDiffKey);
            SessionState.EraseBool(ChalAxisPriceKey);
            SessionState.EraseBool(OptimalParallelBenchKey);
            SessionState.EraseBool(PersonaKey);
            SessionState.EraseBool(AscendKey);
            SessionState.EraseInt(AscendRunsKey);
            SessionState.EraseInt(TierCalScoreKey);
            SessionState.EraseInt(TierCalRunsKey);
            SessionState.EraseInt(TierCalBatchesKey);
            SessionState.EraseBool(TierCalResumeKey);
            SessionState.EraseInt(LoopKey);
        }

        [MenuItem("Tools/AutoRun/50pt対アーム/② 遺物なし 50pt × 10000ラン (天井AI)", priority = 11)]
        public static void RunSuper50NoRelic()
        {
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetInt(ChalOneScoreKey, 50);
            SessionState.SetInt(ChalOneRunsKey, 10000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>難易度カーブを 1 本で採る (2026-08-17)。 0/30/50pt を**同一バッチ**で回すので、
        /// 別々に起動したときのような状態の持ち越し (遺物・挑戦ロードアウトの residual) が起きない。</summary>
        [MenuItem("Tools/AutoRun/難易度カーブ: 0/30/50pt × 各1000ラン (天井AI・遺物アリ)", priority = 11)]
        public static void RunChallengeCurve1000WithRelic()
        {
            EditorPrefs.SetBool(NoRelicKey, false);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetString(ChalSweepScoresKey, "0,30,50");
            SessionState.SetInt(ChalSweepRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>0pt を**遺物なし**で単点測定する。 上の難易度カーブ (遺物アリ) と
        /// **同じスイープ経路**を通るのでペルソナが Standard 固定で揃い、 カーブと直接並べられる。
        /// 通常バッチで 0pt を測るとペルソナ 10 種混在になり、 混ぜて読むと取り違える。</summary>
        [MenuItem("Tools/AutoRun/難易度カーブ: 0pt のみ × 1000ラン (天井AI・遺物なし)", priority = 11)]
        public static void RunChallengeCurve0NoRelic()
        {
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Super;
            SessionState.SetString(ChalSweepScoresKey, "0");
            SessionState.SetInt(ChalSweepRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        // ---- 難易度カーブ (ノーマルAI版) ----
        //   天井AI 版と対になる。 **AI を跨いで並べない** ── 技量帯が変わると
        //   同じ挑戦ptでも別の曲線になるので、 比較は必ず同じ AI 同士で行う。
        [MenuItem("Tools/AutoRun/難易度カーブ: 0/30/50pt × 各1000ラン (ノーマルAI・遺物アリ)", priority = 11)]
        public static void RunChallengeCurve1000WithRelicOptimal()
        {
            EditorPrefs.SetBool(NoRelicKey, false);
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetString(ChalSweepScoresKey, "0,30,50");
            SessionState.SetInt(ChalSweepRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/難易度カーブ: 0pt のみ × 1000ラン (ノーマルAI・遺物なし)", priority = 11)]
        public static void RunChallengeCurve0NoRelicOptimal()
        {
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetString(ChalSweepScoresKey, "0");
            SessionState.SetInt(ChalSweepRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        // ---- 帯別 BOT 学習の積み上げ (2026-08-17) ----
        //   挑戦スコア帯ごとに item_stats.json を分けたので、 帯ごとに実際に回して積む必要がある。
        //   **AutoRun.LearnTier を立てないと 1 バイトも積まれない** (既定 OFF)。
        //   ログ冒頭の「学習: … Tier=True」が唯一の確認手段。
        /// <summary>帯別 BOT 学習を積む。
        ///
        /// <para><paramref name="arms"/> は 1000 ラン単位のアーム数。 <b>高難度帯ほど多く要る</b> ──
        /// 同じラン数でも高難度はランが短くショップ訪問が減るので、 探索決定の総数が落ちる。
        /// 実測 (2026-08-17・各10000ラン): 不偏 lift が使える品は 0pt 90 / 30pt 60 と、
        /// 難度が上がるほど比較線に乗る品が減った。</para></summary>
        private static void LaunchBandLearning(int score, int arms = 10)
        {
            EditorPrefs.SetBool(LearnTierKey, true);
            EditorPrefs.SetBool(NoRelicKey, false);
            Skill = AutoRunner.WiringSkill.Optimal;
            var parts = new string[arms];
            for (int i = 0; i < parts.Length; i++) parts[i] = score.ToString();
            SessionState.SetString(ChalSweepScoresKey, string.Join(",", parts));
            SessionState.SetInt(ChalSweepRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/帯別学習/厚取り: 30pt 帯 (1000ラン × 30)", priority = 16)]
        public static void RunBandLearning30Thick() => LaunchBandLearning(30, 30);

        [MenuItem("Tools/AutoRun/帯別学習/厚取り: 50pt 帯 (1000ラン × 40)", priority = 16)]
        public static void RunBandLearning50Thick() => LaunchBandLearning(50, 40);

        [MenuItem("Tools/AutoRun/帯別学習/0pt 帯を積む (1000ラン × 10・ノーマルAI)", priority = 15)]
        public static void RunBandLearning0() => LaunchBandLearning(0);

        [MenuItem("Tools/AutoRun/帯別学習/30pt 帯を積む (1000ラン × 10・ノーマルAI)", priority = 15)]
        public static void RunBandLearning30() => LaunchBandLearning(30);

        [MenuItem("Tools/AutoRun/帯別学習/50pt 帯を積む (1000ラン × 10・ノーマルAI)", priority = 15)]
        public static void RunBandLearning50() => LaunchBandLearning(50);

        /// <summary>現行参照AIで、理論値遺物を持ち挑戦50pt（全軸T3＋全T4）を単点測定する。</summary>
        [MenuItem("Tools/AutoRun/挑戦50pt 全ON × 1000ラン (理論値遺物・最適AI)", priority = 10)]
        public static void RunOptimalAt50()
        {
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetInt(ChalOneScoreKey, 50);
            SessionState.SetInt(ChalOneRunsKey, 1000);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>AI 技量比較用。各プロファイルを独立したバッチとして出力し、
        /// 同じ 10000 本の決定論シードで比較できるようにする。</summary>
        [MenuItem("Tools/AutoRun/AI比較10K/1 素朴AI 50pt × 10000ラン", priority = 10)]
        public static void RunNaiveAt50TenThousand()
            => RunSkillAt50(AutoRunner.WiringSkill.Naive, 10000);

        [MenuItem("Tools/AutoRun/AI比較10K/2 通常AI 50pt × 10000ラン", priority = 10)]
        public static void RunOptimalAt50TenThousand()
            => RunSkillAt50(AutoRunner.WiringSkill.Optimal, 10000);

        [MenuItem("Tools/AutoRun/AI比較10K/3 Super AI 50pt × 10000ラン", priority = 10)]
        public static void RunSuperAt50TenThousand()
            => RunSkillAt50(AutoRunner.WiringSkill.Super, 10000);

        private static void RunSkillAt50(AutoRunner.WiringSkill skill, int runs)
        {
            Skill = skill;
            SessionState.SetInt(ChalOneScoreKey, 50);
            SessionState.SetInt(ChalOneRunsKey, runs);
            SessionState.SetBool(ChalSweepKey, true);
            Launch(1);
        }

        /// <summary>〈最後の審判〉の効果を、既定の 60000 帯とは独立したシードで再測定する。
        /// 基準 + 審判単独 + ノイズ床だけを各 1000 ラン回し、混沌的発散による偶然の差かを判定する。</summary>
        [MenuItem("Tools/AutoRun/診断: 最後の審判 別シード帯 (基準 vs 審判・各1000ラン)", priority = 13)]
        public static void RunFinalJudgmentAlternateSeedDiag()
        {
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisNoRelicKey, true);
            SessionState.SetBool(ChalCatKey, true);
            SessionState.SetBool(ChalCatAloneKey, true);
            SessionState.SetInt(ChalCatOnlyKey, (int)MetaProgression.ChallengeCategory.E崩壊);
            SessionState.SetInt(ChalAxisSeedBaseKey, 70000);
            Launch(1);
        }

        /// <summary>正式な配点条件で E崩壊 6pt と 6pt+〈最後の審判〉を別シード比較する。</summary>
        [MenuItem("Tools/AutoRun/診断: 最後の審判 配点検証 (E6pt vs E6pt+審判・各1000ラン)", priority = 13)]
        public static void RunFinalJudgmentPricingDiag()
        {
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisNoRelicKey, true);
            SessionState.SetBool(ChalCatKey, true);
            SessionState.SetInt(ChalCatOnlyKey, (int)MetaProgression.ChallengeCategory.E崩壊);
            SessionState.SetInt(ChalAxisSeedBaseKey, 70000);
            Launch(1);
        }

        /// <summary>**固着の再現用**。 A生存圧+破綻 アームだけを進捗毎ラン で回す。
        ///
        /// 2026-08-11: T4 スイープが arm 2/7 (A生存圧+破綻) の 850〜900 ラン目付近で
        /// 固着した。 番犬 (実時間) も反復上限もストール検出も**発火しなかった** ──
        /// この 3 つは全て yield のたびに判定するので、 **1 フレーム内で回り続ける
        /// ループ**には効かない。 進捗を毎ラン出せば、 Editor.log の最終行が
        /// 固着したラン番号をそのまま指す。</summary>
        [MenuItem("Tools/AutoRun/T4 固着再現: A生存圧+破綻 のみ (進捗毎ラン)", priority = 13)]
        public static void RunT4StallRepro()
        {
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisNoRelicKey, true);
            SessionState.SetBool(ChalCatKey, true);
            SessionState.SetBool(ChalCatT4Key, true);
            SessionState.SetInt(ChalCatOnlyKey, (int)MetaProgression.ChallengeCategory.A生存圧);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/挑戦 単軸スイープ (遺物なし・ノーマルAI・各1000ラン)", priority = 13)]
        public static void RunChallengeAxisSweepNoRelic()
        {
            Skill = AutoRunner.WiringSkill.Optimal;   // 天井ではなく現行の参照点で測る
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisNoRelicKey, true);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/挑戦 単軸スイープ (全23段 × 各300ラン)", priority = 13)]
        public static void RunChallengeAxisSweep()
        {
            SessionState.SetBool(ChalAxisKey, true);
            Launch(1);
        }

        /// <summary>上の短縮版。 各軸の最上位段だけ (12 アーム) を回す。 段のカーブは見えないが
        /// 「どの軸が一番重いか」だけなら半分の時間で出る。</summary>
        [MenuItem("Tools/AutoRun/挑戦 単軸スイープ: 最上位段のみ (12軸 × 各300ラン)", priority = 13)]
        public static void RunChallengeAxisSweepMaxOnly()
        {
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisMaxKey, true);
            Launch(1);
        }

        /// <summary>上の動作確認版。 **ログを抑止せず** 各段 3 ラン だけ回す。
        /// 本測定は 6900 ラン・50 分級で進捗が一切出ないので、 先にこれで通ることを見る。</summary>
        /// <summary>乖離調査: 基準アームだけを 20 ラン 回して先頭 10 ランの到達層を採る。
        /// 技量帯スイープの同一 runIdx と突き合わせるため。</summary>
        [MenuItem("Tools/AutoRun/診断: 単軸スイープの先頭10ラン (基準のみ・20ラン)", priority = 13)]
        public static void RunAxisProbe()
        {
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisNoRelicKey, true);
            SessionState.SetBool(ChalAxisSmokeKey, true);
            SessionState.SetInt(ChalAxisOnlyKey, 0);   // 1 軸だけ = 基準 + その軸
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/挑戦 単軸スイープ: 動作確認 (各3ラン・ログ有)", priority = 13)]
        public static void RunChallengeAxisSweepSmoke()
        {
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisSmokeKey, true);
            Launch(1);
        }

        /// <summary>1 軸だけを全段で回す診断。 基準 + その軸の段数 だけなので 900〜1200 ラン。
        /// 「価格 +15% と +35% が同じ結果になる」のような 1 軸の疑問に、
        /// 全 23 段 (6900 ラン) を回さずに答える。 **対話ダイアログは置かない** ──
        /// このメニューは MCP から自動実行するので、 モーダルが出ると押せずに固まる。</summary>
        private static void LaunchChallengeAxisOne(MetaProgression.ChallengeAxis axis)
        {
            int idx = MetaProgression.ChallengeCatalog.Axes.FindIndex(a => a.axis == axis);
            if (idx < 0) { Debug.LogError($"[AutoRun] 軸が見つからない: {axis}"); return; }
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetInt(ChalAxisOnlyKey, idx);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/診断: 挑戦1軸/搾取経済 (全段 × 各300ラン)", priority = 13)]
        public static void RunChalAxisOneShopPrice()
            => LaunchChallengeAxisOne(MetaProgression.ChallengeAxis.搾取経済);

        [MenuItem("Tools/AutoRun/診断: 挑戦1軸/宿屋連合 (全段 × 各300ラン)", priority = 13)]
        public static void RunChalAxisOneInn()
            => LaunchChallengeAxisOne(MetaProgression.ChallengeAxis.宿屋連合);

        [MenuItem("Tools/AutoRun/診断: 挑戦1軸/長引く負傷 (全段 × 各300ラン)", priority = 13)]
        public static void RunChalAxisOneWound()
            => LaunchChallengeAxisOne(MetaProgression.ChallengeAxis.長引く負傷);

        /// <summary>決定性の検査。 **0pt だけの同一アームを 4 本**並べ、 ラン単位で指紋を照合する。
        /// 同一シード・同一設定なので全ラン一致が正。 一致しないならラン間で状態が漏れており、
        /// 軸間比較が全て信用できない (2026-08-10 に 4.0pt の食い違いを観測)。</summary>
        [MenuItem("Tools/AutoRun/診断: 決定性検査 (0pt × 4アーム × 各300ラン)", priority = 13)]
        public static void RunDeterminismCheck()
        {
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetInt(ChalAxisDetKey, 4);
            Launch(1);
        }

        /// <summary>決定性の破れを **1 ラン単位で捕まえる**。 同一設定・同一 runIdx の 2 アームを
        /// 1 ラン ずつ回し、 ナラティブの最初に分岐した行を出す。 ログ抑止を切る必要があるので
        /// ラン数は最小にする。</summary>
        [MenuItem("Tools/AutoRun/診断: 決定性 1ラン差分 (2アーム × 1ラン)", priority = 13)]
        public static void RunDeterminismDiff()
        {
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetInt(ChalAxisDetKey, 2);
            SessionState.SetBool(ChalAxisDiffKey, true);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/診断: 通常AI 並列化 (直列 vs 並列・各100ラン)", priority = 13)]
        public static void RunOptimalParallelBenchmark()
        {
            Skill = AutoRunner.WiringSkill.Optimal;
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisNoRelicKey, true);
            SessionState.SetInt(ChalAxisDetKey, 2);
            SessionState.SetBool(OptimalParallelBenchKey, true);
            Launch(1);
        }

        /// <summary>ショップ価格倍率の弾性曲線。 挑戦デバフは 0pt のまま倍率だけを振る。
        /// ×1.15 と ×1.35 で 7F差分がほぼ同じだった原因 (第一段で弾性を使い切っているのか、
        /// 別要因か) を、 折れ点を見て切り分ける。</summary>
        [MenuItem("Tools/AutoRun/診断: ショップ価格 弾性曲線 (7点 × 各300ラン)", priority = 13)]
        public static void RunShopPriceCurve()
        {
            SessionState.SetBool(ChalAxisKey, true);
            SessionState.SetBool(ChalAxisPriceKey, true);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/遺物プリセットスイープ (各300ラン)", priority = 12)]
        public static void RunRelicPresetSweep()
        {
            SessionState.SetBool(RelicSweepKey, true);
            Launch(1);
        }

        /// <summary><b>ランダム付与試行 (2026-09-08)。</b> 1 ランに複数品をランダムに配り、
        /// <b>割り当てベクトルだけ</b>で回帰する ── 交絡の無い全品同時測定。
        ///
        /// <para>1 品ずつのアブレーションより 2〜5 倍安い (期待4品/ラン で SE 0.075 に約6万ラン、
        /// 1品ずつなら 18万ラン)。 <b>付与層をランごとにランダム化</b>しているので、
        /// 開幕固定が複利系 (金を生む品) だけを贔屓する問題も避けられる。
        /// <b>金も 1 つの処置</b>として混ぜてあるので、 その係数が 1G の band 価値になり、
        /// アイテムが生んだ金額を換算して引ける ＝ 戦闘への純粋な寄与に分解できる。</para>
        ///
        /// <para><b>学習は切る。</b> 配った品を BOT が「選んで買った」として積むと、
        /// 累積の学習データが壊れる。 出力は AutoRunLogs/grant_trial/grant_runs.csv に隔離する。</para>
        ///
        /// <para><b>集計は割り当てだけで回すこと (ITT)。</b> BOT が後から買った品を説明変数に
        /// 入れてはいけない ── 購入は処置の下流なので、 経済アイテムの効果が媒介変数に吸われ、
        /// さらにコライダー条件づけで偏りが入る。</para></summary>
        /// <summary><b>難易度の確認 (2026-09-08)。</b> 学習を切って 3,000 ラン 回し、
        /// 7 層クリア率・所持品数・Λ の配給を見る。
        ///
        /// <para>訓練済み BOT (Optimal) の 7層クリアが <b>66.9%</b> に達し、 band 11 に張り付いて
        /// アイテムの効果が測れなくなっていた (設計目標は人間で「2割強」)。 緩和が 3 つ積み上がった結果:
        /// 08-16 のエスカレーション緩和とボス前 Shop+Rest 確定配置、 09-06 の罠アイテム 50 品削除
        /// (48%→65%)。 A: 曲線を巻き戻し、 B: Λ デバフ間隔 3→2 を入れた効果を測る。</para>
        ///
        /// <para><b>学習は切る。</b> 学習を回すと新しい難易度に BOT が再適応してしまい、
        /// 「同じ BOT で難易度だけ変えた差」が読めなくなる。 まず据え置きの BOT で測る。</para></summary>
        [MenuItem("Tools/AutoRun/難易度確認 (メタStandard・遺物なし・0pt・学習OFF・3000ラン)", priority = 11)]
        public static void RunDifficultyCheck()
        {
            ClearBatchModeSessionState();
            // **学習を止める (2026-09-09)。** メニュー名は「学習OFF」なのに `LearnTier` は
            //   EditorPrefs で残るため、 実際には Tier=True で走っていた。 床 (メタOff) で
            //   これを走らせると存在しなかった `learning/buffOff_debuffOff/` が**作られる**ので、
            //   2 回目以降の床測定が「学習なし」でなくなる ── 4 点測定の 1 点目が自壊する。
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            // **メタモードを必ず明示する。** EditorPrefs に残るので、 床測定 (Off) や
            //   パネル分解 (BuildFocused/None) の直後に回すと、 その条件のまま走って
            //   「天井を測ったつもりが中間だった」という取り違えになる (2026-09-09 に 2 回踏んだ)。
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            EditorPrefs.SetBool(SweepAxisKey, false);
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(3000);
        }

        /// <summary><b>バランスの床の測定 (2026-09-08)。</b> メタバフ Off ＝ メタ未取得の新規プレイヤー。
        ///
        /// <para><b>なぜ要るか。</b> これまでの基準は <c>メタ進行 全有効化(Lv58)</c> で測っていた。
        /// ダメージ計装によれば整備パネルは <b>与ダメ ×1.30</b> と攻撃端子+2 を常時乗せている ──
        /// <b>周回で積む引継ぎボーナスであり、 遺物とまったく同じカテゴリ</b>である。
        /// 遺物を基準から外した理由が「周回引継ぎなので初回プレイヤーは 0 個」なら、
        /// メタ進行 Lv58 にも同じ理屈が当てはまる。 <see cref="AutoRunner.MetaBuffMode.Off"/> の
        /// 定義自体が「メタ未取得の新規プレイヤー＝<b>バランスの床</b>」と書いてある。</para></summary>
        [MenuItem("Tools/AutoRun/バランスの床 (メタOff・遺物なし・0pt・学習OFF・3000ラン)", priority = 11)]
        public static void RunFloorMeasure()
        {
            ClearBatchModeSessionState();
            // **学習を止める (2026-09-09)。** メニュー名は「学習OFF」なのに `LearnTier` は
            //   EditorPrefs で残るため、 実際には Tier=True で走っていた。 床 (メタOff) で
            //   これを走らせると存在しなかった `learning/buffOff_debuffOff/` が**作られる**ので、
            //   2 回目以降の床測定が「学習なし」でなくなる ── 4 点測定の 1 点目が自壊する。
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            MetaMode = AutoRunner.MetaBuffMode.Off;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(3000);
        }

        /// <summary><b>整備パネル本体の寄与を切り出す (2026-09-09)。</b>
        ///
        /// <para><b>なぜ「メタOff との差」ではいけないか。</b> <see cref="AutoRunner.MetaBuffMode.Off"/> は
        /// <c>metaProfile</c> も <c>BuffOff_DebuffOff</c> へ切り替えるが、 <b>その学習ディレクトリは存在しない</b>
        /// ── つまり床の測定は「パネル 0pt」ではなく「パネル 0pt <b>かつアイテム知識ゼロ</b>」で走っていた。
        /// アブレーション実測ではショップ判断だけで −20.3pt (うち学習序列 −11.5pt) あるので、
        /// 差をパネルに帰属させると大幅に過大評価する (2026-09-09 に実際に 60.2pt と誤読した)。</para>
        ///
        /// <para><b>このアームの作り: BuildFocused + 配分 None。</b> mode が Off でないので
        /// <c>metaProfile</c> は <c>BuffOn_DebuffOff</c> のまま (学習 250,000ラン を使う)、
        /// <c>metaPattern</c> は <c>FullProgression</c> なので <c>ResetAll</c> は呼ばれず、
        /// <c>Apply(None)</c> で配点だけが 0 になる。 天井 (Balanced 36pt) との差が<b>パネル本体</b>、
        /// メタOff の床との差が<b>知識</b>。</para></summary>
        [MenuItem("Tools/AutoRun/整備パネル分解 (配点0pt・学習あり・3000ラン)", priority = 11)]
        public static void RunPanelZeroMeasure()
        {
            ClearBatchModeSessionState();
            // **学習を止める (2026-09-09)。** メニュー名は「学習OFF」なのに `LearnTier` は
            //   EditorPrefs で残るため、 実際には Tier=True で走っていた。 床 (メタOff) で
            //   これを走らせると存在しなかった `learning/buffOff_debuffOff/` が**作られる**ので、
            //   2 回目以降の床測定が「学習なし」でなくなる ── 4 点測定の 1 点目が自壊する。
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            MetaMode = AutoRunner.MetaBuffMode.BuildFocused;
            MetaAxis = MetaAllocationPresets.Preset.None;
            EditorPrefs.SetBool(SweepAxisKey, false);   // 一斉走査が生きていると軸が上書きされる
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(3000);
        }

        /// <summary><b>4 点測定の最上段 (2026-09-09)。</b> 天井 (学習あり・メタバフあり) を
        /// <b>Super</b> ＝ 戦闘決着まで前向きに読む BOT で回す。 Optimal との差が
        /// 「1 ターン貪欲では届かない読みの深さ」＝ 技量帯の上半分
        /// (<see cref="AutoRunner.WiringSkill.Super"/>)。
        ///
        /// <para><b>ラン数を 1,000 に落とす理由。</b> Super は 1 ラン 5〜10 秒 かかるので
        /// 3,000 ラン だと 4〜8 時間。 クリア率 40〜60% 帯なら n=1,000 の SE は 約1.6pt で、
        /// 他の 3 点 (n=3,000, SE 約0.9pt) との比較には足りる。 <b>数pt の差は解像しない</b>ので、
        /// 「Super が Optimal を数pt 上回った」は主張しないこと。</para></summary>
        [MenuItem("Tools/AutoRun/天井+Super (メタStandard・学習あり・遺物なし・0pt・1000ラン)", priority = 11)]
        public static void RunCeilingSuper()
        {
            ClearBatchModeSessionState();
            // **学習を止める (2026-09-09)。** メニュー名は「学習OFF」なのに `LearnTier` は
            //   EditorPrefs で残るため、 実際には Tier=True で走っていた。 床 (メタOff) で
            //   これを走らせると存在しなかった `learning/buffOff_debuffOff/` が**作られる**ので、
            //   2 回目以降の床測定が「学習なし」でなくなる ── 4 点測定の 1 点目が自壊する。
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            EditorPrefs.SetBool(SweepAxisKey, false);
            Skill = AutoRunner.WiringSkill.Super;
            Launch(1000);
        }

        /// <summary>煙テスト: 500 ラン だけ回して割り当てログの形を確かめる。
        /// 本走の前に必ず通すこと ── 6 万ラン 回してから列が壊れていたのでは遅い。</summary>
        [MenuItem("Tools/AutoRun/診断: ランダム付与 煙テスト (500ラン)", priority = 13)]
        public static void RunRandomGrantSmoke()
        {
            SessionState.SetBool(GrantTrialKey, true);
            SessionState.SetInt(GrantTrialRunsKey, 500);
            EditorPrefs.SetBool(NoRelicKey, true);
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/ランダム付与試行 (全品同時・ITT・30000ラン)", priority = 12)]
        public static void RunRandomGrantTrial()
        {
            SessionState.SetBool(GrantTrialKey, true);
            SessionState.SetInt(GrantTrialRunsKey, 30000);
            EditorPrefs.SetBool(NoRelicKey, true);        // 基準条件 (遺物なし)
            // **メタモードを明示する。** EditorPrefs に残るので、 直前に床測定 (Off) を
            //   回していると Off のまま走る ── 「触らない」と「無し」の取り違えは
            //   このプロジェクトで何度も踏んでいる罠 (2026-08-17 の遺物込み測定と同型)。
            //   測るのは band に余地のある条件なので Standard (クリア 56.1%) を使う。
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;       // 学習バッチと同条件・速度が読める
            Launch(1);
        }

        /// <summary><b>ITT 本走 (2026-09-09)。</b> ランダム付与 60,000ラン を回し、
        /// 割り当てベースの係数 (<see cref="AutoTest.GrantItt"/>) を学習ディレクトリへ書く。
        /// これが BOT の購入優先度の新しい正本になる。
        ///
        /// <para><b>なぜ観測 250,000ラン の代わりになるか。</b> 従来の regβ は
        /// 「そのランが取得した品」＝ BOT が選んだ品を説明変数に置くので内生で、
        /// 交絡が信号の約 30 倍。 ラン数を積んでも消えない。 一方こちらは
        /// 各品を独立に p ≈ 2.9% で配る疎な factorial なので、 割り当ては判断と独立。
        /// N=60,000 で 1 品あたり処置 約1,700 ラン、 回帰調整後の SE は 0.04〜0.07 band
        /// (真のアイテム間 SD 0.15 band を 1/3 の精度で見る想定)。 所要 約1時間。</para>
        ///
        /// <para><b>この走行自体では ITT を使わない</b> ── 優先度は前の値のままでよい。
        /// 割り当てがランダムである限り、 BOT がどんな方策で打っていても推定は不偏。</para></summary>
        [MenuItem("Tools/AutoRun/■ ITT 本走: ランダム付与 60000ラン → 購入優先度へ", priority = 8)]
        public static void RunIttTraining()
        {
            ClearBatchModeSessionState();
            SessionState.SetBool(GrantTrialKey, true);
            SessionState.SetInt(GrantTrialRunsKey, 60000);
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(IttKey, false);           // 採取中は旧優先度のままで良い
            // **武器家系の固定は切る (製品条件で測る)。** アイテム学習 本走 は
            //   4 家系を同じ厚みで測るために固定するが、 あれは<b>武器</b>の話。
            //   パッシブの ITT では固定してもバイアスは入らない代わりに、
            //   「乗り換えない BOT」という製品と違う集団の効果を測ることになる。
            //   EditorPrefs なので明示しないと直前のメニューの設定が residual で残る。
            EditorPrefs.SetBool(LockFamKey, false);
            MetaMode = AutoRunner.MetaBuffMode.Standard;  // band に余地のある条件で測る
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        /// <summary><b>全種プールの煙テスト (2026-09-17)。</b> 500 ラン だけ回して
        /// プール構成と割り当てログの列を確かめる。 <b>本走の前に必ず通す</b> ──
        /// 出目パーツは ItemDatabase に無い ID なので、 配る側 (AutoRunner.GrantOne) が
        /// 取りこぼしていても静かに 0 行になるだけで気付けない。</summary>
        [MenuItem("Tools/AutoRun/診断: ランダム付与 全種プール 煙テスト (500ラン)", priority = 13)]
        public static void RunRandomGrantAllKindsSmoke()
        {
            ClearBatchModeSessionState();
            SessionState.SetBool(GrantTrialKey, true);
            SessionState.SetInt(GrantTrialRunsKey, 500);
            SessionState.SetBool(GrantAllKindsKey, true);
            SessionState.SetInt(GrantItemsKey, 700);      // 7.0 品/ラン
            EditorPrefs.SetBool(NoRelicKey, true);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        /// <summary><b>ITT 本走・全種プール (2026-09-17)。</b> 消耗品・武器・出目パーツまで
        /// 付与対象にして 60,000 ラン。 これが通ると準パワーを <b>Δクリア率 (pt) 単位</b>で
        /// 全品に張れる ＝ 「この判断は何 pt か」「この出費は何 pt か」を同じ物差しで比べられる。
        ///
        /// <para><b>なぜ要るか。</b> 従来プールはパッシブ 89 品だけで、 残りは regβ (band 単位)
        /// のまま同じ序列へ混ざっていた。 band→pt の等化は引けない
        /// (2026-09-17 実測 89 対で r=−0.100 ＝ 相関が検出できない)。
        /// 単位を揃える道はプールを広げることしか無い。</para>
        ///
        /// <para><b>期待付与数 7.0。</b> プールが 89 → 約 160 品 なので、
        /// 1 品あたりの付与確率を従来 (4/89 ≈ 4.5%) に揃えるとこの値になる。
        /// 据え置くと 1 品あたりの処置ラン数が半分になり <see cref="AutoTest.GrantItt.MinTreatedRuns"/>
        /// を割る品が出る。 <b>ただし付与数が増えるぶんランは強くなり、基準クリア率が動く</b> ──
        /// pt の単位も金の交換レートも<b>同じ fit から引き直すこと</b> (itt_clear.txt の金係数)。</para>
        ///
        /// <para>ログは <c>allkinds_grant_runs.csv</c> へ分離される ── 旧設計と列の語彙も
        /// 付与確率も違うので、 追記で混ぜると切片が 2 設計の混合になる。</para></summary>
        [MenuItem("Tools/AutoRun/■ ITT 本走・全種プール: ランダム付与 60000ラン", priority = 8)]
        public static void RunIttTrainingAllKinds()
        {
            ClearBatchModeSessionState();
            SessionState.SetBool(GrantTrialKey, true);
            SessionState.SetInt(GrantTrialRunsKey, 60000);
            SessionState.SetBool(GrantAllKindsKey, true);
            SessionState.SetInt(GrantItemsKey, 700);      // 7.0 品/ラン (プール 1.8 倍ぶん)
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(IttKey, false);           // 採取中は旧優先度のままで良い
            EditorPrefs.SetBool(LockFamKey, false);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        /// <summary><b>ITT 優先度での測定 (2026-09-09)。</b> 4 点測定の 2/3 点目を、
        /// 観測 regβ ではなく ITT 係数で並べた BOT で回す。
        /// <see cref="RunIttTraining"/> を先に通しておくこと ── itt_effects.json が
        /// 無いと厳格モードで全品が未学習になり、 手書きフォールバックまで落ちる。</summary>
        [MenuItem("Tools/AutoRun/ITT で測定 (メタStandard・遺物なし・0pt・3000ラン)", priority = 11)]
        public static void RunIttMeasure()
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(IttKey, true);
            EditorPrefs.SetBool(LockFamKey, false);   // 製品条件 (武器の乗り換えあり)
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            EditorPrefs.SetBool(SweepAxisKey, false);
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(3000);
        }

        /// <summary><b>ITT の条件転移テスト (2026-09-10)。</b> 整備パネル 配点 0pt ＝
        /// クリア率 1.3% 帯で、 Standard (22〜44%) で推定した ITT 序列が効くかを見る。
        ///
        /// <para><b>なぜ要るか。</b> アイテムの価値は条件依存であることが実測で分かっている
        /// (火花: メタOff −2.39pt / Standard +3.35pt と符号が反転)。 ITT 表は Standard 一点で
        /// 推定しているので、 <b>製品が届く床のプレイヤーに効く保証が無い</b>。
        /// 「+20pt は Standard での話でした」で終わらせないための検証。</para>
        ///
        /// <para><b>床 (メタOff) ではなくパネル 0pt を使う理由。</b> メタOff は学習キーが
        /// <c>buffOff_debuffOff</c> へ切り替わり、 <c>item_stats.json</c> が無いので
        /// <c>Reload</c> が ITT 読み込みの手前で手書きフォールバックへ抜ける。
        /// パネル 0pt なら学習ディレクトリは同じまま難易度だけ 22.4% → 1.3% に落とせる。</para></summary>
        [MenuItem("Tools/AutoRun/ITT 転移確認 (配点0pt・学習あり・3000ラン)", priority = 11)]
        public static void RunIttTransfer()
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(IttKey, true);
            EditorPrefs.SetBool(SweepAxisKey, false);
            MetaMode = AutoRunner.MetaBuffMode.BuildFocused;
            MetaAxis = MetaAllocationPresets.Preset.None;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(3000);
        }

        // ===== 高難易度帯の ITT (2026-09-10) =====
        // 0pt で強い品と高難易度で要る品は違う ── `bot_band_*` を分けた理由と同じ。
        // 付与ログも学習ディレクトリも帯別なので、 0pt の 60,000ラン とは混ざらない。

        /// <summary>挑戦 <paramref name="score"/>pt の通常バッチ。 <paramref name="itt"/> で序列を切り替える。
        /// 対照と処置を同じ関数から立てて、 条件が 1 箇所でしか決まらないようにする。</summary>
        private static void LaunchAtChallenge(int score, bool itt, int runs = 3000, bool theoRelic = false)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            // **遺物は両方を明示する。** `forceNoRelic` が `forceTheoreticalRelic` より
            //   優先される実装 (AutoRunner) なので、 片方だけ立てると意図と逆になる。
            EditorPrefs.SetBool(NoRelicKey, !theoRelic);
            EditorPrefs.SetBool(TheoRelicKey, theoRelic);
            EditorPrefs.SetBool(SweepAxisKey, false);
            EditorPrefs.SetBool(IttKey, itt);
            EditorPrefs.SetInt(ChalTargetKey, score);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(runs);
        }

        [MenuItem("Tools/AutoRun/高難易度 探り: 挑戦50pt 対照 (観測regβ・3000ラン)", priority = 11)]
        public static void RunChal50Control() => LaunchAtChallenge(50, itt: false);

        [MenuItem("Tools/AutoRun/高難易度 探り: 挑戦50pt ITT (帯の表・3000ラン)", priority = 11)]
        public static void RunChal50Itt() => LaunchAtChallenge(50, itt: true);

        /// <summary><b>帯別学習の決着用 (2026-09-10)。</b> n=3000 では二値クリアの MDE が 0.86pt で、
        /// 帯分布で見ると +0.129 段 (p=0.088) の上積みが**見えているのに有意にならない**。
        /// 4 倍にすると SE が半分になり、 本物なら z=3.4 で決着する。
        /// 挑戦50pt はランが短いので 1 アーム 約12分。
        /// <b>比較は二値クリアではなく平均到達段で行うこと</b> ── クリアは 3000ラン で 43 件しかなく、
        /// 情報の大半を捨てている。</summary>
        [MenuItem("Tools/AutoRun/高難易度 決着: 挑戦50pt ITT (帯の表・12000ラン)", priority = 11)]
        public static void RunChal50Itt12k() => LaunchAtChallenge(50, itt: true, runs: 12000);

        // --- 理論値遺物つき (2026-09-10) ---
        // 難易度 max で遺物ゼロは製品の状況として現実的でない、 という判断。
        // 遺物なしは §13-5 のバランス基準として別途残す。

        [MenuItem("Tools/AutoRun/高難易度 探り: 挑戦50pt+理論値遺物 対照 (観測regβ・3000ラン)", priority = 11)]
        public static void RunChal50RelicControl() => LaunchAtChallenge(50, itt: false, theoRelic: true);

        [MenuItem("Tools/AutoRun/高難易度 探り: 挑戦50pt+理論値遺物 ITT (帯の表・3000ラン)", priority = 11)]
        public static void RunChal50RelicItt() => LaunchAtChallenge(50, itt: true, theoRelic: true);

        [MenuItem("Tools/AutoRun/高難易度 決着: 挑戦50pt+理論値遺物 ITT (帯の表・12000ラン)", priority = 11)]
        public static void RunChal50RelicItt12k() => LaunchAtChallenge(50, itt: true, runs: 12000, theoRelic: true);

        [MenuItem("Tools/AutoRun/高難易度 決着: 挑戦50pt+理論値遺物 対照 (観測regβ・12000ラン)", priority = 11)]
        public static void RunChal50RelicControl12k() => LaunchAtChallenge(50, itt: false, runs: 12000, theoRelic: true);

        /// <summary><b>高難易度帯の ITT 本走 (理論値遺物つき)。</b> 2026-09-10。
        /// 挑戦50pt を遺物込みで測るなら、 <b>訓練も遺物込みでなければ条件が食い違う</b>。
        /// 出力先は遺物なし版と同じ <c>bot_band_31up</c> なので、 走らせる前に
        /// 既存の <c>grant_runs_band_31up.csv</c> と <c>itt_clear.json</c> を退避すること
        /// (追記式なので混ざる)。</summary>
        [MenuItem("Tools/AutoRun/■ ITT 本走: 高難易度帯+理論値遺物 (挑戦50pt・60000ラン)", priority = 8)]
        public static void RunIttTrainingHardRelic()
        {
            ClearBatchModeSessionState();
            SessionState.SetBool(GrantTrialKey, true);
            SessionState.SetInt(GrantTrialRunsKey, 60000);
            EditorPrefs.SetBool(NoRelicKey, false);
            EditorPrefs.SetBool(TheoRelicKey, true);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(IttKey, false);
            EditorPrefs.SetBool(LockFamKey, false);
            EditorPrefs.SetInt(ChalTargetKey, 50);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        /// <summary><b>整備パネル drop-one アブレーション (2026-09-10)。</b>
        /// Balanced 36pt から 1 軸ずつ抜き、 どの軸がクリア率を支配しているかを測る。
        ///
        /// <para><b>動機。</b> パネルはオッズ比 14〜21 と全強化軸で最大なのに
        /// (遺物 29pt でさえ 5.4)、 個々のボーナスは +30%与ダメ / +18HP / +15%会心 と中庸。
        /// <b>数字と効果が釣り合っていない</b>ので内訳を出す。</para>
        ///
        /// <para>11 アーム (満額 + 9 軸抜き + 0pt) × 3000 ラン = 33,000 ラン。 約35分。
        /// 序列は ITT 固定 ── 対照と処置で BOT の質が変わらないようにする。</para></summary>
        /// <summary><b>極点 (r10) 同士の比較 (2026-09-10)。</b> 数値系 8 トラックを 1 本ずつ
        /// 振り切って、 「特化は報われるか」を測る。 drop-one は全アームが r6 以下だったので
        /// 極点に一度も触れていない。 「MAX を 9 段階へ」の可否はこの結果次第。
        /// 9 アーム × 3000 ラン = 27,000 ラン (約30分)。</summary>
        [MenuItem("Tools/AutoRun/■ 極点比較: r10 を1本ずつ振り切る (9アーム×3000ラン)", priority = 8)]
        public static void RunKeystoneSweepMenu() => LaunchKeystoneSweep(robberyBlocksShops: false);

        /// <summary><b>極点比較の高解像度版 (2026-09-12)。</b> 1 アーム 5000ラン。
        /// 差の 95%CI が ±2.5pt → <b>±1.9pt</b> へ狭まる。
        ///
        /// <para>今日の 4 変更 (強盗の方策・リロール曲線・オーバーロード反動 5%・
        /// ラストスタンドの最大HP半減 復活) が入った後の、 **現行の全体像を取り直す**ためのもの。
        /// 45,000ラン ≒ 3,600 秒 (約 60 分)。</para></summary>
        [MenuItem("Tools/AutoRun/■ 極点比較 高解像度 (9アーム×5000ラン ≒3600秒)", priority = 8)]
        public static void RunKeystoneSweepHiResMenu() => LaunchKeystoneSweep(robberyBlocksShops: false, runs: 5000);

        /// <summary><b>強盗の切り分け用 (2026-09-12)。</b> 上と同じ 9 アームを、 強盗の罰だけ
        /// 旧仕様「出禁」に戻して走らせる。 BOT の方策修正は両方に入っているので、
        /// <b>2 本の Trade r10 の差 = 罰を出禁から価格割増へ替えた効果</b>。
        /// 方策修正そのものの効果は、 こちらの Trade r10 を 2026-09-11 の −15.33pt と比べる。</summary>
        [MenuItem("Tools/AutoRun/■ 極点比較: 強盗=旧出禁 (切り分け用・9アーム×3000ラン)", priority = 8)]
        public static void RunKeystoneSweepOldRobberyMenu() => LaunchKeystoneSweep(robberyBlocksShops: true);

        /// <summary><b>商才 r10 を単発で走らせる (2026-09-12)。</b> 9 アーム のスイープは
        /// 1 トラックを見たいだけのときには 9 倍の無駄 ── 実際、 強盗の切り分けでは
        /// <b>8 アームが小数点まで同一</b>だった。
        ///
        /// <para>配分は極点比較の Trade アームを手で再構成したもの
        /// ([焦点 r10 = 10pt] + [共通順序で残り 23pt] = AttackTerm1(3) BlockTerm1(3)
        /// Shell6 Guard5 Vault4 Output2)。 条件も同スイープと揃えてあるので、
        /// 直近の <b>39.17%</b> と直接比べられる。 3,000ラン ≒ 230 秒。</para></summary>
        [MenuItem("Tools/AutoRun/商才 r10 単発 (3000ラン ≒230秒)", priority = 8)]
        public static void RunTradeKeystoneSolo()
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetString(RankSpecKey,
                "Trade:10,AttackTerm:1,BlockTerm:1,Shell:6,Guard:5,Vault:4,Output:2");
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);      // 極点比較と同条件
            EditorPrefs.SetBool(IttKey, true);          // BOT の質を固定
            EditorPrefs.SetBool(LockFamKey, false);
            EditorPrefs.SetBool(SweepAxisKey, false);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(3000);
        }

        /// <summary><b>r9 vs r10 の切り分け (2026-09-12)。</b> 既定は 外殻 と 燈火 ──
        /// 前者は「+8.19pt はラストスタンドの取り分か、 外殻が強いだけか」、
        /// 後者は「−5.37pt は横移動5→2 が悪いのか、 燈火に 10pt 使うのが悪いのか」。
        /// どちらも**次に何をリワークするか**が変わるので測る価値がある。
        /// 4 アーム × 3000ラン = 12,000ラン ≒ 950 秒。</summary>
        /// <summary><b>全軸 r9+r10+Balanced (2026-09-12)。</b> 17 アーム × 5000ラン = 85,000ラン ≒ 113 分。
        /// 判定基準 (r9 = Bal±2 / r10 = r9 超 かつ Bal±3) をレポートが直接出す。
        /// 埋め草は兵站5を最優先で確保するので、 全アームが Balanced と同じ開幕パッシブ 2 本。</summary>
        /// <summary><b>予算 24pt・均一土台での全軸 r10 比較 (2026-09-12)。</b>
        /// Balanced (数値系 8本 × r3) + 8 軸の r10 = 9 アーム × 10,000ラン = 90,000ラン ≒ 119 分。
        /// 差の 95%CI が ±1.36pt まで狭まり、 ±2pt の基準が初めて解像できる。
        /// r9 は「配点が無料で組み替えられる以上、 誰も選ばない配分」なので外した。</summary>
        // メニューパスに "/" を入れるとサブメニュー扱いになり実行できないので使わないこと。
        [MenuItem("Tools/AutoRun/■ 全軸 r10 + Balanced 24pt (9アーム×10000ラン ≒119分)", priority = 8)]
        public static void RunAllAxisR10Menu() => LaunchAllAxis("10", 10000);

        /// <summary><b>r9 だけ撮る (2026-09-12)。</b> r10 は同条件・同シードで撮ってあるので撮り直さない。
        /// r9 = 焦点9 + 他7本×2 = 23pt で、 <b>余り 1pt は使わない</b> ──
        /// r10 との違いを焦点トラックの 1 段だけに閉じるため。
        /// Balanced も同梱して再現性を検査する (25.31% に一致するはず)。
        /// 9 アーム × 10,000ラン = 90,000ラン ≒ 119 分。</summary>
        [MenuItem("Tools/AutoRun/■ 全軸 r9 + Balanced 24pt (9アーム×10000ラン ≒119分)", priority = 8)]
        public static void RunAllAxisR9Menu() => LaunchAllAxis("9", 10000);

        [MenuItem("Tools/AutoRun/■ 全軸 r9+r10+Balanced (17アーム×5000ラン ≒113分)", priority = 8)]
        public static void RunAllAxisR9R10Menu() => LaunchAllAxis("9,10", 5000);

        /// <summary><b>スループット計測 (2026-09-13)。</b> runsPerYield を 1/5/25/100 で振って
        /// ラン/秒 を測る。 4 条件 × 600ラン = 2,400ラン ≒ 3 分。
        /// スイープが遅いのがフレーム待ちか計算かを切り分ける。</summary>
        [MenuItem("Tools/AutoRun/計測: runsPerYield とスループット (4条件×600ラン ≒3分)", priority = 8)]
        public static void RunYieldBenchmarkMenu()
        {
            ClearBatchModeSessionState();
            SessionState.SetBool(YieldBenchKey, true);
            SessionState.SetInt(YieldBenchKey + ".Runs", 600);
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(IttKey, true);
            EditorPrefs.SetBool(LockFamKey, false);
            EditorPrefs.SetBool(SweepAxisKey, false);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        private static void LaunchAllAxis(string ranks, int runs)
        {
            ClearBatchModeSessionState();
            SessionState.SetBool(RankMarginKey, true);
            SessionState.SetInt(RankMarginKey + ".Runs", runs);
            SessionState.SetString(RankMarginKey + ".Tracks", "*");
            SessionState.SetString(RankMarginKey + ".Ranks", ranks);
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(IttKey, true);
            EditorPrefs.SetBool(LockFamKey, false);
            EditorPrefs.SetBool(SweepAxisKey, false);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/r9 vs r10 切り分け: 外殻・燈火 (4アーム×3000ラン ≒950秒)", priority = 8)]
        public static void RunRankMarginSweepMenu()
        {
            ClearBatchModeSessionState();
            SessionState.SetBool(RankMarginKey, true);
            SessionState.SetInt(RankMarginKey + ".Runs", 3000);
            SessionState.SetString(RankMarginKey + ".Tracks", "Shell,Lantern");
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(IttKey, true);
            EditorPrefs.SetBool(LockFamKey, false);
            EditorPrefs.SetBool(SweepAxisKey, false);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        /// <summary><b>オーバーロード再測定 (2026-09-12)。</b> 反動 15% → 5% にした後の 出力 r10 単発。
        /// 直近の 39.37% (反動15%・旧リロール曲線) と比べる。 3,000ラン ≒ 230 秒。
        /// 配分は極点比較の Output アームを手で再構成
        /// (Output10 + AttackTerm1(3) BlockTerm1(3) Shell6 Guard5 Vault4 Supply2)。</summary>
        [MenuItem("Tools/AutoRun/出力 r10 単発: オーバーロード反動5% (3000ラン ≒230秒)", priority = 8)]
        public static void RunOutputKeystoneSolo()
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetString(RankSpecKey,
                "Output:10,AttackTerm:1,BlockTerm:1,Shell:6,Guard:5,Vault:4,Supply:2");
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(IttKey, true);
            EditorPrefs.SetBool(LockFamKey, false);
            EditorPrefs.SetBool(SweepAxisKey, false);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(3000);
        }

        /// <summary><b>全 8 トラックの r9 を 5000ラン で撮る (2026-09-12)。</b>
        /// r10 側は同日の極点比較 (5000ラン) が<b>同じシード列 60000+i・同じ配分構築</b>で
        /// 既に撮ってあるので、 撮り直さない (80,000 → 40,000ラン)。
        /// 差 = 極点 1 個ぶんの限界価値。 40,000ラン ≒ 3,200 秒。</summary>
        [MenuItem("Tools/AutoRun/■ r9 全トラック (8アーム×5000ラン ≒3200秒)", priority = 8)]
        public static void RunRankMarginR9AllMenu()
        {
            ClearBatchModeSessionState();
            SessionState.SetBool(RankMarginKey, true);
            SessionState.SetInt(RankMarginKey + ".Runs", 5000);
            SessionState.SetString(RankMarginKey + ".Tracks", "*");   // 10 段トラック全部
            SessionState.SetString(RankMarginKey + ".Ranks", "9");    // r10 は既存データを使う
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(IttKey, true);
            EditorPrefs.SetBool(LockFamKey, false);
            EditorPrefs.SetBool(SweepAxisKey, false);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        /// <summary><b>兵站の原因究明 (2026-09-12)。</b> 開幕パッシブ 3 本 (兵站r9) と 0 本 (商才r9) を
        /// <b>埋め草を完全に同一にして</b>単発で回し、 サマリーの「パッシブ取得の経路別」を突き合わせる。
        ///
        /// <para>仮説: 所持パッシブが <c>InventoryPower</c> を押し上げ、 帯が上がると
        /// <c>CurrentDynamicMinPower</c> の足切りが 0.03 → 0.10 へ締まる (対象 127 品 → 89 品)。
        /// 配られたぶん BOT が買うのをやめる<b>補償ループ</b>。 ショップ品数が 3 本ぶん減っていれば実在。</para>
        ///
        /// <para>両者とも Shell6 Guard5 Vault4 Output3 AT1 BT1 で焦点だけが違う (各 33pt)。
        /// アーム別に見たいので 2 バッチに分ける (スイープだと集約になる)。 各 3,000ラン ≒ 230 秒。</para></summary>
        [MenuItem("Tools/AutoRun/兵站診断A: 兵站r9 開幕P3本 (3000ラン)", priority = 8)]
        public static void RunSupplyR9Solo()
            => LaunchSpecSolo("Supply:9,AttackTerm:1,BlockTerm:1,Shell:6,Guard:5,Vault:4,Output:3");

        [MenuItem("Tools/AutoRun/兵站診断B: 商才r9 開幕P0本 (3000ラン)", priority = 8)]
        public static void RunTradeR9Solo()
            => LaunchSpecSolo("Trade:9,AttackTerm:1,BlockTerm:1,Shell:6,Guard:5,Vault:4,Output:3");

        /// <summary><b>ペア比較の利得測定 (2026-09-13)。</b> Balanced と 商才r10 を
        /// <b>同じシード列</b>で 2,000ラン ずつ回し、 run.jsonl をシード番号で突き合わせる。
        ///
        /// <para>全アームが同じシードを走っているのに、 スイープは集計値どうしを比べている。
        /// 「このシードは当たり/外れ」という両アーム共通のばらつきが、 そのまま誤差に乗っている。
        /// ペアで見ればそれが相殺され、 <b>片方だけクリアしたシード</b>だけが効く。
        /// 節約幅は食い違い率で決まるので<b>推測せず測る</b> ── 出すのは係数ひとつ:
        /// <c>必要ラン数の比 = 食い違い率 ÷ (pA(1−pA) + pB(1−pB))</c>。</para>
        ///
        /// <para>対照に 商才r10 (+3.90pt) を選ぶ理由: <b>境界付近の軸</b>で、 まさに精度が要る帯。
        /// 外殻 (+12.70pt) で測ると相関が良く見えすぎて節約幅を過大評価する。</para>
        ///
        /// <para>集計は <c>Tools/pair_gain.py A/runs.jsonl B/runs.jsonl</c>。</para></summary>
        [MenuItem("Tools/AutoRun/ペア測定A: Balanced 2000ラン (jsonl)", priority = 8)]
        public static void RunPairBalancedSolo()
            => LaunchSpecSolo("Shell:3,Output:3,Guard:3,Vault:3,Plunder:3,Trade:3,Supply:3,Lantern:3", 2000);

        /// <summary><b>決定論の検査 (2026-09-13)。</b> 同じ配分を n=1000 と n=2000 で走らせ、
        /// <c>deterministicDigest</c> を index 0〜999 で突き合わせる。
        ///
        /// <para><b>並列化の前提を確かめるため。</b> ラン i の結果が runIdx だけの関数なら
        /// どのプロセスが走らせても同じで、 シード分割は安全。 しかし 2026-08-10 に
        /// <c>CombatManager._combatSeq</c> が<b>ラン跨ぎで累積</b>していた前例がある
        /// (ラン i がラン 1〜i−1 に依存していた)。 同種の static が他に無い保証はない。</para>
        ///
        /// <para>バッチ長が変われば後続ランの履歴が変わるので、 累積状態があれば
        /// <b>必ず digest がずれる</b> ── プロセス分割と同じ性質のテストになる。</para></summary>
        [MenuItem("Tools/AutoRun/決定論検査: Balanced 1000ラン (jsonl)", priority = 8)]
        public static void RunDeterminismCheck1000()
            => LaunchSpecSolo("Shell:3,Output:3,Guard:3,Vault:3,Plunder:3,Trade:3,Supply:3,Lantern:3", 1000);

        [MenuItem("Tools/AutoRun/ペア測定B: 商才r10 2000ラン (jsonl)", priority = 8)]
        public static void RunPairTradeSolo()
            => LaunchSpecSolo("Trade:10,Shell:2,Output:2,Guard:2,Vault:2,Plunder:2,Supply:2,Lantern:2", 2000);

        /// <summary>任意のパネル配分を単発で回す。 条件は極点比較と揃える。</summary>
        private static void LaunchSpecSolo(string spec, int runs = 3000)
        {
            ClearBatchModeSessionState();
            EditorPrefs.SetString(RankSpecKey, spec);
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(IttKey, true);
            EditorPrefs.SetBool(LockFamKey, false);
            EditorPrefs.SetBool(SweepAxisKey, false);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(runs);
        }

        private static void LaunchKeystoneSweep(bool robberyBlocksShops, int runs = 3000)
        {
            ClearBatchModeSessionState();
            SessionState.SetBool(KeystoneKey, true);
            SessionState.SetInt(KeystoneKey + ".Runs", runs);
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(IttKey, true);          // BOT の質を固定
            EditorPrefs.SetBool(LockFamKey, false);
            EditorPrefs.SetBool(SweepAxisKey, false);
            EditorPrefs.SetBool(RobBlockKey, robberyBlocksShops);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/■ パネル内訳: drop-one アブレーション (11アーム×3000ラン)", priority = 8)]
        public static void RunMetaAxisAblation()
        {
            ClearBatchModeSessionState();
            SessionState.SetBool(MetaAblKey, true);
            SessionState.SetInt(MetaAblKey + ".Runs", 3000);
            EditorPrefs.SetBool(LearnTierKey, false);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(NoRelicKey, true);      // 基準条件
            EditorPrefs.SetBool(IttKey, true);          // BOT の質を固定
            EditorPrefs.SetBool(LockFamKey, false);
            EditorPrefs.SetBool(SweepAxisKey, false);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/高難易度 探り: 挑戦30pt 対照 (観測regβ・3000ラン)", priority = 11)]
        public static void RunChal30Control() => LaunchAtChallenge(30, itt: false);

        [MenuItem("Tools/AutoRun/高難易度 探り: 挑戦30pt ITT (帯の表・3000ラン)", priority = 11)]
        public static void RunChal30Itt() => LaunchAtChallenge(30, itt: true);

        /// <summary><b>高難易度帯の ITT 本走。</b> 付与ログは <c>grant_runs_band_*.csv</c>、
        /// 係数は <c>bot_band_*/itt_clear.json</c> へ落ちるので 0pt 帯と混ざらない。
        ///
        /// <para><b>2026-09-10 の実測: 挑戦50pt では上積みゼロだった。</b>
        /// 0pt 帯の表 1.53% に対し 帯別表 1.43% (差 −0.10pt / 95%CI [−0.71, +0.51])。
        /// 2 つの表の順位相関は +0.823・上位10 の重なり 8品 ＝ <b>良い品同士の並べ替え</b>で、
        /// 1 ラン に 89 品中 15 品 取得する以上<b>バスケットが変わらない</b>。
        /// 序列の差が方策に効くのは「良い品と悪い品が入れ替わる」ときだけ
        /// (観測regβ→ITT は順位相関 +0.066 で +20pt 動いた)。
        /// <b>他の帯を作る前に、 表の相関ではなく購入バスケットが変わるかを見ること。</b></para>
        ///
        /// <para><b>探りを先に通すこと。</b> その帯のクリア率が 1% を大きく割ると
        /// band=11 の事象が薄くなる。 離散時間生存モデルは段ごとのハザードを使うので
        /// 二値より遥かに粘るが、 それでも下限はある。</para></summary>
        [MenuItem("Tools/AutoRun/■ ITT 本走: 高難易度帯 (挑戦50pt・60000ラン)", priority = 8)]
        public static void RunIttTrainingHard()
        {
            ClearBatchModeSessionState();
            SessionState.SetBool(GrantTrialKey, true);
            SessionState.SetInt(GrantTrialRunsKey, 60000);
            EditorPrefs.SetBool(NoRelicKey, true);
            EditorPrefs.SetBool(TuneBossKey, false);
            EditorPrefs.SetBool(LearnAiKey, false);
            EditorPrefs.SetBool(IttKey, false);       // 採取中は旧優先度のままで良い
            EditorPrefs.SetBool(LockFamKey, false);
            EditorPrefs.SetInt(ChalTargetKey, 50);
            MetaMode = AutoRunner.MetaBuffMode.Standard;
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        /// <summary><b>アイテム アブレーション: 実現可能性の測定 (2026-09-08)。</b>
        ///
        /// <para>観測データからアイテムの因果効果は取り出せない ── 所持は結果の下流にあり、
        /// 実測で素の差 +4.35 band に対し不偏 lift は +0.14、 <b>97% が交絡</b>だった。
        /// 一方でアイテム間の真のばらつきは SD <b>0.15 band</b> しかない。
        /// 回帰は交絡の 95% を落とすが、 残差がその 0.15 より大きいので勝負にならない。</para>
        ///
        /// <para>シミュレーションだけが<b>同一シードで介入できる</b>。 これを全 163 品でやるかは
        /// <b>対の差の SD</b> 次第 ── 必要な対の数が (SD/0.05)² で効くので、
        /// SD 2.83 なら 52 万ラン、 SD 1.0 なら 6.5 万ラン。 まず 3 品で測る。</para>
        ///
        /// <para>先頭 2 アームは <b>A/A (両方とも付与なし)</b>。 ここで差が出たら決定性が壊れており、
        /// ペアリングが前提から崩れる。 対象品は推定量が割れているものを選んである:
        /// 共鳴 (LEG / lift +0.933 vs regβ +0.087 ＝ 最大の食い違い) /
        /// 発火 (BRONZE / lift +0.284 vs regβ +0.253 ＝ 両者一致) /
        /// 無心の刃 (GOLD / regβ −0.168 ＝ regβ が最下位に置いた品)。</para></summary>
        [MenuItem("Tools/AutoRun/アイテム アブレーション (A/A + 3品 × 各500対)", priority = 12)]
        public static void RunItemAblation()
        {
            SessionState.SetBool(ItemAblKey, true);
            SessionState.SetString(ItemAblIdsKey, "共鳴,発火,無心の刃");
            SessionState.SetInt(ItemAblRunsKey, 500);
            EditorPrefs.SetBool(NoRelicKey, true);   // 基準条件 (遺物なし) で測る
            // **配線技量を Optimal に固定する。** 前の作業で Super/Ultra のままだと
            //   1 ラン 5〜10 秒 ＝ 2,500 ラン で数時間かかり、 エディタが無応答に見える。
            //   学習バッチと同じ条件でもある。
            Skill = AutoRunner.WiringSkill.Optimal;
            Launch(1);
        }

        /// <summary>全 16 軸を「その軸 1 本だけ・段9」の遺物で個別に回す。
        /// プリセットスイープが「遺物の総量」を測るのに対し、 こちらは **軸間の強弱**を測る。
        /// 単位 (期待与ダメ 6% = 攻撃+1) に揃っているかの検算用。</summary>
        [MenuItem("Tools/AutoRun/遺物 単軸スイープ (全16軸 × 各300ラン)", priority = 12)]
        public static void RunRelicAxisSweep()
        {
            SessionState.SetBool(AxisSweepKey, true);
            Launch(1);
        }

        /// <summary>バランス基準値の測定。 **遺物なし・挑戦0pt が正式な調整基準** (2026-08-04)。
        /// 遺物は周回引継ぎなので初回プレイヤーは 0 個。 目標は 7 層クリア 2 割強。</summary>
        [MenuItem("Tools/AutoRun/基準値測定 (遺物なし・0pt・500ラン)", priority = 14)]
        public static void RunBaselineMeasure()
        {
            SessionState.SetBool(RelicSweepKey, true);
            SessionState.SetBool(RelicBaseKey, true);
            Launch(1);
        }

        /// <summary>会心倍率が有害な原因を追う。 [基準]攻撃3 と 会心倍率@段9 の 2 アームだけを
        /// **jsonl 付き**で回し、 死因・死亡層・致死敵を突き合わせる。
        /// 通常の単軸スイープは jsonl を切っているので死因が採れない。</summary>
        [MenuItem("Tools/AutoRun/診断: 会心倍率 (2アーム × 500ラン・jsonl)", priority = 13)]
        public static void RunCritMultDiagnose()
        {
            SessionState.SetBool(AxisSweepKey, true);
            SessionState.SetBool(AxisDiagKey, true);
            Launch(1);
        }

        /// <summary>全 BuildPersona を同一条件・同一シードで回して素の勝率を出す。
        /// 周回モードと違って難易度ラチェットを入れないので「どのビルドが強いか」に直接答える。</summary>
        [MenuItem("Tools/AutoRun/ビルド別勝率スイープ (全10ビルド × 各500ラン)", priority = 14)]
        public static void RunPersonaSweep()
        {
            SessionState.SetBool(PersonaKey, true);
            Launch(1);
        }

        [MenuItem("Tools/AutoRun/Open log folder", priority = 20)]
        public static void OpenLogFolder()
        {
            string dir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "AutoRunLogs"));
            System.IO.Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        private static void Launch(int count, bool sweep = false, bool lambdaSweep = false, int loopBatches = 1)
        {
            if (EditorApplication.isPlaying)
            {
                // **モーダルを出さない** (2026-08-08)。 DisplayDialog は誰かがクリックするまで
                //   Editor を丸ごと止める。 MCP/CLI から叩く運用ではクリックする人が居ないので、
                //   実測で 5 時間 Editor が固まり、 スイープが 1 件も出力しなかった。
                //   ここは警告して抜けるだけにし、 呼び出し側が停止を判断する。
                Debug.LogWarning("[AutoRunMenu] 既に PlayMode 中です。 停止してから実行してください "
                               + "(前のバッチが終了処理中の可能性があります)。");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetInt(PendingKey, count);
            SessionState.SetBool(SweepKey, sweep);
            SessionState.SetBool(LambdaKey, lambdaSweep);
            SessionState.SetInt(LoopKey, loopBatches);
            string modeLabel = sweep ? "5Fボス勝率スイープ"
                              : lambdaSweep ? "Λファーム量スイープ"
                              : loopBatches >= 2 ? $"自動周回 {count}ラン × {loopBatches}回"
                              : count + " ラン";
            Debug.Log($"[AutoRunMenu] {modeLabel}予約 → PlayMode 開始"
                    + $"  ①メタバフ={MetaMode}{(MetaMode == AutoRunner.MetaBuffMode.BuildFocused ? $"({MetaAxis})" : "")}"
                    + $"  ②アイテム={ItemMode}"
                    + $"  ③学習: ボス={EditorPrefs.GetBool(TuneBossKey, false)}"
                    + $"/Tier={EditorPrefs.GetBool(LearnTierKey, false)}"
                    + $"/BOT={EditorPrefs.GetBool(LearnAiKey, false)}"
                    );
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;

            int count = SessionState.GetInt(PendingKey, 0);
            if (count <= 0) return;
            bool sweep = SessionState.GetBool(SweepKey, false);
            bool lambdaSweep = SessionState.GetBool(LambdaKey, false);
            bool relicSweep = SessionState.GetBool(RelicSweepKey, false);
            SessionState.EraseBool(RelicSweepKey);
            bool relicBaseline = SessionState.GetBool(RelicBaseKey, false);
            SessionState.EraseBool(RelicBaseKey);
            bool chalSweep = SessionState.GetBool(ChalSweepKey, false);
            SessionState.EraseBool(ChalSweepKey);
            bool axisSweep = SessionState.GetBool(AxisSweepKey, false);
            SessionState.EraseBool(AxisSweepKey);
            bool chalAxisSweep = SessionState.GetBool(ChalAxisKey, false);
            SessionState.EraseBool(ChalAxisKey);
            bool chalAxisMaxOnly = SessionState.GetBool(ChalAxisMaxKey, false);
            SessionState.EraseBool(ChalAxisMaxKey);
            bool chalAxisSmoke = SessionState.GetBool(ChalAxisSmokeKey, false);
            SessionState.EraseBool(ChalAxisSmokeKey);
            int chalAxisOnly = SessionState.GetInt(ChalAxisOnlyKey, -1);
            SessionState.EraseInt(ChalAxisOnlyKey);
            int chalAxisDet = SessionState.GetInt(ChalAxisDetKey, 0);
            SessionState.EraseInt(ChalAxisDetKey);
            bool chalAxisDiff = SessionState.GetBool(ChalAxisDiffKey, false);
            SessionState.EraseBool(ChalAxisDiffKey);
            bool chalAxisPrice = SessionState.GetBool(ChalAxisPriceKey, false);
            SessionState.EraseBool(ChalAxisPriceKey);
            bool optimalParallelBench = SessionState.GetBool(OptimalParallelBenchKey, false);
            SessionState.EraseBool(OptimalParallelBenchKey);
            bool axisDiag = SessionState.GetBool(AxisDiagKey, false);
            SessionState.EraseBool(AxisDiagKey);
            bool personaSweep = SessionState.GetBool(PersonaKey, false);
            SessionState.EraseBool(PersonaKey);
            int tierCalScore = SessionState.GetInt(TierCalScoreKey, -1);
            int tierCalRuns = SessionState.GetInt(TierCalRunsKey, 0);
            int tierCalBatches = SessionState.GetInt(TierCalBatchesKey, 1);
            bool tierCalResume = SessionState.GetBool(TierCalResumeKey, false);
            bool ultraStandard50WithRelic10K = SessionState.GetBool(UltraStandard50WithRelic10KKey, false);
            int ultraSmokeSink = SessionState.GetInt(UltraSmokeSinkKey, 0);
            SessionState.EraseInt(UltraSmokeSinkKey);
            string ultraCapturePath = SessionState.GetString(UltraCaptureKey, "");
            SessionState.EraseString(UltraCaptureKey);
            string ultraResumePath = SessionState.GetString(UltraResumeKey, "");
            SessionState.EraseString(UltraResumeKey);
            bool ultraCensus = SessionState.GetBool(UltraCensusKey, false);
            SessionState.EraseBool(UltraCensusKey);
            int ultraRolloutRuns = SessionState.GetInt(UltraRolloutRunsKey, 0);
            SessionState.EraseInt(UltraRolloutRunsKey);
            int ultraRolloutPerAction = SessionState.GetInt(UltraRolloutPerActionKey, 0);
            SessionState.EraseInt(UltraRolloutPerActionKey);
            int floorDpPct = SessionState.GetInt(FloorDpKey, 0);
            SessionState.EraseInt(FloorDpKey);
            int floorDpBands = SessionState.GetInt(FloorDpBandsKey, 0);
            SessionState.EraseInt(FloorDpBandsKey);
            int tieExpand = SessionState.GetInt(TieExpandKey, 0);
            SessionState.EraseInt(TieExpandKey);
            bool satCensus = SessionState.GetBool(SatCensusKey, false);
            SessionState.EraseBool(SatCensusKey);
            int rerollGainPct = SessionState.GetInt(RerollGainKey, 0);
            SessionState.EraseInt(RerollGainKey);
            bool vescaKeepRolesOff = SessionState.GetBool(VescaKeepRolesKey, false);
            SessionState.EraseBool(VescaKeepRolesKey);
            int exactReroll = SessionState.GetInt(ExactRerollKey, 0);
            SessionState.EraseInt(ExactRerollKey);
            int winHpPct = SessionState.GetInt(WinHpWeightKey, 0);
            SessionState.EraseInt(WinHpWeightKey);
            int ttkX10 = SessionState.GetInt(TtkWeightKey, 0);
            SessionState.EraseInt(TtkWeightKey);
            int ablateAxis = SessionState.GetInt(AblateKey, 0);
            SessionState.EraseInt(AblateKey);
            int exploreK = SessionState.GetInt(ExploreBlendKey, 0);
            SessionState.EraseInt(ExploreBlendKey);
            bool layer7OptimalOn = SessionState.GetBool(Layer7OptimalKey, false);
            SessionState.EraseBool(Layer7OptimalKey);
            int ttdCap = SessionState.GetInt(TtdCapKey, 0);
            SessionState.EraseInt(TtdCapKey);
            SessionState.EraseInt(TierCalScoreKey);
            SessionState.EraseInt(TierCalRunsKey);
            SessionState.EraseInt(TierCalBatchesKey);
            SessionState.EraseBool(TierCalResumeKey);
            SessionState.EraseBool(UltraStandard50WithRelic10KKey);
            int loopBatches = SessionState.GetInt(LoopKey, 1);
            SessionState.EraseInt(PendingKey);
            SessionState.EraseBool(SweepKey);
            SessionState.EraseBool(LambdaKey);
            SessionState.EraseInt(LoopKey);

            var go = new GameObject("[AutoRunner]");
            var runner = go.AddComponent<AutoRunner>();
            runner.runCount = count;
            // 長大バッチは同期ゲームロジックをまとめて進める。乱数・判断結果は不変。
            // 25runごとには描画へ戻し、Editor/MCPが完全に無応答になるのを避ける。
            if (count >= 3000 && loopBatches <= 1 && !sweep && !lambdaSweep)
            {
                runner.stepsPerYield = 1000;
                runner.runsPerYield = 25;
            }
            // **大バッチは周回でも runs.jsonl を書かない (2026-09-04)。**
            //   1000 ラン で 12.1MB あるので、 25 万ラン 級の蓄積では 3GB に達する。
            //   学習の取り込み (IngestBatch) はメモリ上の _records を使うのでファイルは要らず、
            //   判定に必要な数字は summary と item_stats に入る。
            if (count >= 3000 && !sweep && !lambdaSweep)
                runner.writeRunsJsonl = false;
            // 決定論シード: EditorPrefs から。 空ならランダム動作 (従来どおり)。
            runner.masterSeed = EditorPrefs.GetString("AutoRun.MasterSeed", "");
            // **確認用のシード上書き。** 同じシードで何本もアームを回すと、
            //   多重比較でどれかが偶然 p<0.05 に入る (8 本なら約 34%)。
            //   再現を別のシードで取るための、 1 回で消費するキー。
            {
                string seedOverride = SessionState.GetString(SeedOverrideKey, "");
                SessionState.EraseString(SeedOverrideKey);
                if (!string.IsNullOrEmpty(seedOverride))
                {
                    runner.masterSeed = seedOverride;
                    Debug.LogWarning("[AutoRunMenu] 決定seed を上書き: " + seedOverride
                        + " ── **別シードの測定は同シードの測定と混ぜないこと**");
                }
            }
            runner.challengeSpec = EditorPrefs.GetString("AutoRun.ChallengeSpec", "");
            runner.autoStart = false;
            runner.exitPlayModeWhenDone = true;
            // プロファイルから metaPattern / enableAllDebuffs は Begin() 内で自動上書きされる
            // ① メタバフ (排他) / ② アイテム選択 (排他) / ③ 学習 (同時選択可)
            runner.metaBuffMode  = MetaMode;
            runner.metaBuildAxis = MetaAxis;
            runner.sweepAllMetaAxes = EditorPrefs.GetBool(SweepAxisKey, false);
            runner.enableAllDebuffs = EditorPrefs.GetBool(DebuffKey, false);
            runner.itemPickMode  = ItemMode;
            runner.tuneBosses = EditorPrefs.GetBool(TuneBossKey, false);
            runner.learnTier  = EditorPrefs.GetBool(LearnTierKey, false);
            runner.learnBotAi = EditorPrefs.GetBool(LearnAiKey, false);
            runner.wiringSkill = Skill;
            // ⑤ 対照群: EditorPrefs が立っているときだけ「肩代わりしない」で走らせる
            runner.shieldAbsorbsUnmitigable = !EditorPrefs.GetBool(ShieldChipKey, false);
            // 遺物なし強制 (⑥)。 既定 OFF ＝ 従来どおり「触らない」。
            runner.forceNoRelic = EditorPrefs.GetBool(NoRelicKey, false);
            runner.forceTheoreticalRelic = EditorPrefs.GetBool(TheoRelicKey, false);
            runner.lockWeaponFamily = EditorPrefs.GetBool(LockFamKey, false);
            runner.useIttBeta = EditorPrefs.GetBool(IttKey, false);
            runner.challengeScoreTarget = EditorPrefs.GetInt(ChalTargetKey, 0);
            runner.metaRankSpec = EditorPrefs.GetString(RankSpecKey, "");
            runner.robberyBlocksShops = EditorPrefs.GetBool(RobBlockKey, false);
            runner.yieldBenchmark = SessionState.GetBool(YieldBenchKey, false);
            SessionState.EraseBool(YieldBenchKey);
            if (runner.yieldBenchmark)
            {
                runner.yieldBenchRuns = SessionState.GetInt(YieldBenchKey + ".Runs", 600);
                SessionState.EraseInt(YieldBenchKey + ".Runs");
                runner.writeRunsJsonl = false;
            }
            runner.rankMarginSweep = SessionState.GetBool(RankMarginKey, false);
            SessionState.EraseBool(RankMarginKey);
            if (runner.rankMarginSweep)
            {
                runner.metaAblationRuns = SessionState.GetInt(RankMarginKey + ".Runs", 3000);
                runner.rankMarginTracks = SessionState.GetString(RankMarginKey + ".Tracks", "Shell,Lantern");
                runner.rankMarginRanks = SessionState.GetString(RankMarginKey + ".Ranks", "9,10");
                SessionState.EraseInt(RankMarginKey + ".Runs");
                SessionState.EraseString(RankMarginKey + ".Tracks");
                SessionState.EraseString(RankMarginKey + ".Ranks");
                runner.writeRunsJsonl = false;
                runner.stepsPerYield = 1000; runner.runsPerYield = 1;
            }
            runner.keystoneSweep = SessionState.GetBool(KeystoneKey, false);
            SessionState.EraseBool(KeystoneKey);
            if (runner.keystoneSweep)
            {
                runner.metaAblationRuns = SessionState.GetInt(KeystoneKey + ".Runs", 3000);
                SessionState.EraseInt(KeystoneKey + ".Runs");
                runner.writeRunsJsonl = false;
                runner.stepsPerYield = 1000; runner.runsPerYield = 1;
            }
            runner.metaAxisAblation = SessionState.GetBool(MetaAblKey, false);
            SessionState.EraseBool(MetaAblKey);
            if (runner.metaAxisAblation)
            {
                runner.metaAblationRuns = SessionState.GetInt(MetaAblKey + ".Runs", 3000);
                SessionState.EraseInt(MetaAblKey + ".Runs");
                runner.writeRunsJsonl = false;
                runner.stepsPerYield = 1000; runner.runsPerYield = 1;
            }
            runner.suppressFacePartOffers = EditorPrefs.GetBool(NoPartsKey, false);
            if (tierCalScore != -1)
            {
                runner.tierCalibrationScores = tierCalScore == -2
                    ? new[] { 0, 30, 50 }
                    : new[] { tierCalScore };
                runner.tierCalibrationRuns = Mathf.Max(1, tierCalRuns);
                runner.tierCalibrationBatches = Mathf.Max(1, tierCalBatches);
                if (tierCalResume)
                    runner.tierCalibrationBatchCounts = new[] { 4, 10, 10 };
                runner.learnTier = true;
                runner.tuneBosses = false;
                runner.learnBotAi = false;
                runner.itemPickMode = AutoRunner.ItemPickMode.BuildFocused;
                runner.wiringSkill = AutoRunner.WiringSkill.Optimal;
                runner.metaBuffMode = AutoRunner.MetaBuffMode.Standard;
                runner.sweepAllMetaAxes = false;
                runner.enableAllDebuffs = false;
                runner.runCount = 0;
            }
            runner.simBoss5Sweep = sweep;
            runner.lambdaFarmSweep = lambdaSweep;
            runner.relicPresetSweep = relicSweep;
            runner.relicSweepBaselineOnly = relicBaseline;
            if (relicBaseline) runner.relicSweepRuns = 500;   // 1 プリセットだけなので厚めに取る
            runner.challengeFixedSweep = chalSweep;
            if (SessionState.GetBool(SkillCmpKey, false))
            {
                runner.wiringSkillCompare = true;
                runner.wiringSkillCompareRuns = SessionState.GetInt(SkillCmpRunsKey, 300);
                runner.wiringSkillCompareScore = SessionState.GetInt(SkillCmpScoreKey, 0);
                SessionState.EraseInt(SkillCmpScoreKey);
                if (SessionState.GetBool(SkillCmpBudgetKey, false))
                {
                    runner.wiringSkillCompareArms = new[] { AutoRunner.WiringSkill.Super, AutoRunner.WiringSkill.Super };
                    runner.wiringSkillCompareBudget = new[] { false, true };
                    SessionState.EraseBool(SkillCmpBudgetKey);
                }
                else runner.wiringSkillCompareArms = SessionState.GetBool(SkillCmp3Key, false)
                    ? new[] { AutoRunner.WiringSkill.Naive, AutoRunner.WiringSkill.Optimal, AutoRunner.WiringSkill.Super }
                    : new[] { AutoRunner.WiringSkill.Optimal, AutoRunner.WiringSkill.Super };
                runner.writeRunsJsonl = false;   // 600〜900 ラン。 判定は本文の McNemar 表で足りる
                // **1 ラン ごとに制御を返す。** 天井 AI は 1 ラン 数秒〜十数秒かかるので、
                // 25 ラン刻みだとエディタが 1〜2 分単位で無応答になり操作できなくなる。
                runner.stepsPerYield = 1000; runner.runsPerYield = 1;
                SessionState.EraseBool(SkillCmpKey);
                SessionState.EraseBool(SkillCmp3Key);
            }
            // 単点測定: 固定難易度スイープの器を 1 段だけで使う (遺物 TheoreticalBestCursed /
            // ペルソナ Standard / 5層裏ボス遮断 の条件はスイープと共通なので、 器を借りる方が
            // 条件の食い違いが起きない)。
            if (chalSweep && SessionState.GetInt(ChalOneScoreKey, -1) >= 0)
            {
                runner.challengeSweepScores = new[] { SessionState.GetInt(ChalOneScoreKey, 50) };
                runner.challengeSweepRuns = SessionState.GetInt(ChalOneRunsKey, 100);
                SessionState.EraseInt(ChalOneScoreKey);
                SessionState.EraseInt(ChalOneRunsKey);
            }
            // 複数点の上書き (例: "0,30,50")。 単点指定より後に置いて、 併用時はこちらを優先する。
            string sweepScores = SessionState.GetString(ChalSweepScoresKey, "");
            if (chalSweep && !string.IsNullOrEmpty(sweepScores))
            {
                var parts = sweepScores.Split(',');
                var scores = new System.Collections.Generic.List<int>();
                foreach (var p in parts)
                    if (int.TryParse(p.Trim(), out int v)) scores.Add(v);
                if (scores.Count > 0) runner.challengeSweepScores = scores.ToArray();
                int r = SessionState.GetInt(ChalSweepRunsKey, 0);
                if (r > 0) runner.challengeSweepRuns = r;
                SessionState.EraseString(ChalSweepScoresKey);
                SessionState.EraseInt(ChalSweepRunsKey);
            }
            runner.relicAxisSweep = axisSweep;
            // アイテム アブレーション。 **学習は切る** ── 開幕付与した品が
            //   「BOT が選んだ取得」として item_stats に入ると、 累積の学習データが汚れる。
            // **フラグ本体を必ず消す (2026-09-09)。** ここは SessionState なので Editor を
            //   落とすまで生き残る。 以前は Runs/Ids だけ消してブール値を残していたため、
            //   一度アブレーションや付与試行を回すと **以後すべてのバッチに 1,000＋5,000 ラン が
            //   黙って相乗り**し、 サマリの「総ラン数」が 3000 指定でも常に 9000 になっていた。
            //   しかもクリア率はその 3 アームの混合平均 ── 床/天井の測定値が全部汚れていた。
            runner.randomGrantTrial = SessionState.GetBool(GrantTrialKey, false);
            SessionState.EraseBool(GrantTrialKey);
            if (runner.randomGrantTrial)
            {
                runner.randomGrantRuns = SessionState.GetInt(GrantTrialRunsKey, 5000);
                runner.learnTier = false;       // 配った品を「買った」として積ませない
                runner.writeRunsJsonl = false;
                runner.stepsPerYield = 1000; runner.runsPerYield = 1;
                // 全種プール (2026-09-17)。 **期待付与数も一緒に運ぶ** ── プールが
                //   1.8 倍になるので、 据え置くと 1 品あたりの処置ラン数が半分になる。
                runner.randomGrantAllKinds = SessionState.GetBool(GrantAllKindsKey, false);
                int gi = SessionState.GetInt(GrantItemsKey, 0);
                if (gi > 0) runner.randomGrantExpectedItems = gi / 100f;
                SessionState.EraseInt(GrantTrialRunsKey);
                SessionState.EraseBool(GrantAllKindsKey);
                SessionState.EraseInt(GrantItemsKey);
            }
            runner.itemAblationSweep = SessionState.GetBool(ItemAblKey, false);
            SessionState.EraseBool(ItemAblKey);
            if (runner.itemAblationSweep)
            {
                runner.itemAblationIds  = SessionState.GetString(ItemAblIdsKey, "");
                runner.itemAblationRuns = SessionState.GetInt(ItemAblRunsKey, 500);
                runner.learnTier = false;
                runner.writeRunsJsonl = false;
                // **毎ラン制御を返す** (挑戦単軸スイープと同じ設定)。 20 ラン刻みだと
                //   数秒ごとにエディタが無応答になり、 「重い」と「止まった」が区別できない。
                runner.stepsPerYield = 1000; runner.runsPerYield = 1;
                SessionState.EraseString(ItemAblIdsKey);
                SessionState.EraseInt(ItemAblRunsKey);
            }
            runner.challengeAxisSweep = chalAxisSweep;
            runner.challengeAxisSweepMaxTierOnly = chalAxisMaxOnly;
            runner.challengeAxisSweepOnlyAxis = chalAxisOnly;
            runner.challengeAxisSweepDeterminismArms = chalAxisDet;
            runner.challengeAxisSweepParallelCompare = optimalParallelBench;
            // 23 アーム × 300 = 6900 ラン。 jsonl は肥大するので止める。
            if (chalAxisSweep)
            {
                // **毎ラン制御を返す** (2026-08-11)。 25 ラン刻みだと 1 ラン 0.13 秒 ×
                //   25 = 約 3.4 秒ごとにエディタが無応答になり、 「重い」と「止まった」が
                //   区別できない。 実際それで固着を 20 分見逃した。 毎ラン yield しても
                //   1 ラン 0.13 秒に対するオーバーヘッドは無視できる。
                runner.stepsPerYield = 1000; runner.runsPerYield = 1;
                runner.writeRunsJsonl = false;
                runner.challengeAxisSweepRuns = 1000;
                if (optimalParallelBench)
                {
                    runner.challengeAxisSweepRuns = 100;
                    runner.challengeAxisSweepProgressEvery = 20;
                }
                runner.challengeAxisSweepNoRelic = SessionState.GetBool(ChalAxisNoRelicKey, false);
                runner.challengeCategorySweep = SessionState.GetBool(ChalCatKey, false);
                runner.challengeCategorySweepT4Only = SessionState.GetBool(ChalCatT4Key, false);
                runner.challengeCategorySweepOnlyCat = SessionState.GetInt(ChalCatOnlyKey, -1);
                runner.challengeCategorySweepT4Alone = SessionState.GetBool(ChalCatAloneKey, false);
                runner.challengeAxisSweepIndexBase = SessionState.GetInt(ChalAxisSeedBaseKey, 60000);
                SessionState.EraseBool(ChalCatAloneKey);
                SessionState.EraseInt(ChalAxisSeedBaseKey);
                SessionState.EraseBool(ChalCatT4Key);
                SessionState.EraseBool(ChalCatKey);
                // 固着再現: **毎ラン進捗を出し、 毎ラン制御を返す**。 50 ラン刻みだと
                //   固着したランを 50 本の幅でしか特定できず、 25 ラン刻みの yield は
                //   エディタを 3 秒周期で無応答にして「重い」と「止まった」を混ぜる。
                if (runner.challengeCategorySweepOnlyCat >= 0)
                {
                    runner.challengeAxisSweepProgressEvery = 1;
                    runner.runsPerYield = 1;
                    SessionState.EraseInt(ChalCatOnlyKey);
                }
                if (SessionState.GetBool(ChalAxisChangedKey, false))
                {
                    runner.challengeAxisSweepAxisNames = new[]
                    {
                        "穴の空いた鞄", "遅い回復", "厚い皮膚", "狂暴化",
                        "通行料", "絶望的な戦闘", "天変地異"
                    };
                    SessionState.EraseBool(ChalAxisChangedKey);
                }
                SessionState.EraseBool(ChalAxisNoRelicKey);
                // 動作確認版: ログを残して少ランだけ。 本測定は進捗が一切出ないので先にこれを通す。
                if (chalAxisSmoke)
                {
                    runner.challengeAxisSweepRuns = 3;
                    runner.challengeAxisSweepMaxTierOnly = true;
                    runner.suppressLogsDuringBatch = false;
                    runner.clearConsoleAfterBatch = false;
                    // **速度設定は本測定と同じに保つ。** 200/1 まで落とすと 1 ラン 12 秒
                    // かかり、 動作確認のほうが本測定より遅いという本末転倒になる。
                }
                // 1ラン差分: ナラティブは Debug.Log をハンドラで拾って作るので、
                //   **ログ抑止を切らないと _detail が空になる**。 2 ランだけなので許容。
                // 価格弾性曲線: 挑戦は 0pt のまま倍率だけを振る。 基準 + 6 点 + ノイズ床。
                if (chalAxisPrice)
                    runner.shopPriceCurve = new float[] { 1.05f, 1.10f, 1.15f, 1.20f, 1.35f, 1.60f };
                if (chalAxisDiff)
                {
                    runner.challengeAxisSweepRuns = 1;
                    runner.suppressLogsDuringBatch = false;
                    runner.clearConsoleAfterBatch = false;
                    runner.writeDetailLog = true;
                }
            }
            runner.personaSweep = personaSweep;
            if (personaSweep)
            {
                runner.stepsPerYield = 1000; runner.runsPerYield = 25; runner.writeRunsJsonl = false;
                runner.personaSweepRuns = 500;
            }
            // 16 軸 × 300 = 4800 ラン。 jsonl を書くと肥大するので、 被ダメ集計に使う
            // _records は残しつつ書き出しだけ止める。
            if (axisSweep)
            {
                runner.stepsPerYield = 1000; runner.runsPerYield = 25;
                runner.relicAxisSweepRuns = 500;   // ペア比較なので 300 でも読めるが、不一致ペアを厚く取る
                // 診断は 2 アームだけなので jsonl を出せる (死因・死亡層・致死敵を追うため)。
                runner.writeRunsJsonl = axisDiag;
                runner.relicAxisSweepOnlyAxis =
                    axisDiag ? (int)MetaProgression.Relics.RelicAxis.CritMultPct : -1;
                // 2026-08-09: **挑戦 30pt で測る**。 高難易度限定軸 (Λ共鳴/渇き/刻限/背水) は
                //   25pt 以上でしか出ないので、 0pt の測定は「出ない軸を出た前提で測る」形だった。
                //   30pt は RelicBonusCap = 30 ＝ 遺物の見返りが頭打ちになる実用上限でもある。
                runner.relicAxisSweepChallengeScore = 30;
            }
            runner.autoLoopBatches = loopBatches;
            // 2026-07-15: ADR-0009 相互攻撃 + ビルド軸ペルソナ
            runner.useMutualAttackPipeline = EditorPrefs.GetBool(MutualKey, false);
            runner.rawTierRatio = EditorPrefs.GetInt(RatioKey, 50) / 100f;
            // 周回モード (遺物 §15-5)。 通常バッチの後段で 10 ペルソナぶんのラインが走る。
            if (SessionState.GetBool(AscendKey, false))
            {
                runner.ascensionMode = true;
                runner.ascensionRunsPerPersona = SessionState.GetInt(AscendRunsKey, 300);
                runner.stepsPerYield = 1000;
                runner.runsPerYield  = 25;
                runner.writeRunsJsonl = false;
                SessionState.EraseBool(AscendKey);
                SessionState.EraseInt(AscendRunsKey);
                Debug.Log($"[AutoRunMenu] 周回モード: 10ペルソナ × {runner.ascensionRunsPerPersona} ラン "
                        + $"= {runner.ascensionRunsPerPersona * 10} ラン / ポリシー {runner.ascensionPolicy}");
            }
            if (ultraStandard50WithRelic10K)
            {
                UltraAutoRunProfile profile = UltraAutoRunProfile.CreateStandard50WithRelic10000();
                profile.ApplyTo(runner);
                if (!profile.Matches(runner, out string profileError))
                {
                    Debug.LogError("[AutoRunMenu][Ultra] runtime profile validation failed: " + profileError);
                    EditorApplication.isPlaying = false;
                    return;
                }
            }

            // 診断: dispatch 経路に smoke sink を差す。 **既定では絶対に差さない**
            //   (差した状態で測ると基準値が壊れるため、 キーは 1 回で消費する)。
            // **撃つ決定点を rollout アームと揃える。** 揃えないと、 マップ選択の影響力を
            //   測っているつもりで「マクロ判断を全部潰した結果」を測ることになる
            //   ── 2026-08-21 に実際にやった (先頭手が全決定点を乗っ取り、 全ラン 1層で死亡)。
            //   rollout sink は AllowedPoints で MapNavigation のみに絞っているので、
            //   対照も同じ 1 点だけを差し替える。
            var smokeOnly = new[] { AutoTest.Ultra.UltraDecisionPoint.MapNavigation };
            if (ultraSmokeSink == 1)
            {
                runner.ultraSink = new AutoTest.Ultra.UltraFirstLegalSink { answerOnly = smokeOnly };
                Debug.LogWarning("[AutoRunMenu][Ultra] smoke sink = 先頭の合法手 (MapNavigation のみ) "
                    + "── **これは配管確認用で、 方策としては弱い**");
            }
            else if (ultraSmokeSink == 2)
            {
                runner.ultraSink = new AutoTest.Ultra.UltraLastLegalSink { answerOnly = smokeOnly };
                Debug.LogWarning("[AutoRunMenu][Ultra] smoke sink = 末尾の合法手 (MapNavigation のみ)");
            }
            if (ultraRolloutRuns > 0 && !TryInstallRolloutSink(runner, ultraRolloutPerAction))
            {
                EditorApplication.isPlaying = false;
                return;
            }
            // 階層DP航行 (④)。 **キーが無ければ 1 命令も変わらない** ── 既定 OFF を保つ。
            if (floorDpPct > 0)
            {
                runner.useFloorDpNavigation = true;
                runner.floorDpDiscount = floorDpPct / 100f;
                if (floorDpBands > 0) runner.floorDpHpBands = floorDpBands;
                Debug.LogWarning("[AutoRunMenu] 階層DP航行 ON / 割引 "
                    + runner.floorDpDiscount.ToString("F2")
                    + " / HP帯 " + runner.floorDpHpBands
                    + " ── **Super の航行方策が変わる。 既存の基準値と混ぜないこと**");
            }

            // 同点拡張。 **キーが無ければ 1 命令も変わらない** ── 既定 OFF を保つ。
            if (tieExpand > 0)
            {
                runner.superAI.tieExpandCandidates = tieExpand;
                Debug.LogWarning("[AutoRunMenu] 配線の同点拡張 ON / 候補枠 " + tieExpand
                    + " ── **Super の戦闘方策が変わる。 既存の基準値と混ぜないこと**");
            }

            // 飽和センサス。 **診断専用で判断を変えない** ので、 基準値はそのまま再現するはず。
            //   再現しなかったら計装が盤面へ漏れている ── それ自体が検査になる。
            if (satCensus)
            {
                runner.superAI.saturationCensus = true;
                Debug.LogWarning("[AutoRunMenu] 飽和センサス ON (診断専用・判断は変えない)");
            }

            // 充電の値付け (chargeRerollGain)。 **キーが無ければ既定 0.30 のまま。**
            if (rerollGainPct > 0)
            {
                runner.superAI.chargeRerollGain = rerollGainPct / 100f;
                Debug.LogWarning("[AutoRunMenu] chargeRerollGain = "
                    + runner.superAI.chargeRerollGain.ToString("F2")
                    + " (既定 0.30) ── **Super の戦闘方策が変わる。 既存の基準値と混ぜないこと**");
            }

            // 7層の役札持ち越し。 **2026-08-22 に既定 true で確定済み。**
            //   static なので前バッチから残る ── 毎回明示的に書き込むが、
            //   **キーが無いときは false ではなく「既定」を書く**。 false を書くと
            //   採用済みの仕様が黙って無効になる (null=「触らない」の取り違え)。
            CombatSystem.CombatManager.VescaKeepRolesAcrossPhases = !vescaKeepRolesOff;
            if (vescaKeepRolesOff)
                Debug.LogWarning("[AutoRunMenu] 7層 役札の連戦持ち越し **OFF** (旧仕様の対照アーム)");

            // リロールの全列挙。 **キーが無ければ従来の標本抽出のまま。**
            if (exactReroll > 0)
            {
                runner.superAI.exactRerollMaxDice = exactReroll;
                Debug.LogWarning("[AutoRunMenu] リロール全列挙 ON / 最大 " + exactReroll + " 個"
                    + " ── **Super の戦闘方策が変わる。 既存の基準値と混ぜないこと**");
            }

            // 残HP の値付け (winHpWeight)。 **キーが無ければ既定 0.5 のまま。**
            if (winHpPct > 0)
            {
                runner.superAI.winHpWeight = winHpPct / 100f;
                Debug.LogWarning("[AutoRunMenu] winHpWeight = "
                    + runner.superAI.winHpWeight.ToString("F2") + " (既定 0.50)"
                    + " ── **Super の戦闘方策が変わる。 既存の基準値と混ぜないこと**");
            }

            // ロールアウト内方策の ttk 価格。 **キーが無ければ既定 4.0 のまま。**
            if (ttkX10 > 0)
            {
                runner.superAI.rolloutTtkWeight = ttkX10 / 10f;
                Debug.LogWarning("[AutoRunMenu] rolloutTtkWeight = "
                    + runner.superAI.rolloutTtkWeight.ToString("F1") + " (既定 4.0)"
                    + " ── **全ロールアウトの向きが変わる。 既存の基準値と混ぜないこと**");
            }

            // マクロ判断のアブレーション。 **キーが無ければ 1 命令も変わらない。**
            if (ablateAxis > 0)
            {
                runner.ablateNavigation = ablateAxis == 1;
                runner.ablateShop       = ablateAxis == 2;
                runner.ablateEvent      = ablateAxis == 3;
                runner.ablateShopScore  = ablateAxis == 4;
                string axis = ablateAxis == 1 ? "航行"
                            : ablateAxis == 2 ? "ショップ購入 (作法ごと)"
                            : ablateAxis == 3 ? "イベント選択"
                            : "ショップの順位付けのみ";
                Debug.LogWarning("[AutoRunMenu] **アブレーション: " + axis + " を一様抽選へ潰す**"
                    + " ── 判断の価値を落ち幅で測る。 基準値ではない");
            }

            // 不偏 lift の混入。 **キーが無ければ K=0 で 1 品も触らない。**
            //   Reload より前に立てる必要がある (Reload の中で連続スコアが作られるため)。
            if (exploreK > 0)
            {
                AutoTest.LearnedPriorityProvider.ExploreBlendK = exploreK;
                Debug.LogWarning("[AutoRunMenu] 不偏lift混入 K=" + exploreK
                    + " ── **購入序列が変わる。 既存の基準値と混ぜないこと**");
            }

            // 7層の戦闘方策。 **既定 OFF (Super のまま)。**
            //   ttdCapTurns で根本を直したので、 Optimal へ逃がす対症療法は撤回済み。
            //   static ではないが毎回明示的に書く (前バッチの設定を持ち越さない)。
            runner.superLayer7OptimalCombatRoutine = layer7OptimalOn;
            if (layer7OptimalOn)
                Debug.LogWarning("[AutoRunMenu] 7層の戦闘方策を **Optimal へ切替** (撤回済み措置の対照アーム)");

            // ttd の頭打ち。 **キーが無ければ 0 = 従来どおり (999 で飽和)。**
            if (ttdCap > 0)
            {
                runner.superAI.ttdCapTurns = ttdCap;
                Debug.LogWarning("[AutoRunMenu] ttd 頭打ち = " + ttdCap + " ターン"
                    + " ── **StaticValue の飽和を止める。 既存の基準値と混ぜないこと**");
            }
            AutoTest.Ultra.UltraDispatchStats.Reset();

            // 国勢調査は **数えるだけ**。 一度で消費するキーにしてあるので、 次のバッチへ
            // 持ち越して基準値の測定に紛れ込むことはない。
            AutoTest.Ultra.UltraDecisionCensus.Reset();
            AutoTest.Ultra.UltraDecisionCensus.Enabled = ultraCensus;
            if (ultraCensus)
                Debug.Log("[AutoRunMenu][Ultra] マクロ決定の国勢調査 ON "
                    + "── 判断は変えない。 サマリ末尾に集計を出す");

            if (!string.IsNullOrEmpty(ultraCapturePath))
            {
                runner.ultraCaptureCheckpointPath = ultraCapturePath;
                Debug.Log("[AutoRunMenu][Ultra] checkpoint 採取モード → " + ultraCapturePath);
            }
            if (!string.IsNullOrEmpty(ultraResumePath))
            {
                try
                {
                    var payload = JsonUtility.FromJson<AutoTest.Ultra.UltraResumePayload>(
                        System.IO.File.ReadAllText(ultraResumePath, System.Text.Encoding.UTF8));
                    runner.ultraResumeFrom = payload;
                    // **強制手も worker と同じ形で入れる。** 入れずに再現すると
                    //   「Editor では動くのに worker では止まる」の差分が消えてしまう。
                    string forced = FirstMoveActionId(payload);
                    runner.ultraResumeForcedActionId = forced;
                    Debug.Log("[AutoRunMenu][Ultra] resume モード ← " + ultraResumePath
                        + "  現在地=" + payload.map.currentNodeId
                        + " ノード=" + payload.map.nodes.Length
                        + " 強制手='" + forced + "'");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("[AutoRunMenu][Ultra] checkpoint 読み込み失敗: " + ex.Message);
                    EditorApplication.isPlaying = false;
                    return;
                }
            }

            runner.Begin();
            string startLabel = sweep ? "5Fボス勝率スイープ"
                              : lambdaSweep ? "Λファーム量スイープ"
                              : loopBatches >= 2 ? $"自動周回 {count}ラン × {loopBatches}回"
                              : count + " ラン";
            Debug.Log($"[AutoRunMenu] AutoRunner 起動 ({startLabel}) ①{runner.metaBuffMode} ②{runner.itemPickMode} ③ボス{runner.tuneBosses}/Tier{runner.learnTier}/BOT{runner.learnBotAi} ④技量{runner.wiringSkill}");
        }
    }

    /// <summary>カスタム回数入力用の簡易モーダル。</summary>
    public class AutoRunCountWindow : EditorWindow
    {
        private int _value;
        private bool _done;
        private int _result;

        public static int Ask(int initial)
        {
            var w = CreateInstance<AutoRunCountWindow>();
            w._value = initial;
            w.titleContent = new GUIContent("AutoRun");
            w.position = new Rect(Screen.width / 2f, Screen.height / 2f, 260, 90);
            w.ShowModalUtility();
            return w._done ? w._result : 0;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("実行ラン数を入力");
            _value = EditorGUILayout.IntField("ラン数", Mathf.Max(1, _value));
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("実行")) { _result = _value; _done = true; Close(); }
                if (GUILayout.Button("キャンセル")) { _done = false; Close(); }
            }
        }
    }

    /// <summary>自動周回モード用: ラン数とバッチ数の同時入力ウィンドウ。</summary>
    public class AutoLoopConfigWindow : EditorWindow
    {
        private int _runs;
        private int _batches;
        private bool _done;
        private int _resultRuns;
        private int _resultBatches;

        public static (int runs, int batches) Ask(int initialRuns, int initialBatches)
        {
            var w = CreateInstance<AutoLoopConfigWindow>();
            w._runs = initialRuns;
            w._batches = initialBatches;
            w.titleContent = new GUIContent("AutoRun 自動周回");
            w.position = new Rect(Screen.width / 2f, Screen.height / 2f, 320, 140);
            w.ShowModalUtility();
            return w._done ? (w._resultRuns, w._resultBatches) : (0, 0);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("自動周回モード", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "指定ラン数 × 指定バッチ数を連続実行。\n" +
                "各バッチ間で L1 (アイテム勝率) と L2 (パラメータ) が自動学習される。\n" +
                "推奨: 1000ラン × 10-30回。",
                MessageType.Info);
            _runs    = EditorGUILayout.IntField("1バッチのラン数", Mathf.Max(1, _runs));
            _batches = EditorGUILayout.IntField("バッチ数 (周回回数)", Mathf.Max(1, _batches));
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("実行")) { _resultRuns = _runs; _resultBatches = _batches; _done = true; Close(); }
                if (GUILayout.Button("キャンセル")) { _done = false; Close(); }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// L1: アイテム勝率学習。
    ///
    /// バッチごとに RunRec を流し込むと、各 itemId について
    ///   ・acquired (取得した N ラン) のメトリクス平均
    ///   ・notAcquired (未取得 N ラン) のメトリクス平均
    /// を保持し、 lift = acq.bandScore − noAcq.bandScore を主軸スコアとして公開する。
    ///
    /// 永続化:
    ///   AutoRunLogs/learning/item_stats.json (累積)
    ///   バッチ folder の ai_stats.json (AI読みの差分・コンパクト形式)
    ///   summary.txt 末尾には人間向けの Top20 lift 表
    ///
    /// 算術注意: 取得 N が小さいと lift がノイズ。表示時は minN フィルタを掛ける。
    /// </summary>
    public static class ItemLearningStats
    {
        /// <summary>
        /// 集計除外リスト。
        /// チェーン進行アイテム（持ってる≠強い、進行度の代理変数）と
        /// デフォルト/初期装備（ほぼ全ランが持つ）。
        /// 除外しても累積カウンタは作るが Top/Bottom 表からは隠す。
        /// </summary>
        public static readonly HashSet<string> ExcludedFromLift = new HashSet<string>
        {
            // 確信チェーン (5F-6F の進路フラグ・アイテム)
            "真理", "決意", "根拠のない確信",
            "苦難の予言", "苦難の確信", "真理の予兆",
            // デフォルト/初期装備 (差をつけない)
            "dice_wood",
            // 起動でしか取れないイベント専用アイテム (買い目に影響しない)
            "ちいさな灯火",
            // フラグ系武器 (進路条件で配布、 lift評価対象外)
            "シュヴァリエのレイピア",
            // 武器階梯 T2/T3 (チェーン進化先の強さが lift に流れ込むため、 Tier評価から除外)
            //   **T1 は 2026-09-05 に廃止** (職業配布が T2 になった)。
            //   T2: 初期装備100%割当により多重共線性で regβ も歪む / T3: 進化下流で lift 膨張
            //   T4 (純・複合とも) は終端でチェーン流れ込み無しのため残す
            "鍛鉄の剣",   "銀の長剣",
            "鉄盾",  "聖騎士の盾",
            "猛斧",     "血塗りの戦斧",
            "盗賊の短刀",  "処刑人の曲刀",
        };

        /// <summary>**回帰には入れるが、準パワーの順位付けプールからは外す品**（2026-09-05）。
        ///
        /// <para>現状は進行武器の終端 (T4) 4 種。 <b>1 ランに武器は 1 本しかない</b>ため、
        /// 武器ダミーの「持たない側」は<b>そのチェーンを完走できなかったラン</b>になる ──
        /// 実測で 斧T3止まり の 7層クリアは 5.4%、 T4 到達で 66.0% (差 +60.6pt)。
        /// 一方パッシブの「持たない側」は「200 品のうちこの 1 品だけ買わなかった」で、
        /// 反実仮想の大きさが桁違いになる。 結果、 regβ の平均が
        /// <b>武器 +0.786 / パッシブ +0.128 (6 倍)</b> と系統的に膨らみ、
        /// regβ 主軸 (重み 0.55) の下では武器 4 品が上位 7 位を占めた
        /// ── これは強さではなく参照カテゴリの違いである。</para>
        ///
        /// <para><see cref="ExcludedFromLift"/> と違い<b>回帰の特徴量からは外さない</b>。
        /// 外すと「武器を育て切ったか」が残差へ落ち、 それと相関する品 (金回り系) の β を歪める
        /// ＝ 欠落変数バイアスになる。 <b>正規化プール (z-score の μ/σ) からだけ外し</b>、
        /// スコア自体は同じ μ/σ で算出して BOT の挙動は変えない。 表示は専用テーブルへ。</para></summary>
        public static readonly HashSet<string> RankedSeparately = new HashSet<string>
        {
            "デュランダル", "血帝廻天", "ノクタリア", "ドーンブリンガー",
        };

        /// <summary>削除済みアイテム: ゲームから完全に取り除かれたが累積 item_stats.json に残骸が残るもの。
        /// 表示・分類対象から完全除外する (ExcludedFromLift は表示はするが lift計算除外)。</summary>
        public static readonly HashSet<string> DeletedItems = new HashSet<string>
        {
            "invest_t2",        // 2026-05-31 削除 (投資武器系廃止)
            "invest_t3",        // 2026-05-31 削除 (投資武器系廃止)
            "invest_t4",        // 2026-05-31 削除 (投資武器系廃止)
            "dead_fang",        // 2026-05-30 削除 (E級致命罠)
            "disp_shield",      // 同上
            "disp_charm",       // 同上
            "disp_knife",       // 同上
            "賽の女神",         // 2026-05-30 削除 (リワーク失敗、 削除)
            "curse_t1",         // 2026-07-17 削除 (curse 系列全廃・自傷武器の役割を焦土契約/臨界に統合)
            "curse_t2",
            "curse_t3",
            "curse_t4",
            "dead_staff",       // 2026-07-17 削除 (Burn パッシブ廃止に伴い意味喪失)
            "古い歯車",         // 2026-07-17 削除 (フレーバー未実装)
            "怪しい水",         // 2026-07-17 削除 (フレーバー未実装)
            "uniq_earth_guard", // 2026-07-17 削除 (cons_reduce_3 と機構重複)
            "uniq_food_horn",   // 2026-07-17 削除 (cons_food_2/3 の中間値で冗長)
            "狂乱のメダリオン", // 2026-07-17 削除 (背水の狂刃と機構重複)
            // 2026-07-17 大掃除 (低使用ユニーク単独品 18品削除):
            "黒煙の符", "商人の符牒", "守護天使の鈴", "沈黙の剣帯", "蒼白の槍騎士", "飢餓丸",
            "双子の賽", "吸命の牙", "死神の数珠", "静寂のローブ", "食通の懐刀",
            "記憶の砂時計", "蒼穹の眼", "血路の旗", "嵐の徽章", "天梯", "停戦協定", "一点集中",
            // 複合武器 6 品 (2026-09-05 追加 → 2026-09-21 削除)。 純 T2〜T4 で足りると判断。
            //   **DeletedItems へ移すこと** ── 累積 item_stats.json には 250,000 ラン ぶんの
            //   残骸が残っているので、 外さないと存在しない武器が序列表に居座り続ける。
            "swordaxe_t4", "swordshield_t4", "sworddagger_t4",
            "axeshield_t4", "axedagger_t4", "shielddagger_t4",
        };

        /// <summary>1アイテム1行ぶんの累積カウンタ。
        /// 「全ラン」と「6F到達ラン限定」の2層で同じ集計を別バケットで持つことで、
        /// チェーン進行と弱相関の真の貢献を 6F 層別 lift で抽出する。</summary>
        [Serializable]
        public class ItemAggregate
        {
            public string id;
            // ---- 全ラン: 取得 ----
            public int acqRuns;
            public double acqBandScoreSum;
            public double acqDamageDealtSum;
            public double acqDamageTakenSum;
            public double acqHealedSum;
            public double acqShieldSum;
            public int acqFullClear;        // R11+R12
            public int acqReachedFloor7;    // 7F到達
            public int acqAwakenedFormsKilledSum; // ヴェスカ撃破段数の合計
            // ---- 全ラン: 未取得 ----
            public int noAcqRuns;
            public double noAcqBandScoreSum;
            public double noAcqDamageDealtSum;
            public double noAcqDamageTakenSum;
            public double noAcqHealedSum;
            public double noAcqShieldSum;
            public int noAcqFullClear;
            public int noAcqReachedFloor7;
            public int noAcqAwakenedFormsKilledSum;
            // ---- 6F到達ラン限定: 取得 ----
            public int acq6FRuns;
            public double acq6FBandScoreSum;
            public int acq6FFullClear;
            public int acq6FAwakenedFormsKilledSum;
            // ---- 6F到達ラン限定: 未取得 ----
            public int noAcq6FRuns;
            public double noAcq6FBandScoreSum;
            public int noAcq6FFullClear;
            public int noAcq6FAwakenedFormsKilledSum;
            // ---- per-floor 到達バケット (3F/4F/5F/7F: 取得/未取得の bandScore のみ集計) ----
            public int acq3FRuns; public double acq3FBandScoreSum;
            public int noAcq3FRuns; public double noAcq3FBandScoreSum;
            public int acq4FRuns; public double acq4FBandScoreSum;
            public int noAcq4FRuns; public double noAcq4FBandScoreSum;
            public int acq5FRuns; public double acq5FBandScoreSum;
            public int noAcq5FRuns; public double noAcq5FBandScoreSum;
            public int acq7FRuns; public double acq7FBandScoreSum;
            public int noAcq7FRuns; public double noAcq7FBandScoreSum;
            // ---- 出現lift用 (ショップで提示された/されなかった) ----
            // 出現バイアスを除去するために、取得有無を問わず「提示されたラン」だけで比較
            public int offRuns;        public double offBandSum;        // 全ラン: 提示された
            public int noOffRuns;      public double noOffBandSum;      // 全ラン: 提示されなかった
            public int off6FRuns;      public double off6FBandSum;      // 6F到達: 提示された
            public int noOff6F;        public double noOff6FBandSum;    // 6F到達: 提示されなかった
            public int offSqSum;       public double off6FSqSum;        // 分散計算用

            // ---- 探索 lift (ランダム化ホールドアウト・2026-08-17) ----
            // **上の lift は因果効果ではない。** BOT は金があり順調なときに買うので、
            // 取得したこと自体が「うまくいっている」の代理変数になっている。
            // ここは**ランダムに選ばれた決定だけ**を積む ── 候補集合の中では取得/未取得が
            // 期待値で等質なので、 差がそのまま因果効果になる。
            // 母集団は「探索の候補に入ったラン」で、 処置群は「探索で実際に選ばれたラン」。
            public int expAcqRuns;     public double expAcqBandSum;     // 探索で取得された
            public int expNoAcqRuns;   public double expNoAcqBandSum;   // 候補に入ったが選ばれなかった

            /// <summary>ランダム化決定だけから出した lift。 **これが不偏推定**。
            /// 両群とも 30 未満ならノイズ扱いにすること (母数が小さい)。</summary>
            public double ExploreLift => (expAcqRuns > 0 && expNoAcqRuns > 0)
                ? expAcqBandSum / expAcqRuns - expNoAcqBandSum / expNoAcqRuns : 0;

            public double AcqBandAvg     => acqRuns > 0 ? acqBandScoreSum / acqRuns : 0;
            public double NoAcqBandAvg   => noAcqRuns > 0 ? noAcqBandScoreSum / noAcqRuns : 0;
            public double Lift           => AcqBandAvg - NoAcqBandAvg;
            public double AcqClearRate   => acqRuns > 0 ? (double)acqFullClear / acqRuns : 0;
            public double NoAcqClearRate => noAcqRuns > 0 ? (double)noAcqFullClear / noAcqRuns : 0;
            public double ClearLift      => AcqClearRate - NoAcqClearRate;
            public double AcqDmgDealtAvg => acqRuns > 0 ? acqDamageDealtSum / acqRuns : 0;
            public double AcqHealedAvg   => acqRuns > 0 ? acqHealedSum / acqRuns : 0;

            // 6F層別: チェーン進行差を消したフェアな lift
            public double Acq6FBandAvg   => acq6FRuns > 0 ? acq6FBandScoreSum / acq6FRuns : 0;
            public double NoAcq6FBandAvg => noAcq6FRuns > 0 ? noAcq6FBandScoreSum / noAcq6FRuns : 0;
            public double Lift6F         => Acq6FBandAvg - NoAcq6FBandAvg;
            public double Acq6FClearRate => acq6FRuns > 0 ? (double)acq6FFullClear / acq6FRuns : 0;
            public double NoAcq6FClearRate => noAcq6FRuns > 0 ? (double)noAcq6FFullClear / noAcq6FRuns : 0;
            public double ClearLift6F    => Acq6FClearRate - NoAcq6FClearRate;
            public double Acq6FFormsAvg  => acq6FRuns > 0 ? (double)acq6FAwakenedFormsKilledSum / acq6FRuns : 0;
            public double NoAcq6FFormsAvg => noAcq6FRuns > 0 ? (double)noAcq6FAwakenedFormsKilledSum / noAcq6FRuns : 0;
            public double FormsLift6F    => Acq6FFormsAvg - NoAcq6FFormsAvg;

            // per-floor lift (シンプル: band 平均差分のみ)
            // 片群サンプルが MinFloorSample 未満なら lift=0 (= データ不足扱い)。
            // 旧仕様だと acq=0 で 0 - (noAcq 平均=10) = -10 等の極端値が出て分布を歪めた。
            public const int MinFloorSample = 20;
            public double Lift3F => (acq3FRuns < MinFloorSample || noAcq3FRuns < MinFloorSample) ? 0
                                  : (acq3FBandScoreSum / acq3FRuns - noAcq3FBandScoreSum / noAcq3FRuns);
            public double Lift4F => (acq4FRuns < MinFloorSample || noAcq4FRuns < MinFloorSample) ? 0
                                  : (acq4FBandScoreSum / acq4FRuns - noAcq4FBandScoreSum / noAcq4FRuns);
            public double Lift5F => (acq5FRuns < MinFloorSample || noAcq5FRuns < MinFloorSample) ? 0
                                  : (acq5FBandScoreSum / acq5FRuns - noAcq5FBandScoreSum / noAcq5FRuns);
            public double Lift7F => (acq7FRuns < MinFloorSample || noAcq7FRuns < MinFloorSample) ? 0
                                  : (acq7FBandScoreSum / acq7FRuns - noAcq7FBandScoreSum / noAcq7FRuns);

            // 出現lift (取得有無を問わず、 ショップで提示されたかどうかで層別)
            // 出現バイアスを除去できる: 強アイテムが「未提示=確率的にレア」なだけで弱判定されることを防ぐ
            public double OffBandAvg     => offRuns > 0 ? offBandSum / offRuns : 0;
            public double NoOffBandAvg   => noOffRuns > 0 ? noOffBandSum / noOffRuns : 0;
            public double OfferedLift    => OffBandAvg - NoOffBandAvg;
            public double Off6FBandAvg   => off6FRuns > 0 ? off6FBandSum / off6FRuns : 0;
            public double NoOff6FBandAvg => noOff6F > 0 ? noOff6FBandSum / noOff6F : 0;
            public double OfferedLift6F  => Off6FBandAvg - NoOff6FBandAvg;
        }

        /// <summary>シナジー探索（別枠）集計: グループのメンバー所持数 k 別に bandScore を積む。
        /// k=0..M（M=メンバー数）。 単体 lift では拾えない「セット相乗」を可視化するための単位。</summary>
        [Serializable]
        public class SynergyAggregate
        {
            public string id = "";
            public string name = "";
            public int members;                              // グループのメンバー数 M
            public List<int> kRuns = new List<int>();        // index k: メンバーをちょうど k 種取得したラン数（全ラン）
            public List<double> kBandSum = new List<double>();
            public List<int> k6FRuns = new List<int>();      // 同・6F到達ラン
            public List<double> k6FBandSum = new List<double>();

            /// <summary>k=0..members の長さを確保する。</summary>
            public void EnsureSize()
            {
                int need = members + 1;
                while (kRuns.Count < need) kRuns.Add(0);
                while (kBandSum.Count < need) kBandSum.Add(0);
                while (k6FRuns.Count < need) k6FRuns.Add(0);
                while (k6FBandSum.Count < need) k6FBandSum.Add(0);
            }
            public double KBandAvg(int k)   => (k >= 0 && k < kRuns.Count   && kRuns[k]   > 0) ? kBandSum[k]   / kRuns[k]   : 0;
            public double K6FBandAvg(int k) => (k >= 0 && k < k6FRuns.Count && k6FRuns[k] > 0) ? k6FBandSum[k] / k6FRuns[k] : 0;
        }

        /// <summary>1バッチ分のスナップショット (バッチ単位履歴で 直近N バッチを保持するための単位)。</summary>
        [Serializable]
        public class BatchSnapshot
        {
            public string timestamp = "";
            public int runs;
            public List<ItemAggregate> items = new List<ItemAggregate>();
            public List<SynergyAggregate> synergies = new List<SynergyAggregate>(); // シナジー探索（別枠）
        }

        [Serializable]
        public class StatsFile
        {
            public string updatedAt = "";
            public int totalBatches;                    // = batches.Count
            public int totalRuns;                       // = batches.Sum(b => b.runs)
            public List<ItemAggregate> items = new List<ItemAggregate>(); // 直近Nバッチを合算した表示用
            public List<BatchSnapshot> batches = new List<BatchSnapshot>(); // 直近Nバッチの履歴
            public List<SynergyAggregate> synergies = new List<SynergyAggregate>(); // 直近Nバッチ合算（シナジー探索表示用）
        }

        // ============================================================
        //  バッチ集計
        // ============================================================

        /// <summary>L1 更新に必要な最小バッチサイズ。 これ未満はノイズ過大としてスキップ。</summary>
        public const int MinBatchForL1 = 50;

        /// <summary>累積するバッチ数の上限 (これを超えた古いバッチは捨てる)。</summary>
        public const int MaxBatchesRetained = 50;

        /// <summary>src を dst に加算 (id は dst を優先)。</summary>
        private static void AddAggregate(ItemAggregate dst, ItemAggregate src)
        {
            dst.acqRuns                       += src.acqRuns;
            dst.acqBandScoreSum              += src.acqBandScoreSum;
            dst.acqDamageDealtSum            += src.acqDamageDealtSum;
            dst.acqDamageTakenSum            += src.acqDamageTakenSum;
            dst.acqHealedSum                 += src.acqHealedSum;
            dst.acqShieldSum                 += src.acqShieldSum;
            dst.acqFullClear                  += src.acqFullClear;
            dst.acqReachedFloor7              += src.acqReachedFloor7;
            dst.acqAwakenedFormsKilledSum     += src.acqAwakenedFormsKilledSum;
            dst.noAcqRuns                     += src.noAcqRuns;
            dst.noAcqBandScoreSum             += src.noAcqBandScoreSum;
            dst.noAcqDamageDealtSum           += src.noAcqDamageDealtSum;
            dst.noAcqDamageTakenSum           += src.noAcqDamageTakenSum;
            dst.noAcqHealedSum                += src.noAcqHealedSum;
            dst.noAcqShieldSum                += src.noAcqShieldSum;
            dst.noAcqFullClear                += src.noAcqFullClear;
            dst.noAcqReachedFloor7            += src.noAcqReachedFloor7;
            dst.noAcqAwakenedFormsKilledSum   += src.noAcqAwakenedFormsKilledSum;
            dst.acq6FRuns                     += src.acq6FRuns;
            dst.acq6FBandScoreSum             += src.acq6FBandScoreSum;
            dst.acq6FFullClear                += src.acq6FFullClear;
            dst.acq6FAwakenedFormsKilledSum   += src.acq6FAwakenedFormsKilledSum;
            dst.noAcq6FRuns                   += src.noAcq6FRuns;
            dst.noAcq6FBandScoreSum           += src.noAcq6FBandScoreSum;
            dst.noAcq6FFullClear              += src.noAcq6FFullClear;
            dst.noAcq6FAwakenedFormsKilledSum += src.noAcq6FAwakenedFormsKilledSum;
            dst.acq3FRuns += src.acq3FRuns; dst.acq3FBandScoreSum += src.acq3FBandScoreSum;
            dst.noAcq3FRuns += src.noAcq3FRuns; dst.noAcq3FBandScoreSum += src.noAcq3FBandScoreSum;
            dst.acq4FRuns += src.acq4FRuns; dst.acq4FBandScoreSum += src.acq4FBandScoreSum;
            dst.noAcq4FRuns += src.noAcq4FRuns; dst.noAcq4FBandScoreSum += src.noAcq4FBandScoreSum;
            dst.acq5FRuns += src.acq5FRuns; dst.acq5FBandScoreSum += src.acq5FBandScoreSum;
            dst.noAcq5FRuns += src.noAcq5FRuns; dst.noAcq5FBandScoreSum += src.noAcq5FBandScoreSum;
            dst.acq7FRuns += src.acq7FRuns; dst.acq7FBandScoreSum += src.acq7FBandScoreSum;
            dst.noAcq7FRuns += src.noAcq7FRuns; dst.noAcq7FBandScoreSum += src.noAcq7FBandScoreSum;
            dst.expAcqRuns   += src.expAcqRuns;   dst.expAcqBandSum   += src.expAcqBandSum;
            dst.expNoAcqRuns += src.expNoAcqRuns; dst.expNoAcqBandSum += src.expNoAcqBandSum;
            dst.offRuns += src.offRuns;     dst.offBandSum += src.offBandSum;
            dst.noOffRuns += src.noOffRuns; dst.noOffBandSum += src.noOffBandSum;
            dst.off6FRuns += src.off6FRuns; dst.off6FBandSum += src.off6FBandSum;
            dst.noOff6F += src.noOff6F;     dst.noOff6FBandSum += src.noOff6FBandSum;
            dst.offSqSum += src.offSqSum;   dst.off6FSqSum += src.off6FSqSum;
        }

        /// <summary>シナジー集計 src を dst に加算（k別バケットを足し合わせる）。</summary>
        private static void AddSynergy(SynergyAggregate dst, SynergyAggregate src)
        {
            if (src == null) return;
            dst.members = Math.Max(dst.members, src.members);
            if (!string.IsNullOrEmpty(src.name)) dst.name = src.name;
            dst.EnsureSize();
            for (int k = 0; k < src.kRuns.Count; k++)
            {
                while (dst.kRuns.Count <= k) { dst.kRuns.Add(0); dst.kBandSum.Add(0); dst.k6FRuns.Add(0); dst.k6FBandSum.Add(0); }
                dst.kRuns[k]    += src.kRuns[k];
                dst.kBandSum[k] += src.kBandSum[k];
                if (k < src.k6FRuns.Count)   dst.k6FRuns[k]    += src.k6FRuns[k];
                if (k < src.k6FBandSum.Count) dst.k6FBandSum[k] += src.k6FBandSum[k];
            }
        }

        /// <summary>batches 履歴から items (表示用累積) を再構築。</summary>
        private static void RebuildItemsFromBatches(StatsFile sf)
        {
            var merged = new Dictionary<string, ItemAggregate>();
            foreach (var b in sf.batches)
                foreach (var a in b.items)
                {
                    if (a == null || string.IsNullOrEmpty(a.id)) continue;
                    if (!merged.TryGetValue(a.id, out var dst))
                    {
                        dst = new ItemAggregate { id = a.id };
                        merged[a.id] = dst;
                    }
                    AddAggregate(dst, a);
                }
            sf.items = new List<ItemAggregate>(merged.Values);

            // シナジー探索（別枠）も同様に batches から再構築。
            var mergedSyn = new Dictionary<string, SynergyAggregate>();
            foreach (var b in sf.batches)
            {
                if (b.synergies == null) continue;
                foreach (var s in b.synergies)
                {
                    if (s == null || string.IsNullOrEmpty(s.id)) continue;
                    if (!mergedSyn.TryGetValue(s.id, out var dst))
                    {
                        dst = new SynergyAggregate { id = s.id, name = s.name, members = s.members };
                        dst.EnsureSize();
                        mergedSyn[s.id] = dst;
                    }
                    AddSynergy(dst, s);
                }
            }
            sf.synergies = new List<SynergyAggregate>(mergedSyn.Values);

            sf.totalBatches = sf.batches.Count;
            int rt = 0; foreach (var b in sf.batches) rt += b.runs;
            sf.totalRuns = rt;
        }

        /// <summary>1バッチ分の RunRec[] を投入。永続ファイルへマージし、累積を保存。
        /// バッチサイズが MinBatchForL1 未満ならスキップ（小さすぎる更新でデータを汚さない）。</summary>
        public static StatsFile IngestBatch(string learningRoot, IList<AutoRunner.RunRec> recs)
        {
            string path = Path.Combine(learningRoot, "item_stats.json");
            var sf = LoadOrNew(path);

            // 有効ラン数 (CRASH/DEADLOCK 除外) をカウント
            int validCount = 0;
            if (recs != null)
                foreach (var r in recs)
                    if (r != null && r.bandScore >= 0) validCount++;
            if (validCount < MinBatchForL1)
            {
                UnityEngine.Debug.Log($"[ItemLearningStats] サイズゲート: 有効{validCount}ラン < {MinBatchForL1} → L1更新スキップ");
                return sf;
            }

            // 全 itemId 抽出（このバッチで誰かが取得 or 提示されたもの）
            var allIds = new HashSet<string>();
            foreach (var r in recs)
            {
                if (r == null) continue;
                if (r.acquiredItemsEver != null) foreach (var id in r.acquiredItemsEver) allIds.Add(id);
                if (r.offeredItemsEver  != null) foreach (var id in r.offeredItemsEver)  allIds.Add(id);
            }

            // 新バッチ分のみの ItemAggregate を作成 (空から積み上げる)
            var byId = new Dictionary<string, ItemAggregate>(allIds.Count);
            foreach (var id in allIds)
                byId[id] = new ItemAggregate { id = id };

            // シナジー探索（別枠）: 既知グループのメンバー所持数 k 別に band を積む。
            var synById = new Dictionary<string, SynergyAggregate>();
            foreach (var g in SynergyGroups.All)
            {
                var sa = new SynergyAggregate { id = g.id, name = g.name, members = g.members.Length };
                sa.EnsureSize();
                synById[g.id] = sa;
            }

            foreach (var r in recs)
            {
                if (r == null) continue;
                // CRASH/DEADLOCK はノイズなのでスキップ
                if (r.bandScore < 0) continue;
                int dmgDealt = (int)r.totalDamageDealt;
                int dmgTaken = (int)r.totalDamageTaken;
                int healed   = (int)r.totalHealed;
                int shield   = (int)r.totalShieldGained;
                int forms    = r.awakenedFormsKilled != null ? r.awakenedFormsKilled.Count : 0;
                bool fullClear = r.bandScore >= 11; // R11
                bool reach7    = r.reachedFloor >= 7;
                bool reach6    = r.reachedFloor >= 6;
                bool reach3    = r.reachedFloor >= 3;
                bool reach4    = r.reachedFloor >= 4;
                bool reach5    = r.reachedFloor >= 5;

                var acquired = r.acquiredItemsEver ?? new HashSet<string>();
                var offered  = r.offeredItemsEver  ?? new HashSet<string>();
                var expOffered  = r.exploreOfferedEver  ?? new HashSet<string>();
                var expAcquired = r.exploreAcquiredEver ?? new HashSet<string>();

                // シナジー探索: グループごとに「取得したメンバー数 k」を数えて band を積む。
                foreach (var g in SynergyGroups.All)
                {
                    int k = 0;
                    foreach (var m in g.members) if (acquired.Contains(m)) k++;
                    var sa = synById[g.id];
                    sa.kRuns[k]++; sa.kBandSum[k] += r.bandScore;
                    if (reach6) { sa.k6FRuns[k]++; sa.k6FBandSum[k] += r.bandScore; }
                }

                // **探索ランは観測統計に入れない (2026-08-17b)。**
                //   探索で買った品が「取得」として混ざると、 貪欲方策の統計が汚れる。
                //   実測: band_0 に探索付き 10,000 ランを足しただけで 0pt クリア率が
                //   Optimal −3.1pt / Super −4.7pt 下がった (技量帯に関係なく下がったので原因は学習側)。
                //   ラン単位で分けているので、 探索ランは下の探索 lift だけに寄与する。
                bool greedyRun = !r.isExploreRun;

                foreach (var kv in byId)
                {
                    // ---- 出現lift: 提示有無で層別 (取得・未取得問わず) ----
                    bool wasOffered = offered.Contains(kv.Key) || acquired.Contains(kv.Key);
                    var ag = kv.Value;
                    if (greedyRun)
                    {
                    if (wasOffered) { ag.offRuns++; ag.offBandSum += r.bandScore; if (reach6) { ag.off6FRuns++; ag.off6FBandSum += r.bandScore; } }
                    else            { ag.noOffRuns++; ag.noOffBandSum += r.bandScore; if (reach6) { ag.noOff6F++;  ag.noOff6FBandSum += r.bandScore; } }
                    }

                    // ---- 探索 lift: **ランダム化された決定だけ**で層別 ----
                    //   母集団は「探索の候補に入った」ラン。 そこから一様に選ばれているので、
                    //   取得/未取得の差が交絡なしの因果効果になる。
                    if (expOffered.Contains(kv.Key))
                    {
                        if (expAcquired.Contains(kv.Key)) { ag.expAcqRuns++;   ag.expAcqBandSum   += r.bandScore; }
                        else                              { ag.expNoAcqRuns++; ag.expNoAcqBandSum += r.bandScore; }
                    }

                    // 探索ランは取得/未取得の層別にも入れない (上のコメント参照)。
                    if (!greedyRun) continue;

                    bool has = acquired.Contains(kv.Key);
                    var a = kv.Value;
                    if (has)
                    {
                        a.acqRuns++;
                        a.acqBandScoreSum += r.bandScore;
                        a.acqDamageDealtSum += dmgDealt;
                        a.acqDamageTakenSum += dmgTaken;
                        a.acqHealedSum += healed;
                        a.acqShieldSum += shield;
                        a.acqAwakenedFormsKilledSum += forms;
                        if (fullClear) a.acqFullClear++;
                        if (reach7)    a.acqReachedFloor7++;
                        // 6F層別
                        if (reach6)
                        {
                            a.acq6FRuns++;
                            a.acq6FBandScoreSum += r.bandScore;
                            a.acq6FAwakenedFormsKilledSum += forms;
                            if (fullClear) a.acq6FFullClear++;
                        }
                        // per-floor (band 平均のみ)
                        if (reach3) { a.acq3FRuns++; a.acq3FBandScoreSum += r.bandScore; }
                        if (reach4) { a.acq4FRuns++; a.acq4FBandScoreSum += r.bandScore; }
                        if (reach5) { a.acq5FRuns++; a.acq5FBandScoreSum += r.bandScore; }
                        if (reach7) { a.acq7FRuns++; a.acq7FBandScoreSum += r.bandScore; }
                    }
                    else
                    {
                        a.noAcqRuns++;
                        a.noAcqBandScoreSum += r.bandScore;
                        a.noAcqDamageDealtSum += dmgDealt;
                        a.noAcqDamageTakenSum += dmgTaken;
                        a.noAcqHealedSum += healed;
                        a.noAcqShieldSum += shield;
                        a.noAcqAwakenedFormsKilledSum += forms;
                        if (fullClear) a.noAcqFullClear++;
                        if (reach7)    a.noAcqReachedFloor7++;
                        // 6F層別
                        if (reach6)
                        {
                            a.noAcq6FRuns++;
                            a.noAcq6FBandScoreSum += r.bandScore;
                            a.noAcq6FAwakenedFormsKilledSum += forms;
                            if (fullClear) a.noAcq6FFullClear++;
                        }
                        if (reach3) { a.noAcq3FRuns++; a.noAcq3FBandScoreSum += r.bandScore; }
                        if (reach4) { a.noAcq4FRuns++; a.noAcq4FBandScoreSum += r.bandScore; }
                        if (reach5) { a.noAcq5FRuns++; a.noAcq5FBandScoreSum += r.bandScore; }
                        if (reach7) { a.noAcq7FRuns++; a.noAcq7FBandScoreSum += r.bandScore; }
                    }
                }
            }

            // バッチスナップショットに追加し、 上限超過分は古いものから捨てる
            var snap = new BatchSnapshot
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                runs = recs.Count,
                items = new List<ItemAggregate>(byId.Values),
                synergies = new List<SynergyAggregate>(synById.Values),
            };
            if (sf.batches == null) sf.batches = new List<BatchSnapshot>();
            sf.batches.Add(snap);
            while (sf.batches.Count > MaxBatchesRetained)
                sf.batches.RemoveAt(0);

            // items (表示用累積) を batches から再構築
            RebuildItemsFromBatches(sf);
            sf.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Directory.CreateDirectory(learningRoot);
            // Pretty Print オフでサイズ半減 (Unity JsonUtility は大きい JSON 苦手)
            File.WriteAllText(path, JsonUtility.ToJson(sf, false), new UTF8Encoding(false));

            // シナジー探索（別枠・任意ペア発掘）: 本体とは別ファイル synergy_pairs.json に累積。
            PairSynergyStats.Ingest(learningRoot, recs);

            return sf;
        }

        public static StatsFile LoadOrNew(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var txt = File.ReadAllText(path, Encoding.UTF8);
                    var sf = JsonUtility.FromJson<StatsFile>(txt);
                    if (sf != null) return sf;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[ItemLearningStats] load fail: {e.Message}"); }
            return new StatsFile();
        }

        // ============================================================
        //  出力（人間向け / AI 向け）
        // ============================================================

        /// <summary>人間向け Top-N lift 表（summary.txt 末尾）。
        /// 6F到達層別 lift を主軸に表示。全ラン lift は参考列。
        /// 除外リスト（チェーン進行/デフォルト装備）は表示しない。</summary>
        public static string BuildHumanLiftTable(StatsFile sf, int topN = 25, int minN = 8)
        {
            if (sf == null || sf.items == null || sf.items.Count == 0)
                return "(L1学習データなし)";
            var rows = new List<ItemAggregate>(sf.items);
            // 除外リスト & 最低N (6F層別)
            rows.RemoveAll(a => ExcludedFromLift.Contains(a.id) || a.acq6FRuns < minN || a.noAcq6FRuns < minN);
            rows.Sort((a, b) => b.Lift6F.CompareTo(a.Lift6F));

            var sb = new StringBuilder();
            sb.AppendLine($"---- L1学習: Top {topN} 6F層別lift (チェーン除外, minN={minN}) ----");
            sb.AppendLine($"  累積バッチ {sf.totalBatches} / 累積ラン {sf.totalRuns} / 更新 {sf.updatedAt}");
            sb.AppendLine($"  注: 6F到達ラン同士で比較し、チェーン進行差を相殺。lift6F が本当の貢献。");
            sb.AppendLine($"  offLift6F = 提示 vs 未提示 (出現バイアス除去)。差が大きいなら出現自体が勝率を変える。");
            sb.AppendLine($"  {Pad("ID",26)} {Pad("acq6F",6)} {Pad("lift6F",7)} {Pad("offL6F",7)} {Pad("clrΔ6F",7)} {Pad("formΔ",6)} {Pad("liftAll",7)}");
            int n = Math.Min(topN, rows.Count);
            for (int i = 0; i < n; i++)
            {
                var a = rows[i];
                sb.AppendLine($"  {Pad(a.id,26)} {PadR(a.acq6FRuns.ToString(),6)} " +
                              $"{PadR(a.Lift6F.ToString("F2"),7)} " +
                              $"{PadR(a.OfferedLift6F.ToString("F2"),7)} " +
                              $"{PadR(a.ClearLift6F.ToString("F3"),7)} " +
                              $"{PadR(a.FormsLift6F.ToString("F2"),6)} " +
                              $"{PadR(a.Lift.ToString("F2"),7)}");
            }
            sb.AppendLine();
            sb.AppendLine($"---- Bottom 10 6F層別lift (acq6Fラン同士で見ても弱い＝罠候補) ----");
            rows.Sort((a, b) => a.Lift6F.CompareTo(b.Lift6F));
            int m = Math.Min(10, rows.Count);
            for (int i = 0; i < m; i++)
            {
                var a = rows[i];
                sb.AppendLine($"  {Pad(a.id,26)} {PadR(a.acq6FRuns.ToString(),6)} " +
                              $"{PadR(a.Lift6F.ToString("F2"),7)}");
            }
            return sb.ToString();
        }

        /// <summary>AI 向けコンパクト形式。トークン圧縮のため
        ///   - キー名を1〜2文字に
        ///   - 配列で固定順
        ///   - 浮動小数は2-3桁に丸め
        /// 1ファイル＝そのバッチでの累積最新のスナップショット（差分でなく全量）。</summary>
        public static string BuildAiCompact(StatsFile sf, int minN = 5)
        {
            if (sf == null || sf.items == null) return "{}";
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"b\":").Append(sf.totalBatches).Append(",");
            sb.Append("\"r\":").Append(sf.totalRuns).Append(",");
            sb.Append("\"t\":\"").Append(sf.updatedAt).Append("\",");
            // キー説明: aN=acqN, nN=noAcqN, L=全ranlift, L6=6F層別lift, cL6=6F clearLift, fL6=6F formsLift,
            //           a6=acq6Fn, n6=noAcq6Fn, dD=与ダメ均, hl=回復均, sh=シールド均
            //           oL6=offeredLift6F (提示有vs無の bandScore 差), oN=offeredRuns6F
            sb.Append("\"k\":[\"id\",\"aN\",\"nN\",\"L\",\"a6\",\"n6\",\"L6\",\"cL6\",\"fL6\",\"dD\",\"hl\",\"sh\",\"oL6\",\"oN\",\"ex\"],");
            sb.Append("\"i\":[");
            bool first = true;
            // 6F層別lift 降順（除外品は ex=1 フラグで残すが末尾に）
            var rows = new List<ItemAggregate>(sf.items);
            rows.Sort((a, b) => b.Lift6F.CompareTo(a.Lift6F));
            foreach (var a in rows)
            {
                if (a.acqRuns < minN) continue;
                bool excluded = ExcludedFromLift.Contains(a.id);
                if (!first) sb.Append(",");
                first = false;
                sb.Append("[\"").Append(Esc(a.id)).Append("\",")
                  .Append(a.acqRuns).Append(",")
                  .Append(a.noAcqRuns).Append(",")
                  .Append(a.Lift.ToString("F2")).Append(",")
                  .Append(a.acq6FRuns).Append(",")
                  .Append(a.noAcq6FRuns).Append(",")
                  .Append(a.Lift6F.ToString("F2")).Append(",")
                  .Append(a.ClearLift6F.ToString("F3")).Append(",")
                  .Append(a.FormsLift6F.ToString("F2")).Append(",")
                  .Append(a.AcqDmgDealtAvg.ToString("F0")).Append(",")
                  .Append(a.AcqHealedAvg.ToString("F0")).Append(",")
                  .Append((a.acqRuns > 0 ? a.acqShieldSum / a.acqRuns : 0).ToString("F0")).Append(",")
                  .Append(a.OfferedLift6F.ToString("F2")).Append(",")
                  .Append(a.off6FRuns).Append(",")
                  .Append(excluded ? 1 : 0)
                  .Append("]");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ============================================================
        //  ヘルパ
        // ============================================================
        private static string Pad(string s, int n)
        {
            if (s == null) s = "";
            if (s.Length >= n) return s.Substring(0, n);
            return s + new string(' ', n - s.Length);
        }
        private static string PadR(string s, int n)
        {
            if (s == null) s = "";
            if (s.Length >= n) return s.Substring(0, n);
            return new string(' ', n - s.Length) + s;
        }
        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

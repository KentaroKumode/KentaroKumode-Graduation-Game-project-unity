using System.Collections.Generic;
using MetaProgression;

namespace AutoTest
{
    /// <summary>
    /// 整備パネル v6 の **実在する 36pt 配分** プリセット。 正本: docs/GAME.md §15-1。
    ///
    /// 背景 (2026-07-28):
    ///   AutoRunner の旧 <c>MetaPattern.FullProgression</c> は <c>MaxAllForTesting()</c> を呼び、
    ///   **予算 36pt に対して全 108 段を立てていた** (3 倍)。 旧 58 段トラックは「遊べば全解放」
    ///   モデルだったので正当だったが、 v6 の固定予算モデルへ置換した際に追従漏れした。
    ///   結果、 buffOn_* プロファイルで得た tier list と boss_tuning は
    ///   **存在しえないプレイヤー**を基準に較正されている。
    ///
    ///   枠総数 98 / 上限 33 = 34% ＝ **常に取捨選択**。 それを表現するのがこのプリセット群。
    ///   (2026-09-10: 精密トラック撤去で 108→98 枠 / 36→33pt。 詳細は MetaPanel.PrecisionCritRatePct)
    /// </summary>
    public static class MetaAllocationPresets
    {
        public enum Preset
        {
            /// <summary>0pt。 メタ未取得の新規プレイヤー＝バランスの床。</summary>
            None,
            /// <summary>予算を初めて使い切ったプレイヤーの標準形。**ボス調整の基準はこれ**。</summary>
            Balanced,
            /// <summary>純火力。 出力を上限まで積む。</summary>
            Offense,
            /// <summary>耐久。 外殻・防御を上限まで積む。</summary>
            Defense,
            /// <summary>経済。 メタで直接強くならず、 ランの中で買って強くなる経路。</summary>
            Economy,
            /// <summary>種火宣言 (出血)。 キーワードビルドが成立するかの検証用。</summary>
            SparkBuild,
        }

        /// <summary>プリセットの配分表。 値の合計は必ず <see cref="MetaPanel.MaxPoints"/> (24)。</summary>
        public static Dictionary<MetaPanelKind, int> Ranks(Preset p)
        {
            switch (p)
            {
                // 2026-09-12: 予算 24pt。 **数値系 8 本へ 3pt ずつの完全均等**。
                //   旧 Balanced は強奪/商才を 0 に置いていたため、 その 2 本を焦点にした
                //   極点アームだけが 10pt 全額を新規に払い、 犠牲が不均一になっていた。
                //   3×8 = 24
                case Preset.Balanced:
                    return new Dictionary<MetaPanelKind, int> {
                    { MetaPanelKind.Shell, 3 }, { MetaPanelKind.Output, 3 }, { MetaPanelKind.Guard, 3 },
                    { MetaPanelKind.Vault, 3 }, { MetaPanelKind.Plunder, 3 }, { MetaPanelKind.Trade, 3 },
                    { MetaPanelKind.Supply, 3 }, { MetaPanelKind.Lantern, 3 },
                };

                // 10+5+4+3+2 = 24
                case Preset.Offense: return new Dictionary<MetaPanelKind, int> {
                    { MetaPanelKind.Output, 10 }, { MetaPanelKind.Shell, 5 }, { MetaPanelKind.Guard, 4 },
                    { MetaPanelKind.Vault, 3 }, { MetaPanelKind.Supply, 2 },
                };

                // 10+8+3+3 = 24
                case Preset.Defense: return new Dictionary<MetaPanelKind, int> {
                    { MetaPanelKind.Shell, 10 }, { MetaPanelKind.Guard, 8 },
                    { MetaPanelKind.Output, 3 }, { MetaPanelKind.Supply, 3 },
                };

                // 10+7+7 = 24
                case Preset.Economy: return new Dictionary<MetaPanelKind, int> {
                    { MetaPanelKind.Vault, 10 }, { MetaPanelKind.Plunder, 7 }, { MetaPanelKind.Trade, 7 },
                };

                // 種火は宣言系なので 3pt/段。 SparkBleed3 = 9pt。 9+5+5+5 = 24
                case Preset.SparkBuild: return new Dictionary<MetaPanelKind, int> {
                    { MetaPanelKind.SparkBleed, 3 }, { MetaPanelKind.Shell, 5 },
                    { MetaPanelKind.Output, 5 }, { MetaPanelKind.Guard, 5 },
                };

                default: return new Dictionary<MetaPanelKind, int>();
            }
        }

        /// <summary>配分の合計 <b>pt</b> (段数ではない)。 検証用。</summary>
        public static int TotalPoints(Preset p)
        {
            int t = 0;
            foreach (var kv in Ranks(p)) t += MetaPanel.CostOf(kv.Key, kv.Value);
            return t;
        }

        /// <summary>プリセットをメタ状態へ適用する。 予算超過は例外ではなく警告＋切り詰めで扱う
        /// (配分表の書き換えミスを黙って通さないため)。</summary>
        public static void Apply(Preset p)
        {
            var mgr = MetaProgressManager.Instance;
            if (mgr == null) return;

            mgr.ResetAll();
            if (p == Preset.None) return;

            ApplyRanks(Ranks(p), p.ToString());
        }

        /// <summary><b>任意の配分を適用する (2026-09-10)。</b> <see cref="Apply"/> の本体を切り出したもの。
        ///
        /// <para><b>なぜ要るか。</b> 従来はプリセット単位でしか配れず、
        /// 「Balanced から 1 軸だけ抜く」ができなかった。 整備パネルはオッズ比 14〜21 と
        /// 全軸で最大の効果を持つのに、 <b>どの軸が効いているのかを誰も測っていない</b> ──
        /// 効果量の内訳が無いまま「強すぎる/妥当」を論じることになる。
        /// drop-one アブレーション用の注入口。</para>
        ///
        /// <para><b>ResetAll は呼ばない</b> ── 呼び出し側 (<see cref="Apply"/>) が既に呼んでいる。
        /// 単独で使うときは先に <c>mgr.ResetAll()</c> すること。</para></summary>
        public static void ApplyRanks(Dictionary<MetaPanelKind, int> ranks, string label)
        {
            var mgr = MetaProgressManager.Instance;
            if (mgr == null || ranks == null) return;

            int total = 0;
            foreach (var kv in ranks) total += MetaPanel.CostOf(kv.Key, kv.Value);
            if (total > MetaPanel.MaxPoints)
                UnityEngine.Debug.LogError(
                    $"[MetaAllocationPresets] {label} の配分が予算超過 ({total} > {MetaPanel.MaxPoints})。");

            var st = mgr.State;
            if (st == null) return;
            st.EnsurePanelInitialized();
            st.panelPointsPurchased = System.Math.Min(total, MetaPanel.MaxPoints);

            int spent = 0;
            foreach (var kv in ranks)
            {
                if (kv.Value <= 0) continue;
                int unit = MetaPanel.RankCost(kv.Key);
                int room = st.panelPointsPurchased - spent;
                if (room < unit) continue;
                int r = System.Math.Min(kv.Value, room / unit);
                st.SetRank(kv.Key, r);
                spent += r * unit;
            }

            UnityEngine.Debug.Log($"[MetaAllocationPresets] {label} を適用: {spent}/{MetaPanel.MaxPoints}pt");
        }

        /// <summary><c>"Shell:6,Output:0,Guard:5"</c> 形式を配分表へ。 未記載の軸は 0。
        /// 解釈できない軸名は無視して警告する (黙って 0 にすると条件を取り違える)。</summary>
        public static Dictionary<MetaPanelKind, int> ParseSpec(string spec)
        {
            var d = new Dictionary<MetaPanelKind, int>();
            if (string.IsNullOrWhiteSpace(spec)) return d;
            foreach (var tok in spec.Split(','))
            {
                var kv = tok.Split(':');
                if (kv.Length != 2) continue;
                if (!System.Enum.TryParse(kv[0].Trim(), true, out MetaPanelKind kind))
                { UnityEngine.Debug.LogWarning($"[MetaAllocationPresets] 未知の軸名: {kv[0]}"); continue; }
                if (int.TryParse(kv[1].Trim(), out int r) && r > 0) d[kind] = r;
            }
            return d;
        }
    }
}
